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
        { return _config.FastLane ? IsAssignableKeepFastLaneSlot(station) : IsAssignable(station); }

        // ===================== SA-HADGS time/EST primitives =====================

        /// <summary>Speed used for distance→time conversion (config override or max bot velocity).</summary>
        private double NominalSpeed()
        {
            if (_config.NominalSpeed > 0.0)
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
            return StarveAwareCost.TravelTime(dist, NominalSpeed()) + 2.0 * transfer;
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
            // Urgent-order mode (HADGSManager.cs:954-968)
            HashSet<Order> Od = GenerateOd(_pendingOrders);
            HashSet<Order> backup = new HashSet<Order>(_pendingOrders);
            if (Od.Count == 0 || Od.Count < Cs.Sum(v => v.Value))
                RunWaterFill();
            else
            {
                _pendingOrders.Clear();
                _pendingOrders = new HashSet<Order>(Od);
                RunWaterFill();
                _pendingOrders.Clear();
                _pendingOrders = new HashSet<Order>(backup);
            }
        }

        /// <summary>Min-EST water-filling main loop (full version lands in Task 4; this placeholder
        /// keeps the build green with POA-only behavior).</summary>
        private void RunWaterFill()
        {
            double now = Instance.Controller.CurrentTime;
            _localJobs.Clear();
            _selectedPods = new HashSet<Pod>();
            foreach (var station in Instance.OutputStations.Where(s => ValidStation(s)).OrderBy(s => CurrentEst(s, now)))
                while (ValidStation(station) && TryPoaAssignOnce(station)) { }
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
