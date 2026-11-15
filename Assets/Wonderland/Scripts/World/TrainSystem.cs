using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// The locomotive and its two coaches running along the railway. Each car is posed from two points on the track
    /// (its bogie pivots), so it turns into curves and tilts on grades; wheels spin with distance travelled, side rods
    /// follow the cranks and the chimney puffs steam in time with speed. On an open line the train eases to a stop at
    /// the buffers and reverses.
    /// </summary>
    public sealed class TrainSystem : MonoBehaviour
    {
        public const float CruiseSpeed = 0.13f;      // m/s: a gentle scale speed
        public const float Acceleration = 0.07f;
        private const float Gap = 0.006f;

        public bool Placed { get; private set; }
        public bool Running { get; set; }
        public float S;                                // arc length of the locomotive centre
        public float Direction = 1f;
        public float Speed { get; private set; }
        public float Odometer { get; private set; }

        private TrackLayout layout;
        /// <summary>Platform centres (arc length) where the train halts.</summary>
        public readonly List<float> Stops = new();
        /// <summary>Whether an arc length is inside a tunnel (no steam underground).</summary>
        public System.Func<float, bool> InTunnel;
        public const float DwellSeconds = 3.5f;
        public float Dwell { get; private set; }
        private int departedFrom = -1;
        private Car[] cars;
        private ParticleSystem smoke;
        private Material lit, smokeMaterial;
        private float chuff;

        private sealed class Car
        {
            public Transform Body;
            public Transform[] Axles;
            public float[] Radius;
            public Transform Rods;
            public CarLayout Layout;
            public float Offset;                       // arc-length offset from the locomotive centre
            public float Distance;                     // signed distance travelled, drives wheel rotation
        }

        public void Init(Material litMaterial, Material smokeMat) { lit = litMaterial; smokeMaterial = smokeMat; }

        public static float TrainLength => TrainFactory.Loco.Front - TrainFactory.Loco.Rear + (TrainFactory.Coach.Front - TrainFactory.Coach.Rear) * 2f + Gap * 2f;

        public bool Place(TrackLayout track, float s)
        {
            if (track == null || track.Length < TrainLength + 0.1f) return false;
            layout = track;
            if (cars == null) Build();
            S = track.Wrap(s);
            if (!track.Closed) S = Mathf.Clamp(S, TrainLength - TrainFactory.Loco.Front + 0.02f, track.Length - TrainFactory.Loco.Front - 0.02f);
            Placed = true; Speed = 0f; Direction = 1f;
            foreach (var c in cars) c.Body.gameObject.SetActive(true);
            Pose();
            return true;
        }

        /// <summary>Swaps in a re-profiled layout of the same route (sculpting under the track) without disturbing the run.</summary>
        public void SwapLayout(TrackLayout track)
        {
            if (!Placed || track == null) return;
            layout = track;
            S = track.Wrap(S);
            Pose();
        }

        /// <summary>Loco position that centres the whole train on a platform at <paramref name="centre"/>.</summary>
        private float StopPosition(float centre) =>
            centre - (TrainFactory.Loco.Front + cars[2].Offset + TrainFactory.Coach.Rear) * 0.5f * Direction;

        /// <summary>The next stop ahead in the running direction (not the one just left) and the distance to it.</summary>
        private int NextStop(out float ahead)
        {
            ahead = float.MaxValue; int best = -1;
            for (int i = 0; i < Stops.Count; i++)
            {
                float t = StopPosition(Stops[i]);
                float d = Direction > 0f ? t - S : S - t;
                if (layout.Closed) d = Mathf.Repeat(d, layout.Length);
                if (d < 0f) continue;
                if (i == departedFrom)
                {
                    if (d < layout.Length * 0.5f && d > 0.25f) departedFrom = -1;   // well clear of it again
                    else continue;
                }
                if (d < ahead) { ahead = d; best = i; }
            }
            return best;
        }

        public void Remove()
        {
            Placed = false; Running = false; Speed = 0f; layout = null;
            if (cars != null) foreach (var c in cars) c.Body.gameObject.SetActive(false);
            if (smoke) smoke.Clear();
        }

        private void Build()
        {
            float offset = 0f;
            cars = new Car[3];
            cars[0] = CreateCar(TrainFactory.LocoMesh, TrainFactory.Loco, offset, true);
            offset += TrainFactory.Loco.Rear - Gap + TrainFactory.Coach.Rear;
            cars[1] = CreateCar(TrainFactory.CoachMesh(0), TrainFactory.Coach, offset, false);
            offset += TrainFactory.Coach.Rear - Gap + TrainFactory.Coach.Rear;
            cars[2] = CreateCar(TrainFactory.CoachMesh(1), TrainFactory.Coach, offset, false);
            smoke = CreateSmoke(cars[0].Body);
        }

        private Car CreateCar(Mesh mesh, CarLayout carLayout, float offset, bool loco)
        {
            var body = Part(mesh.name, mesh, transform);
            var car = new Car { Body = body, Layout = carLayout, Offset = offset, Axles = new Transform[carLayout.Axles.Length], Radius = carLayout.WheelRadius };
            for (int i = 0; i < carLayout.Axles.Length; i++)
            {
                car.Axles[i] = Part("Axle", TrainFactory.AxleMesh(carLayout.WheelRadius[i]), body);
                car.Axles[i].localPosition = carLayout.Axles[i];
            }
            if (loco)
            {
                car.Rods = Part("Rods", TrainFactory.RodMesh, body);
                car.Rods.localPosition = new Vector3(0f, carLayout.Axles[0].y + carLayout.WheelRadius[0] * 0.45f, 0f);
            }
            return car;
        }

        private Transform Part(string name, Mesh mesh, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = lit; r.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        private ParticleSystem CreateSmoke(Transform loco)
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(loco, false);
            go.transform.localPosition = TrainFactory.Loco.Chimney;
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

        private void Update()
        {
            if (!Placed || layout == null) return;
            Step(Time.deltaTime);
        }

        /// <summary>Advances the simulation (also called by the scripted journey test).</summary>
        public void Step(float dt)
        {
            if (!Placed || layout == null) return;
            float target = Running ? CruiseSpeed : 0f;
            // Station stops: brake to halt with the train centred on the platform, wait, then pull away.
            if (Running && Stops.Count > 0)
            {
                if (Dwell > 0f)
                {
                    Dwell -= dt; Speed = 0f;
                    if (Dwell > 0f) { Pose(); return; }
                }
                int next = NextStop(out float ahead);
                if (next >= 0)
                {
                    float brake = Mathf.Sqrt(2f * Acceleration * Mathf.Max(0f, ahead - 0.001f));
                    target = Mathf.Min(target, Mathf.Max(brake, 0.004f));
                    if (ahead < 0.003f)
                    {
                        S = layout.Wrap(StopPosition(Stops[next]));
                        Speed = 0f; Dwell = DwellSeconds; departedFrom = next;
                        Pose(); return;
                    }
                }
            }
            Speed = Mathf.MoveTowards(Speed, target, Acceleration * dt);
            if (!layout.Closed)
            {
                // Brake for the buffers, then reverse.
                float front = Direction > 0f ? S + TrainFactory.Loco.Front : S + cars[2].Offset + TrainFactory.Coach.Rear;
                float remaining = Direction > 0f ? layout.Length - front : front;
                float brake = Mathf.Sqrt(2f * Acceleration * Mathf.Max(0f, remaining - 0.01f));
                if (Speed > brake) Speed = brake;
                if (Running && remaining < 0.012f && Speed < 0.005f) Direction = -Direction;
            }
            float ds = Speed * dt;
            S = layout.Wrap(S + ds * Direction);
            Odometer += ds;
            Pose();
            foreach (var c in cars)
            {
                c.Distance += ds * Direction;
                for (int i = 0; i < c.Axles.Length; i++)
                    c.Axles[i].localRotation = Quaternion.Euler(c.Distance / c.Radius[i] * Mathf.Rad2Deg, 0f, 0f);
                if (c.Rods)
                {
                    // The crank pin sits at +Y on each wheel; rotating by theta about X moves it to (cos, sin).
                    float angle = c.Distance / c.Radius[0], crank = c.Radius[0] * 0.45f;
                    c.Rods.localPosition = new Vector3(0f, c.Layout.Axles[0].y + Mathf.Cos(angle) * crank, Mathf.Sin(angle) * crank);
                }
            }
            if (smoke)
            {
                var emission = smoke.emission;
                chuff += ds / (TrainFactory.Loco.WheelRadius[0] * Mathf.PI * 0.5f);
                float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(chuff * Mathf.PI));
                emission.rateOverTime = InTunnel != null && InTunnel(S) ? 0f : 3f + 30f * (Speed / CruiseSpeed) * pulse;
            }
        }

        private void Pose()
        {
            foreach (var c in cars)
            {
                float centre = S + c.Offset;
                var front = layout.Evaluate(centre + c.Layout.PivotHalf, out _);
                var back = layout.Evaluate(centre - c.Layout.PivotHalf, out _);
                var forward = front - back;
                if (forward.sqrMagnitude < 1e-8f) continue;
                c.Body.localPosition = (front + back) * 0.5f + Vector3.up * TrackSystem.RailTop;
                c.Body.localRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        /// <summary>Whether a local point is on (near) the train, for picking.</summary>
        public bool Near(Vector3 p, float within)
        {
            if (!Placed) return false;
            foreach (var c in cars)
                if (new Vector2(c.Body.localPosition.x - p.x, c.Body.localPosition.z - p.z).magnitude < within) return true;
            return false;
        }

        public Vector3 LocoPosition => cars != null ? cars[0].Body.localPosition : Vector3.zero;
        public Quaternion LocoRotation => cars != null ? cars[0].Body.localRotation : Quaternion.identity;
    }
}
