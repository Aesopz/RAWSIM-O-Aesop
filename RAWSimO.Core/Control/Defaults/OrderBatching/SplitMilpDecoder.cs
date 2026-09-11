using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure (Gurobi-free) decoder from a SplitM1G MILP solution slice of one order to per-station
    /// child plans. Validates the solution invariants defensively: over-assignment, unknown SKUs
    /// and M1 all-or-nothing violations throw instead of silently mis-assigning picks.
    /// See docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md.
    /// </summary>
    public static class SplitMilpDecoder
    {
        /// <summary>
        /// Decodes the q[o,i,s] values of one order into non-empty per-station quantity parts.
        /// </summary>
        /// <param name="remaining">The order's residual demand snapshot of this solve (per SKU, > 0).</param>
        /// <param name="perStationQuantities">q values aligned by station index (may contain zeros/empty dicts).</param>
        /// <param name="crossTime">False enforces M1 all-or-nothing (total assigned == total remaining or 0).</param>
        /// <param name="fullyAssigned">True if all remaining units are assigned in this solution.</param>
        /// <returns>(stationIndex, quantities) list containing only non-empty parts; empty list if nothing assigned.</returns>
        public static List<KeyValuePair<int, Dictionary<ItemDescription, int>>> Decode(
            IList<KeyValuePair<ItemDescription, int>> remaining,
            IList<Dictionary<ItemDescription, int>> perStationQuantities,
            bool crossTime,
            out bool fullyAssigned)
        {
            if (remaining == null)
                throw new ArgumentNullException("remaining");
            if (perStationQuantities == null)
                throw new ArgumentNullException("perStationQuantities");
            // Aggregate the residual (defensive: tolerate duplicate SKU entries by summing)
            Dictionary<ItemDescription, int> residual = new Dictionary<ItemDescription, int>();
            foreach (var position in remaining)
            {
                if (residual.ContainsKey(position.Key))
                    residual[position.Key] += position.Value;
                else
                    residual.Add(position.Key, position.Value);
            }
            Dictionary<ItemDescription, int> assigned = new Dictionary<ItemDescription, int>();
            List<KeyValuePair<int, Dictionary<ItemDescription, int>>> parts =
                new List<KeyValuePair<int, Dictionary<ItemDescription, int>>>();
            for (int stationIndex = 0; stationIndex < perStationQuantities.Count; stationIndex++)
            {
                Dictionary<ItemDescription, int> quantities = perStationQuantities[stationIndex];
                if (quantities == null)
                    continue;
                Dictionary<ItemDescription, int> part = quantities
                    .Where(v => v.Value > 0)
                    .ToDictionary(v => v.Key, v => v.Value);
                if (part.Count == 0)
                    continue;
                foreach (var sku in part)
                {
                    if (!residual.ContainsKey(sku.Key))
                        throw new InvalidOperationException("MILP assigned a SKU that is not part of the order's residual demand!");
                    if (assigned.ContainsKey(sku.Key))
                        assigned[sku.Key] += sku.Value;
                    else
                        assigned.Add(sku.Key, sku.Value);
                }
                parts.Add(new KeyValuePair<int, Dictionary<ItemDescription, int>>(stationIndex, part));
            }
            foreach (var sku in assigned)
                if (sku.Value > residual[sku.Key])
                    throw new InvalidOperationException("MILP assigned more units of a SKU than the residual demand!");
            int totalRemaining = residual.Values.Sum();
            int totalAssigned = assigned.Values.Sum();
            fullyAssigned = totalRemaining > 0 && totalAssigned == totalRemaining;
            // M1 all-or-nothing: per-SKU <= residual + total equality together imply per-SKU equality
            if (!crossTime && totalAssigned != 0 && !fullyAssigned)
                throw new InvalidOperationException("M1 all-or-nothing violated: order partially assigned!");
            return parts;
        }
    }
}
