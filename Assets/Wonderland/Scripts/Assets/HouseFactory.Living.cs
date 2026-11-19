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
            float zoo = ZooPiece(b, ref rng, id);
            if (zoo >= 0f) return zoo;
            switch (id)
            {
                case Catalog.TaxiRank: return TaxiShelter(b);
                case Catalog.Sawmill: return SawmillShed(b, ref rng);
                case Catalog.Letterbox: return PillarBox(b);
                case Catalog.CarPark: return CarParkBays(b);
                case Catalog.Zebra: return ZebraStripes(b);
                case Catalog.FreightYard: return FreightYardPad(b, ref rng);
                case Catalog.Harbour: return HarbourQuay(b, ref rng);
                case Catalog.Zoo: return ZooGrounds(b, ref rng);
                case Catalog.FairyCastle: return FairyCastleGrand(b, ref rng);
                case Catalog.Stadium: return StadiumBowl(b);
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
        /// A zebra crossing across a street (local X across it, Z along it): white bars on the asphalt, a hair above the
        /// road, and a black-and-white post with an orange globe on the pavement at each end.
        /// </summary>
        private static float ZebraStripes(MeshBuilder b)
        {
            const float lift = 0.0021f, half = 0.026f;
            for (float x = -half + 0.002f; x <= half - 0.002f + 1e-4f; x += 0.0075f)
                b.AddBox(new Vector3(x, lift, 0f), new Vector3(0.004f, 0.0003f, 0.018f), Pal.FlowerWhite);
            foreach (int s in new[] { -1, 1 })
            {
                var post = new Vector3(s * 0.037f, 0.0016f, 0.009f);
                for (int k = 0; k < 6; k++) b.AddCylinder(post + Vector3.up * k * 0.004f, 0.0007f, 0.0007f, 0.004f, 6, k % 2 == 0 ? Pal.TrainBlack : Pal.FlowerWhite, k % 2 == 0 ? Pal.TrainBlack : Pal.FlowerWhite, false);
                b.AddBlob(post + Vector3.up * 0.026f, Vector3.one * 0.0022f, 1, 0f, 3 + s, (_, __) => Pal.Glow(Pal.Hex("#F29A2E")));
            }
            return 0.03f;
        }

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

        /// <summary>
        /// A harbour quay (its front, -Z, to the water): a stone quay wall with bollards along its edge, the rail the quay
        /// crane rolls along, a warehouse with a big door, and a few containers stacked at the back. The crane and the
        /// containers being moved are drawn by the harbour system.
        /// </summary>
        private static float HarbourQuay(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.15f, D = 0.1f;
            var stone = Pal.Hex("#A9A49A");
            b.AddBox(new Vector3(0f, -0.004f, 0f), new Vector3(W, 0.012f, D), stone, Pal.Hex("#B9B4AA"), Pal.Hex("#8E8A82"));
            b.AddBox(new Vector3(0f, -0.012f, -D / 2 + 0.002f), new Vector3(W + 0.002f, 0.022f, 0.004f), Pal.StoneDark);   // the quay wall
            for (int k = 0; k < 5; k++)
            {
                var at = new Vector3(-W / 2 + 0.015f + k * (W - 0.03f) / 4f, 0.002f, -D / 2 + 0.005f);
                b.AddCylinder(at, 0.0018f, 0.0018f, 0.004f, 8, Pal.TrainBlack, Pal.TrainBlack, true);
                b.AddCylinder(at + Vector3.up * 0.004f, 0.0024f, 0.0024f, 0.001f, 8, Pal.TrainBlack, Pal.TrainBlack, true, true);
            }
            foreach (float z in new[] { CraneRailZ - 0.004f, CraneRailZ + 0.004f }) b.AddBox(new Vector3(0f, 0.0025f, z), new Vector3(W - 0.004f, 0.001f, 0.0012f), Pal.Hex("#5A5A62"));
            // A warehouse at the back.
            var shed = Pal.Hex("#5E7E9A");
            b.AddBox(new Vector3(W / 2 - 0.03f, 0.016f, D / 2 - 0.016f), new Vector3(0.05f, 0.032f, 0.028f), shed, Pal.Shade(shed, 1.1f), Pal.Shade(shed, 0.75f));
            b.AddBox(new Vector3(W / 2 - 0.03f, 0.0335f, D / 2 - 0.016f), new Vector3(0.054f, 0.003f, 0.032f), Pal.Hex("#3A3A40"));
            b.AddFace(new Vector3(W / 2 - 0.03f, 0.012f, D / 2 - 0.0301f), Vector3.back, Vector3.up, 0.022f, 0.022f, Pal.Hex("#3A4A5A"));
            // Containers stacked at the back corner.
            for (int k = 0; k < 4; k++) AddContainer(b, new Vector3(-W / 2 + 0.018f + (k % 2) * 0.026f, 0.002f + k / 2 * 0.0122f, D / 2 - 0.012f), k + 3);
            return 0.1f;
        }

        /// <summary>A ship moors this far out in front of a harbour (local z).</summary>
        public const float HarbourMooring = -0.085f;

        private static readonly Color[] ContainerColours = { Pal.Hex("#C8392E"), Pal.Hex("#2F5A8A"), Pal.Hex("#3E8A4A"), Pal.Hex("#E07B39"), Pal.Hex("#7A4A9A"), Pal.Hex("#D9C04A") };

        /// <summary>A shipping container (its bottom-centre at <paramref name="at"/>, lying along x), ribbed, in one of six colours.</summary>
        public static void AddContainer(MeshBuilder b, Vector3 at, int colour)
        {
            var c = ContainerColours[Mathf.Abs(colour) % ContainerColours.Length];
            b.AddBox(at + new Vector3(0f, 0.006f, 0f), new Vector3(0.024f, 0.012f, 0.011f), c, Pal.Shade(c, 1.12f), Pal.Shade(c, 0.75f));
            for (int r = -4; r <= 4; r++) b.AddBox(at + new Vector3(r * 0.0026f, 0.006f, 0f), new Vector3(0.0006f, 0.0115f, 0.0114f), Pal.Shade(c, 0.85f));
        }

        private static Mesh container, ship;
        private static readonly Mesh[] decks = new Mesh[9];

        /// <summary>One container on its own (bottom-centre at the origin): what the quay crane carries.</summary>
        public static Mesh ContainerMesh() { if (container != null) return container; var b = new MeshBuilder(); AddContainer(b, Vector3.zero, 1); return container = b.ToMesh("Container"); }

        /// <summary>
        /// A little container ship (bow +Z, the waterline at y = 0): a red and black hull, the white bridge aft with its
        /// funnel, and a deck for up to eight containers (see <see cref="ShipDeck"/>).
        /// </summary>
        private static readonly Mesh[] shipLevels = new Mesh[MeshBuilder.Levels], craneLevels = new Mesh[MeshBuilder.Levels];

        /// <summary>The container ship at a level of detail.</summary>
        public static Mesh ShipMesh(int detail) => detail == 0 ? ShipMesh() : shipLevels[detail + 1] ??= MeshBuilder.At(detail, 1f, () => { var was = ship; ship = null; try { return ShipMesh(); } finally { ship = was; } });

        /// <summary>The crane at a level of detail.</summary>
        public static Mesh CraneMesh(int detail) => detail == 0 ? CraneMesh() : craneLevels[detail + 1] ??= MeshBuilder.At(detail, 1f, () => { var was = crane; crane = null; try { return CraneMesh(); } finally { crane = was; } });

        public static Mesh ShipMesh()
        {
            if (ship != null) return ship;
            var b = new MeshBuilder();
            const float L = 0.16f, W = 0.034f;
            var red = Pal.Hex("#B8342A"); var black = Pal.TrainBlack; var white = Pal.Hex("#F2EFE8");
            b.AddBox(new Vector3(0f, -0.004f, -0.006f), new Vector3(W, 0.008f, L - 0.03f), red, red, Pal.Shade(red, 0.7f));   // below the waterline
            b.AddBox(new Vector3(0f, 0.006f, -0.006f), new Vector3(W, 0.012f, L - 0.03f), black, Pal.Shade(black, 1.4f), black);
            // The bow: a wedge.
            b.AddTriangleOutward(new Vector3(-W / 2, -0.008f, L / 2 - 0.021f), new Vector3(W / 2, -0.008f, L / 2 - 0.021f), new Vector3(0f, -0.008f, L / 2), new Vector3(0f, 0.01f, L / 2 - 0.03f), red);
            b.AddTriangleOutward(new Vector3(-W / 2, 0.012f, L / 2 - 0.021f), new Vector3(W / 2, 0.012f, L / 2 - 0.021f), new Vector3(0f, 0.014f, L / 2 + 0.002f), new Vector3(0f, -0.01f, L / 2 - 0.03f), Pal.Shade(black, 1.4f));
            foreach (int s in new[] { -1, 1 }) b.AddQuad(new Vector3(s * W / 2, -0.008f, L / 2 - 0.021f), new Vector3(s * W / 2, 0.012f, L / 2 - 0.021f), new Vector3(0f, 0.014f, L / 2 + 0.002f), new Vector3(0f, -0.008f, L / 2), s > 0 ? black : black);
            b.AddBox(new Vector3(0f, 0.0125f, -0.006f), new Vector3(W - 0.004f, 0.001f, L - 0.034f), Pal.Hex("#7A3A2A"));   // the deck
            // The bridge and funnel aft.
            b.AddBox(new Vector3(0f, 0.024f, -L / 2 + 0.02f), new Vector3(W - 0.006f, 0.022f, 0.018f), white, white, Pal.Shade(white, 0.8f));
            b.AddBox(new Vector3(0f, 0.031f, -L / 2 + 0.029f), new Vector3(W - 0.008f, 0.004f, 0.0008f), Pal.Glow(Pal.WindowGlow));
            b.AddBox(new Vector3(0f, 0.0365f, -L / 2 + 0.02f), new Vector3(W, 0.002f, 0.022f), white);
            b.AddCylinder(new Vector3(0f, 0.035f, -L / 2 + 0.012f), 0.004f, 0.0045f, 0.014f, 10, Pal.Hex("#2F5A8A"), Pal.Hex("#D9C04A"), true);
            return ship = b.ToMesh("Ship");
        }

        /// <summary>The containers on a ship's deck: up to eight, two across and four along.</summary>
        public static Mesh ShipDeck(int n)
        {
            n = Mathf.Clamp(n, 0, 8);
            if (decks[n] != null) return decks[n];
            var b = new MeshBuilder();
            for (int k = 0; k < n; k++)
            {
                b.Push(new Vector3((k % 2 - 0.5f) * 0.0125f, 0.013f, 0.04f - k / 2 * 0.026f), Quaternion.Euler(0f, 90f, 0f));
                AddContainer(b, Vector3.zero, k);
                b.Pop();
            }
            return decks[n] = b.ToMesh("Deck");
        }

        /// <summary>Where a container goes on a ship's deck, slot <paramref name="k"/> (local to the ship).</summary>
        public static Vector3 DeckSlot(int k) => new((k % 2 - 0.5f) * 0.0125f, 0.013f, 0.04f - k / 2 * 0.026f);

        // ---------------------------------------------------------------- attractions

        /// <summary>The zoo's enclosures (local centre and half size): giraffes, elephants, lions, zebras, and the penguin pool.</summary>
        public static readonly (Vector2 centre, Vector2 half)[] ZooPens =
        {
            (new Vector2(-0.07f, 0.04f), new Vector2(0.045f, 0.035f)), (new Vector2(0.03f, 0.045f), new Vector2(0.045f, 0.03f)),
            (new Vector2(-0.07f, -0.045f), new Vector2(0.04f, 0.025f)), (new Vector2(0.075f, -0.025f), new Vector2(0.035f, 0.035f)),
            (new Vector2(-0.005f, -0.035f), new Vector2(0.022f, 0.018f)),
        };

        /// <summary>
        /// The zoo: a gate with its name over it at the front, gravel paths between fenced enclosures (grass, a sandy lion
        /// enclosure, the penguins' pool with its rocks), a keeper's kiosk with a striped awning, and trees. The animals
        /// themselves move about (see AttractionSystem).
        /// </summary>
        private static float ZooGrounds(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.24f, D = 0.18f;
            b.AddBox(new Vector3(0f, 0.0006f, 0f), new Vector3(W, 0.0012f, D), Pal.Hex("#D8C8A0"));   // gravel
            var fence = Pal.Hex("#6B4630");
            for (int k = 0; k < ZooPens.Length; k++)
            {
                var (c, h) = ZooPens[k];
                var ground = k == 2 ? Pal.Hex("#E2C88A") : k == 4 ? Pal.Hex("#5A9AC8") : Pal.Hex("#7FB25A");
                b.AddBox(new Vector3(c.x, 0.0014f, c.y), new Vector3(h.x * 2f, 0.0012f, h.y * 2f), ground);
                if (k == 4) { for (int r = 0; r < 3; r++) b.AddBlob(new Vector3(c.x - 0.012f + r * 0.012f, 0.003f, c.y + 0.012f), new Vector3(0.006f, 0.004f, 0.005f), 1, 0.2f, 90 + r, (_, __) => Pal.Hex("#B8B4AA")); }
                // The fence round it: posts and two rails.
                foreach (int sx in new[] { -1, 1 })
                {
                    b.AddBox(new Vector3(c.x + sx * h.x, 0.006f, c.y), new Vector3(0.001f, 0.0008f, h.y * 2f), fence);
                    b.AddBox(new Vector3(c.x + sx * h.x, 0.0035f, c.y), new Vector3(0.001f, 0.0008f, h.y * 2f), fence);
                }
                foreach (int sz in new[] { -1, 1 })
                {
                    b.AddBox(new Vector3(c.x, 0.006f, c.y + sz * h.y), new Vector3(h.x * 2f, 0.0008f, 0.001f), fence);
                    b.AddBox(new Vector3(c.x, 0.0035f, c.y + sz * h.y), new Vector3(h.x * 2f, 0.0008f, 0.001f), fence);
                }
                for (float x = -h.x; x <= h.x + 1e-4f; x += h.x / 3f) foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(c.x + x, 0.0035f, c.y + sz * h.y), new Vector3(0.0012f, 0.007f, 0.0012f), fence);
            }
            // Trees in the giraffe and elephant enclosures, and round the edge.
            foreach (var (x, z) in new[] { (-0.1f, 0.06f), (0.06f, 0.06f), (-0.11f, -0.075f), (0.11f, 0.075f), (-0.11f, 0.08f) })
            {
                b.AddCylinder(new Vector3(x, 0f, z), 0.0012f, 0.0015f, 0.02f, 5, Pal.Bark, Pal.Bark, false);
                b.AddBlob(new Vector3(x, 0.024f, z), new Vector3(0.011f, 0.009f, 0.011f), 1, 0.25f, (int)(x * 100f + z * 37f), (_, __) => Pal.Hex("#5E9A4A"));
            }
            // The gate at the front, with ZOO over it.
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.016f, 0.016f, -D / 2 + 0.002f), new Vector3(0.006f, 0.032f, 0.006f), Pal.Hex("#8A5A34"));
            Lettering.Board(b, "ZOO", new Vector3(0f, 0.036f, -D / 2 + 0.002f), Vector3.back, 0.009f, Pal.Hex("#3E7A3A"), Pal.Hex("#F2E3A8"), Pal.Hex("#2A4A2A"));
            // The keeper's kiosk.
            b.AddBox(new Vector3(0.085f, 0.009f, -0.075f), new Vector3(0.026f, 0.018f, 0.018f), Pal.Hex("#F2E3C2"), Pal.Hex("#FFF2D8"), Pal.Hex("#C8B898"));
            Awning(b, new Vector3(0.085f, 0.017f, -0.084f), 0.026f, 0.008f, Pal.Hex("#3E7A3A"), Pal.Hex("#F7F1E6"));
            return 0.05f;
        }

        public const float PitchHalfX = 0.075f, PitchHalfZ = 0.048f, StandHeight = 0.03f;

        /// <summary>
        /// The football stadium: a green pitch with its white lines and goals, stepped stands all round (the crowd in
        /// them comes on match days - see AttractionSystem), a roof over the main stand, and floodlights at the corners.
        /// </summary>
        private static float StadiumBowl(MeshBuilder b)
        {
            var grass = Pal.Hex("#4E9A44"); var white = Pal.FlowerWhite; var seats = Pal.Hex("#C8392E"); var concrete = Pal.Hex("#B9B4AA");
            b.AddBox(new Vector3(0f, 0.001f, 0f), new Vector3(PitchHalfX * 2f + 0.012f, 0.002f, PitchHalfZ * 2f + 0.012f), grass);
            for (int k = 0; k < 8; k++) b.AddBox(new Vector3(-PitchHalfX + (k + 0.5f) * PitchHalfX / 4f, 0.0021f, 0f), new Vector3(PitchHalfX / 4f, 0.0002f, PitchHalfZ * 2f), k % 2 == 0 ? Pal.Shade(grass, 1.08f) : grass);
            void Line(Vector3 c, Vector3 size) => b.AddBox(c + Vector3.up * 0.0024f, size, white);
            Line(Vector3.zero, new Vector3(0.0008f, 0.0002f, PitchHalfZ * 2f));
            foreach (int s in new[] { -1, 1 })
            {
                Line(new Vector3(0f, 0f, s * PitchHalfZ), new Vector3(PitchHalfX * 2f, 0.0002f, 0.0008f));
                Line(new Vector3(s * PitchHalfX, 0f, 0f), new Vector3(0.0008f, 0.0002f, PitchHalfZ * 2f));
                Line(new Vector3(s * (PitchHalfX - 0.012f), 0f, 0f), new Vector3(0.0008f, 0.0002f, 0.04f));
                b.AddBox(new Vector3(s * (PitchHalfX + 0.002f), 0.005f, 0f), new Vector3(0.001f, 0.01f, 0.02f), white);   // the goals
            }
            b.AddCylinder(new Vector3(0f, 0.0023f, 0f), 0.012f, 0.012f, 0.0002f, 18, white, white, true, true);
            b.AddCylinder(new Vector3(0f, 0.0024f, 0f), 0.0112f, 0.0112f, 0.0002f, 18, grass, grass, true, true);
            // Three stepped rows of seats all round.
            for (int row = 0; row < 3; row++)
            {
                float gap = 0.012f + row * 0.01f, y = 0.004f + row * 0.008f;
                foreach (int s in new[] { -1, 1 })
                {
                    b.AddBox(new Vector3(0f, y, s * (PitchHalfZ + gap)), new Vector3(PitchHalfX * 2f + gap * 2f, 0.008f, 0.01f), row % 2 == 0 ? seats : Pal.Shade(seats, 0.85f), seats, concrete);
                    b.AddBox(new Vector3(s * (PitchHalfX + gap), y, 0f), new Vector3(0.01f, 0.008f, PitchHalfZ * 2f + gap * 2f), row % 2 == 0 ? seats : Pal.Shade(seats, 0.85f), seats, concrete);
                }
            }
            // The main stand's roof (at the back) and the floodlights.
            b.AddBox(new Vector3(0f, 0.04f, PitchHalfZ + 0.03f), new Vector3(PitchHalfX * 2f + 0.04f, 0.002f, 0.03f), Pal.Hex("#3A3A40"));
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * (PitchHalfX + 0.03f), 0.02f, PitchHalfZ + 0.04f), new Vector3(0.002f, 0.04f, 0.002f), Pal.Hex("#3A3A40"));
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                var at = new Vector3(sx * (PitchHalfX + 0.045f), 0f, sz * (PitchHalfZ + 0.045f));
                b.AddCylinder(at, 0.0012f, 0.0016f, 0.075f, 6, Pal.Hex("#5A5A62"), Pal.Hex("#5A5A62"), true);
                b.AddBox(at + new Vector3(0f, 0.077f, 0f), new Vector3(0.012f, 0.008f, 0.003f), Pal.Glow(Pal.Hex("#FFF6D8")));
            }
            Lettering.Board(b, "STADIUM", new Vector3(0f, 0.05f, -(PitchHalfZ + 0.046f)), Vector3.back, 0.008f, Pal.Hex("#2F5A8A"), Pal.FlowerWhite, Pal.Hex("#1E3A5A"));
            return 0.085f;
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
