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
        private readonly Dictionary<long, List<Tuft>> tufts = new();   // per chunk
        private int count, seed;
        private readonly Dictionary<long, (GameObject go, Mesh mesh)> chunks = new();
        private readonly HashSet<long> dirty = new();
        private MeshData[] meshes;
        private TerrainField terrain;
        private Material material;
        private Func<Vector2, bool> blocked;

        public int Count => count;

        public void Init(TerrainField field, Material lit, int seed, Func<Vector2, bool> isBlocked)
        {
            terrain = field; material = lit; blocked = isBlocked; this.seed = seed;
            meshes = new MeshData[16];
            for (int k = 0; k < meshes.Length; k++) meshes[k] = new MeshData(Detail(k));
            Scatter();
            MarkAllDirty();
            PerfSettings.Changed -= OnSetting; PerfSettings.Changed += OnSetting;
        }

        // The Grass setting: fewer tufts (or none), re-scattered at once.
        private void OnSetting(PerfSettings.Setting s) { if (s == PerfSettings.Grass && this != null && terrain != null) Scatter(); }
        private void OnDestroy() => PerfSettings.Changed -= OnSetting;

        /// <summary>
        /// Candidate positions on a global 4.2 cm lattice, each seeded by its lattice index: the same tufts appear in the
        /// same places however the terrain grid is cut, so re-fitting the grid to the room changes nothing visible.
        /// </summary>
        public void Scatter()
        {
            foreach (var k in chunks.Keys) dirty.Add(k);
            tufts.Clear(); count = 0;
            const float spacing = 0.05f;   // (was 0.042: 30 % fewer tufts - the grass reads the same from a step away)
            float share = PerfSettings.GrassShare;
            int ix0 = Mathf.FloorToInt(terrain.X0 / spacing), ix1 = Mathf.CeilToInt(terrain.XMax / spacing);
            int iz0 = Mathf.FloorToInt(terrain.Z0 / spacing), iz1 = Mathf.CeilToInt(terrain.ZMax / spacing);
            for (int iz = iz0; iz <= iz1; iz++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                if (share < 1f && Noise.Hash(ix, iz, seed + 7) >= share) continue;   // (the Grass setting)
                var rng = new Rng((int)(Noise.Hash(ix, iz, seed) * 16777215f) + 1);
                var p = new Vector2((ix + rng.Range(-0.45f, 0.45f)) * spacing, (iz + rng.Range(-0.45f, 0.45f)) * spacing);
                float meadow = Noise.Fbm2(p.x * 3f + seed, p.y * 3f, 2);
                if (rng.Value > 0.35f + meadow * 0.5f) continue;
                float r = rng.Value;
                // Mostly grass; flowers, daisies, lavender and tulips in drifts; pebbles; a rare ring of toadstools.
                float drift = Noise.Value2(p.x * 5f - seed, p.y * 5f + 11f);
                byte kind = r < 0.62f ? (byte)rng.Range(0, 2)
                    : r < 0.8f ? (byte)(drift > 0.5f ? rng.Range(2, 6) : rng.Range(7, 9))
                    : r < 0.92f ? (byte)(drift > 0.62f ? 9 : rng.Range(2, 6))
                    : r < 0.985f ? (byte)6 : (byte)10;
                long key = ChunkKey(p.x, p.y);
                if (!tufts.TryGetValue(key, out var list)) tufts[key] = list = new List<Tuft>();
                list.Add(new Tuft { x = p.x, z = p.y, scale = rng.Range(0.75f, 1.3f), rotation = rng.Range(0f, 360f), kind = kind, tint = rng.Range(0.85f, 1.1f) });
                count++;
            }
            MarkAllDirty();
        }

        /// <summary>Whether a chunk (centre, half size) is near enough the world window to be built (null: everywhere).</summary>
        public System.Func<Vector2, float, bool> Shown;

        /// <summary>Shows just the chunks in the window: the grass and flowers outside it are neither drawn nor rebuilt.</summary>
        public void Cull(System.Func<Vector2, float, bool> shown)
        {
            foreach (var (k, c) in chunks)
            {
                bool show = shown(ChunkCentre(k), ChunkSize);
                if (c.go.activeSelf != show) c.go.SetActive(show);
            }
        }

        private static long ChunkKey(float x, float z) => ((long)Mathf.FloorToInt(x / ChunkSize) << 32) ^ (uint)Mathf.FloorToInt(z / ChunkSize);
        private static Vector2 ChunkCentre(long k) => new(((int)(k >> 32) + 0.5f) * ChunkSize, ((int)(uint)k + 0.5f) * ChunkSize);

        public void MarkDirtyRegion(Vector2 centre, float radius)
        {
            for (float x = centre.x - radius - ChunkSize; x <= centre.x + radius + ChunkSize; x += ChunkSize * 0.5f)
            for (float z = centre.y - radius - ChunkSize; z <= centre.y + radius + ChunkSize; z += ChunkSize * 0.5f)
                dirty.Add(ChunkKey(x, z));
        }

        public void MarkAllDirty()
        {
            if (terrain == null) return;
            for (float x = terrain.X0; x <= terrain.XMax + ChunkSize; x += ChunkSize)
            for (float z = terrain.Z0; z <= terrain.ZMax + ChunkSize; z += ChunkSize)
                dirty.Add(ChunkKey(x, z));
        }

        private readonly MeshBuilder builder = new();
        private readonly Dictionary<long, float> lastBuilt = new();
        private readonly List<long> ready = new();

        // Same budget as the forest: editing re-dirties chunks every frame; rebuild each at most every 0.15 s, two per frame.
        private void LateUpdate() => RebuildDirty(2, 0.15f);

        public void RebuildDirty(int maxChunks = int.MaxValue, float minInterval = 0f)
        {
            if (dirty.Count == 0 || terrain == null) return;
            float now = Time.realtimeSinceStartup;
            ready.Clear();
            foreach (long k in dirty)
            {
                if (ready.Count >= maxChunks) break;
                if (minInterval > 0f && lastBuilt.TryGetValue(k, out float t) && now - t < minInterval) continue;
                if (Shown != null && !Shown(ChunkCentre(k), ChunkSize)) continue;   // (out of the window: built when it comes into view)
                ready.Add(k);
            }
            foreach (long k in ready)
            {
                dirty.Remove(k);
                lastBuilt[k] = now;
                builder.Clear();
                if (tufts.TryGetValue(k, out var list))
                    foreach (var t in list)
                    {
                        // By the water: reeds on the bank, lily pads floating on the pond.
                        float wet = terrain.WaterAt(t.x, t.z);
                        if (wet > 0.5f)
                        {
                            float level = terrain.WaterLevelAt(t.x, t.z);
                            if (level > 0f && t.kind % 3 == 0 && level - terrain.SurfaceAt(t.x, t.z) > 0.006f)
                                builder.AppendFast(meshes[t.kind % 2 == 0 ? 12 : 13], new Vector3(t.x, level, t.z), Quaternion.Euler(0f, t.rotation, 0f), t.scale, Color.white);
                            continue;
                        }
                        if (wet > 0.02f || terrain.WaterAt(t.x + 0.03f, t.z) > 0.5f || terrain.WaterAt(t.x - 0.03f, t.z) > 0.5f || terrain.WaterAt(t.x, t.z + 0.03f) > 0.5f || terrain.WaterAt(t.x, t.z - 0.03f) > 0.5f)
                        {
                            if (t.kind != 6) builder.AppendFast(meshes[11], new Vector3(t.x, terrain.SurfaceAt(t.x, t.z) - 0.0008f, t.z), Quaternion.Euler(0f, t.rotation, 0f), t.scale, new Color(t.tint, t.tint, t.tint));
                            continue;
                        }
                        if (terrain.EdgeAt(t.x, t.z) < 0.3f || terrain.LockAt(t.x, t.z) > 0.1f || blocked(new Vector2(t.x, t.z))) continue;
                        var normal = terrain.NormalAt(t.x, t.z);
                        float height = terrain.SurfaceAt(t.x, t.z);
                        // Mountains: boulders on scree and cliffs, alpine flowers up high, nothing but rocks in the snow.
                        int kind = t.kind;
                        bool steep = normal.y < 0.8f, high = height > AlpineHeight;
                        if (steep || high)
                        {
                            if (kind == 6 || kind == 10 || (steep && t.kind % 3 == 0)) kind = 14;
                            else if (height > SnowHeight && normal.y > 0.72f) continue;
                            else if (high && kind >= 2 && kind <= 9) kind = 15;
                        }
                        if (normal.y < 0.6f && kind < 6) continue;   // no tufts on steep banks
                        var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, normal, 0.6f)) * Quaternion.Euler(0f, t.rotation, 0f);
                        builder.AppendFast(meshes[kind], new Vector3(t.x, height - (kind == 14 ? 0.002f : 0.0008f), t.z), rot, t.scale, new Color(t.tint, t.tint, t.tint));
                    }
                if (builder.VertexCount == 0)
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
                    // Tufts a centimetre tall are less than a pixel beyond a couple of metres: not drawn there.
                    var lod = go.AddComponent<LODGroup>();
                    lod.SetLODs(new[] { new LOD(0.1f / (2f * 2.6f * Mathf.Tan(48f * Mathf.Deg2Rad)) * QualitySettings.lodBias, new Renderer[] { r }) });
                    lod.size = 0.1f;
                    chunk = (go, new Mesh { name = go.name });
                    go.GetComponent<MeshFilter>().sharedMesh = chunk.mesh;
                    chunks[k] = chunk;
                }
                builder.ToMesh(chunk.go.name, chunk.mesh);
                chunk.mesh.RecalculateBounds();
            }
        }

        private static void Release(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>Heights (local metres) where the land turns alpine, and where the snow starts (matches the terrain shader).</summary>
        public const float AlpineHeight = 0.14f, SnowHeight = 0.2f;

        /// <summary>0-1 grass, 2-5 flower clumps, 6 pebbles, 7 daisies, 8 tulips, 9 lavender, 10 toadstool ring, 11 reeds, 12-13 lily pads, 14 boulders, 15 alpine flowers.</summary>
        private static Mesh Detail(int kind)
        {
            var b = new MeshBuilder();
            var rng = new Rng(300 + kind * 17);
            switch (kind)
            {
                case 0: case 1: Flora.GrassTuft(b, Vector3.zero, 1f, ref rng, kind); break;
                case 2: Flora.Flowers(b, Vector3.zero, Pal.FlowerYellow, 1f, ref rng); break;
                case 3: Flora.Flowers(b, Vector3.zero, Pal.FlowerWhite, 1f, ref rng); break;
                case 4: Flora.Flowers(b, Vector3.zero, Pal.FlowerPink, 1f, ref rng); break;
                case 5: Flora.Flowers(b, Vector3.zero, Pal.FlowerLilac, 1f, ref rng); break;
                case 6: Flora.Pebbles(b, Vector3.zero, ref rng); break;
                case 7:
                    Flora.GrassTuft(b, Vector3.zero, 0.8f, ref rng);
                    Flora.Flowers(b, new Vector3(0.003f, 0f, 0.002f), Pal.FlowerWhite, 0.8f, ref rng, 5);
                    break;
                case 8: Flora.Tulips(b, Vector3.zero, rng.Chance(0.5f) ? Pal.FlowerRed : Pal.FlowerYellow, 1f, ref rng); break;
                case 9: Flora.Lavender(b, Vector3.zero, 1f, ref rng); break;
                case 11: Flora.Reeds(b, Vector3.zero, ref rng); Flora.Reeds(b, new Vector3(0.005f, 0f, 0.003f), ref rng); break;
                case 12: Flora.LilyPad(b, Vector3.zero, ref rng, false); Flora.LilyPad(b, new Vector3(0.008f, 0f, 0.004f), ref rng, false); break;
                case 13: Flora.LilyPad(b, Vector3.zero, ref rng, true); break;
                case 14:
                    for (int i = 0; i < 3; i++)
                    {
                        float r = rng.Range(0.004f, 0.009f) * (i == 0 ? 1.3f : 1f);
                        var c = Pal.Jitter(Pal.Hex("#A39E96"), ref rng, 0.25f);
                        b.AddBlob(new Vector3(rng.Range(-0.008f, 0.008f), r * 0.5f, rng.Range(-0.008f, 0.008f)), new Vector3(r, r * 0.75f, r * 0.9f), 1, 0.3f, i + 7,
                            (d, q) => d.y > 0.5f && Noise.Value3(q * 600f) > 0.55f ? Pal.Hex("#7E9A4A") : Color.Lerp(Pal.Shade(c, 0.75f), c, d.y * 0.4f + 0.6f), 2.5f);
                    }
                    break;
                case 15:
                    Flora.GrassTuft(b, Vector3.zero, 0.6f, ref rng, 1);
                    for (int i = 0; i < 4; i++)
                    {
                        var p = new Vector3(rng.Range(-0.005f, 0.005f), 0.0035f, rng.Range(-0.005f, 0.005f));
                        var c = i % 2 == 0 ? Pal.Hex("#F7F4EA") : Pal.Hex("#7FA8E0");   // edelweiss and gentians
                        b.AddBlob(p, new Vector3(0.0016f, 0.0006f, 0.0016f), 0, 0.3f, i, (_, __) => c);
                        b.AddBlob(p + Vector3.up * 0.0004f, Vector3.one * 0.0005f, 0, 0f, i + 9, (_, __) => Pal.FlowerYellow);
                    }
                    break;
                default:
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i / 5f * Mathf.PI * 2f + rng.Range(-0.3f, 0.3f);
                        Flora.Mushroom(b, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.011f, rng.Range(0.6f, 0.9f), ref rng, true);
                    }
                    break;
            }
            return b.ToMesh("GroundDetail" + kind);
        }
    }
}
