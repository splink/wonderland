using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    /// <summary>
    /// Railway building on the player's own worlds (Tests/Saves/headset-*.json, pulled from the headset; not in git):
    /// for each, whether its network runs one way, and what happens to the strokes a player would draw - a loop in open
    /// land, a branch off the railway, a link between two lines - previewed and then built.
    /// Logs/trackprobe-results.txt.
    /// </summary>
    public static class WonderlandTrackProbe
    {
        private const float Dt = 1f / 72f;

        public static void Run()
        {
            var report = new StringBuilder();
            int failures = 0;
            foreach (var file in Directory.GetFiles("Tests/Saves", "headset-*.json"))
            {
                var w = new GameObject("Probe").AddComponent<WonderlandWorld>();
                w.transform.position = new Vector3(600f, 0f, 0f);
                try { w.Deserialize(File.ReadAllText(file), WonderlandJourney.Materials()); }
                catch (Exception e) { report.AppendLine($"{Path.GetFileName(file)}: could not load ({e.Message})"); UnityEngine.Object.DestroyImmediate(w.gameObject); continue; }
                var net = w.Track.Network;
                report.AppendLine($"{Path.GetFileName(file)}: {net.Edges.Count} pieces, {net.Joints.Count} switches, {w.Trains.Trains.Count} trains; one way {net.OneWay}{(net.OneWay ? "" : " - " + net.TwoWayReason)}");
                var t = w.Terrain;
                var land = t.LandBounds();
                // A loop in open land: the first spot (on a coarse grid) far enough from track, buildings and water.
                bool tried = false;
                for (float x = land.xMin + 0.4f; x < land.xMax - 0.4f && !tried; x += 0.15f)
                for (float z = land.yMin + 0.35f; z < land.yMax - 0.35f && !tried; z += 0.15f)
                {
                    if (net.DistanceTo(new Vector3(x, 0f, z)) < 0.5f) continue;
                    var loop = new List<Vector3>();
                    for (int i = 0; i <= 90; i++) { float a = i / 90f * Mathf.PI * 2f * 0.98f; loop.Add(t.PointAt(x + Mathf.Cos(a) * 0.3f, z + Mathf.Sin(a) * 0.25f)); }
                    var preview = w.PreviewTrack(loop, out _, out _);
                    if (!preview.Ok && preview.Error != TrackNetwork.TurnBack) continue;   // houses or water in the way: another spot
                    tried = true;
                    var built = w.BuildTrack(loop);
                    bool ok = preview.Ok && built.Ok;
                    if (!ok) failures++;
                    report.AppendLine($"  {(ok ? "PASS" : "FAIL")}  a loop in open land at ({x:0.00}, {z:0.00}): preview {preview.Error ?? "ok"}, built {built.Error ?? "ok"}");
                }
                if (!tried) report.AppendLine("  (no open land for a loop)");
                // A branch off the railway, outwards from the middle of a piece.
                net = w.Track.Network;
                int branches = 0, branchOk = 0;
                var reasons = new Dictionary<string, int>();
                for (int e = 0; e < net.Edges.Count && branches < 12; e++)
                {
                    var l = net.Edges[e].Layout;
                    if (l.Length < 0.8f) continue;
                    foreach (float frac in new[] { 0.3f, 0.6f })
                    {
                        var p = l.Evaluate(l.Length * frac, out var tan); tan.y = 0f; tan.Normalize();
                        var side = Vector3.Cross(Vector3.up, tan);
                        foreach (int sgn in new[] { 1, -1 })
                        {
                            var stroke = new List<Vector3>();
                            for (int i = 0; i <= 30; i++) { float u = i / 30f; var q = p + tan * sgn * u * 0.35f + side * u * u * 0.3f; stroke.Add(new Vector3(q.x, t.SurfaceAt(q.x, q.z), q.z)); }
                            var preview = w.PreviewTrack(stroke, out _, out _);
                            if (!preview.Ok) { reasons.TryGetValue("preview: " + preview.Error, out int k); reasons["preview: " + preview.Error] = k + 1; branches++; continue; }
                            var built = w.BuildTrack(stroke);
                            branches++;
                            if (built.Ok) { branchOk++; w.Undo(); }
                            else { reasons.TryGetValue("built: " + built.Error, out int k); reasons["built: " + built.Error] = k + 1; failures++; }
                        }
                    }
                }
                var sb = new StringBuilder();
                foreach (var kv in reasons) sb.Append($"; {kv.Value}x {kv.Key}");
                report.AppendLine($"  INFO  branches off the railway: {branchOk}/{branches} built (a preview that was green always built){sb}");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            File.WriteAllText("Logs/trackprobe-results.txt", report.ToString());
            Debug.Log("Track probe\n" + report);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// What the player's world asks of the GPU (Logs/budget-results.txt): renderers, triangles and shadow casters by
        /// kind of thing, the ground cover, particles and the land's area - to see what to trim.
        /// </summary>
        public static void Budget()
        {
            var report = new StringBuilder();
            string file = Environment.GetEnvironmentVariable("WL_SAVE") ?? "Tests/Saves/headset-wonderland-world.json";
            var w = new GameObject("Budget").AddComponent<WonderlandWorld>();
            w.Deserialize(File.ReadAllText(file), WonderlandJourney.Materials());
            w.People.Grid.Complete();
            for (int i = 0; i < 72 * 5; i++) { w.Clock.Tick(Dt); w.People.Step(Dt); }
            w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
            var groups = new Dictionary<string, (int renderers, long tris, int shadows, long shadowTris)>();
            foreach (var r in w.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                string kind = r.transform.parent != null ? r.transform.parent.name : r.name;
                for (var p = r.transform; p != null && p != w.transform; p = p.parent) if (p.parent == w.transform) kind = p.name;
                if (r.name == "Proxy") kind += " (far stand-ins)";
                if (r.name == "Shadow") kind += " (shadow stand-ins)";
                long tris = 0;
                if (r is MeshRenderer mr && mr.GetComponent<MeshFilter>()?.sharedMesh is { } m) for (int sm = 0; sm < m.subMeshCount; sm++) tris += m.GetIndexCount(sm) / 3;
                else if (r is ParticleSystemRenderer) { kind += " (particles)"; }
                groups.TryGetValue(kind, out var g);
                bool shadow = r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
                groups[kind] = (g.renderers + 1, g.tris + tris, g.shadows + (shadow ? 1 : 0), g.shadowTris + (shadow ? tris : 0));
            }
            long allTris = 0, allShadow = 0; int all = 0, allCasters = 0;
            var rows = new List<KeyValuePair<string, (int renderers, long tris, int shadows, long shadowTris)>>(groups);
            rows.Sort((a, b) => b.Value.tris.CompareTo(a.Value.tris));
            report.AppendLine($"{Path.GetFileName(file)}: {w.Buildings.Houses.Count} buildings, {w.Forest.Trees.Count} trees, {w.People.People.Count} people, {w.Animals.Animals.Count} animals, {w.Trains.Trains.Count} trains, land {w.Terrain.LandArea():0.0} m2");
            report.AppendLine("  kind                     renderers   triangles   shadow casters  shadow triangles");
            foreach (var kv in rows)
            {
                report.AppendLine($"  {kv.Key,-24} {kv.Value.renderers,9} {kv.Value.tris,11:N0} {kv.Value.shadows,16} {kv.Value.shadowTris,17:N0}");
                all += kv.Value.renderers; allTris += kv.Value.tris; allCasters += kv.Value.shadows; allShadow += kv.Value.shadowTris;
            }
            report.AppendLine($"  {"total",-24} {all,9} {allTris,11:N0} {allCasters,16} {allShadow,17:N0}");
            File.WriteAllText("Logs/budget-results.txt", report.ToString());
            Debug.Log("Budget\n" + report);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
