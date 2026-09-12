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

        /// <summary>
        /// (M2e-IC / D16) Whether OutputStation should wake the order manager when an
        /// extract task reaches its last pick. Only the IC family wants this; it used to be
        /// inferred from "a buffer exists at all", which silently became wrong once other
        /// managers started creating buffers of their own. Default false, so a buffer is
        /// pure bookkeeping unless its owner asks for the wake-up.
        /// </summary>
        public bool WakeOnLastPick { get; set; }
        private readonly HashSet<int> _aliveParentIds = new HashSet<int>();
        /// <summary>Number of split parents currently holding a box (B_occ).</summary>
        public int AliveParentCount { get { return _aliveParentIds.Count; } }
        /// <summary>Reserves a box for a parent (idempotent). True if newly reserved.</summary>
        /// <param name="orderId">The parent order's ID.</param>
        public bool RegisterParent(int orderId) { return _aliveParentIds.Add(orderId); }
        /// <summary>Whether this parent currently holds a box. The buffer is the single
        /// source of truth for that: it is filled by whichever manager owns it and drained
        /// by the engine at consolidation, so a manager-side mirror would only be able to
        /// disagree with it.</summary>
        /// <param name="orderId">The parent order's ID.</param>
        public bool Holds(int orderId) { return _aliveParentIds.Contains(orderId); }

        /// <summary>Releases the parent's box at consolidation (idempotent, unknown-safe).</summary>
        /// <param name="orderId">The parent order's ID.</param>
        public bool ReleaseParent(int orderId) { return _aliveParentIds.Remove(orderId); }
    }
}
