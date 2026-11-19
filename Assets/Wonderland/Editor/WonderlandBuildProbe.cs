using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wonderland.Editor
{
    /// <summary>
    /// Building roads and railway as a player does, through the tool controller with a synthetic hand: what each stroke
    /// costs (the slowest frame while drawing, the frame that builds it, the live preview) and what it leaves behind
    /// (meshes, materials, game objects, memory) - so leaks and slow frames show up before the headset runs out of memory.
    /// Output: Logs/buildprobe-results.txt.
    /// </summary>
    public static class WonderlandBuildProbe
    {
        private const float Dt = 1f / 72f;
        private static WonderlandWorld world;
        private static ToolController tools;
        private static PaletteUI palette;
        private static bool triggerHeld;
        private static readonly StringBuilder Report = new();

        public static void Run()
        {
            Report.Clear();
            bool gridWas = BuildGrid.Snap;
            BuildGrid.Snap = true;
            try { Probe(); RealWorlds(); Expanse(); }
            catch (Exception e) { Report.AppendLine("EXCEPTION " + e); }
            finally { BuildGrid.Snap = gridWas; }
            File.WriteAllText("Logs/buildprobe-results.txt", Report.ToString());
            Debug.Log("Wonderland build probe\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Probe()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            world = new GameObject("Wonderland").AddComponent<WonderlandWorld>();
            world.transform.position = new Vector3(0f, 0f, 1.3f);
            world.Create(1.8f, WonderlandJourney.Materials());
            palette = new GameObject("Palette").AddComponent<PaletteUI>();
            palette.transform.SetPositionAndRotation(new Vector3(-0.15f, 1.0f, 0.3f), Quaternion.Euler(38f, 0f, 0f));
            palette.Build(world.Materials.UI, world.Materials.Lit);
            tools = new GameObject("Tools").AddComponent<ToolController>();
            tools.HapticsEnabled = false;
            tools.Init(world, palette, null);
            // Land to build on: 2.4 m square.
            for (float x = -1.2f; x <= 1.2f; x += 0.2f)
            for (float z = -1.2f; z <= 1.2f; z += 0.2f)
                for (int k = 0; k < 20; k++) world.TerrainStroke(TerrainBrush.Grow, new Vector3(x, 0f, z), 0.3f, 1f, 0.1f, 0f);
            Report.AppendLine($"Land {world.Terrain.LandArea():0.0} m2");
            var hand = world.transform.TransformPoint(new Vector3(0f, 1.4f, -0.5f));

            // Roads: a grid of streets drawn by hand, then streets carried on from their ends.
            Select(hand, "Roads");
            var before = Snapshot();
            var rng = new Rng(4);
            var strokes = new List<List<Vector2>>();
            foreach (float z in new[] { -0.7f, -0.25f, 0.2f, 0.65f }) strokes.Add(Hand(new Vector2(-0.95f, z), new Vector2(0.1f, z + 0.02f), rng));
            foreach (float x in new[] { -0.6f, -0.1f }) strokes.Add(Hand(new Vector2(x, -0.95f), new Vector2(x - 0.02f, 0.9f), rng));
            foreach (float z in new[] { -0.7f, -0.25f, 0.2f, 0.65f }) strokes.Add(Hand(new Vector2(0.1f, z), new Vector2(0.9f, z - 0.02f), rng));
            Draw(hand, "road", strokes);
            Leaks("roads", before, Snapshot());
            foreach (var l in world.Paths.Lines) Report.AppendLine("  road shape: " + Bends(l.points, l.closed));

            // Railway: a loop, then branches and lines carried on from their ends.
            Select(hand, "Track");
            before = Snapshot();
            strokes.Clear();
            var loop = new List<Vector2>();
            for (int i = 0; i < 160; i++) { float a = i / 160f * Mathf.PI * 2f * 0.96f; loop.Add(new Vector2(0.3f + Mathf.Cos(a) * 0.55f, -1.6f + Mathf.Sin(a) * 0.35f)); }
            strokes.Add(loop);
            Draw(hand, "track", strokes);
            strokes.Clear();
            strokes.Add(Hand(new Vector2(0.3f, -1.25f), new Vector2(0.6f, -0.95f), rng));
            Draw(hand, "branch", strokes);
            Leaks("track", before, Snapshot());

            // Holding the trigger on a planned line for 30 s (the live preview showing all the while).
            before = Snapshot();
            var held = Hand(new Vector2(-0.8f, -1.3f), new Vector2(-0.2f, -1.0f), rng);
            var clock = new Stopwatch(); double worst = 0;
            for (int i = 0; i < held.Count; i++) Frame(hand, W(held[i]), true);
            for (int i = 0; i < 72 * 30; i++) { clock.Restart(); Frame(hand, W(held[^1]), true); worst = Math.Max(worst, clock.Elapsed.TotalMilliseconds); }
            Frame(hand, W(held[^1]), false);
            Report.AppendLine($"Holding a planned track for 30 s: slowest frame {worst:0.0} ms");
            Leaks("holding a track preview", before, Snapshot());

            // The live preview's cost: planning a stroke as it grows (what drawing does several times a second).
            var growing = Hand(new Vector2(-0.9f, 0.9f), new Vector2(0.9f, 0.95f), rng);
            Preview("road", growing, p =>
            {
                var c = Stopwatch.StartNew();
                var l = world.Paths.Plan(p, (int)PathKind.Road, out string e);
                if (c.Elapsed.TotalMilliseconds > 20) Report.AppendLine($"  slow road plan: {c.Elapsed.TotalMilliseconds:0} ms, {p.Count} stroke points, {(l != null ? l.points.Count + " road points" : e)}; stroke ({p[0].x:0.000},{p[0].z:0.000}) .. ({p[^1].x:0.000},{p[^1].z:0.000})");
            });
            var trackStroke = Hand(new Vector2(-0.6f, -1.4f), new Vector2(0.0f, -2.1f), rng);
            Preview("track", trackStroke, p => world.PreviewTrack(p, out _, out _));
            UnityEngine.Object.DestroyImmediate(world.gameObject);
        }

        /// <summary>
        /// Players' saved worlds (Tests/Saves, pulled from the headset; not in git): what letting go of a branch off their
        /// railway costs, removing a piece, and undoing - on networks the size players really build.
        /// </summary>
        private static void RealWorlds()
        {
            if (!Directory.Exists("Tests/Saves")) { Report.AppendLine("No saved worlds in Tests/Saves"); return; }
            var seen = new HashSet<long>();
            foreach (var file in Directory.GetFiles("Tests/Saves", "*.json"))
            {
                if (!seen.Add(new FileInfo(file).Length)) continue;   // (copies of the same world)
                var w = new GameObject("Saved world").AddComponent<WonderlandWorld>();
                w.transform.position = new Vector3(500f, 0f, 0f);
                try
                {
                    w.Deserialize(File.ReadAllText(file), WonderlandJourney.Materials());
                    var net = w.Track.Network;
                    int points = 0; foreach (var e in net.Edges) points += e.Layout.Points.Count;
                    Report.AppendLine($"{Path.GetFileNameWithoutExtension(file)}: {net.Edges.Count} pieces, {points / 100f:0} m of track, {net.Joints.Count} switches, {w.Paths.Lines.Count} roads, {w.Buildings.Houses.Count} houses, {w.Forest.Trees.Count} trees");
                    Branches(w);
                }
                catch (Exception e) { Report.AppendLine($"  {Path.GetFileName(file)}: {e}"); }
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
        }

        /// <summary>Branches drawn off the middle of the longest pieces (either side), each built, removed and undone.</summary>
        private static void Branches(WonderlandWorld w)
        {
            var net = w.Track.Network;
            var order = new List<int>(); for (int e = 0; e < net.Edges.Count; e++) order.Add(e);
            order.Sort((a, b) => net.Edges[b].Layout.Length.CompareTo(net.Edges[a].Layout.Length));
            int built = 0, tries = 0;
            foreach (int e in order)
            {
                if (built >= 3 || tries >= 24) break;
                var l = net.Edges[e].Layout;
                if (l.Length < 0.6f) continue;
                foreach (float side in new[] { 1f, -1f })
                {
                    if (built >= 3) break;
                    tries++;
                    var p = l.Evaluate(l.Length * 0.5f, out var t);
                    var a = new Vector2(p.x, p.z); var d = new Vector2(t.x, t.z).normalized; var n = new Vector2(-d.y, d.x) * side;
                    var stroke = new List<Vector3>();
                    for (int i = 0; i <= 40; i++) { float u = i / 40f; var q = a + d * (u * 0.5f) + n * (u * u * 0.3f); stroke.Add(new Vector3(q.x, w.Terrain.SurfaceAt(q.x, q.y), q.y)); }
                    var c = Stopwatch.StartNew();
                    var plan = w.BuildTrack(stroke);
                    double ms = c.Elapsed.TotalMilliseconds;
                    if (!plan.Ok) continue;
                    built++;
                    Report.AppendLine($"  branch built in {ms:0} ms");
                    var mid = plan.Path[plan.Path.Count / 2];
                    c.Restart();
                    bool removed = w.RemovePieceAt(new Vector3(mid.x, 0f, mid.y));
                    Report.AppendLine($"  piece {(removed ? "removed" : "NOT removed")} in {c.Elapsed.TotalMilliseconds:0} ms");
                    c.Restart(); w.Undo(); Report.AppendLine($"  undo {c.Elapsed.TotalMilliseconds:0} ms");
                    c.Restart(); w.Undo(); Report.AppendLine($"  undo {c.Elapsed.TotalMilliseconds:0} ms");
                    net = w.Track.Network;
                    break;
                }
            }
            if (built == 0) Report.AppendLine($"  no branch could be built ({tries} tries)");
        }

        /// <summary>
        /// A big endless world: a railway and a street 24 m long across many windows, houses and trees along them, trains
        /// and cars running. Navigating along it: what each slide costs, what is drawn against what exists, the memory,
        /// and saving it.
        /// </summary>
        private static void Expanse()
        {
            var w = new GameObject("Expanse").AddComponent<WonderlandWorld>();
            w.transform.position = new Vector3(0f, 0f, 400f);
            w.Create(2f, WonderlandJourney.Materials(), 7, false, endless: true);
            w.UseWindow(true);
            var t = w.Terrain;
            bool snap = BuildGrid.Snap; BuildGrid.Snap = true;
            var build = Stopwatch.StartNew();
            int built = 0, houses = 0;
            // Built window by window, as a player would: the window slides along, the line and street carry on.
            for (int seg = 0; seg < 16; seg++)
            {
                float x0 = -1.2f + seg * 1.5f, x1 = x0 + 1.5f;
                List<Vector3> L(float z, float a, float b) { var pts = new List<Vector3>(); for (int i = 0; i <= 20; i++) { float x = Mathf.Lerp(a, b, i / 20f); pts.Add(new Vector3(x, t.SurfaceAt(x, z), z)); } return pts; }
                if (w.BuildTrack(L(0.3f, x0, x1)).Ok) built++;
                w.BuildPath(L(-0.4f, x0, x1), PathKind.Road);
                for (int k = 0; k < 6; k++) if (w.PlaceHouse(new HouseInstance { x = x0 + 0.15f + k * 0.22f, z = -0.85f - (k % 2) * 0.3f, rotation = 0f, variant = k % 3 }) == null) houses++;
                for (int k = 0; k < 3; k++) w.PaintForest(new Vector3(x0 + 0.7f, 0f, 1.0f), 0.3f, 1f, 0.3f);
                w.Navigate(new Vector2Int(1, 0)); w.FinishSlide();
            }
            for (int i = 0; i < 4; i++) w.PlaceTrain(t.PointAt(2f + i * 5f, 0.3f), TrainKind.Passenger, Vector3.right, out _);
            for (int i = 0; i < 6; i++) w.PlaceVehicle(t.PointAt(1f + i * 3.5f, -0.4f), VehicleKind.Car, 90f, i);
            Report.AppendLine($"Expanse: {built}/16 pieces of railway, {w.Track.Network.Edges.Count} pieces now, {w.Paths.Lines.Count} roads, {houses} houses, {w.Forest.Trees.Count} trees, {w.People.Population} people, built in {build.Elapsed.TotalSeconds:0.0} s");
            // Sliding back home along it, and out again.
            var slides = new List<double>(); var frames = new List<double>();
            long managed0 = GC.GetTotalMemory(true);
            for (int i = 0; i < 32; i++)
            {
                var c = Stopwatch.StartNew();
                w.Navigate(new Vector2Int(i < 16 ? -1 : 1, 0));
                double start = c.Elapsed.TotalMilliseconds;
                // The slide's frames (a real 0.45 s at 72 Hz): each frame's cost; the worst is the hitch a player would see.
                double worstFrame = start;
                for (int f = 0; f < 33 && w.Sliding; f++)
                {
                    var fc = Stopwatch.StartNew();
                    w.Advance(1f / 72f);
                    w.Forest.RebuildDirty(2, 0.12f); w.Cover.RebuildDirty(2, 0.15f);
                    worstFrame = Math.Max(worstFrame, fc.Elapsed.TotalMilliseconds);
                }
                slides.Add(worstFrame);
                frames.Add(c.Elapsed.TotalMilliseconds);
            }
            slides.Sort();
            frames.Sort();
            Report.AppendLine($"  sliding 32 times along it: the slowest frame of a slide {slides[16]:0} ms (median), {slides[^1]:0} ms (worst); a whole slide's work {frames[16]:0} ms (median); the grid stays {t.Nx * t.Cell:0.0} x {t.Nz * t.Cell:0.0} m");
            long managed1 = GC.GetTotalMemory(true);
            int renderers = 0, drawn = 0;
            foreach (var r in w.GetComponentsInChildren<Renderer>(true)) { renderers++; if (r.enabled && r.gameObject.activeInHierarchy) drawn++; }
            Report.AppendLine($"  {drawn} of {renderers} renderers drawn at the window; {t.Store.Count} chunks of land kept; managed memory {managed0 / 1048576} -> {managed1 / 1048576} MB over 32 slides");
            // The simulation everywhere: a second of frames.
            var sim = Stopwatch.StartNew();
            for (int f = 0; f < 72; f++) { w.Trains.Step(1f / 72f); w.Traffic.Step(1f / 72f); w.People.Step(1f / 72f); w.SendMessage("LateUpdate"); }
            Report.AppendLine($"  a second of the whole world's life (trains, traffic, people): {sim.Elapsed.TotalMilliseconds / 72f:0.0} ms a frame");
            string dir = Path.Combine(Path.GetTempPath(), "wonderland-expanse");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var save = Stopwatch.StartNew();
            w.Save(Path.Combine(dir, "world.json"));
            double first = save.Elapsed.TotalMilliseconds;
            save.Restart(); w.Save(Path.Combine(dir, "world.json"));
            Report.AppendLine($"  saving: {first:0} ms the first time, {save.Elapsed.TotalMilliseconds:0} ms again; {new FileInfo(Path.Combine(dir, "world.json")).Length / 1024} KB + {Directory.GetFiles(WonderlandWorld.ChunkFolder(Path.Combine(dir, "world.json"))).Length} chunk files");
            var load = Stopwatch.StartNew();
            var again = new GameObject("Expanse again").AddComponent<WonderlandWorld>();
            again.transform.position = new Vector3(0f, 0f, 450f);
            again.Load(WonderlandJourney.Materials(), Path.Combine(dir, "world.json"));
            again.UseWindow(true);
            Report.AppendLine($"  loading it: {load.Elapsed.TotalMilliseconds:0} ms ({again.Buildings.Houses.Count} houses, {again.Track.Network.Edges.Count} track pieces)");
            UnityEngine.Object.DestroyImmediate(again.gameObject);
            Directory.Delete(dir, true);
            BuildGrid.Snap = snap;
            Shader.SetGlobalFloat("_WL_WindowOn", 0f);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>A line's bends: each stretch where it turns, and by how much (degrees); and its straight stretches' headings.</summary>
        public static string Bends(List<Vector2> p, bool closed)
        {
            var sb = new StringBuilder();
            int bends = 0; float turning = 0f; bool inBend = false;
            var headings = new List<string>();
            for (int i = 1; i + 1 < p.Count; i++)
            {
                var a = p[i] - p[i - 1]; var b = p[i + 1] - p[i];
                if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) continue;
                float ang = Vector2.SignedAngle(a, b);
                if (Mathf.Abs(ang) > 0.3f) { turning += ang; inBend = true; }
                else if (inBend) { sb.Append($" {turning:0}"); bends++; turning = 0f; inBend = false; }
            }
            if (inBend) { sb.Append($" {turning:0}"); bends++; }
            float len = 0f; for (int i = 1; i < p.Count; i++) len += (p[i] - p[i - 1]).magnitude;
            return $"{len:0.00} m, {bends} bends{(bends > 0 ? ":" + sb : "")}";
        }

        private static Vector3 W(Vector2 p) => world.transform.TransformPoint(new Vector3(p.x, world.Terrain.SurfaceAt(p.x, p.y), p.y));

        /// <summary>A stroke as a hand draws it: about 1.2 cm a frame, a slow wobble and a little jitter.</summary>
        private static List<Vector2> Hand(Vector2 a, Vector2 b, Rng rng)
        {
            var r = new List<Vector2>();
            int n = Mathf.CeilToInt((b - a).magnitude / 0.012f);
            var side = new Vector2(-(b - a).y, (b - a).x).normalized;
            float phase = rng.Range(0f, 6f);
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                r.Add(Vector2.Lerp(a, b, t) + side * (0.008f * Mathf.Sin(t * 9f + phase)) + rng.InsideUnitCircle() * 0.002f);
            }
            return r;
        }

        private static void Draw(Vector3 hand, string what, List<List<Vector2>> strokes)
        {
            var clock = new Stopwatch();
            double drawWorst = 0, buildWorst = 0, buildSum = 0;
            int built = 0;
            foreach (var s in strokes)
            {
                int pieces = world.Paths.Lines.Count + world.Track.Network.Edges.Count;
                int worstAt = -1, k = 0;
                foreach (var p in s) { clock.Restart(); Frame(hand, W(p), true); double f = clock.Elapsed.TotalMilliseconds; if (f > drawWorst) { drawWorst = f; worstAt = k; } k++; }
                if (drawWorst > 30) Report.AppendLine($"  slowest drawing frame was point {worstAt} of {s.Count}");
                clock.Restart();
                Frame(hand, W(s[^1]), false);
                double ms = clock.Elapsed.TotalMilliseconds;
                buildWorst = Math.Max(buildWorst, ms); buildSum += ms;
                if (world.Paths.Lines.Count + world.Track.Network.Edges.Count != pieces) built++;
                Report.AppendLine($"  {what}: \"{tools.LastMessage}\" ({ms:0} ms)");
            }
            Report.AppendLine($"{what}: {built}/{strokes.Count} built; slowest frame while drawing {drawWorst:0.0} ms; letting go {buildSum / strokes.Count:0} ms on average, {buildWorst:0} ms at worst");
        }

        private static void Preview(string what, List<Vector2> stroke, Action<List<Vector3>> plan)
        {
            var clock = new Stopwatch(); double worst = 0, sum = 0; int n = 0;
            var pts = new List<Vector3>();
            for (int i = 0; i < stroke.Count; i++)
            {
                pts.Add(new Vector3(stroke[i].x, world.Terrain.SurfaceAt(stroke[i].x, stroke[i].y), stroke[i].y));
                if (i < 4 || i % 6 != 0) continue;
                clock.Restart(); plan(pts); double ms = clock.Elapsed.TotalMilliseconds;
                worst = Math.Max(worst, ms); sum += ms; n++;
            }
            Report.AppendLine($"Live preview of a {what} {TrackPlanner.Length(pts):0.0} m long: {sum / Math.Max(1, n):0.0} ms a plan on average, {worst:0.0} ms at worst ({n} plans)");
        }

        private static Dictionary<string, long> Snapshot()
        {
            GC.Collect();
            return new Dictionary<string, long>
            {
                ["meshes"] = Resources.FindObjectsOfTypeAll<Mesh>().Length,
                ["materials"] = Resources.FindObjectsOfTypeAll<Material>().Length,
                ["textures"] = Resources.FindObjectsOfTypeAll<Texture>().Length,
                ["game objects"] = Resources.FindObjectsOfTypeAll<GameObject>().Length,
                ["native MB"] = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024),
                ["managed MB"] = GC.GetTotalMemory(true) / (1024 * 1024),
            };
        }

        private static void Leaks(string what, Dictionary<string, long> a, Dictionary<string, long> b)
        {
            var sb = new StringBuilder();
            foreach (var kv in a) sb.Append($"{kv.Key} {kv.Value} -> {b[kv.Key]} ({b[kv.Key] - kv.Value:+0;-0;0}); ");
            Report.AppendLine($"After {what}: {sb}");
        }

        /// <summary>Picks a tool on the palette, then puts the palette out of the way of the drawing hand.</summary>
        private static void Select(Vector3 hand, string tile)
        {
            palette.transform.position = new Vector3(-0.15f, 1.0f, 0.3f);
            var at = new Vector3(0.2f, 1.1f, 0.25f);
            bool found = false;
            foreach (var b in palette.GetComponentsInChildren<PaletteButton>(true))
                if (b.Text == tile)
                {
                    Physics.SyncTransforms();
                    var p = b.transform.position;
                    Frame(at, p, false); Frame(at, p, true); Frame(at, p, false);
                    found = true;
                    break;
                }
            if (!found) Report.AppendLine("No button " + tile);
            Report.AppendLine($"Tool: {tools.Tool}");
            palette.transform.position = new Vector3(0f, -20f, 0f);
            Physics.SyncTransforms();
        }

        private static void Frame(Vector3 hand, Vector3 target, bool trigger)
        {
            Physics.SyncTransforms();
            var input = new PointerInput
            {
                Valid = true, Ray = new Ray(hand, (target - hand).normalized),
                Trigger = trigger, TriggerDown = trigger && !triggerHeld, TriggerUp = !trigger && triggerHeld,
            };
            triggerHeld = trigger;
            tools.Process(input, Dt);
            world.Forest.RebuildDirty();
        }
    }
}
