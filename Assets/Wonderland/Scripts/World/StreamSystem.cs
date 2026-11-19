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
        public List<float> widths = new();      // the width at each point, when it changes along it (a creek carried on as a river); empty: all `width`
        [NonSerialized] public bool JoinsAtStart, JoinsAtEnd;   // flows out of / into another stream there (full width, no taper)
        public float Width => width > 0f ? width : 1f;

        private bool Varies => widths != null && widths.Count == points.Count && points.Count > 0;

        /// <summary>The width at point <paramref name="i"/>.</summary>
        public float WidthAt(int i) => Varies ? widths[Mathf.Clamp(i, 0, widths.Count - 1)] : Width;

        /// <summary>The width <paramref name="along"/> metres downstream.</summary>
        public float WidthAlong(float along)
        {
            if (!Varies) return Width;
            float f = Mathf.Clamp(along / StreamSystem.Spacing, 0f, widths.Count - 1);
            int i = Mathf.Min(Mathf.FloorToInt(f), widths.Count - 1);
            return i + 1 < widths.Count ? Mathf.Lerp(widths[i], widths[i + 1], f - i) : widths[i];
        }

        /// <summary>Its widest.</summary>
        public float MaxWidth { get { if (!Varies) return Width; float m = 0f; foreach (float w in widths) m = Mathf.Max(m, w); return m; } }

        public StreamPath Copy() => new() { points = new List<Vector3>(points), width = width, widths = widths != null ? new List<float>(widths) : new List<float>() };
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

        /// <summary>
        /// The lowest a stream's water may be (just above the land's lowest): it never floats above the ground to make
        /// room for its channel - on low land the channel is just shallower (the bed stops at the land's lowest).
        /// </summary>
        private const float Lowest = TerrainField.MinHeight + 0.003f;

        /// <summary>Smooths a drawn stroke into a stream course and gives it a downhill water level. Null if too short.</summary>
        public static StreamPath Plan(IReadOnlyList<Vector3> drawn, TerrainField terrain, float width = 1f)
        {
            var raw = new List<Vector2>();
            foreach (var p in drawn) if (raw.Count == 0 || (new Vector2(p.x, p.z) - raw[^1]).magnitude > 0.004f) raw.Add(new Vector2(p.x, p.z));
            if (raw.Count < 3 || TrackPlanner.Length(drawn) < 0.15f) return null;
            var pts = Polyline.Resample(raw, Spacing, false);
            for (int it = 0; it < 30; it++)
            {
                var copy = pts.ToArray();
                for (int i = 1; i < pts.Count - 1; i++) pts[i] = copy[i] + ((copy[i - 1] + copy[i + 1]) * 0.5f - copy[i]) * 0.5f;
            }
            pts = Polyline.Resample(pts, Spacing, false);
            // Water flows the way it is drawn - unless that is clearly uphill (drawn from a lower pond up to higher land),
            // and a stream drawn to a pond always runs into it.
            bool intoPond = InPond(terrain, pts[^1].x, pts[^1].y) && !InPond(terrain, pts[0].x, pts[0].y);
            if (!intoPond && Target(terrain, pts[^1].x, pts[^1].y) > Target(terrain, pts[0].x, pts[0].y) + 0.008f) pts.Reverse();
            var path = new StreamPath { width = width };
            float level = float.MaxValue;
            foreach (var p in pts)
            {
                // Through a pond the stream is the pond: its level is the pond's surface, not the pond's bed.
                level = Mathf.Max(Lowest, Mathf.Min(level, Target(terrain, p.x, p.y)));
                path.points.Add(new Vector3(p.x, level, p.y));
            }
            MeetPond(path, terrain);
            return path;
        }

        /// <summary>
        /// A stream running into a pond meets it at the pond's own level: where the land is flat it would otherwise
        /// arrive below the pond's surface. (Raising every point to at least that level keeps it never flowing uphill;
        /// low banks on the way are built up by <see cref="Carve"/>.)
        /// </summary>
        private static void MeetPond(StreamPath s, TerrainField terrain)
        {
            var mouth = s.points[^1];
            if (!InPond(terrain, mouth.x, mouth.z)) return;
            float pond = terrain.WaterLevelAt(mouth.x, mouth.z) - 0.0003f;
            for (int i = s.points.Count - 1; i >= 0 && s.points[i].y < pond; i--) s.points[i] = new Vector3(s.points[i].x, pond, s.points[i].z);
        }

        /// <summary>
        /// A stream drawn on from the free end of another (its source or its mouth) becomes part of it, as a railway or
        /// a road carried on from its end does: one stream, still running the way the old one ran. The old stream stays
        /// exactly where it is - its channel is already carved there - and the new water leaves its end the way it was
        /// going, sweeping round in one smooth curve into the line that was drawn, however near the end the stroke began
        /// and at whatever angle; its width changes gently along the new water from the old one's to the new (a river
        /// narrowing to a creek, a creek widening to a river). Both ends may carry on streams (joining two into one). The
        /// streams joined are taken out of <paramref name="all"/>; the result is not levelled yet (<see cref="Relevel"/>).
        /// Returns <paramref name="fresh"/> itself when it joins none.
        /// </summary>
        public static StreamPath Carry(List<StreamPath> all, StreamPath fresh, TerrainField terrain)
        {
            var p = new List<Vector2>(); var w = new List<float>();
            foreach (var q in fresh.points) { p.Add(new Vector2(q.x, q.z)); w.Add(fresh.Width); }
            bool oriented = false;
            var freshEnds = new[] { p[0], p[^1] };
            foreach (var freshEnd in freshEnds)
            {
                bool atHead = (p[0] - freshEnd).sqrMagnitude < (p[^1] - freshEnd).sqrMagnitude;   // (where that end is now)
                var end = atHead ? p[0] : p[^1];
                StreamPath best = null; bool source = false; float bestD = float.MaxValue;
                foreach (var s in all)
                {
                    if (s.points.Count < 2) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        // Joined already, a river and a creek can only meet head to tail: a mouth to a source.
                        if (oriented && (atHead ? k == 0 : k == 1)) continue;
                        int i = k == 0 ? 0 : s.points.Count - 1;
                        var e = new Vector2(s.points[i].x, s.points[i].z);
                        float d = (e - end).magnitude;
                        if (d >= HalfWidth * Mathf.Max(s.WidthAt(i), fresh.Width) + 0.04f || d >= bestD || !FreeEnd(all, s, k == 0, terrain)) continue;
                        best = s; source = k == 0; bestD = d;
                    }
                }
                if (best == null) continue;
                all.Remove(best);
                // Work outwards from the old stream's free end: the old stream ordered to end there, the new stroke to
                // start there.
                var old = new List<Vector2>(); var ow = new List<float>();
                for (int i = 0; i < best.points.Count; i++) { old.Add(new Vector2(best.points[i].x, best.points[i].z)); ow.Add(best.WidthAt(i)); }
                if (source) { old.Reverse(); ow.Reverse(); }
                bool freshFromJoint = !atHead;   // (the stroke's end at the joint is its tail: turn it to start there)
                if (freshFromJoint) { p.Reverse(); w.Reverse(); }
                var tail = Onwards(old, ow, p, w);
                // Back in the stream's own order: into a source, the new water first; out of a mouth, the old first.
                var cp = new List<Vector2>(old); var cw = new List<float>(ow);
                cp.AddRange(tail.p); cw.AddRange(tail.w);
                if (source) { cp.Reverse(); cw.Reverse(); }
                p = cp; w = cw;
                oriented = true;
            }
            if (!oriented) return fresh;
            var joined = new StreamPath { width = fresh.Width };
            for (int i = 0; i < p.Count; i++) { joined.points.Add(new Vector3(p[i].x, 0f, p[i].y)); joined.widths.Add(w[i]); }
            // A stream that now begins in a pond and ends on dry land runs the other way: into the pond.
            if (InPond(terrain, p[0].x, p[0].y) && !InPond(terrain, p[^1].x, p[^1].y)) { joined.points.Reverse(); joined.widths.Reverse(); }
            return joined;
        }

        /// <summary>
        /// The new water on from the end of an old stream (<paramref name="old"/> ends at the joint, <paramref name="p"/>
        /// is the stroke starting near it): whatever of the stroke lies back over the old stream is dropped; the rest is
        /// joined on by a curve leaving the old end along the old stream's own heading and meeting the stroke a little way
        /// along, tangent to it there; then evenly spaced from the joint (the old points are not touched). Its width runs
        /// from the old end's to the stroke's over the first stretch.
        /// </summary>
        private static (List<Vector2> p, List<float> w) Onwards(List<Vector2> old, List<float> ow, List<Vector2> p, List<float> w)
        {
            var e0 = old[^1];
            var heading = (e0 - old[Mathf.Max(0, old.Count - 4)]).normalized;
            // Drop what was drawn back over the old stream, or right at its end.
            int first = 0;
            while (first < p.Count - 3 && Vector2.Dot(p[first] - e0, heading) < 0.01f) first++;
            var stroke = p.GetRange(first, p.Count - first);
            float strokeLength = 0f; for (int i = 1; i < stroke.Count; i++) strokeLength += (stroke[i] - stroke[i - 1]).magnitude;
            // Where the curve meets the stroke: as near as it can while it bends no tighter than a stream would (6 degrees
            // every 1.5 cm, from the old stream's last stretch on), further along the stroke if it has to - never past
            // most of it.
            var before = old.Count >= 2 ? old[^2] : e0 - heading * Spacing;
            List<Vector2> rp = null; List<float> rw0 = null;
            for (float want = 0.05f; ; want *= 1.25f)
            {
                bool last = want >= strokeLength * 0.8f;
                int m = 0; float along = 0f;
                while (m < stroke.Count - 2 && along < Mathf.Min(want, strokeLength * 0.8f)) { along += (stroke[m + 1] - stroke[m]).magnitude; m++; }
                var p1 = stroke[m];
                var t1 = (stroke[Mathf.Min(stroke.Count - 1, m + 1)] - stroke[Mathf.Max(0, m - 1)]).normalized;
                float span = (p1 - e0).magnitude;
                // A cubic Hermite curve from the old end (along its heading) to the stroke (along it).
                var curve = new List<Vector2>();
                int steps = Mathf.Max(4, Mathf.CeilToInt(span / 0.004f));
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps, t2 = t * t, t3 = t2 * t;
                    curve.Add((2f * t3 - 3f * t2 + 1f) * e0 + (t3 - 2f * t2 + t) * span * heading + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * span * t1);
                }
                for (int i = m + 1; i < stroke.Count; i++) curve.Add(stroke[i]);
                var cw = new List<float>(); foreach (var _ in curve) cw.Add(w[Mathf.Min(w.Count - 1, first)]);
                Resample(curve, cw, out rp, out rw0);
                // How sharply the curve bends (the stretch it meets the stroke on included), from the old stream's end on.
                float sharpest = Vector2.Angle(e0 - before, rp.Count > 1 ? rp[1] - rp[0] : heading);
                int check = Mathf.Min(rp.Count - 1, Mathf.CeilToInt(along / Spacing) + 3);
                for (int i = 1; i < check; i++) sharpest = Mathf.Max(sharpest, Vector2.Angle(rp[i] - rp[i - 1], rp[i + 1] - rp[i]));
                if (sharpest <= 6f || last) break;
            }
            var rw = rw0;
            // Its width: from the old end's to its own, gently over the first stretch (longer for a bigger change).
            float from = ow[^1], to = rw.Count > 0 ? rw[^1] : from;
            int blend = Mathf.Clamp(Mathf.RoundToInt((0.06f + Mathf.Abs(to - from) * 0.15f) / Spacing), 3, Mathf.Max(3, rw.Count - 1));
            for (int i = 0; i < rw.Count; i++) rw[i] = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(i / (float)blend)));
            // (the first point is the old end itself)
            rp.RemoveAt(0); rw.RemoveAt(0);
            return (rp, rw);
        }

        /// <summary>
        /// Where a stream runs into a pond or a lake (or out of one), its last stretch widens into a mouth - half as wide
        /// again at the water - so it opens into it instead of stopping like a pipe.
        /// </summary>
        public static void Mouths(StreamPath s, TerrainField terrain)
        {
            int n = s.points.Count;
            if (n < 8) return;
            int reach = Mathf.Min(n / 3, Mathf.RoundToInt((0.07f + 0.06f * s.MaxWidth) / Spacing));
            for (int end = 0; end < 2; end++)
            {
                int tip = end == 0 ? 0 : n - 1;
                if (!InPond(terrain, s.points[tip].x, s.points[tip].z) || (end == 0 ? s.JoinsAtStart : s.JoinsAtEnd)) continue;
                if (s.widths == null || s.widths.Count != n) { s.widths = new List<float>(); for (int i = 0; i < n; i++) s.widths.Add(s.Width); }
                for (int k = 0; k <= reach; k++)
                {
                    int i = end == 0 ? k : n - 1 - k;
                    float t = 1f - k / (float)reach;
                    s.widths[i] = s.widths[end == 0 ? reach : n - 1 - reach] * Mathf.Lerp(1f, 1.5f, Mathf.SmoothStep(0f, 1f, t));
                }
            }
        }

        /// <summary>Points (and their widths) every <see cref="Spacing"/> along the line.</summary>
        private static void Resample(List<Vector2> p, List<float> w, out List<Vector2> rp, out List<float> rw)
        {
            rp = new List<Vector2> { p[0] }; rw = new List<float> { w[0] };
            float carry = 0f;
            for (int i = 1; i < p.Count; i++)
            {
                var a = p[i - 1]; var b = p[i];
                float seg = (b - a).magnitude;
                float t = Spacing - carry;
                while (t <= seg)
                {
                    rp.Add(Vector2.Lerp(a, b, t / seg)); rw.Add(Mathf.Lerp(w[i - 1], w[i], t / seg));
                    t += Spacing;
                }
                carry = seg - (t - Spacing);
            }
            if ((rp[^1] - p[^1]).magnitude > Spacing * 0.3f) { rp.Add(p[^1]); rw.Add(w[^1]); }
            else { rp[^1] = p[^1]; rw[^1] = w[^1]; }
        }

        /// <summary>Whether a stream's source (or mouth) is free: not in a pond, not on another stream.</summary>
        private static bool FreeEnd(List<StreamPath> all, StreamPath s, bool source, TerrainField terrain)
        {
            var e = s.points[source ? 0 : s.points.Count - 1];
            if (InPond(terrain, e.x, e.z)) return false;
            foreach (var o in all)
            {
                if (o == s || o.points.Count < 2) continue;
                if (Nearest(o, new Vector2(e.x, e.z), out _, out float along) < HalfWidth * o.WidthAlong(along) + 0.012f) return false;
            }
            return true;
        }

        /// <summary>Re-levels a stream on its course from the (sculpted) ground, still never flowing uphill.</summary>
        public static void Relevel(StreamPath s, TerrainField terrain, List<StreamPath> others = null)
        {
            // (a side in another stream's channel - where a creek runs in - says nothing about this one)
            bool Channel(float x, float z)
            {
                if (others == null) return false;
                foreach (var o in others)
                    if (o != s && o.points.Count > 1 && Nearest(o, new Vector2(x, z), out _, out float oa) < (HalfWidth + Bank) * o.WidthAlong(oa) + Margin) return true;
                return false;
            }
            float level = float.MaxValue;
            for (int i = 0; i < s.points.Count; i++)
            {
                var p = s.points[i];
                float ground = terrain.SurfaceAt(p.x, p.z) - 0.004f;
                if (InPond(terrain, p.x, p.z)) { level = Mathf.Min(level, Target(terrain, p.x, p.z)); s.points[i] = new Vector3(p.x, level, p.z); continue; }
                // Sample the land just beyond the banks it carved, not the channel, so a stream never digs itself ever deeper.
                var t = (s.points[Mathf.Min(i + 1, s.points.Count - 1)] - s.points[Mathf.Max(i - 1, 0)]); t.y = 0f;
                if (t.sqrMagnitude > 1e-8f)
                {
                    var side = Vector3.Cross(Vector3.up, t.normalized) * ((HalfWidth + Bank) * s.WidthAt(i) + Margin + 0.006f);
                    // (a side out over the shore's slope says nothing about the stream)
                    bool inLeft = Channel(p.x + side.x, p.z + side.z), inRight = Channel(p.x - side.x, p.z - side.z);
                    float left = terrain.EdgeAt(p.x + side.x, p.z + side.z) > 0.6f && !inLeft ? terrain.SurfaceAt(p.x + side.x, p.z + side.z) : float.MaxValue;
                    float right = terrain.EdgeAt(p.x - side.x, p.z - side.z) > 0.6f && !inRight ? terrain.SurfaceAt(p.x - side.x, p.z - side.z) : float.MaxValue;
                    if (Mathf.Min(left, right) < float.MaxValue) ground = Mathf.Min(left, right) - 0.004f;
                    else if (inLeft || inRight) ground = float.MaxValue;   // (running into another stream: its level comes from that one, see Join)
                }
                level = Mathf.Max(Lowest, Mathf.Min(level, ground));
                s.points[i] = new Vector3(p.x, level, p.z);
            }
            MeetPond(s, terrain);
        }

        /// <summary>How much wider than its water a channel is carved (and its ribbon of water drawn), so the land's
        /// coarse grid never shows in the water's outline: the water's soft edge lies inside the channel.</summary>
        public const float Margin = 0.005f;

        /// <summary>
        /// Carves the channel and shapes the banks around a stream; returns the touched region. Its banks never fill
        /// another stream's channel (where a creek runs into a river the river's bed stays open).
        /// </summary>
        public static RectInt Carve(TerrainField terrain, StreamPath s, List<StreamPath> others = null)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in s.points) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
            float widest = (HalfWidth + Bank) * s.MaxWidth + Margin;
            bool wetStart = InPond(terrain, s.points[0].x, s.points[0].z) || s.JoinsAtStart, wetEnd = InPond(terrain, s.points[^1].x, s.points[^1].z) || s.JoinsAtEnd;
            // (its own bounds, not a square round it: a long stream's region is a strip)
            RectInt lo = terrain.RegionAround(new Vector2(minX, minZ), widest + 0.02f), hi = terrain.RegionAround(new Vector2(maxX, maxZ), widest + 0.02f);
            var region = new RectInt(lo.xMin, lo.yMin, Mathf.Max(0, hi.xMax - lo.xMin), Mathf.Max(0, hi.yMax - lo.yMin));
            var near = new List<StreamPath>();
            if (others != null)
                foreach (var o in others)
                {
                    if (o == s || o.points.Count < 2) continue;
                    foreach (var q in o.points) if (q.x > minX - 0.1f && q.x < maxX + 0.1f && q.z > minZ - 0.1f && q.z < maxZ + 0.1f) { near.Add(o); break; }
                }
            float length = PathLength(s);
            // (each cell looks only at the stretches of stream in its neighbourhood, not along the whole stream)
            var grid = new Segments(s, widest);
            var nearGrids = near.ConvertAll(o => new Segments(o, widest));
            for (int z = region.yMin; z < region.yMax; z++)
            for (int x = region.xMin; x < region.xMax; x++)
            {
                var c = new Vector2(terrain.LocalX(x), terrain.LocalZ(z));
                if (c.x < minX - widest || c.x > maxX + widest || c.y < minZ - widest || c.y > maxZ + widest) continue;
                float d = grid.Nearest(c, out float level, out float along);
                float width = s.WidthAlong(along);
                float reach = (HalfWidth + Bank) * width + Margin;
                if (d >= reach) continue;
                int i = z * terrain.Nx + x;
                if (terrain.Land[i] < 0.5f || terrain.Pad[i] > 0.05f) continue;
                if (terrain.Water != null && terrain.Water[i] > 0.5f) continue;   // in a pond: the pond is the stream there (no dam across it)
                float taper = (wetStart ? 1f : Mathf.Clamp01(along / 0.05f)) * (wetEnd ? 1f : Mathf.Clamp01((length - along) / 0.05f));   // full width into a pond
                float hw = HalfWidth * width * Mathf.Lerp(0.45f, 1f, taper) + Margin;
                if (d < hw)
                {
                    float bed = level - Depth * DepthScale(width) * Mathf.Lerp(0.5f, 1f, taper) * (1f - (d / hw) * (d / hw)) - 0.0025f;
                    terrain.Height[i] = Mathf.Min(terrain.Height[i], terrain.HeightForSurface(i, bed));
                }
                else
                {
                    bool inOther = false;
                    for (int k = 0; k < near.Count && !inOther; k++) if (nearGrids[k].Nearest(c, out _, out float oa) < HalfWidth * near[k].WidthAlong(oa) + Margin) inOther = true;
                    if (inOther) continue;
                    float w = 1f - (d - hw) / (reach - hw);
                    // The bank starts just under the water (the ribbon's edge dips into it) and rises gently.
                    float bank = level - 0.0015f + (d - hw) * 0.3f;
                    float target = terrain.HeightForSurface(i, bank);
                    bool shore = terrain.Water != null && terrain.Water[i] > 0.02f;   // (by a pond: never built up)
                    if (terrain.Surface[i] < level + 0.0015f && !shore) terrain.Height[i] = Mathf.Max(terrain.Height[i], target);
                    else terrain.Height[i] = Mathf.Lerp(terrain.Height[i], Mathf.Min(terrain.Height[i], target), w * w);
                }
            }
            terrain.Despike(region);
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
                        if (d > HalfWidth * Mathf.Max(o.WidthAlong(along), s.WidthAt(i)) + 0.03f) continue;
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

        /// <summary>
        /// Where a stream runs into another's side (or leaves it), it meets it as a real confluence does: its last stretch
        /// swings round in one smooth curve to join at no more than 45 degrees to the other's flow - downstream, never
        /// against it - and widens into a mouth. So a branch drawn off a river later looks as if it was always there.
        /// </summary>
        public static void Confluence(List<StreamPath> all, StreamPath s)
        {
            int n = s.points.Count;
            if (n < 8) return;
            foreach (var o in all)
            {
                if (o == s || o.points.Count < 4) continue;
                for (int end = 0; end < 2; end++)
                {
                    int tip = end == 0 ? 0 : n - 1;
                    var p = new Vector2(s.points[tip].x, s.points[tip].z);
                    float d = Nearest(o, p, out _, out float along);
                    float ow = o.WidthAlong(along);
                    if (d > HalfWidth * Mathf.Max(ow, s.WidthAt(tip)) + 0.03f) continue;
                    int k = Mathf.Clamp(Mathf.RoundToInt(along / Spacing), 1, o.points.Count - 2);
                    if (k <= 2 || k >= o.points.Count - 3) continue;   // (at the other's end: carried on, not a confluence)
                    var q = new Vector2(o.points[k].x, o.points[k].z);
                    var flow = new Vector2(o.points[k + 1].x - o.points[k - 1].x, o.points[k + 1].z - o.points[k - 1].z).normalized;
                    // How far back the curve starts (enough for the sweep to show beside the other's water), and the
                    // stream's own heading there.
                    int span = Mathf.Min(n / 2, Mathf.RoundToInt((0.06f + 3f * HalfWidth * Mathf.Max(ow, s.WidthAt(tip))) / Spacing));
                    int from = end == 0 ? span : n - 1 - span;
                    var p0 = new Vector2(s.points[from].x, s.points[from].z);
                    int before = end == 0 ? Mathf.Min(from + 1, n - 1) : Mathf.Max(from - 1, 0);
                    var t0 = (p0 - new Vector2(s.points[before].x, s.points[before].z)).normalized;   // (heading towards the confluence)
                    // Meeting the other's flow: a tributary arrives going downstream with it, a branch leaves going downstream.
                    var arrive = end == 1 ? (q - p0).normalized : (p0 - q).normalized;
                    if (Vector2.Dot(arrive, flow) < 0f) arrive = Vector2.Reflect(arrive, new Vector2(-flow.y, flow.x));
                    float angle = Vector2.SignedAngle(flow, arrive);
                    if (Mathf.Abs(angle) > 45f) arrive = (Vector2)(Quaternion.Euler(0f, 0f, Mathf.Sign(angle) * 45f) * flow);
                    float len = (q - p0).magnitude;
                    // A cubic curve from where the stream was to the other's middle, tangent to both.
                    Vector2 c0 = p0 + t0 * len / 3f, c1 = end == 1 ? q - arrive * len / 3f : q + arrive * len / 3f;
                    bool varied = s.widths != null && s.widths.Count == n;
                    if (!varied) { s.widths = new List<float>(); for (int i = 0; i < n; i++) s.widths.Add(s.Width); }
                    for (int j = 0; j <= span; j++)
                    {
                        float t = j / (float)span;
                        float u = 1f - t;
                        var b = u * u * u * p0 + 3f * u * u * t * c0 + 3f * u * t * t * c1 + t * t * t * q;
                        int i = end == 0 ? from - j : from + j;
                        s.points[i] = new Vector3(b.x, s.points[i].y, b.y);
                        s.widths[i] = s.widths[from] * Mathf.Lerp(1f, 1.45f, t * t);   // (the mouth widens)
                    }
                }
            }
        }

        /// <summary>A river's channel is deeper too (not in proportion: a creek is still a little channel).</summary>
        private static float DepthScale(float width) => Mathf.Lerp(1f, width, 0.6f);

        /// <summary>
        /// A stream's stretches filed by where they are (cells of at least the reach asked about), so the nearest stretch
        /// to a point within that reach is found among its neighbours instead of along the whole stream.
        /// </summary>
        private sealed class Segments
        {
            private readonly StreamPath s;
            private readonly float cell;
            private readonly Dictionary<long, List<int>> cells = new();

            public Segments(StreamPath stream, float reach)
            {
                s = stream; cell = Mathf.Max(reach, Spacing * 2f);
                for (int i = 0; i < s.points.Count - 1; i++)
                {
                    var a = s.points[i]; var b = s.points[i + 1];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / cell);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.z, b.z) / cell), z1 = Mathf.FloorToInt(Mathf.Max(a.z, b.z) / cell);
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        long k = ((long)x << 32) ^ (uint)z;
                        if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<int>();
                        list.Add(i);
                    }
                }
            }

            /// <summary>As <see cref="StreamSystem.Nearest"/>, for points within the reach (beyond it: infinity, or farther).</summary>
            public float Nearest(Vector2 p, out float level, out float along)
            {
                float best = float.MaxValue; level = 0f; along = 0f;
                int cx = Mathf.FloorToInt(p.x / cell), cz = Mathf.FloorToInt(p.y / cell);
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!cells.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var list)) continue;
                    foreach (int i in list)
                    {
                        var a = new Vector2(s.points[i].x, s.points[i].z); var b = new Vector2(s.points[i + 1].x, s.points[i + 1].z);
                        var ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
                        float d = (p - (a + ab * t)).sqrMagnitude;
                        if (d < best) { best = d; level = Mathf.Lerp(s.points[i].y, s.points[i + 1].y, t); along = (i + t) * Spacing; }
                    }
                }
                return Mathf.Sqrt(best);
            }
        }

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

        /// <summary>
        /// Distance to the nearest stream (infinite without streams) and its water level there. Streams farther than
        /// <paramref name="within"/> are skipped by their bounding boxes (and count as infinitely far).
        /// </summary>
        public float DistanceTo(Vector2 p, out float level, float within = float.MaxValue)
        {
            float best = float.MaxValue; level = -1f;
            bool skip = within < float.MaxValue;
            if (skip && (boxes.Count != Streams.Count || boxedPoints != PointCount())) Box();
            for (int i = 0; i < Streams.Count; i++)
            {
                if (skip) { var b = boxes[i]; if (p.x < b.xMin - within || p.x > b.xMax + within || p.y < b.yMin - within || p.y > b.yMax + within) continue; }
                float d = Nearest(Streams[i], p, out float l, out _);
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
                if (Nearest(Streams[i], p, out _, out float along) < HalfWidth * Streams[i].WidthAlong(along) + margin) return true;
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
            foreach (var s in streams) Streams.Add(s.Copy());
            Rebuild();
        }

        /// <summary>Flowing ribbons of water along every stream (UV.y = distance downstream drives the flow).</summary>
        public void Rebuild()
        {
            boxes.Clear();   // (the streams may have changed: boxed again when next needed)
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
                // Across the ribbon: a soft edge each side that fades out and dips under the bank (so the water's outline
                // is the ribbon's own smooth edge, never the land's coarse grid), and the open water between.
                float[] offs = { -1f, -0.72f, -0.36f, 0f, 0.36f, 0.72f, 1f };
                int across = offs.Length - 1;
                // Where it runs into a pond (or another stream) it carries on a little way under that water, fading out:
                // no edge where they meet.
                var wet = new bool[n];
                var under = new float[n];   // how far it has gone under the water it runs into
                var cover = new float[n];   // that water's surface (or -1)
                for (int i = 0; i < n; i++)
                {
                    var q = s.points[i];
                    cover[i] = -1f;
                    if (InPond(Terrain, q.x, q.z)) { wet[i] = true; cover[i] = Terrain.WaterLevelAt(q.x, q.z); }
                    else if ((i < 10 && s.JoinsAtStart) || (i > n - 11 && s.JoinsAtEnd))
                        foreach (var o in Streams)
                        {
                            if (o == s || o.points.Count < 2) continue;
                            if (Nearest(o, new Vector2(q.x, q.z), out float ol, out float oa) < HalfWidth * o.WidthAlong(oa)) { wet[i] = true; cover[i] = ol; break; }
                        }
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    float run = 0f;
                    for (int k = 0; k < n; k++)
                    {
                        int i = pass == 0 ? k : n - 1 - k;
                        run = wet[i] ? run + Spacing : 0f;
                        under[i] = pass == 0 ? run : Mathf.Min(under[i], run);
                    }
                }
                // ...and how far it still has to go to reach that water: it shoals as it nears a lake, its colour easing to
                // the lake's shallows instead of meeting them deep.
                var toWet = new float[n];
                for (int pass = 0; pass < 2; pass++)
                {
                    float run = float.MaxValue;
                    for (int k = 0; k < n; k++)
                    {
                        int i = pass == 0 ? k : n - 1 - k;
                        run = wet[i] ? 0f : run + Spacing;
                        toWet[i] = pass == 0 ? run : Mathf.Min(toWet[i], run);
                    }
                }
                const float fade = 0.025f;
                float startTaper = wet[0] || s.JoinsAtStart ? 0f : 0.05f, endTaper = wet[n - 1] || s.JoinsAtEnd ? 0f : 0.05f;
                for (int i = 0; i < n; i++)
                {
                    var p = s.points[i];
                    var t = s.points[Mathf.Min(i + 1, n - 1)] - s.points[Mathf.Max(i - 1, 0)]; t.y = 0f; t.Normalize();
                    var side = Vector3.Cross(Vector3.up, t);
                    float along = i * Spacing, length = (n - 1) * Spacing;
                    float taper = (startTaper > 0f ? Mathf.Clamp01(along / startTaper) : 1f) * (endTaper > 0f ? Mathf.Clamp01((length - along) / endTaper) : 1f);
                    float hw = (HalfWidth * s.WidthAt(i) + Margin) * Mathf.Lerp(0.45f, 1f, taper);
                    float drop = (s.points[Mathf.Max(i - 1, 0)].y - s.points[Mathf.Min(i + 1, n - 1)].y) / (2f * Spacing);
                    float rapids = Mathf.Clamp01(drop * 12f);
                    // In the water it runs into: at that water's surface, calm, fading out quickly.
                    float surface = wet[i] ? Mathf.Min(p.y, cover[i]) : p.y + 0.0004f;
                    float shown = wet[i] ? 1f - Mathf.Clamp01(under[i] / fade) : 1f;
                    if (wet[i]) rapids = 0f;
                    for (int k = 0; k <= across; k++)
                    {
                        float off = offs[k], u = (off + 1f) * 0.5f;
                        bool rim = Mathf.Abs(off) > 0.99f;
                        float depth = (1f - off * off) * (wet[i] ? 0.3f : Mathf.Lerp(0.3f, 0.75f, Mathf.SmoothStep(0f, 1f, toWet[i] / 0.1f)));
                        float shore = Mathf.Max(rim ? 1f : Mathf.Abs(off) > 0.6f ? 0.3f : 0f, rapids * 0.45f);   // (whitewater streaks along the edge, not a white block)
                        var at = new Vector3(p.x, surface - (rim ? 0.003f : 0f), p.z) + side * off * hw;
                        int v = b.AddVertex(at, Vector3.up, new Color(depth, shore, 0.5f + rapids * 0.5f, rim ? 0f : shown));
                        b.Uvs[v] = new Vector2(u * 0.6f, along);
                    }
                    if (i > 0 && !(wet[i] && wet[i - 1] && under[i] > fade && under[i - 1] > fade))
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
                    var spray = TrafficSystem.Smoke(transform, transform.TransformPoint(s.points[foot] + Vector3.up * 0.002f), Smoke ?? material, new Color(0.92f, 0.96f, 1f, 0.55f), 10f * s.WidthAt(foot), 0.007f * s.WidthAt(foot), Vector3.up, 0.012f);
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
