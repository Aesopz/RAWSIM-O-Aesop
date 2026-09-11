using System;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Integer-safe dominance weights for the pod-centric adaptive hierarchy:
    /// completions, processing-pod completed orders, selected-new-pod completed orders,
    /// fewer station parts, processing-pod units, urgency, and assigned units. Distance is
    /// optimized after these levels are locked.
    /// </summary>
    public static class M2eAdaptiveExactMath
    {
        public sealed class Weights
        {
            public long EfficientCompletion;
            public long Completion;
            public long FocusOrder;
            public long NewPodOrder;
            public long FocusUnit;
            public long Urgency;
            public long StationPart;
            public long AssignedUnit;
        }

        public static Weights BuildWeights(int maxOrders, int maxAssignedUnits,
            int maxStationParts, int maxUrgencyScore)
        {
            if (maxOrders < 0)
                throw new ArgumentOutOfRangeException(nameof(maxOrders));
            if (maxAssignedUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(maxAssignedUnits));
            if (maxStationParts < 0)
                throw new ArgumentOutOfRangeException(nameof(maxStationParts));
            if (maxUrgencyScore < 0)
                throw new ArgumentOutOfRangeException(nameof(maxUrgencyScore));

            long n = maxOrders;
            long m = maxAssignedUnits;
            long y = maxStationParts;
            long h = maxUrgencyScore;
            long assigned = 1;
            long focusUnit = checked(m + 1);
            long lowerFocusUnitRange = checked(m * focusUnit + m);
            long urgency = checked(lowerFocusUnitRange + 1);
            long lowerUrgencyRange = checked(h * urgency + lowerFocusUnitRange);
            long stationPart = checked(lowerUrgencyRange + 1);
            long lowerStationPartRange = checked(y * stationPart + lowerUrgencyRange);
            long newPodOrder = checked(lowerStationPartRange + 1);
            long lowerNewPodOrderRange = checked(n * newPodOrder + lowerStationPartRange);
            long focusOrder = checked(lowerNewPodOrderRange + 1);
            long lowerFocusOrderRange = checked(n * focusOrder + lowerNewPodOrderRange);
            long completion = checked(lowerFocusOrderRange + 1);
            long lowerCompletionRange = checked(n * completion + lowerFocusOrderRange);
            long efficientCompletion = checked(lowerCompletionRange + 1);

            return new Weights
            {
                EfficientCompletion = efficientCompletion,
                Completion = completion,
                FocusOrder = focusOrder,
                NewPodOrder = newPodOrder,
                FocusUnit = focusUnit,
                Urgency = urgency,
                StationPart = stationPart,
                AssignedUnit = assigned
            };
        }

        public static long Score(int efficientCompletions, int completions, int focusOrders, int focusUnits,
            int urgencyScore, int stationParts,
            int assignedUnits, Weights weights)
        {
            return Score(efficientCompletions, completions, focusOrders, 0, focusUnits,
                urgencyScore, stationParts, assignedUnits, weights);
        }

        public static long Score(int efficientCompletions, int completions, int focusOrders,
            int newPodOrders, int focusUnits, int urgencyScore, int stationParts,
            int assignedUnits, Weights weights)
        {
            if (weights == null)
                throw new ArgumentNullException(nameof(weights));
            return checked(
                efficientCompletions * weights.EfficientCompletion
                + completions * weights.Completion
                + focusOrders * weights.FocusOrder
                + newPodOrders * weights.NewPodOrder
                + focusUnits * weights.FocusUnit
                + urgencyScore * weights.Urgency
                - stationParts * weights.StationPart
                + assignedUnits * weights.AssignedUnit);
        }

        /// <summary>
        /// Keeps one future pod in the station pipeline. Pods already at the pick waypoint
        /// are current work, not future supply, so they do not suppress replenishment.
        /// </summary>
        public static bool NeedsFuturePod(int inboundPods, int podsPhysicallyAtStation, int targetFuturePods = 1)
        {
            if (inboundPods < 0)
                throw new ArgumentOutOfRangeException(nameof(inboundPods));
            if (podsPhysicallyAtStation < 0 || podsPhysicallyAtStation > inboundPods)
                throw new ArgumentOutOfRangeException(nameof(podsPhysicallyAtStation));
            if (targetFuturePods < 1)
                throw new ArgumentOutOfRangeException(nameof(targetFuturePods));
            return inboundPods - podsPhysicallyAtStation < targetFuturePods;
        }

        /// <summary>
        /// Lexicographic PS comparison: completed-order value, relevant item coverage,
        /// travel distance, then deterministic pod id.
        /// </summary>
        public static bool IsBetterPodCandidate(int completions, int coverage, double distance, int podId,
            int bestCompletions, int bestCoverage, double bestDistance, int bestPodId)
        {
            return completions > bestCompletions
                || (completions == bestCompletions && coverage > bestCoverage)
                || (completions == bestCompletions && coverage == bestCoverage
                    && distance < bestDistance - 1e-9)
                || (completions == bestCompletions && coverage == bestCoverage
                    && Math.Abs(distance - bestDistance) <= 1e-9 && podId < bestPodId);
        }
    }
}
