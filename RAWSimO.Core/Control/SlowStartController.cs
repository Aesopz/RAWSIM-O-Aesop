using RAWSimO.Core.Bots;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control.JIT;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.Core.Waypoints;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// PP-aware slow-start controller. Computes the hold duration for a bot that has
    /// just arrived at its target pod (Extract task), so that the bot does not start
    /// the pod→station traversal earlier than necessary. The hold duration equals
    /// max(0, T_starve − ETA), where:
    ///   - ETA is the reservation-aware path time from pod cell to station, queried
    ///     from PathManager.EstimateReservationAwareEta().
    ///   - T_starve is the time until the station would run out of work, computed by
    ///     sequencing the station's current pick, queued item requests, and all
    ///     in-flight ExtractTasks heading to this station (excluding self).
    /// See docs/superpowers/specs/2026-05-20-slow-start-pod-release-design.md (rev2).
    /// </summary>
    public static class SlowStartController
    {
        private struct StarveJob
        {
            public double Arrival;
            public int BaseItems;
            public Pod Pod;
            public bool ArrivalConfirmed;
        }

        internal struct StationWorkJob
        {
            public double ArrivalAbs;
            public int BaseItems;
            public Pod Pod;
            public bool ArrivalConfirmed;
        }

        internal struct StationWorkProjection
        {
            public double FirstStarveSec;
            public double WorkHorizonSec;
            public double StarvationGapSec;
            public int JobCount;
            public int LateJobCount;
            public int ConfirmedJobCount;
            public int UncertainJobCount;
        }

        /// <summary>
        /// Diagnostic outputs from a single ComputeHold call.
        /// </summary>
        public struct HoldDiagnostics
        {
            public double Delay;             // chosen hold duration [s]
            public double Eta;               // reservation-aware ETA pod→station [s] (NaN if probe failed)
            public double TStarve;           // computed time-to-starvation [s]
            public double QueueBudget;        // inferred station-queue conversion budget [s]
            public double ReleaseBudget;      // budget actually used for release timing [s]
            public bool   EtaProbeFailed;    // true if ETA probe returned NaN
            public int    MissingArrivalCount; // # of in-flight tasks without ExpectedArrivalAtStation cache
            public bool   ImmediateRelease;  // true if delay = 0 (ETA ≥ T_starve, or station already starving)
        }

        /// <summary>
        /// Compute the slow-start hold for `self` carrying `task.ReservedPod` toward `task.OutputStation`.
        /// Caller (BotSlowStartHold.Act) is responsible for setting BlockedUntil = currentTime + diag.Delay
        /// and writing task.ExpectedArrivalAtStation = currentTime + diag.Delay + diag.Eta (if Eta valid).
        /// </summary>
        public static HoldDiagnostics ComputeHold(BotNormal self, ExtractTask task, double currentTime)
        {
            var diag = new HoldDiagnostics();

            // ── 1. ETA pod→station ─────────────────────────────────────────
            // Ideal kinematic ETA — distance-cost A* (no reservation table, no multi-bot
            // conflict). Yields the no-conflict lower bound; multi-bot delay is absorbed
            // by SlowStartEtaSafetyBuffer instead. Avoids the 89% probe-failure rate seen
            // with reservation-aware probing in busy reservation windows.
            double eta = ComputeIdealEta(self, self.CurrentWaypoint, task.OutputStation?.Waypoint);
            diag.EtaProbeFailed = double.IsNaN(eta);
            if (diag.EtaProbeFailed)
            {
                // Path manager unavailable (e.g., null waypoint). Fall back to Manhattan.
                eta = ManhattanEta(self.CurrentWaypoint, task.OutputStation?.Waypoint);
            }
            diag.Eta = eta;

            // ── 2. T_starve ─────────────────────────────────────────────────
            double tStarve = ComputeStarvation(task.OutputStation, self, currentTime, out diag.MissingArrivalCount);
            diag.TStarve = tStarve;

            // ── 3. delay ────────────────────────────────────────────────────
            if (double.IsNaN(eta) || double.IsInfinity(eta))
            {
                // Even Manhattan fallback failed (waypoints null) — give up safely.
                diag.Delay = 0.0;
                diag.ImmediateRelease = true;
                return diag;
            }
            var setting = self.Instance != null ? self.Instance.SettingConfig : null;
            SlowStartReleasePolicy policy = setting != null ? setting.SlowStartReleasePolicy : SlowStartReleasePolicy.StationSlack;
            // Safety buffer applies UNCONDITIONALLY (regardless of release policy): the real
            // holding budget is T_starve minus lift time, minus the ideal travel ETA, minus a
            // fixed safety margin. This absorbs (a) the +~2 s ideal-estimate over/under-shoot
            // measured under bot=1 and (b) multi-bot congestion delay on the actual trip, so the
            // bot arrives slightly early rather than starving the station. Defaults to 10 s when
            // SlowStartEtaSafetyBuffer is left at 0/unset.
            double etaSafetyBuffer = (setting != null) ? Math.Max(0.0, setting.SlowStartEtaSafetyBuffer) : 0.0;
            if (etaSafetyBuffer <= 0.0)
                etaSafetyBuffer = 16.8;  // empirical mean |estimate − actual| gap from Mu-100 ON run:
                                          // ideal ETA under-estimates congested travel by ~16.8 s,
                                          // so release this much earlier to absorb the delay and
                                          // stop the station starving (was fixed 10 s).
            double budget = tStarve;
            if (policy == SlowStartReleasePolicy.QueueBudgetEtaImprovement)
            {
                diag.QueueBudget = ComputeQueueBudget(task.OutputStation, self, setting);
                budget = Math.Max(budget, diag.QueueBudget);
            }
            diag.ReleaseBudget = budget;
            // Lift-up time (PodTransferTime, typically 2.2 s). Pre-lift hold means after
            // the hold ends the bot still has to lift the pod before traveling, so total
            // time until arrival = hold + lift + eta. Solve for hold:
            //   slack = T_starve - (lift + eta) - safety_buffer
            // If the bot is already loaded (post-lift hold, the "else" branch in task
            // dispatch), liftTime should be 0 — detect by checking whether self carries pod.
            double liftTime = (self != null && self.Pod == null) ? self.PodTransferTime : 0.0;
            double slack = budget - eta - liftTime - etaSafetyBuffer;
            diag.Delay = (slack > 0.0) ? slack : 0.0;
            diag.ImmediateRelease = (diag.Delay <= 0.0);
            return diag;
        }

        /// <summary>
        /// Ideal kinematic ETA from `from` to `to` for `bot`. Delegates to the path planner's
        /// `EstimateIdealKinematicEta`, which runs the SAME SpaceTimeAStar machinery as the
        /// planner (same Graph, Physics, edge directionality) against an EMPTY reservation
        /// table — same path-finding logic as WHCA*n in the no-conflict case.
        ///
        /// Falls back to TimeEfficientPathManager + IdealTravelTime if the planner does not
        /// implement EstimateIdealKinematicEta (returns NaN).
        ///
        /// Returns NaN only when both the planner probe and the fallback fail.
        /// </summary>
        public static double ComputeIdealEta(BotNormal bot, Waypoint from, Waypoint to)
        {
            if (bot == null || from == null || to == null) return double.NaN;
            var instance = bot.Instance;
            if (instance == null) return double.NaN;

            // Primary: SpaceTimeAStar with empty reservation table — matches WHCA*n's
            // search behavior including single-direction aisles and turn penalties.
            var pathManager = instance.Controller?.PathManager;
            if (pathManager != null)
            {
                double eta = pathManager.EstimateIdealKinematicEta(
                    bot, from, to,
                    instance.Controller != null ? instance.Controller.CurrentTime : 0.0,
                    bot.GetTargetOrientation());
                if (!double.IsNaN(eta) && !double.IsInfinity(eta))
                    return eta;
            }

            // Fallback: TimeEfficientPathManager + kinematic accumulation (used when the
            // planner is not a WHCA* family that overrides EstimateIdealKinematicEta).
            var meta = instance.MetaInfoManager;
            if (meta == null) return double.NaN;
            List<Waypoint> path = meta.TimeEfficientPathManager
                .GetShortestPathNodes(from, to, instance, emulatePodCarrying: true);
            if (path == null || path.Count < 2) return 0.0;

            double initialOri = bot.GetTargetOrientation();
            return IdealTravelTime.Compute(
                path, bot.Physics, instance.StraightOrientationTolerance, initialOri);
        }

        /// <summary>
        /// Geometric Manhattan / v_conservative ETA estimate used as a last-resort fallback
        /// when waypoints or path manager are unavailable.
        /// </summary>
        private static double ManhattanEta(Waypoint from, Waypoint to)
        {
            const double V_CONSERVATIVE = 0.6;  // m/s — well below typical bot MaxVelocity (~1.5)
            if (from == null || to == null) return double.NaN;
            double dx = Math.Abs(from.X - to.X);
            double dy = Math.Abs(from.Y - to.Y);
            return (dx + dy) / V_CONSERVATIVE;
        }

        /// <summary>
        /// T_starve computation (rev5 — station-starvation monitor with sequential pipeline):
        ///
        /// Models the station as a single-server queue. Each committed (non-holding, non-self)
        /// ExtractTask is one job characterized by:
        ///   - arrivalTime  : when the bot reaches station.Waypoint (cached in
        ///                    ExtractTask.ExpectedArrivalAtStation when set by the SlowStart
        ///                    release path; falls back to currentTime for already-at/queueing
        ///                    bots; missing-arrival count flagged for in-flight w/o cache)
        ///   - workTime     : Requests.Count × ItemTransferTime (with −1 on the active job
        ///                    whose first item is already covered by stationBusy)
        ///
        /// Pipeline through the station:
        ///   serviceStart = max(stationFreeAt, arrivalTime)
        ///   serviceEnd   = serviceStart + workTime
        ///   stationFreeAt = serviceEnd
        /// T_starve = final stationFreeAt − currentTime.
        ///
        /// Implements the user's spec: a release pod that CAN arrive in time (arrival ≤
        /// running stationFreeAt) extends T_starve by its own pick time; a release pod that
        /// CANNOT arrive in time pushes T_starve to (arrival + pick time) — naturally
        /// creating an unavoidable starvation gap but providing the correct reference for
        /// subsequent holders' budgets.
        ///
        /// Holders are excluded to prevent the distributed prediction chain (rev2 issue).
        /// </summary>
        private static double ComputeStarvation(OutputStation station, BotNormal self, double currentTime, out int missingArrivalCount)
        {
            missingArrivalCount = 0;
            if (station == null) return 0.0;

            // 1. Station free-at: max(currentTime, busy-until of in-progress pick)
            double blockedUntilAbs = station.GetBlockedUntilTime();
            bool stationActive = !double.IsNaN(blockedUntilAbs) && blockedUntilAbs > currentTime;
            double stationFreeAt = stationActive ? blockedUntilAbs : currentTime;

            // 2. Build job list (arrivalTime, workTime) for each committed task at this station.
            // Two sources:
            //  (a) non-holding tasks (processing / queueing / post-release in-flight) — direct
            //  (b) OTHER holders with EARLIER _slowStartHoldStartTime than self (FIFO seniors)
            //      — look-ahead: assume they release at currentTime; their arrival = lift + eta
            //      This breaks the symmetry that otherwise causes simultaneous release.
            var jobs = new List<StarveJob>();

            // (a) Non-holding committed tasks at this station
            foreach (var t in station.GetActiveExtractTasks())
            {
                if (t == null || t.Requests == null || t.Requests.Count == 0) continue;
                if (object.ReferenceEquals(t, self.CurrentTask)) continue;
                var other = t.Bot as BotNormal;
                if (other == null) continue;
                if (other._isSlowStartHolding) continue;     // holders handled in (b)

                int itemsRemaining = t.Requests.Count;
                bool atStation = other.CurrentWaypoint == station.Waypoint;
                if (atStation && stationActive)
                    itemsRemaining = Math.Max(0, itemsRemaining - 1);
                if (itemsRemaining == 0) continue;
                bool arrivalConfirmed;
                double arrival = ResolveTaskArrival(t, station, other, currentTime, ref missingArrivalCount, out arrivalConfirmed);
                jobs.Add(new StarveJob { Arrival = arrival, BaseItems = itemsRemaining, Pod = t.ReservedPod, ArrivalConfirmed = arrivalConfirmed });
            }

            // 3. Sequential pipeline. Sort by arrival; for each job, serve when both
            //    station-free and bot-arrived.
            var projection = PipelineWorkProjectionWithPotential(station, stationFreeAt, jobs, currentTime);
            return projection.FirstStarveSec;
        }

        /// <summary>
        /// Time-to-starvation for a station, EXCLUDING every slow-start holder (the scheduler
        /// accounts for holders separately). Counts the in-progress pick plus all committed,
        /// non-holding ExtractTasks (processing / queueing / released-inbound) as single-server
        /// jobs, then returns seconds-from-now until the station would run dry.
        /// </summary>
        internal static double ComputeStationStarvation(OutputStation station, double currentTime)
        {
            return ComputeStationWorkProjection(station, currentTime).FirstStarveSec;
        }

        internal static StationWorkProjection ComputeStationWorkProjection(OutputStation station, double currentTime, IEnumerable<StationWorkJob> additionalJobs = null)
        {
            if (station == null) return new StationWorkProjection();

            double blockedUntilAbs = station.GetBlockedUntilTime();
            bool stationActive = !double.IsNaN(blockedUntilAbs) && blockedUntilAbs > currentTime;
            double stationFreeAt = stationActive ? blockedUntilAbs : currentTime;

            var jobs = new List<StarveJob>();
            foreach (var t in station.GetActiveExtractTasks())
            {
                if (t == null || t.Requests == null || t.Requests.Count == 0) continue;
                var other = t.Bot as BotNormal;
                if (other == null) continue;
                if (other._isSlowStartHolding) continue;   // ALL holders excluded

                int itemsRemaining = t.Requests.Count;
                bool atStation = other.CurrentWaypoint == station.Waypoint;
                if (atStation && stationActive)
                    itemsRemaining = Math.Max(0, itemsRemaining - 1);
                if (itemsRemaining == 0) continue;
                int ignoredMissingCount = 0;
                bool arrivalConfirmed;
                double arrival = ResolveTaskArrival(t, station, other, currentTime, ref ignoredMissingCount, out arrivalConfirmed);
                jobs.Add(new StarveJob { Arrival = arrival, BaseItems = itemsRemaining, Pod = t.ReservedPod, ArrivalConfirmed = arrivalConfirmed });
            }

            if (additionalJobs != null)
            {
                foreach (var job in additionalJobs)
                {
                    if (job.BaseItems <= 0 && job.Pod == null) continue;
                    jobs.Add(new StarveJob
                    {
                        Arrival = job.ArrivalAbs,
                        BaseItems = Math.Max(0, job.BaseItems),
                        Pod = job.Pod,
                        ArrivalConfirmed = job.ArrivalConfirmed
                    });
                }
            }

            return PipelineWorkProjectionWithPotential(station, stationFreeAt, jobs, currentTime);
        }

        /// <summary>
        /// Earliest-starvation single-server pipeline. Jobs are (arrivalTime, workTime).
        /// Server has work until stationFreeAt. If the next job arrives after that time,
        /// the station starves at stationFreeAt and later work cannot extend the safe hold
        /// budget. Returns the absolute starvation/free time; caller subtracts `now` to get
        /// a relative duration. `now` is accepted for signature symmetry with callers.
        /// </summary>
        internal static double PipelineNextFreeTime(
            double stationFreeAt, IEnumerable<(double arrival, double work)> jobs, double now)
        {
            var ordered = jobs.OrderBy(j => j.arrival).ToList();
            double t0 = stationFreeAt;
            foreach (var job in ordered)
            {
                if (job.arrival > t0)
                    return t0;
                t0 += job.work;
            }
            return t0;
        }

        private static double PipelineNextFreeTimeWithPotential(
            OutputStation station, double stationFreeAt, IEnumerable<StarveJob> jobs, double now)
        {
            return now + PipelineWorkProjectionWithPotential(station, stationFreeAt, jobs, now).FirstStarveSec;
        }

        /// <summary>
        /// Generic single-server projection over plain (arrival, work) jobs. Returns BOTH the
        /// earliest-starvation time (first arrival gap) and the FULL work horizon (time until ALL
        /// jobs clear, NOT truncated at the first gap). Mirrors PipelineWorkProjectionWithPotential
        /// but without pod-potential enrichment — used by the input scheduler, where store work is
        /// deterministic. WorkHorizon is what the non-chosen holders' cascade budget needs so a
        /// released pod's work extends the others' allowable hold (matching the output scheduler).
        /// </summary>
        internal static void PipelineFirstStarveAndHorizon(
            double stationFreeAt, IEnumerable<(double arrival, double work)> jobs, double now,
            out double firstStarveSec, out double workHorizonSec)
        {
            var ordered = jobs.OrderBy(j => j.arrival).ToList();
            double horizon = stationFreeAt;
            double firstStarve = double.NaN;
            foreach (var job in ordered)
            {
                if (job.work <= 0.0)
                    continue;
                if (job.arrival > horizon)
                {
                    if (double.IsNaN(firstStarve))
                        firstStarve = horizon;
                    horizon = job.arrival + job.work;
                }
                else
                {
                    horizon += job.work;
                }
            }
            if (double.IsNaN(firstStarve))
                firstStarve = horizon;
            firstStarveSec = Math.Max(0.0, firstStarve - now);
            workHorizonSec = Math.Max(0.0, horizon - now);
        }

        private static StationWorkProjection PipelineWorkProjectionWithPotential(
            OutputStation station, double stationFreeAt, IEnumerable<StarveJob> jobs, double now)
        {
            var openDemand = IsOnTheFlyExtractEnabled(station?.Instance)
                ? BuildOpenDemandByItem(station)
                : new Dictionary<ItemDescription, int>();
            var ordered = jobs.OrderBy(j => j.Arrival).ToList();
            double horizon = stationFreeAt;
            double firstStarve = double.NaN;
            double starvationGap = 0.0;
            int jobCount = 0;
            int lateJobCount = 0;
            int confirmedJobCount = 0;
            int uncertainJobCount = 0;

            foreach (var job in ordered)
            {
                int extraItems = ReservePotentialPicks(job.Pod, openDemand);
                // Pod-depletion time: the pod releases after the LAST item's PICK (ItemPickTime),
                // not its full transfer — the trailing tote-placement (ItemTransferTime − ItemPickTime)
                // is station-internal with the pod already gone. So m items deplete the pod in
                // (m−1)·ItemTransferTime + ItemPickTime, not m·ItemTransferTime.
                int totalItems = job.BaseItems + extraItems;
                double work = totalItems <= 0
                    ? 0.0
                    : (totalItems - 1) * station.ItemTransferTime + station.ItemPickTime;
                if (work <= 0.0)
                    continue;

                jobCount++;
                if (job.ArrivalConfirmed) confirmedJobCount++;
                else uncertainJobCount++;

                if (job.Arrival > horizon)
                {
                    if (double.IsNaN(firstStarve))
                        firstStarve = horizon;
                    starvationGap += job.Arrival - horizon;
                    lateJobCount++;
                    horizon = job.Arrival + work;
                }
                else
                {
                    horizon += work;
                }
            }

            if (double.IsNaN(firstStarve))
                firstStarve = horizon;

            return new StationWorkProjection
            {
                FirstStarveSec = Math.Max(0.0, firstStarve - now),
                WorkHorizonSec = Math.Max(0.0, horizon - now),
                StarvationGapSec = Math.Max(0.0, starvationGap),
                JobCount = jobCount,
                LateJobCount = lateJobCount,
                ConfirmedJobCount = confirmedJobCount,
                UncertainJobCount = uncertainJobCount
            };
        }

        private static double ResolveTaskArrival(
            ExtractTask task, OutputStation station, BotNormal bot, double currentTime, ref int missingArrivalCount)
        {
            bool ignoredConfirmed;
            return ResolveTaskArrival(task, station, bot, currentTime, ref missingArrivalCount, out ignoredConfirmed);
        }

        private static double ResolveTaskArrival(
            ExtractTask task, OutputStation station, BotNormal bot, double currentTime, ref int missingArrivalCount, out bool arrivalConfirmed)
        {
            arrivalConfirmed = false;
            if (bot == null || task == null || station == null)
                return currentTime;
            if (bot.CurrentWaypoint == station.Waypoint || bot.IsQueueing)
            {
                arrivalConfirmed = true;
                return currentTime;
            }
            if (!double.IsNaN(task.ExpectedArrivalAtStation) && task.ExpectedArrivalAtStation > currentTime)
                return task.ExpectedArrivalAtStation;

            missingArrivalCount++;
            double eta = ComputeIdealEta(bot, bot.CurrentWaypoint, station.Waypoint);
            if (!double.IsNaN(eta) && !double.IsInfinity(eta))
                return currentTime + Math.Max(0.0, eta);
            return currentTime;
        }

        private static Dictionary<ItemDescription, int> BuildOpenDemandByItem(OutputStation station)
        {
            var demand = new Dictionary<ItemDescription, int>();
            var resourceManager = station?.Instance?.ResourceManager;
            if (station == null || resourceManager == null)
                return demand;

            foreach (var request in resourceManager.GetExtractRequestsOfStation(station))
            {
                if (request == null || request.State != RequestState.Unfinished)
                    continue;
                if (demand.ContainsKey(request.Item)) demand[request.Item]++;
                else demand[request.Item] = 1;
            }
            return demand;
        }

        private static int ReservePotentialPicks(Pod pod, Dictionary<ItemDescription, int> openDemand)
        {
            if (pod == null || openDemand == null || openDemand.Count == 0)
                return 0;

            int total = 0;
            foreach (var item in openDemand.Keys.ToList())
            {
                int take = Math.Min(openDemand[item], pod.CountAvailable(item));
                if (take <= 0)
                    continue;
                total += take;
                openDemand[item] -= take;
                if (openDemand[item] <= 0)
                    openDemand.Remove(item);
            }
            return total;
        }

        private static bool IsOnTheFlyExtractEnabled(Instance instance)
        {
            var taskAllocationConfig = instance?.ControllerConfig?.TaskAllocationConfig;
            if (taskAllocationConfig == null)
                return true;

            var type = taskAllocationConfig.GetType();
            var podSelectionField = type.GetField("PodSelectionConfig");
            var podSelectionConfig = podSelectionField?.GetValue(taskAllocationConfig) as DefaultPodSelectionConfiguration;
            if (podSelectionConfig != null)
                return podSelectionConfig.OnTheFlyExtract;

            var directField = type.GetField("OnTheFlyExtract");
            object directValue = directField?.GetValue(taskAllocationConfig);
            return directValue is bool enabled ? enabled : true;
        }

        private static double ComputeQueueBudget(OutputStation station, BotNormal self, SettingConfiguration setting)
        {
            if (station == null || setting == null)
                return 0.0;

            double maxBudget = Math.Max(0.0, setting.SlowStartQueueBudgetMaxSec);
            if (maxBudget <= 0.0)
                return 0.0;

            int otherActiveExtractTasks = 0;
            double activeWork = 0.0;
            foreach (var t in station.GetActiveExtractTasks())
            {
                if (t == null || t.Requests == null || t.Requests.Count == 0)
                    continue;
                if (self != null && object.ReferenceEquals(t, self.CurrentTask))
                    continue;

                var other = t.Bot as BotNormal;
                if (other != null && other._isSlowStartHolding)
                    continue;

                otherActiveExtractTasks++;
                activeWork += t.Requests.Count * station.ItemTransferTime;
            }

            if (otherActiveExtractTasks < Math.Max(1, setting.SlowStartQueueBudgetMinOtherExtractTasks))
                return 0.0;

            double multiplier = Math.Max(0.0, setting.SlowStartQueueBudgetWorkMultiplier);
            return Math.Min(maxBudget, activeWork * multiplier);
        }

    }
}
