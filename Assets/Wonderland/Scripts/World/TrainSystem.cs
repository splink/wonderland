using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    public enum TrainKind { Passenger = 0, Goods = 1, Railcar = 2, Express = 3, Tank = 4 }

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
        public int Reversals { get; internal set; }           // how often it has turned back (at buffers, a station, or backing away)
        internal float HeldBack;                              // extra seconds held at a station to keep its distance
        public float Length { get; internal set; }
        public float Odometer { get; internal set; }
        public float Dwell { get; internal set; }
        public readonly List<TrackSpan> Path = new();         // the track it stands on, from its tail to its locomotive end
        public readonly List<TrackSpan> Claim = new();        // the track reserved ahead of it, in the direction it moves
        public float ClaimLength { get; internal set; }
        public int BlockedBy { get; internal set; } = -1;     // index of the train it is waiting for (-1 none)
        public BlockReason Blocked { get; internal set; }
        public float WaitTime { get; internal set; }
        public int Cargo;                                     // goods trains: crates of produce aboard
        public int Carriages = -1;                                 // carriages or wagons behind the engine (-1: the kind's usual number)
        public int LoadedAt = -1;                             // the station they were loaded at (unloaded elsewhere)
        public int HandledStop = -1;                          // the halt whose goods were already handled
        internal int DepartedStation = -1;
        internal readonly List<int> Calls = new();            // StopsAhead, cached while it stands still
        internal float CallsOdometer = -1f;
        internal int CallsKey;
        /// <summary>The station it is standing at (an index into the stations), or -1 while it is not halted at one.</summary>
        public int AtStation => Dwell > 0f ? DepartedStation : -1;
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

        /// <summary>How many carriages (wagons; railcar units) a train of this kind may have, and has unless changed.</summary>
        public static (int min, int usual, int max) CarRange(TrainKind kind) => kind switch
        {
            TrainKind.Goods => (0, 4, 8), TrainKind.Railcar => (1, 1, 3), TrainKind.Express => (2, 4, 7), TrainKind.Tank => (0, 2, 3), _ => (0, 2, 6),
        };

        public static int CarsOf(Train t) => t.Carriages >= 0 ? t.Carriages : CarRange(t.Kind).usual;

        private static CarLayout[] Consist(TrainKind kind, int cars = -1)
        {
            var (min, usual, max) = CarRange(kind);
            int n = Mathf.Clamp(cars < 0 ? usual : cars, min, max);
            if (kind == TrainKind.Railcar) { var r = new CarLayout[n]; for (int i = 0; i < n; i++) r[i] = TrainFactory.Railcar; return r; }
            var c = new CarLayout[n + 1];
            c[0] = kind == TrainKind.Express ? TrainFactory.Diesel : kind == TrainKind.Tank ? TrainFactory.Tank : TrainFactory.Loco;
            for (int i = 1; i <= n; i++) c[i] = kind == TrainKind.Goods ? TrainFactory.Wagon : TrainFactory.Coach;
            return c;
        }

        private static Mesh CarMesh(TrainKind kind, int index, int livery) => kind switch
        {
            TrainKind.Goods => index == 0 ? TrainFactory.LocoMesh(livery) : TrainFactory.WagonMesh((index - 1) % TrainFactory.WagonVariants),
            TrainKind.Railcar => TrainFactory.RailcarMesh(livery),
            TrainKind.Express => index == 0 ? TrainFactory.DieselMesh(livery) : TrainFactory.CoachMesh(index - 1, livery),
            TrainKind.Tank => index == 0 ? TrainFactory.TankMesh(livery) : TrainFactory.CoachMesh(index - 1, livery),
            _ => index == 0 ? TrainFactory.LocoMesh(livery) : TrainFactory.CoachMesh(index - 1, livery),
        };

        public static float LengthOf(TrainKind kind, int cars = -1)
        {
            float l = 0f;
            var c = Consist(kind, cars);
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
            var t = new Train { Kind = kind, Livery = livery, Length = LengthOf(kind), SpeedFactor = kind == TrainKind.Express ? 1.3f : kind == TrainKind.Tank ? 0.8f : 1f };
            if (Net.Directed(edge)) orientation = 1;   // one way: every train faces the way the railway runs
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

        /// <summary>
        /// Couples on or uncouples carriages (wagons, railcar units): the engine stays where it is and the train grows or
        /// shrinks behind it. Returns null, or why not (the most or fewest it can have; no room on the track behind).
        /// </summary>
        public string SetCars(Train t, int cars)
        {
            var (min, _, max) = CarRange(t.Kind);
            int now = CarsOf(t);
            if (cars > max) return $"That's as long as this train gets ({max} {CarWord(t.Kind, max)})";
            if (cars < min) return t.Kind == TrainKind.Railcar ? "A railcar runs on its own at the least" : "Just the engine now";
            if (cars == now) return null;
            var front = PointAt(t.Path, t.Length, out var forward);   // the engine's end (the path runs tail to engine)
            var old = (t.Carriages, t.Length, new List<TrackSpan>(t.Path));
            t.Carriages = cars; t.Length = LengthOf(t.Kind, cars);
            bool laid = LayAt(t, front, forward, null);
            var mine = laid ? Intervals(t.Path) : null;
            bool clash = false;
            if (laid) { RefreshHeld(); int me = Trains.IndexOf(t); for (int i = 0; i < Trains.Count && !clash; i++) if (i != me && Conflicts(mine, held[i], true)) clash = true; }
            if (!laid || clash)
            {
                (t.Carriages, t.Length) = (old.Item1, old.Item2); t.Path.Clear(); t.Path.AddRange(old.Item3);
                return clash ? "Another train is in the way" : "No room on the track behind for another";
            }
            t.Claim.Clear(); t.ClaimLength = 0f;
            Release(t.Root.gameObject);
            Build(t);
            Pose(t);
            return null;
        }

        public static string CarWord(TrainKind kind, int n) => kind switch
        {
            TrainKind.Goods => n == 1 ? "wagon" : "wagons", TrainKind.Railcar => n == 1 ? "car" : "cars", _ => n == 1 ? "carriage" : "carriages",
        };

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
        /// <summary>The drawn bodies of a train's cars (tests).</summary>
        public IEnumerable<Transform> CarBodies(Train t) { foreach (var c in t.Cars) yield return c.Body; }

        /// <summary>A train's state in a line (tests and diagnostics).</summary>
        public string Debug(Train t) =>
            $"{t.Kind} dir {t.Dir} speed {t.Speed:0.000} dwell {t.Dwell:0.0} running {t.Running} blocked {t.Blocked}/{t.BlockedBy} wait {t.WaitTime:0.0} path {t.Path.Count} spans {LengthOf(t.Path):0.000}/{t.Length:0.000} m claim {t.ClaimLength:0.00}; spans {string.Join(" ", t.Path.ConvertAll(sp => $"[e{sp.Edge}{(Net.Edges[sp.Edge].Layout.Closed ? " loop" : "")} len {Net.Edges[sp.Edge].Layout.Length:0.000} {sp.S0:0.000}->{sp.S1:0.000}]"))}";

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

        /// <summary>How far apart two trains' bodies are (between their nearest cars, side by side or end to end; 0: touching).</summary>
        public float Between(Train a, Train b) => Between(a, b, out _, out _);

        /// <summary>...and whether that is side by side (not one behind the other on the same line), and where.</summary>
        public float Between(Train a, Train b, out bool sideBySide, out Vector3 where)
        {
            float best = float.MaxValue; sideBySide = false; where = Vector3.zero;
            if (a.Cars == null || b.Cars == null) return best;
            foreach (var ca in a.Cars)
            foreach (var cb in b.Cars)
            {
                if (ca?.Body == null || cb?.Body == null) continue;
                var fa = ca.Body.localRotation * Vector3.forward; var fb = cb.Body.localRotation * Vector3.forward;
                for (int i = 0; i <= 4; i++)
                for (int k = 0; k <= 4; k++)
                {
                    var pa = ca.Body.localPosition + fa * Mathf.Lerp(ca.Layout.Rear, ca.Layout.Front, i / 4f);
                    var pb = cb.Body.localPosition + fb * Mathf.Lerp(cb.Layout.Rear, cb.Layout.Front, k / 4f);
                    float d = (pa - pb).magnitude;
                    if (d < best)
                    {
                        best = d; where = (pa + pb) * 0.5f;
                        var off = pb - pa; off.y = 0f;
                        sideBySide = off.sqrMagnitude > 1e-8f && Mathf.Abs(Vector3.Dot(off.normalized, fa)) < 0.7f;   // (across, not along)
                    }
                }
            }
            return Mathf.Max(0f, best - 0.026f);   // (a car body is about 2.6 cm wide)
        }

        /// <summary>The train the pointer ray is on (local space, unit direction), nearer than <paramref name="maxT"/>; null if none.</summary>
        public Train PickRay(Vector3 o, Vector3 d, float maxT, out float t)
        {
            Train best = null; float bestScore = 1f; t = float.MaxValue;
            foreach (var train in Trains)
            {
                if (train.Cars == null) continue;
                foreach (var car in train.Cars)
                {
                    if (car?.Body == null || !car.Body.gameObject.activeInHierarchy) continue;
                    var fwd = car.Body.localRotation * Vector3.forward;
                    var mid = car.Body.localPosition + car.Body.localRotation * Vector3.up * 0.02f;
                    float score = RayPick.Score(o, d, mid + fwd * car.Layout.Rear * 0.8f, mid + fwd * car.Layout.Front * 0.8f, maxT, out float ti);
                    if (score < bestScore) { bestScore = score; best = train; t = ti; }
                }
            }
            return best;
        }

        /// <summary>
        /// A passenger's view: just outside a window of the first carriage behind the engine (the engine's cab for a
        /// railcar or a lone engine), looking out sideways and a little ahead - the countryside going by.
        /// </summary>
        public void WindowView(Train t, out Vector3 eye, out Quaternion look)
        {
            var car = t.Cars.Length > 1 ? t.Cars[1] : t.Cars[0];
            var rot = car.Body.localRotation;
            float half = car.Body.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh != null ? f.sharedMesh.bounds.extents.x : 0.013f;
            eye = car.Body.localPosition + rot * new Vector3(half + 0.0025f, 0.024f, 0f);
            look = rot * Quaternion.Euler(6f, 62f, 0f);
        }
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
            public int cargo;
            public int cars = -1;   // carriages (-1: the usual; older saves)
        }

        public List<TrainData> ToData()
        {
            var list = new List<TrainData>();
            foreach (var t in Trains)
            {
                var front = PointAt(t.Path, t.Length, out var tangent);
                list.Add(new TrainData { kind = (int)t.Kind, livery = t.Livery, dir = t.Dir, x = front.x, z = front.z, fx = tangent.x, fz = tangent.z, speed = t.SpeedFactor, running = t.Running, cargo = t.Cargo, cars = t.Carriages });
            }
            return list;
        }

        public void FromData(IEnumerable<TrainData> data)
        {
            RemoveAll();
            if (data == null || Net.Empty) return;
            foreach (var d in data)
            {
                var t = new Train { Kind = (TrainKind)Mathf.Clamp(d.kind, 0, 4), Livery = d.livery, Length = LengthOf((TrainKind)Mathf.Clamp(d.kind, 0, 4)), Dir = d.dir >= 0 ? 1 : -1, SpeedFactor = d.speed > 0f ? d.speed : 1f, Running = d.running, Cargo = Mathf.Max(0, d.cargo), Carriages = d.cars };
                t.Length = LengthOf(t.Kind, t.Carriages);
                if (!LayAt(t, new Vector3(d.x, 0f, d.z), new Vector3(d.fx, 0f, d.fz), null)) continue;
                Build(t);
                Trains.Add(t);
                AlignToFlow(t);
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
                AlignToFlow(t);
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
                float foul = Net.FoulOf(ji);
                float lo = e.End == 0 ? 0f : s - foul, hi = e.End == 0 ? foul : s;
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
                    if (TouchesJoint(probeSet, ji) && !TouchesJoint(claimSet, ji)) { extra = Mathf.Max(extra, Net.FoulOf(ji) * 2f + 0.03f); PrepareSwitch(ji, t); }
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
            var last = t.Claim[^1];
            var heading = new EdgeEnd(last.Edge, last.Dir > 0 ? 1 : 0);
            if (heading != j.Trunk) return;                                                       // trailing: nothing to choose
            if (Mode == SwitchMode.Manual || j.Pinned) { Note(ji, j.Setting, "kept: pinned or Manual"); return; }
            for (int i = 0; i < Trains.Count; i++) if (TouchesJoint(held[i], ji)) { Note(ji, j.Setting, "kept: still in use by a train"); return; }   // in use: leave it
            int setting = Mode == SwitchMode.Random ? random.Next(2) : (Net.Directed(j.Trunk.Edge) ? Spread(ji) : j.Setting);
            Note(ji, setting, "chosen");
            if (setting != j.Setting) { j.Setting = setting; SwitchesChanged = true; }
        }

        /// <summary>Diagnostics: every facing switch a train came to - which branch it got and why.</summary>
        public readonly Dictionary<(int joint, int branch, string why), int> SwitchLog = new();
        private void Note(int ji, int branch, string why) { SwitchLog.TryGetValue((ji, branch, why), out int n); SwitchLog[(ji, branch, why)] = n + 1; }

        // ------------------------------------------------------------------ spreading out

        private readonly Dictionary<long, float> branchUsed = new();
        private float clock;

        /// <summary>
        /// Explore on a one-way railway: which way a train coming to a splitting switch goes. Not into a dead-end spur
        /// when the other way carries on; then the way with fewer trains on the next stretch of line; on a tie the
        /// way taken longer ago - so trains run every part of the railway and spread out rather than bunch up.
        /// </summary>
        private int Spread(int ji)
        {
            var j = Net.Joints[ji];
            float best = float.MaxValue; int choice = j.Setting;
            for (int k = 0; k < 2; k++)
            {
                var b = j.Branch(k);
                float score = 0f;
                if (!Net.Through(b.Edge)) score += 100f;                        // a spur: only when there's no way through
                // Into another circuit it can't come back from: only while that one has fewer trains for its length.
                int here = Net.CircuitOf(j.Trunk.Edge), there = Net.CircuitOf(b.Edge);
                if (here != there && Density(there) + 1f / Mathf.Max(0.5f, CircuitLength(there)) > Density(here)) score += 50f;
                score += TrainsAhead(b) * 2f;
                branchUsed.TryGetValue(((long)ji << 1) | (uint)k, out float used);
                score += Mathf.Clamp01(1f - (clock - used) / 60f) * 1.5f;       // taken lately: less keen
                score += (float)random.NextDouble() * 0.01f;
                if (score < best) { best = score; choice = k; }
            }
            branchUsed[((long)ji << 1) | (uint)choice] = clock;
            return choice;
        }

        /// <summary>Trains on (or reserving) the track within about two metres beyond a switch's branch, nearer ones counting more.</summary>
        private float TrainsAhead(EdgeEnd branch)
        {
            var near = new Dictionary<int, float>();   // edge -> how far along the way it starts
            var queue = new Queue<(int edge, float at)>();
            queue.Enqueue((branch.Edge, 0f)); near[branch.Edge] = 0f;
            while (queue.Count > 0)
            {
                var (e, at) = queue.Dequeue();
                float end = at + Net.Edges[e].Layout.Length;
                if (end > 2f || Net.Edges[e].Closed) continue;
                int ji = Net.JointAt(new EdgeEnd(e, 1));
                if (ji < 0) continue;
                foreach (var x in Net.Joints[ji].Ends)
                    if (!TrackNetwork.Arrives(x) && !near.ContainsKey(x.Edge)) { near[x.Edge] = end; queue.Enqueue((x.Edge, end)); }
            }
            float count = 0f;
            foreach (var t in Trains)
            {
                float closest = float.MaxValue;
                foreach (var sp in t.Path) if (near.TryGetValue(sp.Edge, out float d)) closest = Mathf.Min(closest, d);
                foreach (var sp in t.Claim) if (near.TryGetValue(sp.Edge, out float d)) closest = Mathf.Min(closest, d);
                if (closest < float.MaxValue) count += 1f / (1f + closest);
            }
            return count;
        }

        /// <summary>Trains per metre of track in a circuit.</summary>
        private float Density(int c)
        {
            int n = 0;
            foreach (var t in Trains) if (t.Path.Count > 0 && Net.CircuitOf(t.Path[^1].Edge) == c) n++;
            return n / Mathf.Max(0.5f, CircuitLength(c));
        }

        private float CircuitLength(int c)
        {
            float l = 0f;
            for (int e = 0; e < Net.Edges.Count; e++) if (Net.CircuitOf(e) == c) l += Net.Edges[e].Layout.Length;
            return l;
        }

        /// <summary>How far trains would be apart if spread evenly over the railway.</summary>
        private float EvenSpacing() => Trains.Count > 0 ? Net.TotalLength / Trains.Count : float.MaxValue;

        /// <summary>Straight-line distance to the nearest other train in front of this one's engine (roughly how close the next train is).</summary>
        private float GapAhead(Train t)
        {
            var at = LocoPosition(t); var fwd = LocoRotation(t) * Vector3.forward;
            float best = float.MaxValue;
            foreach (var o in Trains)
            {
                if (o == t) continue;
                var d = CentrePosition(o) - at;
                if (Vector3.Dot(d, fwd) > 0f) best = Mathf.Min(best, d.magnitude);
            }
            return best;
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
            long t0 = SimCost.Now; Step(Time.deltaTime); SimCost.Add(SimCost.Trains, t0);
        }

        /// <summary>Advances every train (also called by the scripted tests).</summary>
        public void Step(float dt)
        {
            if (Net.Empty) return;
            clock += dt;
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
                // (Trains never turn back or run round: they go the way the arrows point, always.)
                // Keeping their distance: a train ahead that is close? Wait a little longer, so trains spread round the railway.
                if (OneWayHere(t) && t.HeldBack < 8f && Trains.Count > 1 && GapAhead(t) < EvenSpacing() * 0.45f) { t.Dwell = 1f; t.HeldBack += 1f; Pose(t); return; }
                t.HeldBack = 0f;
            }
            // Never beyond the reservation - and when it ends at the train ahead, a couple of centimetres short of it.
            float room = Mathf.Max(0f, t.ClaimLength - (t.Blocked == BlockReason.Train ? 0.025f : 0.008f));
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

        /// <summary>No other train within <paramref name="within"/> of this one's engine (so it can turn back safely).</summary>
        private bool Alone(Train t, float within)
        {
            var at = LocoPosition(t);
            foreach (var o in Trains) if (o != t && (LocoPosition(o) - at).magnitude < within + 0.1f * (CarsOf(o) + CarsOf(t))) return false;
            return true;
        }

        /// <summary>Is the train on a part of the railway that runs one way (every piece under it)?</summary>
        private bool OneWayHere(Train t)
        {
            if (t.Path.Count == 0) return false;
            foreach (var sp in t.Path) if (!Net.Directed(sp.Edge)) return false;
            return true;
        }

        /// <summary>Is the train running the way the railway runs (one way), engine first?</summary>
        private bool WithFlow(Train t)
        {
            if (t.Path.Count == 0) return true;
            int lead = t.Dir > 0 ? t.Path[^1].Dir : -t.Path[0].Dir;
            return lead > 0 && t.Dir > 0;
        }

        /// <summary>One way: a train facing against the railway's direction runs round and sets off the right way.</summary>
        public void AlignToFlow(Train t)
        {
            if (t.Path.Count == 0 || !OneWayHere(t)) return;
            int lead = t.Dir > 0 ? t.Path[^1].Dir : -t.Path[0].Dir;
            if (lead < 0) { Reverse(t); t.Reversals--; }                      // the right way round, but moving backwards
            if (t.Dir < 0) TurnRound(t);                                        // engine at the back: run round
            lead = t.Path[^1].Dir;
            if (lead < 0) { Reverse(t); TurnRound(t); t.Reversals--; }          // still against the flow: set off the other way
        }

        /// <summary>
        /// The engine runs round its train: it moves to the other end, so a train that was pushing (running tender or
        /// tail first) now has its engine leading, the same way along the same track. Railcars have a cab at each end
        /// and just change ends.
        /// </summary>
        public void TurnRound(Train t)
        {
            t.Path.Reverse();
            for (int i = 0; i < t.Path.Count; i++) { var sp = t.Path[i]; t.Path[i] = new TrackSpan { Edge = sp.Edge, Dir = -sp.Dir, S0 = sp.S1, S1 = sp.S0 }; }
            t.Dir = -t.Dir;
            t.Claim.Clear(); t.ClaimLength = 0f;
            t.WaitTime = 0f; t.BlockedBy = -1;
            t.CallsOdometer = -1f;
            Pose(t);
        }

        public void Reverse(Train t)
        {
            t.Reversals++;
            t.Dir = -t.Dir;
            t.Claim.Clear(); t.ClaimLength = 0f;
            t.WaitTime = 0f; t.BlockedBy = -1;
        }

        /// <summary>
        /// How long trains have stood waiting (for the train ahead, a red signal, the buffers). They never back away or
        /// turn round: the railway runs one way, so whoever is ahead moves on in time - and a train at the buffers of a
        /// line that ends stays there (the Track tool says such a line isn't used).
        /// </summary>
        private void ResolveStandoffs(float dt)
        {
            for (int i = 0; i < Trains.Count; i++)
            {
                var t = Trains[i];
                bool waiting = t.Running && t.Dwell <= 0f && t.Speed < 0.002f && t.ClaimLength < 0.02f;
                t.WaitTime = waiting && t.Blocked != BlockReason.None ? t.WaitTime + dt : 0f;
            }
        }

        /// <summary>Explore: a switch a train went through facing flips once it is clear, so the next train goes the other way.</summary>
        private void AlternateSwitches()
        {
            for (int ji = 0; ji < Net.Joints.Count; ji++)
            {
                var j = Net.Joints[ji];
                if (!j.FlipPending) continue;
                if (Net.Directed(j.Trunk.Edge)) { j.FlipPending = false; continue; }   // one way: chosen as each train comes (see PrepareSwitch)
                bool busy = false;
                for (int i = 0; i < Trains.Count && !busy; i++) busy = TouchesJoint(held[i], ji);
                if (busy) continue;
                j.FlipPending = false;
                if (Mode == SwitchMode.Explore && !j.Pinned) { j.Setting = 1 - j.Setting; SwitchesChanged = true; }
            }
        }

        /// <summary>
        /// The stations a train will call at next, in order (not the one it stands at), following the switches as they
        /// are set now and turning back at buffers as it would: what a passenger on the platform reads off the
        /// destination board. In Explore mode a switch flips once each train has passed, so this is what the next train
        /// through will do; a later train may of course be routed differently.
        /// </summary>
        public void StopsAhead(Train t, List<int> into, int max = 4)
        {
            StopsAheadAll(t, into, max);
            if (into.Count > max) into.RemoveRange(max, into.Count - max);
        }

        private void StopsAheadAll(Train t, List<int> into, int max)
        {
            into.Clear();
            if (Stations.Count == 0 || Net.Empty || t.Path.Count == 0) return;
            float total = 0f;
            foreach (var e in Net.Edges) total += e.Layout.Length;
            float range = Mathf.Min(total * 2.2f + 0.5f, 40f), dist = 0f;
            // From its centre (where it halts) to its leading end, then on along the line.
            var probe = AheadOfCentre(t);
            if (probe.Count == 0) probe.Add(LeadingSpan(t));
            int skip = t.AtStation, last = -1, turns = 0, legStart = 0;
            bool first = true;
            var at = new List<float>();   // how far along the line each call is
            for (int guard = 0; guard < 400 && dist < range && into.Count <= max; guard++)
            {
                float got = 0f;
                if (first) { first = false; foreach (var p0 in probe) got += p0.Length; got = Mathf.Max(got, 0.001f); }
                else { var sp = probe[^1]; sp.S0 = sp.S1; probe.Clear(); probe.Add(sp); got = ExtendFront(probe, 0.25f, null); }
                if (got < 0.0005f)
                {
                    // Buffers: a platform the train's centre can't reach before them is no stop. Then it reverses.
                    while (into.Count > legStart && at[^1] > dist - t.Length * 0.5f + 0.01f) { into.RemoveAt(into.Count - 1); at.RemoveAt(at.Count - 1); }
                    last = into.Count > 0 ? into[^1] : -1; skip = last >= 0 ? last : t.AtStation;
                    legStart = into.Count;
                    if (++turns > 2) break;
                    var end = probe[^1];
                    probe.Clear(); probe.Add(new TrackSpan { Edge = end.Edge, S0 = end.S1, S1 = end.S1, Dir = -end.Dir });
                    continue;
                }
                float segStart = dist;
                foreach (var seg in probe)
                {
                    var l = Net.Edges[seg.Edge].Layout;
                    int found = -1; float best = float.MaxValue;
                    for (int si = 0; si < Stations.Count; si++)
                    {
                        var st = Stations[si];
                        if (st.Edge != seg.Edge || si == skip) continue;
                        float s = st.S;
                        if (l.Closed) { float lo = Mathf.Min(seg.S0, seg.S1); s = lo + Mathf.Repeat(s - lo, l.Length); }
                        float along = (s - seg.S0) * seg.Dir;
                        if (along < 0f || along > seg.Length || along >= best) continue;
                        best = along; found = si;
                    }
                    if (found >= 0 && found != last) { into.Add(found); at.Add(segStart + best); last = found; skip = found; }
                    segStart += seg.Length;
                }
                dist += got;
            }
        }

        /// <summary>Whether a train is bound for a station within its next few (up to four) calls.</summary>
        public bool CallsAt(Train t, int station, int within = 3)
        {
            // Asked every frame by everyone on the platform: worked out again only once the train or a switch moved.
            int key = t.Dir * 7919 + Stations.Count * 31;
            foreach (var j in Net.Joints) key = key * 3 + j.Setting;
            if (t.CallsOdometer != t.Odometer || t.CallsKey != key) { StopsAhead(t, t.Calls, 4); t.CallsOdometer = t.Odometer; t.CallsKey = key; }
            for (int k = 0; k < t.Calls.Count && k < within; k++) if (t.Calls[k] == station) return true;
            return false;
        }

        /// <summary>The next station ahead whose platform centre the train's centre will reach, and how far that is.</summary>
        /// <summary>The track from a train's centre to its leading end, in the direction it moves.</summary>
        private List<TrackSpan> AheadOfCentre(Train t)
        {
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
            return route;
        }

        private int NextStation(Train t, out float ahead)
        {
            ahead = float.MaxValue;
            var route = AheadOfCentre(t);
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

        private bool[] lamps = Array.Empty<bool>();

        /// <summary>A train holds track within 40 cm of signal <paramref name="i"/> (its lamp shows red on automatic).</summary>
        public bool SignalOccupied(int i)
        {
            if (i >= track.SignalSpots.Count) return false;
            var (edge, s) = track.SignalSpots[i];
            if (edge < 0) return false;
            var l = Net.Edges[edge].Layout;
            for (int t = 0; t < Trains.Count && t < held.Length; t++)
                foreach (var iv in held[t])
                {
                    if (iv.Edge != edge) continue;
                    float lo = s - 0.4f, hi = s + 0.4f;
                    if (iv.Hi > lo && iv.Lo < hi) return true;
                    if (l.Closed && (iv.Hi > lo + l.Length && iv.Lo < hi + l.Length || iv.Hi > lo - l.Length && iv.Lo < hi - l.Length)) return true;
                }
            return false;
        }

        /// <summary>Whether signal <paramref name="i"/> shows green now.</summary>
        public bool SignalGreen(int i) => i < track.Signals.Count && !track.Signals[i].red && !SignalOccupied(i);

        // ------------------------------------------------------------------ names and descriptions

        private readonly List<string> stationNames = new();
        private int namedStations = -1;

        /// <summary>The station (an index into the stations) of the station building standing at <paramref name="at"/>, or -1.</summary>
        public int StationAt(Vector2 at)
        {
            for (int i = 0; i < Stations.Count; i++) if ((Stations[i].House - at).sqrMagnitude < 1e-6f) return i;
            return -1;
        }

        /// <summary>The village a station serves ("Brookside"): fixed by where it stands, and unique in the world.</summary>
        public string StationName(int i)
        {
            if (i < 0 || i >= Stations.Count) return "the station";
            int key = Stations.Count;
            foreach (var st in Stations) key = key * 31 + Mathf.RoundToInt(st.House.x * 997f) + Mathf.RoundToInt(st.House.y * 991f);
            if (key != namedStations)
            {
                namedStations = key; stationNames.Clear();
                foreach (var st in Stations)
                {
                    string name = PlaceNames.Village(st.House);
                    for (int k = 1; stationNames.Contains(name); k++) name = PlaceNames.Village(st.House + new Vector2(k * 0.37f, k * 0.71f));
                    stationNames.Add(name);
                }
            }
            return stationNames[i];
        }

        /// <summary>One line on a train for the palette: what it is, what it is doing, and why if it isn't moving.</summary>
        public string Describe(Train t)
        {
            int cars = CarsOf(t);
            string what = t.Kind switch
            {
                TrainKind.Goods => $"Goods train, {cars} {CarWord(t.Kind, cars)}" + (t.Cargo > 0 ? $" ({t.Cargo} crates)" : ""),
                TrainKind.Railcar => cars == 1 ? "Railcar" : $"Railcar, {cars} cars",
                TrainKind.Express => $"Express, {cars} {CarWord(t.Kind, cars)}",
                TrainKind.Tank => cars == 0 ? "Tank engine" : $"Tank engine, {cars} {CarWord(t.Kind, cars)}",
                _ => $"Steam train, {cars} {CarWord(t.Kind, cars)}",
            };
            string doing;
            if (t.AtStation >= 0) doing = $"at {StationName(t.AtStation)} station";
            else if (!t.Running) doing = t.Speed > 0.002f ? "stopping" : "stopped (trigger: go)";
            else if (t.Speed < 0.004f && t.Blocked == BlockReason.Signal) doing = "waiting at a red signal (tap it for automatic)";
            else if (t.Speed < 0.004f && t.Blocked == BlockReason.Train) doing = "waiting for the train ahead to clear the line";
            else if (t.Blocked == BlockReason.Buffers) doing = "at the end of the line (it goes no further: trains never reverse)";
            else doing = $"running at {t.SpeedFactor * 100f:0} %";
            if (t.AtStation < 0 && Stations.Count > 0 && t.Running)
            {
                StopsAhead(t, scratchStops, 1);
                if (scratchStops.Count > 0) doing += $", next stop {StationName(scratchStops[0])}";
            }
            return $"{what} - {doing}";
        }

        private readonly List<int> scratchStops = new();

        private void UpdateLamps()
        {
            if (track == null) return;
            var net = Net;
            int count = 0;
            foreach (var j in net.Joints) if (j.Ends.Count == 3) count += 3;
            if (lamps.Length != count + track.Signals.Count) lamps = new bool[count + track.Signals.Count];
            var green = lamps;
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
                        float foul = net.FoulOf(ji);
                        float lo = end.End == 0 ? 0f : s - foul, hi = end.End == 0 ? foul : s;
                        foreach (var iv in held[owner]) if (iv.Edge == end.Edge && iv.Hi > lo && iv.Lo < hi) g = true;
                    }
                    green[k++] = g;
                }
            }
            // Signals: held at red by the player, or automatic - red while a train is on the line within 40 cm.
            for (int i = 0; i < track.Signals.Count; i++) green[k++] = !track.Signals[i].red && !SignalOccupied(i);
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
            var consist = Consist(t.Kind, t.Carriages);
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
                    t.Smoke = CreateSmoke(body, layout.Chimney);
                }
                else if (layout == TrainFactory.Tank) t.Smoke = CreateSmoke(body, layout.Chimney);
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

        private ParticleSystem CreateSmoke(Transform loco, Vector3 chimney)
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(loco, false);
            go.transform.localPosition = chimney;
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
