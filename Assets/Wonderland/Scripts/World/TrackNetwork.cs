using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>One end of a track piece: <see cref="End"/> 0 is its start (s = 0), 1 its end (s = length).</summary>
    public struct EdgeEnd : IEquatable<EdgeEnd>
    {
        public int Edge, End;
        public EdgeEnd(int edge, int end) { Edge = edge; End = end; }
        public bool Equals(EdgeEnd o) => Edge == o.Edge && End == o.End;
        public override bool Equals(object o) => o is EdgeEnd e && Equals(e);
        public override int GetHashCode() => Edge * 2 + End;
        public static bool operator ==(EdgeEnd a, EdgeEnd b) => a.Equals(b);
        public static bool operator !=(EdgeEnd a, EdgeEnd b) => !a.Equals(b);
    }

    /// <summary>A piece of track between junctions (or dead ends), or a whole loop without any.</summary>
    public sealed class TrackEdge
    {
        public TrackLayout Layout;
        public readonly int[] Joint = { -1, -1 };   // joint at the start / end; -1 is a dead end (buffers)
        public bool[] Tunnel;                       // per point: inside a tunnel
        public bool Closed => Layout.Closed;
    }

    /// <summary>
    /// A switch: three track ends meeting at one point. Trains arriving from the <see cref="Trunk"/> take the branch the
    /// switch is set to; trains arriving from either branch always run through onto the trunk (the points spring over).
    /// </summary>
    public sealed class TrackJoint
    {
        public Vector3 Position;
        public EdgeEnd Trunk;
        public readonly List<EdgeEnd> Ends = new();   // all ends (trunk and branches); 2 while being built, then merged away
        public int Setting;                             // which branch (0 / 1)
        public bool Pinned;                             // set by hand: trains don't change it
        public bool FlipPending;                        // a train went through facing it: alternate once it is clear

        public EdgeEnd Branch(int i)
        {
            int k = 0;
            foreach (var e in Ends) if (e != Trunk) { if (k == i) return e; k++; }
            return Trunk;
        }
    }

    /// <summary>Where two pieces of track cross at the same level (a diamond crossing).</summary>
    public struct TrackCrossing
    {
        public int EdgeA, EdgeB;
        public float SA, SB;
        public Vector3 Position;
    }

    /// <summary>
    /// The railway as a graph of track pieces joined by switches. Construction only ever adds pieces and joints and then
    /// <see cref="Normalize"/>s: two ends meeting become one piece (a piece meeting its own other end becomes a loop), an
    /// end with nothing left at its joint becomes a dead end. So a switch always has exactly three ends.
    /// </summary>
    public sealed class TrackNetwork
    {
        public const float FoulDistance = 0.16f;   // trains on different branches are clear of each other this far from a switch
        public const float CrossingZone = 0.055f;  // a diamond crossing is occupied within this distance of its centre
        public readonly List<TrackEdge> Edges = new();
        public readonly List<TrackJoint> Joints = new();
        public readonly List<TrackCrossing> Crossings = new();
        private readonly Dictionary<long, List<(int edge, int index)>> grid = new();
        private const float GridCell = 0.08f;

        public bool Empty => Edges.Count == 0;

        // ------------------------------------------------------------------ one way

        /// <summary>
        /// The railway runs one way: trains travel along each piece from its end 0 to its end 1 (round a loop, the way its
        /// points go). Every switch then either splits one line into two going the same way (trains arrive on the trunk)
        /// or merges two into one (they leave on the trunk), so trains never meet head-on. False for a network that can't
        /// run one way (a line that turns back into itself): trains then shuttle as before.
        /// </summary>
        public bool OneWay { get; private set; }
        public string TwoWayReason { get; private set; }

        /// <summary>Do trains arrive at a switch through this end (true), or leave through it?</summary>
        public static bool Arrives(EdgeEnd e) => e.End == 1;

        /// <summary>
        /// Turns pieces round so the whole network runs one way. Which way each connected part runs is free: it is chosen
        /// to agree with <paramref name="agreement"/> (per piece: positive if it runs the way it should already, negative
        /// if it would rather be turned; e.g. the way the railway ran before a new piece was added), so the old railway
        /// keeps its direction and a loop being joined to it turns to match. False (nothing turned) when some switch can't
        /// be made one way, with the reason in <paramref name="why"/>.
        /// </summary>
        public bool Orient(Func<int, float> agreement, out string why)
        {
            why = null;
            int n = Edges.Count;
            var flip = new int[n]; var seen = new bool[n];
            var links = new List<(int other, int parity)>[n];
            for (int i = 0; i < n; i++) links[i] = new List<(int, int)>();
            foreach (var j in Joints)
            {
                if (j == null || j.Ends.Count != 3) continue;
                var t = j.Trunk;
                foreach (var b in j.Ends)
                {
                    if (b == t) continue;
                    // Trunk and branch must differ: one arrives, the other leaves.
                    int p = (Arrives(t) ? 1 : 0) ^ (Arrives(b) ? 1 : 0) ^ 1;
                    if (t.Edge == b.Edge) { if (p != 0) { why = TurnBack; OneWay = false; TwoWayReason = why; return false; } continue; }
                    links[t.Edge].Add((b.Edge, p)); links[b.Edge].Add((t.Edge, p));
                }
            }
            var comp = new List<int>(); var queue = new Queue<int>();
            for (int s = 0; s < n; s++)
            {
                if (seen[s]) continue;
                comp.Clear(); queue.Enqueue(s); seen[s] = true; flip[s] = 0;
                while (queue.Count > 0)
                {
                    int e = queue.Dequeue(); comp.Add(e);
                    foreach (var (o, p) in links[e])
                    {
                        int want = flip[e] ^ p;
                        if (!seen[o]) { seen[o] = true; flip[o] = want; queue.Enqueue(o); }
                        else if (flip[o] != want) { why = TurnBack; OneWay = false; TwoWayReason = why; return false; }
                    }
                }
                float keep = 0f;
                foreach (int e in comp) keep += (agreement?.Invoke(e) ?? 0f) * (flip[e] == 0 ? 1f : -1f);
                if (keep < 0f) foreach (int e in comp) flip[e] ^= 1;
            }
            for (int e = 0; e < n; e++) if (flip[e] == 1) Reverse(e);
            OneWay = true; TwoWayReason = null;
            return true;
        }

        public const string TurnBack = "Trains would have to turn round here: the new line must run the same way as the track it joins - follow the arrows";

        /// <summary>
        /// How much a piece agrees with the way the railway ran in <paramref name="before"/> (positive: same way): its
        /// direction compared with the old track it lies on, sampled along it. Zero for new track.
        /// </summary>
        public float Agreement(int edge, TrackNetwork before)
        {
            if (before == null || before.Empty) return 0f;
            var l = Edges[edge].Layout;
            float sum = 0f;
            for (float s = 0.02f; s < l.Length; s += 0.1f)
            {
                var p = l.Evaluate(s, out var t);
                if (!before.Nearest(p, out int oe, out float os, out float d, 0.004f)) continue;
                before.Edges[oe].Layout.Evaluate(os, out var ot);
                sum += Vector3.Dot(new Vector3(t.x, 0f, t.z).normalized, new Vector3(ot.x, 0f, ot.z).normalized) > 0f ? 0.1f : -0.1f;
            }
            return sum;
        }

        /// <summary>
        /// Pieces trains can carry on from, running one way (they don't lead only into a dead end): what the trains'
        /// routing prefers at a switch. Cached until the network changes.
        /// </summary>
        public bool Through(int edge)
        {
            if (through == null || through.Length != Edges.Count || throughVersion != Version) ComputeThrough();
            return edge >= 0 && edge < through.Length && through[edge];
        }

        private bool[] through;
        private int throughVersion = -1;

        /// <summary>
        /// The circuit a piece belongs to: pieces trains can go round between, running one way (a strongly connected part).
        /// Two loops joined by one link are two circuits: trains can go from one to the other but not back.
        /// </summary>
        public int CircuitOf(int edge)
        {
            if (circuit == null || circuit.Length != Edges.Count || circuitVersion != Version) ComputeCircuits();
            return edge >= 0 && edge < circuit.Length ? circuit[edge] : -1;
        }

        /// <summary>Circuits trains can go round in (with at least one way round them).</summary>
        public int Circuits { get { CircuitOf(0); return circuitCount; } }

        /// <summary>
        /// One-way traps: parts of one connected railway that trains can enter but never leave again (a single link from
        /// one loop to another). Zero when every train can get everywhere and back.
        /// </summary>
        public int Traps
        {
            get
            {
                if (Empty || !OneWay) return 0;
                CircuitOf(0);
                // Circuits with a way in from another circuit but no way out to one: each extra one is a trap.
                var into = new HashSet<int>(); var outOf = new HashSet<int>();
                for (int e = 0; e < Edges.Count; e++)
                    foreach (int n in Successors(e))
                        if (circuit[n] != circuit[e]) { outOf.Add(circuit[e]); into.Add(circuit[n]); }
                int traps = 0;
                foreach (int c in into) if (!outOf.Contains(c) && cyclic.Contains(c)) traps++;
                return traps;
            }
        }

        private int[] circuit;
        private int circuitVersion = -1, circuitCount;
        private readonly HashSet<int> cyclic = new();

        /// <summary>Pieces a train can run onto from the far end of this one (running one way).</summary>
        private IEnumerable<int> Successors(int e)
        {
            if (Edges[e].Closed) { yield return e; yield break; }
            var exit = new EdgeEnd(e, 1);
            int ji = JointAt(exit);
            if (ji < 0) yield break;
            var j = Joints[ji];
            foreach (var x in j.Ends)
                if (x != exit && (exit == j.Trunk ? x != j.Trunk : x == j.Trunk) && !Arrives(x)) yield return x.Edge;
        }

        private void ComputeCircuits()
        {
            // Tarjan's strongly connected components over the pieces.
            int n = Edges.Count;
            circuit = new int[n]; circuitCount = 0; cyclic.Clear();
            var index = new int[n]; var low = new int[n]; var on = new bool[n];
            for (int i = 0; i < n; i++) index[i] = -1;
            var stack = new Stack<int>(); int counter = 0;
            void Visit(int v)
            {
                index[v] = low[v] = counter++; stack.Push(v); on[v] = true;
                foreach (int w in Successors(v))
                {
                    if (index[w] < 0) { Visit(w); low[v] = Mathf.Min(low[v], low[w]); }
                    else if (on[w]) low[v] = Mathf.Min(low[v], index[w]);
                }
                if (low[v] != index[v]) return;
                int size = 0; bool self = false;
                while (true)
                {
                    int w = stack.Pop(); on[w] = false; circuit[w] = circuitCount; size++;
                    foreach (int x in Successors(w)) if (x == w) self = true;
                    if (w == v) break;
                }
                if (size > 1 || self) cyclic.Add(circuitCount);
                circuitCount++;
            }
            for (int v = 0; v < n; v++) if (index[v] < 0) Visit(v);
            circuitVersion = Version;
        }
        /// <summary>Bumped whenever the shape of the network changes.</summary>
        public int Version { get; private set; }

        private void ComputeThrough()
        {
            through = new bool[Edges.Count];
            for (int e = 0; e < through.Length; e++) through[e] = true;
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int e = 0; e < through.Length; e++)
                {
                    if (!through[e] || Edges[e].Closed) continue;
                    var exit = new EdgeEnd(e, 1);
                    int ji = JointAt(exit);
                    bool on = false;
                    if (ji >= 0)
                    {
                        var j = Joints[ji];
                        foreach (var x in j.Ends)
                            if (x != exit && (exit == j.Trunk ? x != j.Trunk : x == j.Trunk) && !Arrives(x) && through[x.Edge]) on = true;
                    }
                    if (!on) { through[e] = false; changed = true; }
                }
            }
            throughVersion = Version;
        }
        public float TotalLength { get { float l = 0f; foreach (var e in Edges) l += e.Layout.Length; return l; } }

        // ------------------------------------------------------------------ geometry queries

        public Vector3 EndPoint(EdgeEnd e)
        {
            var l = Edges[e.Edge].Layout;
            return e.End == 0 ? l.Points[0] : l.Points[^1];
        }

        /// <summary>Direction leaving the joint into the piece at this end (horizontal, unit).</summary>
        public Vector3 IntoEdge(EdgeEnd e)
        {
            var l = Edges[e.Edge].Layout;
            float s = e.End == 0 ? Mathf.Min(0.03f, l.Length) : Mathf.Max(0f, l.Length - 0.03f);
            var p = l.Evaluate(s, out _) - EndPoint(e); p.y = 0f;
            return p.sqrMagnitude > 1e-10f ? p.normalized : Vector3.forward;
        }

        public float EndS(EdgeEnd e) => e.End == 0 ? 0f : Edges[e.Edge].Layout.Length;

        public int JointAt(EdgeEnd e) => Edges[e.Edge].Joint[e.End];

        /// <summary>Where a train leaving a piece through <paramref name="exit"/> continues (null at a dead end).</summary>
        public EdgeEnd? Next(EdgeEnd exit, Func<TrackJoint, int, EdgeEnd> choose = null)
        {
            int j = JointAt(exit);
            if (j < 0) return null;
            var joint = Joints[j];
            if (exit != joint.Trunk) return joint.Trunk;
            return choose != null ? choose(joint, j) : joint.Branch(joint.Setting);
        }

        /// <summary>The track point nearest <paramref name="p"/> (horizontally): piece, arc length and distance.</summary>
        public bool Nearest(Vector3 p, out int edge, out float s, out float distance, float within = float.MaxValue)
        {
            edge = -1; s = 0f; distance = float.MaxValue;
            if (Edges.Count == 0) return false;
            float best = float.MaxValue;
            if (within < 0.5f && grid.Count > 0)
            {
                int r = Mathf.CeilToInt(within / GridCell);
                int cx = Mathf.FloorToInt(p.x / GridCell), cz = Mathf.FloorToInt(p.z / GridCell);
                for (int oz = -r; oz <= r; oz++)
                for (int ox = -r; ox <= r; ox++)
                {
                    if (!grid.TryGetValue(Key(cx + ox, cz + oz), out var list)) continue;
                    foreach (var (e, i) in list) Segment(e, i, p, ref best, ref edge, ref s);
                }
            }
            else
                for (int e = 0; e < Edges.Count; e++)
                {
                    var l = Edges[e].Layout;
                    int count = l.Closed ? l.Points.Count : l.Points.Count - 1;
                    for (int i = 0; i < count; i++) Segment(e, i, p, ref best, ref edge, ref s);
                }
            distance = Mathf.Sqrt(best);
            return edge >= 0 && distance <= within;
        }

        private void Segment(int e, int i, Vector3 p, ref float best, ref int edge, ref float s)
        {
            var l = Edges[e].Layout;
            var a = l.Points[i]; var b = l.Points[(i + 1) % l.Points.Count];
            var ab = new Vector2(b.x - a.x, b.z - a.z); var ap = new Vector2(p.x - a.x, p.z - a.z);
            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
            float d = (ap - ab * t).sqrMagnitude;
            if (d < best) { best = d; edge = e; s = l.Cum[i] + t * (i + 1 < l.Points.Count ? l.Cum[i + 1] - l.Cum[i] : l.Length - l.Cum[i]); }
        }

        public float DistanceTo(Vector3 p)
        {
            Nearest(p, out _, out _, out float d, 0.3f);
            return d;
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>Rebuilds the lookup grid and the diamond crossings; call after any change of the pieces.</summary>
        public void Reindex()
        {
            Version++;
            grid.Clear();
            for (int e = 0; e < Edges.Count; e++)
            {
                var l = Edges[e].Layout;
                int count = l.Closed ? l.Points.Count : l.Points.Count - 1;
                for (int i = 0; i < count; i++)
                {
                    var a = l.Points[i]; var b = l.Points[(i + 1) % l.Points.Count];
                    var cells = new HashSet<long> { Key(Mathf.FloorToInt(a.x / GridCell), Mathf.FloorToInt(a.z / GridCell)), Key(Mathf.FloorToInt(b.x / GridCell), Mathf.FloorToInt(b.z / GridCell)) };
                    foreach (var k in cells)
                    {
                        if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<(int, int)>();
                        list.Add((e, i));
                    }
                }
            }
            FindCrossings();
        }

        private void FindCrossings()
        {
            Crossings.Clear();
            var seen = new HashSet<long>();
            foreach (var list in grid.Values)
                for (int a = 0; a < list.Count; a++)
                for (int b = a + 1; b < list.Count; b++)
                {
                    var (ea, ia) = list[a]; var (eb, ib) = list[b];
                    if (ea > eb || (ea == eb && ia > ib)) { (ea, ia, eb, ib) = (eb, ib, ea, ia); }
                    if (ea == eb && Mathf.Abs(ia - ib) < 4) continue;
                    if (ea == eb && Edges[ea].Closed && Edges[ea].Layout.Points.Count - Mathf.Abs(ia - ib) < 4) continue;
                    long key = ((long)ea * 100000 + ia) * 1000000 + eb * 100000 + ib;
                    if (!seen.Add(key)) continue;
                    if (!SegmentCrossing(ea, ia, eb, ib, out var x, out float ta, out float tb)) continue;
                    if (NearJoint(x, 0.06f)) continue;   // pieces meeting at a switch touch there, they don't cross
                    var la = Edges[ea].Layout; var lb = Edges[eb].Layout;
                    float sa = la.Cum[ia] + ta * SegLength(la, ia), sb = lb.Cum[ib] + tb * SegLength(lb, ib);
                    bool dup = false;
                    foreach (var c in Crossings) if ((c.Position - x).sqrMagnitude < 0.02f * 0.02f) { dup = true; break; }
                    if (!dup) Crossings.Add(new TrackCrossing { EdgeA = ea, SA = sa, EdgeB = eb, SB = sb, Position = x });
                }
        }

        private static float SegLength(TrackLayout l, int i) => i + 1 < l.Points.Count ? l.Cum[i + 1] - l.Cum[i] : l.Length - l.Cum[i];

        private bool SegmentCrossing(int ea, int ia, int eb, int ib, out Vector3 x, out float ta, out float tb)
        {
            var la = Edges[ea].Layout; var lb = Edges[eb].Layout;
            var a = la.Points[ia]; var b = la.Points[(ia + 1) % la.Points.Count];
            var c = lb.Points[ib]; var d = lb.Points[(ib + 1) % lb.Points.Count];
            x = default; ta = tb = 0f;
            if (!la.Closed && ia + 1 >= la.Points.Count) return false;
            if (!lb.Closed && ib + 1 >= lb.Points.Count) return false;
            var r = new Vector2(b.x - a.x, b.z - a.z); var q = new Vector2(d.x - c.x, d.z - c.z);
            float den = r.x * q.y - r.y * q.x;
            if (Mathf.Abs(den) < 1e-10f) return false;
            var ac = new Vector2(c.x - a.x, c.z - a.z);
            ta = (ac.x * q.y - ac.y * q.x) / den; tb = (ac.x * r.y - ac.y * r.x) / den;
            if (ta < 0f || ta >= 1f || tb < 0f || tb >= 1f) return false;
            x = Vector3.Lerp(a, b, ta);
            return true;
        }

        public bool NearJoint(Vector3 p, float within)
        {
            foreach (var j in Joints)
                if (new Vector2(j.Position.x - p.x, j.Position.z - p.z).sqrMagnitude < within * within) return true;
            return false;
        }

        /// <summary>Arc-length distance along a piece from <paramref name="s"/> to its nearest switch (infinite if none).</summary>
        public float DistanceToJoint(int edge, float s)
        {
            var e = Edges[edge];
            float d = float.MaxValue;
            if (e.Joint[0] >= 0) d = Mathf.Min(d, s);
            if (e.Joint[1] >= 0) d = Mathf.Min(d, e.Layout.Length - s);
            return d;
        }

        public float DistanceToCrossing(int edge, float s)
        {
            float d = float.MaxValue;
            var l = Edges[edge].Layout;
            foreach (var c in Crossings)
            {
                if (c.EdgeA == edge) d = Mathf.Min(d, ArcGap(l, s, c.SA));
                if (c.EdgeB == edge) d = Mathf.Min(d, ArcGap(l, s, c.SB));
            }
            return d;
        }

        public static float ArcGap(TrackLayout l, float a, float b)
        {
            float d = Mathf.Abs(a - b);
            return l.Closed ? Mathf.Min(d, l.Length - d) : d;
        }

        // ------------------------------------------------------------------ construction

        public int AddEdge(TrackLayout layout)
        {
            Edges.Add(new TrackEdge { Layout = layout });
            return Edges.Count - 1;
        }

        /// <summary>
        /// Splits a piece at point <paramref name="index"/>. Returns the end arriving at the split point (in the piece's
        /// direction) and the end leaving it. A loop becomes one piece starting and ending there.
        /// </summary>
        public (EdgeEnd arriving, EdgeEnd leaving) Split(int edge, int index)
        {
            var e = Edges[edge];
            var pts = e.Layout.Points;
            if (e.Closed)
            {
                var open = new List<Vector3>(pts.Count + 1);
                for (int k = 0; k <= pts.Count; k++) open.Add(pts[(index + k) % pts.Count]);
                e.Layout = new TrackLayout(open, false);
                e.Tunnel = null;
                return (new EdgeEnd(edge, 1), new EdgeEnd(edge, 0));
            }
            index = Mathf.Clamp(index, 1, pts.Count - 2);
            var first = pts.GetRange(0, index + 1);
            var second = pts.GetRange(index, pts.Count - index);
            int endJoint = e.Joint[1];
            e.Layout = new TrackLayout(first, false);
            e.Tunnel = null;
            e.Joint[1] = -1;
            int b = AddEdge(new TrackLayout(second, false));
            Edges[b].Joint[1] = endJoint;
            if (endJoint >= 0) Replace(Joints[endJoint], new EdgeEnd(edge, 1), new EdgeEnd(b, 1));
            return (new EdgeEnd(edge, 1), new EdgeEnd(b, 0));
        }

        /// <summary>Makes a joint from track ends at one point; its trunk is the end pointing away from the others.</summary>
        public int Join(Vector3 position, params EdgeEnd[] ends)
        {
            var j = new TrackJoint { Position = position };
            foreach (var e in ends) { j.Ends.Add(e); Edges[e.Edge].Joint[e.End] = Joints.Count; }
            Joints.Add(j);
            ChooseTrunk(j);
            return Joints.Count - 1;
        }

        private void ChooseTrunk(TrackJoint j)
        {
            if (j.Ends.Count < 3) { j.Trunk = j.Ends[0]; return; }
            float best = float.MaxValue;
            foreach (var e in j.Ends)
            {
                var others = Vector3.zero;
                foreach (var o in j.Ends) if (o != e) others += IntoEdge(o);
                float d = Vector3.Dot(IntoEdge(e), others);
                if (d < best) { best = d; j.Trunk = e; }
            }
        }

        private static void Replace(TrackJoint j, EdgeEnd from, EdgeEnd to)
        {
            for (int i = 0; i < j.Ends.Count; i++) if (j.Ends[i] == from) j.Ends[i] = to;
            if (j.Trunk == from) j.Trunk = to;
        }

        /// <summary>Takes a piece out; what remains is tidied up by <see cref="Normalize"/>.</summary>
        public void RemoveEdge(int edge)
        {
            var e = Edges[edge];
            for (int k = 0; k < 2; k++)
            {
                if (e.Joint[k] < 0) continue;
                var j = Joints[e.Joint[k]];
                j.Ends.RemoveAll(x => x.Edge == edge);
                e.Joint[k] = -1;
            }
            e.Layout = null;
            Normalize();
        }

        public void Reverse(int edge)
        {
            var e = Edges[edge];
            var pts = new List<Vector3>(e.Layout.Points); pts.Reverse();
            e.Layout = new TrackLayout(pts, e.Closed);
            if (e.Tunnel != null) Array.Reverse(e.Tunnel);
            int j0 = e.Joint[0], j1 = e.Joint[1];
            // Update the joints first (an edge may meet the same joint at both ends).
            var a = new EdgeEnd(edge, 0); var b = new EdgeEnd(edge, 1); var tmp = new EdgeEnd(-2, 0);
            if (j0 >= 0) Replace(Joints[j0], a, tmp);
            if (j1 >= 0) Replace(Joints[j1], b, a);
            if (j0 >= 0) Replace(Joints[j0], tmp, b);
            e.Joint[0] = j1; e.Joint[1] = j0;
        }

        /// <summary>
        /// Tidies the graph: joints with two ends merge their pieces, joints with one become dead ends, empty joints and
        /// removed pieces disappear. Keeps every switch at exactly three ends.
        /// </summary>
        public void Normalize()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int ji = 0; ji < Joints.Count; ji++)
                {
                    var j = Joints[ji];
                    if (j == null) continue;
                    if (j.Ends.Count == 3) { if (!j.Ends.Contains(j.Trunk)) ChooseTrunk(j); continue; }
                    if (j.Ends.Count == 2)
                    {
                        // Merge so the running direction is kept: the piece trains arrive on, then the one they leave on.
                        var a = j.Ends[0]; var b = j.Ends[1];
                        if (!Arrives(a) && Arrives(b)) (a, b) = (b, a);
                        Merge(ji, a, b); changed = true; continue;
                    }
                    foreach (var e in j.Ends) Edges[e.Edge].Joint[e.End] = -1;
                    Joints[ji] = null;
                    changed = true;
                }
            }
            Compact();
        }

        private void Merge(int ji, EdgeEnd a, EdgeEnd b)
        {
            Joints[ji] = null;
            if (a.Edge == b.Edge)
            {
                // A piece meeting itself: a loop.
                var e = Edges[a.Edge];
                var pts = new List<Vector3>(e.Layout.Points);
                if (pts.Count > 2 && (pts[^1] - pts[0]).sqrMagnitude < 0.004f * 0.004f) pts.RemoveAt(pts.Count - 1);
                e.Layout = new TrackLayout(pts, true);
                e.Tunnel = null;
                e.Joint[0] = e.Joint[1] = -1;
                return;
            }
            // Orient: a's piece ends at the joint, b's piece starts there.
            Edges[a.Edge].Joint[a.End] = -1; Edges[b.Edge].Joint[b.End] = -1;
            if (a.End == 0) Reverse(a.Edge);
            if (b.End == 1) Reverse(b.Edge);
            var ea = Edges[a.Edge]; var eb = Edges[b.Edge];
            var merged = new List<Vector3>(ea.Layout.Points);
            for (int i = 1; i < eb.Layout.Points.Count; i++) merged.Add(eb.Layout.Points[i]);
            ea.Layout = new TrackLayout(merged, false);
            ea.Tunnel = null;
            ea.Joint[1] = eb.Joint[1];
            if (ea.Joint[1] >= 0) Replace(Joints[ea.Joint[1]], new EdgeEnd(b.Edge, 1), new EdgeEnd(a.Edge, 1));
            eb.Layout = null;
            eb.Joint[0] = eb.Joint[1] = -1;
        }

        private void Compact()
        {
            var edgeMap = new int[Edges.Count];
            var edges = new List<TrackEdge>();
            for (int i = 0; i < Edges.Count; i++) { edgeMap[i] = Edges[i].Layout != null ? edges.Count : -1; if (Edges[i].Layout != null) edges.Add(Edges[i]); }
            var jointMap = new int[Joints.Count];
            var joints = new List<TrackJoint>();
            for (int i = 0; i < Joints.Count; i++) { jointMap[i] = Joints[i] != null ? joints.Count : -1; if (Joints[i] != null) joints.Add(Joints[i]); }
            foreach (var e in edges)
                for (int k = 0; k < 2; k++) e.Joint[k] = e.Joint[k] >= 0 ? jointMap[e.Joint[k]] : -1;
            foreach (var j in joints)
            {
                for (int i = 0; i < j.Ends.Count; i++) j.Ends[i] = new EdgeEnd(edgeMap[j.Ends[i].Edge], j.Ends[i].End);
                j.Trunk = new EdgeEnd(edgeMap[j.Trunk.Edge], j.Trunk.End);
            }
            Edges.Clear(); Edges.AddRange(edges);
            Joints.Clear(); Joints.AddRange(joints);
        }

        // ------------------------------------------------------------------ copy / save

        [Serializable]
        public sealed class EdgeData
        {
            public List<Vector3> points;
            public bool closed;
            public int joint0 = -1, joint1 = -1;
        }

        [Serializable]
        public sealed class JointData
        {
            public Vector3 position;
            public int[] ends;   // edge * 2 + end; the first is the trunk
            public int setting;
            public bool pinned;
        }

        [Serializable]
        public sealed class Data
        {
            public List<EdgeData> edges = new();
            public List<JointData> joints = new();
        }

        public Data ToData()
        {
            var d = new Data();
            foreach (var e in Edges) d.edges.Add(new EdgeData { points = new List<Vector3>(e.Layout.Points), closed = e.Closed, joint0 = e.Joint[0], joint1 = e.Joint[1] });
            foreach (var j in Joints)
            {
                var ends = new List<int> { j.Trunk.Edge * 2 + j.Trunk.End };
                foreach (var e in j.Ends) if (e != j.Trunk) ends.Add(e.Edge * 2 + e.End);
                d.joints.Add(new JointData { position = j.Position, ends = ends.ToArray(), setting = j.Setting, pinned = j.Pinned });
            }
            return d;
        }

        public static TrackNetwork FromData(Data d)
        {
            var n = new TrackNetwork();
            if (d?.edges == null) return n;
            foreach (var e in d.edges)
            {
                if (e.points == null || e.points.Count < 2) continue;
                var edge = new TrackEdge { Layout = new TrackLayout(new List<Vector3>(e.points), e.closed) };
                edge.Joint[0] = e.joint0; edge.Joint[1] = e.joint1;
                n.Edges.Add(edge);
            }
            if (d.joints != null)
                foreach (var jd in d.joints)
                {
                    var j = new TrackJoint { Position = jd.position, Setting = jd.setting, Pinned = jd.pinned };
                    foreach (int code in jd.ends) j.Ends.Add(new EdgeEnd(code / 2, code % 2));
                    j.Trunk = j.Ends[0];
                    n.Joints.Add(j);
                }
            n.Heal();
            n.Orient(null, out _);
            n.Reindex();
            return n;
        }

        /// <summary>
        /// Repairs a switch that lists a track end which isn't there (saved before a bug was fixed: a line drawn from a
        /// dead end back onto its own piece joined the wrong end, and trains taking the switch jumped across): each
        /// switch gets the free track end that really meets it. Returns whether anything was repaired.
        /// </summary>
        public bool Heal(float within = 0.005f)
        {
            bool repaired = false;
            // A piece with a jump in it (two pieces merged end to wrong end): cut it at the jump.
            for (int e = 0; e < Edges.Count; e++)
            {
                var l = Edges[e].Layout;
                if (l == null) continue;
                for (int i = 1; i < l.Points.Count; i++)
                {
                    if (Flat(l.Points[i], l.Points[i - 1]) <= 0.03f) continue;
                    if (l.Closed)
                    {
                        var open = new List<Vector3>(l.Points.Count);
                        for (int k = 0; k < l.Points.Count; k++) open.Add(l.Points[(i + k) % l.Points.Count]);
                        Edges[e].Layout = new TrackLayout(open, false);
                    }
                    else
                    {
                        Edges[e].Layout = new TrackLayout(l.Points.GetRange(0, i), false);
                        int b = AddEdge(new TrackLayout(l.Points.GetRange(i, l.Points.Count - i), false));
                        int far = Edges[e].Joint[1];
                        Edges[e].Joint[1] = -1; Edges[b].Joint[1] = far;
                        if (far >= 0) Replace(Joints[far], new EdgeEnd(e, 1), new EdgeEnd(b, 1));
                    }
                    Edges[e].Tunnel = null;
                    repaired = true;
                    e--;   // look at what is left of this piece again
                    break;
                }
            }
            for (int ji = 0; ji < Joints.Count; ji++)
            {
                var j = Joints[ji];
                if (j == null) continue;
                bool changed = false;
                for (int i = j.Ends.Count - 1; i >= 0; i--)
                {
                    var e = j.Ends[i];
                    bool valid = e.Edge >= 0 && e.Edge < Edges.Count && Edges[e.Edge].Layout != null && !Edges[e.Edge].Closed;
                    if (valid && Flat(EndPoint(e), j.Position) <= within) continue;
                    if (valid && Edges[e.Edge].Joint[e.End] == ji) Edges[e.Edge].Joint[e.End] = -1;
                    var free = FreeEndAt(j.Position, within, j.Ends);
                    if (free.Edge >= 0) { if (j.Trunk == e) j.Trunk = free; j.Ends[i] = free; Edges[free.Edge].Joint[free.End] = ji; }
                    else j.Ends.RemoveAt(i);
                    changed = true;
                }
                if (changed) { repaired = true; if (j.Ends.Count == 3) ChooseTrunk(j); }
            }
            // ...and a track end that thinks it meets a switch which doesn't list it.
            for (int e = 0; e < Edges.Count; e++)
                for (int k = 0; k < 2; k++)
                {
                    int ji = Edges[e].Joint[k];
                    if (ji < 0) continue;
                    if (ji >= Joints.Count || Joints[ji] == null || !Joints[ji].Ends.Contains(new EdgeEnd(e, k))) { Edges[e].Joint[k] = -1; repaired = true; }
                }
            if (repaired) Normalize();
            return repaired;
        }

        /// <summary>A track end with no switch at <paramref name="at"/> (not one of <paramref name="taken"/>), or Edge -1.</summary>
        public EdgeEnd FreeEndAt(Vector3 at, float within, List<EdgeEnd> taken = null)
        {
            var best = new EdgeEnd(-1, 0); float bestD = within;
            for (int e = 0; e < Edges.Count; e++)
            {
                if (Edges[e].Layout == null || Edges[e].Closed) continue;
                for (int k = 0; k < 2; k++)
                {
                    var c = new EdgeEnd(e, k);
                    if (Edges[e].Joint[k] >= 0 || taken != null && taken.Contains(c)) continue;
                    float d = Flat(EndPoint(c), at);
                    if (d <= bestD) { bestD = d; best = c; }
                }
            }
            return best;
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        public TrackNetwork Clone() => FromData(ToData());
    }
}
