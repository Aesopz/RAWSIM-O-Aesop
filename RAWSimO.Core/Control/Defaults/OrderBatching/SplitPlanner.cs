using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure planning logic of the greedy split heuristic: distributes the remaining units of ONE order
    /// over a number of free station slots. No simulation state involved, hence unit-testable.
    /// See docs/superpowers/specs/2026-07-02-order-splitting-consolidation-enabler-design.md.
    /// </summary>
    public static class SplitPlanner
    {
        /// <summary>
        /// Computes the per-child quantities for one order.
        /// </summary>
        /// <param name="remaining">Remaining (stock-capped) demand per SKU.</param>
        /// <param name="freeStationSlots">Number of stations with at least one free order slot.</param>
        /// <param name="crossTime">M2: residual units may stay unassigned for later epochs. M1 (false): all units are assigned.</param>
        /// <param name="maxChildrenPerOrder">Maximal parts per epoch (>= 1).</param>
        /// <param name="maxUnitsPerChild">Unit cap per child (0 = unlimited); only effective when crossTime is enabled.</param>
        /// <returns>One SKU-&gt;quantity map per child (index = station rank); empty list = do not assign this epoch.</returns>
        public static List<Dictionary<ItemDescription, int>> ComputePlan(
            IList<KeyValuePair<ItemDescription, int>> remaining,
            int freeStationSlots, bool crossTime, int maxChildrenPerOrder, int maxUnitsPerChild)
        {
            List<Dictionary<ItemDescription, int>> children = new List<Dictionary<ItemDescription, int>>();
            int totalUnits = remaining.Sum(r => r.Value);
            if (totalUnits <= 0 || freeStationSlots <= 0 || maxChildrenPerOrder <= 0)
                return children;
            int parts = Math.Min(Math.Min(freeStationSlots, maxChildrenPerOrder), totalUnits);
            // Units to assign this epoch (M2 with cap may leave a residual)
            int unitsToAssign = totalUnits;
            if (crossTime && maxUnitsPerChild > 0)
                unitsToAssign = Math.Min(totalUnits, parts * maxUnitsPerChild);
            // Even budgets, larger parts first
            int[] budgets = new int[parts];
            int baseSize = unitsToAssign / parts, rest = unitsToAssign % parts;
            for (int i = 0; i < parts; i++)
                budgets[i] = baseSize + (i < rest ? 1 : 0);
            for (int i = 0; i < parts; i++)
                children.Add(new Dictionary<ItemDescription, int>());
            // Fill children sequentially per SKU (keeps SKU units together as far as budgets allow)
            int childIdx = 0;
            foreach (var position in remaining)
            {
                int left = position.Value;
                while (left > 0 && childIdx < parts)
                {
                    int used = children[childIdx].Values.Sum();
                    int space = budgets[childIdx] - used;
                    if (space <= 0) { childIdx++; continue; }
                    int put = Math.Min(space, left);
                    if (!children[childIdx].ContainsKey(position.Key))
                        children[childIdx][position.Key] = 0;
                    children[childIdx][position.Key] += put;
                    left -= put;
                }
                if (childIdx >= parts)
                    break;
            }
            // Defensive: drop empty children
            return children.Where(c => c.Count > 0).ToList();
        }
    }
}
