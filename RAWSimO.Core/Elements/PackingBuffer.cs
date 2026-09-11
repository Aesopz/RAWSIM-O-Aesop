using System.Collections.Generic;

namespace RAWSimO.Core.Elements
{
    /// <summary>
    /// Global downstream consolidation buffer for split orders (Xie et al. 2021, Appendix
    /// B: 78 boxes per shelf). ONE box per SPLIT PARENT order: reserved when the parent is
    /// first split (decode time - conservative, earlier than the physical first-part
    /// arrival, so the budget can never overshoot), released when the parent consolidates.
    /// Capacity &lt;= 0 = unlimited (probe-tracking only). Null on Instance unless an
    /// IC-family order manager instantiates it - every other manager is untouched.
    /// </summary>
    public class PackingBuffer
    {
        /// <summary>Creates the buffer with the given box capacity (&lt;= 0 = unlimited).</summary>
        /// <param name="capacity">Total box count C.</param>
        public PackingBuffer(int capacity) { Capacity = capacity; }
        /// <summary>Total box count C; &lt;= 0 means unlimited.</summary>
        public int Capacity { get; private set; }
        private readonly HashSet<int> _aliveParentIds = new HashSet<int>();
        /// <summary>Number of split parents currently holding a box (B_occ).</summary>
        public int AliveParentCount { get { return _aliveParentIds.Count; } }
        /// <summary>Reserves a box for a parent (idempotent). True if newly reserved.</summary>
        /// <param name="orderId">The parent order's ID.</param>
        public bool RegisterParent(int orderId) { return _aliveParentIds.Add(orderId); }
        /// <summary>Releases the parent's box at consolidation (idempotent, unknown-safe).</summary>
        /// <param name="orderId">The parent order's ID.</param>
        public bool ReleaseParent(int orderId) { return _aliveParentIds.Remove(orderId); }
    }
}
