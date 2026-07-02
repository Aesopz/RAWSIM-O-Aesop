# 拆單（Order Splitting）＋ Consolidation Enabler 設計

日期：2026-07-02
狀態：設計定案（brainstorming 完成，待實作計畫）
範圍：**Spec 1（enabler）**。MILP 聯合模型（放鬆 shi2 的 `q[o,i,s]`）另立 **Spec 2**。

## 1. 論文定位與背景

融合兩篇 source paper，補其交集空白：

| | split.pdf（Xie et al. 2021, EJOR 288）| online.pdf（Jiao et al. 2026, Omega 138）| 本論文 |
|---|---|---|---|
| 決策範圍 | POA+PPS | OA+PS+TA（M1G）| OA+PS+TA |
| 拆單 | 有（首篇）| 無（`shi2`：每單至多一站）| 有 |
| SKU 數量假設 | 每 order line 恰 1 件 | — | **放寬：line 可複數件，且同一 SKU 的數量可拆** |
| 線上性 | 逐期快照 | 逐期快照 MILP | 逐期快照 MILP（沿用 M1G rolling horizon）|

貢獻敘事：
1. 首個把 order splitting 帶進**線上 OA+PS+TA 聯合決策**的模型。
2. 放寬 split.pdf 的 one-unit-per-line 假設：拆單粒度細至**單件級**（同一 SKU 的複數件可拆到不同站／不同期）。
3. 兩種拆單模式（對應 split.pdf 的兩個變體、深化到數量維度）：
   - **cross-station split（M1）**：本期內把訂單拆到不同站（per-epoch all-or-nothing）。
   - **cross-time split（M2）**：可部分指派，殘量留 backlog 供後續期再拆（同站或異站）。

## 2. 假設

- **拆單免費成本**：僅以 picking station 為系統末端接口，不考慮下游 packing 負載。與 split.pdf「忽略 consolidation 時間」假設一致。
- **不建模 packing station**：無服務時間、無緩衝、無 East/West 分側、**無同側限制**（2026-07-02 定案，完全取代先前 East/West 單服務器＋5s/10s＋78 緩衝設計）。
- 母訂單完成 = 全部子單揀畢（consolidation 只是記帳），完成時間 = max(各子單完成時間)。
- 子單建立後不可取消（沿 RAWSimO 語意）。
- 補貨/放回流程不動。

## 3. 資料模型：child Order ＋ 需求帳本

### 3.1 child Order（沿用方案 A）

拆單產生**真正的 child `Order` 物件**（`RAWSimO.Core/Items/Order.cs`）：

- child 持母單的部分 (SKU, 數量)（`AddPosition` 建 positions），走既有 `AllocateOrder` → `ExtractRequest` → 揀取管線**零改動**；`OutputStation` 對 child 無感。
- child **繼承**母單 `TimePlaced` / `TimeStamp` / `DueTime`（tie-breaker、統計語意一致）。
- 母單新增欄位：`Children`（list）、`Parent`（child 端反向參照）、`IsSplitChild` / `IsSplitParent` 判別。
- 未拆訂單完全照舊（單一 Order 直接 allocate，不建 child）。

### 3.2 需求帳本（demand ledger）——避免重複揀貨的核心

母單每個 SKU 維護三個量：

| 量 | 意義 |
|---|---|
| 總需求 | 原始訂單量（既有 `_quantities`）|
| 已認領 | 已拆給 children 的量（新增）|
| 已完成 | children 已揀畢回報的量（新增彙總）|

- **剩餘量 = 總需求 − 已認領**。決策層（heuristic / 未來 MILP）每期快照**只看剩餘量**；in-flight children 不在 backlog → 結構上不可能重複指派。
- **扣帳原子點 = child 建立**：solve/拆單決定 `(station, SKU, qty)` 組合後，同一步建 child、扣「已認領」、child 立即 `AllocateOrder`。無時間窗漏洞。
- 母單本體**留在 backlog** 承載殘量（M2）；殘量為 0 時離開 backlog（但尚未「完成」，等 children 揀畢）。

### 3.3 老化優先權：沿用既有 Timestay 機制，不另設 aging

既有機制（已驗證存在）：

- M1G（`M1GManager.cs:382-399, 636-638`）：`Timestay = DueTime − 已等待時間`；`Timestay < 30min` 進急單集 `Od`；急單數超過總站容量時 MILP 只考慮急單。
- HADGS（`HADGSManager.cs:413-430, 759, 1033-1042`）：全體 pending 依 `Timestay` 升冪編 `sequence`，挑單永遠先挑 `sequence` 最小；急單夠多時縮候選池到 `Od`。

母單保留原始 `TimePlaced`/`DueTime` 留在 backlog ⇒ M2 拆一半的殘量等越久 `Timestay` 越小，**自動**升隊頭、進急單集。cross-time 長尾滯留風險由此機制吸收，不新增殘量 aging。

## 4. 完成語意與 KPI

### 4.1 完成路徑改動

現況：`OutputStation.RemoveAnyCompletedOrder`（`OutputStation.cs:331-352`）→ `ItemManager.CompleteOrder(order)` ＋ `Instance.NotifyOrderCompleted(order, station)`（`InstanceEvents.cs:108-129`，進 `StatOverallOrdersHandled`、turnover/throughput/lateness）。

改動原則：**站台層照舊、KPI 層以母單計**。

- child 在站上完成：站台本地流程照舊（釋放容量 slot、`BlockedUntil`、`StatNumOrdersFinished`、`ItemManager.CompleteOrder` 內部清理）。
- child 完成**不**觸發 `NotifyOrderCompleted`（否則 KPI 重複計數）；改通知母單記帳（該 child 的量記入「已完成」）。
- 母單「已完成 = 總需求 且 全部 children 完成」→ 以**最後一個 child 的站**觸發 `NotifyOrderCompleted(parent, lastStation)`。母單 cycle time = 到達 → 最後 child 完成，天然涵蓋 consolidation 等待。
- 未拆訂單路徑不變。

### 4.2 新增統計

- 拆單率（母單被拆的比例）、平均拆成幾份、cross-station vs cross-time 次數。
- consolidation 等待 = 母單最後 child 完成 − 最先 child 完成（分布）。
- 既有 KPI（throughput / turnover / lateness / starvation）一律以母單計。

## 5. Heuristic 拆單器（Spec 1 交付物，非論文貢獻）

目的：跨通管線驗證（資料模型＋帳本＋consolidation 記帳），之後被 Spec 2 的 MILP 取代。

- 介面：`ISplitter`（或 OrderManager 內掛鉤）：輸入（母單剩餘量、站容量、pod 庫存快照）→ 輸出 `(station, SKU, qty)` 組合清單。
- 預設實作：貪婪版——依站台既有 pod 覆蓋（Pod-Match 精神）把 line/數量分給覆蓋最好的站；塞不下的量留殘（即 M2 語意）。
- Config 開關：`OrderBatchingConfiguration` 新增拆單啟用旗標＋模式（Off / M1 / M2）。**Off = 零行為改變**（回歸保證）。

## 6. 風險與對策

| 風險 | 對策 |
|---|---|
| child 因 pod 缺貨卡住 → 母單永久不完成 | 拆單只對 stock-feasible 的量做（決策當下庫存可行性檢查）；殘餘風險（貨被他站先揀走）記入 Spec 2 的庫存約束；必要時加 stock claim 保留 |
| `ItemManager.CompleteOrder`/`NotifyOrderCompleted` 記帳漏、KPI 重複計 | §4.1 的 gating：child 不進 KPI 事件；單元測試覆蓋 |
| M2 殘量長尾滯留 | 沿用 Timestay 急單機制（§3.3），實驗監測母單 cycle time 分布 |
| 拆單 Off 時行為漂移 | 回歸測試：Off 之下 small/large 標準 case 結果與 baseline 位元級或統計等價 |

## 7. 測試

1. **單元測試**：帳本（扣帳/回報/剩餘量）、consolidation 判定（全 children 完成才觸發）、KPI 不重複計數、child 繼承時間戳。
2. **煙霧測試**：small instance（20 bots）＋貪婪拆單器，跑通 7200s，母單全數完成、無卡單。
3. **回歸測試**：拆單 Off，small/large 標準 case（HADGS＋WHCA*n）與現行 baseline 比對。

## 8. Spec 2 預告（另立文件）

M1G MILP 改動：移除 `shi2`（`M1GManager.cs:707`）；`q[o,i,s] ∈ Z≥0` 數量分配變數；M1 = `Σs q = 剩餘量 or 0`、M2 = `Σs q ≤ 剩餘量`；庫存可行性 `Σp x[p,s]·stock[p,i] ≥ Σo q[o,i,s]`；站容量改 item capacity；目標函數不加拆單懲罰（拆單免費）。實驗階梯：M0（原 M1G）→ M1 → M2。
