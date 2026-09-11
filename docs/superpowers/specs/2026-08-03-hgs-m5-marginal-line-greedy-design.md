# HGS-M5：邊際行貪婪（Marginal-Line Greedy）設計規格

日期：2026-08-03
狀態：**規格定案，尚未實作**
目標：一個與 M4G **共用同一個解空間與同一個目標式**、但以貪婪方式建構解的啟發式。

---

## 1. 動機：現行 HGS-M4 為什麼只拿到三成

### 1.1 實測（2 小時 / 10 車 / 2 站 / o100 / inv70，5 seeds）

| 指標 | M1G | M4G（正典） | HGS-M4 |
|---|---|---|---|
| 處理件數 | 1418.8 | 1429.4 | 1426.0 |
| 完成訂單 | 644.0 | 585.0 | 637.4 |
| 貨架到站 | 213.2 | 100.8 | 171.8 |
| pile-on | 3.021 | 5.804 | 3.711 |
| 每件能耗 | 0.896 | **0.482** | 0.769 |
| 周轉時間 | 831 s | 1223 s | 847 s |
| 逾期訂單 | 5.2 | 10.4 | 5.2 |
| 整場牆鐘 | 91.5 s | 804.2 s | **4.4 s** |
| δ | — | **0.166** | **0.946** |

HGS-M4 抓到 M1G→M4G 增益的比例：每件能耗 31%、貨架到站 37%、pile-on 25%。
速度優勢 **181 倍**，服務水準與 M1G 齊平（M4G 在這三項全輸）。

### 1.2 根因：解空間比 M4G 小

`GreedyM4GManager.CompletionSweep`（`:392-412`）：

```csharp
var parts = PvgsStationSplitPlanner.PlanCompletion(residual, st.PerStationAvail, slotFree);
if (parts == null)
    continue;          // 湊不齊整單 -> 完全不動這張單
```

**HGS-M4 只承諾「本輪能被完整覆蓋」的訂單。** 它支援跨站拆單，但**不支援跨期拆單**（部分滿足）。

M4G 的 `V2` 是不等式 $\sum_{p,s}\hat q_{oips}\le \mathrm{rem}(o,i)$；`PlanCompletion` 等於把它收成等式。

實測佐證 M4G 的主力正是部分滿足：每次決策只綁定 **1.55 件**（待辦池 148 張），平均掛著 **44.9 張**半完成母單。

| | 跨站拆單 | 跨期拆單 |
|---|---|---|
| M4G | ✓ | **✓（主力）** |
| HGS-M4 | ✓ | **✗** |

### 1.3 次要缺陷（HGS-M5 自動解決，不需個別修）

- **δ 錯配**：`GreedyM4GManager.cs:689` 以 `bound = 已關行`、`valued = 已關行 + 裝不下的行` 餵給 `M4GPricing`，得 δ≈0.946；M4G 是對整個待辦池全域估值，得 δ≈0.166。兩者的 `valued` 不是同一個量。後果是計分式（`:549`）的 `lambda * delta * valuedNotBoundLines` 幾乎全額計價「看得到吃不到」的行，系統性高估貨架價值。
- **入口守衛**：`DecideAboutPendingOrders` 以 `Ra.Count() > 0` 閘住整個決策。沒有閒置機器人時連「從已在站貨架撿貨」都不做。M4G 有 89% 的決策不派新車（`newTrips` 平均 0.11）卻仍綁定約 1.5 件。

> λ 的差異（12.648 vs 8.662）**不是缺陷**。λ = 累積距離 ÷ 累積關閉行數，HGS 每關一行走得遠，λ 本來就該高。這是自我校準正常運作的證據。

---

## 2. 核心設計

把「挑訂單來完成」換成「**反覆挑下一個邊際 Δ 目標值最負的動作**」。

### 2.1 動作集合（顆粒度：行）

**動作 A — 取行（draw-line）**
從貨架 $p$ 在站台 $s$，把訂單 $o$ 的商品 $i$ 這一行的殘餘 $r_{oi}$ **一次取滿**。

前提：$p$ 已承諾至 $s$（Pp/Pq/Pb），或本輪已被動作 B 派往 $s$；且 $p$ 的 $i$ 庫存 $\ge r_{oi}$（扣除本輪已配出的量）；且 $s$ 尚有空槽，或 $o$ 已佔用 $s$ 的槽。

$$\Delta_A = -\lambda \;-\; \mu\cdot[\text{此行為 } o \text{ 的最後一行}] \;+\; \sum_{\text{units}}\big(\pm\rho\big) \;-\; \varepsilon\cdot r_{oi}$$

其中 $\rho$ 的正負依貨架分級：Pp 為 $-\rho$、Pq/Pb 為 $0$、Pa（本輪新派）為 $+\rho$。

**動作 B — 派車（dispatch）**
把 Pa 貨架 $p$ 指派給站台 $s$，並綁定最近的閒置機器人 $r$。

$$\Delta_B = +\,d(p,s) + d^{bot}(r,p)$$

派車本身恆為正成本，**必須靠它解鎖的動作 A 才可能划算**。因此評估動作 B 時採「前瞻一步」：計算派下去後可立即執行的動作 A 集合之總和，取

$$\Delta_B^{\text{net}} = \Delta_B + \sum_{\text{可解鎖的 A}} \Delta_A$$

這是唯一的前瞻，範圍限於「這台貨架這一輪能做的事」，不跨貨架、不跨輪。

### 2.2 主迴圈

```
repeat
    best <- argmin over all feasible A and B of  Δ
    if Δ(best) >= 0: stop
    apply best
until no feasible move
```

槽位與機器人是**硬限制**，不進目標式（與 M4G 的 B3 一致）。

### 2.3 為什麼顆粒度是「行」而不是「件」

$\lambda$ 只在一行**完全關閉**時入帳（M4G 的 V3 / B5）。以件為單位時，一行需 3 件的情況下前兩件的 $\Delta$ 只有 $-\varepsilon\approx 0$，純貪婪永遠走不到第三件——除非加入跨件前瞻，那就不再是純貪婪。以行為單位讓每個動作的邊際價值都非零。

代價：無法表達「同一行分兩台貨架湊滿」。M4G 可以（V1 是 per-pod 的庫存上限，不禁止同 SKU 跨貨架）。這是 HGS-M5 相對 M4G 的**已知且已申報**的解空間縮減，應在論文中明說，並作為 optimality gap 的一部分來源。

---

## 3. 關鍵性質：可行性與上界

每個動作只是在設定 M4G 的 $q$ / $x$ / $y^r$ / $y$ 變數，且主迴圈全程維持 R1–R5、V1–V4、B1–B7、B3 全部成立。因此：

> **HGS-M5 產出的每一個解，都是 M4G 那個 MILP 的可行解。**

由於兩者目標式逐項相同（T1–T6，含 $\lambda,\mu,\delta,\rho,\varepsilon$ 的同一組自我校準價格），恆有

$$\mathrm{obj}_{\text{HGS-M5}} \;\ge\; \mathrm{obj}_{\text{M4G}}$$

**兩者之差即為可直接量測的最佳化間隙。** 這讓 M4G 名正言順成為「可證明的上界參照」，HGS-M5 成為「可擴展的近似」，並可逐決策報出 gap。

⚠️ 前提：M4G 必須設 `MIPGap = 0` 才真的是最優（現況為 Gurobi 預設 1e-4 相對間隙）。這是實作 HGS-M5 之前該一併處理的事，見 §7。

### 3.1 δ 在 HGS-M5 中的地位

建構式貪婪**沒有「估值了但沒綁定」這個狀態**——評估什麼就做什麼。因此：

- 目標式中 $-\lambda(1-\delta)c - \lambda\delta\hat c$ 兩項合併為 $-\lambda c$（等價於 $\delta$ 任意值下 $\hat c = c$ 的情形）
- $\mu$ 同理
- **不再呼叫 `M4GPricing.Delta()`，也不再呼叫 `RegisterDecision`**

這不是「拿掉一個機制」，而是「兩層退化成一層時 δ 自然消失」。λ、μ、ρ、ε 的校準完全照舊（`RegisterClosedLines` / `RegisterCompletedOrders` 仍照常呼叫）。

### 3.2 λ 的外層迭代（可選，預設開啟）

貪婪整場僅 4.4 秒，故可比照 Dinkelbach：以歷史 $\lambda_0$ 建一次解，讀回實際的 $D^*$ 與 $V^*$，令 $\lambda_1 = D^*/V^*$ 重建，迭代至收斂或達上限。上限沿用 `DinkelbachIterations` 的語意，另設 `LambdaIterations`（預設 5）。

---

## 4. 架構決策

### 4.1 新檔案，不動既有 manager

依專案憲法：`M1GManager.cs` / `HADGSManager.cs` / `SplitM2eICManager.cs` 絕不修改。
**`GreedyM4GManager.cs` 同樣不修改**——它是「只跨站、不跨期」的乾淨對照臂，是本設計的消融組，必須維持逐位可重現。

新增：

| 檔案 | 內容 |
|---|---|
| `RAWSimO.Core/Control/Defaults/OrderBatching/GreedyM5Manager.cs` | 主體 |
| `MethodConfigurationsOB.cs` 內新增 `GreedyM5Configuration` | 組態 |

### 4.2 繼承鏈

```csharp
public class GreedyM5Configuration : PVGSConfiguration, IM4GPrices
```

與 `GreedyM4GConfiguration`（`MethodConfigurationsOB.cs:1612`）相同的繼承鏈。理由：`PVGSConfiguration` 已在引擎的型別檢查鏈上，`IM4GPrices` 讓它能直接餵 `M4GPricing`，兩者共用同一組價格欄位與預設值。

`IM4GPrices` 要求 `DeltaScale` / `DeltaFallback` / `DeltaFixed` 三個欄位——**保留但不使用**（介面契約要求），在 XML 註解中註明 HGS-M5 不呼叫 `Delta()`。

### 4.3 引擎掛勾（三處，全為新增）

| 位置 | 動作 |
|---|---|
| `MethodConfiguration.cs:369` 附近 | `OrderBatchingMethodType` 列舉新增 `GreedyM5` |
| `MethodConfiguration.cs:808` 附近 | 新增 `[XmlInclude(typeof(GreedyM5Configuration))]` |
| `Controller.cs:128` 附近 | 新增 `case OrderBatchingMethodType.GreedyM5: OrderManager = new GreedyM5Manager(instance); break;` |
| `RAWSimO.Core.csproj:133` 附近 | 手動加 `<Compile Include="Control\Defaults\OrderBatching\GreedyM5Manager.cs" />` |

⚠️ `XmlInclude` 與列舉新增必須**追加在既有項目之後**，不得插隊——否則既有 xconf 的反序列化順序會壞掉。

### 4.4 可重用的既有元件

| 元件 | 用途 |
|---|---|
| `M4GPricing` + `IM4GPrices` | λ / μ / ρ / ε 校準（δ 不用） |
| `Order.CreateSplitChild` + demand ledger | 拆單資料層，勿重新發明 |
| `SplitConsolidationLogger` | `splitorders.csv` 記錄 |
| `GreedyM4GManager` 的 `InitializeSnapshot` / `BuildEpochState` 的**語意** | 鏡像複製，不繼承（憲法要求忠實鏡像優於共用） |

### 4.5 入口不設機器人守衛

`DecideAboutPendingOrders` **不得**沿用 `Ra.Count() > 0` 的閘門。動作 A 不需要機器人；只有動作 B 需要。無閒置機器人時仍應執行動作 A 的迴圈。

---

## 5. 消融與對照設計

新增後可得的四臂對稱結構：

| | 精解 | 貪婪 |
|---|---|---|
| **不跨期拆單** | M1G-a（`ForbidSplitting=true`） | **HGS-M4**（現行） |
| **跨期拆單** | M4G | **HGS-M5**（本規格） |

- 橫向（同一列）= **求解方法**的影響 → optimality gap
- 縱向（同一欄）= **跨期拆單能力**的影響 → 論文核心貢獻

兩個家族用同一個消融回答同一個問題，這是本設計最主要的論文價值。

### 5.1 驗收基準

基準點：2 小時 / 10 車 / 2 站 / o100 / inv70，seed 0–4。

| 條件 | 判準 |
|---|---|
| **前提** | `ItemsHandled` 與 M4G 打平（±0.5%）。件數掉了效率數字一律不算 |
| **必要** | 每件能耗顯著優於 HGS-M4（0.769），朝 M4G（0.482）方向移動 |
| **必要** | 目標值 $\ge$ M4G 的目標值（若出現 $<$，代表可行性被破壞，是 bug） |
| **必要** | 牆鐘仍在數十秒量級（相對 M4G 的 804 秒） |
| 觀察 | 周轉時間 / 逾期是否向 M4G 惡化——若是，代表跨期拆單的積壓代價是**機制固有**而非 M4G 的實作問題，這本身是有價值的發現 |

---

## 6. 風險

| 風險 | 說明 | 緩解 |
|---|---|---|
| **貪婪把槽位浪費在低價值部分滿足** | 一行只值 λ，但佔一個槽可能擋掉一張能完成的整單（值 μ + 多個 λ） | 先實測。若確實發生，考慮在**同一 Δ 值下**優先取能完成整單的動作（tie-break，不改目標式） |
| **周轉時間惡化** | M4G 的積壓（WIP 44.9、周轉 +47%）若源自跨期拆單本身，HGS-M5 會繼承 | 這是預期中的科學結果，不是 bug。已知 WIP 定價無法乾淨解決（2026-08-03，5 seeds，p > 0.10） |
| **動作 B 的前瞻造成 $O(|P|\times|S|\times|O|)$ 掃描** | 每次派車評估都要試算可解鎖的行 | 沿用 `GreedyM4GManager` 現有的 `st.Avail` / `OiSKU` 索引；必要時對候選貨架做距離預篩（**須記錄被篩掉的數量**，不得靜默截斷） |
| **同一行跨貨架湊滿無法表達** | §2.3 已申報的解空間縮減 | 明說，並歸入 optimality gap 的來源分析 |

---

## 7. 前置事項

1. **M4G 設 `MIPGap = 0`**。否則「HGS-M5 離最優多遠」的最優是近似的，上界主張不成立。`LinearModel` 未設 `MIPGap`（Gurobi 預設 1e-4 相對），改動極小。實測目標值中位 −15、僅 1.4% 落在 |obj| < 1，預期對結果影響很小但必須驗證逐位差異。
2. **`sens_b10_a_s0` 的漂移檢查**（見 `docs/2026-08-03-split-sensitivity-plan.md` §4）——`ForbidSplitting` 路徑尚未在新 binary 上驗證過。

---

## 8. 不做的事

- 不修改 `GreedyM4GManager`（它是對照臂）
- 不修 δ 錯配（HGS-M5 不用 δ；HGS-M4 維持現狀當歷史紀錄）
- 不做局部搜尋 / 改良階段（先確立純貪婪的基線，改良留作後續）
- 不做件級顆粒度（§2.3 已論證需要前瞻，違背純貪婪定位）
