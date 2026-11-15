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
        private readonly List<Stop> stations = new();
        private WonderlandWorld world;
        private Material material;
        private float clock, stationsAt = -1f;

        public enum Activity { Idle, Walking, Chatting, Waiting, Riding, Waving }

        private sealed class Agent
        {
            public Transform Root, LegL, LegR, ArmL, ArmR;
            public int Index;
            public Vector2 Home, Position, Target;
            public float Heading, Timer, Phase, Height, Cooldown, Ride, WaveHeading, ResumeTimer;
            public Activity State, Resume;
            public bool ToStation, Cheering;
            public Rng Rng;
        }

        private struct Stop { public float S; public int Side; }

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
        public Activity StateOf(int i) => agents[i].State;
        public bool WavingNow(int i) => agents[i].State == Activity.Waving || agents[i].Cheering;
        public float HeightOf(int i) => agents[i].Height;

        // ------------------------------------------------------------------ where people may walk

        /// <summary>Dry land off the rails and out of houses - or a station platform.</summary>
        public bool Walkable(Vector2 p) => OnPlatform(p, out _) || WalkableGround(p);

        private bool WalkableGround(Vector2 p)
        {
            var t = world.Terrain;
            if (t.EdgeAt(p.x, p.y) < 0.4f || t.WaterAt(p.x, p.y) > 0.05f) return false;
            if (world.Streams.IsWet(p, Radius + 0.002f)) return false;
            if (world.Track.HasTrack && world.Track.DistanceTo(new Vector3(p.x, 0f, p.y)) < TrackSystem.BedHalfWidth + Radius) return false;
            return !InsideHouse(p, Radius);
        }

        /// <summary>Inside a house's walls (an oriented box a little inside the eaves), so doorsteps stay reachable.</summary>
        public bool InsideHouse(Vector2 p, float margin)
        {
            foreach (var h in world.Buildings.Houses)
            {
                var half = HouseFactory.HalfExtents(h.variant) * 0.86f + Vector2.one * margin;
                var d = Quaternion.Euler(0f, -h.rotation, 0f) * new Vector3(p.x - h.x, 0f, p.y - h.z);
                if (Mathf.Abs(d.x) < half.x && Mathf.Abs(d.z) < half.y) return true;
            }
            return false;
        }

        private void RefreshStations()
        {
            if (Mathf.Abs(clock - stationsAt) < 0.5f && stationsAt >= 0f) return;
            stationsAt = clock;
            stations.Clear();
            if (!world.Track.HasTrack) return;
            var layout = world.Track.Layout;
            foreach (var h in world.Buildings.Houses)
            {
                if (h.variant != HouseFactory.Station) continue;
                var c = new Vector3(h.x, 0f, h.z);
                float s = layout.Nearest(c, out _);
                var p = layout.Evaluate(s, out var tangent); tangent.y = 0f; tangent.Normalize();
                stations.Add(new Stop { S = s, Side = Vector3.Dot(Vector3.Cross(Vector3.up, tangent), c - p) >= 0f ? 1 : -1 });
            }
        }

        /// <summary>Whether a point is on a platform's paving, with the paving's height.</summary>
        public bool OnPlatform(Vector2 p, out float top)
        {
            top = 0f;
            RefreshStations();
            if (stations.Count == 0) return false;
            var layout = world.Track.Layout;
            var q = new Vector3(p.x, 0f, p.y);
            float s = layout.Nearest(q, out float d);
            if (d < 0.046f || d > 0.076f) return false;   // keep back from the edge, where the coaches overhang
            var pt = layout.Evaluate(s, out var tangent); tangent.y = 0f; tangent.Normalize();
            int side = Vector3.Dot(Vector3.Cross(Vector3.up, tangent), q - pt) >= 0f ? 1 : -1;
            foreach (var st in stations)
            {
                if (st.Side != side) continue;
                float along = Mathf.Abs(s - st.S);
                if (layout.Closed) along = Mathf.Min(along, layout.Length - along);
                if (along > WonderlandWorld.PlatformLength / 2f - 0.012f) continue;
                top = pt.y + 0.0135f;
                return true;
            }
            return false;
        }

        private Vector2 PlatformSpot(Stop st, float offset)
        {
            var layout = world.Track.Layout;
            var p = layout.Evaluate(layout.Closed ? layout.Wrap(st.S + offset) : Mathf.Clamp(st.S + offset, 0f, layout.Length), out var tangent);
            tangent.y = 0f; tangent.Normalize();
            var q = p + Vector3.Cross(Vector3.up, tangent) * st.Side * 0.052f;
            return new Vector2(q.x, q.z);
        }

        private float GroundAt(Vector2 p) => OnPlatform(p, out float top) ? top : world.Terrain.SurfaceAt(p.x, p.y);

        private bool TrainRunning => world.Train.Placed && world.Train.Running;

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
                if (!Walkable(a.Position))   // the world changed under them
                {
                    if (FindNear(a.Position, 0.06f, out var near)) a.Position = near; else a.Position = a.Home;
                    a.State = Activity.Idle; a.Timer = 1f;
                }
                a.Cheering = a.State == Activity.Waiting && TrainPassing(a, out _);   // waiting passengers wave on the spot
                if (a.State != Activity.Waving && a.State != Activity.Waiting && a.Cooldown <= 0f && TrainPassing(a, out var toTrain))
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
                        if (TrainRunning && world.Train.Dwell > 0f && world.Train.Near(new Vector3(a.Position.x, 0f, a.Position.y), 0.17f))
                        { a.State = Activity.Riding; a.Ride = 0f; a.Root.gameObject.SetActive(false); }
                        else if (a.Timer <= 0f || !TrainRunning) { a.ToStation = false; Decide(a); }
                        break;
                    case Activity.Waving:
                        a.Heading = Mathf.MoveTowardsAngle(a.Heading, a.WaveHeading, 240f * dt);
                        if (TrainPassing(a, out var t)) a.WaveHeading = Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg;
                        if (a.Timer <= 0f) { a.State = a.Resume; a.Timer = Mathf.Max(a.ResumeTimer, 0.5f); }
                        break;
                }
                a.Height = Mathf.MoveTowards(a.Height, GroundAt(a.Position), 0.12f * dt);
                Pose(a);
            }
        }

        private bool TrainPassing(Agent a, out Vector2 toTrain)
        {
            toTrain = Vector2.zero;
            if (!TrainRunning || world.Train.Speed < 0.03f) return false;
            var loco = world.Train.LocoPosition;
            if (world.Train.InTunnel != null && world.Train.InTunnel(world.Train.S)) return false;
            toTrain = new Vector2(loco.x, loco.z) - a.Position;
            return toTrain.magnitude < 0.24f;
        }

        private float FacingTrack(Agent a)
        {
            if (!world.Track.HasTrack) return a.Heading;
            var layout = world.Track.Layout;
            var p = layout.Evaluate(layout.Nearest(new Vector3(a.Position.x, 0f, a.Position.y), out _), out _);
            return Mathf.Atan2(p.x - a.Position.x, p.z - a.Position.y) * Mathf.Rad2Deg;
        }

        /// <summary>Picks what to do next: the station, a neighbour's door, or a quiet spot near home.</summary>
        private void Decide(Agent a)
        {
            RefreshStations();
            float roll = a.Rng.Value;
            if (TrainRunning && stations.Count > 0 && roll < 0.3f)
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
            if (roll < 0.7f)
            {
                var houses = world.Buildings.Houses;
                for (int k = 0; k < 4 && houses.Count > 0; k++)
                {
                    var h = houses[a.Rng.Range(0, houses.Count)];
                    if (h.variant == HouseFactory.Station || (new Vector2(h.x, h.z) - a.Home).magnitude > 0.8f) continue;
                    var door = DoorStep(h);
                    if (Walkable(door)) { GoTo(a, door, false); return; }
                }
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
        }

        private void Walk(Agent a, float dt)
        {
            var to = a.Target - a.Position;
            if (to.magnitude < 0.004f || a.Timer <= 0f)
            {
                bool there = to.magnitude < 0.02f;
                if (a.ToStation && there) { a.State = Activity.Waiting; a.Timer = a.Rng.Range(30f, 50f); }
                else if (there && InsideHouseNear(a.Target)) { a.State = Activity.Chatting; a.Timer = a.Rng.Range(3f, 7f); a.Heading = DoorFacing(a.Target); }
                else { a.State = Activity.Idle; a.Timer = a.Rng.Range(2f, 6f); }
                a.ToStation &= there;
                return;
            }
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
            if (float.IsNaN(chosen)) { a.Timer = 0f; return; }
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
            if (!TrainRunning)
            {
                a.Position = a.Home; a.Height = GroundAt(a.Home);
                Alight(a);
                return;
            }
            if (a.Ride < 6f || world.Train.Dwell <= 0f) return;
            // The train has stopped at a station: get off there, and live near it from now on.
            RefreshStations();
            var loco = world.Train.LocoPosition;
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
            bool walking = a.State == Activity.Walking;
            float swing = walking ? Mathf.Sin(a.Phase) : 0f;
            float bob = walking ? Mathf.Abs(Mathf.Cos(a.Phase)) * 0.0006f : 0f;
            a.Root.localPosition = new Vector3(a.Position.x, a.Height + bob, a.Position.y);
            a.Root.localRotation = Quaternion.Euler(0f, a.Heading, walking ? swing * 3f : 0f);
            a.LegL.localRotation = Quaternion.Euler(swing * 30f, 0f, 0f);
            a.LegR.localRotation = Quaternion.Euler(-swing * 30f, 0f, 0f);
            a.ArmL.localRotation = Quaternion.Euler(-swing * 26f, 0f, -6f);
            if (a.State == Activity.Waving || a.Cheering)
                a.ArmR.localRotation = Quaternion.Euler(0f, 0f, 145f + Mathf.Sin(clock * 11f) * 22f);
            else if (a.State == Activity.Chatting)
                a.ArmR.localRotation = Quaternion.Euler(-35f + Mathf.Sin(clock * 3f + a.Home.y * 40f) * 15f, 0f, 10f);   // gesturing
            else
                a.ArmR.localRotation = Quaternion.Euler(swing * 26f, 0f, 6f);
        }
    }
}
