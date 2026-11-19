using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The town's buildings, each given a silhouette of its own - recognisable from across the room before a sign can be
    /// read, as rich close up as the rest of the village. A bakery turns its tall gable to the street, the café sits
    /// under a curving mansard, the grocer is a long low shop behind a parapet, the restaurant has a glass conservatory,
    /// the furniture maker a stepped Dutch gable, the ice cream parlour is a round striped pavilion, the pub an old
    /// timber-framed inn with its upper floor jettied out over the street, the post office a little classical temple;
    /// the school has its bell cupola, the fire station its hose tower over an engine hall with three great doors, the
    /// police a flat-roofed modern block with its mast, the hospital wings round a helipad, the hotel a grand mansard
    /// with corner turrets. Same footprints and doors as before (the villagers and traffic know them).
    /// </summary>
    public static partial class HouseFactory
    {
        /// <summary>Whether a building has its own design here (and its signs with it).</summary>
        private static bool IsOverhauled(int id) => id is Catalog.Bakery or Catalog.Cafe or Catalog.Grocer or Catalog.Restaurant or Catalog.FurnitureShop or Catalog.IceCream
            or Catalog.Pub or Catalog.PostOffice or Catalog.School or Catalog.FireStation or Catalog.Police or Catalog.Hospital or Catalog.Hotel
            or Catalog.Apartments or Catalog.Farmhouse or Catalog.Theatre;

        private static float Overhauled(MeshBuilder b, ref Rng rng, Style s, int id, int style, out bool warp)
        {
            warp = true;
            switch (id)
            {
                case Catalog.Bakery: return GableShop(b, ref rng, s);
                case Catalog.Cafe: return MansardCafe(b, ref rng, s, style);
                case Catalog.Grocer: return ParapetGrocer(b, ref rng, s);
                case Catalog.Restaurant: return ConservatoryRestaurant(b, ref rng, s);
                case Catalog.FurnitureShop: return DutchGableShop(b, ref rng, s);
                case Catalog.Pub: return JettiedInn(b, ref rng, s);
            }
            warp = false;
            switch (id)
            {
                case Catalog.IceCream: return IcePavilion(b, ref rng);
                case Catalog.PostOffice: return TemplePostOffice(b, ref rng, s);
                case Catalog.School: return CupolaSchool(b, ref rng, s);
                case Catalog.FireStation: return EngineHall(b, ref rng, s);
                case Catalog.Police: return PoliceBlock(b, ref rng);
                case Catalog.Hospital: return HospitalWings(b, ref rng);
                case Catalog.Hotel: return GrandHotel(b, ref rng, s);
                case Catalog.Apartments: return ApartmentBlock(b, ref rng, s, style);
                case Catalog.Theatre: return TheatreHouse(b, ref rng, s);
            }
            warp = true;
            if (id == Catalog.Farmhouse) return FarmhouseLong(b, ref rng, s);
            warp = false;
            return -1f;
        }

        // ---------------------------------------------------------------- shared parts

        /// <summary>A window on a wall facing <paramref name="normal"/> (its centre at <paramref name="at"/>): the village's own, lit at night.</summary>
        private static void WallWindow(MeshBuilder b, ref Rng rng, Style s, Vector3 at, Vector3 normal, float w, float h, bool shutters = false, bool flowers = false, bool arched = false)
        {
            b.Push(at, Quaternion.LookRotation(normal, Vector3.up));
            Window(b, ref rng, w, h, s, shutters, flowers, arched);
            b.Pop();
        }

        private static void WallDoor(MeshBuilder b, ref Rng rng, Style s, Vector3 foot, Vector3 normal, float w, float h, bool arched = false, bool canopy = false)
        {
            b.Push(foot, Quaternion.LookRotation(normal, Vector3.up));
            Door(b, ref rng, w, h, s, arched, canopy);
            b.Pop();
        }

        /// <summary>A hipped roof: four slopes rising from the eaves of a <paramref name="w"/> by <paramref name="d"/> rectangle to a ridge.</summary>
        private static void HipRoof(MeshBuilder b, Vector3 eave, float w, float d, float h, Color roof, float overhang = 0.004f)
        {
            float ex = w / 2 + overhang, ez = d / 2 + overhang, ridge = Mathf.Max(0f, ex - ez);
            var a = eave + new Vector3(-ex, 0f, -ez); var c = eave + new Vector3(ex, 0f, -ez); var e = eave + new Vector3(ex, 0f, ez); var f = eave + new Vector3(-ex, 0f, ez);
            var r0 = eave + new Vector3(-ridge, h, 0f); var r1 = eave + new Vector3(ridge, h, 0f);
            var inside = eave + Vector3.up * (h * 0.3f);
            var light = Pal.Shade(roof, 1.12f);
            b.AddTriangleOutward(a, c, r1, inside, roof); b.AddTriangleOutward(a, r1, r0, inside, roof);
            b.AddTriangleOutward(e, f, r0, inside, light); b.AddTriangleOutward(e, r0, r1, inside, light);
            b.AddTriangleOutward(c, e, r1, inside, Pal.Shade(roof, 1.05f)); b.AddTriangleOutward(f, a, r0, inside, Pal.Shade(roof, 0.95f));
            Courses(b, eave, ex, ez, h, roof);
        }

        /// <summary>Courses of tiles across a roof's front and back slopes (thin lines, close up only).</summary>
        private static void Courses(MeshBuilder b, Vector3 eave, float ex, float ez, float h, Color roof)
        {
            if (b.TooSmall(0.003f)) return;
            int n = Mathf.Max(2, Mathf.RoundToInt(h / 0.006f));
            float pitch = Mathf.Atan2(h, ez) * Mathf.Rad2Deg;
            for (int k = 1; k < n; k++)
            {
                float t = k / (float)n;
                foreach (int side in new[] { -1, 1 })
                {
                    b.Push(eave + new Vector3(0f, h * t, side * ez * (1f - t) * 1.01f), Quaternion.Euler(side * pitch, 0f, 0f));
                    b.AddBox(Vector3.zero, new Vector3(ex * 2f * (1f - t * 0.3f), 0.0007f, 0.0012f), Pal.Shade(roof, 0.82f));
                    b.Pop();
                }
            }
        }

        /// <summary>A mansard: steep curving lower slopes with dormers, a shallow top.</summary>
        private static void MansardRoof(MeshBuilder b, ref Rng rng, Style s, Vector3 eave, float w, float d, float h, Color roof, int dormers)
        {
            float inset = Mathf.Min(w, d) * 0.22f;
            const int steps = 4;
            for (int k = 0; k < steps; k++)
            {
                float t0 = k / (float)steps, t1 = (k + 1) / (float)steps;
                float i0 = inset * Mathf.Pow(t0, 2.2f), i1 = inset * Mathf.Pow(t1, 2.2f);
                float y0 = h * 0.8f * t0, y1 = h * 0.8f * t1;
                var p = new[] { new Vector3(-w / 2 + i0, y0, -d / 2 + i0), new Vector3(w / 2 - i0, y0, -d / 2 + i0), new Vector3(w / 2 - i0, y0, d / 2 - i0), new Vector3(-w / 2 + i0, y0, d / 2 - i0) };
                var q = new[] { new Vector3(-w / 2 + i1, y1, -d / 2 + i1), new Vector3(w / 2 - i1, y1, -d / 2 + i1), new Vector3(w / 2 - i1, y1, d / 2 - i1), new Vector3(-w / 2 + i1, y1, d / 2 - i1) };
                for (int e = 0; e < 4; e++)
                {
                    int f = (e + 1) % 4;
                    var shade = Pal.Shade(roof, 0.92f + 0.05f * k);
                    b.AddTriangleOutward(eave + p[e], eave + p[f], eave + q[f], eave + Vector3.up * h * 0.3f, shade);
                    b.AddTriangleOutward(eave + p[e], eave + q[f], eave + q[e], eave + Vector3.up * h * 0.3f, shade);
                }
            }
            // The top, nearly flat, and its cresting.
            var top = eave + Vector3.up * h * 0.8f;
            b.AddBox(top + Vector3.up * h * 0.1f, new Vector3(w - inset * 2f, h * 0.2f, d - inset * 2f), Pal.Shade(roof, 1.1f), Pal.Shade(roof, 1.15f), roof);
            if (!b.TooSmall(0.002f)) b.AddBox(top + Vector3.up * h * 0.21f, new Vector3(w - inset * 2f, 0.0012f, 0.0012f), Pal.Brass);
            for (int k = 0; k < dormers; k++)
            {
                float x = -w / 2 + w * (k + 0.5f) / dormers;
                var at = eave + new Vector3(x, h * 0.35f, -d / 2 + inset * 0.45f);
                b.AddBox(at + Vector3.up * 0.006f, new Vector3(0.012f, 0.014f, 0.008f), s.Trim);
                b.AddCylinder(at + new Vector3(0f, 0.013f, 0f), 0.0075f, 0.0075f, 0.008f, 12, roof, roof, true, false, true);
                WallWindow(b, ref rng, s, at + new Vector3(0f, 0.006f, -0.0042f), Vector3.back, 0.0075f, 0.009f, false, false, true);
            }
        }

        /// <summary>A flat roof behind a parapet with a coping, a few roof boxes.</summary>
        private static void FlatRoof(MeshBuilder b, Vector3 top, float w, float d, Color wall, Color coping)
        {
            b.AddBox(top + Vector3.up * 0.0006f, new Vector3(w - 0.004f, 0.0012f, d - 0.004f), Pal.Hex("#8A8A86"));
            foreach (int sz in new[] { -1, 1 }) b.AddBox(top + new Vector3(0f, 0.003f, sz * (d / 2 - 0.0015f)), new Vector3(w, 0.006f, 0.003f), wall, coping, wall);
            foreach (int sx in new[] { -1, 1 }) b.AddBox(top + new Vector3(sx * (w / 2 - 0.0015f), 0.003f, 0f), new Vector3(0.003f, 0.006f, d), wall, coping, wall);
        }

        /// <summary>A sign hanging from an iron bracket on a wall: a board with a picture, swinging out over the pavement.</summary>
        private static void BracketSign(MeshBuilder b, Vector3 wall, Vector3 normal, System.Action<Vector3> picture, Color board)
        {
            var iron = Pal.Hex("#2E3238");
            var arm = wall + normal * 0.012f;
            b.Push((wall + arm) / 2f, Quaternion.LookRotation(normal, Vector3.up));
            b.AddBox(Vector3.zero, new Vector3(0.0008f, 0.0008f, 0.012f), iron);
            b.Pop();
            b.AddBox(arm + Vector3.down * 0.007f, new Vector3(Mathf.Abs(normal.z) * 0.0008f + Mathf.Abs(normal.x) * 0.014f, 0.012f, Mathf.Abs(normal.x) * 0.0008f + Mathf.Abs(normal.z) * 0.014f), board);
            picture(arm + Vector3.down * 0.007f);
        }

        // ---------------------------------------------------------------- shops

        /// <summary>
        /// The bakery: a tall narrow house turning its steep gable to the street, half-timbered upstairs, a big bay shop
        /// window full of loaves below, the oven's broad chimney stack up the side, and a golden pretzel hanging out over
        /// the pavement.
        /// </summary>
        private static float GableShop(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.07f, d = 0.08f, wallH = 0.072f, roofH = 0.06f;
            Foundation(b, ref rng, w, d);
            b.AddBox(new Vector3(0f, 0.004f + wallH / 2, 0f), new Vector3(w, wallH, d), s.Plaster, s.Plaster, s.Plaster);
            TimberFrameFront(b, w, 0.04f, wallH, -d / 2, s.Timber);
            // The gable to the street (the ridge runs front to back).
            GableRoofColoured(b, new Vector3(0f, 0.004f + wallH, 0f), w + 0.008f, d + 0.01f, roofH, s.Roof, s.Plaster, alongZ: true);
            // A bay window on the ground floor, loaves on its shelf.
            var bay = new Vector3(0f, 0.004f, -d / 2);
            b.AddBox(bay + new Vector3(0f, 0.0035f, -0.006f), new Vector3(0.05f, 0.007f, 0.012f), s.Timber);
            b.AddBox(bay + new Vector3(0f, 0.018f, -0.006f), new Vector3(0.048f, 0.022f, 0.011f), Pal.Glow(Pal.WindowGlow), Pal.Glow(Pal.WindowGlow), s.Timber);
            if (!b.TooSmall(0.002f)) for (int k = -2; k <= 2; k++) b.AddBox(bay + new Vector3(k * 0.012f, 0.018f, -0.0118f), new Vector3(0.0012f, 0.022f, 0.0008f), s.Trim);
            b.AddBox(bay + new Vector3(0f, 0.0305f, -0.006f), new Vector3(0.052f, 0.003f, 0.013f), s.Roof);
            if (!b.TooSmall(0.003f)) for (int k = 0; k < 6; k++) b.AddBlob(bay + new Vector3(-0.018f + k * 0.0072f, 0.0105f, -0.009f), new Vector3(0.0028f, 0.0016f, 0.0018f), 1, 0f, k, (_, __) => Pal.Hex("#C98A3E"));
            Lettering.Board(b, "BAKERY", bay + new Vector3(0f, 0.036f, -0.0002f), Vector3.back, 0.005f, Pal.Hex("#8E2F25"), Pal.Hex("#F2D27A"), s.Timber);
            WallDoor(b, ref rng, s, new Vector3(-0.0275f, 0.004f, -d / 2), Vector3.back, 0.012f, 0.026f);
            // Upstairs: two windows and a little round one in the gable.
            foreach (float x in new[] { -0.017f, 0.017f }) WallWindow(b, ref rng, s, new Vector3(x, 0.052f, -d / 2), Vector3.back, 0.011f, 0.015f, true, true);
            b.Push(new Vector3(0f, 0.004f + wallH + roofH * 0.38f, -d / 2 - 0.005f), Quaternion.LookRotation(Vector3.back));
            RoundWindow(b, 0.0055f, s.Trim, s);
            b.Pop();
            // Side windows.
            foreach (int sx in new[] { -1, 1 }) for (int k = 0; k < 2; k++) WallWindow(b, ref rng, s, new Vector3(sx * w / 2, 0.022f + k * 0.03f, 0.012f), sx > 0 ? Vector3.right : Vector3.left, 0.01f, 0.014f, true, false);
            // The oven's chimney stack up the side, smoke-stained at the top.
            b.AddBox(new Vector3(w / 2 + 0.006f, 0.06f, 0.02f), new Vector3(0.012f, 0.12f, 0.016f), Pal.Hex("#A85A3E"), Pal.Hex("#5A4A42"), Pal.Hex("#A85A3E"));
            // The pretzel sign over the pavement.
            BracketSign(b, new Vector3(0.03f, 0.042f, -d / 2), Vector3.back, at => Pretzel(b, at + Vector3.back * 0.001f), Pal.Hex("#2E3238"));
            return 0.004f + wallH + roofH;
        }

        private static void Pretzel(MeshBuilder b, Vector3 at)
        {
            var gold = Pal.Hex("#D9A040");
            for (int k = 0; k < 14; k++)
            {
                float a = k / 14f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Sin(a) * 0.0055f * (1f + 0.25f * Mathf.Cos(2f * a)), Mathf.Cos(a) * 0.0045f, 0f);
                b.AddBlob(at + p, Vector3.one * 0.0013f, 0, 0f, k, (_, __) => gold);
            }
        }

        private static void TimberFrameFront(MeshBuilder b, float w, float y0, float y1, float z, Color timber)
        {
            if (b.TooSmall(0.002f)) return;
            for (int k = 0; k <= 4; k++) b.AddBox(new Vector3(-w / 2 + k * w / 4, (y0 + y1) / 2 + 0.004f, z - 0.0004f), new Vector3(0.0018f, y1 - y0, 0.0008f), timber);
            b.AddBox(new Vector3(0f, y0 + 0.004f, z - 0.0004f), new Vector3(w, 0.0022f, 0.0008f), timber);
            foreach (int sx in new[] { -1, 1 })
            {
                b.Push(new Vector3(sx * w * 0.375f, (y0 + y1) / 2 + 0.004f, z - 0.0004f), Quaternion.Euler(0f, 0f, sx * 40f));
                b.AddBox(Vector3.zero, new Vector3(0.0016f, (y1 - y0) * 1.1f, 0.0008f), timber);
                b.Pop();
            }
        }

        /// <summary>A gable roof whose gable ends are the given wall colour (see GableRoof).</summary>
        private static void GableRoofColoured(MeshBuilder b, Vector3 eave, float w, float d, float h, Color roof, Color gable, bool alongZ)
        {
            int first = b.VertexCount;
            GableRoof(b, eave, w, d, h, roof, alongZ);
            for (int i = first; i < b.VertexCount; i++) if (b.Colors[i] == CastleWall) b.Colors[i] = gable;
        }

        /// <summary>
        /// The café: a two-storey corner house under a curving slate mansard with arched dormers, a long striped awning,
        /// tall French windows and a terrace of little tables under parasols.
        /// </summary>
        private static float MansardCafe(MeshBuilder b, ref Rng rng, Style s, int style)
        {
            const float w = 0.088f, d = 0.06f, wallH = 0.06f, roofH = 0.034f;
            Foundation(b, ref rng, w, d);
            b.AddBox(new Vector3(0f, 0.004f + wallH / 2, 0.008f), new Vector3(w, wallH, d), s.Plaster, s.Plaster, s.Plaster);
            b.AddBox(new Vector3(0f, 0.004f + 0.031f, 0.008f), new Vector3(w + 0.002f, 0.002f, d + 0.002f), s.Trim);
            b.AddBox(new Vector3(0f, 0.004f + wallH - 0.002f, 0.008f), new Vector3(w + 0.004f, 0.004f, d + 0.004f), s.Trim);
            MansardRoof(b, ref rng, s, new Vector3(0f, 0.004f + wallH, 0.008f), w + 0.004f, d + 0.004f, roofH, Pal.Hex("#4A5468"), 3);
            float z = -d / 2 + 0.008f;
            for (int k = 0; k < 4; k++)
            {
                float x = -w / 2 + w * (k + 0.5f) / 4f;
                if (k == 1) WallDoor(b, ref rng, s, new Vector3(x, 0.004f, z), Vector3.back, 0.012f, 0.024f, true);
                else WallWindow(b, ref rng, s, new Vector3(x, 0.018f, z), Vector3.back, 0.013f, 0.022f, false, false, true);
                WallWindow(b, ref rng, s, new Vector3(x, 0.048f, z), Vector3.back, 0.01f, 0.016f, true, true);
                if (!b.TooSmall(0.002f)) b.AddBox(new Vector3(x, 0.0405f, z - 0.003f), new Vector3(0.015f, 0.0008f, 0.005f), Pal.Hex("#2E3238"));   // little balconies
            }
            Awning(b, new Vector3(0f, 0.033f, z - 0.002f), w * 0.95f, 0.016f, style % 2 == 0 ? Pal.Hex("#2E4E7A") : Pal.Hex("#8E2F25"), Pal.Hex("#F7F1E6"));
            Lettering.Board(b, "CAFE", new Vector3(0f, 0.0415f, z - 0.0005f), Vector3.back, 0.0045f, Pal.Hex("#2E4E7A"), Pal.Hex("#F7F1E6"), s.Trim);
            foreach (float x in new[] { -0.03f, 0.022f }) { b.AddCylinder(new Vector3(x, 0.01f, -0.072f), 0.0006f, 0.0006f, 0.02f, 6, Pal.Hex("#E8E2D6"), Pal.Hex("#E8E2D6"), false); b.AddCylinder(new Vector3(x, 0.028f, -0.072f), 0.014f, 0.001f, 0.006f, 12, Pal.Hex("#F7F1E6"), Pal.Hex("#2E4E7A"), true); }
            return 0.004f + wallH + roofH;
        }

        /// <summary>The grocer: a long, low shop behind a stepped parapet with its name, a deep canopy over crates of fruit and veg.</summary>
        private static float ParapetGrocer(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.096f, d = 0.06f, wallH = 0.044f;
            Foundation(b, ref rng, w, d);
            b.AddBox(new Vector3(0f, 0.004f + wallH / 2, 0.008f), new Vector3(w, wallH, d), s.Plaster, s.Plaster, s.Plaster);
            FlatRoof(b, new Vector3(0f, 0.004f + wallH, 0.008f), w, d, s.Plaster, s.Trim);
            // The stepped parapet over the front with the name.
            float z = -d / 2 + 0.008f;
            b.AddBox(new Vector3(0f, 0.004f + wallH + 0.008f, z), new Vector3(0.06f, 0.016f, 0.004f), s.Plaster, s.Trim, s.Plaster);
            b.AddBox(new Vector3(0f, 0.004f + wallH + 0.018f, z), new Vector3(0.028f, 0.006f, 0.004f), s.Plaster, s.Trim, s.Plaster);
            Lettering.Board(b, "GROCER", new Vector3(0f, 0.004f + wallH + 0.008f, z - 0.0022f), Vector3.back, 0.0055f, Pal.Hex("#2E6B3A"), Pal.Hex("#F7F1E6"), s.Trim);
            Big(b, new Vector3(0f, 0.004f + wallH + 0.03f, z), at => Apple(b, at));
            // Wide shop windows and the door, a deep green canopy.
            foreach (float x in new[] { -0.03f, 0.03f }) b.AddFace(new Vector3(x, 0.019f, z - 0.0003f), Vector3.back, Vector3.up, 0.016f, 0.012f, Pal.Glow(Pal.WindowGlow));
            WallDoor(b, ref rng, s, new Vector3(0f, 0.004f, z), Vector3.back, 0.012f, 0.026f);
            Awning(b, new Vector3(0f, 0.035f, z - 0.002f), w, 0.02f, Pal.Hex("#2E6B3A"), Pal.Hex("#F7F1E6"));
            // Crates of produce on trestles out front.
            var fruit = new[] { Pal.Hex("#E2383E"), Pal.Hex("#F2A030"), Pal.Hex("#7FB25A"), Pal.Hex("#7A3A82") };
            for (int k = 0; k < 4; k++)
            {
                var c = new Vector3(-0.036f + k * 0.024f, 0f, z - 0.016f);
                b.AddBox(c + Vector3.up * 0.005f, new Vector3(0.018f, 0.004f, 0.01f), Pal.Hex("#A8743E"));
                if (!b.TooSmall(0.003f)) for (int f = 0; f < 6; f++) b.AddBlob(c + new Vector3(-0.006f + (f % 3) * 0.006f, 0.0082f, -0.002f + (f / 3) * 0.004f), Vector3.one * 0.0019f, 0, 0f, k * 7 + f, (_, __) => fruit[k]);
                else b.AddBox(c + Vector3.up * 0.008f, new Vector3(0.016f, 0.002f, 0.008f), fruit[k]);
            }
            return 0.004f + wallH + 0.03f;
        }

        /// <summary>The restaurant: a long house under a hipped roof, a glass conservatory across its front for dining, string lights over the terrace.</summary>
        private static float ConservatoryRestaurant(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.12f, d = 0.05f, wallH = 0.05f;
            Foundation(b, ref rng, w, d + 0.02f);
            b.AddBox(new Vector3(0f, 0.004f + wallH / 2, 0.012f), new Vector3(w, wallH, d), s.Plaster, s.Plaster, s.Plaster);
            HipRoof(b, new Vector3(0f, 0.004f + wallH, 0.012f), w, d, 0.035f, Pal.Hex("#5A1E2A"));
            for (int k = 0; k < 5; k++) WallWindow(b, ref rng, s, new Vector3(-0.048f + k * 0.024f, 0.042f, -d / 2 + 0.012f), Vector3.back, 0.011f, 0.012f, true, true);
            // The conservatory: glass walls and a lean-to glass roof on white bars.
            var glass = new Color(0.82f, 0.92f, 0.95f, 1f); var bar = Pal.Hex("#F2F2EE");
            var c = new Vector3(0f, 0.004f, -d / 2 + 0.012f - 0.012f);
            b.AddBox(c + new Vector3(0f, 0.013f, 0f), new Vector3(0.1f, 0.026f, 0.024f), glass, glass, glass);
            b.Push(c + new Vector3(0f, 0.029f, 0.002f), Quaternion.Euler(-18f, 0f, 0f));
            b.AddBox(Vector3.zero, new Vector3(0.104f, 0.0012f, 0.028f), glass);
            b.Pop();
            if (!b.TooSmall(0.002f)) for (int k = 0; k <= 8; k++) b.AddBox(c + new Vector3(-0.05f + k * 0.0125f, 0.014f, -0.0122f), new Vector3(0.0012f, 0.028f, 0.0012f), bar);
            b.AddBox(c + new Vector3(0f, 0.026f, -0.0122f), new Vector3(0.1f, 0.0014f, 0.0014f), bar);
            // Tables inside, lit.
            if (!b.TooSmall(0.003f)) for (int k = 0; k < 4; k++) b.AddCylinder(c + new Vector3(-0.036f + k * 0.024f, 0f, 0f), 0.004f, 0.004f, 0.008f, 8, Pal.Hex("#F7F1E6"), Pal.Hex("#F7F1E6"), true);
            Lettering.Board(b, "RESTAURANT", new Vector3(0f, 0.0375f, -d / 2 + 0.0115f), Vector3.back, 0.0042f, Pal.Hex("#5A1E2A"), Pal.Hex("#F2D27A"), s.Trim);
            StringLights(b);
            return 0.004f + wallH + 0.035f;
        }

        /// <summary>The furniture maker: a red-brick shop with a stepped Dutch gable to the street, big display windows, a chair hung over the door.</summary>
        private static float DutchGableShop(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.074f, d = 0.07f, wallH = 0.06f;
            var brick = Pal.Hex("#A8523E");
            Foundation(b, ref rng, w, d);
            b.AddBox(new Vector3(0f, 0.004f + wallH / 2, 0f), new Vector3(w, wallH, d), brick, brick, brick);
            GableRoofColoured(b, new Vector3(0f, 0.004f + wallH, 0.004f), w - 0.006f, d - 0.004f, 0.036f, Pal.Hex("#4A4440"), brick, alongZ: true);
            // The stepped gable rising in front of the roof.
            for (int k = 0; k < 5; k++)
            {
                float half = w / 2 * (1f - k * 0.18f), y = 0.004f + wallH + k * 0.0085f;
                b.AddBox(new Vector3(0f, y + 0.00425f, -d / 2 + 0.002f), new Vector3(half * 2f, 0.0085f, 0.004f), brick, Pal.Hex("#E8DCC8"), brick);
            }
            b.AddBlob(new Vector3(0f, 0.004f + wallH + 0.046f, -d / 2 + 0.002f), new Vector3(0.004f, 0.004f, 0.002f), 0, 0f, 1, (_, __) => Pal.Hex("#E8DCC8"));
            // Display windows with a chair and a table in them; the door between.
            foreach (int sx in new[] { -1, 1 })
            {
                b.AddFace(new Vector3(sx * 0.022f, 0.02f, -d / 2 - 0.0003f), Vector3.back, Vector3.up, 0.013f, 0.013f, Pal.Glow(Pal.WindowGlow));
                b.AddBox(new Vector3(sx * 0.022f, 0.006f, -d / 2 - 0.001f), new Vector3(0.028f, 0.004f, 0.002f), Pal.Hex("#E8DCC8"));
                if (!b.TooSmall(0.003f)) { b.AddBox(new Vector3(sx * 0.022f, 0.012f, -d / 2 + 0.003f), new Vector3(0.008f, 0.006f, 0.006f), Pal.Hex("#8A5A34")); b.AddBox(new Vector3(sx * 0.022f + 0.003f, 0.016f, -d / 2 + 0.005f), new Vector3(0.002f, 0.008f, 0.004f), Pal.Hex("#8A5A34")); }
            }
            WallDoor(b, ref rng, s, new Vector3(0f, 0.004f, -d / 2), Vector3.back, 0.011f, 0.026f, true);
            for (int k = 0; k < 3; k++) WallWindow(b, ref rng, s, new Vector3(-0.022f + k * 0.022f, 0.048f, -d / 2), Vector3.back, 0.01f, 0.015f, false, true);
            Lettering.Board(b, "FURNITURE", new Vector3(0f, 0.037f, -d / 2 - 0.0005f), Vector3.back, 0.0042f, Pal.Hex("#5A3A22"), Pal.Hex("#F2E3C2"), Pal.Hex("#E8DCC8"));
            BracketSign(b, new Vector3(0.03f, 0.044f, -d / 2), Vector3.back, at =>
            {
                b.AddBox(at + new Vector3(0f, -0.002f, -0.001f), new Vector3(0.007f, 0.0012f, 0.0012f), Pal.Hex("#C8A060"));
                b.AddBox(at + new Vector3(-0.003f, 0.002f, -0.001f), new Vector3(0.0012f, 0.008f, 0.0012f), Pal.Hex("#C8A060"));
            }, Pal.Hex("#5A3A22"));
            return 0.004f + wallH + 0.05f;
        }

        /// <summary>The ice cream parlour: a round pavilion with a pink-and-white striped cone roof, a giant ice cream on top, and little tables round it.</summary>
        private static float IcePavilion(MeshBuilder b, ref Rng rng)
        {
            b.AddCylinder(Vector3.zero, 0.036f, 0.036f, 0.004f, 24, Pal.Hex("#E8E2D6"), Pal.Hex("#F2EDE4"), true);
            b.AddCylinder(new Vector3(0f, 0.004f, 0f), 0.022f, 0.022f, 0.03f, 24, Pal.Hex("#FFF4F8"), Pal.Hex("#FFF4F8"), false);
            for (int k = 0; k < 6; k++)
            {
                float a = k / 6f * Mathf.PI * 2f;
                var n = new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a));
                b.AddFace(new Vector3(0f, 0.019f, 0f) + n * 0.0222f, n, Vector3.up, 0.007f, 0.009f, Pal.Glow(Pal.WindowGlow));
            }
            // The striped roof: alternating segments.
            const int seg = 16;
            for (int k = 0; k < seg; k++)
            {
                float a0 = k / (float)seg * Mathf.PI * 2f, a1 = (k + 1) / (float)seg * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.03f, 0.034f, Mathf.Sin(a0) * 0.03f); var p1 = new Vector3(Mathf.Cos(a1) * 0.03f, 0.034f, Mathf.Sin(a1) * 0.03f);
                b.AddTriangleOutward(p0, p1, new Vector3(0f, 0.058f, 0f), new Vector3(0f, 0.03f, 0f), k % 2 == 0 ? Pal.Hex("#F2A7C3") : Pal.Hex("#FFFFFF"));
            }
            if (!b.TooSmall(0.002f)) for (int k = 0; k < seg; k++) { float a = (k + 0.5f) / seg * Mathf.PI * 2f; b.AddBlob(new Vector3(Mathf.Cos(a) * 0.03f, 0.0325f, Mathf.Sin(a) * 0.03f), Vector3.one * 0.0016f, 0, 0f, k, (_, __) => k % 2 == 0 ? Pal.Hex("#F2A7C3") : Pal.Hex("#FFFFFF")); }
            Cone(b, new Vector3(0f, 0.062f, 0f));
            Lettering.Board(b, "ICE CREAM", new Vector3(0f, 0.0285f, -0.0225f), Vector3.back, 0.0032f, Pal.Hex("#F2A7C3"), Pal.Hex("#7A3A52"), Pal.Hex("#FFFFFF"));
            foreach (float x in new[] { -0.024f, 0.024f })
            {
                b.AddCylinder(new Vector3(x, 0.004f, -0.05f), 0.005f, 0.005f, 0.0008f, 10, Pal.Hex("#FFFFFF"), Pal.Hex("#FFFFFF"), true);
                b.AddCylinder(new Vector3(x, 0.004f, -0.05f), 0.0006f, 0.0006f, 0.006f, 6, Pal.Hex("#FFFFFF"), Pal.Hex("#FFFFFF"), false);
                b.AddCylinder(new Vector3(x, 0.004f, -0.05f), 0.0005f, 0.0005f, 0.02f, 6, Pal.Hex("#E8E2D6"), Pal.Hex("#E8E2D6"), false);
                b.AddCylinder(new Vector3(x, 0.022f, -0.05f), 0.012f, 0.001f, 0.005f, 12, Pal.Hex("#F2A7C3"), Pal.Hex("#FFFFFF"), true);
            }
            return 0.075f;
        }

        /// <summary>
        /// The pub: an old timber-framed inn - its upper floor jettied out over the street on carved brackets, dark beams
        /// on white, a deep tiled roof with a tall chimney, small-paned windows, a sign of the red lion swinging from a
        /// post, benches and barrels out front.
        /// </summary>
        private static float JettiedInn(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.1f, d = 0.07f, ground = 0.03f, upper = 0.03f;
            var white = Pal.Hex("#F4EEE2"); var beam = Pal.Hex("#3A2A20");
            Foundation(b, ref rng, w, d);
            b.AddBox(new Vector3(0f, 0.004f + ground / 2, 0.006f), new Vector3(w, ground, d), white, white, white);
            // The jettied upper floor, 6 mm proud of the front, on brackets.
            b.AddBox(new Vector3(0f, 0.004f + ground + upper / 2, 0.0f), new Vector3(w + 0.004f, upper, d + 0.012f), white, white, white);
            if (!b.TooSmall(0.002f)) for (int k = 0; k < 6; k++) { b.Push(new Vector3(-w / 2 + 0.008f + k * (w - 0.016f) / 5f, 0.004f + ground - 0.003f, -d / 2 + 0.003f), Quaternion.Euler(45f, 0f, 0f)); b.AddBox(Vector3.zero, new Vector3(0.002f, 0.002f, 0.009f), beam); b.Pop(); }
            foreach (var (y0, y1, z) in new[] { (0.004f, 0.004f + ground, -d / 2 + 0.006f), (0.004f + ground, 0.004f + ground + upper, -d / 2 - 0.006f) })
            {
                if (b.TooSmall(0.002f)) break;
                for (int k = 0; k <= 6; k++) b.AddBox(new Vector3(-w / 2 + k * w / 6, (y0 + y1) / 2, z - 0.0005f), new Vector3(0.002f, y1 - y0, 0.001f), beam);
                b.AddBox(new Vector3(0f, y1 - 0.001f, z - 0.0005f), new Vector3(w + 0.002f, 0.002f, 0.001f), beam);
                for (int k = 0; k < 6; k += 2) { b.Push(new Vector3(-w / 2 + (k + 0.5f) * w / 6, (y0 + y1) / 2, z - 0.0005f), Quaternion.Euler(0f, 0f, 35f)); b.AddBox(Vector3.zero, new Vector3(0.0015f, (y1 - y0) * 1.05f, 0.001f), beam); b.Pop(); }
            }
            GableRoofColoured(b, new Vector3(0f, 0.004f + ground + upper, 0f), w + 0.01f, d + 0.022f, 0.038f, Pal.Hex("#6A4A3A"), white, alongZ: false);
            b.AddBox(new Vector3(-0.03f, 0.004f + ground + upper + 0.03f, 0.012f), new Vector3(0.012f, 0.04f, 0.01f), Pal.Hex("#8E4A3A"), Pal.Hex("#3A3030"), Pal.Hex("#8E4A3A"));
            for (int k = 0; k < 3; k++) WallWindow(b, ref rng, s, new Vector3(-0.03f + k * 0.03f, 0.004f + ground + upper * 0.5f, -d / 2 - 0.006f), Vector3.back, 0.014f, 0.011f, false, true);
            foreach (float x in new[] { -0.03f, 0.03f }) WallWindow(b, ref rng, s, new Vector3(x, 0.02f, -d / 2 + 0.006f), Vector3.back, 0.016f, 0.012f);
            WallDoor(b, ref rng, s, new Vector3(-0.002f, 0.004f, -d / 2 + 0.006f), Vector3.back, 0.012f, 0.024f, true);
            // The inn sign on its post.
            var post = new Vector3(0.044f, 0f, -d / 2 - 0.016f);
            b.AddCylinder(post, 0.0012f, 0.001f, 0.05f, 6, beam, beam, true);
            b.AddBox(post + new Vector3(-0.007f, 0.047f, 0f), new Vector3(0.014f, 0.0014f, 0.0014f), beam);
            b.AddBox(post + new Vector3(-0.009f, 0.036f, 0f), new Vector3(0.016f, 0.018f, 0.0012f), Pal.Hex("#2E5A3A"));
            b.AddBlob(post + new Vector3(-0.009f, 0.036f, -0.0008f), new Vector3(0.005f, 0.005f, 0.0005f), 1, 0f, 2, (_, __) => Pal.Hex("#C8392E"));
            Lettering.Board(b, "THE RED LION", new Vector3(0f, 0.004f + ground + upper - 0.001f, -d / 2 - 0.0075f), Vector3.back, 0.0034f, Pal.Hex("#2E5A3A"), Pal.Hex("#F2D27A"), beam);
            // Benches and barrels.
            foreach (float x in new[] { -0.03f, 0.018f }) { b.AddBox(new Vector3(x, 0.006f, -0.058f), new Vector3(0.02f, 0.0015f, 0.008f), Pal.Hex("#8A5A34")); b.AddBox(new Vector3(x, 0.003f, -0.058f), new Vector3(0.016f, 0.006f, 0.002f), Pal.Hex("#6A4A2A")); }
            b.AddCylinder(new Vector3(-0.046f, 0f, -0.046f), 0.004f, 0.004f, 0.009f, 10, Pal.Hex("#8A5A34"), Pal.Hex("#6A4A2A"), true);
            return 0.004f + ground + upper + 0.05f;
        }

        // ---------------------------------------------------------------- public buildings

        /// <summary>The post office: a little classical temple in cream stone - four columns under a pediment, a clock in it, the doors red, a letterbox by the steps.</summary>
        private static float TemplePostOffice(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.084f, d = 0.06f, wallH = 0.05f;
            var stone = Pal.Hex("#EDE3CC"); var red = Pal.Hex("#C8392E");
            b.AddBox(new Vector3(0f, 0.003f, 0.004f), new Vector3(w + 0.006f, 0.006f, d + 0.01f), Pal.Shade(stone, 0.9f));
            for (int k = 0; k < 3; k++) b.AddBox(new Vector3(0f, 0.001f + k * 0.002f, -d / 2 - 0.004f - k * 0.0025f), new Vector3(0.04f, 0.002f, 0.004f), Pal.Shade(stone, 0.95f));   // the steps
            b.AddBox(new Vector3(0f, 0.006f + wallH / 2, 0.01f), new Vector3(w, wallH, d - 0.012f), stone, stone, stone);
            FlatRoof(b, new Vector3(0f, 0.006f + wallH, 0.01f), w, d - 0.012f, stone, Pal.Shade(stone, 1.06f));
            // The portico: four columns, an entablature, the pediment with a clock.
            for (int k = 0; k < 4; k++) b.AddCylinder(new Vector3(-0.024f + k * 0.016f, 0.006f, -d / 2 + 0.002f), 0.0032f, 0.0028f, wallH - 0.006f, 12, stone, stone, true);
            b.AddBox(new Vector3(0f, 0.006f + wallH - 0.003f, -d / 2 + 0.004f), new Vector3(0.06f, 0.006f, 0.014f), stone, Pal.Shade(stone, 1.05f), stone);
            var pTop = new Vector3(0f, 0.006f + wallH + 0.018f, -d / 2 - 0.003f);
            var pl = new Vector3(-0.031f, 0.006f + wallH, -d / 2 - 0.003f); var pr = new Vector3(0.031f, 0.006f + wallH, -d / 2 - 0.003f);
            b.AddTriangleOutward(pl, pr, pTop, pTop + Vector3.forward * 0.01f + Vector3.down * 0.01f, stone);
            for (int k = 0; k < 2; k++) { var back = Vector3.forward * 0.012f; b.AddTriangleOutward(pl + back, pTop + back, pTop, pl + Vector3.right * 0.03f + back * 0.5f + Vector3.down * 0.005f, Pal.Shade(stone, 0.95f)); b.AddTriangleOutward(pr, pTop, pTop + back, pr + Vector3.left * 0.03f + back * 0.5f + Vector3.down * 0.005f, Pal.Shade(stone, 0.95f)); }
            b.Push(new Vector3(0f, 0.006f + wallH + 0.0065f, -d / 2 - 0.0035f), Quaternion.FromToRotation(Vector3.up, Vector3.back));
            b.AddCylinder(Vector3.zero, 0.0055f, 0.0055f, 0.001f, 16, Pal.Brass, Pal.Hex("#F7F1E6"), true);
            b.Pop();
            WallDoor(b, ref rng, new Style { Door = red, Trim = s.Trim, Plaster = stone, Curtain = s.Curtain, Flower = s.Flower, Shutter = s.Shutter, Roof = s.Roof, Timber = s.Timber }, new Vector3(0f, 0.006f, -d / 2 + 0.0105f), Vector3.back, 0.013f, 0.028f, true);
            foreach (float x in new[] { -0.034f, 0.034f }) WallWindow(b, ref rng, s, new Vector3(x, 0.03f, -d / 2 + 0.0105f), Vector3.back, 0.009f, 0.02f, false, false, true);
            Lettering.Board(b, "POST OFFICE", new Vector3(0f, 0.006f + wallH - 0.003f, -d / 2 - 0.0032f), Vector3.back, 0.0032f, red, Pal.Hex("#F2C12E"), stone);
            PillarBoxAt(b, new Vector3(0.036f, 0f, -d / 2 - 0.012f));
            return 0.006f + wallH + 0.02f;
        }

        private static void PillarBoxAt(MeshBuilder b, Vector3 at)
        {
            b.AddCylinder(at, 0.0032f, 0.0032f, 0.012f, 10, Pal.Hex("#C8392E"), Pal.Hex("#C8392E"), false);
            b.AddBlob(at + Vector3.up * 0.012f, new Vector3(0.0035f, 0.002f, 0.0035f), 1, 0f, 3, (_, __) => Pal.Hex("#C8392E"));
        }

        /// <summary>The school: a long single-storey schoolhouse of tall windows under a hipped roof, a bell in an open cupola with a clock, its yard fenced, a flagpole.</summary>
        private static float CupolaSchool(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.15f, d = 0.06f, wallH = 0.045f;
            var brick = Pal.Hex("#C27A5A");
            Foundation(b, ref rng, w, d);
            b.AddBox(new Vector3(0f, 0.004f + wallH / 2, 0.02f), new Vector3(w, wallH, d), brick, brick, brick);
            b.AddBox(new Vector3(0f, 0.004f + wallH - 0.002f, 0.02f), new Vector3(w + 0.003f, 0.004f, d + 0.003f), Pal.Hex("#F2EAD8"));
            HipRoof(b, new Vector3(0f, 0.004f + wallH, 0.02f), w, d, 0.035f, Pal.Hex("#4A5A6A"));
            for (int k = 0; k < 8; k++)
            {
                float x = -w / 2 + 0.012f + k * (w - 0.024f) / 7f;
                if (k == 4) WallDoor(b, ref rng, s, new Vector3(x, 0.004f, -d / 2 + 0.02f), Vector3.back, 0.014f, 0.03f, true, true);
                else WallWindow(b, ref rng, s, new Vector3(x, 0.026f, -d / 2 + 0.02f), Vector3.back, 0.011f, 0.024f);
            }
            // The cupola: an open bell turret with a clock, a copper dome and weather vane.
            var cup = new Vector3(0f, 0.004f + wallH + 0.03f, 0.02f);
            b.AddBox(cup + Vector3.up * 0.004f, new Vector3(0.018f, 0.008f, 0.018f), Pal.Hex("#F2EAD8"));
            b.Push(cup + new Vector3(0f, 0.004f, -0.0092f), Quaternion.FromToRotation(Vector3.up, Vector3.back));
            b.AddCylinder(Vector3.zero, 0.0042f, 0.0042f, 0.0008f, 14, Pal.Hex("#2E3238"), Pal.Hex("#F7F1E6"), true);
            b.Pop();
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.AddBox(cup + new Vector3(sx * 0.0075f, 0.014f, sz * 0.0075f), new Vector3(0.0022f, 0.012f, 0.0022f), Pal.Hex("#F2EAD8"));
            b.AddBlob(cup + new Vector3(0f, 0.013f, 0f), new Vector3(0.0035f, 0.0035f, 0.0035f), 1, 0f, 5, (_, __) => Pal.Brass);   // the bell
            b.Push(cup + Vector3.up * 0.02f, Quaternion.identity, new Vector3(1f, 0.8f, 1f));
            b.AddBlob(Vector3.zero, Vector3.one * 0.011f, 1, 0f, 6, (d2, _) => d2.y > 0f ? Pal.Hex("#5EA88A") : Pal.Hex("#F2EAD8"));
            b.Pop();
            WeatherVane(b, cup + Vector3.up * 0.029f);
            Lettering.Board(b, "SCHOOL", new Vector3(0f, 0.004f + wallH + 0.005f, -d / 2 + 0.018f), Vector3.back, 0.0045f, Pal.Hex("#2F5A8A"), Pal.Hex("#F7F1E6"), Pal.Hex("#F2EAD8"));
            // The flagpole by the door; the yard's fence.
            b.AddCylinder(new Vector3(0.05f, 0f, -0.04f), 0.0007f, 0.0006f, 0.075f, 6, Pal.Hex("#E8E2D6"), Pal.Hex("#E8E2D6"), false);
            b.AddBox(new Vector3(0.056f, 0.069f, -0.04f), new Vector3(0.012f, 0.008f, 0.0005f), Pal.Hex("#2F5A8A"));
            return 0.004f + wallH + 0.06f;
        }

        /// <summary>The fire station: a wide engine hall with three tall red doors, a flat roof, and the slim hose-drying tower beside it with its lookout and bell.</summary>
        private static float EngineHall(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.1f, d = 0.07f, wallH = 0.05f;
            var brick = Pal.Hex("#A8463A"); var red = Pal.Hex("#D7382E"); var cream = Pal.Hex("#F2E3C2");
            Foundation(b, ref rng, w + 0.02f, d);
            b.AddBox(new Vector3(-0.01f, 0.004f + wallH / 2, 0f), new Vector3(w, wallH, d), brick, brick, brick);
            FlatRoof(b, new Vector3(-0.01f, 0.004f + wallH, 0f), w, d, brick, cream);
            b.AddBox(new Vector3(-0.01f, 0.004f + wallH - 0.012f, -d / 2 - 0.0005f), new Vector3(w, 0.0025f, 0.001f), cream);
            for (int k = 0; k < 3; k++)
            {
                float x = -0.01f - 0.03f + k * 0.03f;
                b.AddFace(new Vector3(x, 0.004f + 0.017f, -d / 2 - 0.0004f), Vector3.back, Vector3.up, 0.011f, 0.017f, red);
                if (!b.TooSmall(0.002f)) for (int r = 1; r < 5; r++) b.AddBox(new Vector3(x, 0.004f + r * 0.007f, -d / 2 - 0.0008f), new Vector3(0.022f, 0.0006f, 0.0004f), Pal.Shade(red, 0.75f));
                b.AddFace(new Vector3(x, 0.004f + 0.03f, -d / 2 - 0.0006f), Vector3.back, Vector3.up, 0.009f, 0.002f, Pal.Glow(Pal.WindowGlow));
            }
            Lettering.Board(b, "FIRE STATION", new Vector3(-0.01f, 0.004f + wallH - 0.005f, -d / 2 - 0.0012f), Vector3.back, 0.0038f, red, cream, cream);
            // The hose tower.
            var tower = new Vector3(w / 2 + 0.004f, 0f, 0.012f);
            b.AddBox(tower + Vector3.up * 0.05f, new Vector3(0.022f, 0.1f, 0.022f), brick, brick, brick);
            for (int k = 0; k < 3; k++) WallWindow(b, ref rng, s, tower + new Vector3(0f, 0.03f + k * 0.025f, -0.011f), Vector3.back, 0.006f, 0.01f);
            b.AddBox(tower + Vector3.up * 0.102f, new Vector3(0.026f, 0.004f, 0.026f), cream);
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.AddBox(tower + new Vector3(sx * 0.011f, 0.11f, sz * 0.011f), new Vector3(0.002f, 0.012f, 0.002f), cream);
            b.AddBlob(tower + Vector3.up * 0.11f, Vector3.one * 0.003f, 1, 0f, 3, (_, __) => Pal.Brass);
            b.AddCylinder(tower + Vector3.up * 0.116f, 0.017f, 0.001f, 0.016f, 4, Pal.Hex("#3A4250"), Pal.Hex("#4A5260"), true, false, false, Mathf.PI / 4f);
            return 0.135f;
        }

        /// <summary>The police station: a modern three-storey block, flat-roofed, a blue band round it, a glass entrance under a canopy, a radio mast and a blue lamp.</summary>
        private static float PoliceBlock(MeshBuilder b, ref Rng rng)
        {
            const float w = 0.1f, d = 0.07f, h = 0.075f;
            var wall = Pal.Hex("#E4E6EA"); var blue = Pal.Hex("#1F3E7A"); var glass = Pal.Hex("#5A7A9A");
            b.AddBox(new Vector3(0f, 0.002f, 0f), new Vector3(w + 0.006f, 0.004f, d + 0.006f), Pal.Hex("#9A9EA6"));
            b.AddBox(new Vector3(0f, 0.004f + h / 2, 0f), new Vector3(w, h, d), wall, wall, wall);
            FlatRoof(b, new Vector3(0f, 0.004f + h, 0f), w, d, wall, Pal.Hex("#C8CCD2"));
            b.AddBox(new Vector3(0f, 0.004f + 0.026f, 0f), new Vector3(w + 0.001f, 0.004f, d + 0.001f), blue);
            for (int floor = 0; floor < 3; floor++)
                for (int k = 0; k < 6; k++)
                {
                    float x = -w / 2 + 0.01f + k * (w - 0.02f) / 5f, y = 0.014f + floor * 0.024f;
                    if (floor == 0 && (k == 2 || k == 3)) continue;
                    b.AddFace(new Vector3(x, 0.004f + y + 0.002f, -d / 2 - 0.0003f), Vector3.back, Vector3.up, 0.006f, 0.007f, floor == 0 ? glass : Pal.Glow(Pal.WindowGlow));
                    foreach (int sx in new[] { -1, 1 }) b.AddFace(new Vector3(sx * (w / 2 + 0.0003f), 0.004f + y + 0.002f, -d / 2 + 0.01f + k * (d - 0.02f) / 5f), sx > 0 ? Vector3.right : Vector3.left, Vector3.up, 0.005f, 0.007f, Pal.Glow(Pal.WindowGlow));
                }
            // The entrance: glass doors under a canopy, the blue lamp over it.
            b.AddFace(new Vector3(0f, 0.004f + 0.011f, -d / 2 - 0.0003f), Vector3.back, Vector3.up, 0.012f, 0.011f, glass);
            b.AddBox(new Vector3(0f, 0.004f + 0.023f, -d / 2 - 0.007f), new Vector3(0.034f, 0.002f, 0.014f), wall);
            b.AddBlob(new Vector3(0f, 0.004f + 0.029f, -d / 2 - 0.004f), new Vector3(0.0022f, 0.003f, 0.0022f), 1, 0f, 3, (_, __) => Pal.Glow(Pal.Hex("#5A8AE8")));
            Lettering.Board(b, "POLICE", new Vector3(0f, 0.004f + h - 0.008f, -d / 2 - 0.0008f), Vector3.back, 0.006f, blue, Pal.Hex("#F7F1E6"), Pal.Hex("#C8CCD2"));
            // The radio mast.
            b.AddCylinder(new Vector3(0.035f, 0.004f + h, 0.02f), 0.001f, 0.0005f, 0.06f, 6, Pal.Hex("#8A8F96"), Pal.Hex("#8A8F96"), false);
            if (!b.TooSmall(0.002f)) for (int k = 1; k < 4; k++) b.AddBox(new Vector3(0.035f, 0.004f + h + k * 0.014f, 0.02f), new Vector3(0.008f, 0.0006f, 0.0006f), Pal.Hex("#8A8F96"));
            return 0.004f + h + 0.06f;
        }

        /// <summary>The hospital: a tall white main block with a red cross, two lower wings stepped round a forecourt, the entrance canopy where the ambulance draws up, and a helipad on the roof.</summary>
        private static float HospitalWings(MeshBuilder b, ref Rng rng)
        {
            var wall = Pal.Hex("#F2F2EE"); var band = Pal.Hex("#8AB4C8"); var red = Pal.Hex("#D7382E");
            b.AddBox(new Vector3(0f, 0.002f, 0f), new Vector3(0.17f, 0.004f, 0.12f), Pal.Hex("#A6AAB0"));
            void Block(Vector3 c, Vector3 size, int floors)
            {
                b.AddBox(c + Vector3.up * (0.004f + size.y / 2), size, wall, wall, wall);
                FlatRoof(b, c + Vector3.up * (0.004f + size.y), size.x, size.z, wall, Pal.Hex("#D8DCE0"));
                for (int f = 0; f < floors; f++)
                {
                    float y = 0.004f + 0.012f + f * (size.y - 0.012f) / floors;
                    b.AddBox(c + new Vector3(0f, y - 0.004f, 0f), new Vector3(size.x + 0.001f, 0.002f, size.z + 0.001f), band);
                    int n = Mathf.Max(2, Mathf.RoundToInt(size.x / 0.013f));
                    for (int k = 0; k < n; k++) b.AddFace(c + new Vector3(-size.x / 2 + (k + 0.5f) * size.x / n, y + 0.003f, -size.z / 2 - 0.0003f), Vector3.back, Vector3.up, 0.004f, 0.0055f, Pal.Glow(Pal.WindowGlow));
                }
            }
            Block(new Vector3(0f, 0f, 0.025f), new Vector3(0.08f, 0.1f, 0.05f), 5);
            Block(new Vector3(-0.064f, 0f, 0.0f), new Vector3(0.048f, 0.05f, 0.09f), 2);
            Block(new Vector3(0.064f, 0f, 0.0f), new Vector3(0.048f, 0.05f, 0.09f), 2);
            // The red cross high on the main block, and HOSPITAL under it.
            b.AddBox(new Vector3(0f, 0.09f, -0.0002f), new Vector3(0.012f, 0.004f, 0.001f), red);
            b.AddBox(new Vector3(0f, 0.09f, -0.0002f), new Vector3(0.004f, 0.012f, 0.001f), red);
            Lettering.Board(b, "HOSPITAL", new Vector3(0f, 0.076f, -0.0006f), Vector3.back, 0.0048f, wall, red, Pal.Hex("#D8DCE0"));
            // The entrance canopy and glass doors.
            b.AddBox(new Vector3(0f, 0.022f, -0.012f), new Vector3(0.04f, 0.002f, 0.024f), wall);
            foreach (int sx in new[] { -1, 1 }) b.AddCylinder(new Vector3(sx * 0.018f, 0.004f, -0.022f), 0.0012f, 0.0012f, 0.018f, 6, wall, wall, false);
            b.AddFace(new Vector3(0f, 0.014f, -0.0003f), Vector3.back, Vector3.up, 0.01f, 0.01f, Pal.Hex("#5A7A9A"));
            // The helipad on the main block's roof: a circle with an H.
            var pad = new Vector3(0f, 0.004f + 0.1f + 0.0015f, 0.025f);
            b.AddCylinder(pad, 0.019f, 0.019f, 0.0008f, 20, Pal.Hex("#4A5058"), Pal.Hex("#4A5058"), true);
            b.AddCylinder(pad + Vector3.up * 0.0008f, 0.016f, 0.016f, 0.0003f, 20, Pal.Hex("#F2C12E"), Pal.Hex("#F2C12E"), true);
            b.AddCylinder(pad + Vector3.up * 0.0009f, 0.0145f, 0.0145f, 0.0003f, 20, Pal.Hex("#4A5058"), Pal.Hex("#4A5058"), true);
            foreach (int sx in new[] { -1, 1 }) b.AddBox(pad + new Vector3(sx * 0.005f, 0.0013f, 0f), new Vector3(0.002f, 0.0003f, 0.014f), Pal.Hex("#F7F1E6"));
            b.AddBox(pad + new Vector3(0f, 0.0013f, 0f), new Vector3(0.01f, 0.0003f, 0.002f), Pal.Hex("#F7F1E6"));
            return 0.11f;
        }

        /// <summary>The hotel: a grand four-storey front under a slate mansard, round corner turrets with domes, balconies, a canopy over the door and flags.</summary>
        private static float GrandHotel(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.13f, d = 0.07f, wallH = 0.09f;
            var stone = Pal.Hex("#F2E6CC"); var trim = Pal.Hex("#FBF5EA"); var roof = Pal.Hex("#3E4A5E");
            Foundation(b, ref rng, w, d);
            b.AddBox(new Vector3(0f, 0.004f + wallH / 2, 0.006f), new Vector3(w, wallH, d), stone, stone, stone);
            for (int f = 1; f < 4; f++) b.AddBox(new Vector3(0f, 0.004f + f * 0.022f, 0.006f), new Vector3(w + 0.002f, 0.0016f, d + 0.002f), trim);
            MansardRoof(b, ref rng, s, new Vector3(0f, 0.004f + wallH, 0.006f), w, d, 0.035f, roof, 5);
            for (int f = 0; f < 4; f++)
                for (int k = 0; k < 7; k++)
                {
                    float x = -w / 2 + 0.012f + k * (w - 0.024f) / 6f, y = 0.004f + 0.011f + f * 0.022f;
                    if (f == 0 && k == 3) continue;
                    WallWindow(b, ref rng, s, new Vector3(x, y, -d / 2 + 0.006f), Vector3.back, 0.009f, 0.013f, f > 0, f == 1, f == 0);
                    if (f > 0 && f < 3 && !b.TooSmall(0.002f)) b.AddBox(new Vector3(x, y - 0.0085f, -d / 2 + 0.003f), new Vector3(0.013f, 0.0012f, 0.006f), Pal.Hex("#2E3238"));
                }
            // Corner turrets with copper domes.
            foreach (int sx in new[] { -1, 1 })
            {
                var t = new Vector3(sx * (w / 2), 0f, -d / 2 + 0.008f);
                b.AddCylinder(t, 0.012f, 0.012f, wallH + 0.012f, 16, stone, stone, false);
                b.AddCylinder(t + Vector3.up * (wallH + 0.012f), 0.0135f, 0.0135f, 0.003f, 16, trim, trim, true);
                b.Push(t + Vector3.up * (wallH + 0.015f), Quaternion.identity, new Vector3(1f, 1.3f, 1f));
                b.AddBlob(Vector3.zero, Vector3.one * 0.012f, 1, 0f, 4, (dd, _) => dd.y > 0f ? Pal.Hex("#5EA88A") : stone);
                b.Pop();
                b.AddCylinder(t + Vector3.up * (wallH + 0.03f), 0.0006f, 0.0006f, 0.01f, 6, Pal.Brass, Pal.Brass, false);
                for (int f = 0; f < 4; f++) WallWindow(b, ref rng, s, t + new Vector3(sx * 0.0045f, 0.015f + f * 0.022f, -0.011f), Vector3.back, 0.006f, 0.011f);
            }
            // The canopy over the door, its name in gold, and flags.
            WallDoor(b, ref rng, s, new Vector3(0f, 0.004f, -d / 2 + 0.006f), Vector3.back, 0.014f, 0.026f, true);
            b.AddBox(new Vector3(0f, 0.033f, -d / 2 - 0.006f), new Vector3(0.03f, 0.002f, 0.024f), Pal.Hex("#5A1E2A"));
            foreach (int sx in new[] { -1, 1 }) b.AddCylinder(new Vector3(sx * 0.014f, 0.004f, -d / 2 - 0.016f), 0.0008f, 0.0008f, 0.029f, 6, Pal.Brass, Pal.Brass, false);
            Lettering.Board(b, "GRAND HOTEL", new Vector3(0f, 0.004f + wallH - 0.004f, -d / 2 + 0.0055f), Vector3.back, 0.0048f, Pal.Hex("#5A1E2A"), Pal.Hex("#F2D27A"), trim);
            foreach (int sx in new[] { -1, 1 })
            {
                b.Push(new Vector3(sx * 0.032f, 0.06f, -d / 2 + 0.006f), Quaternion.Euler(-35f, 0f, 0f));
                b.AddCylinder(Vector3.zero, 0.0005f, 0.0005f, 0.026f, 5, Pal.Hex("#E8E2D6"), Pal.Hex("#E8E2D6"), false);
                b.AddBox(new Vector3(0f, 0.02f, -0.005f), new Vector3(0.0005f, 0.008f, 0.01f), sx < 0 ? Pal.Hex("#D7382E") : Pal.Hex("#2E4E7A"));
                b.Pop();
            }
            return 0.004f + wallH + 0.05f;
        }

        // ---------------------------------------------------------------- the rest of the town

        /// <summary>
        /// The railway station: a two-storey booking hall under a hipped roof with its clock tower and lantern, a
        /// single-storey wing either side, and along the platform (the front, -Z) an iron-and-glass canopy on cast columns
        /// with brackets, a clock hanging from it, the nameboard, benches and the way out through tall arched doors.
        /// </summary>
        private static float RailwayStation(MeshBuilder b, ref Rng rng, Style s)
        {
            const float back = 0.01f;   // (the building stands back from the platform's edge, under the canopy)
            var brick = Pal.Hex("#B4644A"); var stone = Pal.Hex("#EDE3CC"); var iron = Pal.Hex("#2E4A3E"); var roof = Pal.Hex("#4A4E5A");
            b.AddBox(new Vector3(0f, 0.002f, back), new Vector3(0.12f, 0.004f, 0.052f), Pal.Hex("#B9B2A4"));
            // The hall and its wings.
            b.AddBox(new Vector3(0f, 0.004f + 0.03f, back + 0.006f), new Vector3(0.05f, 0.06f, 0.04f), brick, brick, brick);
            HipRoof(b, new Vector3(0f, 0.064f, back + 0.006f), 0.05f, 0.04f, 0.022f, roof);
            foreach (int sx in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(sx * 0.042f, 0.004f + 0.018f, back + 0.008f), new Vector3(0.034f, 0.036f, 0.034f), brick, brick, brick);
                HipRoof(b, new Vector3(sx * 0.042f, 0.04f, back + 0.008f), 0.034f, 0.034f, 0.014f, roof);
                for (int k = 0; k < 2; k++) WallWindow(b, ref rng, s, new Vector3(sx * 0.042f + (k - 0.5f) * 0.016f, 0.022f, back - 0.0092f), Vector3.back, 0.009f, 0.016f, false, false, true);
            }
            b.AddBox(new Vector3(0f, 0.04f, back - 0.014f), new Vector3(0.052f, 0.002f, 0.001f), stone);   // a string course
            foreach (int sx in new[] { -1, 0, 1 })
                if (sx == 0) WallDoor(b, ref rng, s, new Vector3(0f, 0.004f, back - 0.014f), Vector3.back, 0.014f, 0.03f, true);
                else WallWindow(b, ref rng, s, new Vector3(sx * 0.016f, 0.024f, back - 0.014f), Vector3.back, 0.01f, 0.02f, false, false, true);
            for (int k = 0; k < 3; k++) WallWindow(b, ref rng, s, new Vector3((k - 1) * 0.016f, 0.052f, back - 0.014f), Vector3.back, 0.008f, 0.012f);
            // The clock tower with its lantern.
            var tower = new Vector3(0f, 0.07f, back + 0.006f);
            b.AddBox(tower + Vector3.up * 0.015f, new Vector3(0.018f, 0.03f, 0.018f), brick, brick, brick);
            b.Push(tower + new Vector3(0f, 0.02f, -0.0092f), Quaternion.FromToRotation(Vector3.up, Vector3.back));
            b.AddCylinder(Vector3.zero, 0.0065f, 0.0065f, 0.001f, 16, Pal.Brass, Pal.Glow(Pal.Hex("#FFF6E0")), true);
            b.Pop();
            b.AddBox(tower + Vector3.up * 0.031f, new Vector3(0.021f, 0.002f, 0.021f), stone);
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.AddBox(tower + new Vector3(sx * 0.007f, 0.037f, sz * 0.007f), new Vector3(0.0018f, 0.01f, 0.0018f), stone);
            b.AddCylinder(tower + Vector3.up * 0.042f, 0.012f, 0.0008f, 0.018f, 4, roof, Pal.Shade(roof, 1.2f), true, false, false, Mathf.PI / 4f);
            WeatherVane(b, tower + Vector3.up * 0.06f);
            // The platform canopy: glass on iron, columns with brackets, a valance of saw-tooth boards.
            const float cz = -0.026f, cw = 0.12f;
            b.Push(new Vector3(0f, 0.034f, cz + 0.006f), Quaternion.Euler(-8f, 0f, 0f));
            b.AddBox(Vector3.zero, new Vector3(cw, 0.0012f, 0.028f), new Color(0.78f, 0.88f, 0.9f, 1f));
            if (!b.TooSmall(0.002f)) for (int k = 0; k <= 12; k++) b.AddBox(new Vector3(-cw / 2 + k * cw / 12f, 0.0008f, 0f), new Vector3(0.0009f, 0.0009f, 0.028f), iron);
            b.Pop();
            for (int k = 0; k < 5; k++)
            {
                float x = -cw / 2 + 0.008f + k * (cw - 0.016f) / 4f;
                b.AddCylinder(new Vector3(x, 0.004f, cz - 0.004f), 0.0011f, 0.0009f, 0.029f, 8, iron, iron, false);
                if (!b.TooSmall(0.002f)) { b.Push(new Vector3(x, 0.03f, cz - 0.002f), Quaternion.Euler(45f, 0f, 0f)); b.AddBox(Vector3.zero, new Vector3(0.0008f, 0.008f, 0.0008f), iron); b.Pop(); }
            }
            if (!b.TooSmall(0.002f)) for (int k = 0; k < 24; k++) b.AddTriangleOutward(new Vector3(-cw / 2 + k * cw / 24f, 0.032f, cz - 0.0075f), new Vector3(-cw / 2 + (k + 1) * cw / 24f, 0.032f, cz - 0.0075f), new Vector3(-cw / 2 + (k + 0.5f) * cw / 24f, 0.028f, cz - 0.0075f), new Vector3(0f, 0.03f, 0f), Pal.Hex("#F2E3C2"));
            // The hanging clock, the nameboard, benches.
            b.AddBox(new Vector3(0.02f, 0.03f, cz - 0.002f), new Vector3(0.0006f, 0.004f, 0.0006f), iron);
            b.AddCylinder(new Vector3(0.02f, 0.027f, cz - 0.0045f), 0.0035f, 0.0035f, 0.005f, 14, Pal.Hex("#F7F1E6"), Pal.Hex("#F7F1E6"), true, true, true);
            Lettering.Board(b, "STATION", new Vector3(-0.02f, 0.024f, cz - 0.004f), Vector3.back, 0.0034f, Pal.Hex("#1F3E7A"), Pal.Hex("#F7F1E6"), iron);
            foreach (float x in new[] { -0.042f, 0.042f }) { b.AddBox(new Vector3(x, 0.008f, cz + 0.004f), new Vector3(0.016f, 0.0012f, 0.005f), Pal.Hex("#8A5A34")); b.AddBox(new Vector3(x, 0.011f, cz + 0.0065f), new Vector3(0.016f, 0.004f, 0.0009f), Pal.Hex("#8A5A34")); }
            return 0.11f;
        }

        /// <summary>
        /// Apartments: a modern four-storey block, flat-roofed, its front a grid of windows and balconies with railings
        /// and plants, a glass stair tower to one side, an entrance canopy, and a water tank and lift housing on the roof.
        /// </summary>
        private static float ApartmentBlock(MeshBuilder b, ref Rng rng, Style s, int style)
        {
            const float w = 0.1f, d = 0.07f, h = 0.1f;
            var walls = new[] { Pal.Hex("#E8E2D6"), Pal.Hex("#D9C8B4"), Pal.Hex("#C8D4D8"), Pal.Hex("#E2D2C2") };
            var wall = walls[style % 4]; var accent = new[] { Pal.Hex("#C8653A"), Pal.Hex("#3E6A8A"), Pal.Hex("#5A8A4A"), Pal.Hex("#8A4A5A") }[style % 4];
            b.AddBox(new Vector3(0f, 0.002f, 0f), new Vector3(w + 0.024f, 0.004f, d + 0.006f), Pal.Hex("#A6AAB0"));
            b.AddBox(new Vector3(-0.008f, 0.004f + h / 2, 0f), new Vector3(w - 0.016f, h, d), wall, wall, wall);
            FlatRoof(b, new Vector3(-0.008f, 0.004f + h, 0f), w - 0.016f, d, wall, Pal.Hex("#C8CCD2"));
            for (int f = 0; f < 4; f++)
            {
                float y = 0.004f + 0.006f + f * 0.024f;
                b.AddBox(new Vector3(-0.008f, y - 0.004f, 0f), new Vector3(w - 0.014f, 0.0014f, d + 0.002f), accent);
                for (int k = 0; k < 3; k++)
                {
                    float x = -0.008f - 0.03f + k * 0.03f;
                    if (f == 0 && k == 1) { b.AddFace(new Vector3(x, y + 0.008f, -d / 2 - 0.0003f), Vector3.back, Vector3.up, 0.007f, 0.01f, Pal.Hex("#5A7A9A")); continue; }
                    WallWindow(b, ref rng, s, new Vector3(x, y + 0.01f, -d / 2), Vector3.back, 0.014f, 0.012f);
                    if (f == 0) continue;
                    // A balcony: a slab, a railing and a pot plant.
                    b.AddBox(new Vector3(x, y + 0.002f, -d / 2 - 0.006f), new Vector3(0.022f, 0.0014f, 0.012f), wall);
                    b.AddBox(new Vector3(x, y + 0.006f, -d / 2 - 0.0118f), new Vector3(0.022f, 0.006f, 0.0008f), new Color(0.78f, 0.9f, 0.95f, 1f));
                    if (!b.TooSmall(0.003f)) b.AddBlob(new Vector3(x + 0.008f, y + 0.005f, -d / 2 - 0.006f), Vector3.one * 0.0022f, 1, 0.2f, f * 3 + k, (_, __) => Pal.Hex("#5E9A4A"));
                }
            }
            foreach (int sx in new[] { -1, 1 }) for (int f = 0; f < 4; f++) for (int k = 0; k < 2; k++) b.AddFace(new Vector3(-0.008f + sx * (w - 0.016f) / 2f + sx * 0.0003f, 0.004f + 0.016f + f * 0.024f, (k - 0.5f) * 0.03f), sx > 0 ? Vector3.right : Vector3.left, Vector3.up, 0.006f, 0.006f, Pal.Glow(Pal.WindowGlow));
            // The stair tower: glass on the front, a little taller.
            var st = new Vector3(w / 2 - 0.004f, 0f, -0.012f);
            b.AddBox(st + Vector3.up * (0.004f + (h + 0.012f) / 2), new Vector3(0.018f, h + 0.012f, 0.026f), wall, wall, wall);
            b.AddFace(st + new Vector3(0f, 0.004f + (h + 0.012f) / 2, -0.0133f), Vector3.back, Vector3.up, 0.006f, (h + 0.004f) / 2, new Color(0.62f, 0.78f, 0.88f, 1f));
            // The canopy over the entrance, the roof's tank and lift housing.
            b.AddBox(new Vector3(-0.008f, 0.026f, -d / 2 - 0.007f), new Vector3(0.024f, 0.0016f, 0.014f), accent);
            b.AddCylinder(new Vector3(-0.03f, 0.004f + h, 0.015f), 0.008f, 0.008f, 0.014f, 12, Pal.Hex("#9AA0A8"), Pal.Hex("#B8BEC4"), true);
            b.AddBox(new Vector3(0.015f, 0.004f + h + 0.008f, 0.01f), new Vector3(0.018f, 0.016f, 0.016f), wall);
            return 0.004f + h + 0.016f;
        }

        /// <summary>
        /// The farmhouse: a long, low house under a deep half-hipped roof sweeping down almost to the ground floor, a
        /// timber barn wing attached at the side with its big doors, a porch with a bench along the front, a pump, a
        /// vegetable patch and a woodpile.
        /// </summary>
        private static float FarmhouseLong(MeshBuilder b, ref Rng rng, Style s)
        {
            const float w = 0.1f, d = 0.06f, wallH = 0.032f;
            var white = Pal.Hex("#F2ECE0"); var timber = Pal.Hex("#7A4E2C");
            Foundation(b, ref rng, w + 0.05f, d);
            b.AddBox(new Vector3(-0.02f, 0.004f + wallH / 2, 0f), new Vector3(w, wallH, d), white, white, white);
            HipRoof(b, new Vector3(-0.02f, 0.004f + wallH, 0f), w, d, 0.045f, Pal.Hex("#8E4A34"), 0.008f);
            // The half-hips: gable triangles in the roof's ends.
            foreach (int sx in new[] { -1, 1 })
            {
                var c = new Vector3(-0.02f + sx * (w / 2 - 0.006f), 0.004f + wallH + 0.02f, 0f);
                b.AddTriangleOutward(c + new Vector3(0f, -0.012f, -0.014f), c + new Vector3(0f, -0.012f, 0.014f), c + new Vector3(0f, 0.012f, 0f), c - new Vector3(sx * 0.02f, 0f, 0f), white);
            }
            // The barn wing.
            var barn = new Vector3(0.055f, 0f, 0.004f);
            b.AddBox(barn + Vector3.up * (0.004f + 0.022f), new Vector3(0.04f, 0.044f, 0.052f), timber, timber, timber);
            if (!b.TooSmall(0.002f)) for (int k = 0; k < 8; k++) b.AddBox(barn + new Vector3(-0.0175f + k * 0.005f, 0.026f, -0.0262f), new Vector3(0.0008f, 0.044f, 0.0006f), Pal.Shade(timber, 0.75f));
            GableRoofColoured(b, barn + Vector3.up * 0.048f, 0.046f, 0.058f, 0.024f, Pal.Hex("#6A6A66"), timber, alongZ: true);
            b.AddFace(barn + new Vector3(0f, 0.02f, -0.0262f), Vector3.back, Vector3.up, 0.012f, 0.016f, Pal.Hex("#B8483E"));
            if (!b.TooSmall(0.002f)) foreach (int sx in new[] { -1, 1 }) { b.Push(barn + new Vector3(sx * 0.006f, 0.02f, -0.0266f), Quaternion.Euler(0f, 0f, sx * 53f)); b.AddBox(Vector3.zero, new Vector3(0.0012f, 0.03f, 0.0006f), white); b.Pop(); }
            // Windows with shutters, the door, the porch.
            for (int k = 0; k < 4; k++)
            {
                float x = -0.06f + k * 0.027f;
                if (k == 2) WallDoor(b, ref rng, s, new Vector3(x, 0.004f, -d / 2), Vector3.back, 0.011f, 0.025f);
                else WallWindow(b, ref rng, s, new Vector3(x, 0.02f, -d / 2), Vector3.back, 0.01f, 0.012f, true, true);
            }
            b.AddBox(new Vector3(-0.02f, 0.005f, -d / 2 - 0.008f), new Vector3(0.09f, 0.002f, 0.016f), Pal.Hex("#A8743E"));
            for (int k = 0; k < 5; k++) b.AddBox(new Vector3(-0.062f + k * 0.021f, 0.018f, -d / 2 - 0.015f), new Vector3(0.0016f, 0.026f, 0.0016f), timber);
            b.Push(new Vector3(-0.02f, 0.031f, -d / 2 - 0.009f), Quaternion.Euler(-20f, 0f, 0f));
            b.AddBox(Vector3.zero, new Vector3(0.094f, 0.0014f, 0.02f), Pal.Hex("#8E4A34"));
            b.Pop();
            b.AddBox(new Vector3(-0.045f, 0.009f, -d / 2 - 0.004f), new Vector3(0.016f, 0.0012f, 0.005f), timber);
            // The pump, the woodpile and the vegetable patch.
            b.AddCylinder(new Vector3(-0.072f, 0f, -0.045f), 0.0015f, 0.0015f, 0.014f, 6, Pal.Hex("#3A5A4A"), Pal.Hex("#3A5A4A"), true);
            for (int k = 0; k < 6; k++) b.AddCylinder(new Vector3(0.08f, 0.0025f + (k / 3) * 0.004f, -0.015f + (k % 3) * 0.004f), 0.0018f, 0.0018f, 0.012f, 6, Pal.Hex("#A8743E"), Pal.Hex("#C8945E"), true, true, true);
            return 0.004f + wallH + 0.045f;
        }

        /// <summary>
        /// The theatre: a temple front of six columns under a great pediment, a marquee over the doors framed in light
        /// bulbs with THEATRE on it, posters either side, and behind it all the tall plain fly tower that every theatre has.
        /// </summary>
        private static float TheatreHouse(MeshBuilder b, ref Rng rng, Style s)
        {
            var stone = Pal.Hex("#EDE0C8"); var red = Pal.Hex("#8E2F3A"); var gold = Pal.Hex("#E0B040");
            b.AddBox(new Vector3(0f, 0.003f, 0f), new Vector3(0.124f, 0.006f, 0.1f), Pal.Shade(stone, 0.9f));
            for (int k = 0; k < 3; k++) b.AddBox(new Vector3(0f, 0.001f + k * 0.002f, -0.054f - k * 0.003f), new Vector3(0.07f, 0.002f, 0.004f), Pal.Shade(stone, 0.95f));
            // The auditorium and, behind it, the fly tower.
            b.AddBox(new Vector3(0f, 0.006f + 0.035f, 0.005f), new Vector3(0.11f, 0.07f, 0.07f), stone, stone, stone);
            HipRoof(b, new Vector3(0f, 0.076f, 0.005f), 0.11f, 0.07f, 0.018f, Pal.Hex("#5EA88A"));
            b.AddBox(new Vector3(0f, 0.006f + 0.06f, 0.034f), new Vector3(0.07f, 0.12f, 0.03f), Pal.Shade(stone, 0.92f), Pal.Shade(stone, 0.96f), stone);
            // The portico.
            for (int k = 0; k < 6; k++) b.AddCylinder(new Vector3(-0.04f + k * 0.016f, 0.006f, -0.042f), 0.003f, 0.0026f, 0.052f, 12, stone, stone, true);
            b.AddBox(new Vector3(0f, 0.061f, -0.038f), new Vector3(0.094f, 0.007f, 0.014f), stone);
            var top = new Vector3(0f, 0.088f, -0.045f); var l = new Vector3(-0.048f, 0.065f, -0.045f); var r = new Vector3(0.048f, 0.065f, -0.045f);
            b.AddTriangleOutward(l, r, top, top + Vector3.forward * 0.02f + Vector3.down * 0.01f, stone);
            b.AddTriangleOutward(l, top, top + Vector3.forward * 0.012f, new Vector3(0.03f, 0.07f, -0.04f), Pal.Shade(stone, 0.92f));
            b.AddTriangleOutward(r, top + Vector3.forward * 0.012f, top, new Vector3(-0.03f, 0.07f, -0.04f), Pal.Shade(stone, 0.92f));
            b.AddBlob(new Vector3(0f, 0.073f, -0.0455f), new Vector3(0.006f, 0.004f, 0.0008f), 1, 0f, 4, (_, __) => gold);   // the masks
            // Doors, the marquee in bulbs, posters.
            for (int k = -1; k <= 1; k++) WallDoor(b, ref rng, new Style { Door = red, Trim = s.Trim, Plaster = stone, Curtain = s.Curtain, Flower = s.Flower, Shutter = s.Shutter, Roof = s.Roof, Timber = s.Timber }, new Vector3(k * 0.02f, 0.006f, -0.0302f), Vector3.back, 0.012f, 0.028f, true);
            b.AddBox(new Vector3(0f, 0.038f, -0.05f), new Vector3(0.07f, 0.012f, 0.012f), red, red, red);
            Lettering.Add(b, "THEATRE", new Vector3(0f, 0.038f, -0.0562f), Vector3.back, 0.0055f, 0.0005f, gold);
            if (!b.TooSmall(0.002f))
                for (int k = 0; k < 18; k++) { float x = -0.034f + k * 0.004f; foreach (float y in new[] { 0.0322f, 0.0438f }) b.AddBlob(new Vector3(x, y, -0.0563f), Vector3.one * 0.0007f, 0, 0f, k, (_, __) => Pal.Glow(Pal.Hex("#FFE9B0"))); }
            foreach (int sx in new[] { -1, 1 }) b.AddFace(new Vector3(sx * 0.05f, 0.025f, -0.0302f), Vector3.back, Vector3.up, 0.006f, 0.009f, sx < 0 ? Pal.Hex("#2E4E7A") : Pal.Hex("#C8392E"));
            return 0.126f;
        }
    }
}