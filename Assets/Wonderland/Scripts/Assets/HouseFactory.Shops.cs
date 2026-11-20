using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The bigger town's shops and pleasures: a shopping mall (two wings of shop fronts either side of a glass atrium), a
    /// supermarket with its trolley bay, a news kiosk, a pizzeria with a wood-fired oven, a boutique with dresses in the
    /// windows, a florist with buckets of flowers on the pavement, and an open-air lido - a pool with a diving board, a
    /// slide, loungers and a row of changing huts. Front is -Z, like the rest.
    /// </summary>
    public static partial class HouseFactory
    {
        private static float Shops(MeshBuilder b, ref Rng rng, Style s, int id, out bool warp)
        {
            warp = false;
            switch (id)
            {
                case Catalog.ShoppingMall: return Mall(b, ref rng);
                case Catalog.Supermarket: return SupermarketHall(b, ref rng);
                case Catalog.Kiosk: return NewsKiosk(b);
                case Catalog.Boutique: return BoutiqueShop(b, ref rng);
            }
            warp = true;   // (the older trades: hand-made, leaning a little)
            switch (id)
            {
                case Catalog.Pizzeria: return PizzeriaHouse(b, ref rng, s);
                case Catalog.Florist: return FloristShop(b, ref rng, s);
            }
            warp = false;
            if (id == Catalog.Lido) return LidoPool(b, ref rng);
            return -1f;
        }

        /// <summary>A flat face given its whole width and height (AddFace takes half of each).</summary>
        private static void FullFace(MeshBuilder b, Vector3 centre, Vector3 normal, Vector3 up, float w, float h, Color colour) => b.AddFace(centre, normal, up, w / 2f, h / 2f, colour);

        /// <summary>A shop window with a coloured awning over it and a little name board above (on a wall facing -Z).</summary>
        private static void ShopFront(MeshBuilder b, Vector3 foot, float w, Color awning, string name)
        {
            FullFace(b, foot + new Vector3(0f, 0.016f, -0.0004f), Vector3.back, Vector3.up, w - 0.006f, 0.016f, Pal.Glow(Pal.WindowGlow));
            b.AddBox(foot + new Vector3(0f, 0.016f, -0.0006f), new Vector3(0.0012f, 0.016f, 0.0008f), Pal.Hex("#3A3A40"));
            Awning(b, foot + new Vector3(0f, 0.029f, -0.0015f), w - 0.002f, 0.012f, awning, Pal.Hex("#F7F1E6"));
            if (name != null) Lettering.Board(b, name, foot + new Vector3(0f, 0.037f, -0.0012f), Vector3.back, 0.0034f, awning, Pal.Hex("#F7F1E6"), Pal.Hex("#3A3A40"));
        }

        /// <summary>A planter with a round bush in it.</summary>
        private static void Planter(MeshBuilder b, Vector3 at, int seed)
        {
            b.AddBox(at + Vector3.up * 0.004f, new Vector3(0.012f, 0.008f, 0.012f), Pal.Hex("#8A8A86"), Pal.Hex("#A8A8A2"), Pal.Hex("#6E6E6A"));
            b.AddBlob(at + Vector3.up * 0.013f, new Vector3(0.0065f, 0.006f, 0.0065f), 1, 0.25f, seed, (d, _) => Color.Lerp(Pal.Hex("#2E6A2C"), Pal.Hex("#7DB04A"), d.y * 0.5f + 0.5f));
        }

        /// <summary>A table with a parasol over it.</summary>
        private static void ParasolTable(MeshBuilder b, Vector3 at, Color canopy)
        {
            b.AddCylinder(at, 0.0045f, 0.0045f, 0.008f, 10, Pal.Hex("#F2F2EE"), Pal.Hex("#F2F2EE"), true);
            b.AddCylinder(at, 0.0006f, 0.0006f, 0.026f, 5, Pal.Hex("#3A3A40"), Pal.Hex("#3A3A40"), false);
            b.AddCylinder(at + Vector3.up * 0.022f, 0.012f, 0.0008f, 0.006f, 8, canopy, Pal.Shade(canopy, 1.15f), false, true);
        }

        private static float Mall(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.22f, D = 0.13f, H = 0.05f, A = 0.06f, AH = 0.07f, Z = 0.008f;
            var wall = Pal.Hex("#EDE6D8"); var stone = Pal.Hex("#C9BBA3"); var glass = new Color(0.78f, 0.9f, 0.96f, 1f); var frame = Pal.Hex("#3A3A40");
            Foundation(b, ref rng, W, D);
            float front = Z - D / 2;
            // The wings either side, a stone band along their foot, flat roofs with the plant on top.
            foreach (int side in new[] { -1, 1 })
            {
                float cx = side * (W + A) / 4f, ww = (W - A) / 2f;
                b.AddBox(new Vector3(cx, 0.004f + H / 2, Z), new Vector3(ww, H, D), wall, wall, wall);
                b.AddBox(new Vector3(cx, 0.007f, front - 0.0004f), new Vector3(ww, 0.006f, 0.002f), stone);
                FlatRoof(b, new Vector3(cx, 0.004f + H, Z), ww, D, wall, stone);
                b.AddBox(new Vector3(cx + side * 0.02f, 0.004f + H + 0.006f, Z + 0.02f), new Vector3(0.022f, 0.008f, 0.03f), Pal.Hex("#9A9A96"));
                string[] names = side < 0 ? new[] { "SHOES", "BOOKS" } : new[] { "TOYS", "FASHION" };
                Color[] colours = side < 0 ? new[] { Pal.Hex("#2E5E8A"), Pal.Hex("#8A3A2A") } : new[] { Pal.Hex("#C8A12E"), Pal.Hex("#7A3A82") };
                for (int k = 0; k < 2; k++)
                    ShopFront(b, new Vector3(cx + (k - 0.5f) * ww / 2f, 0.004f, front), ww / 2f, colours[k], names[k]);
                // Upper windows.
                for (int k = 0; k < 4; k++) FullFace(b, new Vector3(cx - ww / 2f + 0.011f + k * (ww - 0.022f) / 3f, 0.045f, front - 0.0004f), Vector3.back, Vector3.up, 0.008f, 0.006f, Pal.Glow(Pal.WindowGlow));
            }
            // The glass atrium in the middle, under a barrel-vaulted glass roof; the entrance and its sign.
            b.AddBox(new Vector3(0f, 0.004f + AH / 2, Z), new Vector3(A, AH, D - 0.004f), glass, glass, glass);
            if (!b.TooSmall(0.002f))
            {
                for (int k = 0; k <= 6; k++) b.AddBox(new Vector3(-A / 2 + k * A / 6f, 0.004f + AH / 2, front - 0.0004f), new Vector3(0.0012f, AH, 0.0012f), frame);
                for (int k = 1; k <= 3; k++) b.AddBox(new Vector3(0f, 0.004f + k * AH / 4f, front - 0.0004f), new Vector3(A, 0.0012f, 0.0012f), frame);
            }
            var path = new[] { new Vector3(0f, 0.004f + AH, Z - D / 2 + 0.002f), new Vector3(0f, 0.004f + AH, Z + D / 2 - 0.002f) };
            const int arc = 8;
            var profile = new Vector2[arc + 1]; var colours2 = new Color[arc + 1];
            for (int i = 0; i <= arc; i++)
            {
                float a = Mathf.PI * (1f - i / (float)arc);
                profile[i] = new Vector2(Mathf.Cos(a) * A / 2f, Mathf.Sin(a) * 0.018f);
                colours2[i] = Color.Lerp(Pal.Shade(glass, 0.85f), glass, Mathf.Sin(a));
            }
            b.AddExtrusion(path, new[] { Vector3.right, Vector3.right }, new[] { Vector3.up, Vector3.up }, profile, colours2, false);
            b.AddBox(new Vector3(0f, 0.004f + 0.012f, front - 0.003f), new Vector3(0.026f, 0.024f, 0.006f), frame);   // the entrance
            FullFace(b, new Vector3(0f, 0.016f, front - 0.0062f), Vector3.back, Vector3.up, 0.02f, 0.02f, Pal.Glow(Pal.WindowGlow));
            b.AddBox(new Vector3(0f, 0.031f, front - 0.008f), new Vector3(0.036f, 0.002f, 0.014f), frame);                  // its canopy
            Lettering.Board(b, "MALL", new Vector3(0f, 0.004f + AH - 0.008f, front - 0.0016f), Vector3.back, 0.009f, Pal.Hex("#2E3A5A"), Pal.Hex("#FFD27A"), frame);
            // Out front: planters, benches and flags.
            foreach (int side in new[] { -1, 1 })
            {
                Planter(b, new Vector3(side * 0.03f, 0.004f, front - 0.018f), 30 + side);
                b.AddBox(new Vector3(side * 0.05f, 0.008f, front - 0.018f), new Vector3(0.016f, 0.002f, 0.005f), Pal.Hex("#8A5A34"));
                Flag(b, new Vector3(side * 0.1f, 0.004f, front - 0.012f), 0.07f, side < 0 ? Pal.Hex("#C8352B") : Pal.Hex("#2F6DB5"));
            }
            return 0.004f + AH + 0.018f;
        }

        private static float SupermarketHall(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.16f, D = 0.1f, H = 0.04f, Z = 0.01f;
            var wall = Pal.Hex("#F2F0EA"); var red = Pal.Hex("#C8352B"); var frame = Pal.Hex("#3A3A40");
            Foundation(b, ref rng, W, D);
            float front = Z - D / 2;
            b.AddBox(new Vector3(0f, 0.004f + H / 2, Z), new Vector3(W, H, D), wall, wall, wall);
            FlatRoof(b, new Vector3(0f, 0.004f + H, Z), W, D, wall, red);
            b.AddBox(new Vector3(0f, 0.004f + H - 0.004f, front - 0.0006f), new Vector3(W, 0.008f, 0.002f), red);   // the red band along the top
            // The glass front, the entrance canopy, the name.
            FullFace(b, new Vector3(0f, 0.018f, front - 0.0004f), Vector3.back, Vector3.up, W - 0.02f, 0.022f, Pal.Glow(Pal.WindowGlow));
            if (!b.TooSmall(0.002f)) for (int k = 0; k <= 10; k++) b.AddBox(new Vector3(-W / 2 + 0.01f + k * (W - 0.02f) / 10f, 0.018f, front - 0.0007f), new Vector3(0.0012f, 0.022f, 0.0008f), frame);
            b.AddBox(new Vector3(-0.03f, 0.033f, front - 0.008f), new Vector3(0.04f, 0.002f, 0.016f), red);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(-0.03f + s * 0.018f, 0.018f, front - 0.014f), new Vector3(0.0015f, 0.03f, 0.0015f), frame);
            Lettering.Board(b, "SUPERMARKET", new Vector3(0f, 0.004f + H + 0.008f, front + 0.002f), Vector3.back, 0.0062f, red, Pal.Hex("#FFFFFF"), frame);
            // The trolley bay: a shelter with rows of trolleys.
            var tb = new Vector3(0.05f, 0.004f, front - 0.016f);
            b.AddBox(tb + new Vector3(0f, 0.016f, 0f), new Vector3(0.03f, 0.0016f, 0.012f), red);
            foreach (int s in new[] { -1, 1 }) b.AddBox(tb + new Vector3(s * 0.014f, 0.008f, 0f), new Vector3(0.0012f, 0.016f, 0.0012f), frame);
            if (!b.TooSmall(0.002f))
                for (int k = 0; k < 6; k++)
                    b.AddBox(tb + new Vector3(-0.01f + k * 0.004f, 0.005f, 0f), new Vector3(0.003f, 0.006f, 0.008f), Pal.Hex("#B9BCC2"), Pal.Hex("#D9DCE2"), Pal.Hex("#8A8D92"));
            return 0.004f + H + 0.016f;
        }

        private static float NewsKiosk(MeshBuilder b)
        {
            var green = Pal.Hex("#2E6B4E"); var cream = Pal.Hex("#F1E6CC");
            const float W = 0.028f, D = 0.02f, H = 0.026f;
            b.AddBox(new Vector3(0f, 0.002f, 0f), new Vector3(W + 0.006f, 0.004f, D + 0.006f), Pal.Hex("#B9A88C"));
            b.AddBox(new Vector3(0f, 0.004f + H / 2, 0f), new Vector3(W, H, D), green, green, green);
            FullFace(b, new Vector3(0f, 0.019f, -D / 2 - 0.0004f), Vector3.back, Vector3.up, W - 0.008f, 0.009f, Pal.Glow(Pal.WindowGlow));   // the hatch
            b.AddBox(new Vector3(0f, 0.0135f, -D / 2 - 0.003f), new Vector3(W - 0.006f, 0.0012f, 0.006f), cream);                       // its counter
            Awning(b, new Vector3(0f, 0.027f, -D / 2 - 0.001f), W, 0.008f, Pal.Hex("#C8352B"), cream);
            b.Push(new Vector3(0f, 0.004f + H, 0f), Quaternion.identity);
            b.AddCylinder(Vector3.zero, W * 0.75f, 0.001f, 0.01f, 8, Pal.Hex("#7A3A2E"), Pal.Hex("#9A4A3A"), false, true);
            b.Pop();
            Lettering.Board(b, "NEWS", new Vector3(0f, 0.004f + H - 0.003f, -D / 2 - 0.0012f), Vector3.back, 0.0034f, cream, green, green);
            // Racks of newspapers and magazines either side.
            var papers = new[] { Pal.Hex("#F2F2EE"), Pal.Hex("#E2383E"), Pal.Hex("#2F6DB5"), Pal.Hex("#F2C12E") };
            foreach (int s in new[] { -1, 1 })
            {
                var r = new Vector3(s * (W / 2 + 0.005f), 0.004f, -D / 2 + 0.004f);
                b.AddBox(r + Vector3.up * 0.008f, new Vector3(0.006f, 0.016f, 0.008f), Pal.Hex("#3A3A40"));
                for (int k = 0; k < 3; k++) b.AddBox(r + new Vector3(0f, 0.004f + k * 0.005f, -0.0045f), new Vector3(0.005f, 0.004f, 0.0008f), papers[(k + (s > 0 ? 1 : 0)) % 4]);
            }
            return 0.004f + H + 0.01f;
        }

        private static float PizzeriaHouse(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.08f, d = 0.056f, wallH = 0.042f;
            var terracotta = Pal.Hex("#E2A070"); var roof = Pal.Hex("#B8482E"); var green = Pal.Hex("#2E8A44"); var red = Pal.Hex("#C8352B"); var white = Pal.Hex("#F7F1E6");
            Foundation(b, ref rng, w, d);
            b.AddBox(new Vector3(0f, 0.004f + wallH / 2, 0.006f), new Vector3(w, wallH, d), terracotta, terracotta, terracotta);
            HipRoof(b, new Vector3(0f, 0.004f + wallH, 0.006f), w, d, 0.026f, roof);
            float z = -d / 2 + 0.006f;
            for (int k = 0; k < 3; k++) WallWindow(b, ref rng, s, new Vector3(-0.025f + k * 0.025f, 0.036f, z), Vector3.back, 0.01f, 0.01f, true, true);
            FullFace(b, new Vector3(-0.02f, 0.017f, z - 0.0003f), Vector3.back, Vector3.up, 0.022f, 0.014f, Pal.Glow(Pal.WindowGlow));
            WallDoor(b, ref rng, s, new Vector3(0.018f, 0.004f, z), Vector3.back, 0.011f, 0.024f, true);
            // A green, white and red awning; the wood oven's chimney with a wisp of smoke; the name and a big pizza.
            Awning(b, new Vector3(-0.012f, 0.03f, z - 0.002f), 0.05f, 0.014f, green, red);
            b.AddBox(new Vector3(0.026f, 0.004f + wallH + 0.022f, 0.02f), new Vector3(0.01f, 0.03f, 0.01f), Pal.Hex("#9A6A4A"), Pal.Hex("#7A4A3A"), Pal.Hex("#8A5A3A"));
            b.AddBlob(new Vector3(0.026f, 0.004f + wallH + 0.042f, 0.02f), new Vector3(0.006f, 0.004f, 0.006f), 1, 0.3f, 7, (_, __) => new Color(0.92f, 0.92f, 0.92f));
            Lettering.Board(b, "PIZZA", new Vector3(0f, 0.004f + wallH - 0.004f, z - 0.0016f), Vector3.back, 0.006f, green, white, red);
            Big(b, new Vector3(-0.02f, 0.004f + wallH + 0.03f, 0.004f), at =>
            {
                b.Push(at, Quaternion.Euler(-70f, 0f, 0f));
                b.AddCylinder(Vector3.zero, 0.009f, 0.009f, 0.0016f, 14, Pal.Hex("#D9A55A"), Pal.Hex("#F2C46A"), true, true);
                b.AddDisc(Vector3.up * 0.0017f, 0.0076f, 14, Pal.Hex("#D8402E"), true);
                for (int k = 0; k < 5; k++) b.AddDisc(new Vector3(Mathf.Cos(k * 1.3f) * 0.0045f, 0.0019f, Mathf.Sin(k * 1.3f) * 0.0045f), 0.0014f, 8, k % 2 == 0 ? Pal.Hex("#8A2A22") : Pal.Hex("#F2E3B0"), true);
                b.Pop();
            });
            // Two tables out front.
            ParasolTable(b, new Vector3(-0.026f, 0.004f, z - 0.02f), red);
            ParasolTable(b, new Vector3(0.012f, 0.004f, z - 0.022f), green);
            return 0.004f + wallH + 0.04f;
        }

        private static float BoutiqueShop(MeshBuilder b, ref Rng rng)
        {
            const float w = 0.072f, d = 0.054f, wallH = 0.05f;
            var facade = Pal.Hex("#2E2E36"); var cream = Pal.Hex("#EFE6D2"); var gold = Pal.Hex("#D9B45A");
            Foundation(b, ref rng, w, d);
            b.AddBox(new Vector3(0f, 0.004f + wallH / 2, 0.006f), new Vector3(w, wallH, d), cream, cream, cream);
            float z = -d / 2 + 0.006f;
            b.AddBox(new Vector3(0f, 0.004f + 0.017f, z - 0.0008f), new Vector3(w, 0.034f, 0.002f), facade);   // the dark shop front
            FlatRoof(b, new Vector3(0f, 0.004f + wallH, 0.006f), w, d, cream, gold);
            // Two tall windows, a dress on a dummy in each; the door between.
            foreach (int sx in new[] { -1, 1 })
            {
                var win = new Vector3(sx * 0.021f, 0.018f, z - 0.002f);
                FullFace(b, win, Vector3.back, Vector3.up, 0.02f, 0.024f, Pal.Glow(Pal.WindowGlow));
                var dummy = new Vector3(sx * 0.021f, 0.006f, z + 0.002f);
                b.AddCylinder(dummy, 0.0005f, 0.0005f, 0.006f, 5, Pal.Hex("#3A3A40"), Pal.Hex("#3A3A40"), false);
                var dress = sx < 0 ? Pal.Hex("#C8355E") : Pal.Hex("#2F6DB5");
                b.AddCylinder(dummy + Vector3.up * 0.006f, 0.0055f, 0.0018f, 0.014f, 10, dress, Pal.Shade(dress, 1.1f), false);
                b.AddBlob(dummy + Vector3.up * 0.022f, Vector3.one * 0.0022f, 1, 0f, sx + 5, (_, __) => Pal.Hex("#E8D8C8"));
            }
            FullFace(b, new Vector3(0f, 0.016f, z - 0.002f), Vector3.back, Vector3.up, 0.01f, 0.022f, Pal.Hex("#4A3A30"));
            Awning(b, new Vector3(0f, 0.037f, z - 0.002f), w, 0.012f, facade, cream);
            for (int k = 0; k < 3; k++) FullFace(b, new Vector3(-0.022f + k * 0.022f, 0.046f, z - 0.0004f), Vector3.back, Vector3.up, 0.011f, 0.007f, Pal.Glow(Pal.WindowGlow));
            Lettering.Board(b, "BOUTIQUE", new Vector3(0f, 0.004f + wallH + 0.007f, z), Vector3.back, 0.0052f, facade, gold, gold);
            Planter(b, new Vector3(-0.04f, 0.004f, z - 0.012f), 12);
            return 0.004f + wallH + 0.014f;
        }

        private static float FloristShop(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.07f, d = 0.054f, wallH = 0.042f;
            var green = Pal.Hex("#3E7A4A"); var cream = Pal.Hex("#F3EEDF");
            Foundation(b, ref rng, w, d);
            b.AddBox(new Vector3(0f, 0.004f + wallH / 2, 0.006f), new Vector3(w, wallH, d), s.Plaster, s.Plaster, s.Plaster);
            HipRoof(b, new Vector3(0f, 0.004f + wallH, 0.006f), w, d, 0.024f, s.Roof);
            float z = -d / 2 + 0.006f;
            b.AddBox(new Vector3(0f, 0.004f + 0.016f, z - 0.0008f), new Vector3(w - 0.004f, 0.032f, 0.002f), green);
            FullFace(b, new Vector3(-0.014f, 0.018f, z - 0.002f), Vector3.back, Vector3.up, 0.026f, 0.02f, Pal.Glow(Pal.WindowGlow));
            WallDoor(b, ref rng, s, new Vector3(0.02f, 0.004f, z - 0.001f), Vector3.back, 0.011f, 0.024f);
            Awning(b, new Vector3(0f, 0.035f, z - 0.002f), w, 0.012f, green, cream);
            for (int k = 0; k < 2; k++) WallWindow(b, ref rng, s, new Vector3(-0.016f + k * 0.032f, 0.04f, z), Vector3.back, 0.01f, 0.009f, false, true);
            Lettering.Board(b, "FLOWERS", new Vector3(0f, 0.004f + wallH - 0.004f, z - 0.0018f), Vector3.back, 0.005f, cream, green, green);
            // Buckets of flowers on the pavement, on tiered stands.
            var blooms = new[] { Pal.FlowerPink, Pal.FlowerYellow, Pal.Hex("#E2383E"), Pal.FlowerWhite, Pal.Hex("#8A5AC8"), Pal.Hex("#F2A030") };
            for (int k = 0; k < 6; k++)
            {
                var at = new Vector3(-0.03f + k * 0.012f, 0.004f + (k % 2) * 0.004f, z - 0.012f - (k % 2) * 0.004f);
                b.AddCylinder(at, 0.0035f, 0.003f, 0.006f, 8, Pal.Hex("#5A6A7A"), Pal.Hex("#7A8A9A"), false);
                if (b.TooSmall(0.002f)) { b.AddBox(at + Vector3.up * 0.008f, Vector3.one * 0.006f, blooms[k]); continue; }
                for (int f = 0; f < 5; f++)
                {
                    var p = at + new Vector3(Mathf.Cos(f * 1.25f) * 0.002f, 0.009f + (f % 2) * 0.0015f, Mathf.Sin(f * 1.25f) * 0.002f);
                    var c = blooms[k];
                    b.AddBlob(p, Vector3.one * 0.0016f, 0, 0f, k * 10 + f, (_, __) => c);
                }
                b.AddBlob(at + Vector3.up * 0.0065f, new Vector3(0.003f, 0.002f, 0.003f), 0, 0.2f, k, (_, __) => Pal.Hex("#4E8A3E"));
            }
            return 0.004f + wallH + 0.024f;
        }

        private static float LidoPool(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.29f, D = 0.21f, PW = 0.17f, PD = 0.08f, PZ = 0.006f;
            var deck = Pal.Hex("#E8E2D6"); var tile = Pal.Hex("#4FB6DA"); var water = new Color(0.36f, 0.76f, 0.9f, 1f); var white = Pal.Hex("#F7F7F2");
            // The tiled deck round the pool (four slabs round the hole), the pool's blue walls and its water.
            float dx0 = -W / 2, dx1 = W / 2, dz0 = -D / 2, dz1 = D / 2, px0 = -PW / 2, px1 = PW / 2, pz0 = PZ - PD / 2, pz1 = PZ + PD / 2;
            void Slab(float x0, float x1, float z0, float z1) { if (x1 > x0 && z1 > z0) b.AddBox(new Vector3((x0 + x1) / 2, 0.002f, (z0 + z1) / 2), new Vector3(x1 - x0, 0.004f, z1 - z0), Pal.Shade(deck, 0.8f), deck, Pal.Shade(deck, 0.85f)); }
            Slab(dx0, dx1, dz0, pz0); Slab(dx0, dx1, pz1, dz1); Slab(dx0, px0, pz0, pz1); Slab(px1, dx1, pz0, pz1);
            b.AddBox(new Vector3(0f, -0.002f, PZ), new Vector3(PW, 0.004f, PD), tile, tile, tile);
            b.AddBox(new Vector3(0f, 0.0028f, PZ), new Vector3(PW, 0.0004f, PD), water);
            foreach (int e in new[] { -1, 1 }) b.AddBox(new Vector3(0f, 0.0042f, PZ + e * (PD / 2 + 0.0015f)), new Vector3(PW + 0.006f, 0.0012f, 0.003f), white);   // the edging
            foreach (int e in new[] { -1, 1 }) b.AddBox(new Vector3(e * (PW / 2 + 0.0015f), 0.0042f, PZ), new Vector3(0.003f, 0.0012f, PD), white);
            if (!b.TooSmall(0.002f))
                for (int k = 1; k < 4; k++)   // lane ropes
                    for (int f = 0; f < 16; f++)
                        b.AddBlob(new Vector3(px0 + (f + 0.5f) * PW / 16f, 0.0034f, pz0 + k * PD / 4f), Vector3.one * 0.001f, 0, 0f, k * 20 + f, (_, __) => f % 2 == 0 ? Pal.Hex("#E2383E") : white);
            // The diving board at the deep end, the slide at the other.
            b.AddBox(new Vector3(px1 + 0.004f, 0.012f, PZ), new Vector3(0.006f, 0.016f, 0.008f), white);
            b.AddBox(new Vector3(px1 - 0.012f, 0.0205f, PZ), new Vector3(0.036f, 0.0016f, 0.006f), Pal.Hex("#2F6DB5"));
            b.AddBox(new Vector3(px0 - 0.012f, 0.016f, pz1 - 0.012f), new Vector3(0.008f, 0.024f, 0.008f), white);
            for (int k = 0; k < 8; k++)
            {
                float t = k / 7f;
                var p = new Vector3(px0 - 0.012f + t * 0.03f, 0.028f - t * 0.024f, pz1 - 0.012f - Mathf.Sin(t * Mathf.PI) * 0.01f);
                b.AddBox(p, new Vector3(0.007f, 0.0016f, 0.008f), Pal.Hex("#F2C12E"));
            }
            // Loungers and parasols along the near side; changing huts along the back.
            var parasols = new[] { Pal.Hex("#E2383E"), Pal.Hex("#F2C12E"), Pal.Hex("#2F6DB5"), Pal.Hex("#3E8A44") };
            for (int k = 0; k < 4; k++)
            {
                float x = -0.1f + k * 0.066f, z = pz0 - 0.022f;
                b.Push(new Vector3(x, 0.006f, z), Quaternion.Euler(0f, 0f, 0f));
                b.AddBox(Vector3.zero, new Vector3(0.007f, 0.0015f, 0.018f), white);
                b.Push(new Vector3(0f, 0.003f, 0.007f), Quaternion.Euler(-35f, 0f, 0f));
                b.AddBox(Vector3.zero, new Vector3(0.007f, 0.0015f, 0.007f), white);
                b.Pop();
                b.Pop();
                b.AddBox(new Vector3(x + 0.012f, 0.006f, z), new Vector3(0.007f, 0.0015f, 0.018f), white);
                b.AddCylinder(new Vector3(x + 0.006f, 0.004f, z - 0.01f), 0.0006f, 0.0006f, 0.03f, 5, Pal.Hex("#3A3A40"), Pal.Hex("#3A3A40"), false);
                b.AddCylinder(new Vector3(x + 0.006f, 0.028f, z - 0.01f), 0.014f, 0.0008f, 0.007f, 8, parasols[k], Pal.Shade(parasols[k], 1.15f), false, true);
            }
            var huts = new[] { Pal.Hex("#E2383E"), Pal.Hex("#F2F2EE"), Pal.Hex("#2F6DB5"), Pal.Hex("#F2C12E"), Pal.Hex("#3E8A44"), Pal.Hex("#F2F2EE"), Pal.Hex("#C8355E") };
            for (int k = 0; k < huts.Length; k++)
            {
                var at = new Vector3(-0.12f + k * 0.04f, 0.004f, dz1 - 0.02f);
                b.AddBox(at + Vector3.up * 0.012f, new Vector3(0.034f, 0.024f, 0.022f), huts[k], Pal.Shade(huts[k], 1.05f), Pal.Shade(huts[k], 0.8f));
                FullFace(b, at + new Vector3(0f, 0.011f, -0.0114f), Vector3.back, Vector3.up, 0.012f, 0.02f, Pal.Shade(huts[k], 0.7f));
                b.Push(at + new Vector3(0f, 0.025f, 0f), Quaternion.Euler(-10f, 0f, 0f));
                b.AddBox(Vector3.zero, new Vector3(0.036f, 0.0016f, 0.026f), Pal.Hex("#6E6E74"));
                b.Pop();
            }
            // A low white fence round it all; the entrance and its sign at the front.
            if (!b.TooSmall(0.002f))
            {
                foreach (int e in new[] { -1, 1 }) b.AddBox(new Vector3(0f, 0.01f, e * (D / 2 - 0.002f)), new Vector3(W, 0.0012f, 0.0012f), white);
                foreach (int e in new[] { -1, 1 }) b.AddBox(new Vector3(e * (W / 2 - 0.002f), 0.01f, 0f), new Vector3(0.0012f, 0.0012f, D), white);
                for (float x = -W / 2; x <= W / 2 + 1e-4f; x += 0.024f)
                    foreach (int e in new[] { -1, 1 }) b.AddBox(new Vector3(x, 0.007f, e * (D / 2 - 0.002f)), new Vector3(0.0012f, 0.007f, 0.0012f), white);
            }
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.018f, 0.018f, dz0 + 0.003f), new Vector3(0.004f, 0.032f, 0.004f), Pal.Hex("#2F6DB5"));
            b.AddBox(new Vector3(0f, 0.034f, dz0 + 0.003f), new Vector3(0.044f, 0.012f, 0.003f), Pal.Hex("#2F6DB5"));
            Lettering.Board(b, "LIDO", new Vector3(0f, 0.034f, dz0 + 0.001f), Vector3.back, 0.0075f, Pal.Hex("#2F6DB5"), white, white);
            return 0.05f;
        }
    }
}
