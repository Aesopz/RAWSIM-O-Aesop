using System;
using RAWSimO.Core.Configurations;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// The set of price-calibration fields <see cref="M4GPricing"/> reads. Extracted so the
    /// same pricing implementation can be shared between the exact model (M4GManager, priced
    /// via M4GConfiguration) and its greedy counterpart (GreedyM4GManager, priced via
    /// GreedyM4GConfiguration) - the two managers must price identically so the only variable
    /// between them is solution method (exact vs greedy), not objective definition.
    /// </summary>
    public interface IM4GPrices
    {
        /// <summary>Dose knob on lambda (metres per closed line). 1.0 = pure self-calibration.</summary>
        double LambdaScale { get; }
        /// <summary>Dose knob on mu (metres per completed order).</summary>
        double MuScale { get; }
        /// <summary>Dose knob on delta (realisation rate of unbound valuation, 0..1).</summary>
        double DeltaScale { get; }
        /// <summary>Epsilon = EpsilonScale * lambda. Tie-break only; must stay far below lambda.</summary>
        double EpsilonScale { get; }
        /// <summary>Below this many cumulative closed lines the fallback prices are used.</summary>
        int WarmupLines { get; }
        /// <summary>Warm-up lambda in metres per line.</summary>
        double LambdaFallback { get; }
        /// <summary>Warm-up delta: realisation rate of valuation into binding.</summary>
        double DeltaFallback { get; }
        /// <summary>Warm-up lines-per-order.</summary>
        double LinesPerOrderFallback { get; }
        /// <summary>&gt; 0 overrides the running lambda with this fixed value (open-loop ablation).</summary>
        double LambdaFixed { get; }
        /// <summary>&gt; 0 overrides the running delta with this fixed value (open-loop ablation).</summary>
        double DeltaFixed { get; }
        /// <summary>
        /// Warm-up rho in metres per unit picked (pod-tier draw pricing fallback). Not in the
        /// task brief's enumerated field list, but M4GPricing.Rho() reads it - included so the
        /// interface actually covers everything the class reads (see hgs-m4-report.md for the
        /// note on this discrepancy).
        /// </summary>
        double RhoFallback { get; }
    }

    /// <summary>
    /// M4G price calibration (spec 3.5). Every price is denominated in metres and derived
    /// from running statistics, so the objective carries no hand-tuned constant.
    ///
    /// lambda = distance travelled per closed line          [m / line]
    /// mu     = lambda * lines per completed order          [m / order]
    /// delta  = realisation rate of valuation into binding: cumulative bound line
    ///          closures / cumulative valued line closures  [0..1]
    /// epsilon = EpsilonScale * lambda, a tie-break only    [m / unit]
    ///
    /// Pure state machine: it never touches the solver, the instance or the file system,
    /// which keeps it reasonable to reason about and to check by hand from the decision log.
    /// Shared by M4GManager (exact) and GreedyM4GManager (greedy) via <see cref="IM4GPrices"/>
    /// so both price identically.
    /// </summary>
    public class M4GPricing
    {
        private readonly IM4GPrices _config;
        private int _closedLines;
        private int _completedOrders;
        private long _boundLinesTotal;
        private long _valuedLinesTotal;

        /// <summary>Creates the pricing state machine.</summary>
        /// <param name="config">The owning manager's price configuration.</param>
        public M4GPricing(IM4GPrices config)
        {
            if (config == null) throw new ArgumentNullException("config");
            _config = config;
        }

        /// <summary>Cumulative number of order lines closed so far.</summary>
        public int CumulativeClosedLines { get { return _closedLines; } }
        /// <summary>Cumulative number of orders completed so far.</summary>
        public int CumulativeCompletedOrders { get { return _completedOrders; } }
        /// <summary>Cumulative number of lines the binding layer closed, across all decisions.</summary>
        public long CumulativeBoundLines { get { return _boundLinesTotal; } }
        /// <summary>Cumulative number of lines the valuation layer closed, across all decisions.</summary>
        public long CumulativeValuedLines { get { return _valuedLinesTotal; } }

        /// <summary>Whether the running statistics are still too thin to price from.</summary>
        private bool InWarmup { get { return _closedLines < _config.WarmupLines; } }

        /// <summary>Metres per closed order line.</summary>
        /// <param name="cumulativeDistanceMetres">Instance.StatOverallDistanceTraveled at decision time.</param>
        public double Lambda(double cumulativeDistanceMetres)
        {
            if (_config.LambdaFixed > 0) return _config.LambdaFixed;
            if (InWarmup) return _config.LambdaScale * _config.LambdaFallback;
            return _config.LambdaScale * (cumulativeDistanceMetres / Math.Max(1, _closedLines));
        }

        /// <summary>Metres per completed order = lambda * lines per order.</summary>
        /// <param name="cumulativeDistanceMetres">Instance.StatOverallDistanceTraveled at decision time.</param>
        public double Mu(double cumulativeDistanceMetres)
        {
            double linesPerOrder = _completedOrders > 0
                ? (double)_closedLines / _completedOrders
                : _config.LinesPerOrderFallback;
            return _config.MuScale * Lambda(cumulativeDistanceMetres) * linesPerOrder;
        }

        /// <summary>
        /// Realisation rate of valuation into binding: of the lines the valuation layer scored
        /// closed, what share did the binding layer actually take. Clamped to [0,1].
        /// </summary>
        public double Delta()
        {
            if (_config.DeltaFixed > 0) return Math.Min(1.0, _config.DeltaFixed);
            double raw = InWarmup
                ? _config.DeltaFallback
                : (double)_boundLinesTotal / Math.Max(1, _valuedLinesTotal);
            return Math.Max(0.0, Math.Min(1.0, _config.DeltaScale * raw));
        }

        /// <summary>Tie-break weight on bound units.</summary>
        /// <param name="cumulativeDistanceMetres">Instance.StatOverallDistanceTraveled at decision time.</param>
        public double Epsilon(double cumulativeDistanceMetres)
        { return _config.EpsilonScale * Lambda(cumulativeDistanceMetres); }

        /// <summary>
        /// Metres per unit picked, for pod-tier draw pricing: a unit drawn from a newly
        /// dispatched (Pa) pod costs +rho (it genuinely requires an extra trip), a unit drawn
        /// from a processing (Pp) pod is rewarded -rho (not taking it now means paying for a
        /// future trip to fetch that item later), and queued/en-route (Pq/Pb) draws are free.
        /// Measured live from cumulative distance / cumulative units picked, same warm-up gate
        /// as the other prices - not a tuned constant.
        /// </summary>
        /// <param name="cumulativeDistanceMetres">Instance.StatOverallDistanceTraveled at decision time.</param>
        /// <param name="cumulativeUnitsPicked">Instance.StatOverallItemsHandled at decision time.</param>
        public double Rho(double cumulativeDistanceMetres, double cumulativeUnitsPicked)
        {
            if (InWarmup) return _config.RhoFallback;
            return cumulativeDistanceMetres / Math.Max(1, cumulativeUnitsPicked);
        }

        /// <summary>Records lines closed by this decision.</summary>
        public void RegisterClosedLines(int count)
        { if (count > 0) _closedLines += count; }

        /// <summary>Records orders completed by this decision.</summary>
        public void RegisterCompletedOrders(int count)
        { if (count > 0) _completedOrders += count; }

        /// <summary>
        /// Records one decision's valuation/binding line counts into the running totals that
        /// drive delta. Called once per decision, straight off the same ValuedLineKeys.Count /
        /// BoundLineKeys.Count that land in the decision log's valuedLines / boundLines columns.
        /// </summary>
        public void RegisterDecision(int boundLines, int valuedLines)
        {
            if (boundLines > 0) _boundLinesTotal += boundLines;
            if (valuedLines > 0) _valuedLinesTotal += valuedLines;
        }

        /// <summary>Stable key for a line, shared by the valuation bookkeeping and the commit path.</summary>
        public static string LineKey(int orderId, int skuId)
        { return orderId.ToString() + ":" + skuId.ToString(); }
    }
}
