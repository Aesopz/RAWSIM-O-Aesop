using System;
using System.Collections.Generic;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control.Defaults.OrderBatching;

namespace RAWSimO.Tests
{
    public static class M2eSunkFirstMathTests
    {
        public static void Register()
        {
            TestRunner.Add("M2e-SF dominance multiplier is residual pool plus one", DominanceMultiplierUsesResidualPool);
            TestRunner.Add("M2e-SF one more completed order dominates all item tie-breaks", CompletedOrderDominatesItems);
            TestRunner.Add("M2e-SF item score breaks equal-order ties", ItemsBreakEqualOrderTie);
            TestRunner.Add("M2e-SF completion requires every residual SKU", CompletionRequiresEveryResidualSku);
            TestRunner.Add("M2e-SF completion accepts sufficient aggregated sunk supply", CompletionAcceptsAggregatedSupply);
            TestRunner.Add("M2e-SF rejects negative residual quantities", NegativeResidualIsRejected);
            TestRunner.Add("M2e-SF configuration defaults off", ConfigurationDefaultsOff);
        }

        private static void DominanceMultiplierUsesResidualPool()
        {
            TestRunner.AssertEqual(11, M2eSunkFirstMath.DominanceMultiplier(new[] { 2, 3, 5 }),
                "the multiplier must exceed the maximum possible item tie-break by one");
        }

        private static void CompletedOrderDominatesItems()
        {
            int multiplier = M2eSunkFirstMath.DominanceMultiplier(new[] { 2, 3, 5 });
            long fewerOrdersBestItems = M2eSunkFirstMath.LexicographicScore(1, 10, multiplier);
            long moreOrdersNoItems = M2eSunkFirstMath.LexicographicScore(2, 0, multiplier);
            TestRunner.AssertTrue(moreOrdersNoItems > fewerOrdersBestItems,
                "one additional completed order must dominate the entire item range");
        }

        private static void ItemsBreakEqualOrderTie()
        {
            int multiplier = M2eSunkFirstMath.DominanceMultiplier(new[] { 2, 3, 5 });
            long fewerItems = M2eSunkFirstMath.LexicographicScore(2, 4, multiplier);
            long moreItems = M2eSunkFirstMath.LexicographicScore(2, 5, multiplier);
            TestRunner.AssertTrue(moreItems > fewerItems,
                "completed residual items must decide ties at equal completed-order count");
        }

        private static void CompletionRequiresEveryResidualSku()
        {
            var residual = new Dictionary<string, int> { { "A", 2 }, { "B", 1 } };
            var sunkSupply = new Dictionary<string, int> { { "A", 2 } };
            TestRunner.AssertTrue(!M2eSunkFirstMath.IsFullySupplied(residual, sunkSupply),
                "a missing residual SKU must prevent true sunk completion");
        }

        private static void CompletionAcceptsAggregatedSupply()
        {
            var residual = new Dictionary<string, int> { { "A", 2 }, { "B", 1 } };
            var sunkSupply = new Dictionary<string, int> { { "A", 3 }, { "B", 1 } };
            TestRunner.AssertTrue(M2eSunkFirstMath.IsFullySupplied(residual, sunkSupply),
                "supply meeting every residual line must count as complete");
        }

        private static void NegativeResidualIsRejected()
        {
            TestRunner.AssertThrows<ArgumentOutOfRangeException>(
                () => M2eSunkFirstMath.DominanceMultiplier(new[] { 1, -1 }),
                "negative residual quantities are invalid");
        }

        private static void ConfigurationDefaultsOff()
        {
            var configuration = new SplitM1GExactConfiguration();
            TestRunner.AssertTrue(!configuration.SunkFirstScoring,
                "existing M0e/M1e/M2e configurations must stay on the legacy one-solve path");
        }
    }
}
