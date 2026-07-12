using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Deferred-binding ledger for the late-binding split manager (SplitM1GLB): holds
    /// planned-but-unbound order allocations keyed by order, carrying the full supplier-pod
    /// set so an order binds when the FIRST of its supplier pods reaches request resolution
    /// (GetPossibleRequestsofMP consumes _Ziops destructively, so binding must precede any
    /// supplier's resolution). Pure data structure with no Instance dependency - generic so
    /// unit tests can drive it with plain strings.
    /// See docs/superpowers/specs/2026-07-12-m2e-lb-design.md.
    /// </summary>
    public class LbLedger<TOrder, TStation, TPod>
    {
        private class Entry
        {
            public TOrder Order;
            public TStation Station;
            public HashSet<TPod> Suppliers;
            public double PlannedAt;
        }

        private readonly Dictionary<TOrder, Entry> _entries = new Dictionary<TOrder, Entry>();
        private readonly Dictionary<TStation, int> _plannedPerStation = new Dictionary<TStation, int>();

        /// <summary>Number of ledgered (planned, unbound) orders for the station.</summary>
        public int PlannedCount(TStation station)
        {
            int n;
            return _plannedPerStation.TryGetValue(station, out n) ? n : 0;
        }

        /// <summary>Total ledgered orders (diagnostics).</summary>
        public int Count { get { return _entries.Count; } }

        /// <summary>Ledgers a planned allocation. Throws on duplicate order (planning must not double-claim).</summary>
        public void Add(TOrder order, TStation station, IEnumerable<TPod> suppliers, double plannedAt)
        {
            _entries.Add(order, new Entry() { Order = order, Station = station, Suppliers = new HashSet<TPod>(suppliers), PlannedAt = plannedAt });
            int n;
            _plannedPerStation.TryGetValue(station, out n);
            _plannedPerStation[station] = n + 1;
        }

        /// <summary>All ledgered orders of the station supplied (at least partly) by the pod, oldest plan first.</summary>
        public List<TOrder> OrdersFor(TPod pod, TStation station)
        {
            return _entries.Values
                .Where(e => e.Station.Equals(station) && e.Suppliers.Contains(pod))
                .OrderBy(e => e.PlannedAt)
                .Select(e => e.Order).ToList();
        }

        /// <summary>Station the order was planned for, or default(TStation) when not ledgered.</summary>
        public TStation StationOf(TOrder order)
        {
            Entry e;
            return _entries.TryGetValue(order, out e) ? e.Station : default(TStation);
        }

        /// <summary>Planning timestamp of the order (PositiveInfinity when not ledgered).</summary>
        public double PlannedAt(TOrder order)
        {
            Entry e;
            return _entries.TryGetValue(order, out e) ? e.PlannedAt : double.PositiveInfinity;
        }

        /// <summary>True when the order is ledgered (planned, unbound).</summary>
        public bool Contains(TOrder order) { return _entries.ContainsKey(order); }

        /// <summary>Removes the order (after binding). No-op when absent.</summary>
        public void Remove(TOrder order)
        {
            Entry e;
            if (!_entries.TryGetValue(order, out e))
                return;
            _entries.Remove(order);
            _plannedPerStation[e.Station] = _plannedPerStation[e.Station] - 1;
        }

        /// <summary>Orders ledgered longer than timeout (watchdog candidates), oldest first.</summary>
        public List<TOrder> DueOrders(double now, double timeout)
        {
            return _entries.Values
                .Where(e => now - e.PlannedAt > timeout)
                .OrderBy(e => e.PlannedAt)
                .Select(e => e.Order).ToList();
        }

        /// <summary>
        /// W-capacity helper: remaining plannable in-flight slots for a station
        /// = W - bound-in-use - bound-reserved - planned-unbound, floored at 0.
        /// </summary>
        public static int RemainingCapacity(int plannedWipCap, int capacityInUse, int capacityReserved, int planned)
        {
            int remaining = plannedWipCap - capacityInUse - capacityReserved - planned;
            return remaining > 0 ? remaining : 0;
        }
    }
}
