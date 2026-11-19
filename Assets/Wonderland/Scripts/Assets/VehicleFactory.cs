using UnityEngine;

namespace Wonderland
{
    public enum VehicleKind { Car = 0, Motorbike = 1, Bicycle = 2, Ambulance = 3, Police = 4, FireEngine = 5, Taxi = 6, Van = 7, Bus = 8, Truck = 9, Tanker = 10, Flatbed = 11, Tractor = 12 }

    /// <summary>
    /// Storybook road vehicles at the villagers' scale (a car is 5 cm long): rounded cars in four colours, a motorbike and
    /// a bicycle with their riders, an ambulance, a police car and a fire engine with a ladder. Front faces +Z, the road
    /// at y = 0. Emergency vehicles' blue lights are separate little meshes that blink.
    /// </summary>
    public static class VehicleFactory
    {
        private static readonly Mesh[] meshes = new Mesh[64];   // four colours of each kind

        /// <summary>The vehicles a player can put on the road (they drive about the town).</summary>
        public static readonly VehicleKind[] Placeable = { VehicleKind.Car, VehicleKind.Taxi, VehicleKind.Van, VehicleKind.Bus, VehicleKind.Truck, VehicleKind.Tanker };

        /// <summary>Big vehicles keep to the slow lane of a highway.</summary>
        public static bool Heavy(VehicleKind k) => k is VehicleKind.Bus or VehicleKind.Truck or VehicleKind.Tanker or VehicleKind.FireEngine or VehicleKind.Flatbed;
        private static Mesh beacon;

        public static float Length(VehicleKind k) => k switch
        {
            VehicleKind.Motorbike => 0.026f, VehicleKind.Bicycle => 0.022f, VehicleKind.FireEngine => 0.075f, VehicleKind.Ambulance => 0.058f,
            VehicleKind.Van => 0.056f, VehicleKind.Bus => 0.1f, VehicleKind.Truck => 0.088f, VehicleKind.Tanker => 0.088f, VehicleKind.Flatbed => 0.088f, VehicleKind.Tractor => 0.034f, _ => 0.05f,
        };

        public static Mesh Get(VehicleKind k, int colour = 0) => Get(k, colour, 0);

        private static readonly System.Collections.Generic.Dictionary<int, Mesh> levels = new();

        /// <summary>A vehicle at a level of detail (see <see cref="MeshBuilder.Detail"/>).</summary>
        public static Mesh Get(VehicleKind k, int colour, int detail)
        {
            int key = (int)k * 4 + (k is VehicleKind.Car or VehicleKind.Van or VehicleKind.Truck or VehicleKind.Flatbed ? colour % 4 : 0);
            if (detail == 0) return meshes[key] ??= Build(k, colour % 4);
            int at = key * 8 + detail + 1;
            if (!levels.TryGetValue(at, out var mesh)) { levels[at] = mesh = MeshBuilder.At(detail, 1f, () => Build(k, colour % 4)); mesh.name += " detail " + detail; }
            return mesh;
        }

        /// <summary>Where the blue lights sit (local), for vehicles that have them.</summary>
        public static Vector3[] Beacons(VehicleKind k) => k switch
        {
            VehicleKind.Ambulance => new[] { new Vector3(-0.007f, 0.031f, 0.016f), new Vector3(0.007f, 0.031f, 0.016f) },
            VehicleKind.Police => new[] { new Vector3(-0.005f, 0.0232f, -0.002f), new Vector3(0.005f, 0.0232f, -0.002f) },
            VehicleKind.FireEngine => new[] { new Vector3(-0.008f, 0.03f, 0.026f), new Vector3(0.008f, 0.03f, 0.026f) },
            _ => System.Array.Empty<Vector3>(),
        };

        public static Mesh Beacon()
        {
            if (beacon != null) return beacon;
            var b = new MeshBuilder();
            b.AddBlob(Vector3.zero, new Vector3(0.0026f, 0.0018f, 0.0026f), 1, 0f, 3, (_, __) => Pal.Glow(Pal.Hex("#3D7BFF")));
            return beacon = b.ToMesh("Beacon");
        }

        private static readonly Color[] CarColours = { Pal.Hex("#C8453A"), Pal.Hex("#4A7FB8"), Pal.Hex("#E2B04A"), Pal.Hex("#5E9A63") };

        private static Mesh Build(VehicleKind k, int colour)
        {
            var b = new MeshBuilder();
            switch (k)
            {
                case VehicleKind.Car: Car(b, CarColours[colour], Pal.Hex("#F7F1E6")); break;
                case VehicleKind.Police: Car(b, Pal.Hex("#F4F2EC"), Pal.Hex("#2E4A7A"), true); break;
                case VehicleKind.Ambulance: Van(b, Pal.Hex("#F4F2EC"), Pal.Hex("#E0352B"), 0.058f, false); break;
                case VehicleKind.FireEngine: Van(b, Pal.Hex("#D7382E"), Pal.Hex("#F7F1E6"), 0.075f, true); break;
                case VehicleKind.Motorbike: Bike(b, true); break;
                case VehicleKind.Taxi: Car(b, Pal.Hex("#F2C12E"), Pal.TrainBlack); TaxiSign(b); break;
                case VehicleKind.Van: Van(b, Pal.Hex("#F4F2EC"), CarColours[colour], 0.056f, false, false); break;
                case VehicleKind.Bus: Bus(b); break;
                case VehicleKind.Truck: Lorry(b, CarColours[colour], false); break;
                case VehicleKind.Tanker: Lorry(b, Pal.Hex("#3E6FB0"), true); break;
                case VehicleKind.Flatbed: Flatbed(b, CarColours[colour]); break;
                case VehicleKind.Tractor: Tractor(b); break;
                default: Bike(b, false); break;
            }
            return b.ToMesh(k + "_" + colour);
        }

        private static void Wheels(MeshBuilder b, float halfW, float[] zs, float r)
        {
            foreach (float z in zs)
            foreach (int s in new[] { -1, 1 })
            {
                b.Push(new Vector3(s * halfW, r, z), Quaternion.Euler(0f, 0f, 90f));
                b.AddCylinder(new Vector3(0f, -0.0022f, 0f), r, r, 0.0044f, 10, Pal.TrainBlack, Pal.TrainBlack, true, true);
                b.AddDisc(new Vector3(0f, s > 0 ? -0.0023f : 0.0023f, 0f), r * 0.5f, 8, Pal.Hex("#C9CDD2"), s < 0);
                b.Pop();
            }
        }

        private static void Lights(MeshBuilder b, float halfW, float y, float front, float back)
        {
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBlob(new Vector3(s * halfW * 0.7f, y, front), Vector3.one * 0.0015f, 0, 0f, 1, (_, __) => Pal.Glow(Pal.Hex("#FFF2C8")));
                b.AddBox(new Vector3(s * halfW * 0.72f, y, back), new Vector3(0.003f, 0.0018f, 0.0008f), Pal.Glow(Pal.Hex("#FF3B30")));
            }
        }

        private static void Car(MeshBuilder b, Color body, Color trim, bool police = false)
        {
            const float L = 0.05f, W = 0.021f;
            var glass = Pal.Hex("#5A7090");
            Wheels(b, W / 2 - 0.0012f, new[] { -0.015f, 0.016f }, 0.0045f);
            b.AddBox(new Vector3(0f, 0.0085f, 0f), new Vector3(W, 0.008f, L), body, Pal.Shade(body, 1.1f), Pal.Shade(body, 0.7f));
            b.AddBlob(new Vector3(0f, 0.0128f, 0.017f), new Vector3(W * 0.48f, 0.0026f, 0.009f), 1, 0f, 2, (_, __) => body);   // a rounded bonnet
            // The cabin: glass all round under a roof.
            b.AddBox(new Vector3(0f, 0.0165f, -0.003f), new Vector3(W - 0.003f, 0.0075f, 0.024f), glass, glass, glass);
            b.AddBox(new Vector3(0f, 0.0205f, -0.004f), new Vector3(W - 0.002f, 0.0013f, 0.02f), body);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 - 0.0016f), 0.0165f, -0.003f), new Vector3(0.0009f, 0.0075f, 0.0012f), body);
            b.AddBox(new Vector3(0f, 0.0065f, L / 2 + 0.0003f), new Vector3(W - 0.002f, 0.0022f, 0.0012f), Pal.Hex("#C9CDD2"));   // bumpers
            b.AddBox(new Vector3(0f, 0.0065f, -L / 2 - 0.0003f), new Vector3(W - 0.002f, 0.0022f, 0.0012f), Pal.Hex("#C9CDD2"));
            b.AddBox(new Vector3(0f, 0.0105f, 0f), new Vector3(W + 0.0004f, 0.0012f, L * 0.8f), trim);   // a stripe
            Lights(b, W / 2, 0.009f, L / 2 + 0.0004f, -L / 2 - 0.0004f);
            if (police)
            {
                b.AddBox(new Vector3(0f, 0.0218f, -0.002f), new Vector3(0.016f, 0.0016f, 0.004f), Pal.TrainBlack);
                b.AddBox(new Vector3(0f, 0.0115f, 0f), new Vector3(W + 0.0006f, 0.003f, L * 0.55f), trim);
            }
        }

        private static void Van(MeshBuilder b, Color body, Color trim, float L, bool fire, bool cross = true)
        {
            const float W = 0.024f;
            var glass = Pal.Hex("#5A7090");
            Wheels(b, W / 2 - 0.0012f, fire ? new[] { -0.026f, -0.013f, 0.024f } : new[] { -0.019f, 0.019f }, 0.005f);
            float H = fire ? 0.022f : 0.026f;
            b.AddBox(new Vector3(0f, 0.005f + H / 2, -0.005f), new Vector3(W, H, L - 0.01f), body, Pal.Shade(body, 1.08f), Pal.Shade(body, 0.7f));
            // Cab at the front with a windscreen.
            b.AddBox(new Vector3(0f, 0.012f, L / 2 - 0.007f), new Vector3(W, 0.014f, 0.014f), body);
            b.AddBox(new Vector3(0f, 0.016f, L / 2 - 0.0003f), new Vector3(W - 0.004f, 0.006f, 0.0008f), glass);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 + 0.0002f), 0.016f, L / 2 - 0.007f), new Vector3(0.0006f, 0.005f, 0.008f), glass);
            b.AddBox(new Vector3(0f, 0.012f, -0.005f), new Vector3(W + 0.0006f, 0.003f, L - 0.012f), trim);   // the band along the side
            Lights(b, W / 2, 0.009f, L / 2 + 0.0004f, -L / 2 + 0.0046f);
            if (fire)
            {
                // A ladder on top and rolled hoses.
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.005f, H + 0.0072f, -0.006f), new Vector3(0.0012f, 0.0012f, L - 0.016f), Pal.Hex("#C9CDD2"));
                for (int i = 0; i < 12; i++) b.AddBox(new Vector3(0f, H + 0.0072f, -0.034f + i * 0.0052f), new Vector3(0.01f, 0.0008f, 0.0008f), Pal.Hex("#C9CDD2"));
                foreach (int s in new[] { -1, 1 })
                {
                    b.Push(new Vector3(s * (W / 2 + 0.0012f), 0.013f, -0.02f), Quaternion.Euler(0f, 0f, 90f));
                    b.AddCylinder(new Vector3(0f, -0.0012f, 0f), 0.004f, 0.004f, 0.0024f, 10, Pal.Hex("#E2B04A"), Pal.Hex("#E2B04A"), true, true);
                    b.Pop();
                }
            }
            else if (cross)
            {
                // A red cross on the side and back.
                foreach (int s in new[] { -1, 1 })
                {
                    b.AddBox(new Vector3(s * (W / 2 + 0.0005f), 0.02f, -0.008f), new Vector3(0.0006f, 0.0026f, 0.008f), trim);
                    b.AddBox(new Vector3(s * (W / 2 + 0.0005f), 0.02f, -0.008f), new Vector3(0.0006f, 0.008f, 0.0026f), trim);
                }
            }
        }

        private static void TaxiSign(MeshBuilder b)
        {
            b.AddBox(new Vector3(0f, 0.0232f, -0.004f), new Vector3(0.011f, 0.004f, 0.0035f), Pal.Hex("#FFF7D6"));
            foreach (int s in new[] { -1, 1 }) Lettering.Add(b, "TAXI", new Vector3(0f, 0.0232f, -0.004f + s * 0.0018f), new Vector3(0f, 0f, s), 0.0026f, 0.0003f, Pal.Glow(Pal.Hex("#2A2A2A")));
            // Chequers along the side.
            for (int i = 0; i < 8; i++) foreach (int s in new[] { -1, 1 })
                b.AddBox(new Vector3(s * 0.0107f, 0.0112f + (i % 2) * 0.0012f, -0.017f + i * 0.0045f), new Vector3(0.0004f, 0.0012f, 0.0022f), Pal.TrainBlack);
        }

        /// <summary>A single-deck town bus: long, red, windows all along, a destination board.</summary>
        private static void Bus(MeshBuilder b)
        {
            const float L = 0.1f, W = 0.026f, H = 0.03f;
            var red = Pal.Hex("#C8352B"); var glass = Pal.Hex("#5A7090"); var cream = Pal.Hex("#F3E9D2");
            Wheels(b, W / 2 - 0.0012f, new[] { -0.032f, 0.03f }, 0.0055f);
            b.AddBox(new Vector3(0f, 0.005f + H / 2, 0f), new Vector3(W, H, L), red, Pal.Hex("#D9D2C6"), Pal.Shade(red, 0.7f));
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(s * (W / 2 + 0.0002f), 0.0235f, 0f), new Vector3(0.0006f, 0.008f, L - 0.012f), glass);
                for (int i = 0; i < 9; i++) b.AddBox(new Vector3(s * (W / 2 + 0.0005f), 0.0235f, -L / 2 + 0.008f + i * 0.0105f), new Vector3(0.0004f, 0.0085f, 0.0012f), red);   // window pillars
                b.AddBox(new Vector3(s * (W / 2 + 0.0004f), 0.0145f, 0f), new Vector3(0.0005f, 0.0025f, L - 0.006f), cream);
            }
            b.AddBox(new Vector3(0f, 0.022f, L / 2 + 0.0002f), new Vector3(W - 0.003f, 0.013f, 0.0006f), glass);
            b.AddBox(new Vector3(0f, 0.0315f, L / 2 + 0.0003f), new Vector3(W - 0.006f, 0.004f, 0.0008f), Pal.TrainBlack);
            Lettering.Add(b, "TOWN", new Vector3(0f, 0.0315f, L / 2 + 0.0008f), Vector3.forward, 0.0026f, 0.0003f, Pal.Glow(Pal.Hex("#FFB840")));
            Lights(b, W / 2, 0.009f, L / 2 + 0.0004f, -L / 2 - 0.0004f);
        }

        /// <summary>A lorry: a cab and a box body (or a tank) on a long chassis.</summary>
        private static void Lorry(MeshBuilder b, Color body, bool tank)
        {
            const float L = 0.088f, W = 0.025f;
            var glass = Pal.Hex("#5A7090"); var chassis = Pal.Hex("#2E2E34");
            Wheels(b, W / 2 - 0.0012f, new[] { -0.03f, -0.019f, 0.03f }, 0.0055f);
            b.AddBox(new Vector3(0f, 0.0065f, 0f), new Vector3(W - 0.006f, 0.004f, L - 0.004f), chassis);
            // The cab at the front.
            b.AddBox(new Vector3(0f, 0.017f, L / 2 - 0.009f), new Vector3(W, 0.02f, 0.018f), body, Pal.Shade(body, 1.1f), Pal.Shade(body, 0.7f));
            b.AddBox(new Vector3(0f, 0.021f, L / 2 + 0.0002f), new Vector3(W - 0.004f, 0.007f, 0.0008f), glass);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 + 0.0002f), 0.021f, L / 2 - 0.007f), new Vector3(0.0006f, 0.006f, 0.008f), glass);
            Lights(b, W / 2, 0.01f, L / 2 + 0.0004f, -L / 2 - 0.0004f);
            if (tank)
            {
                b.Push(new Vector3(0f, 0.019f, -0.012f), Quaternion.Euler(90f, 0f, 0f));
                b.AddCylinder(new Vector3(0f, -0.029f, 0f), 0.0105f, 0.0105f, 0.058f, 14, Pal.Hex("#D9DDE2"), Pal.Hex("#F2F4F6"), true, true);
                b.Pop();
                b.AddBox(new Vector3(0f, 0.0292f, -0.012f), new Vector3(0.0016f, 0.0016f, 0.05f), Pal.Hex("#9AA0A8"));   // walkway on top
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.0106f, 0.019f, -0.012f), new Vector3(0.0004f, 0.0035f, 0.048f), body);
            }
            else
            {
                var box = Pal.Hex("#F2EFE8");
                b.AddBox(new Vector3(0f, 0.0215f, -0.011f), new Vector3(W + 0.001f, 0.026f, 0.064f), box, box, Pal.Shade(box, 0.8f));
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 + 0.0008f), 0.0215f, -0.011f), new Vector3(0.0004f, 0.008f, 0.05f), body);   // a coloured band
            }
        }

        /// <summary>An open lorry: a cab and a flat bed with low sides, so what it carries shows (see <see cref="Load"/>).</summary>
        private static void Flatbed(MeshBuilder b, Color body)
        {
            const float L = 0.088f, W = 0.025f;
            var glass = Pal.Hex("#5A7090"); var chassis = Pal.Hex("#2E2E34"); var wood = Pal.Hex("#9A6B42");
            Wheels(b, W / 2 - 0.0012f, new[] { -0.03f, -0.019f, 0.03f }, 0.0055f);
            b.AddBox(new Vector3(0f, 0.0065f, 0f), new Vector3(W - 0.006f, 0.004f, L - 0.004f), chassis);
            b.AddBox(new Vector3(0f, 0.017f, L / 2 - 0.009f), new Vector3(W, 0.02f, 0.018f), body, Pal.Shade(body, 1.1f), Pal.Shade(body, 0.7f));
            b.AddBox(new Vector3(0f, 0.021f, L / 2 + 0.0002f), new Vector3(W - 0.004f, 0.007f, 0.0008f), glass);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 + 0.0002f), 0.021f, L / 2 - 0.007f), new Vector3(0.0006f, 0.006f, 0.008f), glass);
            Lights(b, W / 2, 0.01f, L / 2 + 0.0004f, -L / 2 - 0.0004f);
            // The bed: planks, low sides and a headboard behind the cab.
            b.AddBox(new Vector3(0f, BedTop - 0.0009f, BedCentre), new Vector3(W, 0.0018f, BedLength), wood, Pal.Shade(wood, 1.1f), Pal.Shade(wood, 0.75f));
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 - 0.0006f), BedTop + 0.0022f, BedCentre), new Vector3(0.0012f, 0.0044f, BedLength), body);
            b.AddBox(new Vector3(0f, BedTop + 0.0022f, BedCentre - BedLength / 2 + 0.0006f), new Vector3(W, 0.0044f, 0.0012f), body);
            b.AddBox(new Vector3(0f, BedTop + 0.006f, BedCentre + BedLength / 2 - 0.0008f), new Vector3(W, 0.012f, 0.0016f), Pal.Shade(body, 0.85f));
        }

        /// <summary>
        /// A little red tractor: big ridged back wheels, small front ones, a long bonnet with a chimney, an open seat
        /// (the farmer sits at <see cref="TractorSeat"/>) under a roll bar.
        /// </summary>
        private static void Tractor(MeshBuilder b)
        {
            var red = Pal.Hex("#C8392E"); var tyre = Pal.TrainBlack; var hub = Pal.Hex("#E8C24A"); var metal = Pal.Hex("#4A4A52");
            foreach (int s in new[] { -1, 1 })
            {
                b.Push(new Vector3(s * 0.0105f, 0.0085f, -0.007f), Quaternion.Euler(0f, 0f, 90f));
                b.AddCylinder(new Vector3(0f, -0.0028f, 0f), 0.0085f, 0.0085f, 0.0056f, 14, tyre, tyre, true, true);
                b.AddCylinder(new Vector3(0f, -0.003f, 0f), 0.0045f, 0.0045f, 0.006f, 10, hub, hub, true, true);
                b.Pop();
                b.Push(new Vector3(s * 0.008f, 0.0048f, 0.011f), Quaternion.Euler(0f, 0f, 90f));
                b.AddCylinder(new Vector3(0f, -0.0018f, 0f), 0.0048f, 0.0048f, 0.0036f, 12, tyre, tyre, true, true);
                b.AddCylinder(new Vector3(0f, -0.002f, 0f), 0.0024f, 0.0024f, 0.004f, 8, hub, hub, true, true);
                b.Pop();
                b.AddBox(new Vector3(s * 0.0105f, 0.0175f, -0.007f), new Vector3(0.0066f, 0.0012f, 0.017f), red);   // mudguards
            }
            b.AddBox(new Vector3(0f, 0.0105f, 0.007f), new Vector3(0.011f, 0.009f, 0.022f), red, Pal.Shade(red, 1.1f), Pal.Shade(red, 0.75f));   // bonnet
            b.AddBox(new Vector3(0f, 0.0105f, 0.0182f), new Vector3(0.0102f, 0.008f, 0.0008f), metal);   // grille
            b.AddBox(new Vector3(0f, 0.012f, -0.007f), new Vector3(0.012f, 0.005f, 0.012f), red);
            b.AddBox(new Vector3(0f, 0.016f, -0.009f), new Vector3(0.007f, 0.0014f, 0.006f), Pal.TrainBlack);   // seat
            b.AddBox(new Vector3(0f, 0.0195f, -0.0118f), new Vector3(0.007f, 0.006f, 0.0012f), Pal.TrainBlack);
            b.AddCylinder(new Vector3(0f, 0.015f, -0.0015f), 0.0006f, 0.0006f, 0.006f, 4, metal, metal, false);
            b.AddCylinder(new Vector3(0f, 0.021f, -0.0015f), 0.002f, 0.002f, 0.0006f, 8, Pal.TrainBlack, Pal.TrainBlack, true, true);   // steering wheel
            b.AddCylinder(new Vector3(0.0035f, 0.015f, 0.012f), 0.0008f, 0.0008f, 0.012f, 6, metal, Pal.TrainBlack, true, true);   // chimney
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.0072f, 0.0255f, -0.0125f), new Vector3(0.0012f, 0.019f, 0.0012f), metal);   // roll bar
            b.AddBox(new Vector3(0f, 0.0348f, -0.0125f), new Vector3(0.0156f, 0.0012f, 0.0012f), metal);
            foreach (int s in new[] { -1, 1 }) b.AddBlob(new Vector3(s * 0.0035f, 0.0128f, 0.0185f), Vector3.one * 0.0011f, 0, 0f, 60 + s, (_, __) => Pal.Glow(Pal.Hex("#FFF2C0")));
        }

        /// <summary>Where the farmer sits on the tractor (local, the figure's feet).</summary>
        public static readonly Vector3 TractorSeat = new(0f, 0.005f, -0.009f);

        /// <summary>The flat bed of a <see cref="VehicleKind.Flatbed"/>: its top, its middle (z) and its length.</summary>
        public const float BedTop = 0.0105f, BedCentre = -0.012f, BedLength = 0.06f;

        private static readonly Mesh[] loads = new Mesh[64];
        private static Mesh one;

        /// <summary>
        /// A load on a flat bed: <paramref name="crates"/> crates of produce, cardboard boxes of goods, logs, or mail sacks,
        /// two by three, centred on the bed (local to the lorry).
        /// </summary>
        public static Mesh Load(Cargo cargo, int crates)
        {
            int kind = cargo switch { Cargo.Goods => 1, Cargo.Timber => 2, Cargo.Mail => 3, _ => 0 };
            crates = Mathf.Clamp(crates, 0, 6);
            int key = kind * 8 + crates;
            if (loads[key] != null) return loads[key];
            var b = new MeshBuilder();
            if (kind == 2)
            {
                // Logs lie lengthwise, a pile that grows with the load.
                for (int i = 0; i < crates * 2; i++)
                {
                    int row = i < 5 ? 0 : i < 9 ? 1 : 2, inRow = i < 5 ? i : i < 9 ? i - 5 : i - 9, of = row == 0 ? 5 : row == 1 ? 4 : 3;
                    float x = (inRow - (of - 1) / 2f) * 0.0046f;
                    b.Push(new Vector3(x, BedTop + 0.0023f + row * 0.004f, BedCentre), Quaternion.Euler(90f, 0f, 0f));
                    b.AddCylinder(new Vector3(0f, -0.026f, 0f), 0.0023f, 0.0023f, 0.052f, 7, Pal.Hex("#8A5A34"), Pal.Hex("#D8B27A"), true, true);
                    b.Pop();
                }
            }
            else
                for (int i = 0; i < crates; i++)
                {
                    var at = new Vector3((i % 2 - 0.5f) * 0.011f, BedTop, BedCentre + BedLength / 2 - 0.011f - i / 2 * 0.0185f);
                    Crate(b, at, kind, i);
                }
            return loads[key] = b.ToMesh("Load");
        }

        private static readonly Mesh[] wagonLoads = new Mesh[8];

        /// <summary>Crates in an open goods wagon (up to six, two across and three along, on its floor; local to the wagon).</summary>
        public static Mesh WagonLoad(int crates)
        {
            crates = Mathf.Clamp(crates, 0, 6);
            if (wagonLoads[crates] != null) return wagonLoads[crates];
            var b = new MeshBuilder();
            for (int i = 0; i < crates; i++) Crate(b, new Vector3((i % 2 - 0.5f) * 0.0125f, 0.019f, (i / 2 - 1) * 0.024f), i % 3 == 2 ? 1 : 0, i);
            return wagonLoads[crates] = b.ToMesh("WagonLoad");
        }

        /// <summary>One crate (or box, or sack) on its own, its bottom at the origin: what is carried across when loading.</summary>
        public static Mesh OneCrate(Cargo cargo) => cargo == Cargo.Produce ? one ??= Single(0) : Single(cargo switch { Cargo.Goods => 1, Cargo.Timber => 2, _ => 3 });

        private static Mesh Single(int kind)
        {
            var b = new MeshBuilder();
            if (kind == 2) { b.Push(Vector3.up * 0.0023f, Quaternion.Euler(90f, 0f, 0f)); b.AddCylinder(new Vector3(0f, -0.012f, 0f), 0.0023f, 0.0023f, 0.024f, 7, Pal.Hex("#8A5A34"), Pal.Hex("#D8B27A"), true, true); b.Pop(); }
            else Crate(b, Vector3.zero, kind, 1);
            return b.ToMesh("Crate");
        }

        private static void Crate(MeshBuilder b, Vector3 at, int kind, int i)
        {
            switch (kind)
            {
                case 1:   // a cardboard box, taped
                    b.AddBox(at + new Vector3(0f, 0.0045f, 0f), new Vector3(0.0096f, 0.009f, 0.0096f), Pal.Hex("#C9A06A"), Pal.Hex("#D8B27A"), Pal.Hex("#A9804E"));
                    b.AddBox(at + new Vector3(0f, 0.00905f, 0f), new Vector3(0.0022f, 0.0002f, 0.0098f), Pal.Hex("#E8D9B0"));
                    break;
                case 3:   // a mail sack
                    b.AddBlob(at + new Vector3(0f, 0.004f, 0f), new Vector3(0.0045f, 0.004f, 0.004f), 1, 0.1f, 30 + i, (_, __) => Pal.Hex("#B89A6A"));
                    break;
                default:  // a wooden crate heaped with fruit and veg
                    var wood = Pal.Hex("#B07A45");
                    b.AddBox(at + new Vector3(0f, 0.0032f, 0f), new Vector3(0.0096f, 0.0064f, 0.0096f), wood, Pal.Shade(wood, 1.1f), Pal.Shade(wood, 0.7f));
                    var colours = new[] { "#D9473A", "#E8B84A", "#6FA84A", "#E07B39" };
                    for (int k = 0; k < 4; k++)
                        b.AddBlob(at + new Vector3((k % 2 - 0.5f) * 0.0044f, 0.0068f, (k / 2 - 0.5f) * 0.0044f), Vector3.one * 0.0019f, 0, 0f, 40 + i * 4 + k, (_, __) => Pal.Hex(colours[(i + k) % 4]));
                    break;
            }
        }

        private static void Bike(MeshBuilder b, bool motor)
        {
            var frame = motor ? Pal.Hex("#2A2A30") : Pal.Hex("#4A7FB8");
            foreach (float z in new[] { -0.0085f, 0.0085f })
            {
                b.Push(new Vector3(0f, motor ? 0.0048f : 0.0055f, z), Quaternion.Euler(0f, 0f, 90f));
                b.AddCylinder(new Vector3(0f, -0.0008f, 0f), motor ? 0.0048f : 0.0055f, motor ? 0.0048f : 0.0055f, 0.0016f, 12, Pal.TrainBlack, Pal.TrainBlack, true, true);
                b.Pop();
            }
            if (motor)
            {
                b.AddBox(new Vector3(0f, 0.0085f, 0f), new Vector3(0.006f, 0.005f, 0.014f), Pal.Hex("#C3312A"));
                b.AddBox(new Vector3(0f, 0.0115f, -0.004f), new Vector3(0.005f, 0.0015f, 0.008f), Pal.TrainBlack);
                b.AddBlob(new Vector3(0f, 0.011f, 0.0095f), Vector3.one * 0.0016f, 0, 0f, 1, (_, __) => Pal.Glow(Pal.Hex("#FFF2C8")));
            }
            else
            {
                b.Push(new Vector3(0f, 0.0075f, 0f), Quaternion.Euler(90f, 0f, 0f));
                b.AddCylinder(new Vector3(0f, -0.008f, 0f), 0.0005f, 0.0005f, 0.016f, 4, frame, frame, false);
                b.Pop();
                b.AddCylinder(new Vector3(0f, 0.0055f, -0.003f), 0.0005f, 0.0005f, 0.006f, 4, frame, frame, false);
            }
            b.AddBox(new Vector3(0f, motor ? 0.0128f : 0.0125f, 0.0082f), new Vector3(0.008f, 0.0006f, 0.0006f), Pal.Hex("#C9CDD2"));   // handlebars
            // The rider, leaning forward, legs down to the pedals or pegs.
            var at = new Vector3(0f, motor ? 0.004f : 0.0045f, -0.003f);
            int style = motor ? 3 : 4;
            var m = Matrix4x4.TRS(at, Quaternion.Euler(motor ? 18f : 14f, 0f, 0f), Vector3.one);
            b.Append(new MeshData(PersonFactory.Body(style)), m, Color.white);
            foreach (int side in new[] { -1, 1 })
            {
                b.Append(new MeshData(PersonFactory.Leg(style)), m * Matrix4x4.TRS(PersonFactory.Hip + Vector3.right * side * PersonFactory.LegX, Quaternion.Euler(-55f, 0f, 0f), Vector3.one), Color.white);
                b.Append(new MeshData(PersonFactory.Arm(style)), m * Matrix4x4.TRS(new Vector3(side * PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f), Quaternion.Euler(-70f, 0f, side * 6f), Vector3.one), Color.white);
            }
            if (motor) b.AddBlob(at + new Vector3(0f, 0.0285f, 0.002f), new Vector3(0.0048f, 0.0045f, 0.005f), 1, 0f, 4, (_, __) => Pal.Hex("#E2B04A"));   // a helmet
        }
    }
}
