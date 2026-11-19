using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    public enum BayKind { Bus, Taxi, Drive }

    /// <summary>
    /// A lay-by or a driveway off a street, part of the street itself: the kerb and pavement step back round a lay-by
    /// (tapering out and in again) so a bus at its stop or the taxis at their rank stand clear of the traffic; a
    /// driveway is a dropped kerb and a strip of asphalt from the street to a car park, a garage or a station's doors.
    /// </summary>
    public sealed class RoadBay
    {
        public BayKind Kind;
        public PathLine Line;
        public Vector2 At, Along;   // the point on the street's middle it is beside, and the street's direction there (its course's)
        public int Side;            // +1: on the right of Along, -1: on its left
        public float Length;        // a lay-by: its length along the kerb (the tapers are beyond); a driveway: its width
        public float Depth;         // a lay-by: how far it reaches back from the asphalt's edge; a driveway: how far to its end
        public float Edge;          // the asphalt's edge, from the street's middle
        public Vector2 Of;          // the building it is for (where it stands)

        public Vector2 Out => new Vector2(Along.y, -Along.x) * Side;   // away from the street
        /// <summary>The middle of a lay-by, <paramref name="along"/> from its centre (where a vehicle stands in it).</summary>
        public Vector2 Stand(float along) => At + Along * along + Out * (Edge + Depth * 0.5f);
        /// <summary>Where a driveway leaves the street (at the asphalt's edge), and its far end.</summary>
        public Vector2 Mouth => At + Out * Edge;
        public Vector2 End => At + Out * (Edge + Depth);
        public float Taper => Depth * 1.6f;

        /// <summary>Whether a point is on its asphalt.</summary>
        public bool Covers(Vector2 p)
        {
            float u = Vector2.Dot(p - At, Along), v = Vector2.Dot(p - At, Out);
            if (Kind == BayKind.Drive) return Mathf.Abs(u) < Length * 0.5f && v > Edge - 0.002f && v < Edge + Depth;
            float reach = Length * 0.5f + Taper;
            if (Mathf.Abs(u) > reach || v < Edge - 0.002f) return false;
            return v < Edge + Depth * Mathf.Clamp01((reach - Mathf.Abs(u)) / Taper);
        }

        public int Key()
        {
            var h = new System.HashCode();
            h.Add(Kind); h.Add(At); h.Add(Along); h.Add(Side); h.Add(Length); h.Add(Depth);
            return h.ToHashCode();
        }
    }

    public sealed partial class PathSystem
    {
        /// <summary>The lay-bys and driveways off the streets (set by the world from the buildings that need them).</summary>
        public readonly List<RoadBay> Bays = new();

        /// <summary>Sets the lay-bys and driveways; true when they changed (the streets want re-laying).</summary>
        public bool SetBays(List<RoadBay> bays)
        {
            bool same = bays.Count == Bays.Count;
            for (int i = 0; same && i < bays.Count; i++) same = bays[i].Key() == Bays[i].Key() && bays[i].Line == Bays[i].Line;
            if (same) return false;
            Bays.Clear(); Bays.AddRange(bays);
            return true;
        }

        /// <summary>The lay-by or driveway a point is on, or null.</summary>
        public RoadBay BayAt(Vector2 p)
        {
            foreach (var b in Bays) if (b.Covers(p)) return b;
            return null;
        }

        /// <summary>The nearest point on a street's middle (not a lane, a path or a highway) within <paramref name="within"/>, its direction there and the street.</summary>
        public bool NearestStreet(Vector2 p, float within, out PathLine line, out Vector2 at, out Vector2 along)
        {
            line = null; at = default; along = default;
            float best = within;
            foreach (var l in Lines)
            {
                if (l.kind != (int)PathKind.Road) continue;
                int n = l.points.Count, segs = l.closed ? n : n - 1;
                for (int i = 0; i < segs; i++)
                {
                    var a = l.points[i]; var b = l.points[(i + 1) % n];
                    var ab = b - a; float len2 = ab.sqrMagnitude;
                    if (len2 < 1e-12f) continue;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    var q = a + ab * t;
                    float d = (p - q).magnitude;
                    if (d < best) { best = d; line = l; at = q; along = ab / Mathf.Sqrt(len2); }
                }
            }
            return line != null;
        }

        private int BaysKey(PathLine l)
        {
            int k = 0;
            foreach (var b in Bays) if (b.Line == l) k = k * 31 + b.Key();
            return k;
        }

        /// <summary>The lay-bys and driveways along a street: their samples are marked (no kerb or pavement there) and returned with the range to draw them over.</summary>
        private List<(RoadBay bay, int from, int to)> MarkBays(PathLine l, List<RoadSample> samples)
        {
            var found = new List<(RoadBay, int, int)>();
            foreach (var bay in Bays)
            {
                if (bay.Line != l) continue;
                float reach = bay.Kind == BayKind.Drive ? bay.Length * 0.5f : bay.Length * 0.5f + bay.Taper;
                int from = -1, to = -1;
                for (int i = 0; i < samples.Count; i++)
                {
                    var d = samples[i].P - bay.At;
                    if (samples[i].Junction || Mathf.Abs(Vector2.Dot(d, bay.Along)) > reach || d.sqrMagnitude > (reach + 0.02f) * (reach + 0.02f)) continue;
                    if (from < 0) from = i;
                    to = i;
                }
                if (from <= 0 || to >= samples.Count - 1) continue;   // (it needs the street either side of it)
                int bit = bay.Side > 0 ? 1 : 2;
                for (int i = from; i <= to; i++) { var sm = samples[i]; sm.Cut |= bit; samples[i] = sm; }
                found.Add((bay, from - 1, to + 1));
            }
            return found;
        }

        /// <summary>
        /// A lay-by: asphalt out to a kerb that steps back from the street's (tapering out and in again over the samples
        /// either side, so it meets the street's own kerb exactly), the pavement behind it, a dashed line along the
        /// carriageway's edge and a box painted round it (yellow for a bus stop, white for taxis). A driveway: a dropped
        /// kerb, the pavement's ends squared off either side of it, and asphalt on to its end.
        /// </summary>
        private void DrawBay(MeshBuilder b, RoadBay bay, List<RoadSample> samples, int from, int to, float hw, float pw)
        {
            float e = hw - KerbWidth, sgn = bay.Side;
            Vector3 V(Vector2 q, float y) => new(q.x, y, q.y);
            void Up(Vector3 a, Vector3 c, Vector3 d, Vector3 f, Color colour) { if (Vector3.Cross(c - a, d - a).y >= 0f) b.AddQuad(a, c, d, f, colour); else b.AddQuad(a, f, d, c, colour); }
            void Both(Vector3 a, Vector3 c, Vector3 d, Vector3 f, Color colour) { b.AddQuad(a, c, d, f, colour); b.AddQuad(a, f, d, c, colour); }
            if (bay.Kind != BayKind.Drive)
            {
                float reach = bay.Length * 0.5f + bay.Taper;
                float u0 = Vector2.Dot(samples[from].P - bay.At, bay.Along), u1 = Vector2.Dot(samples[to].P - bay.At, bay.Along);
                float F(Vector2 p)
                {
                    float u = Vector2.Dot(p - bay.At, bay.Along);
                    // (0 exactly at the street's samples either side, 1 along the lay-by's length)
                    float edge = u < 0f ? -u0 : u1;
                    float t = Mathf.Clamp01((edge - Mathf.Abs(u)) / Mathf.Max(1e-4f, edge - bay.Length * 0.5f));
                    return Mathf.SmoothStep(0f, 1f, t);
                }
                var frames = new List<RoadSample>();
                for (int i = from; i <= to; i++)
                {
                    var sm = samples[i];
                    var right = new Vector2(sm.T.y, -sm.T.x) * sgn;
                    float outer = e + bay.Depth * F(sm.P);
                    if (i < to)
                    {
                        var nx = samples[i + 1];
                        var rn = new Vector2(nx.T.y, -nx.T.x) * sgn;
                        float on = e + bay.Depth * F(nx.P), y0 = sm.Y + RoadLift + 0.00005f, y1 = nx.Y + RoadLift + 0.00005f;
                        Up(V(sm.P + right * (e - 0.0012f), y0), V(sm.P + right * outer, y0), V(nx.P + rn * on, y1), V(nx.P + rn * (e - 0.0012f), y1), Asphalt);
                    }
                    frames.Add(new RoadSample { P = sm.P + right * outer, T = sm.T, Y = sm.Y, S = sm.S });
                }
                // The kerb and pavement round it (offsets outwards: to the right of the frames on the right, else the left).
                CrossSection(b, frames, false, new[]
                {
                    new Strip(0f, 0f, 0f, KerbHeight, Stone.Kerb), new Strip(0f, KerbHeight, sgn * KerbWidth, KerbHeight, Stone.Kerb),
                    new Strip(sgn * KerbWidth, KerbHeight, sgn * (KerbWidth + pw), KerbHeight, Stone.Paving), new Strip(sgn * (KerbWidth + pw), KerbHeight, sgn * (KerbWidth + pw), -Skirt, Stone.Paving),
                });
                // Markings: the carriageway's edge dashed across its mouth, and a box round where the vehicles stand.
                var inner = new List<RoadSample>();
                foreach (var sm in samples.GetRange(from, to - from + 1))
                    if (Mathf.Abs(Vector2.Dot(sm.P - bay.At, bay.Along)) <= bay.Length * 0.5f) inner.Add(sm);
                if (inner.Count >= 2)
                {
                    PaintLine(b, inner, false, sgn * (e - 0.0008f), 0.0009f, Paint, 0.006f, 0.004f);
                    var colour = bay.Kind == BayKind.Bus ? Yellow : Paint;
                    PaintLine(b, inner, false, sgn * (e + 0.0018f), 0.0012f, colour, 0f, 0f);
                    PaintLine(b, inner, false, sgn * (e + bay.Depth - 0.0018f), 0.0012f, colour, 0f, 0f);
                    foreach (var end in new[] { inner[0], inner[^1] })
                    {
                        var right = new Vector2(end.T.y, -end.T.x) * sgn;
                        var a = end.P + right * (e + 0.0012f); var c = end.P + right * (e + bay.Depth - 0.0012f);
                        float y = end.Y + RoadLift + 0.0003f;
                        var w = end.T * 0.0006f;
                        Up(V(a - w, y), V(c - w, y), V(c + w, y), V(a + w, y), colour);
                    }
                }
                return;
            }
            // A driveway: the opening runs between the street's samples either side of it.
            var s0 = samples[from]; var s1 = samples[to];
            var o = bay.Out;
            float ya = Mathf.Max(s0.Y, s1.Y) + RoadLift + 0.00005f;
            Vector2 left = s0.P + o * (e - 0.0012f), right2 = s1.P + o * (e - 0.0012f);
            // Over the pavement, level with the street; then on to its end, following the ground.
            float pave = hw + pw - e, len = Mathf.Max(bay.Depth, pave + 0.004f);
            int steps = Mathf.Max(2, Mathf.CeilToInt(len / 0.006f));
            Vector3 Pt(Vector2 at, float t)
            {
                var q = at + o * t;
                float ground = terrain.SurfaceAt(q.x, q.y) + RoadLift;
                float y = t <= pave ? ya : Mathf.Lerp(ya, ground, Mathf.Clamp01((t - pave) / 0.015f));
                return V(q, y);
            }
            for (int k = 0; k < steps; k++)
            {
                float ta = len * k / steps, tb = len * (k + 1) / steps;
                Up(Pt(left, ta), Pt(right2, ta), Pt(right2, tb), Pt(left, tb), Pal.Shade(Asphalt, 1.04f));
                // Its sides: a skirt down into the ground beyond the pavement, the pavement's squared-off ends beside it.
                foreach (var side in new[] { left, right2 })
                {
                    var p0 = Pt(side, ta); var p1 = Pt(side, tb);
                    if (ta < pave) Both(p0, new Vector3(p0.x, ya + KerbHeight, p0.z), new Vector3(p1.x, ya + KerbHeight, p1.z), p1, Pal.Shade(Paving, 0.9f));
                    Both(p0, p1, p1 + Vector3.down * Skirt, p0 + Vector3.down * Skirt, Pal.Shade(Paving, 0.85f));
                }
            }
            // The dropped kerb: a pale strip across the mouth.
            Up(V(left, ya + 0.0001f), V(right2, ya + 0.0001f), V(right2 + o * 0.003f, ya + 0.0001f), V(left + o * 0.003f, ya + 0.0001f), KerbStone);
        }
    }
}
