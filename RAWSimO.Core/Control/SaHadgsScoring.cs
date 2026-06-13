using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// Pure scoring math for SA-HADGS (see docs/superpowers/specs/2026-06-13-sa-hadgs-design.md).
    /// No Instance/simulation state — unit-testable via the Playground selftest harness.
    /// </summary>
    public static class SaHadgsScoring
    {
        /// <summary>Lexicographic "late" penalty for TA pairing: any on-time pair beats any late pair.</summary>
        public const double BIG = 1e6;

        /// <summary>
        /// Single-server pipeline simulation: total starvation gap (seconds) the job set leaves
        /// against a station whose current work runs out estSec from now. Jobs are (etaSec, workSec)
        /// relative to now; arrival order is normalized internally.
        /// Infinity in etaSec/estSec propagates to the returned gap; jobs with workSec &lt;= 0 are ignored entirely (including their eta). NaN inputs are unsupported.
        /// </summary>
        public static double ProjectedGapSeconds(double estSec, IEnumerable<(double etaSec, double workSec)> jobs)
        {
            double pipelineEnd = Math.Max(0.0, estSec);
            double gap = 0.0;
            foreach (var job in jobs.OrderBy(j => j.etaSec))
            {
                if (job.workSec <= 0.0)
                    continue;
                if (job.etaSec > pipelineEnd)
                {
                    gap += job.etaSec - pipelineEnd;
                    pipelineEnd = job.etaSec + job.workSec;
                }
                else
                {
                    pipelineEnd += job.workSec;
                }
            }
            return gap;
        }

        /// <summary>
        /// TA bot↔pod pair score (minimize): on-time (eta &lt;= est + slack) lexicographically
        /// dominates late via the BIG penalty; within a class, smaller eta wins.
        /// Assumes finite etaSec is travel-scale (≪ BIG); unreachable pods must use PositiveInfinity, not a large finite sentinel.
        /// </summary>
        public static double TaPairScore(double etaSec, double estSec, double slackSec)
        {
            return etaSec + (etaSec <= estSec + slackSec ? 0.0 : BIG);
        }

        /// <summary>
        /// Weighted candidate score (minimize):
        /// −orderRewardSec×completableOrders + projectedGapSec + travelWeight×sumTravelSec.
        /// orderRewardSec = "seconds of starvation gap one completed order is worth" (main sweep knob).
        /// </summary>
        public static double CandidateScore(int completableOrders, double projectedGapSec,
            double sumTravelSec, double orderRewardSec, double travelWeight)
        {
            return -orderRewardSec * completableOrders + projectedGapSec + travelWeight * sumTravelSec;
        }

        /// <summary>
        /// Regret-based greedy assignment on a pods×bots score matrix (minimize per pair).
        /// Each round: for every unassigned pod compute best and second-best free-bot scores;
        /// the pod with the largest regret (second − best) binds its best bot first.
        /// Returns podIndex → botIndex, or null if pods outnumber bots.
        /// Empty matrices return an empty assignment. Regret ties break by lowest pod index (deterministic).
        /// </summary>
        public static int[] RegretAssign(double[,] score)
        {
            int pods = score.GetLength(0);
            int bots = score.GetLength(1);
            if (pods > bots)
                return null;
            int[] result = new int[pods];
            for (int i = 0; i < pods; i++) result[i] = -1;
            bool[] botUsed = new bool[bots];
            for (int round = 0; round < pods; round++)
            {
                int pickPod = -1, pickBot = -1;
                double pickRegret = double.NegativeInfinity;
                for (int p = 0; p < pods; p++)
                {
                    if (result[p] >= 0) continue;
                    int bestBot = -1;
                    double best = double.PositiveInfinity, second = double.PositiveInfinity;
                    for (int b = 0; b < bots; b++)
                    {
                        if (botUsed[b]) continue;
                        double s = score[p, b];
                        if (s < best) { second = best; best = s; bestBot = b; }
                        else if (s < second) second = s;
                    }
                    if (bestBot < 0) return null;
                    double regret = double.IsPositiveInfinity(second) ? best : second - best;
                    if (regret > pickRegret) { pickRegret = regret; pickPod = p; pickBot = bestBot; }
                }
                result[pickPod] = pickBot;
                botUsed[pickBot] = true;
            }
            return result;
        }
    }
}
