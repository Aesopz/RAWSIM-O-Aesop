using RAWSimO.Core.Configurations;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Late-binding split manager shell - replaced wholesale by the full mirror in the next task.
    /// </summary>
    public class SplitM1GLBManager : SplitM1GExactManager
    {
        /// <summary>
        /// Creates a new instance of this controller.
        /// </summary>
        /// <param name="instance">The instance this controller belongs to.</param>
        public SplitM1GLBManager(Instance instance) : base(instance) { }
    }
}
