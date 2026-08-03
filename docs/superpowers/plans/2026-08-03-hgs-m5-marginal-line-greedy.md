# HGS-M5 實作計畫（邊際行貪婪）

規格：`docs/superpowers/specs/2026-08-03-hgs-m5-marginal-line-greedy-design.md`
日期：2026-08-03

---

## Global Constraints（每個 Task 都適用，不得違反）

1. **Build 指令**（唯一合法）：
   ```
   & "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
     "C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\RAWSimOWithSolverWrapping.sln" `
     /p:Platform=x64 /p:Configuration=Release /v:minimal /nologo /m
   ```
   x86 會因 Gurobi 只有 win64 版而 runtime 失敗。**改完程式碼一定要 build 過才算完成。**

2. **語言版本 C# 7.3**（net48 legacy csproj）。禁用：target-typed `new()`、record、switch expression、`using` 宣告式。`out` 參數不能被 lambda 捕獲（CS1628）——需要時用 `var copy = outParam;` 包一層。

3. **新增 .cs 檔案必須手動加 `<Compile Include>` 到 `RAWSimO.Core/RAWSimO.Core.csproj`**，不會自動抓。

4. **絕對不可修改**下列檔案：
   - `RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs`
   - `RAWSimO.Core/Control/Defaults/OrderBatching/HADGSManager.cs`
   - `RAWSimO.Core/Control/Defaults/OrderBatching/SplitM2eICManager.cs`
   - `RAWSimO.Core/Control/Defaults/OrderBatching/M4GManager.cs`
   - **`RAWSimO.Core/Control/Defaults/OrderBatching/GreedyM4GManager.cs`** ← 這是本計畫的對照臂，鏡像它、不要改它
   即使造成程式碼重複也在所不惜——這是消融實驗乾淨對照組的前提。

5. **xconf / xsett / xlayo 的修改一律用 PowerShell `[IO.File]::ReadAllText` / `WriteAllText`，不得用 `sed`**（會把 CRLF 改成 LF）。新建的組態檔必須做 byte-clean 驗證：與母本比對只有預期的行不同，且 `file` 顯示仍為 CRLF。

6. **每個 Task 結束後跑 `git diff --stat`**，確認只有預期的檔案被改到。

7. **驗證基準讀這裡**（2 小時 / 10 車 / 2 站 / o100 / inv70，seed 0–4 平均）：

   | 指標 | M1G | M4G | HGS-M4 |
   |---|---|---|---|
   | 處理件數 | 1418.8 | 1429.4 | 1426.0 |
   | 完成訂單 | 644.0 | 585.0 | 637.4 |
   | 貨架到站 | 213.2 | 100.8 | 171.8 |
   | pile-on | 3.021 | 5.804 | 3.711 |
   | 每件能耗 | 0.896 | 0.482 | 0.769 |
   | 周轉時間 | 831 s | 1223 s | 847 s |
   | 整場牆鐘 | 91.5 s | 804.2 s | 4.4 s |

   對應的輸出目錄：`out/h2_o100_m1g_s0..s4`、`out/dink_s0..s4`、`out/hgs2h_s0..s4`。
   模擬指令範本：
   ```
   RAWSimO.CLI\bin\x64\Release\RAWSimO.CLI.exe ^
     Material\Instances\CoreBenchmark\small\small_10bot.xlayo ^
     Material\Instances\CoreBenchmark\small\small_o100_mu100_2h_inv70.xsett ^
     Material\Instances\CoreBenchmark\small\<arm>.xconf ^
     out\<dir> <seed>
   ```
   長跑批次寫成 `.cmd` + `Start-Process -WindowStyle Hidden`，不要用前景等待。

8. **指標定義權威位置**：`RAWSimO.Core/Statistics/InstanceStatistics.cs` 的 KPI Summary block（約 line 1648-1654）。footprint 欄位順序見 `RAWSimO.Core/Statistics/DataPoint.cs` 的 `enum FootPrintEntry`（1-based：items=36、lines=37、orders=38、OSIdleTimeAvg=142）。

---

## Task 1 — `GreedyM5Configuration`

**檔案**：`RAWSimO.Core/Configurations/MethodConfigurationsOB.cs`
**動作**：在 `GreedyM4GConfiguration` 類別（起始於 line 1612）**之後**、緊接著它的結尾大括號插入下列類別。不得插在它之前，也不得修改 `GreedyM4GConfiguration` 的任何一行。

```csharp
    /// <summary>
    /// Configuration for HGS-M5, the marginal-line greedy (spec 2026-08-03). Same price fields
    /// and same defaults as GreedyM4GConfiguration so the two greedy arms differ only in how they
    /// construct a solution, never in what a line or an order is worth.
    ///
    /// Note on delta: IM4GPrices requires DeltaScale / DeltaFallback / DeltaFixed, and they are
    /// kept here to satisfy the interface, but HGS-M5 never calls M4GPricing.Delta(). A
    /// constructive greedy has no "valued but not bound" state - it binds exactly what it scores -
    /// so the valuation/binding split that delta discounts does not exist here and the objective
    /// collapses to -lambda per closed line and -mu per completed order. Leaving the fields inert
    /// is deliberate; do not wire them up.
    /// </summary>
    public class GreedyM5Configuration : PVGSConfiguration, IM4GPrices
    {
        /// <summary>Returns the method type of this configuration.</summary>
        public override OrderBatchingMethodType GetMethodType() { return OrderBatchingMethodType.GreedyM5; }
        /// <summary>Returns a short name of this configuration.</summary>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "OBGREEDYM5"; }

        // ── Price calibration, identical field set and defaults to GreedyM4GConfiguration. ──
        public double LambdaScale { get; set; } = 1.0;
        public double MuScale { get; set; } = 1.0;
        public double DeltaScale { get; set; } = 1.0;
        public double EpsilonScale { get; set; } = 0.001;
        public int WarmupLines { get; set; } = 50;
        public double LambdaFallback { get; set; } = 10.0;
        public double DeltaFallback { get; set; } = 0.05;
        public double LinesPerOrderFallback { get; set; } = 2.4;
        public double LambdaFixed { get; set; } = 0.0;
        public double DeltaFixed { get; set; } = 0.0;
        public double RhoFallback { get; set; } = 15.0;

        /// <summary>
        /// Outer lambda iteration, the greedy counterpart of M4G's Dinkelbach loop: rebuild the
        /// whole epoch solution at lambda = D*/V* of the previous build and repeat. A full greedy
        /// epoch costs microseconds, so this is nearly free. 0 = single build at the historical
        /// lambda.
        /// </summary>
        public int LambdaIterations = 5;
        /// <summary>Stop the outer lambda iteration once |objective| falls below this.</summary>
        public double LambdaTolerance = 0.5;
        /// <summary>Mirrors M4G's EPR - release the parent's Fill slot the first time it is split.</summary>
        public bool ReleaseParentOnFirstSplit = true;
    }
```

**驗收**：檔案能編譯；`GreedyM4GConfiguration` 的 diff 為零。

---

## Task 2 — 引擎掛勾（四處，全部是「追加」）

⚠️ **列舉值與 `XmlInclude` 一律追加在既有項目最後，不得插隊** —— 否則既有 xconf 的反序列化會壞掉。

**2a.** `RAWSimO.Core/Configurations/MethodConfiguration.cs`，`OrderBatchingMethodType` 列舉中 `GreedyM4G,`（line 369）**之後**加：
```csharp
        /// <summary>The marginal-line greedy counterpart of M4G (HGS-M5).</summary>
        GreedyM5,
```

**2b.** 同檔案，`[XmlInclude(typeof(GreedyM4GConfiguration))]`（line 808）**之後**加：
```csharp
    [XmlInclude(typeof(GreedyM5Configuration))]
```

**2c.** `RAWSimO.Core/Control/Controller.cs`，`case OrderBatchingMethodType.GreedyM4G:`（line 128）**之後**加：
```csharp
                case OrderBatchingMethodType.GreedyM5: OrderManager = new GreedyM5Manager(instance); break;
```

**2d.** `RAWSimO.Core/RAWSimO.Core.csproj`，`<Compile Include="Control\Defaults\OrderBatching\GreedyM4GManager.cs" />`（line 133）**之後**加：
```xml
    <Compile Include="Control\Defaults\OrderBatching\GreedyM5Manager.cs" />
```

**驗收**：`git diff --stat` 只有這四個檔案，每個各 +1~3 行。

---

## Task 3 — Manager 骨架與快照層（忠實鏡像）

**新檔案**：`RAWSimO.Core/Control/Defaults/OrderBatching/GreedyM5Manager.cs`

從 `GreedyM4GManager.cs` **逐字複製**下列區段，只把類別名稱 `GreedyM4GManager` → `GreedyM5Manager`、`M4gResult` → `M5Result`、`M4gEpochState` → `M5EpochState`、`_config` 的型別 `GreedyM4GConfiguration` → `GreedyM5Configuration`：

| 來源行號 | 內容 | 修改 |
|---|---|---|
| 1–29 | using 區與類別 XML 註解 | 註解改寫為 HGS-M5 的說明 |
| 30–54 | 類別宣告、建構子、`_config` / `_pricing` 欄位 | 型別改名 |
| 55–73 | `GenerateOiSKUSplit` | 逐字 |
| 74–79 | `M4gResult` | 改名 |
| 86–117 | `M4gEpochState` | 改名，**並刪除 `ValuedNotBoundLinesThisEpoch` 欄位**（HGS-M5 不用） |
| 119–125 | `GetAvail` | 逐字 |
| 127–204 | `InitializeSnapshot` | 逐字 |
| 206–259 | `BuildEpochState` | 逐字 |
| 261–284 | `PodDrawTier` | 逐字 |
| 285–310 | `ComputeProcessingAvail` | 逐字 |

**鏡像失真才是錯，鏡像本身是規格要求。** 特別注意 `InitializeSnapshot` 的 `.Any` 收錄規則（line 163-164）與 Pa 二次收窄（line 199-200）必須完全一致——它們是與 M4G `BuildSnapshot` 對齊的關鍵。

**不要複製**：`CommitParts`（Task 6 另行處理）、`CompletionSweep`、`DispatchLoop`、`DecideAboutPendingOrders`（Task 4/5/7 重寫）。

**驗收**：檔案能編譯（此時還沒有決策邏輯，`DecideAboutPendingOrders` 先寫空實作 `{ }`）。

---

## Task 4 — 邊際動作的列舉與計價

在 `GreedyM5Manager` 中新增下列成員。

```csharp
        /// <summary>
        /// One candidate "draw-line" move: take order o's line for SKU i to its FULL current
        /// residual, from pod p standing at (or dispatched to) station s. Line granularity is
        /// deliberate - lambda only accrues when a line closes in full (M4G's V3/B5), so a
        /// per-unit move would score -epsilon (~0) for every unit but the last and a pure greedy
        /// would never reach the last one. See spec 2.3.
        /// </summary>
        private class M5LineMove
        {
            public Order Order;
            public ItemDescription Sku;
            public Pod Pod;
            public int StationIndex;
            public int Units;
            /// <summary>Marginal change in M4G's objective. Negative = worth doing.</summary>
            public double Delta;
            /// <summary>True when this line is the order's last open one, so mu also accrues.</summary>
            public bool CompletesOrder;
            /// <summary>True when the order does not yet occupy a slot at this station.</summary>
            public bool NeedsSlot;
        }

        /// <summary>
        /// Prices one draw-line move with M4G's own objective terms and nothing else:
        ///
        ///   delta = -lambda                                  (this line closes)
        ///           - mu          if it is the order's last open line
        ///           +/- rho * units   by pod tier: processing -rho, queued/en-route 0, Pa +rho
        ///           - epsilon * units                        (tie-break toward acting now)
        ///
        /// No delta-discount term: a constructive greedy binds exactly what it scores, so the
        /// valuation/binding split M4G's delta discounts does not exist here (spec 3.1).
        /// Slot capacity is a hard constraint, never a price (spec, matches M4G's B3).
        /// </summary>
        private static double ScoreLineMove(M5EpochState st, Order order, ItemDescription sku, Pod pod,
            int stationIndex, int units, bool completesOrder, double lambda, double mu, double rho, double epsilon)
        {
            double d = -lambda - epsilon * units;
            if (completesOrder)
                d -= mu;
            int tier = PodDrawTier(st, pod, st.StationList[stationIndex]);
            if (tier == 0) d -= rho * units;          // processing: window closing, taking now avoids a future trip
            else if (tier == 2 && !st.PodsAt[stationIndex].Contains(pod)) d += rho * units;  // freshly dispatched Pa
            // tier == 1 (queued) and inherited en-route Pb pods are sunk: no rho either way.
            return d;
        }
```

**注意 `tier == 2` 的判別**：`PodDrawTier` 對「還在路上」與「本輪剛派」都回 2，但只有**本輪剛派的 Pa 貨架**該付 `+rho`（繼承的 Pb 是沉沒成本）。用 `st.PodsAt[stationIndex].Contains(pod)` 區分：`BuildEpochState` 只把 Pb 放進 `PodsAt`，本輪新派的貨架由 Task 5 的派車動作加入，**加入時必須另記一個 `HashSet<Pod> DispatchedThisEpoch`**。請在 `M5EpochState` 加上：

```csharp
            /// <summary>Pa pods dispatched by THIS epoch - their draws pay +rho, unlike inherited Pb.</summary>
            public HashSet<Pod> DispatchedThisEpoch = new HashSet<Pod>();
```

並把 `ScoreLineMove` 中的判別式改為 `st.DispatchedThisEpoch.Contains(pod)`（比 `!PodsAt.Contains` 精確，且不受 Task 5 把新貨架加進 `PodsAt` 的影響）。

**列舉可行的取行動作**：

```csharp
        /// <summary>
        /// Enumerates every currently feasible draw-line move. A move is feasible when the pod
        /// stands at (or was dispatched to) the station, carries the line's FULL remaining
        /// residual in this epoch's working books, and either the order already holds a slot at
        /// that station or the station still has one free.
        /// </summary>
        private List<M5LineMove> EnumerateLineMoves(M5EpochState st,
            double lambda, double mu, double rho, double epsilon)
        {
            var moves = new List<M5LineMove>();
            foreach (var order in st.ScanOrder)
            {
                if (st.Committed.Contains(order)) continue;
                var open = st.Residuals[order].Where(p => p.Value > 0).ToList();
                if (open.Count == 0) continue;
                foreach (var line in open)
                {
                    for (int s = 0; s < st.StationList.Count; s++)
                    {
                        bool holdsSlot = st.OrderSlotAt.Contains(OrderStationKey(order, s));
                        if (!holdsSlot && st.FreeSlots[s] <= 0) continue;
                        foreach (var pod in st.PodsAt[s])
                        {
                            if (GetAvail(st, pod, line.Key) < line.Value) continue;
                            var mv = new M5LineMove
                            {
                                Order = order, Sku = line.Key, Pod = pod, StationIndex = s,
                                Units = line.Value, CompletesOrder = open.Count == 1, NeedsSlot = !holdsSlot
                            };
                            mv.Delta = ScoreLineMove(st, order, line.Key, pod, s, line.Value,
                                mv.CompletesOrder, lambda, mu, rho, epsilon);
                            moves.Add(mv);
                        }
                    }
                }
            }
            return moves;
        }

        /// <summary>Stable key for "order o occupies a slot at station index s".</summary>
        private static long OrderStationKey(Order order, int stationIndex)
        { return ((long)order.ID << 32) | (uint)stationIndex; }
```

並在 `M5EpochState` 加上：
```csharp
            /// <summary>(order, stationIndex) pairs that already consumed a slot this epoch.</summary>
            public HashSet<long> OrderSlotAt = new HashSet<long>();
```

**驗收**：能編譯。此階段尚無行為。

---

## Task 5 — 主貪婪迴圈

```csharp
        /// <summary>
        /// The greedy construction. Two move types, both of which only ever set variables that
        /// exist in M4G's model, so the solution this builds is always FEASIBLE for that MILP -
        /// which is what makes obj(HGS-M5) >= obj(M4G) hold and the difference a real optimality
        /// gap (spec 3).
        ///
        ///   A. draw-line  : take one line to its full residual from a pod already at a station.
        ///   B. dispatch   : fetch a Pa pod, paying travel; scored net of the draw-line moves it
        ///                   immediately unlocks, since a dispatch alone is always a pure cost.
        ///
        /// Slots and bots are hard constraints, never priced. Stops when no move has a negative
        /// marginal objective.
        /// </summary>
        private void GreedyBuild(M5EpochState st, HashSet<Pod> paCandidates,
            double lambda, double mu, double rho, double epsilon)
        {
            var dispatched = new HashSet<Pod>();
            while (true)
            {
                // ---- best draw-line move among already-available pods ----
                var moves = EnumerateLineMoves(st, lambda, mu, rho, epsilon);
                M5LineMove bestDraw = null;
                foreach (var m in moves)
                    if (m.Delta < 0 && (bestDraw == null
                        || m.Delta < bestDraw.Delta
                        || (m.Delta == bestDraw.Delta && !m.NeedsSlot && bestDraw.NeedsSlot)))
                        bestDraw = m;

                // ---- best dispatch, scored NET of what it unlocks ----
                Pod bestPod = null; int bestStation = -1; Bot bestBot = null;
                double bestDispatchDelta = 0.0;
                if (st.FreeBots.Count > 0 && st.FreeSlots.Any(f => f > 0))
                    EvaluateDispatch(st, paCandidates, dispatched, lambda, mu, rho, epsilon,
                        out bestPod, out bestStation, out bestBot, out bestDispatchDelta);

                bool takeDraw = bestDraw != null
                    && (bestPod == null || bestDraw.Delta <= bestDispatchDelta);

                if (takeDraw)
                {
                    ApplyLineMove(st, bestDraw);
                    continue;
                }
                if (bestPod != null && bestDispatchDelta < 0)
                {
                    ApplyDispatch(st, bestPod, bestStation, bestBot);
                    dispatched.Add(bestPod);
                    continue;
                }
                break;   // nothing left with a negative marginal objective
            }
        }
```

`EvaluateDispatch` 的規格（實作者自行寫，須符合下列語意）：

- 對每個尚未派出的 Pa 貨架 `p`、每個仍有空槽的站台 `s`、最近的閒置機器人 `r`：
  - `dispatchCost = EstimateBotPodDistance(r, p) + M1GPodStationCost(p, station) + PodStationExtraCost(p, station)`
  - **試算解鎖的取行動作**：把 `p` 暫時視為在 `s`，用與 `EnumerateLineMoves` 相同的可行性判別，依 `Delta` 由小到大貪婪選取，受該站剩餘空槽數限制，累加負值
  - `netDelta = dispatchCost + Σ(unlocked deltas)`
- 取 `netDelta` 最小者回傳；全部 `>= 0` 時 `bestPod = null`
- **試算不得改動 `st`**：用區域副本記錄暫時消耗的庫存與槽位
- 距離函數沿用 `M1GManager` 既有的 `M1GPodStationCost` / `PodStationExtraCost` / `EstimateBotPodDistance`，**不要自己算距離**

`ApplyDispatch(st, pod, stationIndex, bot)` 語意：
```csharp
            st.PodsAt[stationIndex].Add(pod);
            st.DispatchedThisEpoch.Add(pod);
            st.PodToBot[pod] = bot;
            st.FreeBots.Remove(bot);
            st.Dispatched++;
            foreach (var e in st.Avail[pod])       // 併入該站的聚合可用量
            {
                int cur;
                st.PerStationAvail[stationIndex][e.Key] =
                    (st.PerStationAvail[stationIndex].TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
            }
```
⚠️ 派車還需要把 `xps` / `yrp` 的真實副作用寫進引擎（貨架 claim、機器人綁定）。**請逐字比照 `GreedyM4GManager.DispatchLoop` 內派車成立後的那段引擎寫入**（`Instance.ResourceManager.BottoPod.Add` / `ClaimPod` 等），一行都不要漏——漏掉會導致「求解器決定了但車從未出發」，這個專案有過同樣的 bug。

**驗收**：能編譯；此時尚未接上 `DecideAboutPendingOrders`。

---

## Task 6 — 提交路徑（鏡像 `CommitParts`，一處語意修正）

從 `GreedyM4GManager.cs` line 301–383 逐字複製 `CommitParts`，改名 `M4gEpochState` → `M5EpochState`，並做**唯一一處**語意修正：

原本（line 377–382）：
```csharp
            if (coversAll)
            {
                st.Committed.Add(order);
                st.ClosedLinesThisEpoch += linesClosedThisOrder;
                st.CompletedOrdersThisEpoch += 1;
            }
```

改為：
```csharp
            // HGS-M5 difference: lambda is earned per CLOSED LINE, whether or not the whole order
            // is covered - that is the point of allowing partial commitment. HGS-M4 could only
            // ever commit full covers, so counting lines inside the coversAll branch was correct
            // there; here it would silently under-report every partial draw and starve lambda's
            // calibration. Line granularity guarantees every committed position IS a full line.
            st.ClosedLinesThisEpoch += linesClosedThisOrder;
            if (coversAll)
            {
                st.Committed.Add(order);
                st.CompletedOrdersThisEpoch += 1;
            }
```

`ApplyLineMove` 則把單一 move 包成 `CommitParts` 吃的 `parts` 格式：

```csharp
        /// <summary>Applies one accepted draw-line move through the shared commit path.</summary>
        private void ApplyLineMove(M5EpochState st, M5LineMove mv)
        {
            var part = new Dictionary<ItemDescription, int>();
            part[mv.Sku] = mv.Units;
            var parts = new List<KeyValuePair<int, Dictionary<ItemDescription, int>>>();
            parts.Add(new KeyValuePair<int, Dictionary<ItemDescription, int>>(mv.StationIndex, part));
            CommitParts(st, mv.Order, parts, mv.Pod);
            st.OrderSlotAt.Add(OrderStationKey(mv.Order, mv.StationIndex));
        }
```

⚠️ **槽位重複扣減的陷阱**：`CommitParts` 每個 part 都做 `st.FreeSlots[part.Key]--`。同一張單在同一站台被取第二行時**不該再扣一次槽**。請在複製來的 `CommitParts` 中把該行改為：
```csharp
                if (!st.OrderSlotAt.Contains(OrderStationKey(order, part.Key)))
                    st.FreeSlots[part.Key]--;
```
並確認 `ApplyLineMove` 在 `CommitParts` **之後**才登記 `OrderSlotAt`（如上）。

⚠️ **子單重複建立的陷阱**：`CommitParts` 對非 fastPath 一律 `Order.CreateSplitChild`。同一張母單在同一站台取第二行時會再生一個子單。這是可接受的（每個子單各自 consolidation），但**必須確認 `st.Result.Allocations` 不會為同一 (單, 站) 重複呼叫 `AllocateOrder`**——實作者請在 Task 7 的提交迴圈中對 `Allocations` 去重，或確認引擎對重複 `AllocateOrder` 是冪等的。**這一點必須實測驗證，不得假設。**

---

## Task 7 — `DecideAboutPendingOrders`、λ 外層迭代與決策日誌

以 `GreedyM4GManager.cs` line 633 起的 `DecideAboutPendingOrders` 為藍本，做三處改動：

1. **移除機器人守衛**。原本是
   ```csharp
   if (Ra.Count() > 0 && pendingOrders.Count > 0 && Cs.Count > 0 && Cs.Values.Any(v => v > 0) && allPods.Count > 0)
   ```
   改為（取行動作不需要機器人，只有派車需要）：
   ```csharp
   if (pendingOrders.Count > 0 && Cs.Count > 0 && Cs.Values.Any(v => v > 0) && allPods.Count > 0)
   ```

2. **以 `GreedyBuild` 取代 `CompletionSweep` + `DispatchLoop`**，外面包 λ 迭代：
   - 用 `lambda0 = _pricing.Lambda(cumDist)` 建一次
   - 讀回 `D*`（本次派車的距離總和）與 `V* = (D* - obj) / lambda`，其中 `obj = D* + Σ(所有已採納動作的 Delta 中的價值部分)`
   - `lambdaNext = D* / V*`，重建整個 epoch（**必須從乾淨的 `BuildEpochState` 重來**，不可在已修改的 `st` 上疊加）
   - 迭代至 `|obj| <= LambdaTolerance`、`V* <= 0`、或達 `LambdaIterations`
   - **採納最後一個 `V* > 0` 的建構結果**（比照 M4G 對退化解的防護：`M4GManager.cs` 的 Dinkelbach 迴圈註解說明過，無防護時整場會塌成 0 訂單）

3. **決策日誌** `hgs_m5_decision_log.csv`，欄位：
   ```
   decision,time,pendingOrders,stationsWithCap,podsInModel,dispatched,children,fastPath,units,
   decisionSec,lambda,mu,rho,epsilon,closedLines,completedOrders,lambdaIters,lambdaStart,lambdaEnd,objective
   ```
   **不要寫 `delta` 欄**——HGS-M5 不使用它，寫出來會誤導後續分析。

---

## Task 8 — 組態臂

用 PowerShell（非 sed）由 `Material/Instances/CoreBenchmark/small/hgs_m4.xconf` 產生 `hgs_m5.xconf`，**只改兩處**：
- `<OrderBatchingConfig xsi:type="GreedyM4GConfiguration">` → `xsi:type="GreedyM5Configuration"`
- 頂層 `<Name>` → `hgs_m5`

**byte-clean 驗收**：與母本 diff 僅上述兩行；`file` 顯示 CRLF。

---

## Task 9 — 驗證

**9a. 對照臂零漂移**：確認 `GreedyM4GManager.cs` 的 diff 為零，並重跑 `hgs_m4.xconf` seed 0，與 `out/hgs2h_s0` 逐位比對（footprint 排除 `Memory*` / `RealTime*` / `Timing*` 後全等）。**不過就是有東西被誤改了。**

**9b. 主驗收**：跑 `hgs_m5.xconf` seed 0–4，基準條件同 Global Constraints §7。

| 判準 | 門檻 |
|---|---|
| **前提：處理件數** | 與 M4G（1429.4）打平 ±0.5%。**件數掉了效率數字一律不算** |
| 必要：每件能耗 | 顯著低於 HGS-M4 的 0.769，朝 M4G 的 0.482 移動 |
| 必要：牆鐘 | 數十秒量級（M4G 是 804 s） |
| 必要：可行性 | 決策日誌的 `objective` 恆 $\ge$ 同決策 M4G 的目標值。**出現 $<$ 代表可行性被破壞，是 bug，必須查到底** |
| 觀察 | WIP / 周轉 / 逾期是否向 M4G 靠攏 |

**9c. 消融表**（論文用）：

| | 精解 | 貪婪 |
|---|---|---|
| 不跨期拆單 | M1G-a | HGS-M4 |
| 跨期拆單 | M4G | **HGS-M5** |

---

## 前置事項（本計畫之外，但影響 9b 的「可行性」判準）

`LinearModel` 未設 `MIPGap`（Gurobi 預設 1e-4 相對間隙），所以 M4G 目前給的是近似最優。「HGS-M5 離最優多遠」要成立，需先設 `MIPGap = 0` 並重新驗證 M4G 基準是否漂移。**建議在 Task 9 之前處理。**

---

## 已知風險（規格 §6 摘要）

| 風險 | 應對 |
|---|---|
| 貪婪把槽位浪費在低價值部分滿足 | 先實測。若發生，只在**同 Δ 值**下 tie-break 偏好能完成整單者，不改目標式 |
| 周轉時間 / 逾期跟著 M4G 惡化 | 預期中的科學結果，不是 bug。WIP 定價已證實救不了（5 seeds，p > 0.10） |
| `EvaluateDispatch` 的前瞻掃描過慢 | 沿用既有 `st.Avail` / `OiSKU` 索引；必要時對候選貨架距離預篩，**但必須 log 被篩掉的數量，不得靜默截斷** |
| 同一行跨貨架湊滿無法表達 | 已申報的解空間縮減（規格 §2.3），歸入 gap 來源分析 |
