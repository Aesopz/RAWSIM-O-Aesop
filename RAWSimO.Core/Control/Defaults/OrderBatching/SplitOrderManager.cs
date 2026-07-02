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
        /// Decides about the pending orders: earliest-due order first; splits it over the stations
        /// with free capacity according to SplitPlanner. A split parent stays in the backlog with its
        /// residual demand (cross-time) and keeps its original timestamps, so the existing urgency
        /// mechanisms apply to the residual automatically.
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
            foreach (var order in _pendingOrders.OrderBy(o => o.DueTime).ThenBy(o => o.ID).ToList())
            {
                // Stations with at least one free order slot, most free capacity first (deterministic tie-break)
                List<OutputStation> stations = Instance.OutputStations
                    .Where(s => FreeCapacity(s) > 0)
                    .OrderByDescending(s => FreeCapacity(s)).ThenBy(s => s.ID)
                    .ToList();
                if (stations.Count == 0)
                    return;
                // Remaining demand capped by the actually available stock
                List<KeyValuePair<ItemDescription, int>> remaining = order.RemainingPositions
                    .Select(p => new KeyValuePair<ItemDescription, int>(p.Key, Math.Min(p.Value, Instance.StockInfo.GetActualStock(p.Key))))
                    .Where(p => p.Value > 0)
                    .ToList();
                if (remaining.Count == 0)
                    continue;
                // M1 (cross-station only): the complete remaining demand must be stock-feasible this epoch
                if (!_config.CrossTime && remaining.Sum(r => r.Value) != order.RemainingPositions.Sum(r => r.Value))
                    continue;
                List<Dictionary<ItemDescription, int>> plan = SplitPlanner.ComputePlan(
                    remaining, stations.Count, _config.CrossTime, _config.MaxChildrenPerOrder, _config.MaxUnitsPerChild);
                if (plan.Count == 0)
                    continue;
                // Unsplit fast path: a single part covering the complete demand of an untouched order
                // -> allocate the order itself (no child overhead, plain semantics).
                if (plan.Count == 1 && !order.IsSplitParent && plan[0].Values.Sum() == order.GetOpenDemandCount())
                {
                    AllocateOrder(order, stations[0]);
                    continue;
                }
                // Create and allocate one child per part (claiming = atomic in CreateSplitChild)
                for (int i = 0; i < plan.Count; i++)
                {
                    Order child = Order.CreateSplitChild(order, plan[i]);
                    child.ID = idoforder++;
                    Instance.ResourceManager.TransferExtractRequests(order, child);
                    AllocateOrder(child, stations[i]);
                }
                // Parent KPI: submit timestamp = first (partial) allocation
                if (double.IsPositiveInfinity(order.TimeStampSubmit))
                    order.TimeStampSubmit = Instance.Controller.CurrentTime;
                // Parent leaves the backlog once its demand is fully claimed (children may still be picking)
                if (order.IsFullyClaimed)
                {
                    _pendingOrders.Remove(order);
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(order);
                }
            }
        }

        /// <summary>
        /// Signals the current time to the mechanism.
        /// </summary>
        /// <param name="currentTime">The current simulation time.</param>
        public override void SignalCurrentTime(double currentTime) { /* nothing to do */ }
    }
}
