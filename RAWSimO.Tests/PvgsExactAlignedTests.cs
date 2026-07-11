using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core;
using RAWSimO.Core.Control.Defaults.OrderBatching;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class PvgsExactAlignedTests
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
        private static void AssertClose(double expected, double actual, string message)
        { TestRunner.AssertTrue(Math.Abs(expected - actual) < 1e-9, message + " (expected " + expected + ", got " + actual + ")"); }

        public static void Register()
        {
            TestRunner.Add("EScore_CompletionDominatesDistance", () =>
            {
                // one completion (40) minus 30m total distance = 10 > 0 -> dispatch worth it
                AssertClose(10.0, PvgsExactAligned.Score(1, 0, 10, 20, 40, 1, 0, 0), "40*1 - 1*(10+20)");
            });
            TestRunner.Add("EScore_NoCompletion_Negative", () =>
            {
                // no completion -> pure cost, must be negative (dispatch refused)
                AssertClose(-10.0, PvgsExactAligned.Score(0, 0, 5, 5, 40, 1, 0, 0), "-(5+5)");
            });
            TestRunner.Add("EScore_TripTaxSubtracted", () =>
            {
                // w4=40 exactly cancels one completion at zero distance
                AssertClose(0.0, PvgsExactAligned.Score(1, 0, 0, 0, 40, 1, 40, 0), "40 - w4(40)");
            });
            TestRunner.Add("EScore_EpsilonAddsPerUnit_MinimizationSign", () =>
            {
                // unitDrawReward is the MINIMIZATION-form epsilon (negative = reward):
                // eps=-0.1 with 5 units must ADD 0.5 to the maximization score
                AssertClose(40.5, PvgsExactAligned.Score(1, 5, 0, 0, 40, 1, 0, -0.1), "40 + 0.1*5");
            });
            TestRunner.Add("ESqueeze_PicksMaxUnitsStation", () =>
            {
                var a = Sku(); var b = Sku();
                int stationIndex;
                // residual A=3,B=2; station0 offers 2 units of A, station1 offers 3 (A2+B1) -> station1
                var part = PvgsExactAligned.PlanSqueeze(
                    new[] { Q(a, 3), Q(b, 2) }.ToList(),
                    new List<Dictionary<ItemDescription, int>> { D(Q(a, 2)), D(Q(a, 2), Q(b, 1)) },
                    new[] { true, true }, out stationIndex);
                TestRunner.AssertEqual(1, stationIndex, "max-units station wins");
                TestRunner.AssertEqual(2, part[a], "A capped at station avail");
                TestRunner.AssertEqual(1, part[b], "B capped at station avail");
            });
            TestRunner.Add("ESqueeze_RespectsSlotFree", () =>
            {
                var a = Sku();
                int stationIndex;
                // best station (0, 5 units) has no free slot -> falls back to station 1
                var part = PvgsExactAligned.PlanSqueeze(
                    new[] { Q(a, 5) }.ToList(),
                    new List<Dictionary<ItemDescription, int>> { D(Q(a, 5)), D(Q(a, 2)) },
                    new[] { false, true }, out stationIndex);
                TestRunner.AssertEqual(1, stationIndex, "blocked station skipped");
                TestRunner.AssertEqual(2, part[a], "falls back to lesser station");
            });
            TestRunner.Add("ESqueeze_NothingDrawable_Null", () =>
            {
                var a = Sku(); var b = Sku();
                int stationIndex;
                // stations only hold SKU b, residual only needs a -> null
                var part = PvgsExactAligned.PlanSqueeze(
                    new[] { Q(a, 3) }.ToList(),
                    new List<Dictionary<ItemDescription, int>> { D(Q(b, 4)) },
                    new[] { true }, out stationIndex);
                TestRunner.AssertTrue(part == null, "no overlap -> null");
                TestRunner.AssertEqual(-1, stationIndex, "stationIndex stays -1");
            });
            TestRunner.Add("ESqueeze_CapsAtResidual", () =>
            {
                var a = Sku();
                int stationIndex;
                // station holds 10 but order only needs 4 -> take exactly 4
                var part = PvgsExactAligned.PlanSqueeze(
                    new[] { Q(a, 4) }.ToList(),
                    new List<Dictionary<ItemDescription, int>> { D(Q(a, 10)) },
                    new[] { true }, out stationIndex);
                TestRunner.AssertEqual(4, part[a], "take = min(avail, residual)");
            });
        }
    }
}
