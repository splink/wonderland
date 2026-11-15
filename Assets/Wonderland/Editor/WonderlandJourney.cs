using System;
using System.Collections.Generic;
using System.IO;
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
    public static class WonderlandJourney
    {
        private const float Dt = 1f / 72f;
        private static readonly StringBuilder Report = new();
        private static int failures;

        public static void Test()
        {
            Report.Clear(); failures = 0;
            try { Run(out _); }
            catch (Exception e) { Fail("Exception: " + e); }
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            Directory.CreateDirectory("docs");
            File.WriteAllText("docs/test-results.txt", Report.ToString());
            Debug.Log("Wonderland journey test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static void Check(bool ok, string what)
        {
            Report.AppendLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) failures++;
        }

        private static void Fail(string what) { Report.AppendLine("FAIL  " + what); failures++; }

        public static WorldMaterials Materials() => new()
        {
            Lit = WonderlandMaterials.Lit, Terrain = WonderlandMaterials.Terrain, UI = WonderlandMaterials.UI, Smoke = WonderlandMaterials.Smoke,
            Water = WonderlandMaterials.Water,
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
            var figureEight = new List<Vector3>();
            for (int i = 0; i <= 160; i++) { float a = i / 160f * Mathf.PI * 2f; figureEight.Add(new Vector3(Mathf.Sin(a) * 0.5f, 0f, Mathf.Sin(a) * Mathf.Cos(a) * 0.5f)); }
            var bad = world.BuildTrack(figureEight);
            Check(!bad.Ok && bad.Error.Contains("figure 8"), $"Figure-8 track rejected (\"{bad.Error}\")");
            Check(!world.Track.HasTrack, "Rejected strokes leave the world unchanged");

            drawnLoop = HandDrawnLoop(t, new Vector2(0.05f, -0.02f), new Vector2(0.62f, 0.44f));
            int treesBeforeTrack = world.Forest.Trees.Count;
            var plan = world.BuildTrack(drawnLoop);
            Check(plan.Ok, "Hand-drawn loop accepted" + (plan.Ok ? "" : $" (\"{plan.Error}\")"));
            if (!plan.Ok) return world;
            var layout = world.Track.Layout;
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
            {
                // Tunnel: a big hill across the line keeps its top; portals at both mouths; no steam underground.
                var w = new GameObject("Tunnel").AddComponent<WonderlandWorld>();
                w.transform.position = new Vector3(60f, 0f, 0f);
                w.Create(2f, Materials(), 7, false);
                var hill = new Vector3(0.5f, 0f, 0f);
                Hold(() => w.TerrainStroke(TerrainBrush.Raise, hill, 0.3f, 1f, Dt, 0f), 6f);
                float hillTop = w.Terrain.SurfaceAt(hill.x, hill.z);
                var tunnelPlan = w.BuildTrack(Loop(w.Terrain, 0.5f, 0.38f, 0.97f, false));
                var mask = w.Track.Tunnel;
                int tunnelPoints = 0; if (mask != null) foreach (bool m in mask) if (m) tunnelPoints++;
                bool hillKept = false;
                if (tunnelPlan.Ok && mask != null)
                {
                    float sMid = tunnelPlan.Layout.Nearest(hill, out _);
                    var bed = tunnelPlan.Layout.Evaluate(sMid, out _);
                    hillKept = w.Terrain.SurfaceAt(bed.x, bed.z) > bed.y + TrackSystem.TunnelHeight;
                }
                Check(tunnelPlan.Ok && tunnelPoints > 8 && hillKept, $"A railway under a big hill becomes a tunnel (hill {hillTop * 100f:0} cm, {tunnelPoints} cm of tunnel, hill kept above it)");
                if (tunnelPlan.Ok && mask != null)
                {
                    w.PlaceTrain(tunnelPlan.Layout.Points[0]); w.SetTrainRunning(true);
                    bool steamless = false;
                    for (int i = 0; i < 72 * 40 && !steamless; i++) { w.Train.Step(Dt); if (w.Train.InTunnel(w.Train.S)) steamless = true; }
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
                    float sCross = crossing.Layout.Nearest(spot, out _);
                    var bed = crossing.Layout.Evaluate(sCross, out _);
                    bridged = bed.y >= lvl + TrackPlanner.BridgeClearance * 0.9f;
                    waterKept = w.Terrain.WaterAt(bed.x, bed.z) > 0.5f && w.Terrain.SurfaceAt(bed.x, bed.z) < lvl;
                }
                Check(crossing.Ok && bridged && waterKept, crossing.Ok ? "Railway across a pond rises onto a bridge and the water stays" : $"Railway across a pond (\"{crossing.Error}\")");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }

            // 4. Sculpting under the railway lifts the track with the land (within its grade limit), and the bed stays graded.
            int probeIndex = layout.Points.Count / 3;
            var probe = layout.Points[probeIndex];
            float trackBefore = probe.y;
            Hold(() => world.TerrainStroke(TerrainBrush.Raise, probe, 0.35f, 1f, Dt, 0f), 2f);
            world.UpdateTrackFromTerrain();
            float trackAfter = world.Track.Layout.Points[probeIndex].y;
            Check(trackAfter > trackBefore + 0.01f, $"Raising the ground under the railway lifts the track ({trackBefore * 100f:0.0} -> {trackAfter * 100f:0.0} cm)");
            bool bedFollows = true; int tunnelled = 0;
            for (int i = 0; i < world.Track.Layout.Points.Count; i++)
            {
                var p = world.Track.Layout.Points[i];
                bool nearTunnel = false;
                if (world.Track.Tunnel != null)
                    for (int k = -6; k <= 6 && !nearTunnel; k++) nearTunnel = world.Track.Tunnel[((i + k) % world.Track.Tunnel.Length + world.Track.Tunnel.Length) % world.Track.Tunnel.Length];
                if (nearTunnel) { tunnelled++; continue; }   // the hill is kept over a tunnel and rises at its mouths
                if (world.WaterLevelFor(new Vector2(p.x, p.z)) >= 0f) continue;                        // a bridge spans water
                float dev = t.SurfaceAt(p.x, p.z) - (p.y - 0.0015f);
                if (Mathf.Abs(dev) > 0.004f) { bedFollows = false; Report.AppendLine($"INFO  bed off by {dev * 1000f:0.0} mm at point {i} ({p.x:0.00},{p.z:0.00}) water {world.WaterLevelFor(new Vector2(p.x, p.z)):0.000} pad {t.Pad[t.Index(Mathf.RoundToInt((p.x - t.X0) / t.Cell), Mathf.RoundToInt((p.z - t.Z0) / t.Cell))]:0.00} lock {t.LockAt(p.x, p.z):0.00} edge {t.EdgeAt(p.x, p.z):0.00} land {t.LandAt(p.x, p.z)} bedY {p.y * 100f:0.00} cm"); break; }
            }
            Check(bedFollows, $"The bed is regraded under the lifted track (tunnel {tunnelled} cm)");
            Check(MaxGrade(world.Track.Layout) <= TrackPlanner.MaxGrade * 1.2f, $"Lifted track keeps its grade limit (max {MaxGrade(world.Track.Layout) * 100f:0.0} %)");
            layout = world.Track.Layout;

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
                Check(error == null, $"Place {HouseFactory.Names[v]}" + (error == null ? "" : $" (\"{error}\")"));
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
            string trainError = world.PlaceTrain(layout.Points[layout.Points.Count / 2] + new Vector3(0.01f, 0f, 0f));
            Check(trainError == null, "Train placed on the railway" + (trainError == null ? "" : $" (\"{trainError}\")"));
            world.SetTrainRunning(true);
            float worstOffset = 0f, worstAlign = 1f;
            for (int i = 0; i < 72 * 12; i++)
            {
                world.Train.Step(Dt);
                var pos = world.Train.LocoPosition;
                layout.Nearest(pos, out float d);
                worstOffset = Mathf.Max(worstOffset, d);
                float s = layout.Nearest(pos, out _);
                layout.Evaluate(s, out var tangent);
                var fwd = world.Train.LocoRotation * Vector3.forward;
                worstAlign = Mathf.Min(worstAlign, Mathf.Abs(Vector3.Dot(new Vector3(fwd.x, 0f, fwd.z).normalized, new Vector3(tangent.x, 0f, tangent.z).normalized)));
            }
            Check(world.Train.Odometer > 1.0f, $"Train runs continuously ({world.Train.Odometer:0.00} m in 12 s)");
            Check(worstOffset < 0.006f, $"Locomotive stays on the rails (worst offset {worstOffset * 1000f:0.0} mm)");
            Check(worstAlign > 0.97f, $"Locomotive follows the curves (worst alignment {worstAlign:0.000})");
            world.SetTrainRunning(false);
            for (int i = 0; i < 72 * 3; i++) world.Train.Step(Dt);
            Check(world.Train.Speed < 0.001f, "Train stops smoothly");
            world.SetTrainRunning(true);

            // 6b. Railway station: snaps beside the rails; the train halts centred on the platform, waits, pulls away.
            var outside = layout.Points[layout.Points.Count * 2 / 3];
            var outward = new Vector3(outside.x - 0.05f, 0f, outside.z + 0.02f).normalized;
            var station = world.StationAt(outside + outward * 0.1f, 1);
            Check(station.HasValue, "Station snaps to the railway");
            string stationError = station.HasValue ? world.PlaceHouse(station.Value) : "no snap";
            Check(stationError == null && world.Train.Stops.Count == 1, "Station placed beside the rails with a platform" + (stationError == null ? "" : $" (\"{stationError}\")"));
            bool halted = false; float haltOffset = 0f;
            for (int i = 0; i < 72 * 40 && !halted; i++)
            {
                world.Train.Step(Dt);
                if (world.Train.Dwell > 0f)
                {
                    halted = true;
                    float sLoco = world.Track.Layout.Nearest(world.Train.LocoPosition, out _);
                    float trainCentre = sLoco + (TrainFactory.Loco.Front + (-TrainSystem.TrainLength + TrainFactory.Loco.Front)) * 0.5f;
                    haltOffset = Mathf.Abs(Mathf.DeltaAngle(trainCentre / world.Track.Layout.Length * 360f, world.Train.Stops[0] / world.Track.Layout.Length * 360f)) / 360f * world.Track.Layout.Length;
                }
            }
            Check(halted && world.Train.Speed == 0f, "Train halts at the station");
            Check(haltOffset < 0.03f, $"Train stops centred on the platform (off by {haltOffset * 100f:0.0} cm)");
            float odoAtStation = world.Train.Odometer;
            for (int i = 0; i < 72 * 8; i++) world.Train.Step(Dt);
            Check(world.Train.Odometer > odoAtStation + 0.2f, "Train pulls away after waiting");

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
                float sTrack = world.Track.Layout.Nearest(mid, out _);
                bool crossesTrack = false;
                foreach (var p in sp) { world.Track.Layout.Nearest(p, out float dd); if (dd < 0.01f) { crossesTrack = true; break; } }
                if (crossesTrack)
                {
                    float worstClear = float.MaxValue;
                    foreach (var p in world.Track.Layout.Points)
                    {
                        float lv = world.WaterLevelFor(new Vector2(p.x, p.z));
                        if (lv >= 0f) worstClear = Mathf.Min(worstClear, p.y - lv);
                    }
                    Check(worstClear >= TrackPlanner.BridgeClearance * 0.9f, $"The railway bridges the stream (clearance {worstClear * 1000f:0} mm)");
                }
                else Check(false, "Stream crosses the railway (test layout)");
                _ = sTrack;
            }
            layout = world.Track.Layout;

            // 6e. Villagers: on dry land only; they stroll to the neighbours' doors, wait at the station, ride the train
            //     and wave at it - and never stand on the rails, in water or inside a house.
            {
                var homesFor = new List<Vector3>();
                var stationHouse = world.Buildings.Houses.Find(h => h.variant == HouseFactory.Station);
                var stationAt = new Vector2(stationHouse.x, stationHouse.z);
                var away = (stationAt - new Vector2(world.Track.Layout.Evaluate(world.Track.Layout.Nearest(new Vector3(stationAt.x, 0f, stationAt.y), out _), out _).x,
                    world.Track.Layout.Evaluate(world.Track.Layout.Nearest(new Vector3(stationAt.x, 0f, stationAt.y), out _), out _).z)).normalized;
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
                Check(world.Train.Running, "The train runs for the villagers");
                bool walked = false, chatted = false, waited = false, rode = false, waved = false, safe = true;
                var start = new Vector2[world.People.People.Count];
                for (int i = 0; i < start.Length; i++) start[i] = world.People.PositionOf(i);
                for (int step = 0; step < 72 * 240; step++)
                {
                    world.Train.Step(Dt);
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
                for (int i = 0; i < villagers && victimIndex < 0; i++) if (world.People.StateOf(i) != PeopleSystem.Activity.Riding) victimIndex = i;
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
            Check(copy.Track.HasTrack && Mathf.Abs(copy.Track.Layout.Length - layout.Length) < 0.001f, "Save/load keeps the railway");
            Check(copy.Train.Placed && copy.Train.Running, "Save/load keeps the running train");
            Check(copy.Train.Stops.Count == world.Train.Stops.Count && copy.Train.Stops.Count > 0, "Save/load keeps the station and its stop");
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
            bool ok = plan.Ok && plan.Layout.Closed && (!expectGrowth || w.Terrain.LandArea() > land + 0.2f);
            Check(ok, what + (plan.Ok ? $" ({plan.Layout.Length:0.00} m{(expectGrowth ? $", land {land:0.0} -> {w.Terrain.LandArea():0.0} m2" : "")})" : $" (\"{plan.Error}\")"));
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
            Report.Clear(); failures = 0;
            var world = Run(out _);
            world.Forest.RebuildDirty();
            world.Cover.RebuildDirty();
            for (int i = 0; i < 72 * 60; i++) { world.Train.Step(Dt); if (world.Train.Dwell > 1f) break; }
            Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), new Vector3(0f, 0.05f, -0.05f), 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            File.Delete(Path.Combine(WonderlandRenders.Output, "warmup.png"));
            WonderlandRenders.Capture(cam, "editor-world-overview.png", 1920, 1200);
            cam.transform.position = new Vector3(-1.05f, 0.75f, -1.2f); cam.transform.LookAt(new Vector3(0.05f, 0.04f, 0f)); cam.fieldOfView = 40f;
            WonderlandRenders.Capture(cam, "editor-world-corner.png", 1920, 1200);
            // Close-up behind the locomotive at standing / kneeling viewpoints.
            var loco = world.Train.LocoPosition; var fwd = world.Train.LocoRotation * Vector3.forward;
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
            for (int i = 0; i < 72 * 30; i++) { world.Train.Step(Dt); world.People.Step(Dt); }
            for (int i = 0; i < world.People.People.Count; i++)
            {
                if (world.People.StateOf(i) == PeopleSystem.Activity.Riding) continue;
                var v = world.People.PositionOf(i);
                var at = new Vector3(v.x, world.People.HeightOf(i), v.y);
                var rail = world.Track.Layout.Evaluate(world.Track.Layout.Nearest(at, out float fromRail), out _);
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
                foreach (var p in world.Streams.Streams[0].points) { world.Track.Layout.Nearest(p, out float dd); if (dd < bestD) { bestD = dd; crossing = p; } }
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
