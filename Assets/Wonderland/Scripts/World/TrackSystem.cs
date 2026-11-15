using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// The single railway: generates ballast, sleepers and rails along a <see cref="TrackLayout"/>, grades and locks the
    /// terrain corridor under it, and draws the stroke preview while the player is drawing.
    /// Terrain rule: laying a track grades the ground to the track's smooth profile (embankments / cuttings) and locks the
    /// corridor; terrain brushes skip locked ground, so later sculpting can never bury or float the track. Erasing the
    /// track unlocks the ground (the graded bed remains as a gentle path).
    /// </summary>
    public sealed class TrackSystem : MonoBehaviour
    {
        public const float RailTop = 0.0063f;    // rail top above the ballast bed
        public const float BedHalfWidth = 0.042f;
        public const float CorridorHalfWidth = 0.12f;

        public TrackLayout Layout { get; private set; }
        private MeshFilter trackFilter;
        private MeshFilter previewFilter;
        private Mesh trackMesh, previewMesh;
        private Material previewMaterial;

        public void Init(Material lit, Material ui)
        {
            var track = new GameObject("Railway");
            track.transform.SetParent(transform, false);
            trackFilter = track.AddComponent<MeshFilter>();
            var r = track.AddComponent<MeshRenderer>();
            r.sharedMaterial = lit; r.shadowCastingMode = ShadowCastingMode.On;
            trackMesh = new Mesh { name = "Railway", indexFormat = IndexFormat.UInt32 };
            trackFilter.sharedMesh = trackMesh;

            var preview = new GameObject("Track preview");
            preview.transform.SetParent(transform, false);
            previewFilter = preview.AddComponent<MeshFilter>();
            var pr = preview.AddComponent<MeshRenderer>();
            previewMaterial = new Material(ui) { renderQueue = 3005 };
            pr.sharedMaterial = previewMaterial; pr.shadowCastingMode = ShadowCastingMode.Off;
            previewMesh = new Mesh { name = "Track preview" };
            previewMesh.MarkDynamic();
            previewFilter.sharedMesh = previewMesh;
        }

        public bool HasTrack => Layout != null;

        public void SetLayout(TrackLayout layout)
        {
            Layout = layout;
            trackMesh.Clear();
            if (layout == null) return;
            var b = new MeshBuilder();
            BuildTrack(b, layout);
            b.ToMesh("Railway", trackMesh);
            trackMesh.RecalculateBounds();
        }

        public static void BuildTrack(MeshBuilder b, TrackLayout layout)
        {
            var pts = layout.Points; int n = pts.Count;
            var right = new List<Vector3>(n); var up = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                var a = pts[layout.Closed ? (i - 1 + n) % n : Mathf.Max(0, i - 1)];
                var c = pts[layout.Closed ? (i + 1) % n : Mathf.Min(n - 1, i + 1)];
                var t = c - a; t.y = 0f; t.Normalize();
                right.Add(Vector3.Cross(Vector3.up, t).normalized);
                up.Add(Vector3.up);
            }
            // Ballast bed: trapezoid that sinks into the graded ground; gravel speckle per vertex.
            int start = b.VertexCount;
            var ballastProfile = new[] { new Vector2(-BedHalfWidth, -0.012f), new Vector2(-0.027f, 0.0006f), new Vector2(0.027f, 0.0006f), new Vector2(BedHalfWidth, -0.012f) };
            var ballastColours = new[] { Pal.BallastDark, Pal.Ballast, Pal.Ballast, Pal.BallastDark };
            b.AddExtrusion(pts, right, up, ballastProfile, ballastColours, layout.Closed);
            for (int i = start; i < b.VertexCount; i++)
            {
                var v = b.Vertices[i];
                float g = Noise.Hash(Mathf.RoundToInt(v.x * 2000f), Mathf.RoundToInt(v.z * 2000f), i & 3);
                var c = (Color)b.Colors[i];
                b.Colors[i] = Pal.Shade(c, 0.86f + g * 0.26f);
            }
            // Sleepers every 14 mm, slightly irregular like hand-laid track.
            var rng = new Rng(77);
            for (float s = 0.005f; s < layout.Length - (layout.Closed ? 0.007f : 0.004f); s += 0.014f)
            {
                var p = layout.Evaluate(s, out var tangent);
                tangent.y = 0f;
                var rot = Quaternion.LookRotation(tangent.normalized, Vector3.up) * Quaternion.Euler(0f, rng.Range(-2.5f, 2.5f), 0f);
                b.Push(p + Vector3.up * 0.0016f, rot);
                var colour = Pal.Jitter(Pal.Sleeper, ref rng, 0.25f);
                b.AddBox(Vector3.zero, new Vector3(0.046f, 0.0028f, 0.0065f), Pal.Shade(colour, 0.8f), colour, colour);
                b.Pop();
            }
            // Rails: a simple flat-bottom profile with a bright running surface and rusty sides.
            var railProfile = new[]
            {
                new Vector2(-0.0008f, 0f), new Vector2(-0.0008f, 0.0026f), new Vector2(-0.0012f, 0.0028f),
                new Vector2(-0.0012f, 0.0035f), new Vector2(0.0012f, 0.0035f), new Vector2(0.0012f, 0.0028f),
                new Vector2(0.0008f, 0.0026f), new Vector2(0.0008f, 0f),
            };
            var railColours = new[] { Pal.RailSide, Pal.RailSide, Pal.RailSide, Pal.RailTop, Pal.RailTop, Pal.RailSide, Pal.RailSide, Pal.RailSide };
            foreach (int side in new[] { -1, 1 })
            {
                var rail = new List<Vector3>(n);
                for (int i = 0; i < n; i++) rail.Add(pts[i] + right[i] * (side * TrainFactory.Gauge * 0.5f) + Vector3.up * 0.0028f);
                b.AddExtrusion(rail, right, up, railProfile, railColours, layout.Closed);
            }
            if (!layout.Closed)
            {
                // Buffer stops at both ends.
                foreach (bool atStart in new[] { true, false })
                {
                    var p = atStart ? pts[0] : pts[n - 1];
                    var t = atStart ? pts[0] - pts[1] : pts[n - 1] - pts[n - 2]; t.y = 0f;
                    b.Push(p, Quaternion.LookRotation(t.normalized, Vector3.up));
                    b.AddBox(new Vector3(0f, 0.008f, 0f), new Vector3(0.034f, 0.008f, 0.005f), Pal.TrainRed);
                    b.AddBox(new Vector3(0f, 0.004f, -0.004f), new Vector3(0.03f, 0.008f, 0.006f), Pal.Timber);
                    b.Pop();
                }
            }
        }

        /// <summary>Grades the corridor to the track bed and locks it; returns the touched region.</summary>
        public static RectInt GradeTerrain(TerrainField terrain, TrackLayout layout)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in layout.Points) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
            var region = terrain.RegionAround(new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f), Mathf.Max(maxX - minX, maxZ - minZ) / 2f + CorridorHalfWidth + 0.02f);
            // Bucket track points into 6 cm cells for fast nearest queries.
            const float cellSize = 0.06f;
            var buckets = new Dictionary<long, List<int>>();
            long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            for (int i = 0; i < layout.Points.Count; i++)
            {
                var p = layout.Points[i];
                long k = Key(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));
                if (!buckets.TryGetValue(k, out var list)) buckets[k] = list = new List<int>();
                list.Add(i);
            }
            int reach = Mathf.CeilToInt(CorridorHalfWidth / cellSize) + 1;
            for (int z = region.yMin; z < region.yMax; z++)
            for (int x = region.xMin; x < region.xMax; x++)
            {
                float px = terrain.LocalX(x), pz = terrain.LocalZ(z);
                int cx = Mathf.FloorToInt(px / cellSize), cz = Mathf.FloorToInt(pz / cellSize);
                float best = float.MaxValue; int bestIndex = -1;
                for (int oz = -reach; oz <= reach; oz++)
                for (int ox = -reach; ox <= reach; ox++)
                {
                    if (!buckets.TryGetValue(Key(cx + ox, cz + oz), out var list)) continue;
                    foreach (int i in list)
                    {
                        var q = layout.Points[i];
                        float d = (q.x - px) * (q.x - px) + (q.z - pz) * (q.z - pz);
                        if (d < best) { best = d; bestIndex = i; }
                    }
                }
                if (bestIndex < 0) continue;
                float dist = Mathf.Sqrt(best);
                if (dist > CorridorHalfWidth) continue;
                int g = z * TerrainField.N + x;
                float w = dist < BedHalfWidth * 0.8f ? 1f : Mathf.SmoothStep(1f, 0f, (dist - BedHalfWidth * 0.8f) / (CorridorHalfWidth - BedHalfWidth * 0.8f));
                float target = layout.Points[bestIndex].y - 0.0015f;
                terrain.Height[g] = Mathf.Lerp(terrain.Height[g], terrain.HeightForSurface(g, target), w);
                terrain.Lock[g] = Mathf.Max(terrain.Lock[g], Mathf.Clamp01(w * 1.15f));
                terrain.Soil[g] = Mathf.Max(terrain.Soil[g], Mathf.SmoothStep(1f, 0f, (dist - 0.03f) / 0.04f));
            }
            terrain.UpdateSurface(region);
            return region;
        }

        public static void UnlockTerrain(TerrainField terrain)
        {
            System.Array.Clear(terrain.Lock, 0, terrain.Lock.Length);
            System.Array.Clear(terrain.Soil, 0, terrain.Soil.Length);
        }

        /// <summary>Horizontal distance from <paramref name="p"/> to the track centre line (infinite without a track).</summary>
        public float DistanceTo(Vector3 p)
        {
            if (Layout == null) return float.MaxValue;
            Layout.Nearest(p, out float d);
            return d;
        }

        // ------------------------------------------------------------------ drawing preview

        /// <summary>
        /// Ribbon along the stroke being drawn. <paramref name="closing"/> highlights the start point (release to close the
        /// loop); <paramref name="error"/> paints it red.
        /// </summary>
        public void ShowPreview(IReadOnlyList<Vector3> points, bool closing, bool error)
        {
            previewMesh.Clear();
            if (points == null || points.Count < 2) return;
            var b = new MeshBuilder();
            var colour = error ? new Color(1f, 0.35f, 0.3f, 0.85f) : new Color(1f, 0.92f, 0.55f, 0.85f);
            const float half = 0.012f;
            for (int i = 0; i < points.Count; i++)
            {
                var prev = points[Mathf.Max(0, i - 1)]; var next = points[Mathf.Min(points.Count - 1, i + 1)];
                var t = next - prev; t.y = 0f;
                var side = Vector3.Cross(Vector3.up, t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward) * half;
                var c = colour; c.a *= Mathf.Lerp(0.5f, 1f, i / (float)points.Count);
                b.AddVertex(points[i] - side + Vector3.up * 0.004f, Vector3.up, c);
                b.AddVertex(points[i] + side + Vector3.up * 0.004f, Vector3.up, c);
                if (i > 0) { int v = b.VertexCount - 4; b.AddTriangle(v, v + 2, v + 3); b.AddTriangle(v, v + 3, v + 1); }
            }
            // Start marker: a ring that lights up when releasing would close the loop.
            var start = points[0] + Vector3.up * 0.005f;
            float r = closing ? CloseRingRadius * (1f + 0.08f * Mathf.Sin(Time.time * 8f)) : 0.03f;
            var ring = closing ? new Color(0.55f, 1f, 0.5f, 0.95f) : new Color(1f, 1f, 1f, 0.6f);
            const int seg = 32;
            int first = b.VertexCount;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.AddVertex(start + d * (r - 0.004f), Vector3.up, ring);
                b.AddVertex(start + d * (r + 0.004f), Vector3.up, ring);
                if (i > 0) { int v = first + (i - 1) * 2; b.AddTriangle(v, v + 2, v + 3); b.AddTriangle(v, v + 3, v + 1); }
            }
            b.ToMesh("Track preview", previewMesh);
            previewMesh.RecalculateBounds();
        }

        public const float CloseRingRadius = 0.05f;

        public void HidePreview() => previewMesh.Clear();
    }
}
