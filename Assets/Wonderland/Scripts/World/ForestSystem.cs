using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    [Serializable]
    public struct TreeInstance
    {
        public float x, z, scale, rotation;
        public byte kind, variant;
        public float tint;   // brightness variation
    }

    /// <summary>
    /// Painted vegetation. Trees live in a flat list (saved, undoable) and are drawn as combined meshes per 25 cm chunk,
    /// so a forest of hundreds of trees costs a few dozen draw calls and only edited chunks are rebuilt. Species follow a
    /// coherent "biome" noise (spruce groves on high ground, oaks and blossoms in the meadows, cypresses as accents,
    /// bushes as undergrowth), each tree varying in variant, size, rotation and tint.
    /// </summary>
    public sealed class ForestSystem : MonoBehaviour
    {
        public const int MaxTrees = 1400;
        private const float ChunkSize = 0.25f;

        public readonly List<TreeInstance> Trees = new();
        private readonly Dictionary<int, (GameObject go, Mesh mesh)> chunks = new();
        private readonly HashSet<int> dirty = new();
        private readonly Dictionary<int, MeshData> meshData = new();
        private TerrainField terrain;
        private Material material;
        private Rng rng = new(4242);
        private int biomeSeed;

        public void Init(TerrainField field, Material lit, int seed)
        {
            terrain = field; material = lit; biomeSeed = seed;
        }

        private static int ChunkKey(float x, float z) => (Mathf.FloorToInt(x / ChunkSize) + 64) * 1000 + Mathf.FloorToInt(z / ChunkSize) + 64;

        public void MarkDirty(float x, float z) => dirty.Add(ChunkKey(x, z));

        public void MarkDirtyRegion(Vector2 centre, float radius)
        {
            for (float x = centre.x - radius - ChunkSize; x <= centre.x + radius + ChunkSize; x += ChunkSize * 0.5f)
            for (float z = centre.y - radius - ChunkSize; z <= centre.y + radius + ChunkSize; z += ChunkSize * 0.5f)
                dirty.Add(ChunkKey(x, z));
        }

        public void MarkAllDirty()
        {
            foreach (var k in chunks.Keys) dirty.Add(k);
            foreach (var t in Trees) dirty.Add(ChunkKey(t.x, t.z));
        }

        private void LateUpdate() => RebuildDirty();

        public void RebuildDirty()
        {
            if (dirty.Count == 0) return;
            var byChunk = new Dictionary<int, List<TreeInstance>>();
            foreach (var t in Trees)
            {
                int k = ChunkKey(t.x, t.z);
                if (!dirty.Contains(k)) continue;
                if (!byChunk.TryGetValue(k, out var list)) byChunk[k] = list = new List<TreeInstance>();
                list.Add(t);
            }
            foreach (int k in dirty)
            {
                byChunk.TryGetValue(k, out var list);
                if (list == null || list.Count == 0)
                {
                    if (chunks.TryGetValue(k, out var empty)) { Release(empty.go); Release(empty.mesh); chunks.Remove(k); }
                    continue;
                }
                if (!chunks.TryGetValue(k, out var chunk))
                {
                    var go = new GameObject("Forest chunk " + k);
                    go.transform.SetParent(transform, false);
                    go.AddComponent<MeshFilter>();
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                    chunk = (go, new Mesh { name = go.name });
                    go.GetComponent<MeshFilter>().sharedMesh = chunk.mesh;
                    chunks[k] = chunk;
                }
                var b = new MeshBuilder();
                foreach (var t in list)
                {
                    float y = terrain.SurfaceAt(t.x, t.z) - 0.003f * t.scale;
                    var m = Matrix4x4.TRS(new Vector3(t.x, y, t.z), Quaternion.Euler(0f, t.rotation, 0f), Vector3.one * t.scale);
                    b.Append(Data((TreeKind)t.kind, t.variant), m, new Color(t.tint, t.tint, t.tint * 0.97f));
                }
                b.ToMesh(chunk.go.name, chunk.mesh);
                chunk.mesh.RecalculateBounds();
            }
            dirty.Clear();
        }

        private static void Release(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else DestroyImmediate(o);
        }

        private MeshData Data(TreeKind kind, int variant)
        {
            int key = (int)kind * 10 + variant;
            if (!meshData.TryGetValue(key, out var d)) meshData[key] = d = new MeshData(TreeFactory.Get(kind, variant));
            return d;
        }

        /// <summary>Species choice from position: coherent groves rather than random confetti.</summary>
        public TreeKind PickKind(float x, float z, ref Rng r)
        {
            float biome = Noise.Fbm2(x * 2.4f + biomeSeed * 0.37f, z * 2.4f - biomeSeed * 0.21f, 3);
            float blossom = Noise.Value2(x * 4.5f + 31f + biomeSeed, z * 4.5f - 7f);
            float altitude = terrain.SurfaceAt(x, z);
            float wConifer = Mathf.SmoothStep(0f, 1f, (biome - 0.45f) / 0.2f) * 1.4f + Mathf.Clamp01((altitude - 0.08f) * 8f);
            float wOak = Mathf.SmoothStep(1f, 0f, (biome - 0.35f) / 0.25f) * 1.2f + 0.2f;
            float wBlossom = Mathf.SmoothStep(0f, 1f, (blossom - 0.62f) / 0.12f) * 0.9f * (1f - Mathf.Clamp01(altitude * 6f));
            float wCypress = 0.16f;
            float wBush = 0.45f;
            float total = wConifer + wOak + wBlossom + wCypress + wBush;
            float pick = r.Value * total;
            if ((pick -= wConifer) < 0f) return TreeKind.Conifer;
            if ((pick -= wOak) < 0f) return TreeKind.Oak;
            if ((pick -= wBlossom) < 0f) return TreeKind.Blossom;
            if ((pick -= wCypress) < 0f) return TreeKind.Cypress;
            return TreeKind.Bush;
        }

        /// <summary>
        /// One frame of forest painting: a handful of placement attempts inside the brush, each accepted only where trees
        /// keep their spacing (density sets how close) and <paramref name="canPlace"/> allows. Returns trees added.
        /// </summary>
        public int Paint(Vector2 centre, float radius, float density, float dt, Func<Vector2, float, bool> canPlace)
        {
            float area = Mathf.PI * radius * radius;
            int attempts = Mathf.Max(1, Mathf.RoundToInt((25f + area * 900f) * dt * (0.4f + density)));
            float spacing = Mathf.Lerp(1.5f, 0.78f, density);
            int added = 0;
            for (int a = 0; a < attempts && Trees.Count < MaxTrees; a++)
            {
                var p = centre + rng.InsideUnitCircle() * radius;
                var kind = PickKind(p.x, p.y, ref rng);
                float scale = rng.Range(0.75f, 1.25f) * (kind == TreeKind.Bush ? rng.Range(0.8f, 1.3f) : 1f);
                float footprint = TreeFactory.Radius(kind) * scale;
                if (!canPlace(p, footprint)) continue;
                if (terrain.EdgeAt(p.x, p.y) < 0.35f) continue;
                if (Crowded(p, footprint, spacing)) continue;
                Trees.Add(new TreeInstance
                {
                    x = p.x, z = p.y, scale = scale, rotation = rng.Range(0f, 360f), kind = (byte)kind,
                    variant = (byte)rng.Range(0, TreeFactory.VariantCount), tint = rng.Range(0.88f, 1.1f),
                });
                MarkDirty(p.x, p.y);
                added++;
            }
            return added;
        }

        private bool Crowded(Vector2 p, float footprint, float spacing)
        {
            foreach (var t in Trees)
            {
                float dx = t.x - p.x, dz = t.z - p.y;
                float min = (footprint + TreeFactory.Radius((TreeKind)t.kind) * t.scale) * spacing;
                if (dx * dx + dz * dz < min * min) return true;
            }
            return false;
        }

        /// <summary>Gradual erasing: trees inside the brush disappear at a rate set by <paramref name="strength"/>.</summary>
        public int Erase(Vector2 centre, float radius, float strength, float dt)
        {
            int removed = 0;
            float p = Mathf.Clamp01(strength * dt * 7f);
            for (int i = Trees.Count - 1; i >= 0; i--)
            {
                var t = Trees[i];
                float d = new Vector2(t.x - centre.x, t.z - centre.y).magnitude / radius;
                if (d > 1f) continue;
                if (rng.Value < p * (1.3f - d)) { MarkDirty(t.x, t.z); Trees.RemoveAt(i); removed++; }
            }
            return removed;
        }

        public int RemoveWhere(Func<TreeInstance, bool> predicate)
        {
            int removed = 0;
            for (int i = Trees.Count - 1; i >= 0; i--)
                if (predicate(Trees[i])) { MarkDirty(Trees[i].x, Trees[i].z); Trees.RemoveAt(i); removed++; }
            return removed;
        }

        public void SetTrees(IEnumerable<TreeInstance> trees)
        {
            MarkAllDirty();
            Trees.Clear(); Trees.AddRange(trees);
            MarkAllDirty();
        }

        public int ChunkCount => chunks.Count;
    }
}
