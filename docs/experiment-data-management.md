# 實驗資料管控守則

建立：2026-09-15（同日改版：輸出集中到實驗資料夾、可重複跑批、正典版本與自動失效）。工具：`scripts/exp.py`（執行時加 `PYTHONIOENCODING=utf-8`）。

## 目的

任何一場模擬，事後都要能回答下面六個問題，而且**不依賴目錄名稱或記憶**：

1. 跑的是哪個模型、哪些旗標開或關？
2. 用了哪個 layout、場景設定、SKU 產生器？
3. 用的是哪一份程式（DLL）？
4. 結果如何？
5. 這場模擬乾不乾淨：有沒有正常結束、時長對不對、跟對照組是不是單變數？
6. 這筆資料在**目前的正典**下還有效嗎？若無效，差在哪裡？

## 這份守則要防止的真實事故（2026-09-11～15）

| 事故 | 後果 |
|---|---|
| 輸出目錄只存名稱，不存設定檔 | 9/11 的定價實驗只能從 git 反推用了哪個 xconf |
| 一次性跑批腳本散落在 `scripts/`，輸出散落在 `out/` | `su_dyn`、`tfA_*` 找不到當初的指令 |
| 目錄名稱失準（`4h`／`gate_8h`／`m1g`） | 只能用 KPI 反算時長、用 `footprint.csv` 第 11 欄辨識控制器 |
| 正典變更後舊資料失效沒有標記 | 只能靠記憶文件記哪些資料作廢 |
| 除錯、中途終止的場次與正式資料混在 `out/` | 必須人工排除 |
| 批次進行中重建 DLL 的風險 | 無法證明同一實驗的各組跑的是同一份程式 |

---

## 目錄結構

```
Material/Instances/Canon/            正典範本（唯一真相來源）
  VERSION.json                       正典版本號＋指紋＋變更歷史
  small/ large/                      xlayo / xsett / xconf 範本
experiments/
  registry.csv                       全域登記簿（每場一列，含 kind）
  fast/ dev/ formal/                 三類實驗（見 R0）
    <YYYY-MM-DD>_<slug>/
      experiment.json                實驗宣告（kind、question、motivation、arms、comparisons）
      NOTES.md                       輕量分析檔（見 R6b）
      inputs/                        本實驗改過的設定檔（從 Canon 複製、只改宣告的變數）
      runs/<組別>_s<seed>/           引擎輸出＋config/ 快照＋manifest.json＋engine.log
      launch/                        自動產生的 worker 腳本（每次 run 覆寫）
      verify.json  results.csv
```

## R0. 三類實驗（2026-09-15 使用者定案）

| 類別 | 用途 | seeds | plan 強制檢查 |
|---|---|---|---|
| `fast` | 快速測試、快速驗證、守門員、煙霧測試 | 1 | 每組 1 seed |
| `dev` | 迭代開發：新增元件的測試 | 1（看趨勢）或 5（看統計） | 每組 1 或 5，各組相同 |
| `formal` | 正式實驗：實驗組＋對照組完整比較，進論文 | 10 | 每組 10；必須宣告 comparisons，每組都在某個比較中，每個比較寫明唯一差異變數 |

- 類別由 `exp.py new <kind> <id> "<問題>"` 決定，資料夾放在 `experiments/<kind>/`，`experiment.json` 的 `kind` 必須與上層資料夾一致。
- `dev` 從 1 seed 升到 5 seeds 時，**開一個新的實驗 id**（例如 `..._5s`），不在原實驗上加 seed，讓每批數據各有一份 NOTES。
- 進論文或投影片的數字只能引用 `formal`；`dev` 的 5 seeds 結果可以用來做決定，但要註明是開發階段數據。

**`out/` 不再放新資料。** 2026-09-15 以前的 `out/*` 屬於舊時代，依 `docs/experiments/2026-09-15-data-check-and-cautions.md` 判定。

## 流程

```
exp.py new <kind> <id> "<問題>"   建資料夾骨架（kind＝fast / dev / formal）
（編輯 experiment.json：寫 motivation、arms、comparisons；從 Canon 複製檔案到 inputs/ 並改一個變數）
exp.py plan <id>                  檢查＋印場景行 → 給使用者確認 → 設 scenario_confirmed_by_user: true
exp.py run <id> [--workers N] [--max-sims M] [--resume]
exp.py wait <id>                  每完成一場印一行（給 Monitor 用）
exp.py verify <id>                乾淨度檢查 → valid / invalid，並自動做一次 stale-check
exp.py table <id>                 12 欄表＋場景行＋配對檢定 → results.csv，並產生／更新 NOTES.md
（Claude 填寫 NOTES.md 的「分析」段，再回報使用者）
exp.py status                     每個實驗顯示 kind 與 NOTES 狀態（no-notes / analysis-pending / analysed）
```

---

## 規則

### R1. 一個實驗＝一個資料夾＋一份 `experiment.json`

必填：`id`（＝資料夾名）、`kind`（＝上層資料夾）、`question`（要回答什麼）、`motivation`（為什麼現在做、結果會影響什麼決定）、`arms`（`label`、`xlayo`、`xsett`、`xconf`、`seeds`；非 Canon 檔要寫 `templates` 與 `changed`）、`comparisons`、`scenario_confirmed_by_user`。
路徑以 `inputs/` 開頭的相對於實驗資料夾，其餘相對於 `Material/Instances/`。

### R2. 啟動前檢查（`plan`），不通過不准跑

1. 所有檔案存在，檔名與內部名稱一致。
2. 非 Canon 檔與其 Canon 範本相比，只差名稱行與 `changed` 宣告的標籤行。
3. 同一比較中兩組場景一致（bots 可不同），seed 清單相同。
4. **把 `plan` 輸出給使用者確認**，確認後才設 `scenario_confirmed_by_user: true`。

### R3. 只能用 `exp.py run` 啟動

- 正典檔案若被改過卻沒有 `canon-bump`，拒絕啟動。
- 每場把三個設定檔複製到 `runs/<組別>_s<seed>/config/`，引擎直接讀快照。
- `manifest.json` 記錄：三檔 SHA-256 與來源／範本、xconf 全部旗標、場景、**正典版本與指紋**、DLL SHA-256 與建置時間、開始時的 DLL SHA-256、git HEAD、未提交程式碼 diff 雜湊、完整指令、排隊／開始／結束時間、exit、第幾次嘗試。
- 全域節流：worker 在啟動每一場前，等到系統上的 `RAWSimO.CLI.exe` 少於 `--max-sims`（預設 6），所以**多個實驗同時排隊也不會超載**。
- 已登記的實驗再跑會被拒絕；要補跑失敗或中斷的場次用 `--resume`（只清掉那些場次的引擎輸出，成功的不動）。
- 批次進行中**不准重建 Release**（manifest 會抓到，verify 判 invalid）。

### R4. 結束後驗證（`verify`）

| 檢查 | 方法 |
|---|---|
| 正常結束 | exit 0，且 `statistics.txt` 存在 |
| 時長正確 | `StatOverallOrdersHandled / StatThroughputOrdersPerHour` ＝ xsett 小時數 |
| 控制器一致 | 同組各 seed 的 `footprint.csv` 第 11 欄相同 |
| 場景正確 | footprint 的 bots／pods／站數／SKU 與 layout、xsett 一致 |
| 誠實命名 | 引擎輸出目錄名讀得出三個快照檔名 |
| 設定未被動過 | `config/` 快照 SHA-256 與啟動時一致 |
| 同一份程式 | 排隊與開始時 DLL 相同；同實驗所有場次 DLL 相同 |

任一不成立 → `invalid` 並寫明原因。

### R5. 正典版本與自動失效

- **每次把改動烘入正典**：先更新 `Canon/` 範本，再執行 `exp.py canon-bump "<改了什麼>"`。版本號 +1，並自動執行 `stale-check`。
- `stale-check` 對每一場 valid／unverified 的資料，把它的 **config 快照** 與 **目前的 Canon 範本** 逐行比對：
  - 忽略名稱行；
  - 實驗臂另外忽略它宣告的 `changed` 標籤（例如 N=2 臂的 `MaxPartsPerOrder`）；
  - 其餘任何一行不同 → 標為 `superseded`，note 寫明「superseded by Canon vN：差異行」。
- 判準是**設定內容**而不是日期或版本號：正典改的旗標若與某實驗無關（例如只改 large 範本），small 的資料不會被誤殺。
- ⚠️ 限制：只看設定檔。**只改程式碼、不改任何旗標**的正典變更（例如修 bug）偵測不到——這種情況要手動 `exp.py supersede <id> "<原因>"`，並把 bug 修正寫進 `canon-bump` 的說明。

### R6. 狀態只能往後推進

| validity | 意義 |
|---|---|
| `unverified` | 跑完未驗證 |
| `valid` | 驗證通過，而且與目前正典一致 |
| `superseded` | 驗證通過，但正典已變更（note 寫差異） |
| `invalid` | 驗證不通過、中途終止或除錯用 |

| status | 意義 |
|---|---|
| `queued` / `running` / `finished` / `failed` | 執行狀態 |
| `pruned` | 引擎輸出已刪，只留 `manifest.json` 與 `config/` |

- `exp.py prune <id> --yes`：刪除該實驗中 `superseded`／`invalid` 場次的引擎輸出，**保留 manifest 與設定快照**，所以永遠查得到「當時跑了什麼、為什麼作廢」。
- prune 只在使用者要求清理時執行；預設標記不刪。

### R6b. 每批數據一份 NOTES.md（輕量分析檔）

`table` 自動產生 `NOTES.md`，內容：

| 段落 | 來源 |
|---|---|
| 類別、產生時間、正典版本、DLL、git、場次有效性 | 自動（manifest、登記簿） |
| 為何做這組實驗：問題＋動機 | 自動（experiment.json） |
| 設定：每組 xconf、控制器、seeds、場景行、相對 Canon 範本的差異行、比較宣告 | 自動（從檔案讀，不憑記憶） |
| 結果：12 欄表與配對檢定 | 自動（results.csv） |
| **分析（Claude 當下判讀）** | **手寫**：數據說明了什麼、機制解釋、可信度（1 seed 只看方向）、下一步建議 |

- 重跑 `table` 時自動段落會更新，**分析段保留不覆寫**；數據變了就要重看分析是否仍成立。
- 一批實驗跑完，**先寫好分析段再回報使用者**；`status` 顯示 `analysis-pending` 表示還沒寫。
- 分析要寫當下的判讀與不確定性，不要寫成定論；之後被推翻就在分析段追加一行「YYYY-MM-DD 更正：…」，不刪原文。

### R7. 結果產出與引用

- `table` 只接受 `valid`（或 `superseded`，會在表頭加警告）。
- 報告、HTML、投影片、論文引用數字前，先 `exp.py status <id>` 確認是 `valid`；引用 `superseded` 必須註明正典版本與差異。

### R8. 例外

- **守門員／煙霧測試**：也走 exp.py，id 加 `_guard` 或 `_smoke`。
- **除錯埋點**：建置到另一個資料夾，輸出放 `out/_debug/`，不登記、不引用。

---

## 自我測試紀錄（2026-09-15）

- `plan`：擋下不誠實名稱、宣告外的變數改動、混場景比較、seed 不一致；未確認場景時 `run` 拒絕。
- `stale-check`：合成兩場資料（N=2 臂），一場與 Canon v1 一致 → 保持 valid；另一場 `LineBoundTau=false`（模擬烘入 τ 以前的資料）→ 標為 superseded，note 列出差異行。宣告的 `MaxPartsPerOrder` 未被誤判。
- 未 `canon-bump` 就改 Canon 檔 → `stale-check`／`run` 拒絕執行。
- `prune`：刪除引擎輸出，保留 `config/` 與 `manifest.json`。
- `run`／`wait`／`verify`／`table` 的端到端測試待使用者確認守門員場景（`2026-09-15_canon_guard_m5`）。
