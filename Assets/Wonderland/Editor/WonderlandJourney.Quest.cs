using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// A benchmark on a real player's world (Tests/Saves/quest-world.json, pulled from the headset; not in git): loads
        /// it, lives a while, and times each system per frame (editor CPU, simulation only), with allocations and a
        /// census. Logs/questworld-results.txt. Skipped when the save isn't there.
        /// </summary>
        public static void QuestWorld()
        {
            var report = new StringBuilder();
            string save = Environment.GetEnvironmentVariable("WL_SAVE") ?? "Tests/Saves/quest-world.json";
            if (!File.Exists(save)) { report.AppendLine("SKIP  no " + save); }
            else
            {
                var w = new GameObject("Quest world").AddComponent<WonderlandWorld>();
                w.transform.position = new Vector3(400f, 0f, 0f);
                var load = Stopwatch.StartNew();
                w.Deserialize(File.ReadAllText(save), Materials());
                report.AppendLine($"Loaded in {load.ElapsedMilliseconds} ms: {w.People.People.Count} people, {w.Buildings.Houses.Count} buildings, {w.Animals.Animals.Count} animals, {w.Trains.Trains.Count} trains, {w.Forest.Trees.Count} trees");
                load.Restart();
                w.People.Grid.Complete();
                report.AppendLine($"Walk grid: {w.People.Grid.Cells} cells, {w.People.Grid.Regions} regions, {load.ElapsedMilliseconds} ms");
                report.AppendLine("Exact walkability, per check: " + w.People.ProfileWalkable(20000));
                if (Environment.GetEnvironmentVariable("WL_PROBE_ONLY") == null) { Bench(w, 120f, null); Bench(w, 30f, report); }
                SoundProbe(w, report);
                OffTrackProbe(w, Environment.GetEnvironmentVariable("WL_PROBE_ONLY") == null ? 400f : 30f, report);
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            File.WriteAllText("Logs/questworld-results.txt", report.ToString());
            Debug.Log("Quest world benchmark\n" + report);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Bench(WonderlandWorld w, float seconds, StringBuilder report)
        {
            string[] names = { "trains", "people", "traffic", "animals", "flights", "sky", "sound", "cctv" };
            var sum = new double[names.Length]; var worst = new double[names.Length];
            var watch = new Stopwatch();
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            int frames = 0;
            double total = 0, worstFrame = 0;
            for (float s = 0f; s < seconds; s += Dt)
            {
                w.Clock.Tick(Dt);
                double frame = 0;
                for (int k = 0; k < names.Length; k++)
                {
                    watch.Restart();
                    switch (k)
                    {
                        case 0: w.Trains.Step(Dt); break;
                        case 1: w.People.Step(Dt); break;
                        case 2: w.Traffic.Step(Dt); break;
                        case 3: w.Animals.Step(Dt); break;
                        case 4: w.Buildings.StepFlights(Dt); break;
                        case 5: w.Sky.Step(Dt); break;
                        case 6: w.Sounds.Step(Dt); break;
                        default: w.Cctv.Step(Dt); break;
                    }
                    double ms = watch.Elapsed.TotalMilliseconds;
                    sum[k] += ms; worst[k] = Math.Max(worst[k], ms); frame += ms;
                }
                total += frame; worstFrame = Math.Max(worstFrame, frame); frames++;
            }
            if (report == null) return;
            long alloc = GC.GetAllocatedBytesForCurrentThread() - bytes;
            report.AppendLine($"{seconds:0} s at 72 fps from {w.Clock.Label}: simulation {total / frames:0.00} ms per frame on average, worst {worstFrame:0.0} ms; {alloc / 1024f / frames:0.0} KB allocated per frame");
            for (int k = 0; k < names.Length; k++) report.AppendLine($"  {names[k],-8} {sum[k] / frames,6:0.000} ms avg, worst {worst[k],6:0.00} ms");
            report.AppendLine("  " + w.People.Census() + $"; {w.People.Diagnostics}");
        }

        /// <summary>
        /// Every car of every train, where it is actually drawn, checked against the rails each frame for a while (with
        /// the whole world living, so trains stop at stations, turn back, wait for each other). Reports what each train
        /// was doing when a car was more than 6 mm off the track.
        /// </summary>
        private static void OffTrackProbe(WonderlandWorld w, float seconds, StringBuilder report)
        {
            var net = w.Track.Network;
            for (int j = 0; j < net.Joints.Count; j++)
            {
                var jt = net.Joints[j];
                var sb = new StringBuilder($"  joint {j} at {jt.Position.x:0.000},{jt.Position.z:0.000} trunk e{jt.Trunk.Edge}/{jt.Trunk.End}:");
                bool off = false;
                foreach (var e in jt.Ends) { float d = Vector3.Distance(net.EndPoint(e), jt.Position); off |= d > 0.005f; sb.Append($" e{e.Edge}/{e.End} {d * 100f:0.0} cm"); }
                if (off || jt.Ends.Count != 3) report.AppendLine(sb.ToString() + (off ? "  <- ends apart" : "") + (jt.Ends.Count != 3 ? $"  <- {jt.Ends.Count} ends" : ""));
            }
            for (int e = 0; e < net.Edges.Count; e++) report.AppendLine($"  edge {e}: {net.Edges[e].Layout.Points.Count} points, {net.Edges[e].Layout.Length:0.000} m, closed {net.Edges[e].Layout.Closed}, joints {net.Edges[e].Joint[0]}/{net.Edges[e].Joint[1]}, ends {net.Edges[e].Layout.Points[0]} {net.Edges[e].Layout.Points[^1]}");
            int bad = 0, frames = 0; float worst = 0f; var seen = new System.Collections.Generic.HashSet<string>();
            for (float s = 0f; s < seconds; s += Dt)
            {
                w.Clock.Tick(Dt); w.Trains.Step(Dt); w.People.Step(Dt);
                frames++;
                foreach (var t in w.Trains.Trains)
                    foreach (var body in w.Trains.CarBodies(t))
                    {
                        var p = w.transform.InverseTransformPoint(body.position) - Vector3.up * TrackSystem.RailTop;
                        float d = w.Track.Network.DistanceTo(p);
                        if (d <= 0.012f) continue;   // (a car's middle sits up to ~8 mm inside a tight curve between its bogies)
                        bad++; worst = Mathf.Max(worst, d);
                        string why = w.Trains.Debug(t);
                        if (seen.Count < 12 && seen.Add(body.name + (d > 0.03f ? "big" : "small") + (int)(s / 10f))) report.AppendLine($"  OFF {d * 100f:0.0} cm at {s:0.0} s: {body.name} - {why}");
                    }
            }
            // One way: does the railway run one way, are there traps, and are the trains spread out?
            var counts = new Dictionary<int, int>();
            foreach (var t in w.Trains.Trains) { int c = net.CircuitOf(t.Path[^1].Edge); counts.TryGetValue(c, out int k); counts[c] = k + 1; }
            var spread = new StringBuilder();
            foreach (var kv in counts) spread.Append($" circuit {kv.Key}: {kv.Value}");
            report.AppendLine($"  one way {net.OneWay}{(net.OneWay ? "" : " (" + net.TwoWayReason + ")")}, {net.Circuits} circuits, {net.Traps} traps; trains by circuit:{spread}; reversals {w.Trains.Trains.Sum(t => t.Reversals)}");
            report.AppendLine(bad == 0 ? $"PASS  Every car stays on the rails ({seconds:0} s, {w.Trains.Trains.Count} trains)" : $"FAIL  Cars off the rails (more than 1.2 cm) in {bad} car-frames over {frames} frames, worst {worst * 100f:0.0} cm");
        }

        /// <summary>What the player would hear on this world, standing at a few spots around it at day and at night.</summary>
        private static void SoundProbe(WonderlandWorld w, StringBuilder report)
        {
            foreach (SoundLoop k in Enum.GetValues(typeof(SoundLoop)))
                try { var c = SoundBank.Loop(k); report.AppendLine($"  clip {k}: {(c != null ? c.length.ToString("0.0") + " s" : "null")}"); } catch (Exception e) { report.AppendLine($"  clip {k}: FAILS {e.GetType().Name}: {e.Message}"); }
            foreach (SoundCue k in Enum.GetValues(typeof(SoundCue)))
                try { var c = SoundBank.Cue(k); report.AppendLine($"  cue {k}: {(c != null ? c.length.ToString("0.0") + " s" : "null")}"); } catch (Exception e) { report.AppendLine($"  cue {k}: FAILS {e.GetType().Name}: {e.Message}"); }
            var ears = new GameObject("Ears").transform;
            w.Sounds.Listener = ears;
            var keep = SoundSystem.Setting; SoundSystem.Setting = SoundSetting.On;
            var land = w.Terrain.LandBounds();
            foreach (float hour in new[] { 11f, 23f })
            {
                w.Clock.Hour = hour;
                foreach (var spot in new[] { new Vector2(land.center.x, land.yMin - 0.4f), land.center, new Vector2(land.xMax + 0.3f, land.center.y) })
                {
                    ears.position = w.transform.TransformPoint(new Vector3(spot.x, 1.2f, spot.y));
                    for (int k = 0; k < 72 * 6; k++) w.Sounds.Step(Dt);
                    var heard = new StringBuilder();
                    foreach (var (kind, vol) in w.Sounds.Playing) heard.Append($" {kind} {vol:0.00}");
                    report.AppendLine($"  sound at {hour:00}:00 from ({spot.x:0.0}, {spot.y:0.0}): {w.Sounds.Playing.Count} playing:{heard} [{w.Sounds.Diagnostics}]");
                }
            }
            SoundSystem.Setting = keep;
            UnityEngine.Object.DestroyImmediate(ears.gameObject);
        }
    }
}
