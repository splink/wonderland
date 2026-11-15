using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Automatic ground cover: grass tufts, little flower clumps and pebbles scattered over the meadow so open ground
    /// reads as lush, hand-made miniature turf rather than a flat green sheet. Positions come from the world seed (not
    /// saved); a tuft shows only where the ground is free (not on the railway corridor, under a house or at the rim),
    /// and follows the terrain as it is sculpted. Drawn as combined meshes per 25 cm chunk, like the forest.
    /// </summary>
    public sealed class GroundCover : MonoBehaviour
    {
        private const float ChunkSize = 0.25f;
        private struct Tuft { public float x, z, scale, rotation; public byte kind; public float tint; }
        private readonly List<Tuft> tufts = new();
        private readonly Dictionary<int, (GameObject go, Mesh mesh)> chunks = new();
        private readonly HashSet<int> dirty = new();
        private MeshData[] meshes;
        private TerrainField terrain;
        private Material material;
        private Func<Vector2, bool> blocked;

        public int Count => tufts.Count;

        public void Init(TerrainField field, Material lit, int seed, Func<Vector2, bool> isBlocked)
        {
            terrain = field; material = lit; blocked = isBlocked;
            meshes = new[] { new MeshData(GrassTuft(0)), new MeshData(GrassTuft(1)), new MeshData(FlowerClump(Pal.FlowerYellow)),
                new MeshData(FlowerClump(Pal.FlowerWhite)), new MeshData(FlowerClump(Pal.FlowerPink)), new MeshData(FlowerClump(Pal.FlowerLilac)), new MeshData(Pebbles()) };
            var rng = new Rng(seed * 31 + 5);
            const float spacing = 0.042f;
            for (float x = -field.Half; x < field.Half; x += spacing)
            for (float z = -field.Half; z < field.Half; z += spacing)
            {
                var p = new Vector2(x + rng.Range(-0.45f, 0.45f) * spacing, z + rng.Range(-0.45f, 0.45f) * spacing);
                float meadow = Noise.Fbm2(p.x * 3f + seed, p.y * 3f, 2);
                if (rng.Value > 0.35f + meadow * 0.5f) continue;
                float r = rng.Value;
                byte kind = r < 0.72f ? (byte)rng.Range(0, 2) : r < 0.94f ? (byte)rng.Range(2, 6) : (byte)6;
                tufts.Add(new Tuft { x = p.x, z = p.y, scale = rng.Range(0.75f, 1.3f), rotation = rng.Range(0f, 360f), kind = kind, tint = rng.Range(0.85f, 1.1f) });
            }
            MarkAllDirty();
        }

        private static int ChunkKey(float x, float z) => (Mathf.FloorToInt(x / ChunkSize) + 64) * 1000 + Mathf.FloorToInt(z / ChunkSize) + 64;

        public void MarkDirtyRegion(Vector2 centre, float radius)
        {
            for (float x = centre.x - radius - ChunkSize; x <= centre.x + radius + ChunkSize; x += ChunkSize * 0.5f)
            for (float z = centre.y - radius - ChunkSize; z <= centre.y + radius + ChunkSize; z += ChunkSize * 0.5f)
                dirty.Add(ChunkKey(x, z));
        }

        public void MarkAllDirty()
        {
            if (terrain == null) return;
            for (float x = -terrain.Half; x <= terrain.Half + ChunkSize; x += ChunkSize)
            for (float z = -terrain.Half; z <= terrain.Half + ChunkSize; z += ChunkSize)
                dirty.Add(ChunkKey(x, z));
        }

        private void LateUpdate() => RebuildDirty();

        public void RebuildDirty()
        {
            if (dirty.Count == 0 || terrain == null) return;
            var builders = new Dictionary<int, MeshBuilder>();
            foreach (var t in tufts)
            {
                int k = ChunkKey(t.x, t.z);
                if (!dirty.Contains(k)) continue;
                var p = new Vector2(t.x, t.z);
                if (terrain.EdgeAt(t.x, t.z) < 0.3f || terrain.LockAt(t.x, t.z) > 0.1f || blocked(p)) continue;
                if (!builders.TryGetValue(k, out var b)) builders[k] = b = new MeshBuilder();
                var normal = terrain.NormalAt(t.x, t.z);
                if (normal.y < 0.6f && t.kind < 6) continue;   // no tufts on steep banks
                var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, normal, 0.6f)) * Quaternion.Euler(0f, t.rotation, 0f);
                var m = Matrix4x4.TRS(new Vector3(t.x, terrain.SurfaceAt(t.x, t.z) - 0.0008f, t.z), rot, Vector3.one * t.scale);
                b.Append(meshes[t.kind], m, new Color(t.tint, t.tint, t.tint));
            }
            foreach (int k in dirty)
            {
                builders.TryGetValue(k, out var b);
                if (b == null || b.VertexCount == 0)
                {
                    if (chunks.TryGetValue(k, out var empty)) { Release(empty.go); Release(empty.mesh); chunks.Remove(k); }
                    continue;
                }
                if (!chunks.TryGetValue(k, out var chunk))
                {
                    var go = new GameObject("Ground cover " + k);
                    go.transform.SetParent(transform, false);
                    go.AddComponent<MeshFilter>();
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
                    chunk = (go, new Mesh { name = go.name });
                    go.GetComponent<MeshFilter>().sharedMesh = chunk.mesh;
                    chunks[k] = chunk;
                }
                b.ToMesh(chunk.go.name, chunk.mesh);
                chunk.mesh.RecalculateBounds();
            }
            dirty.Clear();
        }

        private static void Release(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>Two-sided blade fins leaning outwards; dark at the root, sunlit tips.</summary>
        private static Mesh GrassTuft(int variant)
        {
            var b = new MeshBuilder();
            var rng = new Rng(300 + variant);
            int blades = 5 + variant * 2;
            var root = Pal.Hex(variant == 0 ? "#2F6326" : "#3A6E2A");
            var tip = Pal.Hex(variant == 0 ? "#8FBF4A" : "#B5C95A");
            for (int i = 0; i < blades; i++)
            {
                float a = i / (float)blades * Mathf.PI * 2f + rng.Range(-0.3f, 0.3f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, dir) * rng.Range(0.0011f, 0.0016f);
                var basePos = dir * rng.Range(0f, 0.002f);
                float h = rng.Range(0.007f, 0.013f);
                var top = basePos + dir * h * rng.Range(0.35f, 0.6f) + Vector3.up * h;
                var c = Pal.Jitter(tip, ref rng, 0.25f);
                foreach (bool front in new[] { true, false })
                {
                    int v = b.AddVertex(basePos - side, Vector3.up, root);
                    b.AddVertex(basePos + side, Vector3.up, root);
                    b.AddVertex(top, Vector3.up, c);
                    if (front) b.AddTriangle(v, v + 2, v + 1); else b.AddTriangle(v, v + 1, v + 2);
                }
            }
            return b.ToMesh("GrassTuft" + variant);
        }

        private static Mesh FlowerClump(Color flower)
        {
            var b = new MeshBuilder();
            var rng = new Rng(flower.GetHashCode());
            var leaf = Pal.Hex("#447F2E");
            b.AddBlob(new Vector3(0f, 0.002f, 0f), new Vector3(0.005f, 0.0028f, 0.005f), 1, 0.25f, 3, (d, _) => Color.Lerp(Pal.Shade(leaf, 0.7f), leaf, d.y * 0.5f + 0.5f));
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(rng.Range(-0.004f, 0.004f), rng.Range(0.006f, 0.01f), rng.Range(-0.004f, 0.004f));
                b.AddCylinder(new Vector3(p.x * 0.5f, 0f, p.z * 0.5f), 0.0004f, 0.0003f, p.y, 3, leaf, leaf, false);
                var c = Pal.Jitter(flower, ref rng, 0.2f);
                b.AddBlob(p, new Vector3(0.0019f, 0.0012f, 0.0019f), 0, 0.1f, i, (_, __) => c);
                b.AddBlob(p + Vector3.up * 0.0008f, Vector3.one * 0.0007f, 0, 0f, i + 9, (_, __) => Pal.FlowerYellow);
            }
            return b.ToMesh("Flowers");
        }

        private static Mesh Pebbles()
        {
            var b = new MeshBuilder();
            var rng = new Rng(77);
            for (int i = 0; i < 3; i++)
            {
                var c = Pal.Jitter(Pal.Stone, ref rng, 0.3f);
                float r = rng.Range(0.002f, 0.0035f);
                b.AddBlob(new Vector3(rng.Range(-0.004f, 0.004f), r * 0.3f, rng.Range(-0.004f, 0.004f)), new Vector3(r, r * 0.6f, r * 0.8f), 1, 0.2f, i,
                    (d, _) => Pal.Shade(c, 0.8f + 0.25f * d.y));
            }
            return b.ToMesh("Pebbles");
        }
    }
}
