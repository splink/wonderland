using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>A finished railway: a 1 cm polyline in world-root space whose y is the top of the ballast bed.</summary>
    public sealed class TrackLayout
    {
        public readonly List<Vector3> Points;
        public readonly bool Closed;
        public readonly float[] Cum;
        public readonly float Length;

        public TrackLayout(List<Vector3> points, bool closed)
        {
            Points = points; Closed = closed;
            Cum = new float[points.Count];
            for (int i = 1; i < points.Count; i++) Cum[i] = Cum[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            Length = Cum[^1] + (closed ? Vector3.Distance(points[^1], points[0]) : 0f);
        }

        public float Wrap(float s) => Closed ? Mathf.Repeat(s, Length) : Mathf.Clamp(s, 0f, Length);

        public Vector3 Evaluate(float s, out Vector3 tangent)
        {
            s = Wrap(s);
            int lo = 0, hi = Points.Count - 1;
            if (s >= Cum[hi]) lo = hi;
            else
            {
                while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (Cum[mid] <= s) lo = mid; else hi = mid; }
            }
            int next = lo + 1;
            if (next >= Points.Count) next = Closed ? 0 : Points.Count - 1;
            if (next == lo) { tangent = (Points[lo] - Points[lo - 1]).normalized; return Points[lo]; }
            float seg = Closed && next == 0 ? Length - Cum[lo] : Cum[next] - Cum[lo];
            float t = seg > 1e-6f ? (s - Cum[lo]) / seg : 0f;
            tangent = (Points[next] - Points[lo]).normalized;
            return Vector3.Lerp(Points[lo], Points[next], t);
        }

        /// <summary>Arc length of the point nearest to <paramref name="p"/> (horizontal distance).</summary>
        public float Nearest(Vector3 p, out float distance)
        {
            float best = float.MaxValue, bestS = 0f;
            int count = Closed ? Points.Count : Points.Count - 1;
            for (int i = 0; i < count; i++)
            {
                var a = Points[i]; var b = Points[(i + 1) % Points.Count];
                var ab = new Vector2(b.x - a.x, b.z - a.z); var ap = new Vector2(p.x - a.x, p.z - a.z);
                float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
                float d = (ap - ab * t).sqrMagnitude;
                if (d < best) { best = d; bestS = Cum[i] + t * ab.magnitude; }
            }
            distance = Mathf.Sqrt(best);
            return bestS;
        }
    }

    public enum AttachKind { None, End, Mid }

    /// <summary>Where a stroke meets existing track: at a dead end (extending it) or along a piece (a new switch).</summary>
    public struct TrackAttach
    {
        public AttachKind Kind;
        public EdgeEnd End;      // End: the dead end
        public int Edge;         // Mid: the piece
        public float S;          // Mid: where along it
        public Vector2 Point;    // the joint position
        public Vector2 Dir;      // start: direction the new track leaves in; finish: direction it arrives in
    }

    public sealed class TrackPlan
    {
        public List<Vector2> Path;          // 1 cm centre line of the new piece (heights come from the network profile)
        public bool Closed;
        public TrackAttach Start, Finish;
        public string Error;
        public List<Vector3> Smoothed;      // coarse centre line, for feedback even when invalid
        public bool Ok => Error == null;
    }

    /// <summary>
    /// Turns a hand-drawn stroke into a new piece of railway. Everything you draw connects: a stroke that starts or ends
    /// at a dead end extends that line, one that starts or ends on a piece of track branches off it or joins it with a
    /// switch (the new track is bent to leave or meet it smoothly), one that crosses track gets a diamond crossing, and a
    /// free stroke that comes back to its start becomes a loop. The stroke is smoothed until every curve is at least
    /// <see cref="MinRadius"/>; the network then gives every piece a smooth height profile limited to <see cref="MaxGrade"/>.
    /// </summary>
    public static class TrackPlanner
    {
        public const float MinRadius = 0.18f;
        public const float MaxGrade = 0.09f;   // steep for real railways, fine for a storybook train; lets track follow the land
        public const float CloseDistance = 0.3f;
        public const float Spacing = 0.01f;
        public const float Coarse = 0.03f;
        public const float MinLength = 0.5f;
        public const float MinConnectorLength = 0.2f;
        public const float MinLoopLength = 0.9f;
        public const float SnapEnd = 0.09f, SnapMid = 0.07f;
        public const float MinJointGap = 0.22f;     // switches and crossings keep this far apart along the track
        public const float TrackSpacing = 0.088f;   // parallel tracks at least this far apart (centre to centre)
        public const float MinCrossingAngle = 22f;

        public static float Length(IReadOnlyList<Vector3> pts)
        {
            float l = 0f;
            for (int i = 1; i < pts.Count; i++) l += Flat(pts[i] - pts[i - 1]).magnitude;
            return l;
        }

        /// <summary>
        /// Whether releasing now would close the loop: a free stroke, long enough, whose end region comes back within
        /// <see cref="CloseDistance"/> of its start region (ending short of the start or overshooting past it).
        /// </summary>
        public static bool WouldClose(IReadOnlyList<Vector3> drawn, TrackNetwork network = null)
        {
            if (drawn.Count < 3 || Length(drawn) < MinLoopLength) return false;
            if (network != null && !network.Empty)
            {
                var a = Attach(network, drawn, true, out _); var b = Attach(network, drawn, false, out _);
                if (a.Kind != AttachKind.None || b.Kind != AttachKind.None) return false;
            }
            var pts = new List<Vector2>(drawn.Count);
            foreach (var p in drawn) pts.Add(new Vector2(p.x, p.z));
            return FindLoop(pts, out _, out _);
        }

        /// <summary>Where the start (or the end) of a stroke would attach to the network.</summary>
        public static TrackAttach Attach(TrackNetwork network, IReadOnlyList<Vector3> drawn, bool start, out string error)
        {
            error = null;
            var none = new TrackAttach { Kind = AttachKind.None };
            if (network == null || network.Empty || drawn.Count == 0) return none;
            var p = drawn[start ? 0 : drawn.Count - 1];
            // Direction of the stroke as it leaves the start (or arrives at the end), over its first (last) 6 cm.
            var dir = Vector2.zero;
            if (start) { for (int i = 1; i < drawn.Count; i++) { var d = Flat(drawn[i] - p); if (d.magnitude > 0.06f || i == drawn.Count - 1) { dir = d; break; } } }
            else { for (int i = drawn.Count - 2; i >= 0; i--) { var d = Flat(p - drawn[i]); if (d.magnitude > 0.06f || i == 0) { dir = d; break; } } }
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector2.up;
            var p2 = Flat(p);
            // A dead end nearby: extend (or join) it in line.
            float bestEnd = SnapEnd; var deadEnd = new EdgeEnd(-1, 0);
            for (int e = 0; e < network.Edges.Count; e++)
            {
                if (network.Edges[e].Closed) continue;
                for (int k = 0; k < 2; k++)
                {
                    if (network.Edges[e].Joint[k] >= 0) continue;
                    float d = (Flat(network.EndPoint(new EdgeEnd(e, k))) - p2).magnitude;
                    if (d < bestEnd) { bestEnd = d; deadEnd = new EdgeEnd(e, k); }
                }
            }
            if (deadEnd.Edge >= 0) return EndAttach(network, deadEnd, start);
            if (!network.Nearest(p, out int edge, out float s, out _, SnapMid)) return none;
            var layout = network.Edges[edge].Layout;
            // Close to a dead end along the piece: extend it instead.
            if (!layout.Closed)
                for (int k = 0; k < 2; k++)
                    if (network.Edges[edge].Joint[k] < 0 && (k == 0 ? s : layout.Length - s) < 0.12f) return EndAttach(network, new EdgeEnd(edge, k), start);
            // Keep clear of switches and crossings: slide along the piece, or give up when there is no room.
            float gap0 = network.Edges[edge].Joint[0] >= 0 ? s : float.MaxValue, gap1 = network.Edges[edge].Joint[1] >= 0 ? layout.Length - s : float.MaxValue;
            if (gap0 < MinJointGap) s = MinJointGap;
            else if (gap1 < MinJointGap) s = layout.Length - MinJointGap;
            if (network.DistanceToJoint(edge, s) < MinJointGap - 0.001f || layout.Length < MinJointGap * 2f && !layout.Closed)
            { error = "Too close to a switch - start a little further along"; return none; }
            if (network.DistanceToCrossing(edge, s) < MinJointGap * 0.7f) { error = "Too close to a crossing"; return none; }
            int index = IndexAt(layout, s);
            if (!layout.Closed) index = Mathf.Clamp(index, 1, layout.Points.Count - 2);
            var at = layout.Points[index];
            layout.Evaluate(layout.Cum[index], out var tangent);
            var t = Flat(tangent).normalized;
            return new TrackAttach { Kind = AttachKind.Mid, Edge = edge, S = layout.Cum[index], Point = Flat(at), Dir = Vector2.Dot(dir, t) >= 0f ? t : -t };
        }

        private static TrackAttach EndAttach(TrackNetwork network, EdgeEnd end, bool start)
        {
            var into = network.IntoEdge(end);
            var outward = -Flat(into).normalized;
            return new TrackAttach { Kind = AttachKind.End, End = end, Point = Flat(network.EndPoint(end)), Dir = start ? outward : -outward };
        }

        public static int IndexAt(TrackLayout l, float s)
        {
            int lo = 0, hi = l.Points.Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (l.Cum[mid] <= s) lo = mid; else hi = mid; }
            return s - l.Cum[lo] < l.Cum[hi] - s ? lo : hi;
        }

        /// <summary>
        /// Plans a new piece from a stroke, as forgivingly as possible: attachments snap and the track is bent to meet them
        /// smoothly, small accidental loops from hand jitter are snipped out, curves are smoothed until the train can take
        /// them, and <paramref name="ensureLand"/> may grow land under any part drawn over bare floor. Refused: strokes too
        /// short, tracks crossing at a very shallow angle or right at a switch, and track squeezed against other track.
        /// </summary>
        public static TrackPlan Plan(IReadOnlyList<Vector3> drawn, TerrainField terrain, TrackNetwork network,
            System.Func<List<Vector2>, bool> ensureLand = null)
        {
            var plan = new TrackPlan();
            var raw = new List<Vector2>();
            foreach (var p in drawn)
                if (raw.Count == 0 || (new Vector2(p.x, p.z) - raw[^1]).magnitude > 0.004f) raw.Add(new Vector2(p.x, p.z));
            plan.Start = Attach(network, drawn, true, out string startError);
            plan.Finish = Attach(network, drawn, false, out string finishError);
            if (plan.Start.Kind == AttachKind.End && plan.Finish.Kind == AttachKind.End && plan.Start.End == plan.Finish.End) plan.Finish.Kind = AttachKind.None;
            if (plan.Start.Kind == AttachKind.Mid && plan.Finish.Kind == AttachKind.Mid && plan.Start.Edge == plan.Finish.Edge
                && TrackNetwork.ArcGap(network.Edges[plan.Start.Edge].Layout, plan.Start.S, plan.Finish.S) < MinJointGap) plan.Finish.Kind = AttachKind.None;
            bool attached = plan.Start.Kind != AttachKind.None || plan.Finish.Kind != AttachKind.None;
            float strokeLength = Length(drawn);
            if (raw.Count < 2 || strokeLength < (attached ? MinConnectorLength : MinLength) || (!attached && raw.Count < 3))
            { plan.Error = startError ?? finishError ?? "Draw a longer track"; return plan; }

            int from = 0, to = raw.Count - 1;
            bool closed = !attached && FindLoop(raw, out from, out to);
            List<Vector2> path;
            if (closed) path = raw.GetRange(from, to - from + 1);
            else path = raw;

            List<Vector2> pts = null;
            float radius = 0f;
            foreach (var (trim, turn) in new[] { (0.1f, 0.24f), (0.16f, 0.28f), (0.24f, 0.34f), (0.34f, 0.42f), (0.06f, 0.2f), (0.12f, 0.2f) })
            {
                var shaped = Shape(path, closed, plan.Start, plan.Finish, trim, turn);
                if (shaped == null) continue;
                float r = MinRadiusOf(shaped, closed);
                if (pts == null || r > radius) { pts = shaped; radius = r; }
                if (radius >= MinRadius * 0.95f) break;
            }
            if (pts == null || pts.Count < 3) { plan.Error = "Draw a longer track"; return plan; }
            plan.Smoothed = pts.ConvertAll(p => terrain.PointAt(p.x, p.y) + Vector3.up * 0.006f);
            if (closed && Perimeter(pts, true) < MinLoopLength) { plan.Error = "Make the loop a little bigger"; return plan; }
            if (radius < MinRadius * 0.85f) { plan.Error = $"A curve is too tight for the train ({radius * 100f:0} cm)"; return plan; }

            var dense = CatmullRom(pts, Spacing, closed);
            if (plan.Start.Kind != AttachKind.None) dense[0] = plan.Start.Point;
            if (plan.Finish.Kind != AttachKind.None) dense[^1] = plan.Finish.Point;
            string error = CheckCrossings(dense, closed, network, plan) ?? CheckClearance(dense, closed, network, plan);
            if (error != null) { plan.Error = error; return plan; }

            bool offLand = false;
            foreach (var p in dense) if (terrain.EdgeAt(p.x, p.y) < 0.4f) { offLand = true; break; }
            if (offLand && ensureLand != null) ensureLand(dense);
            foreach (var p in dense)
                if (terrain.EdgeAt(p.x, p.y) < 0.4f) { plan.Error = "Part of the railway is too far out to build"; return plan; }
            plan.Path = dense;
            plan.Closed = closed;
            return plan;
        }

        /// <summary>
        /// Smooths a stroke into a drivable line. At an attachment the new track leaves (meets) the old one like a real
        /// switch: a short straight continuing the old track's direction, then a curve of at least <paramref name="radius"/>
        /// turning until it heads straight for the drawn line. These leads are pinned while the rest is smoothed, so they
        /// join the smoothed stroke without a kink.
        /// </summary>
        private static List<Vector2> Shape(List<Vector2> path, bool closed, TrackAttach start, TrackAttach finish, float trim, float radius)
        {
            var body = new List<Vector2>(path);
            bool pinStart = start.Kind != AttachKind.None, pinEnd = finish.Kind != AttachKind.None;
            List<Vector2> leadA = null, leadB = null;
            if (pinStart)
            {
                while (body.Count > 1 && (body[0] - start.Point).magnitude < trim) body.RemoveAt(0);
                leadA = Lead(start.Point, start.Dir, body, false, radius);
                if (leadA == null) return null;
            }
            if (pinEnd)
            {
                while (body.Count > 1 && (body[^1] - finish.Point).magnitude < trim) body.RemoveAt(body.Count - 1);
                leadB = Lead(finish.Point, -finish.Dir, body, true, radius);
                if (leadB == null) return null;
                leadB.Reverse();
            }
            var pts = new List<Vector2>();
            if (leadA != null) pts.AddRange(leadA);
            // The drawn line between the leads (at least its far end, so short connectors still have a middle).
            if (leadA != null) while (body.Count > 1 && (body[0] - leadA[^1]).magnitude < Coarse) body.RemoveAt(0);
            if (leadB != null) while (body.Count > 1 && (body[^1] - leadB[0]).magnitude < Coarse) body.RemoveAt(body.Count - 1);
            pts.AddRange(body);
            if (leadB != null) pts.AddRange(leadB);
            if (pts.Count < 3) return null;
            int nA = leadA?.Count ?? 0, nB = leadB?.Count ?? 0;
            void Pin(List<Vector2> q)
            {
                for (int k = 0; k < nA && k < q.Count; k++) q[k] = leadA[k];
                for (int k = 0; k < nB && k < q.Count; k++) q[q.Count - nB + k] = leadB[k];
            }
            List<Vector2> Resampled(List<Vector2> q)
            {
                if (nA == 0 && nB == 0) return Resample(q, Coarse, closed);
                // Keep the pinned leads as they are; resample only what lies between them.
                int a = Mathf.Max(0, nA - 1), b = q.Count - Mathf.Max(1, nB);
                if (b - a < 1) return q;
                var mid = Resample(q.GetRange(a, b - a + 1), Coarse, false);
                var r = new List<Vector2>();
                for (int k = 0; k < a; k++) r.Add(q[k]);
                r.AddRange(mid);
                for (int k = b + 1; k < q.Count; k++) r.Add(q[k]);
                return r;
            }
            pts = Resampled(pts);
            if (closed)
                while (pts.Count > 4 && (pts[^1] - pts[0]).magnitude < Coarse * 0.6f) pts.RemoveAt(pts.Count - 1);
            if (nA == 0 && nB == 0) pts = RemoveSmallLoops(pts, closed);
            Pin(pts);
            // Denoise the hand jitter without shrinking, round the spots that are still too tight, and if that is not
            // enough, smooth the whole line (a loop shrinks a little, but it always becomes drivable).
            for (int iter = 0; iter < 24; iter++) { Taubin(pts, closed); Pin(pts); }
            bool ok = false;
            for (int iter = 0; iter < 1500 && !ok; iter++)
            {
                if (iter % 10 == 0) { pts = Resampled(pts); Pin(pts); ok = MinRadiusOf(pts, closed) >= MinRadius * 1.03f; if (ok) break; }
                SmoothTightSpots(pts, closed, MinRadius * 1.08f); Pin(pts);
            }
            for (int iter = 0; iter < 800 && !ok; iter++)
            {
                if (iter % 10 == 0) { pts = Resampled(pts); if (nA == 0 && nB == 0) pts = RemoveSmallLoops(pts, closed); Pin(pts); ok = MinRadiusOf(pts, closed) >= MinRadius * 1.03f; if (ok) break; }
                Laplace(pts, closed, 0.5f); Pin(pts);
            }
            pts = Resampled(pts);
            if (nA == 0 && nB == 0) pts = RemoveSmallLoops(pts, closed);
            Pin(pts);
            return pts;
        }

        /// <summary>
        /// The switch end of a new piece: from <paramref name="p"/> a 4 cm straight along <paramref name="dir"/>, then an
        /// arc of <paramref name="radius"/> turning towards the drawn line until it points at the first stroke point
        /// outside the turning circle. Null when the stroke gives nothing to aim at.
        /// </summary>
        private static List<Vector2> Lead(Vector2 p, Vector2 dir, List<Vector2> body, bool fromEnd, float radius)
        {
            var lead = new List<Vector2> { p };
            var a = p + dir * 0.04f;
            lead.Add(p + dir * 0.02f);
            lead.Add(a);
            if (body.Count == 0) return lead;
            // Aim at the first stroke point (from this end) that lies outside the turning circle on its side.
            for (int k = 0; k < body.Count; k++)
            {
                var target = fromEnd ? body[body.Count - 1 - k] : body[k];
                var to = target - a;
                float cross = dir.x * to.y - dir.y * to.x;
                if (Vector2.Dot(to, dir) < 0.02f && Mathf.Abs(cross) < 0.02f) continue;
                if (Mathf.Abs(cross) < 0.003f * to.magnitude) { lead.Add(a + dir * Mathf.Min(0.03f, to.magnitude * 0.5f)); return lead; }   // straight on
                float side = Mathf.Sign(cross);
                var left = new Vector2(-dir.y, dir.x);
                var centre = a + left * side * radius;
                if ((target - centre).magnitude < radius * 1.1f) continue;
                // Walk round the circle until the heading points at the target.
                float step = Coarse / radius;
                var r0 = a - centre;
                for (float th = step; th < Mathf.PI * 1.2f; th += step)
                {
                    float c = Mathf.Cos(th * side), s = Mathf.Sin(th * side);
                    var q = centre + new Vector2(r0.x * c - r0.y * s, r0.x * s + r0.y * c);
                    var h = new Vector2(dir.x * c - dir.y * s, dir.x * s + dir.y * c);
                    var toT = target - q;
                    if ((h.x * toT.y - h.y * toT.x) * side <= 0f) return lead;
                    lead.Add(q);
                }
                return null;
            }
            return null;   // every stroke point is inside the turning circle: try a tighter or longer shape
        }

        /// <summary>
        /// Bends a piece of track like a rope: the point at <paramref name="index"/> moves to <paramref name="target"/>, the
        /// track around it follows with a smooth falloff, and its ends stay put where they meet switches. The result is
        /// rounded until drivable and checked against the rest of the network. Returns an error, or null with the new
        /// 1 cm centre line in <paramref name="dense"/>.
        /// </summary>
        public static string Reshape(TrackNetwork network, int edge, int index, Vector2 target, out List<Vector2> dense, out List<Vector3> preview, TerrainField terrain)
        {
            dense = null; preview = null;
            var e = network.Edges[edge];
            var l = e.Layout;
            int n = l.Points.Count;
            var grab = Flat(l.Points[index]);
            var move = target - grab;
            float reach = Mathf.Clamp(0.3f + move.magnitude * 1.6f, 0.25f, 0.9f);
            bool pin0 = !l.Closed && e.Joint[0] >= 0, pin1 = !l.Closed && e.Joint[1] >= 0;
            var pts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float d = TrackNetwork.ArcGap(l, l.Cum[i], l.Cum[index]);
                float w = Mathf.SmoothStep(0f, 1f, 1f - d / reach);
                if (pin0) w *= Mathf.SmoothStep(0f, 1f, (l.Cum[i] - 0.06f) / 0.16f);
                if (pin1) w *= Mathf.SmoothStep(0f, 1f, (l.Length - l.Cum[i] - 0.06f) / 0.16f);
                pts.Add(Flat(l.Points[i]) + move * w);
            }
            // Coarse line, ends pinned where they meet switches, rounded until the train can take it.
            var coarse = Resample(pts, Coarse, l.Closed);
            int keep = 3;
            var head = coarse.GetRange(0, Mathf.Min(keep, coarse.Count)); var tail = coarse.GetRange(Mathf.Max(0, coarse.Count - keep), Mathf.Min(keep, coarse.Count));
            void Pin(List<Vector2> q)
            {
                if (pin0) for (int k = 0; k < keep && k < q.Count; k++) q[k] = head[k];
                if (pin1) for (int k = 0; k < keep && k < q.Count; k++) q[q.Count - keep + k] = tail[k];
            }
            bool ok = false;
            for (int iter = 0; iter < 400 && !ok; iter++)
            {
                if (iter % 10 == 0) { ok = MinRadiusOf(coarse, l.Closed) >= MinRadius * 1.03f; if (ok) break; }
                SmoothTightSpots(coarse, l.Closed, MinRadius * 1.08f); Pin(coarse);
            }
            preview = coarse.ConvertAll(p => terrain.PointAt(p.x, p.y) + Vector3.up * 0.006f);
            float radius = MinRadiusOf(coarse, l.Closed);
            if (radius < MinRadius * 0.85f) return "That bend is too tight for the train";
            var line = CatmullRom(coarse, Spacing, l.Closed);
            if (!l.Closed) { line[0] = Flat(l.Points[0]); line[^1] = Flat(l.Points[^1]); }
            foreach (var p in line) if (terrain.EdgeAt(p.x, p.y) < 0.4f) return "Keep the track on the land";
            // Check against the rest of the network (without this piece).
            var others = network.Clone();
            others.RemoveEdge(edge);
            others.Reindex();
            var check = new TrackPlan
            {
                Start = new TrackAttach { Kind = pin0 ? AttachKind.Mid : AttachKind.None, Point = line[0] },
                Finish = new TrackAttach { Kind = pin1 ? AttachKind.Mid : AttachKind.None, Point = line[^1] },
            };
            string error = CheckCrossings(line, l.Closed, others, check) ?? CheckClearance(line, l.Closed, others, check);
            if (error != null) return error;
            dense = line;
            return null;
        }

        /// <summary>Diamond crossings with existing track (and itself) must be at a decent angle and clear of switches.</summary>
        private static string CheckCrossings(List<Vector2> dense, bool closed, TrackNetwork network, TrackPlan plan)
        {
            var found = new List<Vector2>();
            int n = dense.Count, segs = closed ? n : n - 1;
            bool NearAttach(Vector2 x, float within) =>
                (plan.Start.Kind != AttachKind.None && (x - plan.Start.Point).magnitude < within) || (plan.Finish.Kind != AttachKind.None && (x - plan.Finish.Point).magnitude < within);
            for (int i = 0; i < segs; i++)
            {
                var a = dense[i]; var b = dense[(i + 1) % n];
                // Against the network: only pieces near this segment.
                var mid = (a + b) * 0.5f;
                if (network != null && !network.Empty && network.Nearest(new Vector3(mid.x, 0f, mid.y), out int edge, out float s, out float d, 0.02f))
                {
                    var l = network.Edges[edge].Layout;
                    int k = IndexAt(l, s);
                    for (int o = -2; o <= 1; o++)
                    {
                        int k0 = k + o, k1 = k0 + 1;
                        if (l.Closed) { k0 = (k0 % l.Points.Count + l.Points.Count) % l.Points.Count; k1 = (k0 + 1) % l.Points.Count; }
                        else if (k0 < 0 || k1 >= l.Points.Count) continue;
                        var c = new Vector2(l.Points[k0].x, l.Points[k0].z); var e = new Vector2(l.Points[k1].x, l.Points[k1].z);
                        if (!SegmentsCross(a, b, c, e)) continue;
                        var x = Intersection(a, b, c, e);
                        if (NearAttach(x, 0.1f)) continue;
                        float angle = Vector2.Angle(b - a, e - c); angle = Mathf.Min(angle, 180f - angle);
                        if (angle < MinCrossingAngle) return "Tracks must cross at a wider angle";
                        if (network.NearJoint(new Vector3(x.x, 0f, x.y), MinJointGap)) return "Too close to a switch to cross here";
                        if (NearAttach(x, MinJointGap)) return "Too close to the new switch to cross here";
                        found.Add(x);
                    }
                }
                // Against itself (a figure 8).
                for (int j = i + 4; j < segs; j++)
                {
                    if (closed && i == 0 && j >= segs - 3) continue;
                    var c = dense[j]; var e = dense[(j + 1) % n];
                    if (!SegmentsCross(a, b, c, e)) continue;
                    var x = Intersection(a, b, c, e);
                    float angle = Vector2.Angle(b - a, e - c); angle = Mathf.Min(angle, 180f - angle);
                    if (angle < MinCrossingAngle) return "Tracks must cross at a wider angle";
                    if (NearAttach(x, MinJointGap)) return "Too close to the new switch to cross here";
                    found.Add(x);
                }
            }
            for (int i = 0; i < found.Count; i++)
            for (int j = i + 1; j < found.Count; j++)
                if ((found[i] - found[j]).magnitude > 0.02f && (found[i] - found[j]).magnitude < MinJointGap * 0.7f) return "Two crossings are too close together";
            return null;
        }

        /// <summary>Running alongside other track is fine, but not squeezed against it (the trains would touch).</summary>
        private static string CheckClearance(List<Vector2> dense, bool closed, TrackNetwork network, TrackPlan plan)
        {
            if (network == null || network.Empty) return null;
            for (int i = 0; i < dense.Count; i++)
            {
                var p = dense[i];
                if (plan.Start.Kind != AttachKind.None && (p - plan.Start.Point).magnitude < 0.3f) continue;
                if (plan.Finish.Kind != AttachKind.None && (p - plan.Finish.Point).magnitude < 0.3f) continue;
                var q = new Vector3(p.x, 0f, p.y);
                if (!network.Nearest(q, out int edge, out float s, out float d, TrackSpacing)) continue;
                if (network.DistanceToCrossing(edge, s) < 0.14f) continue;
                bool nearNewCrossing = false;
                // Approaching a crossing of the new track at an angle passes close to the other track for a moment.
                var l = network.Edges[edge].Layout;
                var at = l.Evaluate(s, out var tangent);
                var dir = dense[Mathf.Min(i + 1, dense.Count - 1)] - dense[Mathf.Max(i - 1, 0)];
                float angle = Vector2.Angle(dir, new Vector2(tangent.x, tangent.z)); angle = Mathf.Min(angle, 180f - angle);
                if (angle > MinCrossingAngle * 0.9f) nearNewCrossing = true;
                if (!nearNewCrossing) return "Too close to the other track - leave a little gap";
            }
            return null;
        }

        /// <summary>
        /// Finds where a stroke closes on itself: the nearest pair of points between its first quarter and its last
        /// 40 %, at least a loop's length apart along the stroke. Within <see cref="CloseDistance"/> it is a loop from
        /// <paramref name="from"/> to <paramref name="to"/> (tails beyond are overshoot and get trimmed).
        /// </summary>
        private static bool FindLoop(List<Vector2> raw, out int from, out int to)
        {
            from = 0; to = raw.Count - 1;
            var cum = new float[raw.Count];
            for (int i = 1; i < raw.Count; i++) cum[i] = cum[i - 1] + (raw[i] - raw[i - 1]).magnitude;
            float total = cum[^1];
            if (total < MinLoopLength) return false;
            float best = float.MaxValue;
            int step = Mathf.Max(1, raw.Count / 400);
            for (int i = 0; i < raw.Count && cum[i] <= total * 0.25f; i += step)
            for (int j = raw.Count - 1; j > i && cum[j] >= total * 0.6f; j -= step)
            {
                if (cum[j] - cum[i] < MinLoopLength * 0.9f) continue;
                float d = (raw[j] - raw[i]).sqrMagnitude;
                if (d < best) { best = d; from = i; to = j; }
            }
            return best < CloseDistance * CloseDistance;
        }

        /// <summary>
        /// Snips out accidental small loops (a hand that wobbled back over its own line): at a crossing, the shorter part
        /// is dropped when it is under 40 % of the line. A crossing of two big parts (a figure 8) is left alone.
        /// </summary>
        public static List<Vector2> RemoveSmallLoops(List<Vector2> p, bool closed)
        {
            for (int pass = 0; pass < 12; pass++)
            {
                int n = p.Count, segs = closed ? n : n - 1;
                bool changed = false;
                float total = Perimeter(p, closed);
                for (int i = 0; i < segs && !changed; i++)
                for (int j = i + 2; j < segs && !changed; j++)
                {
                    if (closed && i == 0 && j == segs - 1) continue;
                    var a = p[i]; var b = p[(i + 1) % n]; var c = p[j]; var d = p[(j + 1) % n];
                    if (!SegmentsCross(a, b, c, d)) continue;
                    var x = Intersection(a, b, c, d);
                    float inner = 0f;
                    for (int k = i + 1; k < j; k++) inner += (p[k + 1] - p[k]).magnitude;
                    float outer = total - inner;
                    if (inner <= total * 0.4f)
                    {
                        // Drop the little loop between the crossing segments.
                        var next = new List<Vector2>(n);
                        for (int k = 0; k <= i; k++) next.Add(p[k]);
                        next.Add(x);
                        for (int k = j + 1; k < n; k++) next.Add(p[k]);
                        p = next; changed = true;
                    }
                    else if (closed && outer <= total * 0.4f)
                    {
                        var next = new List<Vector2> { x };
                        for (int k = i + 1; k <= j; k++) next.Add(p[k]);
                        p = next; changed = true;
                    }
                }
                if (!changed) break;
            }
            return p;
        }

        private static Vector2 Intersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var r = b - a; var s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-9f) return (a + b) * 0.5f;
            float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / den;
            return a + r * t;
        }

        /// <summary>Clearance of the rail bed above water where the railway crosses it on a bridge.</summary>
        public const float BridgeClearance = 0.032f;

        /// <summary>
        /// Smooth bed heights following the terrain, grade-limited, never below the floor margin; over water high enough
        /// for a bridge. <paramref name="fixedHeights"/> pins points (switches and crossings, shared with other pieces).
        /// </summary>
        public static float[] Profile(List<Vector2> pts, TerrainField terrain, bool closed, System.Func<Vector2, float> waterLevel = null,
            Dictionary<int, float> fixedHeights = null)
        {
            int n = pts.Count;
            var h = new float[n];
            // Over water the bed must clear it (a bridge); everywhere else there is no lower bound.
            var minH = new float[n];
            for (int i = 0; i < n; i++)
            {
                float lvl = waterLevel != null ? waterLevel(pts[i]) : -1f;
                minH[i] = lvl >= 0f ? lvl + BridgeClearance : float.MinValue;
            }
            for (int i = 0; i < n; i++)
            {
                var a = pts[(i - 1 + n) % n]; var b = pts[(i + 1) % n];
                var t = (b - a).normalized; var side = new Vector2(-t.y, t.x) * 0.025f;
                var p = pts[i];
                h[i] = (terrain.SurfaceAt(p.x, p.y) * 2f + terrain.SurfaceAt(p.x + side.x, p.y + side.y) + terrain.SurfaceAt(p.x - side.x, p.y - side.y)) / 4f + TrackSystem.BedSink;
            }
            var tmp = new float[n];
            for (int pass = 0; pass < 6; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f; int c = 0;
                    for (int k = -10; k <= 10; k++)
                    {
                        int j = i + k;
                        if (closed) j = (j % n + n) % n; else if (j < 0 || j >= n) continue;
                        sum += h[j]; c++;
                    }
                    tmp[i] = sum / c;
                }
                System.Array.Copy(tmp, h, n);
            }
            float maxStep = MaxGrade * Spacing;
            for (int i = 0; i < n; i++) h[i] = Mathf.Max(h[i], minH[i]);
            void Pin() { if (fixedHeights != null) foreach (var kv in fixedHeights) if (kv.Key >= 0 && kv.Key < n) h[kv.Key] = kv.Value; }
            Pin();
            for (int pass = 0; pass < 6; pass++)
            {
                for (int i = 1; i < n + (closed ? n / 2 : 0); i++)
                {
                    int cur = i % n, prev = (i - 1) % n;
                    h[cur] = Mathf.Max(Mathf.Clamp(h[cur], h[prev] - maxStep, h[prev] + maxStep), minH[cur]);
                }
                Pin();
                for (int i = n - 2 + (closed ? n / 2 : 0); i >= 0; i--)
                {
                    int cur = ((i % n) + n) % n, next = (cur + 1) % n;
                    if (!closed && cur == n - 1) continue;
                    h[cur] = Mathf.Max(Mathf.Clamp(h[cur], h[next] - maxStep, h[next] + maxStep), minH[cur]);
                }
                Pin();
            }
            for (int i = 0; i < n; i++) h[i] = Mathf.Max(h[i], TerrainField.MinHeight + 0.006f);
            return h;
        }

        // ------------------------------------------------------------------ geometry helpers

        private static Vector2 Flat(Vector3 v) => new(v.x, v.z);

        private static float Length(List<Vector2> p)
        {
            float l = 0f;
            for (int i = 1; i < p.Count; i++) l += (p[i] - p[i - 1]).magnitude;
            return l;
        }

        private static float Perimeter(List<Vector2> p, bool closed) => Length(p) + (closed ? (p[^1] - p[0]).magnitude : 0f);

        public static List<Vector2> Resample(List<Vector2> p, float spacing, bool closed)
        {
            var src = new List<Vector2>(p);
            if (closed) src.Add(p[0]);
            float total = Length(src);
            int count = Mathf.Max(closed ? 4 : 2, Mathf.RoundToInt(total / spacing));
            float step = total / count;
            var result = new List<Vector2>(count + 1) { src[0] };
            float acc = 0f; int seg = 0; float segLen = (src[1] - src[0]).magnitude;
            for (int k = 1; k < (closed ? count : count + 1); k++)
            {
                float target = k * step;
                while (acc + segLen < target && seg < src.Count - 2) { acc += segLen; seg++; segLen = (src[seg + 1] - src[seg]).magnitude; }
                float t = segLen > 1e-7f ? Mathf.Clamp01((target - acc) / segLen) : 0f;
                result.Add(Vector2.Lerp(src[seg], src[seg + 1], t));
            }
            if (!closed) result[^1] = p[^1];
            return result;
        }

        private static void Taubin(List<Vector2> p, bool closed)
        {
            Laplace(p, closed, 0.5f);
            Laplace(p, closed, -0.53f);
        }

        /// <summary>Laplacian smoothing restricted to points (and neighbours) whose local radius is below <paramref name="target"/>.</summary>
        private static void SmoothTightSpots(List<Vector2> p, bool closed, float target)
        {
            int n = p.Count, k = 3;
            var weight = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (!closed && (i < k || i >= n - k)) continue;
                var a = p[(i - k + n) % n]; var b = p[i]; var c = p[(i + k) % n];
                float ab = (b - a).magnitude, bc = (c - b).magnitude, ca = (a - c).magnitude;
                float cross = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                float r = cross < 1e-9f ? float.MaxValue : ab * bc * ca / (2f * cross);
                if (r >= target) continue;
                for (int o = -6; o <= 6; o++)
                {
                    int j = i + o;
                    if (closed) j = (j % n + n) % n; else if (j < 0 || j >= n) continue;
                    weight[j] = Mathf.Max(weight[j], 1f - Mathf.Abs(o) / 7f);
                }
            }
            var copy = p.ToArray();
            for (int i = 0; i < n; i++)
            {
                if (weight[i] <= 0f || (!closed && (i == 0 || i == n - 1))) continue;
                var a = copy[(i - 1 + n) % n]; var b = copy[(i + 1) % n];
                p[i] = copy[i] + ((a + b) * 0.5f - copy[i]) * (0.5f * weight[i]);
            }
        }

        private static void Laplace(List<Vector2> p, bool closed, float factor)
        {
            int n = p.Count;
            var copy = p.ToArray();
            for (int i = 0; i < n; i++)
            {
                if (!closed && (i == 0 || i == n - 1)) continue;
                var a = copy[(i - 1 + n) % n]; var b = copy[(i + 1) % n];
                p[i] = copy[i] + ((a + b) * 0.5f - copy[i]) * factor;
            }
        }

        /// <summary>Smallest circumradius over triples spaced ~9 cm apart (curvature the train actually feels).</summary>
        public static float MinRadiusOf(List<Vector2> p, bool closed)
        {
            int n = p.Count, k = 3;
            float min = float.MaxValue;
            for (int i = closed ? 0 : k; i < (closed ? n : n - k); i++)
            {
                var a = p[(i - k + n) % n]; var b = p[i]; var c = p[(i + k) % n];
                float ab = (b - a).magnitude, bc = (c - b).magnitude, ca = (a - c).magnitude;
                float cross = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                if (cross < 1e-9f) continue;
                min = Mathf.Min(min, ab * bc * ca / (2f * cross));
            }
            return min;
        }

        public static bool SelfIntersects(List<Vector2> p, bool closed)
        {
            int n = p.Count, segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n];
                for (int j = i + 3; j < segs; j++)
                {
                    if (closed && (j + 1) % n == i) continue;
                    if (closed && i == 0 && j >= segs - 2) continue;
                    if (SegmentsCross(a, b, p[j], p[(j + 1) % n])) return true;
                }
            }
            return false;
        }

        private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        public static List<Vector2> CatmullRom(List<Vector2> p, float spacing, bool closed)
        {
            int n = p.Count;
            var dense = new List<Vector2>();
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 p0 = p[closed ? (i - 1 + n) % n : Mathf.Max(0, i - 1)], p1 = p[i];
                Vector2 p2 = p[(i + 1) % n], p3 = p[closed ? (i + 2) % n : Mathf.Min(n - 1, i + 2)];
                int steps = Mathf.Max(1, Mathf.RoundToInt((p2 - p1).magnitude / spacing));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                    dense.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            if (!closed) dense.Add(p[^1]);
            return dense;
        }
    }
}
