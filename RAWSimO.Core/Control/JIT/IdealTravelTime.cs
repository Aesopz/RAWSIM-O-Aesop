using System;
using System.Collections.Generic;
using RAWSimO.Core.Geometrics;
using RAWSimO.Core.Waypoints;
using RAWSimO.MultiAgentPathFinding.Physic;

namespace RAWSimO.Core.Control.JIT
{
    /// <summary>
    /// Computes kinematically-correct ideal travel time for a waypoint path.
    /// Mirrors SpaceTimeAStar reconstruction segmentation:
    ///   - "Stop" = any node whose in-edge → out-edge orientation change ≥ straightOrientationTolerance.
    ///   - Each stop-to-stop run is ONE accel-cruise-decel segment via Physics.getTimeNeededToMove(0, segLen).
    ///   - Each in-place turn between segments adds Physics.getTimeNeededToTurn(prevOri, nextOri).
    /// Bot is assumed at rest at path[0] and at rest at path[Last].
    /// </summary>
    public static class IdealTravelTime
    {
        /// <param name="path">Waypoint sequence; must contain ≥ 2 nodes for non-zero result.</param>
        /// <param name="physics">Bot kinematics (a, d, vMax, TurnSpeed).</param>
        /// <param name="straightOrientationTolerance">Radians; below this the bot does not stop at the node.</param>
        /// <param name="initialOrientation">Bot's current orientation [rad]. If finite and differs from the first
        /// edge orientation by ≥ straightOrientationTolerance, an initial in-place turn is added. Default NaN = skip.</param>
        /// <returns>Total ideal travel time [s]. Returns 0 for null/short paths.</returns>
        public static double Compute(
            IReadOnlyList<Waypoint> path,
            Physics physics,
            double straightOrientationTolerance,
            double initialOrientation = double.NaN)
        {
            if (path == null || path.Count < 2 || physics == null)
                return 0.0;

            double totalTime = 0.0;
            double segmentLength = path[0].GetDistance(path[1]);
            double prevEdgeOri = Circle.GetOrientation(path[0].X, path[0].Y, path[1].X, path[1].Y);

            // Initial in-place turn from current bot orientation to first edge direction.
            if (!double.IsNaN(initialOrientation))
            {
                double startDiff = Math.Abs(Circle.GetOrientationDifference(initialOrientation, prevEdgeOri));
                if (startDiff >= straightOrientationTolerance)
                    totalTime += physics.getTimeNeededToTurn(initialOrientation, prevEdgeOri);
            }

            for (int i = 1; i < path.Count - 1; i++)
            {
                double nextEdgeOri = Circle.GetOrientation(path[i].X, path[i].Y, path[i + 1].X, path[i + 1].Y);
                double oriDiff = Math.Abs(Circle.GetOrientationDifference(prevEdgeOri, nextEdgeOri));

                if (oriDiff >= straightOrientationTolerance)
                {
                    // Stop at path[i]: close the current segment (drive to rest) + turn in place.
                    totalTime += physics.getTimeNeededToMove(0.0, segmentLength);
                    totalTime += physics.getTimeNeededToTurn(prevEdgeOri, nextEdgeOri);
                    segmentLength = 0.0;
                    prevEdgeOri = nextEdgeOri;
                }
                // else: straight enough → no stop, accumulate into the running segment.

                segmentLength += path[i].GetDistance(path[i + 1]);
            }

            // Final segment to destination (always ends at rest).
            totalTime += physics.getTimeNeededToMove(0.0, segmentLength);
            return totalTime;
        }

        /// <summary>
        /// Like Compute, but computes time to reach a CHECKPOINT waypoint somewhere along the path
        /// without forcing v=0 at that checkpoint — i.e., the bot continues past the checkpoint
        /// (the path final destination is further). Used when the JIT trip ends at queue.Last()
        /// (bbox entry) but the bot's actual BotMove target is station.Waypoint beyond it.
        /// The kinematics within the segment containing the checkpoint are integrated for the
        /// partial distance using Physics.getTimeNeededToMove(checkpointDist) on the FULL segment
        /// followed by linear interpolation over distance — exact for accel/cruise, approximate
        /// for decel-phase entry (rare since checkpoint is normally cruise/accel into next stop).
        /// </summary>
        /// <param name="path">Full waypoint sequence including the post-checkpoint segments.</param>
        /// <param name="checkpointIndex">Index of the checkpoint waypoint in path; time accumulated
        /// is the moment bot's geometric position reaches path[checkpointIndex].</param>
        public static double ComputeToCheckpoint(
            IReadOnlyList<Waypoint> path,
            int checkpointIndex,
            Physics physics,
            double straightOrientationTolerance,
            double initialOrientation = double.NaN)
        {
            if (path == null || path.Count < 2 || physics == null || checkpointIndex <= 0)
                return 0.0;
            if (checkpointIndex >= path.Count - 1)
                return Compute(path, physics, straightOrientationTolerance, initialOrientation);

            double totalTime = 0.0;
            double segmentLength = path[0].GetDistance(path[1]);
            double segmentStartDist = 0.0;  // cumulative distance at start of current segment
            double prevEdgeOri = Circle.GetOrientation(path[0].X, path[0].Y, path[1].X, path[1].Y);

            if (!double.IsNaN(initialOrientation))
            {
                double startDiff = Math.Abs(Circle.GetOrientationDifference(initialOrientation, prevEdgeOri));
                if (startDiff >= straightOrientationTolerance)
                    totalTime += physics.getTimeNeededToTurn(initialOrientation, prevEdgeOri);
            }

            // Track cumulative distance to know which segment the checkpoint falls in.
            // We need to identify: at which segment does the cumulative path distance equal
            // distance-to-checkpoint? Then return totalTime + partial-segment-time.
            double checkpointPathDist = 0.0;
            for (int k = 0; k < checkpointIndex; k++)
                checkpointPathDist += path[k].GetDistance(path[k + 1]);

            for (int i = 1; i < path.Count - 1; i++)
            {
                double nextEdgeOri = Circle.GetOrientation(path[i].X, path[i].Y, path[i + 1].X, path[i + 1].Y);
                double oriDiff = Math.Abs(Circle.GetOrientationDifference(prevEdgeOri, nextEdgeOri));

                if (oriDiff >= straightOrientationTolerance)
                {
                    // Segment ends at path[i]: close it (drive to rest).
                    double segEnd = segmentStartDist + segmentLength;
                    if (checkpointPathDist <= segEnd + 1e-9)
                    {
                        // Checkpoint falls inside this segment — compute partial time.
                        return totalTime + PartialSegmentTime(physics, segmentLength, checkpointPathDist - segmentStartDist);
                    }
                    totalTime += physics.getTimeNeededToMove(0.0, segmentLength);
                    totalTime += physics.getTimeNeededToTurn(prevEdgeOri, nextEdgeOri);
                    segmentStartDist = segEnd;
                    segmentLength = 0.0;
                    prevEdgeOri = nextEdgeOri;
                }
                segmentLength += path[i].GetDistance(path[i + 1]);
            }

            // Final segment to destination (always ends at rest).
            double finalEnd = segmentStartDist + segmentLength;
            if (checkpointPathDist <= finalEnd + 1e-9)
                return totalTime + PartialSegmentTime(physics, segmentLength, checkpointPathDist - segmentStartDist);
            // Should not happen (checkpoint past end), fall back to full time.
            return totalTime + physics.getTimeNeededToMove(0.0, segmentLength);
        }

        /// <summary>
        /// Time to traverse partial distance within a single accel-cruise-decel segment
        /// of total length segLen, starting at rest. Uses Physics.getTimeNeededToMove to
        /// populate kinematic phases, then integrates from rest to partialDist.
        /// </summary>
        private static double PartialSegmentTime(Physics physics, double segLen, double partialDist)
        {
            if (partialDist <= 0.0) return 0.0;
            if (partialDist >= segLen - 1e-9) return physics.getTimeNeededToMove(0.0, segLen);

            // Use single-checkpoint mechanism via overload that returns time at each checkpoint.
            // getTimeNeededToMove(currentSpeed, currentTime, distanceToDestination, checkPointDistances, out checkPointTimes)
            // populates checkPointTimes with absolute time at each checkpoint distance.
            var checkpoints = new List<double> { partialDist };
            List<double> times;
            physics.getTimeNeededToMove(0.0, 0.0, segLen, checkpoints, out times);
            return times[0];
        }
    }
}
