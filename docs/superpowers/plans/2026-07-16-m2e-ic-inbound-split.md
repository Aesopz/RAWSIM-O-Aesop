# M2e-IC (v4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> **本檔取代 2026-07-16 的 v1 計畫（該版未執行任何 Task）。以 spec v4 為準。**

**Goal:** 新增 `SplitM2eICManager`（M2e 鏡像）：P1 入站限定 + 四 pod 狀態（Pp/Pq）+ SG 拆單閘門 + D9 多站懲罰 + D11 pipeline floor + D14 覆蓋獎勵 + D15 稀缺度 ε + PK packing 預算 + D16 pod-nearing-release 觸發。

**Spec:** `docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md`（v4 定稿版）。

**Architecture:** `SplitM2eICManager` = `SplitM1GExactManager.cs` 腳本化拷貝＋改名，之後在同一 Gurobi model 內插入 v4 區塊。引擎接觸面：`PackingBuffer` 新元件 + `InstanceCore` 一屬性 + `OutputStation` 兩個 null-safe 呼叫（整併釋放、D16 觸發），全部 `Instance.PackingBuffer != null` 閘控 → 非 IC 完全 inert。

**Tech Stack:** C# 7.3 / .NET Framework 4.8 / Gurobi win64 / 手寫 TestRunner。

## Global Constraints

- **絕對不改**：`M1GManager.cs`、`HADGSManager.cs`、`SplitM1GExactManager.cs`、`SplitM1GManager.cs`、`SplitM1GLBManager.cs`、`PVGSManager.cs`。
- 保留工作樹既有未提交修改；**禁止 `git add -A` / reset / checkout**——只 add 本計畫明列檔案。
- C# 7.3：新 .cs 檔必須手動加 `<Compile Include>`；lambda 不可捕獲 out 參數。
- Build（solution）：`& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:m /nologo`
- Build（tests）：同上但目標 `RAWSimO.Tests\RAWSimO.Tests.csproj`；執行 `RAWSimO.Tests\bin\x64\Release\RAWSimO.Tests.exe`（exit 0 = 全過）。
- 模擬 CLI：`RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe <xlayo> <xsett> <xconf> <outputDir> <seed>`。
- 長跑用 `.cmd` + `Start-Process -WindowStyle Hidden` + Monitor。
- 每 Task 結束 commit（只 add 該 Task 檔案），附 trailer：`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`。
- 範圍外：PVGS buffer 臂、release-moment retention、5-seed 長跑（單 seed 機制成立後由使用者拍板）。

---

### Task 1: M2eICMath 純函數 helper（8 個）+ 單元測試

**Files:**
- Create: `RAWSimO.Core/Control/Defaults/OrderBatching/M2eICMath.cs`
- Create: `RAWSimO.Tests/M2eICMathTests.cs`
- Modify: `RAWSimO.Core/RAWSimO.Core.csproj`、`RAWSimO.Tests/RAWSimO.Tests.csproj`、`RAWSimO.Tests/Program.cs`

**Interfaces（Task 4/5/6 依賴的精確簽名）:**
- `static int PaDrawBigM(IEnumerable<int> residualUnits)`
- `static bool IsWholeEligible(bool isSplitParent, bool hasInvisibleResidualSku)`
- `static int PackingBudget(int capacity, int aliveParents)`
- `static bool SplitOrderDrawsFromStorage(bool tookSplitPath, int paUnitsDrawn)`
- `static int MultiPartExcess(int stationPartCount)`
- `static bool PipelineGateOpen(bool hasProcessingPod, double releaseLeftSec, double leadSec)`
- `static bool SplitGateOpen(bool hasProcessingPod, int queuedPodCount, bool strictMode)`
- `static double PoolScarcity(int backlogDemand, int poolSupply)`

- [ ] **Step 1: 先寫測試（紅）**——建立 `RAWSimO.Tests/M2eICMathTests.cs`：

```csharp
using System;
using RAWSimO.Core.Control.Defaults.OrderBatching;

namespace RAWSimO.Tests
{
    public static class M2eICMathTests
    {
        public static void Register()
        {
            TestRunner.Add("M2eICMath.PaDrawBigM sums residual units", () =>
                TestRunner.AssertEqual(5, M2eICMath.PaDrawBigM(new[] { 3, 2 }), "3+2"));
            TestRunner.Add("M2eICMath.PaDrawBigM empty = 0", () =>
                TestRunner.AssertEqual(0, M2eICMath.PaDrawBigM(new int[0]), "empty"));
            TestRunner.Add("M2eICMath.PaDrawBigM rejects negatives", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.PaDrawBigM(new[] { 1, -1 }), "negative residual"));
            TestRunner.Add("M2eICMath.IsWholeEligible fresh + fully visible", () =>
                TestRunner.AssertTrue(M2eICMath.IsWholeEligible(false, false), "eligible"));
            TestRunner.Add("M2eICMath.IsWholeEligible split parent never whole (P1c)", () =>
                TestRunner.AssertTrue(!M2eICMath.IsWholeEligible(true, false), "parent"));
            TestRunner.Add("M2eICMath.IsWholeEligible OOS residual never whole (P1oos)", () =>
                TestRunner.AssertTrue(!M2eICMath.IsWholeEligible(false, true), "oos"));
            TestRunner.Add("M2eICMath.PackingBudget normal clamp", () =>
                TestRunner.AssertEqual(3, M2eICMath.PackingBudget(78, 75), "78-75"));
            TestRunner.Add("M2eICMath.PackingBudget floor at zero", () =>
                TestRunner.AssertEqual(0, M2eICMath.PackingBudget(78, 80), "over-occupied clamps to 0"));
            TestRunner.Add("M2eICMath.PackingBudget disabled = unlimited", () =>
                TestRunner.AssertEqual(int.MaxValue, M2eICMath.PackingBudget(0, 5), "cap<=0 sentinel"));
            TestRunner.Add("M2eICMath.PackingBudget rejects negative occupancy", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.PackingBudget(78, -1), "negative occupancy"));
            TestRunner.Add("M2eICMath.SplitOrderDrawsFromStorage flags Pa draw on split path", () =>
                TestRunner.AssertTrue(M2eICMath.SplitOrderDrawsFromStorage(true, 1), "violation"));
            TestRunner.Add("M2eICMath.SplitOrderDrawsFromStorage ok when no Pa units", () =>
                TestRunner.AssertTrue(!M2eICMath.SplitOrderDrawsFromStorage(true, 0), "clean split"));
            TestRunner.Add("M2eICMath.SplitOrderDrawsFromStorage fast path may draw Pa", () =>
                TestRunner.AssertTrue(!M2eICMath.SplitOrderDrawsFromStorage(false, 4), "whole order"));
            TestRunner.Add("M2eICMath.MultiPartExcess 0/1 part = 0", () =>
            {
                TestRunner.AssertEqual(0, M2eICMath.MultiPartExcess(0), "0 parts");
                TestRunner.AssertEqual(0, M2eICMath.MultiPartExcess(1), "1 part");
            });
            TestRunner.Add("M2eICMath.MultiPartExcess counts beyond first", () =>
                TestRunner.AssertEqual(3, M2eICMath.MultiPartExcess(4), "4 parts -> 3"));
            TestRunner.Add("M2eICMath.MultiPartExcess rejects negatives", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.MultiPartExcess(-1), "negative parts"));
            TestRunner.Add("M2eICMath.PipelineGateOpen no processing pod = open", () =>
                TestRunner.AssertTrue(M2eICMath.PipelineGateOpen(false, double.NaN, 70), "empty station must fetch"));
            TestRunner.Add("M2eICMath.PipelineGateOpen inside lead = open", () =>
                TestRunner.AssertTrue(M2eICMath.PipelineGateOpen(true, 69.9, 70), "releaseLeft <= lead"));
            TestRunner.Add("M2eICMath.PipelineGateOpen beyond lead = closed", () =>
                TestRunner.AssertTrue(!M2eICMath.PipelineGateOpen(true, 70.1, 70), "defer while busy"));
            TestRunner.Add("M2eICMath.PipelineGateOpen NaN with processing pod = closed (AE convention)", () =>
                TestRunner.AssertTrue(!M2eICMath.PipelineGateOpen(true, double.NaN, 70), "NaN treated as > lead"));
            TestRunner.Add("M2eICMath.PipelineGateOpen rejects negative lead", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.PipelineGateOpen(true, 1, -1), "negative lead"));
            TestRunner.Add("M2eICMath.SplitGateOpen strict: Pp and empty queue = open", () =>
                TestRunner.AssertTrue(M2eICMath.SplitGateOpen(true, 0, true), "bridge window"));
            TestRunner.Add("M2eICMath.SplitGateOpen strict: queued successor closes gate", () =>
                TestRunner.AssertTrue(!M2eICMath.SplitGateOpen(true, 1, true), "successor queued"));
            TestRunner.Add("M2eICMath.SplitGateOpen loose: queued successor stays open", () =>
                TestRunner.AssertTrue(M2eICMath.SplitGateOpen(true, 1, false), "loose mode"));
            TestRunner.Add("M2eICMath.SplitGateOpen no processing pod = closed in both modes", () =>
            {
                TestRunner.AssertTrue(!M2eICMath.SplitGateOpen(false, 0, true), "strict, no Pp");
                TestRunner.AssertTrue(!M2eICMath.SplitGateOpen(false, 0, false), "loose, no Pp");
            });
            TestRunner.Add("M2eICMath.SplitGateOpen rejects negative queue count", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.SplitGateOpen(true, -1, true), "negative queue"));
            TestRunner.Add("M2eICMath.PoolScarcity zero demand = 0", () =>
                TestRunner.AssertTrue(M2eICMath.PoolScarcity(0, 10) == 0.0, "no demand no scarcity"));
            TestRunner.Add("M2eICMath.PoolScarcity zero supply = 1", () =>
                TestRunner.AssertTrue(M2eICMath.PoolScarcity(5, 0) == 1.0, "missing supply maximally scarce"));
            TestRunner.Add("M2eICMath.PoolScarcity ratio and clamp", () =>
            {
                TestRunner.AssertTrue(Math.Abs(M2eICMath.PoolScarcity(5, 10) - 0.5) < 1e-12, "5/10");
                TestRunner.AssertTrue(M2eICMath.PoolScarcity(20, 10) == 1.0, "clamped at 1");
            });
            TestRunner.Add("M2eICMath.PoolScarcity rejects negative demand", () =>
                TestRunner.AssertThrows<ArgumentOutOfRangeException>(() => M2eICMath.PoolScarcity(-1, 10), "negative demand"));
        }
    }
}
```

- [ ] **Step 2: 掛進工程**——`RAWSimO.Tests/RAWSimO.Tests.csproj` Edit：

old: `    <Compile Include="M2eSunkFirstMathTests.cs" />`
new:
```xml
    <Compile Include="M2eSunkFirstMathTests.cs" />
    <Compile Include="M2eICMathTests.cs" />
```

`RAWSimO.Tests/Program.cs` Edit：

old:
```csharp
            M2eAdaptiveExactMathTests.Register();
            return TestRunner.RunAll();
```
new:
```csharp
            M2eAdaptiveExactMathTests.Register();
            M2eICMathTests.Register();
            return TestRunner.RunAll();
```

- [ ] **Step 3: 確認紅**——build tests csproj。Expected: FAIL（`M2eICMath` 不存在；編譯失敗即紅燈）。

- [ ] **Step 4: 實作**——建立 `RAWSimO.Core/Control/Defaults/OrderBatching/M2eICMath.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure arithmetic helpers for the M2e-IC (inbound-committed split) model: P1 gate,
    /// SG split gate, D11 pipeline lead gate, D9 multi-part accounting, D15 pool-local
    /// scarcity and the PK packing budget.
    /// Spec: docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md (v4).
    /// </summary>
    public static class M2eICMath
    {
        /// <summary>
        /// Big-M for the icP1d/icSG2 gates: the most units order o could possibly draw
        /// from a gated pod class this solve = its total remaining demand.
        /// </summary>
        public static int PaDrawBigM(IEnumerable<int> residualUnits)
        {
            if (residualUnits == null)
                throw new ArgumentNullException(nameof(residualUnits));
            checked
            {
                int total = 0;
                foreach (int quantity in residualUnits)
                {
                    if (quantity < 0)
                        throw new ArgumentOutOfRangeException(nameof(residualUnits), "Residual quantities cannot be negative.");
                    total += quantity;
                }
                return total;
            }
        }

        /// <summary>
        /// whole[o] eligibility (icP1c/icP1oos): an existing split parent, or an order
        /// with any out-of-stock residual SKU, can never be whole - it would decode into
        /// split children while P1 still permitted it storage-area draws.
        /// </summary>
        public static bool IsWholeEligible(bool isSplitParent, bool hasInvisibleResidualSku)
        {
            return !isSplitParent && !hasInvisibleResidualSku;
        }

        /// <summary>
        /// Remaining packing budget for NEW split parents this solve (icPK2 RHS).
        /// capacity &lt;= 0 = unlimited (int.MaxValue sentinel; PK block skipped anyway).
        /// </summary>
        public static int PackingBudget(int capacity, int aliveParents)
        {
            if (aliveParents < 0)
                throw new ArgumentOutOfRangeException(nameof(aliveParents));
            if (capacity <= 0)
                return int.MaxValue;
            return Math.Max(0, capacity - aliveParents);
        }

        /// <summary>
        /// Decode-side ground truth for the P1 gate: true when a split-path order drew a
        /// positive number of units from storage-area (Pa) pods - a P1 violation.
        /// </summary>
        public static bool SplitOrderDrawsFromStorage(bool tookSplitPath, int paUnitsDrawn)
        {
            if (paUnitsDrawn < 0)
                throw new ArgumentOutOfRangeException(nameof(paUnitsDrawn));
            return tookSplitPath && paUnitsDrawn > 0;
        }

        /// <summary>
        /// (D9) Station-parts beyond the first: max(0, parts - 1). The ep[o] lower bound.
        /// </summary>
        public static int MultiPartExcess(int stationPartCount)
        {
            if (stationPartCount < 0)
                throw new ArgumentOutOfRangeException(nameof(stationPartCount));
            return Math.Max(0, stationPartCount - 1);
        }

        /// <summary>
        /// (D11) Lead gate: open when the station has no processing pod at all, or when
        /// its remaining committed work is within leadSec. NaN releaseLeft with a
        /// processing pod present is treated as "&gt; lead" (defer) - the AE convention.
        /// </summary>
        public static bool PipelineGateOpen(bool hasProcessingPod, double releaseLeftSec, double leadSec)
        {
            if (leadSec < 0)
                throw new ArgumentOutOfRangeException(nameof(leadSec));
            if (!hasProcessingPod)
                return true;
            return !double.IsNaN(releaseLeftSec) && releaseLeftSec <= leadSec;
        }

        /// <summary>
        /// (SG) Split gate: may this station open NEW partial children this solve?
        /// A bridge partial exists to squeeze a LIVE processing pod, so no processing pod
        /// = closed in both modes. Strict additionally requires no queued successor
        /// (the bridge window = the successor's travel window).
        /// </summary>
        public static bool SplitGateOpen(bool hasProcessingPod, int queuedPodCount, bool strictMode)
        {
            if (queuedPodCount < 0)
                throw new ArgumentOutOfRangeException(nameof(queuedPodCount));
            if (!hasProcessingPod)
                return false;
            return !strictMode || queuedPodCount == 0;
        }

        /// <summary>
        /// (D15) Pool-local scarcity of a SKU: min(1, backlog demand / candidate-pool
        /// supply). Zero demand = 0; missing/non-positive supply = maximally scarce (1).
        /// Same formula as PVGS's sigma, but the denominator is THIS solve's candidate
        /// pod pool, not warehouse-wide supply.
        /// </summary>
        public static double PoolScarcity(int backlogDemand, int poolSupply)
        {
            if (backlogDemand < 0)
                throw new ArgumentOutOfRangeException(nameof(backlogDemand));
            if (backlogDemand == 0)
                return 0.0;
            if (poolSupply <= 0)
                return 1.0;
            return Math.Min(1.0, (double)backlogDemand / poolSupply);
        }
    }
}
```

`RAWSimO.Core/RAWSimO.Core.csproj` Edit：

old: `    <Compile Include="Control\Defaults\OrderBatching\M2eSunkFirstMath.cs" />`
new:
```xml
    <Compile Include="Control\Defaults\OrderBatching\M2eSunkFirstMath.cs" />
    <Compile Include="Control\Defaults\OrderBatching\M2eICMath.cs" />
```

- [ ] **Step 5: 綠**——build tests + 跑 exe。Expected: 全 `[PASS]`。

- [ ] **Step 6: Commit**

```powershell
git add RAWSimO.Core/Control/Defaults/OrderBatching/M2eICMath.cs RAWSimO.Tests/M2eICMathTests.cs RAWSimO.Core/RAWSimO.Core.csproj RAWSimO.Tests/RAWSimO.Tests.csproj RAWSimO.Tests/Program.cs
git commit -m "feat(m2e-ic): v4 pure math helpers (P1/SG/D11 gates, D9, scarcity, packing budget)"
```

---

### Task 2: PackingBuffer 元件 + Instance 屬性 + OutputStation 兩掛點 + 測試

**Files:**
- Create: `RAWSimO.Core/Elements/PackingBuffer.cs`
- Create: `RAWSimO.Tests/PackingBufferTests.cs`
- Modify: `RAWSimO.Core/InstanceCore.cs`、`RAWSimO.Core/Elements/OutputStation.cs`、兩個 csproj、`RAWSimO.Tests/Program.cs`

**Interfaces:**
- `class RAWSimO.Core.Elements.PackingBuffer`：ctor `(int capacity)`；`int Capacity`；`int AliveParentCount`；`bool RegisterParent(int orderId)`；`bool ReleaseParent(int orderId)`
- `Instance.PackingBuffer`（預設 null——**null 即所有非 IC manager 完全不受影響**）
- D16 觸發：`OutputStation.TakeItemFromPod` 兩分支各一段（`PackingBuffer != null` 閘控）

- [ ] **Step 1: 先寫測試（紅）**——建立 `RAWSimO.Tests/PackingBufferTests.cs`：

```csharp
using RAWSimO.Core.Elements;

namespace RAWSimO.Tests
{
    public static class PackingBufferTests
    {
        public static void Register()
        {
            TestRunner.Add("PackingBuffer.RegisterParent counts each parent once", () =>
            {
                var buffer = new PackingBuffer(78);
                TestRunner.AssertTrue(buffer.RegisterParent(7), "first registration");
                TestRunner.AssertTrue(!buffer.RegisterParent(7), "idempotent re-registration");
                TestRunner.AssertEqual(1, buffer.AliveParentCount, "one alive parent");
            });
            TestRunner.Add("PackingBuffer.ReleaseParent frees the box once", () =>
            {
                var buffer = new PackingBuffer(78);
                buffer.RegisterParent(7);
                TestRunner.AssertTrue(buffer.ReleaseParent(7), "release");
                TestRunner.AssertEqual(0, buffer.AliveParentCount, "box freed");
                TestRunner.AssertTrue(!buffer.ReleaseParent(7), "idempotent re-release");
            });
            TestRunner.Add("PackingBuffer.ReleaseParent unknown parent is safe", () =>
            {
                var buffer = new PackingBuffer(78);
                TestRunner.AssertTrue(!buffer.ReleaseParent(99), "unknown id no-op");
                TestRunner.AssertEqual(0, buffer.AliveParentCount, "still empty");
            });
            TestRunner.Add("PackingBuffer capacity is stored as configured", () =>
            {
                TestRunner.AssertEqual(78, new PackingBuffer(78).Capacity, "capacity 78");
                TestRunner.AssertEqual(0, new PackingBuffer(0).Capacity, "capacity 0 = unlimited");
            });
        }
    }
}
```

掛 csproj（Edit）：old `    <Compile Include="M2eICMathTests.cs" />` → new：
```xml
    <Compile Include="M2eICMathTests.cs" />
    <Compile Include="PackingBufferTests.cs" />
```
`Program.cs`（Edit）：old
```csharp
            M2eICMathTests.Register();
            return TestRunner.RunAll();
```
new
```csharp
            M2eICMathTests.Register();
            PackingBufferTests.Register();
            return TestRunner.RunAll();
```

- [ ] **Step 2: 確認紅**——build tests。Expected: FAIL。

- [ ] **Step 3: 元件**——建立 `RAWSimO.Core/Elements/PackingBuffer.cs`：

```csharp
using System.Collections.Generic;

namespace RAWSimO.Core.Elements
{
    /// <summary>
    /// Global downstream consolidation buffer for split orders (Xie et al. 2021, Appendix
    /// B: 78 boxes per shelf). ONE box per SPLIT PARENT order: reserved when the parent is
    /// first split (decode time - conservative, earlier than the physical first-part
    /// arrival, so the budget can never overshoot), released when the parent consolidates.
    /// Capacity &lt;= 0 = unlimited (probe-tracking only). Null on Instance unless an
    /// IC-family order manager instantiates it - every other manager is untouched.
    /// </summary>
    public class PackingBuffer
    {
        /// <summary>Creates the buffer with the given box capacity (&lt;= 0 = unlimited).</summary>
        /// <param name="capacity">Total box count C.</param>
        public PackingBuffer(int capacity) { Capacity = capacity; }
        /// <summary>Total box count C; &lt;= 0 means unlimited.</summary>
        public int Capacity { get; private set; }
        private readonly HashSet<int> _aliveParentIds = new HashSet<int>();
        /// <summary>Number of split parents currently holding a box (B_occ).</summary>
        public int AliveParentCount { get { return _aliveParentIds.Count; } }
        /// <summary>Reserves a box for a parent (idempotent). True if newly reserved.</summary>
        /// <param name="orderId">The parent order's ID.</param>
        public bool RegisterParent(int orderId) { return _aliveParentIds.Add(orderId); }
        /// <summary>Releases the parent's box at consolidation (idempotent, unknown-safe).</summary>
        /// <param name="orderId">The parent order's ID.</param>
        public bool ReleaseParent(int orderId) { return _aliveParentIds.Remove(orderId); }
    }
}
```

core csproj：先 Grep 確認 `Elements\OutputStation.cs` include 行，Edit：

old: `    <Compile Include="Elements\OutputStation.cs" />`
new:
```xml
    <Compile Include="Elements\OutputStation.cs" />
    <Compile Include="Elements\PackingBuffer.cs" />
```

- [ ] **Step 4: Instance 屬性**——`RAWSimO.Core/InstanceCore.cs` Edit：

old:
```csharp
        /// <summary>
        /// The configuration for all controlling mechanisms.
        /// </summary>
        public ControlConfiguration ControllerConfig { get; set; }
```
new:
```csharp
        /// <summary>
        /// The configuration for all controlling mechanisms.
        /// </summary>
        public ControlConfiguration ControllerConfig { get; set; }
        /// <summary>
        /// Global downstream packing/consolidation buffer (M2e-IC). Null unless an
        /// IC-family order manager instantiates it - all other managers are unaffected.
        /// </summary>
        public Elements.PackingBuffer PackingBuffer { get; set; }
```

- [ ] **Step 5: 整併釋放掛點**——`OutputStation.cs` Edit（`RemoveAnyCompletedOrder` 內）：

old:
```csharp
                        if (parent.NotifyChildCompleted(finishedOrder))
                        {
                            parent.TimeStampCompleted = currentTime;
                            Instance.ItemManager.CompleteOrder(parent);
                            Instance.NotifyOrderCompleted(parent, this);
                        }
```
new:
```csharp
                        if (parent.NotifyChildCompleted(finishedOrder))
                        {
                            parent.TimeStampCompleted = currentTime;
                            Instance.ItemManager.CompleteOrder(parent);
                            Instance.NotifyOrderCompleted(parent, this);
                            // (M2e-IC) consolidation releases the parent's packing box.
                            // Null-safe no-op for every manager that does not create the buffer.
                            if (Instance.PackingBuffer != null)
                                Instance.PackingBuffer.ReleaseParent(parent.ID);
                        }
```

- [ ] **Step 6: D16 pod-nearing-release 觸發（兩分支各一段）**——`OutputStation.cs` `TakeItemFromPod`。
  背景事實：`SignalOrderFinished` 的 body 只有 `SituationInvestigated = false;`（`Control/OrderManager.cs:204-207`，參數未使用）→ 傳 null 安全。

Edit ①（`request.Order == null` 分支；錨點含 32 空格縮排，全檔唯一）：

old:
```csharp
                                bot.WaitUntil(currentTime + ItemPickTime);
                                // Track when the currently processed pod can leave the station.
                                UpdateCurrentProcessingPodRelease(bot, currentTime);
```
new:
```csharp
                                bot.WaitUntil(currentTime + ItemPickTime);
                                // Track when the currently processed pod can leave the station.
                                UpdateCurrentProcessingPodRelease(bot, currentTime);
                                // (M2e-IC / D16) one pick left on this task -> wake the order
                                // manager so a re-solve can extend the pod while the on-the-fly
                                // window (Requests.Any) is still open. Null-safe: only IC-family
                                // managers create the buffer; all other configs bit-identical.
                                if (Instance.PackingBuffer != null && bot.CurrentTask is Control.ExtractTask _icNr0
                                    && _icNr0.Requests != null && _icNr0.Requests.Count == 1)
                                    Instance.Controller.OrderManager.SignalOrderFinished(null, this);
```

Edit ②（`request.Order` 指定分支；28 空格縮排，全檔唯一）：

old:
```csharp
                            bot.WaitUntil(currentTime + ItemPickTime);
                            // Track when the currently processed pod can leave the station.
                            UpdateCurrentProcessingPodRelease(bot, currentTime);
```
new:
```csharp
                            bot.WaitUntil(currentTime + ItemPickTime);
                            // Track when the currently processed pod can leave the station.
                            UpdateCurrentProcessingPodRelease(bot, currentTime);
                            // (M2e-IC / D16) see the twin branch above.
                            if (Instance.PackingBuffer != null && bot.CurrentTask is Control.ExtractTask _icNr1
                                && _icNr1.Requests != null && _icNr1.Requests.Count == 1)
                                Instance.Controller.OrderManager.SignalOrderFinished(null, this);
```

- [ ] **Step 7: 綠**——build **全 solution** + 跑 tests exe。Expected: 0 error、全 `[PASS]`。

- [ ] **Step 8: Commit**

```powershell
git add RAWSimO.Core/Elements/PackingBuffer.cs RAWSimO.Tests/PackingBufferTests.cs RAWSimO.Core/InstanceCore.cs RAWSimO.Core/Elements/OutputStation.cs RAWSimO.Core/RAWSimO.Core.csproj RAWSimO.Tests/RAWSimO.Tests.csproj RAWSimO.Tests/Program.cs
git commit -m "feat(m2e-ic): PackingBuffer + consolidation release + D16 pod-nearing-release trigger (inert unless instantiated)"
```

---

### Task 3: Config / enum / XmlInclude / Controller + Manager 鏡像骨架

**Files:**
- Modify: `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`、`RAWSimO.Core/Configurations/MethodConfiguration.cs`、`RAWSimO.Core/Control/Controller.cs`、`RAWSimO.Core/RAWSimO.Core.csproj`
- Create: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`（腳本化拷貝）

**Interfaces:**
- Produces：`SplitM2eICConfiguration`（v4 全欄位，見下）；enum `SplitM2eIC`；token `OBSPLITM2EIC`；manager 私有欄位 `_icConfig`（Task 4/5/6 讀）。

- [ ] **Step 1: Config 類**——`MethodConfigurationsOB.cs` Edit，插在 `SplitM1GLBConfiguration` doc comment 前：

old:
```csharp
    /// <summary>
    /// Late-binding variant of SplitM1GExact (M2e-LB): identical MILP, but AllocateOrder is
```
new:
```csharp
    /// <summary>
    /// M2e-IC (Inbound-Committed Split, spec v4): SplitM1GExact plus (P1) split orders draw
    /// only from committed pods while storage pods serve WHOLE orders only, (SG) a
    /// state-conditional split gate (new partials bridge a dying processing pod only),
    /// (D9) a multi-part penalty, (D11) a lead-gated pipeline floor, (D14) a selected-pod
    /// residual-coverage tie-break, (D15) pool-scarcity-weighted squeeze and (PK) an
    /// optional per-split-parent packing budget (Xie et al. 2021 Appx B).
    /// Spec: docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md.
    /// </summary>
    public class SplitM2eICConfiguration : SplitM1GExactConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SplitM2eIC; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBSPLITM2EIC"; }
        /// <summary>
        /// (PK) Downstream packing buffer capacity C (Xie et al. 2021 Appendix B derives
        /// 78 boxes per shelf). One box per split parent from first split until
        /// consolidation. &lt;= 0 = unlimited (constraint absent) - the default.
        /// </summary>
        public int PackingBufferCapacity = 0;
        /// <summary>
        /// (D9) wp: penalty per station-part beyond an order's first. Soft whole-preference:
        /// keep 2*IdleSlotWeight &lt; MultiPartPenalty &lt; |OrderRewardWeight| so gratuitous
        /// multi-station shredding loses while genuinely-needed multi-station completions
        /// stay affordable. 0 = off.
        /// </summary>
        public double MultiPartPenalty = 12;
        /// <summary>
        /// (D11) w_pipe: penalty per unit of per-station pipeline shortfall while the lead
        /// gate is open. Size between the net new-trip completion value and
        /// |OrderRewardWeight| so a slot+trip gets dedicated to the NEXT pod even while
        /// the current one still offers completions.
        /// </summary>
        public double PipelineFloorWeight = 20;
        /// <summary>
        /// (D11) Lead: dispatch the next pod once the processing pod's remaining committed
        /// work is within this many seconds (AE-validated 70; lead=0 over-supplies).
        /// </summary>
        public double PipelineFloorLeadSec = 70;
        /// <summary>
        /// (D11) T: future pods (queued + en-route) targeted per station beyond the
        /// physical one. AE-validated 1 (i.e. two-pod pipeline); also the hard cap.
        /// </summary>
        public int PipelineFloorTarget = 1;
        /// <summary>(D11) Master switch for the pipeline floor.</summary>
        public bool PipelineFloorEnabled = true;
        /// <summary>(SG) Master switch for the split gate.</summary>
        public bool SplitGateEnabled = true;
        /// <summary>
        /// (SG) Strict = new partials only when the station has a processing pod AND no
        /// queued successor (bridge window = successor's travel window). False (loose) =
        /// processing pod present suffices (harvests the post-queue tail; ablation arm).
        /// </summary>
        public bool SplitGateStrict = true;
        /// <summary>
        /// (D14) Epsilon_cov: reward per unit of selected-pod-set coverage of the backlog
        /// residual pool (negative = reward). Among equal-completion pod sets this is
        /// exactly the leftover-coverage tie-break. Keep |value|*max-coverage well below
        /// |OrderRewardWeight|. 0 = off.
        /// </summary>
        public double CoverageRewardWeight = -0.2;
    }

    /// <summary>
    /// Late-binding variant of SplitM1GExact (M2e-LB): identical MILP, but AllocateOrder is
```

- [ ] **Step 2: enum**——`MethodConfiguration.cs` Edit：

old:
```csharp
        /// <summary>
        /// Late-binding SplitM1GExact (M2e-LB): same MILP, AllocateOrder deferred to pod-claim
        /// time via a deferred-binding ledger; PlannedWipCap replaces physical Cs semantics.
        /// </summary>
        SplitM1GLB,
    }
```
new:
```csharp
        /// <summary>
        /// Late-binding SplitM1GExact (M2e-LB): same MILP, AllocateOrder deferred to pod-claim
        /// time via a deferred-binding ledger; PlannedWipCap replaces physical Cs semantics.
        /// </summary>
        SplitM1GLB,
        /// <summary>
        /// M2e-IC (Inbound-Committed Split, v4): SplitM1GExact plus the P1/SG gates
        /// (splits draw from committed pods; new partials bridge a dying processing pod
        /// only), multi-part penalty, lead-gated pipeline floor, coverage/scarcity
        /// tie-breaks and an optional per-split-parent packing budget.
        /// </summary>
        SplitM2eIC,
    }
```

- [ ] **Step 3: XmlInclude**——`MethodConfiguration.cs` Edit：

old:
```csharp
    [XmlInclude(typeof(PVGSConfiguration))]
    public abstract class OrderBatchingConfiguration : ControllerConfigurationBase
```
new:
```csharp
    [XmlInclude(typeof(PVGSConfiguration))]
    [XmlInclude(typeof(SplitM2eICConfiguration))]
    public abstract class OrderBatchingConfiguration : ControllerConfigurationBase
```

- [ ] **Step 4: Controller case**——`Controller.cs` Edit：

old: `                case OrderBatchingMethodType.SplitM1GLB: OrderManager = new SplitM1GLBManager(instance); break;`
new:
```csharp
                case OrderBatchingMethodType.SplitM1GLB: OrderManager = new SplitM1GLBManager(instance); break;
                case OrderBatchingMethodType.SplitM2eIC: OrderManager = new SplitM2eICManager(instance); break;
```

- [ ] **Step 5: 鏡像拷貝（腳本化）**——PowerShell 逐字執行：

```powershell
Copy-Item RAWSimO.Core\Control\Defaults\OrderBatching\SplitM1GExactManager.cs RAWSimO.Core\Control\Defaults\OrderBatching\SplitM2eICManager.cs
$f = 'RAWSimO.Core\Control\Defaults\OrderBatching\SplitM2eICManager.cs'
$t = Get-Content $f -Raw
$t = $t.Replace('public class SplitM1GExactManager : M1GManager', 'public class SplitM2eICManager : M1GManager')
$t = $t.Replace('public SplitM1GExactManager(Instance instance)', 'public SplitM2eICManager(Instance instance)')
$t = $t.Replace('"splitm1gx_decision_log.csv"', '"splitm2eic_decision_log.csv"')
$t = $t.Replace('"m2e_adaptive_exact_log.csv"', '"m2eic_adaptive_exact_log.csv"')
$t = $t.Replace('"m2e_pod_replenishment_log.csv"', '"m2eic_pod_replenishment_log.csv"')
$t = $t.Replace('"m2e_pipeline_probe_log.csv"', '"m2eic_pipeline_probe_log.csv"')
Set-Content $f $t -Encoding UTF8
```
驗證：`Select-String -Path $f -Pattern 'class SplitM2eICManager'` 命中 1；`'class SplitM1GExactManager'` 命中 0。

- [ ] **Step 6: ctor + `_icConfig` + guard + buffer**——`SplitM2eICManager.cs` Edit：

old:
```csharp
        public SplitM2eICManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GExactConfiguration;
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }
```
new:
```csharp
        public SplitM2eICManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GExactConfiguration;
            _icConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM2eICConfiguration;
            // (IC) experimental M2e arms re-shape the objective or the solve structure;
            // their interaction with the P1/SG gates is undefined - fail fast.
            if (_splitConfig != null && (_splitConfig.AdaptiveExactResweeps || _splitConfig.SunkFirstScoring
                || _splitConfig.CoverageFirstScoring || _splitConfig.TrueCompletionReward > 0))
                throw new InvalidOperationException("SplitM2eIC does not support AdaptiveExactResweeps/SunkFirstScoring/CoverageFirstScoring/TrueCompletionReward.");
            // (IC) global downstream packing buffer; probe tracking always on, the PK
            // budget constraint additionally requires PackingBufferCapacity > 0. Creating
            // the buffer also arms the two null-safe engine hooks (release + D16 trigger).
            if (instance.PackingBuffer == null)
                instance.PackingBuffer = new PackingBuffer(_icConfig != null ? _icConfig.PackingBufferCapacity : 0);
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }

        /// <summary>The IC-specific config (v4 fields live here).</summary>
        private SplitM2eICConfiguration _icConfig;
```

- [ ] **Step 7: 類別 doc comment 更新**——Edit：

old:
```csharp
    /// <summary>
    /// MILP-based order-splitting manager with pod-level attribution decided inside the model:
    /// q[i,o,p,s] (4D) replaces SplitM1G's q[i,o,s] (3D), so the solver itself picks which pod
    /// serves each unit instead of a post-solve greedy pass. Independent sibling of
    /// SplitM1GManager (Spec 2) - does not inherit it, mirrors M1GManager directly.
    /// See docs/superpowers/specs/2026-07-07-splitm1g-exact-design.md.
    /// </summary>
```
new:
```csharp
    /// <summary>
    /// M2e-IC (Inbound-Committed Split, spec v4): faithful mirror of SplitM1GExactManager
    /// (4D q[i,o,p,s] MILP) plus the P1/SG gates, the D9 multi-part penalty, the D11
    /// lead-gated pipeline floor, the D14 coverage and D15 scarcity tie-breaks and the PK
    /// packing budget. SplitM1GExactManager itself stays untouched as the M2e baseline.
    /// See docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md.
    /// </summary>
```

- [ ] **Step 8: csproj include**——Edit：

old: `    <Compile Include="Control\Defaults\OrderBatching\SplitM1GExactManager.cs" />`
new:
```xml
    <Compile Include="Control\Defaults\OrderBatching\SplitM1GExactManager.cs" />
    <Compile Include="Control\Defaults\OrderBatching\SplitM2eICManager.cs" />
```

- [ ] **Step 9: Build 全 solution**。Expected: 0 error（此時行為 = M2e 鏡像 + guard + buffer）。

- [ ] **Step 10: Commit**

```powershell
git add RAWSimO.Core/Configurations/MethodConfigurationsOB.cs RAWSimO.Core/Configurations/MethodConfiguration.cs RAWSimO.Core/Control/Controller.cs RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs RAWSimO.Core/RAWSimO.Core.csproj
git commit -m "feat(m2e-ic): SplitM2eICManager mirror skeleton + v4 config/enum/controller plumbing"
```

---

### Task 4: 四狀態 partition + 稀缺度 + P1 + SG + D9 限制式 + decode assert

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`

**Interfaces:**
- Produces（Task 5/6 依賴）：`SolveSplitExact` 區域變數 `icPpByStation`/`icPqCountByStation`/`icFutureByStation`/`icSgOpenByStation`/`icSgOpenCount`/`icGateForegone`/`icScarcity`（`Dictionary<int,double>`）/`icWholeVarNames`；變數命名 `icwholex_<oid>`、`icepx_<oid>`。

- [ ] **Step 1: processingPods 無條件蒐集**——Edit：

old:
```csharp
            HashSet<Pod> processingPods = new HashSet<Pod>();
            if (w5 != 0 || adaptiveExact)
                foreach (var station in Cs.Keys)
```
new:
```csharp
            HashSet<Pod> processingPods = new HashSet<Pod>();
            // (IC) always collected: the Pp/Pq partition and icProcUnits probe need it
            // regardless of w5 (mirror deviation - original gates on w5/adaptive).
            if (true)
                foreach (var station in Cs.Keys)
```

- [ ] **Step 2: partition + 稀缺度 + probe 常數**——Edit（錨點 = `int maxCs`，全檔唯一）：

old:
```csharp
            int maxCs = Cs.Count > 0 ? Cs.Values.Max() : 1;
```
new:
```csharp
            // ── (IC/v4) per-station pod-state partition (Pp/Pq/future) + pool scarcity ──
            // Decision-time constants driving the SG gate, the D11 floor, the D15
            // scarcity weights and the strict-gate opportunity-cost probe.
            Dictionary<int, HashSet<int>> icPpByStation = new Dictionary<int, HashSet<int>>();
            Dictionary<int, int> icPqCountByStation = new Dictionary<int, int>();
            Dictionary<int, int> icFutureByStation = new Dictionary<int, int>();
            foreach (var station in Cs.Keys)
            {
                HashSet<int> icProc = new HashSet<int>();
                int icQueued = 0;
                int icCommittedHere = 0;
                HashSet<Pod> icStationPods;
                if (inboundPods.TryGetValue(station, out icStationPods))
                    foreach (var pod in icStationPods)
                    {
                        if (!Pb.Contains(pod) || !PodToBot.ContainsKey(pod))
                            continue;
                        icCommittedHere++;
                        var icWayp = PodToBot[pod].CurrentWaypoint;
                        if (icWayp != null && station.Waypoint != null && icWayp.ID == station.Waypoint.ID)
                            icProc.Add(pod.ID);
                        else if (icWayp != null && icWayp.IsQueueWaypoint)
                            icQueued++;
                    }
                icPpByStation[station.ID] = icProc;
                icPqCountByStation[station.ID] = icQueued;
                icFutureByStation[station.ID] = icCommittedHere - icProc.Count;
            }
            bool icSgEnabled = _icConfig == null || _icConfig.SplitGateEnabled;
            bool icSgStrict = _icConfig == null || _icConfig.SplitGateStrict;
            Dictionary<int, bool> icSgOpenByStation = new Dictionary<int, bool>();
            foreach (var station in Cs.Keys)
                icSgOpenByStation[station.ID] = M2eICMath.SplitGateOpen(
                    icPpByStation[station.ID].Count > 0, icPqCountByStation[station.ID], icSgStrict);
            int icSgOpenCount = icSgOpenByStation.Values.Count(v => v);
            // (D15) pool-local scarcity per SKU id
            Dictionary<int, int> icDemById = new Dictionary<int, int>();
            foreach (var icPair in residuals)
                foreach (var icLine in icPair.Value)
                {
                    int icCur;
                    icDemById[icLine.Key.ID] = (icDemById.TryGetValue(icLine.Key.ID, out icCur) ? icCur : 0) + icLine.Value;
                }
            Dictionary<int, int> icSupById = new Dictionary<int, int>();
            foreach (var pod in Pods)
                foreach (var icItem in pod.ItemDescriptionsContained)
                {
                    int icCur;
                    icSupById[icItem.ID] = (icSupById.TryGetValue(icItem.ID, out icCur) ? icCur : 0) + pod.CountAvailable(icItem);
                }
            Dictionary<int, double> icScarcity = new Dictionary<int, double>();
            foreach (var icDem in icDemById)
            {
                int icSupVal;
                icScarcity[icDem.Key] = M2eICMath.PoolScarcity(icDem.Value,
                    icSupById.TryGetValue(icDem.Key, out icSupVal) ? icSupVal : 0);
            }
            // (probe) strict-gate opportunity cost: backlog-matching residual sitting on the
            // processing pods of gate-CLOSED stations (units the strict gate declines to harvest)
            int icGateForegone = 0;
            foreach (var station in Cs.Keys)
            {
                if (!icSgEnabled || icSgOpenByStation[station.ID])
                    continue;
                foreach (var pod in Pods.Where(p => icPpByStation[station.ID].Contains(p.ID)))
                    foreach (var icItem in pod.ItemDescriptionsContained)
                    {
                        int icDemHere;
                        if (icDemById.TryGetValue(icItem.ID, out icDemHere) && icDemHere > 0)
                            icGateForegone += Math.Min(pod.CountAvailable(icItem), icDemHere);
                    }
            }
            int maxCs = Cs.Count > 0 ? Cs.Values.Max() : 1;
```

- [ ] **Step 3: P1 + SG + D9 限制式區塊**——Edit（錨點 = `// Order-first control:` 註解，全檔唯一）：

old:
```csharp
            // Order-first control: the normal pass admits only z=1 complete residuals. A
```
new:
```csharp
            // ── (IC/P1) inbound-committed gate ─────────────────────────────────────────
            // An order drawing ANY unit from a storage-area pod (Pa) must be WHOLE:
            // completed this solve (icP1a), at most one station (icP1b, big-M form),
            // not an existing split parent and no out-of-stock residual (icP1c/icP1oos -
            // legacy zdonex skips OOS SKUs, so without the oos leg a "complete" order
            // could decode into split children bound to a Pa pod).
            List<string> icWholeVarNames = new List<string>();
            int icStationCount = Cs.Count;
            foreach (var order in pendingOrders.OrderBy(o => o.ID))
            {
                string icWname = "icwholex_" + order.ID.ToString();
                string icZname = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                icWholeVarNames.Add(icWname);
                bool icHasInvisibleResidual = residuals[order].Any(p => p.Value > 0 && !PiSKU.ContainsKey(p.Key));
                if (!M2eICMath.IsWholeEligible(order.IsSplitParent, icHasInvisibleResidual))
                {
                    wrapper.AddConstr(variablesBinary[icWname] <= 0, "icP1c");
                }
                else
                {
                    wrapper.AddConstr(variablesBinary[icWname] <= variablesBinary[icZname], "icP1a");
                    var icOrderY = deVarNamey.Where(v => v.order.ID == order.ID)
                        .Select(v => variablesBinary[v.name]).ToList();
                    if (icOrderY.Count > 0 && icStationCount > 1)
                        wrapper.AddConstr(LinearExpression.Sum(icOrderY)
                            + (icStationCount - 1) * variablesBinary[icWname] <= icStationCount, "icP1b");
                }
                var icPaQ = deVarNameq.Where(v => v.order.ID == order.ID && Pa.Contains(v.pod))
                    .Select(v => variablesQ[v.name]).ToList();
                if (icPaQ.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(icPaQ)
                        <= M2eICMath.PaDrawBigM(residuals[order].Values) * variablesBinary[icWname], "icP1d");
            }
            // ── (IC/SG) split gate ─────────────────────────────────────────────────────
            // Gate-closed stations: fresh orders may take a slot only if they COMPLETE this
            // solve (whole or multi-station completion); new partials are bridge actions for
            // a dying processing pod. Existing parents exempt (finishing them shrinks WIP).
            // (SG2) fresh partials additionally draw from the station's PROCESSING pod only
            // - a bridge must be pickable NOW (Pa is already gated by icP1d).
            if (icSgEnabled)
            {
                foreach (var y in deVarNamey)
                {
                    if (y.order.IsSplitParent || icSgOpenByStation[y.outputstation.ID])
                        continue;
                    string icZn = (crossTime ? "zdonex" : "zfullx") + "_" + y.order.ID.ToString();
                    wrapper.AddConstr(variablesBinary[y.name] <= variablesBinary[icZn], "icSG1");
                }
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    if (order.IsSplitParent)
                        continue;
                    var icNonPpQ = deVarNameq.Where(v => v.order.ID == order.ID && Pb.Contains(v.pod)
                        && !icPpByStation[v.outputstation.ID].Contains(v.pod.ID))
                        .Select(v => variablesQ[v.name]).ToList();
                    if (icNonPpQ.Count > 0)
                    {
                        string icZn = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                        wrapper.AddConstr(LinearExpression.Sum(icNonPpQ)
                            <= M2eICMath.PaDrawBigM(residuals[order].Values) * variablesBinary[icZn], "icSG2");
                    }
                }
            }
            // ── (IC/D9) multi-part linearization: sum_s ysp <= ep + 1 ──────────────────
            if (_icConfig != null && _icConfig.MultiPartPenalty != 0)
            {
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    var icOrderY = deVarNamey.Where(v => v.order.ID == order.ID)
                        .Select(v => variablesBinary[v.name]).ToList();
                    if (icOrderY.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(icOrderY)
                            <= variablesUs["icepx_" + order.ID.ToString()] + 1, "icD9");
                }
            }
            // Order-first control: the normal pass admits only z=1 complete residuals. A
```

- [ ] **Step 4: decode 硬 assert**——Edit（錨點 = split path 的 `result.SplitParents.Add(order);`，全檔唯一）：

old:
```csharp
                        result.SplitParents.Add(order);
```
new:
```csharp
                        result.SplitParents.Add(order);
                        // (IC) P1 ground truth: a split-path order must not have drawn from
                        // any storage-area (Pa) pod in this solution.
                        int icPaUnits = IsdeVarNameq.Where(v => v.order.ID == order.ID && Pa.Contains(v.pod))
                            .Sum(v => (int)Math.Round(variablesQ[v.name].GetValue()));
                        if (M2eICMath.SplitOrderDrawsFromStorage(true, icPaUnits))
                            throw new InvalidOperationException("M2e-IC: split order " + order.ID
                                + " drew " + icPaUnits + " unit(s) from storage-area pods (P1 violated).");
```

- [ ] **Step 5: Build 全 solution**。Expected: 0 error（`icWholeVarNames`/`icSgOpenCount`/`icGateForegone`/`icScarcity` 暫未讀取屬正常，Task 5/6 使用）。

- [ ] **Step 6: Commit**

```powershell
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs
git commit -m "feat(m2e-ic): pod-state partition, scarcity map, P1/SG/D9 constraints, decode assert"
```

---

### Task 5: 目標式 v4（稀缺 ε 替換 + wp 項 + D11 floor + D14 coverage）

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`

**Interfaces:**
- Consumes：Task 4 的 `icScarcity`/`icPpByStation`/`icFutureByStation`、Task 1 的 gate helpers、Task 3 的 `_icConfig`。
- Produces：區域變數 `icShortfallNames`（Task 6 log 讀）。

- [ ] **Step 1: eps → 稀缺度加權**——Edit：

old:
```csharp
            double eps = _splitConfig != null ? _splitConfig.UnitDrawReward : 0;
            if (eps != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name])) * eps;
```
new:
```csharp
            double eps = _splitConfig != null ? _splitConfig.UnitDrawReward : 0;
            // (IC/D15) scarcity-weighted squeeze: eps * poolScarcity[i] per drawn unit.
            // P1d + icSG2 cap the dose structurally (partial draws are Pp-only), so unlike
            // the flat eps arm (TP 623) no fishing trip can ever be provoked by this term.
            if (eps != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v =>
                    variablesQ[v.name] * (eps * (icScarcity.ContainsKey(v.skui.ID) ? icScarcity[v.skui.ID] : 0.0))), wrapper);
```

- [ ] **Step 2: wp 項 + D11 floor + D14 coverage**——Edit（錨點 = PR pro-rata 區塊，全檔唯一）：

old:
```csharp
            if (prMode && prR != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name] * (-prR / v.order.GetDemandCount())));
```
new:
```csharp
            if (prMode && prR != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name] * (-prR / v.order.GetDemandCount())));
            // ── (IC/v4) objective additions: D9 wp, D11 pipeline floor, D14 coverage ──
            double icWpPen = _icConfig != null ? _icConfig.MultiPartPenalty : 0;
            if (icWpPen != 0 && pendingOrders.Count > 0)
                objective = objective + LinearExpression.Sum(pendingOrders.OrderBy(o => o.ID)
                    .Select(o => variablesUs["icepx_" + o.ID.ToString()])) * icWpPen;
            List<string> icShortfallNames = new List<string>();
            bool icFloorOn = _icConfig != null && _icConfig.PipelineFloorEnabled && _icConfig.PipelineFloorWeight != 0;
            if (icFloorOn)
            {
                int icT = Math.Max(1, _icConfig.PipelineFloorTarget);
                foreach (var station in Cs.Keys)
                {
                    var icNewXps = deVarNamexps.Where(v => v.outputstation.ID == station.ID && Pa.Contains(v.pod))
                        .Select(v => variablesBinary[v.name]).ToList();
                    int icFuture = icFutureByStation[station.ID];
                    // (icLGcap) hard anti-oversupply: future + new <= T (AE target=3 counterexample)
                    if (icNewXps.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(icNewXps) <= Math.Max(0, icT - icFuture), "icLGcap");
                    // (icLG1) soft floor while the lead gate is open: dispatch is clock-driven,
                    // not exhaustion-driven - squeeze and dispatch coexist in one solve.
                    bool icGateOpen = M2eICMath.PipelineGateOpen(icPpByStation[station.ID].Count > 0,
                        station.GetInfoCurrentPodReleaseLeft(), _icConfig.PipelineFloorLeadSec);
                    if (!icGateOpen || icT - icFuture <= 0)
                        continue;
                    string icSfName = "icsfx_" + station.ID.ToString();
                    icShortfallNames.Add(icSfName);
                    if (icNewXps.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(icNewXps) + variablesUs[icSfName]
                            >= icT - icFuture, "icLG1");
                    else
                        wrapper.AddConstr(variablesUs[icSfName] >= icT - icFuture, "icLG1");
                }
                if (icShortfallNames.Count > 0)
                    objective = objective + LinearExpression.Sum(icShortfallNames.Select(n => variablesUs[n])) * _icConfig.PipelineFloorWeight;
            }
            double icCov = _icConfig != null ? _icConfig.CoverageRewardWeight : 0;
            if (icCov != 0)
            {
                // (IC/D14) selected-pod-set residual coverage as a SMALL additive tie-break
                // (not CF's failed lexicographic stage): c_i <= pool demand, c_i <= selected
                // supply. Among equal-completion pod sets total coverage differs exactly by
                // the leftover coverage - the residual-SKU sizing tie-break, no subtraction.
                VariableCollection<string> icCovVars = new VariableCollection<string>(wrapper, VariableType.Continuous, 0, double.PositiveInfinity, (string s) => { return s; });
                List<string> icCovNames = new List<string>();
                foreach (var icSku in OiSKU.Keys.Where(k => PiSKU.ContainsKey(k)))
                {
                    int icDemandHere = 0;
                    foreach (var icPair in residuals)
                    {
                        int icLine;
                        if (icPair.Value.TryGetValue(icSku, out icLine))
                            icDemandHere += icLine;
                    }
                    if (icDemandHere <= 0)
                        continue;
                    var icSupplyTerms = deVarNamexps.Where(v => v.pod.CountAvailable(icSku) > 0)
                        .Select(v => variablesBinary[v.name] * (double)v.pod.CountAvailable(icSku)).ToList();
                    if (icSupplyTerms.Count == 0)
                        continue;
                    string icCn = "iccov_" + icSku.ID.ToString();
                    icCovNames.Add(icCn);
                    wrapper.AddConstr(icCovVars[icCn] <= icDemandHere, "iccovd");
                    wrapper.AddConstr(icCovVars[icCn] <= LinearExpression.Sum(icSupplyTerms), "iccovs");
                }
                if (icCovNames.Count > 0)
                    objective = objective + LinearExpression.Sum(icCovNames.Select(n => icCovVars[n])) * icCov;
            }
```

- [ ] **Step 3: Build 全 solution + tests**。Expected: 0 error、全 `[PASS]`（`icShortfallNames` 未讀取屬正常，Task 6 用）。

- [ ] **Step 4: Commit**

```powershell
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs
git commit -m "feat(m2e-ic): v4 objective - scarcity-weighted squeeze, wp term, pipeline floor, coverage tie-break"
```

---

### Task 6: PK packing 預算 + decode 登記 + 決策 log 探針欄位

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`

- [ ] **Step 1: PK 區塊**——Edit（錨點 = `// Order-first control:`，Task 4 的區塊已在其前，本區塊插在其後仍以同錨點定位——**插在 P1/SG/D9 區塊與錨點註解之間**）：

old:
```csharp
            // Order-first control: the normal pass admits only z=1 complete residuals. A
```
new:
```csharp
            // ── (IC/PK) downstream packing budget (Xie Appx B; one box per split parent,
            // reserved at decode, released at consolidation; <=0 disables the block) ──
            int icPackCapacity = _icConfig != null ? _icConfig.PackingBufferCapacity : 0;
            if (icPackCapacity > 0)
            {
                int icBOcc = Instance.PackingBuffer != null ? Instance.PackingBuffer.AliveParentCount : 0;
                List<string> icYpackNames = new List<string>();
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    if (order.IsSplitParent)
                        continue; // box already reserved at its first split (inside icBOcc)
                    string icPname = "icypackx_" + order.ID.ToString();
                    string icWn = "icwholex_" + order.ID.ToString();
                    icYpackNames.Add(icPname);
                    var icPkY = deVarNamey.Where(v => v.order.ID == order.ID).ToList();
                    foreach (var y in icPkY)
                        wrapper.AddConstr(variablesBinary[icPname] + variablesBinary[icWn]
                            >= variablesBinary[y.name], "icPK1a");
                    if (icPkY.Count > 0)
                        wrapper.AddConstr(variablesBinary[icPname]
                            <= LinearExpression.Sum(icPkY.Select(v => variablesBinary[v.name])), "icPK1b");
                    wrapper.AddConstr(variablesBinary[icPname] + variablesBinary[icWn] <= 1, "icPK1c");
                }
                if (icYpackNames.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(icYpackNames.Select(n => variablesBinary[n]))
                        <= M2eICMath.PackingBudget(icPackCapacity, icBOcc), "icPK2");
            }
            // Order-first control: the normal pass admits only z=1 complete residuals. A
```

- [ ] **Step 2: decode 登記**——Edit（錨點 = Task 4 assert 尾端）：

old:
```csharp
                        if (M2eICMath.SplitOrderDrawsFromStorage(true, icPaUnits))
                            throw new InvalidOperationException("M2e-IC: split order " + order.ID
                                + " drew " + icPaUnits + " unit(s) from storage-area pods (P1 violated).");
```
new:
```csharp
                        if (M2eICMath.SplitOrderDrawsFromStorage(true, icPaUnits))
                            throw new InvalidOperationException("M2e-IC: split order " + order.ID
                                + " drew " + icPaUnits + " unit(s) from storage-area pods (P1 violated).");
                        // (IC/PK) reserve the parent's packing box at first split (idempotent
                        // for re-split parents; conservative - never overshoots capacity).
                        if (Instance.PackingBuffer != null)
                            Instance.PackingBuffer.RegisterParent(order.ID);
```

- [ ] **Step 3: log 簽名擴充**——Edit：

old:
```csharp
        private void WriteExactDecisionLog(bool solved, double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int nXps, int nChildren, int nFastPath, int unitsAssigned, double sumUs, double objective, double optSec,
            int partialOrders, int partialOnlyNewTrips, int odCount, bool odFired,
            int sunkOrdersStar, int sunkItemsStar, int sunkUnitsFinal, int newTripsFinal,
            double solve1Sec, double solve2Sec)
```
new:
```csharp
        private void WriteExactDecisionLog(bool solved, double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int nXps, int nChildren, int nFastPath, int unitsAssigned, double sumUs, double objective, double optSec,
            int partialOrders, int partialOnlyNewTrips, int odCount, bool odFired,
            int sunkOrdersStar, int sunkItemsStar, int sunkUnitsFinal, int newTripsFinal,
            double solve1Sec, double solve2Sec,
            int icWhole, int icPackOcc, int icPackBudget, int icProcUnits,
            int icEpSum, int icShortfall, int icSgOpen, int icGateForegone)
```

- [ ] **Step 4: header**——Edit：

old:
```csharp
                _exactDecisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsInModel,xps,children,fastPath,units,sumUs,objective,solveSec,partialOrders,partialOnlyNewTrips,odCount,odFired,sunkOrdersStar,sunkItemsStar,sunkUnitsFinal,newTripsFinal,solve1Sec,solve2Sec");
```
new:
```csharp
                _exactDecisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsInModel,xps,children,fastPath,units,sumUs,objective,solveSec,partialOrders,partialOnlyNewTrips,odCount,odFired,sunkOrdersStar,sunkItemsStar,sunkUnitsFinal,newTripsFinal,solve1Sec,solve2Sec,icWhole,icPackOcc,icPackBudget,icProcUnits,icEpSum,icShortfall,icSgOpen,icGateForegone");
```

- [ ] **Step 5: 值列**——Edit：

old:
```csharp
                solve1Sec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                solve2Sec.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
            _exactDecisionIndex++;
```
new:
```csharp
                solve1Sec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                solve2Sec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                icWhole.ToString(),
                icPackOcc.ToString(),
                icPackBudget.ToString(),
                icProcUnits.ToString(),
                icEpSum.ToString(),
                icShortfall.ToString(),
                icSgOpen.ToString(),
                icGateForegone.ToString()
            }));
            _exactDecisionIndex++;
```

- [ ] **Step 6: 呼叫端**——三個 Edit。

Edit ①（`_optSec` 後插共用計算）：

old:
```csharp
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;
            if (wrapper.HasSolution())
```
new:
```csharp
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;
            // (IC) probe values for the decision log
            int icLogPackCap = _icConfig != null ? _icConfig.PackingBufferCapacity : 0;
            int icLogPackOcc = Instance.PackingBuffer != null ? Instance.PackingBuffer.AliveParentCount : 0;
            int icLogPackBudget = icLogPackCap > 0 ? M2eICMath.PackingBudget(icLogPackCap, icLogPackOcc) : -1;
            if (wrapper.HasSolution())
```

Edit ②（solved 呼叫端）：

old:
```csharp
                WriteExactDecisionLog(true, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    IsdeVarNamexps.Count, nChildren, nFastPath, unitsAssigned, sumUs, wrapper.GetObjectiveValue(), _optSec,
                    partialOrders, partialOnlyNewTrips, odCount, odFired, sunkOrdersStar, sunkItemsStar,
                    sunkUnitsFinal, newTripsFinal, solve1Sec, solve2Sec);
```
new:
```csharp
                int icWholeCount = icWholeVarNames.Count(n => Math.Round(variablesBinary[n].GetValue()) != 0);
                int icEpSum = 0;
                if (_icConfig != null && _icConfig.MultiPartPenalty != 0)
                    foreach (var icOrd in pendingOrders)
                        icEpSum += (int)Math.Round(variablesUs["icepx_" + icOrd.ID.ToString()].GetValue());
                int icShortfallSum = icShortfallNames.Count == 0 ? 0
                    : icShortfallNames.Sum(n => (int)Math.Round(variablesUs[n].GetValue()));
                WriteExactDecisionLog(true, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    IsdeVarNamexps.Count, nChildren, nFastPath, unitsAssigned, sumUs, wrapper.GetObjectiveValue(), _optSec,
                    partialOrders, partialOnlyNewTrips, odCount, odFired, sunkOrdersStar, sunkItemsStar,
                    sunkUnitsFinal, newTripsFinal, solve1Sec, solve2Sec,
                    icWholeCount, icLogPackOcc, icLogPackBudget, result.ProcessingUnitCount,
                    icEpSum, icShortfallSum, icSgOpenCount, icGateForegone);
```

Edit ③（unsolved 呼叫端）：

old:
```csharp
                WriteExactDecisionLog(false, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    0, 0, 0, 0, 0.0, double.NaN, _optSec, 0, 0, odCount, odFired,
                    sunkOrdersStar, sunkItemsStar, 0, 0, solve1Sec, solve2Sec);
```
new:
```csharp
                WriteExactDecisionLog(false, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    0, 0, 0, 0, 0.0, double.NaN, _optSec, 0, 0, odCount, odFired,
                    sunkOrdersStar, sunkItemsStar, 0, 0, solve1Sec, solve2Sec,
                    0, icLogPackOcc, icLogPackBudget, 0, 0, 0, icSgOpenCount, icGateForegone);
```

- [ ] **Step 7: Build 全 solution + tests**。Expected: 0 error、全 `[PASS]`。

- [ ] **Step 8: Commit**

```powershell
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs
git commit -m "feat(m2e-ic): packing budget (icPK), decode-time box reservation, v4 probe log columns"
```

---

### Task 7: 實驗 xconf（v4 主臂 / gate-loose / pk78）

**Files:**
- Create: `Material/Instances/CoreBenchmark/small/split_milp_m2eic.xconf`
- Create: `Material/Instances/CoreBenchmark/small/split_milp_m2eic_gl.xconf`
- Create: `Material/Instances/CoreBenchmark/small/split_milp_m2eic_pk78.xconf`

- [ ] **Step 1: 主臂**：

```powershell
Copy-Item Material\Instances\CoreBenchmark\small\split_milp_m2ea.xconf Material\Instances\CoreBenchmark\small\split_milp_m2eic.xconf
```
三個 Edit（**只准動這些行**）：

Edit ①：old `  <Name>split_milp_m2ea</Name>` → new `  <Name>split_milp_m2eic</Name>`
Edit ②：old `  <OrderBatchingConfig xsi:type="SplitM1GExactConfiguration">` → new `  <OrderBatchingConfig xsi:type="SplitM2eICConfiguration">`
Edit ③（權重整組 + v4 欄位；序列化順序 = 宣告順序，衍生欄位必須在 base 欄位之後）：

old:
```xml
    <IdleSlotWeight>0</IdleSlotWeight>
  </OrderBatchingConfig>
```
new:
```xml
    <IdleSlotWeight>5</IdleSlotWeight>
    <PodTripFixedCost>10</PodTripFixedCost>
    <UnitDrawReward>-0.5</UnitDrawReward>
    <PackingBufferCapacity>0</PackingBufferCapacity>
    <MultiPartPenalty>12</MultiPartPenalty>
    <PipelineFloorWeight>20</PipelineFloorWeight>
    <PipelineFloorLeadSec>70</PipelineFloorLeadSec>
    <PipelineFloorTarget>1</PipelineFloorTarget>
    <PipelineFloorEnabled>true</PipelineFloorEnabled>
    <SplitGateEnabled>true</SplitGateEnabled>
    <SplitGateStrict>true</SplitGateStrict>
    <CoverageRewardWeight>-0.2</CoverageRewardWeight>
  </OrderBatchingConfig>
```

- [ ] **Step 2: gate-loose 臂**：

```powershell
Copy-Item Material\Instances\CoreBenchmark\small\split_milp_m2eic.xconf Material\Instances\CoreBenchmark\small\split_milp_m2eic_gl.xconf
```
Edit ①：Name → `split_milp_m2eic_gl`；Edit ②：old `    <SplitGateStrict>true</SplitGateStrict>` → new `    <SplitGateStrict>false</SplitGateStrict>`

- [ ] **Step 3: pk78 臂**：

```powershell
Copy-Item Material\Instances\CoreBenchmark\small\split_milp_m2eic.xconf Material\Instances\CoreBenchmark\small\split_milp_m2eic_pk78.xconf
```
Edit ①：Name → `split_milp_m2eic_pk78`；Edit ②：old `    <PackingBufferCapacity>0</PackingBufferCapacity>` → new `    <PackingBufferCapacity>78</PackingBufferCapacity>`

- [ ] **Step 4: byte-discipline 驗證**：

```powershell
Compare-Object (Get-Content Material\Instances\CoreBenchmark\small\split_milp_m2ea.xconf) (Get-Content Material\Instances\CoreBenchmark\small\split_milp_m2eic.xconf) | Format-Table -AutoSize
```
Expected: 差異僅 Name、xsi:type、IdleSlotWeight 3 對行 + 12 行新增。m2eic vs _gl：Name + SplitGateStrict 各 1 對。m2eic vs _pk78：Name + PackingBufferCapacity 各 1 對。

- [ ] **Step 5: Commit**

```powershell
git add Material/Instances/CoreBenchmark/small/split_milp_m2eic.xconf Material/Instances/CoreBenchmark/small/split_milp_m2eic_gl.xconf Material/Instances/CoreBenchmark/small/split_milp_m2eic_pk78.xconf
git commit -m "feat(m2e-ic): v4 xconf arms (strict gate main, gate-loose, packing-78)"
```

---

### Task 8: 驗證 gates（build / tests / 禁區 / 中立性 / 煙霧 / 報告）

- [ ] **Gate 1**：全 solution build 0 error；tests exe 全 `[PASS]`。
- [ ] **Gate 2（禁區未動）**：`git status --porcelain`——`M1GManager.cs`/`HADGSManager.cs`/`PVGSManager.cs` 不得出現；`SplitM1GExactManager.cs` 的 diff 與計畫開始前相同（`git diff <file> | git hash-object --stdin` 前後比對）。
- [ ] **Gate 3（引擎中立性）**：重跑 `split_milp_m2ea.xconf` seed0（`output_gate3_m2ea_s0`），與既有 `output_acc_m2ea_s0` 比對 `footprint.csv` 全部欄位（排除 RealTimeUsed/MemoryUsedMax/Timing* 牆鐘欄）：**必須完全一致**（PackingBuffer 為 null → OutputStation 兩個新掛點恆不觸發）。參考目錄若不存在，改記六核心統計對照 memory（M2e seed0 TP=657）並回報。
- [ ] **Gate 4（三臂煙霧 seed0 7200s）**——建 `run_ic_smoke.cmd`：

```
@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo IC-V4 seed0 >> run_ic_smoke.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic.xconf" "output_ic_v4_s0" 0 >> run_ic_smoke_runs.log 2>&1
echo IC-GL seed0 >> run_ic_smoke.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_gl.xconf" "output_ic_gl_s0" 0 >> run_ic_smoke_runs.log 2>&1
echo IC-PK78 seed0 >> run_ic_smoke.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_pk78.xconf" "output_ic_pk78_s0" 0 >> run_ic_smoke_runs.log 2>&1
echo IC-SMOKE-DONE >> run_ic_smoke.log
```
`Start-Process -FilePath cmd.exe -ArgumentList '/c','run_ic_smoke.cmd' -WindowStyle Hidden`，Monitor tail `run_ic_smoke.log` 等 `IC-SMOKE-DONE`。

**通過判準（三臂皆須）**：
1. 三次 `.Fin. - SUCCESS`；**任何 `P1 violated` 例外 = 立即 STOP 回報**（模型/decoder 失配的判決性證據）。
2. `splitm2eic_decision_log.csv` 含 8 個 ic 欄；`icWhole` 總和 > 0；`children` > 0 且多數決策 `icSgOpen` 有變化（gate 真的在動）；`icShortfall` 出現非零（floor 真的觸發過）。
3. whole 佔比（fastPath/(fastPath+children)）> 50%（「完成主旋律」的直接驗證）；`icEpSum` 低位（wp 壓制多站碎裂）。
4. KPI sanity：orders handled ≥ 600（低於此 = 機制問題，STOP 回報而非調參）。
5. pk78 臂：`icPackBudget` 非 -1、記錄最小值（binding 與否照實報）。
6. gl vs strict：`icGateForegone`（strict 臂）與兩臂 pile-on/consolidation wait 對照——SG 模式的機會成本第一手數據。
- [ ] **Gate 5（機制報告）**：彙整表——IC-v4 / IC-gl / IC-pk78 / 參考臂（acc_m2ea 657、acc_pvgse 658 seed0）的 TP、PO、IPO、orders late、consolidation wait（splitorders.csv）、pod visits、駐留、whole 佔比、8 個 ic probe 統計、solveSec 中位/p95/max。**明確標注**：單 seed 僅驗證機制；5-seed 與權重掃描（wp/w3'/w_pipe/Lead/SG）待使用者拍板。
- [ ] 收尾：刪或明示留存 `run_ic_smoke.cmd`；不 commit 輸出目錄。

---

## Self-Review 紀錄

- **Spec 覆蓋**：P1→T4；SG（strict/loose config）→T4/T7；D9→T4(約束)+T5(目標)；D11→T5；D14 cpool→T5；D15 稀缺 ε→T5；PK→T6；D16 觸發→T2；四狀態 partition→T4；probe 欄→T6；xconf 臂→T7；gates→T8。目標式其餘（w2/w3'/w4/w1）走既有程式路徑、僅 xconf 值（T7）。
- **命名一致性**：`icwholex_`（T4 定義、T6 PK/log 引用）、`icepx_`（T4 約束、T5 目標、T6 log）、`icsfx_`（T5 定義、T6 log 經 `icShortfallNames`）、`M2eICMath` 8 簽名（T1↔T4/T5/T6）、`PackingBuffer` API（T2↔T3/T6/OutputStation）——已互相核對。
- **佔位符**：無 TBD；所有 Edit 附逐字 old/new；指令附預期輸出。
