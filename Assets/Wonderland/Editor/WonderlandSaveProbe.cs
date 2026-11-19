using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    /// <summary>
    /// Loads a player's save (WONDERLAND_SAVE) and reports on it: streams with unset water, land at the ceiling, steps
    /// along roads, and why each car park, garage or station has (or hasn't) a driveway; renders the places that look
    /// wrong (Logs/saveprobe-results.txt, docs/renders/save-*.png).
    /// </summary>
    public static class WonderlandSaveProbe
    {
        public static void Run()
        {
            string path = System.Environment.GetEnvironmentVariable("WONDERLAND_SAVE");
            var report = new System.Text.StringBuilder();
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), Vector3.zero, 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = new GameObject("Save").AddComponent<WonderlandWorld>();
            w.Create(2f, WonderlandJourney.Materials(), 7, true, endless: true);
            Application.logMessageReceived += (msg, _, __) => { if (msg.StartsWith("Wonderland: mended") || msg.StartsWith("Wonderland: took down")) report.AppendLine(msg); };
            w.Load(WonderlandJourney.Materials(), path);
            w.Sky.Shown = false;
            var t = w.Terrain;
            report.AppendLine($"grid {t.Nx}x{t.Nz} at cells ({t.Cells.x}, {t.Cells.y}); window centre ({w.WindowCenter.x:0.00}, {w.WindowCenter.y:0.00})");
            int unset = 0; foreach (var s in w.Streams.Streams) foreach (var q in s.points) if (!(q.y <= TerrainField.MaxHeight)) unset++;
            float highest = 0f; Vector2 top = default;
            for (int z = 0; z < t.Nz; z++) for (int x = 0; x < t.Nx; x++) { float h = t.Surface[z * t.Nx + x]; if (h > highest) { highest = h; top = new Vector2(t.LocalX(x), t.LocalZ(z)); } }
            report.AppendLine($"after loading: {unset} stream points unset, highest ground {highest * 1000f:0} mm at ({top.x:0.00}, {top.y:0.00})");
            var shots = new List<(string, Vector2)>();
            // Where the streams (and the walls) were: bring that land into the grid, and look.
            w.SetWindowCenter(new Vector2(3.7f, -1.0f));
            highest = 0f;
            for (int z = 0; z < t.Nz; z++) for (int x = 0; x < t.Nx; x++) { float h = t.Surface[z * t.Nx + x]; if (h > highest) { highest = h; top = new Vector2(t.LocalX(x), t.LocalZ(z)); } }
            report.AppendLine($"by the streams (window at 3.7, -1.0): highest ground {highest * 1000f:0} mm at ({top.x:0.00}, {top.y:0.00})");

            {
                var c = t.RegionAround(top, 0.001f); int ci = (c.y + 1) * t.Nx + c.x + 1;
                var row = new System.Text.StringBuilder("around the highest (mm):\n");
                for (int dz = -3; dz <= 3; dz++) { for (int dx = -3; dx <= 3; dx++) row.Append($"{t.Surface[ci + dz * t.Nx + dx] * 1000f,5:0}"); row.Append("\n"); }
                report.Append(row);
            }
            // Steps along roads: the road's own surface height between neighbouring points of its course.
            foreach (var l in w.Paths.Lines)
            {
                if (!PathSystem.IsCarWay(l.kind) || l.points.Count < 2) continue;
                float worst = 0f; Vector2 at = default;
                for (int i = 1; i < l.points.Count; i++)
                {
                    var a = l.points[i - 1]; var b = l.points[i];
                    float step = Mathf.Abs(t.SurfaceAt(a.x, a.y) - t.SurfaceAt(b.x, b.y)) / Mathf.Max(0.001f, (b - a).magnitude);
                    if (step > worst) { worst = step; at = b; }
                }
                if (worst > 9f) { report.AppendLine($"road {w.Paths.Lines.IndexOf(l)} ({PathSystem.Names[l.kind]}, {l.points.Count} points): steepest ground under it {worst * 100f:0}% at ({at.x:0.00}, {at.y:0.00})"); if (shots.Count < 4) shots.Add(($"road{w.Paths.Lines.IndexOf(l)}", at)); }
            }
            // The roads' own surfaces: where the ground comes up through them, and where they step.
            {
                var samplesOf = typeof(PathSystem).GetMethod("RoadSamples", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                int pokes = 0, steps = 0, shown = 0;
                foreach (var l in w.Paths.Lines)
                {
                    if (!PathSystem.IsRoad(l.kind) || l.points.Count < 2) continue;
                    var list = (System.Collections.IList)samplesOf.Invoke(w.Paths, new object[] { l });
                    float hw = PathSystem.HalfWidth(l.kind) - PathSystem.KerbWidth, lastY = float.NaN; Vector2 lastP = default;
                    foreach (var o in list)
                    {
                        var ty = o.GetType();
                        var P = (Vector2)ty.GetField("P").GetValue(o); var T = (Vector2)ty.GetField("T").GetValue(o); float Y = (float)ty.GetField("Y").GetValue(o);
                        if ((bool)ty.GetField("Junction").GetValue(o)) { lastY = float.NaN; continue; }
                        var right = new Vector2(T.y, -T.x);
                        float over = 0f;
                        for (int k = -4; k <= 4; k++) { var q = P + right * (hw * k / 4f); over = Mathf.Max(over, t.SurfaceAt(q.x, q.y) - (Y + PathSystem.RoadLift)); }
                        if (over > 0.0003f) { pokes++; if (shown < 2 && over > 0.001f) { report.AppendLine($"road {w.Paths.Lines.IndexOf(l)}: ground {over * 1000f:0.0} mm up through it at ({P.x:0.00}, {P.y:0.00})"); shots.Add(($"poke{shown}", P)); shown++; } }
                        if (!float.IsNaN(lastY) && (P - lastP).magnitude > 1e-4f && Mathf.Abs(Y - lastY) / (P - lastP).magnitude > 0.25f) { steps++; if (steps <= 12 && steps % 4 == 1) shots.Add(($"step{steps}", P)); if (steps <= 12) report.AppendLine($"road {w.Paths.Lines.IndexOf(l)}: steps {(Y - lastY) * 1000f:0.0} mm in {(P - lastP).magnitude * 1000f:0} mm at ({P.x:0.00}, {P.y:0.00}); deck here {w.Bridges.Deck(P, out _, out _)}, before {w.Bridges.Deck(lastP, out _, out _)}; ground {t.SurfaceAt(P.x, P.y) * 1000f:0.0} / {t.SurfaceAt(lastP.x, lastP.y) * 1000f:0.0} mm"); }
                        lastY = Y; lastP = P;
                    }
                }
                report.AppendLine($"roads: {pokes} places the ground comes up through the asphalt, {steps} steep steps");
            }
            // Roads running over each other with no junction where they do.
            {
                var lines = w.Paths.Lines;
                int found = 0;
                for (int a = 0; a < lines.Count; a++)
                for (int b = a + 1; b < lines.Count; b++)
                {
                    if (!PathSystem.IsCarWay(lines[a].kind) || !PathSystem.IsCarWay(lines[b].kind)) continue;
                    float reach = (PathSystem.HalfWidth(lines[a].kind) + PathSystem.HalfWidth(lines[b].kind)) * 0.8f;
                    int run = 0; Vector2 at = default;
                    foreach (var p in lines[a].points)
                    {
                        if (PathSystem.Distance(lines[b], p) < reach && !w.Paths.NearJunction(p, 0.01f)) { run++; at = p; }
                    }
                    if (run >= 3) { found++; report.AppendLine($"roads {a} and {b} run over each other for {run} points with no junction, e.g. at ({at.x:0.00}, {at.y:0.00})"); if (found <= 2) shots.Add(($"overlap{found}", at)); }
                }
                report.AppendLine($"{found} pairs of roads overlap without a junction");
            }
            // Driveways.
            float edge = PathSystem.HalfWidth((int)PathKind.Road) - PathSystem.KerbWidth;
            foreach (var h in w.Buildings.Houses)
            {
                if (h.variant is not (Catalog.CarPark or Catalog.ParkingGarage or Catalog.Police or Catalog.FireStation or Catalog.Hospital)) continue;
                var way = WonderlandWorld.WayIn(h);
                var bay = w.BayOf(new Vector2(h.x, h.z));
                string why = bay != null ? $"driveway {bay.Depth * 1000f:0} mm" : "no driveway";
                if (bay == null)
                {
                    if (!w.Paths.NearestStreet(way, 0.25f, out var line, out var p, out var along)) why += ": no street within 25 cm of its way in";
                    else
                    {
                        var o = new Vector2(along.y, -along.x); if (Vector2.Dot(way - p, o) < 0f) o = -o;
                        float depth = Vector2.Dot(way - p, o) - edge;
                        float across = Mathf.Abs(Vector2.Dot((new Vector2(h.x, h.z) - p).normalized, o));
                        why += $": street {(way - p).magnitude * 100f:0.0} cm from its way in, depth {depth * 1000f:0} mm, near a junction {w.Paths.NearJunction(p, WonderlandWorld.DrivewayWidth)}, facing the street {Vector2.Dot(Quaternion.Euler(0f, 0f, -h.rotation) * Vector2.down, o) * -1f:0.00}";
                    }

                }
                report.AppendLine($"{Catalog.Get(h.variant).Name} at ({h.x:0.00}, {h.z:0.00}) turned {h.rotation:0}: {why}");
            }
            foreach (var (name, at) in shots)
            {
                w.SetWindowCenter(at);
                {
                    int pond = 0, cells = 0; float nearStream = float.MaxValue;
                    for (float dz = -0.3f; dz <= 0.3f; dz += 0.02f) for (float dx = -0.3f; dx <= 0.3f; dx += 0.02f) { cells++; if (t.WaterAt(at.x + dx, at.y + dz) > 0.5f) pond++; }
                    foreach (var st in w.Streams.Streams) nearStream = Mathf.Min(nearStream, StreamSystem.Nearest(st, at, out _, out _));
                    report.AppendLine($"shot {name} at ({at.x:0.00}, {at.y:0.00}): {pond}/{cells} pond cells within 30 cm, nearest stream {nearStream * 100f:0} cm; shores smoothed now: {t.SmoothShores(t.All)}");
                    t.UpdateSurface(t.All); w.TerrainView.RebuildAll(); w.WaterView.RebuildAll();
                }
                w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
                var p = w.transform.TransformPoint(new Vector3(at.x, t.SurfaceAt(at.x, at.y), at.y));
                cam.transform.position = p + new Vector3(0.1f, 0.3f, -0.35f); cam.transform.LookAt(p); cam.fieldOfView = 45f;
                WonderlandRenders.Capture(cam, $"save-{name}.png", 1280, 900);
            }
            System.IO.File.WriteAllText("Logs/saveprobe-results.txt", report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
