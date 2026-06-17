# Station-aware JIT：Net-supply Pod Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在原生出庫 PS 選 pod 時注入 net-supply 評分（`netGain = proc − w·lateness`），讓 station 快餓時改選「供給效益高且不過度晚到」的 pod，消除飢餓；下半 slow-start 不變。

**Architecture:** 一個純函式 `NetSupplyPodSelector.SelectBestPod`（無引擎物件、可單元測試）封裝選擇邏輯；`BotManagerPodSelection.DoExtractTaskForStation`（base 變體，原生路徑）在旗標開啟時建候選並呼叫它。EST/ETA/value 全部重用既有函式（`ComputeStationStarvation`、`EstimateIdealKinematicEta`、`ComputePodStationValue`）。

**Tech Stack:** C# / .NET Framework、RAWSimO.Core、RAWSimO.Playground（runnable self-test）、MSBuild x64 Release（Gurobi 僅 win64）。

**Spec:** `docs/superpowers/specs/2026-05-30-station-aware-jit-pod-selection-design.md`

---

## File Structure

| 檔案 | 動作 | 責任 |
|---|---|---|
| `RAWSimO.Core/Control/NetSupplyPodSelector.cs` | **Create** | 純函式：`PodCandidate` struct + `SelectBestPod(est, candidates, w)`，argmax netGain 與決定性 tie-break |
| `RAWSimO.Core/Configurations/SettingConfiguration.cs` | Modify | 新增 `StationAwarePodSelectionEnabled` / `JitStarvationPenalty` / `JitSelectionBuffer` |
| `RAWSimO.Core/Control/BotManagerPodSelection.cs` | Modify | base `DoExtractTaskForStation` 的「無 pod 選新 pod」分支：旗標開→走 net-supply 選擇（含兩段 ETA、proc、completeable 輔助） |
| `RAWSimO.Playground/Tests/NetSupplyPodSelectorSelfTest.cs` | **Create** | 對純函式的 red-green 斷言（仿 `StationReleaseSchedulerSelfTest`） |
| `RAWSimO.Playground/Program.cs` | Modify | `selftest` 參數同時跑新測試 |

依賴順序：Task 1（config）→ Task 2（純函式 + 測試）→ Task 3（wire 測試）→ Task 4（整合）→ Task 5（建置 + 驗收）。

---

## Task 1: 新增 config 旗標

**Files:**
- Modify: `RAWSimO.Core/Configurations/SettingConfiguration.cs:148`（緊接 `SlowStartEtaImprovementLookaheadSec` 之後）

- [ ] **Step 1: 在 SlowStart 旗標群組後新增三個欄位**

在 `RAWSimO.Core/Configurations/SettingConfiguration.cs` 第 148 行
（`public int SlowStartEtaImprovementLookaheadSec = 5;`）之後插入：

```csharp
        /// <summary>
        /// Station-aware JIT pod selection (upper half). When true, the native output-station
        /// pod selection (BotManagerPodSelection.DoExtractTaskForStation) ranks candidate pods
        /// by net-supply: netGain = proc - JitStarvationPenalty * max(0, arrival - EST),
        /// instead of the native pure-value scorer. See
        /// docs/superpowers/specs/2026-05-30-station-aware-jit-pod-selection-design.md.
        /// </summary>
        public bool StationAwarePodSelectionEnabled = false;
        /// <summary>
        /// w in netGain = proc - w * lateness. w=0 -> native pure value; w=1 -> maximize net
        /// station busy time (occupancy); large w -> hard EST feasibility. Default 1.0.
        /// </summary>
        public double JitStarvationPenalty = 1.0;
        /// <summary>
        /// Selection-layer safety buffer [s] folded into arrival = eta_bp + lift + eta_ps + buffer.
        /// Larger than SlowStartEtaSafetyBuffer because the two-leg (bot->pod->station) estimate
        /// is coarser. Initial value pending the w/buffer sweep in spec §8.
        /// </summary>
        public double JitSelectionBuffer = 16.8;
```

- [ ] **Step 2: Commit**

```powershell
git add RAWSimO.Core/Configurations/SettingConfiguration.cs
git commit -m "Add station-aware JIT pod selection config flags"
```

---

## Task 2: NetSupplyPodSelector 純函式（TDD）

**Files:**
- Create: `RAWSimO.Core/Control/NetSupplyPodSelector.cs`
- Test: `RAWSimO.Playground/Tests/NetSupplyPodSelectorSelfTest.cs`（本任務先建檔，Task 3 接線）

- [ ] **Step 1: 寫失敗測試**

建立 `RAWSimO.Playground/Tests/NetSupplyPodSelectorSelfTest.cs`：

```csharp
using System;
using System.Collections.Generic;
using RAWSimO.Core.Control;

namespace RAWSimO.Playground.Tests
{
    /// <summary>Runnable red-green assertions for NetSupplyPodSelector pure functions.</summary>
    public static class NetSupplyPodSelectorSelfTest
    {
        private static int _fails = 0;

        private static void Check(bool cond, string name)
        {
            Console.WriteLine((cond ? "PASS  " : "FAIL  ") + name);
            if (!cond) _fails++;
        }

        private static NetSupplyPodSelector.PodCandidate C(int id, double proc, double arrival, double comp = 0)
            => new NetSupplyPodSelector.PodCandidate { PodId = id, Proc = proc, Arrival = arrival, Completeable = comp };

        public static int RunAll()
        {
            _fails = 0;
            Test_WZero_MaxProc();
            Test_LargeW_FeasibleBeatsInfeasible();
            Test_LargeW_AllInfeasible_MinArrival();
            Test_WOne_Tradeoff();
            Test_NetGainTie_CompleteableBreaks();
            Test_HugeEst_EqualsWZero();
            Test_Empty_ReturnsMinusOne();
            Console.WriteLine(_fails == 0 ? "NETSUPPLY ALL PASS" : $"NETSUPPLY {_fails} FAILURES");
            return _fails == 0 ? 0 : 1;
        }

        // w=0 -> netGain=proc, lateness ignored -> max proc wins (B).
        private static void Test_WZero_MaxProc()
        {
            var cands = new List<NetSupplyPodSelector.PodCandidate> { C(1, 20, 10), C(2, 50, 200) };
            Check(NetSupplyPodSelector.SelectBestPod(est: 100, candidates: cands, w: 0) == 2,
                "WZero: max proc (pod2) wins, lateness ignored");
        }

        // large w: a feasible pod (lateness=0) beats a late high-proc pod.
        private static void Test_LargeW_FeasibleBeatsInfeasible()
        {
            var cands = new List<NetSupplyPodSelector.PodCandidate> { C(1, 50, 200), C(2, 20, 50) };
            Check(NetSupplyPodSelector.SelectBestPod(est: 100, candidates: cands, w: 1e6) == 2,
                "LargeW: feasible pod2 beats late pod1");
        }

        // large w, all late: lateness term dominates -> min arrival (B).
        private static void Test_LargeW_AllInfeasible_MinArrival()
        {
            var cands = new List<NetSupplyPodSelector.PodCandidate> { C(1, 50, 200), C(2, 20, 50) };
            Check(NetSupplyPodSelector.SelectBestPod(est: 10, candidates: cands, w: 1e6) == 2,
                "LargeW all-late: min arrival (pod2) wins");
        }

        // w=1: high proc slightly late (60-30=30) beats low proc on time (20-0=20).
        private static void Test_WOne_Tradeoff()
        {
            var cands = new List<NetSupplyPodSelector.PodCandidate> { C(1, 60, 130), C(2, 20, 90) };
            Check(NetSupplyPodSelector.SelectBestPod(est: 100, candidates: cands, w: 1) == 1,
                "WOne: high-proc-slightly-late (pod1) wins the tradeoff");
        }

        // netGain tie -> higher Completeable wins (B).
        private static void Test_NetGainTie_CompleteableBreaks()
        {
            var cands = new List<NetSupplyPodSelector.PodCandidate> { C(1, 30, 100, comp: 2), C(2, 30, 100, comp: 5) };
            Check(NetSupplyPodSelector.SelectBestPod(est: 100, candidates: cands, w: 1) == 2,
                "Tie: higher Completeable (pod2) wins");
        }

        // EST huge -> all lateness=0 -> equals w=0 (max proc, B).
        private static void Test_HugeEst_EqualsWZero()
        {
            var cands = new List<NetSupplyPodSelector.PodCandidate> { C(1, 20, 10), C(2, 50, 500) };
            Check(NetSupplyPodSelector.SelectBestPod(est: 1e9, candidates: cands, w: 1) == 2,
                "HugeEst: degrades to max proc (pod2)");
        }

        private static void Test_Empty_ReturnsMinusOne()
        {
            Check(NetSupplyPodSelector.SelectBestPod(est: 100, candidates: new List<NetSupplyPodSelector.PodCandidate>(), w: 1) == -1,
                "Empty: returns -1");
        }
    }
}
```

- [ ] **Step 2: 確認測試無法編譯 / 失敗**

此時 `NetSupplyPodSelector` 尚不存在，編譯會失敗（type not found）。這是預期的紅燈。
（待 Task 3 接線後可實際執行；此步只需確認型別未定義。）

- [ ] **Step 3: 實作純函式**

建立 `RAWSimO.Core/Control/NetSupplyPodSelector.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// Upper-half station-aware JIT pod selection (pure / testable, no engine objects).
    /// Ranks candidate pods for a fixed (bot, station) by net-supply:
    ///   netGain = proc - w * max(0, arrival - EST)
    /// proc      = supplied-items * ItemTransferTime  (busy time the pod adds, extends future EST)
    /// lateness  = max(0, arrival - EST)              (idle the pod causes if picked now)
    /// w=0 -> native pure value; w=1 -> maximize net station busy time; large w -> hard feasibility.
    /// See docs/superpowers/specs/2026-05-30-station-aware-jit-pod-selection-design.md §4.
    /// </summary>
    public static class NetSupplyPodSelector
    {
        /// <summary>One candidate pod's decision inputs for a fixed (bot, station).</summary>
        public struct PodCandidate
        {
            public int PodId;
            public double Proc;         // supplied-items * ItemTransferTime [s]
            public double Arrival;      // eta_bp + lift + eta_ps + buffer [s, from now]
            public double Completeable; // native completeable-order count (tie-break; higher better)
        }

        /// <summary>
        /// Returns the PodId maximizing netGain = Proc - w*max(0, Arrival-est).
        /// Tie-break: higher Completeable, then smaller Arrival, then smaller PodId (deterministic).
        /// Returns -1 when there are no candidates.
        /// </summary>
        public static int SelectBestPod(double est, IReadOnlyList<PodCandidate> candidates, double w)
        {
            if (candidates == null || candidates.Count == 0)
                return -1;

            double NetGain(PodCandidate c) => c.Proc - w * Math.Max(0.0, c.Arrival - est);

            return candidates
                .OrderByDescending(c => NetGain(c))
                .ThenByDescending(c => c.Completeable)
                .ThenBy(c => c.Arrival)
                .ThenBy(c => c.PodId)
                .First()
                .PodId;
        }
    }
}
```

- [ ] **Step 4: 加入 csproj（若非萬用 glob）**

確認 `RAWSimO.Core/RAWSimO.Core.csproj` 是否逐檔列出 `.cs`。若是，新增：

```xml
    <Compile Include="Control\NetSupplyPodSelector.cs" />
```
（放在既有 `Control\StationReleaseScheduler.cs` 的 `<Compile>` 附近。若 csproj 用萬用
glob 收檔則跳過此步。）

同理確認 `RAWSimO.Playground/RAWSimO.Playground.csproj` 是否需新增
`<Compile Include="Tests\NetSupplyPodSelectorSelfTest.cs" />`。

- [ ] **Step 5: Commit**

```powershell
git add RAWSimO.Core/Control/NetSupplyPodSelector.cs RAWSimO.Playground/Tests/NetSupplyPodSelectorSelfTest.cs RAWSimO.Core/RAWSimO.Core.csproj RAWSimO.Playground/RAWSimO.Playground.csproj
git commit -m "Add NetSupplyPodSelector pure function + self-test"
```

---

## Task 3: 接線 self-test 進 Program.cs

**Files:**
- Modify: `RAWSimO.Playground/Program.cs:25-29`

- [ ] **Step 1: 讓 `selftest` 同時跑兩組測試**

把 `RAWSimO.Playground/Program.cs` 第 25-29 行：

```csharp
            if (args.Length == 1 && args[0] == "selftest")
            {
                Environment.Exit(RAWSimO.Playground.Tests.StationReleaseSchedulerSelfTest.RunAll());
                return;
            }
```

改為：

```csharp
            if (args.Length == 1 && args[0] == "selftest")
            {
                int rc = RAWSimO.Playground.Tests.StationReleaseSchedulerSelfTest.RunAll();
                rc += RAWSimO.Playground.Tests.NetSupplyPodSelectorSelfTest.RunAll();
                Environment.Exit(rc);
                return;
            }
```

- [ ] **Step 2: 建置 x64 Release**

```powershell
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
& $msbuild RAWSimO.sln /p:Platform=x64 /p:Configuration=Release /m /v:minimal
```
Expected: `Build succeeded.`（0 errors）

- [ ] **Step 3: 跑 self-test，確認全綠**

```powershell
& ".\RAWSimO.Playground\bin\x64\Release\RAWSimO.Playground.exe" selftest
```
Expected: 既有 `ALL PASS` + 新增 `NETSUPPLY ALL PASS`，行程結束碼 0
（`$LASTEXITCODE` 為 0）。

- [ ] **Step 4: Commit**

```powershell
git add RAWSimO.Playground/Program.cs
git commit -m "Wire NetSupplyPodSelector self-test into selftest entrypoint"
```

---

## Task 4: 整合進 BotManagerPodSelection（net-supply 選擇路徑）

**Files:**
- Modify: `RAWSimO.Core/Control/BotManagerPodSelection.cs:1495-1540`（base `DoExtractTaskForStation` 的「無 pod 選新 pod」`else` 分支）
- Modify: 同檔末尾（`#region On-the-fly work helpers` 之前）新增三個 private 輔助方法

- [ ] **Step 1: 新增三個 private 輔助方法**

在 `RAWSimO.Core/Control/BotManagerPodSelection.cs` 的 `DoExtractTaskForStation`
方法之後（`#endregion` 前的類別內任意位置）新增：

```csharp
        /// <summary>
        /// Station-aware JIT pod selection (spec §4). For a fixed (bot, oStation), builds
        /// net-supply candidates over UnusedPods with relevant work and returns the argmax pod
        /// (or null if none). Gated by SettingConfig.StationAwarePodSelectionEnabled at call site.
        /// </summary>
        private Pod SelectPodNetSupply(Bot bot, OutputStation oStation, DefaultPodSelectionConfiguration config)
        {
            var bn = bot as BotNormal;
            double now = Instance.Controller.CurrentTime;
            double est = SlowStartController.ComputeStationStarvation(oStation, now);
            double w = Instance.SettingConfig.JitStarvationPenalty;
            double buffer = Instance.SettingConfig.JitSelectionBuffer;

            var candidates = new List<NetSupplyPodSelector.PodCandidate>();
            var podById = new Dictionary<int, Pod>();
            foreach (var pod in Instance.ResourceManager.UnusedPods
                .Where(p => AnyRelevantRequests(p, oStation, config.FilterForConsideration)))
            {
                double suppliedItems = StationReleaseScheduler.ComputePodStationValue(pod, oStation, 0);
                double proc = suppliedItems * oStation.ItemTransferTime;
                double arrival = ComputeSelectionArrivalEta(bn, pod, oStation, now, buffer);
                double completeable = CountCompleteableOrders(pod, oStation);
                candidates.Add(new NetSupplyPodSelector.PodCandidate
                {
                    PodId = pod.ID, Proc = proc, Arrival = arrival, Completeable = completeable
                });
                podById[pod.ID] = pod;
            }

            int bestId = NetSupplyPodSelector.SelectBestPod(est, candidates, w);
            return bestId >= 0 ? podById[bestId] : null;
        }

        /// <summary>
        /// Two-leg ideal-kinematic arrival estimate (bot->pod->station) + lift + buffer [s from now].
        /// Mirrors StationReleaseScheduler.Schedule's ETA probe with the same NaN/Inf fallback chain
        /// (ideal kinematic -> ComputeIdealEta -> 0).
        /// </summary>
        private double ComputeSelectionArrivalEta(BotNormal bn, Pod pod, OutputStation oStation, double now, double buffer)
        {
            var pm = Instance.Controller.PathManager;
            double orient = (bn != null) ? bn.GetTargetOrientation() : 0.0;

            double leg1 = double.NaN, leg2 = double.NaN;
            if (pm != null && bn != null)
            {
                leg1 = pm.EstimateIdealKinematicEta(bn, bn.CurrentWaypoint, pod.Waypoint, now, orient);
                leg2 = pm.EstimateIdealKinematicEta(bn, pod.Waypoint, oStation.Waypoint, now, orient);
            }
            if (double.IsNaN(leg1) || double.IsInfinity(leg1))
                leg1 = SlowStartController.ComputeIdealEta(bn, bn?.CurrentWaypoint, pod.Waypoint);
            if (double.IsNaN(leg2) || double.IsInfinity(leg2))
                leg2 = SlowStartController.ComputeIdealEta(bn, pod.Waypoint, oStation.Waypoint);
            if (double.IsNaN(leg1) || double.IsInfinity(leg1)) leg1 = 0.0;
            if (double.IsNaN(leg2) || double.IsInfinity(leg2)) leg2 = 0.0;

            double lift = (bn != null && bn.Pod == null) ? bn.PodTransferTime : 0.0;
            return leg1 + lift + leg2 + buffer;
        }

        /// <summary>
        /// Count of the station's open-demand orders this pod can fully cover on its own.
        /// Used only as a deterministic tie-break when netGain ties (spec §4.2).
        /// </summary>
        private double CountCompleteableOrders(Pod pod, OutputStation oStation)
        {
            int count = 0;
            foreach (var orderGroup in Instance.ResourceManager.GetExtractRequestsOfStation(oStation)
                .Where(r => r != null && r.State == RequestState.Unfinished)
                .GroupBy(r => r.Order))
            {
                bool coverable = orderGroup
                    .GroupBy(r => r.Item)
                    .All(itemGroup => pod.CountAvailable(itemGroup.Key) >= itemGroup.Count());
                if (coverable) count++;
            }
            return count;
        }
```

> 若檔案頂端缺少 `using RAWSimO.Core.Bots;` 或 `using RAWSimO.Core.Management;`
> （`RequestState`），補上。`System.Linq` 與 `RAWSimO.Core.Elements` 應已存在。

- [ ] **Step 2: 在「無 pod 選新 pod」分支接上旗標切換**

把 `RAWSimO.Core/Control/BotManagerPodSelection.cs:1495-1516` 的：

```csharp
            else
            {
                _bestPodOStationCandidateSelector.Recycle();
                // Determine best pod
                Pod bestPod = null;
                foreach (var pod in Instance.ResourceManager.UnusedPods
                     // Get best pod while ensuring that any work can be done with it
                     .Where(p => AnyRelevantRequests(p, oStation, config.FilterForConsideration)))
                {
                    // Update current candidate to assess
                    _currentBot = bot;
                    _currentOStation = oStation;
                    _currentPod = pod;
                    // Check whether the current combination is better
                    if (_bestPodOStationCandidateSelector.Reassess())
                    {
                        // Update best candidate
                        bestPod = _currentPod;
                    }
                }
```

改為（在原生迴圈外包一層旗標判斷；旗標關閉時行為位元級不變）：

```csharp
            else
            {
                Pod bestPod = null;
                if (Instance.SettingConfig != null && Instance.SettingConfig.StationAwarePodSelectionEnabled)
                {
                    // Station-aware JIT (spec §4): net-supply selection over candidate pods.
                    bestPod = SelectPodNetSupply(bot, oStation, config);
                }
                else
                {
                    _bestPodOStationCandidateSelector.Recycle();
                    // Determine best pod
                    foreach (var pod in Instance.ResourceManager.UnusedPods
                         // Get best pod while ensuring that any work can be done with it
                         .Where(p => AnyRelevantRequests(p, oStation, config.FilterForConsideration)))
                    {
                        // Update current candidate to assess
                        _currentBot = bot;
                        _currentOStation = oStation;
                        _currentPod = pod;
                        // Check whether the current combination is better
                        if (_bestPodOStationCandidateSelector.Reassess())
                        {
                            // Update best candidate
                            bestPod = _currentPod;
                        }
                    }
                }
```

> 注意：原本 `Pod bestPod = null;` 移到旗標判斷之上；其後的 `if (bestPod != null) { ... }`
> 區塊（含 `EnqueueExtract` 與 stat 記錄）**保持不變**，緊接在新 `else` 區塊之後。
> stat 記錄區塊使用 `_bestPodOStationCandidateSelector.BestScores`，net-supply 路徑未
> 呼叫 `Reassess()`，故旗標開啟時該 stat 反映上一輪值——可接受（非主 KPI）。

- [ ] **Step 3: 建置 x64 Release**

```powershell
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
& $msbuild RAWSimO.sln /p:Platform=x64 /p:Configuration=Release /m /v:minimal
```
Expected: `Build succeeded.`（0 errors）

- [ ] **Step 4: 回歸 — self-test 仍全綠**

```powershell
& ".\RAWSimO.Playground\bin\x64\Release\RAWSimO.Playground.exe" selftest
```
Expected: `ALL PASS` + `NETSUPPLY ALL PASS`，`$LASTEXITCODE` 為 0。

- [ ] **Step 5: Commit**

```powershell
git add RAWSimO.Core/Control/BotManagerPodSelection.cs
git commit -m "Integrate net-supply pod selection into native extract PS (flag-gated)"
```

---

## Task 5: 整合驗收（baseline 不變 + treatment 可跑）

**Files:**
- Create: `Material/Instances/CoreBenchmark/large_test_jit_on.xsett`（複製 `large_test.xsett`，加旗標）

- [ ] **Step 1: 建 treatment 設定檔**

複製 `Material/Instances/CoreBenchmark/large_test.xsett` 為
`Material/Instances/CoreBenchmark/large_test_jit_on.xsett`，在
`<SettingConfiguration>` 內（與既有 `<SlowStartEnabled>` 同層）加入：

```xml
  <StationAwarePodSelectionEnabled>true</StationAwarePodSelectionEnabled>
  <JitStarvationPenalty>1.0</JitStarvationPenalty>
  <JitSelectionBuffer>16.8</JitSelectionBuffer>
```
（若原檔無 `<SlowStartEnabled>` 節點，仍直接加上述三行；XML 反序列化會填預設值給未列欄位。）

- [ ] **Step 2: 跑 baseline（旗標關）冒煙，確認可完成**

用既有 `large_test.xsett`（旗標預設 false）跑一次短模擬，確認行為與現況一致、無例外。
依你既有的執行方式（`RAWSimO.Playground.exe` 互動選單 1: ExecuteInstance，或既有 CLI
腳本）執行 `large_test` 案例。
Expected: 正常結束、產出 KPI（OrdersHandled 等）與改動前一致（旗標關→原生路徑）。

- [ ] **Step 3: 跑 treatment（旗標開）冒煙**

用 `large_test_jit_on.xsett` 跑同案例。
Expected: 正常結束、無例外；station starvation KPI 相對 baseline 下降（方向性檢查，
非最終數據）。

- [ ] **Step 4: Commit treatment 設定檔**

```powershell
git add Material/Instances/CoreBenchmark/large_test_jit_on.xsett
git commit -m "Add large-test JIT treatment setting (StationAwarePodSelection on)"
```

- [ ] **Step 5: 記錄後續實驗待辦（spec §8）**

完整 2×2 矩陣 + `w ∈ {0, 0.5, 1, 2, 1e6}` 掃描為獨立實驗工作，非本實作計畫範圍。
於 commit message 或 analysis 目錄留一行指標即可，留待後續分析回合執行。

---

## Self-Review（已隨計畫完成）

- **Spec 覆蓋**：§3 名詞 → Task 2/4 對應 `Proc/Arrival/Completeable/w`；§4.2 演算法 →
  Task 2 `SelectBestPod` + Task 4 候選建構；§4.3 `w` 角點 → Task 2 測試 1/2/4；§4.4 退化
  → 測試 `Test_HugeEst_EqualsWZero`；§4.5 ETA fallback → `ComputeSelectionArrivalEta`；
  §5 交接（共用 EST/value）→ 重用 `ComputeStationStarvation`/`ComputePodStationValue`；
  §6 旗標 gating → Task 4 Step 2；§8 實驗 → Task 5 + 留待後續掃描；§9 測試 → Task 2。
- **Placeholder 掃描**：無 TBD；`JitSelectionBuffer` 給具體初值 16.8（spec §10.6 標為待掃描，
  此處以可執行初值落地）。
- **型別一致**：`PodCandidate{PodId,Proc,Arrival,Completeable}`、`SelectBestPod(double,
  IReadOnlyList<PodCandidate>,double)`、`SelectPodNetSupply`/`ComputeSelectionArrivalEta`/
  `CountCompleteableOrders` 在 Task 2/4 簽章一致；重用函式
  `ComputeStationStarvation`/`ComputeIdealEta`/`ComputePodStationValue`/
  `EstimateIdealKinematicEta` 簽章與既有代碼一致。
