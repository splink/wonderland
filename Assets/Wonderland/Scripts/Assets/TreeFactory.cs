using UnityEngine;

namespace Wonderland
{
    public enum TreeKind { Oak = 0, Conifer = 1, Cypress = 2, Blossom = 3, Bush = 4 }

    /// <summary>
    /// Seeded procedural miniature trees: rounded oaks, tiered spruces, slender cypresses, cherry blossoms and flowering
    /// bushes, three variants each. Meshes are built once and shared; placement varies scale, rotation and tint.
    /// Pivot at the trunk base; about 6-13 cm tall at scale 1 (a 8 cm locomotive is the yardstick).
    /// </summary>
    public static class TreeFactory
    {
        public const int KindCount = 5;
        public const int VariantCount = 3;
        private static readonly Mesh[] Cache = new Mesh[KindCount * VariantCount];

        public static Mesh Get(TreeKind kind, int variant)
        {
            int index = (int)kind * VariantCount + variant;
            if (Cache[index] == null) Cache[index] = Build(kind, variant);
            return Cache[index];
        }

        /// <summary>Footprint radius used for spacing (metres at scale 1).</summary>
        public static float Radius(TreeKind kind) => kind switch
        {
            TreeKind.Oak => 0.034f, TreeKind.Blossom => 0.034f, TreeKind.Conifer => 0.026f,
            TreeKind.Cypress => 0.018f, _ => 0.02f,
        };

        public static Mesh Build(TreeKind kind, int variant)
        {
            var b = new MeshBuilder();
            var rng = new Rng(1000 + (int)kind * 97 + variant * 13);
            switch (kind)
            {
                case TreeKind.Oak: BuildRound(b, ref rng, variant, false); break;
                case TreeKind.Blossom: BuildRound(b, ref rng, variant, true); break;
                case TreeKind.Conifer: BuildConifer(b, ref rng, variant); break;
                case TreeKind.Cypress: BuildCypress(b, ref rng, variant); break;
                default: BuildBush(b, ref rng, variant); break;
            }
            return b.ToMesh($"Tree_{kind}_{variant}");
        }

        private static void Trunk(MeshBuilder b, ref Rng rng, float height, float radius, Color bark, float lean)
        {
            b.Push(Vector3.zero, Quaternion.Euler(rng.Range(-lean, lean), 0f, rng.Range(-lean, lean)));
            // Root flare, then the tapering trunk.
            b.AddCylinder(Vector3.zero, radius * 1.7f, radius, height * 0.18f, 7, Pal.Shade(bark, 0.75f), bark, false, false, true, rng.Value);
            b.AddCylinder(new Vector3(0f, height * 0.18f, 0f), radius, radius * 0.62f, height * 0.82f, 7, bark, Pal.Shade(bark, 1.15f), true, false, true, rng.Value);
            b.Pop();
        }

        /// <summary>
        /// Leaf colouring: dark underside to sunny top, a warm sun side, ambient occlusion towards the canopy centre (so
        /// clumps read as separate masses) and per-vertex speckle for a hand-painted texture.
        /// </summary>
        private static MeshBuilder.BlobColor Foliage(Color deep, Color mid, Color light, Color sun, int seed, Vector3 centre, float radius,
            float speckle = 0.08f)
        {
            return (dir, pos) =>
            {
                float t = dir.y * 0.5f + 0.5f;
                var c = Color.Lerp(deep, mid, Mathf.SmoothStep(0f, 1f, t * 1.3f));
                c = Color.Lerp(c, light, Mathf.SmoothStep(0.6f, 1f, t));
                float sunSide = Mathf.Clamp01(Vector3.Dot(dir, new Vector3(0.35f, 0.8f, -0.45f).normalized));
                c = Color.Lerp(c, sun, sunSide * sunSide * 0.45f);
                float occlusion = Mathf.Clamp01((pos - centre).magnitude / Mathf.Max(radius, 1e-4f));
                c = Pal.Shade(c, Mathf.Lerp(0.55f, 1.05f, occlusion * occlusion));
                float n = Noise.Value3(pos * 900f + new Vector3(seed, seed * 0.5f, 0f)) - 0.5f;
                return Pal.Shade(c, 1f + n * speckle * 2f);
            };
        }

        private static void BuildRound(MeshBuilder b, ref Rng rng, int variant, bool blossom)
        {
            float height = blossom ? 0.095f : 0.1f + variant * 0.006f;
            float trunkHeight = height * (blossom ? 0.42f : 0.38f);
            var bark = blossom ? Pal.BarkDark : Pal.Bark;
            Trunk(b, ref rng, trunkHeight + 0.012f, blossom ? 0.0042f : 0.0048f, bark, 4f);

            Color deep, mid, light, sun;
            if (blossom)
            {
                deep = Pal.Hex(variant == 2 ? "#C9668E" : "#CF6F93"); mid = Pal.Blossom; light = Pal.BlossomLight;
                sun = variant == 1 ? Pal.Hex("#FFEFF4") : Pal.Hex("#FFE0EA");
            }
            else if (variant == 2)
            {
                // A sunnier, slightly autumnal oak for variety.
                deep = Pal.Hex("#42702A"); mid = Pal.Hex("#749F37"); light = Pal.Hex("#A9BF47"); sun = Pal.Hex("#D9B957");
            }
            else
            {
                deep = Pal.LeafDeep; mid = Pal.LeafMid; light = Pal.LeafLight; sun = Pal.LeafSun;
            }

            float canopyR = (height - trunkHeight) * 0.5f;
            var centre = new Vector3(0f, trunkHeight + canopyR * 0.95f, 0f);
            // Branches reaching into the canopy.
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f + rng.Range(-20f, 20f);
                b.Push(new Vector3(0f, trunkHeight * 0.8f, 0f), Quaternion.Euler(0f, a, rng.Range(28f, 42f)));
                b.AddCylinder(Vector3.zero, 0.0024f, 0.0012f, canopyR * 0.9f, 5, bark, bark, true);
                b.Pop();
            }
            float span = canopyR * 1.35f;
            int seed = rng.Range(0, 1000);
            b.AddBlob(centre, new Vector3(canopyR * 0.9f, canopyR * 0.78f, canopyR * 0.9f), 1, 0.15f, seed,
                Foliage(deep, mid, light, sun, seed, centre, span));
            // Leafy clumps spread over the upper canopy (golden-angle spiral).
            int clumps = 8 + variant * 2 + (blossom ? 2 : 0);
            for (int i = 0; i < clumps; i++)
            {
                float y = 1f - (i + 0.5f) / clumps * 1.55f;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float phi = i * 2.39996f + rng.Value;
                var dir = new Vector3(Mathf.Cos(phi) * r, y * 0.8f, Mathf.Sin(phi) * r);
                float size = canopyR * rng.Range(0.42f, 0.56f);
                var p = centre + Vector3.Scale(dir, new Vector3(canopyR * 0.82f, canopyR * 0.7f, canopyR * 0.82f));
                int s = rng.Range(0, 1000);
                b.AddBlob(p, new Vector3(size, size * 0.86f, size), 1, 0.2f, s, Foliage(deep, mid, light, sun, s, centre, span), 2.8f);
            }
            if (blossom)
            {
                for (int i = 0; i < 5; i++)
                {
                    var dir = new Vector3(rng.Range(-1f, 1f), rng.Range(0.2f, 1f), rng.Range(-1f, 1f)).normalized;
                    float r = canopyR * 0.2f;
                    b.AddBlob(centre + dir * canopyR * 1.05f, Vector3.one * r, 1, 0.25f, i + 7,
                        Foliage(Pal.Blossom, Pal.BlossomLight, Pal.Hex("#FFEAF0"), Pal.Hex("#FFF6F8"), i + 7, centre, span));
                }
            }
        }

        private static void BuildConifer(MeshBuilder b, ref Rng rng, int variant)
        {
            float height = 0.12f + variant * 0.012f;
            Trunk(b, ref rng, 0.03f, 0.0045f, Pal.BarkDark, 2f);
            int tiers = 4 + (variant == 2 ? 1 : 0);
            float baseY = 0.014f;
            float maxR = 0.03f - variant * 0.002f;
            Color dark = variant == 1 ? Pal.Hex("#2A6A40") : Pal.Conifer;
            Color light = variant == 1 ? Pal.Hex("#5DA05E") : Pal.ConiferLight;
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)(tiers - 1);
                float r = Mathf.Lerp(maxR, maxR * 0.38f, t);
                float y = baseY + (height - baseY - 0.03f) * t * 0.92f;
                float h = Mathf.Lerp(0.042f, 0.032f, t);
                var bottom = Pal.Shade(dark, 0.72f + 0.08f * t);
                var top = Color.Lerp(dark, light, 0.55f + 0.45f * t);
                b.AddCylinder(new Vector3(rng.Range(-0.001f, 0.001f), y, rng.Range(-0.001f, 0.001f)), r, 0f, h, 14, bottom, top, false, true, true, rng.Value * 6f, 0.13f);
            }
        }

        private static void BuildCypress(MeshBuilder b, ref Rng rng, int variant)
        {
            float height = 0.12f + variant * 0.015f;
            Trunk(b, ref rng, 0.02f, 0.0035f, Pal.BarkDark, 1f);
            int seed = rng.Range(0, 999);
            float r = 0.015f + variant * 0.0015f;
            var deep = Pal.Shade(Pal.Cypress, 0.8f);
            var centre = new Vector3(0f, 0.012f + (height - 0.012f) * 0.48f, 0f);
            b.AddBlob(centre, new Vector3(r, (height - 0.012f) * 0.52f, r), 2, 0.2f, seed,
                Foliage(deep, Pal.Cypress, Pal.Hex("#4F8A55"), Pal.Hex("#7FAE5E"), seed, centre, r * 1.4f, 0.1f), 4.5f);
        }

        private static void BuildBush(MeshBuilder b, ref Rng rng, int variant)
        {
            int lumps = 3 + variant;
            var deep = Pal.Hex("#2E6A2C"); var mid = Pal.Hex("#4F963A"); var light = Pal.Hex("#86BC4C");
            for (int i = 0; i < lumps; i++)
            {
                float a = i / (float)lumps * Mathf.PI * 2f + rng.Value;
                float d = i == 0 ? 0f : rng.Range(0.007f, 0.012f);
                float r = rng.Range(0.010f, 0.015f) * (i == 0 ? 1.25f : 1f);
                var c = new Vector3(Mathf.Cos(a) * d, r * 0.75f, Mathf.Sin(a) * d);
                int s = rng.Range(0, 999);
                b.AddBlob(c, new Vector3(r, r * 0.85f, r), 1, 0.22f, s, Foliage(deep, mid, light, Pal.LeafSun, s, new Vector3(0f, 0.01f, 0f), 0.03f));
            }
            // Flowering variants: dots of colour over the leaves.
            if (variant > 0)
            {
                var flower = variant == 1 ? Pal.FlowerPink : Pal.FlowerYellow;
                for (int i = 0; i < 9; i++)
                {
                    var dir = new Vector3(rng.Range(-1f, 1f), rng.Range(0.2f, 1f), rng.Range(-1f, 1f)).normalized;
                    var p = new Vector3(0f, 0.012f, 0f) + Vector3.Scale(dir, new Vector3(0.019f, 0.014f, 0.019f));
                    var col = Pal.Jitter(flower, ref rng, 0.3f);
                    b.AddBlob(p, Vector3.one * 0.0024f, 0, 0.1f, i, (_, __) => col);
                }
            }
        }
    }
}
