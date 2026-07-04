using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.SolverWrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using static RAWSimO.Core.Management.ResourceManager;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// MILP-based order-splitting manager: M1G with the one-station-per-order constraint (shi2)
    /// relaxed to a unit-level quantity assignment q[o,i,s]. Reuses the Spec 1 enabler pipeline
    /// (CreateSplitChild / TransferExtractRequests / consolidation) to commit the solution.
    /// The original M1GManager stays untouched and serves as the M0 ablation baseline.
    /// See docs/superpowers/specs/2026-07-04-order-splitting-milp-design.md.
    /// </summary>
    public class SplitM1GManager : M1GManager
    {
        /// <summary>
        /// Creates a new instance of this manager.
        /// </summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public SplitM1GManager(Instance instance) : base(instance)
        {
            _splitConfig = instance.ControllerConfig.OrderBatchingConfig as SplitM1GConfiguration;
        }

        /// <summary>
        /// The split-specific config of this controller.
        /// </summary>
        private SplitM1GConfiguration _splitConfig;
    }
}
