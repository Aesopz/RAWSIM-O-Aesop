using RAWSimO.Core.Control.Defaults.OrderBatching;
using RAWSimO.Core.Control.Shared;
using RAWSimO.Core.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace RAWSimO.Core.Configurations
{
    #region Order batching configurations

    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class DefaultOrderBatchingConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.Default; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obD";
            switch (OrderSelectionRule)
            {
                case DefaultOrderSelection.Random: name += "r"; break;
                case DefaultOrderSelection.FCFS: name += "f"; break;
                case DefaultOrderSelection.DueTime: name += "d"; break;
                case DefaultOrderSelection.FrequencyAge: name += "a"; break;
                default: throw new ArgumentException("Unexpected argument!");
            }
            switch (StationSelectionRule)
            {
                case DefaultOutputStationSelection.Random: name += "r"; break;
                case DefaultOutputStationSelection.LeastBusy: name += "l"; break;
                case DefaultOutputStationSelection.MostBusy: name += "m"; break;
                default: throw new ArgumentException("Unexpected argument!");
            }
            name += ((Recycle == true) ? "t" : "f");
            return name;
        }
        /// <summary>
        /// The rule to choose the order by.
        /// </summary>
        public DefaultOrderSelection OrderSelectionRule = DefaultOrderSelection.Random;
        /// <summary>
        /// The rule to choose the station by.
        /// </summary>
        public DefaultOutputStationSelection StationSelectionRule = DefaultOutputStationSelection.Random;
        /// <summary>
        /// Indicates whether stations are recycled, i.e. one station is filled with orders as long as there is capacity left.
        /// </summary>
        public bool Recycle = false;
        /// <summary>
        /// Indicates whether a fast lane is used overriding the last assignment slot.
        /// </summary>
        public bool FastLane = false;
        /// <summary>
        /// Indicates how to break ties when assigning fast lane orders.
        /// </summary>
        public FastLaneTieBreaker FastLaneTieBreaker = FastLaneTieBreaker.EarliestDueTime;
    }

    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class RandomOrderBatchingConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.Random; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "obR" + ((Recycle == true) ? "t" : "f"); }
        /// <summary>
        /// Indicates whether stations are recycled, i.e. one station is filled with orders as long as there is capacity left.
        /// </summary>
        public bool Recycle = false;
    }

    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class WorkloadOrderBatchingConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.Workload; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obWL";
            switch (OrderingRule)
            {
                case WorkloadOrderingRule.LowestOrderCount: name += "l"; break;
                case WorkloadOrderingRule.HighestOrderCount: name += "h"; break;
                default: throw new ArgumentException("Unexpected argument!");
            }
            return name;
        }
        /// <summary>
        /// Indicates which stations will be preferred for assigning orders to them.
        /// </summary>
        public WorkloadOrderingRule OrderingRule = WorkloadOrderingRule.LowestOrderCount;
    }

    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class RelatedOrderBatchingConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.Related; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obRL";
            switch (TieBreaker)
            {
                case RelatedOrderBatchingTieBreaker.Random: name += "r"; break;
                case RelatedOrderBatchingTieBreaker.LeastBusy: name += "l"; break;
                case RelatedOrderBatchingTieBreaker.MostBusy: name += "m"; break;
                default: throw new ArgumentException("Unexpected argument!");
            }
            return name;
        }
        /// <summary>
        /// The tie breaker to use when there are multiple stations with same number of order lines in common.
        /// </summary>
        public RelatedOrderBatchingTieBreaker TieBreaker = RelatedOrderBatchingTieBreaker.LeastBusy;
    }

    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class NearBestPodOrderBatchingConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.NearBestPod; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obNBP";
            switch (DistanceRule)
            {
                case NearBestPodOrderBatchingDistanceRule.Euclid: name += "e"; break;
                case NearBestPodOrderBatchingDistanceRule.Manhattan: name += "m"; break;
                case NearBestPodOrderBatchingDistanceRule.ShortestPath: name += "s"; break;
                default: throw new ArgumentException("Unexpected argument!");
            }
            return name;
        }
        /// <summary>
        /// The rule determining which combination of best pod and output station is nearest to each other.
        /// </summary>
        public NearBestPodOrderBatchingDistanceRule DistanceRule = NearBestPodOrderBatchingDistanceRule.ShortestPath;
    }

    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class ForesightOrderBatchingConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.Foresight; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obFo";
            switch (ScoreFunctionStationOrder)
            {
                case ScoreFunctionStationOrder.InboundPodsAvailablePicks: name += "P"; break;
                case ScoreFunctionStationOrder.InboundPodsAvailablePicksDepletePod: name += "Pd"; break;
                case ScoreFunctionStationOrder.Deadline: name += "D"; break;
                case ScoreFunctionStationOrder.InboundPodsAvailablePicksNotDepletePod: name += "Ow"; break;
                default:
                    break;
            }
            switch (ScoreFunctionStationOrderSecondLevel)
            {
                case ScoreFunctionStationOrder.InboundPodsAvailablePicks: name += "P"; break;
                case ScoreFunctionStationOrder.InboundPodsAvailablePicksDepletePod: name += "Pd"; break;
                case ScoreFunctionStationOrder.Deadline: name += "D"; break;
                case ScoreFunctionStationOrder.InboundPodsAvailablePicksNotDepletePod: name += "Ow"; break;
                default:
                    break;
            }
            return name;
        }
        /// <summary>
        /// The first score function to determine the best combination of order and station.
        /// </summary>
        public ScoreFunctionStationOrder ScoreFunctionStationOrder = ScoreFunctionStationOrder.InboundPodsAvailablePicks;
        /// <summary>
        /// The tie breaker function to determine the best combination of order and station.
        /// </summary>
        public ScoreFunctionStationOrder ScoreFunctionStationOrderSecondLevel = ScoreFunctionStationOrder.Deadline;
    }

    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class PodMatchingOrderBatchingConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.PodMatching; }
        /// <summary>
        /// Indicates how to break ties when assigning orders.
        /// </summary>
        public OrderSelectionTieBreaker TieBreaker = OrderSelectionTieBreaker.EarliestDueTime;
        /// <summary>
        /// Indicates whether a fast lane slot is used, i.e. one slot of each station is kept free for immediately fulfillable orders.
        /// </summary>
        public bool FastLane = true;
        /// <summary>
        /// Indicates that orders already late will be preferred over a well matching order.
        /// </summary>
        public bool LateBeforeMatch = false;
        /// <summary>
        /// Indicates how to break ties when assigning fast lane orders.
        /// </summary>
        public FastLaneTieBreaker FastLaneTieBreaker = FastLaneTieBreaker.EarliestDueTime;
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obPM" + (FastLane ? "y" : "n");
            return name;
        }
    }

    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class M1GConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.GM1; }
        /// <summary>
        /// Indicates how to break ties when assigning orders.
        /// </summary>
        public OrderSelectionTieBreaker TieBreaker = OrderSelectionTieBreaker.EarliestDueTime;
        /// <summary>
        /// Indicates whether a fast lane slot is used, i.e. one slot of each station is kept free for immediately fulfillable orders.
        /// </summary>
        public bool FastLane = true;
        /// <summary>
        /// Indicates that orders already late will be preferred over a well matching order.
        /// </summary>
        public bool LateBeforeMatch = false;
        /// <summary>
        /// Indicates how to break ties when assigning fast lane orders.
        /// </summary>
        public FastLaneTieBreaker FastLaneTieBreaker = FastLaneTieBreaker.EarliestDueTime;
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obMP" + (FastLane ? "y" : "n");
            return name;
        }
    }
    /// <summary>
    /// Starvation-Aware M1G (SA-M1G): M1G plus a pod-delay penalty in the pod-&gt;station objective.
    /// When a pod's free-flow ideal arrival (bot-&gt;pod + pod-&gt;station, conflict-free lower bound)
    /// exceeds the target station's EST (FirstStarveSec), the starvation window (arrival - EST) is
    /// penalized by <see cref="DelayPenaltyWeight"/>. Discourages high-value pods that would starve
    /// the station. EST recomputed each solve.
    /// </summary>
    public class SAM1GConfiguration : M1GConfiguration
    {
        /// <summary>Returns the type of the corresponding method this configuration belongs to.</summary>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SAM1G; }
        /// <summary>Sweep knob: penalty weight applied to the estimated starvation delay
        /// (seconds the pod's free-flow arrival exceeds the target station's EST). To be tuned.</summary>
        public double DelayPenaltyWeight = 1.0;
        /// <summary>Returns a name identifying the method.</summary>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "obSAM1G" + DelayPenaltyWeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
    /// <summary>
    /// M1G variant that can reserve a next pod for bots that are nearly done returning their current pod.
    /// </summary>
    public class M1GReturnPendingConfiguration : M1GConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.GM1ReturnPending; }
        /// <summary>
        /// Maximum shortest-path distance from the return storage location for a park-pod bot to be considered reusable.
        /// A bot whose next waypoint is the storage location is considered eligible regardless of this value.
        /// </summary>
        public double ReturnPendingDistanceThreshold = 1.0;
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "M1G-RP";
        }
    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class M2GConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.GM2; }
        /// <summary>
        /// Indicates how to break ties when assigning orders.
        /// </summary>
        public OrderSelectionTieBreaker TieBreaker = OrderSelectionTieBreaker.EarliestDueTime;
        /// <summary>
        /// Indicates whether a fast lane slot is used, i.e. one slot of each station is kept free for immediately fulfillable orders.
        /// </summary>
        public bool FastLane = true;
        /// <summary>
        /// Indicates that orders already late will be preferred over a well matching order.
        /// </summary>
        public bool LateBeforeMatch = false;
        /// <summary>
        /// Indicates how to break ties when assigning fast lane orders.
        /// </summary>
        public FastLaneTieBreaker FastLaneTieBreaker = FastLaneTieBreaker.EarliestDueTime;
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obMP" + (FastLane ? "y" : "n");
            return name;
        }
    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class HASConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.HAS; }
        /// <summary>
        /// Indicates how to break ties when assigning orders.
        /// </summary>
        public OrderSelectionTieBreaker TieBreaker = OrderSelectionTieBreaker.EarliestDueTime;
        /// <summary>
        /// Indicates whether a fast lane slot is used, i.e. one slot of each station is kept free for immediately fulfillable orders.
        /// </summary>
        public bool FastLane = true;
        /// <summary>
        /// Indicates that orders already late will be preferred over a well matching order.
        /// </summary>
        public bool LateBeforeMatch = false;
        /// <summary>
        /// Indicates how to break ties when assigning fast lane orders.
        /// </summary>
        public FastLaneTieBreaker FastLaneTieBreaker = FastLaneTieBreaker.EarliestDueTime;
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obMP" + (FastLane ? "y" : "n");
            return name;
        }
    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class HADGSConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.HADGS; }
        /// <summary>
        /// Indicates how to break ties when assigning orders.
        /// </summary>
        public OrderSelectionTieBreaker TieBreaker = OrderSelectionTieBreaker.EarliestDueTime;
        /// <summary>
        /// Indicates whether a fast lane slot is used, i.e. one slot of each station is kept free for immediately fulfillable orders.
        /// </summary>
        public bool FastLane = true;
        /// <summary>
        /// Indicates that orders already late will be preferred over a well matching order.
        /// </summary>
        public bool LateBeforeMatch = false;
        /// <summary>
        /// Indicates how to break ties when assigning fast lane orders.
        /// </summary>
        public FastLaneTieBreaker FastLaneTieBreaker = FastLaneTieBreaker.EarliestDueTime;
        /// <summary>
        /// Enables BAED (Blocking-Aware Effective Distance) cost augmentation in HADGS scoring.
        /// When true, EstimateBotPodDistance / EstimatePodStationDistance return
        /// physDistance + BAEDReferenceSpeed * sum(EntryDelay along path) computed from the live WCHA* reservation table.
        /// Default off → standard static-distance behaviour, identical to baseline HADGS.
        /// </summary>
        public bool UseBAED = false;
        /// <summary>
        /// Reference speed (m/s) used to convert entry-delay seconds into distance-equivalent metres when UseBAED is on.
        /// Default 1.5 m/s matches the layouts' MaxVelocity.
        /// </summary>
        public double BAEDReferenceSpeed = 1.5;
        /// <summary>
        /// Includes bots that are nearly done returning a pod as available candidates for HADGS pod-to-bot assignment.
        /// Default off keeps baseline HADGS behavior.
        /// </summary>
        public bool UseReturnPendingBots = false;
        /// <summary>
        /// Maximum shortest-path distance from the return storage location for a park-pod bot to be considered reusable.
        /// A bot whose next waypoint is the storage location is considered eligible regardless of this value.
        /// </summary>
        public double ReturnPendingDistanceThreshold = 1.0;
        /// <summary>
        /// When enabled, HADGS only re-evaluates after SituationInvestigated has
        /// been invalidated, matching the M1G trigger gate. Default off preserves
        /// the legacy HADGS behavior of re-evaluating while station capacity exists.
        /// </summary>
        public bool UseM1GTriggerGate = false;
        /// <summary>
        /// Multiplier on the travel (bot→pod + pod→station) term of the Completeable output-pod score,
        /// relative to the fixed +40 completion reward. 1.0 = baseline HADGS. Raising it makes pod
        /// selection prefer spatially nearer pods among those completing orders (spatial routing knob;
        /// used for the distance-vs-completion frontier sweep / adaptive-weight study).
        /// </summary>
        public double DistanceWeight = 1.0;
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obMP" + (FastLane ? "y" : "n") + (UseBAED ? "-baed" : "") + (UseReturnPendingBots ? "-rp" : "");
            return name;
        }
    }
    /// <summary>
    /// HADGS variant that can reserve a next pod for bots that are nearly done returning their current pod.
    /// </summary>
    public class HADGSReturnPendingConfiguration : HADGSConfiguration
    {
        /// <summary>
        /// Creates a new return-pending HADGS configuration.
        /// </summary>
        public HADGSReturnPendingConfiguration()
        {
            UseReturnPendingBots = true;
        }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "HADGS-RP";
        }
    }
    /// <summary>
    /// Starvation-aware HADGS. Inherits HADGSConfiguration and enables the
    /// M1G-style SituationInvestigated trigger gate by default.
    /// See docs/superpowers/specs/2026-06-13-sa-hadgs-design.md.
    /// </summary>
    public class SAHADGSConfiguration : HADGSConfiguration
    {
        /// <summary>
        /// Creates a starvation-aware HADGS configuration with the M1G trigger
        /// gate enabled. Set UseM1GTriggerGate=false to restore legacy triggering.
        /// </summary>
        public SAHADGSConfiguration()
        {
            UseM1GTriggerGate = true;
        }
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SAHADGS; }
        /// <summary>
        /// Main sweep knob: seconds of projected starvation gap one completed order is worth
        /// in the weighted candidate score (analogous to M1G's w2).
        /// </summary>
        public double OrderRewardSec = 60.0;
        /// <summary>
        /// Secondary weight on summed travel time (energy/distance proxy) in the candidate score.
        /// </summary>
        public double TravelTimeWeight = 0.1;
        /// <summary>
        /// Number of most-urgent stock-feasible orders competing per station per round (flexible POA).
        /// </summary>
        public int TopKOrders = 3;
        /// <summary>
        /// Also build an ETA-greedy cover-set variant per order (second candidate; ablation switch).
        /// </summary>
        public bool UseEtaGreedyVariant = true;
        /// <summary>
        /// Tolerance added to EST when classifying a TA pair as on-time. Negative values
        /// compensate the optimism of nominal-speed ETAs under congestion.
        /// </summary>
        public double FeasibilitySlackSec = 0.0;
        /// <summary>
        /// Speed used to convert distances into travel time. 0 → max bot velocity of the instance.
        /// </summary>
        public double NominalSpeed = 0.0;
        /// <summary>
        /// Number of most-urgent stock-feasible backlog orders over which a candidate pod-set's
        /// completable-order reward is measured (pile-on horizon). 0 = unlimited (whole stock-feasible
        /// backlog, most HADGS-like). Cover-sets are still seeded from the top TopKOrders only.
        /// </summary>
        public int CompletableHorizon = 0;
        /// <summary>
        /// SA-M1G-aligned sweep knob: penalty weight applied to the estimated starvation delay
        /// (seconds the pod's free-flow arrival exceeds the target station's EST). Added to the
        /// minimized output pod score so late pods are avoided. Mirrors SAM1GConfiguration.DelayPenaltyWeight.
        /// </summary>
        public double DelayPenaltyWeight = 10.0;
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "obSAMP" + (FastLane ? "y" : "n") + "-w" + OrderRewardSec.ToString("0")
                + "-k" + TopKOrders.ToString() + (UseEtaGreedyVariant ? "-v2" : "-v1")
                + "-t" + TravelTimeWeight.ToString("0.##") + "-h" + CompletableHorizon.ToString();
        }
    }
    /// <summary>
    /// ALNS (Adaptive Large Neighborhood Search) order-batching configuration. Inherits HADGS knobs;
    /// optimizes M1G's objective via destroy/repair + simulated-annealing acceptance under a per-epoch
    /// time budget, warm-started from a HADGS-style greedy. See <see cref="Control.Defaults.OrderBatching.ALNSManager"/>.
    /// </summary>
    public class ALNSConfiguration : HADGSConfiguration
    {
        /// <summary>Use the M1G-style SituationInvestigated trigger gate by default.</summary>
        public ALNSConfiguration()
        {
            UseM1GTriggerGate = true;
        }
        /// <summary>Returns the type of the corresponding method this configuration belongs to.</summary>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.ALNS_OB; }

        /// <summary>Per-epoch wall-clock budget (ms) for the ALNS search. 0 ⇒ commit the greedy warm-start only (sanity mode).</summary>
        public int TimeBudgetMs = 30;
        /// <summary>Hard cap on ALNS iterations per epoch. 0 ⇒ greedy warm-start only.</summary>
        public int MaxIterations = 2000;
        /// <summary>Initial simulated-annealing temperature.</summary>
        public double InitialTemperature = 50.0;
        /// <summary>Geometric cooling factor applied per iteration.</summary>
        public double CoolingRate = 0.999;
        /// <summary>Minimum fraction of current moves removed by a destroy operator.</summary>
        public double DestroyMinFraction = 0.15;
        /// <summary>Maximum fraction of current moves removed by a destroy operator.</summary>
        public double DestroyMaxFraction = 0.4;
        /// <summary>Cap on the number of most-urgent pending orders considered per epoch (0 = unlimited).</summary>
        public int MaxOrdersConsidered = 0;
        /// <summary>Objective weight on travel (M1G w1). Default 1.</summary>
        public double ObjW1 = 1.0;
        /// <summary>Reward per covered order (M1G |w2|). Default 40.</summary>
        public double ObjOrderReward = 40.0;
        /// <summary>Penalty per empty station slot (M1G w3). Default 1000.</summary>
        public double ObjW3 = 1000.0;

        /// <summary>Returns a name identifying the method.</summary>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "obALNS" + (FastLane ? "y" : "n") + "-b" + TimeBudgetMs.ToString() + "-i" + MaxIterations.ToString();
        }
    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class LinesInCommonOrderBatchingConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.LinesInCommon; }
        /// <summary>
        /// Indicates how to break ties when assigning orders.
        /// </summary>
        public OrderSelectionTieBreaker TieBreaker = OrderSelectionTieBreaker.EarliestDueTime;
        /// <summary>
        /// Indicates whether a fast lane slot is used, i.e. one slot of each station is kept free for immediately fulfillable orders.
        /// </summary>
        public bool FastLane = false;
        /// <summary>
        /// Indicates how to break ties when assigning fast lane orders.
        /// </summary>
        public FastLaneTieBreaker FastLaneTieBreaker = FastLaneTieBreaker.EarliestDueTime;
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obLC" + (FastLane ? "y" : "n");
            return name;
        }
    }

    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class QueueOrderBatchingConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.Queue; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "obQ";
            name += QueueLength.ToString(IOConstants.FORMATTER);
            return name;
        }
        /// <summary>
        /// The length of the order queue per station.
        /// </summary>
        public int QueueLength = 20;
        /// <summary>
        /// Indicates whether there always is one capacity slot of a station reserved for an immediately fulfillable order.
        /// </summary>
        public bool FastLane = true;

        /// <summary>
        /// Rule settings for selecting an order to be assigned to a station (main rule).
        /// </summary>
        public QueueOrderSelectionRuleConfig StationOrderSelectionRule1 = new QueueOrderSelectionDeadlineVacant();
        /// <summary>
        /// Rule settings for selecting an order to be assigned to a station (first tie breaker).
        /// </summary>
        public QueueOrderSelectionRuleConfig StationOrderSelectionRule2 = new QueueOrderSelectionCompleteable() { OnlyNearestPod = true };
        /// <summary>
        /// Rule settings for selecting an order to be assigned to a station (second tie breaker).
        /// </summary>
        public QueueOrderSelectionRuleConfig StationOrderSelectionRule3 = new QueueOrderSelectionCompleteable() { OnlyNearestPod = false };
        /// <summary>
        /// Rule settings for selecting an order for an open fast lane slot of a station (main rule).
        /// </summary>
        public QueueOrderSelectionRuleConfig FastLaneOrderSelectionRule1 = new QueueOrderSelectionMostLines();
        /// <summary>
        /// Rule settings for selecting an order for an open fast lane slot of a station (first tie breaker).
        /// </summary>
        public QueueOrderSelectionRuleConfig FastLaneOrderSelectionRule2 = new QueueOrderSelectionEarliestDeadline();
        /// <summary>
        /// Rule settings for selecting an order for an open fast lane slot of a station (second tie breaker).
        /// </summary>
        public QueueOrderSelectionRuleConfig FastLaneOrderSelectionRule3 = new QueueOrderSelectionFCFS();
        /// <summary>
        /// Rule settings for selecting an order for the queue of a station (main rule).
        /// </summary>
        public QueueOrderSelectionRuleConfig QueueOrderSelectionRule1 = new QueueOrderSelectionInboundMatches();
        /// <summary>
        /// Rule settings for selecting an order for the queue of a station (first tie breaker).
        /// </summary>
        public QueueOrderSelectionRuleConfig QueueOrderSelectionRule2 = new QueueOrderSelectionRelated();
        /// <summary>
        /// Rule settings for selecting an order for the queue of a station (second tie breaker).
        /// </summary>
        public QueueOrderSelectionRuleConfig QueueOrderSelectionRule3 = new QueueOrderSelectionEarliestDeadline();

        /// <summary>
        /// Distinguishes between different order selection strategies for the queue order manager.
        /// </summary>
        public enum QueueOrderSelectionRuleType
        {
            /// <summary>
            /// A random selection rule.
            /// </summary>
            Random,
            /// <summary>
            /// A first come first served selection rule.
            /// </summary>
            FCFS,
            /// <summary>
            /// Prefers the earliest deadline.
            /// </summary>
            EarliestDeadline,
            /// <summary>
            /// Prefers deadlines that are becoming vacant.
            /// </summary>
            VacantDeadline,
            /// <summary>
            /// Prefers orders with matches along the inbound pods.
            /// </summary>
            InboundMatches,
            /// <summary>
            /// Prefers orders that can be completed quickly.
            /// </summary>
            Completable,
            /// <summary>
            /// Prefers orders with most lines.
            /// </summary>
            Lines,
            /// <summary>
            /// Prefers orders related to already assigned ones.
            /// </summary>
            Related,

        }
        /// <summary>
        /// The base config for all order selection rules implemented by this manager.
        /// </summary>
        [XmlInclude(typeof(QueueOrderSelectionRandom))]
        [XmlInclude(typeof(QueueOrderSelectionFCFS))]
        [XmlInclude(typeof(QueueOrderSelectionEarliestDeadline))]
        [XmlInclude(typeof(QueueOrderSelectionDeadlineVacant))]
        [XmlInclude(typeof(QueueOrderSelectionInboundMatches))]
        [XmlInclude(typeof(QueueOrderSelectionCompleteable))]
        [XmlInclude(typeof(QueueOrderSelectionMostLines))]
        [XmlInclude(typeof(QueueOrderSelectionRelated))]
        public abstract class QueueOrderSelectionRuleConfig
        {
            /// <summary>
            /// Returns the type of this selection rule.
            /// </summary>
            /// <returns>The type of this selection rule.</returns>
            public abstract QueueOrderSelectionRuleType Type();
        }
        /// <summary>
        /// The config for the random selection rule.
        /// </summary>
        public class QueueOrderSelectionRandom : QueueOrderSelectionRuleConfig
        {
            /// <summary>
            /// Returns the type of this selection rule.
            /// </summary>
            /// <returns>The type of this selection rule.</returns>
            public override QueueOrderSelectionRuleType Type() { return QueueOrderSelectionRuleType.Random; }
        }
        /// <summary>
        /// The config for the FCFS selection rule.
        /// </summary>
        public class QueueOrderSelectionFCFS : QueueOrderSelectionRuleConfig
        {
            /// <summary>
            /// Returns the type of this selection rule.
            /// </summary>
            /// <returns>The type of this selection rule.</returns>
            public override QueueOrderSelectionRuleType Type() { return QueueOrderSelectionRuleType.FCFS; }
        }
        /// <summary>
        /// The config for the earliest deadline selection rule.
        /// </summary>
        public class QueueOrderSelectionEarliestDeadline : QueueOrderSelectionRuleConfig
        {
            /// <summary>
            /// Returns the type of this selection rule.
            /// </summary>
            /// <returns>The type of this selection rule.</returns>
            public override QueueOrderSelectionRuleType Type() { return QueueOrderSelectionRuleType.EarliestDeadline; }
        }
        /// <summary>
        /// The config for the deadline closer than X selection rule.
        /// </summary>
        public class QueueOrderSelectionDeadlineVacant : QueueOrderSelectionRuleConfig
        {
            /// <summary>
            /// Returns the type of this selection rule.
            /// </summary>
            /// <returns>The type of this selection rule.</returns>
            public override QueueOrderSelectionRuleType Type() { return QueueOrderSelectionRuleType.VacantDeadline; }
            /// <summary>
            /// All orders with a deadline earlier than the given cutoff will be considered vacant.
            /// </summary>
            public double VacantOrderCutoff = 600;
        }
        /// <summary>
        /// The config for the inbound inventory match count selection rule.
        /// </summary>
        public class QueueOrderSelectionInboundMatches : QueueOrderSelectionRuleConfig
        {
            /// <summary>
            /// Returns the type of this selection rule.
            /// </summary>
            /// <returns>The type of this selection rule.</returns>
            public override QueueOrderSelectionRuleType Type() { return QueueOrderSelectionRuleType.InboundMatches; }
            /// <summary>
            /// The distance within which available material will get a score bonus. Matches behind this distance are considered equally.
            /// </summary>
            public double DistanceForWeighting = 10;
        }
        /// <summary>
        /// The config for the immediate order completability selection rule.
        /// </summary>
        public class QueueOrderSelectionCompleteable : QueueOrderSelectionRuleConfig
        {
            /// <summary>
            /// Returns the type of this selection rule.
            /// </summary>
            /// <returns>The type of this selection rule.</returns>
            public override QueueOrderSelectionRuleType Type() { return QueueOrderSelectionRuleType.Completable; }
            /// <summary>
            /// Indicates whether only the nearest pod is considered for really immediately completable orders.
            /// </summary>
            public bool OnlyNearestPod = true;
        }
        /// <summary>
        /// The config for the most lines selection rule.
        /// </summary>
        public class QueueOrderSelectionMostLines : QueueOrderSelectionRuleConfig
        {
            /// <summary>
            /// Returns the type of this selection rule.
            /// </summary>
            /// <returns>The type of this selection rule.</returns>
            public override QueueOrderSelectionRuleType Type() { return QueueOrderSelectionRuleType.Lines; }
            /// <summary>
            /// Prefers orders with most units overall instead of most lines.
            /// </summary>
            public bool UnitsInsteadOfLines = true;
        }
        /// <summary>
        /// The config for the related to order pool selection rule.
        /// </summary>
        public class QueueOrderSelectionRelated : QueueOrderSelectionRuleConfig
        {
            /// <summary>
            /// Returns the type of this selection rule.
            /// </summary>
            /// <returns>The type of this selection rule.</returns>
            public override QueueOrderSelectionRuleType Type() { return QueueOrderSelectionRuleType.Related; }
        }
    }

    /// <summary>
    /// The configuration for the greedy order-splitting heuristic manager.
    /// See docs/superpowers/specs/2026-07-02-order-splitting-consolidation-enabler-design.md.
    /// </summary>
    public class SplitHeuristicConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SplitHeuristic; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBSPLITH"; }
        /// <summary>
        /// M2 (cross-time) splitting: residual demand may stay in the backlog for later epochs.
        /// If false (M1, cross-station only), an order is only assigned when its complete remaining demand fits this epoch.
        /// </summary>
        public bool CrossTime = true;
        /// <summary>
        /// Maximal number of children an order may be split into per epoch.
        /// </summary>
        public int MaxChildrenPerOrder = 2;
        /// <summary>
        /// Cap of units per child (0 = no cap). Only effective when CrossTime is enabled.
        /// </summary>
        public int MaxUnitsPerChild = 0;
    }

    /// <summary>
    /// Configuration of the MILP-based order-splitting manager: M1G with shi2 relaxed to a
    /// unit-level quantity assignment q[o,i,s]. Inherits M1GConfiguration so all engine
    /// type-routing checks ("is M1GConfiguration") pass without engine changes (SAM1G precedent).
    /// See docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md.
    /// </summary>
    public class SplitM1GConfiguration : M1GConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SplitM1G; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBSPLITM1G"; }
        /// <summary>
        /// M2 (cross-time) splitting: Σs q[o,i,s] ≤ residual, leftovers stay in the backlog.
        /// If false (M1, cross-station only): Σs q[o,i,s] = residual · z[o] (all-or-nothing this epoch).
        /// </summary>
        public bool CrossTime = true;
        /// <summary>
        /// Per-unit assignment reward w2' in the objective (negative = reward). Replaces the
        /// per-order reward w2=-40 of plain M1G; -40 keeps the same magnitude per unit.
        /// </summary>
        public double UnitRewardWeight = -40;
    }

    /// <summary>
    /// SplitM1G with pod-level attribution decided inside the MILP: q[i,o,p,s] replaces
    /// q[i,o,s], so the solver itself picks which specific pod serves each unit instead of a
    /// post-solve greedy pass. Reward switches from per-unit (UnitRewardWeight) to per-order
    /// completion, fixing the known orders-vs-items confound of plain SplitM1G.
    /// </summary>
    public class SplitM1GExactConfiguration : SplitM1GConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SplitM1GExact; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBSPLITM1GX"; }
        /// <summary>
        /// Per-order-completion reward w2 in the objective (negative = reward). Applies to
        /// zfullx[o] (M1e) or zdonex[o] (M2e) — not used the way UnitRewardWeight (inherited,
        /// unused here) was in SplitM1G.
        /// </summary>
        public double OrderRewardWeight = -40;
        /// <summary>
        /// Idle-slot penalty w3 in the objective. Default 1000 reproduces the inherited
        /// M1G-family slot-forcing behavior exactly. Set to 0 to align the objective with
        /// PVGS's trip-rationing economics: a pod trip must pay for itself through
        /// |OrderRewardWeight| per completed order versus distance - no fill-the-slot subsidy.
        /// </summary>
        public double IdleSlotWeight = 1000;
        /// <summary>
        /// Per-trip fixed cost w4 added to the pod-station cost coefficient of every
        /// newly-claimable pod assignment (the Xie et al. 2021 pod-visit objective term).
        /// Default 0 = current behavior; also the natural hook for the energy model's
        /// per-trip constant (rotation/lift) component.
        /// </summary>
        public double PodTripFixedCost = 0;
        /// <summary>
        /// Caps the number of NEW pod trips (newly claimed Pa pods) a single decision may
        /// open: sum over p in Pa of xps[p,s] &lt;= K. Restores the sequential trip
        /// discipline a per-epoch snapshot optimizer cannot express through static prices
        /// alone (decisions re-trigger on every freed slot, so small K does not starve
        /// supply - PVGS empirically dispatches ~0.24 pods per decision). 0 = unlimited
        /// (bit-identical default).
        /// </summary>
        public int MaxNewPodTripsPerDecision = 0;
        /// <summary>
        /// Per-unit reward w5 (negative = reward) for every unit drawn from the pod
        /// CURRENTLY BEING PROCESSED at a station (its bot stands at the station's pick
        /// waypoint) - NOT from queueing or en-route inbound pods. The processing pod's
        /// inventory is a perishable opportunity: once its assigned picks finish it leaves,
        /// while queued pods have many future epochs left. Rewarding all inherited pods
        /// equally would let queue-servable orders crowd out the closing window. Keep |w5|
        /// well below |OrderRewardWeight| so completing whole orders always dominates raw
        /// unit milking. Default 0 = term omitted entirely (bit-identical model).
        /// </summary>
        public double ProcessingPodDrawReward = 0;
        /// <summary>
        /// Per-unit reward epsilon (negative = reward) for EVERY assigned unit q[i,o,p,s],
        /// regardless of pod phase - the lexicographic item-pile-on secondary objective
        /// (user decision 2026-07-12): keep |epsilon| well below |OrderRewardWeight| so it
        /// only breaks ties among completion-equivalent solutions toward drawing more
        /// items per pod visit. Shared by the exact model and PVGS-E (which inherits this
        /// field), so both sides price the same epsilon by construction. Default 0 = term
        /// omitted entirely (bit-identical model).
        /// </summary>
        public double UnitDrawReward = 0;
        /// <summary>
        /// Coverage-first lexicographic objective (spec: docs/superpowers/specs/
        /// 2026-07-12-coverage-first-objective-design.md). Solve 1 maximizes
        /// completions + PoolCoverWeight * whole-backlog pool coverage
        /// - PodSelectTiebreakCost * new pod trips, with NO distance term; Solve 2
        /// minimizes travel within the locked Solve-1 optimum (pod->station->bot
        /// assignment and split shapes emerge from distance there). PVGS-E mirrors the
        /// same objective greedily when this flag is set. Default false = bit-identical
        /// single-solve legacy objective.
        /// </summary>
        public bool CoverageFirstScoring = false;
        /// <summary>
        /// Beta: weight per unit of whole-backlog pool coverage in Solve 1. Calibrate
        /// beta * max-per-SKU-pool-demand &lt; 1 so pool coverage never outbids one
        /// completed order. Only read when CoverageFirstScoring is true.
        /// </summary>
        public double PoolCoverWeight = 0.005;
        /// <summary>
        /// Epsilon_S: fixed charge per NEW pod trip in Solve 1 (pod-visit minimization
        /// layer, below beta in the hierarchy). Only read when CoverageFirstScoring is
        /// true.
        /// </summary>
        public double PodSelectTiebreakCost = 0.01;
        /// <summary>
        /// (M2e-PR) B: true-completion bonus per order whose REMAINING demand is fully
        /// assigned this solve, under unfiltered zfin semantics - out-of-stock SKUs force
        /// zfin=0 instead of being silently skipped the way eM2done does (the missing-SKU
        /// slice-arbitrage hole). Replaces OrderRewardWeight's zdonex reward when active.
        /// Only defined for CrossTime=true (eM1 is an all-or-nothing equality - no partial
        /// exists to reward). 0 = feature off, legacy zdonex/w2 path, bit-identical.
        /// Spec: docs/superpowers/specs/2026-07-14-m2e-pr-design.md.
        /// </summary>
        public double TrueCompletionReward = 0;
        /// <summary>
        /// (M2e-PR) R: pro-rata reward. Each assigned unit of order o earns R/D_o where
        /// D_o is the order's ORIGINAL overall demand (GetDemandCount(), includes
        /// currently out-of-stock SKUs) so slice rewards across epochs sum to exactly R.
        /// Only read when TrueCompletionReward &gt; 0. Keep R &lt;= TrueCompletionReward;
        /// the completion hierarchy R*frac &lt; R &lt; B+R holds structurally for any R&gt;0.
        /// </summary>
        public double ProRataReward = 0;
        /// <summary>
        /// M2e-SF: before the legacy objective, lexicographically maximize parent orders
        /// fully supplied by inherited Pb pods, then the residual item count of those
        /// completed orders. Active only when CrossTime is true. Default false keeps the
        /// existing one-solve path behavior-identical. Spec: docs/superpowers/specs/
        /// 2026-07-14-m2e-sunk-first-design.md.
        /// </summary>
        public bool SunkFirstScoring = false;
        /// <summary>
        /// M2e-AE: pod-centric adaptive control. Each event first exhausts full orders from
        /// inherited/processing pods, then repeatedly fixes the new pod/station with the
        /// greatest marginal completed-order value and re-solves exact SKU-order-pod-station
        /// OA while slots remain. Partial progress is a final fallback. Default false
        /// preserves the existing one-shot M2e path.
        /// </summary>
        public bool AdaptiveExactResweeps = false;
        /// <summary>
        /// Normal cross-event supply target behind the pod physically at a station. Positive-
        /// value resweeps may exceed it inside the same decision event while slots remain.
        /// </summary>
        public int AdaptiveFuturePodTarget = 1;
        /// <summary>
        /// Maximum pods fixed into one exact OA burst. The first pods satisfy periodic
        /// station supply; every additional pod must add at least one completable order to
        /// the already selected bundle. Default 1 is conservative and inert while M2e-AE is
        /// disabled.
        /// </summary>
        public int AdaptiveMaxPodBurst = 1;
        /// <summary>
        /// Allows one additional future pod only when the station already has its normal
        /// pipeline target but the live work projection still contains a starvation gap.
        /// Default false keeps the conservative fixed-target behavior.
        /// </summary>
        public bool AdaptiveRiskPrefetch = false;
        /// <summary>
        /// Minimum projected station starvation gap, in seconds, required to open the
        /// risk-triggered second-future-pod allowance.
        /// </summary>
        public double AdaptiveRiskPrefetchGapSec = 1.0;
        /// <summary>
        /// When positive, a station whose physical processing pod has more than this many
        /// seconds of committed work first receives a Pb-only OA sweep. Periodic PS opens
        /// once the release window is shorter, or immediately when Pb cannot complete an
        /// order. Zero disables this timing gate.
        /// </summary>
        public double AdaptivePeriodicSupplyLeadTimeSec = 0.0;
        /// <summary>
        /// Optional ablation: prioritize 2*completed-orders - station-parts before raw
        /// completions. Default false keeps raw throughput first and parts as its next guard.
        /// </summary>
        public bool AdaptiveSlotEfficientCompletion = false;
        /// <summary>
        /// Maximum number of committed completion/supply resweeps in one external decision.
        /// </summary>
        public int AdaptiveExactResweepLimit = 32;
    }

    /// <summary>
    /// M2e-IC (Inbound-Committed Split, spec v4): SplitM1GExact plus (P1) split orders draw
    /// only from committed pods while storage pods serve WHOLE orders only, (SG) a
    /// state-conditional split gate (new partials bridge a dying processing pod only),
    /// (D9) a multi-part penalty, (D11) a lead-gated pipeline floor, (D14) a selected-pod
    /// residual-coverage tie-break, (D15) pool-scarcity-weighted squeeze and (PK) an
    /// optional per-split-parent packing budget (Xie et al. 2021 Appx B).
    /// Spec: docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md.
    /// </summary>
    public class SplitM2eICConfiguration : SplitM1GExactConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SplitM2eIC; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBSPLITM2EIC"; }
        /// <summary>
        /// (PK) Box slots per abstract packing station (Xie et al. 2021 Appendix B derives
        /// 78 boxes per shelf). Total capacity C = PackingStationCount * this, no
        /// per-station attribution: any split parent occupies one box from first split
        /// until consolidation. &lt;= 0 = unlimited (constraint absent).
        /// </summary>
        public int PackingBufferCapacity = 78;
        /// <summary>
        /// (PK) Number of abstract packing stations; capacity grows linearly
        /// (C = PackingStationCount * PackingBufferCapacity). &lt;= 0 = unlimited.
        /// </summary>
        public int PackingStationCount = 1;
        /// <summary>
        /// (D9) wp: penalty per station-part beyond an order's first. Soft whole-preference:
        /// keep 2*IdleSlotWeight &lt; MultiPartPenalty &lt; |OrderRewardWeight| so gratuitous
        /// multi-station shredding loses while genuinely-needed multi-station completions
        /// stay affordable. 0 = off.
        /// </summary>
        public double MultiPartPenalty = 12;
        /// <summary>
        /// (D11) w_pipe: penalty per unit of per-station pipeline shortfall while the lead
        /// gate is open. Size between the net new-trip completion value and
        /// |OrderRewardWeight| so a slot+trip gets dedicated to the NEXT pod even while
        /// the current one still offers completions.
        /// </summary>
        public double PipelineFloorWeight = 20;
        /// <summary>
        /// (D11) Lead: dispatch the next pod once the processing pod's remaining committed
        /// work is within this many seconds (AE-validated 70; lead=0 over-supplies).
        /// </summary>
        public double PipelineFloorLeadSec = 70;
        /// <summary>
        /// (D11) T: future pods (queued + en-route) targeted per station beyond the
        /// physical one. AE-validated 1 (i.e. two-pod pipeline); also the hard cap.
        /// </summary>
        public int PipelineFloorTarget = 1;
        /// <summary>(D11) Master switch for the pipeline floor.</summary>
        public bool PipelineFloorEnabled = true;
        /// <summary>
        /// (Starvation-aware dispatch) Master switch: add an explicit station-starvation
        /// reward to the xps (pod->station) objective term. For each candidate dispatch whose
        /// station is projected to starve (StarvationGapSec > 0) and that a free-flow-arriving
        /// pod can reach before the station's EST, the objective is rewarded by
        /// StarvationWeight * StarvationGapSec — so the MILP dispatches pods to feed stations
        /// before they go idle (fixes M3G under-feeding vs the greedy HGS-M3). Off = whole-block
        /// skip, bit-identical to M3G MINCORE. See docs/superpowers/specs/2026-07-28-m3g-starvation-aware-design.md.
        /// </summary>
        public bool StarvationAwareDispatch = false;
        /// <summary>(Starvation-aware dispatch) w_starve: reward weight per second of station
        /// starvation gap relieved by an in-time dispatch. Initial 1.0; sweep later. Ignored when
        /// StarvationAwareDispatch is false.</summary>
        public double StarvationWeight = 1.0;
        /// <summary>
        /// (Remove soft floor) When true, skip the icLG1 soft pipeline floor entirely — the
        /// penalized "dispatch up to T when the lead gate is open" pressure is removed, so
        /// dispatch is driven purely by the completion/distance objective (no pod-count floor).
        /// The hard icLGcap anti-oversupply cap is UNAFFECTED. Off = current M3G (bit-identical).
        /// </summary>
        public bool PipelineSoftFloorDisabled = false;
        /// <summary>
        /// (Force-feed constraint) When true, add a HARD constraint (no penalty): a station that
        /// will starve within ForceFeedHorizonSec AND for which at least one storage pod can
        /// free-flow-arrive before its EST MUST receive at least one such timely pod dispatch.
        /// Re-couples pod supply to station work at the unit/timing level (M1G-analog for the
        /// split model) instead of a penalty/floor. Feasibility-bounded: only applied when a
        /// timely candidate exists, so it can never make the MILP infeasible. Off = no constraint.
        /// Intended to be used with PipelineSoftFloorDisabled=true and DispatchCapEnabled=false.
        /// </summary>
        public bool ForceFeedConstraint = false;
        /// <summary>(Force-feed) planning horizon in seconds: only force-feed a station whose EST
        /// is within this window (0 &lt; EST &lt;= horizon). Beyond it there is still ample time, so a
        /// later epoch handles it. Default 120. Ignored when ForceFeedConstraint is false.</summary>
        public double ForceFeedHorizonSec = 120;
        /// <summary>
        /// (Line-closure reward) W_line: adds a per-order-line closure variable c[o,i] (=1 iff
        /// SKU i's full demand for order o is served, Σ_s q[o,i,s] &gt;= demand·c) and rewards it in
        /// the objective by -W_line·Σc. Realises the MILP counterpart of HGS's lexicographic
        /// "close order-lines" tier: value structural progress (a finished SKU-position) above raw
        /// item coverage, suppressing partial-item flooding. Keep |OrderRewardWeight| ≫ W_line ≫
        /// distance for near-strict lexicographic (orders completed ≫ lines closed ≫ distance).
        /// 0 = off, bit-identical to MINCORE. Items are NOT rewarded (that would flood).
        /// </summary>
        public double LineClosureWeight = 0;
        /// <summary>
        /// (No-artificial-cap) M1G has no per-round limit on how many new pods a station may
        /// claim - it dispatches however many genuinely-justified (order-demand-backed, per
        /// eshi13') pods it needs, bounded only by real resources (bots, pod uniqueness).
        /// When false (default = TRUE, i.e. cap ENABLED, current behavior unchanged), the
        /// icLGcap constraint (new dispatches this round &lt;= PipelineFloorTarget - future)
        /// is skipped entirely - eshi13'/P1/SG remain fully in force, so a new Pa pod still
        /// requires a genuinely justifying order; this only removes the ARTIFICIAL ceiling on
        /// how many such justified dispatches may happen in the same round. icLG1's soft
        /// shortfall push (using the same PipelineFloorTarget) is unaffected either way.
        /// </summary>
        public bool DispatchCapEnabled = true;
        /// <summary>
        /// (Dual-price probe) Diagnostic only, never influences a decision. When enabled, every
        /// DualPriceProbeEveryNDecisions-th decision additionally solves the relaxed "ideal
        /// allocation" LP for the same snapshot and logs its shadow prices - the marginal metre
        /// value of a station slot, of a unit of each SKU, and of a bot. Those are the quantities
        /// the online objective currently approximates with hand-tuned constants
        /// (IdleSlotWeight, LineClosureWeight, UnitDrawReward, the pod-tier draw penalties), so
        /// the log answers whether shadow prices are stable enough, discriminating enough, and
        /// how far the tuned constants sit from them. false = off, no extra solve.
        /// </summary>
        public bool DualPriceProbeEnabled = false;
        /// <summary>Probe cadence in decisions. Ignored when DualPriceProbeEnabled is false.</summary>
        public int DualPriceProbeEveryNDecisions = 50;
        /// <summary>Seconds allowed for one probe LP. &lt;= 0 = no limit.</summary>
        public double DualPriceProbeTimeLimitSec = 10;
        /// <summary>
        /// (Scout) Anticipatory dispatch: reward per unit of backlog-matching supply for a
        /// NEW Pa pod dispatched while the D11 lead gate is open, WITHOUT requiring any
        /// order to consume it this solve (relaxes eshi13' for that pod/station pair only).
        /// Decouples "when to send a bot for a pod" from "which order it serves" - the
        /// order binds honestly in whatever future epoch actually claims it, under the
        /// unchanged P1/SG rules. Shares D11's per-station new-dispatch budget
        /// (PipelineFloorTarget - future), so it can never inflate trip volume beyond the
        /// existing pipeline target; it only lets that budget be spent early. 0 = off
        /// (bit-identical - no scout term, eshi13' unconditional as before).
        /// </summary>
        public double AnticipatoryDispatchWeight = 0;
        /// <summary>
        /// (Value-dispatch) Removes eshi13's requirement that a newly-claimed Pa pod be
        /// consumed (q&gt;0) THIS solve. A Pa pod's dispatch is then justified purely by its
        /// backlog-coverage VALUE (the existing D14 CoverageRewardWeight term, elevated to a
        /// real magnitude rather than a tiny tie-break) competing against distance/PodTripFixedCost
        /// in the SAME objective - no order needs to complete or even partially draw from it
        /// this round. Safe: P1 (icP1a-d) is untouched and still forbids a fresh SPLIT order
        /// from ever drawing on a Pa pod - only a WHOLE order (or a closing parent) may, exactly
        /// as before. Once claimed, the pod leaves Pa and is governed entirely by the unchanged
        /// SG gate in later epochs, same as any other committed pod. Pair with
        /// PipelineFloorWeight=0 (removes the artificial per-station new-dispatch count cap,
        /// icLGcap) so bot count and station slot count are the only real limits, per design.
        /// False = eshi13' unconditional, bit-identical to all prior arms.
        /// </summary>
        public bool NewPodDispatchByValue = false;
        /// <summary>
        /// (Defer-to-processing) A decode-side filter, not a solver constraint: an order this
        /// solve would allocate is only actually committed (AllocateOrder + Ziops
        /// registration + split-child creation) when EVERY pod it draws from is currently the
        /// one being actively processed at its station. If not, the order is skipped entirely
        /// this round - no mutation happens - so it stays fully pending and gets freshly
        /// re-decided next epoch (perhaps against a different, by-then-available pod). Content
        /// matching (which pod serves which order) still comes straight out of the same MILP
        /// solve as today: only the physical act of filling a station slot is deferred. Adds no
        /// new solver constraint, so it cannot make the model infeasible. Pairs naturally with
        /// NewPodDispatchByValue: pods can be fetched speculatively, but a station slot is
        /// never spent on one that has not actually arrived. False = unchanged (every
        /// allocation this solve decides is committed immediately, as today).
        /// </summary>
        public bool DeferAllocationUntilProcessing = false;
        /// <summary>
        /// (Pod-tier draw cost) Penalty per unit drawn from a QUEUED (arrived, waiting behind
        /// the processing pod) pod, on top of the existing distance/completion terms. Drawing
        /// from the currently-processing pod stays free (0 cost, unchanged from today).
        /// Creates a soft high-to-low preference - processing pod first, queued pod only when
        /// it is still worth the penalty - without banning anything (no infeasibility risk,
        /// unlike a hard pod-state gate). 0 = off (bit-identical).
        /// </summary>
        public double QueuedPodDrawPenalty = 0;
        /// <summary>
        /// (Pod-tier draw cost) Penalty per unit drawn from an ON-THE-WAY (committed, neither
        /// processing nor queued yet) pod. Should be &gt;= QueuedPodDrawPenalty so the
        /// high-to-low preference (processing &gt; queued &gt; on-the-way) holds. 0 = off
        /// (bit-identical).
        /// </summary>
        public double OnTheWayPodDrawPenalty = 0;
        /// <summary>
        /// (Squeeze-coupled dispatch) When true, the D11 pipeline floor's soft gate opens
        /// EITHER when releaseLeft &lt;= PipelineFloorLeadSec (as today) OR whenever the SG
        /// squeeze window is already open at that station (Pp present, no queued successor -
        /// the same signal that permits splitting). Closes the gap where squeezing can start
        /// (successor not yet arrived) well before releaseLeft drops low enough to wake the
        /// floor - by the time squeezing is happening, supply is already proven thin, so the
        /// soft push to fetch a successor should fire then, not later. Still only a SOFT
        /// (icLG1) push under the SAME icLGcap budget - no new hard constraint, cannot make
        /// the model infeasible. False = unchanged (D11 gate is releaseLeft-only, as today).
        /// </summary>
        public bool CoupleDispatchToSqueezeWindow = false;
        /// <summary>
        /// (Split-fed dispatch) When true, the D11 pipeline floor's soft gate ALSO opens for
        /// a station if the PREVIOUS completed decode actually assigned a split (non-whole)
        /// order there - a REALIZED fact (the solver already confirmed no whole order fit,
        /// per the w2/wp weight tower's whole-first preference), not merely an eligibility
        /// precondition like CoupleDispatchToSqueezeWindow's SG-gate check. One-epoch lag
        /// (next solve reacts to the last one's outcome - avoids the circularity of a solve
        /// needing to know its own split decision before it runs), which given epochs fire
        /// every few seconds is effectively immediate. Still only a SOFT (icLG1) push under
        /// the SAME icLGcap budget - no new hard constraint, cannot make the model infeasible.
        /// False = unchanged (D11 gate is releaseLeft-only, as today).
        /// </summary>
        public bool CoupleDispatchToRecentSplit = false;
        /// <summary>(SG) Master switch for the split gate.</summary>
        public bool SplitGateEnabled = true;
        /// <summary>
        /// (SG) Strict = new partials only when the station has a processing pod AND no
        /// queued successor (bridge window = successor's travel window). False (loose) =
        /// processing pod present suffices (harvests the post-queue tail; ablation arm).
        /// </summary>
        public bool SplitGateStrict = true;
        /// <summary>
        /// (SG-twilight) &gt; 0 switches the split gate from position-based (no queued
        /// successor) to clock-based: new partials open while the CURRENT processing
        /// pod's releaseLeft &lt;= this many seconds, regardless of successor position.
        /// Decouples the split window from dispatch timing (pair with a large
        /// PipelineFloorLeadSec for always-on dispatch). 0 = legacy position mode.
        /// </summary>
        public double SplitGateTwilightSec = 0;
        /// <summary>
        /// (D14) Epsilon_cov: reward per unit of selected-pod-set coverage of the backlog
        /// residual pool (negative = reward). Among equal-completion pod sets this is
        /// exactly the leftover-coverage tie-break. Keep |value|*max-coverage well below
        /// |OrderRewardWeight|. 0 = off.
        /// </summary>
        public double CoverageRewardWeight = -0.2;
        /// <summary>
        /// (w2p) Parent-closing bonus: extra completion reward for orders that are
        /// existing split parents (their zdone closes a consolidation tail and frees a
        /// packing box). MILP counterpart of PVGS's ParentClosingBonus (=20, half of
        /// |OrderRewardWeight|); PVGS's 48s consolidation waits vs M2e-IC's 721s traced
        /// to exactly this missing priority. Rewards COMPLETION only - not in the
        /// three-times-dead partial-reward family. 0 = off, bit-identical.
        /// </summary>
        public double ParentClosingReward = 0;
        /// <summary>
        /// (adm) Parent admission priority: when the urgent-order set (Od) overflows the
        /// slots and replaces the backlog for this solve, existing split parents survive
        /// the replacement (treated like deadline orders). Fixes the documented
        /// Od-replacement exclusion (26% of M2e decisions dropped open parents entirely,
        /// see docs/2026-07-14-m2e-sunk-first-negative-result.md section 4).
        /// false = legacy replacement, bit-identical.
        /// </summary>
        public bool ParentAdmissionPriority = false;
        /// <summary>
        /// (close) Parent closing-only dispatch: an existing split parent may draw from
        /// storage-area (Pa) pods ONLY when the draw fully closes it this solve
        /// (zdone-gated icP1d) - a dedicated tail-ending trip, the MILP counterpart of
        /// PVGS's dispatch-side parent hunting (ParentClosingBonus). Partial fishing from
        /// Pa remains impossible (z=0 forces zero Pa draws). false = strict P1,
        /// bit-identical.
        /// </summary>
        public bool ParentClosingDispatch = false;
        /// <summary>
        /// (Split-driven dispatch) When true, ANY fresh order that COMPLETES this solve
        /// (zdone=1) may draw from storage-area (Pa) pods - i.e. a completing split can
        /// summon a fresh trip, not just a single-station whole. icP1d switches its gate
        /// variable from whole to zdone for fresh orders (OOS-residual orders excluded,
        /// same as the parent-closing path, since legacy zdonex skips out-of-stock SKUs and
        /// its z=1 is not a true close). Partial fishing stays impossible (zdone=0 forces
        /// zero Pa draws), so the anti-fishing seal is preserved - the distinction moves
        /// from "whole vs split" to "completes vs merely fishes". Fixes the sparse-supply
        /// deadlock where no whole order is feasible so no fresh pod is ever dispatched.
        /// false = strict whole-only P1d, bit-identical.
        /// </summary>
        public bool SplitCanDriveDispatch = false;

        /// <summary>(Set-level redesign, master switch) When true, the inbound-committed
        /// hard gates (icP1d / icSG1 / icSG2 constraints and the decode P1 assert) are
        /// NOT generated; dispatch is justified by the set-level "completion + order-level
        /// progress" objective instead, and new-pod partial draws are priced by
        /// NewPodPartialPenalty. Default false = current tier, bit-identical (whole gated
        /// blocks are skipped, not coefficient-zeroed). ProgressRewardWeight and
        /// NewPodPartialPenalty only take effect when this is true.</summary>
        public bool SoftInboundCommitted = false;

        /// <summary>(Set-level redesign) Order-level LINEAR progress reward magnitude
        /// (&gt;=0). Each assigned unit of order o earns -ProgressRewardWeight / D_o where
        /// D_o = order's ORIGINAL total demand (GetDemandCount), so per-epoch slices sum to
        /// the reward over the order's life. Structurally mirrors the PR pro-rata term but
        /// is decoupled from prMode/true-completion. Only applied when SoftInboundCommitted
        /// is true. 0 = no term.</summary>
        public double ProgressRewardWeight = 0;

        /// <summary>(Set-level redesign) Pod-tier draw cost 4th tier (gamma): per-unit cost
        /// of drawing from a brand-new storage (Pa) pod, on top of the existing
        /// Queued/OnTheWay tiers. Should be &gt;= OnTheWayPodDrawPenalty (a new pod is further
        /// from committed than an on-the-way one). Only applied when SoftInboundCommitted is
        /// true (else Pa pods keep the current OnTheWay penalty). 0 = no extra cost.</summary>
        public double NewPodPartialPenalty = 0;

        /// <summary>(Fill fairness) When true, a split parent is released from the ItemManager's
        /// available-order backlog on its FIRST split - freeing a Fill replenishment slot so a
        /// fresh order is injected at the same cadence M1G gets from whole-order assignment -
        /// while staying in _pendingOrders to serve its residual across periods. It is NOT marked
        /// complete; the parent completes only via child consolidation. Default false = release
        /// only at IsFullyClaimed (current behavior, bit-identical). Only affects Fill mode.</summary>
        public bool ReleaseParentOnFirstSplit = false;
    }

    /// <summary>
    /// Late-binding variant of SplitM1GExact (M2e-LB): identical MILP, but AllocateOrder is
    /// deferred from solve time to the moment a bot claims the pod trip - planned orders live
    /// in a deferred-binding ledger and only consume physical slot capacity when bound. The
    /// planning admission gate uses PlannedWipCap (W) instead of physical free slots, which is
    /// the load-bearing knob: W=6 reproduces the M2e ceiling, W>6 lets the pod pipeline run
    /// ahead of slot recycling. See docs/superpowers/specs/2026-07-12-m2e-lb-design.md.
    /// </summary>
    public class SplitM1GLBConfiguration : SplitM1GExactConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SplitM1GLB; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBSPLITM1GLB"; }
        /// <summary>
        /// W: per-station cap on in-flight orders (bound-incomplete + planned-unbound). Replaces
        /// the physical-free-slot semantics of Cs in the MILP. 6 = regression anchor (approximates
        /// legacy M2e admission); sweep upward to let planning run ahead of slot recycling.
        /// </summary>
        public int PlannedWipCap = 6;
        /// <summary>
        /// Seconds after which a planned-unbound order is force-bound by the watchdog (guards
        /// against _Ziops entries stranded by rerouted/cancelled pod trips). Binding still
        /// requires a free reservation slot; blocked watchdog bindings retry each update.
        /// </summary>
        public double BindingWatchdogTimeout = 180;
        /// <summary>
        /// Stage-2 flag (parsed but INERT in stage 1): opportunistic backfill of pending orders
        /// from the claimed pod's residual stock at binding time. Implementation lands in a
        /// follow-up plan only if the W sweep verdict is positive. Default false.
        /// </summary>
        public bool LateBindingBackfill = false;
    }

    /// <summary>
    /// Pod-Value Greedy Splitting (PVGS): the fast heuristic counterpart of SplitM1GExact,
    /// positioned as HADGS is to M1G. Pod-centric greedy driven by a residual-coverage value
    /// index; commits exact ledger claims through the Spec 1 enabler pipeline - no MILP.
    /// CrossTime=false selects PVGS-M1e (per-epoch all-or-nothing), true selects PVGS-M2e
    /// (partial service allowed, residuals stay in the backlog).
    /// </summary>
    public class PVGSConfiguration : SplitM1GExactConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.PVGS; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBPVGS"; }
        /// <summary>Dispatch-score weight per newly completable order (mirrors |w2|=40 of the exact objective).</summary>
        public double CompletionWeight = 40;
        /// <summary>Dispatch-score weight per meter of bot-to-pod plus pod-to-station distance (mirrors w1=1).</summary>
        public double DistanceWeight = 1;
        /// <summary>M2e only: dispatch-score weight per scarcity-weighted unit of partial progress a candidate pod offers.</summary>
        public double PartialUnitWeight = 1;
        /// <summary>M2e only: dispatch-score bonus per newly completable order that is an open split parent (closes a consolidation tail).</summary>
        public double ParentClosingBonus = 20;
        /// <summary>Scarcity exponent beta in the pod value index: value += min(avail, R) * (1 + beta * R / supply).</summary>
        public double ScarcityBeta = 1;
        /// <summary>M2e only: minimum units for a NEW partial child (anti-fragmentation threshold theta).</summary>
        public int MinPartialUnits = 2;
        /// <summary>Number of top-value candidate pods evaluated per dispatch iteration.</summary>
        public int ShortlistK = 15;
        /// <summary>
        /// NoSplit control arm: restricts PVGS to single-station full-order commits
        /// (HADGS-equivalent order semantics) - no cross-station children, no partials.
        /// Used to isolate the pure splitting increment against regular PVGS with the
        /// identical engine (PVGS-NoSplit vs HADGS validates engine parity; PVGS vs
        /// PVGS-NoSplit is the same-engine splitting gain). Default false.
        /// </summary>
        public bool DisableSplitting = false;
        /// <summary>
        /// PVGS-E mode: makes PVGS a faithful epoch greedy of the SplitM1GExact objective
        /// (see docs/superpowers/specs/2026-07-12-pvgs-e-design.md). When true: the dispatch
        /// score becomes CompletionWeight*newCompletions - UnitDrawReward*units -
        /// DistanceWeight*distance - PodTripFixedCost (no PartialUnitWeight, no
        /// ParentClosingBonus), the coverage-only shortlist truncation is disabled (all
        /// relevant candidates are scored), and the PartialSweep/MinPartialUnits partial
        /// engine is replaced by the epsilon SqueezeSweep (active only when
        /// UnitDrawReward != 0). Default false = bit-identical to regular PVGS.
        /// </summary>
        public bool ExactAlignedScoring = false;
        /// <summary>
        /// (Pod-tier draw preference) Greedy analogue of SplitM2eIC's pod-tier draw cost:
        /// when filling an order's units from the pods at a station, drain PROCESSING pods
        /// (bot at the station pick waypoint) first, then QUEUED pods (bot at a queue
        /// waypoint), and only last the ON-THE-WAY / freshly-dispatched pods. This makes
        /// PVGS squeeze already-present sunk supply before leaning on incoming trips - the
        /// same intent the MILP encodes as QueuedPodDrawPenalty/OnTheWayPodDrawPenalty, here
        /// realised as a hard ordering (a greedy fill has no soft objective to price into).
        /// Overrides the legacy "prefer the newly-dispatched pod" bias within each tier only.
        /// Default false = legacy prefer-dispatched ordering, bit-identical.
        /// </summary>
        public bool PodTierDrawPreference = false;
        /// <summary>
        /// (Force-fill empty slots) Greedy analogue of M1G's w3=1000 IdleSlotWeight: when the
        /// value-driven dispatch loop would otherwise stop with free slots still open and
        /// uncommitted orders remaining (no candidate pod has a positive completion score),
        /// force-dispatch the highest-COVERAGE candidate anyway - even if it completes nothing
        /// this epoch. This guarantees the decode never leaves a station idle while work and
        /// capacity exist, so it can never fall into the empty-output state that (with Fill's
        /// pull-based generation + the event-driven trigger) freezes the whole system. Mirrors
        /// the hypothesis that the observed PVGS/nocap deadlock is caused by NOT forcing slots
        /// filled (unconditional idle-slot pressure), the same lever that cured the nocap
        /// deadlock on the MILP side. Default false = value-only dispatch, bit-identical.
        /// </summary>
        public bool ForceFillEmptySlots = false;
        /// <summary>
        /// (Force-fill budget) Max number of coverage-fallback dispatches ForceFillEmptySlots
        /// may make PER decode epoch. 0 = unlimited (fills every idle slot - brute-force, most
        /// M1G-like, lowest pile-on). A small positive value (e.g. 1) keeps PVGS alive with a
        /// minimal keep-awake dispatch only when the value-driven loop would otherwise produce
        /// nothing, letting the efficient value logic dominate the rest of the epoch - so
        /// pile-on / EOR sit BETWEEN brute-force M1G and the optimal MILP. Tunes the heuristic's
        /// efficiency-vs-liveness point. Ignored when ForceFillEmptySlots is false.
        /// </summary>
        public int ForceFillMaxPerEpoch = 0;
    }

    /// <summary>
    /// HGS-M3: greedy heuristic counterpart of M3G (SplitM2eIC MINCORE), as HADGS is to M1G.
    /// Inherits PVGS's engine config; GetMethodType routes to GreedyM3GManager. Adds the
    /// PipelineFloor parameters (mirrored from SplitM2eIC) that the faithful M3G marginal
    /// dispatch score needs. Solution space is a subset of M3G by construction.
    /// </summary>
    public class GreedyM3GConfiguration : PVGSConfiguration
    {
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.GreedyM3G; }
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBGREEDYM3G"; }
        /// <summary>w_pipe: soft-objective weight per unit of per-station pipeline shortfall while the lead gate is open. Mirrors SplitM2eIC PipelineFloorWeight (20).</summary>
        public double PipelineFloorWeight = 20;
        /// <summary>Lead seconds for the pipeline gate. Mirrors SplitM2eIC PipelineFloorLeadSec (70).</summary>
        public double PipelineFloorLeadSec = 70;
        /// <summary>Target future (inbound-minus-processing) pods per station; hard anti-oversupply cap. Mirrors SplitM2eIC PipelineFloorTarget (1).</summary>
        public int PipelineFloorTarget = 1;
        /// <summary>
        /// (Lexicographic scoring) When true, the dispatch scorer ranks candidates by strict
        /// lexicographic tiers instead of a weighted sum: (orders completed, order-lines closed,
        /// items served, feeds-a-gate-open-starving-station, then nearest). A pod-set's value is
        /// "complete orders first, then close lines, then serve items"; starvation feed and
        /// distance are only tie-breaks. Off = weighted-sum score (bit-identical to prior HGS).
        /// </summary>
        public bool LexicographicScoring = false;
        /// <summary>(Fill fairness) On a parent's FIRST split, release its slot in the Fill backlog
        /// pool so a fresh order is injected, while keeping the parent in the pending set so its
        /// residual demand is still served. Mirrors SplitM2eIC/M4G. Inert in Fixed order mode,
        /// where the order stream is predetermined. false = current behaviour.</summary>
        public bool ReleaseParentOnFirstSplit = false;
    }

    /// <summary>
    /// HGS-M4: greedy heuristic counterpart of M4G, as HADGS is to M1G and HGS-M3 is to M3G.
    /// Inherits PVGS's pod-centric greedy engine config; GetMethodType routes to
    /// GreedyM4GManager. Implements IM4GPrices so it prices dispatch candidates with the
    /// exact same self-calibrated price list M4G optimises exactly - the only difference
    /// between the two managers is solution method, not objective.
    /// </summary>
    public class GreedyM4GConfiguration : PVGSConfiguration, IM4GPrices
    {
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.GreedyM4G; }
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBGREEDYM4G"; }

        // ── Price calibration (spec 3.5), identical field set and defaults to M4GConfiguration -
        //    see IM4GPrices for what each one means. ──
        public double LambdaScale { get; set; } = 1.0;
        public double MuScale { get; set; } = 1.0;
        public double DeltaScale { get; set; } = 1.0;
        /// <summary>Mirror of M4GConfiguration.SeparateOrderDelta. Off keeps the greedy pricing
        /// identical to every published result.</summary>
        public bool SeparateOrderDelta { get; set; } = false;

        /// 0 = canon (2026-08-28): the unit-level tie-break was removed so every remaining
        /// price is measured, leaving no chosen constant in the objective. Removing it left the
        /// splitting benefit intact (items +18.0% vs the no-split arm either way) and cost only
        /// efficiency: energy-per-order gain 34.3% -> 31.5%, distance -30.1% -> -24.9% (seed 0).
        public double EpsilonScale { get; set; } = 0.0;
        public int WarmupLines { get; set; } = 50;
        public double LambdaFallback { get; set; } = 10.0;
        public double DeltaFallback { get; set; } = 0.05;
        public double LinesPerOrderFallback { get; set; } = 2.4;
        public double LambdaFixed { get; set; } = 0;
        public double DeltaFixed { get; set; } = 0;
        public double RhoFallback { get; set; } = 15.0;
        /// <summary>Denominate the running prices in Extract-task distance only. false reproduces every published result bit-for-bit. See IM4GPrices.PickDistancePricing.</summary>
        public bool PickDistancePricing { get; set; } = false;
        /// <summary>Condition delta on committed-supply count instead of one system-wide scalar.
        /// Default true since 2026-08-07; set false for the flat-delta ablation, which is what every
        /// result published before that date used. See IM4GPrices.StratifiedDelta.</summary>
        public bool StratifiedDelta { get; set; } = true;
        /// <summary>Valued lines a stratum needs before its own ratio is trusted; below it the system-wide ratio is used. See IM4GPrices.DeltaStratumMinLines.</summary>
        public int DeltaStratumMinLines { get; set; } = 50;

        /// <summary>(Fill fairness) On a parent's FIRST split, release its slot in the Fill backlog
        /// pool so a fresh order is injected, while keeping the parent in the pending set so its
        /// residual demand is still served. Mirrors M4G/HGS-M3. Inert in Fixed order mode,
        /// where the order stream is predetermined. Default false.</summary>
        public bool ReleaseParentOnFirstSplit = false;
    }

    /// <summary>
    /// HGS-M5: the marginal-line greedy (spec 2026-08-03). Same price fields and same defaults as
    /// GreedyM4GConfiguration, so the two greedy arms differ only in how a solution is CONSTRUCTED,
    /// never in what a line or an order is worth.
    ///
    /// On delta: IM4GPrices requires DeltaScale / DeltaFallback / DeltaFixed and they are kept here
    /// to satisfy the interface, but HGS-M5 never calls M4GPricing.Delta(). A constructive greedy
    /// has no "valued but not bound" state - it binds exactly what it scores - so the
    /// valuation/binding split that delta discounts does not exist, and the objective collapses to
    /// -lambda per closed line and -mu per completed order. Leaving these inert is deliberate.
    /// </summary>
    public class GreedyM5Configuration : PVGSConfiguration, IM4GPrices
    {
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.GreedyM5; }
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBGREEDYM5"; }

        // ── Price calibration, identical field set and defaults to GreedyM4GConfiguration. ──
        public double LambdaScale { get; set; } = 1.0;
        public double MuScale { get; set; } = 1.0;
        public double DeltaScale { get; set; } = 1.0;
        /// <summary>Mirror of M4GConfiguration.SeparateOrderDelta. Off keeps the greedy pricing
        /// identical to every published result.</summary>
        public bool SeparateOrderDelta { get; set; } = false;

        /// 0 = canon (2026-08-28): the unit-level tie-break was removed so every remaining
        /// price is measured, leaving no chosen constant in the objective. Removing it left the
        /// splitting benefit intact (items +18.0% vs the no-split arm either way) and cost only
        /// efficiency: energy-per-order gain 34.3% -> 31.5%, distance -30.1% -> -24.9% (seed 0).
        public double EpsilonScale { get; set; } = 0.0;
        public int WarmupLines { get; set; } = 50;
        public double LambdaFallback { get; set; } = 10.0;
        public double DeltaFallback { get; set; } = 0.05;
        public double LinesPerOrderFallback { get; set; } = 2.4;
        public double LambdaFixed { get; set; } = 0;
        public double DeltaFixed { get; set; } = 0;
        public double RhoFallback { get; set; } = 15.0;
        /// <summary>Mirrors M4GConfiguration.PodTierDrawPricingEnabled so M5 keeps M4G's price
        /// list exactly (feedback_m5_must_mirror_m4g). false = every draw is free regardless of pod
        /// tier, which is canon: the sunk-vs-new distinction is already carried by the trip term in
        /// D (only Pa pods pay travel), so rho expressed the same idea a second time at unit
        /// granularity. Measured on seed 0: removing it left completed orders bit-for-bit identical
        /// (610) while total distance fell 3.7%.</summary>
        public bool PodTierDrawPricingEnabled = false;
        /// <summary>Denominate the running prices in Extract-task distance only. false reproduces every published result bit-for-bit. See IM4GPrices.PickDistancePricing.</summary>
        public bool PickDistancePricing { get; set; } = false;
        /// <summary>Condition delta on committed-supply count instead of one system-wide scalar.
        /// Default true since 2026-08-07; set false for the flat-delta ablation, which is what every
        /// result published before that date used. See IM4GPrices.StratifiedDelta.</summary>
        public bool StratifiedDelta { get; set; } = true;
        /// <summary>Valued lines a stratum needs before its own ratio is trusted; below it the system-wide ratio is used. See IM4GPrices.DeltaStratumMinLines.</summary>
        public int DeltaStratumMinLines { get; set; } = 50;

        /// <summary>Mirrors M4GConfiguration.IncrementalValuationEnabled: cap draws from pods
        /// fetched this epoch at the residual demand that already-committed inbound stock cannot
        /// cover. In M4G this is constraint V2a, which bounds the valuation layer and therefore -
        /// through B1's qb &lt;= qh - the binding layer too, so a greedy that omits it would be
        /// searching a LARGER feasible set than the exact model it is measured against.</summary>
        public bool IncrementalValuationEnabled = true;
        /// <summary>Outer lambda iteration, the greedy counterpart of M4G's Dinkelbach loop:
        /// rebuild the whole epoch at lambda = D*/V* of the previous build and repeat. A greedy
        /// epoch costs microseconds, so this is nearly free. 0 = single build at the historical
        /// lambda.</summary>
        public int LambdaIterations = 5;
        /// <summary>Stops the outer lambda iteration once |objective| falls below this.</summary>
        public double LambdaTolerance = 0.5;
        /// <summary>
        /// Score a draw-line move by its TRUE change in the objective, -lambda*(1-delta), instead
        /// of -lambda. Default true since 2026-08-07; false is the biased-score ablation and
        /// reproduces everything published before that date bit-for-bit.
        ///
        /// Canonised because HGS-M5 must mirror M4G exactly, the only permitted difference being
        /// that it constructs greedily instead of solving. A local score that is not the gradient
        /// of the objective it accumulates is a different objective, not a faster solver. Cost at
        /// the operating point is nil over 5 paired seeds: orders -1.34% (t = -1.21), pile-on
        /// +1.87% (t = +1.20), EOR +1.46% (t = +1.28), backlog +2.12% (t = 0.18).
        ///
        /// The plan's total is already M4G's objective term for term: a bound line contributes
        /// -lambda, a valued-only line -lambda*delta, and ValuationSweep supplies the second set.
        /// The greedy's per-move score is not that objective's gradient, though. A line that gets
        /// bound was, before the move, a line ValuationSweep would have counted (the sweep only
        /// needs stock at some station; binding needs stock AND a free slot, so binding-eligible
        /// implies sweep-eligible), so it was already earning -lambda*delta. Taking it moves the
        /// line from -lambda*delta to -lambda, i.e. the objective improves by lambda*(1-delta),
        /// not by lambda. Scoring it as -lambda overstates every draw by lambda*delta and makes
        /// the greedy climb a surface that is not the one it reports.
        ///
        /// The correction is not exactly lambda*delta for every line - the sweep consumes stock in
        /// ScanOrder, so binding can also displace some OTHER line's sweep eligibility - which is
        /// presumably why the original took the shortcut: getting it exact needs a sweep per
        /// candidate move. lambda*(1-delta) is the first-order term and has the right sign.
        ///
        /// At the shipped prices the bias is small: lambda ~ 8.7 and delta ~ 0.17 put it at ~1.5 m
        /// against move scores of 8-17 m, so few moves flip. It matters at HIGH delta, where
        /// lambda*delta is most of lambda - which is exactly where HGS-M5 was measured to be
        /// insensitive to delta while M4G collapsed. Part of that asymmetry is this bias, not
        /// structure, so expect the insensitivity to shrink when this is on.
        /// </summary>
        public bool FaithfulMarginal = true;
        /// <summary>Maximum times a degenerate ("do nothing", V*&lt;=0) solve may double lambda and retry,
        /// making the ratio search two-sided. 0 restores the original one-sided loop bit-for-bit.
        /// Needed whenever the running price statistic can UNDER-estimate the true marginal ratio,
        /// which the one-sided loop cannot recover from: it only ever lowers lambda, so an
        /// under-estimate feeds itself (fewer dispatches, less distance, lower lambda still).
        /// Default 6 since the drift check: with escalations on and the calibration numerator left
        /// at fleet distance, a 2h run is bit-identical to the one-sided baseline (908/908
        /// decision-log rows match except decisionSec), i.e. this path never fires under the
        /// shipped prices and only arms the recovery when it is actually needed. See the remarks
        /// at the loop itself for the measurement.</summary>
        public int LambdaEscalations = 6;
        /// <summary>(Fill fairness) Mirrors M4G's EPR - release the parent's Fill slot on its first
        /// split. Inert in Fixed order mode.</summary>
        public bool ReleaseParentOnFirstSplit = true;

        /// <summary>
        /// Screens the Pa candidate pods down to the K most promising before the expensive
        /// per-pod trial, which is the planner's O(|Pa|) term and the only thing standing between
        /// this heuristic and a workable large-instance cost. Pods are ranked by a cheap
        /// optimistic score - dispatch travel minus lambda*(1+delta) per order line the pod would
        /// newly make coverable - and the survivors are then evaluated in full, in their original
        /// enumeration order so tie-breaking among them is unchanged.
        ///
        /// This is the ONE deliberate approximation in HGS-M5: with a cap, the greedy no longer
        /// searches the same candidate set as M4G, so "the only difference is exact vs greedy"
        /// becomes "exact vs greedy plus candidate truncation". Feasibility - and hence
        /// obj(HGS-M5) &gt;= obj(M4G) - is untouched, since dropping candidates can only make the
        /// constructed solution worse, never infeasible.
        ///
        /// 0 (default) = no cap, every Pa pod fully evaluated: the strictly-aligned reference.
        /// </summary>
        public int CandidatePodTopK = 0;

        /// <summary>(GreedyM5) Mirror of M4GConfiguration.LexicographicRatioFirst. Only meaningful
        /// at DeltaFixed = 1, where lamEff = lambda*(1-delta) and muEff are both zero and every
        /// draw-line move scores exactly 0 - the greedy counterpart of the exact model's beta = 1
        /// degeneracy, and it stops the plan dead for the same reason ("accept the most negative
        /// move" never fires on a zero).
        ///
        /// Restores progress the same way the exact model does, without a price: a zero-scoring
        /// move is accepted when it CLAIMS A SLOT, and among tied moves the slot-claiming one wins
        /// instead of losing. Strictly ordered after the ratio, exactly as the exact model's
        /// tie-break is: the existing "bestDraw.Delta &lt;= dDelta" test still lets any
        /// objective-improving dispatch pre-empt a zero-scoring draw, so slots are only filled once
        /// nothing left improves the ratio.
        ///
        /// The dispatch lookahead's inner harvest uses the same rule - it must, or the lookahead
        /// and the real run would disagree about what a pod unlocks, which is the one invariant
        /// EvaluateDispatch's trial clone exists to preserve.</summary>
        public bool LexicographicRatioFirst = false;

        /// <summary>(GreedyM5, requires LexicographicRatioFirst) Among moves tied at zero that all
        /// claim a slot, take the one leaving the ORDER closest to done (fewest units still short;
        /// 0 = this move completes it).
        ///
        /// This does not contradict the exact model: at beta = 1 the draw variables carry a zero
        /// objective coefficient, so which order receives a given slot is genuinely undetermined
        /// there - Gurobi returns an arbitrary member of that tie set and the greedy has to pick
        /// one too. This picks a specific member rather than the enumeration-order default, and the
        /// choice is forward-looking in the second stage's own currency: the order nearest
        /// completion frees its slot soonest, and free slots are exactly what stage 2 minimises.
        ///
        /// Without it the greedy ranks tied moves by nothing at all, which is why the faithful
        /// mirror alone reversed the sign of every efficiency metric against M4G's.</summary>
        public bool LexSlotFillLeastResidual = false;

        /// <summary>(GreedyM5, requires LexicographicRatioFirst) Among moves tied at zero that all
        /// claim a slot, take the line with the FEWEST units. Ranked ahead of
        /// LexSlotFillLeastResidual when both are on.
        ///
        /// This is the key that reproduces M4G rev-lex's operating shape rather than merely
        /// avoiding its collapse. With the draw variables carrying a zero objective coefficient,
        /// the exact solver satisfies B4 (a claimed slot needs one drawn line) as cheaply as it
        /// can, which lands it on single-unit lines: 1.09 items per closed line against the
        /// greedy's 1.52, and 1311 closed lines against 938. Ranking tied moves by unit count picks
        /// the same members of the tie set the solver picks, so the two agree on shape and not just
        /// on throughput.</summary>
        public bool LexSlotFillSmallestLine = false;

        /// <summary>(GreedyM5, requires LexicographicRatioFirst) Also accept a zero-scoring move on
        /// an order that ALREADY holds its slot at that station, not only one that claims a fresh
        /// slot.
        ///
        /// Without this the greedy binds exactly one line per slot claim, because a top-up move
        /// scores zero and claims nothing - an artefact of how the acceptance test was written, not
        /// something the exact model does. B4 only requires that a claimed slot carry at least one
        /// drawn line; it never caps the count. The measured consequence is 1.50 closed lines per
        /// completed order against M4G rev-lex's 2.16.
        ///
        /// Terminates: an accepted move zeroes that line's residual, so the same (order, sku,
        /// station) triple is never emitted twice and the move set strictly shrinks.
        ///
        /// Claiming a fresh slot still outranks topping up, so idle slots are still minimised
        /// first - the top-up only spends capacity that has already been paid for.</summary>
        public bool LexSlotFillContinueOrder = false;
    }

    /// <summary>
    /// M4G: unit-level order splitting with the M1G valuation/binding layer separation
    /// restored. Inherits M1GConfiguration so every engine-side `is M1GConfiguration`
    /// type check passes without touching any engine file.
    /// Spec: docs/superpowers/specs/2026-07-31-m4g-valuation-binding-design.md
    /// </summary>
    public class M4GConfiguration : M1GConfiguration, IM4GPrices
    {
        /// <summary>Returns the method type of this configuration.</summary>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.M4G; }
        /// <summary>Returns a short name of this configuration.</summary>
        public override string GetMethodName() { return "M4G"; }

        // ── Price calibration (spec 3.5). All prices are metres-denominated and derived
        //    from running statistics; these scales exist only for dose-response ablation.
        //    Implemented as auto-properties (not bare fields) so this class can implement
        //    IM4GPrices - XmlSerializer round-trips public auto-properties identically to
        //    public fields, so this is not a behavioural change. ──
        /// <summary>Dose knob on lambda (metres per closed line). 1.0 = pure self-calibration.</summary>
        public double LambdaScale { get; set; } = 1.0;
        /// <summary>Dose knob on mu (metres per completed order).</summary>
        public double MuScale { get; set; } = 1.0;
        /// <summary>Dose knob on delta (realisation rate of unbound valuation, 0..1).</summary>
        public double DeltaScale { get; set; } = 1.0;
        /// <summary>Epsilon = EpsilonScale * lambda. Tie-break only; must stay far below lambda.</summary>
        /// 0 = canon (2026-08-28): the unit-level tie-break was removed so every remaining
        /// price is measured, leaving no chosen constant in the objective. Removing it left the
        /// splitting benefit intact (items +18.0% vs the no-split arm either way) and cost only
        /// efficiency: energy-per-order gain 34.3% -> 31.5%, distance -30.1% -> -24.9% (seed 0).
        public double EpsilonScale { get; set; } = 0.0;
        /// <summary>Below this many cumulative closed lines the fallback prices are used.</summary>
        public int WarmupLines { get; set; } = 50;
        /// <summary>Warm-up lambda in metres per line (measured 10.1-10.5 in the 3-way comparison).</summary>
        public double LambdaFallback { get; set; } = 10.0;
        /// <summary>Warm-up delta: realisation rate of valuation into binding (measured ~0.044 post-fix, same order as the corrected statistic - not the old ~0.7 "eventually closed by anyone" figure).</summary>
        public double DeltaFallback { get; set; } = 0.05;
        /// <summary>Warm-up lines-per-order (measured ~2.37 units per order).</summary>
        public double LinesPerOrderFallback { get; set; } = 2.4;
        /// <summary>&gt; 0 overrides the running lambda with this fixed value (open-loop ablation).</summary>
        public double LambdaFixed { get; set; } = 0;
        /// <summary>&gt; 0 overrides the running delta with this fixed value (open-loop ablation).</summary>
        public double DeltaFixed { get; set; } = 0;
        /// <summary>Warm-up rho in metres per unit picked (pod-tier draw pricing fallback).</summary>
        public double RhoFallback { get; set; } = 15.0;
        /// <summary>Denominate the running prices in Extract-task distance only. false reproduces every published result bit-for-bit. See IM4GPrices.PickDistancePricing.</summary>
        public bool PickDistancePricing { get; set; } = false;
        /// <summary>Condition delta on committed-supply count instead of one system-wide scalar.
        /// Default true since 2026-08-07; set false for the flat-delta ablation, which is what every
        /// result published before that date used. See IM4GPrices.StratifiedDelta.</summary>
        public bool StratifiedDelta { get; set; } = true;
        /// <summary>Valued lines a stratum needs before its own ratio is trusted; below it the system-wide ratio is used. See IM4GPrices.DeltaStratumMinLines.</summary>
        public int DeltaStratumMinLines { get; set; } = 50;

        // ── Ablation (spec 6) ──
        /// <summary>true forces q == q-hat, degenerating the valuation layer. Should reproduce M3G-like behaviour.</summary>
        public bool DegenerateToBindingOnly = false;
        /// <summary>Cap on orders admitted to the valuation layer (0 = no cap). Solve-time convergence knob.</summary>
        public int ValuationOrderLimit = 0;
        /// <summary>Caps the valuation credit a single dispatched pod can receive at the station's
        /// total slot capacity times the mean residual units per pending order (V5). false reproduces
        /// the uncapped behaviour that over-dispatched.
        ///
        /// Default flipped true -> false on 2026-08-07. The ablation found V5 inert once the delta
        /// attribution and pod-tier pricing were both in place, so it was never taken into the
        /// canon, and every shipped M4G-family config already writes false explicitly - the true
        /// default only sat there waiting for a config that omitted the line to silently pick up a
        /// constraint the model does not use. HGS-M5 has no counterpart, so leaving it on by
        /// default would also have put the two managers on different models (see the mirroring
        /// rule). No behaviour change: m4g / m1g_a / m4g_sdelta / m4g_flatdelta all set it already.</summary>
        public bool PodCreditCapEnabled = false;
        /// <summary>Prices each bound draw by the pod's tier: processing pods are rewarded rho per
        /// unit (their window is closing), queued and en-route pods are free (sunk), and newly
        /// dispatched storage pods pay rho per unit. rho is measured, not tuned. false = no tier
        /// pricing, reproducing the flat behaviour where every draw is free.</summary>
        public bool PodTierDrawPricingEnabled = false;
        /// <summary>(Experimental) Charge +rho per unit drawn from a newly dispatched (Pa) pod.
        /// The trip itself is already priced once in D (d_bot_pod + d_pod_station, independent of
        /// how many units are taken), so this per-unit charge is a second, finer-grained levy on
        /// the same trip - and because it scales with the draw count it penalises loading up a pod
        /// that has already been paid for, which is the opposite of the pile-on incentive. false
        /// keeps only the -rho reward on processing (Pp) pods, whose justification is different:
        /// those pods pay no D at all, so -rho prices a genuine opportunity cost (skip the unit
        /// now, pay a future trip to fetch it). true reproduces the canon bit-for-bit.</summary>
        public bool PodTierPenaltyOnNew = true;
        /// <summary>Prices due dates instead of gating on them. lambda and mu are scaled per order
        /// by (1 + u_o), where u_o = clamp(1 - Timestay_o / Tbar, 0, 1), Timestay is M1G's remaining
        /// slack (DueTime minus the time already elapsed since the order was placed, defined exactly
        /// as in M1GManager.GenerateOd) and Tbar is the running mean order turnover time - a measured
        /// quantity like every other M4G price, not a tuned constant. The mechanism is inert until at
        /// least one order has completed (Tbar undefined) and inert for every order whose remaining
        /// slack still exceeds Tbar, which mirrors M1G's Od gate: that too does nothing until orders
        /// are genuinely close to their due date. The cap at u_o = 1 (a factor of at most 2) is a
        /// structural choice, not a tuned one - it stops a single already-hopeless order from
        /// monopolising the objective. Since the factor does not depend on lambda, the whole value
        /// side still scales linearly with lambda and the Dinkelbach linearisation is unaffected.
        /// false reproduces the urgency-blind objective bit-for-bit.</summary>
        public bool DueDatePricingEnabled = false;
        /// <summary>Prices work in progress - orders left half-served. Existing WIP is a state, not a
        /// decision, so charging for it directly would be a constant the solver cannot act on; the
        /// charge is therefore split into the two levers the model does control. An order that already
        /// carries WIP is charged kappa unless it completes, which after dropping the constant is an
        /// extra completion bonus of kappa (clear what is already half-done); an order with no WIP that
        /// receives bound draws without completing pays kappa (do not start what you will not finish).
        /// Same kappa on both sides, so the two are one mechanism, not two knobs. kappa scales with
        /// lambda, so the value side stays proportional to lambda and Dinkelbach is unaffected.
        /// false reproduces the WIP-blind objective bit-for-bit.</summary>
        public bool WipHoldingEnabled = false;
        /// <summary>Relative MIP optimality gap for this model's solves. Negative leaves Gurobi's
        /// default (1e-4), which is what every published M4G result was produced with. Set to 0 to
        /// solve to proven optimality - required before M4G can be described as an upper-bound
        /// reference whose distance from a heuristic is reported, since a 1e-4 gap means the
        /// "optimum" it returns is merely near-optimal.</summary>
        public double MipGap = -1.0;

        /// <summary>
        /// Per-decision wall-clock budget for the solver, in seconds. On expiry Gurobi returns its
        /// best incumbent; "do nothing" is always feasible here, so a solution always exists.
        ///
        /// This is a statement about the PROBLEM, not a workaround: an online controller that must
        /// answer every few simulated seconds does not get unbounded solve time, and the
        /// no-splitting arm's solve time was measured diverging (1.06 -> 151.7 s per decision as
        /// the backlog grew) on the fixed_fill1350 workload while the splitting arm stayed flat
        /// (0.31 -> 0.15 s). Giving every arm the SAME budget turns solve tractability from an
        /// uncontrolled variable into a controlled one, and lets the divergence be reported as
        /// "worse solutions under an equal budget" rather than "did not finish".
        ///
        /// Negative (default) = unbounded, i.e. the historical behaviour.
        /// </summary>
        public double DecisionTimeLimitSec = -1.0;

        /// <summary>
        /// Deterministic per-decision effort budget in Gurobi work units. Preferred over
        /// <see cref="DecisionTimeLimitSec"/> for anything that goes in the thesis: a wall-clock
        /// cap makes the simulation's outcome depend on machine load, so the same config would not
        /// reproduce, whereas a work cap truncates the search identically on every run. Both may be
        /// set; whichever binds first stops the solve. Negative (default) = unbounded.
        /// Note this is per Optimize() call, and the Dinkelbach loop solves several times per
        /// decision, so the per-decision ceiling is this value times the iteration count.
        /// </summary>
        public double DecisionWorkLimit = -1.0;
        /// <summary>kappa = WipHoldingScale * lambda. 1.0 means "leaving an order open costs what one
        /// closed line is worth" - a structural choice of scale, in the same spirit as EpsilonScale,
        /// not a fitted value. Exposed so the dose can be probed without recompiling.</summary>
        public double WipHoldingScale = 1.0;
        /// <summary>Values newly dispatched storage pods only against demand that pods already
        /// committed to a station cannot supply, so the same demand does not justify fetching a
        /// fresh pod on every consecutive decision. false = value every pod against the raw
        /// backlog (the behaviour that over-dispatched).</summary>
        public bool IncrementalValuationEnabled = true;
        /// <summary>Commits split children only for orders whose draws all come from pods physically
        /// at a station (being picked or queued there). A pod still travelling may only be committed
        /// whole orders, so no split shape is fixed before the pod arrives; the solver re-decides the
        /// split each decision against the state that actually holds. false = current behaviour, which
        /// binds every assigned order - splits included - at dispatch time.</summary>
        public bool SplitOnlyAtPresentPods = false;

        // ── Diagnostics (spec 4) ──
        /// <summary>Enables the no-split counterfactual solve that measures the marginal value of splitting.</summary>
        public bool SplitMarginalProbeEnabled = false;
        /// <summary>Probe cadence in decisions.</summary>
        public int SplitMarginalProbeEveryNDecisions = 50;
        /// <summary>Seconds allowed for one probe solve. &lt;= 0 = no limit.</summary>
        public double SplitMarginalProbeTimeLimitSec = 10;

        /// <summary>(Fill fairness) On a parent's FIRST split, release its slot in the Fill backlog
        /// pool so a fresh order is injected, while keeping the parent in the pending set so its
        /// residual demand is still served. Mirrors M3G's ReleaseParentOnFirstSplit. Inert in Fixed
        /// order mode, where the order stream is predetermined.
        ///
        /// Default flipped false -> true on 2026-08-07: this is fairness machinery every splitting
        /// model should carry, and leaving it off by default put M4G at odds with
        /// GreedyM5Configuration, which has defaulted true all along - exactly the kind of
        /// asymmetry the mirroring rule forbids. No behaviour change: every shipped M4G-family
        /// config already writes true explicitly. M1GConfiguration is untouched; M4G declares this
        /// field itself rather than inheriting it, so the baseline keeps its own default.</summary>
        public bool ReleaseParentOnFirstSplit = true;

        /// <summary>Dinkelbach iterations for the ratio objective. 0 = current behaviour: a single
        /// linearisation step using the historical lambda. &gt; 0 = iterate, re-solving with lambda
        /// updated to the ratio realised by the previous solution, until the linearised optimum
        /// reaches zero within DinkelbachTolerance or this many iterations have run. The converged
        /// lambda is the minimum achievable distance per unit value for this decision.</summary>
        public int DinkelbachIterations = 0;
        /// <summary>Convergence tolerance on the linearised objective value, in metres.</summary>
        public double DinkelbachTolerance = 0.5;
        /// <summary>Maximum times a degenerate ("do nothing", V*&lt;=0) solve may double lambda and retry,
        /// making the ratio search two-sided. 0 restores the original one-sided loop bit-for-bit.
        /// Needed whenever the running price statistic can UNDER-estimate the true marginal ratio,
        /// which the one-sided loop cannot recover from: measured over 1013 decisions it raised
        /// lambda zero times (869 lowered, 11 held), so one under-estimate feeds itself.
        /// Default 6 since the drift check: with escalations on and the calibration numerator left
        /// at fleet distance, a 2h run is bit-identical to the one-sided baseline (917/917
        /// decision-log rows match except solveSec), i.e. this path never fires under the shipped
        /// prices and only arms the recovery when it is actually needed. See the remarks at the
        /// loop itself for the measurement.</summary>
        public int DinkelbachEscalations = 6;

        /// <summary>Gives the model whole-order semantics - the assignment structure of the M1G
        /// baseline - instead of M4G's unit-level splitting: every order is bound to at most one
        /// station in a decision (mirrors M1G's yos/shi2) and is either fully satisfied this
        /// decision or not served at all, with no partial fulfilment carried to a later decision
        /// (mirrors M1G having no unit-level draw variable to split in the first place). No split
        /// child can ever be created. Everything else - prices, Dinkelbach iteration, the
        /// valuation/binding layers, EPR - is unchanged, so the difference against the
        /// unrestricted model isolates the effect of splitting itself. false = current behaviour
        /// (M4G unit-level splitting, unrestricted).</summary>
        public bool ForbidSplitting = false;
        /// <summary>
        /// Under ForbidSplitting, restrict ONLY the cross-station half: an order still goes to at
        /// most one station (V7a/V7b, B8), but may be filled over several decisions there. Drops
        /// the all-or-nothing equalities V8 and B9. false keeps both halves restricted, which is
        /// what every result before 2026-08-07 used.
        ///
        /// Why this exists: ForbidSplitting as originally written forbids BOTH of Xie et al.'s
        /// categories at once - split-among-stations AND split-over-time. M1G, the baseline it is
        /// meant to mirror, only forbids the first: it assigns an order to a station with yos and
        /// the pick layer OUTSIDE the MILP then serves it as pods arrive, with no requirement that
        /// one station cover the whole order in a single instant. The stricter reading makes the
        /// arm collapse where M1G would not: at 1000 SKUs on small/6bot it served 51 orders and
        /// stopped deciding at t=1374 of 7200, with replenishment never triggered (0 bundles
        /// placed) and the fleet resting 90% of the time - not deadlocked, simply unable to find
        /// an order one station could cover all at once.
        ///
        /// Caveat this does NOT fix: B8 is a per-decision constraint. A parent that keeps residual
        /// demand is re-decided next time and nothing pins it to the station it used before, so
        /// cross-period CROSS-STATION filling can still occur. The split-lifetime probe already
        /// classifies that case (category C), so measure before adding any pinning.
        /// </summary>
        public bool ForbidCrossStationOnly = false;
        /// <summary>
        /// Under ForbidSplitting, make the ORDER the unit of assignment the way M1G does: the whole
        /// order is committed to one station and leaves the pending set, whatever could not be drawn
        /// this decision is left to the pod-selection layer to fetch later, and no split child is
        /// ever created. false keeps the original reading, which additionally demands the entire
        /// order be drawn in the same decision.
        ///
        /// Why: the original reading conflates "one station" with "all at once", and the second half
        /// is not what M1G does. M1G's shi5 only constrains SKUs that some pod currently holds
        /// (`OiSKU.Where(v =&gt; PiSKU.ContainsKey(v.Key))`), so it will happily assign an order whose
        /// remaining lines nothing can supply yet; the order takes a slot and
        /// BotManagerPodSelection keeps fetching pods for its outstanding extract requests until it
        /// finishes. M4G-NS instead has V8g force zhat = 0 for exactly those orders, and B9 then
        /// pins every draw to zero, so the order cannot be touched at all.
        ///
        /// At 1000 SKUs on small/6bot that difference is total: nearly every order has at least one
        /// line no pod currently holds, so the arm valued 0 lines with 100 orders pending, 75
        /// storage pods and 4 pods already at stations - lambda escalated 10 -> 320 and still found
        /// "do nothing" optimal. It served 51 orders and stopped deciding at t=1374 of 7200. M1G on
        /// the same instance served 393 and ran to the end. The collapse is this modelling choice,
        /// not a property of not splitting.
        ///
        /// The order stays the atom: one station, never re-decided, no children. Only the demand
        /// that this decision can actually source is priced now, at lambda per line closed; the
        /// rest carries no reward until it is picked, so nothing is claimed that is not delivered.
        /// </summary>
        public bool WholeOrderDeferredFill = false;

        /// <summary>Coarsens the splitting atom from the UNIT to the LINE. Unrestricted M4G decides
        /// q[o,i,p,s] per unit, so one order line (o,i) may be served partly at one station and
        /// partly at another, or partly now and the rest in a later decision. With this on, every
        /// line is drawn to its FULL residual at exactly ONE station in a single decision, or not
        /// drawn at all - which is precisely HGS-M5's solution space. Order-level splitting is
        /// untouched: different lines of the same order may still go to different stations
        /// (cross-station) and undrawn lines still carry to later decisions (cross-period). Pods
        /// stay free, so several pods at the same station may jointly supply one line, exactly as
        /// M5's CommitParts does. Weaker than <see cref="ForbidSplitting"/>, which removes
        /// splitting entirely; this arm isolates what the unit-level atom is worth on top of the
        /// line-level one. false = current behaviour (unit-level, unrestricted).</summary>
        public bool LineAtomicSplitting = false;

        /// <summary>
        /// Rewrites <see cref="ForbidSplitting"/>'s all-or-nothing condition in its tight,
        /// big-M-free form. The original states it as a big-M cap per station (B2:
        /// sum draws &lt;= totalResidual * y[o,s]) plus a separate order-level equality (B9:
        /// sum draws == d * z_o); the LP relaxation of that pair is very loose - y can sit at a
        /// small fraction while paying for a full draw - so branch-and-bound has to search its way
        /// to integrality. The tight form pins the draw directly to the station indicator, per line
        /// and per station:
        ///
        ///     sum_p qb[o,i,p,s] == d[o,i] * y[o,s]  ,  sum_s y[o,s] &lt;= 1  ,  z_o == sum_s y[o,s]
        ///
        /// The set of INTEGER-feasible solutions is identical - an order is still served whole at
        /// one station or not at all - so this is a reformulation, not a model change. Only the
        /// relaxation (and hence solve time) differs. Degenerate ties may be broken differently,
        /// which is why it is gated rather than applied unconditionally.
        /// Requires ForbidSplitting; inert on its own.
        /// </summary>
        public bool TightWholeOrder = false;

        /// <summary>
        /// CORRECTNESS FIX (default on). Stops the objective paying the completion reward mu for
        /// orders it cannot actually complete.
        ///
        /// BuildSnapshot admits an order when ANY of its residual lines has stock this decision
        /// (`.Any`), but the c/z constraints B5/B7 - and the ch/zh symbols themselves - only range
        /// over lines that survive the PiSKU filter. A residual line with no pod coverage this
        /// decision therefore has no c variable and constrains nothing, so the solver may set
        /// z_o = 1 and collect -mu while that line goes unserved. Measured on
        /// small/2h/o100/seed 0 (K=10): 826 z=1 events against 569 orders that actually completed,
        /// out of only 672 orders that ever existed - at least 154 double-counts. HGS-M5 has no
        /// such hole (its completion test walks the FULL residual), which is the leading
        /// explanation for the greedy out-completing the exact model at equal policy.
        ///
        /// The fix forces zh_o = 0 (and hence z_o = 0 through B6z) whenever any residual line is
        /// uncoverable this decision. Splitting is untouched - partial draws remain legal, they
        /// simply stop earning a completion reward, which is what mu (metres per COMPLETED order)
        /// always meant. Note the price calibration was already honest: RegisterCompletedOrders is
        /// fed a residual-coverage test over all lines, so only the objective's incentive was wrong.
        ///
        /// Set false to reproduce results generated before 2026-08-04.
        /// </summary>
        public bool HonestCompletionReward = true;

        /// <summary>
        /// Caps the Pa dispatch-candidate set at the K best-scoring pods, using the same optimistic
        /// score as GreedyM5Configuration.CandidatePodTopK. Exists so M4G and HGS-M5 can be
        /// compared at EQUAL POLICY - see M4GManager.ScreenPaCandidates. 0 (default) = no cap,
        /// canonical M4G.
        /// </summary>
        public int CandidatePodTopK = 0;

        /// <summary>Scores the solution with the legacy M1G objective - w1 * travel distance,
        /// w2 * orders served (counted once per order, not per station), w3 * idle slots - instead
        /// of the metre-denominated self-calibrated prices. The feasible region is untouched, so
        /// splitting remains available; this arm exists to show what splitting is worth when the
        /// objective does not price partial fulfilment. false = the self-calibrated objective.</summary>
        public bool LegacyObjective = false;
        /// <summary>Legacy w2, the per-order reward. Negative because the objective is minimised.</summary>
        public double LegacyOrderReward = -40;
        /// <summary>Legacy w3, the idle-slot weight.</summary>
        public double LegacyIdleSlotWeight = 0;
        /// <summary>
        /// Measurement-only probe: every N-th decision, re-solve the SAME model once per station
        /// with that station's free-slot count raised by one, and log the objective improvement.
        /// That difference is the exact integer marginal value of one station slot, in metres -
        /// the quantity an LP dual would approximate. Gurobi does not define duals for a MIP
        /// (LinearModel.GetDuals throws), and the relaxation's dual would be an approximation of
        /// a quantity we can obtain exactly, so the probe re-solves instead.
        ///
        /// What it is for: the objective prices what a decision CONSUMES and PRODUCES, but never
        /// what committing a scarce slot now forecloses later. If the marginal value of a slot is
        /// roughly constant over time, using one now versus later is equivalent and the myopic
        /// policy loses nothing; if it swings, there is a real inter-temporal opportunity cost the
        /// current objective cannot see. Measuring the dispersion is what decides whether such a
        /// term is worth adding at all.
        ///
        /// Cost: one extra solve per station per probed decision. 0 (default) disables it, leaving
        /// the decision path bit-identical.
        /// </summary>
        public int SlotShadowProbeCadence = 0;

        /// <summary>
        /// Valuation-fidelity diagnostic (measurement only, never read back into any decision).
        /// Writes m4g_valuation_fidelity.csv, one row per decision.
        ///
        /// What it answers: delta is a RATIO OF COUNTS - of the lines the valuation layer scored
        /// closed, what share did the binding layer take. Within one decision the two layers
        /// cannot disagree in SHAPE, because B1/B6c/B6z nest the binding layer inside the
        /// valuation layer index by index. Across decisions there is no such tie: a line valued
        /// but not bound this tick may never be the line that closes later, and delta cannot see
        /// the difference. Two consequences are worth measuring:
        ///
        ///   (A) Identity carry-over. Are the lines the binding layer closes the SAME lines the
        ///   valuation layer promised, or does the promise get filled by whatever happens to be
        ///   convenient next tick? Shape drift is harmless while every line carries the same
        ///   price lambda (a promise kept by a substitute scores identically), so a low carry-over
        ///   rate is a finding about the estimate's meaning, not a bug - unless DueDatePricing is
        ///   on, which makes lines heterogeneously priced and turns drift into real bias.
        ///
        ///   (B) The mu bias. mu is lambda times the MEAN lines per order, so a valued order and
        ///   a bound order are exchanged one-for-one regardless of how much work each represents.
        ///   The valuation layer is slot-free and can favour large orders; the binding layer is
        ///   slot-bound and may complete small ones. Logging the residual line counts of each side
        ///   tests whether that asymmetry is systematic (which would make mu a standing
        ///   over-estimate) or averages out.
        ///
        /// Cost: two set operations and one line of CSV per decision. false (default) leaves the
        /// decision path and every existing output bit-identical.
        /// </summary>
        public bool ValuationFidelityLog = false;

        /// <summary>
        /// EXPERIMENTAL (2026-08-09), default 0 = off, decision path bit-identical when off.
        ///
        /// Idle-slot price as a multiple of mu: each station slot left empty this decision costs
        /// SlotScale * mu metres. 1.0 reads as "leaving a slot empty costs one order's worth of
        /// value" - zero new free parameters, since mu is measured.
        ///
        /// Why it might matter: M1G carries a hand-set w3 = 1000 per idle slot (25x its order
        /// reward w2 = 40), which force-fills stations; canonical M4G has no such term at all and
        /// relies purely on the value side to pull orders in. This flag tests whether M4G is
        /// leaving throughput on the table by not pricing an idle slot.
        ///
        /// Scales with mu, which itself scales with lambda, so the Dinkelbach linearisation stays
        /// intact (the whole value side must remain proportional to lambda or the
        /// V* = (D* - obj)/lambda recovery silently computes garbage).
        /// </summary>
        public double SlotScale = 0.0;
        /// <summary>(Experimental) Fill station slots as a LEXICOGRAPHIC first priority instead of
        /// pricing idle slots in the objective. Stage 1 minimises the idle-slot count on its own;
        /// the optimum U* is then frozen as a constraint and stage 2 optimises D - lambda*V over
        /// the solutions that achieve it.
        ///
        /// This is what a big-M slot penalty is approximating, but without the side effect: a
        /// penalty term enters the objective, hence V* = (D* - objective)/lambda, hence the
        /// Dinkelbach lambda update. Measured with sigma = 5..400 mu, lambda collapsed from its
        /// measured 8.05 to ~0.9 in every arm - the price table was rewritten by a term that was
        /// only ever meant to set a priority. A frozen constraint cannot do that.
        ///
        /// Costs one extra solve per decision. Independent of SlotScale; setting both is
        /// redundant, not wrong.</summary>
        public bool LexicographicSlotFill = false;

        /// <summary>(M4G, experimental) Reverses the lexicographic order: the ratio comes FIRST and
        /// slot filling only breaks ties among solutions that already achieve it. Requires
        /// LexicographicSlotFill (it supplies the us variables); ignored on its own.
        ///
        /// Stage 1 is the ordinary Dinkelbach loop, run to convergence. Only afterwards is
        /// "objective &lt;= obj* + LexTieTolerance" added and the objective switched to sum(us),
        /// so the tie-break happens strictly after lambda has settled and cannot touch the
        /// V* = (D* - objective)/lambda recovery the loop depends on.
        ///
        /// Only meaningful at DeltaFixed = 1. With beta &lt; 1 the binding variables carry positive
        /// objective coefficients, the ratio optimum is near-unique, the tie set is empty and this
        /// is inert. At beta = 1 those coefficients are zero, every binding pattern consistent with
        /// the chosen valuation ties, and the tie-break is what stops the solver from choosing the
        /// q = 0 member of that set (measured: pure beta = 1 completes 0 orders).
        ///
        /// Versus the forward order (fill slots, then optimise the ratio inside that bound): this
        /// never sacrifices the ratio to fill a slot, at the cost of one extra solve per decision.</summary>
        /// <summary>(M4G, experimental) Weights each BOUND line closure by 1 + scarcity, where
        /// scarcity in [0,1] is how short the sunk pods' stock of that SKU falls of the outstanding
        /// backlog demand for it.
        ///
        /// The reading is cost-to-go, not preference. A line whose SKU the already-committed pods
        /// still cover in abundance costs nothing to postpone - a trip somebody has already paid
        /// for will serve it next epoch. A line whose SKU has no sunk supply left costs a whole new
        /// dispatch if postponed, so closing it now is worth strictly more. That is a sourcing
        /// fact, unlike DueDatePricing which encodes a preference about order urgency.
        ///
        /// Deliberately applied to the binding layer ONLY: the valuation layer keeps a uniform
        /// lambda, so "the objective counts how many lines, not which ones" still holds there and
        /// the shape drift measured in the valuation-fidelity log stays harmless.
        ///
        /// It also gives the greedy strictly more gradient rather than less, since scarcity is a
        /// local quantity every candidate move can evaluate from its own books - the opposite of
        /// the lexicographic route, which removed the gradient the greedy needs.
        ///
        /// First-order: scarcity is read off the pre-decision books, so it ignores the sunk stock
        /// this very decision consumes and under-states crowding-out.</summary>
        /// <summary>(M4G, experimental) Measure the realisation rate separately for lines and for
        /// orders, instead of applying the line-level beta to the mu terms as well.
        ///
        /// The two rates are not the same, and the gap is measurable: an order enters a station
        /// whole the moment any of it is bound, while its lines are gated one at a time by slot
        /// capacity. On the canonical seed the order-level rate is 0.2837 against the line-level
        /// 0.1995 - 1.42x - so the shared beta under-credits valued orders by about 30%, worth
        /// roughly 6% of V.
        ///
        /// Off by default: it takes the price table from three measured numbers to four, and the
        /// canonical results were produced with the shared rate.</summary>
        public bool SeparateOrderDelta { get; set; } = false;

        public bool ScarcityWeightedBinding = false;

        /// <summary>(M4G, experimental) Order-level counterpart of ScarcityWeightedBinding, and the
        /// one that expresses the intent. The bound COMPLETION reward mu*(1-delta) is weighted by
        /// 1 + w_o, where w_o is the MAXIMUM per-SKU scarcity over the order's open lines.
        ///
        /// Max, not mean: a single line with no sunk supply behind it already forces a fresh
        /// dispatch if the order is deferred, however easy its other lines are. w_o is therefore
        /// "how expensive is this order to finish later", which is exactly the difficulty the
        /// binding layer should be paying attention to.
        ///
        /// Why completion rather than line closure: the line-level version was measured to scatter
        /// the model across scarce SKUs instead of consolidating whole orders (pile-on -4.7%,
        /// lines per trip 8.75 -> 8.45, orders -3.8%). Paying the premium only when an order
        /// actually finishes keeps the consolidation incentive that mu exists to provide, and adds
        /// the difficulty signal on top of it rather than in competition with it.
        ///
        /// Also far cheaper to solve: one distinct coefficient per order rather than per line, so
        /// it does not flatten the symmetry the branch-and-bound relies on (the line-level version
        /// cost +65% solve time).</summary>
        public bool ScarcityWeightedCompletion = false;

        /// <summary>(M4G, experimental) Pays the full price on BOTH layers: a bound line earns
        /// lambda and a valued line earns lambda too, rather than the two coefficients splitting a
        /// single lambda between them. Orders likewise earn mu on both.
        ///
        /// This is not a delta setting. The canonical pair lambda*(1-delta) / lambda*delta is
        /// constrained to sum to lambda by construction, so "full credit on both" is unreachable
        /// through any delta and needs its own objective.
        ///
        /// The reason the canonical form discounts at all is repeated valuation across epochs: an
        /// open line is re-valued at every decision until it is finally closed - measured at 4872
        /// valuations for 962 closures, about 5x - so paying the closed-line price on every
        /// valuation credits the same work roughly five times, inflating V and collapsing the
        /// Dinkelbach lambda. Unlike DeltaFixed = 1 this keeps a non-zero bound coefficient, so
        /// binding still scores and the greedy still has a gradient; the failure mode to look for
        /// is over-dispatch, not deadlock.</summary>
        public bool FullCreditBothLayers = false;

        /// <summary>(M4G, experimental) Tiers the VALUATION credit by whether a line's deferred
        /// closure has already been paid for.
        ///
        /// A line the sunk pods (Pb - at a station or en route, their travel charged in an earlier
        /// decision) can cover in full will be closed by a trip nobody has to buy again, so it
        /// earns the whole lambda. A line that still needs a fresh dispatch earns only
        /// lambda*beta, the measured share such claims historically convert.
        ///
        /// The point is interpretability: it replaces "a global 0.19 conversion ratio" with a
        /// statement about whether the travel bill is already settled, which is a physical fact
        /// rather than a statistic. beta survives, but only on the pods whose trip is still being
        /// decided - the one place the credit actually changes a dispatch.
        ///
        /// Applied to the valued coefficient ONLY. Keeping the original (1-beta)/beta pair, which
        /// sums to lambda, would drive the bound increment to zero on the sunk-coverable lines and
        /// reproduce the beta = 1 gradient collapse on a subset - fatal for the greedy mirror.
        /// Decoupled, the bound increment stays lambda*(1-beta) everywhere.
        ///
        /// The tier is computed from the snapshot, so it adds no variables and no big-M. It ignores
        /// contention between orders competing for the same sunk stock, which makes it optimistic.</summary>
        public bool TieredBetaBySunkCoverage = false;

        public bool LexicographicRatioFirst = false;

        /// <summary>(M4G) Slack in metres allowed on the frozen objective when
        /// LexicographicRatioFirst re-solves. Exact equality on a floating-point objective value
        /// risks reporting infeasible on rounding alone; this is a numerical guard, not a
        /// relaxation knob - keep it far below the ~metre scale at which decisions differ.</summary>
        public double LexTieTolerance = 1e-4;

    }

    /// <summary>
    /// M4G-NS: the whole-order no-split control arm. Structurally M1G's assignment (one order,
    /// one station, all-or-nothing) with M4G's self-calibrated pricing substituted for M1G's
    /// hand-tuned w1/w2/w3.
    ///
    /// Inherits M1GConfiguration so every `is M1GConfiguration` type test in the engine passes
    /// without touching any engine file - the same trick SAM1GConfiguration and M4GConfiguration
    /// use.
    ///
    /// The price list is deliberately NOT IM4GPrices: this arm has no lambda (per line), no rho
    /// (per unit) and no epsilon (per unit), and the absence is enforced structurally rather than
    /// by configuration. See spec 2026-08-09 INV-2.
    /// </summary>
    public class M4GNSConfiguration : M1GConfiguration
    {
        /// <summary>Returns the type of the method.</summary>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.M4GNS; }

        /// <summary>Scales mu after it is measured. 1.0 = pure measurement, the intended setting.</summary>
        public double MuScale = 1.0;
        /// <summary>Scales delta after it is measured. 1.0 = pure measurement.</summary>
        public double DeltaScale = 1.0;
        /// <summary>Completed orders required before mu is measured rather than taken from MuFallback.</summary>
        public int WarmupOrders = 50;
        /// <summary>Warm-up metres per completed order, used until WarmupOrders is reached.</summary>
        public double MuFallback = 24.0;
        /// <summary>Warm-up realisation rate, used until WarmupOrders is reached.</summary>
        public double DeltaFallback = 0.05;
        /// <summary>Pins mu to a constant when &gt; 0 (diagnostics / ablation only).</summary>
        public double MuFixed = 0;
        /// <summary>Pins delta to a constant when &gt; 0 (diagnostics / ablation only).</summary>
        public double DeltaFixed = 0;
        /// <summary>Stratify delta by committed-pod count, same key M4G uses (min(|Pb|,3)).</summary>
        public bool StratifiedDelta = true;
        /// <summary>Minimum valued orders in a stratum before its own ratio is trusted.</summary>
        public int DeltaStratumMinOrders = 50;
        /// <summary>Dinkelbach iterations per decision. 0 = single solve at the measured mu.</summary>
        public int DinkelbachIterations = 5;
        /// <summary>Stop the Dinkelbach loop once |objective| falls below this.</summary>
        public double DinkelbachTolerance = 0.5;
        /// <summary>Max doublings of mu when the solve degenerates to "dispatch nothing".</summary>
        public int DinkelbachEscalations = 6;
        /// <summary>Idle-slot price as a multiple of mu. 1.0 = "an empty slot costs one order's
        /// worth of value". Replaces M1G's hand-set w3 = 1000, which the measured slot shadow
        /// price (8.51 m) says over-prices a slot by roughly 120x. 0 disables the term.</summary>
        public double SlotScale = 1.0;
    }

    /// <summary>
    /// M5-NS: the greedy counterpart of M4G-NS. Same decision structure (M1G's whole-order
    /// assignment), same price list (mu, delta, sigma = SlotScale * mu), same solution space -
    /// the only permitted difference is that a greedy construction replaces the MILP solve.
    /// Reuses M4GNSPricing rather than declaring its own, which is what structurally guarantees
    /// the two arms price identically.
    /// </summary>
    public class GreedyM5NSConfiguration : M1GConfiguration
    {
        /// <summary>Returns the type of the method.</summary>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.GreedyM5NS; }

        /// <summary>Scales mu after it is measured. 1.0 = pure measurement.</summary>
        public double MuScale = 1.0;
        /// <summary>Scales delta after it is measured. 1.0 = pure measurement.</summary>
        public double DeltaScale = 1.0;
        /// <summary>Completed orders required before mu is measured rather than taken from MuFallback.</summary>
        public int WarmupOrders = 50;
        /// <summary>Warm-up metres per completed order.</summary>
        public double MuFallback = 24.0;
        /// <summary>Warm-up realisation rate.</summary>
        public double DeltaFallback = 0.05;
        /// <summary>Pins mu when &gt; 0 (ablation only).</summary>
        public double MuFixed = 0;
        /// <summary>Pins delta when &gt; 0 (ablation only).</summary>
        public double DeltaFixed = 0;
        /// <summary>Stratify delta by committed-pod count, same key M4G-NS uses.</summary>
        public bool StratifiedDelta = true;
        /// <summary>Minimum valued orders in a stratum before its own ratio is trusted.</summary>
        public int DeltaStratumMinOrders = 50;
        /// <summary>Idle-slot price as a multiple of mu; mirrors M4GNSConfiguration.SlotScale.</summary>
        public double SlotScale = 1.0;
        /// <summary>Outer mu iterations per decision (greedy Dinkelbach).</summary>
        public int MuIterations = 5;
        /// <summary>Stop the mu loop once |objective| falls below this.</summary>
        public double MuTolerance = 0.5;
        /// <summary>Max doublings of mu when the plan degenerates to "do nothing".</summary>
        public int MuEscalations = 6;
        /// <summary>Score an assign move by its TRUE marginal change: an order already counted by
        /// the valuation layer only improves by mu*(1-delta), not mu. Mirrors M5's FaithfulMarginal.</summary>
        public bool FaithfulMarginal = true;
    }

    #endregion
}
