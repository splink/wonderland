using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Renders a <see cref="TerrainField"/> as one dynamic mesh (97 x 97 vertices). Only the touched region is recomputed
    /// while brushing. Vertex colour carries per-vertex masks for Wonderland/Terrain (railway soil, rim, cavity).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class TerrainRenderer : MonoBehaviour
    {
        private const int N = TerrainField.N;
        private TerrainField field;
        private Mesh mesh;
        private Vector3[] vertices;
        private Vector3[] normals;
        private Color32[] colours;
        private MeshRenderer meshRenderer;
        private Material material;
        private static readonly int BrushPos = Shader.PropertyToID("_BrushPos");
        private static readonly int BrushColor = Shader.PropertyToID("_BrushColor");

        public void Init(TerrainField terrain, Material sharedMaterial)
        {
            field = terrain;
            meshRenderer = GetComponent<MeshRenderer>();
            material = new Material(sharedMaterial) { name = "Terrain (instance)" };
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            mesh = new Mesh { name = "Terrain", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            vertices = new Vector3[N * N]; normals = new Vector3[N * N]; colours = new Color32[N * N];
            var tris = new List<int>();
            for (int z = 0; z < N - 1; z++)
            for (int x = 0; x < N - 1; x++)
            {
                int i = z * N + x;
                // Skip cells entirely outside the organic rim.
                if (field.Edge[i] <= 0f && field.Edge[i + 1] <= 0f && field.Edge[i + N] <= 0f && field.Edge[i + N + 1] <= 0f) continue;
                // Alternate the diagonal for a less regular look.
                if (((x + z) & 1) == 0) { tris.Add(i); tris.Add(i + N); tris.Add(i + N + 1); tris.Add(i); tris.Add(i + N + 1); tris.Add(i + 1); }
                else { tris.Add(i); tris.Add(i + N); tris.Add(i + 1); tris.Add(i + 1); tris.Add(i + N); tris.Add(i + N + 1); }
            }
            Rebuild(new RectInt(0, 0, N, N));
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        public void RebuildAll() => Rebuild(new RectInt(0, 0, N, N));

        public void Rebuild(RectInt region)
        {
            int x0 = Mathf.Max(0, region.xMin - 1), x1 = Mathf.Min(N, region.xMax + 1);
            int z0 = Mathf.Max(0, region.yMin - 1), z1 = Mathf.Min(N, region.yMax + 1);
            var s = field.Surface;
            float cell = field.Cell;
            for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
            {
                int i = z * N + x;
                float h = s[i];
                float hl = x > 0 ? s[i - 1] : h, hr = x < N - 1 ? s[i + 1] : h;
                float hd = z > 0 ? s[i - N] : h, hu = z < N - 1 ? s[i + N] : h;
                vertices[i] = new Vector3(field.LocalX(x), h, field.LocalZ(z));
                normals[i] = new Vector3(hl - hr, 2f * cell, hd - hu).normalized;
                float laplacian = (hl + hr + hd + hu - 4f * h) / (cell * cell);
                float cavity = Mathf.Clamp01(0.5f - laplacian * 0.05f);
                colours[i] = new Color32((byte)(Mathf.Clamp01(field.Soil[i]) * 255f), (byte)(field.Edge[i] * 255f), (byte)(cavity * 255f), 255);
            }
            if (mesh == null) return;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colours);
            mesh.bounds = new Bounds(new Vector3(0f, TerrainField.MaxHeight * 0.5f, 0f), new Vector3(field.Size, TerrainField.MaxHeight + 0.02f, field.Size));
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
    }
}
