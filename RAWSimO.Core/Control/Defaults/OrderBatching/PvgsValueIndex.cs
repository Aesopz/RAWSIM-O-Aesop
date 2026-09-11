using System;
using System.Collections.Generic;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure (solver-free) pod value function for PVGS: how much of the backlog's residual
    /// demand can this pod's current stock serve, weighting scarce SKUs higher because they
    /// gate order completion (the exact model's per-order reward, translated to a continuous
    /// proxy). value = sum_i min(avail[i], R[i]) * (1 + beta * R[i]/supply[i]).
    /// </summary>
    public static class PvgsValueIndex
    {
        /// <summary>
        /// Computes the value of one pod's available stock against global residual demand.
        /// SKUs without residual demand contribute nothing; a missing or non-positive supply
        /// total is treated as maximally scarce (sigma = 1).
        /// </summary>
        public static double ComputeValue(
            Dictionary<ItemDescription, int> podAvail,
            Dictionary<ItemDescription, int> residualTotal,
            Dictionary<ItemDescription, int> supplyTotal,
            double scarcityBeta)
        {
            double value = 0.0;
            foreach (var entry in podAvail)
            {
                if (entry.Value <= 0)
                    continue;
                int demand;
                if (!residualTotal.TryGetValue(entry.Key, out demand) || demand <= 0)
                    continue;
                int supply;
                double sigma = supplyTotal.TryGetValue(entry.Key, out supply) && supply > 0
                    ? Math.Min(1.0, (double)demand / supply)
                    : 1.0;
                value += Math.Min(entry.Value, demand) * (1.0 + scarcityBeta * sigma);
            }
            return value;
        }
    }
}
