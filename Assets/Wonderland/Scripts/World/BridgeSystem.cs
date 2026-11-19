using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    public enum BridgeStyle { Stone = 0, Timber = 1, Truss = 2, Suspension = 3, Covered = 4, Footbridge = 5, Stones = 6 }

    /// <summary>A bridge style chosen by hand, remembered at the place it was chosen (bridges themselves are derived).</summary>
    [Serializable]
    public struct BridgeChoice
    {
        public float x, z;
        public int style;
    }

    /// <summary>
    /// Bridges, everywhere a railway, road or path crosses a pond or a stream. They are not drawn separately: they follow
    /// from what crosses the water, so they appear, widen and disappear as the network changes. Parallel crossings close
    /// together share one bridge (draw a second track beside the first and the bridge widens to carry both; a road beside a
    /// railway gets a combined bridge). Each bridge gets a style that suits it - stone arches for railways and roads, a
    /// timber trestle for lanes, a little footbridge for paths, stepping stones where a trail meets a narrow stream - and a
    /// tap cycles through the other styles that fit its span: steel truss, suspension (long spans), covered (short ones).
    /// Hand-picked styles are remembered (saved, undoable) by place.
    /// </summary>
    public sealed class BridgeSystem : MonoBehaviour
    {
        public static readonly string[] Names = { "Stone arch bridge", "Timber trestle", "Steel truss bridge", "Suspension bridge", "Covered bridge", "Footbridge", "Stepping stones" };
        public const int Bank = 4;   // points of approach on each bank (1 cm apart)

        public readonly List<BridgeChoice> Choices = new();
        public readonly List<Bridge> Bridges = new();
        private TerrainField terrain;
        private Func<Vector2, float> waterLevel;
        private Mesh mesh;

        public sealed class Member
        {
            public bool Track;
            public int Index, Kind;               // track piece or path line; path kind (-1 for track)
            public List<Vector3> Points = new();  // x, deck (or bed) height, z
            public bool[] Wet;
            public float HalfWidth;
            public Rect Bounds;
        }

        public sealed class Bridge
        {
            public readonly List<Member> Members = new();
            public int Spine;
            public BridgeStyle Style;
            public Vector2 Anchor;
            public float Length;
            public bool Rail, Road;
            public bool Foot => !Rail && !Road;
        }

        public void Init(TerrainField field, Material lit, Func<Vector2, float> level)
        {
            terrain = field; waterLevel = level;
            var go = new GameObject("Bridges");
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Bridges", indexFormat = IndexFormat.UInt32 };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = lit; r.shadowCastingMode = ShadowCastingMode.On;
        }

        // ------------------------------------------------------------------ finding the crossings

        /// <summary>Finds every crossing, groups them into bridges, picks styles and rebuilds the mesh.</summary>
        public void Rebuild(TrackNetwork network, IReadOnlyList<PathLine> paths)
        {
            Bridges.Clear();
            var runs = new List<Member>();
            if (network != null)
                for (int e = 0; e < network.Edges.Count; e++)
                {
                    var l = network.Edges[e].Layout;
                    var pts = l.Points;
                    if (l.Closed)
                    {
                        // A loop: start the walk on dry ground, so a crossing is never split at the seam.
                        int n = pts.Count, dry = 0;
                        for (int i = 0; i < n; i++)
                        {
                            bool ok = true;
                            for (int k = -Bank - 1; k <= Bank + 1 && ok; k++) { var q = pts[(i + k + n) % n]; ok = waterLevel(new Vector2(q.x, q.z)) < 0f; }
                            if (ok) { dry = i; break; }
                        }
                        var rotated = new List<Vector3>(n);
                        for (int k = 0; k < n; k++) rotated.Add(pts[(dry + k) % n]);
                        pts = rotated;
                    }
                    var list = pts;
                    foreach (var (i0, i1, wet) in WetRuns(list.Count, i => { var p = list[i]; return waterLevel(new Vector2(p.x, p.z)); }))
                        runs.Add(new Member { Track = true, Index = e, Kind = -1, HalfWidth = 0.054f, Points = list.GetRange(i0, i1 - i0 + 1), Wet = wet });
                }
            if (paths != null)
                for (int li = 0; li < paths.Count; li++)
                {
                    var line = paths[li];
                    if (!PathSystem.IsWay(line.kind)) continue;
                    foreach (var (i0, i1, wet) in WetRuns(line.points.Count, i => waterLevel(line.points[i])))
                    {
                        var m = new Member { Track = false, Index = li, Kind = line.kind, HalfWidth = PathSystem.HalfWidth(line.kind) + 0.008f, Wet = wet };
                        // Deck: clear of the highest water under it, ramping up from the banks.
                        float level = 0f;
                        for (int i = i0; i <= i1; i++) level = Mathf.Max(level, waterLevel(line.points[i]));
                        float deck = level + (PathSystem.IsCarWay(line.kind) ? 0.026f : 0.02f);
                        int n = i1 - i0 + 1;
                        for (int k = 0; k < n; k++)
                        {
                            var p = line.points[i0 + k];
                            float t = Mathf.Clamp01(Mathf.Min(k, n - 1 - k) / (float)Bank);
                            float ground = terrain.SurfaceAt(p.x, p.y);
                            m.Points.Add(new Vector3(p.x, Mathf.Lerp(ground, Mathf.Max(deck, ground), Mathf.SmoothStep(0f, 1f, t)), p.y));
                        }
                        runs.Add(m);
                    }
                }
            foreach (var m in runs)
            {
                float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                foreach (var p in m.Points) { x0 = Mathf.Min(x0, p.x); z0 = Mathf.Min(z0, p.z); x1 = Mathf.Max(x1, p.x); z1 = Mathf.Max(z1, p.z); }
                m.Bounds = Rect.MinMaxRect(x0 - m.HalfWidth - 0.02f, z0 - m.HalfWidth - 0.02f, x1 + m.HalfWidth + 0.02f, z1 + m.HalfWidth + 0.02f);
            }
            // Crossings close together and side by side share a bridge.
            var parent = new int[runs.Count];
            for (int i = 0; i < runs.Count; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int a = 0; a < runs.Count; a++)
            for (int b = a + 1; b < runs.Count; b++)
                if (SideBySide(runs[a], runs[b])) parent[Find(a)] = Find(b);
            var groups = new Dictionary<int, Bridge>();
            for (int i = 0; i < runs.Count; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var br)) groups[root] = br = new Bridge();
                br.Members.Add(runs[i]);
            }
            foreach (var br in groups.Values)
            {
                br.Spine = 0;
                for (int i = 1; i < br.Members.Count; i++)
                {
                    var m = br.Members[i]; var s = br.Members[br.Spine];
                    if ((m.Track && !s.Track) || (m.Track == s.Track && m.Points.Count > s.Points.Count)) br.Spine = i;
                }
                foreach (var m in br.Members) { br.Rail |= m.Track; br.Road |= !m.Track && PathSystem.IsCarWay(m.Kind); }
                var spine = br.Members[br.Spine];
                var sum = Vector2.zero; int count = 0;
                for (int i = 0; i < spine.Points.Count; i++) if (spine.Wet[i]) { sum += new Vector2(spine.Points[i].x, spine.Points[i].z); count++; }
                br.Anchor = count > 0 ? sum / count : new Vector2(spine.Points[0].x, spine.Points[0].z);
                br.Length = (spine.Points.Count - 1) * 0.01f;
                br.Style = DefaultStyle(br);
                foreach (var c in Choices)
                    if ((new Vector2(c.x, c.z) - br.Anchor).magnitude < 0.12f && Allowed(br).Contains((BridgeStyle)c.style)) br.Style = (BridgeStyle)c.style;
                Bridges.Add(br);
            }
            BuildMesh();
        }

        private static IEnumerable<(int i0, int i1, bool[] wet)> WetRuns(int n, Func<int, float> level)
        {
            var wet = new bool[n];
            for (int i = 0; i < n; i++) wet[i] = level(i) >= 0f;
            var span = new bool[n];
            for (int i = 0; i < n; i++)
                if (wet[i]) for (int k = -Bank; k <= Bank; k++) { int j = i + k; if (j >= 0 && j < n) span[j] = true; }
            int start = -1;
            for (int i = 0; i <= n; i++)
            {
                bool w = i < n && span[i];
                if (w && start < 0) start = i;
                if (!w && start >= 0)
                {
                    if (i - 1 - start >= 3)
                    {
                        var part = new bool[i - start];
                        Array.Copy(wet, start, part, 0, part.Length);
                        yield return (start, i - 1, part);
                    }
                    start = -1;
                }
            }
        }

        private static bool SideBySide(Member a, Member b)
        {
            // Close (centre lines within 15 cm somewhere over the water) and roughly parallel.
            Vector2 Dir(Member m) { var d = m.Points[^1] - m.Points[0]; return new Vector2(d.x, d.z).normalized; }
            float angle = Vector2.Angle(Dir(a), Dir(b)); angle = Mathf.Min(angle, 180f - angle);
            if (angle > 30f) return false;
            float best = float.MaxValue;
            for (int i = 0; i < a.Points.Count; i += 3)
                for (int j = 0; j < b.Points.Count; j += 3)
                    best = Mathf.Min(best, new Vector2(a.Points[i].x - b.Points[j].x, a.Points[i].z - b.Points[j].z).sqrMagnitude);
            return best < 0.15f * 0.15f;
        }

        // ------------------------------------------------------------------ styles

        /// <summary>The styles that suit a bridge: what it carries and how long it is.</summary>
        public static List<BridgeStyle> Allowed(Bridge b)
        {
            var list = new List<BridgeStyle>();
            if (b.Foot)
            {
                list.Add(BridgeStyle.Footbridge);
                if (b.Length <= 0.2f) list.Add(BridgeStyle.Stones);
                list.Add(BridgeStyle.Stone);
                list.Add(BridgeStyle.Timber);
                if (b.Length >= 0.16f) list.Add(BridgeStyle.Suspension);
                list.Add(BridgeStyle.Covered);
                return list;
            }
            list.Add(BridgeStyle.Stone);
            list.Add(BridgeStyle.Timber);
            list.Add(BridgeStyle.Truss);
            if (b.Length >= 0.26f) list.Add(BridgeStyle.Suspension);
            if (b.Length <= 0.5f && b.Members.Count <= 2) list.Add(BridgeStyle.Covered);
            return list;
        }

        private static BridgeStyle DefaultStyle(Bridge b)
        {
            if (b.Rail) return b.Length > 0.6f ? BridgeStyle.Truss : BridgeStyle.Stone;
            if (b.Road) return b.Members.Exists(m => PathSystem.IsRoad(m.Kind)) ? BridgeStyle.Stone : BridgeStyle.Timber;
            bool trail = b.Members.TrueForAll(m => m.Kind == (int)PathKind.Trail);
            return trail && b.Length <= 0.2f ? BridgeStyle.Stones : BridgeStyle.Footbridge;
        }

        /// <summary>The bridge under (or very near) a local point, or -1.</summary>
        public int Pick(Vector2 p)
        {
            for (int bi = 0; bi < Bridges.Count; bi++)
                foreach (var m in Bridges[bi].Members)
                    for (int i = 0; m.Bounds.Contains(p) && i < m.Points.Count; i++)
                        if (new Vector2(m.Points[i].x - p.x, m.Points[i].z - p.y).magnitude < m.HalfWidth + 0.02f) return bi;
            return -1;
        }

        /// <summary>Changes a bridge to the next style that suits it and remembers the choice. Returns the new style.</summary>
        public BridgeStyle Cycle(int bridge)
        {
            var b = Bridges[bridge];
            var allowed = Allowed(b);
            var next = allowed[(allowed.IndexOf(b.Style) + 1) % allowed.Count];
            Choices.RemoveAll(c => (new Vector2(c.x, c.z) - b.Anchor).magnitude < 0.12f);
            Choices.Add(new BridgeChoice { x = b.Anchor.x, z = b.Anchor.y, style = (int)next });
            return next;
        }

        // ------------------------------------------------------------------ decks for paths and walkers

        /// <summary>
        /// Whether a local point is on the deck of a road or path bridge: its height there, and whether the path's own
        /// surface should give way to the bridge's (footbridges and stepping stones draw their own).
        /// </summary>
        /// <summary>A key for the road and path decks overlapping an area: it changes when any of them does.</summary>
        public int KeyNear(Rect area)
        {
            var h = new System.HashCode();
            foreach (var b in Bridges)
                foreach (var m in b.Members)
                {
                    if (m.Track || !m.Bounds.Overlaps(area)) continue;
                    h.Add(b.Style); h.Add(b.Length); h.Add(m.Bounds);
                    foreach (var q in m.Points) h.Add(q);
                    foreach (bool w in m.Wet) h.Add(w);
                }
            return h.ToHashCode();
        }

        public bool Deck(Vector2 p, out float height, out bool hide, float margin = 0f)
        {
            height = 0f; hide = false;
            foreach (var b in Bridges)
                foreach (var m in b.Members)
                {
                    if (m.Track || !m.Bounds.Contains(p)) continue;
                    int best = -1; float bestD = (m.HalfWidth - 0.006f + margin) * (m.HalfWidth - 0.006f + margin);
                    for (int i = 0; i < m.Points.Count; i++)
                    {
                        float d = new Vector2(m.Points[i].x - p.x, m.Points[i].z - p.y).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    if (best < 0) continue;
                    height = m.Points[best].y;
                    float t = best / (float)Mathf.Max(1, m.Points.Count - 1);
                    if (b.Style == BridgeStyle.Footbridge) height += Mathf.Sin(t * Mathf.PI) * Mathf.Min(0.012f, b.Length * 0.05f);
                    if (b.Style == BridgeStyle.Stones)
                    {
                        float lvl = waterLevel(p);
                        if (lvl >= 0f) height = lvl + 0.003f;
                    }
                    hide = (b.Style == BridgeStyle.Footbridge || b.Style == BridgeStyle.Stones) && m.Wet[best];
                    return true;
                }
            return false;
        }

        // ------------------------------------------------------------------ meshes

        private void BuildMesh()
        {
            var b = new MeshBuilder();
            foreach (var br in Bridges) Build(b, br);
            b.ToMesh("Bridges", mesh);
            mesh.RecalculateBounds();
        }

        private void Build(MeshBuilder b, Bridge br)
        {
            var spine = br.Members[br.Spine];
            int n = spine.Points.Count;
            // The deck follows the spine; it is widened (and shifted) to carry every member.
            var right = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                var t = spine.Points[Mathf.Min(i + 1, n - 1)] - spine.Points[Mathf.Max(i - 1, 0)]; t.y = 0f;
                right.Add(Vector3.Cross(Vector3.up, t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward));
            }
            float lo = -spine.HalfWidth, hi = spine.HalfWidth;
            foreach (var m in br.Members)
            {
                if (m == spine) continue;
                foreach (var p in m.Points)
                {
                    int k = NearestIndex(spine.Points, p);
                    float off = Vector3.Dot(new Vector3(p.x - spine.Points[k].x, 0f, p.z - spine.Points[k].z), right[k]);
                    lo = Mathf.Min(lo, off - m.HalfWidth); hi = Mathf.Max(hi, off + m.HalfWidth);
                }
            }
            float centre = (lo + hi) * 0.5f, half = (hi - lo) * 0.5f;
            var path = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                float top = spine.Points[i].y - (spine.Track ? 0.004f : 0.0005f);
                // A shared deck sits level with its highest member.
                foreach (var m in br.Members) if (m != spine) top = Mathf.Max(top, m.Points[NearestIndex(m.Points, spine.Points[i])].y - (m.Track ? 0.004f : 0.0005f));
                var q = spine.Points[i] + right[i] * centre; q.y = top;
                path.Add(q);
            }
            var rng = new Rng(Mathf.RoundToInt(br.Anchor.x * 1000f) * 31 + Mathf.RoundToInt(br.Anchor.y * 1000f));
            switch (br.Style)
            {
                case BridgeStyle.Stone: Stone(b, path, right, half, br.Rail); break;
                case BridgeStyle.Timber: Timber(b, path, right, half, br.Rail); break;
                case BridgeStyle.Truss: Truss(b, path, right, half, br.Rail); break;
                case BridgeStyle.Suspension: Suspension(b, path, right, half, br.Rail || br.Road); break;
                case BridgeStyle.Covered: Covered(b, path, right, half, br.Rail); break;
                case BridgeStyle.Footbridge: Footbridge(b, path, right, half, br.Length); break;
                default: Stones(b, spine, ref rng); break;
            }
        }

        private static int NearestIndex(List<Vector3> pts, Vector3 p)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < pts.Count; i++) { float d = (pts[i].x - p.x) * (pts[i].x - p.x) + (pts[i].z - p.z) * (pts[i].z - p.z); if (d < bestD) { bestD = d; best = i; } }
            return best;
        }

        private float Ground(Vector3 p) => terrain.SurfaceAt(p.x, p.z) - 0.006f;

        private static void Slab(MeshBuilder b, List<Vector3> path, List<Vector3> right, float half, float thickness, Color top, Color side)
        {
            var profile = new[] { new Vector2(-half, -thickness), new Vector2(-half, 0f), new Vector2(half, 0f), new Vector2(half, -thickness) };
            b.AddExtrusion(path, right, Up(path.Count), profile, new[] { side, top, top, side }, false);
            // The underside.
            var under = new[] { new Vector2(half, -thickness), new Vector2(-half, -thickness) };
            b.AddExtrusion(path, right, Up(path.Count), under, new[] { Pal.Shade(side, 0.6f), Pal.Shade(side, 0.6f) }, false);
        }

        private static List<Vector3> Up(int n) { var l = new List<Vector3>(n); for (int i = 0; i < n; i++) l.Add(Vector3.up); return l; }

        private static void Rail(MeshBuilder b, List<Vector3> path, List<Vector3> right, float offset, float height, Color post, Color rail, float spacing = 3, float postSize = 0.0018f)
        {
            for (int i = 0; i < path.Count; i++)
            {
                var p = path[i] + right[i] * offset;
                if (i % Mathf.Max(1, (int)spacing) == 0) b.AddBox(p + Vector3.up * height * 0.5f, new Vector3(postSize, height, postSize), post);
                if (i > 0)
                {
                    var a = path[i - 1] + right[i - 1] * offset + Vector3.up * height; var c = p + Vector3.up * height;
                    b.Push((a + c) * 0.5f, Quaternion.LookRotation((c - a).normalized, Vector3.up));
                    b.AddBox(Vector3.zero, new Vector3(postSize * 0.8f, postSize * 0.8f, (c - a).magnitude * 1.05f), rail);
                    b.Pop();
                }
            }
        }

        /// <summary>Stone arches between piers, with parapets and coursed stonework (the classic viaduct).</summary>
        private void Stone(MeshBuilder b, List<Vector3> path, List<Vector3> right, float half, bool rail)
        {
            var stone = Pal.Stone; var dark = Pal.StoneDark; var coping = Pal.Hex("#C9BBA3");
            int n = path.Count;
            Slab(b, path, right, half, 0.012f, rail ? dark : Pal.Hex("#B5AA98"), stone);
            foreach (int side in new[] { -1, 1 })
            {
                float x0 = side * (half - 0.007f), x1 = side * half;
                var wall = side > 0
                    ? new[] { new Vector2(x0, 0f), new Vector2(x0, 0.012f), new Vector2(x1, 0.012f), new Vector2(x1, 0f) }
                    : new[] { new Vector2(x1, 0f), new Vector2(x1, 0.012f), new Vector2(x0, 0.012f), new Vector2(x0, 0f) };
                b.AddExtrusion(path, right, Up(n), wall, new[] { stone, stone, coping, stone }, false);
                for (int i = 0; i < n; i += 2)
                {
                    var face = path[i] + right[i] * side * (half + 0.0006f);
                    b.Push(face + Vector3.up * (i / 2 % 2 == 0 ? 0.008f : 0.002f), Quaternion.LookRotation(Vector3.Cross(right[i] * side, Vector3.up), Vector3.up));
                    b.AddBox(Vector3.zero, new Vector3(0.0004f, 0.005f, 0.0005f), dark);
                    b.Pop();
                }
            }
            float length = (n - 1) * 0.01f;
            int arches = Mathf.Max(1, Mathf.RoundToInt(length / 0.08f));
            float step = (n - 1) / (float)arches;
            for (int k = 0; k <= arches; k++)
            {
                int i = Mathf.Clamp(Mathf.RoundToInt(k * step), 0, n - 1);
                var p = path[i];
                float ground = Ground(p), top = p.y - 0.012f, h = Mathf.Max(0.004f, top - ground);
                b.Push(new Vector3(p.x, ground + h / 2f, p.z), Quaternion.LookRotation(Vector3.Cross(right[i], Vector3.up), Vector3.up));
                b.AddBox(Vector3.zero, new Vector3(half * 2f + 0.004f, h, 0.014f), dark, stone, dark);
                b.AddBox(new Vector3(0f, h / 2f - 0.002f, 0f), new Vector3(half * 2f + 0.008f, 0.003f, 0.017f), coping);
                b.Pop();
            }
            for (int k = 0; k < arches; k++)
            {
                int ia = Mathf.RoundToInt(k * step), ib = Mathf.Min(n - 1, Mathf.RoundToInt((k + 1) * step));
                foreach (int side in new[] { -1, 1 })
                {
                    const int seg = 8;
                    float span = (ib - ia) * 0.01f * 0.5f;
                    for (int q = 0; q < seg; q++)
                    {
                        float u0 = q / (float)seg, u1 = (q + 1f) / seg;
                        Vector3 At(float u, out Vector3 rr)
                        {
                            float fi = Mathf.Lerp(ia, ib, u); int lo = Mathf.Clamp(Mathf.FloorToInt(fi), 0, n - 1), hi = Mathf.Min(lo + 1, n - 1);
                            rr = right[lo];
                            return Vector3.Lerp(path[lo], path[hi], fi - lo) + rr * side * (half + 0.0005f);
                        }
                        var top0 = At(u0, out var r0) + Vector3.up * -0.012f; var top1 = At(u1, out _) + Vector3.up * -0.012f;
                        float springing = Mathf.Min(0.03f, span * 0.6f);
                        var bot0 = top0 - Vector3.up * (springing + 0.004f - Mathf.Sin(u0 * Mathf.PI) * span * 0.5f);
                        var bot1 = top1 - Vector3.up * (springing + 0.004f - Mathf.Sin(u1 * Mathf.PI) * span * 0.5f);
                        bot0.y = Mathf.Min(bot0.y, top0.y - 0.004f); bot1.y = Mathf.Min(bot1.y, top1.y - 0.004f);
                        b.AddQuad(top0, top1, bot1, bot0, stone);
                        b.AddQuad(top1, top0, bot0, bot1, stone);
                        b.Push((bot0 + bot1) * 0.5f + r0 * side * 0.0006f, Quaternion.LookRotation((bot1 - bot0).normalized, r0 * side));
                        b.AddBox(Vector3.zero, new Vector3(0.004f, 0.0012f, (bot1 - bot0).magnitude * 0.92f), q % 2 == 0 ? coping : stone);
                        b.Pop();
                    }
                }
            }
        }

        /// <summary>A timber trestle: plank deck on stringers, railings, and X-braced bents down to the bed.</summary>
        private void Timber(MeshBuilder b, List<Vector3> path, List<Vector3> right, float half, bool rail)
        {
            var wood = Pal.Hex("#8A5A36"); var light = Pal.Hex("#A8743E"); var dark = Pal.Hex("#5E3A22");
            int n = path.Count;
            Slab(b, path, right, half, 0.004f, light, dark);
            for (int i = 0; i < n; i += 1)
            {
                b.Push(path[i] + Vector3.up * 0.0004f, Quaternion.LookRotation(Vector3.Cross(right[i], Vector3.up), Vector3.up));
                b.AddBox(Vector3.zero, new Vector3(half * 2f, 0.0008f, 0.0006f), dark);
                b.Pop();
            }
            foreach (int side in new[] { -1, 1 }) Rail(b, path, right, side * (half - 0.002f), rail ? 0.012f : 0.011f, wood, light);
            for (int i = 2; i < n - 1; i += 6)
            {
                var p = path[i];
                float ground = Ground(p), h = Mathf.Max(0.004f, p.y - 0.004f - ground);
                b.Push(new Vector3(p.x, ground, p.z), Quaternion.LookRotation(Vector3.Cross(right[i], Vector3.up), Vector3.up));
                foreach (int side in new[] { -1, 1 })
                {
                    b.Push(new Vector3(side * half * 0.75f, 0f, 0f), Quaternion.Euler(0f, 0f, side * 6f));
                    b.AddBox(new Vector3(0f, h / 2f, 0f), new Vector3(0.0028f, h, 0.0028f), wood);
                    b.Pop();
                }
                float diag = Mathf.Sqrt(half * 1.5f * half * 1.5f + h * h);
                foreach (int s in new[] { -1, 1 })
                {
                    b.Push(new Vector3(0f, h / 2f, 0f), Quaternion.Euler(0f, 0f, s * Mathf.Atan2(h, half * 1.5f) * Mathf.Rad2Deg));
                    b.AddBox(Vector3.zero, new Vector3(diag, 0.0014f, 0.0014f), dark);
                    b.Pop();
                }
                b.AddBox(new Vector3(0f, h - 0.002f, 0f), new Vector3(half * 2f, 0.003f, 0.004f), wood);
                b.Pop();
            }
        }

        /// <summary>A steel truss: lattice girders on both sides of the deck, portal bracing overhead for trains, stone abutments.</summary>
        private void Truss(MeshBuilder b, List<Vector3> path, List<Vector3> right, float half, bool rail)
        {
            var steel = Pal.Hex("#4E7A5A"); var steelDark = Pal.Shade(steel, 0.75f);
            int n = path.Count;
            float h = rail ? 0.07f : 0.04f;
            Slab(b, path, right, half, 0.006f, Pal.Hex("#7A7A80"), steelDark);
            int panel = 5;
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 Low(int i) => path[i] + right[i] * side * half;
                Vector3 High(int i) => Low(i) + Vector3.up * h * Mathf.Clamp01(Mathf.Min(i, n - 1 - i) / 5f + 0.35f);
                for (int i = 0; i + panel <= n - 1; i += panel)
                {
                    int j = i + panel;
                    Beam(b, Low(i), Low(j), 0.0024f, steel);
                    Beam(b, High(i), High(j), 0.0024f, steel);
                    Beam(b, Low(i), High(i), 0.0018f, steelDark);
                    Beam(b, (i / panel) % 2 == 0 ? Low(i) : High(i), (i / panel) % 2 == 0 ? High(j) : Low(j), 0.0016f, steel);
                }
                Beam(b, Low(n - 1), High(n - 1), 0.0018f, steelDark);
            }
            if (rail)
                for (int i = 0; i < n; i += panel * 2)
                    Beam(b, path[i] + right[i] * -half + Vector3.up * h * Mathf.Clamp01(Mathf.Min(i, n - 1 - i) / 5f + 0.35f), path[i] + right[i] * half + Vector3.up * h * Mathf.Clamp01(Mathf.Min(i, n - 1 - i) / 5f + 0.35f), 0.0018f, steelDark);
            foreach (int end in new[] { 0, n - 1 })
            {
                var p = path[end];
                float ground = Ground(p), hh = Mathf.Max(0.004f, p.y - 0.006f - ground);
                b.Push(new Vector3(p.x, ground + hh / 2f, p.z), Quaternion.LookRotation(Vector3.Cross(right[end], Vector3.up), Vector3.up));
                b.AddBox(Vector3.zero, new Vector3(half * 2f + 0.006f, hh, 0.016f), Pal.StoneDark, Pal.Stone, Pal.StoneDark);
                b.Pop();
            }
            // Long trusses rest on a pier in the middle.
            if (n > 40)
            {
                var p = path[n / 2];
                float ground = Ground(p), hh = Mathf.Max(0.004f, p.y - 0.006f - ground);
                b.Push(new Vector3(p.x, ground + hh / 2f, p.z), Quaternion.LookRotation(Vector3.Cross(right[n / 2], Vector3.up), Vector3.up));
                b.AddBox(Vector3.zero, new Vector3(half * 2f, hh, 0.012f), Pal.StoneDark, Pal.Stone, Pal.StoneDark);
                b.Pop();
            }
        }

        private static void Beam(MeshBuilder b, Vector3 a, Vector3 c, float size, Color colour)
        {
            var d = c - a;
            if (d.sqrMagnitude < 1e-10f) return;
            b.Push((a + c) * 0.5f, Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up));
            b.AddBox(Vector3.zero, new Vector3(size, size, d.magnitude + size), colour);
            b.Pop();
        }

        /// <summary>Suspension: two towers on the banks, sweeping main cables, hangers and a light deck.</summary>
        private void Suspension(MeshBuilder b, List<Vector3> path, List<Vector3> right, float half, bool heavy)
        {
            var white = Pal.Hex("#F2EEE4"); var cable = Pal.Hex("#C8453A");
            int n = path.Count;
            Slab(b, path, right, half, heavy ? 0.006f : 0.003f, heavy ? Pal.Hex("#8A8A90") : Pal.Hex("#A8743E"), Pal.Shade(white, 0.8f));
            int ta = Mathf.Clamp(Mathf.RoundToInt(n * 0.12f), 1, n - 2), tb = Mathf.Clamp(n - 1 - Mathf.RoundToInt(n * 0.12f), 1, n - 2);
            float tower = heavy ? 0.1f : 0.05f;
            foreach (int ti in new[] { ta, tb })
            {
                var p = path[ti];
                float ground = Ground(p);
                foreach (int side in new[] { -1, 1 })
                {
                    var foot = p + right[ti] * side * (half + 0.004f); foot.y = ground;
                    b.AddBox(new Vector3(foot.x, (ground + p.y + tower) * 0.5f, foot.z), new Vector3(0.006f, p.y + tower - ground, 0.006f), heavy ? white : Pal.Hex("#8A5A36"));
                }
                Beam(b, p + right[ti] * -(half + 0.004f) + Vector3.up * tower, p + right[ti] * (half + 0.004f) + Vector3.up * tower, 0.005f, heavy ? white : Pal.Hex("#8A5A36"));
                Beam(b, p + right[ti] * -(half + 0.004f) + Vector3.up * tower * 0.55f, p + right[ti] * (half + 0.004f) + Vector3.up * tower * 0.55f, 0.004f, heavy ? white : Pal.Hex("#8A5A36"));
            }
            foreach (int side in new[] { -1, 1 })
            {
                // Main cable: a sag between the towers, backstays down to the deck ends.
                var prev = path[0] + right[0] * side * (half + 0.004f);
                for (int i = 1; i < n; i++)
                {
                    float y;
                    if (i <= ta) y = Mathf.Lerp(0f, tower, i / (float)ta);
                    else if (i >= tb) y = Mathf.Lerp(tower, 0f, (i - tb) / (float)Mathf.Max(1, n - 1 - tb));
                    else { float u = (i - ta) / (float)(tb - ta) * 2f - 1f; y = Mathf.Lerp(0.008f, tower, u * u); }
                    var p = path[i] + right[i] * side * (half + 0.004f) + Vector3.up * y;
                    Beam(b, prev, p, 0.0012f, cable);
                    if (i > ta && i < tb && i % 3 == 0) Beam(b, p, path[i] + right[i] * side * (half + 0.004f), 0.0005f, Pal.Shade(cable, 0.8f));
                    prev = p;
                }
                Rail(b, path, right, side * (half - 0.001f), 0.008f, white, white, 4, 0.0011f);
            }
        }

        /// <summary>A covered bridge: plank walls with little windows under a gabled roof, open portals at both ends.</summary>
        private void Covered(MeshBuilder b, List<Vector3> path, List<Vector3> right, float half, bool rail)
        {
            var red = Pal.Hex("#A8403A"); var redDark = Pal.Shade(red, 0.8f); var roof = Pal.Hex("#5A4A44"); var white = Pal.Hex("#F2EEE4");
            int n = path.Count;
            float h = rail ? 0.095f : 0.055f;
            Slab(b, path, right, half, 0.006f, Pal.Hex("#8A6A4A"), redDark);
            foreach (int side in new[] { -1, 1 })
            {
                float x0 = side * (half - 0.002f), x1 = side * half;
                var wall = side > 0
                    ? new[] { new Vector2(x0, 0f), new Vector2(x0, h), new Vector2(x1, h), new Vector2(x1, 0f) }
                    : new[] { new Vector2(x1, 0f), new Vector2(x1, h), new Vector2(x0, h), new Vector2(x0, 0f) };
                b.AddExtrusion(path, right, Up(n), wall, new[] { red, red, redDark, red }, false);
                for (int i = 3; i < n - 3; i += 2)
                {
                    var face = path[i] + right[i] * side * (half + 0.0004f);
                    b.Push(face + Vector3.up * h * 0.5f, Quaternion.LookRotation(Vector3.Cross(right[i] * side, Vector3.up), Vector3.up));
                    b.AddBox(Vector3.zero, new Vector3(0.0004f, h * 0.95f, 0.0007f), redDark);
                    b.Pop();
                }
                for (int i = 6; i < n - 6; i += 8)
                {
                    var face = path[i] + right[i] * side * (half + 0.0006f);
                    b.AddFace(face + Vector3.up * h * 0.62f, right[i] * side, Vector3.up, 0.006f, 0.005f, Pal.Glow(Pal.WindowGlow));
                    b.Push(face + Vector3.up * h * 0.62f, Quaternion.LookRotation(right[i] * side, Vector3.up));
                    b.AddBox(new Vector3(0f, 0.0058f, 0.0004f), new Vector3(0.0136f, 0.0012f, 0.0008f), white);
                    b.AddBox(new Vector3(0f, -0.0058f, 0.0004f), new Vector3(0.0136f, 0.0012f, 0.0008f), white);
                    b.Pop();
                }
            }
            // Gabled roof along the bridge.
            var roofProfile = new[] { new Vector2(-half - 0.008f, h - 0.004f), new Vector2(0f, h + half * 0.75f), new Vector2(half + 0.008f, h - 0.004f) };
            var ext = new List<Vector3>(path);
            var t0 = (path[0] - path[1]).normalized; var t1 = (path[n - 1] - path[n - 2]).normalized;
            ext[0] += t0 * 0.006f; ext[n - 1] += t1 * 0.006f;
            b.AddExtrusion(ext, right, Up(n), roofProfile, new[] { Pal.Shade(roof, 0.85f), Pal.Shade(roof, 1.2f), Pal.Shade(roof, 0.85f) }, false);
            b.AddExtrusion(ext, right, Up(n), new[] { new Vector2(half + 0.008f, h - 0.0055f), new Vector2(0f, h + half * 0.75f - 0.0015f), new Vector2(-half - 0.008f, h - 0.0055f) }, new[] { roof, roof, roof }, false);
            // Gable boards with a white trim over each portal.
            foreach (int end in new[] { 0, n - 1 })
            {
                var c = path[end] + Vector3.up * h;
                var r = right[end];
                b.AddTriangleOutward(c - r * half, c + r * half, c + Vector3.up * half * 0.72f, path[end == 0 ? 1 : n - 2] + Vector3.up * h, red);
                b.AddBox(c + Vector3.up * 0.002f, new Vector3(Mathf.Abs(r.x) * half * 2f + 0.003f, 0.003f, Mathf.Abs(r.z) * half * 2f + 0.003f), white);
            }
            foreach (int end in new[] { 0, n - 1 })
            {
                var p = path[end];
                float ground = Ground(p), hh = Mathf.Max(0.004f, p.y - 0.006f - ground);
                b.Push(new Vector3(p.x, ground + hh / 2f, p.z), Quaternion.LookRotation(Vector3.Cross(right[end], Vector3.up), Vector3.up));
                b.AddBox(Vector3.zero, new Vector3(half * 2f + 0.006f, hh, 0.016f), Pal.StoneDark, Pal.Stone, Pal.StoneDark);
                b.Pop();
            }
        }

        /// <summary>A little humped wooden footbridge with handrails.</summary>
        private void Footbridge(MeshBuilder b, List<Vector3> path, List<Vector3> right, float half, float length)
        {
            var wood = Pal.Hex("#A8743E"); var dark = Pal.Hex("#6E4A33");
            int n = path.Count;
            float hump = Mathf.Min(0.012f, length * 0.05f);
            var humped = new List<Vector3>(n);
            for (int i = 0; i < n; i++) humped.Add(path[i] + Vector3.up * Mathf.Sin(i / (float)(n - 1) * Mathf.PI) * hump);
            Slab(b, humped, right, half, 0.003f, wood, dark);
            for (int i = 0; i < n; i++)
            {
                b.Push(humped[i] + Vector3.up * 0.0003f, Quaternion.LookRotation(Vector3.Cross(right[i], Vector3.up), Vector3.up));
                b.AddBox(Vector3.zero, new Vector3(half * 2f, 0.0006f, 0.0006f), dark);
                b.Pop();
            }
            foreach (int side in new[] { -1, 1 }) Rail(b, humped, right, side * (half - 0.0015f), 0.01f, dark, wood, 4, 0.0014f);
            foreach (int k in new[] { n / 3, 2 * n / 3 })
            {
                var p = humped[Mathf.Clamp(k, 0, n - 1)];
                float ground = Ground(p);
                foreach (int side in new[] { -1, 1 })
                {
                    var foot = p + right[k] * side * half * 0.7f;
                    b.AddBox(new Vector3(foot.x, (ground + p.y) * 0.5f, foot.z), new Vector3(0.0025f, Mathf.Max(0.002f, p.y - ground), 0.0025f), dark);
                }
            }
        }

        /// <summary>Stepping stones across the water.</summary>
        private void Stones(MeshBuilder b, Member m, ref Rng rng)
        {
            for (int i = 0; i < m.Points.Count; i += 2)
            {
                if (!m.Wet[i]) continue;
                var p = m.Points[i];
                float lvl = waterLevel(new Vector2(p.x, p.z));
                if (lvl < 0f) continue;
                var c = Pal.Jitter(Pal.Stone, ref rng, 0.25f);
                var q = new Vector3(p.x + rng.Range(-0.003f, 0.003f), lvl, p.z + rng.Range(-0.003f, 0.003f));
                b.AddBlob(q, new Vector3(0.0055f, 0.004f, 0.005f), 1, 0.2f, i, (d, _) => d.y > 0.5f ? Pal.Shade(c, 1.1f) : Pal.Shade(c, 0.75f), 2f);
            }
        }
    }
}
