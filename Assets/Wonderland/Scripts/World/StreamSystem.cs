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
        /// A stream drawn on from the free end of another (its source or its mouth) becomes part of it: one stream, still
        /// running the way the old one ran, its course curving on from the old one's without a kink and its width
        /// changing gently from the old one's to the new (a creek widening into a stream, a stream into a river). Both
        /// ends may carry on streams (joining two into one). The streams joined are taken out of <paramref name="all"/>;
        /// the result is not levelled yet (<see cref="Relevel"/>). Returns <paramref name="fresh"/> itself when it joins none.
        /// </summary>
        public static StreamPath Carry(List<StreamPath> all, StreamPath fresh, TerrainField terrain)
        {
            var p = new List<Vector2>(); var w = new List<float>();
            foreach (var q in fresh.points) { p.Add(new Vector2(q.x, q.z)); w.Add(fresh.Width); }
            var joints = new List<Vector2>();
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
                        if (d >= HalfWidth * Mathf.Max(s.WidthAt(i), fresh.Width) + 0.035f || d >= bestD || !FreeEnd(all, s, k == 0, terrain)) continue;
                        best = s; source = k == 0; bestD = d;
                    }
                }
                if (best == null) continue;
                all.Remove(best);
                var sp = new List<Vector2>(); var sw = new List<float>();
                for (int i = 0; i < best.points.Count; i++) { sp.Add(new Vector2(best.points[i].x, best.points[i].z)); sw.Add(best.WidthAt(i)); }
                // The new stroke must meet the old stream at its free end: turn it round so that end comes last (into a
                // source) or first (out of a mouth).
                bool freshFirst = source;   // into its source: the new water first, then the old stream
                if (freshFirst == atHead) { p.Reverse(); w.Reverse(); }
                var e0 = source ? sp[0] : sp[^1];
                var outward = source ? (sp[0] - sp[Mathf.Min(4, sp.Count - 1)]).normalized : (sp[^1] - sp[Mathf.Max(sp.Count - 5, 0)]).normalized;
                // Whatever was drawn back over the old stream (or right at its end) is not new water.
                if (freshFirst) { while (p.Count > 2 && Vector2.Dot(p[^1] - e0, outward) < 0.006f) { p.RemoveAt(p.Count - 1); w.RemoveAt(w.Count - 1); } p.AddRange(sp); w.AddRange(sw); }
                else
                {
                    while (p.Count > 2 && Vector2.Dot(p[0] - e0, outward) < 0.006f) { p.RemoveAt(0); w.RemoveAt(0); }
                    sp.AddRange(p); sw.AddRange(w); p = sp; w = sw;
                }
                joints.Add(e0);
                oriented = true;
            }
            if (!oriented) return fresh;
            // Evenly spaced again, then smoothed round the joins: the course bends on, the width changes gradually.
            Resample(p, w, out var rp, out var rw);
            foreach (var j in joints)
            {
                int at = 0; float near = float.MaxValue;
                for (int i = 0; i < rp.Count; i++) { float d = (rp[i] - j).sqrMagnitude; if (d < near) { near = d; at = i; } }
                const int reach = 10;
                for (int it = 0; it < 24; it++)
                {
                    var copy = rp.ToArray();
                    for (int i = Mathf.Max(1, at - reach); i <= Mathf.Min(rp.Count - 2, at + reach); i++)
                    {
                        float k = 0.5f * (1f - Mathf.Abs(i - at) / (float)(reach + 1));
                        rp[i] = copy[i] + ((copy[i - 1] + copy[i + 1]) * 0.5f - copy[i]) * k;
                    }
                }
                const int blend = 12;
                float before = rw[Mathf.Max(0, at - blend)], after = rw[Mathf.Min(rw.Count - 1, at + blend)];
                for (int i = Mathf.Max(0, at - blend); i <= Mathf.Min(rw.Count - 1, at + blend); i++)
                    rw[i] = Mathf.Lerp(before, after, Mathf.SmoothStep(0f, 1f, (i - (at - blend)) / (2f * blend)));
            }
            var joined = new StreamPath { width = fresh.Width };
            for (int i = 0; i < rp.Count; i++) { joined.points.Add(new Vector3(rp[i].x, 0f, rp[i].y)); joined.widths.Add(rw[i]); }
            // A stream that now begins in a pond and ends on dry land runs the other way: into the pond.
            if (InPond(terrain, rp[0].x, rp[0].y) && !InPond(terrain, rp[^1].x, rp[^1].y)) { joined.points.Reverse(); joined.widths.Reverse(); }
            return joined;
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
            var region = terrain.RegionAround(new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f), Mathf.Max(maxX - minX, maxZ - minZ) / 2f + widest + 0.02f);
            var near = new List<StreamPath>();
            if (others != null)
                foreach (var o in others)
                {
                    if (o == s || o.points.Count < 2) continue;
                    foreach (var q in o.points) if (q.x > minX - 0.1f && q.x < maxX + 0.1f && q.z > minZ - 0.1f && q.z < maxZ + 0.1f) { near.Add(o); break; }
                }
            float length = PathLength(s);
            for (int z = region.yMin; z < region.yMax; z++)
            for (int x = region.xMin; x < region.xMax; x++)
            {
                var c = new Vector2(terrain.LocalX(x), terrain.LocalZ(z));
                if (c.x < minX - widest || c.x > maxX + widest || c.y < minZ - widest || c.y > maxZ + widest) continue;
                float d = Nearest(s, c, out float level, out float along);
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
                    foreach (var o in near) if (Nearest(o, c, out _, out float oa) < HalfWidth * o.WidthAlong(oa) + Margin) { inOther = true; break; }
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
                        float depth = (1f - off * off) * (wet[i] ? 0.45f : 0.75f);
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
