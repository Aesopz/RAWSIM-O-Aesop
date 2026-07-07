using RAWSimO.Core.Bots;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using RAWSimO.Core.IO;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.Core.Statistics;
using RAWSimO.Core.Waypoints;
using RAWSimO.Toolbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RAWSimO.Core.Metrics;
using static RAWSimO.Core.Statistics.StationTripDatapoint;

namespace RAWSimO.Core
{
    /// THIS PARTIAL CLASS CONTAINS ALL CORE ELEMENTS OF THE PERFORMANCE INDICATORS
    /// <summary>
    /// The core element of each simulation instance.
    /// </summary>
    public partial class Instance
    {
        #region Data fields

        /// <summary>
        /// Flag used by OrderManager to trigger a one-time distance table read.
        /// Set to true externally; reset to false after ReadDistance() completes.
        /// </summary>
        public bool ifreaddistance;

        /// <summary>
        /// Number of datapoints stored before a flush is done.
        /// </summary>
        public const int STAT_MAX_DATA_POINTS = 10000;

        /// <summary>
        /// The time the recording of the statistics started.
        /// </summary>
        internal double StatTimeStart = 0.0;
        /// <summary>
        /// The current time regarding all statistical measurements.
        /// </summary>
        internal double StatTime { get { return Controller.CurrentTime - StatTimeStart; } }

        /// <summary>
        /// Indicates whether the stat reset after the warmup period was done.
        /// </summary>
        public bool StatWarmupResetDone { get; private set; }
        /// <summary>
        /// Indicates whether the stats have been written.
        /// </summary>
        public bool StatResultsWritten { get; private set; }

        /// <summary>
        /// The number of times bundle generation was paused.
        /// </summary>
        public int StatBundleGenerationStops { get; private set; }
        /// <summary>
        /// The number of times order generation was paused.
        /// </summary>
        public int StatOrderGenerationStops { get; private set; }
        /// <summary>
        /// The number of handled items overall.
        /// </summary>
        public int StatOverallItemsHandled { get; private set; }
        /// <summary>
        /// The number of handled bundles overall.
        /// </summary>
        public int StatOverallBundlesHandled { get; private set; }
        /// <summary>
        /// The number of handled order lines overall.
        /// </summary>
        public int StatOverallLinesHandled { get; private set; }
        /// <summary>
        /// The number of handled orders overall.
        /// </summary>
        public int StatOverallOrdersHandled { get; private set; }
        /// <summary>
        /// The number of handled orders overall that were not completed in time.
        /// </summary>
        public int StatOverallOrdersLate { get; private set; }
        /// <summary>
        /// The absolute number of items ordered.
        /// </summary>
        public int StatOverallItemsOrdered { get; private set; }
        /// <summary>
        /// The absolute number of bundles placed.
        /// </summary>
        public int StatOverallBundlesPlaced { get; private set; }
        /// <summary>
        /// The absolute number of orders placed.
        /// </summary>
        public int StatOverallOrdersPlaced { get; private set; }
        /// <summary>
        /// The absolute number of bundles placed.
        /// </summary>
        public int StatOverallBundlesRejected { get; private set; }
        /// <summary>
        /// The absolute number of orders placed.
        /// </summary>
        public int StatOverallOrdersRejected { get; private set; }
        /// <summary>
        /// The total number of repositioning moves that were executed.
        /// </summary>
        public int StatRepositioningMoves { get; private set; }
        /// <summary>
        /// The total distance traveled by the bots so far.
        /// </summary>
        public double StatOverallDistanceTraveled { get { return Bots.Sum(b => b.StatDistanceTraveled); } }
        /// <summary>Total energy consumed by all bots [J] (Rizqi model).</summary>
        public double StatOverallEnergyTotalJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyTotalJ); } }
        /// <summary>Fleet acceleration energy E1 [J].</summary>
        public double StatOverallEnergyE1J { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE1AccelJ); } }
        /// <summary>Fleet deceleration energy E2 [J].</summary>
        public double StatOverallEnergyE2J { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE2DecelJ); } }
        /// <summary>Fleet cruise energy E3 [J].</summary>
        public double StatOverallEnergyE3J { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE3CruiseJ); } }
        /// <summary>Fleet rotation energy E4 [J].</summary>
        public double StatOverallEnergyE4J { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE4RotationJ); } }
        /// <summary>Fleet pod lift/lower energy E5 [J].</summary>
        public double StatOverallEnergyE5J { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE5LiftLowerJ); } }
        /// <summary>Fleet support energy [J] = Sum SupportPower(Pod) x wall-clock time (always-on; configured by payload state).</summary>
        public double StatOverallEnergySupportJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatESupportJ); } }
        /// <summary>Fleet total energy including support [J] = E_mech + Σ SupportPower(Pod)×time.</summary>
        public double StatOverallEnergyTotalWithSupportJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyTotalWithSupportJ); } }
        /// <summary>Total turning events across all bots.</summary>
        public int StatOverallTurningCount { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTurningCount); } }
        /// <summary>Total orders completed across all bots.</summary>
        public int StatOverallOrdersCompletedByBots { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatOrdersCompleted); } }
        /// <summary>Total distance traveled by bots (Rizqi tracker) [m].</summary>
        public double StatOverallDistanceTraveledRizqi { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatDistanceTraveledM); } }
        /// <summary>Total distance traveled while carrying a pod [m].</summary>
        public double StatOverallLoadedDistanceM { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatLoadedDistanceM); } }
        /// <summary>Total turning events while carrying a pod.</summary>
        public int StatOverallLoadedTurningCount { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatLoadedTurningCount); } }
        /// <summary>Total turning events while empty.</summary>
        public int StatOverallEmptyTurningCount { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEmptyTurningCount); } }
        /// <summary>Total wait time across all bots (stationary, not rotating) [s].</summary>
        public double StatOverallWaitTimeSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatWaitTimeSec); } }
        /// <summary>Fleet E_support (always-on SupportPower(Pod) x wall-clock time; configured by payload state; includes moving) [J].</summary>
        public double StatOverallESupportJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatESupportJ); } }
        /// <summary>Fleet E_wait (SupportPower(Pod) × congestion-wait subset; strict subset of E_support) [J].</summary>
        public double StatOverallEWaitJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEWaitJ); } }
        /// <summary>Fleet E_wait while loaded [J].</summary>
        public double StatOverallEWaitLoadedJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEWaitLoadedJ); } }
        /// <summary>Fleet E_wait while empty [J].</summary>
        public double StatOverallEWaitEmptyJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEWaitEmptyJ); } }
        /// <summary>Fleet premature-arrival queue time [s] — bot inside station queue zone, not yet in GetItems/PutItems service. KPI for starvation-aware OB+PS evaluation.</summary>
        public double StatOverallQueueingAtStationTimeSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatQueueingAtStationTimeSec); } }
        /// <summary>Fleet premature-arrival queueing energy [J] = SupportPower(Pod) × StatOverallQueueingAtStationTimeSec. Strict subset of E_support.</summary>
        public double StatOverallEQueueingAtStationJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEQueueingAtStationJ); } }
        /// <summary>Fleet WHCA*/reservation-table planned-wait stop-and-go event count (queue-manager creep counted separately).</summary>
        public int StatOverallStopAndGoCount { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatStopAndGoCount); } }
        /// <summary>Fleet planned-wait stop-and-go energy [J] = E2 into wait + E1 out of wait; not additive to total.</summary>
        public double StatOverallStopAndGoEnergyJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatStopAndGoEnergyJ); } }
        /// <summary>Fleet station-queue creep stop-and-go event count caused by QueueManager advancing stopped bots.</summary>
        public int StatOverallQueueStopAndGoCount { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatQueueStopAndGoCount); } }
        /// <summary>Fleet station-queue creep stop-and-go energy [J] = E2 into queue stop + E1 out of queue stop; not additive to total.</summary>
        public double StatOverallQueueStopAndGoEnergyJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatQueueStopAndGoEnergyJ); } }
        /// <summary>Event-level stop-and-go rows for visualization; written to stop_and_go_events.csv.</summary>
        public List<string> StatStopGoEventRows = new List<string>();
        /// <summary>WHCA*/reservation-table planned-wait points in native RAWSim-O heatmap format; written to conflictwait.heat.</summary>
        public List<LocationDatapoint> StatConflictWaitHeatPoints = new List<LocationDatapoint>();
        /// <summary>JIT validation: ideal-kinematic expected travel duration [s] per pod→station trip, captured at trip start.</summary>
        public List<double> StatJITEtaExpectedSamples = new List<double>();
        /// <summary>JIT validation: measured actual travel duration [s] per pod→station trip, captured at queue-zone arrival.</summary>
        public List<double> StatJITEtaActualSamples = new List<double>();
        /// <summary>JIT validation diagnostic: A* path nodes count per trip (start, ..., end inclusive).</summary>
        public List<int> StatJITEtaPathNodeCounts = new List<int>();
        /// <summary>JIT validation diagnostic: A* path total euclidean length [m] per trip.</summary>
        public List<double> StatJITEtaPathLengths = new List<double>();
        /// <summary>JIT validation diagnostic: source waypoint ID per trip.</summary>
        public List<int> StatJITEtaSourceIds = new List<int>();
        /// <summary>JIT validation diagnostic: destination waypoint ID per trip (queue rear or fallback).</summary>
        public List<int> StatJITEtaDestIds = new List<int>();
        public List<int> StatJITEtaBotIds = new List<int>();
        public List<string> StatJITEtaTaskIds = new List<string>();
        public List<int> StatJITEtaTripIds = new List<int>();
        public List<int> StatJITEtaStopGoCounts = new List<int>();
        public List<int> StatJITEtaQueueStopGoCounts = new List<int>();
        public List<double> StatJITEtaWaitSecs = new List<double>();
        /// <summary>Actual extract-leg rows with ETA-surrogate feature columns; written to eta_surrogate_actual_legs.csv.</summary>
        public List<string> StatEtaSurrogateActualLegRows = new List<string>();
        /// <summary>Backfill-potential probe rows (BackfillProbeEnabled). One row per projected
        /// starvation-gap onset per station; written to backfill_probe.csv at finish.</summary>
        public List<string> StatBackfillProbeRows = new List<string>();
        /// <summary>Input-scheduler diagnostic rows (one per Schedule call with >=1 holder);
        /// written to input_sched_probe.csv at finish. For diagnosing input EST under-estimation.</summary>
        public List<string> StatInputSchedRows = new List<string>();
        /// <summary>Per-pod INPUT-station queue wait [s]: queue-zone arrival → storing start. One
        /// sample per pod visit to an InputStation. Previously unmeasured (only output was tracked).</summary>
        public List<double> StatInputPodQueueWaitSamples = new List<double>();
        /// <summary>Input slow-start release events (gated by BackfillProbeEnabled): one row per
        /// holder release; written to input_release_probe.csv. For detecting synchronized releases
        /// (two holders released within seconds at the same input station).</summary>
        public List<string> StatInputReleaseRows = new List<string>();
        /// <summary>Fleet Type A starvation (supply lag, optimizable): sum across stations of idle time where
        /// the station has assigned orders but no pod is ready/queued (pod not yet arrived) [s].</summary>
        public double StatOverallStationStarvationTimeSec { get { return OutputStations.Sum(s => s.StatStarvationTimeSec); } }
        /// <summary>Fleet Type B starvation (no-order waste, HADGS-caused): sum across stations of idle time
        /// where the station has NO assigned order and no pod — the order manager left it unused [s].</summary>
        public double StatOverallStationNoOrderIdleTimeSec { get { return OutputStations.Sum(s => s.StatStationNoOrderIdleTimeSec); } }
        /// <summary>Per-pod queue wait [s]: time from bot+pod entering queue zone until station begins picking.
        /// Excludes processing time. One sample per pod visit to an OutputStation.</summary>
        public List<double> StatPodQueueWaitSamples = new List<double>();
        /// <summary>Diagnostic — per-pod-handoff gap [s] at an OutputStation: time from the previous pod's
        /// last transfer finishing until the next pod's first pick begins. Captures the inter-pod
        /// "銜接段" gap (waiting for / positioning the next pod) regardless of whether
        /// HasReadyOrQueuedExtractPod masked it from the Type-A starvation counter. One sample per
        /// pod transition (excludes each station's very first pod). Lets us check how much
        /// handoff gap exists even when measured starvation is ~0 (e.g. bot-abundant).</summary>
        public List<double> StatPodHandoffGapSamples = new List<double>();
        /// <summary>SA-HADGS: candidate commits whose projected pipeline gap was zero (on-time).</summary>
        public int StatSaHadgsOnTimeCommits = 0;
        /// <summary>SA-HADGS: candidate commits with a positive projected gap (late, soft-constraint path).</summary>
        public int StatSaHadgsLateCommits = 0;
        /// <summary>SA-HADGS: summed projected gap seconds over late commits.</summary>
        public double StatSaHadgsLatenessSumSec = 0.0;
        /// <summary>Per-pod picking time [s]: time from station beginning to pick until last item finished.
        /// One sample per pod visit to an OutputStation.</summary>
        public List<double> StatPodPickingTimeSamples = new List<double>();
        /// <summary>
        /// Diagnostic KPI: distinct orders served per pod-visit (one sample per ExtractTask.Finish).
        /// Distribution stats (mean, p25/50/75/95) reveal whether pod value is multi-order or single-order.
        /// Used to decide if starvation-aware OB+PS has value-differentiation headroom over distance-min.
        /// </summary>
        public List<int> StatPodVisitOrdersServedSamples = new List<int>();
        /// <summary>
        /// Diagnostic KPI: per-station inbound-pod count snapshot taken at the start of each OB decision trigger.
        /// One sample per (trigger × station) pair. Distribution reveals queue congestion at decision time.
        /// </summary>
        public List<int> StatDecisionTriggerQueueDepthSamples = new List<int>();
        /// <summary>
        /// Per-decision trace for slow-start release timing. Used to evaluate whether upstream
        /// holds are actually converting station queue wait into better departure timing.
        /// </summary>
        public List<SlowStartDecisionTrace> StatSlowStartDecisionTraces = new List<SlowStartDecisionTrace>();
        /// <summary>
        /// Per-scheduler-tick slow-start holding decisions. One row per holder per station
        /// scheduler invocation; lifecycle callbacks fill in the eventual realized timings.
        /// </summary>
        public List<SlowStartHoldingDecisionTrace> StatSlowStartHoldingDecisionTraces = new List<SlowStartHoldingDecisionTrace>();
        /// <summary>
        /// Decision-space KPI: total available station slots (Σ Cs[s] = Capacity − Reserved − InUse) at each HADGS trigger.
        /// Equals the maximum number of orders the trigger can admit across all stations.
        /// </summary>
        public List<int> StatDecisionAvailableStationSlotsSamples = new List<int>();
        /// <summary>
        /// Decision-space KPI: count of UnusedPods (pods available for fresh assignment) at each HADGS trigger.
        /// </summary>
        public List<int> StatDecisionUnusedPodsSamples = new List<int>();
        /// <summary>
        /// Decision-space KPI: count of pending orders in backlog at each HADGS trigger.
        /// </summary>
        public List<int> StatDecisionPendingOrdersSamples = new List<int>();
        /// <summary>
        /// Decision-space KPI: candidate-combination size = slots × pods × pendingOrders at each trigger.
        /// Approximates the size of the discrete decision space HADGS is choosing from.
        /// </summary>
        public List<long> StatDecisionCandidateCombosSamples = new List<long>();
        /// <summary>Bots whose current task is Rest at the moment of HADGS trigger (over-supply indicator).</summary>
        public List<int> StatDecisionBotsInRestSamples = new List<int>();
        /// <summary>Bots whose current task is Rest or None (truly unproductive) at HADGS trigger.</summary>
        public List<int> StatDecisionBotsIdleOrRestSamples = new List<int>();
        /// <summary>Fleet idle time (no task assigned) [s].</summary>
        public double StatOverallTimeIdleSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTimeIdleSec); } }
        /// <summary>
        /// Fleet Rest-task time [s] — bot was on Rest (parking / returning to rest spot).
        /// Rest is NOT productive work; counted separately so utilization calculations
        /// can correctly classify it as non-productive.
        /// </summary>
        public double StatOverallTimeRestSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b =>
            b.StatTotalTaskTimes.TryGetValue(Control.BotTaskType.Rest, out var t) ? t : 0.0); } }
        /// <summary>
        /// Fleet total bot-seconds = SimulationDuration × botCount. Denominator for utilization ratios.
        /// </summary>
        public double StatTotalBotSec { get { return StatTime * Bots.OfType<Bots.BotNormal>().Count(); } }
        /// <summary>
        /// Layer 1 utilization (legacy / raw occupancy): fraction of bot-time with ANY assigned task
        /// (including Rest). Equals 1 − idle/total. Kept for backward compatibility but inflates the
        /// real productive figure because Rest is counted as utilized.
        /// </summary>
        public double StatUtilizationRaw { get {
            double denom = StatTotalBotSec; if (denom <= 0) return double.NaN;
            return 1.0 - StatOverallTimeIdleSec / denom; } }
        /// <summary>
        /// Layer 2 utilization (productive): fraction of bot-time on tasks that advance system state.
        /// Excludes None AND Rest. Captures Extract / Insert / ParkPod / RepositionPod time.
        /// </summary>
        public double StatUtilizationProductive { get {
            double denom = StatTotalBotSec; if (denom <= 0) return double.NaN;
            return Math.Max(0.0, 1.0 - (StatOverallTimeIdleSec + StatOverallTimeRestSec) / denom); } }
        /// <summary>
        /// Layer 3 utilization (effective): productive utilization minus time spent waiting
        /// at station queue or stuck in congestion wait. The fraction of bot-time *actually*
        /// doing work that drives throughput. Headline number for "fleet truly busy".
        /// </summary>
        public double StatUtilizationEffective { get {
            double denom = StatTotalBotSec; if (denom <= 0) return double.NaN;
            double nonProductive = StatOverallTimeIdleSec + StatOverallTimeRestSec
                                 + StatOverallQueueingAtStationTimeSec + StatOverallWaitTimeSec;
            return Math.Max(0.0, 1.0 - nonProductive / denom); } }
        // ── Pref calibration: event-level 8-accumulator aggregation ───────────────
        public double StatOverallMoveEnergyEmptyJ  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatMoveEnergyEmptyJ); } }
        public double StatOverallMoveTimeEmptySec  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatMoveTimeEmptySec); } }
        public double StatOverallTurnEnergyEmptyJ  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTurnEnergyEmptyJ); } }
        public double StatOverallTurnTimeEmptySec  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTurnTimeEmptySec); } }
        public double StatOverallMoveEnergyLoadedJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatMoveEnergyLoadedJ); } }
        public double StatOverallMoveTimeLoadedSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatMoveTimeLoadedSec); } }
        public double StatOverallTurnEnergyLoadedJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTurnEnergyLoadedJ); } }
        public double StatOverallTurnTimeLoadedSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTurnTimeLoadedSec); } }
        /// <summary>Total number of completed loaded trips (pickup → setdown) across all bots.</summary>
        public int StatOverallTripCountLoaded { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTripCountLoaded); } }
        /// <summary>Total number of completed empty trips (setdown → next pickup) across all bots.</summary>
        public int StatOverallTripCountEmpty { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatTripCountEmpty); } }
        /// <summary>Fleet distance traveled while empty [m].</summary>
        public double StatOverallEmptyDistanceM { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEmptyDistanceM); } }
        /// <summary>System throughput [orders/hour] = orders / simulation hours.</summary>
        public double StatThroughputOrdersPerHour => StatTime > 0 ? StatOverallOrdersHandled / (StatTime / 3600.0) : 0.0;
        /// <summary>Average distance per completed order [m/order] = total_distance_m / orders (consistent with CSV kpi_report).</summary>
        public double StatOrderDistanceM => StatOverallOrdersHandled > 0 ? StatOverallDistanceTraveled / StatOverallOrdersHandled : 0.0;
        /// <summary>System-wide output-side order pile-on [orders/output_station_arrival].</summary>
        public double StatSystemOrderPileOn { get { int arrivals = StatOverallOutputStationArrivals; return arrivals > 0 ? (double)StatOverallOrdersHandled / arrivals : 0.0; } }
        /// <summary>Fleet wait time while loaded [s].</summary>
        public double StatOverallWaitTimeLoadedSec { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatWaitTimeLoadedSec); } }
        /// <summary>Fleet wait time while empty [s].</summary>
        public double StatOverallWaitTimeEmptySec  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatWaitTimeEmptySec); } }
        /// <summary>Fleet wait energy while loaded [J] = SUPPORT_POWER_LOADED × congestion-wait time.</summary>
        public double StatOverallWaitEnergyLoadedJ { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatWaitEnergyLoadedJ); } }
        /// <summary>Fleet wait energy while empty [J].</summary>
        public double StatOverallWaitEnergyEmptyJ  { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatWaitEnergyEmptyJ); } }
        /// <summary>Fleet input-station processing arrivals.</summary>
        public int StatOverallInputStationArrivals { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatInputStationArrivals); } }
        /// <summary>Fleet output-station processing arrivals.</summary>
        public int StatOverallOutputStationArrivals { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatOutputStationArrivals); } }
        /// <summary>Fleet station processing arrivals.</summary>
        public int StatOverallStationArrivals { get { return Bots.OfType<Bots.BotNormal>().Sum(b => b.StatStationArrivals); } }
        /// <summary>
        /// The estimated distance by the bots.
        /// </summary>
        public double StatOverallDistanceEstimated { get { return Bots.Sum(b => b.StatDistanceEstimated); } }
        /// <summary>
        /// The total number of assigned tasks so far.
        /// </summary>
        public int StatOverallAssignedTasks { get { return Bots.Sum(b => b.StatAssignedTasks); } }
        /// <summary>
        /// The maximum Memory Usage in megabyte.
        /// </summary>
        public double StatMaxMemoryUsed { get; set; }
        /// <summary>
        /// The turnover times for all completed orders.
        /// </summary>
        internal List<double> _statOrderTurnoverTimes = new List<double>();
        /// <summary>
        /// The throughput times for all completed orders.
        /// </summary>
        internal List<double> _statOrderThroughputTimes = new List<double>();
        /// <summary>
        /// The lateness for all completed orders.
        /// </summary>
        internal List<double> _statOrderLatenessTimes = new List<double>();
        /// <summary>
        /// The turnover times for all completed bundles.
        /// </summary>
        internal List<double> _statBundleTurnoverTimes = new List<double>();
        /// <summary>
        /// The throughput times for all completed bundles.
        /// </summary>
        internal List<double> _statBundleThroughputTimes = new List<double>();
        /// <summary>
        /// The average order throughput time.
        /// </summary>
        public double StatOrderThroughputTimeAvg { get { return _statOrderThroughputTimes.Any() ? _statOrderThroughputTimes.Average(t => t) : 0; } }
        /// <summary>
        /// Stores all times at which an item was handled.
        /// </summary>
        internal List<ItemHandledDatapoint> _statItemHandlingTimestamps = new List<ItemHandledDatapoint>();
        /// <summary>
        /// Stores all times at which an bundle was handled.
        /// </summary>
        internal List<BundleHandledDatapoint> _statBundleHandlingTimestamps = new List<BundleHandledDatapoint>();
        /// <summary>
        /// Stores all times at which an order was handled.
        /// </summary>
        internal List<OrderHandledDatapoint> _statOrderHandlingTimestamps = new List<OrderHandledDatapoint>();
        /// <summary>
        /// Stores all times at which an incoming bundle was placed.
        /// </summary>
        internal List<BundlePlacedDatapoint> _statBundlePlacementTimestamps = new List<BundlePlacedDatapoint>();
        /// <summary>
        /// Stores all times at which a new order was placed.
        /// </summary>
        internal List<OrderPlacedDatapoint> _statOrderPlacementTimestamps = new List<OrderPlacedDatapoint>();
        /// <summary>
        /// Stores all times at which a collision happened.
        /// </summary>
        internal List<CollisionDatapoint> _statCollisionTimestamps = new List<CollisionDatapoint>();
        /// <summary>
        /// Stores all completed trips to stations queueing areas.
        /// </summary>
        internal List<StationTripDatapoint> _statStationTripTimestamps = new List<StationTripDatapoint>();
        /// <summary>
        /// The number of collisions that happened in this instance. This value may count a collision of two robots twice, hence it should only be used as an indicator.
        /// </summary>
        public int StatOverallCollisions { get; private set; }
        /// <summary>
        /// The number of bots that reached their targeted output station queueing area.
        /// </summary>
        private int _oStationTripCount = 0;
        /// <summary>
        /// The average time it took a bot for their last trip towards an output station queueing area.
        /// </summary>
        private double _oStationTripTimeAvg = 0;
        /// <summary>
        /// The number of bots that reached their targeted input station queueing area.
        /// </summary>
        private int _iStationTripCount = 0;
        /// <summary>
        /// The average time it took a bot for their last trip towards an input station queueing area.
        /// </summary>
        private double _iStationTripTimeAvg = 0;
        /// <summary>
        /// Registers another trip completed trip to a station queueing area.
        /// </summary>
        /// <param name="tripType">The type of the trip.</param>
        /// <param name="tripTime">The time for completing the trip.</param>
        protected void StatAddTrip(StationTripType tripType, double tripTime)
        {
            switch (tripType)
            {
                case StationTripType.O: _oStationTripCount++; _oStationTripTimeAvg = _oStationTripTimeAvg + (tripTime - _oStationTripTimeAvg) / (_oStationTripCount); break;
                case StationTripType.I: _iStationTripCount++; _iStationTripTimeAvg = _iStationTripTimeAvg + (tripTime - _iStationTripTimeAvg) / (_iStationTripCount); break;
                default: throw new ArgumentException("Unknown trip type: " + tripType);
            }
        }
        /// <summary>
        /// The number of bots that reached their targeted output station queueing area.
        /// </summary>
        public double OStationTripCount { get { return _oStationTripCount; } }
        /// <summary>
        /// The average time it took a bot for their last trip towards an output station queueing area.
        /// </summary>
        public double OStationTripTimeAvg { get { return _oStationTripTimeAvg; } }
        /// <summary>
        /// The number of bots that reached their targeted input station queueing area.
        /// </summary>
        public double IStationTripCount { get { return _iStationTripCount; } }
        /// <summary>
        /// The number of bots that reached their targeted output station queueing area.
        /// </summary>
        public double IStationTripTimeAvg { get { return _iStationTripTimeAvg; } }
        /// <summary>
        /// The number of times a move could not be executed due to a failed reservation.
        /// </summary>
        public int StatOverallFailedReservations { get; internal set; }
        /// <summary>
        /// The number of times the runtime limit was reached by the path planning method.
        /// </summary>
        public int StatOverallPathPlanningTimeouts { get; internal set; }
        /// <summary>
        /// The overall storage capacity.
        /// </summary>
        private double _storageCapacity = double.NaN;
        /// <summary>
        /// The current overall storage usage.
        /// </summary>
        private double _storageUsage = double.NaN;
        /// <summary>
        /// The current overall storage reserved for bundles.
        /// </summary>
        private double _storageReserved = double.NaN;
        /// <summary>
        /// The current storage capacity required by the backlog bundles.
        /// </summary>
        private double _storageBacklog = double.NaN;
        /// <summary>
        /// The overall storage capacity.
        /// </summary>
        internal double StorageCapacity
        {
            get { if (double.IsNaN(_storageCapacity)) InitStorageTracking(); return _storageCapacity; }
            set { if (double.IsNaN(_storageCapacity)) InitStorageTracking(); _storageCapacity = value; }
        }
        /// <summary>
        /// The current overall storage usage.
        /// </summary>
        internal double StorageUsage
        {
            get { if (double.IsNaN(_storageUsage)) InitStorageTracking(); return _storageUsage; }
            set { if (double.IsNaN(_storageUsage)) InitStorageTracking(); _storageUsage = value; }
        }
        /// <summary>
        /// The current overall storage reserved for bundles.
        /// </summary>
        internal double StorageReserved
        {
            get { if (double.IsNaN(_storageReserved)) InitStorageTracking(); return _storageReserved; }
            set { if (double.IsNaN(_storageReserved)) InitStorageTracking(); _storageReserved = value; }
        }
        /// <summary>
        /// The current storage capacity required by the backlog bundles.
        /// </summary>
        internal double StorageBacklog
        {
            get { if (double.IsNaN(_storageBacklog)) InitStorageTracking(); return _storageBacklog; }
            set { if (double.IsNaN(_storageBacklog)) InitStorageTracking(); _storageBacklog = value; }
        }
        /// <summary>
        /// Initializes storage tracking.
        /// </summary>
        private void InitStorageTracking()
        {
            _storageCapacity = Pods.Sum(p => p.Capacity);
            _storageUsage = Pods.Sum(p => p.CapacityInUse);
            _storageReserved = Pods.Sum(p => p.CapacityReserved);
            _storageBacklog = 0;
        }
        /// <summary>
        /// The overall storage fill level.
        /// </summary>
        public double StatStorageFillLevel { get { return StorageUsage / StorageCapacity; } }
        /// <summary>
        /// The overall storage fill level including the reservations present for the pods.
        /// </summary>
        public double StatStorageFillAndReservedLevel { get { return (StorageUsage + StorageReserved) / StorageCapacity; } }
        /// <summary>
        /// The overall storage fill level including the reservations present for the pods and the capacity consumed by the backlog bundles.
        /// </summary>
        public double StatStorageFillAndReservedAndBacklogLevel { get { return (StorageUsage + StorageReserved + StorageBacklog) / StorageCapacity; } }
        /// <summary>
        /// The maximal number of items handled by a pod.
        /// </summary>
        public int StatMaxItemsHandledByPod { get; private set; }
        /// <summary>
        /// The maximal number of bundles handled by a pod.
        /// </summary>
        public int StatMaxBundlesHandledByPod { get; private set; }
        /// <summary>
        /// Contains custom info written by the different controllers.
        /// </summary>
        public CustomControllerDatapoint StatCustomControllerInfo { get; private set; } = new CustomControllerDatapoint();

        #endregion

        #region Stat I/O and reset

        /// <summary>
        /// Resets the statistics
        /// </summary>
        public void StatReset()
        {
            // Indicate reset for controlling processes
            StatWarmupResetDone = true;
            // Reset basics
            StatTimeStart = Controller.CurrentTime;
            StatMaxItemsHandledByPod = 0;
            StatMaxBundlesHandledByPod = 0;
            StatBundleGenerationStops = 0;
            StatOrderGenerationStops = 0;
            StatOverallItemsHandled = 0;
            StatOverallBundlesHandled = 0;
            StatOverallLinesHandled = 0;
            StatOverallOrdersHandled = 0;
            StatOverallOrdersLate = 0;
            _statBundleHandlingTimestamps.Clear();
            _statItemHandlingTimestamps.Clear();
            _statOrderHandlingTimestamps.Clear();
            _statBundlePlacementTimestamps.Clear();
            _statOrderPlacementTimestamps.Clear();
            StatOverallCollisions = 0;
            StatOverallFailedReservations = 0;
            StatOverallPathPlanningTimeouts = 0;
            _statCollisionTimestamps.Clear();
            _oStationTripCount = 0;
            _oStationTripTimeAvg = 0;
            _iStationTripCount = 0;
            _iStationTripTimeAvg = 0;
            _statStationTripTimestamps.Clear();
            StatOverallItemsOrdered = 0;
            StatOverallOrdersPlaced = 0;
            StatOverallBundlesPlaced = 0;
            StatOverallOrdersRejected = 0;
            StatOverallBundlesRejected = 0;
            StatRepositioningMoves = 0;
            _statOrderTurnoverTimes.Clear();
            _statOrderThroughputTimes.Clear();
            _statOrderLatenessTimes.Clear();
            _statBundleThroughputTimes.Clear();
            _statBundleTurnoverTimes.Clear();
            StatJITEtaExpectedSamples.Clear();
            StatJITEtaActualSamples.Clear();
            StatJITEtaPathNodeCounts.Clear();
            StatJITEtaPathLengths.Clear();
            StatJITEtaSourceIds.Clear();
            StatJITEtaDestIds.Clear();
            StatJITEtaBotIds.Clear();
            StatJITEtaTaskIds.Clear();
            StatJITEtaTripIds.Clear();
            StatJITEtaStopGoCounts.Clear();
            StatJITEtaQueueStopGoCounts.Clear();
            StatJITEtaWaitSecs.Clear();
            StatEtaSurrogateActualLegRows.Clear();
            StatStopGoEventRows.Clear();
            StatConflictWaitHeatPoints.Clear();
            StatBackfillProbeRows.Clear();
            StatInputSchedRows.Clear();
            StatInputPodQueueWaitSamples.Clear();
            StatInputReleaseRows.Clear();
            StatSlowStartDecisionTraces.Clear();
            StatSlowStartHoldingDecisionTraces.Clear();
            StatSaHadgsOnTimeCommits = 0;
            StatSaHadgsLateCommits = 0;
            StatSaHadgsLatenessSumSec = 0.0;

            // Reset custom controller info
            StatCustomControllerInfo = new CustomControllerDatapoint();
            Controller.MethodManager?.StatReset();
            Controller.StationManager?.StatReset();
            Controller.OrderManager?.StatReset();
            Controller.BundleManager?.StatReset();
            Controller.StorageManager?.StatReset();
            Controller.PodStorageManager?.StatReset();
            Controller.RepositioningManager?.StatReset();
            Controller.BotManager?.StatReset();
            Controller.PathManager?.StatReset();

            // Reset observer
            Observer.Reset();

            // Reset frequency tracker
            FrequencyTracker.Reset();

            // Reset all objects
            foreach (var b in Bots)
            {
                b.ResetStatistics();
                if (b is Bots.BotNormal bn)
                    bn.ResetEnergyStatistics();
            }
            foreach (var ls in InputStations)
                ls.ResetStatistics();
            foreach (var ws in OutputStations)
                ws.ResetStatistics();
            foreach (var b in Pods)
                b.ResetStatistics();
            foreach (var wp in Waypoints)
                wp.ResetStatistics();
            ItemManager.ResetStatistics();

            // Init statistics directory
            StatInitDirectory();

            // Clean up potential previous data
            if (Directory.Exists(SettingConfig.StatisticsDirectory))
            {
                // Delete all stat files one by one (sparing other files in the directory)
                foreach (var statFileName in IOConstants.StatFileNames.Values)
                {
                    string statFilePath = Path.Combine(SettingConfig.StatisticsDirectory, statFileName);
                    if (File.Exists(statFilePath))
                        File.Delete(statFilePath);
                }
            }
        }

        /// <summary>
        /// Finalizes some potentially incomplete statistics.
        /// </summary>
        public void StatFinish()
        {
            // Finalize potentially incomplete trips
            foreach (var bot in Bots)
                bot.LogIncompleteTrip();
            // Submit custom statistics
            Controller.MethodManager?.StatFinish();
            Controller.StationManager?.StatFinish();
            Controller.OrderManager?.StatFinish();
            Controller.BundleManager?.StatFinish();
            Controller.StorageManager?.StatFinish();
            Controller.PodStorageManager?.StatFinish();
            Controller.RepositioningManager?.StatFinish();
            Controller.BotManager?.StatFinish();
            Controller.PathManager?.StatFinish();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics to reduce memory usage.
        /// </summary>
        public void StatFlushBundlesHandled()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write bundle-handling progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.BundleProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.BundleProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + BundleHandledDatapoint.GetHeader());
                        foreach (var d in _statBundleHandlingTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statBundleHandlingTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics to reduce memory usage.
        /// </summary>
        public void StatFlushItemsHandled()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write item-handling progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ItemProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ItemProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + ItemHandledDatapoint.GetHeader());
                        foreach (var d in _statItemHandlingTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statItemHandlingTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics to reduce memory usage.
        /// </summary>
        public void StatFlushOrdersHandled()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write order-handling progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.OrderProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.OrderProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + OrderHandledDatapoint.GetHeader());
                        foreach (var d in _statOrderHandlingTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statOrderHandlingTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics.
        /// </summary>
        public void StatFlushBundlesPlaced()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write bundle-placement progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.BundlePlacementProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.BundlePlacementProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + BundlePlacedDatapoint.GetHeader());
                        foreach (var d in _statBundlePlacementTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statBundlePlacementTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics.
        /// </summary>
        public void StatFlushOrdersPlaced()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write order-placement progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.OrderPlacementProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.OrderPlacementProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + OrderPlacedDatapoint.GetHeader());
                        foreach (var d in _statOrderPlacementTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statOrderPlacementTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics.
        /// </summary>
        private void StatFlushPathFinding()
        {
            if (Controller.PathManager == null || !Controller.PathManager.Log)
                return;

            // Init statistics directory
            StatInitDirectory();
            // Write path finding data
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.PathFinding]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.PathFinding]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + PathFindingDatapoint.GetHeader());
                        foreach (var d in Controller.PathManager.StatDataPoints)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            Controller.PathManager.StatDataPoints.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics to reduce memory usage.
        /// </summary>
        public void StatFlushCollisions()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write collision progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.CollisionProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.CollisionProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + CollisionDatapoint.GetHeader());
                        foreach (var d in _statCollisionTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statCollisionTimestamps.Clear();
        }

        /// <summary>
        /// Flushes the current state of the corresponding statistics to reduce memory usage.
        /// </summary>
        public void StatFlushTripsCompleted()
        {
            // Init statistics directory
            StatInitDirectory();
            // Write station trip progression
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    bool alreadyExists = File.Exists(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.TripsCompletedProgressionRaw]));
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.TripsCompletedProgressionRaw]), true))
                    {
                        if (!alreadyExists)
                            sw.WriteLine(IOConstants.COMMENT_LINE + StationTripDatapoint.GetHeader());
                        foreach (var d in _statStationTripTimestamps)
                            sw.WriteLine(d.GetLine());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
            // Clear the data points
            _statStationTripTimestamps.Clear();
        }

        /// <summary>
        /// Flushes statistics about the trips of the robots.
        /// </summary>
        public void StatFlushTripStatistics()
        {
            // --> Flush whole trip statistics
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    // Get station waypoints
                    List<Waypoint> outputstationWPs = Waypoints.Where(wp => wp.OutputStation != null).ToList();
                    List<Waypoint> inputstationWPs = Waypoints.Where(wp => wp.InputStation != null).ToList();
                    // Create files and flush data
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.HeatTrips]), false))
                    {
                        // Write header
                        sw.WriteLine(TimeIndependentTripDataPoint.GetCSVHeader());
                        // Write datapoints per waypoint
                        foreach (var waypoint in Waypoints)
                        {
                            TimeIndependentTripDataPoint datapoint = new TimeIndependentTripDataPoint()
                            {
                                Tier = waypoint.Tier.ID,
                                X = waypoint.X,
                                Y = waypoint.Y,
                                Overall = Waypoints.Sum(wp => wp.StatContainsTripDataIn(waypoint) ? wp.StatGetTripDataIn(waypoint).Count : 0),
                                ToOStation = outputstationWPs.Sum(o => o.StatContainsTripDataIn(waypoint) ? o.StatGetTripDataIn(waypoint).Count : 0),
                                FromOStation = outputstationWPs.Sum(o => o.StatContainsTripDataOut(waypoint) ? o.StatGetTripDataOut(waypoint).Count : 0),
                                ToIStation = inputstationWPs.Sum(i => i.StatContainsTripDataIn(waypoint) ? i.StatGetTripDataIn(waypoint).Count : 0),
                                FromIStation = inputstationWPs.Sum(i => i.StatContainsTripDataOut(waypoint) ? i.StatGetTripDataOut(waypoint).Count : 0),
                            };
                            sw.WriteLine(datapoint.ToCSV());
                        }
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
        }

        /// <summary>
        /// Flushes all data of all connections that have been used.
        /// </summary>
        public void StatFlushConnectionStatistics()
        {
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ConnectionStatistics]), false))
                    {
                        sw.WriteLine(IOConstants.COMMENT_LINE + ConnectionStatisticsDataPoint.GetStringTupleRepresentationDescription());
                        foreach (var from in Waypoints)
                            foreach (var to in Waypoints)
                                if (from.StatContainsTripDataOut(to))
                                    sw.WriteLine(from.StatGetTripDataOut(to).GetStringTupleRepresentation());
                    }
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }
        }

        /// <summary>
        /// Initializes the statistics directory.
        /// </summary>
        internal void StatInitDirectory()
        {
            // Create a default statistics directory name if none is given
            if (string.IsNullOrWhiteSpace(SettingConfig.StatisticsDirectory))
                SettingConfig.StatisticsDirectory = GetMetaInfoBasedInstanceName() + "-" + ControllerConfig.GetMetaInfoBasedConfigName() + "-" + SettingConfig.Seed;
            // Create the new and empty directory
            if (!Directory.Exists(SettingConfig.StatisticsDirectory))
                Directory.CreateDirectory(SettingConfig.StatisticsDirectory);
        }

        /// <summary>
        /// Calculates the p-th percentile of a sorted list of values.
        /// </summary>
        /// <param name="sortedValues">List of values already sorted in ascending order.</param>
        /// <param name="p">Percentile (0.0 to 1.0), e.g., 0.50 for median, 0.95 for p95.</param>
        /// <returns>The percentile value, or NaN if list is empty.</returns>
        private static double Percentile(List<double> sortedValues, double p)
        {
            if (sortedValues.Count == 0) return double.NaN;
            double idx = p * (sortedValues.Count - 1);
            int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            return sortedValues[lo] + (sortedValues[hi] - sortedValues[lo]) * (idx - lo);
        }

        /// <summary>
        /// Writes and flushes all statistics to the directory specified in the configuration.
        /// </summary>
        public void WriteStatistics()
        {
            // Indicate stats written for controlling processes
            StatResultsWritten = true;

            // Finalize statistics
            StatFinish();

            // Init statistics directory
            StatInitDirectory();

            // Write footprint
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.Footprint])))
                // Write stat line
                sw.WriteLine(new FootprintDatapoint(this).GetFootprint());

            // Write 6-Layer KPI report (always, regardless of log level)
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, "kpi_report.csv")))
                WriteKpiReport(sw);

            // Write slow-start decision trace (always, even when empty, so experiment scripts can assert presence)
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, "slowstart_decisions.csv")))
                WriteSlowStartDecisionTrace(sw);
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, "slowstart_holding_decisions.csv")))
                WriteSlowStartHoldingDecisionTrace(sw);

            // Write further statistics
            switch (SettingConfig.LogFileLevel)
            {
                case Configurations.LogFileLevel.All:
                    // Write readable statistics
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ReadableStatistics])))
                        PrintStatistics(sw.WriteLine, detailedAll: true);
                    // Write station statistics
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.StationStatistics])))
                        WriteStationStatistics(sw.Write);
                    // Write item descriptions statistics
                    using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ItemDescriptionStatistics])))
                        WriteItemDescriptionStatistics(sw.Write);
                    break;
                case Configurations.LogFileLevel.FootprintOnly:
                    break;
                default: throw new ArgumentException("Unknown log level: " + SettingConfig.LogFileLevel);
            }

            // Write instance name
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.InstanceName])))
                sw.Write((string.IsNullOrWhiteSpace(this.Name) ? this.GetMetaInfoBasedInstanceName() : this.Name));

            // Write setting name
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.SettingName])))
                sw.Write(SettingConfig.Name);

            // Write controller name
            using (StreamWriter sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, IOConstants.StatFileNames[IOConstants.StatFile.ControllerName])))
                sw.Write(ControllerConfig.Name);

            // Flush the data
            StatFlushBundlesHandled();
            StatFlushItemsHandled();
            StatFlushOrdersHandled();
            StatFlushBundlesPlaced();
            StatFlushOrdersPlaced();
            StatFlushCollisions();
            StatFlushTripsCompleted();
            StatFlushPathFinding();

            // Flush observer data
            Observer.FlushData();

            // Flush controller dependant performance information
            WriteIndividiualStatistics();

            // Flush trip statistics data
            StatFlushTripStatistics();
            StatFlushConnectionStatistics();
        }

        internal SlowStartDecisionTrace NotifySlowStartDecision(
            Bots.BotNormal bot,
            ExtractTask task,
            SlowStartController.HoldDiagnostics diag,
            double currentTime,
            double deadline)
        {
            var station = task != null ? task.OutputStation : null;
            double stationBusyRemaining = 0.0;
            if (station != null)
            {
                double blockedUntil = station.GetBlockedUntilTime();
                if (!double.IsNaN(blockedUntil) && blockedUntil > currentTime)
                    stationBusyRemaining = blockedUntil - currentTime;
            }

            var trace = new SlowStartDecisionTrace()
            {
                Time = currentTime,
                BotId = bot != null ? bot.GetIdentfierString() : "",
                TaskId = task != null ? task.GetHashCode().ToString(IOConstants.FORMATTER) : "",
                PodId = task != null && task.ReservedPod != null ? task.ReservedPod.GetIdentfierString() : "",
                StationId = station != null ? station.GetIdentfierString() : "",
                EtaNow = diag.Eta,
                TStarve = diag.TStarve,
                QueueBudget = diag.QueueBudget,
                ReleaseBudget = diag.ReleaseBudget,
                Slack = Math.Max(diag.TStarve, diag.QueueBudget) - diag.Eta - (SettingConfig != null ? SettingConfig.SlowStartEtaSafetyBuffer : 0.0),
                ChosenDelay = diag.Delay,
                Deadline = deadline,
                ReleasePolicy = SettingConfig != null ? SettingConfig.SlowStartReleasePolicy.ToString() : "",
                EtaSafetyBuffer = SettingConfig != null ? SettingConfig.SlowStartEtaSafetyBuffer : 0.0,
                ProbeSuccess = !diag.EtaProbeFailed,
                ReleaseReason = diag.Delay <= 0.0 ? "immediate_release" : "",
                StationPendingItems = station != null ? station.PendingItemRequestCount : 0,
                StationBusyRemaining = stationBusyRemaining,
                StationQueueWork = EstimateStationQueueWorkForSlowStart(station, bot),
                NeighborCountAtDecision = CountNearbyBots(bot, 2.0),
                MissingArrivalCount = diag.MissingArrivalCount,
                QueueArrivalTime = double.NaN,
                ReleaseTime = diag.Delay <= 0.0 ? currentTime : double.NaN,
                ActualArrivalTime = double.NaN,
                ActualEta = double.NaN,
                ArrivalError = double.NaN,
                StationIdleAtArrival = double.NaN,
                QueueWaitAtStation = double.NaN,
            };
            StatSlowStartDecisionTraces.Add(trace);
            return trace;
        }

        internal void NotifySlowStartRelease(SlowStartDecisionTrace trace, double currentTime, string releaseReason, double etaNow, double remainingTStarve, double bestFutureEta = double.NaN, double bestFutureDelay = double.NaN)
        {
            if (trace == null)
                return;
            trace.ReleaseTime = currentTime;
            if (!double.IsNaN(trace.Time))
                trace.ActualHoldTime = Math.Max(0.0, currentTime - trace.Time);
            trace.ReleaseReason = releaseReason ?? "";
            trace.EtaAtRelease = etaNow;
            trace.RemainingTStarveAtRelease = remainingTStarve;
            trace.BestFutureEtaAtRelease = bestFutureEta;
            trace.BestFutureDelayAtRelease = bestFutureDelay;
            if (!double.IsNaN(etaNow) && !double.IsNaN(bestFutureEta))
                trace.EtaImprovementAtRelease = etaNow - bestFutureEta;
            UpdateSlowStartHoldingRows(trace.TaskId, trace.BotId, row =>
            {
                row.ReleaseTime = currentTime;
                row.ReleaseReason = trace.ReleaseReason;
                row.ActualHoldTime = trace.ActualHoldTime;
                row.EtaAtRelease = etaNow;
                row.RemainingEstAtRelease = remainingTStarve;
            });
        }

        internal void NotifySlowStartStationTripStart(ExtractTask task, Bots.BotNormal bot, double currentTime)
        {
            var trace = FindLatestSlowStartTrace(task, bot);
            if (trace != null && double.IsNaN(trace.MovementStartTime))
                trace.MovementStartTime = currentTime;

            string taskId = task != null ? task.GetHashCode().ToString(IOConstants.FORMATTER) : "";
            string botId = bot != null ? bot.GetIdentfierString() : "";
            UpdateSlowStartHoldingRows(taskId, botId, row =>
            {
                if (double.IsNaN(row.MovementStartTime))
                    row.MovementStartTime = currentTime;
            });
        }

        internal void NotifySlowStartStationQueueArrival(ExtractTask task, Bots.BotNormal bot, double currentTime)
        {
            var trace = FindLatestSlowStartTrace(task, bot);
            if (trace != null && double.IsNaN(trace.QueueArrivalTime))
            {
                trace.QueueArrivalTime = currentTime;
                if (!double.IsNaN(trace.ReleaseTime))
                    trace.ReleaseToQueueTime = currentTime - trace.ReleaseTime;
                if (!double.IsNaN(trace.MovementStartTime))
                    trace.MovementTimeToQueue = currentTime - trace.MovementStartTime;
            }

            string taskId = task != null ? task.GetHashCode().ToString(IOConstants.FORMATTER) : "";
            string botId = bot != null ? bot.GetIdentfierString() : "";
            UpdateSlowStartHoldingRows(taskId, botId, row =>
            {
                if (double.IsNaN(row.QueueArrivalTime))
                    row.QueueArrivalTime = currentTime;
                if (!double.IsNaN(row.ReleaseTime))
                    row.ReleaseToQueueTime = currentTime - row.ReleaseTime;
                if (!double.IsNaN(row.MovementStartTime))
                    row.MovementTimeToQueue = currentTime - row.MovementStartTime;
            });
        }

        internal void NotifySlowStartActualArrival(ExtractTask task, Bots.BotNormal bot, double currentTime)
        {
            var trace = FindLatestSlowStartTrace(task, bot);
            if (trace == null || !double.IsNaN(trace.ActualArrivalTime))
                return;

            trace.ActualArrivalTime = currentTime;
            if (!double.IsNaN(trace.ReleaseTime))
                trace.ActualEta = currentTime - trace.ReleaseTime;
            if (!double.IsNaN(trace.EtaNow))
                trace.ArrivalError = currentTime - (trace.Time + trace.ChosenDelay + trace.EtaNow);
            if (!double.IsNaN(trace.QueueArrivalTime))
                trace.QueueWaitAtStation = currentTime - trace.QueueArrivalTime;

            var station = task != null ? task.OutputStation : null;
            if (station != null)
            {
                double blockedUntil = station.GetBlockedUntilTime();
                bool stationIdle = (double.IsNaN(blockedUntil) || blockedUntil <= currentTime) && station.PendingItemRequestCount == 0;
                trace.StationIdleAtArrival = stationIdle ? 1.0 : 0.0;
            }

            string taskId = task != null ? task.GetHashCode().ToString(IOConstants.FORMATTER) : "";
            string botId = bot != null ? bot.GetIdentfierString() : "";
            UpdateSlowStartHoldingRows(taskId, botId, row =>
            {
                if (double.IsNaN(row.ActualArrivalTime))
                    row.ActualArrivalTime = currentTime;
                if (!double.IsNaN(row.ReleaseTime))
                    row.ActualEta = currentTime - row.ReleaseTime;
                if (!double.IsNaN(row.DecisionTime) && !double.IsNaN(row.HoldDelay) && !double.IsNaN(row.Travel))
                    row.ArrivalError = currentTime - (row.DecisionTime + row.HoldDelay + row.Lift + row.Travel);
                if (!double.IsNaN(row.QueueArrivalTime))
                    row.QueueWaitAtStation = currentTime - row.QueueArrivalTime;
                row.StationIdleAtArrival = trace.StationIdleAtArrival;
            });
        }

        internal void NotifySlowStartProcessingFinished(ExtractTask task, Bots.BotNormal bot, double currentTime)
        {
            var trace = FindLatestSlowStartTrace(task, bot);
            if (trace != null)
            {
                trace.ProcessingFinishTime = currentTime;
                if (!double.IsNaN(trace.ActualArrivalTime))
                    trace.ProcessingTime = currentTime - trace.ActualArrivalTime;
            }

            string taskId = task != null ? task.GetHashCode().ToString(IOConstants.FORMATTER) : "";
            string botId = bot != null ? bot.GetIdentfierString() : "";
            UpdateSlowStartHoldingRows(taskId, botId, row =>
            {
                row.ProcessingFinishTime = currentTime;
                if (!double.IsNaN(row.ActualArrivalTime))
                    row.ProcessingTime = currentTime - row.ActualArrivalTime;
            });
        }

        private SlowStartDecisionTrace FindLatestSlowStartTrace(ExtractTask task, Bots.BotNormal bot)
        {
            string taskId = task != null ? task.GetHashCode().ToString(IOConstants.FORMATTER) : "";
            string botId = bot != null ? bot.GetIdentfierString() : "";
            for (int i = StatSlowStartDecisionTraces.Count - 1; i >= 0; i--)
            {
                var trace = StatSlowStartDecisionTraces[i];
                if (trace.TaskId == taskId && trace.BotId == botId)
                    return trace;
            }
            return null;
        }

        internal void NotifySlowStartHoldingDecision(
            OutputStation station,
            Bots.BotNormal bot,
            ExtractTask task,
            double currentTime,
            double stationEst,
            double stationWorkHorizon,
            double stationStarvationGap,
            int stationLateJobs,
            int stationUncertainJobs,
            double buffer,
            double lift,
            double travel,
            double value,
            double proc,
            double releaseBudget,
            double budget,
            double holdDelay,
            double deadline,
            bool feasible,
            bool chosen,
            bool chosenReleaseNow,
            int holderCount,
            int chosenBotId)
        {
            double stationBusyRemaining = 0.0;
            if (station != null)
            {
                double blockedUntil = station.GetBlockedUntilTime();
                if (!double.IsNaN(blockedUntil) && blockedUntil > currentTime)
                    stationBusyRemaining = blockedUntil - currentTime;
            }

            var row = new SlowStartHoldingDecisionTrace
            {
                DecisionTime = currentTime,
                BotId = bot != null ? bot.GetIdentfierString() : "",
                TaskId = task != null ? task.GetHashCode().ToString(IOConstants.FORMATTER) : "",
                PodId = task != null && task.ReservedPod != null ? task.ReservedPod.GetIdentfierString() : "",
                StationId = station != null ? station.GetIdentfierString() : "",
                StationEst = stationEst,
                StationWorkHorizon = stationWorkHorizon,
                StationStarvationGap = stationStarvationGap,
                StationLateJobs = stationLateJobs,
                StationUncertainJobs = stationUncertainJobs,
                Buffer = buffer,
                Lift = lift,
                Travel = travel,
                Value = value,
                ProcessingBudget = proc,
                ReleaseBudget = releaseBudget,
                HoldingBudget = budget,
                HoldDelay = holdDelay,
                Deadline = deadline,
                HoldElapsed = bot != null && !double.IsNaN(bot._slowStartHoldStartTime) ? Math.Max(0.0, currentTime - bot._slowStartHoldStartTime) : double.NaN,
                Feasible = feasible,
                Chosen = chosen,
                ChosenReleaseNow = chosenReleaseNow,
                HolderCount = holderCount,
                ChosenBotId = chosenBotId >= 0 ? chosenBotId.ToString(IOConstants.FORMATTER) : "",
                StationPendingItems = station != null ? station.PendingItemRequestCount : 0,
                StationBusyRemaining = stationBusyRemaining,
                StationQueueWork = EstimateStationQueueWorkForSlowStart(station, bot),
                NeighborCountAtDecision = CountNearbyBots(bot, 2.0),
            };
            StatSlowStartHoldingDecisionTraces.Add(row);
        }

        private void UpdateSlowStartHoldingRows(string taskId, string botId, Action<SlowStartHoldingDecisionTrace> update)
        {
            if (update == null || string.IsNullOrEmpty(taskId) || string.IsNullOrEmpty(botId))
                return;
            foreach (var row in StatSlowStartHoldingDecisionTraces)
            {
                if (row.TaskId == taskId && row.BotId == botId)
                    update(row);
            }
        }

        private double EstimateStationQueueWorkForSlowStart(OutputStation station, Bots.BotNormal self)
        {
            if (station == null)
                return 0.0;
            double work = station.PendingItemRequestCount * station.ItemTransferTime;
            foreach (var task in station.GetActiveExtractTasks())
            {
                if (task == null || task.Requests == null || task.Requests.Count == 0)
                    continue;
                if (self != null && object.ReferenceEquals(task, self.CurrentTask))
                    continue;
                var other = task.Bot as Bots.BotNormal;
                if (other == null || other._isSlowStartHolding)
                    continue;
                if (other.IsQueueing)
                    work += task.Requests.Count * station.ItemTransferTime;
            }
            return work;
        }

        private int CountNearbyBots(Bots.BotNormal bot, double radiusCells)
        {
            if (bot == null || bot.CurrentWaypoint == null)
                return 0;
            double radiusSquared = radiusCells * radiusCells;
            int count = 0;
            foreach (var other in Bots.OfType<Bots.BotNormal>())
            {
                if (object.ReferenceEquals(other, bot) || other.CurrentWaypoint == null)
                    continue;
                double dx = other.CurrentWaypoint.X - bot.CurrentWaypoint.X;
                double dy = other.CurrentWaypoint.Y - bot.CurrentWaypoint.Y;
                if (dx * dx + dy * dy <= radiusSquared)
                    count++;
            }
            return count;
        }

        public void WriteSlowStartDecisionTrace(TextWriter writer)
        {
            writer.WriteLine(SlowStartDecisionTrace.GetHeader());
            foreach (var trace in StatSlowStartDecisionTraces)
                writer.WriteLine(trace.GetLine());
        }

        public void WriteSlowStartHoldingDecisionTrace(TextWriter writer)
        {
            writer.WriteLine(SlowStartHoldingDecisionTrace.GetHeader());
            foreach (var trace in StatSlowStartHoldingDecisionTraces)
                writer.WriteLine(trace.GetLine());
        }

        /// <summary>
        /// Writes basic statistics about the stations in a CSV manner.
        /// </summary>
        /// <param name="writer">The write action to use.</param>
        public void WriteStationStatistics(Action<string> writer)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(
                "Ident" + IOConstants.DELIMITER_VALUE +
                "X" + IOConstants.DELIMITER_VALUE +
                "Y" + IOConstants.DELIMITER_VALUE +
                "Transfers" + IOConstants.DELIMITER_VALUE +
                "InjectedTransfers" + IOConstants.DELIMITER_VALUE +
                "PodsHandled" + IOConstants.DELIMITER_VALUE +
                "PodHandlingTimeAvg" + IOConstants.DELIMITER_VALUE +
                "PodHandlingTimeVar" + IOConstants.DELIMITER_VALUE +
                "PodHandlingTimeMin" + IOConstants.DELIMITER_VALUE +
                "PodHandlingTimeMax" + IOConstants.DELIMITER_VALUE +
                "PileOn" + IOConstants.DELIMITER_VALUE +
                "IdleTime" + IOConstants.DELIMITER_VALUE +
                "UpTime");
            foreach (var station in InputStations)
            {
                sb.AppendLine(
                    station.GetIdentfierString() + IOConstants.DELIMITER_VALUE +
                    station.X.ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.Y.ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatNumBundlesStored.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatNumInjectedBundlesStored.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatPodsHandled.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeAvg.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeVar.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeMin.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeMax.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatBundlePileOn.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatIdleTime.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatActiveTime.ToString(IOConstants.FORMATTER));
            }
            foreach (var station in OutputStations)
            {
                sb.AppendLine(
                    station.GetIdentfierString() + IOConstants.DELIMITER_VALUE +
                    station.X.ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.Y.ToString(IOConstants.EXPORT_FORMAT_SHORT, IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatNumItemsPicked.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatNumInjectedItemsPicked.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatPodsHandled.ToString() + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeAvg.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeVar.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeMin.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatPodHandlingTimeMax.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatItemPileOn.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatIdleTime.ToString(IOConstants.FORMATTER) + IOConstants.DELIMITER_VALUE +
                    station.StatActiveTime.ToString(IOConstants.FORMATTER));
            }
            writer(sb.ToString());
        }

        /// <summary>
        /// Writes basic information about the SKUs and how they were ordered during simulation.
        /// </summary>
        /// <param name="writer">The writer to use.</param>
        public void WriteItemDescriptionStatistics(Action<string> writer)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(IOConstants.COMMENT_LINE + ItemDescriptionFrequencyDatapoint.GetHeader());
            // Output all item descriptions
            foreach (var itemDescription in ItemDescriptions.OrderBy(i => i.ID))
                sb.AppendLine(new ItemDescriptionFrequencyDatapoint(itemDescription, FrequencyTracker).GetLine());
            // Write it
            writer(sb.ToString());
        }

        /// <summary>
        /// Prints a statistics overview in readable format to a specified action.
        /// </summary>
        /// <param name="writer">The action to print the statistics to.</param>
        /// <param name="detailedAll">Indicates whether to print detailed or short statistics.</param>
        /// <param name="detailedBots">Indicates whether to print detailed statistics about the bots.</param>
        /// <param name="detailedPods">Indicates whether to print detailed statistics about the pods.</param>
        /// <param name="detailedStations">Indicates whether to print detailed statistics about the stations.</param>
        public void PrintStatistics(Action<string> writer, bool detailedAll = false, bool detailedBots = false, bool detailedPods = false, bool detailedStations = false)
        {
            StringBuilder sb = new StringBuilder();
            if (detailedAll || detailedBots)
            {
                sb.AppendLine(">>> Bots");
                foreach (var bot in Bots)
                {
                    sb.AppendLine(bot.ToString() + ":");
                    sb.AppendLine("DistanceTraveled: " + bot.StatDistanceTraveled);
                    sb.AppendLine("DistanceEstimated: " + bot.StatDistanceEstimated);
                    sb.AppendLine("NumberOfPickups: " + bot.StatNumberOfPickups);
                    sb.AppendLine("NumberOfSetdowns: " + bot.StatNumberOfSetdowns);
                    sb.AppendLine("NumCollisions: " + bot.StatNumCollisions);
                    sb.AppendLine("TotalTimeMoving: " + bot.StatTotalTimeMoving.ToString(IOConstants.FORMATTER));
                    //sb.AppendLine("TaskStartTime: " + bot.StatTaskStartTime);
                    foreach (var taskType in Enum.GetValues(typeof(BotTaskType)).Cast<BotTaskType>())
                        if (bot.StatTotalTaskTimes.ContainsKey(taskType))
                            sb.AppendLine(taskType + ": " + bot.StatTotalTaskTimes[taskType].ToString(IOConstants.FORMATTER));
                    foreach (var stateType in Enum.GetValues(typeof(BotStateType)).Cast<BotStateType>())
                        if (bot.StatTotalStateTimes.ContainsKey(stateType))
                            sb.AppendLine(stateType + ": " + bot.StatTotalStateTimes[stateType].ToString(IOConstants.FORMATTER));
                    foreach (var stateType in Enum.GetValues(typeof(BotStateType)).Cast<BotStateType>())
                        if (bot.StatTotalStateCounts.ContainsKey(stateType))
                            sb.AppendLine(stateType + "_Count: " + bot.StatTotalStateCounts[stateType]);
                    // PP-aware slow-start dedicated counters
                    if (bot is Bots.BotNormal bn)
                    {
                        sb.AppendLine("StatSlowStartHoldTimeSec: " + bn.StatSlowStartHoldTimeSec.ToString(IOConstants.FORMATTER));
                        sb.AppendLine("StatSlowStartHoldEnergyJ: " + bn.StatSlowStartHoldEnergyJ.ToString(IOConstants.FORMATTER));
                        sb.AppendLine("StatSlowStartDecisionCount: " + bn.StatSlowStartDecisionCount);
                        sb.AppendLine("StatSlowStartImmediateReleaseCount: " + bn.StatSlowStartImmediateReleaseCount);
                        sb.AppendLine("StatSlowStartSearchFailures: " + bn.StatSlowStartSearchFailures);
                        sb.AppendLine("StatSlowStartExpectedArrivalMissingCount: " + bn.StatSlowStartExpectedArrivalMissingCount);
                    }
                }
            }
            if (detailedAll || detailedPods)
            {
                sb.AppendLine(">>> Pods");
                foreach (var pod in Pods)
                {
                    sb.AppendLine(pod.ToString() + ":");
                    string itemDescriptions = "";
                    foreach (var description in pod.ItemDescriptionsContained)
                        itemDescriptions += description.ToString() + " ";
                    sb.AppendLine(itemDescriptions);
                    sb.AppendLine("StatItemsHandledAtOutputStations: " + pod.StatItemsHandled);
                    sb.AppendLine("StatBundlesHandledAtInputStations: " + pod.StatBundlesHandled);
                }
            }
            if (detailedAll || detailedStations)
            {
                sb.AppendLine(">>> InputStations");
                foreach (var iStation in InputStations)
                {
                    sb.AppendLine(iStation.ToString() + ":");
                    sb.AppendLine("IdleTime: " + iStation.StatIdleTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("UpTime: " + iStation.StatActiveTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("DownTime: " + iStation.StatDownTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("NumBundlesPut: " + iStation.StatNumBundlesStored);
                    sb.AppendLine("NumInjectedBundlesPut: " + iStation.StatNumInjectedBundlesStored);
                    sb.AppendLine("BundlePileOn: " + iStation.StatBundlePileOn.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodsHandled: " + iStation.StatPodsHandled.ToString());
                    sb.AppendLine("PodHandlingTimeAvg: " + iStation.StatPodHandlingTimeAvg.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeVar: " + iStation.StatPodHandlingTimeVar.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeMin: " + iStation.StatPodHandlingTimeMin.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeMax: " + iStation.StatPodHandlingTimeMax.ToString(IOConstants.FORMATTER));
                }
                sb.AppendLine(">>> OutputStations");
                foreach (var oStation in OutputStations)
                {
                    sb.AppendLine(oStation.ToString() + ":");
                    sb.AppendLine("IdleTime: " + oStation.StatIdleTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("UpTime: " + oStation.StatActiveTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("DownTime: " + oStation.StatDownTime.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("NumItemsPicked: " + oStation.StatNumItemsPicked);
                    sb.AppendLine("NumInjectedItemsPicked: " + oStation.StatNumInjectedItemsPicked);
                    sb.AppendLine("NumOrdersFinished: " + oStation.StatNumOrdersFinished);
                    sb.AppendLine("ItemPileOn: " + oStation.StatItemPileOn.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("OrderPileOn: " + oStation.StatOrderPileOn.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodsHandled: " + oStation.StatPodsHandled.ToString());
                    sb.AppendLine("PodHandlingTimeAvg: " + oStation.StatPodHandlingTimeAvg.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeVar: " + oStation.StatPodHandlingTimeVar.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeMin: " + oStation.StatPodHandlingTimeMin.ToString(IOConstants.FORMATTER));
                    sb.AppendLine("PodHandlingTimeMax: " + oStation.StatPodHandlingTimeMax.ToString(IOConstants.FORMATTER));
                }
            }
            sb.AppendLine(">>> Timings");
            sb.AppendLine("StatTimingPathPlanningAverage: " + Observer.TimingPathPlanningAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingPathPlanningOverall: " + Observer.TimingPathPlanningOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingPathPlanningCount: " + Observer.TimingPathPlanningDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingTaskAllocationAverage: " + Observer.TimingTaskAllocationAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingTaskAllocationOverall: " + Observer.TimingTaskAllocationOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingTaskAllocationCount: " + Observer.TimingTaskAllocationDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingItemStorageAverage: " + Observer.TimingItemStorageAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingItemStorageOverall: " + Observer.TimingItemStorageOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingItemStorageCount: " + Observer.TimingItemStorageDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingPodStorageAverage: " + Observer.TimingPodStorageAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingPodStorageOverall: " + Observer.TimingPodStorageOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingPodStorageCount: " + Observer.TimingPodStorageDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingReplenishmentBatchingAverage: " + Observer.TimingReplenishmentBatchingAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingReplenishmentBatchingOverall: " + Observer.TimingReplenishmentBatchingOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingReplenishmentBatchingCount: " + Observer.TimingReplenishmentBatchingDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingOrderBatchingAverage: " + Observer.TimingOrderBatchingAverage.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingOrderBatchingOverall: " + Observer.TimingOrderBatchingOverall.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimingOrderBatchingCount: " + Observer.TimingOrderBatchingDecisionCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine(">>> SlowStartProbeDiagnostics");
            var pT = typeof(RAWSimO.Core.Control.Defaults.PathPlanning.WHCAnStarPathManager);
            sb.AppendLine("s_ProbeAttempts: " + pT.GetField("s_ProbeAttempts").GetValue(null));
            sb.AppendLine("s_ProbeEarlyExit: " + pT.GetField("s_ProbeEarlyExit").GetValue(null));
            sb.AppendLine("s_ProbeFromEqualsTo: " + pT.GetField("s_ProbeFromEqualsTo").GetValue(null));
            sb.AppendLine("s_ProbeSelfListNull: " + pT.GetField("s_ProbeSelfListNull").GetValue(null));
            sb.AppendLine("s_ProbeSelfListEmpty: " + pT.GetField("s_ProbeSelfListEmpty").GetValue(null));
            sb.AppendLine("s_ProbeSelfLastEndNotInf: " + pT.GetField("s_ProbeSelfLastEndNotInf").GetValue(null));
            sb.AppendLine("s_ProbeSelfLastNodeMismatch: " + pT.GetField("s_ProbeSelfLastNodeMismatch").GetValue(null));
            sb.AppendLine("s_ProbeSelfRemovedOk: " + pT.GetField("s_ProbeSelfRemovedOk").GetValue(null));
            sb.AppendLine("s_ProbeSearchSuccess: " + pT.GetField("s_ProbeSearchSuccess").GetValue(null));
            sb.AppendLine("s_ProbeSearchFalse: " + pT.GetField("s_ProbeSearchFalse").GetValue(null));
            sb.AppendLine("s_ProbeGoalInvalid: " + pT.GetField("s_ProbeGoalInvalid").GetValue(null));
            sb.AppendLine("s_ProbeArrivalInf: " + pT.GetField("s_ProbeArrivalInf").GetValue(null));
            sb.AppendLine("s_ProbeException: " + pT.GetField("s_ProbeException").GetValue(null));
            sb.AppendLine("s_ProbeStartWaitOk: " + pT.GetField("s_ProbeStartWaitOk").GetValue(null));
            sb.AppendLine("s_ProbeStartWaitBlocked: " + pT.GetField("s_ProbeStartWaitBlocked").GetValue(null));
            sb.AppendLine("s_ProbeAnyMoveGenerated: " + pT.GetField("s_ProbeAnyMoveGenerated").GetValue(null));
            sb.AppendLine("s_ProbeOnlyWaits: " + pT.GetField("s_ProbeOnlyWaits").GetValue(null));
            sb.AppendLine("s_ProbeGoalIsDest: " + pT.GetField("s_ProbeGoalIsDest").GetValue(null));
            sb.AppendLine("s_ProbeGoalIsWindowExpiry: " + pT.GetField("s_ProbeGoalIsWindowExpiry").GetValue(null));
            sb.AppendLine("s_ProbeMaxNodeTimeSumMs: " + pT.GetField("s_ProbeMaxNodeTimeSumMs").GetValue(null));
            sb.AppendLine("s_ProbeMaxNodeTimeSamples: " + pT.GetField("s_ProbeMaxNodeTimeSamples").GetValue(null));
            sb.AppendLine(">>> Overall");
            sb.AppendLine("StatOverallBundlesPlaced: " + StatOverallBundlesPlaced);
            sb.AppendLine("StatOverallItemsOrdered: " + StatOverallItemsOrdered);
            sb.AppendLine("StatOverallOrdersPlaced: " + StatOverallOrdersPlaced);
            sb.AppendLine("StatOverallBundlesRejected: " + StatOverallBundlesRejected);
            sb.AppendLine("StatOverallOrdersRejected: " + StatOverallOrdersRejected);
            sb.AppendLine("StatOverallBundlesHandled: " + StatOverallBundlesHandled);
            sb.AppendLine("StatOverallItemsHandled: " + StatOverallItemsHandled);
            sb.AppendLine("StatOverallLinesHandled: " + StatOverallLinesHandled);
            sb.AppendLine("StatOverallOrdersHandled: " + StatOverallOrdersHandled);
            sb.AppendLine("StatOverallOrdersLate: " + StatOverallOrdersLate);
            sb.AppendLine("StatOrdersLateRate: " + (StatOverallOrdersHandled > 0 ? ((double)StatOverallOrdersLate / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            var _lateOnlyTimes = _statOrderLatenessTimes.Where(t => t > 0).ToList();
            sb.AppendLine("StatAverageLatenessSec: " + ((_lateOnlyTimes.Count == 0) ? "0" : _lateOnlyTimes.Average().ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatMaxLatenessSec: " + ((_lateOnlyTimes.Count == 0) ? "0" : _lateOnlyTimes.Max().ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatThroughputOrdersPerHour: " + StatThroughputOrdersPerHour.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatOrderDistanceM: " + StatOrderDistanceM.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatInputStationArrivals: " + StatOverallInputStationArrivals);
            sb.AppendLine("StatOutputStationArrivals: " + StatOverallOutputStationArrivals);
            sb.AppendLine("StatStationArrivalsTotal: " + StatOverallStationArrivals);
            sb.AppendLine("StatSystemOrderPileOn: " + StatSystemOrderPileOn.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatOverallCollisions: " + StatOverallCollisions);
            sb.AppendLine("StatOverallDistanceTraveled: " + StatOverallDistanceTraveled.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatOverallDistanceEstimated: " + StatOverallDistanceEstimated.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatOverallAssignedTasks: " + StatOverallAssignedTasks);
            sb.AppendLine("StatMaxMemoryUsed: " + StatMaxMemoryUsed);
            sb.AppendLine("StatRealTimeUsed: " + ((SettingConfig.StartTime != default(DateTime) && SettingConfig.StopTime != default(DateTime)) ? (SettingConfig.StopTime - SettingConfig.StartTime).TotalSeconds.ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatAverageTurnoverTime: " + ((_statOrderTurnoverTimes.Count == 0) ? "0" : _statOrderTurnoverTimes.Average().ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatMedianTurnoverTime: " + ((_statOrderTurnoverTimes.Count == 0) ? "0" : StatisticsHelper.GetMedian(_statOrderTurnoverTimes).ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatLowerQuartileTurnoverTime: " + ((_statOrderTurnoverTimes.Count == 0) ? "0" : StatisticsHelper.GetLowerQuartile(_statOrderTurnoverTimes).ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatUpperQuartileTurnoverTime: " + ((_statOrderTurnoverTimes.Count == 0) ? "0" : StatisticsHelper.GetUpperQuartile(_statOrderTurnoverTimes).ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatAverageThroughputTime: " + ((_statOrderThroughputTimes.Count == 0) ? "0" : _statOrderThroughputTimes.Average().ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatMedianThroughputTime: " + ((_statOrderThroughputTimes.Count == 0) ? "0" : StatisticsHelper.GetMedian(_statOrderThroughputTimes).ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatLowerQuartileThroughputTime: " + ((_statOrderThroughputTimes.Count == 0) ? "0" : StatisticsHelper.GetLowerQuartile(_statOrderThroughputTimes).ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatUpperQuartileThroughputTime: " + ((_statOrderThroughputTimes.Count == 0) ? "0" : StatisticsHelper.GetUpperQuartile(_statOrderThroughputTimes).ToString(IOConstants.FORMATTER)));
            // KPI Summary (5 core metrics)
            sb.AppendLine(">>> KPI Summary");
            sb.AppendLine("KPI_TP: " + StatThroughputOrdersPerHour.ToString(IOConstants.FORMATTER));
            sb.AppendLine("KPI_PO: " + StatSystemOrderPileOn.ToString(IOConstants.FORMATTER));
            sb.AppendLine("KPI_RD: " + StatOverallDistanceTraveled.ToString(IOConstants.FORMATTER));
            sb.AppendLine("KPI_OD: " + (StatOverallOrdersHandled > 0 ? (StatOverallDistanceTraveled / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("KPI_EOR: " + (StatOverallOrdersHandled > 0 ? (StatOverallEnergyTotalJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("KPI_LATE: " + StatOverallOrdersLate);
            // Energy statistics (Rizqi model)
            sb.AppendLine(">>> Energy (Rizqi model)");
            sb.AppendLine("EnergyRobotMassKg: " + Metrics.EnergyConsumption.ROBOT_MASS.ToString(IOConstants.FORMATTER));
            sb.AppendLine("EnergySupportPowerEmptyW: " + Metrics.EnergyConsumption.SUPPORT_POWER_EMPTY.ToString(IOConstants.FORMATTER));
            sb.AppendLine("EnergySupportPowerLoadedW: " + Metrics.EnergyConsumption.SUPPORT_POWER_LOADED.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyTotalKJ: " + (StatOverallEnergyTotalJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyE1AccelKJ: " + (StatOverallEnergyE1J / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyE2DecelKJ: " + (StatOverallEnergyE2J / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyE3CruiseKJ: " + (StatOverallEnergyE3J / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyE4RotationKJ: " + (StatOverallEnergyE4J / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyE5LiftLowerKJ: " + (StatOverallEnergyE5J / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyPerOrderKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallEnergyTotalJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatEnergyPerMeterJoule: " + (StatOverallDistanceTraveledRizqi > 0 ? (StatOverallEnergyTotalJ / StatOverallDistanceTraveledRizqi).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatEnergySupportKJ: " + (StatOverallEnergySupportJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyTotalWithSupportKJ: " + (StatOverallEnergyTotalWithSupportJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEnergyPerOrderWithSupportKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallEnergyTotalWithSupportJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatDistanceTraveledRizqiM: " + StatOverallDistanceTraveledRizqi.ToString(IOConstants.FORMATTER));
            sb.AppendLine(">>> Motion Behavior");
            sb.AppendLine("StatTurningCount: " + StatOverallTurningCount);
            sb.AppendLine("StatLoadedDistanceM: " + StatOverallLoadedDistanceM.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatLoadedTurningCount: " + StatOverallLoadedTurningCount);
            sb.AppendLine("StatEmptyTurningCount: " + StatOverallEmptyTurningCount);
            sb.AppendLine("StatWaitTimeSec: " + StatOverallWaitTimeSec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatPathPlanningTimeouts: " + StatOverallPathPlanningTimeouts);
            // ── Pref calibration output (event-level, move/turn split) ───────────
            sb.AppendLine("StatMoveEnergyEmptyKJ: "  + (StatOverallMoveEnergyEmptyJ  / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatMoveTimeEmptySec: "   + StatOverallMoveTimeEmptySec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTurnEnergyEmptyKJ: "  + (StatOverallTurnEnergyEmptyJ  / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTurnTimeEmptySec: "   + StatOverallTurnTimeEmptySec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatMoveEnergyLoadedKJ: " + (StatOverallMoveEnergyLoadedJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatMoveTimeLoadedSec: "  + StatOverallMoveTimeLoadedSec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTurnEnergyLoadedKJ: " + (StatOverallTurnEnergyLoadedJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTurnTimeLoadedSec: "  + StatOverallTurnTimeLoadedSec.ToString(IOConstants.FORMATTER));
            // Pref = (moveE + turnE) / (moveT + turnT)  — power during active motion only
            double prefEmpty  = (StatOverallMoveTimeEmptySec  + StatOverallTurnTimeEmptySec)  > 0
                ? (StatOverallMoveEnergyEmptyJ  + StatOverallTurnEnergyEmptyJ)  / (StatOverallMoveTimeEmptySec  + StatOverallTurnTimeEmptySec)  : 0.0;
            double prefLoaded = (StatOverallMoveTimeLoadedSec + StatOverallTurnTimeLoadedSec) > 0
                ? (StatOverallMoveEnergyLoadedJ + StatOverallTurnEnergyLoadedJ) / (StatOverallMoveTimeLoadedSec + StatOverallTurnTimeLoadedSec) : 0.0;
            sb.AppendLine("StatPrefEmptyW: "  + prefEmpty.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatPrefLoadedW: " + prefLoaded.ToString(IOConstants.FORMATTER));
            // ── Per-trip wait ratio statistics ───────────────
            var totalPickupCount = Bots.OfType<Bots.BotNormal>().Sum(b => b.StatPickupCount);
            var totalSetdownCount = Bots.OfType<Bots.BotNormal>().Sum(b => b.StatSetdownCount);
            var totalWaitRatiosEmptyCount = Bots.OfType<Bots.BotNormal>().Sum(b => b.PerTripWaitRatioEmpty.Count);
            var totalWaitRatiosLoadedCount = Bots.OfType<Bots.BotNormal>().Sum(b => b.PerTripWaitRatioLoaded.Count);
            sb.AppendLine("StatPickupCount: " + totalPickupCount);
            sb.AppendLine("StatSetdownCount: " + totalSetdownCount);
            sb.AppendLine("PerTripWaitRatioEmptyCount: " + totalWaitRatiosEmptyCount);
            sb.AppendLine("PerTripWaitRatioLoadedCount: " + totalWaitRatiosLoadedCount);
            sb.AppendLine("StatTripCountLoaded: " + StatOverallTripCountLoaded);
            sb.AppendLine("StatTripCountEmpty: " + StatOverallTripCountEmpty);
            // ── Per-robot diagnostics ───────────────
            sb.AppendLine(">>> Per-Robot Trip Counts");
            foreach (var bot in Bots.OfType<Bots.BotNormal>().OrderBy(b => b.ID))
            {
                sb.AppendLine($"  Bot{bot.ID}: pickupCount={bot.StatPickupCount}, tripCountEmpty={bot.StatTripCountEmpty}, setdownCount={bot.StatSetdownCount}, tripCountLoaded={bot.StatTripCountLoaded}, ordersCompleted={bot.StatOrdersCompleted}");
            }
            // Aggregate all per-trip wait ratios from all bots
            var allWaitRatiosLoaded = Bots.OfType<Bots.BotNormal>()
                .SelectMany(b => b.PerTripWaitRatioLoaded).ToList();
            if (allWaitRatiosLoaded.Count > 0)
            {
                allWaitRatiosLoaded.Sort();
                double meanLoaded = allWaitRatiosLoaded.Average();
                double medianLoaded = Percentile(allWaitRatiosLoaded, 0.50);
                double p95Loaded = Percentile(allWaitRatiosLoaded, 0.95);
                sb.AppendLine("StatTripWaitRatioLoadedMean: " + meanLoaded.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatTripWaitRatioLoadedMedian: " + medianLoaded.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatTripWaitRatioLoadedP95: " + p95Loaded.ToString(IOConstants.FORMATTER));
            }
            var allWaitRatiosEmpty = Bots.OfType<Bots.BotNormal>()
                .SelectMany(b => b.PerTripWaitRatioEmpty).ToList();
            if (allWaitRatiosEmpty.Count > 0)
            {
                allWaitRatiosEmpty.Sort();
                double meanEmpty = allWaitRatiosEmpty.Average();
                double medianEmpty = Percentile(allWaitRatiosEmpty, 0.50);
                double p95Empty = Percentile(allWaitRatiosEmpty, 0.95);
                sb.AppendLine("StatTripWaitRatioEmptyMean: " + meanEmpty.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatTripWaitRatioEmptyMedian: " + medianEmpty.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatTripWaitRatioEmptyP95: " + p95Empty.ToString(IOConstants.FORMATTER));
            }
            sb.AppendLine(">>> Support & Utilization");
            int botCount = Bots.OfType<Bots.BotNormal>().Count();
            double utilization = (StatTime > 0 && botCount > 0)
                ? 1.0 - (StatOverallTimeIdleSec / (StatTime * botCount))
                : double.NaN;
            // E_support = always-on background energy (SupportPower(Pod) x wall-clock time;
            //             configured by payload state; includes idle/moving/waiting, no task gate)
            sb.AppendLine("StatESupportKJ: " + (StatOverallESupportJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatESupportPerOrderKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallESupportJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            // E_wait = congestion-wait subset of E_support (stationary with active task)
            sb.AppendLine("StatEWaitKJ: " + (StatOverallEWaitJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEWaitLoadedKJ: " + (StatOverallEWaitLoadedJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEWaitEmptyKJ: " + (StatOverallEWaitEmptyJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEWaitPerOrderKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallEWaitJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            // E_queueing_at_station = premature-arrival waste (bot in station queue zone, not yet picking)
            sb.AppendLine("StatQueueingAtStationTimeSec: " + StatOverallQueueingAtStationTimeSec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEQueueingAtStationKJ: " + (StatOverallEQueueingAtStationJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatEQueueingAtStationPerOrderKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallEQueueingAtStationJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            double queueingShareOfSupport = StatOverallESupportJ > 0
                ? StatOverallEQueueingAtStationJ / StatOverallESupportJ : double.NaN;
            sb.AppendLine("StatEQueueingShareOfSupport: " + (double.IsNaN(queueingShareOfSupport) ? "NaN" : queueingShareOfSupport.ToString(IOConstants.FORMATTER)));

            // stop_and_go_* = WHCA*/reservation-table planned waits that split straight travel.
            // queue_stop_and_go_* = QueueManager creep inside station queue zones.
            // Energy is E2 into the stop + E1 out of the stop (re-classification; not additive to total).
            sb.AppendLine("StatStopAndGoCount: " + StatOverallStopAndGoCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatStopAndGoEnergyKJ: " + (StatOverallStopAndGoEnergyJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatStopAndGoCountPerOrder: " + (StatOverallOrdersHandled > 0 ? ((double)StatOverallStopAndGoCount / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatStopAndGoEnergyPerOrderKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallStopAndGoEnergyJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatQueueStopAndGoCount: " + StatOverallQueueStopAndGoCount.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatQueueStopAndGoEnergyKJ: " + (StatOverallQueueStopAndGoEnergyJ / 1000.0).ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatQueueStopAndGoCountPerOrder: " + (StatOverallOrdersHandled > 0 ? ((double)StatOverallQueueStopAndGoCount / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
            sb.AppendLine("StatQueueStopAndGoEnergyPerOrderKJ: " + (StatOverallOrdersHandled > 0 ? (StatOverallQueueStopAndGoEnergyJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));

            // ─── Backfill-potential probe (deferred-binding + opportunistic backfill hit rate) ───
            sb.AppendLine("StatBackfillProbeRowCount: " + StatBackfillProbeRows.Count.ToString(IOConstants.FORMATTER));
            if (StatBackfillProbeRows.Count > 0 && Directory.Exists(SettingConfig.StatisticsDirectory))
            {
                string backfillCsvPath = Path.Combine(SettingConfig.StatisticsDirectory, "backfill_probe.csv");
                using (var sw = new StreamWriter(backfillCsvPath))
                {
                    sw.WriteLine("time_sec;station_id;cap_in_use;cap;free_slot;other_present_pods;inbound_pods;pod_id;pod_remaining_units;assigned_match;backlog_size;full_match_orders;partial_match_orders;max_match_units;best_full_demand");
                    foreach (var row in StatBackfillProbeRows)
                        sw.WriteLine(row);
                }
            }

            if (StatStopGoEventRows.Count > 0 && Directory.Exists(SettingConfig.StatisticsDirectory))
            {
                using (var sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, "stop_and_go_events.csv")))
                {
                    sw.WriteLine("time_sec;type;bot_id;from_node;stop_node;to_node;x;y;tier;energy_kJ;decel_kJ;accel_kJ;queue_terminal_node;destination_node;loaded");
                    foreach (var row in StatStopGoEventRows)
                        sw.WriteLine(row);
                }
            }

            if (StatConflictWaitHeatPoints.Count > 0 && Directory.Exists(SettingConfig.StatisticsDirectory))
            {
                using (var sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, "conflictwait.heat")))
                {
                    sw.WriteLine(LocationDatapoint.GetCSVHeader());
                    foreach (var point in StatConflictWaitHeatPoints)
                        sw.WriteLine(point.ToCSV());
                }
            }

            // ─── Input-station per-pod queue wait (previously unmeasured) ───
            int inWaitN = StatInputPodQueueWaitSamples.Count;
            sb.AppendLine("StatInputPodQueueWaitSampleCount: " + inWaitN.ToString(IOConstants.FORMATTER));
            if (inWaitN > 0)
            {
                var sorted = StatInputPodQueueWaitSamples.OrderBy(v => v).ToList();
                double mean = sorted.Average();
                double p50 = sorted[(int)(0.50 * (inWaitN - 1))];
                double p95 = sorted[(int)(0.95 * (inWaitN - 1))];
                sb.AppendLine("StatInputPodQueueWaitMeanSec: " + mean.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatInputPodQueueWaitP50Sec: " + p50.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatInputPodQueueWaitP95Sec: " + p95.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatInputPodQueueWaitMaxSec: " + sorted[inWaitN - 1].ToString(IOConstants.FORMATTER));
            }

            // ─── Input slow-start release events (synchronized-release detection) ───
            if (StatInputReleaseRows.Count > 0 && Directory.Exists(SettingConfig.StatisticsDirectory))
            {
                using (var sw = new StreamWriter(Path.Combine(SettingConfig.StatisticsDirectory, "input_release_probe.csv")))
                {
                    sw.WriteLine("time_sec;station_id;bot_id;inbound_pods;bundles");
                    foreach (var row in StatInputReleaseRows)
                        sw.WriteLine(row);
                }
            }

            // ─── Input-scheduler diagnostic (input EST under-estimation analysis) ───
            sb.AppendLine("StatInputSchedRowCount: " + StatInputSchedRows.Count.ToString(IOConstants.FORMATTER));
            if (StatInputSchedRows.Count > 0 && Directory.Exists(SettingConfig.StatisticsDirectory))
            {
                string inSchedCsv = Path.Combine(SettingConfig.StatisticsDirectory, "input_sched_probe.csv");
                using (var sw = new StreamWriter(inSchedCsv))
                {
                    sw.WriteLine("time_sec;station_id;holders;queued_pods;intransit_pods;first_starve_sec;work_horizon_sec;cascade_starve_sec;chosen_delay_sec;release_now");
                    foreach (var row in StatInputSchedRows)
                        sw.WriteLine(row);
                }
            }

            // ─── JIT ETA validation (ideal kinematic vs measured trip duration) ───
            int jitN = StatJITEtaActualSamples.Count;
            sb.AppendLine("StatJITEtaSampleCount: " + jitN.ToString(IOConstants.FORMATTER));
            // Per-sample CSV dump for diagnostic
            if (jitN > 0 && Directory.Exists(SettingConfig.StatisticsDirectory))
            {
                string csvPath = Path.Combine(SettingConfig.StatisticsDirectory, "jit_eta_samples.csv");
                using (var sw = new StreamWriter(csvPath))
                {
                    sw.WriteLine("idx;bot_id;task_id;trip_id;src_id;dest_id;path_nodes;path_len_m;expected_sec;actual_sec;gap_sec;abs_err_sec;rel_err_pct;stop_go_count;queue_stop_go_count;conflict_wait_sec");
                    for (int i = 0; i < jitN; i++)
                    {
                        double e = StatJITEtaExpectedSamples[i];
                        double a = StatJITEtaActualSamples[i];
                        double gap = a - e;
                        double absErr = Math.Abs(a - e);
                        double rel = e > 1e-9 ? absErr / e * 100.0 : 0.0;
                        int botId = i < StatJITEtaBotIds.Count ? StatJITEtaBotIds[i] : -1;
                        string taskId = i < StatJITEtaTaskIds.Count ? (StatJITEtaTaskIds[i] ?? "").Replace(";", "_") : "";
                        int tripId = i < StatJITEtaTripIds.Count ? StatJITEtaTripIds[i] : -1;
                        int srcId = i < StatJITEtaSourceIds.Count ? StatJITEtaSourceIds[i] : -1;
                        int destId = i < StatJITEtaDestIds.Count ? StatJITEtaDestIds[i] : -1;
                        int nodes = i < StatJITEtaPathNodeCounts.Count ? StatJITEtaPathNodeCounts[i] : 0;
                        double plen = i < StatJITEtaPathLengths.Count ? StatJITEtaPathLengths[i] : 0.0;
                        int stopGo = i < StatJITEtaStopGoCounts.Count ? StatJITEtaStopGoCounts[i] : 0;
                        int queueStopGo = i < StatJITEtaQueueStopGoCounts.Count ? StatJITEtaQueueStopGoCounts[i] : 0;
                        double waitSec = i < StatJITEtaWaitSecs.Count ? StatJITEtaWaitSecs[i] : 0.0;
                        sw.WriteLine(i + ";" + botId + ";" + taskId + ";" + tripId + ";" +
                                     srcId + ";" + destId + ";" + nodes + ";" +
                                     plen.ToString(IOConstants.FORMATTER) + ";" +
                                     e.ToString(IOConstants.FORMATTER) + ";" +
                                     a.ToString(IOConstants.FORMATTER) + ";" +
                                     gap.ToString(IOConstants.FORMATTER) + ";" +
                                     absErr.ToString(IOConstants.FORMATTER) + ";" +
                                     rel.ToString(IOConstants.FORMATTER) + ";" +
                                     stopGo.ToString(IOConstants.FORMATTER) + ";" +
                                     queueStopGo.ToString(IOConstants.FORMATTER) + ";" +
                                     waitSec.ToString(IOConstants.FORMATTER));
                    }
                }
                string botCsvPath = Path.Combine(SettingConfig.StatisticsDirectory, "jit_eta_by_bot.csv");
                using (var sw = new StreamWriter(botCsvPath))
                {
                    sw.WriteLine("bot_id;sample_count;expected_mean_sec;actual_mean_sec;gap_mean_sec;gap_var_sec2;gap_std_sec;abs_err_mean_sec;stop_go_mean;queue_stop_go_mean;conflict_wait_mean_sec");
                    foreach (var group in Enumerable.Range(0, jitN).GroupBy(i => i < StatJITEtaBotIds.Count ? StatJITEtaBotIds[i] : -1).OrderBy(g => g.Key))
                    {
                        int n = group.Count();
                        double sumE = 0.0, sumA = 0.0, sumGap = 0.0, sumGap2 = 0.0, sumAbs = 0.0, sumStop = 0.0, sumQStop = 0.0, sumWait = 0.0;
                        foreach (int i in group)
                        {
                            double e = StatJITEtaExpectedSamples[i];
                            double a = StatJITEtaActualSamples[i];
                            double gap = a - e;
                            sumE += e;
                            sumA += a;
                            sumGap += gap;
                            sumGap2 += gap * gap;
                            sumAbs += Math.Abs(gap);
                            sumStop += i < StatJITEtaStopGoCounts.Count ? StatJITEtaStopGoCounts[i] : 0;
                            sumQStop += i < StatJITEtaQueueStopGoCounts.Count ? StatJITEtaQueueStopGoCounts[i] : 0;
                            sumWait += i < StatJITEtaWaitSecs.Count ? StatJITEtaWaitSecs[i] : 0.0;
                        }
                        double meanGap = sumGap / n;
                        double varGap = Math.Max(0.0, sumGap2 / n - meanGap * meanGap);
                        sw.WriteLine(group.Key.ToString(IOConstants.FORMATTER) + ";" +
                                     n.ToString(IOConstants.FORMATTER) + ";" +
                                     (sumE / n).ToString(IOConstants.FORMATTER) + ";" +
                                     (sumA / n).ToString(IOConstants.FORMATTER) + ";" +
                                     meanGap.ToString(IOConstants.FORMATTER) + ";" +
                                     varGap.ToString(IOConstants.FORMATTER) + ";" +
                                     Math.Sqrt(varGap).ToString(IOConstants.FORMATTER) + ";" +
                                     (sumAbs / n).ToString(IOConstants.FORMATTER) + ";" +
                                     (sumStop / n).ToString(IOConstants.FORMATTER) + ";" +
                                     (sumQStop / n).ToString(IOConstants.FORMATTER) + ";" +
                                     (sumWait / n).ToString(IOConstants.FORMATTER));
                    }
                }
            }
            if (jitN > 0)
            {
                double sumE = 0, sumA = 0, sumSignedErr = 0, sumSignedErr2 = 0, sumAbsErr = 0, sumAbsRel = 0, maxRel = 0;
                double sumStopGo = 0, sumQueueStopGo = 0, sumJitWait = 0;
                for (int i = 0; i < jitN; i++)
                {
                    double e = StatJITEtaExpectedSamples[i];
                    double a = StatJITEtaActualSamples[i];
                    double signedErr = a - e;
                    double absErr = Math.Abs(signedErr);
                    double rel = e > 1e-9 ? absErr / e : 0.0;
                    sumE += e; sumA += a; sumSignedErr += signedErr; sumSignedErr2 += signedErr * signedErr; sumAbsErr += absErr; sumAbsRel += rel;
                    sumStopGo += i < StatJITEtaStopGoCounts.Count ? StatJITEtaStopGoCounts[i] : 0;
                    sumQueueStopGo += i < StatJITEtaQueueStopGoCounts.Count ? StatJITEtaQueueStopGoCounts[i] : 0;
                    sumJitWait += i < StatJITEtaWaitSecs.Count ? StatJITEtaWaitSecs[i] : 0.0;
                    if (rel > maxRel) maxRel = rel;
                }
                double meanSignedErr = sumSignedErr / jitN;
                double varSignedErr = Math.Max(0.0, sumSignedErr2 / jitN - meanSignedErr * meanSignedErr);
                sb.AppendLine("StatJITEtaExpectedMeanSec: " + (sumE / jitN).ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatJITEtaActualMeanSec: " + (sumA / jitN).ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatJITEtaMeanGapSec: " + meanSignedErr.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatJITEtaGapVarianceSec2: " + varSignedErr.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatJITEtaGapStdDevSec: " + Math.Sqrt(varSignedErr).ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatJITEtaMeanAbsErrSec: " + (sumAbsErr / jitN).ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatJITEtaMeanRelErrPct: " + (sumAbsRel / jitN * 100.0).ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatJITEtaMaxRelErrPct: " + (maxRel * 100.0).ToString(IOConstants.FORMATTER));
                double aggregateBiasPct = sumE > 1e-9 ? (sumA - sumE) / sumE * 100.0 : 0.0;
                sb.AppendLine("StatJITEtaAggregateBiasPct: " + aggregateBiasPct.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatJITEtaStopGoMean: " + (sumStopGo / jitN).ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatJITEtaQueueStopGoMean: " + (sumQueueStopGo / jitN).ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatJITEtaConflictWaitMeanSec: " + (sumJitWait / jitN).ToString(IOConstants.FORMATTER));
            }

            if (StatEtaSurrogateActualLegRows.Count > 0 && Directory.Exists(SettingConfig.StatisticsDirectory))
            {
                string surrogateCsvPath = Path.Combine(SettingConfig.StatisticsDirectory, "eta_surrogate_actual_legs.csv");
                using (var sw = new StreamWriter(surrogateCsvPath))
                {
                    sw.WriteLine("kind,visit_id,leg_index,source_scope,bot_id,station_queue_wp_id,from_id,to_id,pod_id,station_id,loaded,orientation_bucket,from_x,from_y,to_x,to_y,abs_dx,abs_dy,euclid,manhattan,same_tier,from_storage,to_storage,from_queue,to_queue,from_degree,to_degree,path_found,path_hops,path_distance,path_turns,path_segments,eta_sec,actual_sec,wait_sec,turn_count,stop_go_count,queue_stop_go_count,trip_start_time,loaded_bot_count,station_inbound_count,local_bot_count_src,local_bot_count_dest,codest_bot_count,station_face_bot_count");
                    foreach (string row in StatEtaSurrogateActualLegRows)
                        sw.WriteLine(row);
                }
                sb.AppendLine("StatEtaSurrogateActualLegSampleCount: " + StatEtaSurrogateActualLegRows.Count.ToString(IOConstants.FORMATTER));
            }

            // ─── Station starvation, split into two mutually-exclusive types ───
            // Type A "supply lag" (optimizable): has order, no pod ready/queued (pod not yet arrived).
            // Type B "no-order waste" (HADGS left station unassigned): no order and no pod.
            double simDuration = SettingConfig.SimulationDuration;
            int nStations = OutputStations.Count;
            sb.AppendLine("StatStationStarvationTimeSec: " + StatOverallStationStarvationTimeSec.ToString(IOConstants.FORMATTER));
            double starvPct = (simDuration > 0 && nStations > 0)
                ? StatOverallStationStarvationTimeSec / (simDuration * nStations) * 100.0
                : 0.0;
            sb.AppendLine("StatStationStarvationPctOfSimXStations: " + starvPct.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatStationNoOrderIdleTimeSec: " + StatOverallStationNoOrderIdleTimeSec.ToString(IOConstants.FORMATTER));
            double noOrderPct = (simDuration > 0 && nStations > 0)
                ? StatOverallStationNoOrderIdleTimeSec / (simDuration * nStations) * 100.0
                : 0.0;
            sb.AppendLine("StatStationNoOrderIdlePctOfSimXStations: " + noOrderPct.ToString(IOConstants.FORMATTER));

            // ─── Diagnostic: per-pod-handoff gap (prev pod finished → next pod first pick) ───
            int hN = StatPodHandoffGapSamples.Count;
            sb.AppendLine("StatPodHandoffGapCount: " + hN.ToString(IOConstants.FORMATTER));
            if (hN > 0)
            {
                double hSum = 0, hMax = 0;
                for (int i = 0; i < hN; i++) { hSum += StatPodHandoffGapSamples[i]; if (StatPodHandoffGapSamples[i] > hMax) hMax = StatPodHandoffGapSamples[i]; }
                var hSorted = new List<double>(StatPodHandoffGapSamples); hSorted.Sort();
                sb.AppendLine("StatPodHandoffGapSumSec: " + hSum.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatPodHandoffGapMeanSec: " + (hSum / hN).ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatPodHandoffGapMaxSec: " + hMax.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatPodHandoffGapP50Sec: " + hSorted[hN / 2].ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatPodHandoffGapP95Sec: " + hSorted[(int)(hN * 0.95)].ToString(IOConstants.FORMATTER));
            }

            // ─── SA-HADGS: candidate commit timeliness ───
            sb.AppendLine("StatSaHadgsOnTimeCommits: " + StatSaHadgsOnTimeCommits.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatSaHadgsLateCommits: " + StatSaHadgsLateCommits.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatSaHadgsLatenessMeanSec: " + (StatSaHadgsLateCommits > 0 ? StatSaHadgsLatenessSumSec / StatSaHadgsLateCommits : 0.0).ToString(IOConstants.FORMATTER));

            // ─── Per-pod queue-wait + picking-time (per pod visit to OS) ───
            int qN = StatPodQueueWaitSamples.Count;
            int pN = StatPodPickingTimeSamples.Count;
            sb.AppendLine("StatPodVisitCount: " + Math.Min(qN, pN).ToString(IOConstants.FORMATTER));
            if (qN > 0)
            {
                double qSum = 0, qMax = 0;
                for (int i = 0; i < qN; i++) { qSum += StatPodQueueWaitSamples[i]; if (StatPodQueueWaitSamples[i] > qMax) qMax = StatPodQueueWaitSamples[i]; }
                var qSorted = new List<double>(StatPodQueueWaitSamples); qSorted.Sort();
                sb.AppendLine("StatPodQueueWaitMeanSec: " + (qSum / qN).ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatPodQueueWaitMaxSec: " + qMax.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatPodQueueWaitP50Sec: " + qSorted[qN / 2].ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatPodQueueWaitP95Sec: " + qSorted[(int)(qN * 0.95)].ToString(IOConstants.FORMATTER));
            }
            if (pN > 0)
            {
                double pSum = 0, pMax = 0;
                for (int i = 0; i < pN; i++) { pSum += StatPodPickingTimeSamples[i]; if (StatPodPickingTimeSamples[i] > pMax) pMax = StatPodPickingTimeSamples[i]; }
                var pSorted = new List<double>(StatPodPickingTimeSamples); pSorted.Sort();
                sb.AppendLine("StatPodPickingTimeMeanSec: " + (pSum / pN).ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatPodPickingTimeMaxSec: " + pMax.ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatPodPickingTimeP50Sec: " + pSorted[pN / 2].ToString(IOConstants.FORMATTER));
                sb.AppendLine("StatPodPickingTimeP95Sec: " + pSorted[(int)(pN * 0.95)].ToString(IOConstants.FORMATTER));
            }
            // Per-pod-visit CSV dump
            if ((qN > 0 || pN > 0) && Directory.Exists(SettingConfig.StatisticsDirectory))
            {
                string podCsv = Path.Combine(SettingConfig.StatisticsDirectory, "pod_visit_metrics.csv");
                int n = Math.Min(qN, pN);
                using (var sw = new StreamWriter(podCsv))
                {
                    sw.WriteLine("idx;queue_wait_sec;picking_time_sec");
                    for (int i = 0; i < n; i++)
                        sw.WriteLine(i + ";" + StatPodQueueWaitSamples[i].ToString(IOConstants.FORMATTER) + ";" +
                                     StatPodPickingTimeSamples[i].ToString(IOConstants.FORMATTER));
                }
            }

            // ─── Diagnostic KPIs for value-vs-distance tradeoff investigation ────────
            // KPI A: orders served per pod-visit — does a single pod typically serve >1 order?
            WritePercentileBlock(sb, "StatPodVisitOrdersServed", StatPodVisitOrdersServedSamples);
            // KPI B: per-station inbound-pod count at decision trigger — queue congestion at OB time
            WritePercentileBlock(sb, "StatDecisionTriggerQueueDepth", StatDecisionTriggerQueueDepthSamples);
            // KPI C: decision-space — how many choices does HADGS face per trigger?
            WritePercentileBlock(sb, "StatDecisionAvailableStationSlots", StatDecisionAvailableStationSlotsSamples);
            WritePercentileBlock(sb, "StatDecisionUnusedPods", StatDecisionUnusedPodsSamples);
            WritePercentileBlock(sb, "StatDecisionPendingOrders", StatDecisionPendingOrdersSamples);
            WritePercentileBlockLong(sb, "StatDecisionCandidateCombos", StatDecisionCandidateCombosSamples);
            // KPI D: bots in Rest / Idle at HADGS trigger — over-supply signal
            WritePercentileBlock(sb, "StatDecisionBotsInRest", StatDecisionBotsInRestSamples);
            WritePercentileBlock(sb, "StatDecisionBotsIdleOrRest", StatDecisionBotsIdleOrRestSamples);
            sb.AppendLine("StatTimeIdleSec: " + StatOverallTimeIdleSec.ToString(IOConstants.FORMATTER));
            sb.AppendLine("StatTimeRestSec: " + StatOverallTimeRestSec.ToString(IOConstants.FORMATTER));
            // ── Layered utilization: Raw vs Productive vs Effective ───────────────────
            // Raw      : 1 − idle/total  (legacy; counts Rest as utilized — INFLATES)
            // Productive: subtracts Rest as well (genuine task-doing fraction)
            // Effective : also subtracts at-station queue + congestion wait
            //             (fraction of bot-time actually advancing throughput)
            sb.AppendLine("StatRobotUtilization: " + (double.IsNaN(StatUtilizationRaw) ? "NaN" : StatUtilizationRaw.ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatRobotUtilizationProductive: " + (double.IsNaN(StatUtilizationProductive) ? "NaN" : StatUtilizationProductive.ToString(IOConstants.FORMATTER)));
            sb.AppendLine("StatRobotUtilizationEffective: " + (double.IsNaN(StatUtilizationEffective) ? "NaN" : StatUtilizationEffective.ToString(IOConstants.FORMATTER)));
            double eWaitToMechRatio = StatOverallEnergyTotalJ > 0
                ? StatOverallEWaitJ / StatOverallEnergyTotalJ : double.NaN;
            sb.AppendLine("StatEWaitToMechRatio: " + (double.IsNaN(eWaitToMechRatio) ? "NaN" : eWaitToMechRatio.ToString(IOConstants.FORMATTER)));
            double eSupportToMechRatio = StatOverallEnergyTotalJ > 0
                ? StatOverallESupportJ / StatOverallEnergyTotalJ : double.NaN;
            sb.AppendLine("StatESupportToMechRatio: " + (double.IsNaN(eSupportToMechRatio) ? "NaN" : eSupportToMechRatio.ToString(IOConstants.FORMATTER)));
            // Write output
            writer(sb.ToString());
        }

        /// <summary>
        /// Writes a CSV KPI report covering the 6-layer metrics framework.
        /// Columns: layer,metric,empty,loaded,total,unit
        /// </summary>
        private static void WritePercentileBlockLong(StringBuilder sb, string prefix, List<long> samples)
        {
            sb.AppendLine(prefix + "_count: " + samples.Count);
            if (samples.Count == 0)
            {
                foreach (var k in new[] { "mean", "p25", "p50", "p75", "p95", "max" })
                    sb.AppendLine(prefix + "_" + k + ": NaN");
                return;
            }
            var sorted = samples.OrderBy(v => v).ToList();
            int n = sorted.Count;
            double Pct(double q)
            {
                double pos = q * (n - 1);
                int lo = (int)Math.Floor(pos);
                int hi = (int)Math.Ceiling(pos);
                if (lo == hi) return sorted[lo];
                return sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
            }
            sb.AppendLine(prefix + "_mean: " + samples.Average().ToString(IOConstants.FORMATTER));
            sb.AppendLine(prefix + "_p25: " + Pct(0.25).ToString(IOConstants.FORMATTER));
            sb.AppendLine(prefix + "_p50: " + Pct(0.50).ToString(IOConstants.FORMATTER));
            sb.AppendLine(prefix + "_p75: " + Pct(0.75).ToString(IOConstants.FORMATTER));
            sb.AppendLine(prefix + "_p95: " + Pct(0.95).ToString(IOConstants.FORMATTER));
            sb.AppendLine(prefix + "_max: " + sorted[n - 1]);
        }

        /// <summary>
        /// Writes count, mean, p25/p50/p75/p95/max for an integer sample list.
        /// Used for diagnostic distribution KPIs (pod-visit orders, queue depth, etc.).
        /// </summary>
        private static void WritePercentileBlock(StringBuilder sb, string prefix, List<int> samples)
        {
            sb.AppendLine(prefix + "_count: " + samples.Count);
            if (samples.Count == 0)
            {
                sb.AppendLine(prefix + "_mean: NaN");
                sb.AppendLine(prefix + "_p25: NaN");
                sb.AppendLine(prefix + "_p50: NaN");
                sb.AppendLine(prefix + "_p75: NaN");
                sb.AppendLine(prefix + "_p95: NaN");
                sb.AppendLine(prefix + "_max: NaN");
                return;
            }
            var sorted = samples.OrderBy(v => v).ToList();
            int n = sorted.Count;
            double Pct(double q)
            {
                double pos = q * (n - 1);
                int lo = (int)Math.Floor(pos);
                int hi = (int)Math.Ceiling(pos);
                if (lo == hi) return sorted[lo];
                return sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
            }
            sb.AppendLine(prefix + "_mean: " + samples.Average().ToString(IOConstants.FORMATTER));
            sb.AppendLine(prefix + "_p25: " + Pct(0.25).ToString(IOConstants.FORMATTER));
            sb.AppendLine(prefix + "_p50: " + Pct(0.50).ToString(IOConstants.FORMATTER));
            sb.AppendLine(prefix + "_p75: " + Pct(0.75).ToString(IOConstants.FORMATTER));
            sb.AppendLine(prefix + "_p95: " + Pct(0.95).ToString(IOConstants.FORMATTER));
            sb.AppendLine(prefix + "_max: " + sorted[n - 1]);
        }

        private void WriteKpiReport(StreamWriter sw)
        {
            var bots = Bots.OfType<Bots.BotNormal>().ToList();
            string fmt(double v) => v.ToString(IOConstants.FORMATTER);
            string row(string layer, string metric, object empty, object loaded, object total, string unit)
                => $"{layer},{metric},{empty},{loaded},{total},{unit}";

            double totalEnergyJ   = StatOverallEnergyTotalWithSupportJ;
            double totalMechJ     = StatOverallEnergyTotalJ;
            double kpiDistM       = StatOverallDistanceTraveled;
            double motionDistM    = StatOverallDistanceTraveledRizqi;
            double totalDistLoad  = StatOverallLoadedDistanceM;
            double totalDistEmpty = StatOverallEmptyDistanceM;
            int    totalOrders    = StatOverallOrdersHandled;
            double durationSec    = StatTime;
            int    tripsLoaded    = StatOverallTripCountLoaded;
            int    tripsEmpty     = StatOverallTripCountEmpty;

            double moveE_L = StatOverallMoveEnergyLoadedJ, moveE_E = StatOverallMoveEnergyEmptyJ;
            double turnE_L = StatOverallTurnEnergyLoadedJ, turnE_E = StatOverallTurnEnergyEmptyJ;
            double waitE_L = StatOverallWaitEnergyLoadedJ,  waitE_E = StatOverallWaitEnergyEmptyJ;

            double moveT_L = StatOverallMoveTimeLoadedSec, moveT_E = StatOverallMoveTimeEmptySec;
            double turnT_L = StatOverallTurnTimeLoadedSec, turnT_E = StatOverallTurnTimeEmptySec;
            double waitT_L = StatOverallWaitTimeLoadedSec, waitT_E = StatOverallWaitTimeEmptySec;

            int tnC_L = StatOverallLoadedTurningCount,   tnC_E = StatOverallEmptyTurningCount;

            sw.WriteLine("layer,metric,empty,loaded,total,unit");

            // Layer 1
            sw.WriteLine(row("L1", "orders_completed", "", "", totalOrders, "orders"));
            double opH = durationSec > 0 ? totalOrders / (durationSec / 3600.0) : 0.0;
            sw.WriteLine(row("L1", "orders_per_hour", "", "", fmt(opH), "orders/h"));
            sw.WriteLine(row("L1", "total_energy_with_support_kJ", "", "", fmt(totalEnergyJ / 1000.0), "kJ"));
            sw.WriteLine(row("L1", "total_energy_mech_kJ", "", "", fmt(totalMechJ / 1000.0), "kJ"));
            sw.WriteLine(row("L1", "energy_total_with_support_per_order_kJ", "", "",
                fmt(totalOrders > 0 ? totalEnergyJ / 1000.0 / totalOrders : 0.0), "kJ/order"));
            sw.WriteLine(row("L1", "energy_mech_per_order_kJ", "", "",
                fmt(totalOrders > 0 ? totalMechJ / 1000.0 / totalOrders : 0.0), "kJ/order"));
            sw.WriteLine(row("L1", "total_distance_m", "", "", fmt(kpiDistM), "m"));
            sw.WriteLine(row("L1", "order_distance_m", "", "", fmt(totalOrders > 0 ? kpiDistM / totalOrders : 0.0), "m/order"));
            sw.WriteLine(row("L1", "system_order_pile_on", "", "", fmt(StatSystemOrderPileOn), "orders/output_station_arrival"));
            sw.WriteLine(row("L1", "input_station_arrivals", "", "", StatOverallInputStationArrivals, "count"));
            sw.WriteLine(row("L1", "output_station_arrivals", "", "", StatOverallOutputStationArrivals, "count"));
            sw.WriteLine(row("L1", "station_arrivals_total", "", "", StatOverallStationArrivals, "count"));

            // Layer 2: trip split
            sw.WriteLine(row("L2", "trip_count", tripsEmpty, tripsLoaded, tripsEmpty + tripsLoaded, "trips"));
            sw.WriteLine(row("L2", "distance_m", fmt(totalDistEmpty), fmt(totalDistLoad), fmt(motionDistM), "m"));
            double timeLoad = moveT_L + turnT_L + waitT_L;
            double timeEmpty = moveT_E + turnT_E + waitT_E;
            sw.WriteLine(row("L2", "total_time_sec", fmt(timeEmpty), fmt(timeLoad), fmt(timeEmpty + timeLoad), "s"));
            double energyL_mech = moveE_L + turnE_L, energyE_mech = moveE_E + turnE_E;
            // E1-E4 only (E5 cannot be cleanly split by load state; see L3 for full phase breakdown)
            sw.WriteLine(row("L2", "total_move_turn_energy_kJ", fmt(energyE_mech / 1000.0), fmt(energyL_mech / 1000.0),
                fmt((energyE_mech + energyL_mech) / 1000.0), "kJ"));
            sw.WriteLine(row("L2", "move_turn_pct_of_mech",
                fmt(totalMechJ > 0 ? 100.0 * energyE_mech / totalMechJ : 0.0),
                fmt(totalMechJ > 0 ? 100.0 * energyL_mech / totalMechJ : 0.0),
                "", "%"));
            sw.WriteLine(row("L2", "avg_distance_per_trip_m",
                fmt(tripsEmpty  > 0 ? totalDistEmpty / tripsEmpty : 0.0),
                fmt(tripsLoaded > 0 ? totalDistLoad  / tripsLoaded : 0.0), "", "m/trip"));
            sw.WriteLine(row("L2", "avg_time_per_trip_sec",
                fmt(tripsEmpty  > 0 ? timeEmpty / tripsEmpty : 0.0),
                fmt(tripsLoaded > 0 ? timeLoad  / tripsLoaded : 0.0), "", "s/trip"));
            sw.WriteLine(row("L2", "avg_energy_per_trip_kJ",
                fmt(tripsEmpty  > 0 ? energyE_mech / 1000.0 / tripsEmpty : 0.0),
                fmt(tripsLoaded > 0 ? energyL_mech / 1000.0 / tripsLoaded : 0.0), "", "kJ/trip"));

            // Layer 3: energy phase breakdown (E1–E5), denominator = totalMechJ
            double e1J = StatOverallEnergyE1J;
            double e2J = StatOverallEnergyE2J;
            double e3J = StatOverallEnergyE3J;
            double e4J = StatOverallEnergyE4J;
            double e5J = StatOverallEnergyE5J;
            string mechPct(double v) => fmt(totalMechJ > 0 ? 100.0 * v / totalMechJ : 0.0);
            sw.WriteLine(row("L3", "e1_accel_kJ",  "", "", fmt(e1J / 1000.0), "kJ"));
            sw.WriteLine(row("L3", "e2_decel_kJ",  "", "", fmt(e2J / 1000.0), "kJ"));
            sw.WriteLine(row("L3", "e3_cruise_kJ", "", "", fmt(e3J / 1000.0), "kJ"));
            sw.WriteLine(row("L3", "e4_rotation_kJ", "", "", fmt(e4J / 1000.0), "kJ"));
            sw.WriteLine(row("L3", "e5_lift_kJ",   "", "", fmt(e5J / 1000.0), "kJ"));
            sw.WriteLine(row("L3", "e1_pct_of_mech", "", "", mechPct(e1J), "%"));
            sw.WriteLine(row("L3", "e2_pct_of_mech", "", "", mechPct(e2J), "%"));
            sw.WriteLine(row("L3", "e3_pct_of_mech", "", "", mechPct(e3J), "%"));
            sw.WriteLine(row("L3", "e4_pct_of_mech", "", "", mechPct(e4J), "%"));
            sw.WriteLine(row("L3", "e5_pct_of_mech", "", "", mechPct(e5J), "%"));

            // Layer 4: turning
            sw.WriteLine(row("L4", "turn_count", tnC_E, tnC_L, tnC_E + tnC_L, "events"));
            sw.WriteLine(row("L4", "turn_energy_kJ", fmt(turnE_E / 1000.0), fmt(turnE_L / 1000.0),
                fmt((turnE_E + turnE_L) / 1000.0), "kJ"));
            sw.WriteLine(row("L4", "turn_pct_of_total",
                fmt(totalEnergyJ > 0 ? 100.0 * turnE_E / totalEnergyJ : 0.0),
                fmt(totalEnergyJ > 0 ? 100.0 * turnE_L / totalEnergyJ : 0.0), "", "%"));
            sw.WriteLine(row("L4", "turn_pct_of_state",
                fmt(energyE_mech > 0 ? 100.0 * turnE_E / energyE_mech : 0.0),
                fmt(energyL_mech > 0 ? 100.0 * turnE_L / energyL_mech : 0.0), "", "%"));
            sw.WriteLine(row("L4", "turn_per_trip",
                fmt(tripsEmpty  > 0 ? (double)tnC_E / tripsEmpty : 0.0),
                fmt(tripsLoaded > 0 ? (double)tnC_L / tripsLoaded : 0.0), "", "per trip"));
            sw.WriteLine(row("L4", "turn_per_m",
                fmt(totalDistEmpty > 0 ? tnC_E / totalDistEmpty : 0.0),
                fmt(totalDistLoad  > 0 ? tnC_L / totalDistLoad  : 0.0), "", "per m"));

            // Stop-and-go: congestion-induced stop/restart cycles. Split into:
            //   stop_and_go_*       = WHCA*/reservation-table planned-wait stop/restart
            //   queue_stop_and_go_* = QueueManager creep stop/restart inside station queue zones
            // Plus queue_holding_*  = stationary support energy while waiting in queue (already tracked).
            // Not split by load state. Stop-and-go energy is a strict subset of E1+E2; queue holding is a strict subset of E_support.
            sw.WriteLine(row("L4", "stop_and_go_count", "", "", StatOverallStopAndGoCount.ToString(IOConstants.FORMATTER), "events"));
            sw.WriteLine(row("L4", "stop_and_go_energy_kJ", "", "", fmt(StatOverallStopAndGoEnergyJ / 1000.0), "kJ"));
            sw.WriteLine(row("L4", "stop_and_go_pct_of_e1e2", "", "",
                fmt((e1J + e2J) > 0 ? 100.0 * StatOverallStopAndGoEnergyJ / (e1J + e2J) : 0.0), "%"));
            sw.WriteLine(row("L4", "stop_and_go_per_order", "", "",
                fmt(StatOverallOrdersHandled > 0 ? (double)StatOverallStopAndGoCount / StatOverallOrdersHandled : 0.0), "per order"));
            sw.WriteLine(row("L4", "queue_stop_and_go_count", "", "", StatOverallQueueStopAndGoCount.ToString(IOConstants.FORMATTER), "events"));
            sw.WriteLine(row("L4", "queue_stop_and_go_energy_kJ", "", "", fmt(StatOverallQueueStopAndGoEnergyJ / 1000.0), "kJ"));
            sw.WriteLine(row("L4", "queue_stop_and_go_pct_of_e1e2", "", "",
                fmt((e1J + e2J) > 0 ? 100.0 * StatOverallQueueStopAndGoEnergyJ / (e1J + e2J) : 0.0), "%"));
            sw.WriteLine(row("L4", "queue_stop_and_go_per_order", "", "",
                fmt(StatOverallOrdersHandled > 0 ? (double)StatOverallQueueStopAndGoCount / StatOverallOrdersHandled : 0.0), "per order"));
            sw.WriteLine(row("L4", "queue_holding_time_sec", "", "", fmt(StatOverallQueueingAtStationTimeSec), "s"));
            sw.WriteLine(row("L4", "queue_holding_energy_kJ", "", "", fmt(StatOverallEQueueingAtStationJ / 1000.0), "kJ"));
            sw.WriteLine(row("L4", "queue_holding_pct_of_support", "", "",
                fmt(StatOverallESupportJ > 0 ? 100.0 * StatOverallEQueueingAtStationJ / StatOverallESupportJ : 0.0), "%"));
            sw.WriteLine(row("L4", "queue_holding_per_order_kJ", "", "",
                fmt(StatOverallOrdersHandled > 0 ? StatOverallEQueueingAtStationJ / 1000.0 / StatOverallOrdersHandled : 0.0), "per order"));

            // Layer 5: wait
            sw.WriteLine(row("L5", "wait_time_sec", fmt(waitT_E), fmt(waitT_L), fmt(waitT_E + waitT_L), "s"));
            sw.WriteLine(row("L5", "wait_energy_kJ", fmt(waitE_E / 1000.0), fmt(waitE_L / 1000.0),
                fmt((waitE_E + waitE_L) / 1000.0), "kJ"));
            sw.WriteLine(row("L5", "wait_pct_of_total",
                fmt(totalEnergyJ > 0 ? 100.0 * waitE_E / totalEnergyJ : 0.0),
                fmt(totalEnergyJ > 0 ? 100.0 * waitE_L / totalEnergyJ : 0.0), "", "%"));
            // wait_ratio stats (combined + split)
            var wrAll = bots.SelectMany(b => b.PerTripWaitRatioLoaded.Concat(b.PerTripWaitRatioEmpty)).ToList();
            var wrL = bots.SelectMany(b => b.PerTripWaitRatioLoaded).ToList();
            var wrE = bots.SelectMany(b => b.PerTripWaitRatioEmpty).ToList();
            double meanR(List<double> xs) => xs.Count > 0 ? xs.Average() : 0.0;
            double medR (List<double> xs) { if (xs.Count == 0) return 0.0; xs.Sort(); return Percentile(xs, 0.50); }
            double p95R (List<double> xs) { if (xs.Count == 0) return 0.0; xs.Sort(); return Percentile(xs, 0.95); }
            sw.WriteLine(row("L5", "wait_ratio_mean", fmt(meanR(wrE)), fmt(meanR(wrL)), fmt(meanR(wrAll)), "ratio"));
            sw.WriteLine(row("L5", "wait_ratio_median", fmt(medR(wrE)), fmt(medR(wrL)), fmt(medR(wrAll)), "ratio"));
            sw.WriteLine(row("L5", "wait_ratio_p95", fmt(p95R(wrE)), fmt(p95R(wrL)), fmt(p95R(wrAll)), "ratio"));

            // Layer 6: derived insight metrics
            // 5-component energy composition: non-overlapping sum = E_mech + E_support.
            //   move    = E1+E2+E3 (mechanical drive)
            //   turn    = E4       (mechanical rotation)
            //   lift    = E5       (mechanical lift/lower)
            //   wait    = SupportPower(Pod) × WaitTimeSec  (routing-induced congestion cost)
            //   support = E_support_total − wait  (always-on background minus the wait subset; includes standby)
            // denominator = move+turn+lift+wait+support = StatOverallEnergyTotalWithSupportJ
            double totMove = moveE_L + moveE_E;
            double totTurn = turnE_L + turnE_E;
            double totLift = StatOverallEnergyE5J;
            double totWait = waitE_L + waitE_E;
            double totSupport = StatOverallEnergySupportJ - totWait;
            double comp5Total = totMove + totTurn + totLift + totWait + totSupport; // == StatOverallEnergyTotalWithSupportJ
            string compPct(double v) => fmt(comp5Total > 0 ? 100.0 * v / comp5Total : 0.0);
            sw.WriteLine(row("L6", "composition_move_pct",    "", "", compPct(totMove),    "%"));
            sw.WriteLine(row("L6", "composition_turn_pct",    "", "", compPct(totTurn),    "%"));
            sw.WriteLine(row("L6", "composition_lift_pct",    "", "", compPct(totLift),    "%"));
            sw.WriteLine(row("L6", "composition_wait_pct",    "", "", compPct(totWait),    "%"));
            sw.WriteLine(row("L6", "composition_support_pct", "", "", compPct(totSupport), "%"));
            sw.WriteLine(row("L6", "energy_empty_to_loaded_ratio", "", "",
                fmt(energyL_mech > 0 ? energyE_mech / energyL_mech : 0.0), "ratio"));
            double travT = moveT_L + moveT_E + turnT_L + turnT_E;
            sw.WriteLine(row("L6", "effective_motion_ratio", "", "",
                fmt((travT + waitT_L + waitT_E) > 0 ? travT / (travT + waitT_L + waitT_E) : 0.0), "ratio"));
            sw.WriteLine(row("L6", "wait_energy_per_order_kJ", "", "",
                fmt(totalOrders > 0 ? (waitE_L + waitE_E) / 1000.0 / totalOrders : 0.0), "kJ/order"));
            sw.WriteLine(row("L6", "energy_per_m_empty_J",
                fmt(totalDistEmpty > 0 ? energyE_mech / totalDistEmpty : 0.0), "", "", "J/m"));
            sw.WriteLine(row("L6", "energy_per_m_loaded_J", "",
                fmt(totalDistLoad > 0 ? energyL_mech / totalDistLoad : 0.0), "", "J/m"));
        }

        /// <summary>
        /// Flushes individual statistics, if the corresponding controllers are present.
        /// </summary>
        private void WriteIndividiualStatistics()
        {
            // Nothing to see here currently (experimental methods using this have been removed)
            // You may use this for dumping additional statistics for custom controllers (just check whether your controller was running)
            // e.g.: if(ControllerConfig.OrderBatchingConfig.GetMethodType() == Configurations.OrderBatchingMethodType.SimpleSavings) DumpStatisticsOfSimpleSavings
        }

        #endregion
    }

    public class SlowStartDecisionTrace
    {
        public double Time;
        public string BotId;
        public string TaskId;
        public string PodId;
        public string StationId;
        public double EtaNow;
        public double TStarve;
        public double QueueBudget;
        public double ReleaseBudget;
        public double Slack;
        public double ChosenDelay;
        public double Deadline;
        public string ReleasePolicy;
        public double EtaSafetyBuffer;
        public bool ProbeSuccess;
        public string ReleaseReason;
        public int StationPendingItems;
        public double StationBusyRemaining;
        public double StationQueueWork;
        public double EtaAtRelease = double.NaN;
        public double RemainingTStarveAtRelease = double.NaN;
        public double BestFutureEtaAtRelease = double.NaN;
        public double BestFutureDelayAtRelease = double.NaN;
        public double EtaImprovementAtRelease = double.NaN;
        public int NeighborCountAtDecision;
        public int MissingArrivalCount;
        public double QueueArrivalTime = double.NaN;
        public double MovementStartTime = double.NaN;
        public double ReleaseTime = double.NaN;
        public double ActualHoldTime = double.NaN;
        public double ActualArrivalTime = double.NaN;
        public double ActualEta = double.NaN;
        public double ReleaseToQueueTime = double.NaN;
        public double MovementTimeToQueue = double.NaN;
        public double ArrivalError = double.NaN;
        public double StationIdleAtArrival = double.NaN;
        public double QueueWaitAtStation = double.NaN;
        public double ProcessingFinishTime = double.NaN;
        public double ProcessingTime = double.NaN;

        public static string GetHeader()
        {
            return "time,bot_id,task_id,pod_id,station_id,eta_now,t_starve,queue_budget,release_budget,slack,chosen_delay,deadline,release_policy,eta_safety_buffer,probe_success,release_reason,station_pending_items,station_busy_remaining,station_queue_work,eta_at_release,remaining_t_starve_at_release,best_future_eta_at_release,best_future_delay_at_release,eta_improvement_at_release,neighbor_count_at_decision,missing_arrival_count,queue_arrival_time,movement_start_time,release_time,actual_hold_time,actual_arrival_time,actual_eta,release_to_queue_time,movement_time_to_queue,arrival_error,station_idle_at_arrival,queue_wait_at_station,processing_finish_time,processing_time";
        }

        public string GetLine()
        {
            return F(Time) + "," +
                S(BotId) + "," +
                S(TaskId) + "," +
                S(PodId) + "," +
                S(StationId) + "," +
                F(EtaNow) + "," +
                F(TStarve) + "," +
                F(QueueBudget) + "," +
                F(ReleaseBudget) + "," +
                F(Slack) + "," +
                F(ChosenDelay) + "," +
                F(Deadline) + "," +
                S(ReleasePolicy) + "," +
                F(EtaSafetyBuffer) + "," +
                (ProbeSuccess ? "1" : "0") + "," +
                S(ReleaseReason) + "," +
                StationPendingItems.ToString(IOConstants.FORMATTER) + "," +
                F(StationBusyRemaining) + "," +
                F(StationQueueWork) + "," +
                F(EtaAtRelease) + "," +
                F(RemainingTStarveAtRelease) + "," +
                F(BestFutureEtaAtRelease) + "," +
                F(BestFutureDelayAtRelease) + "," +
                F(EtaImprovementAtRelease) + "," +
                NeighborCountAtDecision.ToString(IOConstants.FORMATTER) + "," +
                MissingArrivalCount.ToString(IOConstants.FORMATTER) + "," +
                F(QueueArrivalTime) + "," +
                F(MovementStartTime) + "," +
                F(ReleaseTime) + "," +
                F(ActualHoldTime) + "," +
                F(ActualArrivalTime) + "," +
                F(ActualEta) + "," +
                F(ReleaseToQueueTime) + "," +
                F(MovementTimeToQueue) + "," +
                F(ArrivalError) + "," +
                F(StationIdleAtArrival) + "," +
                F(QueueWaitAtStation) + "," +
                F(ProcessingFinishTime) + "," +
                F(ProcessingTime);
        }

        private static string F(double value)
        {
            return double.IsNaN(value) ? "" : value.ToString(IOConstants.EXPORT_FORMAT_SHORTER, IOConstants.FORMATTER);
        }

        private static string S(string value)
        {
            return (value ?? "").Replace(",", "_");
        }
    }

    public class SlowStartHoldingDecisionTrace
    {
        public double DecisionTime;
        public string BotId;
        public string TaskId;
        public string PodId;
        public string StationId;
        public double StationEst;
        public double StationWorkHorizon;
        public double StationStarvationGap;
        public int StationLateJobs;
        public int StationUncertainJobs;
        public double Buffer;
        public double Lift;
        public double Travel;
        public double Value;
        public double ProcessingBudget;
        public double ReleaseBudget;
        public double HoldingBudget;
        public double HoldDelay;
        public double Deadline;
        public double HoldElapsed;
        public bool Feasible;
        public bool Chosen;
        public bool ChosenReleaseNow;
        public int HolderCount;
        public string ChosenBotId;
        public int StationPendingItems;
        public double StationBusyRemaining;
        public double StationQueueWork;
        public int NeighborCountAtDecision;
        public string ReleaseReason = "";
        public double ReleaseTime = double.NaN;
        public double ActualHoldTime = double.NaN;
        public double EtaAtRelease = double.NaN;
        public double RemainingEstAtRelease = double.NaN;
        public double MovementStartTime = double.NaN;
        public double QueueArrivalTime = double.NaN;
        public double ActualArrivalTime = double.NaN;
        public double ActualEta = double.NaN;
        public double ReleaseToQueueTime = double.NaN;
        public double MovementTimeToQueue = double.NaN;
        public double ArrivalError = double.NaN;
        public double StationIdleAtArrival = double.NaN;
        public double QueueWaitAtStation = double.NaN;
        public double ProcessingFinishTime = double.NaN;
        public double ProcessingTime = double.NaN;

        public static string GetHeader()
        {
            return "decision_time,bot_id,task_id,pod_id,station_id,station_est,station_work_horizon,station_starvation_gap,station_late_jobs,station_uncertain_jobs,buffer,lift,travel,value,processing_budget,release_budget,holding_budget,hold_delay,deadline,hold_elapsed,feasible,chosen,chosen_release_now,holder_count,chosen_bot_id,station_pending_items,station_busy_remaining,station_queue_work,neighbor_count_at_decision,release_reason,release_time,actual_hold_time,eta_at_release,remaining_est_at_release,movement_start_time,queue_arrival_time,actual_arrival_time,actual_eta,release_to_queue_time,movement_time_to_queue,arrival_error,station_idle_at_arrival,queue_wait_at_station,processing_finish_time,processing_time";
        }

        public string GetLine()
        {
            return F(DecisionTime) + "," +
                S(BotId) + "," +
                S(TaskId) + "," +
                S(PodId) + "," +
                S(StationId) + "," +
                F(StationEst) + "," +
                F(StationWorkHorizon) + "," +
                F(StationStarvationGap) + "," +
                StationLateJobs.ToString(IOConstants.FORMATTER) + "," +
                StationUncertainJobs.ToString(IOConstants.FORMATTER) + "," +
                F(Buffer) + "," +
                F(Lift) + "," +
                F(Travel) + "," +
                F(Value) + "," +
                F(ProcessingBudget) + "," +
                F(ReleaseBudget) + "," +
                F(HoldingBudget) + "," +
                F(HoldDelay) + "," +
                F(Deadline) + "," +
                F(HoldElapsed) + "," +
                (Feasible ? "1" : "0") + "," +
                (Chosen ? "1" : "0") + "," +
                (ChosenReleaseNow ? "1" : "0") + "," +
                HolderCount.ToString(IOConstants.FORMATTER) + "," +
                S(ChosenBotId) + "," +
                StationPendingItems.ToString(IOConstants.FORMATTER) + "," +
                F(StationBusyRemaining) + "," +
                F(StationQueueWork) + "," +
                NeighborCountAtDecision.ToString(IOConstants.FORMATTER) + "," +
                S(ReleaseReason) + "," +
                F(ReleaseTime) + "," +
                F(ActualHoldTime) + "," +
                F(EtaAtRelease) + "," +
                F(RemainingEstAtRelease) + "," +
                F(MovementStartTime) + "," +
                F(QueueArrivalTime) + "," +
                F(ActualArrivalTime) + "," +
                F(ActualEta) + "," +
                F(ReleaseToQueueTime) + "," +
                F(MovementTimeToQueue) + "," +
                F(ArrivalError) + "," +
                F(StationIdleAtArrival) + "," +
                F(QueueWaitAtStation) + "," +
                F(ProcessingFinishTime) + "," +
                F(ProcessingTime);
        }

        private static string F(double value)
        {
            return double.IsNaN(value) ? "" : value.ToString(IOConstants.EXPORT_FORMAT_SHORTER, IOConstants.FORMATTER);
        }

        private static string S(string value)
        {
            return (value ?? "").Replace(",", "_");
        }
    }
}
