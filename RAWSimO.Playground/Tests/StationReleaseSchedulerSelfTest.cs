using System;
using System.Collections.Generic;
using RAWSimO.Core.Control;

namespace RAWSimO.Playground.Tests
{
    /// <summary>Runnable red-green assertions for StationReleaseScheduler pure functions.</summary>
    public static class StationReleaseSchedulerSelfTest
    {
        private static int _fails = 0;

        private static void Check(bool cond, string name)
        {
            Console.WriteLine((cond ? "PASS  " : "FAIL  ") + name);
            if (!cond) _fails++;
        }

        private static void Near(double actual, double expected, string name, double tol = 1e-6)
        {
            bool ok = Math.Abs(actual - expected) <= tol;
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + $"  (got {actual}, want {expected})");
            if (!ok) _fails++;
        }

        /// <summary>Runs all checks; returns process exit code (0 = all pass).</summary>
        public static int RunAll()
        {
            _fails = 0;
            Test_SingleHolder();
            Test_TwoFeasible_HigherValueChosen();
            Test_NoneFeasible_FastestChosenReleaseNow();
            Test_CascadeCaseA();
            Test_CascadeCaseB();
            Test_ValueTieBreak();
            Test_Pipeline();
            Console.WriteLine(_fails == 0 ? "ALL PASS" : $"{_fails} FAILURES");
            return _fails == 0 ? 0 : 1;
        }

        private static void Test_SingleHolder()
        {
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 7, Lift = 2.2, Travel = 10, Value = 5, Proc = 30 }
            };
            var r = StationReleaseScheduler.Decide(starveTime: 50, holders: holders, buffer: 10);
            Check(r.ChosenBotId == 7, "SingleHolder: chosen is the only holder");
            Check(!r.ChosenReleaseNow, "SingleHolder: feasible -> not fire-fighting");
            Near(r.HoldDelayByBot[7], 50 - 2.2 - 10 - 10, "SingleHolder: hold delay = budget");
        }

        private static void Test_TwoFeasible_HigherValueChosen()
        {
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 1, Lift = 0, Travel = 10, Value = 3, Proc = 20 },
                new StationReleaseScheduler.HolderInput { BotId = 2, Lift = 0, Travel = 12, Value = 8, Proc = 40 }
            };
            var r = StationReleaseScheduler.Decide(starveTime: 60, holders: holders, buffer: 10);
            Check(r.ChosenBotId == 2, "TwoFeasible: higher-value (bot2) chosen");
            Near(r.HoldDelayByBot[2], 60 - 0 - 12 - 10, "TwoFeasible: chosen hold = budget");
            Near(r.HoldDelayByBot[1], 100 - 0 - 10 - 10, "TwoFeasible: other extended via Case B");
        }

        private static void Test_NoneFeasible_FastestChosenReleaseNow()
        {
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 1, Lift = 0, Travel = 30, Value = 9, Proc = 20 },
                new StationReleaseScheduler.HolderInput { BotId = 2, Lift = 0, Travel = 18, Value = 2, Proc = 25 }
            };
            var r = StationReleaseScheduler.Decide(starveTime: 15, holders: holders, buffer: 10);
            Check(r.ChosenBotId == 2, "NoneFeasible: fastest-arrival (bot2) chosen");
            Check(r.ChosenReleaseNow, "NoneFeasible: release now");
            Near(r.HoldDelayByBot[2], 0, "NoneFeasible: chosen hold delay = 0");
        }

        private static void Test_CascadeCaseA()
        {
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 1, Lift = 2, Travel = 40, Value = 9, Proc = 30 },
                new StationReleaseScheduler.HolderInput { BotId = 2, Lift = 2, Travel = 8, Value = 1, Proc = 20 }
            };
            var r = StationReleaseScheduler.Decide(starveTime: 50, holders: holders, buffer: 10);
            Check(r.ChosenBotId == 2, "CascadeA-pre: bot2 chosen");
            var holders2 = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 5, Lift = 2, Travel = 30, Value = 4, Proc = 25 },
                new StationReleaseScheduler.HolderInput { BotId = 6, Lift = 2, Travel = 35, Value = 9, Proc = 25 }
            };
            var r2 = StationReleaseScheduler.Decide(starveTime: 15, holders: holders2, buffer: 10);
            Check(r2.ChosenBotId == 5, "CascadeA: fastest (bot5) chosen");
            Near(r2.HoldDelayByBot[6], 57 - 2 - 35 - 10, "CascadeA: other extended via Case A");
        }

        private static void Test_CascadeCaseB()
        {
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 3, Lift = 0, Travel = 10, Value = 7, Proc = 50 },
                new StationReleaseScheduler.HolderInput { BotId = 4, Lift = 0, Travel = 11, Value = 1, Proc = 20 }
            };
            var r = StationReleaseScheduler.Decide(starveTime: 80, holders: holders, buffer: 10);
            Check(r.ChosenBotId == 3, "CascadeB: high-value (bot3) chosen");
            Near(r.HoldDelayByBot[4], 130 - 0 - 11 - 10, "CascadeB: other extended via Case B");
        }

        private static void Test_ValueTieBreak()
        {
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 9, Lift = 0, Travel = 15, Value = 5, Proc = 20 },
                new StationReleaseScheduler.HolderInput { BotId = 8, Lift = 0, Travel = 12, Value = 5, Proc = 20 }
            };
            var r = StationReleaseScheduler.Decide(starveTime: 60, holders: holders, buffer: 10);
            Check(r.ChosenBotId == 8, "ValueTieBreak: smaller arrival (bot8) chosen");
        }

        private static void Test_Pipeline()
        {
            var jobs = new List<(double arrival, double work)> { (0, 10), (30, 8) };
            double t = SlowStartController.PipelineNextFreeTime(5, jobs, 0);
            Near(t, 15, "Pipeline: gap returns earliest starvation");

            var jobs2 = new List<(double arrival, double work)> { (0, 10), (2, 5) };
            Near(SlowStartController.PipelineNextFreeTime(0, jobs2, 0), 15, "Pipeline: back-to-back");

            var jobs3 = new List<(double arrival, double work)> { (30, 8) };
            Near(SlowStartController.PipelineNextFreeTime(5, jobs3, 0), 5, "Pipeline: first late job starves at free time");
        }
    }
}
