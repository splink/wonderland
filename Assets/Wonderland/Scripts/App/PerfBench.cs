using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Wonderland
{
    /// <summary>
    /// An on-device experiment to find what limits the frame rate: with a saved world in a fixed view, each variant
    /// switches one thing off (shadows, MSAA, resolution, ground cover, trees, buildings, figures, vehicles, the
    /// simulation...) for a few seconds and logs the frame rate with the headset's own app GPU and CPU times, the GPU
    /// clock held at one level so the times compare. Whatever moves the GPU time most is the bottleneck.
    /// Started by a file named "bench" in the app's data folder (adb push), which it deletes; its text names the worlds
    /// ("worlds 1 4"; 0 or nothing: the current one). Results in logcat ("Wonderland bench") and bench-results.txt.
    /// Nothing is saved while it runs; the current world, its place and the clocks are put back afterwards.
    /// </summary>
    public sealed class PerfBench : MonoBehaviour
    {
        public WonderlandWorld World;
        public WorldAnchor Anchor;
        public WorldMaterials Materials;
        private const float Settle = 3f, Measure = 5f;

        /// <summary>True while it runs (the app doesn't save then: a benchmarked world must not replace the player's).</summary>
        public static bool Running { get; private set; }

        public static string Trigger => Path.Combine(Application.persistentDataPath, "bench");
        public static string Results => Path.Combine(Application.persistentDataPath, "bench-results.txt");

        private IEnumerator Start()
        {
            if (!File.Exists(Trigger)) yield break;
            string spec = File.ReadAllText(Trigger).Trim();
            File.Delete(Trigger);
            Running = true;
            while (World == null || World.Terrain == null) yield return null;
            yield return new WaitForSeconds(12f);   // the anchor restored, the forest and ground cover built
            var worlds = new List<int>();
            foreach (var word in spec.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(word, out int n)) worlds.Add(n);
            if (worlds.Count == 0) worlds.Add(0);
            int slot = WonderlandWorld.CurrentSlot;
            bool anchorWas = Anchor != null && Anchor.enabled;
            if (Anchor != null) Anchor.enabled = false;
            var pos = World.transform.position; var rot = World.transform.rotation;
            var cpuLevel = OVRManager.suggestedCpuPerfLevel; var gpuLevel = OVRManager.suggestedGpuPerfLevel;
            OVRManager.suggestedCpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
            OVRManager.suggestedGpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
            Log($"=== {System.DateTime.Now:yyyy-MM-dd HH:mm} spec '{spec}'; clocks held at sustained high");
            foreach (int w in worlds)
            {
                bool loaded = true;
                if (w > 0)
                {
                    try { World.LoadSlot(w, Materials); }
                    catch (System.Exception e) { Log($"world {w}: could not load ({e.Message})"); loaded = false; }
                    if (loaded) yield return new WaitForSeconds(15f);   // the forest and ground cover rebuilt, villagers out
                }
                if (loaded) yield return Run(w == 0 ? "the current world" : $"world {w}");
            }
            if (worlds.Exists(w => w > 0))
            {
                try { World.Load(Materials); } catch (System.Exception e) { Log("could not restore the current world: " + e.Message); }
                WonderlandWorld.CurrentSlot = slot;
            }
            World.transform.SetPositionAndRotation(pos, rot);
            if (Anchor != null) Anchor.enabled = anchorWas;
            OVRManager.suggestedCpuPerfLevel = cpuLevel; OVRManager.suggestedGpuPerfLevel = gpuLevel;
            Running = false;
            Log("done");
        }

        private struct Variant { public string Name; public System.Action On, Off; }

        private IEnumerator Run(string label)
        {
            Log($"--- {label}: {World.Buildings.Houses.Count} buildings, {World.Forest.Trees.Count} trees, {World.People.People.Count} people, {World.Animals.Animals.Count} animals, {World.Trains.Trains.Count} trains, {World.Traffic.Vehicles.Count} vehicles, land {World.Terrain.LandArea():0.0} m2");
            // A fixed view: the world's middle a metre ahead and below the eyes, as if leaning over the table.
            var head = Camera.main != null ? Camera.main.transform : transform;
            var land = World.Terrain.LandBounds();
            var fwd = head.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            var centre = World.transform.TransformVector(new Vector3(land.center.x, 0f, land.center.y));
            World.transform.position = head.position + fwd * 1.0f + Vector3.down * 0.75f - centre;
            var clockMode = World.Clock.Mode; float hour = World.Clock.Hour;
            World.Clock.Hour = 12f; World.Clock.Mode = ClockMode.Paused;

            var urp = UniversalRenderPipeline.asset;
            Light sun = null; foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) sun = l;
            var renderersOf = new Dictionary<string, List<Renderer>>();
            List<Renderer> Of(string key, params Component[] roots)
            {
                if (renderersOf.TryGetValue(key, out var list)) return list;
                list = new List<Renderer>();
                foreach (var r in roots) if (r != null) foreach (var x in r.GetComponentsInChildren<Renderer>(false)) if (x.enabled) list.Add(x);
                return renderersOf[key] = list;
            }
            void Show(List<Renderer> list, bool on) { foreach (var r in list) if (r != null) r.enabled = on; }
            var logic = new Behaviour[] { World.People, World.Traffic, World.Trains, World.Animals, World.Boats, World.Sky, World.Buildings, World.Cctv };
            var all = new List<Renderer>(); foreach (var r in World.GetComponentsInChildren<Renderer>(false)) if (r.enabled) all.Add(r);
            int msaa = urp.msaaSampleCount; float scale = UnityEngine.XR.XRSettings.eyeTextureResolutionScale; float bias = QualitySettings.lodBias;
            var shadows = sun != null ? sun.shadows : LightShadows.None;

            var variants = new List<Variant>
            {
                new() { Name = "baseline" },
                new() { Name = "no shadows", On = () => { if (sun != null) sun.shadows = LightShadows.None; }, Off = () => { if (sun != null) sun.shadows = shadows; } },
                new() { Name = "MSAA 1x", On = () => urp.msaaSampleCount = 1, Off = () => urp.msaaSampleCount = msaa },
                new() { Name = "resolution 60%", On = () => UnityEngine.XR.XRSettings.eyeTextureResolutionScale = 0.6f, Off = () => UnityEngine.XR.XRSettings.eyeTextureResolutionScale = scale },
                new() { Name = "full detail everywhere (no stand-ins)", On = () => QualitySettings.lodBias = 100f, Off = () => QualitySettings.lodBias = bias },
                new() { Name = "no ground cover", On = () => Show(Of("cover", World.Cover), false), Off = () => Show(Of("cover", World.Cover), true) },
                new() { Name = "no trees", On = () => Show(Of("trees", World.Forest), false), Off = () => Show(Of("trees", World.Forest), true) },
                new() { Name = "no buildings", On = () => Show(Of("houses", World.Buildings), false), Off = () => Show(Of("houses", World.Buildings), true) },
                new() { Name = "no villagers or animals", On = () => Show(Of("life", World.People, World.Animals), false), Off = () => Show(Of("life", World.People, World.Animals), true) },
                new() { Name = "no trains, cars or boats", On = () => Show(Of("vehicles", World.Trains, World.Traffic, World.Boats), false), Off = () => Show(Of("vehicles", World.Trains, World.Traffic, World.Boats), true) },
                new() { Name = "no railway, roads or water", On = () => Show(Of("lines", World.Track, World.Paths, World.Streams, World.Bridges), false), Off = () => Show(Of("lines", World.Track, World.Paths, World.Streams, World.Bridges), true) },
                new() { Name = "no terrain", On = () => Show(Of("terrain", World.TerrainView), false), Off = () => Show(Of("terrain", World.TerrainView), true) },
                new() { Name = "simulation stopped (drawn, not moving)", On = () => { foreach (var b in logic) if (b != null) b.enabled = false; }, Off = () => { foreach (var b in logic) if (b != null) b.enabled = true; } },
                new() { Name = "nothing drawn (passthrough and palette only)", On = () => Show(all, false), Off = () => Show(all, true) },
                new() { Name = "baseline again" },
            };
            foreach (var v in variants)
            {
                v.On?.Invoke();
                yield return new WaitForSeconds(Settle);
                yield return Sample(v.Name);
                v.Off?.Invoke();
            }
            World.Clock.Hour = hour; World.Clock.Mode = clockMode;
        }

        private IEnumerator Sample(string name)
        {
            int frames = 0; float t = 0f, worst = 0f;
            double appGpu = 0, appCpu = 0, gpuUtil = 0, gpuMhz = 0, main = 0; int metrics = 0, timed = 0;
            var timing = new FrameTiming[1];
            var sim = new float[SimCost.Count];
            while (t < Measure)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                t += dt; frames++; worst = Mathf.Max(worst, dt);
                var g = OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_GpuTime_Float);
                if (g.HasValue)
                {
                    appGpu += g.Value;
                    appCpu += OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_CpuTime_Float) ?? 0f;
                    gpuUtil += OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.System_GpuUtilPercentage_Float) ?? 0f;
#pragma warning disable 618
                    gpuMhz += OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.Device_GpuClockFrequencyInMHz_Float) ?? 0f;
#pragma warning restore 618
                    metrics++;
                }
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].cpuMainThreadFrameTime > 0) { main += timing[0].cpuMainThreadFrameTime; timed++; }
                for (int i = 0; i < SimCost.Count; i++) sim[i] += SimCost.Ms[i];
            }
            var sb = new StringBuilder();
            sb.Append($"{name,-44} {frames / t,5:0.0} fps  frame {t / frames * 1000f,5:0.0} ms  worst {worst * 1000f,5:0.0} ms");
            // (the headset reports times in seconds and utilisation as a fraction)
            if (metrics > 0) sb.Append($"  app GPU {appGpu / metrics * 1000.0,5:0.0} ms  app CPU {appCpu / metrics * 1000.0,5:0.0} ms  GPU busy {gpuUtil / metrics * 100.0,3:0}%  GPU {gpuMhz / metrics,4:0} MHz");
            if (timed > 0) sb.Append($"  main {main / timed,4:0.0} ms");
            float total = 0f; for (int i = 0; i < SimCost.Count; i++) total += sim[i] / frames;
            sb.Append($"  sim {total:0.0} ms");
            Log(sb.ToString());
        }

        private static void Log(string s)
        {
            Debug.Log("Wonderland bench: " + s);
            try { File.AppendAllText(Results, s + "\n"); } catch (System.Exception) { }
        }
    }
}
