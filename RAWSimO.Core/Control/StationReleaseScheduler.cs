using RAWSimO.Core.Bots;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// Centralized per-station release scheduler. Replaces the distributed-FIFO holder
    /// coordination in SlowStartController. One pod releases at a time, chosen by
    /// feasibility-first (can arrive before the station starves) then by live pod↔station
    /// value. Non-chosen holders evaluate their own budget independently against the same
    /// starveTime (no cascade extension), and are re-evaluated each tick.
    /// See docs/superpowers/specs/2026-05-26-station-release-scheduler-design.md.
    /// </summary>
    public static partial class StationReleaseScheduler
    {
        /// <summary>One holding bot's decision inputs (no engine objects — pure/testable).</summary>
        public struct HolderInput
        {
            public int BotId;
            public double Lift;    // PodTransferTime if not yet lifted, else 0 [s]
            public double Travel;  // ideal kinematic pod→station ETA, no conflicts [s]
            public double Value;   // live pod↔station demand match (selection score)
            public double Proc;    // committed pick work = Requests.Count × ItemTransferTime [s]
            public int BaseItems;  // committed item count
            public Pod Pod;        // pod used for on-the-fly potential work projection
        }

        /// <summary>Scheduler output: which holder releases, and each holder's hold delay from now.</summary>
        public struct SchedulerResult
        {
            public int ChosenBotId;
            public bool ChosenReleaseNow;                 // true when fire-fighting (no feasible holder)
            public Dictionary<int, double> HoldDelayByBot; // botId → seconds to keep holding from now
        }

        /// <summary>
        /// Pure decision. starveTime = time-to-starvation excluding ALL holders.
        /// budget_h = starveTime − lift_h − travel_h − buffer; feasible_h = budget_h ≥ 0.
        /// chosen = argmax value among feasible; if none feasible, argmin (lift+travel) and release now.
        /// Cascade for others is refined in Schedule with a full station work projection,
        /// because late in-flight pods can extend the post-starvation work horizon.
        /// </summary>
        public static SchedulerResult Decide(double starveTime, IReadOnlyList<HolderInput> holders, double buffer)
        {
            var result = new SchedulerResult { HoldDelayByBot = new Dictionary<int, double>() };
            if (holders == null || holders.Count == 0)
            {
                result.ChosenBotId = -1;
                return result;
            }

            double Budget(HolderInput h) => starveTime - h.Lift - h.Travel - buffer;
            double Arrival(HolderInput h) => h.Lift + h.Travel;

            var feasible = holders.Where(h => Budget(h) >= 0.0).ToList();

            HolderInput chosen;
            bool releaseNow;
            if (feasible.Count > 0)
            {
                chosen = feasible
                    .OrderByDescending(h => h.Value)
                    .ThenBy(h => Arrival(h))
                    .ThenBy(h => h.BotId)
                    .First();
                releaseNow = false;
            }
            else
            {
                chosen = holders
                    .OrderBy(h => Arrival(h))
                    .ThenBy(h => h.BotId)
                    .First();
                releaseNow = true;
            }

            result.ChosenBotId = chosen.BotId;
            result.ChosenReleaseNow = releaseNow;
            result.HoldDelayByBot[chosen.BotId] = releaseNow ? 0.0 : Math.Max(0.0, Budget(chosen));

            double chosenArrivalFull = Arrival(chosen);
            double newStarve = (chosenArrivalFull > starveTime)
                ? chosenArrivalFull + chosen.Proc
                : starveTime + chosen.Proc;

            foreach (var h in holders)
            {
                if (h.BotId == chosen.BotId) continue;
                result.HoldDelayByBot[h.BotId] = Math.Max(0.0, newStarve - h.Lift - h.Travel - buffer);
            }
            return result;
        }

        /// <summary>
        /// Live pod↔station value = Σ_item min(pod.CountAvailable(item), station open demand[item]),
        /// matching SEQU's PodMatchingOrderManager scorer against ResourceManager open demand.
        /// Committed picks on the task are counted separately so already claimed work is not
        /// lost when open demand is empty.
        /// </summary>
        public static double ComputePodStationValue(
            RAWSimO.Core.Elements.Pod pod, RAWSimO.Core.Elements.OutputStation station, int committedFallback)
        {
            return ComputePodStationValue(pod, BuildStationOpenDemand(station), null, committedFallback);
        }

        public static double ComputePodStationValue(
            RAWSimO.Core.Elements.Pod pod,
            IReadOnlyDictionary<RAWSimO.Core.Items.ItemDescription, int> openDemand,
            IEnumerable<ExtractRequest> committedRequests,
            int committedFallback)
        {
            double value = 0.0;
            if (committedRequests != null)
                value += committedRequests.Count(r => r != null && r.State != RequestState.Finished);
            if (pod == null || openDemand == null || openDemand.Count == 0)
                return value > 0.0 ? value : committedFallback;

            foreach (var kv in openDemand)
                value += Math.Min(pod.CountAvailable(kv.Key), kv.Value);
            return value > 0.0 ? value : committedFallback;
        }

        private static Dictionary<RAWSimO.Core.Items.ItemDescription, int> BuildStationOpenDemand(OutputStation station)
        {
            var demand = new Dictionary<RAWSimO.Core.Items.ItemDescription, int>();
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

        /// <summary>
        /// Per-station, per-tick entry point. Gathers all current holders for the station,
        /// builds HolderInput (ETA via pathManager, value via ComputePodStationValue, starve via
        /// SlowStartController.ComputeStationStarvation), runs Decide, and writes the release
        /// deadline + diagnostics onto each holder bot.
        /// </summary>
        public static void Schedule(
            RAWSimO.Core.Elements.OutputStation station, PathManager pathManager, double currentTime, double buffer)
        {
            if (station == null || pathManager == null) return;

            var instance = station.Instance;
            // Collect holders bound to THIS station.
            var holderBots = new List<BotNormal>();
            foreach (var b in instance.Bots)
            {
                var bn = b as BotNormal;
                if (bn == null || !bn._isSlowStartHolding) continue;
                var task = bn.CurrentTask as ExtractTask;
                if (task == null || task.OutputStation != station) continue;
                holderBots.Add(bn);
            }
            if (holderBots.Count == 0) return;

            var projection = SlowStartController.ComputeStationWorkProjection(station, currentTime);
            double starve = projection.FirstStarveSec;
            var openDemand = BuildStationOpenDemand(station);
            bool useReservationEta = station.Instance.SettingConfig != null
                && station.Instance.SettingConfig.SlowStartUseReservationEta;

            var inputs = new List<HolderInput>();
            var etaById = new Dictionary<int, double>();
            foreach (var bn in holderBots)
            {
                var task = bn.CurrentTask as ExtractTask;
                double eta = double.NaN;
                if (useReservationEta)
                    eta = pathManager.EstimateReservationAwareEta(
                        bn, bn.CurrentWaypoint, station.Waypoint, currentTime, bn.GetTargetOrientation());
                if (double.IsNaN(eta) || double.IsInfinity(eta))
                    eta = pathManager.EstimateIdealKinematicEta(
                        bn, bn.CurrentWaypoint, station.Waypoint, currentTime, bn.GetTargetOrientation());
                if (double.IsNaN(eta) || double.IsInfinity(eta))
                    eta = SlowStartController.ComputeIdealEta(bn, bn.CurrentWaypoint, station.Waypoint);
                if (double.IsNaN(eta) || double.IsInfinity(eta)) eta = 0.0;

                double lift = (bn.Pod == null) ? bn.PodTransferTime : 0.0;
                int committed = (task.Requests != null) ? task.Requests.Count : 0;
                double value = ComputePodStationValue(task.ReservedPod, openDemand, task.Requests, committed);
                // Pod-depletion time (see SlowStartController): last item releases the pod at
                // ItemPickTime, so m items = (m−1)·ItemTransferTime + ItemPickTime.
                double proc = committed <= 0 ? 0.0 : (committed - 1) * station.ItemTransferTime + station.ItemPickTime;

                etaById[bn.ID] = eta;
                inputs.Add(new HolderInput
                {
                    BotId = bn.ID,
                    Lift = lift,
                    Travel = eta,
                    Value = value,
                    Proc = proc,
                    BaseItems = committed,
                    Pod = task.ReservedPod
                });
            }

            var result = Decide(starve, inputs, buffer);
            var inputById = inputs.ToDictionary(h => h.BotId, h => h);
            var chosenInput = inputById.ContainsKey(result.ChosenBotId)
                ? inputById[result.ChosenBotId]
                : new HolderInput { BotId = -1 };
            double chosenArrival = chosenInput.BotId >= 0 ? chosenInput.Lift + chosenInput.Travel : 0.0;
            double cascadeStarve = projection.WorkHorizonSec;
            if (chosenInput.BotId >= 0)
            {
                var chosenJob = new SlowStartController.StationWorkJob
                {
                    ArrivalAbs = currentTime + chosenArrival,
                    BaseItems = chosenInput.BaseItems,
                    Pod = chosenInput.Pod,
                    ArrivalConfirmed = false
                };
                var chosenProjection = SlowStartController.ComputeStationWorkProjection(
                    station, currentTime, new[] { chosenJob });
                cascadeStarve = chosenProjection.WorkHorizonSec;
            }

            foreach (var bn in holderBots)
            {
                var input = inputById[bn.ID];
                bn._slowStartIsChosen = (bn.ID == result.ChosenBotId);
                double effectiveReleaseBudget = bn._slowStartIsChosen ? starve : cascadeStarve;
                double delay = bn._slowStartIsChosen
                    ? (result.HoldDelayByBot.TryGetValue(bn.ID, out var d) ? d : 0.0)
                    : Math.Max(0.0, effectiveReleaseBudget - input.Lift - input.Travel - buffer);
                bn._slowStartReleaseDeadline = currentTime + delay;
                bn._slowStartEta = etaById[bn.ID];
                bn._slowStartTStarve = starve;
                double baseBudget = starve - input.Lift - input.Travel - buffer;
                double effectiveBudget = effectiveReleaseBudget - input.Lift - input.Travel - buffer;
                instance.NotifySlowStartHoldingDecision(
                    station,
                    bn,
                    bn.CurrentTask as ExtractTask,
                    currentTime,
                    starve,
                    projection.WorkHorizonSec,
                    projection.StarvationGapSec,
                    projection.LateJobCount,
                    projection.UncertainJobCount,
                    buffer,
                    input.Lift,
                    input.Travel,
                    input.Value,
                    input.Proc,
                    effectiveReleaseBudget,
                    effectiveBudget,
                    delay,
                    bn._slowStartReleaseDeadline,
                    baseBudget >= 0.0,
                    bn._slowStartIsChosen,
                    result.ChosenReleaseNow && bn._slowStartIsChosen,
                    holderBots.Count,
                    result.ChosenBotId);
            }
        }
    }
}
