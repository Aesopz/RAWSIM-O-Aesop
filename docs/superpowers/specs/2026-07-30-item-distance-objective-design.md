# Item-Distance 目標式重構：把「未來距離」寫進當期最優

日期：2026-07-30
分支：`setlevel-redesign`
狀態：**待使用者審查**（brainstorm 問答被跳過，設計決策由我代為定案，見 §2，請逐條確認或推翻）

---

## 1. 問題陳述

### 1.1 兩個結構性發現

**發現 A：M3G 的解空間裡沒有時間。**

決策變數只有 `xps[p,s]`、`yrp[r,p]`、`q[o,i,p,s]`、`y[o,s]`、`z[o]`，**沒有一個帶時間索引**。模型描述的是「此刻的靜態指派問題」，但真實系統玩的是連續時間的補給問題。所有補丁（`PipelineFloor`、`icFeed`、`PipelineFloorLeadSec=70`、`icLGcap`）都是缺失時間軸的代理品，這解釋了為何 `w_pipe`/`w_starve` 全部 inert。

**發現 B：目標式沒有為拆單定價。**

檢查 `split_milp_m3g.xconf` 實際生效的權重：

| 項 | 值 | 獎勵對象 |
|---|---|---|
| `OrderRewardWeight` (w2) | −40 | 完成**整張**訂單 |
| `LineClosureWeight` | 30 | 關掉一條 line |
| `UnitDrawReward` (eps) | −0.5 × scarcity | 每件 |
| `QueuedPodDrawPenalty` | +1 /件 | 從排隊貨架取 |
| `OnTheWayPodDrawPenalty` | +3 /件 | 從在途貨架取 |
| `MultiPartPenalty` | 0（關） | — |
| `NewPodPartialPenalty` | 0（關） | — |

每一個獎勵項都**與「這張單是不是拆的」無關**：完成訂單 o 得 40，不論它被 1 台貨架整批服務或 3 台貨架跨站拆著服務。而成本側嚴格為正（多一台貨架 = 多一份 `d(p,s) + w4`；從非處理中貨架取件每件 1 或 3）。

> **收益 0、成本 > 0 ⇒ 純最佳化下永遠不拆。**

M3G 之所以仍會拆，是 `icLGcap`（每站每次至多 1 台新貨架）與 `eshi4`（等式，必須填滿當前空槽）聯手把它逼到「不拆就無解」。**拆單是供給受限下的可行性殘差，不是被最佳化出來的決策。**

### 1.2 A 與 B 是同一件事

拆單真正的收益是**未來省下的趟次**：一台貨架不必湊齊某張單的所有 SKU 就能貢獻，因而能服務更多單，因而之後少跑一趟。

> **拆單的收益在未來，成本在當期。單期模型看得見成本、看不見收益，於是系統性低估拆單。**

這也解釋了三方公平對照的結果（`fixed_fill1350_inv70`，seed 0）：M3G pile-on 3.58 對 HGS 4.42。不是 HGS 算得準，是 M3G 的目標式壓根不獎勵這件事。

### 1.3 使用者陳述的真實目標

> 「我希望最小化的是 item distance，並且最大化訂單完成數，我需要判斷本輪完成的部分單能不能利於未來的完成。」

這與現行目標式（−40 × 完成整單數為絕對主導項，距離為配角）不一致，也與論文題目「能耗感知的線上拆單聯合優化」不一致。

---

## 2. 設計決策（代為定案，待審）

brainstorm 問答被使用者跳過，以下為我代為做的判斷。**每一條都可被推翻。**

| # | 決策 | 選擇 | 理由 |
|---|---|---|---|
| **D1** | 範圍 | **分階段。本 spec 只做階段一（加法）**：在現有目標式上加 ρ_i 與 λ 兩項，舊權重全部保留。階段二（拆權重塔）另開 spec。 | 專案方法論鐵律是單變數對照。一次拆掉六個權重無法歸因，且 memory 記載 `PipelineFloor` 是壓飢餓的承重牆，同時鬆開會同時放出多個病理。 |
| **D2** | 是否保留 w2 = −40 | **保留。** | 使用者明說要「最大化訂單完成數」。w2 就是這一項。它不是要被刪掉的東西，缺的是另外兩項。 |
| **D3** | ρ_i 取代還是疊加於 `LineClosureWeight` | **取代**（模式切換，非同時生效）。 | 兩個 line-closure 獎勵同時存在會互相污染。切換式才能乾淨回答「調出來的常數 30 vs 算出來的 ρ_i」。 |
| **D4** | λ 靜態或動態 | **預設動態自我校準**（跑動 m/item），另提供固定值模式供消融。 | 動態版無需調參且在數學上是對比值目標做下降步。固定版用於劑量反應。 |
| **D5** | 是否同時拿掉 `icLGcap` | **本階段不動**，但納入消融矩陣。 | 若 ρ_i 真的為拆單定價，人工上限就不再必要——這是一個**可證偽的預測**，比宣稱有價值。 |
| **D6** | 實作載體 | **在 `SplitM2eICManager.cs` 內加旗標**，預設關閉 = bit-identical。不新建 manager。 | 這是該檔案既有的擴充模式（`SoftInboundCommitted`、`PipelineFloor` 等皆然）。`M1GManager.cs` / `HADGSManager.cs` 依憲法完全不動。 |

---

## 3. 數學模型

### 3.1 核心物理事實

RMFS 的距離是**按 line 計價**的，不是按件：要取某 SKU 必須叫一台載有它的貨架過來；叫來之後取 1 件與取 5 件的距離成本相同。

> **一個部分完成有利於未來，若且唯若它把某條 line 完全關掉。**

取走 3 件中的 2 件 → 未來仍須為該 SKU 再叫一次貨架 → 距離成本一分未省，只是把工作往後推並多佔一個槽。

### 3.2 成本待付函數（cost-to-go）

定義訂單殘餘狀態的勢函數，對 line 可加：

$$\Phi(o)=\sum_{i\,\in\,\text{open lines of }o}\rho_i$$

其中 ρ_i = 未來為取得 SKU i 而須額外付出的預期距離（公尺）。

關掉一條 line (o,i) 使 Φ 下降 ρ_i。這就是它在當期應得的獎勵——**有單位、有物理意義**，而非調出來的 30。

### 3.3 完整目標式

現行目標式（`SplitM2eICManager.cs:705-841`，w1 = 1，故距離項本來就是公尺）：

```
obj =   Σ_{p,s} [ d(p,s) + extra(p,s) + w4 ] · x[p,s]      # 當期貨架→站距離
      + Σ_{r,p} d_bot(r,p) · yrp[r,p]                       # 當期車→貨架距離
      + w2 · Σ_o z[o]                          w2 = −40     # 完成整單
      + w3 · Σ_s us[s]                         w3 = 0
      + Σ eps · scarcity_i · q[o,i,p,s]        eps = −0.5
      + Σ tier_pen · q[o,i,p,s]                Queued 1 / OnTheWay 3
      − W_line · Σ_{o,i} c[o,i]                W_line = 30
      + W_pipe · Σ_s shortfall[s]              W_pipe = 20
```

**階段一新增兩項**（皆 gated，關閉時 bit-identical）：

```
      − Σ_{o,i} ρ_i · c[o,i]        # 未來省下的距離（取代上面的 W_line 項）
      − λ  · Σ q[o,i,p,s]           # 每件的距離信用
```

連結限制式**已存在**（`:1012`，`icLine`），不需新增：

$$\sum_{p,s} q_{oips}\;\ge\;\text{demand}(o,i)\cdot c_{oi}$$

即 `c[o,i]` 僅在該 line 被取滿時方可為 1。**這正是防住 partial 氾濫的結構性保險**（memory: θ=2 那次崩到 TP 518 的教訓）：取一半拿不到 ρ_i，模型會自己拒絕。

### 3.4 ρ_i 的定義

直覺：若很多貨架載有 SKU i，遲早有一台順路帶它來，ρ_i 應接近 0——不值得為它現在特地拆單。若只有一台很遠的貨架載有它，ρ_i 就是那趟的距離——現在不取，以後很貴。

$$\rho_i \;=\; \texttt{RhoScale}\;\cdot\;\frac{2\,\bar d_{ps}}{\max(1,\;n_i)}$$

- `n_i` = 目前候選集 `Pa` 中載有 SKU i 的**貨架台數**（非件數——決定「會不會有貨架順路帶它來」的是台數）
- `d̄_ps` = 本次決策中所有 (Pa 貨架, Cs 站) 配對的 `EstimatePodStationDistance` 平均值
- 係數 2 = 來回程
- `RhoScale` 預設 1.0，作為劑量反應的唯一旋鈕

**與既有 `icScarcity` 的關係**：程式中已有 `M2eICMath.PoolScarcity(demand, supply) = min(1, demand/supply)`（`:637-643`），但它是**需求壓力比**（無單位，[0,1]），不是**取得難度**，且分母用的是件數而非台數。ρ_i 不重用它，但保留 `PoolScarcity` 形式作為消融替代式（見 §6）。

### 3.5 λ 的定義

「最小化 item-distance」是比值目標，不可直接線性化。採 **Dinkelbach 參數化**：引入 λ =「一件商品值多少公尺」，將比值問題轉為線性問題。

$$\lambda=\texttt{LambdaScale}\cdot\frac{\texttt{Instance.StatOverallDistanceTraveled}}{\max(1,\;\texttt{Instance.StatOverallItemsHandled})}$$

兩個統計欄位皆可於決策時即時讀取（已驗證存在）。

語意：模型只接受**比當前實測平均更划算**的動作，數學上即對比值目標做一步下降。自我校準，無需調參。

**暖機**：`StatOverallItemsHandled < LambdaWarmupItems`（預設 50）時使用 `LambdaFallback`（預設 10.0 m/item，取自三方對照實測 10.1–10.5）。

---

## 4. 實作面

### 4.1 檔案

| 檔案 | 動作 |
|---|---|
| `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs` | 於 `SplitM2eICConfiguration` 新增 §4.2 欄位 |
| `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs` | 改 line-closure 係數為模式切換；新增 λ 項；新增 ρ/λ 診斷欄位至 `_exactDecisionLog` |
| `Material/Instances/CoreBenchmark/small/*.xconf` | 新增消融組態（不改正典 `split_milp_m3g.xconf`） |

**不動**：`M1GManager.cs`、`HADGSManager.cs`、任何引擎檔、正典 `split_milp_m3g.xconf`。

### 4.2 新增組態欄位

```csharp
public enum LineClosureValuationMode { Flat, Rho }

/// Flat = 現行行為（LineClosureWeight 常數）；Rho = §3.4 的 per-SKU 公尺計價。
public LineClosureValuationMode LineClosureMode = LineClosureValuationMode.Flat;
/// ρ_i 的劑量旋鈕。僅 LineClosureMode = Rho 時生效。
public double RhoScale = 1.0;

/// 是否啟用 §3.5 的每件距離信用 λ。false = bit-identical。
public bool ItemDistanceCreditEnabled = false;
/// λ 的劑量旋鈕。
public double LambdaScale = 1.0;
/// > 0 時改用此固定 λ（消融用），忽略跑動統計。
public double LambdaFixed = 0;
/// 暖機門檻與回退值。
public int LambdaWarmupItems = 50;
public double LambdaFallback = 10.0;
```

### 4.3 Gating 保證

- `LineClosureMode = Flat`（預設）→ 係數計算路徑與現行完全相同
- `ItemDistanceCreditEnabled = false`（預設）→ 整個 λ 區塊跳過，`LinearExpression.Sum` 不被呼叫
- 兩者皆為預設值時，目標式須與 `split_milp_m3g.xconf` 現況 **bit-identical**（驗收條件，見 §5）

### 4.4 單位一致性防呆（重要）

ρ_i 與 λ 都以**公尺**計價，這依賴目標式的距離項也是公尺。已驗證：

- `w1 = 1`（`SplitM2eICManager.cs:505`，硬編碼常數，非組態）
- `split_milp_m3g.xconf` 的 `StarveAwareCostEnabled = false`
- 因此 `ExactPodStationCost` / `ExactBotPodCost`（`:164-181`）走 `return d` 分支，**回傳原始距離（公尺）**

**但這是條件成立的**：若 `StarveAwareCostEnabled = true`，兩函式改回傳 `StarveAwareCost.TravelTime(d, speed)`，單位變成**秒**，此時 ρ_i 與 λ 的公尺計價與目標式其餘部分不可通約，模型行為無意義。

**要求**：`LineClosureMode = Rho` 或 `ItemDistanceCreditEnabled = true` 時，若偵測到 `StarveAwareCostEnabled = true`，於 `SplitM2eICManager` 初始化時擲出 `InvalidOperationException`，訊息明示單位衝突。寧可硬失敗，不要靜默產生無意義的解。

### 4.5 C# 7.3 注意事項

- 新 enum 與欄位置於既有 `MethodConfigurationsOB.cs`，不新增檔案 → 無須改 csproj
- λ 與 ρ 皆為區域 `double`，不涉 out 參數 lambda 捕獲（CS1628）
- 建置：`MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`

---

## 5. 驗證計畫

基準：`small.xlayo` + `fixed_fill1350_inv70.xsett`（乾淨單變數對照，Fixed 模式），seed 0。
參照數據（已有）：

| 模型 | TP | 件數 | 飢餓s | pile-on | 趟次 | EOR | m/item |
|---|---|---|---|---|---|---|---|
| M1G | 312.5 | 2835 | 388.8 | 1.619 | 771 | 3.600 | 19.12 |
| M3G | 295.0 | 2823 | 500.0 | 3.576 | 328 | 2.040 | 10.54 |
| HGS | 305.0 | 2859 | 149.9 | 4.420 | 274 | 1.847 | 10.12 |

**驗收 0（硬性）**：預設旗標下重跑 M3G，所有 KPI 與上表 M3G 列**逐位相同**。不通過則 gating 有漏，不得繼續。

**消融矩陣**（2 × 2）：

| 組態 | LineClosureMode | ItemDistanceCredit | 檔名 |
|---|---|---|---|
| A（基線） | Flat | off | `split_milp_m3g.xconf`（正典，不動） |
| B | **Rho** | off | `split_milp_m3g_rho.xconf` |
| C | Flat | **on** | `split_milp_m3g_lambda.xconf` |
| D | **Rho** | **on** | `split_milp_m3g_rholambda.xconf` |

主要指標：**m/item**（目標函數本身）、pile-on、趟次、TP、飢餓。

**劑量反應**：D 組確認方向後，`RhoScale ∈ {0.5, 1, 2, 4}`、`LambdaScale ∈ {0.5, 1, 2}` 各掃一軸。

**D5 的可證偽預測**：若 ρ_i 真的為拆單定價，則在 D 組上關閉 `DispatchCapEnabled`（`icLGcap`）不應使 pile-on 崩潰。這一項單獨跑，結果無論正反都寫進論文。

---

## 6. 風險

1. **仍是單期的。** ρ_i 是對未來的靜態近似，假設「未來世界與現在相似」。訂單流劇烈變化時會失準。但相較於目前完全看不見未來，已改善一個量級。**此限制須誠實寫進論文，不可宣稱解決了跨期問題。**

2. **w2 = −40 可能仍然壓過一切，使新項 inert。** 這是本設計最大的失敗模式，且有前例（`w_pipe`/`w_starve` 全 inert）。緩解：`RhoScale`/`LambdaScale` 提供劑量反應，若掃到 4× 仍無反應即為乾淨負結果，直接證明瓶頸在時間軸而非目標式——**此結果對論文同樣有用**（強化「MILP 降級為模型定義與上界參照」的定位）。

3. **λ 的自我校準有回饋迴路。** λ 由模型自己造成的距離統計算出，理論上可能自我強化或震盪。緩解：`LambdaFixed` 模式提供開迴路對照；若動態版不穩即改用固定值。

4. **partial 氾濫。** 已由 §3.3 的 `icLine` 結構性限制式擋住（取不滿拿不到 ρ_i）。仍須在消融中監看 partial 訂單數。

5. **單位一致性依賴 `StarveAwareCostEnabled = false`。** 見 §4.4。以硬失敗防呆，不留靜默錯誤路徑。

6. **ρ_i 用 `Pa` 台數當分母**，而 `Pa` 只含此刻停在儲位的閒置貨架（發現 A 的候選集截斷）。因此 ρ_i 會系統性高估稀缺度。此偏差方向是「偏好多拆」，須在劑量反應中留意。

---

## 7. 明確不做的事

- 不加時間索引 / 不做多期模型（求解時間會爆炸；memory 記載 exact 已比啟發式慢 45×）
- 不改 `M1GManager.cs` / `HADGSManager.cs` / 任何引擎檔
- 不改正典 `split_milp_m3g.xconf`
- 不動 `Cs` / `Pa` 候選集的前瞻窗（發現 A 的另一半，另開 spec）
- 不重啟已放棄的方向：晚綁定、CBS、RL、分散式控制

---

## 8. 待使用者確認

1. §2 的六條決策（尤其 **D1 分階段**與 **D2 保留 w2**）是否同意？
2. §3.4 的 ρ_i 公式——分母用「載有該 SKU 的貨架台數」是否符合你的直覺？
3. §5 的驗收基準用 seed 0 單一種子先探路，方向確認後再補 seed 1（依 `feedback_default_2seeds`）——可以嗎？
