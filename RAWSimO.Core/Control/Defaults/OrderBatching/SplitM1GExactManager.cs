using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.SolverWrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using static RAWSimO.Core.Management.ResourceManager;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// MILP-based order-splitting manager with pod-level attribution decided inside the model:
    /// q[i,o,p,s] (4D) replaces SplitM1G's q[i,o,s] (3D), so the solver itself picks which pod
    /// serves each unit instead of a post-solve greedy pass. Independent sibling of
    /// SplitM1GManager (Spec 2) - does not inherit it, mirrors M1GManager directly.
    /// See docs/superpowers/specs/2026-07-07-splitm1g-exact-design.md.
    /// </summary>
    public class SplitM1GExactManager : M1GManager
    {
        /// <summary>
        /// Creates a new instance of this controller.
        /// </summary>
        /// <param name="instance">The instance this controller belongs to.</param>
        public SplitM1GExactManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GExactConfiguration;
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }

        /// <summary>
        /// The split-specific config of this controller.
        /// </summary>
        private SplitM1GExactConfiguration _splitConfig;

        /// <summary>
        /// Shared consolidation CSV logger (splitorders.csv) - same class Spec 2 extracted.
        /// </summary>
        private SplitConsolidationLogger _logger;

        /// <summary>
        /// Order enters Od (urgent-order set) if due within this many seconds (mirrors the
        /// base's private DueTimeOrderofMP, copied because it's private in M1GManager).
        /// </summary>
        private static readonly double _dueTimeOrderofMP = TimeSpan.FromMinutes(30).TotalSeconds;

        /// <summary>
        /// Residual-demand version of GenerateOiSKU: indexes pending orders by the SKUs they
        /// still need (RemainingPositions), not their full demand. Copied verbatim from Spec 2's
        /// SplitM1GManager (private there, not inherited).
        /// </summary>
        private Dictionary<ItemDescription, List<Order>> GenerateOiSKUSplit(HashSet<Order> pendingOrders)
        {
            Dictionary<ItemDescription, List<Order>> OiSKU = new Dictionary<ItemDescription, List<Order>>();
            foreach (var order in pendingOrders)
            {
                foreach (var sku in order.RemainingPositions)
                {
                    if (OiSKU.ContainsKey(sku.Key))
                        OiSKU[sku.Key].Add(order);
                    else
                        OiSKU.Add(sku.Key, new List<Order>() { order });
                }
            }
            return OiSKU;
        }

        /// <summary>
        /// Residual-demand version of GenerateOd. Copied verbatim from Spec 2's SplitM1GManager.
        /// </summary>
        private HashSet<Order> GenerateOdSplit(HashSet<Order> pendingOrders, Dictionary<ItemDescription, List<Pod>> PiSKU)
        {
            HashSet<Order> Od = new HashSet<Order>();
            foreach (Order order in pendingOrders)
            {
                order.Timestay = order.DueTime - (Instance.SettingConfig.StartTime.AddSeconds(Convert.ToInt32(Instance.Controller.CurrentTime)) - order.TimePlaced).TotalSeconds;
                if (order.Timestay < _dueTimeOrderofMP)
                {
                    bool Isadd = true;
                    foreach (var sku in order.RemainingPositions)
                    {
                        if (PiSKU.ContainsKey(sku.Key) && PiSKU[sku.Key].All(v => v.CountAvailable(sku.Key) >= sku.Value && Instance.ResourceManager.UnusedPods.Contains(v)))
                            continue;
                        else
                            Isadd = false;
                    }
                    if (Isadd)
                        Od.Add(order);
                }
            }
            int i = 0;
            foreach (Order order in pendingOrders.OrderBy(v => v.Timestay).ThenBy(u => u.DueTime))
            {
                order.sequence = i;
                i++;
            }
            return Od;
        }

        // ── Starve-aware cost wrappers (copied verbatim from Spec 2's SplitM1GManager; base
        //    members are private there, so this repeats the mirroring rather than inheriting) ──
        private bool _saEnabledExact;
        private double _saNominalSpeedExact;
        private double _saFixedParamExact;
        private Dictionary<int, double> _saEstByStationExact = new Dictionary<int, double>();
        private Dictionary<int, double> _saRepBotPodTimeExact = new Dictionary<int, double>();

        private void PrepareStarveAwareExact(IEnumerable<Pod> pods, Dictionary<OutputStation, int> Cs, HashSet<Bot> Ra)
        {
            _saEnabledExact = Instance != null && Instance.SettingConfig != null && Instance.SettingConfig.StarveAwareCostEnabled;
            if (!_saEnabledExact)
                return;
            _saFixedParamExact = Instance.SettingConfig.StarveAwareFixedParam;
            double cfgSpeed = Instance.SettingConfig.StarveAwareNominalSpeed;
            _saNominalSpeedExact = cfgSpeed > 0.0
                ? cfgSpeed
                : (Instance.Bots != null && Instance.Bots.Count > 0
                    ? Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity))
                    : 1.0);
            double now = Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;
            _saEstByStationExact = new Dictionary<int, double>();
            foreach (var s in Cs.Keys)
                _saEstByStationExact[s.ID] = StarveAwareCost.Est(s, now);
            _saRepBotPodTimeExact = new Dictionary<int, double>();
            foreach (var p in pods)
            {
                double best = double.PositiveInfinity;
                foreach (var r in Ra)
                    best = Math.Min(best, StarveAwareCost.TravelTime(EstimateBotPodDistance(r, p), _saNominalSpeedExact));
                _saRepBotPodTimeExact[p.ID] = best;
            }
        }

        private double ExactBotPodCost(Bot robot, Pod pod)
        {
            double d = EstimateBotPodDistance(robot, pod);
            return _saEnabledExact ? StarveAwareCost.TravelTime(d, _saNominalSpeedExact) : d;
        }

        private double ExactPodStationCost(Pod pod, OutputStation station)
        {
            double d = EstimatePodStationDistance(pod, station);
            if (!_saEnabledExact)
                return d;
            double podStationTime = StarveAwareCost.TravelTime(d, _saNominalSpeedExact);
            double repBotPod = (_saRepBotPodTimeExact.TryGetValue(pod.ID, out var t) && !double.IsPositiveInfinity(t)) ? t : 0.0;
            double taCost = podStationTime + repBotPod;
            double est = _saEstByStationExact.TryGetValue(station.ID, out var e) ? e : double.PositiveInfinity;
            double penalty = StarveAwareCost.DelayPenalty(taCost, est, _saFixedParamExact);
            return podStationTime + penalty;
        }

        /// <summary>
        /// The committed outcome of one SplitM1GExact solve, consumed by DecideAboutPendingOrders.
        /// </summary>
        private class SplitExactSolveResult
        {
            public Dictionary<Symbol, int> NewZiops = new Dictionary<Symbol, int>();
            public List<Symbol> Allocations = new List<Symbol>();
            public HashSet<Order> SplitParents = new HashSet<Order>();
        }

        // ── Per-decision summary logger (diagnostic; separate file from Spec 2's so the two
        //    models' decision logs never collide when both are run against the same output dir) ──
        private System.IO.StreamWriter _exactDecisionLog;
        private int _exactDecisionIndex = 0;
        private void WriteExactDecisionLog(bool solved, double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int nXps, int nChildren, int nFastPath, int unitsAssigned, double sumUs, double objective, double optSec)
        {
            if (_exactDecisionLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _exactDecisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "splitm1gx_decision_log.csv"), false) { AutoFlush = true };
                _exactDecisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsInModel,xps,children,fastPath,units,sumUs,objective,solveSec");
            }
            _exactDecisionLog.WriteLine(string.Join(",", new string[] {
                _exactDecisionIndex.ToString(),
                time.ToString(System.Globalization.CultureInfo.InvariantCulture),
                solved ? "1" : "0",
                pendingOrdersN.ToString(),
                stationsWithCap.ToString(),
                podsInModel.ToString(),
                nXps.ToString(),
                nChildren.ToString(),
                nFastPath.ToString(),
                unitsAssigned.ToString(),
                sumUs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                objective.ToString(System.Globalization.CultureInfo.InvariantCulture),
                optSec.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
            _exactDecisionIndex++;
        }

        /// <summary>
        /// Split-exact version of Initialize: snapshots residual demand exactly like Spec 2's
        /// InitializeSplit, but appends 4D q[i,o,p,s] / ysp[o,s] / completion-flag variable name
        /// lists (keys 7/8/9) instead of Spec 2's 3D q + M1-only zfull. The pod dimension pulls
        /// candidates from PiSKU (both Pb and Pa pods that physically carry the SKU) - link-up
        /// needs a real candidate for every pod actually able to supply the unit, not only
        /// newly-claimable (Pa) ones the way Spec 2's dops did.
        /// </summary>
        private HashSet<Pod> InitializeSplitExact(out Dictionary<ItemDescription, List<Pod>> PiSKU, out Dictionary<ItemDescription, List<Order>> OiSKU,
            out Dictionary<int, List<Symbol>> variableNames, out Dictionary<OutputStation, int> Cs, out HashSet<Order> pendingOrders,
            out Dictionary<OutputStation, HashSet<Pod>> inboundPods, out HashSet<Bot> Ra, out HashSet<Bot> Rb,
            out HashSet<Bot> R, out HashSet<Pod> Pb, out HashSet<Pod> Pa, out Dictionary<Pod, Bot> PodToBot,
            out Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            HashSet<Order> pendingOrders1 = _splitConfig != null && _splitConfig.CrossTime
                ? new HashSet<Order>(_pendingOrders.Where(o => o.RemainingPositions.Any(p => Instance.StockInfo.GetActualStock(p.Key) >= 1)))
                : new HashSet<Order>(_pendingOrders.Where(o => o.RemainingPositions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value)));
            OiSKU = GenerateOiSKUSplit(pendingOrders1);
            Cs = GenerateCs();
            inboundPods = GeneratePs(Cs);
            HashSet<ItemDescription> ItemofOiSKU = new HashSet<ItemDescription>(OiSKU.Keys);
            HashSet<Pod> allPods = new HashSet<Pod>();
            Ra = new HashSet<Bot>();
            Rb = new HashSet<Bot>();
            R = new HashSet<Bot>();
            Pb = new HashSet<Pod>();
            PodToBot = new Dictionary<Pod, Bot>();
            HashSet<Pod> Pa1 = new HashSet<Pod>();
            foreach (var pods in inboundPods)
            {
                foreach (Pod pod in pods.Value)
                {
                    if (PodToBot.ContainsKey(pod)) continue;
                    if (Instance.ResourceManager._usedPods.ContainsKey(pod))
                    {
                        allPods.Add(pod);
                        Rb.Add(Instance.ResourceManager._usedPods[pod]);
                        R.Add(Instance.ResourceManager._usedPods[pod]);
                        PodToBot[pod] = Instance.ResourceManager._usedPods[pod];
                        Pb.Add(pod);
                    }
                    else if (Instance.ResourceManager.BottoPod.ContainsValue(pod))
                    {
                        var bot = Instance.ResourceManager.BottoPod.Where(V => V.Value.ID == pod.ID).First().Key;
                        allPods.Add(pod);
                        Rb.Add(bot);
                        R.Add(bot);
                        PodToBot[pod] = bot;
                        Pb.Add(pod);
                    }
                    else
                    {
                        // Snapshot can contain a station inbound pod before its pod-bot ownership is visible.
                    }
                }
            }
            foreach (var pod in Instance.ResourceManager.UnusedPods.Where(v =>
                v.IsAvailabletoOiSKU(ItemofOiSKU) &&
                !Instance.ResourceManager.BottoPod.ContainsValue(v) &&
                !Instance.ResourceManager._usedPods.ContainsKey(v) &&
                v.Waypoint != null &&
                v.Waypoint.PodStorageLocation))
            {
                allPods.Add(pod);
                Pa1.Add(pod);
            }
            foreach (var bot in Instance._outputstationbots)
            {
                if (bot.Pod == null && !Instance.ResourceManager._usedPods.ContainsValue(bot) && !Instance.ResourceManager.BottoPod.ContainsKey(bot))
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
                else if (bot.Pod == null && !Instance.ResourceManager.BottoPod.ContainsKey(bot) && !Rb.Contains(bot) && bot.CurrentTask is RestTask && bot.GetInfoDestinationWaypoint() == null)
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
                else if (CanUseReturnPendingBot(bot))
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
            }
            PiSKU = GeneratePiSKU(allPods);
            if (_splitConfig != null && _splitConfig.CrossTime)
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.Any(p => IsAvailabletoPiSKU(p.Key) >= 1)));
            else
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.All(p => IsAvailabletoPiSKU(p.Key) >= p.Value)));
            HashSet<Order> Od = GenerateOdSplit(pendingOrders, PiSKU);
            if (Od.Count > Cs.Values.Sum())
                pendingOrders = new HashSet<Order>(Od);
            OiSKU = GenerateOiSKUSplit(pendingOrders);
            residuals = pendingOrders.ToDictionary(o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            variableNames = CreatedeVarName(PiSKU, OiSKU, allPods, pendingOrders, Cs, R, Pa1, out Pa);
            var piSkuCopy = PiSKU;  // Capture out parameter for use in lambda
            bool crossTimeExact = _splitConfig != null && _splitConfig.CrossTime;
            List<Symbol> deVarNameq = new List<Symbol>();
            List<Symbol> deVarNamey = new List<Symbol>();
            List<Symbol> deVarNamez = new List<Symbol>();
            foreach (var order in pendingOrders)
            {
                foreach (var station in Cs.Keys)
                    deVarNamey.Add(new Symbol { order = order, outputstation = station, name = "yspx" + "_" + order.ID.ToString() + "_" + station.ID.ToString() });
                foreach (var sku in residuals[order].Where(p => piSkuCopy.ContainsKey(p.Key)))
                {
                    foreach (var pod in piSkuCopy[sku.Key])
                    {
                        foreach (var station in Cs.Keys)
                            deVarNameq.Add(new Symbol { order = order, outputstation = station, skui = sku.Key, pod = pod, name = "qx" + "_" + sku.Key.ID.ToString() + "_" + order.ID.ToString() + "_" + pod.ID.ToString() + "_" + station.ID.ToString() });
                    }
                }
                deVarNamez.Add(new Symbol { order = order, name = (crossTimeExact ? "zdonex" : "zfullx") + "_" + order.ID.ToString() });
            }
            variableNames.Add(7, deVarNameq);
            variableNames.Add(8, deVarNamey);
            variableNames.Add(9, deVarNamez);
            return allPods;
        }

        /// <summary>
        /// Builds and solves the SplitM1GExact MILP (q[i,o,p,s], 4D pod-exact attribution) and
        /// commits the solution directly: robot-pod claiming (base parity), child creation via
        /// SplitMilpDecoder (Spec 2, unchanged - fed pod-aggregated quantities), and per-(sku,
        /// order-or-child,pod,station) Ziops writes read straight off the solved q values. No
        /// greedy Ziops pass, no dops, no unused-pod release - link-up + shi13' make every
        /// selected pod's usage exact by construction (see spec §5/§7).
        /// </summary>
        private SplitExactSolveResult SolveSplitExact(SolverType type, Dictionary<ItemDescription, List<Pod>> PiSKU,
            Dictionary<ItemDescription, List<Order>> OiSKU, IEnumerable<Pod> Pods, Dictionary<OutputStation, int> Cs,
            Dictionary<int, List<Symbol>> variableNames, HashSet<Order> pendingOrders,
            Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Bot> Ra, HashSet<Bot> Rb, HashSet<Bot> R,
            HashSet<Pod> Pb, HashSet<Pod> Pa, Dictionary<Pod, Bot> PodToBot,
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            LinearModel wrapper = new LinearModel(type, (string s) => { Console.Write(s); });
            SplitExactSolveResult result = new SplitExactSolveResult();
            bool crossTime = _splitConfig != null && _splitConfig.CrossTime;
            List<Symbol> deVarNamexps = variableNames[1];
            List<Symbol> deVarNameyrp = variableNames[4];
            List<Symbol> deVarNameus = variableNames[5];
            List<Symbol> deVarNameq = variableNames[7];
            List<Symbol> deVarNamey = variableNames[8];
            List<Symbol> deVarNamez = variableNames[9];
            double w1 = 1;
            double w2 = _splitConfig != null ? _splitConfig.OrderRewardWeight : -40;
            double w3 = _splitConfig != null ? _splitConfig.IdleSlotWeight : 1000;
            double w4 = _splitConfig != null ? _splitConfig.PodTripFixedCost : 0;
            double w5 = _splitConfig != null ? _splitConfig.ProcessingPodDrawReward : 0;
            // Pods currently being PROCESSED (bot standing at the station's pick waypoint) - the
            // perishable squeeze targets. Queueing / en-route Pb pods deliberately excluded: their
            // windows stay open for future epochs, the processing pod's window is closing now.
            HashSet<Pod> processingPods = new HashSet<Pod>();
            if (w5 != 0)
                foreach (var station in Cs.Keys)
                    foreach (var pod in Pb)
                        if (station.Waypoint != null && PodToBot.ContainsKey(pod) && PodToBot[pod].CurrentWaypoint != null
                            && PodToBot[pod].CurrentWaypoint.ID == station.Waypoint.ID)
                            processingPods.Add(pod);
            int maxCs = Cs.Count > 0 ? Cs.Values.Max() : 1;
            int maxR = residuals.Count > 0 ? residuals.Values.SelectMany(d => d.Values).DefaultIfEmpty(1).Max() : 1;
            VariableCollection<string> variablesBinary = new VariableCollection<string>(wrapper, VariableType.Binary, 0, 1, (string s) => { return s; });
            VariableCollection<string> variablesUs = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxCs, (string s) => { return s; });
            VariableCollection<string> variablesQ = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxR, (string s) => { return s; });
            PrepareStarveAwareExact(Pods, Cs, Ra);
            PrepareDecisionExtras(Pods, Cs, Ra);
            // Objective: w1*(pod-station + bot-pod distance) + w2*(completion reward) + w3*(idle slots)
            // + w4 per newly-claimable pod trip (folded into the xps coefficient; sum_s xps <= 1 per
            // pod makes the coefficient add-on an exact per-trip fixed cost)
            // + w5 per unit drawn from the pod currently being PROCESSED at a station (squeeze the
            // closing window; queued/en-route pods excluded; term omitted at w5=0 = bit-identical).
            LinearExpression objective;
            if (Ra.Count() > 0)
                objective = (LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (ExactPodStationCost(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation) + w4)), wrapper)
                    + LinearExpression.Sum(deVarNameyrp.Where(u => Ra.Contains(u.robot) && Instance.ResourceManager.UnusedPods.Contains(u.pod) && u.pod.Waypoint != null).Select(v => variablesBinary[v.name] *
                    ExactBotPodCost(v.robot, v.pod)), wrapper)) * w1
                    + LinearExpression.Sum(deVarNamez.Select(v => variablesBinary[v.name])) * w2
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesUs[v.name])) * w3;
            else
                objective = LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (ExactPodStationCost(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation) + w4)), wrapper) * w1
                    + LinearExpression.Sum(deVarNamez.Select(v => variablesBinary[v.name])) * w2
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesUs[v.name])) * w3;
            if (w5 != 0 && processingPods.Count > 0)
            {
                // A processing pod may carry nothing the backlog still needs -> no q variables
                // reference it; LinearExpression.Sum throws on an empty sequence, so materialize
                // and guard (the w5 term is simply absent when there is nothing to squeeze).
                var squeezeVars = deVarNameq.Where(v => processingPods.Contains(v.pod)).Select(v => variablesQ[v.name]).ToList();
                if (squeezeVars.Count > 0)
                    objective = objective + LinearExpression.Sum(squeezeVars) * w5;
            }
            // (eps) lexicographic item-pile-on layer: every assigned unit earns eps (negative
            // = reward), phase-blind - among completion-equivalent solutions the solver now
            // systematically prefers drawing more items per committed pod. |eps| << |w2|, so
            // it can never trade away a completion. Guarded so eps=0 stays bit-identical
            // (LinearExpression.Sum throws on an empty sequence).
            double eps = _splitConfig != null ? _splitConfig.UnitDrawReward : 0;
            if (eps != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name])) * eps;
            wrapper.SetObjective(objective, OptimizationSense.Minimize);
            // (ecap) sequential trip discipline: at most K new pod trips per decision. Restores
            // the one-move-at-a-time cadence (the greedy's structural advantage) while keeping
            // each move jointly optimal. Skipped at K=0 (unlimited, bit-identical default).
            int tripCap = _splitConfig != null ? _splitConfig.MaxNewPodTripsPerDecision : 0;
            if (tripCap > 0)
            {
                var newTripVars = deVarNamexps.Where(v => Pa.Contains(v.pod)).Select(v => variablesBinary[v.name]).ToList();
                if (newTripVars.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(newTripVars) <= tripCap, "ecap");
            }
            // (elink1) sum_o q[i,o,p,s] <= stock[p,i] * xps[p,s] - ties the TOTAL demand drawn from
            // one specific pod's real inventory across ALL orders. A per-order bound alone would let
            // several orders each draw the full stock of the same pod (Task 6 smoke crash:
            // "Cannot reserve an item for picking, if there is none left of the kind!").
            foreach (var group in deVarNameq.GroupBy(v => new { skuId = v.skui.ID, podId = v.pod.ID, stationId = v.outputstation.ID }))
            {
                var first = group.First();
                wrapper.AddConstr(LinearExpression.Sum(group.Select(v => variablesQ[v.name]))
                    <= first.pod.CountAvailable(first.skui) * variablesBinary["xps" + "_" + first.pod.ID.ToString() + "_" + first.outputstation.ID.ToString()], "elink1");
            }
            // (elink2) ysp[o,s] <= sum_i sum_p q[i,o,p,s] - forbids an empty child
            foreach (var y in deVarNamey)
                wrapper.AddConstr(variablesBinary[y.name] <= LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == y.order.ID && v.outputstation.ID == y.outputstation.ID).Select(v => variablesQ[v.name])), "elink2");
            // (elink3) sum_p q[i,o,p,s] <= r[o,i] * ysp[o,s] - q>0 forces the slot flag on, so
            // eshi4's capacity accounting sees every order the decoder will allocate. Without it
            // the solver earns completion rewards with ysp=0 and the decoder over-commits stations
            // ("Cannot reserve more capacity than this station has!" - second Task 6 smoke crash).
            // Mirrors Spec 2's slink1 with the pod dimension aggregated.
            foreach (var order in pendingOrders)
            {
                foreach (var sku in residuals[order].Where(p => PiSKU.ContainsKey(p.Key)))
                {
                    foreach (var station in Cs.Keys)
                    {
                        wrapper.AddConstr(LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID && v.outputstation.ID == station.ID).Select(v => variablesQ[v.name]))
                            <= sku.Value * variablesBinary["yspx" + "_" + order.ID.ToString() + "_" + station.ID.ToString()], "elink3");
                    }
                }
            }
            // (eshi4) pure slot conservation
            foreach (var station in Cs.Keys)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamey.Where(v => v.outputstation.ID == station.ID).Select(v => variablesBinary[v.name])) == Cs[station] - variablesUs["us" + "_" + station.ID.ToString()], "eshi4");
            // (eshi6) pod assigned to at most one station
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "eshi6");
            // (eshi7/eshi11) inherited (Pb) pods/bots stay fixed from the previous decision
            foreach (var station in inboundPods)
            {
                foreach (var pod in station.Value)
                {
                    if (!Pb.Contains(pod))
                        continue;
                    wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.Key.ID.ToString()] == 1, "eshi7");
                    wrapper.AddConstr(variablesBinary["yrp" + "_" + PodToBot[pod].ID.ToString() + "_" + pod.ID.ToString()] == 1, "eshi11");
                }
            }
            // (eshi8) a pod assigned to a station needs a bot
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])), "eshi8");
            // (eshi9) at most one bot per pod
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "eshi9");
            // (eshi10) at most one pod per bot
            foreach (var robot in R)
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.robot.ID == robot.ID).Select(v => variablesBinary[v.name])) <= 1, "eshi10");
            // (eshi13') a newly-claimed pod must actually be consumed - summed directly against q,
            // not the dops proxy Spec 2 used (see spec §3.3/§7: dops never linked to real q usage)
            foreach (var pod in Pa)
            {
                foreach (var station in Cs.Keys)
                    wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()]
                        <= LinearExpression.Sum(deVarNameq.Where(v => v.pod.ID == pod.ID && v.outputstation.ID == station.ID).Select(v => variablesQ[v.name])), "eshi13");
            }
            // (eM1/eM2/eM2done) per-SKU completion linkage, aggregated across stations AND pods
            foreach (var order in pendingOrders)
            {
                string zname = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                foreach (var sku in residuals[order].Where(p => PiSKU.ContainsKey(p.Key)))
                {
                    var lhs = LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID).Select(v => variablesQ[v.name]));
                    if (!crossTime)
                        wrapper.AddConstr(lhs == sku.Value * variablesBinary[zname], "eM1");
                    else
                    {
                        wrapper.AddConstr(lhs <= sku.Value, "eM2");
                        wrapper.AddConstr(lhs >= sku.Value * variablesBinary[zname], "eM2done");
                    }
                }
            }
            wrapper.Update();
            DateTime _optStart = DateTime.Now;
            wrapper.Optimize();
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;
            if (wrapper.HasSolution())
            {
                List<Symbol> IsdeVarNamexps = deVarNamexps.Where(v => Math.Round(variablesBinary[v.name].GetValue()) != 0).ToList();
                List<Symbol> IsdeVarNameq = deVarNameq.Where(v => Math.Round(variablesQ[v.name].GetValue()) > 0).ToList();
                foreach (var itemName in deVarNameyrp)
                {
                    if (Math.Round(variablesBinary[itemName.name].GetValue()) != 0 && Ra.Contains(itemName.robot))
                    {
                        Instance.ResourceManager.BottoPod.Add(itemName.robot, itemName.pod);
                        Instance.ResourceManager.ClaimPod(itemName.pod, itemName.robot, BotTaskType.Extract);
                        foreach (var xps in IsdeVarNamexps.Where(v => v.pod.ID == itemName.pod.ID))
                            xps.outputstation.RegisterInboundPod(itemName.pod);
                    }
                }
                List<OutputStation> stationList = Cs.Keys.OrderBy(s => s.ID).ToList();
                int nChildren = 0, nFastPath = 0, unitsAssigned = 0;
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    List<Dictionary<ItemDescription, int>> perStation = new List<Dictionary<ItemDescription, int>>();
                    foreach (var station in stationList)
                    {
                        var podQuantities = IsdeVarNameq
                            .Where(v => v.order.ID == order.ID && v.outputstation.ID == station.ID)
                            .Select(v => new KeyValuePair<ItemDescription, int>(v.skui, (int)Math.Round(variablesQ[v.name].GetValue())));
                        perStation.Add(SplitM1GExactAggregator.AggregatePodQuantities(podQuantities));
                    }
                    bool fullyAssigned;
                    List<KeyValuePair<int, Dictionary<ItemDescription, int>>> parts =
                        SplitMilpDecoder.Decode(residuals[order].ToList(), perStation, crossTime, out fullyAssigned);
                    if (parts.Count == 0)
                        continue;
                    unitsAssigned += parts.Sum(p => p.Value.Values.Sum());
                    if (parts.Count == 1 && fullyAssigned && !order.IsSplitParent)
                    {
                        OutputStation station = stationList[parts[0].Key];
                        result.Allocations.Add(new Symbol { order = order, outputstation = station });
                        foreach (var q in IsdeVarNameq.Where(v => v.order.ID == order.ID && v.outputstation.ID == station.ID))
                        {
                            int units = (int)Math.Round(variablesQ[q.name].GetValue());
                            Symbol name = new Symbol { pod = q.pod, order = order, outputstation = station, skui = q.skui,
                                name = "ziops" + "_" + q.skui.ID.ToString() + "_" + order.ID.ToString() + "_" + q.pod.ID.ToString() + "_" + station.ID.ToString() };
                            result.NewZiops.Add(name, units);
                            Instance.ResourceManager._Ziops[station].Add(name, units);
                        }
                        nFastPath++;
                    }
                    else
                    {
                        foreach (var part in parts)
                        {
                            OutputStation station = stationList[part.Key];
                            Order child = Order.CreateSplitChild(order, part.Value);
                            child.ID = idoforder++;
                            Instance.ResourceManager.TransferExtractRequests(order, child);
                            result.Allocations.Add(new Symbol { order = child, outputstation = station });
                            foreach (var q in IsdeVarNameq.Where(v => v.order.ID == order.ID && v.outputstation.ID == station.ID))
                            {
                                int units = (int)Math.Round(variablesQ[q.name].GetValue());
                                Symbol name = new Symbol { pod = q.pod, order = child, outputstation = station, skui = q.skui,
                                    name = "ziops" + "_" + q.skui.ID.ToString() + "_" + child.ID.ToString() + "_" + q.pod.ID.ToString() + "_" + station.ID.ToString() };
                                result.NewZiops.Add(name, units);
                                Instance.ResourceManager._Ziops[station].Add(name, units);
                            }
                            nChildren++;
                        }
                        result.SplitParents.Add(order);
                    }
                }
                double sumUs = 0.0;
                foreach (var s in deVarNameus)
                    sumUs += Math.Round(variablesUs[s.name].GetValue());
                WriteExactDecisionLog(true, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    IsdeVarNamexps.Count, nChildren, nFastPath, unitsAssigned, sumUs, wrapper.GetObjectiveValue(), _optSec);
            }
            else
            {
                WriteExactDecisionLog(false, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    0, 0, 0, 0, 0.0, double.NaN, _optSec);
            }
            return result;
        }

        /// <summary>
        /// This is called to decide about potentially pending orders (split-exact MILP version).
        /// Mirrors Spec 2's SplitM1GManager.DecideAboutPendingOrders wiring exactly - only the
        /// Initialize/Solve method names and result type differ.
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
            DateTime A = DateTime.Now;
            Dictionary<ItemDescription, List<Pod>> PiSKU;
            Dictionary<ItemDescription, List<Order>> OiSKU;
            Dictionary<int, List<Symbol>> variableNames;
            Dictionary<OutputStation, int> Cs;
            Dictionary<OutputStation, HashSet<Pod>> inboundPods;
            HashSet<Order> pendingOrders;
            HashSet<Bot> Ra;
            HashSet<Bot> Rb;
            HashSet<Bot> R;
            HashSet<Pod> Pb;
            HashSet<Pod> Pa;
            Dictionary<Pod, Bot> PodToBot;
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals;
            HashSet<Pod> allPods = InitializeSplitExact(out PiSKU, out OiSKU, out variableNames, out Cs, out pendingOrders,
                out inboundPods, out Ra, out Rb, out R, out Pb, out Pa, out PodToBot, out residuals);
            if (R.Count() > 0 && pendingOrders.Count > 0)
            {
                SplitExactSolveResult result = SolveSplitExact(SolverType.Gurobi, PiSKU, OiSKU, allPods, Cs, variableNames,
                    pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa, PodToBot, residuals);
                foreach (var symbol in result.NewZiops)
                {
                    for (int i = 0; i < symbol.Value; i++)
                        symbol.Key.pod.JustRegisterItem(symbol.Key.skui);
                }
                foreach (var alloc in result.Allocations)
                {
                    AllocateOrder(alloc.order, alloc.outputstation);
                    Instance.StatCustomControllerInfo.CustomLogOB1++;
                }
                foreach (var parent in result.SplitParents)
                {
                    if (double.IsPositiveInfinity(parent.TimeStampSubmit))
                        parent.TimeStampSubmit = Instance.Controller.CurrentTime;
                    if (parent.IsFullyClaimed)
                    {
                        _pendingOrders.Remove(parent);
                        (Instance.ItemManager as ItemManager).TakeAvailableOrder(parent);
                    }
                }
                Instance.Observer.TimeOrderBatchingbyMP((DateTime.Now - A).TotalSeconds);
            }
        }
    }
}
