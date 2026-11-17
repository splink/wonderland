using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    [Serializable]
    public struct AnimalInstance
    {
        public float x, z;     // home: where it was placed; it lives around here
        public int kind;
        public int seed;
    }

    /// <summary>
    /// Animals living in places that suit them. Each kind has a habitat and a temperament, and the rest is the same small
    /// loop: rest or feed for a while, pick a nearby spot that suits it, turn, go there. Grazers (sheep, cows, horses,
    /// pigs) keep to open grass and, when placed inside a fenced pasture, to that pasture - fences, hedges and walls are
    /// never crossed. Hens peck about close to home. Ducks paddle on ponds and dabble. Deer stay near the woods, look up
    /// alertly, and bolt when a person or a train comes close. Homes are saved; positions are runtime only.
    /// </summary>
    public sealed class AnimalSystem : MonoBehaviour
    {
        public const float WanderRadius = 0.22f;
        public readonly List<AnimalInstance> Animals = new();
        private readonly List<Agent> agents = new();
        private TerrainField terrain;
        private Material material;
        private Func<Vector2, AnimalKind, Vector2, bool> suits;     // (point, kind, home) -> may stand here
        private Func<Vector2, Vector2, bool> blocked;               // a barrier between two points
        private Func<Vector2, float, Vector2?> threat;              // something scary within a distance (deer)
        /// <summary>Night: animals settle down where they are (deer still bolt).</summary>
        public Func<bool> Resting;
        /// <summary>Pets: the villager living nearest a pet's home (-1: none), where a villager is when out of doors (null: indoors,
        /// on a train, far), the top of a wall, fence or hedge at a point (0: none), the ground (or a bridge deck) height.</summary>
        public Func<Vector2, int> OwnerOf;
        public Func<int, Vector2?> Outdoors;
        public Func<Vector2, float> PerchAt;
        public Func<Vector2, float> GroundAt;
        /// <summary>An animal makes itself heard (a bark, a meow): sound picks it up.</summary>
        public event Action<Vector2, AnimalKind> Voice;
        public const float Leash = 0.8f, CatRange = 0.12f;

        private sealed class Agent
        {
            public Transform Body, Head;
            public AnimalKind Kind;
            public Vector2 Home, Position, Target;
            public float Heading, Timer, Bob, Pitch, Speed;
            public int Index, Level;   // (Level: the level of detail it is drawn at)
            public Vector2 GroundPos = new(float.NaN, 0f);   // where Ground and Normal were last looked up (Smart scheduling)
            public float Ground;
            public Vector3 Normal = Vector3.up;
            public bool Walking, Fleeing;
            public Rng Rng;
            // Pets.
            public int Owner = -1;
            public float OwnerTimer, Wait, Rest, Perch, VoiceTimer;
            public bool Following, Returning;
            public readonly List<Vector2> Trail = new();   // where its owner walked (a dog follows in their footsteps)
            public readonly List<Vector2> Away = new();    // its own way from home (to go back the same way)
        }

        private struct Temperament { public float Speed, Range, GrazePitch, TurnRate; }

        private static Temperament Of(AnimalKind k) => k switch
        {
            AnimalKind.Sheep => new Temperament { Speed = 0.014f, Range = WanderRadius, GrazePitch = 38f, TurnRate = 120f },
            AnimalKind.Cow => new Temperament { Speed = 0.011f, Range = WanderRadius, GrazePitch = 38f, TurnRate = 100f },
            AnimalKind.Horse => new Temperament { Speed = 0.02f, Range = 0.3f, GrazePitch = 50f, TurnRate = 130f },
            AnimalKind.Pig => new Temperament { Speed = 0.01f, Range = 0.15f, GrazePitch = 25f, TurnRate = 140f },
            AnimalKind.Chicken => new Temperament { Speed = 0.016f, Range = 0.08f, GrazePitch = 60f, TurnRate = 300f },
            AnimalKind.Duck => new Temperament { Speed = 0.009f, Range = 0.3f, GrazePitch = 0f, TurnRate = 90f },
            _ => new Temperament { Speed = 0.016f, Range = 0.35f, GrazePitch = 45f, TurnRate = 160f },
        };

        public void Init(TerrainField field, Material lit, Func<Vector2, AnimalKind, Vector2, bool> habitat, Func<Vector2, Vector2, bool> barrier = null, Func<Vector2, float, Vector2?> danger = null)
        {
            terrain = field; material = lit; suits = habitat; blocked = barrier; threat = danger;
        }

        public void SetAnimals(IEnumerable<AnimalInstance> animals)
        {
            Animals.Clear(); Animals.AddRange(animals);
            Rebuild();
        }

        public void Rebuild()
        {
            foreach (var a in agents) Release(a.Body.gameObject);
            agents.Clear();
            foreach (var a in Animals) agents.Add(Spawn(a));
        }

        private static void Release(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        private Agent Spawn(AnimalInstance a)
        {
            var kind = (AnimalKind)Mathf.Clamp(a.kind, 0, AnimalFactory.KindCount - 1);
            var body = Part(kind + " body", AnimalFactory.Body(kind), transform, kind != AnimalKind.Duck);
            var head = Part("Head", AnimalFactory.Head(kind), body, kind != AnimalKind.Duck);
            head.localPosition = AnimalFactory.NeckOffset(kind);
            var rng = new Rng(a.seed);
            var agent = new Agent { Body = body, Head = head, Kind = kind, Home = new Vector2(a.x, a.z), Position = new Vector2(a.x, a.z), Heading = rng.Range(0f, 360f), Timer = rng.Range(0.5f, 4f), Rng = rng, VoiceTimer = rng.Range(20f, 60f) };
            agent.Target = agent.Position;
            Pose(agent);
            return agent;
        }

        private Transform Part(string name, Mesh mesh, Transform parent, bool shadows)
        {
            var go = new GameObject(name);
            go.layer = WonderlandWorld.MiniatureLayer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = shadows && name != "Head" ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        public void Add(AnimalInstance a) { Animals.Add(a); agents.Add(Spawn(a)); }

        public AnimalInstance RemoveAt(int i)
        {
            var a = Animals[i];
            Animals.RemoveAt(i);
            Release(agents[i].Body.gameObject);
            agents.RemoveAt(i);
            return a;
        }

        /// <summary>The animal nearest a local point within <paramref name="within"/>, or -1.</summary>
        public int Nearest(Vector3 p, float within)
        {
            int best = -1; float bestD = within * within;
            for (int i = 0; i < agents.Count; i++)
            {
                float d = (agents[i].Position - new Vector2(p.x, p.z)).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public Vector2 PositionOf(int i) => agents[i].Position;
        public bool IsFleeing(int i) => agents[i].Fleeing;

        private bool Suits(Agent a, Vector2 p) => suits == null || suits(p, a.Kind, a.Home);
        private int tick;

        /// <summary>A simpler animal further away (same shape and coat, fewer facets), unless Distance is set to Full.</summary>
        private static void Detail(Agent a, float distance)
        {
            var size = AnimalFactory.Body(a.Kind).bounds.size;
            int level = Lod.For(Mathf.Max(size.x, size.y, size.z), distance, a.Level);
            if (level == a.Level) return;
            a.Level = level;
            a.Body.GetComponent<MeshFilter>().sharedMesh = AnimalFactory.Body(a.Kind, level);
            a.Head.GetComponent<MeshFilter>().sharedMesh = AnimalFactory.Head(a.Kind, level);
        }

        private void Update()
        {
            if (terrain == null) return;
            long t0 = SimCost.Now; Step(Time.deltaTime); SimCost.Add(SimCost.Animals, t0);
        }

        /// <summary>Advances every animal (also driven by the scripted test).</summary>
        public void Step(float dt)
        {
            tick++;
            bool smart = PerfSettings.SmartVillagers;
            var eye = Camera.main != null ? (Vector3?)transform.InverseTransformPoint(Camera.main.transform.position) : null;
            float drawn = WonderlandWorld.MiniatureCullDistance / Mathf.Max(transform.lossyScale.x, 1e-3f) + 0.1f;
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                a.Index = i;
                int beat = tick + i;
                var t = Of(a.Kind);
                a.Timer -= dt;
                if (eye.HasValue && (beat & 7) == 0) Detail(a, (new Vector3(a.Position.x, a.Ground, a.Position.y) - eye.Value).magnitude * transform.lossyScale.x);
                // Beyond the distance figures are drawn at, nobody sees them: posed only now and then.
                bool unseen = smart && eye.HasValue && (beat & 7) != 0 && (new Vector3(a.Position.x, a.Ground, a.Position.y) - eye.Value).sqrMagnitude > drawn * drawn;
                if (a.Kind == AnimalKind.Dog) { StepDog(a, dt); if (!unseen) Pose(a); continue; }
                if (a.Kind == AnimalKind.Cat) { StepCat(a, dt); if (!unseen) Pose(a); continue; }
                // Deer: something close? Bolt away from it.
                if (a.Kind == AnimalKind.Deer && threat != null && !a.Fleeing && (!smart || beat % 6 == 0))
                {
                    var danger = threat(a.Position, 0.11f);
                    if (danger.HasValue)
                    {
                        var away = (a.Position - danger.Value).normalized;
                        for (int k = 0; k < 8; k++)
                        {
                            var turned = Quaternion.Euler(0f, 0f, (k % 2 == 0 ? 1 : -1) * k * 20f) * new Vector3(away.x, away.y, 0f);
                            var tt = a.Position + new Vector2(turned.x, turned.y) * 0.15f;
                            if (!Suits(a, tt)) continue;
                            a.Target = tt; a.Walking = true; a.Fleeing = true; a.Timer = 4f;
                            break;
                        }
                    }
                }
                if (!a.Walking)
                {
                    bool resting = Resting != null && Resting();
                    a.Pitch = Mathf.MoveTowards(a.Pitch, resting ? t.GrazePitch * 0.5f : a.Kind == AnimalKind.Deer && a.Timer % 3f < 1f ? -12f : t.GrazePitch, 60f * dt);   // head down, nibbling (deer look up now and then)
                    if (resting) { if (a.Timer <= 0f) a.Timer = a.Rng.Range(2f, 5f); }
                    else if (a.Timer <= 0f && PickTarget(a, t)) { a.Walking = true; a.Timer = 12f; }
                    else if (a.Timer <= 0f) a.Timer = a.Rng.Range(1f, 3f);
                }
                else
                {
                    float speed = t.Speed * (a.Fleeing ? 3.5f : 1f);
                    a.Pitch = Mathf.MoveTowards(a.Pitch, a.Fleeing ? -20f : 0f, 120f * dt);
                    var to = a.Target - a.Position;
                    float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                    a.Heading = Mathf.MoveTowardsAngle(a.Heading, want, t.TurnRate * (a.Fleeing ? 3f : 1f) * dt);
                    if (Mathf.Abs(Mathf.DeltaAngle(a.Heading, want)) < 25f)
                    {
                        var fwd = new Vector2(Mathf.Sin(a.Heading * Mathf.Deg2Rad), Mathf.Cos(a.Heading * Mathf.Deg2Rad));
                        var next = a.Position + fwd * Mathf.Min(speed * dt, to.magnitude);
                        // Smart: the ground and fences ahead are checked every fourth frame, looking far enough ahead to cover them
                        // (the way to the target was checked when it was picked).
                        bool free = smart && (beat & 3) != 0 || Suits(a, next) && (blocked == null || !blocked(a.Position, next + fwd * (0.004f + (smart ? speed * dt * 4f : 0f))));
                        if (free) { a.Position = next; a.Bob += dt * (a.Kind == AnimalKind.Chicken ? 30f : 14f) * (a.Fleeing ? 2f : 1f); }
                        else a.Timer = 0f;   // blocked: stop and graze
                    }
                    if (to.magnitude < 0.004f || a.Timer <= 0f) { a.Walking = false; a.Fleeing = false; a.Timer = a.Kind == AnimalKind.Chicken ? a.Rng.Range(0.6f, 2f) : a.Rng.Range(2.5f, 7f); }
                }
                if (!unseen) Pose(a);
            }
        }

        private bool PickTarget(Agent a, Temperament t)
        {
            for (int k = 0; k < 8; k++)
            {
                var target = a.Home + a.Rng.InsideUnitCircle() * t.Range;
                if ((target - a.Position).magnitude < 0.02f) continue;
                if (!Suits(a, target)) continue;
                if (blocked != null && blocked(a.Position, target)) continue;
                a.Target = target;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the card

        private static readonly string[] PetNames = { "Rex", "Bella", "Luna", "Max", "Biscuit", "Pepper", "Sooty", "Ginger", "Toffee", "Pip", "Rusty", "Mabel", "Otis", "Willow", "Bramble", "Mittens" };
        private static readonly string[] FarmNames = { "Daisy", "Buttercup", "Clover", "Bluebell", "Hazel", "Poppy", "Rosie", "Bramble", "Duchess", "Captain", "Nutmeg", "Maisie", "Barnaby", "Honey", "Dandelion", "Truffle" };

        public struct AnimalCard
        {
            public string Name, Species, Home, Now, Goal;
            public AnimalKind Kind;
            public Vector3 Position;
            public Vector3? Destination;
            public int Owner;
        }

        /// <summary>Who an animal is and what it is doing (wild ones go unnamed).</summary>
        public AnimalCard CardOf(int i, System.Func<int, string> personName)
        {
            var a = agents[i];
            var c = new AnimalCard { Kind = a.Kind, Species = AnimalFactory.Names[(int)a.Kind], Owner = a.Owner, Position = a.Body.localPosition };
            uint h = (uint)Animals[i].seed * 2654435761u; h ^= h >> 15;
            bool wild = a.Kind is AnimalKind.Deer or AnimalKind.Duck;
            c.Name = wild ? null : AnimalFactory.Pet(a.Kind) ? PetNames[h % (uint)PetNames.Length] : FarmNames[h % (uint)FarmNames.Length];
            string owner = a.Owner >= 0 && personName != null ? personName(a.Owner) : null;
            c.Home = a.Kind switch
            {
                AnimalKind.Dog => owner != null ? $"Lives with {owner}" : "Waiting for someone to live with",
                AnimalKind.Cat => owner != null ? $"Lives with {owner}, and keeps to a patch {CatRange * 100f:0} cm round" : $"Keeps to a patch {CatRange * 100f:0} cm round its home",
                AnimalKind.Duck => "Lives on the pond",
                AnimalKind.Deer => "Lives in the woods; shy of people and trains",
                AnimalKind.Chicken => "Pecks about the yard",
                _ => "Grazes its pasture",
            };
            bool resting = Resting != null && Resting();
            if (a.Kind == AnimalKind.Dog)
            {
                c.Now = a.Following ? (owner != null ? $"Trotting along behind {owner}" : "Following its owner") : a.Returning ? "Going home the way it came" : a.Walking ? "Pottering about the garden" : a.Rest > 4f ? "Lying on the doorstep" : "Sniffing about";
                c.Goal = a.Following ? "Keep up" : a.Wait > 0f && !a.Returning ? $"Wait for {owner ?? "its owner"} to come out" : a.Returning ? "Home" : null;
            }
            else if (a.Kind == AnimalKind.Cat)
            {
                c.Now = a.Fleeing ? "Dashing away from a dog" : a.Walking ? "Strolling about its patch" : a.Perch > 0f ? "Sitting up on the wall" : a.Rest > 3f ? "Napping" : "Washing its paws";
                c.Goal = a.Walking ? (PerchAt != null && PerchAt(a.Target) > 0f ? "Up on the wall" : "A sunny spot") : null;
            }
            else
            {
                c.Now = a.Fleeing ? "Bolting!" : a.Walking ? (a.Kind == AnimalKind.Duck ? "Paddling" : "Ambling to fresh grass") : resting ? "Settled for the night" : a.Kind switch
                {
                    AnimalKind.Duck => "Dabbling", AnimalKind.Chicken => "Pecking", AnimalKind.Deer => "Grazing, watchful", _ => "Grazing",
                };
                c.Goal = a.Fleeing ? "Away from danger" : a.Walking ? "A good spot nearby" : null;
            }
            if (a.Walking) c.Destination = new Vector3(a.Target.x, a.Body.localPosition.y, a.Target.y);
            return c;
        }

        // ------------------------------------------------------------------ pets

        public bool IsFollowing(int i) => agents[i].Following;
        public int OwnerIndex(int i) => agents[i].Owner;
        public bool Perched(int i) => agents[i].Perch > 0f;

        /// <summary>
        /// A dog keeps to its garden while its owner is indoors (dozing on the doorstep at night), and goes with them when
        /// they are out - trotting along in their footsteps, so it never cuts through a house or jumps a fence. When the
        /// owner goes into a shop it waits by the door; if they don't come out it goes home the way it came.
        /// </summary>
        private void StepDog(Agent a, float dt)
        {
            a.OwnerTimer -= dt;
            if (a.OwnerTimer <= 0f) { a.OwnerTimer = 3f; a.Owner = OwnerOf?.Invoke(a.Home) ?? -1; }
            var owner = a.Owner >= 0 && Outdoors != null ? Outdoors(a.Owner) : null;
            bool night = Resting != null && Resting();
            if (owner.HasValue && (owner.Value - a.Home).magnitude < Leash && (a.Following || (owner.Value - a.Position).magnitude < 0.15f))
            {
                if (!a.Following) { a.Following = true; a.Returning = false; a.Trail.Clear(); Speak(a, 0.6f); }
                a.Wait = 0f;
                if (a.Trail.Count == 0 || (owner.Value - a.Trail[^1]).magnitude > 0.008f) { a.Trail.Add(owner.Value); if (a.Trail.Count > 400) a.Trail.RemoveAt(0); }
                while (a.Trail.Count > 1 && (a.Trail[0] - a.Position).magnitude < 0.008f) a.Trail.RemoveAt(0);
                float gap = (owner.Value - a.Position).magnitude;
                if (gap < 0.02f) { a.Walking = false; a.Pitch = Mathf.MoveTowards(a.Pitch, 20f, 60f * dt); Face(a, owner.Value, dt, 200f); }
                else Move(a, a.Trail[0], gap > 0.08f ? 0.04f : 0.026f, dt, false);
                NoteAway(a);
                if ((a.VoiceTimer -= dt) <= 0f) { a.VoiceTimer = a.Rng.Range(40f, 90f); Speak(a, 1f); }
                return;
            }
            if (a.Following)
            {
                // The owner went in somewhere (or onto a train): wait by the door a while.
                a.Walking = false; a.Rest += dt;
                a.Wait += dt;
                if (a.Wait > (night ? 3f : 25f)) { a.Following = false; a.Returning = true; a.Trail.Clear(); }
                return;
            }
            if (a.Returning)
            {
                while (a.Away.Count > 0 && (a.Away[^1] - a.Position).magnitude < 0.008f) a.Away.RemoveAt(a.Away.Count - 1);
                if (a.Away.Count == 0) { a.Returning = false; a.Walking = false; a.Timer = a.Rng.Range(2f, 6f); return; }
                Move(a, a.Away[^1], 0.022f, dt, false);
                return;
            }
            a.Away.Clear();
            // At home: potter about the garden, or lie on the doorstep (all night).
            if (!a.Walking)
            {
                a.Rest += dt;
                if (a.Timer <= 0f && !night)
                {
                    var target = a.Home + a.Rng.InsideUnitCircle() * 0.07f;
                    if (Suits(a, target) && (blocked == null || !blocked(a.Position, target))) { a.Target = target; a.Walking = true; a.Timer = 8f; a.Rest = 0f; }
                    else a.Timer = a.Rng.Range(1f, 3f);
                }
                a.Pitch = Mathf.MoveTowards(a.Pitch, a.Rest > 4f ? 30f : 10f, 40f * dt);
            }
            else
            {
                Move(a, a.Target, 0.015f, dt, true);
                if ((a.Target - a.Position).magnitude < 0.004f || a.Timer <= 0f) { a.Walking = false; a.Timer = a.Rng.Range(5f, 15f); }
            }
        }

        /// <summary>
        /// A cat keeps to its territory round its home: naps in the garden, strolls, sits up on a wall or fence (cats climb),
        /// and keeps out of a dog's way.
        /// </summary>
        private void StepCat(Agent a, float dt)
        {
            foreach (var d in agents)
            {
                if (d.Kind != AnimalKind.Dog || (d.Position - a.Position).magnitude > 0.05f || a.Fleeing) continue;
                var away = (a.Position - d.Position).normalized;
                var to = a.Position + away * 0.06f;
                if ((to - a.Home).magnitude < CatRange * 1.3f && (Suits(a, to) || (PerchAt?.Invoke(to) ?? 0f) > 0f)) { a.Target = to; a.Walking = true; a.Fleeing = true; a.Timer = 3f; a.Perch = 0f; Speak(a, 1f); }
            }
            if (!a.Walking)
            {
                a.Rest += dt;
                a.Pitch = Mathf.MoveTowards(a.Pitch, a.Rest > 3f ? 35f : 0f, 40f * dt);
                if (a.Timer > 0f) return;
                for (int k = 0; k < 8; k++)
                {
                    var target = a.Home + a.Rng.InsideUnitCircle() * CatRange;
                    float perch = PerchAt?.Invoke(target) ?? 0f;
                    if (perch <= 0f && !Suits(a, target)) continue;
                    a.Target = target; a.Walking = true; a.Timer = 10f; a.Rest = 0f; a.Perch = 0f;
                    break;
                }
                if (!a.Walking) a.Timer = a.Rng.Range(2f, 5f);
                if ((a.VoiceTimer -= 5f) <= 0f) { a.VoiceTimer = a.Rng.Range(60f, 150f); Speak(a, 1f); }
                return;
            }
            float speed = a.Fleeing ? 0.05f : 0.013f;
            Move(a, a.Target, speed, dt, false);
            if ((a.Target - a.Position).magnitude < 0.004f || a.Timer <= 0f)
            {
                a.Walking = false; a.Fleeing = false;
                a.Perch = PerchAt?.Invoke(a.Position) ?? 0f;   // up on the wall
                a.Timer = a.Rng.Range(6f, 22f);
            }
        }

        private void Move(Agent a, Vector2 target, float speed, float dt, bool careful)
        {
            var to = target - a.Position;
            if (to.sqrMagnitude < 1e-8f) return;
            a.Walking = true;
            Face(a, target, dt, 400f);
            var next = a.Position + to.normalized * Mathf.Min(speed * dt, to.magnitude);
            if (careful && (!Suits(a, next) || (blocked != null && blocked(a.Position, next)))) { a.Timer = 0f; a.Walking = false; return; }
            a.Position = next;
            a.Bob += dt * speed * 900f;
            a.Pitch = Mathf.MoveTowards(a.Pitch, 0f, 120f * dt);
        }

        private static void Face(Agent a, Vector2 at, float dt, float rate)
        {
            var to = at - a.Position;
            if (to.sqrMagnitude > 1e-8f) a.Heading = Mathf.MoveTowardsAngle(a.Heading, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg, rate * dt);
        }

        private static void NoteAway(Agent a)
        {
            if (a.Away.Count == 0) a.Away.Add(a.Home);
            if ((a.Position - a.Away[^1]).magnitude > 0.01f) { a.Away.Add(a.Position); if (a.Away.Count > 500) a.Away.RemoveAt(1); }
        }

        private void Speak(Agent a, float chance) { if (a.Rng.Chance(chance)) Voice?.Invoke(a.Position, a.Kind); }

        private void Pose(Agent a)
        {
            if (AnimalFactory.Pet(a.Kind)) { PosePet(a); return; }
            float y;
            if (a.Kind == AnimalKind.Duck)
            {
                float level = terrain.WaterLevelAt(a.Position.x, a.Position.y);
                y = (level > 0f ? level : terrain.SurfaceAt(a.Position.x, a.Position.y)) - 0.0012f + Mathf.Sin(Time.realtimeSinceStartup * 2.2f + a.Home.x * 40f) * 0.0003f;
            }
            else
            {
                // The ground and its slope: looked up again once it has moved (or now and then: the land may change).
                if (!PerfSettings.SmartVillagers || ((tick + a.Index) & 15) == 0 || !((a.Position - a.GroundPos).sqrMagnitude < 0.001f * 0.001f))
                { a.Ground = terrain.SurfaceAt(a.Position.x, a.Position.y); a.Normal = terrain.NormalAt(a.Position.x, a.Position.y); a.GroundPos = a.Position; }
                y = a.Ground;
            }
            var normal = a.Kind == AnimalKind.Duck ? Vector3.up : a.Normal;
            float bob = a.Walking ? Mathf.Abs(Mathf.Sin(a.Bob)) * (a.Kind == AnimalKind.Chicken ? 0.0012f : a.Fleeing ? 0.002f : 0.0007f) : 0f;
            float roll = a.Walking ? Mathf.Sin(a.Bob) * 3f : 0f;
            a.Body.localPosition = new Vector3(a.Position.x, y + bob, a.Position.y);
            var up = a.Kind == AnimalKind.Duck ? Vector3.up : Vector3.Lerp(Vector3.up, normal, 0.5f);
            // Ducks dabble: tip forward, tail up, now and then.
            float tip = a.Kind == AnimalKind.Duck && !a.Walking && Mathf.Repeat(Time.realtimeSinceStartup * 0.3f + a.Home.y * 13f, 1f) > 0.85f ? 70f : 0f;
            a.Body.localRotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(tip, a.Heading, roll);
            float nibble = a.Walking ? 0f : Mathf.Sin(Time.realtimeSinceStartup * (a.Kind == AnimalKind.Chicken ? 18f : 7f) + a.Home.x * 50f) * (a.Kind == AnimalKind.Chicken ? 25f : 4f);
            a.Head.localRotation = Quaternion.Euler(a.Pitch + nibble, 0f, 0f);
        }

        private void PosePet(Agent a)
        {
            float y = (GroundAt != null ? GroundAt(a.Position) : terrain.SurfaceAt(a.Position.x, a.Position.y)) + (a.Walking ? 0f : a.Perch);
            bool lying = !a.Walking && a.Rest > 4f;
            float bob = a.Walking ? Mathf.Abs(Mathf.Sin(a.Bob)) * 0.0007f : 0f;
            a.Body.localPosition = new Vector3(a.Position.x, y + bob - (lying ? 0.0025f : 0f), a.Position.y);
            a.Body.localRotation = Quaternion.Euler(0f, a.Heading, a.Walking ? Mathf.Sin(a.Bob) * 4f : 0f);
            float look = a.Walking ? 0f : Mathf.Sin(Time.realtimeSinceStartup * 0.8f + a.Home.x * 60f) * 25f;
            a.Head.localRotation = Quaternion.Euler(lying ? a.Pitch * 0.4f : a.Pitch * 0.3f, look, 0f);
        }
    }
}
