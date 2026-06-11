# M1G Station-Starve-Aware Cost — Implementation Plan (Phase 1: shared core + M1G)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make M1G prefer sending pods to output stations that are about to go idle, by converting its pod→station / bot→pod distance cost to travel time and adding a starvation delay penalty.

**Architecture:** A pure, side-effect-free helper `StarveAwareCost` (travel-time conversion + piecewise delay penalty + station EST lookup). M1G precomputes per-epoch EST(s) and a representative bot→pod time per pod, then folds the penalty into the `xps` coefficient as a constant — preserving the linear `w1/w2/w3` objective. Gated by a config flag (default off → baseline unchanged).

**Tech Stack:** C# (.NET Framework), RAWSimO.Core, Gurobi MILP via SolverWrappers, RAWSimO.Playground self-test harness (no xUnit). Build: VS18 MSBuild x64 Release on `RAWSimOWithSolverWrapping.sln`.

**Spec:** `docs/superpowers/specs/2026-06-06-m1g-hadgs-station-starve-aware-design.md`

**Scope note:** This plan covers the shared core + **M1G** only. HADGS is deferred: its pod scoring is demand-based (not distance-based; see spec discrepancy noted at end), so HADGS needs a separate design decision + plan.

---

## File Structure

- Create `RAWSimO.Core/Control/StarveAwareCost.cs` — pure helper: `TravelTime`, `DelayPenalty`, `Est`. One responsibility: starve-aware cost math. No mutation.
- Modify `RAWSimO.Core/Configurations/SettingConfiguration.cs` — 3 new gated config fields.
- Modify `RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs` — per-epoch prep + cost wrappers + objective wiring.
- Create `RAWSimO.Playground/Tests/StarveAwareCostSelfTest.cs` — red-green assertions for the pure functions.
- Modify `RAWSimO.Playground/Program.cs` — run the new self-test under `selftest`.

---

## Task 1: Pure helper `StarveAwareCost` (TravelTime + DelayPenalty)

**Files:**
- Create: `RAWSimO.Core/Control/StarveAwareCost.cs`
- Test: `RAWSimO.Playground/Tests/StarveAwareCostSelfTest.cs`
- Modify: `RAWSimO.Playground/Program.cs:25-29`

- [ ] **Step 1: Write the failing self-test**

Create `RAWSimO.Playground/Tests/StarveAwareCostSelfTest.cs`:
```csharp
using System;
using RAWSimO.Core.Control;

namespace RAWSimO.Playground.Tests
{
    /// <summary>Runnable red-green assertions for StarveAwareCost pure functions.</summary>
    public static class StarveAwareCostSelfTest
    {
        private static int _fails = 0;

        private static void Near(double actual, double expected, string name, double tol = 1e-6)
        {
            bool ok = Math.Abs(actual - expected) <= tol;
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + $"  (got {actual}, want {expected})");
            if (!ok) _fails++;
        }

        public static int RunAll()
        {
            _fails = 0;

            // TravelTime: distance / speed
            Near(StarveAwareCost.TravelTime(30.0, 1.5), 20.0, "TravelTime: 30m / 1.5 = 20s");
            // TravelTime: non-positive speed falls back to identity (distance unchanged)
            Near(StarveAwareCost.TravelTime(30.0, 0.0), 30.0, "TravelTime: speed<=0 -> identity");

            // DelayPenalty: delay >= 0 -> delay (pod late vs starve horizon)
            Near(StarveAwareCost.DelayPenalty(taCost: 50, est: 30, fixedParam: 5), 20.0, "DelayPenalty: late -> delay");
            // DelayPenalty: delay == 0 boundary -> delay (0)
            Near(StarveAwareCost.DelayPenalty(taCost: 30, est: 30, fixedParam: 5), 0.0, "DelayPenalty: boundary -> 0");
            // DelayPenalty: delay < 0 (pod in time) -> fixed floor
            Near(StarveAwareCost.DelayPenalty(taCost: 10, est: 30, fixedParam: 5), 5.0, "DelayPenalty: in-time -> fixedParam");

            Console.WriteLine(_fails == 0 ? "ALL PASS" : $"{_fails} FAILURES");
            return _fails == 0 ? 0 : 1;
        }
    }
}
```

Wire it in `RAWSimO.Playground/Program.cs` — replace the `selftest` block (lines 25-29):
```csharp
            if (args.Length == 1 && args[0] == "selftest")
            {
                int rc = RAWSimO.Playground.Tests.StationReleaseSchedulerSelfTest.RunAll();
                rc += RAWSimO.Playground.Tests.StarveAwareCostSelfTest.RunAll();
                Environment.Exit(rc);
                return;
            }
```

- [ ] **Step 2: Build to verify it fails (StarveAwareCost undefined)**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /t:Build /m /v:minimal /clp:ErrorsOnly`
Expected: FAIL — `The name 'StarveAwareCost' does not exist`.

- [ ] **Step 3: Write minimal implementation**

Create `RAWSimO.Core/Control/StarveAwareCost.cs`:
```csharp
using RAWSimO.Core.Elements;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// Pure, side-effect-free cost helpers for station-starve-aware order/pod/task allocation.
    /// Converts distance to travel time and computes the starvation delay penalty
    /// (delay = TA travel time - station EST). See
    /// docs/superpowers/specs/2026-06-06-m1g-hadgs-station-starve-aware-design.md.
    /// </summary>
    public static class StarveAwareCost
    {
        /// <summary>Distance [m] -> travel time [s]. nominalSpeed [m/s]; if &lt;= 0 returns
        /// the distance unchanged (identity fallback, lets callers degrade safely).</summary>
        public static double TravelTime(double distance, double nominalSpeed)
        {
            return nominalSpeed > 0.0 ? distance / nominalSpeed : distance;
        }

        /// <summary>Piecewise starvation delay penalty. delay = taCost - est.
        /// delay &gt;= 0 (pod cannot reach station before it goes idle) -> penalty = delay.
        /// delay &lt; 0 (pod can ideally arrive in time, ideal lower bound) -> penalty = fixedParam.</summary>
        public static double DelayPenalty(double taCost, double est, double fixedParam)
        {
            double delay = taCost - est;
            return delay >= 0.0 ? delay : fixedParam;
        }

        /// <summary>Station EST [s] — projected seconds until the station next goes idle,
        /// from the existing pipeline projection. Thin read-only wrapper.</summary>
        public static double Est(OutputStation station, double now)
        {
            return SlowStartController.ComputeStationWorkProjection(station, now).FirstStarveSec;
        }
    }
}
```

- [ ] **Step 4: Build + run self-test to verify it passes**

Run build (same command as Step 2), then:
`RAWSimO.Playground\bin\x64\Release\RAWSimO.Playground.exe selftest`
Expected: build EXIT=0; self-test output contains the 6 new `PASS` lines and `ALL PASS` for both suites (exit code 0).

- [ ] **Step 5: Commit**

```bash
git add RAWSimO.Core/Control/StarveAwareCost.cs RAWSimO.Playground/Tests/StarveAwareCostSelfTest.cs RAWSimO.Playground/Program.cs
git commit -m "Add StarveAwareCost pure helper + self-test"
```

---

## Task 2: Config flags

**Files:**
- Modify: `RAWSimO.Core/Configurations/SettingConfiguration.cs:162` (after `SlowStartUseReservationEta`)

- [ ] **Step 1: Add the gated fields**

Insert after line 162 (`public bool SlowStartUseReservationEta = false;`):
```csharp
        /// <summary>
        /// Station-starve-aware M1G cost (output stations). When true, M1G converts its
        /// pod->station / bot->pod distance cost to travel time and adds a starvation delay
        /// penalty so pods are steered to stations about to go idle. Preserves w1/w2/w3 and
        /// adds no MILP variables. Default off = baseline distance cost.
        /// See docs/superpowers/specs/2026-06-06-m1g-hadgs-station-starve-aware-design.md.
        /// </summary>
        public bool StarveAwareCostEnabled = false;
        /// <summary>Fixed floor penalty [s] applied when a pod can ideally arrive before the
        /// station starves (delay &lt; 0). Small positive constant; keeps the cost non-negative.</summary>
        public double StarveAwareFixedParam = 30.0;
        /// <summary>Nominal speed [m/s] for distance-&gt;travel-time conversion. If &lt;= 0, M1G
        /// derives it from the fleet's max bot velocity at solve time.</summary>
        public double StarveAwareNominalSpeed = 0.0;
```

- [ ] **Step 2: Build to verify it compiles**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /t:Build /m /v:minimal /clp:ErrorsOnly`
Expected: EXIT=0, no errors.

- [ ] **Step 3: Commit**

```bash
git add RAWSimO.Core/Configurations/SettingConfiguration.cs
git commit -m "Add StarveAware config flags (gated, default off)"
```

---

## Task 3: M1G per-epoch starve-aware prep + cost wrappers

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs` (add fields + methods near the existing estimators, ~line 151)

- [ ] **Step 1: Add cached fields and helper methods**

Insert immediately after `EstimatePodStationDistance` (after `M1GManager.cs:151`):
```csharp
        // ── Station-starve-aware cost (gated by SettingConfig.StarveAwareCostEnabled) ──
        private bool _saEnabled;
        private double _saNominalSpeed;
        private double _saFixedParam;
        private System.Collections.Generic.Dictionary<int, double> _saEstByStation;   // station.ID -> EST [s]
        private System.Collections.Generic.Dictionary<int, double> _saRepBotPodTime;  // pod.ID -> min bot->pod time [s]

        /// <summary>Precompute per-epoch starve-aware inputs: nominal speed, station EST,
        /// and the representative (min available-bot) bot->pod travel time per pod.
        /// No-op (and leaves cost wrappers in distance mode) when the feature is disabled.</summary>
        private void PrepareStarveAware(System.Collections.Generic.HashSet<Pod> pods,
            System.Collections.Generic.Dictionary<OutputStation, int> Cs,
            System.Collections.Generic.HashSet<Bot> Ra)
        {
            _saEnabled = Instance != null && Instance.SettingConfig != null && Instance.SettingConfig.StarveAwareCostEnabled;
            if (!_saEnabled)
                return;
            _saFixedParam = Instance.SettingConfig.StarveAwareFixedParam;
            double cfgSpeed = Instance.SettingConfig.StarveAwareNominalSpeed;
            _saNominalSpeed = cfgSpeed > 0.0
                ? cfgSpeed
                : (Instance.Bots != null && Instance.Bots.Count > 0
                    ? System.Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity))
                    : 1.0);
            double now = Instance.Controller.CurrentTime;
            _saEstByStation = new System.Collections.Generic.Dictionary<int, double>();
            foreach (var s in Cs.Keys)
                _saEstByStation[s.ID] = StarveAwareCost.Est(s, now);
            _saRepBotPodTime = new System.Collections.Generic.Dictionary<int, double>();
            foreach (var p in pods)
            {
                double best = double.PositiveInfinity;
                foreach (var r in Ra)
                    best = System.Math.Min(best, StarveAwareCost.TravelTime(EstimateBotPodDistance(r, p), _saNominalSpeed));
                _saRepBotPodTime[p.ID] = best;
            }
        }

        /// <summary>bot->pod objective coefficient: travel time when starve-aware, else distance.</summary>
        private double M1GBotPodCost(Bot robot, Pod pod)
        {
            double d = EstimateBotPodDistance(robot, pod);
            return _saEnabled ? StarveAwareCost.TravelTime(d, _saNominalSpeed) : d;
        }

        /// <summary>pod->station objective coefficient: travel time + starvation delay penalty
        /// when starve-aware, else distance.</summary>
        private double M1GPodStationCost(Pod pod, OutputStation station)
        {
            double d = EstimatePodStationDistance(pod, station);
            if (!_saEnabled)
                return d;
            double podStationTime = StarveAwareCost.TravelTime(d, _saNominalSpeed);
            double repBotPod = (_saRepBotPodTime.TryGetValue(pod.ID, out var t) && !double.IsPositiveInfinity(t)) ? t : 0.0;
            double taCost = podStationTime + repBotPod;
            double est = _saEstByStation.TryGetValue(station.ID, out var e) ? e : double.PositiveInfinity;
            double penalty = StarveAwareCost.DelayPenalty(taCost, est, _saFixedParam);
            return podStationTime + penalty;
        }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /t:Build /m /v:minimal /clp:ErrorsOnly`
Expected: EXIT=0 (methods compile; not yet called — a `_saEnabled` unused-warning is acceptable).

- [ ] **Step 3: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs
git commit -m "M1G: add starve-aware prep + cost wrappers (not yet wired)"
```

---

## Task 4: Wire starve-aware cost into the M1G objective

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs:572-580` (objective build) and the `solve` body to call `PrepareStarveAware`

- [ ] **Step 1: Call PrepareStarveAware before SetObjective**

In `solve(...)`, immediately before `if (Ra.Count() > 0)` at `M1GManager.cs:572`, insert:
```csharp
            PrepareStarveAware(Pods, Cs, Ra);
```
(`Pods`, `Cs`, `Ra` are the existing `solve` parameters.)

- [ ] **Step 2: Replace the cost calls in the objective**

In the objective expression (`M1GManager.cs:573-580`), replace both branches' cost calls:
- `EstimatePodStationDistance(v.pod, v.outputstation)` → `M1GPodStationCost(v.pod, v.outputstation)` (appears in both the `Ra.Count() > 0` branch and the `else` branch)
- `EstimateBotPodDistance(v.robot, v.pod)` → `M1GBotPodCost(v.robot, v.pod)` (in the `Ra.Count() > 0` branch)

After editing, the `Ra.Count() > 0` branch reads:
```csharp
                wrapper.SetObjective((LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * M1GPodStationCost(v.pod, v.outputstation)), wrapper)
                    + LinearExpression.Sum(deVarNameyrp.Where(u => Ra.Contains(u.robot) && Instance.ResourceManager.UnusedPods.Contains(u.pod) && u.pod.Waypoint != null).Select(v => variablesBinary[v.name] *
                    M1GBotPodCost(v.robot, v.pod)), wrapper)) * w1 + LinearExpression.Sum(deVarNameyos.Select(v => variablesBinary[v.name])) * w2
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesInteger3[v.name])) * w3, OptimizationSense.Minimize);
```
and the `else` branch's `xps` sum uses `M1GPodStationCost(v.pod, v.outputstation)`.

- [ ] **Step 3: Build to verify it compiles**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /t:Build /m /v:minimal /clp:ErrorsOnly`
Expected: EXIT=0, no errors.

- [ ] **Step 4: Regression smoke — feature OFF must equal baseline**

With `StarveAwareCostEnabled = false` (default), the cost wrappers return the original distances, so behaviour is unchanged. Run an existing small M1G config for a short horizon and confirm it runs without exception and produces a `statistics.txt`.

Run (adjust to an existing M1G instance/config in `Material/` or `Results/`):
`RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe <layout> <small M1G setting> <M1G controller> <out dir> 0`
Expected: completes, `statistics.txt` written, no exception.

- [ ] **Step 5: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs
git commit -m "M1G: wire starve-aware cost into objective (gated)"
```

---

## Task 5: Validation run (ON vs OFF)

**Files:** none (experiment + observation)

- [ ] **Step 1: Create two settings differing only in the flag**

Duplicate an existing small/large M1G setting config; in the treatment copy set `StarveAwareCostEnabled = true` and `StarveAwareNominalSpeed` to the real fleet speed (e.g. the bot `MaxVelocity` from the instance). Keep `StarveAwareFixedParam = 30.0` initially.

- [ ] **Step 2: Run both under a matched seed**

Run baseline (flag off) and treatment (flag on) to the standard horizon. Use the project-standard case: large bot=45 / small bot=20, M1G + WHCA*n-P.

- [ ] **Step 3: Compare the starvation KPI**

In each `statistics.txt`, read `StatStationStarvationTimeSec` / `StatOverallStationStarvationTimeSec`. Treatment should show lower total starvation; confirm throughput / station occupancy not degraded.
Expected: starvation total ↓ ON vs OFF; throughput not worse.

- [ ] **Step 4: Record results**

Write a short note (numbers + verdict) under `analysis/` or the project memory. If starvation does not drop, sweep `StarveAwareFixedParam` and `StarveAwareNominalSpeed` (note the double-count scale risk from spec §5).

---

## Self-Review

**Spec coverage:** §3.1 travel-time conversion → Task 1 (`TravelTime`) + Task 3 wrappers. §3.2 delay penalty → Task 1 (`DelayPenalty`). §3.3 M1G injection (precomputed constant on xps, separated structure) → Tasks 3–4. §3.4 gating → Task 2. §4 validation KPI → Task 5. §3.3 HADGS injection → **deferred** (see discrepancy below). §6 out-of-scope (no new vars, w1/w2/w3 intact) → respected in Task 4.

**Placeholder scan:** none — all code shown, exact file:line targets, exact build/run commands.

**Type consistency:** `StarveAwareCost.TravelTime/DelayPenalty/Est`, `M1GPodStationCost(Pod,OutputStation)`, `M1GBotPodCost(Bot,Pod)`, `PrepareStarveAware(HashSet<Pod>,Dictionary<OutputStation,int>,HashSet<Bot>)`, cache fields `_saEstByStation`/`_saRepBotPodTime` keyed by `.ID` — consistent across Tasks 1, 3, 4.

**Spec discrepancy found (HADGS):** spec §3.3 assumes HADGS scores pod candidates by distance/TA_cost. Code inspection shows HADGS pod scoring is **demand-based** (`HADGSManager.cs:428-439`: Demand / Completeable / WorkAmount); distance is used only to pick the nearest bot for an already-chosen pod (`HADGSManager.cs:599`). So the HADGS injection needs a different design (e.g. order station processing by EST, or add the delay penalty as an extra scorer), decided in a follow-up brainstorm + plan. M1G is unaffected.
