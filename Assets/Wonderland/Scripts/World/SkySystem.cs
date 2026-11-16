using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// The sky over the miniature: storybook clouds (lumpy white blobs, no volumetrics) drifting a metre above the land
    /// on a wind that slowly turns, bobbing and casting slow shadows, wrapping round when they pass the land's edge.
    /// Now and then a shower: the clouds go grey, rain falls from the ones over the land (a light particle streak per
    /// cloud), the light dims, it can be heard, and villagers would rather be indoors. Fourteen clouds, one renderer
    /// each, one small particle system each (only while it rains). The Life tool's Clouds chip hides them, its Rain chip
    /// starts or stops a shower (clouds remembered between sessions; weather isn't saved).
    /// </summary>
    public sealed class SkySystem : MonoBehaviour
    {
        public const int Count = 14;
        public const float Height = 1.0f;
        private const string Key = "wonderland.clouds";

        private sealed class Cloud { public Transform T; public MeshRenderer R; public ParticleSystem Rain; public float Speed, Bob, Scale; public int Shape; }

        private readonly List<Cloud> clouds = new();
        private WonderlandWorld world;
        private bool shown = true;
        private Rect land;
        private float landTimer, windAngle, windTimer, clock, showerUntil = -1f, nextCheck;
        private Rng rng;
        private Material smoke;

        /// <summary>0 dry, 1 in full rain (eases in and out).</summary>
        public float Rain { get; private set; }
        public bool Raining => showerUntil > 0f;
        public Vector2 Wind => new(Mathf.Cos(windAngle), Mathf.Sin(windAngle));

        public bool Shown
        {
            get => shown;
            set { shown = value; PlayerPrefs.SetInt(Key, value ? 1 : 0); foreach (var c in clouds) c.T.gameObject.SetActive(value); }
        }

        public void Init(WonderlandWorld owner, Material lit, int seed)
        {
            world = owner;
            smoke = owner.Materials.Smoke;
            rng = new Rng(seed * 13 + 5);
            windAngle = rng.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Cloud");
                go.transform.SetParent(transform, false);
                var c = new Cloud { T = go.transform, Shape = i % 4, Speed = rng.Range(0.75f, 1.3f), Bob = rng.Range(0f, 10f), Scale = rng.Range(0.6f, 1.6f) };
                go.AddComponent<MeshFilter>().sharedMesh = CloudMesh(c.Shape, false);
                c.R = go.AddComponent<MeshRenderer>();
                c.R.sharedMaterial = lit; c.R.shadowCastingMode = ShadowCastingMode.On; c.R.receiveShadows = false;
                go.transform.localPosition = new Vector3(rng.Range(-1.2f, 1.2f), Height + rng.Range(0f, 0.3f), rng.Range(-1.2f, 1.2f));
                go.transform.localRotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                go.transform.localScale = Vector3.one * c.Scale;
                clouds.Add(c);
            }
            shown = PlayerPrefs.GetInt(Key, 1) == 1;
            foreach (var c in clouds) c.T.gameObject.SetActive(shown);
        }

        private static readonly Mesh[] meshes = new Mesh[8];

        private static Mesh CloudMesh(int v, bool grey)
        {
            int key = v + (grey ? 4 : 0);
            if (meshes[key] != null) return meshes[key];
            var b = new MeshBuilder();
            var r0 = new Rng(70 + v);
            int puffs = 5 + v;
            Color top = grey ? Pal.Hex("#C3C8D0") : Pal.Hex("#FFFFFF"), under = grey ? Pal.Hex("#7E8794") : Pal.Hex("#C9D2DE");
            for (int i = 0; i < puffs; i++)
            {
                float x = (i - (puffs - 1) / 2f) * 0.035f + r0.Range(-0.01f, 0.01f);
                float r = 0.03f + (1f - Mathf.Abs(x) / 0.12f) * 0.025f + r0.Range(0f, 0.01f);
                var c = new Vector3(x, r * 0.35f, r0.Range(-0.02f, 0.02f));
                b.AddBlob(c, new Vector3(r, r * 0.75f, r * 0.9f), 1, 0.08f, 80 + v * 10 + i, (d, _) => Color.Lerp(under, top, Mathf.SmoothStep(0f, 1f, d.y * 0.7f + 0.45f)));
            }
            return meshes[key] = b.ToMesh(grey ? "Raincloud" + v : "Cloud" + v);
        }

        private void Update() { if (world == null) return; long t0 = SimCost.Now; Step(Time.deltaTime); SimCost.Add(SimCost.Sky, t0); }

        /// <summary>Starts a shower now (or stops the one falling). Returns what happened, for the palette.</summary>
        public string ToggleRain()
        {
            if (Raining) { showerUntil = -1f; return "The rain stops"; }
            showerUntil = world.Clock.Total + rng.Range(2f, 3f);
            return "A shower! The clouds grey over";
        }

        /// <summary>A shower for this many village hours from now (tests and renders).</summary>
        public void StartShower(float hours) { shown = true; showerUntil = world.Clock.Total + hours; }

        /// <summary>Drifts the clouds on the turning wind, wraps them round the land, and brings the odd shower.</summary>
        public void Step(float dt)
        {
            clock += dt;
            landTimer -= dt;
            if (landTimer <= 0f) { landTimer = 2f; land = world.Terrain.LandBounds(); if (land.width <= 0f) land = new Rect(-1f, -1f, 2f, 2f); }
            // The wind veers slowly.
            windTimer -= dt;
            if (windTimer <= 0f) { windTimer = rng.Range(20f, 60f); }
            windAngle += Mathf.Sin(clock * 0.05f) * 0.02f * dt;
            // Weather: about one shower in ten village hours, by day or night; each lasting one and a half to three.
            float total = world.Clock.Total;
            if (showerUntil > 0f && total > showerUntil) showerUntil = -1f;
            if (showerUntil < 0f && total > nextCheck) { nextCheck = total + 1f; if (rng.Chance(0.1f)) showerUntil = total + rng.Range(1.5f, 3f); }
            float before = Rain;
            Rain = Mathf.MoveTowards(Rain, Raining && shown ? 1f : 0f, dt * 0.3f);
            world.People.Rain = Rain;
            if ((before < 0.5f) != (Rain < 0.5f)) foreach (var c in clouds) c.T.GetComponent<MeshFilter>().sharedMesh = CloudMesh(c.Shape, Rain >= 0.5f);
            if (!shown) return;
            var wind = Wind * 0.03f;
            var box = new Rect(land.xMin - 0.6f, land.yMin - 0.6f, land.width + 1.2f, land.height + 1.2f);
            foreach (var c in clouds)
            {
                var p = c.T.localPosition;
                p.x += wind.x * c.Speed * dt; p.z += wind.y * c.Speed * dt;
                p.y = Height + 0.15f + Mathf.Sin(clock * 0.15f + c.Bob) * 0.04f + c.Bob * 0.02f;
                // Gone past the edge downwind: come round again on the far side.
                if (p.x > box.xMax) p.x = box.xMin; else if (p.x < box.xMin) p.x = box.xMax;
                if (p.z > box.yMax) p.z = box.yMin; else if (p.z < box.yMin) p.z = box.yMax;
                c.T.localPosition = p;
                c.T.localScale = Vector3.one * c.Scale * (1f + Mathf.Sin(clock * 0.2f + c.Bob * 3f) * 0.04f);
                Shower(c, p);
            }
        }

        /// <summary>Rain falls from a grey cloud while it is over the land.</summary>
        private void Shower(Cloud c, Vector3 p)
        {
            bool over = Rain > 0.3f && land.Contains(new Vector2(p.x, p.z));
            if (over && c.Rain == null) c.Rain = MakeRain(c.T);
            if (c.Rain == null) return;
            var e = c.Rain.emission; e.rateOverTime = over ? 70f * Rain * c.Scale : 0f;
            if (over && !c.Rain.isPlaying) c.Rain.Play();
        }

        private ParticleSystem MakeRain(Transform cloud)
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(cloud, false);
            go.transform.localPosition = new Vector3(0f, -0.01f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // pointing down
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 0.8f; main.startSpeed = 1.3f; main.startSize = 0.0025f;
            main.startColor = new Color(0.75f, 0.85f, 1f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.maxParticles = 120;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(0.14f, 0.08f, 0.01f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 6f; r.velocityScale = 0f;
            r.sharedMaterial = smoke; r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        public Vector3 PositionOf(int i) => clouds[i].T.localPosition;
        public int Raindrops { get { int n = 0; foreach (var c in clouds) if (c.Rain != null) n += c.Rain.particleCount; return n; } }
        public bool RainFalling { get { foreach (var c in clouds) if (c.Rain != null && c.Rain.emission.rateOverTime.constant > 0f) return true; return false; } }
    }
}
