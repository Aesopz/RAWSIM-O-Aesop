using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core;
using RAWSimO.Core.Control.Defaults.OrderBatching;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class SplitPlannerTests
    {
        private static Instance _instance = new Instance();
        private static ItemDescription Sku() { return new SimpleItemDescription(_instance); }
        private static KeyValuePair<ItemDescription, int> Q(ItemDescription i, int q)
        { return new KeyValuePair<ItemDescription, int>(i, q); }
        private static int Units(List<Dictionary<ItemDescription, int>> plan)
        { return plan.Sum(c => c.Values.Sum()); }
        private static int UnitsOf(List<Dictionary<ItemDescription, int>> plan, ItemDescription sku)
        { return plan.Sum(c => c.ContainsKey(sku) ? c[sku] : 0); }

        public static void Register()
        {
            TestRunner.Add("Planner_SingleSlot_NoSplit", () =>
            {
                var a = Sku(); var b = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 3), Q(b, 1) }.ToList(), 1, true, 2, 0);
                TestRunner.AssertEqual(1, plan.Count, "one child only");
                TestRunner.AssertEqual(4, Units(plan), "all units in the single child");
            });
            TestRunner.Add("Planner_TwoSlots_EvenSplit_ConservesQuantities", () =>
            {
                var a = Sku(); var b = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 3), Q(b, 1) }.ToList(), 2, true, 2, 0);
                TestRunner.AssertEqual(2, plan.Count, "two children");
                TestRunner.AssertEqual(4, Units(plan), "quantity conservation");
                TestRunner.AssertEqual(3, UnitsOf(plan, a), "A total conserved");
                TestRunner.AssertEqual(1, UnitsOf(plan, b), "B total conserved");
                TestRunner.AssertTrue(plan.All(c => c.Values.Sum() == 2), "even 2/2 split");
            });
            TestRunner.Add("Planner_SkuQuantitySplitAcrossChildren", () =>
            {
                var a = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 4) }.ToList(), 2, true, 2, 0);
                TestRunner.AssertEqual(2, plan.Count, "two children");
                TestRunner.AssertEqual(2, plan[0][a], "A split 2/2 - first child");
                TestRunner.AssertEqual(2, plan[1][a], "A split 2/2 - second child");
            });
            TestRunner.Add("Planner_M1_AssignsEverything", () =>
            {
                var a = Sku(); var b = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 5), Q(b, 2) }.ToList(), 3, false, 3, 0);
                TestRunner.AssertEqual(7, Units(plan), "M1 assigns all units");
            });
            TestRunner.Add("Planner_M2_CapLeavesResidual", () =>
            {
                var a = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 5) }.ToList(), 2, true, 2, 2);
                TestRunner.AssertEqual(4, Units(plan), "2 children x cap 2, residual 1 unassigned");
            });
            TestRunner.Add("Planner_NoSlots_Empty", () =>
            {
                var a = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 3) }.ToList(), 0, true, 2, 0);
                TestRunner.AssertEqual(0, plan.Count, "no slots, no plan");
            });
            TestRunner.Add("Planner_MoreSlotsThanUnits_NoEmptyChildren", () =>
            {
                var a = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 1) }.ToList(), 3, true, 3, 0);
                TestRunner.AssertEqual(1, plan.Count, "1 unit -> 1 child");
            });
        }
    }
}
