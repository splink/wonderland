using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The working world's buildings: places that bring activities with them - a taxi rank, and the rest added as the
    /// living world grows. Same parts and palette as the village.
    /// </summary>
    public static partial class HouseFactory
    {
        private static float Living(MeshBuilder b, ref Rng rng, Style s, int id, int style, out bool warp)
        {
            warp = false;
            switch (id)
            {
                case Catalog.TaxiRank: return TaxiShelter(b);
            }
            return -1f;
        }

        /// <summary>A taxi rank: a little glass shelter with a bench, and a yellow TAXI sign on a post at the kerb (front).</summary>
        private static float TaxiShelter(MeshBuilder b)
        {
            var frame = Pal.Hex("#3C4048"); var glass = Pal.Hex("#BFD6E2"); var yellow = Pal.Hex("#F2C12E");
            const float W = 0.034f, D = 0.014f, H = 0.026f;
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                b.AddBox(new Vector3(sx * W / 2, H / 2, sz * D / 2 + 0.004f), new Vector3(0.0012f, H, 0.0012f), frame);
            b.AddBox(new Vector3(0f, H / 2, D / 2 + 0.004f), new Vector3(W, H * 0.8f, 0.0006f), glass);   // the back
            foreach (int sx in new[] { -1, 1 }) b.AddBox(new Vector3(sx * W / 2, H / 2, 0.004f), new Vector3(0.0006f, H * 0.8f, D), glass);
            b.AddBox(new Vector3(0f, H + 0.0008f, 0.004f), new Vector3(W + 0.004f, 0.0016f, D + 0.004f), frame);   // the roof
            b.AddBox(new Vector3(0f, 0.006f, 0.008f), new Vector3(W * 0.8f, 0.0014f, 0.005f), Pal.Hex("#9A6B42"));   // the bench
            // The sign at the kerb: TAXI, lit, on a post.
            b.AddCylinder(new Vector3(W / 2 + 0.006f, 0f, -0.012f), 0.0009f, 0.0009f, 0.034f, 6, frame, frame, true);
            Lettering.Board(b, "TAXI", new Vector3(W / 2 + 0.006f, 0.036f, -0.012f), Vector3.back, 0.0045f, Pal.Glow(yellow), Pal.TrainBlack, frame);
            b.AddBox(new Vector3(0f, 0.0004f, -0.004f), new Vector3(W + 0.016f, 0.0008f, 0.034f), Pal.Hex("#B9B4AA"));   // paving
            return 0.042f;
        }
    }
}
