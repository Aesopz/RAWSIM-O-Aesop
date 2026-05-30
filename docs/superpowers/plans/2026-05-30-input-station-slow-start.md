# Input-station (Replenishment) Slow-start Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 slow-start 緩啟動（下半邊：hold-at-pod + JIT release）鏡像到 replenishment（store / `InsertTask`）任務上，減少 input-station 的 pod queue 能耗。

**Architecture:** 平行於 output 的 `StationReleaseScheduler`，新增 `InputStationReleaseScheduler`（per-input-station per-tick），重用既有純函式 `StationReleaseScheduler.Decide` 與 `SlowStartController.PipelineNextFreeTime`；新增最小化 `BotSlowStartHoldInput` 狀態（無 telemetry，零風險不碰 output 的 `BotSlowStartHold`）。RPS/ROA/RTA（補貨的 OB/PS/TA）全維持原生，不做上半邊 PS 優化（補貨是支援模塊，不在優化範圍）。

**Tech Stack:** C# / .NET、RAWSimO.Core、MSBuild x64 Release。

**Scope 界定（依使用者確認）：**
- ✅ 只做下半 slow-start（省能 hold/release）。
- ❌ 不做上半 net-supply / EST-feasible store-pod 選擇。
- ✅ 每個 input station 依當前 store 任務算簡化版 EST。
- ✅ 獨立旗標 `SlowStartInputEnabled`，與 output 的 `SlowStartEnabled` 正交。
- ✅ 平行建置，不修改 output 的 `BotSlowStartHold` / `StationReleaseScheduler`。

---

## File Structure

| 檔案 | 動作 | 責任 |
|---|---|---|
| `RAWSimO.Core/Configurations/SettingConfiguration.cs` | Modify | 新增 `SlowStartInputEnabled` 旗標 |
| `RAWSimO.Core/Bots/BotNormal.cs` | Modify | (a) 新增 `BotSlowStartHoldInput` 最小狀態；(b) `BotTaskType.Store` 分支注入 hold（旗標 gated） |
| `RAWSimO.Core/Control/InputStationReleaseScheduler.cs` | **Create** | per-input-station per-tick：收集 InsertTask holders、算 input EST、重用 `Decide`、寫 release deadline |
| `RAWSimO.Core/Control/PathManager.cs` | Modify | `Update` 中依 `SlowStartInputEnabled` 對每個 input station 呼叫排程器 |

重用（不改）：`StationReleaseScheduler.Decide`、`SlowStartController.PipelineNextFreeTime`、`SlowStartController.ComputeIdealEta`、`PathManager.EstimateIdealKinematicEta`、`bot._slowStartReleaseDeadline` 等既有欄位。

依賴順序：Task 1（flag）→ Task 2（hold state + 注入）→ Task 3（scheduler）→ Task 4（wiring）→ Task 5（建置 + 驗收）。各 task 因旗標預設 false，中途 commit 不改變預設行為。

---

## Task 1: 新增 `SlowStartInputEnabled` 旗標

**Files:**
- Modify: `RAWSimO.Core/Configurations/SettingConfiguration.cs:133`（緊接 `SlowStartEnabled` 之後）

- [ ] **Step 1: 新增旗標欄位**

在 `RAWSimO.Core/Configurations/SettingConfiguration.cs` 第 133 行
（`public bool SlowStartEnabled = false;`）之後插入：

```csharp
        /// <summary>
        /// Replenishment (input-station) slow-start. Holds the bot at the pod cell before a
        /// store/InsertTask travels to the input station, releasing per
        /// InputStationReleaseScheduler so the pod arrives JIT and reduces input-station pod
        /// queue energy. Lower-half only — replenishment pod selection stays native.
        /// Independent of SlowStartEnabled (output). See
        /// docs/superpowers/plans/2026-05-30-input-station-slow-start.md.
        /// </summary>
        public bool SlowStartInputEnabled = false;
```

- [ ] **Step 2: Commit**

```powershell
git add RAWSimO.Core/Configurations/SettingConfiguration.cs
git commit -m "Add SlowStartInputEnabled config flag (replenishment slow-start)"
```

---

## Task 2: `BotSlowStartHoldInput` 狀態 + Store 分支注入

**Files:**
- Modify: `RAWSimO.Core/Bots/BotNormal.cs`（新增狀態類別於 `#region SlowStartHold state` 的 `#endregion` 之後）
- Modify: `RAWSimO.Core/Bots/BotNormal.cs:707-721`（`BotTaskType.Store` 分支）

- [ ] **Step 1: 新增最小化 `BotSlowStartHoldInput` 狀態**

在 `RAWSimO.Core/Bots/BotNormal.cs` 的 `#region SlowStartHold state` 對應
`#endregion`（`BotSlowStartHold` 類別結束）之後，新增：

```csharp
        #region SlowStartHoldInput state

        /// <summary>
        /// Input-station (replenishment) slow-start hold. Mirrors BotSlowStartHold but for
        /// store/InsertTask, stripped of output-only telemetry. Keeps the bot stationary at the
        /// pod cell, reading the release deadline written each tick by InputStationReleaseScheduler,
        /// then releases. See docs/superpowers/plans/2026-05-30-input-station-slow-start.md.
        /// </summary>
        internal class BotSlowStartHoldInput : IBotState
        {
            private const double PROBE_INTERVAL = 1.0;
            private readonly InsertTask _task;
            private bool _initialized = false;
            private bool _holdFinished = false;
            private double _holdStartTime = 0.0;
            private Waypoint _claimedStorage = null;

            public BotSlowStartHoldInput(InsertTask task) { _task = task; }
            public Waypoint DestinationWaypoint
            {
                get { return _task != null && _task.ReservedPod != null ? _task.ReservedPod.Waypoint : null; }
            }

            public void Act(Bot self, double lastTime, double currentTime)
            {
                var bot = self as BotNormal;
                bot._lastExteriorState = Type;

                // First-tick setup: mark holding so the scheduler picks us up; re-claim pod cell.
                if (!_initialized)
                {
                    self.StatTotalStateCounts[Type]++;
                    _initialized = true;
                    _holdStartTime = currentTime;
                    bot._isSlowStartHolding = true;
                    bot._slowStartHoldStartTime = currentTime;

                    var podWp = (bot.CurrentWaypoint != null && bot.CurrentWaypoint.PodStorageLocation)
                                ? bot.CurrentWaypoint : null;
                    if (podWp != null)
                    {
                        try { bot.Instance.ResourceManager.ClaimStorageLocation(podWp); _claimedStorage = podWp; }
                        catch (System.InvalidOperationException) { _claimedStorage = null; }
                    }
                }

                // Read scheduler-written deadline; hold until then.
                if (!_holdFinished)
                {
                    double deadline = bot._slowStartReleaseDeadline;
                    bool release = !double.IsNaN(deadline) && currentTime >= deadline;
                    if (release)
                    {
                        _holdFinished = true;
                    }
                    else
                    {
                        bot.BlockedUntil = double.IsNaN(deadline) ? currentTime + PROBE_INTERVAL : deadline;
                        bot.WaitUntil(currentTime + PROBE_INTERVAL);
                    }
                }

                if (_holdFinished)
                {
                    bot._isSlowStartHolding = false;
                    bot._slowStartHoldStartTime = double.NaN;
                    bot._slowStartReleaseDeadline = double.NaN;
                    bot._slowStartIsChosen = false;
                    bot._slowStartEta = double.NaN;
                    bot._slowStartTStarve = double.NaN;
                    bot.RequestReoptimization = true;
                    bot.BlockedUntil = -1.0;
                    bot.WaitUntil(-1.0);

                    if (_claimedStorage != null)
                    {
                        try { bot.Instance.ResourceManager.ReleaseStorageLocation(_claimedStorage); }
                        catch (System.InvalidOperationException) { /* already released */ }
                        _claimedStorage = null;
                    }

                    bot.DequeueState(lastTime, currentTime);
                }
            }

            public override string ToString() { return "SlowStartHoldInput"; }
            public BotStateType Type { get { return BotStateType.SlowStartHold; } }
        }
        #endregion
```

> 重用 `BotStateType.SlowStartHold`（不新增 enum 值）。此類別為 `BotNormal` 巢狀類別，
> 與 `BotSlowStartHold` 同樣可存取 `bot._isSlowStartHolding` 等 internal 欄位。

- [ ] **Step 2: 在 Store 分支注入 hold（旗標 gated）**

把 `RAWSimO.Core/Bots/BotNormal.cs:707-721` 的：

```csharp
                    InsertTask storeTask = t as InsertTask;
                    if (storeTask.ReservedPod != Pod)
                    {
                        var podWaypoint = storeTask.ReservedPod.Waypoint;
                        _appendMoveStates(CurrentWaypoint, podWaypoint, tripLoaded: false);
                        StateQueueEnqueue(new BotPickupPod(storeTask.ReservedPod));
                        _appendMoveStates(podWaypoint, storeTask.InputStation.Waypoint, tripLoaded: true);
                    }
                    else
                    {
                        _appendMoveStates(CurrentWaypoint, storeTask.InputStation.Waypoint, tripLoaded: true);
                    }
                    StateQueueEnqueue(new BotGetItems(storeTask));
```

改為（鏡像 extract 的 slow-start 注入）：

```csharp
                    InsertTask storeTask = t as InsertTask;
                    bool slowStartInputEnabled = Instance.SettingConfig.SlowStartInputEnabled;
                    if (storeTask.ReservedPod != Pod)
                    {
                        var podWaypoint = storeTask.ReservedPod.Waypoint;
                        _appendMoveStates(CurrentWaypoint, podWaypoint, tripLoaded: false);
                        // Pre-lift hold at pod cell before fetching the pod to the input station.
                        if (slowStartInputEnabled)
                            StateQueueEnqueue(new BotSlowStartHoldInput(storeTask));
                        StateQueueEnqueue(new BotPickupPod(storeTask.ReservedPod));
                        _appendMoveStates(podWaypoint, storeTask.InputStation.Waypoint, tripLoaded: true);
                    }
                    else
                    {
                        // Bot already carrying the reserved pod — post-lift hold.
                        if (slowStartInputEnabled)
                            StateQueueEnqueue(new BotSlowStartHoldInput(storeTask));
                        _appendMoveStates(CurrentWaypoint, storeTask.InputStation.Waypoint, tripLoaded: true);
                    }
                    StateQueueEnqueue(new BotGetItems(storeTask));
```

- [ ] **Step 3: Commit**

```powershell
git add RAWSimO.Core/Bots/BotNormal.cs
git commit -m "Add BotSlowStartHoldInput state + inject into store task (flag-gated)"
```

---

## Task 3: `InputStationReleaseScheduler`

**Files:**
- Create: `RAWSimO.Core/Control/InputStationReleaseScheduler.cs`

- [ ] **Step 1: 建立排程器**

建立 `RAWSimO.Core/Control/InputStationReleaseScheduler.cs`：

```csharp
using RAWSimO.Core.Bots;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using System;
using System.Collections.Generic;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// Per-input-station slow-start release scheduler (replenishment lower-half only).
    /// Mirrors StationReleaseScheduler for store/InsertTask holders. Reuses the pure
    /// StationReleaseScheduler.Decide and SlowStartController.PipelineNextFreeTime. The input
    /// starvation model is the simplified single-server pipeline of committed store jobs plus
    /// the station's current bundle-transfer block; no demand matching (pod selection stays native).
    /// See docs/superpowers/plans/2026-05-30-input-station-slow-start.md.
    /// </summary>
    public static class InputStationReleaseScheduler
    {
        /// <summary>Per-tick entry for one input station. Collects InsertTask holders, computes
        /// input starvation, runs Decide, writes release deadline onto each holder bot.</summary>
        public static void Schedule(InputStation station, PathManager pathManager, double currentTime, double buffer)
        {
            if (station == null || pathManager == null) return;
            var instance = station.Instance;

            // Collect holders bound to THIS input station.
            var holderBots = new List<BotNormal>();
            foreach (var b in instance.Bots)
            {
                var bn = b as BotNormal;
                if (bn == null || !bn._isSlowStartHolding) continue;
                var task = bn.CurrentTask as InsertTask;
                if (task == null || task.InputStation != station) continue;
                holderBots.Add(bn);
            }
            if (holderBots.Count == 0) return;

            double starve = ComputeInputStarvation(station, pathManager, currentTime);

            var inputs = new List<StationReleaseScheduler.HolderInput>();
            foreach (var bn in holderBots)
            {
                var task = bn.CurrentTask as InsertTask;
                double eta = IdealEta(pathManager, bn, station, currentTime);
                double lift = (bn.Pod == null) ? bn.PodTransferTime : 0.0;
                int committed = (task.Requests != null) ? task.Requests.Count : 0;
                inputs.Add(new StationReleaseScheduler.HolderInput
                {
                    BotId = bn.ID,
                    Lift = lift,
                    Travel = eta,
                    Value = committed,                               // chosen-selection score (committed bundles)
                    Proc = committed * station.ItemBundleTransferTime // store work time
                });
            }

            var result = StationReleaseScheduler.Decide(starve, inputs, buffer);

            foreach (var bn in holderBots)
            {
                double delay = result.HoldDelayByBot.TryGetValue(bn.ID, out var d) ? d : 0.0;
                bn._slowStartReleaseDeadline = currentTime + delay;
                bn._slowStartIsChosen = (bn.ID == result.ChosenBotId);
            }
        }

        /// <summary>Ideal kinematic ETA bot->station with the same fallback chain as the output scheduler.</summary>
        private static double IdealEta(PathManager pm, BotNormal bn, InputStation station, double now)
        {
            double eta = pm.EstimateIdealKinematicEta(bn, bn.CurrentWaypoint, station.Waypoint, now, bn.GetTargetOrientation());
            if (double.IsNaN(eta) || double.IsInfinity(eta))
                eta = SlowStartController.ComputeIdealEta(bn, bn.CurrentWaypoint, station.Waypoint);
            if (double.IsNaN(eta) || double.IsInfinity(eta)) eta = 0.0;
            return eta;
        }

        /// <summary>
        /// Simplified input starvation: single-server pipeline of committed (non-holding) store
        /// jobs targeting this station, started from the station's current bundle-transfer block.
        /// Returns seconds-from-now until the station would run out of store work.
        /// </summary>
        private static double ComputeInputStarvation(InputStation station, PathManager pm, double now)
        {
            double blockedLeft = station.GetInfoBlockedLeft();
            double stationFreeAt = (!double.IsNaN(blockedLeft) && blockedLeft > 0.0) ? now + blockedLeft : now;

            var jobs = new List<(double arrival, double work)>();
            foreach (var b in station.Instance.Bots)
            {
                var bn = b as BotNormal;
                if (bn == null || bn._isSlowStartHolding) continue;   // exclude holders
                var task = bn.CurrentTask as InsertTask;
                if (task == null || task.InputStation != station) continue;
                int items = (task.Requests != null) ? task.Requests.Count : 0;
                if (items <= 0) continue;

                double arrival = (bn.CurrentWaypoint == station.Waypoint || bn.IsQueueing)
                    ? now
                    : now + Math.Max(0.0, IdealEta(pm, bn, station, now));
                jobs.Add((arrival, items * station.ItemBundleTransferTime));
            }

            double t0 = SlowStartController.PipelineNextFreeTime(stationFreeAt, jobs, now);
            return Math.Max(0.0, t0 - now);
        }
    }
}
```

- [ ] **Step 2: 加入 csproj（若逐檔列出）**

確認 `RAWSimO.Core/RAWSimO.Core.csproj` 若逐檔列 `.cs`，於
`Control\StationReleaseScheduler.cs` 的 `<Compile>` 附近新增：

```xml
    <Compile Include="Control\InputStationReleaseScheduler.cs" />
```
（萬用 glob 則跳過。）

- [ ] **Step 3: Commit**

```powershell
git add RAWSimO.Core/Control/InputStationReleaseScheduler.cs RAWSimO.Core/RAWSimO.Core.csproj
git commit -m "Add InputStationReleaseScheduler (reuses Decide + PipelineNextFreeTime)"
```

---

## Task 4: PathManager.Update 接線

**Files:**
- Modify: `RAWSimO.Core/Control/PathManager.cs:528-535`

- [ ] **Step 1: 在 output 排程後加入 input 排程（獨立旗標）**

把 `RAWSimO.Core/Control/PathManager.cs:528-535` 的：

```csharp
            // Centralized slow-start release scheduling (one decision per station per tick).
            if (Instance.SettingConfig != null && Instance.SettingConfig.SlowStartEnabled)
            {
                double buffer = Instance.SettingConfig.SlowStartEtaSafetyBuffer;
                if (buffer <= 0.0) buffer = 0;  // default; see SlowStartController history
                foreach (var os in Instance.OutputStations)
                    StationReleaseScheduler.Schedule(os, this, currentTime, buffer);
            }
```

改為（在其後追加 input 區塊，獨立 gated）：

```csharp
            // Centralized slow-start release scheduling (one decision per station per tick).
            if (Instance.SettingConfig != null && Instance.SettingConfig.SlowStartEnabled)
            {
                double buffer = Instance.SettingConfig.SlowStartEtaSafetyBuffer;
                if (buffer <= 0.0) buffer = 0;  // default; see SlowStartController history
                foreach (var os in Instance.OutputStations)
                    StationReleaseScheduler.Schedule(os, this, currentTime, buffer);
            }
            // Replenishment (input-station) slow-start — independent flag, lower-half only.
            if (Instance.SettingConfig != null && Instance.SettingConfig.SlowStartInputEnabled)
            {
                double bufferIn = Instance.SettingConfig.SlowStartEtaSafetyBuffer;
                if (bufferIn <= 0.0) bufferIn = 0;
                foreach (var ins in Instance.InputStations)
                    InputStationReleaseScheduler.Schedule(ins, this, currentTime, bufferIn);
            }
```

- [ ] **Step 2: 建置 x64 Release**

```powershell
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
& $msbuild RAWSimO.sln /p:Platform=x64 /p:Configuration=Release /m /v:minimal
```
Expected: `Build succeeded.`（0 errors）

- [ ] **Step 3: 回歸 — 既有 self-test 仍全綠**

```powershell
& ".\RAWSimO.Playground\bin\x64\Release\RAWSimO.Playground.exe" selftest
```
Expected: `ALL PASS`（純函式 `Decide`/`PipelineNextFreeTime` 未被改動，回歸應通過）。

- [ ] **Step 4: Commit**

```powershell
git add RAWSimO.Core/Control/PathManager.cs
git commit -m "Wire InputStationReleaseScheduler into PathManager (SlowStartInputEnabled-gated)"
```

---

## Task 5: 整合驗收（baseline 不變 + replenishment 省能方向性）

**Files:**
- Create: `Material/Instances/CoreBenchmark/large_test_ss_input_on.xsett`

- [ ] **Step 1: 建 treatment 設定檔**

複製 `Material/Instances/CoreBenchmark/large_test.xsett` 為
`Material/Instances/CoreBenchmark/large_test_ss_input_on.xsett`，在
`<SettingConfiguration>` 內加入：

```xml
  <SlowStartInputEnabled>true</SlowStartInputEnabled>
```

- [ ] **Step 2: 跑 baseline（旗標關）冒煙**

用 `large_test.xsett`（`SlowStartInputEnabled` 預設 false）跑短模擬。
Expected: 正常結束、無例外、KPI 與改動前一致（旗標關→ store 流程未注入 hold，原生路徑）。

- [ ] **Step 3: 跑 treatment（input 旗標開）冒煙**

用 `large_test_ss_input_on.xsett` 跑同案例。
Expected: 正常結束、無例外；補貨 bot 在前往 input station 前出現 hold（state
`SlowStartHoldInput` 計數 > 0）；input-station pod queue 等待 / 相關能耗相對 baseline
**方向性下降**（非最終數據）。

- [ ] **Step 4: 健全性檢查 — input hold 不影響 output**

確認 output 的 throughput / starvation KPI 在 treatment 與 baseline 間**無顯著差異**
（input slow-start 只動 store 流程，output 不應被影響）。

- [ ] **Step 5: Commit treatment 設定檔**

```powershell
git add Material/Instances/CoreBenchmark/large_test_ss_input_on.xsett
git commit -m "Add large-test replenishment slow-start treatment setting"
```

- [ ] **Step 6: 後續實驗（範圍外，留待分析回合）**

完整 2×2（input on/off × output on/off）省能拆解、以及「input station 是否為瓶頸 /
是否值得」的判斷（先量 input queue/occupancy），為獨立分析工作，非本實作計畫範圍。

---

## Self-Review

- **Scope 覆蓋**：下半 slow-start（hold/release）→ Task 2 狀態 + Task 3 排程；簡化 input
  EST（依當前 store 任務）→ Task 3 `ComputeInputStarvation`；獨立旗標 → Task 1 + Task 4
  獨立 gating；不碰 output → 新檔 + 新狀態類別，未修改 `BotSlowStartHold`/
  `StationReleaseScheduler`；RPS/ROA/RTA 原生 → 完全未觸碰補貨 OB/PS/TA。
- **Placeholder 掃描**：無 TBD；所有步驟含完整程式碼與指令。
- **型別一致**：`BotSlowStartHoldInput(InsertTask)`、`InputStationReleaseScheduler.Schedule(
  InputStation, PathManager, double, double)`、重用 `StationReleaseScheduler.HolderInput{
  BotId,Lift,Travel,Value,Proc}` / `Decide` / `SchedulerResult.HoldDelayByBot` /
  `SlowStartController.PipelineNextFreeTime(double, IEnumerable<(double,double)>, double)` /
  `ComputeIdealEta` / `EstimateIdealKinematicEta` 簽章與既有代碼一致；`InsertTask.Requests`
  /`InputStation`/`ReservedPod`、`InputStation.ItemBundleTransferTime`/`GetInfoBlockedLeft()`/
  `Waypoint`、`bot._isSlowStartHolding`/`_slowStartReleaseDeadline`/`IsQueueing` 均已驗證存在。
- **風險**：input EST 透過列舉 `Instance.Bots`（非 store-task 註冊表）取 store 任務——
  O(bots) per input station per tick，與 output 排程器同量級；large 場景可接受。若日後成
  熱點，可加 store-task 註冊（範圍外）。
