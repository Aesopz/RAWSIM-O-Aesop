# Fill 模式 tier 母單提早釋出 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill 模式下,tier 拆單後在「首次拆分」即把母單移出 `_availableOrders`(觸發 Fill 補新鮮單),但保留在 `_pendingOrders` 服務殘餘、不記完成——讓 tier 訂單流不再被殘餘母單堵塞,T=1 吞吐真實勝過 M1G。

**Architecture:** 解耦「Fill 補單閘門(`_availableOrders`)」與「tier 決策工作集(`_pendingOrders`)」。三處小改,全 config-gated(`ReleaseParentOnFirstSplit`,預設 false = 逐位等同現況)。M1G 完全不動。

**Tech Stack:** C# 7.3 / net48;驗證 = build + Fill 模擬 + zero-drift 逐位比對 + log 交叉核對(released⊆completed + 需求守恆)。

## Global Constraints

- **Build**:`& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo`(PowerShell 工具跑)。x86 會 runtime 失敗。
- **C# 7.3 / net48**。本計畫**不新增檔案**,全是既有檔編輯,**不需動 csproj**。
- **絕不動** `M1GManager.cs` / `HADGSManager.cs`。
- **分支**:`fill-early-release`(已從 `tier-current` 開好、spec 已提交 67c1b78,**無任何 RL 程式碼**)。實作就在此分支。
- **Config-gated 鐵律**:`ReleaseParentOnFirstSplit=false`(預設)時,新增的 `if` 區塊與診斷 logger **整段不可達** → flag off = 逐位等同現況(zero-drift)。
- **Fill 模式驗收設定**:`Material/Instances/CoreBenchmark/small/small_o100_mu100_4h_inv50.xsett`(OrderMode=Fill,OrderCount=100)。
- **M1G 對照 config**:`Material/Instances/CoreBenchmark/small/m1g.xconf`。tier config:`split_milp_m2eic_tier.xconf`。
- **CLI**:`RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe <layout.xlayo> <settings.xsett> <config.xconf> <outDir> <seed>`。
- **長跑批次**用 detached PowerShell `Start-Process`(cmd 的 LF 行尾會失敗)+ Monitor tail,勿前景阻塞。

---

### Task 1: 實作機制 + gated 釋出診斷 + zero-drift

**Files:**
- Modify: `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`(`SplitM2eICConfiguration`,`SplitCanDriveDispatch` 約 line 1350 附近)
- Modify: `RAWSimO.Core/Management/ItemManager.cs`(`TakeAvailableOrder` 約 line 1312 附近)
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`(SplitParents 迴圈 line 1762-1771;新增 logger helper)

**Interfaces:**
- Produces:`SplitM2eICConfiguration.ReleaseParentOnFirstSplit`(bool)、`ItemManager.IsOrderAvailable(Order)`(bool)、`early_release.csv`(診斷輸出,Task 2 讀)。

- [ ] **Step 1: 加 config flag**

`MethodConfigurationsOB.cs`,`public bool SplitCanDriveDispatch = false;` 之後加:

```csharp
        /// <summary>(Fill fairness) When true, a split parent is released from the ItemManager's
        /// available-order backlog on its FIRST split - freeing a Fill replenishment slot so a
        /// fresh order is injected at the same cadence M1G gets from whole-order assignment -
        /// while staying in _pendingOrders to serve its residual across periods. It is NOT marked
        /// complete; the parent completes only via child consolidation. Default false = release
        /// only at IsFullyClaimed (current behavior, bit-identical). Only affects Fill mode.</summary>
        public bool ReleaseParentOnFirstSplit = false;
```

- [ ] **Step 2: 加 O(1) availability accessor**

`ItemManager.cs`,`TakeAvailableOrder` 方法之後加:

```csharp
        /// <summary>(Fill fairness) O(1) check whether an order is still in the available-order
        /// backlog (used to release a split parent from Fill exactly once).</summary>
        public bool IsOrderAvailable(Order order) { lock (_syncRoot) { return _availableOrders.Contains(order); } }
```

- [ ] **Step 3: 加釋出邏輯 + gated 診斷 logger**

`SplitM2eICManager.cs`,把現有 SplitParents 迴圈(line 1762-1771):

```csharp
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
```

改為:

```csharp
            foreach (var parent in result.SplitParents)
            {
                if (double.IsPositiveInfinity(parent.TimeStampSubmit))
                    parent.TimeStampSubmit = Instance.Controller.CurrentTime;
                // (Fill fairness) On the parent's FIRST split, free its Fill backlog slot so a
                // fresh order is injected, while keeping it in _pendingOrders for residual
                // service. Fires exactly once (guarded by IsOrderAvailable). Whole block is
                // unreachable when the flag is off => bit-identical to current behavior.
                if (_icConfig != null && _icConfig.ReleaseParentOnFirstSplit
                    && parent.IsSplitParent
                    && (Instance.ItemManager as ItemManager).IsOrderAvailable(parent))
                {
                    int remUnits = parent.RemainingPositions.Sum(p => p.Value);
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(parent);
                    WriteEarlyRelease(parent.ID, remUnits);
                }
                if (parent.IsFullyClaimed)
                {
                    _pendingOrders.Remove(parent);
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(parent);
                }
            }
```

並在類別內(靠近其他 log helper,例如 `WriteAdaptiveExactLog` 附近)加 logger 欄位與 helper:

```csharp
        private System.IO.StreamWriter _earlyReleaseLog;

        // (Fill fairness diagnostic) one row per parent released early from the Fill backlog.
        // Only ever called on the flag-on path, so no file appears when the feature is off.
        private void WriteEarlyRelease(int parentId, int remainingUnitsAtRelease)
        {
            if (_earlyReleaseLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir)) dir = ".";
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                _earlyReleaseLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "early_release.csv"), false) { AutoFlush = true };
                _earlyReleaseLog.WriteLine("time,parentId,remainingUnitsAtRelease");
            }
            _earlyReleaseLog.WriteLine(
                Instance.Controller.CurrentTime.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "," + parentId + "," + remainingUnitsAtRelease);
        }
```

(`System.Linq` 已在檔案 using 中,`RemainingPositions.Sum` 可用;`_icConfig` 為既有欄位 `SplitM2eICManager.cs:30,48`。)

- [ ] **Step 4: Build**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo`
Expected: `RAWSimO.CLI -> ...RAWSimO.CLI.exe`,無 error。

- [ ] **Step 5: zero-drift(flag off = 現況)**

先用**改動前**的 tier 二進位跑一次 Fill 基準(若無現成),再用新二進位、flag 未設(預設 false)重跑,逐位比 kpi:

```bash
CLI="RAWSimO.CLI/bin/x64/Release/RAWSimO.CLI.exe"; SM="Material/Instances/CoreBenchmark/small"
"$CLI" "$SM/small.xlayo" "$SM/small_o100_mu100_4h_inv50.xsett" "$SM/split_milp_m2eic_tier.xconf" "output_fer_driftcheck" 0
# baseline = a pre-change tier Fill run at seed 0 (build tier-current first if none exists)
dnew=$(find output_fer_driftcheck -maxdepth 1 -type d -name "1-*")
dold=$(find output_fer_baseline_pre -maxdepth 1 -type d -name "1-*")
diff -q "$dnew/kpi_report.csv" "$dold/kpi_report.csv" && echo "ZERO-DRIFT OK" || echo "DRIFT!"
ls "$dnew/early_release.csv" 2>/dev/null && echo "UNEXPECTED early_release.csv on flag-off!" || echo "no early_release.csv (correct)"
```
Expected: `ZERO-DRIFT OK` **且** flag-off 沒有 `early_release.csv`(證明新路徑整段不可達)。若 DRIFT 或有 log → flag gating 有漏,必修。

- [ ] **Step 6: Commit**

```bash
git add RAWSimO.Core/Configurations/MethodConfigurationsOB.cs RAWSimO.Core/Management/ItemManager.cs RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs
git commit -m "feat(fill): ReleaseParentOnFirstSplit - free Fill backlog slot on first split (gated, default off)"
```

---

### Task 2: 正確性驗證 — 釋出後母單仍可見且完成(使用者硬性要求)

**Files:**
- Create: `Material/Instances/CoreBenchmark/small/tier_earlyrelease.xconf`(= `split_milp_m2eic_tier.xconf` + `ReleaseParentOnFirstSplit=true`,byte-clean 只改這一處)

**Interfaces:**
- Consumes:`early_release.csv`(Task 1)、`splitorders.csv`(既有 SplitConsolidationLogger:`parent,units,children,firstChildDone,lastChildDone,consolidated,...`,每列=一個已 consolidation 完成的母單)。

- [ ] **Step 1: 建 flag-on config**

複製 tier.xconf,只把 `ReleaseParentOnFirstSplit` 設 true(若原檔無此 token 則新增一行;其餘 byte-clean):

```bash
SM="Material/Instances/CoreBenchmark/small"
sed 's#</SplitCanDriveDispatch>#</SplitCanDriveDispatch>\n  <ReleaseParentOnFirstSplit>true</ReleaseParentOnFirstSplit>#' \
    "$SM/split_milp_m2eic_tier.xconf" > "$SM/tier_earlyrelease.xconf"
grep -c "ReleaseParentOnFirstSplit>true" "$SM/tier_earlyrelease.xconf"   # expect 1
# verify ONLY that one line differs:
diff "$SM/split_milp_m2eic_tier.xconf" "$SM/tier_earlyrelease.xconf"
```
Expected: 恰一行新增 `<ReleaseParentOnFirstSplit>true</ReleaseParentOnFirstSplit>`,無其他差異。（若 tier.xconf 無 `SplitCanDriveDispatch` 標籤,改插在任一 config token 後,仍須確保只此一處差異。）

- [ ] **Step 2: 跑 flag-on + flag-off(同 seed 0,Fill)**

```bash
CLI="RAWSimO.CLI/bin/x64/Release/RAWSimO.CLI.exe"; SM="Material/Instances/CoreBenchmark/small"
"$CLI" "$SM/small.xlayo" "$SM/small_o100_mu100_4h_inv50.xsett" "$SM/tier_earlyrelease.xconf" "output_fer_on_s0" 0
"$CLI" "$SM/small.xlayo" "$SM/small_o100_mu100_4h_inv50.xsett" "$SM/split_milp_m2eic_tier.xconf" "output_fer_off_s0" 0
```
Expected: 兩場 SUCCESS;`output_fer_on_s0/.../early_release.csv` 存在且有列。

- [ ] **Step 3: 證明「釋出的母單仍完成」(released ⊆ completed)**

```bash
don=$(find output_fer_on_s0 -maxdepth 1 -type d -name "1-*")
# released parent IDs (distinct)
tail -n +2 "$don/early_release.csv" | cut -d, -f2 | sort -u > /tmp/released_ids.txt
# completed (consolidated) parent IDs
tail -n +2 "$don/splitorders.csv" | cut -d, -f1 | sort -u > /tmp/completed_ids.txt
nrel=$(wc -l < /tmp/released_ids.txt); ncomp=$(wc -l < /tmp/completed_ids.txt)
# released-but-not-completed = still in-flight at sim end (expected small tail)
notdone=$(comm -23 /tmp/released_ids.txt /tmp/completed_ids.txt | wc -l)
echo "released=$nrel  completed(consolidated)=$ncomp  released-not-completed(end tail)=$notdone"
# residual-served-post-release proof: released parents had remaining>0 at release yet complete
awk -F, 'NR>1 && $3>0{c++} END{print "released with remaining>0 at release (must be served MORE after release):",c}' "$don/early_release.csv"
```
Expected:**絕大多數 released 母單出現在 splitorders.csv(completed)**;`released-not-completed` 只是模擬結束時仍在途的小尾巴。`remaining>0 at release` 的母單數 > 0,證明它們在釋出後仍被 tier 看到並繼續服務至完成。**若 released-not-completed 佔比異常高 → 母單被「弄丟」,必查。**

- [ ] **Step 4: 需求守恆交叉核對(沒有件數遺失)**

```bash
don=$(find output_fer_on_s0  -maxdepth 1 -type d -name "1-*")
doff=$(find output_fer_off_s0 -maxdepth 1 -type d -name "1-*")
for d in "$don" "$doff"; do
  it=$(grep -a "^StatOverallItemsHandled:"  "$d/statistics.txt" | grep -oE "[0-9]+")
  od=$(grep -a "^StatOverallOrdersHandled:" "$d/statistics.txt" | grep -oE "[0-9]+")
  echo "$d -> items=$it orders=$od"
done
```
Expected:flag-on 的 items **不低於** flag-off(沒有件數因釋出而遺失;理想上更高——這是本功能目的)。若 flag-on items 顯著**下降** → 有母單殘餘未被服務,違反正確性,必查。

- [ ] **Step 5: Commit**

```bash
git add Material/Instances/CoreBenchmark/small/tier_earlyrelease.xconf
git commit -m "test(fill): early-release correctness - released parents stay visible and complete (demand conserved)"
```

---

### Task 3: 主結果 — Fill 模式 tier(flag on)vs M1G 吞吐

**Files:**（無新檔;跑批 + 記錄）

- [ ] **Step 1: 跑 tier(flag on) vs M1G,Fill,seeds 0,1**

detached PowerShell 驅動(seeds 0,1;每場 ~7-8 分鐘):

```
tier-on : small_o100_mu100_4h_inv50.xsett + tier_earlyrelease.xconf   -> output_fer_tier_s{0,1}
M1G     : small_o100_mu100_4h_inv50.xsett + m1g.xconf                 -> output_fer_m1g_s{0,1}
```
（可加 tier-off 作三方對照,顯示釋出前後 tier 的吞吐差。）

- [ ] **Step 2: 比較指標**

各 output dir 抽取:items(`StatOverallItemsHandled`)、orders(`StatOverallOrdersHandled`)、能耗(`total_energy_mech_kJ`)、pile-on(`system_order_pile_on`)、trips(`L2,trip_count` total)、EOR(`energy_empty_to_loaded_ratio`)。兩 seed 取平均,比 **tier-on vs M1G**。
Expected(目標):tier-on items/orders 吞吐 **勝 M1G**,且維持 pile-on 高、能耗/trips 低(拆單效率)。同時 tier-on 吞吐應 **高於 tier-off**(證明釋出解掉了 Fill 堵塞)。

- [ ] **Step 3: 監看風險 R1(pending 膨脹 / MILP 變慢)**

```bash
d=$(find output_fer_tier_s0 -maxdepth 1 -type d -name "1-*")
awk -F, 'NR>1{n++; s+=$13; if($4+0>mx)mx=$4} END{print "decisions="n" avg_solveSec="s/n" max_pendingOrders="mx}' "$d/splitm2eic_decision_log.csv"
```
Expected:solve 時間與 pendingOrders 未失控(MILP 仍可解)。若 pendingOrders 隨時間單調暴增 → R1 觸發,回報使用者是否加 cap。

- [ ] **Step 4: 更新 memory + commit 結果筆記**

更新 `project_m2e_ic.md`:記錄 Fill 模式 EarlyParentRelease 的機制、正確性驗證結論(released⊆completed、需求守恆)、tier-on vs M1G 吞吐數字、與 Fixed 模式結論的一致性。

---

## 完成後

- 全分支最終審查(整 diff range),確認 flag-off zero-drift + 正確性 + 結果站得住。
- **回退**:`ReleaseParentOnFirstSplit=false` = 逐位現況;或 `git checkout tier-current` 回純 tier。
