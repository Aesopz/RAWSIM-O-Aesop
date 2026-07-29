# M3G 登基：set-level 派遣，tier 家族的下一代

2026-07-27 · `split_milp_m3g.xconf` · branch `setlevel-redesign` · 取代 tier(M2e-IC)為預設模型
2026-07-28 更新：**全塔消融收斂**——見文末「消融定案」一節。正典 config 已改為 MINCORE 版。
2026-07-29 更新：**line-closure 修正 + HGS-M3 貪婪對照**——見下方「2026-07-29」一節。正典已烘入 `LineClosureWeight=30`。

---

## ⭐ 2026-07-29：line-closure 修正 + HGS-M3 貪婪對照

**背景**：建了貪婪啟發式對照 **HGS-M3**（`GreedyM3GManager`，`hgs_m3.xconf`）——M3G 的可部署、可擴展的貪婪版（如 HADGS 之於 M1G）。發現在**相同 T=1 派車上限**下，HGS-M3 的吞吐/效率**全面贏過 M3G**。5-seed 診斷：M3G 站台飢餓 5.4× 於 HGS，因**逐期 exact 優化會全域理性跳過補 pipeline**（軟地板是罰非強制），貪婪逐步局部抓則不會——這是 exact-vs-online 的本質，非 bug。

**失敗的修補（全記，勿重試）**：飢餓寫進目標式（反應式，壓不動）、force-feed 硬約束（壓垮 pile-on 退回 M1G）、拿掉軟地板（更差）、w_pipe 調高（inert）。**飢餓是結構問題，調權重/加硬約束都救不了。**

**✅ 成功修正 = line-closure 值函數（使用者洞見）**：MILP 加 `c[o,i]` 訂單-line 關閉變數（`Σ_s q[o,i,s] ≥ demand·c`）+ 目標獎勵 `−W_line·Σc`，**夾在完成（|w2|=40）與距離之間，item 不獎勵（避免 partial 氾濫）**。定案 **W_line=30**（W→40 撞 |w2| 破嚴格性=strictness 鐵證）。效果：PO 5.82→6.35、IPO 9.70→11.23、EOR 1.49→1.43、飢餓 842→346。

**最終定位（5-seed o100，M3G+line30 vs HGS-M3）**：

| 指標 | M3G+line30 | HGS-M3 | 誰贏 |
|---|---|---|---|
| PO | **6.35** | 6.02 | M3G |
| IPO | **11.23** | 10.89 | M3G |
| EOR | **1.43** | 1.52 | M3G（能耗更省） |
| TP | 291.4 | 295.8 | HGS（−1.5%，每 seed 皆輸） |
| 飢餓 | 346 | 181 | HGS |

**結論**：**exact（M3G）贏「結構效率 + 能耗」主場（PO/IPO/EOR）；greedy（HGS）贏「線上反應性（TP/飢餓）」。互補，誰都不全勝。** TP 那 −1.5% 是逐期 exact 相對反應式貪婪的內在線上劣勢，乾淨手段補不平。**未走的乾淨槓桿 = Backfill**（留住將走的 pod 服務 backlog，零能耗壓飢餓），因動 bot 生命週期較大而擱置。

---

---

## ⭐ 2026-07-28 消融定案（最終 M3G 定義）

使用者主張「模型太客製化 → 拆單效益跟 M1G 的 gap 混雜 → M3G 必須像 M1G 一樣簡單可解釋」,要求把整座權重塔一路砍。全 config 驅動消融(abl1-4 累積剝除 → disA/B/C 解耦 → MINCORE 2-seed+o200 複核)結論:

**拆單效益(pile-on 2.45×、dist/item 不到 M1G 一半)只來自 3 個結構性 delta,不是調參:**
1. `q[o,i,s]` 單位級拆單變數(treatment 本體)
2. `SoftInboundCommitted`(拿掉 P1 硬閘)＝**死鎖的唯一解**(progress reward 不需要)
3. `PipelineFloor`(延遲綁定引擎)＝**pile-on 的唯一來源**(關掉即塌回 M1G:pile-on 9.7→4.6)
- w1 距離 + w2 完成獎勵 = 原封不動沿用 M1G 目標式。

**全砍的死重(逐一驗證 inert/不承重):** γ NewPodPartialPenalty、w3 IdleSlotWeight、ProgressRewardWeight、MultiPartPenalty、SplitGateEnabled。**α/β pod 分層**弱承重(−5% pile-on),使用者裁定保留。

**一句話定義:** M3G =「M1G,但一張單的每個單位可跨站/跨期分配(q 變數),放寬 P1 硬約束以免死鎖,並用 PipelineFloor 延遲綁定以累積 pile-on」——delta 乾淨到跟 M1G 一樣可解釋,M3G vs M1G 的差 = 純拆單。

**MINCORE 2-seed(o100 Fill inv70):** pile-on 9.70、dist/item 7.73(M1G 16.83)、TP 290.5(M1G 313.6;剝塔損 ~10 TP/h = 甩掉的 confound)、o200 兩 seed 不死鎖。

**現行 config(MINCORE):** SoftInboundCommitted=true, PipelineFloorEnabled=true, ReleaseParentOnFirstSplit=true(EPR), Queued/OnTheWay=1/3, w2=-40, **IdleSlotWeight=0, MultiPartPenalty=0, SplitGateEnabled=false, ProgressRewardWeight=0, NewPodPartialPenalty=0**。舊全塔版備份 `split_milp_m3g.PRE_ABLATION.xconf.bak`。

> 以下為 07-27 登基原文(描述舊全塔版,保留供歷史對照;實際模型以上方消融定案為準)。

---

## 命名脈絡

- **M1G**（Jiao et al. 2025）：整單、不拆，pod→單站。`M1GManager`。
- **M2G**（Xie et al. 2021）：split-over-time，跨站＋跨期部分滿足。
- **M3G**（本研究）：**線上 set-level 拆單**——把派遣正當性從 per-pod 硬閘搬到「該站 inbound pod 集合能完成的訂單數 + 訂單級推進度」目標，並用 pod 分層第 4 層軟成本保住 pile-on。M3G = tier(M2e-IC) 的**死鎖免疫版**。

## M3G = 什麼（相對 tier 的 delta）

M3G 是 `SplitM2eICManager` 開 `SoftInboundCommitted` 主開關的具現：
- **軟化 P1**：移除 icP1d / icSG1 / icSG2 三條硬閘 + decode P1 斷言 → 新車可服 partial（不再「新車必須服整單」）。
- **訂單級線性推進項**（`ProgressRewardWeight`）：每單位服務 earn `-w/D_o`（D_o=原始需求）→ 部分推進有小價值 → 死角時派車做部分推進有理由 → 破死鎖。
- **pod 分層第 4 層 γ**（`NewPodPartialPenalty`）：新車服 partial 最貴（0<α<β<γ）→「偏好沉沒供給」的 pile-on 來源改由軟成本保住,不收斂 M1G。
- **CoverageRewardWeight=0**：訂單級推進項取代 SKU 級覆蓋（統一 w2/cov 概念）。

**config（`split_milp_m3g.xconf`）**：`SoftInboundCommitted=true, ProgressRewardWeight=5, NewPodPartialPenalty=6, CoverageRewardWeight=0`,其餘承襲 tier（w2=-40, w3=1000, w4=0, w2p=0, α/β=1/3, T=1）。`SoftInboundCommitted=false` → 逐位等同 tier（zero-drift 驗證過）。

## 誠實定位（經多輪方法論修正後的最終圖像）

| 場景 | M3G vs tier | M3G vs M1G |
|---|---|---|
| **正常 o100（小 backlog）** | **≈ tier**（打平,無額外好處） | 碾壓（pile-on ~2×、能耗 ~半;bot 稀缺兩者都贏 M1G） |
| **大 backlog o200/o300（Fill, inv70）** | **完勝**——tier 死鎖(凍@2971/2223), M3G 跑滿 ~300-307 TP/h（**~4×**） | 碾壓 |
| **稀疏 SKU（Mu-500）/ 極端 bot 稀缺** | tier 崩/死鎖, M3G 穩健 | — |
| **Fixed 同訂單檔** | ≈ tier | 完勝（既定事實,不再特地驗） |

**一句話**：**M3G 在 tier 不死鎖的正常場景跟 tier 打平,在 tier 會死鎖的場景(大 backlog、稀疏 SKU,即使 70% 庫存)救場、維持 ~300 TP/h。** 它的貢獻是**穩健性/泛用性**(不死鎖),不是正常場景的吞吐碾壓。

## 關鍵證據

- **死鎖治癒(Fill inv70,15 bots)**：tier o200 凍@2971(75.8 TP/h)、o300 凍@2223(56.5)；M3G o200 跑滿(300.5)、o300 跑滿(306.8)。**死鎖非 inv50 產物,inv70 照樣發生,M3G 照樣治。**
- **Fixed(Mu-500,15 bots)9 點掃描**：M3G 全點完勝 M1G,吞吐 +3% vs tier、EOR 更好、pile-on −4%(結構性)。
- **zero-drift**：`SoftInboundCommitted=false` 時 kpi/trips 與 tier 逐位一致。

## 標準評估設定（2026-07-27 起,使用者定案）

- **庫存 70%**（`InitialInventory=0.7`,取代先前的 inv50）。
- **Fill mode**(前進驗證一律 Fill;`small_o{100,200,300}_mu100_4h_inv70.xsett`)。
- **Fixed「拆單≫M1G」為既定事實**,不再特地驗證(其用途=乾淨證明 split>M1G,已完成)。

## 待辦

- M3G 參數(prog=5, γ=6)是 Fixed-Mu-500 選的;宜在 **Fill inv70 標準場景**掃 prog×γ 微調定案。
- 補正常 o100 在 inv70 的三方(M3G/tier/M1G)完整表。
- 分支處理:`setlevel-redesign` 現為 M3G 主線;是否合併/改名由使用者定。
- 統計 bug 修(`DataPoint.cs` FootprintDatapoint,commit 於本分支)宜 port 回 `fill-early-release`。
