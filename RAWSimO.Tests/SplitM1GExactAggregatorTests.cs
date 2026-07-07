using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core;
using RAWSimO.Core.Control.Defaults.OrderBatching;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class SplitM1GExactAggregatorTests
    {
        private static Instance _instance = new Instance();
        private static ItemDescription Sku() { return new SimpleItemDescription(_instance); }
        private static KeyValuePair<ItemDescription, int> Q(ItemDescription i, int q)
        { return new KeyValuePair<ItemDescription, int>(i, q); }

        public static void Register()
        {
            TestRunner.Add("Aggregator_SumsAcrossPods_SameSku", () =>
            {
                var a = Sku();
                var result = SplitM1GExactAggregator.AggregatePodQuantities(new[] { Q(a, 2), Q(a, 3) });
                TestRunner.AssertEqual(1, result.Count, "one distinct SKU");
                TestRunner.AssertEqual(5, result[a], "2 (pod X) + 3 (pod Y) = 5");
            });
            TestRunner.Add("Aggregator_KeepsSkusSeparate", () =>
            {
                var a = Sku(); var b = Sku();
                var result = SplitM1GExactAggregator.AggregatePodQuantities(new[] { Q(a, 2), Q(b, 4) });
                TestRunner.AssertEqual(2, result.Count, "two distinct SKUs");
                TestRunner.AssertEqual(2, result[a], "SKU a untouched by SKU b's entry");
                TestRunner.AssertEqual(4, result[b], "SKU b untouched by SKU a's entry");
            });
            TestRunner.Add("Aggregator_DropsNonPositive", () =>
            {
                var a = Sku(); var b = Sku();
                var result = SplitM1GExactAggregator.AggregatePodQuantities(new[] { Q(a, 0), Q(b, -1), Q(a, 3) });
                TestRunner.AssertEqual(1, result.Count, "only the positive entry for a survives");
                TestRunner.AssertEqual(3, result[a], "zero-quantity pod entries do not contribute");
                TestRunner.AssertTrue(!result.ContainsKey(b), "negative-quantity SKU is dropped entirely");
            });
            TestRunner.Add("Aggregator_EmptyInput_ReturnsEmpty", () =>
            {
                var result = SplitM1GExactAggregator.AggregatePodQuantities(Enumerable.Empty<KeyValuePair<ItemDescription, int>>());
                TestRunner.AssertEqual(0, result.Count, "no pods contributed -> empty dictionary");
            });
        }
    }
}
