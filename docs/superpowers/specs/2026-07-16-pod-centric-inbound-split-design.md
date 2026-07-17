# Spec：M2e-IC — 貨架中心、入站限定的線上拆單 MILP（修正 M2e）

> 日期：2026-07-16（v1）→ 2026-07-17（v3 定稿）　分支：6/24
> 狀態：設計定稿（brainstorm 多輪收斂，待 writing-plans 產出/更新逐字實作計畫）
> 事實來源：程式碼 / git log / memory。本 spec 定案 brainstorm 決策，供 writing-plans 產出逐字實作計畫。
> 命名為**暫定**（`M2e-IC` = Inbound-Committed Split）；使用者可改名。保留 `M2e` 字首是為了論文 ablation 血緣。
> **v3 變更摘要**：v1/v2 用單一 `w3=1000`「填槽壓力」同時扮演「產出驅動」與「榨乾驅動」兩個角色，在拆單開放後被證明會誘發「絞碎完整單去佔多槽」的反向誘因（§5.0）。v3 改用**五層權重階層**（完成優先、多站懲罰、填槽降級為橋接、榨取 tie-break、距離 tie-break）+ **lead-gated pipeline floor**（榨取與派工解耦、同時發生）。P1/PK 結構不變。

前置與血緣：
- 拆單合併語意骨架 = **Xie et al. (2021) split-over-time**（`paper/split.pdf` §3.4 + 附錄 B），本 spec 忠實沿用。
- 線上聯合 + Pa/Pb pod 狀態 = **Jiao et al. (2026) M1G**（EE-RAWSim-O 本體）。
- 修正對象 = **M2e**（`SplitM1GExactManager.cs`，`docs/superpowers/specs/2026-07-07-splitm1g-exact-design.md`）。
- 已排除的死路（不要重提）：目標式端獎勵重塑（eps / CF / pro-rata M2e-PR 三度陣亡）、字典序 sunk-first（五 seed FAIL）、天真晚綁定旗標（引擎 pre-claim 使延後 ~3s，已死）。詳見 memory `project_m2e_pr_negative`、`project_slot_ceiling_diagnosis`、`m2e-sunk-first-negative-result.md`。
- **標準測試案例槽數（2026-07-17 使用者確認）**：本 spec 所有 `Cs[s]` 假設皆以 `OStationCapacity=6` 為準（`Material/Instances/CoreBenchmark/small/small.xlayo:21` 已核對）；見 memory `feedback_test_case_standard.md`。

---

## 1. 定位：修正 M2e 的哪一個病，以及怎麼修

**M2e 的病灶（診斷已見骨，見 memory `project_slot_ceiling_diagnosis` + 本專案多輪對話）：**

1. **早綁定 + 綁到未到站的 pod**：`SplitM1GExactManager.cs:328-337` 把 `UnusedPods`（還在儲區的閒置貨架）納入候選 `Pa`，`shi13'` 又強迫被派的 Pa pod 一定要被消耗 → **訂單被綁到還在儲區的 pod，佔著槽空轉等它被搬來**。
2. **拆單手足時間不同步**：同一張母單的 child 可被綁在到站時間天差地別的 pod（在場 5s vs 儲區 200s+）→ 母單卡等最慢的 child → consolidation 尾巴拖到 3781s（vs PVGS 48s）。
3. **薄榨**：pile-on 4.6 vs PVGS 8.28；pod 到訪 306 vs 168（同揀貨量）——派太多薄 pod、不肯深榨現場 pod。

**修正的一句話**：把「貨還沒到就綁單」改成「**貨到了才切、切了就撿、撿完就走**」——拆單的 child 只能綁在已承諾（在場 or 在途）的 pod，絕不綁儲區閒置 pod；剩餘需求留 backlog 等未來。**同時**：完成訂單仍是主旋律，拆單是供給疲弱時的彈性手段，不是被鼓勵的常態（v3 新增，見 §1.1）。

### 1.1 v3 追加的核心原則（使用者 2026-07-16~17 逐輪定案）

- **order 作為「限制」在拆單下消失，但作為「計分/整併/槽位量化單位」不可拿掉**（§4.6 專節說明，回應「order 內容物要不要納入聯合決策」的提問）。
- **完成訂單數是主旋律，拆單是手段**：一個 slot 能整單完成，就不該讓它拆成兩三個 slot——除非 processing pod 供給耗盡、下一顆貨架尚未抵達，或 bot 耗盡供給疲弱，此時用 partial 完成部分訂單只是「不浪費撿貨產能」的彈性手段。
- **節奏**：新貨架抵達 → 完整訂單揀貨 → 貨架耗盡 → 部分訂單（橋接）→ 貨架離開／新貨架抵達 → 循環。此節奏由 v3 權重階層（§4.4）+ pipeline floor（§4.7）湧現，不是外掛的事件規則。
- **榨取（q 層）與派工（x 層）必須能在同一次解裡同時發生**：純貪婪榨取會把派工系統性推遲到「當前 pod 再也擠不出完成」才觸發，此時派新車已經來不及（供給缺口 = 一整段行走時間）。v3 用 lead-gated pipeline floor 把派工觸發權從「榨乾與否」交給「時鐘」，解除這個衝突（§4.7）。

**模組邊界（專案硬性約束）**：`M1GManager.cs` / `HADGSManager.cs` / `SplitM1GExactManager.cs` **一行不動**。M2e-IC = 新鏡像檔（`SplitM2eICManager : M1GManager`）。消融階梯擴充為：M0（M1G）→ M1/M2（SplitM1G）→ M1e/M2e（SplitM1GExact）→ **M2e-IC（本 spec）** → H（heuristic）；大規模對照 PVGS。

---

## 2. 設計決策（brainstorm 逐項定案，v3 更新標記 ★）

| # | 決策 | 定案 | 理由 |
|---|---|---|---|
| D1 | 拆單 child 的 pod 候選 | **只允許在場（processing）+ 在途（inbounded）pod，即 `P^C`（= M2e 的 Pb）；禁止儲區 unused pod（`P^U` = Pa）** | 消除時間不同步的手足（3781s 尾巴的根）。使用者明確指示。 |
| D2 | 未拆單（整單單站完成）的 pod 候選 | 仍可用 `P^U`（保留 M1G 的 fresh-dispatch，兼作補充 inbound 供給） | 使用者限制只針對「被拆」的單；且需維持供給管線 |
| D3★ | 榨乾 processing pod 的驅動力 | **不加 partial/pro-rata 獎勵**；靠 P1 + **v3 五層權重階層**（§4.4）+ 成本不對稱（`P^C` 免費、`P^U` 收全程費）湧現 | 目標式端獎勵三度陣亡；此路繞開它。v1 的「單靠 w3=1000」已被 v3 推翻（§5.0），改用階層 |
| D4 | 完成獎勵 | 沿用 M2e 的 **per-order all-or-nothing `zdone`**（`w2`） | per-unit 有「少張大單」confound；per-order 乾淨 |
| D5 | 拆單型態 | **跨站 + 時空（cross-station + cross-time）皆允許**（沿用 M2e `CrossTime`；殘量留 backlog） | 使用者明確指示；= Xie2021 split-over-time |
| D6 | 下游 consolidation | **packing buffer 容量 `C`（Xie2021 附錄 B = 78），config-gated，預設關（∞）** | 忠實 Xie2021；on/off 為 ablation；預設關保 baseline bit-identical |
| D7 | consolidation 時間成本 | **忽略（零時間）**，只計 packing **空間**佔用 | Xie2021 明文「ignore this time」；耦合是空間不是時間 |
| D8 | 供給補充 | `P^U` 仍可被派遣（透過 D2 的整單、或未來 epoch 變成 `P^C` 後才綁 child） | 避免 inbound 管線枯竭 |
| D9★ | 多站拆單懲罰 | **新增 `wp`**：訂單每多佔一個站-part 罰一次，量級介於 w2 與 w3' 之間（§4.4） | 「一個 slot 能完成就不該拆」的字面實現；軟性偏好非硬禁令，真正需要跨站湊貨的單仍可負擔 |
| D10★ | 填槽壓力角色 | 從「主驅動」降級為「橋接誘因」：`w3'≈5 << w2=40`，只在完成不可得時才點火 partial | v1 的 `w3=1000` 在拆單世界裡誘發「絞碎完整單去佔多槽」的反向誘因（§5.0），必須降級 |
| D11★ | 派工時機 | 新增 **lead-gated pipeline floor**：站台 processing pod 剩餘工作 ≤ `Lead`（預設 70s，沿用 AE 實測最佳值）時，罰款逼模型即使還在榨取也要騰槽給下一顆 pod 的 seed whole order | 解決「榨取 vs 派工」衝突（§4.7）；`Lead=70s`/`target=2` 為 M2eAdaptiveExactMath 實測最佳（PO 3.86≈PVGS 3.87） |

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

## 4. 數學模型

### 4.1 集合與參數

- `O`：本決策期 pending 訂單（含 fresh 與跨期 parent）。
- `I_o`：訂單 `o` 的**剩餘**需求 SKU（demand ledger `RemainingPositions`）。
- `S`：揀貨站；`Cs[s]`：站 `s` 的槽位容量——**標準案例 `Cs[s]=6`**（見前置章節）。
- `P = P^C ∪ P^U`：候選 pod（見 §3）。`P_i` = 含 SKU `i` 且 `stock>0` 的 pod。
- `r[o,i]`：訂單 `o` 的 SKU `i` **剩餘**需求件數（`residuals[o][i]`）。
- `D_o = Σ_i r[o,i]`：訂單 `o` 剩餘總件數（big-M 用）。
- `stock[p,i]`：pod `p` 的 SKU `i` 可用量（`pod.CountAvailable(i)`）。
- `d(p,s)`、`d(r,p)`：pod→站、bot→pod 距離成本（沿用 `SplitPodStationCost`/`SplitBotPodCost`；starve-aware 開啟時含飢餓後悔罰項，見 §4.4 附註）。
- `C`：packing buffer 容量（Xie2021 附錄 B = **78**；config-gated，`≤0` = ∞ = 停用）。
- `B_occ`：決策開始時已在 packing、尚未整併的 split 母單數（引擎狀態，見 §6）。
- `releaseLeft(s)`：站 `s` 目前 processing pod 的剩餘已指派揀貨工作時間（引擎既有 `GetInfoCurrentPodReleaseLeft()`）。
- `Lead`：pipeline floor 的前置秒數（預設 70，見 §4.7）；`T`：每站目標管線深度（預設 1，即「現場 1 + 在途 1」）。

### 4.2 變數

| 變數 | 型別 | 意義 | 相對 M2e |
|---|---|---|---|
| `q[i,o,p,s]` | int ≥0 | 訂單 `o` 的 SKU `i` 在站 `s` 從 pod `p` 抽幾件 | 沿用 |
| `xps[p,s]` | binary | pod `p` 指派站 `s`（`P^C` 前期固定；`P^U` 為決策） | 沿用 |
| `yrp[r,p]` | binary | bot `r` 指派 pod `p`（僅 `P^U` 新派；`P^C` 保留原 bot） | 沿用 |
| `ysp[o,s]` | binary | `o` 在 `s` 產生 child/part | 沿用 |
| `us[s]` | int 0..Cs | 站 `s` 未填滿 slot | 沿用 |
| `zdone[o]` | binary | `o` 本期整單完成 | 沿用（M2e `zdonex`） |
| `whole[o]` | binary | `o` 本期為「單站整單完成、非拆單」 | P1 閘門用（v1 引入，不變） |
| `ypack[o]` | binary | `o` 本期成為/仍是佔 packing 的 split 母單 | packing cap 用（v1 引入，不變） |
| **`ep[o]`** | int ≥0 | `o` 本期超出 1 個 station-part 的數量（多站懲罰的線性化） | **v3 新增（D9）** |
| **`shortfall[s]`** | int ≥0 | 站 `s` 相對管線目標 `T` 的缺口 | **v3 新增（D11，pipeline floor）** |

### 4.3 限制式

**（A）繼承 M2e，語意不變**
```
(C1 link-up)     Σ_o q[i,o,p,s] ≤ stock[p,i]·xps[p,s]        ∀ i, p∈P_i, s
(C2 link-down)   ysp[o,s] ≤ Σ_i Σ_p q[i,o,p,s]               ∀ o, s
(C3 link-up-y)   Σ_p q[i,o,p,s] ≤ r[o,i]·ysp[o,s]            ∀ o, i∈I_o, s
(C4 slot 守恆)   Σ_o ysp[o,s] = Cs[s] − us[s]                ∀ s          ← 每站槽位守恆（Cs=6）
(C5 跨期部分)    Σ_s Σ_p q[i,o,p,s] ≤ r[o,i]                 ∀ o, i∈I_o   ← 殘量留 backlog（time-split）
(C6 完成旗標)    Σ_s Σ_p q[i,o,p,s] ≥ r[o,i]·zdone[o]        ∀ o, i∈I_o
(C7 新 pod 消耗) xps[p,s] ≤ Σ_o Σ_i q[i,o,p,s]               ∀ p∈P^U, s   ← 被派的 unused pod 必須被用到（引擎硬需求：trip 至少一個真實請求）
(C8)             pod≤1 站、pod 需 bot、pod-bot 1:1、在途 Pb 固定  （沿用 shi6/7/8/9/10/11）
```
- **跨站拆單**：同一 `o` 的 `q` 可落在多個 `s`（C3 允許），天然成立。
- **時空拆單**：C5 允許部分滿足、殘量留 backlog；配 `CrossTime=true` 跨期。二者皆 D5 要求。

**（B）P1：拆單 child 只綁 committed pod（本 spec 核心，不變）**
```
(P1a)   whole[o] ≤ zdone[o]                              ∀ o        // whole ⟹ 本期整單完成
(P1b)   Σ_s ysp[o,s] + (|S|−1)·whole[o] ≤ |S|            ∀ o        // whole=1 ⟹ 至多一站；whole=0 不設限
(P1c)   whole[o] = 0                                     ∀ o ∈ SplitParents  // 既有 parent 永非 whole
(P1oos) whole[o] = 0        ∀ o 含任一 r[o,i]>0 且 i 不在 PiSKU（缺貨/不可見 SKU）
(P1d)   Σ_{i,s} Σ_{p∈P^U} q[i,o,p,s] ≤ D_o · whole[o]    ∀ o        // unused pod 只服務 whole 訂單
```
**語意**：任一訂單若從**任何 unused pod（`P^U`）**抽貨（`q>0`），則 `whole[o]=1` → 它必是單站、整單、非既有 parent、且無缺貨殘量。逆否命題：**只要是拆單（多站 / 部分滿足 / 既有跨期 parent），其所有 `q` 只能來自 `P^C`（在場+在途）**。= 使用者的規則「order 被拆只能分配到 inbounded pod」。

> **P1b 為何是 big-M 形式**：草稿版 `whole ≤ 2 − Σysp` 在 `Σysp ≥ 3` 時給出 `whole ≤ 負數`，與 binary 下界 0 矛盾 → 整個模型不可行（禁止任何訂單跨 ≥3 站）。改為 `Σysp + (|S|−1)·whole ≤ |S|`：`whole=1` ⟹ `Σysp ≤ 1`；`whole=0` ⟹ `Σysp ≤ |S|`（無效約束），永不不可行。
>
> **P1oos 為何必要**：legacy `zdonex` 語意會**跳過**缺貨 SKU（只檢查可見 SKU）。若無 P1oos，一張含缺貨 SKU 的單可以 `zdone=1`＋`whole=1`＋從 `P^U` 抽貨，但 decoder 對照完整殘量會判 `fullyAssigned=false` → 走 **split path** → child 綁到 unused pod——正面違反本模型的核心規則。P1oos 把這個洞焊死。

**（C）packing cap（D6，config-gated；`C≤0` 時整組省略、baseline bit-identical，不變）**
```
(PK1a) ypack[o] + whole[o] ≥ ysp[o,s]         ∀ 新鮮 o（¬IsSplitParent）, s   // 有 part 且非 whole → 開一箱
(PK1b) ypack[o] ≤ Σ_s ysp[o,s]                ∀ 新鮮 o                        // 無 part 不開箱
(PK1c) ypack[o] + whole[o] ≤ 1                ∀ 新鮮 o                        // whole 單直接出貨、不進 packing
(PK2)  Σ_{新鮮 o} ypack[o] ≤ max(0, C − B_occ)                                 // 本期新開箱數 ≤ 剩餘預算
```
- 忠實 Xie2021：**一個 split 母單佔一個 packing 箱格**（非 per-child）；`C=78` = 附錄 B 的一架 shelf 箱數。
- **箱位預留時點 = decode 時**：母單在**首次被拆**（decode split path）當下即預留箱位，整併時釋放——比 Xie 的「第一個 part 實體到站」更早、更保守（絕不超收）。**by construction 永不溢出，不需要引擎側 back-pressure 牆**。
- 既有跨期 parent 首拆時已登記 → 已在 `B_occ` 內；`ypack` 只對 `¬IsSplitParent` 的新鮮單建變數，無雙重計數。

**（D）D9：多站拆單懲罰線性化（v3 新增）**
```
(D9-1)  ep[o] ≥ Σ_s ysp[o,s] − 1               ∀ o        // 超出第 1 個 station-part 的數量
(D9-2)  ep[o] ≥ 0                              ∀ o
```
`ep[o]` 在目標式被 `wp` 計價（§4.4）。單站或未服務時 `Σysp≤1` → `ep=0`（第二項自動生效，第一項可為負但下界卡住）；`Σysp=k>1` → `ep≥k−1`，模型不會無謂讓 `ep` 超過下界（因為 `wp>0`），故等式在最優解成立。

**（E）D11：lead-gated pipeline floor（v3 新增，§4.7 詳述）**
```
(icLG-gate)  gate[s] = 1  若 releaseLeft(s) ≤ Lead 或站 s 無 processing pod；否則 0     ← 決策時已知常數，非變數
(icLG-1)     shortfall[s] ≥ gate[s] · ( T − inbound_committed(s) − Σ_{p∈P^U} xps[p,s] )  ∀ s
(icLG-2)     shortfall[s] ≥ 0                                                            ∀ s
(icLG-cap)   inbound_committed(s) + Σ_{p∈P^U} xps[p,s] ≤ T                               ∀ s   ← 防過度供給
```
`inbound_committed(s)` = 已在途、尚未到站的 `P^C` pod 數（引擎既有量）。`shortfall[s]` 在目標式被 `w_pipe` 計價。

### 4.4 目標式（v3：五層權重階層，取代 v1 單一 w3）

```
min   w3'·Σ_s us[s]                                                    L1 產出層（w3'=5）
    − w2·Σ_o zdone[o]                                                  L2 完成層（w2=40，主旋律）
    + wp·Σ_o ep[o]                                                     L3 多站拆單懲罰（wp=8，新增）
    + w4·Σ_{p∈P^U,s} xps[p,s]                                          L4 新趟次費（w4=10）
    + w1·[ Σ_{p∈P^U,s} xps[p,s]·d(p,s) + Σ_{r∈Ra,p∈P^U} yrp[r,p]·d(r,p) ]   L5a 距離 tie-break（w1=1）
    − ε·Σ q[i,o,p,s]                                                   L5b 榨取深度 tie-break（ε=0.5）
    + w_pipe·Σ_s shortfall[s]                                          L6 pipeline floor（w_pipe=20，新增）
```

**權重量級關係（校準必守）**：`w3' < w_pipe < wp < w4 < w2`，且 `w3'+ε` 遠小於 `w2`（partial 永遠比完成便宜太多，不會誤導模型放棄完成）。`wp` 的雙邊夾擠條件：
- **上界**：`wp < w2`——否則跨站湊貨才能完成的必要拆單（`ep=1` 但仍能拿到 `w2`）會被誤殺成「乾脆不完成」。
- **下界**：`wp > 2·w3'`——無謂把一張單從 1 站拆成 2 站（`Σysp` 從 1→2，`ep` 從 0→1），多佔的那一格槽帶來的 `w3'` 減免（至多一次，因為原本 1 站就能填滿的槽守恆量不變，多站只是把同一批件數攤更多格）必須賠不過 `wp` 的懲罰，否則模型仍有利可圖去多佔槽。

建議起點：`w3'=5, wp=8, w4=10, ε=0.5, w_pipe=20, w2=40, w1=1`（`wp=8 > 2·w3'=10` 不成立——**此起點違反下界，僅作實作占位，Task 校準時必須先跑 §9.3 的 probe 重新求解 wp**，不可直接沿用；建議校準搜尋範圍 `wp∈[11,35]`）。

**逐層白話（每層 = 一個關於未來的假說）：**
- **L1（產出層，降級）**：空槽仍是罪，但量級降到「橋接觸發器」而非「主驅動」——避免誘發絞碎完整單去佔多槽（§5.0）。
- **L2（完成層，主旋律）**：完成的單永遠不再需要未來趟次；同樣趟次下 OA 挑完成最多的組合。
- **L3（多站懲罰，新增）**：一個 slot 能完成就不該拆成兩三個——每多佔一個站-part 罰一次，讓「能整就整」成為模型的內生偏好而非硬規則（真正需要跨站湊貨的單，完成的 40 仍買得起 1~2 次懲罰）。
- **L4（趟次費）**：每開一趟新 visit，未來 pile-on 分母 +1；只對 `P^U` 計價，`P^C` 的趟次已沉沒不重複收費。
- **L5a/L5b（tie-break）**：距離與榨取深度只在上層打平時才裁決——L5b 是「從沉沒供給免費多撿一件」的 tie-break，劑量被 P1 結構性夾住（見 §5.2）。
- **L6（pipeline floor，新增）**：見 §4.7，把「派工」的觸發權從榨取狀態交還給時鐘，解決榨取/派工衝突。

**starve-aware 附註**：`d(p,s)` 可選開啟 `StarveAwareCost`（既有機制，`SettingConfig.StarveAwareCostEnabled`），此時 `d(p,s) = 行走時間 + DelayPenalty(到站時間, EST_s)`——到站晚於站台最早飢餓時點的 pod 被加收「飢餓後悔罰項」，對應使用者的「最小後悔值」派遣直覺（§4.5）。預設關閉，作為 ablation 臂。

### 4.5 「最小後悔值」派遣的模型定位

使用者的例子（單站，`o1=3A2B`、`o2=2A1C`，候選遠 pod `5A2B1C` vs 近雙 pod `4A1C+1A2B`）在固定「兩張單都完成」下，模型選擇由 `w4`（每趟固定費）與 `w1·d`（距離）的交換率決定：選近雙 pod ⟺ `w4 < w1·[d(far) − d1 − d2]`。**`w4/w1` 就是「一趟趟次值幾公尺行走延遲」的校準目標**。若進一步要求「行走期間站台不能餓」，開啟 `StarveAwareCost`，讓 `d(p,s)` 內含 `DelayPenalty`——到站晚於 `EST_s` 的候選被直接加罰，這是「機會成本／最小後悔值」的字面模型化，非另立新項。

### 4.6 為何 order 維度不能拿掉（回應「內容物要不要納入聯合決策」）

拆單全開後，`o` 不再限制任何 unit 的去向（任何 pod 都能部分供應任何單），但 `o` 仍是三件事的唯一標籤，拿掉即斷：

1. **計分**：`zdone[o]`/`whole[o]` 以訂單為單位；改成純 SKU 池目標式退化為線性件數函數，無法表達「AND 閉合」（3A2B 值 40 的條件是 A、B 同時滿足）——這正是 per-unit 獎勵「少張大單」confound 的根源，此路已在 Spec 2→3 改版與 eps 臂驗證中證偽。
2. **槽位量化**：站台 `Cs=6` 格槽裝的是 order-child，聚合流量無法回答「這批流量該佔幾格」——Spec 2 的鏡像實驗（聚合 pod 維度、事後貪婪分配）已實測崩潰兩次（跨單庫存超提、站台容量超收 6×），對稱地聚合需求維度會重蹈覆轍。
3. **整併債**：packing 箱以母單計（`ypack[o]`），SKU 池記不了這筆帳。

**結論**：訂單作為「硬限制」被拆單拿掉，作為「計分/整併/量化單位」保留——這是模型維度取捨的正式立場，供論文方法論章節引用。

### 4.7 榨取與派工的解耦：lead-gated pipeline floor 詳述

**衝突機制**：v3 槽位競價順序下，用當前 `P^C` pod 完成 whole（淨值 ≈ `w2+w3'=45`）恆優於派新 `P^U` pod 完成 whole（淨值 ≈ `w2−w4−w1d+w3'≈15`）——模型會持續吃免費的 45、把派工推遲到「當前 pod 真的擠不出任何完成」才觸發，此時派車已錯過安全窗（行走時間 30~100s 的飢餓缺口）。

**修正**：pipeline floor（icLG，§4.3E）把觸發權交給時鐘，而非榨取狀態——

- `gate[s]` 由 `releaseLeft(s)`（引擎既有量）決定，是**決策時的常數**，不受本次求解的 q/x 影響 → 不會被榨取行為操縱。
- gate 開啟後，`shortfall` 罰款（`w_pipe=20`）介於 L2 完成（40）與 L4 趟次費（10）之間——模型願意騰一格槽給下一顆 pod 的 seed whole order，**即使當前 pod 還有完成可吃**（其他槽照常榨取），達成「同一次解裡榨取與派工同時發生」。
- `T=1, Lead=70s` 沿用 `M2eAdaptiveExactMath` 已實測的最佳節奏（PO 3.86 ≈ PVGS 3.87；`lead=0` 崩到 3.44、`target=3` 過供崩到 3.39）——本 spec 把該事件規則改寫成 MILP 內的線性限制式，同一行為現在是聯合最優化的一部分。

**節奏湧現**：新 pod 帶 seed whole 抵達 → `w2` 吃其完成（同時舊 pod 尾巴被 partial 榨取）→ `releaseLeft` 走到 `Lead` 內 → gate 開、floor 逼出下一顆派工（帶自己的 seed）→ 行走窗內舊 pod 殘量被榨光 → 舊 pod 離開同時新 pod 進站 → 循環。此即使用者定案的節奏（§1.1）。

---

## 5. 「榨乾 processing pod」如何從模型長出來（v3：權重階層，不靠獎勵）

### 5.0 v1 的教訓：為何 `w3=1000` 在拆單世界裡是毒藥（v3 廢除的推理，保留存檔）

v1 沿用 M1G 的 `w3=1000 >> w2=40`（填槽壓倒完成）。M1G 不能拆單，填槽的唯一方式是塞完整單，填槽即真產出，無副作用。**但拆單開放後，填槽有了作弊管道**：一張在單站就能完成的單，模型會故意拆到兩站各佔一格——兩格的 `w3` 減免（2000）遠大於任何代價，即使沒有多完成任何訂單、白佔一個 packing 箱、還欠一筆整併債。這正是舊 M2e「474 次薄承諾 × 3.1 件」病理的目標式根源：**填槽壓倒完成，模型就把訂單絞碎去餵槽**。v3 的 D9（`wp`）+ D10（`w3'` 降級）聯手修正此洞。

### 5.1 v3 的榨取機制（三力＋一表）

1. **成本不對稱**（不變）：填槽要有 `q>0`；用 `P^C` pod 填是免費，用 `P^U` pod 填要付 `w1·d`。
2. **P1 閘門**（不變）：拆單的 child **只能**用 `P^C` pod 填。
3. **權重階層**（v3 新）：完成不可得時，`w3'+ε≈5.5`（partial 填槽+榨取）仍優於 0（空槽）→ partial 作為「完成不可得時的次選」被觸發，但**永遠打不贏**任何可行的完成（`w2=40` 或 `w2−wp=32` 的跨站完成）。

**合起來**：當一個槽空出、backlog 有可整單完成的單（Pb 或便宜的 Pa）→ 模型優先完成它；當現場 pod 殘量湊不齊任何完整單、又還有 backlog 需求 → 模型才把殘量切成 partial child 填槽（榨乾），殘餘 SKU 因 C5 留 backlog——**這正是使用者定案的「完成主旋律、拆單為彈性」節奏**。

### 5.2 3A/2B 場景走查（供給疲弱時的驗證，機制不變、量級更新）

狀態：站 `s` 有 processing pod `p`（`P^C`）剩 `stock[p,A]=3`；一個空槽（`us[s]≥1`）；backlog 單 `o` 需 `r[o,A]=3, r[o,B]=2`；**backlog 中沒有其他單可被 `p` 或任何便宜 `P^U` pod 整單完成**（供給疲弱的前提）；下一顆 pod 未到。

- 若存在別的可整單完成的單，模型優先完成它（L2 淨值 40 > partial 的 5.5）——這是 v3 相對 v1 的行為差異：**v1 會無條件榨；v3 只在完成選項用盡時才榨**。
- 在「供給疲弱」前提成立時：模型選擇「填槽」：`ysp[o,s]=1`、`q[A,o,p,s]=3`（`p∈P^C`）。`us[s]` 減 1 → 省 `w3'=5`；`p` 免費 → 成本 0；`ep[o]=0`（單站，不觸發 D9）。
- `o` 是拆單（`r[o,B]=2` 未服務、`zdone[o]=0`）→ P1 要求 `q` 只能來自 `P^C` → 合法；`whole[o]=0`。
- SKU B：無 `P^C` pod 有 B → `q[B,·]=0` → 2B 依 C5 留 backlog、不綁任何 pod。
- packing（若開）：`o` 成為 split 母單 → `ypack[o]=1`，佔 1 格（PK2 計入）。

**結果 = 使用者描述的場景，但現在明確發生在「供給疲弱、無完成可選」的條件下**——這是 v3 相對 v1 最重要的行為修正：v1 無條件優先榨（因 w3=1000 主導一切），v3 讓「完成」先於「榨取」發生，榨取只在完成用盡後接手。

---

## 6. 解碼與 packing 記帳

- **解碼**（沿用 M2e §5 精簡管線）：讀 `q[i,o,p,s]` 落地，單站全數且 `whole` → 快路徑不建 child；否則對每站建 child（`Order.CreateSplitChild`），殘量留 backlog（demand ledger 自動保留）。
- **packing 記帳**（per-母單一箱；**不動 `OutputStation` 完成即釋槽的行為**——揀貨 slot 仍在 child 完成時釋放）：
  - **登記**：decode split path 建 child 當下 `PackingBuffer.RegisterParent(parent)`（idempotent，再拆不重複計）。
  - **釋放**：母單整併（`NotifyChildCompleted` 回 true 的分支）→ `PackingBuffer.ReleaseParent(parent)`——引擎側唯一掛點（null-safe，非 IC manager 不受影響）。
  - 決策開始：`B_occ = PackingBuffer.AliveParentCount`（已登記未整併的母單數）。
  - **無 back-pressure 牆**：預留發生在決策時刻、受 PK2 管制，by construction 不溢出。
- **inbound 供給**：`P^U` 被派（C7）後成為在途，下一 epoch 變 `P^C` 才被綁 child——「規劃 pod 覆蓋（早）／槽綁定（pod 臨站才做）」解耦，靠週期性重解自然達成；pipeline floor（§4.7）確保這個補充不會延遲到飢餓發生後才觸發。

---

## 7. 承諾（Commitment）與綁定（Binding）的關係

兩個動作、兩個時點，舊 M2e 的病就是把它們焊在一起：

| | 承諾（commitment） | 綁定（binding） |
|---|---|---|
| 變數 | `xps=1, yrp=1` | `ysp=1, q>0` |
| 物理意義 | bot 出發、pod 上路——**資源付費時點** | 訂單佔槽、unit 認 pod——**槽位佔用時點** |
| 可逆性 | 下一期進 `P^C`，`C8`（沿用 shi7/11）焊死不可反悔 | 每期重解前 backlog 殘量自由 |

**三條規則：**
1. **綁定只准指向已承諾的供給**——拆單的 `q` 只能流向 `P^C`（P1d 的逆否命題）；這是「晚綁定」的 MILP 化：貨上路了才准綁單。
2. **新承諾只被 whole 綁定觸發**——`P^U` pod 要被派，必須有一張 whole 訂單消耗它（C7+P1d）；whole 是唯一允許「綁定先於供給到位」的例外（引擎需要真實 extract request 才能執行 trip，且 whole 無手足、無整併尾巴）。
3. **舊 M2e 的病灶用這語言講**：它允許「拆單綁定與新承諾同時發生」——child 綁到還在儲區的 pod，槽位空轉等承諾兌現（駐留 132.9s、尾巴 3781s）。P1 把這條路斷掉。

**初始化性質**：`t=0` 時 `P^C=∅` → P1 逼所有首期訂單走 whole → **第一期行為 = M1G**（沒有供給就沒有拆單），管線建立後拆單才漸次開啟。拆單嚴格是「利用沉沒供給」的行為。

---

## 8. 架構

### 8.1 Config：`SplitM2eICConfiguration : SplitM1GExactConfiguration`
- 繼承 `CrossTime`（拆單型態）、`OrderRewardWeight`（w2）。
- 新增欄位：`int PackingBufferCapacity = 0`（`≤0`=∞停用）；`double MultiPartPenalty`(wp，預設 8)；`double IdleSlotWeight` 沿用既有欄位但語意改為 `w3'`（預設值需在 xconf 顯式覆寫為 5，見 §9 風險 3）；`double PodTripFixedCost`（w4，沿用既有欄位，預設值需覆寫為 10）；`double UnitDrawReward`（ε，沿用既有欄位，覆寫為 0.5）；`double PipelineFloorWeight`（w_pipe，新增，預設 20）；`double PipelineFloorLeadSec`（`Lead`，新增，預設 70）；`int PipelineFloorTarget`（`T`，新增，預設 1）。
- token：`OBSPLITM2EIC`；新 `OrderBatchingMethodType.SplitM2eIC` enum + `[XmlInclude]` + `Controller.cs` case。
- 繼承鏈仍滿足 `is M1GConfiguration`（引擎零改動優勢保留）。

### 8.2 Manager：`SplitM2eICManager : M1GManager`（新檔，鏡像 M2e，**不繼承** `SplitM1GExactManager`）
- override `DecideAboutPendingOrders`。
- 鏡像 `InitializeSplitExact`，**差異**：候選 pod 依 §3 分 `P^C`/`P^U`；加 `whole`/`ypack`/`ep`/`shortfall` 變數與 P1/PK/D9/pipeline floor 限制式；目標式改五層階層。
- 重用 starve-aware 私有方法（§4.4 附註，作為可選 ablation 臂）。
- packing 記帳 → 新 `PackingBuffer` 元件（掛 `Instance`，全域，因整併跨站）；引擎側只讀 `OutputStation`/`Order`，不改其行為。

### 8.3 消融安全
- `M1GManager`/`HADGSManager`/`SplitM1GExactManager` 行為完全不變（不同檔、不同 token）。
- `PackingBufferCapacity≤0` 且非 IC manager → 引擎層 packing 完全 inert → 既有 M0/M1e/M2e/PVGS 數字 bit-identical。

---

## 9. 驗證與實驗

### 9.1 單元測試（RAWSimO.Tests，pure-function，不碰 Gurobi）
- **P1 閘門**：任一 `q[i,o,p,s]>0` 且 `p∈P^U` ⟹ `whole[o]=1`（單站+`zdone`）；拆單單（多站/部分/parent）從 `P^U` 抽貨 → 不可行。
- **whole 定義**：`Σ_s ysp=2` ⟹ `whole=0`；`SplitParents` ⟹ `whole=0`。
- **跨站+跨期**：同單 `q` 落多站可行；殘量留 backlog（`Σq<r` 可行、`zdone=0`）。
- **packing cap**：`B_occ+Σypack>C` 不可行；`whole` 單不計入 `ypack`。
- **D9 線性化**：`ep` 的下界計算（`Σysp` 從 0 到 4 對應 `ep` 的正確值）。
- **pipeline floor gate**：`releaseLeft ≤ Lead` 或無 processing pod 時 `gate=1` 的邊界值測試。

### 9.2 Ablation（乾淨隔離每個 lever）
| 臂 | P1 | 權重階層(v3) | packing C | pipeline floor | 目的 |
|---|---|---|---|---|---|
| M2e（baseline, w3=0） | off | off（v1式） | ∞ | off | 現況（acc_m2ea 參考） |
| M2e（w3=1000） | off | off | ∞ | off | **v1 的 w3 歸因對照**（不可省略，見 §10 風險 3） |
| IC-struct | on | off（w3=1000, 沿用 v1） | ∞ | off | P1 結構單獨效應 |
| **IC-v3** | on | **on** | ∞ | off | **v3 權重階層效應**（核心主張） |
| **IC-v3-pipe** | on | on | ∞ | **on** | + pipeline floor（榨取/派工解耦） |
| IC-full | on | on | 78 | on | + Xie2021 packing 實體限制（完整模型） |
| （對照）PVGS | — | — | 78 & ∞ | — | buffer on/off 對 PVGS 的影響 |

- **主張驗收**：IC-v3 相對 IC-struct——multi-part 拆單比例下降（D9 生效的直接證據）、whole 單佔比上升（完成主旋律的直接證據）；IC-v3-pipe 相對 IC-v3——站台飢餓事件數/starvation gap 下降（pipeline floor 生效的直接證據）。IC-full 相對 M2e（w3=1000）——consolidation wait 中位下降、pile-on 上升、pod 到訪下降、槽駐留縮短。
- 標準案例 `small_o100_mu100`（`Cs=6`）、5 seeds、7200s。指標全家桶：TP / OrderPO / ItemPO / RD / OD / EOR / OrdersLate / consolidation wait 分佈 / solveSec / **whole 單佔比 / multi-part 拆單比例 / starvation gap 分佈**（v3 新增探針）。

### 9.3 Probe（先量後判）
- **供給充足性**：量 P1 開啟後有無站台因「拆單只能綁 `P^C`」而 starve。
- **packing binding 頻率**：78 是否真的 binding；量 `B_occ` 峰值 vs 78。
- **場景重現**：決策 log 直接觀測「processing pod 殘量被切成 child 填空槽、殘 SKU 留 backlog」的次數（§5.2 行為的直接證據），並交叉比對此時是否確實無其他可完成的整單（供給疲弱前提）。
- **節奏驗證（v3 新增）**：decision log 逐決策記錄 whole/partial 比例、`gate[s]` 觸發次數與觸發後是否成功派出新 pod、`shortfall` 是否真的 binding——直接驗證 §1.1 的節奏是否如預期湧現。
- **權重敏感度（v3 新增）**：`wp`、`w3'`、`w_pipe`、`Lead`、`T` 為論文的天然掃描軸，small 案例先做粗掃（2~3 個取值）以確認方向正確，5-seed 精細掃描留待使用者拍板。

### 9.4 論文 ablation 階梯
M0 → M1/M2 → M1e/M2e → M2e（w3=1000, 歸因對照） → IC-struct → **IC-v3** → **IC-v3-pipe/full** → H；大規模 vs PVGS/HADGS。故事線：M2e→IC-struct 的增益歸因於結構（P1）；IC-struct→IC-v3 的增益歸因於權重階層修正「填槽誘發絞碎」的反向誘因；IC-v3→IC-v3-pipe 的增益歸因於榨取/派工解耦——三段增量各自可消融、各自可歸因，回應 pro-rata 三度失敗的「歸因不清」教訓。

---

## 10. 風險與開放決策

1. **供給枯竭（D8）**：拆單 child 只能綁 `P^C`，若 inbound 管線斷、站台無 `P^C` 可用 → 拆單無法推進。緩解：D2 保留整單走 `P^U`；pipeline floor（D11）主動預防飢餓（比 v1 單靠事件重解更早介入）。9.3 probe 為前置驗證。
2. **whole 單仍可能早綁定 `P^U`（D2）**：整單走 fresh pod 仍有駐留膨脹（等 pod 到），但無 consolidation 尾巴（單一 child、無手足）。pipeline floor 縮短這段等待（用 partial 橋接），但不消除。9.2 監測 whole 單佔比與其駐留。
3. **權重階層必須整組校準，不能沿用 v1 的 `w3=1000`**（v3 核心變更）：`IdleSlotWeight` 欄位語意從「主驅動」變成「橋接誘因」，**xconf 必須顯式覆寫為 5**（沿用預設會回到 v1 的絞碎誘因）。歸因注意：現行 5-seed M2e 基準（acc_m2ea, TP=642.4）是 `w3=0` 臂；IC-v3 用全新的五層權重，**三方對照**（M2e w3=0、M2e w3=1000、IC-v3）才能把「P1 效應」「w3 效應」「v3 階層效應」三者分離，缺一則歸因不清。
4. **on-the-fly extract 窗口**：現場 pod 接新 child 需 `Requests.Any()` 窗口（`BotManagerPodSelection.cs:1711-1729`）。slot 空出瞬間 pod 須未離站；巧合率現況 15–27%。v3 的權重階層在同一次解裡主動把殘量塞給空槽（不等事件），部分繞開此窗口，仍需 9.3 probe 確認。
5. **packing per-母單 vs per-child 語意**：本 spec 依 Xie2021 採 per-母單一格；與 2026-07-15 packing-buffer spec 的 per-child 計法不同。以 Xie2021 為準。
6. **變數/限制式規模**：`whole`/`ypack`/`ep`/`shortfall` 各 `O(|O|)` 或 `O(|S|)`，P1d 為 `|O|×|P^U|×|S|`。與 M2e 同階；pipeline floor 只加 `|S|` 個變數/限制式，量級可忽略。solveSec 監測。
7. **派遣半徑的有限性（v3 新增風險）**：v1 的 `w3=1000` 等於無限派遣半徑（再遠的 pod 只要能填槽就派）；v3 的有效派遣半徑 ≈ `(w2+w3'−w4)/w1 ≈ 35` 公尺（校準值下）。small 陣地距離量級無虞，**上到 45-bot 大陣地距離可能超過此半徑 → 站台可能因「無便宜可派 pod」而餓死**。緩解：大陣地校準時需重新量測派遣半徑 vs 站間距離，必要時對 `w1` 做正規化或啟用 starve-aware 後悔項（§4.4 附註）讓飢餓站自動抬高出價；9.3 probe 需含大陣地（4o10b 或以上）的 starvation 檢查，small-only 驗證不足以放行大陣地部署。
8. **D9/D11 與 P1 的交互未經驗證**：三者皆為新元件，理論上正交（D9 罰多站、D11 逼派工、P1 限來源），但堆疊效應需 9.2 的分層 ablation（IC-struct→IC-v3→IC-v3-pipe）逐段驗證，不可只驗最終 IC-full 就下結論。

---

## 11. 文獻錨點（寫論文時的對照）

| 本 spec 元素 | Xie2021 (split.pdf) 對應 |
|---|---|
| 跨站 + 跨期拆單、殘量留 backlog | split-over-time（§3.4，變數 `ybio`「SKU 移回 backlog」） |
| packing cap `C=78`、`Σypack≤C` | 附錄 B（99×99×244 shelf，S×12×5 + M×6×3 = 78 箱/單） + §4.3 限制式(18) |
| consolidation 只計空間、零時間 | §3.1「ignore this time」 |
| buffer off = ∞ | Xie2021 base 模型假設 `C` 無限大（headline 結果） |
| time-split 使半成品滯留更久 | §4.3「incomplete orders stay longer in packing」（＝ M2e consolidation 尾巴的文獻先例） |

**新意（兩篇源論文都沒有）**：
1. pod 級 4D `q[i,o,p,s]` 精確歸屬（承 Spec 3）。
2. 拆單 child 入站限定（P1）——結構性防止手足時間不同步。
3. **五層權重階層，把「完成優先、拆單為彈性」的管理直覺變成可證明的目標式結構**（v3 新意，兩篇源論文均無此層次的節奏設計）。
4. **lead-gated pipeline floor，把榨取與派工的時序衝突解耦成聯合最優化的一部分**（v3 新意，對應 [Jiao2026] 未觸及的「動態供給節奏」維度）。
