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

    public sealed class TrackPlan
    {
        public TrackLayout Layout;
        public string Error;
        public List<Vector3> Smoothed;   // coarse centre line, for feedback even when invalid
        public bool Ok => Error == null;
    }

    /// <summary>
    /// Turns a hand-drawn stroke into a railway: closes it into a loop when the end comes back near the start, smooths
    /// it until every curve is at least <see cref="MinRadius"/> (so the train can take it), rejects self-crossings
    /// (no bridges in this prototype) and track off the landscape, and gives it a smooth height profile limited to
    /// <see cref="MaxGrade"/>; the terrain is then graded to that profile (embankments and cuttings).
    /// </summary>
    public static class TrackPlanner
    {
        public const float MinRadius = 0.18f;
        public const float MaxGrade = 0.09f;   // steep for real railways, fine for a storybook train; lets track follow the land
        public const float CloseDistance = 0.3f;
        public const float Spacing = 0.01f;
        public const float Coarse = 0.03f;
        public const float MinLength = 0.6f;
        public const float MinLoopLength = 0.9f;

        public static float Length(IReadOnlyList<Vector3> pts)
        {
            float l = 0f;
            for (int i = 1; i < pts.Count; i++) l += Flat(pts[i] - pts[i - 1]).magnitude;
            return l;
        }

        /// <summary>
        /// Whether releasing now would close the loop: the stroke is long enough and its end region comes back within
        /// <see cref="CloseDistance"/> of its start region (ending short of the start or overshooting past it).
        /// </summary>
        public static bool WouldClose(IReadOnlyList<Vector3> drawn)
        {
            if (drawn.Count < 3 || Length(drawn) < MinLoopLength) return false;
            var pts = new List<Vector2>(drawn.Count);
            foreach (var p in drawn) pts.Add(new Vector2(p.x, p.z));
            return FindLoop(pts, out _, out _);
        }

        /// <summary>
        /// Turns a stroke into a railway, as forgivingly as possible: a stroke that comes back to its start becomes a loop
        /// (overlapping tails are trimmed), small accidental loops from hand jitter are snipped out, curves are smoothed
        /// until the train can take them, and <paramref name="ensureLand"/> may grow land under any part drawn over bare
        /// floor. Only a genuine figure-8 (it would need a bridge) or a too-short stroke is refused.
        /// </summary>
        public static TrackPlan Plan(IReadOnlyList<Vector3> drawn, TerrainField terrain, System.Func<List<Vector2>, bool> ensureLand = null,
            System.Func<Vector2, float> waterLevel = null)
        {
            var plan = new TrackPlan();
            var raw = new List<Vector2>();
            foreach (var p in drawn)
                if (raw.Count == 0 || (new Vector2(p.x, p.z) - raw[^1]).magnitude > 0.004f) raw.Add(new Vector2(p.x, p.z));
            if (raw.Count < 3 || Length(drawn) < MinLength) { plan.Error = "Draw a longer track"; return plan; }

            bool closed = FindLoop(raw, out int from, out int to);
            var path = closed ? raw.GetRange(from, to - from + 1) : raw;
            var pts = Resample(path, Coarse, closed);
            if (closed)
                while (pts.Count > 4 && (pts[^1] - pts[0]).magnitude < Coarse * 0.6f) pts.RemoveAt(pts.Count - 1);
            pts = RemoveSmallLoops(pts, closed);

            // Denoise the hand jitter without shrinking, round the spots that are still too tight, and if that is not
            // enough, smooth the whole line (a loop shrinks a little, but it always becomes drivable).
            for (int iter = 0; iter < 24; iter++) Taubin(pts, closed);
            bool ok = false;
            for (int iter = 0; iter < 1500 && !ok; iter++)
            {
                if (iter % 10 == 0) { pts = Resample(pts, Coarse, closed); ok = MinRadiusOf(pts, closed) >= MinRadius * 1.03f; if (ok) break; }
                SmoothTightSpots(pts, closed, MinRadius * 1.08f);
            }
            for (int iter = 0; iter < 800 && !ok; iter++)
            {
                if (iter % 10 == 0) { pts = Resample(pts, Coarse, closed); pts = RemoveSmallLoops(pts, closed); ok = MinRadiusOf(pts, closed) >= MinRadius * 1.03f; if (ok) break; }
                Laplace(pts, closed, 0.5f);
            }
            pts = Resample(pts, Coarse, closed);
            pts = RemoveSmallLoops(pts, closed);
            plan.Smoothed = pts.ConvertAll(p => terrain.PointAt(p.x, p.y) + Vector3.up * 0.006f);

            if (closed && Perimeter(pts, true) < MinLoopLength) { plan.Error = "Make the loop a little bigger"; return plan; }
            if (SelfIntersects(pts, closed)) { plan.Error = "The track crosses itself like a figure 8 - no bridges yet"; return plan; }
            float radius = MinRadiusOf(pts, closed);
            if (radius < MinRadius * 0.85f) { plan.Error = $"A curve is too tight for the train ({radius * 100f:0} cm)"; return plan; }

            var dense = CatmullRom(pts, Spacing, closed);
            bool offLand = false;
            foreach (var p in dense) if (terrain.EdgeAt(p.x, p.y) < 0.4f) { offLand = true; break; }
            if (offLand && ensureLand != null) ensureLand(dense);
            foreach (var p in dense)
                if (terrain.EdgeAt(p.x, p.y) < 0.4f) { plan.Error = "Part of the railway is too far out to build"; return plan; }

            var heights = Profile(dense, terrain, closed, waterLevel);
            var points = new List<Vector3>(dense.Count);
            for (int i = 0; i < dense.Count; i++) points.Add(new Vector3(dense[i].x, heights[i], dense[i].y));
            plan.Layout = new TrackLayout(points, closed);
            return plan;
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

        /// <summary>Smooth bed heights following the terrain, grade-limited, never below the floor margin.</summary>
        /// <summary>Clearance of the rail bed above water where the railway crosses it on a bridge.</summary>
        public const float BridgeClearance = 0.032f;

        public static float[] Profile(List<Vector2> pts, TerrainField terrain, bool closed, System.Func<Vector2, float> waterLevel = null)
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
                h[i] = (terrain.SurfaceAt(p.x, p.y) * 2f + terrain.SurfaceAt(p.x + side.x, p.y + side.y) + terrain.SurfaceAt(p.x - side.x, p.y - side.y)) / 4f;
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
            for (int pass = 0; pass < 6; pass++)
            {
                for (int i = 1; i < n + (closed ? n / 2 : 0); i++)
                {
                    int cur = i % n, prev = (i - 1) % n;
                    h[cur] = Mathf.Max(Mathf.Clamp(h[cur], h[prev] - maxStep, h[prev] + maxStep), minH[cur]);
                }
                for (int i = n - 2 + (closed ? n / 2 : 0); i >= 0; i--)
                {
                    int cur = ((i % n) + n) % n, next = (cur + 1) % n;
                    if (!closed && cur == n - 1) continue;
                    h[cur] = Mathf.Max(Mathf.Clamp(h[cur], h[next] - maxStep, h[next] + maxStep), minH[cur]);
                }
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
