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
        /// PP-aware slow-start: hold bot at pod cell after pickup for a duration that
        /// preserves station-busy continuity (T_starve − ETA), to convert station-queue
        /// premature wait into upstream controlled hold and reduce stop-and-go.
        /// </summary>
        public bool SlowStartEnabled = false;
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
