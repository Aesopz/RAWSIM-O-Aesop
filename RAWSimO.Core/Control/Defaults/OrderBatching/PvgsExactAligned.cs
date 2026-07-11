using System;
using System.Collections.Generic;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure helpers for PVGS-E (ExactAlignedScoring): the dispatch score aligned term-by-term
    /// to the SplitM1GExact objective, and the spare-slot squeeze planner implementing the
    /// epsilon (UnitDrawReward) secondary layer greedily. Pure static functions - unit tested
    /// in RAWSimO.Tests/PvgsExactAlignedTests.cs. Spec: docs/superpowers/specs/
    /// 2026-07-12-pvgs-e-design.md.
    /// </summary>
    public static class PvgsExactAligned
    {
        /// <summary>
        /// Dispatch score in MAXIMIZATION form, aligned to the exact objective's terms:
        /// completionWeight * newCompletions - unitDrawReward * unitsEnabled
        /// - distanceWeight * (botPodDistance + podStationDistance) - podTripFixedCost.
        /// unitDrawReward is the MINIMIZATION-form epsilon (negative = reward), hence the
        /// minus sign; podTripFixedCost mirrors w4 (positive = cost per new trip).
        /// </summary>
        public static double Score(int newCompletions, int unitsEnabled, double botPodDistance, double podStationDistance,
            double completionWeight, double distanceWeight, double podTripFixedCost, double unitDrawReward)
        {
            return completionWeight * newCompletions
                - unitDrawReward * unitsEnabled
                - distanceWeight * (botPodDistance + podStationDistance)
                - podTripFixedCost;
        }

        /// <summary>
        /// Epsilon squeeze planner: the largest drawable partial part (max units) for one
        /// order across stations with a free slot, using station-side availability only
        /// (already-committed pods - a NEW trip can never pay for itself at epsilon scale).
        /// No MinPartialUnits floor: the exact model prices every drawn unit at epsilon with
        /// no extra gates, so the faithful greedy has none either. Returns null when nothing
        /// is drawable; stationIndex receives the chosen station (or -1).
        /// </summary>
        public static Dictionary<ItemDescription, int> PlanSqueeze(
            List<KeyValuePair<ItemDescription, int>> residual,
            List<Dictionary<ItemDescription, int>> perStationAvail,
            bool[] slotFree, out int stationIndex)
        {
            stationIndex = -1;
            Dictionary<ItemDescription, int> best = null;
            int bestUnits = 0;
            for (int s = 0; s < perStationAvail.Count; s++)
            {
                if (!slotFree[s])
                    continue;
                Dictionary<ItemDescription, int> part = null;
                int units = 0;
                foreach (var pos in residual)
                {
                    int have;
                    if (!perStationAvail[s].TryGetValue(pos.Key, out have) || have <= 0)
                        continue;
                    int take = Math.Min(have, pos.Value);
                    if (take <= 0)
                        continue;
                    if (part == null)
                        part = new Dictionary<ItemDescription, int>();
                    part[pos.Key] = take;
                    units += take;
                }
                if (units > bestUnits)
                {
                    bestUnits = units;
                    best = part;
                    stationIndex = s;
                }
            }
            return best;
        }
    }
}
