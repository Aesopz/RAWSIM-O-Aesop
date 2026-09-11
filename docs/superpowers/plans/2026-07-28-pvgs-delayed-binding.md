# PVGS-DB 貪婪延遲綁定 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 PVGSManager 疊一層 config-gated 的「延遲綁定」閘門，讓貪婪拉新 pod 的時機改成時鐘驅動（同 M3G 的 PipelineFloor），使 pile-on/IPO/EOR 逼近 M3G 到 −2% 內。

**Architecture:** 重用 M3G 現成的 `M2eICMath.PipelineGateOpen` 閘門與 `OutputStation.GetInfoCurrentPodReleaseLeft()` 投影量，在 `PVGSManager.DispatchLoop` 的 per-station 迴圈加一道 gated 閘：閘關時該站本 epoch 不拉新 pod → 訂單留 backlog 累積 → 閘開拉到的 pod 服務更多累積單 → pile-on 攢高。開關 `DelayedBindingFloor` 關閉時整塊跳過、與現行 PVGS 逐位一致。

**Tech Stack:** C# 7.3 / net48 / RAWSimO.Core；Gurobi 不參與（純啟發式）。

## Global Constraints

- **Build 只能 x64 Release**：`MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release`（PowerShell，不要用 Git Bash 跑 MSBuild，`/p:` 會被 MSYS 路徑轉換破壞）。
- **C# 7.3**：新欄位加在既有 `.cs` 內，無需改 csproj。out 參數不可被 lambda 捕獲（CS1628）。
- **不可動** `M1GManager.cs` / `HADGSManager.cs` / `SplitM2eICManager.cs`。可改 `PVGSManager.cs`（非上述三者）。
- **zero-drift 紀律**：`DelayedBindingFloor=false` 時必須與現行 `pvgs_e_eps_tier.xconf` 跑出的 footprint **逐位一致**（whole-block skip，非係數歸零）。
- **標準場景**：o100 = `Material\Instances\CoreBenchmark\small\small_o100_mu100_4h_inv70.xsett`；o200 同名 o200；layout = `small.xlayo`；2 seeds（0,1）。
- **CLI**：`RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe <layout> <xsett> <xconf> <outdir> <seed>`。
- **驗收指標欄位**（footprint.csv `;` 分隔，0-based 無 header）：TP=col92(OrderThroughputRate)、PO=col130(OrderPileOneAvg)、IPO=col122(ItemPileOneAvg)、turnover=col93(OrderTurnoverTimeAvg)；EOR 讀 statistics.txt 的 `KPI_EOR:` 行。
- **M3G 基準（o100 2-seed，驗收比對目標）**：PO=5.82、IPO=9.70、EOR=1.49。

---

### Task 1: Config 欄位 + pvgs_db.xconf（scaffolding + zero-drift 驗證）

**Files:**
- Modify: `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`（`PVGSConfiguration` 類，約 line 1432 起）
- Create: `Material/Instances/CoreBenchmark/small/pvgs_db.xconf`

**Interfaces:**
- Produces: `PVGSConfiguration.DelayedBindingFloor` (bool), `.PipelineFloorLeadSec` (double), `.PipelineFloorTarget` (int) — Task 2 消費。

- [ ] **Step 1: 在 PVGSConfiguration 加三個欄位**

在 `PVGSConfiguration` 類內（最後一個既有欄位 `ForceFillMaxPerEpoch` 之後、類的 `}` 之前）加入：

```csharp
        /// <summary>
        /// (PVGS-DB) Master switch for greedy delayed binding: gate new-pod dispatch in
        /// DispatchLoop behind the pipeline lead gate, so pods are pulled clock-driven
        /// (like SplitM2eIC's PipelineFloor) instead of coverage-driven. This lets demand
        /// accumulate in the backlog between gate openings, raising pile-on toward the MILP.
        /// Default false = whole-block skip, bit-identical to plain PVGS.
        /// </summary>
        public bool DelayedBindingFloor = false;
        /// <summary>
        /// (PVGS-DB) Lead seconds: pull the next pod for a station only once its processing
        /// pod's remaining committed work is within this many seconds. Mirrors SplitM2eIC
        /// PipelineFloorLeadSec (AE-validated 70). Ignored when DelayedBindingFloor is false.
        /// </summary>
        public double PipelineFloorLeadSec = 70;
        /// <summary>
        /// (PVGS-DB) Target in-flight (inbound) pods per station - anti-oversupply cap: skip
        /// pulling a new pod when the station already has >= this many inbound. Mirrors
        /// SplitM2eIC PipelineFloorTarget (1). Ignored when DelayedBindingFloor is false.
        /// </summary>
        public int PipelineFloorTarget = 1;
```

- [ ] **Step 2: Build 驗證欄位編譯過**

Run（PowerShell）：
```
MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release /t:RAWSimO.Core
```
Expected: Build succeeded, 0 errors。

- [ ] **Step 3: 建立 pvgs_db.xconf（拷貝 pvgs_e_eps_tier 並開三欄位）**

在 `Material/Instances/CoreBenchmark/small/` 執行（Git Bash 可）：
```bash
cd "C:/Users/Aesop/Desktop/EE-RAWSim-O_PP/Material/Instances/CoreBenchmark/small"
sed -e 's|<Name>pvgs_e_eps_tier</Name>|<Name>pvgs_db</Name>|' \
    -e 's|</OrderBatchingConfig>|  <DelayedBindingFloor>true</DelayedBindingFloor>\n    <PipelineFloorLeadSec>70</PipelineFloorLeadSec>\n    <PipelineFloorTarget>1</PipelineFloorTarget>\n  </OrderBatchingConfig>|' \
    pvgs_e_eps_tier.xconf > pvgs_db.xconf
grep -E "DelayedBindingFloor|PipelineFloorLeadSec|PipelineFloorTarget|<Name>pvgs_db" pvgs_db.xconf
```
Expected: 印出三個新 tag（true/70/1）與 `<Name>pvgs_db</Name>`。若 XmlSerializer 對欄位順序敏感導致反序列化失敗，改為手動把三行插在 `</OrderBatchingConfig>` 前。

- [ ] **Step 4: zero-drift 驗證（floor 關 = 現行 PVGS 逐位一致）**

建一個 `pvgs_db_off.xconf`（floor 關）：
```bash
sed 's|<DelayedBindingFloor>true</DelayedBindingFloor>|<DelayedBindingFloor>false</DelayedBindingFloor>|' pvgs_db.xconf > pvgs_db_off.xconf
```
跑 pvgs_db_off vs pvgs_e_eps_tier（seed 0）：
```
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
& $cli "$sm\small.xlayo" $st "$sm\pvgs_e_eps_tier.xconf" "output_zd_ref" 0
& $cli "$sm\small.xlayo" $st "$sm\pvgs_db_off.xconf" "output_zd_off" 0
```
比對兩者 footprint.csv 的 KPI 欄位（col92/122/130/93）：
```bash
for c in 92 122 130 93; do a=$(awk -F';' -v C=$c '{print $C}' output_zd_ref/*/footprint.csv); b=$(awk -F';' -v C=$c '{print $C}' output_zd_off/*/footprint.csv); [ "$a" = "$b" ] && echo "col$c OK" || echo "col$c DRIFT ref=$a off=$b"; done
```
Expected: 四欄全 `OK`。若 DRIFT，表示欄位插入意外改變了反序列化（回報，勿硬改）。因為 Task 1 尚未動 DispatchLoop，floor 開/關都不該有行為差異——此步純驗證「加欄位本身」zero-drift。

- [ ] **Step 5: 記錄完成（不 commit，除非使用者要求）**

在 `.superpowers/sdd/progress.md` 記 Task 1 done + zero-drift 結果。

---

### Task 2: DispatchLoop 延遲綁定閘門（核心邏輯 + 驗收）

**Files:**
- Modify: `RAWSimO.Core/Control/Defaults/OrderBatching/PVGSManager.cs`（`DispatchLoop`，per-station 迴圈約 line 620-623）

**Interfaces:**
- Consumes: Task 1 的 `_config.DelayedBindingFloor` / `.PipelineFloorLeadSec` / `.PipelineFloorTarget`；既有 `st.StationList[s]` (OutputStation)、`st.PodsAt[s]` (HashSet<Pod> 在途)、`M2eICMath.PipelineGateOpen`、`OutputStation.GetInfoCurrentPodReleaseLeft()`。

- [ ] **Step 1: 在 DispatchLoop per-station 迴圈加 gated 閘**

在 `PVGSManager.cs` 的 `DispatchLoop` 內，找到（約 line 620-623）：
```csharp
                    for (int s = 0; s < st.StationList.Count; s++)
                    {
                        if (st.FreeSlots[s] <= 0)
                            continue;
```
在 `if (st.FreeSlots[s] <= 0) continue;` 之後、緊接的 force-fill 註解之前，插入：
```csharp
                        // (PVGS-DB) Delayed binding: gate new-pod dispatch behind the pipeline
                        // lead gate + anti-oversupply cap, mirroring SplitM2eIC's PipelineFloor.
                        // Empty station (no processing pod -> releaseLeft NaN) => gate open, so a
                        // starved station always pulls a pod (deadlock-safe). Busy station defers
                        // until its processing pod is within LeadSec of finishing, letting demand
                        // accumulate so the pulled pod serves more orders (higher pile-on).
                        if (_config != null && _config.DelayedBindingFloor)
                        {
                            double dbReleaseLeft = st.StationList[s].GetInfoCurrentPodReleaseLeft();
                            bool dbGateOpen = M2eICMath.PipelineGateOpen(
                                !double.IsNaN(dbReleaseLeft), dbReleaseLeft, _config.PipelineFloorLeadSec);
                            if (!dbGateOpen || st.PodsAt[s].Count >= _config.PipelineFloorTarget)
                                continue;
                        }
```

確認檔案頂部已 `using` 到 `M2eICMath`（同 namespace `RAWSimO.Core.Control.Defaults.OrderBatching`，無需額外 using；若編譯報找不到，加 `using` 或用完整名稱 `RAWSimO.Core.Control.Defaults.OrderBatching.M2eICMath`）。

- [ ] **Step 2: Build**

Run（PowerShell）：
```
MSBuild RAWSimOWithSolverWrapping.sln /p:Platform=x64 /p:Configuration=Release
```
Expected: Build succeeded, 0 errors。

- [ ] **Step 3: 確認只改了預期檔案**

Run: `git diff --stat`
Expected: 只有 `MethodConfigurationsOB.cs` 與 `PVGSManager.cs` 被改（xconf 為 untracked）。

- [ ] **Step 4: 主驗收 — pvgs_db (floor on) o100 2-seed vs M3G**

```
$cli="RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe"; $sm="Material\Instances\CoreBenchmark\small"; $st="$sm\small_o100_mu100_4h_inv70.xsett"
foreach($s in 0,1){ & $cli "$sm\small.xlayo" $st "$sm\pvgs_db.xconf" "output_pvgsdb_o100_s$s" $s }
```
解析（Git Bash，注意 cp1252 stdout 不能有中文/希臘字母）：
```bash
python - <<'PY'
import glob
def col(d):
    v=open(glob.glob(d+"/*/footprint.csv")[0],encoding='utf-8-sig').readline().split(';')
    eor=None
    for ln in open(glob.glob(d+"/*/statistics.txt")[0],encoding='utf-8-sig'):
        if ln.startswith("KPI_EOR:"): eor=float(ln.split(":")[1])
    return float(v[91]),float(v[129]),float(v[121]),eor
tp=[];po=[];ipo=[];eor=[]
for s in [0,1]:
    a,b,c,d=col(f"output_pvgsdb_o100_s{s}"); tp.append(a);po.append(b);ipo.append(c);eor.append(d)
avg=lambda x:sum(x)/len(x)
PO,IPO,EOR=avg(po),avg(ipo),avg(eor)
print(f"PVGS-DB  TP={avg(tp):.1f}  PO={PO:.2f}  IPO={IPO:.2f}  EOR={EOR:.2f}")
print(f"M3G ref  PO=5.82  IPO=9.70  EOR=1.49")
print(f"gap%%: PO={100*(PO-5.82)/5.82:+.1f}  IPO={100*(IPO-9.70)/9.70:+.1f}  EOR={100*(EOR-1.49)/1.49:+.1f}")
ok = PO>=5.82*0.98 and IPO>=9.70*0.98 and EOR<=1.49*1.02
print("ACCEPT" if ok else "NOT YET (see risk mitigation: sweep LeadSec)")
PY
```
Expected（驗收）：PO≥5.70、IPO≥9.51、EOR≤1.52（即三者 −2% 內）。印 `ACCEPT`。

- [ ] **Step 5: 若未達標 — LeadSec 掃描**

若 Step 4 印 `NOT YET`，建 LeadSec∈{120,180} 變體並重跑 Step 4：
```bash
for L in 120 180; do sed "s|<PipelineFloorLeadSec>70</PipelineFloorLeadSec>|<PipelineFloorLeadSec>$L</PipelineFloorLeadSec>|" pvgs_db.xconf > pvgs_db_L$L.xconf; done
```
跑各變體 o100 2-seed，取最接近 M3G（且仍小輸）的 LeadSec。若三個 LeadSec 都差 M3G >5%，**停手回報使用者**（貪婪結構極限，勿加新機制污染啟發式定位）。

- [ ] **Step 6: o200 死鎖檢查**

```
$st2="$sm\small_o200_mu100_4h_inv70.xsett"
& $cli "$sm\small.xlayo" $st2 "$sm\pvgs_db.xconf" "output_pvgsdb_o200_s0" 0
```
解析 TP（col92）：正常（>250）= 不死鎖；若 <100 或凍結 = 死鎖，回報。

- [ ] **Step 7: 記錄完成**

在 `.superpowers/sdd/progress.md` 記 Task 2 done + 驗收數字 + 選定的 LeadSec。

---

## Self-Review

- **Spec coverage**：Config 三欄位（Task 1）✓；DispatchLoop 閘（Task 2 Step 1）✓；zero-drift（Task 1 Step 4）✓；主驗收 −2%（Task 2 Step 4）✓；o200 死鎖（Task 2 Step 6）✓；LeadSec 風險緩解（Task 2 Step 5）✓；命名 pvgs_db ✓。
- **Placeholder scan**：無 TBD；所有 code step 有逐字程式碼。
- **Type consistency**：`DelayedBindingFloor`/`PipelineFloorLeadSec`/`PipelineFloorTarget` 三處命名一致；`st.StationList[s]`/`st.PodsAt[s]`/`GetInfoCurrentPodReleaseLeft`/`M2eICMath.PipelineGateOpen` 皆為既有真實符號。
