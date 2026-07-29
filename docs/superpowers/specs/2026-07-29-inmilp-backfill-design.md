# In-MILP Backfill：讓 M3G 保留將走的 pod 服務 backlog（保住 exact 可解釋性）

2026-07-29 · branch `setlevel-redesign` · 作者：Aesop + AI 助理

## 1. 動機與證據

M3G+line30 對 HGS-M3（5-seed o100）：贏 PO/IPO/EOR，但 **TP 小輸 1.5%、飢餓 346 vs 181**。拆因（見 [[project-m3g]]、本輪診斷）：
- **非站間不平衡**：兩 output station 幾乎全等（Transfers 1418≈1422、Idle 227≈181）。
- **非資源不足**：bot 90% 閒、pod **排隊 22615s**（大量提早到站）。
- **是時序錯配**：pod 到站流不均——一陣擠著到（排隊 22615s）、一陣斷檔（飢餓 346s）。**過度供給 + 時機錯位**，不是量的問題。

**已排除的死路（勿重試）**：force-feed 硬約束（壓垮 pile-on、與 cap 病態互斥）、w_pipe/w_starve 調權重（inert）、拿掉軟地板（更差）。飢餓是 exact-online 結構問題，加權重/硬約束救不了。

**backfill 的對症性**：斷檔那一刻「剛好沒 pod 到」，而系統裡 pod 多到排隊——最省的解不是派新車，是**把正要走、身上還有殘餘料的 pod 留在站上服務 backlog**（填時序缺口、零新行程/能耗）。probe 實測：三方 pod 離站都留 ~4 張單可用殘餘、67-79% 有殘餘；[[project-backfill-probe-validation]]：gap 時 pod 97% 在站、75% 可回填。

## 2. 可解釋性紅線（本設計的最高約束）

**只做 in-MILP backfill，絕不做 bolt-on 啟發式**。理由：bolt-on（在 bot 釋放點用啟發式留 pod）會讓 M3G 變成「MILP + 貪婪 override」，模糊「exact vs greedy」對照、且推翻「exact 贏效率／greedy 贏線上」的乾淨敘事。**保留的決策必須由 MILP 的 exact 優化做出**——M3G 仍是「最小化那個目標式」，只是多了一個「可保留處理中 pod」的合法選項。

## 3. 缺口定位（spec 第一步驗證）

已確認：處理中 pod **在 Pb 候選集、有 q 變數、admission 收其可服務的 backlog 單、留住零新行程成本、line-closure 給關 line 的 c-reward**。所以 MILP **結構上與誘因上都『該』backfill**。它沒做，高機率是**時機**：

> pod 的最後一張單完成 → 觸發決策；但完成的同一個 tick，bot 的 task 清空 → `BotNormal.cs:2913 DequeueState` **已把 pod 釋放**（bot 帶它離站）→ 下一次 MILP 重解時 pod 已不在 Pb → 沒得留。

**Task 0（驗證）**：在 decode 加一行 log，記「決策當下,快照 Pb 是否含『上個 tick 剛完成最後一單、本可保留的 pod』」。確認是「不在快照」（時機）而非「在但沒選」（誘因）。若是後者,設計退化成只調目標式（小改）；若是前者（預期）,走下方 park-and-defer。

## 4. 設計：Park-and-Defer（MILP 決定保留）

核心：**當一個處理中 pod 的 task 即將清空、且該站有飢餓風險時,不立刻 `DequeueState` 釋放,而是「停泊(park)」它一個決策週期**,讓下一次 MILP 決策把它當候選,由 **MILP 決定**「指派 backlog 給它(保留)」或「放它走」。保留與否是 exact 優化的輸出。

### 4.1 掛鉤（`BotNormal.cs:2913`,gated `InMilpBackfill`）
task 清空、`DequeueState` 之前:
```
若 InMilpBackfill 且 該站 GetInfoStationEST() < BackfillHorizonSec(即將飢餓)
   且 該 pod 對 backlog 有可服務殘餘(≥1 條 line 可關)
   且 park 次數 < BackfillMaxParkCycles(防死鎖/佔槽):
      → 標記 pod 為 parked(維持 bot-pod 所有權、留在站台 Pb),不 DequeueState,
        觸發 OrderManager 重新決策(SituationInvestigated=false)。
   否則 → 照舊 DequeueState 釋放。
```

### 4.2 MILP 端(SplitM2eICManager,gated)
- parked pod 自然落在 Pb（bot-owned、在站）→ 已有 q 變數。**不需改快照結構**,只需確保 parked pod 被 GeneratePs/Pb 收錄(驗證)。
- **保留的正當性由既有目標式提供**:MILP 若把 backlog 的 q 指派給 parked pod,關 line(c-reward)/推進完成(z),而**距離成本=0**(pod 已在站)→ 正收益 → MILP 自然選擇保留。**不需要新的目標項**(這是保住可解釋性的關鍵:保留是既有目標式的最佳解,不是外加獎懲)。
- **釋放**:若 MILP 沒對 parked pod 指派任何新 q(留著沒用),decode 後把它 `DequeueState` 釋放(park 一週期未被 MILP 採用即放走)。

### 4.3 為什麼這保住可解釋性
M3G 目標式**一字未改**;只是「pod 何時可離站」多給 MILP 一次決策機會。保留是「MILP 在既有目標式下,發現用零成本的在站 pod 關更多 line 更划算」的**最佳解**。M3G 仍 = 「exact 最小化該目標式」。

## 5. 硬性防護（風險所在,必須全做）
- **park 上限 `BackfillMaxParkCycles`(預設 2)**:同一 pod 最多停泊 N 週期,逾期強制釋放 → 防「永不放走佔死站台/死鎖」。
- **槽位**:parked pod 佔著它原本的槽;若站台無空槽承接新 backlog,parked 無意義 → 條件加「有空槽」。
- **consolidation 相容**:parked pod 服務的新 backlog 單走既有 `Order.CreateSplitChild`/consolidation 事件鏈,不另造。split parent/child 會計不能因 park 亂掉。
- **gated zero-drift**:`InMilpBackfill=false` → 整塊跳過,`BotNormal` 照舊釋放、MILP 無變化,與現行 M3G 逐位一致。
- **公平**:若要與 HGS 對照,backfill 是**系統級 pod-retention 政策**,理論上該對兩者都開;但首階段先只在 M3G 驗證「能不能壓飢餓」,對照公平性留待有正向結果後處理。

## 6. 驗收
1. Task 0:decision-log 確認缺口=時機(pod 離站早於重解)。
2. zero-drift:flag off 逐位一致。
3. **飢餓下壓**:`InMilpBackfill=true` o100 2-seed,`StatStationStarvationTimeSec` 從 346 朝 HGS 的 181 逼近。
4. **TP 反超**:飢餓壓下後 TP ≥ HGS(295.8)——這是使用者的目標。
5. **pile-on/EOR 不崩**(park 用的是既有 pod,理應保住;若崩表示 park 誤留低效 pod)。
6. **無死鎖/無 park 爆量**:檢查 park 次數分布、站台不被停泊 pod 佔死。
7. o200 死鎖檢查。

## 7. 風險與緩解
- **park 佔槽 → 反而擋住新 pod → 飢餓更糟**(force-feed 的教訓):`BackfillMaxParkCycles` 小(2)、且只在「有空槽 + 站將餓」park。若飢餓不降反升,縮小 horizon/cycles 或回報。
- **時機仍不對**(park 一週期後 MILP 仍太晚):若 park 沒用,退而考慮「pod task 快空前就觸發」(更前瞻),但先驗 park。
- **decode 釋放邏輯漏 parked pod → pod 永久卡站**:park 上限 + 「未被採用即釋放」雙保險。
- **與 EarlyParentRelease / PipelineFloor 交互**:先各自 gated,park 開時觀察是否衝突。

## 8. 落地順序
Task 0(log 驗缺口)→ Task 1(config 三旗標 InMilpBackfill/BackfillHorizonSec/BackfillMaxParkCycles + gated BotNormal park 掛鉤)→ Task 2(decode 端 parked pod 收錄 + 未採用釋放)→ 驗收掃 horizon/cycles。
