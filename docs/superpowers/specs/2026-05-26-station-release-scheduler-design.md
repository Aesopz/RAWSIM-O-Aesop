# Centralized Station Release Scheduler — Design Spec

**Date**: 2026-05-26
**Author**: Aesop (with Claude)
**Status**: Design approved, awaiting implementation plan

---

## 1. Motivation

現行 slow-start 的 holder 協調是**分散式 FIFO**：每台 holding bot 各自每 tick 跑
`SlowStartController.ComputeHold`，靠 `_slowStartHoldStartTime` 排序、用「senior look-ahead」
推測別人的釋放。這個設計的根本問題：

1. **同步釋放 bug** — 多個 holder 看到對稱的 T_starve 就一起放（FIFO senior 只是緩解、非根治）。
2. **無法做價值取捨** — FIFO 只看「誰先 hold」，不看哪個 pod 對 station 更有價值。
3. **靜態緩衝無法修復結構性飢餓**（2026-05-26 實驗）：把 buffer 從 10s 加到 16.8s（= 多車 est−act
   離差平均），station IdleTime 不降反升（807→831s）、conflict stop&go +34%。因為 est−act 離差由
   內生的 queue/壅塞主導，會隨提早釋放而自我膨脹，不是能用靜態緩衝補掉的固定 bias。

**核心轉向**：單一 station 由多台 bot 服務，本質是**集中式排程問題** —— 一次只釋放一個 pod，
依「可行性（不讓 station 飢餓）優先、再比 pod 價值」決定釋放順序與時機。把分散式 FIFO 換成
per-station 仲裁者。

## 2. Scope

**包含**：
- 新增 `StationReleaseScheduler`，per-station 每 tick 仲裁：算一次 starve_time → 選單一 chosen pod
  → 設釋放決定、其餘 holder 套 cascade budget。
- `BotSlowStartHold.Act` 由「自己算」改為「讀排程器寫入的釋放決定」（被動）。
- pod 價值 = 即時 pod↔station 需求配對（含未綁定 open demand）。
- cascade budget 用單伺服器佇列推導（Case A / Case B）。
- 沿用既有 ETA（空 reservation table 的 SpaceTimeAStar 動力學）與 T_starve 單伺服器模型。

**明確排除**：
- RL / 學習式排序（依 CLAUDE.md「不包含 RL」）。
- 多 station 之間的 pod 重指派（pod 在 order batching 指派時已綁定 station，本設計不動綁定）。
- 改動 order batching 本身；只「讀取」其配對口徑來算價值。

## 3. 名詞與資料來源

| 符號 | 定義 | 來源 |
|---|---|---|
| `starve_time` | 從 now 起，station 把現有工作（processing pod + queue + 已釋放 inbound）做完所需時間 | `ComputeStarvation`，排除所有 holder |
| `travel_h` | holder h 的 pod→station 理想動力學行程時間（無衝突下界） | `EstimateIdealKinematicEta`（空 reservation table） |
| `lift_h` | h 舉 pod 時間；已舉則 0 | `PodTransferTime` |
| `value_h` | pod 對 station 的即時配對價值 = `Σ_i min(pod_h.stock[i], station.openDemand[i])` | pod 庫存 ∩ station open demand（order batching 口徑） |
| `proc_c` | chosen pod 的**實際撿貨工時** = `chosen.Requests.Count × ItemTransferTime` | 已綁定 ExtractTask.Requests |
| `buffer` | 安全緩衝 | 設定值，預設沿用現行常數 |

> **value 與 proc 刻意分開**：`value_h` 是「選擇用」的潛在配對分數（含未綁定 open demand，反映 pod
> 抵達後可能爭取的工作量）；`proc_c` 是「cascade 用」的已承諾撿貨工時（決定 station 下次空轉的絕對時間）。

## 4. 每 tick 單站決策演算法

```
holders H = {此站所有處於 BotSlowStartHold 的 bot}
if H == ∅: return

starve_time = ComputeStarvation(station, exclude = all holders)

for h in H:
    travel_h   = EstimateIdealKinematicEta(h, h.pod.Waypoint, station.Waypoint)
    lift_h     = (h.Pod == null) ? h.PodTransferTime : 0
    value_h    = Σ_i min(pod_h.stock[i], station.openDemand[i])
    budget_h   = starve_time − lift_h − travel_h − buffer
    feasible_h = (budget_h ≥ 0)            // 能否在 starve 前（含 buffer）抵達

feasible = {h in H : feasible_h}
if feasible ≠ ∅:
    chosen = argmax_{h in feasible} value_h        // 可行者中價值最高
else:
    chosen = argmin_{h in H} (lift_h + travel_h)   // 全不可行 → 救火，選最快抵達
```

### 4.1 釋放時機

- **chosen**：`deadline = now + max(0, budget_chosen)`。budget 耗盡才釋放（最後責任時刻，
  最大化 holding 省能）。若 chosen 來自「救火」分支（`feasible == ∅`）→ **立即釋放**。
- **其餘 holder**：本 tick 不釋放，套用 cascade budget 延長 holding（見 4.2）。

### 4.2 Cascade budget（其餘 holder）

chosen 釋放後，station 下次空轉的絕對時間（從 now 計）：

> **lift 一致性（對原始口述的刻意修正）**：原始描述的 cascade 條件用「chosen ideal travel time」
> 未含 lift，但 base budget 公式含 lift。chosen pod 實際抵達 station = `lift + travel`，故本設計
> 在 cascade 的抵達比較與 newStarve 中**一律用 `travel_chosen_full = lift + travel`**，與 base
> budget 保持一致。

```
travel_chosen_full = lift_chosen + travel_chosen
proc_c             = chosen.Requests.Count × ItemTransferTime

newStarve = (travel_chosen_full > starve_time)
              ? travel_chosen_full + proc_c     // Case A：chosen 抵達前 station 已空窗
              : starve_time        + proc_c     // Case B：chosen 排隊，做完現有 pipeline 再做 chosen

for h in H \ {chosen}:
    budget'_h = newStarve − lift_h − travel_h − buffer   // 延長後的 holding budget
```

### 4.3 序列化（一次一個）

下一 tick 重跑：chosen 已釋放 → 變成 inbound in-flight，計入 `ComputeStarvation` 的 starve_time
（不再是 holder）。排程器在**剩餘** holder 中重選 next chosen。如此每個 starve 缺口只放一台，
自然序列化，無需顯式 queue。

## 5. 邊界處理

| 情況 | 處理 |
|---|---|
| 只有 1 個 holder | 它即 chosen，無 cascade；deadline = now + max(0, budget) |
| 全不可行（救火） | chosen 立即釋放，不等 deadline |
| station 正在 processing | starve_time 含當前 pod 剩餘工時（`GetBlockedUntilTime`） |
| ETA probe 失敗（NaN） | fallback：TimeEfficientPathManager + IdealTravelTime（沿用現行）；再失敗用 Manhattan |
| value 平手 | tie-break 用較小 (lift+travel)（早到優先），再 tie 用 bot.ID（決定性） |
| open demand 為 0（無未綁定需求） | value_h 退化為 `Requests.Count`（committed），仍可比較 |

## 6. 元件邊界與介面

```
StationReleaseScheduler (新)
  ├─ 輸入：OutputStation、其 holders（從 Instance.Bots 篩 _isSlowStartHolding 且同站）
  ├─ 依賴：ComputeStarvation、EstimateIdealKinematicEta（沿用 SlowStartController 既有靜態函式）
  ├─ 輸出：每 holder 寫入 release 決定 — (isChosen, deadline) 或 (hold, budget')
  └─ 被呼叫：PathManager 每 tick（每 station 一次）

SlowStartController (保留)
  └─ 降為計算工具庫：ComputeStarvation、ComputeIdealEta、ManhattanEta、ComputeQueueBudget
     移除：FIFO senior look-ahead（被 scheduler 取代）

BotNormal.BotSlowStartHold.Act (改)
  └─ 不再自己算 ComputeHold；改讀 bot 上由 scheduler 寫入的 release 決定欄位
     第一 tick 仍負責 _isSlowStartHolding=true、claim storage；釋放時清理（同現行）
```

決定欄位（寫在 BotNormal 或 ExtractTask）：
- `_slowStartReleaseDeadline : double`（now + budget，scheduler 每 tick 更新）
- `_slowStartIsChosen : bool`（診斷/telemetry 用）

`Act` 邏輯：`if currentTime ≥ _slowStartReleaseDeadline → release; else 續 hold`。

## 7. 測試與驗收

**單元測試**（NUnit，對 `StationReleaseScheduler` 的純函式部分）：
1. 單 holder → 它是 chosen，budget = starve − lift − travel − buffer。
2. 兩 feasible holder → 高 value 者 chosen，低 value 者套 cascade（budget 變大）。
3. 全不可行 → 最快抵達者 chosen 且立即釋放。
4. Cascade Case A（travel_full > starve）→ newStarve = travel_full + proc。
5. Cascade Case B（travel_full ≤ starve）→ newStarve = starve + proc。
6. value 平手 → tie-break early-arrival → bot.ID。

**整合驗收**（test + SEQU + Mu-100 + cap100 + 7200，對比現行 10s buffer 版）：
- **主指標**：OutputStation IdleTime 應 ≤ 現行 ON（807s），目標逼近 OFF（728s）。
- TP：應 ≥ 現行 ON（219），目標逼近 OFF（225）。
- 能耗 kJ/order：維持 < OFF（11.03），不顯著劣於現行 ON（10.22）。
- conflict stop&go：不高於現行 ON（113），不應回到 16.8s 版的 151。
- 不再出現多 holder 同步釋放（檢查 slowstart_decisions.csv 釋放時間戳分散）。

## 8. 風險與未決

- **starve_time 仍是無壅塞估計**：scheduler 修正「選誰、何時」，但 travel_h 仍是理想下界，
  多車實際旅行更久。本設計不直接解這個（屬 buffer/壅塞耦合議題），但「一次一個 + 價值選擇」
  應減少同時在途車數 → 間接降低壅塞，需用驗收實驗確認。
- **每 tick O(N·items) 成本**：value 計算要掃 pod stock ∩ open demand。test 場景 N=4 可忽略；
  large（N=45、6 station）需評估，必要時 value 快取（demand 變動才重算）。
- **cascade 的 chosen 行進間，starve_time 重估的一致性**：下一 tick chosen 已是 inbound，
  其 ExpectedArrivalAtStation 需正確快取，否則 starve_time 會少算（沿用現行 cache 機制）。
