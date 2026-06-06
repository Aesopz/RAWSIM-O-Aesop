using RAWSimO.Core.Elements;

namespace RAWSimO.Core.Control
{
    /// <summary>
    /// Pure, side-effect-free cost helpers for station-starve-aware order/pod/task allocation.
    /// Converts distance to travel time and computes the starvation delay penalty
    /// (delay = TA travel time - station EST). See
    /// docs/superpowers/specs/2026-06-06-m1g-hadgs-station-starve-aware-design.md.
    /// </summary>
    public static class StarveAwareCost
    {
        /// <summary>Distance [m] -> travel time [s]. nominalSpeed [m/s]; if &lt;= 0 returns
        /// the distance unchanged (identity fallback, lets callers degrade safely).</summary>
        public static double TravelTime(double distance, double nominalSpeed)
        {
            return nominalSpeed > 0.0 ? distance / nominalSpeed : distance;
        }

        /// <summary>Piecewise starvation delay penalty. delay = taCost - est.
        /// delay &gt;= 0 (pod cannot reach station before it goes idle) -> penalty = delay.
        /// delay &lt; 0 (pod can ideally arrive in time, ideal lower bound) -> penalty = fixedParam.</summary>
        public static double DelayPenalty(double taCost, double est, double fixedParam)
        {
            double delay = taCost - est;
            return delay >= 0.0 ? delay : fixedParam;
        }

        /// <summary>Station EST [s] — projected seconds until the station next goes idle,
        /// from the existing pipeline projection. Thin read-only wrapper.</summary>
        public static double Est(OutputStation station, double now)
        {
            return SlowStartController.ComputeStationWorkProjection(station, now).FirstStarveSec;
        }
    }
}
