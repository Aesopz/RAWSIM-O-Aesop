using System;
using RAWSimO.Core.Control.Defaults.OrderBatching;

namespace RAWSimO.Tests
{
    public static class M2eICMathTests
    {
        public static void Register()
        {
            TestRunner.Add("M2eICMath.PaDrawBigM sums residual units", () =>
                TestRunner.AssertEqual(5, M2eICMath.PaDrawBigM(new[] { 3, 2 }), "3+2"));
            TestRunner.Add("M2eICMath.PaDrawBigM empty = 0", () =>
                TestRunner.AssertEqual(0, M2eICMath.PaDrawBigM(new int[0]), "empty"));
            TestRunner.Add("M2eICMath.PaDrawBigM rejects negatives", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.PaDrawBigM(new[] { 1, -1 }), "negative residual"));
            TestRunner.Add("M2eICMath.IsWholeEligible fresh + fully visible", () =>
                TestRunner.AssertTrue(M2eICMath.IsWholeEligible(false, false), "eligible"));
            TestRunner.Add("M2eICMath.IsWholeEligible split parent never whole (P1c)", () =>
                TestRunner.AssertTrue(!M2eICMath.IsWholeEligible(true, false), "parent"));
            TestRunner.Add("M2eICMath.IsWholeEligible OOS residual never whole (P1oos)", () =>
                TestRunner.AssertTrue(!M2eICMath.IsWholeEligible(false, true), "oos"));
            TestRunner.Add("M2eICMath.PackingBudget normal clamp", () =>
                TestRunner.AssertEqual(3, M2eICMath.PackingBudget(78, 75), "78-75"));
            TestRunner.Add("M2eICMath.PackingBudget floor at zero", () =>
                TestRunner.AssertEqual(0, M2eICMath.PackingBudget(78, 80), "over-occupied clamps to 0"));
            TestRunner.Add("M2eICMath.PackingBudget disabled = unlimited", () =>
                TestRunner.AssertEqual(int.MaxValue, M2eICMath.PackingBudget(0, 5), "cap<=0 sentinel"));
            TestRunner.Add("M2eICMath.PackingBudget rejects negative occupancy", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.PackingBudget(78, -1), "negative occupancy"));
            TestRunner.Add("M2eICMath.SplitOrderDrawsFromStorage flags Pa draw on split path", () =>
                TestRunner.AssertTrue(M2eICMath.SplitOrderDrawsFromStorage(true, 1), "violation"));
            TestRunner.Add("M2eICMath.SplitOrderDrawsFromStorage ok when no Pa units", () =>
                TestRunner.AssertTrue(!M2eICMath.SplitOrderDrawsFromStorage(true, 0), "clean split"));
            TestRunner.Add("M2eICMath.SplitOrderDrawsFromStorage fast path may draw Pa", () =>
                TestRunner.AssertTrue(!M2eICMath.SplitOrderDrawsFromStorage(false, 4), "whole order"));
            TestRunner.Add("M2eICMath.MultiPartExcess 0/1 part = 0", () =>
            {
                TestRunner.AssertEqual(0, M2eICMath.MultiPartExcess(0), "0 parts");
                TestRunner.AssertEqual(0, M2eICMath.MultiPartExcess(1), "1 part");
            });
            TestRunner.Add("M2eICMath.MultiPartExcess counts beyond first", () =>
                TestRunner.AssertEqual(3, M2eICMath.MultiPartExcess(4), "4 parts -> 3"));
            TestRunner.Add("M2eICMath.MultiPartExcess rejects negatives", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.MultiPartExcess(-1), "negative parts"));
            TestRunner.Add("M2eICMath.PipelineGateOpen no processing pod = open", () =>
                TestRunner.AssertTrue(M2eICMath.PipelineGateOpen(false, double.NaN, 70), "empty station must fetch"));
            TestRunner.Add("M2eICMath.PipelineGateOpen inside lead = open", () =>
                TestRunner.AssertTrue(M2eICMath.PipelineGateOpen(true, 69.9, 70), "releaseLeft <= lead"));
            TestRunner.Add("M2eICMath.PipelineGateOpen beyond lead = closed", () =>
                TestRunner.AssertTrue(!M2eICMath.PipelineGateOpen(true, 70.1, 70), "defer while busy"));
            TestRunner.Add("M2eICMath.PipelineGateOpen NaN with processing pod = closed (AE convention)", () =>
                TestRunner.AssertTrue(!M2eICMath.PipelineGateOpen(true, double.NaN, 70), "NaN treated as > lead"));
            TestRunner.Add("M2eICMath.PipelineGateOpen rejects negative lead", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.PipelineGateOpen(true, 1, -1), "negative lead"));
            TestRunner.Add("M2eICMath.SplitGateOpen strict: Pp and empty queue = open", () =>
                TestRunner.AssertTrue(M2eICMath.SplitGateOpen(true, 0, true), "bridge window"));
            TestRunner.Add("M2eICMath.SplitGateOpen strict: queued successor closes gate", () =>
                TestRunner.AssertTrue(!M2eICMath.SplitGateOpen(true, 1, true), "successor queued"));
            TestRunner.Add("M2eICMath.SplitGateOpen loose: queued successor stays open", () =>
                TestRunner.AssertTrue(M2eICMath.SplitGateOpen(true, 1, false), "loose mode"));
            TestRunner.Add("M2eICMath.SplitGateOpen no processing pod = closed in both modes", () =>
            {
                TestRunner.AssertTrue(!M2eICMath.SplitGateOpen(false, 0, true), "strict, no Pp");
                TestRunner.AssertTrue(!M2eICMath.SplitGateOpen(false, 0, false), "loose, no Pp");
            });
            TestRunner.Add("M2eICMath.SplitGateOpen rejects negative queue count", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.SplitGateOpen(true, -1, true), "negative queue"));
            TestRunner.Add("M2eICMath.PoolScarcity zero demand = 0", () =>
                TestRunner.AssertTrue(M2eICMath.PoolScarcity(0, 10) == 0.0, "no demand no scarcity"));
            TestRunner.Add("M2eICMath.PoolScarcity zero supply = 1", () =>
                TestRunner.AssertTrue(M2eICMath.PoolScarcity(5, 0) == 1.0, "missing supply maximally scarce"));
            TestRunner.Add("M2eICMath.PoolScarcity ratio and clamp", () =>
            {
                TestRunner.AssertTrue(Math.Abs(M2eICMath.PoolScarcity(5, 10) - 0.5) < 1e-12, "5/10");
                TestRunner.AssertTrue(M2eICMath.PoolScarcity(20, 10) == 1.0, "clamped at 1");
            });
            TestRunner.Add("M2eICMath.PoolScarcity rejects negative demand", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.PoolScarcity(-1, 10), "negative demand"));
        }
    }
}
