# 方法論稽核（2026-09-17）

目的：在投稿與口試前，逐一檢查論文中會出現的每個政策（M1G、M4G-NS、M4G、M4G-WS、M4G-WS₀、M4G-WS-NS、HGS-M5、HADGS）之**程式實作是否與論文主張一致**，以及所有指標（12 欄）與能耗模型的計算來源是否可辯護。依據為 Canon v2 組態檔與 `RAWSimO.Core` 原始碼（commit 777f71b），非依記憶。

嚴重度：🔴 需在寫作前修正或明確揭露；🟡 需在論文中揭露／措辭調整；🟢 已確認一致。

---

## 0. 總表

| 政策 | 實作位置 | 與主張一致 | 需處理 |
|---|---|---|---|
| M1G（Jiao） | `M1GManager.cs`（原檔＋旗標閘門，預設逐位相同） | 🟢 | 🟡 揭露：檔案含閘門式擴充，正典全部關閉 |
| M4G-NS | `m4g.xconf`＋`OrderAtomicNoSplit`＋`OrderAtomicCanonPrices` | 🟢 | 🟡 候選單集合較 M4G 嚴（見 2.3） |
| M4G | `M4GManager.cs` Compact＋LineAtomic 分支 | 🟢 | 🟡 目標式是「距離／線」比值，不是「件數」；用字要改 |
| M4G-WS / WS₀ / WS-NS | `LegacyObjective` 分支 | 🟢 | 🟡 WS 沿用 M4G 可行域，非 Jiao 原模型；命名已學術化 |
| HGS-M5 | `GreedyM5Manager.cs` | 🟢 | 🟡 一線不可跨兩貨架（已在 spec 揭露）；v2 整單 fallback 屬 HADGS 式接受準則 |
| HADGS | `HADGSManager.cs`＋`hadgs_aligned.xconf` | 🟢 | — |
| 指標／能耗 | `InstanceStatistics.cs`、`InstanceEvents.cs`、`BotNormal.cs`、`EnergyConsumption.cs`、`stats_pipeline.py` | 🟢 | 🔴 Pile-on 定義用字；🟡 EOR 與距離高度共線；🟡 拆單在站端零時間成本 |

---

## 1. M4G（主模型）

### 1.1 每一決策解的模型（Compact＋LineAtomic 分支，`BuildModel` → `AddSharedConstraints` + `AddCompactConstraints`）

**變數**：`xps[p,s]`（貨架 p 派往站 s）、`yrp[r,p]`（機器人 r 取貨架 p）、`gh/gb[o,i,s]`（訂單 o 的線 i 在站 s 被**估值／綑綁**放置）、`ch/c[o,i]`（線估值／綑綁關閉）、`zh/z[o]`（訂單估值／綑綁完成）、`y[o,s]`（o 佔用 s 一個槽）、`vpa[i,s]`（新貨架供給）。

**限制式**（皆已讀碼確認）：
- R1 每貨架至多一站；R2 新貨架須有機器人；R3 每機器人至多一貨架；R4 每貨架至多一機器人；R5 在途貨架固定於其目的站與載具。
- V1c 站 s 對 SKU i 的估值需求 ≤ 在途貨架存量 + `vpa`，`vpa` ≤ 派往 s 的新貨架存量（V1cPa）。
- V10c Σ_s gh = ch；B12c Σ_s gb = c；B1c gb ≤ gh。→ **一條線整條落在一站**（LineAtomic），但同一訂單的不同線可落在不同站或不同期。
- V4 zh ≤ ch（每線）；V4g 有線無庫存則 zh = 0（HonestCompletionReward）；B7 z ≤ c；B6z z ≤ zh。
- B2c/B4c y 與 gb 互相蘊含；B3 Σ_o y[o,s] ≤ Cs[s]（**槽容量只約束綑綁層**，估值層看不到——這是設計，δ 即為此而生）。
- `AddSplitCapConstraints`：MaxPartsPerOrder／包裝緩衝（大規模實驗用）。

**目標式**（`SolveM4G`，最小化，全部以公尺計價）：

  D(x) − λ(1−δ)·Σc − λδ·Σch − μ(1−δ)·Σz − μδ·Σzh

其中 D = Σ 新派貨架的（機器人→貨架 + 貨架→站）曼哈頓距離（`EstimateBotPodDistance`/`EstimatePodStationDistance`，`UseBAED=false`），在途貨架為沉沒成本不計。ε、ρ、σ 在正典皆為 0（Compact 模型無單位級變數）。

### 1.2 價格（`M4GPricing.cs`）

- λ₀ = 累積揀貨行駛距離 ÷ 累積關閉線數（`PickDistancePricing=true` → 只算 extract 段）。暖機（<50 線）用 `LambdaFallback=10`。
- μ = λ × κ，κ = 累積關閉線 ÷ 累積完成單（暖機 2.4）。**μ 隨 λ 等比縮放**，所以 Dinkelbach 迭代時 μ/λ 恆為 κ。
- δ = 歷史（綑綁線 ÷ 估值線），依 |Pb| 分層（0,1,2,≥3），層樣本 <50 線時退回總體；暖機 0.05；截於 [0,1]。
- 結論：**除 κ 與 δ 兩個「實測比率」外，模型沒有任何人工權重**。「self-calibrating」的主張成立。

### 1.3 Dinkelbach（`DecideAboutPendingOrders`）

- 解 F(λ_k) = min D − λ_k·V；V* = (D* − Obj*)/λ_k；λ_{k+1} = D*/V*；最多 5 次，|Obj| ≤ 0.5 收斂。
- V* ≤ 0（無利可圖、什麼都不派）→ `UpperBoundJump`：λ 跳到「最便宜且能關閉一條線的單趟成本 + τ（τ = 0.5，`LineBoundTau`）」再解一次，保證至少一趟可行趟次變得划算。
- 收斂點意義：λ* = 最小可達的「公尺／線當量」比；所有邊際成本 < λ* 的趟次都會被納入。**這是 λ 門檻控制器，而不是件數最大化**。

🟡 **措辭風險**：摘要／標題目前寫「exchange rate between items and travel distance」。程式裡的分母是**線（line）**與**訂單（經 κ 折成線當量）**，件數只透過線間接進入。建議全文改為「line–distance exchange rate」或「service–distance」。口委若問「你的目標式哪裡有件數」，現在的寫法會被抓。

🟡 **理論風險（要在方法章正面回答）**：最小化比值 D/V 在單一決策上可能偏好「少而精」的趟次而讓機器人閒置。實作上有兩道防線：(a) λ* 是門檻，凡邊際公尺／線低於 λ* 的趟次都會入選，不是只選最好的一趟；(b) UpperBoundJump 保證有可用機器人與新貨架時不會空手。建議在論文引用決策日誌的 `dinkIters`／`lbFail` 欄位給出「每決策平均派車數」佐證。

### 1.4 候選集合（`BuildSnapshot`）

- 候選訂單：任一線有庫存（`Any`）即納入；NS／OrderAtomic 則需全部線可被 PiSKU 完整覆蓋（`All`）。
- 新貨架 Pa：儲位上、未被佔用、含任一需求 SKU；`CandidatePodTopK`（小 0＝不截斷，大規模 M5 用 10，K∈[5,20] 已證無影響）。
- 機器人 Ra：空手且無任務；`UseReturnPendingBots=false`、`ContinuousDispatch=false`（與 HADGS 對齊）。
- 緊急閘門 `M1GUrgentGate`：與 `M1GManager.GenerateOd` 同形（含 Jiao 原碼的 `All` 怪癖）；`|Od| > 空槽數` 才啟動。**M1G、M4G、M4G-NS、M5、HADGS（`UseM1GTriggerGate=true`）全部同一閘門**。

### 1.5 提交（`CommitM4G`）

- 綑綁放置依 `NewPodFirstAllocation=true` 先從新貨架配貨；短缺會丟例外（V1c 保證不會）。
- 拆單透過 `Order.CreateSplitChild`，`ReleaseParentOnFirstSplit=true`；`_pricing.RegisterDecision(綑綁線, 估值線, 層)` 餵回 δ。

---

## 2. M4G-NS（不拆單對照）

### 2.1 實作
`m4g.xconf` 加兩旗標：`OrderAtomicNoSplit=true`（`AddOrderAtomicConstraints`：NS1 供給、NS2 Σ_s oyh = zh、NS3 y ≤ oyh、NS4 Σ_s y = z）、`OrderAtomicCanonPrices=true`（目標式 z·[λ(1−δ)·n(o) + μ(1−δ)]、zh·[λδ·n(o) + μδ]）。λ 上界改用 `ComputeLambdaUpperBoundOrderAtomic`。δ 以「訂單×線數」餵回（`BoundLinesByOrder`）。

### 2.2 一致性 🟢
- 與 M4G 差異**只有承諾單位**：整單一次落在一站。目標式價格逐項對應（線價 × n(o)），Dinkelbach、閘門、候選集、距離函數皆共用。
- 這正是「拆單的淨效果」消融所需的最小差異對照。

### 2.3 需揭露 🟡
- 候選訂單集合較嚴：NS 只考慮**全部線都有庫存**的訂單，M4G 考慮任一線有庫存者。這是不拆單的邏輯必然（不能承諾做不完的整單），但意味著 NS 在缺貨 SKU 多時會少看到一些訂單。建議一句話揭露：「NS 的可行訂單集合為 M4G 的子集，此為整單承諾之定義後果。」

---

## 3. M4G-WS、M4G-WS₀、M4G-WS-NS（Jiao 權重目標式）

### 3.1 實作（`SolveM4G` 的 `LegacyObjective` 分支）
  min w₁·D + w₂·Σzh + w₃·Σidle_s，w₁ = `LegacyDistanceWeight`（WS=1，WS₀=0）、w₂ = `LegacyOrderReward` = −40、w₃ = `LegacyIdleSlotWeight` = 1000，`LegacyRewardValuation=true` → 獎勵掛在估值變數 zh（對應 M1G 的 yos）。Dinkelbach 整段跳過（λ 無定義）。
- 與 `M1GManager.cs:767-769`（w1=1, w2=−40, w3=1000）逐字一致。
- WS-NS 再加 `OrderAtomicNoSplit=true`、`OrderAtomicCanonPrices=false`。

### 3.2 一致性 🟢／需揭露 🟡
- WS **不是** Jiao 的模型重跑；它是「Jiao 的目標式放在 M4G 的可行域（含拆單、線級變數、估值／綑綁雙層）」。論文必須這樣定義它，否則審稿人會拿 M1G 的數字對照問為何不同。
- 2×2（NS／split × WS／ratio）因此是**同一可行域、只換目標式或只換承諾單位**的乾淨因子設計，這點可以明講。
- WS₀（w₁=0）目標式中距離完全消失，只剩「多完成單、少空槽」；它回答「距離放進目標式是否必要」——結論是必要（EOR 差 −16~−35%）。

---

## 4. HGS-M5（貪婪鏡像）

### 4.1 實作（`GreedyM5Manager.cs`）
- 兩種移動：draw-line（一條線整條從站上存量關閉，Δ = −λ，若為該單最後一線再 −μ）與 dispatch（派一新貨架，成本 = 機器人→貨架＋貨架→站，淨值扣除它立即解鎖的 draw）。`DrawsFirstDispatch=true`：先耗盡所有負值 draw，再評估 dispatch（已證精度 n.s.、求解 −76~82%）。
- 迭代選最負者，直到無負值。槽與機器人是硬限制（同 B3、R3）。
- 結束後 `ValuationSweep` 加上 −λδ·估值線 − μδ·估值單，並以同一「綑綁／估值」比餵 δ。**目標式與 M4G 逐項相同**，故 obj(M5) ≥ obj(M4G) 是真實可報告的最適性差距。
- λ 迭代：`LambdaIterations=5`、`LambdaTolerance=0.5`、`UpperBoundJump`、`LambdaEscalations=0`，與 M4G 的 5／0.5／jump／0 對齊。
- Canon v2 `PackingFullWholeOrderFallback=true`：僅在拆分預算綁定（包裝緩衝滿或 `MaxPartsPerOrder` 只剩一份）且所有單步移動皆無利時，才組合最便宜的貨架集合整單派出（HADGS 式「任何可覆蓋即接受」）。預算不綁定時為死碼——已由 2026-09-17 守門逐位證明。

### 4.2 需揭露 🟡
- 一條線不能由兩個貨架拼湊（M4G 可以）——spec 2.3 已揭露；小規模件數差 0.16／0.11%（n.s.）即為此差距的實測上限。
- v2 fallback 的接受準則（任何覆蓋皆接受、取最便宜）**不是**比值目標式的一部分；它是預算耗盡時的退化規則。論文中應寫成「當拆分預算耗盡，M5 退化為整單啟發式（與 HADGS 同準則）」，並說明 M4G 在同情境下由 `AddSplitCapConstraints` 直接處理，兩者機制不同。大規模只跑 M5，所以這個差異不會混入任何 M4G vs M5 對照。

---

## 5. M1G 與 HADGS（外部基準）

- `M1GManager.cs` 歷史上加過閘門式擴充（starve-aware、SA-M1G、`SlotPenaltyWeight`、`RobotIdTieBreak`、`IncrementalValuationEnabled`），**正典組態全部關閉，預設值註明逐位相同**。`us` 變數上界 6 與站容量 6 一致（若未來站容量 >6 會出錯，需注意）。
- HADGS 用 `hadgs_aligned.xconf`：`UseBAED=false`、`UseReturnPendingBots=false`、`UseM1GTriggerGate=true`。與 M4G/M5 的差異僅在決策邏輯本身。
- `FastLane=true` 在所有組態中出現，但 M1G／HADGS 只建立選擇器未使用；M4G／M5 完全不引用。無影響，但建議正典組態統一改 false 以免被問。🟡（改動會影響 xconf 逐位比對；若改，須 canon-bump 並以守門證明逐位相同。**不建議現在改**，寫進限制／設定說明即可。）

---

## 6. 指標與能耗程式碼

### 6.1 計數事件（`InstanceEvents.cs`、`OutputStation.cs`）
- Items：每一次揀取 `NotifyItemHandled` +1。
- Lines：`PositionServedCount ≥ PositionOverallCount` 時 +1；LineAtomic 下每條母單線只對應一條子單線，不重複計。
- Orders：**只在母單合併完成時** +1（`parent.NotifyChildCompleted` 為 true → `NotifyOrderCompleted(parent)`）；子單完成不計。Turnover 以母單 `TimeStamp` 到合併時刻計。🟢 拆單不會灌水訂單數。
- Trips：`StatOutputStationArrivals`（貨架到站次數）。

### 6.2 衍生指標（`stats_pipeline.run_metrics`）
- Pile-on = Orders ÷ Trips（`StatSystemOrderPileOn`）。🔴 **摘要與研究目的寫的是 “items served per pod visit”，程式算的是「每趟到站完成的訂單數」**。兩者方向一致但不是同一量；投稿版必須統一——建議用字「orders completed per pod visit (order pile-on)」，或改算 items/trips 並在 REPORTING-STANDARD 改定義。我建議**保留 orders/trips 並改用字**：它對拆單較嚴格（拆單使單筆訂單需要更多趟），是「對自己不利」的定義，審稿人不會質疑偏袒。
- m/Line = 全部機器人總距離 ÷ Lines（含補貨與歸位段）；目標式只計揀貨去程。🟡 需揭露「目標式是去程代理，指標是全程」。
- EOR = `StatOverallEnergyTotalJ`/1000 ÷ Orders，機械能 E1–E5（加速、減速、巡航摩擦、旋轉、舉降），質量含機器人 115 kg＋貨架框 100 kg＋載重；支援功率 `SUPPORT_POWER_*` = 0，等待能耗另計不入 EOR。🟢
- 🟡 **能耗與距離高度共線**：`BotNormal.cs:1302` 每段路徑以 `getTimeNeededToMove(0, d)` 從靜止起步計算，即每一路段皆視為停走一次；能耗 ≈ 線性於段數×質量，已實測 cv 1.6%、方向一致率 97.7%。因此 EOR 與 m/order 幾乎是同一資訊的兩種單位。論文可保留 EOR 作為「以物理模型換算的可讀單位」，但**不要把 EOR 當成獨立於距離的第二證據**。
- Turnover median = `StatMedianTurnoverTime`。🟢
- Station idle = Σ IdleTime ÷ UpTime（含初始化與貨架間隔）。🟢

### 6.3 拆單的站端成本 🟡
`OutputStation.OrderCompletionTime` 從未被賦值（= 0），子單完成不佔站時間；合併只受 `PackingBuffer` 容量限制，無人工或時間成本。→ 本模擬量到的「拆單代價」只有 (a) 週轉時間、(b) 包裝緩衝容量、(c) 每趟貨架交接時間（`PodTransferTime` 2.2 s）。**限制章必須寫明合併作業的人力／時間未建模**，這是教授「成本搬到未建模處」批評的正面回應方式：我們建模了容量、沒建模時間。

### 6.4 統計管線 🟢
- 配對以 seed 為單位（seed 決定訂單流），雙尾 `scipy.stats.ttest_rel`，95% CI 用 t 分佈，dz = 均差／差之 SD；Excel 原生公式交叉驗證 1e-9。
- Δ% = (mean_b − mean_a)/mean_a；比值型指標（Pile-on、m/Line）以每 seed 比值取平均後再比，屬「平均的比值」而非「比值的平均」之爭議，n=10 下差異可忽略，但表格 Note 已寫 "means over 10 seeds"，一致。

---

## 7. 行動清單

| # | 嚴重度 | 事項 | 位置 |
|---|---|---|---|
| 1 | 🔴 | Pile-on 用字統一為「orders completed per pod visit」；摘要／研究目的／投影片全部改 | 摘要、REPORTING-STANDARD §2 |
| 2 | 🟡 | 「items–distance exchange rate」改為「line–distance」（或 service–distance）；說明件數經由線進入 | 摘要、方法章 |
| 3 | 🟡 | 方法章加一段：Dinkelbach 收斂點是門檻 λ*，非單趟最優；UpperBoundJump 防止空手 | 方法章 3.x |
| 4 | 🟡 | 限制章：目標式只計揀貨去程曼哈頓距離；指標計全程；EOR 與距離共線（cv 1.6%） | 限制章 |
| 5 | 🟡 | 限制章：合併／包裝的人力與時間未建模，只建模容量 | 限制章 |
| 6 | 🟡 | 定義 WS 為「Jiao 目標式 × M4G 可行域」，2×2 為同可行域因子設計 | 實驗章 |
| 7 | 🟡 | NS 候選集合為 M4G 子集（整單承諾之定義後果）一句話揭露 | 實驗章 |
| 8 | 🟡 | M5 v2 fallback 寫成「預算耗盡時退化為整單啟發式」，與 M4G 的容量限制式機制不同；大規模不含 M4G 故無混淆 | 方法章 M5 |
| 9 | 🟢 | 其餘：閘門、距離函數、機器人集合、δ 餵回、訂單計數皆一致，無需動作 | — |

程式碼**無需修改**即可投稿；所有問題都在文字層。
