using RAWSimO.SolverWrappers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// One order's residual demand as seen by the probe.
    /// </summary>
    public class DualProbeOrder
    {
        public int OrderId;
        /// <summary>skuId -> units still needed.</summary>
        public Dictionary<int, int> Residual = new Dictionary<int, int>();
        public int TotalUnits { get { return Residual.Values.Sum(); } }
    }

    /// <summary>
    /// A decision-epoch snapshot, expressed in plain data so the probe carries no dependency on
    /// the manager, the instance, or any engine type.
    /// </summary>
    public class DualPriceProbeInput
    {
        /// <summary>stationId -> free slots.</summary>
        public Dictionary<int, int> SlotsByStation = new Dictionary<int, int>();
        public List<DualProbeOrder> Orders = new List<DualProbeOrder>();
        /// <summary>podId -> skuId -> units available on that pod.</summary>
        public Dictionary<int, Dictionary<int, int>> PodStock = new Dictionary<int, Dictionary<int, int>>();
        /// <summary>podId -> stationId -> metres.</summary>
        public Dictionary<int, Dictionary<int, double>> PodStationDistance = new Dictionary<int, Dictionary<int, double>>();
        public int BotCount;
        /// <summary>Metres a completed order is worth, i.e. |w2|. Sets the price scale.</summary>
        public double CompletionValue;
    }

    /// <summary>
    /// Shadow prices read off the relaxed allocation LP. Every price is in metres, the same unit
    /// the objective's distance terms use.
    /// </summary>
    public class DualPriceProbeResult
    {
        public bool Solved;
        public double ObjectiveValue;
        public double SolveSeconds;
        public int VariableCount;
        /// <summary>stationId -> metres per free slot.</summary>
        public Dictionary<int, double> SlotPrice = new Dictionary<int, double>();
        /// <summary>skuId -> metres per unit, aggregated over the pods carrying it (max).</summary>
        public Dictionary<int, double> SkuPriceMax = new Dictionary<int, double>();
        /// <summary>skuId -> metres per unit, aggregated over the pods carrying it (mean).</summary>
        public Dictionary<int, double> SkuPriceMean = new Dictionary<int, double>();
        /// <summary>Metres per available bot.</summary>
        public double BotPrice;
    }

    /// <summary>
    /// Solves the "ideal allocation" relaxation of the current decision epoch and reports its dual
    /// prices. The relaxation drops every artificial restriction the online model carries
    /// (icLGcap, the pipeline floor, force-feed, one-partial-per-round) and asks only: given this
    /// backlog and this pod inventory, what is the best achievable allocation? The duals of its
    /// resource constraints are, by LP duality, the marginal opportunity cost of a slot, of a unit
    /// of a SKU, and of a bot - the quantities the online objective currently approximates with
    /// hand-tuned constants.
    ///
    /// Diagnostic only. It builds its own model, reads the duals, and discards everything; it
    /// never touches the decision it was called alongside.
    /// </summary>
    public static class DualPriceProbe
    {
        private const string SlotPrefix = "SLOT_";
        private const string InvPrefix = "INV_";
        private const string BotName = "BOT";

        public static DualPriceProbeResult Solve(DualPriceProbeInput input, double timeLimitSeconds)
        {
            DualPriceProbeResult result = new DualPriceProbeResult();
            if (input == null || input.SlotsByStation.Count == 0 || input.Orders.Count == 0 || input.PodStock.Count == 0)
                return result;

            List<int> stations = input.SlotsByStation.Keys.OrderBy(v => v).ToList();
            List<int> pods = input.PodStock.Keys.OrderBy(v => v).ToList();

            // Which pods can supply which SKU - the probe only creates q for combinations that
            // physically exist, exactly as PiSKU does in the online model.
            Dictionary<int, List<int>> podsBySku = new Dictionary<int, List<int>>();
            foreach (var pod in input.PodStock)
                foreach (var sku in pod.Value.Where(s => s.Value > 0))
                {
                    List<int> carriers;
                    if (!podsBySku.TryGetValue(sku.Key, out carriers))
                        podsBySku[sku.Key] = carriers = new List<int>();
                    carriers.Add(pod.Key);
                }

            LinearModel model = new LinearModel(SolverType.Gurobi, (string s) => { });
            if (timeLimitSeconds > 0)
                model.SetTimelimit(TimeSpan.FromSeconds(timeLimitSeconds));

            // Everything continuous: duals are undefined for a MIP.
            int maxUnits = Math.Max(1, input.Orders.Max(o => o.Residual.Values.DefaultIfEmpty(0).Max()));
            VariableCollection<string> q = new VariableCollection<string>(
                model, VariableType.Continuous, 0, maxUnits, (string s) => { return s; });
            VariableCollection<string> unit = new VariableCollection<string>(
                model, VariableType.Continuous, 0, 1, (string s) => { return s; });

            // q[o,i,p,s], and the index lists needed to slice it later.
            List<QKey> qKeys = new List<QKey>();
            foreach (var order in input.Orders)
                foreach (var line in order.Residual.Where(l => l.Value > 0 && podsBySku.ContainsKey(l.Key)))
                    foreach (int podId in podsBySku[line.Key])
                        foreach (int stationId in stations)
                            qKeys.Add(new QKey
                            {
                                OrderId = order.OrderId,
                                SkuId = line.Key,
                                PodId = podId,
                                StationId = stationId,
                                Name = "q_" + order.OrderId + "_" + line.Key + "_" + podId + "_" + stationId
                            });

            if (qKeys.Count == 0)
            {
                return result;
            }

            Func<int, int, string> xName = (podId, stationId) => "x_" + podId + "_" + stationId;
            Func<int, int, string> yName = (orderId, stationId) => "y_" + orderId + "_" + stationId;
            Func<int, string> zName = orderId => "z_" + orderId;

            // ── Objective: metres travelled, less the metre-value of what gets completed ──
            List<LinearExpression> objTerms = new List<LinearExpression>();
            foreach (int podId in pods)
                foreach (int stationId in stations)
                {
                    double dist;
                    Dictionary<int, double> byStation;
                    if (!input.PodStationDistance.TryGetValue(podId, out byStation)
                        || !byStation.TryGetValue(stationId, out dist))
                        continue;
                    objTerms.Add(unit[xName(podId, stationId)] * dist);
                }
            foreach (var order in input.Orders)
                objTerms.Add(unit[zName(order.OrderId)] * (-input.CompletionValue));
            model.SetObjective(LinearExpression.Sum(objTerms), OptimizationSense.Minimize);

            // ── (SLOT_s) tracked: the marginal value of one free slot at station s ──
            foreach (int stationId in stations)
            {
                var ys = input.Orders.Select(o => unit[yName(o.OrderId, stationId)]).ToList();
                model.AddConstrTracked(LinearExpression.Sum(ys) <= input.SlotsByStation[stationId],
                    SlotPrefix + stationId);
            }

            // ── (BOT) tracked: the marginal value of one more available bot ──
            var allX = new List<LinearExpression>();
            foreach (int podId in pods)
                foreach (int stationId in stations)
                    allX.Add(unit[xName(podId, stationId)]);
            if (allX.Count > 0)
                model.AddConstrTracked(LinearExpression.Sum(allX) <= Math.Max(1, input.BotCount), BotName);

            // ── (INV_p_i) tracked: the marginal value of one unit of SKU i sitting on pod p ──
            // Deliberately NOT paired with a "pod visits at most one station" constraint: the
            // ideal problem lets a pod's inventory serve several stations, which is what makes
            // this a genuine upper bound and keeps the constraint non-redundant with the
            // dispatch link below (a redundant copy would split the dual arbitrarily).
            foreach (var pod in input.PodStock)
                foreach (var sku in pod.Value.Where(s => s.Value > 0))
                {
                    var slice = qKeys.Where(k => k.PodId == pod.Key && k.SkuId == sku.Key)
                        .Select(k => q[k.Name]).ToList();
                    if (slice.Count == 0)
                        continue;
                    model.AddConstrTracked(LinearExpression.Sum(slice) <= sku.Value,
                        InvPrefix + pod.Key + "_" + sku.Key);
                }

            // ── (DISP_p_s) drawing from a pod at a station requires dispatching it there ──
            foreach (int podId in pods)
            {
                int podCapacity = Math.Max(1, input.PodStock[podId].Values.Sum());
                foreach (int stationId in stations)
                {
                    var slice = qKeys.Where(k => k.PodId == podId && k.StationId == stationId)
                        .Select(k => q[k.Name]).ToList();
                    if (slice.Count == 0)
                        continue;
                    model.AddConstr(LinearExpression.Sum(slice)
                        <= unit[xName(podId, stationId)] * podCapacity, "DISP");
                }
            }

            // ── (SLOTY_o_s) drawing any unit of an order at a station occupies a slot there ──
            foreach (var order in input.Orders)
            {
                int orderUnits = Math.Max(1, order.TotalUnits);
                foreach (int stationId in stations)
                {
                    var slice = qKeys.Where(k => k.OrderId == order.OrderId && k.StationId == stationId)
                        .Select(k => q[k.Name]).ToList();
                    if (slice.Count == 0)
                        continue;
                    model.AddConstr(LinearExpression.Sum(slice)
                        <= unit[yName(order.OrderId, stationId)] * orderUnits, "SLOTY");
                }
            }

            // ── (DEM_o_i) never draw more than the order still needs ──
            foreach (var order in input.Orders)
                foreach (var line in order.Residual.Where(l => l.Value > 0))
                {
                    var slice = qKeys.Where(k => k.OrderId == order.OrderId && k.SkuId == line.Key)
                        .Select(k => q[k.Name]).ToList();
                    if (slice.Count == 0)
                        continue;
                    model.AddConstr(LinearExpression.Sum(slice) <= line.Value, "DEM");
                }

            // ── (COMP_o) an order counts as complete only once every unit of it is served ──
            foreach (var order in input.Orders)
            {
                var slice = qKeys.Where(k => k.OrderId == order.OrderId).Select(k => q[k.Name]).ToList();
                if (slice.Count == 0)
                    continue;
                model.AddConstr(unit[zName(order.OrderId)] * order.TotalUnits
                    <= LinearExpression.Sum(slice), "COMP");
            }

            Stopwatch watch = Stopwatch.StartNew();
            model.Optimize();
            watch.Stop();

            result.SolveSeconds = watch.Elapsed.TotalSeconds;
            result.VariableCount = qKeys.Count;
            if (!model.HasSolution())
                return result;

            result.Solved = true;
            result.ObjectiveValue = model.GetObjectiveValue();

            // Duals carry the sign convention of a <= constraint under Minimize, i.e. relaxing the
            // right-hand side by one unit changes the objective by Pi. Tightening a resource makes
            // the objective worse, so a scarce resource reports a negative Pi; negate so every
            // reported price is "metres gained per extra unit of this resource", positive = scarce.
            Dictionary<int, List<double>> skuPrices = new Dictionary<int, List<double>>();
            foreach (var dual in model.GetDuals())
            {
                double price = -dual.Value;
                if (dual.Key == BotName)
                {
                    result.BotPrice = price;
                }
                else if (dual.Key.StartsWith(SlotPrefix, StringComparison.Ordinal))
                {
                    result.SlotPrice[int.Parse(dual.Key.Substring(SlotPrefix.Length))] = price;
                }
                else if (dual.Key.StartsWith(InvPrefix, StringComparison.Ordinal))
                {
                    string[] parts = dual.Key.Substring(InvPrefix.Length).Split('_');
                    if (parts.Length != 2)
                        continue;
                    int skuId = int.Parse(parts[1]);
                    List<double> bucket;
                    if (!skuPrices.TryGetValue(skuId, out bucket))
                        skuPrices[skuId] = bucket = new List<double>();
                    bucket.Add(price);
                }
            }
            foreach (var sku in skuPrices)
            {
                result.SkuPriceMax[sku.Key] = sku.Value.Max();
                result.SkuPriceMean[sku.Key] = sku.Value.Average();
            }
            return result;
        }

        private class QKey
        {
            public int OrderId;
            public int SkuId;
            public int PodId;
            public int StationId;
            public string Name;
        }
    }
}
