using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Draws the ponds: a flat water surface over every water cell and two cells beyond, under the banks, where the
    /// opaque terrain clips it into a smooth, natural shoreline. Chunked like the terrain; only touched chunks rebuild.
    /// Vertex colour: r = depth (0 shore .. 1 deep), g = shore (foam), b = flow (0 for still water).
    /// </summary>
    public sealed class WaterRenderer : MonoBehaviour
    {
        private const int ChunkCells = 40;
        private TerrainField field;
        private Material material;
        private readonly Dictionary<int, (GameObject go, Mesh mesh)> chunks = new();
        private readonly MeshBuilder builder = new();

        public void Init(TerrainField terrain, Material water) { field = terrain; material = water; RebuildAll(); }

        public void RebuildAll()
        {
            foreach (var c in chunks.Values) { Release(c.go); Release(c.mesh); }
            chunks.Clear();
            Rebuild(field.All);
        }

        private static void Release(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        public void Rebuild(RectInt region)
        {
            if (region.width <= 0) return;
            int cx0 = Mathf.Max(0, region.xMin - 1) / ChunkCells, cx1 = Mathf.Min(field.Nx - 2, region.xMax) / ChunkCells;
            int cz0 = Mathf.Max(0, region.yMin - 1) / ChunkCells, cz1 = Mathf.Min(field.Nz - 2, region.yMax) / ChunkCells;
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++) BuildChunk(cx, cz);
        }

        private float LevelNear(int x, int z)
        {
            float best = -1f;
            for (int oz = -2; oz <= 2; oz++)
            for (int ox = -2; ox <= 2; ox++)
            {
                int xx = x + ox, zz = z + oz;
                if (xx < 0 || zz < 0 || xx >= field.Nx || zz >= field.Nz) continue;
                int j = zz * field.Nx + xx;
                if (field.Water[j] >= 0.5f) best = Mathf.Max(best, field.WaterLevel[j]);
            }
            return best;
        }

        private void BuildChunk(int cx, int cz)
        {
            int key = cz * 10000 + cx;
            builder.Clear();
            int x0 = cx * ChunkCells, z0 = cz * ChunkCells;
            int x1 = Mathf.Min(field.Nx - 1, x0 + ChunkCells), z1 = Mathf.Min(field.Nz - 1, z0 + ChunkCells);
            // Dry all round (the usual case): nothing to build - and no searching cell by cell.
            bool anyWater = false;
            for (int z = Mathf.Max(0, z0 - 3); z <= Mathf.Min(field.Nz - 1, z1 + 3) && !anyWater; z++)
            for (int x = Mathf.Max(0, x0 - 3); x <= Mathf.Min(field.Nx - 1, x1 + 3) && !anyWater; x++)
                anyWater = field.Water[z * field.Nx + x] > 0f;
            if (!anyWater)
            {
                if (chunks.TryGetValue(key, out var dry)) { Release(dry.go); Release(dry.mesh); chunks.Remove(key); }
                return;
            }
            float cell = field.Cell;
            for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
            {
                // A quad wherever any corner is water or borders water.
                float level = Mathf.Max(Mathf.Max(LevelNear(x, z), LevelNear(x + 1, z)), Mathf.Max(LevelNear(x, z + 1), LevelNear(x + 1, z + 1)));
                if (level < 0f) continue;
                // A quad whose dry corner lies below the water would drape down a slope (a spill): leave it out.
                bool spill = false;
                for (int k = 0; k < 4 && !spill; k++)
                {
                    int i = (z + (k >= 2 ? 1 : 0)) * field.Nx + x + (k == 1 || k == 2 ? 1 : 0);
                    if (field.Water[i] < 0.5f && field.Surface[i] < level - 0.0005f) spill = true;
                }
                if (spill) continue;
                int v = builder.VertexCount;
                for (int k = 0; k < 4; k++)
                {
                    int xx = x + (k == 1 || k == 2 ? 1 : 0), zz = z + (k >= 2 ? 1 : 0);
                    int i = zz * field.Nx + xx;
                    float depth = Mathf.Clamp01((level - field.Surface[i]) / TerrainField.PondDepth);
                    bool wet = field.Water[i] >= 0.5f;
                    float shore = wet ? Mathf.Clamp01(1f - depth * 4f) : 0.8f;
                    // Flat all the way out under the banks: the ground cuts it, so the shoreline is the smooth line where
                    // the land meets the water's level (not the grid's steps).
                    float y = level;
                    builder.AddVertex(new Vector3(field.LocalX(xx), y, field.LocalZ(zz)), Vector3.up, new Color(depth, shore, 0f, wet ? 1f : 0f));   // (a: fades out towards dry land - a soft edge, not the grid's steps)
                }
                builder.AddTriangle(v, v + 3, v + 2); builder.AddTriangle(v, v + 2, v + 1);
            }
            if (builder.VertexCount == 0)
            {
                if (chunks.TryGetValue(key, out var empty)) { Release(empty.go); Release(empty.mesh); chunks.Remove(key); }
                return;
            }
            if (!chunks.TryGetValue(key, out var chunk))
            {
                var go = new GameObject($"Water chunk {cx},{cz}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
                chunk = (go, new Mesh { name = go.name });
                go.GetComponent<MeshFilter>().sharedMesh = chunk.mesh;
                chunks[key] = chunk;
            }
            builder.ToMesh(chunk.go.name, chunk.mesh);
            chunk.mesh.RecalculateBounds();
        }
    }
}
