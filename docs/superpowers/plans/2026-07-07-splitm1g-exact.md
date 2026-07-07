# SplitM1GExact Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new MILP-based order-splitting manager (`SplitM1GExact`) that decides pod-level
item attribution (`q[i,o,p,s]`, a 4-dimensional quantity variable) directly inside the Gurobi
model, replacing the post-solve greedy Ziops pod-attribution pass used by the existing
`SplitM1GManager` (Spec 2). The objective's per-unit reward is replaced with a per-order
completion reward.

**Architecture:** New mirrored manager `SplitM1GExactManager : M1GManager` (does NOT inherit
`SplitM1GManager` — independent sibling, same pattern Spec 2 used relative to base `M1GManager`).
New config `SplitM1GExactConfiguration : SplitM1GConfiguration` (reuses the `CrossTime` M1/M2
toggle). A new pure-logic helper `SplitM1GExactAggregator` collapses the pod dimension of a
solved `q` before feeding the existing `SplitMilpDecoder.Decode` (Spec 2, unchanged) to reuse
its station-level child/fast-path decision logic.

**Tech Stack:** C# 7.3 / .NET Framework 4.8, Gurobi 12.0.3 via `RAWSimO.SolverWrappers.LinearModel`,
custom hand-rolled test runner in `RAWSimO.Tests` (no NuGet/xUnit in this legacy solution).

## Global Constraints

- Build: `MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release` — x64 only (Gurobi has no x86 build). Locate MSBuild via: `Get-ChildItem -Path "C:\Program Files (x86)\Microsoft Visual Studio","C:\Program Files\Microsoft Visual Studio" -Recurse -Filter MSBuild.exe | Select-Object -First 1 -ExpandProperty FullName`.
- C# 7.3 (legacy `net48` csproj): new `.cs` files need a manual `<Compile Include>` entry — MSBuild will not auto-discover them.
- **Never modify** `RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs`, `HADGSManager.cs`, or `SplitM1GManager.cs` (Spec 2, already merged-in on this branch). This plan only adds new files plus small, additive edits to shared config/wiring files (enum, `Controller.cs`, `.csproj`).
- Objective weights: `w1 = 1` (distance), `w2 = OrderRewardWeight` (default `-40`, per-order-completion reward), `w3 = 1000` (idle-slot penalty). These are literal values in `SolveSplitExact`, not guesses — copy them exactly.
- Config token: `OBSPLITM1GX`. New enum value: `OrderBatchingMethodType.SplitM1GExact`.
- Baseline regression numbers to protect (must stay bit-identical / unaffected by this plan): Spec 2's `split_milp_m1.xconf`/`split_milp_m2.xconf` runs (599/611 handled-count family, 4o10b positive-result numbers in `project_split_4o10b_result.md`). This plan never edits those files or the code they exercise.
- Test runner: `RAWSimO.Tests` is a console app (no xUnit). Tests are registered via `TestRunner.Add(name, () => { ... })` inside a class's static `Register()` method, called from `RAWSimO.Tests/Program.cs::Main`. Assertions: `TestRunner.AssertTrue(bool, string)`, `TestRunner.AssertEqual(int, int, string)`, `TestRunner.AssertThrows<T>(Action, string)`. Run via: build `RAWSimO.Tests.csproj` (x64 Release) then execute the produced `RAWSimO.Tests.exe`; exit code 0 = all passed, console shows `[PASS]`/`[FAIL]` per test plus a final `"N/M passed"` line.

---

### Task 1: Config plumbing + enum + Controller wiring + empty manager stub

**Files:**
- Modify: `RAWSimO.Core/Configurations/MethodConfiguration.cs:334-339` (enum), `:768-770` (XmlInclude)
- Modify: `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs:944-966` (add new config class after `SplitM1GConfiguration`)
- Modify: `RAWSimO.Core/Control/Controller.cs:121` (switch case)
- Create: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs` (stub only — constructor + empty `DecideAboutPendingOrders` override; full logic lands in later tasks)
- Modify: `RAWSimO.Core/RAWSimO.Core.csproj` (add `<Compile Include>`)

**Interfaces:**
- Produces: `SplitM1GExactConfiguration` (public class, field `double OrderRewardWeight = -40`, inherited field `bool CrossTime` from `SplitM1GConfiguration`), `OrderBatchingMethodType.SplitM1GExact` (enum value), `SplitM1GExactManager` (public class, constructor `SplitM1GExactManager(Instance instance)`).

- [ ] **Step 1: Add the enum value**

Edit `RAWSimO.Core/Configurations/MethodConfiguration.cs`. Find:

```csharp
        /// <summary>
        /// The MILP-based order-splitting manager (M1G with shi2 relaxed to unit-level q[o,i,s]).
        /// </summary>
        SplitM1G,
    }
```

Replace with:

```csharp
        /// <summary>
        /// The MILP-based order-splitting manager (M1G with shi2 relaxed to unit-level q[o,i,s]).
        /// </summary>
        SplitM1G,
        /// <summary>
        /// SplitM1G with pod-level attribution decided inside the MILP (q[i,o,p,s], 4D), instead
        /// of the post-solve greedy Ziops pass. Reward is per-order-completion, not per-unit.
        /// </summary>
        SplitM1GExact,
    }
```

- [ ] **Step 2: Add the XmlInclude attribute**

In the same file, find:

```csharp
    [XmlInclude(typeof(SplitM1GConfiguration))]
    public abstract class OrderBatchingConfiguration : ControllerConfigurationBase
```

Replace with:

```csharp
    [XmlInclude(typeof(SplitM1GConfiguration))]
    [XmlInclude(typeof(SplitM1GExactConfiguration))]
    public abstract class OrderBatchingConfiguration : ControllerConfigurationBase
```

- [ ] **Step 3: Add the config class**

Edit `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`. Find the end of `SplitM1GConfiguration`:

```csharp
        public bool CrossTime = true;
        /// <summary>
        /// Per-unit assignment reward w2' in the objective (negative = reward). Replaces the
        /// per-order reward w2=-40 of plain M1G; -40 keeps the same magnitude per unit.
        /// </summary>
        public double UnitRewardWeight = -40;
    }

    #endregion
}
```

Replace with (adds the new class right after, before `#endregion`):

```csharp
        public bool CrossTime = true;
        /// <summary>
        /// Per-unit assignment reward w2' in the objective (negative = reward). Replaces the
        /// per-order reward w2=-40 of plain M1G; -40 keeps the same magnitude per unit.
        /// </summary>
        public double UnitRewardWeight = -40;
    }

    /// <summary>
    /// SplitM1G with pod-level attribution decided inside the MILP: q[i,o,p,s] replaces
    /// q[i,o,s], so the solver itself picks which specific pod serves each unit instead of a
    /// post-solve greedy pass. Reward switches from per-unit (UnitRewardWeight) to per-order
    /// completion, fixing the known orders-vs-items confound of plain SplitM1G.
    /// </summary>
    public class SplitM1GExactConfiguration : SplitM1GConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SplitM1GExact; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBSPLITM1GX"; }
        /// <summary>
        /// Per-order-completion reward w2 in the objective (negative = reward). Applies to
        /// zfullx[o] (M1e) or zdonex[o] (M2e) — not used the way UnitRewardWeight (inherited,
        /// unused here) was in SplitM1G.
        /// </summary>
        public double OrderRewardWeight = -40;
    }

    #endregion
}
```

- [ ] **Step 4: Wire the Controller switch case**

Edit `RAWSimO.Core/Control/Controller.cs`. Find:

```csharp
                case OrderBatchingMethodType.SplitM1G: OrderManager = new SplitM1GManager(instance); break;
                default: throw new ArgumentException("Unknown order manager: " + instance.ControllerConfig.OrderBatchingConfig.GetMethodType());
```

Replace with:

```csharp
                case OrderBatchingMethodType.SplitM1G: OrderManager = new SplitM1GManager(instance); break;
                case OrderBatchingMethodType.SplitM1GExact: OrderManager = new SplitM1GExactManager(instance); break;
                default: throw new ArgumentException("Unknown order manager: " + instance.ControllerConfig.OrderBatchingConfig.GetMethodType());
```

- [ ] **Step 5: Create the manager stub**

Create `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs`:

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
    /// MILP-based order-splitting manager with pod-level attribution decided inside the model:
    /// q[i,o,p,s] (4D) replaces SplitM1G's q[i,o,s] (3D), so the solver itself picks which pod
    /// serves each unit instead of a post-solve greedy pass. Independent sibling of
    /// SplitM1GManager (Spec 2) - does not inherit it, mirrors M1GManager directly.
    /// See docs/superpowers/specs/2026-07-07-splitm1g-exact-design.md.
    /// </summary>
    public class SplitM1GExactManager : M1GManager
    {
        /// <summary>
        /// Creates a new instance of this controller.
        /// </summary>
        /// <param name="instance">The instance this controller belongs to.</param>
        public SplitM1GExactManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GExactConfiguration;
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }

        /// <summary>
        /// The split-specific config of this controller.
        /// </summary>
        private SplitM1GExactConfiguration _splitConfig;

        /// <summary>
        /// Shared consolidation CSV logger (splitorders.csv) - same class Spec 2 extracted.
        /// </summary>
        private SplitConsolidationLogger _logger;

        /// <summary>
        /// This is called to decide about potentially pending orders. Full logic lands in
        /// later tasks of this plan (InitializeSplitExact / SolveSplitExact wiring).
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
        }
    }
}
```

- [ ] **Step 6: Add the csproj compile entry**

Edit `RAWSimO.Core/RAWSimO.Core.csproj`. Find:

```xml
    <Compile Include="Control\Defaults\OrderBatching\SplitM1GManager.cs" />
    <Compile Include="Control\Defaults\OrderBatching\SplitMilpDecoder.cs" />
    <Compile Include="Control\Defaults\OrderBatching\SplitConsolidationLogger.cs" />
```

Replace with:

```xml
    <Compile Include="Control\Defaults\OrderBatching\SplitM1GManager.cs" />
    <Compile Include="Control\Defaults\OrderBatching\SplitM1GExactManager.cs" />
    <Compile Include="Control\Defaults\OrderBatching\SplitMilpDecoder.cs" />
    <Compile Include="Control\Defaults\OrderBatching\SplitConsolidationLogger.cs" />
```

- [ ] **Step 7: Build to verify it compiles**

Run:
```
$msbuild = Get-ChildItem -Path "C:\Program Files (x86)\Microsoft Visual Studio","C:\Program Files\Microsoft Visual Studio" -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
& $msbuild "RAWSimOWithSolverWrapping.sln" /p:Platform=x64 /p:Configuration=Release /v:minimal
```
Expected: `RAWSimO.Core -> ...RAWSimO.Core.dll` succeeds, no new errors (pre-existing `CS0649`/`CS0414` warnings in unrelated files are fine).

- [ ] **Step 8: Commit**

```bash
git add RAWSimO.Core/Configurations/MethodConfiguration.cs RAWSimO.Core/Configurations/MethodConfigurationsOB.cs RAWSimO.Core/Control/Controller.cs RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs RAWSimO.Core/RAWSimO.Core.csproj
git commit -m "feat: SplitM1GExact config plumbing + manager stub (Task 1)"
```

---

### Task 2: Pure pod-quantity aggregation helper (TDD)

This is the one piece of new logic that is pure (no Gurobi, no `Instance`) and worth testing in
isolation: collapsing a flat list of `(SKU, quantity)` pairs coming from potentially many
different pods at one (order, station) pair into a single per-SKU dictionary, which is exactly
the shape `SplitMilpDecoder.Decode` (Spec 2, unchanged) already expects as its per-station input.

**Files:**
- Create: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactAggregator.cs`
- Modify: `RAWSimO.Core/RAWSimO.Core.csproj` (add `<Compile Include>`)
- Create: `RAWSimO.Tests/SplitM1GExactAggregatorTests.cs`
- Modify: `RAWSimO.Tests/RAWSimO.Tests.csproj`, `RAWSimO.Tests/Program.cs`

**Interfaces:**
- Produces: `SplitM1GExactAggregator.AggregatePodQuantities(IEnumerable<KeyValuePair<ItemDescription,int>> perPodQuantities) : Dictionary<ItemDescription,int>` — sums quantities per SKU across however many pods contributed to that SKU, dropping non-positive entries.
- Consumes (Task 4): feeds `SplitMilpDecoder.Decode`'s `perStationQuantities` parameter (already defined in Spec 2, unchanged).

- [ ] **Step 1: Write the failing tests**

Create `RAWSimO.Tests/SplitM1GExactAggregatorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core;
using RAWSimO.Core.Control.Defaults.OrderBatching;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class SplitM1GExactAggregatorTests
    {
        private static Instance _instance = new Instance();
        private static ItemDescription Sku() { return new SimpleItemDescription(_instance); }
        private static KeyValuePair<ItemDescription, int> Q(ItemDescription i, int q)
        { return new KeyValuePair<ItemDescription, int>(i, q); }

        public static void Register()
        {
            TestRunner.Add("Aggregator_SumsAcrossPods_SameSku", () =>
            {
                var a = Sku();
                var result = SplitM1GExactAggregator.AggregatePodQuantities(new[] { Q(a, 2), Q(a, 3) });
                TestRunner.AssertEqual(1, result.Count, "one distinct SKU");
                TestRunner.AssertEqual(5, result[a], "2 (pod X) + 3 (pod Y) = 5");
            });
            TestRunner.Add("Aggregator_KeepsSkusSeparate", () =>
            {
                var a = Sku(); var b = Sku();
                var result = SplitM1GExactAggregator.AggregatePodQuantities(new[] { Q(a, 2), Q(b, 4) });
                TestRunner.AssertEqual(2, result.Count, "two distinct SKUs");
                TestRunner.AssertEqual(2, result[a], "SKU a untouched by SKU b's entry");
                TestRunner.AssertEqual(4, result[b], "SKU b untouched by SKU a's entry");
            });
            TestRunner.Add("Aggregator_DropsNonPositive", () =>
            {
                var a = Sku(); var b = Sku();
                var result = SplitM1GExactAggregator.AggregatePodQuantities(new[] { Q(a, 0), Q(b, -1), Q(a, 3) });
                TestRunner.AssertEqual(1, result.Count, "only the positive entry for a survives");
                TestRunner.AssertEqual(3, result[a], "zero-quantity pod entries do not contribute");
                TestRunner.AssertTrue(!result.ContainsKey(b), "negative-quantity SKU is dropped entirely");
            });
            TestRunner.Add("Aggregator_EmptyInput_ReturnsEmpty", () =>
            {
                var result = SplitM1GExactAggregator.AggregatePodQuantities(Enumerable.Empty<KeyValuePair<ItemDescription, int>>());
                TestRunner.AssertEqual(0, result.Count, "no pods contributed -> empty dictionary");
            });
        }
    }
}
```

- [ ] **Step 2: Register the test class**

Edit `RAWSimO.Tests/Program.cs`. Find:

```csharp
            OrderSplitTests.Register();
            SplitPlannerTests.Register();
            SplitMilpDecoderTests.Register();
            return TestRunner.RunAll();
```

Replace with:

```csharp
            OrderSplitTests.Register();
            SplitPlannerTests.Register();
            SplitMilpDecoderTests.Register();
            SplitM1GExactAggregatorTests.Register();
            return TestRunner.RunAll();
```

- [ ] **Step 3: Add the test csproj entry**

Edit `RAWSimO.Tests/RAWSimO.Tests.csproj`. Find:

```xml
    <Compile Include="SplitMilpDecoderTests.cs" />
```

Replace with:

```xml
    <Compile Include="SplitMilpDecoderTests.cs" />
    <Compile Include="SplitM1GExactAggregatorTests.cs" />
```

- [ ] **Step 4: Run tests to verify they fail (compile error - type doesn't exist yet)**

Build `RAWSimO.Tests.csproj` (x64 Release) using the same MSBuild pattern as Task 1 Step 7, targeting the test project:
```
& $msbuild "RAWSimO.Tests\RAWSimO.Tests.csproj" /p:Platform=x64 /p:Configuration=Release /v:minimal
```
Expected: FAIL with `CS0246: The type or namespace name 'SplitM1GExactAggregator' could not be found`.

- [ ] **Step 5: Write the implementation**

Create `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactAggregator.cs`:

```csharp
using System.Collections.Generic;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure (Gurobi-free) helper for SplitM1GExact: collapses a flat list of per-pod
    /// (SKU, quantity) extraction amounts for one (order, station) pair into a single per-SKU
    /// dictionary, summing across however many pods contributed. Feeds the result into
    /// SplitMilpDecoder.Decode's per-station quantity input (unchanged from Spec 2), reusing
    /// its station-level child/fast-path decision logic without needing a pod-aware decoder.
    /// </summary>
    public static class SplitM1GExactAggregator
    {
        /// <summary>
        /// Sums quantities per SKU across all contributing pods. Non-positive entries are
        /// dropped (a solved q[i,o,p,s] of 0 for some pod should not add a zero/negative key).
        /// </summary>
        public static Dictionary<ItemDescription, int> AggregatePodQuantities(
            IEnumerable<KeyValuePair<ItemDescription, int>> perPodQuantities)
        {
            Dictionary<ItemDescription, int> result = new Dictionary<ItemDescription, int>();
            foreach (var entry in perPodQuantities)
            {
                if (entry.Value <= 0)
                    continue;
                if (result.ContainsKey(entry.Key))
                    result[entry.Key] += entry.Value;
                else
                    result[entry.Key] = entry.Value;
            }
            return result;
        }
    }
}
```

- [ ] **Step 6: Add the csproj compile entry for the implementation**

Edit `RAWSimO.Core/RAWSimO.Core.csproj`. Find:

```xml
    <Compile Include="Control\Defaults\OrderBatching\SplitM1GExactManager.cs" />
```

Replace with:

```xml
    <Compile Include="Control\Defaults\OrderBatching\SplitM1GExactManager.cs" />
    <Compile Include="Control\Defaults\OrderBatching\SplitM1GExactAggregator.cs" />
```

- [ ] **Step 7: Run tests to verify they pass**

Rebuild both `RAWSimO.Core.csproj` and `RAWSimO.Tests.csproj` (x64 Release), then run the produced `RAWSimO.Tests\bin\x64\Release\RAWSimO.Tests.exe`.
Expected: `[PASS] Aggregator_SumsAcrossPods_SameSku`, `[PASS] Aggregator_KeepsSkusSeparate`, `[PASS] Aggregator_DropsNonPositive`, `[PASS] Aggregator_EmptyInput_ReturnsEmpty`, plus all pre-existing tests still `[PASS]`, ending in `"N/N passed"` with N = previous count + 4.

- [ ] **Step 8: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactAggregator.cs RAWSimO.Core/RAWSimO.Core.csproj RAWSimO.Tests/SplitM1GExactAggregatorTests.cs RAWSimO.Tests/RAWSimO.Tests.csproj RAWSimO.Tests/Program.cs
git commit -m "feat: SplitM1GExact pod-quantity aggregation helper + tests (Task 2)"
```

---

### Task 3: `InitializeSplitExact` + mirrored private helpers

Mirrors Spec 2's `InitializeSplit` (residual-demand snapshot, `Pb`/`Pa`/`Ra`/`Rb`/`R` construction)
verbatim, replacing only the tail block that appends decision-variable name lists: instead of
3D `q`/`ysp`/`zfull`(M1-only), this builds 4D `q[i,o,p,s]`, `ysp[o,s]`, and a completion flag
that exists in **both** M1e and M2e modes (`zfullx`/`zdonex`).

Because `SplitM1GExactManager` does not inherit `SplitM1GManager`, the private helper methods
Spec 2 wrote (`GenerateOiSKUSplit`, `GenerateOdSplit`, starve-aware cost wrappers, the
`SplitSolveResult` result class, the per-decision CSV logger) are not inherited either — they
must be copied into this file too, exactly as Spec 2 copied them from base-class concepts.

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs` (add private helpers + `InitializeSplitExact`)

**Interfaces:**
- Produces: `InitializeSplitExact(out PiSKU, out OiSKU, out variableNames, out Cs, out pendingOrders, out inboundPods, out Ra, out Rb, out R, out Pb, out Pa, out PodToBot, out residuals) : HashSet<Pod>` — same 13-out-param shape Spec 2's `InitializeSplit` uses (`SplitM1GManager.cs:164-168`), so Task 5's `DecideAboutPendingOrders` wiring is a straight copy of Spec 2's wiring code. `variableNames[7]` = `q[i,o,p,s]` symbols (fields: `order`, `outputstation`, `skui`, `pod`, `name`), `variableNames[8]` = `ysp[o,s]` symbols (`order`, `outputstation`, `name`), `variableNames[9]` = completion-flag symbols (`order`, `name`) named `zfullx_<orderId>` when `!CrossTime` or `zdonex_<orderId>` when `CrossTime`.
- Consumes: `CreatedeVarName` (public, `M1GManager.cs:429`, inherited), `GeneratePiSKU`/`GeneratePs`/`GenerateCs`/`IsAvailabletoPiSKU`/`CanUseReturnPendingBot` (protected/public, inherited from `M1GManager`).

- [ ] **Step 1: Add the mirrored private helpers and residual-demand generators**

Edit `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs`. Insert after the
`_logger` field declaration (before the `DecideAboutPendingOrders` method):

```csharp
        /// <summary>
        /// Order enters Od (urgent-order set) if due within this many seconds (mirrors the
        /// base's private DueTimeOrderofMP, copied because it's private in M1GManager).
        /// </summary>
        private static readonly double _dueTimeOrderofMP = TimeSpan.FromMinutes(30).TotalSeconds;

        /// <summary>
        /// Residual-demand version of GenerateOiSKU: indexes pending orders by the SKUs they
        /// still need (RemainingPositions), not their full demand. Copied verbatim from Spec 2's
        /// SplitM1GManager (private there, not inherited).
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
        /// Residual-demand version of GenerateOd. Copied verbatim from Spec 2's SplitM1GManager.
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

        // ── Starve-aware cost wrappers (copied verbatim from Spec 2's SplitM1GManager; base
        //    members are private there, so this repeats the mirroring rather than inheriting) ──
        private bool _saEnabledExact;
        private double _saNominalSpeedExact;
        private double _saFixedParamExact;
        private Dictionary<int, double> _saEstByStationExact = new Dictionary<int, double>();
        private Dictionary<int, double> _saRepBotPodTimeExact = new Dictionary<int, double>();

        private void PrepareStarveAwareExact(IEnumerable<Pod> pods, Dictionary<OutputStation, int> Cs, HashSet<Bot> Ra)
        {
            _saEnabledExact = Instance != null && Instance.SettingConfig != null && Instance.SettingConfig.StarveAwareCostEnabled;
            if (!_saEnabledExact)
                return;
            _saFixedParamExact = Instance.SettingConfig.StarveAwareFixedParam;
            double cfgSpeed = Instance.SettingConfig.StarveAwareNominalSpeed;
            _saNominalSpeedExact = cfgSpeed > 0.0
                ? cfgSpeed
                : (Instance.Bots != null && Instance.Bots.Count > 0
                    ? Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity))
                    : 1.0);
            double now = Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;
            _saEstByStationExact = new Dictionary<int, double>();
            foreach (var s in Cs.Keys)
                _saEstByStationExact[s.ID] = StarveAwareCost.Est(s, now);
            _saRepBotPodTimeExact = new Dictionary<int, double>();
            foreach (var p in pods)
            {
                double best = double.PositiveInfinity;
                foreach (var r in Ra)
                    best = Math.Min(best, StarveAwareCost.TravelTime(EstimateBotPodDistance(r, p), _saNominalSpeedExact));
                _saRepBotPodTimeExact[p.ID] = best;
            }
        }

        private double ExactBotPodCost(Bot robot, Pod pod)
        {
            double d = EstimateBotPodDistance(robot, pod);
            return _saEnabledExact ? StarveAwareCost.TravelTime(d, _saNominalSpeedExact) : d;
        }

        private double ExactPodStationCost(Pod pod, OutputStation station)
        {
            double d = EstimatePodStationDistance(pod, station);
            if (!_saEnabledExact)
                return d;
            double podStationTime = StarveAwareCost.TravelTime(d, _saNominalSpeedExact);
            double repBotPod = (_saRepBotPodTimeExact.TryGetValue(pod.ID, out var t) && !double.IsPositiveInfinity(t)) ? t : 0.0;
            double taCost = podStationTime + repBotPod;
            double est = _saEstByStationExact.TryGetValue(station.ID, out var e) ? e : double.PositiveInfinity;
            double penalty = StarveAwareCost.DelayPenalty(taCost, est, _saFixedParamExact);
            return podStationTime + penalty;
        }

        /// <summary>
        /// The committed outcome of one SplitM1GExact solve, consumed by DecideAboutPendingOrders.
        /// </summary>
        private class SplitExactSolveResult
        {
            public Dictionary<Symbol, int> NewZiops = new Dictionary<Symbol, int>();
            public List<Symbol> Allocations = new List<Symbol>();
            public HashSet<Order> SplitParents = new HashSet<Order>();
        }

        // ── Per-decision summary logger (diagnostic; separate file from Spec 2's so the two
        //    models' decision logs never collide when both are run against the same output dir) ──
        private System.IO.StreamWriter _exactDecisionLog;
        private int _exactDecisionIndex = 0;
        private void WriteExactDecisionLog(bool solved, double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int nXps, int nChildren, int nFastPath, int unitsAssigned, double sumUs, double objective, double optSec)
        {
            if (_exactDecisionLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _exactDecisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "splitm1gx_decision_log.csv"), false) { AutoFlush = true };
                _exactDecisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsInModel,xps,children,fastPath,units,sumUs,objective,solveSec");
            }
            _exactDecisionLog.WriteLine(string.Join(",", new string[] {
                _exactDecisionIndex.ToString(),
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
            _exactDecisionIndex++;
        }
```

- [ ] **Step 2: Add `InitializeSplitExact`**

In the same file, insert `InitializeSplitExact` right after the helpers added in Step 1:

```csharp
        /// <summary>
        /// Split-exact version of Initialize: snapshots residual demand exactly like Spec 2's
        /// InitializeSplit, but appends 4D q[i,o,p,s] / ysp[o,s] / completion-flag variable name
        /// lists (keys 7/8/9) instead of Spec 2's 3D q + M1-only zfull. The pod dimension pulls
        /// candidates from PiSKU (both Pb and Pa pods that physically carry the SKU) - link-up
        /// needs a real candidate for every pod actually able to supply the unit, not only
        /// newly-claimable (Pa) ones the way Spec 2's dops did.
        /// </summary>
        private HashSet<Pod> InitializeSplitExact(out Dictionary<ItemDescription, List<Pod>> PiSKU, out Dictionary<ItemDescription, List<Order>> OiSKU,
            out Dictionary<int, List<Symbol>> variableNames, out Dictionary<OutputStation, int> Cs, out HashSet<Order> pendingOrders,
            out Dictionary<OutputStation, HashSet<Pod>> inboundPods, out HashSet<Bot> Ra, out HashSet<Bot> Rb,
            out HashSet<Bot> R, out HashSet<Pod> Pb, out HashSet<Pod> Pa, out Dictionary<Pod, Bot> PodToBot,
            out Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
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
            if (_splitConfig != null && _splitConfig.CrossTime)
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.Any(p => IsAvailabletoPiSKU(p.Key) >= 1)));
            else
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.All(p => IsAvailabletoPiSKU(p.Key) >= p.Value)));
            HashSet<Order> Od = GenerateOdSplit(pendingOrders, PiSKU);
            if (Od.Count > Cs.Values.Sum())
                pendingOrders = new HashSet<Order>(Od);
            OiSKU = GenerateOiSKUSplit(pendingOrders);
            residuals = pendingOrders.ToDictionary(o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            variableNames = CreatedeVarName(PiSKU, OiSKU, allPods, pendingOrders, Cs, R, Pa1, out Pa);
            bool crossTimeExact = _splitConfig != null && _splitConfig.CrossTime;
            List<Symbol> deVarNameq = new List<Symbol>();
            List<Symbol> deVarNamey = new List<Symbol>();
            List<Symbol> deVarNamez = new List<Symbol>();
            foreach (var order in pendingOrders)
            {
                foreach (var station in Cs.Keys)
                    deVarNamey.Add(new Symbol { order = order, outputstation = station, name = "yspx" + "_" + order.ID.ToString() + "_" + station.ID.ToString() });
                foreach (var sku in residuals[order].Where(p => PiSKU.ContainsKey(p.Key)))
                {
                    foreach (var pod in PiSKU[sku.Key])
                    {
                        foreach (var station in Cs.Keys)
                            deVarNameq.Add(new Symbol { order = order, outputstation = station, skui = sku.Key, pod = pod, name = "qx" + "_" + sku.Key.ID.ToString() + "_" + order.ID.ToString() + "_" + pod.ID.ToString() + "_" + station.ID.ToString() });
                    }
                }
                deVarNamez.Add(new Symbol { order = order, name = (crossTimeExact ? "zdonex" : "zfullx") + "_" + order.ID.ToString() });
            }
            variableNames.Add(7, deVarNameq);
            variableNames.Add(8, deVarNamey);
            variableNames.Add(9, deVarNamez);
            return allPods;
        }
```

- [ ] **Step 3: Build to verify it compiles**

Run the same MSBuild command as Task 1 Step 7 targeting the solution. Expected: succeeds
(this method is not called from anywhere yet — dead code is fine, it will be wired in Task 5).

- [ ] **Step 4: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs
git commit -m "feat: SplitM1GExact InitializeSplitExact + mirrored private helpers (Task 3)"
```

---

### Task 4: `SolveSplitExact` — MILP build, solve, and direct commit (no greedy pass)

Builds the 4D-`q` MILP (link-up/link-down/`eshi4`/`eshi6`/`eshi7`/`eshi11`/`eshi8`/`eshi9`/
`eshi10`/`eshi13'`/completion-linkage constraints, objective with per-order-completion reward),
solves it, and commits the solution directly — no `dops`/greedy-Ziops/unused-pod-release pass
(see spec §5 and §7: `dops`/shi12' are dropped entirely, `shi13'` sums `q` directly instead).

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs` (add `SolveSplitExact`)

**Interfaces:**
- Consumes: `SplitM1GExactAggregator.AggregatePodQuantities` (Task 2), `SplitMilpDecoder.Decode` (Spec 2, unchanged, signature `Decode(IList<KeyValuePair<ItemDescription,int>> remaining, IList<Dictionary<ItemDescription,int>> perStationQuantities, bool crossTime, out bool fullyAssigned) : List<KeyValuePair<int, Dictionary<ItemDescription,int>>>`), `InitializeSplitExact` (Task 3).
- Produces: `SolveSplitExact(SolverType type, Dictionary<ItemDescription,List<Pod>> PiSKU, Dictionary<ItemDescription,List<Order>> OiSKU, IEnumerable<Pod> Pods, Dictionary<OutputStation,int> Cs, Dictionary<int,List<Symbol>> variableNames, HashSet<Order> pendingOrders, Dictionary<OutputStation,HashSet<Pod>> inboundPods, HashSet<Bot> Ra, HashSet<Bot> Rb, HashSet<Bot> R, HashSet<Pod> Pb, HashSet<Pod> Pa, Dictionary<Pod,Bot> PodToBot, Dictionary<Order,Dictionary<ItemDescription,int>> residuals) : SplitExactSolveResult` — consumed by Task 5's `DecideAboutPendingOrders`.

- [ ] **Step 1: Add `SolveSplitExact`**

In `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs`, insert after
`InitializeSplitExact`:

```csharp
        /// <summary>
        /// Builds and solves the SplitM1GExact MILP (q[i,o,p,s], 4D pod-exact attribution) and
        /// commits the solution directly: robot-pod claiming (base parity), child creation via
        /// SplitMilpDecoder (Spec 2, unchanged - fed pod-aggregated quantities), and per-(sku,
        /// order-or-child,pod,station) Ziops writes read straight off the solved q values. No
        /// greedy Ziops pass, no dops, no unused-pod release - link-up + shi13' make every
        /// selected pod's usage exact by construction (see spec §5/§7).
        /// </summary>
        private SplitExactSolveResult SolveSplitExact(SolverType type, Dictionary<ItemDescription, List<Pod>> PiSKU,
            Dictionary<ItemDescription, List<Order>> OiSKU, IEnumerable<Pod> Pods, Dictionary<OutputStation, int> Cs,
            Dictionary<int, List<Symbol>> variableNames, HashSet<Order> pendingOrders,
            Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Bot> Ra, HashSet<Bot> Rb, HashSet<Bot> R,
            HashSet<Pod> Pb, HashSet<Pod> Pa, Dictionary<Pod, Bot> PodToBot,
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            LinearModel wrapper = new LinearModel(type, (string s) => { Console.Write(s); });
            SplitExactSolveResult result = new SplitExactSolveResult();
            bool crossTime = _splitConfig != null && _splitConfig.CrossTime;
            List<Symbol> deVarNamexps = variableNames[1];
            List<Symbol> deVarNameyrp = variableNames[4];
            List<Symbol> deVarNameus = variableNames[5];
            List<Symbol> deVarNameq = variableNames[7];
            List<Symbol> deVarNamey = variableNames[8];
            List<Symbol> deVarNamez = variableNames[9];
            double w1 = 1;
            double w2 = _splitConfig != null ? _splitConfig.OrderRewardWeight : -40;
            double w3 = 1000;
            int maxCs = Cs.Count > 0 ? Cs.Values.Max() : 1;
            int maxR = residuals.Count > 0 ? residuals.Values.SelectMany(d => d.Values).DefaultIfEmpty(1).Max() : 1;
            VariableCollection<string> variablesBinary = new VariableCollection<string>(wrapper, VariableType.Binary, 0, 1, (string s) => { return s; });
            VariableCollection<string> variablesUs = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxCs, (string s) => { return s; });
            VariableCollection<string> variablesQ = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxR, (string s) => { return s; });
            PrepareStarveAwareExact(Pods, Cs, Ra);
            PrepareDecisionExtras(Pods, Cs, Ra);
            // Objective: w1*(pod-station + bot-pod distance) + w2*(completion reward) + w3*(idle slots)
            if (Ra.Count() > 0)
                wrapper.SetObjective((LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (ExactPodStationCost(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation))), wrapper)
                    + LinearExpression.Sum(deVarNameyrp.Where(u => Ra.Contains(u.robot) && Instance.ResourceManager.UnusedPods.Contains(u.pod) && u.pod.Waypoint != null).Select(v => variablesBinary[v.name] *
                    ExactBotPodCost(v.robot, v.pod)), wrapper)) * w1
                    + LinearExpression.Sum(deVarNamez.Select(v => variablesBinary[v.name])) * w2
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesUs[v.name])) * w3, OptimizationSense.Minimize);
            else
                wrapper.SetObjective(LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (ExactPodStationCost(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation))), wrapper) * w1
                    + LinearExpression.Sum(deVarNamez.Select(v => variablesBinary[v.name])) * w2
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesUs[v.name])) * w3, OptimizationSense.Minimize);
            // (elink1) q[i,o,p,s] <= stock[p,i] * xps[p,s] - ties demand directly to one specific pod's real inventory
            foreach (var q in deVarNameq)
                wrapper.AddConstr(variablesQ[q.name] <= q.pod.CountAvailable(q.skui) * variablesBinary["xps" + "_" + q.pod.ID.ToString() + "_" + q.outputstation.ID.ToString()], "elink1");
            // (elink2) ysp[o,s] <= sum_i sum_p q[i,o,p,s] - forbids an empty child
            foreach (var y in deVarNamey)
                wrapper.AddConstr(variablesBinary[y.name] <= LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == y.order.ID && v.outputstation.ID == y.outputstation.ID).Select(v => variablesQ[v.name])), "elink2");
            // (eshi4) pure slot conservation
            foreach (var station in Cs.Keys)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamey.Where(v => v.outputstation.ID == station.ID).Select(v => variablesBinary[v.name])) == Cs[station] - variablesUs["us" + "_" + station.ID.ToString()], "eshi4");
            // (eshi6) pod assigned to at most one station
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "eshi6");
            // (eshi7/eshi11) inherited (Pb) pods/bots stay fixed from the previous decision
            foreach (var station in inboundPods)
            {
                foreach (var pod in station.Value)
                {
                    if (!Pb.Contains(pod))
                        continue;
                    wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.Key.ID.ToString()] == 1, "eshi7");
                    wrapper.AddConstr(variablesBinary["yrp" + "_" + PodToBot[pod].ID.ToString() + "_" + pod.ID.ToString()] == 1, "eshi11");
                }
            }
            // (eshi8) a pod assigned to a station needs a bot
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])), "eshi8");
            // (eshi9) at most one bot per pod
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "eshi9");
            // (eshi10) at most one pod per bot
            foreach (var robot in R)
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.robot.ID == robot.ID).Select(v => variablesBinary[v.name])) <= 1, "eshi10");
            // (eshi13') a newly-claimed pod must actually be consumed - summed directly against q,
            // not the dops proxy Spec 2 used (see spec §3.3/§7: dops never linked to real q usage)
            foreach (var pod in Pa)
            {
                foreach (var station in Cs.Keys)
                    wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()]
                        <= LinearExpression.Sum(deVarNameq.Where(v => v.pod.ID == pod.ID && v.outputstation.ID == station.ID).Select(v => variablesQ[v.name])), "eshi13");
            }
            // (eM1/eM2/eM2done) per-SKU completion linkage, aggregated across stations AND pods
            foreach (var order in pendingOrders)
            {
                string zname = (crossTime ? "zdonex" : "zfullx") + "_" + order.ID.ToString();
                foreach (var sku in residuals[order].Where(p => PiSKU.ContainsKey(p.Key)))
                {
                    var lhs = LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID).Select(v => variablesQ[v.name]));
                    if (!crossTime)
                        wrapper.AddConstr(lhs == sku.Value * variablesBinary[zname], "eM1");
                    else
                    {
                        wrapper.AddConstr(lhs <= sku.Value, "eM2");
                        wrapper.AddConstr(lhs >= sku.Value * variablesBinary[zname], "eM2done");
                    }
                }
            }
            wrapper.Update();
            DateTime _optStart = DateTime.Now;
            wrapper.Optimize();
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;
            if (wrapper.HasSolution())
            {
                List<Symbol> IsdeVarNamexps = deVarNamexps.Where(v => Math.Round(variablesBinary[v.name].GetValue()) != 0).ToList();
                List<Symbol> IsdeVarNameq = deVarNameq.Where(v => Math.Round(variablesQ[v.name].GetValue()) > 0).ToList();
                foreach (var itemName in deVarNameyrp)
                {
                    if (Math.Round(variablesBinary[itemName.name].GetValue()) != 0 && Ra.Contains(itemName.robot))
                    {
                        Instance.ResourceManager.BottoPod.Add(itemName.robot, itemName.pod);
                        Instance.ResourceManager.ClaimPod(itemName.pod, itemName.robot, BotTaskType.Extract);
                        foreach (var xps in IsdeVarNamexps.Where(v => v.pod.ID == itemName.pod.ID))
                            xps.outputstation.RegisterInboundPod(itemName.pod);
                    }
                }
                List<OutputStation> stationList = Cs.Keys.OrderBy(s => s.ID).ToList();
                int nChildren = 0, nFastPath = 0, unitsAssigned = 0;
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    List<Dictionary<ItemDescription, int>> perStation = new List<Dictionary<ItemDescription, int>>();
                    foreach (var station in stationList)
                    {
                        var podQuantities = IsdeVarNameq
                            .Where(v => v.order.ID == order.ID && v.outputstation.ID == station.ID)
                            .Select(v => new KeyValuePair<ItemDescription, int>(v.skui, (int)Math.Round(variablesQ[v.name].GetValue())));
                        perStation.Add(SplitM1GExactAggregator.AggregatePodQuantities(podQuantities));
                    }
                    bool fullyAssigned;
                    List<KeyValuePair<int, Dictionary<ItemDescription, int>>> parts =
                        SplitMilpDecoder.Decode(residuals[order].ToList(), perStation, crossTime, out fullyAssigned);
                    if (parts.Count == 0)
                        continue;
                    unitsAssigned += parts.Sum(p => p.Value.Values.Sum());
                    if (parts.Count == 1 && fullyAssigned && !order.IsSplitParent)
                    {
                        OutputStation station = stationList[parts[0].Key];
                        result.Allocations.Add(new Symbol { order = order, outputstation = station });
                        foreach (var q in IsdeVarNameq.Where(v => v.order.ID == order.ID && v.outputstation.ID == station.ID))
                        {
                            int units = (int)Math.Round(variablesQ[q.name].GetValue());
                            Symbol name = new Symbol { pod = q.pod, order = order, outputstation = station, skui = q.skui,
                                name = "ziops" + "_" + q.skui.ID.ToString() + "_" + order.ID.ToString() + "_" + q.pod.ID.ToString() + "_" + station.ID.ToString() };
                            result.NewZiops.Add(name, units);
                            Instance.ResourceManager._Ziops[station].Add(name, units);
                        }
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
                            foreach (var q in IsdeVarNameq.Where(v => v.order.ID == order.ID && v.outputstation.ID == station.ID))
                            {
                                int units = (int)Math.Round(variablesQ[q.name].GetValue());
                                Symbol name = new Symbol { pod = q.pod, order = child, outputstation = station, skui = q.skui,
                                    name = "ziops" + "_" + q.skui.ID.ToString() + "_" + child.ID.ToString() + "_" + q.pod.ID.ToString() + "_" + station.ID.ToString() };
                                result.NewZiops.Add(name, units);
                                Instance.ResourceManager._Ziops[station].Add(name, units);
                            }
                            nChildren++;
                        }
                        result.SplitParents.Add(order);
                    }
                }
                double sumUs = 0.0;
                foreach (var s in deVarNameus)
                    sumUs += Math.Round(variablesUs[s.name].GetValue());
                WriteExactDecisionLog(true, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    IsdeVarNamexps.Count, nChildren, nFastPath, unitsAssigned, sumUs, wrapper.GetObjectiveValue(), _optSec);
            }
            else
            {
                WriteExactDecisionLog(false, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    0, 0, 0, 0, 0.0, double.NaN, _optSec);
            }
            return result;
        }
```

- [ ] **Step 2: Build to verify it compiles**

Run the same MSBuild command as Task 1 Step 7. Expected: succeeds (still dead code, wired next).

- [ ] **Step 3: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs
git commit -m "feat: SplitM1GExact SolveSplitExact - 4D MILP build/solve/direct-commit (Task 4)"
```

---

### Task 5: `DecideAboutPendingOrders` wiring

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs`

**Interfaces:**
- Consumes: `InitializeSplitExact` (Task 3), `SolveSplitExact` (Task 4), `AllocateOrder` (protected, inherited from `OrderManager`), `Instance.ItemManager`/`ItemManager.TakeAvailableOrder`, `Instance.StatCustomControllerInfo.CustomLogOB1`, `Instance.Observer.TimeOrderBatchingbyMP` (all already used identically by Spec 2's `SplitM1GManager.DecideAboutPendingOrders`).

- [ ] **Step 1: Replace the stub `DecideAboutPendingOrders`**

In `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs`, find:

```csharp
        /// <summary>
        /// This is called to decide about potentially pending orders. Full logic lands in
        /// later tasks of this plan (InitializeSplitExact / SolveSplitExact wiring).
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
        }
```

Replace with:

```csharp
        /// <summary>
        /// This is called to decide about potentially pending orders (split-exact MILP version).
        /// Mirrors Spec 2's SplitM1GManager.DecideAboutPendingOrders wiring exactly - only the
        /// Initialize/Solve method names and result type differ.
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
            HashSet<Pod> allPods = InitializeSplitExact(out PiSKU, out OiSKU, out variableNames, out Cs, out pendingOrders,
                out inboundPods, out Ra, out Rb, out R, out Pb, out Pa, out PodToBot, out residuals);
            if (R.Count() > 0 && pendingOrders.Count > 0)
            {
                SplitExactSolveResult result = SolveSplitExact(SolverType.Gurobi, PiSKU, OiSKU, allPods, Cs, variableNames,
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

- [ ] **Step 2: Build to verify it compiles**

Run the same MSBuild command as Task 1 Step 7. Expected: succeeds, no more dead-code warnings
for the Task 3/4 methods (they are now called).

- [ ] **Step 3: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs
git commit -m "feat: SplitM1GExact DecideAboutPendingOrders wiring (Task 5)"
```

---

### Task 6: xconf files + smoke test + regression check

**Files:**
- Create: `Material/Instances/CoreBenchmark/small/split_milp_m1e.xconf`
- Create: `Material/Instances/CoreBenchmark/small/split_milp_m2e.xconf`

**Interfaces:** None (this task is CLI runs, not code).

- [ ] **Step 1: Create the M1e xconf**

Copy `Material/Instances/CoreBenchmark/small/split_milp_m1.xconf` to
`Material/Instances/CoreBenchmark/small/split_milp_m1e.xconf`, then change exactly two things:
line 3's `<Name>split_milp_m1</Name>` to `<Name>split_milp_m1e</Name>`, and inside the
`<OrderBatchingConfig>` block change `xsi:type="SplitM1GConfiguration"` to
`xsi:type="SplitM1GExactConfiguration"` and `<UnitRewardWeight>-40</UnitRewardWeight>` to
`<OrderRewardWeight>-40</OrderRewardWeight>`. The file uses CRLF line endings — preserve that
(no other line may differ from `split_milp_m1.xconf`, byte-for-byte, per this project's
xconf-diff convention). The resulting `<OrderBatchingConfig>` block must read exactly:

```xml
  <OrderBatchingConfig xsi:type="SplitM1GExactConfiguration">
    <Name />
    <TieBreaker>EarliestDueTime</TieBreaker>
    <FastLane>true</FastLane>
    <LateBeforeMatch>false</LateBeforeMatch>
    <FastLaneTieBreaker>EarliestDueTime</FastLaneTieBreaker>
    <CrossTime>false</CrossTime>
    <OrderRewardWeight>-40</OrderRewardWeight>
  </OrderBatchingConfig>
```

Verify byte-parity of everything else with:
```bash
diff <(sed -e '3d' -e '/xsi:type="SplitM1G/d' -e '/UnitRewardWeight\|OrderRewardWeight/d' -e '/CrossTime/d' "Material/Instances/CoreBenchmark/small/split_milp_m1.xconf") \
     <(sed -e '3d' -e '/xsi:type="SplitM1G/d' -e '/UnitRewardWeight\|OrderRewardWeight/d' -e '/CrossTime/d' "Material/Instances/CoreBenchmark/small/split_milp_m1e.xconf")
```
Expected: no output (identical outside the permitted diff lines).

- [ ] **Step 2: Create the M2e xconf**

Same as Step 1 but starting from `split_milp_m2.xconf`: `<Name>split_milp_m2e</Name>`,
`xsi:type="SplitM1GExactConfiguration"`, `<CrossTime>true</CrossTime>` (unchanged from the M2
source), `<OrderRewardWeight>-40</OrderRewardWeight>`. Run the analogous `diff` check against
`split_milp_m2.xconf`.

- [ ] **Step 3: Smoke run — M1e**

```
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe "Material\Instances\CoreBenchmark\small\small.xlayo" "Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett" "Material\Instances\CoreBenchmark\small\split_milp_m1e.xconf" "output_split_m1e_smoke" 0
```
Expected: run completes without an unhandled exception; `output_split_m1e_smoke\...\statistics.txt`
contains `StatOverallOrdersHandled` > 0 and `StatOverallOrdersPlaced` >= `StatOverallOrdersHandled`;
`splitm1gx_decision_log.csv` exists in the same run folder with at least one `solved=1` row;
note the `solveSec` column values — this is the regression-risk metric from spec §3.5/§7.

- [ ] **Step 4: Smoke run — M2e**

Same command with `split_milp_m2e.xconf` and a distinct output dir
(`output_split_m2e_smoke`). Same expectations as Step 3.

- [ ] **Step 5: Regression check — Spec 2 unaffected**

Re-run Spec 2's existing `split_milp_m1.xconf`/`split_milp_m2.xconf` against `small.xlayo` +
`small_o100_mu100.xsett` (same command shape as Step 3, swapping the xconf and output dir) and
confirm `StatOverallOrdersHandled`/`StatOverallOrdersPlaced` match the previously-recorded
599/611 family of numbers (see `project_split_4o10b_result.md` and this project's memory
files) — this plan must not have touched any code path Spec 2 exercises.

- [ ] **Step 6: Commit**

```bash
git add Material/Instances/CoreBenchmark/small/split_milp_m1e.xconf Material/Instances/CoreBenchmark/small/split_milp_m2e.xconf
git commit -m "feat: SplitM1GExact xconf files + smoke/regression verification (Task 6)"
```

---

## Post-plan notes (not tasks — read before starting the next spec)

- **Solve-time measurement is the real deliverable of Task 6's smoke runs**, not just "did it
  not crash" — read `splitm1gx_decision_log.csv`'s `solveSec` column and compare against
  `m1g_decision_log.csv`/`splitm1g_decision_log.csv` from the same instance size. Spec 3 §3.5/§7
  flags this as the biggest open risk; do not schedule a 45-bot large-scale run before this
  number is in hand.
- Once solve-time is measured and acceptable at `small`/`4o10b` scale, the natural next step is
  the M0 → SplitM1G → SplitM1GExact three-stage ablation experiment described in spec §6.4 — that
  is a new experiment-running task, not part of this implementation plan.
