using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using RAWSimO.Core.IO;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.Core.Metrics;
using RAWSimO.Toolbox;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static RAWSimO.Core.Control.BotManager;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// ALNS (Adaptive Large Neighborhood Search) order-batching manager.
    ///
    /// Sits between M1G (exact Gurobi MILP — optimal but not scalable) and HADGS (one-pod-at-a-time
    /// greedy — fast but myopic). Each decision epoch it builds a HADGS-style greedy warm-start, then
    /// improves it with destroy/repair + simulated-annealing acceptance under a per-epoch time budget,
    /// optimizing the SAME objective M1G defines:
    ///     minimize  w1·Σ travel(bot→pod + pod→station)  −  OrderReward·(#orders covered)  +  w3·(empty station slots)
    /// (M1G weights w1=1, w2=−40 → OrderReward=40, w3=1000; see <see cref="M1GManager"/>.)
    ///
    /// Decision unit = a "cover move": assign one order o to a station s using a minimal pod cover-set
    /// (carried by nearest free bots). This makes coverage increment by whole orders (never stalls on
    /// multi-pod orders the way a single-pod greedy would), and maps directly to M1G's yos+xps+dops.
    /// Inbound pods already at a station (Pb) are fixed and contribute to coverage but are never reassigned.
    ///
    /// Reuses HADGS helpers (EstimateBotPodDistance/EstimatePodStationDistance, GenerateAvailableBots,
    /// AnyRelevantRequests1, GetPossibleRequests, AllocateOrder) and commits through the identical API
    /// (RegisterInboundPod / ClaimPod / BottoPod / _Ziops1 / AllocateOrder).
    /// </summary>
    public class ALNSManager : HADGSManager
    {
        private readonly ALNSConfiguration _alns;

        public ALNSManager(Instance instance) : base(instance)
        {
            _alns = instance.ControllerConfig.OrderBatchingConfig as ALNSConfiguration;
        }

        // ───────────────────────── per-epoch context (rebuilt each decision) ─────────────────────────
        private List<OutputStation> _capStations;                 // stations with free capacity this epoch
        private Dictionary<OutputStation, int> _capLeft;          // Cs[s] snapshot
        private List<Order> _orderPool;                           // candidate pending orders (urgency-ordered)
        private List<Pod> _podPool;                               // candidate free pods (Pa)
        private List<Bot> _botPool;                               // candidate free bots (Ra)
        private double _w1, _orderReward, _w3;

        /// <summary>A single cover move: order o served at station s by Pods (each carried by PodBot[pod]).</summary>
        private class Move
        {
            public Order Order;
            public OutputStation Station;
            public List<Pod> Pods;
            public Dictionary<Pod, Bot> PodBot;
            public double Travel;
        }

        /// <summary>An in-memory candidate solution (no simulator state touched until commit).</summary>
        private class Sol
        {
            public List<Move> Moves = new List<Move>();
            public HashSet<Pod> UsedPods = new HashSet<Pod>();
            public HashSet<Bot> UsedBots = new HashSet<Bot>();
            public HashSet<Order> UsedOrders = new HashSet<Order>();
            public Dictionary<OutputStation, int> OrdersAt = new Dictionary<OutputStation, int>();

            public Sol Clone()
            {
                var c = new Sol
                {
                    UsedPods = new HashSet<Pod>(UsedPods),
                    UsedBots = new HashSet<Bot>(UsedBots),
                    UsedOrders = new HashSet<Order>(UsedOrders),
                    OrdersAt = new Dictionary<OutputStation, int>(OrdersAt),
                };
                foreach (var m in Moves)
                    c.Moves.Add(new Move { Order = m.Order, Station = m.Station, Pods = new List<Pod>(m.Pods), PodBot = new Dictionary<Pod, Bot>(m.PodBot), Travel = m.Travel });
                return c;
            }
        }

        protected override void DecideAboutPendingOrders()
        {
            PrepareDecisionExtras(); // SA hooks etc.; base no-op for plain ALNS

            // Rebuild fixed inbound set (Pb) from each station's currently-registered inbound pods,
            // mirroring HADGSManager.DecideAboutPendingOrders (release a stray resting pod if present).
            _inboundPodsPerStation.Clear();
            foreach (var s in Instance.OutputStations)
            {
                _inboundPodsPerStation[s] = new HashSet<Pod>(s.InboundPods);
                foreach (var pod in s.InboundPods)
                {
                    if (!Instance.ResourceManager.BottoPod.ContainsValue(pod) &&
                        Instance.ResourceManager._usedPods.ContainsKey(pod) &&
                        Instance.ResourceManager._usedPods[pod].CurrentTask is RestTask)
                    {
                        _inboundPodsPerStation[s].Remove(pod);
                        Instance.ResourceManager.ReleasePod(pod);
                        s.UnregisterInboundPod(pod);
                        break;
                    }
                }
            }

            // Objective weights (default aligned with M1G: 1 / 40 / 1000).
            _w1 = _alns != null ? _alns.ObjW1 : 1.0;
            _orderReward = _alns != null ? _alns.ObjOrderReward : 40.0;
            _w3 = _alns != null ? _alns.ObjW3 : 1000.0;

            // Decision domain.
            _capStations = Instance.OutputStations.Where(s => s.Active && Cs.ContainsKey(s) && Cs[s] > 0)
                                                  .OrderBy(s => s.ID).ToList();
            _capLeft = _capStations.ToDictionary(s => s, s => Cs[s]);
            if (_capStations.Count == 0)
                return;

            _botPool = GenerateAvailableBots().OrderBy(b => b.ID).ToList();
            if (_botPool.Count == 0)
                return;

            _podPool = Instance.ResourceManager.UnusedPods
                .Where(p => p.Waypoint != null && AnyRelevantRequests1(p) && !Instance.ResourceManager.BottoPod.ContainsValue(p))
                .OrderBy(p => p.ID).ToList();
            if (_podPool.Count == 0)
                return;

            int maxOrders = (_alns != null && _alns.MaxOrdersConsidered > 0) ? _alns.MaxOrdersConsidered : int.MaxValue;
            _orderPool = _pendingOrders
                .OrderBy(o => o.DueTime).ThenBy(o => o.TimeStamp).ThenBy(o => o.ID)
                .Take(maxOrders).ToList();
            if (_orderPool.Count == 0)
                return;

            // Warm-start (greedy) then ALNS improvement under the time budget.
            Sol best = FillGreedy(new Sol(), noisy: false);
            best = RunALNS(best);

            Commit(best);
        }

        // ───────────────────────── ALNS loop ─────────────────────────
        private Sol RunALNS(Sol warm)
        {
            int budgetMs = _alns != null ? _alns.TimeBudgetMs : 30;
            int maxIter = _alns != null ? _alns.MaxIterations : 2000;
            if (budgetMs <= 0 || maxIter <= 0)
                return warm; // sanity mode: pure greedy warm-start

            double T = _alns != null ? Math.Max(1e-6, _alns.InitialTemperature) : 50.0;
            double cooling = _alns != null ? _alns.CoolingRate : 0.999;
            double qMin = _alns != null ? _alns.DestroyMinFraction : 0.15;
            double qMax = _alns != null ? _alns.DestroyMaxFraction : 0.4;

            Sol x = warm, xbest = warm.Clone();
            double fx = Objective(x), fbest = fx;

            // adaptive operator weights: 2 destroy × 2 repair
            double[] dW = { 1.0, 1.0 }, rW = { 1.0, 1.0 };

            var sw = Stopwatch.StartNew();
            int iter = 0;
            while (iter < maxIter && sw.ElapsedMilliseconds < budgetMs)
            {
                int di = Roulette(dW), ri = Roulette(rW);
                Sol y = x.Clone();
                int q = Math.Max(1, (int)Math.Round((qMin + Instance.Randomizer.NextDouble() * (qMax - qMin)) * Math.Max(1, y.Moves.Count)));
                if (di == 0) DestroyRandom(y, q); else DestroyWorst(y, q);
                FillGreedy(y, noisy: ri == 1);

                double fy = Objective(y);
                double reward;
                if (fy < fbest - 1e-9) { xbest = y.Clone(); fbest = fy; x = y; fx = fy; reward = 3.0; }
                else if (fy < fx - 1e-9) { x = y; fx = fy; reward = 2.0; }
                else if (Instance.Randomizer.NextDouble() < Math.Exp(-(fy - fx) / T)) { x = y; fx = fy; reward = 1.0; }
                else reward = 0.0;

                // EWMA weight update
                dW[di] = 0.9 * dW[di] + 0.1 * reward;
                rW[ri] = 0.9 * rW[ri] + 0.1 * reward;
                if (dW[di] < 0.05) dW[di] = 0.05;
                if (rW[ri] < 0.05) rW[ri] = 0.05;

                T *= cooling;
                iter++;
            }
            return xbest;
        }

        private int Roulette(double[] w)
        {
            double sum = w.Sum(), r = Instance.Randomizer.NextDouble() * sum, acc = 0;
            for (int i = 0; i < w.Length; i++) { acc += w[i]; if (r <= acc) return i; }
            return w.Length - 1;
        }

        // ───────────────────────── destroy operators ─────────────────────────
        private void DestroyRandom(Sol sol, int q)
        {
            for (int k = 0; k < q && sol.Moves.Count > 0; k++)
                RemoveMove(sol, Instance.Randomizer.NextInt(sol.Moves.Count));
        }

        private void DestroyWorst(Sol sol, int q)
        {
            for (int k = 0; k < q && sol.Moves.Count > 0; k++)
            {
                int worst = 0; double worstVal = double.MinValue;
                for (int i = 0; i < sol.Moves.Count; i++)
                    if (sol.Moves[i].Travel > worstVal) { worstVal = sol.Moves[i].Travel; worst = i; }
                RemoveMove(sol, worst);
            }
        }

        private void RemoveMove(Sol sol, int idx)
        {
            var m = sol.Moves[idx];
            sol.Moves.RemoveAt(idx);
            sol.UsedOrders.Remove(m.Order);
            sol.OrdersAt[m.Station] = sol.OrdersAt.TryGetValue(m.Station, out int c) ? c - 1 : 0;
            foreach (var p in m.Pods) sol.UsedPods.Remove(p);
            foreach (var b in m.PodBot.Values) sol.UsedBots.Remove(b);
        }

        // ───────────────────────── repair / greedy construction ─────────────────────────
        /// <summary>Greedily insert cover-moves until no improving (negative-delta) move remains.
        /// noisy=true randomizes ties / occasionally takes a non-best feasible move for diversification.</summary>
        private Sol FillGreedy(Sol sol, bool noisy)
        {
            var residual = BuildResidual(sol);

            while (true)
            {
                Move best = null; double bestDelta = -1e-9; OutputStation bestStation = null;

                foreach (var s in _capStations)
                {
                    if (sol.OrdersAt.TryGetValue(s, out int used) && used >= _capLeft[s]) continue;
                    foreach (var o in _orderPool)
                    {
                        if (sol.UsedOrders.Contains(o)) continue;
                        var mv = TryCover(o, s, residual[s], sol.UsedPods, sol.UsedBots);
                        if (mv == null) continue;
                        // delta: fill one order ⇒ −OrderReward (coverage) − w3 (one fewer empty slot) + w1·travel
                        double delta = _w1 * mv.Travel - _orderReward - _w3;
                        if (delta < bestDelta || (noisy && delta < -1e-9 && Instance.Randomizer.NextDouble() < 0.15))
                        {
                            best = mv; bestDelta = delta; bestStation = s;
                            if (noisy) break; // take an early improving move for diversity
                        }
                    }
                    if (noisy && best != null) break;
                }

                if (best == null) break;

                // apply
                sol.Moves.Add(best);
                sol.UsedOrders.Add(best.Order);
                sol.OrdersAt[bestStation] = sol.OrdersAt.TryGetValue(bestStation, out int c) ? c + 1 : 1;
                foreach (var p in best.Pods) sol.UsedPods.Add(p);
                foreach (var b in best.PodBot.Values) sol.UsedBots.Add(b);
                // update station residual: add cover-set stock, then consume the order's demand
                var res = residual[bestStation];
                foreach (var p in best.Pods)
                    foreach (var it in p.ItemDescriptionsContained)
                        res[it] = (res.TryGetValue(it, out int v) ? v : 0) + p.CountAvailable(it);
                foreach (var pos in best.Order.Positions)
                    res[pos.Key] = (res.TryGetValue(pos.Key, out int v2) ? v2 : 0) - pos.Value;
            }
            return sol;
        }

        /// <summary>Per-station residual SKU inventory = (inbound Pb ∪ pods already assigned in sol) − orders already assigned.</summary>
        private Dictionary<OutputStation, Dictionary<ItemDescription, int>> BuildResidual(Sol sol)
        {
            var residual = new Dictionary<OutputStation, Dictionary<ItemDescription, int>>();
            foreach (var s in _capStations)
            {
                var avail = new Dictionary<ItemDescription, int>();
                foreach (var pod in _inboundPodsPerStation[s])
                    foreach (var it in pod.ItemDescriptionsContained)
                        avail[it] = (avail.TryGetValue(it, out int v) ? v : 0) + pod.CountAvailable(it);
                residual[s] = avail;
            }
            foreach (var m in sol.Moves)
            {
                var res = residual[m.Station];
                foreach (var p in m.Pods)
                    foreach (var it in p.ItemDescriptionsContained)
                        res[it] = (res.TryGetValue(it, out int v) ? v : 0) + p.CountAvailable(it);
                foreach (var pos in m.Order.Positions)
                    res[pos.Key] = (res.TryGetValue(pos.Key, out int v2) ? v2 : 0) - pos.Value;
            }
            return residual;
        }

        /// <summary>Try to fully cover order o at station s. Returns a Move (minimal extra cover-set + bot assignment)
        /// or null if infeasible with the currently-available pods/bots.</summary>
        private Move TryCover(Order o, OutputStation s, Dictionary<ItemDescription, int> residualS, HashSet<Pod> usedPods, HashSet<Bot> usedBots)
        {
            // running availability at s = residual snapshot + stock of pods we add
            var have = new Dictionary<ItemDescription, int>(residualS);
            var extra = new List<Pod>();

            foreach (var pos in o.Positions)
            {
                var item = pos.Key; int qty = pos.Value;
                int cur = have.TryGetValue(item, out int v) ? v : 0;
                while (cur < qty)
                {
                    Pod pick = null; double pickDist = double.MaxValue;
                    foreach (var p in _podPool)
                    {
                        if (usedPods.Contains(p) || extra.Contains(p)) continue;
                        if (p.CountAvailable(item) <= 0) continue;
                        double d = EstimatePodStationDistance(p, s);
                        if (d < pickDist) { pickDist = d; pick = p; }
                    }
                    if (pick == null) return null; // SKU cannot be satisfied
                    extra.Add(pick);
                    foreach (var it in pick.ItemDescriptionsContained)
                        have[it] = (have.TryGetValue(it, out int hv) ? hv : 0) + pick.CountAvailable(it);
                    cur = have[item];
                }
            }

            // assign nearest free bot to each extra pod
            var podBot = new Dictionary<Pod, Bot>();
            var localUsedBots = new HashSet<Bot>(usedBots);
            double travel = 0.0;
            foreach (var p in extra)
            {
                Bot pick = null; double pickD = double.MaxValue;
                foreach (var b in _botPool)
                {
                    if (localUsedBots.Contains(b)) continue;
                    double d = EstimateBotPodDistance(b, p);
                    if (d < pickD) { pickD = d; pick = b; }
                }
                if (pick == null) return null; // not enough bots
                podBot[p] = pick;
                localUsedBots.Add(pick);
                travel += pickD + EstimatePodStationDistance(p, s);
            }

            return new Move { Order = o, Station = s, Pods = extra, PodBot = podBot, Travel = travel };
        }

        // ───────────────────────── objective ─────────────────────────
        private double Objective(Sol sol)
        {
            double travel = 0.0;
            foreach (var m in sol.Moves) travel += m.Travel;
            int covered = sol.Moves.Count;
            int emptySlots = 0;
            foreach (var s in _capStations)
                emptySlots += Math.Max(0, _capLeft[s] - (sol.OrdersAt.TryGetValue(s, out int c) ? c : 0));
            return _w1 * travel - _orderReward * covered + _w3 * emptySlots;
        }

        // ───────────────────────── commit (HADGS-identical API) ─────────────────────────
        private void Commit(Sol sol)
        {
            // 1) attach pods to stations / claim bots
            foreach (var m in sol.Moves)
            {
                foreach (var p in m.Pods)
                {
                    var b = m.PodBot[p];
                    if (Instance.ResourceManager.BottoPod.ContainsKey(b) || Instance.ResourceManager.BottoPod.ContainsValue(p))
                        continue; // safety: never double-claim
                    _inboundPodsPerStation[m.Station].Add(p);
                    m.Station.RegisterInboundPod(p);
                    Instance.ResourceManager.BottoPod.Add(b, p);
                    Instance.ResourceManager.ClaimPod(p, b, BotTaskType.Extract);
                }
            }

            // 2) allocate orders and register item picks (water-fill marking), mirroring HADGS Phase A
            foreach (var m in sol.Moves)
            {
                var s = m.Station; var o = m.Order;
                AllocateOrder(o, s);
                var itemDemands = new HashSet<ExtractRequest>(Instance.ResourceManager.GetExtractRequestsOfOrder(o));
                foreach (var pod in _inboundPodsPerStation[s].OrderBy(v => EstimatePodStationDistance(v, s)))
                {
                    if (!itemDemands.Any(g => pod.IsAvailable(g.Item))) continue;
                    List<ExtractRequest> fitting = GetPossibleRequests(pod, itemDemands);
                    foreach (var fr in fitting) itemDemands.Remove(fr);
                    foreach (var fr in fitting) pod.JustRegisterItem(fr.Item);
                    if (Instance.ResourceManager._Ziops1[s].ContainsKey(pod))
                        Instance.ResourceManager._Ziops1[s][pod].AddRange(fitting);
                    else
                        Instance.ResourceManager._Ziops1[s].Add(pod, fitting);
                }
            }
        }
    }
}
