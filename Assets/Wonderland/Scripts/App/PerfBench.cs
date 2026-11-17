using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Wonderland
{
    /// <summary>
    /// What costs the frame time, measured on the headset: each variant switches one thing off (shadows, MSAA, ground
    /// cover, trees, buildings, figures, vehicles, the terrain, the simulation...) for a moment and samples the frame
    /// rate with the headset's own app GPU and CPU times; what a variant saves against the baseline is what that thing
    /// costs. <see cref="Sweep"/> is what the Perf panel's Measure button runs on the current view.
    /// A file named "bench" in the app's data folder (adb push) runs the long version at start-up on saved worlds
    /// ("worlds 1 4"; 0 or nothing: the current one) with the clocks held, in a fixed view; results in logcat
    /// ("Wonderland bench") and bench-results.txt. Nothing is saved while it runs; everything is put back afterwards.
    /// </summary>
    public sealed class PerfBench : MonoBehaviour
    {
        public WonderlandWorld World;
        public WorldAnchor Anchor;
        public WorldMaterials Materials;

        /// <summary>True while a sweep runs (the app doesn't save then: a benchmarked world must not replace the player's).</summary>
        public static bool Running { get; private set; }

        public static string Trigger => Path.Combine(Application.persistentDataPath, "bench");
        public static string Results => Path.Combine(Application.persistentDataPath, "bench-results.txt");

        public struct Result { public string Name; public float Fps, GpuMs, CpuMs, MainMs, SimMs; }

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
                if (!loaded) continue;
                Log($"--- {(w == 0 ? "the current world" : $"world {w}")}: {World.Buildings.Houses.Count} buildings, {World.Forest.Trees.Count} trees, {World.People.People.Count} people, {World.Animals.Animals.Count} animals, {World.Trains.Trains.Count} trains, {World.Traffic.Vehicles.Count} vehicles, land {World.Terrain.LandArea():0.0} m2");
                // A fixed view: the world's middle a metre ahead and below the eyes, as if leaning over the table.
                var head = Camera.main != null ? Camera.main.transform : transform;
                var land = World.Terrain.LandBounds();
                var fwd = head.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
                var centre = World.transform.TransformVector(new Vector3(land.center.x, 0f, land.center.y));
                World.transform.position = head.position + fwd * 1.0f + Vector3.down * 0.75f - centre;
                yield return Sweep(World, 3f, 5f, null, r => Log(Line(r)));
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

        /// <summary>
        /// Every variant in turn on the world as it is in view: <paramref name="settle"/> seconds to settle, then
        /// <paramref name="measure"/> seconds sampled. Calls <paramref name="progress"/> with what is being measured
        /// and <paramref name="done"/> with each result. The clock stands at midday meanwhile; all is put back.
        /// </summary>
        public static IEnumerator Sweep(WonderlandWorld world, float settle, float measure, System.Action<string, int, int> progress, System.Action<Result> done)
        {
            bool wasRunning = Running;
            Running = true;
            var clockMode = world.Clock.Mode; float hour = world.Clock.Hour;
            world.Clock.Hour = 12f; world.Clock.Mode = ClockMode.Paused;
            var urp = UniversalRenderPipeline.asset;
            Light sun = null; foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) sun = l;
            var renderersOf = new Dictionary<string, List<Renderer>>();
            List<Renderer> Of(string key, params Component[] roots)
            {
                if (renderersOf.TryGetValue(key, out var list)) return list;
                list = new List<Renderer>();
                foreach (var r in roots) if (r != null) foreach (var x in r.GetComponentsInChildren<Renderer>(false)) if (x.enabled) list.Add(x);
                return renderersOf[key] = list;
            }
            void Show(List<Renderer> list, bool on) { foreach (var r in list) if (r != null) r.enabled = on; }
            var logic = new Behaviour[] { world.People, world.Traffic, world.Trains, world.Animals, world.Boats, world.Sky, world.Buildings, world.Cctv };
            var all = new List<Renderer>(); foreach (var r in world.GetComponentsInChildren<Renderer>(false)) if (r.enabled) all.Add(r);
            int msaa = urp != null ? urp.msaaSampleCount : 4;
            var shadows = sun != null ? sun.shadows : LightShadows.None;
            int terrainMode = PerfSettings.Terrain.Value, detail = PerfSettings.Detail.Value;
            var variants = new List<Variant>
            {
                new() { Name = "baseline" },
                new() { Name = "shadows", On = () => { if (sun != null) sun.shadows = LightShadows.None; }, Off = () => { if (sun != null) sun.shadows = shadows; } },
                new() { Name = "edges (MSAA)", On = () => { if (urp != null) urp.msaaSampleCount = 1; }, Off = () => { if (urp != null) urp.msaaSampleCount = msaa; } },
                new() { Name = "terrain shading", On = () => Shader.SetGlobalFloat("_WLTerrainMode", 2), Off = () => Shader.SetGlobalFloat("_WLTerrainMode", terrainMode) },
                new() { Name = "terrain", On = () => Show(Of("terrain", world.TerrainView), false), Off = () => Show(Of("terrain", world.TerrainView), true) },
                new() { Name = "grass", On = () => Show(Of("cover", world.Cover), false), Off = () => Show(Of("cover", world.Cover), true) },
                new() { Name = "trees", On = () => Show(Of("trees", world.Forest), false), Off = () => Show(Of("trees", world.Forest), true) },
                new() { Name = "buildings", On = () => Show(Of("houses", world.Buildings), false), Off = () => Show(Of("houses", world.Buildings), true) },
                new() { Name = "villagers and animals", On = () => Show(Of("life", world.People, world.Animals), false), Off = () => Show(Of("life", world.People, world.Animals), true) },
                new() { Name = "trains, cars and boats", On = () => Show(Of("vehicles", world.Trains, world.Traffic, world.Boats), false), Off = () => Show(Of("vehicles", world.Trains, world.Traffic, world.Boats), true) },
                new() { Name = "railway, roads and water", On = () => Show(Of("lines", world.Track, world.Paths, world.Streams, world.Bridges), false), Off = () => Show(Of("lines", world.Track, world.Paths, world.Streams, world.Bridges), true) },
                new() { Name = "simulation", On = () => { foreach (var b in logic) if (b != null) b.enabled = false; }, Off = () => { foreach (var b in logic) if (b != null) b.enabled = true; } },
                new() { Name = "everything (passthrough and palette left)", On = () => Show(all, false), Off = () => Show(all, true) },
                new() { Name = "baseline again" },
            };
            for (int i = 0; i < variants.Count; i++)
            {
                var v = variants[i];
                progress?.Invoke(v.Name, i, variants.Count);
                v.On?.Invoke();
                yield return new WaitForSeconds(settle);
                var r = new Result { Name = v.Name };
                yield return Sample(measure, x => r = x);
                r.Name = v.Name;
                v.Off?.Invoke();
                done?.Invoke(r);
            }
            world.Clock.Hour = hour; world.Clock.Mode = clockMode;
            Running = wasRunning;
        }

        private static IEnumerator Sample(float measure, System.Action<Result> result)
        {
            int frames = 0, metrics = 0, timed = 0; float t = 0f;
            double appGpu = 0, appCpu = 0, main = 0, sim = 0;
            var timing = new FrameTiming[1];
            while (t < measure)
            {
                yield return null;
                t += Time.unscaledDeltaTime; frames++;
                var g = OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_GpuTime_Float);
                if (g.HasValue) { appGpu += g.Value; appCpu += OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_CpuTime_Float) ?? 0f; metrics++; }
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].cpuMainThreadFrameTime > 0) { main += timing[0].cpuMainThreadFrameTime; timed++; }
                sim += SimCost.Total;
            }
            // (the headset reports times in seconds)
            result(new Result
            {
                Fps = frames / Mathf.Max(t, 1e-3f),
                GpuMs = metrics > 0 ? (float)(appGpu / metrics * 1000.0) : 0f,
                CpuMs = metrics > 0 ? (float)(appCpu / metrics * 1000.0) : 0f,
                MainMs = timed > 0 ? (float)(main / timed) : 0f,
                SimMs = (float)(sim / Mathf.Max(frames, 1)),
            });
        }

        public static string Line(Result r) => $"{r.Name,-44} {r.Fps,5:0.0} fps  app GPU {r.GpuMs,5:0.0} ms  app CPU {r.CpuMs,5:0.0} ms  main {r.MainMs,4:0.0} ms  sim {r.SimMs:0.0} ms";

        private static void Log(string s)
        {
            Debug.Log("Wonderland bench: " + s);
            try { File.AppendAllText(Results, s + "\n"); } catch (System.Exception) { }
        }
    }
}
