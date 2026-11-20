using UnityEngine;

namespace Wonderland
{
    public enum TreeKind { Oak = 0, Conifer = 1, Cypress = 2, Blossom = 3, Bush = 4, Birch = 5, Willow = 6, Palm = 7, Maple = 8, Pine = 9 }

    /// <summary>
    /// Seeded procedural storybook trees. Canopies are built from clustered leaf puffs arranged in tiers, so the outline
    /// is scalloped and leafy; trunks are gnarled with root flares and branches reaching into the crown; each tree
    /// stands in its own little undergrowth (grass, ferns, flowers, now and then a toadstool). Species x variants:
    /// oak (storybook oak, linden, golden oak, apple tree), spruce (three tiered firs), cypress (flame cypress, poplar),
    /// blossom (cherry, white cherry, weeping cherry), bushes (leafy, pink, yellow, fern), birch (silver, twin, golden),
    /// willow (weeping, small weeping), palm (coconut, date, fan), maple (red, orange, yellow autumn) and pine (Scots
    /// pine, stone pine, young pine). Meshes are built once and
    /// shared; placement varies scale, rotation and tint. Pivot at the trunk base; 4-14 cm tall at scale 1.
    /// </summary>
    public static class TreeFactory
    {
        public const int KindCount = 10;
        public const int MaxVariants = 4;
        private static readonly Mesh[] Cache = new Mesh[KindCount * MaxVariants * MeshBuilder.Levels];

        public static int Variants(TreeKind kind) => kind switch { TreeKind.Oak or TreeKind.Bush => 4, TreeKind.Willow => 2, _ => 3 };

        /// <summary>Variant choice with art-directed odds: golden oaks and weeping cherries are accents, not the norm.</summary>
        public static int PickVariant(TreeKind kind, ref Rng rng)
        {
            float r = rng.Value;
            return kind switch
            {
                TreeKind.Oak => r < 0.42f ? 0 : r < 0.72f ? 1 : r < 0.84f ? 2 : 3,
                TreeKind.Blossom => r < 0.45f ? 0 : r < 0.8f ? 1 : 2,
                TreeKind.Bush => r < 0.4f ? 0 : r < 0.6f ? 1 : r < 0.8f ? 2 : 3,
                _ => rng.Range(0, Variants(kind)),
            };
        }

        public static Mesh Get(TreeKind kind, int variant) => Get(kind, variant, 0);

        /// <summary>A tree at a level of detail (0 full; 1 and 2 simpler for further away - see <see cref="MeshBuilder.Detail"/>).</summary>
        public static Mesh Get(TreeKind kind, int variant, int detail)
        {
            variant = Mathf.Clamp(variant, 0, Variants(kind) - 1);
            int index = ((int)kind * MaxVariants + variant) * MeshBuilder.Levels + detail + 1;
            if (Cache[index] == null)
            {
                int was = MeshBuilder.DefaultDetail;
                MeshBuilder.DefaultDetail = detail;
                try { Cache[index] = Build(kind, variant); }
                finally { MeshBuilder.DefaultDetail = was; }
            }
            return Cache[index];
        }

        /// <summary>Footprint radius used for spacing (metres at scale 1).</summary>
        public static float Radius(TreeKind kind) => kind switch
        {
            TreeKind.Oak => 0.034f, TreeKind.Blossom => 0.036f, TreeKind.Conifer => 0.026f,
            TreeKind.Cypress => 0.018f, TreeKind.Birch => 0.022f, TreeKind.Willow => 0.042f, TreeKind.Palm => 0.03f,
            TreeKind.Maple => 0.033f, TreeKind.Pine => 0.028f, _ => 0.02f,
        };

        public static Mesh Build(TreeKind kind, int variant)
        {
            var b = new MeshBuilder();
            var rng = new Rng(1000 + (int)kind * 97 + variant * 13);
            switch (kind)
            {
                case TreeKind.Oak: BuildBroadleaf(b, ref rng, variant); break;
                case TreeKind.Blossom: BuildBlossom(b, ref rng, variant); break;
                case TreeKind.Conifer: BuildConifer(b, ref rng, variant); break;
                case TreeKind.Cypress: BuildCypress(b, ref rng, variant); break;
                case TreeKind.Birch: BuildBirch(b, ref rng, variant); break;
                case TreeKind.Willow: BuildWillow(b, ref rng, variant); break;
                case TreeKind.Palm: BuildPalm(b, ref rng, variant); break;
                case TreeKind.Maple: BuildMaple(b, ref rng, variant); break;
                case TreeKind.Pine: BuildPine(b, ref rng, variant); break;
                default: BuildBush(b, ref rng, variant); break;
            }
            return b.ToMesh($"Tree_{kind}_{variant}");
        }

        // ------------------------------------------------------------------ shared parts

        /// <summary>Leaf colouring: dark underside to sunny top, warm sun side, occlusion towards the crown's heart, speckle.</summary>
        private static Flora.Paint Foliage(Color deep, Color mid, Color light, Color sun, Vector3 centre, float radius, int seed, float speckle = 0.09f)
        {
            return (dir, pos) =>
            {
                float t = dir.y * 0.5f + 0.5f;
                var c = Color.Lerp(deep, mid, Mathf.SmoothStep(0f, 1f, t * 1.3f));
                c = Color.Lerp(c, light, Mathf.SmoothStep(0.62f, 1f, t));
                float sunSide = Mathf.Clamp01(Vector3.Dot(dir, new Vector3(0.35f, 0.8f, -0.45f).normalized));
                c = Color.Lerp(c, sun, sunSide * sunSide * 0.5f);
                float occlusion = Mathf.Clamp01((pos - centre).magnitude / Mathf.Max(radius, 1e-4f));
                c = Pal.Shade(c, Mathf.Lerp(0.6f, 1.06f, occlusion * occlusion));
                float n = Noise.Value3(pos * 1100f + new Vector3(seed, seed * 0.5f, 0f)) - 0.5f;
                float fine = Noise.Value3(pos * 2600f + new Vector3(0f, seed, seed * 0.3f)) - 0.5f;
                return Pal.Shade(c, 1f + n * speckle * 2.4f + fine * speckle * 1.4f);
            };
        }

        /// <summary>A gnarled trunk in three kinked segments with root flares; returns the top of the trunk.</summary>
        private static Vector3 Trunk(MeshBuilder b, ref Rng rng, float height, float radius, Color bark, float kink)
        {
            b.Sway = 0f;
            var dark = Pal.Shade(bark, 0.7f);
            // Roots spreading over the ground.
            int roots = rng.Range(3, 6);
            for (int i = 0; i < roots; i++)
            {
                float a = i * 360f / roots + rng.Range(-20f, 20f);
                b.Push(new Vector3(0f, radius * 0.4f, 0f), Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(0f, 0f, -rng.Range(62f, 75f)));
                b.AddCylinder(Vector3.zero, radius * 0.75f, radius * 0.2f, radius * rng.Range(2.4f, 3.4f), 5, dark, bark, true);
                b.Pop();
            }
            var p = Vector3.zero;
            float r = radius;
            for (int s = 0; s < 3; s++)
            {
                float h = height / 3f;
                var lean = new Vector3(rng.Range(-kink, kink), 1f, rng.Range(-kink, kink)).normalized;
                b.Push(p, Quaternion.FromToRotation(Vector3.up, lean));
                float r1 = r * 0.82f;
                b.AddCylinder(Vector3.zero, r, r1, h * 1.04f, 7, s == 0 ? dark : bark, Pal.Shade(bark, 1.08f), s == 2, false, true, rng.Value);
                b.Pop();
                p += lean * h; r = r1;
            }
            // A knot hole on the trunk for character.
            b.AddBlob(new Vector3(radius * 0.9f, height * 0.35f, 0f), new Vector3(radius * 0.25f, radius * 0.35f, radius * 0.25f), 0, 0f, 3, (_, __) => Pal.Hex("#2A1A12"));
            return p;
        }

        private static void Branch(MeshBuilder b, Vector3 from, Vector3 to, float radius, Color bark)
        {
            b.Sway = 0.15f;
            var dir = to - from;
            b.Push(from, Quaternion.FromToRotation(Vector3.up, dir.normalized));
            b.AddCylinder(Vector3.zero, radius, radius * 0.45f, dir.magnitude, 5, bark, Pal.Shade(bark, 1.1f), true);
            b.Pop();
            b.Sway = 0f;
        }

        /// <summary>A tiered crown of leaf clusters (storybook "cloud" tree). Returns the cluster centres.</summary>
        private static void Crown(MeshBuilder b, ref Rng rng, Vector3 trunkTop, Vector3 centre, Vector3 size, int[] tiers, float clusterSize,
            Color bark, Flora.Paint paint, float branchRadius)
        {
            for (int t = 0; t < tiers.Length; t++)
            {
                float ty = tiers.Length == 1 ? 0f : t / (tiers.Length - 1f);
                float y = centre.y + (ty - 0.45f) * size.y * 1.3f;
                float ring = Mathf.Lerp(0.78f, 0.25f, ty) * (tiers[t] == 1 ? 0f : 1f);
                float offset = rng.Value * 6.28f;
                for (int i = 0; i < tiers[t]; i++)
                {
                    float a = offset + i * Mathf.PI * 2f / tiers[t] + rng.Range(-0.25f, 0.25f);
                    var c = new Vector3(centre.x + Mathf.Cos(a) * size.x * ring, y + rng.Range(-0.12f, 0.12f) * size.y, centre.z + Mathf.Sin(a) * size.z * ring);
                    if (t == 0 && branchRadius > 0f) Branch(b, trunkTop - Vector3.up * size.y * 0.25f, Vector3.Lerp(trunkTop, c, 0.85f), branchRadius, bark);
                    float r = clusterSize * 1.15f * rng.Range(0.88f, 1.12f) * Mathf.Lerp(1f, 0.82f, ty);
                    b.Sway = Mathf.Lerp(0.7f, 1f, ty);
                    Flora.LeafCluster(b, c, r, c - centre, rng.Range(4, 7), ref rng, paint);
                }
            }
            b.Sway = 0f;
        }

        // ------------------------------------------------------------------ species

        private static void BuildBroadleaf(MeshBuilder b, ref Rng rng, int variant)
        {
            // 0 storybook oak, 1 linden (tall egg), 2 golden oak (autumn tints), 3 apple tree (low, round, fruit).
            Color deep = Pal.LeafDeep, mid = Pal.LeafMid, light = Pal.LeafLight, sun = Pal.LeafSun;
            if (variant == 1) { deep = Pal.Hex("#2C6630"); mid = Pal.Hex("#4C9240"); light = Pal.Hex("#86BB55"); sun = Pal.Hex("#B5D266"); }
            if (variant == 2) { deep = Pal.Hex("#5A6E26"); mid = Pal.Hex("#93A33A"); light = Pal.Hex("#D2B445"); sun = Pal.Hex("#F0C35A"); }
            if (variant == 3) { deep = Pal.Hex("#336E2C"); mid = Pal.Hex("#58A03E"); light = Pal.Hex("#95C955"); sun = Pal.Hex("#C4DC6A"); }
            float trunkH = variant switch { 1 => 0.042f, 3 => 0.026f, _ => 0.036f };
            var top = Trunk(b, ref rng, trunkH, variant == 3 ? 0.0046f : 0.0052f, Pal.Bark, variant == 3 ? 0.35f : 0.22f);
            var size = variant switch
            {
                1 => new Vector3(0.026f, 0.05f, 0.026f),
                3 => new Vector3(0.034f, 0.026f, 0.034f),
                _ => new Vector3(0.036f, 0.034f, 0.036f),
            };
            var centre = top + Vector3.up * size.y * 0.65f;
            var tiers = variant switch { 1 => new[] { 3, 3, 2, 1 }, 3 => new[] { 4, 3, 1 }, _ => new[] { 4, 3, 1 } };
            float cluster = variant switch { 1 => 0.016f, 3 => 0.018f, _ => 0.02f };
            var paint = Foliage(deep, mid, light, sun, centre, size.magnitude * 1.1f, rng.Range(0, 999));
            if (variant == 2)
            {
                // A few warmer clusters among the green.
                var warm = Foliage(Pal.Hex("#8A5A22"), Pal.Hex("#D08A34"), Pal.Hex("#F2B04C"), Pal.Hex("#FFD27A"), centre, size.magnitude * 1.1f, 7);
                var basePaint = paint;
                paint = (d, p) => Noise.Value3(p * 90f + new Vector3(3f, 5f, 1f)) > 0.62f ? warm(d, p) : basePaint(d, p);
            }
            Crown(b, ref rng, top, centre, size, tiers, cluster, Pal.Bark, paint, 0.0022f);
            if (variant == 3)
            {
                // Apples in the crown and two in the grass.
                b.Sway = 0.9f;
                for (int i = 0; i < 12; i++)
                {
                    var d = new Vector3(rng.Range(-1f, 1f), rng.Range(-0.5f, 0.8f), rng.Range(-1f, 1f)).normalized;
                    var p = centre + Vector3.Scale(d, size) * 1.05f;
                    var red = Color.Lerp(Pal.Hex("#C8281E"), Pal.Hex("#E8572B"), rng.Value);
                    b.AddBlob(p, Vector3.one * 0.0024f, 1, 0f, i, (dd, _) => Color.Lerp(Pal.Shade(red, 0.75f), red, dd.y * 0.5f + 0.5f));
                }
                b.Sway = 0f;
                for (int i = 0; i < 2; i++)
                    b.AddBlob(new Vector3(rng.Range(-0.03f, 0.03f), 0.0018f, rng.Range(-0.03f, 0.03f)), Vector3.one * 0.0022f, 1, 0f, i + 40, (_, __) => Pal.Hex("#C8281E"));
            }
            Flora.Undergrowth(b, 0.022f, ref rng, variant == 0 || variant == 2);
        }

        private static void BuildBlossom(MeshBuilder b, ref Rng rng, int variant)
        {
            // 0 cherry (spreading umbrella), 1 white cherry, 2 weeping cherry (drooping curtains of blossom).
            Color deep = Pal.Hex("#D45E93"), mid = Pal.Hex("#F78DB9"), light = Pal.Hex("#FFC2D8"), sun = Pal.Hex("#FFE3EE");
            if (variant == 1) { deep = Pal.Hex("#E9A3BF"); mid = Pal.Hex("#FFD6E5"); light = Pal.Hex("#FFF1F6"); sun = Color.white; }
            var top = Trunk(b, ref rng, variant == 2 ? 0.045f : 0.034f, 0.0045f, Pal.BarkDark, 0.32f);
            var size = variant == 2 ? new Vector3(0.03f, 0.024f, 0.03f) : new Vector3(0.04f, 0.024f, 0.04f);
            var centre = top + Vector3.up * size.y * 0.5f;
            var paint = Foliage(deep, mid, light, sun, centre, size.magnitude * 2.2f, rng.Range(0, 999), 0.07f);   // blossom: little occlusion, stays luminous
            Crown(b, ref rng, top, centre, size, variant == 2 ? new[] { 4, 3, 1 } : new[] { 5, 3, 1 }, 0.018f, Pal.BarkDark, paint, 0.002f);
            if (variant == 2)
            {
                // Weeping strands of small blossom puffs hanging from the crown's rim.
                for (int s = 0; s < 7; s++)
                {
                    float a = s / 7f * Mathf.PI * 2f + rng.Range(-0.2f, 0.2f);
                    var p = centre + new Vector3(Mathf.Cos(a) * size.x * 1.05f, -size.y * 0.2f, Mathf.Sin(a) * size.z * 1.05f);
                    int beads = rng.Range(2, 4);
                    for (int k = 0; k < beads; k++)
                    {
                        b.Sway = 1f;
                        float r = 0.0062f * (1f - k * 0.12f);
                        b.AddBlob(p, new Vector3(r, r * 1.1f, r), 1, 0.15f, s * 10 + k, (d, q) => paint(d, q));
                        p += new Vector3(Mathf.Cos(a) * 0.002f, -0.008f, Mathf.Sin(a) * 0.002f);
                    }
                }
                b.Sway = 0f;
            }
            // Fallen petals on the grass.
            for (int i = 0; i < 16; i++)
            {
                var p = new Vector3(rng.Range(-0.045f, 0.045f), 0.0012f, rng.Range(-0.045f, 0.045f));
                var c = Color.Lerp(mid, light, rng.Value);
                b.AddDisc(p, rng.Range(0.0011f, 0.0017f), 5, c, true, rng.Value);
            }
            Flora.Undergrowth(b, 0.02f, ref rng, false);
        }

        private static void BuildConifer(MeshBuilder b, ref Rng rng, int variant)
        {
            float height = variant switch { 1 => 0.12f, 2 => 0.15f, _ => 0.13f };
            b.Sway = 0f;
            var bark = Pal.BarkDark;
            b.AddCylinder(Vector3.zero, 0.0065f, 0.004f, 0.006f, 7, Pal.Shade(bark, 0.7f), bark, false);
            b.AddCylinder(new Vector3(0f, 0.006f, 0f), 0.004f, 0.0028f, 0.03f, 7, bark, Pal.Shade(bark, 1.1f), true);
            int tiers = variant == 2 ? 7 : 5;
            float baseY = 0.013f;
            float maxR = variant == 2 ? 0.024f : 0.031f;
            Color dark = variant == 1 ? Pal.Hex("#2A6A40") : Pal.Conifer;
            Color light = variant == 1 ? Pal.Hex("#5DA05E") : Pal.ConiferLight;
            float tilt = rng.Range(-4f, 4f);
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)(tiers - 1);
                float r = Mathf.Lerp(maxR, maxR * 0.3f, Mathf.Pow(t, 0.9f));
                float y = baseY + (height - baseY - 0.028f) * t * 0.93f;
                float h = Mathf.Lerp(0.04f, 0.028f, t);
                var bottom = Pal.Shade(dark, 0.62f + 0.12f * t);
                var top = Color.Lerp(dark, light, 0.5f + 0.5f * t);
                b.Sway = Mathf.Lerp(0.25f, 1f, t);
                b.Push(new Vector3(Mathf.Sin(tilt * Mathf.Deg2Rad) * y * 0.6f, y, 0f), Quaternion.Euler(0f, rng.Range(0f, 360f), tilt * t));
                // Outer star-shaped skirt plus a darker inner cone underneath for depth.
                b.AddCylinder(Vector3.zero, r, 0f, h, 16, bottom, top, false, true, true, rng.Value * 6f, 0.16f);
                b.AddCylinder(new Vector3(0f, -h * 0.12f, 0f), r * 0.7f, 0f, h * 0.7f, 8, Pal.Shade(dark, 0.45f), Pal.Shade(dark, 0.7f), false, true);
                b.Pop();
            }
            // A crooked little leader at the top.
            b.Sway = 1f;
            b.Push(new Vector3(Mathf.Sin(tilt * Mathf.Deg2Rad) * height * 0.6f, height - 0.012f, 0f), Quaternion.Euler(rng.Range(-12f, 12f), 0f, tilt * 2f));
            b.AddCylinder(Vector3.zero, 0.0035f, 0f, 0.016f, 8, light, Pal.Shade(light, 1.15f), false, true);
            b.Pop();
            if (variant != 1)
            {
                // Cones hanging in the branches.
                for (int i = 0; i < 4; i++)
                {
                    float y = baseY + rng.Range(0.1f, 0.6f) * height, a = rng.Range(0f, 6.28f);
                    float rr = Mathf.Lerp(maxR, maxR * 0.3f, (y - baseY) / height) * 0.8f;
                    b.Sway = 0.6f;
                    b.AddBlob(new Vector3(Mathf.Cos(a) * rr, y - 0.004f, Mathf.Sin(a) * rr), new Vector3(0.0018f, 0.003f, 0.0018f), 0, 0.1f, i, (_, __) => Pal.Hex("#7A4A2A"));
                }
            }
            b.Sway = 0f;
            Flora.Undergrowth(b, 0.02f, ref rng, variant == 2);
        }

        private static void BuildCypress(MeshBuilder b, ref Rng rng, int variant)
        {
            // 0/1 flame cypress, 2 poplar (lighter, looser).
            float height = 0.125f + variant * 0.01f;
            b.Sway = 0f;
            b.AddCylinder(Vector3.zero, 0.005f, 0.0035f, 0.006f, 6, Pal.Shade(Pal.BarkDark, 0.7f), Pal.BarkDark, false);
            b.AddCylinder(new Vector3(0f, 0.006f, 0f), 0.0035f, 0.0025f, 0.018f, 6, Pal.BarkDark, Pal.BarkDark, true);
            Color deep = variant == 2 ? Pal.Hex("#3E7A36") : Pal.Shade(Pal.Cypress, 0.8f);
            Color mid = variant == 2 ? Pal.Hex("#5E9E44") : Pal.Cypress;
            Color light = variant == 2 ? Pal.Hex("#98C45A") : Pal.Hex("#4F8A55");
            Color sun = variant == 2 ? Pal.Hex("#C6DC74") : Pal.Hex("#7FAE5E");
            var centre = new Vector3(0f, height * 0.52f, 0f);
            var paint = Foliage(deep, mid, light, sun, centre, height * 0.5f, rng.Range(0, 999));
            // A flame of stacked, slightly spiralling leaf clusters, widest a third of the way up.
            int lumps = 7;
            for (int i = 0; i < lumps; i++)
            {
                float t = i / (lumps - 1f);
                float y = 0.02f + t * (height - 0.03f);
                float width = Mathf.Sin(Mathf.Lerp(0.35f, 3.0f, t)) * (variant == 2 ? 0.021f : 0.019f) + 0.005f;
                float a = i * 2.2f;
                var c = new Vector3(Mathf.Cos(a) * width * 0.25f, y, Mathf.Sin(a) * width * 0.25f);
                b.Sway = Mathf.Lerp(0.3f, 1f, t);
                Flora.LeafCluster(b, c, width, new Vector3(Mathf.Cos(a), 0.3f, Mathf.Sin(a)), variant == 2 ? 5 : 3, ref rng, paint, 1.45f);
            }
            b.Sway = 0f;
            Flora.Undergrowth(b, 0.016f, ref rng, false);
        }

        private static void BuildBush(MeshBuilder b, ref Rng rng, int variant)
        {
            // 0 leafy, 1 pink rhododendron, 2 yellow broom, 3 fern clump with a toadstool.
            if (variant == 3)
            {
                b.Sway = 0f;
                Flora.Fern(b, Vector3.zero, 1.5f, ref rng);
                Flora.Fern(b, new Vector3(0.012f, 0f, 0.006f), 1.05f, ref rng);
                Flora.Mushroom(b, new Vector3(-0.01f, 0f, 0.01f), 1.1f, ref rng);
                return;
            }
            var deep = Pal.Hex("#2E6A2C"); var mid = Pal.Hex("#4F963A"); var light = Pal.Hex("#86BC4C");
            var centre = new Vector3(0f, 0.012f, 0f);
            var paint = Foliage(deep, mid, light, Pal.LeafSun, centre, 0.03f, rng.Range(0, 999));
            int lumps = 4;
            for (int i = 0; i < lumps; i++)
            {
                float a = i / (float)lumps * Mathf.PI * 2f + rng.Value;
                float d = i == 0 ? 0f : rng.Range(0.008f, 0.013f);
                float r = rng.Range(0.009f, 0.012f) * (i == 0 ? 1.25f : 1f);
                var c = new Vector3(Mathf.Cos(a) * d, r * 0.85f, Mathf.Sin(a) * d);
                b.Sway = 0.6f;
                Flora.LeafCluster(b, c, r, c - centre + Vector3.up * 0.01f, 4, ref rng, paint);
            }
            if (variant > 0)
            {
                var flower = variant == 1 ? Pal.FlowerPink : Pal.FlowerYellow;
                b.Sway = 0.7f;
                for (int i = 0; i < 14; i++)
                {
                    var dir = new Vector3(rng.Range(-1f, 1f), rng.Range(0.15f, 1f), rng.Range(-1f, 1f)).normalized;
                    var p = centre + Vector3.Scale(dir, new Vector3(0.021f, 0.016f, 0.021f));
                    var col = Pal.Jitter(flower, ref rng, 0.25f);
                    b.AddBlob(p, new Vector3(0.0028f, 0.002f, 0.0028f), 0, 0.15f, i, (_, __) => col);
                    b.AddBlob(p + Vector3.up * 0.0012f, Vector3.one * 0.0009f, 0, 0f, i + 50, (_, __) => Pal.Shade(col, 1.25f));
                }
            }
            b.Sway = 0f;
            Flora.Undergrowth(b, 0.016f, ref rng, false);
        }

        private static void BuildBirch(MeshBuilder b, ref Rng rng, int variant)
        {
            // 0 silver birch, 1 two stems from one root, 2 golden autumn birch.
            var bark = Pal.Hex("#ECE8DE");
            Color deep = Pal.Hex("#4E8A3A"), mid = Pal.Hex("#7DB04A"), light = Pal.Hex("#B6D46A"), sun = Pal.Hex("#DDE68A");
            if (variant == 2) { deep = Pal.Hex("#9A7A22"); mid = Pal.Hex("#D2A93A"); light = Pal.Hex("#F0CF5A"); sun = Pal.Hex("#FFE58A"); }
            int stems = variant == 1 ? 2 : 1;
            for (int s = 0; s < stems; s++)
            {
                var foot = stems == 1 ? Vector3.zero : new Vector3(s == 0 ? -0.004f : 0.004f, 0f, 0f);
                float lean = stems == 1 ? 0f : (s == 0 ? -10f : 9f);
                float h = (stems == 1 ? 0.075f : 0.066f - s * 0.01f) * rng.Range(0.92f, 1.08f);
                b.Push(foot, Quaternion.Euler(0f, 0f, lean));
                b.Sway = 0f;
                b.AddCylinder(Vector3.zero, 0.0032f, 0.0018f, h, 7, Pal.Shade(bark, 0.82f), bark, true);
                // The black marks of birch bark.
                for (int k = 0; k < 9; k++)
                {
                    float y = rng.Range(0.004f, h * 0.85f), a = rng.Range(0f, 6.28f), r = Mathf.Lerp(0.0032f, 0.0018f, y / h);
                    b.AddBlob(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), new Vector3(0.0016f, 0.0005f, 0.0016f), 0, 0f, k, (_, __) => Pal.Hex("#2A2622"));
                }
                // A small, high, airy crown: the white trunk shows beneath it.
                var top = new Vector3(0f, h, 0f);
                var size = new Vector3(0.014f, 0.022f, 0.014f) * (stems == 1 ? 1f : 0.85f);
                var centre = top - Vector3.up * size.y * 0.1f;
                var paint = Foliage(deep, mid, light, sun, centre, size.magnitude * 1.6f, rng.Range(0, 999), 0.11f);
                Crown(b, ref rng, top - Vector3.up * size.y * 0.6f, centre, size, new[] { 4, 3, 1 }, 0.0085f, bark, paint, 0.0011f);
                b.Pop();
            }
            Flora.Undergrowth(b, 0.016f, ref rng, variant != 2);
        }

        private static void BuildWillow(MeshBuilder b, ref Rng rng, int variant)
        {
            // 0 a big weeping willow, 1 a smaller one: a broad dome with curtains of leaves down to the grass.
            float scale = variant == 0 ? 1f : 0.75f;
            var top = Trunk(b, ref rng, 0.03f * scale, 0.006f * scale, Pal.BarkDark, 0.3f);
            var size = new Vector3(0.04f, 0.026f, 0.04f) * scale;
            var centre = top + Vector3.up * size.y * 0.4f;
            var paint = Foliage(Pal.Hex("#4E7A2E"), Pal.Hex("#7FA640"), Pal.Hex("#B5CC62"), Pal.Hex("#D8E07E"), centre, size.magnitude * 1.4f, rng.Range(0, 999));
            Crown(b, ref rng, top, centre, size, new[] { 5, 3, 1 }, 0.017f * scale, Pal.BarkDark, paint, 0.0024f * scale);
            int strands = variant == 0 ? 16 : 11;
            for (int s = 0; s < strands; s++)
            {
                float a = s / (float)strands * Mathf.PI * 2f + rng.Range(-0.15f, 0.15f);
                var p = centre + new Vector3(Mathf.Cos(a) * size.x * 1.05f, -size.y * 0.1f, Mathf.Sin(a) * size.z * 1.05f);
                // (longer, simpler beads: the same curtain for a third of the vertices)
                int beads = Mathf.RoundToInt(rng.Range(3f, 4f) * scale);
                for (int k = 0; k < beads && p.y > 0.006f; k++)
                {
                    b.Sway = 1f;
                    float r = 0.0048f * scale * (1f - k * 0.08f);
                    b.AddBlob(p, new Vector3(r * 0.85f, r * 2.1f, r * 0.85f), 0, 0.15f, s * 10 + k, (d, q) => paint(d, q));
                    p += new Vector3(Mathf.Cos(a) * 0.002f, -0.0115f * scale, Mathf.Sin(a) * 0.002f);
                }
            }
            b.Sway = 0f;
            Flora.Undergrowth(b, 0.022f, ref rng, false);
        }

        private static void BuildPalm(MeshBuilder b, ref Rng rng, int variant)
        {
            // 0 coconut palm (a leaning, curving trunk), 1 date palm (straight, a full head), 2 fan palm (short and bushy).
            var bark = Pal.Hex("#9A7A55"); var ring = Pal.Hex("#7A5E40");
            float height = variant switch { 1 => 0.085f, 2 => 0.03f, _ => 0.095f };
            float bend = variant == 0 ? 22f : 3f;
            int segments = variant == 2 ? 3 : 8;
            var p = Vector3.zero;
            float r = variant == 2 ? 0.0045f : 0.0036f;
            float heading = rng.Range(0f, 360f);
            b.Sway = 0f;
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)segments;
                var dir = Quaternion.Euler(0f, heading, 0f) * Quaternion.Euler(bend * t, 0f, 0f) * Vector3.up;
                float h = height / segments;
                b.Sway = t * 0.4f;
                b.Push(p, Quaternion.FromToRotation(Vector3.up, dir));
                b.AddCylinder(Vector3.zero, r, r * 0.94f, h * 1.02f, 7, i % 2 == 0 ? ring : bark, bark, false);
                b.Pop();
                p += dir * h; r *= 0.96f;
            }
            // Fronds: long arching leaves, each a row of flattened leaflets drooping as it goes.
            int fronds = variant switch { 1 => 11, 2 => 9, _ => 8 };
            var green = variant == 2 ? Pal.Hex("#3E8A44") : Pal.Hex("#4C9A3C"); var tip = variant == 2 ? Pal.Hex("#6FB25A") : Pal.Hex("#8CC255");
            float length = variant switch { 1 => 0.04f, 2 => 0.03f, _ => 0.046f };
            for (int f = 0; f < fronds; f++)
            {
                float a = f / (float)fronds * Mathf.PI * 2f + rng.Range(-0.2f, 0.2f);
                var out1 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float rise = variant == 1 ? rng.Range(0.4f, 0.9f) : rng.Range(0.1f, 0.6f);
                var q = p;
                int leaves = 7;
                for (int k = 0; k < leaves; k++)
                {
                    float t = k / (float)(leaves - 1);
                    b.Sway = 0.6f + 0.4f * t;
                    var step = (out1 + Vector3.up * (rise - t * 1.6f)).normalized * (length / leaves);
                    q += step;
                    float w = Mathf.Lerp(0.0055f, 0.0018f, t);
                    var c = Color.Lerp(green, tip, t);
                    b.Push(q, Quaternion.LookRotation(step.normalized, Vector3.up));
                    b.AddBlob(Vector3.zero, new Vector3(w, 0.0007f, length / leaves * 0.75f), 0, 0f, f * 10 + k, (d, _) => Pal.Shade(c, 0.85f + 0.2f * Mathf.Max(0f, d.y)));
                    b.Pop();
                }
            }
            if (variant == 0)
            {
                // Coconuts under the head.
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 1.6f;
                    b.Sway = 0.4f;
                    b.AddBlob(p + new Vector3(Mathf.Cos(a) * 0.003f, -0.003f, Mathf.Sin(a) * 0.003f), Vector3.one * 0.0022f, 1, 0f, i, (_, __) => Pal.Hex("#6A4A2A"));
                }
            }
            if (variant == 1)
            {
                // Dates hanging in amber bunches.
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f;
                    b.Sway = 0.4f;
                    b.AddBlob(p + new Vector3(Mathf.Cos(a) * 0.004f, -0.006f, Mathf.Sin(a) * 0.004f), new Vector3(0.002f, 0.0035f, 0.002f), 1, 0.3f, i, (_, __) => Pal.Hex("#D08A2A"));
                }
            }
            b.Sway = 0f;
            Flora.Undergrowth(b, 0.014f, ref rng, false);
        }

        private static void BuildMaple(MeshBuilder b, ref Rng rng, int variant)
        {
            // Autumn maples: 0 red, 1 orange, 2 yellow, with fallen leaves round the foot.
            Color deep, mid, light, sun;
            switch (variant)
            {
                case 0: deep = Pal.Hex("#7A1E1A"); mid = Pal.Hex("#C23A26"); light = Pal.Hex("#E86A3A"); sun = Pal.Hex("#FF9A5A"); break;
                case 1: deep = Pal.Hex("#8A4012"); mid = Pal.Hex("#D9742A"); light = Pal.Hex("#F5A23E"); sun = Pal.Hex("#FFC86A"); break;
                default: deep = Pal.Hex("#8A7012"); mid = Pal.Hex("#D8B42A"); light = Pal.Hex("#F4D650"); sun = Pal.Hex("#FFEC8A"); break;
            }
            var top = Trunk(b, ref rng, 0.034f, 0.0048f, Pal.BarkDark, 0.25f);
            var size = new Vector3(0.033f, 0.034f, 0.033f);
            var centre = top + Vector3.up * size.y * 0.6f;
            var paint = Foliage(deep, mid, light, sun, centre, size.magnitude * 1.2f, rng.Range(0, 999), 0.1f);
            Crown(b, ref rng, top, centre, size, new[] { 4, 3, 1 }, 0.019f, Pal.BarkDark, paint, 0.0021f);
            for (int i = 0; i < 20; i++)
            {
                var q = new Vector3(rng.Range(-0.04f, 0.04f), 0.0011f, rng.Range(-0.04f, 0.04f));
                b.AddDisc(q, rng.Range(0.0012f, 0.0019f), 5, Color.Lerp(mid, light, rng.Value), true, rng.Value);
            }
            Flora.Undergrowth(b, 0.018f, ref rng, true);
        }

        private static void BuildPine(MeshBuilder b, ref Rng rng, int variant)
        {
            // 0 Scots pine (tall, bare orange trunk, tufts at the top), 1 stone pine (a flat umbrella), 2 young pine.
            var bark = Pal.Hex("#B0643A"); var barkLow = Pal.Hex("#6E4A32");
            float height = variant switch { 1 => 0.07f, 2 => 0.03f, _ => 0.09f };
            b.Sway = 0f;
            b.AddCylinder(Vector3.zero, 0.0048f, 0.0034f, height * 0.4f, 7, barkLow, Pal.Shade(bark, 0.85f), false);
            b.AddCylinder(new Vector3(0f, height * 0.4f, 0f), 0.0034f, 0.0022f, height * 0.6f, 7, Pal.Shade(bark, 0.85f), bark, true);
            var top = new Vector3(0f, height, 0f);
            Color deep = Pal.Hex("#1F4A30"), mid = Pal.Hex("#2F6A3E"), light = Pal.Hex("#5E9256"), sun = Pal.Hex("#8DB46A");
            var paint = Foliage(deep, mid, light, sun, top, 0.04f, rng.Range(0, 999), 0.1f);
            if (variant == 2)
            {
                // Young: a loose cone of tufts all the way up.
                for (int i = 0; i < 6; i++)
                {
                    float t = i / 5f, a = i * 2.3f;
                    var c = new Vector3(Mathf.Cos(a) * 0.006f * (1f - t), 0.012f + t * 0.028f, Mathf.Sin(a) * 0.006f * (1f - t));
                    b.Sway = 0.4f + 0.6f * t;
                    Flora.LeafCluster(b, c, Mathf.Lerp(0.011f, 0.005f, t), c - new Vector3(0f, c.y, 0f) + Vector3.up * 0.01f, 4, ref rng, paint, 0.75f);
                }
            }
            else
            {
                // Branches out from the upper trunk to flat tufts; stone pine one wide umbrella.
                int tufts = variant == 1 ? 7 : 5;
                float spread = variant == 1 ? 0.03f : 0.018f;
                for (int i = 0; i < tufts; i++)
                {
                    float a = i / (float)tufts * Mathf.PI * 2f + rng.Range(-0.3f, 0.3f);
                    float d = i == 0 && variant == 0 ? 0f : spread * rng.Range(0.6f, 1f);
                    var c = top + new Vector3(Mathf.Cos(a) * d, rng.Range(-0.012f, 0.004f) * (variant == 1 ? 0.3f : 1f), Mathf.Sin(a) * d);
                    Branch(b, top - Vector3.up * 0.012f, c, 0.0014f, bark);
                    b.Sway = 0.8f;
                    Flora.LeafCluster(b, c, variant == 1 ? 0.014f : 0.012f, Vector3.up + (c - top) * 20f, 5, ref rng, paint, 0.55f);
                }
            }
            b.Sway = 0f;
            Flora.Undergrowth(b, 0.018f, ref rng, variant == 0);
        }
    }
}
