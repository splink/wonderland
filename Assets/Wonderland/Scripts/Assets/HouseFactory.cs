using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Three storybook houses built from parts (stone plinth, plaster walls, timber framing, framed glowing windows with
    /// shutters and flower boxes, doors with lamps, tiled or thatched roofs, chimneys). Pivot at the ground centre; the
    /// plinth reaches 4 cm below ground so a house can sit on gentle slopes. Front faces -Z.
    /// </summary>
    public static class HouseFactory
    {
        public const int VariantCount = 3;
        public static readonly string[] Names = { "Cottage", "Townhouse", "Thatched" };
        private static readonly Mesh[] Cache = new Mesh[VariantCount];

        public static Mesh Get(int variant)
        {
            if (Cache[variant] == null) Cache[variant] = Build(variant);
            return Cache[variant];
        }

        /// <summary>Half extents of the footprint including eaves (x, z).</summary>
        public static Vector2 HalfExtents(int variant) => variant switch
        {
            0 => new Vector2(0.061f, 0.05f),
            1 => new Vector2(0.05f, 0.047f),
            _ => new Vector2(0.072f, 0.054f),
        };

        public static Mesh Build(int variant)
        {
            var b = new MeshBuilder();
            var rng = new Rng(500 + variant * 31);
            switch (variant)
            {
                case 0: Cottage(b, ref rng); break;
                case 1: Townhouse(b, ref rng); break;
                default: Thatched(b, ref rng); break;
            }
            return b.ToMesh("House_" + Names[variant]);
        }

        private static void Cottage(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.105f, D = 0.08f, H = 0.06f, R = 0.046f;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, H, Pal.Plaster);
            TimberFrame(b, W, D, H);
            var frame = Pal.FrameWhite;
            Wall(b, new Vector3(-0.022f, 0.006f, -D / 2), Vector3.back); Door(b, 0.019f, 0.034f, Pal.DoorGreen, false); b.Pop();
            Wall(b, new Vector3(0.022f, 0.033f, -D / 2), Vector3.back); Window(b, ref rng, 0.018f, 0.02f, frame, Pal.ShutterBlue, true, true); b.Pop();
            Wall(b, new Vector3(-0.022f, 0.033f, D / 2), Vector3.forward); Window(b, ref rng, 0.018f, 0.02f, frame, Pal.ShutterBlue, true, false); b.Pop();
            Wall(b, new Vector3(0.024f, 0.033f, D / 2), Vector3.forward); Window(b, ref rng, 0.016f, 0.018f, frame, Pal.ShutterBlue, false, true); b.Pop();
            Wall(b, new Vector3(W / 2, 0.031f, 0f), Vector3.right); Window(b, ref rng, 0.016f, 0.018f, frame, Pal.ShutterBlue, true, true); b.Pop();
            Wall(b, new Vector3(-W / 2, 0.031f, 0f), Vector3.left); Window(b, ref rng, 0.016f, 0.018f, frame, Pal.ShutterBlue, true, false); b.Pop();
            // Little gable windows (attic).
            Wall(b, new Vector3(W / 2, H + 0.015f, 0f), Vector3.right); Window(b, ref rng, 0.01f, 0.011f, frame, Pal.ShutterBlue, false, false); b.Pop();
            GableRoof(b, ref rng, W, D, H, R, 0.009f, Pal.RoofTerracotta, Pal.Plaster, 6, true);
            Chimney(b, ref rng, new Vector3(0.028f, H, 0.016f), H + R + 0.014f - H, Pal.Brick);
            Shrubs(b, ref rng, W, D, 3);
        }

        private static void Townhouse(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.085f, D = 0.075f, H = 0.096f, R = 0.05f;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, H, Pal.PlasterPink);
            // String course between floors and corner quoins.
            b.AddBox(new Vector3(0f, 0.05f, 0f), new Vector3(W + 0.003f, 0.003f, D + 0.003f), Pal.FrameWhite);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            for (int k = 0; k < 7; k++)
            {
                float y = 0.008f + k * 0.0125f;
                var size = k % 2 == 0 ? new Vector3(0.009f, 0.007f, 0.005f) : new Vector3(0.005f, 0.007f, 0.009f);
                b.AddBox(new Vector3(sx * (W / 2 - size.x / 2 + 0.001f), y, sz * (D / 2 - size.z / 2 + 0.001f)), size,
                    Pal.Jitter(Pal.Stone, ref rng, 0.2f));
            }
            var frame = Pal.FrameWhite;
            Wall(b, new Vector3(0f, 0.006f, -D / 2), Vector3.back); Door(b, 0.018f, 0.036f, Pal.DoorRed, false); b.Pop();
            foreach (float x in new[] { -0.026f, 0.026f })
                { Wall(b, new Vector3(x, 0.027f, -D / 2), Vector3.back); Window(b, ref rng, 0.015f, 0.021f, frame, Pal.ShutterGreen, true, true); b.Pop(); }
            foreach (float x in new[] { -0.026f, 0f, 0.026f })
                { Wall(b, new Vector3(x, 0.072f, -D / 2), Vector3.back); Window(b, ref rng, 0.014f, 0.02f, frame, Pal.ShutterGreen, false, true); b.Pop(); }
            foreach (float x in new[] { -0.02f, 0.02f })
            {
                Wall(b, new Vector3(x, 0.027f, D / 2), Vector3.forward); Window(b, ref rng, 0.015f, 0.021f, frame, Pal.ShutterGreen, true, false); b.Pop();
                Wall(b, new Vector3(x, 0.072f, D / 2), Vector3.forward); Window(b, ref rng, 0.014f, 0.02f, frame, Pal.ShutterGreen, false, false); b.Pop();
            }
            foreach (int s in new[] { -1, 1 })
            {
                var n = new Vector3(s, 0f, 0f);
                Wall(b, new Vector3(s * W / 2, 0.027f, 0f), n); Window(b, ref rng, 0.014f, 0.02f, frame, Pal.ShutterGreen, true, false); b.Pop();
                Wall(b, new Vector3(s * W / 2, 0.072f, 0f), n); Window(b, ref rng, 0.014f, 0.02f, frame, Pal.ShutterGreen, false, true); b.Pop();
            }
            GableRoof(b, ref rng, W, D, H, R, 0.007f, Pal.RoofSlate, Pal.PlasterPink, 7, false);
            // Dormer on the front slope.
            float slopeZ = -D / 2 + 0.022f;
            b.Push(new Vector3(0f, H + 0.004f, slopeZ - 0.004f));
            b.AddBox(new Vector3(0f, 0.012f, 0.006f), new Vector3(0.026f, 0.024f, 0.022f), Pal.PlasterPink);
            Wall(b, new Vector3(0f, 0.013f, -0.005f), Vector3.back); Window(b, ref rng, 0.011f, 0.013f, frame, Pal.ShutterGreen, false, false); b.Pop();
            b.Push(new Vector3(0f, 0.024f, 0.006f), Quaternion.Euler(0f, 90f, 0f));
            GableRoof(b, ref rng, 0.024f, 0.026f, 0f, 0.013f, 0.003f, Pal.RoofSlate, Pal.PlasterPink, 3, false);
            b.Pop();
            b.Pop();
            Chimney(b, ref rng, new Vector3(-0.024f, H, 0.012f), R + 0.016f, Pal.StoneDark);
            Chimney(b, ref rng, new Vector3(0.026f, H, 0.012f), R + 0.012f, Pal.StoneDark);
            Shrubs(b, ref rng, W, D, 2);
        }

        private static void Thatched(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.115f, D = 0.076f, H = 0.048f, R = 0.058f;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, H, Pal.PlasterWarm);
            var frame = Pal.Timber;
            Wall(b, new Vector3(0f, 0.006f, -D / 2), Vector3.back); Door(b, 0.02f, 0.03f, Pal.DoorGreen, true); b.Pop();
            foreach (float x in new[] { -0.034f, 0.034f })
                { Wall(b, new Vector3(x, 0.027f, -D / 2), Vector3.back); RoundWindow(b, 0.0085f, frame); b.Pop(); }
            foreach (float x in new[] { -0.03f, 0.03f })
                { Wall(b, new Vector3(x, 0.027f, D / 2), Vector3.forward); RoundWindow(b, 0.008f, frame); b.Pop(); }
            Wall(b, new Vector3(-W / 2, 0.027f, 0f), Vector3.left); Window(b, ref rng, 0.014f, 0.016f, frame, Pal.ShutterGreen, true, true); b.Pop();
            ThatchRoof(b, ref rng, W, D, H, R);
            Chimney(b, ref rng, new Vector3(W / 2 - 0.012f, H - 0.01f, 0f), R + 0.022f, Pal.Brick);
            Shrubs(b, ref rng, W, D, 5);
        }

        // ---------------------------------------------------------------- parts

        private static void Foundation(MeshBuilder b, ref Rng rng, float w, float d)
        {
            b.AddBox(new Vector3(0f, -0.017f, 0f), new Vector3(w + 0.008f, 0.046f, d + 0.008f), Pal.StoneDark, Pal.Stone, Pal.StoneDark);
            // Irregular stones peeking out along the base.
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float len = alongX ? w + 0.008f : d + 0.008f;
                float edge = alongX ? (d / 2 + 0.004f) : (w / 2 + 0.004f);
                float sgn = side % 2 == 0 ? -1f : 1f;
                for (float t = -len / 2 + 0.005f; t < len / 2 - 0.004f; t += rng.Range(0.007f, 0.011f))
                {
                    var size = new Vector3(rng.Range(0.005f, 0.009f), rng.Range(0.004f, 0.006f), 0.002f);
                    var c = Pal.Jitter(Pal.Stone, ref rng, 0.35f);
                    float y = rng.Range(-0.001f, 0.002f);
                    if (alongX) b.AddBox(new Vector3(t, y, sgn * edge), size, c);
                    else b.AddBox(new Vector3(sgn * edge, y, t), new Vector3(size.z, size.y, size.x), c);
                }
            }
        }

        private static void Walls(MeshBuilder b, float w, float d, float h, Color plaster)
        {
            var bottom = Pal.Shade(plaster, 0.9f);
            // Four wall faces with a soft vertical gradient (darker at the base).
            for (int i = 0; i < 4; i++)
            {
                var n = i switch { 0 => Vector3.back, 1 => Vector3.forward, 2 => Vector3.left, _ => Vector3.right };
                float half = i < 2 ? w / 2 : d / 2;
                var c = (i < 2 ? d / 2 : w / 2) * n;
                var right = Vector3.Cross(n, Vector3.up);
                int v = b.AddVertex(c - right * half + Vector3.up * 0.004f, n, bottom);
                b.AddVertex(c - right * half + Vector3.up * h, n, plaster);
                b.AddVertex(c + right * half + Vector3.up * h, n, plaster);
                b.AddVertex(c + right * half + Vector3.up * 0.004f, n, bottom);
                b.AddTriangle(v, v + 1, v + 2); b.AddTriangle(v, v + 2, v + 3);
            }
            b.AddFace(new Vector3(0f, h, 0f), Vector3.up, Vector3.forward, w / 2, d / 2, plaster);
        }

        private static void TimberFrame(MeshBuilder b, float w, float d, float h)
        {
            var t = Pal.Timber; const float s = 0.0032f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                b.AddBox(new Vector3(sx * (w / 2 - s / 2 + 0.0008f), h / 2 + 0.002f, sz * (d / 2 - s / 2 + 0.0008f)), new Vector3(s, h - 0.004f, s), t);
            foreach (float y in new[] { 0.0055f, h * 0.55f, h - 0.0015f })
            {
                b.AddBox(new Vector3(0f, y, -d / 2 - 0.0004f), new Vector3(w + 0.001f, s * 0.8f, 0.0024f), t);
                b.AddBox(new Vector3(0f, y, d / 2 + 0.0004f), new Vector3(w + 0.001f, s * 0.8f, 0.0024f), t);
                b.AddBox(new Vector3(-w / 2 - 0.0004f, y, 0f), new Vector3(0.0024f, s * 0.8f, d + 0.001f), t);
                b.AddBox(new Vector3(w / 2 + 0.0004f, y, 0f), new Vector3(0.0024f, s * 0.8f, d + 0.001f), t);
            }
            // Diagonal braces on the upper storey band of the long walls.
            float y0 = h * 0.55f, y1 = h - 0.0015f, len = Mathf.Sqrt(0.018f * 0.018f + (y1 - y0) * (y1 - y0));
            float angle = Mathf.Atan2(y1 - y0, 0.018f) * Mathf.Rad2Deg;
            foreach (float z in new[] { -d / 2 - 0.0005f, d / 2 + 0.0005f })
            foreach (int s2 in new[] { -1, 1 })
            {
                b.Push(new Vector3(s2 * (w / 2 - 0.012f), (y0 + y1) / 2, z), Quaternion.Euler(0f, 0f, s2 * angle));
                b.AddBox(Vector3.zero, new Vector3(len, 0.0024f, 0.0022f), t);
                b.Pop();
            }
        }

        /// <summary>Pushes a frame on a wall (origin at <paramref name="position"/>, +Z = outward normal); the caller pops it.</summary>
        private static void Wall(MeshBuilder b, Vector3 position, Vector3 normal) =>
            b.Push(position, Quaternion.LookRotation(normal, Vector3.up));

        private static void Window(MeshBuilder b, ref Rng rng, float w, float h, Color frame, Color shutter, bool shutters, bool flowers)
        {
            const float t = 0.0016f, d = 0.0022f;
            b.AddFace(new Vector3(0f, 0f, 0.0004f), Vector3.forward, Vector3.up, w / 2, h / 2, Pal.Glow(Pal.WindowGlow));
            b.AddBox(new Vector3(0f, h / 2 + t / 2, d / 2), new Vector3(w + 2 * t, t, d), frame);
            b.AddBox(new Vector3(0f, -h / 2 - t * 0.6f, d * 0.8f), new Vector3(w + 3.2f * t, t * 1.2f, d * 1.6f), frame);
            b.AddBox(new Vector3(-w / 2 - t / 2, 0f, d / 2), new Vector3(t, h, d), frame);
            b.AddBox(new Vector3(w / 2 + t / 2, 0f, d / 2), new Vector3(t, h, d), frame);
            b.AddBox(new Vector3(0f, 0f, d * 0.35f), new Vector3(t * 0.55f, h, d * 0.7f), frame);
            b.AddBox(new Vector3(0f, h * 0.1f, d * 0.35f), new Vector3(w, t * 0.55f, d * 0.7f), frame);
            if (shutters)
            {
                float sw = w * 0.46f;
                foreach (int s in new[] { -1, 1 })
                {
                    float x = s * (w / 2 + t + sw / 2 + 0.0003f);
                    b.AddBox(new Vector3(x, 0f, 0.0007f), new Vector3(sw, h + t, 0.0014f), shutter);
                    var slat = Pal.Shade(shutter, 0.78f);
                    for (int k = -1; k <= 1; k++)
                        b.AddBox(new Vector3(x, k * h * 0.28f, 0.0015f), new Vector3(sw * 0.8f, 0.0008f, 0.0004f), slat);
                }
            }
            if (flowers)
            {
                float y = -h / 2 - t * 1.4f - 0.0024f;
                b.AddBox(new Vector3(0f, y, 0.0035f), new Vector3(w + 2 * t, 0.004f, 0.0042f), Pal.Timber);
                var palette = new[] { Pal.FlowerRed, Pal.FlowerPink, Pal.FlowerYellow, Pal.FlowerWhite, Pal.FlowerLilac };
                int count = Mathf.RoundToInt(w / 0.0028f);
                for (int i = 0; i < count; i++)
                {
                    float x = -w / 2 + (i + 0.5f) * (w / count);
                    var leaf = Pal.Jitter(Pal.LeafMid, ref rng, 0.3f);
                    b.AddBlob(new Vector3(x, y + 0.0028f, 0.0035f), new Vector3(0.0018f, 0.0016f, 0.0018f), 0, 0.15f, i, (_, __) => leaf);
                    if (rng.Chance(0.75f))
                    {
                        var c = Pal.Jitter(palette[rng.Range(0, palette.Length)], ref rng, 0.2f);
                        b.AddBlob(new Vector3(x + rng.Range(-0.0006f, 0.0006f), y + 0.0045f, 0.0042f), Vector3.one * 0.0012f, 0, 0.1f, i + 50, (_, __) => c);
                    }
                }
            }
        }

        private static void RoundWindow(MeshBuilder b, float r, Color frame)
        {
            // Cylinder axis along local Z (outwards): rotate the +Y cylinder by 90 degrees about X.
            b.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            b.AddCylinder(new Vector3(0f, 0f, 0f), r + 0.0018f, r + 0.0018f, 0.0022f, 14, frame, frame, false);
            b.AddDisc(new Vector3(0f, 0.0006f, 0f), r, 14, Pal.Glow(Pal.WindowGlow), true);
            b.Pop();
            // Ring frame on the face plus a cross.
            b.AddBox(new Vector3(0f, 0f, 0.0016f), new Vector3(2 * r, 0.0011f, 0.0012f), frame);
            b.AddBox(new Vector3(0f, 0f, 0.0016f), new Vector3(0.0011f, 2 * r, 0.0012f), frame);
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                b.Push(new Vector3(Mathf.Cos(a) * (r + 0.0006f), Mathf.Sin(a) * (r + 0.0006f), 0.0016f), Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f));
                b.AddBox(Vector3.zero, new Vector3(r * 0.58f, 0.0016f, 0.0016f), frame);
                b.Pop();
            }
        }

        private static void Door(MeshBuilder b, float w, float h, Color colour, bool arched)
        {
            const float t = 0.0018f;
            float rect = arched ? h - w / 2 : h;
            b.AddBox(new Vector3(0f, rect / 2, 0.0006f), new Vector3(w, rect, 0.0012f), colour);
            // Planks.
            var plank = Pal.Shade(colour, 0.82f);
            for (int i = 1; i < 4; i++)
                b.AddBox(new Vector3(-w / 2 + i * w / 4, rect / 2, 0.0013f), new Vector3(0.0005f, rect * 0.96f, 0.0003f), plank);
            if (arched)
            {
                var centre = new Vector3(0f, rect, 0.0012f);
                var inside = new Vector3(0f, rect, -0.01f);
                const int seg = 10;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i / (float)seg * Mathf.PI, a1 = (i + 1) / (float)seg * Mathf.PI;
                    var p0 = centre + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * (w / 2);
                    var p1 = centre + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * (w / 2);
                    b.AddTriangleOutward(centre, p0, p1, inside, colour);
                    // Stone arch blocks.
                    var mid = (a0 + a1) / 2;
                    b.Push(centre + new Vector3(Mathf.Cos(mid), Mathf.Sin(mid), 0f) * (w / 2 + t), Quaternion.Euler(0f, 0f, mid * Mathf.Rad2Deg));
                    b.AddBox(Vector3.zero, new Vector3(0.003f, w * 0.17f, 0.0022f), i % 2 == 0 ? Pal.Stone : Pal.StoneDark);
                    b.Pop();
                }
                b.AddBox(new Vector3(-w / 2 - t / 2, rect / 2, 0.0011f), new Vector3(t, rect, 0.0022f), Pal.Stone);
                b.AddBox(new Vector3(w / 2 + t / 2, rect / 2, 0.0011f), new Vector3(t, rect, 0.0022f), Pal.Stone);
            }
            else
            {
                b.AddBox(new Vector3(0f, h + t / 2, 0.0011f), new Vector3(w + 2 * t, t, 0.0022f), Pal.FrameWhite);
                b.AddBox(new Vector3(-w / 2 - t / 2, h / 2, 0.0011f), new Vector3(t, h, 0.0022f), Pal.FrameWhite);
                b.AddBox(new Vector3(w / 2 + t / 2, h / 2, 0.0011f), new Vector3(t, h, 0.0022f), Pal.FrameWhite);
                // Transom light above the door.
                b.AddFace(new Vector3(0f, h - 0.0035f, 0.0013f), Vector3.forward, Vector3.up, w * 0.36f, 0.0022f, Pal.Glow(Pal.WindowGlow));
            }
            b.AddBox(new Vector3(w * 0.32f, rect * 0.5f, 0.0016f), new Vector3(0.0016f, 0.0016f, 0.0012f), Pal.Brass);
            // Step and lamp.
            b.AddBox(new Vector3(0f, -0.003f, 0.005f), new Vector3(w * 1.5f, 0.006f, 0.009f), Pal.Stone);
            b.AddBox(new Vector3(w / 2 + 0.0055f, h * 0.78f, 0.0022f), new Vector3(0.0032f, 0.0042f, 0.0032f), Pal.Glow(Pal.Hex("#FFE2A0")));
            b.AddBox(new Vector3(w / 2 + 0.0055f, h * 0.78f + 0.0026f, 0.0022f), new Vector3(0.0042f, 0.0011f, 0.0042f), Pal.TrainBlack);
        }

        private static void GableRoof(MeshBuilder b, ref Rng rng, float w, float d, float wallH, float ridgeH, float overhang,
            Color roof, Color gable, int rows, bool gableTimber)
        {
            float slope = ridgeH / (d / 2);
            float halfSpan = d / 2 + overhang;
            float eaveY = wallH - overhang * slope;
            var ridge = new Vector3(0f, wallH + ridgeH, 0f);
            float roofLen = w + overhang * 2f;
            foreach (int side in new[] { -1, 1 })
            {
                var eave = new Vector3(0f, eaveY, side * halfSpan);
                var upSlope = (ridge - eave).normalized;
                var normal = Vector3.Cross(upSlope, Vector3.right * -side).normalized;
                if (normal.y < 0f) normal = -normal;
                float slopeLen = Vector3.Distance(ridge, eave);
                float step = slopeLen / rows;
                b.Push(eave, Quaternion.LookRotation(upSlope, normal));
                for (int k = 0; k < rows; k++)
                {
                    var rowColour = Pal.Shade(roof, 0.86f + 0.14f * (k / (float)rows));
                    int tiles = Mathf.Max(3, Mathf.RoundToInt(roofLen / 0.011f));
                    float tw = roofLen / tiles;
                    float offset = (k % 2) * tw * 0.5f;
                    for (int i = 0; i < tiles; i++)
                    {
                        float x = -roofLen / 2 + (i + 0.5f) * tw + offset;
                        float wTile = tw - 0.0005f;
                        if (x + wTile / 2 > roofLen / 2) { wTile = roofLen / 2 - (x - wTile / 2); x = roofLen / 2 - wTile / 2; }
                        if (wTile < 0.002f) continue;
                        var c = Pal.Jitter(rowColour, ref rng, 0.18f);
                        b.Push(new Vector3(x, 0.0016f + k * 0.00005f, (k + 0.55f) * step), Quaternion.Euler(-4f, 0f, 0f));
                        b.AddBox(Vector3.zero, new Vector3(wTile, 0.0022f, step * 1.18f), Pal.Shade(c, 0.85f), c, Pal.Shade(c, 0.55f), false);
                        b.Pop();
                    }
                }
                b.Pop();
            }
            // Ridge cap.
            b.Push(ridge + Vector3.up * 0.002f, Quaternion.Euler(0f, 0f, 90f));
            b.AddCylinder(new Vector3(0f, -roofLen / 2 - 0.001f, 0f), 0.0028f, 0.0028f, roofLen + 0.002f, 8, Pal.Shade(roof, 0.7f), Pal.Shade(roof, 0.7f), true, true);
            b.Pop();
            // Gable ends.
            var inside = new Vector3(0f, wallH + ridgeH * 0.3f, 0f);
            foreach (int s in new[] { -1, 1 })
            {
                float x = s * w / 2;
                b.AddTriangleOutward(new Vector3(x, wallH, -d / 2), new Vector3(x, wallH, d / 2), new Vector3(x, wallH + ridgeH, 0f), inside, gable);
                if (gableTimber)
                {
                    b.AddBox(new Vector3(x + s * 0.0012f, wallH + ridgeH * 0.42f, 0f), new Vector3(0.0024f, ridgeH * 0.84f, 0.003f), Pal.Timber);
                    b.AddBox(new Vector3(x + s * 0.0012f, wallH + ridgeH * 0.35f, 0f), new Vector3(0.0024f, 0.0026f, d * 0.6f), Pal.Timber);
                }
            }
        }

        private static void ThatchRoof(MeshBuilder b, ref Rng rng, float w, float d, float wallH, float roofH)
        {
            int start = b.VertexCount;
            float eave = wallH - 0.006f;
            int seed = rng.Range(0, 999);
            var straw = Pal.Thatch; var strawDark = Pal.Hex("#A87A33"); var strawLight = Pal.Hex("#EDC872"); var moss = Pal.Hex("#8E9A45");
            var radius = new Vector3(w / 2 + 0.014f, roofH, d / 2 + 0.017f);
            b.AddBlob(new Vector3(0f, eave, 0f), radius, 3, 0.05f, seed, (dir, pos) =>
            {
                // Bundled straw: streaks running down the slope, lighter combed tips at the eaves, moss near the top.
                float angle = Mathf.Atan2(dir.z, dir.x);
                float streak = Noise.Value2(angle * 70f, dir.y * 5f) * 0.7f + Noise.Value2(angle * 160f, dir.y * 12f) * 0.3f;
                var c = Color.Lerp(strawDark, straw, 0.2f + 0.8f * streak);
                float band = Mathf.Repeat(dir.y * 7f, 1f);
                c = Pal.Shade(c, 0.86f + 0.2f * Mathf.SmoothStep(0f, 0.35f, band));
                c = Color.Lerp(c, strawLight, Mathf.SmoothStep(0.3f, 0.08f, dir.y) * 0.5f);
                c = Color.Lerp(c, moss, Mathf.SmoothStep(0.8f, 1f, dir.y) * 0.5f * Noise.Value2(dir.x * 8f + 3f, dir.z * 8f));
                if (dir.y < 0.03f) c = Pal.Shade(strawDark, 0.5f);
                return c;
            }, 3.5f);
            // Flatten the lower half into the eave line so the walls show beneath the roof; pull the ridge up slightly
            // so the roof reads as thatch over a gable rather than a dome.
            for (int i = start; i < b.VertexCount; i++)
            {
                var v = b.Vertices[i];
                if (v.y < eave) { v.y = eave - 0.0015f; b.Normals[i] = Vector3.down; }
                else
                {
                    float t = (v.y - eave) / roofH;
                    float ridgeLift = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v.z) / radius.z), 2f) * t * 0.012f;
                    // Stepped courses of straw: each band flares out at its lower edge.
                    float band = Mathf.Repeat(t * 4.5f, 1f);
                    float flare = 1f + 0.05f * (1f - band) * (1f - t);
                    v.x *= flare; v.z *= flare;
                    v.y += ridgeLift;
                }
                b.Vertices[i] = v;
            }
            b.RecalculateNormals(start);
        }

        private static void Chimney(MeshBuilder b, ref Rng rng, Vector3 basePos, float height, Color colour)
        {
            const float s = 0.012f;
            b.AddBox(basePos + new Vector3(0f, height / 2, 0f), new Vector3(s, height, s), colour, Pal.Shade(colour, 0.6f), colour);
            for (float y = 0.004f; y < height - 0.004f; y += 0.004f)
                b.AddBox(basePos + new Vector3(0f, y, 0f), new Vector3(s + 0.0006f, 0.0006f, s + 0.0006f), Pal.Shade(colour, 0.8f));
            b.AddBox(basePos + new Vector3(0f, height + 0.0015f, 0f), new Vector3(s + 0.004f, 0.003f, s + 0.004f), Pal.StoneDark);
            b.AddCylinder(basePos + new Vector3(0f, height + 0.003f, 0f), 0.0028f, 0.0026f, 0.005f, 8, Pal.Brick, Pal.TrainBlack, true);
        }

        private static void Shrubs(MeshBuilder b, ref Rng rng, float w, float d, int count)
        {
            var palette = new[] { Pal.FlowerPink, Pal.FlowerYellow, Pal.FlowerWhite, Pal.FlowerLilac, Pal.FlowerRed };
            for (int i = 0; i < count; i++)
            {
                float side = rng.Chance(0.5f) ? -1f : 1f;
                var p = new Vector3(rng.Range(-w / 2, w / 2), 0.004f, -d / 2 - 0.007f);
                if (i % 2 == 1) p = new Vector3(side * (w / 2 + 0.007f), 0.004f, rng.Range(-d / 2, d / 2));
                if (Mathf.Abs(p.x) < 0.016f && p.z < 0f) p.x += 0.03f; // keep the front door clear
                float r = rng.Range(0.006f, 0.009f);
                int seed = rng.Range(0, 999);
                var leaf = Pal.Jitter(Pal.LeafMid, ref rng, 0.25f);
                var leafDark = Pal.Shade(leaf, 0.7f);
                b.AddBlob(p, new Vector3(r, r * 0.8f, r), 1, 0.25f, seed, (dir, _) => Color.Lerp(leafDark, leaf, dir.y * 0.5f + 0.5f));
                var flower = palette[rng.Range(0, palette.Length)];
                for (int k = 0; k < 4; k++)
                {
                    var dir = new Vector3(rng.Range(-1f, 1f), rng.Range(0.3f, 1f), rng.Range(-1f, 1f)).normalized;
                    var c = Pal.Jitter(flower, ref rng, 0.15f);
                    b.AddBlob(p + dir * r * 0.95f, Vector3.one * 0.0016f, 0, 0.1f, k, (_, __) => c);
                }
            }
        }
    }
}
