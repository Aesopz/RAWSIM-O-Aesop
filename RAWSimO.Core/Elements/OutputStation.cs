using RAWSimO.Core.Control;
using RAWSimO.Core.Geometrics;
using RAWSimO.Core.Info;
using RAWSimO.Core.Interfaces;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.Core.Statistics;
using RAWSimO.Core.Waypoints;
using RAWSimO.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace RAWSimO.Core.Elements
{
    /// <summary>
    /// Implements the output-station, i.e. the picking station.
    /// </summary>
    public class OutputStation : Circle, IUpdateable, IOutputStationInfo, IQueuesOwner, IExposeVolatileID
    {
        #region Constructors

        /// <summary>
        /// Creates a new output-station.
        /// </summary>
        /// <param name="instance">The instance this station belongs to.</param>
        internal OutputStation(Instance instance) : base(instance) { }

        #endregion

        #region Core

        /// <summary>
        /// Indicates whether this station is active
        /// </summary>
        public bool Active { get; private set; } = true;
        /// <summary>
        /// Activates this station.
        /// </summary>
        public void Activate()
        {
            // Make available
            Active = true;
            // Mark new situation to manager
            Instance?.Controller?.OrderManager?.SignalStationActivated(this);
            // Track up-time from the current time on
            _lastActiveMeasurement = Instance.Controller != null ? Instance.Controller.CurrentTime : 0;
        }
        /// <summary>
        /// Deactivates this station.
        /// </summary>
        public void Deactivate()
        {
            // Make unavailable
            Active = false;
            // Finish tracking of up-time
            if (_lastActiveMeasurement < (Instance.Controller != null ? Instance.Controller.CurrentTime : 0))
                StatActiveTime += (Instance.Controller != null ? Instance.Controller.CurrentTime : 0) - _lastActiveMeasurement;
        }

        /// <summary>
        /// The time it takes to pick one item from a pod and put it into the order tote.
        /// </summary>
        public double ItemTransferTime;

        /// <summary>
        /// The time it takes to pick one item from a pod (excluding the time it takes to put it into a tote - after this the robot is free to leave, if it does not have other items to be picked).
        /// </summary>
        public double ItemPickTime;

        /// <summary>
        /// The time it takes to complete an order.
        /// </summary>
        public double OrderCompletionTime;

        /// <summary>
        /// The waypoint this output-station is located at.
        /// </summary>
        public Waypoint Waypoint;

        /// <summary>
        /// The order ID of this station that defines the sequence in which the stations have to be activated.
        /// </summary>
        public int ActivationOrderID;

        /// <summary>
        /// The capacity of this station.
        /// </summary>
        public int Capacity;

        /// <summary>
        /// The capacity currently in use at this station.
        /// </summary>
        public int CapacityInUse { get { return _assignedOrders.Count; } }

        /// <summary>
        /// (SlotOccupancy) When each currently-assigned order took its slot. Keyed on the object
        /// that actually holds the slot, which for a split order is the CHILD, not the parent -
        /// the parent never occupies a picking-station slot, it only waits downstream for
        /// consolidation. Measurement only; nothing reads it back into a decision.
        /// </summary>
        private readonly Dictionary<Order, double> _slotTakenAt = new Dictionary<Order, double>();
        /// <summary>(SlotOccupancy) Orders whose first pick has already been timed.</summary>
        private readonly HashSet<Order> _firstPickSeen = new HashSet<Order>();

        /// <summary>
        /// (SlotOccupancy) Times the wait for supply: from taking the slot to the first item of
        /// this order actually being picked. Idempotent per order.
        /// </summary>
        private void NoteFirstPick(Order order, double currentTime)
        {
            if (order == null || !_firstPickSeen.Add(order)) return;
            double takenAt;
            if (_slotTakenAt.TryGetValue(order, out takenAt))
                Instance._statSlotFirstPickWaits.Add(currentTime - takenAt);
        }

        /// <summary>
        /// The amount of capacity reserved by a controller.
        /// </summary>
        internal int CapacityReserved { get { return _registeredOrders.Count; } }

        /// <summary>
        /// The orders currently assigned to this station.
        /// </summary>
        private HashSet<Order> _assignedOrders = new HashSet<Order>();
        /// <summary>
        /// The set of orders not yet allocated but already registered with this station.
        /// </summary>
        private HashSet<Order> _registeredOrders = new HashSet<Order>();
        /// <summary>
        /// The set of orders queued for this station.
        /// </summary>
        private HashSet<Order> _queuedOrders = new HashSet<Order>();

        /// <summary>
        /// The orders currently assigned to this station.
        /// </summary>
        public IEnumerable<Order> AssignedOrders { get { return _assignedOrders; } }
        /// <summary>
        /// The orders currently queued to this station.
        /// </summary>
        public IEnumerable<Order> QueuedOrders { get { return _queuedOrders; } }

        /// <summary>
        /// Checks whether the specified order can be added for reservation to this station.
        /// </summary>
        /// <param name="order">The order that has to be checked.</param>
        /// <returns><code>true</code> if the bundle fits, <code>false</code> otherwise.</returns>
        public bool FitsForReservation(Order order) { return CapacityInUse + CapacityReserved + 1 <= Capacity; }

        /// <summary>
        /// Reserves capacity of this station for the given order. The reserved capacity will be maintained when the order is allocated.
        /// </summary>
        /// <param name="order">The order for which capacity shall be reserved.</param>
        internal void RegisterOrder(Order order)
        {
            _registeredOrders.Add(order);
            if (CapacityInUse + CapacityReserved > Capacity)
                throw new InvalidOperationException("Cannot reserve more capacity than this station has!");

        }
        /// <summary>
        /// Remove the order from queue in for this station.
        /// </summary>
        /// <param name="order">The order to queue in.</param>
        internal void RemoveQueueOrder(Order order)
        {
            StatCurrentlyOpenQueuedItems -= order.Requests.Count();
            _queuedOrders.Remove(order);
        }

        /// <summary>
        /// The order to queue in for this station.
        /// </summary>
        /// <param name="order">The order to queue in.</param>
        internal void QueueOrder(Order order)
        {
            StatCurrentlyOpenQueuedItems += order.Requests.Count();
            _queuedOrders.Add(order);
        }
        /// <summary>
        /// Clser the order to queue in for this station.
        /// </summary>
        internal void ClserQueueOrder()
        {
            _queuedOrders.Clear();
        }
        /// <summary>
        /// Assigns a new order to this station.
        /// </summary>
        /// <param name="order">The order to assign to this station.</param>
        /// <returns><code>true</code> if the order was successfully assigned, <code>false</code> otherwise.</returns>
        public bool AssignOrder(Order order)
        {
            if (_assignedOrders.Count < Capacity)
            {
                // Assign the order
                _assignedOrders.Add(order);
                // (SlotOccupancy) Stamp the moment this order started blocking a slot.
                _slotTakenAt[order] = Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;
                // Remove the bundle from the reservation list
                _registeredOrders.Remove(order);
                // Notify the instance about the order
                Instance.NotifyOrderAllocated(this, order);
                // Keep track of current number of items to pick
                StatCurrentlyOpenItems += order.Positions.Sum(p => p.Value);
                // Remove order from queue, if it came from the queue
                //if (_queuedOrders.Contains(order))
                //{
                //    StatCurrentlyOpenQueuedItems -= order.Requests.Count();
                //    _queuedOrders.Remove(order);
                //}
                // Reset rest-time
                _statDepletionTime = double.PositiveInfinity;
                // Update the order list for the visualization, if present
                if (Instance.SettingConfig.VisualizationAttached)
                    lock (_syncRoot)
                        _openOrders.Add(order);
                // Return success
                return true;
            }
            else
            {
                // Return fail
                return false;
            }
        }
        /// <summary>
        /// The queue of items to extract from the pods.
        /// </summary>
        private Queue<ExtractRequest> _requestsExtract = new Queue<ExtractRequest>();
        /// <summary>
        /// The queue of bots per item extraction request.
        /// </summary>
        private Queue<Bot> _requestsBot = new Queue<Bot>();

        /// <summary>
        /// Picks the next enqueued item.
        /// </summary>
        /// <param name="currentTime">The current simulation time.</param>
        /// <returns><code>true</code> if there was an item to pick and the operation was successful, <code>false</code> otherwise.</returns>
        protected bool TakeItemFromPod(double currentTime)
        {
            //if(_requestsExtract.Count == 0 && _assignedOrders.Count > 0)
            //    Thread.Sleep(1);
            // Keep going through queue until have something to take or done with queue
            while (_requestsExtract.Count > 0)
            {
                // Fetch necessary stuff
                Bot bot = _requestsBot.Dequeue();
                Pod pod = bot.Pod;
                ExtractRequest request = _requestsExtract.Dequeue();
                ItemDescription item = request.Item;

                if (pod.IsContained(item) && GetDistance(pod) < GetInfoRadius())
                {
                    // If order is null, then just choose the first one that fits 
                    if (request.Order == null)
                    {
                        foreach (var order in _assignedOrders)
                            if (order.Serve(item))
                            {
                                // KPI: record this pod-visit served this order
                                if (bot.CurrentTask is Control.ExtractTask _et0)
                                    _et0.ServedOrdersThisVisit.Add(order);
                                NoteFirstPick(order, currentTime);
                                // Physically remove the item
                                pod.Remove(item, request);
                                // Block the station for the transfer
                                BlockedUntil = currentTime + ItemTransferTime;
                                // Make the bot wait until completion
                                bot.WaitUntil(currentTime + ItemPickTime);
                                // Track when the currently processed pod can leave the station.
                                UpdateCurrentProcessingPodRelease(bot, currentTime);
                                // (M2e-IC / D16) one pick left on this task -> wake the order
                                // manager so a re-solve can extend the pod while the on-the-fly
                                // window (Requests.Any) is still open. Gated on the buffer's own
                                // WakeOnLastPick flag, NOT on the buffer existing: other managers
                                // now create buffers purely to count boxes, and must stay inert here.
                                if (Instance.PackingBuffer != null && Instance.PackingBuffer.WakeOnLastPick
                                    && bot.CurrentTask is Control.ExtractTask _icNr0
                                    && _icNr0.Requests != null && _icNr0.Requests.Count == 1)
                                    Instance.Controller.OrderManager.SignalOrderFinished(null, this);
                                // Count the number of picked items
                                StatNumItemsPicked++;
                                // Keep track of injected item picks
                                if (request.StatInjected)
                                    StatNumInjectedItemsPicked++;
                                // Keep track of current number of items to pick
                                StatCurrentlyOpenItems--;
                                // Count pods served if this is a beginning transaction
                                if (_newPodTransaction) { RecordPodHandoffGap(currentTime); _newPodTransaction = false; }
                                // Notify instance about the pick
                                Instance.NotifyItemHandled(pod, bot, this, request.Item);
                                Instance.NotifyPodHandled(pod, null, this);
                                // Notify the instance, if the line was completed by the pick
                                if (order.PositionServedCount(item) >= order.PositionOverallCount(item))
                                    Instance.NotifyLineHandled(this, item, order.PositionOverallCount(item));
                                // Keep track of the time at which the transaction will be finished
                                _statLastTimeTransactionFinished = currentTime + ItemTransferTime;
                                // Return success
                                return true;
                            }
                        // Mark request aborted
                        request.Abort();
                    }
                    else
                    {
                        // Order is specified
                        // If it's at this station and the item can be added, then add it
                        if (_assignedOrders.Contains(request.Order) && request.Order.Serve(item))
                        {
                            // KPI: record this pod-visit served this order
                            if (bot.CurrentTask is Control.ExtractTask _et1)
                                _et1.ServedOrdersThisVisit.Add(request.Order);
                            NoteFirstPick(request.Order, currentTime);
                            // Physically remove the item
                            pod.Remove(item, request);
                            // Block the station for the transfer
                            BlockedUntil = currentTime + ItemTransferTime;
                            // Make the bot wait until completion
                            bot.WaitUntil(currentTime + ItemPickTime);
                            // Track when the currently processed pod can leave the station.
                            UpdateCurrentProcessingPodRelease(bot, currentTime);
                            // (M2e-IC / D16) see the twin branch above.
                            if (Instance.PackingBuffer != null && Instance.PackingBuffer.WakeOnLastPick
                                    && bot.CurrentTask is Control.ExtractTask _icNr1
                                && _icNr1.Requests != null && _icNr1.Requests.Count == 1)
                                Instance.Controller.OrderManager.SignalOrderFinished(null, this);
                            // Count the number of picked items
                            StatNumItemsPicked++;
                            // Keep track of injected item picks
                            if (request.StatInjected)
                                StatNumInjectedItemsPicked++;
                            // Keep track of current number of items to pick
                            StatCurrentlyOpenItems--;
                            // Count pods served if this is a beginning transaction
                            if (_newPodTransaction) { RecordPodHandoffGap(currentTime); _newPodTransaction = false; }
                            // Notify instance about the pick
                            Instance.NotifyItemHandled(pod, bot, this, request.Item);
                            Instance.NotifyPodHandled(pod, null, this);
                            // Notify the instance, if the line was completed by the pick
                            if (request.Order.PositionServedCount(item) >= request.Order.PositionOverallCount(item))
                                Instance.NotifyLineHandled(this, item, request.Order.PositionOverallCount(item));
                            // Keep track of the time at which the transaction will be finished
                            _statLastTimeTransactionFinished = currentTime + ItemTransferTime;
                            // Return success
                            return true;
                        }
                        else
                        {
                            // Mark the request aborted
                            request.Abort();
                        }
                    }
                }
            }
            // Nothing to pick - return unsuccessfully
            return false;
        }

        /// <summary>
        /// Completes an order that is ready, if there is one.
        /// </summary>
        /// <param name="currentTime">The current simulation time.</param>
        /// <returns>The completed order if there was one, <code>null</code> otherwise.</returns>
        protected Order RemoveAnyCompletedOrder(double currentTime)
        {
            // Remove any orders that are finished
            Order finishedOrder = null;
            foreach (var order in _assignedOrders)
                if (order.IsCompleted())
                {
                    finishedOrder = order;
                    StatNumOrdersFinished++;
                    finishedOrder.TimeStampCompleted = currentTime;
                    // Notify the item manager about this
                    Instance.ItemManager.CompleteOrder(finishedOrder);
                    if (finishedOrder.Parent != null)
                    {
                        // Split child: only bookkeeping towards the parent; parent-level KPI fires
                        // once ALL children are done (consolidation), attributed to this station.
                        Order parent = finishedOrder.Parent;
                        if (parent.NotifyChildCompleted(finishedOrder))
                        {
                            parent.TimeStampCompleted = currentTime;
                            Instance.ItemManager.CompleteOrder(parent);
                            Instance.NotifyOrderCompleted(parent, this);
                            // (M2e-IC) consolidation releases the parent's packing box.
                            // Null-safe no-op for every manager that does not create the buffer.
                            if (Instance.PackingBuffer != null)
                                Instance.PackingBuffer.ReleaseParent(parent.ID);
                        }
                        else
                        {
                            // Non-final child: no parent-level KPI event fires, but the freed station
                            // slot must still wake the order manager (baseline parity).
                            Instance.Controller.OrderManager.SignalOrderFinished(finishedOrder, this);
                        }
                    }
                    else
                    {
                        // Notify completed order
                        Instance.NotifyOrderCompleted(finishedOrder, this);
                    }
                    // Break early and block action
                    BlockedUntil = currentTime + OrderCompletionTime;
                    break;
                }
            // Check if the station has no further assigned orders and may rest now
            if (!_assignedOrders.Any())
                _statDepletionTime = currentTime;
            // Remove the finished order from the todo-list
            // (SlotOccupancy) The slot is freed here, so this is where its holding time ends.
            if (finishedOrder != null)
            {
                double takenAt;
                if (_slotTakenAt.TryGetValue(finishedOrder, out takenAt))
                {
                    Instance._statSlotOccupancyTimes.Add(currentTime - takenAt);
                    _slotTakenAt.Remove(finishedOrder);
                    _firstPickSeen.Remove(finishedOrder);
                }
            }
            _assignedOrders.Remove(finishedOrder);
            if (Instance.SettingConfig.VisualizationAttached && finishedOrder != null)
            {
                lock (_syncRoot)
                {
                    // Add order to the completed order list
                    _completedOrders.Add(finishedOrder);
                    // Remove it from the open list
                    _openOrders.Remove(finishedOrder);
                }
            }
            // Return either the completed order or null to signal no order could be completed
            return finishedOrder;
        }

        /// <summary>
        /// Requests the station to pick the given item for the given order.
        /// </summary>
        /// <param name="bot">The bot that requests the pick.</param>
        /// <param name="request">The request to handle.</param>
        public void RequestItemTake(Bot bot, ExtractRequest request)
        {
            if (bot.Pod != null && _assignedOrders.Contains(request.Order))
            {
                // Add the request to the list of requests to handle
                _requestsBot.Enqueue(bot);
                _requestsExtract.Enqueue(request);
            }
            else
            {
                // Something went wrong, refuse to handle the request
                request.Abort();
                bot.WaitUntil(Instance.Controller.CurrentTime + Instance.RefusedRequestPenaltyTime);
            }
        }

        /// <summary>
        /// Contains all pods currently inbound for this station.
        /// </summary>
        private HashSet<Pod> _inboundPods = new HashSet<Pod>();
        /// <summary>
        /// Marks a pod as inbound for a station.
        /// </summary>
        /// <param name="pod">The pod that being brought to the station.</param>
        internal void RegisterInboundPod(Pod pod) { _inboundPods.Add(pod); }
        /// <summary>
        /// Removes a pod from the list of inbound pods.
        /// </summary>
        /// <param name="pod">The pod that is not inbound anymore.</param>
        internal void UnregisterInboundPod(Pod pod) { _inboundPods.Remove(pod); }
        /// <summary>
        /// All pods currently approaching the station.
        /// </summary>
        internal IEnumerable<Pod> InboundPods { get { return _inboundPods; } }

        /// <summary>
        /// All extract tasks that are currently carried out by robots for this station.
        /// </summary>
        private HashSet<ExtractTask> _activeExtractTasks = new HashSet<ExtractTask>();
        private object _activeExtractTasksSyncRoot = new object();
        /// <summary>
        /// Register an extract task with this station.
        /// </summary>
        /// <param name="task">The task that shall be done at this station.</param>
        internal void RegisterExtractTask(ExtractTask task) { lock (_activeExtractTasksSyncRoot) { _activeExtractTasks.Add(task); } }
        /// <summary>
        /// Unregister an extract task with this station.
        /// </summary>
        /// <param name="task">The task that was done or cancelled for this station.</param>
        internal void UnregisterExtractTask(ExtractTask task) { lock (_activeExtractTasksSyncRoot) { _activeExtractTasks.Remove(task); } }
        /// <summary>
        /// Read-only enumeration of all extract tasks currently registered for this station
        /// (covers both "bot moving toward pod" and "bot carrying pod toward station").
        /// Used by SlowStartController to compute T_starve.
        /// </summary>
        public IEnumerable<Control.ExtractTask> GetActiveExtractTasks() { lock (_activeExtractTasksSyncRoot) { return _activeExtractTasks.ToList(); } }

        /// <summary>
        /// Number of item-pick requests already queued at this station (bot+pod present, awaiting service).
        /// Used by SlowStartController to compute T_starve.
        /// </summary>
        public int PendingItemRequestCount { get { return _requestsExtract.Count; } }

        private double _currentProcessingPodReleaseUntil = double.NaN;

        private void UpdateCurrentProcessingPodRelease(Bot bot, double currentTime)
        {
            var extractTask = bot != null ? bot.CurrentTask as ExtractTask : null;
            int remainingItems = extractTask != null && extractTask.Requests != null
                ? Math.Max(1, extractTask.Requests.Count)
                : 1;
            _currentProcessingPodReleaseUntil = currentTime + (remainingItems - 1) * ItemTransferTime + ItemPickTime;
        }

        /// <summary>
        /// Returns the station's blocked-until time as an absolute simulation timestamp.
        /// Returns NaN when not currently blocked. Used by SlowStartController to compute
        /// "station busy until" inside T_starve.
        /// </summary>
        public double GetBlockedUntilTime() { return BlockedUntil; }

        private double GetIdleDuration(double lastTime, double currentTime)
        {
            if (currentTime <= lastTime)
                return 0.0;
            if (currentTime <= BlockedUntil)
                return 0.0;
            return currentTime - Math.Max(lastTime, BlockedUntil);
        }

        private bool HasReadyOrQueuedExtractPod()
        {
            if (_requestsExtract.Count > 0)
                return true;

            foreach (var task in GetActiveExtractTasks())
            {
                if (task == null || task.OutputStation != this || task.ReservedPod == null || task.Requests == null || !task.Requests.Any())
                    continue;

                var bot = task.Bot as RAWSimO.Core.Bots.BotNormal;
                if (bot == null || bot.Pod != task.ReservedPod)
                    continue;

                if (bot.IsQueueing || bot.CurrentWaypoint == Waypoint || GetDistance(bot.Pod) < GetInfoRadius())
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Indicates whether the order backlog still holds pending (not-yet-assigned) orders that could be
        /// fed to this station. Used to tell "supply lag" (Type A — work exists, pod not here yet) apart
        /// from "no-order waste" (Type B — nothing to do at all) when the station is idle.
        /// </summary>
        private bool HasServeablePendingDemand()
        {
            var orderManager = Instance != null && Instance.Controller != null ? Instance.Controller.OrderManager : null;
            return orderManager != null
                && orderManager.BacklogSnapshot != null
                && orderManager.BacklogSnapshot.Count > 0;
        }

        /// <summary>
        /// All extract tasks that are registered for being done at this station.
        /// </summary>
        IEnumerable<ExtractTask> ActiveTasks { get { return GetActiveExtractTasks(); } }

        /// <summary>
        /// Register a newly approached bot before picking begins for statistical purposes.
        /// </summary>
        /// <param name="bot">The bot that just approached the station.</param>
        public void RegisterBot(Bot bot)
        {
            _newPodTransaction = true;
            // Track the time it took to serve the last pod
            if (!double.IsNaN(_statLastTimeNewPod))
            {
                if (_statPodHandling == null)
                    _statPodHandling = StatInitPodHandlingDataPoint(_statLastTimeTransactionFinished - _statLastTimeNewPod);
                else
                    StatisticsHelper.UpdateAvgVarData(
                        ref _statPodHandling.PodsHandled,
                        ref _statPodHandling.PodHandlingTimeAvg,
                        ref _statPodHandling.PodHandlingTimeVariance,
                        ref _statPodHandling.PodHandlingTimeMin,
                        ref _statPodHandling.PodHandlingTimeMax,
                        ref _statPodHandling.PodHandlingTimeSum,
                        _statLastTimeTransactionFinished - _statLastTimeNewPod);
            }
            // Log the arrival time of the pod
            _statLastTimeNewPod = Instance.Controller.CurrentTime;
        }

        /// <summary>
        /// Diagnostic: record the inter-pod handoff gap [s] at this station — the time from the previous
        /// pod's last transfer finishing (_statLastTimeTransactionFinished, still the prior value when
        /// this is called, before the new pick overwrites it) until the new pod's first pick begins
        /// (currentTime). Called once per pod transition, at the first pick of a new pod. Skips the
        /// station's very first pod (no prior transaction). Negative values are clamped to 0.
        /// </summary>
        private void RecordPodHandoffGap(double currentTime)
        {
            if (double.IsNaN(_statLastTimeTransactionFinished))
                return;
            double gap = currentTime - _statLastTimeTransactionFinished;
            if (gap < 0.0) gap = 0.0;
            Instance.StatPodHandoffGapSamples.Add(gap);
        }

        #endregion

        #region Statistics

        /// <summary>
        /// The number of items handled by this station.
        /// </summary>
        public int StatNumItemsPicked;
        /// <summary>
        /// The number of items picked by this station that were injected to the task of the robot.
        /// </summary>
        public int StatNumInjectedItemsPicked;
        /// <summary>
        /// The number of orders completed at this station.
        /// </summary>
        public int StatNumOrdersFinished;
        /// <summary>
        /// The number of requests currently open (not assigned to a bot) for this station.
        /// </summary>
        internal int StatCurrentlyOpenRequests { get; set; }
        /// <summary>
        /// The number of requests currently open (not assigned to a bot) for this station.
        /// </summary>
        internal int StatCurrentlyOpenQueuedRequests { get; set; }
        /// <summary>
        /// The number of items currently open (not picked yet) for this station.
        /// </summary>
        internal int StatCurrentlyOpenItems { get; private set; }
        /// <summary>
        /// The number of items currently open (not picked yet) and queued for this station.
        /// </summary>
        internal int StatCurrentlyOpenQueuedItems { get; private set; }
        /// <summary>
        /// Contains statistics about the pods handled at this station.
        /// </summary>
        private StationStatisticsDataPoint _statPodHandling;
        /// <summary>
        /// The (sequential) number of pods handled at this station.
        /// </summary>
        public int StatPodsHandled { get { return _statPodHandling == null ? 0 : _statPodHandling.PodsHandled; } }
        /// <summary>
        /// The time it took to handle one pod in average.
        /// </summary>
        public double StatPodHandlingTimeAvg { get { return _statPodHandling == null ? 0 : _statPodHandling.PodHandlingTimeAvg; } }
        /// <summary>
        /// The variance in the handling times of the pods.
        /// </summary>
        public double StatPodHandlingTimeVar { get { return _statPodHandling == null ? 0 : _statPodHandling.PodHandlingTimeVariance; } }
        /// <summary>
        /// The minimal handling time of a pod.
        /// </summary>
        public double StatPodHandlingTimeMin { get { return _statPodHandling == null ? 0 : _statPodHandling.PodHandlingTimeMin; } }
        /// <summary>
        /// The maximal handling time of a pod.
        /// </summary>
        public double StatPodHandlingTimeMax { get { return _statPodHandling == null ? 0 : _statPodHandling.PodHandlingTimeMax; } }
        /// <summary>
        /// Indicates that the next item picked belongs to a new transaction serving one pod.
        /// </summary>
        private bool _newPodTransaction;
        /// <summary>
        /// The item pile-on of this station, i.e. the relative number of items picked from the same pod in one 'transaction'.
        /// </summary>
        public double StatItemPileOn { get { return _statPodHandling == null ? 0 : StatNumItemsPicked / (double)_statPodHandling.PodsHandled; } }
        /// <summary>
        /// The injected item pile-on of this station, i.e. the relative number of injected items picked from the same pod in one 'transaction'.
        /// </summary>
        public double StatInjectedItemPileOn { get { return _statPodHandling == null ? 0 : StatNumInjectedItemsPicked / (double)_statPodHandling.PodsHandled; } }
        /// <summary>
        /// The order pile-on of this station, i.e. the relative number of orders finished from the same pod in one 'transaction'.
        /// </summary>
        public double StatOrderPileOn { get { return _statPodHandling == null ? 0 : StatNumOrdersFinished / (double)_statPodHandling.PodsHandled; } }
        /// <summary>
        /// The time this station was idling.
        /// </summary>
        public double StatIdleTime;
        /// <summary>
        /// The time this station was active.
        /// </summary>
        public double StatActiveTime { get; private set; }
        /// <summary>
        /// The last time the activity of the station was logged.
        /// </summary>
        private double _lastActiveMeasurement = 0;
        /// <summary>
        /// The time this station was shutdown.
        /// </summary>
        public double StatDownTime;
        /// <summary>Type A starvation — "supply lag" (optimizable). Cumulative time [s] the station was
        /// idle (no pod currently picking, i.e. stationFreeAt &lt;= now) while the system still had work that
        /// could feed it: a pod was inbound, an order was already committed to this station, or the order
        /// backlog still held pending orders. The pod simply has not arrived yet — the timing/method flaw
        /// the starve-aware feature targets.</summary>
        public double StatStarvationTimeSec;
        /// <summary>Type B starvation — "no-order waste". Cumulative time [s] the station was idle (no pod
        /// picking) while there was genuinely nothing to do: no inbound pod, no committed order, and an
        /// empty backlog. Pure station-resource idleness that no allocation decision could have avoided,
        /// distinct from supply lag.</summary>
        public double StatStationNoOrderIdleTimeSec;
        /// <summary>
        /// The timepoint at which the station completed its last order and may have moved to a rest state.
        /// </summary>
        private double _statDepletionTime = double.PositiveInfinity;
        /// <summary>
        /// Stores the last time when the handling of a pod started.
        /// </summary>
        private double _statLastTimeNewPod = double.NaN;
        /// <summary>
        /// Stores the last time when a transaction was finished.
        /// </summary>
        private double _statLastTimeTransactionFinished = double.NaN;

        /// <summary>
        /// Inits the first datapoint for pod handling.
        /// </summary>
        /// <param name="handlingTime">The time it took to serve the pod.</param>
        /// <returns>The new datapoint.</returns>
        private StationStatisticsDataPoint StatInitPodHandlingDataPoint(double handlingTime)
        {
            return new StationStatisticsDataPoint()
            {
                PodsHandled = 1,
                PodHandlingTimeAvg = handlingTime,
                PodHandlingTimeMax = handlingTime,
                PodHandlingTimeMin = handlingTime,
                PodHandlingTimeSum = handlingTime,
                PodHandlingTimeVariance = 0
            };
        }

        /// <summary>
        /// Resets the statistics.
        /// </summary>
        public void ResetStatistics()
        {
            StatNumOrdersFinished = 0;
            StatNumItemsPicked = 0;
            StatNumInjectedItemsPicked = 0;
            _statPodHandling = null;
            _newPodTransaction = true; // Immediately begin counting of served pods (do not forget the one currently being served)
            _statLastTimeNewPod = double.NaN;
            _statLastTimeTransactionFinished = double.NaN;
            StatIdleTime = 0.0;
            StatActiveTime = 0.0;
            _lastActiveMeasurement = Instance.Controller.CurrentTime;
            StatDownTime = 0.0;
            StatStarvationTimeSec = 0.0;
            StatStationNoOrderIdleTimeSec = 0.0;
        }

        #endregion

        #region Inherited methods

        /// <summary>
        /// Returns a simple string identifying this object in its instance.
        /// </summary>
        /// <returns>A simple name identifying the instance element.</returns>
        public override string GetIdentfierString() { return "OutputStation" + this.ID; }
        /// <summary>
        /// Returns a simple string giving information about the object.
        /// </summary>
        /// <returns>A simple string.</returns>
        public override string ToString() { return "OutputStation" + this.ID; }

        #endregion

        #region IUpdateable Members

        private double BlockedUntil = -1.0;

        /// <summary>
        /// The next event when this element has to be updated.
        /// </summary>
        /// <param name="currentTime">The current time of the simulation.</param>
        /// <returns>The next time this element has to be updated.</returns>
        public double GetNextEventTime(double currentTime)
        {
            if (currentTime >= BlockedUntil) return Double.PositiveInfinity;
            else return BlockedUntil;
        }
        /// <summary>
        /// Updates the element to the specified time.
        /// </summary>
        /// <param name="lastTime">The time before the update.</param>
        /// <param name="currentTime">The time to update to.</param>
        public void Update(double lastTime, double currentTime)
        {
            // Track time the station is available for assignments (active)
            if (_lastActiveMeasurement < currentTime)
            {
                // If active, measure time the station was active
                if (Active)
                    StatActiveTime += currentTime - _lastActiveMeasurement;
                // Update the poll
                _lastActiveMeasurement = currentTime;
            }

            // See whether we have to do anything
            if (currentTime < BlockedUntil)
                return;

            // Indicate change at instance
            Instance.Changed = true;

            double idleDuration = GetIdleDuration(lastTime, currentTime);

            // Log idle time
            StatIdleTime += idleDuration;
            // Split the station's non-picking idle time (idleDuration > 0 == no pod currently picking,
            // equivalently the projection's stationFreeAt <= now) into two mutually-exclusive starvation
            // types, both counted only after the first completed output (warm-up excluded):
            //   Type A "supply lag" (optimizable): something could still feed the station — a pod is
            //     inbound, an order is committed here, or the backlog still holds pending orders — but no
            //     pod has arrived yet. Mirrors the forward-looking "Starve gap" the panel shows.
            //   Type B "no-order waste": genuinely nothing to do (no inbound pod, no committed order, empty
            //     backlog). No allocation decision could have avoided this idleness.
            if (idleDuration > 0.0 && StatNumOrdersFinished > 0)
            {
                if (GetInfoInboundPods() > 0 || CapacityInUse > 0 || HasServeablePendingDemand())
                    StatStarvationTimeSec += idleDuration;            // Type A: supply lag (pod not here yet)
                else
                    StatStationNoOrderIdleTimeSec += idleDuration;    // Type B: nothing to do at all
            }

            // Log down time
            if (currentTime - _statDepletionTime > Instance.SettingConfig.StationShutdownThresholdTime)
                StatDownTime += Math.Min(currentTime - _statDepletionTime, currentTime - lastTime);

            if (RemoveAnyCompletedOrder(currentTime) != null)
                return;

            if (TakeItemFromPod(currentTime))
                return;
        }

        #endregion

        #region IOutputStationInfo Members

        /// <summary>
        /// Returns the active instance belonging to this element.
        /// </summary>
        /// <returns>The active instance.</returns>
        public IInstanceInfo GetInfoInstance() { return Instance; }
        /// <summary>
        /// Gets the current tier this object is placed on. Can't change in case of an immovable object.
        /// </summary>
        /// <returns>The current tier.</returns>
        public ITierInfo GetInfoCurrentTier() { return Tier; }
        /// <summary>
        /// Gets the number of assigned orders.
        /// </summary>
        /// <returns>The number of assigned orders.</returns>
        public int GetInfoAssignedOrders() { return _assignedOrders.Count; }

        private object _syncRoot = new object();
        private List<IOrderInfo> _completedOrders = new List<IOrderInfo>();
        private List<IOrderInfo> _openOrders = new List<IOrderInfo>();
        /// <summary>
        /// Gets all order currently open.
        /// </summary>
        /// <returns>The enumeration of open orders.</returns>
        public IEnumerable<IOrderInfo> GetInfoOpenOrders() { lock (_syncRoot) { return _openOrders.ToList(); } }
        /// <summary>
        /// Gets all orders already completed.
        /// </summary>
        /// <returns>The enumeration of completed orders.</returns>
        public IEnumerable<IOrderInfo> GetInfoCompletedOrders() { lock (_syncRoot) { return _completedOrders.ToList(); } }
        /// <summary>
        /// Gets the capacity this station offers.
        /// </summary>
        /// <returns>The capacity of the station.</returns>
        public double GetInfoCapacity() { return Capacity; }
        /// <summary>
        /// Gets the absolute capacity currently in use.
        /// </summary>
        /// <returns>The capacity in use.</returns>
        public double GetInfoCapacityUsed() { return CapacityInUse; }
        /// <summary>
        /// Indicates the number that determines the overall sequence in which stations get activated.
        /// </summary>
        /// <returns>The order ID of the station.</returns>
        public int GetInfoActivationOrderID() { return ActivationOrderID; }
        /// <summary>
        /// Gets the information queue.
        /// </summary>
        /// <returns>Queue</returns>
        public string GetInfoQueue() { return (Queues == null || Queues.Count == 0) ? "" : Queues.First().Value.Select(w => w.ID.ToString()).Aggregate((current, next) => current + ", " + next); }
        /// <summary>
        /// Indicates whether the station is currently activated (available for new assignments).
        /// </summary>
        /// <returns><code>true</code> if the station is active, <code>false</code> otherwise.</returns>
        public bool GetInfoActive() { return Active; }
        /// <summary>
        /// Indicates whether the station is currently blocked due to activity.
        /// </summary>
        /// <returns><code>true</code> if it is blocked, <code>false</code> otherwise.</returns>
        public bool GetInfoBlocked() { return Instance.Controller.CurrentTime < BlockedUntil; }
        /// <summary>
        /// Gets the remaining time this station is blocked.
        /// </summary>
        /// <returns>The remaining time this station is blocked.</returns>
        public double GetInfoBlockedLeft() { double currentTime = Instance.Controller.CurrentTime; double blockedUntil = BlockedUntil; return currentTime < blockedUntil ? blockedUntil - currentTime : double.NaN; }
        /// <summary>
        /// Gets the of requests currently open (not assigned to a bot) for this station.
        /// </summary>
        /// <returns>The number of active requests.</returns>
        public int GetInfoOpenRequests() { return StatCurrentlyOpenRequests; }
        /// <summary>
        /// Gets the number of queued requests currently open (not assigned to a bot) for this station.
        /// </summary>
        /// <returns>The number of active queued requests.</returns>
        public int GetInfoOpenQueuedRequests() { return StatCurrentlyOpenQueuedRequests; }
        /// <summary>
        /// Gets the number of currently open items (not yet picked) for this station.
        /// </summary>
        /// <returns>The number of open items.</returns>
        public int GetInfoOpenItems() { return StatCurrentlyOpenItems; }
        /// <summary>
        /// Gets the number of currently queued and open items (not yet picked) for this station.
        /// </summary>
        /// <returns>The number of queued open items.</returns>
        public int GetInfoOpenQueuedItems() { return StatCurrentlyOpenQueuedItems; }
        /// <summary>
        /// Gets the number of pods currently incoming to this station.
        /// </summary>
        /// <returns>The number of pods currently incoming to this station.</returns>
        public int GetInfoInboundPods() { return _inboundPods.Count; }
        /// <summary>
        /// Gets the projected time until this station first becomes idle/starved.
        /// </summary>
        /// <returns>The projected time-to-starvation in seconds.</returns>
        public double GetInfoStationEST()
        {
            return SlowStartController.ComputeStationWorkProjection(this, GetInfoCurrentTime()).FirstStarveSec;
        }
        /// <summary>
        /// Gets the projected time until all currently committed station work clears.
        /// </summary>
        /// <returns>The projected work horizon in seconds.</returns>
        public double GetInfoStationWorkHorizon()
        {
            return SlowStartController.ComputeStationWorkProjection(this, GetInfoCurrentTime()).WorkHorizonSec;
        }
        /// <summary>
        /// Gets the total projected idle gap inside the current station work pipeline.
        /// </summary>
        /// <returns>The projected starvation gap in seconds.</returns>
        public double GetInfoStationStarvationGap()
        {
            return SlowStartController.ComputeStationWorkProjection(this, GetInfoCurrentTime()).StarvationGapSec;
        }
        /// <summary>
        /// Gets the cumulative measured starvation time at this station so far.
        /// </summary>
        /// <returns>The accumulated starvation time in seconds.</returns>
        public double GetInfoStationStarvationAccumulated()
        {
            return StatStarvationTimeSec;
        }
        /// <summary>
        /// Gets the projected remaining time until the currently processed pod can leave the station.
        /// </summary>
        /// <returns>The remaining pod-release time in seconds, or NaN if no pod is currently being processed.</returns>
        public double GetInfoCurrentPodReleaseLeft()
        {
            double currentTime = GetInfoCurrentTime();
            if (double.IsNaN(_currentProcessingPodReleaseUntil) || currentTime >= _currentProcessingPodReleaseUntil)
                return double.NaN;
            return _currentProcessingPodReleaseUntil - currentTime;
        }

        private double GetInfoCurrentTime()
        {
            return Instance != null && Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;
        }

        #endregion

        #region IQueueOwner Members

        /// <summary>
        /// The Queue starting with the nearest way point ending with the most far away one.
        /// </summary>
        /// <value>
        /// The queue.
        /// </value>
        public Dictionary<Waypoint, List<Waypoint>> Queues { get; set; }

        #endregion

        #region IExposeVolatileID

        /// <summary>
        /// An ID that is useful as an index for listing this item.
        /// This ID is unique among all <code>ItemDescription</code>s while being as low as possible.
        /// Note: For now the volatile ID matches the actual ID.
        /// </summary>
        int IExposeVolatileID.VolatileID { get { return VolatileID; } }

        #endregion
    }
}
