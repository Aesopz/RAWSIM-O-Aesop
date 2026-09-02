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
    /// HGS-M5: the marginal-line greedy counterpart of M4G.
    /// Spec: docs/superpowers/specs/2026-08-03-hgs-m5-marginal-line-greedy-design.md
    ///
    /// Where HGS-M4 asks "which order can I COMPLETE this epoch" and skips anything it cannot
    /// fully cover, HGS-M5 asks "what is the single best marginal move right now". Two move types:
    ///
    ///   A. draw-line : take one order line to its FULL current residual from stock standing at a
    ///                  station.  delta = -lambda  (-mu if it was the order's last open line)
    ///                            +/- rho per unit by pod tier, - epsilon per unit.
    ///   B. dispatch  : fetch a Pa pod, paying bot->pod + pod->station distance. Scored NET of the
    ///                  draw-line moves it immediately unlocks, since a dispatch alone is always a
    ///                  pure cost and would never be taken on its own.
    ///
    /// Accept the most negative move, stop when none is negative. Station slots and free bots are
    /// HARD constraints, never prices - exactly as in M4G's B3.
    ///
    /// Why this matters: every move only ever sets a variable that exists in M4G's MILP, and the
    /// loop keeps R1-R5 / V1-V4 / B1-B7 satisfied throughout, so the solution HGS-M5 builds is
    /// always FEASIBLE for that MILP. With both objectives written from the same price list this
    /// gives obj(HGS-M5) >= obj(M4G), and the difference is a real, reportable optimality gap.
    ///
    /// The objective is M4G's, term for term, INCLUDING its valuation layer: a bound line earns
    /// -lambda, and a line only the valuation layer can close earns -lambda*delta (same for mu on
    /// orders). Dropping that term would have made this a different objective, not a greedy
    /// solution of the same one - so ValuationSweep reproduces it and RegisterDecision is fed the
    /// same bound/valued ratio M4G feeds, keeping delta calibrated identically.
    ///
    /// Deliberately NOT mirrored from HGS-M4: the `Ra.Count() > 0` entry guard. A draw-line move
    /// needs no robot - only a dispatch does - and M4G spends 89% of its decisions binding sunk
    /// stock without dispatching anything at all.
    ///
    /// Line granularity (not per unit) is a modelling choice, not an approximation of convenience:
    /// lambda only accrues when a line closes IN FULL (M4G's V3/B5), so a per-unit move would
    /// score -epsilon (~0) for every unit but the last and a pure greedy would never reach the
    /// last one. The cost is that one line cannot be assembled from two different pods, which M4G
    /// can do; that gap is declared, not hidden - see spec 2.3.
    /// </summary>
    public class GreedyM5Manager : M1GManager
    {
        /// <summary>Creates a new instance of this controller.</summary>
        /// <param name="instance">The instance this controller belongs to.</param>
        public GreedyM5Manager(Instance instance) : base(instance)
        {
            _config = instance.ControllerConfig.OrderBatchingConfig as GreedyM5Configuration;
            if (_config == null)
                throw new InvalidOperationException("GreedyM5Manager requires a GreedyM5Configuration.");
            _pricing = new M4GPricing(_config);
            _logger = new SplitConsolidationLogger(instance);
            instance.OrderCompleted += _logger.LogParentCompleted;
        }

        /// <summary>The HGS-M5-specific config of this controller.</summary>
        private GreedyM5Configuration _config;
        /// <summary>Price calibration state, shared implementation with M4GManager and HGS-M4.</summary>
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

        /// <summary>Highest committed-pod count that gets its own delta stratum; everything above
        /// shares it. Must match M4GManager.DeltaStratumCap or the two managers stop pricing
        /// identically, which would break the exact-vs-greedy ablation.</summary>
        private const int DeltaStratumCap = 3;

        /// <summary>
        /// Residual-demand version of GenerateOiSKU (faithful mirror of the HGS-M4 copy).
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

        /// <summary>The committed outcome of one epoch, consumed by DecideAboutPendingOrders.</summary>
        private class M5Result
        {
            public Dictionary<Symbol, int> NewZiops = new Dictionary<Symbol, int>();
            public List<Symbol> Allocations = new List<Symbol>();
            public HashSet<Order> SplitParents = new HashSet<Order>();
        }

        /// <summary>
        /// Mutable working state of one epoch. All quantity books are working COPIES seeded from
        /// real pod inventories at epoch start; commits decrement them in lock step with the real
        /// Ziops/ledger writes, so plans can never over-commit reality.
        /// </summary>
        private class M5EpochState
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
            public M5Result Result = new M5Result();
            public int FastPathCount;
            public int ChildCount;
            public int UnitsAssigned;
            public int Dispatched;
            /// <summary>Order lines actually bound (fully drawn) this epoch - lambda's full price.</summary>
            public int ClosedLinesThisEpoch;
            /// <summary>Orders whose entire epoch-start residual got bound this epoch - mu's price.</summary>
            public int CompletedOrdersThisEpoch;
            /// <summary>
            /// Pa pods dispatched by THIS epoch. Their draws pay +rho because they genuinely cost a
            /// trip; inherited Pb pods are already paid for and are free. PodDrawTier returns 2 for
            /// both, which is why this set is needed to tell them apart.
            /// </summary>
            public HashSet<Pod> DispatchedThisEpoch = new HashSet<Pod>();
            /// <summary>(order, stationIndex) pairs already holding a slot in the plan under construction.</summary>
            public HashSet<long> OrderSlotAt = new HashSet<long>();
            /// <summary>
            /// (V2a, incremental valuation) Per-SKU cap on how much may be drawn from pods
            /// dispatched THIS epoch: pooled residual demand minus what already-committed inbound
            /// stock can cover. Mirrors M4GManager's V2a, which bounds qh and therefore - through
            /// B1's qb &lt;= qh - the binding layer too. Read-only after BuildEpochState.
            /// </summary>
            public Dictionary<ItemDescription, int> PaBound = new Dictionary<ItemDescription, int>();
            /// <summary>Units already drawn from this-epoch pods per SKU, against <see cref="PaBound"/>.</summary>
            public Dictionary<ItemDescription, int> PaDrawn = new Dictionary<ItemDescription, int>();

            /// <summary>
            /// Pods whose <see cref="Avail"/> inner dictionary THIS state owns outright. null means
            /// "owns everything" and is the seed state built by BuildEpochState; a clone starts
            /// empty and takes ownership one pod at a time, on first write (see <see cref="OwnAvail"/>).
            /// </summary>
            private HashSet<Pod> _availOwned;
            /// <summary>Same copy-on-write ownership for <see cref="Residuals"/>. See <see cref="OwnResiduals"/>.</summary>
            private HashSet<Order> _residOwned;

            /// <summary>
            /// Memoised <see cref="DrawOrder"/> result per station index, or null where not yet
            /// computed. Entries are replaced, never mutated, so a clone may share the lists; the
            /// only invalidation point is ApplyDispatchToBooks.
            /// </summary>
            public List<Pod>[] DrawCache;

            /// <summary>
            /// PodDrawTier of each pod in <see cref="DrawCache"/>, same index. Cached alongside so
            /// the per-move rho walk does not recompute it for every pod of every candidate move.
            /// </summary>
            public int[][] DrawTierCache;

            /// <summary>
            /// SKU -&gt; the pending orders demanding it, built once per decision. Only the
            /// CandidatePodTopK screening pass uses it, so it stays a plain shared reference on
            /// clones - nothing ever writes to it.
            /// </summary>
            public Dictionary<ItemDescription, List<Order>> OrdersBySku;

            /// <summary>
            /// Returns pod's availability dictionary for WRITING, copying it first if this state is
            /// still sharing the parent's. Profiling showed CloneForTrial's eager deep copy was the
            /// planner's dominant cost: EvaluateDispatch clones once per candidate pod (~84 per
            /// outer iteration here), and each clone allocated one dictionary per pod plus one per
            /// pending order - ~234 allocations - while a trial only ever writes to the handful of
            /// pods actually standing at a station. Copy-on-write keeps the observable books
            /// bit-identical (Dictionary's copy constructor preserves enumeration order, so every
            /// downstream iteration order is unchanged) and reduces the clone to one shallow
            /// reference copy.
            /// </summary>
            public Dictionary<ItemDescription, int> OwnAvail(Pod pod)
            {
                if (_availOwned == null) return Avail[pod];
                if (_availOwned.Add(pod))
                    Avail[pod] = new Dictionary<ItemDescription, int>(Avail[pod]);
                return Avail[pod];
            }

            /// <summary>Residual counterpart of <see cref="OwnAvail"/>.</summary>
            public Dictionary<ItemDescription, int> OwnResiduals(Order order)
            {
                if (_residOwned == null) return Residuals[order];
                if (_residOwned.Add(order))
                    Residuals[order] = new Dictionary<ItemDescription, int>(Residuals[order]);
                return Residuals[order];
            }

            /// <summary>
            /// Copies only the books the planner mutates, so a trial build at a candidate lambda
            /// can run to completion and be thrown away. Result/counters are NOT copied - a trial
            /// never commits anything. Avail and Residuals are copied SHALLOW (the outer map only,
            /// so the clone can rebind an entry without the parent seeing it) and their inner
            /// dictionaries are shared until written through OwnAvail/OwnResiduals. Neither outer
            /// map ever gains or loses a key after BuildEpochState, so sharing the values is safe.
            /// </summary>
            public M5EpochState CloneForTrial()
            {
                M5EpochState c = new M5EpochState();
                c.StationList = StationList;                       // read-only
                c.PodToBot = new Dictionary<Pod, Bot>(PodToBot);
                c.FreeSlots = (int[])FreeSlots.Clone();
                c.PodsAt = PodsAt.Select(h => new HashSet<Pod>(h)).ToList();
                c.Avail = new Dictionary<Pod, Dictionary<ItemDescription, int>>(Avail);
                c._availOwned = new HashSet<Pod>();
                c.PerStationAvail = PerStationAvail.Select(d => new Dictionary<ItemDescription, int>(d)).ToList();
                c.Residuals = new Dictionary<Order, Dictionary<ItemDescription, int>>(Residuals);
                c._residOwned = new HashSet<Order>();
                c.DrawCache = DrawCache == null ? null : (List<Pod>[])DrawCache.Clone();
                c.DrawTierCache = DrawTierCache == null ? null : (int[][])DrawTierCache.Clone();
                c.OrdersBySku = OrdersBySku;                       // read-only
                c.ScanOrder = ScanOrder;                           // read-only
                c.Committed = new HashSet<Order>(Committed);
                c.FreeBots = new List<Bot>(FreeBots);
                c.DispatchedThisEpoch = new HashSet<Pod>(DispatchedThisEpoch);
                c.OrderSlotAt = new HashSet<long>(OrderSlotAt);
                c.PaBound = PaBound;                               // read-only
                c.PaDrawn = new Dictionary<ItemDescription, int>(PaDrawn);
                return c;
            }
        }

        /// <summary>
        /// bot-&gt;pod and pod-&gt;station travel coefficients. Mirrors M4GManager's own private
        /// copies of the same two helpers (M4GManager.cs:725/732) rather than the base class's,
        /// which are private to M1GManager and carry its starve-aware branch. Keeping these
        /// identical to M4G's is what makes T1/T2 the same term in both objectives - without that,
        /// "obj(HGS-M5) >= obj(M4G)" would be comparing two different functions.
        /// </summary>
        private double M1GBotPodCost(Bot robot, Pod pod)
        {
            return EstimateBotPodDistance(robot, pod);
        }
        /// <summary>See <see cref="M1GBotPodCost"/>.</summary>
        private double M1GPodStationCost(Pod pod, OutputStation station)
        {
            return EstimatePodStationDistance(pod, station);
        }

        /// <summary>Reads a pod's remaining working-copy availability for one SKU (0 if absent).</summary>
        private static int GetAvail(M5EpochState st, Pod pod, ItemDescription sku)
        {
            Dictionary<ItemDescription, int> podAvail;
            int have;
            return st.Avail.TryGetValue(pod, out podAvail) && podAvail.TryGetValue(sku, out have) ? have : 0;
        }

        /// <summary>Stable key for "order o holds a slot at station index s".</summary>
        private static long OrderStationKey(Order order, int stationIndex)
        { return ((long)order.ID << 32) | (uint)stationIndex; }

        /// <summary>
        /// Decision-time snapshot of the world, mirroring M4GManager.BuildSnapshot structurally
        /// (same admission rule: `.Any` partial-stock orders, no urgent-subset truncation, Pa
        /// narrowed to pods carrying a SKU of the FINAL admitted order set). Faithful mirror of
        /// GreedyM4GManager.InitializeSnapshot - the two arms must see the identical world.
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

            // HGS-M4 narrows Pa a SECOND time here, against the SKUs of the final admitted order
            // set. HGS-M5 deliberately does not: M4GManager.BuildSnapshot narrows Pa exactly once,
            // against `demanded` (the candidate set's SKUs, computed before the PiSKU filter), and
            // a second pass would leave the greedy with a strictly smaller dispatch candidate set
            // than the exact model it is being measured against. The only difference between the
            // two must be exact-vs-greedy, never what they are allowed to look at.

            residuals = pendingOrders.ToDictionary(o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            return allPods;
        }

        /// <summary>
        /// Seeds the epoch working state. Faithful mirror of GreedyM4GManager.BuildEpochState.
        /// </summary>
        private M5EpochState BuildEpochState(HashSet<Pod> allPods, Dictionary<OutputStation, int> Cs,
            Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Pod> Pb, HashSet<Bot> Ra,
            HashSet<Order> pendingOrders, Dictionary<Order, Dictionary<ItemDescription, int>> residuals,
            Dictionary<Pod, Bot> podToBot)
        {
            M5EpochState st = new M5EpochState();
            st.PodToBot = podToBot;
            st.OrdersBySku = new Dictionary<ItemDescription, List<Order>>();
            foreach (var e in residuals)
                foreach (var line in e.Value)
                {
                    List<Order> lst;
                    if (!st.OrdersBySku.TryGetValue(line.Key, out lst))
                        st.OrdersBySku[line.Key] = lst = new List<Order>();
                    lst.Add(e.Key);
                }
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

            // (V2a) Pooled per-SKU cap on draws from pods fetched this epoch, computed exactly as
            // M4GManager does: total residual demand for the SKU minus what inbound (Pb) stock can
            // already cover, floored at zero. Aggregate per SKU, never per order - deducting the
            // inbound supply separately for every order would over-deduct (M4GManager's own note).
            if (_config.IncrementalValuationEnabled)
            {
                var inbound = new Dictionary<ItemDescription, int>();
                foreach (var pod in Pb)
                    foreach (var e in st.Avail[pod])
                    {
                        int cur;
                        inbound[e.Key] = (inbound.TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
                    }
                var totalResidual = new Dictionary<ItemDescription, int>();
                foreach (var order in pendingOrders)
                    foreach (var e in residuals[order])
                    {
                        if (e.Value <= 0) continue;
                        int cur;
                        totalResidual[e.Key] = (totalResidual.TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
                    }
                foreach (var e in totalResidual)
                {
                    int cov;
                    inbound.TryGetValue(e.Key, out cov);
                    st.PaBound[e.Key] = Math.Max(0, e.Value - cov);
                }
            }
            return st;
        }

        /// <summary>
        /// How many of <paramref name="units"/> would be sourced from pods dispatched THIS epoch,
        /// walking the pods in CommitParts' own preference order so the answer matches what the
        /// real commit will do. Needed to enforce V2a, which caps exactly those draws.
        /// </summary>
        private static int UnitsFromNewPods(M5EpochState st, int stationIndex, ItemDescription sku, int units)
        {
            int need = units, fromNew = 0;
            foreach (var pod in DrawOrder(st, stationIndex, null))
            {
                if (need == 0) break;
                int have = GetAvail(st, pod, sku);
                if (have <= 0) continue;
                int take = Math.Min(have, need);
                if (st.DispatchedThisEpoch.Contains(pod)) fromNew += take;
                need -= take;
            }
            return fromNew;
        }

        /// <summary>Whether a draw would breach V2a's per-SKU cap on this-epoch pod stock.</summary>
        private bool ViolatesPaBound(M5EpochState st, ItemDescription sku, int fromNew)
        {
            if (!_config.IncrementalValuationEnabled) return false;
            if (fromNew == 0) return false;
            int cap, drawn;
            if (!st.PaBound.TryGetValue(sku, out cap)) cap = 0;
            st.PaDrawn.TryGetValue(sku, out drawn);
            return drawn + fromNew > cap;
        }

        /// <summary>
        /// (Pod-tier draw preference) Classifies a committed pod at a station: 0 = processing
        /// (its bot is at the station's pick waypoint), 1 = queued (bot at a queue waypoint),
        /// 2 = on-the-way / freshly dispatched / unknown. Faithful mirror of HGS-M4.
        /// </summary>
        private static int PodDrawTier(M5EpochState st, Pod pod, OutputStation station)
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
        /// The pods at a station in the exact order CommitParts will draw from them, so a move's
        /// rho can be priced against the stock that will REALLY supply it rather than a guess.
        /// </summary>
        /// <remarks>
        /// Memoised per station. This is called from RhoForDraw AND from UnitsFromNewPods, i.e.
        /// TWICE for every candidate move EnumerateLineMoves scores - roughly 300k LINQ sorts per
        /// decision at this instance's size, which profiling identified as the planner's dominant
        /// cost. The result depends only on PodsAt[s] and PodToBot, both of which change in exactly
        /// one place (ApplyDispatchToBooks), so the cache is invalidated there and nowhere else.
        /// preferLast is always null at every call site, so it plays no part in the key; the
        /// ThenBy below is a no-op stabiliser kept only to preserve the original ordering exactly.
        /// </remarks>
        private static List<Pod> DrawOrder(M5EpochState st, int stationIndex, Pod preferLast)
        {
            if (st.DrawCache == null)
            {
                st.DrawCache = new List<Pod>[st.StationList.Count];
                st.DrawTierCache = new int[st.StationList.Count][];
            }
            List<Pod> cached = st.DrawCache[stationIndex];
            if (cached != null && preferLast == null) return cached;
            OutputStation station = st.StationList[stationIndex];
            List<Pod> ordered = st.PodsAt[stationIndex]
                .OrderBy(p => PodDrawTier(st, p, station))
                .ThenBy(p => p == preferLast ? 1 : 0)
                .ToList();
            if (preferLast == null)
            {
                int[] tiers = new int[ordered.Count];
                for (int i = 0; i < ordered.Count; i++) tiers[i] = PodDrawTier(st, ordered[i], station);
                st.DrawCache[stationIndex] = ordered;
                st.DrawTierCache[stationIndex] = tiers;
            }
            return ordered;
        }

        /// <summary>
        /// Sums the rho term for drawing <paramref name="units"/> of one SKU at a station, walking
        /// the pods in CommitParts' own preference order: processing-tier stock is rewarded -rho
        /// (its window is closing; not taking it now buys a future trip), stock on pods dispatched
        /// EARLIER (inherited Pb) is sunk and free, and stock on a pod dispatched by THIS epoch
        /// pays +rho because it genuinely costs a trip. Returns the cost contribution (may be
        /// negative) and how many units could actually be sourced.
        /// </summary>
        private static double RhoForDraw(M5EpochState st, int stationIndex, ItemDescription sku,
            int units, double rho, out int sourced)
        {
            int fromNew;
            return RhoAndNewForDraw(st, stationIndex, sku, units, rho, out sourced, out fromNew);
        }

        /// <summary>
        /// One walk of the draw order that produces BOTH the rho cost and the this-epoch unit count.
        /// EnumerateLineMoves needs both for every candidate move - it used to get them from
        /// RhoForDraw and UnitsFromNewPods, two separate walks over the same pods doing the same
        /// GetAvail lookups. Merging them halves the planner's inner loop; the arithmetic in each
        /// branch is unchanged, so every move scores exactly as before.
        /// </summary>
        private static double RhoAndNewForDraw(M5EpochState st, int stationIndex, ItemDescription sku,
            int units, double rho, out int sourced, out int fromNew)
        {
            double cost = 0.0;
            int need = units;
            fromNew = 0;
            List<Pod> order = DrawOrder(st, stationIndex, null);
            int[] tiers = st.DrawTierCache[stationIndex];
            for (int i = 0; i < order.Count; i++)
            {
                if (need == 0) break;
                Pod pod = order[i];
                int have = GetAvail(st, pod, sku);
                if (have <= 0) continue;
                int take = Math.Min(have, need);
                bool isNew = st.DispatchedThisEpoch.Contains(pod);
                if (tiers[i] == 0) cost -= rho * take;
                else if (isNew) cost += rho * take;
                if (isNew) fromNew += take;
                need -= take;
            }
            sourced = units - need;
            return cost;
        }

        /// <summary>
        /// The valuation layer, greedily. M4G's objective does not only pay for what the binding
        /// layer takes: a line the VALUATION layer can close earns lambda*delta even when slot
        /// capacity stops it being bound this epoch, and the same for mu on whole orders. That
        /// term is a large part of why a pod is worth fetching at all, so a greedy that omits it
        /// is not optimising M4G's objective - it is optimising a different one.
        ///
        /// The valuation layer differs from the binding layer in exactly one way (M4G has no B3
        /// there): it is NOT limited by station slots. It is still limited by pod stock (V1) and
        /// residual demand (V2), and a line still only counts when drawn in full (V3). Binding
        /// draws are a subset of valuation draws (B1: qb &lt;= qh), so this sweep runs on the stock
        /// left after binding and counts what could ADDITIONALLY be closed.
        ///
        /// Greedy, like everything else here: scan orders in urgency order, close whatever fits.
        /// </summary>
        private static void ValuationSweep(M5EpochState st, out int valuedOnlyLines, out int valuedOnlyOrders)
        {
            valuedOnlyLines = 0;
            valuedOnlyOrders = 0;
            // Work on a copy of the post-binding station stock: the sweep must not disturb the books.
            var stock = st.PerStationAvail.Select(d => new Dictionary<ItemDescription, int>(d)).ToList();
            foreach (var order in st.ScanOrder)
            {
                Dictionary<ItemDescription, int> res;
                if (!st.Residuals.TryGetValue(order, out res)) continue;
                int openCount = 0;                      // counted, not materialised - see EnumerateLineMoves
                foreach (var p in res) if (p.Value > 0) openCount++;
                if (openCount == 0) continue;           // already fully bound - nothing left to value
                int closedHere = 0;
                foreach (var line in res)
                {
                    if (line.Value <= 0) continue;
                    for (int s = 0; s < stock.Count; s++)
                    {
                        int have;
                        if (!stock[s].TryGetValue(line.Key, out have) || have < line.Value) continue;
                        stock[s][line.Key] = have - line.Value;
                        valuedOnlyLines++;
                        closedHere++;
                        break;
                    }
                }
                if (closedHere == openCount)
                    valuedOnlyOrders++;                 // every still-open line coverable => zhat = 1
            }
        }

        /// <summary>One candidate draw-line move: take order o's SKU i line to its full residual at station s.</summary>
        private class M5LineMove
        {
            public Order Order;
            public ItemDescription Sku;
            public int StationIndex;
            public int Units;
            public double Delta;
            public bool CompletesOrder;
            public bool NeedsSlot;
            /// <summary>(LexSlotFillLeastResidual) Units this order would still be short of after
            /// this move. 0 means the move completes it. Ranking key only - never priced.</summary>
            public int ResidualAfter;
        }

        /// <summary>One accepted dispatch: pod -> station, carried by bot.</summary>
        private class M5Dispatch
        {
            public Pod Pod;
            public int StationIndex;
            public Bot Bot;
            public double Cost;
        }

        /// <summary>A whole epoch's construction, built on trial books and applied only once chosen.</summary>
        private class M5Plan
        {
            public List<M5LineMove> Draws = new List<M5LineMove>();
            public List<M5Dispatch> Dispatches = new List<M5Dispatch>();
            /// <summary>Realised travel of the dispatches - Dinkelbach's D*.</summary>
            public double DStar;
            /// <summary>Total objective: every accepted move plus the valuation-layer credit.</summary>
            public double Objective;
            /// <summary>Lines the valuation layer can close that the binding layer did not take.</summary>
            public int ValuedOnlyLines;
            /// <summary>Orders likewise completable in the valuation layer only.</summary>
            public int ValuedOnlyOrders;
        }

        /// <summary>
        /// Prices one draw-line move with M4G's objective terms and nothing else. Slot capacity is
        /// a hard constraint upstream, never a price.
        /// </summary>
        private static double ScoreLineMove(M5EpochState st, int stationIndex, ItemDescription sku,
            int units, bool completesOrder, double lambda, double mu, double rho, double epsilon)
        {
            int sourced;
            double d = -lambda - epsilon * units + RhoForDraw(st, stationIndex, sku, units, rho, out sourced);
            if (completesOrder) d -= mu;
            return d;
        }

        /// <summary>
        /// Enumerates every currently feasible draw-line move: the station must hold the line's
        /// FULL remaining residual in this epoch's working books, and the order must already hold a
        /// slot there or the station must still have one free.
        /// </summary>
        /// <param name="delta">Only read under FaithfulMarginal, which scores a draw by its true
        /// change in the objective - the line stops earning lambda*delta as a valued-only line the
        /// moment it becomes bound, so the improvement is lambda*(1-delta), not lambda. Off by
        /// default, which reproduces the published results bit-for-bit. NOTE: the unused static
        /// ScoreLineMove helper above still carries the uncorrected form; the live path is here.</param>
        private List<M5LineMove> EnumerateLineMoves(M5EpochState st,
            double lambda, double mu, double rho, double epsilon, double delta)
        {
            var moves = new List<M5LineMove>();
            foreach (var order in st.ScanOrder)
            {
                if (st.Committed.Contains(order)) continue;
                Dictionary<ItemDescription, int> res;
                if (!st.Residuals.TryGetValue(order, out res)) continue;
                // Counted rather than materialised: `res.Where(...).ToList()` here allocated twice
                // per order per call, and this runs once per candidate pod per outer iteration.
                // Iterating res directly visits the same entries in the same order, so the moves
                // are generated identically.
                int openCount = 0;
                int residualUnits = 0;
                foreach (var p in res) if (p.Value > 0) { openCount++; residualUnits += p.Value; }
                if (openCount == 0) continue;
                foreach (var line in res)
                {
                    if (line.Value <= 0) continue;
                    for (int s = 0; s < st.StationList.Count; s++)
                    {
                        bool holdsSlot = st.OrderSlotAt.Contains(OrderStationKey(order, s));
                        if (!holdsSlot && st.FreeSlots[s] <= 0) continue;
                        int stock;
                        if (!st.PerStationAvail[s].TryGetValue(line.Key, out stock) || stock < line.Value) continue;
                        // One walk for both the V2a check and the rho term - see RhoAndNewForDraw.
                        int sourced, fromNew;
                        double rhoCost = RhoAndNewForDraw(st, s, line.Key, line.Value, rho,
                            out sourced, out fromNew);
                        if (ViolatesPaBound(st, line.Key, fromNew)) continue;         // V2a
                        bool completes = openCount == 1;
                        var mv = new M5LineMove
                        {
                            Order = order,
                            Sku = line.Key,
                            StationIndex = s,
                            Units = line.Value,
                            CompletesOrder = completes,
                            NeedsSlot = !holdsSlot,
                            ResidualAfter = residualUnits - line.Value
                        };
                        double lamEff = _config.FaithfulMarginal ? lambda * (1.0 - delta) : lambda;
                        double muEff = _config.FaithfulMarginal ? mu * (1.0 - delta) : mu;
                        mv.Delta = -lamEff - epsilon * line.Value + rhoCost;
                        if (completes) mv.Delta -= muEff;
                        moves.Add(mv);
                    }
                }
            }
            return moves;
        }

        /// <summary>
        /// Applies an accepted draw-line move to the TRIAL books only: decrements the supplying
        /// pods (in CommitParts' preference order so the books and the eventual real commit agree),
        /// the station aggregate, the order residual, and takes a slot the first time this order
        /// touches this station.
        /// </summary>
        private static void ApplyToBooks(M5EpochState st, M5LineMove mv)
        {
            int fromNew = UnitsFromNewPods(st, mv.StationIndex, mv.Sku, mv.Units);
            if (fromNew > 0)
            {
                int drawn;
                st.PaDrawn.TryGetValue(mv.Sku, out drawn);
                st.PaDrawn[mv.Sku] = drawn + fromNew;      // V2a bookkeeping
            }
            int need = mv.Units;
            foreach (var pod in DrawOrder(st, mv.StationIndex, null))
            {
                if (need == 0) break;
                int have = GetAvail(st, pod, mv.Sku);
                if (have <= 0) continue;
                int take = Math.Min(have, need);
                st.OwnAvail(pod)[mv.Sku] -= take;
                need -= take;
            }
            st.PerStationAvail[mv.StationIndex][mv.Sku] -= mv.Units;
            st.OwnResiduals(mv.Order)[mv.Sku] -= mv.Units;
            if (mv.NeedsSlot)
            {
                st.FreeSlots[mv.StationIndex]--;
                st.OrderSlotAt.Add(OrderStationKey(mv.Order, mv.StationIndex));
            }
            if (st.Residuals[mv.Order].Values.All(v => v <= 0))
                st.Committed.Add(mv.Order);
        }

        /// <summary>
        /// Applies a dispatch to the TRIAL books: the pod becomes available at the station and its
        /// stock joins that station's aggregate. No engine side effects - those happen only in
        /// ApplyPlan, once the winning lambda has been chosen.
        /// </summary>
        private static void ApplyDispatchToBooks(M5EpochState st, Pod pod, int stationIndex, Bot bot)
        {
            st.PodsAt[stationIndex].Add(pod);
            st.DispatchedThisEpoch.Add(pod);
            st.PodToBot[pod] = bot;
            st.FreeBots.Remove(bot);
            if (st.DrawCache != null)                                      // sole invalidation point
            {
                st.DrawCache[stationIndex] = null;
                st.DrawTierCache[stationIndex] = null;
            }
            foreach (var e in st.Avail[pod])
            {
                int cur;
                st.PerStationAvail[stationIndex][e.Key] =
                    (st.PerStationAvail[stationIndex].TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
            }
        }

        /// <summary>
        /// Scores every (Pa pod, station, nearest free bot) candidate NET of the draw-line moves it
        /// would immediately unlock. A dispatch on its own is always a pure cost, so without this
        /// one-step lookahead no pod would ever be fetched. The lookahead is deliberately narrow:
        /// only what THIS pod can do at THIS station in THIS epoch, evaluated on a throwaway copy
        /// of the books. It never looks at other pods and never looks ahead an epoch.
        /// </summary>
        /// <summary>
        /// (CandidatePodTopK, gated) Ranks the Pa pods by a cheap optimistic score and returns only
        /// the best K for full evaluation. The score is the pod's dispatch travel minus
        /// lambda*(1+delta) for every order line it would newly make coverable - the same two terms
        /// that dominate a real trial's outcome, but read straight off a SKU index instead of
        /// cloning the books and re-running the harvest.
        ///
        /// Screening one pod costs O(its SKUs x orders demanding them); a full trial costs
        /// O(pods + orders x lines) and clones the working state, so the screen is roughly two
        /// orders of magnitude cheaper. Survivors are returned in the ORIGINAL enumeration order
        /// (OrderBy is a stable sort, and the filter below preserves paCandidates' own order), so
        /// tie-breaking between two equally-scored survivors is exactly what it was.
        ///
        /// With K = 0 this returns paCandidates untouched and the planner is bit-for-bit the
        /// strictly-aligned reference.
        /// </summary>
        private IEnumerable<Pod> ScreenCandidates(M5EpochState st, HashSet<Pod> paCandidates,
            HashSet<Pod> alreadyDispatched, double lambda, double delta)
        {
            int k = _config.CandidatePodTopK;
            if (k <= 0 || paCandidates.Count <= k) return paCandidates;

            double lineValue = lambda * (1.0 + delta);
            var scored = new List<KeyValuePair<Pod, double>>(paCandidates.Count);
            foreach (var pod in paCandidates)
            {
                if (alreadyDispatched.Contains(pod)) continue;
                double best = double.PositiveInfinity;
                for (int s = 0; s < st.StationList.Count; s++)
                {
                    if (st.FreeSlots[s] <= 0) continue;
                    OutputStation station = st.StationList[s];
                    // Travel is priced with the same two coefficients the real trial uses; the bot
                    // leg is approximated by the nearest free bot, which is what EvaluateDispatch
                    // itself picks.
                    double dBot = double.PositiveInfinity;
                    foreach (var b in st.FreeBots)
                    {
                        double d = M1GBotPodCost(b, pod);
                        if (d < dBot) dBot = d;
                    }
                    if (double.IsPositiveInfinity(dBot)) continue;
                    double cost = dBot + M1GPodStationCost(pod, station) + PodStationExtraCost(pod, station);

                    // Lines this pod would newly make coverable at this station: the station cannot
                    // cover the line now, but could once the pod's stock joins the aggregate.
                    int newlyCoverable = 0;
                    foreach (var e in st.Avail[pod])
                    {
                        List<Order> demanders;
                        if (e.Value <= 0 || !st.OrdersBySku.TryGetValue(e.Key, out demanders)) continue;
                        int stock;
                        st.PerStationAvail[s].TryGetValue(e.Key, out stock);
                        foreach (var order in demanders)
                        {
                            if (st.Committed.Contains(order)) continue;
                            Dictionary<ItemDescription, int> res;
                            int need;
                            if (!st.Residuals.TryGetValue(order, out res)
                                || !res.TryGetValue(e.Key, out need) || need <= 0) continue;
                            if (stock < need && stock + e.Value >= need) newlyCoverable++;
                        }
                    }
                    double score = cost - lineValue * newlyCoverable;
                    if (score < best) best = score;
                }
                if (!double.IsPositiveInfinity(best))
                    scored.Add(new KeyValuePair<Pod, double>(pod, best));
            }
            if (scored.Count <= k) return paCandidates;

            var keep = new HashSet<Pod>(scored.OrderBy(e => e.Value).Take(k).Select(e => e.Key));
            var survivors = new List<Pod>(k);
            foreach (var pod in paCandidates)                 // original order, not score order
                if (keep.Contains(pod)) survivors.Add(pod);
            return survivors;
        }

        /// <summary>
        /// (UpperBoundJump) A provable upper bound on the optimal ratio lambda* = min D/P for this
        /// epoch, or 0 when none can be constructed (no free bot, no free slot, or no candidate pod
        /// that makes any line coverable). Faithful mirror of M4GManager.ComputeLambdaUpperBound.
        ///
        /// lambda* is a minimum over the feasible set, so ANY feasible non-empty plan bounds it:
        /// lambda* &lt;= D(x)/P(x). Take x = "one bot fetches one pod to one station with a free slot,
        /// and one order line is bound there". That plan closes a line, so its P in line-equivalents
        /// is at least 1, and lambda* &lt;= D(x). The cheapest such x gives the tightest bound of the
        /// family, and no plan has to be built to find it.
        ///
        /// The coverability test is the one EnumerateLineMoves uses: the station cannot cover the
        /// line from the stock standing there now, but could once this pod's stock joins the
        /// aggregate. That is exactly when binding the line becomes feasible.
        ///
        /// The bound ignores that the plan usually closes several lines, so it overestimates -
        /// the safe direction, since Dinkelbach converges monotonically downward from any lambda
        /// above lambda*. A loose bound costs iterations, never correctness.
        /// </summary>
        private double ComputeLambdaUpperBound(M5EpochState st, HashSet<Pod> paCandidates)
        {
            if (st.FreeBots.Count == 0 || paCandidates.Count == 0) return 0.0;

            // SKU -> the residual line sizes of orders still open this epoch.
            var demandBySku = new Dictionary<ItemDescription, List<int>>();
            foreach (var order in st.ScanOrder)
            {
                if (st.Committed.Contains(order)) continue;
                Dictionary<ItemDescription, int> res;
                if (!st.Residuals.TryGetValue(order, out res)) continue;
                foreach (var line in res)
                {
                    if (line.Value <= 0) continue;
                    List<int> lst;
                    if (!demandBySku.TryGetValue(line.Key, out lst))
                        demandBySku[line.Key] = lst = new List<int>();
                    lst.Add(line.Value);
                }
            }
            if (demandBySku.Count == 0) return 0.0;

            double best = double.PositiveInfinity;
            foreach (var pod in paCandidates)
            {
                double dBot = double.PositiveInfinity;
                foreach (var bot in st.FreeBots)
                {
                    double d = M1GBotPodCost(bot, pod);
                    if (d < dBot) dBot = d;
                }
                if (double.IsPositiveInfinity(dBot)) continue;

                for (int s = 0; s < st.StationList.Count; s++)
                {
                    if (st.FreeSlots[s] <= 0) continue;
                    OutputStation station = st.StationList[s];
                    double cost = dBot + M1GPodStationCost(pod, station) + PodStationExtraCost(pod, station);
                    if (cost >= best) continue;                 // cannot improve the incumbent
                    bool closesALine = false;
                    foreach (var e in st.Avail[pod])
                    {
                        int have = e.Value;
                        List<int> needs;
                        if (have <= 0 || !demandBySku.TryGetValue(e.Key, out needs)) continue;
                        int stock;
                        st.PerStationAvail[s].TryGetValue(e.Key, out stock);
                        foreach (int need in needs)
                            if (stock < need && stock + have >= need) { closesALine = true; break; }
                        if (closesALine) break;
                    }
                    if (closesALine) best = cost;
                }
            }
            return double.IsPositiveInfinity(best) ? 0.0 : best;
        }

        private void EvaluateDispatch(M5EpochState st, HashSet<Pod> paCandidates, HashSet<Pod> alreadyDispatched,
            double lambda, double mu, double delta, double rho, double epsilon,
            out Pod bestPod, out int bestStation, out Bot bestBot, out double bestDelta)
        {
            bestPod = null; bestStation = -1; bestBot = null; bestDelta = 0.0;
            if (st.FreeBots.Count == 0) return;

            // Baseline valuation credit BEFORE any dispatch, so each candidate is charged only for
            // the credit it actually adds (M4G prices the level, the greedy must price the delta).
            int baseVLines, baseVOrders;
            ValuationSweep(st, out baseVLines, out baseVOrders);
            double baseValuation = -lambda * delta * baseVLines - mu * delta * baseVOrders;

            IEnumerable<Pod> candidates = ScreenCandidates(st, paCandidates, alreadyDispatched, lambda, delta);

            foreach (var pod in candidates)
            {
                if (alreadyDispatched.Contains(pod)) continue;
                Bot bot = null;
                double dBot = double.PositiveInfinity;
                foreach (var b in st.FreeBots)
                {
                    double d = M1GBotPodCost(b, pod);
                    if (d < dBot) { dBot = d; bot = b; }
                }
                if (bot == null) continue;

                for (int s = 0; s < st.StationList.Count; s++)
                {
                    if (st.FreeSlots[s] <= 0) continue;
                    OutputStation station = st.StationList[s];
                    double cost = dBot + M1GPodStationCost(pod, station) + PodStationExtraCost(pod, station);

                    // Throwaway books: place the pod, then harvest greedily exactly as the main
                    // loop would, so the lookahead and the real run agree on what it unlocks.
                    M5EpochState trial = st.CloneForTrial();
                    ApplyDispatchToBooks(trial, pod, s, bot);
                    double unlocked = 0.0;
                    while (true)
                    {
                        var moves = EnumerateLineMoves(trial, lambda, mu, rho, epsilon, delta);
                        M5LineMove best = SelectDraw(moves, false);
                        if (best == null) break;
                        unlocked += best.Delta;
                        ApplyToBooks(trial, best);
                    }

                    // Valuation credit this pod adds on top of what was already reachable. This is
                    // M4G's -lambda*delta*chat / -mu*delta*zhat, and without it a pod that unlocks
                    // no BOUND line (slots full) would look worthless even though the exact model
                    // would still pay to fetch it.
                    int vLines, vOrders;
                    ValuationSweep(trial, out vLines, out vOrders);
                    double valuation = -lambda * delta * vLines - mu * delta * vOrders;

                    double net = cost + unlocked + (valuation - baseValuation);
                    if (net < bestDelta)
                    {
                        bestDelta = net; bestPod = pod; bestStation = s; bestBot = bot;
                    }
                }
            }
        }

        /// <summary>
        /// The greedy construction at one lambda. Builds a plan against a throwaway copy of the
        /// books; nothing here touches the engine.
        /// </summary>
        /// <summary>
        /// Picks the draw-line move to accept, or null to stop drawing.
        ///
        /// Default rule (unchanged): the most negative move; ties prefer the move that does NOT
        /// consume a fresh slot, conserving capacity for a later order.
        ///
        /// LexicographicRatioFirst: at delta = 1 every move scores exactly 0, so the default rule
        /// accepts nothing. A zero-scoring move is then accepted when it claims a slot, and the
        /// tie-break flips to prefer the slot-claiming move - the greedy counterpart of freezing
        /// the ratio and minimising idle slots underneath it. Moves that neither improve the
        /// objective nor claim a slot are still refused, so this cannot run away: every accepted
        /// zero-move strictly decreases FreeSlots.
        /// </summary>
        /// <param name="conserveSlotOnTie">Legacy path only. BuildPlan breaks a tie toward the move
        /// that does not consume a fresh slot; the dispatch lookahead never had that tie-break and
        /// must not gain one, or the flag-off path stops reproducing published results bit-for-bit.
        /// Irrelevant under LexicographicRatioFirst, which defines its own tie-break in the
        /// opposite direction for both callers.</param>
        private M5LineMove SelectDraw(List<M5LineMove> moves, bool conserveSlotOnTie)
        {
            M5LineMove best = null;
            if (_config.LexicographicRatioFirst)
            {
                bool leastResidual = _config.LexSlotFillLeastResidual;
                bool smallestLine = _config.LexSlotFillSmallestLine;
                bool continueOrder = _config.LexSlotFillContinueOrder;
                foreach (var m in moves)
                {
                    bool improves = m.Delta < -LexZeroTolerance;
                    bool free = Math.Abs(m.Delta) <= LexZeroTolerance;
                    bool fillsSlot = free && m.NeedsSlot;
                    // EnumerateLineMoves only emits a move with NeedsSlot == false when the order
                    // ALREADY holds a slot at that station, so this reads as "top up an order whose
                    // slot is already paid for" and nothing else.
                    bool topsUpOrder = continueOrder && free && !m.NeedsSlot;
                    if (!improves && !fillsSlot && !topsUpOrder) continue;
                    if (best == null || m.Delta < best.Delta) { best = m; continue; }
                    if (m.Delta != best.Delta) continue;
                    // Tied on the objective. Claiming a slot always wins - that is the whole point
                    // of accepting a zero. Only when both claim (or both do not) does the residual
                    // key separate them.
                    if (m.NeedsSlot != best.NeedsSlot)
                    {
                        if (m.NeedsSlot) best = m;
                        continue;
                    }
                    // Smallest line first. This is the key that reproduces M4G rev-lex's shape:
                    // with the draw variables unpriced, the exact solver settles on the cheapest
                    // way to satisfy B4 (y needs one drawn line), which is the line with the fewest
                    // units - hence 1.09 items per closed line against the greedy's 1.52. Ranking
                    // tied moves by Units makes the greedy land on the same members of the tie set.
                    if (smallestLine && m.Units != best.Units)
                    {
                        if (m.Units < best.Units) best = m;
                        continue;
                    }
                    if (leastResidual && m.ResidualAfter < best.ResidualAfter) best = m;
                }
                return best;
            }
            foreach (var m in moves)
                if (m.Delta < 0 && (best == null
                    || m.Delta < best.Delta
                    || (conserveSlotOnTie && m.Delta == best.Delta && !m.NeedsSlot && best.NeedsSlot)))
                    best = m;
            return best;
        }

        /// <summary>Below this magnitude a move's score counts as "no change to the objective" for
        /// LexicographicRatioFirst. Mirrors M4GConfiguration.LexTieTolerance: a numerical guard on
        /// an exactly-zero coefficient, far below the metre scale at which moves differ.</summary>
        private const double LexZeroTolerance = 1e-4;

        private M5Plan BuildPlan(M5EpochState seed, HashSet<Pod> paCandidates,
            double lambda, double mu, double delta, double rho, double epsilon)
        {
            M5EpochState st = seed.CloneForTrial();
            M5Plan plan = new M5Plan();
            var dispatched = new HashSet<Pod>();

            while (true)
            {
                var moves = EnumerateLineMoves(st, lambda, mu, rho, epsilon, delta);
                M5LineMove bestDraw = SelectDraw(moves, true);

                Pod dPod; int dStation; Bot dBot; double dDelta;
                EvaluateDispatch(st, paCandidates, dispatched, lambda, mu, delta, rho, epsilon,
                    out dPod, out dStation, out dBot, out dDelta);

                bool takeDraw = bestDraw != null && (dPod == null || bestDraw.Delta <= dDelta);
                if (takeDraw)
                {
                    plan.Draws.Add(bestDraw);
                    plan.Objective += bestDraw.Delta;
                    ApplyToBooks(st, bestDraw);
                    continue;
                }
                if (dPod != null && dDelta < 0)
                {
                    OutputStation station = st.StationList[dStation];
                    double cost = M1GBotPodCost(dBot, dPod)
                        + M1GPodStationCost(dPod, station) + PodStationExtraCost(dPod, station);
                    plan.Dispatches.Add(new M5Dispatch { Pod = dPod, StationIndex = dStation, Bot = dBot, Cost = cost });
                    plan.DStar += cost;
                    plan.Objective += cost;
                    ApplyDispatchToBooks(st, dPod, dStation, dBot);
                    dispatched.Add(dPod);
                    continue;
                }
                break;
            }
            // Final valuation credit of the constructed solution - the -lambda*delta*chat and
            // -mu*delta*zhat terms of M4G's objective, evaluated on the stock left after binding.
            ValuationSweep(st, out plan.ValuedOnlyLines, out plan.ValuedOnlyOrders);
            plan.Objective += -lambda * delta * plan.ValuedOnlyLines - mu * delta * plan.ValuedOnlyOrders;
            return plan;
        }

        /// <summary>
        /// Commits one order's planned parts. Faithful mirror of GreedyM4GManager.CommitParts with
        /// ONE semantic change, flagged below: lambda is earned per closed LINE here, whether or
        /// not the whole order is covered. HGS-M4 could only ever commit full covers, so counting
        /// lines inside the coversAll branch was correct there; keeping it that way would silently
        /// under-report every partial draw and starve lambda's calibration.
        /// </summary>
        private void CommitParts(M5EpochState st, Order order, List<KeyValuePair<int, Dictionary<ItemDescription, int>>> parts, Pod preferPod)
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
                        st.OwnAvail(pod)[pos.Key] -= take;
                        st.PerStationAvail[part.Key][pos.Key] -= take;
                        need -= take;
                    }
                    if (need != 0)
                        throw new InvalidOperationException("HGS-M5 claim assembly under-covered a planned part - working-state books diverged!");
                    st.OwnResiduals(order)[pos.Key] -= pos.Value;
                    st.UnitsAssigned += pos.Value;
                    linesClosedThisOrder++;
                }
                st.FreeSlots[part.Key]--;
            }
            if (!fastPath)
                st.Result.SplitParents.Add(order);
            if (_config.ReleaseParentOnFirstSplit
                && order.IsSplitParent
                && (Instance.ItemManager as ItemManager).IsOrderAvailable(order))
            {
                (Instance.ItemManager as ItemManager).TakeAvailableOrder(order);
            }
            // HGS-M5 difference from HGS-M4 - see method summary.
            st.ClosedLinesThisEpoch += linesClosedThisOrder;
            if (coversAll)
            {
                st.Committed.Add(order);
                st.CompletedOrdersThisEpoch += 1;
            }
        }

        /// <summary>
        /// Turns a chosen plan into real engine state. Draws are grouped into ONE part per
        /// (order, station) so a multi-line draw at the same station yields a single split child
        /// and consumes a single slot - which is also why CommitParts' per-part slot decrement
        /// needs no change here.
        /// </summary>
        private void ApplyPlan(M5EpochState st, M5Plan plan)
        {
            foreach (var d in plan.Dispatches)
            {
                OutputStation station = st.StationList[d.StationIndex];
                Instance.ResourceManager.BottoPod.Add(d.Bot, d.Pod);
                Instance.ResourceManager.ClaimPod(d.Pod, d.Bot, BotTaskType.Extract);
                station.RegisterInboundPod(d.Pod);
                ApplyDispatchToBooks(st, d.Pod, d.StationIndex, d.Bot);
                st.Dispatched++;
            }
            foreach (var byOrder in plan.Draws.GroupBy(m => m.Order))
            {
                var parts = new List<KeyValuePair<int, Dictionary<ItemDescription, int>>>();
                foreach (var byStation in byOrder.GroupBy(m => m.StationIndex))
                {
                    var part = new Dictionary<ItemDescription, int>();
                    foreach (var m in byStation)
                        part[m.Sku] = m.Units;
                    parts.Add(new KeyValuePair<int, Dictionary<ItemDescription, int>>(byStation.Key, part));
                }
                CommitParts(st, byOrder.Key, parts, null);
            }
        }

        // ── Per-decision summary logger ──
        private System.IO.StreamWriter _decisionLog;
        private int _decisionIndex = 0;
        private void EnsureDecisionLog()
        {
            if (_decisionLog != null) return;
            string dir = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            _decisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "hgs_m5_decision_log.csv"), false)
            { AutoFlush = true };
            // No delta column on purpose: HGS-M5 does not use it (see class summary).
            _decisionLog.WriteLine("decision,time,pendingOrders,stationsWithCap,podsInModel,dispatched,children,"
                + "fastPath,units,decisionSec,lambda,mu,rho,epsilon,closedLines,completedOrders,"
                + "lambdaIters,lambdaStart,lambdaEnd,objective,dStar,delta,valuedOnlyLines,valuedOnlyOrders");
        }
        private void WriteDecisionLog(double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            M5EpochState st, double decisionSec, double lambda, double mu, double rho, double epsilon,
            int lambdaIters, double lambdaStart, double lambdaEnd, double objective, double dStar,
            double delta, int valuedOnlyLines, int valuedOnlyOrders)
        {
            EnsureDecisionLog();
            _decisionLog.WriteLine(string.Join(",", new string[] {
                (_decisionIndex++).ToString(), time.ToString(), pendingOrdersN.ToString(),
                stationsWithCap.ToString(), podsInModel.ToString(), st.Dispatched.ToString(),
                st.ChildCount.ToString(), st.FastPathCount.ToString(), st.UnitsAssigned.ToString(),
                decisionSec.ToString(), lambda.ToString(), mu.ToString(), rho.ToString(), epsilon.ToString(),
                st.ClosedLinesThisEpoch.ToString(), st.CompletedOrdersThisEpoch.ToString(),
                lambdaIters.ToString(), lambdaStart.ToString(), lambdaEnd.ToString(),
                objective.ToString(), dStar.ToString(), delta.ToString(),
                valuedOnlyLines.ToString(), valuedOnlyOrders.ToString() }));
        }

        /// <summary>Entry point called by the engine whenever a station has a free slot.</summary>
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

            // NOTE: no `Ra.Count() > 0` guard, unlike HGS-M4 - a draw-line move needs no robot.
            if (pendingOrders.Count == 0 || Cs.Count == 0 || !Cs.Values.Any(v => v > 0) || allPods.Count == 0)
                return;

            M5EpochState st = BuildEpochState(allPods, Cs, inboundPods, Pb, Ra, pendingOrders, residuals, PodToBot);

            double cumDist = PricingDistance();
            double lambda0 = _pricing.Lambda(cumDist);
            double mu0 = _pricing.Mu(cumDist);
            double epsilon0 = _pricing.Epsilon(cumDist);
            // (PodTierDrawPricingEnabled) Mirrors M4GManager's T6 gate. Off in canon: the
            // sunk-vs-new distinction already lives in the trip term (only Pa pods pay travel), so
            // tier pricing restated the same idea a second time at unit granularity. Zeroing rho at
            // source is equivalent to dropping both halves (they are the only rho arithmetic in
            // this class) and leaves RhoForDraw's fromNew/sourced tallies intact - those are
            // diagnostics, not prices. The decision log then records rho = 0, which is honest.
            double rho0 = _config.PodTierDrawPricingEnabled
                ? _pricing.Rho(cumDist, Instance.StatOverallItemsHandled)
                : 0.0;

            // ── Outer lambda iteration: the greedy counterpart of M4G's Dinkelbach loop. mu,
            // epsilon and rho scale proportionally with lambda exactly as in M4GManager.SolveM4G,
            // which is what keeps the whole value side writable as lambda * V. Every trial is
            // built on throwaway books, so re-running is free of side effects; only the winning
            // plan is ever applied. ──
            // (StratifiedDelta, gated) Same stratum M4GManager.DeltaStratum uses - the count of
            // pods already committed - so the two managers keep pricing identically, which is the
            // whole point of sharing M4GPricing through IM4GPrices. Held in a local so the same
            // value feeds the price read here and RegisterDecision at the end. -1 when the flag is
            // off, which routes both calls back to the system-wide totals.
            int deltaStratum = _config.StratifiedDelta ? Math.Min(Pb.Count, DeltaStratumCap) : -1;
            double delta = _pricing.Delta(deltaStratum);
            double lambdaK = lambda0;
            M5Plan plan = BuildPlan(st, Pa, lambdaK, mu0, delta, rho0, epsilon0);
            int lambdaIters = 0;
            int escalations = 0;
            bool jumpedToBound = false;
            for (int i = 0; i < _config.LambdaIterations; i++)
            {
                double vStar = lambdaK > 0 ? (plan.DStar - plan.Objective) / lambdaK : 0.0;
                // (Two-sided search, gated) V* <= 0 means the best plan at this lambda is "do
                // nothing", so the ratio D/V is undefined and the original loop stopped here -
                // which makes the whole search ONE-SIDED: an initial lambda above the true ratio
                // gets pulled down, one below it is stuck forever, because a degenerate plan
                // yields no D*/V* to iterate from. Measured on M4G: across 1013 decisions lambda
                // was revised DOWN 880 times and UP zero times, so an under-estimated starting
                // price silently collapses the run (603 orders -> 38 when the price numerator was
                // narrowed to picking distance alone). Doubling lambda and retrying lets the
                // search approach the fixed point from below as well. 0 (default) keeps the
                // original one-sided behaviour bit-for-bit.
                // (UpperBoundJump) Degenerate plan: the empty plan is best at this lambda, so
                // there is no D*/V* to iterate from. Move ONCE to a provable upper bound on
                // lambda* and let the normal Newton steps walk back down monotonically. Skip when
                // the bound is not above the current lambda - lambda was already high enough and
                // the degeneracy is physical (no bot, no slot, nothing coverable), not a price
                // failure. Mirrors M4GManager exactly, including its position ahead of escalation.
                if (vStar <= 0 && _config.UpperBoundJump && !jumpedToBound && lambdaK > 0)
                {
                    double lambdaUb = ComputeLambdaUpperBound(st, Pa);
                    if (lambdaUb > lambdaK)
                    {
                        jumpedToBound = true;
                        double ratioUb = lambda0 > 0 ? lambdaUb / lambda0 : 1.0;
                        plan = BuildPlan(st, Pa, lambdaUb, mu0 * ratioUb, delta, rho0 * ratioUb, epsilon0 * ratioUb);
                        lambdaK = lambdaUb;
                        lambdaIters++;
                        continue;
                    }
                }
                if (vStar <= 0 && escalations < _config.LambdaEscalations && lambdaK > 0)
                {
                    escalations++;
                    double lambdaUp = lambdaK * 2.0;
                    double ratioUp = lambda0 > 0 ? lambdaUp / lambda0 : 1.0;
                    plan = BuildPlan(st, Pa, lambdaUp, mu0 * ratioUp, delta, rho0 * ratioUp, epsilon0 * ratioUp);
                    lambdaK = lambdaUp;
                    lambdaIters++;
                    continue;
                }
                if (vStar <= 0) break;
                if (Math.Abs(plan.Objective) <= _config.LambdaTolerance) break;
                double lambdaNext = plan.DStar / vStar;
                if (lambdaNext <= 0) break;
                double ratio = lambda0 > 0 ? lambdaNext / lambda0 : 1.0;
                M5Plan next = BuildPlan(st, Pa, lambdaNext, mu0 * ratio, delta, rho0 * ratio, epsilon0 * ratio);
                double nextVStar = lambdaNext > 0 ? (next.DStar - next.Objective) / lambdaNext : 0.0;
                if (nextVStar <= 0) break;      // degenerate "do nothing" - keep the previous plan
                plan = next;
                lambdaK = lambdaNext;
                lambdaIters++;
            }

            ApplyPlan(st, plan);

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
            WriteDecisionLog(Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, allPods.Count,
                st, decisionSec, lambda0, mu0, rho0, epsilon0, lambdaIters, lambda0, lambdaK,
                plan.Objective, plan.DStar, delta, plan.ValuedOnlyLines, plan.ValuedOnlyOrders);
            Instance.Observer.TimeOrderBatchingbyMP(decisionSec);

            // Price calibration - same convention as M4GManager: after the log write, so the logged
            // prices reflect state prior to this decision's own contribution. RegisterDecision uses
            // the SAME definition M4G does - delta = bound line closures / valued line closures,
            // where "valued" is the valuation layer's total (bound plus valued-only). Feeding it
            // anything else is what left HGS-M4's delta at 0.946 against M4G's 0.166.
            _pricing.RegisterClosedLines(st.ClosedLinesThisEpoch);
            _pricing.RegisterCompletedOrders(st.CompletedOrdersThisEpoch);
            _pricing.RegisterDecision(st.ClosedLinesThisEpoch,
                st.ClosedLinesThisEpoch + plan.ValuedOnlyLines, deltaStratum);
        }
    }
}
