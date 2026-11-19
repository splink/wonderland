using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>The sharpest single-cell spike in the grid: how far a cell stands above (or below) the mean of its eight neighbours, and where.</summary>
        private static int worstCell;

        private static float Spike(TerrainField t, out Vector2 at)
        {
            float worst = 0f; at = default; worstCell = -1;
            for (int z = 2; z < t.Nz - 2; z++)
            for (int x = 2; x < t.Nx - 2; x++)
            {
                int i = z * t.Nx + x;
                float sum = 0f, lo = float.MaxValue, hi = float.MinValue;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (dx != 0 || dz != 0) { float v = t.Surface[i + dz * t.Nx + dx]; sum += v; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
                float h = t.Surface[i];
                float s = Mathf.Max(h - hi, lo - h);   // (only a true spike: above all its neighbours, or below all)
                if (s > worst) { worst = s; at = new Vector2(t.LocalX(x), t.LocalZ(z)); worstCell = i; }
            }
            return worst;
        }

        /// <summary>Sculpting round a pond, a stream and a road with every brush leaves no single-cell spikes or pits (roads suite).</summary>
        private static void SpikeJourney()
        {
            {
                // Streams drawn out of a river's side (branches), and into it: no wall of land along them.
                var w = new GameObject("Branch walls").AddComponent<WonderlandWorld>();
                w.Create(2f, Materials(), 7, true, endless: true);
                var t = w.Terrain;
                w.BuildStream(Line(t, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0.05f)), 2.2f);
                // (as in a player's world: branches off its side and off its source, one into it, a short creek drawn
                // inside its channel)
                foreach (var (a, b) in new[] { (new Vector2(-0.3f, 0.005f), new Vector2(-0.25f, 0.45f)), (new Vector2(0.2f, 0.035f), new Vector2(0.35f, -0.4f)), (new Vector2(-0.5f, -0.4f), new Vector2(-0.45f, -0.002f)),
                                               (new Vector2(-0.7f, 0f), new Vector2(-0.62f, 0.4f)), (new Vector2(-0.7f, 0.002f), new Vector2(-0.6f, -0.35f)), (new Vector2(0.05f, 0.022f), new Vector2(0.23f, 0.03f)) })
                    w.BuildStream(Line(t, a, b), 0.6f);
                for (int k = 0; k < 3; k++) w.UpdateStreamsFromTerrain();
                float highest = 0f, wildest = 0f;
                foreach (float h in t.Surface) highest = Mathf.Max(highest, h);
                foreach (var s in w.Streams.Streams) foreach (var q in s.points) wildest = Mathf.Max(wildest, q.y);
                Check(highest < 0.1f && wildest < 0.1f, $"Streams drawn out of a river's side and into it leave no wall of land (highest ground {highest * 1000f:0} mm, highest water {(wildest > 1f ? "unset" : $"{wildest * 1000f:0} mm")}; {w.Streams.Streams.Count} streams)");
                Object.DestroyImmediate(w.gameObject);
            }
            float worst = SpikeRun(out string where);
            Check(worst < 0.005f, $"Sculpting round a pond, a stream and a road with every brush, draining and refilling, leaves no spikes (sharpest cell {worst * 1000f:0.0} mm proud of all its neighbours{where})");
        }

        /// <summary>Sculpting round water, step by step, watching for spikes (Logs/spikes-results.txt).</summary>
        public static void SpikeProbe()
        {
            SpikeRun(out _);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static float SpikeRun(out string where)
        {
            float worstAll = 0f; string worstStep = "";
            var report = new System.Text.StringBuilder();
            var w = new GameObject("Spikes").AddComponent<WonderlandWorld>();
            w.Create(2f, Materials(), 7, true, endless: true);
            var t = w.Terrain;
            void Log(string step)
            {
                float s = Spike(t, out var at);
                if (s > worstAll) { worstAll = s; worstStep = $", {step}"; }
                var c = t.RegionAround(at, 0.001f); int i = worstCell >= 0 ? worstCell : 0;
                float up = t.Surface[i] - t.Surface[i + 1];
                report.AppendLine($"{step}: worst spike {s * 1000f:0.0} mm at ({at.x:0.00}, {at.y:0.00}) {(up > 0 ? "peak" : "pit")} - land {t.Land[i]:0.00} rim {t.Edge[i]:0.00} pad {t.Pad[i]:0.00} lock {t.Lock[i]:0.00} water {t.Water[i]:0.00} level {t.WaterLevel[i] * 1000f:0.0} surface {t.Surface[i] * 1000f:0.0} mm; stream {w.Streams.DistanceTo(at, out _) * 100f:0.0} cm, road {w.Paths.WayDistance(at, out _) * 100f:0.0} cm");
            }
            Log("start");
            var pond = new Vector3(0.2f, 0f, 0.1f);
            float level = w.WaterLevelForStroke(pond);
            for (int k = 0; k < 40; k++) w.WaterStroke(pond, 0.18f, 0.9f, 0.05f, level, false);
            Log("pond");
            w.BuildStream(Line(t, new Vector2(-0.6f, -0.2f), new Vector2(0.1f, 0.05f)), 1f);
            Log("stream into the pond");
            w.BuildPath(Line(t, new Vector2(-0.6f, 0.4f), new Vector2(0.6f, 0.4f)), PathKind.Road);
            Log("road");
            var rng = new Rng(9);
            var brushes = new[] { TerrainBrush.Raise, TerrainBrush.Lower, TerrainBrush.Smooth, TerrainBrush.Flatten, TerrainBrush.Peaks };
            for (int step = 0; step < 60; step++)
            {
                var b = brushes[rng.Range(0, brushes.Length)];
                var c = new Vector3(rng.Range(-0.5f, 0.5f), 0f, rng.Range(-0.3f, 0.5f));
                float flat = t.SurfaceAt(c.x, c.z);
                for (int k = 0; k < 12; k++) w.TerrainStroke(b, c, rng.Range(0.06f, 0.2f), rng.Range(0.4f, 1f), 0.05f, flat);
                if (step % 3 == 0) w.UpdateStreamsFromTerrain();
                if (rng.Chance(0.2f)) for (int k = 0; k < 10; k++) w.WaterStroke(c, 0.1f, 0.9f, 0.05f, w.WaterLevelForStroke(c), rng.Chance(0.3f));
                Log($"step {step} {b} at ({c.x:0.00}, {c.z:0.00})");
            }
            System.IO.File.WriteAllText("Logs/spikes-results.txt", report.ToString());
            Object.DestroyImmediate(w.gameObject);
            where = worstStep;
            return worstAll;
        }
    }
}
