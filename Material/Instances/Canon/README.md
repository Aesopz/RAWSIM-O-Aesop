# 正典實例範本（Canon）

建立：2026-09-15。本資料夾是**跑新實驗時唯一的起點**。`Material/Instances/CoreBenchmark/` 保留作為歷史資料的來源，不再從那裡直接跑新批次。

命名規則見 `docs/naming-standard.md`。核心只有一條：**檔名與內部名稱逐字相同**。這樣輸出目錄 `<NameLayout>-<xsett Name>-<xconf Name>-<seed>` 可以直接讀出場景。

## 1. 範本清單

### small

| 檔案 | 用途 |
|---|---|
| `small_6b_2p_2r.xlayo` | 6 bots、2 揀貨站、2 補貨站、100 pods / 120 cells、cap 100 |
| `small_10b_2p_2r.xlayo` | 同上，10 bots（與 6b 只差 `BotCount`） |
| `small_fill100_mu100_2h_inv70.xsett` | Fill 100 張、Mu-100（100 SKU）、7200 秒、庫存 70% |
| `m1g.xconf` | M1G 基準 |
| `m4g.xconf` | M4G 正典（拆單） |
| `m4g_ns.xconf` | M4G-NS 正典（不拆單＝M4G＋`OrderAtomicNoSplit`＋`OrderAtomicCanonPrices`） |
| `hgs_m5.xconf` | HGS-M5 正典 |

場景行：`2 h · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock`

### large

| 檔案 | 用途 |
|---|---|
| `large_45b_12p_6r.xlayo` | 45 bots、12 揀貨站、6 補貨站、1203 pods / 1352 cells、cap 500 |
| `large_fill200_mu1000_2h_inv70.xsett` | Fill 200 張、Mu-1000（1000 SKU）、7200 秒、庫存 70% |
| `hgs_m5_k10.xconf` | HGS-M5 正典＋`CandidatePodTopK=10` |
| `hadgs_aligned.xconf` | HADGS（`UseM1GTriggerGate=true`） |

場景行：`2 h · 12 Pstations · 6 Rstations · 1000 SKUs · 200 backlog · 1203 pods / 1352 cells · cap 500 · 70% stock`

大規模只跑 HADGS 與 M5；M1G、M4G、M4G-NS 跑不動。

## 2. 範本對應的正典狀態（2026-09-15）

- M4G／M4G-NS／M5 共同：`NewPodFirstAllocation`、`LineBoundTau`、`M1GUrgentGate` 皆為 true。
- M5 另有：`DrawsFirstDispatch=true`。
- 不變式（改動範本後必須仍成立）：
  - `m4g_ns.xconf` 對 `m4g.xconf`：只差名稱與兩行 order-atomic 旗標。
  - `hgs_m5_k10.xconf` 對 `hgs_m5.xconf`：只差名稱與 `CandidatePodTopK`。
- 來源檔與其 SHA-256：`provenance.json`。範本內容與來源逐行相同，只有內部名稱那一行不同（2026-09-15 以腳本驗證）。

## 3. 跑實驗的流程

1. **列出場景給使用者確認**：場景行、bots、policy、xconf、seeds。確認後才進行下一步。
2. **建立實驗並複製範本**：`exp.py new <YYYY-MM-DD>_<slug> "<問題>"`，把要改的範本複製到 `experiments/<id>/inputs/`（未改動的檔案直接引用 `Canon/...`）。
   **不要改動 Canon 內的檔案。**
3. **一次只改一個實驗變數**，並依規範誠實命名，**內部名稱同步修改**：
   - xconf：`<model>_<variant>`，例如 `m4g_n2.xconf`（`MaxPartsPerOrder=2`）、`hgs_m5_k10_n3.xconf`；
   - xsett：改對應欄位，例如 `small_fill200_mu100_2h_inv70.xsett`；
   - xlayo：改對應欄位，例如 `small_8b_2p_2r.xlayo`、`small_6b_2p_2r.cap200.xlayo`。
4. **驗證只差目標行**：`diff <範本> <實驗檔>` 應只有名稱行與實驗變數行。
5. 實驗資料夾內放一份 `README.md`，寫明實驗問題、改了哪個變數、對照的範本。
6. 用 `exp.py plan / run / verify / table` 執行，輸出自動落在 `experiments/<id>/runs/`（見 `docs/experiment-data-management.md`）。

## 4. 正典變更時

正典改動（例如新旗標烘入）時：

1. 先更新 Canon 內所有受影響的範本；
2. 重新檢查第 2 節的不變式；
3. 更新 `provenance.json` 與本 README 第 2 節；
3b. 執行 `exp.py canon-bump "<改了什麼>"`：`VERSION.json` 版本 +1，並自動把設定與新正典不符的既有資料標為 `superseded`；
4. 已經複製出去的實驗檔**不會自動更新**，重跑前要重新從範本複製。
