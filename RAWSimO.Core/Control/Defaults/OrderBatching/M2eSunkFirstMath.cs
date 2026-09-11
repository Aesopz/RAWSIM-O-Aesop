using System;
using System.Collections.Generic;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure arithmetic helpers for M2e sunk-first scoring and diagnostics.
    /// </summary>
    public static class M2eSunkFirstMath
    {
        /// <summary>
        /// Returns a coefficient that makes one additional completed order dominate every
        /// possible change in the completed-item tie-break.
        /// </summary>
        public static int DominanceMultiplier(IEnumerable<int> residualOrderTotals)
        {
            if (residualOrderTotals == null)
                throw new ArgumentNullException(nameof(residualOrderTotals));

            checked
            {
                int total = 0;
                foreach (int quantity in residualOrderTotals)
                {
                    if (quantity < 0)
                        throw new ArgumentOutOfRangeException(nameof(residualOrderTotals), "Residual quantities cannot be negative.");
                    total += quantity;
                }
                return total + 1;
            }
        }

        /// <summary>
        /// Evaluates the integer lexicographic score used by the first solve.
        /// </summary>
        public static long LexicographicScore(int completedOrders, int completedResidualItems, int dominanceMultiplier)
        {
            if (completedOrders < 0)
                throw new ArgumentOutOfRangeException(nameof(completedOrders));
            if (completedResidualItems < 0)
                throw new ArgumentOutOfRangeException(nameof(completedResidualItems));
            if (dominanceMultiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(dominanceMultiplier));

            checked
            {
                return (long)dominanceMultiplier * completedOrders + completedResidualItems;
            }
        }

        /// <summary>
        /// Checks true completion against every residual line. Missing supply is zero.
        /// </summary>
        public static bool IsFullySupplied<TKey>(IEnumerable<KeyValuePair<TKey, int>> residual,
            IDictionary<TKey, int> sunkSupply)
        {
            if (residual == null)
                throw new ArgumentNullException(nameof(residual));
            if (sunkSupply == null)
                throw new ArgumentNullException(nameof(sunkSupply));

            foreach (var line in residual)
            {
                if (line.Value < 0)
                    throw new ArgumentOutOfRangeException(nameof(residual), "Residual quantities cannot be negative.");
                if (line.Value == 0)
                    continue;

                int supplied;
                if (!sunkSupply.TryGetValue(line.Key, out supplied) || supplied < line.Value)
                    return false;
            }
            return true;
        }
    }
}
