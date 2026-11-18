using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    [Serializable]
    public sealed class StreamPath
    {
        public List<Vector3> points = new();   // x, water level, z (1.5 cm apart, level never rises downstream)
        public float width;                     // 1 a stream, less a creek, more a river (0: older saves, a stream)
        [NonSerialized] public bool JoinsAtStart, JoinsAtEnd;   // flows out of / into another stream there (full width, no taper)
        public float Width => width > 0f ? width : 1f;
    }

    /// <summary>
    /// Streams: drawn like the railway, they carve a channel with soft banks and carry a flowing ribbon of water whose
    /// level only ever falls along the stream (it cuts through rises rather than flowing uphill); where it drops faster
    /// the water turns white, and where it tumbles down a slope there is a waterfall, spray rising from its foot.
    /// Three widths: a creek, a stream, a broad river (wider and deeper channel). Several streams may exist; they are
    /// saved and undoable.
    /// </summary>
    public sealed class StreamSystem : MonoBehaviour
    {
        public const float HalfWidth = 0.02f, Bank = 0.026f, Depth = 0.013f, Spacing = 0.015f;
        public readonly List<StreamPath> Streams = new();
        private readonly List<ParticleSystem> falls = new();
        /// <summary>A drop this steep over two steps is a waterfall (spray at its foot).</summary>
        public const float FallDrop = 0.012f;
        /// <summary>The soft particle material for the spray (set by the world).</summary>
        public Material Smoke;
        /// <summary>Waterfalls along the streams now (tests).</summary>
        public int Waterfalls => falls.Count;
        private readonly List<(GameObject go, Mesh mesh)> ribbons = new();
        private Material material;

        public void Init(Material water) => material = water;
        /// <summary>The land (where the ponds are: a stream's ribbon ends at a pond's surface).</summary>
        public TerrainField Terrain;

        private static bool InPond(TerrainField t, float x, float z) => t != null && t.WaterAt(x, z) > 0.5f && t.WaterLevelAt(x, z) > 0f;

        /// <summary>The water level a stream would have here: a pond's own surface in a pond, else just below the ground.</summary>
        private static float Target(TerrainField t, float x, float z) => InPond(t, x, z) ? t.WaterLevelAt(x, z) - 0.0005f : t.SurfaceAt(x, z) - 0.004f;

        /// <summary>Smooths a drawn stroke into a stream course and gives it a downhill water level. Null if too short.</summary>
        public static StreamPath Plan(IReadOnlyList<Vector3> drawn, TerrainField terrain, float width = 1f)
        {
            var raw = new List<Vector2>();
            foreach (var p in drawn) if (raw.Count == 0 || (new Vector2(p.x, p.z) - raw[^1]).magnitude > 0.004f) raw.Add(new Vector2(p.x, p.z));
            if (raw.Count < 3 || TrackPlanner.Length(drawn) < 0.15f) return null;
            var pts = TrackPlanner.Resample(raw, Spacing, false);
            for (int it = 0; it < 30; it++)
            {
                var copy = pts.ToArray();
                for (int i = 1; i < pts.Count - 1; i++) pts[i] = copy[i] + ((copy[i - 1] + copy[i + 1]) * 0.5f - copy[i]) * 0.5f;
            }
            pts = TrackPlanner.Resample(pts, Spacing, false);
            // Water runs downhill: drawn from the lower end (a lower pond) to the higher, it flows the other way.
            if (Target(terrain, pts[^1].x, pts[^1].y) > Target(terrain, pts[0].x, pts[0].y) + 0.003f) pts.Reverse();
            var path = new StreamPath { width = width };
            float level = float.MaxValue;
            foreach (var p in pts)
            {
                // Through a pond the stream is the pond: its level is the pond's surface, not the pond's bed.
                level = Mathf.Max(TerrainField.MinHeight + Depth * 0.6f + 0.002f, Mathf.Min(level, Target(terrain, p.x, p.y)));
                path.points.Add(new Vector3(p.x, level, p.y));
            }
            return path;
        }

        /// <summary>Re-levels a stream on its course from the (sculpted) ground, still never flowing uphill.</summary>
        public static void Relevel(StreamPath s, TerrainField terrain)
        {
            float level = float.MaxValue;
            for (int i = 0; i < s.points.Count; i++)
            {
                var p = s.points[i];
                float ground = terrain.SurfaceAt(p.x, p.z) - 0.004f;
                if (InPond(terrain, p.x, p.z)) { level = Mathf.Min(level, Target(terrain, p.x, p.z)); s.points[i] = new Vector3(p.x, level, p.z); continue; }
                // Sample the banks, not the channel bed, so a stream never digs itself ever deeper.
                var t = (s.points[Mathf.Min(i + 1, s.points.Count - 1)] - s.points[Mathf.Max(i - 1, 0)]); t.y = 0f;
                if (t.sqrMagnitude > 1e-8f)
                {
                    var side = Vector3.Cross(Vector3.up, t.normalized) * (HalfWidth + Bank * 0.8f) * s.Width;
                    ground = Mathf.Min(terrain.SurfaceAt(p.x + side.x, p.z + side.z), terrain.SurfaceAt(p.x - side.x, p.z - side.z)) - 0.004f;
                }
                level = Mathf.Max(TerrainField.MinHeight + Depth * DepthScale(s) * 0.6f + 0.002f, Mathf.Min(level, ground));
                s.points[i] = new Vector3(p.x, level, p.z);
            }
        }

        /// <summary>Carves the channel and shapes the banks around a stream; returns the touched region.</summary>
        public static RectInt Carve(TerrainField terrain, StreamPath s)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in s.points) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
            float reach = (HalfWidth + Bank) * s.Width;
            bool wetStart = InPond(terrain, s.points[0].x, s.points[0].z) || s.JoinsAtStart, wetEnd = InPond(terrain, s.points[^1].x, s.points[^1].z) || s.JoinsAtEnd;
            var region = terrain.RegionAround(new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f), Mathf.Max(maxX - minX, maxZ - minZ) / 2f + reach + 0.02f);
            for (int z = region.yMin; z < region.yMax; z++)
            for (int x = region.xMin; x < region.xMax; x++)
            {
                var c = new Vector2(terrain.LocalX(x), terrain.LocalZ(z));
                float d = Nearest(s, c, out float level, out float along);
                if (d >= reach) continue;
                int i = z * terrain.Nx + x;
                if (terrain.Land[i] < 0.5f || terrain.Pad[i] > 0.05f) continue;
                if (terrain.Water != null && terrain.Water[i] > 0.5f) continue;   // in a pond: the pond is the stream there (no dam across it)
                float taper = (wetStart ? 1f : Mathf.Clamp01(along / 0.05f)) * (wetEnd ? 1f : Mathf.Clamp01((PathLength(s) - along) / 0.05f));   // full width into a pond
                float hw = HalfWidth * s.Width * Mathf.Lerp(0.45f, 1f, taper);
                if (d < hw)
                {
                    float bed = level - Depth * DepthScale(s) * Mathf.Lerp(0.5f, 1f, taper) * (1f - (d / hw) * (d / hw)) - 0.002f;
                    terrain.Height[i] = Mathf.Min(terrain.Height[i], terrain.HeightForSurface(i, bed));
                }
                else
                {
                    float w = 1f - (d - hw) / (reach - hw);
                    float bank = level + 0.002f + (d - hw) * 0.25f;
                    float target = terrain.HeightForSurface(i, bank);
                    if (terrain.Surface[i] < level + 0.0015f) terrain.Height[i] = Mathf.Max(terrain.Height[i], target);
                    else terrain.Height[i] = Mathf.Lerp(terrain.Height[i], Mathf.Min(terrain.Height[i], target), w * w);
                }
            }
            terrain.UpdateSurface(region);
            return region;
        }

        private static float PathLength(StreamPath s) => (s.points.Count - 1) * Spacing;

        /// <summary>
        /// Joins the ends of streams that start or end on another stream: the end is moved onto the other's course (and a
        /// tributary's water meets the other's surface), and that end keeps its full width - so creeks run into streams,
        /// streams into rivers, rivers into lakes, as one network.
        /// </summary>
        public static void Join(List<StreamPath> all)
        {
            foreach (var s in all) { s.JoinsAtStart = s.JoinsAtEnd = false; }
            foreach (var s in all)
            {
                if (s.points.Count < 2) continue;
                foreach (var o in all)
                {
                    if (o == s || o.points.Count < 2) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        int i = end == 0 ? 0 : s.points.Count - 1;
                        var p = s.points[i];
                        float d = Nearest(o, new Vector2(p.x, p.z), out float level, out float along);
                        if (d > HalfWidth * Mathf.Max(o.Width, s.Width) + 0.03f) continue;
                        // Onto the other's course.
                        int k = Mathf.Clamp(Mathf.RoundToInt(along / Spacing), 0, o.points.Count - 1);
                        var q = o.points[k];
                        float y = end == 1 ? Mathf.Max(level, Mathf.Min(p.y, level)) : Mathf.Max(p.y, level);
                        s.points[i] = new Vector3(q.x, end == 1 ? level : y, q.z);
                        if (end == 0) s.JoinsAtStart = true; else s.JoinsAtEnd = true;
                    }
                }
            }
        }

        /// <summary>A river's channel is deeper too (not in proportion: a creek is still a little channel).</summary>
        private static float DepthScale(StreamPath s) => Mathf.Lerp(1f, s.Width, 0.6f);

        /// <summary>Horizontal distance from a point to a stream, with the water level and arc length there.</summary>
        public static float Nearest(StreamPath s, Vector2 p, out float level, out float along)
        {
            float best = float.MaxValue; level = 0f; along = 0f;
            for (int i = 0; i < s.points.Count - 1; i++)
            {
                var a = new Vector2(s.points[i].x, s.points[i].z); var b = new Vector2(s.points[i + 1].x, s.points[i + 1].z);
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
                float d = (p - (a + ab * t)).sqrMagnitude;
                if (d < best) { best = d; level = Mathf.Lerp(s.points[i].y, s.points[i + 1].y, t); along = (i + t) * Spacing; }
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>Distance to the nearest stream (infinite without streams) and its water level there.</summary>
        public float DistanceTo(Vector2 p, out float level)
        {
            float best = float.MaxValue; level = -1f;
            foreach (var s in Streams)
            {
                float d = Nearest(s, p, out float l, out _);
                if (d < best) { best = d; level = l; }
            }
            return best;
        }

        /// <summary>On (or within <paramref name="margin"/> of) a stream's water. Streams far away are skipped by their bounding boxes.</summary>
        public bool IsWet(Vector2 p, float margin = 0f)
        {
            float reach = HalfWidth * 2.2f + margin;
            if (boxes.Count != Streams.Count || boxedPoints != PointCount()) Box();
            for (int i = 0; i < Streams.Count; i++)
            {
                var b = boxes[i];
                if (p.x < b.xMin - reach || p.x > b.xMax + reach || p.y < b.yMin - reach || p.y > b.yMax + reach) continue;
                if (Nearest(Streams[i], p, out _, out _) < HalfWidth * Streams[i].Width + margin) return true;
            }
            return false;
        }

        private readonly List<Rect> boxes = new();
        private int boxedPoints = -1;

        private int PointCount() { int n = 0; foreach (var s in Streams) n += s.points.Count; return n; }

        private void Box()
        {
            boxes.Clear();
            foreach (var s in Streams)
            {
                float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                foreach (var q in s.points) { x0 = Mathf.Min(x0, q.x); z0 = Mathf.Min(z0, q.z); x1 = Mathf.Max(x1, q.x); z1 = Mathf.Max(z1, q.z); }
                boxes.Add(Rect.MinMaxRect(x0, z0, x1, z1));
            }
            boxedPoints = PointCount();
        }

        public int Pick(Vector2 p, float within)
        {
            for (int i = 0; i < Streams.Count; i++) if (Nearest(Streams[i], p, out _, out _) < within) return i;
            return -1;
        }

        public void SetStreams(IEnumerable<StreamPath> streams)
        {
            Streams.Clear();
            foreach (var s in streams) Streams.Add(new StreamPath { points = new List<Vector3>(s.points), width = s.width });
            Rebuild();
        }

        /// <summary>Flowing ribbons of water along every stream (UV.y = distance downstream drives the flow).</summary>
        public void Rebuild()
        {
            Join(Streams);
            foreach (var r in ribbons) { if (Application.isPlaying) { Destroy(r.go); Destroy(r.mesh); } else { DestroyImmediate(r.go); DestroyImmediate(r.mesh); } }
            ribbons.Clear();
            foreach (var f in falls) if (f != null) { if (Application.isPlaying) Destroy(f.gameObject); else DestroyImmediate(f.gameObject); }
            falls.Clear();
            foreach (var s in Streams)
            {
                if (s.points.Count < 2) continue;
                var b = new MeshBuilder();
                int n = s.points.Count;
                const int across = 4;
                // Where it runs through a pond the pond's own water shows; the ribbon meets it at full width (no taper).
                var wet = new bool[n];
                for (int i = 0; i < n; i++) wet[i] = InPond(Terrain, s.points[i].x, s.points[i].z);
                float startTaper = wet[0] || s.JoinsAtStart ? 0f : 0.05f, endTaper = wet[n - 1] || s.JoinsAtEnd ? 0f : 0.05f;
                for (int i = 0; i < n; i++)
                {
                    var p = s.points[i];
                    var t = s.points[Mathf.Min(i + 1, n - 1)] - s.points[Mathf.Max(i - 1, 0)]; t.y = 0f; t.Normalize();
                    var side = Vector3.Cross(Vector3.up, t);
                    float along = i * Spacing, length = (n - 1) * Spacing;
                    float taper = (startTaper > 0f ? Mathf.Clamp01(along / startTaper) : 1f) * (endTaper > 0f ? Mathf.Clamp01((length - along) / endTaper) : 1f);
                    float hw = (HalfWidth * s.Width + 0.006f) * Mathf.Lerp(0.45f, 1f, taper);
                    float drop = (s.points[Mathf.Max(i - 1, 0)].y - s.points[Mathf.Min(i + 1, n - 1)].y) / (2f * Spacing);
                    float rapids = Mathf.Clamp01(drop * 12f);
                    for (int k = 0; k <= across; k++)
                    {
                        float u = k / (float)across, off = (u - 0.5f) * 2f;
                        float depth = (1f - off * off) * 0.75f;
                        float shore = Mathf.Max(Mathf.Abs(off) > 0.75f ? 1f : 0f, rapids * 0.8f);
                        int v = b.AddVertex(p + side * off * hw + Vector3.up * 0.0004f, Vector3.up, new Color(depth, shore, 0.5f + rapids * 0.5f, 1f));
                        b.Uvs[v] = new Vector2(u * 0.6f, along);
                    }
                    if (i > 0 && !(wet[i] && wet[i - 1]))
                        for (int k = 0; k < across; k++)
                        {
                            int a = (i - 1) * (across + 1) + k, c = i * (across + 1) + k;
                            b.AddTriangle(a, c, c + 1); b.AddTriangle(a, c + 1, a + 1);
                        }
                }
                // Waterfalls: where it drops steeply, spray at the foot of the fall (one per fall, a few per stream).
                for (int i = 1, last = -99; i < n - 1 && falls.Count < 24; i++)
                {
                    float drop = s.points[i - 1].y - s.points[i + 1].y;
                    if (drop < FallDrop || wet[i] || i - last < 6) continue;
                    last = i;
                    int foot = Mathf.Min(i + 2, n - 1);
                    var spray = TrafficSystem.Smoke(transform, transform.TransformPoint(s.points[foot] + Vector3.up * 0.002f), Smoke ?? material, new Color(0.92f, 0.96f, 1f, 0.55f), 10f * s.Width, 0.007f * s.Width, Vector3.up, 0.012f);
                    if (spray != null) falls.Add(spray);
                }
                var go = new GameObject("Stream");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = b.ToMesh("Stream");
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off;
                ribbons.Add((go, go.GetComponent<MeshFilter>().sharedMesh));
            }
        }
    }
}
