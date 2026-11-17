using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Simple stand-ins for detailed meshes, made from the meshes themselves: a building becomes a box in its wall colour
    /// with a gabled (or flat) roof in its roof colour and a band of lit windows; a tree becomes a trunk and a canopy in
    /// its own colours. A few dozen triangles instead of thousands - they cast the shadows (a shadow doesn't show the
    /// chimney pots); they are never drawn themselves.
    /// </summary>
    public static class MeshProxy
    {
        private static readonly Dictionary<Mesh, Mesh> buildings = new(), trees = new();

        /// <summary>
        /// The stand-in for a building mesh (cached per mesh): walls and roof in the colours that cover most of the real
        /// ones (weighted by area, so trim and stones don't wash them out), a gabled roof when the real one slopes, and
        /// <paramref name="extra"/> (its name board) on top.
        /// </summary>
        public static Mesh Building(Mesh full, Mesh extra = null)
        {
            if (full == null) return null;
            if (buildings.TryGetValue(full, out var cached) && cached != null) return cached;
            var b = full.bounds;
            var v = full.vertices; var c = full.colors32; var tri = full.triangles;
            float wallTop = b.min.y + b.size.y * 0.55f;
            var wall = new Accum(); var roof = new Accum(); var slopeArea = 0f; var flatArea = 0f;
            var slopes = new List<(float low, float high, float area)>();
            float facingX = 0f, facingZ = 0f;
            bool colours = c != null && c.Length == v.Length;
            for (int t = 0; t + 2 < tri.Length; t += 3)
            {
                Vector3 p0 = v[tri[t]], p1 = v[tri[t + 1]], p2 = v[tri[t + 2]];
                var cross = Vector3.Cross(p1 - p0, p2 - p0);
                float area = cross.magnitude * 0.5f;
                if (area < 1e-9f) continue;
                var nrm = cross / (area * 2f);
                float y = (p0.y + p1.y + p2.y) / 3f;
                var col = colours ? (Color)c[tri[t]] : Color.white;
                bool glows = colours && c[tri[t]].a < 200;
                if (Mathf.Abs(nrm.y) < 0.3f && y > b.min.y + 0.006f && y < wallTop && !glows) wall.Add(col, area);
                var mid = (p0 + p1 + p2) / 3f;
                bool inside = Mathf.Abs(mid.x - b.center.x) < b.size.x * 0.42f && Mathf.Abs(mid.z - b.center.z) < b.size.z * 0.42f;   // (not awnings, signs or porches)
                if (nrm.y > 0.3f && nrm.y < 0.93f && y > b.min.y + b.size.y * 0.35f && !glows && inside)
                {
                    roof.Add(col, area); slopeArea += area;
                    slopes.Add((Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)), Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)), area));
                    facingX += Mathf.Abs(nrm.x) * area; facingZ += Mathf.Abs(nrm.z) * area;
                }
                if (nrm.y >= 0.93f && y > b.min.y + b.size.y * 0.35f) { flatArea += area; if (!glows) roof.Add(col, area * 0.3f); }
            }
            var wallColour = wall.Mean(new Color(0.9f, 0.86f, 0.78f));
            var roofColour = roof.Mean(new Color(0.65f, 0.3f, 0.25f));
            // The eave and the ridge from the sloped roof itself: low and high area-weighted percentiles (chimney pots,
            // weathervanes and the odd sloped ornament don't move them).
            float roofEave = Percentile(slopes, 0.08f, true, b.max.y), roofTop = Percentile(slopes, 0.94f, false, b.max.y);
            bool gabled = slopeArea > flatArea * 0.6f && roofTop > roofEave + 0.01f;
            var m = new MeshBuilder();
            float fx = b.size.x * 0.42f, fz = b.size.z * 0.4f;
            var centre = new Vector3(b.center.x, 0f, b.center.z);
            float body = gabled ? Mathf.Max(b.min.y + 0.02f, roofEave) : Mathf.Max(wallTop, b.min.y + b.size.y * 0.6f);
            m.AddBox(centre + new Vector3(0f, (b.min.y + body) / 2f, 0f), new Vector3(fx * 2f, body - b.min.y, fz * 2f), wallColour, roofColour, wallColour);
            if (gabled)
            {
                // Two roof planes meeting at the ridge (along the longer side), and the gable ends.
                bool alongX = facingZ >= facingX;   // the slopes face front and back: the ridge runs side to side
                float half = (alongX ? fz : fx) + 0.005f, run = (alongX ? fx : fx) + 0.005f;
                if (!alongX) run = fz + 0.005f;
                Vector3 P(float a, float s2, float y) => centre + (alongX ? new Vector3(a, y, s2) : new Vector3(s2, y, a));
                Vector3 Across(float s2) => alongX ? new Vector3(0f, 0f, s2) : new Vector3(s2, 0f, 0f);
                foreach (int side in new[] { -1, 1 })
                    QuadOutward(m, P(-run, side * half, body), P(run, side * half, body), P(run, 0f, roofTop), P(-run, 0f, roofTop), Across(side) + Vector3.up, roofColour);
                foreach (int end in new[] { -1, 1 })
                {
                    var outward = alongX ? new Vector3(end, 0f, 0f) : new Vector3(0f, 0f, end);
                    m.AddTriangleOutward(P(end * (run - 0.005f), -half + 0.005f, body), P(end * (run - 0.005f), half - 0.005f, body), P(end * (run - 0.005f), 0f, roofTop - 0.002f), centre + new Vector3(0f, body, 0f), wallColour);
                }
            }
            // A band of windows front and back, glowing a little after dark like the real ones.
            var glow = new Color(1f, 0.86f, 0.55f, 0.6f);
            float wy = b.min.y + (body - b.min.y) * 0.55f;
            foreach (int s2 in new[] { -1, 1 })
                m.AddFace(centre + new Vector3(0f, wy, s2 * (fz + 0.0005f)), new Vector3(0f, 0f, s2), Vector3.up, fx * 0.6f, (body - b.min.y) * 0.12f, glow);
            if (extra != null) m.AddMesh(extra, Matrix4x4.Translate(new Vector3(0f, 0.004f, -0.012f)));   // (a little forward: in front of the simpler roof)
            var mesh = m.ToMesh(full.name + " proxy");
            buildings[full] = mesh;
            return mesh;
        }

        private static float Percentile(List<(float low, float high, float area)> list, float q, bool low, float fallback)
        {
            if (list.Count == 0) return fallback;
            var sorted = new List<(float y, float a)>(list.Count);
            foreach (var (lo, hi, a) in list) sorted.Add((low ? lo : hi, a));
            sorted.Sort((x, y) => x.y.CompareTo(y.y));
            float total = 0f; foreach (var e in sorted) total += e.a;
            float acc = 0f;
            foreach (var e in sorted) { acc += e.a; if (acc >= total * q) return e.y; }
            return sorted[^1].y;
        }

        private static void QuadOutward(MeshBuilder m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color colour)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0f) m.AddQuad(a, b, c, d, colour);
            else m.AddQuad(a, d, c, b, colour);
        }

        private struct Accum
        {
            private float r, g, b, w;
            public void Add(Color c, float weight) { r += c.r * weight; g += c.g * weight; b += c.b * weight; w += weight; }
            public Color Mean(Color fallback) => w > 0f ? new Color(r / w, g / w, b / w, 1f) : fallback;
        }

        /// <summary>The stand-in for a tree mesh: a trunk and a rounded (or, for conifers, pointed) canopy.</summary>
        public static Mesh Tree(Mesh full, bool pointed)
        {
            if (full == null) return null;
            if (trees.TryGetValue(full, out var cached) && cached != null) return cached;
            var b = full.bounds;
            var v = full.vertices; var n = full.normals; var c = full.colors32;
            float split = b.min.y + b.size.y * 0.3f;
            Color trunk = Average(v, n, c, i => v[i].y < split, new Color(0.45f, 0.32f, 0.22f));
            Color leaves = Average(v, n, c, i => v[i].y > split, new Color(0.35f, 0.6f, 0.3f));
            var m = new MeshBuilder();
            float r = Mathf.Max(b.size.x, b.size.z) * 0.45f;
            m.AddCylinder(new Vector3(b.center.x, b.min.y, b.center.z), r * 0.15f, r * 0.12f, b.size.y * 0.4f, 5, trunk, trunk, false);
            if (pointed) m.AddCylinder(new Vector3(b.center.x, split, b.center.z), r, 0f, b.max.y - split, 7, Pal.Shade(leaves, 0.85f), Pal.Shade(leaves, 1.1f), false, true);
            else m.AddBlob(new Vector3(b.center.x, (split + b.max.y) / 2f, b.center.z), new Vector3(r, (b.max.y - split) * 0.55f, r), 1, 0.05f, 3, (d, _) => Pal.Shade(leaves, 0.85f + d.y * 0.2f));
            var mesh = m.ToMesh(full.name + " proxy");
            trees[full] = mesh;
            return mesh;
        }

        /// <summary>
        /// The stand-in casts the shadow of a detailed renderer (the detail casts none): a shadow on the grass doesn't
        /// show the chimney pots, and it costs a few dozen triangles instead of thousands. The stand-in is never seen.
        /// </summary>
        public static void Shadow(MeshRenderer detail, Mesh proxy)
        {
            detail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Child(detail.transform, "Shadow", proxy, detail.sharedMaterial, UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly);
        }

        private static MeshRenderer Child(Transform parent, string name, Mesh mesh, Material material, UnityEngine.Rendering.ShadowCastingMode shadows)
        {
            var t = parent.Find(name);
            var go = t != null ? t.gameObject : new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            if (!go.TryGetComponent<MeshFilter>(out var f)) f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            if (!go.TryGetComponent<MeshRenderer>(out var r)) r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = shadows; r.receiveShadows = shadows != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            return r;
        }

        private static Color Average(Vector3[] v, Vector3[] n, Color32[] c, System.Func<int, bool> pick, Color fallback)
        {
            if (c == null || c.Length != v.Length || n.Length != v.Length) return fallback;
            float r = 0f, g = 0f, bl = 0f; int k = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (!pick(i) || c[i].a < 200) continue;   // (glowing windows and lamps don't count)
                r += c[i].r; g += c[i].g; bl += c[i].b; k++;
            }
            return k == 0 ? fallback : new Color(r / k / 255f, g / k / 255f, bl / k / 255f, 1f);
        }
    }
}
