using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Small hand-made plant details shared by trees (undergrowth) and the meadow (ground cover): grass, flowers,
    /// lavender, tulips, ferns, toadstools, and the clustered leaf puffs that give canopies a scalloped, leafy silhouette.
    /// Everything that moves in the wind sets <see cref="MeshBuilder.Sway"/> (0 rigid, 1 free).
    /// </summary>
    public static class Flora
    {
        public delegate Color Paint(Vector3 direction, Vector3 position);

        /// <summary>
        /// A bunch of leaves: one smooth mesh pushed out into several rounded lobes (biased outwards and upwards), so the
        /// outline is scalloped like clustered leaves; lobe tops catch light, crevices between lobes go dark.
        /// </summary>
        public static void LeafCluster(MeshBuilder b, Vector3 centre, float radius, Vector3 outward, int lobes, ref Rng rng, Paint paint, float stretchY = 0.85f)
        {
            var sphere = IcoSphere.Get(2);
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.up;
            lobes = Mathf.Clamp(lobes + 3, 5, 10);
            var dirs = new Vector3[lobes];
            for (int i = 0; i < lobes; i++)
                dirs[i] = (outward * 0.7f + Vector3.up * 0.45f + new Vector3(rng.Range(-1f, 1f), rng.Range(-0.6f, 1f), rng.Range(-1f, 1f))).normalized;
            float seed = rng.Range(0f, 100f);
            int start = b.VertexCount;
            var local = new Vector3[sphere.Vertices.Length];
            var bump = new float[local.Length];
            for (int v = 0; v < local.Length; v++)
            {
                var d = sphere.Vertices[v];
                float best = 0f, second = 0f;
                foreach (var l in dirs)
                {
                    float x = Mathf.Clamp01((Vector3.Dot(d, l) - 0.4f) / 0.6f);
                    x = x * x * (3f - 2f * x);
                    if (x > best) { second = best; best = x; } else if (x > second) second = x;
                }
                float n = Noise.Value3(d * 3.1f + new Vector3(seed, 0f, seed)) - 0.5f;
                float r = 0.7f + 0.42f * best + 0.06f * n;
                local[v] = centre + Vector3.Scale(d, new Vector3(radius, radius * stretchY, radius)) * r;
                bump[v] = best - second * 0.8f;   // high on lobe tops, low in the crevices between lobes
            }
            var normals = new Vector3[local.Length];
            var tris = sphere.Triangles;
            for (int t = 0; t < tris.Length; t += 3)
            {
                var fn = Vector3.Cross(local[tris[t + 1]] - local[tris[t]], local[tris[t + 2]] - local[tris[t]]);
                normals[tris[t]] += fn; normals[tris[t + 1]] += fn; normals[tris[t + 2]] += fn;
            }
            for (int v = 0; v < local.Length; v++)
            {
                var c = paint(sphere.Vertices[v], local[v]);
                c = Pal.Shade(c, Mathf.Lerp(0.74f, 1.14f, Mathf.Clamp01(bump[v] * 1.4f + 0.2f)));
                // Painted leaf clumps: light dabs on the upper, outer leaves.
                float dab = Noise.Value3(local[v] * 380f + new Vector3(seed, seed, 0f));
                if (dab > 0.68f && sphere.Vertices[v].y > -0.2f) c = Color.Lerp(c, Pal.Shade(c, 1.35f) + new Color(0.04f, 0.05f, 0f), (dab - 0.68f) * 2.6f);
                b.AddVertex(local[v], normals[v], c);
            }
            for (int t = 0; t < tris.Length; t += 3) b.AddTriangle(start + tris[t], start + tris[t + 1], start + tris[t + 2]);
        }

        /// <summary>Two-sided blades leaning outwards; dark roots, sunlit tips that sway.</summary>
        public static void GrassTuft(MeshBuilder b, Vector3 pos, float scale, ref Rng rng, int variant = 0)
        {
            int blades = 5 + variant * 2;
            var root = Pal.Hex(variant == 0 ? "#2F6326" : "#3A6E2A");
            var tip = Pal.Hex(variant == 0 ? "#8FBF4A" : "#B5C95A");
            for (int i = 0; i < blades; i++)
            {
                float a = i / (float)blades * Mathf.PI * 2f + rng.Range(-0.3f, 0.3f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, dir) * rng.Range(0.0011f, 0.0016f) * scale;
                var basePos = pos + dir * rng.Range(0f, 0.002f) * scale;
                float h = rng.Range(0.007f, 0.013f) * scale;
                var top = basePos + dir * h * rng.Range(0.35f, 0.6f) + Vector3.up * h;
                var c = Pal.Jitter(tip, ref rng, 0.25f);
                Blade(b, basePos - side, basePos + side, top, root, c);
            }
        }

        private static void Blade(MeshBuilder b, Vector3 left, Vector3 right, Vector3 top, Color root, Color tip, float tipSway = 1f)
        {
            float sway = b.Sway;
            foreach (bool front in new[] { true, false })
            {
                b.Sway = 0f;
                int v = b.AddVertex(left, Vector3.up, root);
                b.AddVertex(right, Vector3.up, root);
                b.Sway = tipSway;
                b.AddVertex(top, Vector3.up, tip);
                if (front) b.AddTriangle(v, v + 2, v + 1); else b.AddTriangle(v, v + 1, v + 2);
            }
            b.Sway = sway;
        }

        /// <summary>A leafy clump with a few stems of round flower heads (yellow-eyed).</summary>
        public static void Flowers(MeshBuilder b, Vector3 pos, Color flower, float scale, ref Rng rng, int stems = 4)
        {
            var leaf = Pal.Hex("#447F2E");
            b.Sway = 0.2f;
            b.AddBlob(pos + new Vector3(0f, 0.002f, 0f) * scale, new Vector3(0.005f, 0.0028f, 0.005f) * scale, 1, 0.25f, rng.Range(0, 999),
                (d, _) => Color.Lerp(Pal.Shade(leaf, 0.7f), leaf, d.y * 0.5f + 0.5f));
            for (int i = 0; i < stems; i++)
            {
                var p = pos + new Vector3(rng.Range(-0.004f, 0.004f), rng.Range(0.006f, 0.011f), rng.Range(-0.004f, 0.004f)) * scale;
                b.Sway = 0f;
                b.AddCylinder(new Vector3((p.x + pos.x) * 0.5f, pos.y, (p.z + pos.z) * 0.5f), 0.0004f * scale, 0.0003f * scale, p.y - pos.y, 3, leaf, leaf, false);
                b.Sway = 0.8f;
                var c = Pal.Jitter(flower, ref rng, 0.2f);
                b.AddBlob(p, new Vector3(0.0019f, 0.0011f, 0.0019f) * scale, 0, 0.1f, i, (_, __) => c);
                b.AddBlob(p + Vector3.up * 0.0008f * scale, Vector3.one * 0.0007f * scale, 0, 0f, i + 9, (_, __) => Pal.FlowerYellow);
            }
            b.Sway = 0f;
        }

        /// <summary>Lavender: grey-green tuft with tall purple spikes.</summary>
        public static void Lavender(MeshBuilder b, Vector3 pos, float scale, ref Rng rng)
        {
            var leaf = Pal.Hex("#6F8C5E"); var purple = Pal.Hex("#8E6CD0"); var light = Pal.Hex("#B79BEA");
            b.AddBlob(pos + Vector3.up * 0.003f * scale, new Vector3(0.006f, 0.0035f, 0.006f) * scale, 1, 0.3f, rng.Range(0, 999),
                (d, _) => Color.Lerp(Pal.Shade(leaf, 0.75f), leaf, d.y * 0.5f + 0.5f));
            int spikes = rng.Range(5, 9);
            for (int i = 0; i < spikes; i++)
            {
                float a = i / (float)spikes * Mathf.PI * 2f + rng.Value;
                var lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rng.Range(8f, 22f);
                float h = rng.Range(0.012f, 0.019f) * scale;
                b.Push(pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.002f * scale, Quaternion.Euler(lean.z, 0f, -lean.x));
                b.Sway = 0f;
                b.AddCylinder(Vector3.zero, 0.00035f * scale, 0.0003f * scale, h * 0.6f, 3, leaf, leaf, false);
                b.Sway = 0.9f;
                b.AddCylinder(new Vector3(0f, h * 0.55f, 0f), 0.0013f * scale, 0.0003f * scale, h * 0.45f, 5, purple, light, false, false, false);
                b.Pop();
            }
            b.Sway = 0f;
        }

        /// <summary>Tulips: two leaves and cup-shaped heads.</summary>
        public static void Tulips(MeshBuilder b, Vector3 pos, Color colour, float scale, ref Rng rng)
        {
            var leaf = Pal.Hex("#4E8A3A");
            int count = rng.Range(2, 4);
            for (int i = 0; i < count; i++)
            {
                var p = pos + new Vector3(rng.Range(-0.004f, 0.004f), 0f, rng.Range(-0.004f, 0.004f)) * scale;
                float h = rng.Range(0.009f, 0.014f) * scale;
                var side = new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)).normalized * 0.002f * scale;
                Blade(b, p - side, p + side * 0.2f, p + side * 1.5f + Vector3.up * h * 0.6f, Pal.Shade(leaf, 0.7f), leaf, 0.5f);
                b.Sway = 0f;
                b.AddCylinder(p, 0.0004f * scale, 0.00035f * scale, h, 3, leaf, leaf, false);
                b.Sway = 0.8f;
                var c = Pal.Jitter(colour, ref rng, 0.15f);
                b.AddCylinder(p + Vector3.up * h, 0.0016f * scale, 0.0022f * scale, 0.0032f * scale, 6, Pal.Shade(c, 0.8f), c, false, true, false, rng.Value);
                b.AddDisc(p + Vector3.up * (h + 0.0028f * scale), 0.0018f * scale, 6, Pal.Shade(c, 0.55f), true);
            }
            b.Sway = 0f;
        }

        /// <summary>A toadstool: white stem, red cap with white dots. A dash of fairy tale.</summary>
        public static void Mushroom(MeshBuilder b, Vector3 pos, float scale, ref Rng rng, bool red = true)
        {
            var stem = Pal.Hex("#F3EBDD"); var cap = red ? Pal.Hex("#D8352A") : Pal.Hex("#C98A4B");
            float h = rng.Range(0.005f, 0.008f) * scale, r = rng.Range(0.003f, 0.0045f) * scale;
            b.Sway = 0f;
            b.AddCylinder(pos, r * 0.38f, r * 0.3f, h, 7, Pal.Shade(stem, 0.85f), stem, false);
            int start = b.VertexCount;
            b.AddBlob(pos + Vector3.up * h, new Vector3(r, r * 0.62f, r), 1, 0.05f, rng.Range(0, 99), (d, _) => d.y < 0.05f ? Pal.Hex("#EADFC8") : Color.Lerp(Pal.Shade(cap, 0.8f), cap, d.y));
            for (int i = start; i < b.VertexCount; i++)
                if (b.Vertices[i].y < pos.y + h) { var v = b.Vertices[i]; v.y = pos.y + h - 0.0003f * scale; b.Vertices[i] = v; }
            if (!red) return;
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.3f + rng.Value, t = rng.Range(0.3f, 0.75f);
                var d = new Vector3(Mathf.Cos(a) * t, Mathf.Sqrt(1f - t * t), Mathf.Sin(a) * t);
                b.AddBlob(pos + Vector3.up * h + Vector3.Scale(d, new Vector3(r, r * 0.62f, r)) * 1.02f, Vector3.one * r * 0.18f, 0, 0f, i, (_, __) => Color.white);
            }
        }

        /// <summary>A fern: arched two-sided fronds with lighter tips.</summary>
        public static void Fern(MeshBuilder b, Vector3 pos, float scale, ref Rng rng)
        {
            var dark = Pal.Hex("#2E6A2E"); var light = Pal.Hex("#7DB548");
            int fronds = rng.Range(5, 7);
            for (int i = 0; i < fronds; i++)
            {
                float a = i / (float)fronds * Mathf.PI * 2f + rng.Range(-0.2f, 0.2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, dir);
                float len = rng.Range(0.014f, 0.02f) * scale;
                var prev = pos;
                const int segs = 3;
                for (int s = 1; s <= segs; s++)
                {
                    float t = s / (float)segs;
                    var p = pos + dir * len * t + Vector3.up * len * (0.75f * t - 0.6f * t * t);
                    float w = 0.0028f * scale * (1f - t * 0.75f);
                    var c0 = Color.Lerp(dark, light, (s - 1f) / segs); var c1 = Color.Lerp(dark, light, t);
                    float sway = b.Sway;
                    foreach (bool front in new[] { true, false })
                    {
                        b.Sway = (s - 1f) / segs; int v = b.AddVertex(prev - side * w, Vector3.up, c0); b.AddVertex(prev + side * w, Vector3.up, c0);
                        b.Sway = t; b.AddVertex(p + side * w * 0.6f, Vector3.up, c1); b.AddVertex(p - side * w * 0.6f, Vector3.up, c1);
                        if (front) { b.AddTriangle(v, v + 3, v + 2); b.AddTriangle(v, v + 2, v + 1); }
                        else { b.AddTriangle(v, v + 1, v + 2); b.AddTriangle(v, v + 2, v + 3); }
                    }
                    b.Sway = sway;
                    prev = p;
                }
            }
        }

        /// <summary>Reeds and cattails for a pond's edge.</summary>
        public static void Reeds(MeshBuilder b, Vector3 pos, ref Rng rng)
        {
            var root = Pal.Hex("#3C6E2C"); var tip = Pal.Hex("#A9C25A");
            int stems = rng.Range(5, 8);
            for (int i = 0; i < stems; i++)
            {
                float a = rng.Range(0f, 6.28f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var basePos = pos + dir * rng.Range(0f, 0.004f);
                float h = rng.Range(0.016f, 0.026f);
                var side = Vector3.Cross(Vector3.up, dir) * 0.0009f;
                Blade(b, basePos - side, basePos + side, basePos + dir * h * 0.15f + Vector3.up * h, root, Pal.Jitter(tip, ref rng, 0.2f), 0.7f);
                if (i % 3 == 0)
                {
                    b.Sway = 0.6f;
                    b.AddCylinder(basePos + dir * h * 0.1f + Vector3.up * h * 0.62f, 0.0012f, 0.0011f, h * 0.22f, 6, Pal.Hex("#5A3A24"), Pal.Hex("#6E4A2E"), true, true);
                    b.Sway = 0f;
                }
            }
        }

        /// <summary>A lily pad (flat leaf with a notch), sometimes with a pink water lily.</summary>
        public static void LilyPad(MeshBuilder b, Vector3 pos, ref Rng rng, bool flower)
        {
            var leaf = Pal.Jitter(Pal.Hex("#4F8F3A"), ref rng, 0.2f);
            float r = rng.Range(0.004f, 0.0065f), notch = rng.Range(0f, 6.28f);
            int c = b.AddVertex(pos + Vector3.up * 0.0004f, Vector3.up, Pal.Shade(leaf, 1.1f));
            const int seg = 10;
            for (int i = 0; i <= seg; i++)
            {
                float a = notch + 0.35f + i / (float)seg * (Mathf.PI * 2f - 0.7f);
                b.AddVertex(pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, Vector3.up, leaf);
            }
            for (int i = 0; i < seg; i++) b.AddTriangle(c, c + 2 + i, c + 1 + i);
            if (!flower) return;
            var pink = Pal.Jitter(Pal.Hex("#F5A3C7"), ref rng, 0.15f);
            for (int k = 0; k < 6; k++)
            {
                float a = k / 6f * Mathf.PI * 2f;
                b.AddBlob(pos + new Vector3(Mathf.Cos(a) * 0.0018f, 0.0016f, Mathf.Sin(a) * 0.0018f), new Vector3(0.0014f, 0.0009f, 0.0014f), 0, 0f, k, (_, __) => pink);
            }
            b.AddBlob(pos + Vector3.up * 0.0022f, Vector3.one * 0.0009f, 0, 0f, 9, (_, __) => Pal.FlowerYellow);
        }

        /// <summary>A few smooth pebbles.</summary>
        public static void Pebbles(MeshBuilder b, Vector3 pos, ref Rng rng)
        {
            for (int i = 0; i < 3; i++)
            {
                var c = Pal.Jitter(Pal.Stone, ref rng, 0.3f);
                float r = rng.Range(0.002f, 0.0035f);
                b.AddBlob(pos + new Vector3(rng.Range(-0.004f, 0.004f), r * 0.3f, rng.Range(-0.004f, 0.004f)), new Vector3(r, r * 0.6f, r * 0.8f), 1, 0.2f, i,
                    (d, _) => Pal.Shade(c, 0.8f + 0.25f * d.y));
            }
        }

        /// <summary>Undergrowth around a trunk: grass, a fern or flowers, now and then a toadstool.</summary>
        public static void Undergrowth(MeshBuilder b, float radius, ref Rng rng, bool mushrooms)
        {
            float sway = b.Sway;
            b.Sway = 0f;
            int tufts = 1;
            for (int i = 0; i < tufts; i++)
            {
                float a = rng.Range(0f, 6.28f);
                GrassTuft(b, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * rng.Range(0.4f, 1f), rng.Range(0.8f, 1.2f), ref rng, rng.Range(0, 2));
            }
            float pick = rng.Value, ang = rng.Range(0f, 6.28f);
            var spot = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius * 0.7f;
            if (pick < 0.25f) Fern(b, spot, 0.8f, ref rng);
            else if (pick < 0.5f) Flowers(b, spot, rng.Chance(0.5f) ? Pal.FlowerWhite : Pal.FlowerYellow, 0.9f, ref rng, 3);
            if (mushrooms)
                for (int i = 0; i < 1; i++)
                {
                    float a = rng.Range(0f, 6.28f);
                    Mushroom(b, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * rng.Range(0.5f, 0.9f), rng.Range(0.7f, 1.1f), ref rng, rng.Chance(0.75f));
                }
            b.Sway = sway;
        }
    }
}
