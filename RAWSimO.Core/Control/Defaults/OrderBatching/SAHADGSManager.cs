using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using RAWSimO.Core.IO;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.Core.Metrics;
using RAWSimO.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;
using static RAWSimO.Core.Control.BotManager;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Starvation-aware HADGS: min-EST water-filling over stations; POA/PPS/TA all keyed on
    /// station time-to-starvation; weighted delay-vs-orders candidate scoring; regret-greedy TA.
    /// Spec: docs/superpowers/specs/2026-06-13-sa-hadgs-design.md.
    /// </summary>
    public class SAHADGSManager : OrderManager
    {
        /// <summary>
        /// Creates a new instance of this manager.
        /// </summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public SAHADGSManager(Instance instance) : base(instance)
        { _config = instance.ControllerConfig.OrderBatchingConfig as SAHADGSConfiguration; }

        /// <summary>The config of this controller.</summary>
        private SAHADGSConfiguration _config;

        /// <summary>Deadline for an order to enter the urgent set Od (borrowed: HADGSManager.cs:210).</summary>
        private double DueTimeOrderofMP = TimeSpan.FromMinutes(30).TotalSeconds;

        /// <summary>Per-epoch committed-but-not-yet-tasked work per station, feeding EST projections.</summary>
        private readonly Dictionary<OutputStation, List<SlowStartController.StationWorkJob>> _localJobs =
            new Dictionary<OutputStation, List<SlowStartController.StationWorkJob>>();

        /// <summary>Pods selected within the current epoch (guard against double-claim).</summary>
        private HashSet<Pod> _selectedPods = new HashSet<Pod>();

        // borrowed: HADGSManager.cs:45-68
        private double EstimateBotPodDistance(Bot bot, Pod pod)
        {
            if (bot == null || pod == null)
                return double.PositiveInfinity;

            var botWaypoint = GetBotReferenceWaypoint(bot);
            double botX = botWaypoint != null ? botWaypoint.X : bot.X;
            double botY = botWaypoint != null ? botWaypoint.Y : bot.Y;
            var podWaypoint = GetPodReferenceWaypoint(pod);
            double podX = podWaypoint != null ? podWaypoint.X : pod.X;
            double podY = podWaypoint != null ? podWaypoint.Y : pod.Y;

            double physDist = Math.Abs(botX - podX) + Math.Abs(botY - podY);

            if (_config != null && _config.UseBAED && bot.CurrentWaypoint != null && podWaypoint != null)
            {
                double delaySec = RAWSimO.Core.Metrics.BAEDEstimator.ComputeEntryDelaySecondsForBot(
                    Instance, bot, podWaypoint);
                if (delaySec > 0.0)
                    physDist += _config.BAEDReferenceSpeed * delaySec;
            }

            return physDist;
        }

        // borrowed: HADGSManager.cs:70-81
        private RAWSimO.Core.Waypoints.Waypoint GetBotReferenceWaypoint(Bot bot)
        {
            if (IsReturnPendingBot(bot, out ParkPodTask parkTask))
                return parkTask.StorageLocation;
            if (bot == null)
                return null;
            if (bot.CurrentWaypoint != null)
                return bot.CurrentWaypoint;
            if (Instance != null && Instance.WaypointGraph != null && bot.Tier != null)
                return Instance.WaypointGraph.GetClosestWaypoint(bot.Tier, bot.X, bot.Y);
            return null;
        }

        // borrowed: HADGSManager.cs:83-113
        private double EstimatePodStationDistance(Pod pod, OutputStation station)
        {
            if (pod == null || station == null || station.Waypoint == null)
                return double.PositiveInfinity;

            var podWaypoint = GetPodReferenceWaypoint(pod);

            double physDist;
            if (podWaypoint != null &&
                DistanceSet.ContainsKey(station.Waypoint.ID) &&
                DistanceSet[station.Waypoint.ID].ContainsKey(podWaypoint.ID))
            {
                physDist = DistanceSet[station.Waypoint.ID][podWaypoint.ID];
            }
            else
            {
                double podX = podWaypoint != null ? podWaypoint.X : pod.X;
                double podY = podWaypoint != null ? podWaypoint.Y : pod.Y;
                physDist = Math.Abs(podX - station.Waypoint.X) + Math.Abs(podY - station.Waypoint.Y);
            }

            if (_config != null && _config.UseBAED && podWaypoint != null)
            {
                double delaySec = RAWSimO.Core.Metrics.BAEDEstimator.ComputeEntryDelaySecondsForPodLeg(
                    Instance, podWaypoint, station.Waypoint);
                if (delaySec > 0.0)
                    physDist += _config.BAEDReferenceSpeed * delaySec;
            }

            return physDist;
        }

        // borrowed: HADGSManager.cs:115-126
        private RAWSimO.Core.Waypoints.Waypoint GetPodReferenceWaypoint(Pod pod)
        {
            if (pod == null)
                return null;
            if (pod.Waypoint != null)
                return pod.Waypoint;
            if (pod.Bot != null && pod.Bot.CurrentWaypoint != null)
                return pod.Bot.CurrentWaypoint;
            if (Instance != null && Instance.WaypointGraph != null && pod.Tier != null)
                return Instance.WaypointGraph.GetClosestWaypoint(pod.Tier, pod.X, pod.Y);
            return null;
        }

        // borrowed: HADGSManager.cs:128-139
        private HashSet<Bot> GenerateAvailableBots()
        {
            HashSet<Bot> availableBots = new HashSet<Bot>();
            foreach (var bot in Instance._outputstationbots)
            {
                if (bot.Pod == null && !Instance.ResourceManager._usedPods.ContainsValue(bot) && !Instance.ResourceManager.BottoPod.ContainsKey(bot))
                    availableBots.Add(bot);
                else if (CanUseReturnPendingBot(bot))
                    availableBots.Add(bot);
            }
            return availableBots;
        }

        // borrowed: HADGSManager.cs:141-150
        private bool CanUseReturnPendingBot(Bot bot)
        {
            if (_config == null || !_config.UseReturnPendingBots)
                return false;
            if (!IsReturnPendingBot(bot, out ParkPodTask parkTask))
                return false;
            if (Instance.ResourceManager.BottoPod.ContainsKey(bot))
                return false;
            return IsNearReturnLocation(bot, parkTask.StorageLocation);
        }

        // borrowed: HADGSManager.cs:152-159
        private bool IsReturnPendingBot(Bot bot, out ParkPodTask parkTask)
        {
            parkTask = bot?.CurrentTask as ParkPodTask;
            return parkTask != null &&
                bot.Pod != null &&
                parkTask.Pod == bot.Pod &&
                parkTask.StorageLocation != null;
        }

        // borrowed: HADGSManager.cs:161-180
        private bool IsNearReturnLocation(Bot bot, RAWSimO.Core.Waypoints.Waypoint storageLocation)
        {
            if (storageLocation == null)
                return false;
            if (bot.CurrentWaypoint == storageLocation)
                return true;
            if (bot.GetInfoDestinationWaypoint() == storageLocation)
                return true;

            var botWaypoint = bot.CurrentWaypoint;
            if (botWaypoint == null && Instance != null && Instance.WaypointGraph != null && bot.Tier != null)
                botWaypoint = Instance.WaypointGraph.GetClosestWaypoint(bot.Tier, bot.X, bot.Y);
            if (botWaypoint == null)
                return false;

            double threshold = _config.ReturnPendingDistanceThreshold;
            if (threshold < 0)
                return false;
            return Distances.CalculateShortestPath(botWaypoint, storageLocation, Instance) <= threshold;
        }

        /// <summary>
        /// Stores the available counts per SKU for a pod for on-the-fly assessment.
        /// (borrowed: HADGSManager.cs:194)
        /// </summary>
        private VolatileIDDictionary<ItemDescription, int> _availableCounts;

        /// <summary>
        /// Initializes some fields for pod selection. (borrowed: HADGSManager.cs:198-202)
        /// </summary>
        private void InitPodSelection()
        {
            if (_availableCounts == null)
                _availableCounts = new VolatileIDDictionary<ItemDescription, int>(Instance.ItemDescriptions.Select(i => new VolatileKeyValuePair<ItemDescription, int>(i, 0)).ToList());
        }

        /// <summary>
        /// Checks whether another order is assignable to the given station.
        /// (borrowed: HADGSManager.cs:216-217)
        /// </summary>
        /// <param name="station">The station to check.</param>
        /// <returns><code>true</code> if there is another open slot, <code>false</code> otherwise.</returns>
        private bool IsAssignable(OutputStation station)
        { return station.Active && station.CapacityReserved + station.CapacityInUse < station.Capacity; }

        /// <summary>
        /// Checks whether another order is assignable to the given station.
        /// (borrowed: HADGSManager.cs:219-224)
        /// </summary>
        /// <param name="station">The station to check.</param>
        /// <returns><code>true</code> if there is another open slot and another one reserved for fast-lane, <code>false</code> otherwise.</returns>
        private bool IsAssignableKeepFastLaneSlot(OutputStation station)
        { return station.Active && station.CapacityReserved + station.CapacityInUse < station.Capacity - 1; }

        /// <summary>
        /// 产生Od (borrowed: HADGSManager.cs:404-422)
        /// </summary>
        /// <param name="pendingOrders"></param>
        /// <returns></returns>
        public HashSet<Order> GenerateOd(HashSet<Order> pendingOrders)
        {
            foreach (Order order in pendingOrders)
                order.Timestay = order.DueTime - (Instance.SettingConfig.StartTime.AddSeconds(Convert.ToInt32(Instance.Controller.CurrentTime)) - order.TimePlaced).TotalSeconds;
            int i = 0;
            foreach (Order order in pendingOrders.OrderBy(v => v.Timestay).ThenBy(u => u.DueTime)) //先选剩余的截止时间最短的，再选开始时间最早的
            {
                order.sequence = i;
                i++;
            }
            HashSet<Order> Od = new HashSet<Order>();
            foreach (var order in pendingOrders.Where(v => v.Positions.Sum(line => Math.Min(Instance.ResourceManager.UnusedPods.Sum(pod => pod.CountAvailable(line.Key)), line.Value))
            == v.Positions.Sum(s => s.Value)))//保证Od中的所有order必须能被执行
            {
                if (order.Timestay < DueTimeOrderofMP)
                    Od.Add(order);
            }
            return Od;
        }

        /// <summary>
        /// Returns a list of relevant items for the given pod / output-station combination.
        /// (borrowed: HADGSManager.cs:447-467)
        /// </summary>
        /// <param name="pod">The pod in focus.</param>
        /// <param name="itemDemands">The station in focus.</param>
        /// <returns>A list of tuples of items to serve the respective extract-requests.</returns>
        internal List<ExtractRequest> GetPossibleRequests(Pod pod, IEnumerable<ExtractRequest> itemDemands)
        {
            // Init, if necessary
            InitPodSelection();
            // Match fitting items with requests
            List<ExtractRequest> requestsToHandle = new List<ExtractRequest>();
            // Get current content of the pod
            foreach (var item in itemDemands.Select(r => r.Item).Distinct())
                _availableCounts[item] = pod.CountAvailable(item);
            // First handle requests already assigned to the station
            foreach (var itemRequestGroup in itemDemands.GroupBy(r => r.Item))
            {
                // Handle as many requests as possible with the given SKU
                IEnumerable<ExtractRequest> possibleRequests = itemRequestGroup.Take(_availableCounts[itemRequestGroup.Key]);
                requestsToHandle.AddRange(possibleRequests);
                // Update content available in pod for the given SKU
                _availableCounts[itemRequestGroup.Key] -= possibleRequests.Count();
            }
            // Return the result
            return requestsToHandle;
        }

        /// <summary>Station validity filter honoring the FastLane slot (mirrors HADGSManager.cs:952).</summary>
        private bool ValidStation(OutputStation station)
        {
            bool fastLane = _config == null || _config.FastLane;   // default config has FastLane = true
            return fastLane ? IsAssignableKeepFastLaneSlot(station) : IsAssignable(station);
        }

        // ===================== SA-HADGS time/EST primitives =====================

        /// <summary>Speed used for distance→time conversion (config override or max bot velocity).</summary>
        private double NominalSpeed()
        {
            if (_config != null && _config.NominalSpeed > 0.0)
                return _config.NominalSpeed;
            double v = 0.0;
            foreach (var bot in Instance.Bots)
                if (bot.MaxVelocity > v) v = bot.MaxVelocity;
            return v > 0.0 ? v : 1.0;
        }

        /// <summary>
        /// ETA (seconds from now) for bot→pod→station including fixed pod handling.
        /// Unreachable combinations yield PositiveInfinity (never a large finite sentinel —
        /// SaHadgsScoring.TaPairScore's BIG separation assumes finite ETAs are travel-scale).
        /// </summary>
        private double EtaSeconds(Bot bot, Pod pod, OutputStation station)
        {
            double dist = EstimateBotPodDistance(bot, pod) + EstimatePodStationDistance(pod, station);
            if (double.IsInfinity(dist) || double.IsNaN(dist))
                return double.PositiveInfinity;
            double transfer = bot != null ? bot.PodTransferTime : 0.0;
            return StarveAwareCost.TravelTime(dist, _epochNominalSpeed) + 2.0 * transfer;
        }

        /// <summary>
        /// Current EST (seconds until starvation) of the station, including work committed earlier
        /// in this epoch that has not materialized into ExtractTasks yet (_localJobs).
        /// </summary>
        private double CurrentEst(OutputStation station, double now)
        {
            List<SlowStartController.StationWorkJob> jobs;
            _localJobs.TryGetValue(station, out jobs);
            return SlowStartController.ComputeStationWorkProjection(station, now, jobs).FirstStarveSec;
        }

        // ===================== POA pass =====================

        /// <summary>
        /// Assigns ONE most-urgent pending order fully coverable by the station's current inbound pods
        /// (zero marginal cost — no new pod/bot), then marks its items (HADGSManager.cs:528,550-583).
        /// </summary>
        private bool TryPoaAssignOnce(OutputStation station)
        {
            Order chosen = _pendingOrders
                .Where(o => o.Positions.All(p => _inboundPodsPerStation[station].Sum(pod => pod.CountAvailable(p.Key)) >= p.Value))
                .OrderBy(o => o.sequence)
                .FirstOrDefault();
            if (chosen == null)
                return false;
            AllocateOrder(chosen, station);
            MarkItemsOnInboundPods(chosen, station);
            return true;
        }

        /// <summary>Marks the chosen order's items on the station's inbound pods and registers the
        /// extract requests (verbatim commit path from HADGSManager.cs:550-583).</summary>
        private void MarkItemsOnInboundPods(Order chosenOrder, OutputStation chosenStation)
        {
            HashSet<ExtractRequest> itemDemands = new HashSet<ExtractRequest>();
            foreach (var req in Instance.ResourceManager.GetExtractRequestsOfOrder(chosenOrder))
                itemDemands.Add(req);
            foreach (var pod in _inboundPodsPerStation[chosenStation].OrderBy(v => EstimatePodStationDistance(v, chosenStation)))
            {
                if (itemDemands.Any(g => pod.IsAvailable(g.Item)))
                {
                    List<ExtractRequest> fittingRequests = GetPossibleRequests(pod, itemDemands);
                    foreach (var fittingRequest in fittingRequests)
                        itemDemands.Remove(fittingRequest);
                    foreach (var fittingRequest in fittingRequests)
                        pod.JustRegisterItem(fittingRequest.Item);
                    if (fittingRequests.Count > 0)
                    {
                        if (Instance.ResourceManager._Ziops1[chosenStation].ContainsKey(pod))
                            Instance.ResourceManager._Ziops1[chosenStation][pod].AddRange(fittingRequests);
                        else
                            Instance.ResourceManager._Ziops1[chosenStation].Add(pod, fittingRequests);
                    }
                }
            }
        }

        // ===================== epoch entry (water-fill wired in Task 4) =====================

        /// <summary>
        /// This is called to decide about potentially pending orders.
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
            InitPodSelection();
            // Sync inbound-pod view + release stuck RestTask pods (HADGSManager.cs:930-944)
            _inboundPodsPerStation.Clear();
            foreach (var oStation in Instance.OutputStations)
            {
                _inboundPodsPerStation[oStation] = new HashSet<Pod>(oStation.InboundPods);
                foreach (var pod in oStation.InboundPods)
                {
                    if (!Instance.ResourceManager.BottoPod.ContainsValue(pod) && Instance.ResourceManager._usedPods[pod].CurrentTask is RestTask)
                    {
                        _inboundPodsPerStation[oStation].Remove(pod);
                        Instance.ResourceManager.ReleasePod(pod);
                        oStation.UnregisterInboundPod(pod);
                        break;
                    }
                }
            }
            // Urgent-order mode (HADGSManager.cs:954-968). When the urgent set Od is large enough,
            // run the water-fill on Od only, then restore the full backlog MINUS the orders the
            // Od pass actually allocated (donor keeps a _pendingOrders1 mirror for this; we filter
            // the backup against the post-run remnant instead).
            HashSet<Order> Od = GenerateOd(_pendingOrders);
            if (Od.Count == 0 || Od.Count < Cs.Sum(v => v.Value))
                RunWaterFill();
            else
            {
                HashSet<Order> backup = new HashSet<Order>(_pendingOrders);
                _pendingOrders = new HashSet<Order>(Od);
                RunWaterFill();
                HashSet<Order> odRemnant = _pendingOrders;
                _pendingOrders = new HashSet<Order>(backup.Where(o => !Od.Contains(o) || odRemnant.Contains(o)));
            }
        }

        // ===================== candidate machinery =====================

        private sealed class SaCandidate
        {
            public Order Order;
            public List<Pod> Pods;
            public Dictionary<Pod, Bot> Assignment;
            public Dictionary<Pod, double> EtaByPod;
            public Dictionary<Pod, int> ItemsByPod;
            public double GapSec;
            public double SumTravelSec;
            public int CompletableOrders;
            public double Score;
        }

        /// <summary>
        /// Greedy set-cover for one order: repeatedly take the pod covering the most remaining demand
        /// (tie-break: shorter pod→station travel). Returns null if the pool cannot complete the order
        /// within maxPods. itemsByPod = marginal items each chosen pod contributes (work credit).
        /// </summary>
        private List<Pod> BuildCoverSetCoverageGreedy(Order order, OutputStation station,
            List<Pod> pool, int maxPods, out Dictionary<Pod, int> itemsByPod)
        {
            Dictionary<ItemDescription, int> remaining = RemainingDemand(order, station);
            itemsByPod = new Dictionary<Pod, int>();
            var chosen = new List<Pod>();
            var available = new List<Pod>(pool);
            while (remaining.Count > 0 && chosen.Count < maxPods)
            {
                Pod best = null; int bestCover = 0; double bestDist = double.PositiveInfinity;
                foreach (var pod in available)
                {
                    int cover = 0;
                    foreach (var kv in remaining)
                        cover += Math.Min(pod.CountAvailable(kv.Key), kv.Value);
                    if (cover <= 0) continue;
                    double dist = EstimatePodStationDistance(pod, station);
                    if (cover > bestCover || (cover == bestCover && dist < bestDist))
                    { best = pod; bestCover = cover; bestDist = dist; }
                }
                if (best == null) { itemsByPod = null; return null; }
                chosen.Add(best); available.Remove(best);
                itemsByPod[best] = TakeFromRemaining(best, remaining);
            }
            if (remaining.Count > 0) { itemsByPod = null; return null; }
            return chosen;
        }

        /// <summary>
        /// ETA-greedy variant: walk pods by ascending pod→station travel time, take any pod that
        /// contributes, until covered. Favors continuity over minimal set size.
        /// </summary>
        private List<Pod> BuildCoverSetEtaGreedy(Order order, OutputStation station,
            List<Pod> pool, int maxPods, out Dictionary<Pod, int> itemsByPod)
        {
            Dictionary<ItemDescription, int> remaining = RemainingDemand(order, station);
            itemsByPod = new Dictionary<Pod, int>();
            var chosen = new List<Pod>();
            foreach (var pod in pool.OrderBy(p => EstimatePodStationDistance(p, station)))
            {
                if (remaining.Count == 0 || chosen.Count >= maxPods) break;
                int contributed = TakeFromRemaining(pod, remaining);
                if (contributed > 0) { chosen.Add(pod); itemsByPod[pod] = contributed; }
            }
            if (remaining.Count > 0) { itemsByPod = null; return null; }
            return chosen;
        }

        /// <summary>Order demand not already supplied by the station's inbound pods.</summary>
        private Dictionary<ItemDescription, int> RemainingDemand(Order order, OutputStation station)
        {
            var remaining = new Dictionary<ItemDescription, int>();
            foreach (var pos in order.Positions)
            {
                int fromInbound = _inboundPodsPerStation[station].Sum(p => p.CountAvailable(pos.Key));
                int need = pos.Value - fromInbound;
                if (need > 0) remaining[pos.Key] = need;
            }
            return remaining;
        }

        /// <summary>Deduct what the pod can supply from remaining demand; returns items taken.</summary>
        private static int TakeFromRemaining(Pod pod, Dictionary<ItemDescription, int> remaining)
        {
            int contributed = 0;
            foreach (var key in remaining.Keys.ToList())
            {
                int take = Math.Min(pod.CountAvailable(key), remaining[key]);
                if (take > 0)
                {
                    contributed += take;
                    remaining[key] -= take;
                    if (remaining[key] == 0) remaining.Remove(key);
                }
            }
            return contributed;
        }

        /// <summary>
        /// TA: regret-greedy bot↔pod matching on TaPairScore (on-time lexicographically first).
        /// Returns null if not enough bots or any pod is unreachable.
        /// </summary>
        private Dictionary<Pod, Bot> AssignBots(List<Pod> pods, List<Bot> bots, OutputStation station,
            double estSec, out Dictionary<Pod, double> etaByPod, out double sumTravelSec)
        {
            etaByPod = null; sumTravelSec = 0.0;
            if (pods.Count > bots.Count) return null;
            double[,] eta = new double[pods.Count, bots.Count];
            double[,] score = new double[pods.Count, bots.Count];
            for (int p = 0; p < pods.Count; p++)
                for (int b = 0; b < bots.Count; b++)
                {
                    eta[p, b] = EtaSeconds(bots[b], pods[p], station);
                    score[p, b] = SaHadgsScoring.TaPairScore(eta[p, b], estSec, _config != null ? _config.FeasibilitySlackSec : 0.0);
                }
            int[] asg = SaHadgsScoring.RegretAssign(score);
            if (asg == null) return null;
            var result = new Dictionary<Pod, Bot>();
            etaByPod = new Dictionary<Pod, double>();
            for (int p = 0; p < pods.Count; p++)
            {
                if (double.IsPositiveInfinity(eta[p, asg[p]])) { etaByPod = null; return null; }
                result[pods[p]] = bots[asg[p]];
                etaByPod[pods[p]] = eta[p, asg[p]];
                sumTravelSec += eta[p, asg[p]];
            }
            return result;
        }

        /// <summary>How many of the topK orders the inbound∪set stock can fully cover (greedy, in sequence order).</summary>
        private int CountCompletableTopK(List<Order> topK, OutputStation station, List<Pod> set)
        {
            var avail = new Dictionary<ItemDescription, int>();
            foreach (var p in _inboundPodsPerStation[station].Concat(set))
                foreach (var item in p.ItemDescriptionsContained)
                {
                    int c; avail.TryGetValue(item, out c);
                    avail[item] = c + p.CountAvailable(item);
                }
            int n = 0;
            foreach (var o in topK)
            {
                bool ok = true;
                foreach (var pos in o.Positions)
                { int c; if (!avail.TryGetValue(pos.Key, out c) || c < pos.Value) { ok = false; break; } }
                if (ok)
                {
                    n++;
                    foreach (var pos in o.Positions) avail[pos.Key] -= pos.Value;
                }
            }
            return n;
        }

        /// <summary>
        /// Evaluates ≤ TopKOrders × ≤2 cover-set variants for the station and returns the best
        /// weighted-score candidate (spec §4.2), or null if nothing is buildable.
        /// </summary>
        private SaCandidate SelectBestCandidate(OutputStation station, HashSet<Bot> Ra, double now)
        {
            double est = CurrentEst(station, now);
            var topK = _pendingOrders
                .Where(o => o.Positions.All(p =>
                    Instance.ResourceManager.UnusedPods.Concat(_inboundPodsPerStation[station]).Sum(pod => pod.CountAvailable(p.Key)) >= p.Value))
                .OrderBy(o => o.sequence)
                .Take(Math.Max(1, _config != null ? _config.TopKOrders : 3))
                .ToList();
            if (topK.Count == 0) return null;
            var pool = Instance.ResourceManager.UnusedPods
                .Where(p => !_selectedPods.Contains(p) && !Instance.ResourceManager.BottoPod.ContainsValue(p))
                .ToList();
            if (pool.Count == 0) return null;
            var bots = Ra.ToList();
            if (bots.Count == 0) return null;
            double itt = station.ItemTransferTime;
            double orderReward = _config != null ? _config.OrderRewardSec : 60.0;
            double travelWeight = _config != null ? _config.TravelTimeWeight : 0.1;
            bool useVariant = _config == null || _config.UseEtaGreedyVariant;

            SaCandidate best = null;
            foreach (var order in topK)
            {
                for (int variant = 0; variant < 2; variant++)
                {
                    if (variant == 1 && !useVariant) break;
                    Dictionary<Pod, int> itemsByPod;
                    List<Pod> set = variant == 0
                        ? BuildCoverSetCoverageGreedy(order, station, pool, bots.Count, out itemsByPod)
                        : BuildCoverSetEtaGreedy(order, station, pool, bots.Count, out itemsByPod);
                    if (set == null || set.Count == 0) continue;

                    Dictionary<Pod, double> etaByPod; double sumTravel;
                    var assignment = AssignBots(set, bots, station, est, out etaByPod, out sumTravel);
                    if (assignment == null) continue;

                    double gap = SaHadgsScoring.ProjectedGapSeconds(est,
                        set.Select(p => (etaByPod[p], itemsByPod[p] * itt)));
                    int completable = CountCompletableTopK(topK, station, set);
                    double score = SaHadgsScoring.CandidateScore(
                        completable, gap, sumTravel, orderReward, travelWeight);
                    if (best == null || score < best.Score)
                        best = new SaCandidate
                        {
                            Order = order, Pods = set, Assignment = assignment, EtaByPod = etaByPod,
                            ItemsByPod = itemsByPod, GapSec = gap, SumTravelSec = sumTravel,
                            CompletableOrders = completable, Score = score
                        };
                }
            }
            return best;
        }

        /// <summary>
        /// Commits a candidate: claim pods/bots (mirrors HADGSManager.cs:857-865), then records the
        /// committed work into _localJobs so subsequent EST projections see it.
        /// </summary>
        private void CommitCandidate(SaCandidate cand, OutputStation station, HashSet<Bot> Ra, double now)
        {
            foreach (var kv in cand.Assignment)
            {
                Pod pod = kv.Key; Bot bot = kv.Value;
                Ra.Remove(bot);
                _inboundPodsPerStation[station].Add(pod);
                _selectedPods.Add(pod);
                station.RegisterInboundPod(pod);
                Instance.ResourceManager.BottoPod.Add(bot, pod);
                Instance.ResourceManager.ClaimPod(pod, bot, BotTaskType.Extract);
                List<SlowStartController.StationWorkJob> jobs;
                if (!_localJobs.TryGetValue(station, out jobs))
                    _localJobs[station] = jobs = new List<SlowStartController.StationWorkJob>();
                jobs.Add(new SlowStartController.StationWorkJob
                {
                    ArrivalAbs = now + cand.EtaByPod[pod],
                    BaseItems = cand.ItemsByPod[pod],
                    Pod = pod,
                    ArrivalConfirmed = false
                });
            }
            // On-time vs late commit stats (printed via InstanceStatistics).
            if (cand.GapSec <= 0.0) Instance.StatSaHadgsOnTimeCommits++;
            else { Instance.StatSaHadgsLateCommits++; Instance.StatSaHadgsLatenessSumSec += cand.GapSec; }
        }

        /// <summary>Cached per-epoch nominal speed (avoids per-ETA fleet scans).</summary>
        private double _epochNominalSpeed = 1.0;

        /// <summary>Min-EST water-filling main loop (spec §4.1).</summary>
        private void RunWaterFill()
        {
            double now = Instance.Controller.CurrentTime;
            _localJobs.Clear();
            _selectedPods = new HashSet<Pod>();
            _epochNominalSpeed = NominalSpeed();
            HashSet<Bot> Ra = GenerateAvailableBots();

            // Min-heap on (EST, station.ID) — SortedSet gives O(log n) pop-min with unique keys.
            var heap = new SortedSet<Tuple<double, int>>();
            var stationById = new Dictionary<int, OutputStation>();
            foreach (var s in Instance.OutputStations.Where(st => ValidStation(st)))
            {
                heap.Add(Tuple.Create(CurrentEst(s, now), s.ID));
                stationById[s.ID] = s;
            }

            int guard = 50 * Math.Max(1, Instance.OutputStations.Count)
                          * Math.Max(1, Instance.OutputStations.Sum(s => s.Capacity));
            while (heap.Count > 0 && guard-- > 0)
            {
                var top = heap.Min; heap.Remove(top);
                OutputStation s = stationById[top.Item2];
                if (!ValidStation(s)) continue;

                bool progress = false;
                // ① POA: zero-marginal-cost orders first
                while (ValidStation(s) && TryPoaAssignOnce(s)) progress = true;
                // ② PPS/TA: best weighted candidate
                if (ValidStation(s) && Ra.Count > 0 && _pendingOrders.Count > 0)
                {
                    var cand = SelectBestCandidate(s, Ra, now);
                    if (cand != null)
                    {
                        CommitCandidate(cand, s, Ra, now);
                        while (ValidStation(s) && TryPoaAssignOnce(s)) { }
                        progress = true;
                    }
                }
                // ③ refresh EST and requeue only if we progressed and can still take work
                if (progress && ValidStation(s))
                    heap.Add(Tuple.Create(CurrentEst(s, now), s.ID));
            }
        }

        #region IOptimize Members
        /// <summary>
        /// Signals the current time to the mechanism.
        /// </summary>
        /// <param name="currentTime">The current simulation time.</param>
        public override void SignalCurrentTime(double currentTime) { /* Always ready. */ }
        #endregion

        #region Custom stat tracking
        /// <summary>The callback indicates a reset of the statistics.</summary>
        public override void StatReset() { }
        /// <summary>The callback indicates the simulation finished.</summary>
        public override void StatFinish() { }
        #endregion
    }
}
