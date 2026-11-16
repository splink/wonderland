using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    [Serializable]
    public struct PersonInstance
    {
        public float x, z;     // home: where they live (updated when they get off the train somewhere else)
        public int style;
        public int seed;
    }

    /// <summary>
    /// Villagers about 3 cm tall. Each strolls about their home: to a neighbour's door for a chat, to a quiet spot to
    /// look around, or - when a train runs - to the station, where they wait on the platform, board when the train stops,
    /// ride a while and get off at the next stop. Whenever the train passes close by they stop, turn and wave. They keep
    /// to dry land, off the rails and out of houses, steering around obstacles. Homes are saved; the rest is runtime.
    /// </summary>
    public sealed class PeopleSystem : MonoBehaviour
    {
        public const float WanderRadius = 0.3f, Speed = 0.022f, Radius = 0.005f;
        public readonly List<PersonInstance> People = new();
        private readonly List<Agent> agents = new();
        private WonderlandWorld world;
        private Material material;
        private float clock;
        private List<StationStop> stations => world.Trains.Stations;

        public enum Activity { Idle, Walking, Chatting, Waiting, Riding, Waving, Sitting, Gathering, Playing, Watching, Browsing, OnRide }
        private readonly HashSet<long> claimed = new();

        private sealed class Agent
        {
            public Transform Root, LegL, LegR, ArmL, ArmR;
            public int Index;
            public Vector2 Home, Position, Target;
            public float Heading, Timer, Phase, Height, Cooldown, Ride, WaveHeading, ResumeTimer;
            public Activity State, Resume;
            public bool ToStation, Cheering;
            public Train Riding;
            public Rng Rng;
            public int SpotHouse = -1, SpotIndex = -1;    // the activity spot they are heading to or using
            public Vector2 SpotHousePos, Approach;
            public float SpotYaw, SpotLift;
            public readonly Queue<Vector2> Waypoints = new();
            public bool Hiking;
        }

        public void Init(WonderlandWorld owner, Material lit) { world = owner; material = lit; }

        public void SetPeople(IEnumerable<PersonInstance> people)
        {
            People.Clear(); People.AddRange(people);
            Rebuild();
        }

        public void Rebuild()
        {
            foreach (var a in agents) Release(a.Root.gameObject);
            agents.Clear();
            for (int i = 0; i < People.Count; i++) agents.Add(Spawn(People[i], i));
        }

        private static void Release(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        private Agent Spawn(PersonInstance p, int index)
        {
            var root = Part("Villager", PersonFactory.Body(p.style), transform);
            var agent = new Agent
            {
                Root = root, Index = index,
                LegL = Part("Leg", PersonFactory.Leg(p.style), root), LegR = Part("Leg", PersonFactory.Leg(p.style), root),
                ArmL = Part("Arm", PersonFactory.Arm(p.style), root), ArmR = Part("Arm", PersonFactory.Arm(p.style), root),
                Home = new Vector2(p.x, p.z), Position = new Vector2(p.x, p.z), Rng = new Rng(p.seed),
            };
            agent.LegL.localPosition = PersonFactory.Hip + Vector3.left * PersonFactory.LegX;
            agent.LegR.localPosition = PersonFactory.Hip + Vector3.right * PersonFactory.LegX;
            agent.ArmL.localPosition = new Vector3(-PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f);
            agent.ArmR.localPosition = new Vector3(PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f);
            agent.Heading = agent.Rng.Range(0f, 360f);
            agent.Timer = agent.Rng.Range(0.5f, 3f);
            agent.Target = agent.Position;
            agent.Height = GroundAt(agent.Position);
            Pose(agent);
            return agent;
        }

        private Transform Part(string name, Mesh mesh, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        public void Add(PersonInstance p) { People.Add(p); agents.Add(Spawn(p, People.Count - 1)); }

        public PersonInstance RemoveAt(int i)
        {
            var p = People[i];
            People.RemoveAt(i);
            Release(agents[i].Root.gameObject);
            agents.RemoveAt(i);
            for (int k = i; k < agents.Count; k++) agents[k].Index = k;
            return p;
        }

        /// <summary>The (visible) villager nearest a local point within <paramref name="within"/>, or -1.</summary>
        public int Nearest(Vector3 p, float within)
        {
            int best = -1; float bestD = within * within;
            for (int i = 0; i < agents.Count; i++)
            {
                if (agents[i].State == Activity.Riding) continue;
                float d = (agents[i].Position - new Vector2(p.x, p.z)).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Villagers whose home became unwalkable (a pond, a new railway or house on it) move to the nearest dry spot
        /// within 15 cm, or leave; those standing somewhere unwalkable go home.
        /// </summary>
        public void Rehome()
        {
            for (int i = People.Count - 1; i >= 0; i--)
            {
                var a = agents[i];
                if (!Walkable(a.Home))
                {
                    if (!FindNear(a.Home, 0.15f, out var spot)) { RemoveAt(i); continue; }
                    a.Home = spot;
                    var p = People[i]; p.x = spot.x; p.z = spot.y; People[i] = p;
                }
                if (a.State != Activity.Riding && !Walkable(a.Position)) { a.Position = a.Home; a.Height = GroundAt(a.Home); a.State = Activity.Idle; a.Timer = 1f; Pose(a); }
            }
        }

        private bool FindNear(Vector2 c, float within, out Vector2 spot)
        {
            for (float r = 0.01f; r <= within; r += 0.01f)
            for (int k = 0; k < 12; k++)
            {
                float ang = k * Mathf.PI / 6f + r * 40f;
                spot = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                if (WalkableGround(spot)) return true;
            }
            spot = c;
            return false;
        }

        public Vector2 PositionOf(int i) => agents[i].Position;
        public bool Hiking(int i) => agents[i].Hiking && agents[i].State == Activity.Walking;

        /// <summary>The nearest visible person within <paramref name="within"/> (wild animals keep their distance).</summary>
        public Vector2? NearestPerson(Vector2 p, float within)
        {
            Vector2? best = null; float bestD = within;
            foreach (var a in agents)
            {
                if (a.State == Activity.Riding || a.State == Activity.OnRide) continue;
                float d = (a.Position - p).magnitude;
                if (d < bestD) { bestD = d; best = a.Position; }
            }
            return best;
        }
        public Activity StateOf(int i) => agents[i].State;
        public bool WavingNow(int i) => agents[i].State == Activity.Waving || agents[i].Cheering;
        public float HeightOf(int i) => agents[i].Height;

        // ------------------------------------------------------------------ where people may walk

        /// <summary>Dry land off the rails (except at level crossings), clear of fences and out of houses - or a platform.</summary>
        public bool Walkable(Vector2 p) => OnPlatform(p, out _) || WalkableGround(p);

        private bool WalkableGround(Vector2 p)
        {
            var t = world.Terrain;
            if (t.EdgeAt(p.x, p.y) < 0.4f || t.WaterAt(p.x, p.y) > 0.05f) return false;
            if (world.Streams.IsWet(p, Radius + 0.002f)) return false;
            if (world.Track.HasTrack && world.Track.DistanceTo(new Vector3(p.x, 0f, p.y)) < TrackSystem.BedHalfWidth + Radius && !world.Paths.NearLevelCrossing(p, 0.035f)) return false;
            if (world.Paths.NearBarrier(p, Radius)) return false;
            return !InsideHouse(p, Radius);
        }

        /// <summary>Inside a house's walls (an oriented box a little inside the eaves), so doorsteps stay reachable.</summary>
        public bool InsideHouse(Vector2 p, float margin)
        {
            foreach (var h in world.Buildings.Houses)
            {
                var bp = Catalog.Get(h.variant);
                if (bp.Small || bp.Walkable) continue;   // people step round benches and lamps, and into playgrounds
                var half = bp.Half * 0.86f + Vector2.one * margin;
                var d = Quaternion.Euler(0f, -h.rotation, 0f) * new Vector3(p.x - h.x, 0f, p.y - h.z);
                if (Mathf.Abs(d.x) < half.x && Mathf.Abs(d.z) < half.y) return true;
            }
            return false;
        }

        /// <summary>Whether a point is on a platform's paving, with the paving's height.</summary>
        public bool OnPlatform(Vector2 p, out float top)
        {
            top = 0f;
            if (stations.Count == 0) return false;
            var net = world.Track.Network;
            var q = new Vector3(p.x, 0f, p.y);
            if (!net.Nearest(q, out int edge, out float s, out float d, 0.09f)) return false;
            if (d < 0.046f || d > 0.076f) return false;   // keep back from the edge, where the coaches overhang
            var layout = net.Edges[edge].Layout;
            var pt = layout.Evaluate(s, out var tangent); tangent.y = 0f; tangent.Normalize();
            int side = Vector3.Dot(Vector3.Cross(Vector3.up, tangent), q - pt) >= 0f ? 1 : -1;
            foreach (var st in stations)
            {
                if (st.Edge != edge || st.Side != side) continue;
                if (TrackNetwork.ArcGap(layout, s, st.S) > WonderlandWorld.PlatformLength / 2f - 0.012f) continue;
                top = pt.y + 0.0135f;
                return true;
            }
            return false;
        }

        private Vector2 PlatformSpot(StationStop st, float offset)
        {
            var layout = world.Track.Network.Edges[st.Edge].Layout;
            var p = layout.Evaluate(layout.Closed ? layout.Wrap(st.S + offset) : Mathf.Clamp(st.S + offset, 0.02f, layout.Length - 0.02f), out var tangent);
            tangent.y = 0f; tangent.Normalize();
            var q = p + Vector3.Cross(Vector3.up, tangent) * st.Side * 0.052f;
            return new Vector2(q.x, q.z);
        }

        private float GroundAt(Vector2 p) => OnPlatform(p, out float top) ? top : world.Terrain.SurfaceAt(p.x, p.y);

        private bool TrainRunning => world.Trains.AnyRunning;

        // ------------------------------------------------------------------ behaviour

        private void Update()
        {
            if (world == null || world.Terrain == null) return;
            Step(Time.deltaTime);
        }

        /// <summary>Advances every villager (also driven by the scripted test).</summary>
        public void Step(float dt)
        {
            clock += dt;
            foreach (var a in agents)
            {
                a.Timer -= dt; a.Cooldown -= dt;
                if (a.State == Activity.Riding) { StepRiding(a, dt); continue; }
                if (a.State == Activity.OnRide) { StepRide(a); continue; }
                if (!Walkable(a.Position) && a.State != Activity.Sitting)   // the world changed under them
                {
                    if (FindNear(a.Position, 0.06f, out var near)) a.Position = near; else a.Position = a.Home;
                    a.State = Activity.Idle; a.Timer = 1f; Release(a);
                }
                a.Cheering = a.State == Activity.Waiting && TrainPassing(a, out _);   // waiting passengers wave on the spot
                if (a.State != Activity.Waving && a.State != Activity.Waiting && a.State != Activity.Playing && a.Cooldown <= 0f && TrainPassing(a, out var toTrain))
                {
                    a.Resume = a.State; a.ResumeTimer = a.Timer; a.State = Activity.Waving; a.Timer = 2.6f; a.Cooldown = 9f;
                    a.WaveHeading = Mathf.Atan2(toTrain.x, toTrain.y) * Mathf.Rad2Deg;
                }
                switch (a.State)
                {
                    case Activity.Idle:
                    case Activity.Chatting:
                        if (a.State == Activity.Idle) a.Heading += Mathf.Sin(clock * 0.7f + a.Home.x * 90f) * 12f * dt;   // looking about
                        if (a.Timer <= 0f) Decide(a);
                        break;
                    case Activity.Walking:
                        Walk(a, dt);
                        break;
                    case Activity.Waiting:
                        a.Heading = Mathf.MoveTowardsAngle(a.Heading, FacingTrack(a), 90f * dt);
                        var here = world.Trains.Near(new Vector3(a.Position.x, 0f, a.Position.y), 0.17f);
                        if (here != null && here.Dwell > 0f && here.Kind != TrainKind.Goods)
                        { a.State = Activity.Riding; a.Riding = here; a.Ride = 0f; a.Root.gameObject.SetActive(false); }
                        else if (a.Timer <= 0f || !TrainRunning) { a.ToStation = false; Decide(a); }
                        break;
                    case Activity.Waving:
                        a.Heading = Mathf.MoveTowardsAngle(a.Heading, a.WaveHeading, 240f * dt);
                        if (TrainPassing(a, out var t)) a.WaveHeading = Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg;
                        if (a.Timer <= 0f) { a.State = a.Resume; a.Timer = Mathf.Max(a.ResumeTimer, 0.5f); }
                        break;
                    case Activity.Sitting:
                    case Activity.Gathering:
                    case Activity.Playing:
                    case Activity.Watching:
                    case Activity.Browsing:
                        a.Heading = Mathf.MoveTowardsAngle(a.Heading, a.SpotYaw + (a.State == Activity.Watching ? Mathf.Sin(clock * 0.4f + a.Home.x * 30f) * 25f : 0f), 200f * dt);
                        if (a.Timer <= 0f || !SpotStillThere(a)) EndActivity(a);
                        break;
                }
                a.Height = Mathf.MoveTowards(a.Height, GroundAt(a.Position) + (a.State == Activity.Watching ? a.SpotLift : 0f), (a.SpotLift > 0f ? 0.4f : 0.12f) * dt);
                Pose(a);
            }
        }

        private bool TrainPassing(Agent a, out Vector2 toTrain)
        {
            toTrain = Vector2.zero;
            float best = 0.24f; bool any = false;
            foreach (var t in world.Trains.Trains)
            {
                if (!t.Running || t.Speed < 0.03f || world.Trains.InTunnel(t)) continue;
                var loco = world.Trains.LocoPosition(t);
                var to = new Vector2(loco.x, loco.z) - a.Position;
                if (to.magnitude < best) { best = to.magnitude; toTrain = to; any = true; }
            }
            return any;
        }

        private float FacingTrack(Agent a)
        {
            var net = world.Track.Network;
            if (!net.Nearest(new Vector3(a.Position.x, 0f, a.Position.y), out int edge, out float s, out _, 0.2f)) return a.Heading;
            var p = net.Edges[edge].Layout.Evaluate(s, out _);
            return Mathf.Atan2(p.x - a.Position.x, p.z - a.Position.y) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Picks what to do next, from what the world offers nearby: the station when trains run; the activity spots of
        /// places around home (a café's chairs, the fountain, a shop's crates, the carousel, a bench, a neighbour's door);
        /// a hike along a trail; a stroll along the roads and paths; or a quiet spot near home.
        /// </summary>
        private void Decide(Agent a)
        {
            Release(a);
            a.Hiking = false;
            a.Waypoints.Clear();
            float roll = a.Rng.Value;
            if (TrainRunning && stations.Count > 0 && roll < 0.2f)
            {
                int best = -1; float bestD = 1.2f;
                for (int i = 0; i < stations.Count; i++)
                {
                    float d = (PlatformSpot(stations[i], 0f) - a.Home).magnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        var spot = PlatformSpot(stations[best], a.Rng.Range(-0.15f, 0.15f));
                        if (Walkable(spot)) { GoTo(a, spot, true); return; }
                    }
                }
            }
            // People living by a trail love a hike; everyone else mostly goes about the village.
            if (a.Rng.Value < 0.3f && TryHike(a)) return;
            if (roll < 0.78f && TrySpot(a)) return;
            if (roll < 0.94f)
            {
                var stroll = world.Paths.StrollTarget(a.Home, 0.45f, ref a.Rng);
                if (stroll.HasValue && Walkable(stroll.Value)) { GoTo(a, stroll.Value, false); return; }
            }
            for (int k = 0; k < 6; k++)
            {
                var t = a.Home + a.Rng.InsideUnitCircle() * WanderRadius;
                if ((t - a.Position).magnitude < 0.03f || !Walkable(t)) continue;
                GoTo(a, t, false);
                return;
            }
            a.State = Activity.Idle; a.Timer = a.Rng.Range(1f, 3f);
        }

        private static long SpotKey(HouseInstance h, int spot) =>
            ((long)Mathf.RoundToInt(h.x * 2000f) << 36) ^ ((long)Mathf.RoundToInt(h.z * 2000f) << 8) ^ (uint)spot;

        private static float Weight(SpotKind k) => k switch
        {
            SpotKind.Door => 0.8f, SpotKind.Sit => 1.3f, SpotKind.Gather => 1.4f, SpotKind.Play => 1.1f,
            SpotKind.Ride => 1.7f, SpotKind.Watch => 1.0f, _ => 1.4f,
        };

        /// <summary>Chooses a free activity spot of a place within reach of home (fun places draw people from further away).</summary>
        private bool TrySpot(Agent a)
        {
            var houses = world.Buildings.Houses;
            var options = new List<(int house, int spot, Vector2 approach, float weight)>();
            float total = 0f;
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                var bp = Catalog.Get(h.variant);
                if (bp.Spots.Length == 0) continue;
                float reach = bp.Category == PlaceCategory.Fun ? 1.4f : 0.9f;
                float far = (new Vector2(h.x, h.z) - a.Home).magnitude;
                if (far > reach) continue;
                for (int k = 0; k < bp.Spots.Length; k++)
                {
                    if (claimed.Contains(SpotKey(h, k))) continue;
                    var sp = bp.Spots[k];
                    var pos = world.Buildings.SpotPosition(i, k, out _);
                    var p2 = new Vector2(pos.x, pos.z);
                    var approach = p2;
                    if (sp.Kind == SpotKind.Ride || sp.At.y > 0f || !Walkable(p2))
                    {
                        // Spots on a ride, up a tower or inside a footprint: walk to the edge of the place first.
                        var dir = p2 - new Vector2(h.x, h.z);
                        if (dir.sqrMagnitude < 1e-8f) dir = (Vector2)(Quaternion.Euler(0f, 0f, -h.rotation) * Vector2.down);
                        approach = new Vector2(h.x, h.z) + dir.normalized * (Mathf.Max(bp.Half.x, bp.Half.y) * 0.95f + 0.014f);
                        if (!Walkable(approach)) continue;
                    }
                    float w = Weight(sp.Kind) / (1f + far * 2f);
                    options.Add((i, k, approach, w));
                    total += w;
                }
            }
            if (options.Count == 0) return false;
            float pick = a.Rng.Value * total;
            foreach (var o in options)
            {
                pick -= o.weight;
                if (pick > 0f) continue;
                var h = houses[o.house];
                a.SpotHouse = o.house; a.SpotIndex = o.spot; a.SpotHousePos = new Vector2(h.x, h.z); a.Approach = o.approach;
                claimed.Add(SpotKey(h, o.spot));
                GoTo(a, o.approach, false);
                return true;
            }
            return false;
        }

        private bool TryHike(Agent a)
        {
            var route = world.Paths.Hike(a.Home, 0.45f, ref a.Rng);
            if (route == null) return false;
            foreach (var p in route) a.Waypoints.Enqueue(p);
            a.Hiking = true;
            a.Target = route[^1]; a.ToStation = false;
            a.State = Activity.Walking; a.Timer = 20f + route.Count * 0.03f / Speed;
            return true;
        }

        private bool SpotStillThere(Agent a)
        {
            var houses = world.Buildings.Houses;
            return a.SpotHouse >= 0 && a.SpotHouse < houses.Count && new Vector2(houses[a.SpotHouse].x, houses[a.SpotHouse].z) == a.SpotHousePos;
        }

        private void Release(Agent a)
        {
            if (a.SpotHouse >= 0 && SpotStillThere(a)) claimed.Remove(SpotKey(world.Buildings.Houses[a.SpotHouse], a.SpotIndex));
            else if (a.SpotHouse >= 0) claimed.Clear();   // the place went: forget stale claims
            a.SpotHouse = -1; a.SpotIndex = -1; a.SpotLift = 0f;
        }

        /// <summary>Arrived at a place: do what its spot is for.</summary>
        private void BeginActivity(Agent a)
        {
            if (!SpotStillThere(a)) { Release(a); a.State = Activity.Idle; a.Timer = 1f; return; }
            var h = world.Buildings.Houses[a.SpotHouse];
            var bp = Catalog.Get(h.variant);
            var sp = bp.Spots[a.SpotIndex];
            var pos = world.Buildings.SpotPosition(a.SpotHouse, a.SpotIndex, out float yaw);
            a.SpotYaw = yaw;
            switch (sp.Kind)
            {
                case SpotKind.Door: a.State = Activity.Chatting; a.Timer = a.Rng.Range(3f, 7f); a.Heading = yaw; return;
                case SpotKind.Ride:
                    a.State = Activity.OnRide; a.Timer = a.Rng.Range(9f, 15f);
                    if (bp.Gondolas > 0) a.Root.gameObject.SetActive(false);   // up in a cabin
                    StepRide(a);
                    return;
            }
            a.Position = new Vector2(pos.x, pos.z);
            a.Heading = yaw;
            a.SpotLift = sp.At.y;
            (a.State, a.Timer) = sp.Kind switch
            {
                SpotKind.Sit => (Activity.Sitting, a.Rng.Range(6f, 14f)),
                SpotKind.Gather => (Activity.Gathering, a.Rng.Range(5f, 12f)),
                SpotKind.Play => (Activity.Playing, a.Rng.Range(4f, 9f)),
                SpotKind.Watch => (Activity.Watching, a.Rng.Range(4f, 9f) + (sp.At.y > 0f ? 4f : 0f)),
                _ => (Activity.Browsing, a.Rng.Range(3f, 7f)),
            };
        }

        private void EndActivity(Agent a)
        {
            if (a.State == Activity.Sitting || a.State == Activity.Playing || a.SpotLift > 0f) a.Position = a.Approach;   // step back out
            Release(a);
            a.State = Activity.Idle; a.Timer = a.Rng.Range(0.5f, 2f);
        }

        /// <summary>On a carousel horse (riding round with it) or up in a Ferris wheel cabin, until the ride is over.</summary>
        private void StepRide(Agent a)
        {
            var rotor = SpotStillThere(a) ? world.Buildings.RotorOf(a.SpotHouse) : null;
            if (rotor == null || a.Timer <= 0f)
            {
                a.Root.gameObject.SetActive(true);
                a.Position = a.Approach; a.Height = GroundAt(a.Approach);
                Release(a);
                a.State = Activity.Idle; a.Timer = a.Rng.Range(0.5f, 2f);
                Pose(a);
                return;
            }
            if (!a.Root.gameObject.activeSelf) return;
            var sp = Catalog.Get(world.Buildings.Houses[a.SpotHouse].variant).Spots[a.SpotIndex];
            var bp = Catalog.Get(world.Buildings.Houses[a.SpotHouse].variant);
            var seat = rotor.TransformPoint(sp.At - bp.RotorPivot + Vector3.up * 0.003f);
            a.Root.position = seat;
            a.Root.rotation = rotor.rotation * Quaternion.Euler(0f, sp.Facing, 0f);
            a.Position = new Vector2(transform.InverseTransformPoint(seat).x, transform.InverseTransformPoint(seat).z);
            PoseLimbs(a, false, 0f);
        }

        /// <summary>Just in front of a house's door (the front, -Z, of every variant).</summary>
        public static Vector2 DoorStep(HouseInstance h)
        {
            float doorX = h.variant == 0 ? -0.022f : 0f;
            var d = Quaternion.Euler(0f, h.rotation, 0f) * new Vector3(doorX, 0f, -(HouseFactory.HalfExtents(h.variant).y * 0.86f + Radius + 0.008f));
            return new Vector2(h.x + d.x, h.z + d.z);
        }

        private void GoTo(Agent a, Vector2 target, bool station)
        {
            a.Target = target; a.ToStation = station;
            a.State = Activity.Walking; a.Timer = 25f;
            // Along the roads and paths when that is the way to go.
            var route = world.Paths.Route(a.Position, target);
            if (route != null)
            {
                foreach (var p in route) a.Waypoints.Enqueue(p);
                a.Timer += route.Count * 0.04f / Speed;
            }
        }

        private void Walk(Agent a, float dt)
        {
            while (a.Waypoints.Count > 0 && (a.Waypoints.Peek() - a.Position).magnitude < 0.01f) a.Waypoints.Dequeue();
            if (a.Waypoints.Count > 0 && a.Timer > 0f)
            {
                Steer(a, a.Waypoints.Peek(), dt);
                return;
            }
            var to = a.Target - a.Position;
            if (to.magnitude < 0.004f || a.Timer <= 0f)
            {
                bool there = to.magnitude < 0.02f;
                a.Waypoints.Clear();
                if (a.SpotHouse >= 0)
                {
                    if (there) BeginActivity(a); else { Release(a); a.State = Activity.Idle; a.Timer = 1f; }
                    return;
                }
                if (a.ToStation && there) { a.State = Activity.Waiting; a.Timer = a.Rng.Range(30f, 50f); }
                else if (there && InsideHouseNear(a.Target)) { a.State = Activity.Chatting; a.Timer = a.Rng.Range(3f, 7f); a.Heading = DoorFacing(a.Target); }
                else { a.State = Activity.Idle; a.Timer = a.Rng.Range(2f, 6f); }
                a.ToStation &= there;
                a.Hiking = false;
                return;
            }
            Steer(a, a.Target, dt);
        }

        private void Steer(Agent a, Vector2 goal, float dt)
        {
            var to = goal - a.Position;
            float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            // Steer around obstacles: the first clear heading nearest the goal, looking a little ahead.
            float step = Mathf.Min(Speed * dt, to.magnitude);
            float chosen = float.NaN;
            foreach (float off in new[] { 0f, 30f, -30f, 60f, -60f, 95f, -95f, 135f, -135f })
            {
                float h = want + off;
                var dir = new Vector2(Mathf.Sin(h * Mathf.Deg2Rad), Mathf.Cos(h * Mathf.Deg2Rad));
                if (Walkable(a.Position + dir * Mathf.Min(0.012f, to.magnitude)) && Walkable(a.Position + dir * step)) { chosen = h; break; }
            }
            if (float.IsNaN(chosen)) { a.Timer = 0f; a.Waypoints.Clear(); return; }
            a.Heading = Mathf.MoveTowardsAngle(a.Heading, chosen, 300f * dt);
            if (Mathf.Abs(Mathf.DeltaAngle(a.Heading, chosen)) > 50f) return;   // turn first
            var fwd = new Vector2(Mathf.Sin(a.Heading * Mathf.Deg2Rad), Mathf.Cos(a.Heading * Mathf.Deg2Rad));
            var next = a.Position + fwd * step;
            if (!Walkable(next)) { a.Heading = chosen; return; }
            a.Position = next;
            a.Phase += step / 0.0105f * Mathf.PI;   // one stride per ~1 cm
        }

        private bool InsideHouseNear(Vector2 p) => InsideHouse(p, Radius + 0.014f);

        private float DoorFacing(Vector2 p)
        {
            float best = float.MaxValue, heading = 0f;
            foreach (var h in world.Buildings.Houses)
            {
                float d = (new Vector2(h.x, h.z) - p).sqrMagnitude;
                if (d < best) { best = d; heading = Mathf.Atan2(h.x - p.x, h.z - p.y) * Mathf.Rad2Deg; }
            }
            return heading;
        }

        private void StepRiding(Agent a, float dt)
        {
            a.Ride += dt;
            var train = a.Riding;
            if (train == null || !world.Trains.Trains.Contains(train) || !train.Running)
            {
                a.Position = a.Home; a.Height = GroundAt(a.Home);
                Alight(a);
                return;
            }
            if (a.Ride < 6f || train.Dwell <= 0f) return;
            // The train has stopped at a station: get off there, and live near it from now on.
            var loco = world.Trains.CentrePosition(train);
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < stations.Count; i++)
            {
                float d = (PlatformSpot(stations[i], 0f) - new Vector2(loco.x, loco.z)).magnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            for (int k = 0; k < 6; k++)
            {
                var spot = PlatformSpot(stations[best], a.Rng.Range(-0.12f, 0.12f));
                if (!Walkable(spot)) continue;
                a.Position = spot; a.Height = GroundAt(spot);
                Alight(a);
                Decide(a);
                return;
            }
        }

        private void Alight(Agent a)
        {
            a.Root.gameObject.SetActive(true);
            a.Riding = null;
            a.State = Activity.Idle; a.Timer = a.Rng.Range(1f, 2f); a.Cooldown = 6f; a.ToStation = false;
            // Find a new home a little away from the platform, on ordinary ground.
            for (int k = 0; k < 10; k++)
            {
                var h = a.Position + a.Rng.InsideUnitCircle() * 0.25f;
                if (!WalkableGround(h) || OnPlatform(h, out _)) continue;
                a.Home = h;
                var p = People[a.Index]; p.x = h.x; p.z = h.y; People[a.Index] = p;
                break;
            }
        }

        private void Pose(Agent a)
        {
            if (a.State == Activity.OnRide) return;
            bool walking = a.State == Activity.Walking;
            float swing = walking ? Mathf.Sin(a.Phase) : 0f;
            float bob = walking ? Mathf.Abs(Mathf.Cos(a.Phase)) * 0.0006f : 0f;
            if (a.State == Activity.Playing) bob = Mathf.Abs(Mathf.Sin(clock * 9f + a.Home.x * 31f)) * 0.004f;
            if (a.State == Activity.Sitting) bob = -0.0055f;
            a.Root.localPosition = new Vector3(a.Position.x, a.Height + bob, a.Position.y);
            a.Root.localRotation = Quaternion.Euler(0f, a.Heading, walking ? swing * 3f : 0f);
            PoseLimbs(a, walking, swing);
        }

        private void PoseLimbs(Agent a, bool walking, float swing)
        {
            float stride = a.Hiking ? 36f : 30f;
            bool seated = a.State == Activity.Sitting || a.State == Activity.OnRide;
            a.LegL.localRotation = seated ? Quaternion.Euler(-80f, 0f, -8f) : Quaternion.Euler(swing * stride, 0f, 0f);
            a.LegR.localRotation = seated ? Quaternion.Euler(-80f, 0f, 8f) : Quaternion.Euler(-swing * stride, 0f, 0f);
            float t = clock + a.Home.y * 40f;
            switch (a.State)
            {
                case Activity.Waving:
                    a.ArmL.localRotation = Quaternion.Euler(0f, 0f, -6f);
                    a.ArmR.localRotation = Quaternion.Euler(0f, 0f, 145f + Mathf.Sin(clock * 11f) * 22f);
                    return;
                case Activity.Playing:
                    a.ArmL.localRotation = Quaternion.Euler(0f, 0f, -150f + Mathf.Sin(t * 9f) * 15f);
                    a.ArmR.localRotation = Quaternion.Euler(0f, 0f, 150f - Mathf.Sin(t * 9f) * 15f);
                    return;
                case Activity.Sitting:
                case Activity.OnRide:
                    a.ArmL.localRotation = Quaternion.Euler(-40f, 0f, -10f);
                    a.ArmR.localRotation = a.State == Activity.OnRide && Mathf.Repeat(t * 0.3f, 1f) > 0.7f ? Quaternion.Euler(0f, 0f, 140f + Mathf.Sin(t * 10f) * 20f) : Quaternion.Euler(-40f, 0f, 10f);
                    return;
                case Activity.Browsing:
                    a.ArmL.localRotation = Quaternion.Euler(-10f, 0f, -6f);
                    a.ArmR.localRotation = Quaternion.Euler(-65f + Mathf.Sin(t * 2.5f) * 20f, 0f, 10f);   // picking things up, looking
                    return;
                case Activity.Watching:
                    a.ArmL.localRotation = Quaternion.Euler(0f, 0f, -6f);
                    a.ArmR.localRotation = Mathf.Repeat(t * 0.15f, 1f) > 0.8f ? Quaternion.Euler(-85f, 0f, 0f) : Quaternion.Euler(0f, 0f, 6f);   // pointing now and then
                    return;
                case Activity.Chatting:
                case Activity.Gathering:
                    a.ArmL.localRotation = Quaternion.Euler(Mathf.Repeat(t * 0.4f, 1f) > 0.5f ? -30f + Mathf.Sin(t * 3.1f) * 12f : 0f, 0f, -8f);
                    a.ArmR.localRotation = Quaternion.Euler(-35f + Mathf.Sin(t * 3f) * 15f, 0f, 10f);   // gesturing
                    return;
                default:
                    a.ArmL.localRotation = Quaternion.Euler(-swing * 26f, 0f, -6f);
                    a.ArmR.localRotation = Quaternion.Euler(a.Cheering ? 0f : swing * 26f, 0f, a.Cheering ? 145f + Mathf.Sin(clock * 11f) * 22f : 6f);
                    return;
            }
        }
    }
}
