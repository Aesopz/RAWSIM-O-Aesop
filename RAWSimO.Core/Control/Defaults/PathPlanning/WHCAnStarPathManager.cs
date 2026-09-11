using RAWSimO.Core.Bots;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Helper;
using RAWSimO.Core.Interfaces;
using RAWSimO.Core.Waypoints;
using RAWSimO.MultiAgentPathFinding;
using RAWSimO.MultiAgentPathFinding.Algorithms.AStar;
using RAWSimO.MultiAgentPathFinding.DataStructures;
using RAWSimO.MultiAgentPathFinding.Elements;
using RAWSimO.MultiAgentPathFinding.Methods;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RAWSimO.Core.Control.Defaults.PathPlanning
{

    /// <summary>
    /// Controller of the bot.
    /// </summary>
    public class WHCAnStarPathManager : PathManager
    {
        // ── Probe diagnostics (process-wide, reset per simulation) ──────────
        public static int s_ProbeAttempts;            // EstimateReservationAwareEta entered
        public static int s_ProbeEarlyExit;           // bot/from/to null, method null, waypoint absent
        public static int s_ProbeFromEqualsTo;        // fromId == toId (returns 0.0, not NaN)
        public static int s_ProbeSelfListNull;        // GetBotReservations returned null
        public static int s_ProbeSelfListEmpty;       // returned non-null but Count==0
        public static int s_ProbeSelfLastEndNotInf;   // last entry not (·,t,+∞)
        public static int s_ProbeSelfLastNodeMismatch;// last.Node != fromId
        public static int s_ProbeSelfRemovedOk;       // self entry successfully removed
        public static int s_ProbeSearchSuccess;       // aStar.Search() returned true
        public static int s_ProbeSearchFalse;         // aStar.Search() returned false
        public static int s_ProbeGoalInvalid;         // GoalNode index out of range
        public static int s_ProbeArrivalInf;          // arrival was inf/NaN
        public static int s_ProbeException;           // caught exception
        // rev7d failure-mode localization
        public static int s_ProbeStartWaitOk;         // start cell IntersectionFree over [t, t+waitStep]
        public static int s_ProbeStartWaitBlocked;    // start cell already blocked at t
        public static int s_ProbeAnyMoveGenerated;    // post-search: ≥1 move successor (Edge!=null) was emitted
        public static int s_ProbeOnlyWaits;           // post-search: 0 move successors generated (pure wait chain)
        public static int s_ProbeGoalIsDest;          // GoalNode 2D == destination
        public static int s_ProbeGoalIsWindowExpiry;  // GoalNode set by NodeTime[n] >= window (success path but no dest)
        public static long s_ProbeMaxNodeTimeSumMs;   // sum of (max NodeTime - startTime)*1000, for average reach depth
        public static int s_ProbeMaxNodeTimeSamples;  // # search calls that produced any NodeTime entries
        public static void ResetProbeStats()
        {
            s_ProbeAttempts = s_ProbeEarlyExit = s_ProbeFromEqualsTo = 0;
            s_ProbeSelfListNull = s_ProbeSelfListEmpty = 0;
            s_ProbeSelfLastEndNotInf = s_ProbeSelfLastNodeMismatch = s_ProbeSelfRemovedOk = 0;
            s_ProbeSearchSuccess = s_ProbeSearchFalse = 0;
            s_ProbeGoalInvalid = s_ProbeArrivalInf = s_ProbeException = 0;
            s_ProbeStartWaitOk = s_ProbeStartWaitBlocked = 0;
            s_ProbeAnyMoveGenerated = s_ProbeOnlyWaits = 0;
            s_ProbeGoalIsDest = s_ProbeGoalIsWindowExpiry = 0;
            s_ProbeMaxNodeTimeSumMs = 0; s_ProbeMaxNodeTimeSamples = 0;
        }
        // ────────────────────────────────────────────────────────────────────


        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="instance">instance</param>
        public WHCAnStarPathManager(Instance instance)
            : base(instance)
        {
            //Need a Request on Fail
            BotNormal.RequestReoptimizationAfterFailingOfNextWaypointReservation = true;

            //translate to lightweight graph
            var graph = GenerateGraph();
            var config = instance.ControllerConfig.PathPlanningConfig as WHCAnStarPathPlanningConfiguration;

            PathFinder = new WHCAnStarMethod(graph, instance.SettingConfig.Seed, instance.Bots.Select(b => b.ID).ToList(), instance.Bots.Select(b => _waypointIds[instance.WaypointGraph.GetClosestWaypoint(b.Tier, b.X, b.Y)]).ToList(), new PathPlanningCommunicator(
                instance.LogSevere,
                instance.LogDefault,
                instance.LogInfo,
                instance.LogVerbose,
                () => { instance.StatOverallPathPlanningTimeouts++; }));
            var method = PathFinder as WHCAnStarMethod;
            method.LengthOfAWaitStep = config.LengthOfAWaitStep;
            method.RuntimeLimitPerAgent = config.RuntimeLimitPerAgent;
            method.RunTimeLimitOverall = config.RunTimeLimitOverall;
            method.LengthOfAWindow = config.LengthOfAWindow;
            method.UseBias = config.UseBias;
            method.UseDeadlockHandler = config.UseDeadlockHandler;
            method.UseRulePriority = config is WHCAnStarPriorityPathPlanningConfiguration;

            if (config.AutoSetParameter)
            {
                //best parameter determined my master thesis
                method.LengthOfAWindow = 15;
                method.UseBias = false;
                method.RuntimeLimitPerAgent = config.Clocking / instance.Bots.Count;
                method.RunTimeLimitOverall = config.Clocking;
            }
        }

        /// <summary>
        /// SpaceTimeAStar dry-run probe. Builds a transient Agent stub and a fresh
        /// (RRAStar, SpaceTimeAStar) pair against the shared reservation table, calls
        /// Search() only, and reads NodeTime[GoalNode]. Never invokes GetPathAndReservations,
        /// so the reservation table is not mutated.
        /// </summary>
        public override double EstimateReservationAwareEta(
            BotNormal bot, Waypoint from, Waypoint to,
            double startTime, double startOrientationRad)
        {
            System.Threading.Interlocked.Increment(ref s_ProbeAttempts);
            if (bot == null || from == null || to == null) { System.Threading.Interlocked.Increment(ref s_ProbeEarlyExit); return double.NaN; }
            var method = PathFinder as WHCAnStarMethod;
            if (method == null) { System.Threading.Interlocked.Increment(ref s_ProbeEarlyExit); return double.NaN; }

            if (!_waypointIds.ValuesFirst.Contains(from) || !_waypointIds.ValuesFirst.Contains(to))
            { System.Threading.Interlocked.Increment(ref s_ProbeEarlyExit); return double.NaN; }

            int fromId = _waypointIds[from];
            int toId   = _waypointIds[to];
            if (fromId == toId) { System.Threading.Interlocked.Increment(ref s_ProbeFromEqualsTo); return 0.0; }

            var stub = new Agent
            {
                ID = bot.ID,
                NextNode = fromId,
                ReservationsToNextNode = new System.Collections.Generic.List<MultiAgentPathFinding.DataStructures.ReservationTable.Interval>(),
                ArrivalTimeAtNextNode = startTime,
                OrientationAtNextNode = startOrientationRad,
                DestinationNode = toId,
                FinalDestinationNode = toId,
                FixedPosition = false,
                Resting = false,
                CanGoThroughObstacles = false,
                Physics = bot.Physics,
                RequestReoptimization = false,
                Queueing = false,
                NextNodeObject = from,
                DestinationNodeObject = to,
            };

            // Targeted self-reservation skip: remove ONLY the bot's last reservation
            // — the [t, ∞) blocking interval at its current cell — so the stub agent
            // is not poisoned by its own static reservation when computing wait/move
            // successors. This is safe because:
            //   (1) The last entry of _calculatedReservations is always the bot's
            //       "block here forever" interval at its CurrentWaypoint, by WHCAnMethod
            //       construction (line 240-242 of WHCAnMethod.cs).
            //   (2) WHCAnMethod line 213-214 only prunes entries whose Node ==
            //       agent.NextNode of bots being planned. Since this bot is currently
            //       in slow-start hold (IsQueueing=true) AND its pod cell is re-claimed
            //       (no other dispatch targets it), no other planner will set
            //       NextNode == this cell, so the last interval is guaranteed in-table.
            //   (3) Probe is read-only (does NOT call GetPathAndReservations); the
            //       table is restored exactly to its pre-probe state.
            ReservationTable.Interval restoreInterval = null;
            var selfList = method.GetBotReservations(bot.ID);
            if (selfList == null) System.Threading.Interlocked.Increment(ref s_ProbeSelfListNull);
            else if (selfList.Count == 0) System.Threading.Interlocked.Increment(ref s_ProbeSelfListEmpty);
            else
            {
                var last = selfList[selfList.Count - 1];
                if (!double.IsPositiveInfinity(last.End))
                    System.Threading.Interlocked.Increment(ref s_ProbeSelfLastEndNotInf);
                else if (last.Node != fromId)
                    System.Threading.Interlocked.Increment(ref s_ProbeSelfLastNodeMismatch);
                else
                {
                    method._reservationTable.Remove(last);
                    restoreInterval = last;
                    System.Threading.Interlocked.Increment(ref s_ProbeSelfRemovedOk);
                }
            }

            // Probe uses a deliberately LONGER horizon (3× the planning window) so the
            // A* search can navigate the often-busy aisle near pod storage cells, where
            // the bot must wait for a brief opening in others' reservations. The planning
            // engine itself still uses its configured shorter window.
            double probeWindow = System.Math.Max(method.LengthOfAWindow, 30.0) * 3.0;

            // rev7d-A: pre-check whether the start cell admits the FIRST wait successor
            // SpaceTimeAStar would generate (line 314 of SpaceTimeAStar.cs). If this is
            // blocked, the search can still try immediate moves but loses the wait-then-go
            // tactic — which is precisely what slow-start expects to work in dense scenes.
            if (method._reservationTable.IntersectionFree(fromId, startTime, startTime + method.LengthOfAWaitStep))
                System.Threading.Interlocked.Increment(ref s_ProbeStartWaitOk);
            else
                System.Threading.Interlocked.Increment(ref s_ProbeStartWaitBlocked);

            try
            {
                var rraStar = new ReverseResumableAStar(method.Graph, stub, stub.Physics, stub.DestinationNode);
                var aStar = new SpaceTimeAStar(method.Graph, method.LengthOfAWaitStep,
                    startTime + probeWindow, method._reservationTable, stub, rraStar);

                bool searchOk = aStar.Search();

                // rev7d post-search instrumentation: classify what the A* actually explored.
                if (aStar.NodeBackpointerEdge != null && aStar.NodeBackpointerEdge.Count > 0)
                {
                    bool anyMove = false;
                    for (int i = 0; i < aStar.NodeBackpointerEdge.Count; i++)
                        if (aStar.NodeBackpointerEdge[i] != null) { anyMove = true; break; }
                    if (anyMove) System.Threading.Interlocked.Increment(ref s_ProbeAnyMoveGenerated);
                    else System.Threading.Interlocked.Increment(ref s_ProbeOnlyWaits);
                }
                if (aStar.NodeTime != null && aStar.NodeTime.Count > 0)
                {
                    double maxT = startTime;
                    for (int i = 0; i < aStar.NodeTime.Count; i++)
                        if (aStar.NodeTime[i] > maxT) maxT = aStar.NodeTime[i];
                    System.Threading.Interlocked.Add(ref s_ProbeMaxNodeTimeSumMs, (long)((maxT - startTime) * 1000.0));
                    System.Threading.Interlocked.Increment(ref s_ProbeMaxNodeTimeSamples);
                }

                if (!searchOk) { System.Threading.Interlocked.Increment(ref s_ProbeSearchFalse); return double.NaN; }
                if (aStar.GoalNode < 0 || aStar.GoalNode >= aStar.NodeTime.Count) { System.Threading.Interlocked.Increment(ref s_ProbeGoalInvalid); return double.NaN; }

                // Classify GoalNode: was it the destination cell, or just window-expiry?
                // Map node-id back to 2D using the same NodeBackpointerEdge convention:
                // we walk back to find the 2D cell of the goal node.
                int goal2D = ResolveGoal2DNode(aStar, fromId);
                if (goal2D == toId) System.Threading.Interlocked.Increment(ref s_ProbeGoalIsDest);
                else System.Threading.Interlocked.Increment(ref s_ProbeGoalIsWindowExpiry);

                double arrival = aStar.NodeTime[aStar.GoalNode];
                if (double.IsInfinity(arrival) || double.IsNaN(arrival)) { System.Threading.Interlocked.Increment(ref s_ProbeArrivalInf); return double.NaN; }
                System.Threading.Interlocked.Increment(ref s_ProbeSearchSuccess);
                return System.Math.Max(0.0, arrival - startTime);
            }
            catch
            {
                System.Threading.Interlocked.Increment(ref s_ProbeException);
                return double.NaN;
            }
            finally
            {
                if (restoreInterval != null)
                {
                    try { method._reservationTable.Add(restoreInterval); }
                    catch { /* unexpected; planner will resync next FindPaths */ }
                }
            }
        }

        /// <summary>
        /// Ideal kinematic ETA — uses the SAME SpaceTimeAStar machinery as the planner
        /// (same Graph, Physics, edge directionality), but with an EMPTY reservation table.
        /// No multi-bot conflict, no wait actions. Returns the pure kinematic time
        /// from `from` to `to` consistent with what WHCA*n would produce in a clear table.
        ///
        /// This is the SlowStart-preferred ETA: it matches WHCA*n's path choice (single-
        /// direction aisles, turn penalties, accel-cruise-decel) but ignores conflicts.
        /// </summary>
        public override double EstimateIdealKinematicEta(
            BotNormal bot, Waypoint from, Waypoint to,
            double startTime, double startOrientationRad)
        {
            if (bot == null || from == null || to == null) return double.NaN;
            var method = PathFinder as WHCAnStarMethod;
            if (method == null) return double.NaN;
            if (!_waypointIds.ValuesFirst.Contains(from) || !_waypointIds.ValuesFirst.Contains(to))
                return double.NaN;

            int fromId = _waypointIds[from];
            int toId   = _waypointIds[to];
            if (fromId == toId) return 0.0;

            var stub = new Agent
            {
                ID = bot.ID,
                NextNode = fromId,
                ReservationsToNextNode = new System.Collections.Generic.List<ReservationTable.Interval>(),
                ArrivalTimeAtNextNode = startTime,
                OrientationAtNextNode = startOrientationRad,
                DestinationNode = toId,
                FinalDestinationNode = toId,
                FixedPosition = false,
                Resting = false,
                CanGoThroughObstacles = false,
                Physics = bot.Physics,
                RequestReoptimization = false,
                Queueing = false,
                NextNodeObject = from,
                DestinationNodeObject = to,
            };

            // Empty reservation table — no other bot's reservations interfere.
            var emptyTable = new ReservationTable(method.Graph, true);
            // Generous window so even long no-conflict paths fit.
            double window = System.Math.Max(method.LengthOfAWindow, 30.0) * 5.0;

            try
            {
                var rraStar = new ReverseResumableAStar(method.Graph, stub, stub.Physics, stub.DestinationNode);
                var aStar = new SpaceTimeAStar(method.Graph, method.LengthOfAWaitStep,
                    startTime + window, emptyTable, stub, rraStar);
                if (!aStar.Search()) return double.NaN;
                if (aStar.GoalNode < 0 || aStar.GoalNode >= aStar.NodeTime.Count) return double.NaN;
                double arrival = aStar.NodeTime[aStar.GoalNode];
                if (double.IsInfinity(arrival) || double.IsNaN(arrival)) return double.NaN;
                return System.Math.Max(0.0, arrival - startTime);
            }
            catch { return double.NaN; }
        }

        /// <summary>
        /// Resolve the 2D graph node id of the goal in a SpaceTimeAStar result by walking
        /// the backpointer chain until we find a move edge (Edge != null), whose To gives
        /// the 2D cell. If the chain is all waits, the goal is still at the start cell.
        /// </summary>
        private static int ResolveGoal2DNode(SpaceTimeAStar aStar, int startCellId)
        {
            int n = aStar.GoalNode;
            int guard = 0;
            while (n >= 0 && n < aStar.NodeBackpointerEdge.Count && guard++ < 100000)
            {
                var edge = aStar.NodeBackpointerEdge[n];
                if (edge != null) return edge.To;
                int parent = aStar.NodeBackpointerId[n];
                if (parent == n || parent < 0) break;
                n = parent;
            }
            return startCellId;
        }

        /// <summary>
        /// Per-tick lookahead clearance check used by slow-start hold. Greedy Manhattan
        /// walk from bot's current waypoint toward `to`, lookAheadCells steps. Each
        /// visited cell must be reservation-free in [startTime, startTime + windowSeconds].
        /// Returns true if the immediate path is clear — bot can release hold and depart.
        /// </summary>
        public override bool IsPathClearForDeparture(
            BotNormal bot, Waypoint to, double startTime,
            int lookAheadCells, double windowSeconds)
        {
            if (bot == null || bot.CurrentWaypoint == null || to == null) return true;
            var method = PathFinder as WHCAnStarMethod;
            if (method == null) return true;

            var current = bot.CurrentWaypoint;
            double endTime = startTime + windowSeconds;
            var visited = new System.Collections.Generic.HashSet<Waypoint>();
            visited.Add(current);

            for (int step = 0; step < lookAheadCells; step++)
            {
                // Pick the neighbor that most reduces Manhattan distance to target.
                Waypoint best = null;
                double bestDist = double.PositiveInfinity;
                foreach (var nb in current.Paths)
                {
                    if (nb == null || visited.Contains(nb)) continue;
                    double d = System.Math.Abs(nb.X - to.X) + System.Math.Abs(nb.Y - to.Y);
                    if (d < bestDist) { bestDist = d; best = nb; }
                }
                if (best == null) break;   // no progress possible

                // Lookup node id and check reservation table for this cell.
                if (!_waypointIds.ValuesFirst.Contains(best))
                {
                    visited.Add(best);
                    current = best;
                    continue;   // cell not in graph (shouldn't happen) — treat as clear
                }
                int nodeId = _waypointIds[best];
                if (!method._reservationTable.IntersectionFree(nodeId, startTime, endTime))
                    return false;   // blocked within window

                visited.Add(best);
                current = best;

                if (best == to) break;   // reached station
            }

            return true;   // all checked cells clear
        }
    }
}
