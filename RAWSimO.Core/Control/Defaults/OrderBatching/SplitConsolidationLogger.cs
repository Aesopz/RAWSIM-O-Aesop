using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Shared consolidation logger for order-splitting managers: writes one CSV row
    /// (splitorders.csv in the statistics directory) per completed split parent.
    /// Extracted verbatim from SplitOrderManager so SplitM1GManager can reuse it.
    /// </summary>
    public class SplitConsolidationLogger
    {
        /// <summary>
        /// Creates a new logger bound to the given instance.
        /// </summary>
        /// <param name="instance">The instance whose statistics directory receives the CSV.</param>
        public SplitConsolidationLogger(Instance instance) { _instance = instance; }

        private Instance _instance;

        /// <summary>
        /// Lazily opened CSV logging one row per completed split parent (consolidation detail).
        /// Same location pattern as the M1G decision log.
        /// </summary>
        private System.IO.StreamWriter _splitLog;

        /// <summary>
        /// Writes one CSV row when a split parent order completes (consolidation done).
        /// Signature matches the Instance.OrderCompleted event.
        /// </summary>
        public void LogParentCompleted(Order order, OutputStation station)
        {
            if (!order.IsSplitParent)
                return;
            if (_splitLog == null)
            {
                string dir = _instance != null && _instance.SettingConfig != null ? _instance.SettingConfig.StatisticsDirectory : null;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                _splitLog = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "splitorders.csv"), false) { AutoFlush = true };
                _splitLog.WriteLine("parent,units,children,firstChildDone,lastChildDone,consolidated,consolidationWait,placed,submitted");
            }
            double firstDone = System.Linq.Enumerable.Min(order.Children, c => c.TimeStampCompleted);
            double lastDone = System.Linq.Enumerable.Max(order.Children, c => c.TimeStampCompleted);
            _splitLog.WriteLine(string.Join(",", new string[] {
                order.ID.ToString(),
                order.GetDemandCount().ToString(),
                order.Children.Count.ToString(),
                firstDone.ToString(System.Globalization.CultureInfo.InvariantCulture),
                lastDone.ToString(System.Globalization.CultureInfo.InvariantCulture),
                order.TimeStampCompleted.ToString(System.Globalization.CultureInfo.InvariantCulture),
                (lastDone - firstDone).ToString(System.Globalization.CultureInfo.InvariantCulture),
                order.TimeStamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                order.TimeStampSubmit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }));
        }
    }
}
