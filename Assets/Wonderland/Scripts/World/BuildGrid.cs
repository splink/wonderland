using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The building grid (on by default): road corners land on its points, so roads come out straight, parallel and
    /// square to each other; buildings placed near a road face it and line up along it, elsewhere they sit on the grid
    /// turned in quarter turns. While a road or building is being placed, the grid shows faintly on the land around
    /// the pointer. Off: free drawing and placing as before.
    /// </summary>
    public static class BuildGrid
    {
        public const float Size = 0.05f;
        private const string Key = "wonderland.grid";

        public static bool Snap
        {
            get => PlayerPrefs.GetInt(Key, 1) == 1;
            set { PlayerPrefs.SetInt(Key, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static Vector2 Point(Vector2 p) => new(Mathf.Round(p.x / Size) * Size, Mathf.Round(p.y / Size) * Size);

        /// <summary>A quarter turn nearest <paramref name="yaw"/>.</summary>
        public static float Quarter(float yaw) => Mathf.Round(yaw / 90f) * 90f;

        private static float shown = -1f;

        /// <summary>Shows the grid on the land (around the pointer) while placing roads or buildings, if snapping is on.</summary>
        public static void Show(bool placing)
        {
            float strength = placing && Snap ? 1f : 0f;
            if (strength == shown) return;
            shown = strength;
            Shader.SetGlobalVector("_WLGrid", new Vector4(Size, strength, 0f, 0f));
        }
    }
}
