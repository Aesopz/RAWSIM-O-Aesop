# Set-level 派遣重設計 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 M2e-IC(tier)的派遣正當性從 per-pod 硬閘(icP1d/icSG)換成 set-level「完成 + 訂單級推進」目標 + pod 分層第 4 層 γ,治死鎖泛用而不收斂 M1G;全部由 `SoftInboundCommitted` 主開關閘控,關 = 現任 tier 逐位不變。

**Architecture:** 純加成、config-gated 改在 `SplitM2eICManager.cs` 與 `SplitM2eICConfiguration`(不開新 manager 檔、不動 M1GManager/HADGSManager)。推進項照現有 PR 層公式(`q·(-w/GetDemandCount())`)但**獨立 gated**,不啟用 `prMode`(避免帶進 true-completion 語意)。現任 committed tier(`e3d842f`)不動。

**Tech Stack:** C# 7.3 / net48;Gurobi via SolverWrappers;x64 Release;驗證 = build + CLI 模擬 + zero-drift diff(本專案無單元測試框架,驗收即 build+sim+diff)。

## Global Constraints

- **Build 只能 x64 Release**:`MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`(MSBuild 路徑 `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`)。
- **C# 7.3**:新檔案要手動加 `<Compile Include>`(本計畫不新增檔案,免);out 參數不能被 lambda 捕獲(本計畫不涉及)。
- **絕不動 `M1GManager.cs` / `HADGSManager.cs`**。
- **zero-drift 為硬驗收**:`SoftInboundCommitted` 關時,全部改動整段跳過(whole-block skip,**不靠係數歸零**——PK 非 binding 教訓);Fixed `orders1150` 的 kpi_report.csv / tripscompleted.csv 與現任 tier 逐位一致。
- **CLI 呼叫格式**:`RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe <xlayo> <xsett> <xconf> <outdir> <seed>`。
- **既有值(現任 tier)**:OrderRewardWeight=-40、IdleSlotWeight=1000、PodTripFixedCost=0、ParentClosingReward=0、CoverageRewardWeight=-0.2、QueuedPodDrawPenalty=1、OnTheWayPodDrawPenalty=3、PipelineFloorTarget=1。
- **現任 tier Fixed 基準(orders1150,對照用)**:orders 1048 / pile-on 1.888 / kJ_per_order 3.302 / trips 3525。**M1G Fixed**:959 / 1.228 / 4.605 / 4160。
- **測試檔案**:layout `Material\Instances\CoreBenchmark\small\small.xlayo`;Fixed `...\fixed_1150.xsett`(OrderMode=Fixed, orders1150.xorders, deterministic);Fill 死鎖場景 `...\small_o200_mu100_4h_inv50.xsett`、`...\small_o300_mu100_4h_inv50.xsett`(OrderCount 200/300)。

---

### Task 1: Config 欄位 + xconf 臂

**Files:**
- Modify: `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`(`SplitM2eICConfiguration`,class 起於 line 1143;在 `SplitCanDriveDispatch`(line ~1350)之後插入)
- Create: `Material/Instances/CoreBenchmark/small/split_milp_m2eic_setlevel.xconf`

**Interfaces:**
- Produces:`SplitM2eICConfiguration.SoftInboundCommitted`(bool)、`.ProgressRewardWeight`(double)、`.NewPodPartialPenalty`(double),Task 2/3 讀取。

- [ ] **Step 1: 加 3 個 config 欄位**

在 `MethodConfigurationsOB.cs` 的 `SplitCanDriveDispatch` 欄位宣告(line ~1350)**之後**加入:

```csharp
        /// <summary>(Set-level redesign, master switch) When true, the inbound-committed
        /// hard gates (icP1d / icSG1 / icSG2 constraints and the decode P1 assert) are
        /// NOT generated; dispatch is justified by the set-level "completion + order-level
        /// progress" objective instead, and new-pod partial draws are priced by
        /// NewPodPartialPenalty. Default false = current tier, bit-identical (whole gated
        /// blocks are skipped, not coefficient-zeroed). ProgressRewardWeight and
        /// NewPodPartialPenalty only take effect when this is true.</summary>
        public bool SoftInboundCommitted = false;

        /// <summary>(Set-level redesign) Order-level LINEAR progress reward magnitude
        /// (>=0). Each assigned unit of order o earns -ProgressRewardWeight / D_o where
        /// D_o = order's ORIGINAL total demand (GetDemandCount), so per-epoch slices sum to
        /// the reward over the order's life. Structurally mirrors the PR pro-rata term but
        /// is decoupled from prMode/true-completion. Only applied when SoftInboundCommitted
        /// is true. 0 = no term.</summary>
        public double ProgressRewardWeight = 0;

        /// <summary>(Set-level redesign) Pod-tier draw cost 4th tier (gamma): per-unit cost
        /// of drawing from a brand-new storage (Pa) pod, on top of the existing
        /// Queued/OnTheWay tiers. Should be >= OnTheWayPodDrawPenalty (a new pod is further
        /// from committed than an on-the-way one). Only applied when SoftInboundCommitted is
        /// true (else Pa pods keep the current OnTheWay penalty). 0 = no extra cost.</summary>
        public double NewPodPartialPenalty = 0;
```

- [ ] **Step 2: Build**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo`
Expected: build success, 0 errors。

- [ ] **Step 3: 建 xconf 臂(byte-clean,從現任 tier 生成)**

用 Python 從 `split_milp_m2eic_tier.xconf` 生成,插入 3 個新元素(class 宣告順序 = SplitCanDriveDispatch 之後,故 xconf 中放在 `SplitCanDriveDispatch` 對應位置;tier 無此元素,依 class 順序,3 元素接在 `CoverageRewardWeight` 附近之後——由反序列化驗證定位):

```bash
cd "C:/Users/Aesop/Desktop/EE-RAWSim-O_PP"
python - <<'PY'
raw=open("Material/Instances/CoreBenchmark/small/split_milp_m2eic_tier.xconf",'rb').read()
bom=raw[:3]==b'\xef\xbb\xbf'; txt=raw.decode('utf-8-sig')
txt=txt.replace('<Name>split_milp_m2eic_tier</Name>','<Name>split_milp_m2eic_setlevel</Name>',1)
# CoverageRewardWeight -> 0 (order-level progress replaces SKU-level coverage), and
# append the 3 new elements right after it (adjust position if XmlSerializer rejects).
anchor='    <CoverageRewardWeight>-0.2</CoverageRewardWeight>\r\n'
ins=('    <CoverageRewardWeight>0</CoverageRewardWeight>\r\n'
     '    <SoftInboundCommitted>true</SoftInboundCommitted>\r\n'
     '    <ProgressRewardWeight>2</ProgressRewardWeight>\r\n'
     '    <NewPodPartialPenalty>6</NewPodPartialPenalty>\r\n')
assert anchor in txt
txt=txt.replace(anchor, ins, 1)
open("Material/Instances/CoreBenchmark/small/split_milp_m2eic_setlevel.xconf",'wb').write((('﻿' if bom else '')+txt).encode('utf-8'))
print("created setlevel arm (ProgressRewardWeight=2, NewPodPartialPenalty=6 starting values)")
PY
```

- [ ] **Step 4: 驗證 xconf 反序列化(跑一次,不崩即通過)**

Run: `RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe Material\Instances\CoreBenchmark\small\small.xlayo Material\Instances\CoreBenchmark\small\fixed_1150.xsett Material\Instances\CoreBenchmark\small\split_milp_m2eic_setlevel.xconf output_t1_deser 0`
Expected: 正常結束、產生 `output_t1_deser/*/kpi_report.csv`(此時新旗標的 code 尚未接,行為 = tier + CoverageRewardWeight=0,仍應正常跑完;若 XmlSerializer 因元素順序拋錯,調整 Step 3 的插入位置到 class 宣告對應處再重跑)。

- [ ] **Step 5: Commit**

```bash
git add RAWSimO.Core/Configurations/MethodConfigurationsOB.cs Material/Instances/CoreBenchmark/small/split_milp_m2eic_setlevel.xconf
git commit -m "feat(setlevel): config fields (SoftInboundCommitted/ProgressRewardWeight/NewPodPartialPenalty) + xconf arm"
```

---

### Task 2: 目標式改動(訂單級推進項 + pod 分層第 4 層 γ)

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`(pod-tier 區塊 line ~732-752;推進項插在 PR 區塊 line ~773 之後)

**Interfaces:**
- Consumes:`_icConfig.SoftInboundCommitted / .ProgressRewardWeight / .NewPodPartialPenalty`(Task 1);既有 `Pa`(pod set)、`deVarNameq`、`variablesQ`、`v.order.GetDemandCount()`。

- [ ] **Step 1: pod 分層區塊加第 4 層 γ(gated)**

把 line ~732-752 的區塊改成(新增 `icNewPodPen`、`icSoftInbound`、Pa 分支):

```csharp
            double icQueuedPen = _icConfig != null ? _icConfig.QueuedPodDrawPenalty : 0;
            double icOnTheWayPen = _icConfig != null ? _icConfig.OnTheWayPodDrawPenalty : 0;
            // (Set-level redesign) 4th tier: a brand-new Pa pod serving a partial costs
            // NewPodPartialPenalty (gamma). Only when SoftInboundCommitted - else Pa pods
            // keep falling into icOnTheWayPen exactly as before (bit-identical; and P1
            // pins their q to 0 anyway when the gate is on).
            bool icSoftInbound = _icConfig != null && _icConfig.SoftInboundCommitted;
            double icNewPodPen = icSoftInbound ? _icConfig.NewPodPartialPenalty : 0;
            if ((icQueuedPen != 0 || icOnTheWayPen != 0 || icNewPodPen != 0) && deVarNameq.Count > 0)
            {
                var icTierTerms = new List<LinearExpression>();
                foreach (var v in deVarNameq)
                {
                    HashSet<int> icProcHere, icQueuedHere;
                    bool icIsProcessing = icPpByStation.TryGetValue(v.outputstation.ID, out icProcHere)
                        && icProcHere.Contains(v.pod.ID);
                    if (icIsProcessing)
                        continue;
                    bool icIsQueued = icQueuedByStation.TryGetValue(v.outputstation.ID, out icQueuedHere)
                        && icQueuedHere.Contains(v.pod.ID);
                    double icPen = (icSoftInbound && Pa.Contains(v.pod)) ? icNewPodPen
                        : (icIsQueued ? icQueuedPen : icOnTheWayPen);
                    if (icPen != 0)
                        icTierTerms.Add(variablesQ[v.name] * icPen);
                }
                if (icTierTerms.Count > 0)
                    objective = objective + LinearExpression.Sum(icTierTerms);
            }
```

- [ ] **Step 2: 加訂單級線性推進項(gated),插在 PR 區塊(line ~773)之後**

在 `if (prMode && prR != 0 && deVarNameq.Count > 0) ...` 那段**之後**加入:

```csharp
            // (Set-level redesign) order-level LINEAR progress reward: each assigned unit of
            // order o earns -ProgressRewardWeight / D_o (D_o = original demand GetDemandCount,
            // so per-epoch slices sum to the reward over the order's life). Mirrors the PR
            // term's formula but decoupled from prMode/true-completion, and gated by the
            // SoftInboundCommitted master switch => flag off is a whole-block skip
            // (bit-identical, not coefficient-zeroed).
            if (icSoftInbound && _icConfig.ProgressRewardWeight != 0 && deVarNameq.Count > 0)
                objective = objective + LinearExpression.Sum(deVarNameq.Select(v =>
                    variablesQ[v.name] * (-_icConfig.ProgressRewardWeight / v.order.GetDemandCount())));
```

(`icSoftInbound` 已於 Step 1 宣告於同一方法作用域內、在此行之前;若編譯報未宣告,確認 Step 1 的宣告在此使用點之前。)

- [ ] **Step 3: Build**

Run: `& "C:\Program Files\...\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo`
Expected: 成功、0 error。

- [ ] **Step 4: Zero-drift(flag 關 = 現任 tier 逐位一致)**

跑現任 tier(SoftInboundCommitted 未設 = false),對照本 Task 改動前後:

```bash
CLI="RAWSimO.CLI/bin/x64/Release/RAWSimO.CLI.exe"; SM="Material/Instances/CoreBenchmark/small"
"$CLI" "$SM/small.xlayo" "$SM/fixed_1150.xsett" "$SM/split_milp_m2eic_tier.xconf" output_t2_drift 0
# 對照現任 tier 的已知基準(1048/1.888/3.302/3525);並與改動前的 tier 輸出逐位 diff:
dnew=$(find output_t2_drift -maxdepth 1 -type d -name "1-*")
# (改動前先存一份 output_tier_pre 供 diff;若無,至少比對 KPI 四項與基準一致)
```

Expected: `kpi_report.csv` 四項 = 1048/1.888/3.302/3525(現任 tier);與改動前 tier 輸出 `diff -q` 逐位一致(pod-tier Pa 分支與推進項在 flag 關時整段不進)。

- [ ] **Step 5: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs
git commit -m "feat(setlevel): objective - order-level progress term + pod-tier 4th tier (gamma), gated"
```

---

### Task 3: 約束 + decode 閘控(移除 icP1d/icSG,跳過 decode P1 斷言)

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`(icP1d line ~1113-1133;icSG 區塊 line ~1141-1164;decode 斷言 line ~1709)

**Interfaces:**
- Consumes:`_icConfig.SoftInboundCommitted`(Task 1);既有 `icPaQ`、`icSgEnabled`、`icPaUnits`、`icClosingException`、`icSplitDriveException`。

- [ ] **Step 1: 閘控 icP1d(soft-inbound 時不生成)**

icP1d 在 `if (icPaQ.Count > 0) { ... AddConstr(..., "icP1d"); }`(line ~1113)。把外層條件加上 `&& !soft`:

```csharp
                if (icPaQ.Count > 0 && !(_icConfig != null && _icConfig.SoftInboundCommitted))
                {
```

(其餘 icP1d 內容不變;soft-inbound 時整個 Pa-draw 閘不生成 → 新車可服 partial,由 γ 定價。)

- [ ] **Step 2: 閘控 icSG1/icSG2(soft-inbound 時不生成)**

SG 區塊 `if (icSgEnabled) { ...icSG1...icSG2... }`(line ~1141)。改成:

```csharp
            if (icSgEnabled && !(_icConfig != null && _icConfig.SoftInboundCommitted))
            {
```

(其餘 SG 內容不變;soft-inbound 時 fresh partial 不受時機/處理中閘門限制,由分層代價定價。)

- [ ] **Step 3: 閘控 decode P1 斷言(line ~1709)**

把 throw 條件加上 `&& !soft`:

```csharp
                        if (M2eICMath.SplitOrderDrawsFromStorage(true, icPaUnits) && !icClosingException && !icSplitDriveException
                            && !(_icConfig != null && _icConfig.SoftInboundCommitted))
                            throw new InvalidOperationException("M2e-IC: split order " + order.ID
                                + " drew " + icPaUnits + " unit(s) from storage-area pods (P1 violated).");
```

- [ ] **Step 4: Build**

Run: MSBuild x64 Release(同上)。Expected: 成功、0 error。

- [ ] **Step 5: Zero-drift(flag 關)+ flag-on 冒煙(flag 開不崩)**

```bash
CLI="RAWSimO.CLI/bin/x64/Release/RAWSimO.CLI.exe"; SM="Material/Instances/CoreBenchmark/small"
# (a) zero-drift: tier flag-off 仍 = 1048/1.888/3.302/3525、逐位一致
"$CLI" "$SM/small.xlayo" "$SM/fixed_1150.xsett" "$SM/split_milp_m2eic_tier.xconf" output_t3_drift 0
# (b) flag-on 冒煙: setlevel 臂在 Fixed 跑完不崩(decode 斷言已跳過,Pa partial 合法)
"$CLI" "$SM/small.xlayo" "$SM/fixed_1150.xsett" "$SM/split_milp_m2eic_setlevel.xconf" output_t3_smoke 0
```

Expected:(a) tier 四項 = 基準、與改動前逐位一致;(b) setlevel 正常結束、產生 kpi、**無 P1-violated 例外**。

- [ ] **Step 6: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs
git commit -m "feat(setlevel): gate off icP1d/icSG1/icSG2 + decode P1 assert under SoftInboundCommitted"
```

---

### Task 4: 驗證(死鎖治癒 + 效率掃描,決定成敗)

**Files:**
- Create: 掃描 xconf 臂(從 setlevel 副本改 ProgressRewardWeight × NewPodPartialPenalty),output 目錄;無 code 改動。

**Interfaces:**
- Consumes:Task 1-3 的完整 setlevel 實作。

- [ ] **Step 1: 死鎖治癒(Fill o200/o300 跑滿)**

```bash
CLI="RAWSimO.CLI/bin/x64/Release/RAWSimO.CLI.exe"; SM="Material/Instances/CoreBenchmark/small"
"$CLI" "$SM/small.xlayo" "$SM/small_o200_mu100_4h_inv50.xsett" "$SM/split_milp_m2eic_setlevel.xconf" output_t4_o200 0
"$CLI" "$SM/small.xlayo" "$SM/small_o300_mu100_4h_inv50.xsett" "$SM/split_milp_m2eic_setlevel.xconf" output_t4_o300 0
```
Expected:兩者 `orderprogression.csv` 最後一列時間 > 13000s(跑滿 14400)——對照 tier 凍@5624/1981。若仍凍,先調高 ProgressRewardWeight(2→5)重試;仍凍 → 記錄、進 Step 4 判定。

- [ ] **Step 2: 效率掃描(Fixed orders1150,ε_prog × γ)**

生成 6 個臂(ProgressRewardWeight ∈ {0.5,2,5} × NewPodPartialPenalty ∈ {3,6,12} 中挑 3×2 或先跑對角),各跑 Fixed:

```bash
# 例:對每組 (p,g) 從 setlevel 複製、改兩個值、跑 Fixed;收 orders/pile-on/kJ_per_order/trips
# 對照:tier 1048/1.888/3.302/3525;M1G 959/1.228/4.605/4160
```
Expected:記錄每組四項 KPI。

- [ ] **Step 3: 反碎裂交叉檢查**

對每組讀 `splitorders.csv` 母單數 + `kpi_report.csv` pile-on,確認**無 θ=2 式崩盤**(pile-on 不應大幅低於 tier 的 1.888、母單數不應暴增到絞碎)。

- [ ] **Step 4: 三贏判定**

成功 = 存在一組 (ε_prog, γ) 同時滿足:(a) o200/o300 跑滿(死鎖治癒)、(b) Fixed pile-on ≥ tier 雜訊帶(≈1.888,不崩)、(c) Fixed 完勝 M1G(pile-on ≫ 1.228、能耗 ≪ 4.605)。
- **成功** → 記錄甜蜜點、更新 memory + strongest-model 文件、把該組設為新臂(**但不覆蓋現任 tier,除非使用者裁定**)。
- **失敗**(找不到三贏點)→ **回退 watchdog 路線、保現任 tier**;setlevel 臂保留為 documented 負結果 ablation。

- [ ] **Step 5: Commit(結果與結論)**

```bash
git add docs Material/Instances/CoreBenchmark/small/split_milp_m2eic_setlevel*.xconf
git commit -m "test(setlevel): deadlock-cure + Fixed efficiency sweep results + verdict"
```

---

## Self-Review

**Spec coverage**:§3.1 參數→Task 1;§3.2 目標式(推進項+γ)→Task 2;§3.3 約束移除 + §3.4 decode→Task 3;§6 驗證(zero-drift/死鎖/效率/反碎裂/三贏判定)→Task 2-4;§7 可回退→全程 gated + 不動 tier。無遺漏。

**Placeholder scan**:所有 code 步驟均為逐字程式碼;掃描值為具體集合;無 TBD/TODO。

**Type consistency**:`SoftInboundCommitted`(bool)、`ProgressRewardWeight`(double)、`NewPodPartialPenalty`(double)三處使用一致;`icSoftInbound` 於 Task 2 Step 1 宣告、Task 2 Step 2 使用(同方法作用域);`Pa`/`deVarNameq`/`GetDemandCount()` 皆既有。

**已知風險提醒(給執行者)**:Task 1 Step 4 若 XmlSerializer 因元素順序拋錯,將 3 新元素移到與 class 宣告順序一致的 xconf 位置。Task 2 `icSoftInbound` 宣告位置須在推進項使用點之前(同一 `Decide`/objective 組裝方法內)。
