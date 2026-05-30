# Station-aware JIT：Net-supply Pod Selection + Slow-start — Design Spec

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

### 1.3 為什麼不是「硬性 EST 過濾」，而是「net-supply 權衡」

最直覺的修法是硬過濾：只從「來得及」（`arrival ≤ EST`）的 pod 選價值最高。但這是
**短視的**——它只問「來不來得及」，沒問「來了之後撐多久」：

- 選**低價值但準時**的 pod → station 很快做完它帶的少量工作 → 下一輪 EST 立刻收緊
  → 後續 holding budget 更難。
- 選**高價值但稍晚**的 pod → 現在製造一小段飢餓，但帶來大量 processing time → 把
  未來 EST 大幅往後推 → 後續 holding budget 全部放鬆。

關鍵：**pod 帶來的 processing time 本身就是「延長未來 EST」的資源**，硬過濾把它丟掉。
既有 `StationReleaseScheduler.Decide` 的 cascade 早已體現此理（`newStarve =
max(arrival, starve) + proc`，`+ proc` 就是 processing time 推遲未來飢餓）；本設計把
同一邏輯**提前到 PS 選擇層**，用「供給效益 vs 晚到代價」的權衡取代硬過濾。

### 1.4 研究定位

本設計把「站點感知」拆成共用同一個 EST 的**兩個半邊**，構成完整的 JIT 控制器：

- **上半（新增）**：選 pod 時用 net-supply 分數權衡 processing-time-供給與晚到代價
  → **防飢餓且不犧牲長期供給**。
- **下半（已有）**：slow-start 緩啟動，把選定 pod 壓到 JIT 時刻釋放 → **防 queue、省能**。

對應論文主張：**用同一個 EST 同時驅動 PS 選擇與 release timing，在不顯著增加能耗下
消除站點飢餓**。

---

## 2. Scope

**包含（V1）**：
- 在原生出庫 PS 選 pod 迴圈注入 **net-supply 評分**（`BotManagerPodSelection.cs` 的
  `DoExtractTaskForStation` 系列，無 pod 的新選 pod 分支）。
- 評分用 `netGain = proc − w · lateness`（見 §4），以 `Completeable` 為平手 tie-break。
- 沿用既有 `StationReleaseScheduler` 作為下半邊 release executor，不改其演算法。
- 新增 config 旗標與懲罰權重參數 `w`，供 baseline/treatment 實驗。

**明確排除**：
- **TA（bot 選擇）的 EST 感知**（V2 延伸，見 §7）。V1 中 bot 由原生
  `BalancedTaskAllocation` 指派，PS 只對「固定 bot」評分 pod。
- 改動 OB / PP / `StationReleaseScheduler.Decide` 的核心演算法。
- HADGS 路徑（本案例不使用 HADGS）。
- RL / 學習式（依 CLAUDE.md「不包含 RL」）。

---

## 3. 名詞與資料來源

| 符號 | 定義 | 來源（既有 / 新增） |
|---|---|---|
| `EST` | station 最早飢餓時間（排除 holder，含已派 inbound 的單伺服器管線） | `SlowStartController.ComputeStationStarvation(station, now)` |
| `eta_bp` | 固定 bot 當前位置 → pod 的理想動力學行程下界 | `PathManager.EstimateIdealKinematicEta` |
| `eta_ps` | pod → station 的理想動力學行程下界 | `PathManager.EstimateIdealKinematicEta` |
| `lift` | 舉 pod 時間（bot 尚未持 pod 時計入） | `bot.PodTransferTime` |
| `arrival` | `eta_bp + lift + eta_ps + buffer_sel`，含安全緩衝的抵達估計 | 本設計組合 |
| `buffer_sel` | 選擇層人為安全緩衝，吸收兩段旅行不確定性 | 新增 `JitSelectionBuffer` |
| `proc` | pod 帶來的 processing time＝(pod 能供應給本站的 item 數) × `ItemTransferTime` | 本設計（item 供給 × pick time） |
| `lateness` | `max(0, arrival − EST)`，選此 pod 造成的飢餓（閒置）秒數 | 本設計 |
| `w` | 飢餓單位懲罰權重（無量綱），預設 `1.0` | 新增 `JitStarvationPenalty` |
| `value` | 原生媒合分（可完成訂單 Completeable），用作平手 tie-break | 原生 `PCScorerPodForOStationBotCompleteable` |

> **buffer 分兩層、互不衝突**：選擇層 ETA 是**兩段**（bot→pod→station），不確定性大 →
> `buffer_sel` 較粗；release 層只剩**一段**（pod→station），bot 已在 pod 旁 →
> `SlowStartEtaSafetyBuffer` 較準。下半在更新資訊下每 tick 重算，精修上半粗估。

> **proc 用 item 數而非 Completeable**：`proc` 要忠實反映「pod 讓 station 忙多久」
> （延長未來 EST 的能力），這由**供應的 item 數**決定（每 item 一次 pick time），與
> 「可完成的完整訂單數」不同。Completeable 保留為 netGain 平手時的 tie-break，兼顧
> 「完成訂單騰出站容量」的偏好。

---

## 4. 上半邊（新增）：Net-supply Pod Selection

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
    arrival = eta_bp + lift + eta_ps + buffer_sel

    suppliedItems = Σ_item min(pod.CountAvailable(item), stationOpenDemand[item])
    proc          = suppliedItems × ItemTransferTime          // 帶來的忙碌時間
    lateness      = max(0, arrival − EST)                     // 造成的閒置時間
    netGain(pod)  = proc − w × lateness

// argmax netGain；平手用原生 Completeable，再平手用 min arrival、再 pod.ID
bestPod = candidates.OrderByDescending(netGain)
                    .ThenByDescending(Completeable)
                    .ThenBy(arrival)
                    .ThenBy(pod.ID).First()

EnqueueExtract(bot, oStation, bestPod, GetPossibleRequests(bestPod, oStation, ...))
```

`proc` 是「供給效益」（撐多久、延長未來 EST），`lateness` 是「晚到代價」（餓多久），
`w` 是飢餓單位懲罰。選 netGain 最大的，即「**供應多少 vs 晚到多久**」的權衡。

### 4.3 `w` 的物理意義與兩個角點

`proc` 與 `lateness` **同單位（秒）**，故 `w` 無量綱，且 `w=1` 有明確物理意義：

| `w` | netGain 退化為 | 行為 |
|---|---|---|
| `w = 0` | `proc`（純供給） | 退化回原生 / M1G 純價值（忽略晚到） |
| **`w = 1`** | **`proc − lateness`＝站點淨忙碌秒數變化** | **最大化 occupancy＝「throughput 有效度」** |
| `w → ∞`（有限大值逼近） | 飢餓零容忍 | 退化回硬可行性過濾（可行者必勝；全不可行時 lateness 主導→實質 min arrival） |

`w=1` 時，netGain =（增加的忙碌時間）−（造成的閒置時間）= **站點淨忙碌時間變化量**，
最大化它字面上就是最大化站點佔用率。故硬可行性與原生純價值是本式的兩個角點，`w` 是
把「防飢餓 ↔ 保價值」連續化的**主要研究旋鈕**。

### 4.4 關鍵設計性質

1. **自動退化為原生**：供給寬鬆時 EST 大 → 所有候選 `lateness=0` → netGain=proc，
   等同原生純價值（item-based）。net-supply 只在站快餓時才改變行為，baseline 對比乾淨。

2. **序列化 JIT 為 EST 口徑副產品**：`EnqueueExtract` 後該 pod 成為 active
   ExtractTask，被算進**下一次** PS 的 EST。故選第 N 個 pod 時，EST 已反映前 N−1 個
   的貢獻——「後決策考慮前決策」無需額外機制。

3. **holding budget 難題自動緩解**：`proc` 進入 netGain，演算法主動偏好「帶大量工作、
   把未來 EST 推遠」的 pod → 後續 holding budget 自然放鬆。無需獨立救火分支——全晚到時
   argmax netGain 自會選「晚一點但帶超多工作」或「最不晚」的最佳折衷。

4. **stationOpenDemand 口徑**：與 `SlowStartController` / `StationReleaseScheduler` 的
   open demand 一致（`ResourceManager.GetExtractRequestsOfStation` 未完成請求），確保
   上下半邊對「pod 對站價值」的認定相同。

### 4.5 ETA 探測一致性

- `eta_bp`/`eta_ps` 用空 reservation table 的理想下界（與既有 slow-start ETA probe
  同源）。NaN/Inf fallback：`SlowStartController.ComputeIdealEta` → Manhattan，
  沿用既有降級鏈。
- `pod→station` 段可快取（pod 在站綁定期間 station 固定）以降成本，見 §6.4。

---

## 5. 下半邊（已有）：Slow-start Release 與交接

上半選出 pod（net-supply 最佳）後，下半 `StationReleaseScheduler`
（`PathManager.cs:529-535` 每 tick 呼叫）把它壓到 JIT 時刻釋放，處理「太早到」側。

**交接一致性**：

1. **共用 EST 與 open demand 口徑**：兩半都用 `ComputeStationStarvation` 與相同的
   open demand 定義，認定一致。
2. **兩層 buffer 分工**：上半 `buffer_sel`（兩段、粗、大）→ 選擇時保守估計；下半
   `SlowStartEtaSafetyBuffer`（一段、準、小）→ 釋放時精修。下半每 tick 重算 EST，
   自動修正上半粗估誤差 → 閉迴路。
3. **不確定性的處理方式**：不嘗試消除 ETA 誤差（無法靠改 PP 達成），而是用既有的
   **高頻重規劃**（PS 事件驅動 + scheduler 每 tick）吸收誤差，buffer 吸收殘差。

---

## 6. 元件邊界與介面

```
BotManagerPodSelection.DoExtractTaskForStation*（改）
  ├─ 輸入：固定 (bot, oStation)、UnusedPods
  ├─ 新增依賴：ComputeStationStarvation（EST）、EstimateIdealKinematicEta（兩段 arrival）、
  │            stationOpenDemand（proc 計算）
  ├─ 邏輯：對候選算 netGain = proc − w·lateness → argmax（Completeable / arrival / ID tie-break）
  └─ 輸出：EnqueueExtract(bot, oStation, bestPod, requests)（介面不變）

SlowStartController.ComputeStationStarvation（重用，不改）
PathManager.EstimateIdealKinematicEta（重用，不改）
StationReleaseScheduler（重用，不改）

SettingConfiguration（新增欄位）
  ├─ StationAwarePodSelectionEnabled : bool = false
  ├─ JitStarvationPenalty : double = 1.0     // w
  └─ JitSelectionBuffer  : double            // buffer_sel，預設值待 §8 掃描校準
```

### 6.1 旗標 gating

`StationAwarePodSelectionEnabled == false` 時，選 pod 迴圈走原生路徑（行為與現況
位元級相同），確保 baseline 可重現。

### 6.2 與 `SlowStartEnabled` 正交

兩旗標獨立，構成 §8 的 2×2 矩陣。

### 6.3 純函式抽出（可測性）

把 netGain 評分與選擇抽成純函式（輸入：候選的 `proc`/`arrival`/`Completeable`/`ID`
清單、`EST`、`w`；輸出：bestPodId），與引擎物件解耦，供 §9 單元測試。仿
`StationReleaseScheduler.Decide` 的純函式風格。

### 6.4 效能

- EST 每 `(bot, oStation)` 選擇算一次（非每候選 pod）。
- 兩段 ETA + proc 只對通過 `AnyRelevantRequests` 的候選算。
- `pod→station` 段快取（key = pod.Waypoint × station.Waypoint），demand/位置變動才失效。
- large（N=45、6 station）需於 §8 量測選擇延遲，必要時加快取層。

---

## 7. V2 延伸（不在本次範圍）

**TA（bot 選擇）EST 感知**：V1 中 bot 由原生 `BalancedTaskAllocation` 固定，存在死角
——若指派的 bot 離所有有用 pod 都太遠，所有候選 lateness 巨大、netGain 被壓低。V2 把
net-supply 評分推到 TA 層（聯合 bot×pod），讓「哪台 bot」也進入權衡。改動面大（動
`BalancedTaskAllocation`、易破壞原生行為），留待 V1 有數據後評估。

---

## 8. 實驗設計（baseline / treatment）

**2×2 ablation 矩陣**（拆解兩半各自貢獻）：

| | Slow-start OFF | Slow-start ON |
|---|---|---|
| **Net-supply PS OFF** | A：原生 baseline | B：現況（queue↓ 但仍飢餓） |
| **Net-supply PS ON** | C：純防飢餓 | **D：完整 station-aware JIT（主方法）** |

**`w` 掃描（核心 ablation 軸）**：在 D 上掃 `w ∈ {0, 0.5, 1, 2, ∞}`：
- `w=0`：等同原生純價值（驗證退化性質）。
- `w=1`：最大化 occupancy（理論基準）。
- `w→∞`：硬可行性過濾（對照「短視過濾」的劣化）。
- 證明存在中間 `w`（預期 ~1）在飢餓與能耗間取得最佳權衡。

**`buffer_sel` 掃描**：次要，固定 `w` 後掃 `JitSelectionBuffer` 校準兩段 ETA 不確定性。

**主要 KPI**（皆有既有 stat 對應）：
- station starvation time（飢餓時長）— 主指標
- throughput（OrdersHandled）
- queue wait（PodQueueWait Mean）
- 能耗（E_Total / kJ-per-order）
- workstation occupancy rate
- 平均 `lateness` 與 `proc`（新增診斷：選擇權衡的實際落點）

**預期讀法**：
- C vs A：net-supply 選擇單獨降多少飢餓、能耗是否因搬運模式改變而變動。
- B vs A：slow-start 單獨降多少 queue / 能耗（重現現況）。
- D vs B：加上防飢餓後 throughput 淨提升 vs 能耗代價（論文核心張力）。
- `w` 曲線：飢餓、能耗、throughput 隨 `w` 的變化，定位最佳 `w`。

**場景**：large-test 原生配置（`large_test.xconf`：`BalancedTaskAllocation` +
`PodMatchingOrderBatching` + `PCScorerPodForOStationBotCompleteable`，**非 HADGS**），
N=45、Mu-1000、7200s、PP 用 WHCA*n-P（依 `feedback_pp_default_whcan_p`）。

> 注意：標準測試案例 memory（`feedback_test_case_standard`）記為「HADGS + WHCA*n-P」，
> 但本設計針對 large-test **原生 PS 路徑**（`BotManagerPodSelection`），與 HADGS 無關。
> 本實驗以原生配置為準。

---

## 9. 測試與驗收

**單元測試**（NUnit，對 §6.3 的純 netGain 選擇函式）：
1. `w=0` → 取 max proc（純供給，等同原生 item-value），忽略 lateness。
2. 有限大 `w`（如 1e6）→ 退化為硬可行性：可行者（lateness=0）必勝於不可行者；全不可行時
   lateness 項主導 → 實質選 min arrival（非靠 tie-break，而是 netGain 連續逼近）。
3. `w=1`，高 proc 但稍晚 vs 低 proc 但準時 → 依 `proc − lateness` 正確權衡（高 proc
   稍晚者在 `proc 差 > lateness` 時勝出）。
4. netGain 平手 → Completeable tie-break → min arrival → pod.ID（決定性）。
5. EST 極大（供給寬鬆）→ 全 `lateness=0` → 等同 `w=0`。
6. `StationAwarePodSelectionEnabled == false` → 與原生選擇輸出一致。

**整合驗收**：
- 跑 §8 的 2×2 矩陣 + `w` 掃描，確認 D（`w≈1`）相對 A：飢餓顯著下降、throughput
  不劣於、能耗在可接受範圍。
- 確認 `w→∞`（硬過濾）相對 `w≈1` 在 throughput / holding budget 上的劣化，驗證
  §1.3 的「短視過濾」論點。

---

## 10. 風險與未決

1. **兩段 ETA 不確定性大** → `buffer_sel` 校準重要，但 net-supply 對它較硬過濾更穩健
   （晚到是漸進懲罰，非二元排除）。緩解：§8 次要掃描。
2. **`proc` 與 `lateness` 量綱耦合於 `ItemTransferTime`**：`proc = items × ItemTransferTime`，
   `w=1` 的「秒對秒」物理意義依賴此換算正確。需確認 `ItemTransferTime` 取站別實際值。
3. **能耗變動方向未定**：net-supply 偏好高 proc pod（可能更遠），與「選近 pod 省搬運」
   方向不一定一致 → 能耗升降由 `w` 與場景決定，靠 §8 量化（這是論文要呈現的權衡，非 bug）。
4. **冷啟動**：t=0 站無 work，EST=0 → 全 lateness 大 → netGain 由 proc 主導（選最大供給）。
   可接受（bootstrap）。
5. **效能**：large 場景選擇延遲需實測（§6.4）。
6. **未決：`JitSelectionBuffer` 預設值** — 待 §8 掃描後定錨；初始建議值於實作計畫提出。
7. **未決：`w` 是否需站別/負載自適應** — V1 用全域固定 `w`；若 §8 顯示最佳 `w` 隨負載
   漂移，列為 V2 候選（動態 `w` 即 PP→TA 回饋的一種具體形式）。
