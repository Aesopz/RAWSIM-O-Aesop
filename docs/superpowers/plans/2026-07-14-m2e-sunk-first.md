# M2e-SF Implementation Plan

**Goal:** 在 `SplitM1GExactManager` 加入 default-off 的 sunk-first completion-locked two-solve，使所有可完全由 inherited `Pb` pods 供給的母單先取得嚴格優先，再由既有 M2e objective 對剩餘需求開啟 `Pa` trips。

**Spec:** `docs/superpowers/specs/2026-07-14-m2e-sunk-first-design.md`

**Architecture:** 同一個 Gurobi model、同一組 `q/x/y/v/z/u`，新增 `h_o`。Solve 1 最大化 `(1 + total residual units) * sum(h) + sum(R_o*h_o)`；鎖住整數 `H*`、`I_H*`；Solve 2 恢復既有 objective。只有 Solve 2 被 decode/commit。Od 本輪只加 log。

**Environment:** C# 7.3 / .NET Framework 4.8 / Gurobi / Windows x64。

## Global constraints

- 不修改 `M1GManager.cs`、`HADGSManager.cs`、`PVGSManager.cs`、`SplitM1GLBManager.cs`。
- 保留目前未提交的 LB 工作樹修改；禁止 `git add -A`、reset、checkout。
- `SunkFirstScoring=false` 不建新變數、不增加 optimize call，行為必須 deterministic-equivalent。
- `CrossTime=false` 時 inert；與 `CoverageFirstScoring=true` 同時啟用時 fail fast。
- 主驗收設定只允許 `split_milp_m2ea.xconf` 加一個 sunk-first flag，不疊加 PR/CF/w4/w5/epsilon。
- Build 使用：
  `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:m /nologo`
- 測試專案需另建：
  `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimO.Tests\RAWSimO.Tests.csproj /p:Platform=x64 /p:Configuration=Release /v:m /nologo`
- 本輪不建立 commit，除非使用者另行要求。

## Task 1: 可測試數學 helper 與 config

Files:

- Create `RAWSimO.Core/Control/Defaults/OrderBatching/M2eSunkFirstMath.cs`
- Modify `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`
- Modify `RAWSimO.Core/RAWSimO.Core.csproj`

Steps:

1. 新增 `SunkFirstScoring=false`，置於 `SplitM1GExactConfiguration` 現有欄位最後。
2. helper 實作 dominance multiplier 與 full-residual supplied predicate，拒絕負需求。
3. 將 helper 納入 core csproj。
4. 建置 core/solution。

## Task 2: MILP two-solve 與 Od/SF log

File:

- Modify `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM1GExactManager.cs`

Steps:

1. `InitializeSplitExact` 回傳 `odCount/odFired`，不改 admission 行為。
2. `SolveSplitExact` 讀取 sunk mode；建立每單 `hsunkx_o`。
3. 對完整 `residuals[o]` 加 `SF1/SF1-OOS`，只允許 `Pb` q 支撐 `h_o`。
4. Solve 1 求 `H*`、`I_H*`，加入整數 equality locks。
5. Solve 2 走原 objective；SF/CF 同開 fail fast。
6. 只 decode final solution；驗證 final `H/I_H` 等於 locks。
7. decision log 尾端加 `odCount,odFired,sunkOrdersStar,sunkItemsStar,sunkUnitsFinal,newTripsFinal,solve1Sec,solve2Sec`。
8. Build；`git diff --check`。

## Task 3: Tests 與 xconf

Files:

- Create `RAWSimO.Tests/M2eSunkFirstMathTests.cs`
- Modify `RAWSimO.Tests/Program.cs`
- Modify `RAWSimO.Tests/RAWSimO.Tests.csproj`
- Create `Material/Instances/CoreBenchmark/small/split_milp_m2e_sunk.xconf`

Test cases:

1. multiplier 大於任何 item tie-break 總幅度。
2. 相同 H 時較大 completed-item count 勝出。
3. 缺一條 residual SKU 即不是 sunk-complete。
4. 多 pod 聚合可完整滿足。
5. config default false。
6. xconf 與 `split_milp_m2ea.xconf` 除 type-preserving flag 外逐行一致。

## Task 4: Verification gates

1. Solution + tests build，執行全部手寫 tests。
2. Flag-off seed 0 regression，與既有 aligned baseline 比較 deterministic KPI。
3. M1e flag-on smoke，確認 CrossTime inert。
4. M2e-SF seed 0，檢查 `sunkOrdersStar>0`、lock 零違反、log schema、TP/PO/IPO、residency/occupancy。
5. Od probe 報告 `odFired/decisions`；本輪不修改 Od。
6. 單 seed 機制成立後才啟動五 seed M2e-SF vs `pvgs_e.xconf`；若單 seed 不觸發，停止長跑並回到 SPEC。

## Acceptance

- Flag off deterministic-equivalent。
- Inventory、slot、parent ledger 無例外。
- `h_o=1` 的 final solution 不含 `Pa` draw。
- 五 seed paired means：M2e-SF 的 TP、KPI_PO、KPI_IPO 均不低於 PVGS-E。
- solve-time median/p95 原則上不高於 baseline 2.5 倍；超過則保留功能但不得宣稱 scalable online。

