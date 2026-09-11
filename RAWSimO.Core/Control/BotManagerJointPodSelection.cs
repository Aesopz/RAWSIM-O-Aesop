using RAWSimO.Core.Bots;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Management;
using RAWSimO.Core.Metrics;
using RAWSimO.Core.Waypoints;
using RAWSimO.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control
{
    public abstract partial class BotManager
    {
        /// <summary>
        /// Result of the station-bounded joint task-allocation and pod-selection candidate search.
        /// </summary>
        protected struct JointExtractSelection
        {
            internal bool Found;
            internal bool Feasible;
            internal Bot Bot;
            internal Pod Pod;
            internal OutputStation Station;
            internal List<ExtractRequest> Requests;
            internal double Eta;
            internal double StationEst;
            internal double SafetyBuffer;
        }

        /// <summary>
        /// Relevant pod candidate cached once per station-level joint decision.
        /// </summary>
        private struct JointPodCandidate
        {
            internal Pod Pod;
            internal List<ExtractRequest> Requests;
            internal double PodToStationEta;
        }

        /// <summary>
        /// Selects an extract task by jointly evaluating station-local candidate bots and pods.
        /// </summary>
        /// <param name="station">The output station requesting more work.</param>
        /// <param name="stationBots">The station-bounded bot pool.</param>
        /// <param name="config">The pod-selection configuration to reuse for scoring feasible candidates.</param>
        /// <returns>The selected candidate, or an empty result if no pod can currently serve the station.</returns>
        protected JointExtractSelection SelectStationBoundedJointExtract(
            OutputStation station,
            IEnumerable<Bot> stationBots,
            DefaultPodSelectionConfiguration config)
        {
            var empty = new JointExtractSelection { Found = false };
            if (station == null || stationBots == null || config == null)
                return empty;

            InitPodSelection();

            double currentTime = Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;
            var projection = SlowStartController.ComputeStationWorkProjection(station, currentTime);
            double stationEst = projection.FirstStarveSec;
            double safetyBuffer = Instance.SettingConfig != null
                ? Math.Max(0.0, Instance.SettingConfig.SlowStartEtaSafetyBuffer)
                : 0.0;

            var candidates = new List<JointExtractSelection>();
            var candidateBots = stationBots
                .Where(IsAvailableForStationBoundedJointExtract)
                .ToList();
            if (candidateBots.Count == 0)
                return empty;

            var podCandidates = GetStationBoundedJointPodShortlist(station, config);
            if (podCandidates.Count == 0)
                return empty;

            foreach (var bot in candidateBots)
            {
                foreach (var podCandidate in podCandidates)
                {
                    double eta = EstimateJointExtractEta(bot, podCandidate.Pod, station, podCandidate.PodToStationEta);
                    if (double.IsNaN(eta) || double.IsInfinity(eta))
                        continue;

                    candidates.Add(new JointExtractSelection
                    {
                        Found = true,
                        Bot = bot,
                        Pod = podCandidate.Pod,
                        Station = station,
                        Requests = podCandidate.Requests,
                        Eta = eta,
                        StationEst = stationEst,
                        SafetyBuffer = safetyBuffer,
                        Feasible = eta <= Math.Max(0.0, stationEst - safetyBuffer)
                    });
                }
            }

            if (candidates.Count == 0)
                return empty;

            var feasibleCandidates = candidates.Where(c => c.Feasible).ToList();
            if (feasibleCandidates.Count == 0)
            {
                return candidates
                    .OrderBy(c => c.Eta)
                    .ThenByDescending(c => c.Requests.Count)
                    .First();
            }

            var selector = new BestCandidateSelector(false,
                GenerateScorerPodForOStationBot(config.OutputPodScorer),
                GenerateScorerPodForOStationBot(config.OutputPodScorerTieBreaker1),
                GenerateScorerPodForOStationBot(config.OutputPodScorerTieBreaker2));

            JointExtractSelection best = empty;
            foreach (var candidate in feasibleCandidates)
            {
                _currentBot = candidate.Bot;
                _currentPod = candidate.Pod;
                _currentOStation = candidate.Station;
                if (selector.Reassess())
                    best = candidate;
            }

            return best.Found ? best : feasibleCandidates.OrderBy(c => c.Eta).First();
        }

        private List<JointPodCandidate> GetStationBoundedJointPodShortlist(
            OutputStation station,
            DefaultPodSelectionConfiguration config)
        {
            int limit = Math.Max(1, config.StationBoundedJointCandidatePodLimit);
            var candidates = new List<JointPodCandidate>();

            foreach (var pod in Instance.ResourceManager.UnusedPods)
            {
                var requests = GetPossibleRequests(pod, station, config.FilterForReservation);
                if (requests == null || requests.Count == 0)
                    continue;

                double podToStationEta = EstimateEta(pod.Waypoint, station.Waypoint, true);
                if (double.IsNaN(podToStationEta) || double.IsInfinity(podToStationEta))
                    continue;

                candidates.Add(new JointPodCandidate
                {
                    Pod = pod,
                    Requests = requests,
                    PodToStationEta = podToStationEta
                });
            }

            if (candidates.Count <= limit)
                return candidates;

            int workSlots = Math.Max(1, limit / 2);
            int etaSlots = Math.Max(1, limit - workSlots);
            return candidates
                .OrderByDescending(c => c.Requests.Count)
                .ThenBy(c => c.PodToStationEta)
                .Take(workSlots)
                .Concat(candidates
                    .OrderBy(c => c.PodToStationEta)
                    .ThenByDescending(c => c.Requests.Count)
                    .Take(etaSlots))
                .GroupBy(c => c.Pod)
                .Select(g => g.First())
                .Take(limit)
                .ToList();
        }

        private bool IsAvailableForStationBoundedJointExtract(Bot bot)
        {
            if (bot == null || bot.Pod != null)
                return false;
            if (Instance.ResourceManager._usedPods.ContainsValue(bot))
                return false;
            if (Instance.ResourceManager.BottoPod.ContainsKey(bot))
                return false;

            bool queuedForWork = _taskQueues.ContainsKey(bot) &&
                _taskQueues[bot] != null &&
                _taskQueues[bot].Type != BotTaskType.None &&
                _taskQueues[bot].Type != BotTaskType.Rest;
            if (queuedForWork)
                return false;

            if (bot.CurrentTask == null || bot.CurrentTask.Type == BotTaskType.None)
                return true;
            if (bot.CurrentTask.Type == BotTaskType.Rest)
            {
                var normal = bot as BotNormal;
                return normal != null && normal.IsResting();
            }

            return false;
        }

        private double EstimateJointExtractEta(Bot bot, Pod pod, OutputStation station, double podToStationEta)
        {
            if (bot == null || pod == null || station == null)
                return double.NaN;

            double botToPod = EstimateEta(bot.CurrentWaypoint, pod.Waypoint, false);
            double lift = bot.Pod == null ? bot.PodTransferTime : 0.0;

            if (double.IsNaN(botToPod) || double.IsInfinity(botToPod) ||
                double.IsNaN(podToStationEta) || double.IsInfinity(podToStationEta))
                return double.NaN;

            return botToPod + lift + podToStationEta;
        }

        private double EstimateEta(Waypoint from, Waypoint to, bool emulatePodCarrying)
        {
            if (from == null || to == null)
                return double.NaN;
            return emulatePodCarrying
                ? Distances.CalculateShortestTimePathPodSafe(from, to, Instance)
                : Distances.CalculateShortestTimePath(from, to, Instance);
        }
    }
}
