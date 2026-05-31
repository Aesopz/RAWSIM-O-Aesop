using RAWSimO.Core.Bots;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.Core.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// PURE-OBSERVATION probe for the "deferred binding + opportunistic backfill" idea. Changes
    /// NO decision and touches no engine state. Gated by SettingConfiguration.BackfillProbeEnabled.
    ///
    /// EVENT-DRIVEN: invoked from BotPutItems at the exact instant a pod has finished serving all
    /// of its assigned extract requests at an output station and is about to be released (the bot
    /// is about to leave and return the pod). This is the precise "release moment" the user asked
    /// about — a transient that a per-tick scan misses entirely.
    ///
    /// At that instant we snapshot and scan the backlog: is there ANY order this pod could
    /// single-handedly complete from its REMAINING inventory? If so, the pod could be RETAINED to
    /// keep picking (complete that backlog order into the freed slot) and buy time for a late
    /// inbound pod instead of leaving and letting the station starve.
    ///
    /// Output: backfill_probe.csv (written at finish from Instance.StatBackfillProbeRows).
    /// </summary>
    public static class BackfillProbe
    {
        /// <summary>Called at the pod-release moment (pod's assigned picks exhausted, bot about to
        /// leave the station). Logs the backfill potential of the departing pod against the backlog.</summary>
        public static void OnExtractRelease(BotNormal bot, ExtractTask task, double currentTime)
        {
            if (bot == null || task == null) return;
            var instance = bot.Instance;
            if (instance?.SettingConfig == null || !instance.SettingConfig.BackfillProbeEnabled) return;
            var station = task.OutputStation;
            var pod = bot.Pod;
            if (station == null || pod == null) return;
            var orderManager = instance.Controller?.OrderManager;
            if (orderManager == null) return;

            // Context: other pods physically present at the station (excluding the departing one),
            // inbound pods en route (one of which may be the late pod we'd stall for), free slot.
            int otherPresentPods = 0;
            foreach (var t in station.GetActiveExtractTasks())
            {
                var b2 = t?.Bot as BotNormal;
                if (b2 == null || object.ReferenceEquals(b2, bot) || b2.Pod == null) continue;
                if (b2.CurrentWaypoint == station.Waypoint || b2.IsQueueing)
                    otherPresentPods++;
            }
            int freeSlot = (station.CapacityInUse < station.Capacity) ? 1 : 0;

            // Does the departing pod still match any of the station's assigned open demand?
            // (Should be ~0 — its task is exhausted — but other assigned orders may share items.)
            var assignedDemand = BuildAssignedOpenDemand(station);
            int assignedMatch = MatchUnits(pod, assignedDemand);

            // Snapshot: scan backlog, match each order against the pod's REMAINING inventory.
            int backlogSize = 0;
            int fullMatch = 0;       // backlog orders the pod can single-handedly COMPLETE
            int partialMatch = 0;    // >=1 useful pick but not complete
            int maxUnits = 0;
            int bestFullDemand = 0;

            var backlog = orderManager.BacklogSnapshot;
            if (backlog != null)
            {
                foreach (var order in backlog)
                {
                    if (order == null || order.Positions == null) continue;
                    backlogSize++;
                    int demand = 0;
                    int units = 0;
                    foreach (var line in order.Positions)
                    {
                        demand += line.Value;
                        units += Math.Min(pod.CountAvailable(line.Key), line.Value);
                    }
                    if (demand <= 0) continue;
                    if (units > maxUnits) maxUnits = units;
                    if (units >= demand)
                    {
                        fullMatch++;
                        if (demand > bestFullDemand) bestFullDemand = demand;
                    }
                    else if (units > 0)
                    {
                        partialMatch++;
                    }
                }
            }

            int podRemainingUnits = pod.ItemDescriptionsContained.Sum(it => pod.CountAvailable(it));

            instance.StatBackfillProbeRows.Add(string.Join(";", new[]
            {
                currentTime.ToString(IOConstants.FORMATTER),
                station.ID.ToString(),
                station.CapacityInUse.ToString(),
                station.Capacity.ToString(),
                freeSlot.ToString(),
                otherPresentPods.ToString(),
                station.GetInfoInboundPods().ToString(),
                pod.ID.ToString(),
                podRemainingUnits.ToString(),
                assignedMatch.ToString(),
                backlogSize.ToString(),
                fullMatch.ToString(),
                partialMatch.ToString(),
                maxUnits.ToString(),
                bestFullDemand.ToString()
            }));
        }

        /// <summary>Open (unfinished) per-item demand of the station's currently assigned orders.</summary>
        private static Dictionary<ItemDescription, int> BuildAssignedOpenDemand(OutputStation station)
        {
            var demand = new Dictionary<ItemDescription, int>();
            var resourceManager = station?.Instance?.ResourceManager;
            if (resourceManager == null) return demand;
            foreach (var request in resourceManager.GetExtractRequestsOfStation(station))
            {
                if (request == null || request.State != RequestState.Unfinished) continue;
                if (demand.ContainsKey(request.Item)) demand[request.Item]++;
                else demand[request.Item] = 1;
            }
            return demand;
        }

        /// <summary>Total units of demand the pod can currently satisfy: Σ min(available, demand).</summary>
        private static int MatchUnits(Pod pod, Dictionary<ItemDescription, int> demand)
        {
            if (pod == null || demand == null || demand.Count == 0) return 0;
            int total = 0;
            foreach (var kv in demand)
                total += Math.Min(pod.CountAvailable(kv.Key), kv.Value);
            return total;
        }
    }
}
