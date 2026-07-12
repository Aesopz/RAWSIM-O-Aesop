using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Control.Defaults.OrderBatching;

namespace RAWSimO.Tests
{
    public static class LbLedgerTests
    {
        private static LbLedger<string, string, string> NewLedger() { return new LbLedger<string, string, string>(); }

        public static void Register()
        {
            TestRunner.Add("LbLedger_AddIncrementsPlannedCount", () =>
            {
                var l = NewLedger();
                l.Add("o1", "s0", new[] { "p1" }, 10.0);
                l.Add("o2", "s0", new[] { "p2" }, 11.0);
                l.Add("o3", "s1", new[] { "p1" }, 12.0);
                TestRunner.AssertTrue(l.PlannedCount("s0") == 2, "s0 planned=2");
                TestRunner.AssertTrue(l.PlannedCount("s1") == 1, "s1 planned=1");
                TestRunner.AssertTrue(l.PlannedCount("s9") == 0, "unknown station=0");
                TestRunner.AssertTrue(l.Count == 3, "total=3");
            });
            TestRunner.Add("LbLedger_OrdersFor_FiltersPodAndStation", () =>
            {
                var l = NewLedger();
                l.Add("o1", "s0", new[] { "p1", "p2" }, 10.0);
                l.Add("o2", "s0", new[] { "p2" }, 11.0);
                l.Add("o3", "s1", new[] { "p2" }, 12.0);
                var hits = l.OrdersFor("p2", "s0");
                TestRunner.AssertTrue(hits.Count == 2 && hits[0] == "o1" && hits[1] == "o2", "p2@s0 -> o1,o2 oldest first");
                TestRunner.AssertTrue(l.OrdersFor("p1", "s1").Count == 0, "p1@s1 -> none");
            });
            TestRunner.Add("LbLedger_MultiSupplier_AnyPodTriggers", () =>
            {
                var l = NewLedger();
                l.Add("o1", "s0", new[] { "pA", "pB" }, 5.0);
                TestRunner.AssertTrue(l.OrdersFor("pA", "s0").Count == 1, "first supplier pA finds o1");
                TestRunner.AssertTrue(l.OrdersFor("pB", "s0").Count == 1, "supplier pB also finds o1");
            });
            TestRunner.Add("LbLedger_RemoveDecrementsAndForgets", () =>
            {
                var l = NewLedger();
                l.Add("o1", "s0", new[] { "p1" }, 10.0);
                l.Remove("o1");
                TestRunner.AssertTrue(l.PlannedCount("s0") == 0, "planned back to 0");
                TestRunner.AssertTrue(!l.Contains("o1"), "o1 gone");
                TestRunner.AssertTrue(double.IsPositiveInfinity(l.PlannedAt("o1")), "PlannedAt=inf after remove");
                l.Remove("o1"); // no-op, must not throw
                TestRunner.AssertTrue(l.Count == 0, "still empty");
            });
            TestRunner.Add("LbLedger_StationOfAndPlannedAt", () =>
            {
                var l = NewLedger();
                l.Add("o1", "s1", new[] { "p1" }, 42.5);
                TestRunner.AssertTrue(l.StationOf("o1") == "s1", "station recorded");
                TestRunner.AssertTrue(Math.Abs(l.PlannedAt("o1") - 42.5) < 1e-12, "plannedAt recorded");
                TestRunner.AssertTrue(l.StationOf("oX") == null, "unknown order -> default(null)");
            });
            TestRunner.Add("LbLedger_DueOrders_TimeoutOldestFirst", () =>
            {
                var l = NewLedger();
                l.Add("oOld", "s0", new[] { "p1" }, 0.0);
                l.Add("oMid", "s0", new[] { "p1" }, 50.0);
                l.Add("oNew", "s0", new[] { "p1" }, 199.0);
                var due = l.DueOrders(200.0, 100.0); // overdue: age>100 -> oOld(200), oMid(150); oNew(1) not
                TestRunner.AssertTrue(due.Count == 2 && due[0] == "oOld" && due[1] == "oMid", "oldest first, oNew excluded");
            });
            TestRunner.Add("LbLedger_DuplicateAddThrows", () =>
            {
                var l = NewLedger();
                l.Add("o1", "s0", new[] { "p1" }, 1.0);
                bool threw = false;
                try { l.Add("o1", "s0", new[] { "p2" }, 2.0); }
                catch (ArgumentException) { threw = true; }
                TestRunner.AssertTrue(threw, "double-claim must throw");
            });
            TestRunner.Add("LbLedger_RemainingCapacity_ClampsAtZero", () =>
            {
                TestRunner.AssertTrue(LbLedger<string, string, string>.RemainingCapacity(6, 2, 1, 0) == 3, "6-2-1-0=3");
                TestRunner.AssertTrue(LbLedger<string, string, string>.RemainingCapacity(12, 4, 2, 3) == 3, "12-4-2-3=3");
                TestRunner.AssertTrue(LbLedger<string, string, string>.RemainingCapacity(6, 6, 0, 4) == 0, "negative clamps to 0");
            });
        }
    }
}
