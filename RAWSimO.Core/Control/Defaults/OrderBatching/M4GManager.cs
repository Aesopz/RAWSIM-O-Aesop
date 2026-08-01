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
        /// <summary>Per-decision diagnostic log.</summary>
        private System.IO.StreamWriter _decisionLog;
        /// <summary>Running decision counter, also the probe cadence clock.</summary>
        private int _decisionIndex = 0;

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
                + "lambda,mu,delta,epsilon,valuedLines,boundLines,valuedOrders,boundOrders,newTrips,boundUnits,"
                + "objective,solveSec,qmax,rho,unitsFromSunk,unitsFromNew,inboundCoverUnits,"
                + "splitsDeferred,splitsCommitted,wipOrders,wipUnits,dinkIters,lambdaStart,lambdaEnd");
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

        /// <summary>Writes one decision row. Every numeric field is written unformatted for exact diffing.</summary>
        private void WriteDecision(bool solved, int pendingOrders, int stationsWithCap, int podsPa, int podsPb,
            int botsRa, double lambda, double mu, double delta, double epsilon, int valuedLines, int boundLines,
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
                botsRa.ToString(), lambda.ToString(), mu.ToString(), delta.ToString(), epsilon.ToString(),
                valuedLines.ToString(), boundLines.ToString(), valuedOrders.ToString(), boundOrders.ToString(),
                newTrips.ToString(), boundUnits.ToString(), objective.ToString(), solveSec.ToString(),
                qmax.ToString(), rho.ToString(), unitsFromSunk.ToString(), unitsFromNew.ToString(),
                inboundCoverUnits.ToString(), splitsDeferred.ToString(), splitsCommitted.ToString(),
                wipOrders.ToString(), wipUnits.ToString(), dinkIters.ToString(), lambdaStart.ToString(),
                lambdaEnd.ToString() }));
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

            snap.PiSKU = GeneratePiSKU(snap.AllPods);
            snap.PendingOrders = new HashSet<Order>(candidates.Where(o =>
                o.RemainingPositions.Any(p => snap.PiSKU.ContainsKey(p.Key))));

            // Optional solve-time convergence knob: keep only the most urgent K orders.
            if (_m4gConfig.ValuationOrderLimit > 0 && snap.PendingOrders.Count > _m4gConfig.ValuationOrderLimit)
                snap.PendingOrders = new HashSet<Order>(snap.PendingOrders
                    .OrderBy(o => o.DueTime).ThenBy(o => o.ID)
                    .Take(_m4gConfig.ValuationOrderLimit));

            snap.Residuals = snap.PendingOrders.ToDictionary(
                o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            return snap;
        }

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
                    sym.Chat.Add(new Symbol { order = order, skui = sku.Key,
                        name = "ch_" + order.ID + "_" + sku.Key.ID });
                    foreach (var pod in snap.PiSKU[sku.Key])
                        foreach (var station in snap.Cs.Keys)
                            sym.Qhat.Add(new Symbol { order = order, skui = sku.Key, pod = pod,
                                outputstation = station,
                                name = "qh_" + sku.Key.ID + "_" + order.ID + "_" + pod.ID + "_" + station.ID });
                }
            }
            return sym;
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

        /// <summary>Adds the valuation-layer constraints V1-V4 (spec 3.4).</summary>
        private void AddValuationConstraints(LinearModel wrapper, M4GSnapshot snap, M4GSymbols sym,
            VariableCollection<string> bin, VariableCollection<string> qh)
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
                    var draws = sym.Qhat.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID)
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

            // (V6, gated) ForbidSplitting: restricts each order line to a single (pod, station)
            // supplier, so an order's demand for one SKU can never be met by combining pods or
            // stations. This is additional to V2 (residual-demand bound) - V2 caps the QUANTITY
            // drawn across all candidates, V6 restricts the SUPPLIER SET to exactly one candidate.
            // g_<sku>_<order>_<pod>_<station> is a binary "this candidate supplies this line"
            // indicator; it is added to the shared `bin` collection (already Binary 0..1) under a
            // distinct "g_" prefix rather than threading a new VariableCollection through every
            // caller - when the flag is off, no g variables are ever created, so the model (and
            // the regression run) is unaffected.
            //
            // q inherits this restriction for free: B1 constrains qb <= qh (or qb == qh under
            // DegenerateToBindingOnly) elementwise per (sku,order,pod,station), and V6a below
            // forces qh to 0 for every candidate whose g is 0. Since V6b allows at most one g=1
            // per (order,sku), qh - and therefore qb - can be nonzero for at most one supplier.
            // No separate constraint on q is needed.
            if (_m4gConfig.ForbidSplitting)
            {
                foreach (var order in snap.PendingOrders)
                    foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                    {
                        var candidates = sym.Qhat.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID).ToList();
                        if (candidates.Count == 0) continue;
                        var gVars = new List<Variable>();
                        foreach (var v in candidates)
                        {
                            string gName = "g_" + sku.Key.ID + "_" + order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID;
                            wrapper.AddConstr(qh[v.name] <= sku.Value * bin[gName], "V6a");
                            gVars.Add(bin[gName]);
                        }
                        wrapper.AddConstr(LinearExpression.Sum(gVars) <= 1, "V6b");
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

                    var paDraws = sym.Qhat.Where(v => v.skui.ID == skuId && snap.Pa.Contains(v.pod))
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
            public double SolveSec;
            public int NewTripCount;
            /// <summary>Binding-layer draws: (sku, order, pod, station) -> units.</summary>
            public Dictionary<Symbol, int> BoundDraws = new Dictionary<Symbol, int>();
            /// <summary>Bot chosen for each newly dispatched pod.</summary>
            public Dictionary<int, Bot> BotByPodId = new Dictionary<int, Bot>();
            /// <summary>Line keys the valuation layer closed.</summary>
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
        private M4GResult SolveM4G(M4GSnapshot snap, double lambda, double lambda0, double mu0,
            double epsilon0, double rho0, double delta)
        {
            M4GResult result = new M4GResult();
            M4GSymbols sym = BuildSymbols(snap);
            if (sym.Qhat.Count == 0) return result;

            // The LinearModel (and the native Gurobi model/environment it owns) must stay alive
            // until every value this method needs has been read out of the solved variables -
            // GetValue() queries the live Gurobi model, it does not cache. Everything below reads
            // those values into plain C# fields on `result` before returning, so disposing in a
            // finally block around the whole build/solve/read sequence is safe: the model is
            // released as soon as this decision's solve is done, whether it found a solution,
            // returned early for lack of one, or the caller re-solves at a new lambda for the
            // Dinkelbach iteration (each iteration gets and disposes its own model).
            LinearModel wrapper = new LinearModel(SolverType.Gurobi, (string s) => { Console.Write(s); });
            try
            {
            int maxUnits = snap.Residuals.Count > 0
                ? snap.Residuals.Values.SelectMany(d => d.Values).DefaultIfEmpty(1).Max() : 1;
            int maxSlots = snap.Cs.Count > 0 ? snap.Cs.Values.DefaultIfEmpty(1).Max() : 1;
            VariableCollection<string> bin = new VariableCollection<string>(wrapper, VariableType.Binary, 0, 1,
                (string s) => { return s; });
            VariableCollection<string> qh = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxUnits,
                (string s) => { return s; });
            VariableCollection<string> qb = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxUnits,
                (string s) => { return s; });

            AddSharedConstraints(wrapper, snap, sym, bin);
            AddValuationConstraints(wrapper, snap, sym, bin, qh);

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
                foreach (var station in snap.Cs.Keys)
                {
                    var draws = sym.Qhat.Where(v => v.order.ID == order.ID && v.outputstation.ID == station.ID)
                        .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                        .ToList();
                    if (draws.Count == 0) continue;
                    string yName = "y_" + order.ID + "_" + station.ID;
                    // (B2) drawing for an order at a station occupies one of its slots
                    wrapper.AddConstr(LinearExpression.Sum(draws) <= totalResidual * bin[yName], "B2");
                    // (B4) no empty binding
                    wrapper.AddConstr(bin[yName] <= LinearExpression.Sum(draws), "B4");
                }
                foreach (var sku in snap.Residuals[order].Where(p => snap.PiSKU.ContainsKey(p.Key)))
                {
                    var skuDraws = sym.Qhat.Where(v => v.order.ID == order.ID && v.skui.ID == sku.Key.ID)
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
            // T3: bound line closures at full price, valued-but-unbound at the realisation rate.
            // Guard: these use the plain Sum(IEnumerable<Variable>) overload, which throws on an
            // empty sequence. sym.Chat can only be empty if sym.Qhat is empty too (Qhat is built
            // strictly inside the Chat loop), and that case already returned above - so this is
            // defensive, not load-bearing, but kept for consistency and future-proofing.
            var chatBoundVars = sym.Chat.Select(v => bin["c_" + v.order.ID + "_" + v.skui.ID]).ToList();
            if (chatBoundVars.Count > 0)
                objective = objective + LinearExpression.Sum(chatBoundVars) * (-lambda * (1.0 - delta));
            var chatValuedVars = sym.Chat.Select(v => bin[v.name]).ToList();
            if (chatValuedVars.Count > 0)
                objective = objective + LinearExpression.Sum(chatValuedVars) * (-lambda * delta);
            // T4: same split for completed orders. Same guard rationale as T3.
            var zhatBoundVars = sym.Zhat.Select(v => bin["z_" + v.order.ID]).ToList();
            if (zhatBoundVars.Count > 0)
                objective = objective + LinearExpression.Sum(zhatBoundVars) * (-mu * (1.0 - delta));
            var zhatValuedVars = sym.Zhat.Select(v => bin[v.name]).ToList();
            if (zhatValuedVars.Count > 0)
                objective = objective + LinearExpression.Sum(zhatValuedVars) * (-mu * delta);
            // T5: tie-break that prefers executing now among equally valued solutions. sym.Qhat
            // is guaranteed non-empty by the early return above, but guard anyway for consistency.
            var qbTieVars = sym.Qhat.Select(v =>
                qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID]).ToList();
            if (qbTieVars.Count > 0)
                objective = objective + LinearExpression.Sum(qbTieVars) * (-epsilon);
            // T6: pod-tier draw pricing (rho), binding layer only - the tier preference is about
            // which pod actually gets drained, and only bound (qb) draws have real-world effects.
            // A unit bound from a Pp pod (processing right now, window closing) is rewarded -rho:
            // skipping it means paying for a future trip to fetch that item later. A unit bound
            // from a Pa pod (newly dispatched this decision) pays +rho: it genuinely costs an
            // extra trip that a sunk pod's unit does not. Pq/Pb (queued/en route) draws stay free,
            // matching the T1/T2 sunk-trip treatment of those same pods. false reproduces the flat
            // (every draw free) behaviour bit-for-bit.
            double rho = rho0 * lambdaScaleRatio;
            if (_m4gConfig.PodTierDrawPricingEnabled)
            {
                HashSet<int> processingPodIds = BuildProcessingPodIds(snap);
                var newPodDraws = sym.Qhat.Where(v => snap.Pa.Contains(v.pod))
                    .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                    .ToList();
                if (newPodDraws.Count > 0)
                    objective = objective + LinearExpression.Sum(newPodDraws) * rho;
                var processingDraws = sym.Qhat.Where(v => processingPodIds.Contains(v.pod.ID))
                    .Select(v => qb["q_" + v.skui.ID + "_" + v.order.ID + "_" + v.pod.ID + "_" + v.outputstation.ID])
                    .ToList();
                if (processingDraws.Count > 0)
                    objective = objective + LinearExpression.Sum(processingDraws) * (-rho);
            }
            wrapper.SetObjective(objective, OptimizationSense.Minimize);

            DateTime solveStart = DateTime.Now;
            wrapper.Update();
            wrapper.Optimize();
            result.SolveSec = (DateTime.Now - solveStart).TotalSeconds;
            if (!wrapper.HasSolution()) return result;

            result.HasSolution = true;
            result.Objective = wrapper.GetObjectiveValue();
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
            foreach (var v in sym.Xps.Where(v => snap.Pa.Contains(v.pod)))
                if (Math.Round(bin[v.name].GetValue()) != 0)
                {
                    result.NewTripCount++;
                    var carrier = sym.Yrp.FirstOrDefault(y => y.pod.ID == v.pod.ID
                        && Math.Round(bin[y.name].GetValue()) != 0);
                    if (carrier != null) result.BotByPodId[v.pod.ID] = carrier.robot;
                }
            return result;
            }
            finally
            {
                wrapper.Dispose();
            }
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
            _lastInboundCoverUnits = 0.0;
            _lastSplitsDeferred = 0;
            _lastSplitsCommitted = 0;
            double cumDist = Instance.StatOverallDistanceTraveled;
            double lambda0 = _pricing.Lambda(cumDist);
            double mu0 = _pricing.Mu(cumDist);
            double epsilon0 = _pricing.Epsilon(cumDist);
            double rho0 = _pricing.Rho(cumDist, Instance.StatOverallItemsHandled);
            double delta = _pricing.Delta();

            // ── Dinkelbach iteration (0 = single step at the historical lambda, unchanged
            // behaviour). Every re-solve rebuilds the model from scratch at a new lambda; a
            // single solve costs 0.009-0.2s so this is cheap even at the iteration cap. ──
            double lambdaK = lambda0;
            M4GResult result = SolveM4G(snap, lambdaK, lambda0, mu0, epsilon0, rho0, delta);
            double totalSolveSec = result.SolveSec;
            int dinkIters = 0;
            if (result.HasSolution)
            {
                for (int i = 0; i < _m4gConfig.DinkelbachIterations; i++)
                {
                    // V* = (D* - objective) / lambda_k. The objective returned by the solver is
                    // exactly D* - lambda_k*V* by construction (every value coefficient scales
                    // with lambda_k - see SolveM4G's remarks), so this recovers V* without
                    // needing a second pass over the solution.
                    double vStar = lambdaK > 0 ? (result.DStar - result.Objective) / lambdaK : 0.0;
                    if (vStar <= 0) break;                                    // ratio undefined - keep this solution
                    if (Math.Abs(result.Objective) <= _m4gConfig.DinkelbachTolerance) break;  // converged
                    double lambdaNext = result.DStar / vStar;
                    M4GResult next = SolveM4G(snap, lambdaNext, lambda0, mu0, epsilon0, rho0, delta);
                    totalSolveSec += next.SolveSec;
                    if (!next.HasSolution) break;                             // keep the previous solution
                    // Guard against MILP-integrality overshoot. Dinkelbach's continuous-relaxation
                    // proof has lambda_k decrease monotonically toward the true minimum ratio
                    // without ever undershooting it, but the achievable ratio here is a step
                    // function of lambda (integer pod/bot/order assignment), so one re-solve can
                    // jump straight past the fixed point into the region where "dispatch nothing"
                    // (D=0, V=0, always feasible at objective 0) strictly dominates every real
                    // dispatch. Adopting that trivial solution would silently zero out this
                    // decision's picks - confirmed in testing: an unguarded loop collapsed a whole
                    // 2h run to ~0 orders completed once lambda first overshot on decision 0.
                    // Evaluate the CANDIDATE's own V* before adopting it; if it is degenerate,
                    // stop and keep the last non-degenerate solution instead.
                    double nextVStar = lambdaNext > 0 ? (next.DStar - next.Objective) / lambdaNext : 0.0;
                    if (nextVStar <= 0) break;
                    result = next;
                    lambdaK = lambdaNext;
                    dinkIters++;
                }
            }
            double lambdaEnd = lambdaK;

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
            double cumDistAfter = Instance.StatOverallDistanceTraveled;
            WriteDecision(result.HasSolution, snap.PendingOrders.Count, snap.Cs.Count(c => c.Value > 0),
                snap.Pa.Count, snap.Pb.Count, snap.Ra.Count,
                _pricing.Lambda(cumDistAfter), _pricing.Mu(cumDistAfter), _pricing.Delta(),
                _pricing.Epsilon(cumDistAfter),
                result.ValuedLineKeys.Count, result.BoundLineKeys.Count, result.ValuedOrders, result.BoundOrders,
                result.NewTripCount, result.BoundDraws.Values.Sum(), result.Objective, totalSolveSec, _lastQmax,
                result.Rho, result.UnitsFromSunk, result.UnitsFromNew, _lastInboundCoverUnits,
                _lastSplitsDeferred, _lastSplitsCommitted, dinkIters, lambda0, lambdaEnd);
            _decisionIndex++;
            // Feed this decision's valuation/binding line counts into delta's running totals,
            // after WriteDecision so the logged delta (like lambda/mu) reflects state prior to
            // this decision's own contribution - same convention as RegisterClosedLines below.
            _pricing.RegisterDecision(result.BoundLineKeys.Count, result.ValuedLineKeys.Count);
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
                // allocation) per station touched.
                foreach (var stationGroup in mine.GroupBy(e => e.Key.outputstation.ID))
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
                    if (coversWholeOrder && stationGroup.Count() == mine.Count && !order.IsSplitParent)
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
            _pricing.RegisterClosedLines(closedLines);
            _pricing.RegisterCompletedOrders(completedOrders);
        }
    }
}
