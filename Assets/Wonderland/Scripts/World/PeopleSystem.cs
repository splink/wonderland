using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    [Serializable]
    public struct PersonInstance
    {
        public float x, z;     // home: where they live (a house's doorstep, or where they were set down)
        public int style;
        public int seed;
        public float[] needs;  // how much they want each Need (0-1); older saves: null, filled from the seed
        public int[] friends;  // their friends' seeds
    }

    /// <summary>
    /// Villagers about 3 cm tall, living a day at a time. Each has a home, a few needs that grow through the day
    /// (shopping, food, company, fun, sights, rest, and for shopkeepers work), and the village clock. When one finishes
    /// what they were doing they choose what next (see PeopleSystem.Life.cs: needs x the hour x the places within
    /// reach), walk there along roads and paths - by train when it lies across the line - use one of the place's activity
    /// spots (a café chair, the fountain rim, a carousel horse, a shop's door), and later go home. At night they sleep
    /// indoors and the windows glow. Many have a job (PeopleSystem.Work.cs: shifts, and produce carried from the farms
    /// to the shops), they make friends (PeopleSystem.Friends.cs), and outside deciders can hand them plans
    /// (PeopleSystem.Plans.cs). Whenever a train passes close by they stop, turn and wave.
    /// This file carries the intents out: walking, steering, the activities and the poses. Homes, needs and friends are saved.
    /// </summary>
    public sealed partial class PeopleSystem : MonoBehaviour
    {
        public const float WanderRadius = 0.3f, Speed = 0.022f, Radius = 0.005f;
        public const int MaxDecisionsPerStep = 1;     // choosing (and routing) is spread over frames
        public readonly List<PersonInstance> People = new();
        private readonly List<Agent> agents = new();
        private WonderlandWorld world;
        private Material material;
        private float clock;
        private int decisionsThisStep;
        private List<StationStop> stations => world.Trains.Stations;
        private VillageClock Clock => world.Clock;
        public WalkGrid Grid { get; private set; }
        public int Decisions { get; private set; }    // made so far (diagnostics)
        public double WorstDecideMs { get; set; }     // the slowest single decision (choosing + routing), diagnostics
        private readonly System.Diagnostics.Stopwatch decideWatch = new();
        private double decideTotal;

        public enum Activity { Idle, Walking, Chatting, Waiting, Riding, Waving, Sitting, Gathering, Playing, Watching, Browsing, OnRide, Sleeping, Inside, Working, Unwell }
        private readonly HashSet<long> claimed = new();

        private enum Leg { Direct, ToPlatform, Final, Pickup }

        private sealed class Agent
        {
            public Transform Root, LegL, LegR, ArmL, ArmR;
            public int Index, Seed;
            public string Name;
            public Vector2 Home, Position, Target;
            public Vector2 GroundPos = new(float.NaN, 0f);   // where Ground was last looked up (Smart scheduling)
            public float Ground, Chosen = float.NaN, ChosenWant;
            public int Style, Level;   // their outfit, and the level of detail they are drawn at
            public float Pending;      // time not yet thought through (coarse scheduling)
            public float Spin, LastHeading;   // (diagnostics: turning on the spot)
            public string LastSteer;
            public float Hover, Closest = float.MaxValue;   // time since last getting closer to the current waypoint; how close
            public MeshRenderer[] Parts; public MeshFilter[] Filters; public int PartCount = -1;   // drawn together (FigureBatch)
            public float Heading, Timer, Phase, Height, Cooldown, Ride, WaveHeading, ResumeTimer;
            public Activity State, Resume;
            public bool ToStation, Cheering, Hidden;
            public Train Riding;
            public Rng Rng;
            public int SpotHouse = -1, SpotIndex = -1;    // the activity spot they are heading to or using
            public Vector2 SpotHousePos, Approach;
            public float SpotYaw, SpotLift;
            public readonly Queue<Vector2> Waypoints = new();
            public bool Hiking;
            // The mind: needs, the current intent, home and routine.
            public readonly float[] Need = new float[Needs.Count];
            public Intent Plan = Intent.Nothing;
            public int HomeHouse = -1, Workplace = -1;
            public float WakeHour, BedHour;
            public Leg Leg;
            public Vector2 TripFrom, TripTo;              // stations of a train journey (by their building)
            public bool TripAnywhere;                     // a trip for its own sake: get off at the next stop
            public bool Claimed;                          // holds a claim on its spot
            public bool Changing;                         // got off to change trains
            public int LastCall = -1;                     // the last station their train called at
            public readonly List<Agent> Friends = new();
            public long JobKey;                           // the place they work at (to notice a new job)
            public bool Tending, Bare;                    // working a field; found the shelves bare
            public int Carrying;                          // crates in their arms
            public string CargoName;
            public Vector2 FromPos;                       // the place they collect from (by where it stands)
            public Transform Crate;
            public Transform Umbrella;          // made the first time it rains on them
            public bool Aboard, Landed;         // on a boat trip from a landing stage; the boat brought them back
            // The day's plan, favourite places, places they couldn't get to (until when), the planned place last visited.
            public readonly List<Slot> Day = new();
            public int DayOf = -1;
            public readonly Dictionary<Need, long> Favourites = new();
            public Dictionary<long, float> Avoid;
            public long Visited;
            public float VisitedFrom;
            public bool Child, Running;                   // a child (smaller, goes to school, loves the playground); running about a pitch
            public bool SeedChild;                        // born a child (by the seed) - unless they keep a shop
            public uint Hash;
            public int School = -1;                       // children: the school they go to
            public Vector2 Progress;                      // where they were at the last progress check (walking)
            public float ProgressTimer;
            public int Stuck;
        }

        public void Init(WonderlandWorld owner, Material lit)
        {
            world = owner; material = lit;
            Grid = new WalkGrid(Walkable, (a, b) => world.Paths.CrossesBarrier(a, b), p => world.Paths.WayDistance(p, out _) < 0f, GridBounds);
        }

        public void SetPeople(IEnumerable<PersonInstance> people)
        {
            People.Clear(); People.AddRange(people);
            Rebuild();
            LinkFriends();
            seenAll = true;   // the places already there are not news
            WorldChanged();
        }

        public void Rebuild()
        {
            foreach (var a in agents) Release(a.Root.gameObject);
            agents.Clear();
            claimed.Clear();
            for (int i = 0; i < People.Count; i++) agents.Add(Spawn(People[i], i));
        }

        /// <summary>The villagers as saved: homes and needs (copied, so snapshots don't change with them).</summary>
        public PersonInstance[] Export()
        {
            var list = new PersonInstance[agents.Count];
            for (int i = 0; i < agents.Count; i++)
            {
                var p = People[i];
                p.needs = (float[])agents[i].Need.Clone();
                p.friends = FriendSeeds(agents[i]);
                list[i] = p;
            }
            return list;
        }

        private static void Release(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        private Agent Spawn(PersonInstance p, int index)
        {
            var root = Part("Villager", PersonFactory.Body(p.style), transform);
            var agent = new Agent
            {
                Root = root, Index = index, Seed = p.seed, Name = Needs.NameOf(p.seed), Style = p.style,
                LegL = Part("Leg", PersonFactory.Leg(p.style), root), LegR = Part("Leg", PersonFactory.Leg(p.style), root),
                ArmL = Part("Arm", PersonFactory.Arm(p.style), root), ArmR = Part("Arm", PersonFactory.Arm(p.style), root),
                Home = new Vector2(p.x, p.z), Position = new Vector2(p.x, p.z), Rng = new Rng(p.seed),
            };
            agent.LegL.localPosition = PersonFactory.Hip + Vector3.left * PersonFactory.LegX;
            agent.LegR.localPosition = PersonFactory.Hip + Vector3.right * PersonFactory.LegX;
            agent.ArmL.localPosition = new Vector3(-PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f);
            agent.ArmR.localPosition = new Vector3(PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f);
            agent.Crate = Part("Crate", PersonFactory.Crate, root);
            agent.Crate.localPosition = new Vector3(0f, 0.0165f, 0.0062f);
            agent.Crate.gameObject.SetActive(false);
            agent.Heading = agent.Rng.Range(0f, 360f);
            agent.Timer = agent.Rng.Range(0.5f, 3f);
            agent.Target = agent.Position;
            // Early birds and night owls.
            agent.WakeHour = 6f + agent.Rng.Range(0f, 1.6f);
            agent.BedHour = 21f + agent.Rng.Range(0f, 1.5f);
            // Children (smaller, early to bed) and night owls (up past midnight - the disco crowd), fixed by the seed.
            uint h = (uint)p.seed * 2246822519u ^ 0x9E3779B9u; h ^= h >> 15; h *= 0x85EBCA6Bu; h ^= h >> 13;
            agent.Hash = h;
            agent.SeedChild = h % 100 < 20;
            if (!agent.SeedChild && (h >> 7) % 100 < 25) { agent.BedHour = 23.8f + (h >> 9) % 100 / 100f * 0.7f; agent.WakeHour = 7.5f + (h >> 17) % 100 / 100f; }
            SetChild(agent, agent.SeedChild);
            for (int n = 0; n < Needs.Count; n++)
                agent.Need[n] = p.needs != null && n < p.needs.Length ? Mathf.Clamp01(p.needs[n]) : agent.Rng.Range(0.15f, 0.65f);
            agent.Height = GroundAt(agent.Position);
            Pose(agent);
            return agent;
        }

        /// <summary>Whether a villager with this seed is a child (unless they keep a shop).</summary>
        public static bool ChildSeed(int seed) { uint h = (uint)seed * 2246822519u ^ 0x9E3779B9u; h ^= h >> 15; h *= 0x85EBCA6Bu; h ^= h >> 13; return h % 100 < 20; }

        /// <summary>A child: smaller, early to bed and up for school.</summary>
        private static void SetChild(Agent a, bool child)
        {
            if (a.Child == child && a.Root.localScale.x == (child ? 0.72f : 1f)) return;
            a.Child = child;
            a.Root.localScale = Vector3.one * (child ? 0.72f : 1f);
            if (child) { a.BedHour = 19.5f + (a.Hash >> 8) % 100 / 100f; a.WakeHour = 6.5f + (a.Hash >> 16) % 100 / 100f; }
            else if (a.BedHour < 21f) { a.BedHour = 21f + (a.Hash >> 10) % 100 / 100f * 1.5f; a.WakeHour = 6f + (a.Hash >> 18) % 100 / 100f * 1.6f; }
        }

        private Transform Part(string name, Mesh mesh, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = WonderlandWorld.MiniatureLayer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            // The body casts the shadow; arms, legs and the crate add nothing you'd see at this size.
            r.sharedMaterial = material; r.shadowCastingMode = name == "Villager" ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        public void Add(PersonInstance p) { People.Add(p); agents.Add(Spawn(p, People.Count - 1)); placesDirty = true; }

        public PersonInstance RemoveAt(int i)
        {
            var p = People[i];
            Unclaim(agents[i]);
            foreach (var other in agents) other.Friends.Remove(agents[i]);
            People.RemoveAt(i);
            Release(agents[i].Root.gameObject);
            agents.RemoveAt(i);
            for (int k = i; k < agents.Count; k++) agents[k].Index = k;
            placesDirty = true;
            return p;
        }

        /// <summary>The (visible) villager nearest a local point within <paramref name="within"/>, or -1.</summary>
        public int Nearest(Vector3 p, float within)
        {
            int best = -1; float bestD = within * within;
            for (int i = 0; i < agents.Count; i++)
            {
                if (agents[i].Hidden || agents[i].State == Activity.Riding) continue;
                float d = (agents[i].Position - new Vector2(p.x, p.z)).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// The world changed (something built, removed, drawn or sculpted): walking routes and the places directory are
        /// worked out again. Villagers whose home became unwalkable (a pond, a new railway or house on it) move to the
        /// nearest dry spot within 15 cm, or leave; those standing somewhere unwalkable go home.
        /// </summary>
        public void Rehome()
        {
            WorldChanged();
            for (int i = People.Count - 1; i >= 0; i--)
            {
                var a = agents[i];
                if (!Walkable(a.Home) && !NearHomeDoor(a))
                {
                    if (!FindNear(a.Home, 0.15f, out var spot)) { RemoveAt(i); continue; }
                    a.Home = spot;
                    var p = People[i]; p.x = spot.x; p.z = spot.y; People[i] = p;
                }
                if (a.State != Activity.Riding && !a.Hidden && !Walkable(a.Position)) { a.Position = a.Home; a.Height = GroundAt(a.Home); Stop(a); Pose(a); }
            }
        }

        /// <summary>Marks walking routes and the places directory out of date (cheap; the work happens over the next frames).</summary>
        public void WorldChanged() { Grid.Invalidate(); placesDirty = true; }

        private bool NearHomeDoor(Agent a) => a.HomeHouse >= 0 && a.HomeHouse < world.Buildings.Houses.Count && (DoorOf(a.HomeHouse) - a.Home).magnitude < 0.04f;

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
        public bool IsHidden(int i) => agents[i].Hidden;

        /// <summary>The villager the pointer ray is on (local space, unit direction), nearer than <paramref name="maxT"/>; -1 if none.</summary>
        public int PickRay(Vector3 o, Vector3 d, float maxT, out float t)
        {
            int best = -1; float bestScore = 1f; t = float.MaxValue;
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                if (a.Hidden) continue;
                var feet = new Vector3(a.Position.x, a.Height, a.Position.y);
                float score = RayPick.Score(o, d, feet + Vector3.up * 0.004f, feet + Vector3.up * (a.Child ? 0.022f : 0.03f), maxT, out float ti);
                if (score < bestScore) { bestScore = score; best = i; t = ti; }
            }
            return best;
        }

        public int IndexOfSeed(int seed) { for (int i = 0; i < agents.Count; i++) if (agents[i].Seed == seed) return i; return -1; }

        /// <summary>Whether a villager (by seed) is indoors, out of sight (a driver who has come out is no longer driving).</summary>
        public bool Indoors(int seed) { int i = IndexOfSeed(seed); return i >= 0 && agents[i].Hidden; }

        /// <summary>What a villager's camera needs to know: where their eyes are and which way they face, what they are doing.</summary>
        public struct Pov
        {
            public Vector3 Eye;         // local, between the eyes
            public float Heading;
            public bool Hidden, Child, Aboard;
            public Activity State;
            public Train Train;         // the train they ride
            public int House;           // the building they are in (or at), -1
            public Vector2 Where;       // where they are (or went in)
        }

        public Pov PovOf(int i)
        {
            var a = agents[i];
            float scale = a.Child ? 0.72f : 1f;
            int house = a.SpotHouse >= 0 && a.SpotHouse < world.Buildings.Houses.Count ? a.SpotHouse : -1;
            if (house < 0 && a.Hidden) house = world.Buildings.Nearest(a.Approach, 0.12f);
            return new Pov
            {
                Eye = new Vector3(a.Position.x, a.Height + 0.0272f * scale, a.Position.y), Heading = a.Heading,
                Hidden = a.Hidden, Child = a.Child, Aboard = a.Aboard, State = a.State,
                Train = a.State == Activity.Riding ? a.Riding : null, House = house, Where = a.Hidden ? a.Approach : a.Position,
            };
        }

        /// <summary>
        /// A household's car is setting off from <paramref name="home"/>: someone at home (indoors, with at least
        /// <paramref name="seconds"/> of their stay left) drives it - their camera rides along. Nothing about their day
        /// changes: if they come out before the car is back, they simply aren't in it any more. Their seed, or 0.
        /// </summary>
        public int TakeDriver(Vector2 home, float seconds)
        {
            var houses = world.Buildings.Houses;
            foreach (var a in agents)
            {
                if (!a.Hidden || a.State != Activity.Inside || a.Child || a.HomeHouse < 0 || a.HomeHouse >= houses.Count) continue;
                if ((DoorStep(houses[a.HomeHouse]) - home).magnitude > 0.12f || a.Timer < seconds) continue;
                return a.Seed;
            }
            return 0;
        }

        /// <summary>The nearest visible person within <paramref name="within"/> (wild animals keep their distance).</summary>
        public Vector2? NearestPerson(Vector2 p, float within)
        {
            Vector2? best = null; float bestD = within;
            foreach (var a in agents)
            {
                if (a.Hidden || a.State == Activity.Riding || a.State == Activity.OnRide) continue;
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
        public bool Walkable(Vector2 p) { walkableCalls++; return Grid.Interior(p) || OnPlatform(p, out _) || WalkableGround(p); }

        /// <summary>Times each part of the exact walkability check over random points of the land (diagnostics).</summary>
        public string ProfileWalkable(int samples)
        {
            var land = world.Terrain.LandBounds();
            var rng = new Rng(7);
            var pts = new Vector2[samples];
            for (int i = 0; i < samples; i++) pts[i] = new Vector2(rng.Range(land.xMin, land.xMax), rng.Range(land.yMin, land.yMax));
            var w = new System.Diagnostics.Stopwatch();
            string Time(string name, System.Func<Vector2, bool> f) { w.Restart(); int n = 0; foreach (var p in pts) if (f(p)) n++; return $"{name} {w.Elapsed.TotalMilliseconds * 1000.0 / samples:0.0} us"; }
            var t = world.Terrain;
            return string.Join(", ", new[]
            {
                Time("platform", p => OnPlatform(p, out _)),
                Time("deck", p => world.Bridges.Deck(p, out _, out _, -Radius * 0.5f)),
                Time("terrain", p => t.EdgeAt(p.x, p.y) < 0.4f || t.WaterAt(p.x, p.y) > 0.05f),
                Time("streams", p => world.Streams.IsWet(p, Radius + 0.002f)),
                Time("track", p => world.Track.HasTrack && world.Track.Near(new Vector3(p.x, 0f, p.y), TrackSystem.BedHalfWidth + Radius)),
                Time("crossing", p => world.Paths.NearLevelCrossing(p, 0.056f)),
                Time("barrier", p => world.Paths.NearBarrier(p, Radius)),
                Time("houses", p => InsideHouse(p, Radius)),
                Time("interior", p => Grid.Interior(p)),
            });
        }
        private long walkableCalls, stepFrames;
        private double walkMs, decideMs, upkeepMs;
        /// <summary>Where the villagers' time goes (diagnostics): per-frame averages since the last read.</summary>
        public string Diagnostics
        {
            get
            {
                long f = System.Math.Max(1, stepFrames);
                int toPlace = 0, toWork = 0, home = 0, hike = 0, other = 0;
                foreach (var a in agents)
                {
                    if (a.State != Activity.Walking) continue;
                    if (a.Hiking || a.Plan.What == Intent.Kind.Stroll) hike++;
                    else if (a.Plan.What is Intent.Kind.Home or Intent.Kind.Sleep) home++;
                    else if (a.Plan.What is Intent.Kind.Work or Intent.Kind.Haul) toWork++;
                    else if (a.Plan.What is Intent.Kind.Spot or Intent.Kind.Neighbour or Intent.Kind.Gather or Intent.Kind.MoveIn) toPlace++;
                    else other++;
                }
                string s = $"walking: {toPlace} to places, {toWork} to work, {home} home, {hike} hiking or strolling, {other} other; {walkableCalls / f} walkable checks, walking {walkMs / f:0.000} ms, deciding {decideMs / f:0.000} ms, upkeep {upkeepMs / f:0.000} ms per frame";
                walkableCalls = 0; stepFrames = 0; walkMs = decideMs = upkeepMs = 0;
                return s;
            }
        }

        private bool WalkableGround(Vector2 p)
        {
            var t = world.Terrain;
            // On a road or path bridge: walk across the water.
            if (world.Bridges.Deck(p, out _, out _, -Radius * 0.5f))
                return !(world.Track.HasTrack && world.Track.Near(new Vector3(p.x, 0f, p.y), TrackSystem.BedHalfWidth + Radius)) && !InsideHouse(p, Radius);
            if (t.EdgeAt(p.x, p.y) < 0.4f || t.WaterAt(p.x, p.y) > 0.05f) return false;
            if (world.Streams.IsWet(p, Radius + 0.002f)) return false;
            if (world.Track.HasTrack && world.Track.Near(new Vector3(p.x, 0f, p.y), TrackSystem.BedHalfWidth + Radius) && !world.Paths.NearLevelCrossing(p, 0.056f)) return false;   // the crossing spans the bed
            if (world.Paths.NearBarrier(p, Radius)) return false;
            return !InsideHouse(p, Radius);
        }

        private Rect GridBounds()
        {
            var land = world.Terrain.LandBounds();
            return land.width > 0f ? Rect.MinMaxRect(land.xMin - 0.05f, land.yMin - 0.05f, land.xMax + 0.05f, land.yMax + 0.05f) : land;
        }

        /// <summary>Inside a house's walls (an oriented box a little inside the eaves), so doorsteps stay reachable.</summary>
        public bool InsideHouse(Vector2 p, float margin)
        {
            foreach (var h in world.Buildings.Houses)
            {
                var bp = Catalog.Get(h.variant);
                if (bp.Small || bp.Walkable) continue;   // people step round benches and lamps, and into playgrounds
                float dx = p.x - h.x, dz = p.y - h.z, reach = bp.Half.x + bp.Half.y + margin;
                if (dx * dx + dz * dz > reach * reach) continue;   // far away: skip the rotation
                var half = bp.Half * 0.86f + Vector2.one * margin;
                var d = Quaternion.Euler(0f, -h.rotation, 0f) * new Vector3(dx, 0f, dz);
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

        private float GroundAt(Vector2 p) => OnPlatform(p, out float top) ? top : world.Bridges.Deck(p, out float deck, out _) ? deck : world.Terrain.SurfaceAt(p.x, p.y);

        private bool TrainRunning => world.Trains.AnyRunning;

        // ------------------------------------------------------------------ the day

        private void Update()
        {
            if (world == null || world.Terrain == null) return;
            long t0 = SimCost.Now; Step(Time.deltaTime); SimCost.Add(SimCost.People, t0);
        }

        private readonly FigureBatch batch = new();
        private bool drawnTogether;

        /// <summary>All the villagers drawn together, a draw per body part mesh (see <see cref="FigureBatch"/>).</summary>
        private void LateUpdate()
        {
            if (world == null || world.Terrain == null) return;
            if (!FigureBatch.Together)
            {
                if (drawnTogether) foreach (var a in agents) if (a.Parts != null) FigureBatch.Restore(a.Parts);
                drawnTogether = false;
                return;
            }
            drawnTogether = true;
            batch.Begin(material, world.Cctv != null && world.Cctv.Watching, p => world.Cctv != null && world.Cctv.Filming(transform.InverseTransformPoint(p)));
            foreach (var a in agents)
            {
                if (a.Hidden || !a.Root.gameObject.activeSelf || !batch.Visible(a.Root.position)) continue;
                if (a.Parts == null || a.Root.childCount != a.PartCount)
                {
                    a.Parts = a.Root.GetComponentsInChildren<MeshRenderer>(true);
                    a.Filters = System.Array.ConvertAll(a.Parts, r => r.GetComponent<MeshFilter>());
                    a.PartCount = a.Root.childCount;
                }
                batch.Add(a.Parts, a.Filters);
            }
            batch.Draw(WonderlandWorld.MiniatureLayer);
        }

        /// <summary>Advances every villager (also driven by the scripted test). The village clock is ticked by the world.</summary>
        public void Step(float dt)
        {
            clock += dt;
            stepFrames++;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            decisionsThisStep = 0;
            tick++;
            bool smart = PerfSettings.SmartVillagers;
            var cctv = world.Cctv;   // (on a CCTV feed - out of the player's sight or not - they move smoothly)
            FindLocos();
            MapCrowd();
            var eye = Camera.main != null ? (Vector3?)transform.InverseTransformPoint(Camera.main.transform.position) : null;
            var viewDir = Camera.main != null ? transform.InverseTransformDirection(Camera.main.transform.forward) : Vector3.forward;
            float drawn = WonderlandWorld.MiniatureCullDistance / Mathf.Max(transform.lossyScale.x, 1e-3f) + 0.1f;
            Grid.Update(placesBuilt ? 2f : 6f);
            RefreshPlaces();
            // Needs grow with village time (at the normal pace while the clock is stopped).
            float hours = Clock.Mode == ClockMode.Paused ? dt / VillageClock.SecondsPerHour : Clock.HoursPer(dt);
            lightsTimer -= dt;
            if (lightsTimer <= 0f) { lightsTimer = 0.5f; UpdateLights(); UpdateSupply(); PruneDeciders(); }
            StepFriendships(dt);
            if (pitches > 0 || matches.Count > 0) StepMatches(dt);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            upkeepMs += (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            double decideBefore = decideTotal;
            foreach (var a in agents)
            {
                // Coarse (Smart): behind the player or beyond drawing distance, a villager thinks every fourth frame,
                // catching up the time in between - nothing goes slower, it just goes in bigger steps nobody sees.
                float dtA = dt, hoursA = hours;
                if (smart && eye.HasValue && !(cctv != null && cctv.Filming(new Vector3(a.Position.x, a.Height, a.Position.y))))
                {
                    var rel = new Vector3(a.Position.x, a.Height, a.Position.y) - eye.Value;
                    bool coarse = rel.sqrMagnitude > drawn * drawn || Vector3.Dot(rel, viewDir) < -0.05f;
                    a.Pending += dt;
                    if (coarse && ((tick + a.Index) & 3) != 0) continue;
                    dtA = a.Pending; a.Pending = 0f;
                    hoursA = hours * dtA / Mathf.Max(dt, 1e-5f);
                }
                for (int n = 0; n < Needs.Count; n++) a.Need[n] = Mathf.Min(1f, a.Need[n] + Needs.GrowthOf((Need)n) * hoursA);
                a.Timer -= dtA; a.Cooldown -= dtA;
                if (a.State == Activity.Sleeping) { StepSleeping(a); continue; }
                if (a.State == Activity.Inside)
                {
                    bool atHome = a.Plan.What == Intent.Kind.Home || a.Plan.What == Intent.Kind.MoveIn;
                    if (atHome && Bedtime(a, Clock.Hour) && SpotStillThere(a)) { a.State = Activity.Sleeping; a.Plan.What = Intent.Kind.Sleep; continue; }   // stays in for the night
                    if (a.Timer <= 0f || !SpotStillThere(a) || (!atHome && (Closed(a) || SlotOver(a)))) EndActivity(a);
                    continue;
                }
                if (a.State == Activity.Riding) { StepRiding(a, dtA); continue; }
                if (a.State == Activity.OnRide) { StepRide(a); continue; }
                int beat = tick + a.Index;   // (Smart: the occasional checks spread over the frames)
                if ((!smart || (beat & 7) == 0) && !Walkable(a.Position) && a.State != Activity.Sitting && a.State != Activity.Unwell)   // the world changed under them
                {
                    if (FindNear(a.Position, 0.06f, out var near)) a.Position = near; else a.Position = a.Home;
                    Stop(a);
                }
                bool look = !smart || (beat & 3) == 0;
                if (look) a.Cheering = a.State == Activity.Waiting && TrainPassing(a, out _);   // waiting passengers wave on the spot
                if (look && a.State != Activity.Waving && a.State != Activity.Waiting && a.State != Activity.Playing && a.State != Activity.Unwell && a.Cooldown <= 0f && TrainPassing(a, out var toTrain))
                {
                    a.Resume = a.State; a.ResumeTimer = a.Timer; a.State = Activity.Waving; a.Timer = 2.6f; a.Cooldown = 9f;
                    a.WaveHeading = Mathf.Atan2(toTrain.x, toTrain.y) * Mathf.Rad2Deg;
                }
                switch (a.State)
                {
                    case Activity.Idle:
                    case Activity.Chatting:
                        if (a.State == Activity.Idle) a.Heading += Mathf.Sin(clock * 0.7f + a.Home.x * 90f) * 12f * dtA;   // looking about
                        if (a.Timer <= 0f) { if (a.State == Activity.Chatting) Satisfy(a, 0.3f); Decide(a); }
                        break;
                    case Activity.Unwell:
                        a.Heading = Mathf.MoveTowardsAngle(a.Heading, a.SpotYaw, 60f * dtA);
                        break;
                    case Activity.Walking:
                        if (a.Plan.What == Intent.Kind.Gather && Deciders.Count == 0) { Stop(a); break; }   // the occasion is over
                        if (a.Plan.What == Intent.Kind.Home && Bedtime(a, Clock.Hour)) a.Plan.What = Intent.Kind.Sleep;   // on the way home anyway: straight to bed
                        else if (a.Plan.What is Intent.Kind.Spot or Intent.Kind.Neighbour or Intent.Kind.Gather && (Bedtime(a, Clock.Hour) || Closed(a))) { Stop(a, 0.3f); break; }   // too late now (or it has shut): home instead
                        Walk(a, dtA);
                        break;
                    case Activity.Waiting:
                        a.Heading = Mathf.MoveTowardsAngle(a.Heading, FacingTrack(a), 90f * dtA);
                        var here = world.Trains.Near(new Vector3(a.Position.x, 0f, a.Position.y), 0.17f);
                        if (here != null && here.Dwell > 0f && here.Kind != TrainKind.Goods && Bound(a, here))
                        { a.State = Activity.Riding; a.Riding = here; a.Ride = 0f; a.Changing = false; a.LastCall = here.AtStation; Hide(a, true); }
                        else if (a.Timer <= 0f || !TrainRunning || Bedtime(a, Clock.Hour) && a.Plan.What is not (Intent.Kind.Sleep or Intent.Kind.Home)) { a.ToStation = false; Decide(a); }
                        break;
                    case Activity.Waving:
                        a.Heading = Mathf.MoveTowardsAngle(a.Heading, a.WaveHeading, 240f * dtA);
                        if (TrainPassing(a, out var t)) a.WaveHeading = Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg;
                        if (a.Timer <= 0f) { a.State = a.Resume; a.Timer = Mathf.Max(a.ResumeTimer, 0.5f); }
                        break;
                    case Activity.Working:
                        a.Heading = Mathf.MoveTowardsAngle(a.Heading, a.SpotYaw, 200f * dtA);
                        Grow(a, hoursA);
                        if (a.Timer <= 0f || !SpotStillThere(a) || !Catalog.Get(world.Buildings.Houses[a.SpotHouse].variant).OnShift(Clock.Hour)) EndActivity(a);
                        break;
                    case Activity.Sitting:
                    case Activity.Gathering:
                    case Activity.Playing:
                    case Activity.Watching:
                    case Activity.Browsing:
                        if (a.State == Activity.Playing && Pitch(a) is { } pitch) StepPlayer(a, pitch, dtA);
                        if ((Bedtime(a, Clock.Hour) || HeadHome(a, Clock.Hour) || Closed(a) || SlotOver(a) || (a.Plan.What == Intent.Kind.Gather && Deciders.Count == 0)) && a.Timer > 1.5f) a.Timer = a.Rng.Range(0.5f, 2.5f);   // time to wrap up and go
                        a.Heading = Mathf.MoveTowardsAngle(a.Heading, a.SpotYaw + (a.State == Activity.Watching ? Mathf.Sin(clock * 0.4f + a.Home.x * 30f) * 25f : 0f), 200f * dtA);
                        if (a.Timer <= 0f || (a.SpotHouse >= 0 && !SpotStillThere(a))) EndActivity(a);
                        break;
                }
                // The ground under them: looked up again once they have moved (or now and then: the land may change).
                if (!smart || (beat & 7) == 0 || !((a.Position - a.GroundPos).sqrMagnitude < 0.0015f * 0.0015f)) { a.Ground = GroundAt(a.Position); a.GroundPos = a.Position; }
                if (Diagnose)
                {
                    a.Spin = a.Spin * Mathf.Exp(-dtA * 1.2f) + Mathf.DeltaAngle(a.LastHeading, a.Heading);
                    if (Mathf.Abs(a.Spin) > 270f)
                    {
                        Spins++; a.Spin = 0f;
                        if (SpinNotes.Count < 12) SpinNotes.Add($"{a.State} {a.Plan.What} wp {a.Waypoints.Count} to-target {(a.Target - a.Position).magnitude * 100f:0.0} cm stuck {a.Stuck} last-steer {a.LastSteer}");
                    }
                }
                a.LastHeading = a.Heading;
                a.Height = Mathf.MoveTowards(a.Height, a.Ground + (a.State == Activity.Watching ? a.SpotLift : 0f), (a.SpotLift > 0f ? 0.4f : 0.12f) * dtA);
                if (eye.HasValue && (beat & 7) == 0) Detail(a, (new Vector3(a.Position.x, a.Height, a.Position.y) - eye.Value).magnitude * transform.lossyScale.x);
                // Beyond the distance figures are drawn at, nobody sees them move: posed only now and then.
                if (smart && eye.HasValue && (beat & 7) != 0 && !(cctv != null && cctv.Filming(new Vector3(a.Position.x, a.Height, a.Position.y))) && (new Vector3(a.Position.x, a.Height, a.Position.y) - eye.Value).sqrMagnitude > drawn * drawn) continue;
                Pose(a);
            }
            double stepMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            decideMs += decideTotal - decideBefore;
            walkMs += stepMs - (decideTotal - decideBefore);
        }

        private void Hide(Agent a, bool hidden)
        {
            a.Hidden = hidden;
            a.Root.gameObject.SetActive(!hidden);
        }

        /// <summary>Stops whatever they were doing and stands about for a moment before choosing again.</summary>
        private void Stop(Agent a, float wait = 1f)
        {
            Unclaim(a);
            if (a.Hidden) Hide(a, false);
            a.Riding = null; a.ToStation = false; a.Hiking = false; a.Waypoints.Clear(); a.Changing = false;
            a.SpotHouse = -1; a.SpotIndex = -1; a.SpotLift = 0f;
            a.Plan = Intent.Nothing;
            DropCrates(a);
            a.State = Activity.Idle; a.Timer = wait;
        }

        /// <summary>Asleep: at home (indoors) or, without a home to go to, wherever they settled. They wake with the morning.</summary>
        private void StepSleeping(Agent a)
        {
            bool bedGone = a.SpotHouse >= 0 ? !SpotStillThere(a) : a.Hidden;
            if (!bedGone && Bedtime(a, Clock.Hour)) return;
            a.Need[(int)Need.Rest] = 0f;
            if (a.Hidden || a.SpotHouse >= 0) { a.Position = a.Approach; a.Height = GroundAt(a.Position); a.Heading = a.SpotYaw + 180f; }
            Stop(a, a.Rng.Range(0.3f, 1.5f));
            Pose(a);
        }

        private int tick;
        private readonly List<Vector2> locos = new();

        /// <summary>A simpler figure further away (the same outfit, fewer facets, no buttons), unless Distance is set to Full.</summary>
        private static void Detail(Agent a, float distance)
        {
            int level = Lod.For(0.035f, distance, a.Level);
            if (level == a.Level) return;
            a.Level = level;
            a.Root.GetComponent<MeshFilter>().sharedMesh = PersonFactory.Body(a.Style, level);
            a.LegL.GetComponent<MeshFilter>().sharedMesh = a.LegR.GetComponent<MeshFilter>().sharedMesh = PersonFactory.Leg(a.Style, level);
            a.ArmL.GetComponent<MeshFilter>().sharedMesh = a.ArmR.GetComponent<MeshFilter>().sharedMesh = PersonFactory.Arm(a.Style, level);
        }

        /// <summary>Where the moving engines are, once a frame (for everyone's "a train is passing" check).</summary>
        private void FindLocos()
        {
            locos.Clear();
            foreach (var t in world.Trains.Trains)
            {
                if (!t.Running || t.Speed < 0.03f || world.Trains.InTunnel(t)) continue;
                var loco = world.Trains.LocoPosition(t);
                locos.Add(new Vector2(loco.x, loco.z));
            }
        }

        private bool TrainPassing(Agent a, out Vector2 toTrain)
        {
            toTrain = Vector2.zero;
            float best = 0.24f * 0.24f; bool any = false;
            foreach (var loco in locos)
            {
                var to = loco - a.Position;
                if (to.sqrMagnitude < best) { best = to.sqrMagnitude; toTrain = to; any = true; }
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

        // ------------------------------------------------------------------ carrying out an intent

        /// <summary>Done with the last thing: choose the next (at most a couple of villagers per frame) and set off.</summary>
        private void Decide(Agent a)
        {
            Unclaim(a);
            a.Hiking = false;
            a.Waypoints.Clear();
            a.SpotHouse = -1; a.SpotIndex = -1; a.SpotLift = 0f;
            if (decisionsThisStep >= MaxDecisionsPerStep) { a.State = Activity.Idle; a.Timer = 0.05f + a.Rng.Range(0f, 0.2f); return; }
            decisionsThisStep++; Decisions++;
            long d0 = System.Diagnostics.Stopwatch.GetTimestamp();
            decideWatch.Restart();
            if (Deciders.Count > 0 && !Bedtime(a, Clock.Hour) && AskDeciders(a, out var told)) Carry(a, told);
            else Carry(a, Choose(a));
            WorstDecideMs = System.Math.Max(WorstDecideMs, decideWatch.Elapsed.TotalMilliseconds);
            decideTotal += (System.Diagnostics.Stopwatch.GetTimestamp() - d0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>Sets off to do what an intent says.</summary>
        private void Carry(Agent a, Intent plan)
        {
            a.Plan = plan;
            a.Leg = Leg.Direct;
            if (plan.What == Intent.Kind.Sleep && plan.House < 0) { SettleOutdoors(a); return; }
            switch (plan.What)
            {
                case Intent.Kind.Spot:
                case Intent.Kind.Home:
                case Intent.Kind.Sleep:
                case Intent.Kind.MoveIn:
                case Intent.Kind.Neighbour:
                case Intent.Kind.Work:
                case Intent.Kind.Haul:
                case Intent.Kind.Gather:
                    a.SpotHouse = plan.House; a.SpotIndex = plan.Spot;
                    a.SpotHousePos = new Vector2(world.Buildings.Houses[plan.House].x, world.Buildings.Houses[plan.House].z);
                    a.Approach = plan.Target;
                    if (plan.What == Intent.Kind.Spot || (plan.What == Intent.Kind.Work && plan.Spot >= 0) || (plan.What == Intent.Kind.Sleep && !Catalog.Get(world.Buildings.Houses[plan.House].variant).Dwelling)) Claim(a);
                    if (plan.What == Intent.Kind.Haul)
                    {
                        var from = world.Buildings.Houses[plan.From];
                        a.FromPos = new Vector2(from.x, from.z);
                        if ((plan.Pickup - a.Position).magnitude > 0.02f) { a.Leg = Leg.Pickup; GoTo(a, plan.Pickup, false); return; }
                        if (!PickUp(a)) { Stop(a, a.Rng.Range(1f, 2f)); return; }
                    }
                    break;
                case Intent.Kind.Hike:
                    if (!StartHike(a)) Stop(a, a.Rng.Range(1f, 3f));
                    return;
                case Intent.Kind.None:
                    a.State = Activity.Idle; a.Timer = a.Rng.Range(1.5f, 4f);
                    return;
            }
            if (plan.ByTrain || plan.What == Intent.Kind.TrainTrip)
            {
                a.TripFrom = stations[plan.FromStation].House;
                a.TripAnywhere = plan.What == Intent.Kind.TrainTrip;
                a.TripTo = a.TripAnywhere ? Vector2.zero : stations[plan.ToStation].House;
                a.Leg = Leg.ToPlatform;
                GoTo(a, PlatformFor(plan.FromStation, a), true);
                return;
            }
            GoTo(a, plan.Target, false);
        }


        private Vector2 PlatformFor(int station, Agent a)
        {
            for (int k = 0; k < 6; k++)
            {
                var spot = PlatformSpot(stations[station], a.Rng.Range(-0.15f, 0.15f));
                if (Walkable(spot)) return spot;
            }
            return PlatformSpot(stations[station], 0f);
        }

        private static long SpotKey(HouseInstance h, int spot) =>
            ((long)Mathf.RoundToInt(h.x * 2000f) << 36) ^ ((long)Mathf.RoundToInt(h.z * 2000f) << 8) ^ (uint)spot;

        private void Claim(Agent a)
        {
            if (a.SpotHouse < 0 || !SpotStillThere(a)) return;
            claimed.Add(SpotKey(world.Buildings.Houses[a.SpotHouse], a.SpotIndex));
            a.Claimed = true;
        }

        private void Unclaim(Agent a)
        {
            if (!a.Claimed) return;
            a.Claimed = false;
            if (a.SpotHouse >= 0 && SpotStillThere(a)) claimed.Remove(SpotKey(world.Buildings.Houses[a.SpotHouse], a.SpotIndex));
            else claimed.Clear();   // the place went: forget stale claims (the others re-claim as they arrive)
        }

        private bool Taken(int house, int spot) => claimed.Contains(SpotKey(world.Buildings.Houses[house], spot));

        private bool SpotStillThere(Agent a)
        {
            var houses = world.Buildings.Houses;
            return a.SpotHouse >= 0 && a.SpotHouse < houses.Count && new Vector2(houses[a.SpotHouse].x, houses[a.SpotHouse].z) == a.SpotHousePos;
        }

        /// <summary>The need they meant to satisfy is (mostly) met.</summary>
        private void Satisfy(Agent a, float keep = 0.15f)
        {
            if (a.Plan.For == Need.None) return;
            a.Need[(int)a.Plan.For] *= a.Bare ? Mathf.Max(keep, 0.55f) : keep;   // bare shelves: still wanting
            if (a.Plan.What == Intent.Kind.Gather) a.Need[(int)Need.Fun] *= 0.5f;    // a fête is fun too
            a.Bare = false;
        }

        /// <summary>Arrived: do what the place is for.</summary>
        private void Arrive(Agent a)
        {
            var plan = a.Plan;
            if (plan.What == Intent.Kind.Sleep && plan.House < 0) { SettleOutdoors(a); return; }
            if (!SpotStillThere(a)) { Stop(a); return; }
            var h = world.Buildings.Houses[a.SpotHouse];
            var bp = Catalog.Get(h.variant);
            switch (plan.What)
            {
                case Intent.Kind.Haul:
                    if (a.Leg == Leg.Pickup)
                    {
                        // Collected: on to where it is wanted.
                        if (!PickUp(a)) { Stop(a, a.Rng.Range(1f, 2f)); return; }
                        a.Leg = Leg.Direct; GoTo(a, plan.Target, false);
                        return;
                    }
                    Deliver(a);
                    a.SpotHouse = -1; a.SpotIndex = -1; a.Plan = Intent.Nothing;
                    a.State = Activity.Idle; a.Timer = a.Rng.Range(1f, 2.5f);   // a word with whoever takes it in
                    return;
                case Intent.Kind.Work:
                {
                    var place = PlaceAt(a.SpotHouse);
                    float workYaw = place != null ? place.WorkYaw : h.rotation + 180f;
                    a.SpotYaw = workYaw; a.Heading = workYaw;
                    a.Tending = bp.Farm;
                    a.State = Activity.Working; a.Timer = a.Rng.Range(25f, 45f);
                    a.Approach = a.Position;
                    if (bp.WorksIndoors) Hide(a, true);
                    NoteVisit(a.SpotHouse, a);
                    return;
                }
                case Intent.Kind.Gather:
                {
                    var to = a.SpotHousePos - a.Position;
                    a.SpotYaw = a.Heading = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                    a.Approach = a.Position;
                    a.State = Activity.Gathering; a.Timer = a.Rng.Range(20f, 40f);
                    return;
                }
            }
            CountVisit(a.SpotHouse);
            var pos = world.Buildings.SpotPosition(a.SpotHouse, a.SpotIndex, out float yaw);
            a.SpotYaw = yaw;
            switch (plan.What)
            {
                case Intent.Kind.Sleep when bp.Dwelling && !Bedtime(a, Clock.Hour):
                    // Home in good time: in for the evening (bed comes at bedtime).
                    a.Plan.What = Intent.Kind.Home;
                    a.State = Activity.Inside; a.Timer = a.Rng.Range(12f, 25f); a.Approach = a.Position; Hide(a, true);
                    return;
                case Intent.Kind.Sleep:
                    a.State = Activity.Sleeping;
                    if (bp.Dwelling) { a.Approach = a.Position; Hide(a, true); }
                    else { a.Position = new Vector2(pos.x, pos.z); a.Heading = yaw; }   // a night on the bench
                    return;
                case Intent.Kind.MoveIn:
                    a.Home = a.Position;
                    var p = People[a.Index]; p.x = a.Home.x; p.z = a.Home.y; People[a.Index] = p;
                    a.HomeHouse = a.SpotHouse;
                    News = $"{a.Name} moved into the {bp.Name.ToLower()}";
                    placesDirty = true;
                    a.State = Activity.Inside; a.Timer = a.Rng.Range(6f, 10f); a.Approach = a.Position; Hide(a, true);
                    return;
                case Intent.Kind.Home:
                    a.State = Activity.Inside; a.Timer = a.Rng.Range(12f, 25f); a.Approach = a.Position; Hide(a, true);
                    return;
                case Intent.Kind.Neighbour:
                    a.State = Activity.Chatting; a.Timer = a.Rng.Range(5f, 10f); a.Heading = yaw;
                    ComeOut(a);
                    return;
            }
            var sp = bp.Spots[a.SpotIndex];
            NoteVisit(a.SpotHouse, a);
            if (plan.Planned && PlaceAt(a.SpotHouse) is { } visited && Now(a, Clock.Hour) is { } slot) { a.Visited = visited.Key; a.VisitedFrom = slot.From; }
            if (plan.For == Need.Work) { a.State = Activity.Working; a.Timer = a.Rng.Range(25f, 45f); a.Heading = yaw + 180f; a.SpotYaw = yaw + 180f; a.Tending = false; return; }
            a.Bare = (plan.For == Need.Errands || plan.For == Need.Food) && PlaceAt(a.SpotHouse) is { } shop && !Sell(shop);
            switch (sp.Kind)
            {
                case SpotKind.Door:
                    // In through the door: shopping, a service, a meeting.
                    a.State = Activity.Inside; a.Timer = a.Rng.Range(6f, 12f) * (plan.Planned ? 1.8f : 1f); a.Approach = a.Position; Hide(a, true);
                    return;
                case SpotKind.Ride:
                    a.State = Activity.OnRide; a.Timer = bp.Id == Catalog.Jetty ? a.Rng.Range(70f, 110f) : a.Rng.Range(9f, 15f);   // a boat may be a while coming
                    a.Aboard = a.Landed = false;
                    if (bp.Gondolas > 0 || bp.Id == Catalog.Balloon) Hide(a, true);   // up in a cabin, or the basket
                    StepRide(a);
                    return;
            }
            a.Position = new Vector2(pos.x, pos.z);
            a.Heading = yaw;
            a.SpotLift = sp.At.y;
            (a.State, a.Timer) = sp.Kind switch
            {
                SpotKind.Sit => (Activity.Sitting, a.Rng.Range(10f, 22f)),          // people linger: a village, not a rush hour
                SpotKind.Gather => (Activity.Gathering, a.Rng.Range(8f, 18f)),
                SpotKind.Play => (Activity.Playing, a.Rng.Range(6f, 14f)),
                SpotKind.Watch => (Activity.Watching, a.Rng.Range(6f, 12f) + (sp.At.y > 0f ? 5f : 0f)),
                _ => (Activity.Browsing, a.Rng.Range(5f, 10f)),
            };
            if (plan.Planned) a.Timer *= 1.8f;   // where they meant to spend the morning (afternoon...): they take their time
        }

        /// <summary>Nowhere to go for the night: sit down where they are (on a bench or by the fire if that is the plan).</summary>
        private void SettleOutdoors(Agent a)
        {
            a.State = Activity.Sleeping; a.Approach = a.Position;
            Pose(a);
        }

        private void EndActivity(Agent a)
        {
            if (a.Hidden) { Hide(a, false); a.Position = a.Approach; a.Height = GroundAt(a.Position); a.Heading = a.SpotYaw + 180f; }
            else if (a.State == Activity.Sitting || a.State == Activity.Playing || a.SpotLift > 0f) a.Position = a.Approach;   // step back out
            Satisfy(a);
            Unclaim(a);
            a.SpotHouse = -1; a.SpotIndex = -1; a.SpotLift = 0f;
            a.Plan = Intent.Nothing;
            a.State = Activity.Idle; a.Timer = a.Rng.Range(0.5f, 2f);
            Pose(a);
        }

        /// <summary>On a carousel horse (riding round with it) or up in a Ferris wheel cabin, until the ride is over.</summary>
        // ------------------------------------------------------------------ boat trips

        /// <summary>The seat of whoever is aboard a boat from (landing stage, spot), or null (set by the world: BoatSystem).</summary>
        public Func<int, int, Transform> BoatSeat;

        /// <summary>Someone stands at (landing stage, spot) waiting for a boat.</summary>
        public bool WaitingForBoat(int house, int spot)
        {
            foreach (var a in agents) if (a.SpotHouse == house && a.SpotIndex == spot && a.State == Activity.OnRide && !a.Aboard && !a.Landed) return true;
            return false;
        }

        /// <summary>The boat is back: whoever sat at (landing stage, spot) steps off.</summary>
        public void EndBoatTrip(int house, int spot)
        {
            foreach (var a in agents) if (a.SpotHouse == house && a.SpotIndex == spot && a.State == Activity.OnRide) a.Landed = true;
        }

        /// <summary>
        /// A boat trip: waiting on the landing stage (looking out over the water) until a boat takes them aboard, then
        /// sitting in it round the lake; stepping off when it is back, or going home if no boat comes in time.
        /// </summary>
        private void StepBoatRide(Agent a)
        {
            var seat = a.Landed ? null : BoatSeat?.Invoke(a.SpotHouse, a.SpotIndex);
            if (seat != null)
            {
                a.Aboard = true;
                if (a.Umbrella != null && a.Umbrella.gameObject.activeSelf) a.Umbrella.gameObject.SetActive(false);
                a.Root.position = seat.position;
                a.Root.rotation = seat.rotation;
                var local = transform.InverseTransformPoint(seat.position);
                a.Position = new Vector2(local.x, local.z);
                PoseLimbs(a, false, 0f);
                return;
            }
            var pos = world.Buildings.SpotPosition(a.SpotHouse, a.SpotIndex, out float yaw);
            if (a.Landed || (a.Aboard && seat == null) || a.Timer <= 0f)
            {
                // Off the boat (or tired of waiting): back on the landing stage, and on with the day.
                a.Aboard = a.Landed = false;
                a.Position = a.Approach; a.Height = GroundAt(a.Approach);
                Satisfy(a);
                Unclaim(a);
                a.SpotHouse = -1; a.SpotIndex = -1; a.Plan = Intent.Nothing;
                a.State = Activity.Idle; a.Timer = a.Rng.Range(0.5f, 2f);
                Pose(a);
                return;
            }
            // Waiting: standing on the stage, facing the water.
            a.Root.localPosition = pos;
            a.Root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            a.LegL.localRotation = a.LegR.localRotation = Quaternion.identity;
            a.ArmL.localRotation = Quaternion.Euler(0f, 0f, -6f); a.ArmR.localRotation = Quaternion.Euler(0f, 0f, 6f);
            HoldUmbrella(a, true);
        }

        private void StepRide(Agent a)
        {
            if (SpotStillThere(a) && world.Buildings.Houses[a.SpotHouse].variant == Catalog.Jetty) { StepBoatRide(a); return; }
            var rotor = SpotStillThere(a) ? world.Buildings.RotorOf(a.SpotHouse) : null;
            if (rotor == null || a.Timer <= 0f)
            {
                if (a.Hidden) Hide(a, false);
                a.Position = a.Approach; a.Height = GroundAt(a.Approach);
                Satisfy(a);
                Unclaim(a);
                a.SpotHouse = -1; a.SpotIndex = -1; a.Plan = Intent.Nothing;
                a.State = Activity.Idle; a.Timer = a.Rng.Range(0.5f, 2f);
                Pose(a);
                return;
            }
            if (a.Hidden) return;
            var bp = Catalog.Get(world.Buildings.Houses[a.SpotHouse].variant);
            var sp = bp.Spots[a.SpotIndex];
            var seat = rotor.TransformPoint(sp.At - bp.RotorPivot + Vector3.up * 0.003f);
            a.Root.position = seat;
            a.Root.rotation = rotor.rotation * Quaternion.Euler(0f, sp.Facing, 0f);
            var local = transform.InverseTransformPoint(seat);
            a.Position = new Vector2(local.x, local.z);
            PoseLimbs(a, false, 0f);
        }

        /// <summary>Just in front of a house's door (the front, -Z, of every variant).</summary>
        public static Vector2 DoorStep(HouseInstance h)
        {
            float doorX = h.variant == 0 ? -0.022f : 0f;
            var d = Quaternion.Euler(0f, h.rotation, 0f) * new Vector3(doorX, 0f, -(HouseFactory.HalfExtents(h.variant).y * 0.86f + Radius + 0.008f));
            return new Vector2(h.x + d.x, h.z + d.z);
        }

        private bool StartHike(Agent a)
        {
            var route = world.Paths.Hike(a.Home, 0.45f, ref a.Rng);
            if (route == null) return false;
            var toStart = Grid.Route(a.Position, route[0]);
            var all = new List<Vector2>();
            if (toStart != null) all.AddRange(toStart);
            all.AddRange(route);
            foreach (var p in Soften(a.Position, all)) a.Waypoints.Enqueue(p);
            a.Hover = 0f; a.Closest = float.MaxValue;
            a.Hiking = true;
            a.Target = route[^1]; a.ToStation = false;
            a.State = Activity.Walking; a.Timer = 20f + (route.Count + a.Waypoints.Count) * 0.03f / Speed;
            return true;
        }

        private void GoTo(Agent a, Vector2 target, bool station)
        {
            a.Target = target; a.ToStation = station;
            a.State = Activity.Walking; a.Timer = 25f;
            a.Progress = a.Position; a.ProgressTimer = 3f;
            a.Waypoints.Clear(); a.Hover = 0f; a.Closest = float.MaxValue;
            // Round fences and ponds, over the level crossing, along the roads and paths.
            var route = Grid.Ready ? Grid.Route(a.Position, target) : world.Paths.Route(a.Position, target);
            if (route == null) return;
            float length = 0f; var from = a.Position;
            foreach (var p in Soften(a.Position, route)) { a.Waypoints.Enqueue(p); length += (p - from).magnitude; from = p; }
            a.Timer = 12f + length / Speed * 1.6f;
        }

        /// <summary>A corner that can be rounded: it turns less than 60 degrees and the way straight on to the next waypoint is clear.</summary>
        private bool Gentle(Agent a, Vector2 w0, Vector2 w1)
        {
            var din = w0 - a.Position; var dout = w1 - w0;
            if (din.sqrMagnitude < 1e-8f || dout.sqrMagnitude < 1e-8f) return true;
            if (Vector2.Dot(din.normalized, dout.normalized) < 0.5f) return false;
            return Walkable(Vector2.Lerp(a.Position, w1, 0.5f)) && !world.Paths.CrossesBarrier(a.Position, w1);
        }

        /// <summary>
        /// A route's corners softened (one pass of Chaikin's corner cutting): people walk curves, not zigzags. A cut is
        /// only taken where it stays on walkable ground and doesn't cross a fence or hedge; the end stays exactly where it was.
        /// </summary>
        private List<Vector2> Soften(Vector2 start, IList<Vector2> route)
        {
            if ((LegacyParts & 1) != 0) return new List<Vector2>(route);
            var pts = new List<Vector2>(route.Count + 1) { start };
            pts.AddRange(route);
            if (pts.Count < 3) return new List<Vector2>(route);
            var soft = new List<Vector2>(pts.Count * 2);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector2 prev = pts[i - 1], at = pts[i], next = pts[i + 1];
                var q = Vector2.Lerp(at, prev, 0.3f); var r = Vector2.Lerp(at, next, 0.3f);
                bool ok = Walkable(q) && Walkable(r) && !world.Paths.CrossesBarrier(q, r) && (soft.Count == 0 || !world.Paths.CrossesBarrier(soft[^1], q));
                if (ok) { soft.Add(q); soft.Add(r); } else soft.Add(at);
            }
            soft.Add(pts[^1]);
            return soft;
        }

        private void Walk(Agent a, float dt)
        {
            if (a.SpotHouse >= 0 && !SpotStillThere(a)) { Stop(a, 0.5f); return; }   // where they were going is gone
            // Not getting anywhere (circling a corner, steering against something): find the way again, then give up.
            a.ProgressTimer -= dt;
            if (a.ProgressTimer <= 0f && !a.Hiking)
            {
                bool moved = (a.Position - a.Progress).magnitude > 0.012f;
                a.Stuck = moved ? 0 : a.Stuck + 1;
                if (a.Stuck == 1) { var leg = a.Leg; bool st = a.ToStation; GoTo(a, a.Target, st); a.Leg = leg; a.Stuck = 1; return; }
                if (a.Stuck >= 2) { a.Stuck = 0; if (PlaceAt(a.SpotHouse) is { } missed) Avoid(a, missed.Key, 3f); Stop(a, a.Rng.Range(0.5f, 1.5f)); return; }
                a.Progress = a.Position; a.ProgressTimer = 3f;
            }
            // A waypoint is reached when near it - or when already past it, heading for the next (no turning round to
            // step back onto it).
            while (a.Waypoints.Count > 0)
            {
                var w0 = a.Waypoints.Peek();
                float d0 = (w0 - a.Position).magnitude;
                if (d0 < 0.01f) { a.Waypoints.Dequeue(); a.Hover = 0f; a.Closest = float.MaxValue; continue; }
                // Circling a waypoint that can't quite be reached (just inside a hedge, on a stream bank): no closer for a
                // second and a half - on to the next.
                if (d0 < a.Closest - 0.005f) { a.Closest = d0; a.Hover = 0f; } else a.Hover += dt;
                if (a.Hover > 1.5f && a.Waypoints.Count > 1) { a.Waypoints.Dequeue(); a.Hover = 0f; a.Closest = float.MaxValue; continue; }
                if (a.Waypoints.Count > 1 && d0 < 0.03f && (LegacyParts & 2) == 0)
                {
                    var w1 = a.Waypoints.ElementAt(1);
                    if (Vector2.Dot(w1 - w0, a.Position - w0) > 0f && Gentle(a, w0, w1)) { a.Waypoints.Dequeue(); continue; }
                }
                break;
            }
            if (a.Waypoints.Count > 1 && a.Timer > 0f)
            {
                // Round a gentle corner: closing on this waypoint, aim more and more at the next one. (A sharp turn - round
                // the end of a fence, through a gate - is walked to the point first.)
                var w0 = a.Waypoints.Peek();
                var w1 = a.Waypoints.ElementAt(1);
                float d0 = (w0 - a.Position).magnitude;
                const float lead = 0.025f;
                Steer(a, d0 < lead && (LegacyParts & 2) == 0 && Gentle(a, w0, w1) ? Vector2.Lerp(w1, w0, d0 / lead) : w0, dt);
                return;
            }
            if (a.Waypoints.Count == 1) a.Waypoints.Clear();   // the last waypoint is the target itself
            var to = a.Target - a.Position;
            if (to.magnitude < 0.004f || a.Timer <= 0f)
            {
                bool there = to.magnitude < 0.02f;
                a.Waypoints.Clear();
                if (!there) { a.Hiking = false; if (PlaceAt(a.SpotHouse) is { } missed) Avoid(a, missed.Key, 3f); Stop(a); return; }   // couldn't get there: not again for a while
                if (a.ToStation) { a.ToStation = false; a.State = Activity.Waiting; a.Timer = a.Rng.Range(35f, 55f); return; }
                if (a.Hiking) { a.Hiking = false; Satisfy(a, 0.2f); }
                switch (a.Plan.What)
                {
                    case Intent.Kind.Spot: case Intent.Kind.Home: case Intent.Kind.Sleep: case Intent.Kind.MoveIn: case Intent.Kind.Neighbour:
                    case Intent.Kind.Work: case Intent.Kind.Haul: case Intent.Kind.Gather:
                        Arrive(a); return;
                    case Intent.Kind.Stroll: Satisfy(a, 0.5f); break;
                }
                bool wandered = a.Plan.What == Intent.Kind.Wander;
                a.Plan = Intent.Nothing;
                a.State = Activity.Idle; a.Timer = wandered ? a.Rng.Range(10f, 25f) : a.Rng.Range(2f, 6f);   // someone with no home stays a while in their patch
                return;
            }
            Steer(a, a.Target, dt);
        }

        private void Steer(Agent a, Vector2 goal, float dt)
        {
            var to = goal - a.Position;
            float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            // Steer around obstacles: the first clear heading nearest the goal, looking a little ahead.
            float pace = a.Plan.What == Intent.Kind.Sleep ? 1.35f : a.Hiking ? 1.1f : 1f;   // hurrying home to bed
            float step = Mathf.Min(Speed * pace * dt, to.magnitude);
            float chosen = float.NaN;
            // Smart: the way round obstacles is looked for every third frame (the step itself is always checked below).
            if (PerfSettings.SmartVillagers && ((tick + a.Index) % 3) != 0 && !float.IsNaN(a.Chosen) && Mathf.Abs(Mathf.DeltaAngle(want, a.ChosenWant)) < 12f)
                chosen = a.Chosen + Mathf.DeltaAngle(a.ChosenWant, want);
            else
            {
                // First a way clear of other people and vehicles too; failing that, a way past them (then wait a moment
                // for them instead of walking through).
                bool crowded = false;
                for (int pass = 0; pass < 2 && float.IsNaN(chosen); pass++)
                    foreach (float off in Offsets)
                    {
                        float h = want + off;
                        var dir = new Vector2(Mathf.Sin(h * Mathf.Deg2Rad), Mathf.Cos(h * Mathf.Deg2Rad));
                        var near = a.Position + dir * Mathf.Min(0.012f, to.magnitude);
                        if (!Walkable(near) || !Walkable(a.Position + dir * step)) continue;
                        if (pass == 0 && Occupied(a, near)) continue;
                        if (pass == 1 && world.Traffic.Blocks(near, 0.004f, true)) continue;   // (never into a car going by)
                        chosen = h; crowded = pass == 1 && Occupied(a, near, true);   // past a parked car: walk on; into someone: wait
                        break;
                    }
                a.Chosen = crowded ? float.NaN : chosen; a.ChosenWant = want;
                if (crowded) { a.LastSteer = "waiting for someone"; a.Heading = Mathf.MoveTowardsAngle(a.Heading, want, 300f * dt); return; }   // someone in the way: wait for them
            }
            if (float.IsNaN(chosen)) { a.Timer = 0f; a.Waypoints.Clear(); return; }
            if (Diagnose) a.LastSteer = $"off {Mathf.DeltaAngle(want, chosen):0}, to goal {to.magnitude * 100f:0.0} cm";
            a.Heading = Mathf.MoveTowardsAngle(a.Heading, chosen, 300f * dt);
            if (Mathf.Abs(Mathf.DeltaAngle(a.Heading, chosen)) > 50f) return;   // turn first
            var fwd = new Vector2(Mathf.Sin(a.Heading * Mathf.Deg2Rad), Mathf.Cos(a.Heading * Mathf.Deg2Rad));
            var next = a.Position + fwd * step;
            if (Occupied(a, next, true) || world.Traffic.Blocks(next, 0.002f, true)) { a.Chosen = float.NaN; return; }   // (someone - or a car - stepped in the way)
            if (!Walkable(next)) { a.Heading = chosen; a.Chosen = float.NaN; return; }
            a.Position = next;
            a.Phase += step / 0.0105f * Mathf.PI;   // one stride per ~1 cm
        }

        private static readonly float[] Offsets = { 0f, 30f, -30f, 60f, -60f, 95f, -95f, 135f, -135f };

        /// <summary>Diagnostics for the tests: count spins (turning three quarters round within about a second).</summary>
        public static bool Diagnose;
        /// <summary>Diagnostics: the old walking, part by part (1 straight routes, 2 corners reached exactly, 4 walking through people).</summary>
        public static int LegacyParts;
        public static bool Legacy { get => LegacyParts == 7; set => LegacyParts = value ? 7 : 0; }
        public int Spins { get; set; }
        public readonly List<string> SpinNotes = new();

        /// <summary>Now: pairs of people (not sitting or working together) closer than 5 mm, and people inside a car's body.</summary>
        public (int touching, int inCars) Crowding()
        {
            int touching = 0, inCars = 0;
            var seen = new List<Agent>();
            foreach (var a in agents)
            {
                if (a.Hidden || a.State is Activity.Riding or Activity.OnRide or Activity.Sleeping) continue;
                if (world.Traffic.Blocks(a.Position, -0.002f)) inCars++;
                if (a.State != Activity.Walking) continue;
                foreach (var b in seen) if ((a.Position - b.Position).sqrMagnitude < 0.005f * 0.005f) touching++;
                seen.Add(a);
            }
            return (touching, inCars);
        }

        // Who stands where (a 2 cm grid, rebuilt every frame), for keeping out of each other's way.
        private readonly Dictionary<long, List<Agent>> crowd = new();
        private static long CrowdKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        private void MapCrowd()
        {
            foreach (var l in crowd.Values) l.Clear();
            foreach (var a in agents)
            {
                if (a.Hidden || a.State is Activity.Riding or Activity.OnRide or Activity.Sleeping) continue;
                long k = CrowdKey(Mathf.FloorToInt(a.Position.x / 0.02f), Mathf.FloorToInt(a.Position.y / 0.02f));
                if (!crowd.TryGetValue(k, out var l)) crowd[k] = l = new List<Agent>(4);
                l.Add(a);
            }
        }

        /// <summary>Whether someone else (or a vehicle) is where <paramref name="a"/> would step - personal space, a car's body.</summary>
        private bool Occupied(Agent a, Vector2 p, bool peopleOnly = false)
        {
            if ((LegacyParts & 4) != 0) return false;
            // (the last few centimetres to a door, a platform, a seat on a busy terrace: people gather there - no waiting for room)
            // (...and the first steps out of a door: nobody waits on a doorstep, blocking it)
            if ((a.Target - a.Position).sqrMagnitude < 0.05f * 0.05f || (a.Approach - a.Position).sqrMagnitude < 0.025f * 0.025f) return !peopleOnly && world.Traffic.Blocks(p, 0.004f, true);
            const float room = 0.0075f;   // (shoulder to shoulder, a little apart)
            int cx = Mathf.FloorToInt(p.x / 0.02f), cz = Mathf.FloorToInt(p.y / 0.02f);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!crowd.TryGetValue(CrowdKey(cx + dx, cz + dz), out var l)) continue;
                foreach (var b in l)
                    if (b != a && (b.Position - p).sqrMagnitude < room * room && (b.Position - p).sqrMagnitude < (b.Position - a.Position).sqrMagnitude) return true;   // (only what is nearer than they are now: never stuck in a crowd they're already in)
            }
            return !peopleOnly && world.Traffic.Blocks(p, 0.004f);
        }

        /// <summary>On the train: get off at the station they were going to (or, on a trip for its own sake, the next one).</summary>
        private void StepRiding(Agent a, float dt)
        {
            a.Ride += dt;
            var train = a.Riding;
            if (train == null || !world.Trains.Trains.Contains(train) || !train.Running && train.Dwell <= 0f && a.Ride > 20f)
            {
                // The train went (or stopped for good): step off by the line nearest them, or at home.
                Hide(a, false);
                var at = train != null && world.Trains.Trains.Contains(train) ? world.Trains.CentrePosition(train) : new Vector3(a.Home.x, 0f, a.Home.y);
                a.Position = FindNear(new Vector2(at.x, at.z), 0.15f, out var near) ? near : a.Home; a.Height = GroundAt(a.Position);
                a.Riding = null;
                Stop(a, 1f);
                return;
            }
            int st = train.AtStation;
            if (st < 0 || a.Ride < 4f) return;
            bool mine = a.TripAnywhere ? stations[st].House != a.TripFrom || a.Ride > 40f : stations[st].House == a.TripTo;
            if (!mine && !a.TripAnywhere && a.Ride < 150f && st != a.LastCall)
            {
                a.LastCall = st;
                int to = StationIndex(a.TripTo);
                if (to >= 0 && !world.Trains.CallsAt(train, to, 4))
                {
                    // This train won't get there now (a switch sends it elsewhere): change here and wait for one that will.
                    Alight(a, stations[st], true);
                    return;
                }
            }
            if (!mine && a.Ride < 150f) return;   // stay on (after a long while, give up and get off anywhere)
            Alight(a, stations[st]);
        }

        /// <summary>Whether a waiting passenger takes this train: it calls at their station soon (any train, for a ride).</summary>
        private bool Bound(Agent a, Train t)
        {
            if (a.TripAnywhere || a.Plan.What == Intent.Kind.None) return true;
            int to = StationIndex(a.TripTo);
            return to < 0 || world.Trains.CallsAt(t, to, 3);
        }

        private int StationIndex(Vector2 house)
        {
            for (int i = 0; i < stations.Count; i++) if (stations[i].House == house) return i;
            return -1;
        }

        private void Alight(Agent a, StationStop st, bool change = false)
        {
            var spot = PlatformSpot(st, a.Rng.Range(-0.12f, 0.12f));
            for (int k = 0; k < 6 && !Walkable(spot); k++) spot = PlatformSpot(st, a.Rng.Range(-0.12f, 0.12f));
            a.Position = spot; a.Height = GroundAt(spot);
            Hide(a, false);
            a.Riding = null; a.Cooldown = 6f;
            if (change) { a.Changing = true; a.State = Activity.Waiting; a.Timer = a.Rng.Range(35f, 55f); a.TripFrom = st.House; return; }
            if (a.TripAnywhere) { Satisfy(a, 0.3f); Stop(a, a.Rng.Range(1f, 2f)); return; }
            // On to where they were going, if it can be walked to from here.
            var plan = a.Plan;
            if (plan.What != Intent.Kind.None && Grid.Reachable(a.Position, plan.Target)) { a.Leg = Leg.Final; GoTo(a, plan.Target, false); return; }
            Stop(a, a.Rng.Range(1f, 2f));
        }

        // ------------------------------------------------------------------ poses

        private void Pose(Agent a)
        {
            if (a.State == Activity.OnRide || a.Hidden) return;
            bool walking = a.State == Activity.Walking || a.Running;
            float swing = walking ? Mathf.Sin(a.Phase) : 0f;
            float bob = walking ? Mathf.Abs(Mathf.Cos(a.Phase)) * 0.0006f : 0f;
            if (a.State == Activity.Playing && !a.Running && Pitch(a) == null) bob = Mathf.Abs(Mathf.Sin(clock * 9f + a.Home.x * 31f)) * 0.004f;
            if (a.State == Activity.Sitting || a.State == Activity.Sleeping || a.State == Activity.Unwell) bob = -0.0055f * a.Root.localScale.y;
            a.Root.localPosition = new Vector3(a.Position.x, a.Height + bob, a.Position.y);
            float lean = a.State == Activity.Sleeping ? 12f : a.State == Activity.Unwell ? -14f : a.State == Activity.Working && a.Tending ? 24f + Mathf.Sin(clock * 2.2f + a.Home.x * 50f) * 8f : 0f;   // hoeing
            a.Root.localRotation = Quaternion.Euler(lean, a.Heading, walking ? swing * 3f : 0f);
            PoseLimbs(a, walking, swing);
            HoldUmbrella(a);
        }

        // ------------------------------------------------------------------ umbrellas

        private static readonly Mesh[] umbrellas = new Mesh[5];

        /// <summary>
        /// Out in the rain (walking, waiting, watching, chatting...), up goes the umbrella: each villager opens theirs at
        /// a slightly different moment as a shower sets in, and puts it away when it stops or they go indoors. Five
        /// cheerful colours; made the first time it rains on someone, so dry worlds carry none.
        /// </summary>
        /// <summary>Under a roof already: on a station platform (its canopy), or at a spot within a building's walls or porch.</summary>
        private bool Sheltered(Agent a)
        {
            if (a.State == Activity.Waiting || OnPlatform(a.Position, out _)) return true;
            if (a.SpotHouse < 0 || a.SpotHouse >= world.Buildings.Houses.Count) return false;
            var h = world.Buildings.Houses[a.SpotHouse];
            var half = Catalog.Get(h.variant).Half;
            var q = Quaternion.Inverse(Quaternion.Euler(0f, h.rotation, 0f)) * new Vector3(a.Position.x - h.x, 0f, a.Position.y - h.z);
            return Mathf.Abs(q.x) < half.x + 0.004f && Mathf.Abs(q.z) < half.y + 0.004f;
        }

        private void HoldUmbrella(Agent a, bool queueing = false)
        {
            bool outdoors = queueing || a.State is Activity.Walking or Activity.Waiting or Activity.Watching or Activity.Chatting or Activity.Gathering or Activity.Idle or Activity.Waving;
            float threshold = 0.3f + ((uint)a.Seed % 9u) * 0.05f;   // not all at once
            bool up = outdoors && a.Carrying == 0 && !a.Cheering && Rain > threshold && !Sheltered(a);
            if (up && a.Umbrella == null)
            {
                int colour = (int)((uint)a.Seed / 7u % (uint)umbrellas.Length);
                a.Umbrella = Part("Umbrella", UmbrellaMesh(colour), a.Root);
                a.Umbrella.localPosition = new Vector3(PersonFactory.ShoulderX * 0.9f, 0.032f, 0.005f);   // just over the head, held in front (a villager faces +Z)
                a.Umbrella.localRotation = Quaternion.Euler(-10f, 0f, -4f);
            }
            if (a.Umbrella == null) return;
            if (a.Umbrella.gameObject.activeSelf != up) a.Umbrella.gameObject.SetActive(up);
            if (up) a.ArmR.localRotation = Quaternion.Euler(-62f, 0f, 6f);   // holding it up in front
        }

        private static Mesh UmbrellaMesh(int colour)
        {
            if (umbrellas[colour] != null) return umbrellas[colour];
            var hues = new[] { Pal.Hex("#E2453C"), Pal.Hex("#F2B33D"), Pal.Hex("#3C7FD8"), Pal.Hex("#4FB36A"), Pal.Hex("#9A5BC9") };
            var c = hues[colour];
            var under = Color.Lerp(c, Color.black, 0.35f);
            var b = new MeshBuilder();
            b.AddCylinder(new Vector3(0f, 0f, 0f), 0.0112f, 0.0007f, 0.0048f, 8, under, c, true, true, false);   // the canopy (closed underneath: seen from below too)
            b.AddCylinder(new Vector3(0f, -0.0175f, 0f), 0.0004f, 0.0004f, 0.0215f, 4, Pal.Hex("#3A2A20"), Pal.Hex("#3A2A20"), false, false, false);   // the stick
            b.AddCylinder(new Vector3(0f, 0.0048f, 0f), 0.0005f, 0.0002f, 0.0013f, 4, Pal.Hex("#3A2A20"), Pal.Hex("#3A2A20"), false, false, false);   // the tip
            return umbrellas[colour] = b.ToMesh("Umbrella" + colour);
        }

        private void PoseLimbs(Agent a, bool walking, float swing)
        {
            float stride = a.Hiking ? 36f : 30f;
            bool seated = a.State == Activity.Sitting || a.State == Activity.OnRide || a.State == Activity.Sleeping || a.State == Activity.Unwell;
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
                case Activity.Sleeping:
                case Activity.Unwell:
                    a.ArmL.localRotation = Quaternion.Euler(-30f, 0f, -14f);
                    a.ArmR.localRotation = Quaternion.Euler(-30f, 0f, 14f);
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
                case Activity.Working when a.Tending:
                    a.ArmL.localRotation = Quaternion.Euler(-50f + Mathf.Sin(t * 2.2f) * 25f, 0f, -6f);
                    a.ArmR.localRotation = Quaternion.Euler(-50f + Mathf.Sin(t * 2.2f) * 25f, 0f, 6f);
                    return;
                case Activity.Working:
                    // Minding the shop: hands behind the back, a wave to passers-by now and then.
                    a.ArmL.localRotation = Quaternion.Euler(25f, 0f, -12f);
                    a.ArmR.localRotation = Mathf.Repeat(t * 0.12f, 1f) > 0.85f ? Quaternion.Euler(0f, 0f, 130f + Mathf.Sin(t * 9f) * 18f) : Quaternion.Euler(25f, 0f, 12f);
                    return;
                case Activity.Chatting:
                case Activity.Gathering:
                    a.ArmL.localRotation = Quaternion.Euler(Mathf.Repeat(t * 0.4f, 1f) > 0.5f ? -30f + Mathf.Sin(t * 3.1f) * 12f : 0f, 0f, -8f);
                    a.ArmR.localRotation = Quaternion.Euler(-35f + Mathf.Sin(t * 3f) * 15f, 0f, 10f);   // gesturing
                    return;
                default:
                    if (a.Carrying > 0)
                    {
                        a.ArmL.localRotation = Quaternion.Euler(-72f, 0f, 12f);   // a crate held in front
                        a.ArmR.localRotation = Quaternion.Euler(-72f, 0f, -12f);
                        return;
                    }
                    a.ArmL.localRotation = Quaternion.Euler(-swing * 26f, 0f, -6f);
                    a.ArmR.localRotation = Quaternion.Euler(a.Cheering ? 0f : swing * 26f, 0f, a.Cheering ? 145f + Mathf.Sin(clock * 11f) * 22f : 6f);
                    return;
            }
        }
    }
}
