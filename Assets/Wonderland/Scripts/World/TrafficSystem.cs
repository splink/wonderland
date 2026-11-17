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
            public bool Roams;                 // put on the road by the player: drives about the town for good
            public bool AtLight;               // held by a traffic light (that wait doesn't make it edge on)
            public int Queue;                  // how many vehicles ahead of it lead to one held at a light (0: none)
            public readonly int Id = ++made;   // (who goes first when two are in each other's way)
            private static int made;
            public int Colour;
            public readonly List<bool> Fast = new();   // per path point: on a highway (faster)
        }

        [System.Serializable]
        public struct PlacedVehicle { public int kind, colour; public float x, z, heading; }

        private sealed class Node { public Vector2 P; public bool Car; public int Kind; public readonly List<int> Next = new(); }

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
            DrawLights(dt);
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
                if (!(PathSystem.IsCarWay(l.kind) || l.kind == (int)PathKind.Footpath) || l.points.Count == 0) continue;
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
                if (!(PathSystem.IsCarWay(l.kind) || l.kind == (int)PathKind.Footpath) || l.points.Count < 2) continue;
                bool car = l.kind != (int)PathKind.Footpath;
                int first = graph.Count, prev = -1, step = Mathf.Max(1, Mathf.RoundToInt(NodeSpacing / PathSystem.Spacing));
                for (int i = 0; i < l.points.Count; i += step)
                {
                    int id = Add(l.points[i], car, l.kind);
                    if (prev >= 0) Link(prev, id);
                    prev = id;
                }
                if ((l.points.Count - 1) % step != 0 && !l.closed) { int id = Add(l.points[^1], car, l.kind); Link(prev, id); }
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

        private int Add(Vector2 p, bool car, int kind) { graph.Add(new Node { P = p, Car = car, Kind = kind }); return graph.Count - 1; }
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
            var nodes = RouteNodes(a, z, car);
            return nodes?.ConvertAll(i => graph[i].P);
        }

        private List<int> RouteNodes(int a, int z, bool car)
        {
            if (a < 0 || z < 0) return null;
            if (a == z) return new List<int> { a };
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
            var route = new List<int>();
            for (int cur = z; cur != a; cur = came[cur]) route.Add(cur);
            route.Add(a);
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
            foreach (var v in Vehicles) if (!keep.Contains(v) && !v.Roams) Destroy(v);
            foreach (var v in Vehicles) if (v.Roams) keep.Add(v);   // the player's own vehicles stay
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
            var nodes = RouteNodes(a, z, Car(v));
            if (nodes == null || nodes.Count < 2) return false;
            Follow(v, nodes, home);
            return true;
        }

        /// <summary>Sets a vehicle off along graph nodes, keeping to its lane.</summary>
        private void Follow(Vehicle v, List<int> nodes, bool home)
        {
            v.Path.Clear(); v.Fast.Clear();
            bool slow = VehicleFactory.Heavy(v.Kind);
            for (int i = 0; i < nodes.Count; i++)
            {
                var d = (graph[nodes[Mathf.Min(i + 1, nodes.Count - 1)]].P - graph[nodes[Mathf.Max(i - 1, 0)]].P);
                if (d.sqrMagnitude < 1e-8f && i > 0) d = graph[nodes[i]].P - graph[nodes[i - 1]].P;   // (turning round at a dead end)
                d = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
                int kind = graph[nodes[i]].Kind;
                // Keep right, in the lane for this road (a highway's slow lane for buses and lorries).
                float lane = v.Kind == VehicleKind.Bicycle ? Lane(v) : PathSystem.LaneOffset(kind, slow);
                v.Path.Add(graph[nodes[i]].P + new Vector2(d.y, -d.x) * lane);
                v.Fast.Add(kind == (int)PathKind.Highway);
            }
            var route = v.Path;
            v.Next = 0; v.State = Mode.Driving; v.ToHome = home; v.Waiting = 0f;
            v.Timer = 20f + route.Count * NodeSpacing / Cruise(v) * 3f;   // give up if it takes far too long
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
                    if (v.Roams) { v.Timer -= dt; if (v.Timer <= 0f) Roam(v); return; }
                    if (v.Base < 0 && !visiting.Contains(v)) ConsiderTrip(v, dt);
                    return;
                case Mode.AtScene:
                    return;
            }
            v.Timer -= dt;
            if (v.Timer <= 0f) { if (v.Roams) Roam(v); else GoHome(v, true); return; }   // hopelessly stuck: back home (off-stage), or somewhere else
            while (v.Next < v.Path.Count && (v.Path[v.Next] - v.Position).magnitude < 0.012f) v.Next++;
            if (v.Next >= v.Path.Count || Responding(v) && (Active.At - v.Position).magnitude < 0.06f) { Arrive(v); return; }   // (help pulls up just short of the scene)
            var target = v.Path[v.Next];
            var to = target - v.Position;
            float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            v.Heading = Mathf.MoveTowardsAngle(v.Heading, want, (v.Kind == VehicleKind.Bicycle ? 260f : 200f) * dt);
            float speed = Cruise(v) * Mathf.Lerp(1f, 0.35f, Mathf.Abs(Mathf.DeltaAngle(v.Heading, want)) / 90f);   // slow into bends
            if (v.Next >= v.Path.Count - 3 && !v.Roams) speed *= 0.6f;
            if (v.Next < v.Fast.Count && v.Fast[v.Next]) speed *= 1.5f;   // a highway
            float limit = Obstacle(v);
            if (limit < speed) { if (v.Speed > 0.01f && limit <= 0.001f && v.Waiting == 0f) Stops++; speed = limit; }
            v.Waiting = speed < 0.002f && !v.AtLight && v.Queue == 0 ? v.Waiting + dt : 0f;
            v.Pushing -= dt;
            if (v.Waiting > 4f) { v.Pushing = 1.5f; v.Waiting = 0f; }   // waited long enough: edge on for a moment
            v.Speed = Mathf.MoveTowards(v.Speed, speed, 0.12f * dt);
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            var before = v.Position;
            v.Position += fwd * v.Speed * dt;
            WatchStopLines(v, before);
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
            int queue = 0;
            bool crossing = world.Paths.NearJunction(v.Position, 0.02f);   // (in a junction paths cross at angles: look wider)
            if (patient)
                foreach (var o in Vehicles)
                {
                    if (o == v || o.State != Mode.Driving && o.State != Mode.AtScene && !o.Roams) continue;
                    float gap = GapTo(v, o, crossing);
                    if (gap == float.MaxValue) continue;
                    if (v.Siren && !o.Siren && gap > 0.02f) continue;   // others make way for a siren
                    if (v.Id < o.Id && GapTo(o, v, crossing) < float.MaxValue) continue;   // each in the other's way (crossing paths): one goes first
                    float allowed = gap < 0.012f ? 0f : Mathf.Max(o.Speed * Mathf.Max(0f, Mathf.Cos((o.Heading - v.Heading) * Mathf.Deg2Rad)), (gap - 0.012f) * 1.5f);
                    if (allowed < limit && (o.AtLight || o.Queue > 0)) queue = o.AtLight ? 1 : o.Queue + 1;
                    limit = Mathf.Min(limit, allowed);
                }
            // People on the road just ahead.
            var look = v.Position + fwd * (half + 0.018f);
            if (patient && world.People.NearestPerson(look, 0.016f).HasValue) limit = 0f;
            // A level crossing ahead with a train near it.
            var cross = v.Position + fwd * (half + 0.04f);
            if (world.Paths.NearLevelCrossing(cross, 0.05f) && !world.Paths.NearLevelCrossing(v.Position, 0.03f) && TrainNear(cross, 0.3f)) { limit = 0f; CrossingWaits++; }
            // Traffic lights ahead (always obeyed: no edging on through a red).
            float light = LightAhead(v, fwd, half);
            v.AtLight = light < 0.002f;
            v.Queue = queue > 10 ? 0 : queue;   // (a ring of vehicles queuing behind each other is no queue: let it sort itself out)
            limit = Mathf.Min(limit, light);
            return limit;
        }

        /// <summary>
        /// The gap from <paramref name="v"/>'s nose to <paramref name="o"/>'s body when that is in its way just ahead
        /// (anywhere from tail to nose: a long bus turning across the lane is still in the way), else float.MaxValue.
        /// </summary>
        private static float GapTo(Vehicle v, Vehicle o, bool crossing)
        {
            float wide = crossing ? 0.019f : 0.013f;
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            var of = new Vector2(Mathf.Sin(o.Heading * Mathf.Deg2Rad), Mathf.Cos(o.Heading * Mathf.Deg2Rad));
            float half = VehicleFactory.Length(v.Kind) * 0.5f, halfO = VehicleFactory.Length(o.Kind) * 0.5f, gap = float.MaxValue;
            for (int k = -2; k <= 2; k++)
            {
                var d = o.Position + of * (halfO * k / 2f) - v.Position;
                float ahead = Vector2.Dot(d, fwd), side = Mathf.Abs(d.x * fwd.y - d.y * fwd.x);
                if (ahead <= 0f || side > wide || ahead > 0.09f + halfO) continue;
                gap = Mathf.Min(gap, ahead - half);
            }
            return gap;
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
            if (v.Roams) { Trips++; Roam(v); return; }
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
                if (PathSystem.IsCarWay(l.kind) || l.kind == (int)PathKind.Footpath) best = Mathf.Min(best, PathSystem.Distance(l, p));
            return best;
        }

        /// <summary>One line on a vehicle for the palette.</summary>
        public string Describe(Vehicle v)
        {
            string what = v.Kind switch
            {
                VehicleKind.Car => "A car", VehicleKind.Motorbike => "A motorbike", VehicleKind.Bicycle => "A bicycle",
                VehicleKind.Ambulance => "The ambulance", VehicleKind.Police => "The police car", VehicleKind.FireEngine => "The fire engine",
                VehicleKind.Taxi => "A taxi", VehicleKind.Van => "A delivery van", VehicleKind.Bus => "The town bus", VehicleKind.Truck => "A lorry", _ => "A tanker",
            };
            string doing = v.State switch
            {
                Mode.Parked => v.Base >= 0 ? "waiting at the station" : v.ToHome ? "parked while its driver is out" : "parked at home",
                Mode.AtScene => "at the scene",
                _ => v.Siren ? "on a call, lights flashing" : v.Roams ? (v.Speed < 0.003f ? "waiting at the lights" : "driving about town") : v.ToHome ? "heading home" : "out on an errand",
            };
            return $"{what} - {doing}";
        }

        // ------------------------------------------------------------------ the player's own vehicles

        /// <summary>
        /// Drives on about the town: on along the road it is facing, a random way onward at every junction (never straight
        /// back the way it came, unless the road ends), for a couple of metres; then on again from there.
        /// </summary>
        private void Roam(Vehicle v)
        {
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            int cur = NearestNode(v.Position + fwd * 0.015f, 0.06f, true);
            if (cur < 0) { v.State = Mode.Parked; v.Speed = 0f; v.Timer = 2f; return; }   // off the roads (they were erased): wait
            var nodes = new List<int> { cur };
            var seen = new HashSet<int> { cur };
            var dir = fwd;
            int prev = -1;
            for (int step = 0; step < 70; step++)
            {
                var options = new List<int>();
                var node = graph[cur];
                // Onward: not back the way it came, not a sharp turn about.
                foreach (int nx in node.Next)
                {
                    if (!graph[nx].Car || nx == prev || seen.Contains(nx)) continue;
                    var d = graph[nx].P - node.P;
                    if (d.sqrMagnitude < 1e-8f || Vector2.Dot(d.normalized, dir) > -0.3f) options.Add(nx);
                }
                if (options.Count == 0)
                {
                    // The road ends: turn round.
                    if (prev >= 0) options.Add(prev);
                    else foreach (int nx in node.Next) if (graph[nx].Car) options.Add(nx);
                    if (options.Count == 0) break;
                    seen.Clear();
                }
                int next = options[v.Rng.Range(0, options.Count)];
                var along = graph[next].P - node.P;
                if (along.sqrMagnitude > 0.0001f) dir = along.normalized;   // (short hops between a junction's nodes keep the old direction)
                prev = cur; cur = next;
                nodes.Add(cur); seen.Add(cur);
            }
            if (nodes.Count < 3) { v.State = Mode.Parked; v.Speed = 0f; v.Timer = 2f; return; }
            Follow(v, nodes, false);
        }

        /// <summary>Puts a vehicle on the road at the point nearest <paramref name="at"/>, facing along it; null with the reason if it can't.</summary>
        public Vehicle PlaceOnRoad(VehicleKind kind, Vector2 at, float facing, int colour, out string error, bool restoring = false)
        {
            error = null;
            if (graph.Count == 0) Refresh(true);
            int n = NearestNode(at, 0.05f, true);
            if (n < 0) { error = "Put it on a road"; return null; }
            if (!restoring) foreach (var o in Vehicles) if ((o.Position - graph[n].P).magnitude < (VehicleFactory.Length(o.Kind) + VehicleFactory.Length(kind)) * 0.5f) { error = "Another vehicle is in the way"; return null; }
            var go = new GameObject(kind.ToString());
            go.layer = WonderlandWorld.MiniatureLayer;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = VehicleFactory.Get(kind, colour);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On;
            var v = new Vehicle { Kind = kind, Root = go.transform, Beacons = System.Array.Empty<Transform>(), Home = graph[n].P, Roams = true, Colour = colour, Rng = new Rng(Mathf.RoundToInt(at.x * 9973f + at.y * 7717f) + Vehicles.Count) };
            // Facing the way it was pointed along the road, in the right-hand lane.
            var d = Direction(n);
            if (Mathf.Cos((facing - Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg) * Mathf.Deg2Rad) < 0f) d = -d;
            if (restoring) Place(v, at, facing);   // just where it was
            else Place(v, graph[n].P + new Vector2(d.y, -d.x) * PathSystem.LaneOffset(graph[n].Kind, VehicleFactory.Heavy(kind)), Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
            v.State = Mode.Parked;
            Vehicles.Add(v);
            Roam(v);
            return v;
        }

        public List<PlacedVehicle> ExportPlaced()
        {
            var list = new List<PlacedVehicle>();
            foreach (var v in Vehicles) if (v.Roams) list.Add(new PlacedVehicle { kind = (int)v.Kind, colour = v.Colour, x = v.Position.x, z = v.Position.y, heading = v.Heading });
            return list;
        }

        public void ImportPlaced(IEnumerable<PlacedVehicle> list)
        {
            for (int i = Vehicles.Count - 1; i >= 0; i--) if (Vehicles[i].Roams) { Destroy(Vehicles[i]); Vehicles.RemoveAt(i); }
            if (list == null) return;
            Refresh(true);
            foreach (var p in list) PlaceOnRoad((VehicleKind)p.kind, new Vector2(p.x, p.z), p.heading, p.colour, out _, true);
        }

        /// <summary>Takes away one of the player's vehicles near a point (true if there was one).</summary>
        public bool RemovePlaced(Vector2 p, float within)
        {
            var v = Nearest(p, within);
            if (v == null || !v.Roams) return false;
            Destroy(v); Vehicles.Remove(v);
            return true;
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
