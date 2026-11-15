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

        /// <summary>Per track point: inside a tunnel (the hill is kept above it).</summary>
        public bool[] Tunnel { get; private set; }
        public const float TunnelHeight = 0.1f;   // ground this far above the bed clears the tube's roof and hides the 8 cm train

        public void SetLayout(TrackLayout layout, System.Func<Vector2, float> waterLevel = null, TerrainField terrain = null, bool[] tunnel = null)
        {
            Layout = layout;
            Tunnel = tunnel;
            trackMesh.Clear();
            if (layout == null) return;
            var b = new MeshBuilder();
            BuildTrack(b, layout);
            if (waterLevel != null && terrain != null) BuildBridges(b, layout, waterLevel, terrain);
            if (tunnel != null) BuildTunnels(b, layout, tunnel);
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

        /// <summary>
        /// Stone bridges wherever the railway crosses water: a deck carrying the ballast, parapets with coping, piers
        /// standing in the water and round arches between them with filled spandrels.
        /// </summary>
        public static void BuildBridges(MeshBuilder b, TrackLayout layout, System.Func<Vector2, float> waterLevel, TerrainField terrain)
        {
            var pts = layout.Points; int n = pts.Count;
            var wet = new bool[n];
            for (int i = 0; i < n; i++) wet[i] = waterLevel(new Vector2(pts[i].x, pts[i].z)) >= 0f;
            // Spans: runs of wet points, widened by 4 cm onto the banks.
            var spanWet = new bool[n];
            for (int i = 0; i < n; i++)
                if (wet[i]) for (int k = -4; k <= 4; k++) { int j = i + k; if (layout.Closed) j = (j % n + n) % n; if (j >= 0 && j < n) spanWet[j] = true; }
            int start = -1;
            for (int i = 0; i <= n; i++)
            {
                bool w = i < n && spanWet[i];
                if (w && start < 0) start = i;
                if (!w && start >= 0) { Span(b, layout, start, i - 1, terrain); start = -1; }
            }
        }

        private static void Span(MeshBuilder b, TrackLayout layout, int i0, int i1, TerrainField terrain)
        {
            if (i1 - i0 < 2) return;
            var pts = layout.Points;
            var stone = Pal.Stone; var dark = Pal.StoneDark; var coping = Pal.Hex("#C9BBA3");
            var path = new List<Vector3>(); var right = new List<Vector3>(); var up = new List<Vector3>();
            for (int i = i0; i <= i1; i++)
            {
                var a = pts[Mathf.Max(i - 1, 0)]; var c = pts[Mathf.Min(i + 1, pts.Count - 1)];
                var t = c - a; t.y = 0f; t.Normalize();
                path.Add(pts[i]); right.Add(Vector3.Cross(Vector3.up, t)); up.Add(Vector3.up);
            }
            // Deck: a slab hiding the ballast's foot, with parapet walls and a pale coping on both sides.
            var deck = new[] { new Vector2(-0.054f, -0.016f), new Vector2(-0.054f, -0.004f), new Vector2(0.054f, -0.004f), new Vector2(0.054f, -0.016f) };
            b.AddExtrusion(path, right, up, deck, new[] { dark, stone, stone, dark }, false);
            foreach (int side in new[] { -1, 1 })
            {
                float x0 = side * 0.047f, x1 = side * 0.054f;
                var wall = side > 0
                    ? new[] { new Vector2(x0, -0.004f), new Vector2(x0, 0.009f), new Vector2(x1, 0.009f), new Vector2(x1, -0.004f) }
                    : new[] { new Vector2(x1, -0.004f), new Vector2(x1, 0.009f), new Vector2(x0, 0.009f), new Vector2(x0, -0.004f) };
                b.AddExtrusion(path, right, up, wall, new[] { stone, stone, coping, stone }, false);
                // Coursed stonework: a joint line along the parapet and the deck face, and vertical joints.
                for (int i = 0; i < path.Count; i++)
                {
                    var outward = right[i] * side;
                    var face = path[i] + right[i] * side * 0.0546f;
                    b.Push(face + Vector3.up * 0.0025f, Quaternion.LookRotation(Vector3.Cross(outward, Vector3.up), Vector3.up));
                    b.AddBox(Vector3.zero, new Vector3(0.0004f, 0.0005f, TrackPlanner.Spacing * 1.02f), dark);
                    b.Pop();
                    b.Push(face + Vector3.up * -0.01f, Quaternion.LookRotation(Vector3.Cross(outward, Vector3.up), Vector3.up));
                    b.AddBox(Vector3.zero, new Vector3(0.0004f, 0.0005f, TrackPlanner.Spacing * 1.02f), dark);
                    b.Pop();
                    if (i % 2 == 0)
                    {
                        float y = (i / 2) % 2 == 0 ? 0.0055f : -0.0005f;
                        b.Push(face + Vector3.up * y, Quaternion.LookRotation(Vector3.Cross(outward, Vector3.up), Vector3.up));
                        b.AddBox(Vector3.zero, new Vector3(0.0004f, 0.0055f, 0.0005f), dark);
                        b.Pop();
                    }
                }
            }
            // Piers every ~8 cm, arches between them on both faces.
            float length = (i1 - i0) * TrackPlanner.Spacing;
            int arches = Mathf.Max(1, Mathf.RoundToInt(length / 0.08f));
            float step = (i1 - i0) / (float)arches;
            for (int k = 0; k <= arches; k++)
            {
                int i = Mathf.Clamp(Mathf.RoundToInt(i0 + k * step), i0, i1);
                var p = pts[i]; var r = right[i - i0];
                float ground = terrain.SurfaceAt(p.x, p.z) - 0.006f;
                float top = p.y - 0.016f, h = Mathf.Max(0.004f, top - ground);
                b.Push(new Vector3(p.x, ground + h / 2f, p.z), Quaternion.LookRotation(Vector3.Cross(r, Vector3.up), Vector3.up));
                b.AddBox(Vector3.zero, new Vector3(0.112f, h, 0.014f), dark, stone, dark);
                b.AddBox(new Vector3(0f, h / 2f - 0.002f, 0f), new Vector3(0.116f, 0.003f, 0.017f), coping);
                b.Pop();
            }
            for (int k = 0; k < arches; k++)
            {
                int ia = Mathf.Clamp(Mathf.RoundToInt(i0 + k * step), i0, i1), ib = Mathf.Clamp(Mathf.RoundToInt(i0 + (k + 1) * step), i0, i1);
                foreach (int side in new[] { -1, 1 })
                {
                    // Spandrel strip between the deck's underside and a semicircular arch, plus voussoir stones on the arch.
                    const int seg = 8;
                    for (int q = 0; q < seg; q++)
                    {
                        float u0 = q / (float)seg, u1 = (q + 1f) / seg;
                        Vector3 At(float u, out Vector3 rr)
                        {
                            float fi = Mathf.Lerp(ia, ib, u); int lo = Mathf.Clamp(Mathf.FloorToInt(fi), i0, i1), hi = Mathf.Min(lo + 1, i1);
                            var p = Vector3.Lerp(pts[lo], pts[hi], fi - lo); rr = right[lo - i0];
                            return p + rr * side * 0.0545f;
                        }
                        var top0 = At(u0, out var r0) + Vector3.up * -0.016f; var top1 = At(u1, out _) + Vector3.up * -0.016f;
                        float span = (ib - ia) * TrackPlanner.Spacing * 0.5f;
                        float g0 = Mathf.Sin(u0 * Mathf.PI) * span * 0.9f, g1 = Mathf.Sin(u1 * Mathf.PI) * span * 0.9f;
                        float springing = Mathf.Min(0.03f, span * 0.6f);
                        var bot0 = top0 - Vector3.up * (springing + 0.004f - g0 * 0.55f);
                        var bot1 = top1 - Vector3.up * (springing + 0.004f - g1 * 0.55f);
                        bot0.y = Mathf.Min(bot0.y, top0.y - 0.004f); bot1.y = Mathf.Min(bot1.y, top1.y - 0.004f);
                        var outward = r0 * side;
                        b.AddQuad(top0, top1, bot1, bot0, stone);
                        b.AddQuad(top1, top0, bot0, bot1, stone);
                        b.Push((bot0 + bot1) * 0.5f + outward * 0.0006f, Quaternion.LookRotation((bot1 - bot0).normalized, outward));
                        b.AddBox(Vector3.zero, new Vector3(0.004f, 0.0012f, (bot1 - bot0).magnitude * 0.92f), q % 2 == 0 ? coping : stone);
                        b.Pop();
                    }
                }
            }
        }

        /// <summary>
        /// Where the railway runs so deep under a hill that the ground above would hide the train, it becomes a tunnel:
        /// runs of at least 8 cm with the surface more than <see cref="TunnelHeight"/> above the bed (small gaps filled).
        /// </summary>
        public static bool[] TunnelMask(TerrainField terrain, TrackLayout layout)
        {
            var pts = layout.Points; int n = pts.Count;
            var mask = new bool[n];
            bool any = false;
            for (int i = 0; i < n; i++) { mask[i] = terrain.SurfaceAt(pts[i].x, pts[i].z) > pts[i].y + TunnelHeight; any |= mask[i]; }
            if (!any) return null;
            // Fill gaps under 3 cm, then drop runs under 8 cm.
            Runs(mask, layout.Closed, (s, e, on) => { if (!on && e - s + 1 < 3) for (int i = s; i <= e; i++) mask[i % n] = true; });
            Runs(mask, layout.Closed, (s, e, on) => { if (on && e - s + 1 < 8) for (int i = s; i <= e; i++) mask[i % n] = false; });
            foreach (bool m in mask) if (m) return mask;
            return null;
        }

        /// <summary>Inside the tunnel by at least <see cref="PortalInset"/> points on both sides (so each mouth gets a cutting).</summary>
        public static bool DeepInTunnel(bool[] tunnel, int i, bool closed)
        {
            int n = tunnel.Length;
            for (int k = -PortalInset; k <= PortalInset; k += PortalInset)
            {
                int j = i + k;
                if (closed) j = (j % n + n) % n; else if (j < 0 || j >= n) return false;
                if (!tunnel[j]) return false;
            }
            return true;
        }

        public const int PortalInset = 4;   // 4 cm of cutting before each portal

        /// <summary>Calls <paramref name="visit"/>(start, end, value) for each run of equal values (end may exceed n on a loop).</summary>
        private static void Runs(bool[] m, bool closed, System.Action<int, int, bool> visit)
        {
            int n = m.Length, start = 0;
            if (closed) { int k = 0; while (k < n && m[k] == m[0]) k++; if (k == n) { visit(0, n - 1, m[0]); return; } start = k; }
            int i = start, count = 0;
            while (count < n)
            {
                int s = i; bool v = m[i % n];
                while (count < n && m[i % n] == v) { i++; count++; }
                if (!closed && s == 0 && v == false) { visit(s, i - 1, v); continue; }
                visit(s, i - 1, v);
            }
        }

        /// <summary>Stone portals at each tunnel mouth and a dark arched tube inside, so nobody sees through the hill.</summary>
        public static void BuildTunnels(MeshBuilder b, TrackLayout layout, bool[] tunnel)
        {
            var pts = layout.Points; int n = pts.Count;
            var dark = new Color(0.07f, 0.06f, 0.06f); var stone = Pal.Stone; var stoneDark = Pal.StoneDark;
            Runs(tunnel, layout.Closed, (s0, e0, on) =>
            {
                if (!on || e0 - s0 < PortalInset * 2 + 2) return;
                int s = s0 + 1, e = e0 - 1;   // portals stand in front of the step where the kept hill begins (PortalInset further in)
                // The tube: an arched cross-section facing inwards, along the run (plus a little beyond each mouth).
                var path = new List<Vector3>(); var right = new List<Vector3>(); var up = new List<Vector3>();
                for (int i = s - 2; i <= e + 2; i++)
                {
                    int j = layout.Closed ? ((i % n) + n) % n : Mathf.Clamp(i, 0, n - 1);
                    var t = pts[layout.Closed ? (j + 1) % n : Mathf.Min(j + 1, n - 1)] - pts[layout.Closed ? (j - 1 + n) % n : Mathf.Max(j - 1, 0)]; t.y = 0f; t.Normalize();
                    path.Add(pts[j]); right.Add(Vector3.Cross(Vector3.up, t)); up.Add(Vector3.up);
                }
                const int arc = 8; const float w = 0.042f, wall = 0.05f;
                var profile = new Vector2[arc + 3]; var colours = new Color[arc + 3];
                // Inward-facing: order counter-clockwise (right wall up, over the top, left wall down).
                profile[0] = new Vector2(w, -0.012f);
                for (int k = 0; k <= arc; k++) { float a = k / (float)arc * Mathf.PI; profile[k + 1] = new Vector2(Mathf.Cos(a) * w, wall + Mathf.Sin(a) * w * 0.9f); }
                profile[arc + 2] = new Vector2(-w, -0.012f);
                for (int k = 0; k < colours.Length; k++) colours[k] = dark;
                b.AddExtrusion(path, right, up, profile, colours, false);
                foreach (int mouth in new[] { s, e })
                {
                    int j = layout.Closed ? ((mouth % n) + n) % n : Mathf.Clamp(mouth, 0, n - 1);
                    int dir = mouth == s ? -1 : 1;
                    var t = pts[layout.Closed ? (j + 1) % n : Mathf.Min(j + 1, n - 1)] - pts[layout.Closed ? (j - 1 + n) % n : Mathf.Max(j - 1, 0)]; t.y = 0f; t.Normalize();
                    var outward = t * dir;
                    b.Push(pts[j] + outward * 0.004f, Quaternion.LookRotation(outward, Vector3.up));
                    // The opening: an opaque dark arch just inside the portal (the toy-railway trick: the train disappears
                    // into it, and the hill's cut face behind it is never seen).
                    {
                        var c = new Vector3(0f, wall, -0.003f);
                        var bl = new Vector3(-w, -0.012f, -0.003f); var br = new Vector3(w, -0.012f, -0.003f);
                        var tl = new Vector3(-w, wall, -0.003f); var tr = new Vector3(w, wall, -0.003f);
                        b.AddQuad(br, tr, tl, bl, dark);   // faces outwards (+Z)
                        const int seg = 12;
                        for (int k = 0; k < seg; k++)
                        {
                            float a0 = k / (float)seg * Mathf.PI, a1 = (k + 1) / (float)seg * Mathf.PI;
                            b.AddTriangleOutward(c, c + new Vector3(Mathf.Cos(a0) * w, Mathf.Sin(a0) * w * 0.9f, 0f), c + new Vector3(Mathf.Cos(a1) * w, Mathf.Sin(a1) * w * 0.9f, 0f), c - Vector3.forward * 0.01f, dark);
                        }
                    }
                    // Pillars, a semicircle of voussoirs with a keystone, and a parapet band above.
                    foreach (int side in new[] { -1, 1 })
                        b.AddBox(new Vector3(side * 0.054f, wall / 2f - 0.006f, 0f), new Vector3(0.024f, wall + 0.012f, 0.012f), stoneDark, stone, stoneDark);
                    const int stones = 9;
                    for (int k = 0; k < stones; k++)
                    {
                        float a = (k + 0.5f) / stones * Mathf.PI;
                        var c = new Vector3(Mathf.Cos(a) * (w + 0.007f), wall + Mathf.Sin(a) * (w * 0.9f + 0.007f), 0f);
                        b.Push(c, Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f));
                        bool key = k == stones / 2;
                        b.AddBox(Vector3.zero, new Vector3(key ? 0.014f : 0.0105f, key ? 0.018f : 0.014f, 0.013f), key ? Pal.Hex("#C9BBA3") : (k % 2 == 0 ? stone : stoneDark));
                        b.Pop();
                    }
                    b.AddBox(new Vector3(0f, wall + w * 0.9f + 0.022f, 0f), new Vector3(0.14f, 0.012f, 0.014f), stone, Pal.Hex("#C9BBA3"), stone);
                    // Spandrel fill between the arch and the band, and the wall face either side.
                    foreach (int side in new[] { -1, 1 })
                    {
                        b.AddBox(new Vector3(side * 0.055f, wall + 0.03f, -0.001f), new Vector3(0.03f, 0.05f, 0.01f), stone, stone, stone);
                        b.AddBox(new Vector3(side * 0.03f, wall + w * 0.9f + 0.006f, -0.001f), new Vector3(0.03f, 0.02f, 0.01f), stone, stone, stone);
                    }
                    b.Pop();
                }
            });
        }

        /// <summary>Grades the corridor to the track bed and locks it; returns the touched region. Water is left alone.</summary>
        public static RectInt GradeTerrain(TerrainField terrain, TrackLayout layout, System.Func<Vector2, float> waterLevel = null, bool[] tunnel = null)
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
                if (waterLevel != null && waterLevel(new Vector2(px, pz)) >= 0f) continue;   // the bridge spans it
                if (tunnel != null && DeepInTunnel(tunnel, bestIndex, layout.Closed) && terrain.Surface[z * terrain.Nx + x] > layout.Points[bestIndex].y + TunnelHeight)
                    continue;   // the hill stays over the tunnel (a short cutting is dug up to each portal)
                int g = z * terrain.Nx + x;
                // Under a tunnel (and in the cutting up to its portals) the floor is flat right out to the tube walls, so no
                // bank shows inside the opening.
                float flat = tunnel != null && tunnel[bestIndex] ? 0.056f : BedHalfWidth * 0.8f;
                float w = dist < flat ? 1f : Mathf.SmoothStep(1f, 0f, (dist - flat) / (CorridorHalfWidth - flat));
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
