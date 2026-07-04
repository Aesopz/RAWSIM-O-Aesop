using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.SolverWrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using static RAWSimO.Core.Management.ResourceManager;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// MILP-based order-splitting manager: M1G with the one-station-per-order constraint (shi2)
    /// relaxed to a unit-level quantity assignment q[o,i,s]. Reuses the Spec 1 enabler pipeline
    /// (CreateSplitChild / TransferExtractRequests / consolidation) to commit the solution.
    /// The original M1GManager stays untouched and serves as the M0 ablation baseline.
    /// See docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md.
    /// </summary>
    public class SplitM1GManager : M1GManager
    {
        /// <summary>
        /// Creates a new instance of this manager.
        /// </summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public SplitM1GManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GConfiguration;
        }

        /// <summary>
        /// The split-specific config of this controller.
        /// </summary>
        private SplitM1GConfiguration _splitConfig;

        /// <summary>
        /// order 進入 Od 的截止時間（鏡射 base 的 private DueTimeOrderofMP）。
        /// </summary>
        private static readonly double _dueTimeOrderofMP = TimeSpan.FromMinutes(30).TotalSeconds;

        /// <summary>
        /// Residual-demand version of GenerateOiSKU: indexes pending orders by the SKUs
        /// they still need (RemainingPositions), not their full demand.
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
        /// Residual-demand version of GenerateOd: urgency set based on RemainingPositions,
        /// with a ContainsKey guard (M2 orders may have residual SKUs not coverable this epoch).
        /// Also refreshes Timestay/sequence for all pending orders (base parity).
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

        // ── Starve-aware cost wrappers (mirror of the base's private block; the base members are
        //    private and the original file must stay untouched) ──
        private bool _saEnabledSplit;
        private double _saNominalSpeedSplit;
        private double _saFixedParamSplit;
        private Dictionary<int, double> _saEstByStationSplit = new Dictionary<int, double>();
        private Dictionary<int, double> _saRepBotPodTimeSplit = new Dictionary<int, double>();

        /// <summary>Per-solve precompute of starve-aware inputs; no-op when the feature is disabled.</summary>
        private void PrepareStarveAwareSplit(IEnumerable<Pod> pods, Dictionary<OutputStation, int> Cs, HashSet<Bot> Ra)
        {
            _saEnabledSplit = Instance != null && Instance.SettingConfig != null && Instance.SettingConfig.StarveAwareCostEnabled;
            if (!_saEnabledSplit)
                return;
            _saFixedParamSplit = Instance.SettingConfig.StarveAwareFixedParam;
            double cfgSpeed = Instance.SettingConfig.StarveAwareNominalSpeed;
            _saNominalSpeedSplit = cfgSpeed > 0.0
                ? cfgSpeed
                : (Instance.Bots != null && Instance.Bots.Count > 0
                    ? Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity))
                    : 1.0);
            double now = Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;
            _saEstByStationSplit = new Dictionary<int, double>();
            foreach (var s in Cs.Keys)
                _saEstByStationSplit[s.ID] = StarveAwareCost.Est(s, now);
            _saRepBotPodTimeSplit = new Dictionary<int, double>();
            foreach (var p in pods)
            {
                double best = double.PositiveInfinity;
                foreach (var r in Ra)
                    best = Math.Min(best, StarveAwareCost.TravelTime(EstimateBotPodDistance(r, p), _saNominalSpeedSplit));
                _saRepBotPodTimeSplit[p.ID] = best;
            }
        }

        /// <summary>bot->pod objective coefficient: travel time when starve-aware, else distance.</summary>
        private double SplitBotPodCost(Bot robot, Pod pod)
        {
            double d = EstimateBotPodDistance(robot, pod);
            return _saEnabledSplit ? StarveAwareCost.TravelTime(d, _saNominalSpeedSplit) : d;
        }

        /// <summary>pod->station objective coefficient: travel time + starvation delay penalty when starve-aware, else distance.</summary>
        private double SplitPodStationCost(Pod pod, OutputStation station)
        {
            double d = EstimatePodStationDistance(pod, station);
            if (!_saEnabledSplit)
                return d;
            double podStationTime = StarveAwareCost.TravelTime(d, _saNominalSpeedSplit);
            double repBotPod = (_saRepBotPodTimeSplit.TryGetValue(pod.ID, out var t) && !double.IsPositiveInfinity(t)) ? t : 0.0;
            double taCost = podStationTime + repBotPod;
            double est = _saEstByStationSplit.TryGetValue(station.ID, out var e) ? e : double.PositiveInfinity;
            double penalty = StarveAwareCost.DelayPenalty(taCost, est, _saFixedParamSplit);
            return podStationTime + penalty;
        }

        /// <summary>
        /// Split version of Initialize: snapshots the decision inputs with residual demand
        /// (RemainingPositions / GetRemainingDemand) instead of full order demand, and appends
        /// the q / y / z variable name lists (keys 7 / 8 / 9) to the base variable names.
        /// </summary>
        private HashSet<Pod> InitializeSplit(out Dictionary<ItemDescription, List<Pod>> PiSKU, out Dictionary<ItemDescription, List<Order>> OiSKU,
            out Dictionary<int, List<Symbol>> variableNames, out Dictionary<OutputStation, int> Cs, out HashSet<Order> pendingOrders,
            out Dictionary<OutputStation, HashSet<Pod>> inboundPods, out HashSet<Bot> Ra, out HashSet<Bot> Rb,
            out HashSet<Bot> R, out HashSet<Pod> Pb, out HashSet<Pod> Pa, out Dictionary<Pod, Bot> PodToBot,
            out Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            // SPLIT: first stock filter on residual demand; M1 = all residual SKUs coverable,
            //        M2 = at least one residual unit in actual stock
            HashSet<Order> pendingOrders1 = _splitConfig != null && _splitConfig.CrossTime
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
                        // Exclude it from the model for this decision. (base parity)
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
            // SPLIT: second (PiSKU) filter on residual demand, M1 all / M2 any
            if (_splitConfig != null && _splitConfig.CrossTime)
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.Any(p => IsAvailabletoPiSKU(p.Key) >= 1)));
            else
                pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.RemainingPositions.All(p => IsAvailabletoPiSKU(p.Key) >= p.Value)));
            HashSet<Order> Od = GenerateOdSplit(pendingOrders, PiSKU);
            if (Od.Count > Cs.Values.Sum())
                pendingOrders = new HashSet<Order>(Od);
            OiSKU = GenerateOiSKUSplit(pendingOrders);
            // SPLIT: residual snapshot for this solve (decode consistency)
            residuals = pendingOrders.ToDictionary(o => o, o => o.RemainingPositions.ToDictionary(p => p.Key, p => p.Value));
            variableNames = CreatedeVarName(PiSKU, OiSKU, allPods, pendingOrders, Cs, R, Pa1, out Pa);
            // SPLIT: append q (7) / y (8) / z (9) variable names.
            // q only for residual SKUs coverable by the model pods (PiSKU) - non-coverable residual
            // SKUs simply stay in the backlog this epoch (M2); M1 orders are pre-filtered to full coverage.
            var piSkuCopy = PiSKU;  // Capture out parameter for use in lambda
            List<Symbol> deVarNameq = new List<Symbol>();
            List<Symbol> deVarNamey = new List<Symbol>();
            List<Symbol> deVarNamez = new List<Symbol>();
            foreach (var order in pendingOrders)
            {
                foreach (var station in Cs.Keys)
                {
                    deVarNamey.Add(new Symbol { order = order, outputstation = station, name = "ysp" + "_" + order.ID.ToString() + "_" + station.ID.ToString() });
                    foreach (var sku in residuals[order].Where(p => piSkuCopy.ContainsKey(p.Key)))
                        deVarNameq.Add(new Symbol { order = order, outputstation = station, skui = sku.Key, name = "q" + "_" + sku.Key.ID.ToString() + "_" + order.ID.ToString() + "_" + station.ID.ToString() });
                }
                if (_splitConfig == null || !_splitConfig.CrossTime)
                    deVarNamez.Add(new Symbol { order = order, name = "zfull" + "_" + order.ID.ToString() });
            }
            variableNames.Add(7, deVarNameq);
            variableNames.Add(8, deVarNamey);
            variableNames.Add(9, deVarNamez);
            return allPods;
        }

        /// <summary>
        /// The committed outcome of one SplitM1G solve, consumed by DecideAboutPendingOrders.
        /// </summary>
        private class SplitSolveResult
        {
            public Dictionary<Symbol, int> NewZiops = new Dictionary<Symbol, int>();
            public List<Symbol> Allocations = new List<Symbol>();
            public HashSet<Order> SplitParents = new HashSet<Order>();
        }

        // ── Per-decision summary logger (diagnostic; split analog of m1g_decision_log.csv) ──
        private System.IO.StreamWriter _splitDecisionLog;
        private int _splitDecisionIndex = 0;
        private void WriteSplitDecisionLog(bool solved, double time, int pendingOrdersN, int stationsWithCap, int podsInModel,
            int nXps, int nChildren, int nFastPath, int unitsAssigned, double sumUs, double objective, double optSec)
        {
            if (_splitDecisionLog == null)
            {
                string dir = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _splitDecisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "splitm1g_decision_log.csv"), false) { AutoFlush = true };
                _splitDecisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsInModel,xps,children,fastPath,units,sumUs,objective,solveSec");
            }
            _splitDecisionLog.WriteLine(string.Join(",", new string[] {
                _splitDecisionIndex.ToString(),
                time.ToString(System.Globalization.CultureInfo.InvariantCulture),
                solved ? "1" : "0",
                pendingOrdersN.ToString(),
                stationsWithCap.ToString(),
                podsInModel.ToString(),
                nXps.ToString(),
                nChildren.ToString(),
                nFastPath.ToString(),
                unitsAssigned.ToString(),
                sumUs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                objective.ToString(System.Globalization.CultureInfo.InvariantCulture),
                optSec.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
            _splitDecisionIndex++;
        }

        /// <summary>
        /// Builds and solves the SplitM1G MILP (shi2 relaxed to q[o,i,s]) and commits the solution:
        /// robot-pod claiming (base parity), child creation via SplitMilpDecoder + the Spec 1 enabler
        /// pipeline, greedy Ziops extraction per station, and release of unused newly-selected pods.
        /// </summary>
        private SplitSolveResult SolveSplit(SolverType type, Dictionary<ItemDescription, List<Pod>> PiSKU,
            Dictionary<ItemDescription, List<Order>> OiSKU, IEnumerable<Pod> Pods, Dictionary<OutputStation, int> Cs,
            Dictionary<int, List<Symbol>> variableNames, HashSet<Order> pendingOrders,
            Dictionary<OutputStation, HashSet<Pod>> inboundPods, HashSet<Bot> Ra, HashSet<Bot> Rb, HashSet<Bot> R,
            HashSet<Pod> Pb, HashSet<Pod> Pa, Dictionary<Pod, Bot> PodToBot,
            Dictionary<Order, Dictionary<ItemDescription, int>> residuals)
        {
            LinearModel wrapper = new LinearModel(type, (string s) => { Console.Write(s); });
            SplitSolveResult result = new SplitSolveResult();
            bool crossTime = _splitConfig != null && _splitConfig.CrossTime;
            List<Symbol> deVarNamexps = variableNames[1];
            List<Symbol> deVarNameyrp = variableNames[4];
            List<Symbol> deVarNameus = variableNames[5];
            List<Symbol> deVarNamedops = variableNames[6];
            List<Symbol> deVarNameq = variableNames[7];
            List<Symbol> deVarNamey = variableNames[8];
            // variableNames[9] (z) 只在 M1 約束中以名稱字串引用，毋需區域變數
            double w1 = 1;
            double w2u = _splitConfig != null ? _splitConfig.UnitRewardWeight : -40;
            double w3 = 1000;
            int maxCs = Cs.Count > 0 ? Cs.Values.Max() : 1;
            int maxR = residuals.Count > 0 ? residuals.Values.SelectMany(d => d.Values).DefaultIfEmpty(1).Max() : 1;
            VariableCollection<string> variablesBinary = new VariableCollection<string>(wrapper, VariableType.Binary, 0, 1, (string s) => { return s; });
            VariableCollection<string> variablesUs = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxCs, (string s) => { return s; });
            VariableCollection<string> variablesQ = new VariableCollection<string>(wrapper, VariableType.Integer, 0, maxR, (string s) => { return s; });
            PrepareStarveAwareSplit(Pods, Cs, Ra);
            PrepareDecisionExtras(Pods, Cs, Ra);
            // 目標：w1·(pod->station + bot->pod) + w2'·Σq + w3·Σus（Ra 空時省略 yrp 項，base parity）
            if (Ra.Count() > 0)
                wrapper.SetObjective((LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (SplitPodStationCost(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation))), wrapper)
                    + LinearExpression.Sum(deVarNameyrp.Where(u => Ra.Contains(u.robot) && Instance.ResourceManager.UnusedPods.Contains(u.pod) && u.pod.Waypoint != null).Select(v => variablesBinary[v.name] *
                    SplitBotPodCost(v.robot, v.pod)), wrapper)) * w1
                    + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name])) * w2u
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesUs[v.name])) * w3, OptimizationSense.Minimize);
            else
                wrapper.SetObjective(LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * (SplitPodStationCost(v.pod, v.outputstation) + PodStationExtraCost(v.pod, v.outputstation))), wrapper) * w1
                    + LinearExpression.Sum(deVarNameq.Select(v => variablesQ[v.name])) * w2u
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesUs[v.name])) * w3, OptimizationSense.Minimize);
            // (link-up) q ≤ r·y ；(link-down) y ≤ Σi q
            foreach (var q in deVarNameq)
                wrapper.AddConstr(variablesQ[q.name] <= residuals[q.order][q.skui] * variablesBinary["ysp" + "_" + q.order.ID.ToString() + "_" + q.outputstation.ID.ToString()], "slink1");
            foreach (var y in deVarNamey)
                wrapper.AddConstr(variablesBinary[y.name] <= LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == y.order.ID && v.outputstation.ID == y.outputstation.ID).Select(v => variablesQ[v.name])), "slink2");
            // (shi4') 純 slot 制：Σo y = Cs − us
            foreach (var station in Cs.Keys)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamey.Where(v => v.outputstation.ID == station.ID).Select(v => variablesBinary[v.name])) == Cs[station] - variablesUs["us" + "_" + station.ID.ToString()], "shi4");
            // (shi5') 庫存可行性：Σo q ≤ Σp stock·xps
            foreach (var sku in OiSKU.Where(v => PiSKU.ContainsKey(v.Key)))
            {
                foreach (var station in Cs.Keys)
                {
                    wrapper.AddConstr(LinearExpression.Sum(deVarNameq.Where(v => v.outputstation.ID == station.ID && v.skui == sku.Key).Select(v => variablesQ[v.name]))
                        <= LinearExpression.Sum(deVarNamexps.Where(v => v.outputstation.ID == station.ID && PiSKU[sku.Key].Contains(v.pod)).Select(v =>
                        v.pod.CountAvailable(sku.Key) * variablesBinary[v.name])), "shi5");
                }
            }
            // (M1) Σs q = r·z ／ (M2) Σs q ≤ r
            foreach (var order in pendingOrders)
            {
                foreach (var sku in residuals[order].Where(p => PiSKU.ContainsKey(p.Key)))
                {
                    var lhs = LinearExpression.Sum(deVarNameq.Where(v => v.order.ID == order.ID && v.skui == sku.Key).Select(v => variablesQ[v.name]));
                    if (crossTime)
                        wrapper.AddConstr(lhs <= sku.Value, "sm2");
                    else
                        wrapper.AddConstr(lhs == sku.Value * variablesBinary["zfull" + "_" + order.ID.ToString()], "sm1");
                }
            }
            // shi6~shi13（base 逐字保留；shi12 的 yaos → ysp）
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "shi6");
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
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])), "shi8");
            foreach (var pod in Pods)
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "shi9");
            foreach (var robot in R)
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.robot.ID == robot.ID).Select(v => variablesBinary[v.name])) <= 1, "shi10");
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
                                <= variablesBinary["ysp" + "_" + order.ID.ToString() + "_" + station.ID.ToString()] + variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()], "shi12");
                        }
                    }
                }
            }
            foreach (var pod in Pa)
            {
                foreach (var station in Cs.Keys)
                    wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()]
                        <= LinearExpression.Sum(deVarNamedops.Where(v => v.pod.ID == pod.ID && v.outputstation.ID == station.ID).Select(v => variablesBinary[v.name])), "shi13");
            }
            wrapper.Update();
            DateTime _optStart = DateTime.Now;
            wrapper.Optimize();
            double _optSec = (DateTime.Now - _optStart).TotalSeconds;

            if (wrapper.HasSolution())
            {
                // 1) 選中變數抽取（明確逐清單，不用 base 的 1..N dict 迴圈——keys 2/3 未建變數）
                List<Symbol> IsdeVarNamexps = deVarNamexps.Where(v => Math.Round(variablesBinary[v.name].GetValue()) != 0).ToList();
                List<Symbol> IsdeVarNamedops = deVarNamedops.Where(v => Math.Round(variablesBinary[v.name].GetValue()) != 0).ToList();
                // 2) robot-pod claiming（base i==4 區塊逐字）
                List<Symbol> IsdeVarNameyrp = new List<Symbol>();
                foreach (var itemName in deVarNameyrp)
                {
                    if (Math.Round(variablesBinary[itemName.name].GetValue()) != 0 && Ra.Contains(itemName.robot))
                    {
                        IsdeVarNameyrp.Add(itemName);
                        Instance.ResourceManager.BottoPod.Add(itemName.robot, itemName.pod);
                        Instance.ResourceManager.ClaimPod(itemName.pod, itemName.robot, BotTaskType.Extract);
                        foreach (var xps in IsdeVarNamexps.Where(v => v.pod.ID == itemName.pod.ID))
                            xps.outputstation.RegisterInboundPod(itemName.pod);
                    }
                }
                // 3) 解碼 → children / 快路徑（站序固定以求 determinism）
                List<OutputStation> stationList = Cs.Keys.OrderBy(s => s.ID).ToList();
                Dictionary<OutputStation, List<Order>> _availableStationorder = new Dictionary<OutputStation, List<Order>>();
                // Ziops 的 dops 對照需回到「模型層 order」（child 的 dops 掛在母單 ID 上）
                Dictionary<Order, Order> modelOrderOf = new Dictionary<Order, Order>();
                int nChildren = 0, nFastPath = 0, unitsAssigned = 0;
                foreach (var order in pendingOrders.OrderBy(o => o.ID))
                {
                    List<Dictionary<ItemDescription, int>> perStation = new List<Dictionary<ItemDescription, int>>();
                    foreach (var station in stationList)
                    {
                        Dictionary<ItemDescription, int> quantities = new Dictionary<ItemDescription, int>();
                        foreach (var q in deVarNameq.Where(v => v.order.ID == order.ID && v.outputstation.ID == station.ID))
                        {
                            int units = (int)Math.Round(variablesQ[q.name].GetValue());
                            if (units > 0)
                                quantities[q.skui] = units;
                        }
                        perStation.Add(quantities);
                    }
                    bool fullyAssigned;
                    List<KeyValuePair<int, Dictionary<ItemDescription, int>>> parts =
                        SplitMilpDecoder.Decode(residuals[order].ToList(), perStation, crossTime, out fullyAssigned);
                    if (parts.Count == 0)
                        continue;
                    unitsAssigned += parts.Sum(p => p.Value.Values.Sum());
                    if (parts.Count == 1 && fullyAssigned && !order.IsSplitParent)
                    {
                        // 未拆快路徑：單站全數且母單未被拆過 → 直接 allocate 母單（enabler parity）
                        OutputStation station = stationList[parts[0].Key];
                        result.Allocations.Add(new Symbol { order = order, outputstation = station });
                        if (!_availableStationorder.ContainsKey(station))
                            _availableStationorder.Add(station, new List<Order>());
                        _availableStationorder[station].Add(order);
                        modelOrderOf[order] = order;
                        nFastPath++;
                    }
                    else
                    {
                        foreach (var part in parts)
                        {
                            OutputStation station = stationList[part.Key];
                            Order child = Order.CreateSplitChild(order, part.Value);
                            child.ID = idoforder++;
                            Instance.ResourceManager.TransferExtractRequests(order, child);
                            result.Allocations.Add(new Symbol { order = child, outputstation = station });
                            if (!_availableStationorder.ContainsKey(station))
                                _availableStationorder.Add(station, new List<Order>());
                            _availableStationorder[station].Add(child);
                            modelOrderOf[child] = order;
                            nChildren++;
                        }
                        result.SplitParents.Add(order);
                    }
                }
                // 4) 模型外求 Ziops（base 逐字，兩處差異：迭代物件=child/快路徑母單；
                //    dops 對照改用 modelOrderOf + 站別過濾，否則 child 新 ID 對不上母單的 dops）
                DateTime A = DateTime.Now;
                if (_availableStationorder.Count > 0)
                {
                    foreach (var _currentStationorder in _availableStationorder)
                    {
                        Dictionary<ItemDescription, Dictionary<Pod, int>> _availableCounts = new Dictionary<ItemDescription, Dictionary<Pod, int>>();
                        foreach (var itemName in IsdeVarNamexps.Where(v => v.outputstation.ID == _currentStationorder.Key.ID))
                        {
                            foreach (var item in itemName.pod.ItemDescriptionsContained.Where(v => itemName.pod.CountAvailable(v) > 0))
                            {
                                if (_availableCounts.ContainsKey(item))
                                {
                                    if (_availableCounts[item].ContainsKey(itemName.pod))
                                        _availableCounts[item][itemName.pod] += itemName.pod.CountAvailable(item);
                                    else
                                        _availableCounts[item].Add(itemName.pod, itemName.pod.CountAvailable(item));
                                }
                                else
                                {
                                    Dictionary<Pod, int> Counts = new Dictionary<Pod, int>();
                                    _availableCounts.Add(item, Counts);
                                    _availableCounts[item].Add(itemName.pod, itemName.pod.CountAvailable(item));
                                }
                            }
                        }
                        HashSet<Pod> dopsPodsSelected = new HashSet<Pod>();
                        HashSet<Pod> dopsPodsUsed = new HashSet<Pod>();
                        foreach (var order in _currentStationorder.Value)
                        {
                            Dictionary<ItemDescription, int> itemDemands = new Dictionary<ItemDescription, int>();
                            foreach (var item in order.Positions)
                                itemDemands.Add(item.Key, item.Value);
                            HashSet<Pod> orderDopsPods = new HashSet<Pod>();
                            Order modelOrder = modelOrderOf[order];
                            foreach (var item in IsdeVarNamedops.Where(v => v.order.ID == modelOrder.ID && v.outputstation.ID == _currentStationorder.Key.ID))
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
                                    if (_availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).Count() > 0)
                                    {
                                        pod = _availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).First();
                                        orderDopsPods.Remove(pod);
                                        dopsPodsUsed.Add(pod);
                                    }
                                    else
                                        pod = _availableCounts[itemDemand.Key].Keys.First();
                                    Symbol name = new Symbol
                                    {
                                        pod = pod,
                                        order = order,
                                        outputstation = _currentStationorder.Key,
                                        skui = itemDemand.Key,
                                        name = "ziops" + "_" + itemDemand.Key.ID.ToString() + "_" +
                                        order.ID.ToString() + "_" + pod.ID.ToString() + "_" + _currentStationorder.Key.ID.ToString()
                                    };
                                    if (_availableCounts[itemDemand.Key][pod] >= number)
                                    {
                                        int numpods = _availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).Count();
                                        if (numpods > 0 && number > 1)
                                        {
                                            Pod pod1 = _availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).First();
                                            orderDopsPods.Remove(pod1);
                                            dopsPodsUsed.Add(pod1);
                                            if (_availableCounts[itemDemand.Key][pod] >= _availableCounts[itemDemand.Key][pod1])
                                            {
                                                _availableCounts[itemDemand.Key][pod] -= number - numpods;
                                                Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, number - numpods);
                                                result.NewZiops.Add(name, number - numpods);
                                                number = numpods;
                                            }
                                            else
                                            {
                                                _availableCounts[itemDemand.Key][pod] -= 1;
                                                Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, 1);
                                                result.NewZiops.Add(name, 1);
                                                number = 1;
                                            }
                                        }
                                        else
                                        {
                                            _availableCounts[itemDemand.Key][pod] -= number;
                                            result.NewZiops.Add(name, number);
                                            Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, number);
                                            number = 0;
                                        }
                                        if (_availableCounts[itemDemand.Key][pod] == 0)
                                            _availableCounts[itemDemand.Key].Remove(pod);
                                    }
                                    else
                                    {
                                        result.NewZiops.Add(name, _availableCounts[itemDemand.Key][pod]);
                                        Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, _availableCounts[itemDemand.Key][pod]);
                                        number -= _availableCounts[itemDemand.Key][pod];
                                        _availableCounts[itemDemand.Key].Remove(pod);
                                    }
                                }
                            }
                        }
                        HashSet<Pod> unusedDopsPods = new HashSet<Pod>(dopsPodsSelected.Where(v => !dopsPodsUsed.Contains(v)));
                        if (unusedDopsPods.Count > 0)
                        {
                            foreach (var pod in unusedDopsPods)
                            {
                                Symbol name2 = IsdeVarNameyrp.Where(v => v.pod.ID == pod.ID).FirstOrDefault();
                                if (name2 == null)
                                    continue; // 繼承 pod（shi7/shi11 固定）不在 Ra claiming 清單，不可釋放
                                IsdeVarNameyrp.Remove(name2);
                                Instance.ResourceManager.BottoPod.Remove(name2.robot);
                                Instance.ResourceManager.ReleasePod(name2.pod);
                                foreach (var xps in IsdeVarNamexps.Where(v => v.pod.ID == name2.pod.ID))
                                    xps.outputstation.UnregisterInboundPod(name2.pod);
                                Symbol name1 = IsdeVarNamexps.Where(v => v.pod.ID == pod.ID).First();
                                IsdeVarNamexps.Remove(name1);
                            }
                        }
                    }
                }
                Instance.Observer.TimeOrderBatchingbyziops((DateTime.Now - A).TotalSeconds);
                double sumUs = 0.0;
                foreach (var s in deVarNameus)
                    sumUs += Math.Round(variablesUs[s.name].GetValue());
                WriteSplitDecisionLog(true, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    IsdeVarNamexps.Count, nChildren, nFastPath, unitsAssigned, sumUs, wrapper.GetObjectiveValue(), _optSec);
            }
            else
            {
                WriteSplitDecisionLog(false, Instance.Controller.CurrentTime, pendingOrders.Count, Cs.Count, Pods.Count(),
                    0, 0, 0, 0, 0.0, double.NaN, _optSec);
            }
            return result;
        }
    }
}
