using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The landmarks: places built to be admired. Their silhouettes carry them from across the room; up close every
    /// window has its frame, every tower its corbels and banding. Built from the same primitives as the village, so the
    /// level-of-detail system thins them out with distance like everything else.
    /// </summary>
    public static partial class HouseFactory
    {
        private static readonly Color CastleWall = Pal.Hex("#F4EFE5"), CastleTrim = Pal.Hex("#D8CFBE"), CastleRoof = Pal.Hex("#3D5FAE"),
            CastleRoofLight = Pal.Hex("#5B7CC9"), CastleGold = Pal.Hex("#E0B040"), CastleGlass = Pal.Hex("#2C4673"), CastleRock = Pal.Hex("#8C8274");

        /// <summary>
        /// The fairy-tale castle on its crag: a rocky plateau rising out of the ground (firs on its slopes, a stair winding
        /// up to the gate), crenellated curtain walls with a round tower at each corner, a gatehouse between two needle
        /// turrets with a drawbridge and portcullis, a courtyard with a fountain and gardens, the palace along the back
        /// - three storeys of arched windows, a balcony, stepped gables and a steep blue roof with dormers - a chapel with
        /// its apse, a stair tower, corner turrets cantilevered from the palace, and over everything the great keep: a
        /// square tower turning octagonal, its belfry ringed with corbels and topped by the tallest spire, gold finial and
        /// flag. Blue roofs banded in gold, white walls with stone quoins and string courses.
        /// </summary>
        private static float FairyCastleGrand(MeshBuilder b, ref Rng rng)
        {
            const float Plateau = 0.05f;   // the crag's top
            Crag(b, ref rng, Plateau);
            // The stair up the front of the crag to the gate, winding a little.
            for (int k = 0; k < 10; k++)
            {
                float t = k / 9f;
                var c = new Vector3(Mathf.Sin(t * 2.4f) * 0.012f, Plateau * t - 0.001f, -0.17f + t * 0.062f);
                b.AddBox(c + Vector3.up * 0.0015f, new Vector3(0.026f, 0.003f + 0.0005f * k, 0.007f), Pal.Shade(CastleTrim, 0.9f + 0.05f * (k % 2)), CastleTrim, CastleTrim);
            }
            var court = new Vector3(0f, Plateau, 0f);
            // The courtyard: paving, a fountain, flower beds and two little trees.
            b.AddBox(court + new Vector3(0f, 0.0006f, -0.005f), new Vector3(0.22f, 0.0012f, 0.18f), Pal.Hex("#CFC6B4"));
            b.AddCylinder(court + new Vector3(0f, 0.0012f, -0.035f), 0.012f, 0.012f, 0.004f, 18, CastleTrim, Pal.Hex("#7FB6D8"), true);
            b.AddCylinder(court + new Vector3(0f, 0.005f, -0.035f), 0.002f, 0.0015f, 0.008f, 8, CastleTrim, CastleTrim, true);
            b.AddCylinder(court + new Vector3(0f, 0.012f, -0.035f), 0.005f, 0.004f, 0.0015f, 12, CastleTrim, Pal.Hex("#9CC8E4"), true);
            foreach (int sx in new[] { -1, 1 })
            {
                b.AddBox(court + new Vector3(sx * 0.05f, 0.0018f, -0.04f), new Vector3(0.03f, 0.0016f, 0.016f), Pal.Hex("#6E9E4E"));
                for (int f = 0; f < 6; f++) b.AddBlob(court + new Vector3(sx * 0.05f + (f - 2.5f) * 0.0045f, 0.003f, -0.04f + (f % 2 - 0.5f) * 0.006f), Vector3.one * 0.0018f, 0, 0f, f, (_, __) => f % 2 == 0 ? Pal.FlowerPink : Pal.FlowerYellow);
                b.AddCylinder(court + new Vector3(sx * 0.075f, 0.001f, -0.06f), 0.0012f, 0.001f, 0.01f, 6, Pal.Bark, Pal.Bark, false);
                b.AddBlob(court + new Vector3(sx * 0.075f, 0.014f, -0.06f), new Vector3(0.007f, 0.008f, 0.007f), 1, 0.2f, 60 + sx, (_, __) => Pal.Hex("#4E8A3E"));
            }
            // The curtain walls, a round tower at each corner.
            const float hx = 0.118f, zf = -0.098f, zb = 0.098f, wallH = 0.038f;
            CurtainWall(b, court + new Vector3(-hx, 0f, zf), court + new Vector3(-0.03f, 0f, zf), wallH);
            CurtainWall(b, court + new Vector3(0.03f, 0f, zf), court + new Vector3(hx, 0f, zf), wallH);
            CurtainWall(b, court + new Vector3(-hx, 0f, zf), court + new Vector3(-hx, 0f, zb), wallH);
            CurtainWall(b, court + new Vector3(hx, 0f, zf), court + new Vector3(hx, 0f, zb), wallH);
            CurtainWall(b, court + new Vector3(-hx, 0f, zb), court + new Vector3(hx, 0f, zb), wallH);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    RoundTower(b, court + new Vector3(sx * hx, 0f, sz == -1 ? zf : zb), 0.019f, 0.078f + (sx + sz == 0 ? 0.012f : 0f), 0.062f, CastleRoof, sx * 3 + sz);
            // The gatehouse between two needle turrets: an arched gate, the portcullis, a coat of arms, the drawbridge.
            var gate = court + new Vector3(0f, 0f, zf);
            b.AddBox(gate + new Vector3(0f, 0.034f, 0.004f), new Vector3(0.06f, 0.068f, 0.03f), CastleWall, CastleWall, CastleWall);
            Crenels(b, gate + new Vector3(-0.03f, 0.068f, -0.011f), gate + new Vector3(0.03f, 0.068f, -0.011f), 0.006f);
            ArchedOpening(b, gate + new Vector3(0f, 0f, -0.0112f), Vector3.back, 0.0115f, 0.026f, Pal.Hex("#2B2118"));
            if (!b.TooSmall(0.003f))
                for (int k = -3; k <= 3; k++) b.AddBox(gate + new Vector3(k * 0.003f, 0.02f, -0.0115f), new Vector3(0.0006f, 0.016f, 0.0006f), Pal.Hex("#3A3530"));
            b.AddBox(gate + new Vector3(0f, 0.047f, -0.0112f), new Vector3(0.012f, 0.014f, 0.001f), Pal.Hex("#8A2F3E"));                  // a banner
            b.AddBlob(gate + new Vector3(0f, 0.048f, -0.0122f), new Vector3(0.003f, 0.0035f, 0.0006f), 0, 0f, 3, (_, __) => CastleGold);   // the crest
            b.AddBox(gate + new Vector3(0f, -0.001f, -0.03f), new Vector3(0.022f, 0.002f, 0.04f), Pal.Hex("#7A5232"));                    // the drawbridge
            if (!b.TooSmall(0.003f))
                for (int k = 0; k < 8; k++) b.AddBox(gate + new Vector3(0f, 0.0002f, -0.048f + k * 0.0048f), new Vector3(0.022f, 0.0012f, 0.0006f), Pal.Hex("#5A3A22"));
            foreach (int sx in new[] { -1, 1 }) b.AddBox(gate + new Vector3(sx * 0.0115f, 0.04f, -0.03f), new Vector3(0.0006f, 0.0006f, 0.044f), Pal.Hex("#3A3530"));   // its chains
            foreach (int sx in new[] { -1, 1 }) RoundTower(b, gate + new Vector3(sx * 0.034f, 0f, -0.006f), 0.0125f, 0.095f, 0.07f, CastleRoofLight, 20 + sx);
            Windows(b, gate + new Vector3(0f, 0.058f, -0.0112f), Vector3.back, Vector3.right, 3, 0.012f, 0.0035f, 0.007f);
            // The palace along the back: three storeys, a balcony, stepped gables, a steep roof with dormers.
            var palace = court + new Vector3(-0.02f, 0f, 0.058f);
            PalaceBlock(b, palace, 0.15f, 0.06f, 0.115f, 0.075f);
            // Turrets cantilevered from the palace's front corners.
            foreach (int sx in new[] { -1, 1 }) Bartizan(b, palace + new Vector3(sx * 0.077f, 0.075f, -0.032f), 0.008f, 0.032f, 0.05f);
            // The chapel on the left, its apse round towards the west, a little ridge spire.
            var chapel = court + new Vector3(-0.078f, 0f, -0.03f);
            b.AddBox(chapel + new Vector3(0f, 0.032f, 0f), new Vector3(0.034f, 0.064f, 0.05f), CastleWall, CastleWall, CastleWall);
            GableRoof(b, chapel + new Vector3(0f, 0.064f, 0f), 0.038f, 0.054f, 0.04f, CastleRoof, alongZ: true);
            b.AddCylinder(chapel + new Vector3(-0.017f, 0f, 0f), 0.016f, 0.016f, 0.05f, 20, CastleWall, CastleWall, false);
            b.AddCylinder(chapel + new Vector3(-0.017f, 0.05f, 0f), 0.0175f, 0.002f, 0.03f, 20, CastleRoof, CastleRoofLight, true);
            Spire(b, chapel + new Vector3(0.0f, 0.1f, 0.012f), 0.0045f, 0.04f, CastleRoof);
            RoseWindow(b, chapel + new Vector3(0f, 0.044f, -0.0255f), Vector3.back, 0.009f);
            Windows(b, chapel + new Vector3(0.0175f, 0.034f, 0f), Vector3.right, Vector3.forward, 3, 0.014f, 0.004f, 0.016f);
            // The stair tower at the palace's front.
            RoundTower(b, palace + new Vector3(0.045f, 0f, -0.036f), 0.012f, 0.15f, 0.065f, CastleRoofLight, 31);
            // The great keep: square, then octagonal with a ring of corbels, the belfry, and the tallest spire.
            var keep = court + new Vector3(0.072f, 0f, 0.046f);
            const float keepH = 0.19f, s = 0.044f;
            b.AddBox(keep + new Vector3(0f, keepH / 2, 0f), new Vector3(s, keepH, s), CastleWall, CastleWall, CastleWall);
            Quoins(b, keep, s, keepH);
            for (int floor = 0; floor < 5; floor++)
            {
                float y = 0.035f + floor * 0.03f;
                foreach (var (n, r) in new[] { (Vector3.back, Vector3.right), (Vector3.right, Vector3.forward), (Vector3.left, Vector3.back), (Vector3.forward, Vector3.left) })
                    Windows(b, keep + Vector3.up * y + n * (s / 2 + 0.0002f), n, r, floor == 4 ? 2 : 1, 0.014f, 0.004f, floor == 4 ? 0.012f : 0.009f);
                if (floor < 4) StringCourse(b, keep + Vector3.up * (y + 0.016f), s);
            }
            var top = keep + Vector3.up * keepH;
            for (int k = 0; k < 16; k++)   // corbels under the octagon
            {
                float a = k / 16f * Mathf.PI * 2f;
                b.AddBox(top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.026f + Vector3.down * 0.002f, new Vector3(0.003f, 0.004f, 0.003f), CastleTrim);
            }
            b.AddCylinder(top, 0.03f, 0.03f, 0.003f, 8, CastleTrim, CastleTrim, true, true, false, 22.5f * Mathf.Deg2Rad);
            b.AddCylinder(top + Vector3.up * 0.003f, 0.025f, 0.025f, 0.04f, 8, CastleWall, CastleWall, false, false, false, 22.5f * Mathf.Deg2Rad);
            for (int k = 0; k < 8; k++)
            {
                float a = (k + 0.5f) / 8f * Mathf.PI * 2f + 22.5f * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                ArchedOpening(b, top + Vector3.up * 0.008f + n * 0.0232f, n, 0.0055f, 0.02f, CastleGlass);
            }
            for (int k = 0; k < 8; k++)   // pinnacles round the belfry's top
            {
                float a = k / 8f * Mathf.PI * 2f + 22.5f * Mathf.Deg2Rad;
                var p = top + Vector3.up * 0.043f + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.026f;
                b.AddCylinder(p, 0.0025f, 0f, 0.016f, 6, CastleRoofLight, CastleGold, false);
            }
            b.AddCylinder(top + Vector3.up * 0.043f, 0.027f, 0.027f, 0.003f, 8, CastleTrim, CastleTrim, true, true, false, 22.5f * Mathf.Deg2Rad);
            Spire(b, top + Vector3.up * 0.046f, 0.024f, 0.13f, CastleRoof);
            return 0.38f;
        }

        // ---------------------------------------------------------------- castle parts

        /// <summary>The crag the castle stands on: rock rising steeply to a plateau, firs and boulders on its slopes.</summary>
        private static void Crag(MeshBuilder b, ref Rng rng, float top)
        {
            const int n = 28;
            var rim = new Vector3[n]; var foot = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a) * 0.15f, 0f, Mathf.Sin(a) * 0.128f);
                float jitter = 1f + Noise.Hash(i, 3, 17) * 0.06f;
                rim[i] = dir * jitter + Vector3.up * top;
                foot[i] = dir * (jitter + 0.1f + Noise.Hash(i, 5, 11) * 0.08f);
            }
            var centre = Vector3.up * top;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var shade = Pal.Shade(CastleRock, 0.82f + 0.3f * Noise.Hash(i, 9, 4));
                // The face, broken by a ledge halfway.
                var midI = Vector3.Lerp(foot[i], rim[i], 0.5f) + (rim[i] - centre).normalized * 0.004f * Noise.Hash(i, 1, 2);
                var midJ = Vector3.Lerp(foot[j], rim[j], 0.5f) + (rim[j] - centre).normalized * 0.004f * Noise.Hash(j, 1, 2);
                b.AddTriangleOutward(foot[i], foot[j], midJ, centre + Vector3.down * 0.05f, shade); b.AddTriangleOutward(foot[i], midJ, midI, centre + Vector3.down * 0.05f, shade);
                var upper = Pal.Shade(shade, 1.08f);
                b.AddTriangleOutward(midI, midJ, rim[j], centre + Vector3.down * 0.05f, upper); b.AddTriangleOutward(midI, rim[j], rim[i], centre + Vector3.down * 0.05f, upper);
                b.AddTriangleOutward(rim[i], rim[j], centre, centre + Vector3.down, Pal.Hex("#7DA858"));   // grass on top
            }
            // Firs and boulders on the slopes.
            for (int k = 0; k < 14; k++)
            {
                int i = (k * 5 + 2) % n;
                if (i >= 5 && i <= 9) continue;   // (not on the stair, at the front)
                var p = Vector3.Lerp(foot[i], rim[i], 0.25f + 0.15f * (k % 3));
                if (k % 2 == 0)
                {
                    b.AddCylinder(p, 0.0012f, 0.001f, 0.006f, 5, Pal.Bark, Pal.Bark, false);
                    b.AddCylinder(p + Vector3.up * 0.004f, 0.0075f, 0f, 0.022f, 8, Pal.Hex("#2E5A36"), Pal.Hex("#3E7046"), false);
                }
                else b.AddBlob(p + Vector3.up * 0.002f, new Vector3(0.006f, 0.004f, 0.005f), 1, 0.25f, 40 + k, (_, __) => Pal.Shade(CastleRock, 1.1f));
            }
        }

        /// <summary>A stretch of curtain wall: a walkway between crenellated parapets, corbels under the outer one.</summary>
        private static void CurtainWall(MeshBuilder b, Vector3 a, Vector3 c, float h)
        {
            var d = c - a; float len = d.magnitude; var along = d / len;
            var outward = Vector3.Cross(Vector3.up, along);
            b.Push((a + c) / 2f, Quaternion.LookRotation(along, Vector3.up));
            b.AddBox(new Vector3(0f, h / 2, 0f), new Vector3(0.009f, h, len), CastleWall, CastleTrim, CastleWall);
            b.AddBox(new Vector3(0f, h * 0.35f, 0f), new Vector3(0.0094f, 0.0012f, len), CastleTrim);   // a string course
            b.Pop();
            Crenels(b, a + Vector3.up * h + outward * 0.004f, c + Vector3.up * h + outward * 0.004f, 0.0055f);
            Crenels(b, a + Vector3.up * h - outward * 0.004f, c + Vector3.up * h - outward * 0.004f, 0.0055f);
            if (b.TooSmall(0.002f)) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 0.006f));
            for (int k = 0; k < n; k++) b.AddBox(Vector3.Lerp(a, c, (k + 0.5f) / n) + Vector3.up * (h - 0.003f) + outward * 0.0053f, new Vector3(0.0018f, 0.0035f, 0.0018f), CastleTrim);
        }

        /// <summary>Merlons along the top of a wall, from <paramref name="a"/> to <paramref name="c"/>.</summary>
        private static void Crenels(MeshBuilder b, Vector3 a, Vector3 c, float size)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt((c - a).magnitude / (size * 1.6f)));
            var along = (c - a).normalized;
            var r = Quaternion.LookRotation(along, Vector3.up);
            for (int k = 0; k <= n; k += 2)
            {
                b.Push(Vector3.Lerp(a, c, k / (float)n) + Vector3.up * size * 0.5f, r);
                b.AddBox(Vector3.zero, new Vector3(size * 0.45f, size, size), CastleWall, CastleTrim, CastleWall);
                b.Pop();
            }
        }

        /// <summary>
        /// A round tower: plinth, walls with arched windows a storey apart turning round it, a ring of corbels under a
        /// crenellated parapet, and a tall conical roof banded in gold with a finial and pennant.
        /// </summary>
        private static void RoundTower(MeshBuilder b, Vector3 at, float r, float h, float roofH, Color roof, int seed)
        {
            b.AddCylinder(at, r * 1.12f, r * 1.05f, 0.006f, 24, CastleTrim, CastleTrim, true);
            b.AddCylinder(at, r, r, h, 24, CastleWall, CastleWall, false);
            int storeys = Mathf.Max(2, Mathf.RoundToInt(h / 0.024f));
            for (int k = 0; k < storeys; k++)
            {
                float y = h * (0.22f + 0.66f * k / Mathf.Max(1, storeys - 1));
                for (int side = 0; side < 3; side++)
                {
                    float a = (side * 120f + k * 50f + seed * 23f) * Mathf.Deg2Rad;
                    var n = new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a));
                    ArchedOpening(b, at + Vector3.up * (y - r * 0.4f) + n * (r - 0.0002f), n, r * 0.22f, r * 0.75f, CastleGlass);
                }
                if (k > 0 && k < storeys - 1) b.AddCylinder(at + Vector3.up * (y - r * 0.6f), r * 1.03f, r * 1.03f, 0.001f, 24, CastleTrim, CastleTrim, false);
            }
            // Corbels and the parapet.
            if (!b.TooSmall(0.002f))
                for (int k = 0; k < 14; k++)
                {
                    float a = k / 14f * Mathf.PI * 2f;
                    b.AddBox(at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r * 1.08f) + Vector3.up * (h - 0.002f), new Vector3(0.0018f, 0.004f, 0.0018f), CastleTrim);
                }
            b.AddCylinder(at + Vector3.up * h, r * 1.18f, r * 1.18f, 0.0055f, 24, CastleWall, CastleTrim, true, true);
            if (!b.TooSmall(0.002f))
                for (int k = 0; k < 12; k++)
                {
                    float a = (k + 0.5f) / 12f * Mathf.PI * 2f;
                    b.AddBox(at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r * 1.12f) + Vector3.up * (h + 0.0075f), new Vector3(0.0028f, 0.004f, 0.0028f), CastleWall);
                }
            Spire(b, at + Vector3.up * (h + 0.0055f), r * 1.1f, roofH, roof);
        }

        /// <summary>A conical spire with gold bands, a gold finial and a red pennant.</summary>
        private static void Spire(MeshBuilder b, Vector3 at, float r, float h, Color roof)
        {
            b.AddCylinder(at, r, 0.0005f, h, 24, roof, Pal.Shade(roof, 1.25f), true);
            for (int k = 1; k <= 3; k++)
            {
                float t = k * 0.22f;
                b.AddCylinder(at + Vector3.up * (h * t), r * (1f - t) + 0.0004f, r * (1f - t - 0.02f) + 0.0004f, h * 0.02f, 24, CastleGold, CastleGold, false);
            }
            b.AddBlob(at + Vector3.up * (h + 0.002f), Vector3.one * Mathf.Max(0.0012f, r * 0.12f), 1, 0f, 7, (_, __) => CastleGold);
            b.AddCylinder(at + Vector3.up * h, 0.0004f, 0.0004f, 0.014f, 6, CastleGold, CastleGold, false);
            b.AddBox(at + new Vector3(0.0045f, h + 0.0115f, 0f), new Vector3(0.009f, 0.004f, 0.0005f), Pal.Hex("#D8383E"));
        }

        /// <summary>A little turret cantilevered from a corner: a corbelled cone beneath, a needle spire above.</summary>
        private static void Bartizan(MeshBuilder b, Vector3 at, float r, float h, float roofH)
        {
            b.AddCylinder(at + Vector3.down * 0.012f, 0.001f, r, 0.012f, 16, CastleTrim, CastleTrim, false);
            b.AddCylinder(at, r, r, h, 16, CastleWall, CastleWall, false);
            ArchedOpening(b, at + Vector3.up * h * 0.4f + Vector3.back * (r - 0.0002f), Vector3.back, r * 0.3f, r * 1.1f, CastleGlass);
            b.AddCylinder(at + Vector3.up * h, r * 1.15f, r * 1.15f, 0.002f, 16, CastleTrim, CastleTrim, true, true);
            Spire(b, at + Vector3.up * (h + 0.002f), r * 1.1f, roofH, CastleRoofLight);
        }

        /// <summary>
        /// The palace: a long block of three storeys with arched windows (a balcony across the first floor, stone string
        /// courses, quoins at the corners), stepped gables at the ends, a steep roof with dormers and a gold ridge.
        /// </summary>
        private static void PalaceBlock(MeshBuilder b, Vector3 at, float w, float d, float h, float roofH)
        {
            b.AddBox(at + Vector3.up * (h / 2), new Vector3(w, h, d), CastleWall, CastleWall, CastleWall);
            b.AddBox(at + Vector3.up * 0.004f, new Vector3(w + 0.003f, 0.008f, d + 0.003f), CastleTrim);   // the plinth
            Quoins(b, at, w, h, d);
            for (int floor = 0; floor < 3; floor++)
            {
                float y = 0.018f + floor * 0.034f;
                Windows(b, at + new Vector3(0f, y, -d / 2 - 0.0002f), Vector3.back, Vector3.right, 7, w - 0.02f, 0.0045f, floor == 0 ? 0.018f : 0.015f);
                Windows(b, at + new Vector3(0f, y, d / 2 + 0.0002f), Vector3.forward, Vector3.left, 6, w - 0.03f, 0.0042f, 0.014f);
                if (floor < 2) b.AddBox(at + new Vector3(0f, y + 0.02f, 0f), new Vector3(w + 0.0016f, 0.0016f, d + 0.0016f), CastleTrim);
            }
            // The balcony across the front of the first floor.
            b.AddBox(at + new Vector3(0f, 0.044f, -d / 2 - 0.006f), new Vector3(w * 0.6f, 0.0018f, 0.012f), CastleTrim);
            if (!b.TooSmall(0.002f))
            {
                for (int k = 0; k <= 22; k++) b.AddBox(at + new Vector3(-w * 0.3f + k * w * 0.6f / 22f, 0.0475f, -d / 2 - 0.0115f), new Vector3(0.0008f, 0.005f, 0.0008f), CastleTrim);
                for (int k = 0; k < 5; k++) b.AddCylinder(at + new Vector3(-w * 0.28f + k * w * 0.14f, 0.044f - 0.012f, -d / 2 - 0.011f), 0.0012f, 0.0018f, 0.012f, 6, CastleTrim, CastleTrim, false);   // corbels under it
            }
            b.AddBox(at + new Vector3(0f, 0.0505f, -d / 2 - 0.0115f), new Vector3(w * 0.6f, 0.0012f, 0.0014f), CastleTrim);
            // The roof, with dormers on both sides and a gold ridge cresting.
            GableRoof(b, at + Vector3.up * h, w + 0.006f, d + 0.008f, roofH, CastleRoof, alongZ: false);
            for (int k = 0; k < 4; k++)
            {
                float x = -w * 0.36f + k * w * 0.24f;
                foreach (int side in new[] { -1, 1 })
                {
                    var p = at + new Vector3(x, h + roofH * 0.3f, side * d * 0.32f);
                    b.AddBox(p + Vector3.up * 0.007f, new Vector3(0.011f, 0.014f, 0.012f), CastleWall);
                    GableRoof(b, p + new Vector3(0f, 0.014f, 0f), 0.014f, 0.016f, 0.011f, CastleRoofLight, alongZ: true);
                    Windows(b, p + new Vector3(0f, 0.007f, side * 0.0062f), side < 0 ? Vector3.back : Vector3.forward, Vector3.right, 1, 0.01f, 0.0035f, 0.008f);
                }
            }
            if (!b.TooSmall(0.002f))
                for (int k = 0; k <= 18; k++) b.AddCylinder(at + new Vector3(-w / 2 + k * w / 18f, h + roofH, 0f), 0.0009f, 0f, 0.006f, 4, CastleGold, CastleGold, false);
            // Stepped gables at both ends.
            foreach (int sx in new[] { -1, 1 })
                for (int step = 0; step < 5; step++)
                {
                    float y = h + step * roofH / 5f, half = (d / 2 + 0.004f) * (1f - step / 5f);
                    b.AddBox(at + new Vector3(sx * (w / 2 + 0.002f), y + roofH / 10f, 0f), new Vector3(0.005f, roofH / 5f, half * 2f), CastleWall, CastleTrim, CastleWall);
                }
            // Chimneys.
            foreach (float x in new[] { -w * 0.2f, w * 0.25f }) b.AddBox(at + new Vector3(x, h + roofH * 0.75f, 0.006f), new Vector3(0.006f, 0.03f, 0.006f), CastleTrim, CastleTrim, CastleTrim);
        }

        /// <summary>A gable roof: two slopes and two gable ends over a <paramref name="w"/> by <paramref name="d"/> rectangle.</summary>
        private static void GableRoof(MeshBuilder b, Vector3 eave, float w, float d, float h, Color roof, bool alongZ)
        {
            // Courses of slate across each slope (big roofs only).
            if (!b.TooSmall(0.003f) && Mathf.Max(w, d) > 0.05f)
            {
                float run = alongZ ? w / 2 : d / 2, len = alongZ ? d : w;
                int courses = Mathf.Max(2, Mathf.RoundToInt(h / 0.007f));
                for (int k = 1; k < courses; k++)
                {
                    float t = k / (float)courses;
                    foreach (int side in new[] { -1, 1 })
                    {
                        var p = eave + Vector3.up * (h * t) + (alongZ ? Vector3.right : Vector3.forward) * (side * run * (1f - t) * 1.003f);
                        var lean = Quaternion.LookRotation(alongZ ? Vector3.forward : Vector3.right, Vector3.up) * Quaternion.Euler(0f, 0f, side * Mathf.Atan2(h, run) * Mathf.Rad2Deg * (alongZ ? -1f : 1f));
                        b.Push(p, lean);
                        b.AddBox(Vector3.zero, new Vector3(0.0012f, 0.0007f, len), Pal.Shade(roof, 0.82f));
                        b.Pop();
                    }
                }
            }
            // The ridge runs along X (or along Z).
            float rx = alongZ ? 0f : w / 2, rz = alongZ ? d / 2 : 0f;
            float ex = w / 2, ez = d / 2;
            var r0 = eave + new Vector3(-rx, h, -rz); var r1 = eave + new Vector3(rx, h, rz);
            var inside = eave + Vector3.up * (h * 0.3f);
            var light = Pal.Shade(roof, 1.12f);
            if (!alongZ)
            {
                var a = eave + new Vector3(-ex, 0f, -ez); var c = eave + new Vector3(ex, 0f, -ez);
                var e = eave + new Vector3(-ex, 0f, ez); var f = eave + new Vector3(ex, 0f, ez);
                b.AddTriangleOutward(a, c, r1, inside, roof); b.AddTriangleOutward(a, r1, r0, inside, roof);
                b.AddTriangleOutward(e, f, r1, inside, light); b.AddTriangleOutward(e, r1, r0, inside, light);
                b.AddTriangleOutward(a, e, r0, inside, CastleWall); b.AddTriangleOutward(c, f, r1, inside, CastleWall);
            }
            else
            {
                var a = eave + new Vector3(-ex, 0f, -ez); var c = eave + new Vector3(-ex, 0f, ez);
                var e = eave + new Vector3(ex, 0f, -ez); var f = eave + new Vector3(ex, 0f, ez);
                b.AddTriangleOutward(a, c, r1, inside, roof); b.AddTriangleOutward(a, r1, r0, inside, roof);
                b.AddTriangleOutward(e, f, r1, inside, light); b.AddTriangleOutward(e, r1, r0, inside, light);
                b.AddTriangleOutward(a, e, r0, inside, CastleWall); b.AddTriangleOutward(c, f, r1, inside, CastleWall);
            }
        }

        /// <summary>A row of <paramref name="count"/> arched windows centred on a wall point, spread over <paramref name="span"/>.</summary>
        private static void Windows(MeshBuilder b, Vector3 centre, Vector3 normal, Vector3 right, int count, float span, float halfW, float height)
        {
            for (int k = 0; k < count; k++)
            {
                float x = count == 1 ? 0f : -span / 2 + span * k / (count - 1);
                ArchedOpening(b, centre + right * x - Vector3.up * height * 0.5f, normal, halfW, height, CastleGlass);
            }
        }

        /// <summary>
        /// An arched opening on a wall facing <paramref name="normal"/>, from its sill (at <paramref name="foot"/>) up:
        /// glass (or a gateway's dark), a round head, a stone frame and sill; close up, a mullion and transom.
        /// </summary>
        private static void ArchedOpening(MeshBuilder b, Vector3 foot, Vector3 normal, float halfW, float height, Color fill)
        {
            if (b.TooSmall(halfW * 2f)) return;
            var up = Vector3.up; var right = Vector3.Cross(up, normal).normalized;
            float straight = Mathf.Max(0f, height - halfW);
            var out1 = normal * 0.0003f;
            // The frame, just proud of the wall.
            if (!b.TooSmall(halfW * 3f)) b.AddFace(foot + up * (straight / 2) + out1 * 0.5f, normal, up, halfW * 1.35f, straight / 2 + halfW * 0.2f, CastleTrim);
            b.AddFace(foot + up * (straight / 2) + out1, normal, up, halfW, straight / 2, fill);
            // The round head: a fan.
            int seg = Mathf.Max(3, b.Facets(8, 3));
            var c = foot + up * straight + out1;
            int centre = b.AddVertex(c, normal, fill);
            for (int k = 0; k <= seg; k++)
            {
                float a = Mathf.PI * k / seg;
                b.AddVertex(c + right * (Mathf.Cos(a) * halfW) + up * (Mathf.Sin(a) * halfW), normal, fill);
                if (k > 0)
                {
                    var p0 = b.Vertices[centre + k]; var p1 = b.Vertices[centre + k + 1];
                    if (Vector3.Dot(Vector3.Cross(p0 - b.Vertices[centre], p1 - b.Vertices[centre]), normal) > 0f) b.AddTriangle(centre, centre + k, centre + k + 1);
                    else b.AddTriangle(centre, centre + k + 1, centre + k);
                }
            }
            if (!b.TooSmall(halfW * 3f)) b.AddBox(foot + up * -0.0006f + normal * 0.0008f, new Vector3(Mathf.Abs(right.x) * halfW * 2.8f + Mathf.Abs(right.z) * 0.0016f + 0.0002f, 0.0012f, Mathf.Abs(right.z) * halfW * 2.8f + Mathf.Abs(right.x) * 0.0016f + 0.0002f), CastleTrim);
            if (b.Detail < 0 && fill == CastleGlass)
            {
                b.AddFace(foot + up * (height / 2) + out1 * 2f, normal, up, 0.00025f, height / 2, CastleTrim);           // mullion
                b.AddFace(foot + up * (straight * 0.62f) + out1 * 2f, normal, up, halfW, 0.00025f, CastleTrim);          // transom
            }
        }

        /// <summary>A round rose window: tracery spokes over coloured glass in a stone ring.</summary>
        private static void RoseWindow(MeshBuilder b, Vector3 c, Vector3 normal, float r)
        {
            b.Push(c, Quaternion.LookRotation(-normal, Vector3.up) * Quaternion.Euler(90f, 0f, 0f));
            b.AddCylinder(Vector3.zero, r * 1.2f, r * 1.2f, 0.0008f, 24, CastleTrim, CastleTrim, true);
            b.AddCylinder(new Vector3(0f, 0.0008f, 0f), r, r, 0.0003f, 24, Pal.Hex("#C8392E"), Pal.Hex("#B0304A"), true);
            if (!b.TooSmall(0.002f))
                for (int k = 0; k < 8; k++)
                {
                    b.Push(new Vector3(0f, 0.0012f, 0f), Quaternion.Euler(0f, k * 22.5f, 0f));
                    b.AddBox(Vector3.zero, new Vector3(r * 2f, 0.0004f, 0.0005f), CastleGold);
                    b.Pop();
                }
            b.Pop();
        }

        /// <summary>Stone quoins up the corners of a block (alternating long and short).</summary>
        private static void Quoins(MeshBuilder b, Vector3 at, float w, float h, float d = -1f)
        {
            if (d < 0f) d = w;
            if (b.TooSmall(0.003f)) return;
            int n = Mathf.Max(2, Mathf.RoundToInt(h / 0.007f));
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    for (int k = 0; k < n; k++)
                    {
                        bool longX = k % 2 == 0;
                        var size = new Vector3(longX ? 0.007f : 0.0045f, h / n * 0.8f, longX ? 0.0045f : 0.007f);
                        b.AddBox(at + new Vector3(sx * (w / 2 - size.x / 2 + 0.0004f), (k + 0.5f) * h / n, sz * (d / 2 - size.z / 2 + 0.0004f)), size, CastleTrim);
                    }
        }

        /// <summary>A stone band round a square tower of side <paramref name="s"/>.</summary>
        private static void StringCourse(MeshBuilder b, Vector3 at, float s) => b.AddBox(at, new Vector3(s + 0.0016f, 0.0014f, s + 0.0016f), CastleTrim);
    }
}
