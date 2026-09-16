# Aesop-RAWSim — RAWSim-O 擴充版：考慮訂單拆分之 RMFS 線上聯合最佳化

本分支是 [RAWSim-O](https://github.com/merschformann/RAWSim-O) 的研究擴充版，封版於 2026-09-17，對應碩士論文
**「考慮訂單拆分之機器人移動履行系統線上聯合最佳化」**（Online Joint Optimisation of Robotic Mobile Fulfilment Systems With Order Splitting）。

分支只包含**模擬器原始碼、正典實驗組態與模型設計文件**；實驗輸出、分析腳本與個人工作檔案皆不在此。目的是讓後續研究者可以直接以此為基礎建置、重現正典結果，並開發新的變體。

上游原始 README 見 [README-upstream.md](README-upstream.md)。

---

## 1. 這個版本新增了什麼

| 政策（論文名） | 類別 | 程式 | 一句話 |
|---|---|---|---|
| **M1G** | 外部基準（Jiao） | `RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs` | 線上聯合 OA/PS/TA 的 MILP，一張訂單整批指派至單一站，權重目標式 (1, −40, 1000) |
| **HADGS** | 外部基準 | `HADGSManager.cs` | M1G 的貪婪啟發式，大規模對照 |
| **M4G** | 本研究主模型 | `M4GManager.cs`＋`M4GPricing.cs` | 商品行層級的線上聯合 MILP，容許跨期／跨站拆單；目標式以 Dinkelbach 自校準的「距離／線」交換率 λ 計價，無人工權重 |
| **M4G-NS** | 消融：不拆單 | `m4g.xconf`＋`OrderAtomicNoSplit`＋`OrderAtomicCanonPrices` | 與 M4G 只差「承諾單位」（整單 vs 線） |
| **M4G-WS / WS₀ / WS-NS** | 消融：固定權重 | `LegacyObjective` 系列旗標 | Jiao 的權重目標式放在 M4G 可行域上；WS₀ 拿掉距離項 |
| **HGS-M5** | 可擴展啟發式 | `GreedyM5Manager.cs` | M4G 的邊際線貪婪鏡像，同一目標式與價格；大規模交付物 |

其他歷史模型（SplitM1G、M2e 系列、M3G、PVGS、GreedyM4G、M5-NS…）保留在程式碼中作為演化紀錄，正典實驗不使用。

### 拆單資料層
`Order.CreateSplitChild`／demand ledger（`GetRemainingDemand`、`RemainingPositions`、`IsFullyClaimed`）與 `SplitConsolidationLogger` 是所有拆單策略共用的基礎設施；母單於全部子單完成時才計入 `StatOverallOrdersHandled`。

---

## 2. 建置

- Visual Studio 2022（或 MSBuild 17）、.NET Framework 4.8、**Gurobi 12.0（win64）**。
- **只能用 x64 Release**（Gurobi 只有 win64；x86 執行期會失敗）：

```powershell
msbuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release
```

- Gurobi 安裝路徑預設 `C:\gurobi1203\win64`，可用 MSBuild 屬性覆寫：`/p:GurobiHome=D:\gurobi1203\win64`。Gurobi 授權須自行取得（本分支不含任何授權檔或金鑰）。
- C# 7.3（legacy csproj）：新增檔案要手動加 `<Compile Include>`。

---

## 3. 正典實驗組態（`Material/Instances/Canon/`）

所有論文數據都由這個資料夾的檔案產生；`VERSION.json` 記錄正典版本（封版時為 **v2**），`provenance.json` 記錄每個檔案的來源。

| 規模 | 佈局 `.xlayo` | 設定 `.xsett` | 控制器 `.xconf` |
|---|---|---|---|
| small | `small_6b_2p_2r`、`small_10b_2p_2r`（6／10 台、2 揀貨站、2 補貨站、100 SKU） | `small_fill100_mu100_2h_inv70`（Fill 模式、backlog 100、2 h、庫存 70%） | `m1g`、`m4g`、`m4g_ns`、`hgs_m5` |
| large | `large_45b_12p_6r`（45 台、12 揀貨站） | `large_fill200_mu1000_2h_inv70` | `hadgs_aligned`、`hgs_m5_k10` |

規則：**檔名 = 檔內 `Name`**；做新實驗時複製正典檔、只改一個變數、誠實命名。M4G／M4G-NS／M5 的正典旗標見 `docs/2026-09-17-methodology-audit.md` §1–4。

執行單場模擬（CLI）：

```powershell
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe <layout.xlayo> <setting.xsett> <controller.xconf> <outputDir> <seed>
```

輸出資料夾內 `statistics.txt` 為權威 KPI 來源（`InstanceStatistics.cs` KPI Summary 區塊）。

---

## 4. 重要指標（程式來源）

| 指標 | 來源 |
|---|---|
| Items / Lines / Orders handled | `InstanceEvents.cs`：每次揀取、每條線關閉、每張**母單**合併完成 |
| Trips | `StatOutputStationArrivals` |
| Pile-on | `StatSystemOrderPileOn` = Orders ÷ Trips（每趟到站完成的訂單數） |
| EOR (kJ/order) | `KPI_EOR` = 機械能 E1–E5（`BotNormal.cs`、`Metrics/EnergyConsumption.cs`）÷ Orders |
| Turnover | `StatMedianTurnoverTime`（母單下單→合併） |
| Station idle | `stationstatistics.csv` IdleTime ÷ UpTime |

已知限制（詳見 `docs/2026-09-17-methodology-audit.md` §6）：目標式只計揀貨去程曼哈頓距離；能耗模型每段路徑自靜止起步，故與距離高度共線；合併／包裝作業只建模容量（`PackingBuffer`），不建模時間。

---

## 5. 文件地圖（`docs/`）

- `2026-09-17-methodology-audit.md` — **先讀這份**：每個政策的實作與論文主張逐條核對、指標程式碼稽核、需揭露事項。
- `2026-07-31-m4g-formulation.md`、`2026-07-31-m4g-explained.md`、`2026-08-25-m4g-model*.html`、`2026-08-06-m4g-model-explained.html` — M4G 數學模型與圖解。
- `2026-08-11-m1g-model.html`、`2026-08-11-m1g-vs-m4g.html` — M1G 模型與兩者對照。
- `2026-08-07-delta-dose-response-and-dinkelbach.md`、`2026-09-01-lambda-semantics-and-two-stage-pricing.md` — δ 與 λ 的語意。
- `hadgs.md` — HADGS 運作說明。
- `README-M4G.md` — M4G 登基時的版本說明（2026-07-31，部分路徑已過時）。
- `specs/` — 各模型的設計規格（含已淘汰模型，供演化脈絡）。程式碼註解中引用的 `docs/plans/…` 為實作計畫，未收錄。

---

## 6. 開發約定

- `M1GManager.cs`、`HADGSManager.cs` 為外部基準，**不要修改**（既有旗標式擴充預設全關、逐位相同）；新模型以繼承／鏡像另開檔案。新 Configuration 繼承 `M1GConfiguration` 可讓引擎的型別檢查自動通過。
- 改動任何會影響正典行為的程式碼後，先跑「守門」：同一組態、同一 seed，與改動前的 `statistics.txt` 逐行比對（僅允許 `StatTiming*`、`StatRealTimeUsed`、`StatMaxMemoryUsed` 不同）。
- 佈局產生器上限：`maxNrOfStationsWestOrEast = NrHorizontalAisles/2`、`maxNrOfStationsNorthOrSouth = NrVerticalAisles/2`。

---

## 7. 授權

沿用上游 RAWSim-O 之 [LICENSE](LICENSE)；第三方元件見 [ThirdPartyLicenses.txt](ThirdPartyLicenses.txt)。Gurobi 為商業軟體，需另行授權。
