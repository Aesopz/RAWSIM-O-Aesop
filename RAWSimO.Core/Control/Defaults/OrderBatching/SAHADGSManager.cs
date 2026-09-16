using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Starvation-Aware HADGS (SA-HADGS).
    ///
    /// A thin subclass of <see cref="HADGSManager"/> — inherits ALL of HADGS's POA/PPS/TA logic and
    /// adds only a starvation-delay penalty on the output pod score, aligned term-for-term with
    /// <see cref="SAM1GManager"/>.
    ///
    /// HADGS already minimizes a SA-M1G-shaped pod score: <c>-(|w2|·completable_orders) + Σ travel</c>
    /// (HADGSManager.Score, with |w2|=40). SA-HADGS adds the SAME delay term SA-M1G uses:
    /// <c>+ max(0, free_flow_arrival - EST) · DelayPenaltyWeight</c>, where arrival = representative
    /// (nearest idle bot) bot→pod time + pod→station time (conflict-free lower bound) and
    /// EST = <see cref="SlowStartController.ComputeStationWorkProjection"/>.FirstStarveSec. The selector
    /// is MINIMIZED, so a positive penalty pushes late pods away. Because it is a subclass (not a copy),
    /// it stays in sync with HADGS, and SA-M1G (the exact MILP) validates the near-optimality of this
    /// greedy heuristic on the SAME objective.
    /// </summary>
    public class SAHADGSManager : HADGSManager
    {
        private readonly SAHADGSConfiguration _saConfig;
        private double _delayPenaltyWeight = 10.0;
        private double _nominalSpeed = 1.0;
        private readonly Dictionary<int, double> _estByStation = new Dictionary<int, double>();   // station.ID -> EST [s]
        private readonly Dictionary<int, double> _repBotPodTime = new Dictionary<int, double>();  // pod.ID -> min free-flow bot->pod time [s]

        /// <summary>Creates a new SA-HADGS manager.</summary>
        public SAHADGSManager(Instance instance) : base(instance)
        {
            _saConfig = instance.ControllerConfig.OrderBatchingConfig as SAHADGSConfiguration;
            if (_saConfig != null)
                _delayPenaltyWeight = _saConfig.DelayPenaltyWeight;
        }

        /// <summary>Per-decision precompute: nominal speed and each station's EST (FirstStarveSec).
        /// The representative bot→pod time is filled lazily per pod in the scorer.</summary>
        protected override void PrepareDecisionExtras()
        {
            double cfgSpeed = Instance != null && Instance.SettingConfig != null ? Instance.SettingConfig.StarveAwareNominalSpeed : 0.0;
            _nominalSpeed = cfgSpeed > 0.0
                ? cfgSpeed
                : (Instance != null && Instance.Bots != null && Instance.Bots.Count > 0
                    ? Math.Max(0.1, Instance.Bots.Max(b => b.MaxVelocity))
                    : 1.0);

            double now = Instance != null && Instance.Controller != null ? Instance.Controller.CurrentTime : 0.0;
            _estByStation.Clear();
            if (Instance != null)
                foreach (var s in Instance.OutputStations)
                    _estByStation[s.ID] = StarveAwareCost.Est(s, now);
            _repBotPodTime.Clear();
        }

        /// <summary>Starvation-delay penalty added to the (minimized) output pod score:
        /// max(0, free_flow_arrival - EST) * DelayPenaltyWeight. Positive => late pod => avoided.</summary>
        protected override double PodStationScoreAdjustment(Pod pod, OutputStation station)
        {
            if (pod == null || station == null || _delayPenaltyWeight == 0.0)
                return 0.0;

            // Representative (nearest idle bot) free-flow bot->pod time, cached per decision.
            if (!_repBotPodTime.TryGetValue(pod.ID, out double repBotPod))
            {
                repBotPod = double.PositiveInfinity;
                if (Instance != null && Instance.Bots != null)
                    foreach (var b in Instance.Bots)
                        if (b != null && b.Pod == null)
                            repBotPod = Math.Min(repBotPod, StarveAwareCost.TravelTime(EstimateBotPodDistance(b, pod), _nominalSpeed));
                if (double.IsPositiveInfinity(repBotPod)) repBotPod = 0.0;
                _repBotPodTime[pod.ID] = repBotPod;
            }

            double travel = StarveAwareCost.TravelTime(EstimatePodStationDistance(pod, station), _nominalSpeed);
            double arrival = repBotPod + travel;                                  // free-flow ideal arrival (lower bound)
            double est = _estByStation.TryGetValue(station.ID, out double e) ? e : double.PositiveInfinity;
            double delaySec = Math.Max(0.0, arrival - est);                       // station idle window if pod can't catch EST
            return delaySec * _delayPenaltyWeight;
        }
    }
}
