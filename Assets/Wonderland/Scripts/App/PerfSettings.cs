using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Wonderland
{
    /// <summary>
    /// The performance switches in the Perf panel: each a named setting with a few values, remembered between sessions
    /// (PlayerPrefs) and applied the moment it changes, so the live numbers beside it show what it costs. The systems
    /// read the values here (ground cover density, the terrain shader, distant detail, villager scheduling); the
    /// headset settings (MSAA, resolution, foveation, shadows) are applied by <see cref="Apply"/>.
    /// </summary>
    public static class PerfSettings
    {
        public sealed class Setting
        {
            public string Name, Key;
            public string[] Values;
            public int Default;
            public string Help;
            public int Value
            {
                get => Mathf.Clamp(PlayerPrefs.GetInt(Key, Default), 0, Values.Length - 1);
                set { PlayerPrefs.SetInt(Key, Mathf.Clamp(value, 0, Values.Length - 1)); PlayerPrefs.Save(); Changed?.Invoke(this); }
            }
            public string Label => $"{Name}: {Values[Value]}";
            public void Next() => Value = (Value + 1) % Values.Length;
        }

        private static Setting Make(string name, string key, int def, string help, params string[] values)
        {
            var s = new Setting { Name = name, Key = "wonderland.perf." + key, Values = values, Default = def, Help = help };
            All.Add(s);
            return s;
        }

        public static readonly List<Setting> All = new();
        /// <summary>Raised when any setting changes (after it is stored).</summary>
        public static event Action<Setting> Changed;

        public static readonly Setting Terrain = Make("Terrain", "terrain", 1, "how the land is shaded: full detail, the same look from a noise texture, or plain colours", "Full", "Light", "Simple");
        public static readonly Setting Msaa = Make("Edges", "msaa", 0, "anti-aliasing: smooth edges cost GPU time on every pixel", "MSAA 4x", "MSAA 2x", "Off");
        public static readonly Setting Shadows = Make("Shadows", "shadows", 0, "the sun's shadows", "On", "Off");
        public static readonly Setting Grass = Make("Grass", "grass", 0, "the tufts, flowers and pebbles on the meadows", "Full", "Half", "Off");
        public static readonly Setting Detail = Make("Detail", "detail", 0, "five levels chosen by how big each thing looks: extra detail up close, simpler (still recognisable) far away", "By size", "Always high");
        public static readonly Setting Villagers = Make("Villagers", "villagers", 0, "villagers and animals think on a schedule instead of every frame", "Smart", "Every frame");
        public static readonly Setting Haze = Make("Haze", "haze", 0, "the far side of the world takes on a soft tint of the sky, for depth (costs next to nothing)", "Soft", "Strong", "Off");
        public static readonly Setting WorldSize = Make("World size", "worldsize", 0, "everything smaller, to see more of the world at once (try it: the same things cost the same to draw at any size)", "100%", "80%", "60%");
        public static float WorldScale => WorldSize.Value switch { 0 => 1f, 1 => 0.8f, _ => 0.6f };
        public static readonly Setting Resolution = Make("Resolution", "resolution", 0, "the eye buffers' resolution", "100%", "90%", "80%", "70%");
        public static readonly Setting Foveation = Make("Foveation", "foveation", 0, "the edges of the view drawn at lower resolution", "High", "Highest", "Medium", "Off");

        public static bool SmartVillagers => Villagers.Value == 0;
        public static bool ReducedDetail => Detail.Value == 0;
        public static float GrassShare => Grass.Value switch { 0 => 1f, 1 => 0.5f, _ => 0f };

        public static void ResetAll() { foreach (var s in All) s.Value = s.Default; }

        /// <summary>Applies the headset settings (the systems pick up theirs from <see cref="Changed"/>).</summary>
        public static void Apply(Light sun)
        {
            if (sun != null) sun.shadows = Shadows.Value == 0 ? LightShadows.Soft : LightShadows.None;
            TerrainNoise.SetMode(Terrain.Value);
            Shader.SetGlobalVector("_WL_Haze", new Vector4(1.2f, 2.8f, Haze.Value switch { 0 => 0.35f, 1 => 0.6f, _ => 0f }, 0f));
            if (Application.isEditor) return;   // (in the editor these would be written into the project's assets)
            var urp = UniversalRenderPipeline.asset;
            if (urp != null)
            {
                urp.msaaSampleCount = Msaa.Value switch { 0 => 4, 1 => 2, _ => 1 };
                urp.renderScale = Resolution.Value switch { 0 => 1f, 1 => 0.9f, 2 => 0.8f, _ => 0.7f };
            }
            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = Resolution.Value switch { 0 => 1f, 1 => 0.9f, 2 => 0.8f, _ => 0.7f };
            try
            {
                OVRManager.foveatedRenderingLevel = Foveation.Value switch
                {
                    0 => OVRManager.FoveatedRenderingLevel.High, 1 => OVRManager.FoveatedRenderingLevel.HighTop,
                    2 => OVRManager.FoveatedRenderingLevel.Medium, _ => OVRManager.FoveatedRenderingLevel.Off,
                };
                OVRManager.useDynamicFoveatedRendering = Foveation.Value != 3;
            }
            catch (Exception) { }
        }
    }
}
