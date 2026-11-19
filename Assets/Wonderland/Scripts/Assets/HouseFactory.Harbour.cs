using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The harbour, a family of pieces like the zoo: the container quay with its gantry crane and the ships that call,
    /// the container yard with its straddle carrier, a warehouse with loading docks and a forklift, and the harbour gate
    /// with its gatehouse, barriers and barbed-wire fence. All on concrete. The moving machines (crane trolley, straddle
    /// carrier, forklift, ships) are drawn and driven by the harbour system.
    /// </summary>
    public static partial class HouseFactory
    {
        private static readonly Color Concrete = Pal.Hex("#BDB9B0"), ConcreteDark = Pal.Hex("#A29E95"), SafetyYellow = Pal.Hex("#E8B92E"),
            CraneRed = Pal.Hex("#C8392E"), CraneWhite = Pal.Hex("#EEEBE4"), SteelGrey = Pal.Hex("#7E848C");

        /// <summary>A straight beam from one point to another (square, <paramref name="t"/> thick).</summary>
        private static void Beam(MeshBuilder b, Vector3 from, Vector3 to, float t, Color colour)
        {
            var d = to - from;
            if (d.sqrMagnitude < 1e-10f) return;
            b.Push((from + to) * 0.5f, Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up));
            b.AddBox(Vector3.zero, new Vector3(t, t, d.magnitude), colour);
            b.Pop();
        }

        /// <summary>Concrete ground: slabs with joints between them, a hair above the ground.</summary>
        private static void ConcretePad(MeshBuilder b, float w, float d, float y = 0.0008f)
        {
            b.AddBox(new Vector3(0f, y, 0f), new Vector3(w, y * 2f, d), Concrete, Pal.Shade(Concrete, 1.04f), ConcreteDark);
            if (b.TooSmall(0.003f)) return;
            for (float x = -w / 2 + 0.03f; x < w / 2 - 0.005f; x += 0.03f) b.AddBox(new Vector3(x, y * 2f + 0.00005f, 0f), new Vector3(0.0005f, 0.0001f, d), ConcreteDark);
            for (float z = -d / 2 + 0.03f; z < d / 2 - 0.005f; z += 0.03f) b.AddBox(new Vector3(0f, y * 2f + 0.00005f, z), new Vector3(w, 0.0001f, 0.0005f), ConcreteDark);
        }

        /// <summary>A tall lighting mast with a cluster of floodlights.</summary>
        private static void LightMast(MeshBuilder b, Vector3 at, float height)
        {
            b.AddCylinder(at, 0.0012f, 0.0008f, height, 6, SteelGrey, SteelGrey, true);
            b.AddBox(at + Vector3.up * (height + 0.0015f), new Vector3(0.008f, 0.0015f, 0.0035f), Pal.Hex("#4A4F56"));
            foreach (int s in new[] { -1, 1 }) b.AddBox(at + new Vector3(s * 0.0025f, height, -0.0012f), new Vector3(0.0028f, 0.002f, 0.0012f), Pal.Glow(Pal.Hex("#FFF6D8")));
        }

        /// <summary>
        /// The container quay (its front, -Z, to the water): a concrete apron on a dark quay wall, black rubber fenders
        /// hung on the wall and bollards along its edge, yellow safety lines, the crane rails, lighting masts, a few
        /// containers waiting and a reach stacker parked. The crane and the containers it moves are the harbour system's.
        /// </summary>
        private static float HarbourQuay(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.15f, D = 0.1f;
            b.AddBox(new Vector3(0f, -0.004f, 0f), new Vector3(W, 0.012f, D), Concrete, Pal.Shade(Concrete, 1.03f), ConcreteDark);
            ConcretePad(b, W, D, 0.001f);
            b.AddBox(new Vector3(0f, -0.016f, -D / 2 + 0.002f), new Vector3(W + 0.002f, 0.032f, 0.004f), Pal.Hex("#5C5A55"));   // the quay wall
            // The wharf deck out over the shore on concrete piles, its front where a ship's side lies.
            const float Front = -0.066f;
            b.AddBox(new Vector3(0f, -0.001f, (Front - D / 2) / 2f), new Vector3(W, 0.006f, -D / 2 - Front + 0.002f), Concrete, Pal.Shade(Concrete, 1.03f), ConcreteDark);
            b.AddBox(new Vector3(0f, -0.005f, Front + 0.0008f), new Vector3(W + 0.001f, 0.012f, 0.0016f), Pal.Hex("#6A6862"));   // its edge beam
            for (float x = -W / 2 + 0.008f; x < W / 2; x += 0.024f)
                foreach (float z in new[] { Front + 0.004f, (Front - D / 2) / 2f })
                    b.AddCylinder(new Vector3(x, -0.04f, z), 0.0016f, 0.0016f, 0.036f, 6, Pal.Hex("#8E8A82"), Pal.Hex("#8E8A82"), false);
            b.AddBox(new Vector3(0f, 0.0021f, Front + 0.0035f), new Vector3(W, 0.0003f, 0.0014f), SafetyYellow);                 // the edge line
            for (int k = 0; k < 6; k++)
            {
                float x = -W / 2 + 0.012f + k * (W - 0.024f) / 5f;
                var at = new Vector3(x, 0.002f, Front + 0.006f);
                b.AddCylinder(at, 0.0017f, 0.0015f, 0.004f, 8, Pal.TrainBlack, Pal.TrainBlack, true);              // bollard
                b.AddCylinder(at + Vector3.up * 0.004f, 0.0023f, 0.0023f, 0.0009f, 8, Pal.TrainBlack, Pal.TrainBlack, true, true);
                // A rubber fender hung on the wall below it.
                b.Push(new Vector3(x + 0.012f, -0.007f, Front - 0.0012f), Quaternion.Euler(0f, 0f, 90f));
                b.AddCylinder(new Vector3(0f, -0.006f, 0f), 0.0026f, 0.0026f, 0.012f, 8, Pal.Hex("#26282B"), Pal.Hex("#26282B"), true);
                b.Pop();
            }
            foreach (float z in new[] { CraneRailZ - 0.004f, CraneRailZ + 0.004f })
            {
                b.AddBox(new Vector3(0f, 0.0025f, z), new Vector3(W - 0.004f, 0.001f, 0.0012f), Pal.Hex("#5A5A62"));
                b.AddBox(new Vector3(0f, 0.0021f, z), new Vector3(W - 0.004f, 0.0003f, 0.004f), ConcreteDark);
            }
            // Walkway lines: the lane behind the crane.
            foreach (float z in new[] { 0.034f, 0.046f }) b.AddBox(new Vector3(0f, 0.0021f, z), new Vector3(W - 0.01f, 0.0003f, 0.0008f), SafetyYellow);
            LightMast(b, new Vector3(-W / 2 + 0.006f, 0.002f, D / 2 - 0.006f), 0.07f);
            LightMast(b, new Vector3(W / 2 - 0.006f, 0.002f, D / 2 - 0.006f), 0.07f);
            // Containers waiting at the back, and a reach stacker parked by them.
            for (int k = 0; k < 6; k++) AddContainer(b, new Vector3(-W / 2 + 0.02f + (k % 3) * 0.026f, 0.002f + k / 3 * 0.0122f, D / 2 - 0.014f), k + rng.Range(0, 6));
            Forklift(b, new Vector3(W / 2 - 0.03f, 0.002f, D / 2 - 0.02f), 200f, true);
            return 0.1f;
        }

        /// <summary>
        /// A ship-to-shore gantry crane (its rail centre at the origin, the boom out over the water, -Z): a red portal on
        /// four legs straddling the rails, wheel bogies under them, diagonal bracing, a white boom of twin girders with the
        /// trolley's run between them, the A-frame above with its stays to the boom's ends, the machinery house on the
        /// back reach, the driver's cab and a red warning light on top.
        /// </summary>
        public static Mesh CraneMesh()
        {
            if (crane != null) return crane;
            var b = new MeshBuilder();
            const float X = 0.0105f, Zr = 0.004f, Portal = 0.058f, H = CraneHeight;
            float tip = CraneReach - CraneRailZ - 0.01f, back = 0.032f;
            bool fine = !b.TooSmall(0.003f);
            foreach (int sz in new[] { -1, 1 })
            {
                // Sill beam on its rail, with the wheel bogies.
                b.AddBox(new Vector3(0f, 0.0035f, sz * Zr), new Vector3(2f * X + 0.006f, 0.003f, 0.0028f), CraneRed);
                if (fine) foreach (int sx in new[] { -1, 1 }) for (int w = -1; w <= 1; w += 2) b.AddBox(new Vector3(sx * X + w * 0.0025f, 0.0013f, sz * Zr), new Vector3(0.0022f, 0.0026f, 0.0022f), Pal.Hex("#3A3A40"));
                foreach (int sx in new[] { -1, 1 })
                {
                    b.AddBox(new Vector3(sx * X, Portal / 2f, sz * Zr), new Vector3(0.0024f, Portal, 0.0024f), CraneRed);                   // leg
                    b.AddBox(new Vector3(sx * X, (Portal + H) / 2f, sz * Zr), new Vector3(0.002f, H - Portal, 0.002f), CraneRed);           // upper leg
                }
                b.AddBox(new Vector3(0f, Portal, sz * Zr), new Vector3(2f * X + 0.003f, 0.0045f, 0.003f), CraneRed);                        // portal beam
                if (fine) { Beam(b, new Vector3(-X, 0.006f, sz * Zr), new Vector3(X, Portal - 0.004f, sz * Zr), 0.0012f, CraneRed); Beam(b, new Vector3(X, 0.006f, sz * Zr), new Vector3(-X, Portal - 0.004f, sz * Zr), 0.0012f, CraneRed); }
            }
            foreach (int sx in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(sx * X, Portal, 0f), new Vector3(0.003f, 0.0045f, 2f * Zr + 0.003f), CraneRed);
                // The boom: a girder each side, from the back reach out over the water.
                b.AddBox(new Vector3(sx * 0.0048f, H + 0.0015f, (tip + back) / 2f), new Vector3(0.002f, 0.0045f, back - tip), CraneWhite, CraneWhite, Pal.Shade(CraneWhite, 0.8f));
                // The A-frame above the legs, and its stays to the boom's ends.
                var apex = new Vector3(sx * 0.005f, H + 0.03f, 0.001f);
                Beam(b, new Vector3(sx * X, H, -Zr), apex, 0.0018f, CraneWhite);
                Beam(b, new Vector3(sx * X, H, Zr), apex, 0.0018f, CraneWhite);
                Beam(b, apex, new Vector3(sx * 0.0048f, H + 0.004f, tip + 0.004f), 0.0008f, Pal.Hex("#9AA0A8"));
                Beam(b, apex, new Vector3(sx * 0.0048f, H + 0.004f, back - 0.004f), 0.0008f, Pal.Hex("#9AA0A8"));
            }
            b.AddBox(new Vector3(0f, H + 0.03f, 0.001f), new Vector3(0.012f, 0.0018f, 0.0018f), CraneWhite);   // the apex cross beam
            b.AddBlob(new Vector3(0f, H + 0.032f, 0.001f), Vector3.one * 0.0012f, 0, 0f, 1, (_, __) => Pal.Glow(Pal.Hex("#FF3B30")));
            if (fine) for (float z = tip + 0.006f; z < back - 0.004f; z += 0.012f) b.AddBox(new Vector3(0f, H + 0.0036f, z), new Vector3(0.0114f, 0.0008f, 0.0008f), CraneWhite);   // cross members
            // The machinery house on the back reach, and the driver's cab under the boom on the water side.
            b.AddBox(new Vector3(0f, H + 0.0085f, back - 0.011f), new Vector3(0.014f, 0.009f, 0.016f), CraneWhite, Pal.Shade(CraneWhite, 1.03f), Pal.Shade(CraneWhite, 0.8f));
            b.AddBox(new Vector3(0f, H + 0.0085f, back - 0.011f), new Vector3(0.0142f, 0.0018f, 0.0162f), Pal.Hex("#2E6FB0"));
            b.AddBox(new Vector3(0f, H - 0.006f, -0.018f), new Vector3(0.008f, 0.0065f, 0.008f), CraneRed);
            b.AddFace(new Vector3(0f, H - 0.006f, -0.0221f), Vector3.back, Vector3.up, 0.0034f, 0.0026f, Pal.Glow(Pal.WindowGlow));
            if (fine) for (float y = 0.008f; y < Portal - 0.004f; y += 0.004f) b.AddBox(new Vector3(X + 0.0018f, y, -Zr), new Vector3(0.0012f, 0.0004f, 0.0016f), Pal.Hex("#3A3A40"));   // a ladder up a leg
            return crane = b.ToMesh("Crane");
        }

        /// <summary>
        /// A container ship (bow +Z, the waterline at y = 0): a shaped hull - flared bow over a bulbous foot, a squared
        /// transom - dark blue above a white boot stripe and red below; hatch covers under its four container bays with
        /// yellow lashing bridges between them; a forecastle with a windlass and a foremast; aft, a three-tier white
        /// bridge house with bands of windows, wings and a lit wheelhouse, a radar mast, the funnel in company colours, and
        /// an orange lifeboat each side. Its containers are a separate deck (see <see cref="ShipDeck"/>).
        /// </summary>
        public static Mesh ShipMesh()
        {
            if (ship != null) return ship;
            var b = new MeshBuilder();
            const float L = 0.16f, W = 0.034f, Deck = 0.012f;
            var navy = Pal.Hex("#1E2A44"); var red = Pal.Hex("#B8342A"); var white = Pal.Hex("#F2EFE8"); var hatch = Pal.Hex("#5E6E62");
            bool fine = !b.TooSmall(0.003f);
            int stations = fine ? 28 : 12;
            float Half(float t) => t < 0.06f ? W / 2 * Mathf.Lerp(0.82f, 1f, t / 0.06f) : t > 0.7f ? W / 2 * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((t - 0.7f) / 0.3f, 2f))) : W / 2;
            float Top(float t) => Deck + (t > 0.78f ? 0.005f * Mathf.Pow((t - 0.78f) / 0.22f, 2f) : 0f);
            // Each station's cross-section, port to starboard over the keel: deck edge, boot stripe, waterline, bilge, keel.
            Vector3[] Section(float t)
            {
                float z = -L / 2 + t * L, h = Half(t), top = Top(t);
                return new[] { new Vector3(-h, top, z), new Vector3(-h, 0.0012f, z), new Vector3(-h, 0f, z), new Vector3(-h * 0.92f, -0.006f, z), new Vector3(-h * 0.55f, -0.009f, z),
                               new Vector3(h * 0.55f, -0.009f, z), new Vector3(h * 0.92f, -0.006f, z), new Vector3(h, 0f, z), new Vector3(h, 0.0012f, z), new Vector3(h, top, z) };
            }
            Color[] bands = { navy, white, red, red, red, red, red, white, navy };
            var prev = Section(0f);
            for (int k = 1; k <= stations; k++)
            {
                var cur = Section(k / (float)stations);
                for (int j = 0; j < 9; j++)
                {
                    Vector3 a = prev[j], c = prev[j + 1], d = cur[j + 1], e = cur[j];
                    var centre = new Vector3(0f, 0.002f, (a.z + d.z) * 0.5f);
                    var n = Vector3.Cross(c - a, e - a);
                    if (Vector3.Dot(n, (a + c + d + e) * 0.25f - centre) < 0f) b.AddQuad(a, e, d, c, bands[j]); else b.AddQuad(a, c, d, e, bands[j]);
                }
                prev = cur;
            }
            // The transom (flat stern), and the deck.
            var stern = Section(0f);
            for (int j = 1; j < 9; j++) b.AddTriangleOutward(stern[0], stern[j], stern[j + 1], new Vector3(0f, 0.002f, 0f), j is 1 or 8 ? white : j < 2 || j > 7 ? navy : red);
            b.AddTriangleOutward(stern[0], stern[9], stern[5], new Vector3(0f, 0.002f, 0f), navy);
            for (int k = 0; k < stations; k++)
            {
                float t0 = k / (float)stations, t1 = (k + 1) / (float)stations;
                float z0 = -L / 2 + t0 * L, z1 = -L / 2 + t1 * L, h0 = Half(t0) - 0.0006f, h1 = Half(t1) - 0.0006f;
                var a = new Vector3(-h0, Top(t0) - 0.0004f, z0); var c = new Vector3(h0, Top(t0) - 0.0004f, z0); var d = new Vector3(h1, Top(t1) - 0.0004f, z1); var e = new Vector3(-h1, Top(t1) - 0.0004f, z1);
                if (Vector3.Cross(c - a, d - a).y >= 0f) b.AddQuad(a, c, d, e, Pal.Hex("#8A3A2E")); else b.AddQuad(a, e, d, c, Pal.Hex("#8A3A2E"));
            }
            b.AddBlob(new Vector3(0f, -0.0045f, L / 2 - 0.006f), new Vector3(0.0035f, 0.003f, 0.008f), 1, 0f, 3, (_, __) => red);   // the bulbous bow
            // Hatch covers under the four bays, lashing bridges between them.
            for (int k = 0; k < 4; k++) b.AddBox(new Vector3(0f, Deck + 0.0005f, 0.04f - k * 0.026f), new Vector3(W - 0.006f, 0.001f, 0.024f), hatch, Pal.Shade(hatch, 1.1f), hatch);
            if (fine)
                foreach (float z in new[] { 0.053f, 0.027f, 0.001f, -0.025f, -0.051f })
                {
                    foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 - 0.003f), Deck + 0.007f, z), new Vector3(0.0008f, 0.014f, 0.0008f), SafetyYellow);
                    b.AddBox(new Vector3(0f, Deck + 0.014f, z), new Vector3(W - 0.006f, 0.0008f, 0.0008f), SafetyYellow);
                }
            // The forecastle: a raised deck, its bulwark, the windlass and the foremast with its light.
            b.AddBox(new Vector3(0f, Deck + 0.003f, L / 2 - 0.016f), new Vector3(W * 0.7f, 0.002f, 0.02f), Pal.Hex("#8A3A2E"));
            if (fine) foreach (int s in new[] { -1, 1 }) b.AddCylinder(new Vector3(s * 0.005f, Deck + 0.004f, L / 2 - 0.014f), 0.0018f, 0.0018f, 0.0025f, 8, Pal.Hex("#3A6A4A"), Pal.Hex("#3A6A4A"), true);
            b.AddCylinder(new Vector3(0f, Deck + 0.004f, L / 2 - 0.022f), 0.0007f, 0.0005f, 0.022f, 5, white, white, true);
            b.AddBlob(new Vector3(0f, Deck + 0.026f, L / 2 - 0.022f), Vector3.one * 0.0008f, 0, 0f, 2, (_, __) => Pal.Glow(Pal.Hex("#FFF6D8")));
            // The bridge house aft: three tiers, window bands, the wheelhouse with its wings.
            float zb = -L / 2 + 0.024f;
            for (int tier = 0; tier < 3; tier++)
            {
                float y = Deck + 0.004f + tier * 0.0075f, w = W - 0.006f - tier * 0.003f;
                b.AddBox(new Vector3(0f, y, zb), new Vector3(w, 0.0075f, 0.022f - tier * 0.002f), white, white, Pal.Shade(white, 0.8f));
                b.AddBox(new Vector3(0f, y + 0.0012f, zb + 0.011f - tier * 0.001f + 0.0002f), new Vector3(w - 0.002f, 0.0018f, 0.0004f), tier == 2 ? Pal.Glow(Pal.WindowGlow) : Pal.Hex("#2C3A4A"));
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (w / 2 + 0.0002f), y + 0.0012f, zb), new Vector3(0.0004f, 0.0018f, 0.016f), Pal.Hex("#2C3A4A"));
            }
            float yb = Deck + 0.004f + 2 * 0.0075f;
            b.AddBox(new Vector3(0f, yb + 0.004f, zb + 0.004f), new Vector3(W + 0.004f, 0.0012f, 0.008f), white);   // the bridge wings
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 + 0.0015f), yb + 0.005f, zb + 0.004f), new Vector3(0.001f, 0.001f, 0.001f), Pal.Glow(s < 0 ? Pal.Hex("#FF3B30") : Pal.Hex("#3BE06A")));   // port red, starboard green
            // The radar mast and the funnel.
            b.AddCylinder(new Vector3(0f, yb + 0.0045f, zb), 0.0006f, 0.0005f, 0.016f, 5, white, white, true);
            b.AddBox(new Vector3(0f, yb + 0.016f, zb), new Vector3(0.012f, 0.0007f, 0.0007f), white);
            if (fine) foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.004f, yb + 0.018f, zb), new Vector3(0.0055f, 0.0005f, 0.0012f), Pal.Hex("#3A3A40"));
            var funnel = new Vector3(0f, Deck + 0.024f, -L / 2 + 0.009f);
            b.AddBox(funnel, new Vector3(0.008f, 0.016f, 0.009f), Pal.Hex("#2F5A8A"), Pal.Hex("#2F5A8A"), Pal.Hex("#2F5A8A"));
            b.AddBox(funnel + Vector3.up * 0.002f, new Vector3(0.0082f, 0.004f, 0.0092f), Pal.Hex("#D9C04A"));
            b.AddBox(funnel + Vector3.up * 0.0075f, new Vector3(0.0082f, 0.0015f, 0.0092f), Pal.TrainBlack);
            // A lifeboat each side, slung from its davits.
            foreach (int s in new[] { -1, 1 })
            {
                var boat = new Vector3(s * (W / 2 - 0.0015f), Deck + 0.009f, zb - 0.002f);
                b.AddBlob(boat, new Vector3(0.0022f, 0.0018f, 0.0065f), 1, 0f, 4 + s, (d, _) => d.y > 0.3f ? white : Pal.Hex("#F07A2A"));
                if (fine) foreach (int e in new[] { -1, 1 }) b.AddBox(boat + new Vector3(-s * 0.0012f, 0.003f, e * 0.004f), new Vector3(0.0006f, 0.006f, 0.0006f), Pal.Hex("#9AA0A8"));
            }
            if (fine)   // railings along the deck's edges
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 - 0.0008f), Deck + 0.003f, 0.01f), new Vector3(0.0003f, 0.0003f, L - 0.06f), white);
            return ship = b.ToMesh("Ship");
        }

        /// <summary>
        /// The container yard: a concrete apron with lanes painted on it, two blocks of containers stacked one to four
        /// high either side of the straddle carrier's aisle, and lighting masts at its corners. The carrier is the harbour
        /// system's.
        /// </summary>
        private static float ContainerYard(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.22f, D = 0.15f;
            ConcretePad(b, W, D);
            foreach (float z in new[] { -0.012f, 0.012f }) b.AddBox(new Vector3(0f, 0.0017f, z), new Vector3(W - 0.01f, 0.0003f, 0.0009f), SafetyYellow);   // the aisle
            for (int block = -1; block <= 1; block += 2)
                for (int col = 0; col < 7; col++)
                    for (int row = 0; row < 2; row++)
                    {
                        int high = rng.Range(1, 5);
                        for (int tier = 0; tier < high; tier++)
                            AddContainer(b, new Vector3(-W / 2 + 0.022f + col * 0.0275f, 0.0016f + tier * 0.0122f, block * (0.026f + row * 0.0125f + 0.012f)), rng.Range(0, 60));
                    }
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) LightMast(b, new Vector3(sx * (W / 2 - 0.005f), 0.0016f, sz * (D / 2 - 0.005f)), 0.075f);
            Lettering.Board(b, "YARD 2", new Vector3(-W / 2 + 0.016f, 0.012f, -D / 2 + 0.002f), Vector3.back, 0.004f, Pal.Hex("#2F5A8A"), Pal.FlowerWhite, SteelGrey);
            b.AddBox(new Vector3(-W / 2 + 0.016f, 0.006f, -D / 2 + 0.003f), new Vector3(0.001f, 0.009f, 0.001f), SteelGrey);
            return 0.06f;
        }

        /// <summary>
        /// A warehouse: a long corrugated shed under a shallow roof with skylights, three roller doors at the front each
        /// with its loading dock (a raised platform and black dock seals), a little office at one end, WAREHOUSE across
        /// the front, a lorry trailer backed up to a dock and pallets of goods on the concrete. Its forklift is the
        /// harbour system's.
        /// </summary>
        private static float WarehouseShed(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.16f, D = 0.11f, Wd = 0.15f, Dd = 0.06f, H = 0.04f;
            ConcretePad(b, W, D);
            var wall = Pal.Hex("#8FA3B0"); var roof = Pal.Hex("#6E7B84");
            float zc = D / 2 - Dd / 2 - 0.004f;
            b.AddBox(new Vector3(0f, H / 2 + 0.0016f, zc), new Vector3(Wd, H, Dd), wall, Pal.Shade(wall, 1.08f), Pal.Shade(wall, 0.8f));
            if (!b.TooSmall(0.003f))   // corrugation
                for (float x = -Wd / 2 + 0.003f; x < Wd / 2; x += 0.004f)
                    foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(x, H / 2 + 0.0016f, zc + s * (Dd / 2 + 0.0003f)), new Vector3(0.0012f, H, 0.0006f), Pal.Shade(wall, 0.9f));
            // The roof: two shallow slopes with skylight strips.
            foreach (int s in new[] { -1, 1 })
            {
                b.Push(new Vector3(0f, H + 0.0016f + 0.004f, zc + s * Dd / 4f), Quaternion.Euler(s * 8f, 0f, 0f));
                b.AddBox(Vector3.zero, new Vector3(Wd + 0.004f, 0.0016f, Dd / 2f + 0.003f), roof);
                if (!b.TooSmall(0.003f)) for (float x = -Wd / 2 + 0.02f; x < Wd / 2 - 0.01f; x += 0.03f) b.AddBox(new Vector3(x, 0.001f, 0f), new Vector3(0.008f, 0.0004f, Dd / 2f - 0.006f), Pal.Hex("#C9DCE6"));
                b.Pop();
            }
            // Roller doors and loading docks along the front.
            float front = zc - Dd / 2;
            for (int k = 0; k < 3; k++)
            {
                float x = -Wd / 2 + 0.03f + k * 0.035f;
                b.AddFace(new Vector3(x, 0.016f, front - 0.0004f), Vector3.back, Vector3.up, 0.011f, 0.012f, Pal.Hex("#C9CDD2"));
                if (!b.TooSmall(0.003f)) for (float y = 0.006f; y < 0.028f; y += 0.0025f) b.AddBox(new Vector3(x, y, front - 0.0007f), new Vector3(0.022f, 0.0004f, 0.0004f), Pal.Hex("#A9AEB4"));
                b.AddBox(new Vector3(x, 0.004f, front - 0.004f), new Vector3(0.024f, 0.0056f, 0.008f), Pal.Shade(Concrete, 0.9f));   // the dock platform
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(x + s * 0.0125f, 0.017f, front - 0.0015f), new Vector3(0.0018f, 0.024f, 0.003f), Pal.TrainBlack);   // dock seals
                b.AddBox(new Vector3(x, 0.029f, front - 0.0015f), new Vector3(0.026f, 0.0025f, 0.003f), Pal.TrainBlack);
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(x + s * 0.008f, 0.004f, front - 0.0085f), new Vector3(0.002f, 0.003f, 0.001f), Pal.TrainBlack);   // bumpers
            }
            // A trailer backed up to the first dock.
            float tx = -Wd / 2 + 0.03f;
            b.AddBox(new Vector3(tx, 0.017f, front - 0.04f), new Vector3(0.022f, 0.024f, 0.064f), Pal.Hex("#E8E4DC"), Pal.Hex("#F2EFE8"), Pal.Hex("#3A3A40"));
            Lettering.Add(b, "CARGO", new Vector3(tx - 0.0111f, 0.02f, front - 0.04f), Vector3.left, 0.006f, 0.0006f, Pal.Hex("#2F5A8A"));
            foreach (float z in new[] { front - 0.065f, front - 0.058f }) foreach (int s in new[] { -1, 1 })
            {
                b.Push(new Vector3(tx + s * 0.0105f, 0.0035f, z), Quaternion.Euler(0f, 0f, 90f));
                b.AddCylinder(new Vector3(0f, -0.0012f, 0f), 0.0032f, 0.0032f, 0.0024f, 8, Pal.TrainBlack, Pal.TrainBlack, true);
                b.Pop();
            }
            // The office at the right-hand end, and the sign.
            float ox = Wd / 2 - 0.016f;
            b.AddBox(new Vector3(ox, 0.012f, front - 0.009f), new Vector3(0.028f, 0.021f, 0.018f), Pal.Hex("#E8E4DC"), Pal.Hex("#F2EFE8"), Pal.Shade(Pal.Hex("#E8E4DC"), 0.8f));
            b.AddBox(new Vector3(ox, 0.0155f, front - 0.0182f), new Vector3(0.024f, 0.004f, 0.0005f), Pal.Glow(Pal.WindowGlow));
            b.AddFace(new Vector3(ox - 0.008f, 0.0075f, front - 0.0182f), Vector3.back, Vector3.up, 0.003f, 0.0055f, Pal.Hex("#3A4A5A"));
            Lettering.Board(b, "WAREHOUSE", new Vector3(-0.012f, H - 0.006f, front - 0.0006f), Vector3.back, 0.0055f, Pal.Hex("#2F5A8A"), Pal.FlowerWhite, Pal.Hex("#1E3A5A"));
            // Pallets of goods on the concrete.
            for (int k = 0; k < 5; k++)
            {
                var at = new Vector3(0.012f + (k % 3) * 0.013f, 0.0016f, front - 0.03f - k / 3 * 0.013f);
                b.AddBox(at + Vector3.up * 0.0009f, new Vector3(0.011f, 0.0018f, 0.011f), Pal.Hex("#B07A45"));
                b.AddBox(at + Vector3.up * 0.0055f, new Vector3(0.0095f, 0.0075f, 0.0095f), k % 2 == 0 ? Pal.Hex("#C9A46A") : Pal.Hex("#8FB0C8"));
            }
            return 0.05f;
        }

        /// <summary>
        /// The harbour gate: the way in and out between lanes painted in yellow, a gatehouse on the island between them,
        /// a canopy over both with HARBOUR along it, red-and-white barrier arms, a CCTV camera on a pole, flags, and a
        /// security fence topped with barbed wire running off either side.
        /// </summary>
        private static float HarbourGateHouse(MeshBuilder b, ref Rng rng)
        {
            const float W = 0.14f, D = 0.08f, Canopy = 0.032f;
            ConcretePad(b, W, D);
            foreach (float x in new[] { -0.03f, 0.03f }) b.AddBox(new Vector3(x, 0.0017f, 0f), new Vector3(0.0008f, 0.0003f, D - 0.01f), SafetyYellow);   // lane edges
            // The gatehouse on the island.
            b.AddBox(new Vector3(0f, 0.0028f, 0f), new Vector3(0.018f, 0.0024f, 0.05f), Pal.Shade(Concrete, 0.92f));
            b.AddBox(new Vector3(0f, 0.012f, 0.004f), new Vector3(0.014f, 0.016f, 0.018f), Pal.Hex("#E8E4DC"), Pal.Hex("#F2EFE8"), Pal.Shade(Pal.Hex("#E8E4DC"), 0.8f));
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.0071f, 0.014f, 0.004f), new Vector3(0.0004f, 0.006f, 0.014f), Pal.Glow(Pal.WindowGlow));
            b.AddBox(new Vector3(0f, 0.0205f, 0.004f), new Vector3(0.016f, 0.0015f, 0.02f), Pal.Hex("#3A3F46"));
            // The canopy over both lanes.
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.058f, Canopy / 2f, sz * 0.016f), new Vector3(0.0022f, Canopy, 0.0022f), SteelGrey);
            b.AddBox(new Vector3(0f, Canopy + 0.0012f, 0f), new Vector3(0.124f, 0.0024f, 0.04f), Pal.Hex("#E8E4DC"), Pal.Hex("#C9CDD2"), Pal.Hex("#F2EFE8"));
            foreach (int s in new[] { -1, 1 })   // (lettered both ways: coming in from the street, going out from the quay)
            {
                b.AddBox(new Vector3(0f, Canopy - 0.002f, s * 0.0205f), new Vector3(0.124f, 0.006f, 0.0012f), Pal.Hex("#2F5A8A"));
                Lettering.Add(b, "HARBOUR", new Vector3(0f, Canopy - 0.002f, s * 0.0213f), s < 0 ? Vector3.back : Vector3.forward, 0.0045f, 0.0005f, Pal.FlowerWhite);
            }
            // Barrier arms, one each way (raised a little, striped).
            foreach (int s in new[] { -1, 1 })
            {
                var post = new Vector3(s * 0.011f, 0f, -s * 0.016f);
                b.AddBox(post + Vector3.up * 0.005f, new Vector3(0.003f, 0.01f, 0.003f), Pal.Hex("#3A3F46"));
                b.Push(post + new Vector3(0f, 0.009f, 0f), Quaternion.Euler(0f, 0f, s * -8f));
                for (int k = 0; k < 6; k++) b.AddBox(new Vector3(s * (0.003f + k * 0.0075f), 0f, 0f), new Vector3(0.0075f, 0.0012f, 0.0012f), k % 2 == 0 ? CraneRed : Pal.FlowerWhite);
                b.Pop();
            }
            // CCTV on a pole, and flags.
            b.AddCylinder(new Vector3(0.007f, 0.004f, 0.016f), 0.0006f, 0.0006f, 0.036f, 5, SteelGrey, SteelGrey, true);
            b.Push(new Vector3(0.007f, 0.039f, 0.016f), Quaternion.Euler(20f, 200f, 0f));
            b.AddBox(new Vector3(0f, 0f, 0.002f), new Vector3(0.0022f, 0.0022f, 0.005f), Pal.FlowerWhite);
            b.Pop();
            foreach (int s in new[] { -1, 1 })
            {
                b.AddCylinder(new Vector3(s * 0.064f, 0f, 0.03f), 0.0005f, 0.0005f, 0.05f, 5, Pal.FlowerWhite, Pal.FlowerWhite, true);
                b.AddBox(new Vector3(s * 0.064f + 0.005f, 0.045f, 0.03f), new Vector3(0.01f, 0.0065f, 0.0004f), s < 0 ? Pal.Hex("#2F5A8A") : CraneRed);
            }
            // The security fence off either side: chain-link and barbed wire.
            foreach (int s in new[] { -1, 1 })
            {
                for (float x = 0.062f; x <= W / 2 + 0.0001f; x += 0.008f)
                {
                    float px = s * x;
                    b.AddBox(new Vector3(px, 0.013f, 0.028f), new Vector3(0.0012f, 0.026f, 0.0012f), SteelGrey);
                    b.Push(new Vector3(px, 0.026f, 0.028f), Quaternion.Euler(-45f, 0f, 0f));
                    b.AddBox(new Vector3(0f, 0.003f, 0f), new Vector3(0.0007f, 0.006f, 0.0007f), SteelGrey);
                    b.Pop();
                }
                b.AddBox(new Vector3(s * (0.062f + W / 2) / 2f, 0.0125f, 0.028f), new Vector3(W / 2 - 0.062f, 0.024f, 0.0003f), new Color(0.62f, 0.66f, 0.7f));
                for (int k = 1; k <= 3; k++) b.AddBox(new Vector3(s * (0.062f + W / 2) / 2f, 0.026f + k * 0.0015f, 0.028f + k * 0.0015f), new Vector3(W / 2 - 0.062f, 0.0003f, 0.0003f), Pal.Hex("#5C6168"));
            }
            return 0.05f;
        }

        // ---------------------------------------------------------------- the harbour's machines

        private static readonly Mesh[] carriers = new Mesh[MeshBuilder.Levels], forklifts = new Mesh[MeshBuilder.Levels];

        /// <summary>A straddle carrier (facing +Z): two tall side frames on wheels and a top frame, the driver's cab up on one corner; the container it carries hangs between its legs (a separate part).</summary>
        public static Mesh StraddleCarrier(int detail) => carriers[detail + 1] ??= MeshBuilder.At(detail, 1f, () =>
        {
            var b = new MeshBuilder();
            const float X = 0.0085f, Z = 0.014f, H = 0.026f;
            var frame = Pal.Hex("#2E6FB0");
            foreach (int sx in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(sx * X, 0.003f, 0f), new Vector3(0.003f, 0.003f, 2f * Z + 0.004f), frame);   // the bottom beam
                foreach (int sz in new[] { -1, 1 })
                {
                    b.AddBox(new Vector3(sx * X, H / 2f, sz * Z), new Vector3(0.0022f, H, 0.0022f), frame);
                    b.Push(new Vector3(sx * X, 0.0018f, sz * Z), Quaternion.Euler(0f, 0f, 90f));
                    b.AddCylinder(new Vector3(0f, -0.0013f, 0f), 0.0018f, 0.0018f, 0.0026f, 8, Pal.TrainBlack, Pal.TrainBlack, true);
                    b.Pop();
                }
                b.AddBox(new Vector3(sx * X, H, 0f), new Vector3(0.0028f, 0.003f, 2f * Z + 0.003f), frame);   // the top beam
            }
            foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(0f, H, sz * Z), new Vector3(2f * X + 0.003f, 0.0028f, 0.0025f), frame);
            b.AddBox(new Vector3(X, H + 0.0045f, Z - 0.002f), new Vector3(0.0055f, 0.006f, 0.0055f), Pal.Hex("#F2EFE8"));
            b.AddFace(new Vector3(X, H + 0.005f, Z + 0.0008f), Vector3.forward, Vector3.up, 0.0022f, 0.0018f, Pal.Glow(Pal.WindowGlow));
            b.AddBlob(new Vector3(-X, H + 0.0025f, Z), Vector3.one * 0.0008f, 0, 0f, 1, (_, __) => Pal.Glow(Pal.Hex("#F2A030")));   // beacon
            return b.ToMesh("Straddle carrier");
        });

        /// <summary>The height a straddle carrier lifts its container to.</summary>
        public const float CarrierLift = 0.013f;

        /// <summary>A forklift (facing +Z): yellow body and counterweight, an overhead guard, the mast at the front; its forks are a separate part.</summary>
        public static Mesh ForkliftMesh(int detail) => forklifts[detail + 1] ??= MeshBuilder.At(detail, 1f, () => { var b = new MeshBuilder(); Forklift(b, Vector3.zero, 0f, false); return b.ToMesh("Forklift"); });

        /// <summary>A forklift's forks and carriage (raised by the harbour system), and a pallet of goods on them.</summary>
        public static Mesh ForkMesh(bool loaded)
        {
            var b = new MeshBuilder();
            b.AddBox(new Vector3(0f, 0.002f, 0f), new Vector3(0.008f, 0.004f, 0.0008f), Pal.Hex("#3A3A40"));
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.0022f, 0.0003f, 0.004f), new Vector3(0.0012f, 0.0006f, 0.008f), Pal.Hex("#3A3A40"));
            if (loaded)
            {
                b.AddBox(new Vector3(0f, 0.0015f, 0.0045f), new Vector3(0.009f, 0.0014f, 0.009f), Pal.Hex("#B07A45"));
                b.AddBox(new Vector3(0f, 0.0055f, 0.0045f), new Vector3(0.008f, 0.0066f, 0.008f), Pal.Hex("#C9A46A"));
            }
            return b.ToMesh(loaded ? "Forks, loaded" : "Forks");
        }

        private static void Forklift(MeshBuilder b, Vector3 at, float yaw, bool withForks)
        {
            var yellow = Pal.Hex("#F2B02E");
            b.Push(at, Quaternion.Euler(0f, yaw, 0f));
            b.AddBox(new Vector3(0f, 0.0042f, -0.001f), new Vector3(0.008f, 0.004f, 0.012f), yellow, Pal.Shade(yellow, 1.1f), Pal.Shade(yellow, 0.7f));
            b.AddBox(new Vector3(0f, 0.0055f, -0.0065f), new Vector3(0.0082f, 0.006f, 0.0035f), Pal.Hex("#3A3A40"));   // counterweight
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.AddBox(new Vector3(sx * 0.0036f, 0.0095f, sz * 0.003f - 0.001f), new Vector3(0.0006f, 0.007f, 0.0006f), Pal.TrainBlack);
            b.AddBox(new Vector3(0f, 0.0132f, -0.001f), new Vector3(0.0082f, 0.0006f, 0.0072f), Pal.TrainBlack);   // the guard
            b.AddBox(new Vector3(0f, 0.0072f, -0.0015f), new Vector3(0.0035f, 0.0016f, 0.003f), Pal.Hex("#3A3A40"));   // the seat
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.0028f, 0.0085f, 0.0055f), new Vector3(0.0008f, 0.015f, 0.0008f), Pal.Hex("#3A3A40"));   // the mast
            foreach (int sx in new[] { -1, 1 }) foreach (float z in new[] { 0.0035f, -0.005f })
            {
                b.Push(new Vector3(sx * 0.0042f, 0.0017f, z), Quaternion.Euler(0f, 0f, 90f));
                b.AddCylinder(new Vector3(0f, -0.0009f, 0f), 0.0017f, 0.0017f, 0.0018f, 8, Pal.TrainBlack, Pal.TrainBlack, true);
                b.Pop();
            }
            if (withForks) foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.0022f, 0.0006f, 0.0095f), new Vector3(0.0012f, 0.0006f, 0.008f), Pal.Hex("#3A3A40"));
            b.Pop();
        }
    }
}
