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
    /// M4G-NS v2: M1G's exact decision structure - variables xps/yos/yaos/yrp/us/dops,
    /// constraints shi2..shi13 - with ONLY the hand-tuned objective weights w1/w2/w3 replaced
    /// by measured prices mu/delta/sigma. There are no item-level (g/gh/z/zh) variables: the
    /// order is the atom, exactly as in M1G, and INV-1/INV-2/INV-3 hold structurally because
    /// those variables simply do not exist in this file.
    ///
    /// v1 (superseded - see "計畫增補 v2" at the end of
    /// docs/superpowers/plans/2026-08-09-m4g-ns.md) invented its own g/gh/z/zh model with
    /// constraints N1-N6. That silently dropped M1G's dominant behavioural driver, w3=1000
    /// (the idle-slot penalty that forces M1G to fill stations), replacing it with a bare
    /// inequality that has no price at all. The ablation was therefore confounded: v1 was
    /// "M1G + pricing - guardrail", not "M1G + pricing". v2 fixes this by mirroring M1G's
    /// variables and constraints literally and changing only the objective.
    ///
    /// Two-layer structure (mirrors M1G exactly, spec section "v2 模型"):
    ///   yos[o,s]  - VALUATION layer: order o could be covered by station s this decision
    ///               (shi2: at most one station; shi5: needs pod coverage). NOT slot-limited.
    ///   yaos[o,s] - BINDING layer: order o actually occupies a slot at station s
    ///               (shi3: yaos &lt;= yos; shi4: slot capacity, WITH THE us SLACK, as an
    ///               EQUALITY). This is what is actually committed - AllocateOrder is called
    ///               from yaos==1 exactly as M1GManager.cs:795-805 does.
    ///
    /// Objective (the only thing that changes from M1G):
    ///   min  Sum_ps D_ps * xps + Sum_rp D_rp * yrp                (w1=1, unchanged - metres)
    ///        - mu*(1-delta) * Sum_os yaos                         (replaces w2, binding side)
    ///        - mu*delta     * Sum_os yos                          (replaces w2, valuation side)
    ///        + sigma        * Sum_s us                            (replaces w3=1000)
    /// sigma = SlotScale * mu (SlotScale default 1.0: an empty slot costs one order's value).
    /// At delta=1 this reduces exactly to M1G's `w2 * Sum(yos)` form - the S-8 degeneracy check
    /// pinning delta=1, mu=40, SlotScale=25 (=1000/40) must reproduce M1G-scale behaviour.
    ///
    /// Spec: docs/superpowers/specs/2026-08-09-m4g-ns-whole-order-design.md
    /// Plan addendum: "計畫增補 v2" at the end of docs/superpowers/plans/2026-08-09-m4g-ns.md
    /// (that section supersedes the earlier Task 3 in the same file).
    ///
    /// Constitution: M1GManager / HADGSManager / M4GManager / M4GPricing / SplitM2eICManager /
    /// GreedyM5Manager are never modified. M1GManager.Initialize(...) and
    /// M1GManager.solve(...) are PRIVATE, so this class cannot call them; instead it mirrors
    /// their bodies (M1GManager.cs:558-650 and :681-978) verbatim except for the objective and
    /// the Dinkelbach loop wrapped around it. It DOES reuse M1GManager's PUBLIC building blocks
    /// unchanged - GenerateCs, GeneratePs, GeneratePiSKU, GenerateOiSKU, GenerateOd,
    /// IsAvailabletoPiSKU, CreatedeVarName - so the variable-name generation for xps/yos/yaos/
    /// yrp/us/dops is literally the same code M1G runs, not a re-derivation of it. Mirror
    /// infidelity is the defect the project constitution flags; duplication is expected and
    /// required.
    /// </summary>
    public class M4GNSManager : M1GManager
    {
        /// <summary>Creates a new instance of this manager.</summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public M4GNSManager(Instance instance) : base(instance)
        {
            _cfg = instance.ControllerConfig.OrderBatchingConfig as M4GNSConfiguration;
            if (_cfg == null)
                throw new InvalidOperationException("M4GNSManager requires a M4GNSConfiguration.");
            // Unit-consistency guard, mirrored from M4GManager's constructor: mu is priced in
            // metres, which requires the objective's distance terms to be metres too.
            // StarveAwareCostEnabled switches the cost functions to seconds - fail hard rather
            // than solve nonsense.
            if (instance.SettingConfig != null && instance.SettingConfig.StarveAwareCostEnabled)
                throw new InvalidOperationException(
                    "M4G-NS prices orders in metres but StarveAwareCostEnabled makes the distance terms seconds. "
                    + "Set StarveAwareCostEnabled=false or disable M4G-NS.");
            _pricing = new M4GNSPricing(_cfg);
        }

        /// <summary>The M4G-NS configuration of this manager.</summary>
        private M4GNSConfiguration _cfg;
        /// <summary>Price calibration state (mu, delta only).</summary>
        private M4GNSPricing _pricing;
        /// <summary>Highest Pb value that gets its own delta stratum; everything above shares it.
        /// Same key M4G uses (min(|Pb|,3)).</summary>
        private const int DeltaStratumCap = 3;
        /// <summary>Running decision counter.</summary>
        private int _decisionIndex = 0;
        /// <summary>Per-decision diagnostic log.</summary>
        private System.IO.StreamWriter _decisionLog;

        /// <summary>Lazily opens m4gns_decision_log.csv in the run's statistics directory.</summary>
        private void EnsureDecisionLog()
        {
            if (_decisionLog != null) return;
            string dir = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            _decisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "m4gns_decision_log.csv"), false)
            { AutoFlush = true };
            _decisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsPa,podsPb,botsRa,mu,delta,"
                + "valuedOrders,boundOrders,newTrips,sumUs,objective,solveSec,dinkIters,muStart,muEnd");
        }

        /// <summary>Writes one decision row. Every numeric field is written unformatted for exact diffing.</summary>
        private void WriteDecision(bool solved, int pendingOrders, int stationsWithCap, int podsPa, int podsPb,
            int botsRa, double mu, double delta, int valuedOrders, int boundOrders, int newTrips, double sumUs,
            double objective, double solveSec, int dinkIters, double muStart, double muEnd)
        {
            EnsureDecisionLog();
            _decisionLog.WriteLine(string.Join(",", new string[] {
                _decisionIndex.ToString(),
                Instance.Controller.CurrentTime.ToString(),
                (solved ? "1" : "0"),
                pendingOrders.ToString(), stationsWithCap.ToString(), podsPa.ToString(), podsPb.ToString(),
                botsRa.ToString(), mu.ToString(), delta.ToString(),
                valuedOrders.ToString(), boundOrders.ToString(), newTrips.ToString(), sumUs.ToString(),
                objective.ToString(), solveSec.ToString(), dinkIters.ToString(),
                muStart.ToString(), muEnd.ToString() }));
        }

        /// <summary>Mirrors M1GManager.M1GBotPodCost (private in the base class, so inaccessible to
        /// a subclass - constitution requires mirroring rather than touching M1GManager.cs). The
        /// constructor hard-throws if StarveAwareCostEnabled is set (metres-only invariant), so
        /// this mirror is just the raw-distance path.</summary>
        private double M1GBotPodCostNS(Bot robot, Pod pod)
        {
            return EstimateBotPodDistance(robot, pod);
        }

        /// <summary>Mirrors M1GManager.M1GPodStationCost for the same reason as
        /// <see cref="M1GBotPodCostNS"/>.</summary>
        private double M1GPodStationCostNS(Pod pod, OutputStation station)
        {
            return EstimatePodStationDistance(pod, station);
        }

        /// <summary>
        /// Which delta bucket this decision belongs to, or -1 when StratifiedDelta is off so
        /// every price read falls back to the system-wide ratio. Same stratifier M4G uses
        /// (committed-pod count, capped).
        /// </summary>
        private int DeltaStratum(HashSet<Pod> Pb)
        {
            if (!_cfg.StratifiedDelta) return -1;
            return Math.Min(Pb.Count, DeltaStratumCap);
        }

        /// <summary>
        /// Decision-time snapshot of the world plus the generated decision-variable symbol
        /// tables. This is a faithful mirror of M1GManager.Initialize (M1GManager.cs:558-650) -
        /// same admission rules for pods/bots/orders, same call sequence into the SAME public
        /// helper methods M1G itself uses (GenerateCs/GeneratePs/GeneratePiSKU/GenerateOiSKU/
        /// GenerateOd/CreatedeVarName), so the resulting xps/yos/yaos/yrp/us/dops symbol names
        /// are generated by the exact same code M1G runs, not re-derived.
        /// </summary>
        private HashSet<Pod> BuildInputsNS(out Dictionary<ItemDescription, List<Pod>> PiSKU, out Dictionary<ItemDescription, List<Order>> OiSKU,
            out Dictionary<int, List<Symbol>> variableNames, out Dictionary<OutputStation, int> Cs, out HashSet<Order> pendingOrders,
            out Dictionary<OutputStation, HashSet<Pod>> inboundPods, out HashSet<Bot> Ra, out HashSet<Bot> Rb,
            out HashSet<Bot> R, out HashSet<Pod> Pb, out HashSet<Pod> Pa, out Dictionary<Pod, Bot> PodToBot)
        {
            HashSet<Order> pendingOrders1 = new HashSet<Order>(_pendingOrders.Where(o => o.Positions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value)));
            OiSKU = GenerateOiSKU(pendingOrders1);
            Cs = GenerateCs();
            inboundPods = GeneratePs(Cs);
            HashSet<ItemDescription> ItemofOiSKU = new HashSet<ItemDescription>(OiSKU.Keys);
            HashSet<Pod> allPods = new HashSet<Pod>();
            HashSet<Order> Od;
            Ra = new HashSet<Bot>();
            Rb = new HashSet<Bot>();
            R = new HashSet<Bot>();
            Pb = new HashSet<Pod>();
            PodToBot = new Dictionary<Pod, Bot>();
            HashSet<Pod> Pa1 = new HashSet<Pod>();
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
                        var bot = Instance.ResourceManager.BottoPod.Where(v => v.Value.ID == pod.ID).First().Key;
                        allPods.Add(pod);
                        Rb.Add(bot);
                        R.Add(bot);
                        PodToBot[pod] = bot;
                        Pb.Add(pod);
                    }
                    else
                    {
                        // Snapshot can contain a station inbound pod before its pod-bot ownership is visible.
                        // Exclude it from the model for this decision (mirrors M1G).
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
                Pa1.Add(pod);
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
            pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.Positions.All(p => IsAvailabletoPiSKU(p.Key) >= p.Value)));
            Od = GenerateOd(pendingOrders, PiSKU);
            if (Od.Count > Cs.Values.Sum())
                pendingOrders = new HashSet<Order>(Od);
            OiSKU = GenerateOiSKU(pendingOrders);
            variableNames = CreatedeVarName(PiSKU, OiSKU, allPods, pendingOrders, Cs, R, Pa1, out Pa);
            return allPods;
        }

        /// <summary>
        /// Mu-independent half of one decision's MILP: variables plus constraints shi2..shi13,
        /// a literal mirror of M1GManager.cs:685-772 (minus the objective, which the Dinkelbach
        /// loop sets separately via <see cref="SolveOnceNS"/>). Column-for-column reference to
        /// the source lines is in the class-level report, not repeated here.
        /// </summary>
        private sealed class NSModel : IDisposable
        {
            public LinearModel Wrapper;
            public VariableCollection<string> VariablesBinary;
            public VariableCollection<string> VariablesInteger3;
            public List<Symbol> DeVarNamexps;
            public List<Symbol> DeVarNameyos;
            public List<Symbol> DeVarNameyaos;
            public List<Symbol> DeVarNameyrp;
            public List<Symbol> DeVarNameus;
            public List<Symbol> DeVarNamedops;
            public void Dispose()
            {
                if (Wrapper != null) { Wrapper.Dispose(); Wrapper = null; }
            }
        }

        /// <summary>Builds the shared (mu-independent) model: all six variable collections and
        /// constraints shi2..shi13, verbatim mirror of M1GManager.cs:685-772.</summary>
        private NSModel BuildModelNS(Dictionary<ItemDescription, List<Pod>> PiSKU, Dictionary<ItemDescription, List<Order>> OiSKU,
            IEnumerable<Pod> Pods, Dictionary<OutputStation, int> Cs, Dictionary<int, List<Symbol>> variableNames,
            HashSet<Order> pendingOrders, Dictionary<OutputStation, HashSet<Pod>> inboundPods,
            HashSet<Bot> Ra, HashSet<Bot> R, HashSet<Pod> Pb, HashSet<Pod> Pa, Dictionary<Pod, Bot> PodToBot)
        {
            LinearModel wrapper = new LinearModel(SolverType.Gurobi, (string s) => { Console.Write(s); });
            List<Symbol> deVarNamexps = variableNames[1];
            List<Symbol> deVarNameyos = variableNames[2];
            List<Symbol> deVarNameyaos = variableNames[3];
            List<Symbol> deVarNameyrp = variableNames[4];
            List<Symbol> deVarNameus = variableNames[5];
            List<Symbol> deVarNamedops = variableNames[6];
            try
            {
                VariableCollection<string> variablesBinary = new VariableCollection<string>(wrapper, VariableType.Binary, 0, 1, (string s) => { return s; });
                // Bound 0..6 mirrors M1GManager.cs:700 literally (M1G's own hard-coded "us" bound,
                // not derived from station capacity) - faithful mirror includes this quirk.
                VariableCollection<string> variablesInteger3 = new VariableCollection<string>(wrapper, VariableType.Integer, 0, 6, (string s) => { return s; });

                // M1GManager.solve() also calls PrepareStarveAware(...) here, but that method is
                // private to M1GManager and hence inaccessible to this subclass. It is harmless to
                // omit: the constructor already hard-throws if StarveAwareCostEnabled is set, so
                // M1G's own call would be a no-op in every reachable state of this class anyway.
                PrepareDecisionExtras(Pods, Cs, Ra);

                // shi2: each order goes to at most one station (valuation layer). M1GManager.cs:714-715.
                foreach (var order in pendingOrders)
                    wrapper.AddConstr(LinearExpression.Sum(deVarNameyos.Where(v => v.order.ID == order.ID).Select(v => variablesBinary[v.name])) <= 1, "shi2");
                // shi3: binding requires valuation. M1GManager.cs:716-721.
                foreach (var order in pendingOrders)
                    foreach (var station in Cs.Keys)
                        wrapper.AddConstr(variablesBinary["yaos" + "_" + order.ID.ToString() + "_" + station.ID.ToString()] <= variablesBinary
                            ["yos" + "_" + order.ID.ToString() + "_" + station.ID.ToString()], "shi3");
                // shi4: slot capacity, EQUALITY with the us slack. M1GManager.cs:722-723.
                foreach (var station in Cs.Keys)
                    wrapper.AddConstr(LinearExpression.Sum(deVarNameyaos.Where(v => v.outputstation.ID == station.ID).Select(v => variablesBinary[v.name])) == Cs[station] - variablesInteger3["us" + "_" + station.ID.ToString()], "shi4");
                // shi5: pod coverage bounds the valuation layer, per (sku, station). M1GManager.cs:724-732.
                foreach (var sku in OiSKU.Where(v => PiSKU.ContainsKey(v.Key)))
                {
                    foreach (var instance in Cs.Keys)
                    {
                        wrapper.AddConstr(LinearExpression.Sum(deVarNameyos.Where(v => v.outputstation.ID == instance.ID && sku.Value.Contains(v.order)).Select(v => v.order.PositionOverallCount(sku.Key)
                        * variablesBinary[v.name])) <= LinearExpression.Sum(deVarNamexps.Where(v => v.outputstation.ID == instance.ID && PiSKU[sku.Key].Contains(v.pod)).Select(v =>
                        v.pod.CountAvailable(sku.Key) * variablesBinary[v.name])), "shi5");
                    }
                }
                // shi6: each pod goes to at most one station. M1GManager.cs:733-734.
                foreach (var pod in Pods)
                    wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "shi6");
                // shi7 / shi11: already-committed pods/bots are fixed. M1GManager.cs:735-744.
                foreach (var station in inboundPods)
                {
                    foreach (var pod in station.Value)
                    {
                        if (!Pb.Contains(pod))
                            continue;
                        wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.Key.ID.ToString()] == 1, "shi7");
                        wrapper.AddConstr(variablesBinary["yrp" + "_" + PodToBot[pod].ID.ToString() + "_" + pod.ID.ToString()] == 1, "shi11");
                    }
                }
                // shi8: dispatching a pod requires a bot. M1GManager.cs:745-746.
                foreach (var pod in Pods)
                    wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])), "shi8");
                // shi9: each pod carried by at most one bot. M1GManager.cs:747-748.
                foreach (var pod in Pods)
                    wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "shi9");
                // shi10: each bot carries at most one pod. M1GManager.cs:749-750.
                foreach (var robot in R)
                    wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.robot.ID == robot.ID).Select(v => variablesBinary[v.name])) <= 1, "shi10");
                // shi12: dops requires both the pod-station and order-station bindings. M1GManager.cs:751-765.
                foreach (var sku in OiSKU.Where(v => PiSKU.ContainsKey(v.Key)))
                {
                    List<Pod> listofpod = PiSKU[sku.Key];
                    foreach (var pod in listofpod.Where(v => Pa.Contains(v)))
                    {
                        foreach (var order in sku.Value)
                        {
                            foreach (var station in Cs.Keys)
                            {
                                wrapper.AddConstr(2 * variablesBinary["dops" + "_" + order.ID.ToString() + "_" + pod.ID.ToString() + "_" + station.ID.ToString()]
                                    <= variablesBinary["yaos" + "_" + order.ID.ToString() + "_" + station.ID.ToString()] + variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()], "shi12");
                            }
                        }
                    }
                }
                // shi13: a newly-dispatched pod must serve at least one dops draw. M1GManager.cs:766-771.
                foreach (var pod in Pa)
                {
                    foreach (var station in Cs.Keys)
                        wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()]
                            <= LinearExpression.Sum(deVarNamedops.Where(v => v.pod.ID == pod.ID && v.outputstation.ID == station.ID).Select(v => variablesBinary[v.name])), "shi13");
                }

                return new NSModel
                {
                    Wrapper = wrapper,
                    VariablesBinary = variablesBinary,
                    VariablesInteger3 = variablesInteger3,
                    DeVarNamexps = deVarNamexps,
                    DeVarNameyos = deVarNameyos,
                    DeVarNameyaos = deVarNameyaos,
                    DeVarNameyrp = deVarNameyrp,
                    DeVarNameus = deVarNameus,
                    DeVarNamedops = deVarNamedops,
                };
            }
            catch
            {
                wrapper.Dispose();
                throw;
            }
        }

        /// <summary>Result of one Dinkelbach iteration's solve - only what the loop and the final
        /// commit step need.</summary>
        private struct NSSolveResult
        {
            public bool HasSolution;
            public double Objective;
            /// <summary>Realised distance D* (T1+T2 terms only, mu-independent) read back from
            /// the solved xps/yrp variables - the Dinkelbach numerator.</summary>
            public double DStar;
            public double SolveSec;
        }

        /// <summary>
        /// Sets this iteration's objective and solves. Mirrors M1GManager.cs:694-713's objective
        /// construction (same Ra.Count()&gt;0 branch, same UnusedPods filter, same w1=1 on the
        /// distance terms) with w2/w3 replaced by mu/delta/sigma. Every call replaces the
        /// objective outright; the constraint system (shi2..shi13) is untouched between calls.
        /// </summary>
        private NSSolveResult SolveOnceNS(NSModel model, Dictionary<OutputStation, int> Cs, HashSet<Bot> Ra,
            double mu, double delta, double sigma)
        {
            LinearModel wrapper = model.Wrapper;
            VariableCollection<string> variablesBinary = model.VariablesBinary;
            VariableCollection<string> variablesInteger3 = model.VariablesInteger3;
            double w1 = 1;

            LinearExpression distanceTerm = Ra.Count() > 0
                ? LinearExpression.Sum(model.DeVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (M1GPodStationCostNS(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation))), wrapper)
                    + LinearExpression.Sum(model.DeVarNameyrp.Where(u => Ra.Contains(u.robot) && Instance.ResourceManager.UnusedPods.Contains(u.pod) && u.pod.Waypoint != null).Select(v => variablesBinary[v.name] *
                    M1GBotPodCostNS(v.robot, v.pod)), wrapper)
                : LinearExpression.Sum(model.DeVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (M1GPodStationCostNS(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation))), wrapper);

            LinearExpression objective = distanceTerm * w1
                + LinearExpression.Sum(model.DeVarNameyaos.Select(v => variablesBinary[v.name])) * (-mu * (1.0 - delta))
                + LinearExpression.Sum(model.DeVarNameyos.Select(v => variablesBinary[v.name])) * (-mu * delta)
                + LinearExpression.Sum(model.DeVarNameus.Select(v => variablesInteger3[v.name])) * sigma;

            wrapper.SetObjective(objective, OptimizationSense.Minimize);

            DateTime optStart = DateTime.Now;
            wrapper.Update();
            // The model outlives a single solve (one build, one solve per Dinkelbach mu), so drop
            // any solution left by the previous iteration before optimising - otherwise the
            // re-solve warm-starts from the old solution and may silently settle on a different
            // member of an equally-optimal set rather than actually re-optimising for the new mu.
            wrapper.Reset();
            wrapper.Optimize();
            double optSec = (DateTime.Now - optStart).TotalSeconds;

            NSSolveResult result = new NSSolveResult { SolveSec = optSec };
            if (!wrapper.HasSolution()) return result;
            result.HasSolution = true;
            result.Objective = wrapper.GetObjectiveValue();

            double dStar = 0.0;
            foreach (var v in model.DeVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)))
                if (Math.Round(variablesBinary[v.name].GetValue()) != 0)
                    dStar += M1GPodStationCostNS(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation);
            foreach (var v in model.DeVarNameyrp.Where(u => Ra.Contains(u.robot) && Instance.ResourceManager.UnusedPods.Contains(u.pod) && u.pod.Waypoint != null))
                if (Math.Round(variablesBinary[v.name].GetValue()) != 0)
                    dStar += M1GBotPodCostNS(v.robot, v.pod);
            result.DStar = dStar;
            return result;
        }

        /// <summary>Entry point called by the engine whenever a station has a free slot. Mirrors
        /// M1GManager.DecideAboutPendingOrders, with the single Gurobi solve replaced by a
        /// Dinkelbach loop over mu (same two-sided escalation as M4G) and the final readback done
        /// once, on the winning iteration.</summary>
        protected override void DecideAboutPendingOrders()
        {
            DateTime startAll = DateTime.Now;
            Dictionary<ItemDescription, List<Pod>> PiSKU;
            Dictionary<ItemDescription, List<Order>> OiSKU;
            Dictionary<int, List<Symbol>> variableNames;
            Dictionary<OutputStation, int> Cs;
            Dictionary<OutputStation, HashSet<Pod>> inboundPods;
            HashSet<Order> pendingOrders;
            HashSet<Bot> Ra, Rb, R;
            HashSet<Pod> Pb, Pa;
            Dictionary<Pod, Bot> PodToBot;

            HashSet<Pod> allPods = BuildInputsNS(out PiSKU, out OiSKU, out variableNames, out Cs, out pendingOrders,
                out inboundPods, out Ra, out Rb, out R, out Pb, out Pa, out PodToBot);

            if (R.Count() == 0 || pendingOrders.Count == 0)
                return;

            double cumDist = Instance.StatOverallDistanceTraveled;
            double mu0 = _pricing.Mu(cumDist);
            int deltaStratum = DeltaStratum(Pb);
            double delta = _pricing.Delta(deltaStratum);
            double sigma = _cfg.SlotScale * mu0;

            double muK = mu0;
            NSSolveResult result = new NSSolveResult();
            double totalSolveSec = 0.0;
            int dinkIters = 0;
            NSModel model = BuildModelNS(PiSKU, OiSKU, allPods, Cs, variableNames, pendingOrders, inboundPods, Ra, R, Pb, Pa, PodToBot);
            try
            {
                result = SolveOnceNS(model, Cs, Ra, muK, delta, sigma);
                totalSolveSec += result.SolveSec;
                if (result.HasSolution)
                {
                    int escalations = 0;
                    for (int i = 0; i < _cfg.DinkelbachIterations; i++)
                    {
                        double vStar = muK > 0 ? (result.DStar - result.Objective) / muK : 0.0;
                        // Two-sided search, mirrored from M4GManager: V* <= 0 means the best
                        // solution at this mu is "dispatch nothing", so double mu and retry
                        // rather than getting stuck unable to ever raise it.
                        if (vStar <= 0 && escalations < _cfg.DinkelbachEscalations && muK > 0)
                        {
                            escalations++;
                            double muUp = muK * 2.0;
                            double sigmaUp = _cfg.SlotScale * muUp;
                            NSSolveResult upRes = SolveOnceNS(model, Cs, Ra, muUp, delta, sigmaUp);
                            totalSolveSec += upRes.SolveSec;
                            if (!upRes.HasSolution) break;
                            result = upRes;
                            muK = muUp;
                            sigma = sigmaUp;
                            dinkIters++;
                            continue;
                        }
                        if (vStar <= 0) break;                                       // ratio undefined - keep this solution
                        if (Math.Abs(result.Objective) <= _cfg.DinkelbachTolerance) break;   // converged
                        double muNext = result.DStar / vStar;
                        if (muNext <= 0) break;
                        double sigmaNext = _cfg.SlotScale * muNext;
                        NSSolveResult next = SolveOnceNS(model, Cs, Ra, muNext, delta, sigmaNext);
                        totalSolveSec += next.SolveSec;
                        if (!next.HasSolution) break;                                // keep the previous solution
                        double nextVStar = muNext > 0 ? (next.DStar - next.Objective) / muNext : 0.0;
                        if (nextVStar <= 0) break;                                    // candidate itself degenerate
                        result = next;
                        muK = muNext;
                        sigma = sigmaNext;
                        dinkIters++;
                    }
                }

                double muEnd = muK;
                int valuedOrders = 0, boundOrders = 0, newTrips = 0;
                double sumUs = 0.0;

                if (result.HasSolution)
                {
                    // Final readback on the winning iteration's solved variables - mirrors
                    // M1GManager.cs:776-969 (the i=1..6 loop that builds _availableStationorder
                    // from yaos, claims Pa pods/bots, and the model-external ziops assignment).
                    VariableCollection<string> variablesBinary = model.VariablesBinary;
                    VariableCollection<string> variablesInteger3 = model.VariablesInteger3;

                    Dictionary<OutputStation, List<Order>> availableStationorder = new Dictionary<OutputStation, List<Order>>();
                    List<Symbol> isXps = new List<Symbol>();
                    List<Symbol> isYaos = new List<Symbol>();
                    List<Symbol> isYrp = new List<Symbol>();
                    List<Symbol> isDops = new List<Symbol>();

                    foreach (var v in model.DeVarNamexps)
                        if (Math.Round(variablesBinary[v.name].GetValue()) != 0)
                            isXps.Add(v);
                    foreach (var v in model.DeVarNameyos)
                        if (Math.Round(variablesBinary[v.name].GetValue()) != 0)
                            valuedOrders++;
                    foreach (var v in model.DeVarNameyaos)
                        if (Math.Round(variablesBinary[v.name].GetValue()) != 0)
                        {
                            isYaos.Add(v);
                            boundOrders++;
                            if (availableStationorder.ContainsKey(v.outputstation))
                                availableStationorder[v.outputstation].Add(v.order);
                            else
                                availableStationorder.Add(v.outputstation, new List<Order> { v.order });
                        }
                    foreach (var v in model.DeVarNameyrp)
                        if (Ra.Contains(v.robot) && Math.Round(variablesBinary[v.name].GetValue()) != 0)
                        {
                            isYrp.Add(v);
                            Instance.ResourceManager.BottoPod.Add(v.robot, v.pod);
                            Instance.ResourceManager.ClaimPod(v.pod, v.robot, BotTaskType.Extract);
                            foreach (var xps in isXps.Where(u => u.pod.ID == v.pod.ID))
                                xps.outputstation.RegisterInboundPod(v.pod);
                            newTrips++;
                        }
                    foreach (var v in model.DeVarNamedops)
                        if (Math.Round(variablesBinary[v.name].GetValue()) != 0)
                            isDops.Add(v);
                    foreach (var v in model.DeVarNameus)
                        sumUs += Math.Round(variablesInteger3[v.name].GetValue());

                    Dictionary<Symbol, int> newZiops = new Dictionary<Symbol, int>();
                    if (availableStationorder.Count > 0)
                        AssignZiopsNS(availableStationorder, isXps, isDops, isYrp, newZiops);

                    foreach (var symbol in newZiops)
                        for (int i = 0; i < symbol.Value; i++)
                            symbol.Key.pod.JustRegisterItem(symbol.Key.skui);

                    int completedOrders = 0;
                    foreach (var v in isYaos)
                    {
                        AllocateOrder(v.order, v.outputstation);
                        Instance.StatCustomControllerInfo.CustomLogOB1++;
                        completedOrders++;
                    }
                    _pricing.RegisterCompletedOrders(completedOrders);
                }

                WriteDecision(result.HasSolution, pendingOrders.Count, Cs.Count(c => c.Value > 0), Pa.Count, Pb.Count,
                    Ra.Count, _pricing.Mu(Instance.StatOverallDistanceTraveled), delta,
                    valuedOrders, boundOrders, newTrips, sumUs, result.Objective, totalSolveSec, dinkIters, mu0, muEnd);
                _decisionIndex++;
                // Feed this decision's bound/valued ORDER counts into delta's running totals, after
                // WriteDecision so the logged delta reflects state prior to this decision's own
                // contribution - same convention M4GManager uses for its RegisterDecision call.
                _pricing.RegisterDecision(boundOrders, valuedOrders, deltaStratum);
            }
            finally
            {
                model.Dispose();
            }
            Instance.Observer.TimeOrderBatchingbyMP((DateTime.Now - startAll).TotalSeconds);
        }

        /// <summary>
        /// Model-external assignment of specific pod draws (ziops) to the orders yaos bound this
        /// decision, and release of any dops-selected-but-unused pod claims. Faithful mirror of
        /// M1GManager.cs:826-960 - the only renaming is the container it writes into (an out
        /// parameter here vs. a field/return value there).
        /// </summary>
        private void AssignZiopsNS(Dictionary<OutputStation, List<Order>> availableStationorder,
            List<Symbol> isXps, List<Symbol> isDops, List<Symbol> isYrp, Dictionary<Symbol, int> newZiops)
        {
            foreach (var currentStationorder in availableStationorder)
            {
                Dictionary<ItemDescription, Dictionary<Pod, int>> availableCounts = new Dictionary<ItemDescription, Dictionary<Pod, int>>();
                foreach (var itemName in isXps.Where(v => v.outputstation.ID == currentStationorder.Key.ID))
                {
                    foreach (var item in itemName.pod.ItemDescriptionsContained.Where(v => itemName.pod.CountAvailable(v) > 0))
                    {
                        if (availableCounts.ContainsKey(item))
                        {
                            if (availableCounts[item].ContainsKey(itemName.pod))
                                availableCounts[item][itemName.pod] += itemName.pod.CountAvailable(item);
                            else
                                availableCounts[item].Add(itemName.pod, itemName.pod.CountAvailable(item));
                        }
                        else
                        {
                            Dictionary<Pod, int> counts = new Dictionary<Pod, int>();
                            availableCounts.Add(item, counts);
                            counts.Add(itemName.pod, itemName.pod.CountAvailable(item));
                        }
                    }
                }
                HashSet<Pod> dopsPodsSelected = new HashSet<Pod>();
                HashSet<Pod> dopsPodsUsed = new HashSet<Pod>();
                foreach (var order in currentStationorder.Value)
                {
                    Dictionary<ItemDescription, int> itemDemands = new Dictionary<ItemDescription, int>();
                    foreach (var item in order.Positions)
                        itemDemands.Add(item.Key, item.Value);
                    HashSet<Pod> orderDopsPods = new HashSet<Pod>();
                    foreach (var item in isDops.Where(v => v.order.ID == order.ID))
                    {
                        dopsPodsSelected.Add(item.pod);
                        orderDopsPods.Add(item.pod);
                    }
                    foreach (var itemDemand in itemDemands)
                    {
                        int number = itemDemand.Value;
                        while (number > 0)
                        {
                            Pod pod;
                            if (availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).Count() > 0)
                            {
                                pod = availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).First();
                                orderDopsPods.Remove(pod);
                                dopsPodsUsed.Add(pod);
                            }
                            else
                                pod = availableCounts[itemDemand.Key].Keys.First();
                            Symbol name = new Symbol
                            {
                                pod = pod,
                                order = order,
                                outputstation = currentStationorder.Key,
                                skui = itemDemand.Key,
                                name = "ziops" + "_" + itemDemand.Key.ID.ToString() + "_" +
                                order.ID.ToString() + "_" + pod.ID.ToString() + "_" + currentStationorder.Key.ID.ToString()
                            };
                            if (availableCounts[itemDemand.Key][pod] >= number)
                            {
                                int numpods = availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).Count();
                                if (numpods > 0 && number > 1)
                                {
                                    Pod pod1 = availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).First();
                                    orderDopsPods.Remove(pod1);
                                    dopsPodsUsed.Add(pod1);
                                    if (availableCounts[itemDemand.Key][pod] >= availableCounts[itemDemand.Key][pod1])
                                    {
                                        availableCounts[itemDemand.Key][pod] -= number - numpods;
                                        Instance.ResourceManager._Ziops[currentStationorder.Key].Add(name, number - numpods);
                                        newZiops.Add(name, number - numpods);
                                        number = numpods;
                                    }
                                    else
                                    {
                                        availableCounts[itemDemand.Key][pod] -= 1;
                                        Instance.ResourceManager._Ziops[currentStationorder.Key].Add(name, 1);
                                        newZiops.Add(name, 1);
                                        number = 1;
                                    }
                                }
                                else
                                {
                                    availableCounts[itemDemand.Key][pod] -= number;
                                    newZiops.Add(name, number);
                                    Instance.ResourceManager._Ziops[currentStationorder.Key].Add(name, number);
                                    number = 0;
                                }
                                if (availableCounts[itemDemand.Key][pod] == 0)
                                    availableCounts[itemDemand.Key].Remove(pod);
                            }
                            else
                            {
                                newZiops.Add(name, availableCounts[itemDemand.Key][pod]);
                                Instance.ResourceManager._Ziops[currentStationorder.Key].Add(name, availableCounts[itemDemand.Key][pod]);
                                number -= availableCounts[itemDemand.Key][pod];
                                availableCounts[itemDemand.Key].Remove(pod);
                            }
                        }
                    }
                }
                HashSet<Pod> unusedDopsPods = new HashSet<Pod>(dopsPodsSelected.Where(v => !dopsPodsUsed.Contains(v)));
                if (unusedDopsPods.Count > 0)
                {
                    foreach (var pod in unusedDopsPods)
                    {
                        Symbol name2 = isYrp.Where(v => v.pod.ID == pod.ID).First();
                        isYrp.Remove(name2);
                        Instance.ResourceManager.BottoPod.Remove(name2.robot);
                        Instance.ResourceManager.ReleasePod(name2.pod);
                        foreach (var xps in isXps.Where(v => v.pod.ID == name2.pod.ID))
                            xps.outputstation.UnregisterInboundPod(name2.pod);
                        Symbol name1 = isXps.Where(v => v.pod.ID == pod.ID).First();
                        isXps.Remove(name1);
                    }
                }
            }
        }
    }
}
