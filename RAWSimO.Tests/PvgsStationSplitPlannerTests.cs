using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core;
using RAWSimO.Core.Control.Defaults.OrderBatching;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class PvgsStationSplitPlannerTests
    {
        private static Instance _instance = new Instance();
        private static ItemDescription Sku() { return new SimpleItemDescription(_instance); }
        private static KeyValuePair<ItemDescription, int> Q(ItemDescription i, int q)
        { return new KeyValuePair<ItemDescription, int>(i, q); }
        private static Dictionary<ItemDescription, int> D(params KeyValuePair<ItemDescription, int>[] entries)
        {
            var d = new Dictionary<ItemDescription, int>();
            foreach (var e in entries) d[e.Key] = e.Value;
            return d;
        }

        public static void Register()
        {
            TestRunner.Add("Planner_SingleStation_Preferred", () =>
            {
                var a = Sku(); var b = Sku();
                // both stations could serve, single-station coverage must win (fewest stations)
                var parts = PvgsStationSplitPlanner.PlanCompletion(
                    new[] { Q(a, 2), Q(b, 1) }.ToList(),
                    new List<Dictionary<ItemDescription, int>> { D(Q(a, 2), Q(b, 1)), D(Q(a, 5), Q(b, 5)) },
                    new[] { true, true });
                TestRunner.AssertEqual(1, parts.Count, "single part");
                TestRunner.AssertEqual(0, parts[0].Key, "first fully-covering station wins");
                TestRunner.AssertEqual(2, parts[0].Value[a], "full A quantity at station 0");
            });
            TestRunner.Add("Planner_TwoStation_Split", () =>
            {
                var a = Sku(); var b = Sku();
                // A only at station 0, B only at station 1 -> must split across both
                var parts = PvgsStationSplitPlanner.PlanCompletion(
                    new[] { Q(a, 2), Q(b, 1) }.ToList(),
                    new List<Dictionary<ItemDescription, int>> { D(Q(a, 2)), D(Q(b, 1)) },
                    new[] { true, true });
                TestRunner.AssertEqual(2, parts.Count, "two parts");
                TestRunner.AssertEqual(2, parts.First(p => p.Key == 0).Value[a], "A at station 0");
                TestRunner.AssertEqual(1, parts.First(p => p.Key == 1).Value[b], "B at station 1");
            });
            TestRunner.Add("Planner_Infeasible_ReturnsNull", () =>
            {
                var a = Sku();
                var parts = PvgsStationSplitPlanner.PlanCompletion(
                    new[] { Q(a, 5) }.ToList(),
                    new List<Dictionary<ItemDescription, int>> { D(Q(a, 2)), D(Q(a, 2)) },
                    new[] { true, true });
                TestRunner.AssertTrue(parts == null, "4 < 5 total -> null");
            });
            TestRunner.Add("Planner_SlotBlocked_UsesOtherStation", () =>
            {
                var a = Sku();
                // station 0 covers but has no free slot -> station 1 must be chosen
                var parts = PvgsStationSplitPlanner.PlanCompletion(
                    new[] { Q(a, 2) }.ToList(),
                    new List<Dictionary<ItemDescription, int>> { D(Q(a, 9)), D(Q(a, 2)) },
                    new[] { false, true });
                TestRunner.AssertEqual(1, parts.Count, "single part");
                TestRunner.AssertEqual(1, parts[0].Key, "slot-blocked station skipped");
            });
            TestRunner.Add("Planner_Partial_BestStationByUnits", () =>
            {
                var a = Sku(); var b = Sku();
                int station;
                // station 0 can give 1xA; station 1 can give 2xA + 1xB = 3 units -> station 1 wins
                var part = PvgsStationSplitPlanner.PlanPartial(
                    new[] { Q(a, 4), Q(b, 2) }.ToList(),
                    new List<Dictionary<ItemDescription, int>> { D(Q(a, 1)), D(Q(a, 2), Q(b, 1)) },
                    new[] { true, true }, 2, out station);
                TestRunner.AssertEqual(1, station, "max-units station selected");
                TestRunner.AssertEqual(2, part[a], "capped at avail");
                TestRunner.AssertEqual(1, part[b], "capped at avail");
            });
            TestRunner.Add("Planner_Partial_ThetaRejectsTinyParts", () =>
            {
                var a = Sku();
                int station;
                var part = PvgsStationSplitPlanner.PlanPartial(
                    new[] { Q(a, 5) }.ToList(),
                    new List<Dictionary<ItemDescription, int>> { D(Q(a, 1)) },
                    new[] { true }, 2, out station);
                TestRunner.AssertTrue(part == null && station == -1, "1 unit < theta=2 -> rejected");
            });
        }
    }
}
