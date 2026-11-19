using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Storybook villagers, 2.4 cm tall (the world's scale: see Scale): chunky little figures with round heads, rosy cheeks and
    /// hats (straw hat, flat cap, bonnet), in six outfits. Five parts per figure so they can walk and wave: body (torso,
    /// head, hat), two arms (pivot at the shoulder, hanging down -Y) and two legs (pivot at the hip). Front faces +Z.
    /// </summary>
    public static class PersonFactory
    {
        public const int StyleCount = 6;
        /// <summary>
        /// The figures' size against everything else: 2.4 cm, about 1.75 m tall in the world's scale - the doorways,
        /// benches, cars, buses and trains are made to that (a little chunky, as storybook figures are). They are modelled
        /// at 3 cm and drawn at this scale.
        /// </summary>
        public const float Scale = 0.8f;
        private const float HipModel = 0.0118f;
        public static readonly Vector3 Hip = new(0f, HipModel * Scale, 0f);
        public const float LegX = 0.0019f * Scale, ShoulderX = 0.0048f * Scale, ShoulderY = 0.0215f * Scale;
        /// <summary>A figure's height (to the top of the head).</summary>
        public const float Height = 0.03f * Scale;
        private static readonly Mesh[] Bodies = new Mesh[StyleCount * MeshBuilder.Levels], Arms = new Mesh[StyleCount * MeshBuilder.Levels], Legs = new Mesh[StyleCount * MeshBuilder.Levels];

        private struct Outfit { public Color Shirt, Pants, Hat, Skin, Hair; public int HatKind; public bool Dress; }

        private static Outfit Style(int s) => s switch
        {
            0 => new Outfit { Shirt = Pal.Hex("#C8453A"), Pants = Pal.Hex("#3E4F7A"), Hat = Pal.Hex("#E6C46A"), Skin = Pal.Hex("#F2C9A6"), Hair = Pal.Hex("#6B4630"), HatKind = 0 },
            1 => new Outfit { Shirt = Pal.Hex("#5E9A63"), Pants = Pal.Hex("#6B4F3A"), Hat = Pal.Hex("#4A4A52"), Skin = Pal.Hex("#E3B48E"), Hair = Pal.Hex("#2E2420"), HatKind = 1 },
            2 => new Outfit { Shirt = Pal.Hex("#F2A7C3"), Pants = Pal.Hex("#F2A7C3"), Hat = Pal.Hex("#F7F1E6"), Skin = Pal.Hex("#F5D2B5"), Hair = Pal.Hex("#C98A4B"), HatKind = 2, Dress = true },
            3 => new Outfit { Shirt = Pal.Hex("#4A7FB8"), Pants = Pal.Hex("#2F3A4A"), Hat = Pal.Hex("#8C4A3C"), Skin = Pal.Hex("#C68E68"), Hair = Pal.Hex("#1E1A18"), HatKind = 1 },
            4 => new Outfit { Shirt = Pal.Hex("#E2B04A"), Pants = Pal.Hex("#7A3E4A"), Hat = Pal.Hex("#7A3E4A"), Skin = Pal.Hex("#F2C9A6"), Hair = Pal.Hex("#E8D3A0"), HatKind = 2, Dress = true },
            _ => new Outfit { Shirt = Pal.Hex("#F7F1E6"), Pants = Pal.Hex("#4E6E3A"), Hat = Pal.Hex("#D9A94E"), Skin = Pal.Hex("#8E5E42"), Hair = Pal.Hex("#2E2420"), HatKind = 0 },
        };

        public static Mesh Body(int s, int detail = 0) => Bodies[s % StyleCount * MeshBuilder.Levels + detail + 1] ??= MeshBuilder.At(detail, Figure, () => BuildBody(Style(s % StyleCount)));
        public static Mesh Arm(int s, int detail = 0) => Arms[s % StyleCount * MeshBuilder.Levels + detail + 1] ??= MeshBuilder.At(detail, Figure, () => BuildArm(Style(s % StyleCount)));
        public static Mesh Leg(int s, int detail = 0) => Legs[s % StyleCount * MeshBuilder.Levels + detail + 1] ??= MeshBuilder.At(detail, Figure, () => BuildLeg(Style(s % StyleCount)));
        /// <summary>Size scale of the figures' levels of detail (see <see cref="MeshBuilder.FeatureScale"/>).</summary>
        public const float Figure = 0.4f;
        private static Mesh crate;

        /// <summary>A little crate of produce, carried in front (the figure's local frame, centred).</summary>
        public static Mesh Crate => crate ??= BuildCrate();

        private static readonly Mesh[] faces = new Mesh[StyleCount];

        /// <summary>
        /// A passenger seen through a car's back windows (both sides): their face, hair and hat as a flat silhouette just
        /// outside the glass - the car's windows are painted, so a figure inside wouldn't show. Local to the car.
        /// </summary>
        public static Mesh WindowFace(int s) => faces[s % StyleCount] ??= BuildWindowFace(Style(s % StyleCount));

        private static Mesh BuildWindowFace(Outfit o)
        {
            var b = new MeshBuilder();
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * 0.0096f;
                b.AddBlob(new Vector3(x, 0.0163f, -0.0085f), new Vector3(0.0005f, 0.0029f, 0.0026f), 1, 0f, 70 + side, (_, __) => o.Skin);
                b.AddBlob(new Vector3(x, 0.0182f, -0.0092f), new Vector3(0.00055f, 0.0016f, 0.0028f), 1, 0f, 72 + side, (_, __) => o.HatKind == 0 ? o.Hat : o.Hair);
                b.AddBlob(new Vector3(x, 0.0128f, -0.0085f), new Vector3(0.0005f, 0.0012f, 0.0034f), 1, 0f, 74 + side, (_, __) => o.Shirt);   // shoulders
            }
            return b.ToMesh("WindowFace");
        }

        private static Mesh BuildCrate()
        {
            var b = new MeshBuilder();
            var wood = Pal.Hex("#B07A45");
            b.AddBox(new Vector3(0f, -0.0022f, 0f), new Vector3(0.0074f, 0.0044f, 0.0052f), wood, Pal.Shade(wood, 1.1f), Pal.Shade(wood, 0.7f), false);
            b.AddBox(new Vector3(0f, -0.0008f, 0f), new Vector3(0.0078f, 0.0009f, 0.0056f), Pal.Shade(wood, 0.8f));   // the rim
            for (int i = 0; i < 4; i++)
                b.AddBlob(new Vector3(-0.0024f + i * 0.0016f, 0.0003f, (i % 2 - 0.5f) * 0.0016f), Vector3.one * 0.0011f, 0, 0f, 20 + i, (_, __) => i % 2 == 0 ? Pal.Hex("#D9473A") : Pal.Hex("#E8B84A"));
            return b.ToMesh("Crate");
        }

        private static Mesh BuildBody(Outfit o)
        {
            var b = new MeshBuilder();
            b.Push(Matrix4x4.Scale(Vector3.one * Scale));
            // Torso: a rounded barrel (a skirt flares out for dresses), with a belt and buttons.
            if (o.Dress)
                b.AddCylinder(new Vector3(0f, 0.0085f, 0f), 0.0058f, 0.0036f, 0.0128f, 10, Pal.Shade(o.Shirt, 0.85f), o.Shirt, true, true);
            else
            {
                b.AddCylinder(new Vector3(0f, 0.0112f, 0f), 0.0036f, 0.0039f, 0.0106f, 10, o.Shirt, Pal.Shade(o.Shirt, 1.1f), true, true);
                b.AddCylinder(new Vector3(0f, 0.0125f, 0f), 0.004f, 0.004f, 0.0012f, 10, Pal.Hex("#4A3022"), Pal.Hex("#4A3022"), true);
            }
            b.AddBlob(new Vector3(0f, 0.0218f, 0f), new Vector3(0.0043f, 0.0018f, 0.0035f), 1, 0f, 1, (d, _) => o.Shirt);   // shoulders
            for (int i = 0; i < 2; i++) b.AddBlob(new Vector3(0f, 0.0165f + i * 0.0028f, 0.0039f), Vector3.one * 0.0004f, 0, 0f, i, (_, __) => Pal.Brass);
            // Head with hair, eyes, rosy cheeks and a nose.
            var head = new Vector3(0f, 0.0272f, 0f);
            b.AddBlob(head, new Vector3(0.0043f, 0.0045f, 0.0042f), 2, 0f, 2, (d, _) => Color.Lerp(Pal.Shade(o.Skin, 0.88f), o.Skin, d.y * 0.4f + 0.6f));
            b.AddBlob(head + new Vector3(0f, 0.0012f, -0.0012f), new Vector3(0.0045f, 0.0038f, 0.0038f), 1, 0.1f, 3, (_, __) => o.Hair);
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBlob(head + new Vector3(s * 0.0015f, 0.0006f, 0.0039f), Vector3.one * 0.00055f, 0, 0f, s + 4, (_, __) => Pal.Hex("#2A2224"));
                b.AddBlob(head + new Vector3(s * 0.0026f, -0.0009f, 0.0033f), new Vector3(0.0008f, 0.0006f, 0.0003f), 0, 0f, s + 6, (_, __) => Pal.Hex("#F08A8A"));
            }
            b.AddBlob(head + new Vector3(0f, -0.0003f, 0.0043f), Vector3.one * 0.0006f, 0, 0f, 8, (_, __) => Pal.Shade(o.Skin, 0.9f));
            if (b.Detail < 0)
            {
                // Right up close: a smile and eyebrows.
                for (int i = -2; i <= 2; i++)
                    b.AddBlob(head + new Vector3(i * 0.0005f, -0.0019f + Mathf.Abs(i) * 0.00022f, 0.0038f), new Vector3(0.0003f, 0.00016f, 0.00012f), 0, 0f, 40 + i, (_, __) => Pal.Hex("#A8463F"));
                foreach (int s in new[] { -1, 1 })
                {
                    b.Push(head + new Vector3(s * 0.0015f, 0.0016f, 0.0038f), Quaternion.Euler(0f, 0f, -s * 10f));
                    b.AddBox(Vector3.zero, new Vector3(0.0012f, 0.00028f, 0.0002f), Pal.Shade(o.Hair, 0.8f));
                    b.Pop();
                }
            }
            // Hat.
            var top = head + Vector3.up * 0.0034f;
            switch (o.HatKind)
            {
                case 0:   // straw hat with a band
                    b.AddCylinder(top - Vector3.up * 0.0006f, 0.0072f, 0.0072f, 0.0006f, 14, Pal.Shade(o.Hat, 0.85f), o.Hat, true, true);
                    b.AddCylinder(top, 0.0036f, 0.0032f, 0.0032f, 12, o.Hat, Pal.Shade(o.Hat, 1.1f), true);
                    b.AddCylinder(top, 0.00365f, 0.00365f, 0.0009f, 12, Pal.Hex("#A33A2E"), Pal.Hex("#A33A2E"), false);
                    break;
                case 1:   // flat cap with a peak
                    b.AddBlob(top + new Vector3(0f, -0.0002f, -0.0003f), new Vector3(0.0046f, 0.0018f, 0.0046f), 1, 0f, 9, (d, _) => o.Hat);
                    b.Push(top + new Vector3(0f, -0.0008f, 0.0042f), Quaternion.Euler(12f, 0f, 0f));
                    b.AddBox(Vector3.zero, new Vector3(0.006f, 0.0006f, 0.003f), Pal.Shade(o.Hat, 0.8f));
                    b.Pop();
                    break;
                default:  // bonnet framing the face, with a bow
                    b.AddBlob(head + new Vector3(0f, 0.0016f, -0.0016f), new Vector3(0.0052f, 0.0048f, 0.0046f), 1, 0f, 10, (d, _) => Color.Lerp(Pal.Shade(o.Hat, 0.88f), o.Hat, d.y * 0.5f + 0.5f));
                    b.AddBlob(head + new Vector3(0f, -0.0036f, 0.0026f), new Vector3(0.0016f, 0.0008f, 0.0006f), 0, 0f, 11, (_, __) => Pal.Hex("#7FB0E0"));
                    break;
            }
            return b.ToMesh("PersonBody");
        }

        private static Mesh BuildArm(Outfit o)
        {
            var b = new MeshBuilder();
            b.Push(Matrix4x4.Scale(Vector3.one * Scale));
            b.AddCylinder(new Vector3(0f, -0.0085f, 0f), 0.0011f, 0.0013f, 0.0085f, 6, o.Shirt, o.Shirt, false);
            b.AddBlob(new Vector3(0f, -0.0092f, 0f), Vector3.one * 0.0012f, 0, 0f, 12, (_, __) => o.Skin);   // hand
            return b.ToMesh("PersonArm");
        }

        private static Mesh BuildLeg(Outfit o)
        {
            var b = new MeshBuilder();
            b.Push(Matrix4x4.Scale(Vector3.one * Scale));
            float len = HipModel - 0.0012f;
            b.AddCylinder(new Vector3(0f, -len, 0f), 0.0013f, 0.0015f, len, 6, o.Dress ? o.Skin : o.Pants, o.Pants, false);
            b.AddBlob(new Vector3(0f, -len + 0.0005f, 0.0009f), new Vector3(0.0015f, 0.0009f, 0.0024f), 0, 0f, 13, (_, __) => Pal.Hex("#3A2A20"));   // shoe
            return b.ToMesh("PersonLeg");
        }
    }
}
