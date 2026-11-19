using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    public enum BoatKind { Rowing = 0, Sailing = 1, Steamer = 2 }

    /// <summary>
    /// Storybook boats for the ponds, built like the rest from procedural parts: a wooden rowing boat with two oars, a
    /// little white sailing dinghy whose sail swings with the wind, and a paddle steamer with a cabin, a red funnel and
    /// paddle boxes. Bow towards +Z, the waterline at y = 0. Also the landing stage they call at.
    /// </summary>
    public static class BoatFactory
    {
        public const int KindCount = 3;
        public static readonly string[] Names = { "Rowing boat", "Sailing boat", "Paddle steamer" };

        /// <summary>Length (bow to stern) and how many passengers it takes.</summary>
        public static float Length(BoatKind k) => k switch { BoatKind.Rowing => 0.05f, BoatKind.Sailing => 0.07f, _ => 0.12f };

        /// <summary>How high it stands above the water (the rowers' heads, the mast, the funnel): the room it needs under a bridge.</summary>
        public static float AirDraft(BoatKind k) => k switch { BoatKind.Rowing => 0.028f, BoatKind.Sailing => 0.075f, _ => 0.045f };
        public static int Capacity(BoatKind k) => k switch { BoatKind.Rowing => 1, BoatKind.Sailing => 2, _ => 6 };
        public static float Speed(BoatKind k) => k switch { BoatKind.Rowing => 0.022f, BoatKind.Sailing => 0.034f, _ => 0.03f };

        /// <summary>Where passengers sit (local; the facing is along +Z unless noted).</summary>
        public static Vector3[] Seats(BoatKind k) => k switch
        {
            BoatKind.Rowing => new[] { new Vector3(0f, 0.0045f, 0.013f) },
            BoatKind.Sailing => new[] { new Vector3(0f, 0.005f, 0.006f), new Vector3(0f, 0.005f, -0.01f) },
            _ => new[] { new Vector3(-0.008f, 0.0215f, 0.03f), new Vector3(0.008f, 0.0215f, 0.03f), new Vector3(-0.008f, 0.0215f, 0.043f), new Vector3(0.008f, 0.0215f, 0.043f),
                new Vector3(-0.008f, 0.0215f, -0.04f), new Vector3(0.008f, 0.0215f, -0.04f) },
        };

        private static readonly Mesh[] hulls = new Mesh[KindCount];
        private static Mesh oar, sail, crew;

        /// <summary>The boat without its moving parts (the oars, the sail).</summary>
        public static Mesh Hull(BoatKind k)
        {
            if (hulls[(int)k] != null) return hulls[(int)k];
            var b = new MeshBuilder();
            switch (k)
            {
                case BoatKind.Rowing:
                    Shell(b, 0.05f, 0.0135f, 0.0075f, Pal.Hex("#A8693A"), Pal.Hex("#D9A066"), Pal.Hex("#8A5530"));
                    foreach (float z in new[] { -0.004f, 0.012f }) b.AddBox(new Vector3(0f, 0.0042f, z), new Vector3(0.012f, 0.0012f, 0.004f), Pal.Hex("#C98E55"));   // thwarts
                    break;
                case BoatKind.Sailing:
                    Shell(b, 0.07f, 0.018f, 0.0085f, Pal.Hex("#F4F1EA"), Pal.Hex("#E4D8C2"), Pal.Hex("#2F6DB5"));
                    b.AddCylinder(new Vector3(0f, 0.003f, 0.016f), 0.0011f, 0.0007f, 0.07f, 6, Pal.Hex("#8A5A33"), Pal.Hex("#A87447"));   // mast
                    b.AddBox(new Vector3(0f, -0.006f, 0.0f), new Vector3(0.0012f, 0.008f, 0.014f), Pal.Hex("#2F6DB5"));                      // keel
                    break;
                default:
                    Shell(b, 0.12f, 0.032f, 0.012f, Pal.Hex("#2E4D3E"), Pal.Hex("#C9B79A"), Pal.Hex("#B83A2E"));
                    var white = Pal.Hex("#F3EFE6");
                    b.AddBox(new Vector3(0f, 0.0125f, 0.0f), new Vector3(0.026f, 0.0016f, 0.1f), Pal.Hex("#B89B74"));               // deck
                    b.AddBox(new Vector3(0f, 0.017f, -0.005f), new Vector3(0.02f, 0.008f, 0.045f), white, Pal.Hex("#D5CBB8"), white);  // cabin
                    for (int i = 0; i < 4; i++) b.AddBox(new Vector3(0.0101f, 0.0175f, -0.022f + i * 0.011f), new Vector3(0.0006f, 0.003f, 0.005f), Pal.Hex("#2B3A55"));   // windows
                    for (int i = 0; i < 4; i++) b.AddBox(new Vector3(-0.0101f, 0.0175f, -0.022f + i * 0.011f), new Vector3(0.0006f, 0.003f, 0.005f), Pal.Hex("#2B3A55"));
                    b.AddCylinder(new Vector3(0f, 0.021f, 0.0f), 0.0045f, 0.0042f, 0.022f, 10, Pal.Hex("#C8352B"), Pal.Hex("#C8352B"));   // funnel
                    b.AddCylinder(new Vector3(0f, 0.039f, 0.0f), 0.0046f, 0.0046f, 0.004f, 10, Pal.Hex("#1E1E22"), Pal.Hex("#1E1E22"));
                    foreach (int side in new[] { -1, 1 })
                    {
                        // Paddle boxes: half drums on each side.
                        b.Push(new Vector3(side * 0.019f, 0.006f, 0.0f), Quaternion.Euler(0f, 0f, 90f));
                        b.AddCylinder(new Vector3(0f, -0.004f, 0f), 0.011f, 0.011f, 0.008f, 12, white, Pal.Hex("#B83A2E"), true, true);
                        b.Pop();
                    }
                    // Rails round the passenger decks fore and aft, and benches.
                    foreach (float z in new[] { 0.036f, -0.04f }) b.AddBox(new Vector3(0f, 0.0145f, z), new Vector3(0.022f, 0.0018f, 0.004f), Pal.Hex("#8A5A33"));
                    break;
            }
            return hulls[(int)k] = b.ToMesh("Boat_" + k);
        }

        /// <summary>One oar (pivoting at its oarlock, the blade outwards along +X).</summary>
        public static Mesh Oar()
        {
            if (oar != null) return oar;
            var b = new MeshBuilder();
            b.AddBox(new Vector3(0.011f, 0f, 0f), new Vector3(0.022f, 0.0008f, 0.0008f), Pal.Hex("#C9925A"));
            b.AddBox(new Vector3(0.022f, 0f, 0f), new Vector3(0.006f, 0.0006f, 0.0028f), Pal.Hex("#E6C08A"));
            return oar = b.ToMesh("Oar");
        }

        /// <summary>The sail on its boom, pivoting at the mast (the sail lies along -Z from the mast).</summary>
        public static Mesh Sail()
        {
            if (sail != null) return sail;
            var b = new MeshBuilder();
            var cloth = Pal.Hex("#FBF5E6");
            Vector3 foot = new(0f, 0.006f, 0f), head = new(0f, 0.068f, 0f), clew = new(0f, 0.008f, -0.034f);
            int a = b.AddVertex(foot, Vector3.right, cloth), c = b.AddVertex(head, Vector3.right, cloth), d = b.AddVertex(clew, Vector3.right, Pal.Shade(cloth, 0.95f));
            b.AddTriangle(a, d, c);
            int a2 = b.AddVertex(foot, Vector3.left, cloth), c2 = b.AddVertex(head, Vector3.left, cloth), d2 = b.AddVertex(clew, Vector3.left, Pal.Shade(cloth, 0.95f));
            b.AddTriangle(a2, c2, d2);
            b.AddBox(new Vector3(0f, 0.006f, -0.017f), new Vector3(0.001f, 0.001f, 0.035f), Pal.Hex("#8A5A33"));   // boom
            return sail = b.ToMesh("Sail");
        }

        /// <summary>The one who rows or steers (sitting; a little sailor's figure).</summary>
        public static Mesh Crew()
        {
            if (crew != null) return crew;
            var b = new MeshBuilder();
            b.AddBox(new Vector3(0f, 0.006f, 0f), new Vector3(0.0055f, 0.007f, 0.004f), Pal.Hex("#2F4E86"));      // jumper
            b.AddBlob(new Vector3(0f, 0.0125f, 0f), Vector3.one * 0.0026f, 1, 0f, 3, (_, _) => Pal.Hex("#F0C8A0"));  // head
            b.AddCylinder(new Vector3(0f, 0.0145f, 0f), 0.0028f, 0.0026f, 0.0012f, 8, Pal.Hex("#F6F3EC"), Pal.Hex("#F6F3EC"));   // cap
            return crew = b.ToMesh("Crew");
        }

        /// <summary>
        /// An open hull: the gunwale (pointed bow, a transom at the stern) over a narrower keel line, painted outside,
        /// wooden inside, with a floor and a stripe along the top.
        /// </summary>
        private static void Shell(MeshBuilder b, float length, float width, float height, Color paint, Color inside, Color stripe)
        {
            const int n = 14;
            var top = new List<Vector3>(); var keel = new List<Vector3>(); var floor = new List<Vector3>();
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n, z = -length / 2 + u * length;
                float w = width / 2 * Mathf.Min(1f, 1.25f * Mathf.Sin(Mathf.PI * (0.16f + 0.84f * u)));
                float sheer = height + Mathf.Pow(u, 3f) * height * 0.35f;   // the bow rises a little
                top.Add(new Vector3(w, sheer, z));
                keel.Add(new Vector3(w * 0.42f, -height * 0.25f, z));
                floor.Add(new Vector3(w * 0.78f, height * 0.25f, z));
            }
            for (int i = 0; i < n; i++)
                foreach (int s in new[] { -1, 1 })
                {
                    Vector3 M(Vector3 v) => new(v.x * s, v.y, v.z);
                    var t0 = M(top[i]); var t1 = M(top[i + 1]); var k0 = M(keel[i]); var k1 = M(keel[i + 1]);
                    var band0 = Vector3.Lerp(k0, t0, 0.8f); var band1 = Vector3.Lerp(k1, t1, 0.8f);
                    // Outside (paint below, a stripe at the top), facing out; inside, facing in.
                    if (s < 0) { b.AddQuad(k0, k1, band1, band0, paint); b.AddQuad(band0, band1, t1, t0, stripe); b.AddQuad(t0, t1, k1, k0, inside); }
                    else { b.AddQuad(band0, band1, k1, k0, paint); b.AddQuad(t0, t1, band1, band0, stripe); b.AddQuad(k0, k1, t1, t0, inside); }
                    // The bottom.
                    if (s > 0) b.AddQuad(k0, k1, new Vector3(-k1.x, k1.y, k1.z), new Vector3(-k0.x, k0.y, k0.z), paint);
                    // The floor inside.
                    if (s > 0) b.AddQuad(new Vector3(-floor[i].x, floor[i].y, floor[i].z), new Vector3(-floor[i + 1].x, floor[i + 1].y, floor[i + 1].z), floor[i + 1], floor[i], inside);
                }
            // The transom.
            var tl = new Vector3(-top[0].x, top[0].y, top[0].z); var tr = top[0];
            var kl = new Vector3(-keel[0].x, keel[0].y, keel[0].z); var kr = keel[0];
            b.AddQuad(tl, tr, kr, kl, paint);
            b.AddQuad(kl, kr, tr, tl, inside);
        }

        /// <summary>
        /// The landing stage: a plank deck on posts reaching out over the water (towards -Z, the front), steps up
        /// from the shore, a lamp post and a timetable board at the land end.
        /// </summary>
        public static float Jetty(MeshBuilder b)
        {
            const float W = 0.034f, L = 0.13f, Deck = 0.007f;
            var plank = Pal.Hex("#B98A5A");
            for (int i = 0; i < 16; i++)
            {
                float z = -L / 2 + (i + 0.5f) * L / 16f;
                b.AddBox(new Vector3(0f, Deck, z), new Vector3(W, 0.0014f, L / 16f - 0.0007f), i % 3 == 0 ? Pal.Shade(plank, 0.92f) : plank);
            }
            foreach (int s in new[] { -1, 1 })
                for (int i = 0; i < 5; i++)
                    b.AddCylinder(new Vector3(s * (W / 2 - 0.0015f), -0.03f, -L / 2 + 0.006f + i * (L - 0.012f) / 4f), 0.0016f, 0.0016f, Deck + 0.034f, 6, Pal.Hex("#6E4A2E"), Pal.Hex("#8A5E3A"));
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W / 2 - 0.001f), Deck + 0.006f, -L / 4), new Vector3(0.0014f, 0.0012f, L / 2), Pal.Hex("#8A5E3A"));   // rails over the water
            b.AddCylinder(new Vector3(W / 2 - 0.004f, Deck, L / 2 - 0.008f), 0.0012f, 0.001f, 0.04f, 6, Pal.Hex("#2E3B45"), Pal.Hex("#2E3B45"));
            b.AddBlob(new Vector3(W / 2 - 0.004f, Deck + 0.042f, L / 2 - 0.008f), Vector3.one * 0.003f, 1, 0f, 5, (_, _) => Pal.Glow(Pal.Hex("#FFE3A0")));
            b.AddBox(new Vector3(-W / 2 + 0.006f, Deck + 0.014f, L / 2 - 0.01f), new Vector3(0.01f, 0.008f, 0.0012f), Pal.Hex("#2F6DB5"));   // timetable
            b.AddCylinder(new Vector3(-W / 2 + 0.006f, Deck, L / 2 - 0.0095f), 0.0007f, 0.0007f, 0.01f, 4, Pal.Hex("#4A3A2A"), Pal.Hex("#4A3A2A"));
            return 0.05f;
        }
    }
}
