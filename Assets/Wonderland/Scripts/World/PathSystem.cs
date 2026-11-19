using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    public enum PathKind { Road = 0, Lane = 1, Footpath = 2, Trail = 3, Fence = 4, Hedge = 5, Wall = 6, Highway = 7 }

    [Serializable]
    public sealed class PathLine
    {
        public int kind;
        public List<Vector2> points = new();   // 1 cm apart
        public bool closed;
        // A new road drawn on from free ends of roads (not saved): the lines it carries on, and whether from their starts.
        [NonSerialized] public int extendsStart = -1, extendsEnd = -1;
        [NonSerialized] public bool startAtItsStart, endAtItsStart;
    }

    /// <summary>
    /// Everything drawn as a line on the ground, with the same gesture as the railway and streams: cobbled village roads,
    /// country lanes, gravel footpaths and hiking trails (ways people walk), and fences, hedges and dry stone walls
    /// (barriers animals and people respect). Strokes snap onto existing lines so ways join into networks; a closed
    /// barrier makes an enclosure (a pasture keeps its animals in); where a way crosses the railway it gets a level crossing,
    /// the only place villagers cross the rails. Roads and lanes grade the ground smooth across their width. The ways
    /// form a walking graph villagers use to get about.
    /// </summary>
    public sealed partial class PathSystem : MonoBehaviour
    {
        public static readonly string[] Names = { "Road", "Lane", "Path", "Trail", "Fence", "Hedge", "Wall", "Highway" };
        public const float Spacing = 0.01f, Snap = 0.045f;
        public readonly List<PathLine> Lines = new();
        /// <summary>Level crossings: centre and the way's direction there.</summary>
        public readonly List<(Vector3 at, Vector3 dir, int kind)> LevelCrossings = new();
        private TerrainField terrain;
        private Material material;
        private Mesh groundMesh, barrierMesh;
        private Func<TrackNetwork> network;
        /// <summary>Bridge decks: whether a point is on one, its height, and whether the way's surface gives way to it.</summary>
        public BridgeSystem Bridges;
        private List<Node> graph;
        // Spatial index of every line's segments (5 cm cells), so the many per-frame "near a fence / on a road" questions
        // villagers and animals ask stay cheap.
        private readonly Dictionary<long, List<(int line, int seg)>> cells = new();
        private const float CellSize = 0.05f;
        private static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        public static bool IsWay(int kind) => kind <= (int)PathKind.Trail || kind == (int)PathKind.Highway;
        public static bool IsBarrier(int kind) => kind >= (int)PathKind.Fence && kind <= (int)PathKind.Wall;
        /// <summary>Asphalt roads with lanes and markings (a street, a highway): drawn straight with rounded corners, with junctions and traffic lights.</summary>
        public static bool IsRoad(int kind) => kind == (int)PathKind.Road || kind == (int)PathKind.Highway;
        /// <summary>Ways cars drive on.</summary>
        public static bool IsCarWay(int kind) => IsRoad(kind) || kind == (int)PathKind.Lane;
        public static float HalfWidth(int kind) => kind switch { 0 => 0.03f, 1 => 0.02f, 2 => 0.012f, 3 => 0.008f, 4 => 0.003f, 5 => 0.007f, 7 => 0.046f, _ => 0.006f };

        public void Init(TerrainField field, Material lit, Func<TrackNetwork> railway)
        {
            terrain = field; material = lit; network = railway;
            groundMesh = Renderer("Ways", ShadowCastingMode.Off);
            barrierMesh = Renderer("Fences and hedges", ShadowCastingMode.On);
        }

        private Mesh Renderer(string name, ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = shadows;
            return mesh;
        }

        public void SetLines(IEnumerable<PathLine> lines)
        {
            Lines.Clear();
            if (lines != null) foreach (var l in lines) Lines.Add(new PathLine { kind = l.kind, points = new List<Vector2>(l.points), closed = l.closed });
            Rebuild();
        }

        public static List<PathLine> Copy(List<PathLine> lines)
        {
            var c = new List<PathLine>(lines.Count);
            foreach (var l in lines) c.Add(new PathLine { kind = l.kind, points = new List<Vector2>(l.points), closed = l.closed });
            return c;
        }

        // ------------------------------------------------------------------ planning

        /// <summary>
        /// Turns a drawn stroke into a line of <paramref name="kind"/>: smoothed, ends snapped onto nearby lines of the same
        /// family (ways join ways, barriers join barriers), closed when it comes back to its start. Null with an error.
        /// </summary>
        public PathLine Plan(IReadOnlyList<Vector3> drawn, int kind, out string error)
        {
            error = null;
            var raw = new List<Vector2>();
            foreach (var p in drawn) if (raw.Count == 0 || (new Vector2(p.x, p.z) - raw[^1]).magnitude > 0.004f) raw.Add(new Vector2(p.x, p.z));
            float length = 0f;
            for (int i = 1; i < raw.Count; i++) length += (raw[i] - raw[i - 1]).magnitude;
            if (raw.Count < 2 || length < 0.06f) { error = "Draw a little longer"; return null; }
            bool closed = length > 0.25f && (raw[^1] - raw[0]).magnitude < 0.05f;
            if (IsRoad(kind)) return PlanRoad(raw, kind, closed, out error);
            // Snap the ends onto existing lines of the same family.
            if (!closed)
            {
                if (SnapPoint(raw[0], kind, out var a)) raw[0] = a;
                if (SnapPoint(raw[^1], kind, out var b)) raw[^1] = b;
            }
            var pts = Polyline.Resample(raw, Spacing * 2f, closed);
            for (int it = 0; it < 12; it++)
            {
                var copy = pts.ToArray();
                int n = pts.Count;
                for (int i = closed ? 0 : 1; i < (closed ? n : n - 1); i++)
                {
                    var prev = copy[(i - 1 + n) % n]; var next = copy[(i + 1) % n];
                    pts[i] = copy[i] + ((prev + next) * 0.5f - copy[i]) * (it % 2 == 0 ? 0.5f : -0.3f);
                }
            }
            pts = Polyline.Resample(pts, Spacing, closed);
            if (!closed) { pts[0] = raw[0]; pts[^1] = raw[^1]; }
            foreach (var p in pts)
                if (terrain.EdgeAt(p.x, p.y) < 0.35f) { error = "Keep it on the land"; return null; }
            return new PathLine { kind = kind, points = pts, closed = closed };
        }

        private bool SnapPoint(Vector2 p, int kind, out Vector2 snapped)
        {
            snapped = p;
            float best = Snap;
            bool found = false;
            foreach (var l in Lines)
            {
                if (IsWay(l.kind) != IsWay(kind)) continue;
                // Prefer the other line's ends, then anywhere along it.
                foreach (var e in l.closed ? Array.Empty<Vector2>() : new[] { l.points[0], l.points[^1] })
                    if ((e - p).magnitude < best * 1.3f) { best = (e - p).magnitude / 1.3f; snapped = e; found = true; }
                for (int i = 0; i < l.points.Count; i += 2)
                    if ((l.points[i] - p).magnitude < best) { best = (l.points[i] - p).magnitude; snapped = l.points[i]; found = true; }
            }
            return found;
        }

        /// <summary>Adds a planned line (joining the roads it carries on); the meshes follow with the next <see cref="Rebuild"/>.</summary>
        public void Add(PathLine line)
        {
            if (line.extendsStart >= 0 || line.extendsEnd >= 0) Join(line); else Lines.Add(line);
            graph = null;
            Index();
        }

        /// <summary>
        /// A road drawn on from free ends of roads becomes one road with them: the old road's points, then the new ones,
        /// then (joined at its far end) the other road's. The new road already leaves each end along the old road's line,
        /// so there is no seam; both ends of one road make it a ring.
        /// </summary>
        private void Join(PathLine line)
        {
            var pts = new List<Vector2>(line.points);
            if (line.extendsStart >= 0)
            {
                var head = new List<Vector2>(Lines[line.extendsStart].points);
                if (line.startAtItsStart) head.Reverse();   // (now it ends where the new road starts)
                while (head.Count > 0 && (head[^1] - pts[0]).magnitude < Spacing * 0.5f) head.RemoveAt(head.Count - 1);
                head.AddRange(pts);
                pts = head;
            }
            bool ring = line.extendsEnd >= 0 && line.extendsEnd == line.extendsStart;
            if (line.extendsEnd >= 0 && !ring)
            {
                var tail = new List<Vector2>(Lines[line.extendsEnd].points);
                if (!line.endAtItsStart) tail.Reverse();     // (now it starts where the new road ends)
                while (tail.Count > 0 && (tail[0] - pts[^1]).magnitude < Spacing * 0.5f) tail.RemoveAt(0);
                pts.AddRange(tail);
            }
            if (ring) while (pts.Count > 3 && (pts[^1] - pts[0]).magnitude < Spacing * 0.5f) pts.RemoveAt(pts.Count - 1);
            var joined = new PathLine { kind = line.kind, points = pts, closed = ring };
            if (line.extendsStart >= 0)
            {
                Lines[line.extendsStart] = joined;
                if (line.extendsEnd >= 0 && !ring) Lines.RemoveAt(line.extendsEnd);
            }
            else Lines[line.extendsEnd] = joined;
        }

        /// <summary>Re-indexes after lines were changed directly (removed) without a full rebuild.</summary>
        public void Reindex() => Index();

        /// <summary>The line nearest a local point within its width (plus <paramref name="margin"/>), or -1.</summary>
        public int Pick(Vector2 p, float margin = 0.01f)
        {
            float d = Query(p, 0.04f + margin, _ => true, out int line);
            return line >= 0 && d < HalfWidth(Lines[line].kind) + margin ? line : -1;
        }

        public static float Distance(PathLine l, Vector2 p)
        {
            float best = float.MaxValue;
            int n = l.points.Count, segs = l.closed ? n : n - 1;
            for (int i = 0; i < segs; i++) best = Mathf.Min(best, Polyline.DistanceToSegment(p, l.points[i], l.points[(i + 1) % n]));
            return best;
        }

        /// <summary>Distance to the nearest way (road, lane, path, trail), and its kind.</summary>
        /// <summary>The pavement beyond each kerb of a street or lane (where people walk); none on a highway or a path.</summary>
        public static float Pavement(int kind) => kind is (int)PathKind.Road or (int)PathKind.Lane ? 0.012f : 0f;

        /// <summary>Zebra crossings (set by the world): whether a point is on one.</summary>
        public Func<Vector2, bool> OnCrossing;

        /// <summary>What a point is to someone on foot: 0 nothing, 1 a path or a pavement, 2 a road's carriageway, 3 a zebra crossing.</summary>
        public int Walking(Vector2 p)
        {
            float d = Query(p, 0.09f, li => IsWay(Lines[li].kind), out int line);
            if (line < 0) return 0;
            int kind = Lines[line].kind;
            float hw = HalfWidth(kind);
            if (!IsCarWay(kind)) return d < hw ? 1 : 0;
            if (d < hw - RoadKerb * 0.5f) return OnCrossing != null && OnCrossing(p) ? 3 : 2;
            return d < hw + Pavement(kind) + 0.004f ? 1 : 0;
        }

        private const float RoadKerb = 0.0045f;

        public float WayDistance(Vector2 p, out int kind)
        {
            kind = -1;
            float d = Query(p, 0.08f, li => IsWay(Lines[li].kind), out int line);
            if (line < 0) return float.MaxValue;
            kind = Lines[line].kind;
            return d - HalfWidth(kind);
        }

        /// <summary>Whether a point is on a way or a barrier (things should not be planted or built there).</summary>
        public bool Covers(Vector2 p, float margin)
        {
            float d = Query(p, 0.03f + margin, _ => true, out int line);
            return line >= 0 && d < HalfWidth(Lines[line].kind) + Pavement(Lines[line].kind) + margin;
        }

        public bool NearBarrier(Vector2 p, float margin)
        {
            float d = Query(p, 0.01f + margin, li => IsBarrier(Lines[li].kind), out int line);
            return line >= 0 && d < HalfWidth(Lines[line].kind) + margin;
        }

        /// <summary>Whether the straight move from a to b passes through a fence, hedge or wall.</summary>
        public bool CrossesBarrier(Vector2 a, Vector2 b)
        {
            if ((b - a).magnitude > 0.06f)
            {
                // Long moves (picking a target): test the whole lines.
                foreach (var l in Lines)
                {
                    if (!IsBarrier(l.kind)) continue;
                    int n = l.points.Count, segs = l.closed ? n : n - 1;
                    for (int i = 0; i < segs; i += 2)
                        if (Polyline.SegmentsCross(a, b, l.points[i], l.points[Mathf.Min(i + 2, segs) % n])) return true;
                }
                return false;
            }
            int cx0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / CellSize) - 1, cx1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / CellSize) + 1;
            int cz0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / CellSize) - 1, cz1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / CellSize) + 1;
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                if (!cells.TryGetValue(CellKey(cx, cz), out var list)) continue;
                foreach (var (li, i) in list)
                {
                    var l = Lines[li];
                    if (!IsBarrier(l.kind)) continue;
                    if (Polyline.SegmentsCross(a, b, l.points[i], l.points[(i + 1) % l.points.Count])) return true;
                }
            }
            return false;
        }

        /// <summary>The enclosure (closed barrier) containing a point, or -1.</summary>
        public int EnclosureAt(Vector2 p)
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                var l = Lines[i];
                if (!IsBarrier(l.kind) || !l.closed) continue;
                if (Inside(l.points, p)) return i;
            }
            return -1;
        }

        private static bool Inside(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        /// <summary>The top of a wall, fence or hedge right at a point (a cat's perch), or 0.</summary>
        public float BarrierTop(Vector2 p)
        {
            foreach (var l in Lines)
            {
                if (!IsBarrier(l.kind) || Distance(l, p) > HalfWidth(l.kind) + 0.002f) continue;
                return l.kind == (int)PathKind.Fence ? 0.013f : l.kind == (int)PathKind.Hedge ? 0.017f : 0.011f;
            }
            return 0f;
        }

        public bool NearLevelCrossing(Vector2 p, float within)
        {
            foreach (var c in LevelCrossings)
                if (new Vector2(c.at.x - p.x, c.at.z - p.y).magnitude < within) return true;
            return false;
        }

        // ------------------------------------------------------------------ terrain

        /// <summary>Roads and lanes smooth the ground across their width (following the land along it).</summary>
        public RectInt Grade(PathLine l)
        {
            if (!IsCarWay(l.kind)) return new RectInt(0, 0, 0, 0);
            int n = l.points.Count;
            var h = new float[n];
            for (int i = 0; i < n; i++) h[i] = terrain.SurfaceAt(l.points[i].x, l.points[i].y);
            var smooth = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; int c = 0;
                for (int k = -6; k <= 6; k++) { int j = l.closed ? (i + k + n) % n : Mathf.Clamp(i + k, 0, n - 1); sum += h[j]; c++; }
                smooth[i] = sum / c;
            }
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in l.points) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
            float hw = HalfWidth(l.kind), margin = hw + 0.03f;
            var region = terrain.RegionAround(new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f), Mathf.Max(maxX - minX, maxZ - minZ) / 2f + margin + 0.02f);
            for (int z = region.yMin; z < region.yMax; z++)
            for (int x = region.xMin; x < region.xMax; x++)
            {
                var c = new Vector2(terrain.LocalX(x), terrain.LocalZ(z));
                if (c.x < minX - margin || c.x > maxX + margin || c.y < minZ - margin || c.y > maxZ + margin) continue;
                int g = z * terrain.Nx + x;
                if (terrain.Pad[g] > 0.05f || terrain.Lock[g] > 0.05f || terrain.Water[g] > 0.05f) continue;
                float best = float.MaxValue; int bi = 0;
                for (int i = 0; i < n; i += 2) { float d = (l.points[i] - c).sqrMagnitude; if (d < best) { best = d; bi = i; } }
                float dist = Mathf.Sqrt(best);
                if (dist > margin) continue;
                float w = dist < hw + 0.005f ? 1f : Mathf.SmoothStep(1f, 0f, (dist - hw - 0.005f) / (margin - hw - 0.005f));
                terrain.Height[g] = Mathf.Lerp(terrain.Height[g], terrain.HeightForSurface(g, smooth[bi]), IsRoad(l.kind) ? w : w * 0.9f);   // (under a road: flat)
            }
            terrain.UpdateSurface(region);
            return region;
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>Rebuilds every line's mesh, following the ground (after drawing, erasing or sculpting).</summary>
        private void Index()
        {
            cells.Clear();
            for (int li = 0; li < Lines.Count; li++)
            {
                var l = Lines[li];
                int n = l.points.Count, segs = l.closed ? n : n - 1;
                for (int i = 0; i < segs; i++)
                {
                    var a = l.points[i]; var b = l.points[(i + 1) % n];
                    foreach (var p in new[] { a, b })
                    {
                        long k = CellKey(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize));
                        if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<(int, int)>();
                        if (list.Count == 0 || list[^1] != (li, i)) list.Add((li, i));
                    }
                }
            }
        }

        /// <summary>The smallest distance from p to any segment of lines passing <paramref name="filter"/> within reach; returns the line.</summary>
        private float Query(Vector2 p, float reach, Func<int, bool> filter, out int line)
        {
            line = -1;
            float best = float.MaxValue;
            int r = Mathf.CeilToInt(reach / CellSize);
            int cx = Mathf.FloorToInt(p.x / CellSize), cz = Mathf.FloorToInt(p.y / CellSize);
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (!cells.TryGetValue(CellKey(cx + dx, cz + dz), out var list)) continue;
                foreach (var (li, i) in list)
                {
                    var l = Lines[li];
                    if (!filter(li)) continue;
                    float d = Polyline.DistanceToSegment(p, l.points[i], l.points[(i + 1) % l.points.Count]);
                    if (d < best) { best = d; line = li; }
                }
            }
            return best;
        }

        public void Rebuild()
        {
            graph = null;
            PiecesBuilt = 0;
            Index();
            // The junctions: found again when the ways have changed, else only levelled again where the ground has.
            bool restructured = Restructured();
            if (restructured) FindJunctions(); else Relevel();
            // Ways in a fixed order, widest first, each kind a hair higher, so junctions overlap cleanly.
            var pieces = new List<Piece>();
            foreach (var kind in new[] { PathKind.Highway, PathKind.Road, PathKind.Lane, PathKind.Footpath, PathKind.Trail })
                foreach (var l in Lines) if (l.kind == (int)kind) pieces.Add(WayPiece(l));
            pieces.AddRange(JunctionPieces());
            // (nothing redone and nothing moved: the ways' ground stays as it is)
            if (restructured || PiecesBuilt > 0)
            {
                Gather(ground, pieces, p => p.Ground);
                ground.ToMesh("Ways", groundMesh); groundMesh.RecalculateBounds();
            }
            Gather(barriers, pieces, p => p.Props);
            foreach (var l in Lines) if (IsBarrier(l.kind)) Barrier(barriers, l);
            FindLevelCrossings(barriers);
            ForgetPieces();
            barriers.ToMesh("Fences and hedges", barrierMesh); barrierMesh.RecalculateBounds();
        }

        private static Color WayColour(int kind, Vector2 p, float across)
        {
            float cell = Noise.Hash(Mathf.FloorToInt(p.x * 160f), Mathf.FloorToInt(p.y * 160f), kind);
            switch (kind)
            {
                case 0:   // cobbles with pale kerb stones
                    if (Mathf.Abs(across) > 0.82f) return Pal.Shade(Pal.Hex("#C9BBA3"), 0.9f + 0.15f * cell);
                    return Pal.Shade(Pal.Hex("#9C9488"), 0.82f + 0.3f * cell);
                case 1:   // a lane: two wheel ruts with grass down the middle
                    if (Mathf.Abs(across) < 0.22f) return Pal.Shade(Pal.Hex("#7FA04A"), 0.85f + 0.25f * cell);
                    return Pal.Shade(Pal.Hex("#A8865E"), 0.85f + 0.25f * cell);
                case 2: return Pal.Shade(Pal.Hex("#D8C49A"), 0.88f + 0.2f * cell);   // gravel
                default: return Pal.Shade(Pal.Hex("#8E6A44"), 0.85f + 0.25f * cell);  // trodden earth
            }
        }

        private void Way(MeshBuilder b, MeshBuilder props, PathLine l)
        {
            int n = l.points.Count;
            if (n < 2) return;
            float hw = HalfWidth(l.kind), lift = 0.0014f + 0.00025f * (3 - l.kind);
            const int across = 4;
            int start = b.VertexCount;
            var hidden = new bool[n];
            for (int i = 0; i < n; i++)
            {
                float deckH = 0f; bool hide = false;
                bool onDeck = Bridges != null && Bridges.Deck(l.points[i], out deckH, out hide);
                hidden[i] = onDeck && hide || IsCarWay(l.kind) && InJunction(l.points[i], -0.003f);   // (a lane inside a junction: the junction's asphalt)
                var p = l.points[i];
                var t = l.points[l.closed ? (i + 1) % n : Mathf.Min(i + 1, n - 1)] - l.points[l.closed ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)];
                t = t.sqrMagnitude > 1e-10f ? t.normalized : Vector2.up;
                var right = new Vector2(t.y, -t.x);
                // Taper the very ends of open lines so they fade into the grass.
                float taper = l.closed ? 1f : Mathf.Clamp01(Mathf.Min(i, n - 1 - i) / 3f) * 0.4f + 0.6f;
                for (int k = 0; k <= across; k++)
                {
                    float u = k / (float)across * 2f - 1f;
                    var q = p + right * (u * hw * taper);
                    float y = (onDeck ? deckH : terrain.SurfaceAt(q.x, q.y)) + lift * (1f - 0.35f * Mathf.Abs(u));
                    b.AddVertex(new Vector3(q.x, y, q.y), Vector3.up, WayColour(l.kind, q, u));
                }
                if (i > 0 && !(hidden[i] && hidden[i - 1]))
                    for (int k = 0; k < across; k++)
                    {
                        int a = start + (i - 1) * (across + 1) + k, c = start + i * (across + 1) + k;
                        b.AddTriangle(a, c + 1, a + 1); b.AddTriangle(a, c, c + 1);
                    }
            }
            if (l.closed)
                for (int k = 0; k < across; k++)
                {
                    int a = start + (n - 1) * (across + 1) + k, c = start + k;
                    b.AddTriangle(a, c + 1, a + 1); b.AddTriangle(a, c, c + 1);
                }
            b.RecalculateNormals(start);
            // Trails get little red-and-white way markers; roads a lamp now and then.
            if (l.kind == (int)PathKind.Trail)
                for (int i = 15; i < n; i += 45)
                {
                    var t = l.points[Mathf.Min(i + 1, n - 1)] - l.points[Mathf.Max(i - 1, 0)];
                    var q = l.points[i] + new Vector2(t.y, -t.x).normalized * 0.015f;
                    float y = terrain.SurfaceAt(q.x, q.y);
                    props.AddBox(new Vector3(q.x, y + 0.006f, q.y), new Vector3(0.0018f, 0.012f, 0.0018f), Pal.Hex("#8A5A36"));
                    props.AddBox(new Vector3(q.x, y + 0.0115f, q.y), new Vector3(0.0026f, 0.0016f, 0.0026f), Pal.Hex("#F7F1E6"));
                    props.AddBox(new Vector3(q.x, y + 0.0098f, q.y), new Vector3(0.0026f, 0.0016f, 0.0026f), Pal.Hex("#E25B4F"));
                }
        }

        private void Barrier(MeshBuilder b, PathLine l)
        {
            int n = l.points.Count;
            var rng = new Rng(l.points.Count * 31 + l.kind);
            Vector3 At(int i) { var p = l.points[i]; return new Vector3(p.x, terrain.SurfaceAt(p.x, p.y), p.y); }
            switch ((PathKind)l.kind)
            {
                case PathKind.Fence:
                {
                    var wood = Pal.Hex("#8A5A36"); var rail = Pal.Hex("#A8743E");
                    int step = 2;
                    for (int i = 0; i < n; i += step)
                    {
                        var p = At(i);
                        b.AddBox(p + Vector3.up * 0.006f, new Vector3(0.0018f, 0.013f, 0.0018f), wood);
                        int j = Mathf.Min(i + step, l.closed ? i + step : n - 1);
                        if (j == i) continue;
                        var q = At(j % n);
                        foreach (float y in new[] { 0.0055f, 0.0105f })
                        {
                            var a = p + Vector3.up * y; var c = q + Vector3.up * y;
                            b.Push((a + c) * 0.5f, Quaternion.LookRotation((c - a).normalized, Vector3.up));
                            b.AddBox(Vector3.zero, new Vector3(0.0009f, 0.0016f, (c - a).magnitude * 1.05f), rail);
                            b.Pop();
                        }
                    }
                    break;
                }
                case PathKind.Hedge:
                {
                    for (int i = 0; i < n; i += 1)
                    {
                        var p = At(i);
                        var leaf = Pal.Jitter(Pal.LeafDeep, ref rng, 0.18f);
                        var light = Pal.Shade(leaf, 1.25f);
                        b.Sway = 0.15f;
                        b.AddBlob(p + Vector3.up * 0.007f, new Vector3(0.0075f, 0.0085f, 0.0075f), 1, 0.25f, i + l.kind * 1000, (d, _) => Color.Lerp(Pal.Shade(leaf, 0.7f), light, d.y * 0.5f + 0.5f), 3f);
                        b.Sway = 0f;
                        if (rng.Chance(0.12f))
                        {
                            var c = rng.Chance(0.5f) ? Pal.FlowerWhite : Pal.FlowerPink;
                            b.AddBlob(p + new Vector3(rng.Range(-0.005f, 0.005f), 0.012f, rng.Range(-0.005f, 0.005f)), Vector3.one * 0.0016f, 0, 0f, i, (_, __) => c);
                        }
                    }
                    break;
                }
                default:
                {
                    // Dry stone wall: rough stones in two courses with a row of coping stones on top.
                    for (int i = 0; i < n; i++)
                    {
                        var p = At(i);
                        var t = At(l.closed ? (i + 1) % n : Mathf.Min(i + 1, n - 1)) - At(l.closed ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)); t.y = 0f;
                        var rot = Quaternion.LookRotation(t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward, Vector3.up);
                        for (int course = 0; course < 2; course++)
                        {
                            var c = Pal.Jitter(Pal.Stone, ref rng, 0.3f);
                            b.Push(p + Vector3.up * (0.003f + course * 0.0045f), rot * Quaternion.Euler(0f, rng.Range(-8f, 8f), 0f));
                            b.AddBox(new Vector3(0f, 0f, course * 0.004f), new Vector3(0.011f, 0.0045f, 0.0105f), Pal.Shade(c, 0.85f), c, c);
                            b.Pop();
                        }
                        if (i % 2 == 0)
                        {
                            var c = Pal.Jitter(Pal.StoneDark, ref rng, 0.2f);
                            b.Push(p + Vector3.up * 0.0105f, rot * Quaternion.Euler(0f, 0f, rng.Range(-15f, 15f)));
                            b.AddBox(Vector3.zero, new Vector3(0.012f, 0.003f, 0.007f), c);
                            b.Pop();
                        }
                        if (rng.Chance(0.06f)) b.AddBlob(p + new Vector3(0f, 0.012f, 0f), new Vector3(0.004f, 0.002f, 0.004f), 1, 0.3f, i, (_, __) => Pal.Hex("#7E9A4A"));
                    }
                    break;
                }
            }
        }

        /// <summary>Planks between the rails where a way crosses the railway, with crossbucks beside roads and lanes.</summary>
        private void FindLevelCrossings(MeshBuilder b)
        {
            LevelCrossings.Clear();
            var net = network?.Invoke();
            if (net == null || net.Empty) return;
            foreach (var l in Lines)
            {
                if (!IsWay(l.kind)) continue;
                int n = l.points.Count;
                bool wasOn = false;
                for (int i = 0; i < n; i++)
                {
                    var p = l.points[i];
                    bool on = net.Nearest(new Vector3(p.x, 0f, p.y), out int edge, out float s, out float d, 0.012f);
                    if (on && !wasOn)
                    {
                        var layout = net.Edges[edge].Layout;
                        var c = layout.Evaluate(s, out var tangent); tangent.y = 0f; tangent.Normalize();
                        var dir = new Vector3(l.points[Mathf.Min(i + 1, n - 1)].x - l.points[Mathf.Max(i - 1, 0)].x, 0f, l.points[Mathf.Min(i + 1, n - 1)].y - l.points[Mathf.Max(i - 1, 0)].y).normalized;
                        LevelCrossings.Add((c, dir, l.kind));
                        // Planks laid along the rails, filling the way's width between and beside them.
                        float hw = Mathf.Max(HalfWidth(l.kind), 0.014f);
                        var right = Vector3.Cross(Vector3.up, tangent);
                        for (float off = -hw; off <= hw + 0.0001f; off += 0.0055f)
                        {
                            b.Push(c + Vector3.up * (TrackSystem.RailTop - 0.0006f) + tangent * off, Quaternion.LookRotation(right, Vector3.up));
                            b.AddBox(Vector3.zero, new Vector3(0.005f, 0.0012f, 0.05f), Pal.Shade(Pal.Hex("#8A6A4A"), 0.9f + 0.15f * Noise.Hash(Mathf.RoundToInt(off * 1000f), i, 3)));
                            b.Pop();
                        }
                        if (IsCarWay(l.kind))
                            foreach (int side in new[] { -1, 1 })
                            {
                                // A crossbuck sign on each side, facing the way.
                                var post = c + right * (side * 0.055f) + dir * (side * hw * 0.8f);
                                post.y = terrain.SurfaceAt(post.x, post.z);
                                b.AddBox(post + Vector3.up * 0.013f, new Vector3(0.0016f, 0.026f, 0.0016f), Pal.Hex("#F7F1E6"));
                                b.Push(post + Vector3.up * 0.024f, Quaternion.LookRotation(dir * side, Vector3.up));
                                foreach (float a in new[] { 40f, -40f })
                                {
                                    b.Push(new Vector3(0f, 0f, 0.0012f), Quaternion.Euler(0f, 0f, a));
                                    b.AddBox(Vector3.zero, new Vector3(0.016f, 0.0028f, 0.0008f), Pal.Hex("#F7F1E6"));
                                    b.AddBox(new Vector3(0f, 0f, 0.0005f), new Vector3(0.005f, 0.0029f, 0.0004f), Pal.Hex("#E25B4F"));
                                    b.Pop();
                                }
                                b.Pop();
                            }
                    }
                    wasOn = on;
                }
            }
        }

        // ------------------------------------------------------------------ walking graph

        private sealed class Node { public Vector2 P; public readonly List<int> Next = new(); public int Kind; }

        private void BuildGraph()
        {
            graph = new List<Node>();
            var grid = new Dictionary<long, List<int>>();
            long Key(Vector2 p) => ((long)Mathf.FloorToInt(p.x / 0.04f) << 32) ^ (uint)Mathf.FloorToInt(p.y / 0.04f);
            foreach (var l in Lines)
            {
                if (!IsWay(l.kind)) continue;
                int first = graph.Count, prev = -1;
                for (int i = 0; i < l.points.Count; i += 4)
                {
                    var node = new Node { P = l.points[i], Kind = l.kind };
                    int id = graph.Count;
                    graph.Add(node);
                    if (prev >= 0) { graph[prev].Next.Add(id); node.Next.Add(prev); }
                    prev = id;
                }
                if (l.closed && graph.Count - first > 2) { graph[prev].Next.Add(first); graph[first].Next.Add(prev); }
                else if (!l.closed && (l.points.Count - 1) % 4 != 0)
                {
                    var node = new Node { P = l.points[^1], Kind = l.kind };
                    graph.Add(node); graph[prev].Next.Add(graph.Count - 1); node.Next.Add(prev);
                }
            }
            // Junctions: nodes of different lines close together are connected.
            for (int i = 0; i < graph.Count; i++)
            {
                var k = Key(graph[i].P);
                if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<int>();
                list.Add(i);
            }
            for (int i = 0; i < graph.Count; i++)
            {
                int cx = Mathf.FloorToInt(graph[i].P.x / 0.04f), cz = Mathf.FloorToInt(graph[i].P.y / 0.04f);
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!grid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var list)) continue;
                    foreach (int j in list)
                        if (j > i && !graph[i].Next.Contains(j) && (graph[i].P - graph[j].P).magnitude < 0.03f) { graph[i].Next.Add(j); graph[j].Next.Add(i); }
                }
            }
        }

        private int NearestNode(Vector2 p, float within)
        {
            int best = -1; float bestD = within * within;
            for (int i = 0; i < graph.Count; i++) { float d = (graph[i].P - p).sqrMagnitude; if (d < bestD) { bestD = d; best = i; } }
            return best;
        }

        /// <summary>
        /// A walking route from <paramref name="from"/> to <paramref name="to"/> along ways, when both are near the way
        /// network and it is not a big detour; null otherwise (walk straight).
        /// </summary>
        public List<Vector2> Route(Vector2 from, Vector2 to)
        {
            if (graph == null) BuildGraph();
            if (graph.Count < 2) return null;
            int a = NearestNode(from, 0.12f), z = NearestNode(to, 0.12f);
            if (a < 0 || z < 0 || a == z) return null;
            // A* over the nodes.
            var cost = new Dictionary<int, float> { [a] = 0f };
            var came = new Dictionary<int, int>();
            var open = new List<int> { a };
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                    if (cost[open[i]] + (graph[open[i]].P - to).magnitude < cost[open[best]] + (graph[open[best]].P - to).magnitude) best = i;
                int cur = open[best]; open.RemoveAt(best);
                if (cur == z) break;
                foreach (int nx in graph[cur].Next)
                {
                    float c = cost[cur] + (graph[nx].P - graph[cur].P).magnitude;
                    if (cost.TryGetValue(nx, out float old) && old <= c) continue;
                    cost[nx] = c; came[nx] = cur;
                    if (!open.Contains(nx)) open.Add(nx);
                }
            }
            if (!cost.ContainsKey(z)) return null;
            var route = new List<Vector2>();
            for (int cur = z; cur != a; cur = came[cur]) route.Add(graph[cur].P);
            route.Add(graph[a].P);
            route.Reverse();
            float direct = (to - from).magnitude;
            if (cost[z] > direct * 2.5f + 0.2f) return null;
            return route;
        }

        /// <summary>A hike: the points of a trail near <paramref name="home"/> from its nearest point to one end (or round a loop).</summary>
        public List<Vector2> Hike(Vector2 home, float within, ref Rng rng)
        {
            var trails = new List<PathLine>();
            foreach (var l in Lines) if (l.kind == (int)PathKind.Trail && Distance(l, home) < within) trails.Add(l);
            if (trails.Count == 0) return null;
            var t = trails[rng.Range(0, trails.Count)];
            int nearest = 0; float best = float.MaxValue;
            for (int i = 0; i < t.points.Count; i++) { float d = (t.points[i] - home).sqrMagnitude; if (d < best) { best = d; nearest = i; } }
            var route = new List<Vector2>();
            bool forward = t.closed ? rng.Chance(0.5f) : nearest < t.points.Count / 2;   // the longer way along it
            int count = t.closed ? t.points.Count : (forward ? t.points.Count - 1 - nearest : nearest);
            for (int k = 0; k <= count; k += 3)
            {
                int i = forward ? nearest + k : nearest - k;
                if (t.closed) i = (i % t.points.Count + t.points.Count) % t.points.Count;
                route.Add(t.points[Mathf.Clamp(i, 0, t.points.Count - 1)]);
            }
            return route.Count > 2 ? route : null;
        }

        /// <summary>A point on a way near <paramref name="home"/> (somewhere to stroll to), or null.</summary>
        public Vector2? StrollTarget(Vector2 home, float within, ref Rng rng)
        {
            if (graph == null) BuildGraph();
            var near = new List<int>();
            for (int i = 0; i < graph.Count; i++) if ((graph[i].P - home).magnitude < within) near.Add(i);
            if (near.Count == 0) return null;
            return graph[near[rng.Range(0, near.Count)]].P;
        }
    }
}
