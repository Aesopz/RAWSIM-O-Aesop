using RAWSimO.Core.Helper;
using RAWSimO.Core.Info;
using RAWSimO.Core.Management;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RAWSimO.Core.Items
{
    /// <summary>
    /// Defines one order.
    /// </summary>
    public class Order : IOrderInfo
    {
        #region Constructors

        /// <summary>
        /// Creates a new instance of the order.
        /// </summary>
        internal Order() { TimeStampSubmit = double.PositiveInfinity; DueTime = double.PositiveInfinity; TimeStampCompleted = double.PositiveInfinity; }

        #endregion

        #region Core

        /// <summary>
        /// The overall demand quantity.
        /// </summary>
        private int _overallQuantity = 0;
        /// <summary>
        /// The quantities needed per order line.
        /// </summary>
        private Dictionary<ItemDescription, int> _quantities = new Dictionary<ItemDescription, int>();
        /// <summary>
        /// The requests belonging to the order lines.
        /// </summary>
        private Dictionary<ItemDescription, HashSet<ExtractRequest>> _requests = new Dictionary<ItemDescription, HashSet<ExtractRequest>>();
        /// <summary>
        /// The overall served demand quantity.
        /// </summary>
        private int _overallServedQuantity = 0;
        /// <summary>
        /// The quantities already fulfilled per order line.
        /// </summary>
        private Dictionary<ItemDescription, int> _servedQuantities = new Dictionary<ItemDescription, int>();
        /// <summary>
        /// The number of finished order lines.
        /// </summary>
        private int _servedPositions;
        /// <summary>
        /// The time this order is bore.
        /// </summary>
        public DateTime TimePlaced { get; set; }
        /// <summary>
        /// The time this order is placed.
        /// </summary>
        public double TimeStamp { get; set; }
        /// <summary>
        /// 离截止时间的时间
        /// </summary>
        public double Timestay { get; set; }
        /// <summary>
        /// 离截止时间的排序
        /// </summary>
        public double sequence { get; set; }
        /// <summary>
        /// Id of order.
        /// </summary>
        public int ID { get; set; }
        /// <summary>
        /// The time stamp this order was submitted to an output-station.
        /// </summary>
        public double TimeStampSubmit { get; set; }
        /// <summary>
        /// The time this order was submitted to a queue of a station.
        /// </summary>
        public double TimeStampQueued { get; set; }
        /// <summary>
        /// The time stamp at which the order should be completed.
        /// </summary>
        public double DueTime { get; set; }
        /// <summary>
        /// Enumerates all lines of the order.
        /// </summary>
        public IEnumerable<KeyValuePair<ItemDescription, int>> Positions { get { return _quantities; } }
        /// <summary>
        /// Gets the overall demand count of all lines of the order.
        /// </summary>
        /// <returns>The overall quantity necessary to fulfill this order.</returns>
        public int GetDemandCount() { return _overallQuantity; }
        /// <summary>
        /// Gets the overall open demand count of all lines of the order.
        /// </summary>
        /// <returns>The overall open demand of the order.</returns>
        public int GetOpenDemandCount() { return _overallQuantity - _overallServedQuantity; }
        /// <summary>
        /// Gets the given lines's demand count.
        /// </summary>
        /// <param name="item">The order line.</param>
        /// <returns>The demand of the line.</returns>
        public int GetDemandCount(ItemDescription item) { return _quantities.ContainsKey(item) ? _quantities[item] : 0; }
        /// <summary>
        /// Enumerates all requests for picking the different items.
        /// </summary>
        public IEnumerable<ExtractRequest> Requests { get { return _requests.SelectMany(i => i.Value); } }
        /// <summary>
        /// Adds a new line to the order.
        /// </summary>
        /// <param name="itemDescription">The item description of the line.</param>
        /// <param name="count">The quantity.</param>
        public void AddPosition(ItemDescription itemDescription, int count)
        {
            if (!_quantities.ContainsKey(itemDescription))
            {
                _quantities[itemDescription] = 0;
                _servedQuantities[itemDescription] = 0;
            }
            _quantities[itemDescription] += count;
            _overallQuantity += count;
        }
        /// <summary>
        /// Remove the request to pick the item to the order. This can be used by other components to see the particular requests' status.
        /// </summary>
        /// <param name="itemDescription">The SKU to add the request for.</param>
        /// <param name="request">The particular request to pick the item.</param>
        public void RemoveRequest(ItemDescription itemDescription, ExtractRequest request)
        { _requests[itemDescription].Remove(request); }
        /// <summary>
        /// Adds the request to pick the item to the order. This can be used by other components to see the particular requests' status.
        /// </summary>
        /// <param name="itemDescription">The SKU to add the request for.</param>
        /// <param name="request">The particular request to pick the item.</param>
        public void AddRequest(ItemDescription itemDescription, ExtractRequest request)
        { if (!_requests.ContainsKey(itemDescription)) _requests[itemDescription] = new HashSet<ExtractRequest>(); _requests[itemDescription].Add(request); }
        /// <summary>
        /// Adds the request to pick the item to the order. This can be used by other components to see the particular requests' status.
        /// </summary>
        /// <param name="itemDescription"></param>
        /// <param name="numofitemDescription"></param>
        /// <param name="listofrequest"></param>
        public void SupplementRequest(ItemDescription itemDescription, int numofitemDescription, HashSet<ExtractRequest> listofrequest)
        {
            if (_requests.ContainsKey(itemDescription))
            {
                if (numofitemDescription == listofrequest.Count || _requests[itemDescription].Count > listofrequest.Count)
                    _requests[itemDescription] = new HashSet<ExtractRequest>(listofrequest);
                else if (numofitemDescription > listofrequest.Count && _requests[itemDescription].Count == listofrequest.Count)
                {
                    foreach (var request in listofrequest)
                        _requests[itemDescription].Add(request);
                }
                else
                    throw new InvalidOperationException("Could not any request from the old _requests!");
            }
            else
                throw new InvalidOperationException("Could not any request from the old _requests!");
        }
        /// <summary>
        /// Returns the number of units needed to fulfill the given position.
        /// </summary>
        /// <param name="item">The SKU.</param>
        /// <returns>Number of units necessary to fulfill the position corresponding to the given SKU.</returns>
        public int PositionOverallCount(ItemDescription item) { return _quantities.ContainsKey(item) ? _quantities[item] : 0; }
        /// <summary>
        /// Returns the number of units picked so far for the given position.
        /// </summary>
        /// <param name="item">The SKU.</param>
        /// <returns>Number of units picked so far for the position corresponding to the given SKU.</returns>
        public int PositionServedCount(ItemDescription item) { return _servedQuantities.ContainsKey(item) ? _servedQuantities[item] : 0; }
        /// <summary>
        /// Fulfills one unit of this order.
        /// </summary>
        /// <param name="item">The physical unit used to fulfill a part of the order.</param>
        /// <returns>Indicates whether fulfilling one unit of the order was successful.</returns>
        public bool Serve(ItemDescription item)
        {
            _overallServedQuantity++;
            if (_servedQuantities[item] < _quantities[item])
            {
                _servedQuantities[item]++;
                if (_servedQuantities[item] >= _quantities[item])
                {
                    _servedPositions++;
                }
                return true;
            }
            else
                return false;
        }
        /// <summary>
        /// Checks whether the order is complete.
        /// </summary>
        /// <returns><code>true</code> if the order is complete, <code>false</code> otherwise.</returns>
        public bool IsCompleted() { return _servedPositions >= _quantities.Count; }

        /// <summary>
        /// The time picking of this order finished at a station (+inf while unfinished).
        /// </summary>
        public double TimeStampCompleted { get; set; }

        #region Split orders (order-splitting enabler; see docs/superpowers/specs/2026-07-02-order-splitting-consolidation-enabler-design.md)

        /// <summary>
        /// The parent order, if this order is a split child. <code>null</code> for normal orders and split parents.
        /// </summary>
        public Order Parent { get; private set; }
        /// <summary>
        /// The child orders this order was split into.
        /// </summary>
        private List<Order> _children = new List<Order>();
        /// <summary>
        /// The child orders this order was split into (empty for normal orders).
        /// </summary>
        public IReadOnlyList<Order> Children { get { return _children; } }
        /// <summary>
        /// The children that already completed picking (set for idempotent completion notifications).
        /// </summary>
        private HashSet<Order> _completedChildren = new HashSet<Order>();
        /// <summary>
        /// Demand ledger: quantities per SKU already claimed by children.
        /// </summary>
        private Dictionary<ItemDescription, int> _claimedQuantities = new Dictionary<ItemDescription, int>();
        /// <summary>
        /// Indicates whether this order was split into children.
        /// </summary>
        public bool IsSplitParent { get { return _children.Count > 0; } }
        /// <summary>
        /// The remaining (not yet claimed by any child) demand of the given SKU.
        /// </summary>
        public int GetRemainingDemand(ItemDescription item)
        { return GetDemandCount(item) - (_claimedQuantities.ContainsKey(item) ? _claimedQuantities[item] : 0); }
        /// <summary>
        /// Enumerates all positions with their remaining (unclaimed) quantities; positions without remainder are omitted.
        /// </summary>
        public IEnumerable<KeyValuePair<ItemDescription, int>> RemainingPositions
        {
            get
            {
                return _quantities
                    .Select(q => new KeyValuePair<ItemDescription, int>(q.Key, GetRemainingDemand(q.Key)))
                    .Where(q => q.Value > 0);
            }
        }
        /// <summary>
        /// Indicates whether the complete demand of this order was claimed by children.
        /// </summary>
        public bool IsFullyClaimed { get { return _quantities.All(q => GetRemainingDemand(q.Key) <= 0); } }
        /// <summary>
        /// Creates a split child of the given parent claiming the given quantities from the parent's demand ledger.
        /// Claiming and child creation happen atomically so no unit can be assigned twice.
        /// The child inherits the parent's timing meta-data.
        /// </summary>
        public static Order CreateSplitChild(Order parent, IEnumerable<KeyValuePair<ItemDescription, int>> quantities)
        {
            // Pass 1: validate everything (aggregating duplicate SKUs) before mutating any state,
            // so a failed split leaves the parent's ledger untouched.
            Dictionary<ItemDescription, int> aggregated = new Dictionary<ItemDescription, int>();
            int units = 0;
            foreach (var q in quantities)
            {
                if (q.Value <= 0)
                    throw new ArgumentException("Child quantities must be positive!");
                if (!aggregated.ContainsKey(q.Key))
                    aggregated[q.Key] = 0;
                aggregated[q.Key] += q.Value;
                if (aggregated[q.Key] > parent.GetRemainingDemand(q.Key))
                    throw new InvalidOperationException("Cannot claim more units than remaining for the SKU!");
                units += q.Value;
            }
            if (units == 0)
                throw new ArgumentException("Cannot create an empty split child!");
            // Pass 2: commit (cannot throw anymore) - build child and claim from the ledger.
            Order child = new Order();
            foreach (var q in aggregated)
            {
                child.AddPosition(q.Key, q.Value);
                if (!parent._claimedQuantities.ContainsKey(q.Key))
                    parent._claimedQuantities[q.Key] = 0;
                parent._claimedQuantities[q.Key] += q.Value;
            }
            child.Parent = parent;
            child.TimePlaced = parent.TimePlaced;
            child.TimeStamp = parent.TimeStamp;
            child.DueTime = parent.DueTime;
            parent._children.Add(child);
            return child;
        }
        /// <summary>
        /// Notifies this (parent) order that one of its children completed picking.
        /// </summary>
        /// <param name="child">The completed child.</param>
        /// <returns><code>true</code> if the parent is complete now (all demand claimed and all children done), <code>false</code> otherwise.</returns>
        public bool NotifyChildCompleted(Order child)
        {
            if (child.Parent != this)
                throw new InvalidOperationException("Order is not a child of this order!");
            // Idempotent: a duplicate notification for the same child does not advance state.
            _completedChildren.Add(child);
            return IsFullyClaimed && _completedChildren.Count >= _children.Count;
        }

        #endregion

        #endregion

        #region Inherited methods

        /// <summary>
        /// Returns a simple string giving information about the object.
        /// </summary>
        /// <returns>A simple string.</returns>
        public override string ToString() { return "Order-(" + string.Join(",", _quantities.Keys.Select(p => p.ToDescriptiveString())) + ")"; }

        #endregion

        #region Helper fields

        /// <summary>
        /// A helper tag that can be used for meta information purposes by other methods.
        /// </summary>
        internal bool HelperBoolTag;

        #endregion

        #region IOrderInfo Members

        /// <summary>
        /// Gets all positions of the order.
        /// </summary>
        /// <returns>An enumeration of all item-description in this order.</returns>
        public IEnumerable<IItemDescriptionInfo> GetInfoPositions() { return _quantities.Keys; }
        /// <summary>
        /// Gets the given position's quantity.
        /// </summary>
        /// <param name="item">The position.</param>
        /// <returns>The quantity of the position.</returns>
        public int GetInfoServedCount(IItemDescriptionInfo item) { return _servedQuantities[item as ItemDescription]; }
        /// <summary>
        /// Gets the given position's already served quantity.
        /// </summary>
        /// <param name="item">The position.</param>
        /// <returns>The already served quantity of the position.</returns>
        public int GetInfoDemandCount(IItemDescriptionInfo item) { return _quantities[item as ItemDescription]; }
        /// <summary>
        /// Indicates whether the order is already completed.
        /// </summary>
        /// <returns><code>true</code> if the order is completed, <code>false</code> otherwise.</returns>
        public bool GetInfoIsCompleted() { return IsCompleted(); }

        #endregion
    }
}
