using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    [Serializable]
    public struct BoatInstance
    {
        public float x, z, heading;
        public int kind, seed;
    }

    /// <summary>
    /// Boats on the ponds. Each keeps to its own water: it heads for a point it can reach in a straight line without
    /// touching the shore, turning gently, and picks another when it gets there (or when the bank comes up ahead). Rowing
    /// boats row (the oars dip and sweep), sailing boats swing their sail to the wind, the steamer chugs. When villagers
    /// wait at a landing stage on its pond, a boat comes alongside, takes them aboard (they sit in it), goes round the
    /// lake and brings them back. Saved with the world; erased and undone like anything else.
    /// </summary>
    public sealed class BoatSystem : MonoBehaviour
    {
        public enum State { Cruising, ToStage, Docked, Trip, Returning }

        public sealed class Boat
        {
            public BoatInstance Data;
            public BoatKind Kind;
            public string Name;
            public Transform Root, Sail, OarL, OarR;
            public Transform[] Seats;
            public Vector2 Pos, Target;
            public float Heading, Speed, Timer, Rest, Phase;
            public State State;
            public Vector2 Stage;                                    // the landing stage it serves (where it stands)
            public int Legs;
            public readonly List<(int house, int spot)> Aboard = new();
        }

        private static readonly string[] BoatNames = { "Bluebell", "Mallard", "Puffin", "Daisy", "Kingfisher", "Moorhen", "Skylark", "Willow", "Pippin", "Swallow", "Heron", "Primrose" };

        public readonly List<Boat> Boats = new();
        private WonderlandWorld world;
        private Material lit;
        private Rng rng = new(41);
        private float stageTimer;

        /// <summary>Is a villager waiting at (landing stage, spot) for a boat trip? (set by the world: PeopleSystem)</summary>
        public Func<int, int, bool> Waiting;
        /// <summary>The trip of whoever sat at (landing stage, spot) is over.</summary>
        public Action<int, int> Landed;

        public void Init(WonderlandWorld owner, Material material) { world = owner; lit = material; }

        // ------------------------------------------------------------------ water

        private bool Wet(Vector2 p) => world.Terrain.WaterAt(p.x, p.y) > 0.6f;

        /// <summary>A boat of this kind fits here: water under it and a little round it.</summary>
        public bool Afloat(Vector2 p, float heading, BoatKind k)
        {
            float half = BoatFactory.Length(k) * 0.5f + 0.006f;
            var f = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            var side = new Vector2(f.y, -f.x) * (half * 0.35f);
            return Wet(p) && Wet(p + f * half) && Wet(p - f * half) && Wet(p + side) && Wet(p - side);
        }

        /// <summary>Open water all the way from a to b (for a boat of this size).</summary>
        private bool Clear(Vector2 a, Vector2 b, BoatKind k)
        {
            float margin = BoatFactory.Length(k) * 0.3f + 0.006f;
            var d = b - a; float len = d.magnitude;
            if (len < 1e-4f) return true;
            var dir = d / len; var side = new Vector2(dir.y, -dir.x) * margin;
            for (float t = 0f; t <= len + margin; t += 0.008f)
            {
                var p = a + dir * Mathf.Min(t, len + margin);
                if (!Wet(p) || !Wet(p + side) || !Wet(p - side)) return false;
            }
            return true;
        }

        private float Level(Vector2 p) => world.Terrain.WaterLevelAt(p.x, p.y);

        // ------------------------------------------------------------------ boats

        public string Validate(Vector2 p, float heading, int kind)
        {
            if (world.Terrain.WaterAt(p.x, p.y) < 0.5f) return "Boats go on a pond";
            if (!Afloat(p, heading, (BoatKind)kind)) return "Not enough open water here for it";
            foreach (var b in Boats) if ((b.Pos - p).magnitude < (BoatFactory.Length(b.Kind) + BoatFactory.Length((BoatKind)kind)) * 0.5f) return "Another boat is in the way";
            return null;
        }

        public void Add(BoatInstance d)
        {
            var b = new Boat { Data = d, Kind = (BoatKind)Mathf.Clamp(d.kind, 0, BoatFactory.KindCount - 1), Pos = new Vector2(d.x, d.z), Heading = d.heading };
            b.Name = BoatNames[(int)((uint)d.seed % (uint)BoatNames.Length)];
            b.Target = b.Pos;
            b.Rest = 1f + (d.seed % 5);
            Build(b);
            Boats.Add(b);
            Pose(b, 0f);
        }

        public void SetBoats(IEnumerable<BoatInstance> list)
        {
            foreach (var b in Boats) Release(b);
            Boats.Clear();
            if (list != null) foreach (var d in list) Add(d);
        }

        public BoatInstance[] Export()
        {
            var list = new BoatInstance[Boats.Count];
            for (int i = 0; i < Boats.Count; i++) { var b = Boats[i]; list[i] = new BoatInstance { x = b.Pos.x, z = b.Pos.y, heading = b.Heading, kind = (int)b.Kind, seed = b.Data.seed }; }
            return list;
        }

        public int Nearest(Vector3 p, float within)
        {
            int best = -1; float bestD = within;
            for (int i = 0; i < Boats.Count; i++)
            {
                float d = (Boats[i].Pos - new Vector2(p.x, p.z)).magnitude - BoatFactory.Length(Boats[i].Kind) * 0.35f;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public BoatInstance RemoveAt(int i)
        {
            var b = Boats[i];
            LandAll(b);
            Release(b);
            Boats.RemoveAt(i);
            return b.Data;
        }

        private void Release(Boat b)
        {
            if (b.Root == null) return;
            if (Application.isPlaying) Destroy(b.Root.gameObject); else DestroyImmediate(b.Root.gameObject);
        }

        private Transform Part(string name, Mesh mesh, Transform parent, bool shadow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = lit; r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        private void Build(Boat b)
        {
            b.Root = Part(BoatFactory.Names[(int)b.Kind], BoatFactory.Hull(b.Kind), transform, true);
            switch (b.Kind)
            {
                case BoatKind.Rowing:
                    var rower = Part("Rower", BoatFactory.Crew(), b.Root, false);
                    rower.localPosition = new Vector3(0f, 0.0035f, -0.004f); rower.localRotation = Quaternion.Euler(0f, 180f, 0f);   // rowers face the stern
                    b.OarL = Part("Oar", BoatFactory.Oar(), b.Root, false); b.OarL.localPosition = new Vector3(-0.0065f, 0.008f, 0.0f);
                    b.OarR = Part("Oar", BoatFactory.Oar(), b.Root, false); b.OarR.localPosition = new Vector3(0.0065f, 0.008f, 0.0f);
                    break;
                case BoatKind.Sailing:
                    b.Sail = Part("Sail", BoatFactory.Sail(), b.Root, true); b.Sail.localPosition = new Vector3(0f, 0.003f, 0.016f);
                    var helm = Part("Sailor", BoatFactory.Crew(), b.Root, false); helm.localPosition = new Vector3(0f, 0.004f, -0.024f);
                    break;
                default:
                    var captain = Part("Captain", BoatFactory.Crew(), b.Root, false); captain.localPosition = new Vector3(0f, 0.021f, 0.017f);
                    break;
            }
            var seats = BoatFactory.Seats(b.Kind);
            b.Seats = new Transform[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                b.Seats[i] = new GameObject("Seat").transform;
                b.Seats[i].SetParent(b.Root, false);
                b.Seats[i].localPosition = seats[i];
            }
        }

        // ------------------------------------------------------------------ trips

        /// <summary>Where a boat comes alongside a landing stage: just off its far end.</summary>
        public static Vector2 DockOf(HouseInstance h)
        {
            var off = Quaternion.Euler(0f, h.rotation, 0f) * new Vector3(0f, 0f, -(Catalog.Get(Catalog.Jetty).Half.y + 0.014f));
            return new Vector2(h.x + off.x, h.z + off.z);
        }

        private int StageAt(Vector2 at)
        {
            var houses = world.Buildings.Houses;
            for (int i = 0; i < houses.Count; i++) if (houses[i].variant == Catalog.Jetty && Mathf.Abs(houses[i].x - at.x) < 1e-4f && Mathf.Abs(houses[i].z - at.y) < 1e-4f) return i;
            return -1;
        }

        /// <summary>The seat of whoever is aboard from (landing stage, spot), or null while they are still waiting (or the trip is over).</summary>
        public Transform SeatOf(int house, int spot)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count) return null;
            var h = world.Buildings.Houses[house];
            var at = new Vector2(h.x, h.z);
            foreach (var b in Boats)
            {
                if (b.Aboard.Count == 0 || (b.Stage - at).sqrMagnitude > 1e-8f) continue;
                for (int k = 0; k < b.Aboard.Count; k++) if (b.Aboard[k].spot == spot) return b.Seats[Mathf.Min(k, b.Seats.Length - 1)];
            }
            return null;
        }

        private void LandAll(Boat b)
        {
            int stage = StageAt(b.Stage);
            foreach (var (_, spot) in b.Aboard) if (stage >= 0) Landed?.Invoke(stage, spot);
            b.Aboard.Clear();
        }

        /// <summary>A landing stage on this boat's water where people wait and no other boat is coming: go and fetch them.</summary>
        private bool FindStage(Boat b)
        {
            var houses = world.Buildings.Houses;
            for (int i = 0; i < houses.Count; i++)
            {
                if (houses[i].variant != Catalog.Jetty) continue;
                var at = new Vector2(houses[i].x, houses[i].z);
                bool taken = false;
                foreach (var o in Boats) if (o != b && o.State != State.Cruising && (o.Stage - at).sqrMagnitude < 1e-8f) taken = true;
                if (taken) continue;
                bool someone = false;
                for (int s = 0; s < Catalog.Get(Catalog.Jetty).Spots.Length && !someone; s++) someone = Waiting != null && Waiting(i, s);
                if (!someone) continue;
                var dock = DockOf(houses[i]);
                if (!Clear(b.Pos, dock, b.Kind)) continue;
                b.Stage = at; b.Target = dock; b.State = State.ToStage;
                return true;
            }
            return false;
        }

        private void Wander(Boat b)
        {
            for (int tries = 0; tries < 24; tries++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), r = rng.Range(0.06f, 0.5f);
                var p = b.Pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (Wet(p) && Clear(b.Pos, p, b.Kind)) { b.Target = p; return; }
            }
            b.Target = b.Pos;   // hemmed in: turn on the spot and try again
            b.Heading += 40f;
        }

        // ------------------------------------------------------------------ simulation

        private void Update() { if (world == null) return; long t0 = SimCost.Now; Step(Time.deltaTime); SimCost.Add(SimCost.Boats, t0); }

        public void Step(float dt)
        {
            if (Boats.Count == 0) return;
            stageTimer -= dt;
            bool lookForStages = stageTimer <= 0f;
            if (lookForStages) stageTimer = 1f;
            foreach (var b in Boats) StepBoat(b, dt, lookForStages);
        }

        private void StepBoat(Boat b, float dt, bool lookForStages)
        {
            b.Phase += dt;
            if (b.State is State.ToStage or State.Trip or State.Returning or State.Docked && StageAt(b.Stage) < 0) { LandAll(b); b.State = State.Cruising; }   // the landing stage was taken away
            if (b.State == State.Docked)
            {
                b.Speed = 0f; b.Timer -= dt;
                if (b.Aboard.Count == 0 || b.Legs > 0) Board(b);
                if (b.Timer <= 0f)
                {
                    if (b.Legs > 0 && b.Aboard.Count > 0) { b.State = State.Trip; Wander(b); }
                    else { LandAll(b); b.State = State.Cruising; b.Rest = 12f; b.Legs = 0; }
                }
                Pose(b, dt);
                return;
            }
            if (lookForStages && b.State == State.Cruising && b.Rest <= 0f) FindStage(b);
            b.Rest -= dt;
            var to = b.Target - b.Pos;
            float dist = to.magnitude;
            if (dist < 0.01f)
            {
                switch (b.State)
                {
                    case State.ToStage: b.State = State.Docked; b.Timer = 6f; b.Legs = 3; b.Aboard.Clear(); break;
                    case State.Returning: b.State = State.Docked; b.Timer = 3f; b.Legs = 0; break;
                    case State.Trip:
                        if (--b.Legs > 0) Wander(b);
                        else { b.State = State.Returning; int st = StageAt(b.Stage); b.Target = st >= 0 ? DockOf(world.Buildings.Houses[st]) : b.Pos; if (st >= 0 && !Clear(b.Pos, b.Target, b.Kind)) { b.Legs = 1; b.State = State.Trip; Wander(b); } }
                        break;
                    default: b.Timer -= dt; if (b.Timer <= 0f) { b.Timer = rng.Range(2f, 8f); Wander(b); } break;
                }
                b.Speed = Mathf.MoveTowards(b.Speed, 0f, dt * 0.02f);
            }
            else
            {
                // Turn towards the target, slowing for sharp turns; stop and pick another way if the bank comes up.
                float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                float turn = Mathf.DeltaAngle(b.Heading, want);
                b.Heading += Mathf.Clamp(turn, -50f * dt, 50f * dt);
                float cruise = BoatFactory.Speed(b.Kind) * Mathf.Clamp01(1.2f - Mathf.Abs(turn) / 90f) * (b.State is State.ToStage or State.Returning ? Mathf.Clamp01(dist / 0.05f) * 0.7f + 0.3f : 1f);
                if (b.Kind == BoatKind.Sailing) cruise *= 0.6f + 0.6f * Mathf.Abs(Mathf.Sin((b.Heading - WindDegrees()) * Mathf.Deg2Rad));   // fastest across the wind
                b.Speed = Mathf.MoveTowards(b.Speed, cruise, dt * 0.02f);
                var f = new Vector2(Mathf.Sin(b.Heading * Mathf.Deg2Rad), Mathf.Cos(b.Heading * Mathf.Deg2Rad));
                var next = b.Pos + f * b.Speed * dt;
                if (Afloat(next, b.Heading, b.Kind)) b.Pos = next;
                else { b.Speed = 0f; if (b.State == State.Cruising || b.State == State.Trip) Wander(b); else b.Heading += 30f * dt; }
            }
            Pose(b, dt);
        }

        /// <summary>Takes aboard whoever waits at the landing stage, up to the boat's seats.</summary>
        private void Board(Boat b)
        {
            int stage = StageAt(b.Stage);
            if (stage < 0 || Waiting == null) return;
            int spots = Catalog.Get(Catalog.Jetty).Spots.Length;
            for (int s = 0; s < spots && b.Aboard.Count < BoatFactory.Capacity(b.Kind); s++)
                if (Waiting(stage, s) && !b.Aboard.Exists(x => x.spot == s)) { b.Aboard.Add((stage, s)); b.Timer = Mathf.Max(b.Timer, 2f); }
        }

        private float WindDegrees() { var w = world.Sky.Wind; return Mathf.Atan2(w.x, w.y) * Mathf.Rad2Deg; }

        private void Pose(Boat b, float dt)
        {
            float level = Level(b.Pos);
            float bob = Mathf.Sin(b.Phase * 2.1f + b.Data.seed) * 0.0006f;
            float roll = Mathf.Sin(b.Phase * 1.7f + b.Data.seed * 0.3f) * 2.5f, pitch = Mathf.Sin(b.Phase * 1.3f) * 1.2f;
            b.Root.localPosition = new Vector3(b.Pos.x, level + bob, b.Pos.y);
            b.Root.localRotation = Quaternion.Euler(pitch, b.Heading, roll);
            if (b.OarL != null)
            {
                // A stroke: the oars sweep back in the water and lift forward through the air, quicker the faster it goes.
                float rate = 1.6f + b.Speed * 60f, s = Mathf.Sin(b.Phase * rate), c = Mathf.Cos(b.Phase * rate);
                bool rowing = b.Speed > 0.002f;
                float sweep = rowing ? s * 35f : 0f, dip = rowing ? (c > 0f ? -14f : 6f) : -8f;
                b.OarL.localRotation = Quaternion.Euler(0f, 180f - sweep, -dip);
                b.OarR.localRotation = Quaternion.Euler(0f, sweep, dip);
            }
            if (b.Sail != null)
            {
                // The sail swings out to leeward, more the further the wind is behind.
                float rel = Mathf.DeltaAngle(b.Heading, WindDegrees());
                float swing = Mathf.Clamp(rel * 0.5f, -70f, 70f);
                b.Sail.localRotation = Quaternion.Euler(0f, -swing, 0f);
            }
        }

        /// <summary>"The steamer Mallard - on a trip round the lake with 4 aboard" (cards and hints).</summary>
        public string Describe(int i)
        {
            var b = Boats[i];
            string what = b.State switch
            {
                State.ToStage => "coming in to the landing stage",
                State.Docked => b.Legs > 0 ? $"alongside the landing stage, taking people aboard ({b.Aboard.Count} of {BoatFactory.Capacity(b.Kind)})" : "alongside, people stepping off",
                State.Trip => $"on a trip round the lake with {b.Aboard.Count} aboard",
                State.Returning => $"heading back with {b.Aboard.Count} aboard",
                _ => b.Speed > 0.003f ? (b.Kind == BoatKind.Sailing ? "sailing" : b.Kind == BoatKind.Rowing ? "rowing about" : "steaming round the lake") : "drifting",
            };
            return $"{BoatFactory.Names[(int)b.Kind]} {b.Name} - {what}";
        }
    }
}
