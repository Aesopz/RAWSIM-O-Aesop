# 設計文件：包裝緩衝容量限制與拆單份數上限

2026-09-12 · M4G + HGS-M5 · brainstorm 已定案

---

## 0. 一句話

把拆單的下游代價從「未建模的效度威脅」變成**模型裡的一條限制式**：每張拆單母單佔用一個
包裝緩衝箱位，直到合併完成才釋放；當期能不能拆、能拆幾份，由剩餘箱位與份數上限共同決定。

---

## 1. 動機

`docs/defense material/2026-09-12-research-plan-next.md` 第 5 節把「**合併成本沒有被建模**」
列為第一號效度威脅：我們量了拆單的效益（公尺/行 −34.57%、EOR −28.65%），卻沒有量它把
成本搬到哪裡去。教授對「無病呻吟」的批評，最強的讀法正是這一條。

本設計同時處理兩個旋鈕：

| 旋鈕 | 性質 | 回答的問題 |
|---|---|---|
| 包裝緩衝容量 `C` | **內生資源限制** | 拆單的代價在哪裡兌現？系統會自己撞到牆嗎？ |
| 份數上限 `N` | **外生政策旋鈕** | 效益需要拆到多少份才拿得到？ |

兩者疊加後，`N` 不再是憑空的人為參數，而是**容量的函數** —— 箱位緊張時系統自動退回
不拆單模式。

---

## 2. Brainstorm 定案（不可再議，除非有新證據）

| 決策 | 定案 | 理由 |
|---|---|---|
| `N` 的計數單位 | **每張母單一生的份數（placement 次數），跨期累計** | 「站台數」在小規模天生上限為 2（只有 2 個揀貨站），掃不出曲線 |
| `N` 的上界 | 該訂單的 order-line 數 | 正典 `LineAtomicSplitting = true`，一條 line 不可再分 |
| 箱位消耗者 | **只有拆單母單** | 未拆訂單在單站揀完直接裝箱出貨，不需要合併箱位。亦為 `PackingBuffer` 現有語意 |
| 箱位滿載時 | **只禁新的拆單**，已拆開的母單繼續服務 | 系統自動退化成不拆單模式，不會自造死鎖（見 [[feedback-no-self-inflicted-findings]]） |
| 實作範圍 | M4G 與 M5 **同時** | [[feedback-m5-must-mirror-m4g]]；大規模確認那一步需要 M5 |

---

## 3. 現況盤點（已查證，不是推測）

### 3.1 `PackingBuffer` 已存在且語意正確

```
RAWSimO.Core/Elements/PackingBuffer.cs

  一個箱位 per 拆單母單
  母單首次被拆時 RegisterParent（decode time，保守：早於實體第一箱抵達）
  合併完成時 ReleaseParent
  Capacity <= 0 表示無限（僅追蹤，不約束）
  AliveParentCount = B_occ
  出處註明 Xie et al. (2021) Appendix B：78 boxes per shelf
```

組態欄位亦已存在（`MethodConfigurationsOB.cs:1157-1166`）：
`PackingBufferCapacity = 78`、`PackingStationCount = 1`，總容量 `C = 兩者相乘`。

### 3.2 釋放端不用寫

```
OutputStation.cs:367    Instance.PackingBuffer.ReleaseParent(parent.ID)
```

位於引擎層的 `RemoveAnyCompletedOrder` → `NotifyChildCompleted` 鏈上，**所有 manager 共用**，
且以 `Instance.PackingBuffer != null` 守衛。只要不建立 buffer，其他臂完全不受影響。

### 3.3 註冊端有現成鉤子

```
SplitM2eICManager.cs:1927    既有前例
M4GManager.cs:3200          if (order.IsSplitParent && !order.IsFullyClaimed && _splitSeen.Add(order.ID))
```

`_splitSeen` 每張母單恰好觸發一次，正是需要的時機。

⚠️ **但 `_splitSeen` 目前是「探針」用途**（split-lifetime log）。把控制邏輯掛在探針集合上會
造成量測與控制耦合 —— 日後關掉探針就會靜默改變行為。**必須另建一個權威集合**，探針維持獨立。

### 3.4 實測基線（正典 M4G，small 6 bots，seed 0，673 張母單）

```
份數分布                        不同站台數分布
  1 份  462 張  68.6%            1 站  553 張  82.2%
  2 份  179 張  26.6%            2 站  120 張  17.8%
  3 份   29 張   4.3%
  4 份    3 張   0.4%
```

⟹ **`N ∈ {1, 2, 3, ∞}` 即可涵蓋 99.6%**，`N = 4` 只影響 3 張訂單，不值得跑。

訂單組成（`small_o100_mu100_2h_inv70.xsett:57-60`）：
`OrderPositionCount` 最小 1、最大 5、平均 1。

---

## 4. 數學模型（M4G）

沿用 `docs/thesis/m4g-objective.tex` 的符號慣例：所有集合寫成 `X(t)`，加總標明完整索引集合。

### 4.1 新增參數

```
C            = PackingStationCount × PackingBufferCapacity        總箱位數，<= 0 表示不限
B_occ(t)     = 決策時刻仍佔用箱位的拆單母單數（= PackingBuffer.AliveParentCount）
N            = 每張母單一生允許的最大份數，<= 0 表示不限
n_o(t)       = 訂單 o 在 t 之前已經用掉的份數（跨期累計）
S(t)         = { o ∈ O(t) : o 已是拆單母單 }                       已持有箱位者
```

### 4.2 新增決策變數

```
g[o,w] ∈ {0,1}     訂單 o 本期在站台 w 取得至少一個 placement
t_o    ∈ {0,1}     訂單 o 本期被服務（任一站台）
s_o    ∈ {0,1}     訂單 o 本期成為「新的」拆單母單（本期消耗一個箱位）
```

`z_o ∈ {0,1}`（本期完成）為既有變數，直接沿用。

### 4.3 連結限制式

```
(G1)   g[o,w]  ≥  b[o,i,w]                      ∀ o, ∀ i ∈ L_o, ∀ w ∈ W_a(t)
(G2)   g[o,w]  ≤  Σ_{i ∈ L_o} b[o,i,w]          ∀ o, ∀ w ∈ W_a(t)
(T1)   t_o     ≥  g[o,w]                        ∀ o, ∀ w ∈ W_a(t)
(T2)   t_o     ≤  Σ_{w ∈ W_a(t)} g[o,w]         ∀ o
```

其中 `b[o,i,w]` 為既有的「行 (o,i) 本期在站台 w 被綁定關閉」指示變數。

⚠️ **實作時必須綁到正確的既有符號**。`M4GPlacement` 帶有 `GbName` / `GhName`，
而 compact line model 與 order-atomic path 的 places 展開方式不同
（`M4GManager.cs:2604`）。**不可憑名字猜**，要先讀碼確認 `Gb` 的索引是
`(o, i, w)` 還是 `(o, w)`；若已經是 `(o, w)`，則 `g` 直接沿用，G1/G2 可省。

### 4.4 份數上限

```
(N1)   Σ_{w ∈ W_a(t)} g[o,w]  ≤  N − n_o(t)      ∀ o ∈ O(t)，當 N > 0
```

`n_o(t)` 是參數不是變數，由 manager 在每次 commit 後累加。

⚠️ `N − n_o(t)` 可能為 0 ⟹ 該訂單本期完全不可被服務。這是**正確行為**（它已經用完份數配額，
只能等既有的份被完成），但必須確認不會造成訂單永遠無法完成 —— 見 §7 風險 R2。

### 4.5 包裝緩衝容量

```
(S1)   s_o  ≥  t_o − z_o                         ∀ o ∈ O(t) \ S(t)
(S2)   s_o  ≥  0                                  （由變數定義域保證）
(B1)   Σ_{o ∈ O(t) \ S(t)} s_o  ≤  C − B_occ(t)   當 C > 0
```

讀法：一張**尚未持有箱位**的訂單，若本期被服務（`t_o = 1`）卻沒有完成（`z_o = 0`），
就會留下殘餘需求而成為拆單母單，必須消耗一個箱位。已在 `S(t)` 的母單不再消耗。

`s_o` 不需要上界限制式 —— 目標式沒有獎勵 `s_o`，求解器不會無故把它設為 1。

⚠️ **`s_o` 不進目標式**。它是純粹的計數變數，加任何係數就等於偷渡一個手調價格，
違反自我校準的核心主張（見 [[project-slot-penalty-rejected]] 的前例）。

---

## 5. HGS-M5 的對應

M5 以貪婪構造取代 MILP，沒有限制式，改為**在列舉移動時過濾**：

```
EnumerateLineMoves 產生候選移動時，跳過滿足以下任一條件者：

  (a) 份數上限
      該移動會使 parts[o] 超過 N            parts[o] 在本次決策內即時累加

  (b) 箱位容量
      o ∉ S(t)                              尚未持有箱位
      且 本次決策已新增的母單數 ≥ C − B_occ(t)
      且 該移動無法讓 o 在本期完成           即它會留下殘餘
```

條件 (b) 的「無法在本期完成」在貪婪逐步構造中不可預先得知，因此採**保守判定**：
只要 `o` 尚未持有箱位、且本次決策的新母單額度已用盡，就不允許對 `o` 做**部分**服務，
但仍允許能一次覆蓋全部殘餘的移動。

⚠️ 這使 M5 比 M4G 保守一些。這屬於 [[feedback-m5-must-mirror-m4g]] 允許的
「貪婪提速造成的差異」，但**必須在論文中明說**，並由橋接格量化。

---

## 6. 架構決策

### 6.1 組態欄位（新增於 `M4GConfiguration` 與 `GreedyM5Configuration`）

```csharp
/// <summary>Total consolidation boxes = PackingStationCount * PackingBufferCapacity.
/// 0 or less = unlimited (the buffer is still created and tracked, but never binds),
/// which is the default and keeps canon bit-identical.</summary>
public int PackingStationCount = 0;          // 0 = 功能關閉
public int PackingBufferCapacity = 78;       // Xie et al. 2021 Appendix B

/// <summary>Maximum parts a parent order may be split into over its whole life,
/// counted across epochs. 0 or less = unlimited (canon).</summary>
public int MaxPartsPerOrder = 0;
```

🚨 **預設一律為「關閉」**，且正典 xconf 不寫入這些欄位 ⟹ 正典行為逐位不變。
這是 [[feedback-optimisation-priority]] 的第一原則（可解釋性、大道至簡）與本專案
「新功能一律旗標化預設關閉」慣例的要求。

🚨 既有的 `MaxChildrenPerOrder`（`MethodConfigurationsOB.cs:931`）**不可重用**：
它屬於已放棄的 `SplitOrderManager` / `SplitPlanner`，且 `SplitPlanner.cs:21` 的語意是
**"Maximal parts per epoch"（每期上限）**，與本設計的一生上限不同。命名刻意區隔為
`MaxPartsPerOrder`。

### 6.2 狀態管理

```
_partsUsed : Dictionary<int, int>       母單 ID → 已用份數，commit 後累加
_boxHeld   : HashSet<int>               母單 ID → 是否已持有箱位（權威集合）
```

`_splitSeen`（探針）**維持獨立**，不得與 `_boxHeld` 合併 —— 見 §3.3。

訂單完成時清除兩者的條目，避免長跑批次記憶體累積。

### 6.3 buffer 的建立時機

鏡像 `SplitM2eICManager.cs:38-42`：在 manager 建構時，若 `PackingStationCount > 0`
且 `Instance.PackingBuffer == null` 才建立。**其他臂不建立 ⟹ 引擎層的
`OutputStation.cs:367` 因 null 守衛而完全不動作。**

---

## 7. 風險

| 編號 | 風險 | 處置 |
|---|---|---|
| R1 | 量測與控制耦合 —— 把 `_boxHeld` 掛在探針的 `_splitSeen` 上 | 建立獨立集合；探針不變 |
| R2 | `N − n_o = 0` 的訂單永遠無法完成 ⟹ 積壓爆炸或死鎖 | **必須驗**：N=1 的跑批要確認完成訂單數與不拆單臂同量級。若不然，代表 (N1) 需要對「能一次完成」的移動開例外 |
| R3 | 箱位滿載造成吞吐崩潰 | 定案已選「只禁新拆單」，系統退化成不拆單而非停擺。跑批後必須核對 `StatOverallOrdersHandled` 未崩 |
| R4 | M5 的保守判定使兩者不可比 | 橋接格（同 instance 跑 M4G 與 M5）量化差異，論文明說 |
| R5 | 正典被污染 | 兩個功能預設關閉；合併前跑逐位一致驗證（9 項 KPI） |
| R6 | `Gb` 的索引猜錯 | 實作前先讀碼確認，見 §4.3 的警告 |

---

## 8. 實驗設計

### 8.1 先導（1 seed，確認 78 會不會咬住）

依 [[feedback-agile-one-seed]]，先用 1 seed 探風向。

```
①  只接 buffer、不設限（C = 0，純觀測）    1 場    量 B_occ 的峰值與分布
```

記憶中曾量到**峰值 117 > 78**。若重現，78 會咬住，實驗有東西可看；
若峰值遠低於 78，則需改用更小的 C 或更多拆單的情境，否則整組實驗是空的。

### 8.2 主實驗（小規模 M4G，5 seeds）

```
N ∈ {1, 2, 3, ∞} × C ∈ {∞, 78}  = 8 臂 × 5 seeds = 40 場 ≈ 80 分鐘
```

若 §8.1 顯示 78 不咬住，退回單因子 `N ∈ {1,2,3,∞}` = 20 場。

### 8.3 大規模確認（M5，3 seeds）

```
取 N = 1、N = 2、N = ∞ 三點 × 3 seeds ≈ 1.5 小時
```

**先寬後深**：形狀在小規模掃，只把端點與轉折拿到大規模確認。
⚠️ 大規模冷啟動第一次決策合法地要 105–115 秒，不要在 90 秒判定當機。

🚨 依 `2026-09-12-research-plan-next.md` §4.1，**小規模 M4G 與大規模 M5 的數字永不同表**。

---

## 9. 驗收標準

1. 兩個功能關閉時，正典 9 項 KPI **逐位相同**。
2. `C = 78` 且 `N = ∞` 時，`B_occ` 的峰值不超過 78（限制式確實生效）。
3. `N = 1` 時的份數分布為「全部 1 份」，且完成訂單數與既有不拆單臂同量級（R2）。
4. `N = ∞`、`C = ∞` 時與正典逐位相同（兩條限制式皆不綁定）。
5. M4G 與 M5 在同一 instance 上的差異落在既有橋接格的量級內（件數 n.s.）。

---

## 10. 後續（不在本 spec 範圍）

- 包裝站數量 `PackingStationCount` 的敏感度 ⟹ 第五章 `sec:exp-capacity` 小節
- `B_occ` 的時間序列圖 ⟹ 可直接回應「成本被搬到合併牆」的批評
- N 的劑量反應曲線 ⟹ 若八成效益在 N=2 就拿到，那是**管理洞察**不是效果宣告

---

## 相關文件

- `docs/defense material/2026-09-12-research-plan-next.md` §2 第 1、2 組；§4.1 案例／求解器分工
- `docs/defense material/2026-09-12-pricing-contribution-chain.md` 定價與拆單的隔離量化
- `docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md` 拆單資料層
- `docs/thesis/m4g-objective.tex` 正典模型的符號慣例
