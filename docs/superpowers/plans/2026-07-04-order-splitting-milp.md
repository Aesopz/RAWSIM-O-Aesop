# SplitM1G MILP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 實作拆單版 M1G MILP（`SplitM1GManager`）：放鬆 shi2 為單件級數量分配 `q[o,i,s]`，同一 Gurobi wrapper 求解，解碼為 Spec 1 enabler 的 child Order 管線。

**Architecture:** 新 config `SplitM1GConfiguration : M1GConfiguration`（引擎 type-routing 零改動，SAM1G 先例）＋新 manager `SplitM1GManager : M1GManager`（override `DecideAboutPendingOrders`，另寫 split 版 Initialize/solve，不動 base 的非 virtual 方法）。模型保留 xps/yrp/us/dops 骨架，以 `q[o,i,s]`+`y[o,s]`(+M1 的 `z[o]`) 取代 yos/yaos。解碼經純函式 `SplitMilpDecoder`（可單元測試）→ `CreateSplitChild`/`TransferExtractRequests`/`AllocateOrder`。

**Tech Stack:** C# 7.3（net48 舊式 csproj）、Gurobi via `RAWSimO.SolverWrappers.LinearModel`、自製 TestRunner（RAWSimO.Tests）。

**Spec:** `docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md`（已核可）。

## Global Constraints

- **原版一行不動**：`M1GManager.cs`、`HADGSManager.cs` 禁止任何修改（含可見性）。引擎檔（`BotTask.cs`、`BalancedBotManager.cs`、`BotManagerPodSelection.cs`）本 spec 零改動。
- **建置一律 x64 Release**（Gurobi 只有 win64；x86 會 runtime 失敗）：
  `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /m /v:m`
- **測試工程**：`& "...MSBuild.exe" RAWSimO.Tests\RAWSimO.Tests.csproj /p:Platform=x64 /p:Configuration=Release /v:m`，執行 `RAWSimO.Tests\bin\x64\Release\RAWSimO.Tests.exe`，exit code 0 = 全過。現有 15 tests 必須保持全過。
- **C# 7.3**：禁止 C# 8+ 語法（`??=`、range、switch expression…）。新 .cs 檔必須手動加進對應 csproj 的 `<Compile Include>`。
- **模型權重**：w1=1、w3=1000 沿用；w2' = `UnitRewardWeight` config 欄位 default **-40**。
- **token**：`OBSPLITM1G`；`SplitM1GConfiguration : M1GConfiguration`（繼承是引擎零改動的關鍵，不得改為直接繼承 OrderBatchingConfiguration）。
- **煙霧讀數**：CLI 跑完後讀 `<outdir>` 下 `statistics.txt` 的 `StatOverallOrdersHandled`。CLI 位置 `RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe`（若不存在，用 `Glob RAWSimO.CLI/bin/**/RAWSimO.CLI.exe` 找 x64 Release 版）。
- 工作目錄：`C:\Users\Aesop\Desktop\EE-RAWSim-O_PP`，branch `6/24`。

---

### Task 1: Baseline 快照 + Config plumbing + Manager stub

**Files:**
- Modify: `RAWSimO.Core\Configurations\MethodConfiguration.cs`（enum ~line 334 附近、XmlInclude ~line 765 附近）
- Modify: `RAWSimO.Core\Configurations\MethodConfigurationsOB.cs`（`SplitHeuristicConfiguration` 之後、`#endregion` 之前）
- Modify: `RAWSimO.Core\Control\Controller.cs`（case 區塊 ~line 120）
- Create: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitM1GManager.cs`（stub）
- Modify: `RAWSimO.Core\RAWSimO.Core.csproj`（`<Compile Include>`，放在 line 121 `SplitPlanner.cs` 之後）

**Interfaces:**
- Produces: `SplitM1GConfiguration`（欄位 `bool CrossTime`、`double UnitRewardWeight`）、`SplitM1GManager : M1GManager` 類別骨架、`OrderBatchingMethodType.SplitM1G`。後續 task 全部依賴這些名稱。

- [ ] **Step 1: 建置現況並跑 M0 baseline 快照**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /m /v:m
& .\RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett Material\Instances\CoreBenchmark\small\m1g.xconf output_splitmilp_baseline_m1g 0
Get-ChildItem -Recurse output_splitmilp_baseline_m1g -Filter statistics.txt | ForEach-Object { Select-String StatOverallOrdersHandled $_.FullName }
```

Expected: 跑完 7200s 模擬，記下 `StatOverallOrdersHandled` 數值（M0 baseline，Task 7 回歸比對用）。把數值寫進報告檔。

- [ ] **Step 2: enum + XmlInclude**

`MethodConfiguration.cs`：在 `OrderBatchingMethodType` enum 的 `SplitHeuristic,` 之後加：

```csharp
        /// <summary>
        /// The MILP-based order-splitting manager (M1G with shi2 relaxed to unit-level q[o,i,s]).
        /// </summary>
        SplitM1G,
```

在 `[XmlInclude(typeof(SplitHeuristicConfiguration))]`（line ~765）下一行加：

```csharp
    [XmlInclude(typeof(SplitM1GConfiguration))]
```

- [ ] **Step 3: SplitM1GConfiguration**

`MethodConfigurationsOB.cs`：在 `SplitHeuristicConfiguration` 類別結束的 `}` 之後、`#endregion` 之前加：

```csharp

    /// <summary>
    /// Configuration of the MILP-based order-splitting manager: M1G with shi2 relaxed to a
    /// unit-level quantity assignment q[o,i,s]. Inherits M1GConfiguration so all engine
    /// type-routing checks ("is M1GConfiguration") pass without engine changes (SAM1G precedent).
    /// See docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md.
    /// </summary>
    public class SplitM1GConfiguration : M1GConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SplitM1G; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBSPLITM1G"; }
        /// <summary>
        /// M2 (cross-time) splitting: Σs q[o,i,s] ≤ residual, leftovers stay in the backlog.
        /// If false (M1, cross-station only): Σs q[o,i,s] = residual · z[o] (all-or-nothing this epoch).
        /// </summary>
        public bool CrossTime = true;
        /// <summary>
        /// Per-unit assignment reward w2' in the objective (negative = reward). Replaces the
        /// per-order reward w2=-40 of plain M1G; -40 keeps the same magnitude per unit.
        /// </summary>
        public double UnitRewardWeight = -40;
    }
```

- [ ] **Step 4: Manager stub**

Create `RAWSimO.Core\Control\Defaults\OrderBatching\SplitM1GManager.cs`：

```csharp
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.SolverWrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using static RAWSimO.Core.Management.ResourceManager;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// MILP-based order-splitting manager: M1G with the one-station-per-order constraint (shi2)
    /// relaxed to a unit-level quantity assignment q[o,i,s]. Reuses the Spec 1 enabler pipeline
    /// (CreateSplitChild / TransferExtractRequests / consolidation) to commit the solution.
    /// The original M1GManager stays untouched and serves as the M0 ablation baseline.
    /// See docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md.
    /// </summary>
    public class SplitM1GManager : M1GManager
    {
        /// <summary>
        /// Creates a new instance of this manager.
        /// </summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public SplitM1GManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GConfiguration;
        }

        /// <summary>
        /// The split-specific config of this controller.
        /// </summary>
        private SplitM1GConfiguration _splitConfig;
    }
}
```

- [ ] **Step 5: Controller case**

`Controller.cs`：在 `case OrderBatchingMethodType.SplitHeuristic: ...` 那行之後加：

```csharp
                case OrderBatchingMethodType.SplitM1G: OrderManager = new SplitM1GManager(instance); break;
```

- [ ] **Step 6: csproj include**

`RAWSimO.Core.csproj`：在 `<Compile Include="Control\Defaults\OrderBatching\SplitPlanner.cs" />` 之後加：

```xml
    <Compile Include="Control\Defaults\OrderBatching\SplitM1GManager.cs" />
```

- [ ] **Step 7: 建置驗證**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /m /v:m
```

Expected: Build succeeded, 0 errors。

- [ ] **Step 8: Commit**

```powershell
git add RAWSimO.Core\Configurations\MethodConfiguration.cs RAWSimO.Core\Configurations\MethodConfigurationsOB.cs RAWSimO.Core\Control\Controller.cs RAWSimO.Core\Control\Defaults\OrderBatching\SplitM1GManager.cs RAWSimO.Core\RAWSimO.Core.csproj
git commit -m "feat: SplitM1G config plumbing + manager stub (OBSPLITM1G)"
```

---

### Task 2: SplitMilpDecoder（TDD）

**Files:**
- Create: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitMilpDecoder.cs`
- Create: `RAWSimO.Tests\SplitMilpDecoderTests.cs`
- Modify: `RAWSimO.Core\RAWSimO.Core.csproj`、`RAWSimO.Tests\RAWSimO.Tests.csproj`（各加一個 `<Compile Include>`）、`RAWSimO.Tests\Program.cs`（加 `SplitMilpDecoderTests.Register();`）

**Interfaces:**
- Produces:
  ```csharp
  public static class SplitMilpDecoder
  {
      // remaining: 該 order 本次 solve 快照的殘量（每 SKU 一項，數量>0）
      // perStationQuantities: 與站清單同 index 對齊的 q 值（station i 的 sku->units；可含 0 值或空 dict）
      // crossTime: false=M1（all-or-nothing 驗證）、true=M2
      // fullyAssigned: out，Σq == Σremaining（且 >0）
      // return: (stationIndex, quantities) 清單，僅含非空 part；全零 → 空清單
      public static List<KeyValuePair<int, Dictionary<ItemDescription, int>>> Decode(
          IList<KeyValuePair<ItemDescription, int>> remaining,
          IList<Dictionary<ItemDescription, int>> perStationQuantities,
          bool crossTime,
          out bool fullyAssigned)
  }
  ```
  違反不變式（超派、未知 SKU、M1 部分指派）一律 throw `InvalidOperationException`（模型/抽取 bug 的防衛，寧炸不靜默錯派）。
- Consumes: 無（純函式，不碰 Gurobi / Instance）。

- [ ] **Step 1: 寫失敗測試**

Create `RAWSimO.Tests\SplitMilpDecoderTests.cs`（風格鏡射 `SplitPlannerTests.cs`）：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core;
using RAWSimO.Core.Control.Defaults.OrderBatching;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class SplitMilpDecoderTests
    {
        private static Instance _instance = new Instance();
        private static ItemDescription Sku() { return new SimpleItemDescription(_instance); }
        private static KeyValuePair<ItemDescription, int> Q(ItemDescription i, int q)
        { return new KeyValuePair<ItemDescription, int>(i, q); }
        private static Dictionary<ItemDescription, int> D(params KeyValuePair<ItemDescription, int>[] entries)
        { return entries.ToDictionary(e => e.Key, e => e.Value); }

        public static void Register()
        {
            TestRunner.Add("Decoder_M2_Partial_TwoStations", () =>
            {
                var a = Sku(); var b = Sku();
                bool full;
                var parts = SplitMilpDecoder.Decode(
                    new[] { Q(a, 3), Q(b, 2) }.ToList(),
                    new[] { D(Q(a, 2)), D(Q(b, 1)) }.ToList(), true, out full);
                TestRunner.AssertEqual(2, parts.Count, "two non-empty parts");
                TestRunner.AssertEqual(0, parts[0].Key, "first part maps to station index 0");
                TestRunner.AssertEqual(1, parts[1].Key, "second part maps to station index 1");
                TestRunner.AssertEqual(2, parts[0].Value[a], "station 0 gets 2xA");
                TestRunner.AssertTrue(!full, "3 of 5 units assigned -> not fully assigned");
            });
            TestRunner.Add("Decoder_PrunesEmptyAndZeroStations", () =>
            {
                var a = Sku();
                bool full;
                var parts = SplitMilpDecoder.Decode(
                    new[] { Q(a, 2) }.ToList(),
                    new[] { D(), D(Q(a, 0)), D(Q(a, 2)) }.ToList(), true, out full);
                TestRunner.AssertEqual(1, parts.Count, "empty/zero stations pruned");
                TestRunner.AssertEqual(2, parts[0].Key, "surviving part keeps original station index");
                TestRunner.AssertTrue(full, "all 2 units assigned");
            });
            TestRunner.Add("Decoder_AllZero_ReturnsEmpty", () =>
            {
                var a = Sku();
                bool full;
                var parts = SplitMilpDecoder.Decode(
                    new[] { Q(a, 2) }.ToList(),
                    new[] { D(), D() }.ToList(), false, out full);
                TestRunner.AssertEqual(0, parts.Count, "nothing assigned -> empty (M1 z=0 case)");
                TestRunner.AssertTrue(!full, "not fully assigned");
            });
            TestRunner.Add("Decoder_OverAssignment_Throws", () =>
            {
                var a = Sku();
                bool full;
                TestRunner.AssertThrows<InvalidOperationException>(() =>
                    SplitMilpDecoder.Decode(new[] { Q(a, 2) }.ToList(),
                        new[] { D(Q(a, 2)), D(Q(a, 1)) }.ToList(), true, out full),
                    "assigned 3 > remaining 2 must throw");
            });
            TestRunner.Add("Decoder_UnknownSku_Throws", () =>
            {
                var a = Sku(); var ghost = Sku();
                bool full;
                TestRunner.AssertThrows<InvalidOperationException>(() =>
                    SplitMilpDecoder.Decode(new[] { Q(a, 2) }.ToList(),
                        new[] { D(Q(ghost, 1)) }.ToList(), true, out full),
                    "sku not in remaining must throw");
            });
            TestRunner.Add("Decoder_M1_PartialAssignment_Throws", () =>
            {
                var a = Sku(); var b = Sku();
                bool full;
                TestRunner.AssertThrows<InvalidOperationException>(() =>
                    SplitMilpDecoder.Decode(new[] { Q(a, 2), Q(b, 1) }.ToList(),
                        new[] { D(Q(a, 2)) }.ToList(), false, out full),
                    "M1 with 2 of 3 units assigned must throw");
            });
            TestRunner.Add("Decoder_M1_FullAcrossStations_OK", () =>
            {
                var a = Sku(); var b = Sku();
                bool full;
                var parts = SplitMilpDecoder.Decode(
                    new[] { Q(a, 2), Q(b, 1) }.ToList(),
                    new[] { D(Q(a, 1)), D(Q(a, 1), Q(b, 1)) }.ToList(), false, out full);
                TestRunner.AssertEqual(2, parts.Count, "M1 full assignment across two stations");
                TestRunner.AssertTrue(full, "fully assigned");
                TestRunner.AssertEqual(1, parts[0].Value[a], "station 0: 1xA");
                TestRunner.AssertEqual(1, parts[1].Value[b], "station 1: 1xB");
            });
        }
    }
}
```

`RAWSimO.Tests\Program.cs` 的 `Main` 中，`SplitPlannerTests.Register();` 之後加：

```csharp
            SplitMilpDecoderTests.Register();
```

`RAWSimO.Tests\RAWSimO.Tests.csproj`：在 `<Compile Include="SplitPlannerTests.cs" />` 之後加：

```xml
    <Compile Include="SplitMilpDecoderTests.cs" />
```

- [ ] **Step 2: 跑測試確認失敗**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimO.Tests\RAWSimO.Tests.csproj /p:Platform=x64 /p:Configuration=Release /v:m
```

Expected: **編譯失敗**（`SplitMilpDecoder` 不存在）——RED 證據。

- [ ] **Step 3: 實作 SplitMilpDecoder**

Create `RAWSimO.Core\Control\Defaults\OrderBatching\SplitMilpDecoder.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure (Gurobi-free) decoder from a SplitM1G MILP solution slice of one order to per-station
    /// child plans. Validates the solution invariants defensively: over-assignment, unknown SKUs
    /// and M1 all-or-nothing violations throw instead of silently mis-assigning picks.
    /// See docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md.
    /// </summary>
    public static class SplitMilpDecoder
    {
        /// <summary>
        /// Decodes the q[o,i,s] values of one order into non-empty per-station quantity parts.
        /// </summary>
        /// <param name="remaining">The order's residual demand snapshot of this solve (per SKU, > 0).</param>
        /// <param name="perStationQuantities">q values aligned by station index (may contain zeros/empty dicts).</param>
        /// <param name="crossTime">False enforces M1 all-or-nothing (total assigned == total remaining or 0).</param>
        /// <param name="fullyAssigned">True if all remaining units are assigned in this solution.</param>
        /// <returns>(stationIndex, quantities) list containing only non-empty parts; empty list if nothing assigned.</returns>
        public static List<KeyValuePair<int, Dictionary<ItemDescription, int>>> Decode(
            IList<KeyValuePair<ItemDescription, int>> remaining,
            IList<Dictionary<ItemDescription, int>> perStationQuantities,
            bool crossTime,
            out bool fullyAssigned)
        {
            if (remaining == null)
                throw new ArgumentNullException("remaining");
            if (perStationQuantities == null)
                throw new ArgumentNullException("perStationQuantities");
            // Aggregate the residual (defensive: tolerate duplicate SKU entries by summing)
            Dictionary<ItemDescription, int> residual = new Dictionary<ItemDescription, int>();
            foreach (var position in remaining)
            {
                if (residual.ContainsKey(position.Key))
                    residual[position.Key] += position.Value;
                else
                    residual.Add(position.Key, position.Value);
            }
            Dictionary<ItemDescription, int> assigned = new Dictionary<ItemDescription, int>();
            List<KeyValuePair<int, Dictionary<ItemDescription, int>>> parts =
                new List<KeyValuePair<int, Dictionary<ItemDescription, int>>>();
            for (int stationIndex = 0; stationIndex < perStationQuantities.Count; stationIndex++)
            {
                Dictionary<ItemDescription, int> quantities = perStationQuantities[stationIndex];
                if (quantities == null)
                    continue;
                Dictionary<ItemDescription, int> part = quantities
                    .Where(v => v.Value > 0)
                    .ToDictionary(v => v.Key, v => v.Value);
                if (part.Count == 0)
                    continue;
                foreach (var sku in part)
                {
                    if (!residual.ContainsKey(sku.Key))
                        throw new InvalidOperationException("MILP assigned a SKU that is not part of the order's residual demand!");
                    if (assigned.ContainsKey(sku.Key))
                        assigned[sku.Key] += sku.Value;
                    else
                        assigned.Add(sku.Key, sku.Value);
                }
                parts.Add(new KeyValuePair<int, Dictionary<ItemDescription, int>>(stationIndex, part));
            }
            foreach (var sku in assigned)
                if (sku.Value > residual[sku.Key])
                    throw new InvalidOperationException("MILP assigned more units of a SKU than the residual demand!");
            int totalRemaining = residual.Values.Sum();
            int totalAssigned = assigned.Values.Sum();
            fullyAssigned = totalRemaining > 0 && totalAssigned == totalRemaining;
            // M1 all-or-nothing: per-SKU <= residual + total equality together imply per-SKU equality
            if (!crossTime && totalAssigned != 0 && !fullyAssigned)
                throw new InvalidOperationException("M1 all-or-nothing violated: order partially assigned!");
            return parts;
        }
    }
}
```

`RAWSimO.Core.csproj`：在 `SplitM1GManager.cs` 的 include 之後加：

```xml
    <Compile Include="Control\Defaults\OrderBatching\SplitMilpDecoder.cs" />
```

- [ ] **Step 4: 跑測試確認通過**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /m /v:m
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimO.Tests\RAWSimO.Tests.csproj /p:Platform=x64 /p:Configuration=Release /v:m
.\RAWSimO.Tests\bin\x64\Release\RAWSimO.Tests.exe
```

Expected: `22/22 passed`（15 舊 + 7 新），exit code 0。

- [ ] **Step 5: Commit**

```powershell
git add RAWSimO.Core\Control\Defaults\OrderBatching\SplitMilpDecoder.cs RAWSimO.Core\RAWSimO.Core.csproj RAWSimO.Tests\SplitMilpDecoderTests.cs RAWSimO.Tests\Program.cs RAWSimO.Tests\RAWSimO.Tests.csproj
git commit -m "feat: SplitMilpDecoder pure decode/validate (TDD, 7 tests)"
```

---

### Task 3: 抽取共用 SplitConsolidationLogger

**Files:**
- Create: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitConsolidationLogger.cs`
- Modify: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitOrderManager.cs`（logger 委派）
- Modify: `RAWSimO.Core\RAWSimO.Core.csproj`

**Interfaces:**
- Produces: `SplitConsolidationLogger`，ctor `(Instance instance)`，方法 `void LogParentCompleted(Order order, OutputStation station)`（簽名相容 `Instance.OrderCompleted` 事件）。Task 6 的 SplitM1GManager 依賴此類別。
- Consumes: `Order.IsSplitParent` / `Children` / `TimeStampCompleted` / `GetDemandCount()` / `TimeStamp` / `TimeStampSubmit`（Spec 1 enabler）。

- [ ] **Step 1: 建立共用 logger（程式碼從 SplitOrderManager 原樣搬移）**

Create `RAWSimO.Core\Control\Defaults\OrderBatching\SplitConsolidationLogger.cs`：

```csharp
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Shared consolidation logger for order-splitting managers: writes one CSV row
    /// (splitorders.csv in the statistics directory) per completed split parent.
    /// Extracted verbatim from SplitOrderManager so SplitM1GManager can reuse it.
    /// </summary>
    public class SplitConsolidationLogger
    {
        /// <summary>
        /// Creates a new logger bound to the given instance.
        /// </summary>
        /// <param name="instance">The instance whose statistics directory receives the CSV.</param>
        public SplitConsolidationLogger(Instance instance) { _instance = instance; }

        private Instance _instance;

        /// <summary>
        /// Lazily opened CSV logging one row per completed split parent (consolidation detail).
        /// Same location pattern as the M1G decision log.
        /// </summary>
        private System.IO.StreamWriter _splitLog;

        /// <summary>
        /// Writes one CSV row when a split parent order completes (consolidation done).
        /// Signature matches the Instance.OrderCompleted event.
        /// </summary>
        public void LogParentCompleted(Order order, OutputStation station)
        {
            if (!order.IsSplitParent)
                return;
            if (_splitLog == null)
            {
                string dir = _instance != null && _instance.SettingConfig != null ? _instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _splitLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "splitorders.csv"), false) { AutoFlush = true };
                _splitLog.WriteLine("parent,units,children,firstChildDone,lastChildDone,consolidated,consolidationWait,placed,submitted");
            }
            double firstDone = System.Linq.Enumerable.Min(order.Children, c => c.TimeStampCompleted);
            double lastDone = System.Linq.Enumerable.Max(order.Children, c => c.TimeStampCompleted);
            _splitLog.WriteLine(string.Join(",", new string[] {
                order.ID.ToString(),
                order.GetDemandCount().ToString(),
                order.Children.Count.ToString(),
                firstDone.ToString(System.Globalization.CultureInfo.InvariantCulture),
                lastDone.ToString(System.Globalization.CultureInfo.InvariantCulture),
                order.TimeStampCompleted.ToString(System.Globalization.CultureInfo.InvariantCulture),
                (lastDone - firstDone).ToString(System.Globalization.CultureInfo.InvariantCulture),
                order.TimeStamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                order.TimeStampSubmit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }));
        }
    }
}
```

- [ ] **Step 2: SplitOrderManager 委派**

`SplitOrderManager.cs` 修改三處：

ctor（原第 23-27 行）改為：

```csharp
        public SplitOrderManager(Instance instance) : base(instance)
        {
            _config = instance.ControllerConfig.OrderBatchingConfig as SplitHeuristicConfiguration;
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }
```

欄位區（原 `_splitLog` 欄位與其 summary，第 34-38 行）換成：

```csharp
        /// <summary>
        /// Shared consolidation CSV logger (splitorders.csv).
        /// </summary>
        private SplitConsolidationLogger _logger;
```

整個 `private void LogParentCompleted(Order order, OutputStation station) { ... }` 方法（原第 108-138 行）**刪除**。

`RAWSimO.Core.csproj`：在 `SplitMilpDecoder.cs` include 之後加：

```xml
    <Compile Include="Control\Defaults\OrderBatching\SplitConsolidationLogger.cs" />
```

- [ ] **Step 3: 建置 + 測試 + SplitHeuristic 煙霧回歸**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /m /v:m
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimO.Tests\RAWSimO.Tests.csproj /p:Platform=x64 /p:Configuration=Release /v:m
.\RAWSimO.Tests\bin\x64\Release\RAWSimO.Tests.exe
& .\RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett Material\Instances\CoreBenchmark\small\split_m1.xconf output_split_logger_regression 0
Get-ChildItem -Recurse output_split_logger_regression -Filter statistics.txt | ForEach-Object { Select-String StatOverallOrdersHandled $_.FullName }
Get-ChildItem -Recurse output_split_logger_regression -Filter splitorders.csv | ForEach-Object { (Get-Content $_.FullName | Measure-Object -Line).Lines }
```

Expected: 22/22 tests；`StatOverallOrdersHandled` = **599**（Spec 1 最終數字，逐字搬移不得改變行為）；`splitorders.csv` 存在且有 header + 資料列。

- [ ] **Step 4: Commit**

```powershell
git add RAWSimO.Core\Control\Defaults\OrderBatching\SplitConsolidationLogger.cs RAWSimO.Core\Control\Defaults\OrderBatching\SplitOrderManager.cs RAWSimO.Core\RAWSimO.Core.csproj
git commit -m "refactor: extract SplitConsolidationLogger for reuse by SplitM1GManager"
```

---

### Task 4: InitializeSplit——殘量快照與變數命名

**Files:**
- Modify: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitM1GManager.cs`

**Interfaces:**
- Consumes（base M1GManager，均為 public/protected，原檔不動）：`GeneratePiSKU`、`GeneratePs`、`GenerateCs`、`CreatedeVarName`、`IsAvailabletoPiSKU`、`EstimateBotPodDistance`、`EstimatePodStationDistance`、`PodStationExtraCost`、`PrepareDecisionExtras`、`_pendingOrders`（OrderManager protected）。
- Produces（Task 5 依賴）：
  - `private HashSet<Pod> InitializeSplit(out ... 13 個 out 參數 ...)`——與 base `Initialize` 同構，多一個 `out Dictionary<Order, Dictionary<ItemDescription, int>> residuals`
  - `variableNames` 追加 key **7**=q（Symbol{order, skui, outputstation, name="q_{skuID}_{orderID}_{stationID}"}）、**8**=y（Symbol{order, outputstation, name="ysp_{orderID}_{stationID}"}）、**9**=z（Symbol{order, name="zfull_{orderID}"}，僅 M1 模式非空）
  - 成本包裝 `SplitBotPodCost(Bot, Pod)` / `SplitPodStationCost(Pod, OutputStation)` / `PrepareStarveAwareSplit(...)`（鏡射 base 的 private 實作——base 的 `PrepareStarveAware`/`M1GPodStationCost`/`M1GBotPodCost` 是 private 且原檔不可改）

- [ ] **Step 1: 在 SplitM1GManager 加入殘量版 helper 與 InitializeSplit**

在 `SplitM1GManager` 類別內（`_splitConfig` 欄位之後）加入：

```csharp
        /// <summary>
        /// order 進入 Od 的截止時間（鏡射 base 的 private DueTimeOrderofMP）。
        /// </summary>
        private static readonly double _dueTimeOrderofMP = TimeSpan.FromMinutes(30).TotalSeconds;

        /// <summary>
        /// Residual-demand version of GenerateOiSKU: indexes pending orders by the SKUs
        /// they still need (RemainingPositions), not their full demand.
        /// </summary>
        private Dictionary<ItemDescription, List<Order>> GenerateOiSKUSplit(HashSet<Order> pendingOrders)
        {
            Dictionary<ItemDescription, List<Order>> OiSKU = new Dictionary<ItemDescription, List<Order>>();
            foreach (var order in pendingOrders)
            {
                foreach (var sku in order.RemainingPositions)
                {
                    if (OiSKU.ContainsKey(sku.Key))
                        OiSKU[sku.Key].Add(order);
                    else
                        OiSKU.Add(sku.Key, new List<Order>() { order });
                }
            }
            return OiSKU;
        }

        /// <summary>
        /// Residual-demand version of GenerateOd: urgency set based on RemainingPositions,
        /// with a ContainsKey guard (M2 orders may have residual SKUs not coverable this epoch).
        /// Also refreshes Timestay/sequence for all pending orders (base parity).
        /// </summary>
        private HashSet<Order> GenerateOdSplit(HashSet<Order> pendingOrders, Dictionary<ItemDescription, List<Pod>> PiSKU)
        {
            HashSet<Order> Od = new HashSet<Order>();
            foreach (Order order in pendingOrders)
            {
                order.Timestay = order.DueTime - (Instance.SettingConfig.StartTime.AddSeconds(Convert.ToInt32(Instance.Controller.CurrentTime)) - order.TimePlaced).TotalSeconds;
                if (order.Timestay < _dueTimeOrderofMP)
                {
                    bool Isadd = true;
                    foreach (var sku in order.RemainingPositions)
                    {
                        if (PiSKU.ContainsKey(sku.Key) && PiSKU[sku.Key].All(v => v.CountAvailable(sku.Key) >= sku.Value && Instance.ResourceManager.UnusedPods.Contains(v)))
                            continue;
                        else
                            Isadd = false;
                    }
                    if (Isadd)
                        Od.Add(order);
                }
            }
            int i = 0;
            foreach (Order order in pendingOrders.OrderBy(v => v.Timestay).ThenBy(u => u.DueTime))
            {
                order.sequence = i;
                i++;
            }
            return Od;
        }
```

- [ ] **Step 2: 加入 starve-aware 成本包裝（鏡射 base private 區塊）**

接著加入：

```csharp
        // ── Starve-aware cost wrappers (mirror of the base's private block; the base members are
        //    private and the original file must stay untouched) ──
        private bool _saEnabledSplit;
        private double _saNominalSpeedSplit;
        private double _saFixedParamSplit;
        private Dictionary<int, double> _saEstByStationSplit = new Dictionary<int, double>();
        private Dictionary<int, double> _saRepBotPodTimeSplit = new Dictionary<int, double>();

        /// <summary>Per-solve precompute of starve-aware inputs; no-op when the feature is disabled.</summary>
        private void PrepareStarveAwareSplit(IEnumerable<Pod> pods, Dictionary<OutputStation, int> Cs, HashSet<Bot> Ra)
        {
            _saEnabledSplit = Instance != null && Instance.SettingConfig != null && Instance.SettingConfig.StarveAwareCostEnabled;
            if (!_saEnabledSplit)
                return;
            _saFixedParamSplit = Instance.SettingConfig.StarveAwareFixedParam;
            double cfgSpeed = Instance.SettingConfig.StarveAwareNominalSpeed;
            _saNominalSpeedSplit = cfgSpeed > 0.0
                ? cfgSpeed
                : (Instance.Bots != null && Instance.Bots.Count > 0
                    ? Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity))
                    : 1.0);
            double now = Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;
            _saEstByStationSplit = new Dictionary<int, double>();
            foreach (var s in Cs.Keys)
                _saEstByStationSplit[s.ID] = StarveAwareCost.Est(s, now);
            _saRepBotPodTimeSplit = new Dictionary<int, double>();
            foreach (var p in pods)
            {
                double best = double.PositiveInfinity;
                foreach (var r in Ra)
                    best = Math.Min(best, StarveAwareCost.TravelTime(EstimateBotPodDistance(r, p), _saNominalSpeedSplit));
                _saRepBotPodTimeSplit[p.ID] = best;
            }
        }

        /// <summary>bot->pod objective coefficient: travel time when starve-aware, else distance.</summary>
        private double SplitBotPodCost(Bot robot, Pod pod)
        {
            double d = EstimateBotPodDistance(robot, pod);
            return _saEnabledSplit ? StarveAwareCost.TravelTime(d, _saNominalSpeedSplit) : d;
        }

        /// <summary>pod->station objective coefficient: travel time + starvation delay penalty when starve-aware, else distance.</summary>
        private double SplitPodStationCost(Pod pod, OutputStation station)
        {
            double d = EstimatePodStationDistance(pod, station);
            if (!_saEnabledSplit)
                return d;
            double podStationTime = StarveAwareCost.TravelTime(d, _saNominalSpeedSplit);
            double repBotPod = (_saRepBotPodTimeSplit.TryGetValue(pod.ID, out var t) && !double.IsPositiveInfinity(t)) ? t : 0.0;
            double taCost = podStationTime + repBotPod;
            double est = _saEstByStationSplit.TryGetValue(station.ID, out var e) ? e : double.PositiveInfinity;
            double penalty = StarveAwareCost.DelayPenalty(taCost, est, _saFixedParamSplit);
            return podStationTime + penalty;
        }
```

註：`StarveAwareCost` 為 public static 類別；若命名空間不同需補 `using`（實作時以既有 M1GManager 的引用方式為準）。

- [ ] **Step 3: InitializeSplit 本體**

接著加入（鏡射 base `Initialize`，改動點以 `// SPLIT:` 註記）：

```csharp
        /// <summary>
        /// Split version of Initialize: snapshots the decision inputs with residual demand
        /// (RemainingPositions / GetRemainingDemand) instead of full order demand, and appends
        /// the q / y / z variable name lists (keys 7 / 8 / 9) to the base variable names.
        /// </summary>
        private HashSet<Pod> InitializeSplit(out Dictionary<ItemDescription, List<Pod>> PiSKU, out Dictionary<ItemDescription, List<Order>> OiSKU,
            out Dictionary<int, List<Symbol>> variableNames, out Dictionary<OutputStation, int> Cs, out HashSet<Order> pendingOrders,
            out Dictionary<OutputStation, HashSet<Pod>> inboundPods, out HashSet<Bot> Ra, out HashSet<Bot> Rb,
            out HashSet<Bot> R, out HashSet<Pod> Pb, out HashSet<Pod> Pa, out Dictionary<Pod, Bot> PodToBot,
            out Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            // SPLIT: first stock filter on residual demand; M1 = all residual SKUs coverable,
            //        M2 = at least one residual unit in actual stock
            HashSet<Order> pendingOrders1 = _splitConfig != null && _splitConfig.CrossTime
                ? new HashSet<Order>(_pendingOrders.Where(o => o.RemainingPositions.Any(p => Instance.StockInfo.GetActualStock(p.Key) >= 1)))
                : new HashSet<Order>(_pendingOrders.Where(o => o.RemainingPositions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value)));
            OiSKU = GenerateOiSKUSplit(pendingOrders1);
            Cs = GenerateCs();
            inboundPods = GeneratePs(Cs);
            HashSet<ItemDescription> ItemofOiSKU = new HashSet<ItemDescription>(OiSKU.Keys);
            HashSet<Pod> allPods = new HashSet<Pod>();
            Ra = new HashSet<Bot>();
            Rb = new HashSet<Bot>();
            R = new HashSet<Bot>();
            Pb = new HashSet<Pod>();
            PodToBot = new Dictionary<Pod, Bot>();
            HashSet<Pod> Pa1 = new HashSet<Pod>();
            foreach (var pods in inboundPods)
            {
                foreach (Pod pod in pods.Value)
                {
                    if (PodToBot.ContainsKey(pod)) continue;
                    if (Instance.ResourceManager._usedPods.ContainsKey(pod))
                    {
                        allPods.Add(pod);
                        Rb.Add(Instance.ResourceManager._usedPods[pod]);
                        R.Add(Instance.ResourceManager._usedPods[pod]);
                        PodToBot[pod] = Instance.ResourceManager._usedPods[pod];
                        Pb.Add(pod);
                    }
                    else if (Instance.ResourceManager.BottoPod.ContainsValue(pod))
                    {
                        var bot = Instance.ResourceManager.BottoPod.Where(V => V.Value.ID == pod.ID).First().Key;
                        allPods.Add(pod);
                        Rb.Add(bot);
                        R.Add(bot);
                        PodToBot[pod] = bot;
                        Pb.Add(pod);
                    }
                    else
                    {
                        // Snapshot can contain a station inbound pod before its pod-bot ownership is visible.
                        // Exclude it from the model for this decision. (base parity)
                    }
                }
            }
            foreach (var pod in Instance.ResourceManager.UnusedPods.Where(v =>
                v.IsAvailabletoOiSKU(ItemofOiSKU) &&
                !Instance.ResourceManager.BottoPod.ContainsValue(v) &&
                !Instance.ResourceManager._usedPods.ContainsKey(v) &&
                v.Waypoint != null &&
                v.Waypoint.PodStorageLocation))
            {
                allPods.Add(pod);
                Pa1.Add(pod);
            }
            foreach (var bot in Instance._outputstationbots)
            {
                if (bot.Pod == null && !Instance.ResourceManager._usedPods.ContainsValue(bot) && !Instance.ResourceManager.BottoPod.ContainsKey(bot))
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
                else if (bot.Pod == null && !Instance.ResourceManager.BottoPod.ContainsKey(bot) && !Rb.Contains(bot) && bot.CurrentTask is RestTask && bot.GetInfoDestinationWaypoint() == null)
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
                else if (CanUseReturnPendingBot(bot))
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
            }
            PiSKU = GeneratePiSKU(allPods);
            // SPLIT: second (PiSKU) filter on residual demand, M1 all / M2 any
            if (_splitConfig != null && _splitConfig.CrossTime)
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.Any(p => IsAvailabletoPiSKU(p.Key) >= 1)));
            else
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.All(p => IsAvailabletoPiSKU(p.Key) >= p.Value)));
            HashSet<Order> Od = GenerateOdSplit(pendingOrders, PiSKU);
            if (Od.Count > Cs.Values.Sum())
                pendingOrders = new HashSet<Order>(Od);
            OiSKU = GenerateOiSKUSplit(pendingOrders);
            // SPLIT: residual snapshot for this solve (decode consistency)
            residuals = pendingOrders.ToDictionary(o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            variableNames = CreatedeVarName(PiSKU, OiSKU, allPods, pendingOrders, Cs, R, Pa1, out Pa);
            // SPLIT: append q (7) / y (8) / z (9) variable names.
            // q only for residual SKUs coverable by the model pods (PiSKU) - non-coverable residual
            // SKUs simply stay in the backlog this epoch (M2); M1 orders are pre-filtered to full coverage.
            List<Symbol> deVarNameq = new List<Symbol>();
            List<Symbol> deVarNamey = new List<Symbol>();
            List<Symbol> deVarNamez = new List<Symbol>();
            foreach (var order in pendingOrders)
            {
                foreach (var station in Cs.Keys)
                {
                    deVarNamey.Add(new Symbol { order = order, outputstation = station, name = "ysp" + "_" + order.ID.ToString() + "_" + station.ID.ToString() });
                    foreach (var sku in residuals[order].Where(p => PiSKU.ContainsKey(p.Key)))
                        deVarNameq.Add(new Symbol { order = order, outputstation = station, skui = sku.Key, name = "q" + "_" + sku.Key.ID.ToString() + "_" + order.ID.ToString() + "_" + station.ID.ToString() });
                }
                if (_splitConfig == null || !_splitConfig.CrossTime)
                    deVarNamez.Add(new Symbol { order = order, name = "zfull" + "_" + order.ID.ToString() });
            }
            variableNames.Add(7, deVarNameq);
            variableNames.Add(8, deVarNamey);
            variableNames.Add(9, deVarNamez);
            return allPods;
        }
```

- [ ] **Step 4: 建置驗證**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /m /v:m
```

Expected: Build succeeded, 0 errors。（本 task 無獨立可執行驗證——快照邏輯需 Instance；正確性由 review gate + Task 7 煙霧承擔。）

- [ ] **Step 5: Commit**

```powershell
git add RAWSimO.Core\Control\Defaults\OrderBatching\SplitM1GManager.cs
git commit -m "feat: SplitM1G InitializeSplit - residual snapshot + q/y/z variable names"
```

---

### Task 5: SolveSplit——模型建構、求解、解承諾

**Files:**
- Modify: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitM1GManager.cs`

**Interfaces:**
- Consumes: Task 4 的 `InitializeSplit` 輸出、Task 2 的 `SplitMilpDecoder.Decode`、Spec 1 enabler（`Order.CreateSplitChild`、`ResourceManager.TransferExtractRequests`、`Order.IsSplitParent`、`idoforder`）、`LinearModel`/`VariableCollection`/`LinearExpression`（SolverWrappers）。
- Produces（Task 6 依賴）：
  ```csharp
  private class SplitSolveResult
  {
      public Dictionary<Symbol, int> NewZiops = new Dictionary<Symbol, int>();
      public List<Symbol> Allocations = new List<Symbol>();      // Symbol{order=可 allocate 物件(child 或快路徑母單), outputstation}
      public HashSet<Order> SplitParents = new HashSet<Order>(); // 本次產生 >=1 個 child 的母單
  }
  private SplitSolveResult SolveSplit(SolverType type, Dictionary<ItemDescription, List<Pod>> PiSKU,
      Dictionary<ItemDescription, List<Order>> OiSKU, IEnumerable<Pod> Pods, Dictionary<OutputStation, int> Cs,
      Dictionary<int, List<Symbol>> variableNames, HashSet<Order> pendingOrders,
      Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Bot> Ra, HashSet<Bot> Rb, HashSet<Bot> R,
      HashSet<Pod> Pb, HashSet<Pod> Pa, Dictionary<Pod, Bot> PodToBot,
      Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
  ```

- [ ] **Step 1: SplitSolveResult 與 decision logger**

在 SplitM1GManager 加入：

```csharp
        /// <summary>
        /// The committed outcome of one SplitM1G solve, consumed by DecideAboutPendingOrders.
        /// </summary>
        private class SplitSolveResult
        {
            public Dictionary<Symbol, int> NewZiops = new Dictionary<Symbol, int>();
            public List<Symbol> Allocations = new List<Symbol>();
            public HashSet<Order> SplitParents = new HashSet<Order>();
        }

        // ── Per-decision summary logger (diagnostic; split analog of m1g_decision_log.csv) ──
        private System.IO.StreamWriter _splitDecisionLog;
        private int _splitDecisionIndex = 0;
        private void WriteSplitDecisionLog(bool solved, double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int nXps, int nChildren, int nFastPath, int unitsAssigned, double sumUs, double objective, double optSec)
        {
            if (_splitDecisionLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _splitDecisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "splitm1g_decision_log.csv"), false) { AutoFlush = true };
                _splitDecisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsInModel,xps,children,fastPath,units,sumUs,objective,solveSec");
            }
            _splitDecisionLog.WriteLine(string.Join(",", new string[] {
                _splitDecisionIndex.ToString(),
                time.ToString(System.Globalization.CultureInfo.InvariantCulture),
                solved ? "1" : "0",
                pendingOrdersN.ToString(),
                stationsWithCap.ToString(),
                podsInModel.ToString(),
                nXps.ToString(),
                nChildren.ToString(),
                nFastPath.ToString(),
                unitsAssigned.ToString(),
                sumUs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                objective.ToString(System.Globalization.CultureInfo.InvariantCulture),
                optSec.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
            _splitDecisionIndex++;
        }
```

- [ ] **Step 2: SolveSplit——模型建構與約束**

```csharp
        /// <summary>
        /// Builds and solves the SplitM1G MILP (shi2 relaxed to q[o,i,s]) and commits the solution:
        /// robot-pod claiming (base parity), child creation via SplitMilpDecoder + the Spec 1 enabler
        /// pipeline, greedy Ziops extraction per station, and release of unused newly-selected pods.
        /// </summary>
        private SplitSolveResult SolveSplit(SolverType type, Dictionary<ItemDescription, List<Pod>> PiSKU,
            Dictionary<ItemDescription, List<Order>> OiSKU, IEnumerable<Pod> Pods, Dictionary<OutputStation, int> Cs,
            Dictionary<int, List<Symbol>> variableNames, HashSet<Order> pendingOrders,
            Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Bot> Ra, HashSet<Bot> Rb, HashSet<Bot> R,
            HashSet<Pod> Pb, HashSet<Pod> Pa, Dictionary<Pod, Bot> PodToBot,
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            LinearModel wrapper = new LinearModel(type, (string s) => { Console.Write(s); });
            SplitSolveResult result = new SplitSolveResult();
            bool crossTime = _splitConfig != null && _splitConfig.CrossTime;
            List<Symbol> deVarNamexps = variableNames[1];
            List<Symbol> deVarNameyrp = variableNames[4];
            List<Symbol> deVarNameus = variableNames[5];
            List<Symbol> deVarNamedops = variableNames[6];
            List<Symbol> deVarNameq = variableNames[7];
            List<Symbol> deVarNamey = variableNames[8];
            // variableNames[9] (z) 只在 M1 約束中以名稱字串引用，毋需區域變數
            double w1 = 1;
            double w2u = _splitConfig != null ? _splitConfig.UnitRewardWeight : -40;
            double w3 = 1000;
            int maxCs = Cs.Count > 0 ? Cs.Values.Max() : 1;
            int maxR = residuals.Count > 0 ? residuals.Values.SelectMany(d => d.Values).DefaultIfEmpty(1).Max() : 1;
            VariableCollection<string> variablesBinary = new VariableCollection<string>(wrapper, VariableType.Binary, 0, 1, (string s) => { return s; });
            VariableCollection<string> variablesUs = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxCs, (string s) => { return s; });
            VariableCollection<string> variablesQ = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxR, (string s) => { return s; });
            PrepareStarveAwareSplit(Pods, Cs, Ra);
            PrepareDecisionExtras(Pods, Cs, Ra);
            // 目標：w1·(pod->station + bot->pod) + w2'·Σq + w3·Σus（Ra 空時省略 yrp 項，base parity）
            if (Ra.Count() > 0)
                wrapper.SetObjective((LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (SplitPodStationCost(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation))), wrapper)
                    + LinearExpression.Sum(deVarNameyrp.Where(u => Ra.Contains(u.robot) && Instance.ResourceManager.UnusedPods.Contains(u.pod) && u.pod.Waypoint != null).Select(v => variablesBinary[v.name] *
                    SplitBotPodCost(v.robot, v.pod)), wrapper)) * w1
                    + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name])) * w2u
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesUs[v.name])) * w3, OptimizationSense.Minimize);
            else
                wrapper.SetObjective(LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (SplitPodStationCost(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation))), wrapper) * w1
                    + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name])) * w2u
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesUs[v.name])) * w3, OptimizationSense.Minimize);
            // (link-up) q ≤ r·y ；(link-down) y ≤ Σi q
            foreach (var q in deVarNameq)
                wrapper.AddConstr(variablesQ[q.name] <= residuals[q.order][q.skui] * variablesBinary["ysp" + "_" + q.order.ID.ToString() + "_" + q.outputstation.ID.ToString()], "slink1");
            foreach (var y in deVarNamey)
                wrapper.AddConstr(variablesBinary[y.name] <= LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == y.order.ID && v.outputstation.ID == y.outputstation.ID).Select(v => variablesQ[v.name])), "slink2");
            // (shi4') 純 slot 制：Σo y = Cs − us
            foreach (var station in Cs.Keys)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamey.Where(v => v.outputstation.ID == station.ID).Select(v => variablesBinary[v.name])) == Cs[station] - variablesUs["us" + "_" + station.ID.ToString()], "shi4");
            // (shi5') 庫存可行性：Σo q ≤ Σp stock·xps
            foreach (var sku in OiSKU.Where(v => PiSKU.ContainsKey(v.Key)))
            {
                foreach (var station in Cs.Keys)
                {
                    wrapper.AddConstr(LinearExpression.Sum(deVarNameq.Where(v => v.outputstation.ID == station.ID && v.skui == sku.Key).Select(v => variablesQ[v.name]))
                        <= LinearExpression.Sum(deVarNamexps.Where(v => v.outputstation.ID == station.ID && PiSKU[sku.Key].Contains(v.pod)).Select(v =>
                        v.pod.CountAvailable(sku.Key) * variablesBinary[v.name])), "shi5");
                }
            }
            // (M1) Σs q = r·z ／ (M2) Σs q ≤ r
            foreach (var order in pendingOrders)
            {
                foreach (var sku in residuals[order].Where(p => PiSKU.ContainsKey(p.Key)))
                {
                    var lhs = LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == order.ID && v.skui == sku.Key).Select(v => variablesQ[v.name]));
                    if (crossTime)
                        wrapper.AddConstr(lhs <= sku.Value, "sm2");
                    else
                        wrapper.AddConstr(lhs == sku.Value * variablesBinary["zfull" + "_" + order.ID.ToString()], "sm1");
                }
            }
            // shi6~shi13（base 逐字保留；shi12 的 yaos → ysp）
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "shi6");
            foreach (var station in inboundPods)
            {
                foreach (var pod in station.Value)
                {
                    if (!Pb.Contains(pod))
                        continue;
                    wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.Key.ID.ToString()] == 1, "shi7");
                    wrapper.AddConstr(variablesBinary["yrp" + "_" + PodToBot[pod].ID.ToString() + "_" + pod.ID.ToString()] == 1, "shi11");
                }
            }
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])), "shi8");
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "shi9");
            foreach (var robot in R)
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.robot.ID == robot.ID).Select(v => variablesBinary[v.name])) <= 1, "shi10");
            foreach (var sku in OiSKU.Where(v => PiSKU.ContainsKey(v.Key)))
            {
                List<Pod> listofpod = PiSKU[sku.Key];
                foreach (var pod in listofpod.Where(v => Pa.Contains(v)))
                {
                    foreach (var order in sku.Value)
                    {
                        foreach (var station in Cs.Keys)
                        {
                            wrapper.AddConstr(2 * variablesBinary["dops" + "_" + order.ID.ToString() + "_" + pod.ID.ToString() + "_" + station.ID.ToString()]
                                <= variablesBinary["ysp" + "_" + order.ID.ToString() + "_" + station.ID.ToString()] + variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()], "shi12");
                        }
                    }
                }
            }
            foreach (var pod in Pa)
            {
                foreach (var station in Cs.Keys)
                    wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()]
                        <= LinearExpression.Sum(deVarNamedops.Where(v => v.pod.ID == pod.ID && v.outputstation.ID == station.ID).Select(v => variablesBinary[v.name])), "shi13");
            }
            wrapper.Update();
            DateTime _optStart = DateTime.Now;
            wrapper.Optimize();
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;
```

（接 Step 3 的解承諾區塊，同一方法內。）

- [ ] **Step 3: SolveSplit——解承諾（抽取、claiming、children、Ziops、釋放）**

緊接 Step 2 程式碼（同一方法內）：

```csharp
            if (wrapper.HasSolution())
            {
                // 1) 選中變數抽取（明確逐清單，不用 base 的 1..N dict 迴圈——keys 2/3 未建變數）
                List<Symbol> IsdeVarNamexps = deVarNamexps.Where(v => Math.Round(variablesBinary[v.name].GetValue()) != 0).ToList();
                List<Symbol> IsdeVarNamedops = deVarNamedops.Where(v => Math.Round(variablesBinary[v.name].GetValue()) != 0).ToList();
                // 2) robot-pod claiming（base i==4 區塊逐字）
                List<Symbol> IsdeVarNameyrp = new List<Symbol>();
                foreach (var itemName in deVarNameyrp)
                {
                    if (Math.Round(variablesBinary[itemName.name].GetValue()) != 0 && Ra.Contains(itemName.robot))
                    {
                        IsdeVarNameyrp.Add(itemName);
                        Instance.ResourceManager.BottoPod.Add(itemName.robot, itemName.pod);
                        Instance.ResourceManager.ClaimPod(itemName.pod, itemName.robot, BotTaskType.Extract);
                        foreach (var xps in IsdeVarNamexps.Where(v => v.pod.ID == itemName.pod.ID))
                            xps.outputstation.RegisterInboundPod(itemName.pod);
                    }
                }
                // 3) 解碼 → children / 快路徑（站序固定以求 determinism）
                List<OutputStation> stationList = Cs.Keys.OrderBy(s => s.ID).ToList();
                Dictionary<OutputStation, List<Order>> _availableStationorder = new Dictionary<OutputStation, List<Order>>();
                // Ziops 的 dops 對照需回到「模型層 order」（child 的 dops 掛在母單 ID 上）
                Dictionary<Order, Order> modelOrderOf = new Dictionary<Order, Order>();
                int nChildren = 0, nFastPath = 0, unitsAssigned = 0;
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    List<Dictionary<ItemDescription, int>> perStation = new List<Dictionary<ItemDescription, int>>();
                    foreach (var station in stationList)
                    {
                        Dictionary<ItemDescription, int> quantities = new Dictionary<ItemDescription, int>();
                        foreach (var q in deVarNameq.Where(v => v.order.ID == order.ID && v.outputstation.ID == station.ID))
                        {
                            int units = (int)Math.Round(variablesQ[q.name].GetValue());
                            if (units > 0)
                                quantities[q.skui] = units;
                        }
                        perStation.Add(quantities);
                    }
                    bool fullyAssigned;
                    List<KeyValuePair<int, Dictionary<ItemDescription, int>>> parts =
                        SplitMilpDecoder.Decode(residuals[order].ToList(), perStation, crossTime, out fullyAssigned);
                    if (parts.Count == 0)
                        continue;
                    unitsAssigned += parts.Sum(p => p.Value.Values.Sum());
                    if (parts.Count == 1 && fullyAssigned && !order.IsSplitParent)
                    {
                        // 未拆快路徑：單站全數且母單未被拆過 → 直接 allocate 母單（enabler parity）
                        OutputStation station = stationList[parts[0].Key];
                        result.Allocations.Add(new Symbol { order = order, outputstation = station });
                        if (!_availableStationorder.ContainsKey(station))
                            _availableStationorder.Add(station, new List<Order>());
                        _availableStationorder[station].Add(order);
                        modelOrderOf[order] = order;
                        nFastPath++;
                    }
                    else
                    {
                        foreach (var part in parts)
                        {
                            OutputStation station = stationList[part.Key];
                            Order child = Order.CreateSplitChild(order, part.Value);
                            child.ID = idoforder++;
                            Instance.ResourceManager.TransferExtractRequests(order, child);
                            result.Allocations.Add(new Symbol { order = child, outputstation = station });
                            if (!_availableStationorder.ContainsKey(station))
                                _availableStationorder.Add(station, new List<Order>());
                            _availableStationorder[station].Add(child);
                            modelOrderOf[child] = order;
                            nChildren++;
                        }
                        result.SplitParents.Add(order);
                    }
                }
                // 4) 模型外求 Ziops（base 逐字，兩處差異：迭代物件=child/快路徑母單；
                //    dops 對照改用 modelOrderOf + 站別過濾，否則 child 新 ID 對不上母單的 dops）
                DateTime A = DateTime.Now;
                if (_availableStationorder.Count > 0)
                {
                    foreach (var _currentStationorder in _availableStationorder)
                    {
                        Dictionary<ItemDescription, Dictionary<Pod, int>> _availableCounts = new Dictionary<ItemDescription, Dictionary<Pod, int>>();
                        foreach (var itemName in IsdeVarNamexps.Where(v => v.outputstation.ID == _currentStationorder.Key.ID))
                        {
                            foreach (var item in itemName.pod.ItemDescriptionsContained.Where(v => itemName.pod.CountAvailable(v) > 0))
                            {
                                if (_availableCounts.ContainsKey(item))
                                {
                                    if (_availableCounts[item].ContainsKey(itemName.pod))
                                        _availableCounts[item][itemName.pod] += itemName.pod.CountAvailable(item);
                                    else
                                        _availableCounts[item].Add(itemName.pod, itemName.pod.CountAvailable(item));
                                }
                                else
                                {
                                    Dictionary<Pod, int> Counts = new Dictionary<Pod, int>();
                                    _availableCounts.Add(item, Counts);
                                    _availableCounts[item].Add(itemName.pod, itemName.pod.CountAvailable(item));
                                }
                            }
                        }
                        HashSet<Pod> dopsPodsSelected = new HashSet<Pod>();
                        HashSet<Pod> dopsPodsUsed = new HashSet<Pod>();
                        foreach (var order in _currentStationorder.Value)
                        {
                            Dictionary<ItemDescription, int> itemDemands = new Dictionary<ItemDescription, int>();
                            foreach (var item in order.Positions)
                                itemDemands.Add(item.Key, item.Value);
                            HashSet<Pod> orderDopsPods = new HashSet<Pod>();
                            Order modelOrder = modelOrderOf[order];
                            foreach (var item in IsdeVarNamedops.Where(v => v.order.ID == modelOrder.ID && v.outputstation.ID == _currentStationorder.Key.ID))
                            {
                                dopsPodsSelected.Add(item.pod);
                                orderDopsPods.Add(item.pod);
                            }
                            foreach (var itemDemand in itemDemands)
                            {
                                int number = itemDemand.Value;
                                while (number > 0)
                                {
                                    Pod pod;
                                    if (_availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).Count() > 0)
                                    {
                                        pod = _availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).First();
                                        orderDopsPods.Remove(pod);
                                        dopsPodsUsed.Add(pod);
                                    }
                                    else
                                        pod = _availableCounts[itemDemand.Key].Keys.First();
                                    Symbol name = new Symbol
                                    {
                                        pod = pod,
                                        order = order,
                                        outputstation = _currentStationorder.Key,
                                        skui = itemDemand.Key,
                                        name = "ziops" + "_" + itemDemand.Key.ID.ToString() + "_" +
                                        order.ID.ToString() + "_" + pod.ID.ToString() + "_" + _currentStationorder.Key.ID.ToString()
                                    };
                                    if (_availableCounts[itemDemand.Key][pod] >= number)
                                    {
                                        int numpods = _availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).Count();
                                        if (numpods > 0 && number > 1)
                                        {
                                            Pod pod1 = _availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).First();
                                            orderDopsPods.Remove(pod1);
                                            dopsPodsUsed.Add(pod1);
                                            if (_availableCounts[itemDemand.Key][pod] >= _availableCounts[itemDemand.Key][pod1])
                                            {
                                                _availableCounts[itemDemand.Key][pod] -= number - numpods;
                                                Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, number - numpods);
                                                result.NewZiops.Add(name, number - numpods);
                                                number = numpods;
                                            }
                                            else
                                            {
                                                _availableCounts[itemDemand.Key][pod] -= 1;
                                                Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, 1);
                                                result.NewZiops.Add(name, 1);
                                                number = 1;
                                            }
                                        }
                                        else
                                        {
                                            _availableCounts[itemDemand.Key][pod] -= number;
                                            result.NewZiops.Add(name, number);
                                            Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, number);
                                            number = 0;
                                        }
                                        if (_availableCounts[itemDemand.Key][pod] == 0)
                                            _availableCounts[itemDemand.Key].Remove(pod);
                                    }
                                    else
                                    {
                                        result.NewZiops.Add(name, _availableCounts[itemDemand.Key][pod]);
                                        Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, _availableCounts[itemDemand.Key][pod]);
                                        number -= _availableCounts[itemDemand.Key][pod];
                                        _availableCounts[itemDemand.Key].Remove(pod);
                                    }
                                }
                            }
                        }
                        HashSet<Pod> unusedDopsPods = new HashSet<Pod>(dopsPodsSelected.Where(v => !dopsPodsUsed.Contains(v)));
                        if (unusedDopsPods.Count > 0)
                        {
                            foreach (var pod in unusedDopsPods)
                            {
                                Symbol name2 = IsdeVarNameyrp.Where(v => v.pod.ID == pod.ID).FirstOrDefault();
                                if (name2 == null)
                                    continue; // 繼承 pod（shi7/shi11 固定）不在 Ra claiming 清單，不可釋放
                                IsdeVarNameyrp.Remove(name2);
                                Instance.ResourceManager.BottoPod.Remove(name2.robot);
                                Instance.ResourceManager.ReleasePod(name2.pod);
                                foreach (var xps in IsdeVarNamexps.Where(v => v.pod.ID == name2.pod.ID))
                                    xps.outputstation.UnregisterInboundPod(name2.pod);
                                Symbol name1 = IsdeVarNamexps.Where(v => v.pod.ID == pod.ID).First();
                                IsdeVarNamexps.Remove(name1);
                            }
                        }
                    }
                }
                Instance.Observer.TimeOrderBatchingbyziops((DateTime.Now - A).TotalSeconds);
                double sumUs = 0.0;
                foreach (var s in deVarNameus)
                    sumUs += Math.Round(variablesUs[s.name].GetValue());
                WriteSplitDecisionLog(true, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    IsdeVarNamexps.Count, nChildren, nFastPath, unitsAssigned, sumUs, wrapper.GetObjectiveValue(), _optSec);
            }
            else
            {
                WriteSplitDecisionLog(false, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    0, 0, 0, 0, 0.0, double.NaN, _optSec);
            }
            return result;
        }
```

註記兩個**有意偏離 base 的點**（review 時特別驗證）：
1. dops 對照經 `modelOrderOf` 映射回母單 ID 並加站別過濾——base 直接用 `order.ID` 比對，child 的新 ID 會對不上，導致 dops 優先提示失效且 `unusedDopsPods` 永遠為空（xps 選了卻沒單用的 pod 不會被釋放，bot 白跑）。
2. `IsdeVarNameyrp...FirstOrDefault() + null continue`——base 用 `First()`；split 下 dops 掛母單、多 child 消耗同站 pods，防禦繼承 pod（Rb 持有、不在 claiming 清單）出現在 unused 集合時 `First()` 直接炸。

- [ ] **Step 4: 建置驗證**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /m /v:m
```

Expected: Build succeeded, 0 errors。

- [ ] **Step 5: Commit**

```powershell
git add RAWSimO.Core\Control\Defaults\OrderBatching\SplitM1GManager.cs
git commit -m "feat: SplitM1G SolveSplit - relaxed MILP + child-order solution commit"
```

---

### Task 6: DecideAboutPendingOrders 接線 + consolidation logger

**Files:**
- Modify: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitM1GManager.cs`

**Interfaces:**
- Consumes: Task 4 `InitializeSplit`、Task 5 `SolveSplit`/`SplitSolveResult`、Task 3 `SplitConsolidationLogger`、`ItemManager.TakeAvailableOrder`（enabler 用法）。
- Produces: 完整可運行的 SplitM1GManager（Task 7 煙霧的前提）。

- [ ] **Step 1: ctor 掛 logger**

把 Task 1 的 ctor 改為：

```csharp
        public SplitM1GManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GConfiguration;
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }
```

並在 `_splitConfig` 欄位後加：

```csharp
        /// <summary>
        /// Shared consolidation CSV logger (splitorders.csv).
        /// </summary>
        private SplitConsolidationLogger _logger;
```

- [ ] **Step 2: override DecideAboutPendingOrders**

```csharp
        /// <summary>
        /// This is called to decide about potentially pending orders (split MILP version).
        /// Mirrors the base skeleton: snapshot -> Gurobi solve -> JustRegisterItem -> AllocateOrder,
        /// plus the split-parent bookkeeping of the Spec 1 enabler (TimeStampSubmit, fully-claimed removal).
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
            DateTime A = DateTime.Now;
            Dictionary<ItemDescription, List<Pod>> PiSKU;
            Dictionary<ItemDescription, List<Order>> OiSKU;
            Dictionary<int, List<Symbol>> variableNames;
            Dictionary<OutputStation, int> Cs;
            Dictionary<OutputStation, HashSet<Pod>> inboundPods;
            HashSet<Order> pendingOrders;
            HashSet<Bot> Ra;
            HashSet<Bot> Rb;
            HashSet<Bot> R;
            HashSet<Pod> Pb;
            HashSet<Pod> Pa;
            Dictionary<Pod, Bot> PodToBot;
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals;
            HashSet<Pod> allPods = InitializeSplit(out PiSKU, out OiSKU, out variableNames, out Cs, out pendingOrders,
                out inboundPods, out Ra, out Rb, out R, out Pb, out Pa, out PodToBot, out residuals);
            if (R.Count() > 0 && pendingOrders.Count > 0)
            {
                SplitSolveResult result = SolveSplit(SolverType.Gurobi, PiSKU, OiSKU, allPods, Cs, variableNames,
                    pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa, PodToBot, residuals);
                foreach (var symbol in result.NewZiops)
                {
                    for (int i = 0; i < symbol.Value; i++)
                        symbol.Key.pod.JustRegisterItem(symbol.Key.skui);
                }
                foreach (var alloc in result.Allocations)
                {
                    AllocateOrder(alloc.order, alloc.outputstation);
                    Instance.StatCustomControllerInfo.CustomLogOB1++;
                }
                foreach (var parent in result.SplitParents)
                {
                    if (double.IsPositiveInfinity(parent.TimeStampSubmit))
                        parent.TimeStampSubmit = Instance.Controller.CurrentTime;
                    if (parent.IsFullyClaimed)
                    {
                        _pendingOrders.Remove(parent);
                        (Instance.ItemManager as ItemManager).TakeAvailableOrder(parent);
                    }
                }
                Instance.Observer.TimeOrderBatchingbyMP((DateTime.Now - A).TotalSeconds);
            }
        }
```

- [ ] **Step 3: 建置 + 全測試**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /m /v:m
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimO.Tests\RAWSimO.Tests.csproj /p:Platform=x64 /p:Configuration=Release /v:m
.\RAWSimO.Tests\bin\x64\Release\RAWSimO.Tests.exe
```

Expected: Build succeeded；`22/22 passed`。

- [ ] **Step 4: Commit**

```powershell
git add RAWSimO.Core\Control\Defaults\OrderBatching\SplitM1GManager.cs
git commit -m "feat: SplitM1G DecideAboutPendingOrders wiring + consolidation logging"
```

---

### Task 7: xconf、煙霧、回歸

**Files:**
- Create: `Material\Instances\CoreBenchmark\small\split_milp_m1.xconf`
- Create: `Material\Instances\CoreBenchmark\small\split_milp_m2.xconf`

**Interfaces:**
- Consumes: Task 1-6 全部；`m1g.xconf`（模板與 M0 回歸基準）。

- [ ] **Step 1: 建立 xconf**

複製 `Material\Instances\CoreBenchmark\small\m1g.xconf` 為 `split_milp_m1.xconf`，只改兩處：

1. `<Name>m1g</Name>` → `<Name>split_milp_m1</Name>`
2. `<OrderBatchingConfig>` 段（原 105-111 行）換成：

```xml
  <OrderBatchingConfig xsi:type="SplitM1GConfiguration">
    <Name />
    <TieBreaker>EarliestDueTime</TieBreaker>
    <FastLane>true</FastLane>
    <LateBeforeMatch>false</LateBeforeMatch>
    <FastLaneTieBreaker>EarliestDueTime</FastLaneTieBreaker>
    <CrossTime>false</CrossTime>
    <UnitRewardWeight>-40</UnitRewardWeight>
  </OrderBatchingConfig>
```

再複製為 `split_milp_m2.xconf`：`<Name>split_milp_m2</Name>`、`<CrossTime>true</CrossTime>`，其餘相同。
用 `git diff --no-index`（或 fc.exe）確認兩份新 xconf 與 `m1g.xconf` 除上述差異外逐字節相同。

- [ ] **Step 2: 煙霧 M1 / M2**

```powershell
& .\RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett Material\Instances\CoreBenchmark\small\split_milp_m1.xconf output_splitmilp_m1 0
& .\RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett Material\Instances\CoreBenchmark\small\split_milp_m2.xconf output_splitmilp_m2 0
Get-ChildItem -Recurse output_splitmilp_m1,output_splitmilp_m2 -Filter statistics.txt | ForEach-Object { Select-String "StatOverallOrdersHandled|StatOverallOrdersPlaced" $_.FullName }
Get-ChildItem -Recurse output_splitmilp_m1,output_splitmilp_m2 -Filter splitorders.csv | ForEach-Object { Get-Content $_.FullName -TotalCount 5 }
Get-ChildItem -Recurse output_splitmilp_m1,output_splitmilp_m2 -Filter splitm1g_decision_log.csv | ForEach-Object { Get-Content $_.FullName -TotalCount 3 }
```

Expected（判準，非精確值）：
- 兩組都跑完 7200s，**無 exception**。
- `StatOverallOrdersHandled` 與 M0 baseline（Task 1 記錄值，~656）同量級（±20% 內）；顯著低於（如 <500）= 卡單或模型病態，必須調查。
- `splitorders.csv` 存在（若有母單被拆且完成）；`splitm1g_decision_log.csv` 有資料列且 `solved=1` 佔多數。
- in-flight sanity：`StatOverallOrdersPlaced − StatOverallOrdersHandled` 與 baseline 同量級（差異 >30 = 疑似卡單）。
- 把兩組 handled、拆單列數、fastPath/children 統計寫進報告。

- [ ] **Step 3: M0 回歸（m1g.xconf 必須與 Task 1 快照一致）**

```powershell
& .\RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett Material\Instances\CoreBenchmark\small\m1g.xconf output_splitmilp_regression_m1g 0
Get-ChildItem -Recurse output_splitmilp_regression_m1g -Filter statistics.txt | ForEach-Object { Select-String StatOverallOrdersHandled $_.FullName }
```

Expected: `StatOverallOrdersHandled` 與 Task 1 baseline **完全一致**（原檔未動＋引擎零改動；若不一致先重跑一次排除 WHCA* 牆鐘雜訊，仍不一致 = 回歸失敗，必須調查）。

- [ ] **Step 4: SplitHeuristic 回歸再確認（logger 重構後全鏈路）**

```powershell
& .\RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett Material\Instances\CoreBenchmark\small\split_m2.xconf output_splitmilp_regression_splith 0
Get-ChildItem -Recurse output_splitmilp_regression_splith -Filter statistics.txt | ForEach-Object { Select-String StatOverallOrdersHandled $_.FullName }
```

Expected: **608**（Spec 1 最終 m2 數字）。

- [ ] **Step 5: Commit**

```powershell
git add Material\Instances\CoreBenchmark\small\split_milp_m1.xconf Material\Instances\CoreBenchmark\small\split_milp_m2.xconf
git commit -m "feat: split_milp_m1/m2 xconf; SplitM1G smoke + M0/heuristic regression verified"
```

---

## 驗收總表（全部 task 完成後）

| 項目 | 判準 |
|---|---|
| 測試 | RAWSimO.Tests 22/22（15 舊 + 7 decoder） |
| M0 回歸 | m1g.xconf seed0 = Task 1 快照值（完全一致） |
| Heuristic 回歸 | split_m1=599（Task 3）、split_m2=608（Task 7） |
| SplitM1G 煙霧 | m1/m2 seed0 跑完無 exception、handled 與 M0 同量級、splitorders.csv + splitm1g_decision_log.csv 產出 |
| 原版不動 | `git diff` 不含 M1GManager.cs / HADGSManager.cs / BotTask.cs / BalancedBotManager.cs / BotManagerPodSelection.cs |
