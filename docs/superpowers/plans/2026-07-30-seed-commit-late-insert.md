# 種子承諾 + 到站續單 實作計畫

> **給執行者：** 依 `docs/superpowers/specs/2026-07-30-seed-commit-late-insert-design.md` 實作。步驟用 `- [ ]` 追蹤。

**Goal:** 讓 M3G 在派車時只綁定一張「種子」子單、其餘計畫留待後續重解以更新的狀況決定，藉此把綁定時機往後推而不動目標式。

**Architecture:** 現行 `DeferAllocationUntilProcessing` 已能原子跳過未就緒的訂單，但單獨啟用會死鎖（實測：0 訂單完成、21 次決策後凍結，因為沒有訂單被落實 → 貨架永不進入處理中 → 延後條件永不成立）。本計畫加入**種子豁免**：每一台本輪要被派出的貨架，放行一張最有價值的訂單，讓行程合法成立並打破啟動僵局。

**Tech Stack:** C# 7.3 / net48 / RAWSimO.Core

## Global Constraints

- Build **只能** x64 Release：`MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`（在 PowerShell 執行；Git Bash 會把 `/p:` 誤解為路徑）。MSBuild 路徑：`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- **C# 7.3**：不可用 switch expression、range、target-typed new。out 參數不可被 lambda 捕獲（CS1628），需以區域變數包一層。
- **絕對不可修改** `M1GManager.cs`、`HADGSManager.cs`，以及任何 `RAWSimO.Core/Control/BotManagerPodSelection.cs`、`RAWSimO.Core/Elements/OutputStation.cs` 等引擎檔。本計畫**只動** `SplitM2eICManager.cs` 與 `MethodConfigurationsOB.cs`。
- **新旗標預設關閉**，關閉時必須與現行 M3G **逐位一致**。
- 正典 `Material/Instances/CoreBenchmark/small/split_milp_m3g.xconf` **不可修改**。
- xconf 變體只允許差異：`<Name>` 與新增的設定值行；其餘逐位相同（用 `diff <(tr -d '\r' < A) <(tr -d '\r' < B)` 驗證）。
- 驗收基準（正典 M3G，o100 Fill inv70 4h）：seed0 TP 300.2 / 飢餓 340.0 / pile-on 4.584 / PO 4.62 / IPO 10.92 / EOR 1.397 / 趟次 260；seed1 TP 290.0 / 飢餓 226.8 / pile-on 4.659 / PO 4.70 / IPO 11.54 / EOR 1.450 / 趟次 247。

---

### Task 1: 設定值 + xconf 變體 + zero-drift

**Files:**
- Modify: `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`（`SplitM2eICConfiguration`，緊接在 `DeferAllocationUntilProcessing` 宣告之後）
- Create: `Material/Instances/CoreBenchmark/small/split_milp_m3g_seed.xconf`

**Interfaces:**
- Produces：`SplitM2eICConfiguration.SeedCommitOnly`（bool）、`.SeedSelectionRule`（`M2eICSeedRule` enum）、`.SeedGranularity`（`M2eICSeedGranularity` enum）。Task 2 會讀這三個欄位。

- [ ] **Step 1: 加入兩個 enum 與三個設定欄位**

在 `MethodConfigurationsOB.cs` 中 `SplitM2eICConfiguration` 類別之外（同 namespace，置於該類別定義正上方）加入：

```csharp
    /// <summary>
    /// Which order a dispatched pod's seed commitment is picked from (see
    /// docs/superpowers/specs/2026-07-30-seed-commit-late-insert-design.md).
    /// </summary>
    public enum M2eICSeedRule
    {
        /// <summary>Pick the order for which this solve assigns the most units.</summary>
        MostUnits,
        /// <summary>Pick the order that closes the most complete SKU lines (matches the LineClosureWeight semantics).</summary>
        MostLinesClosed,
    }

    /// <summary>
    /// How much of the seed order is committed. Only WholeChild is implemented in this
    /// stage; the finer settings are the dose-response sweep of the deferral dial.
    /// </summary>
    public enum M2eICSeedGranularity
    {
        /// <summary>Commit the seed order's whole decoded part (default).</summary>
        WholeChild,
        /// <summary>Commit only the seed order's single largest SKU line.</summary>
        SingleLine,
        /// <summary>Commit only one unit of the seed order.</summary>
        SingleUnit,
    }
```

在 `SplitM2eICConfiguration` 內、`public bool DeferAllocationUntilProcessing = false;` 那一行之後加入：

```csharp
        /// <summary>
        /// (Seed-commit) Bootstrap exemption for DeferAllocationUntilProcessing. That gate alone
        /// deadlocks: no order is committed, so no pod ever reaches "processing", so the gate
        /// never opens (measured: 0 orders handled, frozen after 21 decisions). With this on,
        /// each pod that this solve dispatches lets exactly ONE order through the gate, which
        /// makes the trip legal (the engine cancels any trip whose claim-time request match is
        /// empty) and breaks the bootstrap deadlock. Everything else stays deferred to a later
        /// solve, when the pod is inbound/at station and therefore the cheapest supply.
        /// Requires DeferAllocationUntilProcessing = true; inert on its own.
        /// False = unchanged.
        /// </summary>
        public bool SeedCommitOnly = false;
        /// <summary>
        /// Which order becomes a dispatched pod's seed. Default MostLinesClosed aligns the seed
        /// choice with the objective's line-closure reward.
        /// </summary>
        public M2eICSeedRule SeedSelectionRule = M2eICSeedRule.MostLinesClosed;
        /// <summary>
        /// How much of the seed order is committed. WholeChild = the decoded part as-is.
        /// </summary>
        public M2eICSeedGranularity SeedGranularity = M2eICSeedGranularity.WholeChild;
```

- [ ] **Step 2: 建立 xconf 變體**

在 repo 根目錄執行（Git Bash）：

```bash
cd "C:/Users/Aesop/Desktop/EE-RAWSim-O_PP/Material/Instances/CoreBenchmark/small"
sed 's#<PipelineFloorEnabled>true</PipelineFloorEnabled>#<PipelineFloorEnabled>true</PipelineFloorEnabled>\n    <DeferAllocationUntilProcessing>true</DeferAllocationUntilProcessing>\n    <SeedCommitOnly>true</SeedCommitOnly>\n    <SeedSelectionRule>MostLinesClosed</SeedSelectionRule>\n    <SeedGranularity>WholeChild</SeedGranularity>#; s#<Name>split_milp_m3g</Name>#<Name>split_milp_m3g_seed</Name>#' split_milp_m3g.xconf > split_milp_m3g_seed.xconf
diff <(tr -d '\r' < split_milp_m3g.xconf) <(tr -d '\r' < split_milp_m3g_seed.xconf)
```

Expected：只有兩處差異——`<Name>` 一行，以及新增的四行設定值。其他一律不得出現。

- [ ] **Step 3: Build**

PowerShell：

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo /m
```

Expected：`RAWSimO.Core -> ...\RAWSimO.Core.dll`、`RAWSimO.CLI -> ...\RAWSimO.CLI.exe`，無 error。

- [ ] **Step 4: zero-drift 驗證**

```bash
cd "C:/Users/Aesop/Desktop/EE-RAWSim-O_PP"
SM=Material/Instances/CoreBenchmark/small
RAWSimO.CLI/bin/x64/Release/RAWSimO.CLI.exe "$SM/small.xlayo" "$SM/small_o100_mu100_4h_inv70.xsett" "$SM/split_milp_m3g.xconf" "output_zd_after" 0 > /dev/null 2>&1
diff "$(ls -d output_ld_split_milp_m3g_s0/*/)/statistics.txt" "$(ls -d output_zd_after/*/)/statistics.txt" && echo "ZERO-DRIFT OK"
```

Expected：`ZERO-DRIFT OK`（新增設定值預設關閉，不得改變任何現行行為）。

- [ ] **Step 5: Commit**

```bash
git add RAWSimO.Core/Configurations/MethodConfigurationsOB.cs Material/Instances/CoreBenchmark/small/split_milp_m3g_seed.xconf
git commit -m "feat(m3g): seed-commit configuration surface

Adds SeedCommitOnly / SeedSelectionRule / SeedGranularity to
SplitM2eICConfiguration plus the split_milp_m3g_seed.xconf variant.
Inert until Task 2 wires the gate; canonical config untouched and
zero-drift verified.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: 種子豁免（打破延後閘門的啟動僵局）

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`（`DeferAllocationUntilProcessing` 閘門所在的解碼迴圈，現於 :1720-1749）

**Interfaces:**
- Consumes：Task 1 的 `SeedCommitOnly` / `SeedSelectionRule` / `SeedGranularity`。
- Produces：無新公開介面。

**背景（執行者必讀）：** 解碼迴圈 `foreach (var order in pendingOrders.OrderBy(o => o.ID))`（:1720）逐張處理訂單；`SplitMilpDecoder.Decode` 之後、`CreateSplitChild` 之前有一個延後閘門（:1739-1749）。該閘門會 `continue` 掉「並非所有來源貨架都正在處理中」的訂單，**整張原子跳過**（不建子單、不寫 `_Ziops`、不進 `result.Allocations`、訂單完整留在 backlog）。單獨啟用會死鎖。本 Task 在閘門前算出一組「種子訂單」ID，讓它們通過。

- [ ] **Step 1: 在解碼迴圈開始前算出種子集合**

在 `List<OutputStation> stationList = Cs.Keys.OrderBy(s => s.ID).ToList();`（:1715）之後、`HashSet<int> icThisSplitStations = new HashSet<int>();`（:1718）之前，插入：

```csharp
                // (Seed-commit) The defer gate below skips every order whose supplying pods are
                // not already being processed, which alone deadlocks: nothing is committed, so no
                // pod ever becomes "processing", so the gate never opens. Exempt exactly one
                // order per dispatched pod so its trip is legal (the engine cancels a trip whose
                // claim-time request match is empty) and the bootstrap can happen. Everything
                // else stays deferred to a later solve, when this pod is the cheapest supply.
                HashSet<int> icSeedOrderIds = new HashSet<int>();
                if (_icConfig != null && _icConfig.DeferAllocationUntilProcessing && _icConfig.SeedCommitOnly)
                {
                    bool icSeedByLines = _icConfig.SeedSelectionRule == M2eICSeedRule.MostLinesClosed;
                    // Pods this solve actually sends somewhere: they are the ones needing a seed.
                    foreach (var icPodGroup in IsdeVarNameq.GroupBy(v => v.pod.ID))
                    {
                        int icPodId = icPodGroup.Key;
                        // A pod already being processed at its station needs no seed - the gate
                        // opens for it on its own merits.
                        bool icAlreadyProcessing = icPodGroup.Any(v =>
                        {
                            HashSet<int> icProcHere;
                            return icPpByStation.TryGetValue(v.outputstation.ID, out icProcHere)
                                && icProcHere.Contains(icPodId);
                        });
                        if (icAlreadyProcessing)
                            continue;
                        int icBestOrderId = -1;
                        double icBestScore = -1.0;
                        foreach (var icOrderGroup in icPodGroup.GroupBy(v => v.order.ID))
                        {
                            double icScore = 0.0;
                            foreach (var icQ in icOrderGroup)
                            {
                                int icUnits = (int)Math.Round(variablesQ[icQ.name].GetValue());
                                if (icUnits <= 0)
                                    continue;
                                if (icSeedByLines)
                                {
                                    // A line counts only when this pod covers the order's whole
                                    // remaining demand for that SKU - same all-or-nothing rule as
                                    // the objective's c[o,i] line-closure reward.
                                    Dictionary<ItemDescription, int> icRem;
                                    int icNeed;
                                    if (residuals.TryGetValue(icQ.order, out icRem)
                                        && icRem.TryGetValue(icQ.skui, out icNeed) && icUnits >= icNeed)
                                        icScore += 1.0;
                                }
                                else
                                {
                                    icScore += icUnits;
                                }
                            }
                            // Tie-break on the lower order ID so the choice is deterministic.
                            if (icScore > icBestScore
                                || (icScore == icBestScore && icBestOrderId >= 0 && icOrderGroup.Key < icBestOrderId))
                            {
                                icBestScore = icScore;
                                icBestOrderId = icOrderGroup.Key;
                            }
                        }
                        if (icBestOrderId >= 0 && icBestScore > 0.0)
                            icSeedOrderIds.Add(icBestOrderId);
                    }
                }
```

- [ ] **Step 2: 讓種子通過延後閘門**

把現有的閘門（:1739-1749）：

```csharp
                    if (_icConfig != null && _icConfig.DeferAllocationUntilProcessing)
                    {
                        bool icAllPodsProcessing = IsdeVarNameq.Where(v => v.order.ID == order.ID).All(v =>
                        {
                            HashSet<int> icProcHere;
                            return icPpByStation.TryGetValue(v.outputstation.ID, out icProcHere)
                                && icProcHere.Contains(v.pod.ID);
                        });
                        if (!icAllPodsProcessing)
                            continue;
                    }
```

改為：

```csharp
                    if (_icConfig != null && _icConfig.DeferAllocationUntilProcessing)
                    {
                        bool icAllPodsProcessing = IsdeVarNameq.Where(v => v.order.ID == order.ID).All(v =>
                        {
                            HashSet<int> icProcHere;
                            return icPpByStation.TryGetValue(v.outputstation.ID, out icProcHere)
                                && icProcHere.Contains(v.pod.ID);
                        });
                        // (Seed-commit) a seed order is committed even though its pod has not
                        // arrived yet - that is precisely what makes the pod's trip legal.
                        if (!icAllPodsProcessing && !icSeedOrderIds.Contains(order.ID))
                            continue;
                    }
```

- [ ] **Step 3: Build**

同 Task 1 Step 3 的指令。Expected：無 error。

- [ ] **Step 4: zero-drift 再驗一次**

同 Task 1 Step 4 的指令。Expected：`ZERO-DRIFT OK`（`SeedCommitOnly` 預設關閉，且新程式碼整段被 `DeferAllocationUntilProcessing` 與 `SeedCommitOnly` 雙重 gated）。

- [ ] **Step 5: 冒煙測試——系統必須活著**

```bash
cd "C:/Users/Aesop/Desktop/EE-RAWSim-O_PP"
SM=Material/Instances/CoreBenchmark/small
RAWSimO.CLI/bin/x64/Release/RAWSimO.CLI.exe "$SM/small.xlayo" "$SM/small_o100_mu100_4h_inv70.xsett" "$SM/split_milp_m3g_seed.xconf" "output_seed_s0" 0 > /dev/null 2>&1
grep -E "^(StatOverallOrdersHandled|StatThroughputOrdersPerHour|StatPodVisitCount):" "$(ls -d output_seed_s0/*/)/statistics.txt"
```

Expected：`StatOverallOrdersHandled` **遠大於 0**（探針的死鎖值是 0；基準是 1201）。若仍是 0，代表種子沒有生效，**停下來回報**，不要繼續調參。

- [ ] **Step 6: Commit**

```bash
git add RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs
git commit -m "feat(m3g): seed exemption breaks the defer-gate bootstrap deadlock

DeferAllocationUntilProcessing alone freezes the system (0 orders
handled, 21 decisions): nothing is committed, so no pod reaches
processing, so the gate never opens. Each dispatched pod now commits
exactly one seed order, which makes its trip legal; the rest stays
deferred to a later solve when the pod is the cheapest supply.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: 驗收

**Files:** 無程式碼改動。

- [ ] **Step 1: 跑兩個 seed**

```bash
cd "C:/Users/Aesop/Desktop/EE-RAWSim-O_PP"
SM=Material/Instances/CoreBenchmark/small
for s in 0 1; do
  RAWSimO.CLI/bin/x64/Release/RAWSimO.CLI.exe "$SM/small.xlayo" "$SM/small_o100_mu100_4h_inv70.xsett" "$SM/split_milp_m3g_seed.xconf" "output_seed_s$s" $s > /dev/null 2>&1
done
```

- [ ] **Step 2: 對照基準判讀**

用 `parse_lb_probe.sh` 的同款萃取（`StatThroughputOrdersPerHour` / `StatStationStarvationTimeSec` / `StatEnergyPerOrderKJ` / `StatSystemOrderPileOn` / `StatOverallItemsHandled` / `StatOverallOrdersHandled` / `StatPodVisitCount`），對照正典 M3G 的 2-seed 平均：TP 295.1 / 飢餓 283.4 / pile-on 4.62 / PO 4.66 / IPO 11.22 / EOR 1.424 / 趟次 253.5。

判準（spec §6）：
- **成功**＝飢餓下降，且 pile-on / PO / IPO 不掉、趟次不增。
- **失敗**＝飢餓下降但 pile-on 崩或趟次暴增（等同放寬 T，無新意）。
- **無效**＝皆在雜訊內 → 綁定時機不承重，回報收工。

- [ ] **Step 3: 拆單會計自洽檢查**

```bash
head -3 "$(ls -d output_seed_s0/*/)/splitorders.csv"; wc -l "$(ls -d output_seed_s0/*/)/splitorders.csv"
```

Expected：整合鏈欄位正常（`consolidated` 有值、`consolidationWait` 非負），無孤兒 child。

- [ ] **Step 4: o200 死鎖檢查**

```bash
RAWSimO.CLI/bin/x64/Release/RAWSimO.CLI.exe "$SM/small.xlayo" "$SM/small_o200_mu100_4h_inv70.xsett" "$SM/split_milp_m3g_seed.xconf" "output_seed_o200_s0" 0 > /dev/null 2>&1
grep -E "^StatOverallOrdersHandled:" "$(ls -d output_seed_o200_s0/*/)/statistics.txt"
```

Expected：遠大於 0、且與正典 M3G 的 o200 同量級（不得凍結）。
