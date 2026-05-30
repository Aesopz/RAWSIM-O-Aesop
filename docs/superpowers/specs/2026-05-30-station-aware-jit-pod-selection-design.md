# Station-aware JIT：EST-feasible Pod Selection + Slow-start — Design Spec

**Date**: 2026-05-30
**Author**: Aesop (with Claude)
**Status**: Design — awaiting user review
**Thesis context**: Station-aware JIT concept in RMFS（PP 成本回饋驅動的 TA 決策整合）

---

## 1. Motivation

### 1.1 觀察到的問題

採用 RAWSimO 原生控制器組合（large-test 案例）時，station 仍會飢餓：

- **OB**：`PodMatchingOrderBatchingConfiguration`
- **TA**：`BalancedTaskAllocationConfiguration`
- **PS（出庫）**：`OutputPodScorer = PCScorerPodForOStationBotCompleteable`，tie-break 依序 `WorkAmount` → `Nearest`
- **PP**：`WHCAnStarPathPlanningConfiguration`

問題根因：**PS 只看 pod 價值（可完成訂單數），距離只是第三順位 tie-breaker**
（`BotManagerPodSelection.cs:1497-1539` 的選 pod 迴圈，評分器
`[Completeable, WorkAmount, Nearest]`）。因此可能選到「價值高但離得遠」的 pod；
即使任務立即釋放，`bot→pod→station` 的旅行時間仍超過 station 的最早飢餓時間
（EST），飢餓無法避免。

### 1.2 為什麼 slow-start 單獨無法解飢餓

把 EST budget 攤開，pod 抵達相對於飢餓時刻只有兩種偏離：

```
slack = EST − arrival(bot→pod→station)

slack > 0  (太早到) ──► 應 HOLD     ──► slow-start 可處理 ✓（減 queue、省能）
slack < 0  (太晚到) ──► 已飢餓      ──► slow-start 無能為力 ✗
```

slow-start（`StationReleaseScheduler`）只能壓住「太早到」的 pod。一旦 pod 被
`EnqueueExtract` 選定、bot 開始去取，pod 與路徑就固定了；若它本來就太遠，
holding=0 也仍會晚到。**飢餓的修正點必須上移到「選 pod 的那一刻」**——那是最後
一個還能改選近 pod 的時間點。

### 1.3 研究定位

本設計把「站點感知」拆成共用同一個 EST 的**兩個半邊**，構成完整的 JIT 控制器：

- **上半（新增）**：選 pod 時用 EST 過濾掉「來不及」的 pod → **防飢餓**。
- **下半（已有）**：slow-start 緩啟動，把可行 pod 壓到 JIT 時刻釋放 → **防 queue、省能**。

對應論文主張：**用同一個 EST 同時驅動 PS 選擇與 release timing，在不顯著增加能耗下
消除站點飢餓**。

---

## 2. Scope

**包含（V1）**：
- 在原生出庫 PS 選 pod 迴圈注入 EST 可行性過濾（`BotManagerPodSelection.cs` 的
  `DoExtractTaskForStation` 系列，無 pod 的新選 pod 分支）。
- 可行集（`arrival + buffer ≤ EST`）內**沿用原生 `[Completeable, WorkAmount, Nearest]`
  評分**，不改原生選擇邏輯。
- 全不可行時的「救火」分支：選最快抵達（min arrival）的 pod 止血。
- 沿用既有 `StationReleaseScheduler` 作為下半邊 release executor，不改其演算法。
- 新增 config 旗標與選擇層 buffer 參數，供 baseline/treatment 2×2 實驗。

**明確排除**：
- **TA（bot 選擇）的 EST 感知**（V2 延伸，見 §7）。V1 中 bot 由原生
  `BalancedTaskAllocation` 指派，PS 只對「固定 bot」過濾 pod。
- 改動 OB / PP / `StationReleaseScheduler.Decide` 的核心演算法。
- HADGS 路徑（本案例不使用 HADGS）。
- RL / 學習式（依 CLAUDE.md「不包含 RL」）。

---

## 3. 名詞與資料來源

| 符號 | 定義 | 來源（既有） |
|---|---|---|
| `EST` | station 最早飢餓時間（排除 holder，含已派 inbound 的單伺服器管線） | `SlowStartController.ComputeStationStarvation(station, now)` |
| `eta_bp` | 固定 bot 當前位置 → pod 的理想動力學行程下界 | `PathManager.EstimateIdealKinematicEta` |
| `eta_ps` | pod → station 的理想動力學行程下界 | `PathManager.EstimateIdealKinematicEta` |
| `lift` | 舉 pod 時間（bot 尚未持 pod 時計入） | `bot.PodTransferTime` |
| `arrival` | `eta_bp + lift + eta_ps`，pod 抵達 station 的理想下界 | 本設計組合 |
| `buffer_sel` | 選擇層人為安全緩衝，吸收兩段旅行不確定性 | 新增 `JitSelectionBuffer` |
| `value` | pod↔station 原生媒合分（可完成訂單 / work amount） | 原生 `[Completeable, WorkAmount, Nearest]` |

> **為何選擇層 buffer 獨立於 release 層**：選擇層 ETA 是**兩段**（bot 離 pod 還很遠），
> 不確定性大於 release 層的**一段**（bot 已在 pod 旁）。故 `buffer_sel` 通常 >
> `SlowStartEtaSafetyBuffer`，且兩者各自校準。

---

## 4. 上半邊（新增）：EST-feasible Pod Selection

### 4.1 介入點

`BotManagerPodSelection.cs:1497-1539`，`DoExtractTaskForStation` 中 **bot 無 pod、
為固定 `(bot, oStation)` 選新 pod** 的分支。bot 與 station 在此皆已固定（入口簽章
`DoExtractTaskForStation(Bot bot, OutputStation oStation, ...)`），TA 已由上游決定。

### 4.2 演算法

```
EST = ComputeStationStarvation(oStation, now)          // 既有；排除 holder、含已派 inbound
candidates = UnusedPods.Where(p => AnyRelevantRequests(p, oStation))   // 同原生過濾

for pod in candidates:
    eta_bp  = EstimateIdealKinematicEta(bot, bot.CurrentWaypoint, pod.Waypoint, ...)
    eta_ps  = EstimateIdealKinematicEta(bot, pod.Waypoint, oStation.Waypoint, ...)
    lift    = (bot.Pod == null) ? bot.PodTransferTime : 0
    arrival = eta_bp + lift + eta_ps
    feasible(pod) = (arrival + buffer_sel <= EST)

feasibleSet = { pod in candidates : feasible(pod) }

if feasibleSet != ∅:
    bestPod = argmax_{feasibleSet} [Completeable, WorkAmount, Nearest]   // 原生評分，不改
else:
    bestPod = argmin_{candidates} arrival                                // 救火：最快到止血

EnqueueExtract(bot, oStation, bestPod, GetPossibleRequests(bestPod, oStation, ...))
```

### 4.3 三個關鍵設計性質

1. **自動退化為原生**：供給寬鬆時 EST 很大 → `feasibleSet == candidates` → 完全等同
   原生純價值選擇。上半是原生 PS 的**嚴格推廣**，只在站快餓時才改變行為，使
   baseline 對比乾淨。

2. **序列化 JIT 為 EST 口徑副產品**：`EnqueueExtract` 後該 pod 成為 active
   ExtractTask，被算進**下一次** PS 的 EST。故選第 N 個 pod 時，EST 已反映前 N−1 個
   的貢獻——「後決策考慮前決策」無需額外機制。

3. **救火切換目標函數**：可行集為空表示飢餓已不可免，目標從「最大價值」切換為
   「最小化飢餓時長」→ 選 min arrival。與 `StationReleaseScheduler.Decide` 的
   `feasible==∅ → argmin (lift+travel)` 對稱，語義一致。

### 4.4 ETA 探測一致性

- `eta_bp`/`eta_ps` 用空 reservation table 的理想下界（與既有 slow-start ETA probe
  同源）。NaN/Inf fallback：`SlowStartController.ComputeIdealEta` → Manhattan，
  沿用既有降級鏈。
- `pod→station` 段可快取（pod 在站綁定期間 station 固定）以降成本，見 §6.4。

---

## 5. 下半邊（已有）：Slow-start Release 與交接

上半過濾後，下半 `StationReleaseScheduler`（`PathManager.cs:529-535` 每 tick 呼叫）
只需處理「太早到」側：把已可行的 pod 壓到 JIT 時刻釋放。

**交接一致性**：

1. **共用 EST**：兩半都用 `ComputeStationStarvation`，口徑一致。
2. **兩層 buffer 分工**：
   - 上半 `buffer_sel`（兩段 ETA，粗、大）→ 選擇時的保守過濾。
   - 下半 `SlowStartEtaSafetyBuffer`（一段 ETA，準、小）→ 釋放時的精修。
   - 下半在更新資訊下每 tick 重算 EST，自動修正上半粗估誤差 → 閉迴路。
3. **不確定性的處理方式**：不嘗試消除 ETA 誤差（無法靠改 PP 達成），而是用既有的
   **高頻重規劃**（PS 事件驅動 + scheduler 每 tick）吸收誤差，buffer 吸收殘差。

---

## 6. 元件邊界與介面

```
BotManagerPodSelection.DoExtractTaskForStation*（改）
  ├─ 輸入：固定 (bot, oStation)、UnusedPods
  ├─ 新增依賴：ComputeStationStarvation（EST）、EstimateIdealKinematicEta（兩段 arrival）
  ├─ 邏輯：feasible 過濾 → 可行集原生評分 / 空集救火
  └─ 輸出：EnqueueExtract(bot, oStation, bestPod, requests)（介面不變）

SlowStartController.ComputeStationStarvation（重用，不改）
PathManager.EstimateIdealKinematicEta（重用，不改）
StationReleaseScheduler（重用，不改）

SettingConfiguration（新增欄位）
  ├─ StationAwarePodSelectionEnabled : bool = false
  └─ JitSelectionBuffer : double（預設值待 §8 掃描校準）
```

### 6.1 旗標 gating

`StationAwarePodSelectionEnabled == false` 時，選 pod 迴圈走原生路徑（行為與現況
位元級相同），確保 baseline 可重現。

### 6.2 與 `SlowStartEnabled` 正交

兩旗標獨立，構成 §8 的 2×2 矩陣。

### 6.3 救火診斷

每次走救火分支（可行集為空）記一次 KPI（站別計數），用以區分「飢餓源於選擇」vs
「飢餓源於艦隊容量不足」。

### 6.4 效能

- EST 每 `(bot, oStation)` 選擇算一次（非每候選 pod）。
- 兩段 ETA 只對通過 `AnyRelevantRequests` 的候選算。
- `pod→station` 段快取（key = pod.Waypoint × station.Waypoint），demand/位置變動才失效。
- large（N=45、6 station）需於 §8 量測選擇延遲，必要時加快取層。

---

## 7. V2 延伸（不在本次範圍）

**TA（bot 選擇）EST 感知**：V1 中 bot 由原生 `BalancedTaskAllocation` 固定，存在死角
——若指派的 bot 離所有有用 pod 都太遠，可行集恆空、長期救火。V2 把 EST 可行性推到
TA 層（聯合 bot×pod），讓「哪台 bot」也看 EST。改動面大（動 `BalancedTaskAllocation`、
易破壞原生行為），留待 V1 有數據後評估。V1 的救火診斷 KPI（§6.3）正是判斷是否需要
V2 的依據。

---

## 8. 實驗設計（baseline / treatment）

**2×2 ablation 矩陣**（拆解兩半各自貢獻）：

| | Slow-start OFF | Slow-start ON |
|---|---|---|
| **PS-aware OFF** | A：原生 baseline | B：現況（queue↓ 但仍飢餓） |
| **PS-aware ON** | C：純防飢餓 | **D：完整 station-aware JIT（主方法）** |

**主要 KPI**（皆有既有 stat 對應）：
- station starvation time（飢餓時長）— 主指標
- throughput（OrdersHandled）
- queue wait（PodQueueWait Mean）
- 能耗（E_Total / kJ-per-order）
- workstation occupancy rate
- 救火觸發次數（新增診斷）

**預期讀法**：
- C vs A：EST-feasible 選擇單獨降多少飢餓、能耗是否因低價值 pod 上升（pod 趟數↑）。
- B vs A：slow-start 單獨降多少 queue / 能耗（重現現況）。
- D vs B：加上防飢餓後，throughput 淨提升 vs 能耗代價的權衡（論文核心張力）。
- D vs C：slow-start 在防飢餓基礎上再省多少能耗。

**場景**：large-test 原生配置（`large_test.xconf`：`BalancedTaskAllocation` +
`PodMatchingOrderBatching` + `PCScorerPodForOStationBotCompleteable`，**非 HADGS**），
N=45、Mu-1000、7200s、PP 用 WHCA*n-P（依 `feedback_pp_default_whcan_p`）。

> 注意：標準測試案例 memory（`feedback_test_case_standard`）記為「HADGS + WHCA*n-P」，
> 但本設計針對的是 large-test **原生 PS 路徑**（`BotManagerPodSelection`），與 HADGS 無關。
> 本實驗以原生配置為準。

---

## 9. 測試與驗收

**單元測試**（NUnit，仿 `StationReleaseSchedulerSelfTest` 模式，對純可行性選擇函式）：
1. 可行集非空 → 取 max value（原生評分），晚到的高價值 pod 被排除。
2. 可行集為空 → 取 min arrival（救火）。
3. EST 極大（供給寬鬆）→ 退化為原生純價值選擇（可行集 == 全候選）。
4. buffer 邊界：`arrival + buffer == EST` 視為可行 / 不可行的判定一致。
5. `StationAwarePodSelectionEnabled == false` → 與原生選擇輸出一致。

**整合驗收**：
- 跑 §8 的 2×2 矩陣，確認 D（完整方法）相對 A：飢餓顯著下降、throughput 不劣於、
  能耗在可接受範圍。
- 檢查救火觸發次數：若 D 仍長期救火 → 指向需要 V2（TA 層）或艦隊容量問題。

---

## 10. 風險與未決

1. **兩段 ETA 不確定性大** → `buffer_sel` 校準是成敗關鍵。緩解：§8 獨立掃描
   `JitSelectionBuffer`。
2. **低價值 pod 導致 pod 趟數↑、能耗↑** → 不是 bug，是論文要量化的權衡（2×2 D vs C）。
3. **救火退化**：艦隊太小時可能長期無可行 pod → 退化成 Nearest。靠 §6.3 診斷 KPI 偵測。
4. **冷啟動**：t=0 站無 work，EST=0 → 全不可行 → 救火。可接受（bootstrap）。
5. **效能**：large 場景選擇延遲需實測（§6.4）。
6. **未決：`buffer_sel` 預設值** — 待 §8 掃描後定錨；初始建議值將於實作計畫提出。
