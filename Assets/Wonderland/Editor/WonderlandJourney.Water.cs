using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// The water as a hand builds it, in one world: two streams drawn towards each other until they meet; a stream
        /// widened by a second stroke along it; a stroke drawn across a stream; a lake painted in three strokes begun at
        /// different heights, with a stream running into it; a road and a railway over a stream. <paramref name="report"/>
        /// gathers what came of each.
        /// </summary>
        private static WonderlandWorld WaterScene(float x, System.Text.StringBuilder report)
        {
            var w = Scratch("Water", x);
            var t = w.Terrain;
            void Stream(string what, float width, params Vector2[] via)
            {
                string e = w.BuildStream(Line(t, via), width);
                report?.AppendLine($"{what}: {e ?? "ok"}, {w.Streams.Streams.Count} streams");
            }
            // A low hill under the lake's eastern half, so strokes begun there start higher.
            for (int k = 0; k < 40; k++) w.TerrainStroke(TerrainBrush.Raise, new Vector3(0.62f, 0f, -0.05f), 0.16f, 0.5f, Dt, 0f);
            // Two streams drawn towards each other, meeting end to end.
            Stream("meeting: first", 1f, new Vector2(-0.85f, 0.62f), new Vector2(-0.5f, 0.6f));
            Stream("meeting: second, drawn towards the first", 1f, new Vector2(-0.12f, 0.68f), new Vector2(-0.48f, 0.6f));
            // A stream widened by a stroke along it, 1.5 cm to one side.
            Stream("widen: stream", 1f, new Vector2(0.1f, 0.6f), new Vector2(0.85f, 0.62f));
            Stream("widen: stroke alongside", 1f, new Vector2(0.2f, 0.615f), new Vector2(0.75f, 0.635f));
            // A stroke across a stream.
            Stream("cross: stream", 1f, new Vector2(-0.85f, 0.2f), new Vector2(-0.15f, 0.25f));
            Stream("cross: stroke across it", 1f, new Vector2(-0.5f, 0.45f), new Vector2(-0.48f, -0.05f));
            // A lake in three strokes, begun on low ground, on the hill and between; a stream running into it.
            foreach (var (at, seconds) in new[] { (new Vector3(0.3f, 0f, -0.1f), 1.4f), (new Vector3(0.62f, 0f, -0.05f), 1.4f), (new Vector3(0.45f, 0f, 0.1f), 1.2f) })
            {
                float level = w.WaterLevelForStroke(at);
                w.Checkpoint();
                for (float s = 0f; s < seconds; s += Dt) w.WaterStroke(at + new Vector3(Mathf.Sin(s * 3f) * 0.05f, 0f, Mathf.Cos(s * 2f) * 0.04f), 0.14f, 0.9f, Dt, level, false);
                report?.AppendLine($"lake stroke at {at}: level {level * 1000f:0.0} mm");
            }
            Stream("into the lake", 1f, new Vector2(0.05f, 0.35f), new Vector2(0.2f, 0.15f), new Vector2(0.3f, 0.02f));
            // A road and a railway over a stream.
            Stream("under the bridges", 1f, new Vector2(-0.6f, -0.85f), new Vector2(-0.45f, -0.3f), new Vector2(-0.2f, -0.2f));
            report?.AppendLine("road: " + (w.BuildPath(Line(t, new Vector2(-0.75f, -0.45f), new Vector2(-0.15f, -0.5f)), PathKind.Road) ?? "ok"));
            var rail = w.BuildTrack(Line(t, new Vector2(-0.75f, -0.68f), new Vector2(-0.1f, -0.72f)));
            report?.AppendLine("railway: " + (rail.Ok ? "ok" : rail.Error) + $", {w.Bridges.Bridges.Count} bridges");
            w.RefreshBridges();
            return w;
        }

        /// <summary>
        /// Water as one system: a stroke along a stream widens it (no second stream on top); a stroke across one stops at
        /// its banks, the parts either side meeting it at its side (never lying over its water); a lake painted in three
        /// strokes begun at different heights has one level; a stream over which a road passes keeps its bridge's piers on
        /// its banks.
        /// </summary>
        private static void WaterJourney()
        {
            var report = new System.Text.StringBuilder();
            var w = WaterScene(1240f, report);
            var t = w.Terrain;
            StreamPath At(Vector2 p) => w.Streams.Streams.Find(s => StreamSystem.Nearest(s, p, out _, out _) < 0.012f);
            // Widened: one stream along the first stroke, wider in the middle than at its ends.
            var widened = At(new Vector2(0.45f, 0.62f));
            int along = widened != null ? w.Streams.Streams.FindAll(s => StreamSystem.Nearest(s, new Vector2(0.45f, 0.62f), out _, out _) < 0.03f).Count : 0;
            float middle = 0f;
            if (widened != null) { StreamSystem.Nearest(widened, new Vector2(0.45f, 0.62f), out _, out float a); middle = widened.WidthAlong(a); }
            Check(widened != null && along == 1 && middle > 1.3f, $"A stroke drawn along a stream widens it: one stream there ({along}), {middle:0.0} times as wide in the middle");
            // Crossed: the stream across is in two parts, each meeting the stream at its side and nowhere else over its water.
            var crossed = At(new Vector2(-0.3f, 0.235f));
            var parts = w.Streams.Streams.FindAll(s => s != crossed && (StreamSystem.Nearest(s, new Vector2(-0.49f, 0.4f), out _, out _) < 0.02f || StreamSystem.Nearest(s, new Vector2(-0.485f, 0f), out _, out _) < 0.02f));
            int over = 0;
            foreach (var part in parts)
                for (int i = 4; i < part.points.Count - 4; i++)
                {
                    var q = new Vector2(part.points[i].x, part.points[i].z);
                    if (crossed != null && StreamSystem.Nearest(crossed, q, out _, out float a) < StreamSystem.HalfWidth * crossed.WidthAlong(a)) over++;
                }
            bool meet = parts.TrueForAll(s => s.JoinsAtStart || s.JoinsAtEnd);
            Check(crossed != null && parts.Count == 2 && over == 0 && meet, $"A stroke drawn across a stream stops at its banks: {parts.Count} parts, each meeting it ({meet}), {over} points over its water");
            // The lake: one level everywhere it is water.
            float lo = float.MaxValue, hi = float.MinValue; int cells = 0;
            for (int i = 0; i < t.Water.Length; i++)
            {
                if (t.Water[i] < 0.5f) continue;
                float x = t.LocalX(i % t.Nx), z = t.LocalZ(i / t.Nx);
                if (x < 0.1f || x > 0.85f || z < -0.3f || z > 0.3f) continue;
                cells++; lo = Mathf.Min(lo, t.WaterLevel[i]); hi = Mathf.Max(hi, t.WaterLevel[i]);
            }
            Check(cells > 100 && hi - lo < TerrainField.LevelTolerance * 2f, $"A lake painted in three strokes begun at different heights is one body of water at one level ({cells} cells, levels {lo * 1000f:0.0} to {hi * 1000f:0.0} mm)");
            // The stream runs into the lake.
            var inflow = At(new Vector2(0.2f, 0.15f));
            var mouth = inflow != null ? inflow.points[^1] : Vector3.zero;
            Check(inflow != null && t.WaterAt(mouth.x, mouth.z) > 0.5f, "A stream drawn to the lake runs into it");
            // Bridges over the stream: water room under both, and the road bridge's arches spanning it.
            float room = float.MaxValue;
            for (int i = 0; i < w.Bridges.Bridges.Count; i++) room = Mathf.Min(room, w.Bridges.ClearanceOf(i));
            Check(w.Bridges.Bridges.Count == 2 && room > 0.012f && room < 0.05f, $"A road and a railway over a stream get bridges with the water passing under them ({w.Bridges.Bridges.Count} bridges, at least {room * 1000f:0.0} mm of room)" + (w.Bridges.Bridges.Count == 2 ? "" : "\n" + report));
            Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Renders of the water scene (docs/renders/editor-water-*.png) and what came of each stroke (Logs/water-results.txt).</summary>
        public static void WaterRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var key = WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), Vector3.zero, 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var report = new System.Text.StringBuilder();
            var w = WaterScene(0f, report);
            w.Sky.Shown = false;
            w.Clock.Hour = 11f;
            w.Cover.RebuildDirty(); w.Forest.RebuildDirty();
            void Shot(string file, Vector3 at, Vector3 from, float fov) { cam.transform.position = at + from; cam.transform.LookAt(at); cam.fieldOfView = fov; WonderlandRenders.Capture(cam, file, 1600, 1000); }
            Shot("editor-water-overview.png", Vector3.zero, new Vector3(0f, 1.3f, -1.1f), 50f);
            Shot("editor-water-meet.png", new Vector3(-0.48f, 0f, 0.6f), new Vector3(0.05f, 0.22f, -0.3f), 40f);
            Shot("editor-water-widen.png", new Vector3(0.45f, 0f, 0.62f), new Vector3(0.05f, 0.25f, -0.32f), 40f);
            Shot("editor-water-cross.png", new Vector3(-0.49f, 0f, 0.22f), new Vector3(0.05f, 0.22f, -0.3f), 40f);
            Shot("editor-water-lake.png", new Vector3(0.45f, 0f, 0f), new Vector3(0f, 0.45f, -0.45f), 42f);
            Shot("editor-water-mouth.png", new Vector3(0.27f, 0f, 0.07f), new Vector3(-0.1f, 0.15f, -0.2f), 40f);
            foreach (var br in w.Bridges.Bridges)
            {
                var at = new Vector3(br.Anchor.x, 0.015f, br.Anchor.y);
                string kind = br.Rail ? "rail" : "road";
                Shot($"editor-water-bridge-{kind}.png", at, new Vector3(0.16f, 0.05f, -0.1f), 40f);
                Shot($"editor-water-bridge-{kind}-low.png", at, new Vector3(0.02f, 0.012f, -0.13f), 40f);
                report.AppendLine($"{kind} bridge {br.Style} at {br.Anchor}: {br.Length * 100f:0} cm, room under it {w.Bridges.ClearanceOf(w.Bridges.Bridges.IndexOf(br)) * 1000f:0.0} mm");
            }
            System.IO.File.WriteAllText("Logs/water-results.txt", report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
