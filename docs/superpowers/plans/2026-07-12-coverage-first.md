# Coverage-First Objective (M2e-CF / PVGS-CF) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a lexicographic coverage-first objective mode: Solve 1 picks the pod set maximizing (completions + β·whole-backlog pool coverage − ε_S·new trips) with NO distance; Solve 2 assigns pod→station→bot minimizing travel within the locked L1 optimum. PVGS-E mirrors the same objective greedily.

**Architecture:** Flag-guarded branches in `SplitM1GExactManager.cs` (two-solve inside `SolveSplitExact`, constraints untouched) and `PVGSManager.cs` (dispatch-score branch). Three shared config fields on `SplitM1GExactConfiguration`. Defaults bit-identical. Spec: `docs/superpowers/specs/2026-07-12-coverage-first-objective-design.md`.

**Tech Stack:** C# 7.3 / net48, Gurobi via RAWSimO.SolverWrappers (`SetObjective`+`Optimize` re-solve on one model; `VariableType.Continuous`, `OptimizationSense.Maximize` both available), hand-rolled TestRunner.

## Global Constraints

- **Build:** `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release` (plain `msbuild` on PATH is v4.0 and false-fails on C# 7.3; x86 fails at runtime — Gurobi is win64-only).
- **C# 7.3 / net48.** CRLF in edited .cs files. New files need manual `<Compile Include>` in their csproj.
- **NEVER modify** `M1GManager.cs`, `HADGSManager.cs`, or any existing xconf.
- **Bit-identical defaults.** Regression baselines (small, seed 0, 7200s, `StatOverallOrdersHandled`): `split_milp_m2e.xconf` → **643**; `sweep/sw_0_0.xconf` → **657**; `pvgs_e.xconf` → **658**; `pvgs_m2e.xconf` → **648**.
- **Run command:** `RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett <xconf> <outdir> <seed>` from repo root. M2e-class runs ~2-3 min; PVGS runs ~10s.
- **Tests:** `RAWSimO.Tests\bin\x64\Release\RAWSimO.Tests.exe`. Current baseline `45/45 passed`; this plan adds 4 → final `49/49 passed`.
- Commit after every task with the given message.

---

### Task 1: Config fields

**Files:**
- Modify: `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs` (end of `SplitM1GExactConfiguration`, directly after `UnitDrawReward`)

**Interfaces:**
- Produces: `CoverageFirstScoring` (bool, false), `PoolCoverWeight` (double, 0.005), `PodSelectTiebreakCost` (double, 0.01) — read by Tasks 2 and 3 (PVGSConfiguration inherits them).

- [ ] **Step 1: Add the three fields**

Find:

```csharp
        public double UnitDrawReward = 0;
    }
```

Replace with:

```csharp
        public double UnitDrawReward = 0;
        /// <summary>
        /// Coverage-first lexicographic objective (spec: docs/superpowers/specs/
        /// 2026-07-12-coverage-first-objective-design.md). Solve 1 maximizes
        /// completions + PoolCoverWeight * whole-backlog pool coverage
        /// - PodSelectTiebreakCost * new pod trips, with NO distance term; Solve 2
        /// minimizes travel within the locked Solve-1 optimum (pod->station->bot
        /// assignment and split shapes emerge from distance there). PVGS-E mirrors the
        /// same objective greedily when this flag is set. Default false = bit-identical
        /// single-solve legacy objective.
        /// </summary>
        public bool CoverageFirstScoring = false;
        /// <summary>
        /// Beta: weight per unit of whole-backlog pool coverage in Solve 1. Calibrate
        /// beta * max-per-SKU-pool-demand &lt; 1 so pool coverage never outbids one
        /// completed order. Only read when CoverageFirstScoring is true.
        /// </summary>
        public double PoolCoverWeight = 0.005;
        /// <summary>
        /// Epsilon_S: fixed charge per NEW pod trip in Solve 1 (pod-visit minimization
        /// layer, below beta in the hierarchy). Only read when CoverageFirstScoring is
        /// true.
        /// </summary>
        public double PodSelectTiebreakCost = 0.01;
    }
```

- [ ] **Step 2: Build** — expected `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add RAWSimO.Core/Configurations/MethodConfigurationsOB.cs
git commit -m "feat(config): CoverageFirstScoring + PoolCoverWeight + PodSelectTiebreakCost (CF objective)"
```

---

### Task 2: M2e-CF two-solve in `SolveSplitExact`

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs` — two insertion sites: (a) after the weight reads (~:349, after the `eps`/w5 reads region — anchor given below), (b) around `wrapper.Update(); ... wrapper.Optimize();` (~:490-492).

**Interfaces:**
- Consumes: Task 1 fields via `_splitConfig`.
- Produces: the CF two-solve behavior. No signature changes; no constraint changes.

- [ ] **Step 1: Read CF config + build pool/coverage machinery**

Find (the epsilon read added by the PVGS-E plan, directly before `wrapper.SetObjective(objective, OptimizationSense.Minimize);`):

```csharp
            double eps = _splitConfig != null ? _splitConfig.UnitDrawReward : 0;
            if (eps != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name])) * eps;
            wrapper.SetObjective(objective, OptimizationSense.Minimize);
```

Replace with:

```csharp
            double eps = _splitConfig != null ? _splitConfig.UnitDrawReward : 0;
            if (eps != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name])) * eps;
            // (CF) coverage-first lexicographic mode: build the Solve-1 objective
            // (completions + beta * whole-backlog pool coverage - epsS * new trips, NO
            // distance) and its pool-coverage variables. The demand pool deliberately
            // uses the FIRST-stage admission over the full backlog (Od shrink does not
            // apply - the pool is about supply value, not slot eligibility). Constraints
            // of the model are untouched; c_i is capped by pool demand and by the
            // selected pods' stock, so it measures what the CHOSEN SET can cover.
            bool coverageFirst = _splitConfig != null && _splitConfig.CoverageFirstScoring;
            double betaPool = _splitConfig != null ? _splitConfig.PoolCoverWeight : 0;
            double epsPod = _splitConfig != null ? _splitConfig.PodSelectTiebreakCost : 0;
            VariableCollection<string> variablesPool = null;
            LinearExpression coverageObjective = null;
            if (coverageFirst && deVarNamez.Count > 0)
            {
                bool cfCrossTime = _splitConfig != null && _splitConfig.CrossTime;
                Dictionary<ItemDescription, int> poolDemand = new Dictionary<ItemDescription, int>();
                foreach (var order in _pendingOrders)
                {
                    if (cfCrossTime
                        ? !order.RemainingPositions.Any(p => Instance.StockInfo.GetActualStock(p.Key) >= 1)
                        : !order.RemainingPositions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value))
                        continue;
                    foreach (var pos in order.RemainingPositions)
                    {
                        int cur;
                        poolDemand[pos.Key] = (poolDemand.TryGetValue(pos.Key, out cur) ? cur : 0) + pos.Value;
                    }
                }
                List<ItemDescription> poolSkus = poolDemand.Keys.Where(k => PiSKU.ContainsKey(k)).ToList();
                variablesPool = new VariableCollection<string>(wrapper, VariableType.Continuous, 0, double.PositiveInfinity, (string s) => { return s; });
                foreach (var sku in poolSkus)
                {
                    string cname = "cpool_" + sku.ID.ToString();
                    wrapper.AddConstr(variablesPool[cname] <= poolDemand[sku], "cfcap");
                    var supplyTerms = deVarNamexps.Where(v => v.pod.CountAvailable(sku) > 0)
                        .Select(v => variablesBinary[v.name] * (double)v.pod.CountAvailable(sku)).ToList();
                    if (supplyTerms.Count > 0)
                        wrapper.AddConstr(variablesPool[cname] <= LinearExpression.Sum(supplyTerms), "cfsup");
                    else
                        wrapper.AddConstr(variablesPool[cname] <= 0, "cfsup");
                }
                coverageObjective = LinearExpression.Sum(deVarNamez.Select(v => variablesBinary[v.name]));
                if (poolSkus.Count > 0)
                    coverageObjective = coverageObjective
                        + LinearExpression.Sum(poolSkus.Select(k => variablesPool["cpool_" + k.ID.ToString()])) * betaPool;
                var cfNewTrips = deVarNamexps.Where(v => Pa.Contains(v.pod)).Select(v => variablesBinary[v.name]).ToList();
                if (cfNewTrips.Count > 0)
                    coverageObjective = coverageObjective - LinearExpression.Sum(cfNewTrips) * epsPod;
            }
            wrapper.SetObjective(objective, OptimizationSense.Minimize);
```

- [ ] **Step 2: Two-solve around the optimize call**

Find:

```csharp
            wrapper.Update();
            DateTime _optStart = DateTime.Now;
            wrapper.Optimize();
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;
```

Replace with:

```csharp
            wrapper.Update();
            DateTime _optStart = DateTime.Now;
            // (CF) Solve 1: coverage layer without distance. Lock its optimum (combined
            // value, 1e-6 relative tolerance) plus the new-trip count, then hand the
            // model to the legacy objective as Solve 2 - distance now only arbitrates
            // WITHIN the L1-optimal set (pod->station->bot assignment, split shapes).
            if (coverageFirst && coverageObjective != null)
            {
                wrapper.SetObjective(coverageObjective, OptimizationSense.Maximize);
                wrapper.Update();
                wrapper.Optimize();
                if (wrapper.HasSolution())
                {
                    double coverageStar = wrapper.GetObjectiveValue();
                    int tripsStar = deVarNamexps.Count(v => Pa.Contains(v.pod) && Math.Round(variablesBinary[v.name].GetValue()) != 0);
                    wrapper.AddConstr(coverageObjective >= coverageStar - 1e-6 * Math.Max(1.0, Math.Abs(coverageStar)), "cflockobj");
                    var lockTrips = deVarNamexps.Where(v => Pa.Contains(v.pod)).Select(v => variablesBinary[v.name]).ToList();
                    if (lockTrips.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(lockTrips) <= tripsStar, "cflocktrips");
                    wrapper.SetObjective(objective, OptimizationSense.Minimize);
                    wrapper.Update();
                }
            }
            wrapper.Optimize();
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;
```

(Solve 2 reuses the legacy `objective` — its w2·Σz term can only add completions beyond the locked floor within the trip cap, never trade them away; `_optSec` spans both solves so the decision log reports total time.)

- [ ] **Step 3: Build** — expected `0 Error(s)`.

- [ ] **Step 4: Regressions — flag off must be bit-identical (both anchors)**

```
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett Material\Instances\CoreBenchmark\small\split_milp_m2e.xconf output_regr_cf_m2e 0
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\small_o100_mu100.xsett Material\Instances\CoreBenchmark\small\sweep\sw_0_0.xconf output_regr_cf_m2ea 0
```

Expected: `StatOverallOrdersHandled: 643` and `StatOverallOrdersHandled: 657` exactly (grep each `output_regr_cf_*/*/statistics.txt`).

- [ ] **Step 5: CF smoke (behavior must CHANGE with the flag)**

Create a scratch xconf: copy `sweep/sw_0_0.xconf`, rename Name to `cf_smoke`, insert after `<IdleSlotWeight>0</IdleSlotWeight>`:

```xml
    <CoverageFirstScoring>true</CoverageFirstScoring>
```

Run it at seed 0 into `output_smoke_cf`. Expected: completes without crash, `StatOverallOrdersHandled` in 500-700 AND different from 657 (if identical to 657, the flag did not take effect — check XML element order and STOP). Record TP/PO/IPO/RD/EOR in the report. Delete or keep the scratch file out of git (do not commit it).

- [ ] **Step 6: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs
git commit -m "feat(m2e-cf): coverage-first lexicographic two-solve (pool coverage layer + travel layer)"
```

---

### Task 3: PVGS-CF mirror (dispatch score + pool ledger) with TDD helpers

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/PvgsExactAligned.cs` (two new pure statics)
- Modify: `RAWSimO.Tests/PvgsExactAlignedTests.cs` (4 new tests)
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/PVGSManager.cs` (epoch pool ledger + DispatchLoop CF branch)

**Interfaces:**
- Consumes: Task 1 fields; `PvgsValueIndex.ComputeValue(availDict, residTotals, supplyTotals, beta)` (existing).
- Produces: `PvgsExactAligned.CoverageFirstPrimary(int, double, double, double) -> double` and `PvgsExactAligned.CoverageFirstBetter(double, double, double, double) -> bool`.

- [ ] **Step 1: Write the 4 failing tests**

In `RAWSimO.Tests/PvgsExactAlignedTests.cs`, add inside `Register()` before the closing brace:

```csharp
            TestRunner.Add("ECF_Primary_CompletionsDominatePool", () =>
            {
                // 1 completion vs huge pool gain at beta=0.005: 1 + 0.005*100 - 0.01 = 1.49
                AssertClose(1.49, PvgsExactAligned.CoverageFirstPrimary(1, 100, 0.005, 0.01), "1 + 0.5 - 0.01");
            });
            TestRunner.Add("ECF_Primary_PurePoolTrip", () =>
            {
                // 0 completions, pool gain 10 at beta=0.005 vs epsS=0.01: 0.05 - 0.01 = 0.04 > 0 -> supply trip allowed
                AssertClose(0.04, PvgsExactAligned.CoverageFirstPrimary(0, 10, 0.005, 0.01), "pool-only trip clears epsS");
            });
            TestRunner.Add("ECF_Better_PrimaryWinsOverDistance", () =>
            {
                // higher primary wins even at much worse distance
                TestRunner.AssertTrue(PvgsExactAligned.CoverageFirstBetter(2.0, 100.0, 1.0, 5.0), "primary dominates");
                TestRunner.AssertTrue(!PvgsExactAligned.CoverageFirstBetter(1.0, 5.0, 2.0, 100.0), "reverse");
            });
            TestRunner.Add("ECF_Better_DistanceBreaksTies", () =>
            {
                TestRunner.AssertTrue(PvgsExactAligned.CoverageFirstBetter(1.0, 5.0, 1.0, 10.0), "tie -> nearer wins");
                TestRunner.AssertTrue(!PvgsExactAligned.CoverageFirstBetter(1.0, 10.0, 1.0, 5.0), "tie -> farther loses");
            });
```

- [ ] **Step 2: Build — expected compile error** (methods missing) = red state.

- [ ] **Step 3: Implement the two pure statics**

In `PvgsExactAligned.cs`, add before the class's closing brace:

```csharp
        /// <summary>
        /// Coverage-first PRIMARY key of a dispatch candidate (the greedy mirror of the
        /// exact model's Solve-1 objective): newCompletions + poolCoverWeight *
        /// poolCoverGain - podSelectTiebreakCost (one new trip per dispatch). Distance
        /// is NOT part of the primary - it only breaks ties (CoverageFirstBetter).
        /// </summary>
        public static double CoverageFirstPrimary(int newCompletions, double poolCoverGain, double poolCoverWeight, double podSelectTiebreakCost)
        {
            return newCompletions + poolCoverWeight * poolCoverGain - podSelectTiebreakCost;
        }

        /// <summary>
        /// Lexicographic comparison: candidate A beats B iff its primary is strictly
        /// higher (1e-9 tolerance), or primaries tie and A is nearer.
        /// </summary>
        public static bool CoverageFirstBetter(double primaryA, double distanceA, double primaryB, double distanceB)
        {
            if (primaryA > primaryB + 1e-9)
                return true;
            return Math.Abs(primaryA - primaryB) <= 1e-9 && distanceA < distanceB - 1e-9;
        }
```

- [ ] **Step 4: Build + run tests** — expected `49/49 passed`.

- [ ] **Step 5: Epoch pool ledger in PVGSManager**

(a) In the `PvgsEpochState` class, after `public Dictionary<ItemDescription, int> SupplyTotals;` add:

```csharp
            public Dictionary<ItemDescription, int> PoolTotals; // CF mode only: whole-backlog demand pool (first-stage admission, no Od shrink)
```

(b) In `BuildEpochState`, after the `st.SupplyTotals` block (ends with the `st.SupplyTotals[e.Key] = ...` foreach) and before `st.ScanOrder = ...`, add:

```csharp
            if (_config != null && _config.ExactAlignedScoring && _config.CoverageFirstScoring)
            {
                st.PoolTotals = new Dictionary<ItemDescription, int>();
                bool cfCrossTime = _config.CrossTime;
                foreach (var order in _pendingOrders)
                {
                    if (cfCrossTime
                        ? !order.RemainingPositions.Any(p => Instance.StockInfo.GetActualStock(p.Key) >= 1)
                        : !order.RemainingPositions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value))
                        continue;
                    foreach (var pos in order.RemainingPositions)
                    {
                        int cur;
                        st.PoolTotals[pos.Key] = (st.PoolTotals.TryGetValue(pos.Key, out cur) ? cur : 0) + pos.Value;
                    }
                }
            }
```

(c) In `CommitParts`, directly after the existing `ResidualTotals` decrement block:

```csharp
                        int curTotal;
                        if (st.ResidualTotals.TryGetValue(pos.Key, out curTotal))
                            st.ResidualTotals[pos.Key] = Math.Max(0, curTotal - take);
```

extend it to:

```csharp
                        int curTotal;
                        if (st.ResidualTotals.TryGetValue(pos.Key, out curTotal))
                            st.ResidualTotals[pos.Key] = Math.Max(0, curTotal - take);
                        int curPool;
                        if (st.PoolTotals != null && st.PoolTotals.TryGetValue(pos.Key, out curPool))
                            st.PoolTotals[pos.Key] = Math.Max(0, curPool - take);
```

- [ ] **Step 6: DispatchLoop CF branch**

(a) After the weight/flag reads added by the PVGS-E plan (`bool exactAligned = ...; double podTripFixedCost = ...; double unitDrawReward = ...;`), add:

```csharp
                bool coverageFirst = exactAligned && _config != null && _config.CoverageFirstScoring;
                double betaPool = _config != null ? _config.PoolCoverWeight : 0;
                double epsPod = _config != null ? _config.PodSelectTiebreakCost : 0;
```

(b) The selection state: find `double bestScore = 0;` and replace with:

```csharp
                double bestScore = 0;
                double bestPrimary = 0;
                double bestDist = double.PositiveInfinity;
```

(c) The score branch: find the block added by the PVGS-E plan:

```csharp
                        double score = exactAligned
                            ? PvgsExactAligned.Score(newCompletions, newUnits, dBot, dPod,
                                completionWeight, distanceWeight, podTripFixedCost, unitDrawReward)
                            : completionWeight * newCompletions
                                + (crossTime ? partialUnitWeight * cand.Value + parentClosingBonus * parentCloses : 0.0)
                                - distanceWeight * (dBot + dPod);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestPod = cand.Pod;
                            bestStation = s;
                            bestBot = bot;
                        }
```

Replace with:

```csharp
                        if (coverageFirst)
                        {
                            // CF mirror: primary = greedy Solve-1 marginal (completions +
                            // beta * pool-cover gain - epsS), distance ONLY breaks ties.
                            double coverGain = st.PoolTotals != null
                                ? PvgsValueIndex.ComputeValue(st.Avail[cand.Pod], st.PoolTotals, st.SupplyTotals, 0.0)
                                : 0.0;
                            double primary = PvgsExactAligned.CoverageFirstPrimary(newCompletions, coverGain, betaPool, epsPod);
                            double dist = dBot + dPod;
                            if (primary > 1e-9 && (bestPod == null || PvgsExactAligned.CoverageFirstBetter(primary, dist, bestPrimary, bestDist)))
                            {
                                bestPrimary = primary;
                                bestDist = dist;
                                bestPod = cand.Pod;
                                bestStation = s;
                                bestBot = bot;
                            }
                        }
                        else
                        {
                            double score = exactAligned
                                ? PvgsExactAligned.Score(newCompletions, newUnits, dBot, dPod,
                                    completionWeight, distanceWeight, podTripFixedCost, unitDrawReward)
                                : completionWeight * newCompletions
                                    + (crossTime ? partialUnitWeight * cand.Value + parentClosingBonus * parentCloses : 0.0)
                                    - distanceWeight * (dBot + dPod);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestPod = cand.Pod;
                                bestStation = s;
                                bestBot = bot;
                            }
                        }
```

(Note: `coverGain` does not depend on the station `s`, so it is recomputed redundantly per station — acceptable at small scale; do NOT restructure the loops.)

- [ ] **Step 7: Build** — expected `0 Error(s)`; run tests — `49/49 passed`.

- [ ] **Step 8: Regressions — CF off must be bit-identical**

```
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe ... Material\Instances\CoreBenchmark\small\pvgs_e.xconf output_regr_cf_pvgse 0
RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe ... Material\Instances\CoreBenchmark\small\pvgs_m2e.xconf output_regr_cf_pvgs 0
```

(full command as in Global Constraints). Expected: `658` and `648` exactly.

- [ ] **Step 9: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/PvgsExactAligned.cs RAWSimO.Core/Control/Defaults/OrderBatching/PVGSManager.cs RAWSimO.Tests/PvgsExactAlignedTests.cs
git commit -m "feat(pvgs-cf): coverage-first dispatch scoring (lexicographic primary + distance tiebreak, pool ledger)"
```

---

### Task 4: CF xconfs + parse probes + smokes

**Files:**
- Create: `Material/Instances/CoreBenchmark/small/sweep/cf_b005_e01.xconf`, `cf_b002_e01.xconf`, `cf_b01_e01.xconf`, `cf_b005_e05.xconf` (M2e-CF grid: β∈{0.002,0.005,0.01}×ε_S=0.01 plus β=0.005×ε_S=0.05)
- Create: `Material/Instances/CoreBenchmark/small/pvgs_cf.xconf` (PVGS-CF at the default β/ε_S)

**XML element-order rule:** the three new fields are declared AFTER `UnitDrawReward` in `SplitM1GExactConfiguration` — in xconf they must appear after `IdleSlotWeight` (and after `UnitDrawReward` if present) and BEFORE any `PVGSConfiguration` field (`MinPartialUnits`, `ExactAlignedScoring`). XmlSerializer silently drops out-of-order elements; the behavioral probe in Step 3 is the safety net.

- [ ] **Step 1: Create the files (bash, from repo root)**

```bash
cd Material/Instances/CoreBenchmark/small
mk() { sed -e "s|<Name>split_milp_m2ea</Name>|<Name>$1</Name>|" \
          -e "s|<IdleSlotWeight>0</IdleSlotWeight>|<IdleSlotWeight>0</IdleSlotWeight>\n    <CoverageFirstScoring>true</CoverageFirstScoring>\n    <PoolCoverWeight>$2</PoolCoverWeight>\n    <PodSelectTiebreakCost>$3</PodSelectTiebreakCost>|" \
          sweep/sw_0_0.xconf > "sweep/$1.xconf"; }
mk cf_b002_e01 0.002 0.01
mk cf_b005_e01 0.005 0.01
mk cf_b01_e01  0.01  0.01
mk cf_b005_e05 0.005 0.05
sed -e 's|<Name>pvgs_e</Name>|<Name>pvgs_cf</Name>|' \
    -e 's|<MinPartialUnits>6</MinPartialUnits>|<CoverageFirstScoring>true</CoverageFirstScoring>\n    <PoolCoverWeight>0.005</PoolCoverWeight>\n    <PodSelectTiebreakCost>0.01</PodSelectTiebreakCost>\n    <MinPartialUnits>6</MinPartialUnits>|' \
    pvgs_e.xconf > pvgs_cf.xconf
grep -E "CoverageFirst|PoolCover|PodSelect|<Name>" sweep/cf_*.xconf pvgs_cf.xconf
```

Verify the grep shows all four fields per file with correct values and Names.

- [ ] **Step 2: Byte-rule check**

```bash
diff <(grep -v -E "CoverageFirst|PoolCover|PodSelect|<Name>cf_b005_e01</Name>" sweep/cf_b005_e01.xconf) <(grep -v "<Name>split_milp_m2ea</Name>" sweep/sw_0_0.xconf) && echo CF_OK
diff <(grep -v -E "CoverageFirst|PoolCover|PodSelect|<Name>pvgs" pvgs_cf.xconf) <(grep -v "<Name>pvgs" pvgs_e.xconf) && echo PVGSCF_OK
```

Expected: `CF_OK` and `PVGSCF_OK`.

- [ ] **Step 3: Smokes + behavioral parse probes (seed 0)**

Run `sweep/cf_b005_e01.xconf` (→ `output_smoke_cf_m2e`, ~3 min) and `pvgs_cf.xconf` (→ `output_smoke_cf_pvgs`, ~10s) with the standard command. Gates:
- Both complete; `StatOverallOrdersHandled` in 500-700.
- `output_smoke_cf_m2e` handled ≠ 657 (sw_0_0 baseline) — proves the CF elements parsed;
- `output_smoke_cf_pvgs` handled ≠ 658 (pvgs_e baseline) — same proof on the PVGS side. If either equals its baseline exactly, element order is wrong: fix and re-run.
- Record TP/PO/IPO/RD/OD/EOR of both smokes in the report.

- [ ] **Step 4: Commit**

```bash
git add Material/Instances/CoreBenchmark/small/sweep/cf_b002_e01.xconf Material/Instances/CoreBenchmark/small/sweep/cf_b005_e01.xconf Material/Instances/CoreBenchmark/small/sweep/cf_b01_e01.xconf Material/Instances/CoreBenchmark/small/sweep/cf_b005_e05.xconf Material/Instances/CoreBenchmark/small/pvgs_cf.xconf
git commit -m "feat(cf): coverage-first sweep xconfs (beta x epsS grid) + pvgs_cf"
```

---

### Task 5 (controller-run): β×ε_S sweep → operating point

Seeds 0/1, M2e-CF four grid points + PVGS-CF (same grid via scratch copies if needed). Selection rule: prefer the point where M2e-CF's TP ≥ 650-ish at seed 0 with PO ≥ 3 and RD ≤ 15k (efficiency league of PVGS-E); check PVGS-CF at the same point lands BELOW M2e-CF. If no grid point qualifies, widen the grid before touching code. Record everything in the ledger.

### Task 6 (controller-run): three-arm acceptance (user's revised criteria)

`run_cf_accept.cmd`: M1G reuse (`output_acc_m1g_s*` already on disk), M2e-CF at the chosen point seeds 0-4, PVGS-CF same point seeds 0-4. Judge:
1. M2e-CF ≥ M1G (six metrics);
2. PVGS-CF worse than M2e-CF on all five (TP/PO/RD/OD/EOR), each gap ≤ ~5%;
3. Internal sanity: M2e-CF vs old M2e(w4=0): TP loss ≤ ~2%, efficiency substantially better;
4. Two-solve decisionSec median recorded.
Report → `docs/2026-07-12-cf-acceptance.md`, ledger, commit.

## Self-Review Notes

- Spec coverage: §1 Solve1/Solve2 → Task 2; §2 PVGS mirror → Task 3; §3 config → Task 1; §4 acceptance → Tasks 5/6 (controller); travel-budget valve (§1 選配) deliberately NOT implemented (small-only scope, spec marks it optional).
- Type consistency: `CoverageFirstPrimary/Better` signatures match between Task 3 Steps 1/3/6; `PvgsValueIndex.ComputeValue(avail, residTotals, supplyTotals, beta)` argument order matches the existing call at PVGSManager.cs:463.
- Flag-off purity: every new block in Tasks 2/3 is gated on `CoverageFirstScoring` (directly or via `coverageFirst`); the only ungated additions are dead data (PoolTotals null when off; pool decrement guarded by null check).
