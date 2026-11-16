using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The livelier town: a park, a museum with a dinosaur, a restaurant terrace, a disco with a mirror ball, a hospital,
    /// a police station, a biscuit factory, a football pitch, a monitoring studio with a wall of screens, CCTV cameras
    /// and a hot-air balloon. Same parts and palette as the rest of the village.
    /// </summary>
    public static partial class HouseFactory
    {
        /// <summary>The monitoring studio's screens, in its local frame: centre of each (2 x 2 on the front wall) and half size.</summary>
        public static readonly Vector3[] StudioScreens =
        {
            new(-0.037f, 0.062f, -0.042f), new(-0.009f, 0.062f, -0.042f), new(-0.037f, 0.04f, -0.042f), new(-0.009f, 0.04f, -0.042f),
        };
        public static readonly Vector2 StudioScreenHalf = new(0.0125f, 0.0095f);

        /// <summary>Where a CCTV camera's head sits on its pole (local), and the head itself (looks along -Z, its front).</summary>
        public static readonly Vector3 CameraHeadAt = new(0f, 0.082f, -0.004f);
        private static Mesh cameraHead, ball;

        public static Mesh CameraHead()
        {
            if (cameraHead != null) return cameraHead;
            var b = new MeshBuilder();
            var white = Pal.Hex("#EDEBE4"); var dark = Pal.TrainBlack;
            b.AddBox(new Vector3(0f, 0f, 0f), new Vector3(0.008f, 0.0075f, 0.017f), white, Pal.Shade(white, 1.05f), Pal.Shade(white, 0.8f), false);
            b.AddBox(new Vector3(0f, 0.0047f, -0.002f), new Vector3(0.0102f, 0.0012f, 0.021f), white);   // sun hood
            b.Push(new Vector3(0f, 0f, -0.0086f), Quaternion.Euler(-90f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0028f, 0.0028f, 0.0012f, 10, dark, dark, true);
            b.AddDisc(new Vector3(0f, 0.0013f, 0f), 0.0019f, 10, Pal.Hex("#3A5A8A"), true);
            b.Pop();
            b.AddBlob(new Vector3(0.0028f, 0.0026f, -0.0084f), Vector3.one * 0.0007f, 0, 0f, 2, (_, __) => Pal.Glow(Pal.Hex("#FF3B30")));   // recording light
            return cameraHead = b.ToMesh("CameraHead");
        }

        /// <summary>A football (about 6 mm across).</summary>
        public static Mesh Ball()
        {
            if (ball != null) return ball;
            var b = new MeshBuilder();
            b.AddBlob(Vector3.zero, Vector3.one * 0.003f, 1, 0f, 4, (d, _) => Mathf.Abs(d.x) > 0.6f || Mathf.Abs(d.y) > 0.75f ? Pal.TrainBlack : Pal.FlowerWhite);
            return ball = b.ToMesh("Football");
        }

        private static float Town(MeshBuilder b, ref Rng rng, Style s, int id, int style, out bool warp)
        {
            warp = true;
            switch (id)
            {
                case Catalog.Museum: return MuseumHall(b, ref rng, s, style);
                case Catalog.Restaurant: return RestaurantHouse(b, ref rng, s, style);
                case Catalog.Disco: return DiscoHall(b, ref rng, s);
                case Catalog.Hospital: return HospitalBlock(b, ref rng, s);
                case Catalog.Police: return PoliceStation(b, ref rng, s);
                case Catalog.Factory: return FactoryShed(b, ref rng, s);
            }
            warp = false;
            return id switch
            {
                Catalog.Park => ParkGrounds(b, ref rng),
                Catalog.Football => Pitch(b),
                Catalog.Studio => StudioHouse(b, ref rng, s),
                Catalog.Camera => CameraPole(b),
                Catalog.Balloon => LaunchField(b, ref rng),
                Catalog.Jetty => BoatFactory.Jetty(b),
                Catalog.Halt => HaltShelter(b),
                Catalog.RowingBoat or Catalog.SailingBoat or Catalog.Steamer => BoatShape(b, (BoatKind)Catalog.BoatOf(id)),
                _ => -1f,
            };
        }

        /// <summary>A country halt: a little wooden shelter with a bench, a name board on posts and a lamp (the platform is laid along the rails).</summary>
        private static float HaltShelter(MeshBuilder b)
        {
            var wood = Pal.Hex("#8C5A3A"); var paint = Pal.Hex("#2E6B4E"); var cream = Pal.Hex("#F1E6CC"); var roof = Pal.Hex("#6B3F2E");
            const float W = 0.05f, D = 0.024f, H = 0.036f;
            b.AddBox(new Vector3(0f, 0.002f, 0.004f), new Vector3(W + 0.01f, 0.004f, D + 0.008f), Pal.Hex("#B9A88C"));                 // base
            b.AddBox(new Vector3(0f, H / 2 + 0.004f, D / 2 + 0.004f - 0.0015f), new Vector3(W, H, 0.003f), cream, cream, cream);          // back wall
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 - 0.0015f), H / 2 + 0.004f, 0.004f), new Vector3(0.003f, H, D), cream);   // ends
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 - 0.0015f), H / 2 + 0.004f, -D / 2 + 0.004f), new Vector3(0.0035f, H, 0.0035f), paint);   // front posts
            b.Push(new Vector3(0f, H + 0.004f, 0.004f), Quaternion.Euler(-12f, 0f, 0f));
            b.AddBox(new Vector3(0f, 0.002f, 0f), new Vector3(W + 0.012f, 0.003f, D + 0.012f), roof, Pal.Shade(roof, 1.1f), Pal.Shade(roof, 0.8f));   // a sloping roof
            b.Pop();
            b.AddBox(new Vector3(0f, 0.012f, 0.008f), new Vector3(W - 0.012f, 0.0025f, 0.008f), wood);                                   // bench
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.014f, 0.0075f, 0.008f), new Vector3(0.002f, 0.007f, 0.006f), wood);
            // Name board on two posts, and a lamp.
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(W / 2 + 0.012f + s * 0.008f, 0.014f, -0.004f), new Vector3(0.0018f, 0.028f, 0.0018f), Pal.Hex("#3A3A3A"));
            b.AddBox(new Vector3(W / 2 + 0.012f, 0.026f, -0.004f), new Vector3(0.022f, 0.008f, 0.0016f), paint, cream, paint);
            b.AddFace(new Vector3(W / 2 + 0.012f, 0.026f, -0.005f), Vector3.back, Vector3.up, 0.0095f, 0.0028f, cream);
            b.AddCylinder(new Vector3(-W / 2 - 0.01f, 0.002f, -0.004f), 0.0011f, 0.0009f, 0.04f, 6, Pal.Hex("#2E3B45"), Pal.Hex("#2E3B45"));
            b.AddBlob(new Vector3(-W / 2 - 0.01f, 0.044f, -0.004f), Vector3.one * 0.0028f, 1, 0f, 9, (_, _) => Pal.Glow(Pal.Hex("#FFE3A0")));
            return H + 0.01f;
        }

        /// <summary>A boat as one mesh (for the Build tool's ghost and the card's portrait): its hull with the sail set.</summary>
        private static float BoatShape(MeshBuilder b, BoatKind k)
        {
            b.AddMesh(BoatFactory.Hull(k), Matrix4x4.identity);
            if (k == BoatKind.Sailing) b.AddMesh(BoatFactory.Sail(), Matrix4x4.Translate(new Vector3(0f, 0.003f, 0.016f)));
            return 0.03f;
        }

        // ---------------------------------------------------------------- buildings

        private static float MuseumHall(MeshBuilder b, ref Rng rng, Style s, int style)
        {
            const float W = 0.12f, D = 0.075f, H = 0.07f, Z = 0.018f;
            var stone = style == 0 ? Pal.Hex("#EDE3CF") : Pal.Hex("#E2D6E6");
            b.Push(new Vector3(0f, 0f, Z));
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, stone);
            b.AddBox(new Vector3(0f, H + 0.004f, 0f), new Vector3(W + 0.006f, 0.008f, D + 0.006f), Pal.Shade(stone, 0.92f));
            b.AddBox(new Vector3(0f, H + 0.014f, 0f), new Vector3(W * 0.7f, 0.012f, D * 0.7f), stone);
            // A glass dome.
            b.AddBlob(new Vector3(0f, H + 0.02f, 0f), new Vector3(0.026f, 0.024f, 0.026f), 2, 0f, 7, (d, _) => d.y < 0.05f ? stone : Pal.Glow(Color.Lerp(Pal.Hex("#7FB0E0"), Pal.Hex("#CFE6F2"), d.y)));
            Flag(b, new Vector3(0f, H + 0.043f, 0f), 0.02f, Pal.Hex("#4A7FB8"));
            // Portico: six columns, a pediment, steps.
            float front = -D / 2;
            for (int k = 0; k < 3; k++) b.AddBox(new Vector3(-0.02f, 0.0015f + k * 0.002f, front - 0.016f + k * 0.004f), new Vector3(0.06f - k * 0.004f, 0.003f, 0.012f), Pal.Stone);
            for (int c = 0; c < 6; c++) b.AddCylinder(new Vector3(-0.045f + c * 0.012f, 0.006f, front - 0.01f), 0.0025f, 0.0022f, 0.05f, 10, Pal.FrameWhite, Pal.FrameWhite, false);
            b.AddBox(new Vector3(-0.015f, 0.058f, front - 0.01f), new Vector3(0.07f, 0.005f, 0.016f), stone);
            b.Push(new Vector3(-0.015f, 0.061f, front - 0.01f));
            b.AddTriangleOutward(new Vector3(-0.036f, 0f, -0.008f), new Vector3(0.036f, 0f, -0.008f), new Vector3(0f, 0.012f, -0.008f), new Vector3(0f, 0.004f, 0.01f), stone);
            b.Pop();
            Wall(b, new Vector3(-0.02f, 0.006f, front), Vector3.back); Door(b, ref rng, 0.02f, 0.034f, s, true, false); b.Pop();
            Board(b, new Vector3(0.04f, 0.045f, front - 0.001f), 0.034f, 0.008f, Pal.Hex("#4A7FB8"), Pal.FrameWhite);
            Windows(b, ref rng, s, D / 2, new[] { -0.04f, 0f, 0.04f }, 0.035f, 0.012f, 0.026f, false, false);
            SideWindows(b, ref rng, s, W / 2, new[] { -0.015f, 0.015f }, 0.035f, 0.012f, 0.026f);
            b.Pop();
            // The dinosaur in the forecourt: a long-necked skeleton on a plinth.
            var bone = Pal.Hex("#F2E8D2");
            var dino = new Vector3(0.04f, 0f, -0.042f);
            b.AddBox(dino + new Vector3(0f, 0.002f, 0f), new Vector3(0.03f, 0.004f, 0.012f), Pal.StoneDark);
            for (int i = 0; i < 9; i++)
            {
                float t = i / 8f;
                var p = dino + new Vector3(-0.016f + t * 0.03f, 0.02f + Mathf.Sin(t * Mathf.PI) * 0.004f + (t > 0.7f ? (t - 0.7f) * 0.07f : 0f) - (t < 0.25f ? (0.25f - t) * 0.04f : 0f), 0f);
                b.AddBlob(p, Vector3.one * (0.0018f + (t > 0.3f && t < 0.7f ? 0.0008f : 0f)), 0, 0f, i, (_, __) => bone);
                if (t > 0.3f && t < 0.75f) b.AddBox(p - new Vector3(0f, 0.004f, 0f), new Vector3(0.0006f, 0.006f, 0.008f), bone);   // ribs
            }
            b.AddBlob(dino + new Vector3(0.017f, 0.035f, 0f), new Vector3(0.003f, 0.002f, 0.002f), 0, 0f, 9, (_, __) => bone);
            foreach (float x in new[] { -0.008f, 0.008f })
            foreach (float z in new[] { -0.004f, 0.004f })
                b.AddCylinder(dino + new Vector3(x, 0.004f, z), 0.0011f, 0.0009f, 0.014f, 5, bone, bone, false);
            return 0.11f;
        }

        private static float RestaurantHouse(MeshBuilder b, ref Rng rng, Style s, int style)
        {
            const float W = 0.11f, D = 0.07f, H = 0.07f, R = 0.045f;
            s.Plaster = style == 0 ? Pal.Hex("#F2D2A8") : Pal.Hex("#DCEAD2"); s.Roof = style == 0 ? Pal.RoofTerracotta : Pal.RoofSlate;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, s.Plaster);
            b.AddBox(new Vector3(0f, 0.04f, 0f), new Vector3(W + 0.002f, 0.002f, D + 0.002f), s.Trim);
            Wall(b, new Vector3(0f, 0.006f, -D / 2), Vector3.back); Door(b, ref rng, 0.02f, 0.03f, s, true, false); b.Pop();
            // Big lit windows either side of the door, and the upper floor.
            foreach (float x in new[] { -0.035f, 0.035f }) b.AddFace(new Vector3(x, 0.022f, -D / 2 - 0.0006f), Vector3.back, Vector3.up, 0.016f, 0.012f, Pal.Glow(Pal.WindowGlow));
            Windows(b, ref rng, s, -D / 2, new[] { -0.035f, 0f, 0.035f }, 0.055f, 0.013f, 0.016f, true, true);
            SideWindows(b, ref rng, s, W / 2, new[] { 0f }, 0.055f, 0.012f, 0.016f);
            Awning(b, new Vector3(0f, 0.04f, -D / 2), W - 0.01f, 0.018f, style == 0 ? Pal.Hex("#A33A2E") : Pal.Hex("#3F7A4A"), Pal.FrameWhite);
            Board(b, new Vector3(0f, 0.046f, -D / 2 - 0.001f), 0.05f, 0.007f, Pal.Hex("#3A2A20"), Pal.Brass);
            StoryRoof(b, ref rng, W, D, H, R, 0.008f, 0.002f, s.Roof, s.Plaster, s.Trim, 6, false);
            Chimney(b, ref rng, new Vector3(0.03f, H, 0.012f), R + 0.016f, Pal.Brick, 1);
            // The terrace: three tables with checked cloths and lanterns on a string between posts.
            var cloth = style == 0 ? Pal.Hex("#E25B4F") : Pal.Hex("#4A7FB8");
            foreach (var t in new[] { new Vector2(-0.034f, -0.072f), new Vector2(0.034f, -0.072f), new Vector2(0f, -0.092f) })
            {
                b.AddCylinder(new Vector3(t.x, 0f, t.y), 0.0009f, 0.0009f, 0.008f, 5, Pal.TrainBlack, Pal.TrainBlack, false);
                b.AddCylinder(new Vector3(t.x, 0.008f, t.y), 0.0075f, 0.0075f, 0.001f, 10, cloth, Pal.FrameWhite, true);
                b.AddBlob(new Vector3(t.x, 0.0102f, t.y), Vector3.one * 0.0011f, 0, 0f, 1, (_, __) => Pal.Glow(Pal.Hex("#FFE2A0")));   // a candle
                foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(t.x + sx * 0.012f, 0.0055f, t.y), new Vector3(0.006f, 0.0008f, 0.006f), Pal.Timber);
            }
            foreach (float x in new[] { -0.058f, 0.058f }) b.AddCylinder(new Vector3(x, 0f, -0.1f), 0.0008f, 0.0008f, 0.036f, 4, Pal.Timber, Pal.Timber, false);
            for (int i = 0; i <= 8; i++)
            {
                float x = -0.058f + i * 0.0145f;
                float y = 0.035f - Mathf.Sin(i / 8f * Mathf.PI) * 0.006f;
                b.AddBlob(new Vector3(x, y, -0.1f + Mathf.Sin(i / 8f * Mathf.PI) * 0.03f), Vector3.one * 0.0011f, 0, 0f, i, (_, __) => Pal.Glow(i % 2 == 0 ? Pal.Hex("#FFD27A") : Pal.Hex("#FF9E7A")));
            }
            return H + R;
        }

        private static float DiscoHall(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.11f, D = 0.085f, H = 0.075f;
            var wall = Pal.Hex("#3A2E4E");
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, wall);
            b.AddBox(new Vector3(0f, H + 0.003f, 0f), new Vector3(W + 0.004f, 0.006f, D + 0.004f), Pal.Shade(wall, 0.7f));
            // Neon stripes and coloured glass bricks on the front; a marquee over the door.
            Color[] neon = { Pal.Hex("#FF4FA8"), Pal.Hex("#4FD8FF"), Pal.Hex("#FFE04F"), Pal.Hex("#7CFF6B") };
            for (int i = 0; i < 4; i++) b.AddBox(new Vector3(0f, 0.012f + i * 0.016f, -D / 2 - 0.0006f), new Vector3(W - 0.004f, 0.0016f, 0.0008f), Pal.Glow(neon[i]));
            for (int x = 0; x < 6; x++)
            for (int y = 0; y < 2; y++)
                b.AddFace(new Vector3(-0.045f + x * 0.018f + (x >= 3 ? 0.0f : 0f), 0.03f + y * 0.018f, -D / 2 - 0.0008f), Vector3.back, Vector3.up, 0.005f, 0.005f, Pal.Glow(neon[(x + y) % 4]));
            Wall(b, new Vector3(0f, 0.004f, -D / 2), Vector3.back); Door(b, ref rng, 0.022f, 0.03f, s, false, false); b.Pop();
            b.AddBox(new Vector3(0f, 0.038f, -D / 2 - 0.007f), new Vector3(0.05f, 0.008f, 0.014f), Pal.TrainBlack);
            for (int i = 0; i < 9; i++) b.AddBlob(new Vector3(-0.022f + i * 0.0055f, 0.0335f, -D / 2 - 0.0142f), Vector3.one * 0.0009f, 0, 0f, i, (_, __) => Pal.Glow(Pal.Hex("#FFE2A0")));
            Board(b, new Vector3(0f, 0.04f, -D / 2 - 0.0145f), 0.04f, 0.006f, Pal.Glow(Pal.Hex("#FF4FA8")), Pal.FrameWhite);
            // A velvet rope and a red carpet.
            b.AddBox(new Vector3(0f, 0.0006f, -D / 2 - 0.016f), new Vector3(0.024f, 0.0012f, 0.03f), Pal.Hex("#B8483E"));
            foreach (int sx in new[] { -1, 1 }) b.AddCylinder(new Vector3(sx * 0.016f, 0f, -D / 2 - 0.024f), 0.0009f, 0.0009f, 0.012f, 5, Pal.Brass, Pal.Brass, true);
            // The roof: a dome cage for the mirror ball (the ball itself turns, see Rotor).
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                b.AddCylinder(new Vector3(sx * 0.016f, H + 0.006f, sz * 0.016f), 0.0008f, 0.0008f, 0.03f, 4, Pal.Brass, Pal.Brass, false);
            b.AddBox(new Vector3(0f, H + 0.037f, 0f), new Vector3(0.036f, 0.002f, 0.036f), Pal.Brass);
            return H + 0.04f;
        }

        private static Mesh MirrorBall()
        {
            var b = new MeshBuilder();
            b.AddBlob(Vector3.zero, Vector3.one * 0.011f, 2, 0f, 11, (d, _) =>
            {
                int facet = Mathf.FloorToInt((Mathf.Atan2(d.z, d.x) + Mathf.PI) * 4f) + Mathf.FloorToInt(d.y * 6f);
                return facet % 5 == 0 ? Pal.Glow(Pal.Hex("#FFFFFF")) : facet % 3 == 0 ? Pal.Glow(Pal.Hex("#FF8FD0")) : Pal.Hex("#C9CDD2");
            });
            b.AddCylinder(new Vector3(0f, 0.011f, 0f), 0.0005f, 0.0005f, 0.018f, 4, Pal.TrainBlack, Pal.TrainBlack, false);
            return b.ToMesh("MirrorBall");
        }

        private static float HospitalBlock(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.15f, D = 0.09f, H = 0.105f;
            var white = Pal.Hex("#F4F2EC");
            s.Plaster = white;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, white);
            for (float y = 0.035f; y < H; y += 0.034f) b.AddBox(new Vector3(0f, y, 0f), new Vector3(W + 0.002f, 0.002f, D + 0.002f), Pal.Hex("#7FB0E0"));
            foreach (float y in new[] { 0.052f, 0.086f })
            {
                Windows(b, ref rng, s, -D / 2, new[] { -0.06f, -0.036f, -0.012f, 0.012f, 0.036f, 0.06f }, y, 0.014f, 0.018f, false, false);
                Windows(b, ref rng, s, D / 2, new[] { -0.05f, -0.02f, 0.02f, 0.05f }, y, 0.014f, 0.018f, false, false);
            }
            Windows(b, ref rng, s, -D / 2, new[] { 0.03f, 0.055f }, 0.02f, 0.014f, 0.018f, false, false);
            // Entrance canopy and a red cross on a white sign.
            Wall(b, new Vector3(-0.034f, 0.004f, -D / 2), Vector3.back); Door(b, ref rng, 0.028f, 0.028f, s, false, false); b.Pop();
            b.AddBox(new Vector3(-0.034f, 0.034f, -D / 2 - 0.012f), new Vector3(0.05f, 0.003f, 0.026f), white);
            foreach (int sx in new[] { -1, 1 }) b.AddCylinder(new Vector3(-0.034f + sx * 0.022f, 0f, -D / 2 - 0.022f), 0.0011f, 0.0011f, 0.033f, 5, Pal.Hex("#C9CDD2"), Pal.Hex("#C9CDD2"), false);
            var red = Pal.Hex("#E0352B");
            var sign = new Vector3(0.045f, H + 0.012f, -D / 2 + 0.006f);
            b.AddBox(sign, new Vector3(0.024f, 0.024f, 0.003f), white);
            b.AddBox(sign + new Vector3(0f, 0f, -0.0018f), new Vector3(0.016f, 0.005f, 0.0008f), Pal.Glow(red));
            b.AddBox(sign + new Vector3(0f, 0f, -0.0018f), new Vector3(0.005f, 0.016f, 0.0008f), Pal.Glow(red));
            b.AddBox(new Vector3(0f, H + 0.002f, 0f), new Vector3(W + 0.004f, 0.004f, D + 0.004f), Pal.Hex("#C9CDD2"));
            // A helipad H on the roof.
            b.AddCylinder(new Vector3(-0.035f, H + 0.004f, 0.01f), 0.022f, 0.022f, 0.0008f, 16, Pal.Hex("#7E7466"), Pal.Hex("#7E7466"), true);
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(-0.035f + sx * 0.007f, H + 0.0051f, 0.01f), new Vector3(0.002f, 0.0004f, 0.018f), Pal.FrameWhite);
            b.AddBox(new Vector3(-0.035f, H + 0.0051f, 0.01f), new Vector3(0.014f, 0.0004f, 0.002f), Pal.FrameWhite);
            Shrubs(b, ref rng, W, D, 3, Pal.FlowerPink);
            return H + 0.03f;
        }

        private static float PoliceStation(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.1f, D = 0.08f, H = 0.07f, R = 0.035f;
            var stone = Pal.Hex("#C9C2B4"); s.Plaster = stone; s.Roof = Pal.RoofSlate; s.Door = Pal.DoorBlue; s.Shutter = Pal.Hex("#2E4A7A");
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, stone);
            for (float y = 0.008f; y < H; y += 0.006f) b.AddBox(new Vector3(0f, y, 0f), new Vector3(W + 0.0008f, 0.0005f, D + 0.0008f), Pal.Shade(stone, 0.88f));
            Wall(b, new Vector3(0f, 0.006f, -D / 2), Vector3.back); Door(b, ref rng, 0.02f, 0.032f, s, false, true); b.Pop();
            Windows(b, ref rng, s, -D / 2, new[] { -0.032f, 0.032f }, 0.025f, 0.014f, 0.018f, true, false);
            Windows(b, ref rng, s, -D / 2, new[] { -0.032f, 0f, 0.032f }, 0.055f, 0.012f, 0.016f, true, false);
            SideWindows(b, ref rng, s, W / 2, new[] { 0f }, 0.04f, 0.012f, 0.018f);
            // The blue lamp over the door and a POLICE board.
            b.AddBox(new Vector3(0f, 0.043f, -D / 2 - 0.004f), new Vector3(0.0012f, 0.0012f, 0.008f), Pal.TrainBlack);
            b.AddBox(new Vector3(0f, 0.045f, -D / 2 - 0.008f), new Vector3(0.006f, 0.006f, 0.006f), Pal.Glow(Pal.Hex("#4A8BFF")));
            Board(b, new Vector3(0f, 0.069f, -D / 2 - 0.001f), 0.05f, 0.008f, Pal.Hex("#2E4A7A"), Pal.FrameWhite);
            StoryRoof(b, ref rng, W, D, H, R, 0.006f, 0.001f, s.Roof, stone, Pal.FrameWhite, 5, false);
            Flag(b, new Vector3(W / 2 - 0.01f, H + R - 0.01f, 0f), 0.025f, Pal.Hex("#2E4A7A"));
            return H + R + 0.02f;
        }

        private static float FactoryShed(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.16f, D = 0.1f, H = 0.055f;
            var brick = Pal.Hex("#A8503A");
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, brick);
            for (float y = 0.008f; y < H; y += 0.005f) b.AddBox(new Vector3(0f, y, 0f), new Vector3(W + 0.0008f, 0.0005f, D + 0.0008f), Pal.Shade(brick, 0.85f));
            // A saw-tooth roof with north lights.
            for (int k = 0; k < 4; k++)
            {
                float x = -W / 2 + W / 8f + k * W / 4f;
                b.Push(new Vector3(x, H, 0f));
                b.AddTriangleOutward(new Vector3(-W / 8f, 0f, -D / 2), new Vector3(W / 8f, 0f, -D / 2), new Vector3(W / 8f, 0.02f, -D / 2), new Vector3(0f, 0.005f, 0f), brick);
                b.AddTriangleOutward(new Vector3(-W / 8f, 0f, D / 2), new Vector3(W / 8f, 0f, D / 2), new Vector3(W / 8f, 0.02f, D / 2), new Vector3(0f, 0.005f, 0f), brick);
                b.AddQuad(new Vector3(-W / 8f, 0f, -D / 2), new Vector3(-W / 8f, 0f, D / 2), new Vector3(W / 8f, 0.02f, D / 2), new Vector3(W / 8f, 0.02f, -D / 2), Pal.RoofSlate);
                b.AddQuad(new Vector3(W / 8f, 0.02f, -D / 2), new Vector3(W / 8f, 0.02f, D / 2), new Vector3(W / 8f, 0f, D / 2), new Vector3(W / 8f, 0f, -D / 2), Pal.Glow(Pal.Hex("#CFE6F2")));
                b.Pop();
            }
            // Loading door, windows, the sign, and the tall chimney (smoke comes from it while the factory works).
            Wall(b, new Vector3(0.05f, 0.004f, -D / 2), Vector3.back); Door(b, ref rng, 0.024f, 0.03f, s, false, false); b.Pop();
            b.AddFace(new Vector3(-0.025f, 0.018f, -D / 2 - 0.0006f), Vector3.back, Vector3.up, 0.02f, 0.014f, Pal.Hex("#5E6E7A"));
            for (int i = -3; i <= 3; i++) b.AddBox(new Vector3(-0.025f + i * 0.0055f, 0.018f, -D / 2 - 0.001f), new Vector3(0.0006f, 0.028f, 0.0006f), Pal.Shade(Pal.Hex("#5E6E7A"), 0.7f));
            Windows(b, ref rng, s, -D / 2, new[] { -0.065f, 0.02f }, 0.035f, 0.014f, 0.016f, false, false);
            Board(b, new Vector3(-0.025f, 0.045f, -D / 2 - 0.001f), 0.06f, 0.008f, Pal.Hex("#F2E2BE"), Pal.Hex("#A33A2E"));
            b.AddCylinder(FactoryChimney, 0.008f, 0.0055f, 0.12f, 10, brick, Pal.Shade(brick, 0.9f), false);
            b.AddCylinder(FactoryChimney + new Vector3(0f, 0.12f, 0f), 0.0068f, 0.0068f, 0.004f, 10, Pal.TrainBlack, Pal.TrainBlack, true);
            // Crates of biscuits by the door.
            for (int i = 0; i < 3; i++) b.AddBox(new Vector3(0.072f - i * 0.009f, 0.004f + (i == 1 ? 0.007f : 0f), -D / 2 - 0.008f), new Vector3(0.008f, 0.007f, 0.008f), Pal.Hex("#B07A45"));
            return 0.13f;
        }

        /// <summary>The factory's chimney foot (its smoke rises from the top, 12.4 cm up).</summary>
        public static readonly Vector3 FactoryChimney = new(-0.06f, 0f, 0.03f);

        private static float StudioHouse(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.12f, D = 0.08f, H = 0.08f;
            var wall = Pal.Hex("#5E6E7A");
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, wall);
            b.AddBox(new Vector3(0f, H + 0.003f, 0f), new Vector3(W + 0.004f, 0.006f, D + 0.004f), Pal.Shade(wall, 0.75f));
            // The monitor wall: a dark console frame with four bezels (the screens are live, see CctvSystem).
            b.AddBox(new Vector3(-0.023f, 0.051f, -D / 2 - 0.0005f), new Vector3(0.064f, 0.05f, 0.001f), Pal.TrainBlack);
            foreach (var c in StudioScreens) b.AddBox(new Vector3(c.x, c.y, -D / 2 - 0.0009f), new Vector3(StudioScreenHalf.x * 2f + 0.002f, StudioScreenHalf.y * 2f + 0.002f, 0.0008f), Pal.Hex("#3A3A44"));
            b.AddBox(new Vector3(-0.023f, 0.022f, -D / 2 - 0.006f), new Vector3(0.06f, 0.006f, 0.012f), Pal.Hex("#4A4A56"));   // the desk
            for (int i = 0; i < 6; i++) b.AddBlob(new Vector3(-0.045f + i * 0.009f, 0.0255f, -D / 2 - 0.008f), Vector3.one * 0.0008f, 0, 0f, i, (_, __) => Pal.Glow(i % 2 == 0 ? Pal.Hex("#7CFF6B") : Pal.Hex("#FF4F4F")));
            Wall(b, new Vector3(0.045f, 0.004f, -D / 2), Vector3.back); Door(b, ref rng, 0.018f, 0.03f, s, false, false); b.Pop();
            Board(b, new Vector3(0.045f, 0.05f, -D / 2 - 0.001f), 0.026f, 0.007f, Pal.Hex("#2A2A30"), Pal.Glow(Pal.Hex("#7CFF6B")));
            // An antenna mast and a dish on the roof.
            b.AddCylinder(new Vector3(0.04f, H + 0.006f, 0.02f), 0.0008f, 0.0006f, 0.05f, 4, Pal.Hex("#C9CDD2"), Pal.Hex("#C9CDD2"), false);
            b.AddBlob(new Vector3(0.04f, H + 0.057f, 0.02f), Vector3.one * 0.0012f, 0, 0f, 3, (_, __) => Pal.Glow(Pal.Hex("#FF3B30")));
            b.Push(new Vector3(-0.03f, H + 0.014f, 0.015f), Quaternion.Euler(-35f, 30f, 0f));
            b.AddCylinder(Vector3.zero, 0.012f, 0.002f, 0.004f, 14, Pal.FrameWhite, Pal.FrameWhite, true, true);
            b.Pop();
            b.AddCylinder(new Vector3(-0.03f, H + 0.006f, 0.015f), 0.0012f, 0.0012f, 0.008f, 5, Pal.Hex("#C9CDD2"), Pal.Hex("#C9CDD2"), false);
            return H + 0.06f;
        }

        // ---------------------------------------------------------------- grounds

        private static float ParkGrounds(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.22f, D = 0.17f;
            b.AddBox(new Vector3(0f, -0.0015f, 0f), new Vector3(W, 0.004f, D), Pal.Hex("#5E9A3E"), Pal.Hex("#6FB04A"), Pal.Hex("#4A7A30"));
            // A gravel loop path and a crossing path.
            var gravel = Pal.Hex("#D9C9A8");
            for (int i = 0; i < 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                b.Push(new Vector3(Mathf.Cos(a) * 0.075f, 0.0008f, Mathf.Sin(a) * 0.052f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
                b.AddBox(Vector3.zero, new Vector3(0.008f, 0.0008f, 0.0125f), gravel);
                b.Pop();
            }
            b.AddBox(new Vector3(0f, 0.0008f, -0.07f), new Vector3(0.012f, 0.0008f, 0.04f), gravel);
            // A duck pond in the middle with lilies.
            b.AddCylinder(new Vector3(0.0f, 0.0004f, 0.002f), 0.028f, 0.028f, 0.0006f, 18, Pal.Hex("#C9B98A"), Pal.Hex("#C9B98A"), true, false, true, 0f, 0.15f);
            b.AddCylinder(new Vector3(0.0f, 0.0006f, 0.002f), 0.024f, 0.024f, 0.0008f, 18, Pal.Hex("#4A8CB0"), Pal.Hex("#6FB0D0"), true, false, true, 0f, 0.15f);
            for (int i = 0; i < 4; i++) b.AddCylinder(new Vector3(rng.Range(-0.012f, 0.012f), 0.0015f, rng.Range(-0.012f, 0.014f)), 0.0028f, 0.0028f, 0.0003f, 8, Pal.LeafLight, Pal.LeafLight, true);
            // Trees round the edge, flower beds, two benches and a lamp.
            var trees = new[] { (TreeKind.Oak, new Vector3(-0.088f, 0f, -0.05f)), (TreeKind.Blossom, new Vector3(0.085f, 0f, -0.048f)), (TreeKind.Oak, new Vector3(0.088f, 0f, 0.055f)),
                (TreeKind.Conifer, new Vector3(-0.09f, 0f, 0.058f)), (TreeKind.Blossom, new Vector3(-0.03f, 0f, 0.07f)) };
            int v = 0;
            foreach (var (kind, at) in trees) b.Append(new MeshData(TreeFactory.Get(kind, v++ % 3)), Matrix4x4.TRS(at, Quaternion.Euler(0f, v * 70f, 0f), Vector3.one * 0.62f), Color.white);
            foreach (var at in new[] { new Vector3(0.045f, 0f, -0.07f), new Vector3(-0.045f, 0f, 0.004f) })
                for (int i = 0; i < 6; i++)
                    b.AddBlob(at + new Vector3(rng.Range(-0.01f, 0.01f), 0.003f, rng.Range(-0.005f, 0.005f)), Vector3.one * 0.0018f, 0, 0f, i, (_, __) => i % 3 == 0 ? Pal.FlowerPink : i % 3 == 1 ? Pal.FlowerYellow : Pal.FlowerRed);
            b.Append(new MeshData(Get(Catalog.Bench, 0)), Matrix4x4.TRS(new Vector3(-0.048f, 0f, -0.057f), Quaternion.Euler(0f, 180f, 0f), Vector3.one), Color.white);
            b.Append(new MeshData(Get(Catalog.Bench, 1)), Matrix4x4.TRS(new Vector3(0.057f, 0f, 0.054f), Quaternion.identity, Vector3.one), Color.white);
            b.Append(new MeshData(Get(Catalog.Lamp)), Matrix4x4.Translate(new Vector3(0.012f, 0f, -0.06f)), Color.white);
            return 0.07f;
        }

        private static float Pitch(MeshBuilder b)
        {
            const float W = 0.22f, D = 0.15f;
            var light = Pal.Hex("#6FB04A"); var dark = Pal.Hex("#5E9A3E"); var line = Pal.FrameWhite;
            for (int i = 0; i < 8; i++) b.AddBox(new Vector3(-W / 2 + (i + 0.5f) * W / 8f, -0.0015f, 0f), new Vector3(W / 8f, 0.004f, D), i % 2 == 0 ? dark : light, i % 2 == 0 ? dark : light, dark);
            float y = 0.0007f;
            void Line(Vector3 c, Vector3 size) => b.AddBox(c + new Vector3(0f, y, 0f), size, line);
            Line(new Vector3(0f, 0f, -D / 2 + 0.006f), new Vector3(W - 0.012f, 0.0006f, 0.0012f));
            Line(new Vector3(0f, 0f, D / 2 - 0.006f), new Vector3(W - 0.012f, 0.0006f, 0.0012f));
            foreach (int sx in new[] { -1, 1 })
            {
                Line(new Vector3(sx * (W / 2 - 0.006f), 0f, 0f), new Vector3(0.0012f, 0.0006f, D - 0.012f));
                Line(new Vector3(sx * (W / 2 - 0.022f), 0f, 0f), new Vector3(0.0012f, 0.0006f, 0.06f));
                Line(new Vector3(sx * (W / 2 - 0.014f), 0f, -0.03f), new Vector3(0.016f, 0.0006f, 0.0012f));
                Line(new Vector3(sx * (W / 2 - 0.014f), 0f, 0.03f), new Vector3(0.016f, 0.0006f, 0.0012f));
                // Goals with a net.
                float gx = sx * (W / 2 - 0.005f);
                foreach (int sz in new[] { -1, 1 }) b.AddCylinder(new Vector3(gx, 0f, sz * 0.016f), 0.0007f, 0.0007f, 0.013f, 5, line, line, false);
                b.Push(new Vector3(gx, 0.013f, 0f), Quaternion.Euler(90f, 0f, 0f));
                b.AddCylinder(new Vector3(0f, -0.016f, 0f), 0.0007f, 0.0007f, 0.032f, 5, line, line, false);
                b.Pop();
                b.AddBox(new Vector3(gx + sx * 0.005f, 0.0065f, 0f), new Vector3(0.0004f, 0.013f, 0.032f), new Color(0.9f, 0.9f, 0.9f));
            }
            Line(new Vector3(0f, 0f, 0f), new Vector3(0.0012f, 0.0006f, D - 0.012f));
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                b.AddBox(new Vector3(Mathf.Cos(a) * 0.018f, y, Mathf.Sin(a) * 0.018f), new Vector3(0.0028f, 0.0006f, 0.0028f), line);
            }
            // Corner flags.
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) Flag(b, new Vector3(sx * (W / 2 - 0.006f), 0f, sz * (D / 2 - 0.006f)), 0.012f, Pal.Hex("#FFD34D"));
            return 0.03f;
        }

        private static float CameraPole(MeshBuilder b)
        {
            var grey = Pal.Hex("#7E848C");
            b.AddCylinder(Vector3.zero, 0.0035f, 0.003f, 0.003f, 8, Pal.StoneDark, Pal.StoneDark, true);
            b.AddCylinder(new Vector3(0f, 0.003f, 0f), 0.0011f, 0.0009f, 0.076f, 6, grey, grey, true);
            b.AddBox(new Vector3(0f, 0.078f, -0.002f), new Vector3(0.002f, 0.002f, 0.006f), grey);
            b.AddBox(new Vector3(0f, 0.02f, -0.0012f), new Vector3(0.004f, 0.006f, 0.002f), Pal.Hex("#C9CDD2"));   // the junction box
            return 0.09f;
        }

        private static float LaunchField(MeshBuilder b, ref Rng rng)
        {
            // A mown circle, a launch basket stand and a wind sock. The balloon itself flies (see Rotor).
            b.AddCylinder(new Vector3(0f, -0.001f, 0f), 0.048f, 0.048f, 0.0016f, 24, Pal.Hex("#6FB04A"), Pal.Hex("#7FC05A"), true);
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                b.AddBox(new Vector3(Mathf.Cos(a) * 0.043f, 0.001f, Mathf.Sin(a) * 0.043f), new Vector3(0.004f, 0.001f, 0.004f), Pal.FrameWhite);
            }
            b.AddCylinder(new Vector3(0.036f, 0f, 0.03f), 0.0007f, 0.0007f, 0.03f, 4, Pal.TrainBlack, Pal.TrainBlack, false);
            b.Push(new Vector3(0.036f, 0.028f, 0.03f), Quaternion.Euler(0f, 0f, -80f));
            b.AddCylinder(Vector3.zero, 0.0022f, 0.0012f, 0.012f, 8, Pal.Hex("#FF7A3A"), Pal.FrameWhite, false, false);
            b.Pop();
            for (int i = 0; i < 2; i++) b.AddBox(new Vector3(-0.034f + i * 0.007f, 0.0035f, 0.032f), new Vector3(0.006f, 0.007f, 0.006f), Pal.Hex("#B07A45"));   // sandbags
            return 0.03f;
        }

        /// <summary>The balloon: a striped envelope, burner and a wicker basket with two passengers (pivot: the basket floor).</summary>
        private static void BalloonRotor(MeshBuilder b, ref Rng rng)
        {
            Color[] stripes = { Pal.Hex("#E25B4F"), Pal.Hex("#FFD34D"), Pal.Hex("#4A7FB8"), Pal.Hex("#F7F1E6") };
            var wicker = Pal.Hex("#B07A45");
            b.AddBox(new Vector3(0f, 0.004f, 0f), new Vector3(0.014f, 0.008f, 0.014f), wicker, Pal.Shade(wicker, 1.15f), Pal.Shade(wicker, 0.7f), false);
            Person(b, new Vector3(-0.003f, -0.006f, 0.002f), 160f, 2, Color.white, 0.6f);
            Person(b, new Vector3(0.003f, -0.006f, -0.002f), 20f, 1, Color.white, 0.6f);
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                b.AddCylinder(new Vector3(sx * 0.006f, 0.008f, sz * 0.006f), 0.0004f, 0.0004f, 0.024f, 3, Pal.Timber, Pal.Timber, false);
            b.AddCylinder(new Vector3(0f, 0.026f, 0f), 0.003f, 0.003f, 0.005f, 8, Pal.Hex("#C9CDD2"), Pal.Hex("#C9CDD2"), true);
            b.AddBlob(new Vector3(0f, 0.025f, 0f), Vector3.one * 0.0015f, 0, 0f, 2, (_, __) => Pal.Glow(Pal.Hex("#FFB04A")));
            b.AddBlob(new Vector3(0f, 0.066f, 0f), new Vector3(0.034f, 0.04f, 0.034f), 2, 0f, 13, (d, _) =>
            {
                if (d.y < -0.75f) return Pal.Shade(stripes[0], 0.8f);
                int k = Mathf.FloorToInt((Mathf.Atan2(d.z, d.x) + Mathf.PI) / (Mathf.PI * 2f) * 12f);
                return stripes[k % 4];
            });
        }

        private static Mesh TownRotor(int id, ref Rng rng)
        {
            if (id == Catalog.Disco) return MirrorBall();
            var b = new MeshBuilder();
            BalloonRotor(b, ref rng);
            return b.ToMesh("Balloon");
        }
    }
}
