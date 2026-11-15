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
        private TerrainField terrain;
        private Material material;
        private MaterialPropertyBlock block;
        private int highlighted = -1;
        private static readonly int HighlightId = Shader.PropertyToID("_Highlight");

        public void Init(TerrainField field, Material lit) { terrain = field; material = lit; block = new MaterialPropertyBlock(); }

        public void SetHouses(IEnumerable<HouseInstance> houses)
        {
            Houses.Clear(); Houses.AddRange(houses);
            Rebuild();
        }

        public void Rebuild()
        {
            foreach (var r in renderers) if (r) DestroyObject(r.gameObject);
            renderers.Clear();
            foreach (var h in Houses) renderers.Add(CreateRenderer(h, transform, material));
            Resit();
            highlighted = -1;
        }

        public static MeshRenderer CreateRenderer(HouseInstance h, Transform parent, Material material)
        {
            var go = new GameObject("House " + HouseFactory.Names[h.variant]);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = HouseFactory.Get(h.variant, h.style);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On;
            return r;
        }

        private static void DestroyObject(GameObject go)
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
            bool station = h.variant == HouseFactory.Station;   // a station stands beside the rails on purpose
            foreach (var p in FootprintSamples(h))
            {
                if (terrain.EdgeAt(p.x, p.y) < 0.5f) return "Too close to the edge";
                if (terrain.WaterAt(p.x, p.y) > 0.2f) return "That's water";
                if (!station && trackDistance(p) < TrackSystem.BedHalfWidth + 0.012f) return "That's the railway";
            }
            if (!station && trackDistance(new Vector2(h.x, h.z)) < TrackSystem.BedHalfWidth + 0.03f) return "That's the railway";
            SitHeight(h, out float range);
            if (range > MaxSlopeRange) return "Too steep here - smooth it a little first";
            float r = Radius(h.variant) * 0.82f;
            for (int i = 0; i < Houses.Count; i++)
            {
                if (i == ignore) continue;
                float min = r + Radius(Houses[i].variant) * 0.82f;
                if (new Vector2(Houses[i].x - h.x, Houses[i].z - h.z).sqrMagnitude < min * min) return "Too close to another house";
            }
            return null;
        }

        public void Add(HouseInstance h)
        {
            Houses.Add(h);
            renderers.Add(CreateRenderer(h, transform, material));
            Resit();
        }

        public HouseInstance RemoveAt(int index)
        {
            var h = Houses[index];
            Houses.RemoveAt(index);
            DestroyObject(renderers[index].gameObject);
            renderers.RemoveAt(index);
            highlighted = -1;
            return h;
        }

        /// <summary>Nearest house whose footprint (plus height) the local ray passes through.</summary>
        public int Pick(Vector3 origin, Vector3 direction)
        {
            int best = -1; float bestT = float.MaxValue;
            for (int i = 0; i < Houses.Count; i++)
            {
                var h = Houses[i];
                var e = HouseFactory.HalfExtents(h.variant);
                var inv = Quaternion.Inverse(Quaternion.Euler(0f, h.rotation, 0f));
                var y = renderers[i].transform.localPosition.y;
                var o = inv * (origin - new Vector3(h.x, y, h.z)); var d = inv * direction;
                var box = new Bounds(new Vector3(0f, 0.075f, 0f), new Vector3(e.x * 2f, 0.16f, e.y * 2f));
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
            for (int i = 0; i < renderers.Count; i++)
            {
                if (i == index) { block.SetColor(HighlightId, colour); renderers[i].SetPropertyBlock(block); }
                else if (i == highlighted) renderers[i].SetPropertyBlock(null);
            }
            highlighted = index;
        }

        public bool Contains(Vector2 p, float margin)
        {
            foreach (var h in Houses)
                if (new Vector2(h.x - p.x, h.z - p.y).magnitude < Radius(h.variant) * 0.8f + margin) return true;
            return false;
        }
    }
}
