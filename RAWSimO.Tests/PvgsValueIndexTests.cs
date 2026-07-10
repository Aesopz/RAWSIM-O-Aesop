using System;
using System.Collections.Generic;
using RAWSimO.Core;
using RAWSimO.Core.Control.Defaults.OrderBatching;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class PvgsValueIndexTests
    {
        private static Instance _instance = new Instance();
        private static ItemDescription Sku() { return new SimpleItemDescription(_instance); }
        private static Dictionary<ItemDescription, int> D(params KeyValuePair<ItemDescription, int>[] entries)
        {
            var d = new Dictionary<ItemDescription, int>();
            foreach (var e in entries) d[e.Key] = e.Value;
            return d;
        }
        private static KeyValuePair<ItemDescription, int> Q(ItemDescription i, int q)
        { return new KeyValuePair<ItemDescription, int>(i, q); }
        private static void AssertClose(double expected, double actual, string message)
        { TestRunner.AssertTrue(Math.Abs(expected - actual) < 1e-9, message + " (expected " + expected + ", got " + actual + ")"); }

        public static void Register()
        {
            TestRunner.Add("ValueIndex_MinOfAvailAndDemand_NoScarcity", () =>
            {
                var a = Sku();
                // avail 5, demand 3, supply 10, beta 0 -> min(5,3) * 1 = 3
                double v = PvgsValueIndex.ComputeValue(D(Q(a, 5)), D(Q(a, 3)), D(Q(a, 10)), 0.0);
                AssertClose(3.0, v, "beta=0 -> pure min(avail, demand)");
            });
            TestRunner.Add("ValueIndex_ScarcityBoost", () =>
            {
                var a = Sku();
                // avail 2, demand 4, supply 4, beta 1 -> min(2,4) * (1 + 4/4) = 2 * 2 = 4
                double v = PvgsValueIndex.ComputeValue(D(Q(a, 2)), D(Q(a, 4)), D(Q(a, 4)), 1.0);
                AssertClose(4.0, v, "scarce SKU (R/supply=1) doubles at beta=1");
            });
            TestRunner.Add("ValueIndex_FractionalScarcity", () =>
            {
                var a = Sku();
                // avail 5, demand 3, supply 10, beta 1 -> 3 * (1 + 0.3) = 3.9
                double v = PvgsValueIndex.ComputeValue(D(Q(a, 5)), D(Q(a, 3)), D(Q(a, 10)), 1.0);
                AssertClose(3.9, v, "sigma = 3/10 boosts by 30 percent at beta=1");
            });
            TestRunner.Add("ValueIndex_ZeroDemandSku_ContributesNothing", () =>
            {
                var a = Sku(); var b = Sku();
                // b has no residual demand; a contributes 2 * (1 + 1) = 4 (supply==demand)
                double v = PvgsValueIndex.ComputeValue(D(Q(a, 2), Q(b, 9)), D(Q(a, 2)), D(Q(a, 2), Q(b, 9)), 1.0);
                AssertClose(4.0, v, "undemanded stock is worthless");
            });
            TestRunner.Add("ValueIndex_MissingSupplyTotal_TreatedAsScarce", () =>
            {
                var a = Sku();
                // supply dict lacks a -> sigma treated as 1.0 -> 1 * (1 + 1) = 2
                double v = PvgsValueIndex.ComputeValue(D(Q(a, 1)), D(Q(a, 2)), new Dictionary<ItemDescription, int>(), 1.0);
                AssertClose(2.0, v, "missing supply total falls back to sigma=1");
            });
        }
    }
}
