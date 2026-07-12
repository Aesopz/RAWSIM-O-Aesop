using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using System;
using System.Collections.Generic;
using System.Linq;
using static RAWSimO.Core.Management.ResourceManager;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pod-Value Greedy Splitting (PVGS): fast heuristic counterpart of SplitM1GExact,
    /// positioned as HADGS is to M1G. Pod-centric greedy over a residual-coverage value
    /// index with exact working-copy ledgers - no Gurobi. Independent sibling of the other
    /// split managers - mirrors M1GManager directly, modifies none of them.
    /// See docs/2026-07-07-pvgs-fast-heuristic-discussion.md and the plan's Design Decisions.
    /// </summary>
    public class PVGSManager : M1GManager
    {
        /// <summary>
        /// Creates a new instance of this controller.
        /// </summary>
        /// <param name="instance">The instance this controller belongs to.</param>
        public PVGSManager(Instance instance) : base(instance)
        {
            _config = instance.ControllerConfig.OrderBatchingConfig as PVGSConfiguration;
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }

        /// <summary>
        /// The PVGS-specific config of this controller.
        /// </summary>
        private PVGSConfiguration _config;

        /// <summary>
        /// Shared consolidation CSV logger (splitorders.csv).
        /// </summary>
        private SplitConsolidationLogger _logger;

        /// <summary>
        /// Order enters Od (urgent-order set) if due within this many seconds (mirrors the
        /// base's private DueTimeOrderofMP, copied because it's private in M1GManager).
        /// </summary>
        private static readonly double _dueTimeOrderofMP = TimeSpan.FromMinutes(30).TotalSeconds;

        /// <summary>
        /// Residual-demand version of GenerateOiSKU (mirrors the private Spec 2/3 helper).
        /// </summary>
        private Dictionary<ItemDescription, List<Order>> GenerateOiSKUSplit(HashSet<Order> pendingOrders)
        {
            Dictionary<ItemDescription, List<Order>> OiSKU = new Dictionary<ItemDescription, List<Order>>();
            foreach (var order in pendingOrders)
            {
                foreach (var sku in order.RemainingPositions)
                {
                    if (OiSKU.ContainsKey(sku.Key))
                        OiSKU[sku.Key].Add(order);
                    else
                        OiSKU.Add(sku.Key, new List<Order>() { order });
                }
            }
            return OiSKU;
        }

        /// <summary>
        /// Residual-demand version of GenerateOd (mirrors the private Spec 2/3 helper).
        /// </summary>
        private HashSet<Order> GenerateOdSplit(HashSet<Order> pendingOrders, Dictionary<ItemDescription, List<Pod>> PiSKU)
        {
            HashSet<Order> Od = new HashSet<Order>();
            foreach (Order order in pendingOrders)
            {
                order.Timestay = order.DueTime - (Instance.SettingConfig.StartTime.AddSeconds(Convert.ToInt32(Instance.Controller.CurrentTime)) - order.TimePlaced).TotalSeconds;
                if (order.Timestay < _dueTimeOrderofMP)
                {
                    bool Isadd = true;
                    foreach (var sku in order.RemainingPositions)
                    {
                        if (PiSKU.ContainsKey(sku.Key) && PiSKU[sku.Key].All(v => v.CountAvailable(sku.Key) >= sku.Value && Instance.ResourceManager.UnusedPods.Contains(v)))
                            continue;
                        else
                            Isadd = false;
                    }
                    if (Isadd)
                        Od.Add(order);
                }
            }
            int i = 0;
            foreach (Order order in pendingOrders.OrderBy(v => v.Timestay).ThenBy(u => u.DueTime))
            {
                order.sequence = i;
                i++;
            }
            return Od;
        }

        /// <summary>
        /// The committed outcome of one PVGS epoch, consumed by DecideAboutPendingOrders.
        /// </summary>
        private class PvgsResult
        {
            public Dictionary<Symbol, int> NewZiops = new Dictionary<Symbol, int>();
            public List<Symbol> Allocations = new List<Symbol>();
            public HashSet<Order> SplitParents = new HashSet<Order>();
        }

        /// <summary>
        /// Mutable working state of one PVGS epoch. All quantity books are working COPIES
        /// seeded from real pod inventories at epoch start; commits decrement them in lock
        /// step with the real Ziops/ledger writes, so plans can never over-commit reality.
        /// </summary>
        private class PvgsEpochState
        {
            public List<OutputStation> StationList;
            public int[] FreeSlots;
            public List<HashSet<Pod>> PodsAt;
            public Dictionary<Pod, Dictionary<ItemDescription, int>> Avail;
            public List<Dictionary<ItemDescription, int>> PerStationAvail;
            public Dictionary<ItemDescription, int> ResidualTotals;
            public Dictionary<ItemDescription, int> SupplyTotals;
            public Dictionary<ItemDescription, int> PoolTotals; // CF mode only: whole-backlog demand pool (first-stage admission, no Od shrink)
            public Dictionary<Order, Dictionary<ItemDescription, int>> Residuals;
            public List<Order> ScanOrder;
            public HashSet<Order> Committed = new HashSet<Order>();
            public HashSet<Order> PartialThisEpoch = new HashSet<Order>();
            public List<Bot> FreeBots;
            public PvgsResult Result = new PvgsResult();
            public int FastPathCount;
            public int ChildCount;
            public int UnitsAssigned;
            public int Dispatched;
        }

        /// <summary>
        /// Reads a pod's remaining working-copy availability for one SKU (0 if absent).
        /// </summary>
        private static int GetAvail(PvgsEpochState st, Pod pod, ItemDescription sku)
        {
            Dictionary<ItemDescription, int> podAvail;
            int have;
            return st.Avail.TryGetValue(pod, out podAvail) && podAvail.TryGetValue(sku, out have) ? have : 0;
        }

        /// <summary>
        /// PVGS snapshot: identical to Spec 3's InitializeSplitExact minus the MILP variable
        /// name generation (PVGS builds no model). Residual-demand based throughout; the
        /// M1e/M2e entry filters mirror the exact managers so ablation comparisons are
        /// apples-to-apples.
        /// </summary>
        private HashSet<Pod> InitializePvgs(out Dictionary<ItemDescription, List<Pod>> PiSKU, out Dictionary<ItemDescription, List<Order>> OiSKU,
            out Dictionary<OutputStation, int> Cs, out HashSet<Order> pendingOrders,
            out Dictionary<OutputStation, HashSet<Pod>> inboundPods, out HashSet<Bot> Ra, out HashSet<Bot> Rb,
            out HashSet<Bot> R, out HashSet<Pod> Pb, out HashSet<Pod> Pa, out Dictionary<Pod, Bot> PodToBot,
            out Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            HashSet<Order> pendingOrders1 = _config != null && _config.CrossTime
                ? new HashSet<Order>(_pendingOrders.Where(o => o.RemainingPositions.Any(p => Instance.StockInfo.GetActualStock(p.Key) >= 1)))
                : new HashSet<Order>(_pendingOrders.Where(o => o.RemainingPositions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value)));
            OiSKU = GenerateOiSKUSplit(pendingOrders1);
            Cs = GenerateCs();
            inboundPods = GeneratePs(Cs);
            HashSet<ItemDescription> ItemofOiSKU = new HashSet<ItemDescription>(OiSKU.Keys);
            HashSet<Pod> allPods = new HashSet<Pod>();
            Ra = new HashSet<Bot>();
            Rb = new HashSet<Bot>();
            R = new HashSet<Bot>();
            Pb = new HashSet<Pod>();
            PodToBot = new Dictionary<Pod, Bot>();
            Pa = new HashSet<Pod>();
            foreach (var pods in inboundPods)
            {
                foreach (Pod pod in pods.Value)
                {
                    if (PodToBot.ContainsKey(pod)) continue;
                    if (Instance.ResourceManager._usedPods.ContainsKey(pod))
                    {
                        allPods.Add(pod);
                        Rb.Add(Instance.ResourceManager._usedPods[pod]);
                        R.Add(Instance.ResourceManager._usedPods[pod]);
                        PodToBot[pod] = Instance.ResourceManager._usedPods[pod];
                        Pb.Add(pod);
                    }
                    else if (Instance.ResourceManager.BottoPod.ContainsValue(pod))
                    {
                        var bot = Instance.ResourceManager.BottoPod.Where(V => V.Value.ID == pod.ID).First().Key;
                        allPods.Add(pod);
                        Rb.Add(bot);
                        R.Add(bot);
                        PodToBot[pod] = bot;
                        Pb.Add(pod);
                    }
                    else
                    {
                        // Snapshot can contain a station inbound pod before its pod-bot ownership is visible.
                    }
                }
            }
            foreach (var pod in Instance.ResourceManager.UnusedPods.Where(v =>
                v.IsAvailabletoOiSKU(ItemofOiSKU) &&
                !Instance.ResourceManager.BottoPod.ContainsValue(v) &&
                !Instance.ResourceManager._usedPods.ContainsKey(v) &&
                v.Waypoint != null &&
                v.Waypoint.PodStorageLocation))
            {
                allPods.Add(pod);
                Pa.Add(pod);
            }
            foreach (var bot in Instance._outputstationbots)
            {
                if (bot.Pod == null && !Instance.ResourceManager._usedPods.ContainsValue(bot) && !Instance.ResourceManager.BottoPod.ContainsKey(bot))
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
                else if (bot.Pod == null && !Instance.ResourceManager.BottoPod.ContainsKey(bot) && !Rb.Contains(bot) && bot.CurrentTask is RestTask && bot.GetInfoDestinationWaypoint() == null)
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
                else if (CanUseReturnPendingBot(bot))
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
            }
            PiSKU = GeneratePiSKU(allPods);
            if (_config != null && _config.CrossTime)
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.Any(p => IsAvailabletoPiSKU(p.Key) >= 1)));
            else
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.All(p => IsAvailabletoPiSKU(p.Key) >= p.Value)));
            HashSet<Order> Od = GenerateOdSplit(pendingOrders, PiSKU);
            if (Od.Count > Cs.Values.Sum())
                pendingOrders = new HashSet<Order>(Od);
            OiSKU = GenerateOiSKUSplit(pendingOrders);
            // PVGS-E snapshot parity (Task 3 audit): the exact model instantiates q-variables
            // only for pods carrying a SKU of the FINAL admitted order set - narrow Pa the
            // same way so the greedy cannot claim pods the MILP would never see. E-mode only:
            // regular PVGS keeps its historical (wider) candidate set bit-identically.
            if (_config != null && _config.ExactAlignedScoring)
            {
                HashSet<ItemDescription> finalSkus = new HashSet<ItemDescription>(OiSKU.Keys);
                Pa.RemoveWhere(p => !p.IsAvailabletoOiSKU(finalSkus));
            }
            residuals = pendingOrders.ToDictionary(o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            return allPods;
        }

        /// <summary>
        /// Seeds the epoch working state: per-pod availability copies, per-station pod sets
        /// (inherited Pb pods only - each pinned to its first inbound station, mirroring the
        /// exact model's eshi7 fixing), station-aggregated availability, residual/supply
        /// totals for the value index, and the urgency-ordered scan list.
        /// </summary>
        private PvgsEpochState BuildEpochState(HashSet<Pod> allPods, Dictionary<OutputStation, int> Cs,
            Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Pod> Pb, HashSet<Bot> Ra,
            HashSet<Order> pendingOrders, Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            PvgsEpochState st = new PvgsEpochState();
            st.StationList = Cs.Keys.OrderBy(s => s.ID).ToList();
            st.FreeSlots = st.StationList.Select(s => Cs[s]).ToArray();
            st.PodsAt = st.StationList.Select(s => new HashSet<Pod>()).ToList();
            st.Avail = new Dictionary<Pod, Dictionary<ItemDescription, int>>();
            foreach (var pod in allPods)
            {
                var d = new Dictionary<ItemDescription, int>();
                foreach (var sku in pod.ItemDescriptionsContained)
                {
                    int a = pod.CountAvailable(sku);
                    if (a > 0) d[sku] = a;
                }
                st.Avail[pod] = d;
            }
            var seen = new HashSet<Pod>();
            for (int s = 0; s < st.StationList.Count; s++)
            {
                HashSet<Pod> inbound;
                if (inboundPods.TryGetValue(st.StationList[s], out inbound))
                    foreach (var pod in inbound)
                        if (Pb.Contains(pod) && seen.Add(pod))
                            st.PodsAt[s].Add(pod);
            }
            st.PerStationAvail = new List<Dictionary<ItemDescription, int>>();
            for (int s = 0; s < st.StationList.Count; s++)
            {
                var agg = new Dictionary<ItemDescription, int>();
                foreach (var pod in st.PodsAt[s])
                    foreach (var e in st.Avail[pod])
                    {
                        int cur;
                        agg[e.Key] = (agg.TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
                    }
                st.PerStationAvail.Add(agg);
            }
            st.Residuals = residuals;
            st.ResidualTotals = new Dictionary<ItemDescription, int>();
            foreach (var r in residuals.Values)
                foreach (var e in r)
                {
                    int cur;
                    st.ResidualTotals[e.Key] = (st.ResidualTotals.TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
                }
            st.SupplyTotals = new Dictionary<ItemDescription, int>();
            foreach (var pa in st.Avail.Values)
                foreach (var e in pa)
                {
                    int cur;
                    st.SupplyTotals[e.Key] = (st.SupplyTotals.TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
                }
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
            st.ScanOrder = pendingOrders.OrderBy(o => o.Timestay).ThenBy(o => o.DueTime).ToList();
            st.FreeBots = new List<Bot>(Ra);
            return st;
        }

        /// <summary>
        /// Commits a plan (full or partial) for one order: fast path (single part, full
        /// coverage, never split before) allocates the parent directly; otherwise each part
        /// becomes a child via the Spec 1 enabler pipeline. Pod-level claims are assembled
        /// greedily from the working copies (preferring the newly dispatched pod so its trip
        /// is not wasted - the soft eshi13' analogue) and written to Ziops in lock step with
        /// the working-copy decrements, so books and reality cannot diverge.
        /// </summary>
        private void CommitParts(PvgsEpochState st, Order order, List<KeyValuePair<int, Dictionary<ItemDescription, int>>> parts, Pod preferPod)
        {
            int partUnits = parts.Sum(p => p.Value.Values.Sum());
            int residUnits = st.Residuals[order].Values.Sum();
            bool coversAll = partUnits == residUnits;
            bool fastPath = coversAll && parts.Count == 1 && !order.IsSplitParent;
            foreach (var part in parts)
            {
                OutputStation station = st.StationList[part.Key];
                Order target;
                if (fastPath)
                {
                    target = order;
                    st.FastPathCount++;
                }
                else
                {
                    target = Order.CreateSplitChild(order, part.Value);
                    target.ID = idoforder++;
                    Instance.ResourceManager.TransferExtractRequests(order, target);
                    st.ChildCount++;
                }
                st.Result.Allocations.Add(new Symbol { order = target, outputstation = station });
                foreach (var pos in part.Value)
                {
                    int need = pos.Value;
                    foreach (var pod in st.PodsAt[part.Key].OrderBy(p => p == preferPod ? 0 : 1).ThenByDescending(p => GetAvail(st, p, pos.Key)).ToList())
                    {
                        if (need == 0)
                            break;
                        int have = GetAvail(st, pod, pos.Key);
                        if (have <= 0)
                            continue;
                        int take = Math.Min(have, need);
                        Symbol name = new Symbol
                        {
                            pod = pod, order = target, outputstation = station, skui = pos.Key,
                            name = "ziops" + "_" + pos.Key.ID.ToString() + "_" + target.ID.ToString() + "_" + pod.ID.ToString() + "_" + station.ID.ToString()
                        };
                        st.Result.NewZiops.Add(name, take);
                        Instance.ResourceManager._Ziops[station].Add(name, take);
                        st.Avail[pod][pos.Key] -= take;
                        st.PerStationAvail[part.Key][pos.Key] -= take;
                        int curTotal;
                        if (st.ResidualTotals.TryGetValue(pos.Key, out curTotal))
                            st.ResidualTotals[pos.Key] = Math.Max(0, curTotal - take);
                        int curPool;
                        if (st.PoolTotals != null && st.PoolTotals.TryGetValue(pos.Key, out curPool))
                            st.PoolTotals[pos.Key] = Math.Max(0, curPool - take);
                        need -= take;
                    }
                    if (need != 0)
                        throw new InvalidOperationException("PVGS claim assembly under-covered a planned part - working-state books diverged!");
                    st.Residuals[order][pos.Key] -= pos.Value;
                    st.UnitsAssigned += pos.Value;
                }
                st.FreeSlots[part.Key]--;
            }
            if (!fastPath)
                st.Result.SplitParents.Add(order);
            if (coversAll)
                st.Committed.Add(order);
        }

        /// <summary>
        /// Phase A / re-sweep: commits every not-yet-committed order whose residual can be
        /// FULLY covered by the pods currently at (or dispatched to) stations. After this
        /// returns, no uncommitted order is completable - which is what makes the dispatch
        /// loop's "new completions" attribution to a candidate pod sound.
        /// </summary>
        private int CompletionSweep(PvgsEpochState st, Pod preferPod)
        {
            int committed = 0;
            foreach (var order in st.ScanOrder)
            {
                if (st.Committed.Contains(order))
                    continue;
                var residual = st.Residuals[order].Where(p => p.Value > 0).ToList();
                if (residual.Count == 0)
                {
                    st.Committed.Add(order);
                    continue;
                }
                bool[] slotFree = st.FreeSlots.Select(f => f > 0).ToArray();
                var parts = PvgsStationSplitPlanner.PlanCompletion(residual, st.PerStationAvail, slotFree);
                if (parts == null)
                    continue;
                // NoSplit control arm: only single-station full-order commits (HADGS-equivalent
                // semantics) - cross-station splits are rejected, not committed.
                if (_config != null && _config.DisableSplitting && parts.Count > 1)
                    continue;
                CommitParts(st, order, parts, preferPod);
                committed++;
            }
            return committed;
        }

        /// <summary>
        /// M2e only: fills remaining slots with partial children. Open split parents scan
        /// first (closing consolidation tails), then by urgency; a new partial child needs
        /// at least MinPartialUnits units and each order opens at most one new partial child
        /// per epoch (anti-fragmentation guardrails).
        /// </summary>
        private int PartialSweep(PvgsEpochState st)
        {
            if (_config != null && _config.DisableSplitting)
                return 0;
            int created = 0;
            var scan = st.ScanOrder
                .Where(o => !st.Committed.Contains(o) && !st.PartialThisEpoch.Contains(o))
                .OrderBy(o => o.IsSplitParent ? 0 : 1).ThenBy(o => o.Timestay)
                .ToList();
            foreach (var order in scan)
            {
                if (!st.FreeSlots.Any(f => f > 0))
                    break;
                var residual = st.Residuals[order].Where(p => p.Value > 0).ToList();
                if (residual.Count == 0)
                    continue;
                bool[] slotFree = st.FreeSlots.Select(f => f > 0).ToArray();
                int stationIndex;
                var part = PvgsStationSplitPlanner.PlanPartial(residual, st.PerStationAvail, slotFree, _config != null ? _config.MinPartialUnits : 2, out stationIndex);
                if (part == null)
                    continue;
                CommitParts(st, order,
                    new List<KeyValuePair<int, Dictionary<ItemDescription, int>>>
                    { new KeyValuePair<int, Dictionary<ItemDescription, int>>(stationIndex, part) },
                    null);
                st.PartialThisEpoch.Add(order);
                created++;
            }
            return created;
        }

        /// <summary>
        /// PVGS-E epsilon layer: after all completions and dispatches, fill remaining free
        /// slots with the largest drawable partial parts from pods already at stations
        /// (committed supply only - a new trip can never pay for itself at epsilon scale).
        /// The greedy of the exact objective's UnitDrawReward term. Unlike PartialSweep
        /// there is no MinPartialUnits floor and no one-child-per-order guard: the exact
        /// model prices every drawn unit at epsilon with no extra gates, and this runs only
        /// after every rewarded (completion) action has taken its slots. Active only in
        /// ExactAlignedScoring mode with UnitDrawReward != 0.
        /// </summary>
        private int SqueezeSweep(PvgsEpochState st)
        {
            if (_config != null && _config.DisableSplitting)
                return 0;
            int created = 0;
            while (st.FreeSlots.Any(f => f > 0))
            {
                Order bestOrder = null;
                Dictionary<ItemDescription, int> bestPart = null;
                int bestStation = -1, bestUnits = 0;
                foreach (var order in st.ScanOrder)
                {
                    if (st.Committed.Contains(order))
                        continue;
                    var residual = st.Residuals[order].Where(p => p.Value > 0).ToList();
                    if (residual.Count == 0)
                        continue;
                    bool[] slotFree = st.FreeSlots.Select(f => f > 0).ToArray();
                    int stationIndex;
                    var part = PvgsExactAligned.PlanSqueeze(residual, st.PerStationAvail, slotFree, out stationIndex);
                    if (part == null)
                        continue;
                    int units = part.Values.Sum();
                    if (units > bestUnits)
                    {
                        bestUnits = units;
                        bestOrder = order;
                        bestPart = part;
                        bestStation = stationIndex;
                    }
                }
                if (bestOrder == null)
                    break;
                CommitParts(st, bestOrder,
                    new List<KeyValuePair<int, Dictionary<ItemDescription, int>>>
                    { new KeyValuePair<int, Dictionary<ItemDescription, int>>(bestStation, bestPart) },
                    null);
                created++;
            }
            return created;
        }

        /// <summary>
        /// Phase B: value-driven pod dispatch. Shortlists candidate storage pods by the
        /// residual-coverage value index, scores (pod, station) pairs by newly completable
        /// orders (sound attribution: the preceding sweep guarantees nothing was completable
        /// before) + M2e partial-progress value + parent-closing bonus - distance, and
        /// commits the best dispatch (bot claim + inbound registration, mirroring the exact
        /// manager's Ra claiming block). Re-sweeps after every dispatch. Scoring treats each
        /// order's completability independently (slot interactions among simultaneous new
        /// completions are ignored in the SCORE; the sweep enforces real slots on COMMIT).
        /// </summary>
        private void DispatchLoop(PvgsEpochState st, HashSet<Pod> paCandidates, Dictionary<ItemDescription, List<Order>> OiSKU, bool crossTime)
        {
            var dispatched = new HashSet<Pod>();
            while (st.FreeBots.Count > 0 && st.FreeSlots.Any(f => f > 0))
            {
                var shortlist = paCandidates
                    .Where(p => !dispatched.Contains(p))
                    .Select(p => new { Pod = p, Value = PvgsValueIndex.ComputeValue(st.Avail[p], st.ResidualTotals, st.SupplyTotals, _config != null ? _config.ScarcityBeta : 1.0) })
                    .Where(c => c.Value > 0)
                    .OrderByDescending(c => c.Value)
                    .Take(_config != null && _config.ExactAlignedScoring ? int.MaxValue : (_config != null ? _config.ShortlistK : 15))
                    .ToList();
                if (shortlist.Count == 0)
                    break;
                double completionWeight = _config != null ? _config.CompletionWeight : 40;
                double distanceWeight = _config != null ? _config.DistanceWeight : 1;
                double partialUnitWeight = _config != null ? _config.PartialUnitWeight : 1;
                double parentClosingBonus = _config != null ? _config.ParentClosingBonus : 20;
                bool exactAligned = _config != null && _config.ExactAlignedScoring;
                double podTripFixedCost = _config != null ? _config.PodTripFixedCost : 0;
                double unitDrawReward = _config != null ? _config.UnitDrawReward : 0;
                bool coverageFirst = exactAligned && _config != null && _config.CoverageFirstScoring;
                double betaPool = _config != null ? _config.PoolCoverWeight : 0;
                double epsPod = _config != null ? _config.PodSelectTiebreakCost : 0;
                double bestScore = 0;
                double bestPrimary = 0;
                double bestDist = double.PositiveInfinity;
                Pod bestPod = null;
                int bestStation = -1;
                Bot bestBot = null;
                foreach (var cand in shortlist)
                {
                    Bot bot = null;
                    double dBot = double.PositiveInfinity;
                    foreach (var b in st.FreeBots)
                    {
                        double d = EstimateBotPodDistance(b, cand.Pod);
                        if (d < dBot) { dBot = d; bot = b; }
                    }
                    if (bot == null)
                        break;
                    var touched = new HashSet<Order>();
                    foreach (var sku in st.Avail[cand.Pod].Keys)
                    {
                        List<Order> lst;
                        if (OiSKU.TryGetValue(sku, out lst))
                            foreach (var o in lst)
                                if (!st.Committed.Contains(o) && st.Residuals.ContainsKey(o))
                                    touched.Add(o);
                    }
                    for (int s = 0; s < st.StationList.Count; s++)
                    {
                        if (st.FreeSlots[s] <= 0)
                            continue;
                        double dPod = EstimatePodStationDistance(cand.Pod, st.StationList[s]);
                        var merged = new Dictionary<ItemDescription, int>(st.PerStationAvail[s]);
                        foreach (var e in st.Avail[cand.Pod])
                        {
                            int cur;
                            merged[e.Key] = (merged.TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
                        }
                        var hypo = new List<Dictionary<ItemDescription, int>>(st.PerStationAvail);
                        hypo[s] = merged;
                        bool[] slotFree = st.FreeSlots.Select(f => f > 0).ToArray();
                        int newCompletions = 0, parentCloses = 0, newUnits = 0;
                        foreach (var o in touched)
                        {
                            var residual = st.Residuals[o].Where(p => p.Value > 0).ToList();
                            if (residual.Count == 0)
                                continue;
                            var plan = PvgsStationSplitPlanner.PlanCompletion(residual, hypo, slotFree);
                            // NoSplit arm: scoring must count only what the sweep would commit
                            if (plan != null && (_config == null || !_config.DisableSplitting || plan.Count == 1))
                            {
                                newCompletions++;
                                if (o.IsSplitParent)
                                    parentCloses++;
                                newUnits += residual.Sum(p => p.Value);
                            }
                        }
                        // E-mode: the score is the exact objective's marginal value of this
                        // dispatch - no PartialUnitWeight, no ParentClosingBonus (terms the
                        // exact model does not have); w4 and epsilon enter with their exact-
                        // model semantics. Regular mode: unchanged legacy score.
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
                    }
                }
                if (bestPod == null)
                    break;
                // Commit the dispatch (mirrors the exact manager's Ra claiming block).
                Instance.ResourceManager.BottoPod.Add(bestBot, bestPod);
                Instance.ResourceManager.ClaimPod(bestPod, bestBot, BotTaskType.Extract);
                st.StationList[bestStation].RegisterInboundPod(bestPod);
                st.PodsAt[bestStation].Add(bestPod);
                foreach (var e in st.Avail[bestPod])
                {
                    int cur;
                    st.PerStationAvail[bestStation][e.Key] = (st.PerStationAvail[bestStation].TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
                }
                st.FreeBots.Remove(bestBot);
                dispatched.Add(bestPod);
                st.Dispatched++;
                CompletionSweep(st, bestPod);
                if (crossTime && !(_config != null && _config.ExactAlignedScoring))
                    PartialSweep(st);
            }
        }

        // ── Per-decision summary logger (speed evidence for the acceptance criteria) ──
        private System.IO.StreamWriter _pvgsDecisionLog;
        private int _pvgsDecisionIndex = 0;
        private void WritePvgsDecisionLog(double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int dispatched, int nChildren, int nFastPath, int unitsAssigned, double decisionSec)
        {
            if (_pvgsDecisionLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _pvgsDecisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "pvgs_decision_log.csv"), false) { AutoFlush = true };
                _pvgsDecisionLog.WriteLine("decision,time,pendingOrders,stationsWithCap,podsInModel,dispatched,children,fastPath,units,decisionSec");
            }
            _pvgsDecisionLog.WriteLine(string.Join(",", new string[] {
                _pvgsDecisionIndex.ToString(),
                time.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pendingOrdersN.ToString(),
                stationsWithCap.ToString(),
                podsInModel.ToString(),
                dispatched.ToString(),
                nChildren.ToString(),
                nFastPath.ToString(),
                unitsAssigned.ToString(),
                decisionSec.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
            _pvgsDecisionIndex++;
        }

        /// <summary>
        /// This is called to decide about potentially pending orders (PVGS heuristic).
        /// Snapshot -> Phase A completion sweep (inherited pods only) -> Phase B value-driven
        /// dispatch loop (which re-sweeps after every dispatch) -> M2e trailing partial sweep
        /// -> commit wiring in strict base parity: JustRegisterItem over all new Ziops FIRST,
        /// then AllocateOrder, then split-parent bookkeeping.
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
            DateTime A = DateTime.Now;
            Dictionary<ItemDescription, List<Pod>> PiSKU;
            Dictionary<ItemDescription, List<Order>> OiSKU;
            Dictionary<OutputStation, int> Cs;
            Dictionary<OutputStation, HashSet<Pod>> inboundPods;
            HashSet<Order> pendingOrders;
            HashSet<Bot> Ra;
            HashSet<Bot> Rb;
            HashSet<Bot> R;
            HashSet<Pod> Pb;
            HashSet<Pod> Pa;
            Dictionary<Pod, Bot> PodToBot;
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals;
            HashSet<Pod> allPods = InitializePvgs(out PiSKU, out OiSKU, out Cs, out pendingOrders,
                out inboundPods, out Ra, out Rb, out R, out Pb, out Pa, out PodToBot, out residuals);
            if (R.Count() > 0 && pendingOrders.Count > 0)
            {
                bool crossTime = _config != null && _config.CrossTime;
                PvgsEpochState st = BuildEpochState(allPods, Cs, inboundPods, Pb, Ra, pendingOrders, residuals);
                CompletionSweep(st, null);
                DispatchLoop(st, Pa, OiSKU, crossTime);
                bool exactAligned = _config != null && _config.ExactAlignedScoring;
                if (crossTime && !exactAligned)
                    PartialSweep(st);
                if (exactAligned && _config.UnitDrawReward != 0)
                    SqueezeSweep(st);
                foreach (var symbol in st.Result.NewZiops)
                {
                    for (int i = 0; i < symbol.Value; i++)
                        symbol.Key.pod.JustRegisterItem(symbol.Key.skui);
                }
                foreach (var alloc in st.Result.Allocations)
                {
                    AllocateOrder(alloc.order, alloc.outputstation);
                    Instance.StatCustomControllerInfo.CustomLogOB1++;
                }
                foreach (var parent in st.Result.SplitParents)
                {
                    if (double.IsPositiveInfinity(parent.TimeStampSubmit))
                        parent.TimeStampSubmit = Instance.Controller.CurrentTime;
                    if (parent.IsFullyClaimed)
                    {
                        _pendingOrders.Remove(parent);
                        (Instance.ItemManager as ItemManager).TakeAvailableOrder(parent);
                    }
                }
                double decisionSec = (DateTime.Now - A).TotalSeconds;
                WritePvgsDecisionLog(Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, allPods.Count,
                    st.Dispatched, st.ChildCount, st.FastPathCount, st.UnitsAssigned, decisionSec);
                Instance.Observer.TimeOrderBatchingbyMP(decisionSec);
            }
        }
    }
}
