using System;
using System.Collections.Generic;
using RAWSimO.Core.Configurations;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Price state machine for M4G-NS. Deliberately NOT a reuse of M4GPricing.
    ///
    /// M4G-NS's decision atom is the ORDER, so its price list may only contain order-denominated
    /// prices (spec 2026-08-09 INV-2). This class therefore has no lambda (metres per line), no
    /// rho (metres per unit) and no epsilon (metres per unit) - not "set to zero", but absent.
    /// You cannot accidentally price a line here, because the field does not exist. That is the
    /// point: the invariant is structural, not configured.
    ///
    ///   mu    = cumulative distance / cumulative completed orders     [m / order]
    ///   delta = cumulative bound completions / cumulative valued ones [0..1]
    ///
    /// mu is MEASURED directly. It is emphatically not lambda * lines-per-order, which is how
    /// M4G derives its mu - that derivation needs lambda, and lambda does not exist here.
    ///
    /// Pure state machine: never touches the solver, the instance or the file system.
    /// </summary>
    public class M4GNSPricing
    {
        private readonly M4GNSConfiguration _config;
        private int _completedOrders;
        private long _boundOrdersTotal;
        private long _valuedOrdersTotal;
        /// <summary>Per-stratum {bound, valued} order totals; only read when StratifiedDelta is on.</summary>
        private readonly Dictionary<int, long[]> _strata = new Dictionary<int, long[]>();

        /// <summary>Creates the pricing state machine.</summary>
        /// <param name="config">The owning manager's configuration.</param>
        public M4GNSPricing(M4GNSConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            _config = config;
        }

        /// <summary>Cumulative number of orders completed so far.</summary>
        public int CumulativeCompletedOrders { get { return _completedOrders; } }
        /// <summary>Cumulative orders the binding layer completed, across all decisions.</summary>
        public long CumulativeBoundOrders { get { return _boundOrdersTotal; } }
        /// <summary>Cumulative orders the valuation layer completed, across all decisions.</summary>
        public long CumulativeValuedOrders { get { return _valuedOrdersTotal; } }

        /// <summary>Whether the running statistics are still too thin to price from.</summary>
        private bool InWarmup { get { return _completedOrders < _config.WarmupOrders; } }

        /// <summary>Metres per completed order. Measured, not derived.</summary>
        /// <param name="cumulativeDistanceMetres">Instance.StatOverallDistanceTraveled at decision time.</param>
        public double Mu(double cumulativeDistanceMetres)
        {
            if (_config.MuFixed > 0) return _config.MuFixed;
            if (InWarmup) return _config.MuScale * _config.MuFallback;
            return _config.MuScale * (cumulativeDistanceMetres / Math.Max(1, _completedOrders));
        }

        /// <summary>System-wide realisation rate.</summary>
        public double Delta() { return Delta(-1); }

        /// <summary>
        /// Stratum-conditioned realisation rate: of the orders the valuation layer scored complete,
        /// what share did the binding layer actually take. A stratum below DeltaStratumMinOrders
        /// falls back to the system-wide ratio, as does stratum &lt; 0 or StratifiedDelta off.
        /// </summary>
        /// <param name="stratum">Bucket key for this decision, min(|Pb|, 3) to match M4G.</param>
        public double Delta(int stratum)
        {
            if (_config.DeltaFixed > 0) return Math.Min(1.0, _config.DeltaFixed);
            double raw;
            if (InWarmup) raw = _config.DeltaFallback;
            else
            {
                long bound = _boundOrdersTotal, valued = _valuedOrdersTotal;
                long[] cell;
                if (_config.StratifiedDelta && stratum >= 0
                    && _strata.TryGetValue(stratum, out cell)
                    && cell[1] >= _config.DeltaStratumMinOrders)
                { bound = cell[0]; valued = cell[1]; }
                raw = (double)bound / Math.Max(1, valued);
            }
            return Math.Max(0.0, Math.Min(1.0, _config.DeltaScale * raw));
        }

        /// <summary>Records orders completed by this decision (mu's denominator).</summary>
        public void RegisterCompletedOrders(int count)
        { if (count > 0) _completedOrders += count; }

        /// <summary>Feeds one decision's bound/valued completion counts into delta's totals.</summary>
        public void RegisterDecision(int boundOrders, int valuedOrders)
        { RegisterDecision(boundOrders, valuedOrders, -1); }

        /// <summary>Stratified counterpart; stratum &lt; 0 updates only the system-wide totals.</summary>
        public void RegisterDecision(int boundOrders, int valuedOrders, int stratum)
        {
            if (boundOrders > 0) _boundOrdersTotal += boundOrders;
            if (valuedOrders > 0) _valuedOrdersTotal += valuedOrders;
            if (stratum < 0) return;
            long[] cell;
            if (!_strata.TryGetValue(stratum, out cell))
            {
                cell = new long[2];
                _strata[stratum] = cell;
            }
            if (boundOrders > 0) cell[0] += boundOrders;
            if (valuedOrders > 0) cell[1] += valuedOrders;
        }
    }
}
