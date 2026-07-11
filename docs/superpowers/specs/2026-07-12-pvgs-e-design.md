# PVGS-E 設計：ExactAlignedScoring — M2e 的忠實 epoch 貪婪近似

日期：2026-07-12。狀態：設計已與使用者逐節確認。

## 0. 動機與目標

**範式調性要求（使用者定案）**：以 online.pdf 的宣稱為準——exact ≥ heuristic（M1G ≥ HADGS）。本論文的對應組合必須維持同一調性：**M2e 在 TP/PO/RD/OD/EOR 五項上全面 ≥ PVGS**。

**現狀為何違反調性**：現行 PVGS 與 M2e 差了兩個變數——(1) 解的品質（貪婪 vs 證明最優）、(2) epoch 內的評分函數與 M2e 目標式不同構（PVGS 分數含 PartialUnitWeight、ParentClosingBonus 等 M2e 目標沒有的項；缺 w4 項；coverage-only 粗篩與精算目標失準；θ 門檻 M2e 沒有）。變數 (2) 是獨立的政策差異，exact 管不到它，所以「exact ≥ heuristic」無結構保證——D1（ShortlistK 15→999 全指標免費改善）與 60+ runs 排除實驗皆為佐證。

**修法**：拿掉變數 (2)。PVGS-E = 與 M2e **同觸發、同快照、同目標函數**的 epoch 貪婪——唯一殘餘差異是解的品質，統治由構造保證（貪婪解 ≤ 同一問題的最優解）。此即 HADGS 之於 M1G 的構造關係。

**不採用的替代方案**（記錄供口試）：(a) 調參壓弱現行 PVGS（K=15 等）——脆弱，審稿人換參數即破功，且 D1 已證 K=15 是無謂瓶頸；(b) 雙軌（PVGS-E 證調性＋逐步版扛大規模）——「heuristic 反超 exact」的事實仍在，調性未真正解決。使用者已拍板**單軌 PVGS-E**：若對 HADGS 戰績縮水則誠實報告（拆單增益主張改由同引擎 NoSplit 對照扛）。

## 1. 架構

- `PVGSConfiguration` 新增欄位：`public bool ExactAlignedScoring = false;`（default false ＝現行為 **bit-identical**；前例 `DisableSplitting`）。
- 所有行為變化在 `PVGSManager.cs` 內部依旗標分支；**快照層 `InitializePvgs` 不動**（本已鏡像 M2e）。
- 現行逐步版 PVGS 的代碼、config token、既有實驗結果全部保留（對照組／討論素材）。
- 分支點共四處：
  1. **粗篩**：E 模式不用 RCI coverage-only 粗篩，對全部候選 (pod, station) 算真分數；
  2. **評分函數**：換成 M2e 目標式的邊際版（見 §2）；
  3. **部分指派引擎**：PartialSweep、PartialUnitWeight、ParentClosingBonus、MinPartialUnits(θ) 全部短路；
  4. **w4**：認領分數扣 `PodTripFixedCost`（讀既有 config 欄位）。

## 2. Epoch 演算法（E 模式的 `DecideAboutPendingOrders`）

```
快照 InitializePvgs（不變）
Phase A 完成掃描（不變）：
    已在站/在途（Pb）pod 的庫存零成本可「可見殘量全數指派」的訂單，直接收下
    —— 對應 M2e：Pb 沉沒成本免費 + zdonex 完成獎勵
Phase B 派遣迴圈（E 評分）：
    loop:
        對所有 (p ∈ Pa 候選, s ∈ 有空槽站)：
            score(p,s) = CompletionWeight × N(p,s)
                       − DistanceWeight × (d_bot→p + d_p→s)
                       − PodTripFixedCost
            其中 N(p,s) = 認領 p 至 s 後，在 s「可見殘量可全數指派」的新增訂單數
                          （用 epoch 工作副本的即時庫存/槽位/殘量帳計算）
        取 argmax；score > 0 才認領（配空閒 bot、扣槽、CommitParts 記帳）
        認領後重跑 Phase A 完成掃描（新庫存可能解鎖既有站的完成）
        直到 score ≤ 0 或無空槽/無空閒 bot
（E 模式無 Phase C PartialSweep）
提交管線（不變）：JustRegisterItem 全部先行 → AllocateOrder → 母單/child 記帳
```

語意界定（2026-07-12 與使用者對齊後的正式表述）：
- **跨期拆單＝部分指派，是同一個機制**（本期指派 q < 殘量、child 揀完釋槽、殘餘留 backlog 後續任意站完成），設計自由度只在**完成標準**——「什麼樣的部分才值得開 child／領獎勵」。
- **完成標準光譜**（模型家族的統一敘事軸）：per-unit 任何一件都算（Spec 2 M2，大單 confound）→ 佔槽即有價值（原始 M2e w3=1000，趟次浪費病灶）→ θ≥6 件（舊 PVGS，需補丁旋鈕、θ=2 崩盤）→ **可見殘量全數指派（zdonex，現行 M2e）** → 全部殘量一次滿足（M1e 准入，無時間彈性）。三次失敗皆因標準太鬆，zdonex 是收斂點。
- **PVGS-E 的定義**：把完成標準對齊 M2e（可見殘量全數指派才開 child），取代舊 PVGS 的 θ 標準——跨站拆單與跨期拆單機制完整保留，PartialSweep/PartialUnitWeight/ParentClosingBonus/θ 作為「較鬆完成標準」的實作全部短路。門檻對齊後，兩者才是同一個問題的 exact vs greedy，統治由構造保證。
- **ε 次級目標層（2026-07-12 使用者定案，字典序 item-pile-on）**：使用者的目標語意＝「在完成最多訂單的前提下，最大化每趟 pod 到站的抽貨件數（item pile-on）」。實作為**雙邊共享**的次級項：目標式加 `ε·Σq`（每抽一件微獎勵，ε≈−0.1，|ε|≪|w2|＝字典序，永不犧牲完成單換件數）。抽取來源自然限於已承諾 pod（Pb＋本期新認領——倉庫 pod 需開新趟，距離成本≫ε）。**M2e**：新欄位 `UnitDrawReward`（default 0＝bit-identical；實作＝w5 代碼路徑去掉 processing-pod 過濾）。**PVGS-E**：評分加 ε×抽貨件數＋派遣結束後「剩餘槽位榨取 pass」（僅從已承諾 pod、按同一 ε 定價；40 元完成標準不動，ε 是次級層）。安全性：joint solve 中完成單（40）永遠出價贏過 ε 級 partial，partial 只填完成單用不完的剩餘槽——θ=2 病態是貪婪逐步動態才有，MILP 端結構免疫；D3（w4=0 micro-w5：TP −0.5%、效率全面改善）為初步實證。

## 3. 對齊清單（逐項對應 M2e）

| M2e 元素 | PVGS-E 對應 | 狀態 |
|---|---|---|
| w1=1 距離、w2=−40 完成獎勵 | DistanceWeight=1、CompletionWeight=40（既有 default 同值） | 已同 |
| Pb 沉沒成本免費（目標式只計 Pa） | Phase A 完成掃描零成本 | 已同 |
| w4 行程稅（xps 係數折入） | 認領分數扣 PodTripFixedCost（同 config 欄位） | 本設計新增 |
| 部分指派零獎勵 | E 模式不做部分指派 | 本設計 |
| 槽位守恆 eshi4 / pod 一站 eshi6 / bot-pod 1:1 eshi9-10 | 派遣迴圈既有守恆邏輯 | 已同 |
| 訂單准入（CrossTime＝任一 SKU 可見）＋ Od 急單縮池 | InitializePvgs 鏡像 | **待逐字核對**（plan 內列任務；有偏差修至一致） |
| w5 ProcessingPodDrawReward | E 模式不支援（驗收點 w5=0；被 ε 推廣版取代） | 明文排除 |
| **ε UnitDrawReward（新，雙邊）** | M2e：`ε·Σq` 目標項；PVGS-E：評分 +ε×件數＋剩餘槽榨取 pass | 本設計新增（default 0） |
| w3 IdleSlotWeight | 兩側皆 0（驗收點；PVGS 本無空槽罰項） | 對齊 |

## 4. Config 與檔案

- 新 xconf：`Material/Instances/CoreBenchmark/small/pvgs_e.xconf` ＝ 拷貝 `pvgs_m2e.xconf` 僅改 `<Name>pvgs_e</Name>`、加 `<ExactAlignedScoring>true</ExactAlignedScoring>`（憲法 byte 級對照規則：其餘 byte 不得異動；MinPartialUnits=6 保留在檔內但 E 模式下無作用，維持最小 diff）。
- 不動任何既有 xconf。C# 7.3 / net48：欄位序列化順序照類別宣告，`ExactAlignedScoring` 加在 `DisableSplitting` 之後。
- **`UnitDrawReward`（ε）加在 `SplitM1GExactConfiguration`**（`MaxNewPodTripsPerDecision` 之後，default 0＝bit-identical）——M2e 與 PVGS-E（繼承）共用同一欄位，構造上保證兩邊讀同一個 ε。次驗收 xconf：`sw_0_0_eps.xconf`（M2e 側）與 `pvgs_e_eps.xconf`（PVGS-E 側），各僅加 `<UnitDrawReward>-0.1</UnitDrawReward>`。

## 5. 驗收（事前註冊）

0. **KPI 擴充**：`InstanceStatistics` 加 `KPI_IPO` ＝ StatOverallItemsHandled ÷ StatOverallOutputStationArrivals（item pile-on，能耗的物理正解指標；純新增，不動既有 KPI；既有 runs 可由 statistics.txt 回溯計算交叉驗證）。
1. **回歸（旗標關）**：`pvgs_m2e.xconf` seed0 重跑 ＝ 648/656 精準一致（bit-identical 保證）；M2e 側 `UnitDrawReward=0` 回歸 649/643 精準一致。
2. **單元測試（TDD）**：E 評分器抽純函式（給定庫存/殘量/槽位狀態，回傳 N(p,s) 邊際可完成數與分數，含 ε 件數項），新測試進 RAWSimO.Tests。
3. **統治測試（主驗收，使用者定點）**：**w3=0 / w4=0 / w5=0 / ε=0**，small 7200s：
   - M2e：`sw_0_0` 配置補至 5 seeds（現有 s0/s1）；
   - PVGS-E：`pvgs_e.xconf`（權重對齊同點）5 seeds；
   - 判準：**六項（TP/PO/IPO/RD/OD/EOR）5-seed 均值 M2e ≥ PVGS-E，且至少一項嚴格大於（預期為 TP）——全平手不算通過**；逐 seed 明細一併報告。
4. **次驗收（ε 字典序點）**：**ε=−0.1 雙邊同開**（其餘同主驗收點），5 seeds：統治判準同上逐點成立；並驗證 ε 的正效果（IPO 上升、TP 不掉超過 1%）。
5. **速度**：`pvgs_decision_log.csv` 決策中位數維持毫秒級。
6. **後續（不擋合格）**：PVGS-E vs HADGS 三 regime 重跑（small / 4o10b / 4o10b×lines3），縮水即誠實報告；論文表全面換用 PVGS-E＋KPI_IPO。

## 6. 風險與緩解

- **貪婪靠雜訊在單一指標打平/反超**：以 5-seed 均值判定、逐 seed 報告；若翻車，第一嫌疑＝Phase A 完成掃描的順序自由度（誰先收槽），依序診斷，非死路。
- **E 模式全掃描的決策時間**：small 無感（~百 pod × 2 站）；大陣地若超標，加「按真分數排序的 ShortlistK 截斷」當速度旋鈕（沿真目標近似，非另一目標的扭曲）。
- **vs HADGS 戰績縮水**：單軌決議已接受；拆單增益主張改由同引擎 PVGS-E vs PVGS-E-NoSplit（DisableSplitting 與 E 旗標可並用）扛。
- **PO 統治的殘餘不確定性**：M2e(w4=0) 的 PO 僅 2.03——PVGS-E 不做部分指派、不拿 ParentClosingBonus 後 PO 預期落在同水位以下，但這是本設計唯一「構造上沒有嚴格證明」的指標（閉環軌跡發散後無逐點保證），驗收若過即實證封口。

## 7. 相關文件

- 問題診斷：`docs/2026-07-12-po-gap-diagnosis.md`（三嫌疑、D1/D3 結果、範式調性決策）
- M2e 模型定義：`docs/2026-07-11-m2e-model-definition.md`
- 現行 PVGS 設計：`docs/superpowers/plans/2026-07-10-pvgs.md`（Design Decisions 區塊）
- 代碼錨點：`PVGSManager.cs:598`（DecideAboutPendingOrders）、`MethodConfigurationsOB.cs:1035`（PVGSConfiguration）
