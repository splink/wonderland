using UnityEngine;

namespace Wonderland
{
    public enum AnimalKind { Sheep = 0, Cow = 1, Horse = 2, Pig = 3, Chicken = 4, Duck = 5, Deer = 6 }

    /// <summary>
    /// Storybook farm animals as two meshes each: the body (with legs and tail) and the head, which nods separately while
    /// grazing. A little bigger than true scale so they read at arm's length. Pivot at the ground between the feet; the
    /// head's pivot is its neck joint (<see cref="NeckOffset"/>). Front faces +Z.
    /// </summary>
    public static class AnimalFactory
    {
        public const int KindCount = 7;
        public static readonly string[] Names = { "Sheep", "Cow", "Horse", "Pig", "Hen", "Duck", "Deer" };
        private static readonly Mesh[] Bodies = new Mesh[KindCount], Heads = new Mesh[KindCount];

        public static Mesh Body(AnimalKind kind) => Bodies[(int)kind] ??= kind switch
        {
            AnimalKind.Sheep => SheepBody(), AnimalKind.Cow => CowBody(), AnimalKind.Horse => HorseBody(), AnimalKind.Pig => PigBody(),
            AnimalKind.Chicken => ChickenBody(), AnimalKind.Duck => DuckBody(), _ => DeerBody(),
        };

        public static Mesh Head(AnimalKind kind) => Heads[(int)kind] ??= kind switch
        {
            AnimalKind.Sheep => SheepHead(), AnimalKind.Cow => CowHead(), AnimalKind.Horse => HorseHead(), AnimalKind.Pig => PigHead(),
            AnimalKind.Chicken => ChickenHead(), AnimalKind.Duck => DuckHead(), _ => DeerHead(),
        };

        /// <summary>Neck joint position relative to the body pivot.</summary>
        public static Vector3 NeckOffset(AnimalKind kind) => kind switch
        {
            AnimalKind.Sheep => new Vector3(0f, 0.0125f, 0.0085f),
            AnimalKind.Cow => new Vector3(0f, 0.016f, 0.015f),
            AnimalKind.Horse => new Vector3(0f, 0.022f, 0.014f),
            AnimalKind.Pig => new Vector3(0f, 0.009f, 0.011f),
            AnimalKind.Chicken => new Vector3(0f, 0.0085f, 0.003f),
            AnimalKind.Duck => new Vector3(0f, 0.004f, 0.0045f),
            _ => new Vector3(0f, 0.019f, 0.011f),
        };

        public static float Radius(AnimalKind kind) => kind switch
        {
            AnimalKind.Sheep => 0.012f, AnimalKind.Cow => 0.018f, AnimalKind.Horse => 0.02f, AnimalKind.Pig => 0.012f,
            AnimalKind.Chicken => 0.006f, AnimalKind.Duck => 0.007f, _ => 0.015f,
        };

        private static void Legs(MeshBuilder b, float x, float zFront, float zBack, float height, float r, Color colour, Color hoof)
        {
            foreach (float z in new[] { zFront, zBack })
            foreach (int s in new[] { -1, 1 })
            {
                b.AddCylinder(new Vector3(s * x, 0.0012f, z), r, r * 0.9f, height, 6, colour, colour, false);
                b.AddCylinder(new Vector3(s * x, 0f, z), r * 1.1f, r * 1.05f, 0.0012f, 6, hoof, hoof, true);
            }
        }

        private static Mesh SheepBody()
        {
            var b = new MeshBuilder();
            var rng = new Rng(41);
            var face = Pal.Hex("#2E2A2C");
            Legs(b, 0.0034f, 0.0055f, -0.0055f, 0.0062f, 0.0008f, face, Pal.Hex("#1C1A1B"));
            // A cloud of wool: lumpy puffs, creamy white with soft shading.
            var wool = Pal.Hex("#F6F1E6"); var woolShade = Pal.Hex("#D9D0C0");
            Flora.Paint paint = (d, p) => Color.Lerp(woolShade, wool, Mathf.SmoothStep(0f, 1f, d.y * 0.6f + 0.55f)) * (0.95f + 0.1f * Noise.Value3(p * 2000f));
            Flora.LeafCluster(b, new Vector3(0f, 0.0112f, 0f), 0.0072f, Vector3.up, 6, ref rng, paint, 0.78f);
            Flora.LeafCluster(b, new Vector3(0f, 0.0118f, 0.0045f), 0.0052f, Vector3.forward, 4, ref rng, paint, 0.85f);
            Flora.LeafCluster(b, new Vector3(0f, 0.0112f, -0.0048f), 0.0055f, Vector3.back, 4, ref rng, paint, 0.85f);
            b.AddBlob(new Vector3(0f, 0.0125f, -0.0105f), Vector3.one * 0.0016f, 1, 0.2f, 3, (_, __) => wool);   // tail tuft
            return b.ToMesh("SheepBody");
        }

        private static Mesh SheepHead()
        {
            var b = new MeshBuilder();
            var face = Pal.Hex("#2E2A2C");
            // Head reaches forward and down from the neck joint.
            b.AddBlob(new Vector3(0f, 0.0004f, 0.004f), new Vector3(0.0027f, 0.0032f, 0.0038f), 1, 0.05f, 5, (d, _) => Color.Lerp(Pal.Shade(face, 0.8f), Pal.Shade(face, 1.3f), d.y * 0.5f + 0.5f));
            b.AddBlob(new Vector3(0f, 0.0034f, 0.0028f), new Vector3(0.0026f, 0.0016f, 0.0024f), 1, 0.3f, 6, (_, __) => Pal.Hex("#F6F1E6"));   // woolly cap
            foreach (int s in new[] { -1, 1 })
            {
                b.Push(new Vector3(s * 0.0029f, 0.0016f, 0.0026f), Quaternion.Euler(0f, s * 25f, s * -55f));
                b.AddBlob(new Vector3(0f, 0.0016f, 0f), new Vector3(0.0009f, 0.0019f, 0.0006f), 0, 0f, s + 7, (_, __) => Pal.Hex("#3A3436"));
                b.Pop();
                b.AddBlob(new Vector3(s * 0.0013f, 0.0014f, 0.0068f), Vector3.one * 0.00055f, 0, 0f, s + 9, (_, __) => Color.white);
                b.AddBlob(new Vector3(s * 0.00135f, 0.00145f, 0.0072f), Vector3.one * 0.0003f, 0, 0f, s + 11, (_, __) => Color.black);
            }
            return b.ToMesh("SheepHead");
        }

        private static Mesh CowBody()
        {
            var b = new MeshBuilder();
            var white = Pal.Hex("#F4F0EA"); var black = Pal.Hex("#2A2626"); var pink = Pal.Hex("#E9A3A8");
            Legs(b, 0.005f, 0.0085f, -0.0085f, 0.0075f, 0.0016f, white, Pal.Hex("#3A3030"));
            // Barrel body with black patches painted by noise.
            b.AddBlob(new Vector3(0f, 0.0135f, 0f), new Vector3(0.0068f, 0.0062f, 0.0125f), 2, 0.06f, 12, (d, p) =>
                Noise.Value3(p * 520f + new Vector3(3f, 1f, 7f)) > 0.58f ? black : Color.Lerp(Pal.Shade(white, 0.85f), white, d.y * 0.5f + 0.5f));
            b.AddBlob(new Vector3(0f, 0.0078f, -0.002f), new Vector3(0.0026f, 0.0016f, 0.0026f), 1, 0.1f, 13, (_, __) => pink);   // udder
            // Tail with a tuft.
            b.Push(new Vector3(0f, 0.0175f, -0.0122f), Quaternion.Euler(-160f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0005f, 0.0004f, 0.011f, 4, white, white, false);
            b.AddBlob(new Vector3(0f, 0.0115f, 0f), new Vector3(0.0011f, 0.0018f, 0.0011f), 0, 0.2f, 14, (_, __) => black);
            b.Pop();
            return b.ToMesh("CowBody");
        }

        // ---------------------------------------------------------------- horse

        private static readonly Color HorseBrown = Pal.Hex("#8A5236"), HorseDark = Pal.Hex("#3A2420");

        private static Mesh HorseBody()
        {
            var b = new MeshBuilder();
            Legs(b, 0.0045f, 0.0095f, -0.0095f, 0.0125f, 0.0013f, HorseBrown, HorseDark);
            foreach (float z in new[] { 0.0095f, -0.0095f }) foreach (int s in new[] { -1, 1 })
                b.AddCylinder(new Vector3(s * 0.0045f, 0.0012f, z), 0.0016f, 0.0014f, 0.002f, 6, Pal.Hex("#F2EEE4"), Pal.Hex("#F2EEE4"), false);
            b.AddBlob(new Vector3(0f, 0.0175f, 0f), new Vector3(0.0058f, 0.0058f, 0.0125f), 2, 0.04f, 31, (d, _) => Color.Lerp(Pal.Shade(HorseBrown, 0.8f), Pal.Shade(HorseBrown, 1.1f), d.y * 0.5f + 0.5f));
            b.AddBlob(new Vector3(0f, 0.0205f, 0.01f), new Vector3(0.004f, 0.0055f, 0.005f), 1, 0f, 32, (_, __) => HorseBrown);
            // Mane along the neck and a long swishing tail.
            b.AddBlob(new Vector3(0f, 0.025f, 0.0115f), new Vector3(0.0014f, 0.0045f, 0.004f), 1, 0.3f, 33, (_, __) => HorseDark);
            b.Push(new Vector3(0f, 0.02f, -0.012f), Quaternion.Euler(-150f, 0f, 0f));
            b.AddBlob(new Vector3(0f, 0.006f, 0f), new Vector3(0.0018f, 0.0075f, 0.0018f), 1, 0.3f, 34, (_, __) => HorseDark);
            b.Pop();
            return b.ToMesh("HorseBody");
        }

        private static Mesh HorseHead()
        {
            var b = new MeshBuilder();
            b.Push(Vector3.zero, Quaternion.Euler(35f, 0f, 0f));
            b.AddBlob(new Vector3(0f, 0.004f, 0.006f), new Vector3(0.0028f, 0.003f, 0.0065f), 1, 0.03f, 35, (d, _) => d.z > 0.6f ? Pal.Shade(HorseBrown, 0.75f) : HorseBrown);
            b.AddBlob(new Vector3(0f, 0.0058f, 0.007f), new Vector3(0.0012f, 0.0012f, 0.004f), 0, 0f, 36, (_, __) => Pal.Hex("#F2EEE4"));   // blaze
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBlob(new Vector3(s * 0.0015f, 0.008f, 0.0015f), new Vector3(0.0007f, 0.002f, 0.0008f), 0, 0f, s + 37, (_, __) => HorseBrown);
                b.AddBlob(new Vector3(s * 0.0026f, 0.0055f, 0.0045f), Vector3.one * 0.0005f, 0, 0f, s + 40, (_, __) => Color.black);
            }
            b.AddBlob(new Vector3(0f, 0.008f, 0.004f), new Vector3(0.0012f, 0.0016f, 0.003f), 0, 0.3f, 39, (_, __) => HorseDark);   // forelock
            b.Pop();
            return b.ToMesh("HorseHead");
        }

        // ---------------------------------------------------------------- pig

        private static readonly Color PigPink = Pal.Hex("#F2A7A8");

        private static Mesh PigBody()
        {
            var b = new MeshBuilder();
            Legs(b, 0.0035f, 0.0055f, -0.0055f, 0.004f, 0.0012f, PigPink, Pal.Hex("#8A5A5A"));
            b.AddBlob(new Vector3(0f, 0.0095f, 0f), new Vector3(0.0068f, 0.0062f, 0.0095f), 2, 0.03f, 41, (d, p) => Color.Lerp(Pal.Shade(PigPink, 0.85f), PigPink, d.y * 0.5f + 0.5f)
                * (Noise.Value3(p * 700f) > 0.72f ? 0.8f : 1f));
            // A curly tail.
            for (int k = 0; k < 6; k++)
            {
                float a = k * 1.1f;
                b.AddBlob(new Vector3(Mathf.Cos(a) * 0.0012f, 0.0115f + Mathf.Sin(a) * 0.0012f, -0.0098f - k * 0.0003f), Vector3.one * 0.0006f, 0, 0f, k, (_, __) => PigPink);
            }
            return b.ToMesh("PigBody");
        }

        private static Mesh PigHead()
        {
            var b = new MeshBuilder();
            b.AddBlob(new Vector3(0f, 0.0006f, 0.0025f), new Vector3(0.0045f, 0.0042f, 0.0042f), 1, 0.02f, 42, (_, __) => PigPink);
            b.Push(new Vector3(0f, -0.0002f, 0.0064f), Quaternion.Euler(90f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0021f, 0.0021f, 0.0016f, 10, Pal.Shade(PigPink, 0.9f), Pal.Shade(PigPink, 0.9f), true, true);
            b.Pop();
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBlob(new Vector3(s * 0.0008f, -0.0002f, 0.0082f), Vector3.one * 0.0004f, 0, 0f, s + 43, (_, __) => Pal.Hex("#8A4A4E"));
                b.Push(new Vector3(s * 0.0028f, 0.0038f, 0.0018f), Quaternion.Euler(-30f, 0f, s * -30f));
                b.AddBlob(new Vector3(0f, 0.0012f, 0.0006f), new Vector3(0.0018f, 0.0018f, 0.0005f), 0, 0f, s + 45, (_, __) => Pal.Shade(PigPink, 0.92f));
                b.Pop();
                b.AddBlob(new Vector3(s * 0.0017f, 0.0018f, 0.0058f), Vector3.one * 0.0005f, 0, 0f, s + 47, (_, __) => Color.black);
            }
            return b.ToMesh("PigHead");
        }

        // ---------------------------------------------------------------- chicken

        private static Mesh ChickenBody()
        {
            var b = new MeshBuilder();
            var white = Pal.Hex("#F6F1E6"); var yellow = Pal.Hex("#F2B33A");
            foreach (int s in new[] { -1, 1 }) b.AddCylinder(new Vector3(s * 0.0012f, 0f, 0f), 0.00035f, 0.00035f, 0.003f, 4, yellow, yellow, false);
            b.AddBlob(new Vector3(0f, 0.0052f, -0.0005f), new Vector3(0.003f, 0.0028f, 0.0042f), 1, 0.05f, 51, (d, _) => Color.Lerp(Pal.Hex("#D9CFC0"), white, d.y * 0.5f + 0.5f));
            b.AddBlob(new Vector3(0f, 0.0072f, -0.0042f), new Vector3(0.0016f, 0.0028f, 0.0012f), 1, 0.2f, 52, (_, __) => Pal.Hex("#3A3436"));   // tail feathers
            return b.ToMesh("ChickenBody");
        }

        private static Mesh ChickenHead()
        {
            var b = new MeshBuilder();
            b.AddBlob(new Vector3(0f, 0.0018f, 0.0008f), new Vector3(0.0017f, 0.002f, 0.0017f), 1, 0f, 53, (_, __) => Pal.Hex("#F6F1E6"));
            b.AddBlob(new Vector3(0f, 0.0042f, 0.0006f), new Vector3(0.0005f, 0.0011f, 0.0012f), 0, 0.2f, 54, (_, __) => Pal.Hex("#E2483D"));
            b.AddBlob(new Vector3(0f, 0.0008f, 0.0022f), new Vector3(0.0004f, 0.0008f, 0.0004f), 0, 0f, 55, (_, __) => Pal.Hex("#E2483D"));
            b.Push(new Vector3(0f, 0.0018f, 0.0024f), Quaternion.Euler(90f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0005f, 0f, 0.0012f, 4, Pal.Hex("#F2B33A"), Pal.Hex("#F2B33A"), false);
            b.Pop();
            foreach (int s in new[] { -1, 1 }) b.AddBlob(new Vector3(s * 0.0012f, 0.0024f, 0.0014f), Vector3.one * 0.00035f, 0, 0f, s + 56, (_, __) => Color.black);
            return b.ToMesh("ChickenHead");
        }

        // ---------------------------------------------------------------- duck

        private static Mesh DuckBody()
        {
            var b = new MeshBuilder();
            var brown = Pal.Hex("#A8865E");
            b.AddBlob(new Vector3(0f, 0.0018f, 0f), new Vector3(0.0035f, 0.0028f, 0.0058f), 1, 0.03f, 61, (d, _) => d.y < -0.2f ? Pal.Shade(brown, 0.7f) : Color.Lerp(Pal.Shade(brown, 0.85f), brown, d.y * 0.5f + 0.5f));
            foreach (int s in new[] { -1, 1 }) b.AddBlob(new Vector3(s * 0.0032f, 0.0025f, -0.0008f), new Vector3(0.0006f, 0.0013f, 0.0032f), 0, 0f, s + 62, (_, __) => Pal.Hex("#4A6AA8"));   // speculum
            b.AddBlob(new Vector3(0f, 0.0034f, -0.0055f), new Vector3(0.0012f, 0.0012f, 0.0016f), 0, 0f, 64, (_, __) => Pal.Hex("#3A3436"));
            return b.ToMesh("DuckBody");
        }

        private static Mesh DuckHead()
        {
            var b = new MeshBuilder();
            b.AddCylinder(Vector3.zero, 0.0012f, 0.001f, 0.0035f, 6, Pal.Hex("#F7F1E6"), Pal.Hex("#2E6E44"), false);
            b.AddBlob(new Vector3(0f, 0.0044f, 0.0004f), new Vector3(0.0018f, 0.0018f, 0.002f), 1, 0f, 65, (_, __) => Pal.Hex("#2E6E44"));
            b.AddBlob(new Vector3(0f, 0.0038f, 0.0026f), new Vector3(0.0008f, 0.0004f, 0.0014f), 0, 0f, 66, (_, __) => Pal.Hex("#F2C23A"));
            foreach (int s in new[] { -1, 1 }) b.AddBlob(new Vector3(s * 0.0014f, 0.005f, 0.0008f), Vector3.one * 0.0003f, 0, 0f, s + 67, (_, __) => Color.black);
            return b.ToMesh("DuckHead");
        }

        // ---------------------------------------------------------------- deer

        private static readonly Color Fawn = Pal.Hex("#B5784A");

        private static Mesh DeerBody()
        {
            var b = new MeshBuilder();
            Legs(b, 0.0034f, 0.0072f, -0.0072f, 0.0125f, 0.0008f, Pal.Shade(Fawn, 0.85f), Pal.Hex("#2A2224"));
            b.AddBlob(new Vector3(0f, 0.0165f, 0f), new Vector3(0.0045f, 0.0048f, 0.0095f), 2, 0.03f, 71, (d, p) =>
                d.y < -0.4f ? Pal.Hex("#F2E6D2") : (Noise.Value3(p * 900f) > 0.78f && d.y > 0.2f ? Pal.Hex("#F2E6D2") : Color.Lerp(Pal.Shade(Fawn, 0.85f), Fawn, d.y * 0.5f + 0.5f)));
            b.AddBlob(new Vector3(0f, 0.019f, 0.0085f), new Vector3(0.0028f, 0.0045f, 0.0035f), 1, 0f, 72, (_, __) => Fawn);
            b.AddBlob(new Vector3(0f, 0.0185f, -0.0095f), new Vector3(0.0018f, 0.0024f, 0.0012f), 0, 0f, 73, (_, __) => Pal.Hex("#F7F1E6"));   // white tail
            return b.ToMesh("DeerBody");
        }

        private static Mesh DeerHead()
        {
            var b = new MeshBuilder();
            b.Push(Vector3.zero, Quaternion.Euler(20f, 0f, 0f));
            b.AddBlob(new Vector3(0f, 0.0042f, 0.0035f), new Vector3(0.0024f, 0.0026f, 0.0045f), 1, 0.02f, 74, (d, _) => d.z > 0.7f ? Pal.Hex("#2A2224") : Fawn);
            foreach (int s in new[] { -1, 1 })
            {
                b.Push(new Vector3(s * 0.0024f, 0.0062f, 0.0012f), Quaternion.Euler(0f, 0f, s * -55f));
                b.AddBlob(new Vector3(0f, 0.0018f, 0f), new Vector3(0.001f, 0.0022f, 0.0005f), 0, 0f, s + 75, (_, __) => Fawn);
                b.Pop();
                // Antlers: a main beam with two tines.
                b.Push(new Vector3(s * 0.0012f, 0.0068f, 0.002f), Quaternion.Euler(-15f, 0f, s * -25f));
                b.AddCylinder(Vector3.zero, 0.0004f, 0.0003f, 0.0065f, 4, Pal.Hex("#D9C8A8"), Pal.Hex("#F2E6D2"), false);
                b.Push(new Vector3(0f, 0.003f, 0f), Quaternion.Euler(-50f, 0f, 0f));
                b.AddCylinder(Vector3.zero, 0.0003f, 0.0002f, 0.0028f, 4, Pal.Hex("#D9C8A8"), Pal.Hex("#F2E6D2"), false);
                b.Pop();
                b.Pop();
                b.AddBlob(new Vector3(s * 0.0019f, 0.0052f, 0.0052f), Vector3.one * 0.0005f, 0, 0f, s + 77, (_, __) => Color.black);
            }
            b.Pop();
            return b.ToMesh("DeerHead");
        }

        private static Mesh CowHead()
        {
            var b = new MeshBuilder();
            var white = Pal.Hex("#F4F0EA"); var black = Pal.Hex("#2A2626"); var pink = Pal.Hex("#E9A3A8");
            b.AddBlob(new Vector3(0f, 0.0008f, 0.004f), new Vector3(0.0036f, 0.0038f, 0.0046f), 1, 0.05f, 15, (d, p) => d.x > 0.3f ? black : white);
            b.AddBlob(new Vector3(0f, -0.0012f, 0.0082f), new Vector3(0.0032f, 0.0024f, 0.0022f), 1, 0.05f, 16, (_, __) => pink);   // snout
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBlob(new Vector3(s * 0.0012f, -0.0008f, 0.0102f), Vector3.one * 0.00045f, 0, 0f, s + 17, (_, __) => Pal.Shade(pink, 0.6f));
                b.Push(new Vector3(s * 0.0034f, 0.0024f, 0.0032f), Quaternion.Euler(0f, 0f, s * -70f));
                b.AddBlob(new Vector3(0f, 0.0018f, 0f), new Vector3(0.0011f, 0.0022f, 0.0006f), 0, 0f, s + 19, (_, __) => white);
                b.Pop();
                b.Push(new Vector3(s * 0.0022f, 0.0042f, 0.003f), Quaternion.Euler(0f, 0f, s * -30f));
                b.AddCylinder(Vector3.zero, 0.0006f, 0.0002f, 0.0024f, 5, Pal.Hex("#E8DCC0"), Pal.Hex("#B8A888"), false);
                b.Pop();
                b.AddBlob(new Vector3(s * 0.0019f, 0.0018f, 0.0072f), Vector3.one * 0.0006f, 0, 0f, s + 21, (_, __) => Color.white);
                b.AddBlob(new Vector3(s * 0.002f, 0.0019f, 0.0077f), Vector3.one * 0.00032f, 0, 0f, s + 23, (_, __) => Color.black);
            }
            return b.ToMesh("CowHead");
        }
    }
}
