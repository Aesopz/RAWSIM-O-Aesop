# Spec：M2e-IC — 貨架中心、入站限定的線上拆單 MILP（修正 M2e）

> 日期：2026-07-16　分支：6/24
> 狀態：設計草案（brainstorm 已收斂，待使用者審閱後進 writing-plans）
> 事實來源：程式碼 / git log / memory。本 spec 定案 brainstorm 決策，供 writing-plans 產出逐字實作計畫。
> 命名為**暫定**（`M2e-IC` = Inbound-Committed Split）；使用者可改名。保留 `M2e` 字首是為了論文 ablation 血緣。

前置與血緣：
- 拆單合併語意骨架 = **Xie et al. (2021) split-over-time**（`paper/split.pdf` §3.4 + 附錄 B），本 spec 忠實沿用。
- 線上聯合 + Pa/Pb pod 狀態 = **Jiao et al. (2026) M1G**（EE-RAWSim-O 本體）。
- 修正對象 = **M2e**（`SplitM1GExactManager.cs`，`docs/superpowers/specs/2026-07-07-splitm1g-exact-design.md`）。
- 已排除的死路（不要重提）：目標式端獎勵重塑（eps / CF / pro-rata M2e-PR 三度陣亡）、字典序 sunk-first（五 seed FAIL）、天真晚綁定旗標（引擎 pre-claim 使延後 ~3s，已死）。詳見 memory `project_m2e_pr_negative`、`project_slot_ceiling_diagnosis`、`m2e-sunk-first-negative-result.md`。

---

## 1. 定位：修正 M2e 的哪一個病，以及怎麼修

**M2e 的病灶（診斷已見骨，見 memory `project_slot_ceiling_diagnosis` + 本專案多輪對話）：**

1. **早綁定 + 綁到未到站的 pod**：`SplitM1GExactManager.cs:328-337` 把 `UnusedPods`（還在儲區的閒置貨架）納入候選 `Pa`，`shi13'` 又強迫被派的 Pa pod 一定要被消耗 → **訂單被綁到還在儲區的 pod，佔著槽空轉等它被搬來**。
2. **拆單手足時間不同步**：同一張母單的 child 可被綁在到站時間天差地別的 pod（在場 5s vs 儲區 200s+）→ 母單卡等最慢的 child → consolidation 尾巴拖到 3781s（vs PVGS 48s）。
3. **薄榨**：pile-on 4.6 vs PVGS 8.28；pod 到訪 306 vs 168（同揀貨量）——派太多薄 pod、不肯深榨現場 pod。

**修正的一句話**：把「貨還沒到就綁單」改成「**貨到了才切、切了就撿、撿完就走**」。具體 = **拆單的 child 只能綁在已承諾（在場 or 在途）的 pod，絕不綁儲區閒置 pod**；剩餘需求留 backlog 等未來。榨乾現場 pod 的行為，靠 **結構規則 + M1G 原有填槽壓力 + 成本不對稱** 自然湧現，**不加任何 partial 獎勵**。

**模組邊界（專案硬性約束）**：`M1GManager.cs` / `HADGSManager.cs` / `SplitM1GExactManager.cs` **一行不動**。M2e-IC = 新鏡像檔（`SplitM2eICManager : M1GManager`）。消融階梯擴充為：M0（M1G）→ M1/M2（SplitM1G）→ M1e/M2e（SplitM1GExact）→ **M2e-IC（本 spec）** → H（heuristic）；大規模對照 PVGS。

---

## 2. 設計決策（brainstorm 逐項定案）

| # | 決策 | 定案 | 理由 |
|---|---|---|---|
| D1 | 拆單 child 的 pod 候選 | **只允許在場（processing）+ 在途（inbounded）pod，即 `P^C`（= M2e 的 Pb）；禁止儲區 unused pod（`P^U` = Pa）** | 消除時間不同步的手足（3781s 尾巴的根）。使用者明確指示。 |
| D2 | 未拆單（整單單站完成）的 pod 候選 | 仍可用 `P^U`（保留 M1G 的 fresh-dispatch，兼作補充 inbound 供給） | 使用者限制只針對「被拆」的單；且需維持供給管線 |
| D3 | 榨乾 processing pod 的驅動力 | **不加 partial/pro-rata 獎勵**；靠 D1 + 繼承的 `w3` 填槽壓力 + 成本不對稱（`P^C` 免費、`P^U` 收全程費）湧現 | 目標式端獎勵三度陣亡；此路繞開它 |
| D4 | 完成獎勵 | 沿用 M2e 的 **per-order all-or-nothing `zdone`**（`w2`） | per-unit 有「少張大單」confound；per-order 乾淨 |
| D5 | 拆單型態 | **跨站 + 時空（cross-station + cross-time）皆允許**（沿用 M2e `CrossTime`；殘量留 backlog） | 使用者明確指示；= Xie2021 split-over-time |
| D6 | 下游 consolidation | **packing buffer 容量 `C`（Xie2021 附錄 B = 78），config-gated，預設關（∞）** | 忠實 Xie2021；on/off 為 ablation；預設關保 baseline bit-identical |
| D7 | consolidation 時間成本 | **忽略（零時間）**，只計 packing **空間**佔用 | Xie2021 明文「ignore this time」；耦合是空間不是時間 |
| D8 | 供給補充 | `P^U` 仍可被派遣（透過 D2 的整單、或未來 epoch 變成 `P^C` 後才綁 child） | 避免 inbound 管線枯竭 |

---

## 3. Pod 狀態分類（本模型的核心資料軸）

沿用 Xie2021/Jiao2026 的白話-先於-符號慣例：

| 狀態 | 符號 | 引擎對應 | 綁 child? | 目標式成本 |
|---|---|---|---|---|
| **processing**（在站台被撿） | `P^proc` | `_usedPods` 且在站 | ✅ | 0（完全沉沒） |
| **inbounded**（已認領、在途/排隊往站台） | `P^in` | station inbound / 在途 Pb | ✅ | 0（已承諾、成本前期已付） |
| **committed** = processing ∪ inbounded | `P^C` | = M2e 的 `Pb` | ✅ 拆單 child 只能綁這裡 | 0 |
| **unused**（儲區閒置、未承諾） | `P^U` | `UnusedPods` 在儲區 | ❌ 拆單 child 禁止；僅整單可用 | `w1·d`（全程行走） |

「以 processing pod 為中心」= 因為 `P^C` 目標式成本為 0（沉沒），MILP 天然優先榨它們；`P^U` 要付全程費，只有值得時才派。**這是成本不對稱的湧現，非硬規則。**

---

## 4. 數學模型（旗標開啟時；未列者照 M2e 不變）

### 4.1 集合與參數

- `O`：本決策期 pending 訂單（含 fresh 與跨期 parent）。
- `I_o`：訂單 `o` 的**剩餘**需求 SKU（demand ledger `RemainingPositions`）。
- `S`：揀貨站。
- `P = P^C ∪ P^U`：候選 pod（見 §3）。`P_i` = 含 SKU `i` 且 `stock>0` 的 pod。
- `r[o,i]`：訂單 `o` 的 SKU `i` **剩餘**需求件數（`residuals[o][i]`）。
- `D_o = Σ_i r[o,i]`：訂單 `o` 剩餘總件數（big-M 用）。
- `stock[p,i]`：pod `p` 的 SKU `i` 可用量（`pod.CountAvailable(i)`）。
- `Cs[s]`：站 `s` 的槽位容量（訂單數）。
- `d(p,s)`、`d(r,p)`：pod→站、bot→pod 距離成本（沿用 `SplitPodStationCost`/`SplitBotPodCost`）。
- `w1=1`；`w2>0`（完成獎勵幅度，=40）目標式以 `−w2·Σzdone` 計；`w3=1000`（**必須 >0**，見 §5）。
  （註：M2e 程式碼以「負值 `w2` × 正號項」實現同一語意，二者等價；writing-plans 依 M2e 既有寫法落地，避免符號翻轉錯誤。）
- `C`：packing buffer 容量（Xie2021 附錄 B = **78**；config-gated，`≤0` = ∞ = 停用）。
- `B_occ`：決策開始時已在 packing、尚未整併的 split 母單數（引擎狀態，見 §6）。

### 4.2 變數

| 變數 | 型別 | 意義 | 相對 M2e |
|---|---|---|---|
| `q[i,o,p,s]` | int ≥0 | 訂單 `o` 的 SKU `i` 在站 `s` 從 pod `p` 抽幾件 | 沿用 |
| `xps[p,s]` | binary | pod `p` 指派站 `s`（`P^C` 前期固定；`P^U` 為決策） | 沿用 |
| `yrp[r,p]` | binary | bot `r` 指派 pod `p`（僅 `P^U` 新派；`P^C` 保留原 bot） | 沿用 |
| `ysp[o,s]` | binary | `o` 在 `s` 產生 child | 沿用 |
| `us[s]` | int 0..Cs | 站 `s` 未填滿 slot | 沿用 |
| `zdone[o]` | binary | `o` 本期整單完成 | 沿用（M2e `zdonex`） |
| **`whole[o]`** | binary | `o` 本期為「單站整單完成、非拆單」 | **新增（P1 閘門用）** |
| **`ypack[o]`** | binary | `o` 本期成為/仍是佔 packing 的 split 母單 | **新增（packing cap 用，D6 開啟時）** |

### 4.3 限制式

**（A）繼承 M2e，語意不變**
```
(C1 link-up)     Σ_o q[i,o,p,s] ≤ stock[p,i]·xps[p,s]        ∀ i, p∈P_i, s
(C2 link-down)   ysp[o,s] ≤ Σ_i Σ_p q[i,o,p,s]               ∀ o, s
(C3 link-up-y)   Σ_p q[i,o,p,s] ≤ r[o,i]·ysp[o,s]            ∀ o, i∈I_o, s
(C4 slot 守恆)   Σ_o ysp[o,s] = Cs[s] − us[s]                ∀ s          ← M1G 填槽壓力來源
(C5 跨期部分)    Σ_s Σ_p q[i,o,p,s] ≤ r[o,i]                 ∀ o, i∈I_o   ← 殘量留 backlog（time-split）
(C6 完成旗標)    Σ_s Σ_p q[i,o,p,s] ≥ r[o,i]·zdone[o]        ∀ o, i∈I_o
(C7 新 pod 消耗) xps[p,s] ≤ Σ_o Σ_i q[i,o,p,s]               ∀ p∈P^U, s   ← 被派的 unused pod 必須被用到
(C8)             pod≤1 站、pod 需 bot、pod-bot 1:1、在途 Pb 固定  （沿用 shi6/7/8/9/10/11）
```
- **跨站拆單**：同一 `o` 的 `q` 可落在多個 `s`（C3 允許），天然成立。
- **時空拆單**：C5 允許部分滿足、殘量留 backlog；配 `CrossTime=true` 跨期。二者皆 D5 要求。

**（B）新增 P1：拆單 child 只綁 committed pod（本 spec 核心）**
```
(P1a)  whole[o] ≤ zdone[o]                    ∀ o            // whole ⟹ 本期整單完成
(P1b)  whole[o] ≤ 2 − Σ_s ysp[o,s]            ∀ o            // whole ⟹ 至多一個站（≥2 站則 whole=0）
(P1c)  whole[o] = 0                           ∀ o ∈ SplitParents  // 既有跨期 parent 已是拆單，永非 whole
(P1d)  Σ_i q[i,o,p,s] ≤ D_o · whole[o]        ∀ o, p∈P^U, s  // unused pod 只服務 whole 訂單
```
**語意**：任一訂單若從**任何 unused pod（`P^U`）**抽貨（`q>0`），則 `whole[o]=1` → 它必是單站、整單、非既有 parent。逆否命題：**只要是拆單（多站 / 部分滿足 / 既有跨期 parent），其所有 `q` 只能來自 `P^C`（在場+在途）**。= 使用者的規則「order 被拆只能分配到 inbounded pod」。

> P1b 的正確性：在 C4 填槽壓力下，`Σ_s ysp[o,s]` = 訂單觸及的站數。=1 → `whole≤1`（允許）；≥2 → `whole≤0`（禁 unused）；=0 → 無服務、`zdone=0`、P1a 逼 `whole=0`。三情況皆對。

**（C）新增 packing cap（D6，config-gated；`C≤0` 時整組省略、baseline bit-identical）**
```
(PK1a) ypack[o] ≥ ysp[o,s] − whole[o]         ∀ o∉已在packing, s   // 有 child 且非 whole → 佔 packing
(PK1b) ypack[o] ≤ Σ_s ysp[o,s]                ∀ o                  // 無 child 不佔
(PK1c) ypack[o] ≤ 1 − whole[o]                ∀ o                  // whole 單直接出貨、不進 packing
(PK2)  B_occ + Σ_o ypack[o] ≤ C                                     // 同時在製 split 母單 ≤ C（=78）
```
- 忠實 Xie2021：**一個 split 母單佔一個 packing 箱格**（非 per-child），從第一個 part 到站起、到全員整併止；`C=78` = 附錄 B 的一架 shelf 箱數。
- 既有跨期 parent 已計入 `B_occ`（引擎狀態），`ypack` 只數**本期新成為 split** 的 fresh 單，避免雙重計數（精確 bookkeeping 交 writing-plans）。

### 4.4 目標式

```
min   w1·[ Σ_{p∈P^U,s} xps[p,s]·d(p,s)  +  Σ_{r∈Ra,p∈P^U} yrp[r,p]·d(r,p) ]   （只有 UNUSED pod 收費）
    − w2·Σ_o zdone[o]                                                          （per-order 完成獎勵）
    + w3·Σ_s us[s]                                                             （填槽壓力，w3=1000）
```
- **`P^C`（processing+inbounded）目標式成本 = 0**：從沉沒 pod 抽貨純利潤 → MILP 自動以現場 pod 為中心（成本不對稱的湧現，非硬規則）。
- **無 pro-rata / 無 partial 獎勵項**（D3）：刻意省略。榨乾行為由 §5 的三力合成產生。
- 距離成本結構相對 M2e **不變**（掛在 pod-trip 層級）。

---

## 5. 「榨乾 processing pod」如何從模型長出來（不靠獎勵）

三個既有/結構元素合成，逼出你要的行為：

1. **`w3` 填槽壓力（C4 + 目標式）**：留空槽付 `w3=1000`；填一個槽（`ysp[o,s]=1`）省 1000。模型**強烈想填滿槽**。
2. **成本不對稱**：填槽要有 `q>0`；用 `P^C` pod 填是免費，用 `P^U` pod 填要付 `w1·d`。
3. **P1 閘門**：拆單的 child **只能**用 `P^C` pod 填。

合起來：當一個槽空出、現場 processing pod 還有殘量、backlog 有單需要那個 SKU → 模型為了省 `w3`、且 `P^C` 免費、且拆單只能用 `P^C` → **它會把現場 pod 的殘量切成一個 child 填進空槽**（榨乾），殘餘 SKU 因 C5 留 backlog。

### 5.1 你的 3A/2B 場景走查（驗證模型會產生它）

狀態：站 `s` 有 processing pod `p`（`P^C`）剩 `stock[p,A]=3`；一個空槽（`us[s]≥1`）；backlog 單 `o` 需 `r[o,A]=3, r[o,B]=2`；下一顆 pod 未到、`P^U` 無便宜替代。

- 模型選擇「填槽」：`ysp[o,s]=1`、`q[A,o,p,s]=3`（`p∈P^C`）。`us[s]` 減 1 → 省 `w3=1000`；`p` 免費 → 成本 0。
- `o` 是拆單（`r[o,B]=2` 未服務、`zdone[o]=0`）→ P1 要求 `q` 只能來自 `P^C` → `q[A,o,p,s]` 合法（`p∈P^C`）✅；`whole[o]=0`（P1a）。
- SKU B：無 `P^C` pod 有 B → `q[B,·]=0` → **2B 依 C5 留 backlog、不綁任何 pod** ✅。
- packing（若開）：`o` 成為 split 母單 → `ypack[o]=1`，佔 1 格（PK2 計入）。

**結果 = 你描述的場景，逐字由 `w3 + 成本不對稱 + P1` 產生，零 partial 獎勵。** 對照組：若下一顆便宜的 `P^U` pod 同時有 A+B，模型可能改「整單完成」（`whole[o]=1`，付 `w1·d` 但得 `w2` 完成獎勵）——這是**正確的權衡**（便宜就整單完成、貴就榨現場+延後），由成本自然決定，非人為偏袒。

---

## 6. 解碼與 packing 記帳

- **解碼**（沿用 M2e §5 精簡管線）：讀 `q[i,o,p,s]` 落地，單站全數且 `whole` → 快路徑不建 child；否則對每站建 child（`Order.CreateSplitChild`），殘量留 backlog（demand ledger 自動保留）。
- **packing 記帳**（D6 開啟時，掛既有 consolidation 事件鏈，**不動 `OutputStation` 完成即釋槽的行為**——揀貨 slot 仍在 child 完成時釋放）：
  - 決策開始：`B_occ` = 現存未整併 split 母單數（`PackingBuffer.Occupied`，per-parent 計）。
  - 非最終 child 完成 → 該母單若尚未在 packing 佔格則 `Occupied++`（per-**母單**一格，非 per-child）。
  - 母單整併（最後一 child）→ 釋放該母單那一格。
  - 後備牆：`Occupied==C` 時不在本 tick 完成會「開新母單格」的 child（back-pressure）；完整單/最終 child 永不反壓。
- **inbound 供給**：`P^U` 被派（C7）後成為在途，下一 epoch 變 `P^C` 才被綁 child——「規劃 pod 覆蓋（早）／槽綁定（pod 臨站才做）」解耦，靠週期性重解自然達成。

---

## 7. 架構

### 7.1 Config：`SplitM2eICConfiguration : SplitM1GExactConfiguration`
- 繼承 `CrossTime`（拆單型態）、`OrderRewardWeight`（w2）。
- 新增 `int PackingBufferCapacity = 0`（`≤0`=∞停用；拆單 xconf 設 78）。
- token：`OBSPLITM2EIC`；新 `OrderBatchingMethodType.SplitM2eIC` enum + `[XmlInclude]` + `Controller.cs` case。
- 繼承鏈仍滿足 `is M1GConfiguration`（引擎零改動優勢保留）。

### 7.2 Manager：`SplitM2eICManager : M1GManager`（新檔，鏡像 M2e，**不繼承** `SplitM1GExactManager`）
- override `DecideAboutPendingOrders`。
- 鏡像 `InitializeSplitExact`，**差異**：候選 pod 依 §3 分 `P^C`/`P^U`（`P^C` = 現行 Pb 建法、`P^U` = 現行 `UnusedPods` 建法）；加 `whole`/`ypack` 變數與 P1/PK 限制式；目標式 unused-only 收費。
- 重用 starve-aware 私有方法（與 pod 狀態無關）。
- packing 記帳 → 新 `PackingBuffer` 元件（掛 `Instance`，全域，因整併跨站）；引擎側只讀 `OutputStation`/`Order`，不改其行為。

### 7.3 消融安全
- `M1GManager`/`HADGSManager`/`SplitM1GExactManager` 行為完全不變（不同檔、不同 token）。
- `PackingBufferCapacity≤0` 且非 IC manager → 引擎層 packing 完全 inert → 既有 M0/M1e/M2e/PVGS 數字 bit-identical。

---

## 8. 驗證與實驗

### 8.1 單元測試（RAWSimO.Tests，pure-function，不碰 Gurobi）
- **P1 閘門**：任一 `q[i,o,p,s]>0` 且 `p∈P^U` ⟹ `whole[o]=1`（單站+`zdone`）；拆單單（多站/部分/parent）從 `P^U` 抽貨 → 不可行。
- **whole 定義**：`Σ_s ysp=2` ⟹ `whole=0`；`SplitParents` ⟹ `whole=0`。
- **跨站+跨期**：同單 `q` 落多站可行；殘量留 backlog（`Σq<r` 可行、`zdone=0`）。
- **packing cap**：`B_occ+Σypack>C` 不可行；`whole` 單不計入 `ypack`。

### 8.2 Ablation（乾淨隔離每個 lever，直接回應 pro-rata 的「歸因不清」教訓）
| 臂 | P1（inbound-only） | packing C | 目的 |
|---|---|---|---|
| M2e（baseline） | off | ∞ | 現況 |
| **IC-struct** | **on** | ∞ | **P1 結構單獨效應**（核心主張） |
| **IC-full** | on | 78 | + Xie2021 packing 實體限制 |
| （對照）PVGS | — | 78 & ∞ | buffer on/off 對 PVGS 的影響（拉平免費-WIP） |

- **主張驗收**：IC-struct 相對 M2e——consolidation wait 中位自 222s **顯著下降**、pile-on 自 4.6 **上升**、pod 到訪自 306 **下降**、槽駐留自 132.9s **縮短**（→ TP 天花板自 650 升）。
- 標準案例 `small_o100_mu100`、5 seeds、7200s、預設權重。指標全家桶：TP / OrderPO / ItemPO / RD / OD / EOR / OrdersLate / consolidation wait 分佈 / solveSec。

### 8.3 Probe（先量後判）
- **供給充足性**（D8 風險）：量 P1 開啟後有無站台因「拆單只能綁 `P^C`」而 starve（`P^C` 為空且無 whole 單可派）。M2e 現況過度派 pod（306 趟），預期不 starve，但需實測。
- **packing binding 頻率**：78 是否真的 binding（否則 PK 空轉）；量 `B_occ` 峰值 vs 78。
- **場景重現**：決策 log 直接觀測「processing pod 殘量被切成 child 填空槽、殘 SKU 留 backlog」的次數（§5.1 行為的直接證據）。

### 8.4 論文 ablation 階梯
M0 → M1/M2 → M1e/M2e → **M2e-IC** → H；大規模 vs PVGS/HADGS。故事線：M2e→IC 的增益**歸因於結構（P1）而非獎勵**——這正是 pro-rata 三度失敗後、第一個「不靠目標式端」的正向 lever。

---

## 9. 風險與開放決策

1. **供給枯竭（D8）**：拆單 child 只能綁 `P^C`，若 inbound 管線斷、站台無 `P^C` 可用 → 拆單無法推進。緩解：D2 保留整單走 `P^U`；週期重解補充 inbound。**8.3 probe 為前置驗證**，若真 starve 需引入「`P^U` 覆蓋派遣」（放寬 C7、加覆蓋項）——列為後續，非本 spec。
2. **whole 單仍可能早綁定 `P^U`（D2）**：整單走 fresh pod 仍有駐留膨脹（等 pod 到），但**無 consolidation 尾巴**（單一 child、無手足）。使用者優先項是尾巴，此為次要，8.2 監測 whole 單佔比與其駐留。
3. **`w3` 必須 >0**：本模型的榨乾驅動力來自填槽壓力（§5）。若沿用 M2e-PR 的 `w3=0` 會回到死區。**明文固定 `w3=1000`**，writing-plans 不得誤設 0。
4. **on-the-fly extract 窗口**：現場 pod 接新 child 需 `Requests.Any()` 窗口（`BotManagerPodSelection.cs:1711-1729`）。slot 空出瞬間 pod 須未離站；巧合率現況 15–27%。本模型靠 §5 填槽壓力**主動**在同一次解裡把殘量塞給空槽（不等事件），部分繞開此窗口，但仍需 8.3 probe 確認「現場 pod 殘量被實際接走」。
5. **packing per-母單 vs per-child 語意**：本 spec 依 Xie2021 採 **per-母單一格**（一箱一單）；與 2026-07-15 packing-buffer spec 的 per-child 計法不同。以 Xie2021 為準（78 的來源），writing-plans 記帳依此。
6. **變數/限制式規模**：`whole`/`ypack` 各 `|O|` 個、P1d 為 `|O|×|P^U|×|S|`。與 M2e 同階，`P^U` 過濾（僅含需要 SKU 的儲區 pod）沿用。solveSec 監測。

---

## 10. 文獻錨點（寫論文時的對照）

| 本 spec 元素 | Xie2021 (split.pdf) 對應 |
|---|---|
| 跨站 + 跨期拆單、殘量留 backlog | split-over-time（§3.4，變數 `ybio`「SKU 移回 backlog」） |
| packing cap `C=78`、`Σypack≤C` | 附錄 B（99×99×244 shelf，S×12×5 + M×6×3 = 78 箱/單） + §4.3 限制式(18) |
| consolidation 只計空間、零時間 | §3.1「ignore this time」 |
| buffer off = ∞ | Xie2021 base 模型假設 `C` 無限大（headline 結果） |
| time-split 使半成品滯留更久 | §4.3「incomplete orders stay longer in packing」（＝ M2e consolidation 尾巴的文獻先例） |

**新意（兩篇源論文都沒有）**：pod 級 4D `q[i,o,p,s]` 精確歸屬 + **拆單 child 入站限定（P1）** + 以結構（非目標式獎勵）湧現的 processing-pod 深榨。
