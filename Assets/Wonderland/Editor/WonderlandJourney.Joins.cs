using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// Drawn on from what is there: a road carried on from a road's end (grid off as well as on) is one road turning
        /// in a proper curve; where roads meet, each junction is one piece its roads stop at, with room for rounded
        /// corners, and junctions that would overlap are one. A creek carried on as a stream and then as a river into a
        /// lake is one stream, widening gradually, flowing the way it was drawn and into the lake, never uphill; a stream
        /// drawn in several strokes is one stream without a kink; levelling it again and again leaves it where it is; a
        /// creek running into a stream's side leaves the stream's bed open.
        /// </summary>
        private static void JoinJourney()
        {
            bool gridWas = BuildGrid.Snap;
            foreach (bool grid in new[] { false, true })
            {
                BuildGrid.Snap = grid;
                string on = grid ? "on the grid" : "grid off";
                var w = Scratch("Joins " + on, 580f);
                var t = w.Terrain;
                w.BuildPath(Line(t, new Vector2(-0.7f, 0.4f), new Vector2(-0.3f, 0.4f)), PathKind.Road);
                int before = w.Paths.Lines.Count;
                string e = w.BuildPath(Line(t, new Vector2(-0.28f, 0.37f), new Vector2(-0.28f, -0.1f)), PathKind.Road);
                var road = w.Paths.Lines[^1];
                float sharp = Sharpest(road.points);
                Check(e == null && w.Paths.Lines.Count == before && sharp < 15f, $"{on}: a road drawn on from a road's end at a right angle carries it on round a curve - one road, no kink ({w.Paths.Lines.Count - before} new roads, sharpest {sharp:0.0} deg per cm{e})");
                // Junctions: a crossroads, a T, and a T right beside it (one junction).
                w.BuildPath(Line(t, new Vector2(-0.7f, -0.4f), new Vector2(0.7f, -0.4f)), PathKind.Road);
                w.BuildPath(Line(t, new Vector2(0.0f, -0.1f), new Vector2(0.0f, -0.7f)), PathKind.Road);
                w.BuildPath(Line(t, new Vector2(0.4f, -0.1f), new Vector2(0.4f, -0.4f)), PathKind.Road);
                w.BuildPath(Line(t, new Vector2(0.45f, -0.7f), new Vector2(0.45f, -0.4f)), PathKind.Road);
                var js = w.Paths.Junctions;
                bool apart = true, roomy = true;
                for (int a = 0; a < js.Count; a++)
                {
                    for (int b = a + 1; b < js.Count; b++) if ((js[a].At - js[b].At).magnitude < js[a].Radius + js[b].Radius - 0.004f) apart = false;
                    if (js[a].Radius < PathSystem.HalfWidth((int)PathKind.Road) - PathSystem.KerbWidth + PathSystem.CornerRadius) roomy = false;
                }
                Check(js.Count == 2 && apart && roomy, $"{on}: junctions are one piece with room for rounded kerbs, and two T-junctions side by side are one ({js.Count} junctions: {string.Join(", ", js.ConvertAll(j => $"{j.Arms.Count} arms, {j.Radius * 100f:0.0} cm"))})");
                Object.DestroyImmediate(w.gameObject);
            }
            // A highway's junction sweeps round wider than a street's; buses call at bus stops.
            {
                BuildGrid.Snap = false;
                var w = Scratch("Highway and bus", 600f);
                var t = w.Terrain;
                w.BuildPath(Line(t, new Vector2(-0.7f, 0.5f), new Vector2(0.7f, 0.5f)), PathKind.Highway);
                w.BuildPath(Line(t, new Vector2(0f, 0.75f), new Vector2(0f, 0.25f)), PathKind.Road);
                w.BuildPath(Line(t, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f)), PathKind.Road);
                w.BuildPath(Line(t, new Vector2(0.3f, 0.2f), new Vector2(0.3f, -0.2f)), PathKind.Road);
                float highway = 0f, street = 0f;
                foreach (var j in w.Paths.Junctions) { if (j.ArmKind.Contains((int)PathKind.Highway)) highway = Mathf.Max(highway, j.Radius); else street = Mathf.Max(street, j.Radius); }
                Check(highway > street * 1.3f && street > 0f, $"A highway's junction sweeps round wider than a street's ({highway * 100f:0.0} cm against {street * 100f:0.0} cm), with no pavement at its corners");
                // A ring of streets with a bus stop on each side, and a bus going round.
                var w2 = Scratch("Bus stops", 640f);
                var t2 = w2.Terrain;
                w2.BuildPath(Line(t2, new Vector2(-0.5f, -0.4f), new Vector2(0.5f, -0.4f), new Vector2(0.5f, 0.4f), new Vector2(-0.5f, 0.4f), new Vector2(-0.5f, -0.4f)), PathKind.Road);
                int stops = 0;
                foreach (var at in new[] { new Vector2(0f, -0.35f), new Vector2(0.1f, -0.45f), new Vector2(0f, 0.35f), new Vector2(0.1f, 0.45f) })
                    if (w2.Paths.AlongRoad(at, Catalog.Get(Catalog.BusStop).Half.y, out var p, out float yaw) && w2.PlaceHouse(new HouseInstance { x = p.x, z = p.y, rotation = yaw, variant = Catalog.BusStop }) == null) stops++;
                w2.Traffic.Refresh(true);
                string e = w2.PlaceVehicle(new Vector3(-0.3f, 0f, -0.4f), VehicleKind.Bus, 90f, 1);
                float stood = 0f;
                for (float sec = 0f; sec < 150f; sec += Dt)
                {
                    w2.Clock.Tick(Dt); w2.Traffic.Step(Dt);
                    foreach (var v in w2.Traffic.Vehicles) if (v.Kind == VehicleKind.Bus && v.Dwell > 0f) stood += Dt;
                }
                Check(stops == 4 && e == null && w2.Traffic.BusCalls >= 2 && stood > 4f, $"Buses call at the bus stops by the road and stand while people get on ({w2.Traffic.BusCalls} calls at {stops} stops, {stood:0} s standing{(e != null ? ", " + e : "")})");
                Object.DestroyImmediate(w.gameObject); Object.DestroyImmediate(w2.gameObject);
            }
            BuildGrid.Snap = gridWas;
            {
                var w = Scratch("Water joins", 620f);
                var t = w.Terrain;
                var lake = new Vector3(0.62f, 0f, 0.45f);
                float level = w.WaterLevelForStroke(lake);
                Hold(() => w.WaterStroke(lake, 0.22f, 0.9f, Dt, level, false), 2f);
                w.BuildStream(Line(t, new Vector2(-0.7f, 0.35f), new Vector2(-0.45f, 0.4f)), 0.6f);
                w.BuildStream(Line(t, new Vector2(-0.45f, 0.4f), new Vector2(-0.05f, 0.5f)), 1f);
                w.BuildStream(Line(t, new Vector2(-0.05f, 0.5f), new Vector2(0.45f, 0.45f)), 2.2f);
                var river = w.Streams.Streams.Count == 1 ? w.Streams.Streams[0] : null;
                bool gradual = river != null, downhill = river != null;
                float widest = 0f, step = 0f;
                if (river != null)
                    for (int i = 1; i < river.points.Count; i++)
                    {
                        step = Mathf.Max(step, Mathf.Abs(river.WidthAt(i) - river.WidthAt(i - 1)));
                        if (river.points[i].y > river.points[i - 1].y + 1e-5f) downhill = false;
                        widest = Mathf.Max(widest, river.WidthAt(i));
                    }
                gradual &= step < 0.15f;
                var mouth = river != null ? river.points[^1] : Vector3.zero;
                bool intoLake = river != null && t.WaterAt(mouth.x, mouth.z) > 0.5f && river.points[0].x < -0.6f;
                Check(river != null && gradual && downhill && intoLake && river.WidthAt(0) < 0.7f && widest > 2f,
                    $"A creek carried on as a stream, then as a river into the lake, is one stream: widening gradually ({river?.WidthAt(0):0.0} to {widest:0.0}, at most {step:0.00} a step), flowing the way it was drawn and into the lake, never uphill ({w.Streams.Streams.Count} streams)");
                w.BuildStream(Line(t, new Vector2(-0.7f, -0.4f), new Vector2(-0.45f, -0.3f)), 1f);
                w.BuildStream(Line(t, new Vector2(-0.45f, -0.3f), new Vector2(-0.05f, -0.4f)), 1f);
                w.BuildStream(Line(t, new Vector2(-0.05f, -0.4f), new Vector2(0.4f, -0.25f)), 1f);
                var three = w.Streams.Streams[^1];
                var course = three.points.ConvertAll(q => new Vector2(q.x, q.z));
                float bend = Sharpest(course);
                Check(w.Streams.Streams.Count == 2 && bend < 8f, $"A stream drawn in three strokes is one stream, curving on at each join without a kink ({w.Streams.Streams.Count} streams, sharpest {bend:0.0} deg per 1.5 cm)");
                // A creek running into the stream's side: the stream's bed stays open where it comes in.
                w.BuildStream(Line(t, new Vector2(-0.25f, -0.8f), new Vector2(-0.25f, -0.37f)), 0.6f);
                var main = w.Streams.Streams.Find(s => s.points.Count == three.points.Count) ?? three;
                StreamSystem.Nearest(main, new Vector2(-0.25f, -0.35f), out float mainLevel, out float along);
                int at = Mathf.Clamp(Mathf.RoundToInt(along / StreamSystem.Spacing), 0, main.points.Count - 1);
                float bed = t.SurfaceAt(main.points[at].x, main.points[at].z);
                Check(bed < mainLevel - 0.001f, $"A creek running into a stream's side leaves the stream's bed open (bed {(bed - mainLevel) * 1000f:0.0} mm under the water)");
                var creek = w.Streams.Streams.Find(x => x != main && x.points.Count < main.points.Count && x.JoinsAtEnd);
                float meet = 999f, flare = 0f;
                if (creek != null)
                {
                    var last = creek.points[^1] - creek.points[^2]; var tail = new Vector2(last.x, last.z);
                    StreamSystem.Nearest(main, new Vector2(creek.points[^1].x, creek.points[^1].z), out _, out float at2);
                    int k2 = Mathf.Clamp(Mathf.RoundToInt(at2 / StreamSystem.Spacing), 1, main.points.Count - 2);
                    var f = main.points[k2 + 1] - main.points[k2 - 1];
                    meet = Vector2.Angle(tail, new Vector2(f.x, f.z));
                    flare = creek.WidthAt(creek.points.Count - 1) / creek.WidthAt(creek.points.Count / 2);
                }
                Check(creek != null && meet < 50f && flare > 1.3f, $"...and joins it as a real confluence: curving in to meet its flow at {meet:0} degrees (drawn at 90), its mouth widening x{flare:0.0}");
                var levels = new List<float>();
                foreach (var s in w.Streams.Streams) foreach (var q in s.points) levels.Add(q.y);
                for (int k = 0; k < 4; k++) w.UpdateStreamsFromTerrain();
                float sank = 0f; int n = 0; string where = "";
                foreach (var s in w.Streams.Streams)
                    for (int i = 0; i < s.points.Count; i++)
                    {
                        if (n >= levels.Count) break;
                        float d = levels[n++] - s.points[i].y;
                        if (d > sank) { sank = d; where = $"stream {w.Streams.Streams.IndexOf(s)} (width {s.WidthAt(i):0.0}) point {i}/{s.points.Count} at ({s.points[i].x:0.00}, {s.points[i].z:0.00})"; }
                    }
                Check(sank < 0.0015f, $"Levelling the streams again and again leaves them where they are (sank at most {sank * 1000f:0.0} mm{(sank > 0f ? ", " + where : "")})");
                Object.DestroyImmediate(w.gameObject);
            }
            {
                var w = Scratch("Hand water", 660f);
                foreach (var (what, ok) in HandWater(w)) Check(ok, what);
                Object.DestroyImmediate(w.gameObject);
            }
        }

        /// <summary>The sharpest turn between neighbouring stretches of a line (degrees).</summary>
        private static float Sharpest(List<Vector2> pts)
        {
            float worst = 0f;
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                var a = pts[i] - pts[i - 1]; var b = pts[i + 1] - pts[i];
                if (a.magnitude > 0.004f && b.magnitude > 0.004f) worst = Mathf.Max(worst, Vector2.Angle(a, b));
            }
            return worst;
        }

        /// <summary>
        /// Streams carried on as a hand draws them, on a world with a lake at its east: a stream carried on by a stroke
        /// begun beside its end and slanting away; a river carried on as a creek by a stroke begun back over its end; a
        /// creek carried on into the lake. Each comes out one stream, the old part exactly where it was, curving on
        /// without a kink, its width changing gently, opening out into the lake. Returns the checks.
        /// </summary>
        private static List<(string, bool)> HandWater(WonderlandWorld w)
        {
            var t = w.Terrain;
            var results = new List<(string, bool)>();
            var lake = new Vector3(0.62f, 0f, -0.45f);
            float level = w.WaterLevelForStroke(lake);
            Hold(() => w.WaterStroke(lake, 0.22f, 0.9f, Dt, level, false), 2f);
            StreamPath Holding(Vector2 p) => w.Streams.Streams.Find(s => StreamSystem.Nearest(s, p, out _, out _) < 0.01f);
            void Case(string what, float w0, Vector2[] first, float w1, Vector2[] then, bool lakeEnd)
            {
                w.BuildStream(Line(t, first), w0);
                var before = Holding(first[0]);
                var kept = before != null ? before.points.ConvertAll(q => new Vector2(q.x, q.z)) : new List<Vector2>();
                int streams = w.Streams.Streams.Count;
                w.BuildStream(Line(t, then), w1);
                var after = Holding(first[0]);
                bool one = after != null && w.Streams.Streams.Count == streams && StreamSystem.Nearest(after, then[^1], out _, out _) < 0.02f;
                // The old part where it was: every old point is still on the stream, exactly.
                float moved = 0f;
                if (after != null) foreach (var q in kept) moved = Mathf.Max(moved, StreamSystem.Nearest(after, q, out _, out _));
                float bend = after != null ? Sharpest(after.points.ConvertAll(q => new Vector2(q.x, q.z))) : 999f, step = 0f;
                if (after != null) for (int i = 1; i < after.points.Count; i++) step = Mathf.Max(step, Mathf.Abs(after.WidthAt(i) - after.WidthAt(i - 1)));
                string extra = "";
                bool fine = one && moved < 0.0005f && bend < 10f && step < 0.15f;
                if (lakeEnd && after != null)
                {
                    int n = after.points.Count, back = Mathf.Max(0, n - 12);
                    float flare = after.WidthAt(n - 1) / after.WidthAt(back);
                    var mouth = after.points[n - 1];
                    bool inLake = t.WaterAt(mouth.x, mouth.z) > 0.5f;
                    fine &= inLake && flare > 1.3f;
                    extra = $", into the lake {inLake}, its mouth x{flare:0.0}";
                }
                else if (after != null) extra = $", width {after.WidthAt(0):0.0} to {after.WidthAt(after.points.Count - 1):0.0}";
                results.Add(($"Carried on as a hand draws it - {what}: one stream {one}, the old part where it was (moved {moved * 1000f:0.0} mm), curving on (sharpest {bend:0.0} deg per 1.5 cm), its width gradual (at most {step:0.00} a step){extra}", fine));
            }
            Case("a stroke begun 2.5 cm beside a stream's end, slanting off at 45 degrees", 1f, new[] { new Vector2(-0.75f, 0.55f), new Vector2(-0.35f, 0.55f) },
                 1f, new[] { new Vector2(-0.33f, 0.575f), new Vector2(-0.08f, 0.8f) }, false);
            Case("a river carried on as a creek by a stroke begun back over its end", 2.2f, new[] { new Vector2(-0.75f, 0.1f), new Vector2(-0.35f, 0.1f) },
                 0.6f, new[] { new Vector2(-0.38f, 0.11f), new Vector2(-0.05f, 0.02f), new Vector2(0.15f, -0.1f) }, false);
            Case("a creek carried on into the lake", 0.6f, new[] { new Vector2(-0.75f, -0.5f), new Vector2(-0.1f, -0.45f) },
                 1f, new[] { new Vector2(-0.08f, -0.46f), new Vector2(0.25f, -0.44f), new Vector2(0.55f, -0.45f) }, true);
            return results;
        }

        /// <summary>
        /// Renders of things drawn on from other things: roads carried on from a road's end, round a corner, onto a road's
        /// side, between two roads' ends; a creek carried on as a stream and then a river into a lake; a stream drawn in
        /// several strokes (docs/renders/editor-joins-*.png).
        /// </summary>
        public static void JoinRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), Vector3.zero, 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            bool gridWas = BuildGrid.Snap;
            var report = new System.Text.StringBuilder();
            foreach (bool grid in new[] { true, false })
            {
                BuildGrid.Snap = grid;
                var w = Scratch("Road joins", 0f);
                w.Sky.Shown = false;
                var t = w.Terrain;
                void Road(PathKind kind, params Vector2[] via) { string e = w.BuildPath(Line(t, via), kind); report.AppendLine($"{(grid ? "grid" : "free")} {kind} {string.Join(" ", via)}: {e ?? "ok"}, {w.Paths.Lines.Count} lines, {w.Paths.Junctions.Count} junctions"); }
                // Carried on from an end, then round a corner from the new end.
                Road(PathKind.Road, new Vector2(-0.8f, 0.6f), new Vector2(-0.45f, 0.6f));
                Road(PathKind.Road, new Vector2(-0.45f, 0.6f), new Vector2(-0.2f, 0.6f));
                Road(PathKind.Road, new Vector2(-0.2f, 0.6f), new Vector2(-0.05f, 0.6f), new Vector2(-0.05f, 0.3f));
                // A side road onto a road's side (a T), and one drawn from a road's end at right angles.
                Road(PathKind.Road, new Vector2(0.3f, 0.75f), new Vector2(0.3f, -0.2f));
                Road(PathKind.Road, new Vector2(0.75f, 0.3f), new Vector2(0.3f, 0.3f));
                Road(PathKind.Road, new Vector2(0.3f, -0.2f), new Vector2(0.75f, -0.2f));
                // Two roads, then a stroke joining their ends.
                Road(PathKind.Road, new Vector2(-0.8f, -0.1f), new Vector2(-0.5f, -0.1f));
                Road(PathKind.Road, new Vector2(-0.2f, -0.6f), new Vector2(-0.2f, -0.35f));
                Road(PathKind.Road, new Vector2(-0.5f, -0.1f), new Vector2(-0.2f, -0.1f), new Vector2(-0.2f, -0.35f));
                // A lane onto a road's end, a road crossing another near its end.
                Road(PathKind.Lane, new Vector2(-0.8f, -0.75f), new Vector2(-0.5f, -0.75f));
                Road(PathKind.Road, new Vector2(-0.5f, -0.75f), new Vector2(-0.3f, -0.75f));
                Road(PathKind.Road, new Vector2(0.2f, -0.45f), new Vector2(0.8f, -0.45f));
                Road(PathKind.Road, new Vector2(0.75f, -0.3f), new Vector2(0.75f, -0.85f));
                Object.DestroyImmediate(w.gameObject);
            }
            // As a hand draws them: overshooting a road, stopping short of one, coming in at a slant, joining two roads' sides.
            foreach (bool grid in new[] { true, false })
            {
                BuildGrid.Snap = grid;
                var w = Scratch("Hand joins", 0f);
                w.Sky.Shown = false;
                var t = w.Terrain;
                var rng = new Rng(3);
                void Hand(PathKind kind, params Vector2[] via)
                {
                    var line = Line(t, via);
                    for (int k = 0; k < line.Count; k++) line[k] += new Vector3(rng.Range(-0.006f, 0.006f), 0f, rng.Range(-0.006f, 0.006f));
                    string e = w.BuildPath(line, kind);
                    report.AppendLine($"hand {(grid ? "grid" : "free")} {kind} {string.Join(" ", via)}: {e ?? "ok"}, {w.Paths.Lines.Count} lines, {w.Paths.Junctions.Count} junctions");
                }
                // Two parallel streets, joined by a cross street overshooting both; one stopping short; one at a slant.
                Hand(PathKind.Road, new Vector2(-0.8f, 0.55f), new Vector2(0.8f, 0.55f));
                Hand(PathKind.Road, new Vector2(-0.8f, 0.05f), new Vector2(0.8f, 0.05f));
                Hand(PathKind.Road, new Vector2(-0.5f, 0.59f), new Vector2(-0.5f, 0.02f));
                Hand(PathKind.Road, new Vector2(-0.1f, 0.52f), new Vector2(-0.1f, 0.085f));
                Hand(PathKind.Road, new Vector2(0.25f, 0.55f), new Vector2(0.5f, 0.05f));
                // A street ending near another street's end, at right angles; one ending near a junction.
                Hand(PathKind.Road, new Vector2(-0.8f, -0.4f), new Vector2(-0.3f, -0.4f));
                Hand(PathKind.Road, new Vector2(-0.28f, -0.43f), new Vector2(-0.28f, -0.85f));
                Hand(PathKind.Road, new Vector2(0.55f, 0.08f), new Vector2(0.55f, -0.6f));
                Hand(PathKind.Road, new Vector2(0.1f, -0.3f), new Vector2(0.53f, -0.3f));
                w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
                var c = w.transform.position;
                string tag = grid ? "grid" : "free";
                cam.transform.position = c + new Vector3(0f, 1.9f, -0.15f); cam.transform.LookAt(c); cam.fieldOfView = 50f;
                if (grid) WonderlandRenders.Capture(cam, $"editor-joins-hand-{tag}.png", 1200, 1200);
                foreach (var (name, at) in grid ? new[] { ("cross", new Vector2(-0.3f, 0.3f)), ("slant", new Vector2(0.4f, 0.3f)) } : new[] { ("corner", new Vector2(-0.3f, -0.45f)) })
                {
                    var p = c + new Vector3(at.x, 0f, at.y);
                    cam.transform.position = p + new Vector3(0.05f, 0.32f, -0.3f); cam.transform.LookAt(p); cam.fieldOfView = 40f;
                    WonderlandRenders.Capture(cam, $"editor-joins-hand-{tag}-{name}.png", 1280, 900);
                }
                Object.DestroyImmediate(w.gameObject);
            }
            BuildGrid.Snap = gridWas;
            {
                var w = Scratch("Water joins", 0f);
                w.Sky.Shown = false;
                var t = w.Terrain;
                var lake = new Vector3(0.62f, 0f, 0.45f);
                float level = w.WaterLevelForStroke(lake);
                Hold(() => w.WaterStroke(lake, 0.22f, 0.9f, Dt, level, false), 2f);
                void Stream(float width, params Vector2[] via) { string e = w.BuildStream(Line(t, via), width); report.AppendLine($"stream {width} {string.Join(" ", via)}: {e ?? "ok"}, {w.Streams.Streams.Count} streams"); }
                // A creek, carried on as a stream, then a river into the lake.
                Stream(0.6f, new Vector2(-0.75f, 0.35f), new Vector2(-0.45f, 0.4f));
                Stream(1f, new Vector2(-0.45f, 0.4f), new Vector2(-0.05f, 0.5f));
                Stream(2.2f, new Vector2(-0.05f, 0.5f), new Vector2(0.45f, 0.45f));
                // A stream drawn in three strokes, bending a little at each.
                Stream(1f, new Vector2(-0.75f, -0.4f), new Vector2(-0.45f, -0.3f));
                Stream(1f, new Vector2(-0.45f, -0.3f), new Vector2(-0.05f, -0.4f));
                Stream(1f, new Vector2(-0.05f, -0.4f), new Vector2(0.4f, -0.25f));
                // A creek running into a stream's side.
                Stream(0.6f, new Vector2(-0.25f, -0.8f), new Vector2(-0.25f, -0.37f));
                w.UpdateStreamsFromTerrain();
                {
                    var hw = Scratch("Hand water", 3f);
                    hw.Sky.Shown = false;
                    foreach (var (what, ok) in HandWater(hw)) report.AppendLine($"{(ok ? "ok" : "NOT")}: {what}");
                    hw.UpdateStreamsFromTerrain();
                    hw.Forest.RebuildDirty(); hw.Cover.RebuildDirty();
                    var hc = hw.transform.position;
                    cam.transform.position = hc + new Vector3(0f, 1.9f, -0.15f); cam.transform.LookAt(hc); cam.fieldOfView = 50f;
                    WonderlandRenders.Capture(cam, "editor-joins-water-hand.png", 1200, 1200);
                    foreach (var (name, at) in new[] { ("slant", new Vector2(-0.34f, 0.6f)), ("narrowing", new Vector2(-0.33f, 0.08f)), ("into-lake", new Vector2(0.3f, -0.45f)) })
                    {
                        var p = hc + new Vector3(at.x, 0f, at.y);
                        cam.transform.position = p + new Vector3(0.05f, 0.32f, -0.3f); cam.transform.LookAt(p); cam.fieldOfView = 40f;
                        WonderlandRenders.Capture(cam, $"editor-joins-water-hand-{name}.png", 1280, 900);
                    }
                    Object.DestroyImmediate(hw.gameObject);
                }
                w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
                var c = w.transform.position;
                cam.transform.position = c + new Vector3(0f, 1.9f, -0.15f); cam.transform.LookAt(c); cam.fieldOfView = 50f;
                WonderlandRenders.Capture(cam, "editor-joins-water.png", 1200, 1200);
                foreach (var (name, at) in new[] { ("river", new Vector2(-0.05f, 0.5f)), ("lake", new Vector2(0.42f, 0.45f)), ("strokes", new Vector2(-0.3f, -0.35f)) })
                {
                    var p = c + new Vector3(at.x, 0f, at.y);
                    cam.transform.position = p + new Vector3(0.05f, 0.3f, -0.3f); cam.transform.LookAt(p); cam.fieldOfView = 40f;
                    WonderlandRenders.Capture(cam, $"editor-joins-water-{name}.png", 1280, 900);
                }
                foreach (var s in w.Streams.Streams) report.AppendLine($"  levels (mm): {string.Join(" ", s.points.FindAll(q => s.points.IndexOf(q) % 8 == 0).ConvertAll(q => (q.y * 1000f).ToString("0.0")))}; widths {s.WidthAt(0):0.0}..{s.WidthAt(s.points.Count - 1):0.0}");
                foreach (var s in w.Streams.Streams) report.AppendLine($"stream width {s.Width}: {s.points.Count} points, level {s.points[0].y * 1000f:0.0} -> {s.points[^1].y * 1000f:0.0} mm, from ({s.points[0].x:0.00}, {s.points[0].z:0.00}) to ({s.points[^1].x:0.00}, {s.points[^1].z:0.00}), joins {s.JoinsAtStart}/{s.JoinsAtEnd}");
                Object.DestroyImmediate(w.gameObject);
            }
            System.IO.File.WriteAllText("Logs/joins-results.txt", report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
