using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Accumulates vertex-coloured geometry for the procedural generators. Colours are authored in sRGB (Wonderland/Lit
    /// linearises them); alpha is an emission mask: 1 = no glow, 0 = full glow (lit windows, lamps).
    /// Unity front faces wind clockwise as seen from outside: for a face (a, b, c), cross(b - a, c - a) points outwards.
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Color32> Colors = new();
        public readonly List<int> Triangles = new();
        /// <summary>UV0.x = wind sway weight (0 rigid, 1 a leafy canopy); read by Wonderland/Lit.</summary>
        public readonly List<Vector2> Uvs = new();
        /// <summary>Sway weight given to vertices added from now on.</summary>
        public float Sway;
        private readonly Stack<Matrix4x4> stack = new();
        private Matrix4x4 matrix = Matrix4x4.identity, normalMatrix = Matrix4x4.identity;

        public int VertexCount => Vertices.Count;

        public Matrix4x4 Matrix
        {
            get => matrix;
            set { matrix = value; normalMatrix = value.inverse.transpose; }
        }

        public void Push(Vector3 position, Quaternion rotation, Vector3 scale) => Push(Matrix4x4.TRS(position, rotation, scale));
        public void Push(Vector3 position, Quaternion rotation) => Push(Matrix4x4.TRS(position, rotation, Vector3.one));
        public void Push(Vector3 position) => Push(Matrix4x4.Translate(position));
        public void Push(Matrix4x4 local) { stack.Push(matrix); Matrix = matrix * local; }
        public void Pop() => Matrix = stack.Pop();

        public void Clear()
        {
            Vertices.Clear(); Normals.Clear(); Colors.Clear(); Triangles.Clear(); Uvs.Clear(); stack.Clear(); Sway = 0f;
            Matrix = Matrix4x4.identity;
        }

        public int AddVertex(Vector3 position, Vector3 normal, Color color)
        {
            Vertices.Add(matrix.MultiplyPoint3x4(position));
            Normals.Add(SafeNormalize(normalMatrix.MultiplyVector(normal)));
            Colors.Add(color);
            Uvs.Add(new Vector2(Sway, 0f));
            return Vertices.Count - 1;
        }

        public void AddTriangle(int a, int b, int c) { Triangles.Add(a); Triangles.Add(b); Triangles.Add(c); }

        /// <summary>Copies a finished mesh in (placed by <paramref name="at"/>).</summary>
        public void AddMesh(Mesh mesh, Matrix4x4 at)
        {
            Push(at);
            var v = mesh.vertices; var n = mesh.normals; var c = mesh.colors32; var t = mesh.triangles;
            int first = VertexCount;
            for (int i = 0; i < v.Length; i++) AddVertex(v[i], n.Length > i ? n[i] : Vector3.up, c.Length > i ? c[i] : (Color32)Color.white);
            for (int i = 0; i < t.Length; i += 3) AddTriangle(first + t[i], first + t[i + 1], first + t[i + 2]);
            Pop();
        }

        /// <summary>Moves every vertex from <paramref name="firstVertex"/> on through <paramref name="warp"/> (hand-made irregularity).</summary>
        public void Warp(int firstVertex, System.Func<Vector3, Vector3> warp)
        {
            for (int i = firstVertex; i < VertexCount; i++) Vertices[i] = warp(Vertices[i]);
        }

        /// <summary>Smooth normals from the faces that use vertices at or after <paramref name="firstVertex"/> (after reshaping).</summary>
        public void RecalculateNormals(int firstVertex)
        {
            var acc = new Vector3[VertexCount - firstVertex];
            for (int t = 0; t < Triangles.Count; t += 3)
            {
                int a = Triangles[t], b = Triangles[t + 1], c = Triangles[t + 2];
                if (a < firstVertex || b < firstVertex || c < firstVertex) continue;
                var n = Vector3.Cross(Vertices[b] - Vertices[a], Vertices[c] - Vertices[a]);
                acc[a - firstVertex] += n; acc[b - firstVertex] += n; acc[c - firstVertex] += n;
            }
            for (int i = 0; i < acc.Length; i++)
                if (acc[i].sqrMagnitude > 0f) Normals[firstVertex + i] = SafeNormalize(acc[i]);
        }

        /// <summary>Flat triangle; its winding is chosen so the face points away from <paramref name="inside"/>.</summary>
        public void AddTriangleOutward(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, Color color)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0f) { (b, c) = (c, b); n = -n; }
            n = SafeNormalize(n);
            AddTriangle(AddVertex(a, n, color), AddVertex(b, n, color), AddVertex(c, n, color));
        }

        /// <summary>Flat quad with corners in clockwise order as seen from its front.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            var n = SafeNormalize(Vector3.Cross(b - a, c - a));
            int i = AddVertex(a, n, color); AddVertex(b, n, color); AddVertex(c, n, color); AddVertex(d, n, color);
            AddTriangle(i, i + 1, i + 2); AddTriangle(i, i + 2, i + 3);
        }

        /// <summary>Rectangle centred on <paramref name="center"/> facing <paramref name="normal"/>; <paramref name="up"/> spans its height.</summary>
        public void AddFace(Vector3 center, Vector3 normal, Vector3 up, float halfWidth, float halfHeight, Color color)
        {
            var right = Vector3.Cross(normal, up);
            AddQuad(center - right * halfWidth - up * halfHeight, center - right * halfWidth + up * halfHeight,
                    center + right * halfWidth + up * halfHeight, center + right * halfWidth - up * halfHeight, color);
        }

        public void AddBox(Vector3 center, Vector3 size, Color color) => AddBox(center, size, color, color, color);

        /// <summary>Axis-aligned box (in the current matrix) with separate top and bottom colours; <paramref name="skipBottom"/> drops hidden faces.</summary>
        public void AddBox(Vector3 center, Vector3 size, Color side, Color top, Color bottom, bool skipBottom = true)
        {
            var h = size * 0.5f;
            AddFace(center + Vector3.up * h.y, Vector3.up, Vector3.forward, h.x, h.z, top);
            if (!skipBottom) AddFace(center - Vector3.up * h.y, Vector3.down, Vector3.forward, h.x, h.z, bottom);
            AddFace(center + Vector3.right * h.x, Vector3.right, Vector3.up, h.z, h.y, side);
            AddFace(center - Vector3.right * h.x, Vector3.left, Vector3.up, h.z, h.y, side);
            AddFace(center + Vector3.forward * h.z, Vector3.forward, Vector3.up, h.x, h.y, side);
            AddFace(center - Vector3.forward * h.z, Vector3.back, Vector3.up, h.x, h.y, side);
        }

        /// <summary>
        /// Upright (tapered) cylinder or cone from <paramref name="baseCenter"/>; <paramref name="topColor"/> shades the upper
        /// ring, so a gradient from base to top is free.
        /// </summary>
        public void AddCylinder(Vector3 baseCenter, float bottomRadius, float topRadius, float height, int segments,
            Color baseColor, Color topColor, bool capTop = true, bool capBottom = false, bool smooth = true, float phase = 0f,
            float edgeWave = 0f)
        {
            float slope = (bottomRadius - topRadius) / Mathf.Max(height, 1e-5f);
            int start = VertexCount;
            for (int i = 0; i <= segments; i++)
            {
                float a = phase + i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var n = new Vector3(dir.x, slope, dir.z).normalized;
                // Optional star-shaped rim (branch tips on spruce tiers): alternate radii, tips droop a little.
                float wave = 1f + edgeWave * ((i % segments) % 2 == 0 ? 1f : -1f);
                AddVertex(baseCenter + dir * bottomRadius * wave - Vector3.up * (wave - 1f) * height * 0.6f, n, baseColor);
                AddVertex(baseCenter + dir * topRadius + Vector3.up * height, n, topColor);
            }
            if (smooth)
            {
                for (int i = 0; i < segments; i++)
                {
                    int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                    AddTriangle(b0, t0, t1); AddTriangle(b0, t1, b1);
                }
            }
            else
            {
                // Faceted: rebuild the side as flat quads (the smooth ring above is discarded).
                Vertices.RemoveRange(start, VertexCount - start); Normals.RemoveRange(start, Normals.Count - start);
                Colors.RemoveRange(start, Colors.Count - start); Uvs.RemoveRange(start, Uvs.Count - start);
                for (int i = 0; i < segments; i++)
                {
                    float a0 = phase + i * Mathf.PI * 2f / segments, a1 = phase + (i + 1) * Mathf.PI * 2f / segments;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    var p0 = baseCenter + d0 * bottomRadius; var p1 = baseCenter + d1 * bottomRadius;
                    var q0 = baseCenter + d0 * topRadius + Vector3.up * height; var q1 = baseCenter + d1 * topRadius + Vector3.up * height;
                    var n = Vector3.Cross(q0 - p0, q1 - p0);
                    if (n.sqrMagnitude < 1e-20f) n = Vector3.Cross(q0 - p0, p1 - p0);
                    n = SafeNormalize(n);
                    int v = AddVertex(p0, n, baseColor); AddVertex(q0, n, topColor); AddVertex(q1, n, topColor); AddVertex(p1, n, baseColor);
                    AddTriangle(v, v + 1, v + 2);
                    if (topRadius > 1e-5f) AddTriangle(v, v + 2, v + 3); else AddTriangle(v, v + 1, v + 3);
                }
            }
            if (capTop && topRadius > 1e-5f) AddDisc(baseCenter + Vector3.up * height, topRadius, segments, topColor, true, phase);
            if (capBottom && bottomRadius > 1e-5f) AddDisc(baseCenter, bottomRadius, segments, baseColor, false, phase);
        }

        public void AddDisc(Vector3 center, float radius, int segments, Color color, bool facingUp, float phase = 0f)
        {
            var n = facingUp ? Vector3.up : Vector3.down;
            int c = AddVertex(center, n, color);
            for (int i = 0; i <= segments; i++)
            {
                float a = phase + i * Mathf.PI * 2f / segments;
                AddVertex(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius, n, color);
            }
            for (int i = 0; i < segments; i++)
            {
                if (facingUp) AddTriangle(c, c + 2 + i, c + 1 + i);
                else AddTriangle(c, c + 1 + i, c + 2 + i);
            }
        }

        public delegate Color BlobColor(Vector3 direction, Vector3 localPosition);

        /// <summary>
        /// Smooth, noise-displaced ellipsoid (an icosphere): canopies, bushes, steam. <paramref name="lumpiness"/> is the
        /// relative displacement; normals are recomputed from the displaced surface so the lumps catch the light.
        /// </summary>
        public void AddBlob(Vector3 center, Vector3 radius, int subdivisions, float lumpiness, int seed, BlobColor color,
            float lumpFrequency = 2.2f)
        {
            var sphere = IcoSphere.Get(subdivisions);
            int start = VertexCount;
            var local = new Vector3[sphere.Vertices.Length];
            var offset = new Vector3(seed * 1.37f, seed * 2.11f, seed * 0.73f);
            for (int i = 0; i < local.Length; i++)
            {
                var d = sphere.Vertices[i];
                float n = Noise.Value3(d * lumpFrequency + offset) - 0.5f;
                float n2 = Noise.Value3(d * lumpFrequency * 2.3f + offset * 1.7f) - 0.5f;
                local[i] = center + Vector3.Scale(d, radius) * (1f + lumpiness * (n * 1.4f + n2 * 0.6f));
            }
            var normals = new Vector3[local.Length];
            var tris = sphere.Triangles;
            for (int t = 0; t < tris.Length; t += 3)
            {
                var fn = Vector3.Cross(local[tris[t + 1]] - local[tris[t]], local[tris[t + 2]] - local[tris[t]]);
                normals[tris[t]] += fn; normals[tris[t + 1]] += fn; normals[tris[t + 2]] += fn;
            }
            for (int i = 0; i < local.Length; i++)
                AddVertex(local[i], normals[i].sqrMagnitude > 0f ? normals[i] : sphere.Vertices[i], color(sphere.Vertices[i], local[i]));
            for (int t = 0; t < tris.Length; t += 3) AddTriangle(start + tris[t], start + tris[t + 1], start + tris[t + 2]);
        }

        /// <summary>Strip of quads along a polyline with a constant cross-section profile (rails, ballast, roof ridges).</summary>
        public void AddExtrusion(IReadOnlyList<Vector3> path, IReadOnlyList<Vector3> pathRight, IReadOnlyList<Vector3> pathUp,
            Vector2[] profile, Color[] profileColors, bool closed)
        {
            int count = path.Count;
            int segs = closed ? count : count - 1;
            for (int e = 0; e < profile.Length - 1; e++)
            {
                Vector2 a = profile[e], b = profile[e + 1];
                // Edge normal in profile space (x right, y up), pointing outwards for a clockwise profile.
                var edge = b - a; var pn = new Vector2(-edge.y, edge.x).normalized;
                int start = VertexCount;
                for (int i = 0; i < count; i++)
                {
                    var n = pathRight[i] * pn.x + pathUp[i] * pn.y;
                    AddVertex(path[i] + pathRight[i] * a.x + pathUp[i] * a.y, n, profileColors[e]);
                    AddVertex(path[i] + pathRight[i] * b.x + pathUp[i] * b.y, n, profileColors[e + 1]);
                }
                for (int i = 0; i < segs; i++)
                {
                    int i0 = start + i * 2, i1 = start + ((i + 1) % count) * 2;
                    AddTriangle(i0, i1, i1 + 1); AddTriangle(i0, i1 + 1, i0 + 1);
                }
            }
        }

        public Mesh ToMesh(string name, Mesh reuse = null)
        {
            var mesh = reuse != null ? reuse : new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = VertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Vertices); mesh.SetNormals(Normals); mesh.SetColors(Colors); mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Triangles, 0, true);
            return mesh;
        }

        /// <summary>
        /// Fast batching path for rigid, uniformly scaled copies (trees, ground cover): normals only need the rotation,
        /// so there is no matrix inverse and no per-vertex normalisation.
        /// </summary>
        public void AppendFast(MeshData source, Vector3 position, Quaternion rotation, float scale, Color tint)
        {
            int start = VertexCount;
            var v = source.Vertices; var n = source.Normals; var c = source.Colors; var uv = source.Uvs;
            bool hasUv = uv.Length == v.Length;
            Vertices.Capacity = Mathf.Max(Vertices.Capacity, start + v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                Vertices.Add(position + rotation * (v[i] * scale));
                Normals.Add(rotation * n[i]);
                var col = c[i];
                Colors.Add(new Color32((byte)Mathf.Min(255f, col.r * tint.r), (byte)Mathf.Min(255f, col.g * tint.g), (byte)Mathf.Min(255f, col.b * tint.b), col.a));
                Uvs.Add(hasUv ? uv[i] : Vector2.zero);
            }
            var t = source.Triangles;
            for (int i = 0; i < t.Length; i++) Triangles.Add(start + t[i]);
        }

        /// <summary>Appends cached geometry transformed by <paramref name="transform"/>, tinted (used to batch trees per chunk).</summary>
        public void Append(MeshData source, Matrix4x4 transform, Color tint)
        {
            var normalTransform = transform.inverse.transpose;
            int start = VertexCount;
            for (int i = 0; i < source.Vertices.Length; i++)
            {
                Vertices.Add(transform.MultiplyPoint3x4(source.Vertices[i]));
                Normals.Add(SafeNormalize(normalTransform.MultiplyVector(source.Normals[i])));
                var c = source.Colors[i];
                Colors.Add(new Color32((byte)Mathf.Min(255f, c.r * tint.r), (byte)Mathf.Min(255f, c.g * tint.g), (byte)Mathf.Min(255f, c.b * tint.b), c.a));
                Uvs.Add(source.Uvs.Length == source.Vertices.Length ? source.Uvs[i] : Vector2.zero);
            }
            foreach (int t in source.Triangles) Triangles.Add(start + t);
        }

        /// <summary>
        /// Normalises without Unity's 1e-5 cut-off: <c>Vector3.normalized</c> returns zero for the tiny cross products of
        /// millimetre-sized faces, which renders them black.
        /// </summary>
        public static Vector3 SafeNormalize(Vector3 v)
        {
            double x = v.x, y = v.y, z = v.z, m = System.Math.Sqrt(x * x + y * y + z * z);
            if (m < 1e-30) return Vector3.up;
            return new Vector3((float)(x / m), (float)(y / m), (float)(z / m));
        }

    }

    /// <summary>A mesh's arrays, read once so batching never touches the Mesh API per instance.</summary>
    public sealed class MeshData
    {
        public readonly Vector3[] Vertices, Normals;
        public readonly Color32[] Colors;
        public readonly Vector2[] Uvs;
        public readonly int[] Triangles;
        public MeshData(Mesh mesh) { Vertices = mesh.vertices; Normals = mesh.normals; Colors = mesh.colors32; Uvs = mesh.uv; Triangles = mesh.triangles; }
    }

    /// <summary>Unit icospheres, cached per subdivision level, wound outwards.</summary>
    public sealed class IcoSphere
    {
        public Vector3[] Vertices;
        public int[] Triangles;
        private static readonly Dictionary<int, IcoSphere> Cache = new();

        public static IcoSphere Get(int subdivisions)
        {
            if (Cache.TryGetValue(subdivisions, out var s)) return s;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var tris = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int level = 0; level < subdivisions; level++)
            {
                var mid = new Dictionary<long, int>();
                var next = new List<int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int m)) return m;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    mid[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = next;
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            }
            s = new IcoSphere { Vertices = verts.ToArray(), Triangles = tris.ToArray() };
            Cache[subdivisions] = s;
            return s;
        }
    }
}
