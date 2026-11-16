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

        public static readonly Color NightSky = new(0.2f, 0.25f, 0.42f);
        public static readonly Color NightGround = new(0.11f, 0.1f, 0.13f);
        public static readonly Color DuskKey = new(1.0f, 0.68f, 0.42f);
        public static readonly Color MoonKey = new(0.55f, 0.66f, 1.0f);
        private static readonly int NightId = Shader.PropertyToID("_WL_Night");

        public static void Apply() => Apply(Sky, Ground, 0f);

        private static void Apply(Color sky, Color ground, float night)
        {
            // Vectors, not colours: the values are already linear and must not be converted again.
            Shader.SetGlobalVector("_WL_SkyColor", (Vector4)sky.linear);
            Shader.SetGlobalVector("_WL_GroundColor", (Vector4)ground.linear);
            Shader.SetGlobalVector("_WL_ShadowTint", (Vector4)ShadowTint.linear);
            Shader.SetGlobalFloat(NightId, night);   // windows and lamps glow brighter
        }

        /// <summary>
        /// The light of the village clock's hour: the sun climbs from the east in the morning and sets golden in the
        /// west; at night a dim blue moon, a darker ambient, and every lamp and lit window glows. The light turns in
        /// small steps so shadows don't shimmer. <paramref name="worldYaw"/> keeps the sun's path fixed to the miniature.
        /// </summary>
        public static void SetTimeOfDay(Light key, float hour, float daylight, float worldYaw, float overcast = 0f)
        {
            float night = 1f - daylight;
            daylight *= 1f - overcast * 0.35f;   // a shower: greyer, dimmer, softer
            float low = Mathf.Clamp01(1f - Mathf.Abs(daylight - 0.5f) * 2f) + (daylight > 0.5f && (hour < 9f || hour > 17f) ? 0.4f : 0f);   // dawn and dusk
            Apply(Color.Lerp(NightSky, Color.Lerp(Sky, new Color(0.72f, 0.62f, 0.6f), Mathf.Clamp01(low) * 0.4f), daylight), Color.Lerp(NightGround, Ground, daylight), night);
            if (key == null) return;
            key.color = Color.Lerp(MoonKey, Color.Lerp(KeyColour, DuskKey, Mathf.Clamp01(low)), daylight);
            key.intensity = Mathf.Lerp(0.32f, 1.25f, daylight) * (1f - overcast * 0.3f);
            // The sun's arc: east at 6, overhead-ish at 13, west at 20; the moon stands high in the south.
            float t = Mathf.InverseLerp(6f, 20f, hour);
            float sunElevation = Mathf.Lerp(22f, 58f, Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI));
            float sunYaw = Mathf.Lerp(-110f, 40f, Mathf.Clamp01(t));
            float elevation = Mathf.LerpAngle(62f, sunElevation, daylight), yaw = Mathf.LerpAngle(-150f, sunYaw, daylight) + worldYaw;
            var target = Quaternion.Euler(elevation, yaw, 0f);
            if (Quaternion.Angle(key.transform.rotation, target) > 0.25f) key.transform.rotation = target;
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
