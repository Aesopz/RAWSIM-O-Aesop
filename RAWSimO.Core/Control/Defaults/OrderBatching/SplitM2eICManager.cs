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
    /// M2e-IC (Inbound-Committed Split, spec v4): faithful mirror of SplitM1GExactManager
    /// (4D q[i,o,p,s] MILP) plus the P1/SG gates, the D9 multi-part penalty, the D11
    /// lead-gated pipeline floor, the D14 coverage and D15 scarcity tie-breaks and the PK
    /// packing budget. SplitM1GExactManager itself stays untouched as the M2e baseline.
    /// See docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md.
    /// </summary>
    public class SplitM2eICManager : M1GManager
    {
        /// <summary>
        /// Creates a new instance of this controller.
        /// </summary>
        /// <param name="instance">The instance this controller belongs to.</param>
        public SplitM2eICManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GExactConfiguration;
            _icConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM2eICConfiguration;
            // (IC) experimental M2e arms re-shape the objective or the solve structure;
            // their interaction with the P1/SG gates is undefined - fail fast.
            if (_splitConfig != null && (_splitConfig.AdaptiveExactResweeps || _splitConfig.SunkFirstScoring
                || _splitConfig.CoverageFirstScoring || _splitConfig.TrueCompletionReward > 0))
                throw new InvalidOperationException("SplitM2eIC does not support AdaptiveExactResweeps/SunkFirstScoring/CoverageFirstScoring/TrueCompletionReward.");
            // (IC) global downstream packing buffer; probe tracking always on, the PK
            // budget constraint additionally requires a positive total capacity
            // (PackingStationCount x PackingBufferCapacity). Creating the buffer also
            // arms the two null-safe engine hooks (release + D16 trigger).
            if (instance.PackingBuffer == null)
                instance.PackingBuffer = new PackingBuffer(_icConfig != null
                    ? M2eICMath.TotalPackingCapacity(_icConfig.PackingStationCount, _icConfig.PackingBufferCapacity) : 0);
                instance.PackingBuffer.WakeOnLastPick = true;
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }

        /// <summary>The IC-specific config (v4 fields live here).</summary>
        private SplitM2eICConfiguration _icConfig;

        /// <summary>
        /// The split-specific config of this controller.
        /// </summary>
        private SplitM1GExactConfiguration _splitConfig;

        /// <summary>
        /// Shared consolidation CSV logger (splitorders.csv) - same class Spec 2 extracted.
        /// </summary>
        private SplitConsolidationLogger _logger;

        /// <summary>
        /// (Split-fed dispatch) station IDs where the PREVIOUS completed decode actually
        /// assigned a split (non-whole) order to a slot - a realized fact, not a mere
        /// eligibility check. Read at the START of the next solve (one epoch lag, effectively
        /// immediate given epochs fire every few seconds) to couple the pipeline floor's gate
        /// to genuine squeeze activity rather than the SG gate's permissive precondition.
        /// Overwritten (not accumulated) each decode so it only ever reflects the most recent
        /// solve's realized splits.
        /// </summary>
        private HashSet<int> _icPrevSplitStations = new HashSet<int>();

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

        // â”€â”€ Starve-aware cost wrappers (copied verbatim from Spec 2's SplitM1GManager; base
        //    members are private there, so this repeats the mirroring rather than inheriting) â”€â”€
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
            public int CompletionCount;
            public int NewTripCount;
            public int NewPodOrderCount;
            public int ProcessingOrderCount;
            public int FocusOrderCount;
            public int UrgencyScore;
            public int StationPartCount;
            public int NewPodUnitCount;
            public int AssignedUnitCount;
            public int ProcessingUnitCount;
            public int PartialOrderCount;
            public bool HasSolution;
            public Bot NewTripBot;
            public Dictionary<int, Bot> NewTripBotsByPodId = new Dictionary<int, Bot>();
            public Dictionary<int, int> NewPodUnitsByPodId = new Dictionary<int, int>();
        }

        // â”€â”€ Per-decision summary logger (diagnostic; separate file from Spec 2's so the two
        //    models' decision logs never collide when both are run against the same output dir) â”€â”€
        private System.IO.StreamWriter _exactDecisionLog;
        private int _exactDecisionIndex = 0;
        private System.IO.StreamWriter _adaptiveExactLog;
        private System.IO.StreamWriter _earlyReleaseLog;

        // (Fill fairness diagnostic) one row per parent released early from the Fill backlog.
        // Only ever called on the flag-on path, so no file appears when the feature is off.
        private void WriteEarlyRelease(int parentId, int remainingUnitsAtRelease)
        {
            if (_earlyReleaseLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir)) dir = ".";
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                _earlyReleaseLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "early_release.csv"), false) { AutoFlush = true };
                _earlyReleaseLog.WriteLine("time,parentId,remainingUnitsAtRelease");
            }
            _earlyReleaseLog.WriteLine(
                Instance.Controller.CurrentTime.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "," + parentId + "," + remainingUnitsAtRelease);
        }
        private int _adaptiveExactEpoch = 0;
        private System.IO.StreamWriter _adaptiveReplenishmentLog;
        private System.IO.StreamWriter _adaptivePipelineProbeLog;
        private HashSet<string> _adaptivePipelineProbeKeys = new HashSet<string>();
        private Dictionary<int, int> _adaptiveFocusPodByStationId = new Dictionary<int, int>();
        private Dictionary<int, double> _adaptiveLastReplenishmentByStationId = new Dictionary<int, double>();

        private void WriteAdaptiveExactLog(int iteration, bool allowNewTrip, bool partialMode, SplitExactSolveResult result)
        {
            if (_adaptiveExactLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _adaptiveExactLog = new System.IO.StreamWriter(
                    System.IO.Path.Combine(dir, "m2eic_adaptive_exact_log.csv"), false) { AutoFlush = true };
                _adaptiveExactLog.WriteLine("epoch,iteration,time,allowNewTrip,partialMode,completions,newTrips,newPodOrders,processingOrders,focusOrders,urgency,stationParts,newPodUnits,units,processingUnits,partialOrders,allocations,qSymbols");
            }
            _adaptiveExactLog.WriteLine(string.Join(",", new string[] {
                _adaptiveExactEpoch.ToString(),
                iteration.ToString(),
                Instance.Controller.CurrentTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                allowNewTrip ? "1" : "0",
                partialMode ? "1" : "0",
                result.CompletionCount.ToString(),
                result.NewTripCount.ToString(),
                result.NewPodOrderCount.ToString(),
                result.ProcessingOrderCount.ToString(),
                result.FocusOrderCount.ToString(),
                result.UrgencyScore.ToString(),
                result.StationPartCount.ToString(),
                result.NewPodUnitCount.ToString(),
                result.AssignedUnitCount.ToString(),
                result.ProcessingUnitCount.ToString(),
                result.PartialOrderCount.ToString(),
                result.Allocations.Count.ToString(),
                result.NewZiops.Count.ToString()
            }));
        }

        private void WriteExactDecisionLog(bool solved, double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int nXps, int nChildren, int nFastPath, int unitsAssigned, double sumUs, double objective, double optSec,
            int partialOrders, int partialOnlyNewTrips, int odCount, bool odFired,
            int sunkOrdersStar, int sunkItemsStar, int sunkUnitsFinal, int newTripsFinal,
            double solve1Sec, double solve2Sec,
            int icWhole, int icPackOcc, int icPackBudget, int icProcUnits,
            int icEpSum, int icShortfall, int icSgOpen, int icGateForegone)
        {
            if (_exactDecisionLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _exactDecisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "splitm2eic_decision_log.csv"), false) { AutoFlush = true };
                _exactDecisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsInModel,xps,children,fastPath,units,sumUs,objective,solveSec,partialOrders,partialOnlyNewTrips,odCount,odFired,sunkOrdersStar,sunkItemsStar,sunkUnitsFinal,newTripsFinal,solve1Sec,solve2Sec,icWhole,icPackOcc,icPackBudget,icProcUnits,icEpSum,icShortfall,icSgOpen,icGateForegone");
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
                optSec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                partialOrders.ToString(),
                partialOnlyNewTrips.ToString(),
                odCount.ToString(),
                odFired ? "1" : "0",
                sunkOrdersStar.ToString(),
                sunkItemsStar.ToString(),
                sunkUnitsFinal.ToString(),
                newTripsFinal.ToString(),
                solve1Sec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                solve2Sec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                icWhole.ToString(),
                icPackOcc.ToString(),
                icPackBudget.ToString(),
                icProcUnits.ToString(),
                icEpSum.ToString(),
                icShortfall.ToString(),
                icSgOpen.ToString(),
                icGateForegone.ToString()
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
            out Dictionary<Order, Dictionary<ItemDescription, int>> residuals, out int odCount, out bool odFired)
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
            odCount = Od.Count;
            odFired = odCount > Cs.Values.Sum();
            if (odFired)
            {
                // (IC/adm) parent admission priority: when the urgent set replaces the
                // backlog, existing split parents survive the replacement like deadline
                // orders - their residuals must stay visible to the solver (the documented
                // Od-replacement exclusion dropped open parents from 26% of decisions).
                if (_icConfig != null && _icConfig.ParentAdmissionPriority)
                {
                    HashSet<Order> icKeep = new HashSet<Order>(Od);
                    foreach (var icParent in pendingOrders.Where(o => o.IsSplitParent))
                        icKeep.Add(icParent);
                    pendingOrders = icKeep;
                }
                else
                    pendingOrders = new HashSet<Order>(Od);
            }
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
        /// selected pod's usage exact by construction (see spec Â§5/Â§7).
        /// </summary>
        private SplitExactSolveResult SolveSplitExact(SolverType type, Dictionary<ItemDescription, List<Pod>> PiSKU,
            Dictionary<ItemDescription, List<Order>> OiSKU, IEnumerable<Pod> Pods, Dictionary<OutputStation, int> Cs,
            Dictionary<int, List<Symbol>> variableNames, HashSet<Order> pendingOrders,
            Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Bot> Ra, HashSet<Bot> Rb, HashSet<Bot> R,
            HashSet<Pod> Pb, HashSet<Pod> Pa, Dictionary<Pod, Bot> PodToBot,
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals, int odCount, bool odFired,
            Pod adaptiveSelectedPod, OutputStation adaptiveSelectedStation, bool adaptiveAllowPartial)
        {
            List<AdaptivePodCandidate> selected = new List<AdaptivePodCandidate>();
            if (adaptiveSelectedPod != null && adaptiveSelectedStation != null)
                selected.Add(new AdaptivePodCandidate { Pod = adaptiveSelectedPod, Station = adaptiveSelectedStation });
            return SolveSplitExact(type, PiSKU, OiSKU, Pods, Cs, variableNames, pendingOrders,
                inboundPods, Ra, Rb, R, Pb, Pa, PodToBot, residuals, odCount, odFired,
                selected, adaptiveAllowPartial);
        }

        private System.IO.StreamWriter _dualProbeSummaryLog;
        private System.IO.StreamWriter _dualProbeDetailLog;
        private int _dualProbeCallIndex = 0;

        /// <summary>
        /// Diagnostic: solve the relaxed "ideal allocation" LP for this decision's snapshot and log
        /// its shadow prices. Purely observational - it builds a separate model and throws it away,
        /// so the decision this runs alongside is bit-identical either way. Off unless
        /// DualPriceProbeEnabled.
        /// </summary>
        private void RunDualPriceProbe(IEnumerable<Pod> Pods, Dictionary<OutputStation, int> Cs,
            HashSet<Order> pendingOrders, Dictionary<Order, Dictionary<ItemDescription, int>> residuals,
            HashSet<Bot> R)
        {
            if (_icConfig == null || !_icConfig.DualPriceProbeEnabled)
                return;
            int every = Math.Max(1, _icConfig.DualPriceProbeEveryNDecisions);
            if (_dualProbeCallIndex++ % every != 0)
                return;

            DualPriceProbeInput input = new DualPriceProbeInput
            {
                BotCount = R != null ? R.Count : 0,
                CompletionValue = Math.Abs(_splitConfig != null ? _splitConfig.OrderRewardWeight : -40)
            };
            foreach (var station in Cs)
                input.SlotsByStation[station.Key.ID] = station.Value;
            foreach (var order in pendingOrders)
            {
                Dictionary<ItemDescription, int> residual;
                if (!residuals.TryGetValue(order, out residual))
                    continue;
                DualProbeOrder probeOrder = new DualProbeOrder { OrderId = order.ID };
                foreach (var line in residual.Where(l => l.Value > 0))
                    probeOrder.Residual[line.Key.ID] = line.Value;
                if (probeOrder.Residual.Count > 0)
                    input.Orders.Add(probeOrder);
            }
            foreach (var pod in Pods)
            {
                Dictionary<int, int> stock = new Dictionary<int, int>();
                foreach (var item in pod.ItemDescriptionsContained)
                {
                    int available = pod.CountAvailable(item);
                    if (available > 0)
                        stock[item.ID] = available;
                }
                if (stock.Count == 0)
                    continue;
                input.PodStock[pod.ID] = stock;
                Dictionary<int, double> distances = new Dictionary<int, double>();
                foreach (var station in Cs.Keys)
                    distances[station.ID] = EstimatePodStationDistance(pod, station);
                input.PodStationDistance[pod.ID] = distances;
            }

            DualPriceProbeResult probe;
            try
            {
                probe = DualPriceProbe.Solve(input, _icConfig.DualPriceProbeTimeLimitSec);
            }
            catch (Exception ex)
            {
                // A probe must never take the simulation down with it.
                Console.WriteLine("DualPriceProbe failed: " + ex.Message);
                return;
            }

            double now = Instance != null && Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;
            int probeIndex = _dualProbeCallIndex - 1;
            if (_dualProbeSummaryLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _dualProbeSummaryLog = new System.IO.StreamWriter(
                    System.IO.Path.Combine(dir, "dualprice_summary.csv"), false) { AutoFlush = true };
                _dualProbeSummaryLog.WriteLine("probe,time,solved,solveSec,qVars,orders,pods,objective,botPrice,slotPriceMin,slotPriceMean,slotPriceMax,skuCount,skuPriceNonZero,skuPriceMean,skuPriceMax");
                _dualProbeDetailLog = new System.IO.StreamWriter(
                    System.IO.Path.Combine(dir, "dualprice_detail.csv"), false) { AutoFlush = true };
                _dualProbeDetailLog.WriteLine("probe,time,kind,id,priceMax,priceMean");
            }

            System.Globalization.CultureInfo ic = System.Globalization.CultureInfo.InvariantCulture;
            var slotPrices = probe.SlotPrice.Values.ToList();
            var skuMax = probe.SkuPriceMax.Values.ToList();
            _dualProbeSummaryLog.WriteLine(string.Join(",", new string[] {
                probeIndex.ToString(),
                now.ToString(ic),
                probe.Solved ? "1" : "0",
                probe.SolveSeconds.ToString(ic),
                probe.VariableCount.ToString(),
                input.Orders.Count.ToString(),
                input.PodStock.Count.ToString(),
                probe.ObjectiveValue.ToString(ic),
                probe.BotPrice.ToString(ic),
                (slotPrices.Count > 0 ? slotPrices.Min() : 0.0).ToString(ic),
                (slotPrices.Count > 0 ? slotPrices.Average() : 0.0).ToString(ic),
                (slotPrices.Count > 0 ? slotPrices.Max() : 0.0).ToString(ic),
                skuMax.Count.ToString(),
                skuMax.Count(v => Math.Abs(v) > 1e-9).ToString(),
                (skuMax.Count > 0 ? skuMax.Average() : 0.0).ToString(ic),
                (skuMax.Count > 0 ? skuMax.Max() : 0.0).ToString(ic)
            }));
            foreach (var slot in probe.SlotPrice.OrderBy(v => v.Key))
                _dualProbeDetailLog.WriteLine(string.Join(",", new string[] {
                    probeIndex.ToString(), now.ToString(ic), "SLOT", slot.Key.ToString(),
                    slot.Value.ToString(ic), slot.Value.ToString(ic) }));
            foreach (var sku in probe.SkuPriceMax.OrderBy(v => v.Key))
            {
                double mean;
                probe.SkuPriceMean.TryGetValue(sku.Key, out mean);
                _dualProbeDetailLog.WriteLine(string.Join(",", new string[] {
                    probeIndex.ToString(), now.ToString(ic), "SKU", sku.Key.ToString(),
                    sku.Value.ToString(ic), mean.ToString(ic) }));
            }
            _dualProbeDetailLog.WriteLine(string.Join(",", new string[] {
                probeIndex.ToString(), now.ToString(ic), "BOT", "0",
                probe.BotPrice.ToString(ic), probe.BotPrice.ToString(ic) }));
        }

        private SplitExactSolveResult SolveSplitExact(SolverType type, Dictionary<ItemDescription, List<Pod>> PiSKU,
            Dictionary<ItemDescription, List<Order>> OiSKU, IEnumerable<Pod> Pods, Dictionary<OutputStation, int> Cs,
            Dictionary<int, List<Symbol>> variableNames, HashSet<Order> pendingOrders,
            Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Bot> Ra, HashSet<Bot> Rb, HashSet<Bot> R,
            HashSet<Pod> Pb, HashSet<Pod> Pa, Dictionary<Pod, Bot> PodToBot,
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals, int odCount, bool odFired,
            IList<AdaptivePodCandidate> adaptiveSelectedCandidates, bool adaptiveAllowPartial)
        {
            LinearModel wrapper = new LinearModel(type, (string s) => { Console.Write(s); });
            SplitExactSolveResult result = new SplitExactSolveResult();
            RunDualPriceProbe(Pods, Cs, pendingOrders, residuals, R);
            bool crossTime = _splitConfig != null && _splitConfig.CrossTime;
            bool adaptiveReplenishmentTrip = false;
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
            bool adaptiveExact = _splitConfig != null && _splitConfig.AdaptiveExactResweeps;
            if (adaptiveExact && !crossTime)
                throw new InvalidOperationException("AdaptiveExactResweeps requires CrossTime=true.");
            if (adaptiveExact && _splitConfig.AdaptiveExactResweepLimit <= 0)
                throw new InvalidOperationException("AdaptiveExactResweepLimit must be positive.");
            if (adaptiveExact && _splitConfig.AdaptiveFuturePodTarget <= 0)
                throw new InvalidOperationException("AdaptiveFuturePodTarget must be positive.");
            if (adaptiveExact && _splitConfig.AdaptiveMaxPodBurst <= 0)
                throw new InvalidOperationException("AdaptiveMaxPodBurst must be positive.");
            if (adaptiveExact && _splitConfig.AdaptiveRiskPrefetchGapSec < 0)
                throw new InvalidOperationException("AdaptiveRiskPrefetchGapSec cannot be negative.");
            if (adaptiveExact && _splitConfig.AdaptivePeriodicSupplyLeadTimeSec < 0)
                throw new InvalidOperationException("AdaptivePeriodicSupplyLeadTimeSec cannot be negative.");
            // (PR) M2e-PR mode (spec: docs/superpowers/specs/2026-07-14-m2e-pr-design.md):
            // TrueCompletionReward B > 0 switches the z reward to unfiltered true-completion
            // semantics (eZfin, out-of-stock SKUs force zfin=0) and adds the R/D_o pro-rata
            // layer. Only defined for CrossTime (eM1 is an all-or-nothing equality - no
            // partial exists to reward); silently inert otherwise. B=0 keeps the legacy
            // zdonex/w2 path bit-identical.
            bool prMode = _splitConfig != null && _splitConfig.TrueCompletionReward > 0 && _splitConfig.CrossTime;
            double prB = prMode ? _splitConfig.TrueCompletionReward : 0;
            double prR = prMode ? _splitConfig.ProRataReward : 0;
            bool trueCompletionMode = prMode || adaptiveExact;
            double zRewardCoeff = prMode ? -prB : (adaptiveExact ? -Math.Abs(w2) : w2);
            bool sunkFirst = _splitConfig != null && _splitConfig.SunkFirstScoring && crossTime;
            // Pods currently being PROCESSED (bot standing at the station's pick waypoint) - the
            // perishable squeeze targets. Queueing / en-route Pb pods deliberately excluded: their
            // windows stay open for future epochs, the processing pod's window is closing now.
            HashSet<Pod> processingPods = new HashSet<Pod>();
            // (IC) always collected: the Pp/Pq partition and icProcUnits probe need it
            // regardless of w5 (mirror deviation - original gates on w5/adaptive).
            if (true)
                foreach (var station in Cs.Keys)
                    foreach (var pod in Pb)
                        if (station.Waypoint != null && PodToBot.ContainsKey(pod) && PodToBot[pod].CurrentWaypoint != null
                            && PodToBot[pod].CurrentWaypoint.ID == station.Waypoint.ID)
                            processingPods.Add(pod);
            if (adaptiveExact)
            {
                foreach (var station in Cs.Keys)
                {
                    HashSet<Pod> stationPods;
                    inboundPods.TryGetValue(station, out stationPods);
                    bool hasPhysicalPod = stationPods != null && stationPods.Any(p => processingPods.Contains(p));
                    if (hasPhysicalPod)
                        continue;

                    int focusPodId;
                    Pod focusPod = null;
                    if (_adaptiveFocusPodByStationId.TryGetValue(station.ID, out focusPodId) && stationPods != null)
                        focusPod = stationPods.FirstOrDefault(p => p.ID == focusPodId && Pb.Contains(p));
                    if (focusPod == null && adaptiveSelectedCandidates != null)
                    {
                        AdaptivePodCandidate selected = adaptiveSelectedCandidates
                            .FirstOrDefault(c => c.Station != null && c.Station.ID == station.ID);
                        if (selected != null)
                            focusPod = selected.Pod;
                    }
                    if (focusPod != null)
                        processingPods.Add(focusPod);
                }
            }
            // ── (IC/v4) per-station pod-state partition (Pp/Pq/future) + pool scarcity ──
            // Decision-time constants driving the SG gate, the D11 floor, the D15
            // scarcity weights and the strict-gate opportunity-cost probe.
            Dictionary<int, HashSet<int>> icPpByStation = new Dictionary<int, HashSet<int>>();
            Dictionary<int, int> icPqCountByStation = new Dictionary<int, int>();
            Dictionary<int, int> icFutureByStation = new Dictionary<int, int>();
            // (Pod-tier draw cost) which SPECIFIC pods are queued (Pq) at each station - the
            // count above is enough for the SG/D11 gates, but pricing q by tier needs the
            // actual pod IDs, mirroring icPpByStation.
            Dictionary<int, HashSet<int>> icQueuedByStation = new Dictionary<int, HashSet<int>>();
            foreach (var station in Cs.Keys)
            {
                HashSet<int> icProc = new HashSet<int>();
                HashSet<int> icQueuedIds = new HashSet<int>();
                int icQueued = 0;
                int icCommittedHere = 0;
                HashSet<Pod> icStationPods;
                if (inboundPods.TryGetValue(station, out icStationPods))
                    foreach (var pod in icStationPods)
                    {
                        if (!Pb.Contains(pod) || !PodToBot.ContainsKey(pod))
                            continue;
                        icCommittedHere++;
                        var icWayp = PodToBot[pod].CurrentWaypoint;
                        if (icWayp != null && station.Waypoint != null && icWayp.ID == station.Waypoint.ID)
                            icProc.Add(pod.ID);
                        else if (icWayp != null && icWayp.IsQueueWaypoint)
                        {
                            icQueued++;
                            icQueuedIds.Add(pod.ID);
                        }
                    }
                icPpByStation[station.ID] = icProc;
                icPqCountByStation[station.ID] = icQueued;
                icQueuedByStation[station.ID] = icQueuedIds;
                icFutureByStation[station.ID] = icCommittedHere - icProc.Count;
            }
            bool icSgEnabled = _icConfig == null || _icConfig.SplitGateEnabled;
            bool icSgStrict = _icConfig == null || _icConfig.SplitGateStrict;
            double icSgTwilight = _icConfig != null ? _icConfig.SplitGateTwilightSec : 0;
            int icSgT = Math.Max(1, _icConfig != null ? _icConfig.PipelineFloorTarget : 1);
            Dictionary<int, bool> icSgOpenByStation = new Dictionary<int, bool>();
            foreach (var station in Cs.Keys)
                icSgOpenByStation[station.ID] = icSgTwilight > 0
                    ? M2eICMath.SplitGateOpenTwilight(icPpByStation[station.ID].Count > 0,
                        station.GetInfoCurrentPodReleaseLeft(), icSgTwilight,
                        icFutureByStation[station.ID] >= icSgT)
                    : M2eICMath.SplitGateOpen(
                        icPpByStation[station.ID].Count > 0, icPqCountByStation[station.ID], icSgStrict);
            int icSgOpenCount = icSgOpenByStation.Values.Count(v => v);
            // (D15) pool-local scarcity per SKU id
            Dictionary<int, int> icDemById = new Dictionary<int, int>();
            foreach (var icPair in residuals)
                foreach (var icLine in icPair.Value)
                {
                    int icCur;
                    icDemById[icLine.Key.ID] = (icDemById.TryGetValue(icLine.Key.ID, out icCur) ? icCur : 0) + icLine.Value;
                }
            Dictionary<int, int> icSupById = new Dictionary<int, int>();
            foreach (var pod in Pods)
                foreach (var icItem in pod.ItemDescriptionsContained)
                {
                    int icCur;
                    icSupById[icItem.ID] = (icSupById.TryGetValue(icItem.ID, out icCur) ? icCur : 0) + pod.CountAvailable(icItem);
                }
            Dictionary<int, double> icScarcity = new Dictionary<int, double>();
            foreach (var icDem in icDemById)
            {
                int icSupVal;
                icScarcity[icDem.Key] = M2eICMath.PoolScarcity(icDem.Value,
                    icSupById.TryGetValue(icDem.Key, out icSupVal) ? icSupVal : 0);
            }
            // (probe) strict-gate opportunity cost: backlog-matching residual sitting on the
            // processing pods of gate-CLOSED stations (units the strict gate declines to harvest)
            int icGateForegone = 0;
            foreach (var station in Cs.Keys)
            {
                if (!icSgEnabled || icSgOpenByStation[station.ID])
                    continue;
                foreach (var pod in Pods.Where(p => icPpByStation[station.ID].Contains(p.ID)))
                    foreach (var icItem in pod.ItemDescriptionsContained)
                    {
                        int icDemHere;
                        if (icDemById.TryGetValue(icItem.ID, out icDemHere) && icDemHere > 0)
                            icGateForegone += Math.Min(pod.CountAvailable(icItem), icDemHere);
                    }
            }
            // (Scout) anticipatory dispatch precompute: per-station eligibility (D11 lead
            // gate open AND pipeline still short of target) and per-Pa-pod backlog coverage
            // value. 0 weight = both dicts computed but inert (guarded at point of use).
            double icScoutW = _icConfig != null ? _icConfig.AnticipatoryDispatchWeight : 0;
            int icScoutT = Math.Max(1, _icConfig != null ? _icConfig.PipelineFloorTarget : 1);
            double icScoutLead = _icConfig != null ? _icConfig.PipelineFloorLeadSec : 70;
            bool icScoutFloorOn = _icConfig != null && _icConfig.PipelineFloorEnabled;
            Dictionary<int, bool> icScoutGateOpenByStation = new Dictionary<int, bool>();
            Dictionary<int, int> icScoutShortfallByStation = new Dictionary<int, int>();
            foreach (var station in Cs.Keys)
            {
                icScoutGateOpenByStation[station.ID] = icScoutFloorOn && M2eICMath.PipelineGateOpen(
                    icPpByStation[station.ID].Count > 0, station.GetInfoCurrentPodReleaseLeft(), icScoutLead);
                icScoutShortfallByStation[station.ID] = Math.Max(0, icScoutT - icFutureByStation[station.ID]);
            }
            Dictionary<int, double> icScoutValueByPod = new Dictionary<int, double>();
            if (icScoutW != 0)
                foreach (var pod in Pa)
                {
                    double val = 0;
                    foreach (var icItem in pod.ItemDescriptionsContained)
                    {
                        int dem;
                        if (icDemById.TryGetValue(icItem.ID, out dem) && dem > 0)
                            val += Math.Min(pod.CountAvailable(icItem), dem);
                    }
                    icScoutValueByPod[pod.ID] = val;
                }
            // (Scout) per-(pod,station) eligibility, computed once and reused by both the
            // objective reward term and the eshi13' relaxation below.
            Func<int, int, bool> icScoutEligible = (podId, stationId) =>
                icScoutW != 0 && icScoutValueByPod.ContainsKey(podId)
                && M2eICMath.AnticipatoryDispatchOpen(icScoutGateOpenByStation[stationId],
                    icScoutShortfallByStation[stationId], icScoutValueByPod[podId]);
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
                    + LinearExpression.Sum(deVarNamez.Select(v => variablesBinary[v.name])) * zRewardCoeff
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesUs[v.name])) * w3;
            else
                objective = LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (ExactPodStationCost(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation) + w4)), wrapper) * w1
                    + LinearExpression.Sum(deVarNamez.Select(v => variablesBinary[v.name])) * zRewardCoeff
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesUs[v.name])) * w3;
            // (Starvation-aware dispatch) explicit station-starvation reward on xps. Added
            // outside the *w1 scaling as an independent objective term. For each candidate
            // pod->station dispatch whose station is projected to starve (gap>0) and that a
            // free-flow-arriving pod can reach before the station's EST, reward the dispatch by
            // -w_starve*gap so the MILP feeds stations before they idle. Gated: off = M3G MINCORE.
            if (_icConfig != null && _icConfig.StarvationAwareDispatch && _icConfig.StarvationWeight != 0)
            {
                double saW = _icConfig.StarvationWeight;
                double saCfgSpeed = Instance.SettingConfig != null ? Instance.SettingConfig.StarveAwareNominalSpeed : 0.0;
                double saSpeed = saCfgSpeed > 0.0 ? saCfgSpeed : Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity));
                double saNow = Instance.Controller.CurrentTime;
                Dictionary<int, double> saEst = new Dictionary<int, double>();
                Dictionary<int, double> saGap = new Dictionary<int, double>();
                foreach (var saStation in Cs.Keys)
                {
                    var saProj = SlowStartController.ComputeStationWorkProjection(saStation, saNow);
                    saEst[saStation.ID] = saProj.FirstStarveSec;
                    saGap[saStation.ID] = saProj.StarvationGapSec;
                }
                objective = objective + LinearExpression.Sum(deVarNamexps.Where(u =>
                        Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)
                        && saGap.ContainsKey(u.outputstation.ID) && saGap[u.outputstation.ID] > 0.0
                        && StarveAwareCost.TravelTime(EstimatePodStationDistance(u.pod, u.outputstation), saSpeed) <= saEst[u.outputstation.ID])
                    .Select(v => variablesBinary[v.name] * (-saW * saGap[v.outputstation.ID])), wrapper);
            }
            if (w5 != 0 && processingPods.Count > 0)
            {
                // A processing pod may carry nothing the backlog still needs -> no q variables
                // reference it; LinearExpression.Sum throws on an empty sequence, so materialize
                // and guard (the w5 term is simply absent when there is nothing to squeeze).
                var squeezeVars = deVarNameq.Where(v => processingPods.Contains(v.pod)).Select(v => variablesQ[v.name]).ToList();
                if (squeezeVars.Count > 0)
                    objective = objective + LinearExpression.Sum(squeezeVars) * w5;
            }
            // (Pod-tier draw cost) high-to-low preference for WHICH committed pod an order
            // draws from: processing pod stays free (w5 above already prices it separately),
            // a QUEUED pod costs QueuedPodDrawPenalty per unit, anything else not yet arrived
            // (still traveling, or a pod freshly dispatched this same solve out of Pa) costs
            // OnTheWayPodDrawPenalty per unit. Soft preference, not a ban - a big enough
            // completion reward can still outweigh it, so this can never make the model
            // infeasible (unlike a hard pod-state gate). Both 0 = bit-identical.
            double icQueuedPen = _icConfig != null ? _icConfig.QueuedPodDrawPenalty : 0;
            double icOnTheWayPen = _icConfig != null ? _icConfig.OnTheWayPodDrawPenalty : 0;
            // (Set-level redesign) 4th tier: a brand-new Pa pod serving a partial costs
            // NewPodPartialPenalty (gamma). Only when SoftInboundCommitted - else Pa pods
            // keep falling into icOnTheWayPen exactly as before (bit-identical; and P1
            // pins their q to 0 anyway when the gate is on).
            bool icSoftInbound = _icConfig != null && _icConfig.SoftInboundCommitted;
            double icNewPodPen = icSoftInbound ? _icConfig.NewPodPartialPenalty : 0;
            if ((icQueuedPen != 0 || icOnTheWayPen != 0 || icNewPodPen != 0) && deVarNameq.Count > 0)
            {
                var icTierTerms = new List<LinearExpression>();
                foreach (var v in deVarNameq)
                {
                    HashSet<int> icProcHere, icQueuedHere;
                    bool icIsProcessing = icPpByStation.TryGetValue(v.outputstation.ID, out icProcHere)
                        && icProcHere.Contains(v.pod.ID);
                    if (icIsProcessing)
                        continue;
                    bool icIsQueued = icQueuedByStation.TryGetValue(v.outputstation.ID, out icQueuedHere)
                        && icQueuedHere.Contains(v.pod.ID);
                    double icPen = (icSoftInbound && Pa.Contains(v.pod)) ? icNewPodPen
                        : (icIsQueued ? icQueuedPen : icOnTheWayPen);
                    if (icPen != 0)
                        icTierTerms.Add(variablesQ[v.name] * icPen);
                }
                if (icTierTerms.Count > 0)
                    objective = objective + LinearExpression.Sum(icTierTerms);
            }
            // (eps) lexicographic item-pile-on layer: every assigned unit earns eps (negative
            // = reward), phase-blind - among completion-equivalent solutions the solver now
            // systematically prefers drawing more items per committed pod. |eps| << |w2|, so
            // it can never trade away a completion. Guarded so eps=0 stays bit-identical
            // (LinearExpression.Sum throws on an empty sequence).
            double eps = _splitConfig != null ? _splitConfig.UnitDrawReward : 0;
            // (IC/D15) scarcity-weighted squeeze: eps * poolScarcity[i] per drawn unit.
            // P1d + icSG2 cap the dose structurally (partial draws are Pp-only), so unlike
            // the flat eps arm (TP 623) no fishing trip can ever be provoked by this term.
            if (eps != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v =>
                    variablesQ[v.name] * (eps * (icScarcity.ContainsKey(v.skui.ID) ? icScarcity[v.skui.ID] : 0.0))), wrapper);
            // (PR) pro-rata layer: every assigned unit of order o earns R/D_o (negative =
            // reward under Minimize). D_o = the order's ORIGINAL overall demand
            // (GetDemandCount(), includes currently out-of-stock SKUs) so slice rewards
            // across epochs sum to exactly R (spec section 3.4) - a remaining-demand
            // denominator would let every epoch's slice count from 100% and resurrect the
            // slice arbitrage. Guarded like eps: no q variables = term absent
            // (LinearExpression.Sum throws on an empty sequence).
            if (prMode && prR != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name] * (-prR / v.order.GetDemandCount())));
            // (Set-level redesign) order-level LINEAR progress reward: each assigned unit of
            // order o earns -ProgressRewardWeight / D_o (D_o = original demand GetDemandCount,
            // so per-epoch slices sum to the reward over the order's life). Mirrors the PR
            // term's formula but decoupled from prMode/true-completion, and gated by the
            // SoftInboundCommitted master switch => flag off is a whole-block skip.
            if (icSoftInbound && _icConfig.ProgressRewardWeight != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v =>
                    variablesQ[v.name] * (-_icConfig.ProgressRewardWeight / v.order.GetDemandCount())));
            // ── (IC/v4) objective additions: D9 wp, D11 pipeline floor, D14 coverage ──
            double icWpPen = _icConfig != null ? _icConfig.MultiPartPenalty : 0;
            if (icWpPen != 0 && pendingOrders.Count > 0)
                objective = objective + LinearExpression.Sum(pendingOrders.OrderBy(o => o.ID)
                    .Select(o => variablesUs["icepx_" + o.ID.ToString()])) * icWpPen;
            // (w2p) parent-closing bonus: completing an existing split parent closes a
            // consolidation tail (and frees a packing box), so its zdone earns w2 + w2p.
            // MILP counterpart of PVGS's ParentClosingBonus - completion-targeted, never
            // rewards partial draws.
            double icW2p = _icConfig != null ? _icConfig.ParentClosingReward : 0;
            if (icW2p != 0)
            {
                var icParentZ = deVarNamez.Where(v => v.order.IsSplitParent)
                    .Select(v => variablesBinary[v.name]).ToList();
                if (icParentZ.Count > 0)
                    objective = objective - LinearExpression.Sum(icParentZ) * icW2p;
            }
            // (Scout) anticipatory dispatch reward: -w_scout * coverage for a NEW Pa pod at a
            // station whose D11 gate is open and pipeline is short - the ONLY thing that can
            // make x=1 attractive without an order consuming it this round (eshi13' relaxed
            // below for exactly these pairs). 0 weight = no term (bit-identical).
            if (icScoutW != 0)
            {
                var icScoutTerms = deVarNamexps.Where(v => Pa.Contains(v.pod)
                    && icScoutEligible(v.pod.ID, v.outputstation.ID))
                    .Select(v => variablesBinary[v.name] * (-icScoutW * icScoutValueByPod[v.pod.ID])).ToList();
                if (icScoutTerms.Count > 0)
                    objective = objective + LinearExpression.Sum(icScoutTerms);
            }
            List<string> icShortfallNames = new List<string>();
            bool icFloorOn = _icConfig != null && _icConfig.PipelineFloorEnabled && _icConfig.PipelineFloorWeight != 0;
            if (icFloorOn)
            {
                int icT = Math.Max(1, _icConfig.PipelineFloorTarget);
                foreach (var station in Cs.Keys)
                {
                    var icNewXps = deVarNamexps.Where(v => v.outputstation.ID == station.ID && Pa.Contains(v.pod))
                        .Select(v => variablesBinary[v.name]).ToList();
                    int icFuture = icFutureByStation[station.ID];
                    // (icLGcap) hard anti-oversupply: future + new <= T (AE target=3 counterexample).
                    // (No-artificial-cap) skippable: eshi13' still requires genuine order
                    // justification for every new dispatch either way - this only removes the
                    // ARTIFICIAL per-round count ceiling, matching M1G (which has none).
                    if (icNewXps.Count > 0 && (_icConfig == null || _icConfig.DispatchCapEnabled))
                        wrapper.AddConstr(LinearExpression.Sum(icNewXps) <= Math.Max(0, icT - icFuture), "icLGcap");
                    // (icLG1) soft floor while the lead gate is open: dispatch is clock-driven,
                    // not exhaustion-driven - squeeze and dispatch coexist in one solve.
                    bool icGateOpen = M2eICMath.PipelineGateOpen(icPpByStation[station.ID].Count > 0,
                        station.GetInfoCurrentPodReleaseLeft(), _icConfig.PipelineFloorLeadSec);
                    // (Squeeze-coupled dispatch) also open the moment squeezing is already
                    // permitted at this station - closes the "already squeezing but supply
                    // hasn't been told yet" gap between the two independent gates.
                    if (_icConfig != null && _icConfig.CoupleDispatchToSqueezeWindow && icSgOpenByStation[station.ID])
                        icGateOpen = true;
                    // (Split-fed dispatch) also open if the LAST completed decode actually
                    // assigned a split at this station - a realized, whole-first-confirmed
                    // fact, one epoch lagged to avoid circularity.
                    if (_icConfig != null && _icConfig.CoupleDispatchToRecentSplit && _icPrevSplitStations.Contains(station.ID))
                        icGateOpen = true;
                    // (Remove soft floor) skip icLG1 entirely when disabled; icLGcap above stays.
                    if (_icConfig != null && _icConfig.PipelineSoftFloorDisabled)
                        continue;
                    if (!icGateOpen || icT - icFuture <= 0)
                        continue;
                    string icSfName = "icsfx_" + station.ID.ToString();
                    icShortfallNames.Add(icSfName);
                    if (icNewXps.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(icNewXps) + variablesUs[icSfName]
                            >= icT - icFuture, "icLG1");
                    else
                        wrapper.AddConstr(variablesUs[icSfName] >= icT - icFuture, "icLG1");
                }
                if (icShortfallNames.Count > 0)
                    objective = objective + LinearExpression.Sum(icShortfallNames.Select(n => variablesUs[n])) * _icConfig.PipelineFloorWeight;
            }
            // (Force-feed constraint) HARD, no penalty: a station that will starve within the
            // planning horizon and CAN still be reached by a storage pod before its EST MUST get
            // one such timely dispatch. Re-couples supply to station work (M1G-analog for split)
            // without any objective term. Feasibility-bounded: only added when a timely candidate
            // exists, so it can never make the model infeasible.
            if (_icConfig != null && _icConfig.ForceFeedConstraint)
            {
                double ffSpeed0 = Instance.SettingConfig != null ? Instance.SettingConfig.StarveAwareNominalSpeed : 0.0;
                double ffSpeed = ffSpeed0 > 0.0 ? ffSpeed0 : Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity));
                double ffNow = Instance.Controller.CurrentTime;
                double ffHorizon = _icConfig.ForceFeedHorizonSec;
                foreach (var ffStation in Cs.Keys)
                {
                    double ffEst = SlowStartController.ComputeStationWorkProjection(ffStation, ffNow).FirstStarveSec;
                    if (!(ffEst > 0.0) || ffEst > ffHorizon)
                        continue;
                    var ffTimely = deVarNamexps.Where(v => v.outputstation.ID == ffStation.ID && Pa.Contains(v.pod)
                            && Instance.ResourceManager.UnusedPods.Contains(v.pod)
                            && StarveAwareCost.TravelTime(EstimatePodStationDistance(v.pod, ffStation), ffSpeed) <= ffEst)
                        .Select(v => variablesBinary[v.name]).ToList();
                    if (ffTimely.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(ffTimely) >= 1, "icFeed");
                }
            }
            double icCov = _icConfig != null ? _icConfig.CoverageRewardWeight : 0;
            if (icCov != 0)
            {
                // (IC/D14) selected-pod-set residual coverage as a SMALL additive tie-break
                // (not CF's failed lexicographic stage): c_i <= pool demand, c_i <= selected
                // supply. Among equal-completion pod sets total coverage differs exactly by
                // the leftover coverage - the residual-SKU sizing tie-break, no subtraction.
                VariableCollection<string> icCovVars = new VariableCollection<string>(wrapper, VariableType.Continuous, 0, double.PositiveInfinity, (string s) => { return s; });
                List<string> icCovNames = new List<string>();
                foreach (var icSku in OiSKU.Keys.Where(k => PiSKU.ContainsKey(k)))
                {
                    int icDemandHere = 0;
                    foreach (var icPair in residuals)
                    {
                        int icLine;
                        if (icPair.Value.TryGetValue(icSku, out icLine))
                            icDemandHere += icLine;
                    }
                    if (icDemandHere <= 0)
                        continue;
                    var icSupplyTerms = deVarNamexps.Where(v => v.pod.CountAvailable(icSku) > 0)
                        .Select(v => variablesBinary[v.name] * (double)v.pod.CountAvailable(icSku)).ToList();
                    if (icSupplyTerms.Count == 0)
                        continue;
                    string icCn = "iccov_" + icSku.ID.ToString();
                    icCovNames.Add(icCn);
                    wrapper.AddConstr(icCovVars[icCn] <= icDemandHere, "iccovd");
                    wrapper.AddConstr(icCovVars[icCn] <= LinearExpression.Sum(icSupplyTerms), "iccovs");
                }
                if (icCovNames.Count > 0)
                    objective = objective + LinearExpression.Sum(icCovNames.Select(n => icCovVars[n])) * icCov;
            }
            // (CF) coverage-first lexicographic mode: build the Solve-1 objective
            // (completions + beta * whole-backlog pool coverage - epsS * new trips, NO
            // distance) and its pool-coverage variables. The demand pool deliberately
            // uses the FIRST-stage admission over the full backlog (Od shrink does not
            // apply - the pool is about supply value, not slot eligibility). Constraints
            // of the model are untouched; c_i is capped by pool demand and by the
            // selected pods' stock, so it measures what the CHOSEN SET can cover.
            bool coverageFirst = _splitConfig != null && _splitConfig.CoverageFirstScoring;
            double betaPool = _splitConfig != null ? _splitConfig.PoolCoverWeight : 0;
            double epsPod = _splitConfig != null ? _splitConfig.PodSelectTiebreakCost : 0;
            if (sunkFirst && coverageFirst)
                throw new InvalidOperationException("SunkFirstScoring and CoverageFirstScoring cannot be enabled together.");
            if (adaptiveExact && (sunkFirst || coverageFirst))
                throw new InvalidOperationException("AdaptiveExactResweeps cannot be combined with SunkFirstScoring or CoverageFirstScoring.");
            VariableCollection<string> variablesPool = null;
            LinearExpression coverageObjective = null;
            if (coverageFirst && deVarNamez.Count > 0)
            {
                bool cfCrossTime = _splitConfig != null && _splitConfig.CrossTime;
                Dictionary<ItemDescription, int> poolDemand = new Dictionary<ItemDescription, int>();
                foreach (var order in _pendingOrders)
                {
                    if (cfCrossTime
                        ? !order.RemainingPositions.Any(p => Instance.StockInfo.GetActualStock(p.Key) >= 1)
                        : !order.RemainingPositions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value))
                        continue;
                    foreach (var pos in order.RemainingPositions)
                    {
                        int cur;
                        poolDemand[pos.Key] = (poolDemand.TryGetValue(pos.Key, out cur) ? cur : 0) + pos.Value;
                    }
                }
                List<ItemDescription> poolSkus = poolDemand.Keys.Where(k => PiSKU.ContainsKey(k)).ToList();
                variablesPool = new VariableCollection<string>(wrapper, VariableType.Continuous, 0, double.PositiveInfinity, (string s) => { return s; });
                foreach (var sku in poolSkus)
                {
                    string cname = "cpool_" + sku.ID.ToString();
                    wrapper.AddConstr(variablesPool[cname] <= poolDemand[sku], "cfcap");
                    var supplyTerms = deVarNamexps.Where(v => v.pod.CountAvailable(sku) > 0)
                        .Select(v => variablesBinary[v.name] * (double)v.pod.CountAvailable(sku)).ToList();
                    if (supplyTerms.Count > 0)
                        wrapper.AddConstr(variablesPool[cname] <= LinearExpression.Sum(supplyTerms), "cfsup");
                    else
                        wrapper.AddConstr(variablesPool[cname] <= 0, "cfsup");
                }
                coverageObjective = LinearExpression.Sum(deVarNamez.Select(v => variablesBinary[v.name]));
                if (poolSkus.Count > 0)
                    coverageObjective = coverageObjective
                        + LinearExpression.Sum(poolSkus.Select(k => variablesPool["cpool_" + k.ID.ToString()])) * betaPool;
                var cfNewTrips = deVarNamexps.Where(v => Pa.Contains(v.pod)).Select(v => variablesBinary[v.name]).ToList();
                if (cfNewTrips.Count > 0)
                    coverageObjective = coverageObjective - LinearExpression.Sum(cfNewTrips) * epsPod;
            }
            // (Line-closure reward) MILP counterpart of HGS's lexicographic "close order-lines"
            // tier. Per order-line (o, SKU i): c[o,i]=1 requires the full SKU demand served
            // (Σ_s q[o,i,s] >= demand·c); reward -W_line·Σc so the objective values a finished
            // SKU-position above raw item coverage (which is NOT rewarded, to avoid flooding).
            // Keep |w2| ≫ W_line ≫ distance for near-strict lexicographic. 0 = off (bit-identical).
            if (_icConfig != null && _icConfig.LineClosureWeight != 0)
            {
                double icLineW = _icConfig.LineClosureWeight;
                var icLineVars = new List<Symbol>();
                foreach (var order in pendingOrders)
                    foreach (var sku in residuals[order].Where(p => PiSKU.ContainsKey(p.Key) && p.Value > 0))
                    {
                        string icCname = "cline_" + order.ID.ToString() + "_" + sku.Key.ID.ToString();
                        var icLineLhs = LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID).Select(v => variablesQ[v.name]));
                        wrapper.AddConstr(icLineLhs >= sku.Value * variablesBinary[icCname], "icLine");
                        icLineVars.Add(new Symbol { name = icCname });
                    }
                if (icLineVars.Count > 0)
                    objective = objective + LinearExpression.Sum(icLineVars.Select(v => variablesBinary[v.name])) * (-icLineW);
            }
            wrapper.SetObjective(objective, OptimizationSense.Minimize);
            // (ecap) sequential trip discipline: at most K new pod trips per decision. Restores
            // the one-move-at-a-time cadence (the greedy's structural advantage) while keeping
            // each move jointly optimal. Skipped at K=0 (unlimited, bit-identical default).
            int tripCap = adaptiveExact ? 1 : (_splitConfig != null ? _splitConfig.MaxNewPodTripsPerDecision : 0);
            var newTripVarsForCap = deVarNamexps.Where(v => Pa.Contains(v.pod)).Select(v => variablesBinary[v.name]).ToList();
            if (adaptiveExact && adaptiveSelectedCandidates != null && adaptiveSelectedCandidates.Count > 0)
            {
                HashSet<string> selectedPairs = new HashSet<string>(adaptiveSelectedCandidates.Select(c =>
                    c.Pod.ID.ToString() + ":" + c.Station.ID.ToString()));
                foreach (var x in deVarNamexps.Where(v => Pa.Contains(v.pod)))
                {
                    bool selected = selectedPairs.Contains(x.pod.ID.ToString() + ":" + x.outputstation.ID.ToString());
                    wrapper.AddConstr(variablesBinary[x.name] == (selected ? 1 : 0), "aeSelectedPodStation");
                }
                foreach (var selected in adaptiveSelectedCandidates)
                {
                    var selectedPodQ = deVarNameq.Where(v => v.pod.ID == selected.Pod.ID
                        && v.outputstation.ID == selected.Station.ID)
                        .Select(v => variablesQ[v.name]).ToList();
                    if (selectedPodQ.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(selectedPodQ) >= 1, "aeSelectedPodSeedItem");
                    else
                        wrapper.AddConstr(variablesBinary["xps_" + selected.Pod.ID.ToString() + "_"
                            + selected.Station.ID.ToString()] == 0, "aeSelectedPodNoDemand");
                }
            }
            else if (adaptiveExact && newTripVarsForCap.Count > 0)
            {
                wrapper.AddConstr(LinearExpression.Sum(newTripVarsForCap) == 0, "aePbPhase");
            }
            else if (tripCap > 0 && newTripVarsForCap.Count > 0)
            {
                wrapper.AddConstr(LinearExpression.Sum(newTripVarsForCap) <= tripCap, "ecap");
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
            // not the dops proxy Spec 2 used (see spec Â§3.3/Â§7: dops never linked to real q usage).
            // (Scout) relaxed for a (pod,station) pair the anticipatory-dispatch reward term
            // already made eligible above: that trip is justified by backlog coverage, not
            // this-round consumption - the order binds honestly whenever a future epoch
            // actually claims the pod, under the unchanged P1/SG rules.
            // (Value-dispatch) when NewPodDispatchByValue is on, this constraint is dropped
            // entirely for every Pa pod: dispatch is justified by the D14 coverage term in the
            // objective instead of by this-round consumption. P1/SG are untouched (they gate
            // WHICH orders may draw on a pod, not whether the pod may be fetched at all), so a
            // fresh split order still cannot draw on a Pa pod - only a whole/closing-parent can.
            bool icValueDispatch = _icConfig != null && _icConfig.NewPodDispatchByValue;
            foreach (var pod in Pa)
            {
                foreach (var station in Cs.Keys)
                {
                    if (icValueDispatch || icScoutEligible(pod.ID, station.ID))
                        continue;
                    wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()]
                        <= LinearExpression.Sum(deVarNameq.Where(v => v.pod.ID == pod.ID && v.outputstation.ID == station.ID).Select(v => variablesQ[v.name])), "eshi13");
                }
            }
            // (eM1/eM2/eM2done | eZfin) per-SKU completion linkage, aggregated across stations AND pods
            foreach (var order in pendingOrders)
            {
                string zname = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                if (trueCompletionMode)
                {
                    // (eZfin) true-completion semantics (spec section 3.3): iterate the FULL
                    // remaining demand - no PiSKU visibility filter. An out-of-stock SKU has
                    // no q variables (LHS identically 0), so instead of expanding phantom
                    // variables one constant constraint zfin<=0 encodes it exactly. RHS stays
                    // the REMAINING demand (r_rem): an original-demand RHS could never be met
                    // for previously-split orders (their earlier q are already committed) and
                    // would render the completion bonus unreachable (spec section 3.4).
                    foreach (var sku in residuals[order])
                    {
                        if (!PiSKU.ContainsKey(sku.Key))
                        {
                            if (sku.Value > 0)
                                wrapper.AddConstr(variablesBinary[zname] <= 0, "eZfinOOS");
                            continue;
                        }
                        var lhs = LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID).Select(v => variablesQ[v.name]));
                        wrapper.AddConstr(lhs <= sku.Value, "eM2");
                        wrapper.AddConstr(lhs >= sku.Value * variablesBinary[zname], "eZfin");
                    }
                }
                else
                {
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
            }
            // ── (IC/P1) inbound-committed gate ─────────────────────────────────────────
            // An order drawing ANY unit from a storage-area pod (Pa) must be WHOLE:
            // completed this solve (icP1a), at most one station (icP1b, big-M form),
            // not an existing split parent and no out-of-stock residual (icP1c/icP1oos -
            // legacy zdonex skips OOS SKUs, so without the oos leg a "complete" order
            // could decode into split children bound to a Pa pod).
            List<string> icWholeVarNames = new List<string>();
            int icStationCount = Cs.Count;
            foreach (var order in pendingOrders.OrderBy(o => o.ID))
            {
                string icWname = "icwholex_" + order.ID.ToString();
                string icZname = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                icWholeVarNames.Add(icWname);
                bool icHasInvisibleResidual = residuals[order].Any(p => p.Value > 0 && !PiSKU.ContainsKey(p.Key));
                if (!M2eICMath.IsWholeEligible(order.IsSplitParent, icHasInvisibleResidual))
                {
                    wrapper.AddConstr(variablesBinary[icWname] <= 0, "icP1c");
                }
                else
                {
                    wrapper.AddConstr(variablesBinary[icWname] <= variablesBinary[icZname], "icP1a");
                    var icOrderY = deVarNamey.Where(v => v.order.ID == order.ID)
                        .Select(v => variablesBinary[v.name]).ToList();
                    if (icOrderY.Count > 0 && icStationCount > 1)
                        wrapper.AddConstr(LinearExpression.Sum(icOrderY)
                            + (icStationCount - 1) * variablesBinary[icWname] <= icStationCount, "icP1b");
                }
                var icPaQ = deVarNameq.Where(v => v.order.ID == order.ID && Pa.Contains(v.pod))
                    .Select(v => variablesQ[v.name]).ToList();
                if (icPaQ.Count > 0 && !(_icConfig != null && _icConfig.SoftInboundCommitted))
                {
                    // (IC/close) parent closing-only dispatch: an existing split parent may
                    // draw from storage pods ONLY when the draw closes it entirely this
                    // solve (zdone-gated) - a dedicated tail-ending trip. Partial fishing
                    // from Pa stays impossible (z=0 -> zero Pa draws). Fresh orders keep
                    // the whole-gate unchanged. OOS-residual parents are excluded: legacy
                    // zdonex skips out-of-stock SKUs, so their z=1 is NOT a true close and
                    // the decode P1 assert (full-residual semantics) rightly rejects the
                    // draw (8h seed0 crash at t=13352, order 684).
                    bool icParentClose = _icConfig != null && _icConfig.ParentClosingDispatch
                        && order.IsSplitParent && !icHasInvisibleResidual;
                    // (Split-driven dispatch) a completing split may also summon a fresh Pa
                    // trip - gate switches to zdone for fresh orders too. OOS-residual orders
                    // excluded (zdonex skips OOS SKUs, so their z=1 is not a true close).
                    bool icSplitDrive = _icConfig != null && _icConfig.SplitCanDriveDispatch
                        && !icHasInvisibleResidual;
                    wrapper.AddConstr(LinearExpression.Sum(icPaQ)
                        <= M2eICMath.PaDrawBigM(residuals[order].Values)
                        * variablesBinary[(icParentClose || icSplitDrive) ? icZname : icWname], "icP1d");
                }
            }
            // ── (IC/SG) split gate ─────────────────────────────────────────────────────
            // Gate-closed stations: fresh orders may take a slot only if they COMPLETE this
            // solve (whole or multi-station completion); new partials are bridge actions for
            // a dying processing pod. Existing parents exempt (finishing them shrinks WIP).
            // (SG2) fresh partials additionally draw from the station's PROCESSING pod only
            // - a bridge must be pickable NOW (Pa is already gated by icP1d).
            if (icSgEnabled && !(_icConfig != null && _icConfig.SoftInboundCommitted))
            {
                foreach (var y in deVarNamey)
                {
                    if (y.order.IsSplitParent || icSgOpenByStation[y.outputstation.ID])
                        continue;
                    string icZn = (crossTime ? "zdonex" : "zfullx") + "_" + y.order.ID.ToString();
                    wrapper.AddConstr(variablesBinary[y.name] <= variablesBinary[icZn], "icSG1");
                }
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    if (order.IsSplitParent)
                        continue;
                    var icNonPpQ = deVarNameq.Where(v => v.order.ID == order.ID && Pb.Contains(v.pod)
                        && !icPpByStation[v.outputstation.ID].Contains(v.pod.ID))
                        .Select(v => variablesQ[v.name]).ToList();
                    if (icNonPpQ.Count > 0)
                    {
                        string icZn = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                        wrapper.AddConstr(LinearExpression.Sum(icNonPpQ)
                            <= M2eICMath.PaDrawBigM(residuals[order].Values) * variablesBinary[icZn], "icSG2");
                    }
                }
            }
            // ── (IC/D9) multi-part linearization: sum_s ysp <= ep + 1 ──────────────────
            if (_icConfig != null && _icConfig.MultiPartPenalty != 0)
            {
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    var icOrderY = deVarNamey.Where(v => v.order.ID == order.ID)
                        .Select(v => variablesBinary[v.name]).ToList();
                    if (icOrderY.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(icOrderY)
                            <= variablesUs["icepx_" + order.ID.ToString()] + 1, "icD9");
                }
            }
            // ── (IC/PK) downstream packing budget (Xie Appx B; one box per split parent,
            // reserved at decode, released at consolidation; <=0 disables the block) ──
            int icPackCapacity = _icConfig != null
                ? M2eICMath.TotalPackingCapacity(_icConfig.PackingStationCount, _icConfig.PackingBufferCapacity) : 0;
            if (icPackCapacity > 0)
            {
                int icBOcc = Instance.PackingBuffer != null ? Instance.PackingBuffer.AliveParentCount : 0;
                List<string> icYpackNames = new List<string>();
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    if (order.IsSplitParent)
                        continue; // box already reserved at its first split (inside icBOcc)
                    string icPname = "icypackx_" + order.ID.ToString();
                    string icWn = "icwholex_" + order.ID.ToString();
                    icYpackNames.Add(icPname);
                    var icPkY = deVarNamey.Where(v => v.order.ID == order.ID).ToList();
                    foreach (var y in icPkY)
                        wrapper.AddConstr(variablesBinary[icPname] + variablesBinary[icWn]
                            >= variablesBinary[y.name], "icPK1a");
                    if (icPkY.Count > 0)
                        wrapper.AddConstr(variablesBinary[icPname]
                            <= LinearExpression.Sum(icPkY.Select(v => variablesBinary[v.name])), "icPK1b");
                    wrapper.AddConstr(variablesBinary[icPname] + variablesBinary[icWn] <= 1, "icPK1c");
                }
                if (icYpackNames.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(icYpackNames.Select(n => variablesBinary[n]))
                        <= M2eICMath.PackingBudget(icPackCapacity, icBOcc), "icPK2");
            }
            // Order-first control: the normal pass admits only z=1 complete residuals. A
            // second pass is opened only when that pass cannot complete an order; it may
            // create at most one z=0 child, preventing item pile-on from spraying partial
            // claims across many parents.
            if (adaptiveExact)
            {
                List<string> adaptivePartialVarNames = new List<string>();
                foreach (var order in pendingOrders)
                {
                    string zname = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                    var orderQ = deVarNameq.Where(v => v.order.ID == order.ID)
                        .Select(v => variablesQ[v.name]).ToList();
                    if (orderQ.Count == 0)
                        continue;
                    int residualUnits = residuals[order].Values.Sum();
                    if (!adaptiveAllowPartial)
                        wrapper.AddConstr(LinearExpression.Sum(orderQ)
                            <= residualUnits * variablesBinary[zname], "aeCompleteOnly");
                    else
                    {
                        string partialName = "aePartialOrder_" + order.ID.ToString();
                        adaptivePartialVarNames.Add(partialName);
                        wrapper.AddConstr(LinearExpression.Sum(orderQ) <= residualUnits
                            * (variablesBinary[zname] + variablesBinary[partialName]), "aePartialLink");
                        wrapper.AddConstr(variablesBinary[partialName] + variablesBinary[zname] <= 1,
                            "aePartialNotComplete");
                        wrapper.AddConstr(variablesBinary[partialName] <= LinearExpression.Sum(orderQ),
                            "aePartialPositive");
                    }
                }
                if (adaptivePartialVarNames.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(adaptivePartialVarNames
                        .Select(name => variablesBinary[name])) <= 1, "aeOnePartialOrder");
            }
            // (SF) h[o]=1 only when every residual SKU of o is supplied entirely by inherited
            // Pb pods. Unlike legacy eM2done, this walks the complete residual ledger and forces
            // h=0 when a residual line has no Pb q variable. New Pa supply cannot contribute.
            Dictionary<Order, string> sunkVarNames = null;
            LinearExpression sunkOrdersExpression = null;
            LinearExpression sunkItemsExpression = null;
            LinearExpression sunkObjective = null;
            if (sunkFirst)
            {
                sunkVarNames = new Dictionary<Order, string>();
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    string hname = "hsunkx_" + order.ID.ToString();
                    sunkVarNames.Add(order, hname);
                    foreach (var sku in residuals[order])
                    {
                        var sunkQ = deVarNameq
                            .Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID && Pb.Contains(v.pod))
                            .Select(v => variablesQ[v.name]).ToList();
                        if (sunkQ.Count == 0)
                            wrapper.AddConstr(variablesBinary[hname] <= 0, "sfOOS");
                        else
                            wrapper.AddConstr(LinearExpression.Sum(sunkQ) >= sku.Value * variablesBinary[hname], "sfComplete");
                    }
                }

                sunkOrdersExpression = LinearExpression.Sum(sunkVarNames.Values.Select(name => variablesBinary[name]));
                var sunkItemTerms = sunkVarNames.Select(pair => variablesBinary[pair.Value] *
                    (double)residuals[pair.Key].Values.Sum()).ToList();
                sunkItemsExpression = LinearExpression.Sum(sunkItemTerms, wrapper);
                int dominanceMultiplier = M2eSunkFirstMath.DominanceMultiplier(
                    pendingOrders.Select(order => residuals[order].Values.Sum()));
                sunkObjective = sunkOrdersExpression * dominanceMultiplier + sunkItemsExpression;
            }
            LinearExpression adaptiveCompletionExpression = null;
            LinearExpression adaptiveEfficientCompletionExpression = null;
            LinearExpression adaptiveTripExpression = null;
            LinearExpression adaptiveFocusOrderExpression = null;
            LinearExpression adaptiveNewPodOrderExpression = null;
            LinearExpression adaptiveUrgencyExpression = null;
            LinearExpression adaptivePartExpression = null;
            LinearExpression adaptiveNewPodExpression = null;
            LinearExpression adaptiveFocusUnitExpression = null;
            LinearExpression adaptiveUnitExpression = null;
            LinearExpression adaptiveHierarchyObjective = null;
            LinearExpression adaptiveReplenishmentObjective = null;
            List<Symbol> adaptiveTripSymbols = null;
            List<Symbol> adaptiveNewPodSymbols = null;
            List<Symbol> adaptiveProcessingSymbols = null;
            List<Symbol> adaptiveFocusSymbols = null;
            List<string> adaptiveNewPodOrderVarNames = null;
            List<string> adaptiveProcessingOrderVarNames = null;
            List<string> adaptiveFocusOrderVarNames = null;
            Dictionary<int, int> adaptiveUrgencyByOrderId = null;
            if (adaptiveExact)
            {
                adaptiveTripSymbols = deVarNamexps.Where(v => Pa.Contains(v.pod)).ToList();
                adaptiveNewPodSymbols = deVarNameq.Where(v => Pa.Contains(v.pod)).ToList();
                adaptiveProcessingSymbols = deVarNameq.Where(v => processingPods.Contains(v.pod)).ToList();
                adaptiveFocusSymbols = adaptiveProcessingSymbols.ToList();
                adaptiveNewPodOrderVarNames = new List<string>();
                adaptiveProcessingOrderVarNames = new List<string>();
                adaptiveFocusOrderVarNames = new List<string>();
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    int residualUnits = residuals[order].Values.Sum();
                    var newPodQ = adaptiveNewPodSymbols.Where(v => v.order.ID == order.ID)
                        .Select(v => variablesQ[v.name]).ToList();
                    if (newPodQ.Count > 0)
                    {
                        string name = "aeNewPodOrder_" + order.ID.ToString();
                        string zname = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                        adaptiveNewPodOrderVarNames.Add(name);
                        wrapper.AddConstr(LinearExpression.Sum(newPodQ) >= variablesBinary[name], "aeNewPodOrderLb");
                        wrapper.AddConstr(variablesBinary[name] <= variablesBinary[zname], "aeNewPodOrderCompleted");
                        wrapper.AddConstr(LinearExpression.Sum(newPodQ) + residualUnits * variablesBinary[zname]
                            <= residualUnits * variablesBinary[name] + residualUnits, "aeNewPodOrderUb");
                    }
                    var processingQ = adaptiveProcessingSymbols.Where(v => v.order.ID == order.ID)
                        .Select(v => variablesQ[v.name]).ToList();
                    if (processingQ.Count > 0)
                    {
                        string name = "aeProcessingOrder_" + order.ID.ToString();
                        adaptiveProcessingOrderVarNames.Add(name);
                        wrapper.AddConstr(LinearExpression.Sum(processingQ) >= variablesBinary[name], "aeProcessingOrderLb");
                        wrapper.AddConstr(LinearExpression.Sum(processingQ) <= residualUnits * variablesBinary[name], "aeProcessingOrderUb");
                    }
                    var focusQ = adaptiveFocusSymbols.Where(v => v.order.ID == order.ID)
                        .Select(v => variablesQ[v.name]).ToList();
                    if (focusQ.Count > 0)
                    {
                        string name = "aeFocusOrder_" + order.ID.ToString();
                        string zname = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                        adaptiveFocusOrderVarNames.Add(name);
                        wrapper.AddConstr(LinearExpression.Sum(focusQ) >= variablesBinary[name], "aeFocusOrderLb");
                        wrapper.AddConstr(variablesBinary[name] <= variablesBinary[zname], "aeFocusOrderCompleted");
                        wrapper.AddConstr(LinearExpression.Sum(focusQ) + residualUnits * variablesBinary[zname]
                            <= residualUnits * variablesBinary[name] + residualUnits, "aeFocusOrderUb");
                    }
                }
                List<Order> urgencyOrder = pendingOrders.OrderBy(o => o.Timestay)
                    .ThenBy(o => o.DueTime).ThenBy(o => o.ID).ToList();
                adaptiveUrgencyByOrderId = urgencyOrder.Select((order, index) => new { order.ID, Priority = urgencyOrder.Count - index })
                    .ToDictionary(v => v.ID, v => v.Priority);
                adaptiveCompletionExpression = LinearExpression.Sum(deVarNamez.Select(v => variablesBinary[v.name]));
                adaptiveUrgencyExpression = LinearExpression.Sum(deVarNamez
                    .Select(v => variablesBinary[v.name] * (double)adaptiveUrgencyByOrderId[v.order.ID]), wrapper);
                adaptivePartExpression = LinearExpression.Sum(deVarNamey.Select(v => variablesBinary[v.name]));
                adaptiveEfficientCompletionExpression = adaptiveCompletionExpression * 2
                    - adaptivePartExpression;
                adaptiveUnitExpression = LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name]));
                if (adaptiveTripSymbols.Count > 0)
                    adaptiveTripExpression = LinearExpression.Sum(adaptiveTripSymbols.Select(v => variablesBinary[v.name]));
                if (adaptiveNewPodSymbols.Count > 0)
                    adaptiveNewPodExpression = LinearExpression.Sum(adaptiveNewPodSymbols.Select(v => variablesQ[v.name]));
                if (adaptiveFocusSymbols.Count > 0)
                    adaptiveFocusUnitExpression = LinearExpression.Sum(adaptiveFocusSymbols.Select(v => variablesQ[v.name]));
                if (adaptiveFocusOrderVarNames.Count > 0)
                    adaptiveFocusOrderExpression = LinearExpression.Sum(adaptiveFocusOrderVarNames.Select(v => variablesBinary[v]));
                if (adaptiveNewPodOrderVarNames.Count > 0)
                    adaptiveNewPodOrderExpression = LinearExpression.Sum(adaptiveNewPodOrderVarNames
                        .Select(v => variablesBinary[v]));

                int maxAssignedUnits = residuals.Values.Sum(lines => lines.Values.Sum());
                int maxStationParts = Cs.Values.Sum();
                int maxUrgencyScore = adaptiveUrgencyByOrderId.Values.Sum();
                M2eAdaptiveExactMath.Weights weights = M2eAdaptiveExactMath.BuildWeights(
                    pendingOrders.Count, maxAssignedUnits, maxStationParts, maxUrgencyScore);
                adaptiveHierarchyObjective = adaptiveCompletionExpression * (double)weights.Completion
                    + adaptiveUrgencyExpression * (double)weights.Urgency
                    - adaptivePartExpression * (double)weights.StationPart
                    + adaptiveUnitExpression * (double)weights.AssignedUnit;
                if (!adaptiveAllowPartial && _splitConfig.AdaptiveSlotEfficientCompletion)
                    adaptiveHierarchyObjective = adaptiveHierarchyObjective
                        + adaptiveEfficientCompletionExpression * (double)weights.EfficientCompletion;
                if (!ReferenceEquals(adaptiveFocusOrderExpression, null))
                    adaptiveHierarchyObjective = adaptiveHierarchyObjective
                        + adaptiveFocusOrderExpression * (double)weights.FocusOrder;
                if (!ReferenceEquals(adaptiveNewPodOrderExpression, null))
                    adaptiveHierarchyObjective = adaptiveHierarchyObjective
                        + adaptiveNewPodOrderExpression * (double)weights.NewPodOrder;
                if (!ReferenceEquals(adaptiveFocusUnitExpression, null))
                    adaptiveHierarchyObjective = adaptiveHierarchyObjective
                        + adaptiveFocusUnitExpression * (double)weights.FocusUnit;

                long replenishmentUnitWeight = checked((long)maxStationParts + 1L);
                long replenishmentCompletionWeight = checked((long)maxAssignedUnits * replenishmentUnitWeight
                    + maxStationParts + 1L);
                adaptiveReplenishmentObjective = adaptiveCompletionExpression * (double)replenishmentCompletionWeight
                    - adaptivePartExpression;
                if (!ReferenceEquals(adaptiveNewPodExpression, null))
                    adaptiveReplenishmentObjective = adaptiveReplenishmentObjective
                        + adaptiveNewPodExpression * (double)replenishmentUnitWeight;
            }
            wrapper.Update();
            DateTime _optStart = DateTime.Now;
            double solve1Sec = 0.0;
            double solve2Sec = 0.0;
            int sunkOrdersStar = 0;
            int sunkItemsStar = 0;
            int adaptiveCompletionsStar = 0;
            int adaptiveTripsStar = 0;
            int adaptiveFocusOrdersStar = 0;
            int adaptiveNewPodOrdersStar = 0;
            int adaptiveUrgencyStar = 0;
            int adaptivePartsStar = 0;
            int adaptiveNewPodUnitsStar = 0;
            int adaptiveFocusUnitsStar = 0;
            int adaptiveUnitsStar = 0;
            bool runFinalSolve = true;
            Action lockAdaptiveHierarchy = () =>
            {
                adaptiveCompletionsStar = deVarNamez.Count(v => Math.Round(variablesBinary[v.name].GetValue()) != 0);
                adaptiveTripsStar = adaptiveTripSymbols.Count(v => Math.Round(variablesBinary[v.name].GetValue()) != 0);
                adaptiveFocusOrdersStar = adaptiveFocusOrderVarNames.Count(v => Math.Round(variablesBinary[v].GetValue()) != 0);
                adaptiveNewPodOrdersStar = adaptiveNewPodOrderVarNames.Count(v => Math.Round(variablesBinary[v].GetValue()) != 0);
                adaptiveUrgencyStar = deVarNamez.Where(v => Math.Round(variablesBinary[v.name].GetValue()) != 0)
                    .Sum(v => adaptiveUrgencyByOrderId[v.order.ID]);
                adaptivePartsStar = deVarNamey.Count(v => Math.Round(variablesBinary[v.name].GetValue()) != 0);
                adaptiveNewPodUnitsStar = adaptiveNewPodSymbols.Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                adaptiveFocusUnitsStar = adaptiveFocusSymbols.Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                adaptiveUnitsStar = deVarNameq.Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));

                wrapper.AddConstr(adaptiveCompletionExpression == adaptiveCompletionsStar, "aeLockCompletions");
                if (!ReferenceEquals(adaptiveTripExpression, null))
                    wrapper.AddConstr(adaptiveTripExpression == adaptiveTripsStar, "aeLockTrips");
                if (!ReferenceEquals(adaptiveFocusOrderExpression, null))
                    wrapper.AddConstr(adaptiveFocusOrderExpression == adaptiveFocusOrdersStar, "aeLockFocusOrders");
                if (!ReferenceEquals(adaptiveNewPodOrderExpression, null))
                    wrapper.AddConstr(adaptiveNewPodOrderExpression == adaptiveNewPodOrdersStar,
                        "aeLockNewPodOrders");
                wrapper.AddConstr(adaptiveUrgencyExpression == adaptiveUrgencyStar, "aeLockUrgency");
                wrapper.AddConstr(adaptivePartExpression == adaptivePartsStar, "aeLockParts");
                if (!ReferenceEquals(adaptiveFocusUnitExpression, null))
                    wrapper.AddConstr(adaptiveFocusUnitExpression == adaptiveFocusUnitsStar, "aeLockFocusUnits");
                wrapper.AddConstr(adaptiveUnitExpression == adaptiveUnitsStar, "aeLockUnits");
                wrapper.SetObjective(objective, OptimizationSense.Minimize);
                wrapper.Update();
            };
            if (adaptiveExact)
            {
                wrapper.SetObjective(adaptiveReplenishmentTrip
                    ? adaptiveReplenishmentObjective : adaptiveHierarchyObjective,
                    OptimizationSense.Maximize);
                wrapper.Update();
                DateTime adaptiveStart = DateTime.Now;
                wrapper.Optimize();
                solve1Sec = (DateTime.Now - adaptiveStart).TotalSeconds;
                if (wrapper.HasSolution())
                {
                    if (adaptiveReplenishmentTrip)
                    {
                        adaptiveCompletionsStar = deVarNamez.Count(v => Math.Round(variablesBinary[v.name].GetValue()) != 0);
                        adaptiveTripsStar = adaptiveTripSymbols.Count(v => Math.Round(variablesBinary[v.name].GetValue()) != 0);
                        adaptivePartsStar = deVarNamey.Count(v => Math.Round(variablesBinary[v.name].GetValue()) != 0);
                        adaptiveNewPodUnitsStar = adaptiveNewPodSymbols.Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                        adaptiveUnitsStar = deVarNameq.Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                        wrapper.AddConstr(adaptiveCompletionExpression == adaptiveCompletionsStar, "aeLockReplenishmentCompletions");
                        if (!ReferenceEquals(adaptiveTripExpression, null))
                            wrapper.AddConstr(adaptiveTripExpression == adaptiveTripsStar, "aeLockReplenishmentTrips");
                        wrapper.AddConstr(adaptivePartExpression == adaptivePartsStar, "aeLockReplenishmentParts");
                        if (!ReferenceEquals(adaptiveNewPodExpression, null))
                            wrapper.AddConstr(adaptiveNewPodExpression == adaptiveNewPodUnitsStar, "aeLockReplenishmentUnits");
                        wrapper.AddConstr(adaptiveUnitExpression == adaptiveUnitsStar, "aeLockReplenishmentTotalUnits");
                        wrapper.SetObjective(objective, OptimizationSense.Minimize);
                        wrapper.Update();
                    }
                    else
                    {
                        lockAdaptiveHierarchy();
                    }
                }
                else
                {
                    runFinalSolve = false;
                }
            }
            // (SF) Solve 1: maximize true completions supplied entirely by inherited Pb
            // pods; among equal completion counts, maximize the residual items closed by
            // those orders. Lock both integer components, then restore the legacy objective.
            else if (sunkFirst)
            {
                wrapper.SetObjective(sunkObjective, OptimizationSense.Maximize);
                wrapper.Update();
                DateTime solve1Start = DateTime.Now;
                wrapper.Optimize();
                solve1Sec = (DateTime.Now - solve1Start).TotalSeconds;
                if (wrapper.HasSolution())
                {
                    foreach (var pair in sunkVarNames)
                    {
                        if (Math.Round(variablesBinary[pair.Value].GetValue()) == 0)
                            continue;
                        sunkOrdersStar++;
                        sunkItemsStar += residuals[pair.Key].Values.Sum();
                    }
                    wrapper.AddConstr(sunkOrdersExpression == sunkOrdersStar, "sfLockOrders");
                    wrapper.AddConstr(sunkItemsExpression == sunkItemsStar, "sfLockItems");
                    wrapper.SetObjective(objective, OptimizationSense.Minimize);
                    wrapper.Update();
                }
                else
                {
                    runFinalSolve = false;
                }
            }
            // (CF) Solve 1: coverage layer without distance. Lock its optimum (combined
            // value, 1e-6 relative tolerance) plus the new-trip count, then hand the
            // model to the legacy objective as Solve 2 - distance now only arbitrates
            // WITHIN the L1-optimal set (pod->station->bot assignment, split shapes).
            else if (coverageFirst && !ReferenceEquals(coverageObjective, null))
            {
                wrapper.SetObjective(coverageObjective, OptimizationSense.Maximize);
                wrapper.Update();
                wrapper.Optimize();
                if (wrapper.HasSolution())
                {
                    double coverageStar = wrapper.GetObjectiveValue();
                    int tripsStar = deVarNamexps.Count(v => Pa.Contains(v.pod) && Math.Round(variablesBinary[v.name].GetValue()) != 0);
                    wrapper.AddConstr(coverageObjective >= coverageStar - 1e-6 * Math.Max(1.0, Math.Abs(coverageStar)), "cflockobj");
                    var lockTrips = deVarNamexps.Where(v => Pa.Contains(v.pod)).Select(v => variablesBinary[v.name]).ToList();
                    if (lockTrips.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(lockTrips) <= tripsStar, "cflocktrips");
                    wrapper.SetObjective(objective, OptimizationSense.Minimize);
                    wrapper.Update();
                }
            }
            if (runFinalSolve)
            {
                DateTime solve2Start = DateTime.Now;
                wrapper.Optimize();
                solve2Sec = (DateTime.Now - solve2Start).TotalSeconds;
            }
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;
            // (IC) probe values for the decision log
            int icLogPackCap = _icConfig != null
                ? M2eICMath.TotalPackingCapacity(_icConfig.PackingStationCount, _icConfig.PackingBufferCapacity) : 0;
            int icLogPackOcc = Instance.PackingBuffer != null ? Instance.PackingBuffer.AliveParentCount : 0;
            int icLogPackBudget = icLogPackCap > 0 ? M2eICMath.PackingBudget(icLogPackCap, icLogPackOcc) : -1;
            if (wrapper.HasSolution())
            {
                result.HasSolution = true;
                List<Symbol> IsdeVarNamexps = deVarNamexps.Where(v => Math.Round(variablesBinary[v.name].GetValue()) != 0).ToList();
                List<Symbol> IsdeVarNameq = deVarNameq.Where(v => Math.Round(variablesQ[v.name].GetValue()) > 0).ToList();
                int newTripsFinal = IsdeVarNamexps.Count(v => Pa.Contains(v.pod));
                result.CompletionCount = deVarNamez.Count(v => Math.Round(variablesBinary[v.name].GetValue()) != 0);
                result.NewTripCount = newTripsFinal;
                result.NewPodOrderCount = adaptiveExact
                    ? adaptiveNewPodOrderVarNames.Count(v => Math.Round(variablesBinary[v].GetValue()) != 0)
                    : 0;
                result.ProcessingOrderCount = adaptiveExact
                    ? adaptiveProcessingOrderVarNames.Count(v => Math.Round(variablesBinary[v].GetValue()) != 0)
                    : 0;
                result.FocusOrderCount = adaptiveExact
                    ? adaptiveFocusOrderVarNames.Count(v => Math.Round(variablesBinary[v].GetValue()) != 0)
                    : 0;
                result.UrgencyScore = adaptiveExact
                    ? deVarNamez.Where(v => Math.Round(variablesBinary[v.name].GetValue()) != 0)
                        .Sum(v => adaptiveUrgencyByOrderId[v.order.ID])
                    : 0;
                result.StationPartCount = deVarNamey.Count(v => Math.Round(variablesBinary[v.name].GetValue()) != 0);
                result.NewPodUnitCount = IsdeVarNameq.Where(v => Pa.Contains(v.pod))
                    .Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                foreach (var podGroup in IsdeVarNameq.Where(v => Pa.Contains(v.pod)).GroupBy(v => v.pod.ID))
                    result.NewPodUnitsByPodId[podGroup.Key] = podGroup
                        .Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                result.AssignedUnitCount = IsdeVarNameq.Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                result.ProcessingUnitCount = IsdeVarNameq.Where(v => processingPods.Contains(v.pod))
                    .Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                int sunkUnitsFinal = IsdeVarNameq.Where(v => Pb.Contains(v.pod))
                    .Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                if (adaptiveExact)
                {
                    bool lockViolated = result.CompletionCount != adaptiveCompletionsStar
                        || result.NewTripCount != adaptiveTripsStar
                        || result.StationPartCount != adaptivePartsStar
                        || result.AssignedUnitCount != adaptiveUnitsStar;
                    if (adaptiveReplenishmentTrip)
                        lockViolated = lockViolated
                            || result.NewPodUnitCount != adaptiveNewPodUnitsStar;
                    else
                        lockViolated = lockViolated
                            || result.FocusOrderCount != adaptiveFocusOrdersStar
                            || result.NewPodOrderCount != adaptiveNewPodOrdersStar
                            || result.ProcessingUnitCount != adaptiveFocusUnitsStar
                            || result.UrgencyScore != adaptiveUrgencyStar;
                    if (lockViolated)
                        throw new InvalidOperationException(string.Format(
                            "M2e-AE final solution violated the locked adaptive hierarchy: C {0}/{1}, T {2}/{3}, FOrder {4}/{5}, NOrder {6}/{7}, FUnit {8}/{9}, H {10}/{11}, Y {12}/{13}, PaUnit {14}/{15}, U {16}/{17}.",
                            result.CompletionCount, adaptiveCompletionsStar,
                            result.NewTripCount, adaptiveTripsStar,
                            result.FocusOrderCount, adaptiveFocusOrdersStar,
                            result.NewPodOrderCount, adaptiveNewPodOrdersStar,
                            result.ProcessingUnitCount, adaptiveFocusUnitsStar,
                            result.UrgencyScore, adaptiveUrgencyStar,
                            result.StationPartCount, adaptivePartsStar,
                            result.NewPodUnitCount, adaptiveNewPodUnitsStar,
                            result.AssignedUnitCount, adaptiveUnitsStar));
                }
                if (sunkFirst)
                {
                    int sunkOrdersFinal = 0;
                    int sunkItemsFinal = 0;
                    HashSet<int> sunkOrderIds = new HashSet<int>();
                    foreach (var pair in sunkVarNames)
                    {
                        if (Math.Round(variablesBinary[pair.Value].GetValue()) == 0)
                            continue;
                        sunkOrdersFinal++;
                        sunkItemsFinal += residuals[pair.Key].Values.Sum();
                        sunkOrderIds.Add(pair.Key.ID);
                    }
                    if (sunkOrdersFinal != sunkOrdersStar || sunkItemsFinal != sunkItemsStar)
                        throw new InvalidOperationException("M2e-SF final solution violated the locked sunk-first optimum.");
                    if (IsdeVarNameq.Any(v => Pa.Contains(v.pod) && sunkOrderIds.Contains(v.order.ID)))
                        throw new InvalidOperationException("M2e-SF sunk-completed order consumed newly claimed Pa supply.");
                }
                foreach (var itemName in deVarNameyrp)
                {
                    if (Math.Round(variablesBinary[itemName.name].GetValue()) != 0 && Ra.Contains(itemName.robot))
                    {
                        result.NewTripBot = itemName.robot;
                        result.NewTripBotsByPodId[itemName.pod.ID] = itemName.robot;
                        Instance.ResourceManager.BottoPod.Add(itemName.robot, itemName.pod);
                        Instance.ResourceManager.ClaimPod(itemName.pod, itemName.robot, BotTaskType.Extract);
                        foreach (var xps in IsdeVarNamexps.Where(v => v.pod.ID == itemName.pod.ID))
                            xps.outputstation.RegisterInboundPod(itemName.pod);
                    }
                }
                List<OutputStation> stationList = Cs.Keys.OrderBy(s => s.ID).ToList();
                // (Split-fed dispatch) stations where THIS decode actually assigns a split -
                // becomes _icPrevSplitStations for the NEXT solve to react to.
                HashSet<int> icThisSplitStations = new HashSet<int>();
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
                    // (Defer-to-processing) this order's plan is decided, but not yet acted on:
                    // skip it (untouched, still fully pending) unless every pod it draws from is
                    // genuinely the one being processed at its station right now. No solver
                    // constraint involved - purely which of this solve's decisions get committed.
                    if (_icConfig != null && _icConfig.DeferAllocationUntilProcessing)
                    {
                        bool icAllPodsProcessing = IsdeVarNameq.Where(v => v.order.ID == order.ID).All(v =>
                        {
                            HashSet<int> icProcHere;
                            return icPpByStation.TryGetValue(v.outputstation.ID, out icProcHere)
                                && icProcHere.Contains(v.pod.ID);
                        });
                        if (!icAllPodsProcessing)
                            continue;
                    }
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
                            icThisSplitStations.Add(station.ID);
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
                        // (IC) P1 ground truth: a split-path order must not have drawn from
                        // any storage-area (Pa) pod in this solution - EXCEPT a parent's
                        // closing-only dispatch (flag on + fully closed this solve; the MILP
                        // icP1d z-gate guarantees fresh orders still have zero Pa draws).
                        int icPaUnits = IsdeVarNameq.Where(v => v.order.ID == order.ID && Pa.Contains(v.pod))
                            .Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                        bool icClosingException = _icConfig != null && _icConfig.ParentClosingDispatch && fullyAssigned;
                        // (Split-driven dispatch) a completing split (fully assigned this solve)
                        // is allowed to have drawn from Pa; partial splits still may not.
                        bool icSplitDriveException = _icConfig != null && _icConfig.SplitCanDriveDispatch && fullyAssigned;
                        if (M2eICMath.SplitOrderDrawsFromStorage(true, icPaUnits) && !icClosingException && !icSplitDriveException
                            && !(_icConfig != null && _icConfig.SoftInboundCommitted))
                            throw new InvalidOperationException("M2e-IC: split order " + order.ID
                                + " drew " + icPaUnits + " unit(s) from storage-area pods (P1 violated).");
                        // (IC/PK) reserve the parent's packing box at first split (idempotent
                        // for re-split parents; conservative - never overshoots capacity).
                        if (Instance.PackingBuffer != null)
                            Instance.PackingBuffer.RegisterParent(order.ID);
                    }
                }
                // (Split-fed dispatch) overwrite (not accumulate) with THIS decode's realized
                // split stations - read at the start of the NEXT solve.
                _icPrevSplitStations = icThisSplitStations;
                double sumUs = 0.0;
                foreach (var s in deVarNameus)
                    sumUs += Math.Round(variablesUs[s.name].GetValue());
                // (PR diag) partial-draw observability: orders drawn from this solve without
                // earning the completion flag (deadband-break evidence, Gate 1), and
                // newly-claimed (Pa) pods whose ENTIRE draw went to such orders (the
                // fishing-for-frac guardrail, Gate 3). Computed in legacy mode too (zdonex
                // semantics there) - pure logging, zero behavior change.
                HashSet<int> zOffOrders = new HashSet<int>();
                foreach (var z in deVarNamez)
                    if (Math.Round(variablesBinary[z.name].GetValue()) == 0)
                        zOffOrders.Add(z.order.ID);
                int partialOrders = IsdeVarNameq.Where(v => zOffOrders.Contains(v.order.ID)).Select(v => v.order.ID).Distinct().Count();
                result.PartialOrderCount = partialOrders;
                int partialOnlyNewTrips = 0;
                foreach (var x in IsdeVarNamexps)
                {
                    if (!Pa.Contains(x.pod))
                        continue;
                    var podDraws = IsdeVarNameq.Where(q => q.pod.ID == x.pod.ID && q.outputstation.ID == x.outputstation.ID).ToList();
                    if (podDraws.Count > 0 && podDraws.All(q => zOffOrders.Contains(q.order.ID)))
                        partialOnlyNewTrips++;
                }
                int icWholeCount = icWholeVarNames.Count(n => Math.Round(variablesBinary[n].GetValue()) != 0);
                int icEpSum = 0;
                if (_icConfig != null && _icConfig.MultiPartPenalty != 0)
                    foreach (var icOrd in pendingOrders)
                        icEpSum += (int)Math.Round(variablesUs["icepx_" + icOrd.ID.ToString()].GetValue());
                int icShortfallSum = icShortfallNames.Count == 0 ? 0
                    : icShortfallNames.Sum(n => (int)Math.Round(variablesUs[n].GetValue()));
                WriteExactDecisionLog(true, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    IsdeVarNamexps.Count, nChildren, nFastPath, unitsAssigned, sumUs, wrapper.GetObjectiveValue(), _optSec,
                    partialOrders, partialOnlyNewTrips, odCount, odFired, sunkOrdersStar, sunkItemsStar,
                    sunkUnitsFinal, newTripsFinal, solve1Sec, solve2Sec,
                    icWholeCount, icLogPackOcc, icLogPackBudget, result.ProcessingUnitCount,
                    icEpSum, icShortfallSum, icSgOpenCount, icGateForegone);
            }
            else
            {
                WriteExactDecisionLog(false, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    0, 0, 0, 0, 0.0, double.NaN, _optSec, 0, 0, odCount, odFired,
                    sunkOrdersStar, sunkItemsStar, 0, 0, solve1Sec, solve2Sec,
                    0, icLogPackOcc, icLogPackBudget, 0, 0, 0, icSgOpenCount, icGateForegone);
            }
            return result;
        }

        private bool CommitSplitExactResult(SplitExactSolveResult result)
        {
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
                // (Fill fairness) On the parent's FIRST split, free its Fill backlog slot so a
                // fresh order is injected, while keeping it in _pendingOrders for residual
                // service. Fires exactly once (guarded by IsOrderAvailable). Whole block is
                // unreachable when the flag is off => bit-identical to current behavior.
                if (_icConfig != null && _icConfig.ReleaseParentOnFirstSplit
                    && parent.IsSplitParent
                    && (Instance.ItemManager as ItemManager).IsOrderAvailable(parent))
                {
                    int remUnits = parent.RemainingPositions.Sum(p => p.Value);
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(parent);
                    WriteEarlyRelease(parent.ID, remUnits);
                }
                if (parent.IsFullyClaimed)
                {
                    _pendingOrders.Remove(parent);
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(parent);
                }
            }
            return result.Allocations.Count > 0 && result.NewZiops.Count > 0;
        }

        private sealed class AdaptivePodCandidate
        {
            public Pod Pod;
            public Bot Bot;
            public OutputStation Station;
            public int Completions;
            public int MarginalCompletions;
            public int PotentialMarginalCompletions;
            public int Coverage;
            public double Distance;
            public int InboundBefore;
            public int PhysicalBefore;
            public double ProjectedStarvationGap;
            public double CurrentPodReleaseLeft;
        }

        private bool IsPodPhysicallyAtStation(Pod pod, OutputStation station)
        {
            Bot bot;
            if (!Instance.ResourceManager._usedPods.TryGetValue(pod, out bot))
                bot = Instance.ResourceManager.BottoPod.Where(pair => pair.Value.ID == pod.ID)
                    .Select(pair => pair.Key).FirstOrDefault();
            return bot != null && bot.CurrentWaypoint != null && station.Waypoint != null
                && bot.CurrentWaypoint.ID == station.Waypoint.ID;
        }

        private void PruneAdaptiveFocusPods()
        {
            foreach (var stationId in _adaptiveFocusPodByStationId.Keys.ToList())
            {
                int podId = _adaptiveFocusPodByStationId[stationId];
                OutputStation station = Instance.OutputStations.FirstOrDefault(s => s.ID == stationId);
                if (station == null || !station.InboundPods.Any(p => p.ID == podId))
                    _adaptiveFocusPodByStationId.Remove(stationId);
            }
        }

        private void WriteAdaptivePipelineProbe(OutputStation station, int freeSlots,
            HashSet<Pod> inboundPods, int physicalCount)
        {
            string key = _adaptiveExactEpoch.ToString() + ":" + station.ID.ToString();
            if (!_adaptivePipelineProbeKeys.Add(key))
                return;
            if (_adaptivePipelineProbeLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null
                    ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _adaptivePipelineProbeLog = new System.IO.StreamWriter(
                    System.IO.Path.Combine(dir, "m2eic_pipeline_probe_log.csv"), false) { AutoFlush = true };
                _adaptivePipelineProbeLog.WriteLine("epoch,time,station,freeSlots,inbound,physical,future,projectedStarvationGap,currentPodReleaseLeft,futurePodEta");
            }
            double speed = Instance.SettingConfig.StarveAwareNominalSpeed > 0
                ? Instance.SettingConfig.StarveAwareNominalSpeed
                : Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity));
            double futureEta = double.PositiveInfinity;
            if (inboundPods != null)
                foreach (var pod in inboundPods.Where(p => !IsPodPhysicallyAtStation(p, station)))
                    futureEta = Math.Min(futureEta, EstimatePodStationDistance(pod, station) / speed);
            _adaptivePipelineProbeLog.WriteLine(string.Join(",", new string[] {
                _adaptiveExactEpoch.ToString(),
                Instance.Controller.CurrentTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                station.ID.ToString(), freeSlots.ToString(),
                (inboundPods != null ? inboundPods.Count : 0).ToString(), physicalCount.ToString(),
                ((inboundPods != null ? inboundPods.Count : 0) - physicalCount).ToString(),
                station.GetInfoStationStarvationGap().ToString(System.Globalization.CultureInfo.InvariantCulture),
                station.GetInfoCurrentPodReleaseLeft().ToString(System.Globalization.CultureInfo.InvariantCulture),
                futureEta.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
        }

        private int CountAdaptiveFeasibleCompletions(IEnumerable<Order> orders,
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals,
            List<Dictionary<ItemDescription, int>> stationStock, int[] stationSlots, bool crossTime)
        {
            List<Dictionary<ItemDescription, int>> stock = stationStock
                .Select(lines => new Dictionary<ItemDescription, int>(lines)).ToList();
            int[] slots = (int[])stationSlots.Clone();
            int completions = 0;
            foreach (var order in orders.OrderBy(o => o.Timestay).ThenBy(o => o.DueTime).ThenBy(o => o.ID))
            {
                if (!slots.Any(v => v > 0))
                    break;
                bool[] slotFree = slots.Select(v => v > 0).ToArray();
                var plan = PvgsStationSplitPlanner.PlanCompletion(residuals[order].ToList(), stock, slotFree);
                if (plan == null || (!crossTime && plan.Count > 1))
                    continue;
                foreach (var part in plan)
                {
                    foreach (var line in part.Value)
                        stock[part.Key][line.Key] -= line.Value;
                    slots[part.Key]--;
                }
                completions++;
            }
            return completions;
        }

        private AdaptivePodCandidate FindAdaptivePodReplenishment(HashSet<Pod> pa, HashSet<Bot> freeBots,
            Dictionary<OutputStation, int> cs, Dictionary<OutputStation, HashSet<Pod>> inboundPods,
            HashSet<Order> pendingOrders, Dictionary<Order, Dictionary<ItemDescription, int>> residuals,
            HashSet<int> replenishedStationsThisDecision, bool periodicSupply,
            Dictionary<int, List<Pod>> plannedPodsByStationId)
        {
            if (pa.Count == 0 || freeBots.Count == 0 || pendingOrders.Count == 0)
                return null;

            List<OutputStation> stationList = cs.Keys.OrderBy(station => station.ID).ToList();
            List<OutputStation> targetStations = stationList.Where(station =>
            {
                HashSet<Pod> pods;
                inboundPods.TryGetValue(station, out pods);
                int inboundCount = pods != null ? pods.Count : 0;
                int physicalCount = pods != null ? pods.Count(p => IsPodPhysicallyAtStation(p, station)) : 0;
                WriteAdaptivePipelineProbe(station, cs[station], pods, physicalCount);
                int normalTarget = _splitConfig != null ? _splitConfig.AdaptiveFuturePodTarget : 1;
                int futureCount = inboundCount - physicalCount;
                bool riskPrefetch = _splitConfig != null && _splitConfig.AdaptiveRiskPrefetch
                    && futureCount == normalTarget
                    && station.GetInfoStationStarvationGap() > _splitConfig.AdaptiveRiskPrefetchGapSec;
                int effectiveTarget = normalTarget + (riskPrefetch ? 1 : 0);
                bool needsPeriodicSupply = M2eAdaptiveExactMath.NeedsFuturePod(inboundCount, physicalCount,
                    effectiveTarget);
                bool canContinueValueResweep = replenishedStationsThisDecision != null
                    && replenishedStationsThisDecision.Contains(station.ID);
                return cs[station] > 0 && (periodicSupply
                    ? needsPeriodicSupply && !canContinueValueResweep
                    : canContinueValueResweep);
            }).OrderBy(station =>
            {
                double last;
                return _adaptiveLastReplenishmentByStationId.TryGetValue(station.ID, out last)
                    ? last : double.NegativeInfinity;
            }).ThenBy(station => station.ID).ToList();
            if (periodicSupply && targetStations.Count > 1)
                targetStations = targetStations.Take(1).ToList();
            if (targetStations.Count == 0)
                return null;

            List<Dictionary<ItemDescription, int>> stationStock = new List<Dictionary<ItemDescription, int>>();
            foreach (var station in stationList)
            {
                Dictionary<ItemDescription, int> stock = new Dictionary<ItemDescription, int>();
                HashSet<Pod> stationPods;
                if (inboundPods.TryGetValue(station, out stationPods))
                    foreach (var pod in stationPods)
                        foreach (var sku in pod.ItemDescriptionsContained)
                        {
                            int current;
                            stock[sku] = (stock.TryGetValue(sku, out current) ? current : 0)
                                + pod.CountAvailable(sku);
                        }
                List<Pod> plannedPods;
                if (plannedPodsByStationId != null
                    && plannedPodsByStationId.TryGetValue(station.ID, out plannedPods))
                    foreach (var pod in plannedPods)
                        foreach (var sku in pod.ItemDescriptionsContained)
                        {
                            int current;
                            stock[sku] = (stock.TryGetValue(sku, out current) ? current : 0)
                                + pod.CountAvailable(sku);
                        }
                stationStock.Add(stock);
            }
            int[] stationSlots = stationList.Select(station => cs[station]).ToArray();
            Dictionary<ItemDescription, int> poolDemand = new Dictionary<ItemDescription, int>();
            foreach (var demand in residuals.Values)
                foreach (var line in demand)
                {
                    int current;
                    poolDemand[line.Key] = (poolDemand.TryGetValue(line.Key, out current) ? current : 0)
                        + line.Value;
                }

            bool crossTime = _splitConfig != null && _splitConfig.CrossTime;
            int baselinePotentialCompletions = 0;
            foreach (var order in pendingOrders)
            {
                var plan = PvgsStationSplitPlanner.PlanCompletion(residuals[order].ToList(),
                    stationStock, stationSlots.Select(v => v > 0).ToArray());
                if (plan != null && (crossTime || plan.Count == 1))
                    baselinePotentialCompletions++;
            }
            int feasibleBaselineCompletions = CountAdaptiveFeasibleCompletions(pendingOrders, residuals,
                stationStock, stationSlots, crossTime);
            AdaptivePodCandidate best = null;
            foreach (var station in targetStations)
            {
                int stationIndex = stationList.IndexOf(station);
                HashSet<Pod> stationPods;
                inboundPods.TryGetValue(station, out stationPods);
                int inboundBefore = stationPods != null ? stationPods.Count : 0;
                int physicalBefore = stationPods != null
                    ? stationPods.Count(p => IsPodPhysicallyAtStation(p, station)) : 0;
                foreach (var pod in pa)
                {
                    if (plannedPodsByStationId != null
                        && plannedPodsByStationId.Values.Any(pods => pods.Contains(pod)))
                        continue;
                    Bot nearestBot = null;
                    double botDistance = double.PositiveInfinity;
                    foreach (var bot in freeBots)
                    {
                        double distance = EstimateBotPodDistance(bot, pod);
                        if (distance < botDistance)
                        {
                            botDistance = distance;
                            nearestBot = bot;
                        }
                    }
                    if (nearestBot == null)
                        continue;

                    var merged = new Dictionary<ItemDescription, int>(stationStock[stationIndex]);
                    foreach (var sku in pod.ItemDescriptionsContained)
                    {
                        int current;
                        merged[sku] = (merged.TryGetValue(sku, out current) ? current : 0)
                            + pod.CountAvailable(sku);
                    }
                    var hypotheticalStock = new List<Dictionary<ItemDescription, int>>(stationStock);
                    hypotheticalStock[stationIndex] = merged;
                    int completionsWithCandidate = 0;
                    foreach (var order in pendingOrders)
                    {
                        var plan = PvgsStationSplitPlanner.PlanCompletion(residuals[order].ToList(),
                            hypotheticalStock, stationSlots.Select(v => v > 0).ToArray());
                        if (plan != null && (crossTime || plan.Count == 1))
                            completionsWithCandidate++;
                    }
                    int feasibleCompletionsWithCandidate = CountAdaptiveFeasibleCompletions(pendingOrders,
                        residuals, hypotheticalStock, stationSlots, crossTime);
                    int marginalCompletions = Math.Max(0,
                        feasibleCompletionsWithCandidate - feasibleBaselineCompletions);
                    int potentialMarginalCompletions = Math.Max(0,
                        completionsWithCandidate - baselinePotentialCompletions);
                    int rankedCompletions = periodicSupply
                        ? checked(potentialMarginalCompletions * (pendingOrders.Count + 1)
                            + completionsWithCandidate)
                        : marginalCompletions;
                    int coverage = poolDemand.Sum(line => Math.Min(line.Value, pod.CountAvailable(line.Key)));
                    double distanceCost = botDistance + EstimatePodStationDistance(pod, station);
                    int bestRankedCompletions = best == null ? -1 : (periodicSupply
                        ? checked(best.PotentialMarginalCompletions * (pendingOrders.Count + 1)
                            + best.Completions)
                        : best.MarginalCompletions);
                    if (!M2eAdaptiveExactMath.IsBetterPodCandidate(rankedCompletions, coverage, distanceCost, pod.ID,
                        bestRankedCompletions,
                        best != null ? best.Coverage : -1,
                        best != null ? best.Distance : double.PositiveInfinity,
                        best != null ? best.Pod.ID : int.MaxValue))
                        continue;
                    best = new AdaptivePodCandidate
                    {
                        Pod = pod,
                        Bot = nearestBot,
                        Station = station,
                        Completions = completionsWithCandidate,
                        MarginalCompletions = marginalCompletions,
                        PotentialMarginalCompletions = potentialMarginalCompletions,
                        Coverage = coverage,
                        Distance = distanceCost,
                        InboundBefore = inboundBefore,
                        PhysicalBefore = physicalBefore,
                        ProjectedStarvationGap = station.GetInfoStationStarvationGap(),
                        CurrentPodReleaseLeft = station.GetInfoCurrentPodReleaseLeft()
                    };
                }
            }
            return best != null && best.Coverage > 0 ? best : null;
        }

        private List<AdaptivePodCandidate> BuildAdaptivePodBurst(HashSet<Pod> pa, HashSet<Bot> freeBots,
            Dictionary<OutputStation, int> cs, Dictionary<OutputStation, HashSet<Pod>> inboundPods,
            HashSet<Order> pendingOrders, Dictionary<Order, Dictionary<ItemDescription, int>> residuals,
            HashSet<int> replenishedStationsThisDecision)
        {
            int cap = Math.Min(_splitConfig.AdaptiveMaxPodBurst, freeBots.Count);
            List<AdaptivePodCandidate> burst = new List<AdaptivePodCandidate>();
            Dictionary<int, List<Pod>> plannedPodsByStationId = new Dictionary<int, List<Pod>>();
            HashSet<int> planningStations = new HashSet<int>(replenishedStationsThisDecision);
            while (burst.Count < cap)
            {
                AdaptivePodCandidate candidate = FindAdaptivePodReplenishment(pa, freeBots, cs, inboundPods,
                    pendingOrders, residuals, planningStations, true, plannedPodsByStationId);
                bool periodic = candidate != null;
                if (candidate == null || candidate.Completions <= 0)
                {
                    candidate = FindAdaptivePodReplenishment(pa, freeBots, cs, inboundPods, pendingOrders,
                        residuals, planningStations, false, plannedPodsByStationId);
                    periodic = false;
                }
                if (candidate == null || (periodic
                    ? candidate.Completions <= 0 : candidate.MarginalCompletions <= 0))
                    break;

                burst.Add(candidate);
                planningStations.Add(candidate.Station.ID);
                List<Pod> planned;
                if (!plannedPodsByStationId.TryGetValue(candidate.Station.ID, out planned))
                {
                    planned = new List<Pod>();
                    plannedPodsByStationId.Add(candidate.Station.ID, planned);
                }
                planned.Add(candidate.Pod);
            }
            return burst;
        }

        private void WriteAdaptiveReplenishmentLog(AdaptivePodCandidate candidate, Bot bot, int newPodUnits)
        {
            if (_adaptiveReplenishmentLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null
                    ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _adaptiveReplenishmentLog = new System.IO.StreamWriter(
                    System.IO.Path.Combine(dir, "m2eic_pod_replenishment_log.csv"), false) { AutoFlush = true };
                _adaptiveReplenishmentLog.WriteLine("time,station,pod,bot,completedOrderValue,potentialMarginalCompletedOrderValue,feasibleMarginalCompletedOrderValue,coverage,distance,inboundBefore,physicalBefore,futureBefore,projectedStarvationGap,currentPodReleaseLeft,newPodUnits");
            }
            _adaptiveReplenishmentLog.WriteLine(string.Join(",", new string[] {
                Instance.Controller.CurrentTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                candidate.Station.ID.ToString(), candidate.Pod.ID.ToString(), bot.ID.ToString(),
                candidate.Completions.ToString(), candidate.PotentialMarginalCompletions.ToString(),
                candidate.MarginalCompletions.ToString(), candidate.Coverage.ToString(),
                candidate.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture),
                candidate.InboundBefore.ToString(), candidate.PhysicalBefore.ToString(),
                (candidate.InboundBefore - candidate.PhysicalBefore).ToString(),
                candidate.ProjectedStarvationGap.ToString(System.Globalization.CultureInfo.InvariantCulture),
                candidate.CurrentPodReleaseLeft.ToString(System.Globalization.CultureInfo.InvariantCulture),
                newPodUnits.ToString()
            }));
        }

        private void DecideAdaptiveExact(DateTime startedAt)
        {
            bool solvedAny = false;
            int committedSweeps = 0;
            int logIteration = 0;
            int limit = _splitConfig.AdaptiveExactResweepLimit;
            HashSet<int> replenishedStationsThisDecision = new HashSet<int>();

            while (committedSweeps < limit)
            {
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
                int odCount;
                bool odFired;
                HashSet<Pod> allPods = InitializeSplitExact(out PiSKU, out OiSKU, out variableNames, out Cs,
                    out pendingOrders, out inboundPods, out Ra, out Rb, out R, out Pb, out Pa,
                    out PodToBot, out residuals, out odCount, out odFired);

                if (R.Count == 0 || pendingOrders.Count == 0 || !Cs.Values.Any(v => v > 0))
                    break;

                // Phase A: build one joint PS burst. Periodic supply is admitted first;
                // additional pods must create a new completable order after the pods already
                // in the burst. All selected pods share one exact OA, so several trips may
                // serve the same station order without consuming extra backlog slots.
                List<AdaptivePodCandidate> burst = BuildAdaptivePodBurst(Pa, Ra, Cs, inboundPods,
                    pendingOrders, residuals, replenishedStationsThisDecision);
                AdaptivePodCandidate periodicCandidate = burst.FirstOrDefault();
                SplitExactSolveResult inheritedBeforeSupply = null;
                double supplyLead = _splitConfig.AdaptivePeriodicSupplyLeadTimeSec;
                bool deferPeriodicSupply = periodicCandidate != null && supplyLead > 0
                    && periodicCandidate.InboundBefore > 0 && periodicCandidate.PhysicalBefore > 0
                    && (double.IsNaN(periodicCandidate.CurrentPodReleaseLeft)
                        || periodicCandidate.CurrentPodReleaseLeft > supplyLead);
                if (deferPeriodicSupply)
                {
                    inheritedBeforeSupply = SolveSplitExact(SolverType.Gurobi, PiSKU, OiSKU, allPods,
                        Cs, variableNames, pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa, PodToBot,
                        residuals, odCount, odFired, null, null, false);
                    solvedAny = true;
                    WriteAdaptiveExactLog(logIteration++, false, false, inheritedBeforeSupply);
                    if (inheritedBeforeSupply.HasSolution && inheritedBeforeSupply.CompletionCount > 0)
                    {
                        if (!CommitSplitExactResult(inheritedBeforeSupply))
                            break;
                        committedSweeps++;
                        continue;
                    }
                }
                SplitExactSolveResult dispatched = null;
                while (burst.Count > 0)
                {
                    solvedAny = true;
                    dispatched = SolveSplitExact(SolverType.Gurobi, PiSKU, OiSKU, allPods,
                        Cs, variableNames, pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa, PodToBot,
                        residuals, odCount, odFired, burst, false);
                    WriteAdaptiveExactLog(logIteration++, true, false, dispatched);
                    if (dispatched.HasSolution && dispatched.CompletionCount > 0
                        && dispatched.NewTripCount == burst.Count)
                        break;
                    burst.RemoveAt(burst.Count - 1);
                    dispatched = null;
                }
                if (dispatched != null && CommitSplitExactResult(dispatched))
                {
                    foreach (var candidate in burst)
                    {
                        if (!_adaptiveFocusPodByStationId.ContainsKey(candidate.Station.ID))
                            _adaptiveFocusPodByStationId[candidate.Station.ID] = candidate.Pod.ID;
                        _adaptiveLastReplenishmentByStationId[candidate.Station.ID] = Instance.Controller.CurrentTime;
                        replenishedStationsThisDecision.Add(candidate.Station.ID);
                        Bot tripBot;
                        if (!dispatched.NewTripBotsByPodId.TryGetValue(candidate.Pod.ID, out tripBot))
                            tripBot = candidate.Bot;
                        int podUnits;
                        if (!dispatched.NewPodUnitsByPodId.TryGetValue(candidate.Pod.ID, out podUnits))
                            podUnits = 0;
                        WriteAdaptiveReplenishmentLog(candidate, tripBot, podUnits);
                    }
                    committedSweeps++;
                    continue;
                }

                // Keep the best periodic coverage candidate for the empty-station emergency
                // fallback even when it could not complete an order and was excluded above.
                if (periodicCandidate == null)
                    periodicCandidate = FindAdaptivePodReplenishment(Pa, Ra, Cs, inboundPods,
                        pendingOrders, residuals, replenishedStationsThisDecision, true,
                        new Dictionary<int, List<Pod>>());

                // Phase B: if no periodic/value trip was committed, exhaust full orders
                // using inherited supply only. The focus hierarchy protects the physical
                // processing pod before queued/en-route pods.
                SplitExactSolveResult inherited = inheritedBeforeSupply;
                if (inherited == null)
                {
                    inherited = SolveSplitExact(SolverType.Gurobi, PiSKU, OiSKU, allPods,
                        Cs, variableNames, pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa, PodToBot,
                        residuals, odCount, odFired, null, null, false);
                    solvedAny = true;
                    WriteAdaptiveExactLog(logIteration++, false, false, inherited);
                }
                if (inherited.HasSolution && inherited.CompletionCount > 0)
                {
                    if (!CommitSplitExactResult(inherited))
                        break;
                    committedSweeps++;
                    continue;
                }

                // Phase C: order-first has no full completion left. Allow one partial order
                // from sunk supply to squeeze the current pod, then stop this event so item
                // progress cannot fragment the backlog across repeated resweeps.
                SplitExactSolveResult sunkPartial = SolveSplitExact(SolverType.Gurobi, PiSKU, OiSKU, allPods,
                    Cs, variableNames, pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa, PodToBot,
                    residuals, odCount, odFired, null, null, true);
                WriteAdaptiveExactLog(logIteration++, false, true, sunkPartial);
                if (sunkPartial.HasSolution && sunkPartial.AssignedUnitCount > 0
                    && CommitSplitExactResult(sunkPartial))
                {
                    committedSweeps++;
                    break;
                }

                // A completely empty station still needs a way to restart when no pod can
                // complete an order by itself. This guard deliberately excludes stations
                // that already have a physical, queued, or en-route pod: zero-completion
                // trips are emergency supply defense only.
                if (periodicCandidate != null && periodicCandidate.Coverage > 0
                    && periodicCandidate.InboundBefore == 0)
                {
                    SplitExactSolveResult supplyPartial = SolveSplitExact(SolverType.Gurobi, PiSKU, OiSKU,
                        allPods, Cs, variableNames, pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa,
                        PodToBot, residuals, odCount, odFired, periodicCandidate.Pod,
                        periodicCandidate.Station, true);
                    WriteAdaptiveExactLog(logIteration++, true, true, supplyPartial);
                    if (supplyPartial.HasSolution && supplyPartial.AssignedUnitCount > 0
                        && supplyPartial.NewTripCount > 0 && CommitSplitExactResult(supplyPartial))
                    {
                        _adaptiveFocusPodByStationId[periodicCandidate.Station.ID] = periodicCandidate.Pod.ID;
                        _adaptiveLastReplenishmentByStationId[periodicCandidate.Station.ID] = Instance.Controller.CurrentTime;
                        WriteAdaptiveReplenishmentLog(periodicCandidate,
                            supplyPartial.NewTripBot ?? periodicCandidate.Bot, supplyPartial.NewPodUnitCount);
                        committedSweeps++;
                    }
                }
                break;
            }

            _adaptiveExactEpoch++;
            if (solvedAny)
                Instance.Observer.TimeOrderBatchingbyMP((DateTime.Now - startedAt).TotalSeconds);
        }

        /// <summary>
        /// This is called to decide about potentially pending orders (split-exact MILP version).
        /// Mirrors Spec 2's SplitM1GManager.DecideAboutPendingOrders wiring exactly - only the
        /// Initialize/Solve method names and result type differ.
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
            DateTime A = DateTime.Now;
            bool adaptiveExact = _splitConfig != null && _splitConfig.AdaptiveExactResweeps;
            if (adaptiveExact)
            {
                PruneAdaptiveFocusPods();
                DecideAdaptiveExact(A);
                return;
            }

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
            int odCount;
            bool odFired;
            HashSet<Pod> allPods = InitializeSplitExact(out PiSKU, out OiSKU, out variableNames, out Cs, out pendingOrders,
                out inboundPods, out Ra, out Rb, out R, out Pb, out Pa, out PodToBot, out residuals,
                out odCount, out odFired);

            bool solvedAny = false;
            if (R.Count() > 0 && pendingOrders.Count > 0 && Cs.Values.Any(v => v > 0))
            {
                // PS first chooses a pod/station from hos-like completed-order potential.
                // The exact OA then fixes that choice and gives the trip at least one real
                // item request, so TA can execute it in this same external decision event.
                AdaptivePodCandidate candidate = null;
                bool partialMode = false;
                SplitExactSolveResult result = SolveSplitExact(SolverType.Gurobi, PiSKU, OiSKU, allPods, Cs, variableNames,
                    pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa, PodToBot, residuals, odCount, odFired,
                    candidate != null ? candidate.Pod : null, candidate != null ? candidate.Station : null, false);
                solvedAny = true;
                int logIteration = 0;
                if (adaptiveExact && (!result.HasSolution || result.CompletionCount == 0))
                {
                    WriteAdaptiveExactLog(logIteration++, candidate != null, false, result);
                    partialMode = true;
                    result = SolveSplitExact(SolverType.Gurobi, PiSKU, OiSKU, allPods, Cs, variableNames,
                        pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa, PodToBot, residuals, odCount, odFired,
                        candidate != null ? candidate.Pod : null, candidate != null ? candidate.Station : null, true);
                }
                if (adaptiveExact && candidate != null && !result.HasSolution)
                {
                    WriteAdaptiveExactLog(logIteration++, true, partialMode, result);
                    candidate = null;
                    partialMode = true;
                    result = SolveSplitExact(SolverType.Gurobi, PiSKU, OiSKU, allPods, Cs, variableNames,
                        pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa, PodToBot, residuals, odCount, odFired,
                        null, null, true);
                }
                CommitSplitExactResult(result);
                if (adaptiveExact)
                {
                    WriteAdaptiveExactLog(logIteration, candidate != null, partialMode, result);
                    if (candidate != null && result.HasSolution && result.NewTripCount > 0)
                    {
                        _adaptiveFocusPodByStationId[candidate.Station.ID] = candidate.Pod.ID;
                        _adaptiveLastReplenishmentByStationId[candidate.Station.ID] = Instance.Controller.CurrentTime;
                        WriteAdaptiveReplenishmentLog(candidate, result.NewTripBot ?? candidate.Bot,
                            result.NewPodUnitCount);
                    }
                }
            }
            if (solvedAny)
                Instance.Observer.TimeOrderBatchingbyMP((DateTime.Now - A).TotalSeconds);
        }
    }
}

