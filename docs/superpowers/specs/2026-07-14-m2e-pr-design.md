# M2e-PR 設計 spec：pro-rata 部分揀取獎勵 + 真完成語意（zfin）

日期：2026-07-14
狀態：定稿（brainstorm 三段逐段審查通過：2026-07-13/14 對話）
分支：`6/24`
前置：M2e（`SplitM1GExactManager`，CrossTime）、Spec 1 demand ledger、BackfillProbe joint-completion 擴充（commit 00c06c8）

---

## 1. 背景與動機（診斷鏈摘要）

exact-vs-heuristic 差距的排除鏈已完備（目標式×3、shortlist、Pa、綁定時機 LB、派發打包），
最終定位：**差距＝pod 駐留期間的 re-harvest**。同一 acceptance 情境（2 揀貨站、10 bots、7200s）：

| 臂 | orders（5-seed 均） | item pile-on |
|---|---|---|
| M1G | 640.0（除 s3 崩潰 seed） | 3.65 |
| M2e 現行（acc_m2ea） | 642.4 | 4.60 |
| M2e+eps（acc_m2ea_eps） | 623.4 | 4.73 |
| PVGS（acc_pvgse） | 641.2 | **8.28** |

視角重建（itemprogression.csv）證實：首波揀貨兩者相同，**全部 pile-on 差距來自駐留期 re-harvest**；
re-harvest 速率幾乎相等（0.073-0.074 vs 0.084-0.085 picks/s），差在**駐留時長**（35s vs 74s）——
M2e 餵薄、pod 早枯。BackfillProbe 量化可收復價值：release 時殘量 ≈10 件/顆，joint(+inbound)
pivotal 訂單 56-79 張/run（≈趟數缺口的 41-57%）。

現行 M2e 獎勵結構的雙重病理（`eM2done`，SplitM1GExactManager.cs:527-536）：

1. **量的死區（deadband）**：`zdonex` 是 all-or-nothing——10 件的單揀 9 件獎勵為 0。
   `w3=0` 之下「部分填 slot」與「slot 空著」目標式同值，是退化解，MILP 沒有任何
   誘因去榨取 processing pod 的殘量。
2. **缺貨切片套利**：`eM2done` 以 `.Where(PiSKU.ContainsKey)` 跳過缺貨 SKU——只把
   「看得見的那片」揀滿就領整份 40；同一張單跨 epoch 分片可各領一次，獎勵不守恆、
   結構性偏好切片。

eps 臂（無差別每件 +ε）是重要反例：TP 反而掉到 623。結論：**部分揀取獎勵必須錨定在
訂單需求進度上**（智能榨取），不能無差別給件數分（盲目榨取）。

## 2. 設計決策記錄（使用者逐項定案）

| # | 決策 | 定案 |
|---|---|---|
| D1 | 部分獎勵形式 | pro-rata：`R·(本輪指派量/D_o)`，D_o=原始總需求；上限 `R·frac<R` 結構自動成立 |
| D2 | 完成優先形式 | **加權**完成加成 `B·zfin`（非嚴格字典序——Cs=1 節奏下字典序會讓部分榨取永遠排不上） |
| D3 | 完成語意 | `zfin` 真完成：**不過濾缺貨 SKU**；右手邊用剩餘需求 `r_rem`（見 §4 兩分母之辨） |
| D4 | pro-rata 分母 | `D_o = order.GetDemandCount()`（原始總量，含缺貨 SKU），跨 epoch 守恆 `Σ切片獎勵 = R` |
| D5 | processing pod 優先 | **不加閘門限制式**——由 `w1` 只對新車（Pa）收距離費的不對稱定價代數湧現（沉沒 pod 免費）；「防過分拆單」交由 B≫R·frac 階層＋真實距離成本，以護欄指標監測 |
| D6 | 入場過濾 | 不動（line 215-217）。實驗假設庫存充足，缺貨非研究對象；eZfin 對缺貨的強制歸零僅為防禦性保險 |
| D7 | 舊旋鈕 | `eps`（UnitDrawReward）、`w5`（ProcessingPodDrawReward）棄用不疊加；`w4`/`ecap`/CF 維持關閉；`w3` 維持 0（R>0 已破死區） |
| D8 | B/R 量級 | `B=40` 固定（沿用 w2 歷史量級，歸因乾淨）；`R∈{10,20,40}` 唯一掃描軸 |
| D9 | 架構 | 旗標加在 `SplitM1GExactConfiguration`（照 CF/w4/w5 前例；本檔為自有檔案，非 M1GManager 禁區）；`B=0` 走原 code path，bit-identical |
| D10 | Trigger / 交付 | 零改動。slot 釋放→`SignalOrderFinished`→Cs 門檻→重解（現行事件機制）；processing pod 接新 q 走 on-the-fly extract（BotManagerPodSelection.cs:1711-1729，需 `Requests.Any()` 窗口） |

## 3. 數學模型（旗標開啟時；未列者照現行 M2e 不變）

### 3.1 集合/參數（僅列新增/釐清）

- `D_o`：訂單 o 的**原始**總需求件數 = `o.GetDemandCount()`（`_overallQuantity`；含缺貨 SKU；
  parent 跨 epoch 不變——pendingOrders 只含 parent，child 由 decoder 生成後即分配、不回流）。
- `r_rem[o,i]`：本 epoch 剩餘需求 = `residuals[o][i]`（= `RemainingPositions`，已扣除前輪 claim）。
- 變數族不變：`xps[p,s]`、`yrp[r,p]`、`yspx[o,s]`、`q[i,o,p,s]`、`us[s]`、z（語意改為 `zfin_o`）。

### 3.2 目標式（min 形式）

```
min   w1·Σ_{p∈Pa} xps[p,s]·d(p,s)  +  w1·Σ_{p∈Pa} yrp[r,p]·d(r,p)      （不變）
    − B·Σ_o zfin_o                                                        （取代 w2·Σzdonex）
    − Σ_o Σ_{i,p,s} (R/D_o)·q[i,o,p,s]                                    （新增 pro-rata）
    + w3·Σ_s us_s                                                         （不變，維持 0）
```

- pro-rata 無輔助變數：每個 q 變數的目標式係數直接乘 `−R/D_o`，純線性、模型零增長。
- 階層關係代數自動成立：任何部分 `R·frac < R`；`zfin=1 ⟹ frac=1`（本輪吃滿剩餘）⟹ 完成
  該輪拿 `B + R·(r_rem/D_o)`；跨 epoch 總計恰為 `B + R` 一次。
- 「優先榨 processing pod」的湧現：R 對所有 pod 一視同仁，但 Pa 要付 `w1·d`、Pb（processing/
  inbound）免費——同樣的 item 從沉沒 pod 拿是純利潤，MILP 自動優先。

### 3.3 限制式改動（僅 eM2done → eZfin；其餘 elink1/2/3、eshi4/6/7/8/9/10/11/13'、eM2 全部不變）

對每張 pending 訂單 o：

```
（eM2，不變）    Σ_{p,s} q[i,o,p,s] ≤ r_rem[o,i]                 ∀ i ∈ 可見 SKU
（eZfin，新）    Σ_{p,s} q[i,o,p,s] ≥ r_rem[o,i]·zfin_o          ∀ i ∈ RemainingPositions(o)（不過濾）
```

實作等價形：可見 SKU 逐條加 eZfin；一旦存在缺貨 SKU（`!PiSKU.ContainsKey(i)` 且 `r_rem>0`），
改加一條 `zfin_o ≤ 0` 即可（LHS 恆 0，不為缺貨 SKU 展開假變數）。

### 3.4 兩分母之辨（本設計最易錯處，明文固定）

| 位置 | 基準 | 反面後果 |
|---|---|---|
| eZfin 右手邊 | `r_rem`（剩餘） | 若用原始需求：被拆過的單前輪 q 已 commit、本輪補不回，永遠 zfin=0，B 形同虛設 |
| pro-rata 分母 | `D_o`（原始） | 若用剩餘：每輪切片各以當輪剩餘為 100% 計分，跨 epoch 加總 > R，套利復活 |

守恆驗證（3A+2B，B 第一輪缺貨）：epoch1 供 3A → zfin=0（B 那條強制），得 `R·3/5`；
epoch2 B 補貨、供 2B → zfin=1，得 `B + R·2/5`；總計 `B + R` 恰一次。現行 zdonex 同劇本發 40+40。
良性副作用：**B 落在「補完最後一片」的那輪**——MILP 有明確誘因清殘單，直接打
「slot 當 WIP 停車場」病理（槽位診斷 2026-07-12）。

## 4. 架構與實作細節

### 4.1 Config（`RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`，SplitM1GExactConfiguration）

```csharp
/// <summary>(M2e-PR) B: true-completion bonus per order whose REMAINING demand is fully
/// assigned this solve, under unfiltered zfin semantics (out-of-stock SKUs force zfin=0).
/// 0 = feature off, legacy zdonex/w2 path, bit-identical.</summary>
public double TrueCompletionReward = 0;
/// <summary>(M2e-PR) R: pro-rata reward. Each assigned unit of order o earns R/D_o where
/// D_o is the order's ORIGINAL overall demand (GetDemandCount()). Only active when
/// TrueCompletionReward > 0.</summary>
public double ProRataReward = 0;
```

- 無新型別 → 不需要 XmlInclude；既有 xconf 不含新元素（吃預設值）→ 逐 byte 不變。
- 欄位加在類別尾端（XmlSerializer 宣告順序只影響「有寫出該元素」的新 xconf）。

### 4.2 SolveSplitExact 改動點（`SplitM1GExactManager.cs`）

以 `bool prMode = _splitConfig != null && _splitConfig.TrueCompletionReward > 0 && _splitConfig.CrossTime;`
統一分閘（**PR 僅定義於 CrossTime=true**：M1 模式的 eM1 是等式 all-or-nothing，部分揀取不存在，
pro-rata 無從作用；CrossTime=false 時旗標靜默不生效，行為同原版）：

1. **目標式 z 係數**（line 377/381）：`prMode ? -B : w2` 乘 `Σ z`（w2 本即負值，形式一致）。
2. **目標式 q 係數**（新增，緊接 eps 區塊 line 397-399 的位置模式）：`prMode && R != 0` 時
   `objective += Σ_q variablesQ[q.name] * (−R / q.order.GetDemandCount())`（分組不必要，逐變數係數即可；
   guard 空序列同 eps 前例）。
3. **eM2done → eZfin**（line 524-537）：`prMode` 時該迴圈改為：(a) 可見 SKU 照樣加
   `lhs ≥ r_rem·z`；(b) 迴圈外先檢查 `residuals[order].Any(p => r>0 && !PiSKU.ContainsKey(p.Key))`
   成立則加 `z ≤ 0` 並跳過（b) 之後的可見 SKU eZfin 可省略但保留亦無害——**保留**，
   讓 (a)/(b) 邏輯正交、免去分支）。`!prMode` 走原 code path 一行不動。
4. **eps/w5 不疊加**：文件約定 PR xconf 中 `UnitDrawReward=0`、`ProcessingPodDrawReward=0`；
   程式不硬擋（保留正交性），由 xconf 紀律保證。
5. **決策 log 加 2 欄**（WriteExactDecisionLog，line 170-199）：
   - `partialOrders`：本輪 q>0 但 zfin=0 的訂單數（死區破除的直接觀測）；
   - `partialOnlyNewTrips`：本輪新認領（Pa）pod 中，其全部 q 都流向 zfin=0 訂單的顆數
     （「為湊 frac 派車」護欄指標）。
   欄位附加於行尾，header 同步更新（該檔每 run 重寫，無相容性問題）。

### 4.3 不動的東西（明文）

- `M1GManager.cs`/`HADGSManager.cs`（憲法禁區）；`SplitM1GExactManager` 是自有檔案，照 CF 前例就地加旗標。
- Decoder/commit 路徑（line 570-643）：decoder 只讀 q 不讀 z，獎勵語意改變不影響執行層。
- Trigger、入場過濾（D6/D10）、on-the-fly extract 引擎。
- Spec 1 demand ledger、`SplitConsolidationLogger`。

## 5. 驗收設計

情境：acceptance 標準（`1-2-2-10-0.84-small_o100_mu100`，7200s）。對照臂皆已有數據（§1 表）。

- **Gate 0（bit-identical）**：`B=0` 重跑 acc_m2ea seed0，footprint.csv 與既有輸出在**全部
  確定性欄位**上相同（僅容許牆鐘欄位差異：RealTimeUsed、MemoryUsedMax、TimingDecisions* 類；
  逐 byte 比對必然被牆鐘欄位打破，比對腳本須明列排除欄位索引）。
- **Gate 1（功能煙霧）**：`B=40, R=20` seed0 跑完全程；無當機；`placed − handled ≤ 15`；
  決策 log `partialOrders` 總和 > 0（pro-rata 確實觸發）。
- **Gate 2（機制證據，地位高於 TP）**，三探針交叉：
  1. BackfillProbe `podRemainingUnits`：release 殘量自 ≈10 顯著下降；
  2. item pile-on：4.60 → 6+ 為強信號（PVGS 天花板 8.28）；
  3. 槽位駐留/佔用：殘單駐留縮短、佔用自 96-102% 鬆動。
  另報 `DistanceTraveled/ItemsHandled`（item distance，footprint 現成欄位相除）。
- **Gate 3（掃描+吞吐）**：R∈{10,20,40}×seeds{0,1} 粗掃 → 選 R* → 5 seeds。
  及格＝顯著 > 642.4（現行 M2e）；達標＝≥ PVGS 水位（641-648）。
- **護欄**（任一觸發即檢討 R）：`partialOnlyNewTrips`/總新趟 > 5%，或 TP < 620。
- **預期管理**：本情境 TP 天花板 ≈650，PR 的 TP 增益可能僅個位數；論文收益主軸為機制層
  （pile-on↑ ＝ 每 item 趟數↓ ＝ 機械能 E1-E5↓，接能耗感知主軸）。TP 第二戰場為 4o10b
  （bot 稀缺，拆單增益 +28~48% 的情境）。

## 6. 風險

| 風險 | 等級 | 緩解 |
|---|---|---|
| partial 氾濫（θ=2 教訓：518） | 低 | 結構上 `R·frac<R≤B+R`＋距離成本壓制；`partialOnlyNewTrips` 護欄直接觀測 |
| partial 填 slot 加重 WIP 停車 | 中 | B 落於最後一片＝清殘單誘因；Gate 2-3 駐留指標直接驗證方向 |
| on-the-fly 窗口錯過（pod 已 release） | 中 | 已知引擎事實（`Requests.Any()` 前置條件）；trigger 在訂單完成時 pod 通常仍有他單揀貨，窗口開啟；Gate 2-1 殘量若不降即此因，屆時再議（非本 spec 範圍） |
| R/D_o 係數尺度 vs 距離尺度失配 | 低 | R 掃描軸即為此設；決策 log objective 欄可直接檢視兩項量級 |
| 求解時間膨脹 | 低 | 零新變數、限制式數同階；決策 log solveSec 監測 |

## 7. 已排除方案（不要重提）

- **嚴格字典序兩階段解**（完成數先鎖再榨取）：Cs=1 節奏下部分榨取永遠排不上，被 D2 排除。
- **閘門限制式**（`q ≤ D·zfin` for Pa / 按 pod 類別開放獎勵）：與 w1 不對稱定價重複，
  且違反「決策由 MILP 湧現、不外掛規則」原則，被 D5 排除（保留為護欄觸發後的備案）。
- **eps/w5 疊加**：獎勵語意互污，且 eps 臂已實證有害（623<642），被 D7 排除。
- **D_o 用剩餘需求當分母 / zfin 用原始需求當右手邊**：§3.4 兩個反面後果。
- **LB（晚綁定）**：引擎 pre-claim 事實使延後僅 ~3s，已死（2026-07-12 spec + progress ledger）。
