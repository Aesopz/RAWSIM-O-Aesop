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
    /// M5-NS: the whole-order greedy counterpart of M4G-NS, exactly as HGS-M5 is to M4G.
    /// Spec: docs/superpowers/specs/2026-08-09-m5-ns-whole-order-greedy-design.md
    /// (see the end of the file: "修訂 v2 - 單一 complete-order 移動", which describes the move
    /// atom this file still uses - complete-order(o, s), one order plus the pod set it needs, in
    /// one shot).
    ///
    /// v3 scoring (this revision): v2 charged the ENTIRE dispatched pod set's travel to the ONE
    /// order that triggered the move, even though those pods then go on to serve other orders for
    /// free (their stock stays at the station). That systematically over-costs every dispatch and
    /// only very cheap orders ever cleared the mu+sigma threshold (measured: 409 orders, EOR 2.414,
    /// 28.5 m/order vs M4G-NS's 579 orders, EOR 1.618, 20.3 m/order - the greedy was paying for a
    /// whole pod set out of a single order's pocket). v3 keeps v2's pod set (greedy set cover, sunk
    /// station stock first) but scores it NET of everything it unlocks: after dispatching, a
    /// throwaway clone of the books is greedily HARVESTED for every order (the trigger order first,
    /// then any other order) that is now fully coverable at zero further travel, exactly the way
    /// GreedyM5Manager.EvaluateDispatch (~line 885) prices a pod dispatch net of the draw-line moves
    /// it unlocks. See TryCompleteOrderBundle / HarvestZeroCostCompletions below.
    ///
    /// Same decision structure as M4G-NS - M1G's own yos/yaos two-layer, all-or-nothing whole
    /// order atom, no g/gh/z/zh unit-level variables at all - same price list (mu, delta,
    /// sigma = SlotScale * mu, and nothing else: no lambda, no rho, no epsilon), same solution
    /// space. The ONLY permitted difference is that a greedy construction replaces the MILP
    /// solve.
    ///
    /// ONE move type: complete-order(o, s). v1 split this into "assign an order the station
    /// ALREADY covers" plus a separately-scored "dispatch one pod" move, and that measurably
    /// failed (-26.88% orders vs HADGS, 67.7% of decisions were no-ops): under the whole-order
    /// atom, an order usually needs SEVERAL pods before it is coverable at all, so dispatching
    /// just one pod alone unlocks nothing, its one-step lookahead saw zero value, and the greedy
    /// refused to ever dispatch. The move atom has to match the model atom - M5's move is "close
    /// this line, including the pods it needs"; M5-NS's move is "complete this order, including
    /// EVERY pod it needs, in one shot".
    ///
    /// complete-order(o, s) is feasible iff: (1) o is not yet assigned (shi2); (2) s has a free
    /// slot (shi4, HARD constraint, never a price); (3) s's current stock, topped up by
    /// newly-dispatched Pa pods, can cover o's ENTIRE demand (shi5); (4) the number of newly
    /// dispatched pods needed is &lt;= the number of still-free bots (shi8/shi10, HARD
    /// constraint). Pod selection uses stock already at the station first (sunk, zero travel
    /// cost) and closes any remaining shortfall by standard GREEDY SET COVER: repeatedly take
    /// the still-undispatched Pa pod that maximises (newly covered units) / (its travel cost),
    /// paired with its nearest still-free bot, until the shortfall is empty or no candidate pod
    /// covers anything - at which point the move is infeasible. No hand-tuned parameter is
    /// introduced; this is the standard set-cover ratio greedy.
    ///
    /// Score: Delta = (sum of D_bot-&gt;pod + D_pod-&gt;station over newly dispatched pods only -
    /// sunk pods are free) - mu*(1-delta) - sigma, or with the mu*delta term dropped to 0 if the
    /// order was already counted by the pre-move valuation sweep (FaithfulMarginal - see
    /// TryCompleteOrderMove and spec 4.1).
    ///
    /// Accept the most negative move, stop when none is negative. Every accepted move only ever
    /// sets x[p,s]=1 for the chosen pods, u[r,p]=1 for their bots, yaos[o,s]=1 and yos[o,s]=1 -
    /// all inside M4G-NS's shi2..shi13. Greedy set cover may pick a suboptimal pod combination,
    /// but that only makes the objective worse, never leaves the feasible region, so
    /// obj(M5-NS) &gt;= obj(M4G-NS) still holds and the gap is a real, reportable optimality gap
    /// - see spec S-8.
    ///
    /// The objective is M4G-NS's, term for term, INCLUDING its valuation layer: an order the
    /// valuation sweep can already cover pays -mu*delta even when a slot never opens up for it.
    /// ValuationSweepNS reproduces that term (whole-order coverage, no slot limit, one station
    /// per order - shi2/shi5 without shi4), and RegisterDecision is fed the same bound/valued
    /// definition M4G-NS feeds, so the two arms stay calibrated on the same price list. Getting
    /// this definition wrong is a known historical failure (HGS-M4 vs M4G: delta 0.946 vs 0.166
    /// on a nominally shared price list).
    ///
    /// sigma = SlotScale * mu is folded directly into a successful complete-order move's score
    /// (filling a slot improves the objective by sigma - see spec 5), so there is no separate
    /// `+ sigma * sum(us)` term to add to the objective afterwards, and no `us` decision variable
    /// to maintain: it is identically "remaining free slots", a function of the books, not a
    /// decision. sigma is recomputed every time mu changes in the outer Dinkelbach-style loop
    /// (spec 6) - if it did not scale with mu, V* = (D* - Objective) / mu would silently compute
    /// garbage the moment mu moves off its initial value.
    ///
    /// Slots and free bots are HARD CONSTRAINTS, filtered out during move enumeration, and are
    /// NEVER converted into a price or penalty - this mirrors M4G-NS's B3/shi4/shi8 exactly.
    ///
    /// Never creates split children, never touches splitorders.csv / SplitConsolidationLogger:
    /// M5-NS's atom is the whole order, just like M4G-NS's - there is no partial-fulfilment
    /// concept anywhere in this file.
    /// </summary>
    public class GreedyM5NSManager : M1GManager
    {
        /// <summary>Creates a new instance of this controller.</summary>
        /// <param name="instance">The instance this controller belongs to.</param>
        public GreedyM5NSManager(Instance instance) : base(instance)
        {
            _config = instance.ControllerConfig.OrderBatchingConfig as GreedyM5NSConfiguration;
            if (_config == null)
                throw new InvalidOperationException("GreedyM5NSManager requires a GreedyM5NSConfiguration.");
            // Unit-consistency guard, mirrored from M4GNSManager's constructor: mu is priced in
            // metres, which requires the objective's distance terms to be metres too.
            if (instance.SettingConfig != null && instance.SettingConfig.StarveAwareCostEnabled)
                throw new InvalidOperationException(
                    "M5-NS prices orders in metres but StarveAwareCostEnabled makes the distance terms seconds. "
                    + "Set StarveAwareCostEnabled=false or disable M5-NS.");

            // GreedyM5NSConfiguration is NOT an M4GNSConfiguration (constitution forbids that
            // inheritance - it would make `is M4GNSConfiguration` type checks elsewhere
            // misfire) and M4GNSPricing's constructor only accepts M4GNSConfiguration. Rather
            // than touch M4GNSPricing (forbidden) or invent a parallel pricing class (which
            // would let the two arms' price formulas silently drift apart), build a local
            // M4GNSConfiguration here and copy across only the fields M4GNSPricing actually
            // reads (Mu/Delta - see M4GNSPricing.cs). This is what structurally guarantees
            // M5-NS and M4G-NS price identically: the pricing code itself is shared, not just
            // the numbers.
            M4GNSConfiguration priceCfg = new M4GNSConfiguration
            {
                MuScale = _config.MuScale,
                DeltaScale = _config.DeltaScale,
                WarmupOrders = _config.WarmupOrders,
                MuFallback = _config.MuFallback,
                DeltaFallback = _config.DeltaFallback,
                MuFixed = _config.MuFixed,
                DeltaFixed = _config.DeltaFixed,
                StratifiedDelta = _config.StratifiedDelta,
                DeltaStratumMinOrders = _config.DeltaStratumMinOrders,
            };
            _pricing = new M4GNSPricing(priceCfg);
        }

        /// <summary>The M5-NS-specific config of this controller.</summary>
        private GreedyM5NSConfiguration _config;
        /// <summary>Price calibration state, structurally shared formula with M4GNSManager (see
        /// constructor comment) - mu and delta only, no lambda/rho/epsilon.</summary>
        private M4GNSPricing _pricing;

        /// <summary>Highest committed-pod count that gets its own delta stratum; everything above
        /// shares it. Must match M4GNSManager.DeltaStratum's key (min(|Pb|,3)) or the two arms
        /// stop pricing identically.</summary>
        private const int DeltaStratumCap = 3;

        /// <summary>Running decision counter for the CSV log.</summary>
        private int _decisionIndex = 0;
        /// <summary>Per-decision diagnostic log (m5ns_decision_log.csv).</summary>
        private System.IO.StreamWriter _decisionLog;

        /// <summary>
        /// Mutable working state of one epoch. All quantity books are working COPIES seeded from
        /// real pod inventories at epoch start. Unlike HGS-M5, per-pod stock (<see cref="Avail"/>)
        /// is never written during planning - only the per-station AGGREGATE
        /// (<see cref="PerStationAvail"/>) is, because every feasibility check and every move
        /// score in this model reads the aggregate (shi5 has no per-pod term). Specific pods are
        /// only chosen once, for real, when a winning plan is committed - see
        /// <see cref="ApplyPlan"/>. That is what lets <see cref="CloneForTrial"/> share
        /// <see cref="Avail"/> and <see cref="Demands"/> by reference instead of deep-copying them
        /// per candidate, the way HGS-M5's copy-on-write machinery has to.
        /// </summary>
        private class M5NSEpochState
        {
            public List<OutputStation> StationList;
            public int[] FreeSlots;
            public List<HashSet<Pod>> PodsAt;
            public Dictionary<Pod, Bot> PodToBot;
            /// <summary>Per-pod remaining stock. Never mutated during planning - see class summary.</summary>
            public Dictionary<Pod, Dictionary<ItemDescription, int>> Avail;
            public List<Dictionary<ItemDescription, int>> PerStationAvail;
            /// <summary>Each pending order's FULL demand (== order.Positions). Never mutated: an
            /// order is either wholly unassigned or wholly assigned, there is no residual.</summary>
            public Dictionary<Order, Dictionary<ItemDescription, int>> Demands;
            public List<Order> ScanOrder;
            public HashSet<Order> AssignedOrders = new HashSet<Order>();
            public List<Bot> FreeBots;
            /// <summary>(order, station) assignments committed for real this epoch.</summary>
            public List<KeyValuePair<Order, OutputStation>> Allocations = new List<KeyValuePair<Order, OutputStation>>();
            public int Dispatched;
            public int AssignedThisEpoch;

            /// <summary>
            /// Copies the books a trial mutates. Avail and Demands are shared by reference (see
            /// class summary - planning only ever reads them), so this is far cheaper than HGS-M5's
            /// copy-on-write: no dictionary is ever promoted from shared to owned.
            /// </summary>
            public M5NSEpochState CloneForTrial()
            {
                M5NSEpochState c = new M5NSEpochState();
                c.StationList = StationList;                          // read-only
                c.FreeSlots = (int[])FreeSlots.Clone();
                c.PodsAt = PodsAt.Select(h => new HashSet<Pod>(h)).ToList();
                c.PodToBot = new Dictionary<Pod, Bot>(PodToBot);
                c.Avail = Avail;                                      // never written in a trial
                c.PerStationAvail = PerStationAvail.Select(d => new Dictionary<ItemDescription, int>(d)).ToList();
                c.Demands = Demands;                                  // never written in a trial
                c.ScanOrder = ScanOrder;                              // read-only
                c.AssignedOrders = new HashSet<Order>(AssignedOrders);
                c.FreeBots = new List<Bot>(FreeBots);
                return c;
            }
        }

        /// <summary>bot-&gt;pod travel. Mirrors M4GNSManager.M1GBotPodCostNS (private there, so
        /// mirrored rather than reused - constitution forbids touching M4GNSManager.cs).</summary>
        private double M1GBotPodCostNS(Bot robot, Pod pod)
        { return EstimateBotPodDistance(robot, pod); }

        /// <summary>pod-&gt;station travel. See <see cref="M1GBotPodCostNS"/>.</summary>
        private double M1GPodStationCostNS(Pod pod, OutputStation station)
        { return EstimatePodStationDistance(pod, station); }

        /// <summary>Reads a pod's working-copy availability for one SKU (0 if absent).</summary>
        private static int GetAvail(M5NSEpochState st, Pod pod, ItemDescription sku)
        {
            Dictionary<ItemDescription, int> podAvail;
            int have;
            return st.Avail.TryGetValue(pod, out podAvail) && podAvail.TryGetValue(sku, out have) ? have : 0;
        }

        /// <summary>
        /// Decision-time snapshot of the world. Faithful mirror of M4GNSManager.BuildInputsNS's
        /// admission rule (M1G's own: whole order, ALL lines covered by SYSTEM stock, then
        /// narrowed to what the visible PiSKU/IsAvailabletoPiSKU/GenerateOd narrowing keeps) -
        /// not GreedyM5Manager.InitializeSnapshot's `.Any` partial-stock admission, which belongs
        /// to the split (residual-demand) model. M5-NS must see the SAME candidate order set
        /// M4G-NS's MILP sees, or a gap between the two arms could be admission, not optimality.
        /// </summary>
        private HashSet<Pod> InitializeSnapshotNS(out Dictionary<ItemDescription, List<Pod>> PiSKU,
            out Dictionary<ItemDescription, List<Order>> OiSKU, out Dictionary<OutputStation, int> Cs,
            out HashSet<Order> pendingOrders, out Dictionary<OutputStation, HashSet<Pod>> inboundPods,
            out HashSet<Bot> Ra, out HashSet<Pod> Pb, out HashSet<Pod> Pa, out Dictionary<Pod, Bot> PodToBot,
            out Dictionary<Order, Dictionary<ItemDescription, int>> demands)
        {
            HashSet<Order> pendingOrders1 = new HashSet<Order>(_pendingOrders.Where(o =>
                o.Positions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value)));
            OiSKU = GenerateOiSKU(pendingOrders1);
            Cs = GenerateCs();
            inboundPods = GeneratePs(Cs);
            HashSet<ItemDescription> ItemofOiSKU = new HashSet<ItemDescription>(OiSKU.Keys);
            HashSet<Pod> allPods = new HashSet<Pod>();
            Ra = new HashSet<Bot>();
            Pb = new HashSet<Pod>();
            PodToBot = new Dictionary<Pod, Bot>();
            HashSet<Pod> Pa1 = new HashSet<Pod>();
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
            foreach (var pod in Instance.ResourceManager.UnusedPods.Where(v =>
                v.IsAvailabletoOiSKU(ItemofOiSKU) &&
                !Instance.ResourceManager.BottoPod.ContainsValue(v) &&
                !Instance.ResourceManager._usedPods.ContainsKey(v) &&
                v.Waypoint != null &&
                v.Waypoint.PodStorageLocation))
            {
                allPods.Add(pod);
                Pa1.Add(pod);
            }
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
            pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.Positions.All(p => IsAvailabletoPiSKU(p.Key) >= p.Value)));
            HashSet<Order> Od = GenerateOd(pendingOrders, PiSKU);
            if (Od.Count > Cs.Values.Sum())
                pendingOrders = new HashSet<Order>(Od);
            OiSKU = GenerateOiSKU(pendingOrders);
            Pa = Pa1;
            demands = pendingOrders.ToDictionary(o => o, o => o.Positions.ToDictionary(p => p.Key, p => p.Value));
            return allPods;
        }

        /// <summary>Seeds the epoch working state.</summary>
        private M5NSEpochState BuildEpochState(HashSet<Pod> allPods, Dictionary<OutputStation, int> Cs,
            Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Pod> Pb, HashSet<Bot> Ra,
            HashSet<Order> pendingOrders, Dictionary<Order, Dictionary<ItemDescription, int>> demands,
            Dictionary<Pod, Bot> podToBot)
        {
            M5NSEpochState st = new M5NSEpochState();
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
            st.Demands = demands;
            st.ScanOrder = pendingOrders.OrderBy(o => o.Timestay).ThenBy(o => o.DueTime).ToList();
            st.FreeBots = new List<Bot>(Ra);
            return st;
        }

        /// <summary>
        /// The valuation layer, greedily: which pending, still-unassigned orders could be fully
        /// covered by SOME station's current aggregate stock, with no slot limit (M4G-NS's yos,
        /// unconstrained by shi4). Mirrors HGS-M5's ValuationSweep exactly except the atom is the
        /// whole order (shi5's full-coverage rule), not a line, and there is no separate "lines"
        /// count because M4G-NS has none.
        ///
        /// Returns the SET, not just a count: EnumerateAssignMoves needs order identity to apply
        /// FaithfulMarginal per move (spec 4.1), and the final call still only needs Count.
        /// </summary>
        private static HashSet<Order> ValuationSweepNS(M5NSEpochState st)
        {
            var valuedOnly = new HashSet<Order>();
            // Work on a copy of the current station stock: the sweep must not disturb the books.
            var stock = st.PerStationAvail.Select(d => new Dictionary<ItemDescription, int>(d)).ToList();
            foreach (var order in st.ScanOrder)
            {
                if (st.AssignedOrders.Contains(order)) continue;
                Dictionary<ItemDescription, int> dem;
                if (!st.Demands.TryGetValue(order, out dem)) continue;
                for (int s = 0; s < stock.Count; s++)
                {
                    bool covers = true;
                    foreach (var line in dem)
                    {
                        int have;
                        if (!stock[s].TryGetValue(line.Key, out have) || have < line.Value) { covers = false; break; }
                    }
                    if (!covers) continue;
                    foreach (var line in dem)
                        stock[s][line.Key] -= line.Value;
                    valuedOnly.Add(order);
                    break; // shi2: at most one station per order
                }
            }
            return valuedOnly;
        }

        /// <summary>The price/binding half of an accepted complete-order move: bind order o
        /// wholly to station s. Kept as its own tiny record (rather than folded into
        /// <see cref="M5NSCompleteMove"/>) purely so <see cref="ApplyToBooks"/> has a single,
        /// stable shape to consume - the pod-dispatch half is applied separately, pod by pod, via
        /// <see cref="ApplyDispatchToBooks"/>.</summary>
        private class M5NSAssignMove
        {
            public Order Order;
            public int StationIndex;
            /// <summary>The PRICE portion only (-mu(1-delta)-sigma or -mu-sigma) - excludes the
            /// travel cost of any newly dispatched pods, which is booked separately per pod.</summary>
            public double Delta;
            /// <summary>Whether this order was already in the valuation-sweep basis taken at the
            /// top of the current construction round - see spec 4.1's FaithfulMarginal correction.</summary>
            public bool AlreadyValued;
        }

        /// <summary>One accepted dispatch: pod -&gt; station, carried by bot.</summary>
        private class M5NSDispatch
        {
            public Pod Pod;
            public int StationIndex;
            public Bot Bot;
            public double Cost;
        }

        /// <summary>A whole epoch's construction, built on trial books and applied only once chosen.</summary>
        private class M5NSPlan
        {
            public List<M5NSAssignMove> Assigns = new List<M5NSAssignMove>();
            public List<M5NSDispatch> Dispatches = new List<M5NSDispatch>();
            /// <summary>Realised travel of the dispatches - the Dinkelbach numerator D*.</summary>
            public double DStar;
            /// <summary>Total objective: every accepted move plus the final valuation credit.</summary>
            public double Objective;
            /// <summary>Orders the valuation layer can still close after binding stopped (yos=1,
            /// yaos=0) - the -mu*delta*Sum(yos) term M4G-NS pays even when a slot never opens.</summary>
            public int ValuedOnlyOrders;
            /// <summary>Number of accepted bundles this epoch (diagnostic only - the v3 amortisation
            /// evidence is Assigns.Count / AcceptedBundles, i.e. mean orders harvested per move).</summary>
            public int AcceptedBundles;
        }

        /// <summary>One pod picked by greedy set cover to close part of a complete-order move's
        /// shortfall, with the bot chosen to carry it and its total travel cost
        /// (D_bot-&gt;pod + D_pod-&gt;station + any physics extra).</summary>
        private class M5NSPodPick
        {
            public Pod Pod;
            public Bot Bot;
            public double Cost;
        }

        /// <summary>One candidate complete-order(o, s) BUNDLE (spec v2 section 3, v3 scoring):
        /// dispatches every newly-needed pod for order o at station s, then greedily HARVESTS
        /// every order (o itself, plus any other order) that the dispatched pod set's stock makes
        /// fully coverable at zero further travel. This is what amortises a multi-pod dispatch
        /// across every order it actually unlocks, instead of charging it all to o alone (v2's
        /// flaw - see the class header).</summary>
        private class M5NSBundleMove
        {
            public Order TriggerOrder;
            public int StationIndex;
            /// <summary>Pods this move newly dispatches, in pick order - empty if station stock
            /// already covered the trigger order outright.</summary>
            public List<M5NSPodPick> NewPods;
            /// <summary>Sum of NewPods' Cost - the only real travel cost in <see cref="Delta"/>;
            /// sunk station stock is free.</summary>
            public double NewTravel;
            /// <summary>Every order bound by this bundle (trigger order first, then harvested
            /// others), each carrying its own price-only Delta (mu/mu(1-delta), minus sigma).</summary>
            public List<M5NSAssignMove> Harvested;
            /// <summary>Full bundle score: NewTravel + Sum(Harvested.Delta). Accept the most
            /// negative bundle across all (order, station) candidates.</summary>
            public double Delta;
        }

        /// <summary>
        /// Applies an accepted assign move to the TRIAL books: decrements the station aggregate
        /// for every line of the order's demand, takes the slot, marks the order assigned. Never
        /// touches per-pod stock - see <see cref="M5NSEpochState"/>'s class summary for why that
        /// is deferred to <see cref="ApplyPlan"/>.
        /// </summary>
        private static void ApplyToBooks(M5NSEpochState st, M5NSAssignMove mv)
        {
            var agg = st.PerStationAvail[mv.StationIndex];
            foreach (var line in st.Demands[mv.Order])
                agg[line.Key] -= line.Value;
            st.FreeSlots[mv.StationIndex]--;
            st.AssignedOrders.Add(mv.Order);
        }

        /// <summary>
        /// Applies a dispatch to the TRIAL books: the pod becomes available at the station and its
        /// stock joins that station's aggregate. No engine side effects - those happen only in
        /// <see cref="ApplyPlan"/>, once the winning mu has been chosen.
        /// </summary>
        private static void ApplyDispatchToBooks(M5NSEpochState st, Pod pod, int stationIndex, Bot bot)
        {
            st.PodsAt[stationIndex].Add(pod);
            st.PodToBot[pod] = bot;
            st.FreeBots.Remove(bot);
            foreach (var e in st.Avail[pod])
            {
                int cur;
                st.PerStationAvail[stationIndex][e.Key] =
                    (st.PerStationAvail[stationIndex].TryGetValue(e.Key, out cur) ? cur : 0) + e.Value;
            }
        }

        /// <summary>
        /// Selects the pod set order needs at station s, or returns false if infeasible. Sunk
        /// station stock is used first (free); any shortfall is closed by standard greedy set
        /// cover over the still-undispatched Pa candidates - repeatedly take the pod maximising
        /// (newly covered units)/(its travel cost), paired with its nearest still-free bot
        /// (<paramref name="alreadyDispatched"/> excludes pods this SAME construction round has
        /// already committed to an earlier move; bots already claimed by this move are removed
        /// from the local candidate list as they are picked, but <see cref="M5NSEpochState.FreeBots"/>
        /// itself is only touched once the move is applied - see <see cref="ApplyDispatchToBooks"/>).
        /// Returns false (infeasible) if the shortfall cannot be fully closed, whether because no
        /// pod covers what remains or because the bots run out first (shi8/shi10, hard constraint
        /// - never priced). This is PURELY pod selection - no price is computed here; scoring
        /// (which requires knowing what the pod set unlocks beyond this one order) happens in
        /// <see cref="TryCompleteOrderBundle"/>.
        /// </summary>
        private bool SelectPodsForShortfall(M5NSEpochState st, Order order, int s,
            HashSet<Pod> paCandidates, HashSet<Pod> alreadyDispatched,
            out List<M5NSPodPick> newPods, out double newTravel)
        {
            newPods = new List<M5NSPodPick>();
            newTravel = 0.0;
            if (st.FreeSlots[s] <= 0) return false;                               // shi4, hard

            Dictionary<ItemDescription, int> dem = st.Demands[order];
            Dictionary<ItemDescription, int> shortfall = null;
            foreach (var line in dem)
            {
                int have;
                st.PerStationAvail[s].TryGetValue(line.Key, out have);
                int need = line.Value - have;
                if (need > 0)
                {
                    if (shortfall == null) shortfall = new Dictionary<ItemDescription, int>();
                    shortfall[line.Key] = need;
                }
            }
            if (shortfall == null) return true;               // sunk stock already covers it outright

            OutputStation station = st.StationList[s];
            HashSet<Pod> usedPods = new HashSet<Pod>();
            List<Bot> availBots = new List<Bot>(st.FreeBots);
            while (shortfall.Count > 0)
            {
                if (availBots.Count == 0) return false;                          // shi8/shi10, hard

                Pod bestPod = null; Bot bestBot = null; double bestCost = 0.0; double bestRatio = 0.0;
                foreach (var pod in paCandidates)
                {
                    if (alreadyDispatched.Contains(pod) || usedPods.Contains(pod)) continue;
                    Dictionary<ItemDescription, int> podAvail;
                    if (!st.Avail.TryGetValue(pod, out podAvail)) continue;
                    int covered = 0;
                    foreach (var line in shortfall)
                    {
                        int have;
                        if (podAvail.TryGetValue(line.Key, out have))
                            covered += Math.Min(have, line.Value);
                    }
                    if (covered <= 0) continue;                              // no use to this shortfall

                    Bot bot = null;
                    double dBot = double.PositiveInfinity;
                    foreach (var b in availBots)
                    {
                        double d = M1GBotPodCostNS(b, pod);
                        if (d < dBot) { dBot = d; bot = b; }
                    }
                    if (bot == null) continue;
                    double cost = dBot + M1GPodStationCostNS(pod, station) + PodStationExtraCost(pod, station);
                    // A pod that costs (near) nothing to fetch is the best possible pick, not
                    // a divide-by-zero to be skipped - treat cost<=0 as an infinite ratio so it
                    // always wins, rather than silently excluding the ideal candidate.
                    double ratio = cost > 0 ? covered / cost : double.PositiveInfinity;
                    if (bestPod == null || ratio > bestRatio)
                    { bestPod = pod; bestBot = bot; bestCost = cost; bestRatio = ratio; }
                }
                if (bestPod == null) return false;                           // shortfall not coverable - shi5 fails

                Dictionary<ItemDescription, int> chosenAvail = st.Avail[bestPod];
                foreach (var sku in new List<ItemDescription>(shortfall.Keys))
                {
                    int have;
                    if (chosenAvail.TryGetValue(sku, out have) && have > 0)
                    {
                        int take = Math.Min(have, shortfall[sku]);
                        shortfall[sku] -= take;
                        if (shortfall[sku] <= 0) shortfall.Remove(sku);
                    }
                }
                usedPods.Add(bestPod);
                availBots.Remove(bestBot);
                newPods.Add(new M5NSPodPick { Pod = bestPod, Bot = bestBot, Cost = bestCost });
                newTravel += bestCost;
            }
            return true;
        }

        /// <summary>
        /// Prices binding order at station s to the trial books' CURRENT stock (no dispatch - the
        /// caller is responsible for having already placed whatever pods it wants credited), or
        /// returns null if station s cannot fully cover order's demand right now (shi5) or has no
        /// free slot (shi4, hard). <paramref name="valuedBasis"/> is the valuation-sweep basis
        /// taken ONCE at the top of the current BuildPlan round (spec 4.1's FaithfulMarginal
        /// correction) - not recomputed per candidate, so every bundle scored this round is priced
        /// against the same reference point.
        /// </summary>
        private M5NSAssignMove TryZeroCostComplete(M5NSEpochState st, Order order, int s,
            double mu, double delta, double sigma, HashSet<Order> valuedBasis)
        {
            if (st.FreeSlots[s] <= 0) return null;                               // shi4, hard
            Dictionary<ItemDescription, int> dem = st.Demands[order];
            foreach (var line in dem)
            {
                int have;
                st.PerStationAvail[s].TryGetValue(line.Key, out have);
                if (have < line.Value) return null;                              // shi5 fails
            }
            bool alreadyValued = valuedBasis.Contains(order);
            double price = (_config.FaithfulMarginal && alreadyValued)
                ? -mu * (1.0 - delta) - sigma      // mu*delta share already earned as valued-only
                : -mu - sigma;                      // full price: this move is the first credit
            return new M5NSAssignMove { Order = order, StationIndex = s, Delta = price, AlreadyValued = alreadyValued };
        }

        /// <summary>
        /// Repeatedly binds the single best (lowest-Delta, i.e. most negative price) still-
        /// unassigned order that the TRIAL books can fully cover RIGHT NOW at zero further travel -
        /// any station, not just the one a dispatch just fed - until none remains. This is what
        /// amortises a dispatch's travel across every order it unlocks, not just the one that
        /// triggered it (spec THE FIX step 3). Mutates <paramref name="trial"/> as it goes (each
        /// harvested order consumes its station's stock and a free slot), exactly mirroring
        /// GreedyM5Manager.EvaluateDispatch's inner while-loop over EnumerateLineMoves.
        /// </summary>
        private List<M5NSAssignMove> HarvestZeroCostCompletions(M5NSEpochState trial,
            double mu, double delta, double sigma, HashSet<Order> valuedBasis)
        {
            var harvested = new List<M5NSAssignMove>();
            while (true)
            {
                M5NSAssignMove best = null;
                foreach (var order in trial.ScanOrder)
                {
                    if (trial.AssignedOrders.Contains(order)) continue;
                    for (int s = 0; s < trial.StationList.Count; s++)
                    {
                        M5NSAssignMove mv = TryZeroCostComplete(trial, order, s, mu, delta, sigma, valuedBasis);
                        if (mv != null && (best == null || mv.Delta < best.Delta))
                            best = mv;
                    }
                }
                if (best == null) break;
                harvested.Add(best);
                ApplyToBooks(trial, best);
            }
            return harvested;
        }

        /// <summary>
        /// Builds the single candidate complete-order(order, s) BUNDLE, or returns null if
        /// infeasible: selects the pod set order needs (<see cref="SelectPodsForShortfall"/>),
        /// applies it to a THROWAWAY CLONE of the books, binds the trigger order (guaranteed
        /// feasible once its pod set lands - checked defensively anyway), then greedily HARVESTS
        /// every other order the same dispatch happens to unlock at zero further travel
        /// (<see cref="HarvestZeroCostCompletions"/>). The bundle's score is the pod set's real
        /// travel cost minus the price credit of every order it ends up binding - so a multi-pod
        /// dispatch is judged by everything it pays for, not just the one order that asked for it.
        /// </summary>
        private M5NSBundleMove TryCompleteOrderBundle(M5NSEpochState st, Order order, int s,
            HashSet<Pod> paCandidates, HashSet<Pod> alreadyDispatched,
            double mu, double delta, double sigma, HashSet<Order> valuedBasis)
        {
            List<M5NSPodPick> newPods;
            double newTravel;
            if (!SelectPodsForShortfall(st, order, s, paCandidates, alreadyDispatched, out newPods, out newTravel))
                return null;

            M5NSEpochState trial = st.CloneForTrial();
            foreach (var pick in newPods)
                ApplyDispatchToBooks(trial, pick.Pod, s, pick.Bot);

            M5NSAssignMove triggerMove = TryZeroCostComplete(trial, order, s, mu, delta, sigma, valuedBasis);
            if (triggerMove == null) return null;      // defensive - should be feasible by construction

            ApplyToBooks(trial, triggerMove);
            var harvested = new List<M5NSAssignMove> { triggerMove };
            harvested.AddRange(HarvestZeroCostCompletions(trial, mu, delta, sigma, valuedBasis));

            double harvestCredit = 0.0;
            foreach (var h in harvested) harvestCredit += h.Delta;

            return new M5NSBundleMove
            {
                TriggerOrder = order,
                StationIndex = s,
                NewPods = newPods,
                NewTravel = newTravel,
                Harvested = harvested,
                Delta = newTravel + harvestCredit,
            };
        }

        /// <summary>
        /// The greedy construction at one mu. Every outer iteration scans ALL (unassigned order,
        /// station) pairs, builds the complete-order BUNDLE for each (pod set cover, plus a greedy
        /// harvest of every order that pod set unlocks - see <see cref="TryCompleteOrderBundle"/>),
        /// and accepts the single most negative one - "accept the most negative move, stop when
        /// none is negative", now scored net of the whole bundle it unlocks rather than charged to
        /// one order alone (v3 fix - see the class header). Builds a plan against a throwaway copy
        /// of the books; nothing here touches the engine.
        /// </summary>
        private M5NSPlan BuildPlan(M5NSEpochState seed, HashSet<Pod> paCandidates, double mu, double delta, double sigma)
        {
            M5NSEpochState st = seed.CloneForTrial();
            M5NSPlan plan = new M5NSPlan();
            var dispatched = new HashSet<Pod>();

            while (true)
            {
                HashSet<Order> basis = ValuationSweepNS(st);
                M5NSBundleMove best = null;
                foreach (var order in st.ScanOrder)
                {
                    if (st.AssignedOrders.Contains(order)) continue;
                    for (int s = 0; s < st.StationList.Count; s++)
                    {
                        M5NSBundleMove mv = TryCompleteOrderBundle(st, order, s, paCandidates, dispatched, mu, delta, sigma, basis);
                        if (mv != null && mv.Delta < 0 && (best == null || mv.Delta < best.Delta))
                            best = mv;
                    }
                }
                if (best == null) break;

                foreach (var pick in best.NewPods)
                {
                    plan.Dispatches.Add(new M5NSDispatch { Pod = pick.Pod, StationIndex = best.StationIndex, Bot = pick.Bot, Cost = pick.Cost });
                    plan.DStar += pick.Cost;
                    plan.Objective += pick.Cost;
                    ApplyDispatchToBooks(st, pick.Pod, best.StationIndex, pick.Bot);
                    dispatched.Add(pick.Pod);
                }
                foreach (var assign in best.Harvested)
                {
                    plan.Assigns.Add(assign);
                    plan.Objective += assign.Delta;
                    ApplyToBooks(st, assign);
                }
                plan.AcceptedBundles++;
            }
            // Final valuation credit of the constructed solution - the -mu*delta*Sum(yos) term of
            // M4G-NS's objective for orders the valuation layer can still close (yos=1) even
            // though binding (yaos) never reached them this epoch.
            HashSet<Order> finalValued = ValuationSweepNS(st);
            plan.ValuedOnlyOrders = finalValued.Count;
            plan.Objective += -mu * delta * plan.ValuedOnlyOrders;
            return plan;
        }

        /// <summary>
        /// Turns a chosen plan into real engine state. Dispatches first (so their stock is on the
        /// real books before any assign tries to source from them), then assigns in acceptance
        /// order, sourcing each line from the REAL per-pod stock at that station - the only point
        /// in this class that ever writes <see cref="M5NSEpochState.Avail"/>.
        /// </summary>
        private void ApplyPlan(M5NSEpochState st, M5NSPlan plan)
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
            foreach (var mv in plan.Assigns)
            {
                OutputStation station = st.StationList[mv.StationIndex];
                Dictionary<ItemDescription, int> dem = st.Demands[mv.Order];
                foreach (var line in dem)
                {
                    int need = line.Value;
                    foreach (var pod in st.PodsAt[mv.StationIndex].OrderBy(p => p.ID).ToList())
                    {
                        if (need == 0) break;
                        int have = GetAvail(st, pod, line.Key);
                        if (have <= 0) continue;
                        int take = Math.Min(have, need);
                        Symbol name = new Symbol
                        {
                            pod = pod, order = mv.Order, outputstation = station, skui = line.Key,
                            name = "ziops" + "_" + line.Key.ID.ToString() + "_" + mv.Order.ID.ToString()
                                + "_" + pod.ID.ToString() + "_" + station.ID.ToString()
                        };
                        Instance.ResourceManager._Ziops[station].Add(name, take);
                        st.Avail[pod][line.Key] -= take;
                        for (int i = 0; i < take; i++)
                            pod.JustRegisterItem(line.Key);
                        need -= take;
                    }
                    if (need != 0)
                        throw new InvalidOperationException(
                            "M5-NS claim assembly under-covered a planned assign - working-state books diverged!");
                }
                ApplyToBooks(st, mv);
                st.Allocations.Add(new KeyValuePair<Order, OutputStation>(mv.Order, station));
                st.AssignedThisEpoch++;
            }
        }

        /// <summary>Lazily opens m5ns_decision_log.csv in the run's statistics directory.</summary>
        private void EnsureDecisionLog()
        {
            if (_decisionLog != null) return;
            string dir = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            _decisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "m5ns_decision_log.csv"), false)
            { AutoFlush = true };
            // No lambda/rho/epsilon columns on purpose - M5-NS's price list is mu/delta/sigma only.
            // acceptedBundles is the v3 amortisation diagnostic: assignedOrders/acceptedBundles is
            // the mean number of orders one dispatch-bundle harvested (>1 => amortisation working).
            _decisionLog.WriteLine("decision,time,pendingOrders,stations,pods,mu,delta,sigma,assignedOrders,valuedOnlyOrders,"
                + "dispatches,acceptedBundles,objective,dStar,muIters,muStart,muEnd,decisionSec");
        }

        /// <summary>Writes one decision row.</summary>
        private void WriteDecisionLog(double time, int pendingOrders, int stations, int pods, double mu, double delta,
            double sigma, int assignedOrders, int valuedOnlyOrders, int dispatches, int acceptedBundles,
            double objective, double dStar, int muIters, double muStart, double muEnd, double decisionSec)
        {
            EnsureDecisionLog();
            _decisionLog.WriteLine(string.Join(",", new string[] {
                (_decisionIndex++).ToString(), time.ToString(), pendingOrders.ToString(), stations.ToString(),
                pods.ToString(), mu.ToString(), delta.ToString(), sigma.ToString(), assignedOrders.ToString(),
                valuedOnlyOrders.ToString(), dispatches.ToString(), acceptedBundles.ToString(), objective.ToString(), dStar.ToString(),
                muIters.ToString(), muStart.ToString(), muEnd.ToString(), decisionSec.ToString() }));
        }

        /// <summary>Entry point called by the engine whenever a station has a free slot.</summary>
        protected override void DecideAboutPendingOrders()
        {
            DateTime startAll = DateTime.Now;
            Dictionary<ItemDescription, List<Pod>> PiSKU;
            Dictionary<ItemDescription, List<Order>> OiSKU;
            Dictionary<OutputStation, int> Cs;
            Dictionary<OutputStation, HashSet<Pod>> inboundPods;
            HashSet<Order> pendingOrders;
            HashSet<Bot> Ra;
            HashSet<Pod> Pb, Pa;
            Dictionary<Pod, Bot> PodToBot;
            Dictionary<Order, Dictionary<ItemDescription, int>> demands;
            HashSet<Pod> allPods = InitializeSnapshotNS(out PiSKU, out OiSKU, out Cs, out pendingOrders,
                out inboundPods, out Ra, out Pb, out Pa, out PodToBot, out demands);

            // No `Ra.Count() > 0` guard, mirroring HGS-M5: an assign move needs no robot, only a
            // dispatch does.
            if (pendingOrders.Count == 0 || Cs.Count == 0 || !Cs.Values.Any(v => v > 0) || allPods.Count == 0)
                return;

            M5NSEpochState st = BuildEpochState(allPods, Cs, inboundPods, Pb, Ra, pendingOrders, demands, PodToBot);

            double cumDist = Instance.StatOverallDistanceTraveled;
            double mu0 = _pricing.Mu(cumDist);
            int deltaStratum = _config.StratifiedDelta ? Math.Min(Pb.Count, DeltaStratumCap) : -1;
            double delta = _pricing.Delta(deltaStratum);
            double sigma0 = _config.SlotScale * mu0;

            // ── Outer mu iteration: the greedy counterpart of M4G-NS's Dinkelbach loop. sigma is
            // recomputed from mu at EVERY trial (spec 6) - if it were held fixed while mu moved,
            // V* = (D* - Objective) / mu would silently compute garbage the moment mu is revised. ──
            double muK = mu0;
            double sigma = sigma0;
            M5NSPlan plan = BuildPlan(st, Pa, muK, delta, sigma);
            int muIters = 0;
            int escalations = 0;
            for (int i = 0; i < _config.MuIterations; i++)
            {
                double vStar = muK > 0 ? (plan.DStar - plan.Objective) / muK : 0.0;
                // Two-sided search, mirrored from GreedyM5Manager/M4GNSManager: V* <= 0 means the
                // best plan at this mu is "do nothing", so double mu and retry rather than getting
                // stuck unable to ever raise it.
                if (vStar <= 0 && escalations < _config.MuEscalations && muK > 0)
                {
                    escalations++;
                    double muUp = muK * 2.0;
                    double sigmaUp = _config.SlotScale * muUp;
                    plan = BuildPlan(st, Pa, muUp, delta, sigmaUp);
                    muK = muUp;
                    sigma = sigmaUp;
                    muIters++;
                    continue;
                }
                if (vStar <= 0) break;                                          // ratio undefined - keep this plan
                if (Math.Abs(plan.Objective) <= _config.MuTolerance) break;      // converged
                double muNext = plan.DStar / vStar;
                if (muNext <= 0) break;
                double sigmaNext = _config.SlotScale * muNext;
                M5NSPlan next = BuildPlan(st, Pa, muNext, delta, sigmaNext);
                double nextVStar = muNext > 0 ? (next.DStar - next.Objective) / muNext : 0.0;
                if (nextVStar <= 0) break;                                      // candidate itself degenerate
                plan = next;
                muK = muNext;
                sigma = sigmaNext;
                muIters++;
            }

            ApplyPlan(st, plan);

            foreach (var alloc in st.Allocations)
            {
                AllocateOrder(alloc.Key, alloc.Value);
                Instance.StatCustomControllerInfo.CustomLogOB1++;
            }
            _pricing.RegisterCompletedOrders(st.AssignedThisEpoch);

            double decisionSec = (DateTime.Now - startAll).TotalSeconds;
            WriteDecisionLog(Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, allPods.Count,
                mu0, delta, sigma0, st.AssignedThisEpoch, plan.ValuedOnlyOrders, st.Dispatched, plan.AcceptedBundles,
                plan.Objective, plan.DStar, muIters, mu0, muK, decisionSec);
            Instance.Observer.TimeOrderBatchingbyMP(decisionSec);

            // Price calibration - after the log write, same convention M4GNSManager uses, so the
            // logged mu/delta reflect state prior to this decision's own contribution.
            // "valued" = bound (assigned this epoch) + valued-only (coverable but never bound) -
            // the exact definition M4G-NS's own RegisterDecision call uses (M4GNSManager.cs:600).
            _pricing.RegisterDecision(st.AssignedThisEpoch, st.AssignedThisEpoch + plan.ValuedOnlyOrders, deltaStratum);
        }
    }
}
