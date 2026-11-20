using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The wider object library (see <see cref="Catalog"/>): town buildings, farm buildings, fields, rides, landmarks,
    /// railway buildings, rocks and small street details. Same parts and palette as the houses, so a village of mixed
    /// buildings reads as one storybook. Turning parts (carousel, sails, Ferris wheel) are separate meshes.
    /// </summary>
    public static partial class HouseFactory
    {
        private static readonly System.Collections.Generic.Dictionary<int, Mesh> Rotors = new();
        private static Mesh gondola;

        /// <summary>The turning part of an object (null if it has none), built around its pivot.</summary>
        public static Mesh Rotor(int id)
        {
            if (Catalog.Get(id).RotorSpeed == 0f) return null;
            if (!Rotors.TryGetValue(id, out var m))
            {
                var b = new MeshBuilder();
                var rng = new Rng(900 + id);
                if (id == Catalog.Disco || id == Catalog.Balloon) { Rotors[id] = m = TownRotor(id, ref rng); return m; }
                switch (id)
                {
                    case Catalog.Windmill: Sails(b); break;
                    case Catalog.Carousel: CarouselTop(b, ref rng); break;
                    case Catalog.FerrisWheel: WheelRim(b); break;
                }
                Rotors[id] = m = b.ToMesh("Rotor_" + Catalog.Get(id).Name);
            }
            return m;
        }

        /// <summary>A Ferris wheel cabin (hangs from its pivot at the origin).</summary>
        public static Mesh Gondola()
        {
            if (gondola != null) return gondola;
            var b = new MeshBuilder();
            b.AddCylinder(new Vector3(0f, -0.006f, 0f), 0.0006f, 0.0006f, 0.006f, 4, Pal.TrainBlack, Pal.TrainBlack, false);
            b.AddBox(new Vector3(0f, -0.014f, 0f), new Vector3(0.018f, 0.011f, 0.013f), Pal.Hex("#E25B4F"), Pal.Hex("#F7F1E6"), Pal.Hex("#B8483E"));
            b.AddBlob(new Vector3(0f, -0.0075f, 0f), new Vector3(0.011f, 0.003f, 0.008f), 1, 0f, 3, (d, _) => Pal.Hex("#F7F1E6"));
            foreach (int s in new[] { -1, 1 }) b.AddFace(new Vector3(0f, -0.013f, s * 0.0066f), new Vector3(0f, 0f, s), Vector3.up, 0.007f, 0.003f, Pal.Glow(Pal.WindowGlow));
            return gondola = b.ToMesh("Gondola");
        }

        /// <summary>Builds catalog object <paramref name="id"/>; <paramref name="warp"/>: give it the hand-made lean.</summary>
        private static float Place(MeshBuilder b, ref Rng rng, Style s, int id, int style, out bool warp)
        {
            float own = Overhauled(b, ref rng, s, id, style, out warp);
            if (own >= 0f) return own;
            float shop = Shops(b, ref rng, s, id, style, out warp);
            if (shop >= 0f) return shop;
            float living = Living(b, ref rng, s, id, style, out warp);
            if (living >= 0f) return living;
            float town = Town(b, ref rng, s, id, style, out warp);
            if (town >= 0f) return town;
            warp = true;
            switch (id)
            {
                case Catalog.Apartments: return Apartments(b, ref rng, s);
                case Catalog.Chalet: return Chalet(b, ref rng, s);
                case Catalog.Church: return Church(b, ref rng, s, style);
                case Catalog.TownHall: return TownHall(b, ref rng, s, style);
                case Catalog.Bakery: return Shop(b, ref rng, s, 0);
                case Catalog.Cafe: return Shop(b, ref rng, s, 1);
                case Catalog.Grocer: return Shop(b, ref rng, s, 2);
                case Catalog.School: return School(b, ref rng, s);
                case Catalog.FireStation: return FireStation(b, ref rng, s);
                case Catalog.Barn: return Barn(b, ref rng, style);
                case Catalog.SignalBox: return SignalBoxHouse(b, ref rng, s);
            }
            warp = false;
            switch (id)
            {
                case Catalog.Fountain: return FountainSquare(b, ref rng);
                case Catalog.Windmill: return WindmillTower(b, ref rng, s);
                case Catalog.Silo: return SiloTower(b);
                case Catalog.Greenhouse: return GreenhouseGlass(b, ref rng);
                case Catalog.WheatField: return Field(b, ref rng, style);
                case Catalog.Garden: return VegetableGarden(b, ref rng);
                case Catalog.Carousel: return CarouselBase(b);
                case Catalog.FerrisWheel: return WheelStand(b);
                case Catalog.Playground: return PlaygroundYard(b, ref rng);
                case Catalog.Bandstand: return BandstandPavilion(b, ref rng, style);
                case Catalog.CastleRuin: return Ruin(b, ref rng);
                case Catalog.Lookout: return LookoutTower(b);
                case Catalog.Campsite: return Camp(b, ref rng);
                case Catalog.WaterTower: return WaterTowerTank(b);
                case Catalog.Fuel: return FuelStation(b, ref rng);
                case Catalog.Boulders: return Rocks(b, ref rng, style);
                case Catalog.Crag: return CragRock(b, ref rng, style);
                case Catalog.LogPile: return Logs(b, ref rng);
                case Catalog.Lamp: return StreetLamp(b);
                case Catalog.Bench: return ParkBench(b, style);
                case Catalog.FlowerBed: return Bed(b, ref rng, style);
                case Catalog.Well: return WishingWell(b, ref rng);
                case Catalog.Statue: return StatueOnPlinth(b);
                case Catalog.Signpost: return SignpostArrows(b);
                case Catalog.Picnic: return PicnicTable(b);
                case Catalog.HayBales: return Hay(b, ref rng);
            }
            b.AddBox(new Vector3(0f, 0.01f, 0f), Vector3.one * 0.02f, Pal.Stone);
            return 0.02f;
        }

        // ---------------------------------------------------------------- shared parts

        private static void Windows(MeshBuilder b, ref Rng rng, Style s, float face, float[] xs, float y, float w, float h, bool shutters, bool flowers)
        {
            foreach (float x in xs)
            {
                Wall(b, new Vector3(x, y, face), face < 0f ? Vector3.back : Vector3.forward);
                Window(b, ref rng, w, h, s, shutters, flowers, false);
                b.Pop();
            }
        }

        private static void SideWindows(MeshBuilder b, ref Rng rng, Style s, float halfW, float[] zs, float y, float w, float h)
        {
            foreach (int side in new[] { -1, 1 })
            foreach (float z in zs)
            {
                Wall(b, new Vector3(side * halfW, y, z), side < 0 ? Vector3.left : Vector3.right);
                Window(b, ref rng, w, h, s, false, false, false);
                b.Pop();
            }
        }

        /// <summary>A striped canvas awning sloping out from a wall (front -Z) with a scalloped valance.</summary>
        private static void Awning(MeshBuilder b, Vector3 at, float width, float depth, Color a, Color c)
        {
            int stripes = Mathf.Max(3, Mathf.RoundToInt(width / 0.007f));
            float sw = width / stripes;
            for (int i = 0; i < stripes; i++)
            {
                float x = -width / 2 + (i + 0.5f) * sw;
                b.Push(at + new Vector3(x, -depth * 0.35f, -depth / 2), Quaternion.Euler(-35f, 0f, 0f));
                b.AddBox(Vector3.zero, new Vector3(sw, 0.0012f, depth * 1.2f), i % 2 == 0 ? a : c);
                b.Pop();
                b.AddBlob(at + new Vector3(x, -depth * 0.72f - 0.002f, -depth * 0.98f), new Vector3(sw * 0.5f, 0.0028f, 0.0008f), 0, 0f, i, (_, __) => i % 2 == 0 ? a : c);
            }
        }

        private static void Board(MeshBuilder b, Vector3 at, float w, float h, Color board, Color text)
        {
            b.AddBox(at, new Vector3(w, h, 0.0016f), board);
            for (int i = 0; i < 4; i++) b.AddBox(at + new Vector3(-w * 0.32f + i * w * 0.21f, 0f, -0.0009f), new Vector3(w * 0.13f, h * 0.42f, 0.0004f), text);
        }

        private static void Flag(MeshBuilder b, Vector3 at, float height, Color colour)
        {
            b.AddCylinder(at, 0.0006f, 0.0005f, height, 4, Pal.TrainBlack, Pal.TrainBlack, true);
            b.AddBox(at + new Vector3(0.0055f, height - 0.004f, 0f), new Vector3(0.011f, 0.007f, 0.0006f), colour);
        }

        private static void Person(MeshBuilder b, Vector3 at, float yaw, int style, Color tint, float scale = 1f)
        {
            var m = Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);
            b.Append(new MeshData(PersonFactory.Body(style)), m, tint);
            foreach (int side in new[] { -1, 1 })
            {
                b.Append(new MeshData(PersonFactory.Leg(style)), m * Matrix4x4.Translate(PersonFactory.Hip + Vector3.right * side * PersonFactory.LegX), tint);
                b.Append(new MeshData(PersonFactory.Arm(style)), m * Matrix4x4.TRS(new Vector3(side * PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f), Quaternion.Euler(0f, 0f, side * 8f), Vector3.one), tint);
            }
        }

        private static Color Stripe(int i) => i switch { 0 => Pal.Hex("#E25B4F"), 1 => Pal.Hex("#5E9A63"), 2 => Pal.Hex("#4A7FB8"), _ => Pal.Hex("#E2B04A") };

        // ---------------------------------------------------------------- homes

        private static float Apartments(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.1f, D = 0.074f, H = 0.135f, R = 0.04f;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, s.Plaster);
            foreach (float y in new[] { 0.045f, 0.09f }) b.AddBox(new Vector3(0f, y, 0f), new Vector3(W + 0.003f, 0.003f, D + 0.003f), s.Trim);
            for (int floor = 0; floor < 3; floor++)
            {
                float y = 0.026f + floor * 0.045f;
                Windows(b, ref rng, s, -D / 2, floor == 0 ? new[] { -0.034f, 0.034f } : new[] { -0.034f, 0f, 0.034f }, y, 0.016f, 0.019f, true, floor > 0);
                Windows(b, ref rng, s, D / 2, new[] { -0.03f, 0.03f }, y, 0.016f, 0.019f, false, false);
                SideWindows(b, ref rng, s, W / 2, new[] { 0f }, y, 0.014f, 0.018f);
                if (floor > 0)
                {
                    // Balconies with railings and pot plants.
                    foreach (float x in new[] { -0.034f, 0.034f })
                    {
                        b.AddBox(new Vector3(x, y - 0.013f, -D / 2 - 0.006f), new Vector3(0.026f, 0.002f, 0.012f), s.Trim);
                        b.AddBox(new Vector3(x, y - 0.007f, -D / 2 - 0.0115f), new Vector3(0.026f, 0.0012f, 0.0012f), Pal.TrainBlack);
                        for (int k = -3; k <= 3; k++) b.AddBox(new Vector3(x + k * 0.0038f, y - 0.0095f, -D / 2 - 0.0115f), new Vector3(0.0006f, 0.005f, 0.0006f), Pal.TrainBlack);
                        var c = Pal.Jitter(s.Flower, ref rng, 0.2f);
                        b.AddBlob(new Vector3(x + 0.009f, y - 0.009f, -D / 2 - 0.006f), Vector3.one * 0.0026f, 1, 0.2f, floor, (_, __) => c);
                    }
                }
            }
            Wall(b, new Vector3(0f, 0.004f, -D / 2), Vector3.back); Door(b, ref rng, 0.02f, 0.034f, s, true, true); b.Pop();
            StoryRoof(b, ref rng, W, D, H, R, 0.01f, 0.003f, s.Roof, s.Plaster, s.Trim, 6, false);
            Chimney(b, ref rng, new Vector3(-0.03f, H, 0.01f), R + 0.02f, Pal.Brick, 2);
            Chimney(b, ref rng, new Vector3(0.032f, H, -0.008f), R + 0.018f, Pal.Brick, 3);
            Shrubs(b, ref rng, W, D, 4, s.Flower);
            return H + R;
        }

        private static float Chalet(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.096f, D = 0.08f, H = 0.07f, R = 0.036f;
            var wood = Pal.Hex("#8A5A36"); var woodDark = Pal.Hex("#5E3A22");
            Foundation(b, ref rng, W, D);
            b.AddBox(new Vector3(0f, 0.012f, 0f), new Vector3(W + 0.002f, 0.024f, D + 0.002f), Pal.Stone, Pal.Stone, Pal.StoneDark);
            Walls(b, W, D, 0.024f, H, wood);
            for (float y = 0.03f; y < H; y += 0.0055f) b.AddBox(new Vector3(0f, y, 0f), new Vector3(W + 0.0012f, 0.0007f, D + 0.0012f), woodDark);
            Windows(b, ref rng, s, -D / 2, new[] { -0.026f, 0.03f }, 0.043f, 0.016f, 0.017f, true, true);
            Windows(b, ref rng, s, D / 2, new[] { 0f }, 0.043f, 0.016f, 0.017f, true, true);
            SideWindows(b, ref rng, s, W / 2, new[] { 0f }, 0.043f, 0.015f, 0.016f);
            Wall(b, new Vector3(0.02f, 0.024f, -D / 2), Vector3.back); Door(b, ref rng, 0.017f, 0.03f, s, false, false); b.Pop();
            // A carved balcony under the deep gable.
            b.AddBox(new Vector3(0f, H + 0.002f, -D / 2 - 0.009f), new Vector3(W * 0.8f, 0.0025f, 0.018f), woodDark);
            for (float x = -W * 0.38f; x <= W * 0.381f; x += 0.006f) b.AddBox(new Vector3(x, H + 0.008f, -D / 2 - 0.0175f), new Vector3(0.0028f, 0.01f, 0.0008f), Pal.Hex("#A8743E"));
            b.AddBox(new Vector3(0f, H + 0.013f, -D / 2 - 0.0175f), new Vector3(W * 0.8f, 0.0016f, 0.0016f), woodDark);
            for (int i = 0; i < 6; i++)
            {
                var c = i % 2 == 0 ? Pal.FlowerRed : Pal.FlowerPink;
                b.AddBlob(new Vector3(-W * 0.34f + i * W * 0.136f, H + 0.0145f, -D / 2 - 0.0175f), new Vector3(0.004f, 0.0025f, 0.0025f), 1, 0.25f, i, (_, __) => c);
            }
            b.Push(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
            StoryRoof(b, ref rng, D + 0.012f, W, H + 0.016f, R + 0.008f, 0.02f, 0.002f, Pal.Hex("#5A4A44"), wood, s.Trim, 6, false);
            b.Pop();
            Chimney(b, ref rng, new Vector3(-0.025f, H, 0.02f), R + 0.016f, Pal.Stone, 1);
            // Logs stacked under the eaves and a bench by the wall.
            for (int k = 0; k < 6; k++)
                CylinderX(b, new Vector3(W / 2 + 0.004f, 0.004f + (k / 3) * 0.006f, -0.02f + (k % 3) * 0.007f), 0.0032f, 0.018f, Pal.Bark, Pal.Hex("#D9B27A"));
            b.AddBox(new Vector3(-0.03f, 0.007f, -D / 2 - 0.006f), new Vector3(0.026f, 0.0018f, 0.008f), Pal.Hex("#A8743E"));
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(-0.03f + sx * 0.011f, 0.0035f, -D / 2 - 0.006f), new Vector3(0.002f, 0.007f, 0.007f), woodDark);
            return H + R;
        }

        private static void CylinderX(MeshBuilder b, Vector3 centre, float r, float len, Color bark, Color end)
        {
            b.Push(centre + Vector3.right * (-len / 2), Quaternion.Euler(0f, 0f, -90f));
            b.AddCylinder(Vector3.zero, r, r, len, 8, bark, Pal.Shade(bark, 1.15f), false);
            b.Pop();
            foreach (int e in new[] { -1, 1 })
            {
                b.Push(centre + Vector3.right * (e * len / 2), Quaternion.Euler(0f, 0f, e * -90f));
                b.AddDisc(Vector3.zero, r * 0.95f, 8, end, true);
                b.Pop();
            }
        }

        // ---------------------------------------------------------------- town

        private static float Church(MeshBuilder b, ref Rng rng, Style s, int style)
        {
            const float W = 0.066f, D = 0.12f, H = 0.07f, R = 0.05f;
            var stone = style == 0 ? Pal.Hex("#E9E0CF") : Pal.Hex("#D8C8A8");
            s.Plaster = stone; s.Roof = style == 0 ? Pal.RoofSlate : Pal.RoofTerracotta;
            b.Push(new Vector3(0f, 0f, 0.02f));
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, stone);
            // Tall arched windows along the nave.
            foreach (int side in new[] { -1, 1 })
            foreach (float z in new[] { -0.03f, 0f, 0.03f })
            {
                Wall(b, new Vector3(side * W / 2, 0.034f, z), side < 0 ? Vector3.left : Vector3.right);
                Window(b, ref rng, 0.012f, 0.026f, s, false, false, true);
                b.Pop();
            }
            Wall(b, new Vector3(0f, H + 0.02f, D / 2), Vector3.forward); RoundWindow(b, 0.009f, s.Trim, s); b.Pop();
            b.Push(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
            StoryRoof(b, ref rng, D, W, H, R, 0.008f, 0.002f, s.Roof, stone, s.Trim, 8, false);
            b.Pop();
            b.Pop();
            // The tower at the front, with a belfry, a clock and a tall spire.
            float tz = -D / 2 + 0.02f - 0.004f, tw = 0.04f, th = 0.17f;
            b.AddBox(new Vector3(0f, th / 2 - 0.01f, tz), new Vector3(tw, th + 0.02f, tw), stone, stone, Pal.StoneDark);
            for (float y = 0.02f; y < th; y += 0.03f) b.AddBox(new Vector3(0f, y, tz), new Vector3(tw + 0.0016f, 0.0016f, tw + 0.0016f), Pal.Shade(stone, 0.88f));
            Wall(b, new Vector3(0f, 0.003f, tz - tw / 2), Vector3.back); Door(b, ref rng, 0.018f, 0.034f, s, true, false); b.Pop();
            foreach (var n in new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right })
            {
                // Belfry openings (dark arches) and the clock face below them.
                b.Push(new Vector3(0f, th - 0.022f, tz) + n * (tw / 2 + 0.0004f), Quaternion.LookRotation(n, Vector3.up));
                b.AddFace(Vector3.zero, Vector3.forward, Vector3.up, 0.006f, 0.01f, Pal.Hex("#2A2224"));
                b.AddBlob(new Vector3(0f, 0.01f, 0f), new Vector3(0.006f, 0.004f, 0.0006f), 0, 0f, 2, (_, __) => Pal.Hex("#2A2224"));
                b.Pop();
                b.Push(new Vector3(0f, th - 0.055f, tz) + n * (tw / 2 + 0.0005f), Quaternion.LookRotation(n, Vector3.up) * Quaternion.Euler(90f, 0f, 0f));
                b.AddCylinder(Vector3.zero, 0.0095f, 0.0095f, 0.0012f, 16, Pal.Brass, Pal.Brass, true);
                b.AddDisc(new Vector3(0f, 0.0013f, 0f), 0.0078f, 16, Pal.Hex("#F7F1E6"), true);
                b.AddBox(new Vector3(0f, 0.0016f, 0.0028f), new Vector3(0.0009f, 0.0004f, 0.0056f), Pal.TrainBlack);
                b.AddBox(new Vector3(0.002f, 0.0016f, 0f), new Vector3(0.004f, 0.0004f, 0.0009f), Pal.TrainBlack);
                b.Pop();
            }
            b.AddBox(new Vector3(0f, th + 0.002f, tz), new Vector3(tw + 0.006f, 0.004f, tw + 0.006f), Pal.Shade(stone, 0.85f));
            b.AddCylinder(new Vector3(0f, th + 0.004f, tz), tw * 0.68f, 0f, 0.11f, 8, Pal.Shade(s.Roof, 0.85f), s.Roof, false, false, false, Mathf.PI / 8f);
            b.AddBlob(new Vector3(0f, th + 0.116f, tz), Vector3.one * 0.0022f, 1, 0f, 3, (_, __) => Pal.Brass);
            b.AddBox(new Vector3(0f, th + 0.126f, tz), new Vector3(0.0012f, 0.016f, 0.0012f), Pal.Brass);
            b.AddBox(new Vector3(0f, th + 0.129f, tz), new Vector3(0.008f, 0.0012f, 0.0012f), Pal.Brass);
            // A little churchyard: a yew and two gravestones.
            var leaf = Pal.Conifer;
            Flora.LeafCluster(b, new Vector3(0.05f, 0.016f, 0.06f), 0.014f, Vector3.up, 4, ref rng, (d, _) => Color.Lerp(Pal.Shade(leaf, 0.7f), leaf, d.y * 0.5f + 0.5f), 1.2f);
            foreach (float z in new[] { 0.035f, 0.075f })
            {
                b.AddBox(new Vector3(-0.05f, 0.005f, z), new Vector3(0.008f, 0.01f, 0.002f), Pal.StoneDark);
                b.AddBlob(new Vector3(-0.05f, 0.01f, z), new Vector3(0.004f, 0.002f, 0.001f), 0, 0f, 4, (_, __) => Pal.StoneDark);
            }
            return th + 0.13f;
        }

        private static float TownHall(MeshBuilder b, ref Rng rng, Style s, int style)
        {
            const float W = 0.13f, D = 0.078f, H = 0.088f, R = 0.04f;
            s.Plaster = style == 0 ? Pal.Hex("#F2DDB0") : Pal.Hex("#E7D2C4"); s.Roof = style == 0 ? Pal.Hex("#4E6E78") : Pal.RoofTerracotta;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, s.Plaster);
            b.AddBox(new Vector3(0f, 0.044f, 0f), new Vector3(W + 0.003f, 0.003f, D + 0.003f), s.Trim);
            foreach (float y in new[] { 0.024f, 0.066f })
            {
                Windows(b, ref rng, s, -D / 2, y < 0.04f ? new[] { -0.05f, -0.03f, 0.03f, 0.05f } : new[] { -0.05f, -0.03f, -0.01f, 0.01f, 0.03f, 0.05f }, y, 0.012f, 0.02f, false, y > 0.04f);
                Windows(b, ref rng, s, D / 2, new[] { -0.04f, 0f, 0.04f }, y, 0.012f, 0.02f, false, false);
                SideWindows(b, ref rng, s, W / 2, new[] { -0.015f, 0.015f }, y, 0.012f, 0.02f);
            }
            // Columned porch with steps and a pediment.
            Wall(b, new Vector3(0f, 0.006f, -D / 2), Vector3.back); Door(b, ref rng, 0.022f, 0.032f, s, true, false); b.Pop();
            for (int k = 0; k < 3; k++) b.AddBox(new Vector3(0f, 0.0015f + k * 0.002f, -D / 2 - 0.014f + k * 0.004f), new Vector3(0.05f - k * 0.004f, 0.003f, 0.01f), Pal.Stone);
            foreach (float x in new[] { -0.02f, -0.008f, 0.008f, 0.02f }) b.AddCylinder(new Vector3(x, 0.006f, -D / 2 - 0.009f), 0.0025f, 0.0022f, 0.036f, 10, Pal.Hex("#F7F1E6"), Pal.Hex("#F7F1E6"), false);
            b.AddBox(new Vector3(0f, 0.043f, -D / 2 - 0.009f), new Vector3(0.05f, 0.004f, 0.014f), s.Trim);
            b.Push(new Vector3(0f, 0.045f, -D / 2 - 0.009f));
            b.AddTriangleOutward(new Vector3(-0.027f, 0f, -0.007f), new Vector3(0.027f, 0f, -0.007f), new Vector3(0f, 0.012f, -0.007f), new Vector3(0f, 0.004f, 0.01f), s.Trim);
            b.Pop();
            StoryRoof(b, ref rng, W, D, H, R, 0.01f, 0.002f, s.Roof, s.Plaster, s.Trim, 6, false);
            // Clock tower with a dome and a flag.
            float ty = H + R - 0.012f;
            b.AddBox(new Vector3(0f, ty + 0.02f, 0f), new Vector3(0.03f, 0.04f, 0.03f), s.Plaster, s.Plaster, s.Plaster);
            foreach (var n in new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right })
            {
                b.Push(new Vector3(0f, ty + 0.024f, 0f) + n * 0.0155f, Quaternion.LookRotation(n, Vector3.up) * Quaternion.Euler(90f, 0f, 0f));
                b.AddCylinder(Vector3.zero, 0.0085f, 0.0085f, 0.001f, 16, Pal.Brass, Pal.Brass, true);
                b.AddDisc(new Vector3(0f, 0.0011f, 0f), 0.0068f, 16, Pal.Glow(Pal.Hex("#FFF4D8")), true);
                b.AddBox(new Vector3(0f, 0.0014f, 0.0024f), new Vector3(0.0008f, 0.0004f, 0.0048f), Pal.TrainBlack);
                b.AddBox(new Vector3(-0.0017f, 0.0014f, 0f), new Vector3(0.0034f, 0.0004f, 0.0008f), Pal.TrainBlack);
                b.Pop();
            }
            b.AddBlob(new Vector3(0f, ty + 0.042f, 0f), new Vector3(0.017f, 0.014f, 0.017f), 2, 0f, 5, (d, _) => d.y < 0.05f ? s.Trim : Color.Lerp(Pal.Shade(Pal.Hex("#5E9A8A"), 0.8f), Pal.Hex("#7FBFAE"), d.y));
            Flag(b, new Vector3(0f, ty + 0.054f, 0f), 0.024f, Pal.Hex("#E25B4F"));
            Shrubs(b, ref rng, W, D, 4, s.Flower);
            return ty + 0.08f;
        }

        /// <summary>Bakery (0), café (1) or grocer (2): a shopfront with a big lit window and an awning.</summary>
        private static float Shop(MeshBuilder b, ref Rng rng, Style s, int kind)
        {
            const float W = 0.088f, D = 0.072f, H = 0.062f, R = 0.05f;
            // Each trade its own colours, so a street of shops reads from across the room: a warm bakery, a pale blue café,
            // a fresh green grocer (the roof still varies from shop to shop).
            s.Plaster = kind == 0 ? Pal.Hex("#F3DCA0") : kind == 1 ? Pal.Hex("#BFD8EA") : Pal.Hex("#CFE6C2");
            s.Trim = kind == 0 ? Pal.Hex("#8E2F25") : kind == 1 ? Pal.Hex("#2E4E7A") : Pal.Hex("#2E6B3A");
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, s.Plaster);
            if (kind == 0) TimberFrame(b, W, D, H, s.Timber);
            // Shop window and door at the front, flats above.
            float doorX = kind == 0 ? -0.014f : kind == 1 ? -0.004f : 0f;
            float winX = kind == 0 ? 0.018f : kind == 1 ? 0.022f : -0.024f;
            b.Push(new Vector3(winX, 0.022f, -D / 2 - 0.0005f));
            b.AddFace(Vector3.zero, Vector3.back, Vector3.up, 0.016f, 0.012f, Pal.Glow(Pal.Hex("#FFE3A8")));
            b.AddBox(new Vector3(0f, -0.0135f, -0.002f), new Vector3(0.036f, 0.003f, 0.005f), s.Trim);
            b.AddBox(new Vector3(0f, 0.0125f, -0.001f), new Vector3(0.034f, 0.0015f, 0.002f), s.Trim);
            for (int k = -1; k <= 1; k++) b.AddBox(new Vector3(k * 0.0165f, 0f, -0.001f), new Vector3(0.0015f, 0.025f, 0.002f), s.Trim);
            if (kind == 0)
                for (int k = 0; k < 5; k++)
                {
                    var loaf = Pal.Hex(k % 2 == 0 ? "#C98A4B" : "#E0A867");
                    b.AddBlob(new Vector3(-0.012f + k * 0.006f, -0.008f, -0.0035f), new Vector3(0.0028f, 0.0018f, 0.0016f), 1, 0.15f, k, (_, __) => loaf);
                }
            b.Pop();
            if (kind == 2)
            {
                // A second window on the other side of the door.
                b.Push(new Vector3(-winX, 0.022f, -D / 2 - 0.0005f));
                b.AddFace(Vector3.zero, Vector3.back, Vector3.up, 0.012f, 0.012f, Pal.Glow(Pal.Hex("#FFE3A8")));
                b.AddBox(new Vector3(0f, -0.0135f, -0.002f), new Vector3(0.028f, 0.003f, 0.005f), s.Trim);
                b.Pop();
            }
            Wall(b, new Vector3(doorX, 0.004f, -D / 2), Vector3.back); Door(b, ref rng, 0.016f, 0.032f, s, false, false); b.Pop();
            Windows(b, ref rng, s, -D / 2, new[] { -0.022f, 0.022f }, 0.05f, 0.014f, 0.014f, true, true);
            Windows(b, ref rng, s, D / 2, new[] { 0f }, 0.034f, 0.016f, 0.018f, false, false);
            SideWindows(b, ref rng, s, W / 2, new[] { 0f }, 0.036f, 0.014f, 0.018f);
            var awningA = kind == 0 ? Pal.Hex("#E25B4F") : kind == 1 ? Pal.Hex("#4A7FB8") : Pal.Hex("#5E9A63");
            Awning(b, new Vector3(0f, 0.04f, -D / 2), W * 0.92f, 0.016f, awningA, Pal.Hex("#F7F1E6"));
            Board(b, new Vector3(0f, 0.0445f, -D / 2 - 0.0012f), 0.046f, 0.006f, Pal.Shade(awningA, 0.7f), Pal.Hex("#F7E3A8"));
            StoryRoof(b, ref rng, W, D, H, R, 0.011f, 0.003f, s.Roof, s.Plaster, s.Trim, 6, true);
            Chimney(b, ref rng, new Vector3(0.028f, H, 0.012f), R + 0.016f, Pal.Brick, kind == 0 ? 2 : 1);
            if (kind == 1)
            {
                // The café: a wide glass front and bistro tables with chairs on the pavement.
                b.AddFace(new Vector3(0.012f, 0.02f, -D / 2 - 0.0006f), Vector3.back, Vector3.up, 0.026f, 0.011f, Pal.Glow(Pal.Hex("#FFE9C0")));
                for (int k = -2; k <= 2; k++) b.AddBox(new Vector3(0.012f + k * 0.0125f, 0.02f, -D / 2 - 0.0012f), new Vector3(0.0012f, 0.023f, 0.001f), s.Trim);
                foreach (float x in new[] { -0.028f, 0.03f })
                {
                    var t = new Vector3(x, 0f, -D / 2 - 0.022f);
                    b.AddCylinder(t, 0.0006f, 0.0006f, 0.008f, 4, Pal.TrainBlack, Pal.TrainBlack, false);
                    b.AddCylinder(t + Vector3.up * 0.008f, 0.0055f, 0.0055f, 0.0008f, 12, Pal.FlowerWhite, Pal.FlowerWhite, true, true);
                    b.AddBlob(t + new Vector3(0f, 0.0094f, 0f), new Vector3(0.0012f, 0.0012f, 0.0012f), 0, 0f, 3, (_, __) => Pal.Hex("#F7F3EA"));   // a cup
                    foreach (int sx in new[] { -1, 1 })
                    {
                        b.AddBox(t + new Vector3(sx * 0.0075f, 0.0045f, 0f), new Vector3(0.004f, 0.0006f, 0.004f), Pal.Hex("#2E4E7A"));
                        b.AddBox(t + new Vector3(sx * 0.0095f, 0.0075f, 0f), new Vector3(0.0006f, 0.006f, 0.004f), Pal.Hex("#2E4E7A"));
                    }
                }
            }
            if (kind == 2)
            {
                // The grocer: two stands of fruit and veg out on the pavement under the awning.
                var wood = Pal.Hex("#B07A45");
                var produce = new[] { "#D9473A", "#E8B84A", "#6FA84A", "#E07B39", "#8E3A8A", "#F2D24A" };
                foreach (float x in new[] { -0.026f, 0.026f })
                {
                    b.Push(new Vector3(x, 0.006f, -D / 2 - 0.012f), Quaternion.Euler(-20f, 0f, 0f));
                    b.AddBox(Vector3.zero, new Vector3(0.026f, 0.002f, 0.014f), wood);
                    for (int k = 0; k < 6; k++)
                        b.AddBlob(new Vector3(-0.009f + (k % 3) * 0.009f, 0.0025f, -0.003f + (k / 3) * 0.006f), new Vector3(0.0042f, 0.0022f, 0.0028f), 1, 0.2f, 60 + k, (_, __) => Pal.Hex(produce[(k + (x > 0f ? 3 : 0)) % produce.Length]));
                    b.Pop();
                    foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(x + sx * 0.012f, 0.003f, -D / 2 - 0.012f), new Vector3(0.0012f, 0.006f, 0.0012f), wood);
                }
            }
            if (kind == 0)
            {
                // A golden pretzel sign on a bracket and a basket of bread outside.
                b.AddBox(new Vector3(W / 2 + 0.006f, 0.05f, -D / 2 + 0.008f), new Vector3(0.012f, 0.0012f, 0.0012f), Pal.TrainBlack);
                foreach (var (x, y) in new[] { (-0.003f, 0.0f), (0.003f, 0.0f), (0f, -0.004f) })
                    b.AddBlob(new Vector3(W / 2 + 0.011f + x, 0.044f + y, -D / 2 + 0.008f), new Vector3(0.0028f, 0.0028f, 0.0009f), 1, 0f, 7, (_, __) => Pal.Brass);
                b.AddCylinder(new Vector3(0.022f, 0f, -D / 2 - 0.014f), 0.006f, 0.007f, 0.007f, 10, Pal.Hex("#B98A4E"), Pal.Hex("#D9AA68"), true);
                for (int k = 0; k < 4; k++) b.AddBlob(new Vector3(0.019f + (k % 2) * 0.005f, 0.008f, -D / 2 - 0.016f + (k / 2) * 0.004f), new Vector3(0.0025f, 0.0016f, 0.0016f), 1, 0.1f, k + 9, (_, __) => Pal.Hex("#C98A4B"));
            }
            if (kind == 1)
            {
                // Two little round tables with chairs and parasols.
                foreach (float tx in new[] { -0.017f, 0.033f })
                {
                    float tz = -D / 2 - 0.037f + (tx > 0f ? -0.003f : 0f);
                    b.AddCylinder(new Vector3(tx, 0f, tz), 0.0012f, 0.0009f, 0.011f, 6, Pal.TrainBlack, Pal.TrainBlack, false);
                    b.AddCylinder(new Vector3(tx, 0.011f, tz), 0.0065f, 0.0065f, 0.0012f, 12, Pal.Hex("#F7F1E6"), Pal.Hex("#F7F1E6"), true, true);
                    b.AddBlob(new Vector3(tx, 0.0135f, tz), new Vector3(0.0015f, 0.0012f, 0.0015f), 0, 0f, 3, (_, __) => Pal.Hex("#E25B4F"));
                    b.AddCylinder(new Vector3(tx, 0.012f, tz), 0.0005f, 0.0005f, 0.026f, 4, Pal.Hex("#F7F1E6"), Pal.Hex("#F7F1E6"), false);
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * Mathf.PI / 4f;
                        b.Push(new Vector3(tx, 0.036f, tz), Quaternion.Euler(0f, -k * 45f, 0f));
                        b.AddTriangleOutward(Vector3.zero, new Vector3(0.016f, -0.006f, -0.0066f), new Vector3(0.016f, -0.006f, 0.0066f), new Vector3(0f, -0.02f, 0f), k % 2 == 0 ? awningA : Pal.Hex("#F7F1E6"));
                        b.Pop();
                    }
                    foreach (int side in new[] { -1, 1 })
                    {
                        var c = new Vector3(tx + side * 0.011f, 0f, tz);
                        b.AddBox(c + new Vector3(0f, 0.007f, 0f), new Vector3(0.006f, 0.0012f, 0.006f), Pal.Hex("#4A7FB8"));
                        b.AddBox(c + new Vector3(side * 0.003f, 0.011f, 0f), new Vector3(0.001f, 0.008f, 0.006f), Pal.Hex("#4A7FB8"));
                        foreach (float dz in new[] { -0.0025f, 0.0025f }) b.AddBox(c + new Vector3(-side * 0.0025f, 0.0035f, dz), new Vector3(0.0008f, 0.007f, 0.0008f), Pal.TrainBlack);
                    }
                }
                b.AddBox(new Vector3(-0.036f, 0.012f, -D / 2 - 0.006f), new Vector3(0.002f, 0.02f, 0.012f), Pal.TrainBlack);   // menu board
                b.AddBox(new Vector3(-0.0368f, 0.014f, -D / 2 - 0.006f), new Vector3(0.0006f, 0.012f, 0.009f), Pal.Hex("#2E3A30"));
            }
            if (kind == 2)
            {
                // Crates of fruit and vegetables on trestles either side of the door.
                var produce = new[] { Pal.Hex("#E2483D"), Pal.Hex("#F2A53A"), Pal.Hex("#6FAF3E"), Pal.Hex("#FFD34D"), Pal.Hex("#8E4A9A"), Pal.Hex("#E2483D") };
                foreach (float cx in new[] { -0.026f, 0.026f })
                {
                    b.AddBox(new Vector3(cx, 0.008f, -D / 2 - 0.01f), new Vector3(0.026f, 0.0015f, 0.012f), Pal.Hex("#8A5A36"));
                    foreach (float lx in new[] { -0.011f, 0.011f }) b.AddBox(new Vector3(cx + lx, 0.004f, -D / 2 - 0.01f), new Vector3(0.0014f, 0.008f, 0.01f), Pal.Hex("#6E4A33"));
                    for (int k = 0; k < 3; k++)
                    {
                        var crate = new Vector3(cx - 0.0085f + k * 0.0085f, 0.0105f, -D / 2 - 0.01f);
                        b.AddBox(crate, new Vector3(0.0078f, 0.0035f, 0.011f), Pal.Hex("#C9A06A"));
                        var c = produce[(k + (cx > 0f ? 3 : 0)) % produce.Length];
                        for (int f = 0; f < 4; f++)
                            b.AddBlob(crate + new Vector3((f % 2 - 0.5f) * 0.0034f, 0.0024f, (f / 2 - 0.5f) * 0.0046f), Vector3.one * 0.0021f, 0, 0.1f, k * 7 + f, (_, __) => c);
                    }
                }
            }
            return H + R;
        }

        private static float School(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.15f, D = 0.078f, H = 0.065f, R = 0.045f;
            s.Plaster = Pal.Hex("#E8C9A0"); s.Roof = Pal.RoofTerracotta;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, s.Plaster);
            Windows(b, ref rng, s, -D / 2, new[] { -0.058f, -0.036f, -0.014f, 0.014f }, 0.03f, 0.016f, 0.026f, false, true);
            Windows(b, ref rng, s, D / 2, new[] { -0.05f, -0.02f, 0.01f, 0.04f }, 0.03f, 0.016f, 0.026f, false, false);
            SideWindows(b, ref rng, s, W / 2, new[] { 0f }, 0.03f, 0.016f, 0.024f);
            Wall(b, new Vector3(0.05f, 0.004f, -D / 2), Vector3.back); Door(b, ref rng, 0.02f, 0.034f, s, true, true); b.Pop();
            Board(b, new Vector3(-0.022f, 0.056f, -D / 2 - 0.001f), 0.06f, 0.008f, Pal.Hex("#3C6A9A"), Pal.Hex("#F7F1E6"));
            StoryRoof(b, ref rng, W, D, H, R, 0.01f, 0.003f, s.Roof, s.Plaster, s.Trim, 6, false);
            // Bell turret on the ridge.
            float ty = H + R - 0.004f;
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.007f, ty + 0.009f, sz * 0.007f), new Vector3(0.0018f, 0.018f, 0.0018f), Pal.Hex("#F7F1E6"));
            b.AddBlob(new Vector3(0f, ty + 0.01f, 0f), new Vector3(0.0045f, 0.005f, 0.0045f), 1, 0f, 8, (d, _) => Pal.Brass);
            b.AddCylinder(new Vector3(0f, ty + 0.018f, 0f), 0.012f, 0f, 0.014f, 4, s.Roof, Pal.Shade(s.Roof, 1.2f), false, true, false, Mathf.PI / 4f);
            // Schoolyard: a hopscotch, a ball and a low fence.
            for (int k = 0; k < 5; k++) b.AddFace(new Vector3(-0.035f + k * 0.007f, 0.0006f, -D / 2 - 0.03f), Vector3.up, Vector3.forward, 0.003f, 0.003f, Pal.Hex("#F7F1E6"));
            b.AddBlob(new Vector3(0.01f, 0.0025f, -D / 2 - 0.035f), Vector3.one * 0.0025f, 1, 0f, 9, (d, _) => d.y > 0f ? Pal.Hex("#E25B4F") : Pal.Hex("#F7F1E6"));
            for (float x = -W / 2; x <= W / 2 + 0.001f; x += 0.01f)
            {
                if (Mathf.Abs(x - 0.05f) < 0.015f) continue;
                b.AddBox(new Vector3(x, 0.004f, -D / 2 - 0.055f), new Vector3(0.0016f, 0.008f, 0.0016f), Pal.Hex("#F7F1E6"));
            }
            b.AddBox(new Vector3(-0.012f, 0.006f, -D / 2 - 0.055f), new Vector3(W - 0.03f, 0.0012f, 0.0012f), Pal.Hex("#F7F1E6"));
            return H + R + 0.03f;
        }

        private static float FireStation(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.11f, D = 0.085f, H = 0.07f, R = 0.03f;
            var brick = Pal.Hex("#B5503A"); s.Plaster = brick; s.Roof = Pal.RoofSlate; s.Trim = Pal.Hex("#F7F1E6");
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, brick);
            for (float y = 0.008f; y < H; y += 0.005f) b.AddBox(new Vector3(0f, y, 0f), new Vector3(W + 0.0008f, 0.0005f, D + 0.0008f), Pal.Shade(brick, 0.85f));
            // Two arched engine doors: one open with the red fire engine inside.
            foreach (int k in new[] { 0, 1 })
            {
                float x = -0.02f + k * 0.04f;
                b.Push(new Vector3(x, 0.018f, -D / 2 - 0.0006f));
                b.AddFace(Vector3.zero, Vector3.back, Vector3.up, 0.016f, 0.018f, k == 0 ? Pal.Hex("#2A2224") : Pal.Hex("#A33A2E"));
                b.AddBox(new Vector3(0f, 0.019f, -0.001f), new Vector3(0.036f, 0.003f, 0.002f), s.Trim);
                if (k == 1) for (int p = -3; p <= 3; p++) b.AddBox(new Vector3(p * 0.0045f, 0f, -0.001f), new Vector3(0.0006f, 0.034f, 0.0006f), Pal.Shade(Pal.Hex("#A33A2E"), 0.7f));
                b.Pop();
            }
            // The engine nosing out of the open door.
            var red = Pal.Hex("#D7382E");
            b.AddBox(new Vector3(-0.02f, 0.012f, -D / 2 + 0.004f), new Vector3(0.026f, 0.017f, 0.03f), red, red, Pal.TrainBlack);
            b.AddFace(new Vector3(-0.02f, 0.016f, -D / 2 - 0.0112f), Vector3.back, Vector3.up, 0.01f, 0.004f, Pal.Glow(Pal.Hex("#CFE6F2")));
            foreach (int sx in new[] { -1, 1 }) b.AddFace(new Vector3(-0.02f + sx * 0.009f, 0.007f, -D / 2 - 0.0112f), Vector3.back, Vector3.up, 0.0018f, 0.0015f, Pal.Glow(Pal.Hex("#FFF2C8")));
            b.AddBox(new Vector3(-0.02f, 0.022f, -D / 2 + 0.004f), new Vector3(0.012f, 0.0016f, 0.03f), Pal.Hex("#C9CDD2"));
            b.AddBlob(new Vector3(-0.02f, 0.0215f, -D / 2 - 0.006f), Vector3.one * 0.0016f, 0, 0f, 3, (_, __) => Pal.Glow(Pal.Hex("#4A8BFF")));
            Windows(b, ref rng, s, -D / 2, new[] { -0.02f, 0.02f }, 0.054f, 0.014f, 0.014f, false, true);
            Windows(b, ref rng, s, D / 2, new[] { -0.025f, 0.025f }, 0.04f, 0.014f, 0.02f, false, false);
            Board(b, new Vector3(0f, 0.042f, -D / 2 - 0.001f), 0.05f, 0.007f, Pal.Hex("#F7F1E6"), Pal.Hex("#A33A2E"));
            StoryRoof(b, ref rng, W, D, H, R, 0.008f, 0.002f, s.Roof, brick, s.Trim, 5, false);
            // The hose tower.
            b.AddBox(new Vector3(W / 2 - 0.012f, 0.065f, D / 2 - 0.014f), new Vector3(0.024f, 0.13f, 0.024f), brick, brick, brick);
            b.AddCylinder(new Vector3(W / 2 - 0.012f, 0.13f, D / 2 - 0.014f), 0.02f, 0f, 0.022f, 4, s.Roof, Pal.Shade(s.Roof, 1.2f), false, true, false, Mathf.PI / 4f);
            b.AddFace(new Vector3(W / 2 - 0.012f, 0.112f, D / 2 - 0.0265f), Vector3.back, Vector3.up, 0.005f, 0.008f, Pal.Glow(Pal.WindowGlow));
            b.AddBlob(new Vector3(W / 2 - 0.012f, 0.088f, D / 2 - 0.0265f), new Vector3(0.006f, 0.006f, 0.001f), 1, 0f, 4, (d, _) => Pal.Brass);
            return 0.155f;
        }

        private static float FountainSquare(MeshBuilder b, ref Rng rng)
        {
            // Cobbled paving, a round stone basin, the water and a tiered centrepiece.
            b.AddCylinder(new Vector3(0f, -0.004f, 0f), 0.043f, 0.042f, 0.005f, 24, Pal.StoneDark, Pal.Hex("#C9BBA3"), true);
            for (int i = 0; i < 40; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), r = Mathf.Sqrt(rng.Value) * 0.04f;
                if (r < 0.03f) continue;
                b.AddBox(new Vector3(Mathf.Cos(a) * r, 0.0012f, Mathf.Sin(a) * r), new Vector3(0.004f, 0.0006f, 0.004f), Pal.Jitter(Pal.Stone, ref rng, 0.3f));
            }
            b.AddCylinder(Vector3.zero, 0.03f, 0.03f, 0.009f, 24, Pal.Stone, Pal.Hex("#D9CDB8"), false);
            b.AddCylinder(new Vector3(0f, 0.009f, 0f), 0.031f, 0.031f, 0.0018f, 24, Pal.Hex("#D9CDB8"), Pal.Hex("#E9E0CF"), true);
            b.AddDisc(new Vector3(0f, 0.0075f, 0f), 0.027f, 24, Pal.Hex("#5FA8C8"), true);
            for (int i = 0; i < 10; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), r = rng.Range(0.008f, 0.024f);
                b.AddDisc(new Vector3(Mathf.Cos(a) * r, 0.0078f, Mathf.Sin(a) * r), 0.0018f, 8, Pal.Glow(Pal.Hex("#CFEFFF")), true);
            }
            b.AddCylinder(new Vector3(0f, 0.007f, 0f), 0.0045f, 0.0035f, 0.022f, 10, Pal.Stone, Pal.Hex("#D9CDB8"), false);
            b.AddCylinder(new Vector3(0f, 0.026f, 0f), 0.004f, 0.013f, 0.004f, 14, Pal.Stone, Pal.Hex("#D9CDB8"), true);
            b.AddCylinder(new Vector3(0f, 0.03f, 0f), 0.002f, 0.0015f, 0.009f, 8, Pal.Stone, Pal.Hex("#D9CDB8"), false);
            b.AddBlob(new Vector3(0f, 0.042f, 0f), new Vector3(0.0032f, 0.004f, 0.0032f), 1, 0.2f, 3, (_, __) => Pal.Glow(Pal.Hex("#E6F6FF")));
            // Splashing water falling from the bowl.
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                for (int k = 0; k < 3; k++)
                    b.AddBlob(new Vector3(Mathf.Cos(a) * (0.0135f + k * 0.0025f), 0.026f - k * 0.0065f, Mathf.Sin(a) * (0.0135f + k * 0.0025f)), Vector3.one * (0.0013f - k * 0.0002f), 0, 0f, i * 3 + k, (_, __) => Pal.Glow(Pal.Hex("#DDF2FF")));
            }
            // Flower tubs around the rim.
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * 0.038f, 0f, Mathf.Sin(a) * 0.038f);
                b.AddCylinder(p, 0.0035f, 0.0042f, 0.005f, 8, Pal.Hex("#B5603E"), Pal.Hex("#C8754B"), true);
                var c = new[] { Pal.FlowerRed, Pal.FlowerPink, Pal.FlowerYellow, Pal.FlowerLilac }[i];
                b.AddBlob(p + Vector3.up * 0.007f, new Vector3(0.0042f, 0.003f, 0.0042f), 1, 0.25f, i, (d, _) => d.y > 0.3f ? c : Pal.LeafMid);
            }
            return 0.05f;
        }

        // ---------------------------------------------------------------- farm

        private static float Barn(MeshBuilder b, ref Rng rng, int style)
        {
            const float W = 0.12f, D = 0.09f, H = 0.055f;
            var red = style == 0 ? Pal.Hex("#B5403A") : Pal.Hex("#8A6A4A"); var white = Pal.Hex("#F7F1E6"); var roof = Pal.Hex("#5A5A62");
            b.AddBox(new Vector3(0f, -0.01f, 0f), new Vector3(W + 0.004f, 0.026f, D + 0.004f), Pal.StoneDark, Pal.Stone, Pal.StoneDark);
            b.AddBox(new Vector3(0f, H / 2 + 0.002f, 0f), new Vector3(W, H, D), red, red, red);
            for (float x = -W / 2 + 0.005f; x < W / 2; x += 0.006f)
                foreach (int side in new[] { -1, 1 }) b.AddBox(new Vector3(x, H / 2 + 0.002f, side * (D / 2 + 0.0002f)), new Vector3(0.0005f, H, 0.0004f), Pal.Shade(red, 0.8f));
            // Gambrel roof: four slopes, extruded along X, with gable ends filled.
            var profile = new[] { new Vector2(-D / 2 - 0.008f, H - 0.004f), new Vector2(-D * 0.3f, H + 0.026f), new Vector2(0f, H + 0.042f), new Vector2(D * 0.3f, H + 0.026f), new Vector2(D / 2 + 0.008f, H - 0.004f) };
            var path = new[] { new Vector3(-W / 2 - 0.006f, 0f, 0f), new Vector3(W / 2 + 0.006f, 0f, 0f) };
            b.AddExtrusion(path, new[] { Vector3.back, Vector3.back }, new[] { Vector3.up, Vector3.up }, profile, new[] { roof, Pal.Shade(roof, 1.2f), Pal.Shade(roof, 1.3f), Pal.Shade(roof, 1.2f), roof }, false);
            b.AddExtrusion(path, new[] { Vector3.forward, Vector3.forward }, new[] { Vector3.up, Vector3.up }, System.Array.ConvertAll(profile, p => new Vector2(-p.x, p.y - 0.0018f)), new[] { roof, roof, roof, roof, roof }, false);
            foreach (int e in new[] { -1, 1 })
            {
                var c = new Vector3(e * W / 2, H, 0f);
                for (int i = 0; i < profile.Length - 1; i++)
                    b.AddTriangleOutward(c, new Vector3(e * W / 2, profile[i].y, -profile[i].x * 0.93f), new Vector3(e * W / 2, profile[i + 1].y, -profile[i + 1].x * 0.93f), c - Vector3.right * e * 0.02f, red);
            }
            // Big doors with white cross bracing, a hay loft door with hay.
            b.Push(new Vector3(0f, 0.002f, -D / 2 - 0.001f));
            b.AddBox(new Vector3(0f, 0.018f, 0f), new Vector3(0.042f, 0.036f, 0.0012f), Pal.Shade(red, 0.9f));
            b.AddBox(new Vector3(0f, 0.018f, -0.0008f), new Vector3(0.044f, 0.038f, 0.0006f), white);
            b.AddBox(new Vector3(0f, 0.018f, -0.0012f), new Vector3(0.039f, 0.033f, 0.0006f), Pal.Shade(red, 0.9f));
            foreach (int sx in new[] { -1, 1 })
            {
                b.Push(new Vector3(sx * 0.0105f, 0.018f, -0.0016f), Quaternion.Euler(0f, 0f, sx * 58f));
                b.AddBox(Vector3.zero, new Vector3(0.039f, 0.002f, 0.0006f), white);
                b.Pop();
            }
            b.AddBox(new Vector3(0f, 0.018f, -0.0016f), new Vector3(0.0018f, 0.033f, 0.0006f), white);
            b.AddBox(new Vector3(0f, 0.052f, 0f), new Vector3(0.016f, 0.014f, 0.0012f), Pal.Hex("#2A2224"));
            b.AddBox(new Vector3(0f, 0.052f, -0.0008f), new Vector3(0.018f, 0.016f, 0.0006f), white);
            b.AddBlob(new Vector3(0f, 0.047f, -0.002f), new Vector3(0.007f, 0.003f, 0.003f), 1, 0.4f, 2, (_, __) => Pal.Thatch);
            b.Pop();
            b.AddBlob(new Vector3(0f, H + 0.046f, 0f), new Vector3(0.006f, 0.004f, 0.006f), 1, 0f, 5, (_, __) => Pal.Hex("#F7F1E6"));
            WeatherVane(b, new Vector3(0f, H + 0.048f, 0f));
            // Hay bales and a cart wheel against the wall.
            CylinderX(b, new Vector3(W / 2 + 0.012f, 0.007f, -0.02f), 0.007f, 0.012f, Pal.Thatch, Pal.Hex("#E8C46A"));
            CylinderX(b, new Vector3(W / 2 + 0.012f, 0.007f, 0.0f), 0.007f, 0.012f, Pal.Thatch, Pal.Hex("#E8C46A"));
            return H + 0.05f;
        }

        private static float WindmillTower(MeshBuilder b, ref Rng rng, Style s)
        {
            var white = Pal.Hex("#F2EEE4");
            b.AddCylinder(new Vector3(0f, -0.01f, 0f), 0.036f, 0.036f, 0.014f, 8, Pal.StoneDark, Pal.Stone, true, false, false);
            b.AddCylinder(Vector3.zero, 0.033f, 0.021f, 0.14f, 8, white, Pal.Shade(white, 0.95f), false, false, false);
            for (int k = 1; k < 4; k++) b.AddCylinder(new Vector3(0f, k * 0.035f, 0f), 0.034f - k * 0.003f, 0.034f - k * 0.003f, 0.0016f, 8, Pal.Timber, Pal.Timber, false, false, false);
            b.Push(new Vector3(0f, 0.002f, -0.031f)); Door(b, ref rng, 0.016f, 0.03f, s, true, false); b.Pop();
            foreach (float y in new[] { 0.06f, 0.1f })
            {
                b.Push(new Vector3(0f, y, -(0.033f - y * 0.086f) - 0.001f), Quaternion.LookRotation(Vector3.back, Vector3.up));
                Window(b, ref rng, 0.009f, 0.012f, s, false, false, true);
                b.Pop();
            }
            // A thatched cap that turns the sails into the wind, and the shaft.
            b.AddBlob(new Vector3(0f, 0.148f, 0f), new Vector3(0.026f, 0.02f, 0.03f), 2, 0.06f, 7, (d, _) => d.y < -0.2f ? Pal.Timber : Color.Lerp(Pal.Shade(Pal.Thatch, 0.75f), Pal.Thatch, d.y * 0.5f + 0.5f), 3f);
            b.Push(new Vector3(0f, 0.15f, -0.02f), Quaternion.Euler(90f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.003f, 0.003f, 0.016f, 8, Pal.Timber, Pal.Timber, true);
            b.Pop();
            // A gallery round the middle.
            b.AddCylinder(new Vector3(0f, 0.07f, 0f), 0.034f, 0.034f, 0.0015f, 12, Pal.Timber, Pal.Timber, true, true, false);
            return 0.17f;
        }

        /// <summary>Four lattice sails in the XY plane around the origin (turning about Z).</summary>
        private static void Sails(MeshBuilder b)
        {
            var wood = Pal.Hex("#7A5236"); var cloth = Pal.Hex("#F2EEE4");
            b.AddBlob(Vector3.zero, Vector3.one * 0.005f, 1, 0f, 3, (_, __) => Pal.Timber);
            for (int k = 0; k < 4; k++)
            {
                b.Push(Vector3.zero, Quaternion.Euler(0f, 0f, k * 90f + 15f));
                b.AddBox(new Vector3(0f, 0.052f, -0.002f), new Vector3(0.003f, 0.1f, 0.002f), wood);
                b.AddBox(new Vector3(0.008f, 0.06f, -0.0025f), new Vector3(0.014f, 0.074f, 0.0008f), cloth);
                for (int i = 0; i < 7; i++) b.AddBox(new Vector3(0.008f, 0.026f + i * 0.0115f, -0.0033f), new Vector3(0.016f, 0.0009f, 0.0008f), wood);
                b.AddBox(new Vector3(0.0155f, 0.06f, -0.0033f), new Vector3(0.0009f, 0.076f, 0.0008f), wood);
                b.Pop();
            }
        }

        private static float SiloTower(MeshBuilder b)
        {
            var metal = Pal.Hex("#C9CDD2"); var band = Pal.Hex("#9AA0A8");
            b.AddCylinder(new Vector3(0f, -0.008f, 0f), 0.03f, 0.03f, 0.01f, 16, Pal.StoneDark, Pal.Stone, true);
            b.AddCylinder(Vector3.zero, 0.026f, 0.026f, 0.15f, 16, metal, Pal.Shade(metal, 1.05f), false);
            for (float y = 0.015f; y < 0.15f; y += 0.018f) b.AddCylinder(new Vector3(0f, y, 0f), 0.0265f, 0.0265f, 0.0016f, 16, band, band, false);
            b.AddBlob(new Vector3(0f, 0.15f, 0f), new Vector3(0.026f, 0.016f, 0.026f), 2, 0f, 4, (d, _) => Color.Lerp(Pal.Hex("#4E6E78"), Pal.Hex("#7A98A0"), d.y));
            b.AddCylinder(new Vector3(0f, 0.164f, 0f), 0.003f, 0.002f, 0.006f, 8, metal, metal, true);
            // A ladder up the side.
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.004f, 0.075f, -0.0275f), new Vector3(0.0008f, 0.15f, 0.0008f), Pal.TrainBlack);
            for (float y = 0.006f; y < 0.15f; y += 0.006f) b.AddBox(new Vector3(0f, y, -0.0275f), new Vector3(0.008f, 0.0006f, 0.0006f), Pal.TrainBlack);
            return 0.17f;
        }

        private static float GreenhouseGlass(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.1f, D = 0.06f, H = 0.03f;
            var glass = Pal.Hex("#9FD3CF"); var frame = Pal.Hex("#F7F1E6");
            b.AddBox(new Vector3(0f, 0.002f, 0f), new Vector3(W + 0.004f, 0.006f, D + 0.004f), Pal.Brick);
            b.AddBox(new Vector3(0f, H / 2 + 0.004f, 0f), new Vector3(W, H, D), glass, glass, glass);
            foreach (int side in new[] { -1, 1 })
            {
                b.Push(new Vector3(0f, H + 0.004f, side * D / 4), Quaternion.Euler(-side * 32f, 0f, 0f));
                b.AddBox(Vector3.zero, new Vector3(W, 0.0012f, D / 2 * 1.18f), glass);
                for (float x = -W / 2; x <= W / 2 + 0.001f; x += 0.0125f) b.AddBox(new Vector3(x, 0.0008f, 0f), new Vector3(0.0012f, 0.0012f, D / 2 * 1.18f), frame);
                b.Pop();
            }
            foreach (int e in new[] { -1, 1 })
                b.AddTriangleOutward(new Vector3(e * W / 2, H + 0.004f, -D / 2), new Vector3(e * W / 2, H + 0.004f, D / 2), new Vector3(e * W / 2, H + 0.004f + D / 2 * 0.62f, 0f), new Vector3(0f, H, 0f), glass);
            for (float x = -W / 2; x <= W / 2 + 0.001f; x += 0.0125f)
                foreach (int side in new[] { -1, 1 }) b.AddBox(new Vector3(x, H / 2 + 0.004f, side * (D / 2 + 0.0005f)), new Vector3(0.0012f, H, 0.0012f), frame);
            b.AddBox(new Vector3(0f, H + 0.023f, 0f), new Vector3(W + 0.002f, 0.0016f, 0.0016f), frame);
            // Green shapes behind the glass and pots by the door.
            for (int i = 0; i < 6; i++)
            {
                var c = Pal.Jitter(Pal.LeafMid, ref rng, 0.3f);
                b.AddBlob(new Vector3(-0.04f + i * 0.016f, 0.012f, -D / 2 + 0.008f), new Vector3(0.006f, 0.008f, 0.004f), 1, 0.3f, i, (_, __) => c);
            }
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(0.015f + i * 0.008f, 0f, -D / 2 - 0.008f);
                b.AddCylinder(p, 0.0026f, 0.0032f, 0.005f, 8, Pal.Hex("#B5603E"), Pal.Hex("#C8754B"), true);
                b.AddBlob(p + Vector3.up * 0.0075f, Vector3.one * 0.003f, 1, 0.2f, i + 9, (d, _) => d.y > 0.4f ? Pal.FlowerRed : Pal.LeafMid);
            }
            return 0.07f;
        }

        /// <summary>A planted field: wheat (0), sunflowers (1) or lavender (2) in neat rows on tilled soil.</summary>
        private static float Field(MeshBuilder b, ref Rng rng, int style)
        {
            const float W = 0.22f, D = 0.16f;
            b.AddBox(new Vector3(0f, -0.002f, 0f), new Vector3(W, 0.006f, D), Pal.Hex("#7A5A3A"), Pal.Hex("#8E6A44"), Pal.Hex("#5E4430"));
            for (float z = -D / 2 + 0.008f; z < D / 2; z += 0.012f)
            {
                b.AddBox(new Vector3(0f, 0.0012f, z), new Vector3(W - 0.006f, 0.0012f, 0.006f), Pal.Hex("#6E4E32"));
                for (float x = -W / 2 + 0.006f; x < W / 2 - 0.004f; x += style == 1 ? 0.012f : 0.0095f)
                {
                    var p = new Vector3(x + rng.Range(-0.0015f, 0.0015f), 0.002f, z + rng.Range(-0.0015f, 0.0015f));
                    b.Sway = style == 1 ? 0.25f : 0.8f;
                    switch (style)
                    {
                        case 0:
                        {
                            // A clump of ripe wheat: a low-poly golden tuft with a darker base.
                            var gold = Pal.Jitter(Pal.Hex("#E8C46A"), ref rng, 0.15f);
                            b.AddBlob(p + Vector3.up * 0.0068f, new Vector3(0.0048f, 0.0068f, 0.0038f), 0, 0.3f, (int)(x * 9000f + z * 7000f), (d, _) => Color.Lerp(Pal.Shade(gold, 0.72f), gold, d.y * 0.5f + 0.5f));
                            break;
                        }
                        case 1:
                            b.AddCylinder(p, 0.0007f, 0.0006f, 0.014f, 4, Pal.LeafDeep, Pal.LeafMid, false);
                            b.AddBlob(p + new Vector3(0f, 0.01f, 0.002f), new Vector3(0.0035f, 0.0018f, 0.002f), 0, 0f, 1, (_, __) => Pal.LeafMid);
                            b.Push(p + new Vector3(0f, 0.0155f, -0.001f), Quaternion.Euler(-70f, 0f, 0f));
                            b.AddCylinder(Vector3.zero, 0.0042f, 0.0042f, 0.0008f, 8, Pal.Hex("#FFC22E"), Pal.Hex("#FFC22E"), true, true);
                            b.AddDisc(new Vector3(0f, 0.0009f, 0f), 0.0019f, 6, Pal.Hex("#5A3A1A"), true);
                            b.Pop();
                            break;
                        default:
                        {
                            // A lavender bush: grey-green mound with purple spikes on top.
                            var purple = Pal.Jitter(Pal.FlowerLilac, ref rng, 0.12f);
                            b.AddBlob(p + Vector3.up * 0.0035f, new Vector3(0.0045f, 0.0035f, 0.0045f), 0, 0.25f, (int)(x * 7000f + z * 3000f), (_, __) => Pal.Hex("#7E9A6A"));
                            b.AddBlob(p + Vector3.up * 0.0075f, new Vector3(0.004f, 0.003f, 0.004f), 0, 0.4f, (int)(x * 5000f + z * 9000f), (d, _) => Color.Lerp(Pal.Shade(purple, 0.8f), purple, d.y * 0.5f + 0.5f));
                            break;
                        }
                    }
                    b.Sway = 0f;
                }
            }
            // A low fence along two sides and a gate post.
            for (float x = -W / 2; x <= W / 2 + 0.001f; x += 0.014f) b.AddBox(new Vector3(x, 0.005f, -D / 2 - 0.004f), new Vector3(0.0016f, 0.011f, 0.0016f), Pal.Hex("#8A5A36"));
            b.AddBox(new Vector3(0f, 0.008f, -D / 2 - 0.004f), new Vector3(W, 0.0014f, 0.0012f), Pal.Hex("#A8743E"));
            b.AddBox(new Vector3(0f, 0.004f, -D / 2 - 0.004f), new Vector3(W, 0.0014f, 0.0012f), Pal.Hex("#A8743E"));
            return 0.02f;
        }

        private static float VegetableGarden(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.14f, D = 0.11f;
            b.AddBox(new Vector3(0f, -0.002f, 0f), new Vector3(W, 0.006f, D), Pal.Hex("#6E8E3E"), Pal.Hex("#7FA04A"), Pal.Hex("#5E4430"));
            var crops = new[] { "cabbage", "carrot", "lettuce", "pumpkin", "beans", "cabbage" };
            for (int bed = 0; bed < 6; bed++)
            {
                float x = -W / 2 + 0.014f + (bed % 3) * 0.044f, z = bed < 3 ? -0.024f : 0.026f;
                b.AddBox(new Vector3(x + 0.014f, 0.0018f, z), new Vector3(0.034f, 0.004f, 0.042f), Pal.Hex("#6E4E32"), Pal.Hex("#7A5A3A"), Pal.Hex("#5E4430"));
                for (int i = 0; i < 4; i++)
                for (int j = 0; j < 2; j++)
                {
                    var p = new Vector3(x + 0.006f + j * 0.016f, 0.004f, z - 0.015f + i * 0.01f);
                    switch (crops[bed])
                    {
                        case "cabbage": b.AddBlob(p + Vector3.up * 0.002f, new Vector3(0.0042f, 0.0034f, 0.0042f), 1, 0.25f, i * 3 + j, (d, _) => Color.Lerp(Pal.Hex("#5E8E4A"), Pal.Hex("#A8D07A"), d.y * 0.5f + 0.5f)); break;
                        case "carrot": b.AddBlob(p, new Vector3(0.0014f, 0.0008f, 0.0014f), 0, 0f, i, (_, __) => Pal.Hex("#F2853A")); b.AddBlob(p + Vector3.up * 0.0035f, new Vector3(0.002f, 0.003f, 0.002f), 0, 0.4f, i + 1, (_, __) => Pal.LeafLight); break;
                        case "lettuce": b.AddBlob(p + Vector3.up * 0.0015f, new Vector3(0.0035f, 0.0025f, 0.0035f), 1, 0.4f, i * 5 + j, (_, __) => Pal.Hex("#9CCB5A")); break;
                        case "pumpkin": if ((i + j) % 2 == 0) b.AddBlob(p + Vector3.up * 0.0025f, new Vector3(0.0046f, 0.0034f, 0.0046f), 1, 0.15f, i, (d, _) => Color.Lerp(Pal.Hex("#C8601E"), Pal.Hex("#F28A2E"), d.y * 0.5f + 0.5f)); else b.AddBlob(p + Vector3.up * 0.0015f, new Vector3(0.004f, 0.0018f, 0.004f), 0, 0.3f, i, (_, __) => Pal.LeafMid); break;
                        default: b.AddCylinder(p, 0.0006f, 0.0004f, 0.016f, 4, Pal.Hex("#A8743E"), Pal.Hex("#A8743E"), false); b.AddBlob(p + Vector3.up * 0.009f, new Vector3(0.0022f, 0.006f, 0.0022f), 1, 0.4f, i + j, (_, __) => Pal.LeafMid); break;
                    }
                }
            }
            // A scarecrow and a picket fence.
            var sc = new Vector3(W / 2 - 0.008f, 0f, D / 2 - 0.01f);
            b.AddBox(sc + new Vector3(0f, 0.014f, 0f), new Vector3(0.0016f, 0.028f, 0.0016f), Pal.Timber);
            b.AddBox(sc + new Vector3(0f, 0.022f, 0f), new Vector3(0.02f, 0.0016f, 0.0016f), Pal.Timber);
            b.AddBox(sc + new Vector3(0f, 0.019f, 0f), new Vector3(0.008f, 0.01f, 0.004f), Pal.Hex("#4A7FB8"));
            b.AddBlob(sc + new Vector3(0f, 0.03f, 0f), Vector3.one * 0.0035f, 1, 0.1f, 1, (_, __) => Pal.Thatch);
            b.AddCylinder(sc + new Vector3(0f, 0.032f, 0f), 0.006f, 0.006f, 0.0008f, 10, Pal.Hex("#C99A48"), Pal.Hex("#C99A48"), true, true);
            b.AddCylinder(sc + new Vector3(0f, 0.032f, 0f), 0.003f, 0.0026f, 0.004f, 8, Pal.Hex("#C99A48"), Pal.Hex("#D9AA58"), true);
            for (float x = -W / 2; x <= W / 2 + 0.001f; x += 0.006f)
                foreach (int side in new[] { -1, 1 }) b.AddBox(new Vector3(x, 0.004f, side * (D / 2 + 0.002f)), new Vector3(0.0018f, 0.009f, 0.0008f), Pal.Hex("#F7F1E6"));
            return 0.04f;
        }

        // ---------------------------------------------------------------- fun

        private static float CarouselBase(MeshBuilder b)
        {
            b.AddCylinder(new Vector3(0f, -0.004f, 0f), 0.061f, 0.06f, 0.008f, 24, Pal.StoneDark, Pal.Hex("#C9BBA3"), true);
            for (int i = 0; i < 3; i++)
            {
                float a = -Mathf.PI / 2f + (i - 1) * 0.12f;
                b.AddBox(new Vector3(Mathf.Cos(a) * 0.062f, 0.002f, Mathf.Sin(a) * 0.062f), new Vector3(0.008f, 0.003f, 0.006f), Pal.Stone);
            }
            // A ticket booth.
            b.AddBox(new Vector3(0.062f, 0.012f, -0.04f), new Vector3(0.014f, 0.024f, 0.014f), Pal.Hex("#E25B4F"), Pal.Hex("#F7F1E6"), Pal.Hex("#E25B4F"));
            b.AddCylinder(new Vector3(0.062f, 0.024f, -0.04f), 0.011f, 0f, 0.01f, 8, Pal.Hex("#F7F1E6"), Pal.Hex("#E25B4F"), false, true);
            b.AddFace(new Vector3(0.062f, 0.016f, -0.0472f), Vector3.back, Vector3.up, 0.004f, 0.003f, Pal.Glow(Pal.WindowGlow));
            return 0.13f;
        }

        /// <summary>Everything that turns: platform, horses on poles, the decorated column and the striped canopy.</summary>
        private static void CarouselTop(MeshBuilder b, ref Rng rng)
        {
            b.AddCylinder(new Vector3(0f, 0.004f, 0f), 0.056f, 0.056f, 0.004f, 24, Pal.Hex("#8A5A36"), Pal.Hex("#B98A5E"), true);
            b.AddCylinder(new Vector3(0f, 0.008f, 0f), 0.012f, 0.012f, 0.07f, 12, Pal.Hex("#E2B04A"), Pal.Hex("#F2D07A"), false);
            for (float y = 0.015f; y < 0.075f; y += 0.012f) b.AddCylinder(new Vector3(0f, y, 0f), 0.0125f, 0.0125f, 0.004f, 12, Pal.Glow(Pal.Hex("#FFF4C8")), Pal.Glow(Pal.Hex("#FFF4C8")), false);
            var horseColours = new[] { "#F7F1E6", "#5A3826", "#E8C46A", "#9AA0A8", "#F7F1E6", "#3A2E2E" };
            for (int i = 0; i < 6; i++)
            {
                float a = (i + 0.5f) / 6f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Sin(a) * 0.047f, 0.008f, -Mathf.Cos(a) * 0.047f);
                float yaw = -a * Mathf.Rad2Deg + 90f;
                b.AddCylinder(p, 0.0008f, 0.0008f, 0.07f, 6, Pal.Brass, Pal.Brass, false);
                b.Push(p + Vector3.up * (0.011f + (i % 2) * 0.004f), Quaternion.Euler(0f, yaw, 0f));
                var c = Pal.Hex(horseColours[i]);
                b.AddBlob(new Vector3(0f, 0f, 0f), new Vector3(0.0035f, 0.0035f, 0.0075f), 1, 0f, i, (_, __) => c);
                b.AddBlob(new Vector3(0f, 0.005f, 0.0065f), new Vector3(0.0022f, 0.0045f, 0.0025f), 1, 0f, i + 10, (_, __) => c);
                b.AddBlob(new Vector3(0f, 0.008f, 0.009f), new Vector3(0.0018f, 0.0018f, 0.003f), 0, 0f, i + 20, (_, __) => c);
                b.AddBlob(new Vector3(0f, 0.0075f, 0.0045f), new Vector3(0.0008f, 0.004f, 0.0018f), 0, 0.2f, i + 30, (_, __) => Pal.Hex("#E25B4F"));
                b.AddBox(new Vector3(0f, 0.0035f, -0.001f), new Vector3(0.0072f, 0.0016f, 0.005f), Pal.Hex("#E25B4F"));
                foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                    b.AddCylinder(new Vector3(sx * 0.0018f, -0.0075f, sz * 0.0045f), 0.0007f, 0.0008f, 0.007f, 4, c, c, false);
                b.Pop();
            }
            // Canopy: a striped cone with a scalloped rim and a little flag on top.
            b.AddCylinder(new Vector3(0f, 0.078f, 0f), 0.06f, 0.06f, 0.008f, 24, Pal.Hex("#E2B04A"), Pal.Hex("#E2B04A"), false);
            for (int k = 0; k < 12; k++)
            {
                b.Push(new Vector3(0f, 0.086f, 0f), Quaternion.Euler(0f, k * 30f, 0f));
                b.AddTriangleOutward(new Vector3(0f, 0.034f, 0f), new Vector3(-0.0161f, 0f, 0.06f), new Vector3(0.0161f, 0f, 0.06f), new Vector3(0f, -0.02f, 0f), k % 2 == 0 ? Pal.Hex("#E25B4F") : Pal.Hex("#F7F1E6"));
                b.AddBlob(new Vector3(0f, -0.009f, 0.061f), new Vector3(0.0075f, 0.004f, 0.001f), 0, 0f, k, (_, __) => k % 2 == 0 ? Pal.Hex("#E25B4F") : Pal.Hex("#F7F1E6"));
                b.AddBlob(new Vector3(0f, -0.004f, 0.0605f), Vector3.one * 0.0012f, 0, 0f, k + 40, (_, __) => Pal.Glow(Pal.Hex("#FFF4C8")));
                b.Pop();
            }
            Flag(b, new Vector3(0f, 0.119f, 0f), 0.014f, Pal.Hex("#4A7FB8"));
        }

        private static float WheelStand(MeshBuilder b)
        {
            var steel = Pal.Hex("#F2EEE4");
            b.AddBox(new Vector3(0f, 0.001f, 0f), new Vector3(0.1f, 0.004f, 0.06f), Pal.Stone);
            foreach (int sz in new[] { -1, 1 })
            foreach (int sx in new[] { -1, 1 })
            {
                var foot = new Vector3(sx * 0.05f, 0f, sz * 0.03f);
                var hub = new Vector3(0f, 0.16f, sz * 0.026f);
                var dir = hub - foot;
                b.Push(foot, Quaternion.FromToRotation(Vector3.up, dir.normalized));
                b.AddCylinder(Vector3.zero, 0.0028f, 0.0022f, dir.magnitude, 6, steel, steel, false);
                b.Pop();
            }
            b.Push(new Vector3(0f, 0.16f, -0.032f), Quaternion.Euler(90f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.004f, 0.004f, 0.064f, 10, Pal.TrainBlack, Pal.TrainBlack, true, true);
            b.Pop();
            // Boarding platform with steps and a ticket booth.
            b.AddBox(new Vector3(0f, 0.012f, -0.035f), new Vector3(0.03f, 0.004f, 0.02f), Pal.Hex("#8A5A36"));
            for (int k = 0; k < 3; k++) b.AddBox(new Vector3(0f, 0.0025f + k * 0.0035f, -0.05f + k * 0.004f), new Vector3(0.02f, 0.003f, 0.004f), Pal.Stone);
            b.AddBox(new Vector3(0.06f, 0.012f, -0.035f), new Vector3(0.014f, 0.024f, 0.014f), Pal.Hex("#4A7FB8"), Pal.Hex("#F7F1E6"), Pal.Hex("#4A7FB8"));
            b.AddFace(new Vector3(0.06f, 0.016f, -0.0422f), Vector3.back, Vector3.up, 0.004f, 0.003f, Pal.Glow(Pal.WindowGlow));
            return 0.3f;
        }

        /// <summary>The wheel itself (in the XY plane around the origin): two rims with lights, spokes and the hub.</summary>
        private static void WheelRim(MeshBuilder b)
        {
            var steel = Pal.Hex("#F2EEE4");
            const int n = 32; const float R = 0.13f;
            foreach (float z in new[] { -0.02f, 0.02f })
            {
                for (int i = 0; i < n; i++)
                {
                    float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                    var p0 = new Vector3(Mathf.Cos(a0) * R, Mathf.Sin(a0) * R, z); var p1 = new Vector3(Mathf.Cos(a1) * R, Mathf.Sin(a1) * R, z);
                    b.Push((p0 + p1) * 0.5f, Quaternion.LookRotation(Vector3.forward, (p1 - p0).normalized));
                    b.AddBox(Vector3.zero, new Vector3(0.003f, (p1 - p0).magnitude * 1.02f, 0.003f), steel);
                    b.Pop();
                    if (i % 2 == 0) b.AddBlob(p0 + new Vector3(0f, 0f, z > 0f ? 0.002f : -0.002f), Vector3.one * 0.0016f, 0, 0f, i, (_, __) => Pal.Glow(i % 4 == 0 ? Pal.Hex("#FFE27A") : Pal.Hex("#FF8FB0")));
                }
                for (int i = 0; i < 8; i++)
                {
                    float a = i / 8f * Mathf.PI * 2f;
                    var tip = new Vector3(Mathf.Cos(a) * R, Mathf.Sin(a) * R, z);
                    b.Push(tip * 0.5f, Quaternion.LookRotation(Vector3.forward, tip.normalized));
                    b.AddBox(Vector3.zero, new Vector3(0.0016f, R, 0.0016f), steel);
                    b.Pop();
                }
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                b.AddBox(new Vector3(Mathf.Cos(a) * R, Mathf.Sin(a) * R, 0f), new Vector3(0.002f, 0.002f, 0.042f), steel);
            }
            b.AddBlob(Vector3.zero, new Vector3(0.012f, 0.012f, 0.026f), 1, 0f, 3, (_, __) => Pal.Hex("#E25B4F"));
        }

        private static float PlaygroundYard(MeshBuilder b, ref Rng rng)
        {
            b.AddBox(new Vector3(0f, -0.002f, 0f), new Vector3(0.13f, 0.005f, 0.1f), Pal.Hex("#D9B27A"), Pal.Hex("#E8C894"), Pal.Hex("#B98A5E"));
            var red = Pal.Hex("#E25B4F"); var blue = Pal.Hex("#4A7FB8"); var yellow = Pal.Hex("#FFD34D");
            // Swings (left).
            var sw = new Vector3(-0.035f, 0f, -0.01f);
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                b.Push(sw + new Vector3(sx * 0.022f, 0f, sz * 0.008f), Quaternion.Euler(-sz * 12f, 0f, 0f));
                b.AddCylinder(Vector3.zero, 0.0012f, 0.0012f, 0.034f, 6, blue, blue, false);
                b.Pop();
            }
            b.Push(sw + new Vector3(-0.024f, 0.033f, 0f), Quaternion.Euler(0f, 0f, -90f));
            b.AddCylinder(Vector3.zero, 0.0013f, 0.0013f, 0.048f, 6, red, red, true, true);
            b.Pop();
            foreach (float x in new[] { -0.01f, 0.01f })
            {
                foreach (int sz in new[] { -1, 1 }) b.AddBox(sw + new Vector3(x + sz * 0.003f, 0.022f, 0f), new Vector3(0.0004f, 0.022f, 0.0004f), Pal.TrainBlack);
                b.AddBox(sw + new Vector3(x, 0.011f, 0f), new Vector3(0.008f, 0.0012f, 0.004f), yellow);
            }
            // Slide (right): a ladder, a little platform and a chute.
            var sl = new Vector3(0.03f, 0f, 0.012f);
            foreach (int sz in new[] { -1, 1 }) b.AddBox(sl + new Vector3(-0.012f, 0.012f, sz * 0.004f), new Vector3(0.0012f, 0.024f, 0.0012f), red);
            for (float y = 0.004f; y < 0.024f; y += 0.004f) b.AddBox(sl + new Vector3(-0.012f, y, 0f), new Vector3(0.0008f, 0.0008f, 0.008f), Pal.TrainBlack);
            b.AddBox(sl + new Vector3(-0.007f, 0.024f, 0f), new Vector3(0.01f, 0.0016f, 0.01f), red);
            b.Push(sl + new Vector3(0.008f, 0.0125f, 0f), Quaternion.Euler(0f, 0f, 36f));
            b.AddBox(Vector3.zero, new Vector3(0.03f, 0.0012f, 0.009f), yellow);
            foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(0f, 0.0018f, sz * 0.0045f), new Vector3(0.03f, 0.003f, 0.0008f), yellow);
            b.Pop();
            // Seesaw (back) and a sandpit with a bucket.
            var ss = new Vector3(0f, 0f, 0.035f);
            b.AddBox(ss + new Vector3(0f, 0.004f, 0f), new Vector3(0.004f, 0.008f, 0.004f), blue);
            b.Push(ss + new Vector3(0f, 0.008f, 0f), Quaternion.Euler(0f, 0f, 10f));
            b.AddBox(Vector3.zero, new Vector3(0.05f, 0.0016f, 0.005f), red);
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.02f, 0.003f, 0f), new Vector3(0.0012f, 0.004f, 0.006f), yellow);
            b.Pop();
            b.AddBox(new Vector3(-0.04f, 0.0012f, 0.03f), new Vector3(0.026f, 0.003f, 0.022f), Pal.Hex("#8A5A36"));
            b.AddBox(new Vector3(-0.04f, 0.0018f, 0.03f), new Vector3(0.022f, 0.002f, 0.018f), Pal.Hex("#F2DFA0"));
            b.AddCylinder(new Vector3(-0.037f, 0.003f, 0.028f), 0.0018f, 0.0022f, 0.004f, 8, red, red, false);
            return 0.05f;
        }

        private static float BandstandPavilion(MeshBuilder b, ref Rng rng, int style)
        {
            var white = Pal.Hex("#F7F1E6"); var roof = style == 0 ? Pal.Hex("#5E9A8A") : Pal.Hex("#C4573A");
            b.AddCylinder(new Vector3(0f, -0.006f, 0f), 0.04f, 0.04f, 0.016f, 8, Pal.Stone, Pal.Hex("#D9CDB8"), true, false, false, Mathf.PI / 8f);
            for (int k = 0; k < 8; k++)
            {
                float a = (k + 0.5f) / 8f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * 0.035f, 0.01f, Mathf.Sin(a) * 0.035f);
                b.AddCylinder(p, 0.0013f, 0.0013f, 0.042f, 6, white, white, false);
                if (k != 6) b.AddBox(p + new Vector3(0f, 0.008f, 0f) - p.normalized * 0.0005f, new Vector3(0.002f, 0.0012f, 0.002f), white);
            }
            b.AddCylinder(new Vector3(0f, 0.017f, 0f), 0.0362f, 0.0362f, 0.0012f, 8, white, white, false, false, false, Mathf.PI / 8f);
            b.AddCylinder(new Vector3(0f, 0.052f, 0f), 0.046f, 0f, 0.03f, 8, Pal.Shade(roof, 0.85f), roof, false, true, false, Mathf.PI / 8f);
            b.AddCylinder(new Vector3(0f, 0.05f, 0f), 0.046f, 0.046f, 0.003f, 8, white, white, false, true, false, Mathf.PI / 8f);
            b.AddBlob(new Vector3(0f, 0.084f, 0f), Vector3.one * 0.003f, 1, 0f, 3, (_, __) => Pal.Brass);
            // A little band playing.
            for (int i = 0; i < 3; i++)
            {
                float a = i / 3f * Mathf.PI * 2f + 0.4f;
                var p = new Vector3(Mathf.Cos(a) * 0.016f, 0.01f, Mathf.Sin(a) * 0.016f);
                Person(b, p, Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg + 180f, i * 2 + 1, Color.white, 0.95f);
                b.AddCylinder(p + new Vector3(0f, 0.018f, 0f) - p.normalized * 0.006f, 0.0018f, 0.0035f, 0.006f, 8, Pal.Brass, Pal.Brass, false);
            }
            return 0.09f;
        }

        private static float Ruin(MeshBuilder b, ref Rng rng)
        {
            var stone = Pal.Hex("#B9AE9C"); var dark = Pal.Hex("#8E8474");
            void JaggedWall(Vector3 from, Vector3 to, float h, float t)
            {
                var d = to - from; float len = d.magnitude; var dir = d / len;
                int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.008f));
                for (int i = 0; i < n; i++)
                {
                    float hh = h * (0.55f + 0.45f * Mathf.PerlinNoise(from.x * 50f + i * 0.7f, from.z * 50f)) * (i == 0 || i == n - 1 ? 1f : 1f);
                    var c = from + dir * ((i + 0.5f) * len / n);
                    b.Push(c + Vector3.up * hh / 2, Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up));
                    b.AddBox(Vector3.zero, new Vector3(len / n * 1.02f, hh, t), stone, Pal.Shade(stone, 1.1f), dark);
                    b.Pop();
                    for (float y = 0.008f; y < hh; y += 0.008f) b.AddBox(c + Vector3.up * y, new Vector3(Mathf.Abs(dir.x) * len / n + Mathf.Abs(dir.z) * (t + 0.0008f), 0.0006f, Mathf.Abs(dir.z) * len / n + Mathf.Abs(dir.x) * (t + 0.0008f)), dark);
                }
            }
            b.AddBox(new Vector3(0f, -0.01f, 0f), new Vector3(0.16f, 0.024f, 0.14f), dark, Pal.Hex("#7E9A4A"), dark);
            JaggedWall(new Vector3(-0.07f, 0f, 0.06f), new Vector3(0.05f, 0f, 0.06f), 0.06f, 0.01f);
            JaggedWall(new Vector3(-0.07f, 0f, -0.06f), new Vector3(-0.07f, 0f, 0.06f), 0.05f, 0.01f);
            JaggedWall(new Vector3(-0.07f, 0f, -0.06f), new Vector3(-0.02f, 0f, -0.06f), 0.035f, 0.01f);
            JaggedWall(new Vector3(0.02f, 0f, -0.06f), new Vector3(0.05f, 0f, -0.06f), 0.04f, 0.01f);
            // Gate arch between the front walls.
            for (int k = 0; k < 9; k++)
            {
                float a = k / 8f * Mathf.PI;
                b.Push(new Vector3(Mathf.Cos(a) * 0.022f, 0.04f + Mathf.Sin(a) * 0.016f, -0.06f), Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg));
                b.AddBox(Vector3.zero, new Vector3(0.007f, 0.006f, 0.011f), k % 2 == 0 ? stone : dark);
                b.Pop();
            }
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.022f, 0.02f, -0.06f), new Vector3(0.006f, 0.04f, 0.011f), stone);
            // A round tower, partly fallen, with crenellations where it still stands.
            var tower = new Vector3(0.05f, 0f, 0.02f);
            b.AddCylinder(tower, 0.03f, 0.027f, 0.15f, 14, stone, Pal.Shade(stone, 1.08f), false);
            for (int k = 0; k < 14; k++)
            {
                float a = k / 14f * Mathf.PI * 2f;
                if (a > 2.6f && a < 4.4f) continue;
                if (k % 2 == 0) b.AddBox(tower + new Vector3(Mathf.Cos(a) * 0.026f, 0.155f, Mathf.Sin(a) * 0.026f), new Vector3(0.008f, 0.01f, 0.008f), stone);
            }
            b.AddBlob(tower + new Vector3(0f, 0.15f, 0f), new Vector3(0.026f, 0.004f, 0.026f), 1, 0.2f, 4, (_, __) => Pal.Hex("#6E8E3E"));
            foreach (float y in new[] { 0.05f, 0.1f })
                b.AddFace(tower + new Vector3(-0.0275f, y, -0.008f), new Vector3(-0.96f, 0f, -0.28f), Vector3.up, 0.003f, 0.007f, Pal.Hex("#2A2224"));
            // Ivy and rubble.
            for (int i = 0; i < 14; i++)
            {
                var c = Pal.Jitter(Pal.LeafDeep, ref rng, 0.25f);
                var p = new Vector3(rng.Range(-0.07f, 0.05f), rng.Range(0.005f, 0.045f), 0.066f);
                if (i % 2 == 0) p = new Vector3(-0.076f, rng.Range(0.005f, 0.04f), rng.Range(-0.05f, 0.05f));
                b.AddBlob(p, new Vector3(0.006f, 0.008f, 0.004f), 1, 0.4f, i, (_, __) => c);
            }
            for (int i = 0; i < 8; i++)
            {
                var c = Pal.Jitter(stone, ref rng, 0.2f);
                b.AddBlob(new Vector3(rng.Range(-0.06f, 0.06f), 0.003f, rng.Range(-0.075f, -0.065f) + (i % 2) * 0.13f), new Vector3(0.006f, 0.004f, 0.005f), 1, 0.3f, i + 30, (_, __) => c);
            }
            return 0.17f;
        }

        private static float LookoutTower(MeshBuilder b)
        {
            var wood = Pal.Hex("#8A5A36"); var dark = Pal.Hex("#5E3A22");
            const float h = 0.2f, w = 0.028f;
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
            {
                b.Push(new Vector3(sx * w, 0f, sz * w), Quaternion.Euler(-sz * 4f, 0f, sx * 4f));
                b.AddBox(new Vector3(0f, h / 2, 0f), new Vector3(0.003f, h, 0.003f), wood);
                b.Pop();
            }
            for (int k = 0; k < 5; k++)
            {
                float y = 0.02f + k * 0.04f, ww = w * (1f - y * 0.3f);
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = new Vector3(dx, 0f, dz);
                    b.Push(new Vector3(0f, y + 0.02f, 0f) + n * ww, Quaternion.LookRotation(n, Vector3.up) * Quaternion.Euler(0f, 0f, k % 2 == 0 ? 45f : -45f));
                    b.AddBox(Vector3.zero, new Vector3(ww * 2.6f, 0.0016f, 0.0016f), dark);
                    b.Pop();
                    b.AddBox(new Vector3(0f, y, 0f) + n * ww, new Vector3(dz != 0 ? ww * 2f : 0.0016f, 0.0016f, dx != 0 ? ww * 2f : 0.0016f), dark);
                }
            }
            b.AddBox(new Vector3(0f, h, 0f), new Vector3(0.07f, 0.003f, 0.07f), wood);
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = new Vector3(dx, 0f, dz);
                b.AddBox(new Vector3(0f, h + 0.012f, 0f) + n * 0.034f, new Vector3(dz != 0 ? 0.07f : 0.0016f, 0.0016f, dx != 0 ? 0.07f : 0.0016f), dark);
                for (int k = -3; k <= 3; k++) b.AddBox(new Vector3(0f, h + 0.006f, 0f) + n * 0.034f + new Vector3(dz != 0 ? k * 0.01f : 0f, 0f, dx != 0 ? k * 0.01f : 0f), new Vector3(0.0012f, 0.012f, 0.0012f), dark);
            }
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.032f, h + 0.02f, sz * 0.032f), new Vector3(0.0022f, 0.04f, 0.0022f), wood);
            b.AddCylinder(new Vector3(0f, h + 0.04f, 0f), 0.055f, 0f, 0.03f, 4, Pal.Hex("#5A4A44"), Pal.Hex("#7A6A62"), false, true, false, Mathf.PI / 4f);
            Flag(b, new Vector3(0f, h + 0.068f, 0f), 0.02f, Pal.Hex("#E25B4F"));
            return h + 0.09f;
        }

        private static float Camp(MeshBuilder b, ref Rng rng)
        {
            void Tent(Vector3 at, float yaw, Color c)
            {
                b.Push(at, Quaternion.Euler(0f, yaw, 0f));
                var profile = new[] { new Vector2(-0.014f, 0f), new Vector2(0f, 0.02f), new Vector2(0.014f, 0f) };
                var path = new[] { new Vector3(0f, 0f, -0.018f), new Vector3(0f, 0f, 0.018f) };
                b.AddExtrusion(path, new[] { Vector3.right, Vector3.right }, new[] { Vector3.up, Vector3.up }, profile, new[] { Pal.Shade(c, 0.8f), c, Pal.Shade(c, 0.8f) }, false);
                b.AddTriangleOutward(new Vector3(-0.014f, 0f, 0.018f), new Vector3(0.014f, 0f, 0.018f), new Vector3(0f, 0.02f, 0.018f), Vector3.zero, Pal.Shade(c, 0.9f));
                b.AddTriangleOutward(new Vector3(-0.014f, 0f, -0.018f), new Vector3(0.014f, 0f, -0.018f), new Vector3(0f, 0.02f, -0.018f), Vector3.zero, Pal.Shade(c, 0.9f));
                b.AddFace(new Vector3(0f, 0.006f, -0.0184f), Vector3.back, Vector3.up, 0.004f, 0.006f, Pal.Hex("#2A2224"));
                b.Pop();
            }
            Tent(new Vector3(-0.042f, 0f, 0.03f), 20f, Pal.Hex("#E2853A"));
            Tent(new Vector3(0.045f, 0f, 0.028f), -25f, Pal.Hex("#4A9A9A"));
            Tent(new Vector3(0.0f, 0f, 0.05f), 0f, Pal.Hex("#5E9A63"));
            // Campfire: a stone ring, crossed logs and a glowing flame.
            var fire = new Vector3(0.01f, 0f, 0.005f);
            for (int k = 0; k < 9; k++)
            {
                float a = k / 9f * Mathf.PI * 2f;
                b.AddBlob(fire + new Vector3(Mathf.Cos(a) * 0.009f, 0.0015f, Mathf.Sin(a) * 0.009f), Vector3.one * 0.0022f, 0, 0.3f, k, (_, __) => Pal.Stone);
            }
            foreach (float yaw in new[] { 30f, 100f, 170f })
            {
                b.Push(fire + Vector3.up * 0.002f, Quaternion.Euler(0f, yaw, 0f));
                CylinderX(b, Vector3.zero, 0.0013f, 0.012f, Pal.BarkDark, Pal.Hex("#3A2420"));
                b.Pop();
            }
            b.AddBlob(fire + new Vector3(0f, 0.006f, 0f), new Vector3(0.004f, 0.007f, 0.004f), 1, 0.3f, 5, (d, _) => Pal.Glow(Color.Lerp(Pal.Hex("#FF7A2E"), Pal.Hex("#FFE27A"), d.y * 0.5f + 0.5f)));
            // Log seats around it.
            for (int k = 0; k < 2; k++)
            {
                b.Push(fire + new Vector3(k == 0 ? -0.022f : 0.022f, 0.002f, -0.01f), Quaternion.Euler(0f, k == 0 ? 70f : -70f, 0f));
                CylinderX(b, Vector3.zero, 0.0025f, 0.016f, Pal.Bark, Pal.Hex("#D9B27A"));
                b.Pop();
            }
            return 0.03f;
        }

        // ---------------------------------------------------------------- travel

        private static float WaterTowerTank(MeshBuilder b)
        {
            b.AddBox(new Vector3(0f, 0.04f, 0f), new Vector3(0.05f, 0.08f, 0.05f), Pal.Brick, Pal.Brick, Pal.StoneDark);
            for (float y = 0.008f; y < 0.08f; y += 0.006f) b.AddBox(new Vector3(0f, y, 0f), new Vector3(0.0508f, 0.0005f, 0.0508f), Pal.Shade(Pal.Brick, 0.8f));
            b.AddFace(new Vector3(0f, 0.016f, -0.0255f), Vector3.back, Vector3.up, 0.007f, 0.016f, Pal.DoorGreen);
            b.AddBox(new Vector3(0f, 0.082f, 0f), new Vector3(0.056f, 0.004f, 0.056f), Pal.Stone);
            var wood = Pal.Hex("#7A5236");
            b.AddCylinder(new Vector3(0f, 0.084f, 0f), 0.033f, 0.033f, 0.05f, 18, wood, Pal.Shade(wood, 1.1f), false);
            foreach (float y in new[] { 0.09f, 0.108f, 0.126f }) b.AddCylinder(new Vector3(0f, y, 0f), 0.0335f, 0.0335f, 0.0016f, 18, Pal.TrainBlack, Pal.TrainBlack, false);
            b.AddCylinder(new Vector3(0f, 0.134f, 0f), 0.036f, 0f, 0.022f, 18, Pal.Hex("#5A5A62"), Pal.Hex("#7A7A82"), false, true);
            // The spout swung out towards the track.
            b.Push(new Vector3(0f, 0.1f, -0.034f), Quaternion.Euler(-60f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.003f, 0.0025f, 0.032f, 8, Pal.TrainBlack, Pal.TrainBlack, true);
            b.Pop();
            return 0.16f;
        }

        private static float SignalBoxHouse(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.064f, D = 0.05f;
            b.AddBox(new Vector3(0f, 0.02f, 0f), new Vector3(W, 0.04f, D), Pal.Brick, Pal.Brick, Pal.StoneDark);
            var green = Pal.Hex("#3E6A4A"); var cream = Pal.Hex("#F2E2BE");
            b.AddBox(new Vector3(0f, 0.058f, 0f), new Vector3(W + 0.004f, 0.036f, D + 0.004f), cream, cream, cream);
            foreach (var n in new[] { Vector3.back, Vector3.left, Vector3.right })
            {
                float span = n == Vector3.back ? W : D;
                b.Push(new Vector3(0f, 0.06f, 0f) + n * (n == Vector3.back ? D / 2 + 0.0022f : W / 2 + 0.0022f), Quaternion.LookRotation(n, Vector3.up));
                b.AddFace(Vector3.zero, Vector3.forward, Vector3.up, span * 0.42f, 0.01f, Pal.Glow(Pal.WindowGlow));
                for (float x = -span * 0.42f; x <= span * 0.421f; x += span * 0.21f) b.AddBox(new Vector3(x, 0f, 0.0008f), new Vector3(0.0016f, 0.021f, 0.0016f), green);
                b.AddBox(new Vector3(0f, 0.0105f, 0.0008f), new Vector3(span * 0.86f, 0.0016f, 0.0016f), green);
                b.AddBox(new Vector3(0f, -0.0105f, 0.0008f), new Vector3(span * 0.86f, 0.0016f, 0.0016f), green);
                b.Pop();
            }
            // Levers seen through the glass, a nameboard and steps up the side.
            for (int i = 0; i < 6; i++) b.AddBox(new Vector3(-0.02f + i * 0.008f, 0.052f, -0.01f), new Vector3(0.0012f, 0.01f, 0.0012f), i % 2 == 0 ? Pal.TrainRed : Pal.Hex("#4A7FB8"));
            Board(b, new Vector3(0f, 0.044f, -D / 2 - 0.0035f), 0.04f, 0.006f, Pal.Hex("#F7F1E6"), Pal.TrainBlack);
            for (int k = 0; k < 7; k++) b.AddBox(new Vector3(W / 2 + 0.006f, 0.003f + k * 0.0055f, -D / 2 + 0.008f + k * 0.004f), new Vector3(0.01f, 0.0015f, 0.005f), Pal.Hex("#6E4A33"));
            StoryRoof(b, ref rng, W + 0.004f, D + 0.004f, 0.076f, 0.026f, 0.008f, 0.0015f, Pal.RoofSlate, cream, Pal.Hex("#F7F1E6"), 4, false);
            Chimney(b, ref rng, new Vector3(0.02f, 0.076f, 0.01f), 0.04f, Pal.Brick, 1);
            return 0.12f;
        }

        /// <summary>The fuel station's forecourt: where its driveway comes in (its front, the street side), and the pump lane - a car stands at the pumps there facing +x, having come in at -x and leaving at +x.</summary>
        public static readonly Vector3 FuelEntrance = new(-0.045f, 0f, -0.05f);
        public static readonly Vector3 FuelPump = new(-0.03f, 0f, -0.036f);
        public const float FuelLaneEnd = 0.085f;

        private static float FuelStation(MeshBuilder b, ref Rng rng)
        {
            var white = Pal.Hex("#F7F1E6"); var red = Pal.Hex("#D7382E");
            b.AddBox(new Vector3(0f, -0.002f, 0f), new Vector3(0.14f, 0.005f, 0.1f), Pal.Hex("#9A9A9A"), Pal.Hex("#ABABAB"), Pal.Hex("#7A7A7A"));
            // The kiosk.
            b.AddBox(new Vector3(0.035f, 0.018f, 0.025f), new Vector3(0.05f, 0.036f, 0.04f), white, white, white);
            b.AddFace(new Vector3(0.035f, 0.018f, 0.0045f), Vector3.back, Vector3.up, 0.016f, 0.01f, Pal.Glow(Pal.Hex("#FFE3A8")));
            b.AddFace(new Vector3(0.055f, 0.015f, 0.0045f), Vector3.back, Vector3.up, 0.004f, 0.013f, Pal.Hex("#4A7FB8"));
            b.AddBox(new Vector3(0.035f, 0.037f, 0.025f), new Vector3(0.054f, 0.004f, 0.044f), red);
            // Canopy on two posts over the pumps.
            foreach (float x in new[] { -0.055f, -0.005f }) b.AddBox(new Vector3(x, 0.022f, -0.02f), new Vector3(0.003f, 0.044f, 0.003f), white);
            b.AddBox(new Vector3(-0.03f, 0.046f, -0.02f), new Vector3(0.07f, 0.006f, 0.04f), white, white, white);
            b.AddBox(new Vector3(-0.03f, 0.046f, -0.0402f), new Vector3(0.07f, 0.004f, 0.0008f), red);
            for (int k = 0; k < 2; k++)
            {
                var p = new Vector3(-0.042f + k * 0.024f, 0f, -0.02f);
                b.AddBox(p + new Vector3(0f, 0.002f, 0f), new Vector3(0.014f, 0.004f, 0.01f), Pal.Stone);
                b.AddBox(p + new Vector3(0f, 0.013f, 0f), new Vector3(0.008f, 0.018f, 0.006f), k == 0 ? red : Pal.Hex("#4A7FB8"));
                b.AddBlob(p + new Vector3(0f, 0.024f, 0f), new Vector3(0.0045f, 0.0045f, 0.002f), 1, 0f, k, (d, _) => Pal.Glow(Pal.Hex("#FFF4D8")));
                b.AddFace(p + new Vector3(0f, 0.016f, -0.0031f), Vector3.back, Vector3.up, 0.0028f, 0.002f, Pal.Glow(Pal.Hex("#CFE6F2")));
                b.AddBox(p + new Vector3(0.0045f, 0.01f, 0f), new Vector3(0.0008f, 0.008f, 0.0008f), Pal.TrainBlack);
            }
            // The sign on a pole.
            b.AddBox(new Vector3(-0.064f, 0.04f, 0.035f), new Vector3(0.002f, 0.08f, 0.002f), Pal.Hex("#9AA0A8"));
            b.Push(new Vector3(-0.064f, 0.075f, 0.035f), Quaternion.Euler(90f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.01f, 0.01f, 0.002f, 16, red, red, true, true);
            b.AddDisc(new Vector3(0f, 0.0021f, 0f), 0.006f, 16, Pal.Glow(Pal.Hex("#FFD34D")), true);
            b.Pop();
            return 0.09f;
        }

        // ---------------------------------------------------------------- nature

        private static Color RockColour(Vector3 d, Vector3 p, Color baseColour)
        {
            float n = Noise.Value3(p * 400f);
            var c = Color.Lerp(Pal.Shade(baseColour, 0.75f), baseColour, d.y * 0.4f + 0.5f) * (0.9f + 0.2f * n);
            if (d.y > 0.55f && n > 0.45f) c = Color.Lerp(c, Pal.Hex("#6E8E3E"), 0.65f);   // moss on top
            c.a = 1f;
            return c;
        }

        private static float Rocks(MeshBuilder b, ref Rng rng, int style)
        {
            var baseC = style == 2 ? Pal.Hex("#A8907A") : Pal.Hex("#A39E96");
            int n = 3 + style;
            for (int i = 0; i < n; i++)
            {
                float r = rng.Range(0.012f, 0.022f) * (i == 0 ? 1.3f : 1f);
                var p = i == 0 ? Vector3.zero : new Vector3(rng.Range(-0.03f, 0.03f), 0f, rng.Range(-0.025f, 0.025f));
                b.AddBlob(p + Vector3.up * r * 0.45f, new Vector3(r, r * 0.8f, r * 0.9f), 2, 0.25f, rng.Range(0, 999), (d, q) => RockColour(d, q, baseC), 2.5f);
            }
            for (int i = 0; i < 6; i++)
            {
                var c = Pal.Jitter(Pal.LeafMid, ref rng, 0.3f);
                Flora.GrassTuft(b, new Vector3(rng.Range(-0.04f, 0.04f), 0f, rng.Range(-0.035f, 0.035f)), 0.8f, ref rng, i % 3);
            }
            return 0.04f;
        }

        private static float CragRock(MeshBuilder b, ref Rng rng, int style)
        {
            var baseC = style == 0 ? Pal.Hex("#9E9A94") : Pal.Hex("#B09A84");
            Color Strata(Vector3 d, Vector3 q)
            {
                var c = RockColour(d, q, baseC);
                return Mathf.Sin(q.y * 700f + q.x * 90f) > 0.55f ? Pal.Shade(c, 0.84f) : c;
            }
            // A cluster of tilted, faceted rock pillars: a little mountain outcrop.
            var pillars = new[] { (0f, 0f, 0.15f, 0.024f), (0.03f, 0.012f, 0.095f, 0.02f), (-0.028f, 0.016f, 0.08f, 0.019f), (0.008f, -0.026f, 0.06f, 0.018f), (-0.02f, -0.02f, 0.045f, 0.016f), (0.04f, -0.018f, 0.035f, 0.014f) };
            foreach (var (x, z, h, r) in pillars)
            {
                b.Push(new Vector3(x, 0f, z), Quaternion.Euler(rng.Range(-8f, 8f), rng.Range(0f, 360f), rng.Range(-10f, 10f)));
                b.AddBlob(new Vector3(0f, h * 0.48f, 0f), new Vector3(r, h * 0.55f, r * 0.85f), 1, 0.22f, rng.Range(0, 999), Strata, 2.2f);
                b.Pop();
            }
            for (int i = 0; i < 5; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), d = rng.Range(0.035f, 0.055f);
                b.AddBlob(new Vector3(Mathf.Cos(a) * d, 0.004f, Mathf.Sin(a) * d), new Vector3(0.008f, 0.006f, 0.007f), 1, 0.3f, i, (dd, q) => RockColour(dd, q, baseC), 2.5f);
            }
            // A few little pines clinging to the ledges.
            foreach (var (x, y, z, sc) in new[] { (0.042f, 0f, 0.03f, 1f), (-0.045f, 0f, 0.02f, 0.8f), (0.02f, 0.06f, 0.012f, 0.6f) })
            {
                var p = new Vector3(x, y, z);
                b.AddCylinder(p, 0.0016f * sc, 0.0012f * sc, 0.008f * sc, 5, Pal.BarkDark, Pal.Bark, false);
                for (int k = 0; k < 3; k++)
                    b.AddCylinder(p + Vector3.up * (0.006f + k * 0.008f) * sc, (0.011f - k * 0.0028f) * sc, 0f, 0.013f * sc, 7, Pal.Conifer, Pal.ConiferLight, false, true);
            }
            return 0.16f;
        }

        private static float Logs(MeshBuilder b, ref Rng rng)
        {
            for (int row = 0; row < 3; row++)
            for (int k = 0; k < 4 - row; k++)
                CylinderX(b, new Vector3(0f, 0.005f + row * 0.0085f, (k - (3 - row) / 2f) * 0.0095f), 0.0047f * rng.Range(0.9f, 1.1f), 0.06f, Pal.Bark, Pal.Hex("#D9B27A"));
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.02f, 0.012f, -0.019f), new Vector3(0.0016f, 0.024f, 0.0016f), Pal.BarkDark);
            Flora.Mushroom(b, new Vector3(0.034f, 0f, 0.012f), 0.8f, ref rng);
            return 0.03f;
        }

        // ---------------------------------------------------------------- small details

        private static float StreetLamp(MeshBuilder b)
        {
            var iron = Pal.Hex("#2E3A30");
            b.AddCylinder(Vector3.zero, 0.0035f, 0.003f, 0.004f, 8, iron, iron, true);
            b.AddCylinder(new Vector3(0f, 0.004f, 0f), 0.0011f, 0.0009f, 0.05f, 6, iron, iron, false);
            b.AddBox(new Vector3(0f, 0.051f, 0f), new Vector3(0.006f, 0.001f, 0.006f), iron);
            b.AddBox(new Vector3(0f, 0.0555f, 0f), new Vector3(0.0048f, 0.008f, 0.0048f), Pal.Glow(Pal.Hex("#FFE2A0")));
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.0025f, 0.0555f, sz * 0.0025f), new Vector3(0.0007f, 0.008f, 0.0007f), iron);
            b.AddCylinder(new Vector3(0f, 0.0595f, 0f), 0.0045f, 0f, 0.004f, 4, iron, iron, false, true, false, Mathf.PI / 4f);
            b.AddBlob(new Vector3(0f, 0.064f, 0f), Vector3.one * 0.0009f, 0, 0f, 1, (_, __) => iron);
            return 0.065f;
        }

        private static float ParkBench(MeshBuilder b, int style)
        {
            var wood = style == 0 ? Pal.Hex("#A8743E") : Pal.Hex("#4E8A5A"); var iron = Pal.TrainBlack;
            for (int k = 0; k < 3; k++) b.AddBox(new Vector3(0f, 0.0065f, -0.003f + k * 0.0022f), new Vector3(0.032f, 0.0009f, 0.0018f), wood);
            for (int k = 0; k < 2; k++) b.AddBox(new Vector3(0f, 0.0095f + k * 0.0026f, 0.0035f + k * 0.0004f), new Vector3(0.032f, 0.0018f, 0.0009f), wood);
            foreach (int sx in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(sx * 0.013f, 0.003f, -0.002f), new Vector3(0.0012f, 0.006f, 0.0012f), iron);
                b.AddBox(new Vector3(sx * 0.013f, 0.0055f, 0.003f), new Vector3(0.0012f, 0.011f, 0.0012f), iron);
                b.AddBox(new Vector3(sx * 0.0155f, 0.009f, 0f), new Vector3(0.0012f, 0.0009f, 0.007f), iron);
            }
            return 0.015f;
        }

        private static float Bed(MeshBuilder b, ref Rng rng, int style)
        {
            var bloom = new[] { Pal.FlowerRed, Pal.FlowerYellow, Pal.FlowerPink, Pal.FlowerLilac }[style];
            b.AddBox(new Vector3(0f, 0.002f, 0f), new Vector3(0.056f, 0.004f, 0.028f), Pal.Stone, Pal.Hex("#6E4E32"), Pal.StoneDark);
            for (int i = 0; i < 9; i++)
            {
                var p = new Vector3(-0.022f + (i % 5) * 0.011f + (i / 5) * 0.0055f, 0.004f, i < 5 ? -0.006f : 0.006f);
                b.Sway = 0.6f;
                if (style == 3) Flora.Lavender(b, p, 0.8f, ref rng); else Flora.Tulips(b, p, bloom, 0.8f, ref rng);
                b.Sway = 0f;
            }
            return 0.02f;
        }

        private static float WishingWell(MeshBuilder b, ref Rng rng)
        {
            b.AddCylinder(Vector3.zero, 0.013f, 0.013f, 0.012f, 12, Pal.Stone, Pal.Hex("#C9BBA3"), false);
            b.AddCylinder(new Vector3(0f, 0.012f, 0f), 0.0138f, 0.0138f, 0.002f, 12, Pal.Hex("#D9CDB8"), Pal.Hex("#D9CDB8"), true);
            b.AddDisc(new Vector3(0f, 0.0138f, 0f), 0.0105f, 12, Pal.Hex("#2A3A44"), true);
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.0115f, 0.024f, 0f), new Vector3(0.0018f, 0.024f, 0.0018f), Pal.Timber);
            b.Push(new Vector3(-0.012f, 0.03f, 0f), Quaternion.Euler(0f, 0f, -90f));
            b.AddCylinder(Vector3.zero, 0.0009f, 0.0009f, 0.024f, 6, Pal.Timber, Pal.Timber, true, true);
            b.Pop();
            foreach (int side in new[] { -1, 1 })
            {
                b.Push(new Vector3(0f, 0.04f, side * 0.006f), Quaternion.Euler(side * 40f, 0f, 0f));
                b.AddBox(Vector3.zero, new Vector3(0.032f, 0.0014f, 0.016f), Pal.RoofTerracotta);
                b.Pop();
            }
            b.AddBox(new Vector3(0f, 0.022f, 0f), new Vector3(0.0004f, 0.016f, 0.0004f), Pal.Hex("#C9A06A"));
            b.AddCylinder(new Vector3(0f, 0.012f, 0f), 0.0022f, 0.0026f, 0.004f, 8, Pal.Hex("#8A5A36"), Pal.Hex("#A8743E"), true);
            return 0.05f;
        }

        private static float StatueOnPlinth(MeshBuilder b)
        {
            b.AddBox(new Vector3(0f, 0.008f, 0f), new Vector3(0.02f, 0.016f, 0.02f), Pal.Stone, Pal.Hex("#D9CDB8"), Pal.StoneDark);
            b.AddBox(new Vector3(0f, 0.0165f, 0f), new Vector3(0.023f, 0.002f, 0.023f), Pal.Hex("#D9CDB8"));
            Person(b, new Vector3(0f, 0.0175f, 0f), 180f, 1, Pal.Hex("#8FB5A0"), 1.35f);
            b.AddBox(new Vector3(0f, 0.008f, -0.0102f), new Vector3(0.012f, 0.004f, 0.0004f), Pal.Brass);
            return 0.06f;
        }

        private static float SignpostArrows(MeshBuilder b)
        {
            b.AddBox(new Vector3(0f, 0.022f, 0f), new Vector3(0.0022f, 0.044f, 0.0022f), Pal.Hex("#8A5A36"));
            var colours = new[] { Pal.Hex("#F7F1E6"), Pal.Hex("#FFD34D"), Pal.Hex("#F7F1E6") };
            for (int k = 0; k < 3; k++)
            {
                b.Push(new Vector3(0f, 0.034f + k * 0.0055f, 0f), Quaternion.Euler(0f, k * 70f - 40f, 0f));
                b.AddBox(new Vector3(0.008f, 0f, 0f), new Vector3(0.016f, 0.004f, 0.0012f), colours[k]);
                b.AddTriangleOutward(new Vector3(0.016f, 0.002f, 0f), new Vector3(0.016f, -0.002f, 0f), new Vector3(0.0195f, 0f, 0f), new Vector3(0.012f, 0f, 0f), colours[k]);
                b.Pop();
            }
            b.AddBlob(new Vector3(0f, 0.045f, 0f), Vector3.one * 0.0016f, 0, 0f, 2, (_, __) => Pal.TrainRed);
            return 0.05f;
        }

        private static float PicnicTable(MeshBuilder b)
        {
            var wood = Pal.Hex("#A8743E");
            b.AddBox(new Vector3(0f, 0.011f, 0f), new Vector3(0.03f, 0.0014f, 0.014f), wood);
            for (int i = 0; i < 4; i++)
            for (int j = 0; j < 2; j++)
                b.AddFace(new Vector3(-0.011f + i * 0.0074f, 0.0118f, -0.0035f + j * 0.007f), Vector3.up, Vector3.forward, 0.0037f, 0.0035f, (i + j) % 2 == 0 ? Pal.Hex("#E25B4F") : Pal.Hex("#F7F1E6"));
            foreach (int side in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(0f, 0.006f, side * 0.013f), new Vector3(0.03f, 0.0014f, 0.005f), wood);
                foreach (int sx in new[] { -1, 1 })
                {
                    b.Push(new Vector3(sx * 0.012f, 0.0055f, side * 0.004f), Quaternion.Euler(side * 35f, 0f, 0f));
                    b.AddBox(Vector3.zero, new Vector3(0.0016f, 0.013f, 0.0016f), Pal.Shade(wood, 0.8f));
                    b.Pop();
                }
            }
            b.AddBlob(new Vector3(0.005f, 0.0135f, 0f), new Vector3(0.0035f, 0.0018f, 0.0025f), 1, 0.1f, 3, (_, __) => Pal.Hex("#C98A4B"));
            return 0.015f;
        }

        private static float Hay(MeshBuilder b, ref Rng rng)
        {
            CylinderX(b, new Vector3(-0.012f, 0.007f, 0f), 0.007f, 0.012f, Pal.Thatch, Pal.Hex("#E8C46A"));
            CylinderX(b, new Vector3(0.006f, 0.007f, 0.002f), 0.007f, 0.012f, Pal.Thatch, Pal.Hex("#E8C46A"));
            b.Push(new Vector3(-0.003f, 0.0195f, 0.001f), Quaternion.Euler(0f, 90f, 0f));
            CylinderX(b, Vector3.zero, 0.0068f, 0.012f, Pal.Shade(Pal.Thatch, 1.05f), Pal.Hex("#E8C46A"));
            b.Pop();
            for (int i = 0; i < 5; i++) b.AddBlob(new Vector3(rng.Range(-0.025f, 0.02f), 0.001f, rng.Range(-0.015f, 0.015f)), new Vector3(0.004f, 0.0008f, 0.0025f), 0, 0.5f, i, (_, __) => Pal.Thatch);
            return 0.025f;
        }
    }
}
