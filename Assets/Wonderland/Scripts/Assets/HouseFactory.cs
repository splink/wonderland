using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Three storybook houses, each in four colour styles. Built from parts with exaggerated proportions: swayback tiled
    /// roofs with deep eaves, scalloped bargeboards, finials and ridge crests; crooked chimneys with pots; framed
    /// glowing windows with curtains, heart shutters and trailing flower boxes; doors with canopies and lanterns;
    /// climbing roses, stepping stones and shrubs. A final hand-made warp gives every house a gentle lean, twist and
    /// sagging ridge. Pivot at the ground centre; the stone plinth reaches 4 cm below ground for slopes. Front faces -Z.
    /// </summary>
    public static partial class HouseFactory
    {
        public static int VariantCount => Catalog.Count;
        public const int Station = 3;
        public const int StyleCount = 4;
        public static string Name(int variant) => Catalog.Get(variant).Name;
        private static readonly System.Collections.Generic.Dictionary<int, Mesh> Cache = new();

        public static Mesh Get(int variant, int style = 0) => Get(variant, style, 0);

        /// <summary>A building at a level of detail (0 full; 1 and 2 simpler for further away - see <see cref="MeshBuilder.Detail"/>).</summary>
        public static Mesh Get(int variant, int style, int detail)
        {
            variant = Mathf.Clamp(variant, 0, Catalog.Count - 1);
            style = Mathf.Abs(style) % Mathf.Max(1, Catalog.Get(variant).Styles);
            int key = (variant * 16 + style) * 8 + detail + 1;
            if (!Cache.TryGetValue(key, out var mesh))
            {
                int was = MeshBuilder.DefaultDetail;
                MeshBuilder.DefaultDetail = detail;
                try { Cache[key] = mesh = Build(variant, style); }
                finally { MeshBuilder.DefaultDetail = was; }
                if (detail != 0) mesh.name += " detail " + detail;
            }
            return mesh;
        }

        /// <summary>The little model on an object's library tile (its Low level: tiles are a few centimetres across).</summary>
        public static Mesh Preview(int variant) => Get(variant, 0, MeshBuilder.Low);

        /// <summary>
        /// Builds every library tile's model ahead of time, a little each frame (at most <paramref name="budgetMs"/> a
        /// frame), so the tiles show their models the moment a family is opened.
        /// </summary>
        public static System.Collections.IEnumerator WarmPreviews(float budgetMs = 6f)
        {
            var clock = new System.Diagnostics.Stopwatch();
            for (int id = 0; id < Catalog.Count; id++)
            {
                clock.Restart();
                Preview(id);
                if (clock.Elapsed.TotalMilliseconds > budgetMs * 0.5f) yield return null;
                else if (id % 4 == 3) yield return null;
            }
        }

        /// <summary>Half extents of the footprint including eaves (x, z).</summary>
        public static Vector2 HalfExtents(int variant) => Catalog.Get(variant).Half;

        private sealed class Style
        {
            public Color Plaster, Roof, Timber, Shutter, Door, Trim, Curtain, Flower;
            public float Lean, Twist, Sag;
            public bool Roses;
        }

        private static Style MakeStyle(int variant, int style, ref Rng rng)
        {
            if (variant > 3) variant = variant % 3;
            var plasters = new[] { Pal.Plaster, Pal.Hex("#F5DC98"), Pal.PlasterPink, Pal.Hex("#DCEAD2") };
            var roofs = variant switch
            {
                0 => new[] { Pal.RoofTerracotta, Pal.Hex("#6E8C45"), Pal.Hex("#C9743A"), Pal.Hex("#7D4B63") },
                1 => new[] { Pal.RoofSlate, Pal.Hex("#8C4A3C"), Pal.Hex("#4E6E78"), Pal.Hex("#6A5A86") },
                _ => new[] { Pal.Thatch, Pal.Hex("#C99A48"), Pal.Hex("#DDB25E"), Pal.Hex("#B98A3E") },
            };
            var shutters = new[] { Pal.ShutterBlue, Pal.ShutterGreen, Pal.Hex("#B8483E"), Pal.Hex("#4A9A9A") };
            var doors = new[] { Pal.DoorGreen, Pal.DoorRed, Pal.DoorBlue, Pal.Hex("#D9A532") };
            var curtains = new[] { Pal.Hex("#E25B4F"), Pal.Hex("#F7F1E6"), Pal.Hex("#7FB0E0"), Pal.Hex("#F2A7C3") };
            var flowers = new[] { Pal.FlowerRed, Pal.FlowerPink, Pal.FlowerYellow, Pal.FlowerLilac };
            int o = variant;   // offset so each type's default style differs from the others
            return new Style
            {
                Plaster = plasters[(style + o) % 4], Roof = roofs[style], Timber = Pal.Timber,
                Shutter = shutters[(style + o * 2) % 4], Door = doors[(style + o) % 4], Trim = style == 3 ? Pal.Hex("#F7F1E6") : Pal.Hex("#FBF5EA"),
                Curtain = curtains[(style + o) % 4], Flower = flowers[(style + o * 3) % 4],
                Lean = rng.Range(-0.12f, 0.12f), Twist = rng.Range(-3f, 3f), Sag = rng.Range(0.0015f, 0.003f), Roses = (style + variant) % 2 == 0,
            };
        }

        public static Mesh Build(int variant, int style = 0)
        {
            var b = new MeshBuilder();
            var rng = new Rng(500 + variant * 31 + style * 7);
            var s = MakeStyle(variant, style, ref rng);
            float height;
            switch (variant)
            {
                case 0: height = Cottage(b, ref rng, s); break;
                case 1: height = Townhouse(b, ref rng, s); break;
                case 2: height = Thatched(b, ref rng, s); break;
                case 3: height = RailwayStation(b, ref rng, s); break;
                default:
                    height = Place(b, ref rng, s, variant, style, out bool warp);
                    if (!warp) { Signage(b, variant); return b.ToMesh($"Place_{Catalog.Get(variant).Name}_{style}"); }
                    s.Lean *= 0.6f; s.Twist *= 0.6f;
                    break;
            }
            // Hand-made irregularity: a gentle curving lean, a twist that grows with height, a sagging ridge.
            float lean = s.Lean, twist = s.Twist * Mathf.Deg2Rad, sag = s.Sag;
            b.Warp(0, p =>
            {
                if (p.y <= 0.004f) return p;
                float t = Mathf.Clamp01(p.y / height);
                float a = twist * t;
                var q = new Vector3(p.x * Mathf.Cos(a) - p.z * Mathf.Sin(a), p.y, p.x * Mathf.Sin(a) + p.z * Mathf.Cos(a));
                q.x += lean * p.y * t * 0.5f;
                q.y -= sag * Mathf.SmoothStep(0.55f, 1f, t) * (1f - Mathf.Clamp01(Mathf.Abs(p.x) / 0.07f));
                return q;
            });
            Signage(b, variant);   // (after the irregularity: lettering stays straight)
            return b.ToMesh($"House_{Name(variant)}_{style}");
        }

        // ---------------------------------------------------------------- houses

        private static float Cottage(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.1f, D = 0.078f, H = 0.058f, R = 0.062f;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, s.Plaster);
            TimberFrame(b, W, D, H, s.Timber);
            Wall(b, new Vector3(-0.022f, 0.006f, -D / 2), Vector3.back); Door(b, ref rng, 0.019f, 0.033f, s, false, false); b.Pop();
            Wall(b, new Vector3(0.022f, 0.032f, -D / 2), Vector3.back); Window(b, ref rng, 0.018f, 0.019f, s, true, true, false); b.Pop();
            Wall(b, new Vector3(-0.02f, 0.032f, D / 2), Vector3.forward); Window(b, ref rng, 0.018f, 0.019f, s, true, false, false); b.Pop();
            Wall(b, new Vector3(0.024f, 0.032f, D / 2), Vector3.forward); Window(b, ref rng, 0.015f, 0.017f, s, false, true, false); b.Pop();
            Wall(b, new Vector3(W / 2, 0.03f, 0f), Vector3.right); Window(b, ref rng, 0.016f, 0.018f, s, true, true, true); b.Pop();
            Wall(b, new Vector3(-W / 2, 0.03f, 0f), Vector3.left); Window(b, ref rng, 0.016f, 0.018f, s, true, false, false); b.Pop();
            Wall(b, new Vector3(W / 2, H + 0.02f, 0f), Vector3.right); RoundWindow(b, 0.0065f, s.Timber, s); b.Pop();
            Wall(b, new Vector3(-W / 2, H + 0.02f, 0f), Vector3.left); RoundWindow(b, 0.0065f, s.Timber, s); b.Pop();
            StoryRoof(b, ref rng, W, D, H, R, 0.012f, 0.004f, s.Roof, s.Plaster, s.Trim, 7, true);
            // A dormer peeking out of the front slope.
            Dormer(b, ref rng, new Vector3(0.018f, H + 0.012f, -D / 2 + 0.014f), 0.022f, s);
            Chimney(b, ref rng, new Vector3(-0.03f, H, 0.014f), R + 0.022f, Pal.Brick, 2);
            if (s.Roses) Roses(b, ref rng, new Vector3(W / 2, 0f, -D / 2), H * 0.85f, s.Flower);
            SteppingStones(b, ref rng, new Vector3(-0.022f, 0f, -D / 2 - 0.012f));
            Shrubs(b, ref rng, W, D, 3, s.Flower);
            return H + R;
        }

        private static float Townhouse(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.08f, D = 0.072f, H1 = 0.05f, H = 0.104f, R = 0.064f, J = 0.004f;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H1, s.Plaster);
            // Jettied upper storey on carved brackets: the classic storybook overhang.
            float uw = W + 2 * J, ud = D + 2 * J;
            Walls(b, uw, ud, H1, H, Pal.Shade(s.Plaster, 1.03f));
            b.AddBox(new Vector3(0f, H1 + 0.0015f, 0f), new Vector3(uw + 0.002f, 0.003f, ud + 0.002f), s.Timber);
            for (int i = -2; i <= 2; i++)
            foreach (int side in new[] { -1, 1 })
            {
                b.Push(new Vector3(i * W / 5f, H1 - 0.003f, side * (D / 2 + J * 0.5f)), Quaternion.Euler(side * 35f, 0f, 0f));
                b.AddBox(Vector3.zero, new Vector3(0.0025f, 0.006f, 0.0025f), s.Timber);
                b.Pop();
            }
            // Corner quoins on the ground floor.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            for (int k = 0; k < 4; k++)
            {
                float y = 0.008f + k * 0.011f;
                var size = k % 2 == 0 ? new Vector3(0.009f, 0.007f, 0.005f) : new Vector3(0.005f, 0.007f, 0.009f);
                b.AddBox(new Vector3(sx * (W / 2 - size.x / 2 + 0.001f), y, sz * (D / 2 - size.z / 2 + 0.001f)), size, Pal.Jitter(Pal.Stone, ref rng, 0.2f));
            }
            Wall(b, new Vector3(0f, 0.006f, -D / 2), Vector3.back); Door(b, ref rng, 0.018f, 0.035f, s, false, true); b.Pop();
            foreach (float x in new[] { -0.024f, 0.024f }) { Wall(b, new Vector3(x, 0.027f, -D / 2), Vector3.back); Window(b, ref rng, 0.014f, 0.02f, s, true, true, true); b.Pop(); }
            foreach (float x in new[] { -0.026f, 0f, 0.026f }) { Wall(b, new Vector3(x, 0.078f, -ud / 2), Vector3.back); Window(b, ref rng, 0.014f, 0.02f, s, false, true, false); b.Pop(); }
            foreach (float x in new[] { -0.02f, 0.02f })
            {
                Wall(b, new Vector3(x, 0.027f, D / 2), Vector3.forward); Window(b, ref rng, 0.014f, 0.02f, s, true, false, false); b.Pop();
                Wall(b, new Vector3(x, 0.078f, ud / 2), Vector3.forward); Window(b, ref rng, 0.014f, 0.02f, s, false, true, false); b.Pop();
            }
            foreach (int side in new[] { -1, 1 })
            {
                var n = new Vector3(side, 0f, 0f);
                Wall(b, new Vector3(side * W / 2, 0.027f, 0f), n); Window(b, ref rng, 0.013f, 0.019f, s, true, false, false); b.Pop();
                Wall(b, new Vector3(side * uw / 2, 0.078f, 0f), n); Window(b, ref rng, 0.013f, 0.019f, s, false, true, true); b.Pop();
            }
            // Hanging shop sign by the door.
            b.AddBox(new Vector3(0.02f, 0.044f, -D / 2 - 0.006f), new Vector3(0.0012f, 0.0012f, 0.012f), Pal.TrainBlack);
            b.AddBox(new Vector3(0.02f, 0.037f, -D / 2 - 0.009f), new Vector3(0.0014f, 0.01f, 0.012f), s.Shutter, s.Trim, s.Shutter);
            b.AddBlob(new Vector3(0.0198f, 0.037f, -D / 2 - 0.009f), new Vector3(0.0012f, 0.003f, 0.0035f), 1, 0f, 3, (_, __) => Pal.Hex("#E2B04A"));
            StoryRoof(b, ref rng, uw, ud, H, R, 0.009f, 0.0035f, s.Roof, s.Plaster, s.Trim, 8, false);
            Dormer(b, ref rng, new Vector3(0f, H + 0.01f, -ud / 2 + 0.014f), 0.024f, s);
            Chimney(b, ref rng, new Vector3(-0.024f, H, 0.012f), R + 0.016f, Pal.StoneDark, 2);
            Chimney(b, ref rng, new Vector3(0.026f, H, 0.012f), R + 0.01f, Pal.StoneDark, 1);
            WeatherVane(b, new Vector3(0.006f, H + R + 0.003f, 0f));
            if (s.Roses) Roses(b, ref rng, new Vector3(-W / 2, 0f, -D / 2), H1 * 0.9f, s.Flower);
            SteppingStones(b, ref rng, new Vector3(0f, 0f, -D / 2 - 0.012f));
            Shrubs(b, ref rng, W, D, 2, s.Flower);
            return H + R;
        }

        private static float Thatched(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.112f, D = 0.074f, H = 0.044f, R = 0.064f;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, s.Plaster);
            Wall(b, new Vector3(0f, 0.006f, -D / 2), Vector3.back); Door(b, ref rng, 0.02f, 0.03f, s, true, false); b.Pop();
            foreach (float x in new[] { -0.034f, 0.034f }) { Wall(b, new Vector3(x, 0.025f, -D / 2), Vector3.back); RoundWindow(b, 0.0085f, s.Timber, s); FlowerBox(b, ref rng, 0.02f, -0.0125f, s.Flower); b.Pop(); }
            foreach (float x in new[] { -0.03f, 0.03f }) { Wall(b, new Vector3(x, 0.025f, D / 2), Vector3.forward); RoundWindow(b, 0.008f, s.Timber, s); b.Pop(); }
            Wall(b, new Vector3(-W / 2, 0.025f, 0f), Vector3.left); Window(b, ref rng, 0.014f, 0.016f, s, true, true, true); b.Pop();
            ThatchRoof(b, ref rng, W, D, H, R, s.Roof);
            Chimney(b, ref rng, new Vector3(W / 2 - 0.014f, H - 0.01f, 0.004f), R + 0.026f, Pal.Brick, 1);
            if (s.Roses) Roses(b, ref rng, new Vector3(-W / 2, 0f, -D / 2), H * 0.95f, s.Flower);
            SteppingStones(b, ref rng, new Vector3(0f, 0f, -D / 2 - 0.012f));
            Shrubs(b, ref rng, W, D, 5, s.Flower);
            return H + R;
        }

        /// <summary>
        /// The railway station building: a little booking hall with a clock tower, a name board, and a deep scalloped
        /// canopy reaching over the platform (front, -Z) with benches, a lamp and flower tubs beneath. The platform itself
        /// is generated along the rails (<see cref="Platform"/>).
        /// </summary>
        private static float StationHouse(MeshBuilder b, ref Rng rng, Style s)
        {
            const float W = 0.1f, D = 0.05f, H = 0.052f, R = 0.036f;
            Foundation(b, ref rng, W, D);
            Walls(b, W, D, 0f, H, s.Plaster);
            // Brick plinth band and corner pilasters.
            b.AddBox(new Vector3(0f, 0.008f, 0f), new Vector3(W + 0.002f, 0.012f, D + 0.002f), Pal.Brick, Pal.Shade(Pal.Brick, 1.1f), Pal.Brick);
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                b.AddBox(new Vector3(sx * (W / 2 - 0.002f), H / 2, sz * (D / 2 - 0.002f)), new Vector3(0.0055f, H, 0.0055f), Pal.Brick);
            Wall(b, new Vector3(0f, 0.006f, -D / 2), Vector3.back); Door(b, ref rng, 0.018f, 0.032f, s, true, false); b.Pop();
            foreach (float x in new[] { -0.03f, 0.03f }) { Wall(b, new Vector3(x, 0.028f, -D / 2), Vector3.back); Window(b, ref rng, 0.014f, 0.022f, s, false, false, true); b.Pop(); }
            foreach (float x in new[] { -0.025f, 0.025f }) { Wall(b, new Vector3(x, 0.028f, D / 2), Vector3.forward); Window(b, ref rng, 0.014f, 0.02f, s, true, true, false); b.Pop(); }
            StoryRoof(b, ref rng, W, D, H, R, 0.006f, 0.0025f, s.Roof, s.Plaster, s.Trim, 5, false);
            // Clock tower over the middle with a lantern cupola.
            float ty = H + R * 0.6f;
            b.AddBox(new Vector3(0f, ty + 0.012f, 0.004f), new Vector3(0.018f, 0.026f, 0.018f), s.Plaster);
            b.AddBox(new Vector3(0f, ty + 0.0255f, 0.004f), new Vector3(0.021f, 0.0025f, 0.021f), s.Trim);
            Wall(b, new Vector3(0f, ty + 0.014f, 0.004f - 0.009f), Vector3.back);
            b.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0068f, 0.0068f, 0.0012f, 16, Pal.Brass, Pal.Brass, false);
            b.AddDisc(new Vector3(0f, 0.0013f, 0f), 0.0058f, 16, Pal.Glow(Pal.Hex("#FFF6E0")), true);
            b.Pop();
            b.AddBox(new Vector3(0f, 0.0022f, 0.0018f), new Vector3(0.0008f, 0.0045f, 0.0005f), Pal.TrainBlack);
            b.Push(new Vector3(0.0014f, -0.0008f, 0.0018f), Quaternion.Euler(0f, 0f, 60f));
            b.AddBox(Vector3.zero, new Vector3(0.0008f, 0.0032f, 0.0005f), Pal.TrainBlack);
            b.Pop();
            b.Pop();
            b.AddCylinder(new Vector3(0f, ty + 0.027f, 0.004f), 0.0125f, 0f, 0.016f, 4, Pal.Shade(s.Roof, 0.85f), s.Roof, false, true, false, Mathf.PI / 4f);
            b.AddCylinder(new Vector3(0f, ty + 0.043f, 0.004f), 0.0006f, 0.0005f, 0.008f, 4, Pal.TrainBlack, Pal.TrainBlack, true);
            b.AddBlob(new Vector3(0f, ty + 0.0515f, 0.004f), Vector3.one * 0.0016f, 1, 0f, 9, (_, __) => Pal.Brass);
            // Name board above the door.
            b.AddBox(new Vector3(0f, H - 0.008f, -D / 2 - 0.0015f), new Vector3(0.05f, 0.008f, 0.0016f), s.Shutter, s.Shutter, s.Shutter);
            for (int i = 0; i < 9; i++)
                b.AddBox(new Vector3(-0.02f + i * 0.005f, H - 0.008f, -D / 2 - 0.0026f), new Vector3(0.0028f, 0.0035f, 0.0004f), s.Trim);
            // Canopy over the platform: posts, a gently sloped roof and a scalloped valance.
            const float cd = 0.042f;   // reaches 4.2 cm towards the track
            float cy = H - 0.002f;
            for (int i = -2; i <= 2; i++)
            {
                float x = i * W / 4.2f;
                b.AddCylinder(new Vector3(x, 0.004f, -D / 2 - cd + 0.004f), 0.0012f, 0.001f, cy - 0.006f, 6, Pal.Shade(s.Shutter, 0.8f), s.Shutter, false);
                b.Push(new Vector3(x, cy - 0.006f, -D / 2 - cd + 0.008f), Quaternion.Euler(45f, 0f, 0f));
                b.AddBox(Vector3.zero, new Vector3(0.001f, 0.001f, 0.009f), s.Shutter);
                b.Pop();
            }
            b.Push(new Vector3(0f, cy, -D / 2 - cd / 2), Quaternion.Euler(-8f, 0f, 0f));
            b.AddBox(Vector3.zero, new Vector3(W + 0.01f, 0.0022f, cd + 0.006f), Pal.Shade(s.Roof, 0.8f), s.Roof, Pal.Shade(s.Roof, 0.55f), false);
            b.Pop();
            for (float x = -W / 2 - 0.004f; x <= W / 2 + 0.004f; x += 0.0045f)
                b.AddBlob(new Vector3(x, cy - 0.006f, -D / 2 - cd - 0.0035f), new Vector3(0.0022f, 0.0024f, 0.0006f), 0, 0f, (int)(x * 9999f), (_, __) => s.Trim);
            // Benches, a lamp and flower tubs under the canopy.
            foreach (float x in new[] { -0.03f, 0.03f })
            {
                var bench = Pal.Hex("#7A4E30");
                b.AddBox(new Vector3(x, 0.006f, -D / 2 - 0.008f), new Vector3(0.016f, 0.0012f, 0.005f), bench);
                b.AddBox(new Vector3(x, 0.0095f, -D / 2 - 0.0058f), new Vector3(0.016f, 0.004f, 0.0009f), bench);
                foreach (int e in new[] { -1, 1 }) b.AddBox(new Vector3(x + e * 0.007f, 0.0032f, -D / 2 - 0.008f), new Vector3(0.0012f, 0.0055f, 0.0045f), Pal.TrainBlack);
            }
            b.AddCylinder(new Vector3(0.048f, 0.004f, -D / 2 - 0.016f), 0.0008f, 0.0006f, 0.034f, 6, Pal.TrainBlack, Pal.TrainBlack, false);
            b.AddBox(new Vector3(0.048f, 0.04f, -D / 2 - 0.016f), new Vector3(0.0042f, 0.006f, 0.0042f), Pal.Glow(Pal.Hex("#FFE7A8")));
            b.AddCylinder(new Vector3(0.048f, 0.043f, -D / 2 - 0.016f), 0.0036f, 0f, 0.003f, 4, Pal.TrainBlack, Pal.TrainBlack, false, true, false, Mathf.PI / 4f);
            foreach (float x in new[] { -0.047f, 0.0f })
            {
                b.AddCylinder(new Vector3(x, 0.004f, -D / 2 - 0.02f), 0.0035f, 0.0042f, 0.005f, 8, Pal.Hex("#B5603E"), Pal.Hex("#C8754B"), true);
                Flora.Paint leaves = (d, _) => Color.Lerp(Pal.Shade(Pal.LeafMid, 0.7f), Pal.LeafMid, d.y * 0.5f + 0.5f);
                Flora.LeafCluster(b, new Vector3(x, 0.0115f, -D / 2 - 0.02f), 0.004f, Vector3.up, 2, ref rng, leaves);
                for (int k = 0; k < 4; k++)
                    b.AddBlob(new Vector3(x + rng.Range(-0.003f, 0.003f), 0.0145f, -D / 2 - 0.02f + rng.Range(-0.003f, 0.003f)), Vector3.one * 0.0013f, 0, 0f, k, (_, __) => s.Flower);
            }
            Chimney(b, ref rng, new Vector3(-0.034f, H, 0.01f), R + 0.012f, Pal.Brick, 1);
            return H + R + 0.05f;
        }

        /// <summary>
        /// A platform along the railway between arc lengths <paramref name="s0"/> and <paramref name="s1"/> on the given
        /// side: stone face down into the bed, a light paved top at coach-floor height with a pale safety edge, and sloped
        /// ends. Follows curves and grades because it is generated from the layout itself.
        /// </summary>
        public static void Platform(MeshBuilder b, TrackLayout layout, float s0, float s1, int side)
        {
            var path = new System.Collections.Generic.List<Vector3>(); var right = new System.Collections.Generic.List<Vector3>(); var up = new System.Collections.Generic.List<Vector3>();
            for (float s = s0; s <= s1 + 1e-4f; s += 0.01f)
            {
                var p = layout.Evaluate(s, out var t);
                t.y = 0f; t.Normalize();
                var r = Vector3.Cross(Vector3.up, t) * side;
                path.Add(p); right.Add(r); up.Add(Vector3.up);
            }
            if (path.Count < 2) return;
            // Profile in (outwards from the track, up), clockwise seen along the path when side = +1; mirror for -1.
            const float edge = 0.032f, back = 0.08f, top = 0.0135f, foot = -0.04f;   // deep foot: reaches the ground where it falls away
            var profile = new[] { new Vector2(edge, foot), new Vector2(edge, top - 0.0018f), new Vector2(edge + 0.0012f, top), new Vector2(back, top), new Vector2(back, foot) };
            var colours = new[] { Pal.StoneDark, Pal.Stone, Pal.Hex("#F2E6C8"), Pal.Hex("#C9BBA3"), Pal.StoneDark };
            if (side < 0)
            {
                System.Array.Reverse(profile); System.Array.Reverse(colours);
                for (int i = 0; i < profile.Length; i++) profile[i].x = -profile[i].x;
                for (int i = 0; i < right.Count; i++) right[i] = -right[i];
            }
            b.AddExtrusion(path, right, up, profile, colours, false);
            // Paving joints and the pale edge stripe.
            for (int i = 0; i < path.Count; i += 2)
            {
                b.Push(path[i] + right[i] * (side < 0 ? -1f : 1f) * 0.056f + Vector3.up * (top + 0.0001f), Quaternion.LookRotation(Vector3.Cross(right[i], Vector3.up) * (side < 0 ? -1f : 1f), Vector3.up));
                b.AddFace(Vector3.zero, Vector3.up, Vector3.forward, 0.0225f, 0.0003f, Pal.Hex("#B8AA92"));
                b.Pop();
            }
            // Sloped ends.
            foreach (bool start in new[] { true, false })
            {
                int i = start ? 0 : path.Count - 1;
                var t = start ? path[0] - path[1] : path[^1] - path[^2]; t.y = 0f; t.Normalize();
                var r = right[i] * (side < 0 ? -1f : 1f);
                var c = path[i] + r * (edge + back) * 0.5f;
                b.Push(c + t * 0.008f + Vector3.up * (top * 0.5f - 0.003f), Quaternion.LookRotation(t, Vector3.up) * Quaternion.Euler(28f, 0f, 0f));
                b.AddBox(Vector3.zero, new Vector3(back - edge, 0.004f, 0.022f), Pal.StoneDark, Pal.Hex("#C9BBA3"), Pal.StoneDark, false);
                b.Pop();
            }
        }

        // ---------------------------------------------------------------- parts

        private static void Wall(MeshBuilder b, Vector3 position, Vector3 normal) => b.Push(position, Quaternion.LookRotation(normal, Vector3.up));

        private static void Foundation(MeshBuilder b, ref Rng rng, float w, float d)
        {
            b.AddBox(new Vector3(0f, -0.017f, 0f), new Vector3(w + 0.008f, 0.046f, d + 0.008f), Pal.StoneDark, Pal.Stone, Pal.StoneDark);
            if (b.Detail > 0) { b.AddBox(new Vector3(0f, 0.0045f, 0f), new Vector3(w + 0.0095f, 0.004f, d + 0.0095f), Pal.StoneDark, Pal.Hex("#C9BBA3"), Pal.StoneDark); return; }   // (further away: one coping, no stones)
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

        private static void Walls(MeshBuilder b, float w, float d, float y0, float y1, Color plaster)
        {
            var bottom = Pal.Shade(plaster, 0.88f);
            for (int i = 0; i < 4; i++)
            {
                var n = i switch { 0 => Vector3.back, 1 => Vector3.forward, 2 => Vector3.left, _ => Vector3.right };
                float half = i < 2 ? w / 2 : d / 2;
                var c = (i < 2 ? d / 2 : w / 2) * n;
                var right = Vector3.Cross(n, Vector3.up);
                int v = b.AddVertex(c - right * half + Vector3.up * (y0 + 0.004f), n, bottom);
                b.AddVertex(c - right * half + Vector3.up * y1, n, plaster);
                b.AddVertex(c + right * half + Vector3.up * y1, n, plaster);
                b.AddVertex(c + right * half + Vector3.up * (y0 + 0.004f), n, bottom);
                b.AddTriangle(v, v + 1, v + 2); b.AddTriangle(v, v + 2, v + 3);
            }
            b.AddFace(new Vector3(0f, y1, 0f), Vector3.up, Vector3.forward, w / 2, d / 2, plaster);
        }

        private static void TimberFrame(MeshBuilder b, float w, float d, float h, Color t)
        {
            const float s = 0.0032f;
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
            // Crossed braces in the upper band and a few studs, slightly irregular like old oak framing.
            float y0 = h * 0.55f, y1 = h - 0.0015f;
            foreach (float z in new[] { -d / 2 - 0.0005f, d / 2 + 0.0005f })
            foreach (int s2 in new[] { -1, 1 })
            {
                float len = Mathf.Sqrt(0.018f * 0.018f + (y1 - y0) * (y1 - y0)), angle = Mathf.Atan2(y1 - y0, 0.018f) * Mathf.Rad2Deg;
                b.Push(new Vector3(s2 * (w / 2 - 0.012f), (y0 + y1) / 2, z), Quaternion.Euler(0f, 0f, s2 * angle));
                b.AddBox(Vector3.zero, new Vector3(len, 0.0024f, 0.0022f), t);
                b.Pop();
                b.AddBox(new Vector3(s2 * 0.004f, (y0 + y1) / 2, z), new Vector3(0.0022f, y1 - y0, 0.0022f), t);
            }
        }

        private static void Window(MeshBuilder b, ref Rng rng, float w, float h, Style s, bool shutters, bool flowers, bool arched)
        {
            const float t = 0.0016f, d = 0.0022f;
            var frame = s.Trim;
            b.AddFace(new Vector3(0f, 0f, 0.0004f), Vector3.forward, Vector3.up, w / 2, h / 2, Pal.Glow(Pal.WindowGlow));
            // Curtains drawn to the sides, softly lit from inside.
            var curtain = s.Curtain; curtain.a = 0.55f;
            foreach (int side in new[] { -1, 1 })
            {
                b.AddFace(new Vector3(side * w * 0.36f, h * 0.05f, 0.0006f), Vector3.forward, Vector3.up, w * 0.14f, h * 0.45f, curtain);
                b.AddBox(new Vector3(side * w * 0.3f, -h * 0.05f, 0.0007f), new Vector3(w * 0.06f, 0.0007f, 0.0004f), Pal.Shade(s.Curtain, 0.7f));
            }
            b.AddBox(new Vector3(0f, h / 2 + t / 2, d / 2), new Vector3(w + 2 * t, t, d), frame);
            b.AddBox(new Vector3(0f, -h / 2 - t * 0.6f, d * 0.8f), new Vector3(w + 3.2f * t, t * 1.2f, d * 1.6f), frame);
            b.AddBox(new Vector3(-w / 2 - t / 2, 0f, d / 2), new Vector3(t, h, d), frame);
            b.AddBox(new Vector3(w / 2 + t / 2, 0f, d / 2), new Vector3(t, h, d), frame);
            b.AddBox(new Vector3(0f, 0f, d * 0.35f), new Vector3(t * 0.55f, h, d * 0.7f), frame);
            b.AddBox(new Vector3(0f, h * 0.1f, d * 0.35f), new Vector3(w, t * 0.55f, d * 0.7f), frame);
            if (arched)
            {
                // Arched head: a glowing half-moon light with a stone surround.
                var c = new Vector3(0f, h / 2 + t, 0.0004f);
                for (int i = 0; i < 8; i++)
                {
                    float a0 = i / 8f * Mathf.PI, a1 = (i + 1) / 8f * Mathf.PI;
                    b.AddTriangleOutward(c, c + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * (w / 2), c + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * (w / 2), c - Vector3.forward * 0.01f, Pal.Glow(Pal.WindowGlow));
                    float mid = (a0 + a1) / 2;
                    b.Push(c + new Vector3(Mathf.Cos(mid), Mathf.Sin(mid), 0f) * (w / 2 + t * 0.6f) + Vector3.forward * 0.0008f, Quaternion.Euler(0f, 0f, mid * Mathf.Rad2Deg));
                    b.AddBox(Vector3.zero, new Vector3(t * 1.3f, w * 0.21f, d), i % 2 == 0 ? frame : Pal.Shade(frame, 0.9f));
                    b.Pop();
                }
            }
            if (shutters)
            {
                float sw = w * 0.46f;
                foreach (int side in new[] { -1, 1 })
                {
                    float x = side * (w / 2 + t + sw / 2 + 0.0003f);
                    b.AddBox(new Vector3(x, 0f, 0.0007f), new Vector3(sw, h + t, 0.0014f), s.Shutter);
                    var slat = Pal.Shade(s.Shutter, 0.78f);
                    for (int k = -2; k <= 2; k++)
                        if (k != 1) b.AddBox(new Vector3(x, k * h * 0.17f, 0.0015f), new Vector3(sw * 0.8f, 0.0006f, 0.0004f), slat);
                    // A heart cut into each shutter.
                    b.Push(new Vector3(x, h * 0.17f, 0.0016f), Quaternion.Euler(0f, 0f, 45f));
                    b.AddBox(Vector3.zero, new Vector3(sw * 0.3f, sw * 0.3f, 0.0004f), Pal.Shade(s.Shutter, 0.45f));
                    b.Pop();
                    foreach (int lobe in new[] { -1, 1 })
                        b.AddBlob(new Vector3(x + lobe * sw * 0.1f, h * 0.17f + sw * 0.11f, 0.0016f), new Vector3(sw * 0.13f, sw * 0.13f, 0.0003f), 0, 0f, lobe + 5, (_, __) => Pal.Shade(s.Shutter, 0.45f));
                }
            }
            if (flowers) FlowerBox(b, ref rng, w + 2 * t, -h / 2 - t * 1.4f - 0.0024f, s.Flower);
        }

        /// <summary>A planter of leaves and blooms with trailing ivy.</summary>
        private static void FlowerBox(MeshBuilder b, ref Rng rng, float w, float y, Color main)
        {
            b.AddBox(new Vector3(0f, y, 0.0035f), new Vector3(w, 0.004f, 0.0042f), Pal.Timber);
            if (b.Detail > 0)
            {
                // Further away: a band of leaves with a band of blooms on top.
                b.AddBox(new Vector3(0f, y + 0.0028f, 0.0035f), new Vector3(w * 0.96f, 0.0028f, 0.0042f), Pal.LeafMid);
                b.AddBox(new Vector3(0f, y + 0.0046f, 0.0040f), new Vector3(w * 0.85f, 0.0016f, 0.0026f), main);
                return;
            }
            var palette = new[] { main, main, Pal.FlowerWhite, Pal.FlowerYellow, Pal.FlowerPink };
            int count = Mathf.RoundToInt(w / 0.0026f);
            for (int i = 0; i < count; i++)
            {
                float x = -w / 2 + (i + 0.5f) * (w / count);
                var leaf = Pal.Jitter(Pal.LeafMid, ref rng, 0.3f);
                b.AddBlob(new Vector3(x, y + 0.0028f, 0.0035f), new Vector3(0.0019f, 0.0017f, 0.0019f), 0, 0.15f, i, (_, __) => leaf);
                if (rng.Chance(0.8f))
                {
                    var c = Pal.Jitter(palette[rng.Range(0, palette.Length)], ref rng, 0.2f);
                    b.AddBlob(new Vector3(x + rng.Range(-0.0006f, 0.0006f), y + 0.0047f, 0.0043f), Vector3.one * 0.0013f, 0, 0.1f, i + 50, (_, __) => c);
                }
                if (rng.Chance(0.35f))
                {
                    // Trailing ivy hanging over the edge.
                    var ivy = Pal.Jitter(Pal.LeafDeep, ref rng, 0.2f);
                    for (int k = 0; k < rng.Range(2, 4); k++)
                        b.AddBlob(new Vector3(x, y - 0.002f - k * 0.0026f, 0.0058f), Vector3.one * 0.0014f, 0, 0.2f, i * 3 + k, (_, __) => ivy);
                }
            }
        }

        private static void RoundWindow(MeshBuilder b, float r, Color frame, Style s)
        {
            b.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            b.AddCylinder(Vector3.zero, r + 0.0018f, r + 0.0018f, 0.0022f, 16, frame, frame, false);
            b.AddDisc(new Vector3(0f, 0.0006f, 0f), r, 16, Pal.Glow(Pal.WindowGlow), true);
            b.Pop();
            var curtain = s.Curtain; curtain.a = 0.55f;
            b.AddFace(new Vector3(-r * 0.55f, 0f, 0.0008f), Vector3.forward, Vector3.up, r * 0.35f, r * 0.75f, curtain);
            b.AddBox(new Vector3(0f, 0f, 0.0016f), new Vector3(2 * r, 0.0011f, 0.0012f), frame);
            b.AddBox(new Vector3(0f, 0f, 0.0016f), new Vector3(0.0011f, 2 * r, 0.0012f), frame);
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f;
                b.Push(new Vector3(Mathf.Cos(a) * (r + 0.0006f), Mathf.Sin(a) * (r + 0.0006f), 0.0016f), Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f));
                b.AddBox(Vector3.zero, new Vector3(r * 0.5f, 0.0016f, 0.0016f), frame);
                b.Pop();
            }
        }

        private static float doorTop;
        private static readonly System.Collections.Generic.Dictionary<int, float> doors = new();

        /// <summary>The tallest door a building has (0: none), for checking the villagers fit through.</summary>
        public static float DoorHeightOf(int variant)
        {
            Get(variant, 0, 0);
            if (!doors.ContainsKey(variant)) { doorTop = 0f; Build(variant, 0); doors[variant] = doorTop; }
            return doors[variant];
        }

        private static void Door(MeshBuilder b, ref Rng rng, float w, float h, Style s, bool arched, bool canopy)
        {
            doorTop = Mathf.Max(doorTop, h);
            const float t = 0.0018f;
            var colour = s.Door;
            float rect = arched ? h - w / 2 : h;
            b.AddBox(new Vector3(0f, rect / 2, 0.0006f), new Vector3(w, rect, 0.0012f), colour);
            var plank = Pal.Shade(colour, 0.8f);
            for (int i = 1; i < 4; i++)
                b.AddBox(new Vector3(-w / 2 + i * w / 4, rect / 2, 0.0013f), new Vector3(0.0005f, rect * 0.96f, 0.0003f), plank);
            // Iron hinges and a tiny glowing peephole.
            foreach (float y in new[] { rect * 0.25f, rect * 0.75f })
                b.AddBox(new Vector3(-w * 0.2f, y, 0.0015f), new Vector3(w * 0.55f, 0.0011f, 0.0004f), Pal.TrainBlack);
            b.Push(new Vector3(0f, rect * 0.82f, 0.0016f), Quaternion.Euler(-90f, 0f, 0f));
            b.AddDisc(Vector3.zero, 0.0016f, 8, Pal.Glow(Pal.WindowGlow), true);
            b.Pop();
            if (b.Detail < 0)
            {
                // Right up close: a brass knob and a letterbox.
                b.AddBlob(new Vector3(w * 0.32f, rect * 0.48f, 0.0021f), Vector3.one * 0.0007f, 1, 0f, 31, (_, __) => Pal.Brass);
                b.AddBox(new Vector3(0f, rect * 0.58f, 0.00155f), new Vector3(w * 0.32f, 0.0009f, 0.0004f), Pal.Brass);
            }
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
                b.AddBox(new Vector3(0f, h + t / 2, 0.0011f), new Vector3(w + 2 * t, t, 0.0022f), s.Trim);
                b.AddBox(new Vector3(-w / 2 - t / 2, h / 2, 0.0011f), new Vector3(t, h, 0.0022f), s.Trim);
                b.AddBox(new Vector3(w / 2 + t / 2, h / 2, 0.0011f), new Vector3(t, h, 0.0022f), s.Trim);
            }
            if (canopy)
            {
                // A little gabled hood on brackets.
                b.Push(new Vector3(0f, h + 0.004f, 0.006f), Quaternion.identity);
                foreach (int side in new[] { -1, 1 })
                {
                    b.Push(new Vector3(side * (w / 2 + 0.001f), -0.0035f, -0.002f), Quaternion.Euler(-40f, 0f, 0f));
                    b.AddBox(Vector3.zero, new Vector3(0.0015f, 0.0015f, 0.008f), s.Timber);
                    b.Pop();
                    b.Push(new Vector3(side * w * 0.3f, 0.0035f, 0f), Quaternion.Euler(0f, 0f, -side * 32f));
                    b.AddBox(Vector3.zero, new Vector3(w * 0.68f, 0.0018f, 0.013f), Pal.Shade(s.Roof, 0.9f), s.Roof, Pal.Shade(s.Roof, 0.6f), false);
                    b.Pop();
                }
                b.Pop();
            }
            b.AddBox(new Vector3(w * 0.32f, rect * 0.5f, 0.0016f), new Vector3(0.0016f, 0.0016f, 0.0012f), Pal.Brass);
            b.AddBox(new Vector3(0f, -0.003f, 0.005f), new Vector3(w * 1.5f, 0.006f, 0.009f), Pal.Stone);
            // Lantern on a bracket.
            float lx = -(w / 2 + 0.0065f);
            b.AddBox(new Vector3(lx, h * 0.8f + 0.004f, 0.0025f), new Vector3(0.001f, 0.001f, 0.005f), Pal.TrainBlack);
            b.AddBox(new Vector3(lx, h * 0.8f, 0.0045f), new Vector3(0.003f, 0.0042f, 0.003f), Pal.Glow(Pal.Hex("#FFE2A0")));
            b.AddCylinder(new Vector3(lx, h * 0.8f + 0.0021f, 0.0045f), 0.0024f, 0f, 0.002f, 4, Pal.TrainBlack, Pal.TrainBlack, false, true, false, Mathf.PI / 4f);
        }

        /// <summary>
        /// A swayback gabled roof (ridge along X): tile rows follow a gently concave curve with a lifted eave, deep
        /// overhangs, scalloped bargeboards on the gables, a crested ridge and finials.
        /// </summary>
        private static void StoryRoof(MeshBuilder b, ref Rng rng, float w, float d, float wallH, float ridgeH, float overhang,
            float sag, Color roof, Color gable, Color trim, int rows, bool gableTimber)
        {
            // The swayback dips by 'sag': lift the roof so the dip never cuts below the wall top; gables still close
            // down to the real wall top.
            float wallTop = wallH;
            wallH += sag + 0.0015f;
            float slope = ridgeH / (d / 2);
            float halfSpan = d / 2 + overhang;
            float eaveY = wallH - overhang * slope;
            var ridge = new Vector3(0f, wallH + ridgeH, 0f);
            float roofLen = w + overhang * 1.6f;
            foreach (int side in new[] { -1, 1 })
            {
                var eave = new Vector3(0f, eaveY, side * halfSpan);
                var upSlope = (ridge - eave).normalized;
                var normal = Vector3.Cross(upSlope, Vector3.right * -side).normalized;
                if (normal.y < 0f) normal = -normal;
                Vector3 Curve(float u) => Vector3.Lerp(eave, ridge, u) - normal * (sag * Mathf.Sin(u * Mathf.PI)) + normal * (0.0025f * Mathf.Pow(1f - u, 4f));
                for (int k = 0; k < rows; k++)
                {
                    float u0 = k / (float)rows, u1 = (k + 1f) / rows;
                    var p0 = Curve(u0); var p1 = Curve(u1);
                    var tangent = (p1 - p0).normalized;
                    var n = Vector3.Cross(tangent, Vector3.right * -side).normalized; if (n.y < 0f) n = -n;
                    float step = (p1 - p0).magnitude;
                    var rowColour = Pal.Shade(roof, 0.84f + 0.16f * (k / (float)rows));
                    int tiles = Mathf.Max(3, Mathf.RoundToInt(roofLen / 0.0105f));
                    float tw = roofLen / tiles, offset = (k % 2) * tw * 0.5f;
                    b.Push((p0 + p1) * 0.5f, Quaternion.LookRotation(tangent, n));
                    if (b.Detail >= MeshBuilder.Minimal)
                    {
                        // From far away: the whole roof side is one plane in its colour.
                        b.Pop();
                        if (k == 0) { var top = Curve(1f); var mid = (p0 + top) * 0.5f; b.Push(mid, Quaternion.LookRotation((top - p0).normalized, n)); b.AddFace(new Vector3(0f, 0.002f, 0f), Vector3.up, Vector3.forward, roofLen / 2, (top - p0).magnitude * 0.5f, Pal.Shade(roof, 0.92f)); b.Pop(); }
                        continue;
                    }
                    if (b.Detail > 0)
                    {
                        // Seen from further away: each row of tiles is one strip in the row's colour (the stepped rows remain).
                        b.AddFace(new Vector3(0f, 0.0027f, 0.0005f), Vector3.up, Vector3.forward, roofLen / 2, step * 0.6f, rowColour);
                        b.AddFace(new Vector3(0f, 0.0016f, 0.0005f - step * 0.6f), Vector3.back, Vector3.up, roofLen / 2, 0.0011f, Pal.Shade(rowColour, 0.7f));
                        b.Pop();
                        continue;
                    }
                    // Dark underlay so the hairline gaps between tiles read as shadow, not daylight.
                    b.AddFace(new Vector3(0f, 0.0004f, 0f), Vector3.up, Vector3.forward, roofLen / 2, step * 0.6f, Pal.Shade(roof, 0.35f));
                    for (int i = 0; i < tiles; i++)
                    {
                        float x = -roofLen / 2 + (i + 0.5f) * tw + offset;
                        float wTile = tw - 0.0005f;
                        if (x + wTile / 2 > roofLen / 2) { wTile = roofLen / 2 - (x - wTile / 2); x = roofLen / 2 - wTile / 2; }
                        if (wTile < 0.002f) continue;
                        var c = Pal.Jitter(rowColour, ref rng, 0.2f);
                        b.Push(new Vector3(x, 0.0016f + rng.Range(0f, 0.0004f), 0.0005f), Quaternion.Euler(-5f + rng.Range(-1.5f, 1.5f), rng.Range(-2f, 2f), 0f));
                        Tile(b, wTile, step * 1.2f, c, k == 0);
                        b.Pop();
                    }
                    b.Pop();
                }
                // Bargeboards with a scalloped lower edge on both gables.
                foreach (int end in new[] { -1, 1 })
                {
                    float x = end * (roofLen / 2 + 0.0009f);
                    const int segs = 6;
                    for (int i = 0; i < segs; i++)
                    {
                        var a = Curve(i / (float)segs); var c = Curve((i + 1f) / segs);
                        a.x = c.x = x;
                        var mid = (a + c) * 0.5f; var dir = c - a;
                        b.Push(mid - normal * 0.0012f, Quaternion.LookRotation(dir.normalized, normal));
                        b.AddBox(Vector3.zero, new Vector3(0.0016f, 0.0042f, dir.magnitude * 1.05f), trim);
                        b.Pop();
                        b.AddBlob(mid - normal * 0.0042f, new Vector3(0.0009f, 0.0018f, 0.0018f), 0, 0f, i, (_, __) => trim);
                    }
                }
            }
            // Crested ridge and finials.
            b.Push(ridge + Vector3.up * 0.002f, Quaternion.Euler(0f, 0f, 90f));
            b.AddCylinder(new Vector3(0f, -roofLen / 2 - 0.001f, 0f), 0.003f, 0.003f, roofLen + 0.002f, 8, Pal.Shade(roof, 0.7f), Pal.Shade(roof, 0.7f), true, true);
            b.Pop();
            for (float x = -roofLen / 2 + 0.005f; x < roofLen / 2 - 0.003f; x += 0.007f)
                b.AddCylinder(ridge + new Vector3(x, 0.004f, 0f), 0.0014f, 0.0006f, 0.0028f, 5, Pal.Shade(roof, 0.65f), Pal.Shade(roof, 0.8f), true);
            foreach (int end in new[] { -1, 1 })
            {
                var p = ridge + new Vector3(end * (roofLen / 2), 0.004f, 0f);
                b.AddCylinder(p, 0.0007f, 0.0005f, 0.008f, 5, trim, trim, true);
                b.AddBlob(p + Vector3.up * 0.0085f, Vector3.one * 0.0018f, 1, 0f, end + 3, (_, __) => Pal.Brass);
            }
            // Gable walls follow the curve.
            float uWall = 1f - (d / 2) / halfSpan;
            foreach (int end in new[] { -1, 1 })
            {
                float x = end * w / 2;
                var centre = new Vector3(x, wallTop, 0f);
                var inside = new Vector3(0f, wallH + ridgeH * 0.3f, 0f);
                foreach (int side in new[] { -1, 1 })
                {
                    var eave = new Vector3(0f, eaveY, side * halfSpan);
                    var upSlope = (ridge - eave).normalized;
                    var normal = Vector3.Cross(upSlope, Vector3.right * -side).normalized; if (normal.y < 0f) normal = -normal;
                    Vector3 Curve(float u) { var q = Vector3.Lerp(eave, ridge, u) - normal * (sag * Mathf.Sin(u * Mathf.PI)); q.x = x; return q; }
                    const int segs = 6;
                    for (int i = 0; i < segs; i++)
                    {
                        float ua = Mathf.Lerp(uWall, 1f, i / (float)segs), ub = Mathf.Lerp(uWall, 1f, (i + 1f) / segs);
                        b.AddTriangleOutward(centre, Curve(ua) - Vector3.up * 0.001f, Curve(ub) - Vector3.up * 0.001f, inside, gable);
                    }
                    b.AddTriangleOutward(centre, new Vector3(x, wallTop, side * d / 2), Curve(uWall) - Vector3.up * 0.001f, inside, gable);
                }
                if (gableTimber)
                {
                    b.AddBox(new Vector3(x + end * 0.0012f, wallH + ridgeH * 0.42f, 0f), new Vector3(0.0024f, ridgeH * 0.84f, 0.003f), Pal.Timber);
                    b.AddBox(new Vector3(x + end * 0.0012f, wallH + ridgeH * 0.18f, 0f), new Vector3(0.0024f, 0.0026f, d * 0.7f), Pal.Timber);
                }
            }
        }

        /// <summary>A roof tile: its sunlit face and the thick lower lip (the rest is hidden by the overlapping rows).</summary>
        private static void Tile(MeshBuilder b, float w, float len, Color c, bool underside)
        {
            const float t = 0.0022f;
            b.AddFace(new Vector3(0f, t / 2, 0f), Vector3.up, Vector3.forward, w / 2, len / 2, c);
            b.AddFace(new Vector3(0f, 0f, -len / 2), Vector3.back, Vector3.up, w / 2, t / 2, Pal.Shade(c, 0.7f));
            if (underside) b.AddFace(new Vector3(0f, -t / 2, 0f), Vector3.down, Vector3.forward, w / 2, len / 2, Pal.Shade(c, 0.5f));
        }

        private static void Dormer(MeshBuilder b, ref Rng rng, Vector3 position, float width, Style s)
        {
            b.Push(position);
            b.AddBox(new Vector3(0f, 0.011f, 0.007f), new Vector3(width, 0.022f, 0.022f), s.Plaster);
            Wall(b, new Vector3(0f, 0.012f, -0.0042f), Vector3.back); Window(b, ref rng, width * 0.45f, 0.012f, s, false, false, false); b.Pop();
            b.Push(new Vector3(0f, 0.022f, 0.007f), Quaternion.Euler(0f, 90f, 0f));
            StoryRoof(b, ref rng, 0.024f, width + 0.002f, 0f, 0.013f, 0.003f, 0.0012f, s.Roof, s.Plaster, s.Trim, 3, false);
            b.Pop();
            b.Pop();
        }

        /// <summary>
        /// A fat storybook thatch: stacked courses of straw, each a rounded skirt flaring over the one below (a
        /// wedding-cake silhouette), eyebrows bulging over the front windows, combed lighter edges, moss on top and a
        /// rolled ridge.
        /// </summary>
        private static void ThatchRoof(MeshBuilder b, ref Rng rng, float w, float d, float wallH, float roofH, Color strawBase)
        {
            var strawDark = Pal.Shade(strawBase, 0.7f); var strawLight = Pal.Shade(strawBase, 1.15f); var moss = Pal.Hex("#8E9A45");
            float eave = wallH - 0.006f;
            const int courses = 4;
            for (int k = 0; k < courses; k++)
            {
                float t = k / (float)courses;
                var c = new Vector3(0f, eave + roofH * 0.26f * k, 0f);
                var radius = new Vector3((w / 2 + 0.018f) * (1f - 0.2f * k), roofH * (0.62f - 0.08f * k), (d / 2 + 0.021f) * (1f - 0.24f * k));
                int start = b.VertexCount, seed = rng.Range(0, 999);
                b.AddBlob(c, radius, 2, 0.04f, seed, (dir, pos) =>
                {
                    float angle = Mathf.Atan2(dir.z, dir.x);
                    float streak = Noise.Value2(angle * 60f, dir.y * 4f) * 0.7f + Noise.Value2(angle * 150f, dir.y * 9f) * 0.3f;
                    var col = Color.Lerp(strawDark, strawBase, 0.25f + 0.75f * streak);
                    col = Color.Lerp(col, strawLight, Mathf.SmoothStep(0.35f, 0.05f, dir.y) * 0.55f);    // combed lower edge
                    if (k == courses - 1) col = Color.Lerp(col, moss, Mathf.SmoothStep(0.55f, 1f, dir.y) * 0.5f * Noise.Value2(dir.x * 8f + 3f, dir.z * 8f));
                    if (dir.y < 0.02f) col = Pal.Shade(strawDark, 0.5f);
                    return col;
                }, 3f);
                for (int i = start; i < b.VertexCount; i++)
                {
                    var v = b.Vertices[i];
                    if (v.y < c.y) { v.y = c.y - 0.0018f; b.Normals[i] = Vector3.down; b.Vertices[i] = v; continue; }
                    // Eyebrows over the two front round windows (lowest course only).
                    if (k == 0 && v.z < 0f)
                        foreach (float bx in new[] { -0.034f, 0.034f })
                        {
                            float dx = (v.x - bx) / 0.014f, dy = (v.y - (wallH + 0.002f)) / 0.008f;
                            float brow = Mathf.Exp(-dx * dx - dy * dy) * 0.007f;
                            v.z -= brow; v.y += brow * 0.7f;
                        }
                    b.Vertices[i] = v;
                }
                b.RecalculateNormals(start);
            }
            // Rolled ridge along the top.
            float top = eave + roofH * 0.26f * (courses - 1) + roofH * (0.62f - 0.08f * (courses - 1)) * 0.86f;
            b.Push(new Vector3(0f, top, 0f), Quaternion.Euler(0f, 0f, 90f));
            float len = (w / 2 + 0.018f) * (1f - 0.2f * (courses - 1)) * 0.95f;
            b.AddCylinder(new Vector3(0f, -len / 2, 0f), 0.005f, 0.005f, len, 10, strawDark, Pal.Shade(strawBase, 0.9f), true, true);
            b.Pop();
            for (float x = -len / 2 + 0.006f; x < len / 2; x += 0.009f)
            {
                b.Push(new Vector3(x, top + 0.002f, 0f), Quaternion.Euler(0f, 45f, 0f));
                b.AddBox(Vector3.zero, new Vector3(0.0012f, 0.0012f, 0.012f), Pal.Shade(strawBase, 0.6f));
                b.Pop();
            }
        }

        private static void Chimney(MeshBuilder b, ref Rng rng, Vector3 basePos, float height, Color colour, int pots)
        {
            const float s = 0.011f;
            // Slightly crooked, as chimneys in stories are.
            b.Push(basePos, Quaternion.Euler(rng.Range(-4f, 4f), 0f, rng.Range(-6f, 6f)));
            b.AddBox(new Vector3(0f, height / 2, 0f), new Vector3(s, height, s), colour, Pal.Shade(colour, 0.6f), colour);
            for (float y = 0.004f; y < height - 0.004f; y += 0.004f)
                b.AddBox(new Vector3(0f, y, 0f), new Vector3(s + 0.0006f, 0.0006f, s + 0.0006f), Pal.Shade(colour, 0.8f));
            b.AddBox(new Vector3(0f, height + 0.0015f, 0f), new Vector3(s + 0.004f, 0.003f, s + 0.004f), Pal.StoneDark);
            for (int i = 0; i < pots; i++)
            {
                float x = pots == 1 ? 0f : (i - 0.5f) * 0.005f;
                b.AddCylinder(new Vector3(x, height + 0.003f, 0f), 0.0022f, 0.0019f, 0.006f, 8, Pal.Hex("#B5603E"), Pal.Hex("#C8754B"), false);
                b.AddCylinder(new Vector3(x, height + 0.009f, 0f), 0.0025f, 0.0025f, 0.0012f, 8, Pal.Hex("#A9553A"), Pal.Hex("#A9553A"), false);
                b.AddDisc(new Vector3(x, height + 0.0098f, 0f), 0.0018f, 8, Pal.Hex("#2A2224"), true);
            }
            b.Pop();
        }

        private static void WeatherVane(MeshBuilder b, Vector3 p)
        {
            b.AddCylinder(p, 0.0005f, 0.0004f, 0.016f, 4, Pal.TrainBlack, Pal.TrainBlack, true);
            b.AddBox(p + new Vector3(0f, 0.011f, 0f), new Vector3(0.012f, 0.0007f, 0.0007f), Pal.TrainBlack);
            b.AddBox(p + new Vector3(0f, 0.011f, 0f), new Vector3(0.0007f, 0.0007f, 0.012f), Pal.TrainBlack);
            // A little rooster.
            b.AddBlob(p + new Vector3(0.001f, 0.0155f, 0f), new Vector3(0.0035f, 0.0025f, 0.0008f), 1, 0.1f, 4, (_, __) => Pal.Brass);
            b.AddBlob(p + new Vector3(0.0038f, 0.0178f, 0f), new Vector3(0.0013f, 0.0016f, 0.0007f), 0, 0f, 5, (_, __) => Pal.Brass);
            b.AddBox(p + new Vector3(-0.0035f, 0.0175f, 0f), new Vector3(0.0018f, 0.004f, 0.0006f), Pal.Brass);
        }

        /// <summary>A climbing rose up a wall corner: leafy puffs with blooms, thinning towards the top.</summary>
        private static void Roses(MeshBuilder b, ref Rng rng, Vector3 corner, float height, Color bloom)
        {
            var leaf = Pal.Hex("#3C7A33");
            var dir = new Vector3(Mathf.Sign(corner.x), 0f, Mathf.Sign(corner.z));
            var p = corner + new Vector3(dir.x * 0.002f, 0.004f, dir.z * 0.002f);
            for (float y = 0f; y < height; y += 0.0045f)
            {
                float t = y / height;
                var q = p + Vector3.up * y + new Vector3(Mathf.Sin(y * 300f) * 0.002f * -dir.x, 0f, 0f);
                float r = Mathf.Lerp(0.0045f, 0.0025f, t);
                var l = Pal.Jitter(leaf, ref rng, 0.2f);
                b.AddBlob(q, new Vector3(r, r * 0.9f, r), 0, 0.3f, (int)(y * 1000f), (d, _) => Color.Lerp(Pal.Shade(l, 0.7f), l, d.y * 0.5f + 0.5f));
                if (rng.Chance(0.7f))
                {
                    var c = Pal.Jitter(bloom, ref rng, 0.15f);
                    var off = new Vector3(rng.Range(-1f, 1f), rng.Range(-0.5f, 1f), rng.Range(-1f, 1f)).normalized * r;
                    b.AddBlob(q + off, Vector3.one * 0.0016f, 0, 0.1f, (int)(y * 999f), (_, __) => c);
                }
            }
        }

        private static void SteppingStones(MeshBuilder b, ref Rng rng, Vector3 start)
        {
            for (int i = 0; i < 3; i++)
            {
                var c = Pal.Jitter(Pal.Stone, ref rng, 0.25f);
                var p = start + new Vector3(rng.Range(-0.003f, 0.003f), 0.0004f, -i * 0.0085f);
                b.AddCylinder(p, 0.0038f, 0.0035f, 0.0012f, 7, Pal.Shade(c, 0.8f), c, true, false, false, rng.Value);
            }
        }

        private static void Shrubs(MeshBuilder b, ref Rng rng, float w, float d, int count, Color main)
        {
            var palette = new[] { main, Pal.FlowerPink, Pal.FlowerYellow, Pal.FlowerWhite, Pal.FlowerLilac };
            for (int i = 0; i < count; i++)
            {
                float side = rng.Chance(0.5f) ? -1f : 1f;
                var p = new Vector3(rng.Range(-w / 2, w / 2), 0.004f, -d / 2 - 0.007f);
                if (i % 2 == 1) p = new Vector3(side * (w / 2 + 0.007f), 0.004f, rng.Range(-d / 2, d / 2));
                if (Mathf.Abs(p.x) < 0.02f && p.z < 0f) p.x += 0.034f;   // keep the front door and path clear
                float r = rng.Range(0.006f, 0.009f);
                var leaf = Pal.Jitter(Pal.LeafMid, ref rng, 0.25f);
                var leafDark = Pal.Shade(leaf, 0.7f);
                Flora.Paint paint = (dir, _) => Color.Lerp(leafDark, leaf, dir.y * 0.5f + 0.5f);
                b.Sway = 0.3f;
                Flora.LeafCluster(b, p, r, Vector3.up, 3, ref rng, paint);
                var flower = palette[rng.Range(0, palette.Length)];
                for (int k = 0; k < 5; k++)
                {
                    var dir = new Vector3(rng.Range(-1f, 1f), rng.Range(0.3f, 1f), rng.Range(-1f, 1f)).normalized;
                    var c = Pal.Jitter(flower, ref rng, 0.15f);
                    b.AddBlob(p + dir * r * 1.05f, Vector3.one * 0.0017f, 0, 0.1f, k, (_, __) => c);
                }
                b.Sway = 0f;
            }
        }
    }
}
