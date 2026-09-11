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
    /// HGS-M4: greedy heuristic counterpart of M4G, as HADGS is to M1G and HGS-M3 is to M3G.
    /// Copies GreedyM3GManager's validated pod-centric greedy engine (snapshot construction,
    /// pod-tier classification, completion sweep, claim commit pipeline) and replaces ONLY the
    /// dispatch score with M4G's self-calibrated metre price list (lambda/mu/delta/rho), shared
    /// with M4GManager via <see cref="M4GPricing"/>/<see cref="IM4GPrices"/>. No supply cap, no
    /// pipeline floor, no lead-time gate and no hand-tuned weight - those are exactly what M4G
    /// removed from M3G, so HGS-M4 must not reintroduce them.
    ///
    /// The two things this manager mirrors from M4GManager rather than from GreedyM3GManager:
    /// (1) the pending-order snapshot sees the WHOLE backlog, never truncated to an urgent
    ///     subset (M4GManager.BuildSnapshot's explicit design note); (2) partial-stock orders
    ///     are admitted with `.Any` (not `.All`), matching M4G's admission rule exactly.
    ///
    /// See docs/superpowers/specs (M4G design) and .superpowers/sdd/hgs-m4-report.md for the
    /// scoring derivation and the deliberate simplifications versus the exact MILP.
    /// </summary>
    public class GreedyM4GManager : M1GManager
    {
        /// <summary>Creates a new instance of this controller.</summary>
        /// <param name="instance">The instance this controller belongs to.</param>
        public GreedyM4GManager(Instance instance) : base(instance)
        {
            _config = instance.ControllerConfig.OrderBatchingConfig as GreedyM4GConfiguration;
            if (_config == null)
                throw new InvalidOperationException("GreedyM4GManager requires a GreedyM4GConfiguration.");
            _pricing = new M4GPricing(_config);
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }

        /// <summary>The HGS-M4-specific config of this controller.</summary>
        private GreedyM4GConfiguration _config;
        /// <summary>Price calibration state, shared implementation with M4GManager.</summary>
        private M4GPricing _pricing;
        /// <summary>
        /// The cumulative distance the self-calibrated prices are denominated in. Default is the
        /// fleet's unrestricted total, which is what every published result used; under
        /// PickDistancePricing it is Extract-task distance only, i.e. the same span the objective's
        /// own travel term prices. See IM4GPrices.PickDistancePricing for the measurement that
        /// motivates the switch.
        /// </summary>
        private double PricingDistance()
        {
            return _config.PickDistancePricing
                ? Instance.StatOverallDistanceTraveledExtract
                : Instance.StatOverallDistanceTraveled;
        }

        /// <summary>Shared consolidation CSV logger (splitorders.csv).</summary>
        private SplitConsolidationLogger _logger;

        /// <summary>
        /// Residual-demand version of GenerateOiSKU (mirrors the private Spec 2/3 helper, same
        /// as GreedyM3GManager's copy).
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
        /// The committed outcome of one epoch, consumed by DecideAboutPendingOrders.
        /// </summary>
        private class M4gResult
        {
            public Dictionary<Symbol, int> NewZiops = new Dictionary<Symbol, int>();
            public List<Symbol> Allocations = new List<Symbol>();
            public HashSet<Order> SplitParents = new HashSet<Order>();
        }

        /// <summary>
        /// Mutable working state of one epoch. All quantity books are working COPIES seeded
        /// from real pod inventories at epoch start; commits decrement them in lock step with
        /// the real Ziops/ledger writes, so plans can never over-commit reality.
        /// </summary>
        private class M4gEpochState
        {
            public List<OutputStation> StationList;
            public int[] FreeSlots;
            public List<HashSet<Pod>> PodsAt;
            public Dictionary<Pod, Bot> PodToBot;
            public Dictionary<Pod, Dictionary<ItemDescription, int>> Avail;
            public List<Dictionary<ItemDescription, int>> PerStationAvail;
            public Dictionary<Order, Dictionary<ItemDescription, int>> Residuals;
            public List<Order> ScanOrder;
            public HashSet<Order> Committed = new HashSet<Order>();
            public List<Bot> FreeBots;
            public M4gResult Result = new M4gResult();
            public int FastPathCount;
            public int ChildCount;
            public int UnitsAssigned;
            public int Dispatched;
            /// <summary>Order lines actually bound (fully drawn) this epoch - lambda's full price.</summary>
            public int ClosedLinesThisEpoch;
            /// <summary>Whole orders whose entire epoch-start residual got bound this epoch - mu's price.</summary>
            public int CompletedOrdersThisEpoch;
            /// <summary>
            /// Order lines that, at the moment a winning dispatch candidate was scored, were
            /// individually fully coverable at that station's merged availability but did not
            /// get bound (either the station ran out of free slots, or an order needed a
            /// multi-station split the single-station scoring check does not attempt). This is
            /// the greedy manager's operational definition of "valued but unbound" (see report):
            /// there is no MILP valuation layer here, so this is read directly off the same
            /// per-candidate check the dispatch score itself uses.
            /// </summary>
            public int ValuedNotBoundLinesThisEpoch;
        }

        /// <summary>Reads a pod's remaining working-copy availability for one SKU (0 if absent).</summary>
        private static int GetAvail(M4gEpochState st, Pod pod, ItemDescription sku)
        {
            Dictionary<ItemDescription, int> podAvail;
            int have;
            return st.Avail.TryGetValue(pod, out podAvail) && podAvail.TryGetValue(sku, out have) ? have : 0;
        }

        /// <summary>
        /// Decision-time snapshot of the world, mirroring M4GManager.BuildSnapshot structurally
        /// (same admission rule: `.Any` partial-stock orders, no urgent-subset truncation, Pa
        /// narrowed to pods carrying a SKU of the FINAL admitted order set) but exposed as the
        /// multiple out-params the epoch engine below (borrowed from GreedyM3GManager) expects.
        /// </summary>
        private HashSet<Pod> InitializeSnapshot(out Dictionary<ItemDescription, List<Pod>> PiSKU,
            out Dictionary<ItemDescription, List<Order>> OiSKU, out Dictionary<OutputStation, int> Cs,
            out HashSet<Order> pendingOrders, out Dictionary<OutputStation, HashSet<Pod>> inboundPods,
            out HashSet<Bot> Ra, out HashSet<Pod> Pb, out HashSet<Pod> Pa, out Dictionary<Pod, Bot> PodToBot,
            out Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            Cs = GenerateCs();
            inboundPods = GeneratePs(Cs);
            HashSet<Pod> allPods = new HashSet<Pod>();
            PodToBot = new Dictionary<Pod, Bot>();
            Pb = new HashSet<Pod>();
            foreach (var pods in inboundPods)
            {
                foreach (Pod pod in pods.Value)
                {
                    if (PodToBot.ContainsKey(pod)) continue;
                    Bot owner = null;
                    if (Instance.ResourceManager._usedPods.ContainsKey(pod))
                        owner = Instance.ResourceManager._usedPods[pod];
                    else if (Instance.ResourceManager.BottoPod.ContainsValue(pod))
                        owner = Instance.ResourceManager.BottoPod.First(v => v.Value.ID == pod.ID).Key;
                    if (owner == null) continue; // ownership not visible yet this tick
                    allPods.Add(pod);
                    PodToBot[pod] = owner;
                    Pb.Add(pod);
                }
            }

            // M4G admission: `.Any` (partial stock in the whole system is enough to consider
            // the order), not `.All` - matches M4GManager.BuildSnapshot exactly.
            HashSet<Order> candidates = new HashSet<Order>(_pendingOrders.Where(o =>
                o.RemainingPositions.Any(p => Instance.StockInfo.GetActualStock(p.Key) >= 1)));
            HashSet<ItemDescription> demanded = new HashSet<ItemDescription>(
                candidates.SelectMany(o => o.RemainingPositions.Select(p => p.Key)));

            Pa = new HashSet<Pod>();
            foreach (var pod in Instance.ResourceManager.UnusedPods.Where(v =>
                v.IsAvailabletoOiSKU(demanded) &&
                !Instance.ResourceManager.BottoPod.ContainsValue(v) &&
                !Instance.ResourceManager._usedPods.ContainsKey(v) &&
                v.Waypoint != null &&
                v.Waypoint.PodStorageLocation))
            {
                Pa.Add(pod);
                allPods.Add(pod);
            }

            Ra = new HashSet<Bot>();
            foreach (var bot in Instance._outputstationbots)
            {
                if (bot.Pod == null && !Instance.ResourceManager._usedPods.ContainsValue(bot) && !Instance.ResourceManager.BottoPod.ContainsKey(bot))
                    Ra.Add(bot);
                else if (bot.Pod == null && !Instance.ResourceManager.BottoPod.ContainsKey(bot) && !PodToBot.ContainsValue(bot) && bot.CurrentTask is RestTask && bot.GetInfoDestinationWaypoint() == null)
                    Ra.Add(bot);
                else if (CanUseReturnPendingBot(bot))
                    Ra.Add(bot);
            }

            PiSKU = GeneratePiSKU(allPods);
            var piSkuCopy = PiSKU; // out params cannot be captured by lambdas (CS1628)
            pendingOrders = new HashSet<Order>(candidates.Where(o => o.RemainingPositions.Any(p => piSkuCopy.ContainsKey(p.Key))));
            OiSKU = GenerateOiSKUSplit(pendingOrders);

            // M4G-faithful Pa narrowing: only pods carrying a SKU of the FINAL admitted order
            // set (mirrors M4GManager.BuildSnapshot's Pa filter and GreedyM3G's E-mode snapshot
            // parity fix - here unconditional, since HGS-M4 has no legacy non-aligned mode).
            HashSet<ItemDescription> finalSkus = new HashSet<ItemDescription>(OiSKU.Keys);
            Pa.RemoveWhere(p => !p.IsAvailabletoOiSKU(finalSkus));

            residuals = pendingOrders.ToDictionary(o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            return allPods;
        }

        /// <summary>
        /// Seeds the epoch working state: per-pod availability copies, per-station pod sets
        /// (inherited Pb pods only - each pinned to its first inbound station), station-
        /// aggregated availability, and the urgency-ordered scan list. Mirrors
        /// GreedyM3GManager.BuildEpochState minus the PVGS-specific value-index bookkeeping
        /// (ResidualTotals/SupplyTotals/PoolTotals) that HGS-M4's price-based score does not use.
        /// </summary>
        private M4gEpochState BuildEpochState(HashSet<Pod> allPods, Dictionary<OutputStation, int> Cs,
            Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Pod> Pb, HashSet<Bot> Ra,
            HashSet<Order> pendingOrders, Dictionary<Order, Dictionary<ItemDescription, int>> residuals,
            Dictionary<Pod, Bot> podToBot)
        {
            M4gEpochState st = new M4gEpochState();
            st.PodToBot = podToBot;
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
            st.ScanOrder = pendingOrders.OrderBy(o => o.Timestay).ThenBy(o => o.DueTime).ToList();
            st.FreeBots = new List<Bot>(Ra);
            return st;
        }

        /// <summary>
        /// (Pod-tier draw preference) Classifies a committed pod at a station: 0 = processing
        /// (its bot is at the station's pick waypoint), 1 = queued (bot at a queue waypoint),
        /// 2 = on-the-way / freshly dispatched / unknown. Identical mirror of
        /// GreedyM3GManager.PodDrawTier / SplitM2eIC's Pp/Pq/on-the-way partition.
        /// </summary>
        private static int PodDrawTier(M4gEpochState st, Pod pod, OutputStation station)
        {
            Bot bot;
            if (st.PodToBot == null || !st.PodToBot.TryGetValue(pod, out bot) || bot == null)
                return 2;
            var wp = bot.CurrentWaypoint;
            if (wp != null && station.Waypoint != null && wp.ID == station.Waypoint.ID)
                return 0;
            if (wp != null && wp.IsQueueWaypoint)
                return 1;
            return 2;
        }

        /// <summary>
        /// Aggregate per-SKU stock currently carried by PROCESSING-tier (tier 0) pods at one
        /// station - the supply the score's rho term rewards draining before leaning on a new
        /// pod's trip.
        /// </summary>
        private static Dictionary<ItemDescription, int> ComputeProcessingAvail(M4gEpochState st, int stationIndex)
        {
            var result = new Dictionary<ItemDescription, int>();
            OutputStation station = st.StationList[stationIndex];
            foreach (var pod in st.PodsAt[stationIndex])
            {
                if (PodDrawTier(st, pod, station) != 0) continue;
                foreach (var e in st.Avail[pod])
                {
                    int cur;
                    result[e.Key] = (result.TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
                }
            }
            return result;
        }

        /// <summary>
        /// Commits a plan (always a full cover of the order's CURRENT residual - see
        /// CompletionSweep) for one order. Pod-level claims are drawn in a fixed preference
        /// order that matches the score's rho assumption: PROCESSING-tier pods first, then
        /// QUEUED/en-route pods, and the just-dispatched candidate pod (<paramref
        /// name="preferPod"/>) LAST of all - i.e. the opposite of GreedyM3GManager's legacy
        /// "prefer the newly dispatched pod" bias. This ordering is hardcoded, not a config
        /// toggle: it is what makes the realised draws match the -rho/+rho asymmetry the score
        /// prices, not a tunable weight.
        /// </summary>
        private void CommitParts(M4gEpochState st, Order order, List<KeyValuePair<int, Dictionary<ItemDescription, int>>> parts, Pod preferPod)
        {
            int partUnits = parts.Sum(p => p.Value.Values.Sum());
            int residUnits = st.Residuals[order].Values.Sum();
            bool coversAll = partUnits == residUnits;
            bool fastPath = coversAll && parts.Count == 1 && !order.IsSplitParent;
            int linesClosedThisOrder = 0;
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
                    foreach (var pod in st.PodsAt[part.Key]
                        .OrderBy(p => PodDrawTier(st, p, station))
                        .ThenBy(p => p == preferPod ? 1 : 0)
                        .ThenByDescending(p => GetAvail(st, p, pos.Key)).ToList())
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
                        need -= take;
                    }
                    if (need != 0)
                        throw new InvalidOperationException("HGS-M4 claim assembly under-covered a planned part - working-state books diverged!");
                    st.Residuals[order][pos.Key] -= pos.Value;
                    st.UnitsAssigned += pos.Value;
                    linesClosedThisOrder++;
                }
                st.FreeSlots[part.Key]--;
            }
            if (!fastPath)
                st.Result.SplitParents.Add(order);
            // (Fill fairness) mirrors M4G's ReleaseParentOnFirstSplit - see M4GManager.CommitM4G.
            if (_config.ReleaseParentOnFirstSplit
                && order.IsSplitParent
                && (Instance.ItemManager as ItemManager).IsOrderAvailable(order))
            {
                (Instance.ItemManager as ItemManager).TakeAvailableOrder(order);
            }
            if (coversAll)
            {
                st.Committed.Add(order);
                st.ClosedLinesThisEpoch += linesClosedThisOrder;
                st.CompletedOrdersThisEpoch += 1;
            }
        }

        /// <summary>
        /// Phase A / re-sweep: commits every not-yet-committed order whose residual can be
        /// FULLY covered (subject to free slots) by the pods currently at (or dispatched to)
        /// stations. Identical in spirit to GreedyM3GManager.CompletionSweep; the "fully
        /// covered" test may split one order across multiple stations, as PlanCompletion always
        /// could - HGS-M4 does not restrict splitting the way NoSplit control arms do.
        /// </summary>
        private int CompletionSweep(M4gEpochState st, Pod preferPod)
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
                CommitParts(st, order, parts, preferPod);
                committed++;
            }
            return committed;
        }

        /// <summary>
        /// Phase B: price-driven pod dispatch. Scores every (storage pod, station, bot)
        /// combination directly against M4G's price list - no scarcity value-index shortlist,
        /// no ShortlistK truncation, no supply cap, no pipeline floor, no lead-time gate (those
        /// are exactly the PVGS/M3G machinery this manager deliberately drops). Re-sweeps after
        /// every dispatch, same as GreedyM3GManager.
        ///
        /// score(pod, station, bot) =
        ///     lambda * boundLines                      (lines this pod closes now, fits free slots)
        ///   + lambda * delta * valuedNotBoundLines      (lines it could close but don't fit)
        ///   + mu * completions                          (whole orders it completes now)
        ///   + rho * unitsFromProcessingTier              (units drawn from the pod being picked now)
        ///   - rho * unitsFromThisNewPod                  (units drawn from the newly fetched pod)
        ///   - (botToPodDistance + podToStationDistance)
        ///
        /// A candidate is dispatched only if its best score is strictly positive - the greedy
        /// equivalent of the MILP never setting xps=1 when doing so cannot possibly help the
        /// (minimisation) objective; this is a rationality threshold, not a tuned constant.
        /// </summary>
        private void DispatchLoop(M4gEpochState st, HashSet<Pod> paCandidates, Dictionary<ItemDescription, List<Order>> OiSKU)
        {
            var dispatched = new HashSet<Pod>();
            while (st.FreeBots.Count > 0 && st.FreeSlots.Any(f => f > 0))
            {
                var candidates = paCandidates.Where(p => !dispatched.Contains(p)).ToList();
                if (candidates.Count == 0)
                    break;

                double cumDist = PricingDistance();
                double lambda = _pricing.Lambda(cumDist);
                double mu = _pricing.Mu(cumDist);
                double delta = _pricing.Delta();
                double rho = _pricing.Rho(cumDist, Instance.StatOverallItemsHandled);

                var processingAvailByStation = new Dictionary<ItemDescription, int>[st.StationList.Count];
                for (int s = 0; s < st.StationList.Count; s++)
                    processingAvailByStation[s] = ComputeProcessingAvail(st, s);

                double bestScore = 0; // strictly-positive gate: 0 means "no dispatch"
                Pod bestPod = null;
                int bestStation = -1;
                Bot bestBot = null;
                int bestValuedNotBoundLines = 0;

                foreach (var pod in candidates)
                {
                    Bot bot = null;
                    double dBot = double.PositiveInfinity;
                    foreach (var b in st.FreeBots)
                    {
                        double d = EstimateBotPodDistance(b, pod);
                        if (d < dBot) { dBot = d; bot = b; }
                    }
                    if (bot == null)
                        continue;

                    var touched = new HashSet<Order>();
                    foreach (var sku in st.Avail[pod].Keys)
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
                        double dPod = EstimatePodStationDistance(pod, st.StationList[s]);

                        var merged = new Dictionary<ItemDescription, int>(st.PerStationAvail[s]);
                        foreach (var e in st.Avail[pod])
                        {
                            int cur;
                            merged[e.Key] = (merged.TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
                        }
                        var hypo = new List<Dictionary<ItemDescription, int>>(st.PerStationAvail);
                        hypo[s] = merged;
                        bool[] slotFree = st.FreeSlots.Select(f => f > 0).ToArray();

                        int completions = 0, boundLines = 0, valuedNotBoundLines = 0;
                        var boundLineList = new List<KeyValuePair<ItemDescription, int>>();
                        foreach (var o in touched)
                        {
                            var residual = st.Residuals[o].Where(p2 => p2.Value > 0).ToList();
                            if (residual.Count == 0)
                                continue;
                            var plan = PvgsStationSplitPlanner.PlanCompletion(residual, hypo, slotFree);
                            if (plan != null)
                            {
                                completions++;
                                boundLines += residual.Count;
                                boundLineList.AddRange(residual);
                            }
                            else
                            {
                                bool closableHere = residual.All(pos =>
                                {
                                    int av;
                                    return merged.TryGetValue(pos.Key, out av) && av >= pos.Value;
                                });
                                if (closableHere)
                                    valuedNotBoundLines += residual.Count;
                            }
                        }

                        // rho attribution: for the lines this candidate would actually bind,
                        // draw PROCESSING-tier supply first, then other pre-existing supply at
                        // the station (queued/en-route, priced free), then the new pod last.
                        int processingUnits = 0, newPodUnits = 0;
                        if (boundLineList.Count > 0)
                        {
                            var processingAvail = processingAvailByStation[s];
                            var preExisting = st.PerStationAvail[s];
                            var podAvail = st.Avail[pod];
                            foreach (var line in boundLineList)
                            {
                                int need = line.Value;
                                int procAv;
                                int fromProc = Math.Min(need, processingAvail.TryGetValue(line.Key, out procAv) ? procAv : 0);
                                processingUnits += fromProc;
                                int remaining = need - fromProc;
                                int preAv;
                                int fromExistingOther = Math.Max(0, Math.Min(remaining,
                                    (preExisting.TryGetValue(line.Key, out preAv) ? preAv : 0) - fromProc));
                                remaining -= fromExistingOther;
                                int podAv2;
                                int fromNewPod = Math.Min(remaining, podAvail.TryGetValue(line.Key, out podAv2) ? podAv2 : 0);
                                newPodUnits += fromNewPod;
                            }
                        }

                        double score = lambda * boundLines + lambda * delta * valuedNotBoundLines
                            + mu * completions
                            + rho * processingUnits - rho * newPodUnits
                            - (dBot + dPod);

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestPod = pod;
                            bestStation = s;
                            bestBot = bot;
                            bestValuedNotBoundLines = valuedNotBoundLines;
                        }
                    }
                }

                if (bestPod == null)
                    break;

                // Commit the dispatch (mirrors the exact manager's Ra claiming block / M4GManager.CommitM4G).
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
                st.ValuedNotBoundLinesThisEpoch += bestValuedNotBoundLines;
                CompletionSweep(st, bestPod);
            }
        }

        // ── Per-decision summary logger (mirrors GreedyM3GManager's speed-evidence log) ──
        private System.IO.StreamWriter _decisionLog;
        private int _decisionIndex = 0;
        private void WriteDecisionLog(double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int dispatched, int nChildren, int nFastPath, int unitsAssigned, double decisionSec,
            double lambda, double mu, double delta, double rho, int closedLines, int valuedNotBoundLines, int completedOrders)
        {
            if (_decisionLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _decisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "hgs_m4_decision_log.csv"), false) { AutoFlush = true };
                _decisionLog.WriteLine("decision,time,pendingOrders,stationsWithCap,podsInModel,dispatched,children,fastPath,units,decisionSec,lambda,mu,delta,rho,closedLines,valuedNotBoundLines,completedOrders");
            }
            _decisionLog.WriteLine(string.Join(",", new string[] {
                _decisionIndex.ToString(),
                time.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pendingOrdersN.ToString(),
                stationsWithCap.ToString(),
                podsInModel.ToString(),
                dispatched.ToString(),
                nChildren.ToString(),
                nFastPath.ToString(),
                unitsAssigned.ToString(),
                decisionSec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                lambda.ToString(System.Globalization.CultureInfo.InvariantCulture),
                mu.ToString(System.Globalization.CultureInfo.InvariantCulture),
                delta.ToString(System.Globalization.CultureInfo.InvariantCulture),
                rho.ToString(System.Globalization.CultureInfo.InvariantCulture),
                closedLines.ToString(),
                valuedNotBoundLines.ToString(),
                completedOrders.ToString()
            }));
            _decisionIndex++;
        }

        /// <summary>
        /// This is called to decide about potentially pending orders (HGS-M4 heuristic).
        /// Snapshot -> Phase A completion sweep (inherited pods only) -> Phase B price-driven
        /// dispatch loop (re-sweeps after every dispatch) -> commit wiring in strict base
        /// parity: JustRegisterItem over all new Ziops FIRST, then AllocateOrder, then
        /// split-parent bookkeeping, then price-calibration registration (mirrors
        /// M4GManager.DecideAboutPendingOrders / CommitM4G).
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
            HashSet<Pod> Pb;
            HashSet<Pod> Pa;
            Dictionary<Pod, Bot> PodToBot;
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals;
            HashSet<Pod> allPods = InitializeSnapshot(out PiSKU, out OiSKU, out Cs, out pendingOrders,
                out inboundPods, out Ra, out Pb, out Pa, out PodToBot, out residuals);
            if (Ra.Count() > 0 && pendingOrders.Count > 0 && Cs.Count > 0 && Cs.Values.Any(v => v > 0) && allPods.Count > 0)
            {
                M4gEpochState st = BuildEpochState(allPods, Cs, inboundPods, Pb, Ra, pendingOrders, residuals, PodToBot);
                CompletionSweep(st, null);
                DispatchLoop(st, Pa, OiSKU);
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
                double cumDist = PricingDistance();
                double lambda = _pricing.Lambda(cumDist);
                double mu = _pricing.Mu(cumDist);
                double delta = _pricing.Delta();
                double rho = _pricing.Rho(cumDist, Instance.StatOverallItemsHandled);
                WriteDecisionLog(Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, allPods.Count,
                    st.Dispatched, st.ChildCount, st.FastPathCount, st.UnitsAssigned, decisionSec,
                    lambda, mu, delta, rho, st.ClosedLinesThisEpoch, st.ValuedNotBoundLinesThisEpoch, st.CompletedOrdersThisEpoch);
                Instance.Observer.TimeOrderBatchingbyMP(decisionSec);

                // Price-calibration registration - same convention as M4GManager: after the
                // decision log write, so the logged lambda/mu/delta/rho reflect state prior to
                // this decision's own contribution.
                _pricing.RegisterClosedLines(st.ClosedLinesThisEpoch);
                _pricing.RegisterCompletedOrders(st.CompletedOrdersThisEpoch);
                _pricing.RegisterDecision(st.ClosedLinesThisEpoch, st.ClosedLinesThisEpoch + st.ValuedNotBoundLinesThisEpoch);
            }
        }
    }
}
