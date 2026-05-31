using RAWSimO.Core.Bots;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// Per-input-station slow-start release scheduler (replenishment lower-half only).
    /// Mirrors StationReleaseScheduler for store/InsertTask holders. Reuses the pure
    /// StationReleaseScheduler.Decide and SlowStartController projection helpers. The input
    /// starvation model is the single-server pipeline of committed store jobs plus the station's
    /// current bundle-transfer block; no demand matching (pod selection stays native).
    ///
    /// Like the output scheduler, this applies a WORK-HORIZON CASCADE: after picking the holder
    /// to release, the remaining holders' budgets are recomputed against the full work horizon
    /// that INCLUDES the released pod's in-flight store work — so each release extends the other
    /// holders' allowable hold instead of letting them all release against the (truncated)
    /// first-starvation estimate. Without this, the input EST is systematically under-estimated
    /// for queued holders, releasing pods too early and piling them into the station queue.
    /// See docs/superpowers/plans/2026-05-30-input-station-slow-start.md.
    /// </summary>
    public static class InputStationReleaseScheduler
    {
        /// <summary>Per-tick entry for one input station. Collects InsertTask holders, computes the
        /// work projection, runs Decide, then cascades the work horizon onto the non-chosen
        /// holders so a released pod extends the others' hold budget.</summary>
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

            // Base projection over committed (non-holding) store jobs at this station.
            double stationFreeAt;
            var jobs = BuildInputJobs(station, pathManager, currentTime, out stationFreeAt);
            double starve, workHorizon;
            SlowStartController.PipelineFirstStarveAndHorizon(stationFreeAt, jobs, currentTime, out starve, out workHorizon);

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
            var inputById = inputs.ToDictionary(h => h.BotId, h => h);

            // Work-horizon cascade: when the chosen holder releases, its in-flight store work keeps
            // the station busy past the first-starvation point, so the OTHER holders can extend
            // their hold. Recompute the horizon WITH the chosen pod's job added and budget the
            // non-chosen holders against it (mirrors StationReleaseScheduler.Schedule).
            double cascadeStarve = workHorizon;
            if (inputById.TryGetValue(result.ChosenBotId, out var chosenInput) && chosenInput.Proc > 0.0)
            {
                double chosenArrival = currentTime + chosenInput.Lift + chosenInput.Travel;
                var jobsWithChosen = new List<(double arrival, double work)>(jobs) { (chosenArrival, chosenInput.Proc) };
                double ignoredStarve;
                SlowStartController.PipelineFirstStarveAndHorizon(
                    stationFreeAt, jobsWithChosen, currentTime, out ignoredStarve, out cascadeStarve);
            }

            double chosenDelay = 0.0;
            foreach (var bn in holderBots)
            {
                bn._slowStartIsChosen = (bn.ID == result.ChosenBotId);
                double delay;
                if (bn._slowStartIsChosen)
                {
                    delay = result.HoldDelayByBot.TryGetValue(bn.ID, out var d) ? d : 0.0;
                    chosenDelay = delay;
                }
                else
                {
                    var hi = inputById[bn.ID];
                    delay = Math.Max(0.0, cascadeStarve - hi.Lift - hi.Travel - buffer);
                }
                bn._slowStartReleaseDeadline = currentTime + delay;
            }

            // Diagnostic (gated by BackfillProbeEnabled — off by default): one row per Schedule call
            // with holders. Quantifies whether input ever has >=2 concurrent holders (whether the
            // output-style cascade is relevant) and whether EST/budget collapses (release-now).
            // Written to input_sched_probe.csv at finish.
            if (instance.SettingConfig == null || !instance.SettingConfig.BackfillProbeEnabled)
                return;
            int queued = jobs.Count(j => j.arrival <= currentTime + 1e-6);
            instance.StatInputSchedRows.Add(string.Join(";", new[]
            {
                currentTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                station.ID.ToString(),
                holderBots.Count.ToString(),
                queued.ToString(),
                (jobs.Count - queued).ToString(),
                starve.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                workHorizon.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                cascadeStarve.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                chosenDelay.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                (result.ChosenReleaseNow ? 1 : 0).ToString()
            }));
        }

        /// <summary>Builds the single-server (arrival, work) job list for committed (non-holding)
        /// store tasks targeting this station, plus the station's current free-at time.</summary>
        private static List<(double arrival, double work)> BuildInputJobs(
            InputStation station, PathManager pm, double now, out double stationFreeAt)
        {
            double blockedLeft = station.GetInfoBlockedLeft();
            stationFreeAt = (!double.IsNaN(blockedLeft) && blockedLeft > 0.0) ? now + blockedLeft : now;

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

            // Known-but-unassigned store backlog: bundles the station must still store onto
            // future pods (GetInfoOpenRequests = available store requests NOT yet bound to any
            // InsertTask, so no double-count with the committed jobs above). This is deterministic
            // work the station will do — counting it stops holders from releasing in the
            // committed-work trough and arriving into a queue the backlog refills.
            var setting = station.Instance.SettingConfig;
            if (setting != null && setting.InputBacklogAwareHold)
            {
                int backlog = station.GetInfoOpenRequests();
                if (backlog > 0)
                    jobs.Add((now, backlog * station.ItemBundleTransferTime));
            }
            return jobs;
        }

        /// <summary>ETA bot->station: reservation-aware when SlowStartUseReservationEta is set,
        /// else ideal-kinematic, with the same fallback chain as the output scheduler.</summary>
        private static double IdealEta(PathManager pm, BotNormal bn, InputStation station, double now)
        {
            bool useReservationEta = station.Instance.SettingConfig != null
                && station.Instance.SettingConfig.SlowStartUseReservationEta;
            double eta = double.NaN;
            if (useReservationEta)
                eta = pm.EstimateReservationAwareEta(bn, bn.CurrentWaypoint, station.Waypoint, now, bn.GetTargetOrientation());
            if (double.IsNaN(eta) || double.IsInfinity(eta))
                eta = pm.EstimateIdealKinematicEta(bn, bn.CurrentWaypoint, station.Waypoint, now, bn.GetTargetOrientation());
            if (double.IsNaN(eta) || double.IsInfinity(eta))
                eta = SlowStartController.ComputeIdealEta(bn, bn.CurrentWaypoint, station.Waypoint);
            if (double.IsNaN(eta) || double.IsInfinity(eta)) eta = 0.0;
            return eta;
        }
    }
}
