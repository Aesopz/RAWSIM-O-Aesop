# Centralized Station Release Scheduler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the distributed-FIFO slow-start holder coordination with a centralized per-station scheduler that releases one pod at a time, chosen by feasibility-first then live pod↔station value, and extends the remaining holders' budgets via a single-server-queue cascade.

**Architecture:** A new `StationReleaseScheduler` runs once per station per PathManager tick. Its decision math is a pure static function `Decide(...)` over plain `HolderInput` structs (fully unit-testable). A thin `Schedule(...)` adapter gathers inputs from real `OutputStation`/`Pod`/`Bot` objects (ETA via `PathManager.EstimateIdealKinematicEta`, value via aggregated open-demand match, starve via the existing single-server pipeline minus the FIFO look-ahead) and writes a per-bot release deadline + diagnostics. `BotSlowStartHold.Act` becomes passive: it reads the deadline the scheduler wrote.

**Tech Stack:** C# (.NET Framework, RAWSimO.Core), MSBuild x64 Release (VS18), RAWSimO.Playground console for pure-function self-tests, RAWSimO.CLI for integration acceptance runs.

---

## File Structure

- **Create** `RAWSimO.Core/Control/StationReleaseScheduler.cs` — the scheduler: pure `Decide` core + `Schedule` adapter + `ComputePodStationValue`.
- **Modify** `RAWSimO.Core/Control/SlowStartController.cs` — extract a pure `PipelineNextFreeTime` helper; add `internal static double ComputeStationStarvation(...)` (all-holders-excluded); make `ComputeIdealEta` reachable; deprecate `ComputeHold`/FIFO.
- **Modify** `RAWSimO.Core/Bots/BotNormal.cs` — add release-decision fields; rewrite `BotSlowStartHold.Act` to read the scheduler's deadline.
- **Modify** `RAWSimO.Core/Control/PathManager.cs` — call `StationReleaseScheduler.Schedule` per output station each tick.
- **Modify** `RAWSimO.Playground/Program.cs` — add a `selftest` CLI arg that runs `StationReleaseSchedulerSelfTest.RunAll()` and returns exit code 0/1.
- **Create** `RAWSimO.Playground/Tests/StationReleaseSchedulerSelfTest.cs` — runnable red-green assertions over the pure functions.

Build command (used throughout):
```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Playground\RAWSimO.Playground.csproj" /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo
```
(Playground references Core, so this rebuilds Core too. For integration runs rebuild `RAWSimO.CLI\RAWSimO.CLI.csproj` the same way.)

Self-test run command (used throughout):
```
& "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Playground\bin\x64\Release\RAWSimO.Playground.exe" selftest
```

---

## Task 1: Pure decision core `Decide` + types

**Files:**
- Create: `RAWSimO.Core/Control/StationReleaseScheduler.cs`
- Create: `RAWSimO.Playground/Tests/StationReleaseSchedulerSelfTest.cs`
- Modify: `RAWSimO.Playground/Program.cs:23` (Main — add `selftest` arg branch)

- [ ] **Step 1: Write the scheduler types + `Decide` skeleton (no logic yet) so the self-test compiles and fails**

Create `RAWSimO.Core/Control/StationReleaseScheduler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// Centralized per-station release scheduler. Replaces the distributed-FIFO holder
    /// coordination in SlowStartController. One pod releases at a time, chosen by
    /// feasibility-first (can arrive before the station starves) then by live pod↔station
    /// value; the remaining holders extend their hold via a single-server-queue cascade.
    /// See docs/superpowers/specs/2026-05-26-station-release-scheduler-design.md.
    /// </summary>
    public static partial class StationReleaseScheduler
    {
        /// <summary>One holding bot's decision inputs (no engine objects — pure/testable).</summary>
        public struct HolderInput
        {
            public int BotId;
            public double Lift;    // PodTransferTime if not yet lifted, else 0 [s]
            public double Travel;  // ideal kinematic pod→station ETA, no conflicts [s]
            public double Value;   // live pod↔station demand match (selection score)
            public double Proc;    // committed pick work = Requests.Count × ItemTransferTime [s]
        }

        /// <summary>Scheduler output: which holder releases, and each holder's hold delay from now.</summary>
        public struct SchedulerResult
        {
            public int ChosenBotId;
            public bool ChosenReleaseNow;                 // true when fire-fighting (no feasible holder)
            public Dictionary<int, double> HoldDelayByBot; // botId → seconds to keep holding from now
        }

        /// <summary>
        /// Pure decision. starveTime = time-to-starvation excluding ALL holders.
        /// budget_h = starveTime − lift_h − travel_h − buffer; feasible_h = budget_h ≥ 0.
        /// chosen = argmax value among feasible; if none feasible, argmin (lift+travel) and release now.
        /// Cascade for others uses newStarve (single-server queue): see design §4.2.
        /// </summary>
        public static SchedulerResult Decide(double starveTime, IReadOnlyList<HolderInput> holders, double buffer)
        {
            throw new NotImplementedException();
        }
    }
}
```

Create `RAWSimO.Playground/Tests/StationReleaseSchedulerSelfTest.cs`:

```csharp
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
            Console.WriteLine(_fails == 0 ? "ALL PASS" : $"{_fails} FAILURES");
            return _fails == 0 ? 0 : 1;
        }

        // Placeholder bodies filled in later steps.
        private static void Test_SingleHolder() { }
        private static void Test_TwoFeasible_HigherValueChosen() { }
        private static void Test_NoneFeasible_FastestChosenReleaseNow() { }
        private static void Test_CascadeCaseA() { }
        private static void Test_CascadeCaseB() { }
        private static void Test_ValueTieBreak() { }
    }
}
```

Add the `selftest` branch at the very top of `Main` in `RAWSimO.Playground/Program.cs` (right after line 24 `static void Main(string[] args) {`):

```csharp
            if (args.Length == 1 && args[0] == "selftest")
            {
                Environment.Exit(RAWSimO.Playground.Tests.StationReleaseSchedulerSelfTest.RunAll());
                return;
            }
```

- [ ] **Step 2: Fill in the six test bodies (the failing tests)**

Replace the six placeholder methods in `StationReleaseSchedulerSelfTest.cs`:

```csharp
        private static void Test_SingleHolder()
        {
            // 1 holder: it is chosen; HoldDelay = max(0, starve − lift − travel − buffer).
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 7, Lift = 2.2, Travel = 10, Value = 5, Proc = 30 }
            };
            var r = StationReleaseScheduler.Decide(starveTime: 50, holders: holders, buffer: 10);
            Check(r.ChosenBotId == 7, "SingleHolder: chosen is the only holder");
            Check(!r.ChosenReleaseNow, "SingleHolder: feasible → not fire-fighting");
            Near(r.HoldDelayByBot[7], 50 - 2.2 - 10 - 10, "SingleHolder: hold delay = budget");
        }

        private static void Test_TwoFeasible_HigherValueChosen()
        {
            // Both feasible (budget≥0). Higher value chosen. Other extends via cascade Case B.
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 1, Lift = 0, Travel = 10, Value = 3, Proc = 20 },
                new StationReleaseScheduler.HolderInput { BotId = 2, Lift = 0, Travel = 12, Value = 8, Proc = 40 }
            };
            var r = StationReleaseScheduler.Decide(starveTime: 60, holders: holders, buffer: 10);
            Check(r.ChosenBotId == 2, "TwoFeasible: higher-value (bot2) chosen");
            Near(r.HoldDelayByBot[2], 60 - 0 - 12 - 10, "TwoFeasible: chosen hold = budget");
            // chosen travel_full = 0+12 = 12 ≤ starve 60 → Case B: newStarve = 60 + 40 = 100.
            Near(r.HoldDelayByBot[1], 100 - 0 - 10 - 10, "TwoFeasible: other extended via Case B");
        }

        private static void Test_NoneFeasible_FastestChosenReleaseNow()
        {
            // Both budgets < 0 (starve too small). Fire-fighting: argmin(lift+travel) chosen, release now.
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
            // chosen travel_full > starve → Case A: newStarve = travel_full + proc.
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 1, Lift = 2, Travel = 40, Value = 9, Proc = 30 }, // budget 50-2-40-10=-2 → infeasible
                new StationReleaseScheduler.HolderInput { BotId = 2, Lift = 2, Travel = 8,  Value = 1, Proc = 20 }  // budget 50-2-8-10=30 → feasible
            };
            var r = StationReleaseScheduler.Decide(starveTime: 50, holders: holders, buffer: 10);
            // Only bot2 feasible → chosen=2. travel_full=2+8=10 ≤ 50 → Case B actually. Force Case A below instead.
            Check(r.ChosenBotId == 2, "CascadeA-pre: bot2 chosen");
            // Dedicated Case A: single feasible chosen whose travel_full > starve is impossible (feasible ⇒ travel_full+buffer≤starve).
            // Case A only arises for a FIRE-FIGHTING chosen. Construct that:
            var holders2 = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 5, Lift = 2, Travel = 30, Value = 4, Proc = 25 }, // travel_full=32
                new StationReleaseScheduler.HolderInput { BotId = 6, Lift = 2, Travel = 35, Value = 9, Proc = 25 }
            };
            var r2 = StationReleaseScheduler.Decide(starveTime: 15, holders: holders2, buffer: 10); // none feasible
            Check(r2.ChosenBotId == 5, "CascadeA: fastest (bot5) chosen");
            // chosen travel_full = 32 > starve 15 → Case A: newStarve = 32 + 25 = 57.
            Near(r2.HoldDelayByBot[6], 57 - 2 - 35 - 10, "CascadeA: other extended via Case A");
        }

        private static void Test_CascadeCaseB()
        {
            // chosen travel_full ≤ starve → Case B: newStarve = starve + proc.
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 3, Lift = 0, Travel = 10, Value = 7, Proc = 50 }, // chosen (feasible, high value)
                new StationReleaseScheduler.HolderInput { BotId = 4, Lift = 0, Travel = 11, Value = 1, Proc = 20 }
            };
            var r = StationReleaseScheduler.Decide(starveTime: 80, holders: holders, buffer: 10);
            Check(r.ChosenBotId == 3, "CascadeB: high-value (bot3) chosen");
            // travel_full=10 ≤ 80 → Case B: newStarve = 80 + 50 = 130.
            Near(r.HoldDelayByBot[4], 130 - 0 - 11 - 10, "CascadeB: other extended via Case B");
        }

        private static void Test_ValueTieBreak()
        {
            // Equal value, both feasible → tie-break: smaller (lift+travel), then smaller BotId.
            var holders = new List<StationReleaseScheduler.HolderInput> {
                new StationReleaseScheduler.HolderInput { BotId = 9, Lift = 0, Travel = 15, Value = 5, Proc = 20 },
                new StationReleaseScheduler.HolderInput { BotId = 8, Lift = 0, Travel = 12, Value = 5, Proc = 20 }
            };
            var r = StationReleaseScheduler.Decide(starveTime: 60, holders: holders, buffer: 10);
            Check(r.ChosenBotId == 8, "ValueTieBreak: smaller arrival (bot8) chosen");
        }
```

- [ ] **Step 3: Build and run self-test to verify it FAILS**

Run the build command, then:
```
& "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Playground\bin\x64\Release\RAWSimO.Playground.exe" selftest
```
Expected: process prints `FAIL ...` lines / throws `NotImplementedException`; exit code 1.

- [ ] **Step 4: Implement `Decide`**

Replace the `Decide` body in `StationReleaseScheduler.cs`:

```csharp
        public static SchedulerResult Decide(double starveTime, IReadOnlyList<HolderInput> holders, double buffer)
        {
            var result = new SchedulerResult { HoldDelayByBot = new Dictionary<int, double>() };
            if (holders == null || holders.Count == 0)
            {
                result.ChosenBotId = -1;
                return result;
            }

            // Budget + feasibility per holder.
            double Budget(HolderInput h) => starveTime - h.Lift - h.Travel - buffer;
            double Arrival(HolderInput h) => h.Lift + h.Travel;

            var feasible = holders.Where(h => Budget(h) >= 0.0).ToList();

            HolderInput chosen;
            bool releaseNow;
            if (feasible.Count > 0)
            {
                // Max value; tie → smaller arrival; tie → smaller BotId.
                chosen = feasible
                    .OrderByDescending(h => h.Value)
                    .ThenBy(h => Arrival(h))
                    .ThenBy(h => h.BotId)
                    .First();
                releaseNow = false;
            }
            else
            {
                // Fire-fighting: fastest arrival; tie → smaller BotId. Release immediately.
                chosen = holders
                    .OrderBy(h => Arrival(h))
                    .ThenBy(h => h.BotId)
                    .First();
                releaseNow = true;
            }

            result.ChosenBotId = chosen.BotId;
            result.ChosenReleaseNow = releaseNow;
            result.HoldDelayByBot[chosen.BotId] = releaseNow ? 0.0 : Math.Max(0.0, Budget(chosen));

            // Cascade: station's next free time (absolute, from now) after chosen is accounted.
            double chosenArrivalFull = Arrival(chosen);
            double newStarve = (chosenArrivalFull > starveTime)
                ? chosenArrivalFull + chosen.Proc   // Case A: idle gap before chosen arrives
                : starveTime + chosen.Proc;          // Case B: chosen queues behind existing work

            foreach (var h in holders)
            {
                if (h.BotId == chosen.BotId) continue;
                result.HoldDelayByBot[h.BotId] = Math.Max(0.0, newStarve - h.Lift - h.Travel - buffer);
            }
            return result;
        }
```

- [ ] **Step 5: Build and run self-test to verify it PASSES**

Run the build command, then the selftest command.
Expected: all six tests print `PASS`, final line `ALL PASS`, exit code 0.

- [ ] **Step 6: Commit**

```bash
git add RAWSimO.Core/Control/StationReleaseScheduler.cs RAWSimO.Playground/Tests/StationReleaseSchedulerSelfTest.cs RAWSimO.Playground/Program.cs
git commit -m "feat: pure Decide core for centralized station release scheduler"
```

---

## Task 2: Pure single-server pipeline helper `PipelineNextFreeTime`

The starve-time computation sequences jobs (arrival, work) through a single server. Extract that arithmetic as a pure, tested helper so `ComputeStationStarvation` (Task 3) just builds the job list.

**Files:**
- Modify: `RAWSimO.Core/Control/SlowStartController.cs` (add helper near `ComputeStarvation`, line ~285)
- Modify: `RAWSimO.Playground/Tests/StationReleaseSchedulerSelfTest.cs` (add 2 checks)

- [ ] **Step 1: Add a failing self-test for the pipeline**

In `StationReleaseSchedulerSelfTest.RunAll()`, add `Test_Pipeline();` before the summary line. Add the method:

```csharp
        private static void Test_Pipeline()
        {
            // Two jobs, server free at t=5. Job A arrives 0 work 10; Job B arrives 30 work 8.
            // serve A: max(5,0)=5 → end 15. serve B: max(15,30)=30 → end 38. now=0 → 38.
            var jobs = new List<(double arrival, double work)> { (0, 10), (30, 8) };
            double t = SlowStartController.PipelineNextFreeTime(5, jobs, 0);
            Near(t, 38, "Pipeline: gap then late job");

            // Back-to-back: server free 0, A arrive 0 work 10, B arrive 2 work 5 → 0→10→15. now=0 →15.
            var jobs2 = new List<(double arrival, double work)> { (0, 10), (2, 5) };
            Near(SlowStartController.PipelineNextFreeTime(0, jobs2, 0), 15, "Pipeline: back-to-back");
        }
```

- [ ] **Step 2: Build and run self-test to verify the new test FAILS**

Build command, then selftest command.
Expected: compile error (`PipelineNextFreeTime` does not exist) — that is the red state for this step.

- [ ] **Step 3: Implement `PipelineNextFreeTime`**

In `RAWSimO.Core/Control/SlowStartController.cs`, add this `internal static` method (e.g. just below `ComputeStarvation`, before `ComputeQueueBudget`):

```csharp
        /// <summary>
        /// Pure single-server pipeline. Jobs are (arrivalTime, workTime). Server is free at
        /// stationFreeAt. Each job starts at max(serverFree, arrival) and ends at start+work.
        /// Returns the absolute time the server next becomes free, measured from `now`'s frame
        /// (i.e. returns the absolute timeline value, NOT relative; caller subtracts `now`).
        /// </summary>
        internal static double PipelineNextFreeTime(
            double stationFreeAt, IEnumerable<(double arrival, double work)> jobs, double now)
        {
            var ordered = jobs.OrderBy(j => j.arrival).ToList();
            double t0 = stationFreeAt;
            foreach (var job in ordered)
                t0 = Math.Max(t0, job.arrival) + job.work;
            return t0;
        }
```

- [ ] **Step 4: Build and run self-test to verify ALL PASS**

Build command, then selftest command.
Expected: `Pipeline: gap then late job` and `Pipeline: back-to-back` print PASS; final `ALL PASS`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add RAWSimO.Core/Control/SlowStartController.cs RAWSimO.Playground/Tests/StationReleaseSchedulerSelfTest.cs
git commit -m "feat: pure single-server pipeline helper for starve-time"
```

---

## Task 3: `ComputeStationStarvation` (all-holders-excluded) + `ComputePodStationValue`

**Files:**
- Modify: `RAWSimO.Core/Control/SlowStartController.cs` (add `ComputeStationStarvation`; line ~187 region)
- Create (append): `RAWSimO.Core/Control/StationReleaseScheduler.cs` (add `ComputePodStationValue`)

- [ ] **Step 1: Add `ComputeStationStarvation` to SlowStartController**

This mirrors the existing `ComputeStarvation` section (a) (non-holding committed tasks) but takes no `self`, excludes ALL holders, and reuses `PipelineNextFreeTime`. Add as `internal static`:

```csharp
        /// <summary>
        /// Time-to-starvation for a station, EXCLUDING every slow-start holder (the scheduler
        /// accounts for holders separately). Counts the in-progress pick plus all committed,
        /// non-holding ExtractTasks (processing / queueing / released-inbound) as single-server
        /// jobs, then returns seconds-from-now until the station would run dry.
        /// </summary>
        internal static double ComputeStationStarvation(OutputStation station, double currentTime)
        {
            if (station == null) return 0.0;

            double blockedUntilAbs = station.GetBlockedUntilTime();
            bool stationActive = !double.IsNaN(blockedUntilAbs) && blockedUntilAbs > currentTime;
            double stationFreeAt = stationActive ? blockedUntilAbs : currentTime;

            var jobs = new List<(double arrival, double work)>();
            foreach (var t in station.GetActiveExtractTasks())
            {
                if (t == null || t.Requests == null || t.Requests.Count == 0) continue;
                var other = t.Bot as BotNormal;
                if (other == null) continue;
                if (other._isSlowStartHolding) continue;   // ALL holders excluded

                int itemsRemaining = t.Requests.Count;
                bool atStation = other.CurrentWaypoint == station.Waypoint;
                if (atStation && stationActive)
                    itemsRemaining = Math.Max(0, itemsRemaining - 1);
                if (itemsRemaining == 0) continue;
                double work = itemsRemaining * station.ItemTransferTime;

                double arrival;
                if (atStation || other.IsQueueing)
                    arrival = currentTime;
                else if (!double.IsNaN(t.ExpectedArrivalAtStation) && t.ExpectedArrivalAtStation > currentTime)
                    arrival = t.ExpectedArrivalAtStation;
                else
                    arrival = currentTime;
                jobs.Add((arrival, work));
            }

            double t0 = PipelineNextFreeTime(stationFreeAt, jobs, currentTime);
            return Math.Max(0.0, t0 - currentTime);
        }
```

- [ ] **Step 2: Add `ComputePodStationValue` to StationReleaseScheduler**

Append inside the `StationReleaseScheduler` class in `RAWSimO.Core/Control/StationReleaseScheduler.cs`. Add `using RAWSimO.Core.Elements;`, `using RAWSimO.Core.Bots;`, `using RAWSimO.Core.Items;` at the top of the file.

```csharp
        /// <summary>
        /// Live pod↔station value = Σ_item min(pod.CountAvailable(item), station open demand[item]),
        /// matching SEQU's PodMatchingOrderManager scorer (Math.Min(pod stock, line demand)) but
        /// aggregated over ALL the station's assigned-and-unserved order positions. Falls back to
        /// the committed pick count when the station has no open demand recorded.
        /// </summary>
        public static double ComputePodStationValue(
            RAWSimO.Core.Elements.Pod pod, RAWSimO.Core.Elements.OutputStation station, int committedFallback)
        {
            if (pod == null || station == null) return committedFallback;

            var demand = new Dictionary<RAWSimO.Core.Items.ItemDescription, int>();
            foreach (var order in station.AssignedOrders)
            {
                foreach (var line in order.Positions)
                {
                    int open = order.PositionOverallCount(line.Key) - order.PositionServedCount(line.Key);
                    if (open <= 0) continue;
                    if (demand.ContainsKey(line.Key)) demand[line.Key] += open;
                    else demand[line.Key] = open;
                }
            }
            if (demand.Count == 0) return committedFallback;

            double value = 0.0;
            foreach (var kv in demand)
                value += Math.Min(pod.CountAvailable(kv.Key), kv.Value);
            return value;
        }
```

- [ ] **Step 3: Build to verify it compiles**

Run the build command.
Expected: `RAWSimO.Core -> ...` and `RAWSimO.Playground -> ...`, no errors.

- [ ] **Step 4: Run self-test (regression — must still ALL PASS)**

Selftest command. Expected: `ALL PASS`, exit 0 (no behavior change to pure functions).

- [ ] **Step 5: Commit**

```bash
git add RAWSimO.Core/Control/SlowStartController.cs RAWSimO.Core/Control/StationReleaseScheduler.cs
git commit -m "feat: station starvation (holders excluded) + live pod-station value"
```

---

## Task 4: BotNormal release-decision fields + passive `BotSlowStartHold.Act`

**Files:**
- Modify: `RAWSimO.Core/Bots/BotNormal.cs:462` (add fields near `_slowStartHoldStartTime`)
- Modify: `RAWSimO.Core/Bots/BotNormal.cs:2452-2541` (`BotSlowStartHold.Act`)

- [ ] **Step 1: Add release-decision fields**

In `BotNormal.cs`, immediately after `internal double _slowStartHoldStartTime = double.NaN;` (line 462) add:

```csharp
        /// <summary>Absolute time at which the central scheduler permits this holder to release.
        /// NaN until the scheduler has run at least once for this bot's station this hold.</summary>
        internal double _slowStartReleaseDeadline = double.NaN;
        /// <summary>True iff this bot is the scheduler's currently-chosen pod (diagnostic).</summary>
        internal bool _slowStartIsChosen = false;
        /// <summary>Scheduler-provided ETA (pod→station ideal) cached for the release-time arrival estimate.</summary>
        internal double _slowStartEta = double.NaN;
        /// <summary>Scheduler-provided T_starve snapshot for telemetry.</summary>
        internal double _slowStartTStarve = double.NaN;
```

- [ ] **Step 2: Rewrite `BotSlowStartHold.Act` to read the scheduler's decision**

Replace the body from the `// ── Dynamic per-tick recompute ──` block through the end of the `if (!_holdFinished)` block (lines ~2487-2541) with:

```csharp
                // ── Read centralized scheduler decision ──
                // StationReleaseScheduler (run each tick in PathManager.Update) writes
                // bot._slowStartReleaseDeadline. We hold until that deadline, then release.
                // First tick before the scheduler has run: deadline is NaN → keep probing.
                if (!_holdFinished)
                {
                    if (_trace == null)
                    {
                        bot.StatSlowStartDecisionCount++;
                        _trace = bot.Instance.NotifySlowStartDecision(
                            bot, _task, BuildDiag(bot), currentTime,
                            double.IsNaN(bot._slowStartReleaseDeadline) ? currentTime : bot._slowStartReleaseDeadline);
                    }

                    double deadline = bot._slowStartReleaseDeadline;
                    bool release = !double.IsNaN(deadline) && currentTime >= deadline;

                    if (release)
                    {
                        if (_task != null && !double.IsNaN(bot._slowStartEta) && !double.IsInfinity(bot._slowStartEta))
                        {
                            double liftTime = (bot.Pod == null) ? bot.PodTransferTime : 0.0;
                            _task.ExpectedArrivalAtStation = currentTime + liftTime + bot._slowStartEta;
                        }
                        bool everHeld = (currentTime - _holdStartTime) > 0.0;
                        if (!everHeld) bot.StatSlowStartImmediateReleaseCount++;
                        bot.Instance.NotifySlowStartRelease(
                            _trace, currentTime,
                            everHeld ? "hard_deadline" : "immediate_release",
                            bot._slowStartEta, bot._slowStartTStarve);
                        _holdFinished = true;
                    }
                    else
                    {
                        bot.BlockedUntil = double.IsNaN(deadline) ? currentTime + PROBE_INTERVAL : deadline;
                        bot.WaitUntil(currentTime + PROBE_INTERVAL);
                    }
                }
```

Add this private helper inside the `BotSlowStartHold` class (so the telemetry struct is still populated):

```csharp
            private SlowStartController.HoldDiagnostics BuildDiag(BotNormal bot)
            {
                return new SlowStartController.HoldDiagnostics
                {
                    Eta = bot._slowStartEta,
                    TStarve = bot._slowStartTStarve,
                    ReleaseBudget = bot._slowStartTStarve,
                    Delay = double.IsNaN(bot._slowStartReleaseDeadline) ? 0.0
                            : Math.Max(0.0, bot._slowStartReleaseDeadline - bot.Instance.Controller.CurrentTime),
                    EtaProbeFailed = double.IsNaN(bot._slowStartEta),
                    ImmediateRelease = bot._slowStartIsChosen && bot._slowStartReleaseDeadline <= bot.Instance.Controller.CurrentTime
                };
            }
```

- [ ] **Step 3: Clear the new fields in the cleanup block**

In the `if (_holdFinished)` cleanup block (line ~2545, where `_isSlowStartHolding=false` etc. are set), add:

```csharp
                    bot._slowStartReleaseDeadline = double.NaN;
                    bot._slowStartIsChosen = false;
                    bot._slowStartEta = double.NaN;
                    bot._slowStartTStarve = double.NaN;
```

- [ ] **Step 4: Build to verify it compiles**

Run the build command (Playground build pulls Core).
Expected: no errors. (`ComputeHold` may now be unused — that is removed in Task 6; a CS0169/unused warning is acceptable here.)

- [ ] **Step 5: Commit**

```bash
git add RAWSimO.Core/Bots/BotNormal.cs
git commit -m "feat: BotSlowStartHold reads centralized scheduler release deadline"
```

---

## Task 5: `Schedule` adapter — gather inputs, call `Decide`, write to bots

**Files:**
- Modify: `RAWSimO.Core/Control/StationReleaseScheduler.cs` (add `Schedule`)

- [ ] **Step 1: Implement `Schedule`**

Append to `StationReleaseScheduler` (needs `using RAWSimO.Core.Bots;`, `using RAWSimO.Core.Elements;`):

```csharp
        /// <summary>
        /// Per-station, per-tick entry point. Gathers all current holders for the station,
        /// builds HolderInput (ETA via pathManager, value via ComputePodStationValue, starve via
        /// SlowStartController.ComputeStationStarvation), runs Decide, and writes the release
        /// deadline + diagnostics onto each holder bot.
        /// </summary>
        public static void Schedule(
            RAWSimO.Core.Elements.OutputStation station, PathManager pathManager, double currentTime, double buffer)
        {
            if (station == null || pathManager == null) return;

            var instance = station.Instance;
            // Collect holders bound to THIS station.
            var holderBots = new List<BotNormal>();
            foreach (var b in instance.Bots)
            {
                var bn = b as BotNormal;
                if (bn == null || !bn._isSlowStartHolding) continue;
                var task = bn.CurrentTask as ExtractTask;
                if (task == null || task.OutputStation != station) continue;
                holderBots.Add(bn);
            }
            if (holderBots.Count == 0) return;

            double starve = SlowStartController.ComputeStationStarvation(station, currentTime);

            var inputs = new List<HolderInput>();
            var etaById = new Dictionary<int, double>();
            foreach (var bn in holderBots)
            {
                var task = bn.CurrentTask as ExtractTask;
                double eta = pathManager.EstimateIdealKinematicEta(
                    bn, bn.CurrentWaypoint, station.Waypoint, currentTime, bn.GetTargetOrientation());
                if (double.IsNaN(eta) || double.IsInfinity(eta))
                    eta = SlowStartController.ComputeIdealEta(bn, bn.CurrentWaypoint, station.Waypoint);
                if (double.IsNaN(eta) || double.IsInfinity(eta)) eta = 0.0;

                double lift = (bn.Pod == null) ? bn.PodTransferTime : 0.0;
                int committed = (task.Requests != null) ? task.Requests.Count : 0;
                double value = ComputePodStationValue(task.ReservedPod, station, committed);
                double proc = committed * station.ItemTransferTime;

                etaById[bn.ID] = eta;
                inputs.Add(new HolderInput { BotId = bn.ID, Lift = lift, Travel = eta, Value = value, Proc = proc });
            }

            var result = Decide(starve, inputs, buffer);

            foreach (var bn in holderBots)
            {
                double delay = result.HoldDelayByBot.TryGetValue(bn.ID, out var d) ? d : 0.0;
                bn._slowStartReleaseDeadline = currentTime + delay;
                bn._slowStartIsChosen = (bn.ID == result.ChosenBotId);
                bn._slowStartEta = etaById[bn.ID];
                bn._slowStartTStarve = starve;
            }
        }
```

- [ ] **Step 2: Confirm `ComputeIdealEta` is reachable**

`SlowStartController.ComputeIdealEta` is already `public static` (verified at line 116). No change needed. If a build error says it is inaccessible, change its modifier to `public static`.

- [ ] **Step 3: Build to verify it compiles**

Run the build command. Expected: no errors.

- [ ] **Step 4: Run self-test (regression)**

Selftest command. Expected: `ALL PASS`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add RAWSimO.Core/Control/StationReleaseScheduler.cs
git commit -m "feat: Schedule adapter wiring real station/pod/bot into Decide"
```

---

## Task 6: Wire `Schedule` into PathManager tick; retire FIFO

**Files:**
- Modify: `RAWSimO.Core/Control/PathManager.cs:522-526` (call Schedule each tick)
- Modify: `RAWSimO.Core/Control/SlowStartController.cs` (remove FIFO section (b) from `ComputeStarvation`; mark `ComputeHold` obsolete-but-kept or delete if unused)

- [ ] **Step 1: Call the scheduler each tick in PathManager.Update**

In `RAWSimO.Core/Control/PathManager.cs`, inside `Update`, right after the queue-manager loop (line 526, before the reservation-table reorg) insert:

```csharp
            // Centralized slow-start release scheduling (one decision per station per tick).
            if (Instance.SettingConfig != null && Instance.SettingConfig.SlowStartEnabled)
            {
                double buffer = Instance.SettingConfig.SlowStartEtaSafetyBuffer;
                if (buffer <= 0.0) buffer = 16.8;  // default; see SlowStartController history
                foreach (var os in Instance.OutputStations)
                    StationReleaseScheduler.Schedule(os, this, currentTime, buffer);
            }
```

- [ ] **Step 2: Remove the FIFO senior look-ahead from `ComputeStarvation`**

In `RAWSimO.Core/Control/SlowStartController.cs`, delete the entire `// (b) FIFO seniors ...` block (the `double selfHoldStart = self._slowStartHoldStartTime; if (...) { foreach (var b in self.Instance.Bots) {...} }` section, lines ~238-274). The non-holding section (a) plus `PipelineNextFreeTime` remains. `ComputeStarvation` is now only used (if at all) by the deprecated `ComputeHold`.

- [ ] **Step 3: Neutralize the now-unused `ComputeHold` path**

`BotSlowStartHold.Act` no longer calls `ComputeHold`. Leave `ComputeHold` in place but add an XML-doc note marking it deprecated (kept for reference/telemetry struct reuse), OR delete it if no other caller. Verify no remaining callers:

Run: `Grep pattern "ComputeHold\(" across RAWSimO.Core`
Expected: zero matches outside the definition. If zero, delete the `ComputeHold` method body to avoid dead code; otherwise keep.

- [ ] **Step 4: Build CLI for integration**

```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.CLI\RAWSimO.CLI.csproj" /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo
```
Expected: `RAWSimO.CLI -> ...RAWSimO.CLI.exe`, no errors.

- [ ] **Step 5: Run self-test (regression) + commit**

Selftest command → `ALL PASS`.
```bash
git add RAWSimO.Core/Control/PathManager.cs RAWSimO.Core/Control/SlowStartController.cs
git commit -m "feat: wire StationReleaseScheduler into PathManager; retire FIFO look-ahead"
```

---

## Task 7: Integration acceptance run + comparison

**Files:** none (verification only). Working dir: `Material/Instances/CoreBenchmark`.

- [ ] **Step 1: Confirm scenario inputs**

Verify `test.xlayo` is `BotCount=4`, `PodCapacity=100`. Verify `test.xsett` (ON) and `test_off.xsett` (OFF) are Mu-100 / 7200s. (Both confirmed during design; re-check with a quick read if unsure.)

- [ ] **Step 2: Run OFF, current-ON baseline, and new scheduler ON**

OFF and old-ON results already exist under `out_mu100_off/` and `out_mu100_on/` (10s buffer, pre-scheduler). Run the new scheduler build into a fresh dir:
```
$EXE="C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"
& $EXE test.xlayo test.xsett sequ_whcan_priority.xconf out_mu100_on_sched 1
```
Expected: `.Fin. - SUCCESS`.

- [ ] **Step 3: Compare against acceptance criteria (design §7)**

Parse `kpi_report.csv` + `stationstatistics.csv` (OutputStation0) for OFF, old-ON, new-ON. Acceptance:
- OutputStation IdleTime: new-ON ≤ old-ON (807s), target → OFF (728s).
- orders_completed: new-ON ≥ old-ON (219), target → OFF (225).
- energy_total_with_support_per_order_kJ: new-ON < OFF (11.03), not materially worse than old-ON (10.22).
- stop_and_go_count: new-ON not worse than old-ON (113); must not regress to the 16.8s-static result (151).
- `slowstart_decisions.csv`: release timestamps are spread (no simultaneous multi-holder release) and `chosen` rotates among bots.

Produce a 3-column comparison table (OFF / old-ON / new-ON) for the above.

- [ ] **Step 4: Record outcome**

Summarize whether the centralized scheduler reduced station IdleTime toward OFF without an energy/throughput/conflict regression. If IdleTime did NOT improve, the residual is the no-conflict-ETA vs real-congestion gap (design §8 risk) — note it for the next iteration rather than tuning the buffer.

- [ ] **Step 5: Commit any scenario/doc updates** (if files changed; otherwise skip)

---

## Self-Review Notes

- **Spec coverage:** §3 value/proc → Task 3 (`ComputePodStationValue`) + Task 5 (proc). §4 selection → Task 1 `Decide`. §4.1 timing → Task 1 (chosen delay) + Task 4 (Act reads deadline). §4.2 cascade A/B → Task 1 `Decide` + tests. §4.3 serialization → Task 6 (per-tick re-schedule; released chosen re-enters starve via cache). §5 edge cases → Task 1 (tie-break, fire-fighting), Task 3 (open-demand=0 fallback), Task 5 (ETA NaN fallback). §6 interfaces → Tasks 1/4/5/6. §7 acceptance → Task 7.
- **lift consistency (spec §4.2 note):** `Decide` uses `Arrival(h)=lift+travel` for both feasibility and cascade — matches the spec's deliberate refinement.
- **Type consistency:** `HolderInput{BotId,Lift,Travel,Value,Proc}`, `SchedulerResult{ChosenBotId,ChosenReleaseNow,HoldDelayByBot}`, `Decide`, `Schedule`, `ComputePodStationValue`, `ComputeStationStarvation`, `PipelineNextFreeTime` used identically across tasks.
- **Buffer default:** centralized in PathManager wiring (Task 6, 16.8 default) — single source; `Decide` itself takes buffer as a parameter (no hidden default).
