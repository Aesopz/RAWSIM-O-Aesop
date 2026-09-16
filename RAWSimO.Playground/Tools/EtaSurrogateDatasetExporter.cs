using RAWSimO.Core;
using RAWSimO.Core.Bots;
using RAWSimO.Core.Control.JIT;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Geometrics;
using RAWSimO.Core.IO;
using RAWSimO.Core.Waypoints;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RAWSimO.Playground.Tools
{
    internal static class EtaSurrogateDatasetExporter
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        private static readonly double[] EmptyOrientationBuckets =
        {
            0.0,
            Math.PI / 2.0,
            Math.PI,
            Math.PI * 3.0 / 2.0
        };

        public static int Run(string[] args)
        {
            if (args.Length < 5)
            {
                Console.WriteLine("Usage: eta-surrogate <xlayo|xinst> <xsett> <xconf> <outDir> [sourceScope=initial_bots|all_waypoints] [maxVisits]");
                return 2;
            }

            string instancePath = args[1];
            string settingPath = args[2];
            string controlPath = args[3];
            string outDir = args[4];
            string sourceScope = "initial_bots";
            int maxVisits = 0;
            if (args.Length >= 6)
            {
                int parsedLimit;
                if (int.TryParse(args[5], NumberStyles.Integer, Ci, out parsedLimit))
                    maxVisits = parsedLimit;
                else
                    sourceScope = args[5];
            }
            if (args.Length >= 7)
                maxVisits = int.Parse(args[6], Ci);
            sourceScope = sourceScope.ToLowerInvariant();
            if (sourceScope != "initial_bots" && sourceScope != "all_waypoints")
                throw new ArgumentException("Unknown sourceScope: " + sourceScope);

            Directory.CreateDirectory(outDir);
            Instance instance = InstanceIO.ReadInstance(instancePath, settingPath, controlPath, logAction: s => Console.WriteLine(s));
            BotNormal physicsBot = instance.Bots.OfType<BotNormal>().FirstOrDefault();
            if (physicsBot == null)
                throw new InvalidOperationException("No BotNormal found; cannot export kinematic ETA labels.");

            string samplePath = Path.Combine(outDir, "eta_samples.csv");
            using (var writer = new StreamWriter(samplePath))
            {
                writer.WriteLine(string.Join(",",
                    "kind",
                    "visit_id", "leg_index", "source_scope", "bot_id", "station_queue_wp_id",
                    "from_id", "to_id", "pod_id", "station_id", "loaded", "orientation_bucket",
                    "from_x", "from_y", "to_x", "to_y",
                    "abs_dx", "abs_dy", "euclid", "manhattan",
                    "same_tier", "from_storage", "to_storage", "from_queue", "to_queue",
                    "from_degree", "to_degree",
                    "path_found", "path_hops", "path_distance", "path_turns", "path_segments",
                    "eta_sec"));

                ExportCounts counts = ExportVisitSamples(instance, physicsBot, writer, sourceScope, maxVisits);
                Console.WriteLine("Wrote " + samplePath);
                Console.WriteLine("visit_samples=" + counts.Visits.ToString(Ci));
                Console.WriteLine("bot_to_pod_samples=" + counts.BotToPod.ToString(Ci));
                Console.WriteLine("pod_to_station_queue_samples=" + counts.PodToStation.ToString(Ci));
            }

            string manifestPath = Path.Combine(outDir, "manifest.txt");
            File.WriteAllLines(manifestPath, new[]
            {
                "instance=" + Path.GetFullPath(instancePath),
                "setting=" + Path.GetFullPath(settingPath),
                "control=" + Path.GetFullPath(controlPath),
                "sample_csv=" + Path.GetFullPath(samplePath),
                "source_scope=" + sourceScope,
                "max_visits=" + maxVisits.ToString(Ci),
                "bot_count=" + instance.Bots.Count.ToString(Ci),
                "pod_count=" + instance.Pods.Count.ToString(Ci),
                "output_station_count=" + instance.OutputStations.Count.ToString(Ci),
                "waypoint_count=" + instance.Waypoints.Count.ToString(Ci)
            });
            return 0;
        }

        private static ExportCounts ExportVisitSamples(Instance instance, BotNormal physicsBot, StreamWriter writer,
            string sourceScope, int maxVisits)
        {
            var sources = BuildVisitSources(instance, sourceScope);
            var pods = instance.Pods
                .Where(p => p != null && p.Waypoint != null)
                .OrderBy(p => p.ID)
                .ToList();
            var stations = instance.OutputStations
                .Where(s => s != null && s.Waypoint != null)
                .OrderBy(s => s.ID)
                .ToList();

            var counts = new ExportCounts();
            int visitId = 0;
            foreach (VisitSource source in sources)
            {
                foreach (Pod pod in pods)
                {
                    foreach (OutputStation station in stations)
                    {
                        if (maxVisits > 0 && counts.Visits >= maxVisits)
                            return counts;

                        Waypoint queueDest = JITArrivalETA.ResolveQueueRearWaypoint(station);
                        WriteSample(writer, instance, physicsBot, "bot_to_pod", visitId, 1,
                            source.SourceScope, source.BotId, queueDest.ID, source.From, pod.Waypoint,
                            pod.ID, station.ID, loaded: false, initialOrientation: source.InitialOrientation);
                        counts.BotToPod++;

                        WriteSample(writer, instance, physicsBot, "pod_to_station_queue", visitId, 2,
                            source.SourceScope, source.BotId, queueDest.ID, pod.Waypoint, queueDest,
                            pod.ID, station.ID, loaded: true, initialOrientation: double.NaN);
                        counts.PodToStation++;
                        counts.Visits++;
                        visitId++;
                    }
                }
            }
            return counts;
        }

        private static List<VisitSource> BuildVisitSources(Instance instance, string sourceScope)
        {
            if (sourceScope == "all_waypoints")
            {
                var sources = new List<VisitSource>();
                foreach (Waypoint waypoint in instance.Waypoints.Where(w => w != null).OrderBy(w => w.ID))
                {
                    foreach (double orientation in EmptyOrientationBuckets)
                    {
                        sources.Add(new VisitSource
                        {
                            SourceScope = sourceScope,
                            BotId = -1,
                            From = waypoint,
                            InitialOrientation = orientation
                        });
                    }
                }
                return sources;
            }

            return instance.Bots
                .Where(b => b != null)
                .OrderBy(b => b.ID)
                .Select(b => new VisitSource
                {
                    SourceScope = sourceScope,
                    BotId = b.ID,
                    From = b.CurrentWaypoint ?? instance.WaypointGraph.GetClosestWaypoint(b.Tier, b.X, b.Y),
                    InitialOrientation = b.Orientation
                })
                .Where(s => s.From != null)
                .ToList();
        }

        private static void WriteSample(StreamWriter writer, Instance instance, BotNormal physicsBot,
            string kind, int visitId, int legIndex, string sourceScope, int botId, int stationQueueWaypointId,
            Waypoint from, Waypoint to, int podId, int stationId, bool loaded, double initialOrientation)
        {
            List<Waypoint> path = instance.MetaInfoManager.TimeEfficientPathManager
                .GetShortestPathNodes(from, to, instance, loaded);
            bool pathFound = path != null && path.Count >= 2;
            PathFeatures pf = pathFound
                ? BuildPathFeatures(path, instance.StraightOrientationTolerance)
                : new PathFeatures();
            double eta = pathFound
                ? IdealTravelTime.Compute(path, physicsBot.Physics, instance.StraightOrientationTolerance, initialOrientation)
                : double.PositiveInfinity;

            double dx = Math.Abs(from.X - to.X);
            double dy = Math.Abs(from.Y - to.Y);
            double euclid = Math.Sqrt(dx * dx + dy * dy);
            double manhattan = dx + dy;

            writer.WriteLine(string.Join(",",
                kind,
                I(visitId), I(legIndex), sourceScope, I(botId), I(stationQueueWaypointId),
                I(from.ID), I(to.ID), I(podId), I(stationId), B(loaded), D(double.IsNaN(initialOrientation) ? -1.0 : initialOrientation),
                D(from.X), D(from.Y), D(to.X), D(to.Y),
                D(dx), D(dy), D(euclid), D(manhattan),
                B(from.Tier == to.Tier), B(from.PodStorageLocation), B(to.PodStorageLocation), B(from.IsQueueWaypoint), B(to.IsQueueWaypoint),
                I(from.Paths.Count()), I(to.Paths.Count()),
                B(pathFound), I(pathFound ? path.Count - 1 : 0), D(pf.Distance), I(pf.Turns), I(pf.Segments),
                D(eta)));
        }

        private static PathFeatures BuildPathFeatures(IReadOnlyList<Waypoint> path, double straightOrientationTolerance)
        {
            double distance = 0.0;
            int turns = 0;
            int segments = path.Count >= 2 ? 1 : 0;
            double prevOri = 0.0;
            bool hasPrev = false;

            for (int i = 0; i < path.Count - 1; i++)
            {
                Waypoint a = path[i];
                Waypoint b = path[i + 1];
                distance += a.GetDistance(b);
                double ori = Circle.GetOrientation(a.X, a.Y, b.X, b.Y);
                if (hasPrev)
                {
                    double diff = Math.Abs(Circle.GetOrientationDifference(prevOri, ori));
                    if (diff >= straightOrientationTolerance)
                    {
                        turns++;
                        segments++;
                    }
                }
                prevOri = ori;
                hasPrev = true;
            }

            return new PathFeatures
            {
                Distance = distance,
                Turns = turns,
                Segments = segments
            };
        }

        private static string D(double value)
        {
            if (double.IsPositiveInfinity(value)) return "inf";
            if (double.IsNegativeInfinity(value)) return "-inf";
            if (double.IsNaN(value)) return "nan";
            return value.ToString("G17", Ci);
        }

        private static string I(int value) { return value.ToString(Ci); }
        private static string B(bool value) { return value ? "1" : "0"; }

        private struct PathFeatures
        {
            public double Distance;
            public int Turns;
            public int Segments;
        }

        private struct VisitSource
        {
            public string SourceScope;
            public int BotId;
            public Waypoint From;
            public double InitialOrientation;
        }

        private struct ExportCounts
        {
            public int Visits;
            public int BotToPod;
            public int PodToStation;
        }
    }
}
