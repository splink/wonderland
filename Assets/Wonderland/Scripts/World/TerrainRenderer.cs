using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Renders a <see cref="TerrainField"/> as chunk meshes of up to 60 x 60 cells. A brush stroke recomputes and
    /// re-uploads only the chunks it touches; triangles exist only where there is land, so the bare floor around a small
    /// island costs nothing. Vertex colour carries per-vertex masks for Wonderland/Terrain (railway soil, rim, cavity).
    /// </summary>
    public sealed class TerrainRenderer : MonoBehaviour
    {
        private const int ChunkCells = 60;
        private const int V = ChunkCells + 1;

        private TerrainField field;
        private Material material;
        private Chunk[] chunks;
        private int chunksX, chunksZ;
        private readonly List<int> triangles = new();
        private static readonly int BrushPos = Shader.PropertyToID("_BrushPos");
        private static readonly int BrushColor = Shader.PropertyToID("_BrushColor");

        private sealed class Chunk
        {
            public int X0, Z0, W, H;   // first cell and size in cells
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public readonly Vector3[] Vertices = new Vector3[V * V];
            public readonly Vector3[] Normals = new Vector3[V * V];
            public readonly Color32[] Colours = new Color32[V * V];
        }

        public void Init(TerrainField terrain, Material sharedMaterial)
        {
            TerrainNoise.Ensure();
            field = terrain;
            material ??= new Material(sharedMaterial) { name = "Terrain (instance)" };
            Reinit();
        }

        /// <summary>Recreates the chunk layout for the field's current grid (after it was resized).</summary>
        public void Reinit()
        {
            if (chunks != null)
                foreach (var c in chunks)
                {
                    if (Application.isPlaying) { Destroy(c.Renderer.gameObject); Destroy(c.Mesh); }
                    else { DestroyImmediate(c.Renderer.gameObject); DestroyImmediate(c.Mesh); }
                }
            chunksX = Mathf.CeilToInt((field.Nx - 1) / (float)ChunkCells);
            chunksZ = Mathf.CeilToInt((field.Nz - 1) / (float)ChunkCells);
            chunks = new Chunk[chunksX * chunksZ];
            for (int cz = 0; cz < chunksZ; cz++)
            for (int cx = 0; cx < chunksX; cx++)
            {
                var go = new GameObject($"Terrain chunk {cx},{cz}");
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = go.name };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                int x0 = cx * ChunkCells, z0 = cz * ChunkCells;
                chunks[cz * chunksX + cx] = new Chunk
                {
                    X0 = x0, Z0 = z0, W = Mathf.Min(ChunkCells, field.Nx - 1 - x0), H = Mathf.Min(ChunkCells, field.Nz - 1 - z0), Mesh = mesh, Renderer = r,
                };
            }
            RebuildAll();
        }

        public void RebuildAll() => Rebuild(field.All);

        /// <summary>Recomputes vertices (and triangles: the land may have changed) of the chunks overlapping the region.</summary>
        public void Rebuild(RectInt region) => Rebuild(new[] { region });

        /// <summary>Rebuilds the chunks any of <paramref name="regions"/> touch, each chunk once.</summary>
        public void Rebuild(IEnumerable<RectInt> regions)
        {
            var dirty = new SortedSet<int>();
            foreach (var region in regions)
            {
                if (region.width <= 0 || region.height <= 0) continue;
                int x0 = Mathf.Max(0, region.xMin - 1), x1 = Mathf.Min(field.Nx - 1, region.xMax);
                int z0 = Mathf.Max(0, region.yMin - 1), z1 = Mathf.Min(field.Nz - 1, region.yMax);
                for (int cz = z0 / ChunkCells; cz <= Mathf.Min(chunksZ - 1, z1 / ChunkCells); cz++)
                for (int cx = x0 / ChunkCells; cx <= Mathf.Min(chunksX - 1, x1 / ChunkCells); cx++)
                    dirty.Add(cz * chunksX + cx);
            }
            foreach (int c in dirty) RebuildChunk(chunks[c]);
        }

        /// <summary>Sand along pond shores: land within a few centimetres of water, above it but not much higher.</summary>
        private float Beach(int x, int z)
        {
            var water = field.Water;
            int N = field.Nx, Nz = field.Nz, i = z * N + x;
            if (water[i] > 0.5f) return 0.6f;   // the shallow bed shows sand through the water
            float best = 0f;
            for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int xx = x + dx, zz = z + dz;
                if (xx < 0 || zz < 0 || xx >= N || zz >= Nz) continue;
                int j = zz * N + xx;
                if (water[j] < 0.5f) continue;
                float near = 1f - Mathf.Sqrt(dx * dx + dz * dz) / 4.2f;
                float above = field.Surface[i] - field.WaterLevel[j];
                if (above > 0.03f) continue;
                best = Mathf.Max(best, near);
            }
            return Mathf.Clamp01(best * 1.3f);
        }

        private void RebuildChunk(Chunk c)
        {
            var s = field.Surface;
            float cell = field.Cell;
            int N = field.Nx, Nz = field.Nz;
            for (int lz = 0; lz <= c.H; lz++)
            for (int lx = 0; lx <= c.W; lx++)
            {
                int x = c.X0 + lx, z = c.Z0 + lz, i = z * N + x, v = lz * V + lx;
                float h = s[i];
                float hl = x > 0 ? s[i - 1] : h, hr = x < N - 1 ? s[i + 1] : h;
                float hd = z > 0 ? s[i - N] : h, hu = z < Nz - 1 ? s[i + N] : h;
                c.Vertices[v] = new Vector3(field.LocalX(x), h, field.LocalZ(z));
                c.Normals[v] = new Vector3(hl - hr, 2f * cell, hd - hu).normalized;
                float laplacian = (hl + hr + hd + hu - 4f * h) / (cell * cell);
                float cavity = Mathf.Clamp01(0.5f - laplacian * 0.05f);
                c.Colours[v] = new Color32((byte)(Mathf.Clamp01(field.Soil[i]) * 255f), (byte)(field.Edge[i] * 255f), (byte)(cavity * 255f), (byte)(255 - Beach(x, z) * 255f));
            }
            triangles.Clear();
            var edge = field.Edge;
            for (int lz = 0; lz < c.H; lz++)
            for (int lx = 0; lx < c.W; lx++)
            {
                int i = (c.Z0 + lz) * N + c.X0 + lx;
                // Only cells touching land; the rim fades to transparent before the stepped boundary shows.
                if (edge[i] <= 0f && edge[i + 1] <= 0f && edge[i + N] <= 0f && edge[i + N + 1] <= 0f) continue;
                int v = lz * V + lx;
                // Alternate the diagonal for a less regular look.
                if (((lx + lz) & 1) == 0) { triangles.Add(v); triangles.Add(v + V); triangles.Add(v + V + 1); triangles.Add(v); triangles.Add(v + V + 1); triangles.Add(v + 1); }
                else { triangles.Add(v); triangles.Add(v + V); triangles.Add(v + 1); triangles.Add(v + 1); triangles.Add(v + V); triangles.Add(v + V + 1); }
            }
            var mesh = c.Mesh;
            mesh.Clear();
            if (triangles.Count == 0) { c.Renderer.enabled = false; return; }
            c.Renderer.enabled = true;
            mesh.SetVertices(c.Vertices);
            mesh.SetNormals(c.Normals);
            mesh.SetColors(c.Colours);
            mesh.SetTriangles(triangles, 0, false);
            float cx = field.LocalX(c.X0) + c.W * cell * 0.5f, cz = field.LocalZ(c.Z0) + c.H * cell * 0.5f;
            mesh.bounds = new Bounds(new Vector3(cx, TerrainField.MaxHeight * 0.5f, cz), new Vector3(c.W * cell, TerrainField.MaxHeight + 0.02f, c.H * cell));
        }

        /// <summary>Brush ring drawn by the terrain shader (local position, radius); intensity 0 hides it.</summary>
        public void SetBrush(Vector3 localCentre, float radius, Color colour, float intensity)
        {
            if (material == null) return;
            material.SetVector(BrushPos, new Vector4(localCentre.x, localCentre.y, localCentre.z, radius));
            // A vector, so Unity does not linearise it; the shader converts the sRGB colour itself.
            material.SetVector(BrushColor, new Vector4(colour.r, colour.g, colour.b, intensity));
        }

        public void HideBrush() => SetBrush(Vector3.zero, 0f, Color.black, 0f);

        /// <summary>Visible chunk meshes and triangles (budget reporting).</summary>
        public (int chunks, int triangles) Stats()
        {
            int n = 0, t = 0;
            foreach (var c in chunks) if (c.Renderer.enabled) { n++; t += (int)c.Mesh.GetIndexCount(0) / 3; }
            return (n, t);
        }
    }
}
