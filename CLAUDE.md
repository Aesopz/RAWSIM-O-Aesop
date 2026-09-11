# 專案憲法：EE-RAWSim-O_PP 協作規範

寫給接手這個專案的下一個 Claude（Sonnet/Opus）。這份文件記錄的不是程式碼本身能看出來的東西，而是**做法、判斷準則、和已經走過但放棄的死路**。程式碼、commit history、`.claude/…/memory/` 才是事實來源；這份文件是「怎麼工作」的操作手冊。

若與使用者當下的明確指示衝突，以使用者指示為準——這份文件規範的是「沒人特別交代時該怎麼做」。

---

## 0. 論文脈絡（一句話版）

RMFS 揀貨系統，研究「拆單（order splitting）」能否在 bot 稀缺、station 充裕的壓力情境下，透過提高 pile-on（單趟 pod 到站服務多張單/多件商品）來提升吞吐、降低能耗。核心貢獻是 **SplitM1G**：把 M1G 的 MILP 從「一張單只能整批指派給一個 station」放寬成 unit 級的 `q[o,i,s]` 變數，容許跨站/跨期拆單。細節見 `docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md`。

使用者是碩士生，本人（Aesop）是 C# 資深工程師等級的協作對象——不需要解釋基礎語法，但需要在數學模型/RMFS 領域知識上講清楚假設與權衡。使用者會主動用直覺（例如「應該要 Q TO POD TO STATION 吧？」）挑戰我的設計，這種挑戰通常是對的方向、值得認真對待，不要急著防禦，先確認他觀察到的現象再回應。

---

## 1. 工作方式的總原則

### 1.1 Brainstorm → Spec → Plan → Subagent-Driven Development

任何非瑣碎的新功能／新模型，走這個流程，不要跳過任何一步：

1. **Brainstorm**：用 AskUserQuestion 把設計決策一個一個問清楚（例：拆單容不容許跨期？per-order reward 還是 per-unit reward？）。不要自己假設答案然後直接寫 spec。
2. **Spec 文件**（`docs/superpowers/specs/YYYY-MM-DD-<slug>-design.md`）：把 brainstorm 的決策定案，寫出完整數學模型（集合/參數/變數/限制式/目標式）、架構決策（為什麼繼承 A 類別而不是 B）、風險。寫完自己重讀一次抓明顯錯誤（例：child ID 生成邏輯要跟既有 enabler 對齊,不要憑空發明）。
3. **實作計畫**（`docs/superpowers/plans/YYYY-MM-DD-<slug>.md`）：拆成多個 Task，**每個 Task 附完整逐字程式碼**，不要只寫「实现 XXX 功能」這種模糊描述——因為這個計畫是要餵給對這個對話沒有記憶的 subagent 執行的。計畫開頭寫死 Global Constraints（build 指令、語言版本限制、預設參數值、驗證基準在哪裡讀）。
4. **Subagent-Driven Development 執行**：每個 Task 派一個 implementer subagent（見 1.2），完成後派一個 task-reviewer subagent 審查，用 `.superpowers/sdd/progress.md` 當 ledger 記錄完成狀態與遺留的 minor findings，讓 session 重啟後能接著做而不用重問一遍。
5. 全部 Task 完成後，**再跑一次全分支的最終審查**（review 整個 diff range，不是逐 task 審），確認 READY TO MERGE。

### 1.2 Subagent 派遣準則

- **機械/抄寫型任務**（照 spec 逐字建立檔案、加 XML include、寫已經給好程式碼的單元測試）→ haiku。省成本，這類任務不需要判斷力。
- **需要判斷/整合型任務**（要決定怎麼跟既有程式碼掛勾、要處理邊界情況、要做架構取捨）→ sonnet。
- **最終整分支審查**這種一次性、高風險確認 → 用最高規格的 model（這次是 fable）並且明確要求「Yes/No READY TO MERGE」的二元結論，不要接受模糊的「大致可以」。
- Task brief 用腳本產生給 implementer；diff 用腳本產生給 reviewer；不要手動貼一大段 context 進 prompt——讓工具產生標準化、可重複的輸入。
- Reviewer 的 rubric 分 Critical/Important/Minor。Critical 一定要修完才能過；Minor 可以記錄下來留到最後統一處理，不必每次都卡關。

### 1.3 硬性約束（這個專案特有,不可違反)

- **絕對不要動 `M1GManager.cs` / `HADGSManager.cs` 這兩個原始檔**。任何新模型（SplitM1G 等）用繼承/鏡像（mirror）的方式做新檔案，即使造成程式碼重複也在所不惜——這是消融實驗（ablation）乾淨對照組的前提,使用者已經在多次對話中明確要求過。
  - 繼承鏈的訣竅：讓新 Configuration 繼承 `M1GConfiguration`（而不是直接繼承 `OrderBatchingConfiguration`），這樣引擎裡所有 `is M1GConfiguration` 的型別檢查會自動通過,不需要碰任何引擎檔案。前例：`SAM1GConfiguration : M1GConfiguration`。
  - base class 的私有方法/欄位鏡像時必須**忠實複製語意**，鏡像失真（infidelity）才是 reviewer 該抓的錯,鏡像本身是 spec 要求、不是壞味道。
- **Build 只能用 x64 Release**：`MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`。x86 會因為 Gurobi 只有 win64 版而 runtime 失敗。
- **C# 7.3**（net48 legacy csproj）：新檔案要手動加 `<Compile Include>` 到 csproj,不會自動抓。out 參數不能被 lambda 捕獲（CS1628）,遇到時用 `var copy = outParam;` 這種區域變數包一層繞過去。
- **xconf/xsett 檔案的「只改允許的部分」規則**：當某個新 config 檔案是拷貝既有檔案微調時（例如 `split_milp_m1.xconf` 之於 `m1g.xconf`），reviewer 會逐 byte 比對「非目標差異區段」是否一致，**包含換行符（CRLF vs LF）**。用 `cmp` + `grep -v` 排除允許差異行後比對,不要只靠肉眼看內容像不像。
- Layout 產生器有隱藏上限：`maxNrOfStationsWestOrEast() = NrHorizontalAisles/2`,`maxNrOfStationsNorthOrSouth() = NrVerticalAisles/2`。想加站點數之前先算這個上限,不要憑感覺塞數字進去被 validation 打槍。

---

## 2. 驗證紀律

- 改完程式碼**一定要重新 build 過**才算數,不能只憑讀 code 判斷正確。
- 改完之後跑 `git diff --stat` 確認**只有預期的檔案被改到**——尤其是「只改一個全局預設值」這種任務,很容易不小心動到不該動的地方。
- 遇到「新結果比 baseline 差」不要急著當成 bug。先查有沒有 confound（例如：per-unit reward 天生偏好「少張大單」而不是「多張小單」,造成 orders-handled 下降但 items-handled 持平——這不是拆單失效,是獎勵函數語意跟指標語意不對齊)。用其他獨立指標（items handled、pile-on）交叉驗證再下結論。
- 遇到「plan 裡寫的預期基準數字」和「重跑出來的數字」對不上,不要假設是自己實作錯——先去查 spec 1 / 更早的存檔 log 有沒有這個數字的原始出處。這次真實案例：plan 寫「608」,但獨立驗證發現是撰寫計畫時的抄寫錯誤（真值是 599/611）。
- 長跑批次（>10 分鐘）不要用會被 harness 隱性 timeout 打斷的前景 `run_in_background`。改用寫 `.cmd` 腳本 + `Start-Process -WindowStyle Hidden` 完全脫離 harness 行程樹,再用 `Monitor` tail log 拿非同步通知,不要用輪詢等待。

---

## 3. 溝通風格（這個使用者的偏好)

- **回覆要精簡**,不需要每次都交代「我準備做 X」的完整計畫——除非是有風險的動作（見下)。
- 長跑批次進行中被問「進度如何?」→ 給簡短狀態 + ETA,不要貼原始 log。
- 被問指標定義（turnover、pile-on、EOR…)→ 直接講清楚公式跟程式碼位置（file:line),不要展開整個 KPI 系統的教學。
- 使用者常常先做完一個小改動再說「先」（例如「先把 support energy 關掉」)——這個「先」暗示他心裡有後續動作,回覆完當前任務後**該問接下來想做什麼,不要自己假設要重跑實驗**。
- 使用者對「這個結果為什麼會這樣」有很強的因果好奇心,喜歡被给出機制解釋（不只是數字),尤其是「為什麼 A 比 B 差」這類反直覺結果——這時候不要只給數字,要給故事線（例如：M2 訂單數暫時低於 M1 是因為跨期 consolidation 的尾巴還沒轉正,不是產能問題,已被拉長時窗實驗證實)。
- 分支處理完後,预设选项是「保留在當前分支,不合併」——這個使用者傾向持續在同一分支上疊加多個 spec,不急著 merge master。

---

## 4. 專案現況地圖（2026-07-05 快照——之後請以 git log / memory 為準,這裡只是入口)

- **當前分支**：`6/24`。Spec 1（拆單 enabler：Child-Order 資料模型）與 Spec 2（SplitM1G MILP）皆已完成、通過整分支審查,尚未合併。
- **記憶體索引**：`C:\Users\Aesop\.claude\projects\...\memory\MEMORY.md`——每次接手前先讀,尤其 `project_order_splitting.md`（總覽)、`project_split_4o10b_result.md`（目前唯一的正向實證數據)、`feedback_test_case_standard.md`（測試案例規範)。
- **已放棄的研究方向**（不要重提)：CBS 演算法、RL、分散式控制、「PP 成本回饋進 MILP」框架、travel-time/wait-delay 近似估計 strand（2026-06-28 放棄)。
- **已驗證的實驗發現**（見 memory,不要重新論證)：
  - 2 站/20 bots（原 `small` layout）站台已飽和,拆單無發揮空間,三組 items handled 打平。
  - 壓力軸是 **bots/station 比**,不是 bot 絕對數量——`small_4o10b`（10 bots、4 pick station）才第一次量出正向效益。
  - Heuristic（非 MILP）拆單在 bot 稀缺情境下會崩潰（pile-on 暴跌、pod 行程暴增),這是「拆單需要聯合優化,不能裸拆」的論證素材,不是要修的 bug。
  - 時窗放大（2h→8h)使拆單增益從 +27% 放大到 +46%,同時 M0 baseline 會隨時間自我退化——這把論文敘事從「拆單提升穩態吞吐」升級成「拆單同時防止長期退化」,更強也更有新意的主張。
  - `KPI_EOR`/`StatOverallEnergyTotalJ` 一直只算機械能（E1-E5),支持能耗（`StatESupportJ`）分開算——關掉 support power 不會動到已經報告過的任何 EOR 數字。
- **待辦（使用者提過但尚未要求執行,不要主動做,除非被問)**：多 seed（5-10)重跑取統計檢定力;診斷 greedy pod-attribution 跟理論最優 pod 數的差距;分層/pod-aware 的 per-unit reward 模型。

---

## 5. 隱性知識雜項

- `InstanceStatistics.cs` 的 KPI Summary block（約 line 1648-1654)是內建指標的權威定義位置,遇到「這個指標怎麼算」先去那裡查,不要憑名字猜。
- `Order.CreateSplitChild` + demand ledger（`GetRemainingDemand`/`RemainingPositions`/`IsFullyClaimed`)是 Spec 1 留下的「拆單」資料層基礎設施,任何新的拆單策略（heuristic 或 MILP)都應該重用這層,不要重新發明订单拆分的資料結構。
- Consolidation（多個 child 完成後才算 parent 完成)的事件鏈：`OutputStation.RemoveAnyCompletedOrder` → `NotifyChildCompleted`（用 `_completedChildren` HashSet 做 idempotent 保護)→ parent `NotifyOrderCompleted`。這條鏈已經抽成 `SplitConsolidationLogger` 共用,新 manager 要記錄 splitorders.csv 就重用這個類別,不要各自複製一份 log 邏輯。
- M1（跨站、當期 all-or-nothing)與 M2（跨站、跨期允許部分滿足)不是同一個維度的兩端——M2 是 M1 能力的超集,不是「跨期版的 M1」。目前沒有「只跨期、當期仍限單站」這個中間變體,使用者已經確認過這個落差可以不補。

---

若這份文件與你觀察到的現狀（程式碼、git log、memory）衝突,以現狀為準,並更新這份文件——這是操作手冊,不是聖經。
