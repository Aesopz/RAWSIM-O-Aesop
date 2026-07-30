using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// M4G - unit-level order splitting with M1G's valuation/binding separation restored.
    ///
    /// M1G keeps two order-assignment layers: yos (slot-free, rewarded in the objective,
    /// a pod valuation) and yaos (slot-limited, the only one actually bound). M3G collapsed
    /// them, so every scored unit had to occupy a currently free slot; splitting then only
    /// arose as a feasibility residual forced by the per-station new-pod cap. M4G restores
    /// the separation at unit level: q-hat values pods against the whole backlog, q binds
    /// only what fits the slots, and only q has side effects.
    ///
    /// Spec: docs/superpowers/specs/2026-07-31-m4g-valuation-binding-design.md
    /// Constitution: M1GManager / HADGSManager / SplitM2eICManager are never modified;
    /// this class mirrors what it needs rather than reaching into them.
    /// </summary>
    public class M4GManager : M1GManager
    {
        /// <summary>Creates a new instance of this manager.</summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public M4GManager(Instance instance) : base(instance)
        {
            _m4gConfig = instance.ControllerConfig.OrderBatchingConfig as M4GConfiguration;
            if (_m4gConfig == null)
                throw new InvalidOperationException("M4GManager requires an M4GConfiguration.");
            // Unit-consistency guard (spec 3.5): lambda/mu/epsilon are priced in metres, which
            // requires the objective's distance terms to be metres too. StarveAwareCostEnabled
            // switches the cost functions to seconds - fail hard rather than solve nonsense.
            if (instance.SettingConfig != null && instance.SettingConfig.StarveAwareCostEnabled)
                throw new InvalidOperationException(
                    "M4G prices lines in metres but StarveAwareCostEnabled makes the distance terms seconds. "
                    + "Set StarveAwareCostEnabled=false or disable M4G.");
            _pricing = new M4GPricing(_m4gConfig);
        }

        /// <summary>The M4G configuration of this manager.</summary>
        private M4GConfiguration _m4gConfig;
        /// <summary>Price calibration state (spec 3.5).</summary>
        private M4GPricing _pricing;
        /// <summary>Per-decision diagnostic log.</summary>
        private System.IO.StreamWriter _decisionLog;
        /// <summary>Running decision counter, also the probe cadence clock.</summary>
        private int _decisionIndex = 0;

        /// <summary>Lazily opens m4g_decision_log.csv in the run's statistics directory.</summary>
        private void EnsureDecisionLog()
        {
            if (_decisionLog != null) return;
            string dir = Instance != null && Instance.SettingConfig != null
                ? Instance.SettingConfig.StatisticsDirectory : null;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            _decisionLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "m4g_decision_log.csv"), false)
            { AutoFlush = true };
            _decisionLog.WriteLine("decision,time,solved,pendingOrders,stationsWithCap,podsPa,podsPb,botsRa,"
                + "lambda,mu,delta,epsilon,valuedLines,boundLines,valuedOrders,boundOrders,newTrips,boundUnits,"
                + "objective,solveSec");
        }

        /// <summary>Writes one decision row. Every numeric field is written unformatted for exact diffing.</summary>
        private void WriteDecision(bool solved, int pendingOrders, int stationsWithCap, int podsPa, int podsPb,
            int botsRa, double lambda, double mu, double delta, double epsilon, int valuedLines, int boundLines,
            int valuedOrders, int boundOrders, int newTrips, int boundUnits, double objective, double solveSec)
        {
            EnsureDecisionLog();
            _decisionLog.WriteLine(string.Join(",", new string[] {
                _decisionIndex.ToString(),
                Instance.Controller.CurrentTime.ToString(),
                (solved ? "1" : "0"),
                pendingOrders.ToString(), stationsWithCap.ToString(), podsPa.ToString(), podsPb.ToString(),
                botsRa.ToString(), lambda.ToString(), mu.ToString(), delta.ToString(), epsilon.ToString(),
                valuedLines.ToString(), boundLines.ToString(), valuedOrders.ToString(), boundOrders.ToString(),
                newTrips.ToString(), boundUnits.ToString(), objective.ToString(), solveSec.ToString() }));
        }
    }
}
