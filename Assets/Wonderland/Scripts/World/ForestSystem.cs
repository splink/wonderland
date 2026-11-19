using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>What a stroke of the forest brush plants.</summary>
    public enum ForestType { Mixed = 0, Leafy = 1, Pine = 2, Birch = 3, Autumn = 4, Orchard = 5, Riverside = 6, Palms = 7 }

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
        /// <summary>Trees in the whole world (it keeps saves and undo small; the window is what costs drawing).</summary>
        public const int MaxTrees = 12000;
        /// <summary>Trees the window can hold: what the headset draws at once.</summary>
        public const int MaxInView = 1500;
        private const float ChunkSize = 0.25f;

        public readonly List<TreeInstance> Trees = new();
        // The trees by chunk: for spacing, drawing a chunk and counting the window's without going through them all.
        private readonly Dictionary<long, List<TreeInstance>> cells = new();

        private void Reindex()
        {
            cells.Clear();
            foreach (var t in Trees) Cell(ChunkKey(t.x, t.z)).Add(t);
        }

        private List<TreeInstance> Cell(long k)
        {
            if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<TreeInstance>();
            return list;
        }

        /// <summary>The trees in the chunks the window shows (all of them when it shows everywhere).</summary>
        public int InView()
        {
            if (Shown == null) return Trees.Count;
            int n = 0;
            foreach (var (k, list) in cells) if (list.Count > 0 && Shown(ChunkCentre(k), ChunkSize)) n += list.Count;
            return n;
        }

        /// <summary>Why no more trees can be planted (null: they can).</summary>
        public string Full => Trees.Count >= MaxTrees ? "The world has as many trees as it can keep - erase some first"
            : InView() >= MaxInView ? "As many trees as the window can show - plant more elsewhere in the world, or erase some here" : null;
        private readonly Dictionary<long, (GameObject go, Mesh mesh)> chunks = new();
        private readonly HashSet<long> dirty = new();
        private readonly Dictionary<long, MeshData> meshData = new(), proxyData = new();
        private readonly Dictionary<long, Mesh> proxyMeshes = new();
        private readonly Dictionary<long, Mesh[]> levelMeshes = new();   // each chunk's simpler levels (detail 1, 2)
        private readonly MeshBuilder proxyBuilder = new();
        private TerrainField terrain;
        private Material material;
        private Rng rng = new(4242);
        private int biomeSeed;

        public void Init(TerrainField field, Material lit, int seed)
        {
            terrain = field; material = lit; biomeSeed = seed;
        }

        /// <summary>Whether a chunk (centre, half size) is near enough the world window to be built (null: everywhere).</summary>
        public System.Func<Vector2, float, bool> Shown;

        /// <summary>Shows just the chunks in the window: the trees outside it are neither drawn nor rebuilt.</summary>
        /// <remarks>Chunks far from the window (not <paramref name="kept"/>) are let go of altogether and built again when the
        /// window comes back to them.</remarks>
        public void Cull(System.Func<Vector2, float, bool> shown, System.Func<Vector2, float, bool> kept = null)
        {
            List<long> drop = null;
            foreach (var (k, c) in chunks)
            {
                var centre = ChunkCentre(k);
                if (kept != null && !kept(centre, ChunkSize)) { (drop ??= new List<long>()).Add(k); continue; }
                bool show = shown(centre, ChunkSize);
                if (c.go.activeSelf != show) c.go.SetActive(show);
            }
            if (drop != null) foreach (var k in drop) { Drop(k); dirty.Add(k); }
        }

        /// <summary>Lets go of a chunk's objects and meshes.</summary>
        private void Drop(long k)
        {
            if (chunks.TryGetValue(k, out var c)) { Release(c.go); Release(c.mesh); chunks.Remove(k); }
            if (proxyMeshes.TryGetValue(k, out var proxy)) { Release(proxy); proxyMeshes.Remove(k); }
            if (levelMeshes.TryGetValue(k, out var levels)) { foreach (var m in levels) if (m != null) Release(m); levelMeshes.Remove(k); }
        }

        private static long ChunkKey(float x, float z) => ((long)Mathf.FloorToInt(x / ChunkSize) << 32) ^ (uint)Mathf.FloorToInt(z / ChunkSize);
        private static Vector2 ChunkCentre(long k) => new(((int)(k >> 32) + 0.5f) * ChunkSize, ((int)(uint)k + 0.5f) * ChunkSize);

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
            foreach (var k in cells.Keys) dirty.Add(k);
        }

        private readonly MeshBuilder builder = new();
        private readonly Dictionary<long, float> lastBuilt = new();
        private readonly List<long> ready = new();

        // While the player paints or sculpts, the same chunks are dirtied every frame. Rebuild each chunk at most every
        // 0.12 s and at most two chunks per frame, so editing never stalls the main thread; the rest waits a frame.
        private void LateUpdate() => RebuildDirty(2, 0.12f);

        /// <summary>Rebuilds dirty chunks (all of them by default: tests, loading).</summary>
        public void RebuildDirty(int maxChunks = int.MaxValue, float minInterval = 0f)
        {
            if (dirty.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            ready.Clear();
            foreach (long k in dirty)
            {
                if (ready.Count >= maxChunks) break;
                if (minInterval > 0f && lastBuilt.TryGetValue(k, out float t) && now - t < minInterval) continue;
                if (Shown != null && !Shown(ChunkCentre(k), ChunkSize)) continue;   // (out of the window: built when it comes into view)
                ready.Add(k);
            }
            if (ready.Count == 0) return;
            foreach (long k in ready)
            {
                dirty.Remove(k);
                lastBuilt[k] = now;
                if (!cells.TryGetValue(k, out var list) || list.Count == 0) { Drop(k); continue; }
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
                builder.Clear();
                foreach (var t in list)
                {
                    float y = terrain.SurfaceAt(t.x, t.z) - 0.003f * t.scale;
                    builder.AppendFast(Data((TreeKind)t.kind, t.variant), new Vector3(t.x, y, t.z), Quaternion.Euler(0f, t.rotation, 0f), t.scale,
                        new Color(t.tint, t.tint, t.tint * 0.97f));
                }
                builder.ToMesh(chunk.go.name, chunk.mesh);
                chunk.mesh.RecalculateBounds();
                // The same trees as simple stand-ins, for the shadows.
                proxyBuilder.Clear();
                foreach (var t in list)
                {
                    float y = terrain.SurfaceAt(t.x, t.z) - 0.003f * t.scale;
                    proxyBuilder.AppendFast(ProxyData((TreeKind)t.kind, t.variant), new Vector3(t.x, y, t.z), Quaternion.Euler(0f, t.rotation, 0f), t.scale, new Color(t.tint, t.tint, t.tint * 0.97f));
                }
                if (!proxyMeshes.TryGetValue(k, out var proxy)) proxyMeshes[k] = proxy = new Mesh { name = chunk.go.name + " proxy" };
                proxyBuilder.ToMesh(proxy.name, proxy);
                proxy.RecalculateBounds();
                MeshProxy.Shadow(chunk.go.GetComponent<MeshRenderer>(), proxy);
                // The same trees at the other levels of detail (rounder up close, simpler further away).
                if (!levelMeshes.TryGetValue(k, out var levels))
                {
                    levelMeshes[k] = levels = new Mesh[MeshBuilder.Levels];
                    for (int d = MeshBuilder.Extreme; d <= MeshBuilder.Minimal; d++) if (d != MeshBuilder.High) levels[d + 1] = new Mesh { name = chunk.go.name + " detail " + d };
                }
                for (int d = MeshBuilder.Extreme; d <= MeshBuilder.Minimal; d++)
                {
                    if (d == MeshBuilder.High) continue;
                    proxyBuilder.Clear();
                    foreach (var t in list)
                    {
                        float y = terrain.SurfaceAt(t.x, t.z) - 0.003f * t.scale;
                        proxyBuilder.AppendFast(Data((TreeKind)t.kind, t.variant, d), new Vector3(t.x, y, t.z), Quaternion.Euler(0f, t.rotation, 0f), t.scale, new Color(t.tint, t.tint, t.tint * 0.97f));
                    }
                    proxyBuilder.ToMesh(levels[d + 1].name, levels[d + 1]);
                    levels[d + 1].RecalculateBounds();
                }
                Lod.Attach(chunk.go.GetComponent<MeshRenderer>(), d => levels[d + 1], ChunkSize);
            }
        }

        private MeshData ProxyData(TreeKind kind, int variant)
        {
            int key = (int)kind * 10 + variant;
            if (!proxyData.TryGetValue(key, out var d)) proxyData[key] = d = new MeshData(MeshProxy.Tree(TreeFactory.Get(kind, variant), kind is TreeKind.Conifer or TreeKind.Cypress));
            return d;
        }

        private static void Release(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else DestroyImmediate(o);
        }

        private MeshData Data(TreeKind kind, int variant, int detail = 0)
        {
            int key = ((int)kind * 10 + variant) * MeshBuilder.Levels + detail + 1;
            if (!meshData.TryGetValue(key, out var d)) meshData[key] = d = new MeshData(TreeFactory.Get(kind, variant, detail));
            return d;
        }

        public static readonly string[] ForestNames = { "Mixed wood", "Leafy wood", "Pine forest", "Birch grove", "Autumn wood", "Orchard", "Riverside", "Palm grove" };
        public static readonly string[] ForestAbout =
        {
            "Oaks, spruces, birches and blossom in groves that follow the land: spruce up the hills",
            "Oaks, lindens and birches with bushes underneath: a green summer wood",
            "Spruces and Scots pines, ferns underfoot, a birch here and there",
            "Silver birches, some growing in pairs, light and airy",
            "Red, orange and yellow maples, golden oaks and birches, leaves on the ground",
            "Apple and cherry trees planted in neat rows",
            "Weeping willows by the water, poplars and birches further back",
            "Coconut, date and fan palms with flowering bushes",
        };

        /// <summary>
        /// A tree for a forest type at a place: its species and variant (-1: the species' usual mix). The mixed wood keeps
        /// its coherent groves; the riverside's willows crowd to the water.
        /// </summary>
        public (TreeKind kind, int variant) PickTree(float x, float z, ForestType type, ref Rng r)
        {
            float roll = r.Value;
            (TreeKind, int) Weighted(params (TreeKind kind, int variant, float weight)[] options)
            {
                float total = 0f; foreach (var o in options) total += o.weight;
                float pick = roll * total;
                foreach (var o in options) if ((pick -= o.weight) < 0f) return (o.kind, o.variant);
                return (options[^1].kind, options[^1].variant);
            }
            switch (type)
            {
                case ForestType.Leafy: return Weighted((TreeKind.Oak, 0, 0.8f), (TreeKind.Oak, 1, 0.6f), (TreeKind.Birch, 0, 0.45f), (TreeKind.Bush, 0, 0.4f), (TreeKind.Bush, 3, 0.15f), (TreeKind.Blossom, 1, 0.08f));
                case ForestType.Pine: return Weighted((TreeKind.Conifer, -1, 1.3f), (TreeKind.Pine, 0, 0.8f), (TreeKind.Pine, 2, 0.3f), (TreeKind.Bush, 3, 0.35f), (TreeKind.Birch, 0, 0.1f));
                case ForestType.Birch: return Weighted((TreeKind.Birch, 0, 1.2f), (TreeKind.Birch, 1, 0.5f), (TreeKind.Bush, 0, 0.35f), (TreeKind.Bush, 3, 0.15f), (TreeKind.Conifer, -1, 0.1f));
                case ForestType.Autumn: return Weighted((TreeKind.Maple, 0, 0.55f), (TreeKind.Maple, 1, 0.55f), (TreeKind.Maple, 2, 0.45f), (TreeKind.Oak, 2, 0.5f), (TreeKind.Birch, 2, 0.45f), (TreeKind.Bush, 0, 0.25f));
                case ForestType.Orchard: return Weighted((TreeKind.Oak, 3, 1f), (TreeKind.Blossom, 1, 0.45f), (TreeKind.Blossom, 0, 0.25f));
                case ForestType.Palms: return Weighted((TreeKind.Palm, 0, 0.7f), (TreeKind.Palm, 1, 0.6f), (TreeKind.Palm, 2, 0.4f), (TreeKind.Bush, 1, 0.25f), (TreeKind.Bush, 2, 0.2f));
                case ForestType.Riverside:
                {
                    float near = 0f;   // (how much water there is round about)
                    for (int k = 0; k < 8; k++) { float a = k * Mathf.PI / 4f; near = Mathf.Max(near, terrain.WaterAt(x + Mathf.Cos(a) * 0.08f, z + Mathf.Sin(a) * 0.08f)); }
                    return Weighted((TreeKind.Willow, -1, 0.3f + 1.8f * near), (TreeKind.Cypress, 2, 0.6f), (TreeKind.Birch, 0, 0.4f), (TreeKind.Bush, 0, 0.45f));
                }
                default:
                {
                    var kind = PickKind(x, z, ref r);
                    if (kind == TreeKind.Oak && r.Value < 0.18f) kind = TreeKind.Birch;   // (birches among the oaks)
                    return (kind, -1);
                }
            }
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
        public int Paint(Vector2 centre, float radius, float density, float dt, Func<Vector2, float, bool> canPlace, ForestType type = ForestType.Mixed)
        {
            float area = Mathf.PI * radius * radius;
            int attempts = Mathf.Max(1, Mathf.RoundToInt((25f + area * 900f) * dt * (0.4f + density)));
            float spacing = Mathf.Lerp(1.5f, 0.78f, density);
            int added = 0, room = Mathf.Min(MaxTrees - Trees.Count, MaxInView - InView());
            for (int a = 0; a < attempts && added < room; a++)
            {
                var p = centre + rng.InsideUnitCircle() * radius;
                if (type == ForestType.Orchard)
                {
                    // In rows: on a lattice (closer with more density), a hair off true.
                    float step = Mathf.Lerp(0.085f, 0.06f, density);
                    p = new Vector2(Mathf.Round(p.x / step) * step, Mathf.Round(p.y / step) * step) + rng.InsideUnitCircle() * 0.002f;
                }
                var (kind, variant) = PickTree(p.x, p.y, type, ref rng);
                float scale = type == ForestType.Orchard ? rng.Range(0.9f, 1.05f) : rng.Range(0.75f, 1.25f) * (kind == TreeKind.Bush ? rng.Range(0.8f, 1.3f) : 1f);
                float footprint = TreeFactory.Radius(kind) * scale;
                if (!canPlace(p, footprint)) continue;
                if (terrain.EdgeAt(p.x, p.y) < 0.35f) continue;
                if (Crowded(p, footprint, type == ForestType.Orchard ? 0.75f : spacing)) continue;
                var tree = new TreeInstance
                {
                    x = p.x, z = p.y, scale = scale, rotation = rng.Range(0f, 360f), kind = (byte)kind,
                    variant = (byte)(variant >= 0 ? variant : TreeFactory.PickVariant(kind, ref rng)), tint = rng.Range(0.88f, 1.1f),
                };
                Trees.Add(tree);
                Cell(ChunkKey(p.x, p.y)).Add(tree);
                MarkDirty(p.x, p.y);
                added++;
            }
            return added;
        }

        private bool Crowded(Vector2 p, float footprint, float spacing)
        {
            // (the widest two trees, spaced, are well under a chunk apart: the chunks round about are enough)
            int cx = Mathf.FloorToInt(p.x / ChunkSize), cz = Mathf.FloorToInt(p.y / ChunkSize);
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                if (!cells.TryGetValue(((long)(cx + i) << 32) ^ (uint)(cz + j), out var list)) continue;
                foreach (var t in list)
                {
                    float dx = t.x - p.x, dz = t.z - p.y;
                    float min = (footprint + TreeFactory.Radius((TreeKind)t.kind) * t.scale) * spacing;
                    if (dx * dx + dz * dz < min * min) return true;
                }
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
            if (removed > 0) Reindex();
            return removed;
        }

        public int RemoveWhere(Func<TreeInstance, bool> predicate)
        {
            int removed = 0;
            for (int i = Trees.Count - 1; i >= 0; i--)
                if (predicate(Trees[i])) { MarkDirty(Trees[i].x, Trees[i].z); Trees.RemoveAt(i); removed++; }
            if (removed > 0) Reindex();
            return removed;
        }

        public void SetTrees(IEnumerable<TreeInstance> trees)
        {
            MarkAllDirty();
            Trees.Clear(); Trees.AddRange(trees);
            Reindex();
            MarkAllDirty();
        }

        public int ChunkCount => chunks.Count;
    }
}
