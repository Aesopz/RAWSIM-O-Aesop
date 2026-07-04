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
    /// MILP-based order-splitting manager: M1G with the one-station-per-order constraint (shi2)
    /// relaxed to a unit-level quantity assignment q[o,i,s]. Reuses the Spec 1 enabler pipeline
    /// (CreateSplitChild / TransferExtractRequests / consolidation) to commit the solution.
    /// The original M1GManager stays untouched and serves as the M0 ablation baseline.
    /// See docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md.
    /// </summary>
    public class SplitM1GManager : M1GManager
    {
        /// <summary>
        /// Creates a new instance of this manager.
        /// </summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public SplitM1GManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GConfiguration;
        }

        /// <summary>
        /// The split-specific config of this controller.
        /// </summary>
        private SplitM1GConfiguration _splitConfig;

        /// <summary>
        /// order 進入 Od 的截止時間（鏡射 base 的 private DueTimeOrderofMP）。
        /// </summary>
        private static readonly double _dueTimeOrderofMP = TimeSpan.FromMinutes(30).TotalSeconds;

        /// <summary>
        /// Residual-demand version of GenerateOiSKU: indexes pending orders by the SKUs
        /// they still need (RemainingPositions), not their full demand.
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
        /// Residual-demand version of GenerateOd: urgency set based on RemainingPositions,
        /// with a ContainsKey guard (M2 orders may have residual SKUs not coverable this epoch).
        /// Also refreshes Timestay/sequence for all pending orders (base parity).
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

        // ── Starve-aware cost wrappers (mirror of the base's private block; the base members are
        //    private and the original file must stay untouched) ──
        private bool _saEnabledSplit;
        private double _saNominalSpeedSplit;
        private double _saFixedParamSplit;
        private Dictionary<int, double> _saEstByStationSplit = new Dictionary<int, double>();
        private Dictionary<int, double> _saRepBotPodTimeSplit = new Dictionary<int, double>();

        /// <summary>Per-solve precompute of starve-aware inputs; no-op when the feature is disabled.</summary>
        private void PrepareStarveAwareSplit(IEnumerable<Pod> pods, Dictionary<OutputStation, int> Cs, HashSet<Bot> Ra)
        {
            _saEnabledSplit = Instance != null && Instance.SettingConfig != null && Instance.SettingConfig.StarveAwareCostEnabled;
            if (!_saEnabledSplit)
                return;
            _saFixedParamSplit = Instance.SettingConfig.StarveAwareFixedParam;
            double cfgSpeed = Instance.SettingConfig.StarveAwareNominalSpeed;
            _saNominalSpeedSplit = cfgSpeed > 0.0
                ? cfgSpeed
                : (Instance.Bots != null && Instance.Bots.Count > 0
                    ? Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity))
                    : 1.0);
            double now = Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;
            _saEstByStationSplit = new Dictionary<int, double>();
            foreach (var s in Cs.Keys)
                _saEstByStationSplit[s.ID] = StarveAwareCost.Est(s, now);
            _saRepBotPodTimeSplit = new Dictionary<int, double>();
            foreach (var p in pods)
            {
                double best = double.PositiveInfinity;
                foreach (var r in Ra)
                    best = Math.Min(best, StarveAwareCost.TravelTime(EstimateBotPodDistance(r, p), _saNominalSpeedSplit));
                _saRepBotPodTimeSplit[p.ID] = best;
            }
        }

        /// <summary>bot->pod objective coefficient: travel time when starve-aware, else distance.</summary>
        private double SplitBotPodCost(Bot robot, Pod pod)
        {
            double d = EstimateBotPodDistance(robot, pod);
            return _saEnabledSplit ? StarveAwareCost.TravelTime(d, _saNominalSpeedSplit) : d;
        }

        /// <summary>pod->station objective coefficient: travel time + starvation delay penalty when starve-aware, else distance.</summary>
        private double SplitPodStationCost(Pod pod, OutputStation station)
        {
            double d = EstimatePodStationDistance(pod, station);
            if (!_saEnabledSplit)
                return d;
            double podStationTime = StarveAwareCost.TravelTime(d, _saNominalSpeedSplit);
            double repBotPod = (_saRepBotPodTimeSplit.TryGetValue(pod.ID, out var t) && !double.IsPositiveInfinity(t)) ? t : 0.0;
            double taCost = podStationTime + repBotPod;
            double est = _saEstByStationSplit.TryGetValue(station.ID, out var e) ? e : double.PositiveInfinity;
            double penalty = StarveAwareCost.DelayPenalty(taCost, est, _saFixedParamSplit);
            return podStationTime + penalty;
        }

        /// <summary>
        /// Split version of Initialize: snapshots the decision inputs with residual demand
        /// (RemainingPositions / GetRemainingDemand) instead of full order demand, and appends
        /// the q / y / z variable name lists (keys 7 / 8 / 9) to the base variable names.
        /// </summary>
        private HashSet<Pod> InitializeSplit(out Dictionary<ItemDescription, List<Pod>> PiSKU, out Dictionary<ItemDescription, List<Order>> OiSKU,
            out Dictionary<int, List<Symbol>> variableNames, out Dictionary<OutputStation, int> Cs, out HashSet<Order> pendingOrders,
            out Dictionary<OutputStation, HashSet<Pod>> inboundPods, out HashSet<Bot> Ra, out HashSet<Bot> Rb,
            out HashSet<Bot> R, out HashSet<Pod> Pb, out HashSet<Pod> Pa, out Dictionary<Pod, Bot> PodToBot,
            out Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            // SPLIT: first stock filter on residual demand; M1 = all residual SKUs coverable,
            //        M2 = at least one residual unit in actual stock
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
                        // Exclude it from the model for this decision. (base parity)
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
            // SPLIT: second (PiSKU) filter on residual demand, M1 all / M2 any
            if (_splitConfig != null && _splitConfig.CrossTime)
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.Any(p => IsAvailabletoPiSKU(p.Key) >= 1)));
            else
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.All(p => IsAvailabletoPiSKU(p.Key) >= p.Value)));
            HashSet<Order> Od = GenerateOdSplit(pendingOrders, PiSKU);
            if (Od.Count > Cs.Values.Sum())
                pendingOrders = new HashSet<Order>(Od);
            OiSKU = GenerateOiSKUSplit(pendingOrders);
            // SPLIT: residual snapshot for this solve (decode consistency)
            residuals = pendingOrders.ToDictionary(o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            variableNames = CreatedeVarName(PiSKU, OiSKU, allPods, pendingOrders, Cs, R, Pa1, out Pa);
            // SPLIT: append q (7) / y (8) / z (9) variable names.
            // q only for residual SKUs coverable by the model pods (PiSKU) - non-coverable residual
            // SKUs simply stay in the backlog this epoch (M2); M1 orders are pre-filtered to full coverage.
            var piSkuCopy = PiSKU;  // Capture out parameter for use in lambda
            List<Symbol> deVarNameq = new List<Symbol>();
            List<Symbol> deVarNamey = new List<Symbol>();
            List<Symbol> deVarNamez = new List<Symbol>();
            foreach (var order in pendingOrders)
            {
                foreach (var station in Cs.Keys)
                {
                    deVarNamey.Add(new Symbol { order = order, outputstation = station, name = "ysp" + "_" + order.ID.ToString() + "_" + station.ID.ToString() });
                    foreach (var sku in residuals[order].Where(p => piSkuCopy.ContainsKey(p.Key)))
                        deVarNameq.Add(new Symbol { order = order, outputstation = station, skui = sku.Key, name = "q" + "_" + sku.Key.ID.ToString() + "_" + order.ID.ToString() + "_" + station.ID.ToString() });
                }
                if (_splitConfig == null || !_splitConfig.CrossTime)
                    deVarNamez.Add(new Symbol { order = order, name = "zfull" + "_" + order.ID.ToString() });
            }
            variableNames.Add(7, deVarNameq);
            variableNames.Add(8, deVarNamey);
            variableNames.Add(9, deVarNamez);
            return allPods;
        }
    }
}
