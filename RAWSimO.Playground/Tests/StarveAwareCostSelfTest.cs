using System;
using RAWSimO.Core.Control;

namespace RAWSimO.Playground.Tests
{
    /// <summary>Runnable red-green assertions for StarveAwareCost pure functions.</summary>
    public static class StarveAwareCostSelfTest
    {
        private static int _fails = 0;

        private static void Near(double actual, double expected, string name, double tol = 1e-6)
        {
            bool ok = Math.Abs(actual - expected) <= tol;
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + $"  (got {actual}, want {expected})");
            if (!ok) _fails++;
        }

        public static int RunAll()
        {
            _fails = 0;

            // TravelTime: distance / speed
            Near(StarveAwareCost.TravelTime(30.0, 1.5), 20.0, "TravelTime: 30m / 1.5 = 20s");
            // TravelTime: non-positive speed falls back to identity (distance unchanged)
            Near(StarveAwareCost.TravelTime(30.0, 0.0), 30.0, "TravelTime: speed<=0 -> identity");
            // TravelTime: negative speed also takes the identity branch (not an error)
            Near(StarveAwareCost.TravelTime(30.0, -1.0), 30.0, "TravelTime: speed<0 -> identity");

            // DelayPenalty: delay >= 0 -> delay (pod late vs starve horizon)
            Near(StarveAwareCost.DelayPenalty(taCost: 50, est: 30, fixedParam: 5), 20.0, "DelayPenalty: late -> delay");
            // DelayPenalty: delay == 0 boundary -> delay (0)
            Near(StarveAwareCost.DelayPenalty(taCost: 30, est: 30, fixedParam: 5), 0.0, "DelayPenalty: boundary -> 0");
            // DelayPenalty: delay < 0 (pod in time) -> fixed floor
            Near(StarveAwareCost.DelayPenalty(taCost: 10, est: 30, fixedParam: 5), 5.0, "DelayPenalty: in-time -> fixedParam");

            Console.WriteLine(_fails == 0 ? "ALL PASS" : $"{_fails} FAILURES");
            return _fails == 0 ? 0 : 1;
        }
    }
}
