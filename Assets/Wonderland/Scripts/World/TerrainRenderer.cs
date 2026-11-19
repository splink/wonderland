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

        private RectInt laidOut;   // the grid (global cells) the chunks were laid out for

        /// <summary>
        /// Recreates the chunk layout for the field's current grid (after it was resized). In an endless world the grid
        /// slides by whole chunks: a chunk still in it - and not on the edge it had, where its neighbours were missing -
        /// keeps its mesh (vertices are in world coordinates), so only the land coming into the grid is built.
        /// </summary>
        public void Reinit()
        {
            var keep = new Dictionary<Vector2Int, Chunk>();
            if (chunks != null)
                foreach (var c in chunks)
                {
                    bool inner = c.X0 > 0 && c.Z0 > 0 && c.X0 + c.W < laidOut.width - 1 && c.Z0 + c.H < laidOut.height - 1;
                    var at = new Vector2Int(laidOut.x + c.X0, laidOut.y + c.Z0);
                    if (field.Endless && inner && c.W == ChunkCells && c.H == ChunkCells) { keep[at] = c; continue; }
                    if (Application.isPlaying) { Destroy(c.Renderer.gameObject); Destroy(c.Mesh); }
                    else { DestroyImmediate(c.Renderer.gameObject); DestroyImmediate(c.Mesh); }
                }
            laidOut = field.Cells;
            chunksX = Mathf.CeilToInt((field.Nx - 1) / (float)ChunkCells);
            chunksZ = Mathf.CeilToInt((field.Nz - 1) / (float)ChunkCells);
            chunks = new Chunk[chunksX * chunksZ];
            var build = new List<RectInt>();
            for (int cz = 0; cz < chunksZ; cz++)
            for (int cx = 0; cx < chunksX; cx++)
            {
                int kx0 = cx * ChunkCells, kz0 = cz * ChunkCells;
                if (keep.Remove(new Vector2Int(laidOut.x + kx0, laidOut.y + kz0), out var kept) && kept.W == Mathf.Min(ChunkCells, field.Nx - 1 - kx0) && kept.H == Mathf.Min(ChunkCells, field.Nz - 1 - kz0))
                {
                    kept.X0 = kx0; kept.Z0 = kz0;
                    chunks[cz * chunksX + cx] = kept;
                    continue;
                }
                build.Add(new RectInt(kx0, kz0, ChunkCells, ChunkCells));
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
            foreach (var c in keep.Values)
            {
                if (Application.isPlaying) { Destroy(c.Renderer.gameObject); Destroy(c.Mesh); }
                else { DestroyImmediate(c.Renderer.gameObject); DestroyImmediate(c.Mesh); }
            }
            Rebuild(build);
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
            if (dirty.Count > 0 && cut != null) SetCut(cut);   // (the cut edge follows the land)
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
            // Sand only where water is near: a dry chunk needn't look for it vertex by vertex.
            bool wet = false;
            for (int z = Mathf.Max(0, c.Z0 - 4); z <= Mathf.Min(Nz - 1, c.Z0 + c.H + 4) && !wet; z++)
            for (int x = Mathf.Max(0, c.X0 - 4); x <= Mathf.Min(N - 1, c.X0 + c.W + 4) && !wet; x++)
                wet = field.Water[z * N + x] > 0.5f;
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
                c.Colours[v] = new Color32((byte)(Mathf.Clamp01(field.Soil[i]) * 255f), (byte)(field.Edge[i] * 255f), (byte)(cavity * 255f), (byte)(255 - (wet ? Beach(x, z) : 0f) * 255f));
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

        // ------------------------------------------------------------------ the cut edge

        /// <summary>Material for the cut edge (the world's lit material).</summary>
        public Material CutMaterial;
        private MeshRenderer cutRenderer;
        private Mesh cutMesh;
        private Rect? cut;

        /// <summary>
        /// The world cut off cleanly at the window: soil walls from the floor up to the land along its four sides (water
        /// showing where a pond meets the cut) and a base under it, like a model landscape sawn out of a bigger one.
        /// Null removes it.
        /// </summary>
        public void SetCut(Rect? window)
        {
            cut = window;
            if (window is not Rect r || CutMaterial == null)
            {
                if (cutRenderer != null) cutRenderer.enabled = false;
                return;
            }
            if (cutRenderer == null)
            {
                var go = new GameObject("Cut edge");
                go.transform.SetParent(transform, false);
                cutMesh = new Mesh { name = "Cut edge" };
                cutMesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = cutMesh;
                cutRenderer = go.AddComponent<MeshRenderer>();
                cutRenderer.sharedMaterial = CutMaterial;
                cutRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            cutRenderer.enabled = true;
            const float inset = 0.0002f;   // (just inside the window, so the shaders' cut keeps it)
            r = Rect.MinMaxRect(r.xMin + inset, r.yMin + inset, r.xMax - inset, r.yMax - inset);
            var b = new MeshBuilder();
            var grass = new Color(0.36f, 0.48f, 0.22f); var topsoil = new Color(0.45f, 0.33f, 0.21f);
            var soil = new Color(0.37f, 0.27f, 0.18f); var deep = new Color(0.29f, 0.22f, 0.16f);
            var water = new Color(0.32f, 0.52f, 0.62f);
            void Side(Vector2 from, Vector2 to, Vector3 outward)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt((to - from).magnitude / field.Cell));
                int prev = -1; float prevTop = 0f, prevLevel = -1f; Vector2 prevP = default;
                for (int k = 0; k <= n; k++)
                {
                    var p = Vector2.Lerp(from, to, k / (float)n);
                    float top = Mathf.Max(field.SurfaceAt(p.x, p.y), 0.002f);
                    float lip = Mathf.Max(0f, top - 0.0035f);
                    int v = b.AddVertex(new Vector3(p.x, top, p.y), outward, grass);
                    b.AddVertex(new Vector3(p.x, lip, p.y), outward, topsoil);
                    b.AddVertex(new Vector3(p.x, top * 0.45f, p.y), outward, soil);
                    b.AddVertex(new Vector3(p.x, 0f, p.y), outward, deep);
                    if (prev >= 0)
                        for (int band = 0; band < 3; band++)
                        {
                            b.AddTriangle(prev + band, v + band, v + band + 1);
                            b.AddTriangle(prev + band, v + band + 1, prev + band + 1);
                        }
                    // A pond reaching the cut: its water shows as a pane above the bed.
                    float level = field.WaterAt(p.x, p.y) > 0.3f ? field.WaterLevelAt(p.x, p.y) : -1f;
                    if (prev >= 0 && level > top + 0.001f && prevLevel > prevTop + 0.001f)
                        b.AddQuad(new Vector3(prevP.x, prevLevel, prevP.y), new Vector3(p.x, level, p.y), new Vector3(p.x, top, p.y), new Vector3(prevP.x, prevTop, prevP.y), water);
                    prev = v; prevTop = top; prevLevel = level; prevP = p;
                }
            }
            // Wound so each wall faces out of the window.
            Side(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), Vector3.back);
            Side(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), Vector3.right);
            Side(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), Vector3.forward);
            Side(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), Vector3.left);
            // The base, seen from below when the world is lifted off the floor.
            int a0 = b.AddVertex(new Vector3(r.xMin, 0f, r.yMin), Vector3.down, deep);
            int a1 = b.AddVertex(new Vector3(r.xMax, 0f, r.yMin), Vector3.down, deep);
            int a2 = b.AddVertex(new Vector3(r.xMax, 0f, r.yMax), Vector3.down, deep);
            int a3 = b.AddVertex(new Vector3(r.xMin, 0f, r.yMax), Vector3.down, deep);
            b.AddTriangle(a0, a1, a2); b.AddTriangle(a0, a2, a3);
            b.ToMesh("Cut edge", cutMesh);
            cutMesh.RecalculateBounds();
        }

        /// <summary>Visible chunk meshes and triangles (budget reporting).</summary>
        public (int chunks, int triangles) Stats()
        {
            int n = 0, t = 0;
            foreach (var c in chunks) if (c.Renderer.enabled) { n++; t += (int)c.Mesh.GetIndexCount(0) / 3; }
            return (n, t);
        }
    }
}
