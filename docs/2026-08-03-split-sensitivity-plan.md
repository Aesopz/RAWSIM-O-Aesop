# 拆單 vs 不拆單：線上模型對照與敏感度分析（實作計畫）

日期：2026-08-03
狀態：**已備妥，尚未執行**（使用者指示先處理啟發式算法）
執行方式：`run_sens2.cmd`（已建立於 repo 根目錄，可直接啟動）

---

## 0. 這份計畫要回答的問題

**拆單能力本身值多少？在什麼條件下值得？**

不是「M4G 比 M1G 好多少」——那個比較混雜了目標式、模型結構、拆單三件事。這裡只問一件事：**在其餘一切相同的前提下，允許拆單與禁止拆單的差別。**

---

## 1. 對照設計

### 主軸：M4G vs M1G-a，單一變數

| | 組態檔 | `ForbidSplitting` |
|---|---|---|
| 拆單 | `m4g.xconf` | `false` |
| 不拆單 | `m1g_a.xconf` | `true` |

**已查證（2026-08-03）**：兩檔的唯一行為差異就是這個旗標。`m1g_a.xconf` 沒有 `<LegacyObjective>` 元素，取程式碼預設 `false`（`MethodConfigurationsOB.cs:1771`），與 `m4g.xconf` 的顯式 `false` 等價。λ/μ/δ/ρ/ε 定價、EPR、增量估值、Dinkelbach、pod-tier 全部相同。

**M1G 另列為外部文獻基準**，不進對照表——它是獨立實作，帶有待辦池截斷、槽位等式、無 EPR 等五項結構差異（見 `docs/2026-08-02-m1g-vs-m1g-split.md` §4），不是單變數對照。

### 不做 2×2

使用者已定案：不補「M4G 結構 + `ForbidSplitting=true` + `LegacyObjective=true`」那一格。舊目標式下的拆單行為已由 `m1g_split` 記錄（`docs/2026-08-02-m1g-vs-m1g-split.md`），不再擴充。

---

## 2. 基準點與三個敏感度軸

**基準點**：10 車 / 2 站 / o100 / inv70 / **2 小時（7200 秒）**

時長一律 2 小時（使用者 2026-08-03 定案）。注意這與 memory 中 `feedback_default_sim_duration` 記載的「4h 預設」不同——那是 M1G/IC 那條線的舊約定，M4G 這條線的所有既有基準（`dink_s0`~`s4`、`m1ga2_s0`）都是 2h。

單因子變動（OFAT），每點 M4G 與 M1G-a 各跑 seed 0、1：

| 軸 | 點 | 機制假說 |
|---|---|---|
| **車數** | 6 / 8 / **10** / 12 | 車少 → 行走成為瓶頸 → 拆單的共乘效益放大；車多 → 站台飽和 → 效益消失。記憶已確立「壓力軸是 bots/station 比」 |
| **訂單池** | 50 / 75 / **100** | 池小 → 沒有東西可合併；池大 → 可合併對象多。倒 U 假說（記憶：backlog 對拆單效果 o100→o200 為峰、o300 反轉）在此範圍內只驗上升段 |
| **庫存** | **70%** / 50% | 庫存低 → 單一貨架帶的品項少 → 湊齊整單的機率低 → 拆單價值上升。這是拆單機制最直接的因果軸 |

---

## 3. 執行清單（25 場）

已完成 1 場：`sens_b10_a_s0`（M1G-a 基準 seed 0，新 binary）。

剩餘 24 場，`run_sens2.cmd` 內已排好，順序為 車數 → 訂單池 → 庫存，每軸結束寫一個旗標：

| 階段 | 場次 | 旗標 |
|---|---|---|
| 基準 M1G-a seed 1 + 車數 6/8/12 | 13 | `out/sens_bots_done.flag` |
| 訂單池 50/75 | 8 | `out/sens_orders_done.flag` |
| 庫存 50% | 4 | `out/sens_done.flag` |

輸出目錄命名：`out/sens_<point>_<arm>_s<seed>`，`arm` 為 `m4g` 或 `a`。

既有可直接引用、不需重跑的基準資料：

| 點 | M4G | M1G-a |
|---|---|---|
| b10 / o100 / inv70 | `dink_s0`、`dink_s1` | `sens_b10_a_s0`、待跑 s1 |

預估時間：約 3.5 小時（模型重用優化後，單場約 8~9 分鐘）。

---

## 4. 先決條件：漂移檢查

批次第一場（`sens_b10_a_s1`）跑完後，**必須先把 `sens_b10_a_s0` 與舊 binary 的 `m1ga2_s0` 逐位比對**。

理由：`ForbidSplitting=true` 會啟用 V7a/V7b/V8/V8g/B8/B9 這幾條限制式，而模型重用重構（一次建模 + `Reset()` + 多次 `SetObjective`）影響所有程式路徑。先前的逐位驗證只涵蓋 `m4g.xconf`（seed 0 與 seed 1）與 `m4g_wiponly.xconf`，**沒有涵蓋 `ForbidSplitting` 這條路徑**。

比對方式：`footprint.csv` 排除 `Memory*` / `RealTime*` / `Timing*` 欄位後應全等；`m4g_decision_log.csv` 排除 `solveSec` 後應逐列逐欄全等。

**不通過就不要往下跑。**

---

## 5. 分析與驗收準則

### 鐵律：先看處理件數

「少跑不代表節省能耗。倉庫的初衷是吞吐量最大化。」**任何效率宣稱之前，先確認 `ItemsHandled` 打平**（±0.5% 內）。件數掉了就是拿吞吐換效率，效率數字不成立。

### 主要指標

| 指標 | 來源 | 用途 |
|---|---|---|
| `ItemsHandled` | footprint #36 | 前提檢查 |
| `LinesHandled` / `OrdersHandled` | #37 / #38 | 產出 |
| 出貨站到站次數 | `kpi_report` `output_station_arrivals` | 拆單機制的直接證據 |
| 訂單 pile-on | `kpi_report` `system_order_pile_on` | 共乘效率 |
| 總距離、每件能耗 | `kpi_report` | 效率主張 |
| `OSIdleTimeAvg` | footprint #142 | 站台飢餓（三臂需同源取數） |

決策層診斷（`m4g_decision_log.csv`）：`newTrips`、`unitsFromSunk` / `unitsFromNew`、`wipOrders`、`valuedLines` / `boundLines`。

**不用 `LateOrdersCount` 當主指標**——2026-08-03 的 5-seed 實驗已證明它是 6~16 的小樣本計數，單場隨機性就足以造成 ±6 擺動（WIP 定價那輪 p > 0.10）。

### 統計

2 seeds 是探索用，只看方向是否一致，**不下統計結論**。若某個點出現方向不一致或效果量接近噪音，補到 5 seeds 再判定。

---

## 6. 預期的敘事（待數據驗證，不可預設）

若假說成立，應該看到拆單增益隨以下方向放大：

- 車數 12 → 6（行走由不緊變緊）
- 庫存 70% → 50%（單一貨架湊齊整單的機率下降）
- 訂單池 50 → 100（可合併對象變多）

**基準點（10 車）站台已 99.2% 忙碌、件數貼理論上限 99.3%**，行走不是瓶頸——這正是先前量到「M4G 在此點贏的是能耗（−31%）而非吞吐（+0.1%）」的原因。車數軸的價值就在於走出這個飽和區，看拆單能不能在行走吃緊時轉換成吞吐優勢。

若數據不支持，誠實記錄反例即可——這正是敏感度分析該做的事。

---

## 7. 相關檔案

**已建立、byte-clean 驗證過**（各只差一行、CRLF 保留）：

- `Material/Instances/CoreBenchmark/small/small_6bot.xlayo`、`small_8bot.xlayo`、`small_12bot.xlayo`（母本 `small_10bot.xlayo`，只差 `<BotCount>`）
- `small_o50_mu100_2h_inv70.xsett`、`small_o75_mu100_2h_inv70.xsett`（母本 `small_o100_mu100_2h_inv70.xsett`，只差 `<OrderCount>`）
- `small_o100_mu100_2h_inv50.xsett`（同母本，只差 `<InitialInventory>`）

⚠️ 這些新 xsett 的內部 `<Name>` 沿用母本的 `small_o100_mu100_4h_inv70`（母本自身的既存標籤錯誤，內容確為 7200 秒）。輸出資料夾內層名稱因此會長得一樣，**識別靠外層目錄名**。

**組態臂**：`m4g.xconf`（正典，拆單）、`m1g_a.xconf`（不拆單）。

---

## 8. 這段期間 M4G 的狀態

- **正典未變**：`m4g.xconf` 從未加入 `DueDatePricingEnabled` / `WipHoldingEnabled`，兩者程式碼預設皆為 `false`，產出已驗證與 `dink_s0` 逐位一致。
- **效能優化已整併**：模型重用（一次建模，Dinkelbach 迭代只 `SetObjective`）＋ `LinearModel.Reset()`。逐位一致、**快 1.41~1.52 倍**。
- **兩個放棄的機制保留為 ablation**（預設關閉）：到期定價（治標不治本，逾期 16→13）、WIP 持有價格（5 seeds 下逾期改善 p > 0.10 不成立，能耗代價 +5.9% p < 0.002 確定）。
- **已回退**：B2 的 big-M 收緊（破壞逐位一致性且求解未變快）。
