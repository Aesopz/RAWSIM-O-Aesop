# 報告規範：正典組態、指標字典、APA 7 圖表

> 給所有 agent：產出**任何數據、表格、圖**之前讀這份。與 `docs/EXPERIMENT-HANDBOOK.md` 並用。
> 版本：2026-09-16，Canon v2。正典改版（`exp.py canon-bump`）時必須同步更新第 1 節。

---

## 1. 正典組態（Canon v1）

真相來源：`Material/Instances/Canon/`。本節是閱讀用摘要；**有衝突以檔案為準**。

### 1.1 三個模型的關係

```
M4G      ＝ M4GConfiguration（拆單 MILP＋Dinkelbach 定價）
M4G-NS   ＝ M4G ＋ OrderAtomicNoSplit ＋ OrderAtomicCanonPrices     ← 只差 2 行
HGS-M5   ＝ GreedyM5Configuration（M4G 的貪婪鏡像）＋ DrawsFirstDispatch
HGS-M5 大規模 ＝ HGS-M5 ＋ CandidatePodTopK=10                       ← 只差 1 行
```

### 1.2 開啟的行為旗標（true 或非預設值）

| 旗標 | M4G | M4G-NS | M5 | 作用 |
|---|:-:|:-:|:-:|---|
| `UpperBoundJump` | ✓ | ✓ | ✓ | λ 起點過低時跳到可證明上界（取代 ×2 升壓） |
| `LineBoundTau` | ✓ | ✓ | ✓ | 上界加 τ，讓候選計畫嚴格優於空計畫（τ＝DinkelbachTolerance／LambdaTolerance） |
| `NewPodFirstAllocation` | ✓ | ✓ | ✓ | 分配時先用新派貨架（對齊 M1G 貪婪分配） |
| `M1GUrgentGate` | ✓ | ✓ | ✓ | 緊急單閘門採 M1G 形式 |
| `UrgentOrderGate` | ✓ | ✓ | ✓ | 啟用緊急單閘門 |
| `PickDistancePricing` | ✓ | ✓ | ✓ | λ 分子用揀貨（Extract）距離 |
| `FastLane` | ✓ | ✓ | ✓ | 貨架已在站時的快速通道 |
| `ReleaseParentOnFirstSplit` | ✓ | ✓ | ✓ | 首次拆單即釋放母單 |
| `LineAtomicSplitting` | ✓ | ✓ | — | 拆單以行為最小單位 |
| `CompactLineModel` | ✓ | ✓ | — | 行級精簡 MILP 公式 |
| `OrderAtomicNoSplit` | — | ✓ | — | 整單承諾（不拆單） |
| `OrderAtomicCanonPrices` | — | ✓ | — | 整單下沿用正典價格 |
| `MarginalDispatchScore` | — | — | ✓ | 派車以邊際淨值計分 |
| `DrawsFirstDispatch` | — | — | ✓ | 先取完改善的取貨，再評估派車（提速） |
| `PackingFullWholeOrderFallback` | — | — | ✓ | 拆單預算（包裝箱／MaxPartsPerOrder）耗盡時，以 HADGS 式整單貨架組合派車解凍；預算未耗盡時不啟動（Canon v2） |
| `CandidatePodTopK=10` | — | — | 僅大規模 | 候選貨架前 10 |

### 1.3 共同數值參數

| 參數 | 值 | 意義 |
|---|---|---|
| `LambdaScale` / `MuScale` / `DeltaScale` | 1 / 1 / 1 | λ（每行距離價）、μ（每單價）、β（兌現率，程式碼名 Delta）全部量測，不縮放 |
| `EpsilonScale` | 0 | ε 已移除 |
| `DinkelbachIterations` / `DinkelbachTolerance` | 5 / 0.5 | M4G 家族；M5 對應 `LambdaTolerance` |
| `WarmupLines` | 50 | 暖機期，用 fallback 價格 |
| `LambdaFallback` / `DeltaFallback` / `LinesPerOrderFallback` | 10 / 0.05 / 2.4 | 暖機價格 |
| `LambdaFixed` / `DeltaFixed` | 0 / 0 | 0＝不固定（自我校準） |
| `TieBreaker` | EarliestDueTime | |

### 1.4 明確關閉（不在正典）

`ForbidSplitting`、`LegacyObjective`、`DegenerateToBindingOnly`、`PodCreditCapEnabled`、`PodTierDrawPricingEnabled`（ρ）、`IncrementalValuationEnabled`、`SplitOnlyAtPresentPods`、`SplitMarginalProbeEnabled`、`DispatchPairTopK`，以及 `MaxPartsPerOrder`、`PackingStationCount`、`PackingBufferCapacity`（只在實驗臂開）。

### 1.5 正典改版紀錄

| 版本 | 日期 | 內容 |
|---|---|---|
| v1 | 2026-09-15 | NewPodFirstAllocation＋LineBoundTau＋M1GUrgentGate（M4G/NS/M5）；M5 加 DrawsFirstDispatch |
| v2 | 2026-09-16 | M5 加 PackingFullWholeOrderFallback（預算耗盡時整單組合派車）；M4G 不變（MILP 自然退化）；預算未耗盡的場次逐位不變 |

### 1.6 書面 policy 名稱（投影片、論文、CSV 的 Policy 欄）

| 書面名 | 意義 | 程式旗標 |
|---|---|---|
| M1G | Jiao 的 MILP 基準 | `M1GConfiguration` |
| M4G | 比值目標式，行級拆單 | `M4GConfiguration` |
| M4G-NS | 比值目標式，整單承諾（no split） | `OrderAtomicNoSplit`＋`OrderAtomicCanonPrices` |
| M4G-WS | Jiao 加權和目標式 α=(1,−40,1000)，M4G 可行域 | `LegacyObjective`＋`LegacyIdleSlotWeight=1000`＋`LegacyRewardValuation` |
| M4G-WS₀ | 同上，距離權重 w₁=0 | 加 `LegacyDistanceWeight=0` |
| HGS-M5 | **純貪婪**（2026-09-18 定案）：線級 draw／邊際 dispatch／整單 fallback，**無價格**、無 λ 迭代、無跳躍；報告主線 | `GreedyM5Configuration`＋`LambdaFixed=10000`＋`MuFixed=16208`＋`LambdaIterations=0`＋`UpperBoundJump=false` |
| HGS-M5-λ | 同結構＋Dinkelbach 自校準價格（λ、μ、β）＋上界跳躍；Canon v2 原組態；future work | `GreedyM5Configuration`（正典原樣） |
| HGS-M5-λ̄ | 同結構＋固定常數 λ（消融） | `LambdaFixed=λ`＋`MuFixed=κλ`＋`LambdaIterations=0`＋`UpperBoundJump=false` |
| HADGS | 大規模啟發式基準 | `HADGSConfiguration` |


> **命名分界（2026-09-18）**：本日之前產出的所有 CSV／圖／NOTES 中的「HGS-M5」皆指 **HGS-M5-λ**（含 M5 vs HADGS +31%、N 掃描、包裝站、保真度）。舊檔不回頭改名；新產出一律依上表。純貪婪 HGS-M5 目前僅 seed 0（`2026-09-18_m5_noprice_greedy`），作主線前需補 10 seeds 正式對照。

規則：書面名稱描述**它是什麼**（目標式形式、承諾單位），不用程式旗標名；「Legacy」「NoDist」等旗標字樣不得出現在表格、圖或內文。arm label 與實驗 id 可保留程式名，呈現層一律換成書面名（`csv_bundle.py` 的 label map 負責）。

---

## 2. 使用者在意的指標（主表 12 欄，順序固定）

| 欄 | 英文表頭 | 計算 | 來源 | 解讀 |
|---|---|---|---|---|
| policy | Policy | — | experiment arm | |
| bots | Bots | `BotCount` | xlayo | |
| Items | Items | `StatOverallItemsHandled` | statistics.txt | 完成件數；**拆單比較的主指標** |
| Lines | Lines | `StatOverallLinesHandled` | statistics.txt | 完成訂單行數 |
| Orders | Orders | `StatOverallOrdersHandled` | statistics.txt | 完成訂單數；拆單會系統性低估（在製品） |
| Pile-on | Pile-on | Orders ÷ 揀貨站到站次數 | `StatSystemOrderPileOn`（`InstanceStatistics.cs:342`） | 每趟貨架到站完成幾張單 |
| Trips | Trips | 揀貨站到站次數 | kpi_report.csv `output_station_arrivals` | |
| Trips/Orders | Trips/Order | Trips ÷ Orders | 衍生 | Pile-on 的倒數 |
| m/Line | m/Line | `StatOverallDistanceTraveled` ÷ Lines | 衍生 | 全機隊（含補貨）每行距離 |
| EOR | EOR (kJ/order) | 機械能 E1–E5 ÷ Orders | `KPI_EOR`（`InstanceStatistics.cs:1712`） | 不含 support energy |
| TurnoverMedian | Turnover (s) | 訂單「產生 → 完成」的中位數 | `StatMedianTurnoverTime`；樣本在 `InstanceEvents.cs:122` | 拆單代價只在這裡顯形；用中位數（右偏） |
| StationIdle | Station idle (%) | 各揀貨站 `IdleTime / UpTime` 的平均 ×100 | stationstatistics.csv | **完整版**：含初始化、pod 換 pod 間隔 |

**解讀原則**：
- 拆單比較看 Items，件數飽和才看 Orders／EOR。
- 效率一定與 Turnover 並報。
- 優先序：產出 > Pile-on > EOR。

---

## 3. 系統其他指標字典

`statistics.txt` 各段（`>>> Overall`、`>>> KPI Summary`、`>>> Energy`、`>>> Motion Behavior`、`>>> Support & Utilization`）與 `kpi_report.csv`。定義位置皆在 `RAWSimO.Core/InstanceStatistics.cs`，除非另註。

### 3.1 產出與訂單

| 指標 | 意義／計算 |
|---|---|
| `StatOverallOrdersPlaced` / `ItemsOrdered` / `BundlesPlaced` | 模擬期間產生的訂單、訂購件數、補貨批數 |
| `StatOverallBundlesHandled` | 補貨站完成的補貨批數 |
| `StatThroughputOrdersPerHour`（`KPI_TP`） | Orders ÷ 模擬小時；**時長反算**＝Orders ÷ TP |
| `KPI_IPO`（`StatSystemItemPileOn`，`:344`） | Items ÷ 揀貨站到站次數；每趟搬幾件 |
| `StatOverallOrdersLate`（`KPI_LATE`） | 完成時刻晚於 DueTime 的訂單數（`InstanceEvents.cs:113`） |
| `StatOrdersLateRate` | Late ÷ Orders |
| `StatAverageLatenessSec` / `StatMaxLatenessSec` | 僅逾期訂單的平均／最大逾期秒數 |
| `StatAverage/Median/Lower/UpperQuartileTurnoverTime` | 訂單 `TimeStamp`（進入積壓）→ 完成 |
| `Stat*ThroughputTime` | 訂單 `TimeStampSubmit`（指派到站，`InstanceEvents.cs:525`）→ 完成；即占用站台時間 |
| `Stat*SlotOccupancy` | 站台槽位從被占用到訂單完成的秒數（`OutputStation.cs:424`） |
| `Stat*SlotFirstPickWait` | 占用槽位到第一次揀貨的秒數（`OutputStation.cs:118`） |

### 3.2 距離

| 指標 | 意義 |
|---|---|
| `StatOverallDistanceTraveled`（`KPI_RD`） | 全機隊總行駛距離 (m) |
| `StatOverallDistanceTraveledExtract` | 執行揀貨任務（Extract）時的距離；λ 的分子 |
| `StatOverallDistanceEstimated` | 規劃估計距離 |
| `KPI_OD` / `StatOrderDistanceM` | 總距離 ÷ Orders |
| `StatLoadedDistanceM` | 載貨架行駛距離；空車距離＝總距離 − 載重距離 |

### 3.3 能量（Rizqi 模型，機械能）

| 指標 | 意義 |
|---|---|
| `StatEnergyTotalKJ` | E1–E5 合計 |
| `StatEnergyE1AccelKJ` … `E5LiftLowerKJ` | 加速、減速、巡航、旋轉、舉升／放下 |
| `StatEnergyPerOrderKJ` | ＝ `KPI_EOR` |
| `StatEnergyPerMeterJoule` | 總機械能 ÷ Rizqi 距離 |
| `StatMove/TurnEnergy{Empty,Loaded}KJ`、`Stat*Time*Sec` | 空車／載重的移動、旋轉能量與時間 |
| `StatPrefEmptyW` / `StatPrefLoadedW` | 空車／載重平均功率 |
| `StatESupportKJ`、`StatEWait*`、`StatEQueueing*` | 支援能耗（正典關閉，皆 0） |
| kpi_report L2 | 空車／載重的趟次、距離、時間、能量、每趟平均 |
| kpi_report L3 | E1–E5 與占機械能百分比 |
| kpi_report L4 | 旋轉次數與能量、stop-and-go、排隊 stop-and-go |
| kpi_report L5 | 等待時間與等待比例（mean／median／p95） |
| kpi_report L6 | 能量組成、空載能量比、有效運動比、每公尺能量 |

### 3.4 到站、排隊與站台

| 指標 | 意義 |
|---|---|
| `StatInputStationArrivals` / `StatOutputStationArrivals` | 補貨站／揀貨站到站次數 |
| `StatPodVisitCount` | 貨架到揀貨站的造訪數 |
| `StatPodQueueWait{Mean,P50,P95,Max}Sec` | 貨架在揀貨站排隊等待（**只記揀貨站**） |
| `StatPodPickingTime*Sec` | 貨架在站揀貨時間 |
| `StatPodVisitOrdersServed_*` | 每次造訪服務的訂單數分布 |
| `StatPodHandoffGap*` | 同站前一貨架完成 → 下一貨架首揀的間隔（不含第一個貨架） |
| `StatQueueingAtStationTimeSec` | 機器人在站排隊總時間（`BotNormal.cs:2067`） |
| `StatInputPodQueueWait*` | 補貨站貨架排隊等待 |
| `StatStationStarvationTimeSec` | 站台閒置且有供給在途或待服務需求的時間（Type A，供給落後），**不含第一張單完成前**（`OutputStation.cs:836`） |
| `StatStationNoOrderIdleTimeSec` | 站台閒置且沒有任何訂單可做（Type B） |
| `*PctOfSimXStations` | 上兩者 ÷（模擬秒數 × 站數） |
| stationstatistics.csv | 每站 Transfers、PodsHandled、PodHandlingTime、PileOn、IdleTime、UpTime |

⚠️ StationIdle（主表）≠ Starvation。主表一律用 IdleTime/UpTime。

### 3.5 機器人使用率

| 指標 | 意義 |
|---|---|
| `StatTimeIdleSec` / `StatTimeRestSec` | 閒置／休息總時間 |
| `StatRobotUtilization` | 1 − idle／總時間（把 Rest 算成使用，**會高估**） |
| `StatRobotUtilizationProductive` | 再扣除 Rest |
| `StatRobotUtilizationEffective` | 再扣除站前排隊與壅塞等待 |
| `StatPickupCount` / `StatSetdownCount` | 舉起／放下貨架次數 |
| `StatTripCount{Loaded,Empty}` | 載重／空車趟次 |
| `StatTripWaitRatio*` | 每趟等待時間比例 |
| `StatStopAndGo*`、`StatQueueStopAndGo*` | 行進中與排隊中的停走事件數與能量 |
| `StatOverallCollisions` | 碰撞數（應為 0） |

### 3.6 計算時間與診斷

| 指標 | 意義 |
|---|---|
| `StatTimingOrderBatching{Average,Overall,Count}` | 訂單分配（M4G／M5 求解）的每次平均、總秒數、次數；**求解時間比較用這個** |
| `StatTimingPathPlanning*`、`TaskAllocation*`、`PodStorage*`、`ItemStorage*`、`ReplenishmentBatching*` | 其他控制器的計算時間 |
| `StatRealTimeUsed` | 整場牆鐘秒數 |
| `StatJITEta*` | 到站時間預測誤差（診斷用） |
| `StatDecision*` | HADGS 觸發時的決策空間（非 HADGS 為 NaN） |
| `StatSaHadgs*`、`StatSlowStart*`、`StatBackfillProbe*` | 其他實驗元件的診斷（正典為 0） |
| footprint.csv | 一列摘要；第 11 欄＝控制器，16–19 欄＝bots／pods／補貨站／揀貨站，25 欄＝SKU（0-based） |

---

## 4. APA 7th 圖表規範（強制）

**所有交給使用者的數據表格與圖都必須服從 APA 7th**，包括 CSV 轉出的表、HTML 內的表、matplotlib 圖。論文與投影片用圖一律英文。

### 4.1 表格

```
Table 1                                        ← 粗體，靠左
Throughput and Efficiency by Policy            ← 斜體，Title Case
─────────────────────────────────────────────  ← 頂線
Policy   Bots   Items   Orders   Pile-on  ...   ← 表頭，置中或靠左
─────────────────────────────────────────────  ← 表頭下線
M1G      6      1242    574.9    3.26     ...
M4G-NS   6      1147    523.2    3.08     ...
─────────────────────────────────────────────  ← 底線
Note. 2 h, 10 seeds, 2 pick stations, ...       ← Note. 斜體，精簡
* p < .05. ** p < .01. *** p < .001.
```

規則：

1. **只用三條水平線**（頂線、表頭下線、底線），**不用直線、不用格線、不用底色**。
2. 表號粗體（**Table 1**），標題斜體 Title Case，放表格上方。
3. 表頭用英文，單位放括號：`EOR (kJ/order)`、`Turnover (s)`、`Station idle (%)`。
4. 數字靠右或小數點對齊；同一欄小數位數一致：
   - 計數取整數或 1 位；
   - 比率取 2～3 位；
   - 百分比取 1～2 位；
   - p 值不寫前導 0（`.03`）。
5. **表格內禁止出現**：emoji、箭頭（↑↓）、中文口語、粗體強調、顏色標示、「⚠️」「好／差」之類的判讀字、內部代號（`gc_mp2`、`s0`、檔名）、未定義縮寫。
6. 顯著性只用 `*` / `**` / `***`，定義放在 Note 最後一行。
7. **註解能不寫就不寫**；必須寫時放在 `Note.` 內，一至兩句，英文，精簡：
   - 一般註：場景（2 h, 10 seeds, 2 pick / 2 replenishment stations, 100 SKUs, backlog 100, 100 pods / 120 cells, pod capacity 100, 70% initial stock）；
   - 特定註：上標 a、b、c；
   - 機率註：`*`。
8. 縮寫在 Note 定義一次（例如 `EOR = energy per order`）；policy 名稱（M1G、M4G-NS、M4G、HGS-M5、HADGS）不需定義。
9. 比較表：差值欄寫 `Δ%`，正負號明示（`+23.3***`、`−7.7`），負號用 U+2212 `−`。

### 4.1.1 對照組相對百分比格式

當使用者要求將實驗組數值直接標示為相對於對照組的變化時，保留實驗組原始數值，並在括號內附上 Δ%：

```text
實驗組數值 (Δ%)
1147 (−7.6%)
```

計算固定為 `(實驗組 − 對照組) ÷ 對照組 × 100`。對照組必須是同一場景、同一 bots 數與同一 seed／seed 集合的 M1G；不可跨 bots 數、跨場景或跨不同正典版本比較。單一 seed 只報 Δ%，不附 p 值或顯著性星號。

表格外可補充指標方向：Items、Lines、Orders、Pile-on、Trips 通常以增加為正向；Trips/Orders、m/Line、EOR、Turnover 與 Station idle 通常以降低為正向。但表格中的正負號只表示數值變化，不直接寫成好／差。

### 4.2 圖（F1–F11，2026-09-17 使用者定案版）

沿用 `docs/defense material/figures/apa.py`：`apply_style()`、`furniture()`、`place_labels()`、`errorbar_segments()`、`check_axes()`、`save()`。**每一條都是硬規，出圖後必須用 Read 看圖逐條核對，不能只信程式跑完。**

| # | 規則 | 落實方式 |
|---|---|---|
| F1 | **APA 7th**：圖號粗體（**Figure 1**）、標題斜體 Title Case 置於圖上；sans serif（Arial）8–14 pt；無格線、無 3D、無陰影、無裝飾；只留左、下軸線，刻度朝外；可灰階閱讀（marker 形狀／線型／填色區分，不只靠顏色）；軸標籤英文附單位。 | `apa.apply_style()`＋`apa.furniture()` |
| F2 | **確定性工具**：一律用 matplotlib（或同等可重跑的程式）從 `stats.json`／CSV 產圖；禁止手繪、截圖拼貼、Excel 手調、AI 生圖。同一輸入必須產出同一張圖。 | 圖腳本進 `scripts/`，輸入路徑寫在腳本內 |
| F3 | **元件互不阻擋**：資料標籤不得壓到誤差線、marker、其他標籤、軸刻度；圖例不得壓到任何資料。 | 點標籤一律 `apa.place_labels(ax, points, obstacles)`（`errorbar_segments` 產生障礙物）；幾乎重合的點共用一個標籤 |
| F4 | **圖例放空曠區**：先量各角落／面板外的空白，圖例放最大空白處；多面板用 `fig.legend` 放到面板外（標題列右側或圖底）。圖例無外框。 | 出圖後看圖確認 |
| F5 | **Note 精簡**：圖上原則不放 Note；**若必要**（誤差線定義、星號意義）允許一至兩句英文 `Note.`，字少、不裁切、不與任何元素重疊。要強調的重點在回覆裡建議，不寫進圖。 | `apa.furniture(..., note="…")` 只在必要時傳；否則 `note=None` |
| F6 | **尺度可辨識、資料點落位正確**：資料點必須落在刻度對應的位置（y=10 的點不得看起來在 9 或 11）。禁止 matplotlib 的 offset text（`+1e4`）與科學記號偏移；禁止雙 y 軸；禁止手動平移資料；刻度密度要能讀出數值（相鄰刻度差為 1／2／5×10ⁿ）。 | `apa.check_axes(fig)` 會關掉 offset、確認所有資料在軸範圍內並回報刻度步距；出圖後挑一個已知數值的點對刻度核對 |
| F7 | **不留無用空白**：`ax.margins ≤ 0.10`；`save()` 用 `bbox_inches="tight"`；不為了塞圖例或標籤而放大軸範圖；不留空子圖。 | `apa.save()` 已內建 tight bbox |
| F8 | **圖內禁止**：中文、emoji、內部代號（gc_、s0、檔名、旗標名）、未定義縮寫、判讀文字（"better"、"collapse"）。 | 看圖核對 |
| F9 | **輸出**：PNG（≥300 dpi）＋PDF（`pdf.fonttype=42` 字型內嵌），存在實驗夾 `figures/`，並複製到 `docs/experiments/<主題>/figures/`。 | `apa.save()` |
| F11 | **不編號、主題式短標題**（2026-09-17）：圖上**不放「Figure N」**（投影片順序會變動；編號屬於放圖的文件），除非使用者明確要求。標題必須是**一看就知道數據主題**的短名（≤10 個英文字，例：*Fixed vs. Dynamic Exchange Rate: Seven Measures*），禁止詮釋性長句（結論、意義由使用者口頭講）。 | `apa.furniture(fig, None, "<短主題>")`；超過 10 字直接報錯 |
| F10 | **資料時效標記**：每張圖必附 `<檔名>.provenance.json`（實驗 id、run id、正典版本、產圖日期、來源檔）。當來源 run 被標為 `superseded`、或正典版本已升，`scripts/fig_stale_check.py` 會在圖旁寫 `STALE.md` 並列出原因；**過期圖不得再交付**，要重跑圖腳本或在交付時明說「此圖依 Canon vN，已過期」。 | `apa.save(fig, path, provenance=…)`；`python scripts/fig_stale_check.py` |

### 4.3 註解語言原則

| 情況 | 做法 |
|---|---|
| 表／圖本身 | 英文，無多餘文字 |
| 必要註解 | `Note.` 一至兩句英文；越短越好 |
| 需要中文說明的判讀 | **不放進表或圖**；寫在回覆、NOTES.md 或 MD 文件的表格外 |
| 使用者要自己後製 | 提供英文的 title／note 文字，另列在表格外讓他複製 |

### 4.4 交付檢查清單（見文末）

---

## 5. 精確計算管線（強制）

**任何平均、標準差、百分比差、p 值、信賴區間都不得心算或手抄，一律由 `scripts/stats_pipeline.py` 計算。** 回覆使用者時直接引用它的輸出，不自行重算或改寫數字。

### 5.1 管線流程

```
原始輸出（statistics.txt / stationstatistics.csv）
  → run_metrics：全精度 float64 讀入，不先四捨五入
  → numpy / scipy：平均、SD（ddof=1）、SE、95% t 信賴區間；
                   配對 t 檢定 scipy.stats.ttest_rel（雙尾、精確 p）、Cohen's dz
  → stats.xlsx：per_seed 原始數據＋excel_check 表（AVERAGE、STDEV.S、Δ%、T.TEST(…,2,1) 原生公式）
  → 背景啟動 Excel 重算 → 讀回 → 與 scipy 逐項比對（相對誤差 ≤ 1e-9）→ 任一不符即中止
  → 輸出 stats.json、results.csv、apa_tables.docx（三線表）
```

### 5.2 定義（固定，不得更改口徑）

| 量 | 定義 |
|---|---|
| Δ% | (mean_b − mean_a) ÷ mean_a × 100，b 為實驗組、a 為對照組 |
| 配對 | 同 seed 配對，差值 d = b − a |
| p | 雙尾配對 t 檢定；* < .05、** < .01、*** < .001 |
| 95% CI | mean ± t(0.975, n−1) × SE |
| dz | mean(d) ÷ SD(d) |
| identical / constant difference | 所有 seed 差值皆為 0／皆為同一非零值（無法做 t 檢定） |
| 1 seed | 只給 Δ%，不給 p 與星號 |
| 四捨五入 | 只在呈現時做，half-up（Decimal），計算全程不捨入 |

### 5.3 使用方式

| 情境 | 指令 |
|---|---|
| exp.py 管理的實驗 | `python scripts/exp.py table <id>`（內部呼叫管線） |
| 2026-09-15 以前 `out/` 內的舊資料 | `python scripts/stats_pipeline.py legacy <輸出資料夾> <spec.json>`，spec 格式見腳本 docstring |

輸出檔：

| 檔案 | 用途 |
|---|---|
| `stats.xlsx` | 每 seed 原始值＋Excel 公式驗證表；使用者可直接打開檢查 |
| `stats.json` | 全部精確數值（平均、SD、CI、Δ%、t、p、dz）與 Excel 驗證狀態 |
| `results.csv` | 呈現用摘要（已依第 5.4 節位數捨入） |
| `apa_tables.docx` | APA 7 三線表：Table 1 平均、Table 2 標準差、Table 3 配對 Δ%；英文、橫式頁面 |

`excel_check.status` 必須是 `passed` 才能回報；若 Excel 無法啟動（`not run`），回報時註明「僅 scipy 計算，未經 Excel 交叉驗證」。

### 5.4 呈現位數

| 指標 | 位數 |
|---|---|
| Items、Lines、Orders、Trips、Turnover (s) | 1 |
| m/line、Station idle (%)、Δ% | 2 |
| Pile-on、EOR (kJ/order)、p | 3 |
| Trips/order | 4 |

### 5.5 每批數據固定產出 CSV 組（使用者 2026-09-15 定案：「以後也要做 csv」）

`exp.py table` **每次自動**在實驗資料夾 `csv/` 產出下列 CSV（2026-09-16 起；不靠手動）。Policy 欄用 `experiment.json` 各 arm 的 `display` 欄位（書面名，見 1.6），未填則用 label。要放進 `docs/experiments/<日期>-<主題>/` 時直接複製 `csv/`。清單（UTF-8 BOM，表頭英文，內容依 4.1 規範，無中文、無代號、無判讀字）：

| 檔案 | 內容 |
|---|---|
| `table1_means.csv` | 各 policy × bots 的平均（12 欄） |
| `table2_sd.csv` | 標準差 |
| `table3_paired_delta_pct.csv` | 配對 Δ%＋星號 |
| `table3b_paired_p_values.csv` | 精確 p 值 |
| `table3c_paired_ci95_pct.csv` | Δ% 的 95% CI 上下界 |
| `table4_per_seed.csv` | 逐 seed 原始值 |
| `table5_solve_time_summary.csv`／`_per_seed.csv` | 訂單分配求解時間（`StatTimingOrderBatching*`）與加速倍率 |
| `stats_full.xlsx` | 複製自管線輸出，含 Excel 驗證表 |

回覆時附資料夾路徑與投影片用的英文標題／場景行／Note 文案。

---

## 附：交付檢查清單

- [ ] 表號、斜體標題
- [ ] 三條線，無格線、無直線
- [ ] 英文表頭含單位
- [ ] 小數位一致、負號 `−`
- [ ] 表內沒有 emoji、箭頭、中文、內部代號、判讀字
- [ ] Note 精簡英文，含場景與顯著性定義
- [ ] 12 欄順序與第 2 節一致（主表）
- [ ] 圖可灰階閱讀、無格線、sans serif
- [ ] CSV 組（5.5）已產出
- [ ] 所有數字來自 `stats_pipeline.py`（stats.json／results.csv），`excel_check` 為 passed

## 6. 回報格式（2026-09-18 使用者定案）

任何實驗結果回報給使用者時：

1. **一律整理成列表／表格**貼在回覆裡，不可只給檔案路徑、不可貼原始 CSV 文字、不可貼 stats.json。
2. **顯著值必須標記**：配對 Δ% 後接 \*／\*\*／\*\*\*（p<.05／.01／.001），不顯著者不加星號；主要結論的數字用**粗體**。
3. **排序**：表格列依自變數（λ、N、站數、seed）遞增，或依效應大小排序；欄位依 REPORTING-STANDARD §2 的 12 欄順序。禁止照 registry 或檔案順序原樣倒出。
4. **視覺優化**：表頭一行、單位在表頭、小數位一致（Δ% 一位、比率兩位、公尺兩位）、正負號用 − 不用 -、千分位逗號；同一表內不混用兩種分母。
5. **表下三句**：結論（含方向與幅度）、可信度（seeds、檢定、excel_check）、下一步。
6. 圖同樣適用：未依 F1–F11 排序與整理的圖不得交付。

違反任一條即視為未回報。
