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

        private sealed class Agent
        {
            public Transform Body, Head;
            public AnimalKind Kind;
            public Vector2 Home, Position, Target;
            public float Heading, Timer, Bob, Pitch, Speed;
            public bool Walking, Fleeing;
            public Rng Rng;
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
            var agent = new Agent { Body = body, Head = head, Kind = kind, Home = new Vector2(a.x, a.z), Position = new Vector2(a.x, a.z), Heading = rng.Range(0f, 360f), Timer = rng.Range(0.5f, 4f), Rng = rng };
            agent.Target = agent.Position;
            Pose(agent);
            return agent;
        }

        private Transform Part(string name, Mesh mesh, Transform parent, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
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

        private void Update()
        {
            if (terrain == null) return;
            Step(Time.deltaTime);
        }

        /// <summary>Advances every animal (also driven by the scripted test).</summary>
        public void Step(float dt)
        {
            foreach (var a in agents)
            {
                var t = Of(a.Kind);
                a.Timer -= dt;
                // Deer: something close? Bolt away from it.
                if (a.Kind == AnimalKind.Deer && threat != null && !a.Fleeing)
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
                    a.Pitch = Mathf.MoveTowards(a.Pitch, a.Kind == AnimalKind.Deer && a.Timer % 3f < 1f ? -12f : t.GrazePitch, 60f * dt);   // head down, nibbling (deer look up now and then)
                    if (a.Timer <= 0f && PickTarget(a, t)) { a.Walking = true; a.Timer = 12f; }
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
                        bool free = Suits(a, next) && (blocked == null || !blocked(a.Position, next + fwd * 0.004f));
                        if (free) { a.Position = next; a.Bob += dt * (a.Kind == AnimalKind.Chicken ? 30f : 14f) * (a.Fleeing ? 2f : 1f); }
                        else a.Timer = 0f;   // blocked: stop and graze
                    }
                    if (to.magnitude < 0.004f || a.Timer <= 0f) { a.Walking = false; a.Fleeing = false; a.Timer = a.Kind == AnimalKind.Chicken ? a.Rng.Range(0.6f, 2f) : a.Rng.Range(2.5f, 7f); }
                }
                Pose(a);
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

        private void Pose(Agent a)
        {
            float y;
            if (a.Kind == AnimalKind.Duck)
            {
                float level = terrain.WaterLevelAt(a.Position.x, a.Position.y);
                y = (level > 0f ? level : terrain.SurfaceAt(a.Position.x, a.Position.y)) - 0.0012f + Mathf.Sin(Time.realtimeSinceStartup * 2.2f + a.Home.x * 40f) * 0.0003f;
            }
            else y = terrain.SurfaceAt(a.Position.x, a.Position.y);
            var normal = terrain.NormalAt(a.Position.x, a.Position.y);
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
    }
}
