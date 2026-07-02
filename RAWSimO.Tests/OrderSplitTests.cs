using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class OrderSplitTests
    {
        private static Instance _instance = new Instance();
        private static ItemDescription Sku() { return new SimpleItemDescription(_instance); }
        private static KeyValuePair<ItemDescription, int> Q(ItemDescription i, int q)
        { return new KeyValuePair<ItemDescription, int>(i, q); }

        public static void Register()
        {
            TestRunner.Add("CreateSplitChild_ClaimsLedger", () =>
            {
                var a = Sku(); var b = Sku();
                var parent = new Order();
                parent.AddPosition(a, 3); parent.AddPosition(b, 1);
                var child = Order.CreateSplitChild(parent, new[] { Q(a, 2) });
                TestRunner.AssertEqual(1, parent.GetRemainingDemand(a), "remaining A after claim 2/3");
                TestRunner.AssertEqual(1, parent.GetRemainingDemand(b), "remaining B untouched");
                TestRunner.AssertEqual(2, child.GetDemandCount(a), "child holds A x2");
                TestRunner.AssertTrue(child.Parent == parent, "child back-reference");
                TestRunner.AssertTrue(parent.IsSplitParent, "parent flagged");
                TestRunner.AssertTrue(!parent.IsFullyClaimed, "not fully claimed yet");
            });
            TestRunner.Add("CreateSplitChild_Overclaim_Throws", () =>
            {
                var a = Sku();
                var parent = new Order();
                parent.AddPosition(a, 3);
                TestRunner.AssertThrows<InvalidOperationException>(
                    () => Order.CreateSplitChild(parent, new[] { Q(a, 4) }), "overclaim must throw");
            });
            TestRunner.Add("RemainingPositions_ExcludesClaimed", () =>
            {
                var a = Sku(); var b = Sku();
                var parent = new Order();
                parent.AddPosition(a, 2); parent.AddPosition(b, 1);
                Order.CreateSplitChild(parent, new[] { Q(a, 2) });
                var rem = parent.RemainingPositions.ToList();
                TestRunner.AssertEqual(1, rem.Count, "only B remains");
                TestRunner.AssertTrue(rem[0].Key == b && rem[0].Value == 1, "B x1 remains");
            });
            TestRunner.Add("Consolidation_OnlyWhenAllChildrenDone", () =>
            {
                var a = Sku(); var b = Sku();
                var parent = new Order();
                parent.AddPosition(a, 3); parent.AddPosition(b, 1);
                var c1 = Order.CreateSplitChild(parent, new[] { Q(a, 2) });
                var c2 = Order.CreateSplitChild(parent, new[] { Q(a, 1), Q(b, 1) });
                TestRunner.AssertTrue(parent.IsFullyClaimed, "fully claimed");
                TestRunner.AssertTrue(!parent.NotifyChildCompleted(c1), "first child alone must not complete parent");
                TestRunner.AssertTrue(parent.NotifyChildCompleted(c2), "last child completes parent");
            });
            TestRunner.Add("Consolidation_NotBeforeFullyClaimed", () =>
            {
                var a = Sku();
                var parent = new Order();
                parent.AddPosition(a, 3);
                var c1 = Order.CreateSplitChild(parent, new[] { Q(a, 1) });
                TestRunner.AssertTrue(!parent.NotifyChildCompleted(c1), "residual demand keeps parent open");
            });
            TestRunner.Add("Child_InheritsTiming", () =>
            {
                var a = Sku();
                var parent = new Order();
                parent.AddPosition(a, 2);
                parent.TimeStamp = 123.0; parent.DueTime = 456.0;
                parent.TimePlaced = new DateTime(2026, 7, 2);
                var child = Order.CreateSplitChild(parent, new[] { Q(a, 1) });
                TestRunner.AssertTrue(child.TimeStamp == 123.0 && child.DueTime == 456.0
                    && child.TimePlaced == new DateTime(2026, 7, 2), "child inherits timing meta");
            });
        }
    }
}
