using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wonderland.Editor
{
    /// <summary>
    /// The five-minute user journey, scripted against the same <see cref="WonderlandWorld"/> API the controllers drive:
    /// sculpt hills, paint and erase forest, draw a hand-wobbly loop, try invalid tracks, place three houses, run the
    /// train, undo, save and load. <see cref="Test"/> asserts the outcomes (exit code 1 on failure, report in
    /// docs/test-results.txt); <see cref="Renders"/> runs the journey and renders the assembled world (editor renders,
    /// not headset captures). This does not exercise controller input, passthrough or on-device performance.
    /// </summary>
    public static partial class WonderlandJourney
    {
        private const float Dt = 1f / 72f;
        private static readonly StringBuilder Report = new();
        private static int failures;

        public static void Test()
        {
            Report.Clear(); failures = 0; BuildGrid.Snap = false;   // (journeys drawn by hand; the road journey checks the grid)
            try { Run(out _); }
            catch (Exception e) { Fail("Exception: " + e); }
            BuildGrid.Snap = true;
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            Directory.CreateDirectory("docs");
            File.WriteAllText("docs/test-results.txt", Report.ToString());
            Debug.Log("Wonderland journey test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>Just the village's life and work (a quicker loop while working on them): Logs/village-results.txt.</summary>
        public static void TestVillage()
        {
            Report.Clear(); failures = 0; BuildGrid.Snap = false;   // (journeys drawn by hand; the road journey checks the grid)
            PeopleSystem.LegacyParts = int.TryParse(System.Environment.GetEnvironmentVariable("WL_LEGACY"), out int parts) ? parts : 0;   // (compare with the old walking)
            try { VillageLifeJourney(); VillageWorkJourney(); DayJourney(); }
            catch (Exception e) { Fail("Exception: " + e); }
            finally { PeopleSystem.LegacyParts = 0; }
            BuildGrid.Snap = true;
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            File.WriteAllText("Logs/village-results.txt", Report.ToString());
            Debug.Log("Wonderland village test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>Just the livelier town (traffic, emergencies, pets, CCTV, sound...): Logs/town-results.txt.</summary>
        public static void TestTown()
        {
            Report.Clear(); failures = 0; BuildGrid.Snap = false;   // (journeys drawn by hand; the road journey checks the grid)
            PeopleSystem.LegacyParts = int.TryParse(System.Environment.GetEnvironmentVariable("WL_LEGACY"), out int parts) ? parts : 0;   // (compare with the old walking)
            try { TownJourney(); WeatherJourney(); ZooJourney(); ProportionsJourney(); }
            catch (Exception e) { Fail("Exception: " + e); }
            finally { PeopleSystem.Legacy = false; }
            BuildGrid.Snap = true;
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            File.WriteAllText("Logs/town-results.txt", Report.ToString());
            Debug.Log("Wonderland town test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>Just the roads and the player's traffic: Logs/roads-results.txt.</summary>
        public static void TestRoads()
        {
            Report.Clear(); failures = 0; BuildGrid.Snap = false;   // (journeys drawn by hand; the road journey checks the grid)
            try { RoadJourney(); JoinJourney(); SpikeJourney(); PieceJourney(); }
            catch (Exception e) { Fail("Exception: " + e); }
            BuildGrid.Snap = true;
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            File.WriteAllText("Logs/roads-results.txt", Report.ToString());
            Debug.Log("Wonderland roads test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static string mode = "";   // (a prefix for checks run a second time, e.g. on the grid)

        private static void Check(bool ok, string what)
        {
            Report.AppendLine((ok ? "PASS  " : "FAIL  ") + mode + what);
            if (!ok) failures++;
        }

        private static void Fail(string what) { Report.AppendLine("FAIL  " + what); failures++; }

        public static WorldMaterials Materials() => new()
        {
            Lit = WonderlandMaterials.Lit, Terrain = WonderlandMaterials.Terrain, UI = WonderlandMaterials.UI, Smoke = WonderlandMaterials.Smoke,
            Water = WonderlandMaterials.Water, Screen = WonderlandMaterials.Screen,
        };

        /// <summary>Builds the journey's world. Returns the world; <paramref name="drawnLoop"/> is the stroke used for the track.</summary>
        public static WonderlandWorld Run(out List<Vector3> drawnLoop)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            var world = new GameObject("Wonderland").AddComponent<WonderlandWorld>();
            world.Create(2f, Materials());
            var t = world.Terrain;
            var started = DateTime.Now;

            // 1. Terrain: three hills, a hollow, smoothing.
            float before = Max(t.Surface);
            Hold(() => world.TerrainStroke(TerrainBrush.Raise, new Vector3(-0.5f, 0f, 0.42f), 0.26f, 0.9f, Dt, 0f), 3.2f);
            Hold(() => world.TerrainStroke(TerrainBrush.Raise, new Vector3(0.55f, 0f, 0.5f), 0.22f, 0.8f, Dt, 0f), 2.4f);
            Hold(() => world.TerrainStroke(TerrainBrush.Raise, new Vector3(-0.6f, 0f, -0.55f), 0.2f, 0.7f, Dt, 0f), 1.8f);
            Hold(() => world.TerrainStroke(TerrainBrush.Lower, new Vector3(0.45f, 0f, -0.45f), 0.15f, 0.5f, Dt, 0f), 0.8f);
            for (int i = 0; i < 3; i++)
                Hold(() => world.TerrainStroke(TerrainBrush.Smooth, new Vector3(-0.5f + i * 0.5f, 0f, 0.4f), 0.3f, 0.6f, Dt, 0f), 0.6f);
            float after = Max(t.Surface);
            Check(after > before + 0.08f, $"Raise brush builds hills (max height {before * 100f:0.0} cm -> {after * 100f:0.0} cm)");
            float steepest = MaxSlope(t, out var where);
            Check(steepest < 1.3f, $"Hills stay rounded, no spikes (steepest slope {steepest:0.00} at {where.x:0.00},{where.y:0.00}, limit 1.3)");
            Check(Min(t.Height) >= TerrainField.MinHeight - 1e-5f, "Lowering never digs below the floor margin");

            // 1b. Extend the land: grow a peninsula onto the floor beyond the shore, plant it, trim part of it back.
            float area = t.LandArea();
            Check(t.EdgeAt(1.3f, 0f) == 0f, "Beyond the starting shore is bare floor");
            Stroke(new Vector3(0.9f, 0f, -0.1f), new Vector3(1.45f, 0f, 0.05f), 2.5f, p => world.TerrainStroke(TerrainBrush.Grow, p, 0.22f, 0.8f, Dt, 0f));
            float grown = t.LandArea();
            Check(t.EdgeAt(1.3f, 0f) > 0.3f && grown > area + 0.15f, $"Grow brush extends the land ({area:0.00} -> {grown:0.00} m2)");
            Check(t.SurfaceAt(1.3f, 0f) > 0.008f && t.SurfaceAt(1.75f, 0.6f) == 0f, "New land rises gently from the floor; the rest stays floor");
            Hold(() => world.PaintForest(new Vector3(1.25f, 0f, 0f), 0.12f, 0.8f, Dt), 1.5f);
            int planted = world.Forest.Trees.FindAll(tr => tr.x > 1.05f).Count;
            Check(planted > 3, $"Trees grow on the new land ({planted})");
            Hold(() => world.TerrainStroke(TerrainBrush.Trim, new Vector3(1.4f, 0f, 0.03f), 0.18f, 1f, Dt, 0f), 1.5f);
            Check(t.LandArea() < grown && t.EdgeAt(1.42f, 0.03f) == 0f, $"Trim brush takes land away ({grown:0.00} -> {t.LandArea():0.00} m2)");
            Check(!world.Forest.Trees.Exists(tr => t.EdgeAt(tr.x, tr.z) < 0.3f && tr.x > 1.05f), "Trees on trimmed land are removed");

            // 1c. A pond: dig it, it holds water below the banks; trees and houses keep out of it; drain part of it.
            var pondSpot = new Vector3(0.55f, 0f, -0.7f);
            float pondLevel = world.WaterLevelForStroke(pondSpot);
            Hold(() => world.WaterStroke(pondSpot, 0.13f, 0.9f, Dt, pondLevel, false), 1.5f);
            Stroke(pondSpot, pondSpot + new Vector3(-0.12f, 0f, 0.04f), 1.2f, p => world.WaterStroke(p, 0.11f, 0.9f, Dt, world.WaterLevelForStroke(pondSpot), false));
            Check(t.WaterAt(pondSpot.x, pondSpot.z) > 0.9f && t.SurfaceAt(pondSpot.x, pondSpot.z) < pondLevel - 0.008f,
                $"Pond brush digs a basin and fills it (level {pondLevel * 100f:0.0} cm, bed {t.SurfaceAt(pondSpot.x, pondSpot.z) * 100f:0.0} cm)");
            Check(Mathf.Abs(t.WaterLevelAt(pondSpot.x - 0.1f, pondSpot.z + 0.03f) - pondLevel) < 1e-4f, "Extending a pond keeps one water level");
            Check(!world.CanGrowTree(new Vector2(pondSpot.x, pondSpot.z), 0.02f), "Trees do not grow in water");
            Check(world.ValidateHouse(new HouseInstance { x = pondSpot.x, z = pondSpot.z, variant = 0 }) != null, "Houses are not placed in water");
            int waterCells = 0; foreach (float w in t.Water) if (w > 0.5f) waterCells++;
            world.Checkpoint();
            Hold(() => world.WaterStroke(pondSpot + new Vector3(-0.12f, 0f, 0.04f), 0.08f, 1f, Dt, 0f, true), 1f);
            int drained = 0; foreach (float w in t.Water) if (w > 0.5f) drained++;
            Check(drained < waterCells, $"Drain removes water ({waterCells} -> {drained} cells)");
            world.Undo();

            // 2. Forest: two painted strokes, then a little erasing.
            Stroke(new Vector3(-0.75f, 0f, 0.15f), new Vector3(-0.2f, 0f, 0.7f), 3f, p => world.PaintForest(p, 0.2f, 0.75f, Dt));
            Stroke(new Vector3(0.35f, 0f, 0.75f), new Vector3(0.78f, 0f, 0.2f), 2.2f, p => world.PaintForest(p, 0.17f, 0.6f, Dt));
            Stroke(new Vector3(-0.75f, 0f, -0.35f), new Vector3(-0.4f, 0f, -0.75f), 1.6f, p => world.PaintForest(p, 0.16f, 0.55f, Dt));
            int trees = world.Forest.Trees.Count;
            var kinds = new HashSet<int>(); foreach (var tr in world.Forest.Trees) kinds.Add(tr.kind);
            Check(trees > 120, $"Forest brush paints clusters ({trees} trees, {kinds.Count} species)");
            Check(kinds.Count >= 4, "Painted forest mixes species");
            Hold(() => world.EraseForest(new Vector3(-0.45f, 0f, 0.45f), 0.08f, 1f, Dt), 1f);
            Check(world.Forest.Trees.Count < trees, $"Forest eraser removes trees ({trees} -> {world.Forest.Trees.Count})");

            // 3. Railway: invalid strokes are rejected with feedback; a wobbly hand-drawn loop is accepted and closed.
            var tooShort = new List<Vector3> { t.PointAt(0f, 0f), t.PointAt(0.05f, 0f), t.PointAt(0.1f, 0f) };
            var bad = world.BuildTrack(tooShort);
            Check(!bad.Ok && bad.Error.Contains("longer"), $"A too-short stroke is refused (\"{bad.Error}\")");
            Check(!world.Track.HasTrack, "Rejected strokes leave the world unchanged");

            drawnLoop = HandDrawnLoop(t, new Vector2(0.05f, -0.02f), new Vector2(0.62f, 0.44f));
            int treesBeforeTrack = world.Forest.Trees.Count;
            var plan = world.BuildTrack(drawnLoop);
            Check(plan.Ok, "Hand-drawn loop accepted" + (plan.Ok ? "" : $" (\"{plan.Error}\")"));
            if (!plan.Ok) return world;
            var layout = Main(world);
            Check(layout.Closed, $"Loop closed automatically (end drawn {Vector3.Distance(drawnLoop[0], drawnLoop[^1]) * 100f:0} cm from start)");
            var coarse = new List<Vector2>(); for (int i = 0; i < layout.Points.Count; i += 3) coarse.Add(new Vector2(layout.Points[i].x, layout.Points[i].z));
            float minR = TrackPlanner.MinRadiusOf(coarse, true);
            Check(minR >= TrackPlanner.MinRadius * 0.9f, $"Curves smoothed to a drivable radius (min {minR * 100f:0} cm, limit {TrackPlanner.MinRadius * 100f:0} cm), length {layout.Length:0.00} m");
            float grade = MaxGrade(layout);
            Check(grade <= TrackPlanner.MaxGrade * 1.15f, $"Grades limited (max {grade * 100f:0.0} %)");
            Check(world.Forest.Trees.Count < treesBeforeTrack || treesBeforeTrack == 0, $"Trees cleared from the corridor ({treesBeforeTrack} -> {world.Forest.Trees.Count})");
            bool bedMatches = true;
            foreach (var p in layout.Points)
                if (Mathf.Abs(t.SurfaceAt(p.x, p.z) - (p.y - 0.0015f)) > 0.004f) { bedMatches = false; break; }
            Check(bedMatches, "Terrain graded to the track bed (embankments / cuttings)");

            // 3b. Forgiving loops (each in a scratch world): overshooting the start, a jittery kink, half over bare floor.
            TrackTolerance("Loop drawn past its start closes", (t2, l) => Loop(t2, 0.5f, 0.38f, 1.12f, false));
            TrackTolerance("Loop with an accidental kink loop closes", (t2, l) => Loop(t2, 0.5f, 0.38f, 0.97f, true));
            TrackTolerance("Loop ending 25 cm short of the start closes", (t2, l) => Loop(t2, 0.5f, 0.38f, 0.92f, false));
            TrackTolerance("Loop drawn half over bare floor closes and grows land under it", (t2, l) => Loop(t2, 1.05f, 0.6f, 0.97f, false), true);
            NetworkJourney();
            LivingJourney();
            BridgeJourney();
            VillageLifeJourney();
            VillageWorkJourney();
            DayJourney();
            TownJourney();
            RailJourney();
            GridRailJourney();
            { bool snap = BuildGrid.Snap; float size = WonderlandWorld.MaxWindow; WonderlandWorld.MaxWindow = 3f; NavigationJourney(); WonderlandWorld.MaxWindow = 3f; WindowJourney(); WonderlandWorld.MaxWindow = size; BuildGrid.Snap = snap; Shader.SetGlobalFloat("_WL_WindowOn", 0f); }
            WeatherJourney();
            {
                // Tunnel: a big hill across the line keeps its top; portals at both mouths; no steam underground.
                var w = new GameObject("Tunnel").AddComponent<WonderlandWorld>();
                w.transform.position = new Vector3(60f, 0f, 0f);
                w.Create(2f, Materials(), 7, false);
                var hill = new Vector3(0.5f, 0f, 0f);
                Hold(() => w.TerrainStroke(TerrainBrush.Raise, hill, 0.3f, 1f, Dt, 0f), 6f);
                float hillTop = w.Terrain.SurfaceAt(hill.x, hill.z);
                var tunnelPlan = w.BuildTrack(Loop(w.Terrain, 0.5f, 0.38f, 0.97f, false));
                var mask = MainTunnel(w);
                int tunnelPoints = 0; if (mask != null) foreach (bool m in mask) if (m) tunnelPoints++;
                bool hillKept = false;
                if (tunnelPlan.Ok && mask != null)
                {
                    float sMid = Main(w).Nearest(hill, out _);
                    var bed = Main(w).Evaluate(sMid, out _);
                    hillKept = w.Terrain.SurfaceAt(bed.x, bed.z) > bed.y + TrackSystem.TunnelHeight;
                }
                Check(tunnelPlan.Ok && tunnelPoints > 8 && hillKept, $"A railway under a big hill becomes a tunnel (hill {hillTop * 100f:0} cm, {tunnelPoints} cm of tunnel, hill kept above it)");
                if (tunnelPlan.Ok && mask != null)
                {
                    PlaceTrainAt(w, Main(w).Points[0]);
                    bool steamless = false;
                    for (int i = 0; i < 72 * 40 && !steamless && w.Trains.Any; i++) { w.Trains.Step(Dt); if (w.Trains.InTunnel(FirstTrain(w))) steamless = true; }
                    Check(steamless, "The train runs into the tunnel (steam off underground)");
                }
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            {
                var w = new GameObject("Water crossing").AddComponent<WonderlandWorld>();
                w.transform.position = new Vector3(50f, 0f, 0f);
                w.Create(2f, Materials(), 7, false);
                var spot = new Vector3(0.5f, 0f, 0f);
                float lvl = w.WaterLevelForStroke(spot);
                Hold(() => w.WaterStroke(spot, 0.14f, 1f, Dt, lvl, false), 1.5f);
                var crossing = w.BuildTrack(Loop(w.Terrain, 0.5f, 0.38f, 0.97f, false));
                bool bridged = false, waterKept = false;
                if (crossing.Ok)
                {
                    float sCross = Main(w).Nearest(spot, out _);
                    var bed = Main(w).Evaluate(sCross, out _);
                    bridged = bed.y >= lvl + TrackPlanner.BridgeClearance * 0.9f;
                    waterKept = w.Terrain.WaterAt(bed.x, bed.z) > 0.5f && w.Terrain.SurfaceAt(bed.x, bed.z) < lvl;
                }
                Check(crossing.Ok && bridged && waterKept, crossing.Ok ? "Railway across a pond rises onto a bridge and the water stays" : $"Railway across a pond (\"{crossing.Error}\")");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }

            // 4. Sculpting over the railway leaves the track (and the ground under it) where it is: what lies flat on the
            //    ground stays on top. The land beside it rises into a cutting.
            int probeIndex = layout.Points.Count / 3;
            var probe = layout.Points[probeIndex];
            float trackBefore = probe.y;
            Hold(() => world.TerrainStroke(TerrainBrush.Raise, probe, 0.35f, 1f, Dt, 0f), 2f);
            world.UpdateTrackFromTerrain();
            float trackAfter = Main(world).Points[probeIndex].y;
            float besideTrack = world.Terrain.SurfaceAt(probe.x + 0.1f, probe.z + 0.1f);
            Check(Mathf.Abs(trackAfter - trackBefore) < 0.002f, $"Raising the ground over the railway leaves the track where it is, on top ({trackBefore * 100f:0.0} -> {trackAfter * 100f:0.0} cm; the land beside it {besideTrack * 100f:0.0} cm)");
            bool bedFollows = true; int tunnelled = 0;
            for (int i = 0; i < Main(world).Points.Count; i++)
            {
                var p = Main(world).Points[i];
                bool nearTunnel = false;
                if (MainTunnel(world) != null)
                    for (int k = -6; k <= 6 && !nearTunnel; k++) nearTunnel = MainTunnel(world)[((i + k) % MainTunnel(world).Length + MainTunnel(world).Length) % MainTunnel(world).Length];
                if (nearTunnel) { tunnelled++; continue; }   // the hill is kept over a tunnel and rises at its mouths
                if (world.WaterLevelFor(new Vector2(p.x, p.z)) >= 0f) continue;                        // a bridge spans water
                float dev = t.SurfaceAt(p.x, p.z) - (p.y - 0.0015f);
                if (Mathf.Abs(dev) > 0.004f) { bedFollows = false; Report.AppendLine($"INFO  bed off by {dev * 1000f:0.0} mm at point {i} ({p.x:0.00},{p.z:0.00}) water {world.WaterLevelFor(new Vector2(p.x, p.z)):0.000} pad {t.Pad[t.Index(Mathf.RoundToInt((p.x - t.X0) / t.Cell), Mathf.RoundToInt((p.z - t.Z0) / t.Cell))]:0.00} lock {t.LockAt(p.x, p.z):0.00} edge {t.EdgeAt(p.x, p.z):0.00} land {t.LandAt(p.x, p.z)} bedY {p.y * 100f:0.00} cm"); break; }
            }
            Check(bedFollows, $"The bed is regraded under the lifted track (tunnel {tunnelled} cm)");
            Check(MaxGrade(Main(world)) <= TrackPlanner.MaxGrade * 1.2f, $"Lifted track keeps its grade limit (max {MaxGrade(Main(world)) * 100f:0.0} %)");
            layout = Main(world);

            // 5. Houses: three variants, one needs flattening first; invalid spots are refused.
            var spots = new[] { new Vector3(0.05f, 0f, -0.05f), new Vector3(-0.22f, 0f, 0.08f), new Vector3(0.3f, 0f, -0.12f) };
            for (int v = 0; v < 3; v++)
            {
                var h = new HouseInstance { x = spots[v].x, z = spots[v].z, rotation = 15f - v * 25f, variant = v };
                string error = world.PlaceHouse(h);
                if (error != null && error.Contains("steep"))
                {
                    float target = t.SurfaceAt(h.x, h.z);
                    Hold(() => world.TerrainStroke(TerrainBrush.Flatten, spots[v], 0.12f, 1f, Dt, target), 1.2f);
                    error = world.PlaceHouse(h);
                }
                Check(error == null, $"Place {HouseFactory.Name(v)}" + (error == null ? "" : $" (\"{error}\")"));
            }
            float worstSit = 0f;
            for (int i = 0; i < world.Buildings.Houses.Count; i++)
            {
                var hh = world.Buildings.Houses[i];
                float y = world.Buildings.SitHeight(hh, out _);
                foreach (var p in BuildingSystem.FootprintSamples(hh)) worstSit = Mathf.Max(worstSit, Mathf.Abs(t.SurfaceAt(p.x, p.y) - y));
            }
            Check(worstSit < 0.002f, $"Houses sit flush on a graded pad (worst footprint gap {worstSit * 1000f:0.0} mm)");
            // A house on a slope: the pad cuts and fills; sculpting next to it leaves the pad alone.
            var slopeSpot = new Vector3(0.2f, 0f, 0.2f);
            Hold(() => world.TerrainStroke(TerrainBrush.Raise, slopeSpot + new Vector3(0.09f, 0f, 0f), 0.12f, 0.7f, Dt, 0f), 1.2f);
            var slopeHouse = new HouseInstance { x = slopeSpot.x, z = slopeSpot.z, rotation = 30f, variant = 0, style = 2 };
            world.Buildings.SitHeight(slopeHouse, out float slopeRange);
            string slopeError = world.PlaceHouse(slopeHouse);
            if (slopeError == null)
            {
                float y = world.Buildings.SitHeight(slopeHouse, out float rangeAfter);
                Hold(() => world.TerrainStroke(TerrainBrush.Raise, slopeSpot, 0.12f, 1f, Dt, 0f), 1f);
                float y2 = world.Buildings.SitHeight(slopeHouse, out float rangeAfter2);
                Check(rangeAfter < 0.002f && rangeAfter2 < 0.002f && Mathf.Abs(y2 - y) < 0.001f,
                    $"House on a slope gets a flat pad (ground range {slopeRange * 100f:0.0} cm -> {rangeAfter * 1000f:0.0} mm) that brushes leave alone (after brushing: range {rangeAfter2 * 1000f:0.0} mm, moved {(y2 - y) * 1000f:0.0} mm)");
                world.Undo(); world.Undo();
            }
            else Check(false, $"House on a slope placed (\"{slopeError}\")");

            string onTrack = world.ValidateHouse(new HouseInstance { x = layout.Points[0].x, z = layout.Points[0].z, variant = 0 });
            Check(onTrack != null, $"House on the railway refused (\"{onTrack}\")");
            string overlap = world.ValidateHouse(new HouseInstance { x = spots[0].x + 0.02f, z = spots[0].z, variant = 1 });
            Check(overlap != null, $"Overlapping house refused (\"{overlap}\")");
            int pick = world.Buildings.Pick(new Vector3(spots[1].x, 1f, spots[1].z), Vector3.down);
            Check(pick == 1, "Pointer ray picks the house below it");
            var moved = world.PickUpHouse(1);
            moved.x -= 0.04f; moved.rotation += 30f;
            Check(world.PlaceHouse(moved, false) == null, "House repositioned and rotated");

            // 6. Train: place on the loop, run, stays on the rails, keeps orientation.
            string trainError = PlaceTrainAt(world, layout.Points[layout.Points.Count / 2] + new Vector3(0.01f, 0f, 0f));
            var train = FirstTrain(world);
            if (train == null) return world;
            Check(trainError == null, "Train placed on the railway" + (trainError == null ? "" : $" (\"{trainError}\")"));
            world.SetTrainsRunning(true);
            float worstOffset = 0f, worstAlign = 1f;
            for (int i = 0; i < 72 * 12; i++)
            {
                world.Trains.Step(Dt);
                var pos = world.Trains.LocoPosition(FirstTrain(world));
                layout.Nearest(pos, out float d);
                worstOffset = Mathf.Max(worstOffset, d);
                float s = layout.Nearest(pos, out _);
                layout.Evaluate(s, out var tangent);
                var fwd = world.Trains.LocoRotation(FirstTrain(world)) * Vector3.forward;
                worstAlign = Mathf.Min(worstAlign, Mathf.Abs(Vector3.Dot(new Vector3(fwd.x, 0f, fwd.z).normalized, new Vector3(tangent.x, 0f, tangent.z).normalized)));
            }
            Check(FirstTrain(world).Odometer > 1.0f, $"Train runs continuously ({FirstTrain(world).Odometer:0.00} m in 12 s)");
            Check(worstOffset < 0.006f, $"Locomotive stays on the rails (worst offset {worstOffset * 1000f:0.0} mm)");
            Check(worstAlign > 0.97f, $"Locomotive follows the curves (worst alignment {worstAlign:0.000})");
            world.SetTrainsRunning(false);
            for (int i = 0; i < 72 * 3; i++) world.Trains.Step(Dt);
            Check(FirstTrain(world).Speed < 0.001f, "Train stops smoothly");
            world.SetTrainsRunning(true);

            // 6b. Railway station: snaps beside the rails; the train halts centred on the platform, waits, pulls away.
            var outside = layout.Points[layout.Points.Count * 2 / 3];
            var outward = new Vector3(outside.x - 0.05f, 0f, outside.z + 0.02f).normalized;
            var station = world.StationAt(outside + outward * 0.1f, 1);
            Check(station.HasValue, "Station snaps to the railway");
            string stationError = station.HasValue ? world.PlaceHouse(station.Value) : "no snap";
            Check(stationError == null && world.Trains.Stations.Count == 1, "Station placed beside the rails with a platform" + (stationError == null ? "" : $" (\"{stationError}\")"));
            bool halted = false; float haltOffset = 0f;
            for (int i = 0; i < 72 * 40 && !halted; i++)
            {
                world.Trains.Step(Dt);
                if (FirstTrain(world).Dwell > 0f)
                {
                    halted = true;
                    var centre = world.Trains.CentrePosition(FirstTrain(world));
                    var stop = world.Trains.Stations[0];
                    var platform = world.Track.Network.Edges[stop.Edge].Layout.Evaluate(stop.S, out _);
                    haltOffset = new Vector2(centre.x - platform.x, centre.z - platform.z).magnitude;
                }
            }
            Check(halted && FirstTrain(world).Speed == 0f, "Train halts at the station");
            Check(haltOffset < 0.03f, $"Train stops centred on the platform (off by {haltOffset * 100f:0.0} cm)");
            float odoAtStation = FirstTrain(world).Odometer;
            for (int i = 0; i < 72 * 8; i++) world.Trains.Step(Dt);
            Check(FirstTrain(world).Odometer > odoAtStation + 0.2f, "Train pulls away after waiting");

            // 6c. Sheep and cows: placed on open grass only, wander and graze around home, stay off water and rails.
            var meadow = new Vector3(0.12f, 0f, -0.7f);
            int placedAnimals = 0;
            for (int i = 0; i < 6; i++)
            {
                var p = meadow + new Vector3((i % 3) * 0.06f - 0.06f, 0f, (i / 3) * 0.06f);
                if (world.PlaceAnimal(p, i < 4 ? AnimalKind.Sheep : AnimalKind.Cow, 100 + i) == null) placedAnimals++;
            }
            Check(placedAnimals == 6, $"A small herd is placed on the meadow ({placedAnimals}/6)");
            Check(world.PlaceAnimal(pondSpot, AnimalKind.Sheep, 1) != null && world.PlaceAnimal(layout.Points[10], AnimalKind.Cow, 2) != null, "Animals are refused in water and on the railway");
            bool stayed = true; float roam = 0f;
            var homes = world.Animals.Animals.ToArray();
            for (int step = 0; step < 72 * 60; step++)
            {
                world.Animals.Step(Dt);
                if (step % 36 != 0) continue;
                for (int i = 0; i < homes.Length; i++)
                {
                    var pos = world.Animals.PositionOf(i);
                    roam = Mathf.Max(roam, (pos - new Vector2(homes[i].x, homes[i].z)).magnitude);
                    if ((pos - new Vector2(homes[i].x, homes[i].z)).magnitude > AnimalSystem.WanderRadius + 0.02f || !world.IsFreeGrass(pos, 0.004f)) stayed = false;
                }
            }
            Check(roam > 0.03f, $"Animals wander and graze (up to {roam * 100f:0} cm from home in a minute)");
            Check(stayed, "Animals stay on free grass near home");
            var victim = world.Animals.PositionOf(0);
            string erased = world.EraseAt(new Vector3(victim.x, 1f, victim.y), Vector3.down, new Vector3(victim.x, 0f, victim.y));
            Check(erased != null && erased.Contains("removed") && world.Animals.Animals.Count == 5, $"Eraser removes an animal (\"{erased}\")");

            // 6d. A stream across the meadow: flows only downhill, carves its bed, and the railway bridges it.
            var streamStroke = new List<Vector3>();
            for (int i = 0; i <= 60; i++) { var q = Vector3.Lerp(new Vector3(-0.86f, 0f, -0.12f), new Vector3(-0.3f, 0f, -0.3f), i / 60f) + new Vector3(0f, 0f, Mathf.Sin(i * 0.2f) * 0.03f); streamStroke.Add(t.PointAt(q.x, q.z)); }
            string streamError = world.BuildStream(streamStroke);
            Check(streamError == null && world.Streams.Streams.Count == 1, "Stream drawn" + (streamError == null ? "" : $" (\"{streamError}\")"));
            if (streamError == null)
            {
                var sp = world.Streams.Streams[0].points;
                bool downhill = true;
                for (int i = 1; i < sp.Count; i++) if (sp[i].y > sp[i - 1].y + 1e-5f) { downhill = false; break; }
                Check(downhill, $"Stream level only falls downstream ({sp[0].y * 100f:0.0} -> {sp[^1].y * 100f:0.0} cm)");
                var mid = sp[sp.Count / 2];
                Check(t.SurfaceAt(mid.x, mid.z) < mid.y - 0.004f, "Stream carves a channel below its water");
                Check(!world.Forest.Trees.Exists(tr => world.Streams.IsWet(new Vector2(tr.x, tr.z))), "No trees standing in the stream");
                float sTrack = Main(world).Nearest(mid, out _);
                bool crossesTrack = false;
                foreach (var p in sp) { Main(world).Nearest(p, out float dd); if (dd < 0.01f) { crossesTrack = true; break; } }
                if (crossesTrack)
                {
                    float worstClear = float.MaxValue;
                    foreach (var p in Main(world).Points)
                    {
                        float lv = world.WaterLevelFor(new Vector2(p.x, p.z));
                        if (lv >= 0f) worstClear = Mathf.Min(worstClear, p.y - lv);
                    }
                    Check(worstClear >= TrackPlanner.BridgeClearance * 0.9f, $"The railway bridges the stream (clearance {worstClear * 1000f:0} mm)");
                }
                else Check(false, "Stream crosses the railway (test layout)");
                _ = sTrack;
            }
            layout = Main(world);

            // 6e. Villagers: on dry land only; they stroll to the neighbours' doors, wait at the station, ride the train
            //     and wave at it - and never stand on the rails, in water or inside a house.
            {
                var homesFor = new List<Vector3>();
                var stationHouse = world.Buildings.Houses.Find(h => h.variant == HouseFactory.Station);
                var stationAt = new Vector2(stationHouse.x, stationHouse.z);
                var away = (stationAt - new Vector2(Main(world).Evaluate(Main(world).Nearest(new Vector3(stationAt.x, 0f, stationAt.y), out _), out _).x,
                    Main(world).Evaluate(Main(world).Nearest(new Vector3(stationAt.x, 0f, stationAt.y), out _), out _).z)).normalized;
                foreach (var h in world.Buildings.Houses)
                {
                    var origin = h.variant == HouseFactory.Station ? stationAt + away * 0.1f : PeopleSystem.DoorStep(h);
                    int want = h.variant == HouseFactory.Station ? 4 : 1;
                    for (int k = 0; k < 40 && want > 0; k++)
                    {
                        var q = origin + new Vector2(Mathf.Cos(k * 2.4f), Mathf.Sin(k * 2.4f)) * (0.03f + k * 0.004f);
                        if (world.People.Walkable(q) && CountNear(homesFor, q, 0.02f) == 0) { homesFor.Add(new Vector3(q.x, 0f, q.y)); want--; }
                    }
                }
                int placedPeople = 0;
                for (int i = 0; i < homesFor.Count; i++) if (world.PlacePerson(homesFor[i], i % PersonFactory.StyleCount, 300 + i) == null) placedPeople++;
                Check(placedPeople >= 6, $"Villagers move into the village ({placedPeople})");
                Check(world.PlacePerson(layout.Points[20], 0, 1) != null && world.PlacePerson(pondSpot, 0, 1) != null, "Villagers are refused on the rails and in the pond");
                Check(world.Trains.AnyRunning, "The train runs for the villagers");
                bool walked = false, chatted = false, waited = false, rode = false, waved = false, safe = true;
                var start = new Vector2[world.People.People.Count];
                for (int i = 0; i < start.Length; i++) start[i] = world.People.PositionOf(i);
                for (int step = 0; step < 72 * 240; step++)
                {
                    world.Trains.Step(Dt);
                    world.People.Step(Dt);
                    if (step % 12 != 0) continue;
                    for (int i = 0; i < start.Length; i++)
                    {
                        var st = world.People.StateOf(i);
                        chatted |= st == PeopleSystem.Activity.Chatting;
                        waited |= st == PeopleSystem.Activity.Waiting;
                        rode |= st == PeopleSystem.Activity.Riding;
                        waved |= world.People.WavingNow(i);
                        if (st == PeopleSystem.Activity.Riding) continue;
                        var pos = world.People.PositionOf(i);
                        walked |= (pos - start[i]).magnitude > 0.06f;
                        if (!world.People.Walkable(pos) || world.People.InsideHouse(pos, 0f)) { safe = false; Debug.Log($"Villager {i} off limits at {pos} ({st})"); }
                    }
                }
                Check(walked, "Villagers stroll about");
                Check(chatted, "Villagers call at a neighbour's door");
                Check(waited, "Villagers wait for the train on the platform");
                Check(rode, "Villagers board the train");
                Check(waved, "Villagers wave at the passing train");
                Check(safe, "Villagers keep off the rails, out of water and out of houses");
                int villagers = world.People.People.Count, victimIndex = -1;
                for (int i = 0; i < villagers && victimIndex < 0; i++)
                {
                    var at = world.People.PositionOf(i);
                    // Someone standing clear of other things the eraser could hit first.
                    if (world.People.StateOf(i) == PeopleSystem.Activity.Riding || world.Streams.IsWet(at, 0.04f) || world.Animals.Nearest(new Vector3(at.x, 0f, at.y), 0.03f) >= 0
                        || world.Buildings.Pick(new Vector3(at.x, 1f, at.y), Vector3.down) >= 0 || world.Paths.Pick(at) >= 0) continue;
                    victimIndex = i;
                }
                var who = world.People.PositionOf(victimIndex);
                string gone = world.EraseAt(new Vector3(who.x, 1f, who.y), Vector3.down, new Vector3(who.x, 0f, who.y));
                Check(gone == "Villager removed" && world.People.People.Count == villagers - 1, $"Eraser removes a villager (\"{gone}\")");
            }

            // 7. Undo.
            int houses = world.Buildings.Houses.Count;
            var extra = FindHouseSpot(world, 2);
            Check(world.PlaceHouse(extra) == null, $"Fourth house placed at a free spot ({extra.x:0.00}, {extra.z:0.00})");
            int withExtra = world.Buildings.Houses.Count;
            world.Undo();
            Check(withExtra == houses + 1 && world.Buildings.Houses.Count == houses, $"Undo removes the last house ({houses} -> {withExtra} -> {world.Buildings.Houses.Count})");

            // 8. Save / load round trip.
            string path = Path.Combine(Path.GetTempPath(), "wonderland-test.json");
            world.Save(path);
            var json = File.ReadAllText(path);
            var copy = new GameObject("Loaded").AddComponent<WonderlandWorld>();
            copy.Load(Materials(), path);
            Check(copy.Forest.Trees.Count == world.Forest.Trees.Count && copy.Buildings.Houses.Count == world.Buildings.Houses.Count,
                $"Save/load keeps trees and houses ({copy.Forest.Trees.Count} trees, {copy.Buildings.Houses.Count} houses, {json.Length / 1024} KB)");
            Check(copy.Track.HasTrack && Mathf.Abs(Main(copy).Length - layout.Length) < 0.001f, "Save/load keeps the railway");
            Check(copy.Trains.Any && copy.Trains.AnyRunning, "Save/load keeps the running train");
            Check(copy.Trains.Stations.Count == world.Trains.Stations.Count && copy.Trains.Stations.Count > 0, "Save/load keeps the station and its stop");
            Check(copy.Animals.Animals.Count == world.Animals.Animals.Count && copy.Animals.Animals.Count > 0, $"Save/load keeps the animals ({copy.Animals.Animals.Count})");
            Check(copy.Streams.Streams.Count == world.Streams.Streams.Count && copy.Streams.Streams.Count > 0, "Save/load keeps the stream");
            Check(copy.People.People.Count == world.People.People.Count && copy.People.People.Count > 0, $"Save/load keeps the villagers ({copy.People.People.Count})");
            bool same = true;
            for (int i = 0; i < t.Height.Length; i++) if (Mathf.Abs(copy.Terrain.Height[i] - t.Height[i]) > 1e-6f) { same = false; break; }
            Check(same, "Save/load keeps the sculpted terrain exactly");
            bool sameLand = true;
            for (int i = 0; i < t.Land.Length; i++) if (copy.Terrain.Land[i] != t.Land[i]) { sameLand = false; break; }
            Check(sameLand, "Save/load keeps the grown and trimmed land");
            bool sameWater = true;
            for (int i = 0; i < t.Water.Length; i++) if (copy.Terrain.Water[i] != t.Water[i] || copy.Terrain.WaterLevel[i] != t.WaterLevel[i]) { sameWater = false; break; }
            Check(sameWater && t.WaterAt(pondSpot.x, pondSpot.z) > 0.5f, "Save/load keeps the pond");
            UnityEngine.Object.DestroyImmediate(copy.gameObject);

            // 9. Worlds saved before land could grow (save version 1) still load.
            var v1 = new SaveV1 { version = 1, size = 2f, seed = 7, trees = new List<TreeInstance> { new() { x = 0.3f, z = 0.2f, scale = 1f, kind = 0, tint = 1f } } };
            var oldHeights = new float[97 * 97]; for (int i = 0; i < oldHeights.Length; i++) oldHeights[i] = 0.05f;
            var bytes = new byte[oldHeights.Length * 4]; Buffer.BlockCopy(oldHeights, 0, bytes, 0, bytes.Length);
            v1.height = Convert.ToBase64String(bytes); v1.locks = v1.soil = Convert.ToBase64String(new byte[bytes.Length]);
            string v1Path = Path.Combine(Path.GetTempPath(), "wonderland-v1.json");
            File.WriteAllText(v1Path, JsonUtility.ToJson(v1));
            var migrated = new GameObject("Migrated").AddComponent<WonderlandWorld>();
            migrated.Load(Materials(), v1Path);
            var mt = migrated.Terrain;
            Check(Mathf.Abs(mt.Height[mt.Index(-mt.GX0, -mt.GZ0)] - 0.05f) < 1e-4f && mt.EdgeAt(0f, 0f) > 0.9f && mt.EdgeAt(1.3f, 0f) == 0f
                && migrated.Forest.Trees.Count == 1, "Version 1 save migrates (terrain resampled, island rebuilt, trees kept)");
            UnityEngine.Object.DestroyImmediate(migrated.gameObject);

            // 10. Room bounds: the grid fits the room, land grows anywhere on free floor - not through walls or under
            // furniture - and the grid re-fits when the world moves. The starting patch is the user's 1 m2.
            var roomWorld = new GameObject("Room world").AddComponent<WonderlandWorld>();
            roomWorld.transform.position = new Vector3(10f, 0f, 0f);
            var roomFootprint = new RoomFootprint();
            foreach (var c in new[] { new Vector2(-1.2f, -1f), new Vector2(3.4f, -1f), new Vector2(3.4f, 2.6f), new Vector2(-1.2f, 2.6f) })
                roomFootprint.Floor.Add(new Vector3(10f + c.x, 0f, c.y));
            roomFootprint.Obstacles.Add(new List<Vector3> { new(11.6f, 0f, 1.6f), new(13.4f, 0f, 1.6f), new(13.4f, 0.8f, 2.5f), new(11.6f, 0.8f, 2.5f) }); // sofa
            roomWorld.Room = roomFootprint;
            roomWorld.Create(1f, Materials());
            var rt = roomWorld.Terrain;
            Check(Mathf.Abs(rt.LandArea() - 1f) < 0.25f, $"Starts as a small patch ({rt.LandArea():0.00} m2)");
            Check(rt.X0 <= -1.15f && rt.XMax >= 3.35f && rt.Z0 <= -0.95f && rt.ZMax >= 2.55f, $"Grid covers the whole room ({rt.Nx * rt.Cell:0.0} x {rt.Nz * rt.Cell:0.0} m)");
            float startWidth = rt.Nx * rt.Cell;
            Stroke(new Vector3(0.4f, 0f, 0f), new Vector3(5.2f, 0f, 0f), 8f, p => roomWorld.TerrainStroke(TerrainBrush.Grow, p, 0.25f, 1f, Dt, 0f));
            Check(rt.EdgeAt(2.6f, 0f) > 0.3f && rt.EdgeAt(4.8f, 0f) > 0.2f, "Land grows across the room and beyond its scanned outline");
            Check(rt.Nx * rt.Cell > startWidth + 1f, $"The grid grows on demand ({startWidth:0.0} -> {rt.Nx * rt.Cell:0.0} m wide)");
            Stroke(new Vector3(2.5f, 0f, 1.2f), new Vector3(2.5f, 0f, 2.3f), 3f, p => roomWorld.TerrainStroke(TerrainBrush.Grow, p, 0.2f, 1f, Dt, 0f));
            Check(rt.EdgeAt(2.5f, 1.9f) > 0.2f, "Furniture does not fence the land in (the player decides)");
            float landBeforeMove = rt.LandArea();
            roomWorld.transform.SetPositionAndRotation(new Vector3(9.4f, 0f, 0.6f), Quaternion.Euler(0f, 30f, 0f));
            roomWorld.FitToRoom();
            var corner = roomWorld.transform.InverseTransformPoint(new Vector3(10f + 3.3f, 0f, 2.5f));
            Check(Mathf.Abs(rt.LandArea() - landBeforeMove) < 1e-4f && rt.Inside(corner.x, corner.z), "After moving, the grid re-fits to the room and the land is unchanged");
            UnityEngine.Object.DestroyImmediate(roomWorld.gameObject);

            // 11. Bad room data (a volume covering the whole room, as the scanned room mesh carries) must not stop growth,
            // and trimmed land can always grow back.
            var badWorld = new GameObject("Bad room world").AddComponent<WonderlandWorld>();
            badWorld.transform.position = new Vector3(20f, 0f, 0f);
            var badRoom = new RoomFootprint();
            foreach (var c in new[] { new Vector2(-1.5f, -1.5f), new Vector2(2f, -1.5f), new Vector2(2f, 2f), new Vector2(-1.5f, 2f) })
                badRoom.Floor.Add(new Vector3(20f + c.x, 0f, c.y));
            badRoom.Obstacles.Add(new List<Vector3> { new(18.4f, 0f, -1.6f), new(22.1f, 0f, -1.6f), new(22.1f, 2.5f, 2.1f), new(18.4f, 2.5f, 2.1f) });
            badWorld.Room = badRoom;
            badWorld.Create(1f, Materials());
            var bt = badWorld.Terrain;
            Check(bt.AllowedAt(1.0f, 0.5f), $"Bad room data does not block growth (\"{badWorld.RoomStatus}\")");
            Stroke(new Vector3(0.3f, 0f, 0f), new Vector3(1.2f, 0f, 0f), 3f, p => badWorld.TerrainStroke(TerrainBrush.Grow, p, 0.22f, 1f, Dt, 0f));
            Check(bt.EdgeAt(1.0f, 0f) > 0.2f, "Land still grows with bad room data");
            float before2 = bt.LandArea();
            Hold(() => badWorld.TerrainStroke(TerrainBrush.Trim, new Vector3(0.9f, 0f, 0f), 0.2f, 1f, Dt, 0f), 1.5f);
            float trimmed = bt.LandArea();
            badWorld.Room = new RoomFootprint { Floor = { new Vector3(25f, 0f, 0f), new Vector3(26f, 0f, 0f), new Vector3(26f, 0f, 1f) } };   // a room that excludes the world
            badWorld.Room = badRoom;
            Hold(() => badWorld.TerrainStroke(TerrainBrush.Grow, new Vector3(0.9f, 0f, 0f), 0.2f, 1f, Dt, 0f), 1.5f);
            Check(trimmed < before2 && bt.LandArea() > trimmed + 0.05f, $"Trimmed land grows back ({before2:0.00} -> {trimmed:0.00} -> {bt.LandArea():0.00} m2)");
            UnityEngine.Object.DestroyImmediate(badWorld.gameObject);

            world.Forest.RebuildDirty();
            world.Cover.RebuildDirty();
            int renderers = 0; long triangles = 0;
            foreach (var r in world.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null || f.sharedMesh.vertexCount == 0) continue;
                renderers++; triangles += f.sharedMesh.triangles.Length / 3;
            }
            var terrainStats = world.TerrainView.Stats();
            Report.AppendLine($"INFO  terrain: {terrainStats.chunks} chunk meshes, {terrainStats.triangles / 1000} k triangles, land {t.LandArea():0.00} m2 of a {t.Nx * t.Cell:0.0} x {t.Nz * t.Cell:0.0} m grid");
            Report.AppendLine($"INFO  static budget of this world: {renderers} mesh renderers (~draw calls per eye pass before batching, excl. shadows/UI), {triangles / 1000} k triangles, ground cover {world.Cover.Count} tufts");
            Report.AppendLine($"INFO  trees {world.Forest.Trees.Count}, forest chunks {world.Forest.ChunkCount}, houses {world.Buildings.Houses.Count}, " +
                $"track {layout.Length:0.00} m, undo depth {world.UndoDepth}, journey simulated in {(DateTime.Now - started).TotalSeconds:0.0} s wall time");
            return world;
        }

        /// <summary>A hand-drawn ellipse loop: <paramref name="turns"/> above 1 overshoots the start, below 1 stops short.</summary>
        private static List<Vector3> Loop(TerrainField t, float rx, float rz, float turns, bool kink)
        {
            var pts = new List<Vector3>();
            var rng = new Rng(21);
            int count = Mathf.RoundToInt(220 * turns);
            for (int i = 0; i < count; i++)
            {
                float a = i / 220f * Mathf.PI * 2f;
                var p = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * rz) * (1f + 0.04f * Mathf.Sin(a * 3f)) + rng.InsideUnitCircle() * 0.006f;
                pts.Add(t.PointAt(p.x, p.y));
                if (kink && i == count / 3)
                    for (int k = 1; k <= 16; k++)   // a little accidental loop-the-loop
                    {
                        float b = k / 16f * Mathf.PI * 2f;
                        var q = p + new Vector2(Mathf.Cos(a + Mathf.PI / 2f), Mathf.Sin(a + Mathf.PI / 2f)) * 0.03f * (1f - Mathf.Cos(b)) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.03f * Mathf.Sin(b);
                        pts.Add(t.PointAt(q.x, q.y));
                    }
            }
            return pts;
        }

        private static void TrackTolerance(string what, Func<TerrainField, int, List<Vector3>> stroke, bool expectGrowth = false)
        {
            var w = new GameObject("Track tolerance").AddComponent<WonderlandWorld>();
            w.transform.position = new Vector3(40f, 0f, 0f);
            w.Create(2f, Materials(), 7, false);
            float land = w.Terrain.LandArea();
            var plan = w.BuildTrack(stroke(w.Terrain, 0));
            bool ok = plan.Ok && Main(w).Closed && (!expectGrowth || w.Terrain.LandArea() > land + 0.2f);
            Check(ok, what + (plan.Ok ? $" ({Main(w).Length:0.00} m{(expectGrowth ? $", land {land:0.0} -> {w.Terrain.LandArea():0.0} m2" : "")})" : $" (\"{plan.Error}\")"));
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        private static HouseInstance FindHouseSpot(WonderlandWorld world, int variant)
        {
            for (float r = 0.2f; r < 0.9f; r += 0.05f)
            for (float a = 0f; a < 360f; a += 20f)
            {
                var h = new HouseInstance { x = Mathf.Cos(a * Mathf.Deg2Rad) * r, z = Mathf.Sin(a * Mathf.Deg2Rad) * r, rotation = a, variant = variant };
                if (world.ValidateHouse(h) == null) return h;
            }
            return default;
        }

        [Serializable]
        private sealed class SaveV1
        {
            public int version; public float size; public int seed;
            public string height, locks, soil;
            public List<TreeInstance> trees = new(); public List<HouseInstance> houses = new(); public List<Vector3> track = new();
            public bool trackClosed, trainPlaced, trainRunning; public float trainS;
        }

        private static void Hold(Action action, float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt) action();
        }

        private static void Stroke(Vector3 from, Vector3 to, float seconds, Action<Vector3> action)
        {
            for (float t = 0f; t < seconds; t += Dt) action(Vector3.Lerp(from, to, t / seconds));
        }

        /// <summary>An imperfect loop like a hand would draw: wobbly radius, jitter, end short of the start.</summary>
        public static List<Vector3> HandDrawnLoop(TerrainField t, Vector2 centre, Vector2 radius)
        {
            var points = new List<Vector3>();
            var rng = new Rng(9);
            const int count = 180;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f * 0.95f;
                float wobble = 1f + 0.06f * Mathf.Sin(a * 3f + 0.7f) + 0.03f * Mathf.Sin(a * 7f);
                var p = centre + new Vector2(Mathf.Cos(a) * radius.x, Mathf.Sin(a) * radius.y) * wobble + rng.InsideUnitCircle() * 0.008f;
                points.Add(t.PointAt(p.x, p.y));
            }
            return points;
        }

        private static float Max(float[] a) { float m = float.MinValue; foreach (var v in a) m = Mathf.Max(m, v); return m; }
        private static float Min(float[] a) { float m = float.MaxValue; foreach (var v in a) m = Mathf.Min(m, v); return m; }

        private static float MaxSlope(TerrainField t, out Vector2 where)
        {
            float worst = 0f; where = default;
            for (int z = 1; z < t.Nz - 1; z++)
            for (int x = 1; x < t.Nx - 1; x++)
            {
                int i = z * t.Nx + x;
                float gx = (t.Surface[i + 1] - t.Surface[i - 1]) / (2f * t.Cell), gz = (t.Surface[i + t.Nx] - t.Surface[i - t.Nx]) / (2f * t.Cell);
                float g = Mathf.Sqrt(gx * gx + gz * gz);
                if (g > worst) { worst = g; where = new Vector2(t.LocalX(x), t.LocalZ(z)); }
            }
            return worst;
        }

        // ------------------------------------------------------------------ bridges

        /// <summary>
        /// Bridges follow from what crosses the water: a railway over a pond gets stone arches; a second track beside it
        /// shares (and widens) the same bridge; a tap restyles it (saved, undoable); a road over a stream gets a deck
        /// villagers walk across; a trail gets stepping stones, a footpath a footbridge; long spans offer a suspension bridge.
        /// </summary>
        private static void BridgeJourney()
        {
            var w = Scratch("Bridges", 170f);
            var t = w.Terrain;
            var pond = new Vector3(0.5f, 0f, 0f);
            float lvl = w.WaterLevelForStroke(pond);
            Hold(() => w.WaterStroke(pond, 0.15f, 1f, Dt, lvl, false), 1.6f);
            w.BuildTrack(Loop(t, 0.5f, 0.38f, 0.97f, false));
            var br = w.Bridges.Bridges;
            Check(br.Count == 1 && br[0].Rail && br[0].Style == BridgeStyle.Stone, $"A railway over the pond gets a stone arch bridge ({br.Count} bridge{(br.Count == 1 ? $", {br[0].Style}" : "")})");
            if (br.Count != 1) { UnityEngine.Object.DestroyImmediate(w.gameObject); return; }
            // A second track alongside: offset the loop's crossing outward and draw it as a new line.
            var layout = w.Track.Network.Edges[0].Layout;
            int near = 0; float bestD = float.MaxValue;
            for (int i = 0; i < layout.Points.Count; i++) { float d = new Vector2(layout.Points[i].x - pond.x, layout.Points[i].z - pond.z).sqrMagnitude; if (d < bestD) { bestD = d; near = i; } }
            var parallel = new List<Vector3>();
            for (int k = -42; k <= 42; k += 2)
            {
                int i = (near + k + layout.Points.Count) % layout.Points.Count;
                layout.Evaluate(layout.Cum[i], out var tan); tan.y = 0f;
                var outward = Vector3.Cross(Vector3.up, tan.normalized);
                if (Vector3.Dot(outward, layout.Points[i] - pond) < 0f && Vector3.Dot(outward, new Vector3(layout.Points[i].x, 0f, layout.Points[i].z)) < 0f) outward = -outward;
                var q = layout.Points[i] + outward * 0.1f;
                parallel.Add(t.PointAt(q.x, q.z));
            }
            var second = w.BuildTrack(parallel);
            br = w.Bridges.Bridges;
            int members = br.Count > 0 ? br.Max(b => b.Members.Count) : 0;
            Check(second.Ok && br.Count == 1 && members == 2, $"A second track beside the first shares the same, wider bridge ({br.Count} bridge, {members} lines)" + (second.Ok ? "" : $" (\"{second.Error}\")"));
            // Tap to restyle; remembered through rebuilds, save/load and undo.
            var anchor = new Vector3(br[0].Anchor.x, 0f, br[0].Anchor.y);
            string first = w.CycleBridge(anchor);
            Check(first == BridgeSystem.Names[(int)BridgeStyle.Timber] && w.Bridges.Bridges[0].Style == BridgeStyle.Timber, $"A tap on the bridge restyles it (\"{first}\")");
            string again = w.CycleBridge(anchor);
            Check(w.Bridges.Bridges[0].Style == BridgeStyle.Truss, $"Another tap: the next style (\"{again}\")");
            w.RefreshBridges();
            Check(w.Bridges.Bridges[0].Style == BridgeStyle.Truss, "The chosen style stays when the bridge is rebuilt");
            string json = w.Serialize();
            var copy = new GameObject("Bridge copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(180f, 0f, 0f);
            copy.Deserialize(json, Materials());
            Check(copy.Bridges.Bridges.Count == 1 && copy.Bridges.Bridges[0].Style == BridgeStyle.Truss, "Save/load keeps the bridge style");
            UnityEngine.Object.DestroyImmediate(copy.gameObject);
            w.Undo();
            Check(w.Bridges.Bridges[0].Style == BridgeStyle.Timber, "Undo takes the last restyle back");
            var allowed = BridgeSystem.Allowed(w.Bridges.Bridges[0]);
            Check(allowed.Contains(BridgeStyle.Truss) && !allowed.Contains(BridgeStyle.Footbridge), $"A railway bridge offers railway styles ({string.Join(", ", allowed)})");

            // A road over a stream: a deck above the water that villagers walk across.
            var stream = w.BuildStream(Stroke(t, new Vector2(-0.6f, -0.75f), new Vector2(-0.3f, -0.2f), new Vector2(-0.35f, 0.3f)));
            int before = w.Bridges.Bridges.Count;
            string road = w.BuildPath(Stroke(t, new Vector2(-0.6f, -0.22f), new Vector2(-0.05f, -0.2f)), PathKind.Road);
            var roadBridge = w.Bridges.Bridges.Find(b => b.Road);
            Check(stream == null && road == null && roadBridge != null && roadBridge.Style == BridgeStyle.Stone, "A road across the stream gets a bridge" + (stream ?? road ?? ""));
            if (roadBridge != null)
            {
                var a = roadBridge.Anchor;
                float water = w.WaterLevelFor(a);
                bool deck = w.Bridges.Deck(a, out float deckH, out _);
                Check(deck && deckH > water + 0.015f, $"The road rises onto a deck above the water ({(deckH - water) * 1000f:0} mm clear)");
                Check(w.People.Walkable(a), "Villagers can walk across the road bridge");
            }
            // A trail and a footpath over the stream.
            w.BuildPath(Stroke(t, new Vector2(-0.55f, 0.05f), new Vector2(-0.15f, 0.06f)), PathKind.Trail);
            w.BuildPath(Stroke(t, new Vector2(-0.6f, -0.45f), new Vector2(-0.2f, -0.45f)), PathKind.Footpath);
            Check(w.Bridges.Bridges.Exists(b => b.Style == BridgeStyle.Stones), "A trail crosses the stream on stepping stones");
            Check(w.Bridges.Bridges.Exists(b => b.Style == BridgeStyle.Footbridge), "A footpath crosses on a little footbridge");
            Check(w.Bridges.Bridges.Count >= before + 3, $"Each crossing has its bridge ({w.Bridges.Bridges.Count})");
            // Long spans may hang from cables; short ones may not.
            var longest = w.Bridges.Bridges.OrderByDescending(b => b.Length).First();
            var shortest = w.Bridges.Bridges.OrderBy(b => b.Length).First();
            Check(!BridgeSystem.Allowed(shortest).Contains(BridgeStyle.Suspension) || shortest.Length >= 0.16f, $"Short spans don't offer a suspension bridge ({shortest.Length * 100f:0} cm)");
            Check(longest.Length < 0.26f || BridgeSystem.Allowed(longest).Contains(BridgeStyle.Suspension), $"Long spans do ({longest.Length * 100f:0} cm)");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        // ------------------------------------------------------------------ a richer, living world

        private static List<Vector3> Stroke(TerrainField t, params Vector2[] via) => Line(t, via);

        private static string Put(WonderlandWorld w, int id, float x, float z, float rotation = 0f)
        {
            var h = new HouseInstance { x = x, z = z, rotation = rotation, variant = id, style = 0 };
            return w.PlaceHouse(h);
        }

        /// <summary>
        /// The object library, paths and the life in them: every catalogue object builds; a village square and shops get
        /// residents who sit, gather and browse; the fair gets riders; roads grade the ground, join up and carry walkers;
        /// trails carry hikers; a fenced pasture keeps its sheep; a level crossing lets villagers over the rails; ducks swim
        /// on the pond; deer keep to the woods and shy away; the Peaks brush makes craggy mountains; save/load and undo.
        /// </summary>
        private static void LivingJourney()
        {
            // Every object in the library builds, within a sensible vertex budget.
            int worst = 0; string worstName = "";
            bool allBuild = true;
            for (int id = 0; id < Catalog.Count; id++)
            {
                var bp = Catalog.Get(id);
                for (int st = 0; st < bp.Styles; st++)
                {
                    var m = HouseFactory.Get(id, st);
                    if (m == null || m.vertexCount == 0) allBuild = false;
                    else if (m.vertexCount > worst) { worst = m.vertexCount; worstName = bp.Name; }
                }
                if (bp.RotorSpeed != 0f && HouseFactory.Rotor(id) == null) allBuild = false;
            }
            Check(allBuild && Catalog.Count >= 40, $"Every one of the {Catalog.Count} library objects builds (largest: {worstName}, {worst} vertices)");
            Check(worst < 60000, "Library objects stay within the vertex budget");

            var w = Scratch("Living", 140f);
            var t = w.Terrain;
            // A village square: fountain, church, café, bakery, grocer, a bench and homes around.
            int peopleBefore = w.People.People.Count;
            var placed = new List<(int id, string error)>
            {
                (Catalog.Fountain, Put(w, Catalog.Fountain, 0f, 0f)),
                (Catalog.Church, Put(w, Catalog.Church, 0f, 0.26f, 180f)),
                (Catalog.Cafe, Put(w, Catalog.Cafe, -0.24f, 0.04f, 90f)),
                (Catalog.Bakery, Put(w, Catalog.Bakery, 0.24f, 0.04f, -90f)),
                (Catalog.Grocer, Put(w, Catalog.Grocer, 0.24f, -0.17f, -90f)),
                (Catalog.Bench, Put(w, Catalog.Bench, -0.07f, -0.12f, 180f)),
                (Catalog.Cottage, Put(w, Catalog.Cottage, -0.26f, -0.22f, 60f)),
                (Catalog.Apartments, Put(w, Catalog.Apartments, 0.0f, -0.3f, 0f)),
                (Catalog.Lamp, Put(w, Catalog.Lamp, 0.07f, -0.08f)),
            };
            foreach (var (id, error) in placed) if (error != null) Check(false, $"Place {Catalog.Get(id).Name} (\"{error}\")");
            int built = placed.FindAll(p => p.error == null).Count;
            Check(built == placed.Count, $"A village square goes up ({built}/{placed.Count} places)");
            int residents = w.People.People.Count - peopleBefore;
            Check(residents >= 4, $"New homes and shops bring their residents ({residents} moved in)");
            var seen = new HashSet<PeopleSystem.Activity>();
            bool safe = true;
            for (int step = 0; step < 72 * 150; step++)
            {
                w.People.Step(Dt);
                if (step % 12 != 0) continue;
                for (int i = 0; i < w.People.People.Count; i++)
                {
                    var st = w.People.StateOf(i);
                    seen.Add(st);
                    if (st == PeopleSystem.Activity.OnRide || st == PeopleSystem.Activity.Sitting) continue;
                    if (!w.People.Walkable(w.People.PositionOf(i))) safe = false;
                }
            }
            Check(seen.Contains(PeopleSystem.Activity.Sitting), "Villagers sit down at the café and on the bench");
            Check(seen.Contains(PeopleSystem.Activity.Gathering), "Villagers gather at the fountain and the church");
            Check(seen.Contains(PeopleSystem.Activity.Browsing), "Villagers browse the bakery and the grocer's crates");
            Check(safe, "Villagers keep to walkable ground while busy");
            // A fair on the meadow: carousel, Ferris wheel, playground.
            string fairError = Put(w, Catalog.Carousel, 0.55f, 0.3f) ?? Put(w, Catalog.FerrisWheel, 0.6f, -0.12f, -90f) ?? Put(w, Catalog.Playground, 0.45f, 0.62f);
            Check(fairError == null, "A fair: carousel, Ferris wheel and playground" + (fairError == null ? "" : $" (\"{fairError}\")"));
            int wheel = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.FerrisWheel);
            var rotor = w.Buildings.RotorOf(wheel);
            w.Buildings.Animate(0f, 0f); var r0 = rotor != null ? rotor.localRotation : Quaternion.identity;
            w.Buildings.Animate(2f, 2f); var r1 = rotor != null ? rotor.localRotation : Quaternion.identity;
            Check(rotor != null && Quaternion.Angle(r0, r1) > 10f, $"The Ferris wheel turns ({Quaternion.Angle(r0, r1):0} degrees in 2 s)");
            seen.Clear();
            w.Clock.Hour = 15f;   // the fair is busiest in the afternoon
            for (int step = 0; step < 72 * 150; step++) { w.People.Step(Dt); if (step % 12 == 0) for (int i = 0; i < w.People.People.Count; i++) seen.Add(w.People.StateOf(i)); }
            Check(seen.Contains(PeopleSystem.Activity.OnRide), "Visitors take rides at the fair");
            Check(seen.Contains(PeopleSystem.Activity.Playing) || seen.Contains(PeopleSystem.Activity.Watching), "Visitors play and watch at the fair");

            // Roads and paths: a road across the meadow grades the ground smooth across it, and a footpath joins it.
            Hold(() => w.TerrainStroke(TerrainBrush.Raise, new Vector3(-0.45f, 0f, -0.47f), 0.16f, 0.6f, Dt, 0f), 1f);
            float crossBefore = Mathf.Abs(t.SurfaceAt(-0.45f, -0.43f) - t.SurfaceAt(-0.45f, -0.47f));
            string road = w.BuildPath(Stroke(t, new Vector2(-0.7f, -0.45f), new Vector2(-0.3f, -0.45f), new Vector2(-0.05f, -0.42f)), PathKind.Road);
            float crossAfter = Mathf.Abs(t.SurfaceAt(-0.45f, -0.43f) - t.SurfaceAt(-0.45f, -0.47f));
            Check(road == null && w.Paths.Lines.Count == 1, "A road is drawn across the meadow" + (road == null ? "" : $" (\"{road}\")"));
            if (w.Paths.Lines.Count == 0) { UnityEngine.Object.DestroyImmediate(w.gameObject); return; }
            Check(crossAfter < crossBefore * 0.6f + 0.0005f, $"The road smooths the ground across its width ({crossBefore * 1000f:0.0} -> {crossAfter * 1000f:0.0} mm side to side)");
            var roadEnd = w.Paths.Lines[0].points[^1];
            string path = w.BuildPath(Stroke(t, roadEnd + new Vector2(0.02f, 0.01f), new Vector2(-0.05f, -0.2f), new Vector2(-0.08f, -0.1f)), PathKind.Footpath);
            Check(path == null && w.Paths.Lines.Count == 2 && (w.Paths.Lines[1].points[0] - roadEnd).magnitude < 0.005f, "A footpath drawn from the road's end joins it");
            var route = w.Paths.Route(new Vector2(-0.68f, -0.45f), new Vector2(-0.08f, -0.11f));
            Check(route != null && route.Count > 5, $"Villagers find a walking route along the road and the path ({route?.Count ?? 0} waypoints)");
            string onRoad = w.ValidateHouse(new HouseInstance { x = -0.5f, z = -0.45f, variant = Catalog.Cottage });
            Check(onRoad != null && onRoad.StartsWith("That's the road"), $"Houses are not built on the road (\"{onRoad}\")");
            string lampError = w.ValidateHouse(new HouseInstance { x = -0.55f, z = -0.415f, variant = Catalog.Lamp });
            Check(lampError == null, "A street lamp may stand at the roadside" + (lampError == null ? "" : $" (\"{lampError}\")"));
            // A hiking trail and a hiker.
            string trail = w.BuildPath(Stroke(t, new Vector2(-0.2f, 0.55f), new Vector2(-0.5f, 0.6f), new Vector2(-0.75f, 0.35f), new Vector2(-0.6f, 0.15f)), PathKind.Trail);
            // Homes by the trail: their people plan hikes on their afternoons out (fast days: three afternoons).
            string trailHomes = Put(w, Catalog.Cottage, -0.36f, 0.47f, 160f) ?? Put(w, Catalog.Thatched, -0.62f, 0.45f, 120f);
            w.Clock.Hour = 13.5f; w.Clock.Mode = ClockMode.Fast;
            bool hiked = false;
            for (int step = 0; step < 72 * 400 && !hiked; step++) { w.Clock.Tick(Dt); w.People.Step(Dt); if (step % 24 == 0) for (int i = 0; i < w.People.People.Count; i++) hiked |= w.People.Hiking(i); }
            w.Clock.Mode = ClockMode.Normal;
            string hikers = trailHomes != null ? $" (\"{trailHomes}\")" : "";
            Check(trail == null && hiked, "A hiker sets off along the trail" + hikers);

            // A fenced pasture keeps its sheep in; sheep outside stay out.
            var fence = new List<Vector3>();
            for (int i = 0; i <= 64; i++) { float a = i / 64f * Mathf.PI * 2f; fence.Add(t.PointAt(-0.55f + Mathf.Cos(a) * 0.2f, 0.0f + Mathf.Sin(a) * 0.15f)); }
            string fenceError = w.BuildPath(fence, PathKind.Fence);
            int pasture = w.Paths.Lines.Count - 1;
            Check(fenceError == null && w.Paths.Lines[pasture].closed, "A fence drawn round a meadow closes into a pasture");
            int sheepBefore = w.Animals.Animals.Count;
            for (int i = 0; i < 3; i++) w.PlaceAnimal(t.PointAt(-0.6f + i * 0.05f, 0.02f), AnimalKind.Sheep, 50 + i);
            w.PlaceAnimal(t.PointAt(-0.55f, 0.24f), AnimalKind.Horse, 60);
            bool kept = true, outside = true;
            for (int step = 0; step < 72 * 120; step++)
            {
                w.Animals.Step(Dt);
                if (step % 24 != 0) continue;
                for (int i = sheepBefore; i < sheepBefore + 3; i++) if (w.Paths.EnclosureAt(w.Animals.PositionOf(i)) != pasture) kept = false;
                if (w.Paths.EnclosureAt(w.Animals.PositionOf(sheepBefore + 3)) == pasture) outside = false;
            }
            Check(kept, "Sheep stay inside their pasture");
            Check(outside, "The horse outside never wanders into the pasture");

            // Ducks on a pond; deer by the woods, shy of people.
            var pond = new Vector3(0.5f, 0f, -0.6f);
            float lvl = w.WaterLevelForStroke(pond);
            Hold(() => w.WaterStroke(pond, 0.13f, 0.9f, Dt, lvl, false), 1.5f);
            Check(w.PlaceAnimal(t.PointAt(-0.3f, -0.1f), AnimalKind.Duck, 1) != null, "Ducks are refused on dry land");
            int duck = w.Animals.Animals.Count;
            string duckError = w.PlaceAnimal(pond, AnimalKind.Duck, 70);
            bool afloat = duckError == null;
            for (int step = 0; step < 72 * 60 && afloat; step++) { w.Animals.Step(Dt); if (step % 24 == 0 && t.WaterAt(w.Animals.PositionOf(duck).x, w.Animals.PositionOf(duck).y) < 0.5f) afloat = false; }
            Check(afloat, "The duck paddles about on the pond" + (duckError == null ? "" : $" (\"{duckError}\")"));
            string openDeer = w.PlaceAnimal(t.PointAt(0.1f, 0.6f), AnimalKind.Deer, 1);
            Check(openDeer != null, $"Deer won't settle in the open (\"{openDeer}\")");
            var wood = new Vector3(-0.7f, 0f, -0.25f);
            for (int i = 0; i < 8; i++) w.PaintForest(wood + new Vector3(Mathf.Sin(i) * 0.08f, 0f, Mathf.Cos(i * 1.3f) * 0.06f), 0.1f, 0.8f, Dt * 4f);
            int deer = w.Animals.Animals.Count;
            string deerError = w.PlaceAnimal(wood + new Vector3(0.11f, 0f, 0.02f), AnimalKind.Deer, 80);
            if (deerError != null) for (int k = 0; k < 20 && deerError != null; k++) deerError = w.PlaceAnimal(wood + new Vector3(Mathf.Cos(k) * 0.12f, 0f, Mathf.Sin(k) * 0.1f), AnimalKind.Deer, 80 + k);
            Check(deerError == null, "A deer makes its home at the edge of the wood" + (deerError == null ? "" : $" (\"{deerError}\")"));
            if (deerError == null)
            {
                var d = w.Animals.PositionOf(deer);
                w.PlacePerson(new Vector3(d.x + 0.05f, 0f, d.y), 1, 99);
                bool fled = false;
                for (int step = 0; step < 72 * 10 && !fled; step++) { w.Animals.Step(Dt); fled |= w.Animals.IsFleeing(deer); }
                Check(fled, "The deer bolts when a villager comes close");
            }

            // A level crossing: a lane over a railway lets villagers cross the rails there (and only there).
            var loopPlan = w.BuildTrack(Loop(t, 0.95f, 0.85f, 0.97f, false));
            var east = w.Track.HasTrack ? w.Track.Network.Edges[0].Layout.Points[0] : new Vector3(0.95f, 0f, 0f);
            string lane = w.BuildPath(Stroke(t, new Vector2(east.x - 0.18f, east.z + 0.03f), new Vector2(east.x + 0.12f, east.z + 0.05f)), PathKind.Lane);
            Check(lane == null && w.Paths.LevelCrossings.Count >= 1, $"A lane across the railway gets a level crossing ({w.Paths.LevelCrossings.Count}{(loopPlan.Ok ? "" : ", railway: " + loopPlan.Error)}{(lane == null ? "" : ", lane: " + lane)})");
            if (w.Paths.LevelCrossings.Count > 0)
            {
                var c = w.Paths.LevelCrossings[0].at;
                Check(w.People.Walkable(new Vector2(c.x, c.z)), "Villagers may cross the rails at the level crossing");
                var elsewhere = w.Track.Network.Edges[0].Layout.Points[w.Track.Network.Edges[0].Layout.Points.Count / 2];
                Check(!w.People.Walkable(new Vector2(elsewhere.x, elsewhere.z)), "...but not across the open line");
            }

            // Peaks: craggy mountains, rougher than a raised hill.
            var peak = new Vector3(0.0f, 0f, 0.65f);
            Hold(() => w.TerrainStroke(TerrainBrush.Peaks, peak, 0.2f, 1f, Dt, 0f), 4f);
            float top = 0f, rough = 0f;
            for (float x = -0.12f; x <= 0.12f; x += 0.02f)
            for (float z = -0.12f; z <= 0.12f; z += 0.02f)
            {
                float h = t.SurfaceAt(peak.x + x, peak.z + z);
                top = Mathf.Max(top, h);
                rough += Mathf.Abs(h - (t.SurfaceAt(peak.x + x + 0.02f, peak.z + z) + t.SurfaceAt(peak.x + x - 0.02f, peak.z + z)) * 0.5f);
            }
            Check(top > 0.12f, $"The Peaks brush raises a mountain ({top * 100f:0} cm high)");
            Check(rough > 0.01f, $"...with craggy, uneven slopes (roughness {rough * 1000f:0})");

            // Undo takes a path back; save/load keeps paths, new animals and places.
            int linesBefore = w.Paths.Lines.Count;
            w.BuildPath(Stroke(t, new Vector2(0.3f, 0.3f), new Vector2(0.35f, 0.5f)), PathKind.Hedge);
            w.Undo();
            Check(w.Paths.Lines.Count == linesBefore, "Undo takes the hedge away again");
            string json = w.Serialize();
            var copy = new GameObject("Living copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(150f, 0f, 0f);
            copy.Deserialize(json, Materials());
            Check(copy.Paths.Lines.Count == w.Paths.Lines.Count && copy.Paths.Lines.Exists(l => l.closed), $"Save/load keeps roads, paths and the pasture fence ({copy.Paths.Lines.Count})");
            Check(copy.Animals.Animals.Exists(a => a.kind == (int)AnimalKind.Duck) && copy.Buildings.Houses.Exists(h => h.variant == Catalog.Carousel), "Save/load keeps the ducks and the fair");
            UnityEngine.Object.DestroyImmediate(copy.gameObject);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        // ------------------------------------------------------------------ railway network

        private static WonderlandWorld Scratch(string name, float x)
        {
            var w = new GameObject(name).AddComponent<WonderlandWorld>();
            w.transform.position = new Vector3(x, 0f, 0f);
            w.Create(2f, Materials(), 7, false);
            return w;
        }

        private static List<Vector3> Line(TerrainField t, params Vector2[] via)
        {
            var pts = new List<Vector3>();
            for (int k = 0; k < via.Length - 1; k++)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt((via[k + 1] - via[k]).magnitude / 0.012f));
                for (int i = 0; i < n; i++) { var q = Vector2.Lerp(via[k], via[k + 1], i / (float)n); pts.Add(t.PointAt(q.x, q.y)); }
            }
            pts.Add(t.PointAt(via[^1].x, via[^1].y));
            return pts;
        }

        private static int DeadEnds(TrackNetwork n)
        {
            int c = 0;
            foreach (var e in n.Edges) if (!e.Closed) for (int k = 0; k < 2; k++) if (e.Joint[k] < 0) c++;
            return c;
        }

        /// <summary>Runs the trains; returns the closest approach between cars of different trains and the worst offset from the rails.</summary>
        private static (float gap, float offRails) RunTrains(WonderlandWorld w, float seconds, Action<int> each = null)
        {
            float gap = float.MaxValue, off = 0f;
            for (int step = 0; step < Mathf.RoundToInt(seconds * 72f); step++)
            {
                w.Trains.Step(Dt);
                each?.Invoke(step);
                if (step % 6 != 0) continue;
                var samples = new List<List<Vector3>>();
                foreach (var tr in w.Trains.Trains) samples.Add(w.Trains.CarSamples(tr));
                for (int a = 0; a < samples.Count; a++)
                {
                    foreach (var p in samples[a]) off = Mathf.Max(off, w.Track.Network.DistanceTo(p));
                    for (int b = a + 1; b < samples.Count; b++)
                        foreach (var p in samples[a]) foreach (var q in samples[b]) gap = Mathf.Min(gap, new Vector2(p.x - q.x, p.z - q.z).magnitude);
                }
            }
            return (gap, off);
        }

        private static bool OnEdge(Train t, int edge) { foreach (var sp in t.Path) if (sp.Edge == edge) return true; return false; }

        /// <summary>
        /// The railway network, built up the way a child would: a loop, a branch to a dead end, extending it, joining it
        /// back, a second and third train, a figure 8 and a line across the loop (diamond crossings), stop signals, the
        /// switch modes, erasing a piece, undo and save/load.
        /// </summary>
        private static void NetworkJourney()
        {
            {
                // A figure 8 becomes one loop with a diamond crossing; the train runs through it on the rails.
                var w = Scratch("Figure 8", 80f);
                var eight = new List<Vector3>();
                for (int i = 0; i <= 300; i++) { float a = i / 300f * Mathf.PI * 2f * 0.97f; eight.Add(w.Terrain.PointAt(Mathf.Sin(a) * 0.62f, Mathf.Sin(a) * Mathf.Cos(a) * 0.62f)); }
                var plan = w.BuildTrack(eight);
                var n = w.Track.Network;
                Check(plan.Ok && n.Edges.Count == 1 && n.Edges[0].Closed && n.Crossings.Count == 1, "A figure 8 becomes a loop with a diamond crossing" + (plan.Ok ? $" ({n.Crossings.Count} crossing)" : $" (\"{plan.Error}\")"));
                if (plan.Ok && n.Crossings.Count == 1)
                {
                    var c = n.Crossings[0];
                    float ha = n.Edges[c.EdgeA].Layout.Evaluate(c.SA, out _).y, hb = n.Edges[c.EdgeB].Layout.Evaluate(c.SB, out _).y;
                    Check(Mathf.Abs(ha - hb) < 0.001f, $"Both tracks meet at the same height at the crossing ({Mathf.Abs(ha - hb) * 1000f:0.0} mm)");
                    PlaceTrainAt(w, n.Edges[0].Layout.Points[n.Edges[0].Layout.Points.Count / 4]);
                    var (_, off) = RunTrains(w, 40f);
                    Check(w.Trains.Any && FirstTrain(w).Odometer > 4f && off < 0.006f, $"The train runs the figure 8 through its crossing ({(w.Trains.Any ? FirstTrain(w).Odometer : 0f):0.0} m, worst offset {off * 1000f:0.0} mm)");
                }
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            {
                var w = Scratch("Network", 90f);
                var t = w.Terrain;
                var loop = w.BuildTrack(Loop(t, 0.5f, 0.38f, 0.97f, false));
                Check(loop.Ok, "Network: a first loop");
                if (!loop.Ok) { UnityEngine.Object.DestroyImmediate(w.gameObject); return; }
                // Stop signal: a red signal holds the train; green lets it go.
                {
                    var side = t.PointAt(-0.58f, 0f);
                    string sigError = w.PlaceSignal(side);
                    Check(sigError == null && w.Track.Signals.Count == 1 && !w.Track.Signals[0].red, "A signal goes up beside the track, on automatic" + (sigError == null ? "" : $" (\"{sigError}\")"));
                    w.ToggleSignal(0);   // held at red
                    PlaceTrainAt(w, t.PointAt(0f, 0.4f));
                    var tr = FirstTrain(w);
                    RunTrains(w, 30f);
                    float odo = tr.Odometer;
                    RunTrains(w, 8f);
                    Check(tr.Odometer - odo < 0.01f && tr.Blocked == BlockReason.Signal, $"The train waits at the red signal (moved {(tr.Odometer - odo) * 100f:0.0} cm in 8 s)");
                    w.ToggleSignal(0);
                    RunTrains(w, 6f);
                    Check(tr.Odometer - odo > 0.3f, "Green: the train goes on");
                    w.EraseAt(side + Vector3.up, Vector3.down, side);
                    Check(w.Track.Signals.Count == 0, "The eraser takes the signal away");
                }
                // Branch: draw from the loop outwards into the meadow; a switch appears and the new line ends at buffers.
                var from = new Vector2(0.52f, 0.02f);
                var branchStroke = Line(t, from, from + new Vector2(0.12f, 0.2f), from + new Vector2(0.32f, 0.48f));
                var branch = w.BuildTrack(branchStroke);
                var n = w.Track.Network;
                Check(branch.Ok && n.Joints.Count == 1 && DeadEnds(n) == 1 && n.Edges.Count == 2, "Drawing from the loop outwards branches off with a switch to a dead end" + (branch.Ok ? $" ({n.Joints.Count} switch, {n.Edges.Count} pieces)" : $" (\"{branch.Error}\")"));
                if (!branch.Ok || n.Joints.Count != 1) { UnityEngine.Object.DestroyImmediate(w.gameObject); return; }
                int branchEdge = n.Edges[0].Closed || n.Edges[0].Joint[0] >= 0 && n.Edges[0].Joint[1] >= 0 ? 1 : 0;
                var j = n.Joints[0];
                float leave = Vector3.Angle(n.IntoEdge(j.Branch(0)), n.IntoEdge(j.Branch(1)));
                Check(leave < 25f, $"The branch leaves the loop smoothly, like a real switch ({leave:0} degrees apart at the switch)");
                var tr0 = FirstTrain(w);
                Check(tr0 != null, "The train is still on the track after the switch went in");
                // Manual: the switch set away from the branch keeps the train on the loop.
                w.Trains.Mode = SwitchMode.Manual;
                int toLoop = j.Branch(0).Edge == branchEdge ? 1 : 0;
                j.Setting = toLoop;
                bool strayed = false;
                RunTrains(w, 40f, _ => { if (tr0 != null && OnEdge(tr0, branchEdge)) strayed = true; });
                Check(!strayed, "Manual switches: a switch set for the loop keeps the train on it");
                // Explore: the train takes the branch, reverses at the buffers and comes back.
                w.Trains.Mode = SwitchMode.Explore;
                bool visited = false, reversed = false, back = false;
                int turns0 = tr0 != null ? tr0.Reversals : 0;
                RunTrains(w, 120f, _ =>
                {
                    if (tr0 == null) return;
                    if (OnEdge(tr0, branchEdge)) visited = true;
                    if (tr0.Reversals != turns0) reversed = true;   // (it turns round: the engine runs round to lead again)
                    if (visited && reversed && !OnEdge(tr0, branchEdge)) back = true;
                });
                Check(!visited && !reversed, $"One way: the train keeps to the line it can run round and off the dead-end branch (branch {visited}, reversed {reversed})");
                // Extend the dead end.
                n = w.Track.Network;
                Vector3 deadEnd = default;
                foreach (var e in n.Edges) if (!e.Closed) for (int k = 0; k < 2; k++) if (e.Joint[k] < 0) deadEnd = k == 0 ? e.Layout.Points[0] : e.Layout.Points[^1];
                float lengthBefore = n.TotalLength;
                var dead2 = new Vector2(deadEnd.x, deadEnd.z);
                var extend = w.BuildTrack(Line(t, dead2, dead2 + new Vector2(0.05f, 0.3f)));
                n = w.Track.Network;
                Check(extend.Ok && n.Edges.Count == 2 && DeadEnds(n) == 1 && n.TotalLength > lengthBefore + 0.2f, "Drawing on from the buffers extends the line" + (extend.Ok ? $" ({lengthBefore:0.00} -> {n.TotalLength:0.00} m)" : $" (\"{extend.Error}\")"));
                // Join it back onto the loop: a second switch, no dead ends left.
                foreach (var e in n.Edges) if (!e.Closed) for (int k = 0; k < 2; k++) if (e.Joint[k] < 0) deadEnd = k == 0 ? e.Layout.Points[0] : e.Layout.Points[^1];
                dead2 = new Vector2(deadEnd.x, deadEnd.z);
                var join = w.BuildTrack(Line(t, dead2, dead2 + new Vector2(-0.15f, 0.3f), new Vector2(0.15f, 0.95f), new Vector2(-0.35f, 0.75f), new Vector2(-0.3f, 0.36f)));
                n = w.Track.Network;
                Check(join.Ok && n.Joints.Count == 2 && DeadEnds(n) == 0, "Drawing from the end back onto the loop joins it with a second switch" + (join.Ok ? $" ({n.Joints.Count} switches, {n.Edges.Count} pieces)" : $" (\"{join.Error}\")"));
                int undoDepth = w.UndoDepth;
                // More trains sharing the track: they never touch, and all keep going.
                string second = w.PlaceTrain(t.PointAt(-0.5f, -0.05f), TrainKind.Goods, Vector3.back, out var goods);
                string third = w.PlaceTrain(t.PointAt(0.05f, -0.4f), TrainKind.Railcar, Vector3.right, out var railcar);
                Check(second == null && third == null && w.Trains.Trains.Count == 3, $"A goods train and a railcar join the steam train ({w.Trains.Trains.Count} trains)" + (second ?? third ?? ""));
                Check(w.Trains.Trains.Count < 2 || w.Trains.Trains[0].Livery != w.Trains.Trains[1].Livery, "Every train gets its own colours");
                var odo0 = new List<float>(); foreach (var tr in w.Trains.Trains) odo0.Add(tr.Odometer);
                var (gap, offRails) = RunTrains(w, 180f);
                bool allMoved = true;
                for (int i = 0; i < w.Trains.Trains.Count; i++) if (w.Trains.Trains[i].Odometer - odo0[i] < 3f) allMoved = false;
                Check(gap > 0.04f, $"Trains sharing the network never touch (closest {gap * 100f:0.0} cm between cars in 3 minutes)");
                Check(allMoved, "All trains keep running (no jams)" + $" ({string.Join(", ", w.Trains.Trains.ConvertAll(x => x.Odometer.ToString("0.0")))} m)");
                Check(offRails < 0.006f, $"Every train stays on the rails through the switches (worst offset {offRails * 1000f:0.0} mm)");
                // Speed: a slower train is slower.
                if (w.Trains.Trains.Count == 3)
                {
                    w.Trains.Trains[2].SpeedFactor = 0.5f;
                    RunTrains(w, 10f);
                    Check(w.Trains.Trains[2].Speed <= TrainSystem.CruiseSpeed * 0.5f + 0.001f, $"Speed setting: half speed is half speed ({w.Trains.Trains[2].Speed * 100f:0.0} cm/s)");
                }
                // A tapped switch is pinned: in Explore mode trains leave it alone.
                var sw = w.Track.Network.Joints[0];
                string msg = null;
                for (int k = 0; k < 600 && msg == null; k++) { w.Trains.Step(Dt); if (!w.Trains.JointLocked(0)) msg = w.FlipSwitch(0); }
                int pinned = sw.Setting;
                RunTrains(w, 40f);
                Check(msg != null && sw.Pinned && sw.Setting == pinned, $"A tapped switch stays where it was set (\"{msg}\")");
                // Grip and drag: a piece bends like a rope between its switches; the trains stay on it.
                {
                    n = w.Track.Network;
                    int longest = 0;
                    for (int e = 1; e < n.Edges.Count; e++) if (n.Edges[e].Layout.Length > n.Edges[longest].Layout.Length) longest = e;
                    var l = n.Edges[longest].Layout;
                    int idx = l.Points.Count / 2;
                    var p0 = l.Points[idx];
                    var outward = new Vector3(p0.x, 0f, p0.z).normalized;
                    var target = p0 + outward * 0.12f;
                    int trainsBefore = w.Trains.Trains.Count, piecesBefore = n.Edges.Count;
                    string bend = w.Reshape(longest, idx, target);
                    n = w.Track.Network;
                    float reached = n.DistanceTo(target);
                    Check(bend == null && reached < 0.03f && n.Edges.Count == piecesBefore && n.Joints.Count == 2, $"Grip and drag bends a piece of track (moved 12 cm, track within {reached * 100f:0.0} cm of the hand)" + (bend == null ? "" : $" (\"{bend}\")"));
                    Check(w.Trains.Trains.Count == trainsBefore, $"The trains stay on the bent track ({w.Trains.Trains.Count}/{trainsBefore})");
                    var (g2, off2) = RunTrains(w, 30f);
                    Check(g2 > 0.04f && off2 < 0.006f, $"Trains run on over the bent track (closest {g2 * 100f:0.0} cm)");
                    var squeeze = w.ReshapePreview(longest, idx, Vector3.zero, out _);
                    Check(squeeze != null, $"A bend that would squeeze into other track is refused (\"{squeeze}\")");
                }
                // Save / load keeps the whole network, its trains, switch settings and the mode.
                string json = w.Serialize();
                var copy = new GameObject("Network copy").AddComponent<WonderlandWorld>();
                copy.transform.position = new Vector3(100f, 0f, 0f);
                copy.Deserialize(json, Materials());
                var cn = copy.Track.Network;
                Check(cn.Edges.Count == w.Track.Network.Edges.Count && cn.Joints.Count == 2 && copy.Trains.Trains.Count == w.Trains.Trains.Count && cn.Joints[0].Pinned,
                    $"Save/load keeps the network ({cn.Edges.Count} pieces, {cn.Joints.Count} switches, {copy.Trains.Trains.Count} trains)");
                UnityEngine.Object.DestroyImmediate(copy.gameObject);
                // Erase the connecting piece: the switches disappear and the loop is whole again; undo brings it back.
                n = w.Track.Network;
                int connector = -1;
                for (int e = 0; e < n.Edges.Count; e++) if (n.Edges[e].Joint[0] >= 0 && n.Edges[e].Joint[1] >= 0 && n.Edges[e].Joint[0] != n.Edges[e].Joint[1] && n.Edges[e].Layout.Length > 1f) connector = e;
                if (connector < 0) for (int e = 0; e < n.Edges.Count; e++) if (n.Edges[e].Layout.Length > 1f && connector < 0) connector = e;
                var mid = n.Edges[Mathf.Max(0, connector)].Layout.Points[n.Edges[Mathf.Max(0, connector)].Layout.Points.Count / 2];
                string erased = w.EraseAt(mid + Vector3.up, Vector3.down, mid);
                if (erased == "Train removed") erased = w.EraseAt(mid + Vector3.up, Vector3.down, mid);
                n = w.Track.Network;
                Check(erased == "Track removed" && n.Joints.Count < 2, $"The eraser takes away one piece of track (\"{erased}\": {n.Joints.Count} switches, {n.Edges.Count} pieces left)");
                RunTrains(w, 20f);
                w.Undo();
                if (erased == "Track removed") Check(w.Track.Network.Joints.Count == 2, "Undo puts the piece back");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            {
                // A line across the loop crosses it twice; a train on each never meet at the crossings.
                var w = Scratch("Crossings", 110f);
                var t = w.Terrain;
                w.BuildTrack(Loop(t, 0.5f, 0.38f, 0.97f, false));
                var across = w.BuildTrack(Line(t, new Vector2(-0.95f, -0.12f), new Vector2(0.95f, 0.12f)));
                var n = w.Track.Network;
                Check(across.Ok && n.Crossings.Count == 2 && n.Joints.Count == 0, "A line drawn across the loop crosses it with two diamond crossings" + (across.Ok ? $" ({n.Crossings.Count})" : $" (\"{across.Error}\")"));
                PlaceTrainAt(w, t.PointAt(0f, 0.4f));
                w.PlaceTrain(t.PointAt(-0.75f, -0.095f), TrainKind.Railcar, Vector3.right, out _);
                var (gap, off) = RunTrains(w, 150f);
                Check(w.Trains.Trains.Count == 2 && gap > 0.04f, $"Trains take turns at the crossings (closest {gap * 100f:0.0} cm)");
                Check(w.Trains.Trains.Count == 2 && w.Trains.Trains[1].Odometer > 0.5f && w.Trains.Trains[1].Reversals == 0 && w.Trains.Trains[1].Dir > 0,
                    $"The railcar runs along its line the way the arrows point and stops at the buffers - it never reverses ({(w.Trains.Trains.Count == 2 ? w.Trains.Trains[1].Odometer : 0f):0.0} m)");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            {
                // Two trains on a single line, put on facing each other: both are set the way the line runs - no head-on.
                var w = Scratch("Single line", 120f);
                var t = w.Terrain;
                var line = w.BuildTrack(Line(t, new Vector2(-0.9f, -0.2f), new Vector2(-0.3f, 0.1f), new Vector2(0.3f, -0.1f), new Vector2(0.9f, 0.2f)));
                Check(line.Ok && DeadEnds(w.Track.Network) == 2, "An open line with buffers at both ends" + (line.Ok ? "" : $" (\"{line.Error}\")"));
                w.PlaceTrain(t.PointAt(-0.6f, -0.05f), TrainKind.Passenger, Vector3.right, out var a);
                w.PlaceTrain(t.PointAt(0.6f, 0.05f), TrainKind.Goods, Vector3.left, out var b);
                if (a != null && b != null)
                {
                    int da = a.Dir, db = b.Dir;
                    bool turned = false;
                    var (gap, _) = RunTrains(w, 60f, _ => { if (a.Dir != da || b.Dir != db) turned = true; });
                    Check(da > 0 && db > 0 && gap > 0.02f && !turned && a.Reversals + b.Reversals == 0,
                        $"Put on a single line facing each other, both run the way the arrows point - no head-on, nobody reverses (closest {gap * 100f:0.0} cm)");
                    Check(a.Odometer + b.Odometer > 0.3f, $"They run on to the buffers ({a.Odometer:0.0} m, {b.Odometer:0.0} m)");
                }
                else Check(false, "Two trains placed on the single line");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
        }

        private static TrackLayout Main(WonderlandWorld w) => w.Track.Network.Edges.Count > 0 ? w.Track.Network.Edges[0].Layout : null;
        private static bool[] MainTunnel(WonderlandWorld w) => w.Track.Network.Edges.Count > 0 ? w.Track.Network.Edges[0].Tunnel : null;
        private static Train FirstTrain(WonderlandWorld w) => w.Trains.Trains.Count > 0 ? w.Trains.Trains[0] : null;
        private static string PlaceTrainAt(WonderlandWorld w, Vector3 p) => w.PlaceTrain(p, TrainKind.Passenger, Vector3.forward, out _);

        private static float MaxGrade(TrackLayout l)
        {
            float worst = 0f;
            for (int i = 10; i < l.Points.Count; i += 10)
            {
                var a = l.Points[i - 10]; var b = l.Points[i];
                float run = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                if (run > 1e-4f) worst = Mathf.Max(worst, Mathf.Abs(b.y - a.y) / run);
            }
            return worst;
        }

        // ------------------------------------------------------------------ renders

        private static int CountNear(List<Vector3> spots, Vector2 p, float within)
        {
            int n = 0;
            foreach (var q in spots) if (new Vector2(q.x - p.x, q.z - p.y).magnitude < within) n++;
            return n;
        }

        [MenuItem("Wonderland/Render assembled world")]
        public static void Renders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            Report.Clear(); failures = 0; BuildGrid.Snap = false;   // (journeys drawn by hand; the road journey checks the grid)
            var world = Run(out _);
            world.Forest.RebuildDirty();
            world.Cover.RebuildDirty();
            for (int i = 0; i < 72 * 60 && world.Trains.Any; i++) { world.Trains.Step(Dt); if (FirstTrain(world).Dwell > 1f) break; }
            Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), new Vector3(0f, 0.05f, -0.05f), 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            File.Delete(Path.Combine(WonderlandRenders.Output, "warmup.png"));
            WonderlandRenders.Capture(cam, "editor-world-overview.png", 1920, 1200);
            cam.transform.position = new Vector3(-1.05f, 0.75f, -1.2f); cam.transform.LookAt(new Vector3(0.05f, 0.04f, 0f)); cam.fieldOfView = 40f;
            WonderlandRenders.Capture(cam, "editor-world-corner.png", 1920, 1200);
            // Close-up behind the locomotive at standing / kneeling viewpoints.
            var loco = world.Trains.Any ? world.Trains.LocoPosition(FirstTrain(world)) : Vector3.zero; var fwd = world.Trains.Any ? world.Trains.LocoRotation(FirstTrain(world)) * Vector3.forward : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, fwd);
            cam.transform.position = loco + fwd * 0.35f + side * 0.3f + Vector3.up * 0.2f;
            cam.transform.LookAt(loco + Vector3.up * 0.03f - fwd * 0.08f); cam.fieldOfView = 38f;
            WonderlandRenders.Capture(cam, "editor-world-train.png", 1920, 1200);
            cam.transform.position = new Vector3(0f, 2.6f, -0.01f); cam.transform.LookAt(Vector3.zero); cam.fieldOfView = 45f;
            WonderlandRenders.Capture(cam, "editor-world-top.png", 1600, 1600);
            // The herd on the meadow (let them graze a while first).
            for (int i = 0; i < 72 * 20; i++) world.Animals.Step(Dt);
            cam.transform.position = new Vector3(0.22f, 0.12f, -1.0f); cam.transform.LookAt(new Vector3(0.12f, 0.01f, -0.66f)); cam.fieldOfView = 34f;
            WonderlandRenders.Capture(cam, "editor-world-animals.png", 1920, 1200);

            // Villagers about the village, and the platform when the train is in.
            for (int i = 0; i < 72 * 30; i++) { world.Trains.Step(Dt); world.People.Step(Dt); }
            for (int i = 0; i < world.People.People.Count; i++)
            {
                if (world.People.StateOf(i) == PeopleSystem.Activity.Riding) continue;
                var v = world.People.PositionOf(i);
                var at = new Vector3(v.x, world.People.HeightOf(i), v.y);
                var rail = Main(world).Evaluate(Main(world).Nearest(at, out float fromRail), out _);
                if (fromRail < 0.12f && i < world.People.People.Count - 1) continue;
                var back = at - rail; back.y = 0f; back = back.normalized;   // look from the side away from the railway
                cam.transform.position = at + back * 0.2f + Vector3.Cross(Vector3.up, back) * 0.06f + Vector3.up * 0.13f; cam.transform.LookAt(at + Vector3.up * 0.015f); cam.fieldOfView = 36f;
                WonderlandRenders.Capture(cam, "editor-world-villagers.png", 1920, 1200);
                break;
            }

            // The stream and its bridge.
            if (world.Streams.Streams.Count > 0)
            {
                Vector3 crossing = world.Streams.Streams[0].points[0]; float bestD = float.MaxValue;
                foreach (var p in world.Streams.Streams[0].points) { Main(world).Nearest(p, out float dd); if (dd < bestD) { bestD = dd; crossing = p; } }
                cam.transform.position = crossing + new Vector3(0.12f, 0.3f, -0.3f); cam.transform.LookAt(crossing + Vector3.up * 0.01f); cam.fieldOfView = 36f;
                WonderlandRenders.Capture(cam, "editor-world-stream-bridge.png", 1920, 1200);
            }
            {
                // A loop over a pond, in a scratch world: the stone bridge.
                var w = new GameObject("Bridge demo").AddComponent<WonderlandWorld>();
                w.transform.position = new Vector3(30f, 0f, 0f);
                w.Create(2f, Materials(), 11);
                var spot = new Vector3(0.5f, 0f, 0f);
                float lvl = w.WaterLevelForStroke(spot);
                Hold(() => w.WaterStroke(spot, 0.16f, 1f, Dt, lvl, false), 2f);
                w.BuildTrack(Loop(w.Terrain, 0.5f, 0.38f, 0.97f, false));
                w.Cover.RebuildDirty(); w.Forest.RebuildDirty();
                var c = w.transform.TransformPoint(spot);
                cam.transform.position = c + new Vector3(0.3f, 0.12f, -0.3f); cam.transform.LookAt(c + Vector3.up * 0.015f); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, "editor-world-pond-bridge.png", 1920, 1200);
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }

            {
                // Tunnel demo: a big hill across a loop.
                var w = new GameObject("Tunnel demo").AddComponent<WonderlandWorld>();
                w.transform.position = new Vector3(70f, 0f, 0f);
                w.Create(2f, Materials(), 13);
                var hill = new Vector3(0.5f, 0f, 0f);
                Hold(() => w.TerrainStroke(TerrainBrush.Raise, hill, 0.3f, 1f, Dt, 0f), 6f);
                for (int i = 0; i < 20; i++) w.PaintForest(hill + new Vector3(Mathf.Sin(i) * 0.15f, 0f, Mathf.Cos(i * 1.3f) * 0.15f), 0.12f, 0.7f, Dt * 4f);
                var plan = w.BuildTrack(Loop(w.Terrain, 0.5f, 0.38f, 0.97f, false));
                w.Cover.RebuildDirty(); w.Forest.RebuildDirty();
                var c = w.transform.TransformPoint(hill);
                cam.transform.position = c + new Vector3(0.05f, 0.25f, -0.55f); cam.transform.LookAt(c + Vector3.up * 0.03f); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, "editor-world-tunnel.png", 1920, 1200);
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            {
                // Bridges across a river: every style.
                var w = Scratch("Bridge demo", 190f);
                var t = w.Terrain;
                for (float z = -0.75f; z <= 0.75f; z += 0.05f)
                {
                    var at = new Vector3(Mathf.Sin(z * 2.2f) * 0.06f, 0f, z);
                    float lvl = w.WaterLevelForStroke(at);
                    Hold(() => w.WaterStroke(at, 0.13f, 1f, Dt, lvl, false), 0.5f);
                }
                w.BuildStream(Stroke(t, new Vector2(0.62f, -0.75f), new Vector2(0.6f, -0.3f), new Vector2(0.66f, 0.2f)));
                w.BuildTrack(Line(t, new Vector2(-0.75f, 0.52f), new Vector2(0.75f, 0.52f)));
                w.BuildTrack(Line(t, new Vector2(-0.75f, 0.62f), new Vector2(0.75f, 0.62f)));
                w.BuildTrack(Line(t, new Vector2(-0.75f, 0.28f), new Vector2(0.75f, 0.28f)));
                w.BuildPath(Stroke(t, new Vector2(-0.6f, 0.02f), new Vector2(0.45f, 0.02f)), PathKind.Road);
                w.BuildPath(Stroke(t, new Vector2(-0.6f, -0.2f), new Vector2(0.45f, -0.2f)), PathKind.Lane);
                w.BuildPath(Stroke(t, new Vector2(-0.6f, -0.4f), new Vector2(0.45f, -0.4f)), PathKind.Footpath);
                w.BuildPath(Stroke(t, new Vector2(-0.6f, -0.58f), new Vector2(0.45f, -0.58f)), PathKind.Lane);
                w.BuildPath(Stroke(t, new Vector2(0.45f, -0.5f), new Vector2(0.8f, -0.5f)), PathKind.Trail);
                void Style(float z, BridgeStyle style)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        int i = w.Bridges.Bridges.FindIndex(b => Mathf.Abs(b.Anchor.y - z) < 0.06f && Mathf.Abs(b.Anchor.x) < 0.3f);
                        if (i < 0 || w.Bridges.Bridges[i].Style == style) return;
                        w.CycleBridge(new Vector3(w.Bridges.Bridges[i].Anchor.x, 0f, w.Bridges.Bridges[i].Anchor.y));
                    }
                }
                Style(0.57f, BridgeStyle.Stone); Style(0.28f, BridgeStyle.Truss); Style(0.02f, BridgeStyle.Suspension); Style(-0.2f, BridgeStyle.Covered);
                for (int i = 0; i < 3; i++) w.PlacePerson(t.PointAt(-0.3f + i * 0.04f, -0.4f), i, 300 + i);
                w.PlaceTrain(t.PointAt(-0.5f, 0.52f), TrainKind.Passenger, Vector3.right, out _);
                w.PlaceTrain(t.PointAt(0.4f, 0.28f), TrainKind.Goods, Vector3.left, out _);
                for (int i = 0; i < 72 * 6; i++) { w.Trains.Step(Dt); w.People.Step(Dt); }
                w.Cover.RebuildDirty(); w.Forest.RebuildDirty();
                var c = w.transform.position;
                cam.transform.position = c + new Vector3(0.9f, 0.85f, -1.0f); cam.transform.LookAt(c + new Vector3(0.0f, 0.02f, 0.0f)); cam.fieldOfView = 42f;
                WonderlandRenders.Capture(cam, "editor-bridges.png", 1920, 1200);
                cam.transform.position = c + new Vector3(0.35f, 0.18f, 0.25f); cam.transform.LookAt(c + new Vector3(0.0f, 0.03f, 0.5f)); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, "editor-bridges-rail.png", 1920, 1200);
                cam.transform.position = c + new Vector3(0.38f, 0.16f, -0.1f); cam.transform.LookAt(c + new Vector3(0.0f, 0.03f, 0.05f)); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, "editor-bridges-road.png", 1920, 1200);
                cam.transform.position = c + new Vector3(0.3f, 0.14f, -0.75f); cam.transform.LookAt(c + new Vector3(0.05f, 0.01f, -0.4f)); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, "editor-bridges-paths.png", 1920, 1200);
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            {
                // A living village: square, shops, roads and paths, a fair, a pasture, a pond with ducks, a wood with deer,
                // and a mountain with a trail.
                var w = Scratch("Village demo", 160f);
                var t = w.Terrain;
                var mountain = new Vector3(-0.55f, 0f, 0.5f);
                Hold(() => w.TerrainStroke(TerrainBrush.Peaks, mountain, 0.32f, 1f, Dt, 0f), 7f);
                Hold(() => w.TerrainStroke(TerrainBrush.Peaks, mountain + new Vector3(0.12f, 0f, -0.05f), 0.2f, 1f, Dt, 0f), 3f);
                var pond = new Vector3(0.55f, 0f, -0.45f);
                float lvl = w.WaterLevelForStroke(pond);
                Hold(() => w.WaterStroke(pond, 0.15f, 0.9f, Dt, lvl, false), 1.8f);
                w.BuildPath(Stroke(t, new Vector2(-0.8f, -0.12f), new Vector2(-0.3f, -0.1f), new Vector2(0.1f, -0.12f), new Vector2(0.75f, 0.05f)), PathKind.Road);
                w.BuildPath(Stroke(t, new Vector2(0.0f, -0.12f), new Vector2(0.02f, 0.2f), new Vector2(0.05f, 0.45f)), PathKind.Footpath);
                w.BuildPath(Stroke(t, new Vector2(0.05f, 0.45f), new Vector2(-0.25f, 0.42f), new Vector2(-0.45f, 0.3f), new Vector2(-0.55f, 0.5f), new Vector2(-0.6f, 0.62f)), PathKind.Trail);
                w.BuildPath(Stroke(t, new Vector2(0.3f, -0.1f), new Vector2(0.42f, -0.2f), new Vector2(0.62f, -0.22f)), PathKind.Lane);
                Put(w, Catalog.Fountain, -0.05f, -0.28f);
                Put(w, Catalog.Church, -0.05f, -0.55f, 0f);
                Put(w, Catalog.Cafe, -0.3f, -0.3f, 90f);
                Put(w, Catalog.Bakery, 0.2f, -0.3f, -90f);
                Put(w, Catalog.Grocer, 0.18f, 0.05f, 180f);
                Put(w, Catalog.TownHall, -0.28f, 0.06f, 180f);
                Put(w, Catalog.Cottage, -0.55f, -0.3f, 70f);
                Put(w, Catalog.Townhouse, -0.5f, 0.05f, 160f);
                Put(w, Catalog.Apartments, 0.35f, 0.18f, 200f);
                Put(w, Catalog.Bench, -0.12f, -0.2f, 200f);
                Put(w, Catalog.Lamp, 0.05f, -0.2f);
                Put(w, Catalog.Lamp, -0.18f, -0.06f);
                Put(w, Catalog.FlowerBed, 0.06f, -0.38f, 0f);
                Put(w, Catalog.Chalet, -0.3f, 0.32f, 160f);
                Put(w, Catalog.Lookout, -0.62f, 0.66f);
                Put(w, Catalog.Carousel, 0.6f, 0.35f);
                Put(w, Catalog.FerrisWheel, 0.78f, 0.12f, -80f);
                Put(w, Catalog.Barn, 0.7f, -0.25f, -60f);
                Put(w, Catalog.Windmill, 0.82f, -0.02f, -100f);
                Put(w, Catalog.WheatField, 0.25f, -0.65f, 10f);
                var fence = new List<Vector3>();
                for (int i = 0; i <= 64; i++) { float a = i / 64f * Mathf.PI * 2f; fence.Add(t.PointAt(-0.4f + Mathf.Cos(a) * 0.14f, -0.56f + Mathf.Sin(a) * 0.09f)); }
                w.BuildPath(fence, PathKind.Fence);
                for (int i = 0; i < 4; i++) w.PlaceAnimal(t.PointAt(-0.46f + i * 0.04f, -0.57f + (i % 2) * 0.03f), i == 3 ? AnimalKind.Horse : AnimalKind.Sheep, 200 + i);
                for (int i = 0; i < 3; i++) w.PlaceAnimal(t.PointAt(0.5f + i * 0.04f, -0.45f + (i % 2) * 0.03f), AnimalKind.Duck, 210 + i);
                for (int i = 0; i < 4; i++) w.PlaceAnimal(t.PointAt(0.58f + i * 0.02f, -0.18f), AnimalKind.Chicken, 220 + i);
                var wood = new Vector3(-0.15f, 0f, 0.65f);
                for (int i = 0; i < 10; i++) w.PaintForest(wood + new Vector3(Mathf.Sin(i) * 0.12f, 0f, Mathf.Cos(i * 1.3f) * 0.08f), 0.1f, 0.8f, Dt * 4f);
                w.PlaceAnimal(wood + new Vector3(0.14f, 0f, -0.1f), AnimalKind.Deer, 230);
                for (int i = 0; i < 4; i++) w.PlacePerson(t.PointAt(0.5f + i * 0.03f, 0.25f), i, 240 + i);
                for (int i = 0; i < 72 * 60; i++) { w.People.Step(Dt); w.Animals.Step(Dt); }
                w.Buildings.Animate(3f, 3f);
                w.Cover.RebuildDirty(); w.Forest.RebuildDirty(); w.Paths.Rebuild();
                var c = w.transform.position;
                cam.transform.position = c + new Vector3(0.15f, 1.15f, -1.45f); cam.transform.LookAt(c + new Vector3(0f, 0.03f, -0.02f)); cam.fieldOfView = 42f;
                WonderlandRenders.Capture(cam, "editor-village.png", 1920, 1200);
                cam.transform.position = c + new Vector3(0.1f, 0.2f, -0.62f); cam.transform.LookAt(c + new Vector3(-0.06f, 0.015f, -0.25f)); cam.fieldOfView = 38f;
                WonderlandRenders.Capture(cam, "editor-village-square.png", 1920, 1200);
                cam.transform.position = c + new Vector3(0.9f, 0.32f, 0.0f); cam.transform.LookAt(c + new Vector3(0.62f, 0.05f, 0.22f)); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, "editor-village-fair.png", 1920, 1200);
                cam.transform.position = c + new Vector3(0.05f, 0.45f, 0.05f); cam.transform.LookAt(c + new Vector3(-0.5f, 0.12f, 0.5f)); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, "editor-village-mountain.png", 1920, 1200);
                cam.transform.position = c + new Vector3(-0.2f, 0.22f, -0.85f); cam.transform.LookAt(c + new Vector3(-0.4f, 0.01f, -0.56f)); cam.fieldOfView = 36f;
                WonderlandRenders.Capture(cam, "editor-village-pasture.png", 1920, 1200);
                // A day in the village: shoppers in the morning, lit windows in the evening, the quiet night.
                Light key = null;
                foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) key = l;
                foreach (var (hour, file) in new[] { (9.6f, "editor-day-morning.png"), (16f, "editor-day-afternoon.png"), (21.1f, "editor-day-evening.png"), (2f, "editor-day-night.png") })
                {
                    for (int guard = 0; guard < 72 * 900 && Mathf.Abs(w.Clock.Hour - hour) > 0.03f; guard++) { w.Clock.Tick(Dt); w.People.Step(Dt); w.Animals.Step(Dt); }
                    WonderlandLighting.SetTimeOfDay(key, w.Clock.Hour, w.Clock.Daylight, 0f);
                    w.Buildings.Animate(3f, w.Buildings.RidesOpen * 3f);
                    cam.transform.position = c + new Vector3(0.25f, 0.42f, -0.95f); cam.transform.LookAt(c + new Vector3(0.05f, 0.02f, -0.12f)); cam.fieldOfView = 44f;
                    WonderlandRenders.Capture(cam, file, 1920, 1200);
                    Debug.Log($"{file}: {w.People.Census()}");
                }
                // The working day: the farmer in the field and produce carried to the shops; then a fête on the square.
                foreach (int shop in new[] { Catalog.Bakery, Catalog.Grocer, Catalog.Cafe }) w.People.SetStock(w.Buildings.Houses.FindIndex(h => h.variant == shop), 1f);
                int carrying = 0;
                for (int guard = 0; guard < 72 * 900 && (w.Clock.Hour < 9.5f || w.Clock.Hour > 12f || carrying == 0); guard++)
                {
                    w.Clock.Tick(Dt); w.People.Step(Dt); w.Animals.Step(Dt);
                    carrying = 0; for (int i = 0; i < w.People.People.Count; i++) if (w.People.CarryingOf(i) > 0) carrying++;
                }
                for (int i = 0; i < 72 * 3; i++) { w.Clock.Tick(Dt); w.People.Step(Dt); }
                WonderlandLighting.SetTimeOfDay(key, w.Clock.Hour, w.Clock.Daylight, 0f);
                w.Buildings.Animate(3f, 3f);
                cam.transform.position = c + new Vector3(0.62f, 0.3f, -1.05f); cam.transform.LookAt(c + new Vector3(0.35f, 0.02f, -0.45f)); cam.fieldOfView = 44f;
                WonderlandRenders.Capture(cam, "editor-day-work.png", 1920, 1200);
                Debug.Log($"editor-day-work.png: {w.People.Census()}, {carrying} carrying produce");
                for (int guard = 0; guard < 72 * 900 && Mathf.Abs(w.Clock.Hour - 14.5f) > 0.05f; guard++) { w.Clock.Tick(Dt); w.People.Step(Dt); w.Animals.Step(Dt); }
                Debug.Log("Fête: " + w.HoldFete());
                for (int i = 0; i < 72 * 45; i++) { w.Clock.Tick(Dt); w.People.Step(Dt); w.Animals.Step(Dt); }
                WonderlandLighting.SetTimeOfDay(key, w.Clock.Hour, w.Clock.Daylight, 0f);
                w.Buildings.Animate(3f, 3f);
                cam.transform.position = c + new Vector3(0.24f, 0.2f, -0.64f); cam.transform.LookAt(c + new Vector3(-0.07f, 0.015f, -0.26f)); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, "editor-day-fete.png", 1920, 1200);
                Debug.Log($"editor-day-fete.png: {w.People.Census()}");
                WonderlandLighting.SetTimeOfDay(key, 13f, 1f, 0f);
                WonderlandLighting.Configure(key);
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            TownRenders(cam);
            WeatherRenders(cam);
            BoatRenders(cam);
            {
                // Railway network demo: a loop, a branch joined back as a second loop, a line across with diamond crossings,
                // a station, three trains and a stop signal.
                var w = Scratch("Network demo", 130f);
                var t = w.Terrain;
                for (int i = 0; i < 12; i++) w.PaintForest(new Vector3(-0.1f + Mathf.Sin(i * 1.7f) * 0.12f, 0f, 0.05f + Mathf.Cos(i * 1.3f) * 0.1f), 0.1f, 0.7f, Dt * 4f);
                w.BuildTrack(Loop(t, 0.5f, 0.38f, 0.97f, false));
                var from = new Vector2(0.52f, 0.02f);
                w.BuildTrack(Line(t, from, from + new Vector2(0.12f, 0.2f), from + new Vector2(0.32f, 0.48f)));
                Vector3 dead = default;
                foreach (var e in w.Track.Network.Edges) if (!e.Closed) for (int k = 0; k < 2; k++) if (e.Joint[k] < 0) dead = k == 0 ? e.Layout.Points[0] : e.Layout.Points[^1];
                var d2 = new Vector2(dead.x, dead.z);
                w.BuildTrack(Line(t, d2, d2 + new Vector2(-0.15f, 0.3f), new Vector2(0.15f, 0.95f), new Vector2(-0.35f, 0.75f), new Vector2(-0.3f, 0.36f)));
                var across = w.BuildTrack(Line(t, new Vector2(-1.0f, -0.36f), new Vector2(1.0f, -0.2f)));
                if (!across.Ok) Debug.Log("Network demo: crossing line refused: " + across.Error);
                var st = w.StationAt(t.PointAt(-0.2f, -0.52f), 2);
                if (st.HasValue) w.PlaceHouse(st.Value);
                for (int i = 0; i < 3; i++)
                {
                    var spot = FindHouseSpot(w, i);
                    w.PlaceHouse(spot);
                }
                w.PlaceSignal(t.PointAt(-0.58f, 0.05f));   // automatic
                PlaceTrainAt(w, t.PointAt(0f, 0.4f));
                w.PlaceTrain(t.PointAt(-0.5f, -0.05f), TrainKind.Goods, Vector3.back, out _);
                w.PlaceTrain(t.PointAt(-0.8f, -0.344f), TrainKind.Railcar, Vector3.right, out _);
                for (int i = 0; i < 72 * 25; i++) w.Trains.Step(Dt);
                w.Cover.RebuildDirty(); w.Forest.RebuildDirty();
                var c = w.transform.position + new Vector3(0.05f, 0f, 0.15f);
                cam.transform.position = c + new Vector3(0.1f, 1.25f, -1.45f); cam.transform.LookAt(c + Vector3.up * 0.02f); cam.fieldOfView = 42f;
                WonderlandRenders.Capture(cam, "editor-network.png", 1920, 1200);
                cam.transform.position = c + new Vector3(0f, 2.3f, -0.01f); cam.transform.LookAt(c); cam.fieldOfView = 45f;
                WonderlandRenders.Capture(cam, "editor-network-top.png", 1600, 1600);
                if (w.Track.Network.Joints.Count > 0)
                {
                    var j = w.transform.TransformPoint(w.Track.Network.Joints[0].Position);
                    var into = w.transform.TransformDirection(w.Track.Network.IntoEdge(w.Track.Network.Joints[0].Trunk));
                    cam.transform.position = j + into * 0.28f + Vector3.Cross(Vector3.up, into) * 0.12f + Vector3.up * 0.16f;
                    cam.transform.LookAt(j - into * 0.08f + Vector3.up * 0.01f); cam.fieldOfView = 40f;
                    WonderlandRenders.Capture(cam, "editor-network-switch.png", 1920, 1200);
                }
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }

            // The pond.
            cam.transform.position = new Vector3(0.95f, 0.32f, -1.2f); cam.transform.LookAt(new Vector3(0.48f, 0.0f, -0.68f)); cam.fieldOfView = 38f;
            WonderlandRenders.Capture(cam, "editor-world-pond.png", 1920, 1200);

            // The railway station with the train at the platform.
            foreach (var h in world.Buildings.Houses)
            {
                if (h.variant != HouseFactory.Station) continue;
                var c = new Vector3(h.x, world.Terrain.SurfaceAt(h.x, h.z), h.z);
                var front = Quaternion.Euler(0f, h.rotation, 0f) * Vector3.back;
                var across = Vector3.Cross(Vector3.up, front);
                cam.transform.position = c + front * 0.42f + across * 0.22f + Vector3.up * 0.2f;
                cam.transform.LookAt(c + front * 0.08f + Vector3.up * 0.025f); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, "editor-world-station.png", 1920, 1200);
                cam.transform.position = c + front * 0.05f + Vector3.up * 0.75f;
                cam.transform.LookAt(c + front * 0.05f, front); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, "editor-world-station-top.png", 1400, 1400);
                cam.transform.position = c - front * 0.3f + across * 0.15f + Vector3.up * 0.16f;
                cam.transform.LookAt(c + front * 0.1f + Vector3.up * 0.02f); cam.fieldOfView = 42f;
                WonderlandRenders.Capture(cam, "editor-world-station-platform.png", 1920, 1200);
                break;
            }
            // The land grown onto the floor past the original shore (right-hand side).
            cam.transform.position = new Vector3(2.05f, 0.55f, -0.75f); cam.transform.LookAt(new Vector3(1.15f, 0.03f, 0.02f)); cam.fieldOfView = 45f;
            WonderlandRenders.Capture(cam, "editor-world-grown-land.png", 1920, 1200);
            // Kneeling view across the meadow, eye height ~45 cm.
            cam.transform.position = new Vector3(0.55f, 0.32f, -1.05f); cam.transform.LookAt(new Vector3(-0.1f, 0.06f, 0.1f)); cam.fieldOfView = 50f;
            WonderlandRenders.Capture(cam, "editor-world-kneeling.png", 1920, 1200);

            // The palette as seen over the left controller, against the world.
            var palette = new GameObject("Palette").AddComponent<PaletteUI>();
            palette.Build(world.Materials.UI, world.Materials.Lit);
            foreach (var g in new[] { "Undo", "Save", "Load", "Move", "New" }) palette.AddGlobal(g, null);
            palette.SetTool(Tool.Forest);
            palette.SetOptions(new[] { new PaletteUI.Option("Paint", true, null), new PaletteUI.Option("Erase", false, null), new PaletteUI.Option("Sparse", false, null), new PaletteUI.Option("Dense", false, null) });
            palette.SetHint("Hold trigger to paint a forest  ·  A: erase  ·  stick: size / density");
            palette.SetStatus("World saved");
            cam.transform.position = new Vector3(0.0f, 0.95f, -1.55f); cam.transform.rotation = Quaternion.Euler(28f, 0f, 0f); cam.fieldOfView = 40f;
            palette.transform.position = cam.transform.position + cam.transform.forward * 0.42f + cam.transform.up * -0.06f + cam.transform.right * -0.07f;
            palette.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
            WonderlandRenders.Capture(cam, "editor-palette.png", 1920, 1200);
            File.WriteAllText(Path.Combine(WonderlandRenders.Output, "README.txt"),
                "Editor renders (Unity batch mode, desktop GPU) - NOT in-headset captures.\n" +
                "The wooden floor is a procedural stand-in for the passthrough living room.\n" +
                "The world in editor-world-*.png was built by the scripted journey (WonderlandJourney.Run): the same world API the controllers drive.\n");
            Debug.Log("Wonderland: world renders written.\n" + Report);
        }
    }
}
