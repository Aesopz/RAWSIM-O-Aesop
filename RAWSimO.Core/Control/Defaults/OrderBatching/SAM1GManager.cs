using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Starvation-Aware M1G (SA-M1G).
    ///
    /// Identical to <see cref="M1GManager"/> (distance cost + order-completion reward + station
    /// capacity cost) plus one extra term in the pod-&gt;station objective: a <b>pod-delay penalty</b>.
    ///
    /// For each candidate (pod, station) the manager estimates a <b>free-flow ideal arrival time</b>
    /// (a lower bound): representative bot-&gt;pod time + pod-&gt;station travel time, both at nominal
    /// speed and conflict-free. It compares this against the station's <b>EST</b>
    /// (<see cref="SlowStartController.ComputeStationWorkProjection"/>.FirstStarveSec — seconds until
    /// the station runs out of work). If the pod cannot catch the EST (arrival &gt; EST), the station
    /// would sit idle for (arrival - EST) seconds; that starvation window, times
    /// <see cref="SAM1GConfiguration.DelayPenaltyWeight"/>, is added to the cost. This discourages
    /// the optimizer from picking a high-value pod that would nevertheless starve the station.
    ///
    /// EST and arrivals are recomputed on every solve (PrepareDecisionExtras). The penalty weight
    /// is a sweep knob. Base M1G behavior is untouched (PodStationExtraCost returns 0 there).
    /// </summary>
    public class SAM1GManager : M1GManager
    {
        private readonly SAM1GConfiguration _saM1GConfig;
        private double _delayPenaltyWeight = 1.0;
        private double _nominalSpeed = 1.0;
        private readonly Dictionary<int, double> _estByStation = new Dictionary<int, double>();   // station.ID -> EST [s]
        private readonly Dictionary<int, double> _repBotPodTime = new Dictionary<int, double>();  // pod.ID -> min free-flow bot->pod time [s]

        /// <summary>Creates a new SA-M1G manager.</summary>
        public SAM1GManager(Instance instance) : base(instance)
        {
            _saM1GConfig = instance.ControllerConfig.OrderBatchingConfig as SAM1GConfiguration;
            if (_saM1GConfig != null)
                _delayPenaltyWeight = _saM1GConfig.DelayPenaltyWeight;
        }

        /// <summary>Per-solve precompute: nominal speed, each station's EST, and the representative
        /// (nearest available bot) free-flow bot-&gt;pod travel time per candidate pod.</summary>
        protected override void PrepareDecisionExtras(IEnumerable<Pod> pods,
            Dictionary<OutputStation, int> Cs, HashSet<Bot> Ra)
        {
            double cfgSpeed = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StarveAwareNominalSpeed : 0.0;
            _nominalSpeed = cfgSpeed > 0.0
                ? cfgSpeed
                : (Instance != null && Instance.Bots != null && Instance.Bots.Count > 0
                    ? Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity))
                    : 1.0);

            double now = Instance != null && Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;

            _estByStation.Clear();
            foreach (var s in Cs.Keys)
                _estByStation[s.ID] = StarveAwareCost.Est(s, now);

            _repBotPodTime.Clear();
            foreach (var p in pods)
            {
                double best = double.PositiveInfinity;
                foreach (var r in Ra)
                    best = Math.Min(best, StarveAwareCost.TravelTime(EstimateBotPodDistance(r, p), _nominalSpeed));
                _repBotPodTime[p.ID] = best;
            }
        }

        /// <summary>Pod-delay penalty added to the pod-&gt;station objective coefficient:
        /// max(0, free_flow_arrival - EST) * DelayPenaltyWeight.</summary>
        protected override double PodStationExtraCost(Pod pod, OutputStation station)
        {
            if (pod == null || station == null)
                return 0.0;

            double podStationTime = StarveAwareCost.TravelTime(EstimatePodStationDistance(pod, station), _nominalSpeed);
            double repBotPod = (_repBotPodTime.TryGetValue(pod.ID, out var t) && !double.IsPositiveInfinity(t)) ? t : 0.0;
            double arrival = repBotPod + podStationTime;                                  // free-flow ideal arrival (lower bound)
            double est = _estByStation.TryGetValue(station.ID, out var e) ? e : double.PositiveInfinity;

            // Pod cannot catch the EST -> station idles for (arrival - EST) seconds.
            double delaySec = Math.Max(0.0, arrival - est);
            return delaySec * _delayPenaltyWeight;
        }
    }
}
