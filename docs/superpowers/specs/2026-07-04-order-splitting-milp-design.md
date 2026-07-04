# Spec 2：SplitM1G——拆單版 M1G MILP 聯合模型設計

日期：2026-07-04
狀態：已核可（設計對話中逐項確認）
前置：Spec 1 enabler（`2026-07-02-order-splitting-consolidation-enabler-design.md`，branch 6/24，569afef..d04ba44）

## 1. 定位與範圍

新模組 `SplitM1GManager`（M1G 家族），把原 M1G MILP 的「每張 order 至多指派一站」約束
（shi2，`M1GManager.cs:707`）放鬆為**單件級數量分配**：order 的每個 SKU 的每一件都可以
分到不同 picking station（cross-station）或不同決策期（cross-time）。同一顆 Gurobi
wrapper（`RAWSimO.SolverWrappers.LinearModel`，`SolverType.Gurobi`）、同一 license、
同一 solve 觸發時機（`DecideAboutPendingOrders`）。

**模組邊界（硬約束，沿用 Spec 1 定案）**：原版 `M1GManager.cs`、`HADGSManager.cs` 一行不動。
消融階梯：

| 代號 | 方法 | 說明 |
|---|---|---|
| M0 | 原 M1G | baseline，不拆單 |
| M1 | SplitM1G（CrossTime=false） | 本期 all-or-nothing，可跨站拆 |
| M2 | SplitM1G（CrossTime=true） | 可部分指派，殘量留 backlog 跨期拆 |
| H  | SplitOrderManager（Spec 1） | heuristic 拆單對照組 |

**不包含**：packing station 建模（Spec 1 定案：不建模）、拆單懲罰（拆單免費成本假設）、
拆單上限（本 spec 定案：不設限，上限自然是站數×期數）。

## 2. 設計決策記錄（本次 brainstorming 逐項確認）

1. **站容量語意 = 純 slot 制**：引擎 `OutputStation` 是 order-slot 制（每個 allocate 的
   child 佔 1 slot），MILP 的容量約束必須與引擎一致，否則 `AllocateOrder` 失敗。
   不採 split.pdf 的 item-capacity（會與引擎衝突）。
2. **指派獎勵 = 每件 unit**（w2'·Σq）：拆與不拆完全中性（符合拆單免費假設）、M2 部分
   指派有比例誘因。不掛 y[o,s]（拆兩站雙倍獎勵 → 系統性偏好拆單，污染消融歸因）、
   不掛 z[o]（M2 部分指派零獎勵 → M2 形同虛設）。
3. **不設拆單上限**：無 Σs y[o,s] ≤ K 約束。理論最純粹；代價是失去 K=1 對照旋鈕
   （消融以 M0 承擔）。
4. **模型方案 A**：q 變數只到 station 層；pod 層分配（Ziops）沿用 M1G 解後貪婪抽取。
   不採 ziops 進模型（方案 B，變數 |I|×|O|×|P|×|S| 爆炸，原作者已因規模註解棄用）、
   不採兩階段解耦（方案 C，削弱 OA+PS+TA 聯合決策賣點且混淆消融歸因）。
5. **w2' default = −40**：直接沿用原 w2 值換單位。相對原版會偏好多件大單——屬刻意
   設計（每件價值相同）；config 可調（`UnitRewardWeight`），可做參數掃描。
6. **us[s] 上限動態化**：原 M1G 用 `VariableCollection(…, 0, 6, …)` 硬編碼上限 6
   （= small 站容量）。新模型改為每站上限 `Cs[s]`（或集合上限取 max Cs[s]），避免
   大容量 instance 的隱性 bug。
7. **M2 的 order 過濾放寬**：M1 模式維持 enabler parity（殘量全數 stock-feasible 才進
   模型，雙層過濾 `GetActualStock` → `IsAvailabletoPiSKU` 皆以殘量計）；M2 模式放寬為
   「至少一個 SKU 在 PiSKU（模型內 pod）有可用量」即可進模型（shi5' 自動保證只指派
   可行部分）。

## 3. 數學模型

### 3.1 集合與參數（每次 solve 快照，沿用 M1G Initialize 邏輯，改以殘量計）

- `O`：pending orders（過濾規則見決策 7）
- `I_o`：order o 殘量 > 0 的 SKU 集合（`Order.RemainingPositions`）
- `r[o,i]`：殘量 = `GetRemainingDemand(i)`（母單需求 − 已被 child 認領）
- `S`：有空 slot 的站（`GenerateCs()`，`Cs[s]` = 剩餘 slots）
- `P`（= Pa ∪ Pb）、`R`（= Ra ∪ Rb）、`stock[p,i]` = `CountAvailable`：全部沿用
- 成本係數：`M1GPodStationCost` / `M1GBotPodCost`（含 starve-aware gate 與
  `PodStationExtraCost` / `PrepareDecisionExtras` hooks）沿用
- `Od` 急單機制：`GenerateOd` 沿用，惟 SKU 覆蓋檢查改以殘量 `r[o,i]` 計

### 3.2 變數

| 變數 | 型別 | 意義 |
|---|---|---|
| `xps[p,s]` | binary | pod p 指派站 s（保留） |
| `yrp[r,p]` | binary | bot r 指派 pod p（保留） |
| `us[s]` | integer 0..Cs[s] | 站 s 未填滿的 slots（保留，上限動態化） |
| `dops[o,p,s]` | binary | order-pod-station linking（保留） |
| `q[o,i,s]` | integer ≥ 0 | **新**：order o 的 SKU i 在站 s 揀幾件 |
| `y[o,s]` | binary | **新**：o 在 s 產生 child 的指示變數 |
| `z[o]` | binary | **新**：僅 M1 模式建立（all-or-nothing 開關） |

原 `yos`/`yaos` 雙層變數移除，由 `q` + `y` 取代。

實作註記：`q` 的 per-variable 上限由 link-up 約束（q ≤ r·y）隱含，VariableCollection
的集合上限取 max r[o,i] 即可。

### 3.3 約束

```
(link-up)    q[o,i,s] ≤ r[o,i] · y[o,s]                  ∀ o, i∈I_o, s
(link-down)  y[o,s] ≤ Σi q[o,i,s]                        ∀ o, s        （禁止空 child）
(shi4')      Σo y[o,s] = Cs[s] − us[s]                   ∀ s           （純 slot 制）
(shi5')      Σo q[o,i,s] ≤ Σp stock[p,i] · xps[p,s]      ∀ i, s        （庫存可行性）
(M1 模式)    Σs q[o,i,s] = r[o,i] · z[o]                 ∀ o, i∈I_o    （全指派或不指派，可跨站）
(M2 模式)    Σs q[o,i,s] ≤ r[o,i]                        ∀ o, i∈I_o    （可部分，殘量留 backlog）
(shi12')     2 · dops[o,p,s] ≤ y[o,s] + xps[p,s]         （原式 yaos → y）
```

以下逐字保留：shi6（pod 至多一站）、shi7/shi11（繼承在途 pod/bot 固定 =1）、
shi8（pod 需 bot）、shi9/shi10（pod-bot 一對一）、shi13（新 pod 至少服務一單）。
shi2/shi3 移除（shi2 即被放鬆的核心；shi3 隨 yos/yaos 雙層一併消失）。

### 3.4 目標函數

```
min  w1 · ( Σ xps[p,s]·PodStationCost(p,s)  +  Σ yrp[r,p]·BotPodCost(r,p) )
   + w2' · Σ q[o,i,s]
   + w3  · Σ us[s]
```

- `w1 = 1`、`w3 = 1000`：沿用原 M1G
- `w2' = UnitRewardWeight`，default −40（決策 5）
- `Ra.Count == 0` 時退化式（省略 yrp 項）沿用原 M1G 的雙分支寫法

### 3.5 規模評估

small 情境一次 solve：|O|≈67、|I_o|≈1–3、|S|=2 → q ≈ 268 個整數變數、y ≈ 134、
z ≈ 67，相對原模型（xps≈190、dops 數百）增量可忽略；線上頻繁 solve 無風險。

## 4. 架構

### 4.1 Config：`SplitM1GConfiguration : M1GConfiguration`

- 欄位：`bool CrossTime = true`（false=M1 / true=M2）、`double UnitRewardWeight = -40`
- token：`OBSPLITM1G`；新 `OrderBatchingMethodType.SplitM1G` enum 值 +
  `[XmlInclude(typeof(SplitM1GConfiguration))]` + `Controller.cs` case
- **繼承 M1GConfiguration 是關鍵**：引擎所有 M1G type-routing 皆為 `is M1GConfiguration`
  檢查（`BotTask.cs:247`、`BalancedBotManager.cs:208/264/402`、
  `BotManagerPodSelection.cs:1640/1706`），子類別自動通過（`SAM1GConfiguration` 先例）
  → **本 spec 引擎層零改動**（對比 Spec 1 的三處引擎偏差）。

### 4.2 Manager：`SplitM1GManager : M1GManager`（新檔案）

- override `DecideAboutPendingOrders`（該方法在 override 鏈上，可再 override）
- base 的 `Initialize` / `solve` 非 virtual → 不動 base，另寫 split 版私有方法
  （`InitializeSplit` / `SolveSplit`）
- 重用 base 的 protected/public helpers：距離估計（`EstimateBotPodDistance` /
  `EstimatePodStationDistance`）、`GeneratePiSKU` / `GeneratePs` / `GenerateCs` /
  `GenerateOd`（殘量版可能需新寫或參數化）、starve-aware hooks
- 解後流程沿用 M1G 骨架：`JustRegisterItem` 標記、yrp → `ClaimPod` + `BottoPod` +
  `RegisterInboundPod`、Ziops 貪婪抽取（`_Ziops` queue）、unused-dops pod 釋放——
  惟餵入對象從母單改為 child（見 §5）
- 新 .cs 檔記得加 `<Compile Include>`（net48 舊式 csproj）

### 4.3 消融安全性

- 原 M1G / HADGS / SplitHeuristic 組態的行為完全不變（不改原檔＋引擎零改動）
- 回歸判準：M1G baseline xconf 前後應 bit-identical（統計等價 <2% 為 WHCA* 牆鐘
  預算下的退階判準）

## 5. 解碼管線（重用 Spec 1 enabler）

對每個 order o（依解中 y/q 值）：

1. 收集 `{s : y[o,s]=1}` 與各站數量 dict `{i ↦ q[o,i,s]}`
2. **未拆快路徑**（enabler parity）：僅一站、全數指派、且 o 非 split parent →
   直接 `AllocateOrder(o, s)`，不包 child
3. 否則每站依序：`CreateSplitChild(quantities)`（帳本原子扣帳，重複揀貨防護）→
   `child.ID = idoforder++`（沿用 enabler `SplitOrderManager.cs:92` 的 id 發放方式）→
   `ResourceManager.TransferExtractRequests(o, child)` → `AllocateOrder(child, s)`；
   首拆設 `o.TimeStampSubmit`
4. `o.IsFullyClaimed` → `_pendingOrders.Remove(o)` + `TakeAvailableOrder(o)`；
   M2 殘量自然留在 backlog，老化沿用 Timestay 機制（母單保留原 TimePlaced/DueTime，
   殘量自動升隊頭）
5. Ziops 貪婪抽取與 `JustRegisterItem` 以 **child**（或快路徑母單）的 positions 迭代——
   原程式碼以 `order.Positions` 迭代，child 的 positions 即其分得數量，邏輯可直接重用
6. consolidation 記帳走 enabler 既有路徑（`OutputStation.RemoveAnyCompletedOrder` →
   `NotifyChildCompleted` → 母單 KPI）；`splitorders.csv` logger 從 `SplitOrderManager`
   抽成共用 helper 或平行複製（實作時取最小改動，兩者皆可）

## 6. 驗證與實驗

### 6.1 單元測試（RAWSimO.Tests）

把「MILP 解 → per-station quantity dicts」的解碼邏輯抽成不碰 Gurobi 的純函式，測：

- M1 all-or-nothing 性質（z=0 → 全零；z=1 → 各 SKU 總和 = 殘量）
- M2 部分指派（Σs q ≤ r，殘量守恆：認領 + 剩餘 = 原殘量）
- 空 child 禁止（y=1 ⟺ Σq ≥ 1 的解碼端防衛）
- 快路徑判定（單站全數 → 不建 child）

### 6.2 回歸

- 原 M1G xconf：前後 bit-identical（見 §4.3）
- HADGS 標準 xconf、SplitHeuristic xconf：不受影響（抽 logger helper 時需回歸
  SplitHeuristic 煙霧數字 599/608）

### 6.3 煙霧

- 新 xconf：`Material/Instances/CoreBenchmark/small/split_milp_m1.xconf` /
  `split_milp_m2.xconf`——與 M1G baseline xconf 僅 `<Name>` 與 OB 段不同
- small seed0 7200s：handled 數合理、`splitorders.csv` 有拆單列、無卡單
  （in-flight 檢查）、`m1g_decision_log.csv` 式診斷欄位可沿用（改記 q/y/z 計數）

### 6.4 實驗階梯（論文）

M0 → M1 → M2（+H 對照），small（20 bots）與高壓（45 bots）；M1/M2 行為差異
需高壓實證（Spec 1 已證 small 低壓下拆單模式聚合數字不可區分）。

## 7. 風險與備忘

- **shi4' 等式 + 高 w3 在拆單下的行為**:原 M1G 用等式強迫填滿 slots；拆單後模型可以
  「為填滿 slot 而碎拆」——這正是 per-unit 獎勵中性設計下由 w3 驅動的填滿行為，屬預期；
  若實驗發現病態碎拆，調 w2'/w3 比例（config 已可調 w2'）。
- **GenerateOd 殘量化**：`Timestay` 寫入 order 欄位為共享狀態，SplitM1G 與原 M1G 不會
  同時運行（單一 OB config），無衝突。
- **BotTask.cs:247 gate 語意**：實作時驗證該 gate（ClaimPod）對 M1G 家族的實際行為
  與 SplitM1G 解後自行 ClaimPod 的路徑不重複計數（M1G 在 solve 內 ClaimPod，
  Prepare 端 gate 對 M1GConfiguration 的分支行為需煙霧確認）。
- **id 發放**：child ID 發放方式沿用 enabler（`idoforder++` 語意）；實作時確認與
  M1G 路徑下的 order id 空間不衝突。
- **StatOverall KPI**：child 不進 `NotifyOrderCompleted`，母單以最後 child 的站觸發
  ——enabler 已驗證全鏈路，無新增 seam。
