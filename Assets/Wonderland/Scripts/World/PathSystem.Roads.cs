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

        /// <summary>The standard curves of a road on the grid: its own corner, and gentler ones in multiples of it.</summary>
        private static readonly float[] StreetRadii = { 0.05f, 0.1f, 0.15f, 0.2f, 0.3f, 0.4f }, HighwayRadii = { 0.18f, 0.27f, 0.36f, 0.45f, 0.6f };

        /// <summary>
        /// A road on the grid, read the way grid track is (<see cref="TrackPlanner.GridShapes"/>): straight lines fitted to
        /// whole stretches of the stroke and put on the grid - along it, across it or at 45 degrees - so a stroke that wobbles
        /// or drifts a little comes out dead straight, and corners only where the stroke really turns, rounded with a
        /// standard curve. Drawn on from a free end of a road of the same kind, it carries on along that road's line and
        /// becomes part of it (<see cref="Join"/>); an end on another road's side lands exactly on it (a junction).
        /// </summary>
        private PathLine PlanGridRoad(List<Vector2> raw, int kind, bool closed, out string error)
        {
            error = null;
            var line = new PathLine { kind = kind, closed = closed };
            TrackAttach start = default, finish = default;
            if (!closed && FreeEndNear(raw[0], Heading(raw, true), kind, -1, false, out line.extendsStart, out line.startAtItsStart, out var a, out var outA))
            {
                start = new TrackAttach { Kind = AttachKind.End, Point = a, Dir = outA };
                // (what was drawn over the old road, before reaching its end, is not new road)
                int cut = 0;
                while (cut < raw.Count - 2 && Vector2.Dot(raw[cut] - a, outA) < 0.005f) cut++;
                if (cut > 0) { raw.RemoveRange(0, cut); raw.Insert(0, a); }
            }
            if (!closed && FreeEndNear(raw[^1], -Heading(raw, false), kind, line.extendsStart, line.startAtItsStart, out line.extendsEnd, out line.endAtItsStart, out var b, out var outB))
            {
                finish = new TrackAttach { Kind = AttachKind.End, Point = b, Dir = -outB };
                int cut = 0;
                while (cut < raw.Count - 2 && Vector2.Dot(raw[raw.Count - 1 - cut] - b, outB) < 0.005f) cut++;
                if (cut > 0) { raw.RemoveRange(raw.Count - cut, cut); raw.Add(b); }
            }
            var shapes = TrackPlanner.GridShapes(raw, closed, null, start, finish, kind == (int)PathKind.Highway ? HighwayRadii : StreetRadii);
            if (shapes.Count == 0 || shapes[0].Error != null)
            {
                error = shapes.Count == 0 || shapes[0].Error == "Draw a longer track" ? "Draw a little longer" : shapes[0].Error.Replace("for the train", "for the road");
                return null;
            }
            var pts = shapes[0].Path;
            // An end on the side of another road (a T or a corner) lands exactly on it.
            if (!closed && start.Kind == AttachKind.None && SnapRoad(raw[0], out _)) OntoRoad(pts, true);
            if (!closed && finish.Kind == AttachKind.None && SnapRoad(raw[^1], out _)) OntoRoad(pts, false);
            foreach (var p in pts)
                if (terrain.EdgeAt(p.x, p.y) < 0.35f) { error = "Keep the road on the land"; return null; }
            line.points = pts;
            return line;
        }

        /// <summary>
        /// A free end of a road of this kind near <paramref name="p"/> (not where it meets another road; not
        /// <paramref name="notLine"/>'s end <paramref name="notStart"/>): which line, which end, where, and the way out of it.
        /// </summary>
        /// <summary>The way a stroke heads over its first (or towards its last) 8 cm.</summary>
        private static Vector2 Heading(List<Vector2> raw, bool start)
        {
            var from = start ? raw[0] : raw[^1];
            for (int k = 1; k < raw.Count; k++)
            {
                var q = start ? raw[k] : raw[raw.Count - 1 - k];
                if ((q - from).magnitude > 0.08f || k == raw.Count - 1) return start ? (q - from).normalized : (from - q).normalized;
            }
            return Vector2.zero;
        }

        /// <summary>
        /// A free end of a road of this kind that a stroke end at <paramref name="p"/> carries on from: close to it, or a
        /// little way back along the road with the stroke heading out towards it (<paramref name="heading"/>: the way the
        /// stroke runs from <paramref name="p"/>, away from the rest of it... or for the stroke's last point, back along it).
        /// </summary>
        private bool FreeEndNear(Vector2 p, Vector2 heading, int kind, int notLine, bool notStart, out int line, out bool atStart, out Vector2 at, out Vector2 outward)
        {
            line = -1; atStart = false; at = default; outward = default;
            float best = Snap * 1.4f;
            for (int li = 0; li < Lines.Count; li++)
            {
                var l = Lines[li];
                if (l.kind != kind || l.closed || l.points.Count < 8) continue;
                for (int k = 0; k < 2; k++)
                {
                    if (li == notLine && (k == 0) == notStart) continue;
                    var e = k == 0 ? l.points[0] : l.points[^1];
                    var o = (e - (k == 0 ? l.points[5] : l.points[^6])).normalized;
                    float d = (e - p).magnitude;
                    // Or on the road a little way back from its end, the stroke heading out over it towards the end.
                    float back = Vector2.Dot(e - p, o);
                    bool behind = back > 0f && back < 0.35f && Distance(l, p) < HalfWidth(kind) + 0.01f && Vector2.Dot(heading, o) > 0.8f;
                    if (behind) d = Mathf.Min(d, Snap);
                    if (d >= best) continue;
                    int me = li;
                    if (Query(e, 0.02f, m => m != me && IsCarWay(Lines[m].kind), out _) < 0.012f) continue;   // (a junction, not a free end)
                    best = d; line = li; atStart = k == 0; at = e; outward = o;
                }
            }
            return line >= 0;
        }

        /// <summary>Moves a road's end along its own line until it lies exactly on the road it meets (within 8 cm).</summary>
        private void OntoRoad(List<Vector2> pts, bool atStart)
        {
            if (pts.Count < 6) return;
            if (!atStart) pts.Reverse();
            var end = pts[0];
            var inward = (pts[4] - pts[0]).normalized;
            float bestT = 0f, bestD = float.MaxValue;
            for (float t = -0.08f; t <= 0.08f; t += 0.002f)
            {
                float d = Query(end - inward * t, 0.1f, m => IsCarWay(Lines[m].kind), out _);
                if (d < bestD) { bestD = d; bestT = t; }
            }
            if (bestD < 0.004f)
            {
                var to = end - inward * bestT;
                while (pts.Count > 2 && Vector2.Dot(pts[0] - to, inward) < Spacing * 0.5f) pts.RemoveAt(0);   // (cut back to it...)
                var from = pts[0];
                int steps = Mathf.CeilToInt((from - to).magnitude / Spacing);
                for (int k = 1; k < steps; k++) pts.Insert(0, Vector2.Lerp(from, to, k / (float)steps));   // (...or carried on to it)
                pts.Insert(0, to);
            }
            if (!atStart) pts.Reverse();
        }

        /// <summary>A road from a stroke: ends snapped, straight stretches with rounded corners, every 1 cm.</summary>
        private PathLine PlanRoad(List<Vector2> raw, int kind, bool closed, out string error)
        {
            if (BuildGrid.Snap) return PlanGridRoad(raw, kind, closed, out error);
            error = null;
            bool joinA = false, joinB = false;
            if (!closed)
            {
                if (joinA = SnapRoad(raw[0], out var a)) raw[0] = a;
                if (joinB = SnapRoad(raw[^1], out var b)) raw[^1] = b;
            }
            float tolerance = kind == (int)PathKind.Highway ? 0.03f : 0.018f;
            var corners = closed ? Polyline.ClosedSimplify(raw, tolerance) : Polyline.Simplify(raw, tolerance);
            // Corners closer together than a road's width are hand jitter: merge them.
            for (int i = corners.Count - 2; i >= 1; i--)
                if ((corners[i] - corners[i - 1]).magnitude < 0.04f && corners.Count > (closed ? 3 : 2)) corners.RemoveAt(i);
            // A road never turns straight back on itself (an end snapped onto a road behind the last corner, or the hand
            // doubling back): such a corner goes.
            for (int i = corners.Count - 2; i >= 1 && corners.Count > (closed ? 3 : 2); i--)
                if (Vector2.Angle(corners[i] - corners[i - 1], corners[i + 1] - corners[i]) > 150f) corners.RemoveAt(i);
            if (closed && corners.Count < 3) { error = "Draw a bigger ring road"; return null; }
            var pts = Fillet(corners, closed, kind == (int)PathKind.Highway ? HighwayRadius : StreetRadius);
            pts = Polyline.Resample(pts, Spacing, closed);
            if (!closed) { pts[0] = raw[0]; pts[^1] = raw[^1]; }
            foreach (var p in pts)
                if (terrain.EdgeAt(p.x, p.y) < 0.35f) { error = "Keep the road on the land"; return null; }
            return new PathLine { kind = kind, points = pts, closed = closed };
        }

        /// <summary>
        /// Where a building of half-depth <paramref name="depth"/> goes beside the road nearest <paramref name="p"/>
        /// (within reach): just behind the kerb on the pointer's side, its front (-Z) facing the road, stepped along it on
        /// the grid. False when no road (or lane) is near.
        /// </summary>
        public bool AlongRoad(Vector2 p, float depth, out Vector2 at, out float yaw)
        {
            at = p; yaw = 0f;
            float d = Query(p, 0.16f, k => IsCarWay(Lines[k].kind), out int li);
            if (li < 0) return false;
            var l = Lines[li];
            float hw = HalfWidth(l.kind);
            if (d > hw + depth * 2f + 0.03f) return false;
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < l.points.Count; i++) { float q2 = (l.points[i] - p).sqrMagnitude; if (q2 < bestD) { bestD = q2; best = i; } }
            var t = Tangent(l, best);
            var q = l.points[best] + t * Vector2.Dot(p - l.points[best], t);
            var right = new Vector2(t.y, -t.x);
            var normal = Vector2.Dot(p - q, right) >= 0f ? right : -right;
            float s = Vector2.Dot(q, t);
            q += t * (Mathf.Round(s / BuildGrid.Size) * BuildGrid.Size - s);
            at = q + normal * (hw + Pavement(l.kind) + depth + 0.006f);
            yaw = Mathf.Atan2(normal.x, normal.y) * Mathf.Rad2Deg;   // (a building's front is its -Z: it looks back along -normal, at the road)
            return true;
        }

        /// <summary>The point on the street nearest <paramref name="p"/> (within reach) and the way along it: a crossing's yaw.</summary>
        public bool AcrossRoad(Vector2 p, out Vector2 at, out float yaw)
        {
            at = p; yaw = 0f;
            float d = Query(p, 0.08f, k => IsCarWay(Lines[k].kind) && Lines[k].kind != (int)PathKind.Highway, out int li);
            if (li < 0 || d > HalfWidth(Lines[li].kind) + 0.04f) return false;
            var l = Lines[li];
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < l.points.Count; i++) { float q2 = (l.points[i] - p).sqrMagnitude; if (q2 < bestD) { bestD = q2; best = i; } }
            var t = Tangent(l, best);
            at = l.points[best];
            yaw = Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg;   // (its local Z along the street, its long side - local X - across it)
            return true;
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
                if (turn > 170f * Mathf.Deg2Rad) continue;   // (turning straight back: no corner to round - tan(90 deg) would fling it away)
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

        /// <summary>Whether a point is in (or within <paramref name="margin"/> of) a junction where three or more roads meet.</summary>
        public bool NearJunction(Vector2 p, float margin) => InJunction(p, margin);

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

        /// <summary>A point along a road for its mesh: where, which way, how high the (flat) road surface is there, how far along.</summary>
        private struct RoadSample { public Vector2 P, T; public float Y, S; public bool Junction; }

        private static readonly Color Paving = new(0.78f, 0.75f, 0.7f);
        private const float KerbHeight = 0.0012f, Skirt = 0.004f, RoadLift = 0.0016f;

        /// <summary>
        /// The road's centre line smoothed for drawing: between the 1 cm points, extra samples on a Catmull-Rom curve
        /// wherever it bends (no more than ~3 degrees a step, so the kerbs of a tight corner are round, not faceted).
        /// Each sample carries the height of a flat cross-section: the highest ground across the road, so no bump of
        /// the land shows through it (on a bridge, the deck).
        /// </summary>
        private List<RoadSample> RoadSamples(PathLine l)
        {
            var pts = l.points;
            int n = pts.Count;
            var dense = new List<Vector2>(n * 2);
            Vector2 P(int i) => l.closed ? pts[(i % n + n) % n] : pts[Mathf.Clamp(i, 0, n - 1)];
            int segs = l.closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
                float bend = Vector2.Angle(p1 - p0, p2 - p1) + Vector2.Angle(p2 - p1, p3 - p2);
                int sub = Mathf.Clamp(Mathf.CeilToInt(bend * 0.5f / 3f), 1, 8);
                for (int k = 0; k < sub; k++)
                {
                    float t = k / (float)sub, t2 = t * t, t3 = t2 * t;
                    dense.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            if (!l.closed) dense.Add(pts[n - 1]);
            float hw = HalfWidth(l.kind);
            var samples = new List<RoadSample>(dense.Count);
            float s = 0f;
            for (int i = 0; i < dense.Count; i++)
            {
                int m = dense.Count;
                var t = (l.closed ? dense[(i + 1) % m] - dense[(i - 1 + m) % m] : dense[Mathf.Min(i + 1, m - 1)] - dense[Mathf.Max(i - 1, 0)]);
                t = t.sqrMagnitude > 1e-12f ? t.normalized : Vector2.up;
                var right = new Vector2(t.y, -t.x);
                float y = float.MinValue;
                for (int k = -2; k <= 2; k++) y = Mathf.Max(y, RoadHeight(dense[i] + right * (hw * 0.5f * k), out _));
                if (i > 0) s += (dense[i] - dense[i - 1]).magnitude;
                samples.Add(new RoadSample { P = dense[i], T = t, Y = y, S = s, Junction = InJunction(dense[i], 0.004f) });
            }
            // A gentle smoothing along the road, never lowering it (bumps in the ground stay underneath).
            var ys = new float[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                float best = samples[i].Y;
                for (int k = -3; k <= 3; k++) { int j = l.closed ? (i + k + samples.Count) % samples.Count : Mathf.Clamp(i + k, 0, samples.Count - 1); best = Mathf.Max(best, samples[j].Y - Mathf.Abs(k) * 0.0006f); }
                ys[i] = best;
            }
            for (int i = 0; i < samples.Count; i++) { var r = samples[i]; r.Y = ys[i]; samples[i] = r; }
            return samples;
        }

        /// <summary>
        /// A road's surface: flat asphalt between kerbs (with a skirt under each kerb so no gap shows on a slope), white
        /// edge lines, and a dashed centre line (a highway: lane lines and a double yellow line). Markings and kerbs are
        /// left out inside junctions.
        /// </summary>
        private void RoadSurface(MeshBuilder b, MeshBuilder props, PathLine l)
        {
            if (l.points.Count < 2) return;
            float hw = HalfWidth(l.kind);
            bool highway = l.kind == (int)PathKind.Highway;
            var samples = RoadSamples(l);
            // Cross-section strips, left to right: skirt, pavement, kerb top, kerb face, asphalt, kerb face, kerb top,
            // pavement, skirt (offset, height above the road, and the next corner's) - kerbs drop to road level inside
            // junctions. Streets and lanes have a pavement either side for people to walk on; highways don't.
            float pw = Pavement(l.kind);
            (float x0, float h0, float x1, float h1, bool kerb)[] strips =
            {
                (-hw - pw, -Skirt, -hw - pw, KerbHeight, true), (-hw - pw, KerbHeight, -hw, KerbHeight, true), (-hw, KerbHeight, -hw + KerbWidth, KerbHeight, true), (-hw + KerbWidth, KerbHeight, -hw + KerbWidth, 0f, true),
                (-hw + KerbWidth, 0f, hw - KerbWidth, 0f, false),
                (hw - KerbWidth, 0f, hw - KerbWidth, KerbHeight, true), (hw - KerbWidth, KerbHeight, hw, KerbHeight, true), (hw, KerbHeight, hw + pw, KerbHeight, true), (hw + pw, KerbHeight, hw + pw, -Skirt, true),
            };
            int count = samples.Count, last = l.closed ? count : count - 1;
            foreach (var (x0, h0, x1, h1, kerb) in strips)
            {
                int start = b.VertexCount;
                for (int i = 0; i < count; i++)
                {
                    var sm = samples[i];
                    var right = new Vector2(sm.T.y, -sm.T.x);
                    bool flat = sm.Junction;   // inside a junction: all asphalt at road level
                    float a = flat ? Mathf.Min(h0, 0f) : h0, c = flat ? Mathf.Min(h1, 0f) : h1;
                    float y = sm.Y + RoadLift;
                    bool paving = Mathf.Abs(x0) >= hw - 1e-5f && Mathf.Abs(x1) >= hw - 1e-5f && pw > 0f;
                    var colour = kerb && !flat ? (paving ? Pal.Shade(Paving, 0.94f + 0.08f * Noise.Hash(i / 2, (int)(x0 * 1000f), 11)) : Pal.Shade(KerbStone, 0.95f + 0.1f * Noise.Hash(i / 4, (int)(x0 * 1000f), 7))) : Pal.Shade(Asphalt, 0.97f + 0.05f * Noise.Hash(i / 3, 1, 3));
                    var q0 = sm.P + right * x0; var q1 = sm.P + right * x1;
                    var p0 = new Vector3(q0.x, y + a, q0.y); var p1 = new Vector3(q1.x, y + c, q1.y);
                    var along = new Vector3(sm.T.x, 0f, sm.T.y);
                    var normal = Vector3.Cross(p1 - p0, along);
                    if (normal.sqrMagnitude < 1e-14f) normal = Vector3.up; else normal.Normalize();
                    if (Vector3.Dot(normal, Vector3.up) < -0.01f || (Mathf.Abs(normal.y) < 0.01f && Vector3.Dot(normal, new Vector3(right.x, 0f, right.y) * Mathf.Sign(x0 + x1 + 1e-6f)) < 0f)) normal = -normal;
                    b.AddVertex(p0, normal, colour); b.AddVertex(p1, normal, colour);
                }
                for (int i = 0; i < last; i++)
                {
                    int i0 = start + i * 2, i1 = start + ((i + 1) % count) * 2;
                    var v0 = b.Vertices[i0]; var v1 = b.Vertices[i0 + 1]; var v2 = b.Vertices[i1];
                    // Wound so the face points along its normal.
                    if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), b.Normals[i0]) >= 0f) { b.AddTriangle(i0, i0 + 1, i1 + 1); b.AddTriangle(i0, i1 + 1, i1); }
                    else { b.AddTriangle(i0, i1 + 1, i0 + 1); b.AddTriangle(i0, i1, i1 + 1); }
                }
            }
            // Markings, painted a hair above the asphalt, measured exactly along the road.
            float edge = hw - KerbWidth - 0.0016f;
            PaintLine(b, samples, l.closed, edge, 0.0011f, Paint, 0f, 0f);
            PaintLine(b, samples, l.closed, -edge, 0.0011f, Paint, 0f, 0f);
            if (highway)
            {
                PaintLine(b, samples, l.closed, 0.0012f, 0.0009f, Yellow, 0f, 0f);
                PaintLine(b, samples, l.closed, -0.0012f, 0.0009f, Yellow, 0f, 0f);
                PaintLine(b, samples, l.closed, 0.021f, 0.001f, Paint, 0.018f, 0.014f);
                PaintLine(b, samples, l.closed, -0.021f, 0.001f, Paint, 0.018f, 0.014f);
            }
            else PaintLine(b, samples, l.closed, 0f, 0.0011f, Paint, 0.016f, 0.012f);
            // Street lamps along a street now and then, on alternating sides.
            if (!highway)
            {
                int side = 1;
                for (float at = 0.4f; at < samples[^1].S - 0.1f; at += 0.7f, side = -side)
                {
                    int i = samples.FindIndex(x => x.S >= at);
                    if (i < 0 || samples[i].Junction || InJunction(samples[i].P, 0.03f)) continue;
                    var sm = samples[i];
                    var q = sm.P + new Vector2(sm.T.y, -sm.T.x) * side * (hw + 0.004f);
                    float y = RoadHeight(q, out bool deck);
                    if (deck) continue;
                    props.AddCylinder(new Vector3(q.x, y, q.y), 0.0009f, 0.0007f, 0.04f, 5, Pal.Hex("#3A3F46"), Pal.Hex("#3A3F46"), false);
                    props.AddBlob(new Vector3(q.x, y + 0.041f, q.y), new Vector3(0.0028f, 0.0018f, 0.0028f), 1, 0f, i, (_, __) => Pal.Glow(Pal.Hex("#FFE9B0")));
                }
            }
        }

        private static Vector2 Tangent(PathLine l, int i)
        {
            int n = l.points.Count;
            var t = l.points[l.closed ? (i + 1) % n : Mathf.Min(i + 1, n - 1)] - l.points[l.closed ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)];
            return t.sqrMagnitude > 1e-10f ? t.normalized : Vector2.up;
        }

        /// <summary>
        /// A painted line along the road at <paramref name="offset"/> from its middle, as one continuous strip: solid, or
        /// dashed (<paramref name="dash"/> painted, <paramref name="gap"/> bare, measured exactly along the road, so every
        /// dash is the same length on a straight or a bend). Left out inside junctions.
        /// </summary>
        private static void PaintLine(MeshBuilder b, List<RoadSample> samples, bool closed, float offset, float width, Color colour, float dash, float gap)
        {
            const float lift = RoadLift + 0.0003f;
            int count = samples.Count, last = closed ? count : count - 1;
            float period = dash + gap;
            bool On(float s) => dash <= 0f || Mathf.Repeat(s, period) < dash;
            Vector3 Edge(RoadSample sm, float side)
            {
                var right = new Vector2(sm.T.y, -sm.T.x);
                var q = sm.P + right * (offset + side * width / 2);
                return new Vector3(q.x, sm.Y + lift, q.y);
            }
            RoadSample Lerp(RoadSample a, RoadSample c, float t) => new()
            {
                P = Vector2.Lerp(a.P, c.P, t), T = Vector2.Lerp(a.T, c.T, t).normalized, Y = Mathf.Lerp(a.Y, c.Y, t), S = Mathf.Lerp(a.S, c.S, t),
            };
            void Quad(RoadSample a, RoadSample c)
            {
                var va = Edge(a, -1f); var vb = Edge(a, 1f); var vc = Edge(c, 1f); var vd = Edge(c, -1f);
                if (Vector3.Cross(vb - va, vc - va).y >= 0f) b.AddQuad(va, vb, vc, vd, colour); else b.AddQuad(va, vd, vc, vb, colour);
            }
            for (int i = 0; i < last; i++)
            {
                var a = samples[i]; var c = samples[(i + 1) % count];
                if (a.Junction || c.Junction) continue;
                float s0 = a.S, s1 = closed && i == count - 1 ? a.S + (c.P - a.P).magnitude : c.S;
                if (s1 - s0 < 1e-6f) continue;
                if (dash <= 0f) { Quad(a, c); continue; }
                // Split this piece where dashes start and end.
                // (in steps of at least 0.05 mm: a dash edge landing exactly on a sample never stalls the walk)
                float from = s0;
                for (int guard = 0; from < s1 - 1e-5f && guard < 64; guard++)
                {
                    float phase = Mathf.Repeat(from, period);
                    float to = Mathf.Min(s1, from + Mathf.Max(0.00005f, phase < dash ? dash - phase : period - phase));
                    if (On(from + 0.00002f)) Quad(Lerp(a, c, (from - s0) / (s1 - s0)), Lerp(a, c, (to - s0) / (s1 - s0)));
                    from = to;
                }
            }
        }

        /// <summary>The junction patches (plain asphalt over the meeting roads), stop lines, and the traffic light poles.</summary>
        private void JunctionSurfaces(MeshBuilder b, MeshBuilder props)
        {
            for (int ji = 0; ji < Junctions.Count; ji++)
            {
                var j = Junctions[ji];
                // One flat patch, at the highest ground under it (like the roads' flat cross-sections), just above them.
                float y = RoadHeight(j.At, out _);
                for (int k = 0; k < 12; k++) foreach (float f in new[] { 0.5f, 1f }) { float ang = k / 12f * Mathf.PI * 2f; y = Mathf.Max(y, RoadHeight(j.At + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * j.Radius * f, out _)); }
                y += RoadLift + 0.0002f;
                // A rounded patch big enough to cover the arms' full width where they enter.
                const int seg = 24;
                int centre = b.AddVertex(new Vector3(j.At.x, y, j.At.y), Vector3.up, Asphalt);
                for (int k = 0; k <= seg; k++)
                {
                    float a = k / (float)seg * Mathf.PI * 2f;
                    var q = j.At + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (j.Radius + 0.002f);
                    b.AddVertex(new Vector3(q.x, y, q.y), Vector3.up, Asphalt);
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
                    Vector3 V(Vector2 q) => new(q.x, y + 0.0004f, q.y);
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
