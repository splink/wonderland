using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The zoo as a family of pieces that make a zoo together: the entrance with its great ZOO over the gate and ticket
    /// booths, a plaza, enclosures (the savanna, the lion rock, the penguin pool, the monkey island), animal houses (the
    /// elephant house, the aviary, the tropical house, the petting zoo), the kiosk and the safari café, palms and rocks.
    /// One style runs through them all - timber in warm brown, sandstone, thatch, a leafy green, the same railings and
    /// viewing decks - and every enclosure has its front (-Z) open to visitors behind a low wall, a moat or glass. The
    /// animals in the pens (see <see cref="PensOf"/>) live in AttractionSystem.
    /// </summary>
    public static partial class HouseFactory
    {
        public const int Giraffes = 0, Elephants = 1, Lions = 2, Zebras = 3, Penguins = 4, Monkeys = 5, Flamingos = 6, Goats = 7, Parrots = 8;

        /// <summary>A pen in a zoo piece: where (local), which animals and how many, the ground's height, and whether they fly or climb.</summary>
        public struct Pen
        {
            public Vector2 Centre, Half; public int Species, Count; public float Y; public bool Fly, Climb;
            public Pen(float x, float z, float hx, float hz, int species, int count, float y = 0.002f, bool fly = false, bool climb = false)
            { Centre = new Vector2(x, z); Half = new Vector2(hx, hz); Species = species; Count = count; Y = y; Fly = fly; Climb = climb; }
        }

        private static readonly Color ZooTimber = Pal.Hex("#7A4E2C"), ZooTimberLight = Pal.Hex("#A8743E"), ZooSand = Pal.Hex("#E2CB92"), ZooStone = Pal.Hex("#D9C29A"),
            ZooLeaf = Pal.Hex("#4E8A3E"), ZooThatch = Pal.Hex("#C99A48"), ZooGreen = Pal.Hex("#3E7A3A"), ZooWater = Pal.Hex("#4F9BC8"), ZooPath = Pal.Hex("#D8C8A0");

        /// <summary>The animals' pens in a zoo piece (none for anything else).</summary>
        public static Pen[] PensOf(int id) => id switch
        {
            Catalog.Zoo => new[]
            {
                new Pen(-0.07f, 0.04f, 0.045f, 0.035f, Giraffes, 2), new Pen(0.03f, 0.045f, 0.045f, 0.03f, Elephants, 2), new Pen(-0.07f, -0.045f, 0.04f, 0.025f, Lions, 2),
                new Pen(0.075f, -0.025f, 0.035f, 0.035f, Zebras, 3), new Pen(-0.005f, -0.035f, 0.022f, 0.018f, Penguins, 4),
            },
            Catalog.Savanna => new[] { new Pen(-0.03f, 0.01f, 0.08f, 0.06f, Giraffes, 3), new Pen(0.04f, 0.0f, 0.07f, 0.06f, Zebras, 4), new Pen(0.07f, 0.045f, 0.03f, 0.02f, Flamingos, 4) },
            Catalog.ElephantHouse => new[] { new Pen(0f, -0.02f, 0.09f, 0.045f, Elephants, 3) },
            Catalog.LionRock => new[] { new Pen(0f, 0.005f, 0.075f, 0.05f, Lions, 3) },
            Catalog.PenguinPool => new[] { new Pen(0f, 0.015f, 0.065f, 0.035f, Penguins, 7) },
            Catalog.MonkeyIsland => new[] { new Pen(0f, 0.005f, 0.05f, 0.04f, Monkeys, 5, 0.006f, false, true) },
            Catalog.Aviary => new[] { new Pen(0f, 0f, 0.045f, 0.045f, Parrots, 7, 0.05f, true) },
            Catalog.TropicalHouse => new[] { new Pen(0f, 0.01f, 0.04f, 0.03f, Parrots, 3, 0.045f, true) },
            Catalog.PettingZoo => new[] { new Pen(-0.012f, -0.005f, 0.045f, 0.035f, Goats, 5) },
            _ => System.Array.Empty<Pen>(),
        };

        // ---------------------------------------------------------------- shared zoo parts

        /// <summary>The visitors' side of an enclosure: a low sandstone wall with a timber rail along the front (-Z).</summary>
        private static void ViewingWall(MeshBuilder b, float halfX, float z)
        {
            b.AddBox(new Vector3(0f, 0.0045f, z), new Vector3(halfX * 2f, 0.009f, 0.005f), ZooStone, Pal.Shade(ZooStone, 1.08f), ZooStone);
            b.AddBox(new Vector3(0f, 0.0105f, z), new Vector3(halfX * 2f, 0.0012f, 0.0016f), ZooTimber);
            if (b.TooSmall(0.002f)) return;
            for (float x = -halfX; x <= halfX + 1e-4f; x += 0.012f) b.AddBox(new Vector3(x, 0.0095f, z), new Vector3(0.0012f, 0.003f, 0.0012f), ZooTimber);
        }

        /// <summary>A timber post-and-rail fence along a side.</summary>
        private static void ZooFence(MeshBuilder b, Vector3 a, Vector3 c, float h = 0.012f)
        {
            var d = c - a; float len = d.magnitude;
            b.Push((a + c) / 2f, Quaternion.LookRotation(d / len, Vector3.up));
            foreach (float y in new[] { h * 0.45f, h * 0.9f }) b.AddBox(new Vector3(0f, y, 0f), new Vector3(0.001f, 0.0012f, len), ZooTimberLight);
            b.Pop();
            if (b.TooSmall(0.002f)) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 0.014f));
            for (int k = 0; k <= n; k++) b.AddBox(Vector3.Lerp(a, c, k / (float)n) + Vector3.up * h / 2, new Vector3(0.0016f, h, 0.0016f), ZooTimber);
        }

        /// <summary>An acacia: a leaning trunk and a wide flat crown.</summary>
        private static void Acacia(MeshBuilder b, Vector3 at, float scale, int seed)
        {
            b.AddCylinder(at, 0.0016f * scale, 0.0011f * scale, 0.026f * scale, 6, Pal.Bark, Pal.Bark, false);
            b.AddBlob(at + new Vector3(0.002f, 0.028f, 0f) * scale, new Vector3(0.017f, 0.0045f, 0.014f) * scale, 1, 0.25f, seed, (_, __) => Pal.Hex("#6F9A44"));
        }

        /// <summary>A palm: a curving trunk of rings and a crown of drooping fronds.</summary>
        private static void Palm(MeshBuilder b, Vector3 at, float h, int seed)
        {
            var top = at;
            for (int k = 0; k < 6; k++)
            {
                var next = at + new Vector3(Mathf.Sin(k * 0.3f + seed) * 0.002f, h * (k + 1) / 6f, 0f);
                b.AddCylinder(top, 0.0016f - k * 0.0001f, 0.0014f - k * 0.0001f, (next - top).magnitude, 6, Pal.Hex("#8A6A44"), Pal.Hex("#6E5236"), false);
                top = next;
            }
            for (int f = 0; f < 7; f++)
            {
                b.Push(top, Quaternion.Euler(30f, f * 360f / 7f + seed * 17f, 0f));
                b.AddBox(new Vector3(0f, 0f, 0.008f), new Vector3(0.004f, 0.0008f, 0.016f), Pal.Shade(ZooLeaf, 0.9f + 0.05f * (f % 3)));
                b.Pop();
            }
        }

        private static void Rock(MeshBuilder b, Vector3 at, Vector3 size, int seed, Color colour) =>
            b.AddBlob(at, size, 1, 0.3f, seed, (d, _) => Pal.Shade(colour, 0.85f + 0.2f * (d.y * 0.5f + 0.5f)));

        // ---------------------------------------------------------------- the pieces

        /// <summary>
        /// The entrance, unmistakably a zoo from across the world: two rocky towers overgrown with jungle and capped with
        /// thatch, a great golden sign between them with ZOO in big green letters and a lion standing on top, zebra-striped
        /// and leopard-spotted banners hanging from the towers, a giraffe and an elephant twice life size (for the
        /// miniature) on striped plinths either side, the gates folded open, turnstiles, ticket booths and flags.
        /// </summary>
        private static float ZooGateway(MeshBuilder b, ref Rng rng)
        {
            var gold = Pal.Hex("#F2C12E"); var red = Pal.Hex("#C8402E"); var green = Pal.Hex("#1E6B2A");
            var rock = Pal.Hex("#C9A77A"); var black = Pal.TrainBlack; var white = Pal.FlowerWhite;
            b.AddBox(new Vector3(0f, 0.0006f, 0f), new Vector3(0.27f, 0.0012f, 0.09f), ZooPath);
            // The way in, paved in a darker band through the gate.
            b.AddBox(new Vector3(0f, 0.0013f, -0.01f), new Vector3(0.05f, 0.0003f, 0.07f), Pal.Shade(ZooPath, 0.9f));
            foreach (int sx in new[] { -1, 1 })
            {
                // A rocky tower: stacked boulders round a core, jungle tufts in the cracks, a thatched cap.
                var tower = new Vector3(sx * 0.05f, 0f, 0f);
                b.AddBox(tower + Vector3.up * 0.055f, new Vector3(0.022f, 0.11f, 0.022f), Pal.Shade(rock, 0.9f));
                for (int k = 0; k < (b.TooSmall(0.004f) ? 3 : 9); k++)
                {
                    float y = 0.008f + k * 0.0115f;
                    var off = new Vector3(rng.Range(-0.004f, 0.004f), 0f, rng.Range(-0.004f, 0.004f));
                    b.AddBlob(tower + off + Vector3.up * y, new Vector3(0.0155f, 0.009f, 0.0155f) * rng.Range(0.92f, 1.08f), 1, 0.25f, k * 7 + sx + 20, (d, _) => Pal.Shade(rock, 0.82f + 0.25f * Mathf.Abs(d.y)));
                    if (k % 3 == 1 && !b.TooSmall(0.004f)) b.AddBlob(tower + new Vector3(sx * 0.012f, y + 0.004f, -0.008f), new Vector3(0.006f, 0.004f, 0.005f), 1, 0.3f, k + 40, (_, __) => ZooLeaf);
                }
                b.AddCylinder(tower + Vector3.up * 0.108f, 0.021f, 0.002f, 0.026f, 12, ZooThatch, Pal.Shade(ZooThatch, 1.15f), true, false, true, 0.3f);
                b.AddCylinder(tower + Vector3.up * 0.13f, 0.0006f, 0.0006f, 0.025f, 5, Pal.Hex("#E8E2D6"), Pal.Hex("#E8E2D6"), false);   // a flag on top
                b.AddBox(tower + new Vector3(sx * 0.006f, 0.151f, 0f), new Vector3(0.012f, 0.008f, 0.0005f), sx < 0 ? red : gold);
                // Banners hanging down the tower's front: zebra stripes on one side, leopard spots on the other.
                var banner = tower + new Vector3(0f, 0.052f, -0.0175f);
                b.AddBox(banner + Vector3.up * 0.025f, new Vector3(0.016f, 0.0015f, 0.0015f), ZooTimber);
                int bands = b.TooSmall(0.003f) ? 1 : 8;
                for (int k = 0; k < bands; k++)
                {
                    float h = 0.046f / bands, y = 0.023f - (k + 0.5f) * h;
                    var c = sx < 0 ? (k % 2 == 0 ? white : black) : Pal.Hex("#E3A94A");
                    b.AddBox(banner + Vector3.up * y, new Vector3(0.013f, h, 0.0008f), c);
                    if (sx > 0 && bands > 1) b.AddBox(banner + new Vector3(((k * 5) % 3 - 1) * 0.0035f, y, -0.0005f), new Vector3(0.0028f, 0.0024f, 0.0003f), Pal.Hex("#5A3A1E"));
                }
                b.AddBox(banner + Vector3.down * 0.025f, new Vector3(0.013f, 0.003f, 0.0008f), red);   // (a red fringe)
                // The gate leaves, folded open.
                b.Push(new Vector3(sx * 0.037f, 0.016f, -0.008f), Quaternion.Euler(0f, sx * 70f, 0f));
                b.AddBox(new Vector3(sx * 0.0005f, 0f, -0.011f), new Vector3(0.0014f, 0.03f, 0.022f), Pal.Hex("#2E5A3A"));
                if (!b.TooSmall(0.003f)) b.AddBox(new Vector3(sx * 0.0005f, 0.0155f, -0.011f), new Vector3(0.0018f, 0.002f, 0.023f), gold);
                b.Pop();
                // The ticket booth: a timber hut under a thatched roof, its window to the front.
                var booth = new Vector3(sx * 0.092f, 0f, 0.008f);
                b.AddBox(booth + Vector3.up * 0.014f, new Vector3(0.034f, 0.028f, 0.03f), ZooTimberLight, ZooTimberLight, ZooTimberLight);
                b.AddFace(booth + new Vector3(0f, 0.017f, -0.0152f), Vector3.back, Vector3.up, 0.01f, 0.006f, Pal.Hex("#2C4A6A"));
                b.AddBox(booth + new Vector3(0f, 0.0105f, -0.017f), new Vector3(0.022f, 0.0012f, 0.004f), ZooTimber);   // the counter
                Lettering.Board(b, "TICKETS", booth + new Vector3(0f, 0.026f, -0.0153f), Vector3.back, 0.003f, green, gold, ZooTimber);
                b.AddCylinder(booth + Vector3.up * 0.028f, 0.027f, 0.002f, 0.022f, 10, ZooThatch, Pal.Shade(ZooThatch, 1.15f), true, false, true, 0.3f);
                // Turnstiles between the booth and the gate.
                if (!b.TooSmall(0.003f)) b.AddCylinder(new Vector3(sx * 0.068f, 0f, -0.02f), 0.0015f, 0.0015f, 0.009f, 6, Pal.Hex("#8A8F96"), Pal.Hex("#8A8F96"), true);
                // A statue on a zebra-striped plinth: the giraffe on the left, the elephant (trunk raised) on the right.
                var plinth = new Vector3(sx * 0.122f, 0f, -0.012f);
                for (int k = 0; k < 4; k++) b.AddBox(plinth + Vector3.up * (0.0025f + k * 0.005f), new Vector3(0.026f, 0.005f, 0.03f), k % 2 == 0 ? white : black);
                var statue = sx < 0 ? AttractionSystem.Giraffe() : AttractionSystem.Elephant();
                float size = sx < 0 ? 3.1f : 3.4f;
                b.AddMesh(statue, Matrix4x4.TRS(plinth + Vector3.up * 0.02f, Quaternion.Euler(0f, sx < 0 ? 200f : 160f, 0f), Vector3.one * size));
                // Planters with palms in front of the towers.
                b.AddBox(new Vector3(sx * 0.068f, 0.004f, -0.034f), new Vector3(0.012f, 0.008f, 0.012f), ZooStone);
                Palm(b, new Vector3(sx * 0.068f, 0.008f, -0.034f), 0.034f, sx + 3);
            }
            // The arch: a timber beam between the towers, and the great sign hung from it - gold, edged in red, ZOO in big
            // raised green letters front and back - with a lion standing on top.
            b.AddBox(new Vector3(0f, 0.104f, 0f), new Vector3(0.1f, 0.008f, 0.012f), ZooTimber, ZooTimberLight, ZooTimber);
            b.AddBox(new Vector3(0f, 0.083f, 0f), new Vector3(0.092f, 0.034f, 0.006f), red);
            b.AddBox(new Vector3(0f, 0.083f, 0f), new Vector3(0.086f, 0.028f, 0.0072f), gold);
            foreach (var f in new[] { Vector3.back, Vector3.forward })
                Lettering.Add(b, "ZOO", new Vector3(0f, 0.083f, 0f) + f * 0.0037f, f, 0.02f, 0.003f, green);
            b.AddMesh(AttractionSystem.Lion(), Matrix4x4.TRS(new Vector3(0f, 0.108f, 0f), Quaternion.Euler(0f, 180f, 0f), Vector3.one * 2.6f));
            // Zebra-striped posts along the front, carrying the eye out to the statues.
            if (!b.TooSmall(0.003f))
                foreach (float x in new[] { -0.105f, -0.03f, 0.03f, 0.105f })
                    for (int k = 0; k < 3; k++) b.AddBox(new Vector3(x, 0.002f + k * 0.004f, -0.042f), new Vector3(0.003f, 0.004f, 0.003f), k % 2 == 0 ? white : black);
            return 0.16f;
        }

        /// <summary>The plaza: a paved circle with a little elephant fountain, benches round it, lamps, planters and a signpost.</summary>
        private static float ZooPlazaPaving(MeshBuilder b, ref Rng rng)
        {
            b.AddCylinder(Vector3.zero, 0.075f, 0.075f, 0.0014f, 32, ZooPath, ZooPath, true);
            if (!b.TooSmall(0.003f))
                for (int ring = 1; ring <= 3; ring++) b.AddCylinder(new Vector3(0f, 0.0014f, 0f), ring * 0.02f + 0.001f, ring * 0.02f + 0.001f, 0.0002f, 32, Pal.Shade(ZooPath, 0.88f), Pal.Shade(ZooPath, 0.88f), false);
            b.AddCylinder(new Vector3(0f, 0.0014f, 0f), 0.016f, 0.016f, 0.004f, 20, ZooStone, ZooWater, true);
            var grey = Pal.Hex("#8E949C");
            b.AddBlob(new Vector3(0f, 0.013f, 0f), new Vector3(0.007f, 0.006f, 0.005f), 1, 0f, 8, (_, __) => grey);   // the elephant statue
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.0035f, 0.007f, 0f), new Vector3(0.002f, 0.007f, 0.004f), grey);
            b.Push(new Vector3(0f, 0.015f, -0.006f), Quaternion.Euler(-40f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0013f, 0.0008f, 0.009f, 6, grey, grey, true);   // its trunk raised, spouting
            b.Pop();
            b.AddCylinder(new Vector3(0f, 0.013f, -0.012f), 0.0012f, 0f, 0.008f, 6, Pal.Hex("#BFE2F4"), Pal.Hex("#E8F6FC"), false);
            for (int k = 0; k < 6; k++)
            {
                float a = (k + 0.5f) / 6f * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var at = dir * 0.05f;
                b.Push(at, Quaternion.LookRotation(-dir, Vector3.up));
                if (k % 2 == 0) { b.AddBox(new Vector3(0f, 0.004f, 0f), new Vector3(0.018f, 0.0012f, 0.006f), ZooTimberLight); b.AddBox(new Vector3(0f, 0.007f, 0.0028f), new Vector3(0.018f, 0.005f, 0.001f), ZooTimberLight); }
                else { b.AddBox(new Vector3(0f, 0.004f, 0f), new Vector3(0.012f, 0.008f, 0.012f), ZooStone); b.AddBlob(new Vector3(0f, 0.011f, 0f), new Vector3(0.007f, 0.005f, 0.007f), 1, 0.2f, k, (_, __) => ZooLeaf); }
                b.Pop();
                if (k % 2 == 1) StreetLampAt(b, dir * 0.068f);
            }
            // The signpost: arrows to the animals.
            b.AddCylinder(new Vector3(0.03f, 0f, -0.03f), 0.0008f, 0.0008f, 0.03f, 6, ZooTimber, ZooTimber, true);
            foreach (var (y, yaw, c) in new[] { (0.026f, 20f, Pal.Hex("#E8B04A")), (0.021f, 140f, Pal.Hex("#8E949C")), (0.016f, -100f, Pal.Hex("#4F9BC8")) })
            {
                b.Push(new Vector3(0.03f, y, -0.03f), Quaternion.Euler(0f, yaw, 0f));
                b.AddBox(new Vector3(0.006f, 0f, 0f), new Vector3(0.012f, 0.003f, 0.0008f), c);
                b.Pop();
            }
            return 0.04f;
        }

        private static void StreetLampAt(MeshBuilder b, Vector3 at)
        {
            b.AddCylinder(at, 0.0008f, 0.0006f, 0.032f, 5, Pal.Hex("#2E3238"), Pal.Hex("#2E3238"), false);
            b.AddBlob(at + Vector3.up * 0.033f, new Vector3(0.0025f, 0.002f, 0.0025f), 1, 0f, 4, (_, __) => Pal.Glow(Pal.Hex("#FFE9B0")));
        }

        /// <summary>
        /// The savanna: dry golden grass, acacias, a termite mound, rocks and a waterhole for the flamingos; giraffes and
        /// zebras roam it. A dry moat and a low wall along the front, and a raised timber viewing deck.
        /// </summary>
        private static float SavannaPlain(MeshBuilder b, ref Rng rng)
        {
            const float hx = 0.125f, hz = 0.095f;
            b.AddBox(new Vector3(0f, 0.001f, 0f), new Vector3(hx * 2f, 0.002f, hz * 2f), Pal.Hex("#C9B26A"));
            if (!b.TooSmall(0.003f))
                for (int k = 0; k < 40; k++) b.AddBox(new Vector3(rng.Range(-hx, hx) * 0.95f, 0.0021f, rng.Range(-hz + 0.02f, hz) * 0.95f), new Vector3(rng.Range(0.01f, 0.03f), 0.0003f, rng.Range(0.006f, 0.02f)), Pal.Shade(Pal.Hex("#C9B26A"), rng.Range(0.85f, 1.12f)));
            b.AddCylinder(new Vector3(0.07f, 0.0006f, 0.045f), 0.03f, 0.028f, 0.0018f, 20, Pal.Hex("#8A7A52"), ZooWater, true);   // the waterhole
            foreach (var (x, z, s, seed) in new[] { (-0.08f, 0.06f, 1.2f, 1), (-0.02f, 0.07f, 1f, 2), (0.1f, -0.02f, 0.9f, 3), (-0.1f, -0.03f, 0.8f, 4) }) Acacia(b, new Vector3(x, 0.002f, z), s * 1.3f, seed);
            b.AddCylinder(new Vector3(0.03f, 0.002f, 0.06f), 0.006f, 0.0015f, 0.022f, 8, Pal.Hex("#A8743E"), Pal.Hex("#B88450"), true);   // a termite mound
            Rock(b, new Vector3(-0.04f, 0.004f, -0.03f), new Vector3(0.012f, 0.008f, 0.009f), 11, Pal.Hex("#B8A888"));
            Rock(b, new Vector3(0.05f, 0.003f, 0.08f), new Vector3(0.015f, 0.007f, 0.01f), 12, Pal.Hex("#B8A888"));
            // The front: a dry moat (a darker strip) and the low wall; the back and sides fenced.
            b.AddBox(new Vector3(0f, 0.0022f, -hz + 0.012f), new Vector3(hx * 2f, 0.0003f, 0.01f), Pal.Hex("#8A7A52"));
            ViewingWall(b, hx, -hz + 0.003f);
            ZooFence(b, new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz), 0.016f); ZooFence(b, new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), 0.016f);
            ZooFence(b, new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), 0.016f);
            // The viewing deck at the front left.
            var deck = new Vector3(-0.07f, 0f, -hz - 0.004f);
            b.AddBox(deck + Vector3.up * 0.012f, new Vector3(0.05f, 0.002f, 0.018f), ZooTimberLight);
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.AddBox(deck + new Vector3(sx * 0.023f, 0.006f, sz * 0.007f), new Vector3(0.0018f, 0.012f, 0.0018f), ZooTimber);
            b.AddBox(deck + new Vector3(0f, 0.017f, 0.008f), new Vector3(0.05f, 0.001f, 0.001f), ZooTimber);
            Lettering.Board(b, "SAVANNA", new Vector3(0.05f, 0.016f, -hz - 0.002f), Vector3.back, 0.004f, ZooGreen, Pal.Hex("#F2E3A8"), ZooTimber);
            return 0.05f;
        }

        /// <summary>The elephant house: a big sandstone hall with tall arched doors and a domed roof, a yard with a pool and mud bath in front of it.</summary>
        private static float ElephantHall(MeshBuilder b, ref Rng rng)
        {
            const float hx = 0.115f, hz = 0.095f;
            b.AddBox(new Vector3(0f, 0.001f, 0f), new Vector3(hx * 2f, 0.002f, hz * 2f), Pal.Hex("#B89A6A"));
            b.AddCylinder(new Vector3(-0.05f, 0.0006f, -0.02f), 0.025f, 0.024f, 0.0018f, 20, Pal.Hex("#7A6A4A"), ZooWater, true);   // the pool
            b.AddCylinder(new Vector3(0.055f, 0.0006f, -0.03f), 0.018f, 0.017f, 0.0019f, 16, Pal.Hex("#6E5236"), Pal.Hex("#7A5A3A"), true);   // the mud bath
            Rock(b, new Vector3(0.0f, 0.004f, -0.045f), new Vector3(0.01f, 0.007f, 0.008f), 21, Pal.Hex("#A89878"));
            Acacia(b, new Vector3(0.09f, 0.002f, 0.01f), 1.4f, 9);
            // The house at the back.
            var hall = new Vector3(0f, 0f, 0.06f);
            b.AddBox(hall + Vector3.up * 0.025f, new Vector3(0.15f, 0.05f, 0.05f), ZooStone, ZooStone, ZooStone);
            b.AddBox(hall + Vector3.up * 0.051f, new Vector3(0.156f, 0.003f, 0.056f), ZooTimber);
            b.Push(hall + Vector3.up * 0.052f, Quaternion.identity, new Vector3(0.94f, 0.55f, 0.32f));
            b.AddBlob(Vector3.zero, Vector3.one * 0.075f, 2, 0f, 3, (d, _) => d.y > 0f ? Pal.Shade(Pal.Hex("#4E8A6E"), 0.9f + d.y * 0.2f) : ZooStone);
            b.Pop();
            b.AddCylinder(hall + Vector3.up * 0.09f, 0.004f, 0.001f, 0.012f, 8, Pal.Brass, Pal.Brass, true);
            for (int k = -2; k <= 2; k++)
            {
                bool door = k == 0 || Mathf.Abs(k) == 2;
                float x = k * 0.03f;
                ArchedOpening(b, hall + new Vector3(x, 0.0f, -0.0252f), Vector3.back, door ? 0.009f : 0.005f, door ? 0.034f : 0.016f, door ? Pal.Hex("#3A2A1E") : CastleGlass);
            }
            Lettering.Board(b, "ELEPHANTS", hall + new Vector3(0f, 0.045f, -0.026f), Vector3.back, 0.0045f, ZooGreen, Pal.Hex("#F2E3A8"), ZooTimber);
            // High windows round the back and sides, pilasters between them.
            Windows(b, hall + new Vector3(0f, 0.03f, 0.0252f), Vector3.forward, Vector3.left, 5, 0.12f, 0.005f, 0.014f);
            foreach (int sx in new[] { -1, 1 }) Windows(b, hall + new Vector3(sx * 0.0752f, 0.03f, 0f), sx > 0 ? Vector3.right : Vector3.left, sx > 0 ? Vector3.forward : Vector3.back, 2, 0.025f, 0.005f, 0.014f);
            for (int k = -3; k <= 3; k++) foreach (int sz in new[] { -1, 1 }) b.AddBox(hall + new Vector3(k * 0.024f, 0.025f, sz * 0.0255f), new Vector3(0.004f, 0.05f, 0.002f), Pal.Shade(ZooStone, 0.9f));
            ViewingWall(b, hx, -hz + 0.003f);
            ZooFence(b, new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, 0.035f), 0.018f); ZooFence(b, new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, 0.035f), 0.018f);
            return 0.09f;
        }

        /// <summary>The lion rock: a towering outcrop with a flat ledge for the lions to lie on, dry grass, a moat and glass at the front.</summary>
        private static float LionsRock(MeshBuilder b, ref Rng rng)
        {
            const float hx = 0.095f, hz = 0.08f;
            b.AddBox(new Vector3(0f, 0.001f, 0f), new Vector3(hx * 2f, 0.002f, hz * 2f), Pal.Hex("#C7B27A"));
            var rock = Pal.Hex("#B39A72");
            Rock(b, new Vector3(0.02f, 0.016f, 0.04f), new Vector3(0.05f, 0.03f, 0.03f), 31, rock);
            Rock(b, new Vector3(-0.045f, 0.008f, 0.05f), new Vector3(0.03f, 0.018f, 0.022f), 32, rock);
            b.Push(new Vector3(0.0f, 0.036f, 0.02f), Quaternion.Euler(0f, -10f, 4f));
            b.AddBox(Vector3.zero, new Vector3(0.05f, 0.006f, 0.03f), Pal.Shade(rock, 1.08f));   // the ledge
            b.Pop();
            Acacia(b, new Vector3(-0.07f, 0.002f, -0.01f), 1.2f, 33);
            b.AddBox(new Vector3(0f, 0.0006f, -hz + 0.01f), new Vector3(hx * 2f, 0.0012f, 0.012f), ZooWater);   // the moat
            b.AddBox(new Vector3(0f, 0.009f, -hz + 0.002f), new Vector3(hx * 2f, 0.018f, 0.0012f), Pal.Hex("#BFE2F4"));   // glass
            b.AddBox(new Vector3(0f, 0.0185f, -hz + 0.002f), new Vector3(hx * 2f, 0.0015f, 0.0022f), ZooTimber);
            ZooFence(b, new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz), 0.02f); ZooFence(b, new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), 0.02f);
            ZooFence(b, new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), 0.02f);
            Lettering.Board(b, "LIONS", new Vector3(-0.06f, 0.022f, -hz - 0.001f), Vector3.back, 0.004f, ZooGreen, Pal.Hex("#F2E3A8"), ZooTimber);
            return 0.06f;
        }

        /// <summary>The penguin pool: blue water, white rocks and an iceberg, a little slide, and glass at the front.</summary>
        private static float PenguinWater(MeshBuilder b, ref Rng rng)
        {
            const float hx = 0.085f, hz = 0.065f;
            b.AddBox(new Vector3(0f, 0.001f, 0f), new Vector3(hx * 2f, 0.002f, hz * 2f), Pal.Hex("#E8EEF2"));
            b.AddBox(new Vector3(0f, 0.0018f, -0.02f), new Vector3(hx * 1.7f, 0.0012f, 0.05f), Pal.Hex("#3E8FC2"));
            Rock(b, new Vector3(0.045f, 0.012f, 0.012f), new Vector3(0.022f, 0.016f, 0.014f), 41, Pal.Hex("#F2F6FA"));   // the iceberg
            Rock(b, new Vector3(-0.05f, 0.006f, 0.03f), new Vector3(0.02f, 0.008f, 0.012f), 42, Pal.Hex("#DDE6EC"));
            b.Push(new Vector3(0.03f, 0.014f, 0.0f), Quaternion.Euler(-25f, 0f, 0f));
            b.AddBox(Vector3.zero, new Vector3(0.006f, 0.0008f, 0.03f), Pal.Hex("#7FC4E8"));   // the slide
            b.Pop();
            b.AddBox(new Vector3(0f, 0.009f, -hz + 0.002f), new Vector3(hx * 2f, 0.018f, 0.0012f), Pal.Hex("#BFE2F4"));
            b.AddBox(new Vector3(0f, 0.0185f, -hz + 0.002f), new Vector3(hx * 2f, 0.0015f, 0.0022f), Pal.Hex("#F2F6FA"));
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * hx, 0.009f, 0f), new Vector3(0.003f, 0.018f, hz * 2f), Pal.Hex("#DDE6EC"));
            b.AddBox(new Vector3(0f, 0.009f, hz), new Vector3(hx * 2f, 0.018f, 0.003f), Pal.Hex("#DDE6EC"));
            Lettering.Board(b, "PENGUINS", new Vector3(0f, 0.025f, -hz), Vector3.back, 0.004f, Pal.Hex("#2C4A6A"), Pal.Hex("#F2F6FA"), Pal.Hex("#DDE6EC"));
            return 0.04f;
        }

        /// <summary>The monkey island: an island of trees, rocks and a climbing frame of ropes and platforms, ringed by a moat.</summary>
        private static float MonkeyIsle(MeshBuilder b, ref Rng rng)
        {
            const float hx = 0.095f, hz = 0.085f;
            b.AddBox(new Vector3(0f, 0.001f, 0f), new Vector3(hx * 2f, 0.002f, hz * 2f), Pal.Hex("#7FB25A"));
            b.AddCylinder(new Vector3(0f, 0.0006f, 0.005f), 0.08f, 0.078f, 0.0018f, 28, Pal.Hex("#6E8A5A"), ZooWater, true);   // the moat
            b.AddCylinder(new Vector3(0f, 0.0006f, 0.005f), 0.058f, 0.054f, 0.006f, 24, Pal.Hex("#9A8A62"), Pal.Hex("#6FA04A"), true);   // the island
            Palm(b, new Vector3(-0.025f, 0.006f, 0.03f), 0.05f, 5);
            Palm(b, new Vector3(0.03f, 0.006f, 0.025f), 0.045f, 6);
            Rock(b, new Vector3(-0.03f, 0.009f, -0.02f), new Vector3(0.012f, 0.008f, 0.01f), 51, Pal.Hex("#A89878"));
            // The climbing frame: posts, two platforms and ropes between them.
            foreach (var (x, z, h) in new[] { (0.0f, 0.0f, 0.04f), (0.022f, -0.012f, 0.03f), (-0.01f, 0.018f, 0.032f) })
            {
                b.AddCylinder(new Vector3(x, 0.006f, z), 0.0012f, 0.0012f, h, 6, ZooTimber, ZooTimber, true);
                b.AddBox(new Vector3(x, 0.006f + h * 0.8f, z), new Vector3(0.014f, 0.0015f, 0.014f), ZooTimberLight);
            }
            if (!b.TooSmall(0.002f))
                foreach (var (a, c) in new[] { (new Vector3(0f, 0.04f, 0f), new Vector3(0.022f, 0.03f, -0.012f)), (new Vector3(0f, 0.04f, 0f), new Vector3(-0.01f, 0.032f, 0.018f)), (new Vector3(0.022f, 0.03f, -0.012f), new Vector3(0.03f, 0.04f, 0.025f)) })
                {
                    var d = c - a;
                    b.Push((a + c) / 2f, Quaternion.LookRotation(d.normalized, Vector3.up));
                    b.AddBox(Vector3.zero, new Vector3(0.0006f, 0.0006f, d.magnitude), Pal.Hex("#C8A878"));
                    b.Pop();
                }
            ViewingWall(b, hx, -hz + 0.003f);
            Lettering.Board(b, "MONKEYS", new Vector3(0.06f, 0.016f, -hz), Vector3.back, 0.004f, ZooGreen, Pal.Hex("#F2E3A8"), ZooTimber);
            return 0.055f;
        }

        /// <summary>The aviary: a tall dome of fine green ribs and netting over trees and a pond, the birds flying inside.</summary>
        private static float AviaryDome(MeshBuilder b, ref Rng rng)
        {
            b.AddCylinder(Vector3.zero, 0.065f, 0.065f, 0.002f, 28, ZooStone, Pal.Hex("#7FB25A"), true);
            b.AddCylinder(new Vector3(0.02f, 0.0018f, 0.015f), 0.016f, 0.015f, 0.0006f, 16, ZooWater, ZooWater, true);
            Palm(b, new Vector3(-0.02f, 0.002f, 0.01f), 0.055f, 7);
            b.AddCylinder(new Vector3(0.025f, 0.002f, -0.02f), 0.0018f, 0.0012f, 0.03f, 6, Pal.Bark, Pal.Bark, false);
            b.AddBlob(new Vector3(0.025f, 0.035f, -0.02f), new Vector3(0.014f, 0.012f, 0.014f), 1, 0.25f, 71, (_, __) => ZooLeaf);
            // The ribs of the dome, and a faint net between them.
            var rib = Pal.Hex("#2E5A3A");
            const float r = 0.062f, h = 0.085f;
            int ribs = b.Detail >= MeshBuilder.Low ? 6 : 12;
            for (int k = 0; k < ribs; k++)
            {
                float a = k / (float)ribs * Mathf.PI * 2f;
                Vector3 prev = new(Mathf.Cos(a) * r, 0.002f, Mathf.Sin(a) * r);
                for (int s = 1; s <= 8; s++)
                {
                    float t = s / 8f * Mathf.PI * 0.5f;
                    var p = new Vector3(Mathf.Cos(a) * r * Mathf.Cos(t), 0.002f + h * Mathf.Sin(t), Mathf.Sin(a) * r * Mathf.Cos(t));
                    var d = p - prev;
                    b.Push((p + prev) / 2f, Quaternion.LookRotation(d.normalized, Vector3.up));
                    b.AddBox(Vector3.zero, new Vector3(0.0012f, 0.0012f, d.magnitude), rib);
                    b.Pop();
                    prev = p;
                }
            }
            foreach (float y in new[] { 0.03f, 0.06f })
            {
                float rr = r * Mathf.Cos(Mathf.Asin((y - 0.002f) / h));
                b.AddCylinder(new Vector3(0f, y, 0f), rr, rr, 0.001f, 28, rib, rib, false);
            }
            b.AddBlob(new Vector3(0f, 0.002f + h, 0f), Vector3.one * 0.004f, 1, 0f, 3, (_, __) => Pal.Brass);
            return h + 0.01f;
        }

        /// <summary>The tropical house: a long glasshouse with a curved roof, palms and great leaves pressing against the glass.</summary>
        private static float TropicalGlasshouse(MeshBuilder b, ref Rng rng)
        {
            const float hx = 0.085f, hz = 0.05f, h = 0.06f;
            b.AddBox(new Vector3(0f, 0.002f, 0f), new Vector3(hx * 2f, 0.004f, hz * 2f), ZooStone);
            b.AddBox(new Vector3(0f, 0.0042f, 0f), new Vector3(hx * 2f - 0.006f, 0.0004f, hz * 2f - 0.006f), Pal.Hex("#5E8A3E"));
            for (int k = 0; k < 5; k++) Palm(b, new Vector3(-0.06f + k * 0.03f, 0.004f, (k % 2 - 0.5f) * 0.03f), 0.04f + (k % 3) * 0.006f, 10 + k);
            for (int k = 0; k < 8; k++) b.AddBlob(new Vector3(rng.Range(-hx, hx) * 0.8f, 0.009f, rng.Range(-hz, hz) * 0.7f), new Vector3(0.009f, 0.006f, 0.009f), 1, 0.3f, 80 + k, (_, __) => Pal.Shade(ZooLeaf, 1.15f));
            // The glass: a barrel vault on low walls, white glazing bars.
            var glass = new Color(0.75f, 0.9f, 0.95f, 1f); var bar = Pal.Hex("#F2F2EE");
            foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(0f, 0.016f, sz * hz), new Vector3(hx * 2f, 0.024f, 0.0008f), glass);
            const int arcs = 10;
            for (int s = 0; s < arcs; s++)
            {
                float a0 = s / (float)arcs * Mathf.PI, a1 = (s + 1) / (float)arcs * Mathf.PI;
                Vector3 P(float a, float x) => new(x, 0.028f + Mathf.Sin(a) * (h - 0.028f), Mathf.Cos(a) * hz);
                b.AddTriangleOutward(P(a0, -hx), P(a0, hx), P(a1, hx), new Vector3(0f, 0.02f, 0f), glass);
                b.AddTriangleOutward(P(a0, -hx), P(a1, hx), P(a1, -hx), new Vector3(0f, 0.02f, 0f), glass);
            }
            for (int k = 0; k <= 8; k++)
            {
                float x = -hx + k * hx / 4f;
                for (int s = 0; s < arcs; s++)
                {
                    float a0 = s / (float)arcs * Mathf.PI, a1 = (s + 1) / (float)arcs * Mathf.PI;
                    var p0 = new Vector3(x, 0.028f + Mathf.Sin(a0) * (h - 0.028f), Mathf.Cos(a0) * hz); var p1 = new Vector3(x, 0.028f + Mathf.Sin(a1) * (h - 0.028f), Mathf.Cos(a1) * hz);
                    b.Push((p0 + p1) / 2f, Quaternion.LookRotation((p1 - p0).normalized, Vector3.right));
                    b.AddBox(Vector3.zero, new Vector3(0.0012f, 0.0012f, (p1 - p0).magnitude * 1.05f), bar);
                    b.Pop();
                }
                foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(x, 0.016f, sz * hz), new Vector3(0.0012f, 0.024f, 0.0012f), bar);
            }
            // Gable ends of glass, and a door at the front.
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * hx, 0.03f, 0f), new Vector3(0.0008f, 0.052f, hz * 2f), glass);
            b.AddBox(new Vector3(0f, 0.012f, -hz - 0.002f), new Vector3(0.014f, 0.02f, 0.004f), bar);
            b.AddFace(new Vector3(0f, 0.012f, -hz - 0.0042f), Vector3.back, Vector3.up, 0.005f, 0.009f, Pal.Hex("#2C4A3A"));
            Lettering.Board(b, "TROPICS", new Vector3(0f, 0.027f, -hz - 0.004f), Vector3.back, 0.0035f, ZooGreen, Pal.Hex("#F2E3A8"), bar);
            return h + 0.005f;
        }

        /// <summary>The petting zoo: a little red barn, hay, a water trough and a paddock of goats children can stroke.</summary>
        private static float PettingPaddock(MeshBuilder b, ref Rng rng)
        {
            const float hx = 0.065f, hz = 0.055f;
            b.AddBox(new Vector3(0f, 0.001f, 0f), new Vector3(hx * 2f, 0.002f, hz * 2f), Pal.Hex("#8FBF5E"));
            var barn = new Vector3(0.045f, 0f, 0.03f);
            b.AddBox(barn + Vector3.up * 0.011f, new Vector3(0.03f, 0.022f, 0.026f), Pal.Hex("#B8483E"), Pal.Hex("#B8483E"), Pal.Hex("#B8483E"));
            GableRoof(b, barn + Vector3.up * 0.022f, 0.034f, 0.03f, 0.014f, Pal.Hex("#5A4A3A"), alongZ: true);
            b.AddFace(barn + new Vector3(-0.0152f, 0.008f, 0f), Vector3.left, Vector3.up, 0.006f, 0.008f, Pal.Hex("#F2E3C2"));
            b.AddBox(new Vector3(-0.04f, 0.004f, 0.035f), new Vector3(0.014f, 0.008f, 0.01f), Pal.Hex("#E2C46A"));   // hay
            b.AddBox(new Vector3(-0.02f, 0.003f, -0.03f), new Vector3(0.016f, 0.005f, 0.006f), Pal.Hex("#8A8F96"));   // trough
            ZooFence(b, new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz)); ZooFence(b, new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz));
            ZooFence(b, new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz)); ZooFence(b, new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz));
            Lettering.Board(b, "PETTING ZOO", new Vector3(0f, 0.018f, -hz - 0.001f), Vector3.back, 0.0035f, Pal.Hex("#B8483E"), Pal.Hex("#F7F1E6"), ZooTimber);
            return 0.04f;
        }

        /// <summary>The kiosk: a round hut with a thatched cone roof, a serving hatch, menu board, parasols and tables.</summary>
        private static float ZooSnackKiosk(MeshBuilder b, ref Rng rng)
        {
            b.AddBox(new Vector3(0f, 0.0006f, 0f), new Vector3(0.08f, 0.0012f, 0.07f), ZooPath);
            var hut = new Vector3(0f, 0f, 0.015f);
            b.AddCylinder(hut, 0.016f, 0.016f, 0.024f, 16, ZooTimberLight, ZooTimberLight, false);
            b.AddCylinder(hut + Vector3.up * 0.024f, 0.024f, 0.002f, 0.018f, 16, ZooThatch, Pal.Shade(ZooThatch, 1.15f), true);
            b.AddFace(hut + new Vector3(0f, 0.014f, -0.0162f), Vector3.back, Vector3.up, 0.008f, 0.005f, Pal.Hex("#3A2A1E"));
            b.AddBox(hut + new Vector3(0f, 0.009f, -0.018f), new Vector3(0.018f, 0.0012f, 0.004f), ZooTimber);
            Lettering.Board(b, "SNACKS", hut + new Vector3(0f, 0.021f, -0.0165f), Vector3.back, 0.0032f, Pal.Hex("#E25B4F"), Pal.Hex("#F7F1E6"), ZooTimber);
            foreach (var (x, z, c) in new[] { (-0.025f, -0.02f, Pal.Hex("#E25B4F")), (0.025f, -0.022f, Pal.Hex("#F2C12E")) })
            {
                b.AddCylinder(new Vector3(x, 0f, z), 0.006f, 0.006f, 0.0008f, 10, ZooTimberLight, ZooTimberLight, true);
                b.AddCylinder(new Vector3(x, 0f, z), 0.0006f, 0.0006f, 0.024f, 6, Pal.Hex("#E8E2D6"), Pal.Hex("#E8E2D6"), false);
                b.AddCylinder(new Vector3(x, 0.006f, z), 0.0012f, 0.0012f, 0.0012f, 6, ZooTimberLight, ZooTimberLight, true);
                b.AddCylinder(new Vector3(x, 0.02f, z), 0.014f, 0.001f, 0.006f, 12, c, Pal.Shade(c, 1.1f), true);
            }
            return 0.045f;
        }

        /// <summary>The safari café: a big thatched roundhouse on timber posts, open on all sides, a terrace of tables round it.</summary>
        private static float SafariRoundhouse(MeshBuilder b, ref Rng rng)
        {
            b.AddCylinder(Vector3.zero, 0.06f, 0.06f, 0.003f, 28, ZooTimberLight, Pal.Shade(ZooTimberLight, 1.08f), true);
            for (int k = 0; k < 10; k++)
            {
                float a = k / 10f * Mathf.PI * 2f;
                b.AddCylinder(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.042f, 0.0015f, 0.0015f, 0.03f, 6, ZooTimber, ZooTimber, false);
            }
            b.AddCylinder(new Vector3(0f, 0.03f, 0f), 0.056f, 0.004f, 0.04f, 24, ZooThatch, Pal.Shade(ZooThatch, 1.18f), true, true, true, 0.2f);
            b.AddCylinder(new Vector3(0f, 0.003f, 0.012f), 0.016f, 0.016f, 0.012f, 16, ZooTimber, ZooTimber, true);   // the bar
            for (int k = 0; k < 6; k++)
            {
                float a = (k + 0.5f) / 6f * Mathf.PI * 2f;
                var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.03f;
                b.AddCylinder(at + Vector3.up * 0.003f, 0.005f, 0.005f, 0.0008f, 10, ZooTimberLight, ZooTimberLight, true);
                b.AddCylinder(at + Vector3.up * 0.003f, 0.0008f, 0.0008f, 0.007f, 6, ZooTimber, ZooTimber, false);
            }
            Lettering.Board(b, "SAFARI CAFE", new Vector3(0f, 0.036f, -0.052f), Vector3.back, 0.004f, ZooGreen, Pal.Hex("#F2E3A8"), ZooTimber);
            return 0.075f;
        }

        /// <summary>Palms and rocks: a planted bed to dress the paths between the enclosures.</summary>
        private static float ZooGardenBed(MeshBuilder b, ref Rng rng)
        {
            b.AddCylinder(Vector3.zero, 0.028f, 0.028f, 0.003f, 16, ZooStone, Pal.Hex("#6FA04A"), true);
            Palm(b, new Vector3(-0.008f, 0.003f, 0.004f), 0.04f, 21);
            Palm(b, new Vector3(0.01f, 0.003f, -0.006f), 0.03f, 22);
            Rock(b, new Vector3(0.012f, 0.006f, 0.012f), new Vector3(0.007f, 0.005f, 0.006f), 23, Pal.Hex("#B8A888"));
            for (int k = 0; k < 5; k++) b.AddBlob(new Vector3(rng.Range(-0.02f, 0.02f), 0.005f, rng.Range(-0.02f, 0.02f)), new Vector3(0.004f, 0.003f, 0.004f), 1, 0.2f, 24 + k, (_, __) => k % 2 == 0 ? Pal.FlowerRed : Pal.FlowerYellow);
            return 0.045f;
        }

        /// <summary>The zoo family's pieces, from <see cref="Living"/>.</summary>
        private static float ZooPiece(MeshBuilder b, ref Rng rng, int id) => id switch
        {
            Catalog.ZooGate => ZooGateway(b, ref rng),
            Catalog.ZooPlaza => ZooPlazaPaving(b, ref rng),
            Catalog.Savanna => SavannaPlain(b, ref rng),
            Catalog.ElephantHouse => ElephantHall(b, ref rng),
            Catalog.LionRock => LionsRock(b, ref rng),
            Catalog.PenguinPool => PenguinWater(b, ref rng),
            Catalog.MonkeyIsland => MonkeyIsle(b, ref rng),
            Catalog.Aviary => AviaryDome(b, ref rng),
            Catalog.TropicalHouse => TropicalGlasshouse(b, ref rng),
            Catalog.PettingZoo => PettingPaddock(b, ref rng),
            Catalog.ZooKiosk => ZooSnackKiosk(b, ref rng),
            Catalog.SafariCafe => SafariRoundhouse(b, ref rng),
            Catalog.ZooGarden => ZooGardenBed(b, ref rng),
            _ => -1f,
        };
    }
}
