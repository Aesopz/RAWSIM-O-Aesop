# M4G 實作計畫：估值／綁定分層的單位級拆單

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 實作 M4G——把 M1G 的「估值層／綁定層」分離還原到單位級拆單，使拆單成為被估值算出來的最優解，而非被供給上限逼出的可行性殘差。

**Architecture:** 新增 `M4GManager : M1GManager`（不動任何既有 manager）。單次 MILP 同時決定貨架派遣（`x`、`yrp`）、不受槽位限制的估值層取件（`q̂`）、以及受槽位限制的綁定層取件（`q`）。只有綁定層產生副作用（建子單、佔槽、註冊撿貨請求）。所有價格（λ/μ/δ/ε）由跑動統計自我校準，目標式中不含任何手調常數。刪除 `icLGcap` 與整套 `PipelineFloor`。

**Tech Stack:** C# 7.3 / .NET Framework 4.8 legacy csproj、Gurobi（透過 `RAWSimO.SolverWrappers.LinearModel`）、MSBuild x64 Release。

**Spec:** `docs/superpowers/specs/2026-07-31-m4g-valuation-binding-design.md`（本計畫的所有數學符號與約束編號皆對應該文件）

**基準狀態:** tag `m3g-v1-line30`（commit `b3ce7e6`）。M3G 保留為 ablation 對照，全程不得修改。

---

## Global Constraints

**每一個 Task 的要求都隱含包含本節。**

- **建置指令（唯一合法）**：`MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`。x86 因 Gurobi 只有 win64 版會 runtime 失敗。
- **語言版本 C# 7.3**（net48 legacy csproj）：不可使用 records、switch expressions、target-typed `new`、`using` 宣告式。`out` 參數不可被 lambda 捕獲（CS1628）——需要時用 `var copy = outParam;` 包一層區域變數繞過。
- **新檔案必須手動加入 `<Compile Include>` 到 `RAWSimO.Core/RAWSimO.Core.csproj`**，legacy csproj 不會自動抓。
- **絕對不得修改以下檔案**：`RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs`、`HADGSManager.cs`、`SplitM2eICManager.cs`、`GreedyM3GManager.cs`、`PVGSManager.cs`。M4G 一律以繼承／鏡像新檔案實作，即使造成程式碼重複也在所不惜——這是消融實驗乾淨對照組的前提。
- **不得修改正典組態** `Material/Instances/CoreBenchmark/small/split_milp_m3g.xconf`。
- **本專案沒有單元測試框架**。每個 Task 的「測試」＝(1) 建置成功、(2) 短時間 smoke run、(3) 對決策日誌 CSV 做具體欄位斷言。每個 Task 都明確寫出要跑的指令與預期輸出。
- **長跑批次（>10 分鐘）不得用前景 `run_in_background`**，會被 harness 隱性 timeout 打斷。改寫 `.cmd` 腳本 + `Start-Process -WindowStyle Hidden` 完全脫離 harness 行程樹。
- **每個 Task 結束時跑 `git diff --stat` 確認只有預期檔案被改到**，然後 commit。
- **標準測試實例**：`Material/Instances/CoreBenchmark/small/small.xlayo` + `fixed_fill1350_inv70.xsett` + `orders_fill1350.xorders`。這是唯一乾淨的單變數對照（與 Fill 母本僅差 Name / OrderMode / FixedInventoryConfiguration 三行）。
- **CLI 執行格式**（各 Task 的 smoke run 皆用此形式）：
  ```
  RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe <layout.xlayo> <setting.xsett> <config.xconf> <outputdir> <seed>
  ```

---

## File Structure

| 檔案 | 職責 | Task |
|---|---|---|
| `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs` | 新增 `M4GConfiguration : M1GConfiguration` | 1 |
| `RAWSimO.Core/Configurations/MethodConfiguration.cs` | enum `OrderBatchingMethodType.M4G` + `[XmlInclude]` | 1 |
| `RAWSimO.Core/Control/Controller.cs` | switch case 掛上 `M4GManager` | 1 |
| `RAWSimO.Core/RAWSimO.Core.csproj` | 兩個新檔的 `<Compile Include>` | 1 |
| `Material/Instances/CoreBenchmark/small/m4g.xconf` | 基準組態 | 1 |
| `RAWSimO.Core/Control/Defaults/OrderBatching/M4GPricing.cs` | λ/μ/δ/ε 的跑動統計與計算。純狀態機，不碰 MILP | 2 |
| `RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs` | 候選集建構、MILP 建模與求解、decode 與 commit | 3–7 |

---

## Task 1: 組態、註冊與骨架

**Files:**
- Modify: `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`（檔尾，`SplitM2eICConfiguration` 類別之後）
- Modify: `RAWSimO.Core/Configurations/MethodConfiguration.cs:361` 附近的 enum、`:798` 附近的 XmlInclude 區塊
- Modify: `RAWSimO.Core/Control/Controller.cs:126` 之後
- Create: `RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs`
- Modify: `RAWSimO.Core/RAWSimO.Core.csproj`
- Create: `Material/Instances/CoreBenchmark/small/m4g.xconf`

**Interfaces:**
- Produces: `M4GConfiguration`（所有欄位見下方程式碼）、`OrderBatchingMethodType.M4G`、`M4GManager(Instance instance)` 建構子。Task 2–7 全部依賴這三者。

- [ ] **Step 1: 新增組態類別**

在 `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs` 檔案末尾（最後一個 `}` 之前、與其他 configuration 類別同一個 namespace 內）加入：

```csharp
    /// <summary>
    /// M4G: unit-level order splitting with the M1G valuation/binding layer separation
    /// restored. Inherits M1GConfiguration so every engine-side `is M1GConfiguration`
    /// type check passes without touching any engine file.
    /// Spec: docs/superpowers/specs/2026-07-31-m4g-valuation-binding-design.md
    /// </summary>
    public class M4GConfiguration : M1GConfiguration
    {
        /// <summary>Returns the method type of this configuration.</summary>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.M4G; }
        /// <summary>Returns a short name of this configuration.</summary>
        public override string GetMethodName() { return "M4G"; }

        // ── Price calibration (spec 3.5). All prices are metres-denominated and derived
        //    from running statistics; these scales exist only for dose-response ablation. ──
        /// <summary>Dose knob on lambda (metres per closed line). 1.0 = pure self-calibration.</summary>
        public double LambdaScale = 1.0;
        /// <summary>Dose knob on mu (metres per completed order).</summary>
        public double MuScale = 1.0;
        /// <summary>Dose knob on delta (realisation rate of unbound valuation, 0..1).</summary>
        public double DeltaScale = 1.0;
        /// <summary>Epsilon = EpsilonScale * lambda. Tie-break only; must stay far below lambda.</summary>
        public double EpsilonScale = 0.001;
        /// <summary>Below this many cumulative closed lines the fallback prices are used.</summary>
        public int WarmupLines = 50;
        /// <summary>Warm-up lambda in metres per line (measured 10.1-10.5 in the 3-way comparison).</summary>
        public double LambdaFallback = 10.0;
        /// <summary>Warm-up delta.</summary>
        public double DeltaFallback = 0.5;
        /// <summary>Warm-up lines-per-order (measured ~2.37 units per order).</summary>
        public double LinesPerOrderFallback = 2.4;
        /// <summary>&gt; 0 overrides the running lambda with this fixed value (open-loop ablation).</summary>
        public double LambdaFixed = 0;
        /// <summary>&gt; 0 overrides the running delta with this fixed value (open-loop ablation).</summary>
        public double DeltaFixed = 0;

        // ── Ablation (spec 6) ──
        /// <summary>true forces q == q-hat, degenerating the valuation layer. Should reproduce M3G-like behaviour.</summary>
        public bool DegenerateToBindingOnly = false;
        /// <summary>Cap on orders admitted to the valuation layer (0 = no cap). Solve-time convergence knob.</summary>
        public int ValuationOrderLimit = 0;

        // ── Diagnostics (spec 4) ──
        /// <summary>Enables the no-split counterfactual solve that measures the marginal value of splitting.</summary>
        public bool SplitMarginalProbeEnabled = false;
        /// <summary>Probe cadence in decisions.</summary>
        public int SplitMarginalProbeEveryNDecisions = 50;
        /// <summary>Seconds allowed for one probe solve. &lt;= 0 = no limit.</summary>
        public double SplitMarginalProbeTimeLimitSec = 10;
    }
```

- [ ] **Step 2: 註冊 enum 與 XmlInclude**

在 `RAWSimO.Core/Configurations/MethodConfiguration.cs` 找到 `SplitM2eIC,`（約 `:361`），在其後加入一行：

```csharp
        SplitM2eIC,
        M4G,
```

找到 `[XmlInclude(typeof(SplitM2eICConfiguration))]`（約 `:798`），在其後加入：

```csharp
    [XmlInclude(typeof(SplitM2eICConfiguration))]
    [XmlInclude(typeof(M4GConfiguration))]
```

- [ ] **Step 3: 掛上 Controller switch**

在 `RAWSimO.Core/Control/Controller.cs` 找到 `case OrderBatchingMethodType.SplitM2eIC:`（約 `:126`），在其後加入一行：

```csharp
                case OrderBatchingMethodType.SplitM2eIC: OrderManager = new SplitM2eICManager(instance); break;
                case OrderBatchingMethodType.M4G: OrderManager = new M4GManager(instance); break;
```

- [ ] **Step 4: 建立 manager 骨架**

建立 `RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs`。本 Task 只建立骨架，**不覆寫 `DecideAboutPendingOrders`**，因此行為完全等同 M1G——這是後續 Task 的乾淨對照起點。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// M4G - unit-level order splitting with M1G's valuation/binding separation restored.
    ///
    /// M1G keeps two order-assignment layers: yos (slot-free, rewarded in the objective,
    /// a pod valuation) and yaos (slot-limited, the only one actually bound). M3G collapsed
    /// them, so every scored unit had to occupy a currently free slot; splitting then only
    /// arose as a feasibility residual forced by the per-station new-pod cap. M4G restores
    /// the separation at unit level: q-hat values pods against the whole backlog, q binds
    /// only what fits the slots, and only q has side effects.
    ///
    /// Spec: docs/superpowers/specs/2026-07-31-m4g-valuation-binding-design.md
    /// Constitution: M1GManager / HADGSManager / SplitM2eICManager are never modified;
    /// this class mirrors what it needs rather than reaching into them.
    /// </summary>
    public class M4GManager : M1GManager
    {
        /// <summary>Creates a new instance of this manager.</summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public M4GManager(Instance instance) : base(instance)
        {
            _m4gConfig = instance.ControllerConfig.OrderBatchingConfig as M4GConfiguration;
            if (_m4gConfig == null)
                throw new InvalidOperationException("M4GManager requires an M4GConfiguration.");
            // Unit-consistency guard (spec 3.5): lambda/mu/epsilon are priced in metres, which
            // requires the objective's distance terms to be metres too. StarveAwareCostEnabled
            // switches the cost functions to seconds - fail hard rather than solve nonsense.
            if (instance.SettingConfig != null && instance.SettingConfig.StarveAwareCostEnabled)
                throw new InvalidOperationException(
                    "M4G prices lines in metres but StarveAwareCostEnabled makes the distance terms seconds. "
                    + "Set StarveAwareCostEnabled=false or disable M4G.");
        }

        /// <summary>The M4G configuration of this manager.</summary>
        private M4GConfiguration _m4gConfig;
    }
}
```

- [ ] **Step 5: 加入 csproj**

在 `RAWSimO.Core/RAWSimO.Core.csproj` 找到 `<Compile Include="Control\Defaults\OrderBatching\SplitM2eICManager.cs" />`，在其後加入：

```xml
    <Compile Include="Control\Defaults\OrderBatching\M4GManager.cs" />
```

- [ ] **Step 6: 建立組態檔**

複製 `Material/Instances/CoreBenchmark/small/m1g.xconf` 為 `m4g.xconf`，然後只改 `OrderBatchingConfig` 區段的型別與新增欄位。完成後檔案的 `<OrderBatchingConfig>` 元素必須長成：

```xml
  <OrderBatchingConfig xsi:type="M4GConfiguration">
    <LambdaScale>1</LambdaScale>
    <MuScale>1</MuScale>
    <DeltaScale>1</DeltaScale>
    <EpsilonScale>0.001</EpsilonScale>
    <WarmupLines>50</WarmupLines>
    <LambdaFallback>10</LambdaFallback>
    <DeltaFallback>0.5</DeltaFallback>
    <LinesPerOrderFallback>2.4</LinesPerOrderFallback>
    <LambdaFixed>0</LambdaFixed>
    <DeltaFixed>0</DeltaFixed>
    <DegenerateToBindingOnly>false</DegenerateToBindingOnly>
    <ValuationOrderLimit>0</ValuationOrderLimit>
    <SplitMarginalProbeEnabled>false</SplitMarginalProbeEnabled>
    <SplitMarginalProbeEveryNDecisions>50</SplitMarginalProbeEveryNDecisions>
    <SplitMarginalProbeTimeLimitSec>10</SplitMarginalProbeTimeLimitSec>
  </OrderBatchingConfig>
```

**注意**：`m1g.xconf` 中 `OrderBatchingConfig` 以外的所有區段（PathPlanning、TaskAllocation、PodStorage、Replenishment 等）必須**逐 byte 保持不變，包含換行符（CRLF）**。改完用下列指令驗證只有目標區段不同：

```bash
diff <(grep -v -E "OrderBatchingConfig|LambdaScale|MuScale|DeltaScale|EpsilonScale|WarmupLines|LambdaFallback|DeltaFallback|LinesPerOrderFallback|LambdaFixed|DeltaFixed|DegenerateToBindingOnly|ValuationOrderLimit|SplitMarginalProbe" Material/Instances/CoreBenchmark/small/m1g.xconf) <(grep -v -E "OrderBatchingConfig|LambdaScale|MuScale|DeltaScale|EpsilonScale|WarmupLines|LambdaFallback|DeltaFallback|LinesPerOrderFallback|LambdaFixed|DeltaFixed|DegenerateToBindingOnly|ValuationOrderLimit|SplitMarginalProbe" Material/Instances/CoreBenchmark/small/m4g.xconf)
```

預期輸出：**空**（無任何差異行）。

- [ ] **Step 7: 建置**

Run:
```
MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release
```
Expected: `Build succeeded.` `0 Error(s)`

- [ ] **Step 8: Smoke run（驗證註冊鏈通了）**

Run:
```
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\fixed_fill1350_inv70.xsett Material\Instances\CoreBenchmark\small\m4g.xconf out\m4g_t1 0
```
Expected：程式正常跑完不擲例外，`out\m4g_t1\` 產生統計檔。因為尚未覆寫決策方法，**KPI 應與同設定下的 M1G 完全相同**——若不同，代表 xconf 的非目標區段被動到了，回 Step 6 修正。

- [ ] **Step 9: 確認改動範圍並提交**

```bash
git diff --stat
```
預期只有 5 個檔案：`MethodConfigurationsOB.cs`、`MethodConfiguration.cs`、`Controller.cs`、`RAWSimO.Core.csproj`、外加新增的 `M4GManager.cs` 與 `m4g.xconf`。

```bash
git add RAWSimO.Core/Configurations/MethodConfigurationsOB.cs RAWSimO.Core/Configurations/MethodConfiguration.cs RAWSimO.Core/Control/Controller.cs RAWSimO.Core/RAWSimO.Core.csproj RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs Material/Instances/CoreBenchmark/small/m4g.xconf
git commit -m "feat(m4g): config, registration and manager skeleton"
```

---

## Task 2: 價格自我校準（`M4GPricing`）

**Files:**
- Create: `RAWSimO.Core/Control/Defaults/OrderBatching/M4GPricing.cs`
- Modify: `RAWSimO.Core/RAWSimO.Core.csproj`
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs`

**Interfaces:**
- Consumes: `M4GConfiguration`（Task 1）
- Produces:
  - `M4GPricing(M4GConfiguration config)`
  - `double Lambda(double cumulativeDistanceMetres)` — 公尺/line
  - `double Mu(double cumulativeDistanceMetres)` — 公尺/單
  - `double Delta()` — 0..1
  - `double Epsilon(double cumulativeDistanceMetres)` — 公尺/件
  - `void RegisterClosedLines(int count)`
  - `void RegisterCompletedOrders(int count)`
  - `void RegisterValuedButUnbound(IEnumerable<string> lineKeys)`
  - `void RegisterLineClosed(string lineKey)`
  - `int CumulativeClosedLines { get; }`
  - Task 4 用 `Lambda`/`Mu`/`Delta`/`Epsilon` 組目標式；Task 5 用 `Register*` 更新統計。
  - `lineKey` 的格式固定為 `orderId + ":" + skuId`，Task 5 必須用同一格式產生，否則兌現率會恆為 0。

- [ ] **Step 1: 建立 `M4GPricing.cs`**

```csharp
using System;
using System.Collections.Generic;
using RAWSimO.Core.Configurations;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// M4G price calibration (spec 3.5). Every price is denominated in metres and derived
    /// from running statistics, so the objective carries no hand-tuned constant.
    ///
    /// lambda = distance travelled per closed line          [m / line]
    /// mu     = lambda * lines per completed order          [m / order]
    /// delta  = share of valued-but-unbound lines that were later actually closed  [0..1]
    /// epsilon = EpsilonScale * lambda, a tie-break only    [m / unit]
    ///
    /// Pure state machine: it never touches the solver, the instance or the file system,
    /// which keeps it reasonable to reason about and to check by hand from the decision log.
    /// </summary>
    public class M4GPricing
    {
        private readonly M4GConfiguration _config;
        private int _closedLines;
        private int _completedOrders;
        private int _valuedUnboundTotal;
        private int _valuedUnboundRealised;
        /// <summary>Line keys that were valued but not bound, and have not yet been closed.</summary>
        private readonly HashSet<string> _pendingValuedUnbound = new HashSet<string>();

        /// <summary>Creates the pricing state machine.</summary>
        /// <param name="config">The owning manager's configuration.</param>
        public M4GPricing(M4GConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            _config = config;
        }

        /// <summary>Cumulative number of order lines closed so far.</summary>
        public int CumulativeClosedLines { get { return _closedLines; } }
        /// <summary>Cumulative number of orders completed so far.</summary>
        public int CumulativeCompletedOrders { get { return _completedOrders; } }
        /// <summary>Cumulative number of lines that were valued without being bound.</summary>
        public int CumulativeValuedUnbound { get { return _valuedUnboundTotal; } }
        /// <summary>How many of those were closed afterwards.</summary>
        public int CumulativeValuedUnboundRealised { get { return _valuedUnboundRealised; } }

        /// <summary>Whether the running statistics are still too thin to price from.</summary>
        private bool InWarmup { get { return _closedLines < _config.WarmupLines; } }

        /// <summary>Metres per closed order line.</summary>
        /// <param name="cumulativeDistanceMetres">Instance.StatOverallDistanceTraveled at decision time.</param>
        public double Lambda(double cumulativeDistanceMetres)
        {
            if (_config.LambdaFixed > 0) return _config.LambdaFixed;
            if (InWarmup) return _config.LambdaScale * _config.LambdaFallback;
            return _config.LambdaScale * (cumulativeDistanceMetres / Math.Max(1, _closedLines));
        }

        /// <summary>Metres per completed order = lambda * lines per order.</summary>
        /// <param name="cumulativeDistanceMetres">Instance.StatOverallDistanceTraveled at decision time.</param>
        public double Mu(double cumulativeDistanceMetres)
        {
            double linesPerOrder = _completedOrders > 0
                ? (double)_closedLines / _completedOrders
                : _config.LinesPerOrderFallback;
            return _config.MuScale * Lambda(cumulativeDistanceMetres) * linesPerOrder;
        }

        /// <summary>Realisation rate of valuation that was not bound this decision, clamped to [0,1].</summary>
        public double Delta()
        {
            if (_config.DeltaFixed > 0) return Math.Min(1.0, _config.DeltaFixed);
            double raw = _valuedUnboundTotal < _config.WarmupLines
                ? _config.DeltaFallback
                : (double)_valuedUnboundRealised / Math.Max(1, _valuedUnboundTotal);
            return Math.Max(0.0, Math.Min(1.0, _config.DeltaScale * raw));
        }

        /// <summary>Tie-break weight on bound units.</summary>
        /// <param name="cumulativeDistanceMetres">Instance.StatOverallDistanceTraveled at decision time.</param>
        public double Epsilon(double cumulativeDistanceMetres)
        { return _config.EpsilonScale * Lambda(cumulativeDistanceMetres); }

        /// <summary>Records lines closed by this decision.</summary>
        public void RegisterClosedLines(int count)
        { if (count > 0) _closedLines += count; }

        /// <summary>Records orders completed by this decision.</summary>
        public void RegisterCompletedOrders(int count)
        { if (count > 0) _completedOrders += count; }

        /// <summary>
        /// Records lines the valuation layer scored but the binding layer did not take.
        /// Re-registering an already pending key is a no-op, so a line valued across many
        /// consecutive decisions still counts once in the denominator.
        /// </summary>
        public void RegisterValuedButUnbound(IEnumerable<string> lineKeys)
        {
            if (lineKeys == null) return;
            foreach (var key in lineKeys)
                if (_pendingValuedUnbound.Add(key))
                    _valuedUnboundTotal++;
        }

        /// <summary>Records that a specific line was closed; credits the realisation numerator if it was pending.</summary>
        public void RegisterLineClosed(string lineKey)
        {
            if (lineKey == null) return;
            if (_pendingValuedUnbound.Remove(lineKey))
                _valuedUnboundRealised++;
        }

        /// <summary>Stable key for a line, shared by the valuation bookkeeping and the commit path.</summary>
        public static string LineKey(int orderId, int skuId)
        { return orderId.ToString() + ":" + skuId.ToString(); }
    }
}
```

- [ ] **Step 2: 加入 csproj**

在 `RAWSimO.Core/RAWSimO.Core.csproj` 剛才那行之後加入：

```xml
    <Compile Include="Control\Defaults\OrderBatching\M4GPricing.cs" />
```

- [ ] **Step 3: 在 manager 裡建立 pricing 實例與決策日誌**

在 `M4GManager.cs` 的 `_m4gConfig` 欄位之後加入：

```csharp
        /// <summary>Price calibration state (spec 3.5).</summary>
        private M4GPricing _pricing;
        /// <summary>Per-decision diagnostic log.</summary>
        private System.IO.StreamWriter _decisionLog;
        /// <summary>Running decision counter, also the probe cadence clock.</summary>
        private int _decisionIndex = 0;

        /// <summary>Lazily opens m4g_decision_log.csv in the run's statistics directory.</summary>
        private void EnsureDecisionLog()
        {
            if (_decisionLog != null) return;
            string dir = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            _decisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "m4g_decision_log.csv"), false)
            { AutoFlush = true };
            _decisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsPa,podsPb,botsRa,"
                + "lambda,mu,delta,epsilon,valuedLines,boundLines,valuedOrders,boundOrders,newTrips,boundUnits,"
                + "objective,solveSec");
        }

        /// <summary>Writes one decision row. Every numeric field is written unformatted for exact diffing.</summary>
        private void WriteDecision(bool solved, int pendingOrders, int stationsWithCap, int podsPa, int podsPb,
            int botsRa, double lambda, double mu, double delta, double epsilon, int valuedLines, int boundLines,
            int valuedOrders, int boundOrders, int newTrips, int boundUnits, double objective, double solveSec)
        {
            EnsureDecisionLog();
            _decisionLog.WriteLine(string.Join(",", new string[] {
                _decisionIndex.ToString(),
                Instance.Controller.CurrentTime.ToString(),
                (solved ? "1" : "0"),
                pendingOrders.ToString(), stationsWithCap.ToString(), podsPa.ToString(), podsPb.ToString(),
                botsRa.ToString(), lambda.ToString(), mu.ToString(), delta.ToString(), epsilon.ToString(),
                valuedLines.ToString(), boundLines.ToString(), valuedOrders.ToString(), boundOrders.ToString(),
                newTrips.ToString(), boundUnits.ToString(), objective.ToString(), solveSec.ToString() }));
        }
```

並在建構子最後一行（單位防呆之後）加入：

```csharp
            _pricing = new M4GPricing(_m4gConfig);
```

- [ ] **Step 4: 建置**

Run: `MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`
Expected: `Build succeeded.` `0 Error(s)`

- [ ] **Step 5: 確認範圍並提交**

```bash
git diff --stat
git add RAWSimO.Core/Control/Defaults/OrderBatching/M4GPricing.cs RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs RAWSimO.Core/RAWSimO.Core.csproj
git commit -m "feat(m4g): self-calibrating price state machine + decision log"
```

---

## Task 3: 候選集建構與估值層 MILP

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs`

**Interfaces:**
- Consumes: `M4GPricing`（Task 2）
- Produces:
  - `private M4GSnapshot BuildSnapshot()` — 決策時的世界快照
  - `M4GSnapshot` 的公開欄位：`PendingOrders`、`Cs`、`Pa`、`Pb`、`Ra`、`PodToBot`、`InboundPods`、`Residuals`、`PiSKU`
  - Task 4 直接讀這些欄位建綁定層與目標式；Task 5 讀 `Residuals` 做 decode。

- [ ] **Step 1: 加入快照型別與建構方法**

在 `M4GManager.cs` 類別內加入：

```csharp
        /// <summary>Decision-time snapshot of the world. Mirrors what SplitM2eICManager builds,
        /// minus every IC gate - M4G has no supply cap, no pipeline floor and no lead-time gate.</summary>
        private sealed class M4GSnapshot
        {
            /// <summary>Orders eligible for this decision (parents carry residual demand only).</summary>
            public HashSet<Order> PendingOrders = new HashSet<Order>();
            /// <summary>Free slots per station.</summary>
            public Dictionary<OutputStation, int> Cs = new Dictionary<OutputStation, int>();
            /// <summary>Idle pods standing in storage that carry at least one demanded SKU.</summary>
            public HashSet<Pod> Pa = new HashSet<Pod>();
            /// <summary>Pods already committed to a station (at it or en route).</summary>
            public HashSet<Pod> Pb = new HashSet<Pod>();
            /// <summary>Bots available to be dispatched.</summary>
            public HashSet<Bot> Ra = new HashSet<Bot>();
            /// <summary>Bot owning each committed pod.</summary>
            public Dictionary<Pod, Bot> PodToBot = new Dictionary<Pod, Bot>();
            /// <summary>Committed pods grouped by their destination station.</summary>
            public Dictionary<OutputStation, HashSet<Pod>> InboundPods = new Dictionary<OutputStation, HashSet<Pod>>();
            /// <summary>Remaining demand per order per SKU.</summary>
            public Dictionary<Order, Dictionary<ItemDescription, int>> Residuals
                = new Dictionary<Order, Dictionary<ItemDescription, int>>();
            /// <summary>Pods carrying each demanded SKU.</summary>
            public Dictionary<ItemDescription, List<Pod>> PiSKU = new Dictionary<ItemDescription, List<Pod>>();
            /// <summary>All pods in the model = Pa union Pb.</summary>
            public HashSet<Pod> AllPods = new HashSet<Pod>();
        }

        /// <summary>
        /// Builds the decision snapshot. Note the deliberate difference from M3G: the pending
        /// set is NOT truncated to an urgent subset and NOT clipped to the free-slot count -
        /// the valuation layer's whole point is to see the entire backlog (spec D3).
        /// </summary>
        private M4GSnapshot BuildSnapshot()
        {
            M4GSnapshot snap = new M4GSnapshot();
            snap.Cs = GenerateCs();
            snap.InboundPods = GeneratePs(snap.Cs);

            // Committed pods and their bots.
            foreach (var entry in snap.InboundPods)
                foreach (Pod pod in entry.Value)
                {
                    if (snap.PodToBot.ContainsKey(pod)) continue;
                    Bot owner = null;
                    if (Instance.ResourceManager._usedPods.ContainsKey(pod))
                        owner = Instance.ResourceManager._usedPods[pod];
                    else if (Instance.ResourceManager.BottoPod.ContainsValue(pod))
                        owner = Instance.ResourceManager.BottoPod.First(v => v.Value.ID == pod.ID).Key;
                    if (owner == null) continue;   // ownership not visible yet this tick
                    snap.PodToBot[pod] = owner;
                    snap.Pb.Add(pod);
                    snap.AllPods.Add(pod);
                }

            // Orders whose residual demand is at least partly in stock.
            HashSet<Order> candidates = new HashSet<Order>(_pendingOrders.Where(o =>
                o.RemainingPositions.Any(p => Instance.StockInfo.GetActualStock(p.Key) >= 1)));
            HashSet<ItemDescription> demanded = new HashSet<ItemDescription>(
                candidates.SelectMany(o => o.RemainingPositions.Select(p => p.Key)));

            // Idle storage pods carrying something demanded.
            foreach (var pod in Instance.ResourceManager.UnusedPods.Where(v =>
                v.IsAvailabletoOiSKU(demanded)
                && !Instance.ResourceManager.BottoPod.ContainsValue(v)
                && !Instance.ResourceManager._usedPods.ContainsKey(v)
                && v.Waypoint != null && v.Waypoint.PodStorageLocation))
            {
                snap.Pa.Add(pod);
                snap.AllPods.Add(pod);
            }

            // Dispatchable bots (mirrors M1G's three admission cases).
            foreach (var bot in Instance._outputstationbots)
            {
                if (bot.Pod == null && !Instance.ResourceManager._usedPods.ContainsValue(bot)
                    && !Instance.ResourceManager.BottoPod.ContainsKey(bot))
                    snap.Ra.Add(bot);
                else if (bot.Pod == null && !Instance.ResourceManager.BottoPod.ContainsKey(bot)
                    && !snap.PodToBot.ContainsValue(bot) && bot.CurrentTask is RestTask
                    && bot.GetInfoDestinationWaypoint() == null)
                    snap.Ra.Add(bot);
                else if (CanUseReturnPendingBot(bot))
                    snap.Ra.Add(bot);
            }

            snap.PiSKU = GeneratePiSKU(snap.AllPods);
            snap.PendingOrders = new HashSet<Order>(candidates.Where(o =>
                o.RemainingPositions.Any(p => snap.PiSKU.ContainsKey(p.Key))));

            // Optional solve-time convergence knob: keep only the most urgent K orders.
            if (_m4gConfig.ValuationOrderLimit > 0 && snap.PendingOrders.Count > _m4gConfig.ValuationOrderLimit)
                snap.PendingOrders = new HashSet<Order>(snap.PendingOrders
                    .OrderBy(o => o.DueTime).ThenBy(o => o.ID)
                    .Take(_m4gConfig.ValuationOrderLimit));

            snap.Residuals = snap.PendingOrders.ToDictionary(
                o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            return snap;
        }
```

- [ ] **Step 2: 加入變數命名與估值層建模**

在 `M4GManager.cs` 內加入。**符號命名規則固定如下，Task 4/5/7 必須沿用**：
`xps_<podId>_<stationId>`、`yrp_<botId>_<podId>`、`qh_<skuId>_<orderId>_<podId>_<stationId>`、`ch_<orderId>_<skuId>`、`zh_<orderId>`。

```csharp
        /// <summary>Names of the symbols the model was built from, kept for decoding.</summary>
        private sealed class M4GSymbols
        {
            public List<Symbol> Xps = new List<Symbol>();
            public List<Symbol> Yrp = new List<Symbol>();
            public List<Symbol> Qhat = new List<Symbol>();
            public List<Symbol> Chat = new List<Symbol>();
            public List<Symbol> Zhat = new List<Symbol>();
        }

        /// <summary>Enumerates the decision symbols for a snapshot.</summary>
        private M4GSymbols BuildSymbols(M4GSnapshot snap)
        {
            M4GSymbols sym = new M4GSymbols();
            foreach (var pod in snap.AllPods)
                foreach (var station in snap.Cs.Keys)
                    sym.Xps.Add(new Symbol { pod = pod, outputstation = station,
                        name = "xps_" + pod.ID + "_" + station.ID });
            foreach (var bot in snap.Ra)
                foreach (var pod in snap.Pa)
                    sym.Yrp.Add(new Symbol { robot = bot, pod = pod,
                        name = "yrp_" + bot.ID + "_" + pod.ID });
            foreach (var pod in snap.Pb)
                sym.Yrp.Add(new Symbol { robot = snap.PodToBot[pod], pod = pod,
                    name = "yrp_" + snap.PodToBot[pod].ID + "_" + pod.ID });
            foreach (var order in snap.PendingOrders)
            {
                sym.Zhat.Add(new Symbol { order = order, name = "zh_" + order.ID });
                foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                {
                    sym.Chat.Add(new Symbol { order = order, skui = sku.Key,
                        name = "ch_" + order.ID + "_" + sku.Key.ID });
                    foreach (var pod in snap.PiSKU[sku.Key])
                        foreach (var station in snap.Cs.Keys)
                            sym.Qhat.Add(new Symbol { order = order, skui = sku.Key, pod = pod,
                                outputstation = station,
                                name = "qh_" + sku.Key.ID + "_" + order.ID + "_" + pod.ID + "_" + station.ID });
                }
            }
            return sym;
        }
```

- [ ] **Step 3: 加入共用層與估值層約束**

```csharp
        /// <summary>Adds the shared pod/bot constraints R1-R5 (spec 3.4).</summary>
        private void AddSharedConstraints(LinearModel wrapper, M4GSnapshot snap, M4GSymbols sym,
            VariableCollection<string> bin)
        {
            // (R1) each pod goes to at most one station
            foreach (var pod in snap.AllPods)
                wrapper.AddConstr(LinearExpression.Sum(sym.Xps.Where(v => v.pod.ID == pod.ID)
                    .Select(v => bin[v.name])) <= 1, "R1");
            // (R2) dispatching a storage pod requires a bot
            foreach (var pod in snap.Pa)
                wrapper.AddConstr(LinearExpression.Sum(sym.Xps.Where(v => v.pod.ID == pod.ID).Select(v => bin[v.name]))
                    <= LinearExpression.Sum(sym.Yrp.Where(v => v.pod.ID == pod.ID).Select(v => bin[v.name])), "R2");
            // (R3) each bot carries at most one pod
            foreach (var bot in snap.Ra)
                wrapper.AddConstr(LinearExpression.Sum(sym.Yrp.Where(v => v.robot.ID == bot.ID)
                    .Select(v => bin[v.name])) <= 1, "R3");
            // (R4) each pod is carried by at most one bot
            foreach (var pod in snap.Pa)
                wrapper.AddConstr(LinearExpression.Sum(sym.Yrp.Where(v => v.pod.ID == pod.ID)
                    .Select(v => bin[v.name])) <= 1, "R4");
            // (R5) already-committed pods are fixed to their destination and carrier
            foreach (var entry in snap.InboundPods)
                foreach (var pod in entry.Value.Where(p => snap.Pb.Contains(p)))
                {
                    wrapper.AddConstr(bin["xps_" + pod.ID + "_" + entry.Key.ID] == 1, "R5a");
                    wrapper.AddConstr(bin["yrp_" + snap.PodToBot[pod].ID + "_" + pod.ID] == 1, "R5b");
                }
        }

        /// <summary>Adds the valuation-layer constraints V1-V4 (spec 3.4).</summary>
        private void AddValuationConstraints(LinearModel wrapper, M4GSnapshot snap, M4GSymbols sym,
            VariableCollection<string> bin, VariableCollection<string> qh)
        {
            // (V1) draws from a pod at a station are bounded by its stock and require dispatch
            foreach (var group in sym.Qhat.GroupBy(v => new { sku = v.skui.ID, pod = v.pod.ID, st = v.outputstation.ID }))
            {
                var first = group.First();
                wrapper.AddConstr(LinearExpression.Sum(group.Select(v => qh[v.name]))
                    <= first.pod.CountAvailable(first.skui) * bin["xps_" + first.pod.ID + "_" + first.outputstation.ID],
                    "V1");
            }
            foreach (var order in snap.PendingOrders)
                foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                {
                    var draws = sym.Qhat.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID)
                        .Select(v => qh[v.name]).ToList();
                    // (V2) never draw more than the residual demand
                    wrapper.AddConstr(LinearExpression.Sum(draws) <= sku.Value, "V2");
                    // (V3) a line only counts as closed when it is drawn in full
                    wrapper.AddConstr(LinearExpression.Sum(draws)
                        >= sku.Value * bin["ch_" + order.ID + "_" + sku.Key.ID], "V3");
                    // (V4) completing an order requires every one of its lines closed
                    wrapper.AddConstr(bin["ch_" + order.ID + "_" + sku.Key.ID] >= bin["zh_" + order.ID], "V4");
                }
        }
```

- [ ] **Step 4: 建置**

Run: `MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`
Expected: `Build succeeded.` `0 Error(s)`

若出現 CS1628（lambda 捕獲 out 參數），依 Global Constraints 用區域變數複本繞過。

- [ ] **Step 5: 提交**

```bash
git diff --stat
git add RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs
git commit -m "feat(m4g): decision snapshot + valuation-layer model (V1-V4, R1-R5)"
```

---

## Task 4: 綁定層與目標式，接上決策入口

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs`

**Interfaces:**
- Consumes: `BuildSnapshot`、`BuildSymbols`、`AddSharedConstraints`、`AddValuationConstraints`（Task 3）、`M4GPricing`（Task 2）
- Produces:
  - `private M4GResult SolveM4G(M4GSnapshot snap)`
  - `M4GResult` 欄位：`HasSolution`、`Objective`、`BoundDraws`（`Dictionary<Symbol,int>`）、`ValuedLineKeys`、`BoundLineKeys`、`ValuedOrders`、`BoundOrders`、`NewTripCount`、`SolveSec`、`BotByPodId`
  - Task 5 讀 `BoundDraws` 與 `BotByPodId` 做 commit；Task 7 讀 `Objective` 做反事實比較。
- 綁定層符號命名：`q_<skuId>_<orderId>_<podId>_<stationId>`、`y_<orderId>_<stationId>`、`c_<orderId>_<skuId>`、`z_<orderId>`。

- [ ] **Step 1: 加入結果型別**

```csharp
        /// <summary>Outcome of one M4G solve. Only BoundDraws has side effects downstream.</summary>
        private sealed class M4GResult
        {
            public bool HasSolution;
            public double Objective;
            public double SolveSec;
            public int NewTripCount;
            /// <summary>Binding-layer draws: (sku, order, pod, station) -> units.</summary>
            public Dictionary<Symbol, int> BoundDraws = new Dictionary<Symbol, int>();
            /// <summary>Bot chosen for each newly dispatched pod.</summary>
            public Dictionary<int, Bot> BotByPodId = new Dictionary<int, Bot>();
            /// <summary>Line keys the valuation layer closed.</summary>
            public HashSet<string> ValuedLineKeys = new HashSet<string>();
            /// <summary>Line keys the binding layer closed.</summary>
            public HashSet<string> BoundLineKeys = new HashSet<string>();
            public int ValuedOrders;
            public int BoundOrders;
        }
```

- [ ] **Step 2: 加入求解方法**

```csharp
        /// <summary>
        /// Builds and solves the M4G model. One solve decides pods, bots, the valuation-layer
        /// split shape and the binding-layer subset jointly (spec D2) - never in two passes.
        /// </summary>
        private M4GResult SolveM4G(M4GSnapshot snap)
        {
            M4GResult result = new M4GResult();
            M4GSymbols sym = BuildSymbols(snap);
            if (sym.Qhat.Count == 0) return result;

            LinearModel wrapper = new LinearModel(SolverType.Gurobi, (string s) => { Console.Write(s); });
            int maxUnits = snap.Residuals.Count > 0
                ? snap.Residuals.Values.SelectMany(d => d.Values).DefaultIfEmpty(1).Max() : 1;
            int maxSlots = snap.Cs.Count > 0 ? snap.Cs.Values.DefaultIfEmpty(1).Max() : 1;
            VariableCollection<string> bin = new VariableCollection<string>(wrapper, VariableType.Binary, 0, 1,
                (string s) => { return s; });
            VariableCollection<string> qh = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxUnits,
                (string s) => { return s; });
            VariableCollection<string> qb = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxUnits,
                (string s) => { return s; });

            AddSharedConstraints(wrapper, snap, sym, bin);
            AddValuationConstraints(wrapper, snap, sym, bin, qh);

            // ── Binding layer B1-B7 (spec 3.4) ──
            foreach (var v in sym.Qhat)
            {
                string qbName = "q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID;
                // (B1) binding is a subset of valuation. DegenerateToBindingOnly forces equality,
                // collapsing the layers back to M3G-like behaviour for the ablation arm.
                if (_m4gConfig.DegenerateToBindingOnly)
                    wrapper.AddConstr(qb[qbName] == qh[v.name], "B1eq");
                else
                    wrapper.AddConstr(qb[qbName] <= qh[v.name], "B1");
            }
            foreach (var order in snap.PendingOrders)
            {
                int totalResidual = snap.Residuals[order].Values.Sum();
                foreach (var station in snap.Cs.Keys)
                {
                    var draws = sym.Qhat.Where(v => v.order.ID == order.ID && v.outputstation.ID == station.ID)
                        .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                        .ToList();
                    if (draws.Count == 0) continue;
                    string yName = "y_" + order.ID + "_" + station.ID;
                    // (B2) drawing for an order at a station occupies one of its slots
                    wrapper.AddConstr(LinearExpression.Sum(draws) <= totalResidual * bin[yName], "B2");
                    // (B4) no empty binding
                    wrapper.AddConstr(bin[yName] <= LinearExpression.Sum(draws), "B4");
                }
                foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                {
                    var skuDraws = sym.Qhat.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID)
                        .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                        .ToList();
                    string cName = "c_" + order.ID + "_" + sku.Key.ID;
                    // (B5) a bound line closure needs the full residual bound
                    wrapper.AddConstr(LinearExpression.Sum(skuDraws) >= sku.Value * bin[cName], "B5");
                    // (B6) binding closures are a subset of valued closures
                    wrapper.AddConstr(bin[cName] <= bin["ch_" + order.ID + "_" + sku.Key.ID], "B6c");
                    // (B7) a bound completion needs every line bound-closed
                    wrapper.AddConstr(bin[cName] >= bin["z_" + order.ID], "B7");
                }
                wrapper.AddConstr(bin["z_" + order.ID] <= bin["zh_" + order.ID], "B6z");
            }
            // (B3) slot capacity - an inequality, unlike M3G's eshi4 equality
            foreach (var station in snap.Cs.Keys)
            {
                var ys = snap.PendingOrders.Select(o => bin["y_" + o.ID + "_" + station.ID]).ToList();
                if (ys.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(ys) <= snap.Cs[station], "B3");
            }

            // ── Objective T1-T5 (spec 3.3), everything denominated in metres ──
            double cumDist = Instance.StatOverallDistanceTraveled;
            double lambda = _pricing.Lambda(cumDist);
            double mu = _pricing.Mu(cumDist);
            double delta = _pricing.Delta();
            double epsilon = _pricing.Epsilon(cumDist);

            // T1/T2: only newly dispatched (Pa) pods pay travel - Pb trips are sunk.
            LinearExpression objective =
                LinearExpression.Sum(sym.Xps.Where(v => snap.Pa.Contains(v.pod))
                    .Select(v => bin[v.name] * (M1GPodStationCost(v.pod, v.outputstation)
                        + PodStationExtraCost(v.pod, v.outputstation))), wrapper)
                + LinearExpression.Sum(sym.Yrp.Where(v => snap.Pa.Contains(v.pod) && v.pod.Waypoint != null)
                    .Select(v => bin[v.name] * M1GBotPodCost(v.robot, v.pod)), wrapper);
            // T3: bound line closures at full price, valued-but-unbound at the realisation rate.
            objective = objective
                + LinearExpression.Sum(sym.Chat.Select(v => bin["c_" + v.order.ID + "_" + v.skui.ID]))
                    * (-lambda * (1.0 - delta))
                + LinearExpression.Sum(sym.Chat.Select(v => bin[v.name])) * (-lambda * delta);
            // T4: same split for completed orders.
            objective = objective
                + LinearExpression.Sum(sym.Zhat.Select(v => bin["z_" + v.order.ID])) * (-mu * (1.0 - delta))
                + LinearExpression.Sum(sym.Zhat.Select(v => bin[v.name])) * (-mu * delta);
            // T5: tie-break that prefers executing now among equally valued solutions.
            objective = objective
                + LinearExpression.Sum(sym.Qhat.Select(v =>
                    qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])) * (-epsilon);
            wrapper.SetObjective(objective, OptimizationSense.Minimize);

            DateTime solveStart = DateTime.Now;
            wrapper.Update();
            wrapper.Optimize();
            result.SolveSec = (DateTime.Now - solveStart).TotalSeconds;
            if (!wrapper.HasSolution()) return result;

            result.HasSolution = true;
            result.Objective = wrapper.GetObjectiveValue();
            foreach (var v in sym.Qhat)
            {
                int units = (int)Math.Round(qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID
                    + "_" + v.outputstation.ID].GetValue());
                if (units > 0) result.BoundDraws[v] = units;
            }
            foreach (var v in sym.Chat)
            {
                if (Math.Round(bin[v.name].GetValue()) != 0)
                    result.ValuedLineKeys.Add(M4GPricing.LineKey(v.order.ID, v.skui.ID));
                if (Math.Round(bin["c_" + v.order.ID + "_" + v.skui.ID].GetValue()) != 0)
                    result.BoundLineKeys.Add(M4GPricing.LineKey(v.order.ID, v.skui.ID));
            }
            result.ValuedOrders = sym.Zhat.Count(v => Math.Round(bin[v.name].GetValue()) != 0);
            result.BoundOrders = sym.Zhat.Count(v => Math.Round(bin["z_" + v.order.ID].GetValue()) != 0);
            foreach (var v in sym.Xps.Where(v => snap.Pa.Contains(v.pod)))
                if (Math.Round(bin[v.name].GetValue()) != 0)
                {
                    result.NewTripCount++;
                    var carrier = sym.Yrp.FirstOrDefault(y => y.pod.ID == v.pod.ID
                        && Math.Round(bin[y.name].GetValue()) != 0);
                    if (carrier != null) result.BotByPodId[v.pod.ID] = carrier.robot;
                }
            return result;
        }
```

- [ ] **Step 3: 覆寫決策入口**

```csharp
        /// <summary>Entry point called by the engine whenever a station has a free slot.</summary>
        protected override void DecideAboutPendingOrders()
        {
            DateTime start = DateTime.Now;
            M4GSnapshot snap = BuildSnapshot();
            if (snap.PendingOrders.Count == 0 || snap.Cs.Count == 0
                || !snap.Cs.Values.Any(v => v > 0) || snap.AllPods.Count == 0)
                return;
            M4GResult result = SolveM4G(snap);
            double cumDist = Instance.StatOverallDistanceTraveled;
            WriteDecision(result.HasSolution, snap.PendingOrders.Count, snap.Cs.Count(c => c.Value > 0),
                snap.Pa.Count, snap.Pb.Count, snap.Ra.Count,
                _pricing.Lambda(cumDist), _pricing.Mu(cumDist), _pricing.Delta(), _pricing.Epsilon(cumDist),
                result.ValuedLineKeys.Count, result.BoundLineKeys.Count, result.ValuedOrders, result.BoundOrders,
                result.NewTripCount, result.BoundDraws.Values.Sum(), result.Objective, result.SolveSec);
            _decisionIndex++;
            if (result.HasSolution)
                CommitM4G(snap, result);
            Instance.Observer.TimeOrderBatchingbyMP((DateTime.Now - start).TotalSeconds);
        }
```

**注意**：`CommitM4G` 於 Task 5 實作。本 Task 先加入空實作以便建置：

```csharp
        /// <summary>Applies the binding layer. Implemented in Task 5.</summary>
        private void CommitM4G(M4GSnapshot snap, M4GResult result) { }
```

- [ ] **Step 4: 建置**

Run: `MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`
Expected: `Build succeeded.` `0 Error(s)`

若 `wrapper.GetObjectiveValue()` 不存在，改讀 `wrapper.GetObjective()`——以 `RAWSimO.SolverWrappers/LinearModel.cs` 實際公開的方法為準，並在本步驟註記實際採用的名稱。

- [ ] **Step 5: Smoke run（驗證會解、但還不會動作）**

Run:
```
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\fixed_fill1350_inv70.xsett Material\Instances\CoreBenchmark\small\m4g.xconf out\m4g_t4 0
```
Expected：
- 產生 `out\m4g_t4\m4g_decision_log.csv`
- 第一列資料的 `lambda` 欄為 `10`（暖機值）、`delta` 為 `0.5`
- `solved` 欄多數為 `1`
- 因為 commit 尚未實作，完成訂單數應為 0——**這是本 Task 的預期結果，不是錯誤**

檢查：
```bash
head -3 out/m4g_t4/m4g_decision_log.csv
awk -F, 'NR>1 && $3==1 {n++} END {print "solved rows:", n}' out/m4g_t4/m4g_decision_log.csv
```
Expected: `solved rows:` 大於 0。

- [ ] **Step 6: 提交**

```bash
git diff --stat
git add RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs
git commit -m "feat(m4g): binding layer B1-B7 + metre-denominated objective T1-T5"
```

---

## Task 5: Decode 與 Commit

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs`

**Interfaces:**
- Consumes: `M4GResult.BoundDraws`、`M4GResult.BotByPodId`（Task 4）、`M4GPricing.Register*`（Task 2）
- Produces: 完整可跑的 M4G。後續 Task 只加消融旗標與診斷。
- 重用既有基礎設施，**不得重新發明**：`Order.CreateSplitChild`、`Instance.ResourceManager.TransferExtractRequests`、`Instance.ResourceManager._Ziops`、`Pod.JustRegisterItem`、`AllocateOrder`。

- [ ] **Step 1: 實作 commit**

以下方完整版本取代 Task 4 Step 3 留下的空 `CommitM4G`：

```csharp
        /// <summary>
        /// Applies the binding layer and only the binding layer (spec 5.4). Whatever the
        /// valuation layer scored but the binding layer did not take has no side effect at
        /// all - it is priced once and discarded with the solve. Nothing is promised; the
        /// next decision re-solves the split shape against a fresh backlog.
        /// </summary>
        private void CommitM4G(M4GSnapshot snap, M4GResult result)
        {
            if (result.BoundDraws.Count == 0) return;

            // Register the picks on the pods so the trip carries a real request.
            foreach (var entry in result.BoundDraws)
                for (int i = 0; i < entry.Value; i++)
                    entry.Key.pod.JustRegisterItem(entry.Key.skui);

            int closedLines = 0, completedOrders = 0;
            foreach (var order in snap.PendingOrders.OrderBy(o => o.ID))
            {
                var mine = result.BoundDraws.Where(e => e.Key.order.ID == order.ID).ToList();
                if (mine.Count == 0) continue;

                // Group this order's bound draws by station: one child (or one plain
                // allocation) per station touched.
                foreach (var stationGroup in mine.GroupBy(e => e.Key.outputstation.ID))
                {
                    OutputStation station = stationGroup.First().Key.outputstation;
                    Dictionary<ItemDescription, int> quantities = new Dictionary<ItemDescription, int>();
                    foreach (var e in stationGroup)
                    {
                        if (!quantities.ContainsKey(e.Key.skui)) quantities[e.Key.skui] = 0;
                        quantities[e.Key.skui] += e.Value;
                    }
                    bool coversWholeOrder = snap.Residuals[order]
                        .All(p => quantities.ContainsKey(p.Key) && quantities[p.Key] >= p.Value);
                    // Fast path: the whole residual is served here and the order was never split
                    // before - no child needed, the order itself takes the slot.
                    Order target;
                    if (coversWholeOrder && stationGroup.Count() == mine.Count && !order.IsSplitParent)
                        target = order;
                    else
                    {
                        target = Order.CreateSplitChild(order, quantities);
                        target.ID = Instance.Orders.Count + Instance.OrdersHandled + _decisionIndex;
                        Instance.ResourceManager.TransferExtractRequests(order, target);
                    }
                    AllocateOrder(target, station);
                    Instance.StatCustomControllerInfo.CustomLogOB1++;
                    foreach (var e in stationGroup)
                    {
                        Symbol ziop = new Symbol { pod = e.Key.pod, order = target, outputstation = station,
                            skui = e.Key.skui,
                            name = "ziops_" + e.Key.skui.ID + "_" + target.ID + "_" + e.Key.pod.ID + "_" + station.ID };
                        Instance.ResourceManager._Ziops[station].Add(ziop, e.Value);
                    }
                }

                // Bookkeeping for the price calibration.
                foreach (var sku in snap.Residuals[order])
                {
                    int drawn = mine.Where(e => e.Key.skui.ID == sku.Key.ID).Sum(e => e.Value);
                    if (drawn >= sku.Value)
                    {
                        closedLines++;
                        _pricing.RegisterLineClosed(M4GPricing.LineKey(order.ID, sku.Key.ID));
                    }
                }
                if (order.IsFullyClaimed || snap.Residuals[order]
                    .All(p => mine.Where(e => e.Key.skui.ID == p.Key.ID).Sum(e => e.Value) >= p.Value))
                    completedOrders++;
                if (order.IsFullyClaimed)
                {
                    _pendingOrders.Remove(order);
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(order);
                }
            }
            _pricing.RegisterClosedLines(closedLines);
            _pricing.RegisterCompletedOrders(completedOrders);
            // Lines the valuation layer scored but the binding layer left behind: they enter
            // the delta denominator now and credit the numerator if they close later.
            _pricing.RegisterValuedButUnbound(result.ValuedLineKeys.Where(k => !result.BoundLineKeys.Contains(k)));
        }
```

- [ ] **Step 2: 建置**

Run: `MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`
Expected: `Build succeeded.` `0 Error(s)`

- [ ] **Step 3: 完整跑一場（背景，脫離 harness）**

建立 `run_m4g_t5.cmd`：
```
@echo off
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\fixed_fill1350_inv70.xsett Material\Instances\CoreBenchmark\small\m4g.xconf out\m4g_t5 0
```
以 PowerShell 執行：
```powershell
Start-Process -FilePath .\run_m4g_t5.cmd -WindowStyle Hidden
```

- [ ] **Step 4: 驗收本 Task**

跑完後檢查：
```bash
awk -F, 'NR>1 {bound+=$16; lines+=$14} END {print "bound units:", bound, " bound lines:", lines}' out/m4g_t5/m4g_decision_log.csv
tail -3 out/m4g_t5/m4g_decision_log.csv
```
Expected：
- `bound units` 明顯大於 0
- 最後幾列的 `lambda` **不再是 10**（已脫離暖機、改用跑動值），且落在 5–40 之間；若仍為 10，代表 `RegisterClosedLines` 沒被呼叫到，回 Step 1 檢查
- `delta` 落在 0 與 1 之間
- `out/m4g_t5/` 的統計檔中完成訂單數大於 0

- [ ] **Step 5: 提交**

```bash
git diff --stat
git add RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs run_m4g_t5.cmd
git commit -m "feat(m4g): binding-layer commit - children, Ziops, slot allocation, price bookkeeping"
```

---

## Task 6: 消融組態

**Files:**
- Create: `Material/Instances/CoreBenchmark/small/m4g_degenerate.xconf`
- Create: `Material/Instances/CoreBenchmark/small/m4g_lambda{05,2}.xconf`
- Create: `Material/Instances/CoreBenchmark/small/m4g_delta{05,2}.xconf`
- Create: `Material/Instances/CoreBenchmark/small/m4g_mu{05,2}.xconf`

**Interfaces:**
- Consumes: Task 1 的所有組態欄位。無新程式碼——這是驗證消融全部可由組態驅動、無需重新編譯的檢查點（沿用 M3G 消融的做法）。

- [ ] **Step 1: 產生七份消融組態**

每一份都由 `m4g.xconf` 複製，**只改一個欄位**：

| 檔名 | 唯一改動 |
|---|---|
| `m4g_degenerate.xconf` | `<DegenerateToBindingOnly>true</DegenerateToBindingOnly>` |
| `m4g_lambda05.xconf` | `<LambdaScale>0.5</LambdaScale>` |
| `m4g_lambda2.xconf` | `<LambdaScale>2</LambdaScale>` |
| `m4g_delta05.xconf` | `<DeltaScale>0.5</DeltaScale>` |
| `m4g_delta2.xconf` | `<DeltaScale>2</DeltaScale>` |
| `m4g_mu05.xconf` | `<MuScale>0.5</MuScale>` |
| `m4g_mu2.xconf` | `<MuScale>2</MuScale>` |

- [ ] **Step 2: 驗證每份只有一行不同**

```bash
for f in degenerate lambda05 lambda2 delta05 delta2 mu05 mu2; do
  echo "== $f =="
  diff Material/Instances/CoreBenchmark/small/m4g.xconf Material/Instances/CoreBenchmark/small/m4g_$f.xconf | grep -c "^[<>]"
done
```
Expected：每一份輸出 `2`（一行減、一行增）。任何大於 2 的都代表複製時動到別的地方，必須修正。

- [ ] **Step 3: 提交**

```bash
git add Material/Instances/CoreBenchmark/small/m4g_*.xconf
git commit -m "test(m4g): ablation configs - degenerate arm + lambda/mu/delta dose response"
```

---

## Task 7: 拆單邊際價值診斷

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs`
- Create: `Material/Instances/CoreBenchmark/small/m4g_probe.xconf`

**Interfaces:**
- Consumes: `SolveM4G`（Task 4）、`M4GSnapshot`（Task 3）
- Produces: `m4g_split_value.csv`，欄位固定為 `decision,time,pendingOrders,botsRa,objSplit,objNoSplit,marginalValue,linesSplit,linesNoSplit`

- [ ] **Step 1: 加入反事實求解**

`SolveM4G` 增加一個參數以支援禁止拆單的版本。修改其簽章為：

```csharp
        private M4GResult SolveM4G(M4GSnapshot snap, bool forbidSplitting)
```

並在 `AddValuationConstraints` 呼叫之後、綁定層之前插入：

```csharp
            // (Probe) no-split counterfactual: each line may be supplied by exactly one
            // (pod, station) pair. Same snapshot, same objective, one extra constraint -
            // the objective gap is the marginal value of splitting for this decision.
            if (forbidSplitting)
            {
                foreach (var group in sym.Qhat.GroupBy(v => new { o = v.order.ID, i = v.skui.ID }))
                {
                    var members = group.ToList();
                    foreach (var v in members)
                    {
                        string gName = "g_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID;
                        wrapper.AddConstr(qh[v.name]
                            <= snap.Residuals[v.order][v.skui] * bin[gName], "PG1");
                    }
                    wrapper.AddConstr(LinearExpression.Sum(members.Select(v =>
                        bin["g_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])) <= 1,
                        "PG2");
                }
            }
```

Task 4 中原有的兩個呼叫點改為 `SolveM4G(snap, false)`。

- [ ] **Step 2: 加入探針與日誌**

在 `M4GManager.cs` 加入：

```csharp
        /// <summary>Split marginal-value probe log.</summary>
        private System.IO.StreamWriter _probeLog;

        /// <summary>
        /// Re-solves the same snapshot with splitting forbidden and records the objective gap.
        /// Diagnostic only - the probe's solution is discarded and never committed.
        /// Note the gap is an instantaneous marginal value, not realised savings: only the
        /// first step of any solve is ever executed (spec 7.6).
        /// </summary>
        private void RunSplitMarginalProbe(M4GSnapshot snap, M4GResult splitResult)
        {
            if (_m4gConfig == null || !_m4gConfig.SplitMarginalProbeEnabled) return;
            if (_m4gConfig.SplitMarginalProbeEveryNDecisions <= 0) return;
            if (_decisionIndex % _m4gConfig.SplitMarginalProbeEveryNDecisions != 0) return;
            if (!splitResult.HasSolution) return;

            M4GResult noSplit = SolveM4G(snap, true);
            if (!noSplit.HasSolution) return;
            if (_probeLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null
                    ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir)) dir = ".";
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                _probeLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "m4g_split_value.csv"), false)
                { AutoFlush = true };
                _probeLog.WriteLine("decision,time,pendingOrders,botsRa,objSplit,objNoSplit,marginalValue,"
                    + "linesSplit,linesNoSplit");
            }
            _probeLog.WriteLine(string.Join(",", new string[] {
                _decisionIndex.ToString(), Instance.Controller.CurrentTime.ToString(),
                snap.PendingOrders.Count.ToString(), snap.Ra.Count.ToString(),
                splitResult.Objective.ToString(), noSplit.Objective.ToString(),
                (noSplit.Objective - splitResult.Objective).ToString(),
                splitResult.ValuedLineKeys.Count.ToString(), noSplit.ValuedLineKeys.Count.ToString() }));
        }
```

在 `DecideAboutPendingOrders` 的 `CommitM4G` 呼叫**之前**插入：

```csharp
            RunSplitMarginalProbe(snap, result);
```

（必須在 commit 之前——commit 會改變世界狀態，探針就不再是同一個快照。）

- [ ] **Step 3: 建立探針組態**

由 `m4g.xconf` 複製為 `m4g_probe.xconf`，只改一行：
```xml
    <SplitMarginalProbeEnabled>true</SplitMarginalProbeEnabled>
```

- [ ] **Step 4: 建置並跑探針**

Run: `MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`
Expected: `Build succeeded.` `0 Error(s)`

```powershell
Start-Process -FilePath .\run_m4g_probe.cmd -WindowStyle Hidden
```
（`run_m4g_probe.cmd` 內容同 Task 5 的腳本，但組態改為 `m4g_probe.xconf`、輸出目錄改為 `out\m4g_probe`。）

- [ ] **Step 5: 驗收**

```bash
awk -F, 'NR>1 {n++; s+=$7; if($7<0) neg++} END {print "probes:", n, " mean marginal value:", s/n, " negative:", neg+0}' out/m4g_probe/m4g_split_value.csv
```
Expected：
- `probes` 大於 10
- **`negative` 必須為 0**——禁止拆單是加約束，其最優值不可能優於不加約束的版本。出現負值代表探針的約束寫錯或求解器提前停止，必須修正後才能繼續。

- [ ] **Step 6: 提交**

```bash
git diff --stat
git add RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs Material/Instances/CoreBenchmark/small/m4g_probe.xconf run_m4g_probe.cmd
git commit -m "feat(m4g): no-split counterfactual probe measuring the marginal value of splitting"
```

---

## Task 8: 驗收與四方對照

**Files:**
- Create: `run_m4g_accept.cmd`
- Create: `docs/2026-07-31-m4g-acceptance.md`

**Interfaces:**
- Consumes: Task 1–7 的全部產出。無新程式碼。

- [ ] **Step 1: 撰寫驗收批次腳本**

`run_m4g_accept.cmd`（seed 0 與 1，正典＋退化組）：
```
@echo off
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set L=Material\Instances\CoreBenchmark\small\small.xlayo
set S=Material\Instances\CoreBenchmark\small\fixed_fill1350_inv70.xsett
set D=Material\Instances\CoreBenchmark\small
%CLI% %L% %S% %D%\m4g.xconf out\acc_m4g_s0 0
%CLI% %L% %S% %D%\m4g.xconf out\acc_m4g_s1 1
%CLI% %L% %S% %D%\m4g_degenerate.xconf out\acc_deg_s0 0
%CLI% %L% %S% %D%\m4g_degenerate.xconf out\acc_deg_s1 1
```

以 `Start-Process -FilePath .\run_m4g_accept.cmd -WindowStyle Hidden` 執行。

- [ ] **Step 2: 驗收 1（結構）**

比對 `acc_m4g_s0` 與 `acc_deg_s0` 的 pile-on 與貨架趟次。

判定：退化組（`DegenerateToBindingOnly=true`）的 pile-on 與趟次應落回 M3G 量級（pile-on ≈ 3.6、趟次 ≈ 328）。**不要求逐位相同**——目標式常數不同。

**若退化組反而優於完整 M4G，即為估值層無效的證據，必須誠實記入驗收文件，不得掩蓋。**

- [ ] **Step 3: 驗收 2（核心可證偽預測）**

完整 M4G 不含 `icLGcap`。判定條件：**pile-on ≥ 3.576**（M3G 在同一實例 seed 0 的值）。

通過 = 拆單可由估值支撐，人工供給上限確實是贅物。
不通過 = 拆單效益依賴供給節流，spec §D6 的假設被推翻——這同樣是有價值的結論，必須寫進文件並停下來與使用者討論，不得自行加回上限。

- [ ] **Step 4: 驗收 3（外生訂單流穩健性）**

另跑 Fill 模式對照（`small_o100_mu100_4h_inv70.xsett`，其餘相同），計算 pile-on 由 Fill 到 Fixed 的跌幅。

參照：M3G 跌 22%（4.58→3.58），HGS 幾乎不跌（4.56→4.42）。判定：M4G 的跌幅應顯著小於 22%。

- [ ] **Step 5: 求解時間檢查**

```bash
awk -F, 'NR>1 {a[n++]=$20} END {asort(a); print "median solve sec:", a[int(n/2)]}' out/acc_m4g_s0/m4g_decision_log.csv
```
判定：中位求解時間若超過 M3G 的 3 倍，用 `ValuationOrderLimit`（由大往小掃 200 / 100 / 50）找可接受點，並把該限制誠實記為模型的可擴展性邊界。

- [ ] **Step 6: 撰寫驗收文件**

建立 `docs/2026-07-31-m4g-acceptance.md`，內容包含：

1. 四方對照表（M1G / M3G / HGS / M4G），欄位：TP、件數、飢餓秒、pile-on、趟次、EOR、m/item、每 line 距離
2. 驗收 1/2/3 的逐項判定與數據
3. λ/μ/δ 的實際跑動值範圍（由決策日誌統計），特別是 **δ 的最終值**——這是 spec §7.1 指出的最可能失敗模式
4. 拆單邊際價值分布（Task 7 的探針資料）：平均值、中位數、為 0 的比例
5. 求解時間分布與是否啟用 `ValuationOrderLimit`
6. 消融劑量反應：λ/μ/δ 三軸各自對主要指標的影響；若三軸皆 inert，明確記為「瓶頸不在目標式」的乾淨負結果

- [ ] **Step 7: 提交**

```bash
git add run_m4g_accept.cmd docs/2026-07-31-m4g-acceptance.md
git commit -m "docs(m4g): acceptance results - 4-way comparison, ablation and split marginal value"
```

---

## 自審紀錄

**Spec 覆蓋檢查**：spec §3.2 全部變數 → Task 3/4；§3.3 目標式 T1–T5 → Task 4 Step 2；§3.4 約束 R1–R5/V1–V4/B1–B7 → Task 3 Step 3、Task 4 Step 2；§3.5 價格校準 → Task 2；§4 拆單邊際價值診斷 → Task 7；§5.1 檔案結構 → File Structure 表；§5.2 組態欄位 → Task 1 Step 1（逐欄對應）；§5.4 提交語意 → Task 5；§6 驗證計畫三項驗收 + 劑量反應 + 求解時間 → Task 6、Task 8；§7 風險 → Task 8 Step 6 的驗收文件逐項回報。

**已知偏離**：spec §5.1 規劃 `M4GPricing.cs` 為獨立檔案，本計畫遵守；spec 未指定符號命名，本計畫在 Task 3 Step 2 與 Task 4 開頭固定命名規則，供跨 Task 一致引用。

**型別一致性**：`M4GPricing.LineKey(int,int)` 於 Task 2 定義，Task 4（`ValuedLineKeys`/`BoundLineKeys` 填值）與 Task 5（`RegisterLineClosed`）皆使用同一靜態方法，不手寫字串串接。`SolveM4G` 於 Task 4 定義為單參數、Task 7 改為雙參數並同步更新呼叫點——Task 7 Step 1 已明確要求修改呼叫點。
