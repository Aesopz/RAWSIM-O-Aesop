# PP-aware Slow-Start at Pod Pickup — Design Spec

**Date**: 2026-05-20 (rev 2 — post-review)
**Author**: Aesop (with Claude)
**Status**: Phase 1 design, awaiting implementation

---

## 1. Motivation

在 RAWSimO 既有觀察中，output station 是系統瓶頸。當 bot 載 pod 抵達 station 時，若 queue 已有 pod 排隊，bot 必須在 station 周邊停等，這段「過早抵達」並未帶來吞吐量增益，反而：

1. 在 station 周邊製造交通熱點，引發其他 bot 的 stop-and-go 與 turn
2. 消耗 P_SUPPORT × 停等時間 的能量
3. 佔據 station 入口 cell，干擾後續 dispatch

**核心假設**：若能在 bot 載 pod 起步前，把「預估的 station-queue 排隊時間」原地 hold 起來，理論上：
- throughput 不變（hold 時長 ≤ 原本要在 station 排隊的時間）
- station 不會餓著（hold 上限由 T_starve 嚴格控制）
- station-queue premature wait ↓
- path-conflict wait 不應增加（核心反事實）

## 2. Scope (Phase 1)

**目標重設（post-review）**：
> 把 station queue 的 premature wait 轉成 upstream controlled hold（在 pod cell），且**不降低 throughput、不增加 path-conflict wait**。

> 注意：「降低總能量」不是 Phase 1 的承諾，是觀察。Phase 1 必須能精確分離三種 wait 來證明轉換確實發生；能量改善是次要結論，受 hold-cell impact、layout 與 fleet size 影響很大。

**包含**：
- 在 `BotNormal` 的 ExtractTask（pod→station）流程中、bot 完成 pod lift 的瞬間，插入單次決策的 `BotSlowStartHold` state
- 決策公式封閉式、0 超參數
- T_starve 對齊真實 `OutputStation` 狀態
- ETA probe 公開 API 在 `PathManager` 層
- 三種 wait 分離記帳 + hold-cell impact 觀察 + 預測鏈診斷
- Phase 1a: small/N=20 smoke test；Phase 1b: large/N=60 統計顯著實驗

**明確排除**：
- RL / contextual bandit 等學習機制（依 CLAUDE.md「不包含 RL」）
- 多階段 re-evaluation / 動態 cancel hold
- StorageTask（InsertTask, pod→input-station）—— 本研究聚焦 output side
- Station 周邊密度的額外 congestion multiplier（與 reservation-aware ETA 訊號重複）

## 3. Decision Rule

### 3.1 Trigger

- **Hook 插入點**：`BotNormal.cs` 的 ExtractTask switch 分支（line 667-668 + 670-673 兩處）
  - 分支 A：`StateQueueEnqueue(new BotPickupPod(...))` 後、`_appendMoveStates(...)` 前插入
  - 分支 B（`extractTask.ReservedPod == Pod`，pod 已在身上）：在 `_appendMoveStates(...)` 之前直接插入
- **決策計算時機**：`BotSlowStartHold.Act()` 的 `_initialized` 首次執行 —— 此時 bot 在 pod cell、pod 已 lift（若需要），這正是「bot arrive at target pod」的精確語義

### 3.2 ETA estimation — via PathManager API

**新增 PathManager 公開方法**（**禁止** SlowStartController 直接碰 `_reservationTable`）：

```csharp
// In RAWSimO.Core/Control/PathManager.cs
/// <summary>
/// Reservation-aware ETA probe: estimates how long it would take the given bot
/// to travel from `from` to `to` starting now, considering current reservations.
/// Pure read-only — does NOT modify the reservation table.
/// Returns NaN if no plan found within the planner's window.
/// </summary>
public abstract double EstimateReservationAwareEta(
    BotNormal bot, Waypoint from, Waypoint to,
    double startTime, double startOrientationRad);
```

**Concrete implementation** lives next to where each PathManager subclass already constructs SpaceTimeAStar (e.g., inside the WHCAn-derived manager). The implementation:
1. Builds a transient Agent stub (start=from-node-id, dest=to-node-id, ArrivalTimeAtNextNode=startTime, OrientationAtNextNode=startOrientationRad)
2. Constructs a fresh `ReverseResumableAStar` + `SpaceTimeAStar` against the **current** `_reservationTable`
3. Calls `Search()`. If `GoalNode != -1`: return `NodeTime[GoalNode] − startTime`. Else: return `double.NaN`
4. **Never** calls `GetPathAndReservations()` → no writes to reservation table

If the SlowStartController gets `NaN`, fallback: `delay = 0` and increment `StatSlowStartSearchFailures`.

### 3.3 T_starve calculation — grounded in real OutputStation state

依 reviewer 修正 —— **不**用抽象 `queue_pods`，直接讀真實 station 狀態。

對 station `S`，給定當前時間 `t_now` 與本台 bot `self`：

```
# 1. Station's current pick (if any)
station_busy_until = max(t_now, S.BlockedUntil)

# 2. Item requests already queued at station (bot+pod physically present, waiting to be picked)
queued_item_work = S._requestsExtract.Count * S.ItemTransferTime

# 3. In-transit / not-yet-arrived ExtractTasks heading to S, excluding `self`
# (covers both "bot moving to pod" and "bot moving to station with pod")
in_flight_events = []
for task in S._activeExtractTasks where task.Bot != self:
    arrival_t = task.ExpectedArrivalAtStation
              if (task.ExpectedArrivalAtStation > 0 && task.ExpectedArrivalAtStation < +inf)
              else fallback_estimate(task)        # see §3.4
    work = task.Requests.Count * S.ItemTransferTime
    in_flight_events.append( (arrival_t, work) )

# 4. Sequence-merge
busy_until = station_busy_until + queued_item_work    # already-present work
sort in_flight_events by arrival_t ascending
for (arrival_t, work) in in_flight_events:
    if arrival_t > busy_until:
        # Station goes IDLE between busy_until and arrival_t — first starvation gap
        T_starve = busy_until - t_now
        return T_starve
    busy_until = max(busy_until, arrival_t) + work

# 5. No idle gap found — station busy continuously from t_now to busy_until
T_starve = busy_until - t_now
```

**Notes**:
- Phase 1 一律使用 `ItemTransferTime`（station 的鎖定時間）。`ItemPickTime`（bot 端）不納入 —— 它影響 bot 的離開時機而非 station 對下一個 pod 的可用性
- 若 `S._activeExtractTasks` 為空且 `_requestsExtract` 為空且 `BlockedUntil ≤ t_now`：station 已餓著 → `T_starve = 0` → `delay = 0`

### 3.4 Fallback for missing ExpectedArrivalAtStation

`task.ExpectedArrivalAtStation` 在以下情況可能未設：
- Task 剛被 dispatch、bot 尚未到 pod、hold 還沒算
- 早期 bot（在 slow-start 啟用前已 dispatch 的任務）
- 上一輪 hold 後 task 已抵達 station 並進入服務（值仍存在但已過期）

**fallback_estimate(task)** 流程：
1. 若 bot 尚未抵達 pod：`fallback = PathManager.EstimateReservationAwareEta(bot, bot.CurrentWaypoint, task.ReservedPod.Waypoint, t_now, bot.Orientation) + PodTransferTime + EstimateReservationAwareEta(bot, task.ReservedPod.Waypoint, S.Waypoint, ...)` —— 但這在 N=20 也要跑 ~40 次 A* per 決策，過貴
2. **Phase 1 採用便宜近似**：`fallback = manhattan(bot.CurrentWaypoint, S.Waypoint) / v_avg + safety_margin`
3. 計入診斷 counter `StatSlowStartExpectedArrivalMissingCount++`

### 3.5 Final delay

```
delay = max(0, T_starve − ETA)
BlockedUntil = currentTime + delay
ExtractTask.ExpectedArrivalAtStation = currentTime + delay + ETA
```

寫回 `ExtractTask.ExpectedArrivalAtStation` 供後續其他 bot 算 T_starve 時讀。

## 4. Implementation Plan (sketch — to be expanded by writing-plans skill)

### 4.1 New classes & APIs

- **`PathManager.EstimateReservationAwareEta` (abstract method on PathManager base, concrete impl in WHCAn-style subclass)** — §3.2
- **`SlowStartController`** (static helper, new file `RAWSimO.Core/Control/SlowStartController.cs`)
  - `ComputeHold(BotNormal self, ExtractTask task, double t_now) -> (delay, eta, t_starve, diagnostics)` —— 同時回傳 hold time 與診斷資料以利寫入 stats
- **`BotSlowStartHold` IBotState** (nested in BotNormal.cs, 樣板抄 BotRest)
  - 欄位：`_initialized`, `_holdUntil`, `_task`, `_diagWritten`
  - `Act()` 首次進入呼叫 `SlowStartController.ComputeHold(...)`，設 `BlockedUntil`，把診斷寫進 stats
- **`BotStateType.SlowStartHold`** — 加入 enum（用於 gate 判斷）

### 4.2 Modified files

- **`RAWSimO.Core/Control/BotTask.cs`** (ExtractTask)
  - 新增 `public double ExpectedArrivalAtStation { get; set; } = double.NaN;`（用 NaN 而非 +inf 以利「未設過」判定）
- **`RAWSimO.Core/Bots/BotNormal.cs`**
  - ExtractTask 分支 (line 667-668)：`StateQueueEnqueue(new BotPickupPod(...))` 後加 `StateQueueEnqueue(new BotSlowStartHold(extractTask))`
  - ExtractTask 分支 else (line 670-673)：在 `_appendMoveStates(...)` 之前加同樣 enqueue
  - **KPI 污染防護**：line 1331-1342 的 wait-gate（`hasActiveTask && !Moving && !_isRotatingThisTick && !inPickupOrSetdown`）必須額外排除 `BotSlowStartHold` —— 加 flag `_isSlowStartHolding`，gate 變成 `... && !_isSlowStartHolding`
  - **三軌 wait 分離**（reviewer 強調）：將 `StatWaitTimeLoadedSec` / `StatWaitTimeEmptySec` 進一步拆成 sub-categories，由當下 state / 位置決定：
    - `path_conflict_wait_sec` —— 在 BotMove state 中、被 reservation 卡住的 wait（既有 wait 主體）
    - `station_queue_wait_sec` —— 在 station queue zone 中的 wait（`IsInStationQueueZone` 為 true）
    - `intentional_hold_sec` —— 在 BotSlowStartHold state 中的 hold
    - 三者**不重疊**：state-based 判定優先序為 SlowStartHold > InStationQueueZone > PathConflict
- **`RAWSimO.Core/Control/PathManager.cs`** —— 新 abstract method
- **`RAWSimO.Core/Control/PathManagerDefault.cs` (or WHCAn subclass)** —— 提供 concrete EstimateReservationAwareEta
- **`RAWSimO.Core/Bots/BotStateType.cs`** —— 加 `SlowStartHold`

### 4.3 New stat counters (per-bot, summed at instance level)

| Counter | 型別 | 意義 |
|---|---|---|
| `StatSlowStartHoldTimeSec` | double | 累計 hold 秒數 |
| `StatSlowStartHoldEnergyJ` | double | = P_SUPPORT × hold 秒數，hold 的支援能量 |
| `StatSlowStartDecisionCount` | int | hold 決策觸發次數 |
| `StatSlowStartImmediateReleaseCount` | int | 決策結果 delay=0 次數 |
| `StatSlowStartSearchFailures` | int | ETA probe 回傳 NaN 次數 |
| `StatSlowStartExpectedArrivalMissingCount` | int | T_starve 計算時 in-flight task 缺 ExpectedArrivalAtStation 的次數 |
| `StatStationQueueWaitSec` | double | wait 拆分：站旁排隊 |
| `StatPathConflictWaitSec` | double | wait 拆分：路徑衝突 |
| `StatPathConflictWaitEnergyJ` | double | = P_SUPPORT × StatPathConflictWaitSec |
| `StatStationQueueWaitEnergyJ` | double | = P_SUPPORT × StatStationQueueWaitSec |
| `StatStarvationMissCount` | int | per-station 計：hold 結束後 station 已 idle 等本車的次數（diagnostic） |
| `StatExpectedVsActualArrivalGapSec` | double | 預測 vs 實際抵達差的累計絕對誤差 |
| `StatNeighborWaitImpactSec` | double | hold 期間半徑 K 內其他 bot 的 wait 累計（hold-cell impact proxy） |

### 4.4 Config flag

`SettingConfiguration` 新增 `bool SlowStartEnabled = false`。Baseline run 設 false（行為等同既有），Treatment run 設 true。

## 5. Experimental Plan

### 5.1 Phase 1a — Smoke test (small, N=20)

- Config: `sequ_whcan.xconf` 或 `sequ_whcan_priority.xconf`（依當前預設 WHCA\*n-P priority on，per memory `feedback_pp_default_whcan_p`）
- Layout: small
- Fleet: N=20
- Duration: 7200s
- 單 seed
- **目標**：驗證 hold 真的被觸發、KPI 拆分計數正確、無 crash。**不**用來下結論。

### 5.2 Phase 1b — Real evidence (large, N=60)

- Layout: large（既有 large layout）
- Fleet: N=60（per memory `project_fleet_sweep_nstar45`，large layout 在更高 N 下 loaded wait 才顯著）
- Duration: 7200s
- 3~5 seed paired
- **目標**：驗證主要 hypothesis（station queue wait ↓、path-conflict wait 不增、throughput 持平）

### 5.3 KPI Suite

**Constraint metrics**（必須不退化，否則整個策略否決）：
- `OrdersHandled` —— throughput 須 ≥ baseline × (1 − 容忍區)，建議 1%
- `StatPathConflictWaitSec` —— **不應上升**（核心反事實 KPI）
- `StatStarvationMissCount` —— 接近 0（hold 沒讓 station 餓著）

**Primary success metrics**（要呈現方向性改善）：
- `StatStationQueueWaitSec` ↓（核心：把 station 端排隊轉走）
- `StatStationQueueWaitEnergyJ` ↓
- `StatEnergyTotalWithSupportJ` —— 觀察性 KPI，可能改善也可能持平（取決於 hold cost vs 省下的 station queue 等待）
- `StatSlowStartHoldTimeSec / StatStationQueueWaitSec_baseline` —— 「轉化率」，若 ≈ 1 表示完全轉換

**Hold-cell impact KPIs**（reviewer #5）：
- `StatNeighborWaitImpactSec` —— hold 期間周邊 bot wait（半徑 K=2 cells 內）
- 各 bot 的 `_currentTripTurnCount` 平均 —— hold 是否讓其他 bot 多繞 / 多轉
- 若 hold-cell impact 不可忽略（neighbor wait > 0.3 × hold benefit），需在討論中標示為 risk

**Strategy behavior metrics**：
- `StatSlowStartHoldTimeSec`、`StatSlowStartDecisionCount`、avg delay per decision
- `StatSlowStartImmediateReleaseCount / StatSlowStartDecisionCount` —— 「delay=0 比例」，反映 T_starve − ETA 多常為負
- `StatSlowStartSearchFailures`、`StatSlowStartExpectedArrivalMissingCount`、`StatExpectedVsActualArrivalGapSec` —— 預測鏈健康度（reviewer #3）

### 5.4 Net energy ledger

```
ΔE_total = (Treatment.StatEnergyTotalWithSupportJ) − (Baseline.StatEnergyTotalWithSupportJ)
ΔE_hold  = Treatment.StatSlowStartHoldEnergyJ          # 額外成本
ΔE_saved_station_queue = Baseline.StatStationQueueWaitEnergyJ − Treatment.StatStationQueueWaitEnergyJ
ΔE_saved_path_conflict = Baseline.StatPathConflictWaitEnergyJ − Treatment.StatPathConflictWaitEnergyJ
```

理想方向：`ΔE_saved_station_queue > 0`、`ΔE_hold > 0`、`ΔE_saved_path_conflict ≥ 0`。
Net win 需要 `ΔE_saved_station_queue + ΔE_saved_path_conflict > ΔE_hold`，但這是 bonus，不是 Phase 1 必要條件。

## 6. Open Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| ExpectedArrivalAtStation cache 預測鏈錯誤傳播 | 用 NaN 標記未設、加 fallback、加診斷 counters（`StatSlowStartExpectedArrivalMissingCount`, `StatExpectedVsActualArrivalGapSec`） |
| Hold 期間 bot 佔據 pod cell 阻擋其他路徑 | `StatNeighborWaitImpactSec` 直接量測；若 Phase 1b 顯示 impact 過大，Phase 2 考慮 hold-at-staging-spot |
| SpaceTimeAStar search 失敗 | fallback delay=0 + 統計 counter；失敗率 > 5% 需 escalation |
| ~~`getItemTime` 模型細節~~ | **已對齊**：使用 `OutputStation.ItemTransferTime`（station-side），不混 `ItemPickTime`（bot-side） |
| KPI 污染：hold 時間被誤算為 congestion wait | wait-gate 排除 `BotSlowStartHold` + 三軌 wait 分離（§4.2） |
| 結論泛化性不足 | Phase 1a 只當 smoke test；Phase 1b 在 large/N=60 做統計顯著性 |

## 7. Out of scope (future phases)

- Phase 2：cancel 機制（監聽 queue 變化、提早 release）
- Phase 3：hold-at-staging-spot（若 Phase 1 顯示 pod-cell hold 干擾過大）
- Phase 4：輕量 ML 層調節 hold（contextual bandit 級，非 full RL）
- Phase 5：擴展到 InsertTask side
