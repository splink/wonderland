using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Road traffic on the roads and lanes the player draws. A small graph is derived from them (a node every 3 cm, and
    /// junctions wherever two ways meet; footpaths are for bicycles only), and vehicles follow routes over it, keeping to
    /// the right. The households own them: homes near a road get a car, a motorbike or a bicycle (fixed by where the
    /// house stands), which by day drives to the shops, the restaurant, the park or work, parks at the roadside a while,
    /// and comes home; at night everything is parked. Vehicles slow behind one another, stop for people on the road and
    /// wait at level crossings while a train is near. Hospitals, police and fire stations by a road keep an ambulance,
    /// a patrol car or a fire engine, which answer the village's incidents (see TrafficSystem.Incidents.cs).
    /// Nothing here is saved: the vehicles are derived from homes and roads, and go home when the world changes.
    /// </summary>
    public sealed partial class TrafficSystem : MonoBehaviour
    {
        public const int MaxPrivate = 14;
        public const float NodeSpacing = 0.03f, Junction = 0.026f, Reach = 0.13f;

        public enum Mode { Parked, Driving, AtScene }

        public sealed class Vehicle
        {
            public VehicleKind Kind;
            public Transform Root;
            public Transform[] Beacons;
            public Vector2 Position, Home, HomeDir;
            public float Heading, Speed, Timer, Waiting, Height, Pitch, Pushing;
            public Mode State;
            public readonly List<Vector2> Path = new();
            public int Next;
            public bool Siren, ToHome;
            public int Base = -1;              // emergency vehicles: their station (building index)
            public Vector2 BasePos;
            public Rng Rng;
            public string Owner;
        }

        private sealed class Node { public Vector2 P; public bool Car; public readonly List<int> Next = new(); }

        public readonly List<Vehicle> Vehicles = new();
        private readonly List<Node> graph = new();
        private WonderlandWorld world;
        private Material material;
        private int roadsKey = -1, buildingsVersion = -1;
        private float checkTimer, blink;

        public int Nodes => graph.Count;
        public int Trips { get; private set; }            // journeys completed (tests)
        public int Stops { get; private set; }            // times something stopped them (a car ahead, people, a crossing)
        public int CrossingWaits { get; private set; }

        public void Init(WonderlandWorld owner, Material lit) { world = owner; material = lit; }

        private void Update()
        {
            if (world == null || world.Terrain == null) return;
            long t0 = SimCost.Now; Step(Time.deltaTime); SimCost.Add(SimCost.Traffic, t0);
        }

        /// <summary>Advances the traffic (also driven by the scripted tests).</summary>
        public void Step(float dt)
        {
            checkTimer -= dt;
            if (checkTimer <= 0f) { checkTimer = 1f; Refresh(); }
            blink += dt;
            foreach (var v in Vehicles) Drive(v, dt);
            Return(dt);
            StepIncidents(dt);
        }

        /// <summary>Roads or buildings changed: rebuild the graph and the vehicles (cheap; at most once a second).</summary>
        public void Refresh(bool force = false)
        {
            int key = RoadsKey();
            if (!force && key == roadsKey && world.Buildings.Version == buildingsVersion) return;
            bool roadsChanged = key != roadsKey;
            roadsKey = key; buildingsVersion = world.Buildings.Version;
            if (roadsChanged || force) BuildGraph();
            Populate();
        }

        private int RoadsKey()
        {
            int k = 17;
            foreach (var l in world.Paths.Lines)
            {
                if (l.kind > (int)PathKind.Footpath || l.points.Count == 0) continue;
                k = k * 31 + l.kind * 7 + l.points.Count;
                k = k * 31 + Mathf.RoundToInt(l.points[0].x * 1000f) + Mathf.RoundToInt(l.points[^1].y * 1000f) * 3;
            }
            return k;
        }

        // ------------------------------------------------------------------ the road graph

        private void BuildGraph()
        {
            graph.Clear();
            foreach (var l in world.Paths.Lines)
            {
                if (l.kind > (int)PathKind.Footpath || l.points.Count < 2) continue;
                bool car = l.kind != (int)PathKind.Footpath;
                int first = graph.Count, prev = -1, step = Mathf.Max(1, Mathf.RoundToInt(NodeSpacing / PathSystem.Spacing));
                for (int i = 0; i < l.points.Count; i += step)
                {
                    int id = Add(l.points[i], car);
                    if (prev >= 0) Link(prev, id);
                    prev = id;
                }
                if ((l.points.Count - 1) % step != 0 && !l.closed) { int id = Add(l.points[^1], car); Link(prev, id); }
                if (l.closed && graph.Count - first > 2) Link(prev, first);
            }
            // Junctions: nodes of different ways close together are joined.
            var cells = new Dictionary<long, List<int>>();
            long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            for (int i = 0; i < graph.Count; i++)
            {
                var k = Key(Mathf.FloorToInt(graph[i].P.x / Junction), Mathf.FloorToInt(graph[i].P.y / Junction));
                if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<int>();
                list.Add(i);
            }
            for (int i = 0; i < graph.Count; i++)
            {
                int cx = Mathf.FloorToInt(graph[i].P.x / Junction), cz = Mathf.FloorToInt(graph[i].P.y / Junction);
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!cells.TryGetValue(Key(cx + dx, cz + dz), out var list)) continue;
                    foreach (int j in list)
                        if (j > i && !graph[i].Next.Contains(j) && (graph[i].P - graph[j].P).magnitude < Junction) Link(i, j);
                }
            }
        }

        private int Add(Vector2 p, bool car) { graph.Add(new Node { P = p, Car = car }); return graph.Count - 1; }
        private void Link(int a, int b) { if (a == b) return; graph[a].Next.Add(b); graph[b].Next.Add(a); }

        /// <summary>The road node nearest a point (within <paramref name="within"/>) a vehicle of this kind may use, or -1.</summary>
        public int NearestNode(Vector2 p, float within, bool car)
        {
            int best = -1; float bestD = within * within;
            for (int i = 0; i < graph.Count; i++)
            {
                if (car && !graph[i].Car) continue;
                float d = (graph[i].P - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>A route between two nodes (A*); bicycles may use footpaths, motor vehicles only roads and lanes.</summary>
        public List<Vector2> Route(int a, int z, bool car)
        {
            if (a < 0 || z < 0) return null;
            if (a == z) return new List<Vector2> { graph[a].P };
            var cost = new Dictionary<int, float> { [a] = 0f };
            var came = new Dictionary<int, int>();
            var open = new List<int> { a };
            var goal = graph[z].P;
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                    if (cost[open[i]] + (graph[open[i]].P - goal).magnitude < cost[open[best]] + (graph[open[best]].P - goal).magnitude) best = i;
                int cur = open[best]; open.RemoveAt(best);
                if (cur == z) break;
                foreach (int nx in graph[cur].Next)
                {
                    if (car && !graph[nx].Car) continue;
                    float c = cost[cur] + (graph[nx].P - graph[cur].P).magnitude;
                    if (cost.TryGetValue(nx, out float old) && old <= c) continue;
                    cost[nx] = c; came[nx] = cur;
                    if (!open.Contains(nx)) open.Add(nx);
                }
            }
            if (!cost.ContainsKey(z)) return null;
            var route = new List<Vector2>();
            for (int cur = z; cur != a; cur = came[cur]) route.Add(graph[cur].P);
            route.Add(graph[a].P);
            route.Reverse();
            return route;
        }

        public bool Connected(Vector2 from, Vector2 to, bool car)
        {
            var r = Route(NearestNode(from, Reach, car), NearestNode(to, Reach, car), car);
            return r != null;
        }

        // ------------------------------------------------------------------ who owns what

        /// <summary>Homes near a road get a vehicle (half a car, the rest a bicycle or a motorbike, by where the house stands); stations their own.</summary>
        private void Populate()
        {
            var wanted = new List<(VehicleKind kind, Vector2 home, int colour, int station, string owner)>();
            var houses = world.Buildings.Houses;
            int privateCount = 0;
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                var bp = Catalog.Get(h.variant);
                var door = PeopleSystem.DoorStep(h);
                if (bp.Base)
                {
                    var kind = bp.Id == Catalog.Hospital ? VehicleKind.Ambulance : VehicleKind.Police;
                    if (NearestNode(door, 0.16f, true) >= 0) wanted.Add((kind, door, 0, i, bp.Name));
                    continue;
                }
                if (bp.Id == Catalog.FireStation) { if (NearestNode(door, 0.16f, true) >= 0) wanted.Add((VehicleKind.FireEngine, door, 0, i, bp.Name)); continue; }
                if (!bp.Dwelling || privateCount >= MaxPrivate) continue;
                uint hash = (uint)Mathf.RoundToInt(h.x * 7919f) * 2654435761u ^ (uint)Mathf.RoundToInt(h.z * 6271f) * 40503u;
                hash ^= hash >> 13;
                int roll = (int)(hash % 10);
                var k = roll < 5 ? VehicleKind.Car : roll < 6 ? VehicleKind.Motorbike : roll < 8 ? VehicleKind.Bicycle : (VehicleKind)(-1);
                if ((int)k < 0) continue;
                if (NearestNode(door, Reach, k != VehicleKind.Bicycle) < 0) continue;
                wanted.Add((k, door, (int)(hash >> 8) % 4, -1, bp.Name));
                privateCount++;
            }
            // Keep the vehicles that are still wanted (they go home); add the new ones; take away the rest.
            var keep = new List<Vehicle>();
            foreach (var w in wanted)
            {
                var v = Vehicles.Find(x => x.Kind == w.kind && (x.Home - w.home).sqrMagnitude < 1e-6f && !keep.Contains(x));
                if (v == null) v = Spawn(w.kind, w.home, w.colour);
                v.Base = w.station; v.Owner = w.owner;
                if (w.station >= 0) v.BasePos = new Vector2(houses[w.station].x, houses[w.station].z);
                keep.Add(v);
            }
            foreach (var v in Vehicles) if (!keep.Contains(v)) Destroy(v);
            Vehicles.Clear(); Vehicles.AddRange(keep);
            foreach (var v in Vehicles) if (v.State != Mode.Parked) { if (!Reroute(v)) GoHome(v, true); }
            AbandonOrphans();
        }

        private Vehicle Spawn(VehicleKind kind, Vector2 home, int colour)
        {
            var go = new GameObject(kind.ToString());
            go.layer = WonderlandWorld.MiniatureLayer;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = VehicleFactory.Get(kind, colour);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On;
            var at = VehicleFactory.Beacons(kind);
            var beacons = new Transform[at.Length];
            for (int i = 0; i < at.Length; i++)
            {
                var b = new GameObject("Beacon");
                b.layer = WonderlandWorld.MiniatureLayer;
                b.transform.SetParent(go.transform, false);
                b.transform.localPosition = at[i];
                b.AddComponent<MeshFilter>().sharedMesh = VehicleFactory.Beacon();
                var br = b.AddComponent<MeshRenderer>();
                br.sharedMaterial = material; br.shadowCastingMode = ShadowCastingMode.Off;
                b.SetActive(false);
                beacons[i] = b.transform;
            }
            var v = new Vehicle { Kind = kind, Root = go.transform, Beacons = beacons, Home = home, Rng = new Rng(Mathf.RoundToInt(home.x * 9973f + home.y * 7717f)) };
            v.Timer = v.Rng.Range(2f, 30f);
            GoHome(v, true);
            return v;
        }

        private void Destroy(Vehicle v) { if (v.Root) { if (Application.isPlaying) Object.Destroy(v.Root.gameObject); else Object.DestroyImmediate(v.Root.gameObject); } }

        private void OnDestroy() { foreach (var v in Vehicles) Destroy(v); }

        private static bool Car(Vehicle v) => v.Kind != VehicleKind.Bicycle;

        private float Cruise(Vehicle v) => v.Kind switch
        {
            VehicleKind.Bicycle => 0.034f, VehicleKind.Motorbike => 0.08f, VehicleKind.Car => 0.065f, _ => v.Siren ? 0.095f : 0.06f,
        };

        private static float Lane(Vehicle v) => v.Kind == VehicleKind.Bicycle || v.Kind == VehicleKind.Motorbike ? 0.014f : 0.0105f;

        // ------------------------------------------------------------------ journeys

        /// <summary>Puts a vehicle back at its parking place (beside the road nearest its home), parked.</summary>
        private void GoHome(Vehicle v, bool now)
        {
            int n = NearestNode(v.Home, Reach + 0.04f, Car(v));
            if (n < 0) { v.State = Mode.Parked; Place(v, v.Home, 0f); return; }
            if (!now) { DriveTo(v, graph[n].P, true); return; }
            var p = graph[n].P;
            var dir = Direction(n);
            var side = new Vector2(dir.y, -dir.x);
            if (Vector2.Dot(v.Home - p, side) < 0f) side = -side;   // the kerb on the home's side
            v.State = Mode.Parked; v.Path.Clear(); v.Siren = false; v.Speed = 0f; v.ToHome = false;
            Place(v, p + side * 0.022f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg);
        }

        private Vector2 Direction(int n)
        {
            var node = graph[n];
            if (node.Next.Count == 0) return Vector2.up;
            var d = graph[node.Next[0]].P - node.P;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
        }

        private void Place(Vehicle v, Vector2 at, float heading)
        {
            v.Position = at; v.Heading = heading;
            Pose(v, 0f);
        }

        /// <summary>Sets off for a point on the road network (false if there is no way there).</summary>
        public bool DriveTo(Vehicle v, Vector2 target, bool home)
        {
            int a = NearestNode(v.Position, Reach + 0.04f, Car(v)), z = NearestNode(target, Reach + 0.04f, Car(v));
            var route = Route(a, z, Car(v));
            if (route == null || route.Count < 2) return false;
            v.Path.Clear();
            float lane = Lane(v);
            for (int i = 0; i < route.Count; i++)
            {
                var d = (route[Mathf.Min(i + 1, route.Count - 1)] - route[Mathf.Max(i - 1, 0)]);
                d = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
                v.Path.Add(route[i] + new Vector2(d.y, -d.x) * lane);   // keep right
            }
            v.Next = 0; v.State = Mode.Driving; v.ToHome = home; v.Waiting = 0f;
            v.Timer = 20f + route.Count * NodeSpacing / Cruise(v) * 3f;   // give up if it takes far too long
            return true;
        }

        private bool Reroute(Vehicle v)
        {
            if (v.Path.Count == 0) return false;
            var end = v.Path[^1];
            return DriveTo(v, end, v.ToHome);
        }

        /// <summary>A private vehicle's day: by day, now and then, a trip to a place a little way off and back.</summary>
        private void ConsiderTrip(Vehicle v, float dt)
        {
            v.Timer -= dt;
            if (v.Timer > 0f) return;
            float hour = world.Clock.Hour;
            v.Timer = v.Rng.Range(15f, 45f);
            if (hour < 7f || hour > 21f) return;
            float wish = Mathf.Max(Needs.Appeal(Need.Errands, hour), Needs.Appeal(Need.Food, hour) * 0.8f, Needs.Appeal(Need.Work, hour) * 0.7f, Needs.Appeal(Need.Fun, hour) * 0.6f);
            if (!v.Rng.Chance(0.25f + wish * 0.5f)) return;
            // A destination: a place by the road, not too near (a bicycle goes less far).
            var houses = world.Buildings.Houses;
            float max = v.Kind == VehicleKind.Bicycle ? 0.9f : 2f;
            for (int k = 0; k < 8; k++)
            {
                var h = houses[v.Rng.Range(0, houses.Count)];
                var bp = Catalog.Get(h.variant);
                if (bp.Small || bp.Serves == Need.None && bp.Job == null || bp.Dwelling && bp.Category == PlaceCategory.Homes) continue;
                if (!bp.OpenAt(hour)) continue;
                var door = PeopleSystem.DoorStep(h);
                float d = (door - v.Home).magnitude;
                if (d < 0.25f || d > max) continue;
                if (DriveTo(v, door, false)) return;
            }
        }

        // ------------------------------------------------------------------ driving

        private void Drive(Vehicle v, float dt)
        {
            Blink(v);
            switch (v.State)
            {
                case Mode.Parked:
                    if (v.Base < 0 && !visiting.Contains(v)) ConsiderTrip(v, dt);
                    return;
                case Mode.AtScene:
                    return;
            }
            v.Timer -= dt;
            if (v.Timer <= 0f) { GoHome(v, true); return; }   // hopelessly stuck: back home (off-stage)
            while (v.Next < v.Path.Count && (v.Path[v.Next] - v.Position).magnitude < 0.012f) v.Next++;
            if (v.Next >= v.Path.Count || Responding(v) && (Active.At - v.Position).magnitude < 0.06f) { Arrive(v); return; }   // (help pulls up just short of the scene)
            var target = v.Path[v.Next];
            var to = target - v.Position;
            float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            v.Heading = Mathf.MoveTowardsAngle(v.Heading, want, (v.Kind == VehicleKind.Bicycle ? 260f : 200f) * dt);
            float speed = Cruise(v) * Mathf.Lerp(1f, 0.35f, Mathf.Abs(Mathf.DeltaAngle(v.Heading, want)) / 90f);   // slow into bends
            if (v.Next >= v.Path.Count - 3) speed *= 0.6f;
            float limit = Obstacle(v);
            if (limit < speed) { if (v.Speed > 0.01f && limit <= 0.001f && v.Waiting == 0f) Stops++; speed = limit; }
            v.Waiting = speed < 0.002f ? v.Waiting + dt : 0f;
            v.Pushing -= dt;
            if (v.Waiting > 4f) { v.Pushing = 1.5f; v.Waiting = 0f; }   // waited long enough: edge on for a moment
            v.Speed = Mathf.MoveTowards(v.Speed, speed, 0.12f * dt);
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            v.Position += fwd * v.Speed * dt;
            Pose(v, dt);
        }

        /// <summary>
        /// How fast it may go: slower behind another vehicle going the same way, stopped for people on the road ahead and
        /// at a level crossing while a train is near. After a long wait it edges on (no gridlock).
        /// </summary>
        private float Obstacle(Vehicle v)
        {
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            float half = VehicleFactory.Length(v.Kind) * 0.5f, limit = float.MaxValue;
            bool patient = v.Pushing <= 0f;
            if (patient)
                foreach (var o in Vehicles)
                {
                    if (o == v || o.State != Mode.Driving && o.State != Mode.AtScene) continue;
                    var d = o.Position - v.Position;
                    float ahead = Vector2.Dot(d, fwd), side = Mathf.Abs(d.x * fwd.y - d.y * fwd.x);
                    if (ahead <= 0f || side > 0.013f || ahead > 0.09f) continue;
                    if (v.Siren && !o.Siren && ahead > half + 0.02f) continue;   // others make way for a siren
                    float gap = ahead - half - VehicleFactory.Length(o.Kind) * 0.5f;
                    limit = Mathf.Min(limit, gap < 0.012f ? 0f : Mathf.Max(o.Speed, (gap - 0.012f) * 1.5f));
                }
            // People on the road just ahead.
            var look = v.Position + fwd * (half + 0.018f);
            if (patient && world.People.NearestPerson(look, 0.016f).HasValue) limit = 0f;
            // A level crossing ahead with a train near it.
            var cross = v.Position + fwd * (half + 0.04f);
            if (world.Paths.NearLevelCrossing(cross, 0.05f) && !world.Paths.NearLevelCrossing(v.Position, 0.03f) && TrainNear(cross, 0.3f)) { limit = 0f; CrossingWaits++; }
            return limit;
        }

        private bool TrainNear(Vector2 p, float within)
        {
            foreach (var t in world.Trains.Trains)
            {
                if (!t.Running && t.Speed < 0.01f) continue;
                var c = world.Trains.CentrePosition(t);
                if (new Vector2(c.x - p.x, c.z - p.y).magnitude < within + t.Length * 0.5f) return true;
            }
            return false;
        }

        private void Arrive(Vehicle v)
        {
            v.Speed = 0f;
            if (v.Base >= 0 && Responding(v)) { OnScene(v); return; }
            if (v.ToHome) { Trips++; GoHome(v, true); v.Timer = v.Rng.Range(20f, 60f); return; }
            // At the destination: park at the kerb a while, then home.
            v.State = Mode.Parked;
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            Place(v, v.Position + new Vector2(fwd.y, -fwd.x) * 0.01f, v.Heading);
            v.Timer = v.Rng.Range(15f, 35f);
            v.ToHome = true;
            visiting.Add(v);
        }

        private readonly HashSet<Vehicle> visiting = new();

        /// <summary>Parked away from home: when the visit is over, drive back.</summary>
        public void Return(float dt)
        {
            if (visiting.Count == 0) return;
            List<Vehicle> done = null;
            foreach (var v in visiting)
            {
                if (v.State != Mode.Parked) { (done ??= new List<Vehicle>()).Add(v); continue; }
                v.Timer -= dt;
                if (v.Timer > 0f) continue;
                if (!DriveTo(v, v.Home, true)) GoHome(v, true);
                (done ??= new List<Vehicle>()).Add(v);
            }
            if (done != null) foreach (var v in done) visiting.Remove(v);
        }

        private void Pose(Vehicle v, float dt)
        {
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            float half = VehicleFactory.Length(v.Kind) * 0.4f;
            float front = world.People.GroundHeight(v.Position + fwd * half), back = world.People.GroundHeight(v.Position - fwd * half);
            float y = (front + back) * 0.5f;
            v.Pitch = Mathf.Atan2(back - front, half * 2f) * Mathf.Rad2Deg;
            float lean = v.Kind is VehicleKind.Bicycle or VehicleKind.Motorbike ? Mathf.Clamp(Mathf.DeltaAngle(v.Heading, v.Path.Count > v.Next ? Mathf.Atan2(v.Path[v.Next].x - v.Position.x, v.Path[v.Next].y - v.Position.y) * Mathf.Rad2Deg : v.Heading), -20f, 20f) * -0.6f : 0f;
            v.Root.localPosition = new Vector3(v.Position.x, y, v.Position.y);
            v.Root.localRotation = Quaternion.Euler(v.Pitch, v.Heading, lean);
            v.Height = y;
        }

        private void Blink(Vehicle v)
        {
            if (v.Beacons.Length == 0) return;
            bool on = v.Siren || v.State == Mode.AtScene;
            int phase = Mathf.FloorToInt(blink * 5f) % 2;
            for (int i = 0; i < v.Beacons.Length; i++) v.Beacons[i].gameObject.SetActive(on && i % 2 == phase);
        }

        // ------------------------------------------------------------------ questions

        public int Count(VehicleKind k) { int n = 0; foreach (var v in Vehicles) if (v.Kind == k) n++; return n; }
        public int Moving { get { int n = 0; foreach (var v in Vehicles) if (v.State == Mode.Driving) n++; return n; } }

        /// <summary>Distance from a point to the nearest road or lane centre line (for tests: vehicles stay on the roads).</summary>
        public float OffRoad(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var l in world.Paths.Lines)
                if (l.kind <= (int)PathKind.Footpath) best = Mathf.Min(best, PathSystem.Distance(l, p));
            return best;
        }

        /// <summary>One line on a vehicle for the palette.</summary>
        public string Describe(Vehicle v)
        {
            string what = v.Kind switch
            {
                VehicleKind.Car => "A car", VehicleKind.Motorbike => "A motorbike", VehicleKind.Bicycle => "A bicycle",
                VehicleKind.Ambulance => "The ambulance", VehicleKind.Police => "The police car", _ => "The fire engine",
            };
            string doing = v.State switch
            {
                Mode.Parked => v.Base >= 0 ? "waiting at the station" : v.ToHome ? "parked while its driver is out" : "parked at home",
                Mode.AtScene => "at the scene",
                _ => v.Siren ? "on a call, lights flashing" : v.ToHome ? "heading home" : "out on an errand",
            };
            return $"{what} - {doing}";
        }

        /// <summary>The vehicle nearest a point within <paramref name="within"/>, or null.</summary>
        public Vehicle Nearest(Vector2 p, float within)
        {
            Vehicle best = null; float bestD = within;
            foreach (var v in Vehicles) { float d = (v.Position - p).magnitude; if (d < bestD) { bestD = d; best = v; } }
            return best;
        }
    }
}
