using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Roads and highways: drawn like the railway, not like a trail - the hand's stroke is simplified into straight
    /// stretches joined by round corners (a street turns tighter than a highway), its ends snap onto other roads, and
    /// wherever roads meet or cross there is a junction: a patch of plain asphalt with stop lines, and traffic lights on
    /// every arm when three or more roads meet. A street has a kerb each side, white edge lines and a dashed centre line;
    /// a highway two lanes each way with a double yellow line down the middle. The lights themselves (which lamp is lit)
    /// are the traffic's (see TrafficSystem.Lights).
    /// </summary>
    public sealed partial class PathSystem
    {
        public const float StreetRadius = 0.05f, HighwayRadius = 0.18f, KerbWidth = 0.0045f;

        /// <summary>Where roads meet: its centre, how big, and the directions of its arms (away from the centre).</summary>
        public sealed class RoadJunction
        {
            public Vector2 At;
            public float Radius;
            public readonly List<Vector2> Arms = new();
            public readonly List<float> ArmHalfWidth = new();
            public bool Lights => Arms.Count >= 3;
            /// <summary>Each arm's light phase: arms facing each other share one (the through road, both roads of a crossroads).</summary>
            public int[] Phase;
            public int Phases;

            public void Group()
            {
                Phase = new int[Arms.Count];
                for (int i = 0; i < Phase.Length; i++) Phase[i] = -1;
                Phases = 0;
                for (int i = 0; i < Phase.Length; i++)
                {
                    if (Phase[i] >= 0) continue;
                    Phase[i] = Phases;
                    for (int k = i + 1; k < Phase.Length; k++) if (Phase[k] < 0 && Vector2.Dot(Arms[i], Arms[k]) < -0.8f) Phase[k] = Phases;
                    Phases++;
                }
            }
        }

        public readonly List<RoadJunction> Junctions = new();

        /// <summary>A traffic light's lamps: on which junction's which arm, where its head is, and which way it faces.</summary>
        public readonly List<(int junction, int arm, Vector3 head, Vector3 facing)> LightHeads = new();

        /// <summary>Lane centres (from the middle of the road) for traffic keeping right.</summary>
        public static float LaneOffset(int kind, bool slowLane) => kind switch
        {
            (int)PathKind.Highway => slowLane ? 0.0315f : 0.0105f,
            (int)PathKind.Road => 0.0128f,
            _ => 0.0105f,
        };

        // ------------------------------------------------------------------ planning

        /// <summary>A road from a stroke: ends snapped, straight stretches with rounded corners, every 1 cm.</summary>
        private PathLine PlanRoad(List<Vector2> raw, int kind, bool closed, out string error)
        {
            error = null;
            if (!closed)
            {
                if (SnapRoad(raw[0], out var a)) raw[0] = a;
                if (SnapRoad(raw[^1], out var b)) raw[^1] = b;
            }
            float tolerance = kind == (int)PathKind.Highway ? 0.03f : 0.018f;
            var corners = closed ? ClosedSimplify(raw, tolerance) : Simplify(raw, tolerance);
            // Corners closer together than a road's width are hand jitter: merge them.
            for (int i = corners.Count - 2; i >= 1; i--)
                if ((corners[i] - corners[i - 1]).magnitude < 0.04f && corners.Count > (closed ? 3 : 2)) corners.RemoveAt(i);
            if (closed && corners.Count < 3) { error = "Draw a bigger ring road"; return null; }
            var pts = Fillet(corners, closed, kind == (int)PathKind.Highway ? HighwayRadius : StreetRadius);
            pts = TrackPlanner.Resample(pts, Spacing, closed);
            if (!closed) { pts[0] = raw[0]; pts[^1] = raw[^1]; }
            foreach (var p in pts)
                if (terrain.EdgeAt(p.x, p.y) < 0.35f) { error = "Keep the road on the land"; return null; }
            return new PathLine { kind = kind, points = pts, closed = closed };
        }

        /// <summary>An end near a road: onto its end (to carry it on) or onto its middle (a junction).</summary>
        private bool SnapRoad(Vector2 p, out Vector2 snapped)
        {
            snapped = p;
            float best = Snap; bool found = false;
            foreach (var l in Lines)
            {
                if (!IsCarWay(l.kind)) continue;
                if (!l.closed) foreach (var e in new[] { l.points[0], l.points[^1] }) if ((e - p).magnitude < best * 1.4f) { best = (e - p).magnitude / 1.4f; snapped = e; found = true; }
                for (int i = 0; i < l.points.Count; i++) if ((l.points[i] - p).magnitude < best) { best = (l.points[i] - p).magnitude; snapped = l.points[i]; found = true; }
            }
            return found;
        }

        /// <summary>Douglas-Peucker: the fewest corners within <paramref name="tolerance"/> of the stroke.</summary>
        private static List<Vector2> Simplify(List<Vector2> p, float tolerance)
        {
            var keep = new bool[p.Count];
            keep[0] = keep[^1] = true;
            var stack = new Stack<(int a, int b)>();
            stack.Push((0, p.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                float worst = 0f; int at = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = DistanceToSegment(p[i], p[a], p[b]);
                    if (d > worst) { worst = d; at = i; }
                }
                if (at >= 0 && worst > tolerance) { keep[at] = true; stack.Push((a, at)); stack.Push((at, b)); }
            }
            var r = new List<Vector2>();
            for (int i = 0; i < p.Count; i++) if (keep[i]) r.Add(p[i]);
            return r;
        }

        private static List<Vector2> ClosedSimplify(List<Vector2> p, float tolerance)
        {
            // Split the ring at its farthest point from the start and simplify both halves.
            int far = 0; float best = 0f;
            for (int i = 0; i < p.Count; i++) { float d = (p[i] - p[0]).sqrMagnitude; if (d > best) { best = d; far = i; } }
            var first = Simplify(p.GetRange(0, far + 1), tolerance);
            var second = p.GetRange(far, p.Count - far); second.Add(p[0]);
            second = Simplify(second, tolerance);
            first.RemoveAt(first.Count - 1);
            second.RemoveAt(second.Count - 1);
            first.AddRange(second);
            return first;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Straight stretches between the corners, each corner rounded with an arc of (up to) <paramref name="radius"/>.</summary>
        private static List<Vector2> Fillet(List<Vector2> c, bool closed, float radius)
        {
            var r = new List<Vector2>();
            int n = c.Count;
            if (!closed) r.Add(c[0]);
            for (int i = closed ? 0 : 1; i < (closed ? n : n - 1); i++)
            {
                var prev = c[(i - 1 + n) % n]; var at = c[i]; var next = c[(i + 1) % n];
                var d1 = (at - prev).normalized; var d2 = (next - at).normalized;
                float turn = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d1, d2), -1f, 1f));
                if (turn < 2f * Mathf.Deg2Rad) { r.Add(at); continue; }
                float t = radius * Mathf.Tan(turn / 2f);
                t = Mathf.Min(t, 0.45f * Mathf.Min((at - prev).magnitude, (next - at).magnitude));
                float rad = t / Mathf.Tan(turn / 2f);
                var start = at - d1 * t;
                float side = d1.x * d2.y - d1.y * d2.x > 0f ? 1f : -1f;
                var normal = side > 0f ? new Vector2(-d1.y, d1.x) : new Vector2(d1.y, -d1.x);
                var centre = start + normal * rad;
                var from = start - centre;
                int steps = Mathf.Max(2, Mathf.CeilToInt(turn * rad / 0.01f));
                for (int k = 0; k <= steps; k++)
                {
                    float a = side * turn * k / steps;
                    r.Add(centre + new Vector2(from.x * Mathf.Cos(a) - from.y * Mathf.Sin(a), from.x * Mathf.Sin(a) + from.y * Mathf.Cos(a)));
                }
            }
            if (!closed) r.Add(c[^1]);
            return r;
        }

        // ------------------------------------------------------------------ junctions

        /// <summary>Finds where roads (and lanes) meet or cross, and the arms leaving each junction.</summary>
        private void FindJunctions()
        {
            Junctions.Clear(); LightHeads.Clear();
            var found = new List<Vector2>();
            void Candidate(Vector2 p)
            {
                foreach (var q in found) if ((q - p).magnitude < 0.05f) return;
                found.Add(p);
            }
            for (int li = 0; li < Lines.Count; li++)
            {
                var l = Lines[li];
                if (!IsCarWay(l.kind) || l.points.Count < 2) continue;
                // Its ends on another road (a T or a corner), and where it crosses one (an X).
                if (!l.closed)
                    foreach (var e in new[] { l.points[0], l.points[^1] })
                        if (Query(e, 0.02f, k => k != li && IsCarWay(Lines[k].kind), out _) < 0.012f) Candidate(e);
                int segs = l.closed ? l.points.Count : l.points.Count - 1;
                for (int i = 0; i < segs; i++)
                {
                    var a = l.points[i]; var b = l.points[(i + 1) % l.points.Count];
                    foreach (var end in new[] { a, b })
                    {
                        if (!cells.TryGetValue(CellKey(Mathf.FloorToInt(end.x / CellSize), Mathf.FloorToInt(end.y / CellSize)), out var list)) continue;
                        foreach (var (lk, k) in list)
                        {
                            if (lk <= li || !IsCarWay(Lines[lk].kind)) continue;
                            var o = Lines[lk];
                            if (Crossing(a, b, o.points[k], o.points[(k + 1) % o.points.Count], out var at)) Candidate(at);
                        }
                    }
                }
            }
            foreach (var p in found)
            {
                var j = new RoadJunction { At = p };
                float half = 0f;
                for (int li = 0; li < Lines.Count; li++)
                {
                    var l = Lines[li];
                    if (!IsCarWay(l.kind) || Distance(l, p) > 0.012f) continue;
                    half = Mathf.Max(half, HalfWidth(l.kind));
                }
                j.Radius = half + 0.006f;
                float reach = j.Radius + 0.025f;
                for (int li = 0; li < Lines.Count; li++)
                {
                    var l = Lines[li];
                    if (!IsCarWay(l.kind) || Distance(l, p) > 0.012f) continue;
                    int n = l.points.Count, near = 0; float nearD = float.MaxValue;
                    for (int i = 0; i < n; i++) { float d = (l.points[i] - p).sqrMagnitude; if (d < nearD) { nearD = d; near = i; } }
                    foreach (int step in new[] { 1, -1 })
                    {
                        for (int i = near, k = 0; k < n; i += step, k++)
                        {
                            if (!l.closed && (i < 0 || i >= n)) break;
                            var q = l.points[(i % n + n) % n];
                            if ((q - p).magnitude < reach) continue;
                            var dir = (q - p).normalized;
                            bool dup = false;
                            foreach (var a in j.Arms) if (Vector2.Dot(a, dir) > 0.94f) dup = true;
                            if (!dup) { j.Arms.Add(dir); j.ArmHalfWidth.Add(HalfWidth(l.kind)); }
                            break;
                        }
                    }
                }
                if (j.Arms.Count >= 3) { j.Group(); Junctions.Add(j); }
            }
        }

        private static bool Crossing(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 at)
        {
            at = default;
            var r = b - a; var q = d - c;
            float den = r.x * q.y - r.y * q.x;
            if (Mathf.Abs(den) < 1e-10f) return false;
            float t = ((c.x - a.x) * q.y - (c.y - a.y) * q.x) / den, u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / den;
            if (t < -0.01f || t > 1.01f || u < -0.01f || u > 1.01f) return false;
            at = a + r * t;
            return true;
        }

        /// <summary>How far a point is inside the nearest junction's patch (0 outside), for leaving out markings and kerbs.</summary>
        private bool InJunction(Vector2 p, float margin)
        {
            foreach (var j in Junctions) if ((p - j.At).magnitude < j.Radius + margin) return true;
            return false;
        }

        // ------------------------------------------------------------------ meshes

        private static readonly Color Asphalt = new(0.29f, 0.29f, 0.31f), KerbStone = new(0.78f, 0.77f, 0.74f), Paint = new(0.96f, 0.95f, 0.9f), Yellow = new(0.95f, 0.78f, 0.25f);

        private float RoadHeight(Vector2 q, out bool onDeck)
        {
            float deck = 0f;
            onDeck = Bridges != null && Bridges.Deck(q, out deck, out _);
            return onDeck ? deck : terrain.SurfaceAt(q.x, q.y);
        }

        /// <summary>A road's surface: asphalt between kerbs, edge lines and a centre line (left out inside junctions).</summary>
        private void RoadSurface(MeshBuilder b, MeshBuilder props, PathLine l)
        {
            int n = l.points.Count;
            if (n < 2) return;
            float hw = HalfWidth(l.kind);
            bool highway = l.kind == (int)PathKind.Highway;
            float lift = 0.0016f + (highway ? 0.0002f : 0f);
            // The cross-section, from left kerb to right kerb: offsets and whether that strip is kerb (raised, pale).
            float[] across = { -hw, -hw + KerbWidth, hw - KerbWidth, hw };
            int start = b.VertexCount;
            for (int i = 0; i < n; i++)
            {
                var p = l.points[i];
                var t = Tangent(l, i);
                var right = new Vector2(t.y, -t.x);
                bool junction = InJunction(p, 0.004f);
                for (int k = 0; k < across.Length; k++)
                {
                    var q = p + right * across[k];
                    bool kerb = !junction && (k == 0 || k == 3);
                    float y = RoadHeight(q, out _) + lift + (kerb ? 0.0012f : 0f);
                    var colour = (k == 0 || k == 3) && !junction ? Pal.Shade(KerbStone, 0.95f + 0.1f * Noise.Hash(i / 3, k, 7)) : Pal.Shade(Asphalt, 0.96f + 0.06f * Noise.Hash(i / 2, k, 3));
                    b.AddVertex(new Vector3(q.x, y, q.y), Vector3.up, colour);
                }
                if (i > 0)
                    for (int k = 0; k < across.Length - 1; k++)
                    {
                        int a = start + (i - 1) * across.Length + k, c = start + i * across.Length + k;
                        b.AddTriangle(a, c + 1, a + 1); b.AddTriangle(a, c, c + 1);
                    }
            }
            if (l.closed)
                for (int k = 0; k < across.Length - 1; k++)
                {
                    int a = start + (n - 1) * across.Length + k, c = start + k;
                    b.AddTriangle(a, c + 1, a + 1); b.AddTriangle(a, c, c + 1);
                }
            b.RecalculateNormals(start);
            // Markings: painted strips a hair above the asphalt.
            float paintLift = lift + 0.0003f;
            Strip(b, l, hw - KerbWidth - 0.0016f, 0.0011f, Paint, paintLift, 0f, 0f);    // edge lines
            Strip(b, l, -(hw - KerbWidth - 0.0016f), 0.0011f, Paint, paintLift, 0f, 0f);
            if (highway)
            {
                Strip(b, l, 0.0012f, 0.0009f, Yellow, paintLift, 0f, 0f);                    // the double yellow line
                Strip(b, l, -0.0012f, 0.0009f, Yellow, paintLift, 0f, 0f);
                Strip(b, l, 0.021f, 0.001f, Paint, paintLift, 0.018f, 0.014f);                // lane lines
                Strip(b, l, -0.021f, 0.001f, Paint, paintLift, 0.018f, 0.014f);
            }
            else Strip(b, l, 0f, 0.0011f, Paint, paintLift, 0.016f, 0.012f);                  // the dashed centre line
            // Street lamps along a street now and then, on alternating sides.
            if (!highway)
                for (int i = 40, side = 1; i < n - 10; i += 70, side = -side)
                {
                    var p = l.points[i];
                    if (InJunction(p, 0.03f)) continue;
                    var t = Tangent(l, i);
                    var q = p + new Vector2(t.y, -t.x) * side * (hw + 0.004f);
                    float y = RoadHeight(q, out bool deck);
                    if (deck) continue;
                    props.AddCylinder(new Vector3(q.x, y, q.y), 0.0009f, 0.0007f, 0.04f, 5, Pal.Hex("#3A3F46"), Pal.Hex("#3A3F46"), false);
                    props.AddBlob(new Vector3(q.x, y + 0.041f, q.y), new Vector3(0.0028f, 0.0018f, 0.0028f), 1, 0f, i, (_, __) => Pal.Glow(Pal.Hex("#FFE9B0")));
                }
        }

        private static Vector2 Tangent(PathLine l, int i)
        {
            int n = l.points.Count;
            var t = l.points[l.closed ? (i + 1) % n : Mathf.Min(i + 1, n - 1)] - l.points[l.closed ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)];
            return t.sqrMagnitude > 1e-10f ? t.normalized : Vector2.up;
        }

        /// <summary>
        /// A painted line along a road at <paramref name="offset"/> from its middle: solid, or dashed (<paramref name="dash"/>
        /// painted, <paramref name="gap"/> bare); left out inside junctions.
        /// </summary>
        private void Strip(MeshBuilder b, PathLine l, float offset, float width, Color colour, float lift, float dash, float gap)
        {
            int n = l.points.Count;
            float along = 0f;
            int last = l.closed ? n : n - 1;
            for (int i = 0; i < last; i++)
            {
                var p0 = l.points[i]; var p1 = l.points[(i + 1) % n];
                float seg = (p1 - p0).magnitude;
                float mid = along + seg * 0.5f;
                along += seg;
                if (dash > 0f && (mid % (dash + gap)) > dash) continue;
                if (InJunction(p0, 0.006f) || InJunction(p1, 0.006f)) continue;
                var t0 = Tangent(l, i); var t1 = Tangent(l, (i + 1) % n);
                var r0 = new Vector2(t0.y, -t0.x); var r1 = new Vector2(t1.y, -t1.x);
                var a0 = p0 + r0 * (offset - width / 2); var b0 = p0 + r0 * (offset + width / 2);
                var a1 = p1 + r1 * (offset - width / 2); var b1 = p1 + r1 * (offset + width / 2);
                Vector3 V(Vector2 q) => new(q.x, RoadHeight(q, out _) + lift, q.y);
                var va = V(a0); var vb = V(b0); var vc = V(b1); var vd = V(a1);
                if (Vector3.Cross(vb - va, vc - va).y >= 0f) b.AddQuad(va, vb, vc, vd, colour); else b.AddQuad(va, vd, vc, vb, colour);
            }
        }

        /// <summary>The junction patches (plain asphalt over the meeting roads), stop lines, and the traffic light poles.</summary>
        private void JunctionSurfaces(MeshBuilder b, MeshBuilder props)
        {
            for (int ji = 0; ji < Junctions.Count; ji++)
            {
                var j = Junctions[ji];
                float y = RoadHeight(j.At, out _) + 0.0019f;
                // A rounded patch big enough to cover the arms' full width where they enter.
                const int seg = 24;
                int centre = b.AddVertex(new Vector3(j.At.x, y, j.At.y), Vector3.up, Asphalt);
                for (int k = 0; k <= seg; k++)
                {
                    float a = k / (float)seg * Mathf.PI * 2f;
                    var q = j.At + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (j.Radius + 0.002f);
                    b.AddVertex(new Vector3(q.x, RoadHeight(q, out _) + 0.0019f, q.y), Vector3.up, Asphalt);
                    if (k > 0) b.AddTriangle(centre, centre + k + 1, centre + k);
                }
                for (int arm = 0; arm < j.Arms.Count; arm++)
                {
                    var d = j.Arms[arm];
                    float hw = j.ArmHalfWidth[arm];
                    // The incoming lanes (on the right of a car driving in): a stop line across them.
                    var right = new Vector2(-d.y, d.x);   // right-hand side for traffic heading into the junction (along -d)
                    var line = j.At + d * (j.Radius + 0.004f);
                    var a0 = line + right * 0.0008f; var a1 = line + right * (hw - KerbWidth - 0.001f);
                    var w = d * 0.0014f;
                    Vector3 V(Vector2 q) => new(q.x, RoadHeight(q, out _) + 0.0023f, q.y);
                    var va = V(a0 - w); var vb = V(a1 - w); var vc = V(a1 + w); var vd = V(a0 + w);
                    if (Vector3.Cross(vb - va, vc - va).y >= 0f) b.AddQuad(va, vb, vc, vd, Paint); else b.AddQuad(va, vd, vc, vb, Paint);
                    if (!j.Lights) continue;
                    // A traffic light on the kerb at the stop line, facing the cars coming in.
                    var pole = line + d * 0.006f + right * (hw + 0.003f);
                    float py = RoadHeight(pole, out _);
                    props.AddCylinder(new Vector3(pole.x, py, pole.y), 0.0009f, 0.0008f, 0.034f, 6, Pal.Hex("#2E3238"), Pal.Hex("#2E3238"), false);
                    var facing = new Vector3(d.x, 0f, d.y);
                    var head = new Vector3(pole.x, py + 0.036f, pole.y);
                    props.Push(head, Quaternion.LookRotation(-facing, Vector3.up));
                    props.AddBox(new Vector3(0f, 0f, 0.0012f), new Vector3(0.0055f, 0.0145f, 0.0035f), Pal.Hex("#24272C"));
                    props.Pop();
                    LightHeads.Add((ji, arm, head, facing));
                }
            }
        }
    }
}
