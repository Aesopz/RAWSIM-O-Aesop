using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Waypoints;
using RAWSimO.MultiAgentPathFinding;
using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Interfaces;
using RAWSimO.Core.Info;
using RAWSimO.Core.Items;
using RAWSimO.Core.Geometrics;
using RAWSimO.MultiAgentPathFinding.Physic;
using System.Diagnostics;
using System.Text;
using RAWSimO.Core.IO;
using RAWSimO.Core.Metrics;
using RAWSimO.Core.Statistics;
using RAWSimO.Toolbox;
using RAWSimO.Core.Bots;

namespace RAWSimO.Core.Bots
{
    /// <summary>
    /// Bot Driver
    /// Optimization is complete delegated to the controller, because he knows all the bots
    /// </summary>
    public class BotNormal : Bot
    {

        #region Attributes

        /// <summary>
        /// The bots request a re-optimization after failing of next way point reservation
        /// </summary>
        public static bool RequestReoptimizationAfterFailingOfNextWaypointReservation = false;

        /// <summary>
        /// The current destination of the bot as useful information for other mechanisms. If not available, the current waypoint will be provided.
        /// </summary>
        internal override Waypoint TargetWaypoint { get { return _destinationWaypoint != null ? _destinationWaypoint : _currentWaypoint; } }

        /// <summary>
        /// destination way point
        /// </summary>
        public Waypoint DestinationWaypoint
        {
            get { return _destinationWaypoint; }
            set
            {
                if (_destinationWaypoint != value)
                {
                    _destinationWaypoint = value;
                    Instance.Controller.PathManager.notifyBotNewDestination(this);
                }
            }
        }
        private Waypoint _destinationWaypoint;

        /// <summary>
        /// next way point
        /// </summary>
        public Waypoint NextWaypoint
        {
            get
            {
                return _nextWaypoint;
            }
            private set
            {
                if (value != null) { _nextWaypointID = value.ID; }
                _nextWaypoint = value;
            }
        }
        private Waypoint _nextWaypoint;
        private int _nextWaypointID;

        /// <summary>
        /// next way point
        /// </summary>
        public bool RequestReoptimization;

        #region State handling

        /// <summary>
        /// The state queue
        /// </summary>
        private Queue<IBotState> _stateQueue = new Queue<IBotState>();
        /// <summary>
        /// Returns the next state in the state queue without removing it.
        /// </summary>
        /// <returns>The next state in the state queue.</returns>
        private IBotState StateQueuePeek() { return _stateQueue.Peek(); }
        /// <summary>
        /// Enqueues a state.
        /// </summary>
        /// <param name="state">The state to enqueue.</param>
        private void StateQueueEnqueue(IBotState state) { _stateQueue.Enqueue(state); _currentInfoStateName = _stateQueue.Peek().ToString(); }
        /// <summary>
        /// Dequeues the next state from the state queue.
        /// </summary>
        /// <returns>The state that was just dequeued.</returns>
        private IBotState StateQueueDequeue() { IBotState state = _stateQueue.Dequeue(); _currentInfoStateName = _stateQueue.Any() ? _stateQueue.Peek().ToString() : ""; return state; }
        /// <summary>
        /// Clears the complete state queue.
        /// </summary>
        private void StateQueueClear() { _stateQueue.Clear(); _currentInfoStateName = ""; }
        /// <summary>
        /// The number of states currently in the queue.
        /// </summary>
        private int StateQueueCount { get { return _stateQueue.Count; } }

        #endregion

        /// <summary>
        /// drive until
        /// </summary>
        private double _driveDuration = -1.0;

        /// <summary>
        /// rotate until
        /// </summary>
        private double _rotateDuration = -1.0;

        /// <summary>
        /// True only during the tick where _updateDrive() is executing the rotation phase.
        /// Used to distinguish actual turns from CBS waits, pickup/setdown waits, and rest waits.
        /// </summary>
        private bool _isRotatingThisTick = false;

        /// <summary>
        /// Per-trip tracking: timestamp when current trip started (at _appendMoveStates dispatch).
        /// </summary>
        private double _tripStartTime = 0.0;
        /// <summary>
        /// Per-trip tracking: cumulative wait time in current trip [s].
        /// </summary>
        private double _currentTripWaitSec = 0.0;
        /// <summary>Per-trip snapshot: StatEnergyTotalJ at trip start.</summary>
        private double _tripStartEnergyJ = 0.0;
        /// <summary>Per-trip snapshot: StatDistanceTraveledM at trip start.</summary>
        private double _tripStartDistanceM = 0.0;
        /// <summary>Per-trip cumulative turn count.</summary>
        private int _currentTripTurnCount = 0;
        /// <summary>
        /// Per-trip tracking: whether the currently-open trip is loaded (Pod!=null at its start).
        /// </summary>
        private bool _activeTripLoaded = false;
        /// <summary>
        /// Per-trip tracking: whether a trip is currently open (awaiting close-out at arrival).
        /// </summary>
        private bool _tripOpen = false;
        /// <summary>
        /// Per-trip tracking: queue of loaded/empty flags for trips opened at dispatch but not yet
        /// activated (multiple _appendMoveStates may run in same tick before any movement).
        /// </summary>
        private Queue<bool> _pendingTripsLoaded = new Queue<bool>();

        /// <summary>Per-trip recorded metrics (flushed at CloseCurrentTrip).</summary>
        public struct TripRecord
        {
            public double DurationSec;
            public double DistanceM;
            public double EnergyJ;       // mechanical (E1..E5) consumed during trip
            public double WaitTimeSec;
            public int TurnCount;
        }
        /// <summary>Per-trip records for loaded trips.</summary>
        public List<TripRecord> PerTripRecordsLoaded = new List<TripRecord>();
        /// <summary>Per-trip records for empty trips.</summary>
        public List<TripRecord> PerTripRecordsEmpty  = new List<TripRecord>();

        /// <summary>
        /// Close the currently-open trip (bot arrived at trip destination). Called at
        /// PickupPod/SetdownPod/PutItems/GetItems init.
        /// </summary>
        internal void CloseCurrentTrip(double currentTime)
        {
            if (_tripOpen)
            {
                double dur = currentTime - _tripStartTime;
                if (dur > 0.0)
                {
                    double waitRatio = Math.Min(1.0, _currentTripWaitSec / dur);
                    var rec = new TripRecord
                    {
                        DurationSec = dur,
                        DistanceM   = StatDistanceTraveledM - _tripStartDistanceM,
                        EnergyJ     = StatEnergyTotalJ - _tripStartEnergyJ,
                        WaitTimeSec = _currentTripWaitSec,
                        TurnCount   = _currentTripTurnCount,
                    };
                    if (_activeTripLoaded) { PerTripWaitRatioLoaded.Add(waitRatio); PerTripRecordsLoaded.Add(rec); }
                    else                   { PerTripWaitRatioEmpty.Add(waitRatio);  PerTripRecordsEmpty.Add(rec);  }
                }
                _tripOpen = false;
            }
        }

        /// <summary>
        /// Activate next pending trip when bot starts moving. Called at BotMove init.
        /// </summary>
        internal void ActivateNextTripIfPending(double currentTime)
        {
            if (!_tripOpen && _pendingTripsLoaded.Count > 0)
            {
                _activeTripLoaded = _pendingTripsLoaded.Dequeue();
                _tripStartTime = currentTime;
                _currentTripWaitSec = 0.0;
                _tripStartEnergyJ = StatEnergyTotalJ;
                _tripStartDistanceM = StatDistanceTraveledM;
                _currentTripTurnCount = 0;
                _tripOpen = true;
            }
        }

        /// <summary>
        /// rotate until
        /// </summary>
        private double _waitUntil = -1.0;

        /// <summary>
        /// rotate until
        /// </summary>
        private double _startOrientation = 0;

        /// <summary>
        /// rotate until
        /// </summary>
        private double _endOrientation = 0;

        /// <summary>
        /// The agent reached the next way point
        /// </summary>
        private bool _eventReachedNextWaypoint = false;

        /// <summary>
        /// Indicates whether the first position info was received from the remote server.
        /// </summary>
        private bool _initialEventReceived = false;

        /// <summary>
        /// Indicates the last state of the robot. This is used to lower the communication with the robot.
        /// </summary>
        private BotStateType _lastExteriorState = BotStateType.Rest;

        /// <summary>
        /// The physics calculation object.
        /// </summary>
        public Physics Physics;

        /// <summary>
        /// Optional acceleration multiplier set by GymServer (Plan 2). Null = no override.
        /// </summary>
        public double? AccelerationMultiplier { get; set; } = null;

        /// <summary>
        /// Optional deceleration multiplier set by GymServer (Plan 2). Null = no override.
        /// </summary>
        public double? DecelerationMultiplier { get; set; } = null;

        /// <summary>
        /// The current path.
        /// </summary>
        private Path _path;
        /// <summary>
        /// The current path.
        /// </summary>
        public Path Path
        {
            get
            {
                // Just return it
                return _path;
            }
            internal set
            {
                // Set it
                _path = value;
                // Make path public if visualization is present
                if (Instance.SettingConfig.VisualizationAttached && _path != null)
                    _currentPath = _path.Actions.Select(a => Instance.Controller.PathManager.GetWaypointByNodeId(a.Node)).Cast<IWaypointInfo>().ToList();
            }
        }

        /// <summary>
        /// [Stage A] Pending path to be applied when safe (not mid-segment).
        /// Used by deferred path application to avoid overwriting active movement.
        /// </summary>
        internal Path PendingPlannedPath { get; set; } = null;

        /// <summary>
        /// [Stage A] Flag indicating a pending path is waiting to be applied.
        /// </summary>
        public bool HasPendingPlannedPath => PendingPlannedPath != null;

        /// <summary>
        /// [Stage A] Time when the current path was last assigned.
        /// Useful for tracking how long a path has been active.
        /// </summary>
        public double LastPathAssignmentTime { get; set; } = -1.0;

        #region Tick-Coherence Guard

        /// <summary>
        /// Set to true inside <see cref="_updateMove"/> when the bot arrives at NextWaypoint
        /// during the current tick.  Checked before the second <c>Act()</c> call in
        /// <see cref="Update"/> to prevent a bot from immediately committing to a new
        /// segment in the same tick — which would bypass the conflict-detection pass that
        /// <see cref="Control.PathManager"/> performs at the start of each tick.
        /// Only effective when the path planner is AgentAStar (decentralized).
        /// </summary>
        private bool _arrivedAtWaypointThisTick;

        /// <summary>
        /// Lazily cached flag: true when the active path planner is AgentAStar (decentralized).
        /// Cached on first use because <c>ControllerConfig</c> is not available at construction time.
        /// </summary>
        private bool? _isDecentralizedMode;

        #endregion

        #region Energy Statistics (Rizqi Model)

        /// <summary>Total energy consumed over lifetime [J].</summary>
        public double StatEnergyTotalJ;
        /// <summary>Acceleration phase energy (E1) [J].</summary>
        public double StatEnergyE1AccelJ;
        /// <summary>Deceleration phase energy (E2) [J].</summary>
        public double StatEnergyE2DecelJ;
        /// <summary>Cruise phase energy (E3) [J].</summary>
        public double StatEnergyE3CruiseJ;
        /// <summary>Rotation energy (E4) [J].</summary>
        public double StatEnergyE4RotationJ;
        /// <summary>Pod lift/lower energy (E5a + E5b) [J].</summary>
        public double StatEnergyE5LiftLowerJ;
        /// <summary>Number of pod pickup (lift-up) events.</summary>
        public int StatPickupCount;
        /// <summary>Number of pod setdown (lift-down) events.</summary>
        public int StatSetdownCount;
        /// <summary>Number of turning events (segments with rotation).</summary>
        public int StatTurningCount;
        /// <summary>Number of extract orders completed by this bot.</summary>
        public int StatOrdersCompleted;
        /// <summary>Total distance traveled [m].</summary>
        public double StatDistanceTraveledM;
        /// <summary>Distance traveled while carrying a pod [m].</summary>
        public double StatLoadedDistanceM;
        /// <summary>Distance traveled while empty (no pod) [m].</summary>
        public double StatEmptyDistanceM;
        /// <summary>Total wait time while loaded [s] (path-wait only: not moving, not rotating, not in pickup/setdown).</summary>
        public double StatWaitTimeLoadedSec;
        /// <summary>Total wait time while empty [s].</summary>
        public double StatWaitTimeEmptySec;
        /// <summary>Wait energy while loaded [J] = SUPPORT_POWER_LOADED × StatWaitTimeLoadedSec (congestion cost only).</summary>
        public double StatWaitEnergyLoadedJ => Metrics.EnergyConsumption.SUPPORT_POWER_LOADED * StatWaitTimeLoadedSec;
        /// <summary>Wait energy while empty [J] = SUPPORT_POWER_EMPTY × StatWaitTimeEmptySec (congestion cost only).</summary>
        public double StatWaitEnergyEmptyJ  => Metrics.EnergyConsumption.SUPPORT_POWER_EMPTY * StatWaitTimeEmptySec;
        /// <summary>Input-side station processing arrivals.</summary>
        public int StatInputStationArrivals;
        /// <summary>Output-side station processing arrivals.</summary>
        public int StatOutputStationArrivals;
        /// <summary>Total input-side plus output-side station processing arrivals.</summary>
        public int StatStationArrivals => StatInputStationArrivals + StatOutputStationArrivals;
        /// <summary>Number of turning events while carrying a pod.</summary>
        public int StatLoadedTurningCount;
        /// <summary>Number of turning events while empty (no pod).</summary>
        public int StatEmptyTurningCount;
        /// <summary>Total number of completed loaded trips (pickup → setdown).</summary>
        public int StatTripCountLoaded;
        /// <summary>Total number of completed empty trips (setdown → next pickup).</summary>
        public int StatTripCountEmpty;
        /// <summary>Per-trip wait ratio for loaded trips: (wait time in trip) / (total trip time). Values 0-1.</summary>
        public List<double> PerTripWaitRatioLoaded = new List<double>();
        /// <summary>Per-trip wait ratio for empty trips: (wait time in trip) / (total trip time). Values 0-1.</summary>
        public List<double> PerTripWaitRatioEmpty = new List<double>();
        /// <summary>Total time spent waiting (not moving, not rotating) [s].</summary>
        public double StatWaitTimeSec;
        /// <summary>
        /// Background "support" energy [J] — the per-second overhead the robot draws
        /// whenever a task is active, regardless of whether it is moving or stationary.
        /// Formula: SupportPower(Pod) × wall-clock time, accrued every tick for every bot
        /// (no task gate; idle/None/Rest included). Uses configured support power.
        /// This is the intuitive "E_support" in the energy literature: the cost of
        /// keeping the robot operational during mission time, electronics/sensors/etc.
        /// Planner-agnostic — identical accumulation rules for CBS / ECBS / WHCA*.
        /// </summary>
        public double StatESupportJ;
        /// <summary>Total energy including support = E_mech + E_support [J].</summary>
        public double StatEnergyTotalWithSupportJ => StatEnergyTotalJ + StatESupportJ;

        /// <summary>
        /// Congestion-wait energy [J] — strict SUBSET of E_support accumulated only
        /// during blocked stationary periods (task-assigned AND !Moving AND !rotating
        /// AND !inPickupOrSetdown). Captures wait caused by planner-level conflicts
        /// or queueing, NOT mission-time overhead. Not equal to E_support.
        /// </summary>
        public double StatEWaitJ;
        /// <summary>Congestion-wait energy while carrying a pod (loaded) [J].</summary>
        public double StatEWaitLoadedJ;
        /// <summary>Congestion-wait energy while not carrying a pod (empty) [J].</summary>
        public double StatEWaitEmptyJ;
        /// <summary>
        /// Premature-arrival queueing time [s] — accumulated while the bot is inside a
        /// station queue zone but NOT yet in active GetItems/PutItems service. Captures
        /// the gap "bot arrived at queue → bot starts processing", i.e. wasted wait
        /// caused by mismatched dispatch timing vs station processing rhythm.
        /// Used to evaluate starvation-aware OB+PS optimizations.
        /// </summary>
        public double StatQueueingAtStationTimeSec;
        /// <summary>
        /// Premature-arrival queueing energy [J] = SupportPower(Pod) × StatQueueingAtStationTimeSec.
        /// Quantifies support-power waste from arriving at the station before it can serve.
        /// </summary>
        public double StatEQueueingAtStationJ;

        // Planned-wait stop-and-go counters
        /// <summary>
        /// Number of WHCA*/reservation-table planned waits on an otherwise straight pass-through
        /// waypoint. Excludes turn waypoints, pickup/setdown stops, slow-start, and failed
        /// RegisterNextWaypoint retries.
        /// </summary>
        public int StatStopAndGoCount;
        /// <summary>
        /// Extra stop-and-go energy [J] for open-road planned waits: E2 from the segment into the
        /// wait waypoint plus E1 from the segment leaving it. Strict subset of E1+E2 totals; adds
        /// nothing to total energy because it is a re-classification.
        /// </summary>
        public double StatStopAndGoEnergyJ;
        /// <summary>
        /// Number of station-queue creep stop-and-go events caused by QueueManager advancing
        /// a stopped bot from one queue waypoint to another.
        /// </summary>
        public int StatQueueStopAndGoCount;
        /// <summary>
        /// Extra stop-and-go energy [J] for queue-manager creep: E2 into the stopped queue
        /// waypoint plus E1 from the segment leaving it.
        /// </summary>
        public double StatQueueStopAndGoEnergyJ;
        private Waypoint _lastCommittedSegmentStartWaypoint;
        private Waypoint _lastCommittedSegmentEndWaypoint;
        private double _lastCommittedSegmentE2DecelJ;
        private bool _pendingPlannedWaitStopGo;
        private Waypoint _pendingPlannedWaitWaypoint;
        private double _pendingPlannedWaitDecelEnergyJ;
        // ───────────────────────────────────────────────────────────────────

        // ── PP-aware Slow-Start counters (Phase 1) ─────────────────────────
        /// <summary>Accumulated time bot was holding at pod cell under slow-start policy [s].</summary>
        public double StatSlowStartHoldTimeSec;
        /// <summary>Support-energy spent during slow-start holds [J] = SupportPower(Pod) × StatSlowStartHoldTimeSec.</summary>
        public double StatSlowStartHoldEnergyJ;
        /// <summary>Number of slow-start decisions taken (one per pod pickup when feature enabled).</summary>
        public int StatSlowStartDecisionCount;
        /// <summary>Number of slow-start decisions that resulted in immediate release (delay = 0).</summary>
        public int StatSlowStartImmediateReleaseCount;
        /// <summary>Number of times the reservation-aware ETA probe failed (returned NaN).</summary>
        public int StatSlowStartSearchFailures;
        /// <summary>Number of in-flight tasks lacking ExpectedArrivalAtStation cache during T_starve computation.</summary>
        public int StatSlowStartExpectedArrivalMissingCount;
        /// <summary>True iff the bot is currently in a BotSlowStartHold state; gates wait-time accumulation.</summary>
        internal bool _isSlowStartHolding = false;
        /// <summary>Sim-time the current hold started (for FIFO ordering across same-station holders).
        /// NaN when not holding. Earlier value = higher priority.</summary>
        internal double _slowStartHoldStartTime = double.NaN;
        /// <summary>Absolute time at which the central scheduler permits this holder to release.
        /// NaN until the scheduler has run at least once for this bot's station this hold.</summary>
        internal double _slowStartReleaseDeadline = double.NaN;
        /// <summary>True iff this bot is the scheduler's currently-chosen pod (diagnostic).</summary>
        internal bool _slowStartIsChosen = false;
        /// <summary>Scheduler-provided ETA (pod→station ideal) cached for the release-time arrival estimate.</summary>
        internal double _slowStartEta = double.NaN;
        /// <summary>Scheduler-provided T_starve snapshot for telemetry.</summary>
        internal double _slowStartTStarve = double.NaN;
        // ───────────────────────────────────────────────────────────────────

        /// <summary>Idle time: seconds with no task assigned (BotTaskType.None).</summary>
        public double StatTimeIdleSec => StatTotalTaskTimes.TryGetValue(BotTaskType.None, out var t) ? t : 0.0;
        // ── Pref calibration: event-level 8-accumulator model ─────────────────────
        // All values accumulated at setNextWaypoint() — event-driven, not tick-delta.
        // isLoaded = (Pod != null) at the exact moment the move/turn is committed.
        // Empty = Pod == null (any task; no phase filter).
        // Loaded = Pod != null (any task).
        //
        // move* = E1+E2+E3 drive energy / _driveDuration
        // turn* = E4 rotation energy / _rotateDuration  (0 when no turn this segment)

        /// <summary>Drive energy while empty [J].</summary>
        public double StatMoveEnergyEmptyJ;
        /// <summary>Drive time while empty [s].</summary>
        public double StatMoveTimeEmptySec;
        /// <summary>Turn energy while empty [J].</summary>
        public double StatTurnEnergyEmptyJ;
        /// <summary>Turn time while empty [s].</summary>
        public double StatTurnTimeEmptySec;

        /// <summary>Drive energy while loaded [J].</summary>
        public double StatMoveEnergyLoadedJ;
        /// <summary>Drive time while loaded [s].</summary>
        public double StatMoveTimeLoadedSec;
        /// <summary>Turn energy while loaded [J].</summary>
        public double StatTurnEnergyLoadedJ;
        /// <summary>Turn time while loaded [s].</summary>
        public double StatTurnTimeLoadedSec;
        /// <summary>Current total dynamic mass [kg] = ROBOT_MASS + pod load (0 if no pod).</summary>
        public double CurrentTotalMassKg => RAWSimO.Core.Metrics.EnergyConsumption.GetTotalMass(Pod);


        /// <summary>Resets all energy statistics to zero.</summary>
        public void ResetEnergyStatistics()
        {
            StatEnergyTotalJ = 0.0;
            StatEnergyE1AccelJ = 0.0;
            StatEnergyE2DecelJ = 0.0;
            StatEnergyE3CruiseJ = 0.0;
            StatEnergyE4RotationJ = 0.0;
            StatEnergyE5LiftLowerJ = 0.0;
            StatPickupCount = 0;
            StatSetdownCount = 0;
            StatESupportJ = 0.0;
            StatEWaitJ = 0.0;
            StatEWaitLoadedJ = 0.0;
            StatEWaitEmptyJ = 0.0;
            StatQueueingAtStationTimeSec = 0.0;
            StatEQueueingAtStationJ = 0.0;
            StatStopAndGoCount = 0;
            StatStopAndGoEnergyJ = 0.0;
            StatQueueStopAndGoCount = 0;
            StatQueueStopAndGoEnergyJ = 0.0;
            _lastCommittedSegmentStartWaypoint = null;
            _lastCommittedSegmentEndWaypoint = null;
            _lastCommittedSegmentE2DecelJ = 0.0;
            _pendingPlannedWaitStopGo = false;
            _pendingPlannedWaitWaypoint = null;
            _pendingPlannedWaitDecelEnergyJ = 0.0;
            StatTurningCount = 0;
            StatOrdersCompleted = 0;
            StatDistanceTraveledM = 0.0;
            StatLoadedDistanceM = 0.0;
            StatLoadedTurningCount = 0;
            StatEmptyTurningCount = 0;
            StatWaitTimeSec = 0.0;
            StatMoveEnergyEmptyJ   = 0.0;
            StatMoveTimeEmptySec   = 0.0;
            StatTurnEnergyEmptyJ   = 0.0;
            StatTurnTimeEmptySec   = 0.0;
            StatMoveEnergyLoadedJ  = 0.0;
            StatMoveTimeLoadedSec  = 0.0;
            StatTurnEnergyLoadedJ  = 0.0;
            StatTurnTimeLoadedSec  = 0.0;
            StatTripCountLoaded = 0;
            StatTripCountEmpty = 0;
            PerTripWaitRatioLoaded.Clear();
            PerTripWaitRatioEmpty.Clear();
            PerTripRecordsLoaded.Clear();
            PerTripRecordsEmpty.Clear();
            StatEmptyDistanceM = 0.0;
            StatWaitTimeLoadedSec = 0.0;
            StatWaitTimeEmptySec = 0.0;
            StatInputStationArrivals = 0;
            StatOutputStationArrivals = 0;
            _prevStationProcessingType = null;
            _tripStartTime = 0.0;
            _currentTripWaitSec = 0.0;
            _tripStartEnergyJ = 0.0;
            _tripStartDistanceM = 0.0;
            _currentTripTurnCount = 0;
            _activeTripLoaded = false;
            _tripOpen = false;
        }

        #endregion

        #region core

        /// <summary>
        /// Initializes a new instance of the <see cref="BotNormal"/> class.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <param name="instance">The instance.</param>
        /// <param name="radius">The radius.</param>
        /// <param name="podTransferTime">The pod transfer time.</param>
        /// <param name="acceleration">The maximum acceleration.</param>
        /// <param name="deceleration">The maximum deceleration.</param>
        /// <param name="maxVelocity">The maximum velocity.</param>
        /// <param name="turnSpeed">The turn speed.</param>
        /// <param name="collisionPenaltyTime">The collision penalty time.</param>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        public BotNormal(int id, Instance instance, double radius, double podTransferTime, double acceleration, double deceleration, double maxVelocity, double turnSpeed, double collisionPenaltyTime, double x = 0.0, double y = 0.0) : base(instance)
        {
            this.ID = id;
            this.Instance = instance;
            this.Radius = radius;
            this.X = x;
            this.Y = y;
            this.PodTransferTime = podTransferTime;
            this.MaxAcceleration = acceleration;
            this.MaxDeceleration = deceleration;
            this.MaxVelocity = maxVelocity;
            this.TurnSpeed = turnSpeed;
            this.CollisionPenaltyTime = collisionPenaltyTime;
            this.Physics = new Physics(acceleration, deceleration, maxVelocity, turnSpeed);
        }

        /// <summary>
        /// orientation the bot should look at
        /// </summary>
        /// <returns>orientation</returns>
        public double GetTargetOrientation()
        {
            return _endOrientation;
        }

        #endregion

        #region Bot Members

        /// <summary>
        /// last way point
        /// </summary>
        public override Waypoint CurrentWaypoint
        {
            get
            {
                // Get it
                return _currentWaypoint;
            }
            set
            {
                // Set it
                _currentWaypoint = value;
                // If the current waypoint belongs to a queue, notify the corresponding manager
                if (_currentWaypoint.QueueManager != null)
                    // Notify the manager about this bot joining the queue even if the bot accidentally joined it
                    _currentWaypoint.QueueManager.onBotJoinQueue(this);
            }
        }
        /// <summary>
        /// The last waypoint.
        /// </summary>
        private Waypoint _currentWaypoint;

        /// <summary>
        /// assign a task to a bot -&gt; delegate to controller
        /// </summary>
        /// <param name="t">task</param>
        /// <exception cref="System.ArgumentException">Unknown task-type:  + t.Type</exception>
        public override void AssignTask(BotTask t)
        {
            this.CurrentTask = t;
            // Warn when clearing incomplete tasks
            if (StateQueueCount > 0)
                Instance.LogDefault("WARNING! Aborting some incomplete task: " + string.Join(", ", _stateQueue.Select(s => s.Type)));
            // Forget old tasks
            StateQueueClear();

            switch (t.Type)
            {
                case BotTaskType.None:
                    return;
                case BotTaskType.ParkPod:

                    //re-optimize
                    RequestReoptimization = true;

                    ParkPodTask storePodTask = t as ParkPodTask;
                    // If we have another pod we cannot store the given one
                    if (storePodTask.Pod != Pod)
                    {
                        Instance.LogDefault("WARNING! Cannot park a pod that the bot is not carrying!");
                        Instance.Controller.BotManager.TaskAborted(this, storePodTask);
                        return;
                    }
                    // Add the move states for parking the pod (loaded: carrying pod to storage)
                    _appendMoveStates(CurrentWaypoint, storePodTask.StorageLocation, tripLoaded: true);
                    // After bringing the pod to the storage location set it down
                    StateQueueEnqueue(new BotSetdownPod(storePodTask.StorageLocation));

                    break;
                case BotTaskType.RepositionPod:

                    //re-optimize
                    RequestReoptimization = true;

                    RepositionPodTask repositionPodTask = t as RepositionPodTask;
                    // If don't have pod requested to store, then go get it 
                    if (Pod == null)
                    {
                        // Add states for getting the pod (empty: going to pod)
                        _appendMoveStates(CurrentWaypoint, repositionPodTask.Pod.Waypoint, tripLoaded: false);
                        // Add state for picking up pod
                        StateQueueEnqueue(new BotPickupPod(repositionPodTask.Pod));
                        // Add states for repositioning the pod (loaded: carrying pod to storage)
                        _appendMoveStates(repositionPodTask.Pod.Waypoint, repositionPodTask.StorageLocation, tripLoaded: true);
                        // After bringing the pod to the storage location set it down
                        StateQueueEnqueue(new BotSetdownPod(repositionPodTask.StorageLocation));
                        // Log a repositioning move
                        Instance.NotifyRepositioningStarted(this, repositionPodTask.Pod.Waypoint, repositionPodTask.StorageLocation, repositionPodTask.Pod);
                    }
                    // We are already carrying a pod: we cannot execute the task
                    else
                    {
                        Instance.LogDefault("WARNING! Cannot reposition a pod when the robot already is carrying one!");
                        Instance.Controller.BotManager.TaskAborted(this, repositionPodTask);
                        return;
                    }

                    break;
                case BotTaskType.Insert:

                    //re-optimize
                    RequestReoptimization = true;

                    InsertTask storeTask = t as InsertTask;
                    bool slowStartInputEnabled = Instance.SettingConfig.SlowStartInputEnabled;
                    if (storeTask.ReservedPod != Pod)
                    {
                        var podWaypoint = storeTask.ReservedPod.Waypoint;
                        _appendMoveStates(CurrentWaypoint, podWaypoint, tripLoaded: false);
                        // Pre-lift hold at pod cell before fetching the pod to the input station.
                        if (slowStartInputEnabled)
                            StateQueueEnqueue(new BotSlowStartHoldInput(storeTask));
                        StateQueueEnqueue(new BotPickupPod(storeTask.ReservedPod));
                        _appendMoveStates(podWaypoint, storeTask.InputStation.Waypoint, tripLoaded: true);
                    }
                    else
                    {
                        // Bot already carrying the reserved pod — post-lift hold.
                        if (slowStartInputEnabled)
                            StateQueueEnqueue(new BotSlowStartHoldInput(storeTask));
                        _appendMoveStates(CurrentWaypoint, storeTask.InputStation.Waypoint, tripLoaded: true);
                    }
                    StateQueueEnqueue(new BotGetItems(storeTask));

                    break;
                case BotTaskType.Extract:

                    //re-optimize
                    RequestReoptimization = true;

                    ExtractTask extractTask = t as ExtractTask;
                    bool slowStartEnabled = Instance.SettingConfig.SlowStartEnabled;
                    if (extractTask.ReservedPod != Pod)
                    {
                        var podWaypoint = extractTask.ReservedPod.Waypoint;
                        _appendMoveStates(CurrentWaypoint, podWaypoint, tripLoaded: false);
                        // Pre-lift hold: bot reaches pod cell, then HOLDS empty (orange) before
                        // lifting. Release timing accounts for PodTransferTime so that
                        // (hold + lift + travel) ≈ T_starve. Pre-lift hold also saves the
                        // configured loaded/empty support delta during the hold window.
                        if (slowStartEnabled)
                            StateQueueEnqueue(new BotSlowStartHold(extractTask));
                        StateQueueEnqueue(new BotPickupPod(extractTask.ReservedPod));
                        _appendMoveStates(podWaypoint, extractTask.OutputStation.Waypoint, tripLoaded: true);
                    }
                    else
                    {
                        // Bot already carrying the reserved pod — hold post-lift (only path
                        // available; lift already happened in a prior task).
                        if (slowStartEnabled)
                            StateQueueEnqueue(new BotSlowStartHold(extractTask));
                        _appendMoveStates(CurrentWaypoint, extractTask.OutputStation.Waypoint, tripLoaded: true);
                    }
                    StateQueueEnqueue(new BotPutItems(extractTask));

                    break;
                case BotTaskType.Rest:
                    var restTask = t as RestTask;
                    // Only append move task to get to resting location, if we are not at it yet
                    if ((restTask.RestingLocation != null) && (CurrentWaypoint != restTask.RestingLocation || Moving))
                        _appendMoveStates(CurrentWaypoint, restTask.RestingLocation, tripLoaded: false);
                    StateQueueEnqueue(new BotRest(restTask.RestingLocation, BotRest.DEFAULT_REST_TIME)); // TODO set paramterized wait time and adhere to it
                    break;
                default:
                    throw new ArgumentException("Unknown task-type: " + t.Type);
            }

            // Track task count
            StatAssignedTasks++;
            StatTotalTaskCounts[t.Type]++;
        }

        /// <summary>
        /// appends the move states with respect to tiers and elevators.
        /// </summary>
        /// <param name="waypointFrom">The from waypoint.</param>
        /// <param name="waypointTo">The destination waypoint.</param>
        private void _appendMoveStates(Waypoint waypointFrom, Waypoint waypointTo, bool? tripLoaded = null)
        {
            // ── Per-trip tracking: each solver-dispatched path (currentWaypoint → destinationWaypoint)
            // counts as one trip. Loaded/empty passed by caller (task dispatch knows the semantic —
            // Pod state in queue may not reflect runtime state yet). Rest task (tripLoaded==null)
            // is excluded (non-task return-to-idle). Replans do not pass through here.
            if (tripLoaded.HasValue)
            {
                // Close-out of the previous trip happens in CloseCurrentTrip() when the bot
                // actually arrives at its destination (PickupPod/SetdownPod/PutItems/GetItems
                // Act-init). Here we only open the new trip — multiple _appendMoveStates can
                // run in the same tick at task dispatch without the close-out being premature.
                if (tripLoaded.Value) StatTripCountLoaded++;
                else                  StatTripCountEmpty++;
                _pendingTripsLoaded.Enqueue(tripLoaded.Value);
            }

            double distance;
            var checkPoints = Instance.Controller.PathManager.FindElevatorSequence(this, waypointFrom, waypointTo, out distance);
            StatDistanceEstimated += distance;

            foreach (var point in checkPoints)
            {
                StateQueueEnqueue(new BotMove(point.Item2));
                StateQueueEnqueue(new UseElevator(point.Item1, point.Item2, point.Item3));
            }

            StateQueueEnqueue(new BotMove(waypointTo));
        }

        /// <summary>
        /// Dequeues the state.
        /// </summary>
        /// <param name="lastTime">The last time.</param>
        /// <param name="currentTime">The current time.</param>
        private void DequeueState(double lastTime, double currentTime)
        {
            IBotState dequeuedState = StateQueueDequeue();

            // ── Stat Hook: count completed extract orders ──
            if (dequeuedState.Type == BotStateType.PutItems)
                StatOrdersCompleted++;

            /*
            if (_stateQueue.Count > 0)
            {
                //directly the next way point
                var moveBot = _stateQueue.Peek() as BotMove;
                if(moveBot != null && moveBot.DestinationWaypoint == CurrentWaypoint)
                {
                    DequeueState(currentTime);
                    return;
                }
            }
             * */

            Debug.Assert(StateQueueCount == 0 || !(StateQueuePeek() is BotMove) || this.CurrentWaypoint.Tier.ID == ((BotMove)StateQueuePeek()).DestinationWaypoint.Tier.ID);

            // Act on next task
            if (StateQueueCount > 0)
                StateQueuePeek().Act(this, lastTime, currentTime);
        }

        private void MarkPlannedWaitStopGoIfForced(double waitTime)
        {
            _pendingPlannedWaitStopGo = false;
            _pendingPlannedWaitWaypoint = null;
            _pendingPlannedWaitDecelEnergyJ = 0.0;

            if (waitTime <= 0.0 || Path == null || CurrentWaypoint == null)
                return;
            if (_lastCommittedSegmentStartWaypoint == null || _lastCommittedSegmentEndWaypoint != CurrentWaypoint)
                return;

            var nextStopAction = Path.Actions.Skip(1).FirstOrDefault(a => a.StopAtNode);
            if (nextStopAction == null)
                return;

            var nextStopWaypoint = Instance.Controller.PathManager.GetWaypointByNodeId(nextStopAction.Node);
            if (nextStopWaypoint == null || nextStopWaypoint == CurrentWaypoint || _lastCommittedSegmentStartWaypoint == CurrentWaypoint)
                return;

            double inboundOrientation = Circle.GetOrientation(
                _lastCommittedSegmentStartWaypoint.X, _lastCommittedSegmentStartWaypoint.Y,
                CurrentWaypoint.X, CurrentWaypoint.Y);
            double outboundOrientation = Circle.GetOrientation(
                CurrentWaypoint.X, CurrentWaypoint.Y,
                nextStopWaypoint.X, nextStopWaypoint.Y);

            if (Math.Abs(Circle.GetOrientationDifference(inboundOrientation, outboundOrientation)) >= Instance.StraightOrientationTolerance)
                return;

            _pendingPlannedWaitStopGo = true;
            _pendingPlannedWaitWaypoint = CurrentWaypoint;
            _pendingPlannedWaitDecelEnergyJ = _lastCommittedSegmentE2DecelJ;
        }

        private void CommitPendingPlannedWaitStopGo(double resumeAccelEnergyJ)
        {
            if (!_pendingPlannedWaitStopGo || _pendingPlannedWaitWaypoint != CurrentWaypoint)
                return;

            double stopGoEnergy = _pendingPlannedWaitDecelEnergyJ + resumeAccelEnergyJ;
            StatStopAndGoCount++;
            StatStopAndGoEnergyJ += stopGoEnergy;
            if (_lastTripJITDestination != null)
                _lastTripJITStopGoCount++;
            RecordStopGoEvent(
                "whca_planned_wait",
                _lastCommittedSegmentStartWaypoint,
                CurrentWaypoint,
                NextWaypoint,
                stopGoEnergy,
                _pendingPlannedWaitDecelEnergyJ,
                resumeAccelEnergyJ);

            _pendingPlannedWaitStopGo = false;
            _pendingPlannedWaitWaypoint = null;
            _pendingPlannedWaitDecelEnergyJ = 0.0;
        }

        private bool IsQueueManagerCreepSegment(Waypoint from, Waypoint to)
        {
            return IsQueueing &&
                from != null &&
                to != null &&
                from.QueueManager != null &&
                from.QueueManager == to.QueueManager;
        }

        private void CommitQueueManagerStopGo(double resumeAccelEnergyJ)
        {
            double stopGoEnergy = _lastCommittedSegmentE2DecelJ + resumeAccelEnergyJ;
            StatQueueStopAndGoCount++;
            StatQueueStopAndGoEnergyJ += stopGoEnergy;
            if (_lastTripJITDestination != null)
                _lastTripJITQueueStopGoCount++;
            RecordStopGoEvent(
                "queue_manager_creep",
                _lastCommittedSegmentStartWaypoint,
                CurrentWaypoint,
                NextWaypoint,
                stopGoEnergy,
                _lastCommittedSegmentE2DecelJ,
                resumeAccelEnergyJ);
        }

        private int GetStopGoNodeId(Waypoint waypoint)
        {
            if (waypoint == null || Instance?.Controller?.PathManager == null)
                return -1;
            try { return Instance.Controller.PathManager.GetNodeIdByWaypoint(waypoint); }
            catch { return -1; }
        }

        private void RecordStopGoEvent(string type, Waypoint from, Waypoint stop, Waypoint to, double energyJ, double decelJ, double accelJ)
        {
            if (Instance == null || stop == null)
                return;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            int queueTerminalNode = stop.QueueManager != null ? GetStopGoNodeId(stop.QueueManager.QueueWaypoint) : -1;
            int destinationNode = GetStopGoNodeId(DestinationWaypoint);
            Instance.StatStopGoEventRows.Add(string.Join(";", new[]
            {
                Instance.Controller.CurrentTime.ToString(ci),
                type,
                ID.ToString(ci),
                GetStopGoNodeId(from).ToString(ci),
                GetStopGoNodeId(stop).ToString(ci),
                GetStopGoNodeId(to).ToString(ci),
                stop.X.ToString(ci),
                stop.Y.ToString(ci),
                (stop.Tier != null ? stop.Tier.ID : -1).ToString(ci),
                (energyJ / 1000.0).ToString(ci),
                (decelJ / 1000.0).ToString(ci),
                (accelJ / 1000.0).ToString(ci),
                queueTerminalNode.ToString(ci),
                destinationNode.ToString(ci),
                (Pod != null ? "true" : "false")
            }));

            if (type == "whca_planned_wait")
            {
                Instance.StatConflictWaitHeatPoints.Add(new LocationDatapoint()
                {
                    TimeStamp = Instance.Controller.CurrentTime,
                    Tier = stop.Tier != null ? stop.Tier.ID : -1,
                    X = stop.X,
                    Y = stop.Y,
                    BotTask = CurrentTask != null ? CurrentTask.Type : BotTaskType.None,
                });
            }
        }

        /// <summary>
        /// Sets the next way point.
        /// </summary>
        /// <param name="waypoint">The way point.</param>
        /// <param name="currentTime">The current time.</param>
        /// <returns>A boolean value indicating whether the reservation was successful.</returns>
        public bool setNextWaypoint(Waypoint waypoint, double currentTime)
        {
            if (GetSpeed() > 0)
                return false;
            if (X == waypoint.X && Y == waypoint.Y)
            {
                // Bot is physically already at the target waypoint. This can occur when
                // the CBS path references a node at the bot's current coordinates but
                // a different Waypoint object (e.g. after replanning mid-arrival).
                // Treat as arrived: update CurrentWaypoint and return false so BotMove retries.
                Instance.LogInfo($"Bot{ID}: setNextWaypoint skipped – already at ({X:F2},{Y:F2}), updating CurrentWaypoint");
                CurrentWaypoint = waypoint;
                return false;
            }

            _startOrientation = Orientation;
            _endOrientation = Circle.GetOrientation(X, Y, waypoint.X, waypoint.Y);
            var rotateDuration = Physics.getTimeNeededToTurn(_startOrientation, _endOrientation);
            var waitUntil = Math.Max(_waitUntil, currentTime);

            if (Instance.Controller.PathManager.RegisterNextWaypoint(this, currentTime, waitUntil, rotateDuration, CurrentWaypoint, waypoint))
            {
                //set way point
                NextWaypoint = waypoint;

                //set move times
                _waitUntil = waitUntil;
                _rotateDuration = rotateDuration;
                // Apply optional gym multipliers (Plan 2)
                if (AccelerationMultiplier.HasValue || DecelerationMultiplier.HasValue)
                {
                    double a = MaxAcceleration * (AccelerationMultiplier ?? 1.0);
                    double d = MaxDeceleration * (DecelerationMultiplier ?? 1.0);
                    Physics = new Physics(a, d, MaxVelocity, TurnSpeed);
                }
                double segmentDistance = CurrentWaypoint.GetDistance(NextWaypoint);
                _driveDuration = Physics.getTimeNeededToMove(0, segmentDistance);

                // ── Energy Hook: E1 + E2 + E3 (segment drive) + E4 (rotation) ──
                double mTotal = EnergyConsumption.GetTotalMass(Pod);
                double e1, e2, e3;
                EnergyConsumption.ComputeSegmentEnergy(
                    mTotal, Physics.Acceleration, Physics.Deceleration, Physics.MaxSpeed,
                    segmentDistance, out e1, out e2, out e3);

                StatEnergyE1AccelJ += e1;
                StatEnergyE2DecelJ += e2;
                StatEnergyE3CruiseJ += e3;

                // WHCA* planned-wait stop-and-go: this resume leg contributes its E1.
                // Queue-manager creep stop-and-go is tracked separately below.
                bool queueManagerCreepSegment = _lastCommittedSegmentEndWaypoint == CurrentWaypoint &&
                    IsQueueManagerCreepSegment(CurrentWaypoint, NextWaypoint);
                CommitPendingPlannedWaitStopGo(e1);
                if (queueManagerCreepSegment)
                    CommitQueueManagerStopGo(e1);
                _lastCommittedSegmentStartWaypoint = CurrentWaypoint;
                _lastCommittedSegmentEndWaypoint = NextWaypoint;
                _lastCommittedSegmentE2DecelJ = e2;

                // E4: rotation energy (θ derived from rotateDuration and TurnSpeed)
                // mTotal used here so loaded robots correctly pay heavier rotational inertia.
                double e4 = 0.0;
                if (_rotateDuration > 0.0 && TurnSpeed > 0.0)
                {
                    double thetaRad = _rotateDuration / TurnSpeed * 2.0 * Math.PI;
                    e4 = EnergyConsumption.E4_Rotation(thetaRad, TurnSpeed, mTotal);
                    StatEnergyE4RotationJ += e4;
                }

                double segmentTotal = e1 + e2 + e3 + e4;
                StatEnergyTotalJ += segmentTotal;
                StatDistanceTraveledM += segmentDistance;

                // ── Pref calibration: event-level 8-accumulator split ──────────────
                // isLoaded judged at the instant of commitment (Pod != null at this tick).
                // moveE = drive energy (E1+E2+E3); moveT = physical drive time.
                // turnE = rotation energy (E4);     turnT = physical rotation time.
                double moveE = e1 + e2 + e3;
                double turnE = e4;
                if (Pod != null)
                {
                    StatMoveEnergyLoadedJ += moveE;
                    StatMoveTimeLoadedSec += _driveDuration;
                    StatTurnEnergyLoadedJ += turnE;
                    StatTurnTimeLoadedSec += _rotateDuration;
                }
                else
                {
                    StatMoveEnergyEmptyJ  += moveE;
                    StatMoveTimeEmptySec  += _driveDuration;
                    StatTurnEnergyEmptyJ  += turnE;
                    StatTurnTimeEmptySec  += _rotateDuration;
                }

                // Motion behavior counters
                if (_rotateDuration > 0) StatTurningCount++;
                // Loaded/Empty-specific counters
                if (Pod != null)
                {
                    StatLoadedDistanceM += segmentDistance;
                    if (_rotateDuration > 0) StatLoadedTurningCount++;
                }
                else
                {
                    StatEmptyDistanceM += segmentDistance;
                    if (_rotateDuration > 0) StatEmptyTurningCount++;
                }

                // Per-trip cumulative counters (tied to the currently-open trip)
                if (_tripOpen)
                {
                    if (_rotateDuration > 0) _currentTripTurnCount++;
                }

                return true;

            }
            else
            {
                NextWaypoint = null;
                if (RequestReoptimizationAfterFailingOfNextWaypointReservation)
                    RequestReoptimization = true;

                // Log failed reservation
                Instance.StatOverallFailedReservations++;

                return false;
            }
        }

        /// <summary>
        /// Blocks the robot for the specified time.
        /// </summary>
        /// <param name="time">The time to be blocked for.</param>
        public override void WaitUntil(double time)
        {
            if (this.GetSpeed() > 0)
                throw new Exception("Can not wait while driving!");

            _waitUntil = time;
        }

        #region Stage A Helper Methods

        /// <summary>
        /// [Stage A] Returns true when the bot is actively moving between waypoints
        /// (committed to a next waypoint). Used to detect mid-segment state.
        /// </summary>
        public bool IsMidSegment()
        {
            return NextWaypoint != null;
        }

        /// <summary>
        /// [Stage A] Returns true when it is safe to swap the active path for a new one.
        /// Safe conditions: not moving and no committed next waypoint.
        /// </summary>
        public bool CanSafelySwapPathNow()
        {
            return NextWaypoint == null && GetSpeed() == 0.0;
        }

        /// <summary>
        /// [Stage A] Stores a new path for deferred application. The path is not activated
        /// until ActivatePendingPlannedPathIfSafe() is called.
        /// </summary>
        public void SetPendingPlannedPath(Path path, double currentTime)
        {
            PendingPlannedPath = path;
        }

        /// <summary>
        /// [Stage A] Applies the pending path if it is currently safe to do so
        /// (not mid-segment, speed = 0). If applied, records the assignment time.
        /// </summary>
        public void ActivatePendingPlannedPathIfSafe(double currentTime)
        {
            if (CanSafelySwapPathNow() && HasPendingPlannedPath)
            {
                Path = PendingPlannedPath;
                PendingPlannedPath = null;
                LastPathAssignmentTime = currentTime;
            }
        }

        #endregion

        #endregion

        /// <summary>
        /// Determines whether this bot is fixed to a position.
        /// </summary>
        /// <returns>true, if it is fixed</returns>
        public bool hasFixedPosition()
        {
            return StateQueueCount == 0 || !(StateQueuePeek() is BotMove);
        }

        /// <summary>
        /// Determines whether this bot is currently resting.
        /// </summary>
        /// <returns><code>true</code> if the bot is resting, <code>false</code> otherwise.</returns>
        public bool IsResting()
        {
            return StateQueueCount == 0 || StateQueuePeek() is BotRest;
        }

        /// <summary>
        /// Logs the data of an unfinished trip.
        /// </summary>
        internal override void LogIncompleteTrip()
        {
            if (StateQueueCount > 0 && StateQueuePeek() is BotMove)
                (StateQueuePeek() as BotMove).LogUnfinishedTrip(this);
        }

        #endregion

        #region Queueing zone tracking

        /// <summary>
        /// Stores the last trip start time.
        /// </summary>
        private double _queueTripStartTime = double.NaN;
        /// <summary>Expected (ideal kinematic) duration [s] of the currently open OutputStation trip,
        /// computed by JIT IdealTravelTime at trip start. NaN when no trip open / trip not toward OS.
        /// Used to validate ETA model against measured arrival time.</summary>
        internal double _lastTripExpectedDurationSec = double.NaN;
        internal int _lastTripPathNodeCount = 0;
        internal double _lastTripPathLength = 0.0;
        internal int _lastTripSourceId = -1;
        internal int _lastTripDestId = -1;
        internal double _lastTripStartTimeForJIT = double.NaN;
        internal int _lastTripJITTripId = -1;
        private int _jitTripSequence = 0;
        private int _lastTripJITStopGoCount = 0;
        private int _lastTripJITQueueStopGoCount = 0;
        private double _lastTripJITWaitSec = 0.0;
        private string _lastTripJITTaskId = "";
        internal Waypoints.Waypoint _lastTripJITDestination = null;
        /// <summary>Sequence of waypoints actually visited during the current JIT-tracked trip, in order.
        /// Used to validate IdealTravelTime formula against the path the bot truly walked
        /// (independent of A* path-choice variance).</summary>
        internal List<Waypoints.Waypoint> _lastTripActualPath = null;
        internal List<double> _lastTripActualArrivals = null;
        internal double _lastTripInitialOrientation = double.NaN;
        /// <summary>Time [s] when the bot+pod entered the OS queue zone (start of queue wait).</summary>
        internal double _potQueueArrivalTime = double.NaN;
        /// <summary>Input-station analog of _potQueueArrivalTime: queue-zone arrival time for a
        /// store/InsertTask pod, consumed in BotGetItems to measure input-station per-pod queue
        /// wait (previously unmeasured — only output BotPutItems recorded queue wait).</summary>
        internal double _potInputQueueArrivalTime = double.NaN;
        /// <summary>Time [s] when BotPutItems first initialized (station begins picking from this pod).</summary>
        internal double _potPickingStartTime = double.NaN;
        /// <summary>
        /// Contains all output station queueing areas.
        /// </summary>
        private VolatileIDDictionary<OutputStation, SimpleRectangle> _queueZonesOStations;
        /// <summary>
        /// Contains all input station queueing areas.
        /// </summary>
        private VolatileIDDictionary<InputStation, SimpleRectangle> _queueZonesIStations;
        /// <summary>
        /// Checks whether the bot is currently within the stations queueing area.
        /// </summary>
        /// <param name="station">The station to check.</param>
        /// <returns><code>true</code> if the bot is within the stations queueing area, <code>false</code> otherwise.</returns>
        /// <summary>
        /// Previous-tick snapshot of the station processing state.
        /// Used for edge-triggered station-arrival counting.
        /// </summary>
        private BotStateType? _prevStationProcessingType = null;

        /// <summary>
        /// Edge-triggered station arrival counter. Counts the start of station processing:
        /// GetItems for input-side arrivals and PutItems for output-side arrivals.
        /// Excluded during Rest task.
        /// </summary>
        private void _updateStationArrival()
        {
            if (CurrentTask != null && CurrentTask.Type == BotTaskType.Rest)
            {
                _prevStationProcessingType = null;
                return;
            }

            BotStateType? stationProcessingType = null;
            if (StateQueueCount > 0)
            {
                BotStateType stateType = StateQueuePeek().Type;
                if (stateType == BotStateType.GetItems || stateType == BotStateType.PutItems)
                    stationProcessingType = stateType;
            }

            if (stationProcessingType.HasValue && _prevStationProcessingType != stationProcessingType)
            {
                if (stationProcessingType.Value == BotStateType.PutItems)
                    StatOutputStationArrivals++;
                else
                    StatInputStationArrivals++;
            }

            _prevStationProcessingType = stationProcessingType;
        }

        private SimpleRectangle GetStationQueueZone(OutputStation station)
        {
            if (_queueZonesOStations == null)
                _queueZonesOStations = new VolatileIDDictionary<OutputStation, SimpleRectangle>(Instance.OutputStations.Select(s =>
                {
                    double lowX = s.Queues != null && s.Queues.Any() && s.Queues.First().Value.Any() ? s.Queues.Min(q => q.Value.Min(w => w.X)) : s.X - 0.5;
                    double highX = s.Queues != null && s.Queues.Any() && s.Queues.First().Value.Any() ? s.Queues.Max(q => q.Value.Max(w => w.X)) : s.X + 0.5;
                    double lowY = s.Queues != null && s.Queues.Any() && s.Queues.First().Value.Any() ? s.Queues.Min(q => q.Value.Min(w => w.Y)) : s.Y - 0.5;
                    double highY = s.Queues != null && s.Queues.Any() && s.Queues.First().Value.Any() ? s.Queues.Max(q => q.Value.Max(w => w.Y)) : s.Y + 0.5;
                    return new VolatileKeyValuePair<OutputStation, SimpleRectangle>(s, new SimpleRectangle(s.Tier, lowX, lowY, highX - lowX, highY - lowY));
                }).ToList());
            return _queueZonesOStations[station];
        }

        private bool IsInStationQueueZone(OutputStation station)
        {
            return GetStationQueueZone(station).IsContained(Tier, X, Y);
        }

        private Waypoint ResolveFirstQueueWaypointOnPath(OutputStation station, List<Waypoint> path)
        {
            if (station == null || path == null || path.Count == 0)
                return null;
            SimpleRectangle zone = GetStationQueueZone(station);
            foreach (var waypoint in path)
            {
                if (waypoint != null && zone.IsContained(waypoint.Tier, waypoint.X, waypoint.Y))
                    return waypoint;
            }
            return null;
        }

        private bool TryGetQueueZoneEntryTime(OutputStation station, double xOld, double yOld, double xNew, double yNew, double segmentStartTime, double segmentEndTime, out double entryTime)
        {
            entryTime = double.NaN;
            SimpleRectangle zone = GetStationQueueZone(station);
            if (zone.Tier != Tier || segmentEndTime < segmentStartTime)
                return false;

            if (zone.IsContained(Tier, xOld, yOld))
            {
                entryTime = segmentStartTime;
                return true;
            }

            double dx = xNew - xOld;
            double dy = yNew - yOld;
            double tEnter = 0.0;
            double tExit = 1.0;
            if (!ClipSegmentToRange(xOld, dx, zone.XLower, zone.XUpper, ref tEnter, ref tExit))
                return false;
            if (!ClipSegmentToRange(yOld, dy, zone.YLower, zone.YUpper, ref tEnter, ref tExit))
                return false;
            if (tExit < 0.0 || tEnter > 1.0)
                return false;

            double t = Math.Max(0.0, Math.Min(1.0, tEnter));
            entryTime = segmentStartTime + (segmentEndTime - segmentStartTime) * t;
            return true;
        }

        private static bool ClipSegmentToRange(double origin, double delta, double min, double max, ref double tEnter, ref double tExit)
        {
            const double eps = 1e-9;
            if (Math.Abs(delta) < eps)
                return min <= origin && origin <= max;

            double t1 = (min - origin) / delta;
            double t2 = (max - origin) / delta;
            if (t1 > t2)
            {
                double tmp = t1;
                t1 = t2;
                t2 = tmp;
            }
            if (t1 > tEnter) tEnter = t1;
            if (t2 < tExit) tExit = t2;
            return tEnter <= tExit;
        }

        private void RecordJITEtaArrival(double arrivalTime)
        {
            if (_lastTripJITDestination != null && !double.IsNaN(_lastTripExpectedDurationSec) && !double.IsNaN(_lastTripStartTimeForJIT))
            {
                double actualDur = Math.Max(0.0, arrivalTime - _lastTripStartTimeForJIT);
                Instance.StatJITEtaExpectedSamples.Add(_lastTripExpectedDurationSec);
                Instance.StatJITEtaActualSamples.Add(actualDur);
                Instance.StatJITEtaPathNodeCounts.Add(_lastTripPathNodeCount);
                Instance.StatJITEtaPathLengths.Add(_lastTripPathLength);
                Instance.StatJITEtaSourceIds.Add(_lastTripSourceId);
                Instance.StatJITEtaDestIds.Add(_lastTripDestId);
                Instance.StatJITEtaBotIds.Add(ID);
                Instance.StatJITEtaTaskIds.Add(_lastTripJITTaskId ?? "");
                Instance.StatJITEtaTripIds.Add(_lastTripJITTripId);
                Instance.StatJITEtaStopGoCounts.Add(_lastTripJITStopGoCount);
                Instance.StatJITEtaQueueStopGoCounts.Add(_lastTripJITQueueStopGoCount);
                Instance.StatJITEtaWaitSecs.Add(_lastTripJITWaitSec);
            }
            _lastTripExpectedDurationSec = double.NaN;
            _lastTripJITDestination = null;
            _lastTripActualPath = null;
            _lastTripActualArrivals = null;
            _lastTripJITTripId = -1;
            _lastTripJITStopGoCount = 0;
            _lastTripJITQueueStopGoCount = 0;
            _lastTripJITWaitSec = 0.0;
            _lastTripJITTaskId = "";
        }
        /// <summary>
        /// Checks whether the bot is currently within the stations queueing area.
        /// </summary>
        /// <param name="station">The station to check.</param>
        /// <returns><code>true</code> if the bot is within the stations queueing area, <code>false</code> otherwise.</returns>
        private bool IsInStationQueueZone(InputStation station)
        {
            if (_queueZonesIStations == null)
                _queueZonesIStations = new VolatileIDDictionary<InputStation, SimpleRectangle>(Instance.InputStations.Select(s =>
                {
                    double lowX = s.Queues != null && s.Queues.Any() && s.Queues.First().Value.Any() ? s.Queues.Min(q => q.Value.Min(w => w.X)) : s.X - 0.5;
                    double highX = s.Queues != null && s.Queues.Any() && s.Queues.First().Value.Any() ? s.Queues.Max(q => q.Value.Max(w => w.X)) : s.X + 0.5;
                    double lowY = s.Queues != null && s.Queues.Any() && s.Queues.First().Value.Any() ? s.Queues.Min(q => q.Value.Min(w => w.Y)) : s.Y - 0.5;
                    double highY = s.Queues != null && s.Queues.Any() && s.Queues.First().Value.Any() ? s.Queues.Max(q => q.Value.Max(w => w.Y)) : s.Y + 0.5;
                    return new VolatileKeyValuePair<InputStation, SimpleRectangle>(s, new SimpleRectangle(s.Tier, lowX, lowY, highX - lowX, highY - lowY));
                }).ToList());
            return _queueZonesIStations[station].IsContained(Tier, X, Y);
        }

        #endregion

        #region IUpdateable Members

        /// <summary>
        /// The next event when this element has to be updated.
        /// </summary>
        /// <param name="currentTime">The current time of the simulation.</param>
        /// <returns>The next time this element has to be updated.</returns>
        public override double GetNextEventTime(double currentTime)
        {
            // Return soonest event that has not happened yet
            var minUntil = Double.PositiveInfinity;
            if (_waitUntil >= currentTime) minUntil = Math.Min(_waitUntil, minUntil);
            if (_waitUntil + _rotateDuration >= currentTime) minUntil = Math.Min(_waitUntil + _rotateDuration, minUntil);
            if (_waitUntil + _rotateDuration + _driveDuration >= currentTime) minUntil = Math.Min(_waitUntil + _rotateDuration + _driveDuration, minUntil);
            return minUntil;
        }

        /// <summary>
        /// update bot
        /// </summary>
        /// <param name="lastTime">time stamp: last update</param>
        /// <param name="currentTime">time stamp: now</param>
        public override void Update(double lastTime, double currentTime)
        {
            //wait short start time
            if (currentTime < 0.2)
                return;
            // Reset per-tick rotation flag; only _updateDrive sets it true when a turn is active.
            _isRotatingThisTick = false;
            //bot is blocked
            if (this._waitUntil >= currentTime)
            {
                // We still want to update the statistics of the bot
                _updateStatistics(currentTime - lastTime, X, Y);
                return;
            }

            var delta = currentTime - lastTime;
            var xOld = X;
            var yOld = Y;

            // Reset per-tick arrival flag (tick-coherence guard)
            _arrivedAtWaypointThisTick = false;

            //get a task
            if (StateQueueCount == 0)
            {
                if (CurrentTask != null)
                    Instance.Controller.BotManager.TaskComplete(this, CurrentTask);
                Instance.Controller.BotManager.RequestNewTask(this);
            }

            //do state dependent action
            if (StateQueueCount > 0)
                StateQueuePeek().Act(this, lastTime, currentTime);

            //bot is blocked
            if (this._waitUntil >= currentTime)
                return;

            // Indicate change
            _changed = true;
            Instance.Changed = true;

            //get target orientation
            _updateDrive(lastTime, currentTime);

            // Second state-dependent action: allows the bot to immediately commit to the
            // next path segment after completing a drive within the same tick.
            // In decentralized mode (AgentAStar / JunctionArbitration) this is dangerous:
            // the bot would bypass the conflict-detection pass that PathManager.Update()
            // performs at the START of each tick.  We defer the commitment to the next tick
            // so PathManager can re-evaluate with the bot's updated position.
            // In centralized mode (reservation-table planners) this is safe because
            // reservations already guarantee conflict-free paths.
            if (StateQueueCount > 0)
            {
                // Lazily cache decentralized mode flag
                if (!_isDecentralizedMode.HasValue && Instance.Controller?.PathManager != null)
                {
                    var pt = Instance.ControllerConfig.PathPlanningConfig.GetMethodType();
                    _isDecentralizedMode = pt == PathPlanningMethodType.AgentAStar;
                }

                if (!(_isDecentralizedMode == true && _arrivedAtWaypointThisTick))
                    StateQueuePeek().Act(this, lastTime, currentTime);
            }

            //save statistics
            _updateStatistics(delta, xOld, yOld);
        }

        /// <summary>
        /// Drive the bot.
        /// </summary>
        /// <param name="lastTime">The last time.</param>
        /// <param name="currentTime">The current time.</param>
        private void _updateDrive(double lastTime, double currentTime)
        {
            // Is there a rotation still going on?
            if (_waitUntil + _rotateDuration >= currentTime)
            {
                // --> First rotate
                _isRotatingThisTick = true;  // mark: this tick is a real turn (not a wait/pickup)
                _updateRotation(currentTime);
            }
            else
            {
                // Complete any started rotation
                if (_endOrientation != Orientation)
                {
                    //_rotateDuration = 0;
                    Orientation = _endOrientation;
                    _startOrientation = _endOrientation;
                    if (this.Pod != null && Instance.SettingConfig.RotatePods)
                        this.Pod.Orientation = _endOrientation;
                }

                // --> Then move (if we have a target)
                if (NextWaypoint != null)
                {
                    _updateMove(lastTime, currentTime);
                }

                //_updatePassedWaypoints();
            }
        }

        /*/// <summary>
        /// _updates the passed way points.
        /// </summary>
        private void _updatePassedWaypoints()
        {
            //delete passed way points in skipped way points
            for (int i = 0; i < skippendWaypoints.Count; i++)
            {
                if (Instance.Controller.PathManager.GetWaypointByNodeId(skippendWaypoints[i].Node).GetSquaredDistance(NextWaypoint) > GetSquaredDistance(NextWaypoint))
                    skippendWaypoints.RemoveAt(i--);
                else
                    break;
            }
        }*/

        /// <summary>
        /// update the rotation to the target orientation
        /// </summary>
        /// <param name="currentTime">time stamp: now</param>
        private void _updateRotation(double currentTime)
        {
            //stop the pod (this should already be 0)
            XVelocity = YVelocity = 0;

            Orientation = Physics.getOrientationAfterTimeStep(_startOrientation, _endOrientation, currentTime - _waitUntil);

            //set the pod orientation
            if (this.Pod != null && Instance.SettingConfig.RotatePods)
                this.Pod.Orientation = Orientation;
        }

        /// <summary>
        /// move the bot towards the next way point
        /// </summary>
        /// <param name="currentTime">time stamp: now</param>
        private void _updateMove(double lastTime, double currentTime)
        {
            //get distance traveled
            double distanceTraveled;
            double speed;
            Physics.getTimeNeededToMove(0, CurrentWaypoint.GetDistance(NextWaypoint));
            Physics.GetDistanceTraveledAfterTimeStep(0, Math.Min(_driveDuration, currentTime - _waitUntil - _rotateDuration), out distanceTraveled, out speed);

            //set speed
            XVelocity = Math.Cos(Orientation) * speed;
            YVelocity = Math.Sin(Orientation) * speed;

            //travel in percentage
            var travelPercentage = distanceTraveled / NextWaypoint.GetDistance(CurrentWaypoint);

            //initiate new positions and reset it during this method
            var xOld = X;
            var yOld = Y;
            var xNew = CurrentWaypoint.X * (1 - travelPercentage) + NextWaypoint.X * travelPercentage;
            var yNew = CurrentWaypoint.Y * (1 - travelPercentage) + NextWaypoint.Y * travelPercentage;
            double movementStartTime = Math.Max(lastTime, _waitUntil + _rotateDuration);
            double movementEndTime = Math.Min(currentTime, _waitUntil + _rotateDuration + _driveDuration);

            if (currentTime >= _waitUntil + _rotateDuration + _driveDuration)
            {
                //reached goal
                XVelocity = YVelocity = 0;
                xNew = NextWaypoint.X;
                yNew = NextWaypoint.Y;
                CurrentWaypoint = NextWaypoint;
                NextWaypoint = null;
                // Tick-coherence guard: signal that this bot just completed a segment.
                _arrivedAtWaypointThisTick = true;
                // ── JIT validation: trip ends when bot reaches the recorded JIT destination.
                // Expected is the prediction made at trip start (A* path + IdealTravelTime),
                // NOT recomputed on actual path. This validates the predictor end-to-end.
                if (_lastTripJITDestination != null && CurrentWaypoint == _lastTripJITDestination
                    && (DestinationWaypoint == null || DestinationWaypoint.OutputStation == null)
                    && !double.IsNaN(_lastTripExpectedDurationSec))
                {
                    RecordJITEtaArrival(currentTime);
                }
            }

            // Try to make move. If can't ask move due to a collision, then stop
            bool moveSucceeded = Instance.Compound.BotCurrentTier[this].MoveBotOverride(this, xNew, yNew);
            if (!moveSucceeded)
            {
                // Suppress per-move log in AgentAStar mode: bots intentionally overlap and
                // failed moves happen constantly — logging every attempt floods the UI and
                // causes severe lag (hundreds of calls per sim step with many bots stuck).
                if (!Instance.BotCrashHandler.SuppressMoveFailureLog)
                    Instance.LogInfo("Potential collision (" + GetIdentfierString() + ") - adding check for crashhandler ...");
                // Mark the bot for collision investigation
                Instance.BotCrashHandler.AddPotentialCrashBot(this);
            }

            // Check whether bot is now in destination's queueing area
            if (!double.IsNaN(_queueTripStartTime))
            {
                // Check whether the destination is an output-station and we reached it
                if (DestinationWaypoint.OutputStation != null)
                {
                    double queueArrivalTime = double.NaN;
                    bool enteredQueueZone = moveSucceeded &&
                        TryGetQueueZoneEntryTime(DestinationWaypoint.OutputStation, xOld, yOld, xNew, yNew, movementStartTime, movementEndTime, out queueArrivalTime);
                    if (!enteredQueueZone && IsInStationQueueZone(DestinationWaypoint.OutputStation))
                    {
                        queueArrivalTime = Instance.Controller.CurrentTime;
                        enteredQueueZone = true;
                    }
                    if (enteredQueueZone)
                    {
                        double actualDur = queueArrivalTime - _queueTripStartTime;
                        Instance.NotifySlowStartStationQueueArrival(CurrentTask as ExtractTask, this, queueArrivalTime);
                        Instance.NotifyTripCompleted(this, Statistics.StationTripDatapoint.StationTripType.O, actualDur);
                        // Per-pod queue-wait clock starts now (bot+pod entered queue zone).
                        if (double.IsNaN(_potQueueArrivalTime))
                            _potQueueArrivalTime = queueArrivalTime;
                        RecordJITEtaArrival(queueArrivalTime);
                        _queueTripStartTime = double.NaN;
                    }
                }
                // Check whether the destination is an input-station and we reached it
                if (DestinationWaypoint.InputStation != null)
                    if (IsInStationQueueZone(DestinationWaypoint.InputStation))
                    {
                        double inQueueArrival = Instance.Controller.CurrentTime;
                        Instance.NotifyTripCompleted(this, Statistics.StationTripDatapoint.StationTripType.I, inQueueArrival - _queueTripStartTime);
                        // Per-pod input-station queue-wait clock starts now (pod entered queue zone).
                        if (double.IsNaN(_potInputQueueArrivalTime))
                            _potInputQueueArrivalTime = inQueueArrival;
                        _queueTripStartTime = double.NaN;
                    }
            }
        }

        /// <summary>
        /// update statistical data
        /// </summary>
        /// <param name="delta">time passed since last update</param>
        /// <param name="xOld">Position x before update</param>
        /// <param name="yOld">Position y before update</param>
        private void _updateStatistics(double delta, double xOld, double yOld)
        {
            // Measure moving time
            if (Moving)
                StatTotalTimeMoving += delta;
            // Measure queueing time
            if (IsQueueing)
                StatTotalTimeQueueing += delta;
            // Measure wait time: stationary AND not in a real rotation phase AND not in pickup/setdown.
            // Per-trip wait should only count actual congestion/CBS waits, not mechanical action time.
            // Exclude mechanical stationary actions AND station-process states from wait:
            // - PickupPod/SetdownPod: lift-up/down in place
            // - GetItems/PutItems: items being loaded/unloaded at input/output station
            bool inPickupOrSetdown = StateQueueCount > 0 &&
                (StateQueuePeek().Type == BotStateType.PickupPod || StateQueuePeek().Type == BotStateType.SetdownPod ||
                 StateQueuePeek().Type == BotStateType.GetItems  || StateQueuePeek().Type == BotStateType.PutItems);
            // Slow-start hold is a deliberate, controlled stationary period — NOT congestion wait.
            // Exclude it from wait/energy accumulation so KPI baseline/treatment comparison stays clean.
            // Its own dedicated counters (StatSlowStartHoldTimeSec / StatSlowStartHoldEnergyJ) capture it.
            bool inSlowStartHold = _isSlowStartHolding;
            // Also exclude any time the bot is physically inside a station queue zone (queueing at station)
            bool inStationQueue = false;
            foreach (var s in Instance.OutputStations) { if (IsInStationQueueZone(s)) { inStationQueue = true; break; } }
            if (!inStationQueue)
                foreach (var s in Instance.InputStations) { if (IsInStationQueueZone(s)) { inStationQueue = true; break; } }
            if (inStationQueue) inPickupOrSetdown = true;

            // Rest-task gate: return-to-park is a non-productive trip — no energy / wait / station metrics
            bool isRestTask = (CurrentTask != null && CurrentTask.Type == BotTaskType.Rest);
            // Active-task gate: bot must have a real non-rest task assigned to record productive wait.
            // Support power is now always-on and configured by payload state;
            // it is NOT gated by task state. hasActiveTask/hasSupportTask still gate the
            // wait/queueing SUBSETS below, not the support total.
            bool hasActiveTask = CurrentTask != null &&
                                 CurrentTask.Type != BotTaskType.None &&
                                 !isRestTask;
            bool hasSupportTask = CurrentTask != null &&
                                  CurrentTask.Type != BotTaskType.None;

            // Wait = congestion/CBS-hold: stationary with an active task, no mechanical action.
            // Excludes: rotation (_isRotatingThisTick), lift/setdown (inPickupOrSetdown), no-task standby,
            // and deliberate slow-start hold (inSlowStartHold) — slow-start has its own counters.
            bool inCongestionWait = hasActiveTask && !Moving && !_isRotatingThisTick && !inPickupOrSetdown && !inSlowStartHold;
            if (inCongestionWait)
            {
                StatWaitTimeSec += delta;
                // Accumulate per-trip wait time (congestion/CBS wait only, no mechanical action)
                if (_tripOpen)
                    _currentTripWaitSec += delta;
                if (_lastTripJITDestination != null)
                    _lastTripJITWaitSec += delta;
                // Layer 5: wait-time split by payload state
                if (Pod != null) StatWaitTimeLoadedSec += delta;
                else             StatWaitTimeEmptySec  += delta;
            }


            // Slow-start dedicated time/energy ledger (separated from congestion wait).
            if (hasActiveTask && inSlowStartHold)
            {
                StatSlowStartHoldTimeSec   += delta;
                StatSlowStartHoldEnergyJ   += EnergyConsumption.SupportPower(Pod) * delta;
            }

            // E_support = SupportPower(Pod) × wall-clock time — always-on background power,
            //             configured by payload state. No task gate: idle/None/Rest
            //             all accrue. Wait/queueing/slow-start are strict subsets accumulated below.
            StatESupportJ += EnergyConsumption.SupportPower(Pod) * delta;

            // E_wait = SupportPower(Pod) × congestion-wait subset (task active AND stationary
            //          AND no mechanical action). Strict subset of E_support; loaded/empty split
            //          uses the matching rate so StatEWaitJ == StatEWaitLoadedJ + StatEWaitEmptyJ.
            if (hasActiveTask && !Moving && !_isRotatingThisTick && !inPickupOrSetdown && !inSlowStartHold)
            {
                StatEWaitJ += EnergyConsumption.SupportPower(Pod) * delta;
                if (Pod != null)
                    StatEWaitLoadedJ += EnergyConsumption.SUPPORT_POWER_LOADED * delta;
                else
                    StatEWaitEmptyJ += EnergyConsumption.SUPPORT_POWER_EMPTY * delta;
            }

            // Premature-arrival queueing energy [J] — bot is physically inside a station
            // queue zone but has NOT yet entered GetItems/PutItems service. This captures
            // the "arrived too early, waiting for station processing rhythm" waste that
            // starvation-aware OB+PS optimizations aim to reduce. Strict subset of E_support.
            bool inActiveStationService = StateQueueCount > 0 &&
                (StateQueuePeek().Type == BotStateType.GetItems || StateQueuePeek().Type == BotStateType.PutItems);
            if (hasSupportTask && inStationQueue && !inActiveStationService)
            {
                StatQueueingAtStationTimeSec += delta;
                StatEQueueingAtStationJ      += EnergyConsumption.SupportPower(Pod) * delta;
            }

            // Station arrival (edge-triggered): start of GetItems/PutItems station processing.
            _updateStationArrival();

            // Pref time denominators are now accumulated event-by-event in setNextWaypoint()
            // (StatMoveTimeLoadedSec, StatTurnTimeLoadedSec, etc.) — no delta-based tracking here.

            // Set moving flag bot
            if (XVelocity == 0.0 && YVelocity == 0.0)
                this.Moving = false;
            else
                this.Moving = true;

            // Set moving flag pod
            if (this.Pod != null)
                this.Pod.Moving = this.Moving;

            // Count distanceTraveled
            this.StatDistanceTraveled += Math.Sqrt((X - xOld) * (X - xOld) + (Y - yOld) * (Y - yOld));

            // Compute time in previous task
            this.StatTotalTaskTimes[StatLastTask] += delta;
            StatLastTask = CurrentTask != null ? CurrentTask.Type : BotTaskType.None;

            // Measure time spent in state
            StatTotalStateTimes[StatLastState] += delta;
            StatLastState = StateQueueCount > 0 ? StateQueuePeek().Type : BotStateType.Rest;

            // Compute time in previous state
            if (StateQueueCount > 0)
            {
                // TODO maybe add the time spent in the states in another stat dictionary
                //string s = _stateQueue.Peek().ToString();
                //if (this.StatTotalTimes.ContainsKey(s))
                //    this.StatTotalTimes[s] += delta;
                //else
                //    this.StatTotalTimes[s] = delta;
            }
        }

        #endregion

        #region IBotInfo Members

        /// <summary>
        /// x position of the goal for the info panel
        /// </summary>
        /// <returns>x position</returns>
        public override double GetInfoGoalX()
        {
            Waypoint destinationWP = DestinationWaypoint;
            if (destinationWP != null)
                return destinationWP.X;
            else
                return X;
        }
        /// <summary>
        /// y position of the goal for the info panel
        /// </summary>
        /// <returns>y position</returns>
        public override double GetInfoGoalY()
        {
            Waypoint destinationWP = DestinationWaypoint;
            if (destinationWP != null)
                return destinationWP.Y;
            else
                return Y;
        }
        /// <summary>
        /// target for the info panel
        /// </summary>
        /// <returns>orientation</returns>
        public override double GetInfoTargetOrientation() { return GetTargetOrientation(); }
        /// <summary>
        /// The current state the bot is in (for async access).
        /// </summary>
        public string _currentInfoStateName = "";
        /// <summary>
        /// state for the info panel
        /// </summary>
        /// <returns>state</returns>
        public override string GetInfoState() { return _currentInfoStateName; }
        /// <summary>
        /// Gets the current waypoint that is considered by planning.
        /// </summary>
        /// <returns>The current waypoint.</returns>
        public override IWaypointInfo GetInfoCurrentWaypoint() { return CurrentWaypoint; }
        /// <summary>
        /// Destination way point in the info panel
        /// </summary>
        /// <returns>destination</returns>
        public override IWaypointInfo GetInfoDestinationWaypoint() { return NextWaypoint; }
        /// <summary>
        /// Destination way point in the info panel
        /// </summary>
        /// <returns>destination</returns>
        public override IWaypointInfo GetInfoGoalWaypoint() { return DestinationWaypoint; }
        /// <summary>
        /// The current path the bot is following.
        /// </summary>
        private List<IWaypointInfo> _currentPath = new List<IWaypointInfo>();
        /// <summary>
        /// Gets the current path of the bot.
        /// </summary>
        /// <returns>The current path.</returns>
        public override List<IWaypointInfo> GetInfoPath() { return _currentPath; }
        /// <summary>
        /// Indicates whether the robot is currently blocked.
        /// </summary>
        /// <returns><code>true</code> if the robot is blocked, <code>false</code> otherwise.</returns>
        public override bool GetInfoBlocked() { return Instance.Controller.CurrentTime < _waitUntil || Instance.Controller.CurrentTime < BlockedUntil; }
        /// <summary>
        /// The time until the bot is blocked.
        /// </summary>
        /// <returns>The time until the bot is blocked.</returns>
        public override double GetInfoBlockedLeft()
        {
            double currentTime = Instance.Controller.CurrentTime; double waitUntil = _waitUntil; double blockedUntil = BlockedUntil;
            // Return next block release time that lies in the future
            return
                currentTime < waitUntil && currentTime < blockedUntil ? Math.Min(_waitUntil, blockedUntil) - currentTime :
                currentTime < waitUntil ? waitUntil - currentTime :
                currentTime < blockedUntil ? blockedUntil - currentTime :
                double.NaN;
        }
        /// <summary>
        /// Indicates whether the bot is currently queueing in a managed area.
        /// </summary>
        /// <returns><code>true</code> if the robot is within a queue area, <code>false</code> otherwise.</returns>
        public override bool GetInfoIsQueueing() { return IsQueueing; }

        #endregion

        #region States

        #region Move state

        /// <summary>
        /// The state defining the operation of moving.
        /// </summary>
        internal class BotMove : IBotState
        {
            /// <summary>
            /// next node to reach
            /// </summary>
            public Waypoint DestinationWaypoint { get; private set; }

            /// <summary>
            /// constructor
            /// </summary>
            /// <param name="w">way point</param>
            public BotMove(Waypoint w) { DestinationWaypoint = w; }

            /// <summary>
            /// Indicates whether we just entered the state.
            /// </summary>
            private bool _initialized;

            /// <summary>
            /// Logs an unfinished trip.
            /// </summary>
            /// <param name="bot">The bot that is logging the trip.</param>
            internal void LogUnfinishedTrip(BotNormal bot)
            {
                // Manage connectivity statistics
                if (bot.DestinationWaypoint != null)
                    bot.DestinationWaypoint.StatLogUnfinishedTrip(bot);
            }

            /// <summary>
            /// act
            /// </summary>
            /// <param name="self">driver</param>
            /// <param name="lastTime">The time before this update.</param>
            /// <param name="currentTime">The current time.</param>
            public void Act(Bot self, double lastTime, double currentTime)
            {
                var bot = self as BotNormal;

                // If it's the first time executing this, log the start time of the trip
                if (!_initialized)
                {
                    bot.Path = new Path();
                    // Track path statistics
                    self.StatLastTripStartTime = currentTime;
                    self.StatTotalStateCounts[Type]++;
                    self.StatDistanceRequestedOptimal += self.Pod != null ?
                        Distances.CalculateShortestPathPodSafe(bot.CurrentWaypoint, DestinationWaypoint, bot.Instance) :
                        Distances.CalculateShortestPath(bot.CurrentWaypoint, DestinationWaypoint, bot.Instance);
                    // Track last mile statistics
                    if (DestinationWaypoint.OutputStation != null)
                    {
                        if (!bot.IsInStationQueueZone(DestinationWaypoint.OutputStation))
                        {
                            // Start the trip now
                            bot._queueTripStartTime = bot.Instance.Controller.CurrentTime;
                            bot.Instance.NotifySlowStartStationTripStart(bot.CurrentTask as ExtractTask, bot, bot._queueTripStartTime);
                            // ── JIT validation hook: compute expected ideal travel duration
                            //    from current pos to the rearmost queue waypoint. End-condition
                            //    is the same _updateStationArrival check that closes the trip below.
                            // Use the ACTUAL BotMove destination (station.Waypoint) as the reference point
                            // for both expected and actual measurements. This eliminates the bbox-vs-waypoint
                            // ambiguity introduced by IsInStationQueueZone (which can trigger before reaching
                            // queue.Last() and depends on the approach angle).
                            var os = DestinationWaypoint.OutputStation;
                            var stationDest = os.Waypoint;
                            // Use TIME-optimal A* (turn-aware) — matches WHCA*n's path choice in no-conflict case.
                            var path = bot.Instance.MetaInfoManager.TimeEfficientPathManager
                                .GetShortestPathNodes(bot.CurrentWaypoint, stationDest, bot.Instance, emulatePodCarrying: true);
                            var dest = bot.ResolveFirstQueueWaypointOnPath(os, path) ??
                                Control.JIT.JITArrivalETA.ResolveQueueRearWaypoint(os);
                            int checkpointIndex = path != null ? path.IndexOf(dest) : -1;
                            if (checkpointIndex > 0)
                            {
                                bot._lastTripExpectedDurationSec = Control.JIT.IdealTravelTime.ComputeToCheckpoint(
                                    path, checkpointIndex, bot.Physics, bot.Instance.StraightOrientationTolerance,
                                    initialOrientation: bot.Orientation);
                            }
                            else
                            {
                                path = bot.Instance.MetaInfoManager.TimeEfficientPathManager
                                    .GetShortestPathNodes(bot.CurrentWaypoint, dest, bot.Instance, emulatePodCarrying: true);
                                checkpointIndex = path != null ? path.Count - 1 : -1;
                                bot._lastTripExpectedDurationSec = Control.JIT.IdealTravelTime.Compute(
                                    path, bot.Physics, bot.Instance.StraightOrientationTolerance,
                                    initialOrientation: bot.Orientation);
                            }
                            // Trip start time is recorded at BotMove init. The bot then waits for the
                            // path planner (WHCA*n) to produce a plan; this latency = PathPlanningConfig.Clocking
                            // (one planner cycle). Until then bot doesn't move. Account for this by
                            // shifting the JIT start reference forward by Clocking. This isolates the pure
                            // kinematic time so per-segment formula matches actual within numerical precision.
                            bot._lastTripStartTimeForJIT = bot.Instance.Controller.CurrentTime
                                + bot.Instance.ControllerConfig.PathPlanningConfig.Clocking;
                            bot._lastTripJITDestination = dest;
                            bot._lastTripJITTripId = ++bot._jitTripSequence;
                            bot._lastTripJITStopGoCount = 0;
                            bot._lastTripJITQueueStopGoCount = 0;
                            bot._lastTripJITWaitSec = 0.0;
                            bot._lastTripJITTaskId = bot.CurrentTask != null
                                ? bot.CurrentTask.GetHashCode().ToString(IOConstants.FORMATTER)
                                : "";
                            bot._lastTripInitialOrientation = bot.Orientation;
                            // Start fresh actual-path trace; seed with current waypoint as start.
                            bot._lastTripActualPath = new List<Waypoints.Waypoint> { bot.CurrentWaypoint };
                            bot._lastTripActualArrivals = new List<double> { bot.Instance.Controller.CurrentTime };
                            // Diagnostic
                            bot._lastTripPathNodeCount = checkpointIndex >= 0 ? checkpointIndex + 1 : (path?.Count ?? 0);
                            double pathLen = 0.0;
                            if (path != null)
                                for (int pi = 0; pi < Math.Min(path.Count - 1, checkpointIndex); pi++)
                                    pathLen += path[pi].GetDistance(path[pi + 1]);
                            bot._lastTripPathLength = pathLen;
                            bot._lastTripSourceId = bot.CurrentWaypoint?.ID ?? -1;
                            bot._lastTripDestId = dest?.ID ?? -1;
                        }
                        else
                        {
                            // Already at the location - no trip to do
                            bot._queueTripStartTime = double.NaN;
                            bot._lastTripExpectedDurationSec = double.NaN;
                        }
                    }
                    else if (DestinationWaypoint.InputStation != null)
                    {
                        if (!bot.IsInStationQueueZone(DestinationWaypoint.InputStation))
                            // Start the trip now
                            bot._queueTripStartTime = bot.Instance.Controller.CurrentTime;
                        else
                            // Already at the location - no trip to do
                            bot._queueTripStartTime = double.NaN;
                    }
                    else
                    {
                        // No station trip - do not track
                        bot._queueTripStartTime = double.NaN;
                    }

                    // Mark initialized
                    _initialized = true;
                    // Activate next pending trip (empty/loaded flag) so wait accumulator targets
                    // the correct list. Only activates if no trip is currently open.
                    bot.ActivateNextTripIfPending(currentTime);
                }

                //not while driving
                if (bot.GetSpeed() > 0)
                    return;

                //set destination way point
                bot.DestinationWaypoint = DestinationWaypoint;

                //we are at the destination && RealWorldIntegrationEventDriven
                if (bot.CurrentWaypoint == bot.DestinationWaypoint)
                {
                    //#RealWorldIntegration.start
                    if (bot.Instance.SettingConfig.RealWorldIntegrationEventDriven)
                    {
                        lock (bot)
                        {
                            if (!bot._eventReachedNextWaypoint)
                            {
                                // Logging info message
                                bot.Instance.LogInfo("Setting Bot" + bot.ID + " to sleep");

                                bot.BlockedUntil = bot._waitUntil = double.PositiveInfinity;
                                return;
                            }
                        }
                    }
                    //#RealWorldIntegration.end

                    // Manage connectivity statistics
                    if (bot.DestinationWaypoint != null)
                        bot.CurrentWaypoint.StatReachedDestination(bot);

                    // Remove this task
                    bot.NextWaypoint = null;
                    bot.DestinationWaypoint = null;
                    bot.DequeueState(lastTime, currentTime);
                    return;
                }

                // Only mark the move state if we weren't already at the destination waypoint
                bot._lastExteriorState = Type;

                //the bot has already something to do
                if (bot.NextWaypoint != null)
                    return;

                //Has the bot a path?
                if (bot.Path == null || bot.Path.Count == 0)
                {
                    bot.RequestReoptimization = true;
                    return;
                }

                //#RealWorldIntegration.start
                if (bot.Instance.SettingConfig.RealWorldIntegrationEventDriven)
                {
                    lock (bot)
                    {
                        if (bot.Instance.Controller.PathManager.GetWaypointByNodeId(bot.Path.NextAction.Node) == bot.CurrentWaypoint && !bot._eventReachedNextWaypoint)
                        {
                            // Logging info message
                            bot.Instance.LogInfo("Setting Bot" + bot.ID + " to sleep");

                            bot.BlockedUntil = bot._waitUntil = double.PositiveInfinity;
                            return;
                        }
                    }
                }
                //#RealWorldIntegration.end

                //Bot reached the next way point?
                if (bot.Instance.Controller.PathManager.GetWaypointByNodeId(bot.Path.NextAction.Node) != bot.CurrentWaypoint)
                {
                    // --> Not reached yet
                    bool successfulRegistration = bot.setNextWaypoint(bot.Instance.Controller.PathManager.GetWaypointByNodeId(bot.Path.NextAction.Node), currentTime);
                    if (successfulRegistration)
                        bot._eventReachedNextWaypoint = false;
                    else
                        bot._waitUntil = currentTime + 1;
                }
                else
                {
                    // --> Bot reached the next way point
                    // See whether turning to prepare for next move is necessary
                    if (bot.Path.NextAction == bot.Path.LastAction && bot.Path.NextNodeToPrepareFor >= 0)
                    {
                        // Calculate turn times
                        bot._startOrientation = bot.Orientation;
                        Waypoint turnTowards = bot.Instance.Controller.PathManager.GetWaypointByNodeId(bot.Path.NextNodeToPrepareFor);
                        bot._endOrientation = GetOrientation(bot.X, bot.Y, turnTowards.X, turnTowards.Y);
                        double rotateDuration = bot.Physics.getTimeNeededToTurn(bot._startOrientation, bot._endOrientation);
                        // Forget about the node
                        bot.Path.NextNodeToPrepareFor = -1;
                        // See whether we need to turn at all
                        if (rotateDuration > 0)
                        {
                            // Proceed with turn then come back here to get rid of the action
                            bot._waitUntil = Math.Max(bot._waitUntil, currentTime);
                            bot._rotateDuration = rotateDuration;
                            return;
                        }
                    }

                    // Set wait until time
                    if (bot.Path.NextAction.StopAtNode && bot.Path.NextAction.WaitTimeAfterStop > 0)
                    {
                        bot._waitUntil = currentTime + bot.Path.NextAction.WaitTimeAfterStop;
                        bot.MarkPlannedWaitStopGoIfForced(bot.Path.NextAction.WaitTimeAfterStop);
                    }

                    //pop the node
                    bot.Path.RemoveFirstAction();

                    //skip all non-stopping nodes
                    while (bot.Path.Count > 0 && bot.Path.NextAction.StopAtNode == false)
                        bot.Path.RemoveFirstAction();

                    if (bot.Path == null || bot.Path.Count == 0)
                    {
                        if (bot._waitUntil <= currentTime)
                            bot.RequestReoptimization = true;
                        return;
                    }

                    //set next destination
                    bool successfulRegistration = bot.setNextWaypoint(bot.Instance.Controller.PathManager.GetWaypointByNodeId(bot.Path.NextAction.Node), currentTime);
                    if (successfulRegistration)
                        bot._eventReachedNextWaypoint = false;
                    else
                        bot._waitUntil = currentTime + 1;
                }
            }

            /// <summary>
            /// Notifies the move state, that a collision occurred.
            /// </summary>
            /// <param name="bot">The bot.</param>
            /// <param name="currentTime">The current simulation time.</param>
            internal void NotifyCollision(BotNormal bot, double currentTime)
            {
                if (bot.Path == null)
                    bot.Path = new Path();

                //drive back to passed way point
                bot.setNextWaypoint(bot.CurrentWaypoint, currentTime);

                bot.Path.AddFirst(bot.Instance.Controller.PathManager.GetNodeIdByWaypoint(bot.CurrentWaypoint), true, 0);
            }

            /// <summary>
            /// Returns the current way point this bot wants to drive to
            /// </summary>
            public Waypoint getDestinationWaypoint()
            {
                return DestinationWaypoint;
            }

            /// <summary>
            /// state name
            /// </summary>
            /// <returns>name</returns>
            public override string ToString() { return "Move"; }

            /// <summary>
            /// State type.
            /// </summary>
            public BotStateType Type { get { return BotStateType.Move; } }

        }
        #endregion

        #region Pickup and Set down states

        /// <summary>
        /// The state defining the operation of picking up a pod at the current location.
        /// </summary>
        internal class BotPickupPod : IBotState
        {
            private Pod _pod;
            private Waypoint _waypoint;
            private bool _initialized = false;
            private bool _executed = false;
            public BotPickupPod(Pod b) { _pod = b; _waypoint = _pod.Waypoint; }
            public Waypoint DestinationWaypoint { get { return _waypoint; } }
            public void Act(Bot self, double lastTime, double currentTime)
            {
                var bot = self as BotNormal;

                // Remember the last state we were in
                bot._lastExteriorState = Type;

                // Initialize
                if (!_initialized) { self.StatTotalStateCounts[Type]++; _initialized = true; (self as BotNormal)?.CloseCurrentTrip(currentTime); }

                // Dequeue the state as soon as it is finished
                if (_executed)
                {
                    bot.DequeueState(lastTime, currentTime);
                    return;
                }
                // Act based on whether pod was picked up
                if (bot.PickupPod(_pod, currentTime))
                {
                    _executed = true;
                    bot.WaitUntil(bot.BlockedUntil);
                    bot.Instance.WaypointGraph.PodPickup(_pod);
                    bot.Instance.Controller.BotManager.PodPickedUp(bot, _pod, _waypoint);

                    // ── Energy Hook: E5a (lift pod) ──
                    double mL = EnergyConsumption.GetLoadMass(bot.Pod);
                    double e5a = EnergyConsumption.E5a_LiftPod(mL, bot.PodTransferTime);
                    bot.StatEnergyE5LiftLowerJ += e5a;
                    bot.StatEnergyTotalJ += e5a;
                    bot.StatPickupCount++;

                    //#RealWorldIntegraton.Start
                    //Trigger comes from outside => stay blocked
                    if (bot.Instance.SettingConfig.RealWorldIntegrationEventDriven)
                        bot.BlockedUntil = bot._waitUntil = double.PositiveInfinity;
                    //#RealWorldIntegraton.End
                }
                else
                {
                    // Failed to pick up pod
                    bot.StateQueueClear();
                    bot.Instance.Controller.BotManager.TaskAborted(bot, bot.CurrentTask);
                }
            }

            /// <summary>
            /// state name
            /// </summary>
            /// <returns>name</returns>
            public override string ToString() { return "PickupPod"; }

            /// <summary>
            /// State type.
            /// </summary>
            public BotStateType Type { get { return BotStateType.PickupPod; } }
        }

        /// <summary>
        /// The state defining the operation of setting down a pod at the current location.
        /// </summary>
        internal class BotSetdownPod : IBotState
        {
            private Waypoint _waypoint;
            private bool _initialized = false;
            private bool _executed = false;
            public BotSetdownPod(Waypoint w) { _waypoint = w; }
            public Waypoint DestinationWaypoint { get { return _waypoint; } }
            public void Act(Bot self, double lastTime, double currentTime)
            {
                var bot = self as BotNormal;

                // Remember the last state we were in
                bot._lastExteriorState = Type;

                // Initialize
                if (!_initialized) { self.StatTotalStateCounts[Type]++; _initialized = true; (self as BotNormal)?.CloseCurrentTrip(currentTime); }

                // Dequeue the state as soon as it is finished
                if (_executed)
                {
                    bot.DequeueState(lastTime, currentTime);
                    return;
                }

                //remember Pod
                Pod pod = bot.Pod;

                // ── Energy Hook: E5b (lower pod) — capture m_L before SetdownPod nulls bot.Pod ──
                double mLBeforeSetdown = EnergyConsumption.GetLoadMass(pod);

                // Act based on whether pod was set down
                if (bot.SetdownPod(currentTime))
                {
                    _executed = true;
                    bot.WaitUntil(bot.BlockedUntil);
                    bot.Instance.WaypointGraph.PodSetdown(pod, _waypoint);
                    bot.Instance.Controller.BotManager.PodSetDown(bot, pod, _waypoint);

                    // ── Energy Hook: E5b (lower pod) ──
                    double e5b = EnergyConsumption.E5b_LowerPod(mLBeforeSetdown, bot.PodTransferTime);
                    bot.StatEnergyE5LiftLowerJ += e5b;
                    bot.StatEnergyTotalJ += e5b;
                    bot.StatSetdownCount++;

                    //#RealWorldIntegraton.Start
                    //Trigger comes from outside => stay blocked
                    if (bot.Instance.SettingConfig.RealWorldIntegrationEventDriven)
                        bot.BlockedUntil = bot._waitUntil = double.PositiveInfinity;
                    //#RealWorldIntegraton.End
                }
                else
                {
                    // Failed to set down pod
                    bot.StateQueueClear();
                    bot.Instance.Controller.BotManager.TaskAborted(bot, bot.CurrentTask);
                }
            }

            /// <summary>
            /// state name
            /// </summary>
            /// <returns>name</returns>
            public override string ToString() { return "SetdownPod"; }

            /// <summary>
            /// State type.
            /// </summary>
            public BotStateType Type { get { return BotStateType.SetdownPod; } }
        }

        #endregion

        #region Get and Put states

        /// <summary>
        /// The state defining the operation of storing an item-bundle in the pod at an input-station.
        /// </summary>
        internal class BotGetItems : IBotState
        {
            private InsertTask _storeTask;
            private Waypoint _waypoint;
            private bool _initialized = false;
            private bool alreadyRequested = false;
            public BotGetItems(InsertTask storeTask) { _storeTask = storeTask; _waypoint = _storeTask.InputStation.Waypoint; }
            public Waypoint DestinationWaypoint { get { return _waypoint; } }
            public void Act(Bot self, double lastTime, double currentTime)
            {
                var bot = self as BotNormal;

                // Initialize
                if (!_initialized)
                {
                    self.StatTotalStateCounts[Type]++;
                    _initialized = true;
                    bot.CloseCurrentTrip(currentTime);
                    // Per-pod input-station queue wait: from queue-zone arrival to storing start.
                    if (!double.IsNaN(bot._potInputQueueArrivalTime))
                    {
                        bot.Instance.StatInputPodQueueWaitSamples.Add(currentTime - bot._potInputQueueArrivalTime);
                        bot._potInputQueueArrivalTime = double.NaN;
                    }
                }

                //#RealWorldIntegration.start
                if (bot.Instance.SettingConfig.RealWorldIntegrationCommandOutput && bot._lastExteriorState != Type)
                {
                    // Log the pickup command
                    var sb = new StringBuilder();
                    sb.Append("#RealWorldIntegration => Bot ").Append(bot.ID).Append(" Get");
                    bot.Instance.SettingConfig.LogAction(sb.ToString());
                    // Issue the pickup command
                    bot.Instance.RemoteController.RobotSubmitGetItemCommand(bot.ID);
                }
                //#RealWorldIntegration.end

                // If this is the first put action at a station, register - we need to notify it
                if (bot._lastExteriorState != Type)
                    _storeTask.InputStation.RegisterBot(bot);

                // Remember the last state we were in
                bot._lastExteriorState = Type;

                // If it's the first time, request the bundles
                if (!alreadyRequested)
                {
                    _storeTask.InputStation.RequestBundle(bot, _storeTask.Requests.First());
                    alreadyRequested = true;
                }

                if (bot.Pod == null)
                {
                    // Something wrong happened... don't have a pod!
                    bot.Instance.Controller.BotManager.TaskAborted(bot, bot.CurrentTask);
                    bot.DequeueState(lastTime, currentTime);
                    return;
                }

                // See if bundle has been deposited in the pod
                switch (_storeTask.Requests.First().State)
                {
                    case Management.RequestState.Unfinished: /* Ignore */ break;
                    case Management.RequestState.Aborted: // Request was aborted for some reason - give it back to the manager for re-insertion
                        {
                            // Remove the request that was just aborted
                            _storeTask.FirstAborted();
                            // See whether there are more bundles to store
                            if (_storeTask.Requests.Any())
                            {
                                // Store another one
                                alreadyRequested = false;
                            }
                            else
                            {
                                // We are done here
                                bot.DequeueState(lastTime, currentTime);
                                return;
                            }
                        }
                        break;
                    case Management.RequestState.Finished: // Request was finished - we can go on
                        {
                            // Remove the request that was just completed
                            _storeTask.FirstStored();
                            // See whether there are more bundles to store
                            if (_storeTask.Requests.Any())
                            {
                                // Store another one
                                alreadyRequested = false;
                            }
                            else
                            {
                                // We are done here
                                bot.DequeueState(lastTime, currentTime);
                                return;
                            }
                        }
                        break;
                    default: throw new ArgumentException("Unknown request state: " + _storeTask.Requests.First().State);
                }
            }

            /// <summary>
            /// state name
            /// </summary>
            /// <returns>name</returns>
            public override string ToString() { return "GetItems"; }

            /// <summary>
            /// State type.
            /// </summary>
            public BotStateType Type { get { return BotStateType.GetItems; } }
        }

        /// <summary>
        /// The state defining the operation of picking an item from the pod at an output-station.
        /// </summary>
        internal class BotPutItems : IBotState
        {
            ExtractTask _extractTask;
            Waypoint _waypoint;
            private bool _initialized = false;
            bool alreadyRequested = false;
            public BotPutItems(ExtractTask extractTask)
            { _extractTask = extractTask; _waypoint = extractTask.OutputStation.Waypoint; }
            public Waypoint DestinationWaypoint { get { return _waypoint; } }
            public void Act(Bot self, double lastTime, double currentTime)
            {
                var bot = self as BotNormal;

                // Initialize
                if (!_initialized)
                {
                    self.StatTotalStateCounts[Type]++;
                    _initialized = true;
                    bot.CloseCurrentTrip(currentTime);
                    bot.Instance.NotifySlowStartActualArrival(_extractTask, bot, currentTime);
                    // Per-pod: picking begins NOW. Compute queue-wait (queue-zone arrival → picking start).
                    if (!double.IsNaN(bot._potQueueArrivalTime))
                    {
                        double queueWait = currentTime - bot._potQueueArrivalTime;
                        bot.Instance.StatPodQueueWaitSamples.Add(queueWait);
                        bot._potQueueArrivalTime = double.NaN;
                    }
                    bot._potPickingStartTime = currentTime;
                }

                //#RealWorldIntegration.start
                if (bot.Instance.SettingConfig.RealWorldIntegrationCommandOutput && bot._lastExteriorState != Type)
                {
                    // Log the pickup command
                    var sb = new StringBuilder();
                    sb.Append("#RealWorldIntegration => Bot ").Append(bot.ID).Append(" Put");
                    bot.Instance.SettingConfig.LogAction(sb.ToString());
                    // Issue the pickup command
                    bot.Instance.RemoteController.RobotSubmitPutItemCommand(bot.ID);
                }
                //#RealWorldIntegration.end

                // If this is the first put action at a station, register - we need to notify it
                if (bot._lastExteriorState != Type)
                    _extractTask.OutputStation.RegisterBot(bot);

                // Remember the last state we were in
                bot._lastExteriorState = Type;

                // If it's the first time, request the items be taken
                if (!alreadyRequested)
                {
                    _extractTask.OutputStation.RequestItemTake(bot, _extractTask.Requests.First());
                    alreadyRequested = true;
                }

                if (bot.Pod == null)
                {
                    // Something wrong happened... don't have a pod!
                    bot.Instance.Controller.BotManager.TaskAborted(bot, bot.CurrentTask);
                    bot.StateQueueClear();
                    return;
                }

                // See if item has been picked from the pod
                switch (_extractTask.Requests.First().State)
                {
                    case Management.RequestState.Unfinished: /* Ignore */ break;
                    case Management.RequestState.Aborted: // Request was aborted for some reason - give it back to the manager for re-insertion
                        {
                            // Remove the request that was just aborted
                            _extractTask.FirstAborted();
                            // See whether there are more items to pick
                            if (_extractTask.Requests.Any())
                            {
                                // Pick another one
                                alreadyRequested = false;
                            }
                            else
                            {
                                // We are done here — record picking time for this pod visit.
                                if (!double.IsNaN(bot._potPickingStartTime))
                                {
                                    bot.Instance.NotifySlowStartProcessingFinished(_extractTask, bot, currentTime);
                                    bot.Instance.StatPodPickingTimeSamples.Add(currentTime - bot._potPickingStartTime);
                                    bot._potPickingStartTime = double.NaN;
                                }
                                // Pure-observation: snapshot backfill potential at the pod-release moment.
                                Control.BackfillProbe.OnExtractRelease(bot, _extractTask, currentTime);
                                bot.DequeueState(lastTime, currentTime);
                                return;
                            }
                        }
                        break;
                    case Management.RequestState.Finished: // Request was finished - we can go on
                        {
                            // Remove the request that was just completed
                            _extractTask.FirstPicked();
                            // See whether there are more items to pick
                            if (_extractTask.Requests.Any())
                            {
                                // Pick another one
                                alreadyRequested = false;
                            }
                            else
                            {
                                // We are done here — record picking time for this pod visit.
                                if (!double.IsNaN(bot._potPickingStartTime))
                                {
                                    bot.Instance.NotifySlowStartProcessingFinished(_extractTask, bot, currentTime);
                                    bot.Instance.StatPodPickingTimeSamples.Add(currentTime - bot._potPickingStartTime);
                                    bot._potPickingStartTime = double.NaN;
                                }
                                // Pure-observation: snapshot backfill potential at the pod-release moment.
                                Control.BackfillProbe.OnExtractRelease(bot, _extractTask, currentTime);
                                bot.DequeueState(lastTime, currentTime);
                                return;
                            }
                        }
                        break;
                    default: throw new ArgumentException("Unknown request state: " + _extractTask.Requests.First().State);
                }
            }

            /// <summary>
            /// state name
            /// </summary>
            /// <returns>name</returns>
            public override string ToString() { return "PutItems"; }

            /// <summary>
            /// State type.
            /// </summary>
            public BotStateType Type { get { return BotStateType.PutItems; } }
        }

        #endregion

        #region Use elevator state

        /// <summary>
        /// State: Bot uses an elevator to get to a different tier
        /// </summary>
        internal class UseElevator : IBotState
        {
            private Elevator _elevator;
            private Waypoint _waypointFrom;
            private Waypoint _waypointTo;
            private bool _initialized = false;
            private bool inUse;
            private double travelUntil;
            public Waypoint DestinationWaypoint { get { return _waypointTo; } }
            public UseElevator(Elevator elevator, Waypoint waypointFrom, Waypoint waypointTo) { _elevator = elevator; _waypointFrom = waypointFrom; _waypointTo = waypointTo; inUse = false; }
            public void Act(Bot self, double lastTime, double currentTime)
            {
                var bot = self as BotNormal;

                // Initialize
                if (!_initialized) { self.StatTotalStateCounts[Type]++; _initialized = true; }

                // Remember the last state we were in
                bot._lastExteriorState = Type;

                // Check if i already using the elevator
                if (!inUse)
                {
                    //consistency
                    if (!_elevator.ConnectedPoints.Contains(_waypointFrom) || !_elevator.ConnectedPoints.Contains(_waypointTo))
                        throw new NotSupportedException("Way point is not managed by Elevator!");

                    inUse = true;
                    travelUntil = currentTime + _elevator.GetTiming(_waypointFrom, _waypointTo);
                    bot._waitUntil = travelUntil;
                }


                if (currentTime >= travelUntil)
                {
                    //do the transportation
                    _elevator.Transport(bot, _waypointFrom, _waypointTo);
                    bot.CurrentWaypoint = _waypointTo;
                    bot.DequeueState(lastTime, currentTime);
                    return;
                }

            }

            /// <summary>
            /// state name
            /// </summary>
            /// <returns>name</returns>
            public override string ToString() { return "UseElevator"; }

            /// <summary>
            /// State type.
            /// </summary>
            public BotStateType Type { get { return BotStateType.UseElevator; } }
        }
        #endregion

        #region SlowStartHold state

        /// <summary>
        /// PP-aware slow-start hold: keep bot stationary at the pod cell after lift
        /// for a duration computed by SlowStartController, before starting the
        /// pod→station traversal. The decision is made once on first entry to this
        /// state; the bot does not re-evaluate during the hold.
        /// </summary>
        internal class BotSlowStartHold : IBotState
        {
            // Per-tick release tolerance: if probe ETA + currentTime is within this
            // seconds of the remaining station-busy window, release early.
            private const double RELEASE_TOLERANCE_SEC = 1.0;
            // Wake-up interval during hold (event-driven scheduler) — bounds probe cost
            // (~1 A* per interval per hold) and clearance-detection latency.
            private const double PROBE_INTERVAL = 1.0;

            private ExtractTask _task;
            private bool _initialized = false;
            private bool _holdFinished = false;
            private double _holdStartTime = 0.0;       // when hold began
            private Waypoint _claimedStorage = null;   // pod cell re-claimed during hold (null if no claim)
            private SlowStartDecisionTrace _trace = null;
            private struct FutureEtaProbe
            {
                public double BestEta;
                public double BestDelay;
                public bool HasFeasibleFuture;
            }
            public BotSlowStartHold(ExtractTask task) { _task = task; }
            public Waypoint DestinationWaypoint { get { return _task != null && _task.ReservedPod != null ? _task.ReservedPod.Waypoint : null; } }

            public void Act(Bot self, double lastTime, double currentTime)
            {
                var bot = self as BotNormal;
                bot._lastExteriorState = Type;

                // ── First-tick setup ──
                if (!_initialized)
                {
                    self.StatTotalStateCounts[Type]++;
                    _initialized = true;
                    _holdStartTime = currentTime;

                    // Mark holding state IMMEDIATELY so other bots' ComputeStarvation
                    // calls in this same tick can sequence us via FIFO (_slowStartHoldStartTime).
                    bot._isSlowStartHolding = true;
                    bot._slowStartHoldStartTime = currentTime;

                    // Re-claim the pod storage location so no other task targets this cell
                    // for pod placement during hold (rev6 fix — cures loaded-wait inflation).
                    // Only meaningful for post-lift hold (pod has left the cell). For pre-lift
                    // hold the pod is still physically there; claim is a no-op or harmless.
                    var podWp = (bot.CurrentWaypoint != null && bot.CurrentWaypoint.PodStorageLocation
                                 ? bot.CurrentWaypoint : null);
                    if (podWp != null)
                    {
                        try
                        {
                            bot.Instance.ResourceManager.ClaimStorageLocation(podWp);
                            _claimedStorage = podWp;
                        }
                        catch (System.InvalidOperationException) { _claimedStorage = null; }
                    }
                }

                // ── Read centralized scheduler decision ──
                // StationReleaseScheduler (run each tick in PathManager.Update) writes
                // bot._slowStartReleaseDeadline. We hold until that deadline, then release.
                // First tick before the scheduler has run: deadline is NaN → keep probing.
                if (!_holdFinished)
                {
                    if (_trace == null)
                    {
                        bot.StatSlowStartDecisionCount++;
                        _trace = bot.Instance.NotifySlowStartDecision(
                            bot, _task, BuildDiag(bot), currentTime,
                            double.IsNaN(bot._slowStartReleaseDeadline) ? currentTime : bot._slowStartReleaseDeadline);
                    }

                    double deadline = bot._slowStartReleaseDeadline;
                    bool release = !double.IsNaN(deadline) && currentTime >= deadline;

                    if (release)
                    {
                        if (_task != null && !double.IsNaN(bot._slowStartEta) && !double.IsInfinity(bot._slowStartEta))
                        {
                            double liftTime = (bot.Pod == null) ? bot.PodTransferTime : 0.0;
                            _task.ExpectedArrivalAtStation = currentTime + liftTime + bot._slowStartEta;
                        }
                        bool everHeld = (currentTime - _holdStartTime) > 0.0;
                        if (!everHeld) bot.StatSlowStartImmediateReleaseCount++;
                        bot.Instance.NotifySlowStartRelease(
                            _trace, currentTime,
                            everHeld ? "hard_deadline" : "immediate_release",
                            bot._slowStartEta, bot._slowStartTStarve);
                        _holdFinished = true;
                    }
                    else
                    {
                        bot.BlockedUntil = double.IsNaN(deadline) ? currentTime + PROBE_INTERVAL : deadline;
                        bot.WaitUntil(currentTime + PROBE_INTERVAL);
                    }
                }

                if (_holdFinished)
                {
                    bot._isSlowStartHolding = false;
                    bot._slowStartHoldStartTime = double.NaN;
                    bot._slowStartReleaseDeadline = double.NaN;
                    bot._slowStartIsChosen = false;
                    bot._slowStartEta = double.NaN;
                    bot._slowStartTStarve = double.NaN;
                    bot.RequestReoptimization = true;
                    // Unblock so subsequent BotMove states can act.
                    bot.BlockedUntil = -1.0;
                    bot.WaitUntil(-1.0);

                    // Release the storage location we re-claimed at hold start. Bot is about
                    // to start moving toward the station — the cell becomes available for
                    // other tasks again.
                    if (_claimedStorage != null)
                    {
                        try { bot.Instance.ResourceManager.ReleaseStorageLocation(_claimedStorage); }
                        catch (System.InvalidOperationException) { /* already released somehow */ }
                        _claimedStorage = null;
                    }

                    bot.DequeueState(lastTime, currentTime);
                }
            }
            private SlowStartController.HoldDiagnostics BuildDiag(BotNormal bot)
            {
                return new SlowStartController.HoldDiagnostics
                {
                    Eta = bot._slowStartEta,
                    TStarve = bot._slowStartTStarve,
                    ReleaseBudget = bot._slowStartTStarve,
                    Delay = double.IsNaN(bot._slowStartReleaseDeadline) ? 0.0
                            : Math.Max(0.0, bot._slowStartReleaseDeadline - bot.Instance.Controller.CurrentTime),
                    EtaProbeFailed = double.IsNaN(bot._slowStartEta),
                    ImmediateRelease = bot._slowStartIsChosen && bot._slowStartReleaseDeadline <= bot.Instance.Controller.CurrentTime
                };
            }
            public override string ToString() { return "SlowStartHold"; }
            public BotStateType Type { get { return BotStateType.SlowStartHold; } }

            private double EstimateEtaForReleaseTrace(BotNormal bot, double currentTime)
            {
                var stationWp = _task != null && _task.OutputStation != null
                                ? _task.OutputStation.Waypoint : null;
                var pm = bot.Instance.Controller?.PathManager;
                if (pm == null || stationWp == null)
                    return double.NaN;
                return pm.EstimateReservationAwareEta(
                    bot, bot.CurrentWaypoint, stationWp,
                    currentTime, bot.GetTargetOrientation());
            }

            private bool ShouldReleaseNow(BotNormal bot, PathManager pm, Waypoint stationWp, double currentTime, double eta, double remainingTStarve, out FutureEtaProbe futureProbe)
            {
                futureProbe = new FutureEtaProbe { BestEta = double.NaN, BestDelay = double.NaN, HasFeasibleFuture = false };

                if (!UsesEtaImprovementPolicy(bot.Instance.SettingConfig.SlowStartReleasePolicy))
                    return eta <= remainingTStarve + RELEASE_TOLERANCE_SEC;

                double safetyBuffer = Math.Max(0.0, bot.Instance.SettingConfig.SlowStartEtaSafetyBuffer);
                double safeRemaining = remainingTStarve - safetyBuffer;
                if (eta > safeRemaining + RELEASE_TOLERANCE_SEC)
                    return true;

                int lookahead = Math.Max(0, bot.Instance.SettingConfig.SlowStartEtaImprovementLookaheadSec);
                if (lookahead <= 0)
                    return true;

                futureProbe = FindBestFeasibleFutureEta(bot, pm, stationWp, currentTime, remainingTStarve, safetyBuffer, lookahead);
                if (!futureProbe.HasFeasibleFuture)
                    return true;

                double margin = Math.Max(0.0, bot.Instance.SettingConfig.SlowStartEtaImprovementReleaseMargin);
                return eta <= futureProbe.BestEta + margin;
            }

            private FutureEtaProbe FindBestFeasibleFutureEta(BotNormal bot, PathManager pm, Waypoint stationWp, double currentTime, double remainingTStarve, double safetyBuffer, int lookahead)
            {
                FutureEtaProbe result = new FutureEtaProbe { BestEta = double.NaN, BestDelay = double.NaN, HasFeasibleFuture = false };
                for (int delay = 1; delay <= lookahead; delay++)
                {
                    double safeRemainingAfterDelay = remainingTStarve - delay - safetyBuffer;
                    if (safeRemainingAfterDelay + RELEASE_TOLERANCE_SEC <= 0.0)
                        break;

                    double futureEta = pm.EstimateReservationAwareEta(
                        bot, bot.CurrentWaypoint, stationWp,
                        currentTime + delay, bot.GetTargetOrientation());

                    if (double.IsNaN(futureEta) || double.IsInfinity(futureEta))
                        continue;
                    if (futureEta > safeRemainingAfterDelay + RELEASE_TOLERANCE_SEC)
                        continue;

                    if (!result.HasFeasibleFuture || futureEta < result.BestEta)
                    {
                        result.HasFeasibleFuture = true;
                        result.BestEta = futureEta;
                        result.BestDelay = delay;
                    }
                }
                return result;
            }

            private bool UsesEtaImprovementPolicy(SlowStartReleasePolicy policy)
            {
                return policy == SlowStartReleasePolicy.ReservationEtaImprovement ||
                       policy == SlowStartReleasePolicy.QueueBudgetEtaImprovement;
            }
        }

        /// <summary>
        /// Input-station (replenishment) slow-start hold. Mirrors BotSlowStartHold but for
        /// store/InsertTask, stripped of output-only telemetry. Keeps the bot stationary at the
        /// pod cell, reading the release deadline written each tick by InputStationReleaseScheduler,
        /// then releases. See docs/superpowers/plans/2026-05-30-input-station-slow-start.md.
        /// </summary>
        internal class BotSlowStartHoldInput : IBotState
        {
            private const double PROBE_INTERVAL = 1.0;
            private readonly InsertTask _task;
            private bool _initialized = false;
            private bool _holdFinished = false;
            private double _holdStartTime = 0.0;
            private Waypoint _claimedStorage = null;

            public BotSlowStartHoldInput(InsertTask task) { _task = task; }
            public Waypoint DestinationWaypoint
            {
                get { return _task != null && _task.ReservedPod != null ? _task.ReservedPod.Waypoint : null; }
            }

            public void Act(Bot self, double lastTime, double currentTime)
            {
                var bot = self as BotNormal;
                bot._lastExteriorState = Type;

                // First-tick setup: mark holding so the scheduler picks us up; re-claim pod cell.
                if (!_initialized)
                {
                    self.StatTotalStateCounts[Type]++;
                    _initialized = true;
                    _holdStartTime = currentTime;
                    bot._isSlowStartHolding = true;
                    bot._slowStartHoldStartTime = currentTime;

                    var podWp = (bot.CurrentWaypoint != null && bot.CurrentWaypoint.PodStorageLocation)
                                ? bot.CurrentWaypoint : null;
                    if (podWp != null)
                    {
                        try { bot.Instance.ResourceManager.ClaimStorageLocation(podWp); _claimedStorage = podWp; }
                        catch (System.InvalidOperationException) { _claimedStorage = null; }
                    }
                }

                // Read scheduler-written deadline; hold until then.
                if (!_holdFinished)
                {
                    double deadline = bot._slowStartReleaseDeadline;
                    bool release = !double.IsNaN(deadline) && currentTime >= deadline;
                    if (release)
                    {
                        _holdFinished = true;
                        // Release-event trace (gated): time, station, bot, pods inbound to station,
                        // holder's est/eta at release. Lets us detect synchronized releases at the
                        // same input station (two holders released within seconds of each other).
                        var inst = bot.Instance;
                        if (inst.SettingConfig != null && inst.SettingConfig.BackfillProbeEnabled
                            && _task != null && _task.InputStation != null)
                        {
                            inst.StatInputReleaseRows.Add(string.Join(";", new[]
                            {
                                currentTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                _task.InputStation.ID.ToString(),
                                bot.ID.ToString(),
                                _task.InputStation.GetInfoOpenBundles().ToString(),
                                (_task.Requests != null ? _task.Requests.Count : 0).ToString()
                            }));
                        }
                    }
                    else
                    {
                        bot.BlockedUntil = double.IsNaN(deadline) ? currentTime + PROBE_INTERVAL : deadline;
                        bot.WaitUntil(currentTime + PROBE_INTERVAL);
                    }
                }

                if (_holdFinished)
                {
                    bot._isSlowStartHolding = false;
                    bot._slowStartHoldStartTime = double.NaN;
                    bot._slowStartReleaseDeadline = double.NaN;
                    bot._slowStartIsChosen = false;
                    bot._slowStartEta = double.NaN;
                    bot._slowStartTStarve = double.NaN;
                    bot.RequestReoptimization = true;
                    bot.BlockedUntil = -1.0;
                    bot.WaitUntil(-1.0);

                    if (_claimedStorage != null)
                    {
                        try { bot.Instance.ResourceManager.ReleaseStorageLocation(_claimedStorage); }
                        catch (System.InvalidOperationException) { /* already released */ }
                        _claimedStorage = null;
                    }

                    bot.DequeueState(lastTime, currentTime);
                }
            }

            public override string ToString() { return "SlowStartHoldInput"; }
            public BotStateType Type { get { return BotStateType.SlowStartHold; } }
        }

        #endregion

        #region Rest state

        internal class BotRest : IBotState
        {
            // TODO make rest time randomized and parameterized
            public const double DEFAULT_REST_TIME = 5;

            private Waypoint _waypoint;
            private double _timeSpan;
            private bool _initialized = false;
            private bool alreadyRested = false;
            public BotRest(Waypoint waypoint, double timeSpan) { _waypoint = waypoint; _timeSpan = timeSpan; }
            public Waypoint DestinationWaypoint { get { return _waypoint; } }

            public void Act(Bot self, double lastTime, double currentTime)
            {
                var bot = self as BotNormal;

                // Initialize
                if (!_initialized) { self.StatTotalStateCounts[Type]++; _initialized = true; }

                //#RealWorldIntegration.start
                if (bot.Instance.SettingConfig.RealWorldIntegrationCommandOutput && bot._lastExteriorState != Type)
                {
                    // Log the pickup command
                    var sb = new StringBuilder();
                    sb.Append("#RealWorldIntegration => Bot ").Append(bot.ID).Append(" Rest");
                    bot.Instance.SettingConfig.LogAction(sb.ToString());
                    // Issue the pickup command
                    bot.Instance.RemoteController.RobotSubmitRestCommand(bot.ID);
                }
                //#RealWorldIntegration.end

                // Remember the last state we were in
                bot._lastExteriorState = Type;

                // Randomly rest or exit resting
                if (!alreadyRested)
                {
                    // Rest for a predefined period
                    bot.BlockedUntil = currentTime + _timeSpan;
                    bot.WaitUntil(bot.BlockedUntil);
                    alreadyRested = true;
                    return;
                }
                else
                {
                    // exit the resting
                    bot.DequeueState(lastTime, currentTime);
                }
            }

            /// <summary>
            /// state name
            /// </summary>
            /// <returns>name</returns>
            public override string ToString() { return "Rest"; }

            /// <summary>
            /// State type.
            /// </summary>
            public BotStateType Type { get { return BotStateType.Rest; } }

        }
        #endregion

        #endregion

        #region Events
        /// <summary>
        /// Called when [bot reached way point].
        /// </summary>
        /// <param name="waypoint">The way point.</param>
        /// <exception cref="System.NotImplementedException"></exception>
        public override void OnReachedWaypoint(Waypoint waypoint)
        {
            //not necessary 
            if (!Instance.SettingConfig.RealWorldIntegrationEventDriven)
                return;

            // Logging info message
            Instance.LogInfo("Bot" + this.ID + " is at: " + waypoint.ID);

            //we are not interested in intermediate points
            if (waypoint.ID == _nextWaypointID || !_initialEventReceived)
                lock (this)
                {
                    _nextWaypointID = -1;
                    _eventReachedNextWaypoint = true; _initialEventReceived = true;
                    XVelocity = YVelocity = BlockedUntil = _waitUntil = _rotateDuration = _driveDuration = 0;
                }
        }

        /// <summary>
        /// Called when [bot picked up the pod].
        /// </summary>
        /// <exception cref="System.NotImplementedException"></exception>
        public override void OnPickedUpPod()
        {
            //not necessary 
            if (!Instance.SettingConfig.RealWorldIntegrationEventDriven)
                return;

            //stop blocking
            BlockedUntil = _waitUntil = 0;
        }

        /// <summary>
        /// Called when [bot set down pod].
        /// </summary>
        /// <exception cref="System.NotImplementedException"></exception>
        public override void OnSetDownPod()
        {
            //not necessary 
            if (!Instance.SettingConfig.RealWorldIntegrationEventDriven)
                return;

            //stop blocking
            BlockedUntil = _waitUntil = 0;
        }
        #endregion
    }

}
