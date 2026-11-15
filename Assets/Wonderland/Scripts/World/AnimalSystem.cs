using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    [Serializable]
    public struct AnimalInstance
    {
        public float x, z;     // home: where it was placed; it wanders around here
        public int kind;
        public int seed;
    }

    /// <summary>
    /// Sheep and cows grazing the meadows. Each wanders around its home spot: grazes head-down for a while, picks a nearby
    /// spot of free grass, turns, ambles over with a gentle bob, and grazes again. Free grass means land, away from the
    /// shore, water, the railway corridor and houses. Homes are saved; positions are runtime only.
    /// </summary>
    public sealed class AnimalSystem : MonoBehaviour
    {
        public const float WanderRadius = 0.22f;
        public readonly List<AnimalInstance> Animals = new();
        private readonly List<Agent> agents = new();
        private TerrainField terrain;
        private Material material;
        private Func<Vector2, float, bool> isFree;

        private sealed class Agent
        {
            public Transform Body, Head;
            public AnimalKind Kind;
            public Vector2 Home, Position, Target;
            public float Heading, Timer, Bob, Pitch;
            public bool Walking;
            public Rng Rng;
        }

        public void Init(TerrainField field, Material lit, Func<Vector2, float, bool> free) { terrain = field; material = lit; isFree = free; }

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
            var kind = (AnimalKind)a.kind;
            var body = Part(kind + " body", AnimalFactory.Body(kind), transform);
            var head = Part("Head", AnimalFactory.Head(kind), body);
            head.localPosition = AnimalFactory.NeckOffset(kind);
            var rng = new Rng(a.seed);
            var agent = new Agent { Body = body, Head = head, Kind = kind, Home = new Vector2(a.x, a.z), Position = new Vector2(a.x, a.z), Heading = rng.Range(0f, 360f), Timer = rng.Range(0.5f, 4f), Rng = rng };
            agent.Target = agent.Position;
            Pose(agent, 0f);
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
                float speed = a.Kind == AnimalKind.Sheep ? 0.014f : 0.011f;
                a.Timer -= dt;
                if (!a.Walking)
                {
                    a.Pitch = Mathf.MoveTowards(a.Pitch, 38f, 60f * dt);   // head down, nibbling
                    if (a.Timer <= 0f && PickTarget(a)) { a.Walking = true; a.Timer = 12f; }
                    else if (a.Timer <= 0f) a.Timer = a.Rng.Range(1f, 3f);
                }
                else
                {
                    a.Pitch = Mathf.MoveTowards(a.Pitch, 0f, 90f * dt);
                    var to = a.Target - a.Position;
                    float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                    a.Heading = Mathf.MoveTowardsAngle(a.Heading, want, 120f * dt);
                    if (Mathf.Abs(Mathf.DeltaAngle(a.Heading, want)) < 25f)
                    {
                        var fwd = new Vector2(Mathf.Sin(a.Heading * Mathf.Deg2Rad), Mathf.Cos(a.Heading * Mathf.Deg2Rad));
                        var next = a.Position + fwd * Mathf.Min(speed * dt, to.magnitude);
                        if (isFree(next, AnimalFactory.Radius(a.Kind))) { a.Position = next; a.Bob += dt * 14f; }
                        else a.Timer = 0f;   // blocked: stop and graze
                    }
                    if (to.magnitude < 0.004f || a.Timer <= 0f) { a.Walking = false; a.Timer = a.Rng.Range(2.5f, 7f); }
                }
                Pose(a, dt);
            }
        }

        private bool PickTarget(Agent a)
        {
            for (int k = 0; k < 6; k++)
            {
                var t = a.Home + a.Rng.InsideUnitCircle() * WanderRadius;
                if ((t - a.Position).magnitude < 0.03f) continue;
                if (!isFree(t, AnimalFactory.Radius(a.Kind))) continue;
                a.Target = t;
                return true;
            }
            return false;
        }

        private void Pose(Agent a, float dt)
        {
            float y = terrain.SurfaceAt(a.Position.x, a.Position.y);
            var normal = terrain.NormalAt(a.Position.x, a.Position.y);
            float bob = a.Walking ? Mathf.Abs(Mathf.Sin(a.Bob)) * 0.0007f : 0f;
            float roll = a.Walking ? Mathf.Sin(a.Bob) * 3f : 0f;
            a.Body.localPosition = new Vector3(a.Position.x, y + bob, a.Position.y);
            a.Body.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, normal, 0.5f)) * Quaternion.Euler(0f, a.Heading, roll);
            float nibble = a.Walking ? 0f : Mathf.Sin(Time.realtimeSinceStartup * 7f + a.Home.x * 50f) * 4f;
            a.Head.localRotation = Quaternion.Euler(a.Pitch + nibble, 0f, 0f);
        }
    }
}
