using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Denominate the running prices in picking distance only (Instance.StatOverallDistance
        /// TraveledExtract) instead of the fleet's unrestricted total. false reproduces every
        /// published result bit-for-bit.
        ///
        /// lambda and rho are both "metres per unit of picking work", but the total they divide
        /// also contains replenishment trips and pod returns to storage - about three quarters of
        /// it on the small benchmark - which no picking decision controls. Measured on HGS-M5
        /// (small, 10 bots, seeds identical): lambda's detrended correlation with replenishment
        /// intensity is +0.73 at 2h and +0.40 at 8h, and the sign is perverse - a busy
        /// replenishment period raises lambda, which raises the value side of min D - lambda*V,
        /// which makes the picking side MORE willing to dispatch exactly when the fleet is most
        /// contended. Extract-task distance is the same span the objective's D term prices, so
        /// this makes the calibration statistic commensurate with the objective it feeds.
        ///
        /// Note this does NOT change rho/lambda, mu/lambda or epsilon/lambda: those ratios share
        /// the numerator and are already immune. What it corrects is the LEVEL of lambda - the
        /// metres-to-value exchange rate - which is the only thing the contamination could move.
        /// </summary>
        bool PickDistancePricing { get; }
        /// <summary>
        /// Condition delta on how much supply is already committed, instead of using one
        /// system-wide scalar. Default true since 2026-08-07; false is the flat-delta ablation and
        /// reproduces everything published before that date bit-for-bit.
        ///
        /// Canonised on 5 paired seeds (small/10bot/2h/Fill), M4G: orders +1.62% at t = +2.93 with
        /// all five seeds the same sign, backlog -15.44%, pile-on +1.62% (t = +1.76), EOR -2.24%
        /// (t = -1.38). Orders PLACED were unchanged (t = -0.66), so the throughput gain is real
        /// and not Fill feeding the arm more. The plan had been to settle it on ten seeds; the
        /// owner accepted it at five.
        ///
        /// The flat delta prices every decision at the same realisation rate, but the measured
        /// rate is not flat: on small/10bot/2h it is 0.2117 when one pod is already committed and
        /// 0.1376 when two are (n=518 and n=396, and NOT a proxy for run phase - mean Pb is
        /// 1.38/1.48/1.43 across the three thirds of the run). The cause is that bound lines are
        /// pinned near 1.01 per decision by slot capacity whatever Pb is, while valued lines grow
        /// with it (4.79 -> 7.38), so the extra coverage a third pod is scored for is coverage the
        /// binding layer demonstrably cannot take. This is the discount-rate counterpart of V2a,
        /// which already deducts inbound SUPPLY but leaves the rate alone.
        ///
        /// Stratifying on free slots was measured first and rejected: 913 of 916 decisions have
        /// exactly one station with capacity, so that bucket's rate (0.1720) is the global rate
        /// (0.1727) and the split does nothing.
        /// </summary>
        bool StratifiedDelta { get; }
        /// <summary>
        /// A stratum must have seen at least this many valued lines before its own ratio is
        /// trusted; below it the global ratio is used. Guards against a thin bucket producing a
        /// wild delta early in the run - the same concern WarmupLines addresses for the level.
        /// </summary>
        int DeltaStratumMinLines { get; }
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
        private long _boundOrdersTotal;
        private long _valuedOrdersTotal;
        private long _valuedLinesTotal;
        /// <summary>Per-stratum {bound, valued} line totals, populated only when a caller passes a
        /// non-negative stratum. Empty (and unread) under the default configuration.</summary>
        private readonly Dictionary<int, long[]> _strata = new Dictionary<int, long[]>();

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
        public double Delta() { return Delta(-1); }

        /// <summary>
        /// Stratum-conditioned realisation rate. <paramref name="stratum"/> &lt; 0, StratifiedDelta
        /// off, or a stratum too thin to trust all fall back to the system-wide ratio, so the
        /// default configuration is bit-identical to <see cref="Delta()"/>.
        /// </summary>
        /// <param name="stratum">Bucket key for this decision; see IM4GPrices.StratifiedDelta.</param>
        public double Delta(int stratum)
        {
            if (_config.DeltaFixed > 0) return Math.Min(1.0, _config.DeltaFixed);
            double raw;
            if (InWarmup) raw = _config.DeltaFallback;
            else
            {
                long bound = _boundLinesTotal, valued = _valuedLinesTotal;
                long[] cell;
                if (_config.StratifiedDelta && stratum >= 0
                    && _strata.TryGetValue(stratum, out cell)
                    && cell[1] >= _config.DeltaStratumMinLines)
                { bound = cell[0]; valued = cell[1]; }
                raw = (double)bound / Math.Max(1, valued);
            }
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
            // Warm-up rho is derived from the warm-up lambda rather than taken as a free absolute
            // constant. rho is metres per UNIT and lambda is metres per LINE, so their ratio is
            // fixed by the instance's units-per-line - it is not a second degree of freedom.
            // Leaving RhoFallback absolute made the warm-up price list internally inconsistent:
            // halving LambdaFallback to 5 left rho at 15, so rho/lambda went 1.5 -> 3.0, every
            // draw-line move scored positive, no move was ever accepted, and the run produced
            // zero orders (verified on small/2h at both 6 and 10 bots).
            //
            // The precise statement: the Dinkelbach form is min D - lambda*V, where V is a FIXED
            // linear form whose composition is set by the ratios mu/lambda, rho/lambda and
            // epsilon/lambda (the iteration below scales all three with lambda, exactly so that V
            // stays fixed while lambda searches). An absolute RhoFallback broke that invariant -
            // changing LambdaFallback silently re-weighted the rho term inside V, i.e. it changed
            // the MODEL rather than the ratio estimate. Pinning rho/lambda restores it, leaving
            // LambdaFallback as what it should be: a starting guess for the ratio, which the
            // Dinkelbach iteration then corrects.
            //
            // RhoFallback is kept as the ratio's anchor: it is interpreted relative to the
            // default lambda of 10, i.e. rho = lambda * (RhoFallback / 10), so the shipped
            // default (15) reproduces the historical warm-up prices exactly at LambdaFallback=10.
            if (InWarmup)
                return _config.LambdaScale * _config.LambdaFallback * (_config.RhoFallback / 10.0);
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
        /// <summary>Records this decision's ORDER counts, the counterpart of RegisterDecision's
        /// line counts. Kept separate because the two realisation rates differ materially: an
        /// order enters a station whole once any of it is bound, while its lines are gated one by
        /// one by slot capacity, so the order-level rate runs higher (measured 0.2837 against the
        /// line-level 0.1995 on the canonical seed).</summary>
        public void RegisterDecisionOrders(int boundOrders, int valuedOrders)
        {
            if (boundOrders > 0) _boundOrdersTotal += boundOrders;
            if (valuedOrders > 0) _valuedOrdersTotal += valuedOrders;
        }

        /// <summary>Order-level realisation rate, used for the mu terms when
        /// IM4GPrices.SeparateOrderDelta is on. Falls back to the line-level Delta() while the
        /// order statistics are still thin, so early decisions are unchanged.</summary>
        public double DeltaOrder()
        {
            if (_config.DeltaFixed > 0) return Math.Min(1.0, _config.DeltaFixed);
            if (_valuedOrdersTotal <= 0 || _completedOrders < _config.WarmupLines / 4)
                return Delta();
            double raw = (double)_boundOrdersTotal / _valuedOrdersTotal;
            if (raw < 0.0) raw = 0.0;
            if (raw > 1.0) raw = 1.0;
            return _config.DeltaScale * raw;
        }

        public void RegisterDecision(int boundLines, int valuedLines)
        { RegisterDecision(boundLines, valuedLines, -1); }

        /// <summary>
        /// Same, but also credits the decision to a stratum so <see cref="Delta(int)"/> can
        /// condition on it. A negative stratum only updates the system-wide totals, which is
        /// what every caller that does not opt in passes.
        /// </summary>
        public void RegisterDecision(int boundLines, int valuedLines, int stratum)
        {
            if (boundLines > 0) _boundLinesTotal += boundLines;
            if (valuedLines > 0) _valuedLinesTotal += valuedLines;
            if (stratum < 0) return;
            long[] cell;
            if (!_strata.TryGetValue(stratum, out cell)) { cell = new long[2]; _strata[stratum] = cell; }
            if (boundLines > 0) cell[0] += boundLines;
            if (valuedLines > 0) cell[1] += valuedLines;
        }

        /// <summary>Stable key for a line, shared by the valuation bookkeeping and the commit path.</summary>
        public static string LineKey(int orderId, int skuId)
        { return orderId.ToString() + ":" + skuId.ToString(); }
    }
}
