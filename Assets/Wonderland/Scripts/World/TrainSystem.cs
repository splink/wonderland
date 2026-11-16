using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    public enum TrainKind { Passenger = 0, Goods = 1, Railcar = 2 }

    /// <summary>How switches behave when trains pass them facing (from the trunk).</summary>
    public enum SwitchMode { Explore = 0, Random = 1, Manual = 2 }

    /// <summary>A stretch of one piece of track, traversed from <see cref="S0"/> to <see cref="S1"/> (Dir = +1 or -1).</summary>
    public struct TrackSpan
    {
        public int Edge, Dir;
        public float S0, S1;
        public float Length => (S1 - S0) * Dir;
    }

    /// <summary>Where a train halts: a platform beside the track.</summary>
    public struct StationStop
    {
        public int Edge;
        public float S;
        public int Side;
        public Vector2 House;
    }

    /// <summary>One train on the network.</summary>
    public sealed class Train
    {
        public TrainKind Kind;
        public int Livery;
        public bool Running;
        public float SpeedFactor = 1f;
        public float Speed { get; internal set; }
        public int Dir = 1;                                   // +1: locomotive end leads; -1: pushing (tail leads)
        public float Length { get; internal set; }
        public float Odometer { get; internal set; }
        public float Dwell { get; internal set; }
        public readonly List<TrackSpan> Path = new();         // the track it stands on, from its tail to its locomotive end
        public readonly List<TrackSpan> Claim = new();        // the track reserved ahead of it, in the direction it moves
        public float ClaimLength { get; internal set; }
        public int BlockedBy { get; internal set; } = -1;     // index of the train it is waiting for (-1 none)
        public BlockReason Blocked { get; internal set; }
        public float WaitTime { get; internal set; }
        internal int DepartedStation = -1;
        internal Car[] Cars;
        internal ParticleSystem Smoke;
        internal float Chuff;
        internal Transform Root;

        internal sealed class Car
        {
            public Transform Body;
            public Transform[] Axles;
            public float[] Radius;
            public Transform Rods;
            public CarLayout Layout;
            public float FromFront;                            // distance from the locomotive end to the car centre
            public float Distance;
        }
    }

    public enum BlockReason { None, Train, Signal, Buffers }

    /// <summary>
    /// All trains on the railway network. Each train stands on a path of track spans and moves by growing it at the
    /// leading end and shrinking it at the other. Safety is automatic: before moving, a train reserves the track ahead
    /// (its braking distance and a little more), never track another train stands on or has reserved, and it reserves a
    /// switch or diamond crossing only as a whole, so it never stops in the middle of one. Switches facing a train are set
    /// by <see cref="Mode"/>: Explore alternates after each train (so trains roam the whole network), Random picks, Manual
    /// leaves them as set by hand. Trains halt at stations, reverse at buffers, and when two meet head-on one backs away.
    /// </summary>
    public sealed class TrainSystem : MonoBehaviour
    {
        public const float CruiseSpeed = 0.13f;      // m/s at speed factor 1: a gentle scale speed
        public const float Acceleration = 0.07f;
        public const float DwellSeconds = 3.5f;
        public const float Gap = 0.006f;             // between the cars of one train
        public const float Spacing = 0.03f;          // trains keep at least this far apart
        public const int MaxTrains = 8;
        public const int LiveryCount = 6;

        public readonly List<Train> Trains = new();
        public SwitchMode Mode = SwitchMode.Explore;
        public readonly List<StationStop> Stations = new();
        /// <summary>Set when a switch changed by itself (so its arrows are redrawn).</summary>
        public bool SwitchesChanged;
        public Train Highlighted;
        private TrackSystem track;
        private Material lit, smokeMaterial, ribbonMaterial;
        private Mesh ribbonMesh;
        private System.Random random = new(5);

        private TrackNetwork Net => track.Network;

        public void Init(TrackSystem railway, Material litMaterial, Material smokeMat, Material ui)
        {
            track = railway; lit = litMaterial; smokeMaterial = smokeMat;
            var go = new GameObject("Route ribbon");
            go.transform.SetParent(transform, false);
            ribbonMesh = new Mesh { name = "Route ribbon" };
            ribbonMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = ribbonMesh;
            var r = go.AddComponent<MeshRenderer>();
            ribbonMaterial = new Material(ui) { renderQueue = 3004 };
            r.sharedMaterial = ribbonMaterial; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        public bool Any => Trains.Count > 0;
        public bool AnyRunning { get { foreach (var t in Trains) if (t.Running) return true; return false; } }

        // ------------------------------------------------------------------ consists

        private static CarLayout[] Consist(TrainKind kind) => kind switch
        {
            TrainKind.Goods => new[] { TrainFactory.Loco, TrainFactory.Wagon, TrainFactory.Wagon, TrainFactory.Wagon, TrainFactory.Wagon },
            TrainKind.Railcar => new[] { TrainFactory.Railcar },
            _ => new[] { TrainFactory.Loco, TrainFactory.Coach, TrainFactory.Coach },
        };

        private static Mesh CarMesh(TrainKind kind, int index, int livery) => kind switch
        {
            TrainKind.Goods => index == 0 ? TrainFactory.LocoMesh(livery) : TrainFactory.WagonMesh((index - 1) % TrainFactory.WagonVariants),
            TrainKind.Railcar => TrainFactory.RailcarMesh(livery),
            _ => index == 0 ? TrainFactory.LocoMesh(livery) : TrainFactory.CoachMesh(index - 1, livery),
        };

        public static float LengthOf(TrainKind kind)
        {
            float l = 0f;
            var c = Consist(kind);
            for (int i = 0; i < c.Length; i++) l += c[i].Front - c[i].Rear + (i > 0 ? Gap : 0f);
            return l;
        }

        // ------------------------------------------------------------------ placing and removing

        /// <summary>
        /// Puts a new train on the track, centred on (<paramref name="edge"/>, <paramref name="s"/>) with its locomotive
        /// towards <paramref name="orientation"/> (+1 / -1 along the piece). Returns the train, or null with an error.
        /// </summary>
        public Train Add(TrainKind kind, int livery, int edge, float s, int orientation, out string error)
        {
            error = null;
            if (Trains.Count >= MaxTrains) { error = $"That's plenty of trains ({MaxTrains})"; return null; }
            var t = new Train { Kind = kind, Livery = livery, Length = LengthOf(kind) };
            if (!Lay(t, edge, s, orientation, t.Length * 0.5f, null)) { error = "Not enough track here for this train"; return null; }
            var mine = Intervals(t.Path);
            RefreshHeld();
            for (int i = 0; i < Trains.Count; i++)
                if (Conflicts(mine, held[i], true)) { error = "Another train is in the way"; return null; }
            Build(t);
            Trains.Add(t);
            Pose(t);
            return t;
        }

        /// <summary>Lays a train's path with its locomotive end <paramref name="ahead"/> metres in front of the point.</summary>
        private bool Lay(Train t, int edge, float s, int orientation, float ahead, Func<TrackJoint, int, EdgeEnd> choose)
        {
            t.Path.Clear(); t.Claim.Clear(); t.ClaimLength = 0f;
            t.Path.Add(new TrackSpan { Edge = edge, S0 = s, S1 = s, Dir = orientation >= 0 ? 1 : -1 });
            float front = ExtendFront(t.Path, ahead, choose);
            float back = ExtendBack(t.Path, t.Length - front, choose);
            float missing = t.Length - front - back;
            if (missing > 0.001f) missing -= ExtendFront(t.Path, missing, choose);
            return missing <= 0.002f;
        }

        public void Remove(Train t)
        {
            if (t == null) return;
            Release(t.Root.gameObject);
            Trains.Remove(t);
            if (Highlighted == t) Highlighted = null;
        }

        public void RemoveAll()
        {
            foreach (var t in Trains) Release(t.Root.gameObject);
            Trains.Clear();
            Highlighted = null;
        }

        private static void Release(UnityEngine.Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        /// <summary>The train with a car nearest a local point within <paramref name="within"/>, or null.</summary>
        public Train Near(Vector3 p, float within)
        {
            Train best = null; float bestD = within;
            foreach (var t in Trains)
                foreach (var c in t.Cars)
                {
                    float d = new Vector2(c.Body.localPosition.x - p.x, c.Body.localPosition.z - p.z).magnitude;
                    if (d < bestD) { bestD = d; best = t; }
                }
            return best;
        }

        /// <summary>Points along every car's centre line (front, middle, back): for clearance checks.</summary>
        public List<Vector3> CarSamples(Train t)
        {
            var list = new List<Vector3>();
            foreach (var c in t.Cars)
            {
                float u = t.Length - c.FromFront;
                foreach (float o in new[] { c.Layout.Front - 0.004f, 0f, c.Layout.Rear + 0.004f }) list.Add(PointAt(t.Path, u + o, out _));
            }
            return list;
        }

        public Vector3 LocoPosition(Train t) => t.Cars[0].Body.localPosition;
        public Quaternion LocoRotation(Train t) => t.Cars[0].Body.localRotation;
        public Vector3 CentrePosition(Train t) => PointAt(t.Path, t.Length * 0.5f, out _);

        /// <summary>The piece and arc length under a train's locomotive (for tunnels and steam).</summary>
        public (int edge, float s) LocoSpot(Train t)
        {
            float u = t.Length - t.Cars[0].FromFront;
            float acc = 0f;
            foreach (var sp in t.Path)
            {
                if (acc + sp.Length >= u) return (sp.Edge, sp.S0 + sp.Dir * (u - acc));
                acc += sp.Length;
            }
            var last = t.Path[^1];
            return (last.Edge, last.S1);
        }

        public bool InTunnel(Train t) { var (e, s) = LocoSpot(t); return track.InTunnel(e, s); }

        // ------------------------------------------------------------------ saving

        [Serializable]
        public sealed class TrainData
        {
            public int kind, livery, dir = 1;
            public float x, z, fx, fz = 1f, speed = 1f;
            public bool running;
        }

        public List<TrainData> ToData()
        {
            var list = new List<TrainData>();
            foreach (var t in Trains)
            {
                var front = PointAt(t.Path, t.Length, out var tangent);
                list.Add(new TrainData { kind = (int)t.Kind, livery = t.Livery, dir = t.Dir, x = front.x, z = front.z, fx = tangent.x, fz = tangent.z, speed = t.SpeedFactor, running = t.Running });
            }
            return list;
        }

        public void FromData(IEnumerable<TrainData> data)
        {
            RemoveAll();
            if (data == null || Net.Empty) return;
            foreach (var d in data)
            {
                var t = new Train { Kind = (TrainKind)Mathf.Clamp(d.kind, 0, 2), Livery = d.livery, Length = LengthOf((TrainKind)Mathf.Clamp(d.kind, 0, 2)), Dir = d.dir >= 0 ? 1 : -1, SpeedFactor = d.speed > 0f ? d.speed : 1f, Running = d.running };
                if (!LayAt(t, new Vector3(d.x, 0f, d.z), new Vector3(d.fx, 0f, d.fz), null)) continue;
                Build(t);
                Trains.Add(t);
                Pose(t);
            }
        }

        /// <summary>Lays a train with its locomotive end at <paramref name="front"/>, facing <paramref name="forward"/>.</summary>
        private bool LayAt(Train t, Vector3 front, Vector3 forward, Func<TrackJoint, int, EdgeEnd> choose)
        {
            if (!Net.Nearest(front, out int edge, out float s, out float d, 0.05f)) return false;
            Net.Edges[edge].Layout.Evaluate(s, out var tangent);
            int orientation = Vector3.Dot(new Vector3(tangent.x, 0f, tangent.z), forward) >= 0f ? 1 : -1;
            return Lay(t, edge, s, orientation, 0f, choose);
        }

        /// <summary>
        /// After the network was rebuilt (pieces split, merged or removed): puts every train back where it stood, its cars
        /// following the same branches; trains whose track is gone are taken off.
        /// </summary>
        public void Relocate()
        {
            for (int i = Trains.Count - 1; i >= 0; i--)
            {
                var t = Trains[i];
                var samples = new List<Vector3>();
                foreach (var c in t.Cars) samples.Add(c.Body.localPosition);
                var front = t.Cars[0].Body.localPosition + t.Cars[0].Body.localRotation * Vector3.forward * t.Cars[0].FromFront;
                var forward = t.Cars[0].Body.localRotation * Vector3.forward;
                EdgeEnd Choose(TrackJoint j, int ji)
                {
                    EdgeEnd best = j.Branch(0); float bestD = float.MaxValue;
                    for (int k = 0; k < 2; k++)
                    {
                        var b = j.Branch(k);
                        var p = Net.EndPoint(b) + Net.IntoEdge(b) * 0.05f;
                        foreach (var q in samples)
                        {
                            float dd = new Vector2(p.x - q.x, p.z - q.z).sqrMagnitude;
                            if (dd < bestD) { bestD = dd; best = b; }
                        }
                    }
                    return best;
                }
                if (Net.Empty || !LayAt(t, front, forward, Choose)) { Remove(t); continue; }
                t.BlockedBy = -1; t.WaitTime = 0f;
                Pose(t);
            }
        }

        /// <summary>
        /// After a piece of track was bent (same pieces, new shape): keeps every train where it was along its track, its
        /// arc lengths on the bent piece scaled to the new length.
        /// </summary>
        public void Rebase(int edge, float ratio)
        {
            for (int i = Trains.Count - 1; i >= 0; i--)
            {
                var t = Trains[i];
                var front = t.Path[^1];
                float s = front.Edge == edge ? front.S1 * ratio : front.S1;
                var l = Net.Edges[front.Edge].Layout;
                if (!l.Closed) s = Mathf.Clamp(s, 0f, l.Length);
                if (!Lay(t, front.Edge, s, front.Dir, 0f, null)) { Remove(t); continue; }
                t.BlockedBy = -1; t.WaitTime = 0f;
                Pose(t);
            }
        }

        // ------------------------------------------------------------------ path operations

        /// <summary>Grows a path at its far end by up to <paramref name="d"/>; returns how far it got (dead ends stop it).</summary>
        private float ExtendFront(List<TrackSpan> path, float d, Func<TrackJoint, int, EdgeEnd> choose)
        {
            float done = 0f;
            for (int guard = 0; guard < 64 && d - done > 1e-6f; guard++)
            {
                var sp = path[^1];
                var l = Net.Edges[sp.Edge].Layout;
                float want = d - done;
                if (l.Closed) { sp.S1 += sp.Dir * want; path[^1] = Wrapped(sp, l); return d; }
                float room = sp.Dir > 0 ? l.Length - sp.S1 : sp.S1;
                if (room >= want) { sp.S1 += sp.Dir * want; path[^1] = sp; return d; }
                sp.S1 = sp.Dir > 0 ? l.Length : 0f; path[^1] = sp; done += Mathf.Max(0f, room);
                var next = Net.Next(new EdgeEnd(sp.Edge, sp.Dir > 0 ? 1 : 0), choose);
                if (next == null) break;
                var n = next.Value; float s0 = Net.EndS(n);
                path.Add(new TrackSpan { Edge = n.Edge, S0 = s0, S1 = s0, Dir = n.End == 0 ? 1 : -1 });
            }
            return done;
        }

        /// <summary>Grows a path at its near end (the tail) by up to <paramref name="d"/>.</summary>
        private float ExtendBack(List<TrackSpan> path, float d, Func<TrackJoint, int, EdgeEnd> choose)
        {
            float done = 0f;
            for (int guard = 0; guard < 64 && d - done > 1e-6f; guard++)
            {
                var sp = path[0];
                var l = Net.Edges[sp.Edge].Layout;
                float want = d - done;
                if (l.Closed) { sp.S0 -= sp.Dir * want; path[0] = Wrapped(sp, l); return d; }
                float room = sp.Dir > 0 ? sp.S0 : l.Length - sp.S0;
                if (room >= want) { sp.S0 -= sp.Dir * want; path[0] = sp; return d; }
                sp.S0 = sp.Dir > 0 ? 0f : l.Length; path[0] = sp; done += Mathf.Max(0f, room);
                var next = Net.Next(new EdgeEnd(sp.Edge, sp.Dir > 0 ? 0 : 1), choose);
                if (next == null) break;
                var n = next.Value; float s0 = Net.EndS(n);
                path.Insert(0, new TrackSpan { Edge = n.Edge, S0 = s0, S1 = s0, Dir = n.End == 0 ? -1 : 1 });
            }
            return done;
        }

        private static TrackSpan Wrapped(TrackSpan sp, TrackLayout l)
        {
            float k = Mathf.Floor(Mathf.Min(sp.S0, sp.S1) / l.Length);
            if (k != 0f) { sp.S0 -= k * l.Length; sp.S1 -= k * l.Length; }
            return sp;
        }

        private static void TrimFront(List<TrackSpan> path, float d)
        {
            while (d > 1e-7f && path.Count > 0)
            {
                var sp = path[^1];
                float len = sp.Length;
                if (len > d || path.Count == 1) { sp.S1 -= sp.Dir * Mathf.Min(d, len); path[^1] = sp; return; }
                path.RemoveAt(path.Count - 1); d -= len;
            }
        }

        private static void TrimBack(List<TrackSpan> path, float d)
        {
            while (d > 1e-7f && path.Count > 0)
            {
                var sp = path[0];
                float len = sp.Length;
                if (len > d || path.Count == 1) { sp.S0 += sp.Dir * Mathf.Min(d, len); path[0] = sp; return; }
                path.RemoveAt(0); d -= len;
            }
        }

        private static float LengthOf(List<TrackSpan> path) { float l = 0f; foreach (var sp in path) l += sp.Length; return l; }

        /// <summary>Position (and direction of increasing u) at distance <paramref name="u"/> from the path's start.</summary>
        private Vector3 PointAt(List<TrackSpan> path, float u, out Vector3 forward)
        {
            float acc = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                var sp = path[i];
                if (acc + sp.Length >= u || i == path.Count - 1)
                {
                    var l = Net.Edges[sp.Edge].Layout;
                    var p = l.Evaluate(sp.S0 + sp.Dir * Mathf.Clamp(u - acc, 0f, Mathf.Max(sp.Length, 0f)), out var t);
                    forward = t * sp.Dir;
                    return p;
                }
                acc += sp.Length;
            }
            forward = Vector3.forward;
            return Vector3.zero;
        }

        /// <summary>The track ahead of a train in the direction it moves, starting at its leading end (a fresh span).</summary>
        private static TrackSpan LeadingSpan(Train t)
        {
            if (t.Dir > 0) { var f = t.Path[^1]; return new TrackSpan { Edge = f.Edge, S0 = f.S1, S1 = f.S1, Dir = f.Dir }; }
            var b = t.Path[0];
            return new TrackSpan { Edge = b.Edge, S0 = b.S0, S1 = b.S0, Dir = -b.Dir };
        }

        // ------------------------------------------------------------------ reservations

        private struct Interval { public int Edge; public float Lo, Hi; }

        private List<Interval> Intervals(List<TrackSpan> spans, List<Interval> into = null)
        {
            into ??= new List<Interval>();
            foreach (var sp in spans)
            {
                var l = Net.Edges[sp.Edge].Layout;
                float lo = Mathf.Min(sp.S0, sp.S1), hi = Mathf.Max(sp.S0, sp.S1);
                if (!l.Closed) { into.Add(new Interval { Edge = sp.Edge, Lo = lo, Hi = hi }); continue; }
                if (hi - lo >= l.Length) { into.Add(new Interval { Edge = sp.Edge, Lo = 0f, Hi = l.Length }); continue; }
                float a = Mathf.Repeat(lo, l.Length), b = a + (hi - lo);
                if (b <= l.Length) into.Add(new Interval { Edge = sp.Edge, Lo = a, Hi = b });
                else { into.Add(new Interval { Edge = sp.Edge, Lo = a, Hi = l.Length }); into.Add(new Interval { Edge = sp.Edge, Lo = 0f, Hi = b - l.Length }); }
            }
            return into;
        }

        private List<Interval>[] held = new List<Interval>[0];

        private void RefreshHeld()
        {
            if (held.Length != Trains.Count) held = new List<Interval>[Trains.Count];
            for (int i = 0; i < Trains.Count; i++)
            {
                held[i] ??= new List<Interval>();
                held[i].Clear();
                Intervals(Trains[i].Path, held[i]);
                Intervals(Trains[i].Claim, held[i]);
            }
        }

        /// <summary>Whether an interval set touches a switch (within the fouling distance of any of its ends).</summary>
        private bool TouchesJoint(List<Interval> set, int ji)
        {
            var j = Net.Joints[ji];
            foreach (var e in j.Ends)
            {
                float s = Net.EndS(e);
                float lo = e.End == 0 ? 0f : s - TrackNetwork.FoulDistance, hi = e.End == 0 ? TrackNetwork.FoulDistance : s;
                foreach (var iv in set) if (iv.Edge == e.Edge && iv.Hi > lo && iv.Lo < hi) return true;
            }
            return false;
        }

        private bool TouchesCrossing(List<Interval> set, in TrackCrossing c)
        {
            foreach (var iv in set)
            {
                if (iv.Edge == c.EdgeA && Near(iv, c.EdgeA, c.SA)) return true;
                if (iv.Edge == c.EdgeB && Near(iv, c.EdgeB, c.SB)) return true;
            }
            return false;
        }

        private bool Near(Interval iv, int edge, float s)
        {
            var l = Net.Edges[edge].Layout;
            float lo = s - TrackNetwork.CrossingZone, hi = s + TrackNetwork.CrossingZone;
            if (iv.Hi > lo && iv.Lo < hi) return true;
            if (l.Closed && (iv.Hi > lo + l.Length && iv.Lo < hi + l.Length || iv.Hi > lo - l.Length && iv.Lo < hi - l.Length)) return true;
            return false;
        }

        /// <summary>Two interval sets conflict: overlapping track (with spacing), or sharing a switch or a crossing.</summary>
        private bool Conflicts(List<Interval> a, List<Interval> b, bool spacing)
        {
            float gap = spacing ? Spacing : 0f;
            foreach (var x in a)
                foreach (var y in b)
                    if (x.Edge == y.Edge && x.Hi + gap > y.Lo && x.Lo - gap < y.Hi) return true;
            for (int ji = 0; ji < Net.Joints.Count; ji++)
                if (TouchesJoint(a, ji) && TouchesJoint(b, ji)) return true;
            foreach (var c in Net.Crossings)
                if (TouchesCrossing(a, c) && TouchesCrossing(b, c)) return true;
            return false;
        }

        /// <summary>Whether any train stands on or has reserved a switch (it can't be thrown then).</summary>
        public bool JointLocked(int ji)
        {
            RefreshHeld();
            for (int i = 0; i < Trains.Count; i++) if (TouchesJoint(held[i], ji)) return true;
            return false;
        }

        private float RedSignalOn(List<Interval> piece, List<Interval> own)
        {
            for (int i = 0; i < track.Signals.Count; i++)
            {
                if (!track.Signals[i].red || i >= track.SignalSpots.Count) continue;
                var (edge, s) = track.SignalSpots[i];
                if (edge < 0) continue;
                bool inPiece = false, inOwn = false;
                foreach (var iv in piece) if (iv.Edge == edge && s >= iv.Lo && s <= iv.Hi) inPiece = true;
                foreach (var iv in own) if (iv.Edge == edge && s >= iv.Lo - 0.01f && s <= iv.Hi + 0.01f) inOwn = true;
                if (inPiece && !inOwn) return 1f;
            }
            return 0f;
        }

        /// <summary>Reserves more track ahead of train <paramref name="ti"/>, as far as it may (see the class summary).</summary>
        private void GrowClaim(int ti, float wanted)
        {
            var t = Trains[ti];
            t.Blocked = BlockReason.None; t.BlockedBy = -1;
            if (t.Claim.Count == 0) { t.Claim.Add(LeadingSpan(t)); t.ClaimLength = 0f; }
            var own = Intervals(t.Path);
            const float step = 0.01f;
            for (int guard = 0; guard < 80 && t.ClaimLength < wanted; guard++)
            {
                // Peek one step ahead: does it enter a switch or a crossing? Then reserve all of it at once.
                var probe = new List<TrackSpan> { t.Claim[^1] };
                var start = probe[0]; start.S0 = start.S1; probe[0] = start;
                float got = ExtendFront(probe, step, null);
                if (got < step * 0.5f) { t.Blocked = BlockReason.Buffers; break; }
                float extra = step;
                var claimSet = Intervals(t.Claim);
                claimSet.AddRange(own);
                var probeSet = Intervals(probe);
                for (int ji = 0; ji < Net.Joints.Count; ji++)
                    if (TouchesJoint(probeSet, ji) && !TouchesJoint(claimSet, ji)) { extra = Mathf.Max(extra, TrackNetwork.FoulDistance * 2f + 0.03f); PrepareSwitch(ji, t); }
                foreach (var c in Net.Crossings)
                    if (TouchesCrossing(probeSet, c) && !TouchesCrossing(claimSet, c)) extra = Mathf.Max(extra, TrackNetwork.CrossingZone * 2f + 0.03f);
                // Tentative reservation of this step (or the whole switch / crossing).
                var tentative = new List<TrackSpan> { t.Claim[^1] };
                var ts = tentative[0]; ts.S0 = ts.S1; tentative[0] = ts;
                float reach = ExtendFront(tentative, extra, null);
                if (reach < extra - 0.001f && reach < step) { t.Blocked = BlockReason.Buffers; break; }
                var tentSet = Intervals(tentative);
                if (RedSignalOn(tentSet, own) > 0f) { t.Blocked = BlockReason.Signal; break; }
                int blocker = -1;
                for (int i = 0; i < Trains.Count && blocker < 0; i++)
                    if (i != ti && Conflicts(tentSet, held[i], true)) blocker = i;
                if (blocker >= 0) { t.Blocked = BlockReason.Train; t.BlockedBy = blocker; break; }
                // Commit.
                ExtendFront(t.Claim, reach, null);
                t.ClaimLength += reach;
                MarkFacing(tentative);
                held[ti].AddRange(tentSet);
                if (reach < extra - 0.001f) { t.Blocked = BlockReason.Buffers; break; }
            }
        }

        /// <summary>A train is about to reserve a switch: in Random mode a facing switch gets a fresh random setting.</summary>
        private void PrepareSwitch(int ji, Train t)
        {
            var j = Net.Joints[ji];
            if (Mode != SwitchMode.Random || j.Pinned) return;
            var last = t.Claim[^1];
            var heading = new EdgeEnd(last.Edge, last.Dir > 0 ? 1 : 0);
            if (heading != j.Trunk) return;                                                       // trailing: nothing to choose
            for (int i = 0; i < Trains.Count; i++) if (TouchesJoint(held[i], ji)) return;        // in use: leave it
            int setting = random.Next(2);
            if (setting != j.Setting) { j.Setting = setting; SwitchesChanged = true; }
        }

        /// <summary>Notes switches a reservation runs through facing, so Explore can alternate them once they are clear.</summary>
        private void MarkFacing(List<TrackSpan> spans)
        {
            for (int k = 0; k < spans.Count - 1; k++)
            {
                var sp = spans[k];
                var exit = new EdgeEnd(sp.Edge, sp.Dir > 0 ? 1 : 0);
                int ji = Net.JointAt(exit);
                if (ji >= 0 && Net.Joints[ji].Trunk == exit) Net.Joints[ji].FlipPending = true;
            }
        }

        // ------------------------------------------------------------------ stepping

        private void Update()
        {
            if (Trains.Count == 0 || track == null) { if (ribbonMesh != null && ribbonMesh.vertexCount > 0) ribbonMesh.Clear(); return; }
            Step(Time.deltaTime);
        }

        /// <summary>Advances every train (also called by the scripted tests).</summary>
        public void Step(float dt)
        {
            if (Net.Empty) return;
            RefreshHeld();
            AlternateSwitches();
            for (int i = 0; i < Trains.Count; i++)
            {
                var t = Trains[i];
                float v = CruiseSpeed * t.SpeedFactor;
                float wanted = v * v / (2f * Acceleration) + 0.06f;
                if (t.Running) GrowClaim(i, wanted);
            }
            for (int i = 0; i < Trains.Count; i++) Drive(i, dt);
            ResolveStandoffs(dt);
            RefreshHeld();
            UpdateLamps();
            DrawRibbon();
        }

        private void Drive(int ti, float dt)
        {
            var t = Trains[ti];
            float cruise = CruiseSpeed * t.SpeedFactor;
            float target = t.Running ? cruise : 0f;
            if (t.Dwell > 0f)
            {
                t.Dwell -= dt; t.Speed = 0f;
                if (t.Dwell > 0f) { Pose(t); return; }
            }
            // Never beyond the reservation.
            float room = Mathf.Max(0f, t.ClaimLength - 0.008f);
            target = Mathf.Min(target, Mathf.Sqrt(2f * Acceleration * room));
            // Stations ahead: brake to halt with the train centred on the platform.
            if (t.Running && Stations.Count > 0)
            {
                int st = NextStation(t, out float ahead);
                if (st >= 0)
                {
                    target = Mathf.Min(target, Mathf.Max(Mathf.Sqrt(2f * Acceleration * Mathf.Max(0f, ahead - 0.001f)), 0.004f));
                    if (ahead < 0.003f)
                    {
                        Move(t, Mathf.Max(0f, ahead));
                        t.Speed = 0f; t.Dwell = DwellSeconds; t.DepartedStation = st;
                        Pose(t); return;
                    }
                }
            }
            t.Speed = Mathf.MoveTowards(t.Speed, target, Acceleration * dt);
            if (!t.Running && t.Speed <= 0f) { t.Claim.Clear(); t.ClaimLength = 0f; }   // stopped: release what lies ahead
            if (t.Speed > Mathf.Sqrt(2f * Acceleration * room) + 0.002f) t.Speed = Mathf.Sqrt(2f * Acceleration * room);
            float ds = Mathf.Min(t.Speed * dt, room);
            if (ds > 0f) Move(t, ds);
            Pose(t);
            Animate(t, ds);
        }

        /// <summary>Moves a train <paramref name="d"/> metres along its reservation.</summary>
        private void Move(Train t, float d)
        {
            if (d <= 0f) return;
            float moved;
            if (t.Dir > 0) { moved = ExtendFront(t.Path, d, null); TrimBack(t.Path, moved); }
            else { moved = ExtendBack(t.Path, d, null); TrimFront(t.Path, moved); }
            TrimBack(t.Claim, moved);
            t.ClaimLength = Mathf.Max(0f, t.ClaimLength - moved);
            if (t.Claim.Count == 0 || t.ClaimLength <= 0f) { t.Claim.Clear(); t.ClaimLength = 0f; }
            t.Odometer += moved;
        }

        public void Reverse(Train t)
        {
            t.Dir = -t.Dir;
            t.Claim.Clear(); t.ClaimLength = 0f;
            t.WaitTime = 0f; t.BlockedBy = -1;
        }

        /// <summary>
        /// Trains stuck at buffers reverse; two trains waiting for each other (head-on on single track) resolve it by one
        /// backing away; a train stuck behind another for a long time turns back too.
        /// </summary>
        private void ResolveStandoffs(float dt)
        {
            for (int i = 0; i < Trains.Count; i++)
            {
                var t = Trains[i];
                bool waiting = t.Running && t.Dwell <= 0f && t.Speed < 0.002f && t.ClaimLength < 0.02f;
                t.WaitTime = waiting && t.Blocked != BlockReason.None ? t.WaitTime + dt : 0f;
                if (!waiting) continue;
                if (t.Blocked == BlockReason.Buffers && t.WaitTime > 0.5f) { Reverse(t); continue; }
                if (t.Blocked != BlockReason.Train || t.BlockedBy < 0 || t.BlockedBy >= Trains.Count) continue;
                var other = Trains[t.BlockedBy];
                bool mutual = other.Blocked == BlockReason.Train && other.BlockedBy == i;
                bool queueing = other.Blocked == BlockReason.Signal || other.Dwell > 0f;   // behind a train at a red signal or a station: wait
                if ((mutual && i > t.BlockedBy && t.WaitTime > 1.5f) || (!queueing && t.WaitTime > 12f)) Reverse(t);
            }
        }

        /// <summary>Explore: a switch a train went through facing flips once it is clear, so the next train goes the other way.</summary>
        private void AlternateSwitches()
        {
            for (int ji = 0; ji < Net.Joints.Count; ji++)
            {
                var j = Net.Joints[ji];
                if (!j.FlipPending) continue;
                bool busy = false;
                for (int i = 0; i < Trains.Count && !busy; i++) busy = TouchesJoint(held[i], ji);
                if (busy) continue;
                j.FlipPending = false;
                if (Mode == SwitchMode.Explore && !j.Pinned) { j.Setting = 1 - j.Setting; SwitchesChanged = true; }
            }
        }

        /// <summary>The next station ahead whose platform centre the train's centre will reach, and how far that is.</summary>
        private int NextStation(Train t, out float ahead)
        {
            ahead = float.MaxValue;
            var route = new List<TrackSpan>();
            // From the train's centre to its leading end, then its reservation.
            float half = t.Length * 0.5f, acc = 0f;
            if (t.Dir > 0)
            {
                foreach (var sp in t.Path)
                {
                    float a = acc, b = acc + sp.Length; acc = b;
                    if (b <= half) continue;
                    var c = sp; if (a < half) c.S0 = sp.S0 + sp.Dir * (half - a);
                    route.Add(c);
                }
            }
            else
            {
                for (int k = t.Path.Count - 1; k >= 0; k--)
                {
                    var sp = t.Path[k];
                    float a = acc, b = acc + sp.Length; acc = b;   // measured from the locomotive end
                    if (b <= half) continue;
                    var c = new TrackSpan { Edge = sp.Edge, Dir = -sp.Dir, S0 = sp.S1, S1 = sp.S0 };
                    if (a < half) c.S0 = sp.S1 - sp.Dir * (half - a);
                    route.Add(c);
                }
            }
            route.AddRange(t.Claim);
            int found = -1; float dist = 0f;
            bool departedAhead = false;
            foreach (var sp in route)
            {
                var l = Net.Edges[sp.Edge].Layout;
                for (int si = 0; si < Stations.Count; si++)
                {
                    var st = Stations[si];
                    if (st.Edge != sp.Edge) continue;
                    float s = st.S;
                    if (l.Closed)
                    {
                        float lo = Mathf.Min(sp.S0, sp.S1);
                        s = lo + Mathf.Repeat(s - lo, l.Length);
                    }
                    float along = (s - sp.S0) * sp.Dir;
                    if (along < -0.0005f || along > sp.Length + 0.0005f) continue;
                    float d = dist + along;
                    if (si == t.DepartedStation) { if (d < 0.05f) departedAhead = true; continue; }
                    if (d < ahead) { ahead = d; found = si; }
                }
                dist += sp.Length;
                if (found >= 0) break;
            }
            if (!departedAhead) t.DepartedStation = -1;
            return found;
        }

        // ------------------------------------------------------------------ signals and the route ribbon

        private void UpdateLamps()
        {
            if (track == null) return;
            var net = Net;
            int count = 0;
            foreach (var j in net.Joints) if (j.Ends.Count == 3) count += 3;
            var green = new bool[count + track.Signals.Count];
            int k = 0;
            for (int ji = 0; ji < net.Joints.Count; ji++)
            {
                var j = net.Joints[ji];
                if (j.Ends.Count != 3) continue;
                int owner = -1;
                for (int i = 0; i < Trains.Count && owner < 0; i++) if (TouchesJoint(held[i], ji)) owner = i;
                foreach (var end in j.Ends)
                {
                    bool g = owner < 0;
                    if (owner >= 0)
                    {
                        // Green for the end the owning train comes from or goes to, red for the others.
                        float s = net.EndS(end);
                        float lo = end.End == 0 ? 0f : s - TrackNetwork.FoulDistance, hi = end.End == 0 ? TrackNetwork.FoulDistance : s;
                        foreach (var iv in held[owner]) if (iv.Edge == end.Edge && iv.Hi > lo && iv.Lo < hi) g = true;
                    }
                    green[k++] = g;
                }
            }
            for (int i = 0; i < track.Signals.Count; i++) green[k++] = !track.Signals[i].red;
            track.SetLamps(green);
            if (SwitchesChanged) { SwitchesChanged = false; track.RebuildFixtures(); }
        }

        /// <summary>A glowing ribbon in the train's colour over the track it has reserved (for the train being pointed at).</summary>
        private void DrawRibbon()
        {
            ribbonMesh.Clear();
            var t = Highlighted;
            if (t == null || !Trains.Contains(t)) return;
            var b = new MeshBuilder();
            var colour = TrainFactory.LiveryColour(t.Livery); colour.a = 0.75f;
            var spans = new List<TrackSpan>(t.Claim);
            float total = LengthOf(spans);
            int n = Mathf.Max(2, Mathf.CeilToInt(total / 0.01f));
            for (int i = 0; i <= n && total > 0.005f; i++)
            {
                var p = PointAt(spans, total * i / n, out var f); f.y = 0f;
                var side = Vector3.Cross(Vector3.up, f.sqrMagnitude > 1e-8f ? f.normalized : Vector3.forward) * 0.016f;
                var c = colour; c.a *= 1f - 0.7f * i / n;
                b.AddVertex(p - side + Vector3.up * 0.0075f, Vector3.up, c);
                b.AddVertex(p + side + Vector3.up * 0.0075f, Vector3.up, c);
                if (i > 0) { int v = b.VertexCount - 4; b.AddTriangle(v, v + 2, v + 3); b.AddTriangle(v, v + 3, v + 1); }
            }
            b.ToMesh("Route ribbon", ribbonMesh);
            ribbonMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ visuals

        private void Build(Train t)
        {
            var root = new GameObject(t.Kind + " train");
            root.transform.SetParent(transform, false);
            t.Root = root.transform;
            var consist = Consist(t.Kind);
            t.Cars = new Train.Car[consist.Length];
            float fromFront = 0f;
            for (int i = 0; i < consist.Length; i++)
            {
                var layout = consist[i];
                if (i > 0) fromFront += Gap;
                fromFront += layout.Front;
                var body = Part(CarMesh(t.Kind, i, t.Livery), t.Root);
                var car = new Train.Car { Body = body, Layout = layout, FromFront = fromFront, Axles = new Transform[layout.Axles.Length], Radius = layout.WheelRadius };
                for (int a = 0; a < layout.Axles.Length; a++)
                {
                    car.Axles[a] = Part(TrainFactory.AxleMesh(layout.WheelRadius[a], t.Livery), body);
                    car.Axles[a].localPosition = layout.Axles[a];
                }
                if (layout == TrainFactory.Loco)
                {
                    car.Rods = Part(TrainFactory.RodMesh, body);
                    car.Rods.localPosition = new Vector3(0f, layout.Axles[0].y + layout.WheelRadius[0] * 0.45f, 0f);
                    t.Smoke = CreateSmoke(body);
                }
                t.Cars[i] = car;
                fromFront -= layout.Rear;
            }
        }

        private Transform Part(Mesh mesh, Transform parent)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = lit; r.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        private void Pose(Train t)
        {
            foreach (var c in t.Cars)
            {
                float u = t.Length - c.FromFront;
                var front = PointAt(t.Path, u + c.Layout.PivotHalf, out _);
                var back = PointAt(t.Path, u - c.Layout.PivotHalf, out _);
                var forward = front - back;
                if (forward.sqrMagnitude < 1e-8f) continue;
                c.Body.localPosition = (front + back) * 0.5f + Vector3.up * TrackSystem.RailTop;
                c.Body.localRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        private void Animate(Train t, float ds)
        {
            float signed = ds * t.Dir;
            foreach (var c in t.Cars)
            {
                c.Distance += signed;
                for (int i = 0; i < c.Axles.Length; i++)
                    c.Axles[i].localRotation = Quaternion.Euler(c.Distance / c.Radius[i] * Mathf.Rad2Deg, 0f, 0f);
                if (c.Rods)
                {
                    // The crank pin sits at +Y on each wheel; rotating by theta about X moves it to (cos, sin).
                    float angle = c.Distance / c.Radius[0], crank = c.Radius[0] * 0.45f;
                    c.Rods.localPosition = new Vector3(0f, c.Layout.Axles[0].y + Mathf.Cos(angle) * crank, Mathf.Sin(angle) * crank);
                }
            }
            if (t.Smoke)
            {
                var emission = t.Smoke.emission;
                t.Chuff += ds / (TrainFactory.Loco.WheelRadius[0] * Mathf.PI * 0.5f);
                float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t.Chuff * Mathf.PI));
                emission.rateOverTime = InTunnel(t) ? 0f : 3f + 30f * (t.Speed / CruiseSpeed) * pulse;
            }
        }

        private ParticleSystem CreateSmoke(Transform loco)
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(loco, false);
            go.transform.localPosition = TrainFactory.Loco.Chimney;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f; main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.035f, 0.065f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.gravityModifier = -0.004f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 10f; shape.radius = 0.002f;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.25f, 1.2f), new Keyframe(1f, 2.6f)));
            var colour = ps.colorOverLifetime; colour.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.92f, 0.92f, 0.95f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.08f), new GradientAlphaKey(0.55f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colour.color = g;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.012f; noise.frequency = 1.5f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = smokeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }
    }
}
