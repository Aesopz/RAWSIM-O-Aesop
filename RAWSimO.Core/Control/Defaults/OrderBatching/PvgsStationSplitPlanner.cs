using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure (solver-free) per-order planner for PVGS. Works on station-aggregated
    /// availability (index-aligned lists, the same shape SplitMilpDecoder uses), so it is
    /// fully unit-testable without Instance/Gurobi. PlanCompletion answers the M1e-style
    /// question "can this order's residual be fully covered this epoch, and how do we split
    /// it across stations" (fewest-stations greedy, single station preferred); PlanPartial
    /// answers the M2e-style question "which single station can make the most partial
    /// progress" (subject to the anti-fragmentation minimum).
    /// </summary>
    public static class PvgsStationSplitPlanner
    {
        /// <summary>
        /// Plans a FULL coverage of the residual across stations with a free slot.
        /// Returns per-station parts (station index, per-SKU quantities) or null if the
        /// residual cannot be fully covered. Single-station coverage is preferred; the
        /// multi-station fallback greedily accumulates stations by how much of the residual
        /// they cover (a heuristic - not guaranteed fewest stations, documented).
        /// </summary>
        public static List<KeyValuePair<int, Dictionary<ItemDescription, int>>> PlanCompletion(
            IList<KeyValuePair<ItemDescription, int>> residual,
            IList<Dictionary<ItemDescription, int>> perStationAvail,
            IList<bool> stationHasFreeSlot)
        {
            // Pass 1: any single free-slot station covering everything?
            for (int s = 0; s < perStationAvail.Count; s++)
            {
                if (!stationHasFreeSlot[s])
                    continue;
                bool covers = true;
                foreach (var pos in residual)
                {
                    int avail;
                    if (!perStationAvail[s].TryGetValue(pos.Key, out avail) || avail < pos.Value)
                    { covers = false; break; }
                }
                if (covers)
                {
                    var part = new Dictionary<ItemDescription, int>();
                    foreach (var pos in residual)
                        part[pos.Key] = pos.Value;
                    return new List<KeyValuePair<int, Dictionary<ItemDescription, int>>>
                    { new KeyValuePair<int, Dictionary<ItemDescription, int>>(s, part) };
                }
            }
            // Pass 2: greedy multi-station accumulation by descending contribution.
            var candidateOrder = Enumerable.Range(0, perStationAvail.Count)
                .Where(s => stationHasFreeSlot[s])
                .OrderByDescending(s => residual.Sum(pos =>
                {
                    int a;
                    return perStationAvail[s].TryGetValue(pos.Key, out a) ? Math.Min(a, pos.Value) : 0;
                }))
                .ToList();
            var remaining = new Dictionary<ItemDescription, int>();
            foreach (var pos in residual)
                remaining[pos.Key] = pos.Value;
            var chosen = new List<int>();
            foreach (var s in candidateOrder)
            {
                bool contributes = false;
                foreach (var kv in remaining)
                {
                    int a;
                    if (kv.Value > 0 && perStationAvail[s].TryGetValue(kv.Key, out a) && a > 0)
                    { contributes = true; break; }
                }
                if (!contributes)
                    continue;
                chosen.Add(s);
                foreach (var key in remaining.Keys.ToList())
                {
                    int a;
                    if (perStationAvail[s].TryGetValue(key, out a) && a > 0)
                        remaining[key] = Math.Max(0, remaining[key] - a);
                }
                if (remaining.Values.All(v => v == 0))
                    break;
            }
            if (remaining.Values.Any(v => v > 0))
                return null;
            // Assemble parts: per SKU, take greedily from the chosen stations in chosen order.
            var parts = chosen.ToDictionary(s => s, s => new Dictionary<ItemDescription, int>());
            foreach (var pos in residual)
            {
                int need = pos.Value;
                foreach (var s in chosen)
                {
                    if (need == 0)
                        break;
                    int a;
                    if (!perStationAvail[s].TryGetValue(pos.Key, out a) || a <= 0)
                        continue;
                    int take = Math.Min(a, need);
                    parts[s][pos.Key] = take;
                    need -= take;
                }
            }
            return chosen.Where(s => parts[s].Count > 0)
                .OrderBy(s => s)
                .Select(s => new KeyValuePair<int, Dictionary<ItemDescription, int>>(s, parts[s]))
                .ToList();
        }

        /// <summary>
        /// Plans the best SINGLE-station partial part (M2e): maximize served units, capped by
        /// per-SKU availability and residual; parts below minUnits are rejected
        /// (anti-fragmentation theta). Returns the quantity dict and station index, or
        /// null / -1 when no station qualifies.
        /// </summary>
        public static Dictionary<ItemDescription, int> PlanPartial(
            IList<KeyValuePair<ItemDescription, int>> residual,
            IList<Dictionary<ItemDescription, int>> perStationAvail,
            IList<bool> stationHasFreeSlot,
            int minUnits,
            out int stationIndex)
        {
            stationIndex = -1;
            Dictionary<ItemDescription, int> best = null;
            int bestUnits = 0;
            for (int s = 0; s < perStationAvail.Count; s++)
            {
                if (!stationHasFreeSlot[s])
                    continue;
                var part = new Dictionary<ItemDescription, int>();
                int units = 0;
                foreach (var pos in residual)
                {
                    int a;
                    if (!perStationAvail[s].TryGetValue(pos.Key, out a) || a <= 0)
                        continue;
                    int take = Math.Min(a, pos.Value);
                    if (take > 0)
                    { part[pos.Key] = take; units += take; }
                }
                if (units >= minUnits && units > bestUnits)
                { bestUnits = units; best = part; stationIndex = s; }
            }
            return best;
        }
    }
}
