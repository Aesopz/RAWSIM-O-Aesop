# RL 動態派車上限(T)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 tier(M2e-IC MILP)之上加一個 tabular Q-learning meta-controller,依內部狀態(飢餓+backlog)每 N 秒動態調 `PipelineFloorTarget`(T),reward = Δitems − λ·Δ能耗;學「何時值得多派貨架」,驗證動態 T 是否贏最佳固定 T。

**Architecture:** 分層混合——上層 `TMetaController`(慢節奏、每 N 秒)只**改寫** `SplitM2eICConfiguration.PipelineFloorTarget`;下層 tier decode 每輪照讀該欄位、**一行不改**。全部 config-gated(`RLTMetaEnabled` 預設 false = bit-identical)。

**Tech Stack:** C# 7.3 / net48;無單元測試框架 → 驗證 = build + 模擬跑 + zero-drift 逐位比對(沿用本專案一貫做法)。RL = 內建 tabular Q-learning(36 格 Q-table)。

## Global Constraints

- **Build**:`MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`(msbuild 路徑:`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`)。x86 會 runtime 失敗(Gurobi 只有 win64)。
- **C# 7.3**:新檔要手動加 `<Compile Include>` 到 `RAWSimO.Core.csproj`,不會自動抓。out 參數不能被 lambda 捕獲。
- **絕不動** `M1GManager.cs` / `HADGSManager.cs`。
- **絕不動** `SplitM2eICManager.cs` 的 decode / DispatchLoop / 約束式邏輯。只准透過改寫 `_icConfig.PipelineFloorTarget` 影響 T。
- **可回退硬需求**:一切在分支 `rl-dynamic-t` 上做;RL 邏輯全放**新檔** `RAWSimO.Core/Control/TMetaController.cs`;由 `RLTMetaEnabled`(預設 false)gate。`git checkout 6/24` 即回現狀 tier。
- **zero-drift 驗收**:`RLTMetaEnabled=false` 時,跑既有 tier 場景必須與改動前**逐位一致**(kpi_report.csv 逐 byte 相同)。
- 分支:`rl-dynamic-t`。長跑批次用 detached `Start-Process` + Monitor,勿前景阻塞。

---

### Task 1: RL config 欄位(gated、預設 off)

**Files:**
- Modify: `RAWSimO.Core/Configurations/SettingConfiguration.cs`(在既有欄位區尾端加,約 line 205 `BackfillProbeEnabled` 附近)

**Interfaces:**
- Produces:`SettingConfiguration.RLTMetaEnabled`(bool)、`RLTMetaTrain`(bool)、`RLTMetaQTablePath`(string)、`RLTMetaIntervalSec`(double)、`RLTMetaLambda`(double)、`RLTMetaAlpha`(double)、`RLTMetaGamma`(double)、`RLTMetaEpsilon`(double)。後續 Task 2/3 讀這些。

- [ ] **Step 1: 加欄位**

在 `SettingConfiguration.cs`,`public bool BackfillProbeEnabled = false;` 這行之後加:

```csharp
        /// <summary>(RL-T) Master switch for the RL dispatch-cap meta-controller. Default
        /// false = static PipelineFloorTarget, bit-identical to current tier.</summary>
        public bool RLTMetaEnabled = false;
        /// <summary>(RL-T) true = training (eps-greedy + Q-update + SaveQ at finish);
        /// false = deploy (greedy, read-only Q).</summary>
        public bool RLTMetaTrain = false;
        /// <summary>(RL-T) Path to the Q-table file (loaded at start if it exists; saved at
        /// finish when training).</summary>
        public string RLTMetaQTablePath = "qtable.txt";
        /// <summary>(RL-T) Meta-control interval in seconds; T is (re)set every this many sec.</summary>
        public double RLTMetaIntervalSec = 120.0;
        /// <summary>(RL-T) Reward = dItems - lambda * dEnergyJ.</summary>
        public double RLTMetaLambda = 0.01;
        /// <summary>(RL-T) Q-learning rate.</summary>
        public double RLTMetaAlpha = 0.2;
        /// <summary>(RL-T) Q-learning discount.</summary>
        public double RLTMetaGamma = 0.95;
        /// <summary>(RL-T) eps-greedy exploration prob (training only).</summary>
        public double RLTMetaEpsilon = 0.2;
```

- [ ] **Step 2: Build**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo`
Expected: `RAWSimO.Core -> ...RAWSimO.Core.dll`,無 error。

- [ ] **Step 3: Commit**

```bash
git add RAWSimO.Core/Configurations/SettingConfiguration.cs
git commit -m "feat(rl-t): SettingConfiguration fields for RL dispatch-cap meta-controller (gated, default off)"
```

---

### Task 2: `TMetaController` 類別(全邏輯,無 sim 掛載)

**Files:**
- Create: `RAWSimO.Core/Control/TMetaController.cs`
- Modify: `RAWSimO.Core/RAWSimO.Core.csproj`(加 `<Compile Include="Control\TMetaController.cs" />`)

**Interfaces:**
- Consumes:`SettingConfiguration.RLTMeta*`(Task 1);`Instance.StatOverallItemsHandled`(int)、`Instance.StatOverallEnergyTotalJ`(double)、`Instance.StatOverallStationStarvationTimeSec`(double)、`Instance.ItemManager.GetInfoPendingOrderCount()`(int)、`instance.ControllerConfig.OrderBatchingConfig as SplitM2eICConfiguration`。
- Produces:`TMetaController(Instance instance)` ctor;`void Step(double currentTime)`;`void SaveIfTraining()`。Task 3 呼叫這些。

- [ ] **Step 1: 建檔 `RAWSimO.Core/Control/TMetaController.cs`**

```csharp
using System;
using System.Globalization;
using System.IO;
using RAWSimO.Core.Configurations;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// (RL-T) Tabular Q-learning meta-controller. Every RLTMetaIntervalSec it reads a
    /// discretized internal state (recent station-starvation bin + backlog bin), computes the
    /// reward of the interval that just ended (dItems - lambda*dEnergyJ), does one Q-update
    /// (training only), picks the next T via eps-greedy (train) / greedy (deploy), and writes
    /// it into the tier config's PipelineFloorTarget. The tier decode is untouched - it just
    /// reads the (now dynamic) field every solve. No-op unless RLTMetaEnabled.
    /// </summary>
    public class TMetaController
    {
        private const int NStarvBins = 3, NBacklogBins = 3, NStates = 9, NActions = 4;
        private static readonly int[] TValues = { 1, 2, 3, 4 };
        // Discretization edges (tune from observed distributions in this repo).
        private static readonly double[] StarvEdges = { 50.0, 200.0 };  // per-interval starvation sec
        private static readonly int[] BacklogEdges = { 40, 80 };        // pending order count

        private readonly Instance _instance;
        private readonly SettingConfiguration _cfg;
        private readonly SplitM2eICConfiguration _icConfig;
        private readonly double[,] _Q = new double[NStates, NActions];
        private readonly Random _rng = new Random(12345);
        private readonly bool _active;

        private double _lastStepTime = double.NegativeInfinity;
        private int _lastState = -1, _lastAction = -1;
        private double _snapItems, _snapEnergyJ, _snapStarv;

        public TMetaController(Instance instance)
        {
            _instance = instance;
            _cfg = instance.SettingConfig;
            _icConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM2eICConfiguration;
            _active = _cfg != null && _cfg.RLTMetaEnabled && _icConfig != null;
            if (_active && !string.IsNullOrEmpty(_cfg.RLTMetaQTablePath) && File.Exists(_cfg.RLTMetaQTablePath))
                LoadQ(_cfg.RLTMetaQTablePath);
        }

        public void Step(double currentTime)
        {
            if (!_active) return;
            if (_lastStepTime != double.NegativeInfinity && currentTime - _lastStepTime < _cfg.RLTMetaIntervalSec)
                return;

            int sPrime = DiscretizeState();

            if (_lastState >= 0 && _cfg.RLTMetaTrain)
            {
                double dItems = CurrentItems() - _snapItems;
                double dEnergy = CurrentEnergyJ() - _snapEnergyJ;
                double r = dItems - _cfg.RLTMetaLambda * dEnergy;
                double best = MaxQ(sPrime);
                _Q[_lastState, _lastAction] += _cfg.RLTMetaAlpha *
                    (r + _cfg.RLTMetaGamma * best - _Q[_lastState, _lastAction]);
            }

            int a = (_cfg.RLTMetaTrain && _rng.NextDouble() < _cfg.RLTMetaEpsilon)
                ? _rng.Next(NActions) : ArgMaxQ(sPrime);
            _icConfig.PipelineFloorTarget = TValues[a];

            _lastState = sPrime;
            _lastAction = a;
            _snapItems = CurrentItems();
            _snapEnergyJ = CurrentEnergyJ();
            _snapStarv = CurrentStarv();
            _lastStepTime = currentTime;
        }

        public void SaveIfTraining()
        {
            if (_active && _cfg.RLTMetaTrain && !string.IsNullOrEmpty(_cfg.RLTMetaQTablePath))
                SaveQ(_cfg.RLTMetaQTablePath);
        }

        private double CurrentItems() { return _instance.StatOverallItemsHandled; }
        private double CurrentEnergyJ() { return _instance.StatOverallEnergyTotalJ; }
        private double CurrentStarv() { return _instance.StatOverallStationStarvationTimeSec; }

        private int DiscretizeState()
        {
            double intervalStarv = (_lastState >= 0) ? Math.Max(0.0, CurrentStarv() - _snapStarv) : 0.0;
            int sb = 0; while (sb < StarvEdges.Length && intervalStarv >= StarvEdges[sb]) sb++;
            int pending = _instance.ItemManager.GetInfoPendingOrderCount();
            int bb = 0; while (bb < BacklogEdges.Length && pending >= BacklogEdges[bb]) bb++;
            return sb * NBacklogBins + bb;
        }

        private int ArgMaxQ(int s)
        {
            int best = 0; double bv = _Q[s, 0];
            for (int a = 1; a < NActions; a++) if (_Q[s, a] > bv) { bv = _Q[s, a]; best = a; }
            return best;
        }

        private double MaxQ(int s)
        {
            double bv = _Q[s, 0];
            for (int a = 1; a < NActions; a++) if (_Q[s, a] > bv) bv = _Q[s, a];
            return bv;
        }

        private void SaveQ(string path)
        {
            using (var w = new StreamWriter(path, false))
                for (int s = 0; s < NStates; s++)
                    for (int a = 0; a < NActions; a++)
                        w.WriteLine(s + "," + a + "," + _Q[s, a].ToString("R", CultureInfo.InvariantCulture));
        }

        private void LoadQ(string path)
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var p = line.Split(',');
                if (p.Length != 3) continue;
                int s = int.Parse(p[0], CultureInfo.InvariantCulture);
                int a = int.Parse(p[1], CultureInfo.InvariantCulture);
                double v = double.Parse(p[2], CultureInfo.InvariantCulture);
                if (s >= 0 && s < NStates && a >= 0 && a < NActions) _Q[s, a] = v;
            }
        }
    }
}
```

- [ ] **Step 2: 加 csproj `<Compile Include>`**

在 `RAWSimO.Core/RAWSimO.Core.csproj` 找到任一 `<Compile Include="Control\` 行,在其附近加:

```xml
    <Compile Include="Control\TMetaController.cs" />
```

- [ ] **Step 3: 驗證 Instance 上的成員名稱存在(不存在就修正呼叫)**

Run(Grep 確認,四個都要有):
`grep -nE "public int StatOverallItemsHandled|StatOverallEnergyTotalJ|StatOverallStationStarvationTimeSec" RAWSimO.Core/InstanceStatistics.cs`
`grep -n "public int GetInfoPendingOrderCount" RAWSimO.Core/Management/ItemManager.cs`
Expected: 四個成員都找得到。若 `StatOverallStationStarvationTimeSec` 是別名(例如只有 per-station),改用 sum 或既有 overall 屬性。

- [ ] **Step 4: Build**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo`
Expected: 無 error(CS 錯多半是成員名不符 → 依 Step 3 修)。

- [ ] **Step 5: Commit**

```bash
git add RAWSimO.Core/Control/TMetaController.cs RAWSimO.Core/RAWSimO.Core.csproj
git commit -m "feat(rl-t): TMetaController tabular Q-learning class (logic only, not yet wired)"
```

---

### Task 3: 掛進 `Controller.Update` + zero-drift 驗收

**Files:**
- Modify: `RAWSimO.Core/Control/Controller.cs`(欄位區 + ctor/首次 Update 建立 + `Update(double)` @ line 231 內呼叫 + finish 時 SaveIfTraining)

**Interfaces:**
- Consumes:`TMetaController(Instance)`、`Step(double)`、`SaveIfTraining()`(Task 2)。

- [ ] **Step 1: 加欄位 + lazy 建立 + 每 tick 呼叫**

在 `Controller.cs` 加私有欄位(類別欄位區):

```csharp
        private TMetaController _tMeta;
```

在 `public void Update(double elapsedTime)`(line 231)**方法開頭**加:

```csharp
            if (_tMeta == null) _tMeta = new TMetaController(Instance);
            _tMeta.Step(Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0);
```

(若 Controller 內取 currentTime 的慣用寫法不同,改用該慣用來源;`Instance.Controller.CurrentTime` 為預設。)

- [ ] **Step 2: finish 時存 Q-table**

找到 Controller 模擬結束的收尾點(例如 `Finish()` 或 sim 迴圈結束處;grep `void Finish`),在該處加:

```csharp
            if (_tMeta != null) _tMeta.SaveIfTraining();
```

若無明確 Finish,改在 CLI 收尾寫檔(見 Task 4);此步可先略,Task 4 補。

- [ ] **Step 3: Build**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo`
Expected: 無 error。

- [ ] **Step 4: zero-drift 驗收(flag off = bit-identical)**

用**改動前**已存的 tier 基準輸出(例如 `output_tiernew_mu100_s0`,若不在則先用改動前 binary 跑一次存起來當基準)。用**新 binary**、`RLTMetaEnabled` 未設(預設 false)重跑同一場景到新目錄,逐位比 kpi:

```bash
CLI="RAWSimO.CLI/bin/x64/Release/RAWSimO.CLI.exe"; SM="Material/Instances/CoreBenchmark/small"
"$CLI" "$SM/small.xlayo" "$SM/small_o100_mu100_4h_inv50.xsett" "$SM/split_milp_m2eic_tier.xconf" "output_rl_driftcheck" 0
dnew=$(find output_rl_driftcheck -maxdepth 1 -type d -name "1-*"); dold=$(find output_tiernew_mu100_s0 -maxdepth 1 -type d -name "1-*")
diff -q "$dnew/kpi_report.csv" "$dold/kpi_report.csv" && echo "ZERO-DRIFT OK" || echo "DRIFT!"
```
Expected: `ZERO-DRIFT OK`。若 DRIFT → meta-controller 在 flag off 沒有真正 no-op(檢查 `_active` gate),必修。

- [ ] **Step 5: smoke — flag on 時 T 真的會變**

複製一份 tier xsett 開 flag(只改 4 個 RL 欄位 + Name,byte-clean):

```bash
sed -e 's|<Name>small_o100_mu100_4h_inv50</Name>|<Name>rl_smoke</Name>|' \
    -e 's|<BackfillProbeEnabled>false</BackfillProbeEnabled>|<BackfillProbeEnabled>false</BackfillProbeEnabled>\n  <RLTMetaEnabled>true</RLTMetaEnabled>\n  <RLTMetaTrain>true</RLTMetaTrain>\n  <RLTMetaQTablePath>qtable_smoke.txt</RLTMetaQTablePath>|' \
    "$SM/small_o100_mu100_4h_inv50.xsett" > "$SM/rl_smoke.xsett"
rm -f qtable_smoke.txt
"$CLI" "$SM/small.xlayo" "$SM/rl_smoke.xsett" "$SM/split_milp_m2eic_tier.xconf" "output_rl_smoke" 0
ls -la qtable_smoke.txt && head qtable_smoke.txt
```
Expected: 跑完 SUCCESS、`qtable_smoke.txt` 生成且有 36 行(Q 值非全 0 = 有學到東西 / 有 reward 訊號)。

- [ ] **Step 6: Commit**

```bash
git add RAWSimO.Core/Control/Controller.cs Material/Instances/CoreBenchmark/small/rl_smoke.xsett
git commit -m "feat(rl-t): wire TMetaController into Controller.Update; zero-drift verified off, smoke on"
```

---

### Task 4: 訓練/評估 driver + 首次結果

**Files:**
- Create: `run_rl_train.cmd`、`run_rl_eval.cmd`(repo 根,沿用既有 .cmd + detached Start-Process 模式)

**Interfaces:**
- Consumes:`rl_smoke.xsett` 的 RL 欄位模式(train vs deploy 由 `RLTMetaTrain` 切)。

- [ ] **Step 1: 訓練 driver**

建 `run_rl_train.cmd`:多 seed 迴圈跑 train 模式,共用同一個 `RLTMetaQTablePath`(跨 episode 累積 Q)。先建 train xsett(`RLTMetaTrain=true`,固定 QTablePath=`qtable_lam001.txt`),迴圈 seed 0..M:

```cmd
@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (0 1 2 3 4 5 6 7 8 9) do (
  echo RL-TRAIN-S%%S START >> run_rl_train.log
  "%CLI%" "%SM%\small.xlayo" "%SM%\rl_train.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_rltrain_s%%S" %%S >> run_rl_train_runs.log 2>&1
  echo RL-TRAIN-S%%S done >> run_rl_train.log
)
echo RL-TRAIN-DONE >> run_rl_train.log
```

(`rl_train.xsett` = tier xsett + `RLTMetaEnabled=true` `RLTMetaTrain=true` `RLTMetaQTablePath=qtable_lam001.txt` `RLTMetaLambda=0.01`;byte-clean 除這些欄位。M=10 起,看 Q 收斂再加。)

- [ ] **Step 2: 跑訓練(detached)**

Run(PowerShell):`Remove-Item qtable_lam001.txt -EA SilentlyContinue; Start-Process -WindowStyle Hidden -FilePath "run_rl_train.cmd"`,用 Monitor tail `run_rl_train.log` 的 `done|DONE`。
Expected: 10 個 episode 陸續完成;`qtable_lam001.txt` 的 Q 值隨 episode 演化(非全 0、且各 state 動作偏好逐漸分化)。

- [ ] **Step 3: 評估 driver(deploy vs 最佳固定 T)**

建 `rl_eval.xsett`(= tier + `RLTMetaEnabled=true` `RLTMetaTrain=false`(greedy)`RLTMetaQTablePath=qtable_lam001.txt`)。在 **held-out 種子**(訓練用 0-9 → 評估用 100-104)上跑 deploy;同種子也跑固定 T=1/2/3/4(直接用 tier.xconf 改 PipelineFloorTarget 的 4 個臂)。用同一 λ=0.01 算每場的 `Σ(Δitems−λ·Δenergy)`(或直接用終場 items 與 energy:`score = itemsHandled − 0.01·energyJ`)。

```cmd
@echo off
cd /d C:\Users\Aesop\Desktop\EE-RAWSim-O_PP
set CLI=RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe
set SM=Material\Instances\CoreBenchmark\small
for %%S in (100 101 102 103 104) do (
  "%CLI%" "%SM%\small.xlayo" "%SM%\rl_eval.xsett" "%SM%\split_milp_m2eic_tier.xconf" "output_rleval_rl_s%%S" %%S >> run_rl_eval_runs.log 2>&1
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\tier_T1.xconf" "output_rleval_T1_s%%S" %%S >> run_rl_eval_runs.log 2>&1
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\tier_T2.xconf" "output_rleval_T2_s%%S" %%S >> run_rl_eval_runs.log 2>&1
  "%CLI%" "%SM%\small.xlayo" "%SM%\small_o100_mu100_4h_inv50.xsett" "%SM%\tier_T3.xconf" "output_rleval_T3_s%%S" %%S >> run_rl_eval_runs.log 2>&1
)
echo RL-EVAL-DONE >> run_rl_eval.log
```

(`tier_T1/T2/T3.xconf` 已存在;需要 T4 再建。)

- [ ] **Step 4: 算 score,裁決假說**

用 awk 從各 output 讀 `StatOverallItemsHandled` 與 `StatEnergyTotalKJ`(×1000 回焦耳),算 `score = items − 0.01·energyJ`,對 5 個 held-out 種子取平均,比 RL vs 最佳固定 T。
Expected(裁決):
- RL 平均 score 顯著 > 最佳固定 T → **假說成立**,記進 memory。
- RL ≈ 或 < 最佳固定 T → **null result**:「穩態下動態 T 無顯著增益」,誠實記進 memory(佐證固定 T 近最優)。

- [ ] **Step 5: Commit**

```bash
git add run_rl_train.cmd run_rl_eval.cmd Material/Instances/CoreBenchmark/small/rl_train.xsett Material/Instances/CoreBenchmark/small/rl_eval.xsett
git commit -m "feat(rl-t): training + eval drivers; first dynamic-T vs best-fixed-T result"
```

---

## 完成後

- 不論假說成立或 null,**更新 memory** `project_m2e_ic.md`:記錄 RL-T 的架構、λ、結果(RL vs 最佳固定 T 的 held-out score),以及 zero-drift/回退狀態。
- **回退**:任何時候失敗 → `git checkout 6/24`,現狀 tier 完全復原。RL commits 留在 `rl-dynamic-t`。
