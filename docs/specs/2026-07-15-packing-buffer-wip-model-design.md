# Packing-Buffer WIP 模型設計（拆單的 consolidation 緩衝硬限制）

> 日期：2026-07-15　分支：6/24
> 事實來源：程式碼 / git log / memory。本 spec 定案 brainstorm 決策，供 writing-plans 產出逐字實作計畫。
> 前置討論：`docs/2026-07-14-m2e-sunk-first-defense-strategy.md`（sunk-first 由本模型取代為主 lever）。

---

## 1. 動機：目前拆單的 WIP 成本是「零」（建模缺口）

拆單開放後，模擬器對拆單的物理成本嚴重低估。根因在 `OutputStation.RemoveAnyCompletedOrder`（`RAWSimO.Core/Elements/OutputStation.cs:331-387`）：

- 子單一撿完，`_assignedOrders.Remove(finishedOrder)`（:374）**立刻執行**，而 `CapacityInUse = _assignedOrders.Count`（:97）。→ **子單完成即釋放揀貨站 slot，完全不等 parent 整併。**
- 整併只是 `Order.NotifyChildCompleted` 的一個 `HashSet` bookkeeping（`RAWSimO.Core/Items/Order.cs:295-303`）：瞬間、零成本、**不佔任何緩衝**。

物理後果：把一張單拆成 N 個 child、賺到 pile-on、slot 馬上釋放，而「半成品 parent 堆著等手足到齊」佔用**零**空間。過度拆分在物理上該付的 WIP 代價，模型沒收。這解釋了 slot-ceiling 診斷（早綁定＝WIP 停車場）與 PVGS 高 pile-on（8.28）中「免費堆無限半成品」的成分。

**本 spec 補上這個缺口：加入下游 consolidation 緩衝（packing station）作為全域硬限制，讓每一次拆分都要從有限的 WIP 預算裡付租金，直到整併才退租。**

### 1.1 與 M2e-PR 負結果的區別（關鍵）

M2e-PR 的教訓是「目標式端獎勵重塑無效」（見 memory `project_m2e_pr_negative.md`）。本模型是**限制式端**的物理約束，不是目標式獎勵形狀，是不同類 lever。完成偏好會**內生地**從約束跑出來，不需要 sunk-first two-solve 的求解結構機械。

---

## 2. 物理模型（兩段式串聯）

```
        揀貨站 (OutputStation, 上游)            合併站 (PackingBuffer, 下游, 全域)
        ─────────────────────────────          ─────────────────────────────────
 完整單  撿完 → 直接出貨                          （bypass，永不進入、零佔用）
 拆單    每個 child 撿完 → 釋放揀貨 slot  ──送資料──▶  佔 1 格，直到 parent 整併
                                              parent 全員到齊 → 整併 → 該 parent
                                              所有格子一次全部釋放
```

規則（使用者 2026-07-14/15 定案）：

- **完整單（不拆）**：撿完直接出貨，**不進 packing、零緩衝佔用**。packing 滿載不影響完整單吞吐。
- **拆單 child**：每完成一個 child → 送下游佔 **1 格**。一個 parent 拆成 k 個 child，隨 child 陸續完成逐格累積；最後一件完成觸發整併 → 該 parent 佔的格子**一次全部釋放**。
- **拆分上限 = 物品需求量**：需求 30 的單最多拆 30 個 child（single-unit 拆到底）→ 單一過度拆分的 parent 可獨吞近 30 格。
- **全域硬限制**：packing 同時暫置的 child ≤ `Bcap`（測試案例 = 78）。
- **零轉移/整併時間成本**：packing 只是資料 hand-off，不模擬封裝時間、不模擬搬運成本。

因整併是跨站的（parent 的 child 可散在不同揀貨站），緩衝**全域唯一**、不分站。

---

## 3. 數學模型

### 3.1 集合與既有變數（M2e / SplitM1GExactManager）

- `O` = 本決策期考慮的 pending orders（含 fresh 與跨期 parent），`S` = 揀貨站。
- `y[o,s] ∈ {0,1}`：order `o` 佔用站 `s` 一個槽位（既有變數；受 `eshi4` 槽守恆 `Σ_o y[o,s] = Cs[s] − us[s]`，`SplitM1GExactManager.cs:669`）。
- `z[o] ∈ {0,1}`：order `o` 本期整單完成旗標（既有 `zdonex`/`zfullx`，:704）。
- `q[i,o,p,s] ≥ 0`：既有 unit 級分配變數。

Decode 對應（`SplitM1GExactManager.cs:1160-1213`）：
- **Fast path**（`parts.Count==1 && fullyAssigned && !order.IsSplitParent`, :1178）→ 不建 child，當完整單直接分配。
- **Split path**（else, :1192-1212）→ 每個 part 呼叫 `Order.CreateSplitChild`（:1197），`nChildren++`。
- ∴ **本輪新開 child 數 = split-path 的 parts 總數。**

### 3.2 決策期讀取的即時狀態（引擎提供的常數）

決策開始時，引擎提供三個常數：

| 符號 | 意義 | 來源 |
|---|---|---|
| `Bcap` | packing 容量（78） | config |
| `Bocc` | 當前 packing 佔用（已完成但 parent 未整併的 child 數） | `PackingBuffer.Occupied` |
| `Cpick` | 當前佔用揀貨站槽位、尚未完成的 child 數（`Parent != null`） | `Instance.CountChildrenAtPicking()` |

**本輪拆分預算（使用者給定的管線預留邏輯）：**

```
Budget = max(0, Bcap − Bocc − Cpick)
```

減 `Cpick` 的理由：那些在途 child 一旦撿完就會**流入 packing**，是「已在管線上、預定佔 packing 格」的量，必須先替它們保留空間；剩下的才是本輪能安全開的**新** child 額度。這是最壞情況保守估計（假設在途 child 無一即時整併），保證 packing 永不溢出。`Budget` 夾在 0：管線飽和時本輪不准開新 child，強制去完成/整併既有半成品（正確節流）。

**保守性**：此式略微低估可用空間（有些在途 child 其實是 parent 最後一件、完成即整併、不淨佔 packing）。刻意接受此保守（安全 > 邊際利用率）；probe 量測 binding 頻率後再評估是否放寬。

### 3.3 新增變數與限制式（child-count 線性化）

新增 fast-path 指示變數 `fp[o] ∈ {0,1}`：order `o` 是否為 fast-path（整單、單站、本期完成、非既有拆單 parent）。

**Fast-path 資格限制式：**

```
(pb1)  fp[o] ≤ z[o]                       ∀o∈O        // 必須整單完成
(pb2)  fp[o] ≤ Σ_s y[o,s]                 ∀o∈O        // 必須實際被分配
(pb3)  fp[o] ≤ 2 − Σ_s y[o,s]             ∀o∈O        // 佔 ≥2 站則強制 fp=0
(pb4)  fp[o] = 0                          ∀o∈O 且 o.IsSplitParent  // 既有拆單 parent 永走 split path
```

`(pb2)+(pb3)` 合起來：`Σ_s y[o,s] = 1` 時允許 `fp=1`，`≥2` 時強制 `fp=0`，`=0` 時（配合 pb1，z=0）強制 `fp=0`。

**本輪新開 child 數的線性表達：**

```
新開 child 數 = Σ_{o,s} y[o,s] − Σ_o fp[o]
```

每個槽位佔用要嘛是 fast-path 整單的唯一槽（被 `fp` 減掉 → 淨 0 child），要嘛是一個 child（+1）。

**預算硬限制式：**

```
(pb5)  Σ_{o,s} y[o,s] − Σ_o fp[o] ≤ Budget
```

`fp` 只出現在 `(pb5)`（放鬆該約束），求解器必然對所有合格 order 設 `fp=1`（最大化 `Σfp`）→ child 數被精確計數，無副作用。`Budget` 寬鬆時（packing 少滿）`(pb5)` 鬆弛、不影響解。

**不相交性（避免雙重計數）**：`y` 僅涵蓋本期 pending 集合 `O`（既有 `pendingOrders`，已委派的前期在途 child 不在其中）。故 `Σy − Σfp`（本期**新開** child）與 `Cpick`（前期**在途** child）互斥、二者在 `Budget` 式中分別對 `Bcap` 保留空間，不重疊。

### 3.4 目標式：不動

**目標式完全不改。** 本模型是純限制式 lever。這是與 M2e-PR 的關鍵切割：不在 objective 加任何獎勵/懲罰項，避免不對稱調參嫌疑。預設權重直接沿用。

---

## 4. 引擎架構（`PackingBuffer` 元件）

### 4.1 元件與歸屬

新增 `PackingBuffer`（全域，掛在 `Instance` 上，因整併跨站）。狀態：

- `Capacity`（int，來自 config；`≤0` = 停用 = 無限緩衝 = 現行行為）。
- 內部持有「當前暫置中的 child 集合」（`HashSet<Order>` 或 per-parent 計數），`Occupied` = 集合大小。

不變式：`Occupied = Σ over 未整併 parent (其已完成 child 數)`。

### 4.2 佔用會計（掛進既有 consolidation 事件鏈）

修改點在 `OutputStation.RemoveAnyCompletedOrder`（`OutputStation.cs:343-360`），**不新增 KPI 語意、只加緩衝更新**：

- **非最終 child 完成**（else 分支, :354-359）：`PackingBuffer.Enter(child)` → `Occupied++`。
- **parent 整併**（if 分支, `NotifyChildCompleted` 回 true, :348-353）：`PackingBuffer.ReleaseParent(parent)` → 移除該 parent 所有暫置中的 child → `Occupied −= (releasedCount)`。releasedCount 即該 parent 走過 else 分支的 child 數（= `_children.Count − 1`，最後一件走 if 分支未進 packing）。用集合移除以對 idempotent 重複通知穩健。
- **完整單 / 最終 child**：不進 packing（bypass）。

### 4.3 後備牆（back-pressure，非主要路徑）

若**非最終 child** 將完成而 `Occupied == Capacity`：**不在本 tick 完成該 child**，令其續留 `_assignedOrders`（揀貨槽位被 hold）→ 該站反壓 → 下個事件重試，直到 packing 釋位。完整單與最終 child（觸發整併者）**永不被反壓**（不淨佔 packing）。

決策端預算（§3.2）已保證 budget-respecting manager 不會逼近此牆；後備牆是 (a) 正確性保險、(b) 對忽略預算之 manager 的地面真值強制。

### 4.4 即時計數對外暴露

- `PackingBuffer.Occupied`（→ `Bocc`）。
- `Instance.CountChildrenAtPicking()`：掃所有站 `_assignedOrders`、數 `Parent != null` 且未完成者（→ `Cpick`）。決策期呼叫，反映**先前**epoch 遺留的在途 child（本期新解尚未套用）。

### 4.5 Config 閘門與可重現性

- 新增 config 欄位 `PackingBufferCapacity`（int，預設語意見下）。
- **拆單實驗 xconf**（M2e / PVGS / SplitM1G 變體）設 `= 78` → 緩衝啟用。
- **非拆單 baseline**（M1G / HADGS）：緩衝即使啟用也因永不建 child 而 `Occupied ≡ 0`、`Cpick ≡ 0` → 完全 inert → 既有結果 bit-identical 可重現。
- 預設值 `≤0`（停用），保證任何未顯式開啟的既有 xconf 行為不變。
- 供 **buffer on/off ablation**：同一 manager 跑 `78` vs `停用` 兩組，隔離出緩衝的效應。

引擎在初始化時從 `Instance.Controller.OrderManager` 的 config 讀 `Capacity`（具體 config 類別由 writing-plans 定；不碰 `M1GManager.cs` / `HADGSManager.cs`）。

---

## 5. Manager 整合

| Manager | 檔案 | 拆單? | 本模型作用 |
|---|---|---|---|
| **M2e** | `SplitM1GExactManager.cs` | ✓ MILP | **加 §3.3 預算約束**（fp 變數 + pb1–pb5），主 lever |
| **PVGS** | `PVGSManager.cs` | ✓ heuristic | decode guard：`CommitParts`（:342-363）走 split-path 前檢查即時 `Budget`，耗盡則不建新 child（殘餘留待後續 epoch），與 M2e 對稱受同一物理牆 |
| **SplitM1G 變體** | `SplitM1GManager.cs` / `SplitM1GLBManager.cs` | ✓ | 同 PVGS：decode guard（若納入比較則加） |
| **HADGS** | `HADGSManager.cs` | ✗ | **不動**（不在 `CreateSplitChild` 呼叫者清單）→ 自動免疫 |
| **M1G** | `M1GManager.cs` | ✗ | **不動** → 自動免疫 |

### 5.1 為何 M2e 得 MILP 約束、PVGS 只得 decode guard

兩者受**同一物理牆**（78 buffer，引擎層自動）。差別：M2e 在 MILP 內**主動最優分配**稀缺預算（`(pb5)` 讓求解器決定哪些單值得拆、拆多細）；PVGS 貪婪 `CommitParts` 只能**被動不超支**。若 M2e 勝，正是因為它把稀缺 packing 預算用得更聰明——這就是要展示的 MILP 價值，且兩者都誠實受牆、非人為削弱 PVGS。

---

## 6. Ablation 與公平性

- packing 緩衝為**引擎層物理**，凡拆單 manager 一律受約束、非拆單自動免疫：
  - **小規模 M2e vs M1G**：M1G 免疫、M2e 受約束 → 乾淨隔離拆單的真實 WIP 成本。
  - **大規模 PVGS vs HADGS**：HADGS 免疫、PVGS 受約束 → PVGS 的免費-WIP pile-on 會縮水 → 比較物理誠實。
- 緩衝只懲罰每一對中的**拆單臂**，正是同族防禦策略要的（見 `docs/2026-07-14-m2e-sunk-first-defense-strategy.md`）。
- 不碰 `M1GManager.cs` / `HADGSManager.cs`（專案硬性約束）。所有改動落在引擎（`OutputStation.cs` / `Instance` / `Order.cs` 唯讀）與拆單 manager 鏡像檔。

---

## 7. 驗收準則

**主張（機制假設）**：加入真實 packing 緩衝後，(a) PVGS 的免費-WIP pile-on 縮水；(b) M2e 靠 MILP 最優分配稀缺預算，於 TP 與 pile-on 追平或超越 PVGS。

**驗收（定死）**：
- 5 seeds、small 標準案例（`small_o100_mu100`）、`Bcap=78`。
- **預設權重**（不用 m2e_C 的 w4/w5 定價，避免不對稱調參嫌疑）。
- **TP 與 pile-on 兩項都 M2e ≥ PVGS**。
- 附 **buffer on/off ablation**（M2e 與 PVGS 各跑 78 vs 停用）佐證機制：關牆時複現舊數字、開牆時 PVGS pile-on 下降。

**Probe（先量後判）**：新增 packing 佔用時序 log（epoch, time, Bocc, Cpick, Budget, 本輪 nChildren, 是否 binding）。用以：(1) 確認 78 真的會 binding（否則機制空轉）；(2) 量 binding 頻率評估 §3.2 保守性代價。

**次要主張**：物理正確性本身即貢獻——即使排序未翻轉，補上 WIP 成本使模型更忠實；驗收 report 應同時陳述此點。

---

## 8. 風險

1. **78 不 binding → 機制空轉**：若 packing 容量遠大於實際 WIP 峰值，`(pb5)` 永遠鬆弛、無效應。緩解：先跑 probe 量 `Bocc` 峰值 vs 78；若不 binding，與使用者確認真實 packing 容量（78 為使用者給定，可能對應實體封裝站位數）。
2. **對 M2e 也是約束、未必翻轉排序**：緩衝同時限制 M2e。假設「MILP 最優分配 > PVGS 貪婪」須由驗收檢驗，非保證。備援敘事＝物理正確性（§7 次要主張）。
3. **fast-path 線性化與 decoder rounding 對不齊**：`(pb1–pb3)` 用 `y`/`z` 近似 decoder 的 `parts.Count`/`fullyAssigned` 判準；`q` rounding 邊界可能使 MILP 預估 child 數與實際 decode 差 ±1。緩解：預算保守（§3.2）already 留裕度；probe 比對「MILP 預估 nChildren」vs「decode 實際 nChildren」抓偏差。
4. **後備牆致死鎖疑慮**：若所有站都被反壓且無 child 能整併 → 卡死。實際不會：完整單與最終 child 永不反壓，整併總能推進至少一個 parent 釋位。仍需迴歸測試涵蓋「packing 打滿」情境確認可解。
5. **baseline 重跑成本**：所有拆單 manager 數字位移，需重跑 PVGS/M2e baseline（HADGS/M1G 免疫、不需重跑）。

---

## 9. 範圍外（延後，勿主動做）

- **sunk-first two-solve**：由本模型取代為 M2e≥PVGS 主 lever；擱置，驗收若仍差再疊。
- **Od firing-rate 量測 / OdMode ablation**：獨立線，不納入本 spec。
- **大規模 C 系列**（HADGS vs PVGS, large/bot=45）：通過本 spec 驗收後才進。
- **per-unit reward / pod-aware 定價模型**：不動目標式，範圍外。
