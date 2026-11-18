using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// What makes a town building say what it is from across the room: its name on a board standing on the roof's front
    /// edge (lettering that glows softly at night), and a big symbol of its trade - a golden loaf on the bakery, a steaming
    /// cup on the café, a giant apple at the grocer's, a plate with knife and fork on the restaurant. The disco gets big
    /// neon letters whose glowing tubes light up one by one, blink and shine (<see cref="Neon"/>; animated by the
    /// buildings). Added after a building's hand-made irregularity, so the letters stay straight.
    /// </summary>
    public static partial class HouseFactory
    {
        private static readonly Color Gold = new(0.98f, 0.82f, 0.36f), Cream = new(0.97f, 0.94f, 0.86f), Ink = new(0.12f, 0.1f, 0.12f);

        private static readonly Dictionary<int, Mesh> signCache = new();

        /// <summary>Just a building's name board and symbol (for its far-away stand-in), or null if it has none.</summary>
        public static Mesh SignMesh(int variant)
        {
            if (signCache.TryGetValue(variant, out var m)) return m;
            var b = new MeshBuilder();
            Signage(b, variant);
            if (variant == Catalog.Disco) DiscoSign(b, 0);   // (lit: the stand-in's neon just shines)
            return signCache[variant] = b.VertexCount > 0 ? b.ToMesh("Sign_" + variant) : null;
        }

        private static void Signage(MeshBuilder b, int variant)
        {
            switch (variant)
            {
                case Catalog.Bakery:
                    RoofBoard(b, "BAKERY", 0.072f, 0.062f, 0.013f, Pal.Hex("#8E2F25"), Gold);
                    Big(b, new Vector3(0f, 0.126f, 0.004f), at => Loaf(b, at));
                    break;
                case Catalog.Cafe:
                    RoofBoard(b, "CAFE", 0.072f, 0.062f, 0.014f, Pal.Hex("#2E4E7A"), Cream);
                    Big(b, new Vector3(0f, 0.124f, 0.004f), at => Cup(b, at));
                    break;
                case Catalog.Grocer:
                    RoofBoard(b, "GROCER", 0.072f, 0.062f, 0.013f, Pal.Hex("#2E6B3A"), Cream);
                    Big(b, new Vector3(0f, 0.13f, 0.004f), at => Apple(b, at));
                    break;
                case Catalog.Restaurant:
                    RoofBoard(b, "RESTAURANT", 0.07f, 0.07f, 0.0092f, Pal.Hex("#5A1E2A"), Gold);
                    Big(b, new Vector3(0f, 0.132f, 0.002f), at => Plate(b, at));
                    StringLights(b);
                    break;
                case Catalog.Disco:
                    DiscoSign(b, null);
                    break;
                case Catalog.Museum:
                    RoofBoard(b, "MUSEUM", 0.075f - 0.036f, 0.07f, 0.011f, Pal.Hex("#24345A"), Gold, flat: true);
                    break;
                case Catalog.Studio:
                    RoofBoard(b, "TV STUDIO", 0.08f, 0.08f, 0.0095f, Pal.Hex("#1E2228"), Cream, flat: true);
                    OnAir(b, new Vector3(-0.035f, 0.066f, -0.0412f));
                    break;
                case Catalog.Fuel:
                    Pylon(b, "FUEL", new Vector3(-0.03f, 0.052f, -0.0425f), 0.009f, Pal.Hex("#D7382E"), Cream);
                    break;
                case Catalog.Hospital:
                    RoofBoard(b, "HOSPITAL", 0.09f, 0.105f, 0.011f, Pal.Hex("#F4F2EC"), Pal.Hex("#D7382E"), flat: true);
                    break;
                case Catalog.FurnitureShop:
                    RoofBoard(b, "FURNITURE", 0.072f, 0.068f, 0.0085f, Pal.Hex("#5A3A22"), Cream);
                    break;
                case Catalog.IceCream:
                    RoofBoard(b, "ICE CREAM", 0.06f, 0.034f, 0.008f, Pal.Hex("#F2A7C3"), Pal.Hex("#7A3A52"));
                    Cone(b, new Vector3(0f, 0.08f, 0.004f));
                    break;
                case Catalog.Pub:
                    RoofBoard(b, "THE RED LION", 0.08f, 0.068f, 0.0072f, Pal.Hex("#2E5A3A"), Gold);
                    break;
                case Catalog.Hotel:
                    RoofBoard(b, "HOTEL", 0.08f, 0.102f, 0.012f, Pal.Hex("#5A1E2A"), Gold);
                    break;
                case Catalog.PostOffice:
                    RoofBoard(b, "POST OFFICE", 0.072f, 0.068f, 0.0078f, Pal.Hex("#C8392E"), Pal.Hex("#F2C12E"));
                    break;
                case Catalog.Sawmill:
                    Pylon(b, "SAWMILL", new Vector3(0.06f, 0.05f, -0.055f), 0.008f, Pal.Hex("#6E4A32"), Cream);
                    break;
                case Catalog.Police:
                    RoofBoard(b, "POLICE", 0.08f, 0.07f, 0.011f, Pal.Hex("#1F3E7A"), Cream);
                    break;
                case Catalog.FireStation:
                    RoofBoard(b, "FIRE STATION", 0.085f, 0.07f, 0.0082f, Pal.Hex("#B8241C"), Cream);
                    break;
                case Catalog.TownHall:
                    RoofBoard(b, "TOWN HALL", 0.078f, 0.088f, 0.0095f, Pal.Hex("#2F4F57"), Gold, x: -0.034f);
                    break;
                case Catalog.School:
                    RoofBoard(b, "SCHOOL", 0.078f, 0.065f, 0.011f, Pal.Hex("#2F5A8A"), Cream, x: 0.036f);
                    break;
                case Catalog.Factory:
                    RoofBoard(b, "BISCUITS", 0.1f, 0.055f, 0.0095f, Pal.Hex("#F2D49A"), Pal.Hex("#7A3A22"), flat: true, x: -0.03f);
                    break;
            }
        }

        /// <summary>A shop's big symbol, half as big again, so its trade shows from across the room.</summary>
        private static void Big(MeshBuilder b, Vector3 at, System.Action<Vector3> symbol)
        {
            b.Push(at, Quaternion.identity, Vector3.one * 1.6f);
            symbol(Vector3.zero);
            b.Pop();
        }

        /// <summary>The restaurant's terrace: a pergola of dark beams with strings of warm lights looping between them.</summary>
        private static void StringLights(MeshBuilder b)
        {
            var beam = Pal.Hex("#3A2A20");
            const float front = -0.1f, back = -0.048f, y = 0.034f;
            foreach (float x in new[] { -0.058f, 0f, 0.058f })
            {
                b.AddBox(new Vector3(x, y / 2f, front), new Vector3(0.002f, y, 0.002f), beam);
                b.AddBox(new Vector3(x, y, (front + back) / 2f), new Vector3(0.002f, 0.002f, back - front), beam);
            }
            b.AddBox(new Vector3(0f, y, front), new Vector3(0.118f, 0.002f, 0.002f), beam);
            for (int span = 0; span < 2; span++)
                for (int k = 0; k <= 8; k++)
                {
                    float x = -0.058f + span * 0.058f + k * 0.058f / 8f, sag = Mathf.Sin(k / 8f * Mathf.PI) * 0.006f;
                    b.AddBlob(new Vector3(x, y - 0.002f - sag, front), Vector3.one * 0.0011f, 0, 0f, span * 9 + k, (_, __) => Pal.Glow(Pal.Hex(k % 3 == 0 ? "#FFD27A" : k % 3 == 1 ? "#FF9A6A" : "#FFF2C0")));
                }
            // Vines on the beams.
            for (int k = 0; k < 10; k++) b.AddBlob(new Vector3(-0.055f + k * 0.012f, y + 0.002f, front + 0.006f * (k % 3)), new Vector3(0.004f, 0.002f, 0.004f), 1, 0.3f, 90 + k, (_, __) => Pal.Hex("#4E8A3E"));
        }

        /// <summary>A giant ice cream cone: a waffle cone with three scoops.</summary>
        private static void Cone(MeshBuilder b, Vector3 at)
        {
            b.Push(at, Quaternion.Euler(180f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0075f, 0.0012f, 0.02f, 10, Pal.Hex("#D9A55A"), Pal.Hex("#C08A42"), true);
            b.Pop();
            var scoops = new[] { "#F2A7C3", "#FFF2C0", "#A8D8A8" };
            for (int k = 0; k < 3; k++) b.AddBlob(at + new Vector3((k - 1) * 0.0045f, 0.004f + (k == 1 ? 0.004f : 0f), 0f), Vector3.one * 0.0052f, 1, 0.05f, 80 + k, (_, __) => Pal.Hex(scoops[k]));
        }

        /// <summary>A name board standing on the roof's front edge on two legs (on a flat roof, at its front parapet).</summary>
        private static void RoofBoard(MeshBuilder b, string text, float depth, float eave, float letter, Color board, Color ink, bool flat = false, float x = 0f)
        {
            float h = letter * 1.75f;
            float z = -depth / 2f + (flat ? 0.006f : 0.009f);
            float bottom = eave + (flat ? 0.005f : 0.011f);
            var centre = new Vector3(x, bottom + h / 2f, z);
            float w = Lettering.Width(text, letter) + letter * 0.9f;
            foreach (int s in new[] { -1, 1 })
                b.AddBox(new Vector3(x + s * w * 0.3f, (eave + bottom) / 2f, z + 0.0015f), new Vector3(0.0018f, bottom - eave + 0.004f, 0.0018f), Ink);
            Lettering.Board(b, text, centre, Vector3.back, letter, board, ink, Pal.Shade(board, 0.6f));
        }

        /// <summary>A name board fixed flat on a wall.</summary>
        private static void WallBoard(MeshBuilder b, string text, Vector3 centre, float letter, Color board, Color ink) =>
            Lettering.Board(b, text, centre, Vector3.back, letter, board, ink, Pal.Shade(board, 0.6f));

        /// <summary>A tall sign on a post by the road.</summary>
        private static void Pylon(MeshBuilder b, string text, Vector3 centre, float letter, Color board, Color ink)
        {
            b.AddBox(new Vector3(centre.x, centre.y / 2f, centre.z + 0.003f), new Vector3(0.0025f, centre.y, 0.0025f), Pal.Hex("#9AA0A8"));
            Lettering.Board(b, text, centre, Vector3.back, letter, board, ink, Pal.Shade(board, 0.6f));
        }

        // ------------------------------------------------------------------ symbols of the trade

        /// <summary>A big golden loaf with three cuts, on the ridge.</summary>
        private static void Loaf(MeshBuilder b, Vector3 at)
        {
            b.AddBlob(at, new Vector3(0.019f, 0.009f, 0.0095f), 2, 0.04f, 5, (d, _) => Color.Lerp(Pal.Hex("#A8642C"), Pal.Hex("#E2A257"), d.y * 0.5f + 0.5f));
            for (int k = -1; k <= 1; k++)
            {
                b.Push(at + new Vector3(k * 0.0095f, 0.0085f, -0.002f), Quaternion.Euler(0f, 0f, 35f));
                b.AddBox(Vector3.zero, new Vector3(0.0018f, 0.0012f, 0.011f), Pal.Hex("#F3D9A4"));
                b.Pop();
            }
            b.AddBox(at + new Vector3(0f, -0.01f, 0f), new Vector3(0.0025f, 0.006f, 0.0025f), Ink);
        }

        /// <summary>A giant coffee cup on its saucer, steaming.</summary>
        private static void Cup(MeshBuilder b, Vector3 at)
        {
            var china = Pal.Hex("#F7F3EA");
            b.AddCylinder(at + new Vector3(0f, -0.003f, 0f), 0.014f, 0.014f, 0.002f, 16, Pal.Shade(china, 0.9f), china, true, true);
            b.AddCylinder(at + new Vector3(0f, -0.001f, 0f), 0.008f, 0.011f, 0.015f, 16, Pal.Shade(china, 0.85f), china, false, true);
            b.AddDisc(at + new Vector3(0f, 0.013f, 0f), 0.0102f, 16, Pal.Hex("#5A3A22"), true);
            b.AddBox(at + new Vector3(0f, 0.004f, -0.0118f), new Vector3(0.012f, 0.004f, 0.0006f), Pal.Hex("#4A7FB8"));   // a blue band
            b.Push(at + new Vector3(0.0125f, 0.006f, 0f), Quaternion.identity);
            b.AddBlob(Vector3.zero, new Vector3(0.0045f, 0.0055f, 0.0016f), 1, 0f, 3, (_, __) => china);
            b.Pop();
            for (int k = 0; k < 3; k++)
                b.AddBlob(at + new Vector3(Mathf.Sin(k * 2.1f) * 0.004f, 0.019f + k * 0.007f, 0f), Vector3.one * (0.0035f - k * 0.0006f), 1, 0.1f, 20 + k, (_, __) => new Color(1f, 1f, 1f, 0.8f));
            b.AddBox(at + new Vector3(0f, -0.011f, 0f), new Vector3(0.0025f, 0.014f, 0.0025f), Ink);
        }

        /// <summary>A giant red apple with a stalk and a leaf.</summary>
        private static void Apple(MeshBuilder b, Vector3 at)
        {
            b.AddBlob(at, new Vector3(0.012f, 0.0115f, 0.012f), 2, 0.03f, 8, (d, _) => Color.Lerp(Pal.Hex("#A8201C"), Pal.Hex("#E8443A"), d.y * 0.4f + 0.5f + d.x * 0.2f));
            b.AddCylinder(at + new Vector3(0f, 0.01f, 0f), 0.0009f, 0.0007f, 0.006f, 5, Pal.Hex("#5A3A22"), Pal.Hex("#5A3A22"));
            b.AddBlob(at + new Vector3(0.0035f, 0.0145f, 0f), new Vector3(0.004f, 0.0012f, 0.0022f), 1, 0f, 4, (_, __) => Pal.Hex("#4E9A3A"));
            b.AddBox(at + new Vector3(0f, -0.012f, 0f), new Vector3(0.0025f, 0.006f, 0.0025f), Ink);
        }

        /// <summary>A white plate standing up to the street, a knife and a fork either side.</summary>
        private static void Plate(MeshBuilder b, Vector3 at)
        {
            b.Push(at, Quaternion.Euler(-90f, 0f, 0f));   // the disc faces the street (-Z)
            b.AddCylinder(Vector3.zero, 0.013f, 0.013f, 0.0018f, 20, Pal.Hex("#E9E4DA"), Pal.Hex("#FFFFFF"), true, true);
            b.AddDisc(new Vector3(0f, 0.0019f, 0f), 0.0085f, 20, Pal.Hex("#F4EFE4"), true);
            b.Pop();
            var steel = Pal.Hex("#C9CED6");
            b.AddBox(at + new Vector3(-0.018f, 0f, 0f), new Vector3(0.0018f, 0.024f, 0.0012f), steel);          // the knife
            b.AddBox(at + new Vector3(-0.0186f, 0.005f, 0f), new Vector3(0.0028f, 0.012f, 0.0012f), steel);
            b.AddBox(at + new Vector3(0.018f, -0.003f, 0f), new Vector3(0.0018f, 0.018f, 0.0012f), steel);       // the fork
            for (int k = -1; k <= 1; k++) b.AddBox(at + new Vector3(0.018f + k * 0.0013f, 0.009f, 0f), new Vector3(0.0006f, 0.007f, 0.0012f), steel);
            b.AddBox(at + new Vector3(0f, -0.016f, 0.002f), new Vector3(0.0025f, 0.01f, 0.0025f), Ink);
        }

        /// <summary>A red "ON AIR" lamp over the studio door.</summary>
        private static void OnAir(MeshBuilder b, Vector3 at)
        {
            b.AddBox(at + new Vector3(0f, 0f, 0.0015f), new Vector3(0.026f, 0.009f, 0.003f), Pal.Hex("#3A0E0E"));
            Lettering.Add(b, "ON AIR", at, Vector3.back, 0.0048f, 0.0008f, Pal.Glow(Pal.Hex("#FF3B30")));
        }

        // ------------------------------------------------------------------ the disco's neon

        private const string DiscoWord = "DISCO";
        private const float DiscoLetter = 0.022f;
        private static readonly Vector3 DiscoSignAt = new(0f, 0.075f + 0.006f + 0.006f + DiscoLetter * 0.7f, -0.085f / 2f + 0.007f);
        private static readonly Color[] NeonColours = { Pal.Hex("#FF3FA4"), Pal.Hex("#3FE0FF"), Pal.Hex("#FFE03F"), Pal.Hex("#8CFF5A"), Pal.Hex("#FF7A3F") };

        /// <summary>
        /// The disco's sign: a black frame on the roof's front edge with the letters as glass tubes (dark by day, the
        /// shape always readable); with <paramref name="letter"/> &gt;= 0, just that letter's glowing tube (a neon part).
        /// </summary>
        private static void DiscoSign(MeshBuilder b, int? letter)
        {
            var c = DiscoSignAt;
            float w = Lettering.Width(DiscoWord, DiscoLetter) + 0.012f, h = DiscoLetter * 1.5f;
            if (letter == null)
            {
                // A dark backing panel in a chrome frame, its legs, and the tubes (dark glass by day).
                b.AddBox(c + new Vector3(0f, 0f, 0.0035f), new Vector3(w, h, 0.0016f), Pal.Hex("#1A1424"));
                foreach (int s in new[] { -1, 1 })
                {
                    b.AddBox(c + new Vector3(0f, s * h / 2f, 0.0025f), new Vector3(w + 0.002f, 0.0014f, 0.0024f), Pal.Hex("#B9BEC6"));
                    b.AddBox(c + new Vector3(s * w / 2f, 0f, 0.0025f), new Vector3(0.0014f, h + 0.002f, 0.0024f), Pal.Hex("#B9BEC6"));
                }
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * w * 0.35f, (0.081f + c.y - h / 2f) / 2f, c.z + 0.004f), new Vector3(0.002f, c.y - h / 2f - 0.081f + 0.004f, 0.002f), Ink);
                for (int i = 0; i < DiscoWord.Length; i++) Lettering.AddLetter(b, DiscoWord, i, c, Vector3.back, DiscoLetter, 0.0016f, Pal.Shade(NeonColours[i], 0.45f));
                return;
            }
            // One letter's light: the same tube, a little thicker so it shows in front, glowing fully.
            Lettering.AddLetter(b, DiscoWord, letter.Value, c, Vector3.back, DiscoLetter, 0.0024f, Pal.Glow(NeonColours[letter.Value]));
        }

        private static readonly Dictionary<int, Mesh[]> neonCache = new();

        /// <summary>
        /// The glowing parts of a building's neon sign, one mesh per letter (switched on and off by the buildings to make
        /// it chase, blink and shine), or null for a building without one.
        /// </summary>
        public static Mesh[] Neon(int variant)
        {
            if (variant != Catalog.Disco) return null;
            if (neonCache.TryGetValue(variant, out var cached)) return cached;
            var parts = new Mesh[DiscoWord.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var b = new MeshBuilder();
                DiscoSign(b, i);
                parts[i] = b.ToMesh($"Neon_{DiscoWord[i]}");
            }
            return neonCache[variant] = parts;
        }

        /// <summary>Which neon letters shine at <paramref name="time"/>: they light up one by one, all blink, then all shine.</summary>
        public static bool NeonOn(int letter, int letters, float time)
        {
            float t = time % 7f;
            if (t < 3f) return letter <= Mathf.FloorToInt(t / 3f * (letters + 1)) - 1;
            if (t < 4.6f) return Mathf.FloorToInt(t * 6f) % 2 == 0;
            return true;
        }
    }
}
