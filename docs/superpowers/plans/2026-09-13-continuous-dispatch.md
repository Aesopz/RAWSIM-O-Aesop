# 實作計畫：消除 go-to-rest，讓派工連續化

2026-09-13 · 分支 `setlevel-redesign`

---

## 0. Global Constraints（先讀，不可違反）

```
建置      MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release
          （MSBuild 路徑：C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\）
語言      C# 7.3（net48 legacy csproj）；新檔案要手動加 <Compile Include> 到 csproj
禁改      M1GManager.cs、HADGSManager.cs  ← 專案憲法 §1.3，消融對照組的前提
旗標      所有新行為預設 OFF，正典必須逐位不變
驗證基準  out/su_dyn_s0..4（正典 M4G，6 bots，small_o100_mu100_2h_inv70）
```

---

## 1. 動機與 go/no-go 依據

```
正典 M4G（6 bots，5 seeds）
  rest 時間  13,001 s / 43,200 bot-s = 30.09%      ← 三分之一的機隊容量閒置
  idle 時間       0.3 s                 = 0.001%
  生產性利用率 0.6990   有效利用率 0.5596

保守版 RP（已實作，UseReturnPendingBots=true）
  rest 28.90%（−1.19 pp）  八項 KPI 全部 n.s.（最大 |t| = 2.71，門檻 2.776）
```

**保守版只碰到回程的最後 1 公尺，沒有碰到那 30%。** 完整版的目標是讓機器人在放下貨架的
瞬間接續下一個取貨任務，不經過 rest。

## 2. 設計（定案，不再 brainstorm）

**模型側不需要任何改動。** `GetBotReferenceWaypoint` 已回傳 `parkTask.StorageLocation`，
距離即 `d(儲位 → 新貨架)`。

🚨 **不要改成兩段式距離。** 第一段（現在位置 → 儲位）是**前一個任務已承諾的沉沒成本**，
不是新任務的邊際成本，加進目標式是重複計算 —— 與移除 ρ 的理由同構（見
`project_rho_removed`）。

**要改的是承諾的持久性。** 現況：

```
① 決策層  M4G 寫 ResourceManager.BottoPod + ClaimPod         ← 不打斷當前任務 ✅ 已是要的行為
② 分配層  BotManagerCore._taskQueues[bot]                     ← Dictionary<Bot,BotTask> 單格
          Enqueue* 的第一件事是 _taskQueues[bot].Cancel()      ← 舊承諾被取消 ❌
          RequestNewTask(bot)：信箱空才 GetNextTask            ← 機器人自己要任務 ✅
③ 執行層  BotNormal.AssignTask() → StateQueueClear()          ← 清空未完成動作 ❌
```

⚠️ **1 公尺門檻存在的真正理由**：`_taskQueues` 只有一格，若在回程早期登記，
中途任何 `Enqueue*` 都會 `Cancel()` 掉承諾，而 `BottoPod` 的登記還在 → 狀態不一致。

## 3. 回退策略（先建立，再動任何一行）

```
T0  git checkout -b continuous-dispatch      從 setlevel-redesign 開分支
T0  git tag pre-continuous-dispatch          標記回退點
```

**三層防護：**

1. **分支隔離** —— 失敗就 `git checkout setlevel-redesign`，主線零污染
2. **旗標預設 OFF** —— 即使合併，`ContinuousDispatch=false` 時所有程式碼路徑與現況相同
3. **逐位驗證** —— 每個 Task 完成後跑 `su_dyn_s0` 比對九項 KPI，任一項不同即回退該 Task

**放棄準則（任一成立即中止並回退）：**
- 逐位驗證失敗且 30 分鐘內找不到原因
- 出現貨架卡在機器人身上（`bot.Pod != null` 且無 ParkPodTask）的不變式破壞
- 5 seeds 跑出死結或停擺

---

## 4. Task 分解

### Task 1 — 建立回退點與旗標（不改行為）

```
建分支 + tag
M4GConfiguration 新增：
    public bool ContinuousDispatch = false;
    public double ReturnPendingDistanceThreshold 已存在，沿用
```
**驗收**：build 成功；`su_dyn_s0` 九項 KPI 逐位不變。

### Task 2 — 任務承諾不再被取消

```
BotManagerCore.cs
  _taskQueues[bot] 旁新增 _pendingTasks[bot]（Dictionary<Bot,BotTask>）
  新增 protected void EnqueueAfterCurrent(Bot bot, BotTask task)
      不 Cancel 當前任務，寫入 _pendingTasks[bot]
  RequestNewTask(bot)：信箱空時優先取 _pendingTasks[bot]，取不到才 GetNextTask(bot)
```
⚠️ 既有 `Enqueue*` 全部不動 —— 新路徑只在旗標開啟時被呼叫。

**驗收**：旗標關閉時 `su_dyn_s0` 逐位不變；新增一個單元測試驗證
「承諾後當前任務仍然完成，且完成後立即取得承諾的任務」。

### Task 3 — 放寬准入條件

```
M4GManager.CanUseReturnPendingBot
  ContinuousDispatch=true 時：只要 IsReturnPendingBot 即准入，不再檢查 IsNearReturnLocation
  ContinuousDispatch=false 時：維持現行的 1 公尺門檻
```
**驗收**：旗標關閉逐位不變；開啟後 `botsRa` 平均值應顯著上升（機制生效的證據）。

### Task 4 — 一致性守衛

```
ResourceManager
  承諾中的機器人同時關聯兩個貨架（手上的 + 要接的）
  逐一檢視 BottoPod / _usedPods / IsPodClaimed 的呼叫點，確認不會誤判可用性
```
🚨 **本計畫最高風險項。** 若無法在不改動其他 manager 的前提下保持一致，**中止並回退** ——
改動散落到其他 manager 會破壞既有模型的比較基準，代價遠大於效益。

### Task 5 — 驗收實驗

```
5 seeds：ContinuousDispatch=true  vs  正典
主要指標：rest 時間佔比（預期從 30.09% 下降）
次要指標：件數、公尺/行、pile-on、EOR、週期中位、過期訂單
```

**成功判準**：rest 佔比顯著下降，且件數與 EOR 不顯著惡化。

⚠️ **rest 下降本身不是成功** —— 若 rest 降了但 KPI 沒動，結論是「rest 不是瓶頸」，
那也是一個乾淨的負結果，照 `feedback_no_self_inflicted_findings` 誠實報告，不可包裝成貢獻。

---

## 5. 與口試論述的關係

無論成敗，這個實驗都回答教授的質疑：

```
成功  「我們消除了 go-to-rest，效果是 X」
失敗  「我們實作了它，rest 從 30% 降到 Y，但 KPI 無顯著變化
        ⟹ 在這個機隊規模下 rest 不是瓶頸，教授的疑慮不影響結論」
中止  「引擎的資源管理假設單一貨架，支援連續派工需要跨 manager 的改動，
        會改變所有既有模型的比較基準 ⟹ 屬於後續研究」
```

**三種結果都有話可說，這是做這個實驗的真正理由。**
