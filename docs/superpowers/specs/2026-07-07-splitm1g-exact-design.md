# Spec 3：SplitM1GExact——四維精確 pod 歸屬 MILP 設計

日期：2026-07-07
狀態：已核可（設計對話中逐項確認）
前置：Spec 1 enabler（`2026-07-02-order-splitting-consolidation-enabler-design.md`）、
Spec 2 SplitM1G（`2026-07-04-order-splitting-milp-design.md`，branch 6/24，已完成、READY TO MERGE）

## 1. 定位與範圍

Spec 2 的 SplitM1G 把 M0（M1G）的「一單一站」放鬆到「SKU 級跨站/跨期」（`q[i,o,s]`），
但 pod 級的具體歸屬（這個 SKU 單位到底從哪個 pod 扣）仍然是求解完 MILP 之後，用貪婪法
（`SolveSplit` 內 819-952 行風格的 `_availableCounts`/`dopsPodsSelected`/`dopsPodsUsed`
機制）事後決定的——這個落差正是「反悔機制」存在的原因：MILP 的 `shi5'` 只做**站級聚合**
庫存檢查（`Σo q ≤ Σp stock·xps`），不保證每個被選中的 pod 都真的被貪婪法用到。

本 spec 新增 **`SplitM1GExact`**：把 `q[i,o,s]` 升級成 **`q[i,o,p,s]`**（多一個 pod 維度），
讓 MILP 直接決定「哪個訂單的哪個 SKU 從哪個具體 pod 抽取」，同時把目標函式的獎勵項從
「每 unit 給獎勵」（已知會偏好少張大單、污染 orders-handled 指標）改成「每張訂單完成給
獎勵」，跟 M0 的獎勵哲學一致。求解後不再需要貪婪 Ziops 分配與反悔機制——MILP 的解本身
就是最終答案。

**模組邊界（硬約束，沿用 Spec 1/2 定案）**：`M1GManager.cs`、`HADGSManager.cs`、
`SplitM1GManager.cs`（Spec 2 產物）一行不動。消融階梯擴充為四段：

| 代號 | 方法 | 說明 |
|---|---|---|
| M0 | 原 M1G | baseline，不拆單 |
| M1/M2 | SplitM1G（Spec 2） | unit 級拆單，pod 歸屬靠求解後貪婪法 |
| **M1e/M2e** | **SplitM1GExact（本 spec）** | unit+pod 級聯合決策，pod 歸屬由 MILP 直接決定 |
| H | SplitOrderManager（Spec 1） | heuristic 拆單對照組 |

**不包含**：候選 pod 修剪（本 spec 定案：先求真正最優解，不做 top-K 距離修剪）、
packing station 建模（沿用 Spec 1/2 定案：不建模）。

## 2. 設計決策記錄（本次 brainstorming 逐項確認）

1. **精確度 = 完整四維 `q[i,o,p,s]`**，不採「`q[i,o,s]` 不變、只把距離係數用最近 pod
   距離佰近」的近似方案——近似方案沒有真正解決 pod 歸屬問題，本質問題仍然存在。
2. **新舊模型關係 = 新建鏡像類別**，不修改 `SplitM1GManager`——Spec 2 的 599/611 回歸
   基準與 4o10b 陣地正向實證數據（items +27~48%、pile-on +30~40%）全部保留，論文消融
   鏈可以寫成 M0 → SplitM1G（粗粒度）→ SplitM1GExact（精粒度）三階段故事，比直接覆寫
   更完整。
3. **目標式獎勵 = 完全改成「訂單完成數」**，取代 Spec 2 的 `UnitRewardWeight`（per-unit
   獎勵，已知會系統性偏好少張大單而非多張小單，是 Spec 2 訂單數 vs items 數落差的主因）。
   M1e 直接沿用既有 `zfull[o]` 語意；M2e 新增 `zdone[o]`（見 §3.3）。
4. **`zdone[o]` 不做雙重獎勵（訂單完成 + 少量單位獎勵混合）**——原因：站台閒置懲罰
   `w3·Σus` 本身已經對「有槽位卻不填」重罰，足以避免模型因為「這次填不滿一張大單就乾脆
   躺平」而放棄部分進度；疊加單位獎勵只會重新引入 Spec 2 的 confound，不採用。
5. **候選 pod 範圍 = 全倉庫**（沿用上一輪 `PodAttributionOracle` 討論的決定，該診斷工具
   本身在本 spec 定案後改為非必要——四維模型本身就是那個診斷工具想驗證的「完整解」）。
   不做 top-K 距離修剪：small / 4o10b 陣地的 pod 數量本來就不大（10~100 級），先求真正
   最優解驗證模型行為與求解時間，45-bot 大陣地若真的撞到規模問題再回來考慮修剪。

## 3. 數學模型

### 3.1 集合與參數

沿用 Spec 2 的 `InitializeSplit` 邏輯（殘量快照、`Pb`/`Pa`/`Ra`/`Rb`/`R`、`Cs`、成本係數
`SplitPodStationCost`/`SplitBotPodCost`），新增：

- `P_i`：全倉庫（`Pa ∪ Pb`，即本次決策 `allPods`）中含 SKU i 且 `CountAvailable(i) > 0`
  的 pod 集合——**不限於某個 station 的候選**，任何 pod 都可以是任何 SKU 的候選來源
- `stock[p,i]`：pod p 對 SKU i 的可用量（`pod.CountAvailable(i)`）

### 3.2 變數

| 變數 | 型別 | 意義 | 相對 SplitM1G 的變化 |
|---|---|---|---|
| `xps[p,s]` | binary | pod p 指派站 s | 沿用 |
| `yrp[r,p]` | binary | bot r 指派 pod p | 沿用 |
| `us[s]` | integer 0..Cs[s] | 站 s 未填滿 slots | 沿用 |
| `dops[o,p,s]` | binary | order-pod-station linking | 沿用（語意不變，但下游不再需要靠它做貪婪比對，見 §5） |
| `ysp[o,s]` | binary | o 在 s 產生 child 的指示變數 | 沿用（驅動來源改變，見 shi4'/link-up） |
| **`q[i,o,p,s]`** | integer ≥ 0 | **order o 的 SKU i 在站 s，從 pod p 抽取幾件** | **新增 pod 維度（Spec 2 是 `q[i,o,s]`）** |
| `zfull[o]` | binary | M1e：全指派或不指派開關 | 沿用（M1 專用，`!CrossTime` 才建） |
| **`zdone[o]`** | binary | **M2e：訂單 o 是否因這次決策而完全滿足** | **新增（M2 專用，`CrossTime` 才建）** |

### 3.3 約束

```
(link-up)    q[i,o,p,s] ≤ stock[p,i] · xps[p,s]              ∀ i, o, p∈P_i, s     （逐 pod 焊死庫存，取代 shi5' 的聚合）
(link-down)  ysp[o,s] ≤ Σi Σp q[i,o,p,s]                     ∀ o, s               （禁止空 child，聚合掉 pod 維度）
(shi4')      Σo ysp[o,s] = Cs[s] − us[s]                     ∀ s                  （純 slot 制，沿用 Spec 2）
(M1e 模式)   Σs Σp q[i,o,p,s] = r[o,i] · zfull[o]            ∀ o, i∈I_o           （全指派或不指派，可跨站/跨 pod）
(M2e 模式)   Σs Σp q[i,o,p,s] ≤ r[o,i]                       ∀ o, i∈I_o           （sm2，可部分，殘量留 backlog）
(M2e 完成旗標) Σs Σp q[i,o,p,s] ≥ r[o,i] · zdone[o]          ∀ o, i∈I_o           （新增，配合 M2e 模式的 ≤ 上界，
                                                                                    兩者合起來強制 zdone=1 時所有 SKU 皆滿足）
(shi13')     xps[p,s] ≤ Σo Σi q[i,o,p,s]                     ∀ p∈Pa, s            （新 pod 至少被真正消耗，直接對 q 求和，取代 Spec 2 的 dops-based shi13）
```

**規劃階段修正（相對本文先前草稿）**：原本設想沿用 Spec 2 的 `dops`/shi12' 撐住 shi13
語意，重新推導後發現 `dops` 只跟 `ysp`/`xps` 掛鉤（shi12'：`2·dops ≤ ysp+xps`），從未
真正連到 `q` 的實際消耗量——在聚合式 `shi5'` 底下這是可接受的近似，但在精確 `q[i,o,p,s]`
底下這個近似已經沒有存在理由，且會讓「新 pod 保證被用到」這個宣稱失真。**`dops`/shi12'
在 SplitM1GExact 整組不需要**，shi13 直接改寫成對 `q` 求和（`shi13'`），既更簡單也是
真正的保證（不再是「必要條件」層級的鬆散近似）。

以下逐字保留（Spec 2 §3.3 原樣）：shi6（pod 至多一站）、shi7/shi11（繼承在途 pod/bot
固定 =1）、shi8（pod 需 bot）、shi9/shi10（pod-bot 一對一）。

**與 Spec 2 的關鍵差異**：`shi5'`（聚合庫存檢查）整條被 `link-up` 取代——每個 `q` 變數
自己就跟一個具體 pod 的具體庫存綁定，不再需要一個額外的聚合不等式；這正是消除「反悔
機制」存在理由的根本改動（聚合檢查允許的冗餘解空間，在 link-up 下不存在）。

### 3.4 目標函式

```
min  w1 · ( Σ xps[p,s]·SplitPodStationCost(p,s)  +  Σ yrp[r,p]·SplitBotPodCost(r,p) )
   + w2 · ( Σ zfull[o]   若 M1e   或   Σ zdone[o]  若 M2e )
   + w3 · Σ us[s]
```

- `w1 = 1`、`w3 = 1000`：沿用 Spec 2
- `w2`：訂單完成獎勵權重，需重新校準（Spec 2 的 `w2' = -40` 是 per-unit 尺度，不能直接
  沿用；建議 default 比照 M0 的 `w2 = -40` 語意，因為現在獎勵單位重新對齊「每張訂單」）
- **距離成本項（`xps`/`yrp`）結構完全不變**——距離成本本來就掛在 pod-trip 層級（一趟
  pod 移動的成本，不是 per-unit 成本），不需要因為 `q` 多了 pod 維度而重新設計；本
  spec「item fulfillment distance cost」的訴求，是透過 link-up 讓 `xps`/`yrp` 選中的
  pod **精確對應**到真正會被消耗的 pod，而不是新增一個獨立的距離成本項。

### 3.5 規模評估（需在 Task 1 實測，此處為量級估計）

Spec 2 在 small 陣地（|O|≈67、|I_o|≈1–3、|S|=2）量到 `q[i,o,s]` ≈ 268 個變數。四維化後
`q[i,o,p,s]` 的規模 ≈ 268 × (該 SKU 平均候選 pod 數)。small/4o10b 陣地的 pod 池通常在
10~100 級，保守估計會讓 `q` 的變數數放大一到兩個數量級（**數千到數萬**），`link-up`
限制式數量同步放大（一個 `q` 變數對應一條 `link-up`）。**這是本 spec 最大的風險項**，
必須在 Task 1（baseline 快照 + config plumbing）完成後，用小規模 seed 跑一次
`m1g_decision_log.csv`/`splitm1gx_decision_log.csv` 風格的診斷 log，實測 solve 秒數，
再決定是否要繼續往 45-bot 大陣地推進（若求解時間超過決策週期，需要在後續 spec 討論
候選 pod 修剪，但本 spec 明確排除該範圍）。

## 4. 架構

### 4.1 Config：`SplitM1GExactConfiguration : SplitM1GConfiguration`

- **繼承 `SplitM1GConfiguration`（而非直接繼承 `M1GConfiguration`）**：複用既有的
  `CrossTime`（M1e/M2e 切換）欄位語意，不用重新定義一次 M1/M2 開關。
- 欄位：繼承 `CrossTime`；新增 `double OrderRewardWeight = -40`（取代語意上不再適用的
  `UnitRewardWeight`，這個舊欄位在 `SplitM1GExactConfiguration` 裡不使用）
- token：`OBSPLITM1GX`；新 `OrderBatchingMethodType.SplitM1GExact` enum 值 +
  `[XmlInclude(typeof(SplitM1GExactConfiguration))]` + `Controller.cs` case
- 繼承鏈仍然滿足 `is M1GConfiguration`（`SplitM1GConfiguration : M1GConfiguration`），
  引擎層零改動的優勢完整保留

### 4.2 Manager：`SplitM1GExactManager : M1GManager`（新檔案，不繼承 `SplitM1GManager`）

- **不繼承 `SplitM1GManager`**：避免混進它的貪婪 Ziops/反悔管線（本模型不需要）；跟
  `SplitM1GManager` 一樣直接鏡像 `M1GManager`，各自獨立、互不耦合。
- override `DecideAboutPendingOrders`
- 新寫 `InitializeSplitExact`（鏡像 `InitializeSplit`，差異：`CreatedeVarName` 對應段落
  要多產生一個 pod 維度的 `q` 符號名稱、不再需要 Spec 2 的 `dops` 貪婪比對用途，但
  `dops`/shi12' 本身仍保留給 shi13 用）
- 新寫 `SolveSplitExact`（鏡像 `SolveSplit`，關鍵差異見 §5：求解後直接讀 `q[i,o,p,s]`
  的解落地，不跑貪婪 Ziops 迴圈）
- 重用 Spec 2 已鏡像好的 starve-aware 私有方法（`PrepareStarveAwareSplit`/
  `SplitBotPodCost`/`SplitPodStationCost`），這幾個方法跟 pod 維度無關，可直接複製
  沿用，不用重寫

### 4.3 消融安全性

- `M1GManager`/`HADGSManager`/`SplitM1GManager`（Spec 2）行為完全不變
- 回歸判準：Spec 2 的 599/611、4o10b 陣地實證數字不受影響（不同檔案、不同 config token）

## 5. 解碼管線（相對 Spec 2 大幅簡化）

Spec 2 求解後要跑「決定 child 訂單 → 貪婪 Ziops 逐 pod 扣庫存 → 反悔撤銷未用到的
新 pod」三段式管線。SplitM1GExact 因為 `q[i,o,p,s]` 已經是最終答案，管線簡化為：

1. 對每個 order o，讀出解中 `{(p,s) : Σi q[i,o,p,s] > 0}` 涉及的站集合，比照 Spec 2
   §5 步驟 2/3 判斷快路徑（單站全數且非 split parent）或建 child（`CreateSplitChild`）。
2. **不需要貪婪 Ziops 迴圈**：直接對每個 `(i,o,p,s)` 且 `q[i,o,p,s]>0` 的組合，用
   `q` 的值當作「這個 child/母單這個 SKU 從這個 pod 扣多少」寫入 `NewZiops`/
   `_Ziops`——這一步從「求解」退化成「讀值轉格式」，不再有任何猜測或不確定性。
3. **不需要反悔機制**：因為 `link-up` 保證每個 `xps=1` 的 pod 一定至少被某個
   `q[i,o,p,s]>0` 用到（否則該 pod 不會被 MILP 選中——`shi13` 已經保證這件事，且
   現在是**精確**保證，不是聚合層面的鬆散保證），不會出現「選了卻沒用到」的情況。
4. consolidation 記帳（`_pendingOrders.Remove`、`TakeAvailableOrder`、
   `SplitConsolidationLogger`）沿用 Spec 2 §5 步驟 4/6，邏輯不變。

## 6. 驗證與實驗

### 6.1 單元測試（RAWSimO.Tests）

比照 `SplitMilpDecoderTests.cs` 的風格，新增 pure-function 測試（不碰 Gurobi）：

- M1e all-or-nothing：`zfull=0` → 全零；`zfull=1` → 各 SKU 跨站跨 pod 總和 = 殘量
- M2e 部分指派 + `zdone` 正確性：`zdone=1` 時所有 SKU 必須跨站跨 pod 湊滿；`zdone=0`
  時允許任意子集滿足
- link-up 逐 pod 焊死：任何 `q[i,o,p,s] > stock[p,i]` 的組合不可行（用小規模反例驗證
  約束式本身寫對，不是靠求解器碰運氣）
- 空 child 禁止：`ysp=1 ⟺ Σi Σp q ≥ 1`

### 6.2 回歸

- Spec 2 的 `split_milp_m1.xconf`/`split_milp_m2.xconf` 跑既有 599/611、4o10b 數字：
  bit-identical（本 spec 不動這兩個檔案的程式碼路徑）

### 6.3 煙霧

- 新 xconf：`split_milp_m1e.xconf`/`split_milp_m2e.xconf`（`small` 陣地）——與
  `split_milp_m1.xconf`/`split_milp_m2.xconf` 僅 `<Name>` 與 OB 段（token 換成
  `OBSPLITM1GX`、`UnitRewardWeight` 換成 `OrderRewardWeight`）不同
- small seed0 7200s：**求解時間是本階段最重要的觀察指標**（§3.5 風險）,连带看
  handled 數、pile-on、orders late 是否維持在 Spec 2 同量級或更好

### 6.4 實驗階梯（論文）

M0 → SplitM1G（M1/M2）→ SplitM1GExact（M1e/M2e）+ H 對照，先在 small/4o10b 驗證
四段消融的故事線是否成立（M1e/M2e 的 orders-handled 應該優於 SplitM1G 的 M1/M2，
因為獎勵改回訂單導向且 pod 歸屬更精確，理論上能同時改善 orders 數與 pile-on 而
不再有 Spec 2 那個 per-unit confound）；45-bot 高壓陣地待 §3.5 規模驗證通過後再排。

## 7. 風險與備忘

- **變數/限制式規模爆炸（§3.5 已詳述）**：這是本 spec 最大的不確定性，Task 1 完成
  即應立刻做一次小規模 solve 時間量測,再決定後續 Task 的排期,不要等到全部寫完才
  發現求解時間不可接受。
- **`w2` 重新校準**：不能直接沿用 Spec 2 的 `-40`（那是 per-unit 尺度校準出來的值），
  需要重新對照 M0 的 `-40` 做語意校準（M0 也是 per-order 獎勵，理論上可以直接借用
  同一個值當 default，但要跑一次 small 煙霧確認行為合理，不要假設數字直接可搬）。
- **`link-up` 限制式數量**：從 Spec 2 的「每個 SKU-站」一條聚合式，變成「每個
  SKU-pod-站」一條，限制式總數同步放大；若 Gurobi presolve 無法有效消解，可能需要
  重新檢視 `PiSKU`/`P_i` 的建構方式（例如只對「真的有庫存 > 0」的 pod 建變數，
  Spec 2 `CreatedeVarName` 已經有這個過濾邏輯，本 spec 沿用同一原則，不額外放寬）。
- **`PodAttributionDiagnostic`/`PodAttributionOracle`（上一輪討論的診斷工具）是否還
  需要做**：本 spec 一旦驗證可行，這個診斷工具的存在理由（量測 Spec 2 貪婪法的
  optimality gap）就被 SplitM1GExact 本身取代——診斷工具改為「可選的事後對照」，
  不是本 spec 的前置依賴，兩者可以獨立排期。
- **`dops`/shi12' 已確認移除**：見 §3.3 規劃階段修正，`dops` 在 Spec 2 只是貪婪比對用
  的輔助變數，在精確 `q[i,o,p,s]` 底下沒有存在理由，shi13 直接對 `q` 求和（`shi13'`）
  更簡單也更正確；實作時不要把 Spec 2 的 `dops`/`IsdeVarNamedops` 相關程式碼複製進來。
