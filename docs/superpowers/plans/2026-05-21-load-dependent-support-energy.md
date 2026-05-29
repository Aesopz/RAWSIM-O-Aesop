# Load-Dependent Support Energy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single `P_SUPPORT = 90 W` support-power constant with a load-dependent rate (20 W empty / 50 W loaded) applied to every bot every tick, keeping all energy accounting internally consistent.

**Architecture:** Single source of truth in `EnergyConsumption` (two static fields + a `SupportPower(Pod)` helper). All per-bot accounting in `BotNormal._updateStatistics` and the wait-energy properties switch from the constant to the helper / the matching loaded/empty constant. Fleet aggregation in `InstanceStatistics` is unchanged (it sums per-bot fields); only comments update. The deprecated planner mirror `EnergyModel` is updated to reflect the new values without touching `ESpaceTimeAStar` behavior.

**Tech Stack:** C# (.NET Framework 4.5.2), RAWSimO simulator. Build: VS18 MSBuild x64 Release (Gurobi is win64-only). No unit-test project exists; verification is build + smoke run + `statistics.txt` invariant checks.

---

## Background facts (read before starting)

- There is **no unit-test project**. Verification = successful x64 Release build + a short smoke simulation, then asserting invariants in the produced `statistics.txt`.
- `EnergyConsumption.Configure()` is **never called** in the live tree — all energy parameters use hardcoded defaults today. We keep that reality (Decision 4: no new xinst plumbing).
- Mechanical energies **E1–E5 are unchanged** — they already scale with `mTotal` via `GetTotalMass`/`GetLoadMass`. Do not touch them.
- "Loaded" ≡ `Pod != null` (Decision 3).
- Support power now accrues **every tick for every bot, no task gate** (Decision 2).
- `ESpaceTimeAStar` / `ECBSMethod` are deprecated dead code for WHCA*n-P experiments; do not change their behavior.

---

## Task 1: Single source of truth in EnergyConsumption

**Files:**
- Modify: `RAWSimO.Core/Metrics/EnergyConsumption.cs:46-52` (the `P_SUPPORT` block) and the `Configure(...)` signature at `:64-85`.

- [ ] **Step 1: Replace the P_SUPPORT constant with load-dependent fields + helper**

Replace this block (currently lines 46-52):

```csharp
        /// <summary>
        /// Fixed support power draw [W] — background electronics drain while a task is active.
        /// Zero when bot has no task assigned (standby = powered down for accounting).
        /// Must match EnergyModel.P_SUPPORT in MultiAgentPathFinding so that
        /// statistics-side support energy aligns with planner-side cost.
        /// </summary>
        public const double P_SUPPORT = 90;
```

with:

```csharp
        /// <summary>
        /// Support power draw [W] when the bot is NOT carrying a pod (Pod == null).
        /// Background electronics/standby drain. Accrued every tick for every bot
        /// (no task gate): idle, resting, moving, and waiting all count.
        /// </summary>
        public static double SUPPORT_POWER_EMPTY = 20.0;

        /// <summary>
        /// Support power draw [W] when the bot IS carrying a pod (Pod != null).
        /// Higher than empty because the lift actuator / heavier control load is engaged.
        /// </summary>
        public static double SUPPORT_POWER_LOADED = 50.0;

        /// <summary>
        /// Load-dependent support power [W] for the given carried pod.
        /// Single source of truth for all support-energy accounting.
        /// </summary>
        public static double SupportPower(Elements.Pod pod)
            => pod != null ? SUPPORT_POWER_LOADED : SUPPORT_POWER_EMPTY;
```

- [ ] **Step 2: Extend Configure() to optionally set the two support rates**

In `Configure(...)`, add two optional parameters and assignments (kept for future xinst wiring; defaults preserve 20/50). Change the signature:

```csharp
        public static void Configure(
            double robotMass,
            double robotWidth,
            double robotLength,
            double rollingFriction,
            double inertiaCoeff,
            double liftHeight,
            double podFrameMass,
            double supportPowerEmpty = 20.0,
            double supportPowerLoaded = 50.0)
        {
            ROBOT_MASS          = robotMass;
            ROBOT_WIDTH         = robotWidth;
            ROBOT_LENGTH        = robotLength;
            ROBOT_RADIUS        = robotWidth / 2.0;
            FRICTION            = rollingFriction;
            INERTIA             = inertiaCoeff;
            LIFT_HEIGHT         = liftHeight;
            POD_FRAME_MASS      = podFrameMass;
            SUPPORT_POWER_EMPTY  = supportPowerEmpty;
            SUPPORT_POWER_LOADED = supportPowerLoaded;

            // DEBUG: Log energy config to verify xlayo was loaded correctly
            System.Diagnostics.Debug.WriteLine(
                $"[EnergyConsumption.Configure] RobotMass={robotMass}, PodFrameMass={podFrameMass}, " +
                $"SupportEmpty={supportPowerEmpty}, SupportLoaded={supportPowerLoaded}");
        }
```

- [ ] **Step 3: Build to confirm the type compiles (callers updated in later tasks will still reference P_SUPPORT — expect build errors there, that's fine for now; build the Core project only)**

Run (PowerShell):
```
& "C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe" RAWSimO.Core\RAWSimO.Core.csproj /p:Configuration=Release /p:Platform=x64 /t:Rebuild
```
Expected: compile errors **only** of the form `'EnergyConsumption' does not contain a definition for 'P_SUPPORT'` at the BotNormal call sites. No errors inside `EnergyConsumption.cs` itself. (If your MSBuild path differs, use your VS18 MSBuild; CLAUDE.md mandates `/p:Platform=x64 /p:Configuration=Release`.)

- [ ] **Step 4: Commit**

```
git add RAWSimO.Core/Metrics/EnergyConsumption.cs
git commit -m "feat(energy): load-dependent support power (20 empty / 50 loaded) as single source"
```

---

## Task 2: Update BotNormal accounting to the new model

**Files:**
- Modify: `RAWSimO.Core/Bots/BotNormal.cs` — wait-energy properties (`:354-357`), comment (`:381`), slow-start hold (`:1377`), E_support gate removal (`:1380-1383`), E_wait split (`:1385-1394`), E_queueing (`:1402-1406`), and the standby comment (`:1352`).

- [ ] **Step 1: Update the wait-energy derived properties (lines 354-357)**

Replace:
```csharp
        /// <summary>Wait energy while loaded [J] = P_SUPPORT × StatWaitTimeLoadedSec (congestion cost only).</summary>
        public double StatWaitEnergyLoadedJ => Metrics.EnergyConsumption.P_SUPPORT * StatWaitTimeLoadedSec;
        /// <summary>Wait energy while empty [J] = P_SUPPORT × StatWaitTimeEmptySec (congestion cost only).</summary>
        public double StatWaitEnergyEmptyJ  => Metrics.EnergyConsumption.P_SUPPORT * StatWaitTimeEmptySec;
```
with:
```csharp
        /// <summary>Wait energy while loaded [J] = SUPPORT_POWER_LOADED × StatWaitTimeLoadedSec (congestion cost only).</summary>
        public double StatWaitEnergyLoadedJ => Metrics.EnergyConsumption.SUPPORT_POWER_LOADED * StatWaitTimeLoadedSec;
        /// <summary>Wait energy while empty [J] = SUPPORT_POWER_EMPTY × StatWaitTimeEmptySec (congestion cost only).</summary>
        public double StatWaitEnergyEmptyJ  => Metrics.EnergyConsumption.SUPPORT_POWER_EMPTY * StatWaitTimeEmptySec;
```

- [ ] **Step 2: Update the StatESupportJ doc comment (around line 381)**

Replace the line:
```csharp
        /// Formula: P_SUPPORT × task-active time (standby/None/Rest excluded).
```
with:
```csharp
        /// Formula: SupportPower(Pod) × wall-clock time, accrued every tick for every bot
        /// (no task gate; idle/None/Rest included). 20 W empty, 50 W loaded.
```

- [ ] **Step 3: Update the standby comment (around line 1352)**

Replace:
```csharp
            // No-task (None) = standby → P_SUPPORT = 0 (not accumulating background power).
```
with:
```csharp
            // Support power is now always-on and load-dependent (20 W empty / 50 W loaded);
            // it is NOT gated by task state. hasActiveTask/hasSupportTask still gate the
            // wait/queueing SUBSETS below, not the support total.
```

- [ ] **Step 4: Update slow-start hold energy (line 1377)**

Replace:
```csharp
                StatSlowStartHoldEnergyJ   += EnergyConsumption.P_SUPPORT * delta;
```
with:
```csharp
                StatSlowStartHoldEnergyJ   += EnergyConsumption.SupportPower(Pod) * delta;
```

- [ ] **Step 5: Remove the task gate on StatESupportJ (lines 1380-1383)**

Replace:
```csharp
            // E_support = P_SUPPORT × assigned-task time (background overhead;
            //             includes moving, rotating, waiting, and RestTask standby; excludes no-task standby).
            if (hasSupportTask)
                StatESupportJ += EnergyConsumption.P_SUPPORT * delta;
```
with:
```csharp
            // E_support = SupportPower(Pod) × wall-clock time — always-on background power,
            //             load-dependent (20 W empty / 50 W loaded). No task gate: idle/None/Rest
            //             all accrue. Wait/queueing/slow-start are strict subsets accumulated below.
            StatESupportJ += EnergyConsumption.SupportPower(Pod) * delta;
```

- [ ] **Step 6: Update E_wait split (lines 1385-1394)**

Replace:
```csharp
            // E_wait = P_SUPPORT × congestion-wait subset (task active AND stationary
            //          AND no mechanical action). Subset of E_support.
            if (hasActiveTask && !Moving && !_isRotatingThisTick && !inPickupOrSetdown && !inSlowStartHold)
            {
                StatEWaitJ += EnergyConsumption.P_SUPPORT * delta;
                if (Pod != null)
                    StatEWaitLoadedJ += EnergyConsumption.P_SUPPORT * delta;
                else
                    StatEWaitEmptyJ += EnergyConsumption.P_SUPPORT * delta;
            }
```
with:
```csharp
            // E_wait = SupportPower(Pod) × congestion-wait subset (task active AND stationary
            //          AND no mechanical action). Strict subset of E_support; loaded/empty split
            //          uses the matching rate so StatEWaitJ == StatEWaitLoadedJ + StatEWaitEmptyJ.
            if (hasActiveTask && !Moving && !_isRotatingThisTick && !inPickupOrSetdown && !inSlowStartHold)
            {
                StatEWaitJ += EnergyConsumption.SupportPower(Pod) * delta;
                if (Pod != null)
                    StatEWaitLoadedJ += EnergyConsumption.SUPPORT_POWER_LOADED * delta;
                else
                    StatEWaitEmptyJ += EnergyConsumption.SUPPORT_POWER_EMPTY * delta;
            }
```

- [ ] **Step 7: Update E_queueing-at-station (line 1405)**

Replace:
```csharp
                StatEQueueingAtStationJ      += EnergyConsumption.P_SUPPORT * delta;
```
with:
```csharp
                StatEQueueingAtStationJ      += EnergyConsumption.SupportPower(Pod) * delta;
```

- [ ] **Step 8: Build the Core project**

Run:
```
& "C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe" RAWSimO.Core\RAWSimO.Core.csproj /p:Configuration=Release /p:Platform=x64 /t:Rebuild
```
Expected: BUILD SUCCEEDED, no references to `P_SUPPORT` remaining in `RAWSimO.Core`.

- [ ] **Step 9: Verify no stray P_SUPPORT references remain in Core**

Run (PowerShell): `Select-String -Path RAWSimO.Core\**\*.cs -Pattern "P_SUPPORT" -SimpleMatch`
Expected: zero matches in `RAWSimO.Core` (only `SUPPORT_POWER_EMPTY`/`SUPPORT_POWER_LOADED`/`SupportPower` appear; those won't match `P_SUPPORT`). If any line still says `EnergyConsumption.P_SUPPORT`, fix it.

- [ ] **Step 10: Commit**

```
git add RAWSimO.Core/Bots/BotNormal.cs
git commit -m "feat(energy): apply load-dependent support power in BotNormal accounting, remove standby gate"
```

---

## Task 3: Update InstanceStatistics comments (no formula change)

**Files:**
- Modify: `RAWSimO.Core/InstanceStatistics.cs:1475` and `:1719-1720` (comments only).

- [ ] **Step 1: Update the E_support comment at line 1475**

Replace:
```csharp
            // E_support = background support energy (P_SUPPORT × active-task time; includes moving)
```
with:
```csharp
            // E_support = always-on background energy (SupportPower(Pod) × wall-clock time;
            //             20 W empty / 50 W loaded; includes idle/moving/waiting, no task gate)
```

- [ ] **Step 2: Update the composition comment at lines 1719-1720**

Replace:
```csharp
            //   wait    = P_SUPPORT × WaitTimeSec  (routing-induced congestion cost)
            //   support = E_support_total − wait  (P_SUPPORT during motion & station service, no standby)
```
with:
```csharp
            //   wait    = SupportPower(Pod) × WaitTimeSec  (routing-induced congestion cost)
            //   support = E_support_total − wait  (always-on background minus the wait subset; includes standby)
```

- [ ] **Step 3: Build the Core project**

Run:
```
& "C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe" RAWSimO.Core\RAWSimO.Core.csproj /p:Configuration=Release /p:Platform=x64 /t:Rebuild
```
Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```
git add RAWSimO.Core/InstanceStatistics.cs
git commit -m "docs(energy): update InstanceStatistics comments for always-on load-dependent support"
```

---

## Task 4: Sync deprecated planner mirror (no behavior change to ESpaceTimeAStar)

**Files:**
- Modify: `RAWSimO.MultiAgentPathFinding/Elements/EnergyModel.cs:19-44`.

- [ ] **Step 1: Replace the P_SUPPORT field block with the new model + compat alias**

Replace (lines 19-24):
```csharp
        /// <summary>
        /// Fixed support power draw [W] — background electronics drain while a task is active.
        /// E_wait = P_SUPPORT × waitDuration [J] (congestion/CBS hold).
        /// Independent of mass (control system / motor standby draw).
        /// </summary>
        public static double P_SUPPORT = 90;
```
with:
```csharp
        /// <summary>Support power draw [W] when empty (no pod). Mirror of EnergyConsumption.SUPPORT_POWER_EMPTY.</summary>
        public static double SUPPORT_POWER_EMPTY = 20.0;

        /// <summary>Support power draw [W] when carrying a pod. Mirror of EnergyConsumption.SUPPORT_POWER_LOADED.</summary>
        public static double SUPPORT_POWER_LOADED = 50.0;

        /// <summary>Load-dependent support power [W].</summary>
        public static double SupportPower(bool loaded) => loaded ? SUPPORT_POWER_LOADED : SUPPORT_POWER_EMPTY;

        /// <summary>
        /// DEPRECATED compat alias for the dead-code energy planner (ESpaceTimeAStar/ECBSMethod),
        /// which is NOT used by WHCA*n-P experiments. Set to the loaded rate so that file still
        /// compiles unchanged. Live accounting uses SupportPower(bool) / EnergyConsumption.SupportPower(Pod).
        /// </summary>
        public static double P_SUPPORT = SUPPORT_POWER_LOADED;
```

- [ ] **Step 2: Make ComputeWaitEnergy load-aware (line 43-44)**

Replace:
```csharp
        public static double ComputeWaitEnergy(double mTotal, double waitDuration)
            => P_SUPPORT * waitDuration;
```
with:
```csharp
        public static double ComputeWaitEnergy(double mTotal, double waitDuration)
            => SupportPower(mTotal > ROBOT_MASS + 1e-6) * waitDuration;
```

- [ ] **Step 3: Build the full solution (x64 Release) to confirm ESpaceTimeAStar still compiles unchanged**

Run:
```
& "C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe" RAWSimO.sln /p:Configuration=Release /p:Platform=x64 /t:Rebuild
```
Expected: BUILD SUCCEEDED for all projects (ESpaceTimeAStar compiles because `EnergyModel.P_SUPPORT` still exists as the alias).

- [ ] **Step 4: Commit**

```
git add RAWSimO.MultiAgentPathFinding/Elements/EnergyModel.cs
git commit -m "refactor(energy): mirror load-dependent support in deprecated EnergyModel, keep ESpaceTimeAStar compiling"
```

---

## Task 5: Smoke verification of invariants

**Files:**
- No code changes. Produces a `statistics.txt` to inspect.

- [ ] **Step 1: Run a short smoke simulation (small, bot=20, HADGS + WHCA*n-P)**

Use your standard CLI invocation. CLI argument order is: `Instance Setting ControlConfig StatisticsDir Seed`. Config = `Material/Instances/CoreBenchmark/hadgs_whcan_priority.xconf`, setting = a small (bot=20) `.xsett` (e.g. `Material/Instances/CoreBenchmark/small_sett_slowstart.xsett`), layout = `Material/Instances/CoreBenchmark/benchmark_ss_layout.xlayo`, output to `analysis/energy_smoke_2026-05-21/`, seed 0. (Mirror exactly how the `analysis/slowstart_hadgs_2026-05-20/treatment` run was launched.)

Expected: run completes and writes `statistics.txt`.

- [ ] **Step 2: Assert the energy invariants in statistics.txt**

Run (PowerShell), pointing at the produced file:
```
$f = "analysis/energy_smoke_2026-05-21/<run-dir>/statistics.txt"
$kv = @{}; Select-String -Path $f -Pattern '^(StatE\w+KJ):\s*([0-9.]+)' | ForEach-Object { $kv[$_.Matches[0].Groups[1].Value] = [double]$_.Matches[0].Groups[2].Value }
"EWait check: {0} vs L+E {1}" -f $kv['StatEWaitKJ'], ($kv['StatEWaitLoadedKJ'] + $kv['StatEWaitEmptyKJ'])
"Support >= Wait: {0}" -f ($kv['StatESupportKJ'] -ge $kv['StatEWaitKJ'])
```
Expected:
- `StatEWaitKJ` ≈ `StatEWaitLoadedKJ + StatEWaitEmptyKJ` (equal within floating-point rounding).
- `StatESupportKJ` ≥ `StatEWaitKJ` (support is the superset).
- `StatESupportKJ` is materially **larger** than a pre-change baseline at the same seed (idle/standby now counts), and noticeably **lower per loaded-second** than the old 90 W (loaded is now 50 W). Sanity only — no exact target.

- [ ] **Step 3: Confirm the L6 composition still sums to ~100%**

Run: `Select-String -Path $f -Pattern 'composition_(move|turn|lift|wait|support)_pct'`
Expected: the five percentages sum to ~100% (±0.1 rounding).

- [ ] **Step 4: Commit the smoke evidence (optional)**

```
git add analysis/energy_smoke_2026-05-21/
git commit -m "test(energy): smoke run confirming load-dependent support invariants"
```

---

## Done criteria

- Full solution builds x64 Release.
- No `EnergyConsumption.P_SUPPORT` references remain in `RAWSimO.Core`.
- Smoke `statistics.txt`: `EWaitLoaded + EWaitEmpty == EWait`, `ESupport ≥ EWait`, L6 composition sums to ~100%.
- E1–E5 mechanical energies unchanged (no edits to `ComputeSegmentEnergy`, `E4_Rotation`, `E5a_LiftPod`, `E5b_LowerPod`, `GetTotalMass`, `GetLoadMass`).
