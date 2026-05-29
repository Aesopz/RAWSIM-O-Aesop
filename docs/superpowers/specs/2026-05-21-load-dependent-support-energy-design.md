# Load-Dependent Support Energy — Design

**Date:** 2026-05-21
**Status:** Approved (design), pending implementation plan
**Author:** Aesop + AI assistant

## Problem

The energy model uses a single constant `P_SUPPORT = 90 W` for the
"background support power" (electronics/standby drain) accrued while a bot
has a task. This does not distinguish whether the bot is carrying a pod.
We want support power to depend on payload state:

- **Empty** (`Pod == null`): **20 J/s** (20 W)
- **Loaded** (`Pod != null`): **50 J/s** (50 W)

The mechanical drive/turn/lift energies (E1–E5) already scale with mass and
are unchanged; only the support/background term changes.

## Decisions (from brainstorming)

1. **Scope of the 20/50 split** — it is the *support background power* and
   applies during the **entire** time a bot exists (moving + turning +
   waiting + station service). E1–E5 are computed from `mTotal` and added on
   top, exactly as today.
2. **No task gate** — the previous `standby = 0 W` rule is **removed**.
   Every bot accrues support power every tick: `20 W` empty, `50 W` loaded,
   regardless of task state (idle/None, Rest/return-to-park included).
3. **"Loaded" definition** — `Pod != null` (carrying a pod, regardless of
   cargo). Consistent with the existing `StatWaitTimeLoaded/Empty` split.
4. **Parameter unification** — single source of truth in code
   (`EnergyConsumption`). No new xinst plumbing this round (consistent with
   the current state where *all* energy params are hardcoded defaults and
   `Configure()` is never called). `Configure()` is extended to accept the
   two values for future wiring, but defaults remain 20/50.
5. **Deprecated planner mirror** — `EnergyModel` (used only by the
   deprecated `ESpaceTimeAStar`/`ECBSMethod`) is updated to reflect the new
   20/50 model so it does not contradict Core. `ESpaceTimeAStar` itself gets
   only the minimal mechanical edits needed to keep compiling; its behavior
   is not a concern (dead code for WHCA*n-P experiments).

## New model (precise)

```
SUPPORT_POWER_EMPTY  = 20.0   // W, Pod == null
SUPPORT_POWER_LOADED = 50.0   // W, Pod != null
SupportPower(pod)    = pod != null ? SUPPORT_POWER_LOADED : SUPPORT_POWER_EMPTY

support_energy_tick  = SupportPower(Pod) * delta     // every bot, every tick
total_energy         = E1+E2+E3+E4+E5 (mechanical, mass-based) + support_energy
```

## Touch points (Core live path)

### `RAWSimO.Core/Metrics/EnergyConsumption.cs`
- Remove `public const double P_SUPPORT = 90;`.
- Add:
  ```csharp
  public static double SUPPORT_POWER_EMPTY  = 20.0;
  public static double SUPPORT_POWER_LOADED = 50.0;
  public static double SupportPower(Elements.Pod pod)
      => pod != null ? SUPPORT_POWER_LOADED : SUPPORT_POWER_EMPTY;
  ```
- Extend `Configure(...)` to optionally set the two values (defaults 20/50).

### `RAWSimO.Core/Bots/BotNormal.cs`
- `:355` `StatWaitEnergyLoadedJ => SUPPORT_POWER_LOADED * StatWaitTimeLoadedSec`
- `:357` `StatWaitEnergyEmptyJ  => SUPPORT_POWER_EMPTY  * StatWaitTimeEmptySec`
- `:1377` slow-start hold: `SupportPower(Pod) * delta` (Pod != null during
  hold ⇒ 50).
- `:1382–1383` `StatESupportJ`: **remove the `hasSupportTask` gate**;
  accumulate `SupportPower(Pod) * delta` unconditionally each tick.
- `:1387–1393` E_wait: total uses `SupportPower(Pod)`; loaded branch uses
  `SUPPORT_POWER_LOADED`, empty branch uses `SUPPORT_POWER_EMPTY`.
- `:1405` E_queueing-at-station: `SupportPower(Pod) * delta`.
- Update the `:1352` / `:1380–1381` comments (standby no longer 0).

### `RAWSimO.Core/InstanceStatistics.cs`
- No aggregation-formula change (reads per-bot fields and sums).
- Update comments at `:1475` and `:1719–1720` ("no standby" no longer true;
  support now always-on, load-dependent).
- Verify the 5-component composition still balances (it does; see below).

### `RAWSimO.MultiAgentPathFinding/Elements/EnergyModel.cs` (deprecated mirror)
- Replace `P_SUPPORT = 90` with `SUPPORT_POWER_EMPTY = 20`,
  `SUPPORT_POWER_LOADED = 50`, and `SupportPower(bool loaded)`.
- `ComputeWaitEnergy(mTotal, dur)` derives load from
  `mTotal > ROBOT_MASS + eps`.
- `ESpaceTimeAStar.cs`: mechanical-only edits to keep compiling (replace
  `EnergyModel.P_SUPPORT` references with `SupportPower(...)` using the
  agent's known load). No behavioral tuning.

## Consistency guarantees (no discrepancy)

- **E_wait additivity:** `StatEWaitJ` accumulated with `SupportPower(Pod)`
  equals `StatEWaitLoadedJ (50·t_L) + StatEWaitEmptyJ (20·t_E)` because each
  branch accrues with the matching rate at the same instants.
- **Subset relations preserved:** at any instant all of
  `E_support`, `E_wait`, `E_queueing`, `E_slowstart` use the *same*
  `SupportPower(Pod)`. So `E_support ⊇ E_wait ⊇ {loaded, empty}`,
  `E_support ⊇ E_queueing`, `E_support ⊇ E_slowstart` still hold.
  (`E_support` is now a strict superset that additionally includes
  idle/standby time.)
- **Composition balance:** `StatEnergyTotalWithSupportJ =
  StatEnergyTotalJ + StatESupportJ` unchanged; the 5-component
  `move + turn + lift + wait + support` still sums to it, with
  `support = E_support_total − wait`.
- **Mechanical energies untouched:** E1–E5 keep using
  `GetTotalMass`/`GetLoadMass`; numbers do not move.

## Out of scope

- xinst `EnergyParameters` parsing/DTO plumbing.
- Any behavioral change to `ESpaceTimeAStar`/`ECBSMethod`.
- Re-tuning of mechanical constants (mass, friction, inertia, lift).

## Verification

- Build x64 Release (Gurobi requires win64).
- Smoke run (small, bot=20, hadgs + WHCA*n-P): confirm
  `StatESupportKJ`, `StatEWaitKJ`, `StatEWaitLoadedKJ`, `StatEWaitEmptyKJ`,
  `StatEQueueingAtStationKJ` are populated and that
  `EWaitLoaded + EWaitEmpty == EWait` and the L6 composition percentages
  sum to ~100%.
- Sanity: loaded support rate / empty support rate ≈ 50/20 = 2.5 in a
  controlled single-bot trace.
