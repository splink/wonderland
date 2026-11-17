using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The terrain shader's noise as a texture, for its Light mode: the same value noise the Full mode computes per
    /// pixel (four hashes and a blend, five times), made once at start-up and read with two texture lookups. Two
    /// tileable layers in one 256 x 256 texture: RGB tile every 2 m (the meadows at 2.5/m, the colour patches at 5/m
    /// and 14/m), A tiles every 25 cm (the fine grain at 48/m; 160/m comes from the same layer, read 3.33 x smaller).
    /// The shader picks its mode from global keywords (<see cref="SetMode"/>).
    /// </summary>
    public static class TerrainNoise
    {
        private static Texture2D texture;

        public static void Ensure()
        {
            if (texture != null) return;
            const int size = 256;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "Terrain noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                pixels[y * size + x] = new Color32(
                    (byte)(Value(u, v, 5, 11) * 255f), (byte)(Value(u, v, 10, 23) * 255f),
                    (byte)(Value(u, v, 28, 37) * 255f), (byte)(Value(u, v, 12, 51) * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            Shader.SetGlobalTexture("_WLNoiseTex", texture);
        }

        /// <summary>Tileable value noise: <paramref name="cells"/> lattice cells across the tile, smoothly blended.</summary>
        private static float Value(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float H(int i, int j) => Noise.Hash(((i % cells) + cells) % cells, ((j % cells) + cells) % cells, seed);
            return Mathf.Lerp(Mathf.Lerp(H(ix, iy), H(ix + 1, iy), fx), Mathf.Lerp(H(ix, iy + 1), H(ix + 1, iy + 1), fx), fy);
        }

        /// <summary>0 Full (noise computed per pixel), 1 Light (noise from the texture), 2 Simple (no noise, no flowers).</summary>
        public static void SetMode(int mode)
        {
            Ensure();
            if (mode == 1) Shader.EnableKeyword("_WL_TERRAIN_LIGHT"); else Shader.DisableKeyword("_WL_TERRAIN_LIGHT");
            if (mode == 2) Shader.EnableKeyword("_WL_TERRAIN_SIMPLE"); else Shader.DisableKeyword("_WL_TERRAIN_SIMPLE");
        }
    }
}
