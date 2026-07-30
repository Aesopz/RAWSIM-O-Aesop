using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.SolverWrappers;
using static RAWSimO.Core.Management.ResourceManager;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// M4G - unit-level order splitting with M1G's valuation/binding separation restored.
    ///
    /// M1G keeps two order-assignment layers: yos (slot-free, rewarded in the objective,
    /// a pod valuation) and yaos (slot-limited, the only one actually bound). M3G collapsed
    /// them, so every scored unit had to occupy a currently free slot; splitting then only
    /// arose as a feasibility residual forced by the per-station new-pod cap. M4G restores
    /// the separation at unit level: q-hat values pods against the whole backlog, q binds
    /// only what fits the slots, and only q has side effects.
    ///
    /// Spec: docs/superpowers/specs/2026-07-31-m4g-valuation-binding-design.md
    /// Constitution: M1GManager / HADGSManager / SplitM2eICManager are never modified;
    /// this class mirrors what it needs rather than reaching into them.
    /// </summary>
    public class M4GManager : M1GManager
    {
        /// <summary>Creates a new instance of this manager.</summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public M4GManager(Instance instance) : base(instance)
        {
            _m4gConfig = instance.ControllerConfig.OrderBatchingConfig as M4GConfiguration;
            if (_m4gConfig == null)
                throw new InvalidOperationException("M4GManager requires an M4GConfiguration.");
            // Unit-consistency guard (spec 3.5): lambda/mu/epsilon are priced in metres, which
            // requires the objective's distance terms to be metres too. StarveAwareCostEnabled
            // switches the cost functions to seconds - fail hard rather than solve nonsense.
            if (instance.SettingConfig != null && instance.SettingConfig.StarveAwareCostEnabled)
                throw new InvalidOperationException(
                    "M4G prices lines in metres but StarveAwareCostEnabled makes the distance terms seconds. "
                    + "Set StarveAwareCostEnabled=false or disable M4G.");
            _pricing = new M4GPricing(_m4gConfig);
        }

        /// <summary>The M4G configuration of this manager.</summary>
        private M4GConfiguration _m4gConfig;
        /// <summary>Price calibration state (spec 3.5).</summary>
        private M4GPricing _pricing;
        /// <summary>Per-decision diagnostic log.</summary>
        private System.IO.StreamWriter _decisionLog;
        /// <summary>Running decision counter, also the probe cadence clock.</summary>
        private int _decisionIndex = 0;

        /// <summary>Lazily opens m4g_decision_log.csv in the run's statistics directory.</summary>
        private void EnsureDecisionLog()
        {
            if (_decisionLog != null) return;
            string dir = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            _decisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "m4g_decision_log.csv"), false)
            { AutoFlush = true };
            _decisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsPa,podsPb,botsRa,"
                + "lambda,mu,delta,epsilon,valuedLines,boundLines,valuedOrders,boundOrders,newTrips,boundUnits,"
                + "objective,solveSec");
        }

        /// <summary>Writes one decision row. Every numeric field is written unformatted for exact diffing.</summary>
        private void WriteDecision(bool solved, int pendingOrders, int stationsWithCap, int podsPa, int podsPb,
            int botsRa, double lambda, double mu, double delta, double epsilon, int valuedLines, int boundLines,
            int valuedOrders, int boundOrders, int newTrips, int boundUnits, double objective, double solveSec)
        {
            EnsureDecisionLog();
            _decisionLog.WriteLine(string.Join(",", new string[] {
                _decisionIndex.ToString(),
                Instance.Controller.CurrentTime.ToString(),
                (solved ? "1" : "0"),
                pendingOrders.ToString(), stationsWithCap.ToString(), podsPa.ToString(), podsPb.ToString(),
                botsRa.ToString(), lambda.ToString(), mu.ToString(), delta.ToString(), epsilon.ToString(),
                valuedLines.ToString(), boundLines.ToString(), valuedOrders.ToString(), boundOrders.ToString(),
                newTrips.ToString(), boundUnits.ToString(), objective.ToString(), solveSec.ToString() }));
        }

        /// <summary>Decision-time snapshot of the world. Mirrors what SplitM2eICManager builds,
        /// minus every IC gate - M4G has no supply cap, no pipeline floor and no lead-time gate.</summary>
        private sealed class M4GSnapshot
        {
            /// <summary>Orders eligible for this decision (parents carry residual demand only).</summary>
            public HashSet<Order> PendingOrders = new HashSet<Order>();
            /// <summary>Free slots per station.</summary>
            public Dictionary<OutputStation, int> Cs = new Dictionary<OutputStation, int>();
            /// <summary>Idle pods standing in storage that carry at least one demanded SKU.</summary>
            public HashSet<Pod> Pa = new HashSet<Pod>();
            /// <summary>Pods already committed to a station (at it or en route).</summary>
            public HashSet<Pod> Pb = new HashSet<Pod>();
            /// <summary>Bots available to be dispatched.</summary>
            public HashSet<Bot> Ra = new HashSet<Bot>();
            /// <summary>Bot owning each committed pod.</summary>
            public Dictionary<Pod, Bot> PodToBot = new Dictionary<Pod, Bot>();
            /// <summary>Committed pods grouped by their destination station.</summary>
            public Dictionary<OutputStation, HashSet<Pod>> InboundPods = new Dictionary<OutputStation, HashSet<Pod>>();
            /// <summary>Remaining demand per order per SKU.</summary>
            public Dictionary<Order, Dictionary<ItemDescription, int>> Residuals
                = new Dictionary<Order, Dictionary<ItemDescription, int>>();
            /// <summary>Pods carrying each demanded SKU.</summary>
            public Dictionary<ItemDescription, List<Pod>> PiSKU = new Dictionary<ItemDescription, List<Pod>>();
            /// <summary>All pods in the model = Pa union Pb.</summary>
            public HashSet<Pod> AllPods = new HashSet<Pod>();
        }

        /// <summary>
        /// Builds the decision snapshot. Note the deliberate difference from M3G: the pending
        /// set is NOT truncated to an urgent subset and NOT clipped to the free-slot count -
        /// the valuation layer's whole point is to see the entire backlog (spec D3).
        /// </summary>
        private M4GSnapshot BuildSnapshot()
        {
            M4GSnapshot snap = new M4GSnapshot();
            snap.Cs = GenerateCs();
            snap.InboundPods = GeneratePs(snap.Cs);

            // Committed pods and their bots.
            foreach (var entry in snap.InboundPods)
                foreach (Pod pod in entry.Value)
                {
                    if (snap.PodToBot.ContainsKey(pod)) continue;
                    Bot owner = null;
                    if (Instance.ResourceManager._usedPods.ContainsKey(pod))
                        owner = Instance.ResourceManager._usedPods[pod];
                    else if (Instance.ResourceManager.BottoPod.ContainsValue(pod))
                        owner = Instance.ResourceManager.BottoPod.First(v => v.Value.ID == pod.ID).Key;
                    if (owner == null) continue;   // ownership not visible yet this tick
                    snap.PodToBot[pod] = owner;
                    snap.Pb.Add(pod);
                    snap.AllPods.Add(pod);
                }

            // Orders whose residual demand is at least partly in stock.
            HashSet<Order> candidates = new HashSet<Order>(_pendingOrders.Where(o =>
                o.RemainingPositions.Any(p => Instance.StockInfo.GetActualStock(p.Key) >= 1)));
            HashSet<ItemDescription> demanded = new HashSet<ItemDescription>(
                candidates.SelectMany(o => o.RemainingPositions.Select(p => p.Key)));

            // Idle storage pods carrying something demanded.
            foreach (var pod in Instance.ResourceManager.UnusedPods.Where(v =>
                v.IsAvailabletoOiSKU(demanded)
                && !Instance.ResourceManager.BottoPod.ContainsValue(v)
                && !Instance.ResourceManager._usedPods.ContainsKey(v)
                && v.Waypoint != null && v.Waypoint.PodStorageLocation))
            {
                snap.Pa.Add(pod);
                snap.AllPods.Add(pod);
            }

            // Dispatchable bots (mirrors M1G's three admission cases).
            foreach (var bot in Instance._outputstationbots)
            {
                if (bot.Pod == null && !Instance.ResourceManager._usedPods.ContainsValue(bot)
                    && !Instance.ResourceManager.BottoPod.ContainsKey(bot))
                    snap.Ra.Add(bot);
                else if (bot.Pod == null && !Instance.ResourceManager.BottoPod.ContainsKey(bot)
                    && !snap.PodToBot.ContainsValue(bot) && bot.CurrentTask is RestTask
                    && bot.GetInfoDestinationWaypoint() == null)
                    snap.Ra.Add(bot);
                else if (CanUseReturnPendingBot(bot))
                    snap.Ra.Add(bot);
            }

            snap.PiSKU = GeneratePiSKU(snap.AllPods);
            snap.PendingOrders = new HashSet<Order>(candidates.Where(o =>
                o.RemainingPositions.Any(p => snap.PiSKU.ContainsKey(p.Key))));

            // Optional solve-time convergence knob: keep only the most urgent K orders.
            if (_m4gConfig.ValuationOrderLimit > 0 && snap.PendingOrders.Count > _m4gConfig.ValuationOrderLimit)
                snap.PendingOrders = new HashSet<Order>(snap.PendingOrders
                    .OrderBy(o => o.DueTime).ThenBy(o => o.ID)
                    .Take(_m4gConfig.ValuationOrderLimit));

            snap.Residuals = snap.PendingOrders.ToDictionary(
                o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            return snap;
        }

        /// <summary>Names of the symbols the model was built from, kept for decoding.</summary>
        private sealed class M4GSymbols
        {
            public List<Symbol> Xps = new List<Symbol>();
            public List<Symbol> Yrp = new List<Symbol>();
            public List<Symbol> Qhat = new List<Symbol>();
            public List<Symbol> Chat = new List<Symbol>();
            public List<Symbol> Zhat = new List<Symbol>();
        }

        /// <summary>Enumerates the decision symbols for a snapshot.</summary>
        private M4GSymbols BuildSymbols(M4GSnapshot snap)
        {
            M4GSymbols sym = new M4GSymbols();
            foreach (var pod in snap.AllPods)
                foreach (var station in snap.Cs.Keys)
                    sym.Xps.Add(new Symbol { pod = pod, outputstation = station,
                        name = "xps_" + pod.ID + "_" + station.ID });
            foreach (var bot in snap.Ra)
                foreach (var pod in snap.Pa)
                    sym.Yrp.Add(new Symbol { robot = bot, pod = pod,
                        name = "yrp_" + bot.ID + "_" + pod.ID });
            foreach (var pod in snap.Pb)
                sym.Yrp.Add(new Symbol { robot = snap.PodToBot[pod], pod = pod,
                    name = "yrp_" + snap.PodToBot[pod].ID + "_" + pod.ID });
            foreach (var order in snap.PendingOrders)
            {
                sym.Zhat.Add(new Symbol { order = order, name = "zh_" + order.ID });
                foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                {
                    sym.Chat.Add(new Symbol { order = order, skui = sku.Key,
                        name = "ch_" + order.ID + "_" + sku.Key.ID });
                    foreach (var pod in snap.PiSKU[sku.Key])
                        foreach (var station in snap.Cs.Keys)
                            sym.Qhat.Add(new Symbol { order = order, skui = sku.Key, pod = pod,
                                outputstation = station,
                                name = "qh_" + sku.Key.ID + "_" + order.ID + "_" + pod.ID + "_" + station.ID });
                }
            }
            return sym;
        }

        /// <summary>Adds the shared pod/bot constraints R1-R5 (spec 3.4).</summary>
        private void AddSharedConstraints(LinearModel wrapper, M4GSnapshot snap, M4GSymbols sym,
            VariableCollection<string> bin)
        {
            // (R1) each pod goes to at most one station
            foreach (var pod in snap.AllPods)
                wrapper.AddConstr(LinearExpression.Sum(sym.Xps.Where(v => v.pod.ID == pod.ID)
                    .Select(v => bin[v.name])) <= 1, "R1");
            // (R2) dispatching a storage pod requires a bot
            foreach (var pod in snap.Pa)
                wrapper.AddConstr(LinearExpression.Sum(sym.Xps.Where(v => v.pod.ID == pod.ID).Select(v => bin[v.name]))
                    <= LinearExpression.Sum(sym.Yrp.Where(v => v.pod.ID == pod.ID).Select(v => bin[v.name])), "R2");
            // (R3) each bot carries at most one pod
            foreach (var bot in snap.Ra)
                wrapper.AddConstr(LinearExpression.Sum(sym.Yrp.Where(v => v.robot.ID == bot.ID)
                    .Select(v => bin[v.name])) <= 1, "R3");
            // (R4) each pod is carried by at most one bot
            foreach (var pod in snap.Pa)
                wrapper.AddConstr(LinearExpression.Sum(sym.Yrp.Where(v => v.pod.ID == pod.ID)
                    .Select(v => bin[v.name])) <= 1, "R4");
            // (R5) already-committed pods are fixed to their destination and carrier
            foreach (var entry in snap.InboundPods)
                foreach (var pod in entry.Value.Where(p => snap.Pb.Contains(p)))
                {
                    wrapper.AddConstr(bin["xps_" + pod.ID + "_" + entry.Key.ID] == 1, "R5a");
                    wrapper.AddConstr(bin["yrp_" + snap.PodToBot[pod].ID + "_" + pod.ID] == 1, "R5b");
                }
        }

        /// <summary>Adds the valuation-layer constraints V1-V4 (spec 3.4).</summary>
        private void AddValuationConstraints(LinearModel wrapper, M4GSnapshot snap, M4GSymbols sym,
            VariableCollection<string> bin, VariableCollection<string> qh)
        {
            // (V1) draws from a pod at a station are bounded by its stock and require dispatch
            foreach (var group in sym.Qhat.GroupBy(v => new { sku = v.skui.ID, pod = v.pod.ID, st = v.outputstation.ID }))
            {
                var first = group.First();
                wrapper.AddConstr(LinearExpression.Sum(group.Select(v => qh[v.name]))
                    <= first.pod.CountAvailable(first.skui) * bin["xps_" + first.pod.ID + "_" + first.outputstation.ID],
                    "V1");
            }
            foreach (var order in snap.PendingOrders)
                foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                {
                    var draws = sym.Qhat.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID)
                        .Select(v => qh[v.name]).ToList();
                    // (V2) never draw more than the residual demand
                    wrapper.AddConstr(LinearExpression.Sum(draws) <= sku.Value, "V2");
                    // (V3) a line only counts as closed when it is drawn in full
                    wrapper.AddConstr(LinearExpression.Sum(draws)
                        >= sku.Value * bin["ch_" + order.ID + "_" + sku.Key.ID], "V3");
                    // (V4) completing an order requires every one of its lines closed
                    wrapper.AddConstr(bin["ch_" + order.ID + "_" + sku.Key.ID] >= bin["zh_" + order.ID], "V4");
                }
        }
    }
}
