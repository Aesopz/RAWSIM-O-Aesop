# M2e-IC (Inbound-Committed Split) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增 `SplitM2eICManager`（M2e 鏡像 + P1 入站限定閘門 + packing 預算），使拆單 child 只能綁在 committed pod（processing/inbound），unused pod 只服務 whole 訂單；可選 packing cap（Xie2021 附錄 B, C=78）。

**Spec:** `docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md`（**已含 planning 階段修正**：P1b big-M 形式、P1oos、packing 預留改 decode 時登記、無 back-pressure 牆——以 spec 現行版為準，不要照舊草稿實作）。

**Architecture:** `SplitM2eICManager` = `SplitM1GExactManager.cs` 的腳本化拷貝＋改名，之後在同一組 Gurobi model 內加 `icwholex_o`/`ypackx_o` 變數與 icP1a/b/c/oos/d、icPK1a/b/c/PK2 限制式。目標式**零改動**（Pa 收費/Pb 免費的不對稱已存在）。引擎側僅：`PackingBuffer` 新元件 + `Instance` 一個屬性 + `OutputStation` 整併分支一個 null-safe 釋放呼叫。

**Tech Stack:** C# 7.3 / .NET Framework 4.8 / Gurobi (win64) / 手寫 TestRunner（無 NuGet）。

## Global Constraints

- **絕對不改**：`M1GManager.cs`、`HADGSManager.cs`、`SplitM1GExactManager.cs`、`SplitM1GManager.cs`、`SplitM1GLBManager.cs`、`PVGSManager.cs`。
- 保留工作樹中既有的未提交修改（sunk/adaptive 遺留）；**禁止 `git add -A` / reset / checkout**——只 `git add` 本計畫明列的檔案。
- C# 7.3（net48 legacy csproj）：新 .cs 檔**必須**手動加 `<Compile Include>`；lambda 內不可捕獲 out 參數。
- Build（solution）：
  `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:m /nologo`
- Build（tests）：
  `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimO.Tests\RAWSimO.Tests.csproj /p:Platform=x64 /p:Configuration=Release /v:m /nologo`
- 測試執行：build 後跑 `RAWSimO.Tests\bin\x64\Release\RAWSimO.Tests.exe`（exit code 0 = 全過）。
- 模擬 CLI：`RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe <xlayo> <xsett> <xconf> <outputDir> <seed>`。
- 長跑（>10 分鐘）用 `.cmd` + `Start-Process -WindowStyle Hidden` 脫離 harness，`Monitor` tail log。
- 每個 Task 結束 commit（只 add 該 Task 檔案），訊息附：
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` 與 `Claude-Session: https://claude.ai/code/session_01WmjToo7rFjj6CnXLak7W8f`
- **範圍外（勿做）**：PVGS 的 buffer decode-guard（後續 plan）、5-seed 驗收長跑（單 seed 機制成立後由使用者拍板）、back-pressure 牆（spec 已作廢）。

---

### Task 1: M2eICMath 純函數 helper + 單元測試

**Files:**
- Create: `RAWSimO.Core/Control/Defaults/OrderBatching/M2eICMath.cs`
- Create: `RAWSimO.Tests/M2eICMathTests.cs`
- Modify: `RAWSimO.Core/RAWSimO.Core.csproj`（Compile include）
- Modify: `RAWSimO.Tests/RAWSimO.Tests.csproj`（Compile include）
- Modify: `RAWSimO.Tests/Program.cs`（Register）

**Interfaces:**
- Produces（Task 4/5 依賴的精確簽名）:
  - `static int M2eICMath.PaDrawBigM(IEnumerable<int> residualUnits)`
  - `static bool M2eICMath.IsWholeEligible(bool isSplitParent, bool hasInvisibleResidualSku)`
  - `static int M2eICMath.PackingBudget(int capacity, int aliveParents)`
  - `static bool M2eICMath.SplitOrderDrawsFromStorage(bool tookSplitPath, int paUnitsDrawn)`

- [ ] **Step 1: 先寫測試（紅）**——建立 `RAWSimO.Tests/M2eICMathTests.cs`，內容逐字：

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
        }
    }
}
```

- [ ] **Step 2: 掛進測試工程**——`RAWSimO.Tests/RAWSimO.Tests.csproj` 用 Edit：

old_string:
```xml
    <Compile Include="M2eSunkFirstMathTests.cs" />
```
new_string:
```xml
    <Compile Include="M2eSunkFirstMathTests.cs" />
    <Compile Include="M2eICMathTests.cs" />
```

`RAWSimO.Tests/Program.cs` 用 Edit：

old_string:
```csharp
            M2eAdaptiveExactMathTests.Register();
            return TestRunner.RunAll();
```
new_string:
```csharp
            M2eAdaptiveExactMathTests.Register();
            M2eICMathTests.Register();
            return TestRunner.RunAll();
```

- [ ] **Step 3: 確認紅**——build tests csproj。Expected: **FAIL**（CS0103/CS0246：`M2eICMath` 不存在）。編譯失敗即本 runner 的紅燈。

- [ ] **Step 4: 實作**——建立 `RAWSimO.Core/Control/Defaults/OrderBatching/M2eICMath.cs`，內容逐字：

```csharp
using System;
using System.Collections.Generic;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure arithmetic helpers for the M2e-IC (inbound-committed split) gate and its
    /// downstream packing budget. Spec:
    /// docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md.
    /// </summary>
    public static class M2eICMath
    {
        /// <summary>
        /// Big-M for the icP1d gate: the most units order o could possibly draw from
        /// storage-area (Pa) pods this solve = its total remaining demand.
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
        /// whole[o] eligibility (icP1c/icP1oos): an existing split parent, or an order with
        /// any out-of-stock residual SKU (invisible to supply), can never be whole - such an
        /// order would decode into split children while P1 still permitted it storage-area
        /// draws, violating the core inbound-committed rule.
        /// </summary>
        public static bool IsWholeEligible(bool isSplitParent, bool hasInvisibleResidualSku)
        {
            return !isSplitParent && !hasInvisibleResidualSku;
        }

        /// <summary>
        /// Remaining packing budget for NEW split parents this solve (icPK2 right-hand
        /// side). capacity &lt;= 0 = unlimited (int.MaxValue sentinel; the PK block is
        /// skipped entirely in that case).
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
    }
}
```

`RAWSimO.Core/RAWSimO.Core.csproj` 用 Edit：

old_string:
```xml
    <Compile Include="Control\Defaults\OrderBatching\M2eSunkFirstMath.cs" />
```
new_string:
```xml
    <Compile Include="Control\Defaults\OrderBatching\M2eSunkFirstMath.cs" />
    <Compile Include="Control\Defaults\OrderBatching\M2eICMath.cs" />
```

- [ ] **Step 5: 綠**——build tests csproj 後執行 `RAWSimO.Tests\bin\x64\Release\RAWSimO.Tests.exe`。Expected: 全部 `[PASS]`、exit 0（既有測試 + 新 13 條）。

- [ ] **Step 6: Commit**

```powershell
git add RAWSimO.Core/Control/Defaults/OrderBatching/M2eICMath.cs RAWSimO.Tests/M2eICMathTests.cs RAWSimO.Core/RAWSimO.Core.csproj RAWSimO.Tests/RAWSimO.Tests.csproj RAWSimO.Tests/Program.cs
git commit -m "feat(m2e-ic): pure math helpers for P1 gate and packing budget"
```
（附 Global Constraints 規定的 trailers。）

---

### Task 2: PackingBuffer 元件 + Instance 屬性 + OutputStation 釋放掛點 + 測試

**Files:**
- Create: `RAWSimO.Core/Elements/PackingBuffer.cs`
- Create: `RAWSimO.Tests/PackingBufferTests.cs`
- Modify: `RAWSimO.Core/InstanceCore.cs`（掛屬性）
- Modify: `RAWSimO.Core/Elements/OutputStation.cs`（整併分支 +1 個 null-safe 呼叫）
- Modify: `RAWSimO.Core/RAWSimO.Core.csproj`、`RAWSimO.Tests/RAWSimO.Tests.csproj`、`RAWSimO.Tests/Program.cs`

**Interfaces:**
- Produces:
  - `class RAWSimO.Core.Elements.PackingBuffer`，ctor `PackingBuffer(int capacity)`
  - `int Capacity { get; }`、`int AliveParentCount { get; }`
  - `bool RegisterParent(int orderId)`（idempotent；新登記回 true）
  - `bool ReleaseParent(int orderId)`（idempotent、unknown-safe；有釋放回 true）
  - `Instance.PackingBuffer`（`Elements.PackingBuffer`，預設 null——**null 即所有非 IC manager 完全不受影響**）

- [ ] **Step 1: 先寫測試（紅）**——建立 `RAWSimO.Tests/PackingBufferTests.cs`，內容逐字：

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

掛進 `RAWSimO.Tests/RAWSimO.Tests.csproj`（Edit）：

old_string:
```xml
    <Compile Include="M2eICMathTests.cs" />
```
new_string:
```xml
    <Compile Include="M2eICMathTests.cs" />
    <Compile Include="PackingBufferTests.cs" />
```

`RAWSimO.Tests/Program.cs`（Edit）：

old_string:
```csharp
            M2eICMathTests.Register();
            return TestRunner.RunAll();
```
new_string:
```csharp
            M2eICMathTests.Register();
            PackingBufferTests.Register();
            return TestRunner.RunAll();
```

- [ ] **Step 2: 確認紅**——build tests csproj。Expected: **FAIL**（`PackingBuffer` 不存在）。

- [ ] **Step 3: 實作元件**——建立 `RAWSimO.Core/Elements/PackingBuffer.cs`，內容逐字：

```csharp
using System.Collections.Generic;

namespace RAWSimO.Core.Elements
{
    /// <summary>
    /// Global downstream consolidation buffer for split orders (Xie et al. 2021, Appendix
    /// B: 78 boxes per shelf). ONE box per SPLIT PARENT order: reserved when the parent is
    /// first split (decode time - conservative, earlier than the physical first-part
    /// arrival, so the budget can never overshoot), released when the parent consolidates
    /// (all children completed). Capacity &lt;= 0 = unlimited (the buffer then only tracks
    /// occupancy for probes). Null on Instance unless an IC-family order manager
    /// instantiates it - every other manager is untouched by construction.
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

`RAWSimO.Core/RAWSimO.Core.csproj`：先 Grep 確認 `Elements\OutputStation.cs` 的 include 行存在，然後 Edit：

old_string:
```xml
    <Compile Include="Elements\OutputStation.cs" />
```
new_string:
```xml
    <Compile Include="Elements\OutputStation.cs" />
    <Compile Include="Elements\PackingBuffer.cs" />
```

- [ ] **Step 4: Instance 屬性**——`RAWSimO.Core/InstanceCore.cs` 用 Edit（錨點 = 53-57 行）：

old_string:
```csharp
        /// <summary>
        /// The configuration for all controlling mechanisms.
        /// </summary>
        public ControlConfiguration ControllerConfig { get; set; }
```
new_string:
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

- [ ] **Step 5: OutputStation 釋放掛點**——`RAWSimO.Core/Elements/OutputStation.cs` 用 Edit（錨點 = `RemoveAnyCompletedOrder` 內 348-353 行）：

old_string:
```csharp
                        if (parent.NotifyChildCompleted(finishedOrder))
                        {
                            parent.TimeStampCompleted = currentTime;
                            Instance.ItemManager.CompleteOrder(parent);
                            Instance.NotifyOrderCompleted(parent, this);
                        }
```
new_string:
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

- [ ] **Step 6: 綠**——build **solution**（engine 檔動了，要全建）＋跑 tests exe。Expected: build 成功、全 `[PASS]`。

- [ ] **Step 7: Commit**

```powershell
git add RAWSimO.Core/Elements/PackingBuffer.cs RAWSimO.Tests/PackingBufferTests.cs RAWSimO.Core/InstanceCore.cs RAWSimO.Core/Elements/OutputStation.cs RAWSimO.Core/RAWSimO.Core.csproj RAWSimO.Tests/RAWSimO.Tests.csproj RAWSimO.Tests/Program.cs
git commit -m "feat(m2e-ic): PackingBuffer component with consolidation release hook (inert unless instantiated)"
```

---

### Task 3: Config / enum / XmlInclude / Controller case + Manager 鏡像骨架

**Files:**
- Modify: `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`（新 config 類）
- Modify: `RAWSimO.Core/Configurations/MethodConfiguration.cs`（enum + XmlInclude）
- Modify: `RAWSimO.Core/Control/Controller.cs`（case）
- Create: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`（腳本化拷貝＋改名）
- Modify: `RAWSimO.Core/RAWSimO.Core.csproj`

**Interfaces:**
- Consumes: Task 2 的 `Instance.PackingBuffer` / `PackingBuffer(int)`。
- Produces: `SplitM2eICConfiguration : SplitM1GExactConfiguration`（新欄位 `int PackingBufferCapacity = 0`）；`OrderBatchingMethodType.SplitM2eIC`；token `OBSPLITM2EIC`；`SplitM2eICManager` 私有欄位 `_icConfig`（Task 5 讀 `PackingBufferCapacity` 用）。

- [ ] **Step 1: Config 類**——`MethodConfigurationsOB.cs` 用 Edit，插在 `SplitM1GLBConfiguration` 的 doc comment 前：

old_string:
```csharp
    /// <summary>
    /// Late-binding variant of SplitM1GExact (M2e-LB): identical MILP, but AllocateOrder is
```
new_string:
```csharp
    /// <summary>
    /// M2e-IC (Inbound-Committed Split): SplitM1GExact plus the P1 gate - split orders may
    /// only draw from committed pods (processing + inbound, Pb); storage-area pods (Pa)
    /// may only serve WHOLE orders (single station, completed this solve, no out-of-stock
    /// residual, not an existing split parent). Optional downstream packing budget: one
    /// box per split parent, reserved at first split, released at consolidation (Xie et
    /// al. 2021, Appendix B). Spec:
    /// docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md.
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
        /// Downstream packing buffer capacity C (Xie et al. 2021 Appendix B derives 78
        /// boxes per shelf). One box per split parent from its first split until
        /// consolidation; the MILP may open new split parents only within the remaining
        /// budget. &lt;= 0 = unlimited buffer (constraint absent; Xie's base-model
        /// assumption) - the bit-identical default.
        /// </summary>
        public int PackingBufferCapacity = 0;
    }

    /// <summary>
    /// Late-binding variant of SplitM1GExact (M2e-LB): identical MILP, but AllocateOrder is
```

- [ ] **Step 2: enum**——`MethodConfiguration.cs` 用 Edit：

old_string:
```csharp
        /// <summary>
        /// Late-binding SplitM1GExact (M2e-LB): same MILP, AllocateOrder deferred to pod-claim
        /// time via a deferred-binding ledger; PlannedWipCap replaces physical Cs semantics.
        /// </summary>
        SplitM1GLB,
    }
```
new_string:
```csharp
        /// <summary>
        /// Late-binding SplitM1GExact (M2e-LB): same MILP, AllocateOrder deferred to pod-claim
        /// time via a deferred-binding ledger; PlannedWipCap replaces physical Cs semantics.
        /// </summary>
        SplitM1GLB,
        /// <summary>
        /// M2e-IC (Inbound-Committed Split): SplitM1GExact plus the P1 gate (split orders
        /// draw only from committed pods; storage-area pods serve whole orders only) and an
        /// optional per-split-parent downstream packing budget (Xie et al. 2021, Appx B).
        /// </summary>
        SplitM2eIC,
    }
```

- [ ] **Step 3: XmlInclude**——`MethodConfiguration.cs` 用 Edit：

old_string:
```csharp
    [XmlInclude(typeof(PVGSConfiguration))]
    public abstract class OrderBatchingConfiguration : ControllerConfigurationBase
```
new_string:
```csharp
    [XmlInclude(typeof(PVGSConfiguration))]
    [XmlInclude(typeof(SplitM2eICConfiguration))]
    public abstract class OrderBatchingConfiguration : ControllerConfigurationBase
```

- [ ] **Step 4: Controller case**——`Controller.cs` 用 Edit：

old_string:
```csharp
                case OrderBatchingMethodType.SplitM1GLB: OrderManager = new SplitM1GLBManager(instance); break;
```
new_string:
```csharp
                case OrderBatchingMethodType.SplitM1GLB: OrderManager = new SplitM1GLBManager(instance); break;
                case OrderBatchingMethodType.SplitM2eIC: OrderManager = new SplitM2eICManager(instance); break;
```

- [ ] **Step 5: 鏡像拷貝（腳本化，不手抄）**——PowerShell 逐字執行：

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

驗證：`Select-String -Path $f -Pattern 'class SplitM2eICManager'` 命中 1 行；`Select-String -Path $f -Pattern 'class SplitM1GExactManager'` 命中 0 行。

- [ ] **Step 6: ctor 加 IC 欄位/guard/buffer**——`SplitM2eICManager.cs` 用 Edit：

old_string:
```csharp
        public SplitM2eICManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GExactConfiguration;
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }
```
new_string:
```csharp
        public SplitM2eICManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GExactConfiguration;
            _icConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM2eICConfiguration;
            // (IC) experimental M2e arms re-shape the objective or the solve structure;
            // their interaction with the P1 gate is undefined - fail fast, don't reason it out.
            if (_splitConfig != null && (_splitConfig.AdaptiveExactResweeps || _splitConfig.SunkFirstScoring
                || _splitConfig.CoverageFirstScoring || _splitConfig.TrueCompletionReward > 0))
                throw new InvalidOperationException("SplitM2eIC does not support AdaptiveExactResweeps/SunkFirstScoring/CoverageFirstScoring/TrueCompletionReward.");
            // (IC) global downstream packing buffer. Probe tracking is always on; the PK
            // budget constraint additionally requires PackingBufferCapacity > 0.
            if (instance.PackingBuffer == null)
                instance.PackingBuffer = new PackingBuffer(_icConfig != null ? _icConfig.PackingBufferCapacity : 0);
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }

        /// <summary>The IC-specific config (PackingBufferCapacity lives here).</summary>
        private SplitM2eICConfiguration _icConfig;
```

- [ ] **Step 7: 類別 doc comment 更新**——`SplitM2eICManager.cs` 用 Edit（拷貝來的舊註解會誤導）：

old_string:
```csharp
    /// <summary>
    /// MILP-based order-splitting manager with pod-level attribution decided inside the model:
    /// q[i,o,p,s] (4D) replaces SplitM1G's q[i,o,s] (3D), so the solver itself picks which pod
    /// serves each unit instead of a post-solve greedy pass. Independent sibling of
    /// SplitM1GManager (Spec 2) - does not inherit it, mirrors M1GManager directly.
    /// See docs/superpowers/specs/2026-07-07-splitm1g-exact-design.md.
    /// </summary>
```
new_string:
```csharp
    /// <summary>
    /// M2e-IC (Inbound-Committed Split): faithful mirror of SplitM1GExactManager (4D
    /// q[i,o,p,s] MILP) plus the P1 gate - split orders may only draw from committed pods
    /// (Pb = processing + inbound); storage-area pods (Pa) may only serve WHOLE orders -
    /// and an optional downstream packing budget (one box per split parent, Xie et al.
    /// 2021 Appx B). Mirrors M1GManager-family conventions; SplitM1GExactManager itself
    /// stays untouched as the M2e ablation baseline.
    /// See docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md.
    /// </summary>
```

- [ ] **Step 8: csproj include**——`RAWSimO.Core/RAWSimO.Core.csproj` 用 Edit：

old_string:
```xml
    <Compile Include="Control\Defaults\OrderBatching\SplitM1GExactManager.cs" />
```
new_string:
```xml
    <Compile Include="Control\Defaults\OrderBatching\SplitM1GExactManager.cs" />
    <Compile Include="Control\Defaults\OrderBatching\SplitM2eICManager.cs" />
```

- [ ] **Step 9: Build 全 solution**。Expected: 成功、0 error。（此時 SplitM2eICManager 行為 = M2e 完全鏡像 + guard + buffer 掛載，P1/PK 尚未加。）

- [ ] **Step 10: Commit**

```powershell
git add RAWSimO.Core/Configurations/MethodConfigurationsOB.cs RAWSimO.Core/Configurations/MethodConfiguration.cs RAWSimO.Core/Control/Controller.cs RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs RAWSimO.Core/RAWSimO.Core.csproj
git commit -m "feat(m2e-ic): SplitM2eICManager mirror skeleton + config/enum/controller plumbing"
```

---

### Task 4: P1 入站限定閘門（icP1a/b/c/oos/d）+ decode 硬 assert

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`

**Interfaces:**
- Consumes: Task 1 `M2eICMath.IsWholeEligible` / `PaDrawBigM` / `SplitOrderDrawsFromStorage`。
- Produces: `icwholex_<orderId>` binary 變數（Task 5 的 PK1a/c 與 log 計數引用**同一命名**）；`List<string> icWholeVarNames`（`SolveSplitExact` 區域變數，Task 5 讀）。

- [ ] **Step 1: processingPods 無條件蒐集**（IC 的 probe 欄位需要它；鏡像原版只在 w5/adaptive 時蒐集）——Edit：

old_string:
```csharp
            HashSet<Pod> processingPods = new HashSet<Pod>();
            if (w5 != 0 || adaptiveExact)
                foreach (var station in Cs.Keys)
```
new_string:
```csharp
            HashSet<Pod> processingPods = new HashSet<Pod>();
            // (IC) always collected: the icProcUnits probe column reports units drawn from
            // the pod physically being processed (mirror deviation - original gates on w5).
            if (true)
                foreach (var station in Cs.Keys)
```

- [ ] **Step 2: P1 限制式區塊**——插在唯一錨點 `// Order-first control:` 註解之前。Edit：

old_string:
```csharp
            // Order-first control: the normal pass admits only z=1 complete residuals. A
```
new_string:
```csharp
            // ── (IC / P1) Inbound-committed split gate ─────────────────────────────────
            // Core rule (spec 2026-07-16): an order drawing ANY unit from a storage-area
            // pod (Pa) must be a WHOLE order - completed this solve (icP1a), at most one
            // station (icP1b, big-M form: never infeasible), not an existing split parent
            // and no out-of-stock residual SKU (icP1c/icP1oos - legacy zdonex skips OOS
            // SKUs, so without the oos leg a "complete" order could still decode into
            // split children bound to a Pa pod). Contrapositive: every split draws only
            // from committed pods (Pb = processing + inbound), killing the
            // desynchronized-sibling pathology (3781s consolidation tails).
            List<string> icWholeVarNames = new List<string>();
            int icStationCount = Cs.Count;
            foreach (var order in pendingOrders.OrderBy(o => o.ID))
            {
                string wname = "icwholex_" + order.ID.ToString();
                string zname = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                icWholeVarNames.Add(wname);
                bool icHasInvisibleResidual = residuals[order].Any(p => p.Value > 0 && !PiSKU.ContainsKey(p.Key));
                if (!M2eICMath.IsWholeEligible(order.IsSplitParent, icHasInvisibleResidual))
                {
                    // (icP1c/icP1oos) structurally never whole
                    wrapper.AddConstr(variablesBinary[wname] <= 0, "icP1c");
                }
                else
                {
                    // (icP1a) whole requires completion this solve
                    wrapper.AddConstr(variablesBinary[wname] <= variablesBinary[zname], "icP1a");
                    // (icP1b) whole=1 forces at most one station part; whole=0 is unconstrained
                    var icOrderY = deVarNamey.Where(v => v.order.ID == order.ID)
                        .Select(v => variablesBinary[v.name]).ToList();
                    if (icOrderY.Count > 0 && icStationCount > 1)
                        wrapper.AddConstr(LinearExpression.Sum(icOrderY)
                            + (icStationCount - 1) * variablesBinary[wname] <= icStationCount, "icP1b");
                }
                // (icP1d) storage-area draws only for whole orders
                var icPaQ = deVarNameq.Where(v => v.order.ID == order.ID && Pa.Contains(v.pod))
                    .Select(v => variablesQ[v.name]).ToList();
                if (icPaQ.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(icPaQ)
                        <= M2eICMath.PaDrawBigM(residuals[order].Values) * variablesBinary[wname], "icP1d");
            }
            // Order-first control: the normal pass admits only z=1 complete residuals. A
```

- [ ] **Step 3: decode 硬 assert（P1 的 ground truth）**——Edit（錨點 = split path 的 `result.SplitParents.Add(order);`，全檔唯一）：

old_string:
```csharp
                        result.SplitParents.Add(order);
```
new_string:
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

- [ ] **Step 4: Build 全 solution**。Expected: 成功、0 error（`icWholeVarNames` 暫未被讀取，屬正常——Task 5 使用）。

- [ ] **Step 5: Commit**

```powershell
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs
git commit -m "feat(m2e-ic): P1 inbound-committed gate (icP1a/b/c/oos/d) with decode-side hard assert"
```

---

### Task 5: PK packing 預算 + decode 登記 + 決策 log 探針欄位

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`

**Interfaces:**
- Consumes: Task 1 `M2eICMath.PackingBudget`；Task 2 `Instance.PackingBuffer.{AliveParentCount,RegisterParent}`；Task 3 `_icConfig.PackingBufferCapacity`；Task 4 `icwholex_` 命名與 `icWholeVarNames`。
- Produces: 決策 log 新欄位 `icWhole,icPackOcc,icPackBudget,icProcUnits`（Task 7 驗證 gate 讀取）。

- [ ] **Step 1: PK 限制式區塊**——插在 P1 區塊之後（錨點仍是 `// Order-first control:` 註解，P1 區塊已在其前）。Edit：

old_string:
```csharp
            // Order-first control: the normal pass admits only z=1 complete residuals. A
```
new_string:
```csharp
            // ── (IC / PK) Downstream packing budget ────────────────────────────────────
            // Xie et al. 2021 Appx B: C boxes (78 per shelf); ONE box per split parent
            // from first split until consolidation. Fresh orders opening a NEW split
            // parent this solve consume one box each; already-registered parents hold
            // theirs inside B_occ (registered at decode, released at consolidation).
            // Capacity <= 0 disables the whole block (unlimited buffer = Xie's base model).
            int icPackCapacity = _icConfig != null ? _icConfig.PackingBufferCapacity : 0;
            if (icPackCapacity > 0)
            {
                int icBOcc = Instance.PackingBuffer != null ? Instance.PackingBuffer.AliveParentCount : 0;
                List<string> icYpackNames = new List<string>();
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    if (order.IsSplitParent)
                        continue; // box already reserved at its first split (inside icBOcc)
                    string pname = "icypackx_" + order.ID.ToString();
                    string wname = "icwholex_" + order.ID.ToString();
                    icYpackNames.Add(pname);
                    var icPkY = deVarNamey.Where(v => v.order.ID == order.ID).ToList();
                    // (icPK1a) any station part not covered by whole opens a box
                    foreach (var y in icPkY)
                        wrapper.AddConstr(variablesBinary[pname] + variablesBinary[wname]
                            >= variablesBinary[y.name], "icPK1a");
                    // (icPK1b) no station part - no box
                    if (icPkY.Count > 0)
                        wrapper.AddConstr(variablesBinary[pname]
                            <= LinearExpression.Sum(icPkY.Select(v => variablesBinary[v.name])), "icPK1b");
                    // (icPK1c) a whole order ships directly - never enters packing
                    wrapper.AddConstr(variablesBinary[pname] + variablesBinary[wname] <= 1, "icPK1c");
                }
                // (icPK2) new split parents this solve <= remaining budget
                if (icYpackNames.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(icYpackNames.Select(n => variablesBinary[n]))
                        <= M2eICMath.PackingBudget(icPackCapacity, icBOcc), "icPK2");
            }
            // Order-first control: the normal pass admits only z=1 complete residuals. A
```

- [ ] **Step 2: decode 登記箱位**——Edit（錨點 = Task 4 加入的 assert 尾端）：

old_string:
```csharp
                        if (M2eICMath.SplitOrderDrawsFromStorage(true, icPaUnits))
                            throw new InvalidOperationException("M2e-IC: split order " + order.ID
                                + " drew " + icPaUnits + " unit(s) from storage-area pods (P1 violated).");
```
new_string:
```csharp
                        if (M2eICMath.SplitOrderDrawsFromStorage(true, icPaUnits))
                            throw new InvalidOperationException("M2e-IC: split order " + order.ID
                                + " drew " + icPaUnits + " unit(s) from storage-area pods (P1 violated).");
                        // (IC) reserve the parent's packing box at first split (idempotent for
                        // re-split parents). Conservative: reserved from commitment rather than
                        // first completed child, so the budget can never overshoot capacity.
                        if (Instance.PackingBuffer != null)
                            Instance.PackingBuffer.RegisterParent(order.ID);
```

- [ ] **Step 3: log 簽名擴充**——Edit（`WriteExactDecisionLog` 定義）：

old_string:
```csharp
        private void WriteExactDecisionLog(bool solved, double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int nXps, int nChildren, int nFastPath, int unitsAssigned, double sumUs, double objective, double optSec,
            int partialOrders, int partialOnlyNewTrips, int odCount, bool odFired,
            int sunkOrdersStar, int sunkItemsStar, int sunkUnitsFinal, int newTripsFinal,
            double solve1Sec, double solve2Sec)
```
new_string:
```csharp
        private void WriteExactDecisionLog(bool solved, double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int nXps, int nChildren, int nFastPath, int unitsAssigned, double sumUs, double objective, double optSec,
            int partialOrders, int partialOnlyNewTrips, int odCount, bool odFired,
            int sunkOrdersStar, int sunkItemsStar, int sunkUnitsFinal, int newTripsFinal,
            double solve1Sec, double solve2Sec,
            int icWhole, int icPackOcc, int icPackBudget, int icProcUnits)
```

- [ ] **Step 4: log header**——Edit：

old_string:
```csharp
                _exactDecisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsInModel,xps,children,fastPath,units,sumUs,objective,solveSec,partialOrders,partialOnlyNewTrips,odCount,odFired,sunkOrdersStar,sunkItemsStar,sunkUnitsFinal,newTripsFinal,solve1Sec,solve2Sec");
```
new_string:
```csharp
                _exactDecisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsInModel,xps,children,fastPath,units,sumUs,objective,solveSec,partialOrders,partialOnlyNewTrips,odCount,odFired,sunkOrdersStar,sunkItemsStar,sunkUnitsFinal,newTripsFinal,solve1Sec,solve2Sec,icWhole,icPackOcc,icPackBudget,icProcUnits");
```

- [ ] **Step 5: log 值列**——Edit：

old_string:
```csharp
                solve1Sec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                solve2Sec.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
            _exactDecisionIndex++;
```
new_string:
```csharp
                solve1Sec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                solve2Sec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                icWhole.ToString(),
                icPackOcc.ToString(),
                icPackBudget.ToString(),
                icProcUnits.ToString()
            }));
            _exactDecisionIndex++;
```

- [ ] **Step 6: 呼叫端計算與傳參**——兩個 call site。先在 `_optSec` 之後插入計算。Edit：

old_string:
```csharp
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;
            if (wrapper.HasSolution())
```
new_string:
```csharp
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;
            // (IC) probe values for the decision log (icWholeCount only meaningful when solved)
            int icLogPackCap = _icConfig != null ? _icConfig.PackingBufferCapacity : 0;
            int icLogPackOcc = Instance.PackingBuffer != null ? Instance.PackingBuffer.AliveParentCount : 0;
            int icLogPackBudget = icLogPackCap > 0 ? M2eICMath.PackingBudget(icLogPackCap, icLogPackOcc) : -1;
            if (wrapper.HasSolution())
```

solved 呼叫端（在 solved 分支內、`WriteExactDecisionLog(true, ...)` 之前需要 `icWholeCount`）。Edit：

old_string:
```csharp
                WriteExactDecisionLog(true, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    IsdeVarNamexps.Count, nChildren, nFastPath, unitsAssigned, sumUs, wrapper.GetObjectiveValue(), _optSec,
                    partialOrders, partialOnlyNewTrips, odCount, odFired, sunkOrdersStar, sunkItemsStar,
                    sunkUnitsFinal, newTripsFinal, solve1Sec, solve2Sec);
```
new_string:
```csharp
                int icWholeCount = icWholeVarNames.Count(n => Math.Round(variablesBinary[n].GetValue()) != 0);
                WriteExactDecisionLog(true, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    IsdeVarNamexps.Count, nChildren, nFastPath, unitsAssigned, sumUs, wrapper.GetObjectiveValue(), _optSec,
                    partialOrders, partialOnlyNewTrips, odCount, odFired, sunkOrdersStar, sunkItemsStar,
                    sunkUnitsFinal, newTripsFinal, solve1Sec, solve2Sec,
                    icWholeCount, icLogPackOcc, icLogPackBudget, result.ProcessingUnitCount);
```

unsolved 呼叫端。Edit：

old_string:
```csharp
                WriteExactDecisionLog(false, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    0, 0, 0, 0, 0.0, double.NaN, _optSec, 0, 0, odCount, odFired,
                    sunkOrdersStar, sunkItemsStar, 0, 0, solve1Sec, solve2Sec);
```
new_string:
```csharp
                WriteExactDecisionLog(false, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    0, 0, 0, 0, 0.0, double.NaN, _optSec, 0, 0, odCount, odFired,
                    sunkOrdersStar, sunkItemsStar, 0, 0, solve1Sec, solve2Sec,
                    0, icLogPackOcc, icLogPackBudget, 0);
```

- [ ] **Step 7: Build 全 solution + 跑 tests exe**。Expected: build 成功、tests 全 `[PASS]`。

- [ ] **Step 8: Commit**

```powershell
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs
git commit -m "feat(m2e-ic): packing budget (icPK1/PK2), decode-time box reservation, probe log columns"
```

---

### Task 6: 實驗 xconf（IC-struct 與 IC-pk78）

**Files:**
- Create: `Material/Instances/CoreBenchmark/small/split_milp_m2eic.xconf`
- Create: `Material/Instances/CoreBenchmark/small/split_milp_m2eic_pk78.xconf`

**Interfaces:**
- Consumes: Task 3 的 `SplitM2eICConfiguration` xsi:type 與 `PackingBufferCapacity` 欄位名。
- Produces: Task 7 gate 直接引用這兩個檔名。

- [ ] **Step 1: IC-struct（P1 on、packing ∞、w3=1000）**——腳本化拷貝＋只改允許行：

```powershell
Copy-Item Material\Instances\CoreBenchmark\small\split_milp_m2ea.xconf Material\Instances\CoreBenchmark\small\split_milp_m2eic.xconf
```
然後對 `split_milp_m2eic.xconf` 做三個 Edit（**只准動這三處**）：

Edit 1 — old_string: `  <Name>split_milp_m2ea</Name>` → new_string: `  <Name>split_milp_m2eic</Name>`

Edit 2 — old_string: `  <OrderBatchingConfig xsi:type="SplitM1GExactConfiguration">` → new_string: `  <OrderBatchingConfig xsi:type="SplitM2eICConfiguration">`

Edit 3 — old_string: `    <IdleSlotWeight>0</IdleSlotWeight>` → new_string: `    <IdleSlotWeight>1000</IdleSlotWeight>`
（spec §9 風險 3：w3 是本模型榨乾機制的驅動力，**必須 1000**、顯式寫出。）

- [ ] **Step 2: IC-pk78（+ packing cap 78）**：

```powershell
Copy-Item Material\Instances\CoreBenchmark\small\split_milp_m2eic.xconf Material\Instances\CoreBenchmark\small\split_milp_m2eic_pk78.xconf
```
兩個 Edit：

Edit 1 — old_string: `  <Name>split_milp_m2eic</Name>` → new_string: `  <Name>split_milp_m2eic_pk78</Name>`

Edit 2 — old_string:
```xml
    <IdleSlotWeight>1000</IdleSlotWeight>
  </OrderBatchingConfig>
```
new_string:
```xml
    <IdleSlotWeight>1000</IdleSlotWeight>
    <PackingBufferCapacity>78</PackingBufferCapacity>
  </OrderBatchingConfig>
```
（XmlSerializer 序列化順序：衍生類欄位在 base 欄位之後——`PackingBufferCapacity` 必須放在 OB 區塊**最後一個**元素位置，如上。）

- [ ] **Step 3: byte-discipline 驗證**（專案憲法：逐行比對非目標差異）：

```powershell
Compare-Object (Get-Content Material\Instances\CoreBenchmark\small\split_milp_m2ea.xconf) (Get-Content Material\Instances\CoreBenchmark\small\split_milp_m2eic.xconf) | Format-Table -AutoSize
```
Expected: 差異**恰好** 3 對行（Name / xsi:type / IdleSlotWeight），無其他。同樣比對 m2eic vs m2eic_pk78：差異恰好 Name 1 對 + PackingBufferCapacity 1 行新增。

- [ ] **Step 4: Commit**

```powershell
git add Material/Instances/CoreBenchmark/small/split_milp_m2eic.xconf Material/Instances/CoreBenchmark/small/split_milp_m2eic_pk78.xconf
git commit -m "feat(m2e-ic): small-instance xconfs (IC-struct w3=1000, IC-pk78 packing budget)"
```

---

### Task 7: 驗證 gates（build / tests / 中立性 / 煙霧 / 報告）

**Files:**
- Create: `run_ic_smoke.cmd`（暫時性 runner，不 commit）

- [ ] **Gate 1（build + tests）**：全 solution build 0 error；tests exe 全 `[PASS]`。

- [ ] **Gate 2（禁區未動）**：

```powershell
git status --porcelain
```
Expected: 除本計畫明列檔案（與工作樹**原有**的未提交修改：`MethodConfiguration.cs`/`MethodConfigurationsOB.cs`/`BotManagerPodSelection.cs`/`SplitM1GExactManager.cs`/`SplitM1GLBManager.cs`/csproj/Tests 等既存項）外，無新增修改。特別確認：`M1GManager.cs`、`HADGSManager.cs`、`PVGSManager.cs` **不在**清單；`SplitM1GExactManager.cs` 的 diff 與計畫開始前相同（本計畫零貢獻——可用 `git diff RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs | git hash-object --stdin` 於計畫開始/結束各算一次比對）。

- [ ] **Gate 3（引擎中立性——M2e 基準不受 OutputStation/Instance 改動影響）**：重跑 acc 基準 seed0：

```
"RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe" "Material\Instances\CoreBenchmark\small\small.xlayo" "Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett" "Material\Instances\CoreBenchmark\small\split_milp_m2ea.xconf" "output_gate3_m2ea_s0" 0
```
與既有參考輸出（`output_acc_m2ea_s0`，若目錄仍在）比對 `footprint.csv` 全部欄位，**排除**牆鐘欄位（RealTimeUsed、MemoryUsedMax、Timing* 系列）：必須完全一致。若參考目錄已不存在：改為記錄本次六個核心統計（OrdersHandled、ItemsHandled、TP、PO、IPO、DistanceTraveled）並與 memory 中 M2e seed0（TP=657）對照，回報即可（邏輯論證：`PackingBuffer` 為 null → 兩處新 code path 恆不觸發）。

- [ ] **Gate 4（IC 煙霧，兩臂 seed0 7200s）**——建立 `run_ic_smoke.cmd`：

```
@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
echo IC-STRUCT seed0 >> run_ic_smoke.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic.xconf" "output_ic_struct_s0" 0 >> run_ic_smoke_runs.log 2>&1
echo IC-PK78 seed0 >> run_ic_smoke.log
"%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100.xsett" "%SM%\split_milp_m2eic_pk78.xconf" "output_ic_pk78_s0" 0 >> run_ic_smoke_runs.log 2>&1
echo IC-SMOKE-DONE >> run_ic_smoke.log
```
啟動（脫離 harness）＋非同步等待：

```powershell
Start-Process -FilePath cmd.exe -ArgumentList '/c','run_ic_smoke.cmd' -WindowStyle Hidden
```
用 Monitor tail `run_ic_smoke.log` 等 `IC-SMOKE-DONE`。

**通過判準（兩臂皆須）**：
1. `run_ic_smoke_runs.log` 含兩次 `.Fin. - SUCCESS`；無 `InvalidOperationException`（尤其 P1 assert 訊息「P1 violated」出現 = **立即 STOP 回報**，這是模型/decoder 失配的判決性證據，不是可繞過的小事）。
2. `output_ic_struct_s0\splitm2eic_decision_log.csv` 存在、欄位含 `icWhole,icPackOcc,icPackBudget,icProcUnits`；`icWhole` 總和 > 0；`children` 總和 > 0（有拆單發生）且對應決策的 `icProcUnits` 有非零值（在場 pod 被榨的直接觀測）。
3. pk78 臂：`icPackBudget` 欄出現非 -1 值；記錄其最小值（是否 binding 的 probe——**不設通過門檻**，照實回報）。
4. KPI sanity：兩臂 `footprint.csv` 的 orders handled ≥ 600（低於此 = 機制出問題，STOP 回報而非調參）。

- [ ] **Gate 5（機制報告，不是驗收）**——彙整一張表回報使用者：IC-struct / IC-pk78 / 參考臂（acc_m2ea 657、acc_pvgse 658、M2e w3=1000 若有既存數字）seed0 的 TP、PO、IPO、orders late、consolidation wait 摘要（`splitorders.csv`）、pod visits、平均槽駐留、`icWhole`/`children`/`icProcUnits` 統計、solveSec 中位/p95/max。**明確標注**：單 seed 僅驗證機制成立；5-seed 對照（含 M2e(w3=1000) 臂做 P1 乾淨歸因，見 spec §9.3）待使用者拍板後另跑。

- [ ] **收尾**：刪除 `run_ic_smoke.cmd`/暫時 log 或明示留存；不 commit 輸出目錄。

---

## Self-Review 紀錄（plan 定稿前已跑）

- **Spec 覆蓋**：P1（含 planning 修正 icP1b/icP1oos）→ Task 4；PK/decode 登記/釋放 → Task 2+5；config/enum/token → Task 3；目標式不變 → 無 task（刻意）；§8.1 單元測試 → Task 1+2（Gurobi 綁定的約束式改由 Gate 4 煙霧 + decode assert 覆蓋，pure-function 部分在 M2eICMath）；§8.3 probes → Task 5 欄位 + Gate 4/5；xconf → Task 6；§8.2 的 PVGS buffer 臂 → **明列範圍外**（後續 plan）。
- **型別/命名一致性**：`icwholex_`（T4 定義、T5 引用）、`M2eICMath` 四個簽名（T1 定義、T4/T5 引用）、`PackingBuffer.{RegisterParent,ReleaseParent,AliveParentCount,Capacity}`（T2 定義、T3/T5 + OutputStation 引用）、log 4 新欄位（T5 定義、Gate 4 讀取）——已互相核對。
- **佔位符掃描**：無 TBD/TODO；所有 Edit 均附逐字 old/new；所有指令附預期輸出。
