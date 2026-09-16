# 實驗執行手冊（Experiment Handbook）

> **給接手的 agent**：這是本專案跑模擬的**完整操作手冊**。照著做就能正確跑實驗、驗證、出表、寫分析，不需要使用者提點。
> 開始任何實驗相關工作前，**從頭讀完第 0～4 節**；第 5 節以後是查閱用。
> 產出任何數據表、圖之前，另讀 **`docs/REPORTING-STANDARD.md`**（正典組態與旗標、完整指標字典、APA 7th 圖表強制規範）。
>
> 版本：2026-09-15（Canon v1）。若本手冊與程式碼、`Material/Instances/Canon/`、`scripts/exp.py` 的現況衝突，**以現況為準並更新本手冊**。

---

## 目錄

0. [三十秒版本](#0-三十秒版本)
1. [背景：這個專案在研究什麼](#1-背景這個專案在研究什麼)
2. [不可違反的硬規則](#2-不可違反的硬規則)
3. [三類實驗](#3-三類實驗)
4. [標準流程（逐步）](#4-標準流程逐步)
5. [experiment.json 寫法與範例](#5-experimentjson-寫法與範例)
6. [正典（Canon）範本庫](#6-正典canon範本庫)
7. [指標定義與數據表標準](#7-指標定義與數據表標準)
8. [NOTES.md：每批一份分析](#8-notesmd每批一份分析)
9. [正典改版與資料失效](#9-正典改版與資料失效)
10. [exp.py 指令參考](#10-exppy-指令參考)
11. [已知陷阱](#11-已知陷阱)
12. [故障排除](#12-故障排除)
13. [舊資料（2026-09-15 以前）](#13-舊資料2026-09-15-以前)
14. [相關文件索引](#14-相關文件索引)

---

## 0. 三十秒版本

```
1. 決定類別：fast（1 seed）/ dev（1 或 5 seeds）/ formal（10 seeds＋對照組）
2. exp.py new <kind> <YYYY-MM-DD>_<slug> "<問題>"
3. 從 Canon 複製要改的設定檔到 inputs/，只改一個變數，檔名＝內部名稱
4. 寫 experiment.json（motivation、arms、comparisons）
5. exp.py plan <id>  → 把場景行貼給使用者，等使用者確認  ← 絕不可跳過
6. 使用者確認後設 scenario_confirmed_by_user: true
7. exp.py run <id> --workers 6 --max-sims 6
8. exp.py wait <id>（用 Monitor 追蹤）
9. exp.py verify <id>
10. exp.py table <id>  → 產生 results.csv 與 NOTES.md
11. 填 NOTES.md 的「分析」段 → 用繁體中文回報使用者（12 欄表＋場景行＋分析）
```

所有指令在專案根目錄 `C:\Users\Aesop\Desktop\EE-RAWSim-O_PP` 執行，並加 `PYTHONIOENCODING=utf-8`：

```bash
export PYTHONIOENCODING=utf-8
python scripts/exp.py <command> ...
```

---

## 1. 背景：這個專案在研究什麼

### 1.1 論文

- 題目：「考慮訂單拆分之機器人移動履行系統線上聯合最佳化」。
- 平台：C# RAWSim-O 擴充版（RMFS 模擬器）。
- 核心問題：**拆單**（order splitting，一張訂單可分多次、多站完成）搭配**自我校準定價**的線上最佳化，能否提升 pile-on（一趟貨架到站服務多少件）、降低能耗，代價是什麼（訂單週期、在製品）。

### 1.2 模型家族（policy）

| policy | 設定檔 | 控制器類型 | 性質 |
|---|---|---|---|
| **M1G** | `m1g.xconf` | `M1GConfiguration` | Jiao 的 MILP 基準，不拆單、手調權重 |
| **M4G-NS** | `m4g_ns.xconf` | `M4GConfiguration`＋`OrderAtomicNoSplit`＋`OrderAtomicCanonPrices` | M4G 退化成整單承諾（不拆單對照組） |
| **M4G** | `m4g.xconf` | `M4GConfiguration` | 拆單 MILP＋Dinkelbach 自我校準定價（λ, μ, β） |
| **HGS-M5** | `hgs_m5.xconf`／`hgs_m5_k10.xconf` | `GreedyM5Configuration` | M4G 的貪婪近似，大規模用 |
| **HADGS** | `hadgs_aligned.xconf` | `HADGSConfiguration` | 大規模啟發式基準 |

比較關係：
- M1G → M4G-NS：**對標**（不同模型族，不是消融）。
- M4G-NS → M4G：**消融**，唯一差異＝拆單（承諾單位）。
- M4G → M5：**保真度**檢查（貪婪近似損失多少）。
- 大規模：M5 vs HADGS。

### 1.3 規模

| | 小規模 small | 大規模 large |
|---|---|---|
| 可跑的 policy | M1G、M4G-NS、M4G、M5 | **只有 HADGS 與 M5**（MILP 跑不動） |
| bots | 6 或 10 | 45 |
| 揀貨站／補貨站 | 2 / 2 | 12 / 6 |
| pods / cells | 100 / 120 | 1203 / 1352 |
| 貨架容量 | 100 | 500 |
| SKU | 100（Mu-100） | 1000（Mu-1000） |
| backlog（Fill 模式 OrderCount） | 100 | 200 |
| 時長／起始庫存 | 2 h / 70% | 2 h / 70% |

**時長 2 h 與庫存 70% 永遠固定。** 只允許變動：backlog 深度、bots 數、貨架容量、SKU 種類（以及實驗要測的模型旗標，例如 `MaxPartsPerOrder`）。

### 1.4 目前正典（Canon v2，2026-09-16）

- M4G、M4G-NS、M5 皆開：`NewPodFirstAllocation`、`LineBoundTau`、`M1GUrgentGate`。
- M5 另開 `DrawsFirstDispatch`（先取行後派車，提速且精度 n.s.）與 `PackingFullWholeOrderFallback`（Canon v2，2026-09-16：拆單預算耗盡時 HADGS 式整單組合派車；預算未觸頂時逐位不變）。
- 大規模 M5 另有 `CandidatePodTopK=10`。
- β（程式碼欄位名仍叫 Delta*）仍在 M4G 內。
- `MaxPartsPerOrder`、`PackingStationCount`、`PackingBufferCapacity` **不在正典**，只有實驗臂才開。

---

## 2. 不可違反的硬規則

違反任一條，資料作廢或使用者會中止工作。

| # | 規則 | 為什麼 |
|---|---|---|
| H1 | **未經使用者確認場景，絕不啟動任何模擬**（連 1 場守門員都一樣）。先 `plan`，貼出場景行，等回覆。 | 使用者曾因「未經允許擅自跑 8 個模擬」明確禁止；討論設計時跑模擬等於跳過討論。 |
| H2 | **不自己設計參數格點一次跑完**。要跑什麼先講。 | 同上。 |
| H3 | **所有模擬只能經 `exp.py run`**。不寫一次性 `run_*.cmd`，輸出不寫進 `out/`。 | 舊事故：腳本遺失、輸出散落、設定無法追溯。 |
| H4 | **目錄名稱不可信**。設定一律從檔案讀；控制器看 `footprint.csv` 第 11 欄；時長用 KPI 反算。 | 舊檔 `<Name>` 全部失準（`4h` 實為 2h、M4G 家族叫 `m1g`）。 |
| H5 | **新實驗從 `Material/Instances/Canon/` 複製**，一次只改一個變數，檔名＝內部 `<Name>`／`<NameLayout>`。**不改 Canon 內的檔案**（除非正典改版，見第 9 節）。 | 單變數對照是消融的前提。 |
| H6 | **批次進行中不准重建 Release**。要改程式就建置到其他資料夾（`/p:OutDir=...`）。 | 同一實驗必須同一份 DLL；verify 會抓並判 invalid。 |
| H7 | **Build 只用 x64 Release**：`MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`。 | Gurobi 只有 win64。 |
| H8 | **大規模只跑 HADGS / M5**。不跑大規模 M1G / M4G-NS / M4G。 | 跑不動。 |
| H9 | **不跑 M4G N=1、大規模 M5 N=1**。N=1 小規模直接用 M4G-NS、大規模直接用 HADGS。 | 使用者定案。 |
| H10 | **不修改 `M1GManager.cs` / `HADGSManager.cs`**。 | 基準必須乾淨。 |
| H11 | **M5 必須鏡像 M4G**：M4G 烘入正典的東西，M5 要有對應並同開。 | 保真度的前提。 |
| H12 | **NS 從 M4G 退化**（只差承諾單位），不從 M1G 鏡像；σ 不得出現在 NS。 | 消融語意。 |
| H13 | **回覆使用者一律繁體中文**（技術名詞保留原文）；終端數學式用 Unicode，不用 `$$`。 | 使用者偏好。 |
| H14 | **數據一律附場景行＋12 欄固定表**（第 7 節）。 | 使用者標準。 |
| H15 | **使用者沒說 seeds，預設 1 seed 探風向**（fast 或 dev）。 | 兩階段協定。 |
| H16 | **不 commit / push，除非使用者要求。** 不刪資料，除非使用者要求（`prune` 也要使用者同意）。 | |
| H17 | 死鎖、崩潰是工具或概念錯誤，**不是研究發現**。 | 使用者硬規。 |

---

## 3. 三類實驗

| 類別 | 用途 | seeds | `plan` 強制檢查 | 可引用範圍 |
|---|---|---|---|---|
| `fast` | 快速測試、快速驗證、守門員（guard）、煙霧測試 | **1** | 每組 1 seed | 只用於判斷「有沒有壞」「是否逐位相同」 |
| `dev` | 迭代開發：新增／修改元件的效果測試 | **1**（看趨勢）或 **5**（看統計） | 每組 1 或 5，且各組相同 | 做開發決策；引用時註明「開發階段數據」 |
| `formal` | 正式實驗：實驗組＋對照組完整比較 | **10** | 每組 10；必須有 comparisons；每組都在某個比較中；每個比較寫明唯一差異變數 | **論文、投影片只能引用 formal** |

選擇準則：

```
使用者問「這樣有沒有壞 / 是不是一樣」         → fast
使用者問「這個改動有沒有效 / 看看趨勢」       → dev，1 seed
1 seed 方向值得確認                            → 開新 id 的 dev，5 seeds（id 加 _5s）
使用者要「正式數據 / 論文用 / 10 seeds」       → formal
```

⚠️ 經驗：1 seed 的方向在 5～10 seeds 下消失過很多次（admission、allocation、gate、τ）。**1 seed 結果只能說「方向」，不能說「有效」。**

---

## 4. 標準流程（逐步）

### 步驟 1：建立實驗

```bash
python scripts/exp.py new dev 2026-09-16_m4g_n2_trend "限制拆單份數 N=2 對小規模 M4G 件數的方向？"
```

建立 `experiments/dev/2026-09-16_m4g_n2_trend/`，內含 `experiment.json` 骨架與 `inputs/`。

id 規則：`<YYYY-MM-DD>_<slug>`，slug 用小寫英數與底線，全域唯一。守門員加 `_guard`，煙霧測試加 `_smoke`，5 seeds 版加 `_5s`。

### 步驟 2：準備設定檔

- **不改的檔案**：直接在 arm 裡引用 `Canon/...`。
- **要改的檔案**：複製到 `inputs/`，改檔名與內部名稱，只改一個變數。

```bash
cp Material/Instances/Canon/small/m4g.xconf experiments/dev/2026-09-16_m4g_n2_trend/inputs/m4g_n2.xconf
# 編輯：<Name>m4g</Name> → <Name>m4g_n2</Name>，加入 <MaxPartsPerOrder>2</MaxPartsPerOrder>
```

命名規範（`docs/naming-standard.md`）：
- xconf：`<model>[_<variant>]`，例如 `m4g_n2`、`hgs_m5_k10_n3`。
- xsett：`<scale>_<mode><orders>_<sku>_<hours>h_inv<pct>`，例如 `small_fill200_mu100_2h_inv70`。
- xlayo：`<scale>_<bots>b_<pick>p_<repl>r[.<variant>]`，例如 `small_8b_2p_2r`、`small_6b_2p_2r.cap200`。

⚠️ 保留原檔的 CRLF 換行。用 Edit 工具改內容即可；不要用會轉換換行的方式整檔重寫。

### 步驟 3：寫 experiment.json

見第 5 節範例。必填 `kind`、`question`、`motivation`、`arms`、`comparisons`、`scenario_confirmed_by_user: false`。

### 步驟 4：plan，並請使用者確認場景

```bash
python scripts/exp.py plan 2026-09-16_m4g_n2_trend
```

`plan` 會檢查：
- 檔案存在、檔名＝內部名稱；
- 非 Canon 檔相對其 Canon 範本只差名稱行與 `changed` 宣告的標籤；
- 比較的兩組場景相同（bots 可不同）、seed 清單相同；
- 類別的 seed 數規則、formal 的比較宣告；
- 有 `question`、`motivation`。

有 `PROBLEMS` 就修到 `PLAN OK`。然後**用這個格式問使用者**（場景行直接貼 plan 的輸出，不要手打）：

```
實驗：2026-09-16_m4g_n2_trend（dev・1 seed）
問題：限制拆單份數 N=2 對小規模 M4G 件數的方向？
2 h · 1 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
- m4g_6b：Canon/small/m4g.xconf（M4GConfiguration）· 6 bots · seed 0
- m4g_n2_6b：inputs/m4g_n2.xconf（相對 m4g.xconf：+MaxPartsPerOrder=2）· 6 bots · seed 0
共 2 場，預估並行 2，約 X 分鐘。確認後啟動？
```

⚠️ **只貼場景行不夠**（使用者 2026-09-15 指正：「你又沒給我你的場景數據」）。確認請求必須附一張**從三個檔案實際讀出的參數表**：
- 每個參數寫明數值與來源檔（xlayo／xsett／xconf）；
- 模型旗標與 Canon 範本的差異行；
- 對照或參考資料是哪一場、設定是否相同。

**等使用者明確回覆「確認／跑」之後**，才把 `scenario_confirmed_by_user` 改成 `true`。使用者若改場景，改完重新 `plan` 再問一次。

### 步驟 5：啟動

```bash
python scripts/exp.py run 2026-09-16_m4g_n2_trend --workers 6 --max-sims 6
```

- `--workers`：這個實驗開幾個 worker（不超過場數）。
- `--max-sims`：全系統同時跑的 RAWSimO 上限。機器 6 實體核心，**6～7 最佳**；若有其他批次在跑，worker 會自動排隊等待。
- worker 以隱藏視窗脫離 harness 執行，Claude 對話中斷也不影響。
- 啟動前工具自動檢查：場景已確認、plan 通過、Canon 未被偷改、輸出未重複。

### 步驟 6：追蹤進度

用 Monitor 追蹤 `wait` 的輸出（每完成一場印一行，全部完成印 `ALL DONE`）：

```bash
python scripts/exp.py wait 2026-09-16_m4g_n2_trend --poll 60
```

被問進度時，給簡短狀態＋ETA，不貼原始 log。粗估耗時（單場、單執行緒）：

| 場景 | 約略時間 |
|---|---|
| 小規模 M1G / M4G-NS / M4G | 數分鐘～20 分鐘 |
| 小規模 M5 | 數分鐘 |
| 大規模 HADGS | 數十分鐘 |
| 大規模 M5（DrawsFirstDispatch） | 1～數小時；N=2 最慢 |

### 步驟 7：失敗或中斷時

```bash
python scripts/exp.py status 2026-09-16_m4g_n2_trend
# 看 experiments/<kind>/<id>/runs/<arm>_s<seed>/engine.log 找原因
python scripts/exp.py run 2026-09-16_m4g_n2_trend --resume
```

`--resume` 只清掉失敗或未完成場次的輸出並重跑，成功的場次不動。

### 步驟 8：驗證

```bash
python scripts/exp.py verify 2026-09-16_m4g_n2_trend
```

乾淨的條件（全部成立才是 `valid`）：

| 檢查 | 方法 |
|---|---|
| 正常結束 | exit 0，且有 `statistics.txt` |
| 時長正確 | `StatOverallOrdersHandled / StatThroughputOrdersPerHour` ＝ xsett 小時數 |
| 控制器一致 | 同組各 seed 的 `footprint.csv` 第 11 欄相同 |
| 場景正確 | footprint 的 bots、pods、站數、SKU 與設定檔一致 |
| 誠實命名 | 引擎輸出目錄名讀得出三個設定檔名 |
| 設定未被動過 | `config/` 快照的 SHA-256 與啟動時一致 |
| 同一份程式 | 排隊與開始時 DLL 相同；同實驗所有場次 DLL 相同 |

`verify` 完會自動做一次 `stale-check`。有 `invalid` 時，先查原因再決定是否 `--resume`，**不要直接用 invalid 的數據**。

### 步驟 9：出表

```bash
python scripts/exp.py table 2026-09-16_m4g_n2_trend
```

產生 `results.csv`（12 欄表＋場景行＋配對檢定）、`stats.xlsx`、`stats.json`、`apa_tables.docx` 與 `NOTES.md`。只接受 `valid`；`superseded` 會在表頭加警告。

**所有統計由 `scripts/stats_pipeline.py` 計算**：scipy 算精確 p 值，Excel 用原生公式獨立重算並自動比對，誤差超過 1e-9 就中止。**不得心算或手抄任何平均、百分比、p 值**；回報時直接引用 `results.csv`／`stats.json`，並確認 `excel_check` 是 `passed`（詳見 `docs/REPORTING-STANDARD.md` 第 5 節）。

### 步驟 10：寫分析、回報

1. 打開 `NOTES.md`，填「分析（Claude 當下判讀）」段（見第 8 節）。
2. `python scripts/exp.py status` 確認顯示 `analysed`。
3. 用繁體中文回報使用者，內容包括：
   - 場景行；
   - 12 欄表；
   - 配對結果；
   - 分析重點與機制解釋；
   - NOTES.md 路徑；
   - 下一步建議（例如「方向值得確認，建議開 5 seeds 的 dev」）。
   **不要自己接著跑下一批**，建議由使用者決定。

---

## 5. experiment.json 寫法與範例

### 5.1 欄位

| 欄位 | 必填 | 說明 |
|---|---|---|
| `id` | ✓ | ＝資料夾名 |
| `kind` | ✓ | `fast` / `dev` / `formal`，＝上層資料夾 |
| `question` | ✓ | 這批要回答什麼（一句話） |
| `motivation` | ✓ | 為什麼現在做、結果會影響哪個決定、對應教授或使用者的哪個問題 |
| `scenario_confirmed_by_user` | ✓ | 使用者確認前必須是 `false` |
| `arms[].label` | ✓ | 組名，`[A-Za-z0-9_.-]`，建議 `<policy>_<bots>b` |
| `arms[].xlayo/xsett/xconf` | ✓ | `Canon/...`（相對 `Material/Instances/`）或 `inputs/...`（相對實驗資料夾） |
| `arms[].seeds` | ✓ | seed 清單，例如 `[0]`、`[0,1,2,3,4]`、`[0..9]` 要寫全 |
| `arms[].templates` | 非 Canon 檔必填 | `{"xconf": "Canon/small/m4g.xconf"}` |
| `arms[].changed` | 有改變數時必填 | 改動的 XML 標籤名，例如 `["MaxPartsPerOrder"]` |
| `comparisons[]` | formal 必填 | `{"a": 對照組, "b": 實驗組, "kind": "ablation"/"benchmark"/"fidelity", "variable": "唯一差異"}`；delta ＝ b − a |

### 5.2 fast：守門員（是否逐位相同）

```json
{
 "id": "2026-09-15_canon_guard_m5",
 "kind": "fast",
 "question": "Canon 範本（誠實命名）跑 M5 小規模 seed 0，結果是否與舊檔名的資料逐位相同？",
 "motivation": "exp.py 建立後第一次實跑；確認 Canon 範本改名不改變模擬行為。",
 "scenario_confirmed_by_user": false,
 "arms": [
  {"label": "hgs_m5_6b",
   "xlayo": "Canon/small/small_6b_2p_2r.xlayo",
   "xsett": "Canon/small/small_fill100_mu100_2h_inv70.xsett",
   "xconf": "Canon/small/hgs_m5.xconf",
   "seeds": [0]}
 ],
 "comparisons": []
}
```

### 5.3 dev：新元件 1 seed 趨勢

```json
{
 "id": "2026-09-16_m4g_n2_trend",
 "kind": "dev",
 "question": "MaxPartsPerOrder=2 對小規模 M4G 件數與 StationIdle 的方向？",
 "motivation": "教授問拆單次數上限的曲線；先用 1 seed 看 N=2 是否崩產能，決定是否值得 10 seeds。",
 "scenario_confirmed_by_user": false,
 "arms": [
  {"label": "m4g_6b", "xlayo": "Canon/small/small_6b_2p_2r.xlayo",
   "xsett": "Canon/small/small_fill100_mu100_2h_inv70.xsett", "xconf": "Canon/small/m4g.xconf", "seeds": [0]},
  {"label": "m4g_n2_6b", "xlayo": "Canon/small/small_6b_2p_2r.xlayo",
   "xsett": "Canon/small/small_fill100_mu100_2h_inv70.xsett", "xconf": "inputs/m4g_n2.xconf",
   "templates": {"xconf": "Canon/small/m4g.xconf"}, "changed": ["MaxPartsPerOrder"], "seeds": [0]}
 ],
 "comparisons": [{"a": "m4g_6b", "b": "m4g_n2_6b", "kind": "ablation", "variable": "MaxPartsPerOrder=2"}]
}
```

### 5.4 formal：10 seeds，兩個機隊規模

```json
{
 "id": "2026-09-16_ns_vs_m4g_formal",
 "kind": "formal",
 "question": "在 Canon v1 下，拆單（M4G）相對不拆單（M4G-NS）的效果？",
 "motivation": "論文第五章主消融；Canon v1 烘入 gate/τ/new-pod-first 後需重建正式數據。",
 "scenario_confirmed_by_user": false,
 "arms": [
  {"label": "ns_6b",   "xlayo": "Canon/small/small_6b_2p_2r.xlayo",  "xsett": "Canon/small/small_fill100_mu100_2h_inv70.xsett", "xconf": "Canon/small/m4g_ns.xconf", "seeds": [0,1,2,3,4,5,6,7,8,9]},
  {"label": "m4g_6b",  "xlayo": "Canon/small/small_6b_2p_2r.xlayo",  "xsett": "Canon/small/small_fill100_mu100_2h_inv70.xsett", "xconf": "Canon/small/m4g.xconf",    "seeds": [0,1,2,3,4,5,6,7,8,9]},
  {"label": "ns_10b",  "xlayo": "Canon/small/small_10b_2p_2r.xlayo", "xsett": "Canon/small/small_fill100_mu100_2h_inv70.xsett", "xconf": "Canon/small/m4g_ns.xconf", "seeds": [0,1,2,3,4,5,6,7,8,9]},
  {"label": "m4g_10b", "xlayo": "Canon/small/small_10b_2p_2r.xlayo", "xsett": "Canon/small/small_fill100_mu100_2h_inv70.xsett", "xconf": "Canon/small/m4g.xconf",    "seeds": [0,1,2,3,4,5,6,7,8,9]}
 ],
 "comparisons": [
  {"a": "ns_6b",  "b": "m4g_6b",  "kind": "ablation", "variable": "OrderAtomicNoSplit+OrderAtomicCanonPrices（承諾單位）"},
  {"a": "ns_10b", "b": "m4g_10b", "kind": "ablation", "variable": "OrderAtomicNoSplit+OrderAtomicCanonPrices（承諾單位）"}
 ]
}
```

### 5.5 大規模 arm 範本

```json
{"label": "m5_45b", "xlayo": "Canon/large/large_45b_12p_6r.xlayo",
 "xsett": "Canon/large/large_fill200_mu1000_2h_inv70.xsett", "xconf": "Canon/large/hgs_m5_k10.xconf", "seeds": [0]}
{"label": "hadgs_45b", "xlayo": "Canon/large/large_45b_12p_6r.xlayo",
 "xsett": "Canon/large/large_fill200_mu1000_2h_inv70.xsett", "xconf": "Canon/large/hadgs_aligned.xconf", "seeds": [0]}
```

---

## 6. 正典（Canon）範本庫

位置 `Material/Instances/Canon/`：

```
Canon/
  README.md          範本說明、不變式
  provenance.json    每個範本的來源檔與 SHA-256
  VERSION.json       正典版本號、指紋、變更歷史
  small/  small_6b_2p_2r.xlayo  small_10b_2p_2r.xlayo  small_fill100_mu100_2h_inv70.xsett
          m1g.xconf  m4g.xconf  m4g_ns.xconf  hgs_m5.xconf
  large/  large_45b_12p_6r.xlayo  large_fill200_mu1000_2h_inv70.xsett
          hgs_m5_k10.xconf  hadgs_aligned.xconf
```

- 範本之間的不變式：
  - `m4g_ns.xconf` 對 `m4g.xconf` 只差名稱與兩行 order-atomic 旗標；
  - small 6b 與 10b 的 layout 只差 `BotCount` 與名稱。
- 大規模沒有 xinst，由 xlayo 產生實例。
- `Material/Instances/CoreBenchmark/` 只作歷史來源，**不直接跑新批次**。
- 名稱（`<Name>`）只影響輸出目錄標籤，不影響亂數或行為；seed 來自命令列／xsett。

---

## 7. 指標定義與數據表標準

> 完整指標字典（statistics.txt／kpi_report.csv 全部欄位）與 **APA 7th 表格／圖規範**見 `docs/REPORTING-STANDARD.md`。交給使用者的表與圖一律服從 APA 7th：三線表、英文表頭、表內無 emoji／中文／內部代號／判讀字，註解精簡英文放 `Note.`。

### 7.1 固定 12 欄（順序不可變）

```
policy, bots, Items, Lines, Orders, Pile-on, Trips, Trips/Orders, m/Line, EOR(kJ/order), TurnoverMedian(s), StationIdle(%)
```

| 欄 | 定義（來源） |
|---|---|
| Items | `StatOverallItemsHandled`（statistics.txt） |
| Lines | `StatOverallLinesHandled` |
| Orders | `StatOverallOrdersHandled` |
| Pile-on | kpi_report.csv `system_order_pile_on`（完成訂單 ÷ 到站趟次） |
| Trips | kpi_report.csv `output_station_arrivals` |
| Trips/Orders | Trips ÷ Orders |
| m/Line | `StatOverallDistanceTraveled` ÷ Lines |
| EOR(kJ/order) | `KPI_EOR`（機械能，不含 support energy） |
| TurnoverMedian(s) | `StatMedianTurnoverTime`（用中位數，分布右偏） |
| StationIdle(%) | 揀貨站 `IdleTime / UpTime` 的平均（stationstatistics.csv），**完整版本：含初始化與 pod 換 pod 間隔** |

- 不要用 `StatStationStarvationTimeSec` 取代 StationIdle（它排除第一張訂單完成前的時間）。
- 每件商品鎖站 10 s（ItemTransferTime），bot 等待 3 s（ItemPickTime）。

### 7.2 場景行（每份數據必附，從檔案讀）

```
2 h · 10 seeds · 2 Pstations · 2 Rstations · 100 SKUs · 100 backlog · 100 pods / 120 cells · cap 100 · 70% stock
```

`plan` 和 `table` 會自動產生，**直接貼，不手打**。

### 7.3 解讀口徑

- 拆單比較**看件數**，不看訂單量（拆單產生在製品，會系統性低估完成訂單）。
- 報效率一定一起報 TurnoverMedian：拆單的代價只在週期上顯形。
- 指標優先序：完成件數／訂單 > pile-on > EOR。
- 顯著性：`*` p<.05、`**` p<.01、`***` p<.001（配對 t，雙尾）；`identical` ＝ 每個 seed 差值皆為 0；1 seed 只顯示方向。
- M1G vs M4G-NS 是**對標**，不能寫成消融歸因。

---

## 8. NOTES.md：每批一份分析

`table` 自動產生 `experiments/<kind>/<id>/NOTES.md`：

| 段落 | 來源 |
|---|---|
| 類別、時間、正典版本、DLL、git、場次有效性 | 自動 |
| 為何做這組實驗（問題、動機） | 自動（experiment.json） |
| 設定（每組 xconf、控制器、seeds、場景行、相對 Canon 的差異行、比較宣告） | 自動 |
| 結果（results.csv） | 自動 |
| **分析（Claude 當下判讀）** | **手寫，重跑 table 不覆寫** |

### 分析段怎麼寫

以 5～15 行為原則，寫這些事：

1. **一句話結論**：回答 `question`。
2. **數字依據**：引用 2～4 個關鍵差異，附顯著性。
3. **機制解釋**：為什麼會這樣（使用者重視因果故事，不只數字）。
4. **可信度**：seed 數、有無 invalid／superseded、是否有 confound。
5. **下一步**：建議做什麼（例如升 5 seeds、改做 formal、或結案），但不要自行啟動。

範例：

```markdown
## 分析（Claude 當下判讀）

- 結論：N=2 在 6 bots 下明顯壓低件數（1 seed，只看方向）。
- 件數 −11.3%、StationIdle 從 8% 升到 19%；pile-on 幾乎不變。
- 機制：份數上限讓尚未湊齊的訂單卡住槽位，站台等不到可服務的貨架（站台挨餓），不是派車變差。
- 可信度：1 seed；兩場皆 valid，Canon v1。
- 下一步：方向清楚，建議直接做 formal 10 seeds 的 N=2/3/4 掃描。
```

之後被推翻時，**追加**一行「YYYY-MM-DD 更正：…」，不刪原文。

---

## 9. 正典改版與資料失效

### 9.1 什麼時候算正典改版

使用者說「轟入正典」「烘入正典」「納入正典」「改成預設」時。

### 9.2 程序

1. 改程式碼（新旗標預設 false），build x64 Release（**確認沒有批次在跑**）。
2. 跑守門員（`fast`）確認旗標關閉時逐位不變、開啟時行為符合預期。守門員也要先經使用者確認場景。
3. 更新 `Material/Instances/Canon/` 內**所有受影響的範本**：
   - M4G 改了，M4G-NS、M5 也要同步，見 H11、H12；
   - 重新檢查第 6 節的不變式；
   - 更新 `provenance.json` 與 `Canon/README.md`。
4. 執行：
   ```bash
   python scripts/exp.py canon-bump "Canon v2：<改了什麼、為什麼>"
   ```
   - `VERSION.json` 版本 +1；
   - 自動執行 `stale-check`：把每場 valid 或 unverified 資料的 **config 快照** 與 **新 Canon** 逐行比對；
   - 忽略名稱行與該 arm 宣告的 `changed` 標籤，其餘任何一行不同就標為 `superseded`，note 寫出差異行。
5. 回報使用者：哪些實驗被標失效（`exp.py status`），建議要重跑哪些 formal。
6. 同步更新本手冊第 1.4 節與記憶檔。

### 9.3 限制

- **只改程式碼、不改任何旗標的正典變更**（例如修 bug 改變了既有行為）偵測不到。這時手動標記：
  ```bash
  python scripts/exp.py supersede <id> "Canon v2 修正 XXX bug，行為改變"
  ```
- 未執行 `canon-bump` 就改 Canon 檔，`run` 與 `stale-check` 會拒絕執行（指紋不符）。

### 9.4 清理失效資料

只在使用者要求時執行：

```bash
python scripts/exp.py prune <id> --yes
```

刪除 superseded 或 invalid 場次的引擎輸出，**保留** `manifest.json` 與 `config/`，所以永遠查得到當時跑了什麼、為何作廢。

---

## 10. exp.py 指令參考

| 指令 | 作用 |
|---|---|
| `new <kind> <id> "<問題>"` | 建立 `experiments/<kind>/<id>/` 骨架 |
| `plan <id>` | 全部靜態檢查＋印場景行；有問題 exit 1 |
| `run <id> [--workers N] [--max-sims M] [--resume]` | 快照設定、寫 manifest、登記、啟動節流 worker |
| `wait <id> [--poll S]` | 阻塞直到全部完成，每完成一場印一行 |
| `verify <id>` | 乾淨度檢查 → valid / invalid → 自動 stale-check |
| `table <id>` | 12 欄表＋場景行＋配對檢定 → `results.csv`＋`NOTES.md` |
| `status [id]` | 每個實驗的 kind、NOTES 狀態、status/validity 計數 |
| `canon-bump "<說明>"` | 正典版本 +1，並 stale-check |
| `stale-check` | 設定與現行 Canon 不符者標 superseded |
| `supersede <id> "<原因>"` | 手動整批標 superseded |
| `prune <id> --yes` | 刪失效場次的引擎輸出，留 manifest＋config |
| `start` / `finalize` | worker 內部使用，不要手動呼叫 |

### 每場輸出內容 `runs/<arm>_s<seed>/`

```
config/                  三個設定檔快照（引擎實際讀取的就是這份）
manifest.json            kind、問題、動機、設定 SHA、全部旗標、場景、正典版本、DLL SHA 與建置時間、
                         git HEAD、未提交程式碼 diff 雜湊、完整指令、排隊/開始/結束時間、exit、第幾次嘗試
engine.log               引擎標準輸出（失敗時看這個）
<引擎產生的目錄>/        statistics.txt、footprint.csv、kpi_report.csv、stationstatistics.csv …
```

### 登記簿 `experiments/registry.csv`

每場一列：run_id、experiment、kind、arm、seed、bots、三個設定路徑、xconf_sha、canon_version、dll_sha、git、時間、exit、hours、controller、status、validity、note。

- `status`：queued → running → finished / failed（→ pruned）
- `validity`：unverified → valid / invalid（→ superseded）

**引用任何數字前先查 `validity`。**

---

## 11. 已知陷阱

| 陷阱 | 正確做法 |
|---|---|
| 舊 xsett `<Name>` 寫 4h 實際是 2h；`gate_8h` 實為 2h | 時長用 KPI 反算；新實驗只用 Canon |
| M4G 家族舊 xconf 的 `<Name>` 都是 `m1g` | 控制器看 footprint 第 11 欄 |
| footprint 第 11 欄無法區分 M1G 與 HADGS（`<Name>` 空白時兩者都寫 `obMPy`） | 另看 xconf 的 `OrderBatchingConfig` 類型，以及輸出是否有 `m1g_decision_log.csv` |
| PowerShell 變數名不分大小寫（`$n`／`$N`、`$s`／`$S` 會互相覆蓋） | 生成檔案用 Python，或用不會撞名的變數名 |
| `[IO.File]` 不理會 PowerShell 的 `cd` | 用絕對路徑 |
| PowerShell 字串 `"$name:"` 解析錯誤 | 寫成 `${name}` |
| cmd 會邊跑邊讀 .bat | 執行中的 worker 腳本不要編輯 |
| Release DLL 在模擬中被鎖住 | 測試用 build 放 `/p:OutDir=bin_xxx\` |
| 重建 DLL 即使原始碼相同雜湊也不同（MVID） | 判斷程式是否相同看 git diff，不看 DLL 雜湊 |
| 舊 CoreBenchmark 的 `hgs_m5_k10.xconf` 曾經缺正典旗標 | 只用 Canon；有疑慮就 diff |
| 1 seed 看到的方向到 5～10 seeds 消失 | 1 seed 只說方向；結論要 dev 5 或 formal 10 |
| 大規模 N=2 在舊 M5 下幾乎跑不完 | 正典 M5 已開 DrawsFirstDispatch；N=2 仍是最慢的一組，排程時預留時間 |
| `DispatchPairTopK` 會改變行為（pile-on +7～8%） | 不是忠實近似，不要用在正式實驗 |
| `GreedyM5Configuration` 沒有 `DinkelbachTolerance` | 對應欄位是 `LambdaTolerance` |
| 機器 6 實體／12 邏輯核心 | `--max-sims` 設 6～7；更多反而變慢 |

---

## 12. 故障排除

| 症狀 | 處理 |
|---|---|
| `plan`：internal name != file name | 改 `<Name>`／`<NameLayout>` 與檔名一致 |
| `plan`：differs from template outside declared variables | 把改動的標籤加進 `changed`；若是意外改動，重新從 Canon 複製 |
| `plan`：mixes scenarios | 比較的兩組 xsett 或 layout 不同（bots 以外）；拆成不同比較或修正檔案 |
| `run`：scenario_confirmed_by_user is not true | 還沒經使用者確認，回到步驟 4 |
| `run`：run already registered | 用 `--resume`，或開新 id |
| `run`／`stale-check`：Canon files changed without canon-bump | 有人改了 Canon：若是正典改版就 `canon-bump`；若是誤改就 `git checkout` 還原 |
| `verify`：exit≠0 或沒有 statistics.txt | 看 `engine.log`；修正後 `run --resume` |
| `verify`：hours 不符 | xsett 的 `SimulationDuration` 錯了，或模擬中途被殺 |
| `verify`：footprint 與預期不符 | 設定檔不是你以為的那個，重新 `plan` 檢查 |
| `verify`：DLL changed between queue and start | 批次中重建了 Release（違反 H6）；受影響場次 `--resume` 重跑 |
| `table`：run not verified | 先 `verify` |
| `wait` 一直不結束 | `status <id>` 查是否有 worker 死掉（仍是 queued／running 但沒有 RAWSimO 程序）；用 `run --resume` |
| 終端中文亂碼 | 加 `PYTHONIOENCODING=utf-8` |

---

## 13. 舊資料（2026-09-15 以前）

- `out/` 內的資料沒有 manifest，**不在登記簿**。
- 能不能用，依 `docs/experiments/2026-09-15-data-check-and-cautions.md` 判定（D＝可用、R＝有前提、排除清單）。
- 盤點：`docs/experiments/2026-09-15-experiment-inventory.md`。
- `out/gc_*`（2026-09-15 以 Canon v1 設定重跑的 140 場）是系統建立前啟動的，待匯入登記簿。匯入時歸為 formal，note 標 `retro`，並寫明依據（`scripts/run_gc_*.cmd`）。
- 舊的一次性腳本 `scripts/run_*.cmd` 只作紀錄，**不要再複製它們的寫法**。

---

## 14. 相關文件索引

| 文件 | 內容 |
|---|---|
| `docs/experiment-data-management.md` | 資料管控守則（R0～R8）的正式版 |
| `docs/naming-standard.md` | 檔案命名規範 |
| `Material/Instances/Canon/README.md` | 範本與不變式 |
| `docs/experiments/2026-09-15-data-check-and-cautions.md` | 舊資料判定、比較 C1～C12、教授問題 Q1～Q4 |
| `docs/superpowers/specs/2026-09-15-m5-draws-first-dispatch-design.md` | M5 提速設計 |
| `scripts/exp.py` | 工具本體（開頭 docstring 有 experiment.json 格式） |
| `scripts/standard_table.py` | 舊資料用的 12 欄表工具 |
| `CLAUDE.md` | 專案協作總則（開發流程、build、溝通風格） |
| 記憶 `MEMORY.md` | 研究發現與使用者偏好的索引 |
