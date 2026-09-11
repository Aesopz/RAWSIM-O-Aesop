using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.SolverWrappers;
using static RAWSimO.Core.Management.ResourceManager;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// M4G - unit-level order splitting with M1G's valuation/binding separation restored.
    ///
    /// M1G keeps two order-assignment layers: yos (slot-free, rewarded in the objective,
    /// a pod valuation) and yaos (slot-limited, the only one actually bound). M3G collapsed
    /// them, so every scored unit had to occupy a currently free slot; splitting then only
    /// arose as a feasibility residual forced by the per-station new-pod cap. M4G restores
    /// the separation at unit level: q-hat values pods against the whole backlog, q binds
    /// only what fits the slots, and only q has side effects.
    ///
    /// Spec: docs/superpowers/specs/2026-07-31-m4g-valuation-binding-design.md
    /// Constitution: M1GManager / HADGSManager / SplitM2eICManager are never modified;
    /// this class mirrors what it needs rather than reaching into them.
    /// </summary>
    public class M4GManager : M1GManager
    {
        /// <summary>Creates a new instance of this manager.</summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public M4GManager(Instance instance) : base(instance)
        {
            _m4gConfig = instance.ControllerConfig.OrderBatchingConfig as M4GConfiguration;
            if (_m4gConfig == null)
                throw new InvalidOperationException("M4GManager requires an M4GConfiguration.");
            // Unit-consistency guard (spec 3.5): lambda/mu/epsilon are priced in metres, which
            // requires the objective's distance terms to be metres too. StarveAwareCostEnabled
            // switches the cost functions to seconds - fail hard rather than solve nonsense.
            if (instance.SettingConfig != null && instance.SettingConfig.StarveAwareCostEnabled)
                throw new InvalidOperationException(
                    "M4G prices lines in metres but StarveAwareCostEnabled makes the distance terms seconds. "
                    + "Set StarveAwareCostEnabled=false or disable M4G.");
            _pricing = new M4GPricing(_m4gConfig);
        }

        /// <summary>The M4G configuration of this manager.</summary>
        private M4GConfiguration _m4gConfig;
        /// <summary>Price calibration state (spec 3.5).</summary>
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
            return _m4gConfig.PickDistancePricing
                ? Instance.StatOverallDistanceTraveledExtract
                : Instance.StatOverallDistanceTraveled;
        }

        /// <summary>Per-decision diagnostic log.</summary>
        private System.IO.StreamWriter _decisionLog;
        /// <summary>Running decision counter, also the probe cadence clock.</summary>
        private int _decisionIndex = 0;

        /// <summary>
        /// Split-lifetime probe (measurement only, never reads back into any decision). Emits one
        /// row per event to m4g_split_lifetime.csv:
        ///
        ///   split,&lt;orderId&gt;,&lt;decision&gt;,&lt;simTime&gt;,&lt;linesLeft&gt;,&lt;unitsLeft&gt;,-1,-1,-1
        ///   closed,&lt;orderId&gt;,&lt;decision&gt;,&lt;simTime&gt;,0,0,-1,-1,-1
        ///   piece,&lt;parentId&gt;,&lt;decision&gt;,&lt;simTime&gt;,0,&lt;units&gt;,&lt;stationId&gt;,&lt;stationsInDecision&gt;,&lt;isChild&gt;
        ///
        /// Joining split/closed by orderId gives, for every remainder this model creates, how long
        /// it took to finish - and a "split" with no matching "closed" is a remainder that never got
        /// finished at all. The question that pair exists to answer: is there an identifiable class
        /// of BAD splits (a bimodal distribution - most cleared fast, a few never), or just a smooth
        /// long tail, in which case there is nothing for a split-quality rule to target.
        ///
        /// The "piece" rows answer a second, independent question: WHICH KIND of splitting this
        /// model actually performs. Xie et al. (2021) separate "split among stations" (all lines
        /// placed in the same period, possibly at different stations) from "split over time" (lines
        /// may be deferred to later periods), and report very different gains for the two. Our
        /// model permits both, but permitting is not exercising: one piece row per AllocateOrder,
        /// carrying the station it landed on and how many distinct stations THIS decision used for
        /// THIS parent, is what lets the three event classes be counted after the fact -
        ///   A same-period cross-station : stationsInDecision &gt; 1
        ///   B cross-period same-station : parent spans &gt;1 decision, 1 distinct station overall
        ///   C cross-period cross-station: parent spans &gt;1 decision, &gt;1 distinct station overall
        /// Whole orders (isChild=0, one piece row, one decision) are emitted too, so the same file
        /// carries the denominator and the no-split arm can be post-processed identically.
        /// Event rows rather than one joined row per parent, so no end-of-run flush hook is needed.
        /// </summary>
        private System.IO.StreamWriter _splitLifeLog;
        private readonly HashSet<int> _splitSeen = new HashSet<int>();

        private void EnsureSplitLifeLog()
        {
            if (_splitLifeLog != null) return;
            string dir0 = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir0)) dir0 = ".";
            if (!System.IO.Directory.Exists(dir0)) System.IO.Directory.CreateDirectory(dir0);
            _splitLifeLog = new System.IO.StreamWriter(
                System.IO.Path.Combine(dir0, "m4g_split_lifetime.csv"), false) { AutoFlush = true };
            _splitLifeLog.WriteLine("event,orderId,decision,time,linesLeft,unitsLeft,"
                + "stationId,stationsInDecision,isChild");
        }

        /// <summary>
        /// Writes one probe row. The last three columns are -1 on split/closed rows, which carry no
        /// station identity: those fire once per parent, not once per placement.
        /// </summary>
        private void LogSplitEvent(string ev, int orderId, int linesLeft, int unitsLeft,
            int stationId = -1, int stationsInDecision = -1, int isChild = -1)
        {
            EnsureSplitLifeLog();
            _splitLifeLog.WriteLine(ev + "," + orderId + "," + _decisionIndex + ","
                + Instance.Controller.CurrentTime + "," + linesLeft + "," + unitsLeft + ","
                + stationId + "," + stationsInDecision + "," + isChild);
        }

        /// <summary>
        /// Slot shadow-price probe (measurement only, gated on SlotShadowProbeCadence, never reads
        /// back into any decision). One row per (probed decision, station):
        ///
        ///   decision,time,stationId,freeSlots,objBase,objPlus1,marginalValue,boundOrders,solveSec
        ///
        /// marginalValue = objBase - objPlus1 &gt;= 0 is the exact integer shadow price of one extra
        /// slot at that station, in metres, evaluated at the SAME lambda the base decision settled
        /// on so the two objectives are directly comparable.
        /// </summary>
        private System.IO.StreamWriter _slotShadowLog;

        private void EnsureSlotShadowLog()
        {
            if (_slotShadowLog != null) return;
            string dir0 = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir0)) dir0 = ".";
            if (!System.IO.Directory.Exists(dir0)) System.IO.Directory.CreateDirectory(dir0);
            _slotShadowLog = new System.IO.StreamWriter(
                System.IO.Path.Combine(dir0, "m4g_slot_shadow.csv"), false) { AutoFlush = true };
            _slotShadowLog.WriteLine(
                "decision,time,stationId,freeSlots,objBase,objPlus1,marginalValue,boundOrders,solveSec");
        }

        /// <summary>Valuation-fidelity diagnostic sink; see M4GConfiguration.ValuationFidelityLog.</summary>
        private System.IO.StreamWriter _valFidLog;
        /// <summary>Lines the PREVIOUS decision valued as closed but did not bind - the lag-1
        /// promise set.</summary>
        private readonly HashSet<string> _prevValuedNotBound = new HashSet<string>();
        /// <summary>Every line still carrying an unfulfilled valuation promise: a line enters when
        /// some decision values it without binding it, and leaves when a later decision actually
        /// binds it. Its size is therefore the outstanding promise backlog, and a backlog that
        /// grows without bound is itself the finding - it means the valuation layer is scoring
        /// closures that never happen rather than closures that merely happen later.</summary>
        private readonly HashSet<string> _everValuedNotBound = new HashSet<string>();

        private void EnsureValFidLog()
        {
            if (_valFidLog != null) return;
            string dir0 = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir0)) dir0 = ".";
            if (!System.IO.Directory.Exists(dir0)) System.IO.Directory.CreateDirectory(dir0);
            _valFidLog = new System.IO.StreamWriter(
                System.IO.Path.Combine(dir0, "m4g_valuation_fidelity.csv"), false) { AutoFlush = true };
            _valFidLog.WriteLine("decision,time,valuedLines,boundLines,"
                + "prevPromises,boundFromPrevPromises,openPromises,boundFromOpenPromises,boundNovel,"
                + "valuedOrders,boundOrders,valuedOrderLines,boundOrderLines,delta");
            _samePodLog = new System.IO.StreamWriter(
                System.IO.Path.Combine(dir0, "m4g_samepod_realisation.csv"), false) { AutoFlush = true };
            _samePodLog.WriteLine("decision,time,cumPromisedUnits,cumSettledUnits,samePodRate,"
                + "outstandingKeys,delta");
        }

        /// <summary>
        /// Records one decision's valuation-vs-binding fidelity and rolls the promise sets forward.
        /// Called AFTER the decision is fully determined and only when the flag is on; it reads
        /// the result and writes to its own file, so it can neither change a decision nor perturb
        /// any existing output.
        ///
        /// boundNovel is the count that matters most: lines the binding layer closed that no
        /// earlier decision had ever valued-but-left-open. A high boundNovel means the closures
        /// being delivered are largely NOT the ones that were promised - the promise was kept by a
        /// substitute. That is exactly the drift the caller's remark describes.
        /// </summary>
        private void WriteValuationFidelity(M4GResult result, double delta)
        {
            EnsureValFidLog();
            int prevPromises = _prevValuedNotBound.Count;
            int openPromises = _everValuedNotBound.Count;
            int boundFromPrev = result.BoundLineKeys.Count(k => _prevValuedNotBound.Contains(k));
            int boundFromOpen = result.BoundLineKeys.Count(k => _everValuedNotBound.Contains(k));
            int boundNovel = result.BoundLineKeys.Count - boundFromOpen;
            _valFidLog.WriteLine(string.Join(",", new string[] {
                _decisionIndex.ToString(), Instance.Controller.CurrentTime.ToString(),
                result.ValuedLineKeys.Count.ToString(), result.BoundLineKeys.Count.ToString(),
                prevPromises.ToString(), boundFromPrev.ToString(),
                openPromises.ToString(), boundFromOpen.ToString(), boundNovel.ToString(),
                result.ValuedOrders.ToString(), result.BoundOrders.ToString(),
                result.ValuedOrderResidualLines.ToString(), result.BoundOrderResidualLines.ToString(),
                delta.ToString() }));
            // Roll forward: a promise is discharged when the line is actually bound, and the
            // lines valued-but-unbound by THIS decision become the new promises. Discharge before
            // adding so a line valued and bound in the same decision never enters the backlog.
            _everValuedNotBound.ExceptWith(result.BoundLineKeys);
            _prevValuedNotBound.Clear();
            foreach (string k in result.ValuedLineKeys)
                if (!result.BoundLineKeys.Contains(k)) { _prevValuedNotBound.Add(k); _everValuedNotBound.Add(k); }

            // ── Same-pod realisation ledger ──
            // The line-level backlog above cannot tell whether a promise was kept by the pod that
            // made it or by a substitute; the old any-pod definition of delta measured 0.699 that
            // way and over-credited every dispatch. This ledger is keyed by pod, so it answers the
            // question the AMORTISATION job actually needs: of the units a pod advertised and did
            // not take, how many did THAT pod later draw before leaving?
            foreach (var kv in result.PodDrawnUnits)
            {
                int owed;
                if (!_podPromises.TryGetValue(kv.Key, out owed) || owed <= 0) continue;
                int settled = Math.Min(owed, kv.Value);
                _podPromiseSettled += settled;
                owed -= settled;
                if (owed > 0) _podPromises[kv.Key] = owed; else _podPromises.Remove(kv.Key);
            }
            foreach (var kv in result.PodPromisedUnits)
            {
                int had; _podPromises.TryGetValue(kv.Key, out had);
                _podPromises[kv.Key] = had + kv.Value;
                _podPromiseTotal += kv.Value;
            }
            _samePodLog.WriteLine(_decisionIndex + "," + Instance.Controller.CurrentTime + ","
                + _podPromiseTotal + "," + _podPromiseSettled + ","
                + (_podPromiseTotal > 0 ? (double)_podPromiseSettled / _podPromiseTotal : 0.0) + ","
                + _podPromises.Count + "," + delta);
        }

        /// <summary>Outstanding promised-but-undrawn units, keyed "pod:order:sku".</summary>
        private readonly Dictionary<string,int> _podPromises = new Dictionary<string,int>();
        private long _podPromiseTotal;
        private long _podPromiseSettled;
        private System.IO.StreamWriter _samePodLog;

        /// <summary>
        /// Re-solves this decision once per station with one extra free slot, and records the
        /// objective improvement. Runs BEFORE CommitM4G so it sees the same pre-commit world the
        /// base solve saw, and restores every field it perturbs: snap.Cs is put back station by
        /// station, and the two diagnostic fields BuildModel/SolveM4G write into (_lastQmax,
        /// _lastInboundCoverUnits) are saved and restored so the decision log still reports the
        /// BASE solve's values rather than the last probe's.
        /// </summary>
        private void ProbeSlotShadowPrices(M4GSnapshot snap, M4GResult baseResult, double lambdaK,
            double lambda0, double mu0, double epsilon0, double rho0, double delta, double deltaOrder)
        {
            EnsureSlotShadowLog();
            int savedQmax = _lastQmax;
            double savedInbound = _lastInboundCoverUnits;
            try
            {
                foreach (var station in snap.Cs.Keys.ToList())
                {
                    int original = snap.Cs[station];
                    snap.Cs[station] = original + 1;
                    M4GModel probe = null;
                    try
                    {
                        probe = BuildModel(snap);
                        if (probe == null) continue;
                        M4GResult r = SolveM4G(probe, snap, lambdaK, lambda0, mu0, epsilon0, rho0, delta, deltaOrder);
                        if (!r.HasSolution) continue;
                        _slotShadowLog.WriteLine(_decisionIndex + "," + Instance.Controller.CurrentTime + ","
                            + station.ID + "," + original + "," + baseResult.Objective + ","
                            + r.Objective + "," + (baseResult.Objective - r.Objective) + ","
                            + baseResult.BoundOrders + "," + r.SolveSec);
                    }
                    finally
                    {
                        if (probe != null) probe.Dispose();
                        snap.Cs[station] = original;
                    }
                }
            }
            finally
            {
                _lastQmax = savedQmax;
                _lastInboundCoverUnits = savedInbound;
            }
        }

        /// <summary>Lazily opens m4g_decision_log.csv in the run's statistics directory.</summary>
        private void EnsureDecisionLog()
        {
            if (_decisionLog != null) return;
            string dir = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            _decisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "m4g_decision_log.csv"), false)
            { AutoFlush = true };
            _decisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsPa,podsPb,botsRa,"
                + "lambda,mu,delta,epsilon,lambdaRef,lambdaDF,dStar,valuedLines,boundLines,valuedOrders,boundOrders,newTrips,boundUnits,"
                + "objective,solveSec,qmax,rho,unitsFromSunk,unitsFromNew,inboundCoverUnits,"
                + "splitsDeferred,splitsCommitted,wipOrders,wipUnits,dinkIters,lambdaStart,lambdaEnd,"
                + "tbar,urgentOrders,lbFail,lbValue,mipGap,guardLamNext,guardTrialObj");
        }

        /// <summary>
        /// Work in progress at this decision: orders that have had part of their demand claimed
        /// by a split child but are not yet fully claimed - i.e. started but not finished. An
        /// order is counted the moment its first child exists and stops being counted once every
        /// unit is claimed, so this is a standing level, not a cumulative total. Returns the
        /// order count; <paramref name="wipUnits"/> receives the units already claimed on those
        /// orders, which is the quantity that inflates ItemsHandled without producing a
        /// completed order.
        /// </summary>
        private int CountWip(out int wipUnits)
        {
            int orders = 0;
            wipUnits = 0;
            foreach (var order in _pendingOrders)
            {
                if (!order.IsSplitParent || order.IsFullyClaimed) continue;
                orders++;
                foreach (var position in order.Positions)
                    wipUnits += order.GetDemandCount(position.Key) - order.GetRemainingDemand(position.Key);
            }
            return orders;
        }

        /// <summary>Invariant-culture formatting for the decision log. NaN must not be
        /// localised - zh-TW renders it "非數值", which breaks every CSV reader.</summary>
        private static string Inv(double v)
        { return v.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>Writes one decision row. Every numeric field is written unformatted for exact diffing.</summary>
        private void WriteDecision(bool solved, int pendingOrders, int stationsWithCap, int podsPa, int podsPb,
            int botsRa, double lambda, double mu, double delta, double epsilon, double lambdaRef, double lambdaDF, double dStar,
            int valuedLines, int boundLines,
            int valuedOrders, int boundOrders, int newTrips, int boundUnits, double objective, double solveSec,
            int qmax, double rho, int unitsFromSunk, int unitsFromNew, double inboundCoverUnits,
            int splitsDeferred, int splitsCommitted, int dinkIters, double lambdaStart, double lambdaEnd)
        {
            EnsureDecisionLog();
            int wipUnits;
            int wipOrders = CountWip(out wipUnits);
            _decisionLog.WriteLine(string.Join(",", new string[] {
                _decisionIndex.ToString(),
                Instance.Controller.CurrentTime.ToString(),
                (solved ? "1" : "0"),
                pendingOrders.ToString(), stationsWithCap.ToString(), podsPa.ToString(), podsPb.ToString(),
                botsRa.ToString(), Inv(lambda), Inv(mu), Inv(delta), Inv(epsilon),
                Inv(lambdaRef), Inv(lambdaDF), Inv(dStar),
                valuedLines.ToString(), boundLines.ToString(), valuedOrders.ToString(), boundOrders.ToString(),
                newTrips.ToString(), boundUnits.ToString(), Inv(objective), Inv(solveSec),
                qmax.ToString(), Inv(rho), unitsFromSunk.ToString(), unitsFromNew.ToString(),
                Inv(inboundCoverUnits), splitsDeferred.ToString(), splitsCommitted.ToString(),
                wipOrders.ToString(), wipUnits.ToString(), dinkIters.ToString(), Inv(lambdaStart),
                Inv(lambdaEnd), Inv(_lastTbar), _lastUrgentOrders.ToString(),
                _lastLbFail.ToString(), Inv(_lastLbValue), Inv(_lastGap),
                Inv(_lastGuardLamNext), Inv(_lastGuardTrialObj) }));
        }

        /// <summary>TEMP DIAGNOSTIC (fill-mode deadlock investigation, remove before merge).</summary>
        private System.IO.StreamWriter _botDiagLog;
        private void WriteBotDiag(Func<Bot, bool> isRa)
        {
            string dir = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (_botDiagLog == null)
            {
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                _botDiagLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "m4g_bot_diag.csv"), false)
                { AutoFlush = true };
                _botDiagLog.WriteLine("decision,time,botID,podNull,podID,inUsedPodsValues,inBotToPodKey,"
                    + "currentTaskType,destWaypointNull,isRa,podJustRegisteredCount");
            }
            foreach (var bot in Instance._outputstationbots)
            {
                Pod pod = bot.Pod;
                int justRegistered = -1;
                try { justRegistered = pod != null ? pod.ItemDescriptionsContained.Count() : -1; } catch { }
                _botDiagLog.WriteLine(string.Join(",", new string[] {
                    _decisionIndex.ToString(),
                    Instance.Controller.CurrentTime.ToString(),
                    bot.ID.ToString(),
                    (pod == null).ToString(),
                    pod != null ? pod.ID.ToString() : "-1",
                    Instance.ResourceManager._usedPods.ContainsValue(bot).ToString(),
                    Instance.ResourceManager.BottoPod.ContainsKey(bot).ToString(),
                    bot.CurrentTask != null ? bot.CurrentTask.GetType().Name : "null",
                    (bot.GetInfoDestinationWaypoint() == null).ToString(),
                    isRa(bot).ToString(),
                    justRegistered.ToString()
                }));
            }
        }

        /// <summary>Decision-time snapshot of the world. Mirrors what SplitM2eICManager builds,
        /// minus every IC gate - M4G has no supply cap, no pipeline floor and no lead-time gate.</summary>
        private sealed class M4GSnapshot
        {
            /// <summary>Orders eligible for this decision (parents carry residual demand only).</summary>
            public HashSet<Order> PendingOrders = new HashSet<Order>();
            /// <summary>Free slots per station.</summary>
            public Dictionary<OutputStation, int> Cs = new Dictionary<OutputStation, int>();
            /// <summary>Idle pods standing in storage that carry at least one demanded SKU.</summary>
            public HashSet<Pod> Pa = new HashSet<Pod>();
            /// <summary>Pods already committed to a station (at it or en route).</summary>
            public HashSet<Pod> Pb = new HashSet<Pod>();
            /// <summary>Bots available to be dispatched.</summary>
            public HashSet<Bot> Ra = new HashSet<Bot>();
            /// <summary>Bot owning each committed pod.</summary>
            public Dictionary<Pod, Bot> PodToBot = new Dictionary<Pod, Bot>();
            /// <summary>Committed pods grouped by their destination station.</summary>
            public Dictionary<OutputStation, HashSet<Pod>> InboundPods = new Dictionary<OutputStation, HashSet<Pod>>();
            /// <summary>Remaining demand per order per SKU.</summary>
            public Dictionary<Order, Dictionary<ItemDescription, int>> Residuals
                = new Dictionary<Order, Dictionary<ItemDescription, int>>();
            /// <summary>Pods carrying each demanded SKU.</summary>
            public Dictionary<ItemDescription, List<Pod>> PiSKU = new Dictionary<ItemDescription, List<Pod>>();
            /// <summary>All pods in the model = Pa union Pb.</summary>
            public HashSet<Pod> AllPods = new HashSet<Pod>();
            /// <summary>Per-order urgency multiplier on lambda/mu, in [1, 2]. Empty (and read as 1)
            /// unless DueDatePricingEnabled - see <see cref="M4GConfiguration.DueDatePricingEnabled"/>.</summary>
            public Dictionary<Order, double> Urgency = new Dictionary<Order, double>();
            /// <summary>Pending orders that already carry work in progress entering this decision
            /// (split parents not yet fully claimed) - the same predicate CountWip uses. Populated
            /// only when WipHoldingEnabled.</summary>
            public HashSet<Order> WipOrders = new HashSet<Order>();
        }

        /// <summary>Urgency multiplier for one order; 1 when due-date pricing is off or the order
        /// still has more slack than the mean turnover time.</summary>
        private static double UrgencyOf(M4GSnapshot snap, Order order)
        {
            double f;
            return snap.Urgency.TryGetValue(order, out f) ? f : 1.0;
        }

        /// <summary>
        /// Builds the decision snapshot. Note the deliberate difference from M3G: the pending
        /// set is NOT truncated to an urgent subset and NOT clipped to the free-slot count -
        /// the valuation layer's whole point is to see the entire backlog (spec D3).
        /// </summary>
        private M4GSnapshot BuildSnapshot()
        {
            M4GSnapshot snap = new M4GSnapshot();
            snap.Cs = GenerateCs();
            snap.InboundPods = GeneratePs(snap.Cs);

            // Committed pods and their bots.
            foreach (var entry in snap.InboundPods)
                foreach (Pod pod in entry.Value)
                {
                    if (snap.PodToBot.ContainsKey(pod)) continue;
                    Bot owner = null;
                    if (Instance.ResourceManager._usedPods.ContainsKey(pod))
                        owner = Instance.ResourceManager._usedPods[pod];
                    else if (Instance.ResourceManager.BottoPod.ContainsValue(pod))
                        owner = Instance.ResourceManager.BottoPod.First(v => v.Value.ID == pod.ID).Key;
                    if (owner == null) continue;   // ownership not visible yet this tick
                    snap.PodToBot[pod] = owner;
                    snap.Pb.Add(pod);
                    snap.AllPods.Add(pod);
                }

            // Orders whose residual demand is at least partly in stock.
            HashSet<Order> candidates = new HashSet<Order>(_pendingOrders.Where(o =>
                o.RemainingPositions.Any(p => Instance.StockInfo.GetActualStock(p.Key) >= 1)));
            HashSet<ItemDescription> demanded = new HashSet<ItemDescription>(
                candidates.SelectMany(o => o.RemainingPositions.Select(p => p.Key)));

            // Idle storage pods carrying something demanded.
            foreach (var pod in Instance.ResourceManager.UnusedPods.Where(v =>
                v.IsAvailabletoOiSKU(demanded)
                && !Instance.ResourceManager.BottoPod.ContainsValue(v)
                && !Instance.ResourceManager._usedPods.ContainsKey(v)
                && v.Waypoint != null && v.Waypoint.PodStorageLocation))
            {
                snap.Pa.Add(pod);
                snap.AllPods.Add(pod);
            }

            // Dispatchable bots (mirrors M1G's three admission cases).
            foreach (var bot in Instance._outputstationbots)
            {
                if (bot.Pod == null && !Instance.ResourceManager._usedPods.ContainsValue(bot)
                    && !Instance.ResourceManager.BottoPod.ContainsKey(bot))
                    snap.Ra.Add(bot);
                else if (bot.Pod == null && !Instance.ResourceManager.BottoPod.ContainsKey(bot)
                    && !snap.PodToBot.ContainsValue(bot) && bot.CurrentTask is RestTask
                    && bot.GetInfoDestinationWaypoint() == null)
                    snap.Ra.Add(bot);
                else if (CanUseReturnPendingBot(bot))
                    snap.Ra.Add(bot);
            }

            // TEMP DIAGNOSTIC (fill-mode deadlock investigation, remove before merge).
            WriteBotDiag(bot => snap.Ra.Contains(bot));

            // MUST run before GeneratePiSKU: everything downstream is derived from AllPods, and
            // AddValuationConstraints' `if (draws.Count == 0) continue` guards rely on the
            // invariant that every SKU surviving the PiSKU filter has at least one pod. Pruning
            // pods afterwards breaks that invariant, and the solver then sets ch/zh (and through
            // B7, z) for free on lines with no draw variable at all - which in testing bound 12
            // orders while dispatching zero pods and deadlocked the run on the first decision.
            ScreenPaCandidates(snap, candidates);

            snap.PiSKU = GeneratePiSKU(snap.AllPods);
            snap.PendingOrders = new HashSet<Order>(candidates.Where(o =>
                o.RemainingPositions.Any(p => snap.PiSKU.ContainsKey(p.Key))));

            // (UrgentOrderGate) HADGS's Od switch. Once the orders whose remaining slack is
            // under UrgentSlackSec number at least the free slots, they become the entire
            // candidate set for this decision. Ported from HADGSManager.GenerateOd rather than
            // M1GManager's, because HADGS applies no feasibility test - so there is no original-
            // versus-residual demand question, and a partly served order still counts as urgent.
            if (_m4gConfig.UrgentOrderGate && snap.PendingOrders.Count > 0)
            {
                DateTime nowU = Instance.SettingConfig.StartTime
                    .AddSeconds(Convert.ToInt32(Instance.Controller.CurrentTime));
                var od = new HashSet<Order>(snap.PendingOrders.Where(o =>
                    o.DueTime - (nowU - o.TimePlaced).TotalSeconds < _m4gConfig.UrgentSlackSec));
                int freeSlots = snap.Cs.Values.Sum();
                if (od.Count > 0 && od.Count >= freeSlots)
                    snap.PendingOrders = od;
            }

            // Optional solve-time convergence knob: keep only the most urgent K orders.
            if (_m4gConfig.ValuationOrderLimit > 0 && snap.PendingOrders.Count > _m4gConfig.ValuationOrderLimit)
                snap.PendingOrders = new HashSet<Order>(snap.PendingOrders
                    .OrderBy(o => o.DueTime).ThenBy(o => o.ID)
                    .Take(_m4gConfig.ValuationOrderLimit));

            // Due-date pricing (spec: urgency term). Timestay is copied verbatim from
            // M1GManager.GenerateOd so the two models measure urgency by the same quantity; Tbar
            // is the running mean order turnover time, read straight off the instance's own
            // statistics rather than re-derived, so it stays a measured value and adds no
            // bookkeeping. Both guards below make the term inert rather than approximate: no
            // completed order yet means no Tbar to normalise against, and slack above Tbar means
            // the order is not at risk. See DueDatePricingEnabled for why the cap is structural.
            _lastTbar = 0.0;
            _lastUrgentOrders = 0;
            if (_m4gConfig.DueDatePricingEnabled)
            {
                double tbar = Instance._statOrderTurnoverTimes.Count > 0
                    ? Instance._statOrderTurnoverTimes.Average() : 0.0;
                _lastTbar = tbar;
                if (tbar > 0.0)
                {
                    DateTime now = Instance.SettingConfig.StartTime
                        .AddSeconds(Convert.ToInt32(Instance.Controller.CurrentTime));
                    foreach (var order in snap.PendingOrders)
                    {
                        double timestay = order.DueTime - (now - order.TimePlaced).TotalSeconds;
                        double u = 1.0 - timestay / tbar;
                        if (u > 1.0) u = 1.0;
                        if (u < 0.0) u = 0.0;
                        if (u > 0.0) _lastUrgentOrders++;
                        snap.Urgency[order] = 1.0 + u;
                    }
                }
            }

            // WIP entering this decision. Same predicate as CountWip so the decision log's wipOrders
            // column and the priced set can never disagree.
            if (_m4gConfig.WipHoldingEnabled)
                foreach (var order in snap.PendingOrders)
                    if (order.IsSplitParent && !order.IsFullyClaimed)
                        snap.WipOrders.Add(order);

            snap.Residuals = snap.PendingOrders.ToDictionary(
                o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            return snap;
        }

        /// <summary>
        /// (CandidatePodTopK, gated) Keeps only the K most promising Pa pods, ranked by the SAME
        /// optimistic score GreedyM5Manager.ScreenCandidates uses: dispatch travel minus
        /// lambda*(1+delta) per order line the pod would newly make coverable at a station with a
        /// free slot.
        ///
        /// This exists to settle a fairness question, not to make M4G faster. HGS-M5 with a
        /// candidate cap measured BETTER end-of-run energy than unrestricted M4G, which would be
        /// backwards if the cap were purely a compute shortcut. The hypothesis is that truncation
        /// is a POLICY change - it hides marginal dispatches that price out as barely worth taking
        /// but cost a whole pod trip - in which case giving M4G the same truncation should move it
        /// the same way and restore "exact is never worse than greedy at equal policy". Running
        /// both models under the same cap is the only way to compare them at equal policy.
        ///
        /// 0 (default) = no cap; canonical M4G sees every Pa pod, exactly as before.
        /// </summary>
        private void ScreenPaCandidates(M4GSnapshot snap, HashSet<Order> candidates)
        {
            int k = _m4gConfig.CandidatePodTopK;
            if (k <= 0 || snap.Pa.Count <= k) return;
            if (snap.Ra.Count == 0) return;

            double cumDist = PricingDistance();
            double lambda = _pricing.Lambda(cumDist);
            double lineValue = lambda * (1.0 + _pricing.Delta());

            // Station stock already committed there (Pb pods at or inbound to the station), the
            // counterpart of HGS-M5's PerStationAvail at epoch start.
            var stations = snap.Cs.Keys.OrderBy(s => s.ID).ToList();
            var stationStock = new List<Dictionary<ItemDescription, int>>();
            foreach (var station in stations)
            {
                var agg = new Dictionary<ItemDescription, int>();
                HashSet<Pod> inbound;
                if (snap.InboundPods.TryGetValue(station, out inbound))
                    foreach (var pod in inbound)
                        foreach (var sku in pod.ItemDescriptionsContained)
                        {
                            int a = pod.CountAvailable(sku);
                            if (a <= 0) continue;
                            int cur;
                            agg[sku] = (agg.TryGetValue(sku, out cur) ? cur : 0) + a;
                        }
                stationStock.Add(agg);
            }

            // SKU -> (order, residual units) demanding it, so the newly-coverable count is an index
            // lookup. Built from `candidates` - the pre-PiSKU admitted set - because this runs
            // before snap.PendingOrders/Residuals exist (see the ordering note at the call site).
            var demandBySku = new Dictionary<ItemDescription, List<KeyValuePair<Order, int>>>();
            foreach (var order in candidates)
                foreach (var line in order.RemainingPositions)
                {
                    if (line.Value <= 0) continue;
                    List<KeyValuePair<Order, int>> lst;
                    if (!demandBySku.TryGetValue(line.Key, out lst))
                        demandBySku[line.Key] = lst = new List<KeyValuePair<Order, int>>();
                    lst.Add(new KeyValuePair<Order, int>(order, line.Value));
                }

            var scored = new List<KeyValuePair<Pod, double>>(snap.Pa.Count);
            foreach (var pod in snap.Pa)
            {
                double dBot = double.PositiveInfinity;
                foreach (var bot in snap.Ra)
                {
                    double d = M1GBotPodCost(bot, pod);
                    if (d < dBot) dBot = d;
                }
                if (double.IsPositiveInfinity(dBot)) continue;

                double best = double.PositiveInfinity;
                for (int s = 0; s < stations.Count; s++)
                {
                    if (snap.Cs[stations[s]] <= 0) continue;
                    double cost = dBot + M1GPodStationCost(pod, stations[s]);
                    int newlyCoverable = 0;
                    foreach (var sku in pod.ItemDescriptionsContained)
                    {
                        int have = pod.CountAvailable(sku);
                        List<KeyValuePair<Order, int>> demanders;
                        if (have <= 0 || !demandBySku.TryGetValue(sku, out demanders)) continue;
                        int stock;
                        stationStock[s].TryGetValue(sku, out stock);
                        foreach (var d in demanders)
                        {
                            int need = d.Value;
                            if (stock < need && stock + have >= need) newlyCoverable++;
                        }
                    }
                    double score = cost - lineValue * newlyCoverable;
                    if (score < best) best = score;
                }
                if (!double.IsPositiveInfinity(best))
                    scored.Add(new KeyValuePair<Pod, double>(pod, best));
            }
            if (scored.Count <= k) return;

            var keep = new HashSet<Pod>(scored.OrderBy(e => e.Value).ThenBy(e => e.Key.ID).Take(k).Select(e => e.Key));
            foreach (var pod in snap.Pa.Where(p => !keep.Contains(p)).ToList())
            {
                snap.Pa.Remove(pod);
                snap.AllPods.Remove(pod);
            }
        }

        /// <summary>
        /// (UpperBoundJump) A provable upper bound on the optimal ratio lambda* = min D/P over the
        /// current snapshot, or 0 when no bound can be constructed (no free bot, no free slot, or
        /// no pod that makes any line coverable).
        ///
        /// Why any feasible non-null plan suffices: lambda* is a minimum over the feasible set, so
        /// for ANY feasible x, lambda* &lt;= D(x)/P(x). Take x = "one bot fetches one pod to one
        /// station with a free slot, and one order line is bound there". That plan closes a line,
        /// so its P (in line-equivalents) is at least 1, and lambda* &lt;= D(x)/1 = D(x). The
        /// cheapest such x gives the tightest bound of this family. No solve is needed.
        ///
        /// The line-coverability test mirrors ScreenPaCandidates: the station cannot cover the line
        /// from its committed (Pb) stock alone, but could once this pod's stock joins the
        /// aggregate. That is exactly the condition under which binding the line is feasible.
        ///
        /// The bound ignores that the plan usually closes several lines, so it overestimates -
        /// which is the safe direction. Dinkelbach converges monotonically downward from any
        /// lambda_0 above lambda* (Kouarfate et al., Lemma 2.9 iv), so a loose bound costs
        /// iterations, never correctness.
        /// </summary>
        private double ComputeLambdaUpperBound(M4GSnapshot snap)
        {
            if (snap.Ra.Count == 0) { _lastLbFail = 1; return 0.0; }
            if (snap.Pa.Count == 0) { _lastLbFail = 2; return 0.0; }
            var stations = snap.Cs.Keys.Where(s => snap.Cs[s] > 0).OrderBy(s => s.ID).ToList();
            if (stations.Count == 0) { _lastLbFail = 3; return 0.0; }

            // Stock already committed to each station - the baseline a candidate pod adds to.
            var stationStock = new List<Dictionary<ItemDescription, int>>();
            foreach (var station in stations)
            {
                var agg = new Dictionary<ItemDescription, int>();
                HashSet<Pod> inbound;
                if (snap.InboundPods.TryGetValue(station, out inbound))
                    foreach (var pod in inbound)
                        foreach (var sku in pod.ItemDescriptionsContained)
                        {
                            int a = pod.CountAvailable(sku);
                            if (a <= 0) continue;
                            int cur;
                            agg[sku] = (agg.TryGetValue(sku, out cur) ? cur : 0) + a;
                        }
                stationStock.Add(agg);
            }

            // SKU -> the residual line sizes demanding it.
            var demandBySku = new Dictionary<ItemDescription, List<int>>();
            foreach (var order in snap.PendingOrders)
            {
                Dictionary<ItemDescription, int> res;
                if (!snap.Residuals.TryGetValue(order, out res)) continue;
                foreach (var line in res)
                {
                    if (line.Value <= 0) continue;
                    List<int> lst;
                    if (!demandBySku.TryGetValue(line.Key, out lst))
                        demandBySku[line.Key] = lst = new List<int>();
                    lst.Add(line.Value);
                }
            }
            if (demandBySku.Count == 0) { _lastLbFail = 4; return 0.0; }

            double best = double.PositiveInfinity;
            foreach (var pod in snap.Pa)
            {
                double dBot = double.PositiveInfinity;
                foreach (var bot in snap.Ra)
                {
                    double d = M1GBotPodCost(bot, pod);
                    if (d < dBot) dBot = d;
                }
                if (double.IsPositiveInfinity(dBot)) continue;

                for (int s = 0; s < stations.Count; s++)
                {
                    double cost = dBot + M1GPodStationCost(pod, stations[s])
                                + PodStationExtraCost(pod, stations[s]);
                    if (cost >= best) continue;                 // cannot improve the incumbent
                    bool closesALine = false;
                    foreach (var sku in pod.ItemDescriptionsContained)
                    {
                        int have = pod.CountAvailable(sku);
                        List<int> needs;
                        if (have <= 0 || !demandBySku.TryGetValue(sku, out needs)) continue;
                        int stock;
                        stationStock[s].TryGetValue(sku, out stock);
                        foreach (int need in needs)
                            if (stock < need && stock + have >= need) { closesALine = true; break; }
                        if (closesALine) break;
                    }
                    if (closesALine) best = cost;
                }
            }
            if (double.IsPositiveInfinity(best)) { _lastLbFail = 5; return 0.0; }
            _lastLbFail = 0;
            _lastLbValue = best;
            return best;
        }

        /// <summary>
        /// (StratifiedDelta, gated) Which delta bucket this decision belongs to, or -1 when the
        /// flag is off so every price read falls back to the system-wide ratio.
        ///
        /// The bucket is the number of pods already committed (Pb), capped at
        /// <see cref="DeltaStratumCap"/> so the tail does not fragment into single-observation
        /// cells. Pb is the stratifier because it is what the measurement singled out: bound
        /// lines sit at ~1.01 per decision whatever Pb is (slot capacity pins them), while valued
        /// lines climb with it, so the realisation rate falls from 0.2117 at Pb=1 to 0.1376 at
        /// Pb=2. Free slots were tried first and are useless here - 913 of 916 decisions have
        /// exactly one station with capacity, so that stratum IS the global one.
        /// </summary>
        private int DeltaStratum(M4GSnapshot snap)
        {
            if (!_m4gConfig.StratifiedDelta) return -1;
            return Math.Min(snap.Pb.Count, DeltaStratumCap);
        }

        /// <summary>Highest Pb value that gets its own delta stratum; everything above shares it.</summary>
        private const int DeltaStratumCap = 3;

        /// <summary>
        /// (ForbidCrossStationOnly) Order id -> the station it is committed to for life. Written
        /// the first time an order takes stock anywhere and never revised, so a parent carrying
        /// residual demand cannot continue at a different station on a later decision. Empty and
        /// unread under every other configuration. Keyed on the PARENT id, which is what stays in
        /// the pending set; children are allocated and gone. Entries are never removed - the map
        /// is bounded by the run's order count (a few hundred) and reusing a completed order's id
        /// is impossible, since child and fresh-order ids come from the same monotonic counter.
        /// </summary>
        private readonly Dictionary<int, int> _nsStationPin = new Dictionary<int, int>();

        /// <summary>Mean order turnover time used to normalise urgency this decision (diagnostics
        /// only; 0 when due-date pricing is off or no order has completed yet).</summary>
        private double _lastTbar = 0.0;
        /// <summary>Pending orders carrying a non-zero urgency boost this decision (diagnostics only).</summary>
        private int _lastUrgentOrders = 0;
        /// <summary>(Diagnostic only, never read by the algorithm.) Why ComputeLambdaUpperBound
        /// returned 0 on this decision: -1 not requested, 0 bound found, 1 no idle robot,
        /// 2 no available pod, 3 no station with capacity, 4 no residual demand,
        /// 5 no SINGLE pod makes any line coverable. Code 5 is the known conservative gap:
        /// a line needing more units than station stock plus any one pod can supply is invisible
        /// to the enumeration, so the epoch stops with the empty plan even though a two-pod plan
        /// would have been feasible. Recorded so its frequency can be measured before deciding
        /// whether it is worth closing.</summary>
        private double _lastGap = double.NaN;
        private int _lastLbFail = -1;
        private double _lastLbValue = 0.0;
        /// <summary>(Diagnostic only, never read by the algorithm.) When the overshoot guard
        /// fires, the lambda_next that was rejected and the objective the trial solve returned
        /// there. Discriminates two explanations of the guard: a trial objective of ~0 means
        /// lambda_next landed ON lambda* and the empty plan merely TIED with the incumbent;
        /// a strictly positive objective would mean the empty plan strictly dominated, i.e.
        /// lambda_next undershot lambda*. NaN when the guard did not fire.</summary>
        private double _lastGuardLamNext = double.NaN;
        private double _lastGuardTrialObj = double.NaN;

        /// <summary>Names of the symbols the model was built from, kept for decoding.</summary>
        private sealed class M4GSymbols
        {
            public List<Symbol> Xps = new List<Symbol>();
            public List<Symbol> Yrp = new List<Symbol>();
            public List<Symbol> Qhat = new List<Symbol>();
            public List<Symbol> Chat = new List<Symbol>();
            public List<Symbol> Zhat = new List<Symbol>();
        }

        /// <summary>Enumerates the decision symbols for a snapshot.</summary>
        private M4GSymbols BuildSymbols(M4GSnapshot snap)
        {
            M4GSymbols sym = new M4GSymbols();
            foreach (var pod in snap.AllPods)
                foreach (var station in snap.Cs.Keys)
                    sym.Xps.Add(new Symbol { pod = pod, outputstation = station,
                        name = "xps_" + pod.ID + "_" + station.ID });
            foreach (var bot in snap.Ra)
                foreach (var pod in snap.Pa)
                    sym.Yrp.Add(new Symbol { robot = bot, pod = pod,
                        name = "yrp_" + bot.ID + "_" + pod.ID });
            foreach (var pod in snap.Pb)
                sym.Yrp.Add(new Symbol { robot = snap.PodToBot[pod], pod = pod,
                    name = "yrp_" + snap.PodToBot[pod].ID + "_" + pod.ID });
            foreach (var order in snap.PendingOrders)
            {
                sym.Zhat.Add(new Symbol { order = order, name = "zh_" + order.ID });
                foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                {
                    // (OrderAtomicNoSplit) No line object exists in that arm: assignment covers
                    // the order in full, so e == f identically and the lambda terms would restate
                    // the mu terms. Not emitting Chat removes them from the objective, the
                    // constraints and the read-back in one place.
                    if (!_m4gConfig.OrderAtomicNoSplit)
                        sym.Chat.Add(new Symbol { order = order, skui = sku.Key,
                            name = "ch_" + order.ID + "_" + sku.Key.ID });
                    // (ForbidCrossStationOnly) An order that already took stock at a station is
                    // pinned there for the rest of its life. B8 alone only says "one station per
                    // DECISION"; a parent carrying residual demand is re-decided next tick and
                    // would otherwise be free to continue at a different one, which is a
                    // cross-station split spread over time - exactly what this arm forbids.
                    // Enforced by not generating the variables rather than by adding equalities:
                    // no big-M, no extra rows, and the model shrinks.
                    int pinned = -1;
                    bool isPinned = _m4gConfig.ForbidSplitting && _m4gConfig.ForbidCrossStationOnly
                        && _nsStationPin.TryGetValue(order.ID, out pinned);
                    foreach (var pod in snap.PiSKU[sku.Key])
                        foreach (var station in snap.Cs.Keys)
                        {
                            if (isPinned && station.ID != pinned) continue;
                            sym.Qhat.Add(new Symbol { order = order, skui = sku.Key, pod = pod,
                                outputstation = station,
                                name = "qh_" + sku.Key.ID + "_" + order.ID + "_" + pod.ID + "_" + station.ID });
                        }
                }
            }
            return sym;
        }

        /// <summary>(CompactLineModel) One (order, sku, station) line-placement candidate.</summary>
        private sealed class M4GPlacement
        {
            public Order Order;
            public ItemDescription Sku;
            public OutputStation Station;
            /// <summary>The line's full residual demand - the only quantity the model needs.</summary>
            public int Units;
            /// <summary>Valuation-layer indicator name (ghat).</summary>
            public string GhName;
            /// <summary>Binding-layer indicator name (g).</summary>
            public string GbName;
        }

        /// <summary>
        /// (CompactLineModel) Builds the whole draw structure on line-to-station indicators, with
        /// no unit-level variables at all. Replaces V1, V2, V2a, V3, V9, V10, B1, B2, B4, B5, B6c,
        /// B11 and B12; every other constraint and the objective are added by the shared code paths
        /// exactly as in the full model.
        ///
        /// The equivalence rests on one observation: with the line as the atom, the per-pod split
        /// of its units is a complete-bipartite transportation problem (any pod standing at the
        /// station may serve any order), so feasibility reduces to the aggregate stock inequality
        /// and an integer allocation always exists. See IM4GPrices.CompactLineModel.
        /// </summary>
        private void AddCompactConstraints(LinearModel wrapper, M4GSnapshot snap, M4GSymbols sym,
            VariableCollection<string> bin, M4GModel model)
        {
            // ── ghat / g placement indicators, one pair per (order, sku, station) ──
            foreach (var order in snap.PendingOrders)
                foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                {
                    // Same pinning filter BuildSymbols applies to Qhat, so the compact model spans
                    // the identical candidate set - not a superset that could find better solutions.
                    int pinned = -1;
                    bool isPinned = _m4gConfig.ForbidSplitting && _m4gConfig.ForbidCrossStationOnly
                        && _nsStationPin.TryGetValue(order.ID, out pinned);
                    var ghVars = new List<Variable>();
                    var gbVars = new List<Variable>();
                    foreach (var station in snap.Cs.Keys)
                    {
                        if (isPinned && station.ID != pinned) continue;
                        // A station can only host this line if SOME pod carrying the sku may go
                        // there; otherwise the placement is structurally infeasible and omitting
                        // it shrinks the model instead of leaving the solver to derive ghat = 0.
                        if (!snap.PiSKU[sku.Key].Any(p => sym.Xps.Any(
                                v => v.pod.ID == p.ID && v.outputstation.ID == station.ID)))
                            continue;
                        string gh = "gh_" + order.ID + "_" + sku.Key.ID + "_" + station.ID;
                        string gb = "gb_" + order.ID + "_" + sku.Key.ID + "_" + station.ID;
                        model.Places.Add(new M4GPlacement
                        {
                            Order = order, Sku = sku.Key, Station = station, Units = sku.Value,
                            GhName = gh, GbName = gb
                        });
                        ghVars.Add(bin[gh]);
                        gbVars.Add(bin[gb]);
                        // (B1') binding placement implies valuation placement. In the full model
                        // this is B1 summed over pods; here it must be stated, because there are
                        // no per-pod variables left to sum.
                        wrapper.AddConstr(bin[gb] <= bin[gh], "B1c");
                    }
                    if (ghVars.Count == 0) continue;
                    // (V10) at most one station per valued line, and chat is exactly "some station
                    // took it". Equality, for the reason spelled out at V9/V10 in the full model.
                    wrapper.AddConstr(LinearExpression.Sum(ghVars)
                        == bin["ch_" + order.ID + "_" + sku.Key.ID], "V10c");
                    // (B12) the binding-layer mirror.
                    wrapper.AddConstr(LinearExpression.Sum(gbVars)
                        == bin["c_" + order.ID + "_" + sku.Key.ID], "B12c");
                }

            // ── (V1') stock feasibility per (sku, station), split by pod tier so that V2a can
            // still speak about Pa draws alone. v[i,w] is the Pa-sourced part; Pb stock at the
            // station is free to use because its trip is already sunk. ──
            int vMax = 1;
            foreach (var sku in snap.PiSKU.Keys)
            {
                int total = snap.PendingOrders.Sum(o =>
                {
                    int need; Dictionary<ItemDescription, int> res;
                    return snap.Residuals.TryGetValue(o, out res) && res.TryGetValue(sku, out need) ? need : 0;
                });
                if (total > vMax) vMax = total;
            }
            VariableCollection<string> vPa = new VariableCollection<string>(
                wrapper, VariableType.Integer, 0, vMax, (string s) => { return s; });

            foreach (var sku in snap.PiSKU.Keys)
            {
                var vNames = new List<Variable>();
                foreach (var station in snap.Cs.Keys)
                {
                    var landed = model.Places
                        .Where(pl => pl.Sku.ID == sku.ID && pl.Station.ID == station.ID)
                        .Select(pl => pl.Units * bin[pl.GhName]).ToList();
                    if (landed.Count == 0) continue;
                    var pbStock = new List<LinearExpression>();
                    var paStock = new List<LinearExpression>();
                    foreach (var pod in snap.PiSKU[sku])
                    {
                        string xn = "xps_" + pod.ID + "_" + station.ID;
                        if (!sym.Xps.Any(v => v.name == xn)) continue;
                        double s = pod.CountAvailable(sku);
                        if (s <= 0) continue;
                        (snap.Pa.Contains(pod) ? paStock : pbStock).Add(s * bin[xn]);
                    }
                    string vn = "vpa_" + sku.ID + "_" + station.ID;
                    Variable vVar = vPa[vn];
                    vNames.Add(vVar);
                    // demand landed here <= sunk stock present + Pa units accounted for by vVar
                    LinearExpression supply = vVar;
                    foreach (var e in pbStock) supply = supply + e;
                    wrapper.AddConstr(LinearExpression.Sum(landed) <= supply, "V1c");
                    // vVar may not exceed the Pa stock actually dispatched here
                    if (paStock.Count == 0)
                        wrapper.AddConstr(vVar <= 0, "V1cPa");
                    else
                    {
                        LinearExpression pa = paStock[0];
                        for (int k = 1; k < paStock.Count; k++) pa = pa + paStock[k];
                        wrapper.AddConstr(vVar <= pa, "V1cPa");
                    }
                }
                // (V2a) Pa draws for this sku, pooled across stations, may only serve demand that
                // inbound supply cannot already cover. Same aggregate bound as the full model.
                if (vNames.Count > 0 && _m4gConfig.IncrementalValuationEnabled)
                {
                    double inbound = snap.Pb.Sum(p => p.CountAvailable(sku));
                    double totalResidual = snap.PendingOrders.Sum(o =>
                    {
                        int need; Dictionary<ItemDescription, int> res;
                        return snap.Residuals.TryGetValue(o, out res) && res.TryGetValue(sku, out need) ? need : 0;
                    });
                    _lastInboundCoverUnits += Math.Min(totalResidual, inbound);
                    wrapper.AddConstr(LinearExpression.Sum(vNames)
                        <= Math.Max(0.0, totalResidual - inbound), "V2ac");
                }
            }

            // ── (B2'/B4') slot occupancy. Unit counts come straight from the placement indicators. ──
            foreach (var order in snap.PendingOrders)
            {
                int totalResidual = snap.Residuals[order].Values.Sum();
                foreach (var station in snap.Cs.Keys)
                {
                    var mine = model.Places
                        .Where(pl => pl.Order.ID == order.ID && pl.Station.ID == station.ID).ToList();
                    if (mine.Count == 0) continue;
                    string yName = "y_" + order.ID + "_" + station.ID;
                    var units = mine.Select(pl => pl.Units * bin[pl.GbName]).ToList();
                    wrapper.AddConstr(LinearExpression.Sum(units) <= totalResidual * bin[yName], "B2c");
                    var flags = mine.Select(pl => bin[pl.GbName]).ToList();
                    wrapper.AddConstr(bin[yName] <= LinearExpression.Sum(flags), "B4c");
                }
            }

            // ── (V7/B8, gated) ForbidSplitting: one station per order, in both layers. This is
            // the SYMBOL-ALIGNED no-split control: the canon model plus exactly this restriction,
            // written in the canon's own variables and prices, so the comparison isolates
            // cross-station splitting and nothing else. M4GNSManager is a different thing - it
            // mirrors M1G's order-level structure and carries its own price set (mu, delta, sigma
            // with no lambda at all), so a comparison against it measures splitting AND pricing
            // together. Cross-PERIOD pinning is already enforced by the placement filter above,
            // which is what makes this "one station for life" rather than "one station per
            // decision" (ForbidCrossStationOnly). ──
            if (_m4gConfig.ForbidSplitting)
                foreach (var order in snap.PendingOrders)
                {
                    var mine = model.Places.Where(pl => pl.Order.ID == order.ID).ToList();
                    if (mine.Count == 0) continue;
                    var stations = mine.Select(pl => pl.Station).GroupBy(s => s.ID)
                        .Select(g => g.First()).ToList();
                    // Valuation layer: yhat[o,w] dominates every line this order places at w.
                    var yhVars = new List<Variable>();
                    foreach (var station in stations)
                    {
                        string yh = "yhc_" + order.ID + "_" + station.ID;
                        foreach (var pl in mine.Where(p => p.Station.ID == station.ID))
                            wrapper.AddConstr(bin[pl.GhName] <= bin[yh], "V7ac");
                        yhVars.Add(bin[yh]);
                    }
                    if (yhVars.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(yhVars) <= 1, "V7bc");
                    // Binding layer: x[o,w] already exists. Only stations this order can actually
                    // reach are summed - referencing a y that no B2c/B4c pair constrains would
                    // manufacture a free binary.
                    var xVars = stations.Select(st => bin["y_" + order.ID + "_" + st.ID]).ToList();
                    if (xVars.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(xVars) <= 1, "B8c");
                }
        }

        /// <summary>
        /// (OrderAtomicNoSplit) The no-split control: M1G's assignment semantics written in M4G's
        /// variables and prices. One indicator per (order, station) in each layer; assigning an
        /// order claims its WHOLE demand at that station, which is exactly M1G's shi5. There is no
        /// line object, so no lambda - see IM4GPrices.OrderAtomicNoSplit for why that is a removal
        /// of redundancy rather than a second change of policy.
        ///
        ///   (NS1) sum_o d[o,i] * yhat[o,w] &lt;= (Pb stock at w) + v[i,w]      [= shi5, tier-split]
        ///   (NS1a) v[i,w] &lt;= (Pa stock at w)                                 and V2a on sum_w v
        ///   (NS2) sum_w yhat[o,w] == fhat[o]                                  [= shi2, tightened]
        ///   (NS3) x[o,w] &lt;= yhat[o,w]                                        [= shi3]
        ///   (NS4) sum_w x[o,w]    == f[o]
        ///   B3 (slot capacity) is added by the caller and is the ONLY thing separating the layers.
        /// </summary>
        private void AddOrderAtomicConstraints(LinearModel wrapper, M4GSnapshot snap, M4GSymbols sym,
            VariableCollection<string> bin, M4GModel model)
        {
            // ── one (order, station) placement pair per candidate assignment ──
            foreach (var order in snap.PendingOrders)
            {
                int pinned = -1;
                bool isPinned = _nsStationPin.TryGetValue(order.ID, out pinned);
                int totalResidual = snap.Residuals[order].Values.Sum();
                var yhVars = new List<Variable>();
                var xVars = new List<Variable>();
                foreach (var station in snap.Cs.Keys)
                {
                    if (isPinned && station.ID != pinned) continue;
                    string yh = "oyh_" + order.ID + "_" + station.ID;
                    string xb = "y_" + order.ID + "_" + station.ID;
                    model.Places.Add(new M4GPlacement
                    {
                        Order = order, Sku = null, Station = station,
                        Units = totalResidual, GhName = yh, GbName = xb
                    });
                    // (NS3) binding assignment implies valuation assignment.
                    wrapper.AddConstr(bin[xb] <= bin[yh], "NS3");
                    yhVars.Add(bin[yh]);
                    xVars.Add(bin[xb]);
                }
                // (NS2/NS4) fhat and f ARE the assignment indicators - equality, not <=, so the
                // solver cannot draw an order's stock while leaving its completion flag at 0 to
                // dodge a price, the same reason V10 is an equality in the line-atomic model.
                if (yhVars.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(yhVars) == bin["zh_" + order.ID], "NS2");
                else
                    wrapper.AddConstr(bin["zh_" + order.ID] == 0, "NS2");
                if (xVars.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(xVars) == bin["z_" + order.ID], "NS4");
                else
                    wrapper.AddConstr(bin["z_" + order.ID] == 0, "NS4");
            }

            // ── (NS1) shi5: an assigned order's FULL demand must be covered by the aggregate
            // stock standing at that station. Split by pod tier so V2a survives unchanged. ──
            int vMax = 1;
            foreach (var sku in snap.PiSKU.Keys)
            {
                int total = snap.PendingOrders.Sum(o =>
                {
                    int need; Dictionary<ItemDescription, int> res;
                    return snap.Residuals.TryGetValue(o, out res) && res.TryGetValue(sku, out need) ? need : 0;
                });
                if (total > vMax) vMax = total;
            }
            VariableCollection<string> vPa = new VariableCollection<string>(
                wrapper, VariableType.Integer, 0, vMax, (string s) => { return s; });

            foreach (var sku in snap.PiSKU.Keys)
            {
                var vNames = new List<Variable>();
                foreach (var station in snap.Cs.Keys)
                {
                    var landed = new List<LinearExpression>();
                    foreach (var pl in model.Places.Where(p => p.Station.ID == station.ID))
                    {
                        int need; Dictionary<ItemDescription, int> res;
                        if (!snap.Residuals.TryGetValue(pl.Order, out res)
                            || !res.TryGetValue(sku, out need) || need <= 0) continue;
                        landed.Add(need * bin[pl.GhName]);
                    }
                    if (landed.Count == 0) continue;
                    var pbStock = new List<LinearExpression>();
                    var paStock = new List<LinearExpression>();
                    foreach (var pod in snap.PiSKU[sku])
                    {
                        string xn = "xps_" + pod.ID + "_" + station.ID;
                        if (!sym.Xps.Any(v => v.name == xn)) continue;
                        double s = pod.CountAvailable(sku);
                        if (s <= 0) continue;
                        (snap.Pa.Contains(pod) ? paStock : pbStock).Add(s * bin[xn]);
                    }
                    Variable vVar = vPa["vpa_" + sku.ID + "_" + station.ID];
                    vNames.Add(vVar);
                    LinearExpression supply = vVar;
                    foreach (var e in pbStock) supply = supply + e;
                    wrapper.AddConstr(LinearExpression.Sum(landed) <= supply, "NS1");
                    if (paStock.Count == 0)
                        wrapper.AddConstr(vVar <= 0, "NS1a");
                    else
                    {
                        LinearExpression pa = paStock[0];
                        for (int k = 1; k < paStock.Count; k++) pa = pa + paStock[k];
                        wrapper.AddConstr(vVar <= pa, "NS1a");
                    }
                }
                // (V2a deliberately ABSENT here.) Incremental valuation - deducting inbound Pb
                // supply from what newly dispatched Pa pods may be valued against - is an M4G
                // addition with no counterpart in M1G, whose shi5 is the bare stock inequality.
                // This arm exists to be M1G's constraint set with measured prices, so carrying
                // V2a would make it differ from the canon in TWO ways (the atom AND an extra
                // valuation bound) and stop the comparison isolating splitting.
                //
                // v[i,w] is still needed: NS1 has to know how much of the demand landing at w is
                // served out of Pa stock rather than sunk stock, which is what ties valuation to
                // the dispatch decision. Only the pooled cap on sum_w v is dropped.
            }
        }

        /// <summary>
        /// (TwoStagePricing stage 1) "At least one storage pod is dispatched this decision".
        /// Removes the null plan, so every feasible solution has D &gt; 0 and P &gt; 0 and standard
        /// Dinkelbach applies without any upward search. Returns silently when no Pa pod can go
        /// anywhere - the caller then has no stage-1 problem to solve and keeps the historical price.
        /// </summary>
        private void AddForceDispatch(LinearModel wrapper, M4GSnapshot snap, M4GSymbols sym,
            VariableCollection<string> bin)
        {
            var newPodVars = sym.Xps.Where(v => snap.Pa.Contains(v.pod))
                .Select(v => bin[v.name]).ToList();
            if (newPodVars.Count == 0) return;
            wrapper.AddConstr(LinearExpression.Sum(newPodVars) >= 1, "FD");
        }

        /// <summary>Adds the shared pod/bot constraints R1-R5 (spec 3.4).</summary>
        private void AddSharedConstraints(LinearModel wrapper, M4GSnapshot snap, M4GSymbols sym,
            VariableCollection<string> bin)
        {
            // (R1) each pod goes to at most one station
            // Guard: LinearExpression.Sum(IEnumerable<Variable>) calls .First() with no empty
            // check, so an empty filtered sequence throws. Materialise then skip when empty -
            // for R1 this is defensive only (Xps always has >=1 entry per pod given Cs is
            // non-empty, which the caller already guarantees before invoking SolveM4G).
            foreach (var pod in snap.AllPods)
            {
                var xpsList = sym.Xps.Where(v => v.pod.ID == pod.ID).Select(v => bin[v.name]).ToList();
                if (xpsList.Count == 0) continue;
                wrapper.AddConstr(LinearExpression.Sum(xpsList) <= 1, "R1");
            }
            // (R2) dispatching a storage pod requires a bot
            foreach (var pod in snap.Pa)
            {
                var xpsList = sym.Xps.Where(v => v.pod.ID == pod.ID).Select(v => bin[v.name]).ToList();
                if (xpsList.Count == 0) continue;
                var yrpList = sym.Yrp.Where(v => v.pod.ID == pod.ID).Select(v => bin[v.name]).ToList();
                if (yrpList.Count == 0)
                    // No bot can carry this pod this tick. Skipping the constraint outright
                    // would NOT be vacuous here - it would leave xps unconstrained and let the
                    // solver dispatch a pod with no carrier. Force no-dispatch instead, which is
                    // exactly what R2 means when its right-hand side is empty.
                    wrapper.AddConstr(LinearExpression.Sum(xpsList) <= 0, "R2");
                else
                    wrapper.AddConstr(LinearExpression.Sum(xpsList) <= LinearExpression.Sum(yrpList), "R2");
            }
            // (R3) each bot carries at most one pod
            foreach (var bot in snap.Ra)
            {
                var yrpList = sym.Yrp.Where(v => v.robot.ID == bot.ID).Select(v => bin[v.name]).ToList();
                if (yrpList.Count == 0) continue;   // vacuous: bot has no candidate pod at all
                wrapper.AddConstr(LinearExpression.Sum(yrpList) <= 1, "R3");
            }
            // (R4) each pod is carried by at most one bot
            foreach (var pod in snap.Pa)
            {
                var yrpList = sym.Yrp.Where(v => v.pod.ID == pod.ID).Select(v => bin[v.name]).ToList();
                if (yrpList.Count == 0) continue;   // vacuous: no bot can carry this pod
                wrapper.AddConstr(LinearExpression.Sum(yrpList) <= 1, "R4");
            }
            // (R5) already-committed pods are fixed to their destination and carrier
            foreach (var entry in snap.InboundPods)
                foreach (var pod in entry.Value.Where(p => snap.Pb.Contains(p)))
                {
                    wrapper.AddConstr(bin["xps_" + pod.ID + "_" + entry.Key.ID] == 1, "R5a");
                    wrapper.AddConstr(bin["yrp_" + snap.PodToBot[pod].ID + "_" + pod.ID] == 1, "R5b");
                }
        }

        /// <summary>
        /// Lookup tables over sym.Qhat, built once per solve. Every list preserves the order its
        /// elements have in sym.Qhat, which is exactly the order the LINQ Where clauses these
        /// replace produced - so constraints and objective terms are assembled from identical
        /// sequences and the model handed to the solver is unchanged, term for term. This is
        /// purely a complexity fix: each replaced scan was O(|Qhat|) and ran once per
        /// (order, sku) and once per (order, station), which dominated model build time.
        /// </summary>
        private sealed class M4GQhatIndex
        {
            private static readonly List<Symbol> Empty = new List<Symbol>();
            private readonly Dictionary<long, List<Symbol>> _byOrderSku = new Dictionary<long, List<Symbol>>();
            private readonly Dictionary<long, List<Symbol>> _byOrderStation = new Dictionary<long, List<Symbol>>();
            private readonly Dictionary<int, List<Symbol>> _byOrder = new Dictionary<int, List<Symbol>>();
            private readonly Dictionary<int, List<Symbol>> _bySku = new Dictionary<int, List<Symbol>>();
            private readonly Dictionary<int, List<Symbol>> _yrpByPod = new Dictionary<int, List<Symbol>>();

            private static long Key(int a, int b) { return ((long)a << 32) | (uint)b; }

            public M4GQhatIndex(M4GSymbols sym)
            {
                foreach (var v in sym.Qhat)
                {
                    Add(_byOrderSku, Key(v.order.ID, v.skui.ID), v);
                    Add(_byOrderStation, Key(v.order.ID, v.outputstation.ID), v);
                    Add(_byOrder, v.order.ID, v);
                    Add(_bySku, v.skui.ID, v);
                }
                foreach (var y in sym.Yrp) Add(_yrpByPod, y.pod.ID, y);
            }

            private static void Add<TKey>(Dictionary<TKey, List<Symbol>> map, TKey key, Symbol v)
            {
                List<Symbol> list;
                if (!map.TryGetValue(key, out list)) { list = new List<Symbol>(); map[key] = list; }
                list.Add(v);
            }
            private static List<Symbol> Get<TKey>(Dictionary<TKey, List<Symbol>> map, TKey key)
            {
                List<Symbol> list;
                return map.TryGetValue(key, out list) ? list : Empty;
            }

            public List<Symbol> ByOrderSku(int orderId, int skuId) { return Get(_byOrderSku, Key(orderId, skuId)); }
            public List<Symbol> ByOrderStation(int orderId, int stationId) { return Get(_byOrderStation, Key(orderId, stationId)); }
            public List<Symbol> ByOrder(int orderId) { return Get(_byOrder, orderId); }
            public List<Symbol> BySku(int skuId) { return Get(_bySku, skuId); }
            public List<Symbol> YrpByPod(int podId) { return Get(_yrpByPod, podId); }
        }

        /// <summary>Adds the valuation-layer constraints V1-V4 (spec 3.4).</summary>
        private void AddValuationConstraints(LinearModel wrapper, M4GSnapshot snap, M4GSymbols sym,
            VariableCollection<string> bin, VariableCollection<string> qh, M4GQhatIndex idx)
        {
            // (V1) draws from a pod at a station are bounded by its stock and require dispatch
            foreach (var group in sym.Qhat.GroupBy(v => new { sku = v.skui.ID, pod = v.pod.ID, st = v.outputstation.ID }))
            {
                var first = group.First();
                wrapper.AddConstr(LinearExpression.Sum(group.Select(v => qh[v.name]))
                    <= first.pod.CountAvailable(first.skui) * bin["xps_" + first.pod.ID + "_" + first.outputstation.ID],
                    "V1");
            }
            // Pre-compute, once per decision, how much of each demanded SKU is already covered
            // by pods committed to a station (Pb: being picked, queued, or en route). This is
            // the incremental-valuation fix (spec 2026-07-31): without it, the valuation layer
            // scores a fresh Pa pod against the raw backlog every decision even though pods
            // already dispatched for that same demand are on their way, so the same ~31 lines
            // get re-valued (and re-justify a new pod fetch) on every consecutive decision.
            // Deliberately system-wide per SKU, not per station - simpler, and a per-station
            // split is a follow-up refinement if this proves too coarse.
            //
            // The inbound-supply deduction is an AGGREGATE, once-per-SKU bound (fixed
            // 2026-07-31): deducting inboundSupply(i) separately for every order that demands
            // SKU i over-deducts (e.g. three orders each needing 2 units of i against 5 units
            // of inbound supply would each individually see max(0,2-5)=0 and could draw
            // nothing from Pa, even though inbound only covers 5 of the pooled 6 units of
            // demand). The Pa bound below is therefore summed over ALL orders/pods/stations
            // for a given SKU, deducted once against the pooled residual demand for that SKU.
            Dictionary<int, double> inboundSupplyBySkuId = null;
            Dictionary<int, double> totalResidualBySkuId = null;
            _lastInboundCoverUnits = 0.0;
            if (_m4gConfig.IncrementalValuationEnabled)
            {
                inboundSupplyBySkuId = new Dictionary<int, double>();
                foreach (var sku in snap.PiSKU.Keys)
                    inboundSupplyBySkuId[sku.ID] = snap.Pb.Sum(p => p.CountAvailable(sku));

                totalResidualBySkuId = new Dictionary<int, double>();
                foreach (var order in snap.PendingOrders)
                    foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                    {
                        double cur;
                        totalResidualBySkuId.TryGetValue(sku.Key.ID, out cur);
                        totalResidualBySkuId[sku.Key.ID] = cur + sku.Value;
                    }
            }

            foreach (var order in snap.PendingOrders)
                foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                {
                    var draws = idx.ByOrderSku(order.ID, sku.Key.ID)
                        .Select(v => qh[v.name]).ToList();
                    // (V2) never draw more than the residual demand - this per-(order,sku) bound
                    // holds unconditionally (incremental valuation or not); it is what stops any
                    // single order being over-served.
                    wrapper.AddConstr(LinearExpression.Sum(draws) <= sku.Value, "V2");
                    // (V3) a line only counts as closed when it is drawn in full
                    wrapper.AddConstr(LinearExpression.Sum(draws)
                        >= sku.Value * bin["ch_" + order.ID + "_" + sku.Key.ID], "V3");
                    // (V4) completing an order requires every one of its lines closed
                    wrapper.AddConstr(bin["ch_" + order.ID + "_" + sku.Key.ID] >= bin["zh_" + order.ID], "V4");
                }

            // (V4g) An order with a residual line that NO pod can cover this decision cannot be
            // completed this decision, so it must not collect the completion reward. V4 above can
            // only speak about lines that survived the PiSKU filter - an uncoverable line has no
            // ch symbol at all - so without this the solver sets zhat=1 for free and, through the
            // objective's -mu terms, is actively paid to prefer orders it cannot finish. z_o is
            // covered too: B6z already pins z <= zh. See HonestCompletionReward.
            if (_m4gConfig.HonestCompletionReward)
                foreach (var order in snap.PendingOrders)
                    if (!snap.Residuals[order].All(p => snap.PiSKU.ContainsKey(p.Key)))
                        wrapper.AddConstr(bin["zh_" + order.ID] == 0, "V4g");

            // (V7/V8, gated) ForbidSplitting: gives the valuation layer whole-order semantics -
            // the assignment structure of the M1G baseline - instead of the earlier per-line
            // single-supplier restriction (V6, removed; see remarks below on why it is dropped
            // rather than kept alongside V7/V8).
            //
            // M1G has no unit-level draw variable at all: an order is a single yos/yaos
            // assignment to at most one station (M1GManager.cs "shi2": sum_s yos[o,s] <= 1), and
            // once assigned the order's entire demand is serviced as a unit by the pick layer
            // outside the MILP - there is no MILP-level notion of "half an order". M4G's model is
            // unit-level by construction (qhat[o,i,p,s]), so reproducing that whole-order
            // structure here needs two explicit constraints that M1G gets for free from not having
            // a unit-level variable in the first place:
            //
            //   (V7) One station per order - yhat[o,s] mirrors M1G's yos[o,s]/shi2. A station's
            //   aggregate draw for one SKU of an order is 0 unless that station is the order's
            //   chosen one, and at most one station may be chosen.
            //
            //   (V8) All-or-nothing - for every SKU line, drawn units equal the FULL residual
            //   exactly when the order is valued as complete (zhat=1), and are exactly 0
            //   otherwise. This is the constraint M1G never needs, because it has no partial-draw
            //   variable to constrain; here it is the one that actually stops a split child from
            //   ever being created; the earlier per-line supplier restriction did not (WIP=50.8
            //   with only that constraint in place).
            //
            // (V8g) A pending order can have a residual SKU with zero PiSKU coverage this
            // decision (BuildSnapshot's PendingOrders filter only requires ANY line coverable,
            // not ALL - by design, so the unrestricted model can still serve the coverable part).
            // No qhat variable exists for an uncoverable line, so nothing above would otherwise
            // stop the solver setting zhat=1 "for free" while that line silently goes unserved.
            // Force zhat=0 for such orders so V8 correctly zeroes every other line's draws too -
            // this order simply cannot be completed this decision, which is exactly what
            // all-or-nothing means for it.
            if (_m4gConfig.ForbidSplitting)
            {
                foreach (var order in snap.PendingOrders)
                {
                    bool fullyCoverable = order.RemainingPositions.All(p => snap.PiSKU.ContainsKey(p.Key));
                    if (!fullyCoverable)
                    {
                        wrapper.AddConstr(bin["zh_" + order.ID] == 0, "V8g");
                        // Under the full restriction V8 ties every draw to zhat, so zhat = 0
                        // already zeroes them and the station machinery below is dead weight.
                        // Under ForbidCrossStationOnly there is no V8, so skipping ahead would
                        // leave this order with NO station restriction at all - free to draw from
                        // several at once, i.e. the very thing the arm forbids. Build V7a/V7b for
                        // it too; it simply cannot be COMPLETED this decision, which is what
                        // zhat = 0 says and what V4g says for the unrestricted model.
                        if (!_m4gConfig.ForbidCrossStationOnly && !_m4gConfig.WholeOrderDeferredFill)
                            continue;   // no candidate line can ever draw for this order this decision
                    }

                    var orderQhat = idx.ByOrder(order.ID);
                    if (orderQhat.Count == 0) continue;
                    var yhVars = new List<Variable>();
                    foreach (var station in orderQhat.Select(v => v.outputstation).Distinct())
                    {
                        string yhName = "yh_" + order.ID + "_" + station.ID;
                        bool anyLineAtStation = false;
                        foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                        {
                            var draws = idx.ByOrderSku(order.ID, sku.Key.ID)
                                .Where(v => v.outputstation.ID == station.ID)
                                .Select(v => qh[v.name]).ToList();
                            if (draws.Count == 0) continue;
                            anyLineAtStation = true;
                            // (V7t, gated) Tight counterpart of B9t - equality instead of the
                            // per-line cap, so a fractional yhat cannot carry a full valued draw.
                            if (_m4gConfig.TightWholeOrder)
                                wrapper.AddConstr(LinearExpression.Sum(draws) == sku.Value * bin[yhName], "V7t");
                            else
                                wrapper.AddConstr(LinearExpression.Sum(draws) <= sku.Value * bin[yhName], "V7a");
                        }
                        if (anyLineAtStation) yhVars.Add(bin[yhName]);
                    }
                    if (yhVars.Count > 0)
                    {
                        wrapper.AddConstr(LinearExpression.Sum(yhVars) <= 1, "V7b");
                        // (V8t) zhat is exactly "assigned to some station", the valuation-layer
                        // twin of B8t. Replaces V8's separate per-line equality below.
                        if (_m4gConfig.TightWholeOrder)
                            wrapper.AddConstr(bin["zh_" + order.ID] == LinearExpression.Sum(yhVars), "V8t");
                    }

                    foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                    {
                        var draws = idx.ByOrderSku(order.ID, sku.Key.ID)
                            .Select(v => qh[v.name]).ToList();
                        if (draws.Count == 0) continue;
                        if (_m4gConfig.TightWholeOrder) continue;   // implied by V7t + V8t
                        // (ForbidCrossStationOnly) V8 is the all-or-nothing half of the
                        // restriction. Dropping it leaves V7a/V7b - one station per order - while
                        // letting that station fill the order over several decisions, which is
                        // what M1G actually does. See IM4GPrices' sibling remark on the flag.
                        if (_m4gConfig.ForbidCrossStationOnly
                            || _m4gConfig.WholeOrderDeferredFill) continue;
                        wrapper.AddConstr(LinearExpression.Sum(draws) == sku.Value * bin["zh_" + order.ID], "V8");
                    }
                }
            }

            // (V9/V10, gated) LineAtomicSplitting: the valuation layer's atom becomes the LINE.
            // wh[o,i,s] is "line (o,i) is valued as closed at station s".
            //
            //   (V9)  sum_p qhat[o,i,p,s] == d[o,i] * wh[o,i,s]
            //         All-or-nothing PER STATION: a station either supplies the line's whole
            //         residual or none of it. Pods stay free inside the sum, so several pods at
            //         that station may jointly cover the line - the same freedom M5's
            //         CommitParts has when it walks DrawOrder across the station's pods.
            //   (V10) sum_s wh[o,i,s] == ch[o,i]
            //         At most one station per line, and the line counts as valued-closed exactly
            //         when some station took it. Equality (not <=) is what makes ch honest here:
            //         V3 alone only forces ch=0 when the draw is short, it never stops the
            //         solver drawing a full line and leaving ch=0 to dodge a price.
            //
            // Together these make every valued line either fully drawn at one station or absent,
            // which is exactly ValuationSweep's rule in GreedyM5Manager (`have < line.Value` =>
            // skip this station; `break` after the first station that fits).
            if (_m4gConfig.LineAtomicSplitting)
            {
                foreach (var order in snap.PendingOrders)
                    foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                    {
                        var lineQhat = idx.ByOrderSku(order.ID, sku.Key.ID);
                        if (lineQhat.Count == 0) continue;
                        var whVars = new List<Variable>();
                        foreach (var station in lineQhat.Select(v => v.outputstation).Distinct())
                        {
                            var draws = lineQhat.Where(v => v.outputstation.ID == station.ID)
                                .Select(v => qh[v.name]).ToList();
                            if (draws.Count == 0) continue;
                            string whName = "wh_" + order.ID + "_" + sku.Key.ID + "_" + station.ID;
                            wrapper.AddConstr(LinearExpression.Sum(draws) == sku.Value * bin[whName], "V9");
                            whVars.Add(bin[whName]);
                        }
                        if (whVars.Count > 0)
                            wrapper.AddConstr(LinearExpression.Sum(whVars)
                                == bin["ch_" + order.ID + "_" + sku.Key.ID], "V10");
                    }
            }

            // (V2a) Pa draws (newly dispatched storage pods) are valued, in aggregate per SKU,
            // only against demand that inbound (Pb) supply cannot already cover. One constraint
            // per SKU across every order/pod/station - see note above on why this must be an
            // aggregate deduction and not a per-order one.
            if (_m4gConfig.IncrementalValuationEnabled)
            {
                foreach (var skuId in totalResidualBySkuId.Keys)
                {
                    double inbound = inboundSupplyBySkuId.ContainsKey(skuId) ? inboundSupplyBySkuId[skuId] : 0.0;
                    double totalResidual = totalResidualBySkuId[skuId];
                    double paBound = Math.Max(0.0, totalResidual - inbound);
                    _lastInboundCoverUnits += Math.Min(totalResidual, inbound);

                    var paDraws = idx.BySku(skuId).Where(v => snap.Pa.Contains(v.pod))
                        .Select(v => qh[v.name]).ToList();
                    if (paDraws.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(paDraws) <= paBound, "V2a");
                }
            }

            // (V5) caps how much valuation credit a single dispatched pod can receive at a
            // station. Without this nothing bounds how many closable lines one pod is scored
            // for: a full simulation showed ~33 valued lines credited per decision against
            // ~1.4 actually bound, so a 20-40m pod trip earned credit worth far more than it
            // delivered and pile-on collapsed. Qmax approximates the physical ceiling: the
            // station's total slot capacity times the mean residual units per pending order -
            // more slots means more orders can be served in one pod visit, and orders with
            // more residual units per line inflate that further. This bounds CREDIT per pod,
            // not the number of pods dispatched - it is the missing physical bound, not a
            // tuning knob.
            if (_m4gConfig.PodCreditCapEnabled)
            {
                double meanResidualUnits = snap.PendingOrders.Count > 0
                    ? snap.PendingOrders.Average(o => snap.Residuals[o].Values.Sum())
                    : 1.0;
                foreach (var group in sym.Qhat.GroupBy(v => new { pod = v.pod.ID, st = v.outputstation.ID }))
                {
                    var first = group.First();
                    int qmax = Math.Max(1, (int)Math.Ceiling(first.outputstation.Capacity * meanResidualUnits));
                    _lastQmax = qmax;
                    wrapper.AddConstr(LinearExpression.Sum(group.Select(v => qh[v.name])) <=
                        qmax * bin["xps_" + first.pod.ID + "_" + first.outputstation.ID], "V5");
                }
            }
        }

        /// <summary>Qmax used by V5 for the most recent decision (diagnostics only; last value
        /// written wins, which is fine since Qmax only varies by station capacity within a
        /// single solve and the log records one row per decision, not per station).</summary>
        private int _lastQmax = 0;
        /// <summary>Total units of inbound (Pb) supply deducted from the valuation bound this
        /// decision (diagnostics only; last value written wins, same convention as
        /// <see cref="_lastQmax"/>).</summary>
        private double _lastInboundCoverUnits = 0.0;
        /// <summary>Orders skipped this decision by SplitOnlyAtPresentPods because at least one
        /// pod they draw from is not yet physically at its station (diagnostics only, same
        /// last-value-wins convention as <see cref="_lastQmax"/>).</summary>
        private int _lastSplitsDeferred = 0;
        /// <summary>Orders that required a split child this decision and were committed because
        /// every pod they draw from was already physically at its station.</summary>
        private int _lastSplitsCommitted = 0;

        /// <summary>
        /// Identifies which committed (Pb) pods are physically standing at their destination
        /// station's pick waypoint right now (Pp, "processing") - as opposed to queued (Pq) or
        /// still en route, both of which are priced 0 by the tier-draw term and so need no
        /// further distinction here. Mirrors SplitM2eICManager's `processingPods` collection
        /// (SplitM2eICManager.cs, around the block building `processingPods`/`icPpByStation`):
        /// a committed pod whose carrying bot's current waypoint equals the station's waypoint
        /// is being processed. SplitM2eICManager.cs is read-only reference material here, never
        /// modified (constitution 1.3).
        /// </summary>
        private HashSet<int> BuildProcessingPodIds(M4GSnapshot snap)
        {
            HashSet<int> processing = new HashSet<int>();
            foreach (var station in snap.Cs.Keys)
                foreach (var pod in snap.Pb)
                {
                    Bot bot;
                    if (station.Waypoint != null && snap.PodToBot.TryGetValue(pod, out bot)
                        && bot.CurrentWaypoint != null && bot.CurrentWaypoint.ID == station.Waypoint.ID)
                        processing.Add(pod.ID);
                }
            return processing;
        }

        /// <summary>
        /// Identifies which committed (Pb) pods are physically present at their destination
        /// station right now - either being processed at the pick waypoint or waiting on one of
        /// the station's queue waypoints - as opposed to still en route. Faithful mirror of the
        /// Pp/Pq classification built by SplitM2eICManager.cs (the icPpByStation/icQueuedByStation
        /// block, around the loop over `inboundPods`/`Pb`/`PodToBot` keyed by
        /// `bot.CurrentWaypoint.ID == station.Waypoint.ID` for processing and
        /// `icWayp.IsQueueWaypoint` for queued) and by GreedyM3GManager.PodDrawTier (tier 0/1 vs 2).
        /// Both are read-only reference material here, never modified (constitution 1.3).
        /// Used only by SplitOnlyAtPresentPods; unreferenced when that flag is off.
        /// </summary>
        private HashSet<int> BuildPresentPodIds(M4GSnapshot snap)
        {
            HashSet<int> present = new HashSet<int>();
            foreach (var entry in snap.InboundPods)
            {
                OutputStation station = entry.Key;
                foreach (var pod in entry.Value)
                {
                    if (!snap.Pb.Contains(pod)) continue;
                    Bot bot;
                    if (!snap.PodToBot.TryGetValue(pod, out bot) || bot == null) continue;
                    var wp = bot.CurrentWaypoint;
                    if (wp == null) continue;
                    if (station.Waypoint != null && wp.ID == station.Waypoint.ID)
                        present.Add(pod.ID);          // processing: bot at the pick waypoint
                    else if (wp.IsQueueWaypoint)
                        present.Add(pod.ID);          // queued: bot waiting at a queue waypoint
                }
            }
            return present;
        }

        /// <summary>
        /// Mirrors M1GManager.M1GBotPodCost (private in the base class, so inaccessible to a
        /// subclass - constitution requires mirroring rather than touching M1GManager.cs). The
        /// base version branches on starve-aware travel time; M4G's constructor hard-throws if
        /// StarveAwareCostEnabled is set (metres-only invariant, spec 3.5), so that branch is
        /// unreachable here and this mirror is just the raw-distance path.
        /// </summary>
        private double M1GBotPodCost(Bot robot, Pod pod)
        {
            return EstimateBotPodDistance(robot, pod);
        }

        /// <summary>Mirrors M1GManager.M1GPodStationCost for the same reason as
        /// <see cref="M1GBotPodCost"/> - only the non-starve-aware (raw distance) path applies.</summary>
        private double M1GPodStationCost(Pod pod, OutputStation station)
        {
            return EstimatePodStationDistance(pod, station);
        }
        /// <summary>Outcome of one M4G solve. Only BoundDraws has side effects downstream.</summary>
        private sealed class M4GResult
        {
            public bool HasSolution;
            public double Objective;
            /// <summary>(Diagnostic only.) Gurobi's relative MIP gap when the solve stopped.
            /// Meaningful only when a decision time limit is set; 0 on a solve that reached
            /// proven optimality, NaN when the attribute is unavailable.</summary>
            public double Gap;
            public double SolveSec;
            public int NewTripCount;
            /// <summary>Binding-layer draws: (sku, order, pod, station) -> units.</summary>
            public Dictionary<Symbol, int> BoundDraws = new Dictionary<Symbol, int>();
            /// <summary>Bot chosen for each newly dispatched pod.</summary>
            public Dictionary<int, Bot> BotByPodId = new Dictionary<int, Bot>();
            /// <summary>Line keys the valuation layer closed.</summary>
            /// <summary>(ValuationFidelityLog) Units this decision's VALUATION layer claimed from
            /// each (pod, order, sku) but the binding layer did NOT take. Keyed "pod:order:sku" so
            /// a later draw can be checked against the pod that made the promise - the same-pod
            /// realisation rate the line-level ValuedLineKeys cannot see.</summary>
            public Dictionary<string,int> PodPromisedUnits = new Dictionary<string,int>();
            /// <summary>(ValuationFidelityLog) Units this decision's BINDING layer actually drew,
            /// same key shape.</summary>
            public Dictionary<string,int> PodDrawnUnits = new Dictionary<string,int>();
            public HashSet<string> ValuedLineKeys = new HashSet<string>();
            /// <summary>Line keys the binding layer closed.</summary>
            public HashSet<string> BoundLineKeys = new HashSet<string>();
            public int ValuedOrders;
            public int BoundOrders;
            /// <summary>rho used to price this solve's binding-layer draws (diagnostics).</summary>
            public double Rho;
            /// <summary>Bound units drawn from a sunk (Pp+Pq+Pb) pod.</summary>
            public int UnitsFromSunk;
            /// <summary>Bound units drawn from a newly dispatched (Pa) pod.</summary>
            public int UnitsFromNew;
            /// <summary>Realised distance D* of this solution: the sum of the T1/T2 travel-cost
            /// terms (new-pod dispatch/carry cost) actually taken, read back from the solved
            /// bin variables - not estimated. Lambda-independent; used only by the Dinkelbach
            /// iteration to derive the next lambda.</summary>
            public double DStar;
            /// <summary>The lambda this solve was built with (diagnostics / Dinkelbach bookkeeping).</summary>
            public double LambdaUsed;
            /// <summary>Residual lines summed over the orders the VALUATION layer scored complete
            /// (diagnostics, ValuationFidelityLog). Paired with <see cref="BoundOrderResidualLines"/>
            /// this measures whether mu's one-for-one order exchange hides a size asymmetry.</summary>
            public int ValuedOrderResidualLines;
            /// <summary>Residual lines summed over the orders the BINDING layer completed.</summary>
            public int BoundOrderResidualLines;
        }

        /// <summary>
        /// Builds and solves the M4G model at a given lambda. One solve decides pods, bots, the
        /// valuation-layer split shape and the binding-layer subset jointly (spec D2) - never in
        /// two passes.
        ///
        /// Every value coefficient in the objective (mu, epsilon, rho) is a fixed multiple of
        /// lambda - mu = MuScale*lambda*linesPerOrder and epsilon = EpsilonScale*lambda by their
        /// own formulas, and rho is held proportional to lambda by construction here (its
        /// measured formula, cumDist/cumUnitsPicked, does not itself depend on lambda, so the
        /// ratio rho0/lambda0 computed from the historical lambda is carried forward unchanged
        /// as lambda is varied by the Dinkelbach iteration). This lets the whole value side of
        /// the objective be written lambda * V with V lambda-independent, which is what makes
        /// the Dinkelbach linearisation (minimise D - lambda*V) valid at any lambda, not just
        /// the historical one.
        /// </summary>
        /// <param name="snap">The decision snapshot.</param>
        /// <param name="lambda">Lambda to build this solve's objective with.</param>
        /// <param name="lambda0">The historical (as-measured) lambda, used only to scale mu/epsilon/rho
        /// proportionally to <paramref name="lambda"/> - see remarks above.</param>
        /// <param name="mu0">mu evaluated at lambda0.</param>
        /// <param name="epsilon0">epsilon evaluated at lambda0.</param>
        /// <param name="rho0">rho evaluated at lambda0.</param>
        /// <param name="delta">delta - lambda-independent, computed once per decision.</param>
        /// <summary>
        /// One decision's Gurobi model plus the symbol tables it was built from. Holds only the
        /// lambda-INDEPENDENT part: every variable and every constraint (R, V and B series). None
        /// of them reference lambda, mu, delta, rho or epsilon - only the objective does - so a
        /// Dinkelbach iteration needs nothing but a fresh SetObjective on this same model, and the
        /// constraint system it re-solves against is by construction the identical one.
        /// </summary>
        private sealed class M4GModel : IDisposable
        {
            public LinearModel Wrapper;
            public M4GSymbols Sym;
            public M4GQhatIndex Idx;
            public VariableCollection<string> Bin;
            public VariableCollection<string> Qh;
            public VariableCollection<string> Qb;
            /// <summary>(CompactLineModel) true when Qh/Qb were never created and the draw
            /// structure lives in the ghat/g binaries instead. Read-back must then synthesise
            /// BoundDraws greedily rather than reading q.</summary>
            public bool Compact;
            /// <summary>(OrderAtomicNoSplit) true when the atom is the whole order, so Places
            /// carries one entry per (order, station) with Sku == null.</summary>
            public bool OrderAtomic;
            /// <summary>(CompactLineModel) One entry per (order, sku, station) line-placement
            /// candidate, carrying the ghat/g variable names. Empty unless Compact.</summary>
            public List<M4GPlacement> Places = new List<M4GPlacement>();
            /// <summary>Orders that acquired an "open_o" indicator (WIP holding, gated).</summary>
            public List<int> OpenOrderIds = new List<int>();
            /// <summary>Upper bound for the legacy idle-slot integer variables.</summary>
            public int MaxSlots;
            /// <summary>(SlotScale, experimental) Idle-slot slack variables, one per station.
            /// Null when the flag is off, which is what keeps the default path untouched.</summary>
            public VariableCollection<string> Us;
            /// <summary>Names of the idle-slot slacks, in station order. Empty when off.</summary>
            public List<string> UsNames = new List<string>();
            public void Dispose()
            {
                if (Wrapper != null) { Wrapper.Dispose(); Wrapper = null; }
            }
        }

        /// <summary>
        /// Builds the decision's variables and constraints - everything that does not depend on
        /// lambda. Returns null when the snapshot yields no draw candidates at all. The caller owns
        /// the returned model and must dispose it once every value has been read back out of the
        /// solved variables (GetValue() queries the live Gurobi model, it does not cache).
        /// </summary>
        private M4GModel BuildModel(M4GSnapshot snap) { return BuildModel(snap, false); }

        /// <param name="forceDispatch">(TwoStagePricing stage 1) Adds "at least one new pod is
        /// dispatched", which removes the null plan from the feasible set and makes the ratio
        /// problem non-degenerate. See IM4GPrices.TwoStagePricing.</param>
        private M4GModel BuildModel(M4GSnapshot snap, bool forceDispatch)
        {
            M4GSymbols sym = BuildSymbols(snap);
            if (sym.Qhat.Count == 0) return null;
            M4GQhatIndex idx = new M4GQhatIndex(sym);

            LinearModel wrapper = new LinearModel(SolverType.Gurobi, (string s) => { Console.Write(s); });
            if (_m4gConfig.MipGap >= 0.0)
                wrapper.SetMipGap(_m4gConfig.MipGap);
            if (_m4gConfig.DecisionTimeLimitSec > 0.0)
                wrapper.SetTimeLimit(_m4gConfig.DecisionTimeLimitSec);
            if (_m4gConfig.DecisionWorkLimit > 0.0)
                wrapper.SetWorkLimit(_m4gConfig.DecisionWorkLimit);
            try
            {
            int maxUnits = snap.Residuals.Count > 0
                ? snap.Residuals.Values.SelectMany(d => d.Values).DefaultIfEmpty(1).Max() : 1;
            int maxSlots = snap.Cs.Count > 0 ? snap.Cs.Values.DefaultIfEmpty(1).Max() : 1;
            VariableCollection<string> bin = new VariableCollection<string>(wrapper, VariableType.Binary, 0, 1,
                (string s) => { return s; });
            // (CompactLineModel) The unit-level variables are never created; the draw structure
            // lives in the placement binaries instead. Everything that does not touch q - the
            // resource layer, the completion couplings, slot capacity and the objective - is
            // stated identically, so this branch is a re-encoding of the same model, not a
            // different one. Requires LineAtomicSplitting: without it the line is not the atom and
            // the transportation argument that licenses dropping q does not hold.
            if (_m4gConfig.CompactLineModel && _m4gConfig.LineAtomicSplitting)
            {
                M4GModel compact = new M4GModel
                {
                    Wrapper = wrapper, Sym = sym, Idx = idx, Bin = bin,
                    Qh = null, Qb = null, Compact = true, MaxSlots = maxSlots
                };
                _lastInboundCoverUnits = 0.0;
                AddSharedConstraints(wrapper, snap, sym, bin);
                compact.OrderAtomic = _m4gConfig.OrderAtomicNoSplit;
                if (compact.OrderAtomic)
                    AddOrderAtomicConstraints(wrapper, snap, sym, bin, compact);
                else
                    AddCompactConstraints(wrapper, snap, sym, bin, compact);
                foreach (var order in snap.PendingOrders)
                {
                    // (V4) completing an order requires every one of its lines closed. Vacuous in
                    // the order-atomic arm, where there is no line variable to require.
                    if (!compact.OrderAtomic)
                    foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                        wrapper.AddConstr(bin["ch_" + order.ID + "_" + sku.Key.ID]
                            >= bin["zh_" + order.ID], "V4");
                    // (V4g) an order with a line no pod can cover must not collect the reward.
                    if (_m4gConfig.HonestCompletionReward
                        && !snap.Residuals[order].All(p => snap.PiSKU.ContainsKey(p.Key)))
                        wrapper.AddConstr(bin["zh_" + order.ID] == 0, "V4g");
                    // (B7) a bound completion needs every line bound-closed. Vacuous likewise.
                    if (!compact.OrderAtomic)
                    foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                        wrapper.AddConstr(bin["c_" + order.ID + "_" + sku.Key.ID]
                            >= bin["z_" + order.ID], "B7");
                    // (B6z) binding completion is a subset of valued completion.
                    wrapper.AddConstr(bin["z_" + order.ID] <= bin["zh_" + order.ID], "B6z");
                }
                // (B3) slot capacity - the model's core scarcity, and the ONLY constraint the
                // valuation layer does not see.
                foreach (var station in snap.Cs.Keys)
                {
                    var ys = snap.PendingOrders.Select(o => bin["y_" + o.ID + "_" + station.ID]).ToList();
                    if (ys.Count > 0)
                        wrapper.AddConstr(LinearExpression.Sum(ys) <= snap.Cs[station], "B3");
                }
                if (forceDispatch) AddForceDispatch(wrapper, snap, sym, bin);
                return compact;
            }

            VariableCollection<string> qh = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxUnits,
                (string s) => { return s; });
            VariableCollection<string> qb = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxUnits,
                (string s) => { return s; });

            AddSharedConstraints(wrapper, snap, sym, bin);
            AddValuationConstraints(wrapper, snap, sym, bin, qh, idx);

            // Orders that acquired an "open_o" indicator this solve (WIP holding, gated).
            List<int> openOrderIds = new List<int>();

            // ── Binding layer B1-B7 (spec 3.4) ──
            foreach (var v in sym.Qhat)
            {
                string qbName = "q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID;
                // (B1) binding is a subset of valuation. DegenerateToBindingOnly forces equality,
                // collapsing the layers back to M3G-like behaviour for the ablation arm.
                if (_m4gConfig.DegenerateToBindingOnly)
                    wrapper.AddConstr(qb[qbName] == qh[v.name], "B1eq");
                else
                    wrapper.AddConstr(qb[qbName] <= qh[v.name], "B1");
            }
            foreach (var order in snap.PendingOrders)
            {
                int totalResidual = snap.Residuals[order].Values.Sum();
                var boundStationVars = new List<Variable>();
                foreach (var station in snap.Cs.Keys)
                {
                    var draws = idx.ByOrderStation(order.ID, station.ID)
                        .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                        .ToList();
                    if (draws.Count == 0) continue;
                    string yName = "y_" + order.ID + "_" + station.ID;
                    // (B2) drawing for an order at a station occupies one of its slots
                    wrapper.AddConstr(LinearExpression.Sum(draws) <= totalResidual * bin[yName], "B2");
                    // (B4) no empty binding
                    wrapper.AddConstr(bin[yName] <= LinearExpression.Sum(draws), "B4");
                    boundStationVars.Add(bin[yName]);
                }
                // (B8, gated) One station per order in the binding layer too - the same shi2
                // mirror as V7, applied to y[o,s] (already the binding layer's order-station
                // indicator, unlike the valuation layer which needed a new yhat).
                if (_m4gConfig.ForbidSplitting && boundStationVars.Count > 0)
                {
                    wrapper.AddConstr(LinearExpression.Sum(boundStationVars) <= 1, "B8");
                    // (B8t) With B9t pinning every draw to y, "assigned somewhere" and
                    // "bound-complete" are the same event, so z is an equality rather than being
                    // squeezed from both sides by B7 and B9. This is what removes the last piece
                    // of slack the old formulation left around z.
                    if (_m4gConfig.TightWholeOrder)
                        wrapper.AddConstr(bin["z_" + order.ID] == LinearExpression.Sum(boundStationVars), "B8t");
                }
                // (B10, gated) "Opened but not closed" indicator for orders that carry no WIP yet.
                // open_o >= y_os - z_o at every station forces open_o to 1 whenever the order is
                // bound somewhere and does not complete. Only a lower bound is needed: the objective
                // charges open_o a positive kappa, so the solver drives it to that bound and never
                // sets it gratuitously. Orders that ALREADY carry WIP are excluded here - for them
                // "stay open" is the do-nothing outcome, so the charge degenerates to a constant plus
                // a completion bonus, which the objective applies directly (see WipHoldingEnabled).
                if (_m4gConfig.WipHoldingEnabled && !snap.WipOrders.Contains(order) && boundStationVars.Count > 0)
                {
                    string openName = "open_" + order.ID;
                    foreach (var yv in boundStationVars)
                        wrapper.AddConstr(bin[openName] >= yv - bin["z_" + order.ID], "B10");
                    openOrderIds.Add(order.ID);
                }
                foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                {
                    var skuDraws = idx.ByOrderSku(order.ID, sku.Key.ID)
                        .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                        .ToList();
                    // Guard: structurally this can't be empty (any sku that survives the
                    // PiSKU-containment filter has >=1 pod x >=1 station, since Cs is
                    // non-empty), but keep the guard for consistency with the other summed
                    // sequences rather than relying on that invariant never breaking.
                    if (skuDraws.Count == 0) continue;
                    string cName = "c_" + order.ID + "_" + sku.Key.ID;
                    // (B5) a bound line closure needs the full residual bound
                    wrapper.AddConstr(LinearExpression.Sum(skuDraws) >= sku.Value * bin[cName], "B5");
                    // (B6) binding closures are a subset of valued closures
                    wrapper.AddConstr(bin[cName] <= bin["ch_" + order.ID + "_" + sku.Key.ID], "B6c");
                    // (B9, gated) All-or-nothing in the binding layer: even though V8 already
                    // forces the valuation layer to draw a full order or nothing, B1 (qb <= qh)
                    // still lets the binding layer take only PART of a valued order's draws when
                    // station slot capacity is tight this tick - which would silently create a
                    // split child despite V8. This equality closes that gap: bound draws equal
                    // the full residual exactly when the order is bound-complete (z=1, mirroring
                    // zhat in V8), 0 otherwise - so under slot pressure the order is deferred
                    // whole, never partially committed.
                    // (ForbidCrossStationOnly) B9 is V8's binding-layer twin and the same half of
                    // the restriction, so it drops with it - the order stays pinned to one station
                    // by B8 but may be committed in pieces over successive decisions.
                    if (_m4gConfig.ForbidSplitting && !_m4gConfig.TightWholeOrder
                        && !_m4gConfig.ForbidCrossStationOnly && !_m4gConfig.WholeOrderDeferredFill)
                        wrapper.AddConstr(LinearExpression.Sum(skuDraws) == sku.Value * bin["z_" + order.ID], "B9");
                    // (B9t, gated) Tight replacement for B9: pin each line's draw AT EACH STATION
                    // to that station's assignment indicator. Same integer-feasible set as
                    // B2+B8+B9, but with no big-M, so the relaxation is exact at every vertex where
                    // y is integral instead of allowing a fractional y to carry a full draw.
                    if (_m4gConfig.ForbidSplitting && _m4gConfig.TightWholeOrder)
                        foreach (var station in snap.Cs.Keys)
                        {
                            var stDraws = idx.ByOrderSku(order.ID, sku.Key.ID)
                                .Where(v => v.outputstation.ID == station.ID)
                                .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                                .ToList();
                            if (stDraws.Count == 0) continue;
                            wrapper.AddConstr(LinearExpression.Sum(stDraws)
                                == sku.Value * bin["y_" + order.ID + "_" + station.ID], "B9t");
                        }
                    // (B11/B12, gated) LineAtomicSplitting in the binding layer - the mirror of
                    // V9/V10, and the one that actually decides what gets committed. V9/V10 alone
                    // are not enough for the same reason B9 is needed alongside V8: B1
                    // (qb <= qh) still lets the binding layer take only PART of a line-atomic
                    // valued draw when slot capacity is tight, which would create exactly the
                    // sub-line split child this arm exists to forbid.
                    //
                    // No explicit w <= wh is needed: B1 elementwise plus the two equalities give
                    // d[o,i]*w[o,i,s] <= d[o,i]*wh[o,i,s] at every station already.
                    if (_m4gConfig.LineAtomicSplitting)
                    {
                        var lineQb = idx.ByOrderSku(order.ID, sku.Key.ID);
                        var wVars = new List<Variable>();
                        foreach (var station in lineQb.Select(v => v.outputstation).Distinct())
                        {
                            var stDraws = lineQb.Where(v => v.outputstation.ID == station.ID)
                                .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                                .ToList();
                            if (stDraws.Count == 0) continue;
                            string wName = "w_" + order.ID + "_" + sku.Key.ID + "_" + station.ID;
                            wrapper.AddConstr(LinearExpression.Sum(stDraws) == sku.Value * bin[wName], "B11");
                            wVars.Add(bin[wName]);
                        }
                        if (wVars.Count > 0)
                            wrapper.AddConstr(LinearExpression.Sum(wVars) == bin[cName], "B12");
                    }
                    // (B7) a bound completion needs every line bound-closed
                    wrapper.AddConstr(bin[cName] >= bin["z_" + order.ID], "B7");
                }
                wrapper.AddConstr(bin["z_" + order.ID] <= bin["zh_" + order.ID], "B6z");
            }
            // (B3) slot capacity - an inequality, unlike M3G's eshi4 equality
            foreach (var station in snap.Cs.Keys)
            {
                var ys = snap.PendingOrders.Select(o => bin["y_" + o.ID + "_" + station.ID]).ToList();
                if (ys.Count > 0)
                    wrapper.AddConstr(LinearExpression.Sum(ys) <= snap.Cs[station], "B3");
            }

            // (SlotScale, experimental) Idle-slot slack: occupied + us == Cs, priced in the
            // objective. Mirrors the LegacyIdle machinery above, but with a self-calibrated price
            // instead of a hand-set weight. Added ALONGSIDE B3 rather than replacing it - the
            // equality subsumes B3, so B3 becomes redundant, not wrong, and leaving it in place
            // keeps the flag-off path a strict subset of the flag-on model.
            VariableCollection<string> usVars = null;
            List<string> usNames = new List<string>();
            if (_m4gConfig.SlotScale > 0 || _m4gConfig.LexicographicSlotFill)
            {
                // Only (order, station) pairs that actually own a y variable may be summed - the
                // same restriction the LegacyIdle block relies on. Referencing an undeclared y
                // would silently manufacture a free binary the solver could set to shrink us.
                HashSet<string> ownsY = new HashSet<string>(
                    sym.Qhat.Select(v => v.order.ID + "_" + v.outputstation.ID));
                usVars = new VariableCollection<string>(
                    wrapper, VariableType.Integer, 0, maxSlots, (string s) => { return s; });
                foreach (var station in snap.Cs.Keys)
                {
                    var ys = snap.PendingOrders
                        .Where(o => ownsY.Contains(o.ID + "_" + station.ID))
                        .Select(o => bin["y_" + o.ID + "_" + station.ID]).ToList();
                    string usName = "us_" + station.ID;
                    LinearExpression occupied = ys.Count > 0
                        ? LinearExpression.Sum(ys)
                        : LinearExpression.Sum(new List<LinearExpression>(), wrapper);
                    wrapper.AddConstr(occupied + usVars[usName] == snap.Cs[station], "B3s");
                    usNames.Add(usName);
                }
            }

            if (forceDispatch) AddForceDispatch(wrapper, snap, sym, bin);
            return new M4GModel
            {
                Wrapper = wrapper, Sym = sym, Idx = idx, Bin = bin, Qh = qh, Qb = qb,
                OpenOrderIds = openOrderIds, MaxSlots = maxSlots,
                Us = usVars, UsNames = usNames
            };
            }
            catch
            {
                wrapper.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Sets this solve's objective on an already-built model and optimises it. Called once per
        /// Dinkelbach iteration; every call replaces the objective outright and leaves the
        /// constraint system untouched.
        /// </summary>
        /// <summary>
        /// (TieredBetaBySunkCoverage) Line keys "orderId_skuId" whose full residual is already
        /// coverable by the sunk pods alone. Their deferred closure rides a trip that has already
        /// been charged, so the valuation layer credits them at the full lambda rather than at the
        /// measured conversion share.
        /// </summary>
        private HashSet<string> BuildSunkCoveredLines(M4GSnapshot snap)
        {
            // Collect the demanded SKUs once, keyed by id so the set is stable and loggable.
            Dictionary<int, ItemDescription> skuById = new Dictionary<int, ItemDescription>();
            foreach (var order in snap.PendingOrders)
                foreach (var line in snap.Residuals[order])
                    if (line.Value > 0 && !skuById.ContainsKey(line.Key.ID))
                        skuById[line.Key.ID] = line.Key;
            Dictionary<int, int> stock = new Dictionary<int, int>();
            foreach (var kv in skuById)
            {
                int total = 0;
                foreach (var pod in snap.Pb) total += pod.CountAvailable(kv.Value);
                stock[kv.Key] = total;
            }
            HashSet<string> covered = new HashSet<string>();
            foreach (var order in snap.PendingOrders)
                foreach (var line in snap.Residuals[order])
                {
                    int have;
                    if (line.Value > 0 && stock.TryGetValue(line.Key.ID, out have) && have >= line.Value)
                        covered.Add(order.ID + "_" + line.Key.ID);
                }
            return covered;
        }

        /// <summary>
        /// (ScarcityWeightedBinding) Per-SKU scarcity of SUNK supply against outstanding demand:
        /// max(0, 1 - sunkStock / backlogDemand), in [0,1].
        ///
        /// 0 means the pods already committed cover every open unit of this SKU, so postponing any
        /// of its lines costs nothing - a trip that has already been paid for will serve them.
        /// 1 means no sunk supply exists at all, so every deferred line of this SKU will need a
        /// fresh dispatch. The weight the objective applies is (1 + scarcity), i.e. a line nobody
        /// else can cover is worth up to twice one that is amply covered.
        ///
        /// First-order approximation: computed from the PRE-decision books, so it ignores the
        /// stock this decision is about to consume and therefore under-states crowding-out.
        /// </summary>
        private Dictionary<int, double> BuildSkuScarcity(M4GSnapshot snap)
        {
            Dictionary<int, ItemDescription> skuById = new Dictionary<int, ItemDescription>();
            Dictionary<int, int> demand = new Dictionary<int, int>();
            foreach (var order in snap.PendingOrders)
                foreach (var line in snap.Residuals[order])
                {
                    if (line.Value <= 0) continue;
                    if (!skuById.ContainsKey(line.Key.ID)) skuById[line.Key.ID] = line.Key;
                    int cur;
                    demand[line.Key.ID] = (demand.TryGetValue(line.Key.ID, out cur) ? cur : 0) + line.Value;
                }
            Dictionary<int, double> scarcity = new Dictionary<int, double>();
            foreach (var kv in skuById)
            {
                int sunk = 0;
                foreach (var pod in snap.Pb) sunk += pod.CountAvailable(kv.Value);
                int need = demand[kv.Key];
                double s = need <= 0 ? 0.0 : 1.0 - (double)sunk / need;
                scarcity[kv.Key] = s < 0.0 ? 0.0 : s;
            }
            return scarcity;
        }

        /// <summary>
        /// (ScarcityWeightedCompletion) Per-ORDER difficulty: the maximum per-SKU scarcity over the
        /// order's open lines, in [0,1]. Max rather than mean because one line nobody has sunk
        /// stock for already forces a fresh dispatch if the order is deferred - the other lines
        /// being easy does not make the order cheap to finish later.
        /// </summary>
        private Dictionary<int, double> BuildOrderScarcity(M4GSnapshot snap)
        {
            Dictionary<int, double> perSku = BuildSkuScarcity(snap);
            Dictionary<int, double> perOrder = new Dictionary<int, double>();
            foreach (var order in snap.PendingOrders)
            {
                double worst = 0.0;
                foreach (var line in snap.Residuals[order])
                {
                    if (line.Value <= 0 || !snap.PiSKU.ContainsKey(line.Key)) continue;
                    double w;
                    if (perSku.TryGetValue(line.Key.ID, out w) && w > worst) worst = w;
                }
                perOrder[order.ID] = worst;
            }
            return perOrder;
        }

        private M4GResult SolveM4G(M4GModel model, M4GSnapshot snap, double lambda, double lambda0,
            double mu0, double epsilon0, double rho0, double delta, double deltaOrder,
            bool applySlotTieBreak = false)
        {
            M4GResult result = new M4GResult();
            LinearModel wrapper = model.Wrapper;
            M4GSymbols sym = model.Sym;
            M4GQhatIndex idx = model.Idx;
            VariableCollection<string> bin = model.Bin;
            VariableCollection<string> qb = model.Qb;
            VariableCollection<string> qh = model.Qh;
            List<int> openOrderIds = model.OpenOrderIds;
            int maxSlots = model.MaxSlots;

            // ── Objective T1-T5 (spec 3.3), everything denominated in metres ──
            // mu/epsilon/rho are scaled proportionally from their lambda0 values to lambda -
            // see the remarks on SolveM4G above for why this is exact (mu, epsilon) or a
            // deliberate modelling choice that preserves the Dinkelbach linearity (rho).
            double lambdaScaleRatio = lambda0 > 0 ? lambda / lambda0 : 1.0;
            double mu = mu0 * lambdaScaleRatio;
            double epsilon = epsilon0 * lambdaScaleRatio;

            // T1/T2: only newly dispatched (Pa) pods pay travel - Pb trips are sunk.
            // Both terms use the (expressions, defaultSolver) Sum overload, which already
            // returns a zero expression for an empty sequence - no guard needed here.
            LinearExpression objective =
                LinearExpression.Sum(sym.Xps.Where(v => snap.Pa.Contains(v.pod))
                    .Select(v => bin[v.name] * (M1GPodStationCost(v.pod, v.outputstation)
                        + PodStationExtraCost(v.pod, v.outputstation))), wrapper)
                + LinearExpression.Sum(sym.Yrp.Where(v => snap.Pa.Contains(v.pod) && v.pod.Waypoint != null)
                    .Select(v => bin[v.name] * M1GBotPodCost(v.robot, v.pod)), wrapper);
            double rho = rho0 * lambdaScaleRatio;
            if (_m4gConfig.LegacyObjective)
            {
                // Legacy M1G value side (spec: Xie et al. 2021 s.3.3 construction). M1G's yos[o,s]
                // reward is per (order, station) because M1G forbids splitting, so an order can
                // only ever be assigned to one station and per-station == per-order there. Once
                // splitting is allowed, the reward must move to a variable that is 1 exactly once
                // per order regardless of how many stations serve it, or a split order would
                // collect w2 twice - an artefact of the relaxation, not a faithful legacy score.
                // z_[order.ID] (bin["z_" + order.ID]) already IS that variable: it is keyed only
                // by order.ID (see the binding-layer loop above), forced to 0/1 by B6z/B7/B5
                // irrespective of how many stations' draws feed it, and reaches 1 exactly when
                // every line of the order is bound-closed in aggregate across all stations - i.e.
                // "the order is served," matching Xie et al.'s y_o (constraint set 12). No new
                // variable is introduced; reusing z_o is what keeps the reward from being
                // double-counted under splitting.
                //
                // lambda/mu/delta/epsilon/rho (T3-T6 below, in the else branch) are all scaled
                // proportionally to lambda, which only makes sense under the Dinkelbach ratio
                // parameterisation - LegacyObjective replaces the whole value side with this fixed,
                // lambda-independent term, so none of T3-T6 apply here. The caller (DecideAbout
                // PendingOrders) also skips the Dinkelbach iteration entirely in this mode, since
                // the ratio objective this loop optimises is undefined without a lambda-scaled
                // value side to linearise.
                // (LegacyRewardValuation) M1G attaches w2 to yos, the valuation variable.
                // v.name is zh_<order>; "z_"+order.ID is the binding counterpart.
                var zVars = _m4gConfig.LegacyRewardValuation
                    ? sym.Zhat.Select(v => bin[v.name]).ToList()
                    : sym.Zhat.Select(v => bin["z_" + v.order.ID]).ToList();
                if (zVars.Count > 0)
                    objective = objective + LinearExpression.Sum(zVars) * _m4gConfig.LegacyOrderReward;

                // w3 * idle slots (M1GManager.cs "us"/shi4 mirror): an integer slack per station
                // equal to Cs[station] minus the slots this decision's bound assignments occupy.
                // Only the (order, station) pairs that actually have a Qhat entry have a y_[o]_[s]
                // variable in the model (built in the B2 loop above) - restricting the sum to those
                // pairs (via sym.Qhat, the same source B2 filters on) avoids referencing an
                // undeclared y variable, which would silently manufacture a fresh unconstrained
                // binary the solver could set for free to shrink the idle count.
                if (_m4gConfig.LegacyIdleSlotWeight != 0)
                {
                    HashSet<string> orderStationPairs = new HashSet<string>(
                        sym.Qhat.Select(v => v.order.ID + "_" + v.outputstation.ID));
                    VariableCollection<string> idle = new VariableCollection<string>(
                        wrapper, VariableType.Integer, 0, maxSlots, (string s) => { return s; });
                    var idleVars = new List<Variable>();
                    foreach (var station in snap.Cs.Keys)
                    {
                        var ys = snap.PendingOrders.Where(o => orderStationPairs.Contains(o.ID + "_" + station.ID))
                            .Select(o => bin["y_" + o.ID + "_" + station.ID]).ToList();
                        string idleName = "legacy_idle_" + station.ID;
                        LinearExpression occupied = ys.Count > 0
                            ? LinearExpression.Sum(ys) : LinearExpression.Sum(new List<LinearExpression>(), wrapper);
                        wrapper.AddConstr(occupied + idle[idleName] == snap.Cs[station], "LegacyIdle");
                        idleVars.Add(idle[idleName]);
                    }
                    if (idleVars.Count > 0)
                        objective = objective + LinearExpression.Sum(idleVars) * _m4gConfig.LegacyIdleSlotWeight;
                }
            }
            else
            {
                // T3: bound line closures at full price, valued-but-unbound at the realisation rate.
                // Guard: these use the plain Sum(IEnumerable<Variable>) overload, which throws on an
                // empty sequence. sym.Chat can only be empty if sym.Qhat is empty too (Qhat is built
                // strictly inside the Chat loop), and that case already returned above - so this is
                // defensive, not load-bearing, but kept for consistency and future-proofing.
                // Due-date pricing folds a per-order factor into lambda and mu, so the flat
                // "sum the variables, then multiply once" form no longer applies and each term
                // carries its own coefficient. The urgency-blind branch is kept verbatim rather
                // than expressed as the factor-1 case of the weighted one: summing first and
                // scaling once is not bit-identical to scaling each term and summing, and the
                // whole point of gating this behind a flag is that off must reproduce the
                // published results exactly.
                if (_m4gConfig.DueDatePricingEnabled)
                {
                    objective = objective + LinearExpression.Sum(sym.Chat.Select(v =>
                        bin["c_" + v.order.ID + "_" + v.skui.ID]
                            * (-lambda * UrgencyOf(snap, v.order) * (1.0 - delta))), wrapper);
                    objective = objective + LinearExpression.Sum(sym.Chat.Select(v =>
                        bin[v.name] * (-lambda * UrgencyOf(snap, v.order) * delta)), wrapper);
                    // T4: same split for completed orders.
                    objective = objective + LinearExpression.Sum(sym.Zhat.Select(v =>
                        bin["z_" + v.order.ID]
                            * (-mu * UrgencyOf(snap, v.order) * (1.0 - delta))), wrapper);
                    objective = objective + LinearExpression.Sum(sym.Zhat.Select(v =>
                        bin[v.name] * (-mu * UrgencyOf(snap, v.order) * delta)), wrapper);
                }
                else
                {
                // (FullCreditBothLayers) Pay the FULL price on both layers: a bound line earns
                // lambda and a valued line earns lambda as well, instead of the pair splitting one
                // lambda between them. Not reachable through delta, whose two coefficients are
                // constrained to sum to lambda by construction - this is a different objective, not
                // a delta setting, which is why it needs its own flag.
                //
                // Expected to over-credit: a line is re-valued every decision until it is finally
                // closed (measured: 4872 valued for 962 closed, i.e. about 5x), so paying the full
                // closed-line price on every valuation counts the same work roughly five times.
                bool fullCredit = _m4gConfig.FullCreditBothLayers;
                double lamBound = fullCredit ? lambda : lambda * (1.0 - delta);
                double lamValued = fullCredit ? lambda : lambda * delta;
                double muBound = fullCredit ? mu : mu * (1.0 - deltaOrder);
                double muValued = fullCredit ? mu : mu * deltaOrder;
                // (ScarcityWeightedBinding) Weight each bound line closure by how expensive that
                // line would be to finish LATER. A line whose SKU the sunk pods still cover in
                // abundance costs nothing to defer - somebody's already-paid trip will serve it.
                // A line whose SKU has no sunk supply left costs a whole new dispatch, so closing
                // it now is worth strictly more. The weight is 1 + scarcity, scarcity in [0,1].
                //
                // This is a cost-to-go term, not a preference: it says "deferring this line costs
                // lambda extra metres", which is a sourcing fact, unlike a due-date weight which
                // would say "I would rather do this one first". Applied to the BINDING layer only,
                // so the valuation layer's lambda stays uniform and the delta-as-exchange-rate
                // reading survives intact.
                Dictionary<int, double> scarcity = _m4gConfig.ScarcityWeightedBinding
                    ? BuildSkuScarcity(snap) : null;
                if (scarcity != null)
                {
                    objective = objective + LinearExpression.Sum(sym.Chat.Select(v =>
                    {
                        double w;
                        if (!scarcity.TryGetValue(v.skui.ID, out w)) w = 0.0;
                        return bin["c_" + v.order.ID + "_" + v.skui.ID] * (-lamBound * (1.0 + w));
                    }), wrapper);
                }
                else
                {
                    var chatBoundVars = sym.Chat.Select(v => bin["c_" + v.order.ID + "_" + v.skui.ID]).ToList();
                    if (chatBoundVars.Count > 0)
                        objective = objective + LinearExpression.Sum(chatBoundVars) * (-lamBound);
                }
                // (TieredBetaBySunkCoverage) The valuation credit is tiered by whether the line's
                // deferred closure is already paid for. A line the SUNK pods (Pb - at a station or
                // en route, their travel charged in an earlier decision) can cover in full will be
                // closed by a trip nobody has to buy again, so it earns the whole lambda; a line
                // that still needs a fresh dispatch earns only the measured share that such claims
                // historically convert.
                //
                // Deliberately applied to the VALUED coefficient only. Forcing the pair to sum to
                // lambda - the original (1-beta)/beta split - would drive the bound increment to
                // zero on exactly the sunk-coverable lines, reproducing the beta = 1 gradient
                // collapse on a subset and breaking the greedy mirror. Decoupling keeps the bound
                // increment at lambda*(1-beta) everywhere, so every draw move still scores.
                //
                // The tier is a snapshot parameter, not a variable: no new binaries, no big-M. It
                // ignores contention between orders competing for the same sunk stock, which makes
                // it optimistic; that is a deliberate simplification for a first probe.
                HashSet<string> sunkCovered = (_m4gConfig.TieredBetaBySunkCoverage && !fullCredit)
                    ? BuildSunkCoveredLines(snap) : null;
                if (sunkCovered != null)
                {
                    objective = objective + LinearExpression.Sum(sym.Chat.Select(v =>
                        bin[v.name] * (-lambda * (sunkCovered.Contains(v.order.ID + "_" + v.skui.ID)
                            ? 1.0 : delta))), wrapper);
                }
                else
                {
                    var chatValuedVars = sym.Chat.Select(v => bin[v.name]).ToList();
                    if (chatValuedVars.Count > 0)
                        objective = objective + LinearExpression.Sum(chatValuedVars) * (-lamValued);
                }
                // T4: same split for completed orders. Same guard rationale as T3.
                // (ScarcityWeightedCompletion) Order-level counterpart of the line-level weight,
                // and the one that matches the intent: an order is HARD when at least one of its
                // open lines has no sunk supply behind it, because finishing it later then costs a
                // fresh dispatch no matter how easy its other lines are. The weight is therefore
                // the MAX scarcity over the order's open lines, not the mean - a single unsourced
                // line is enough to make the whole order expensive to defer.
                //
                // Weighting completion rather than line closure is what keeps the pile-on
                // incentive intact: the extra credit is only paid when the order actually
                // finishes, so the model still has a reason to consolidate a whole order onto one
                // trip instead of harvesting scarce lines across many.
                Dictionary<int, double> orderScarcity = _m4gConfig.ScarcityWeightedCompletion
                    ? BuildOrderScarcity(snap) : null;
                if (orderScarcity != null)
                {
                    objective = objective + LinearExpression.Sum(sym.Zhat.Select(v =>
                    {
                        double w;
                        if (!orderScarcity.TryGetValue(v.order.ID, out w)) w = 0.0;
                        return bin["z_" + v.order.ID] * (-muBound * (1.0 + w));
                    }), wrapper);
                }
                else
                {
                    var zhatBoundVars = sym.Zhat.Select(v => bin["z_" + v.order.ID]).ToList();
                    if (zhatBoundVars.Count > 0)
                        objective = objective + LinearExpression.Sum(zhatBoundVars) * (-muBound);
                }
                if (sunkCovered != null)
                {
                    // An order is sunk-covered only when EVERY one of its open lines is - a partial
                    // cover still needs a new trip to finish the order, so mu must not be paid in
                    // full for it.
                    objective = objective + LinearExpression.Sum(sym.Zhat.Select(v =>
                        bin[v.name] * (-mu * (snap.Residuals[v.order]
                            .Where(pp => pp.Value > 0 && snap.PiSKU.ContainsKey(pp.Key))
                            .All(pp => sunkCovered.Contains(v.order.ID + "_" + pp.Key.ID))
                            ? 1.0 : delta))), wrapper);
                }
                else
                {
                    var zhatValuedVars = sym.Zhat.Select(v => bin[v.name]).ToList();
                    if (zhatValuedVars.Count > 0)
                        objective = objective + LinearExpression.Sum(zhatValuedVars) * (-muValued);
                }
                }
                // T5: tie-break that prefers executing now among equally valued solutions. sym.Qhat
                // is guaranteed non-empty by the early return above, but guard anyway for consistency.
                // (CompactLineModel) T5 and T6 are per-UNIT prices, so they have no expression in a
                // model whose finest object is the line. Both are zero in the canon (EpsilonScale
                // = 0, PodTierDrawPricingEnabled = false), which is exactly why the compact
                // encoding is available at all - it could not reproduce them if they were live.
                if (!model.Compact)
                {
                    var qbTieVars = sym.Qhat.Select(v =>
                        qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID]).ToList();
                    if (qbTieVars.Count > 0)
                        objective = objective + LinearExpression.Sum(qbTieVars) * (-epsilon);
                }
                // T6: pod-tier draw pricing (rho), binding layer only - the tier preference is about
                // which pod actually gets drained, and only bound (qb) draws have real-world effects.
                // A unit bound from a Pp pod (processing right now, window closing) is rewarded -rho:
                // skipping it means paying for a future trip to fetch that item later. A unit bound
                // from a Pa pod (newly dispatched this decision) pays +rho: it genuinely costs an
                // extra trip that a sunk pod's unit does not. Pq/Pb (queued/en route) draws stay free,
                // matching the T1/T2 sunk-trip treatment of those same pods. false reproduces the flat
                // (every draw free) behaviour bit-for-bit.
                // T7: WIP holding price (gated). kappa scales with lambda, so this term sits on the
                // value side of the ratio and leaves the Dinkelbach linearisation intact.
                if (_m4gConfig.WipHoldingEnabled)
                {
                    double kappa = _m4gConfig.WipHoldingScale * lambda;
                    var wipClearVars = sym.Zhat.Where(v => snap.WipOrders.Contains(v.order))
                        .Select(v => bin["z_" + v.order.ID]).ToList();
                    if (wipClearVars.Count > 0)
                        objective = objective + LinearExpression.Sum(wipClearVars) * (-kappa);
                    var openVars = openOrderIds.Select(id => bin["open_" + id]).ToList();
                    if (openVars.Count > 0)
                        objective = objective + LinearExpression.Sum(openVars) * kappa;
                }
                if (_m4gConfig.PodTierDrawPricingEnabled && !model.Compact)
                {
                    HashSet<int> processingPodIds = BuildProcessingPodIds(snap);
                    var newPodDraws = sym.Qhat.Where(v => snap.Pa.Contains(v.pod))
                        .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                        .ToList();
                    // (PodTierPenaltyOnNew) The trip is already priced once in D, independent of
                    // the draw count, so this per-unit levy charges the same trip a second time and
                    // its gradient runs against pile-on. Gated so the half can be dropped while the
                    // -rho reward on Pp pods (a genuine opportunity cost, no D charged) stays.
                    if (newPodDraws.Count > 0 && _m4gConfig.PodTierPenaltyOnNew)
                        objective = objective + LinearExpression.Sum(newPodDraws) * rho;
                    var processingDraws = sym.Qhat.Where(v => processingPodIds.Contains(v.pod.ID))
                        .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                        .ToList();
                    if (processingDraws.Count > 0)
                        objective = objective + LinearExpression.Sum(processingDraws) * (-rho);
                }
            }
            // (SlotScale, experimental) T8: price an empty station slot at SlotScale * mu metres.
            // mu already carries the Dinkelbach lambda scaling (mu = mu0 * lambdaScaleRatio above),
            // so sigma tracks lambda and the value side stays proportional - required for the
            // V* = (D* - objective)/lambda recovery to remain exact.
            if (_m4gConfig.SlotScale > 0 && model.Us != null && model.UsNames.Count > 0)
            {
                double sigma = _m4gConfig.SlotScale * mu;
                objective = objective
                    + LinearExpression.Sum(model.UsNames.Select(n => model.Us[n]).ToList()) * sigma;
            }
            wrapper.SetObjective(objective, OptimizationSense.Minimize);

            DateTime solveStart = DateTime.Now;
            wrapper.Update();
            // The model outlives a single solve now (one build, one solve per Dinkelbach lambda),
            // so drop any solution left by the previous iteration before optimising. Without this
            // the re-solve warm-starts from the old solution and may settle on a different member
            // of an equally optimal set - a silent decision change, not a speed-up.
            wrapper.Reset();
            wrapper.Optimize();
            result.SolveSec = (DateTime.Now - solveStart).TotalSeconds;
            if (!wrapper.HasSolution()) return result;
            double objStar = wrapper.GetObjectiveValue();

            // (LexicographicRatioFirst) Ratio-first tie-break. Freeze the achieved ratio objective
            // as a constraint, then minimise idle slots among the solutions that still achieve it.
            // Called only once, after the Dinkelbach loop has converged, so the added constraint
            // never reaches an iteration whose V* = (D* - objective)/lambda recovery would be
            // corrupted by it - and the model is disposed straight after, so the constraint does
            // not leak into the next decision. result.Objective keeps objStar rather than the
            // re-solve's value, which is a slot count, not a distance.
            if (applySlotTieBreak && model.Us != null && model.UsNames.Count > 0)
            {
                wrapper.AddConstr(objective <= objStar + _m4gConfig.LexTieTolerance, "LexTie");
                var tieUs = model.UsNames.Select(n => model.Us[n]).ToList();
                wrapper.SetObjective(LinearExpression.Sum(tieUs), OptimizationSense.Minimize);
                wrapper.Update();
                wrapper.Reset();
                wrapper.Optimize();
                result.SolveSec = (DateTime.Now - solveStart).TotalSeconds;
                // Infeasible here would mean the frozen bound excluded its own witness - only
                // reachable through numerical error. Keep the pre-tie-break solution's absence
                // rather than committing a half-read model.
                if (!wrapper.HasSolution()) return result;
            }

            result.HasSolution = true;
            result.Objective = objStar;
            try { result.Gap = wrapper.GetGap(); } catch { result.Gap = double.NaN; }
            result.Rho = rho;
            result.LambdaUsed = lambda;
            // D* (spec: "realised distance terms"): read back the same T1/T2 travel-cost terms
            // from the solved xps/yrp variables, not estimated from the objective algebraically -
            // this is what the Dinkelbach step needs as the numerator of the next lambda.
            double dStar = 0.0;
            foreach (var v in sym.Xps.Where(v => snap.Pa.Contains(v.pod)))
                if (Math.Round(bin[v.name].GetValue()) != 0)
                    dStar += M1GPodStationCost(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation);
            foreach (var v in sym.Yrp.Where(v => snap.Pa.Contains(v.pod) && v.pod.Waypoint != null))
                if (Math.Round(bin[v.name].GetValue()) != 0)
                    dStar += M1GBotPodCost(v.robot, v.pod);
            result.DStar = dStar;
            if (model.Compact)
            {
                // (CompactLineModel) The solver decided WHICH lines land WHERE; it never decided
                // which pod supplies which unit, because under line atomicity that choice carries
                // no objective value. Recover one feasible allocation greedily - V1' guarantees the
                // stock standing at the station covers everything bound there, and the underlying
                // transportation matrix is a network matrix, so a greedy fill always closes every
                // line. Sunk pods (Pb) are drained before newly dispatched ones (Pa), which is the
                // same tier order GreedyM5Manager.DrawOrder walks.
                var remaining = new Dictionary<string, int>();
                Func<Pod, ItemDescription, int> stockOf = (pod, sku) =>
                {
                    string k = pod.ID + ":" + sku.ID;
                    int left;
                    if (!remaining.TryGetValue(k, out left)) { left = pod.CountAvailable(sku); remaining[k] = left; }
                    return left;
                };
                // (OrderAtomicNoSplit) The atom is the whole order, so every line of an assigned
                // order is drawn in full at that station. Expand each assignment into its lines
                // and reuse the same greedy fill; NS1 guarantees the stock is there.
                var places = model.OrderAtomic
                    ? model.Places.SelectMany(p => snap.Residuals[p.Order]
                          .Where(e => e.Value > 0 && snap.PiSKU.ContainsKey(e.Key))
                          .Select(e => new M4GPlacement
                          {
                              Order = p.Order, Sku = e.Key, Station = p.Station,
                              Units = e.Value, GhName = p.GhName, GbName = p.GbName
                          })).ToList()
                    : model.Places;
                foreach (var pl in places)
                {
                    if (Math.Round(bin[pl.GbName].GetValue()) == 0) continue;
                    int need = pl.Units;
                    var here = snap.PiSKU[pl.Sku]
                        .Where(p => Math.Round(bin["xps_" + p.ID + "_" + pl.Station.ID].GetValue()) != 0)
                        .OrderBy(p => snap.Pa.Contains(p) ? 1 : 0)
                        .ToList();
                    foreach (var pod in here)
                    {
                        if (need == 0) break;
                        int have = stockOf(pod, pl.Sku);
                        if (have <= 0) continue;
                        int take = Math.Min(have, need);
                        remaining[pod.ID + ":" + pl.Sku.ID] = have - take;
                        var draw = new Symbol { order = pl.Order, skui = pl.Sku, pod = pod,
                            outputstation = pl.Station };
                        int had; result.BoundDraws.TryGetValue(draw, out had);
                        result.BoundDraws[draw] = had + take;
                        if (snap.Pa.Contains(pod)) result.UnitsFromNew += take; else result.UnitsFromSunk += take;
                        need -= take;
                    }
                    if (need > 0)
                        throw new InvalidOperationException(
                            "CompactLineModel: bound line " + pl.Order.ID + "/" + pl.Sku.ID
                            + " short by " + need + " units at station " + pl.Station.ID
                            + " - V1' should have made this impossible.");
                }
            }
            else
            foreach (var v in sym.Qhat)
            {
                int units = (int)Math.Round(qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID
                    + "_" + v.outputstation.ID].GetValue());
                if (units > 0)
                {
                    result.BoundDraws[v] = units;
                    if (snap.Pa.Contains(v.pod)) result.UnitsFromNew += units;
                    else result.UnitsFromSunk += units;
                }
                if (_m4gConfig.ValuationFidelityLog)
                {
                    int planned = (int)Math.Round(qh[v.name].GetValue());
                    string pk = v.pod.ID + ":" + v.order.ID + ":" + v.skui.ID;
                    if (units > 0)
                    {
                        int had; result.PodDrawnUnits.TryGetValue(pk, out had);
                        result.PodDrawnUnits[pk] = had + units;
                    }
                    if (planned > units)
                    {
                        int had; result.PodPromisedUnits.TryGetValue(pk, out had);
                        result.PodPromisedUnits[pk] = had + (planned - units);
                    }
                }
            }
            foreach (var v in sym.Chat)
            {
                if (Math.Round(bin[v.name].GetValue()) != 0)
                    result.ValuedLineKeys.Add(M4GPricing.LineKey(v.order.ID, v.skui.ID));
                if (Math.Round(bin["c_" + v.order.ID + "_" + v.skui.ID].GetValue()) != 0)
                    result.BoundLineKeys.Add(M4GPricing.LineKey(v.order.ID, v.skui.ID));
            }
            result.ValuedOrders = sym.Zhat.Count(v => Math.Round(bin[v.name].GetValue()) != 0);
            result.BoundOrders = sym.Zhat.Count(v => Math.Round(bin["z_" + v.order.ID].GetValue()) != 0);
            // (ValuationFidelityLog) Size of the orders each layer claims, in residual lines.
            // Counted off snap.Residuals - the same demand ledger the Chat/Qhat symbols were built
            // from - so "lines" here means exactly what lambda prices, not the order's original
            // width. Guarded by the flag so the default path does no extra work at all.
            if (_m4gConfig.ValuationFidelityLog)
                foreach (var v in sym.Zhat)
                {
                    Dictionary<ItemDescription, int> res;
                    if (!snap.Residuals.TryGetValue(v.order, out res)) continue;
                    int lines = res.Count(p => p.Value > 0);
                    if (Math.Round(bin[v.name].GetValue()) != 0) result.ValuedOrderResidualLines += lines;
                    if (Math.Round(bin["z_" + v.order.ID].GetValue()) != 0) result.BoundOrderResidualLines += lines;
                }
            foreach (var v in sym.Xps.Where(v => snap.Pa.Contains(v.pod)))
                if (Math.Round(bin[v.name].GetValue()) != 0)
                {
                    result.NewTripCount++;
                    var carrier = idx.YrpByPod(v.pod.ID)
                        .FirstOrDefault(y => Math.Round(bin[y.name].GetValue()) != 0);
                    if (carrier != null) result.BotByPodId[v.pod.ID] = carrier.robot;
                }
            return result;
        }

        /// <summary>Entry point called by the engine whenever a station has a free slot.</summary>
        protected override void DecideAboutPendingOrders()
        {
            DateTime start = DateTime.Now;
            M4GSnapshot snap = BuildSnapshot();
            if (snap.PendingOrders.Count == 0 || snap.Cs.Count == 0
                || !snap.Cs.Values.Any(v => v > 0) || snap.AllPods.Count == 0)
                return;
            _lastQmax = 0;
            _lastLbFail = -1;
            _lastLbValue = 0.0;
            _lastGap = double.NaN;
            _lastGuardLamNext = double.NaN;
            _lastGuardTrialObj = double.NaN;
            _lastInboundCoverUnits = 0.0;
            _lastSplitsDeferred = 0;
            _lastSplitsCommitted = 0;
            double cumDist = PricingDistance();
            double lambda0 = _pricing.Lambda(cumDist);
            double mu0 = _pricing.Mu(cumDist);
            double epsilon0 = _pricing.Epsilon(cumDist);
            double rho0 = _pricing.Rho(cumDist, Instance.StatOverallItemsHandled);
            // (StratifiedDelta, gated) Bucket this decision by how much supply is already
            // committed. Held in a local so the SAME stratum feeds the price read here, the
            // logged column below and RegisterDecision at the end - reading snap.Pb.Count three
            // times would be equivalent today but is exactly the kind of drift a later edit
            // introduces silently. DeltaStratum returns -1 when the flag is off, which makes
            // Delta(int)/RegisterDecision(int) fall through to the system-wide totals.
            int deltaStratum = DeltaStratum(snap);
            double delta = _pricing.Delta(deltaStratum);
            // Line-level beta prices the lambda terms; the mu terms use the order-level rate when
            // SeparateOrderDelta is on, since an order's valuation converts at a materially
            // different rate than a line's (measured 0.2837 vs 0.1995).
            double deltaOrder = _m4gConfig.SeparateOrderDelta ? _pricing.DeltaOrder() : delta;

            // ── Dinkelbach iteration (0 = single step at the historical lambda, unchanged
            // behaviour). The model is built ONCE and re-solved at each new lambda: no constraint
            // in the R, V or B series references lambda (or mu/delta/rho/epsilon), so the only
            // thing an iteration changes is the objective, and rebuilding variables and
            // constraints per iteration was pure duplicated work. The model is disposed as soon
            // as the loop ends - everything downstream reads plain C# fields off `result`, never
            // the live Gurobi variables. ──
            // (TwoStagePricing) Stage 1: recover lambda* on the restricted problem, where the
            // null plan is infeasible so standard Dinkelbach converges from any starting value
            // and no escalation is needed. The stage-1 SOLUTION is thrown away - only the scalar
            // survives, and it enters stage 2 as the objective coefficient. See
            // IM4GPrices.TwoStagePricing for why this is not lexicographic optimisation.
            double stageOneLambda = lambda0;
            double stageOneSec = 0.0;
            int stageOneIters = 0;
            if (_m4gConfig.TwoStagePricing)
            {
                M4GModel priced = BuildModel(snap, true);
                if (priced != null)
                {
                    try
                    {
                        double lk = lambda0;
                        M4GResult r0 = SolveM4G(priced, snap, lk, lambda0, mu0, epsilon0, rho0, delta, deltaOrder);
                        stageOneSec += r0.SolveSec;
                        for (int i = 0; i < _m4gConfig.DinkelbachIterations && r0.HasSolution; i++)
                        {
                            double p = lk > 0 ? (r0.DStar - r0.Objective) / lk : 0.0;
                            if (p <= 0) break;                                   // cannot happen on the
                            if (Math.Abs(r0.Objective) <= _m4gConfig.DinkelbachTolerance) break;
                            double ln = r0.DStar / p;
                            if (ln <= 0) break;
                            M4GResult rn = SolveM4G(priced, snap, ln, lambda0, mu0, epsilon0, rho0, delta, deltaOrder);
                            stageOneSec += rn.SolveSec;
                            if (!rn.HasSolution) break;
                            r0 = rn; lk = ln; stageOneIters++;
                        }
                        if (r0.HasSolution && lk > 0) stageOneLambda = lk;
                    }
                    finally { priced.Dispose(); }
                }
            }

            double lambdaK = _m4gConfig.TwoStagePricing ? stageOneLambda : lambda0;
            // Stage-1 work is real work: fold it into the logged totals so solveSec and dinkIters
            // stay comparable with the single-stage arm instead of silently hiding a second solve.
            double carrySec = stageOneSec; int carryIters = stageOneIters;
            M4GResult result;
            double totalSolveSec;
            int dinkIters = 0;
            M4GModel model = BuildModel(snap);
            try
            {
            // (LexicographicSlotFill) Stage 1: minimise idle slots on their own, then freeze the
            // achieved count as a constraint. Slot filling becomes a hard priority that never
            // touches the objective - so it cannot leak into V* and drag the Dinkelbach lambda,
            // which is exactly what the sigma penalty did (lambda 8.05 -> 0.9 at every sigma from
            // 5 to 400 mu). The bound is lambda-independent, so it is added once per decision and
            // holds for every Dinkelbach iteration. Infeasibility is impossible: the stage-1
            // optimum is by construction attainable.
            if (_m4gConfig.LexicographicSlotFill && !_m4gConfig.LexicographicRatioFirst
                && model != null && model.UsNames.Count > 0)
            {
                var usList = model.UsNames.Select(n => model.Us[n]).ToList();
                model.Wrapper.SetObjective(LinearExpression.Sum(usList), OptimizationSense.Minimize);
                model.Wrapper.Update();
                model.Wrapper.Reset();
                model.Wrapper.Optimize();
                if (model.Wrapper.HasSolution())
                {
                    int idleStar = (int)Math.Round(model.Wrapper.GetObjectiveValue());
                    model.Wrapper.AddConstr(LinearExpression.Sum(usList) <= idleStar, "LexSlot");
                    model.Wrapper.Update();
                }
            }
            result = model == null
                ? new M4GResult()
                : SolveM4G(model, snap, lambdaK, lambda0, mu0, epsilon0, rho0, delta, deltaOrder);
            totalSolveSec = result.SolveSec + carrySec;
            dinkIters += carryIters;
            // LegacyObjective replaces the whole lambda-scaled value side with a fixed order
            // reward (see SolveM4G), so there is no ratio left to linearise: Dinkelbach's
            // re-solve-at-a-new-lambda loop is skipped entirely rather than iterating over a
            // parameter the objective no longer uses. The single solve above (built at lambda0,
            // which SolveM4G ignores in this mode) stands as the decision.
            // (TwoStagePricing) Stage 2 is ONE solve at lambda*, nothing more. Iterating here
            // would re-open the degenerate search this design exists to avoid, and would also
            // discard the price stage 1 just established.
            if (result.HasSolution && !_m4gConfig.LegacyObjective && !_m4gConfig.TwoStagePricing)
            {
                int escalations = 0;
                bool jumpedToBound = false;
                for (int i = 0; i < _m4gConfig.DinkelbachIterations; i++)
                {
                    // V* = (D* - objective) / lambda_k. The objective returned by the solver is
                    // exactly D* - lambda_k*V* by construction (every value coefficient scales
                    // with lambda_k - see SolveM4G's remarks), so this recovers V* without
                    // needing a second pass over the solution.
                    double vStar = lambdaK > 0 ? (result.DStar - result.Objective) / lambdaK : 0.0;
                    // (Two-sided search, gated) The break below makes this loop ONE-SIDED: V* <= 0
                    // means the best solution at this lambda is "dispatch nothing", so there is no
                    // D*/V* to iterate from and lambda can never RISE. Measured across 1013
                    // decisions: 880 downward revisions, zero upward. The consequence is that the
                    // running price statistic must be an OVER-estimate to work at all - narrowing
                    // its numerator from fleet distance to picking distance alone (the
                    // conceptually correct span, 21.2% of the total) drops the starting lambda
                    // from 8.70 to 1.98, below the self-consistent marginal price of ~4.5, and the
                    // run collapses from 603 completed orders to 38 with only 5 pod dispatches.
                    // Doubling lambda on a degenerate solve lets the fixed point be approached
                    // from below too. 0 (default) reproduces the one-sided behaviour bit-for-bit.
                    // (UpperBoundJump) Degenerate incumbent: the null plan is optimal at this
                    // lambda, so there is no D*/V* to iterate from. Rather than doubling blindly,
                    // move ONCE to a provable upper bound on lambda* and let the normal Newton
                    // steps walk back down - monotonically, since every lambda_k from here on is
                    // above lambda*. Only worth doing when the bound is actually above the current
                    // lambda; if it is not, lambda was already high enough and the degeneracy
                    // means this snapshot genuinely has nothing worth dispatching.
                    if (vStar <= 0 && _m4gConfig.UpperBoundJump && !jumpedToBound && lambdaK > 0)
                    {
                        double lambdaUb = ComputeLambdaUpperBound(snap);
                        if (lambdaUb > lambdaK)
                        {
                            jumpedToBound = true;
                            M4GResult ubRes = SolveM4G(model, snap, lambdaUb, lambda0, mu0, epsilon0, rho0, delta, deltaOrder);
                            totalSolveSec += ubRes.SolveSec;
                            if (!ubRes.HasSolution) break;
                            result = ubRes;
                            lambdaK = lambdaUb;
                            dinkIters++;
                            continue;
                        }
                    }
                    if (vStar <= 0 && escalations < _m4gConfig.DinkelbachEscalations && lambdaK > 0)
                    {
                        escalations++;
                        double lambdaUp = lambdaK * 2.0;
                        M4GResult upRes = SolveM4G(model, snap, lambdaUp, lambda0, mu0, epsilon0, rho0, delta, deltaOrder);
                        totalSolveSec += upRes.SolveSec;
                        if (!upRes.HasSolution) break;
                        result = upRes;
                        lambdaK = lambdaUp;
                        dinkIters++;
                        continue;
                    }
                    if (vStar <= 0) break;                                    // ratio undefined - keep this solution
                    if (Math.Abs(result.Objective) <= _m4gConfig.DinkelbachTolerance) break;  // converged
                    double lambdaNext = result.DStar / vStar;
                    // (D* = 0) The plan makes progress at zero new travel - every line came from
                    // stock already standing at a station. The ratio is 0, which is not a price,
                    // so there is nothing to iterate on. This used to fall through to a solve at
                    // lambda = 0, whose only possible optimum is the empty plan, and the guard
                    // below then caught it - a redundant solve on ~70% of all decisions. MEASURED:
                    // removing it is bit-identical on every KPI but saves only 0.12% of solver time
                    // (65.14 s -> 65.06 s, seed 0), because minimising D alone is trivial. The point
                    // is that the exit now matches the documented flow, not that it is faster.
                    if (lambdaNext <= 0)
                    {
                        _lastGuardLamNext = lambdaNext;
                        _lastGuardTrialObj = 0.0;
                        break;
                    }
                    M4GResult next = SolveM4G(model, snap, lambdaNext, lambda0, mu0, epsilon0, rho0, delta, deltaOrder);
                    totalSolveSec += next.SolveSec;
                    if (!next.HasSolution) break;                             // keep the previous solution
                    // Guard against adopting the empty plan at the fixed point. MEASURED
                    // 2026-09-11 (984 decisions, seed 0): every one of the 95 guard firings had
                    // a trial objective of exactly 0, i.e. the empty plan only ever TIED with the
                    // incumbent - it never strictly dominated. lambda_next = D*/P* is a ratio some
                    // feasible plan ACHIEVES, so it can never fall below lambda*; the sequence
                    // arrives at the fixed point, it does not overshoot it. What has to be caught
                    // is the TIE: at lambda* the empty plan scores 0 just like the optimum, and
                    // adopting it would silently zero out this decision's picks - confirmed in
                    // testing: an unguarded loop collapsed a whole 2h run to ~0 orders completed.
                    // (Superseded wording: "MILP-integrality overshoot". Dinkelbach's continuous-relaxation
                    // proof has lambda_k decrease monotonically toward the true minimum ratio
                    // without ever undershooting it, but the achievable ratio here is a step
                    // function of lambda (integer pod/bot/order assignment), so one re-solve can
                    // jump straight past the fixed point into the region where "dispatch nothing"
                    // (D=0, V=0, always feasible at objective 0) strictly dominates every real
                    // dispatch. Adopting that trivial solution would silently zero out this
                    // decision's picks - confirmed in testing: an unguarded loop collapsed a whole
                    // 2h run to ~0 orders completed once lambda first overshot on decision 0.
                    // .) Evaluate the CANDIDATE's own V* before adopting it; if it is degenerate,
                    // stop and keep the last non-degenerate solution instead.
                    double nextVStar = lambdaNext > 0 ? (next.DStar - next.Objective) / lambdaNext : 0.0;
                    if (nextVStar <= 0)
                    {
                        _lastGuardLamNext = lambdaNext;
                        _lastGuardTrialObj = next.Objective;
                        break;
                    }
                    result = next;
                    lambdaK = lambdaNext;
                    dinkIters++;
                }
            }
            // (LexicographicRatioFirst) The tie-break runs here, on the converged lambda, so the
            // committed solution is the slot-fullest member of the ratio-optimal set. A degenerate
            // re-solve keeps the pre-tie-break decision rather than discarding the whole decision.
            if (_m4gConfig.LexicographicSlotFill && _m4gConfig.LexicographicRatioFirst
                && result.HasSolution && !_m4gConfig.LegacyObjective
                && model != null && model.UsNames.Count > 0)
            {
                M4GResult tied = SolveM4G(model, snap, lambdaK, lambda0, mu0, epsilon0, rho0, delta, deltaOrder, true);
                totalSolveSec += tied.SolveSec;
                if (tied.HasSolution) result = tied;
            }
            }
            finally
            {
                if (model != null) model.Dispose();
            }
            double lambdaEnd = lambdaK;
            _lastGap = result.Gap;

            // Slot shadow-price probe (gated, measurement only). Deliberately placed after the
            // base model is disposed but BEFORE CommitM4G, so it re-solves against exactly the
            // world the base decision was made in.
            if (_m4gConfig.SlotShadowProbeCadence > 0 && result.HasSolution
                && _decisionIndex % _m4gConfig.SlotShadowProbeCadence == 0)
                ProbeSlotShadowPrices(snap, result, lambdaK, lambda0, mu0, epsilon0, rho0, delta, deltaOrder);

            // Must run before WriteDecision so splitsDeferred/splitsCommitted reflect this
            // decision's own commit, not the previous one's leftover counters.
            if (result.HasSolution)
                CommitM4G(snap, result);
            // lambda/mu/delta/epsilon here are re-queried from _pricing AFTER CommitM4G, exactly
            // as the pre-Dinkelbach code did - CommitM4G's RegisterClosedLines/RegisterCompletedOrders
            // calls have already run by this point, so these four columns reflect this decision's
            // own contribution baked in. This is diagnostic-only (the objective was built earlier,
            // pre-commit, from lambda0/mu0/epsilon0/delta and the Dinkelbach-converged lambdaEnd) -
            // kept bit-identical to the pre-Dinkelbach log so DinkelbachIterations=0 reproduces the
            // old decision log exactly. lambdaStart/lambdaEnd/dinkIters below are the new columns
            // that actually carry the Dinkelbach information.
            double cumDistAfter = PricingDistance();
            WriteDecision(result.HasSolution, snap.PendingOrders.Count, snap.Cs.Count(c => c.Value > 0),
                snap.Pa.Count, snap.Pb.Count, snap.Ra.Count,
                _pricing.Lambda(cumDistAfter), _pricing.Mu(cumDistAfter), _pricing.Delta(deltaStratum),
                _pricing.Epsilon(cumDistAfter), _pricing.LambdaRef,
                _pricing.LambdaDenomFix(cumDistAfter), result.DStar,
                result.ValuedLineKeys.Count, result.BoundLineKeys.Count, result.ValuedOrders, result.BoundOrders,
                result.NewTripCount, result.BoundDraws.Values.Sum(), result.Objective, totalSolveSec, _lastQmax,
                result.Rho, result.UnitsFromSunk, result.UnitsFromNew, _lastInboundCoverUnits,
                _lastSplitsDeferred, _lastSplitsCommitted, dinkIters, lambda0, lambdaEnd);
            _decisionIndex++;
            // Feed this decision's valuation/binding line counts into delta's running totals,
            // after WriteDecision so the logged delta (like lambda/mu) reflects state prior to
            // this decision's own contribution - same convention as RegisterClosedLines below.
            // (OrderAtomicNoSplit) There are no line keys in that arm, so beta is calibrated on
            // ORDERS - bound orders over valued orders - which is the same realisation rate one
            // atom up and keeps the price self-consistent with what the objective actually pays.
            if (_m4gConfig.OrderAtomicNoSplit && _m4gConfig.CompactLineModel)
                _pricing.RegisterDecision(result.BoundOrders, result.ValuedOrders, deltaStratum);
            else
                _pricing.RegisterDecision(result.BoundLineKeys.Count, result.ValuedLineKeys.Count, deltaStratum);
                // Order counts feed the separate order-level realisation rate. Registered
                // unconditionally so the statistic is available for diagnostics even when
                // SeparateOrderDelta is off - it is only READ under the flag.
                _pricing.RegisterDecisionOrders(result.BoundOrders, result.ValuedOrders);
            // (ValuationFidelityLog, gated) Measurement only - see M4GConfiguration. Placed after
            // RegisterDecision so the logged delta matches the decision log's convention.
            if (_m4gConfig.ValuationFidelityLog)
                WriteValuationFidelity(result, delta);
            Instance.Observer.TimeOrderBatchingbyMP((DateTime.Now - start).TotalSeconds);
        }

        /// <summary>
        /// Applies the binding layer and only the binding layer (spec 5.4). Whatever the
        /// valuation layer scored but the binding layer did not take has no side effect at
        /// all - it is priced once and discarded with the solve. Nothing is promised; the
        /// next decision re-solves the split shape against a fresh backlog.
        /// </summary>
        private void CommitM4G(M4GSnapshot snap, M4GResult result)
        {
            if (result.BoundDraws.Count == 0) return;

            // (SplitOnlyAtPresentPods) Decide, per order, whether this decision's commit needs a
            // split child at all (mirrors the fast-path test below: single station, whole residual
            // covered, not already a split parent). Orders that need a child are committed only if
            // every pod they draw from is already physically at a station (BuildPresentPodIds).
            // Deferred orders are excluded BEFORE pod claiming and pick registration below - not
            // just skipped in the per-order loop - so nothing about them is touched this decision:
            // no bot is dispatched on their behalf, no pick is registered, no slot is taken. They
            // remain fully pending for the next decision's fresh snapshot. Entirely inert when the
            // flag is off: presentPodIds/deferredOrderIds stay empty and effectiveDraws==BoundDraws.
            bool gateEnabled = _m4gConfig.SplitOnlyAtPresentPods;
            HashSet<int> presentPodIds = gateEnabled ? BuildPresentPodIds(snap) : null;
            HashSet<int> deferredOrderIds = new HashSet<int>();
            int splitsDeferred = 0, splitsCommitted = 0;
            if (gateEnabled)
            {
                foreach (var order in snap.PendingOrders)
                {
                    var mine0 = result.BoundDraws.Where(e => e.Key.order.ID == order.ID).ToList();
                    if (mine0.Count == 0) continue;
                    var stationGroups0 = mine0.GroupBy(e => e.Key.outputstation.ID).ToList();
                    bool needsAnyChild;
                    if (stationGroups0.Count == 1)
                    {
                        Dictionary<ItemDescription, int> q = new Dictionary<ItemDescription, int>();
                        foreach (var e in stationGroups0[0])
                        {
                            int cur;
                            q[e.Key.skui] = (q.TryGetValue(e.Key.skui, out cur) ? cur : 0) + e.Value;
                        }
                        bool coversWholeOrder = snap.Residuals[order]
                            .All(p => q.ContainsKey(p.Key) && q[p.Key] >= p.Value);
                        needsAnyChild = !(coversWholeOrder && !order.IsSplitParent);
                    }
                    else needsAnyChild = true;

                    if (!needsAnyChild) continue;   // fast path: pod state does not matter

                    bool allPresent = mine0.Select(e => e.Key.pod.ID).Distinct().All(presentPodIds.Contains);
                    if (!allPresent)
                    {
                        deferredOrderIds.Add(order.ID);
                        splitsDeferred++;
                    }
                    else
                        splitsCommitted++;
                }
            }
            _lastSplitsDeferred = splitsDeferred;
            _lastSplitsCommitted = splitsCommitted;

            Dictionary<Symbol, int> effectiveDraws = deferredOrderIds.Count == 0
                ? result.BoundDraws
                : result.BoundDraws.Where(kv => !deferredOrderIds.Contains(kv.Key.order.ID))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (effectiveDraws.Count == 0) return;

            // Claim newly-dispatched (Pa) pods to their bots and register them as inbound at
            // the station their bound draws target - without this the solver's xps/yrp
            // decision never becomes a real bot dispatch, so the picks registered below are
            // never delivered (mirrors SplitM2eICManager's decode block, restricted to pods
            // that actually carry a bound draw: a pod the valuation layer picked for xps=1
            // purely to price a line that the binding layer never took must not move - that
            // is exactly the "no side effect" invariant this method exists to enforce).
            foreach (var podGroup in effectiveDraws.Keys.Where(k => snap.Pa.Contains(k.pod)).GroupBy(k => k.pod.ID))
            {
                Pod pod = podGroup.First().pod;
                Bot bot;
                if (!result.BotByPodId.TryGetValue(pod.ID, out bot)) continue;
                if (Instance.ResourceManager.IsPodClaimed(pod)) continue;
                Instance.ResourceManager.BottoPod.Add(bot, pod);
                Instance.ResourceManager.ClaimPod(pod, bot, BotTaskType.Extract);
                foreach (var station in podGroup.Select(k => k.outputstation).Distinct())
                    station.RegisterInboundPod(pod);
            }

            // Register the picks on the pods so the trip carries a real request.
            foreach (var entry in effectiveDraws)
                for (int i = 0; i < entry.Value; i++)
                    entry.Key.pod.JustRegisterItem(entry.Key.skui);

            int closedLines = 0, completedOrders = 0;
            foreach (var order in snap.PendingOrders.OrderBy(o => o.ID))
            {
                if (deferredOrderIds.Contains(order.ID)) continue;
                var mine = effectiveDraws.Where(e => e.Key.order.ID == order.ID).ToList();
                if (mine.Count == 0) continue;

                // Group this order's bound draws by station: one child (or one plain
                // allocation) per station touched. Materialised (rather than iterated lazily)
                // only so the probe below can state how many stations THIS decision used for
                // THIS parent - the discriminator between Xie's "split among stations" and
                // "split over time". No effect on what is committed.
                var stationGroups = mine.GroupBy(e => e.Key.outputstation.ID).ToList();
                int stationsInDecision = stationGroups.Count;
                foreach (var stationGroup in stationGroups)
                {
                    OutputStation station = stationGroup.First().Key.outputstation;
                    Dictionary<ItemDescription, int> quantities = new Dictionary<ItemDescription, int>();
                    foreach (var e in stationGroup)
                    {
                        if (!quantities.ContainsKey(e.Key.skui)) quantities[e.Key.skui] = 0;
                        quantities[e.Key.skui] += e.Value;
                    }
                    bool coversWholeOrder = snap.Residuals[order]
                        .All(p => quantities.ContainsKey(p.Key) && quantities[p.Key] >= p.Value);
                    // Fast path: the whole residual is served here and the order was never split
                    // before - no child needed, the order itself takes the slot.
                    Order target;
                    // (WholeOrderDeferredFill) The order is the atom: whatever this decision could
                    // source, the ORDER is what gets allocated, so it takes one slot, leaves the
                    // pending set and is never re-decided. Its outstanding extract requests are
                    // then served by BotManagerPodSelection, which keeps fetching pods for a
                    // station's queued orders - the same division of labour M1G relies on. No child
                    // is created, so no sub-order granularity leaks in through the commit path.
                    if (_m4gConfig.WholeOrderDeferredFill)
                        target = order;
                    else if (coversWholeOrder && stationGroup.Count() == mine.Count && !order.IsSplitParent)
                        target = order;
                    else
                    {
                        target = Order.CreateSplitChild(order, quantities);
                        // Child IDs follow the base OrderManager convention used by every other
                        // split manager in this codebase (SplitM1GManager, SplitM1GExactManager,
                        // SplitM1GLBManager, SplitM2eICManager, PVGSManager): idoforder is the
                        // same monotonic counter that assigns IDs to freshly-arriving orders
                        // (OrderManager.cs), so reusing it here cannot collide with any live
                        // order or any other split child.
                        target.ID = idoforder++;
                        Instance.ResourceManager.TransferExtractRequests(order, target);
                    }
                    AllocateOrder(target, station);
                    // (ForbidCrossStationOnly) Remember where this order went the first time it
                    // took anything, so BuildSymbols can refuse to offer it any other station on
                    // later decisions. Keyed on the parent id even when a child was allocated -
                    // the parent is what survives in the pending set carrying residual demand.
                    if (_m4gConfig.ForbidSplitting && _m4gConfig.ForbidCrossStationOnly
                        && !_nsStationPin.ContainsKey(order.ID))
                        _nsStationPin[order.ID] = station.ID;
                    // Probe: one row per placement, keyed on the PARENT id so a post-processor can
                    // group a split order's pieces together regardless of the child ids they were
                    // given. Measurement only.
                    LogSplitEvent("piece", order.ID, 0, quantities.Values.Sum(),
                        station.ID, stationsInDecision, target.ID != order.ID ? 1 : 0);
                    Instance.StatCustomControllerInfo.CustomLogOB1++;
                    foreach (var e in stationGroup)
                    {
                        Symbol ziop = new Symbol { pod = e.Key.pod, order = target, outputstation = station,
                            skui = e.Key.skui,
                            name = "ziops_" + e.Key.skui.ID + "_" + target.ID + "_" + e.Key.pod.ID + "_" + station.ID };
                        Instance.ResourceManager._Ziops[station].Add(ziop, e.Value);
                    }
                }

                // (Fill fairness) On this order's FIRST split, free its Fill backlog slot so a
                // fresh order is injected, while keeping it in _pendingOrders for residual
                // service. Fires exactly once (guarded by IsOrderAvailable). Mirrors
                // SplitM2eICManager.CommitSplitExactResult's ReleaseParentOnFirstSplit block.
                // Unreachable when the flag is off => bit-identical to current behavior.
                if (_m4gConfig.ReleaseParentOnFirstSplit
                    && order.IsSplitParent
                    && (Instance.ItemManager as ItemManager).IsOrderAvailable(order))
                {
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(order);
                }

                // Bookkeeping for the price calibration.
                foreach (var sku in snap.Residuals[order])
                {
                    int drawn = mine.Where(e => e.Key.skui.ID == sku.Key.ID).Sum(e => e.Value);
                    if (drawn >= sku.Value)
                        closedLines++;
                }
                // "Completed" here means the order's entire snapshot-time residual was bound
                // this decision - this is what mu's denominator counts. order.IsFullyClaimed
                // alone is not sufficient: it is driven by Order.CreateSplitChild's demand
                // ledger, which the fast path above deliberately never touches (target == order,
                // no child created), so a whole never-split order fully served this decision
                // would otherwise be silently skipped here. The residual-coverage check below
                // is therefore the primary (and sufficient) test; IsFullyClaimed is kept as an
                // ADDITIONAL trigger only for removal, since it is the authoritative signal for
                // "this split parent has no residual left" independent of how this decision's
                // draws happen to be grouped.
                bool residualFullyBoundThisDecision = snap.Residuals[order]
                    .All(p => mine.Where(e => e.Key.skui.ID == p.Key.ID).Sum(e => e.Value) >= p.Value);
                // Split-lifetime probe. Recorded AFTER this decision's claims, so linesLeft /
                // unitsLeft are the remainder this decision actually leaves behind. Measurement
                // only - nothing below reads these back.
                if (order.IsSplitParent && !order.IsFullyClaimed && _splitSeen.Add(order.ID))
                    LogSplitEvent("split", order.ID,
                        order.RemainingPositions.Count(p => p.Value > 0),
                        order.RemainingPositions.Sum(p => p.Value));
                if (order.IsFullyClaimed && _splitSeen.Contains(order.ID))
                {
                    LogSplitEvent("closed", order.ID, 0, 0);
                    _splitSeen.Remove(order.ID);
                }

                if (residualFullyBoundThisDecision || order.IsFullyClaimed)
                    completedOrders++;
                if (order.IsFullyClaimed)
                {
                    // AllocateOrder already dropped the fast-path case (target == order) from
                    // _pendingOrders internally; this Remove is a no-op there and is the only
                    // removal path for a split parent, which is never itself passed to
                    // AllocateOrder (only its children are).
                    _pendingOrders.Remove(order);
                    (Instance.ItemManager as ItemManager).TakeAvailableOrder(order);
                }
            }
            // Measurement only: what the price WOULD be if the numerator were this decision's own
            // dispatch distance and the denominator matched the objective's progress term.
            // Registered BEFORE the counters below so kappa reflects the same state the decision
            // was priced at. Nothing reads LambdaRef back into a decision.
            _pricing.RegisterReplacementSample(result.DStar, closedLines, completedOrders);
            _pricing.RegisterClosedLines(closedLines);
            _pricing.RegisterCompletedOrders(completedOrders);
        }
    }
}
