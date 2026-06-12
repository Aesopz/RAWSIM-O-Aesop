using System;
using System.Collections.Generic;
using RAWSimO.Core.Control;

namespace RAWSimO.Playground.Tests
{
    /// <summary>Runnable red-green assertions for SaHadgsScoring pure functions.</summary>
    public static class SaHadgsScoringSelfTest
    {
        private static int _fails = 0;

        private static void Near(double actual, double expected, string name, double tol = 1e-6)
        {
            bool ok = Math.Abs(actual - expected) <= tol;
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + $"  (got {actual}, want {expected})");
            if (!ok) _fails++;
        }
        private static void True(bool cond, string name)
        {
            Console.WriteLine((cond ? "PASS  " : "FAIL  ") + name);
            if (!cond) _fails++;
        }

        public static int RunAll()
        {
            _fails = 0;

            // --- ProjectedGapSeconds: single-server pipeline gap vs EST ---
            Near(SaHadgsScoring.ProjectedGapSeconds(30.0,
                new List<(double eta, double work)> { (10.0, 40.0), (20.0, 40.0) }),
                0.0, "Gap: all on-time -> 0");
            Near(SaHadgsScoring.ProjectedGapSeconds(30.0,
                new List<(double eta, double work)> { (50.0, 40.0) }),
                20.0, "Gap: first late -> eta-est");
            // job1 eta 10 work 5: pipeline 30 -> 35; job2 eta 50 -> gap 15
            Near(SaHadgsScoring.ProjectedGapSeconds(30.0,
                new List<(double eta, double work)> { (10.0, 5.0), (50.0, 5.0) }),
                15.0, "Gap: mid-chain starvation counted");
            Near(SaHadgsScoring.ProjectedGapSeconds(30.0,
                new List<(double eta, double work)> { (50.0, 5.0), (10.0, 5.0) }),
                15.0, "Gap: input order independent");
            Near(SaHadgsScoring.ProjectedGapSeconds(30.0,
                new List<(double eta, double work)>()), 0.0, "Gap: empty -> 0");

            // --- TaPairScore: on-time always beats late regardless of magnitude ---
            True(SaHadgsScoring.TaPairScore(100.0, 120.0, 0.0) <
                 SaHadgsScoring.TaPairScore(5.0, 4.0, 0.0),
                 "TaPair: on-time(100s) < late(5s)");
            True(SaHadgsScoring.TaPairScore(10.0, 120.0, 0.0) <
                 SaHadgsScoring.TaPairScore(100.0, 120.0, 0.0),
                 "TaPair: among on-time smaller eta wins");
            True(SaHadgsScoring.TaPairScore(35.0, 30.0, 10.0) < 1e5,
                 "TaPair: slack makes 35<=30+10 on-time");

            // --- CandidateScore: weighted delay vs orders trade-off ---
            double a = SaHadgsScoring.CandidateScore(1, 0.0, 0.0, 60.0, 0.0);
            double b = SaHadgsScoring.CandidateScore(2, 40.0, 0.0, 60.0, 0.0);
            True(b < a, "Score: W_order=60 trades 40s gap for +1 order");
            a = SaHadgsScoring.CandidateScore(1, 0.0, 0.0, 30.0, 0.0);
            b = SaHadgsScoring.CandidateScore(2, 40.0, 0.0, 30.0, 0.0);
            True(a < b, "Score: W_order=30 keeps continuity");
            True(SaHadgsScoring.CandidateScore(1, 0.0, 10.0, 60.0, 0.1) <
                 SaHadgsScoring.CandidateScore(1, 0.0, 50.0, 60.0, 0.1),
                 "Score: less travel wins ties");

            // --- RegretAssign: pods x bots score matrix -> pod->bot assignment ---
            // pod1 regret = 100-3=97 >> pod0 regret = 2-1=1 -> pod1 binds bot0 first.
            double[,] m = new double[2, 2];
            m[0, 0] = 1.0; m[0, 1] = 2.0;
            m[1, 0] = 3.0; m[1, 1] = 100.0;
            int[] asg = SaHadgsScoring.RegretAssign(m);
            True(asg[0] == 1 && asg[1] == 0, "Regret: avoids greedy trap (pod1 binds first)");
            double[,] m2 = new double[2, 1];
            m2[0, 0] = 1.0; m2[1, 0] = 2.0;
            True(SaHadgsScoring.RegretAssign(m2) == null, "Regret: pods>bots -> null");
            double[,] m3 = new double[1, 3];
            m3[0, 0] = 5.0; m3[0, 1] = 2.0; m3[0, 2] = 9.0;
            asg = SaHadgsScoring.RegretAssign(m3);
            True(asg[0] == 1, "Regret: single pod picks min score bot");

            Console.WriteLine(_fails == 0 ? "ALL PASS" : $"{_fails} FAILURES");
            return _fails == 0 ? 0 : 1;
        }
    }
}
