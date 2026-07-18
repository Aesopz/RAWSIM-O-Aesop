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
        /// (PK) Downstream packing buffer capacity C (Xie et al. 2021 Appendix B derives
        /// 78 boxes per shelf). One box per split parent from first split until
        /// consolidation. &lt;= 0 = unlimited (constraint absent) - the default.
        /// </summary>
        public int PackingBufferCapacity = 0;
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
        /// <summary>(SG) Master switch for the split gate.</summary>
        public bool SplitGateEnabled = true;
        /// <summary>
        /// (SG) Strict = new partials only when the station has a processing pod AND no
        /// queued successor (bridge window = successor's travel window). False (loose) =
        /// processing pod present suffices (harvests the post-queue tail; ablation arm).
        /// </summary>
        public bool SplitGateStrict = true;
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
    }

    #endregion
}
