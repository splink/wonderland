using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// The diorama's light: one warm key light that casts the miniature's shadows, plus the storybook ambient read by the
    /// Wonderland shaders (sky / ground fill and a cool shadow tint). Living-room friendly: soft and warm.
    /// </summary>
    public static class WonderlandLighting
    {
        public static readonly Color Sky = new(0.62f, 0.66f, 0.72f);
        public static readonly Color Ground = new(0.42f, 0.36f, 0.28f);
        public static readonly Color ShadowTint = new(0.78f, 0.84f, 1.0f);
        public static readonly Color KeyColour = new(1.0f, 0.93f, 0.8f);

        public static void Apply()
        {
            // Vectors, not colours: the values are already linear and must not be converted again.
            Shader.SetGlobalVector("_WL_SkyColor", (Vector4)Sky.linear);
            Shader.SetGlobalVector("_WL_GroundColor", (Vector4)Ground.linear);
            Shader.SetGlobalVector("_WL_ShadowTint", (Vector4)ShadowTint.linear);
        }

        public static Light CreateKeyLight(Transform parent = null)
        {
            var light = new GameObject("Key Light").AddComponent<Light>();
            if (parent) light.transform.SetParent(parent, false);
            Configure(light);
            return light;
        }

        public static void Configure(Light light)
        {
            light.type = LightType.Directional;
            light.color = KeyColour;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;
            light.shadowBias = 0.02f;
            light.shadowNormalBias = 0.25f;
            light.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
            RenderSettings.skybox = null;
            Apply();
        }
    }
}
