# M2e Sunk-First 修正 + 同族比較防禦策略（討論存檔 2026-07-14）

> 狀態：brainstorm 進行中，尚未定 spec。本檔記錄決策脈絡，供接手 session 對齊。
> 事實來源仍以 code / git log / memory 為準；本檔是「為什麼要做這件事」的脈絡。

---

## 1. 觸發問題：M1G vs HADGS 逆轉的口試風險

拆單開放後出現架構逆轉：
- 原型論文（Jiao et al., Omega 138 (2026) 103374, Table 8）中 M1G 只小贏 HADGS **0.11–0.32% TP**（近似等價）。
- 但在本專案環境，**HADGS 全面壓過 M1G**（acceptance 5-seed：HADGS 648.2 TP / 7.02 pile-on / 10.60 vs M1G 640.0 / 3.65 / 17.93，且 M1G s3 崩到 265 vs HADGS 645）。

逆轉根因（見 `docs/2026-07-14-m1g-hadgs-reversal-analysis.md`）：
- 快照 MILP **沒有時間維度**，看不見 sunk pod（在站/在途）殘值與 bot 機會成本。
- HADGS 的事件節奏（per-station OA drain → 加一顆 pod → goto OA）＝隱性時序優化 + sunk-first 字典序控制流。
- 兩個獨立證據：**(證據一)** M1G 目標式 w2=−40 掛在 `yos`（x̂ **潛在**獎勵，M1GManager.cs:700）+ shi5 供給約束綁 `yos`（:716-723）→ 肥新 pod（−200~−240 潛在）打敗滯留殘值（−40 實際）；**(證據二)** M1G Od 機制（:377-395, 637-638）替換觸發時，用 `All(...UnusedPods.Contains(v))` 判準把「站上可服務的單」恰好全踢出模型。

## 2. 防禦策略（使用者 2026-07-14 定案）

**核心洞見：貢獻是「拆單」，不是「MILP vs heuristic 誰強」。** 用同族 ablation 隔離拆單效應，避免口委追問架構逆轉：

- **小規模**：比 **M2e vs M1G**（含 M1e-exact 排除 greedy-attribution 干擾）→ 隔離拆單效應。
- **大規模**：比 **PVGS vs HADGS**（large layout、bot=45 標準）→ 拆單效應是否在可擴展架構上複現。
- **M1G 與 HADGS 永遠不同表**（同表逆轉就攤在眼前）。真被問「M1G vs HADGS 誰好」→ 退路引 Table 8（近似等價）+「族間比較非本文對象」。

此設計繼承原型論文自己的慣例：exact 只跑小規模（Table 5/6，2 站、6-12 robots），大規模全用 HADGS。口委難質疑原型論文自己就採用的設計。

**升級敘事**：兩族都量到正向拆單效應（小規模 M2e pile-on 4.60 > M1G 3.65；大規模 PVGS 8.28 > HADGS 7.02）→ 主張「拆單增益在兩種架構族上都複現」，比單族更防打。

**要預先堵的兩個追問**：
1. 為何小規模 MILP 族、大規模 heuristic 族？→ MILP 求解不可擴展，PVGS 是可擴展替身，且已用 exact 交叉驗證（PVGS 656 vs exact 649、45× 加速）。論文放 solve-time vs 規模小圖釘死。PVGS vs M2e-exact 比較只進 scalability validation 小節，不進主結果表。
2. 表格紀律：M1G/HADGS 分表分節。

## 3. 必須先修的前置條件：M2e ≥ PVGS（使用者硬性要求）

防禦策略成立的前提是「exact 是最優基準、PVGS 是它的近似」。但**現況相反**：

| 指標（小規模） | M2e-exact | PVGS |
|---|---|---|
| TP（θ=6 對照） | 643 | 648 |
| TP（M1e 對照） | 649 | 656 |
| pile-on（5-seed acceptance） | 4.60 | 8.28 |

PVGS 在 TP 和 pile-on **雙雙壓過 exact**。閉環模擬中「heuristic 贏 exact」非數學矛盾（exact 只保證單快照最優，不保證快照序列最優），但口委一問「你的 MILP 到底 model 對了什麼」很難看。**必須修掉。**

## 4. 修正方向：Sunk-First Two-Solve（待 brainstorm 定案）

把 HADGS 靠控制流拿到的字典序優先權，正式寫進 MILP 求解結構：
- **Solve 1**：只允許用 sunk pod（在站/在途）填 slot，最大化榨取。
- **Solve 2**：對殘餘需求才開放新派車。

如此 M2e 同時擁有「exact 單步最優」＋「HADGS sunk-first 時序結構」，理論上 ≥ PVGS（PVGS 的 K(p) 打分只是這個結構的貪婪近似）。

**Od 修正**可作同一包第二個小修：mirror（`SplitM1GExactManager.cs:74-100, 293-295`）有自己的 `GenerateOdSplit` 副本，改 mirror 完全合規（不碰 M1GManager.cs）。可加 config-gated `OdMode`：0=legacy mirror（預設 bit-identical）、1=關替換、2=HADGS 式總和判準。

**先量再修**：mirror decision log 加 `odCount`/`odFired` 欄位，重跑 1 seed（6 秒級）看 firing rate。近零 → Od 線結案，殘差全歸 cadence；可觀 → 做 OdMode ablation。

**驗收準則（定死）**：5 seeds、**預設權重**（不用 m2e_C 的 w4/w5 定價，避免不對稱調參嫌疑）、**TP 和 pile-on 兩項都 M2e-sunk ≥ PVGS**。

**備援敘事**（若 sunk-first 仍差一點）：「exact 為單步最優、閉環中 heuristic 因隱性時序資訊可小幅超越」——文獻站得住但較弱，故先全力做 sunk-first。

## 5. 與既有負結果的關係

- **不衝突 M2e-PR 負結果**：PR 教訓是「目標式端獎勵重塑無效」（見 [[m2e-pr-negative-result]]）；sunk-first 是**求解結構**改動、Od 是**入場過濾**，皆非目標式獎勵形狀，是不同類 lever。
- **呼應 slot-ceiling 診斷**（[[slot-ceiling-diagnosis-2026-07-12]]）：晚綁定/規劃-槽綁定解耦方向一致。

## 6. 已知 pile-on lever（獨立於本修正）

m2e_C（w4=PodTripFixedCost=40 + w5=ProcessingPodDrawReward=−1）5-seed：pile-on 8.46 > PVGS 8.28、dist/item 8.84 < 9.63、orders 606.4（−5.6%）。此為定價 lever，與 sunk-first（結構 lever）正交；驗收 M2e ≥ PVGS 刻意**不用**這組權重以免調參嫌疑。

## 7. 下一步

1. 完成 sunk-first two-solve brainstorm（approach 取捨 → spec）。
2. Od firing rate 量測（決定 Od 修正是否納入）。
3. spec → plan → SDD 執行。
4. 驗收：5 seeds 預設權重 TP+pile-on 雙指標 M2e-sunk ≥ PVGS。
5. 通過後才進大規模 C 系列（HADGS vs PVGS）。
