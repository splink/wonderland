using UnityEngine;

namespace Wonderland
{
    /// <summary>Where a car's wheels, pivots and extents are (local: +Z forward, +Y up, y = 0 on the rail tops).</summary>
    public sealed class CarLayout
    {
        public float Front, Rear;        // extents along Z (rear is negative)
        public float PivotHalf;          // bogie / axle-group half distance used to follow the track
        public Vector3[] Axles;          // axle centres
        public float[] WheelRadius;
        public float RodFrom, RodTo;     // side rod span (loco only)
        public Vector3 Chimney;          // smoke origin (loco only)
    }

    /// <summary>
    /// Storybook rolling stock for 24 mm gauge track, built from procedural parts in the shared palette: a steam
    /// locomotive (about 8 cm tall: the scale yardstick), passenger coaches, four goods wagons (logs, van, tank, coal) and
    /// a double-ended railcar. Locomotives, coaches and railcars come in six liveries so every train has its own colour.
    /// Wheel sets are separate meshes so they can spin.
    /// </summary>
    public static class TrainFactory
    {
        public const float Gauge = 0.024f;
        public const int WagonVariants = 4;
        private static Mesh rods;
        private static readonly System.Collections.Generic.Dictionary<int, Mesh> Cache = new();

        // Liveries: locomotive / railcar colour, its dark shade, and the two coach colours.
        private static readonly string[,] Liveries =
        {
            { "#C3312A", "#8E1F1C", "#7E2A2F", "#2F6B4F" },
            { "#2F62B0", "#1E3F78", "#2B4A7E", "#6A3A6E" },
            { "#2F8A4E", "#1D5A32", "#2F6B4F", "#8A5A2A" },
            { "#E2A72E", "#A0701A", "#3A5A8A", "#7E2A2F" },
            { "#7A4AA6", "#50306E", "#50306E", "#2F6B4F" },
            { "#3A3B44", "#22232A", "#7E2A2F", "#2B4A7E" },
        };

        public static Color LiveryColour(int livery) => Pal.Hex(Liveries[Mod(livery), 0]);
        private static Color LiveryDark(int livery) => Pal.Hex(Liveries[Mod(livery), 1]);
        private static Color CoachColour(int livery, int variant) => Pal.Hex(Liveries[Mod(livery), 2 + (variant & 1)]);
        private static int Mod(int livery) => ((livery % Liveries.GetLength(0)) + Liveries.GetLength(0)) % Liveries.GetLength(0);

        private static Mesh Cached(int kind, int a, int b, System.Func<Mesh> build)
        {
            int key = kind * 1000000 + a * 1000 + b;
            if (!Cache.TryGetValue(key, out var mesh)) Cache[key] = mesh = build();
            return mesh;
        }

        public static readonly CarLayout Loco = new()
        {
            Front = 0.077f, Rear = -0.066f, PivotHalf = 0.04f,
            Axles = new[] { new Vector3(0f, 0.0105f, -0.031f), new Vector3(0f, 0.0105f, -0.007f), new Vector3(0f, 0.0105f, 0.017f), new Vector3(0f, 0.0068f, 0.048f) },
            WheelRadius = new[] { 0.0105f, 0.0105f, 0.0105f, 0.0068f },
            RodFrom = -0.031f, RodTo = 0.017f,
            Chimney = new Vector3(0f, 0.077f, 0.058f),
        };

        public static readonly CarLayout Coach = new()
        {
            Front = 0.063f, Rear = -0.063f, PivotHalf = 0.038f,
            Axles = new[] { new Vector3(0f, 0.0075f, -0.049f), new Vector3(0f, 0.0075f, -0.027f), new Vector3(0f, 0.0075f, 0.027f), new Vector3(0f, 0.0075f, 0.049f) },
            WheelRadius = new[] { 0.0075f, 0.0075f, 0.0075f, 0.0075f },
        };

        public static readonly CarLayout Wagon = new()
        {
            Front = 0.045f, Rear = -0.045f, PivotHalf = 0.026f,
            Axles = new[] { new Vector3(0f, 0.0075f, -0.026f), new Vector3(0f, 0.0075f, 0.026f) },
            WheelRadius = new[] { 0.0075f, 0.0075f },
        };

        public static readonly CarLayout Railcar = new()
        {
            Front = 0.074f, Rear = -0.074f, PivotHalf = 0.047f,
            Axles = new[] { new Vector3(0f, 0.0075f, -0.058f), new Vector3(0f, 0.0075f, -0.036f), new Vector3(0f, 0.0075f, 0.036f), new Vector3(0f, 0.0075f, 0.058f) },
            WheelRadius = new[] { 0.0075f, 0.0075f, 0.0075f, 0.0075f },
        };

        public static Mesh LocoMesh(int livery = 0) => Cached(1, Mod(livery), 0, () => BuildLoco(Mod(livery)));
        public static Mesh RodMesh => rods ??= BuildRods();
        public static Mesh CoachMesh(int variant, int livery = 0) => Cached(2, Mod(livery), variant & 1, () => BuildCoach(variant & 1, Mod(livery)));
        public static Mesh WagonMesh(int variant) => Cached(3, variant % WagonVariants, 0, () => BuildWagon(variant % WagonVariants));
        public static Mesh RailcarMesh(int livery = 0) => Cached(4, Mod(livery), 0, () => BuildRailcar(Mod(livery)));

        public static Mesh AxleMesh(float radius, int livery = 0) =>
            Cached(5, Mathf.RoundToInt(radius * 10000f), Mod(livery), () => BuildAxle(radius, LiveryColour(livery)));

        private static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);
        private static readonly Quaternion AlongX = Quaternion.Euler(0f, 0f, -90f);

        private static void CylinderZ(MeshBuilder b, Vector3 start, float r0, float r1, float length, int seg, Color c0, Color c1, bool caps = true)
        {
            b.Push(start, AlongZ);
            b.AddCylinder(Vector3.zero, r0, r1, length, seg, c0, c1, caps, caps);
            b.Pop();
        }

        private static Mesh BuildLoco(int livery)
        {
            var b = new MeshBuilder();
            var red = LiveryColour(livery); var redDark = LiveryDark(livery); var black = Pal.TrainBlack; var brass = Pal.Brass;

            // Frame, valance and running boards.
            b.AddBox(new Vector3(0f, 0.012f, 0.004f), new Vector3(0.017f, 0.008f, 0.12f), black);
            b.AddBox(new Vector3(0f, 0.0195f, 0.004f), new Vector3(0.05f, 0.005f, 0.122f), redDark, red, black, false);
            b.AddBox(new Vector3(0f, 0.0222f, 0.004f), new Vector3(0.052f, 0.0008f, 0.122f), brass);

            // Boiler with brass bands, smokebox, front door.
            CylinderZ(b, new Vector3(0f, 0.0395f, -0.012f), 0.0165f, 0.0165f, 0.066f, 18, red, red);
            foreach (float z in new[] { 0.002f, 0.026f, 0.05f })
                CylinderZ(b, new Vector3(0f, 0.0395f, z), 0.0169f, 0.0169f, 0.0016f, 18, brass, brass);
            CylinderZ(b, new Vector3(0f, 0.0395f, 0.052f), 0.0174f, 0.0174f, 0.014f, 18, black, black);
            CylinderZ(b, new Vector3(0f, 0.0395f, 0.066f), 0.0135f, 0.012f, 0.0018f, 18, Pal.Shade(black, 1.4f), Pal.Shade(black, 1.6f));
            b.AddBox(new Vector3(0.006f, 0.0395f, 0.0685f), new Vector3(0.0035f, 0.0012f, 0.0012f), brass);
            // Saddle under the smokebox.
            b.AddBox(new Vector3(0f, 0.027f, 0.059f), new Vector3(0.026f, 0.012f, 0.013f), black);

            // Chimney, dome, whistle, headlamp.
            b.AddCylinder(new Vector3(0f, 0.052f, 0.059f), 0.0052f, 0.0045f, 0.016f, 12, black, black, false);
            b.AddCylinder(new Vector3(0f, 0.068f, 0.059f), 0.0045f, 0.0072f, 0.007f, 12, black, Pal.Shade(black, 1.3f), false);
            b.AddCylinder(new Vector3(0f, 0.0748f, 0.059f), 0.0074f, 0.0074f, 0.0016f, 12, brass, brass, true);
            b.AddCylinder(new Vector3(0f, 0.053f, 0.026f), 0.0078f, 0.0072f, 0.0045f, 12, brass, brass, false);
            b.AddBlob(new Vector3(0f, 0.0575f, 0.026f), new Vector3(0.0072f, 0.0055f, 0.0072f), 2, 0f, 1, (d, _) => Color.Lerp(Pal.Shade(brass, 0.8f), Pal.Hex("#FFE7A3"), d.y));
            b.AddCylinder(new Vector3(0f, 0.054f, 0.004f), 0.0045f, 0.0045f, 0.003f, 10, red, red, false);
            b.AddBlob(new Vector3(0f, 0.057f, 0.004f), new Vector3(0.0045f, 0.0032f, 0.0045f), 1, 0f, 2, (d, _) => red);
            b.AddCylinder(new Vector3(0f, 0.055f, -0.008f), 0.0012f, 0.0016f, 0.007f, 6, brass, brass, true);
            b.AddBox(new Vector3(0f, 0.0605f, 0.0645f), new Vector3(0.0065f, 0.006f, 0.005f), black);
            b.AddBox(new Vector3(0f, 0.0605f, 0.0672f), new Vector3(0.0045f, 0.0042f, 0.0008f), Pal.Glow(Pal.Hex("#FFF2C8")));

            // Cylinders and steam chests.
            foreach (int s in new[] { -1, 1 })
            {
                CylinderZ(b, new Vector3(s * 0.0215f, 0.0155f, 0.040f), 0.0058f, 0.0058f, 0.017f, 12, black, black);
                CylinderZ(b, new Vector3(s * 0.0215f, 0.0155f, 0.0565f), 0.0045f, 0.0045f, 0.0012f, 12, brass, brass);
                // Sand boxes / splashers over the driving wheels.
                b.AddBox(new Vector3(s * 0.0205f, 0.0255f, -0.007f), new Vector3(0.008f, 0.006f, 0.058f), red, red, red);
                b.AddBox(new Vector3(s * 0.0205f, 0.0287f, -0.007f), new Vector3(0.0084f, 0.0007f, 0.0584f), brass);
                // Handrails along the boiler.
                CylinderZ(b, new Vector3(s * 0.0175f, 0.046f, -0.008f), 0.0006f, 0.0006f, 0.06f, 5, brass, brass, false);
            }

            // Cab.
            const float cabZ0 = -0.05f, cabZ1 = -0.011f, cabW = 0.047f, cabY0 = 0.022f, cabY1 = 0.066f;
            float cabZc = (cabZ0 + cabZ1) / 2f, cabL = cabZ1 - cabZ0;
            // Lower cab sides with window openings above.
            b.AddBox(new Vector3(0f, (cabY0 + 0.046f) / 2f, cabZc), new Vector3(cabW, 0.046f - cabY0, cabL), red, red, red);
            foreach (int s in new[] { -1, 1 })
            {
                float x = s * (cabW / 2f - 0.0015f);
                b.AddBox(new Vector3(x, (0.046f + cabY1) / 2f, cabZ1 - 0.0025f), new Vector3(0.003f, cabY1 - 0.046f, 0.005f), red);
                b.AddBox(new Vector3(x, (0.046f + cabY1) / 2f, cabZ0 + 0.0025f), new Vector3(0.003f, cabY1 - 0.046f, 0.005f), red);
                b.AddBox(new Vector3(x, (0.046f + cabY1) / 2f, cabZc - 0.002f), new Vector3(0.003f, cabY1 - 0.046f, 0.003f), red);
                // Warm cab interior glow seen through the side windows.
                b.AddFace(new Vector3(s * (cabW / 2f - 0.004f), 0.0555f, cabZc), new Vector3(s, 0f, 0f), Vector3.up, cabL * 0.42f, 0.0085f, Pal.Glow(Pal.Hex("#8A4A2A")));
                // Brass beading and a name plate.
                b.AddBox(new Vector3(s * (cabW / 2f + 0.0002f), 0.0462f, cabZc), new Vector3(0.0008f, 0.0009f, cabL), brass);
                b.AddBox(new Vector3(s * (cabW / 2f + 0.0003f), 0.034f, cabZc), new Vector3(0.0008f, 0.006f, 0.02f), brass);
                b.AddBox(new Vector3(s * (cabW / 2f + 0.0006f), 0.034f, cabZc), new Vector3(0.0006f, 0.0042f, 0.017f), redDark);
            }
            // Front spectacle plate with two round windows.
            b.AddBox(new Vector3(0f, (0.046f + cabY1) / 2f, cabZ1 - 0.0012f), new Vector3(cabW, cabY1 - 0.046f, 0.0024f), red);
            foreach (int s in new[] { -1, 1 })
            {
                b.Push(new Vector3(s * 0.013f, 0.059f, cabZ1 + 0.0001f), AlongZ);
                b.AddCylinder(Vector3.zero, 0.0042f, 0.0042f, 0.0008f, 12, brass, brass, true);
                b.AddDisc(new Vector3(0f, 0.0009f, 0f), 0.0032f, 12, Pal.Glow(Pal.Hex("#FFE0A0")), true);
                b.Pop();
            }
            // Cab back with a rear window, and the roof (two slightly arched panels).
            b.AddBox(new Vector3(0f, (0.046f + cabY1) / 2f, cabZ0 + 0.0012f), new Vector3(cabW, cabY1 - 0.046f, 0.0024f), red);
            foreach (int s in new[] { -1, 1 })
            {
                b.Push(new Vector3(s * 0.0128f, cabY1 + 0.0028f, cabZc), Quaternion.Euler(0f, 0f, -s * 9f));
                b.AddBox(Vector3.zero, new Vector3(0.027f, 0.0024f, cabL + 0.008f), black, Pal.Shade(black, 1.4f), black, false);
                b.Pop();
            }
            // Coal bunker behind the cab.
            b.AddBox(new Vector3(0f, 0.035f, -0.0575f), new Vector3(0.042f, 0.026f, 0.015f), red, red, red);
            b.AddBox(new Vector3(0f, 0.0482f, -0.0575f), new Vector3(0.0424f, 0.0008f, 0.0154f), brass);
            b.AddBlob(new Vector3(0f, 0.048f, -0.0575f), new Vector3(0.018f, 0.004f, 0.006f), 1, 0.5f, 3, (_, __) => Pal.Hex("#1E1E22"));

            // Buffer beams, buffers, couplings, cowcatcher.
            b.AddBox(new Vector3(0f, 0.0155f, 0.0715f), new Vector3(0.05f, 0.009f, 0.004f), red);
            b.AddBox(new Vector3(0f, 0.0155f, -0.0635f), new Vector3(0.05f, 0.009f, 0.004f), red);
            foreach (int s in new[] { -1, 1 })
            {
                CylinderZ(b, new Vector3(s * 0.017f, 0.0155f, 0.0735f), 0.0016f, 0.0016f, 0.003f, 8, black, black);
                CylinderZ(b, new Vector3(s * 0.017f, 0.0155f, 0.0765f), 0.0028f, 0.0028f, 0.0008f, 10, brass, brass);
                CylinderZ(b, new Vector3(s * 0.017f, 0.0155f, -0.0685f), 0.0016f, 0.0016f, 0.003f, 8, black, black);
                CylinderZ(b, new Vector3(s * 0.017f, 0.0155f, -0.0693f), 0.0028f, 0.0028f, 0.0008f, 10, brass, brass);
            }
            b.Push(new Vector3(0f, 0.0075f, 0.0755f), Quaternion.Euler(38f, 0f, 0f));
            b.AddBox(Vector3.zero, new Vector3(0.036f, 0.002f, 0.015f), Pal.Shade(red, 0.9f), red, redDark, false);
            for (int i = -2; i <= 2; i++) b.AddBox(new Vector3(i * 0.0065f, 0.0014f, 0f), new Vector3(0.0012f, 0.0012f, 0.015f), black);
            b.Pop();
            return b.ToMesh("Locomotive" + livery);
        }

        private static Mesh BuildCoach(int variant, int livery)
        {
            var b = new MeshBuilder();
            var lower = CoachColour(livery, variant);
            var upper = Pal.CoachCream; var roof = Pal.CoachRoof; var black = Pal.TrainBlack; var brass = Pal.Brass;
            const float L = 0.118f, W = 0.044f, y0 = 0.018f, yMid = 0.037f, y1 = 0.06f;

            b.AddBox(new Vector3(0f, 0.014f, 0f), new Vector3(0.03f, 0.006f, L - 0.01f), black);
            b.AddBox(new Vector3(0f, (y0 + yMid) / 2f, 0f), new Vector3(W, yMid - y0, L), lower, lower, black, false);
            b.AddBox(new Vector3(0f, (yMid + y1) / 2f, 0f), new Vector3(W - 0.001f, y1 - yMid, L - 0.001f), upper, upper, upper);
            b.AddBox(new Vector3(0f, yMid, 0f), new Vector3(W + 0.0008f, 0.0011f, L + 0.0008f), brass);
            // Panel lines on the lower body.
            for (int i = -4; i <= 4; i++)
                foreach (int s in new[] { -1, 1 })
                    b.AddBox(new Vector3(s * (W / 2f + 0.0002f), (y0 + yMid) / 2f, i * L / 9f), new Vector3(0.0005f, (yMid - y0) * 0.75f, 0.0006f), Pal.Shade(lower, 0.75f));
            // Windows: warm lit compartments with dark frames.
            const int windows = 5;
            for (int i = 0; i < windows; i++)
            {
                float z = -L / 2f + 0.014f + i * (L - 0.028f) / (windows - 1);
                foreach (int s in new[] { -1, 1 })
                {
                    var n = new Vector3(s, 0f, 0f);
                    float x = s * (W / 2f - 0.0005f + 0.0004f);
                    b.AddFace(new Vector3(x, 0.0485f, z), n, Vector3.up, 0.0058f, 0.0062f, Pal.Glow(Pal.WindowGlow));
                    b.AddBox(new Vector3(x, 0.0485f + 0.0068f, z), new Vector3(0.0009f, 0.0011f, 0.0132f), lower);
                    b.AddBox(new Vector3(x, 0.0485f - 0.0068f, z), new Vector3(0.0009f, 0.0011f, 0.0132f), lower);
                }
            }
            // Ends with doors.
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(0f, 0.039f, s * (L / 2f + 0.0006f)), new Vector3(0.012f, 0.036f, 0.0012f), lower);
                b.AddFace(new Vector3(0f, 0.049f, s * (L / 2f + 0.0013f)), new Vector3(0f, 0f, s), Vector3.up, 0.0038f, 0.005f, Pal.Glow(Pal.WindowGlow));
            }
            // Arched roof (extruded profile) with end caps and ventilators.
            var path = new[] { new Vector3(0f, y1, -L / 2f - 0.003f), new Vector3(0f, y1, L / 2f + 0.003f) };
            var right = new[] { Vector3.right, Vector3.right }; var up = new[] { Vector3.up, Vector3.up };
            const int arc = 9;
            var profile = new Vector2[arc + 1]; var colours = new Color[arc + 1];
            for (int i = 0; i <= arc; i++)
            {
                float a = Mathf.PI * (1f - i / (float)arc);
                profile[i] = new Vector2(Mathf.Cos(a) * (W / 2f + 0.0025f), Mathf.Sin(a) * 0.0105f);
                colours[i] = Color.Lerp(Pal.Shade(roof, 0.8f), Pal.Shade(roof, 1.15f), Mathf.Sin(a));
            }
            b.AddExtrusion(path, right, up, profile, colours, false);
            foreach (int s in new[] { -1, 1 })
            {
                var centre = new Vector3(0f, y1, s * (L / 2f + 0.003f));
                for (int i = 0; i < arc; i++)
                    b.AddTriangleOutward(centre, centre + (Vector3)profile[i], centre + (Vector3)profile[i + 1], new Vector3(0f, y1, 0f), roof);
            }
            foreach (float z in new[] { -0.035f, 0f, 0.035f })
                b.AddCylinder(new Vector3(0f, y1 + 0.0098f, z), 0.0022f, 0.0018f, 0.003f, 8, Pal.Shade(roof, 0.7f), roof, true);
            // Bogie frames, buffers, couplings.
            foreach (float z in new[] { -0.038f, 0.038f })
            {
                b.AddBox(new Vector3(0f, 0.0085f, z), new Vector3(0.033f, 0.006f, 0.032f), black);
                foreach (int s in new[] { -1, 1 })
                    b.AddBox(new Vector3(s * 0.0175f, 0.0095f, z), new Vector3(0.0018f, 0.0045f, 0.026f), Pal.Shade(black, 1.5f));
            }
            foreach (int e in new[] { -1, 1 })
            foreach (int s in new[] { -1, 1 })
            {
                CylinderZ(b, new Vector3(s * 0.016f, 0.0155f, e > 0 ? L / 2f : -L / 2f - 0.003f), 0.0014f, 0.0014f, 0.003f, 8, black, black);
                CylinderZ(b, new Vector3(s * 0.016f, 0.0155f, e > 0 ? L / 2f + 0.003f : -L / 2f - 0.0038f), 0.0026f, 0.0026f, 0.0008f, 10, brass, brass);
            }
            return b.ToMesh("Coach" + variant + "-" + livery);
        }

        private static Mesh BuildAxle(float r, Color red)
        {
            var b = new MeshBuilder(); var black = Pal.TrainBlack; var brass = Pal.Brass;
            const float t = 0.0032f, rail = Gauge / 2f;
            b.Push(new Vector3(-rail, 0f, 0f), AlongX);
            b.AddCylinder(Vector3.zero, 0.0011f, 0.0011f, Gauge, 6, black, black, false);
            b.Pop();
            foreach (int s in new[] { -1, 1 })
            {
                // Flange (inside), tyre, wheel centre, hub and spokes on the outer face.
                float inner = s * (rail - t * 0.5f), outer = s * (rail + t * 0.5f);
                float x0 = Mathf.Min(inner, outer);
                b.Push(new Vector3(s < 0 ? -rail + t * 0.5f - 0.0008f : rail - t * 0.5f, 0f, 0f), AlongX);
                b.AddCylinder(Vector3.zero, r * 1.13f, r * 1.13f, 0.0008f, 16, black, black, true, true);
                b.Pop();
                b.Push(new Vector3(x0, 0f, 0f), AlongX);
                b.AddCylinder(Vector3.zero, r, r, t, 16, black, Pal.Shade(black, 1.3f), true, true);
                b.Pop();
                float face = s * (rail + t * 0.5f);
                b.Push(new Vector3(s < 0 ? face - 0.0005f : face, 0f, 0f), AlongX);
                b.AddCylinder(Vector3.zero, r * 0.84f, r * 0.84f, 0.0005f, 16, red, red, true, true);
                b.Pop();
                b.Push(new Vector3(s < 0 ? face - 0.0014f : face + 0.0005f, 0f, 0f), AlongX);
                b.AddCylinder(Vector3.zero, r * 0.24f, r * 0.2f, 0.0009f, 10, brass, brass, true, true);
                b.Pop();
                int spokes = r > 0.009f ? 10 : 6;
                for (int i = 0; i < spokes; i++)
                {
                    float a = i * 360f / spokes;
                    b.Push(new Vector3(face + s * 0.0003f, 0f, 0f), Quaternion.Euler(a, 0f, 0f));
                    b.AddBox(new Vector3(0f, r * 0.5f, 0f), new Vector3(0.0006f, r * 0.62f, r * 0.13f), Pal.Shade(red, 0.75f));
                    b.Pop();
                }
                // Crank pin (driving wheels).
                if (r > 0.009f) b.AddBox(new Vector3(face + s * 0.0008f, r * 0.45f, 0f), new Vector3(0.0012f, 0.0016f, 0.0016f), brass);
            }
            return b.ToMesh("Axle" + r.ToString("0.0000"));
        }

        /// <summary>Goods wagons: 0 open wagon with logs, 1 covered van, 2 tank wagon, 3 open wagon with coal.</summary>
        private static Mesh BuildWagon(int variant)
        {
            var b = new MeshBuilder();
            var black = Pal.TrainBlack; var brass = Pal.Brass;
            const float L = 0.084f, W = 0.042f;
            // Underframe, buffers, axle boxes.
            b.AddBox(new Vector3(0f, 0.0135f, 0f), new Vector3(0.034f, 0.005f, L), black);
            foreach (int e in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(0f, 0.0155f, e * (L / 2f + 0.001f)), new Vector3(0.044f, 0.007f, 0.002f), Pal.Hex("#5A3826"));
                foreach (int s in new[] { -1, 1 })
                {
                    CylinderZ(b, new Vector3(s * 0.015f, 0.0155f, e > 0 ? L / 2f + 0.002f : -L / 2f - 0.005f), 0.0013f, 0.0013f, 0.003f, 8, black, black);
                    CylinderZ(b, new Vector3(s * 0.015f, 0.0155f, e > 0 ? L / 2f + 0.005f : -L / 2f - 0.0058f), 0.0024f, 0.0024f, 0.0008f, 10, brass, brass);
                    b.AddBox(new Vector3(s * 0.0172f, 0.0085f, e * 0.026f), new Vector3(0.0022f, 0.006f, 0.008f), Pal.Shade(black, 1.5f));
                }
            }
            switch (variant)
            {
                case 0:
                case 3:
                {
                    // Open wagon: plank sides with iron strapping.
                    var plank = variant == 0 ? Pal.Hex("#9A6A42") : Pal.Hex("#6E7378");
                    b.AddBox(new Vector3(0f, 0.0175f, 0f), new Vector3(W, 0.003f, L), Pal.Shade(plank, 0.8f));
                    foreach (int s in new[] { -1, 1 })
                    {
                        b.AddBox(new Vector3(s * (W / 2f - 0.0012f), 0.0255f, 0f), new Vector3(0.0024f, 0.014f, L), plank, Pal.Shade(plank, 1.15f), plank);
                        b.AddBox(new Vector3(0f, 0.0255f, s * (L / 2f - 0.0012f)), new Vector3(W, 0.014f, 0.0024f), plank, Pal.Shade(plank, 1.15f), plank);
                        for (int k = -2; k <= 2; k++)
                            b.AddBox(new Vector3(s * (W / 2f + 0.0001f), 0.0255f, k * 0.018f), new Vector3(0.0006f, 0.014f, 0.0016f), black);
                        for (int r = 0; r < 3; r++)
                            b.AddBox(new Vector3(s * (W / 2f + 0.0002f), 0.0205f + r * 0.0048f, 0f), new Vector3(0.0003f, 0.0004f, L), Pal.Shade(plank, 0.7f));
                    }
                    if (variant == 0)
                    {
                        // A load of logs, chained down.
                        var rng = new Rng(17);
                        for (int row = 0; row < 2; row++)
                        for (int k = 0; k < 4 - row; k++)
                        {
                            float x = (k - (3 - row) / 2f) * 0.0092f, y = 0.0235f + row * 0.0082f;
                            float r = 0.0046f * rng.Range(0.85f, 1.1f);
                            CylinderZ(b, new Vector3(x, y, -L / 2f + 0.003f), r, r, L - 0.006f, 8, Pal.Bark, Pal.Shade(Pal.Bark, 1.15f));
                            b.Push(new Vector3(x, y, L / 2f - 0.0028f), Quaternion.Euler(90f, 0f, 0f));
                            b.AddDisc(Vector3.zero, r * 0.92f, 8, Pal.Hex("#D9B27A"), true);
                            b.Pop();
                            b.Push(new Vector3(x, y, -L / 2f + 0.0028f), Quaternion.Euler(-90f, 0f, 0f));
                            b.AddDisc(Vector3.zero, r * 0.92f, 8, Pal.Hex("#D9B27A"), true);
                            b.Pop();
                        }
                        foreach (float z in new[] { -0.022f, 0.022f })
                            b.AddBox(new Vector3(0f, 0.0365f, z), new Vector3(W * 0.82f, 0.0007f, 0.0012f), Pal.Hex("#B9BCC2"));
                    }
                    else
                        b.AddBlob(new Vector3(0f, 0.029f, 0f), new Vector3(W * 0.45f, 0.006f, L * 0.46f), 2, 0.35f, 9, (d, _) => Pal.Hex("#1E1E22"), 9f);
                    break;
                }
                case 1:
                {
                    // Covered van: planked body, sliding door, arched white roof.
                    var body = Pal.Hex("#8A3A2A");
                    b.AddBox(new Vector3(0f, 0.034f, 0f), new Vector3(W, 0.036f, L), body, body, black);
                    foreach (int s in new[] { -1, 1 })
                    {
                        for (int k = -3; k <= 3; k++)
                            b.AddBox(new Vector3(s * (W / 2f + 0.0002f), 0.034f, k * 0.0115f), new Vector3(0.0004f, 0.034f, 0.0006f), Pal.Shade(body, 0.75f));
                        b.AddBox(new Vector3(s * (W / 2f + 0.0006f), 0.033f, 0f), new Vector3(0.0008f, 0.028f, 0.022f), Pal.Shade(body, 1.12f));
                        b.AddBox(new Vector3(s * (W / 2f + 0.0011f), 0.033f, 0f), new Vector3(0.0006f, 0.0006f, 0.024f), black);
                        b.AddBox(new Vector3(s * (W / 2f + 0.0011f), 0.033f, 0f), new Vector3(0.0006f, 0.026f, 0.0006f), black);
                    }
                    var path = new[] { new Vector3(0f, 0.052f, -L / 2f - 0.002f), new Vector3(0f, 0.052f, L / 2f + 0.002f) };
                    const int arc = 8;
                    var profile = new Vector2[arc + 1]; var colours = new Color[arc + 1];
                    for (int i = 0; i <= arc; i++)
                    {
                        float a = Mathf.PI * (1f - i / (float)arc);
                        profile[i] = new Vector2(Mathf.Cos(a) * (W / 2f + 0.002f), Mathf.Sin(a) * 0.007f);
                        colours[i] = Color.Lerp(Pal.Hex("#C9C6BE"), Pal.Hex("#F2EEE4"), Mathf.Sin(a));
                    }
                    b.AddExtrusion(path, new[] { Vector3.right, Vector3.right }, new[] { Vector3.up, Vector3.up }, profile, colours, false);
                    foreach (int e in new[] { -1, 1 })
                    {
                        var centre = new Vector3(0f, 0.052f, e * (L / 2f + 0.002f));
                        for (int i = 0; i < arc; i++)
                            b.AddTriangleOutward(centre, centre + (Vector3)profile[i], centre + (Vector3)profile[i + 1], new Vector3(0f, 0.052f, 0f), Pal.Hex("#E0DCD2"));
                    }
                    break;
                }
                default:
                {
                    // Tank wagon: a cream barrel with a red band, a dome and ladders.
                    var tank = Pal.Hex("#EDE3C8");
                    b.AddBox(new Vector3(0f, 0.0185f, 0f), new Vector3(0.03f, 0.004f, L * 0.9f), black);
                    CylinderZ(b, new Vector3(0f, 0.0335f, -L / 2f + 0.004f), 0.0145f, 0.0145f, L - 0.008f, 18, tank, tank);
                    CylinderZ(b, new Vector3(0f, 0.0335f, -0.006f), 0.0149f, 0.0149f, 0.012f, 18, Pal.TrainRed, Pal.TrainRed, false);
                    foreach (int e in new[] { -1, 1 })
                        b.AddBlob(new Vector3(0f, 0.0335f, e * (L / 2f - 0.004f)), new Vector3(0.0145f, 0.0145f, 0.004f), 2, 0f, 30 + e, (d, _) => Pal.Shade(tank, 0.92f));
                    b.AddCylinder(new Vector3(0f, 0.046f, 0.018f), 0.0055f, 0.005f, 0.005f, 12, tank, Pal.Shade(tank, 1.05f), true);
                    foreach (float z in new[] { -0.028f, 0.028f })
                        b.AddBox(new Vector3(0f, 0.0335f, z), new Vector3(0.0305f, 0.0015f, 0.0012f), black);
                    break;
                }
            }
            return b.ToMesh("Wagon" + variant);
        }

        /// <summary>A rounded little railcar with a driving cab at each end, so it simply reverses at the buffers.</summary>
        private static Mesh BuildRailcar(int livery)
        {
            var b = new MeshBuilder();
            var lower = LiveryColour(livery); var upper = Pal.CoachCream; var roof = Pal.CoachRoof; var black = Pal.TrainBlack; var brass = Pal.Brass;
            const float L = 0.13f, W = 0.044f, y0 = 0.018f, yMid = 0.035f, y1 = 0.058f;
            b.AddBox(new Vector3(0f, 0.014f, 0f), new Vector3(0.03f, 0.006f, L - 0.02f), black);
            b.AddBox(new Vector3(0f, (y0 + yMid) / 2f, 0f), new Vector3(W, yMid - y0, L), lower, lower, black, false);
            b.AddBox(new Vector3(0f, (yMid + y1) / 2f, 0f), new Vector3(W - 0.001f, y1 - yMid, L - 0.001f), upper, upper, upper);
            b.AddBox(new Vector3(0f, yMid, 0f), new Vector3(W + 0.0008f, 0.0011f, L + 0.0008f), brass);
            // Side windows.
            for (int i = 0; i < 6; i++)
            {
                float z = -L / 2f + 0.022f + i * (L - 0.044f) / 5f;
                foreach (int s in new[] { -1, 1 })
                    b.AddFace(new Vector3(s * (W / 2f + 0.0002f), 0.047f, z), new Vector3(s, 0f, 0f), Vector3.up, 0.0062f, 0.0065f, Pal.Glow(Pal.WindowGlow));
            }
            // Rounded cab ends: a sloped nose, wide front windows, a headlamp and a whistle.
            foreach (int e in new[] { -1, 1 })
            {
                var end = new Vector3(0f, 0f, e * L / 2f);
                b.AddBlob(end + new Vector3(0f, 0.027f, 0f), new Vector3(W / 2f, 0.0095f, 0.012f), 2, 0f, 40 + e, (d, _) => lower);
                b.AddBlob(end + new Vector3(0f, 0.046f, -e * 0.002f), new Vector3(W / 2f - 0.0006f, 0.012f, 0.009f), 2, 0f, 42 + e, (d, _) => upper);
                b.AddFace(end + new Vector3(0f, 0.048f, e * 0.0068f), new Vector3(0f, 0.25f, e).normalized, Vector3.up, 0.015f, 0.0055f, Pal.Glow(Pal.Hex("#CFE6F2")));
                b.AddFace(end + new Vector3(0f, 0.024f, e * 0.0122f), new Vector3(0f, 0f, e), Vector3.up, 0.0035f, 0.0028f, Pal.Glow(Pal.Hex("#FFF2C8")));
                foreach (int s in new[] { -1, 1 })
                {
                    CylinderZ(b, new Vector3(s * 0.016f, 0.0155f, e > 0 ? L / 2f + 0.006f : -L / 2f - 0.009f), 0.0013f, 0.0013f, 0.003f, 8, black, black);
                    CylinderZ(b, new Vector3(s * 0.016f, 0.0155f, e > 0 ? L / 2f + 0.009f : -L / 2f - 0.0098f), 0.0024f, 0.0024f, 0.0008f, 10, brass, brass);
                }
            }
            // Roof with a radiator housing and exhaust.
            var path = new[] { new Vector3(0f, y1, -L / 2f), new Vector3(0f, y1, L / 2f) };
            const int arc = 9;
            var profile = new Vector2[arc + 1]; var colours = new Color[arc + 1];
            for (int i = 0; i <= arc; i++)
            {
                float a = Mathf.PI * (1f - i / (float)arc);
                profile[i] = new Vector2(Mathf.Cos(a) * (W / 2f + 0.002f), Mathf.Sin(a) * 0.008f);
                colours[i] = Color.Lerp(Pal.Shade(roof, 0.8f), Pal.Shade(roof, 1.15f), Mathf.Sin(a));
            }
            b.AddExtrusion(path, new[] { Vector3.right, Vector3.right }, new[] { Vector3.up, Vector3.up }, profile, colours, false);
            b.AddBox(new Vector3(0f, y1 + 0.009f, 0f), new Vector3(0.022f, 0.004f, 0.03f), Pal.Shade(roof, 0.75f));
            b.AddCylinder(new Vector3(0.006f, y1 + 0.011f, 0.009f), 0.0016f, 0.0014f, 0.006f, 8, black, black, true);
            // Bogies.
            foreach (float z in new[] { -0.047f, 0.047f })
                b.AddBox(new Vector3(0f, 0.0085f, z), new Vector3(0.033f, 0.006f, 0.032f), black);
            return b.ToMesh("Railcar" + livery);
        }

        private static Mesh BuildRods()
        {
            var b = new MeshBuilder();
            float len = Loco.RodTo - Loco.RodFrom + 0.004f;
            foreach (int s in new[] { -1, 1 })
            {
                float x = s * (Gauge / 2f + 0.0035f);
                b.AddBox(new Vector3(x, 0f, (Loco.RodFrom + Loco.RodTo) / 2f), new Vector3(0.0009f, 0.0018f, len), Pal.Hex("#B9BCC2"));
                foreach (float z in new[] { Loco.RodFrom, (Loco.RodFrom + Loco.RodTo) / 2f, Loco.RodTo })
                    b.AddBox(new Vector3(x, 0f, z), new Vector3(0.0011f, 0.0026f, 0.0026f), Pal.Hex("#D5D7DB"));
            }
            return b.ToMesh("SideRods");
        }
    }
}
