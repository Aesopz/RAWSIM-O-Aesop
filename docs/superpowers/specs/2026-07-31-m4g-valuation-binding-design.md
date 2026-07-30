# M4G：估值／綁定分層的單位級拆單

日期：2026-07-31
分支：`setlevel-redesign`
基準狀態：tag `m3g-v1-line30`（commit `b3ce7e6`）
狀態：**待使用者審查**

---

## 1. 問題陳述

### 1.1 使用者的訴求

> 「模型因為供給緊縮而努力填槽式的拆單，跟計算效益後的拆單不一樣。後者是計算之後，完成同樣數量前提下，更有效率的拆單，才是我追求的。」

> 「我需要一個很巧妙的方法，逼出拆單趨勢又不限制供給，同時自動即時供應貨架，禁止加重制式化前瞻提前量等等呆板設計。」

### 1.2 M3G 為什麼做不到：估值層遺失

**M1G 有兩層訂單指派變數**（`M1GManager.cs`）：

| 變數 | 受槽位限制 | 目標式獎勵 | 實際綁定 |
|---|---|---|---|
| `yos[o,s]` | **否** | **是**（`:712`，w2） | 否 |
| `yaos[o,s]` | **是**（`shi4`, `:723`：`Σ_o yaos[o,s] == Cs[s] − us[s]`） | 否 | **是**（`:1022` `AllocateOrder`） |

兩者由 `shi3`（`:719`）連接：`yaos[o,s] ≤ yos[o,s]`。`yos` 另受 `:728` 的供給覆蓋限制——只有當派往該站的貨架確實載得動該單全部需求時才可為 1。

因此 **M1G 目標式獎勵的是「若把這些貨架叫來，有多少張單做得完」——一個不受槽位限制的貨架估值**；真正寫進系統的只是其中塞得進空槽的子集。被獎勵的數字從來不是承諾。

**M3G 把這個分層弄丟了。** `SplitM2eICManager.cs`：

- `elink3`（`:1201`）：`Σ_p q[o,i,p,s] ≤ rem(o,i) · y[o,s]` —— 任何取件都必須讓該單佔一個槽
- `eshi4`（`:1208`）：`Σ_o y[o,s] == Cs[s] − us[s]` —— 槽位是硬上限

兩條合起來：**M3G 中任何能被計分的取件，都必須先佔到一個當下空槽**。完成獎勵 `z`、line 關閉獎勵 `c[o,i]` 全被壓在當前空槽的視窗內。同一組變數同時扮演估值、綁定與計分三個角色。

### 1.3 這解釋了 M3G 的三個既有病徵

1. **拆單是被逼出來的，不是算出來的**。目標式對拆單的收益恆為 0（完成 o 得 40 分與拆幾段無關）、成本嚴格為正（多一台貨架多一份距離；非處理中貨架取件每件罰 1 或 3）。純最佳化下永不拆單；M3G 之所以拆，是 `icLGcap`（`:982`，每站每輪至多 1 台新貨架）把它逼到不拆無解。**拆單是供給受限下的可行性殘差。**
2. **需要一整套外掛常數**。`icLGcap`、`PipelineFloorLeadSec=70`、`PipelineFloorWeight=20`、`LineClosureWeight=30` 全是「貨架價值在目標式裡表達不出來」的補丁。已驗證 `w_pipe`/`w_starve` 對結果 inert，`PipelineFloorLeadSec` 掃描 40/70/100 對 TP 亦近乎 inert。
3. **對外生訂單流敏感**。乾淨對照（`fixed_fill1350_inv70`）下 M3G pile-on 由 Fill 模式的 4.58 掉到 3.58，而貪婪對照 HGS 幾乎不變（4.56→4.42）。估值視窗窄則對 backlog 變化敏感。

### 1.4 為什麼「獎勵預測」是安全的（釐清一個誤判）

本次討論中一度認為「目標式獎勵的完成數是會過期的預測，卻要鎖住槽位」是設計缺陷。**這個判斷在 M1G 上不成立**：M1G 正是刻意讓被獎勵的 `yos` 與被綁定的 `yaos` 分離，預測只用於估值、不換取槽位。缺陷在於 M3G 合併了兩者，使「被獎勵」等於「被綁定」。M4G 是把 M1G 的正確做法還原到單位級，不是發明新機制。

---

## 2. 設計決策（brainstorm 定案）

| # | 決策 | 選擇 | 依據 |
|---|---|---|---|
| D1 | 核心結構 | 估值層／綁定層分離，估值層不受槽位限制 | §1.2；還原 M1G 既有結構 |
| D2 | 貨架選擇與拆法 | **同一次求解聯合決定**，不分兩步 | 沿用 M3G；先選貨架再裸拆的啟發式在車稀缺時崩潰（pile-on 暴跌） |
| D3 | 估值層視野 | **整個待辦池**，含當下無槽、但貨架到站後可接手的訂單 | 使用者定案；此即估值層存在的意義 |
| D4 | 價值定義 | **關閉的 line 數**為主、完成整單為輔 | 距離按 line 計價（叫一台貨架取 1 件與 5 件同價）；取一半不計分 |
| D5 | 價格來源 | λ、μ、δ 全部**自我校準**於跑動統計，無手調常數 | 使用者要求「禁止呆板設計」；論文可宣稱本模型無調參 |
| D6 | 供給上限 | **刪除 `icLGcap`**，供給由估值決定 | 使用者要求「不限制供給」；估值層可完整表達貨架價值後上限成為贅物 |
| D7 | 前瞻常數 | **刪除** `PipelineFloor` 全套（lead=70、weight=20、target） | 使用者要求「禁止制式化前瞻提前量」；即時補貨改由估值的邊際遞減自然產生 |
| D8 | 完成數字典序 | **不採用** | 硬性最大化當期完成數會使模型為預測中的完成無限制派車，正是要修掉的病 |
| D9 | 承諾語意 | 每次決策重解估值層、**不承諾任何拆法**；僅綁定層落實 | 實測每次決策僅落實約 1.49 單位，重解本來就是現況 |
| D10 | 實作載體 | **新檔案** `M4GManager` / `M4GConfiguration : M1GConfiguration` | 專案憲法：不得修改 `M1GManager.cs` / `HADGSManager.cs`，新模型以繼承／鏡像實作 |

---

## 3. 數學模型

### 3.1 集合與參數

| 符號 | 意義 | 來源 |
|---|---|---|
| `O` | 待辦訂單池（含拆單母單殘量） | `_pendingOrders` 過濾後 |
| `I` | 商品種類 | — |
| `S` | 有空槽的揀貨站 | `GenerateCs()` |
| `P_b` | 已承諾貨架（在站或在途） | `inboundPods` |
| `P_a` | 儲位閒置且載有待辦商品的候選貨架 | `UnusedPods` 過濾 |
| `R_a` | 可派的空閒車 | — |
| `d(p,s)` | 貨架 p 到站 s 距離（公尺） | `EstimatePodStationDistance` |
| `d_bot(r,p)` | 車 r 到貨架 p 距離（公尺） | — |
| `stock(p,i)` | 貨架 p 商品 i 可取量 | `CountAvailable` |
| `rem(o,i)` | 訂單 o 商品 i 殘量 | `GetRemainingDemand` |
| `cap(s)` | 站 s 當下空槽數 | `Cs[s]` |
| `lines(o)` | 訂單 o 尚未關閉的 (o,i) 集合 | `RemainingPositions` |

### 3.2 變數

**共用層**（貨架與車，只有一組——保證 D2 的聯合求解）

- `x[p,s] ∈ {0,1}`：貨架 p 派往站 s
- `yrp[r,p] ∈ {0,1}`：車 r 去搬貨架 p

**估值層**（不受槽位限制）

- `q̂[o,i,p,s] ∈ Z≥0`：貨架 p 在站 s 為訂單 o 取商品 i 的件數
- `ĉ[o,i] ∈ {0,1}`：line (o,i) 被完全關閉
- `ẑ[o] ∈ {0,1}`：訂單 o 全部 line 關閉

**綁定層**（受槽位限制，估值層的可行子集）

- `q[o,i,p,s] ∈ Z≥0`：本次真正落實的取件
- `y[o,s] ∈ {0,1}`：訂單 o 佔用站 s 的一個槽
- `c[o,i] ∈ {0,1}`：line (o,i) 由綁定層關閉
- `z[o] ∈ {0,1}`：訂單 o 由綁定層完全關閉

### 3.3 目標式

單一層、單位一律為公尺，最小化：

```
min  Σ_{p∈P_a, s∈S} ( d(p,s) + extra(p,s) ) · x[p,s]        (T1) 新貨架行程
   + Σ_{r∈R_a, p∈P_a} d_bot(r,p) · yrp[r,p]                  (T2) 取車行程
   − λ · Σ_{o,i} [ c[o,i] + δ · ( ĉ[o,i] − c[o,i] ) ]        (T3) line 關閉價值
   − μ · Σ_o [ z[o] + δ · ( ẑ[o] − z[o] ) ]                  (T4) 整單完成價值
   − ε · Σ_{o,i,p,s} q[o,i,p,s]                              (T5) 綁定微幅獎勵
```

- **T1/T2 只對 `P_a`（新派貨架）計費**：`P_b` 的行程是沉沒成本。與 M1G 目標式一致（`:706-708` 僅對 `UnusedPods` 計費）。
- **T3 是估值的核心**。已綁定的 line 全額計價；估值層算得出、但本輪因槽位不足而未綁定的 line 按 `δ ∈ [0,1]` 折價。`δ` 表示「估值最終被兌現的比率」，自我校準（§3.5）。**這是估值層不致過度樂觀的關鍵**：若歷史上估值兌現率低，δ 自動下降，模型自動變保守。
- **T4** 額外獎勵整單完成——完成的單會離開槽位，價值高於等量的零散 line。與 T3 一致地採 `δ` 折價：綁定層完成的 `z[o]` 全額，僅估值層完成的部分折價。
- **T5** 的 `ε` 極小（§3.5），只用來在估值同分的解中挑「當下能執行最多」者，不影響貨架選擇。

### 3.4 約束

**共用層**

```
(R1)  Σ_{s}  x[p,s] ≤ 1                            ∀p            每台貨架至多派一站
(R2)  Σ_{s}  x[p,s] ≤ Σ_{r∈R_a} yrp[r,p]           ∀p∈P_a        派貨架必須有車
(R3)  Σ_{p}  yrp[r,p] ≤ 1                          ∀r∈R_a        每台車至多搬一台
(R4)  Σ_{r}  yrp[r,p] ≤ 1                          ∀p            每台貨架至多一台車
(R5)  x[p,s*] = 1, yrp[r*,p] = 1                   ∀p∈P_b        已承諾貨架固定
```

`(R5)` 鏡像現行 `eshi7`/`eshi11`（`:1219-1220`）。

**估值層**

```
(V1)  Σ_{o} q̂[o,i,p,s] ≤ stock(p,i) · x[p,s]       ∀i,p,s        載量上限 + 須派該站
(V2)  Σ_{p,s} q̂[o,i,p,s] ≤ rem(o,i)                ∀(o,i)        不得超過殘量
(V3)  Σ_{p,s} q̂[o,i,p,s] ≥ rem(o,i) · ĉ[o,i]       ∀(o,i)        關 line 須取滿
(V4)  ĉ[o,i] ≥ ẑ[o]                                ∀o, ∀i∈lines(o)  完成須全關
```

`(V3)` 即現行 `icLine`（`:1136`）的估值層版本，是防止 partial 氾濫的結構性保險——取一半拿不到任何獎勵。

**綁定層**

```
(B1)  q[o,i,p,s] ≤ q̂[o,i,p,s]                      ∀(o,i,p,s)    綁定是估值的子集
(B2)  Σ_{i,p} q[o,i,p,s] ≤ ( Σ_i rem(o,i) ) · y[o,s]  ∀o,s       取件須佔槽
(B3)  Σ_{o} y[o,s] ≤ cap(s)                        ∀s            槽位上限（不等式）
(B4)  y[o,s] ≤ Σ_{i,p} q[o,i,p,s]                  ∀o,s          禁止空綁定
(B5)  Σ_{p,s} q[o,i,p,s] ≥ rem(o,i) · c[o,i]       ∀(o,i)        綁定層的 line 關閉
(B6)  c[o,i] ≤ ĉ[o,i]  ,  z[o] ≤ ẑ[o]             ∀(o,i), ∀o    綁定是估值的子集（防 T3/T4 出現負項）
(B7)  c[o,i] ≥ z[o]                                ∀o, ∀i∈lines(o)  綁定層完成須全關
```

`(B3)` 是**不等式**，取代 M3G 的等式 `eshi4`。註：現行 `eshi4` 因 `IdleSlotWeight = 0`（7/28 消融砍除）使 slack 變數 `us` 無成本，實質上已等價於不等式；此處改寫只是把語意寫明確。

**明確不存在的約束**（相對於 M3G）

- 無 `icLGcap`（每站每輪新貨架數上限）
- 無 `icLG1` / shortfall（管線軟地板）
- 無 `icFeed`（強制補車）
- 無 `PipelineFloorLeadSec` 相關的任何提前量判斷

### 3.5 價格的自我校準

三個價格皆由跑動統計即時計算，無手調常數。統計量在 `M4GManager` 內部累計，**不修改引擎統計檔案**。

```
λ = LambdaScale · ( 累計總行走距離 / max(1, 累計關閉 line 數) )        [公尺 / line]
μ = MuScale     · λ · ( 累計關閉 line 數 / max(1, 累計完成訂單數) )    [公尺 / 單]
δ = DeltaScale  · ( 累計「曾被估值但未綁定、其後真的被關掉」的 line 數
                    / max(1, 累計曾被估值但未綁定的 line 數) )         [無單位, 0..1]
ε = EpsilonScale · λ                                                   [公尺 / 件]
```

- `累計總行走距離` 讀 `Instance.StatOverallDistanceTraveled`（已驗證存在）。
- `累計關閉 line 數` 於 commit 時由本次殘量歸零的 (o,i) 計數，manager 內累計。
- `μ` 的語意：完成一整張單值「它平均含多少條 line」× λ。`MuScale` 預設 1.0；> 1 表示額外獎勵清空槽位。
- `δ` 的語意：估值的兌現率。追蹤方式為 manager 內維護一個 `(orderId, skuId) → 首次被估值但未綁定的時刻` 字典，該 line 其後被關閉時計入分子，訂單離開系統時清除。
- **暖機**：累計關閉 line 數 < `WarmupLines`（預設 50）時，`λ = LambdaFallback`（預設 10.0 m/line，取自三方對照實測 10.1–10.5）、`δ = DeltaFallback`（預設 0.5）、`μ = λ × 2.4`（實測平均每單約 2.37 件）。
- **單位一致性防呆**：λ/μ/ε 以公尺計價，依賴目標式距離項亦為公尺。若偵測到 `StarveAwareCostEnabled = true`（該旗標會使成本函式改回傳秒），於初始化時擲 `InvalidOperationException`，訊息明示單位衝突。寧可硬失敗，不得靜默產生無意義的解。

### 3.6 為什麼這一式滿足使用者的四個要求

**逼出拆單**：估值層不受槽位限制，「這台貨架能碰到多少張單的多少條 line」被完整計價。能為 5 張單各關 1 條 line 的貨架，估值高於只能為 1 張單關 3 條的貨架——拆單是被算出來的最優解，不是被上限逼出的殘差。

**不限制供給**：多派貨架不再被禁止，其去留取決於它帶來的 `ĉ` 增量能否抵過距離。**一條 line 只能被關一次**，因此第二台載有相同商品的貨架增量價值為 0 卻要付全額距離——過度供給在目標式中本來就不划算，不需 `icLGcap` 禁止。此即集合覆蓋的次模性，邊際遞減是結構自帶而非人工凹函數。

**自動即時供貨**：某站手上可關的 line 將盡時，派往該站的第一台貨架邊際價值最大（覆蓋自 0 起算），模型自動優先補該站。**「快沒事做的站優先補貨」是最優解的性質，不是提前量規定的行為。**

**無呆板前瞻**：模型中不含任何時間預測。這是刻意的——WHCA* 的等待延遲使行程時間本質上不可預測（2026-06-28 已放棄 travel-time 估計方向），將不可預測量放進最佳解比呆板常數更糟。本模型改以**存量**（各站手上可關的 line 數）取代**時程**，存量在決策時完全確定。

---

## 4. 拆單邊際價值診斷（附帶產出）

`SplitMarginalProbe`（gated，預設關閉）：每 N 次決策，對同一快照額外求解一次「禁止拆單」版本——加上

```
q̂[o,i,p,s] ≤ rem(o,i) · g[o,i,p,s] ,  Σ_{p,s} g[o,i,p,s] ≤ 1
```

即每條 line 只能由單一 (貨架, 站) 供給。兩次求解目標值之差即為**該次決策中拆單的邊際價值（公尺）**。

逐次記錄至 `m4g_split_value.csv`（欄位：決策序號、模擬時刻、待辦數、可用車數、允許拆單目標值、禁止拆單目標值、差值、拆單解的 line 關閉數、禁拆解的 line 關閉數）。

此曲線直接回答「這些拆是不是有意義的拆」，且不需另跑對照實驗。**無論結果正負都寫進論文**：若邊際價值長期接近 0，即為「拆單在此情境無效」的乾淨負結果。

---

## 5. 實作面

### 5.1 檔案

| 檔案 | 動作 |
|---|---|
| `RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs` | **新建**。繼承 `M1GManager`（沿用其 `AllocateOrder`、`GenerateCs`、候選集建構等基礎設施），覆寫 `DecideAboutPendingOrders` |
| `RAWSimO.Core/Control/Defaults/OrderBatching/M4GPricing.cs` | **新建**。λ/μ/δ/ε 的跑動統計與計算，純函式 + 內部狀態，可單獨測試 |
| `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs` | 新增 `M4GConfiguration : M1GConfiguration` 與 §5.2 欄位 |
| `RAWSimO.Core/RAWSimO.Core.csproj` | 手動加入兩個新檔的 `<Compile Include>`（net48 legacy csproj 不自動抓） |
| `Material/Instances/CoreBenchmark/small/m4g.xconf` | **新建**基準組態 |

**完全不動**：`M1GManager.cs`、`HADGSManager.cs`、`SplitM2eICManager.cs`、任何引擎檔、正典 `split_milp_m3g.xconf`。

繼承鏈訣竅：`M4GConfiguration : M1GConfiguration` 使引擎中所有 `is M1GConfiguration` 的型別檢查自動通過，無須改任何引擎檔（前例：`SAM1GConfiguration`、`SplitM2eICConfiguration`）。

拆單資料層重用 Spec 1 既有基礎設施：`Order.CreateSplitChild`、`GetRemainingDemand`/`RemainingPositions`/`IsFullyClaimed`、`SplitMilpDecoder`、`SplitConsolidationLogger`。不重新發明。

### 5.2 組態欄位

```csharp
public class M4GConfiguration : M1GConfiguration
{
    // ── 價格校準（§3.5）──
    public double LambdaScale  = 1.0;
    public double MuScale      = 1.0;
    public double DeltaScale   = 1.0;
    public double EpsilonScale = 0.001;
    public int    WarmupLines     = 50;
    public double LambdaFallback  = 10.0;
    public double DeltaFallback   = 0.5;
    /// > 0 時改用固定值，忽略跑動統計（開迴路消融用）
    public double LambdaFixed = 0;
    public double DeltaFixed  = 0;

    // ── 消融（§6）──
    /// true 時強制 q == q̂，估值層退化 => 應重現 M3G 類行為
    public bool DegenerateToBindingOnly = false;
    /// 估值層納入的訂單數上限（0 = 不限）。求解過慢時的收斂旋鈕
    public int ValuationOrderLimit = 0;

    // ── 診斷（§4）──
    public bool SplitMarginalProbeEnabled = false;
    public int  SplitMarginalProbeEveryNDecisions = 50;
    public double SplitMarginalProbeTimeLimitSec = 10;
}
```

### 5.3 C# 7.3 / 建置

- 語言版本 net48 legacy，**C# 7.3**：不可用 records、switch expressions、target-typed new
- `out` 參數不可被 lambda 捕獲（CS1628）：需要時以 `var copy = outParam;` 區域變數包一層
- 建置：`MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`（x86 因 Gurobi 僅有 win64 版會 runtime 失敗）

### 5.4 提交語意

`CommitM4GResult` 只落實綁定層：

1. `q[o,i,p,s] > 0` → `pod.JustRegisterItem(sku)` 註冊撿貨請求、寫入 `_Ziops[station]`
2. 該單在本次未被完全滿足 → `Order.CreateSplitChild` 建立子單、`TransferExtractRequests`、`AllocateOrder(child, station)`
3. 完全滿足且非母單 → `AllocateOrder(order, station)`（快路徑，不建子單）
4. **`q̂` 中未被綁定的部分不產生任何副作用**——不建子單、不佔槽、不註冊請求。它只在目標式中被計價過，隨本次求解結束即丟棄。

---

## 6. 驗證計畫

基準：`small.xlayo` + `fixed_fill1350_inv70.xsett` + `orders_fill1350.xorders`（唯一乾淨的單變數對照：與 Fill 母本逐字比對僅差 Name / OrderMode / FixedInventoryConfiguration 三行）。探索階段 seed 0，方向確認後補 seed 1。

**已有的四方參照數據**（`fixed_fill1350`, seed 0）：

| 模型 | TP | 件數 | 飢餓s | pile-on | 趟次 | EOR | m/item |
|---|---|---|---|---|---|---|---|
| M1G | 312.5 | 2835 | 388.8 | 1.619 | 771 | 3.600 | 19.12 |
| M3G | 295.0 | 2823 | 500.0 | 3.576 | 328 | 2.040 | 10.54 |
| HGS | 305.0 | 2859 | 149.9 | 4.420 | 274 | 1.847 | 10.12 |

**主要指標**：每 line 距離（目標函數本身）、pile-on、貨架趟次、EOR、完成訂單數、站台飢餓秒數。

**驗收 1（結構）**：`DegenerateToBindingOnly = true` 時，估值層退化，行為應落在 M3G 附近。**不要求逐位相同**——目標式常數不同（λ/μ 取代 40/30/20），要求的是 pile-on 與趟次落回 M3G 量級。若退化版反而優於完整版，即為估值層無效的證據，須誠實報告。

**驗收 2（供給上限確實成為贅物）**：完整 M4G 不含 `icLGcap`。若 pile-on 未崩潰（≥ M3G 的 3.576），即證明拆單可由估值支撐、不需人工上限——**這是本設計的核心可證偽預測**。

**驗收 3（外生訂單流穩健性）**：M3G 由 Fill 模式換到固定訂單檔時 pile-on 掉 22%（4.58→3.58），HGS 幾乎不變。M4G 應顯著小於 22%。此為「估值視窗窄導致對 backlog 敏感」這一診斷的直接檢驗。

**劑量反應**：`LambdaScale ∈ {0.5, 1, 2}`、`MuScale ∈ {0.5, 1, 2}`、`DeltaScale ∈ {0.5, 1, 2}` 各掃一軸。若三軸皆 inert，即為「瓶頸不在目標式」的乾淨負結果（有前例：`w_pipe`/`w_starve` 全 inert），同樣寫進論文。

**求解時間**：估值層變數不再被槽位剪枝，規模上升。以 `m4g_decision_log.csv` 記錄每次求解秒數與變數數。若中位求解時間超過現行 M3G 的 3 倍，用 `ValuationOrderLimit` 由大往小掃找可接受點，並將該限制誠實記為模型的可擴展性邊界。

---

## 7. 風險

1. **估值過度樂觀。** 估值層不受槽位限制，可能為「這輪根本進不了槽」的 line 計分而過度派車。緩解＝`δ` 折價（§3.3 T3）以實測兌現率自動收斂。**這是本設計最可能的失敗模式，須在消融中優先檢查 δ 的實際值。**

2. **求解時間爆炸。** 見 §6 的量測與 `ValuationOrderLimit` 收斂路徑。現行 exact 已比貪婪慢約 45 倍，尚有餘裕但不多。

3. **綁定凍結窗口未處理。** 子單於**派車瞬間**建立（`Order.CreateSplitChild` 永久扣減母單帳，程式中無反悔 API），而非撿貨瞬間。貨架在途期間拆法已凍結。本 spec **不處理**此問題，理由：每次決策僅落實約 1.49 單位，凍結量小；且三次獨立的晚綁定實驗全部無效益（[[project-late-binding-dead-end]]）。**待辦**：量測「子單建立到實際撿走的間隔」與「該期間 backlog 變動量」，若窗口長且變動大再另開 spec。

4. **λ 的回饋迴路。** λ 由模型自己造成的距離統計算出，理論上可能自我強化或震盪。緩解＝`LambdaFixed` 提供開迴路對照；若動態版不穩即改固定值。

5. **仍是單期近視的。** 模型不含時間軸，不預測未來訂單流。估值層擴大了「看得見的範圍」但沒有擴大「看得見的時間」。**此限制須誠實寫進論文，不可宣稱解決了跨期問題。**

6. **拆單邊際價值是瞬時量。** §4 的差值是「此刻資訊下拆單值多少」，不是最終實現的節省（只有第一步被執行）。論文須寫為「每次決策中拆單的邊際價值分布」，總體節省仍以整場模擬的最終指標衡量。

7. **單位一致性依賴 `StarveAwareCostEnabled = false`。** 以硬失敗防呆（§3.5），不留靜默錯誤路徑。

---

## 8. 明確不做的事

- 不加時間索引、不做多期模型、不預測行程時間（WHCA* 等待延遲不可預測）
- 不改 `M1GManager.cs` / `HADGSManager.cs` / `SplitM2eICManager.cs` / 任何引擎檔
- 不改正典 `split_milp_m3g.xconf`（M3G 保留為 ablation 對照，tag `m3g-v1-line30`）
- 不處理綁定凍結窗口（§7.3）
- 不重啟已放棄的方向：晚綁定、CBS、RL、分散式控制、PP 成本回饋進 MILP、travel-time/wait-delay 近似估計

---

## 9. 待使用者確認

1. §2 的十條決策是否同意？特別是 **D6 刪除 `icLGcap`** 與 **D7 刪除 `PipelineFloor` 全套**——這兩項若失敗，pile-on 可能大幅退步。
2. §3.5 的三個價格自我校準公式（尤其 `μ = λ × 平均每單 line 數`、`δ = 估值兌現率`）是否符合你的直覺？
3. §6 的驗收 2（無上限下 pile-on 不崩潰）作為核心可證偽預測，是否同意以此判定本設計成敗？
