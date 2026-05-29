# Slow-Start at Pod Pickup — Implementation Plan

**Goal:** 實作 PP-aware slow-start hold，跑 baseline vs treatment 對比並回報 KPI

**Spec:** `docs/superpowers/specs/2026-05-20-slow-start-pod-release-design.md` (rev2)

---

## Tasks (sequential)

### T1: Add BotStateType.SlowStartHold
- Modify `RAWSimO.Core/Bots/BotStateType.cs`: append enum value

### T2: Add ExtractTask.ExpectedArrivalAtStation
- Modify `RAWSimO.Core/Control/BotTask.cs`: add `public double ExpectedArrivalAtStation { get; set; } = double.NaN;` to ExtractTask

### T3: Add SettingConfiguration.SlowStartEnabled
- Modify `RAWSimO.Core/Configurations/SettingConfiguration.cs`: add `public bool SlowStartEnabled = false;`

### T4: Add PathManager.EstimateReservationAwareEta
- Modify `RAWSimO.Core/Control/PathManager.cs`: add virtual method with Manhattan/v_avg fallback default impl
- Modify `RAWSimO.Core/Control/Defaults/PathPlanning/WHCAnStarPathManager.cs`: override with real SpaceTimeAStar dry-run

### T5: Create SlowStartController helper
- New file `RAWSimO.Core/Control/SlowStartController.cs`
- Method `ComputeHold(BotNormal, ExtractTask, double t_now)` → returns (delay, eta, t_starve, diag)

### T6: Add BotSlowStartHold state class
- Modify `RAWSimO.Core/Bots/BotNormal.cs`: nested class similar to BotRest
- `Act()`: on first entry call SlowStartController, set BlockedUntil, write diag stats

### T7: Wire BotSlowStartHold into ExtractTask state queue
- Modify `RAWSimO.Core/Bots/BotNormal.cs` line 667-674: enqueue BotSlowStartHold after PickupPod (both branches)
- Gate on `Instance.SettingConfig.SlowStartEnabled`

### T8: KPI pollution fix — exclude SlowStartHold from wait-gate
- Modify `RAWSimO.Core/Bots/BotNormal.cs` line 1331-1342: add `&& !_isSlowStartHolding` to gate

### T9: Add stat counters
- Modify `RAWSimO.Core/Elements/Bot.cs` (or Bot/BotNormal.cs near other Stat fields)
- Add: StatSlowStartHoldTimeSec, StatSlowStartHoldEnergyJ, StatSlowStartDecisionCount, StatSlowStartImmediateReleaseCount, StatSlowStartSearchFailures, StatSlowStartExpectedArrivalMissingCount

### T10: Build x64 Release

### T11: Locate / create sequ+small+N=20 config
- Search Material/Instances/...

### T12: Run baseline (SlowStartEnabled=false)
### T13: Run treatment (SlowStartEnabled=true)
### T14: Compare KPIs, report

---

## Out-of-scope deferrals (Phase 1 ships without these)
- `StatNeighborWaitImpactSec` —— 需要 spatial scan，加實作風險，Phase 1b 才補
- `StatStarvationMissCount` —— 需要 station event hook 觀測 idle，Phase 1b 才補
- `StatExpectedVsActualArrivalGapSec` —— 需在 PutItems init 比對 ExpectedArrival，Phase 1b 才補
- Phase 1b large/N=60 multi-seed —— 在 Phase 1a smoke 結果確認後執行
