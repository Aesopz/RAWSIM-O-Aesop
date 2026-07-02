using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Enabler manager for order splitting: greedily assigns (and, from Task 8 on, splits) pending
    /// orders to stations with free capacity. The original M1G / HADGS managers stay untouched and
    /// serve as the no-splitting ablation baseline.
    /// See docs/superpowers/specs/2026-07-02-order-splitting-consolidation-enabler-design.md.
    /// </summary>
    public class SplitOrderManager : OrderManager
    {
        /// <summary>
        /// Creates a new split-order manager.
        /// </summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public SplitOrderManager(Instance instance) : base(instance)
        { _config = instance.ControllerConfig.OrderBatchingConfig as SplitHeuristicConfiguration; }

        /// <summary>
        /// The configuration.
        /// </summary>
        private SplitHeuristicConfiguration _config;

        /// <summary>
        /// Free order slots of the station right now.
        /// </summary>
        private int FreeCapacity(OutputStation station)
        { return station.Capacity - station.CapacityInUse - station.CapacityReserved; }

        /// <summary>
        /// Decides about the pending orders: earliest-due order first to the station with most free capacity.
        /// (Splitting is added in a later task.)
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
            foreach (var order in _pendingOrders.OrderBy(o => o.DueTime).ThenBy(o => o.ID).ToList())
            {
                // Only stock-feasible orders
                if (!order.RemainingPositions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value))
                    continue;
                OutputStation station = Instance.OutputStations
                    .Where(s => FreeCapacity(s) > 0)
                    .OrderByDescending(s => FreeCapacity(s)).ThenBy(s => s.ID)
                    .FirstOrDefault();
                if (station == null)
                    return;
                AllocateOrder(order, station);
            }
        }

        /// <summary>
        /// Signals the current time to the mechanism.
        /// </summary>
        /// <param name="currentTime">The current simulation time.</param>
        public override void SignalCurrentTime(double currentTime) { /* nothing to do */ }
    }
}
