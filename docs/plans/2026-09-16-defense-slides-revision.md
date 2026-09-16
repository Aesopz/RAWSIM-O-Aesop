# 2026-09-16 口試簡報修改行程

依據：2026-09-15 對 `Defense_aesop_rp.pdf`（50 頁）的逐頁檢查。數據全部核對過管線輸出（`docs/experiments/2026-09-15-*`），表格數值無抄錯；問題集中在呈現一致性、章節結構、空白頁、以及定價論證用了舊正典資料。

⏰ **提醒：明天開工先讀本檔，依序執行「上午」→「模擬啟動」→「下午」。**

---

## 0. 今日目標（一句話）

讓簡報從第 16 頁的 RQ1–RQ3 到第 30–34 頁的結果能一一對上，空白頁填滿，定價論證換成 Canon v1 資料。

---

## 1. 上午：不需要模擬的修改（先做）

### 1.1 格式統一（第 30–34 頁）
- [ ] 五頁表頭統一用 APA 名稱：`Trips/order`、`m/line`、`EOR (kJ/order)`、`Turnover (s)`、`Station idle (%)`（第 31、32 頁目前是程式碼名）。
- [ ] 同欄小數位一致：EOR 三位（1.190 不是 1.19）、Pile-on 三位、Trips 一位。
- [ ] Δ% 一律帶正負號，負號用 `−`（第 33 頁 0.16、0.11、0.3、1.15 缺號）。
- [ ] 第 33 頁副標「Percentage Decrease(%)」→「Δ% vs. M4G」；圖標題「per sovle」→「per solve」；圖加 y 軸單位、Note。
- [ ] 去掉紅綠著色（APA 不以色彩傳遞資訊；且 Turnover +156% 標紅與「紅＝好」矛盾）。若堅持保留，語意固定為「紅＝對 M4G 有利」並在 Note 說明。
- [ ] 每張結果頁加一次 Note：`Canon v1, 10 seeds, paired by seed. Δ% = (B − A)/A. Two-tailed paired t tests. *p < .05. **p < .01. ***p < .001.`
- [ ] 第 31 頁標題改「Step 1: Objective and Pricing (M1G → M4G-NS)」；第 32 頁改「Step 2: Order Splitting (M4G-NS → M4G)」。
- [ ] 第 31 頁副標「equivalent core constraints」改「same commitment unit; objective, pricing, formulation differ」。

### 1.2 章節標籤（頁首藍字）
- [ ] 第 27 頁：Chapter 05 · Methodology → Chapter 04 · Methodology（或移到 05 章內）。
- [ ] 第 31、32、34 頁：Chapter 05 · Experiment and Results；第 33 頁 Chapter 06 → 05。
- [ ] 第 46 頁：Chapter 10 → 08 · Appendix。
- [ ] 第 47、48 頁：Chapter 02 · Literature Gap → 08 · Appendix。

### 1.3 結構
- [ ] 第 29 頁實驗設計：只留有結果的項目（Stations/Bots、MaxPartsPerOrder；Packing capacity 若明天跑完才留）。刪 SKU size、Pod capacity、Backlog、Delta。
- [ ] 第 45 頁紅字備忘錄：刪除或移到 Appendix 末尾標「Notes」。
- [ ] 第 13 頁：右側自己模型的 Packing station 改虛線並標「not modelled」，與底部註解一致。
- [ ] 第 12 頁 Jiao et al. (2023) 在第 14 頁出現但未介紹：第 11 或 14 頁補一行，Reference 頁補條目。
- [ ] 第 19 頁「Fixed-λ sweep optimum 14–24 lands on measured marginal price 13.7–18」與第 24 頁「18–50 all n.s.」矛盾 → 改為「λ* varies ≈3× within one run (P10–P90, 10 seeds)」，數據見 1.5。
- [ ] 第 22 頁估值層例子加數值：`e = 1 (orange); ê = 3 (orange, lemon, apple)`。
- [ ] 第 48 頁表格加來源標籤（policy、seed、正典版本）。
- [ ] 第 25 頁圖加版本/日期標籤。

### 1.4 新增頁（資料已齊，直接做圖）
- [ ] **N 掃描頁（回答 RQ2）**：兩張 APA 圖，Items vs N。
  - 小規模：N=1（M4G-NS）、2、3、4、∞（M4G），6 與 10 bots 兩條線，誤差線 95% CI。
  - 大規模：N=1（HADGS）、2、3、4、∞（HGS-M5）。
  - 資料：`experiments/formal/2026-09-15_canon_v1_small_split_limit_fidelity/stats.json`、`…large_split_limit_m5/stats.json`、`docs/experiments/2026-09-15-canon-v1-cross-batch/`。
  - 結論句：Small saturates at N = 3; large needs N = 4; N = 2 underperforms no-split at 10 robots.
- [ ] **Turnover 代價頁（回答 RQ3）**：M4G-NS vs M4G 的 Turnover 中位數與 Station idle，6/10 bots；大規模 HADGS vs M5。附一句「WIP is a steady-state stock; see 2/4/8 h horizon (memory: project_split_horizon_curve)」。
- [ ] 第 35 Discussion、37 Key Findings、38 Contributions、39 Limitations、40 Future Work：用四組 CSV 的結論填。Limitations 必寫：Turnover +84–156%；packing 未建模；單一 layout；M5 訂單數 −2.5%。
- [ ] Reference 頁補齊（De Koster 2007、Xie 2021、Jiao 2023/2026、Tadumadze 2023、Kizil 2026、Rizqi 2025）。

### 1.5 λ* 分布數據（已算好，供第 19 頁）
| bots | P10 | P50 | P90 | P90/P10 |
|---|---|---|---|---|
| 6 | 2.59 | 4.31 | 7.68 | 2.97 |
| 10 | 2.86 | 4.50 | 8.66 | 3.02 |
來源：`out/tau10_ns_{6,10}_s0-9` 決策紀錄，派車決策的 λ_end。要做圖時用 `scripts/` 重算並走 APA 圖規範。

---

## 2. 模擬：建議明天啟動的批次（依優先序）

全部走 `exp.py`：`new → plan → 列參數表給使用者確認 → run`。**未確認不啟動。**

### S1（最高優先）固定 vs 動態定價，Canon v1 重做
- 目的：取代第 24、27 頁的舊正典資料；回應「你怎麼確定動態贏過每個 λ」。
- 模型：**M4G-NS**（消融鏈一致；NS 目標式只看 μ，所以必須同時固定 μ）。
- 固定組：`LambdaFixed`＋`MuFixed`，**關閉 `UpperBoundJump`**（真固定），`DinkelbachIterations=0`。
- 格點：以動態 NS 派車時 λ 中位數（6b 4.31 / 10b 4.50）為錨，取 ×1、×2、×4、18、26 五點；μ 依動態組 μ/λ 比例同步。
- 機隊：6、10 bots；10 seeds；kind=formal。
- 場數：(1 動態既有 + 5 固定) × 2 × 10 = **100 場**（動態組直接用 `tau10_ns`，實際新跑 100）。
- 小規模 NS 每場約 5–10 分鐘，7 併行約 **2–3 小時**。
- 待使用者決定：β 是否一起固定（`DeltaFixed`）。建議先只固定 λ、μ。
- 風險：低 λ 組（×1）預期大量空任務、產出崩跌；報告時寫成「常數價格的結構性限制」，不當發現。

### S2 包裝站容量掃描，Canon v1
- 目的：使用者要求的延伸實驗；第 11 頁引用 Xie「benefits decrease when packing capacity limited」，需自己的數據呼應。
- 模型：M4G 小規模；旗標 `PackingStationCount=1` ＋ `PackingBufferCapacity ∈ {40, 50, 60, 78}`；對照＝Canon M4G（=0 關閉）。
- 舊 `m4g_pb*.xconf` 缺 Canon v1 三旗標，**不可直接用**，要從 Canon 複製後只加兩行。
- 機隊：6、10 bots；10 seeds；formal。
- 場數：4 × 2 × 10 = **80 場**，M4G 每場 10–20 分鐘，7 併行約 **3–4 小時**。
- 先跑 fast 1 seed（pb50, 6b）確認旗標行為與輸出欄位，再開 formal。

### S3（可選）大規模 M5 不拆單對照
- 目的：把第 34 頁的 +31% 拆成定價／拆單兩段，堵「大規模 31% 有多少來自拆單」。
- 程式現況：`GreedyM5NSManager` 存在但 2026-08-10 後未更新，**不是 Canon v1 的鏡像**（不含 DrawsFirstDispatch、M1GUrgentGate 等）。需要先評估是否改成 `GreedyM5Configuration + OrderAtomicNoSplit`，這是程式修改＋守門員，非明天能跑完。
- 建議：明天只做可行性評估，不排模擬。

### 不建議明天跑
- 第 29 頁列的 SKU size / Pod capacity / Backlog / Delta 掃描：與 RQ 無直接對應，時間不夠。
- 大規模 N=1 M5：使用者已定案不跑。

### 時程建議
| 時段 | 事項 |
|---|---|
| 09:00 | 讀本檔；S1 先量動態 NS 的 μ/λ 比例（不跑模擬）→ 列 S1 參數表請使用者確認 |
| 09:30 | 啟動 S1（100 場，7 workers）；同時做 1.1–1.3 格式修改 |
| 11:00 | S2 fast 1 seed 確認 → 列參數表確認 → 排隊啟動（會等 S1 讓出核心） |
| 下午 | 做 1.4 新增頁（N 掃描、Turnover）與空白頁 |
| S1 完成後 | verify → table → NOTES → 產 CSV 組與第 24/27 頁新圖 |
| S2 完成後 | 同上，產包裝容量頁 |

---

## 3. 交付檢查（睡前）
- [ ] 所有結果頁：APA 表頭、Note、無中文/代號/紅綠。
- [ ] RQ1 ↔ 第 32 頁；RQ2 ↔ N 掃描頁＋6/10 bots；RQ3 ↔ Turnover 頁。
- [ ] 第 24、27 頁已換成 Canon v1 資料或 λ* 分布圖。
- [ ] `exp.py status` 全部 analysed；CSV 組在 `docs/experiments/`。
