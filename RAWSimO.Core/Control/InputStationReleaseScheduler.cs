using RAWSimO.Core.Bots;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using System;
using System.Collections.Generic;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// Per-input-station slow-start release scheduler (replenishment lower-half only).
    /// Mirrors StationReleaseScheduler for store/InsertTask holders. Reuses the pure
    /// StationReleaseScheduler.Decide and SlowStartController.PipelineNextFreeTime. The input
    /// starvation model is the simplified single-server pipeline of committed store jobs plus
    /// the station's current bundle-transfer block; no demand matching (pod selection stays native).
    /// See docs/superpowers/plans/2026-05-30-input-station-slow-start.md.
    /// </summary>
    public static class InputStationReleaseScheduler
    {
        /// <summary>Per-tick entry for one input station. Collects InsertTask holders, computes
        /// input starvation, runs Decide, writes release deadline onto each holder bot.</summary>
        public static void Schedule(InputStation station, PathManager pathManager, double currentTime, double buffer)
        {
            if (station == null || pathManager == null) return;
            var instance = station.Instance;

            // Collect holders bound to THIS input station.
            var holderBots = new List<BotNormal>();
            foreach (var b in instance.Bots)
            {
                var bn = b as BotNormal;
                if (bn == null || !bn._isSlowStartHolding) continue;
                var task = bn.CurrentTask as InsertTask;
                if (task == null || task.InputStation != station) continue;
                holderBots.Add(bn);
            }
            if (holderBots.Count == 0) return;

            double starve = ComputeInputStarvation(station, pathManager, currentTime);

            var inputs = new List<StationReleaseScheduler.HolderInput>();
            foreach (var bn in holderBots)
            {
                var task = bn.CurrentTask as InsertTask;
                double eta = IdealEta(pathManager, bn, station, currentTime);
                double lift = (bn.Pod == null) ? bn.PodTransferTime : 0.0;
                int committed = (task.Requests != null) ? task.Requests.Count : 0;
                inputs.Add(new StationReleaseScheduler.HolderInput
                {
                    BotId = bn.ID,
                    Lift = lift,
                    Travel = eta,
                    Value = committed,                               // chosen-selection score (committed bundles)
                    Proc = committed * station.ItemBundleTransferTime // store work time
                });
            }

            var result = StationReleaseScheduler.Decide(starve, inputs, buffer);

            foreach (var bn in holderBots)
            {
                double delay = result.HoldDelayByBot.TryGetValue(bn.ID, out var d) ? d : 0.0;
                bn._slowStartReleaseDeadline = currentTime + delay;
                bn._slowStartIsChosen = (bn.ID == result.ChosenBotId);
            }
        }

        /// <summary>Ideal kinematic ETA bot->station with the same fallback chain as the output scheduler.</summary>
        private static double IdealEta(PathManager pm, BotNormal bn, InputStation station, double now)
        {
            double eta = pm.EstimateIdealKinematicEta(bn, bn.CurrentWaypoint, station.Waypoint, now, bn.GetTargetOrientation());
            if (double.IsNaN(eta) || double.IsInfinity(eta))
                eta = SlowStartController.ComputeIdealEta(bn, bn.CurrentWaypoint, station.Waypoint);
            if (double.IsNaN(eta) || double.IsInfinity(eta)) eta = 0.0;
            return eta;
        }

        /// <summary>
        /// Simplified input starvation: single-server pipeline of committed (non-holding) store
        /// jobs targeting this station, started from the station's current bundle-transfer block.
        /// Returns seconds-from-now until the station would run out of store work.
        /// </summary>
        private static double ComputeInputStarvation(InputStation station, PathManager pm, double now)
        {
            double blockedLeft = station.GetInfoBlockedLeft();
            double stationFreeAt = (!double.IsNaN(blockedLeft) && blockedLeft > 0.0) ? now + blockedLeft : now;

            var jobs = new List<(double arrival, double work)>();
            foreach (var b in station.Instance.Bots)
            {
                var bn = b as BotNormal;
                if (bn == null || bn._isSlowStartHolding) continue;   // exclude holders
                var task = bn.CurrentTask as InsertTask;
                if (task == null || task.InputStation != station) continue;
                int items = (task.Requests != null) ? task.Requests.Count : 0;
                if (items <= 0) continue;

                double arrival = (bn.CurrentWaypoint == station.Waypoint || bn.IsQueueing)
                    ? now
                    : now + Math.Max(0.0, IdealEta(pm, bn, station, now));
                jobs.Add((arrival, items * station.ItemBundleTransferTime));
            }

            double t0 = SlowStartController.PipelineNextFreeTime(stationFreeAt, jobs, now);
            return Math.Max(0.0, t0 - now);
        }
    }
}
