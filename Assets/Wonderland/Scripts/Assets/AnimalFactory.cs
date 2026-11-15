using UnityEngine;

namespace Wonderland
{
    public enum AnimalKind { Sheep = 0, Cow = 1 }

    /// <summary>
    /// Storybook farm animals as two meshes each: the body (with legs and tail) and the head, which nods separately while
    /// grazing. A little bigger than true scale so they read at arm's length. Pivot at the ground between the feet; the
    /// head's pivot is its neck joint (<see cref="NeckOffset"/>). Front faces +Z.
    /// </summary>
    public static class AnimalFactory
    {
        private static readonly Mesh[] Bodies = new Mesh[2], Heads = new Mesh[2];

        public static Mesh Body(AnimalKind kind) => Bodies[(int)kind] ??= kind == AnimalKind.Sheep ? SheepBody() : CowBody();
        public static Mesh Head(AnimalKind kind) => Heads[(int)kind] ??= kind == AnimalKind.Sheep ? SheepHead() : CowHead();

        /// <summary>Neck joint position relative to the body pivot.</summary>
        public static Vector3 NeckOffset(AnimalKind kind) => kind == AnimalKind.Sheep ? new Vector3(0f, 0.0125f, 0.0085f) : new Vector3(0f, 0.016f, 0.015f);

        public static float Radius(AnimalKind kind) => kind == AnimalKind.Sheep ? 0.012f : 0.018f;

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
