using RAWSimO.Core.Configurations;
using RAWSimO.Core.IO;
using RAWSimO.Core.Management;
using RAWSimO.Core.Randomization;
using System;
using System.Xml.Serialization;

namespace RAWSimO.Core.Configurations
{
    public enum SlowStartReleasePolicy
    {
        StationSlack,
        ReservationEtaImprovement,
        QueueBudgetEtaImprovement
    }

    /// <summary>
    /// The base configuration.
    /// </summary>
    public class SettingConfiguration
    {
        #region Naming

        /// <summary>
        /// A name identifying the configuration.
        /// </summary>
        public string Name = "default";
        /// <summary>
        /// Creates a name for the setting supplying basic information.
        /// </summary>
        /// <returns>The name of the scenario.</returns>
        public string GetMetaInfoBasedConfigName()
        {
            string name = "";
            if (InventoryConfiguration != null)
            {
                // Add item type and order mode
                name += InventoryConfiguration.ItemType.ToString() + "-" + InventoryConfiguration.OrderMode.ToString();
                // Add further info depending on mode
                switch (InventoryConfiguration.OrderMode)
                {
                    case OrderMode.Fill:
                        if (InventoryConfiguration.DemandInventoryConfiguration != null)
                            name += "-" + InventoryConfiguration.DemandInventoryConfiguration.BundleCount + "-" + InventoryConfiguration.DemandInventoryConfiguration.OrderCount;
                        break;
                    case OrderMode.Poisson:
                        if (InventoryConfiguration.PoissonInventoryConfiguration != null)
                            name += "-" + InventoryConfiguration.PoissonInventoryConfiguration.PoissonMode;
                        break;
                    case OrderMode.Fixed:
                    default:
                        break;
                }
            }
            // Add the simulation time
            name += (string.IsNullOrWhiteSpace(name) ? "" : "-") + Math.Round(SimulationDuration).ToString(IOConstants.FORMATTER);
            // Return it
            return name;
        }

        #endregion

        #region Basic parameters

        /// <summary>
        /// The warmup-time for the simulation in seconds.
        /// </summary>
        public double SimulationWarmupTime = 0;

        /// <summary>
        /// The duration of the simulation in seconds.
        /// </summary>
        public double SimulationDuration = 7200;

        /// <summary>
        /// The random seed to use.
        /// </summary>
        public int Seed = 0;

        /// <summary>
        /// The log level used to filter output messages.
        /// </summary>
        public LogLevel LogLevel = LogLevel.Info;

        /// <summary>
        /// The log level that indicates which output files will be written.
        /// </summary>
        public LogFileLevel LogFileLevel = LogFileLevel.All;

        /// <summary>
        /// Indicates whether well-sortedness will be tracked (computationally intense).
        /// </summary>
        public bool MonitorWellSortedness = false;

        /// <summary>
        /// Indicates the current debug mode.
        /// </summary>
        public DebugMode DebugMode = DebugMode.RealTimeAndMemory;

        #endregion

        #region Movement related parameters

        /// <summary>
        /// Distance between a pod and a station which is considered close enough.
        /// </summary>
        public double Tolerance = 0.2;

        /// <summary>
        /// Indicates whether to simulate the acceleration or use top-speed instantly.
        /// </summary>
        public bool UseAcceleration = false;

        /// <summary>
        /// Indicates whether to simulate the rotation or use oritaion instantly.
        /// </summary>
        public bool UseTurnDelay = false;

        /// <summary>
        /// Indicates whether to rotate pods while the bot is rotating. This actually only results in different visual feedback.
        /// </summary>
        public bool RotatePods = false;

        /// <summary>
        /// Indicates whether to use or ignore queues in the waypoint-system.
        /// </summary>
        public bool QueueHandlingEnabled = true;
        /// <summary>
        /// Robot chassis mass [kg] used by the energy model.
        /// </summary>
        public double EnergyRobotMassKg = RAWSimO.Core.Metrics.EnergyConsumption.DEFAULT_ROBOT_MASS_KG;
        /// <summary>
        /// Support power [W = J/s] when a bot is not carrying a pod.
        /// </summary>
        public double EnergySupportPowerEmptyW = RAWSimO.Core.Metrics.EnergyConsumption.DEFAULT_SUPPORT_POWER_EMPTY_W;
        /// <summary>
        /// Support power [W = J/s] when a bot is carrying a pod.
        /// </summary>
        public double EnergySupportPowerLoadedW = RAWSimO.Core.Metrics.EnergyConsumption.DEFAULT_SUPPORT_POWER_LOADED_W;
        /// <summary>
        /// PP-aware slow-start: hold bot at pod cell after pickup for a duration that
        /// preserves station-busy continuity (T_starve − ETA), to convert station-queue
        /// premature wait into upstream controlled hold and reduce stop-and-go.
        /// </summary>
        public bool SlowStartEnabled = false;
        /// <summary>
        /// Replenishment (input-station) slow-start. Holds the bot at the pod cell before a
        /// store/InsertTask travels to the input station, releasing per
        /// InputStationReleaseScheduler so the pod arrives JIT and reduces input-station pod
        /// queue energy. Lower-half only — replenishment pod selection stays native.
        /// Independent of SlowStartEnabled (output). See
        /// docs/superpowers/plans/2026-05-30-input-station-slow-start.md.
        /// </summary>
        public bool SlowStartInputEnabled = false;
        /// <summary>
        /// A/B switch: when true, the release schedulers estimate pod→station ETA via the
        /// reservation-aware probe (PathManager.EstimateReservationAwareEta — a SpaceTimeAStar
        /// dry-run against the live WHCA* reservation table that accounts for current congestion)
        /// instead of the empty-table ideal-kinematic lower bound. Aims to remove the systematic
        /// ETA under-estimation that causes late arrival / starvation. Falls back to ideal on NaN.
        /// </summary>
        public bool SlowStartUseReservationEta = false;
        /// <summary>
        /// Station-starve-aware M1G cost (output stations). When true, M1G converts its
        /// pod->station / bot->pod distance cost to travel time and adds a starvation delay
        /// penalty so pods are steered to stations about to go idle. Preserves w1/w2/w3 and
        /// adds no MILP variables. Default off = baseline distance cost.
        /// See docs/superpowers/specs/2026-06-06-m1g-hadgs-station-starve-aware-design.md.
        /// </summary>
        public bool StarveAwareCostEnabled = false;
        /// <summary>Fixed floor penalty [s] applied when a pod can ideally arrive before the
        /// station starves (delay &lt; 0). Small positive constant; keeps the cost non-negative.</summary>
        public double StarveAwareFixedParam = 30.0;
        /// <summary>Nominal speed [m/s] for distance-&gt;travel-time conversion. If &lt;= 0, M1G
        /// derives it from the fleet's max bot velocity at solve time.</summary>
        public double StarveAwareNominalSpeed = 0.0;
        /// <summary>
        /// Input slow-start: include the station's KNOWN-but-unassigned store backlog
        /// (InputStation.GetInfoOpenRequests — store requests not yet bound to any InsertTask) as
        /// deterministic work in the holder's release horizon. Without this the input EST counts
        /// only already-dispatched pods, so holders release in a committed-work "trough" and arrive
        /// into the queue refilled by the pending backlog. The backlog count is exact (no congestion
        /// prediction) and does not double-count committed work (assigned requests are already
        /// removed from the available pool). Default false; A/B knob for the input scheduler.
        /// </summary>
        public bool InputBacklogAwareHold = false;
        /// <summary>
        /// Input slow-start: replace the single-stage chosen/cascade release with explicit FCFS
        /// SEQUENTIAL serialization. Holders are ordered by hold-start time; holder k is timed to
        /// ARRIVE exactly when holder k−1 finishes processing (deadline_k = T_clear + Σ_{j&lt;k}
        /// proc_j − travel_k − lift_k − buffer). This guarantees a ≥proc separation between
        /// consecutive arrivals at the same input station, eliminating the synchronized
        /// (gap&lt;2s) releases the single-cascade leaves in ~16% of cases. Default TRUE (validated:
        /// synchronized releases 16.3%→0%, input pod wait 37.3→4.8s; cost +18% output starvation,
        /// −1.5% throughput from robots held longer at input).
        /// </summary>
        public bool InputSequentialRelease = true;
        /// <summary>
        /// Pure-observation probe (changes NO decision). When true, BackfillProbe runs once per
        /// tick per output station: at the onset of a projected starvation gap it measures, for the
        /// pod physically docked at the station, how many backlog (unassigned) orders that pod could
        /// fully / partially satisfy — i.e. the "deferred-binding + opportunistic backfill" hit rate.
        /// Writes backfill_probe.csv. Default false; independent of all slow-start flags.
        /// </summary>
        public bool BackfillProbeEnabled = false;
        /// <summary>
        /// Slow-start release rule. StationSlack preserves the current baseline.
        /// ReservationEtaImprovement keeps holding when a short delay is predicted to
        /// produce a better reservation-aware ETA while station safety remains feasible.
        /// </summary>
        public SlowStartReleasePolicy SlowStartReleasePolicy = SlowStartReleasePolicy.StationSlack;
        /// <summary>
        /// Explicit ETA uncertainty buffer [s] used by non-baseline slow-start policies.
        /// This reserves station slack against pod-to-station travel-time underestimation.
        /// </summary>
        public double SlowStartEtaSafetyBuffer = 0.0;
        /// <summary>
        /// Number of future seconds to probe when testing reservation ETA improvement.
        /// </summary>
        public int SlowStartEtaImprovementLookaheadSec = 5;
        /// <summary>
        /// Release margin [s] for the reservation ETA improvement policy. A current ETA
        /// within this margin of the best future ETA is good enough to depart.
        /// </summary>
        public double SlowStartEtaImprovementReleaseMargin = 1.0;
        /// <summary>
        /// Max additional queue-conversion hold budget [s] for QueueBudgetEtaImprovement.
        /// This cap prevents inferred station-queue budget from cascading into long holds.
        /// </summary>
        public double SlowStartQueueBudgetMaxSec = 0.0;
        /// <summary>
        /// Multiplier applied to same-station active extract work when estimating queue
        /// budget. Values below 1 are conservative.
        /// </summary>
        public double SlowStartQueueBudgetWorkMultiplier = 1.0;
        /// <summary>
        /// Minimum number of other active extract tasks heading to the same station before
        /// QueueBudgetEtaImprovement may add queue-conversion budget.
        /// </summary>
        public int SlowStartQueueBudgetMinOtherExtractTasks = 1;

        #endregion

        #region Entity related parameters

        /// <summary>
        /// The idle-time after which a station is considered resting / shutdown.
        /// </summary>
        public double StationShutdownThresholdTime = 600;

        #endregion

        #region Statistics related

        /// <summary>
        /// Enables / disables the tracking of correlative frequencies between item descriptions.
        /// </summary>
        public bool CorrelativeFrequencyTracking = false;
        /// <summary>
        /// Indicates whether locations of the robots are polled alot more frequently in order to get more precise statistical feedback (note: this may cause huge output files).
        /// </summary>
        public bool IntenseLocationPolling = false;

        #endregion

        #region Inventory related parameters

        /// <summary>
        /// All configuration settings for the generation or input of inventory.
        /// </summary>
        public InventoryConfiguration InventoryConfiguration = new InventoryConfiguration();

        #endregion

        #region Override parameters

        /// <summary>
        /// Exposes values that override and replace others given by the instance file.
        /// </summary>
        public OverrideConfiguration OverrideConfig;

        #endregion

        #region Comment tags

        /// <summary>
        /// Some optional comment tag that will be written to the footprint.
        /// </summary>
        public string CommentTag1 = "";
        /// <summary>
        /// Some optional comment tag that will be written to the footprint.
        /// </summary>
        public string CommentTag2 = "";
        /// <summary>
        /// Some optional comment tag that will be written to the footprint.
        /// </summary>
        public string CommentTag3 = "";

        #endregion

        #region Live parameters

        /// <summary>
        /// The heat mode to use when visualizing the simulation.
        /// </summary>
        [Live]
        [XmlIgnore]
        public HeatMode HeatMode;

        /// <summary>
        /// The action that is called when something is written to the output.
        /// </summary>
        [Live]
        [XmlIgnore]
        public Action<string> LogAction;

        /// <summary>
        /// The timestamp of the start of the execution.
        /// </summary>
        [Live]
        [XmlIgnore]
        public DateTime StartTime;

        /// <summary>
        /// The timestamp of the finish of the execution.
        /// </summary>
        [Live]
        [XmlIgnore]
        public DateTime StopTime;

        /// <summary>
        /// The directory to write all statistics file to.
        /// </summary>
        [Live]
        [XmlIgnore]
        public string StatisticsDirectory;

        /// <summary>
        /// Indicates whether a visualization is attached.
        /// </summary>
        [Live]
        [XmlIgnore]
        public bool VisualizationAttached;

        /// <summary>
        /// Indicates that the instance will only be drawn and can not be executed.
        /// </summary>
        [Live]
        [XmlIgnore]
        public bool VisualizationOnly;

        /// <summary>
        /// Real world commands will be printed
        /// </summary>
        [Live]
        [XmlIgnore]
        public bool RealWorldIntegrationCommandOutput = false;

        /// <summary>
        /// Real world events determine the end of a state
        /// </summary>
        [Live]
        [XmlIgnore]
        public bool RealWorldIntegrationEventDriven = false;

        #endregion
    }
}
