using System.Collections.Generic;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure (Gurobi-free) helper for SplitM1GExact: collapses a flat list of per-pod
    /// (SKU, quantity) extraction amounts for one (order, station) pair into a single per-SKU
    /// dictionary, summing across however many pods contributed. Feeds the result into
    /// SplitMilpDecoder.Decode's per-station quantity input (unchanged from Spec 2), reusing
    /// its station-level child/fast-path decision logic without needing a pod-aware decoder.
    /// </summary>
    public static class SplitM1GExactAggregator
    {
        /// <summary>
        /// Sums quantities per SKU across all contributing pods. Non-positive entries are
        /// dropped (a solved q[i,o,p,s] of 0 for some pod should not add a zero/negative key).
        /// </summary>
        public static Dictionary<ItemDescription, int> AggregatePodQuantities(
            IEnumerable<KeyValuePair<ItemDescription, int>> perPodQuantities)
        {
            Dictionary<ItemDescription, int> result = new Dictionary<ItemDescription, int>();
            foreach (var entry in perPodQuantities)
            {
                if (entry.Value <= 0)
                    continue;
                if (result.ContainsKey(entry.Key))
                    result[entry.Key] += entry.Value;
                else
                    result[entry.Key] = entry.Value;
            }
            return result;
        }
    }
}
