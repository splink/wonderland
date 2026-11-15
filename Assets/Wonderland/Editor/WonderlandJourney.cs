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
            Check(!bad.Ok && bad.Error.Contains("crosses"), $"Self-crossing track rejected (\"{bad.Error}\")");
            var offEdge = new List<Vector3>();
            for (int i = 0; i <= 50; i++) offEdge.Add(new Vector3(-0.3f + i * 0.026f, 0f, 0.6f + i * 0.01f));
            var off = world.BuildTrack(offEdge);
            Check(!off.Ok, $"Track off the landscape rejected (\"{off.Error}\")");
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

            // 4. Sculpting across the railway leaves the track bed untouched.
            var probe = layout.Points[layout.Points.Count / 3];
            float bedBefore = t.SurfaceAt(probe.x, probe.z);
            Hold(() => world.TerrainStroke(TerrainBrush.Raise, probe, 0.2f, 1f, Dt, 0f), 1.5f);
            float bedAfter = t.SurfaceAt(probe.x, probe.z);
            Check(Mathf.Abs(bedAfter - bedBefore) < 0.0015f, $"Railway corridor locked against brushes (bed moved {(bedAfter - bedBefore) * 1000f:0.0} mm)");

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
            bool same = true;
            for (int i = 0; i < t.Height.Length; i++) if (Mathf.Abs(copy.Terrain.Height[i] - t.Height[i]) > 1e-6f) { same = false; break; }
            Check(same, "Save/load keeps the sculpted terrain exactly");
            bool sameLand = true;
            for (int i = 0; i < t.Land.Length; i++) if (copy.Terrain.Land[i] != t.Land[i]) { sameLand = false; break; }
            Check(sameLand, "Save/load keeps the grown and trimmed land");
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
            Check(rt.AllowedAt(2.8f, 0.5f) && !rt.AllowedAt(3.38f, 0.5f) && !rt.AllowedAt(2.5f, 2f), "Free floor allowed; walls and the sofa footprint are not");
            Stroke(new Vector3(0.4f, 0f, 0f), new Vector3(3.6f, 0f, 0f), 6f, p => roomWorld.TerrainStroke(TerrainBrush.Grow, p, 0.25f, 1f, Dt, 0f));
            Check(rt.EdgeAt(2.6f, 0f) > 0.3f, "Land grows across the room");
            Check(rt.EdgeAt(3.38f, 0f) == 0f && rt.Land[rt.Index(rt.Nx - 1, rt.Nz / 2)] < 0.5f, "Growth stops at the wall");
            Stroke(new Vector3(2.5f, 0f, 1.2f), new Vector3(2.5f, 0f, 2.3f), 3f, p => roomWorld.TerrainStroke(TerrainBrush.Grow, p, 0.2f, 1f, Dt, 0f));
            Check(rt.EdgeAt(2.5f, 1.25f) > 0f && rt.EdgeAt(2.5f, 2.1f) == 0f, "Growth stops at the sofa");
            float landBeforeMove = rt.LandArea();
            roomWorld.transform.SetPositionAndRotation(new Vector3(9.4f, 0f, 0.6f), Quaternion.Euler(0f, 30f, 0f));
            roomWorld.FitToRoom();
            var corner = roomWorld.transform.InverseTransformPoint(new Vector3(10f + 3.3f, 0f, 2.5f));
            Check(Mathf.Abs(rt.LandArea() - landBeforeMove) < 1e-4f && rt.Inside(corner.x, corner.z), "After moving, the grid re-fits to the room and the land is unchanged");
            UnityEngine.Object.DestroyImmediate(roomWorld.gameObject);

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

        [MenuItem("Wonderland/Render assembled world")]
        public static void Renders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            Report.Clear(); failures = 0;
            var world = Run(out _);
            world.Forest.RebuildDirty();
            for (int i = 0; i < 72 * 4; i++) world.Train.Step(Dt);
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
