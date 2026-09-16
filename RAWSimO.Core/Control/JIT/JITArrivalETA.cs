using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Bots;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Waypoints;

namespace RAWSimO.Core.Control.JIT
{
    /// <summary>
    /// Time-budget breakdown for a JIT pre-pickup hold release decision.
    /// LiftTime is mechanical (not compressible by routing changes).
    /// IdealTravelTime is the no-conflict kinematic lower bound; apply
    /// congestion_factor to it (NOT to LiftTime) when adding a safety margin.
    /// </summary>
    public struct ArrivalBreakdown
    {
        /// <summary>Time to raise the pod before motion begins [s].</summary>
        public double LiftTime;
        /// <summary>Per-segment accel-cruise-decel + per-turn rotation time, no conflicts [s].</summary>
        public double IdealTravelTime;
        /// <summary>LiftTime + IdealTravelTime [s].</summary>
        public double Total => LiftTime + IdealTravelTime;
    }

    /// <summary>
    /// Composes lift + ideal travel time for a held bot heading from its pod cell
    /// to the rearmost queue waypoint of a target output station.
    /// Used by the JIT pre-pickup-hold scheduler to compute earliest release time.
    /// </summary>
    public static class JITArrivalETA
    {
        /// <summary>
        /// Source = pod.Waypoint (the bot waits under the pod, currently empty).
        /// Destination = station's physically rearmost queue waypoint = station.Queues[station.Waypoint].Last()
        ///               (the queue list is ordered nearest→farthest from the station).
        /// </summary>
        public static ArrivalBreakdown Compute(
            BotNormal bot,
            Pod pod,
            OutputStation station,
            Instance instance)
        {
            Waypoint src = pod.Waypoint;
            Waypoint dest = ResolveQueueRearWaypoint(station);

            List<Waypoint> path = instance.MetaInfoManager.ShortestPathManager
                .GetShortestPathNodes(src, dest, instance, emulatePodCarrying: true);

            double tTravel = IdealTravelTime.Compute(
                path,
                bot.Physics,
                instance.StraightOrientationTolerance);

            return new ArrivalBreakdown
            {
                LiftTime = bot.PodTransferTime,
                IdealTravelTime = tTravel
            };
        }

        /// <summary>
        /// Rearmost waypoint of the station's queue lane.
        /// Per OutputStation.Queues XML doc: list is "starting with the nearest way point ending with the most far away one".
        /// Falls back to station.Waypoint if no queue is defined (degenerate layout).
        /// </summary>
        public static Waypoint ResolveQueueRearWaypoint(OutputStation station)
        {
            if (station.Queues == null || station.Queues.Count == 0)
                return station.Waypoint;
            if (!station.Queues.TryGetValue(station.Waypoint, out List<Waypoint> queue) || queue == null || queue.Count == 0)
                return station.Waypoint;
            return queue.Last();
        }
    }
}
