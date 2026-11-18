using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The working world's buildings: places that bring activities with them - a taxi rank, and the rest added as the
    /// living world grows. Same parts and palette as the village.
    /// </summary>
    public static partial class HouseFactory
    {
        private static float Living(MeshBuilder b, ref Rng rng, Style s, int id, int style, out bool warp)
        {
            warp = false;
            switch (id)
            {
                case Catalog.TaxiRank: return TaxiShelter(b);
                case Catalog.Sawmill: return SawmillShed(b, ref rng);
                case Catalog.Letterbox: return PillarBox(b);
                case Catalog.CarPark: return CarParkBays(b);
                case Catalog.FreightYard: return FreightYardPad(b, ref rng);
            }
            warp = true;
            switch (id)
            {
                case Catalog.FurnitureShop: return Storefront(b, ref rng, s, 0.09f, 0.072f, 2, Pal.Hex("#8A5A34"), Pal.Hex("#F2E3C2"), 0);
                case Catalog.IceCream: return Storefront(b, ref rng, s, 0.07f, 0.06f, 1, Pal.Hex("#F2A7C3"), Pal.Hex("#FFFFFF"), 1);
                case Catalog.Pub: return Storefront(b, ref rng, s, 0.1f, 0.08f, 2, Pal.Hex("#2E5A3A"), Pal.Hex("#E8C77A"), 2);
                case Catalog.Hotel: return Storefront(b, ref rng, s, 0.13f, 0.08f, 3, Pal.Hex("#5A1E2A"), Pal.Hex("#F2E3C2"), 3);
                case Catalog.PostOffice: return Storefront(b, ref rng, s, 0.09f, 0.072f, 2, Pal.Hex("#C8392E"), Pal.Hex("#F2C12E"), 4);
            }
            warp = false;
            return -1f;
        }

        /// <summary>
        /// A shop on the high street, its trade told by its window and what stands outside: a chair in the furniture shop's
        /// window (kind 0), a giant cone and parasol tables at the ice cream parlour (1), a hanging sign and benches at the
        /// pub (2), balconies and flags on the hotel (3), the post office's red door and posting slot (4). One or more
        /// storeys under a tiled roof, an awning over the window.
        /// </summary>
        private static float Storefront(MeshBuilder b, ref Rng rng, Style s, float W, float D, int floors, Color awning, Color trim, int kind)
        {
            float storey = 0.034f, H = 0.028f + storey * (floors - 1) + 0.006f, R = Mathf.Min(0.05f, D * 0.6f);
            if (kind == 1) { s.Plaster = Pal.Hex("#FBE6EE"); s.Roof = Pal.Hex("#7FC4C8"); }
            if (kind == 2) { s.Plaster = Pal.Hex("#F2EBDD"); s.Timber = Pal.Hex("#3A2A20"); }
            if (kind == 3) { s.Plaster = Pal.Hex("#F5E6C8"); s.Roof = Pal.RoofSlate; }
            if (kind == 4) { s.Plaster = Pal.Hex("#F2EBDD"); s.Door = Pal.Hex("#C8392E"); }
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, s.Plaster);
            if (kind == 2) TimberFrame(b, W, D, H, s.Timber);
            // The shop window, glowing, its trade in it.
            float winX = W * 0.18f;
            b.Push(new Vector3(winX, 0.017f, -D / 2 - 0.0005f));
            b.AddFace(Vector3.zero, Vector3.back, Vector3.up, W * 0.17f, 0.011f, Pal.Glow(Pal.Hex("#FFE3A8")));
            b.AddBox(new Vector3(0f, -0.0125f, -0.002f), new Vector3(W * 0.38f, 0.003f, 0.005f), trim);
            if (kind == 0)
            {
                // A chair and a little table in the window.
                var wood = Pal.Hex("#8A5A34");
                b.AddBox(new Vector3(-0.006f, -0.006f, -0.003f), new Vector3(0.006f, 0.001f, 0.004f), wood);
                b.AddBox(new Vector3(-0.006f, -0.002f, -0.001f), new Vector3(0.006f, 0.007f, 0.001f), wood);
                b.AddBox(new Vector3(0.007f, -0.005f, -0.003f), new Vector3(0.009f, 0.001f, 0.005f), Pal.Shade(wood, 1.2f));
            }
            if (kind == 1)
                for (int k = 0; k < 4; k++) b.AddBlob(new Vector3(-0.009f + k * 0.006f, -0.008f, -0.0035f), Vector3.one * 0.0022f, 0, 0f, k, (_, __) => Pal.Hex(new[] { "#F2A7C3", "#FFF2C0", "#A8D8A8", "#8A5A34" }[k]));
            b.Pop();
            Wall(b, new Vector3(-W * 0.2f, 0.004f, -D / 2), Vector3.back); Door(b, ref rng, 0.016f, 0.03f, s, kind == 3, kind == 3); b.Pop();
            for (int f = 1; f < floors; f++)
            {
                float y = 0.028f + storey * (f - 1) + storey * 0.55f;
                var xs = W > 0.11f ? new[] { -W * 0.34f, -W * 0.11f, W * 0.11f, W * 0.34f } : new[] { -W * 0.25f, W * 0.25f };
                Windows(b, ref rng, s, -D / 2, xs, y, 0.013f, 0.016f, true, kind != 3);
                if (kind == 3) foreach (float x in xs) b.AddBox(new Vector3(x, y - 0.011f, -D / 2 - 0.005f), new Vector3(0.019f, 0.0012f, 0.009f), Pal.Hex("#3A3A40"));   // balconies
            }
            SideWindows(b, ref rng, s, W / 2, new[] { 0f }, H * 0.6f, 0.013f, 0.016f);
            Awning(b, new Vector3(winX, 0.032f, -D / 2), W * 0.4f, 0.014f, awning, Pal.Hex("#F7F1E6"));
            StoryRoof(b, ref rng, W, D, H, R, 0.009f, 0.002f, s.Roof, s.Plaster, s.Trim, 6, kind == 2);
            Chimney(b, ref rng, new Vector3(W * 0.3f, H, 0.012f), R + 0.014f, Pal.Brick, 1);
            switch (kind)
            {
                case 1:
                    // Parasol tables in front.
                    foreach (float x in new[] { -0.024f, 0.024f })
                    {
                        var at = new Vector3(x, 0f, -D / 2 - 0.03f);
                        b.AddCylinder(at, 0.0006f, 0.0006f, 0.026f, 4, Pal.TrainBlack, Pal.TrainBlack, false);
                        b.AddCylinder(at + Vector3.up * 0.007f, 0.006f, 0.006f, 0.001f, 10, Pal.FlowerWhite, Pal.FlowerWhite, true, true);
                        b.AddCylinder(at + Vector3.up * 0.026f, 0.014f, 0.001f, 0.006f, 10, awning, Pal.Hex("#FFFFFF"), true);
                    }
                    break;
                case 2:
                    // The hanging sign on a bracket, and benches outside.
                    b.AddBox(new Vector3(W / 2 + 0.008f, 0.042f, -D / 2 + 0.006f), new Vector3(0.016f, 0.0012f, 0.0012f), Pal.TrainBlack);
                    b.AddBox(new Vector3(W / 2 + 0.013f, 0.034f, -D / 2 + 0.006f), new Vector3(0.0012f, 0.013f, 0.011f), awning);
                    b.AddBox(new Vector3(W / 2 + 0.0124f, 0.034f, -D / 2 + 0.006f), new Vector3(0.0014f, 0.009f, 0.007f), trim);
                    foreach (float x in new[] { -0.03f, 0.018f })
                    {
                        b.AddBox(new Vector3(x, 0.006f, -D / 2 - 0.022f), new Vector3(0.02f, 0.0012f, 0.008f), Pal.Hex("#8A5A34"));
                        foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(x, 0.0035f, -D / 2 - 0.022f + sz * 0.0085f), new Vector3(0.02f, 0.001f, 0.004f), Pal.Hex("#6B4630"));
                    }
                    break;
                case 3:
                    foreach (float x in new[] { -W / 2 + 0.01f, W / 2 - 0.01f }) Flag(b, new Vector3(x, H + R * 0.6f, -D / 2 + 0.01f), 0.03f, x < 0f ? Pal.Hex("#2F5A8A") : Pal.Hex("#C8392E"));
                    break;
                case 4:
                    b.AddBox(new Vector3(-W * 0.2f + 0.014f, 0.016f, -D / 2 - 0.0006f), new Vector3(0.008f, 0.0016f, 0.0008f), Pal.Brass);   // the posting slot
                    break;
            }
            return H + R + 0.02f;
        }

        /// <summary>The sawmill: an open shed on posts over a big round saw, logs waiting at one end and planks stacked at the other.</summary>
        private static float SawmillShed(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.14f, D = 0.09f, H = 0.05f;
            var wood = Pal.Hex("#8A5A34"); var roof = Pal.Hex("#6E4A32"); var bark = Pal.Hex("#6B4630"); var plank = Pal.Hex("#E2C08A");
            b.AddBox(new Vector3(0f, 0.0006f, 0f), new Vector3(W + 0.02f, 0.0012f, D + 0.02f), Pal.Hex("#B8A27E"));   // trodden yard with sawdust
            foreach (float x in new[] { -W / 2, 0f, W / 2 })
            foreach (int sz in new[] { -1, 1 })
                b.AddBox(new Vector3(x, H / 2, sz * D * 0.35f), new Vector3(0.004f, H, 0.004f), wood);
            foreach (int sz in new[] { -1, 1 })
            {
                var a = new Vector3(-W / 2 - 0.008f, H, sz * (D * 0.35f + 0.012f)); var c = new Vector3(W / 2 + 0.008f, H, sz * (D * 0.35f + 0.012f));
                var top = new Vector3(0f, H + 0.022f, 0f);
                b.AddQuad(a, new Vector3(a.x, top.y, 0f), new Vector3(c.x, top.y, 0f), c, roof);
                b.AddQuad(c, new Vector3(c.x, top.y, 0f), new Vector3(a.x, top.y, 0f), a, roof);
            }
            // The saw bench and its round blade.
            b.AddBox(new Vector3(0f, 0.008f, 0f), new Vector3(0.08f, 0.016f, 0.012f), Pal.Hex("#5A5A62"));
            b.Push(new Vector3(0f, 0.018f, 0f), Quaternion.Euler(0f, 0f, 90f));
            b.AddCylinder(new Vector3(0f, -0.0005f, 0f), 0.011f, 0.011f, 0.001f, 18, Pal.Hex("#D8DCE0"), Pal.Hex("#F2F4F6"), true, true);
            b.Pop();
            b.Push(new Vector3(-0.02f, 0.02f, 0f), Quaternion.Euler(0f, 0f, 90f));
            b.AddCylinder(new Vector3(0f, -0.018f, 0f), 0.004f, 0.004f, 0.036f, 8, bark, plank, true, true);   // a log on the bench
            b.Pop();
            // Logs waiting, planks stacked.
            for (int i = 0; i < 6; i++)
            {
                int row = i < 3 ? 0 : i < 5 ? 1 : 2; float z = (i < 3 ? i - 1 : i < 5 ? i - 3.5f : 0f) * 0.009f;
                b.Push(new Vector3(-W / 2 - 0.004f, 0.0045f + row * 0.0078f, z - D * 0.2f), Quaternion.Euler(0f, 0f, 90f));
                b.AddCylinder(new Vector3(0f, -0.022f, 0f), 0.0045f, 0.0045f, 0.044f, 8, bark, plank, true, true);
                b.Pop();
            }
            for (int k = 0; k < 5; k++) b.AddBox(new Vector3(W / 2 - 0.005f, 0.0018f + k * 0.0032f, -D * 0.2f), new Vector3(0.03f, 0.0026f, 0.018f), Pal.Shade(plank, 1f - k * 0.04f));
            return H + 0.03f;
        }

        /// <summary>A red pillar box for letters (the post van empties it on its round).</summary>
        private static float PillarBox(MeshBuilder b)
        {
            var red = Pal.Hex("#C8392E");
            b.AddCylinder(Vector3.zero, 0.0042f, 0.0042f, 0.019f, 12, red, red, true);
            b.AddBlob(new Vector3(0f, 0.019f, 0f), new Vector3(0.0046f, 0.0024f, 0.0046f), 1, 0f, 3, (_, __) => red);
            b.AddBox(new Vector3(0f, 0.0198f, 0f), new Vector3(0.0098f, 0.0012f, 0.0098f), Pal.Shade(red, 0.8f));
            b.AddBox(new Vector3(0f, 0.015f, -0.0042f), new Vector3(0.0042f, 0.0008f, 0.0006f), Pal.TrainBlack);   // the slot
            b.AddBox(new Vector3(0f, 0.0105f, -0.0042f), new Vector3(0.0034f, 0.0024f, 0.0004f), Pal.FlowerWhite);   // collection times
            return 0.024f;
        }

        /// <summary>A car park: tarmac with painted bays either side of an aisle, a lamp at each end. Visitors' cars park in it.</summary>
        private static float CarParkBays(MeshBuilder b)
        {
            const float W = 0.16f, D = 0.1f;
            b.AddBox(new Vector3(0f, 0.0008f, 0f), new Vector3(W, 0.0016f, D), Pal.Hex("#4A4A50"), Pal.Hex("#55555C"), Pal.Hex("#3A3A40"));
            var paint = Pal.Hex("#F2EFE8");
            foreach (int sz in new[] { -1, 1 })
                for (int i = 0; i <= ParkingBays / 2; i++)
                    b.AddBox(new Vector3(-W / 2 + 0.01f + i * (W - 0.02f) / (ParkingBays / 2), 0.0017f, sz * (D / 2 - 0.016f)), new Vector3(0.0012f, 0.0002f, 0.026f), paint);
            b.AddBox(new Vector3(0f, 0.0017f, 0f), new Vector3(W - 0.03f, 0.0002f, 0.0016f), Pal.Hex("#F2C12E"));   // arrows down the aisle
            foreach (int sx in new[] { -1, 1 })
            {
                b.AddCylinder(new Vector3(sx * (W / 2 - 0.004f), 0f, 0f), 0.0007f, 0.0007f, 0.04f, 5, Pal.TrainBlack, Pal.TrainBlack, true);
                b.AddBlob(new Vector3(sx * (W / 2 - 0.004f), 0.04f, 0f), new Vector3(0.0028f, 0.0014f, 0.0028f), 1, 0f, 5, (_, __) => Pal.Glow(Pal.WindowGlow));
            }
            return 0.045f;
        }

        public const int ParkingBays = 8;

        /// <summary>
        /// The freight yard (its front, -Z, to the railway): a concrete pad, the rail the crane rolls along, a stack of
        /// crates waiting, and the crane driver's hut. The crane itself moves (see FreightSystem and <see cref="CraneMesh"/>).
        /// </summary>
        private static float FreightYardPad(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.15f, D = 0.08f;
            b.AddBox(new Vector3(0f, -0.001f, 0f), new Vector3(W, 0.006f, D), Pal.Hex("#A9A49A"), Pal.Hex("#B9B4AA"), Pal.Hex("#8E8A82"));
            foreach (float z in new[] { CraneRailZ - 0.004f, CraneRailZ + 0.004f }) b.AddBox(new Vector3(0f, 0.0025f, z), new Vector3(W - 0.004f, 0.001f, 0.0012f), Pal.Hex("#5A5A62"));
            // The driver's hut, with a lit window.
            b.AddBox(new Vector3(W / 2 - 0.016f, 0.012f, D / 2 - 0.014f), new Vector3(0.024f, 0.024f, 0.02f), Pal.Hex("#E2B04A"), Pal.Hex("#F2C860"), Pal.Hex("#B88A30"));
            b.AddBox(new Vector3(W / 2 - 0.016f, 0.0255f, D / 2 - 0.014f), new Vector3(0.028f, 0.003f, 0.024f), Pal.Hex("#5A5A62"));
            b.AddFace(new Vector3(W / 2 - 0.016f, 0.015f, D / 2 - 0.0241f), Vector3.back, Vector3.up, 0.012f, 0.007f, Pal.Glow(Pal.WindowGlow));
            // Pallets and a few crates around the yard (the waiting stack itself is drawn by the freight system).
            for (int k = 0; k < 3; k++) b.AddBox(new Vector3(-W / 2 + 0.014f + k * 0.014f, 0.0028f, D / 2 - 0.012f), new Vector3(0.011f, 0.0016f, 0.011f), Pal.Hex("#B07A45"));
            return 0.1f;
        }

        /// <summary>Where the crane's rail runs (local z) and how high its jib is.</summary>
        public const float CraneRailZ = 0.022f, CraneHeight = 0.085f;

        /// <summary>The crane's jib reaches this far out over the railway (local z, front).</summary>
        public const float CraneReach = -0.085f;

        private static Mesh crane, trolley;

        /// <summary>The crane: a yellow lattice tower on rail wheels with its jib out over the railway (front, -Z), and a counterweight behind.</summary>
        public static Mesh CraneMesh()
        {
            if (crane != null) return crane;
            var b = new MeshBuilder();
            var yellow = Pal.Hex("#F2C12E"); var dark = Pal.Hex("#3A3A40");
            b.AddBox(new Vector3(0f, 0.004f, 0f), new Vector3(0.022f, 0.006f, 0.016f), dark);
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                b.AddBox(new Vector3(sx * 0.005f, CraneHeight / 2f, sz * 0.005f), new Vector3(0.0016f, CraneHeight, 0.0016f), yellow);
            for (float y = 0.012f; y < CraneHeight; y += 0.012f)
            {
                b.AddBox(new Vector3(0f, y, -0.005f), new Vector3(0.011f, 0.001f, 0.001f), yellow);
                b.AddBox(new Vector3(0f, y, 0.005f), new Vector3(0.011f, 0.001f, 0.001f), yellow);
                b.AddBox(new Vector3(-0.005f, y, 0f), new Vector3(0.001f, 0.001f, 0.011f), yellow);
                b.AddBox(new Vector3(0.005f, y, 0f), new Vector3(0.001f, 0.001f, 0.011f), yellow);
            }
            float reach = CraneReach - CraneRailZ;
            b.AddBox(new Vector3(0f, CraneHeight + 0.002f, reach / 2f + 0.006f), new Vector3(0.008f, 0.004f, -reach + 0.03f), yellow);   // the jib
            b.AddBox(new Vector3(0f, CraneHeight + 0.002f, 0.02f), new Vector3(0.014f, 0.01f, 0.012f), Pal.Hex("#7A7A82"));   // counterweight
            b.AddBox(new Vector3(0f, CraneHeight - 0.008f, 0.008f), new Vector3(0.012f, 0.01f, 0.01f), Pal.Hex("#E25B4F"));   // the cab
            b.AddFace(new Vector3(0f, CraneHeight - 0.008f, 0.0029f), Vector3.back, Vector3.up, 0.008f, 0.005f, Pal.Glow(Pal.WindowGlow));
            return crane = b.ToMesh("Crane");
        }

        /// <summary>The crane's trolley and hook block (the cable is a separate, stretched part).</summary>
        public static Mesh TrolleyMesh()
        {
            if (trolley != null) return trolley;
            var b = new MeshBuilder();
            b.AddBox(new Vector3(0f, 0f, 0f), new Vector3(0.01f, 0.003f, 0.007f), Pal.Hex("#3A3A40"));
            return trolley = b.ToMesh("Trolley");
        }

        /// <summary>Where bay <paramref name="k"/> of a car park is (local) and which way a car in it faces (degrees).</summary>
        public static Vector3 ParkingBay(int k, out float yaw)
        {
            const float W = 0.16f, D = 0.1f;
            int side = k < ParkingBays / 2 ? -1 : 1, i = k % (ParkingBays / 2);
            float pitch = (W - 0.02f) / (ParkingBays / 2);
            yaw = side < 0 ? 180f : 0f;
            return new Vector3(-W / 2 + 0.01f + (i + 0.5f) * pitch, 0f, side * (D / 2 - 0.022f));
        }

        /// <summary>A taxi rank: a little glass shelter with a bench, and a yellow TAXI sign on a post at the kerb (front).</summary>
        private static float TaxiShelter(MeshBuilder b)
        {
            var frame = Pal.Hex("#3C4048"); var glass = Pal.Hex("#BFD6E2"); var yellow = Pal.Hex("#F2C12E");
            const float W = 0.034f, D = 0.014f, H = 0.026f;
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                b.AddBox(new Vector3(sx * W / 2, H / 2, sz * D / 2 + 0.004f), new Vector3(0.0012f, H, 0.0012f), frame);
            b.AddBox(new Vector3(0f, H / 2, D / 2 + 0.004f), new Vector3(W, H * 0.8f, 0.0006f), glass);   // the back
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * W / 2, H / 2, 0.004f), new Vector3(0.0006f, H * 0.8f, D), glass);
            b.AddBox(new Vector3(0f, H + 0.0008f, 0.004f), new Vector3(W + 0.004f, 0.0016f, D + 0.004f), frame);   // the roof
            b.AddBox(new Vector3(0f, 0.006f, 0.008f), new Vector3(W * 0.8f, 0.0014f, 0.005f), Pal.Hex("#9A6B42"));   // the bench
            // The sign at the kerb: TAXI, lit, on a post.
            b.AddCylinder(new Vector3(W / 2 + 0.006f, 0f, -0.012f), 0.0009f, 0.0009f, 0.034f, 6, frame, frame, true);
            Lettering.Board(b, "TAXI", new Vector3(W / 2 + 0.006f, 0.036f, -0.012f), Vector3.back, 0.0045f, Pal.Glow(yellow), Pal.TrainBlack, frame);
            b.AddBox(new Vector3(0f, 0.0004f, -0.004f), new Vector3(W + 0.016f, 0.0008f, 0.034f), Pal.Hex("#B9B4AA"));   // paving
            return 0.042f;
        }
    }
}
