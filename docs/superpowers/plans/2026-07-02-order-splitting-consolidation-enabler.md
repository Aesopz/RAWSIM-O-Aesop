# Order Splitting + Consolidation Enabler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立拆單（order splitting）的 enabler：child-Order 資料模型＋需求帳本＋consolidation 記帳＋母單層 KPI＋貪婪 heuristic 拆單 manager（新模組），原版 M1G/HADGS 完全不動。

**Architecture:** 拆單產生真正的 child `Order` 物件走既有揀取管線；母單留在 backlog 承載殘量（需求帳本防重複揀貨）；child 在 picking 完成點通知母單，全部 children 完成才觸發母單層 KPI 事件。決策層＝新建 `SplitOrderManager`（新 OrderBatching 模組、新 config token），消融對比＝xconf 選原版 vs 新模組。

**Tech Stack:** C# / .NET Framework 4.8（舊式 csproj）、MSBuild x64、RAWSimO.CLI 煙霧測試、自建輕量測試 console（RAWSimO.Tests，無 NuGet）。

**Spec:** `docs/superpowers/specs/2026-07-02-order-splitting-consolidation-enabler-design.md`

## Global Constraints

- **絕不修改** `RAWSimO.Core\Control\Defaults\OrderBatching\M1GManager.cs` 與 `HADGSManager.cs`（原版＝消融 baseline）。
- 建置一律 **x64**（Gurobi 只有 win64，x86 會 runtime 失敗）：
  `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" <csproj> /p:Configuration=Debug /p:Platform=x64 /v:m`
- CLI 執行格式：`RAWSimO.CLI.exe <xlayo> <xsett> <xconf> <outdir> <seed>`；標準 small case = `Material\Instances\CoreBenchmark\small\small.xlayo` + `small_o100_mu100.xsett`（7200 模擬秒，牆鐘可能數分鐘，PowerShell timeout 設 600000 或 run_in_background）。
- 語言層級 C# 7.3（net48 舊式 csproj 預設）：不可用 target-typed `new`、switch expression、record。
- 核心層改動（Order / ResourceManager / OutputStation）必須在「無 split child」時走原路徑（零行為改變）。
- WHCA* 有牆鐘預算 → 模擬**非位元級可重現**；回歸比對用統計等價（throughput 差 <2%），不要求 bit-identical。
- Commit message 結尾加 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`。

---

### Task 1: 基準快照（回歸比對基準，改碼前先跑）

**Files:**
- 無程式修改；產生 `output_split_baseline\`（不 commit，加到比對備忘即可）

**Interfaces:**
- Produces: `output_split_baseline\` 內的 HADGS baseline 統計輸出，Task 10 回歸比對用

- [ ] **Step 1: 建置目前 HEAD 的 CLI（x64 Debug）**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.CLI\RAWSimO.CLI.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
```

Expected: `Build succeeded. 0 Error(s)`；產出 `RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe`

- [ ] **Step 2: 跑 baseline（OBHADGS、seed 0）**

```powershell
$B = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
& "$B\RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe" "$B\Material\Instances\CoreBenchmark\small\small.xlayo" "$B\Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett" "$B\Material\Instances\CoreBenchmark\small\PPWHCAnStar-TABalanced-SAActivateAll-ISEmptiest-PSNearest-RPDummy-OBHADGS-RBSamePod-MMNoChange.xconf" "$B\output_split_baseline" 0
```

Expected: 模擬跑完（exit code 0），`output_split_baseline\` 產生統計輸出資料夾。

- [ ] **Step 3: 記下 baseline 關鍵數字**

在 `output_split_baseline\` 內找 footprint / 統計檔（`Get-ChildItem -Recurse`），記下「handled/completed orders 總數」（欄位名含 `OrdersHandled` 或 CLI console 尾端列印的 orders 數），寫進一個備忘檔：

```powershell
"baseline HADGS small seed0: ordersHandled=<記下的數字>" | Out-File -Encoding utf8 C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\output_split_baseline\BASELINE_NOTE.txt
```

Expected: BASELINE_NOTE.txt 存在且有數字。此 task 不 commit。

---

### Task 2: RAWSimO.Tests 輕量測試工程

**Files:**
- Create: `RAWSimO.Tests\RAWSimO.Tests.csproj`
- Create: `RAWSimO.Tests\Program.cs`
- Modify: `RAWSimO.Core\Properties\AssemblyInfo.cs`（加 InternalsVisibleTo）

**Interfaces:**
- Produces: `TestRunner.Add(string, Action)` / `TestRunner.AssertTrue(bool, string)` / `TestRunner.AssertEqual(int, int, string)` / `TestRunner.AssertThrows<T>(Action, string)` / `TestRunner.RunAll() -> int`（0=全過）；後續 task 的測試檔在 `Program.Main` 註冊。
- Consumes: RAWSimO.Core internals（`internal Order()`、`internal SimpleItemDescription(Instance)`、`internal Instance()`）

- [ ] **Step 1: 在 RAWSimO.Core 開放 internals 給測試工程**

在 `RAWSimO.Core\Properties\AssemblyInfo.cs` 檔尾加：

```csharp
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("RAWSimO.Tests")]
```

- [ ] **Step 2: 建立測試工程 csproj**

建立 `RAWSimO.Tests\RAWSimO.Tests.csproj`（舊式格式，僅 x64 組態；GUID 用新產生的，可用 `[guid]::NewGuid()`）：

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="12.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">x64</Platform>
    <ProjectGuid>{PUT-A-NEW-GUID-HERE}</ProjectGuid>
    <OutputType>Exe</OutputType>
    <RootNamespace>RAWSimO.Tests</RootNamespace>
    <AssemblyName>RAWSimO.Tests</AssemblyName>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    <FileAlignment>512</FileAlignment>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|x64'">
    <DebugSymbols>true</DebugSymbols>
    <OutputPath>bin\x64\Debug\</OutputPath>
    <DefineConstants>DEBUG;TRACE</DefineConstants>
    <DebugType>full</DebugType>
    <PlatformTarget>x64</PlatformTarget>
    <ErrorReport>prompt</ErrorReport>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Release|x64'">
    <OutputPath>bin\x64\Release\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
    <Optimize>true</Optimize>
    <DebugType>pdbonly</DebugType>
    <PlatformTarget>x64</PlatformTarget>
    <ErrorReport>prompt</ErrorReport>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System" />
    <Reference Include="System.Core" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\RAWSimO.Core\RAWSimO.Core.csproj">
      <Project>{29343612-CCD6-4E65-9F95-A6A34E7059EB}</Project>
      <Name>RAWSimO.Core</Name>
    </ProjectReference>
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

- [ ] **Step 3: 建立測試 runner**

建立 `RAWSimO.Tests\Program.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace RAWSimO.Tests
{
    /// <summary>
    /// Minimal hand-rolled test runner (no NuGet in this legacy solution).
    /// Register tests via Add(), run all via RunAll(); process exit code 0 = all passed.
    /// </summary>
    public static class TestRunner
    {
        private static readonly List<Tuple<string, Action>> _tests = new List<Tuple<string, Action>>();
        public static void Add(string name, Action test) { _tests.Add(Tuple.Create(name, test)); }
        public static void AssertTrue(bool condition, string message)
        { if (!condition) throw new Exception("Assertion failed: " + message); }
        public static void AssertEqual(int expected, int actual, string message)
        { if (expected != actual) throw new Exception("Assertion failed: " + message + " (expected " + expected + ", got " + actual + ")"); }
        public static void AssertThrows<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Assertion failed (expected " + typeof(T).Name + "): " + message);
        }
        public static int RunAll()
        {
            int failed = 0;
            foreach (var test in _tests)
            {
                try { test.Item2(); Console.WriteLine("[PASS] " + test.Item1); }
                catch (Exception ex) { failed++; Console.WriteLine("[FAIL] " + test.Item1 + " -- " + ex.Message); }
            }
            Console.WriteLine((_tests.Count - failed) + "/" + _tests.Count + " passed");
            return failed == 0 ? 0 : 1;
        }
    }

    public class Program
    {
        public static int Main(string[] args)
        {
            // Test classes register here (added by later tasks)
            return TestRunner.RunAll();
        }
    }
}
```

- [ ] **Step 4: 建置並執行**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\RAWSimO.Tests.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
& "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\bin\x64\Debug\RAWSimO.Tests.exe"
```

Expected: build 成功；輸出 `0/0 passed`、exit code 0。

- [ ] **Step 5: Commit**

```powershell
git add RAWSimO.Tests RAWSimO.Core/Properties/AssemblyInfo.cs
git commit -m "test: add RAWSimO.Tests lightweight test harness (net48 console, no NuGet)"
```

---

### Task 3: Order 拆單資料模型（child factory＋需求帳本＋consolidation 判定）

**Files:**
- Modify: `RAWSimO.Core\Items\Order.cs`（constructor 約 line 22；新增 region）
- Create: `RAWSimO.Tests\OrderSplitTests.cs`
- Modify: `RAWSimO.Tests\Program.cs`、`RAWSimO.Tests\RAWSimO.Tests.csproj`

**Interfaces:**
- Produces（Order 上的新成員，後續 task 依賴，簽名必須一字不差）:
  - `public Order Parent { get; private set; }`（非 child 為 null）
  - `public IReadOnlyList<Order> Children`
  - `public bool IsSplitParent`（有 children 即 true）
  - `public int GetRemainingDemand(ItemDescription item)`（總需求 − 已認領）
  - `public IEnumerable<KeyValuePair<ItemDescription, int>> RemainingPositions`（只含殘量>0 的 line）
  - `public bool IsFullyClaimed`
  - `public static Order CreateSplitChild(Order parent, IEnumerable<KeyValuePair<ItemDescription, int>> quantities)`（原子扣帳＋建 child＋繼承時間戳）
  - `public bool NotifyChildCompleted(Order child)`（回傳 true = 母單就此 consolidation 完成）
  - `public double TimeStampCompleted { get; set; }`（picking 完成時刻，ctor 初始化為 +∞）
- Consumes: 既有 `AddPosition` / `GetDemandCount` / `_quantities`

- [ ] **Step 1: 寫失敗測試**

建立 `RAWSimO.Tests\OrderSplitTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class OrderSplitTests
    {
        private static Instance _instance = new Instance();
        private static ItemDescription Sku() { return new SimpleItemDescription(_instance); }
        private static KeyValuePair<ItemDescription, int> Q(ItemDescription i, int q)
        { return new KeyValuePair<ItemDescription, int>(i, q); }

        public static void Register()
        {
            TestRunner.Add("CreateSplitChild_ClaimsLedger", () =>
            {
                var a = Sku(); var b = Sku();
                var parent = new Order();
                parent.AddPosition(a, 3); parent.AddPosition(b, 1);
                var child = Order.CreateSplitChild(parent, new[] { Q(a, 2) });
                TestRunner.AssertEqual(1, parent.GetRemainingDemand(a), "remaining A after claim 2/3");
                TestRunner.AssertEqual(1, parent.GetRemainingDemand(b), "remaining B untouched");
                TestRunner.AssertEqual(2, child.GetDemandCount(a), "child holds A x2");
                TestRunner.AssertTrue(child.Parent == parent, "child back-reference");
                TestRunner.AssertTrue(parent.IsSplitParent, "parent flagged");
                TestRunner.AssertTrue(!parent.IsFullyClaimed, "not fully claimed yet");
            });
            TestRunner.Add("CreateSplitChild_Overclaim_Throws", () =>
            {
                var a = Sku();
                var parent = new Order();
                parent.AddPosition(a, 3);
                TestRunner.AssertThrows<InvalidOperationException>(
                    () => Order.CreateSplitChild(parent, new[] { Q(a, 4) }), "overclaim must throw");
            });
            TestRunner.Add("RemainingPositions_ExcludesClaimed", () =>
            {
                var a = Sku(); var b = Sku();
                var parent = new Order();
                parent.AddPosition(a, 2); parent.AddPosition(b, 1);
                Order.CreateSplitChild(parent, new[] { Q(a, 2) });
                var rem = parent.RemainingPositions.ToList();
                TestRunner.AssertEqual(1, rem.Count, "only B remains");
                TestRunner.AssertTrue(rem[0].Key == b && rem[0].Value == 1, "B x1 remains");
            });
            TestRunner.Add("Consolidation_OnlyWhenAllChildrenDone", () =>
            {
                var a = Sku(); var b = Sku();
                var parent = new Order();
                parent.AddPosition(a, 3); parent.AddPosition(b, 1);
                var c1 = Order.CreateSplitChild(parent, new[] { Q(a, 2) });
                var c2 = Order.CreateSplitChild(parent, new[] { Q(a, 1), Q(b, 1) });
                TestRunner.AssertTrue(parent.IsFullyClaimed, "fully claimed");
                TestRunner.AssertTrue(!parent.NotifyChildCompleted(c1), "first child alone must not complete parent");
                TestRunner.AssertTrue(parent.NotifyChildCompleted(c2), "last child completes parent");
            });
            TestRunner.Add("Consolidation_NotBeforeFullyClaimed", () =>
            {
                var a = Sku();
                var parent = new Order();
                parent.AddPosition(a, 3);
                var c1 = Order.CreateSplitChild(parent, new[] { Q(a, 1) });
                TestRunner.AssertTrue(!parent.NotifyChildCompleted(c1), "residual demand keeps parent open");
            });
            TestRunner.Add("Child_InheritsTiming", () =>
            {
                var a = Sku();
                var parent = new Order();
                parent.AddPosition(a, 2);
                parent.TimeStamp = 123.0; parent.DueTime = 456.0;
                parent.TimePlaced = new DateTime(2026, 7, 2);
                var child = Order.CreateSplitChild(parent, new[] { Q(a, 1) });
                TestRunner.AssertTrue(child.TimeStamp == 123.0 && child.DueTime == 456.0
                    && child.TimePlaced == new DateTime(2026, 7, 2), "child inherits timing meta");
            });
        }
    }
}
```

修改 `RAWSimO.Tests\Program.cs` 的 `Main`：

```csharp
        public static int Main(string[] args)
        {
            OrderSplitTests.Register();
            return TestRunner.RunAll();
        }
```

在 `RAWSimO.Tests.csproj` 的 `<Compile Include="Program.cs" />` 後加：

```xml
    <Compile Include="OrderSplitTests.cs" />
```

- [ ] **Step 2: 跑測試確認失敗（編譯錯誤 = 失敗）**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\RAWSimO.Tests.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
```

Expected: FAIL — `'Order' does not contain a definition for 'CreateSplitChild'` 等編譯錯誤。

- [ ] **Step 3: 實作 Order 的拆單成員**

在 `RAWSimO.Core\Items\Order.cs`：

(a) constructor（line 22）改為：

```csharp
        internal Order() { TimeStampSubmit = double.PositiveInfinity; DueTime = double.PositiveInfinity; TimeStampCompleted = double.PositiveInfinity; }
```

(b) 在 `#region Core` 內（`IsCompleted()` 之後、`#endregion` 之前）加入：

```csharp
        /// <summary>
        /// The time picking of this order finished at a station (+inf while unfinished).
        /// </summary>
        public double TimeStampCompleted { get; set; }

        #region Split orders (order-splitting enabler; see docs/superpowers/specs/2026-07-02-order-splitting-consolidation-enabler-design.md)

        /// <summary>
        /// The parent order, if this order is a split child. <code>null</code> for normal orders and split parents.
        /// </summary>
        public Order Parent { get; private set; }
        /// <summary>
        /// The child orders this order was split into.
        /// </summary>
        private List<Order> _children = new List<Order>();
        /// <summary>
        /// The child orders this order was split into (empty for normal orders).
        /// </summary>
        public IReadOnlyList<Order> Children { get { return _children; } }
        /// <summary>
        /// Number of children that already completed picking.
        /// </summary>
        private int _completedChildren = 0;
        /// <summary>
        /// Demand ledger: quantities per SKU already claimed by children.
        /// </summary>
        private Dictionary<ItemDescription, int> _claimedQuantities = new Dictionary<ItemDescription, int>();
        /// <summary>
        /// Indicates whether this order was split into children.
        /// </summary>
        public bool IsSplitParent { get { return _children.Count > 0; } }
        /// <summary>
        /// The remaining (not yet claimed by any child) demand of the given SKU.
        /// </summary>
        public int GetRemainingDemand(ItemDescription item)
        { return GetDemandCount(item) - (_claimedQuantities.ContainsKey(item) ? _claimedQuantities[item] : 0); }
        /// <summary>
        /// Enumerates all positions with their remaining (unclaimed) quantities; positions without remainder are omitted.
        /// </summary>
        public IEnumerable<KeyValuePair<ItemDescription, int>> RemainingPositions
        {
            get
            {
                return _quantities
                    .Select(q => new KeyValuePair<ItemDescription, int>(q.Key, GetRemainingDemand(q.Key)))
                    .Where(q => q.Value > 0);
            }
        }
        /// <summary>
        /// Indicates whether the complete demand of this order was claimed by children.
        /// </summary>
        public bool IsFullyClaimed { get { return _quantities.All(q => GetRemainingDemand(q.Key) <= 0); } }
        /// <summary>
        /// Creates a split child of the given parent claiming the given quantities from the parent's demand ledger.
        /// Claiming and child creation happen atomically so no unit can be assigned twice.
        /// The child inherits the parent's timing meta-data.
        /// </summary>
        public static Order CreateSplitChild(Order parent, IEnumerable<KeyValuePair<ItemDescription, int>> quantities)
        {
            Order child = new Order();
            int units = 0;
            foreach (var q in quantities)
            {
                if (q.Value <= 0)
                    throw new ArgumentException("Child quantities must be positive!");
                if (q.Value > parent.GetRemainingDemand(q.Key))
                    throw new InvalidOperationException("Cannot claim more units than remaining for the SKU!");
                child.AddPosition(q.Key, q.Value);
                if (!parent._claimedQuantities.ContainsKey(q.Key))
                    parent._claimedQuantities[q.Key] = 0;
                parent._claimedQuantities[q.Key] += q.Value;
                units += q.Value;
            }
            if (units == 0)
                throw new ArgumentException("Cannot create an empty split child!");
            child.Parent = parent;
            child.TimePlaced = parent.TimePlaced;
            child.TimeStamp = parent.TimeStamp;
            child.DueTime = parent.DueTime;
            parent._children.Add(child);
            return child;
        }
        /// <summary>
        /// Notifies this (parent) order that one of its children completed picking.
        /// </summary>
        /// <param name="child">The completed child.</param>
        /// <returns><code>true</code> if the parent is complete now (all demand claimed and all children done), <code>false</code> otherwise.</returns>
        public bool NotifyChildCompleted(Order child)
        {
            if (child.Parent != this)
                throw new InvalidOperationException("Order is not a child of this order!");
            _completedChildren++;
            return IsFullyClaimed && _completedChildren >= _children.Count;
        }

        #endregion
```

- [ ] **Step 4: 跑測試確認通過**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\RAWSimO.Tests.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
& "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\bin\x64\Debug\RAWSimO.Tests.exe"
```

Expected: `6/6 passed`、exit code 0。

- [ ] **Step 5: Commit**

```powershell
git add RAWSimO.Core/Items/Order.cs RAWSimO.Tests
git commit -m "feat: Order split-child data model with demand ledger and consolidation check"
```

---

### Task 4: ResourceManager.TransferExtractRequests（把母單的 requests 原子移轉給 child）

**Files:**
- Modify: `RAWSimO.Core\Management\ResourceManager.cs`（`CreateExtractRequests` 約 line 437-451 之後）

**Interfaces:**
- Produces: `public void TransferExtractRequests(Order parent, Order child)` — 依 child.Positions 把母單未指派的 ExtractRequest 換成 child 持有的新 request；`_backlogDemand` 淨值不變。
- Consumes: `_availableExtractRequests`、`_availableExtractRequestsPerOrder`（line 447）、`ExtractRequest(item, order, null)`（line 756）、`Order.RemoveRequest` / `Order.AddRequest`

- [ ] **Step 1: 實作**

在 `CreateExtractRequests` 方法之後加入：

```csharp
        /// <summary>
        /// Moves the extract requests matching the child's positions from the (split) parent order to the child.
        /// Only requests not yet assigned to a station are moved (a split parent is never allocated itself).
        /// Net backlog demand stays unchanged, hence no demand-tracking updates here.
        /// See docs/superpowers/specs/2026-07-02-order-splitting-consolidation-enabler-design.md.
        /// </summary>
        /// <param name="parent">The split parent order holding the original requests.</param>
        /// <param name="child">The freshly created split child (its positions define what to move).</param>
        public void TransferExtractRequests(Order parent, Order child)
        {
            foreach (var position in child.Positions)
            {
                List<ExtractRequest> parentRequests = _availableExtractRequestsPerOrder[parent]
                    .Where(r => r.Item == position.Key && _availableExtractRequests.Contains(r))
                    .Take(position.Value)
                    .ToList();
                if (parentRequests.Count < position.Value)
                    throw new InvalidOperationException("Parent does not have enough open extract requests for the SKU!");
                foreach (var request in parentRequests)
                {
                    _availableExtractRequests.Remove(request);
                    _availableExtractRequestsPerOrder[parent].Remove(request);
                    parent.RemoveRequest(position.Key, request);
                }
                for (int i = 0; i < position.Value; i++)
                {
                    ExtractRequest childRequest = new ExtractRequest(position.Key, child, null);
                    child.AddRequest(position.Key, childRequest);
                    _availableExtractRequests.Add(childRequest);
                }
            }
            if (!_availableExtractRequestsPerOrder.ContainsKey(child))
                _availableExtractRequestsPerOrder[child] = new HashSet<ExtractRequest>(child.Requests);
        }
```

注意：若欄位實際名稱與上述不符（以檔內 `CreateExtractRequests` 用到的名稱為準），以現檔為準調整；集合型別以現檔宣告為準。

- [ ] **Step 2: 建置驗證**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Core\RAWSimO.Core.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
```

Expected: `0 Error(s)`（整合行為由 Task 8/10 煙霧測試驗證；此方法無法脫離 Instance 單元測試）。

- [ ] **Step 3: Commit**

```powershell
git add RAWSimO.Core/Management/ResourceManager.cs
git commit -m "feat: ResourceManager.TransferExtractRequests moves parent requests to split child"
```

---

### Task 5: OutputStation 完成路徑 gating（child 完成→母單記帳；母單完成才進 KPI）

**Files:**
- Modify: `RAWSimO.Core\Elements\OutputStation.cs`（`RemoveAnyCompletedOrder`，line 331-352 區塊）

**Interfaces:**
- Consumes: `Order.Parent` / `Order.NotifyChildCompleted` / `Order.TimeStampCompleted`（Task 3）、`ItemManager.CompleteOrder`、`Instance.NotifyOrderCompleted`
- Produces: 行為約定——child 完成不觸發 `NotifyOrderCompleted`；母單最後一個 child 完成時以該站觸發 `NotifyOrderCompleted(parent, this)`；非 split 訂單路徑逐字不變。

- [ ] **Step 1: 修改完成區塊**

`RemoveAnyCompletedOrder` 中（line 335-347）原：

```csharp
            foreach (var order in _assignedOrders)
                if (order.IsCompleted())
                {
                    finishedOrder = order;
                    StatNumOrdersFinished++;
                    // Notify the item manager about this
                    Instance.ItemManager.CompleteOrder(finishedOrder);
                    // Notify completed order
                    Instance.NotifyOrderCompleted(finishedOrder, this);
                    // Break early and block action
                    BlockedUntil = currentTime + OrderCompletionTime;
                    break;
                }
```

改為：

```csharp
            foreach (var order in _assignedOrders)
                if (order.IsCompleted())
                {
                    finishedOrder = order;
                    StatNumOrdersFinished++;
                    finishedOrder.TimeStampCompleted = currentTime;
                    // Notify the item manager about this
                    Instance.ItemManager.CompleteOrder(finishedOrder);
                    if (finishedOrder.Parent != null)
                    {
                        // Split child: only bookkeeping towards the parent; parent-level KPI fires
                        // once ALL children are done (consolidation), attributed to this station.
                        Order parent = finishedOrder.Parent;
                        if (parent.NotifyChildCompleted(finishedOrder))
                        {
                            parent.TimeStampCompleted = currentTime;
                            Instance.ItemManager.CompleteOrder(parent);
                            Instance.NotifyOrderCompleted(parent, this);
                        }
                    }
                    else
                    {
                        // Notify completed order
                        Instance.NotifyOrderCompleted(finishedOrder, this);
                    }
                    // Break early and block action
                    BlockedUntil = currentTime + OrderCompletionTime;
                    break;
                }
```

- [ ] **Step 2: 建置 + 既有測試迴歸**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\RAWSimO.Tests.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
& "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\bin\x64\Debug\RAWSimO.Tests.exe"
```

Expected: build 成功、`6/6 passed`（Parent==null 走原路徑，無行為變化）。

- [ ] **Step 3: Commit**

```powershell
git add RAWSimO.Core/Elements/OutputStation.cs
git commit -m "feat: gate order-completed KPI at parent level for split children (consolidation)"
```

---

### Task 6: 新模組 wiring（enum＋config＋Controller）＋ SplitOrderManager 骨架 ＋ 煙霧 xconf

**Files:**
- Modify: `RAWSimO.Core\Configurations\MethodConfiguration.cs`（enum `OrderBatchingMethodType` line 260-330；`OrderBatchingConfiguration` 的 XmlInclude 清單 line 742-759）
- Modify: `RAWSimO.Core\Configurations\MethodConfigurationsOB.cs`（檔尾加 config 類）
- Modify: `RAWSimO.Core\Control\Controller.cs`（switch，line 103-119）
- Create: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitOrderManager.cs`
- Modify: `RAWSimO.Core\RAWSimO.Core.csproj`（加 `<Compile Include="Control\Defaults\OrderBatching\SplitOrderManager.cs" />`，放在其他 OrderBatching Compile 條目旁）
- Create: `Material\Instances\CoreBenchmark\small\split_m2.xconf`

**Interfaces:**
- Produces:
  - enum 成員 `OrderBatchingMethodType.SplitHeuristic`
  - `public class SplitHeuristicConfiguration : OrderBatchingConfiguration`，欄位 `bool CrossTime = true`、`int MaxChildrenPerOrder = 2`、`int MaxUnitsPerChild = 0`，`GetMethodName()` 回 `"OBSPLITH"`
  - `public class SplitOrderManager : OrderManager`（本 task 先不拆單：整張最早到期訂單→最空站）
- Consumes: `OrderManager` 基底（`_pendingOrders`、`AllocateOrder`、`DecideAboutPendingOrders` 抽象、`SignalCurrentTime` 抽象、`idoforder`）、`Instance.StockInfo.GetActualStock`

- [ ] **Step 1: enum 加成員**

`MethodConfiguration.cs` 的 `OrderBatchingMethodType`，在 `Foresight,`（line 329）後加：

```csharp
        /// <summary>
        /// Greedy order-splitting heuristic manager (enabler for the order-splitting thesis line).
        /// Originals (M1G / HADGS) stay untouched as the no-splitting ablation baseline.
        /// </summary>
        SplitHeuristic,
```

- [ ] **Step 2: XmlInclude**

`OrderBatchingConfiguration` 的 attribute 清單（line 742-759 區）加一行：

```csharp
    [XmlInclude(typeof(SplitHeuristicConfiguration))]
```

- [ ] **Step 3: config 類**

`MethodConfigurationsOB.cs` 檔尾（namespace 內）加：

```csharp
    /// <summary>
    /// The configuration for the greedy order-splitting heuristic manager.
    /// See docs/superpowers/specs/2026-07-02-order-splitting-consolidation-enabler-design.md.
    /// </summary>
    public class SplitHeuristicConfiguration : OrderBatchingConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.SplitHeuristic; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBSPLITH"; }
        /// <summary>
        /// M2 (cross-time) splitting: residual demand may stay in the backlog for later epochs.
        /// If false (M1, cross-station only), an order is only assigned when its complete remaining demand fits this epoch.
        /// </summary>
        public bool CrossTime = true;
        /// <summary>
        /// Maximal number of children an order may be split into per epoch.
        /// </summary>
        public int MaxChildrenPerOrder = 2;
        /// <summary>
        /// Cap of units per child (0 = no cap). Only effective when CrossTime is enabled.
        /// </summary>
        public int MaxUnitsPerChild = 0;
    }
```

（若基底無 `GetMethodName` 可覆寫，參考 `HADGSReturnPendingConfiguration`（line 510-528）的寫法照抄基底簽名。）

- [ ] **Step 4: Controller wiring**

`Controller.cs` switch（line 119 `Queue` case 後）加：

```csharp
                case OrderBatchingMethodType.SplitHeuristic: OrderManager = new SplitOrderManager(instance); break;
```

檔頭 using 已含 `RAWSimO.Core.Control.Defaults.OrderBatching`（M1GManager 同 namespace，若無則補）。

- [ ] **Step 5: SplitOrderManager 骨架（尚不拆單）**

建立 `RAWSimO.Core\Control\Defaults\OrderBatching\SplitOrderManager.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Enabler manager for order splitting: greedily assigns (and, from Task 8 on, splits) pending
    /// orders to stations with free capacity. The original M1G / HADGS managers stay untouched and
    /// serve as the no-splitting ablation baseline.
    /// See docs/superpowers/specs/2026-07-02-order-splitting-consolidation-enabler-design.md.
    /// </summary>
    public class SplitOrderManager : OrderManager
    {
        /// <summary>
        /// Creates a new split-order manager.
        /// </summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public SplitOrderManager(Instance instance) : base(instance)
        { _config = instance.ControllerConfig.OrderBatchingConfig as SplitHeuristicConfiguration; }

        /// <summary>
        /// The configuration.
        /// </summary>
        private SplitHeuristicConfiguration _config;

        /// <summary>
        /// Free order slots of the station right now.
        /// </summary>
        private int FreeCapacity(OutputStation station)
        { return station.Capacity - station.CapacityInUse - station.CapacityReserved; }

        /// <summary>
        /// Decides about the pending orders: earliest-due order first to the station with most free capacity.
        /// (Splitting is added in a later task.)
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
            foreach (var order in _pendingOrders.OrderBy(o => o.DueTime).ThenBy(o => o.ID).ToList())
            {
                // Only stock-feasible orders
                if (!order.RemainingPositions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value))
                    continue;
                OutputStation station = Instance.OutputStations
                    .Where(s => FreeCapacity(s) > 0)
                    .OrderByDescending(s => FreeCapacity(s)).ThenBy(s => s.ID)
                    .FirstOrDefault();
                if (station == null)
                    return;
                AllocateOrder(order, station);
            }
        }

        /// <summary>
        /// Signals the current time to the mechanism.
        /// </summary>
        /// <param name="currentTime">The current simulation time.</param>
        public override void SignalCurrentTime(double currentTime) { /* nothing to do */ }
    }
}
```

並在 `RAWSimO.Core.csproj` 加對應 `<Compile Include="Control\Defaults\OrderBatching\SplitOrderManager.cs" />`。

- [ ] **Step 6: 煙霧 xconf**

複製 `Material\Instances\CoreBenchmark\small\PPWHCAnStar-TABalanced-SAActivateAll-ISEmptiest-PSNearest-RPDummy-OBHADGS-RBSamePod-MMNoChange.xconf` 為 `Material\Instances\CoreBenchmark\small\split_m2.xconf`，把 `<Name>` 改為 `split_m2`，並把整段 `<OrderBatchingConfig ...>...</OrderBatchingConfig>`（line 105-115）換成：

```xml
  <OrderBatchingConfig xsi:type="SplitHeuristicConfiguration">
    <Name />
    <CrossTime>true</CrossTime>
    <MaxChildrenPerOrder>2</MaxChildrenPerOrder>
    <MaxUnitsPerChild>0</MaxUnitsPerChild>
  </OrderBatchingConfig>
```

- [ ] **Step 7: 建置 + 骨架煙霧**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.CLI\RAWSimO.CLI.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
$B = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
& "$B\RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe" "$B\Material\Instances\CoreBenchmark\small\small.xlayo" "$B\Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett" "$B\Material\Instances\CoreBenchmark\small\split_m2.xconf" "$B\output_split_smoke" 0
```

Expected: build 成功；模擬跑完 exit 0（xconf 反序列化成功＝XmlInclude 正確；骨架 FCFS 有訂單完成）。

- [ ] **Step 8: Commit**

```powershell
git add RAWSimO.Core/Configurations/MethodConfiguration.cs RAWSimO.Core/Configurations/MethodConfigurationsOB.cs RAWSimO.Core/Control/Controller.cs RAWSimO.Core/Control/Defaults/OrderBatching/SplitOrderManager.cs RAWSimO.Core/RAWSimO.Core.csproj Material/Instances/CoreBenchmark/small/split_m2.xconf
git commit -m "feat: SplitHeuristic OB module wiring + SplitOrderManager skeleton (no splitting yet)"
```

---

### Task 7: SplitPlanner（純函式拆單規劃）＋ 單元測試

**Files:**
- Create: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitPlanner.cs`
- Modify: `RAWSimO.Core\RAWSimO.Core.csproj`（加 Compile 條目）
- Create: `RAWSimO.Tests\SplitPlannerTests.cs`
- Modify: `RAWSimO.Tests\Program.cs`、`RAWSimO.Tests\RAWSimO.Tests.csproj`

**Interfaces:**
- Produces:
  ```csharp
  public static List<Dictionary<ItemDescription, int>> SplitPlanner.ComputePlan(
      IList<KeyValuePair<ItemDescription, int>> remaining, // 殘量（呼叫端已用庫存 cap 過）
      int freeStationSlots, bool crossTime, int maxChildrenPerOrder, int maxUnitsPerChild)
  ```
  回傳每個 child 的 SKU→數量表（index = station 排名）；空 list = 本期不指派。M1（crossTime=false）保證全部單位被分配；M2 可留殘（cap 生效時）。
- Consumes: 無模擬狀態（純函式）

- [ ] **Step 1: 寫失敗測試**

建立 `RAWSimO.Tests\SplitPlannerTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core;
using RAWSimO.Core.Control.Defaults.OrderBatching;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class SplitPlannerTests
    {
        private static Instance _instance = new Instance();
        private static ItemDescription Sku() { return new SimpleItemDescription(_instance); }
        private static KeyValuePair<ItemDescription, int> Q(ItemDescription i, int q)
        { return new KeyValuePair<ItemDescription, int>(i, q); }
        private static int Units(List<Dictionary<ItemDescription, int>> plan)
        { return plan.Sum(c => c.Values.Sum()); }
        private static int UnitsOf(List<Dictionary<ItemDescription, int>> plan, ItemDescription sku)
        { return plan.Sum(c => c.ContainsKey(sku) ? c[sku] : 0); }

        public static void Register()
        {
            TestRunner.Add("Planner_SingleSlot_NoSplit", () =>
            {
                var a = Sku(); var b = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 3), Q(b, 1) }.ToList(), 1, true, 2, 0);
                TestRunner.AssertEqual(1, plan.Count, "one child only");
                TestRunner.AssertEqual(4, Units(plan), "all units in the single child");
            });
            TestRunner.Add("Planner_TwoSlots_EvenSplit_ConservesQuantities", () =>
            {
                var a = Sku(); var b = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 3), Q(b, 1) }.ToList(), 2, true, 2, 0);
                TestRunner.AssertEqual(2, plan.Count, "two children");
                TestRunner.AssertEqual(4, Units(plan), "quantity conservation");
                TestRunner.AssertEqual(3, UnitsOf(plan, a), "A total conserved");
                TestRunner.AssertEqual(1, UnitsOf(plan, b), "B total conserved");
                TestRunner.AssertTrue(plan.All(c => c.Values.Sum() == 2), "even 2/2 split");
            });
            TestRunner.Add("Planner_SkuQuantitySplitAcrossChildren", () =>
            {
                var a = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 4) }.ToList(), 2, true, 2, 0);
                TestRunner.AssertEqual(2, plan.Count, "two children");
                TestRunner.AssertEqual(2, plan[0][a], "A split 2/2 - first child");
                TestRunner.AssertEqual(2, plan[1][a], "A split 2/2 - second child");
            });
            TestRunner.Add("Planner_M1_AssignsEverything", () =>
            {
                var a = Sku(); var b = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 5), Q(b, 2) }.ToList(), 3, false, 3, 0);
                TestRunner.AssertEqual(7, Units(plan), "M1 assigns all units");
            });
            TestRunner.Add("Planner_M2_CapLeavesResidual", () =>
            {
                var a = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 5) }.ToList(), 2, true, 2, 2);
                TestRunner.AssertEqual(4, Units(plan), "2 children x cap 2, residual 1 unassigned");
            });
            TestRunner.Add("Planner_NoSlots_Empty", () =>
            {
                var a = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 3) }.ToList(), 0, true, 2, 0);
                TestRunner.AssertEqual(0, plan.Count, "no slots, no plan");
            });
            TestRunner.Add("Planner_MoreSlotsThanUnits_NoEmptyChildren", () =>
            {
                var a = Sku();
                var plan = SplitPlanner.ComputePlan(new[] { Q(a, 1) }.ToList(), 3, true, 3, 0);
                TestRunner.AssertEqual(1, plan.Count, "1 unit -> 1 child");
            });
        }
    }
}
```

`Program.cs` 的 `Main` 改為：

```csharp
        public static int Main(string[] args)
        {
            OrderSplitTests.Register();
            SplitPlannerTests.Register();
            return TestRunner.RunAll();
        }
```

csproj 加 `<Compile Include="SplitPlannerTests.cs" />`。

- [ ] **Step 2: 跑測試確認失敗**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\RAWSimO.Tests.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
```

Expected: FAIL — `The type or namespace name 'SplitPlanner' could not be found`。

- [ ] **Step 3: 實作 SplitPlanner**

建立 `RAWSimO.Core\Control\Defaults\OrderBatching\SplitPlanner.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure planning logic of the greedy split heuristic: distributes the remaining units of ONE order
    /// over a number of free station slots. No simulation state involved, hence unit-testable.
    /// See docs/superpowers/specs/2026-07-02-order-splitting-consolidation-enabler-design.md.
    /// </summary>
    public static class SplitPlanner
    {
        /// <summary>
        /// Computes the per-child quantities for one order.
        /// </summary>
        /// <param name="remaining">Remaining (stock-capped) demand per SKU.</param>
        /// <param name="freeStationSlots">Number of stations with at least one free order slot.</param>
        /// <param name="crossTime">M2: residual units may stay unassigned for later epochs. M1 (false): all units are assigned.</param>
        /// <param name="maxChildrenPerOrder">Maximal parts per epoch (>= 1).</param>
        /// <param name="maxUnitsPerChild">Unit cap per child (0 = unlimited); only effective when crossTime is enabled.</param>
        /// <returns>One SKU-&gt;quantity map per child (index = station rank); empty list = do not assign this epoch.</returns>
        public static List<Dictionary<ItemDescription, int>> ComputePlan(
            IList<KeyValuePair<ItemDescription, int>> remaining,
            int freeStationSlots, bool crossTime, int maxChildrenPerOrder, int maxUnitsPerChild)
        {
            List<Dictionary<ItemDescription, int>> children = new List<Dictionary<ItemDescription, int>>();
            int totalUnits = remaining.Sum(r => r.Value);
            if (totalUnits <= 0 || freeStationSlots <= 0 || maxChildrenPerOrder <= 0)
                return children;
            int parts = Math.Min(Math.Min(freeStationSlots, maxChildrenPerOrder), totalUnits);
            // Units to assign this epoch (M2 with cap may leave a residual)
            int unitsToAssign = totalUnits;
            if (crossTime && maxUnitsPerChild > 0)
                unitsToAssign = Math.Min(totalUnits, parts * maxUnitsPerChild);
            // Even budgets, larger parts first
            int[] budgets = new int[parts];
            int baseSize = unitsToAssign / parts, rest = unitsToAssign % parts;
            for (int i = 0; i < parts; i++)
                budgets[i] = baseSize + (i < rest ? 1 : 0);
            for (int i = 0; i < parts; i++)
                children.Add(new Dictionary<ItemDescription, int>());
            // Fill children sequentially per SKU (keeps SKU units together as far as budgets allow)
            int childIdx = 0;
            foreach (var position in remaining)
            {
                int left = position.Value;
                while (left > 0 && childIdx < parts)
                {
                    int used = children[childIdx].Values.Sum();
                    int space = budgets[childIdx] - used;
                    if (space <= 0) { childIdx++; continue; }
                    int put = Math.Min(space, left);
                    if (!children[childIdx].ContainsKey(position.Key))
                        children[childIdx][position.Key] = 0;
                    children[childIdx][position.Key] += put;
                    left -= put;
                }
                if (childIdx >= parts)
                    break;
            }
            // Defensive: drop empty children
            return children.Where(c => c.Count > 0).ToList();
        }
    }
}
```

csproj（RAWSimO.Core）加 `<Compile Include="Control\Defaults\OrderBatching\SplitPlanner.cs" />`。

- [ ] **Step 4: 跑測試確認通過**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\RAWSimO.Tests.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
& "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\bin\x64\Debug\RAWSimO.Tests.exe"
```

Expected: `13/13 passed`。

- [ ] **Step 5: Commit**

```powershell
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitPlanner.cs RAWSimO.Core/RAWSimO.Core.csproj RAWSimO.Tests
git commit -m "feat: SplitPlanner pure split-quantity planning with M1/M2 semantics + tests"
```

---

### Task 8: SplitOrderManager 拆單整合（child 建立→request 移轉→分派→母單生命週期）

**Files:**
- Modify: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitOrderManager.cs`（取代 `DecideAboutPendingOrders`）

**Interfaces:**
- Consumes: `SplitPlanner.ComputePlan`（Task 7）、`Order.CreateSplitChild` / `RemainingPositions` / `IsFullyClaimed` / `IsSplitParent` / `GetOpenDemandCount`（Task 3）、`ResourceManager.TransferExtractRequests`（Task 4）、基底 `AllocateOrder` / `_pendingOrders` / `idoforder`、`(Instance.ItemManager as ItemManager).TakeAvailableOrder(Order)`（ItemManager.cs line 1312）
- Produces: 行為約定——未拆快路徑（1 站可吞整單且母單無 children）直接 allocate 母單本體；否則建 children；母單 fully-claimed 時離開 `_pendingOrders` 並呼叫 `TakeAvailableOrder`；M2 殘量留在 `_pendingOrders` 下期再處理。

- [ ] **Step 1: 取代 DecideAboutPendingOrders**

`SplitOrderManager.cs` 中骨架版 `DecideAboutPendingOrders` 整個換成：

```csharp
        /// <summary>
        /// Decides about the pending orders: earliest-due order first; splits it over the stations
        /// with free capacity according to SplitPlanner. A split parent stays in the backlog with its
        /// residual demand (cross-time) and keeps its original timestamps, so the existing urgency
        /// mechanisms apply to the residual automatically.
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
            foreach (var order in _pendingOrders.OrderBy(o => o.DueTime).ThenBy(o => o.ID).ToList())
            {
                // Stations with at least one free order slot, most free capacity first (deterministic tie-break)
                List<OutputStation> stations = Instance.OutputStations
                    .Where(s => FreeCapacity(s) > 0)
                    .OrderByDescending(s => FreeCapacity(s)).ThenBy(s => s.ID)
                    .ToList();
                if (stations.Count == 0)
                    return;
                // Remaining demand capped by the actually available stock
                List<KeyValuePair<ItemDescription, int>> remaining = order.RemainingPositions
                    .Select(p => new KeyValuePair<ItemDescription, int>(p.Key, Math.Min(p.Value, Instance.StockInfo.GetActualStock(p.Key))))
                    .Where(p => p.Value > 0)
                    .ToList();
                if (remaining.Count == 0)
                    continue;
                // M1 (cross-station only): the complete remaining demand must be stock-feasible this epoch
                if (!_config.CrossTime && remaining.Sum(r => r.Value) != order.RemainingPositions.Sum(r => r.Value))
                    continue;
                List<Dictionary<ItemDescription, int>> plan = SplitPlanner.ComputePlan(
                    remaining, stations.Count, _config.CrossTime, _config.MaxChildrenPerOrder, _config.MaxUnitsPerChild);
                if (plan.Count == 0)
                    continue;
                // Unsplit fast path: a single part covering the complete demand of an untouched order
                // -> allocate the order itself (no child overhead, plain semantics).
                if (plan.Count == 1 && !order.IsSplitParent && plan[0].Values.Sum() == order.GetOpenDemandCount())
                {
                    AllocateOrder(order, stations[0]);
                    continue;
                }
                // Create and allocate one child per part (claiming = atomic in CreateSplitChild)
                for (int i = 0; i < plan.Count; i++)
                {
                    Order child = Order.CreateSplitChild(order, plan[i]);
                    child.ID = idoforder++;
                    Instance.ResourceManager.TransferExtractRequests(order, child);
                    AllocateOrder(child, stations[i]);
                }
                // Parent KPI: submit timestamp = first (partial) allocation
                if (double.IsPositiveInfinity(order.TimeStampSubmit))
                    order.TimeStampSubmit = Instance.Controller.CurrentTime;
                // Parent leaves the backlog once its demand is fully claimed (children may still be picking)
                if (order.IsFullyClaimed)
                {
                    _pendingOrders.Remove(order);
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(order);
                }
            }
        }
```

- [ ] **Step 2: 建置 + 單元測試迴歸**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\RAWSimO.Tests.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
& "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.Tests\bin\x64\Debug\RAWSimO.Tests.exe"
```

Expected: build 成功、`13/13 passed`。

- [ ] **Step 3: 拆單煙霧**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.CLI\RAWSimO.CLI.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
$B = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
& "$B\RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe" "$B\Material\Instances\CoreBenchmark\small\small.xlayo" "$B\Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett" "$B\Material\Instances\CoreBenchmark\small\split_m2.xconf" "$B\output_split_smoke_m2" 0
```

Expected: 模擬跑完 exit 0、無 exception；輸出統計顯示有訂單完成（拆單行為的量化驗證在 Task 9 的 CSV 之後）。

- [ ] **Step 4: Commit**

```powershell
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitOrderManager.cs
git commit -m "feat: SplitOrderManager creates split children via planner; parent lifecycle + residual backlog"
```

---

### Task 9: splitorders.csv 記錄器（母單 consolidation 明細）

**Files:**
- Modify: `RAWSimO.Core\Control\Defaults\OrderBatching\SplitOrderManager.cs`

**Interfaces:**
- Consumes: `Instance.OrderCompleted` 事件（`InstanceEvents.cs:102`，簽名 `void (Order, OutputStation)`）、`Order.Children` / `TimeStampCompleted` / `TimeStamp` / `TimeStampSubmit`、`Instance.SettingConfig.StatisticsDirectory`（路徑模式照抄 `M1GManager.cs:105-114`）
- Produces: 統計輸出目錄下 `splitorders.csv`，欄位 `parent,units,children,firstChildDone,lastChildDone,consolidated,consolidationWait,placed,submitted`

- [ ] **Step 1: 實作 logger**

`SplitOrderManager` 加欄位、建構子訂閱與 handler：

建構子改為：

```csharp
        public SplitOrderManager(Instance instance) : base(instance)
        {
            _config = instance.ControllerConfig.OrderBatchingConfig as SplitHeuristicConfiguration;
            instance.OrderCompleted += LogParentCompleted;
        }
```

類別內加：

```csharp
        /// <summary>
        /// Lazily opened CSV logging one row per completed split parent (consolidation detail).
        /// Same location pattern as the M1G decision log.
        /// </summary>
        private System.IO.StreamWriter _splitLog;

        /// <summary>
        /// Writes one CSV row when a split parent order completes (consolidation done).
        /// </summary>
        private void LogParentCompleted(Order order, OutputStation station)
        {
            if (!order.IsSplitParent)
                return;
            if (_splitLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _splitLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "splitorders.csv"), false) { AutoFlush = true };
                _splitLog.WriteLine("parent,units,children,firstChildDone,lastChildDone,consolidated,consolidationWait,placed,submitted");
            }
            double firstDone = order.Children.Min(c => c.TimeStampCompleted);
            double lastDone = order.Children.Max(c => c.TimeStampCompleted);
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
```

- [ ] **Step 2: 建置＋煙霧驗證 CSV**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimO.CLI\RAWSimO.CLI.csproj /p:Configuration=Debug /p:Platform=x64 /v:m
$B = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
& "$B\RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe" "$B\Material\Instances\CoreBenchmark\small\small.xlayo" "$B\Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett" "$B\Material\Instances\CoreBenchmark\small\split_m2.xconf" "$B\output_split_smoke_m2b" 0
Get-ChildItem -Recurse $B\output_split_smoke_m2b -Filter splitorders.csv | Get-Content | Select-Object -First 5
```

Expected: `splitorders.csv` 存在、含 header＋至少 1 筆資料列，`consolidationWait >= 0`、`consolidated >= lastChildDone`。

- [ ] **Step 3: Commit**

```powershell
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitOrderManager.cs
git commit -m "feat: splitorders.csv per-parent consolidation logger"
```

---

### Task 10: 最終驗證（M1/M2 煙霧＋原版回歸）＋ M1 xconf

**Files:**
- Create: `Material\Instances\CoreBenchmark\small\split_m1.xconf`

**Interfaces:**
- Consumes: Task 1 的 `output_split_baseline\BASELINE_NOTE.txt`

- [ ] **Step 1: 建 M1 xconf**

複製 `split_m2.xconf` 為 `split_m1.xconf`，`<Name>` 改 `split_m1`，`<CrossTime>` 改 `false`。

- [ ] **Step 2: M1 煙霧**

```powershell
$B = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
& "$B\RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe" "$B\Material\Instances\CoreBenchmark\small\small.xlayo" "$B\Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett" "$B\Material\Instances\CoreBenchmark\small\split_m1.xconf" "$B\output_split_smoke_m1" 0
```

Expected: exit 0；`splitorders.csv` 有資料列（cross-station 拆單發生）。

- [ ] **Step 3: 卡單檢查（M1 與 M2 都做）**

對 `output_split_smoke_m1` 與 `output_split_smoke_m2b`：比對輸出統計中 placed vs handled 訂單數——差值應與 baseline 同量級（模擬結束時在途訂單），不得出現大量永不完成的母單。若 handled 遠低於 baseline（>20% 落差），視為卡單 bug，停下依 systematic-debugging 排查（重點：`TransferExtractRequests` 後母單/子單 request 歸屬、`NotifyChildCompleted` 是否漏觸發）。

- [ ] **Step 4: 原版 HADGS 回歸**

```powershell
$B = "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP"
& "$B\RAWSimO.CLI\bin\x64\Debug\RAWSimO.CLI.exe" "$B\Material\Instances\CoreBenchmark\small\small.xlayo" "$B\Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett" "$B\Material\Instances\CoreBenchmark\small\PPWHCAnStar-TABalanced-SAActivateAll-ISEmptiest-PSNearest-RPDummy-OBHADGS-RBSamePod-MMNoChange.xconf" "$B\output_split_regression" 0
```

Expected: handled orders 數與 `BASELINE_NOTE.txt` 記錄值統計等價（差 <2%；WHCA* 牆鐘預算導致非位元級重現）。超出 → 核心層改動汙染了原路徑，停下排查（重點：OutputStation gating 的 `Parent == null` 分支、Order ctor 變更）。

- [ ] **Step 5: Commit**

```powershell
git add Material/Instances/CoreBenchmark/small/split_m1.xconf
git commit -m "feat: split_m1 xconf (cross-station only); enabler verification complete"
```

---

## Self-Review 紀錄

- **Spec coverage**：資料模型（Task 3）、帳本防重複揀貨（Task 3+4+8）、consolidation 完成語意＋母單 KPI gating（Task 5）、新模組不動原版（Task 6-8，Global Constraints）、heuristic 拆單器 M1/M2（Task 7-8）、拆單統計（Task 9）、回歸保證（Task 1+10）、Timestay 優先權沿用（母單留 backlog 保原時間戳，Task 8 註解明示）——皆有對應 task。Spec §3.3 的急單機制屬既有程式（M1G/HADGS），本 enabler 的新 manager 用 DueTime 排序達成同義優先權，spec 已允許。
- **Placeholder scan**：無 TBD/TODO；Task 4 的「以現檔為準」是欄位名核對指示，非佔位。
- **Type consistency**：`CreateSplitChild(Order, IEnumerable<KeyValuePair<ItemDescription,int>>)`、`TransferExtractRequests(Order, Order)`、`ComputePlan(IList<...>, int, bool, int, int)`、`NotifyChildCompleted(Order) -> bool`、`TimeStampCompleted` 在 Task 3/4/5/7/8/9 間一致。
