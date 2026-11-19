using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    [Serializable]
    public struct HouseInstance
    {
        public float x, z, rotation;
        public int variant;
        public int style;   // colour style (0-3); older saves load as 0
    }

    /// <summary>
    /// Placed houses: sit on the terrain at the lowest footprint point (the stone plinth reaches into the ground on
    /// slopes), re-sit after sculpting, may not overlap each other or the railway, and avoid steep or rim ground.
    /// </summary>
    public sealed class BuildingSystem : MonoBehaviour
    {
        public const float MaxSlopeRange = 0.09f;   // houses cut and fill a pad, so moderate slopes are fine
        public readonly List<HouseInstance> Houses = new();
        private readonly List<MeshRenderer> renderers = new();
        private readonly List<Transform> rotors = new();
        private readonly List<Transform[]> gondolas = new();
        private readonly List<MeshRenderer[]> neon = new();   // glowing sign letters (the disco), switched on and off
        private TerrainField terrain;
        private Material material;
        private MaterialPropertyBlock block;
        private int highlighted = -1;
        private readonly List<float> glow = new();   // window glow per building (NaN: the material's own)
        private readonly Dictionary<int, Material> glowMaterials = new();   // shared variants of the material, one per glow level
        private static readonly int HighlightId = Shader.PropertyToID("_Highlight"), GlowId = Shader.PropertyToID("_EmissionStrength");

        /// <summary>Bumped whenever buildings are added, removed or replaced (caches of places refresh on it).</summary>
        public int Version { get; private set; }

        /// <summary>Whether the fair's rides are running (0-1; they wind down at night).</summary>
        public float RidesOpen = 1f;
        private float rideSpeed = 1f, rideClock;
        /// <summary>Whether balloons take off (by day): one aloft finishes its flight first.</summary>
        public bool BalloonsFly = true;
        private readonly List<float> flights = new();   // each balloon's time into its flight
        public const float FlightSeconds = 100f;

        public void Init(TerrainField field, Material lit) { terrain = field; material = lit; block = new MaterialPropertyBlock(); }

        public void SetHouses(IEnumerable<HouseInstance> houses)
        {
            Houses.Clear(); Houses.AddRange(houses);
            Rebuild();
        }

        public void Rebuild()
        {
            foreach (var r in renderers) if (r) DestroyObject(r.gameObject);
            renderers.Clear(); rotors.Clear(); gondolas.Clear(); glow.Clear(); flights.Clear(); neon.Clear();
            foreach (var h in Houses) AddRenderer(h);
            Resit();
            highlighted = -1;
            Version++;
        }

        private void AddRenderer(HouseInstance h)
        {
            var r = CreateRenderer(h, transform, material);
            renderers.Add(r);
            glow.Add(float.NaN);
            var bp = Catalog.Get(h.variant);
            Transform rotor = null;
            var rotorMesh = HouseFactory.Rotor(h.variant);
            if (rotorMesh != null)
            {
                rotor = Child(r.transform, rotorMesh, "Rotor");
                rotor.localPosition = bp.RotorPivot;
            }
            rotors.Add(rotor);
            flights.Add(rotors.Count * 37f % FlightSeconds * 0.1f);
            Transform[] cabins = null;
            if (bp.Gondolas > 0)
            {
                cabins = new Transform[bp.Gondolas];
                for (int g = 0; g < bp.Gondolas; g++) cabins[g] = Child(r.transform, HouseFactory.Gondola(), "Gondola");
            }
            gondolas.Add(cabins);
            var signs = HouseFactory.Neon(h.variant);
            MeshRenderer[] lights = null;
            if (signs != null)
            {
                lights = new MeshRenderer[signs.Length];
                for (int k = 0; k < signs.Length; k++)
                {
                    var part = Child(r.transform, signs[k], "Neon");
                    lights[k] = part.GetComponent<MeshRenderer>();
                    lights[k].shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            neon.Add(lights);
            Animate(Time.time, rideClock, renderers.Count - 1);
        }

        private Transform Child(Transform parent, Mesh mesh, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material; mr.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        private void Update()
        {
            long t0 = SimCost.Now;
            rideSpeed = Mathf.MoveTowards(rideSpeed, RidesOpen, Time.deltaTime * 0.25f);   // rides wind down and start up gently
            rideClock += Time.deltaTime * rideSpeed;
            StepFlights(Time.deltaTime);
            Animate(Time.time, rideClock);
            SimCost.Add(SimCost.Buildings, t0);
        }

        /// <summary>How many of a building's neon letters shine now (-1: it has no neon sign).</summary>
        public int NeonLit(int index)
        {
            var lights = index >= 0 && index < neon.Count ? neon[index] : null;
            if (lights == null) return -1;
            int n = 0; foreach (var l in lights) if (l.enabled) n++;
            return n;
        }

        /// <summary>
        /// Shows just the buildings in the world window (<paramref name="shown"/>: a centre and a reach): the rest are
        /// neither drawn nor animated - they are all still there, for the people and the traffic.
        /// </summary>
        public void Cull(System.Func<Vector2, float, bool> shown)
        {
            for (int i = 0; i < renderers.Count && i < Houses.Count; i++)
            {
                if (!renderers[i]) continue;
                var h = Houses[i];
                var half = Catalog.Get(h.variant).Half;
                bool show = shown(new Vector2(h.x, h.z), half.magnitude);   // (its radius: see WonderlandWorld.ShowsWhole)
                if (renderers[i].gameObject.activeSelf != show) renderers[i].gameObject.SetActive(show);
            }
        }

        /// <summary>Turns the carousels, sails and wheels (cabins stay upright). Rides follow their own clock.</summary>
        public void Animate(float time, float rides, int only = -1)
        {
            for (int i = only < 0 ? 0 : only; i < (only < 0 ? rotors.Count : only + 1); i++)
            {
                if (i < renderers.Count && renderers[i] && !renderers[i].gameObject.activeSelf) continue;   // (out of the window)
                var lights = i < neon.Count ? neon[i] : null;
                if (lights != null)
                    for (int k = 0; k < lights.Length; k++)
                    {
                        bool on = HouseFactory.NeonOn(k, lights.Length, time + i * 1.3f);
                        if (lights[k].enabled != on) lights[k].enabled = on;
                    }
                if (rotors[i] == null) continue;
                var bp = Catalog.Get(Houses[i].variant);
                if (bp.Id == Catalog.Balloon) { rotors[i].localPosition = Flight(flights[i], i, out var sway); rotors[i].localRotation = sway; continue; }
                float angle = (bp.Ride ? rides : time) * bp.RotorSpeed + i * 37f;
                rotors[i].localRotation = Quaternion.AngleAxis(angle, bp.RotorAxis);
                var cabins = gondolas[i];
                if (cabins == null) continue;
                for (int g = 0; g < cabins.Length; g++)
                {
                    float a = (angle + g * 360f / cabins.Length) * Mathf.Deg2Rad;
                    cabins[g].localPosition = bp.RotorPivot + new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f) * bp.GondolaRadius;
                }
            }
        }

        /// <summary>Balloons: a flight takes 100 s; on the ground they wait for the day (and a flight under way always lands).</summary>
        public void StepFlights(float dt)
        {
            for (int i = 0; i < flights.Count && i < Houses.Count; i++)
            {
                if (Houses[i].variant != Catalog.Balloon) continue;
                float t = flights[i] % FlightSeconds;
                if (!BalloonsFly && t < FlightSeconds * 0.1f) continue;   // grounded for the night
                flights[i] = t + dt;
            }
        }

        /// <summary>Whether a balloon is up in the air.</summary>
        public bool Aloft(int i) => i >= 0 && i < flights.Count && flights[i] % FlightSeconds > FlightSeconds * 0.12f;

        /// <summary>
        /// Where the balloon is (in its launch field's frame) a time into its flight: ten seconds on the ground, a gentle
        /// rise to 45 cm, a wide loop drifting over the countryside (a third of a metre out), and down again.
        /// </summary>
        public static Vector3 Flight(float t, int seed, out Quaternion sway)
        {
            float u = Mathf.Repeat(t, FlightSeconds) / FlightSeconds;
            float air = Mathf.InverseLerp(0.1f, 1f, u);
            float height = 0.45f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.3f, u)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 1f, u)));
            float r = 0.33f * Mathf.Sin(air * Mathf.PI);
            float a = air * Mathf.PI * 1.7f + seed * 1.3f;
            sway = Quaternion.Euler(Mathf.Sin(t * 0.7f) * 3f * air, t * 4f, Mathf.Cos(t * 0.53f) * 3f * air);
            return new Vector3(Mathf.Cos(a) * r - Mathf.Cos(seed * 1.3f) * r, height, Mathf.Sin(a) * r - Mathf.Sin(seed * 1.3f) * r);
        }

        /// <summary>The turning part of a placed object (carousel riders sit on it), or null.</summary>
        public Transform RotorOf(int index) => index >= 0 && index < rotors.Count ? rotors[index] : null;

        /// <summary>Where a placed object's activity spot is, in world-root space, and the yaw a person there faces.</summary>
        /// <summary>A point in a building's own frame (front -Z) in the world's frame, standing where the building stands.</summary>
        public Vector3 LocalToWorld(int index, Vector3 local)
        {
            var h = Houses[index];
            var p = Quaternion.Euler(0f, h.rotation, 0f) * local;
            return new Vector3(h.x + p.x, renderers[index].transform.localPosition.y + p.y, h.z + p.z);
        }

        /// <summary>About the top of a building (smoke from its roof).</summary>
        public float TopOf(int index) => renderers[index].transform.localPosition.y + Catalog.Get(Houses[index].variant).Height * 0.85f;

        public Vector3 SpotPosition(int index, int spot, out float yaw)
        {
            var h = Houses[index];
            var sp = Catalog.Get(h.variant).Spots[spot];
            var rot = Quaternion.Euler(0f, h.rotation, 0f);
            var p = rot * new Vector3(sp.At.x, 0f, sp.At.z);
            yaw = h.rotation + sp.Facing;
            return new Vector3(h.x + p.x, renderers[index].transform.localPosition.y + sp.At.y, h.z + p.z);
        }

        public static MeshRenderer CreateRenderer(HouseInstance h, Transform parent, Material material)
        {
            var go = new GameObject(HouseFactory.Name(h.variant));
            go.transform.SetParent(parent, false);
            var mesh = HouseFactory.Get(h.variant, h.style);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On;
            // Its shadow from a simple stand-in in the same shape (never seen) - unless it is too small to matter.
            if (!Catalog.Get(h.variant).Small && Catalog.BoatOf(h.variant) < 0)
                MeshProxy.Shadow(r, MeshProxy.Building(mesh, HouseFactory.SignMesh(h.variant)));
            Lod.Attach(r, d => HouseFactory.Get(h.variant, h.style, d), Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z));
            return r;
        }

        private static void DestroyObject(UnityEngine.Object go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        public void Resit()
        {
            for (int i = 0; i < Houses.Count; i++)
            {
                var h = Houses[i];
                renderers[i].transform.localPosition = new Vector3(h.x, SitHeight(h, out _), h.z);
                renderers[i].transform.localRotation = Quaternion.Euler(0f, h.rotation, 0f);
            }
        }

        public static Vector2[] FootprintSamples(HouseInstance h)
        {
            var e = HouseFactory.HalfExtents(h.variant) * 0.85f;
            var rot = Quaternion.Euler(0f, h.rotation, 0f);
            var result = new Vector2[9];
            int k = 0;
            for (int sx = -1; sx <= 1; sx++)
            for (int sz = -1; sz <= 1; sz++)
            {
                var p = rot * new Vector3(sx * e.x, 0f, sz * e.y);
                result[k++] = new Vector2(h.x + p.x, h.z + p.z);
            }
            return result;
        }

        /// <summary>The house's ground level: the footprint's average height (its pad is graded to exactly this).</summary>
        public float SitHeight(HouseInstance h, out float range)
        {
            if (h.variant == Catalog.Jetty)
            {
                // Level with its bank (its land end), and never below the water it reaches over.
                var back = new Vector3(h.x, 0f, h.z) + Quaternion.Euler(0f, h.rotation, 0f) * new Vector3(0f, 0f, Catalog.Get(Catalog.Jetty).Half.y * 0.8f);
                var tip = new Vector3(h.x, 0f, h.z) + Quaternion.Euler(0f, h.rotation, 0f) * new Vector3(0f, 0f, -Catalog.Get(Catalog.Jetty).Half.y * 0.8f);
                range = 0f;
                return Mathf.Max(terrain.SurfaceAt(back.x, back.z) - 0.003f, terrain.WaterLevelAt(tip.x, tip.z) + 0.001f);
            }
            float min = float.MaxValue, max = float.MinValue, sum = 0f;
            var samples = FootprintSamples(h);
            foreach (var p in samples)
            {
                float y = terrain.SurfaceAt(p.x, p.y);
                min = Mathf.Min(min, y); max = Mathf.Max(max, y); sum += y;
            }
            range = max - min;
            return sum / samples.Length;
        }

        public static float Radius(int variant) => HouseFactory.HalfExtents(variant).magnitude;

        /// <summary>Why a house cannot go here, or null when it can.</summary>
        public string Validate(HouseInstance h, int ignore, Func<Vector2, float> trackDistance)
        {
            bool station = Catalog.IsStation(h.variant);   // a station stands beside the rails on purpose
            var bp = Catalog.Get(h.variant);
            foreach (var p in FootprintSamples(h))
            {
                if (terrain.EdgeAt(p.x, p.y) < 0.5f) return "Too close to the edge";
                if (terrain.WaterAt(p.x, p.y) > 0.2f && h.variant != Catalog.Jetty) return "That's water";   // (a landing stage reaches out over it)
                if (!station && trackDistance(p) < TrackSystem.BedHalfWidth + 0.012f) return "That's the railway";
            }
            if (!station && trackDistance(new Vector2(h.x, h.z)) < TrackSystem.BedHalfWidth + 0.03f) return "That's the railway";
            SitHeight(h, out float range);
            if (h.variant != Catalog.Jetty && range > (bp.Pad ? MaxSlopeRange : MaxSlopeRange * 1.6f)) return "Too steep here - smooth it a little first";
            float r = Radius(h.variant) * (bp.Small ? 0.7f : 0.82f);
            for (int i = 0; i < Houses.Count; i++)
            {
                if (i == ignore) continue;
                var other = Catalog.Get(Houses[i].variant);
                float min = r + Radius(Houses[i].variant) * (other.Small ? 0.7f : 0.82f);
                if (bp.Small || other.Small) min *= 0.85f;   // details may snuggle up to buildings
                if (new Vector2(Houses[i].x - h.x, Houses[i].z - h.z).sqrMagnitude < min * min) return bp.Small ? "Something is in the way" : "Too close to another building";
            }
            return null;
        }

        public void Add(HouseInstance h)
        {
            Houses.Add(h);
            AddRenderer(h);
            Resit();
            Version++;
        }

        public HouseInstance RemoveAt(int index)
        {
            var h = Houses[index];
            Houses.RemoveAt(index);
            DestroyObject(renderers[index].gameObject);
            renderers.RemoveAt(index); rotors.RemoveAt(index); gondolas.RemoveAt(index); glow.RemoveAt(index); neon.RemoveAt(index);
            highlighted = -1;
            Version++;
            return h;
        }

        /// <summary>Nearest house whose footprint (plus height) the local ray passes through.</summary>
        public int Pick(Vector3 origin, Vector3 direction) => Pick(origin, direction, out _);

        /// <summary>
        /// The building the ray hits first, and how far along the ray (<paramref name="bestT"/>, in units of the
        /// direction's length). <paramref name="walls"/>: only the lower part of each box (its walls; a roof slopes away,
        /// so the top of the box is mostly air) - for whether a building hides what is behind it.
        /// </summary>
        public int Pick(Vector3 origin, Vector3 direction, out float bestT, bool walls = false)
        {
            int best = -1; bestT = float.MaxValue;
            for (int i = 0; i < Houses.Count; i++)
            {
                var h = Houses[i];
                var e = HouseFactory.HalfExtents(h.variant);
                var inv = Quaternion.Inverse(Quaternion.Euler(0f, h.rotation, 0f));
                var y = renderers[i].transform.localPosition.y;
                var o = inv * (origin - new Vector3(h.x, y, h.z)); var d = inv * direction;
                float hh = Mathf.Max(0.03f, Catalog.Get(h.variant).Height) * (walls ? 0.55f : 1f);
                var box = new Bounds(new Vector3(0f, hh / 2f, 0f), new Vector3(Mathf.Max(e.x, 0.012f) * 2f, hh, Mathf.Max(e.y, 0.012f) * 2f));
                if (box.IntersectRay(new Ray(o, d), out float t) && t < bestT) { bestT = t; best = i; }
            }
            return best;
        }

        public int Nearest(Vector2 p, float within)
        {
            int best = -1; float bestD = within * within;
            for (int i = 0; i < Houses.Count; i++)
            {
                float d = new Vector2(Houses[i].x - p.x, Houses[i].z - p.y).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public void Highlight(int index, Color colour)
        {
            if (highlighted == index && index < 0) return;
            int old = highlighted;
            highlighted = index;
            highlightColour = colour;
            if (old >= 0 && old < renderers.Count && old != index) ApplyBlock(old);
            if (index >= 0 && index < renderers.Count) ApplyBlock(index);
        }

        private Color highlightColour;

        /// <summary>
        /// How brightly a building's windows glow (NaN: the material's own): lit homes at night, dark empty ones. Levels
        /// share a handful of material variants (steps of 0.1), which batch like the base material.
        /// </summary>
        public void SetGlow(int index, float strength)
        {
            if (index < 0 || index >= glow.Count) return;
            if (!float.IsNaN(strength)) strength = Mathf.Round(strength * 10f) / 10f;
            float old = glow[index];
            if ((float.IsNaN(old) && float.IsNaN(strength)) || old == strength) return;
            glow[index] = strength;
            var m = float.IsNaN(strength) ? material : GlowMaterial(strength);
            renderers[index].sharedMaterial = m;
            foreach (Transform level in renderers[index].transform)   // its other levels of detail glow the same
                if (level.name.StartsWith("Detail")) level.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        private Material GlowMaterial(float strength)
        {
            int key = Mathf.RoundToInt(strength * 10f);
            if (glowMaterials.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(material) { name = $"{material.name} glow {strength:0.0}" };
            m.SetFloat(GlowId, strength);
            glowMaterials[key] = m;
            return m;
        }

        private void OnDestroy()
        {
            foreach (var m in glowMaterials.Values) if (m != null) DestroyObject(m);
        }

        public float GlowOf(int index) => index >= 0 && index < glow.Count ? glow[index] : float.NaN;

        private void ApplyBlock(int i)
        {
            if (i != highlighted) { renderers[i].SetPropertyBlock(null); return; }
            block.Clear();
            block.SetColor(HighlightId, highlightColour);
            renderers[i].SetPropertyBlock(block);
        }

        public bool Contains(Vector2 p, float margin)
        {
            foreach (var h in Houses)
                if (new Vector2(h.x - p.x, h.z - p.y).magnitude < Radius(h.variant) * 0.8f + margin) return true;
            return false;
        }
    }
}
