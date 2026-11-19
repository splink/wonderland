using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// Road meshes kept piece by piece come out exactly as if built from scratch - after sculpting beside a road, on a
        /// road, and adding one across others - and a rebuild with nothing changed redoes nothing (roads suite).
        /// </summary>
        private static void PieceJourney()
        {
            var w = new GameObject("Pieces").AddComponent<WonderlandWorld>();
            w.Create(2f, Materials(), 7, true, endless: true);
            var t = w.Terrain;
            for (int k = 0; k < 3; k++) w.BuildPath(Line(t, new Vector2(-0.6f, -0.4f + k * 0.4f), new Vector2(0.6f, -0.4f + k * 0.4f)), PathKind.Road);
            for (int k = 0; k < 3; k++) w.BuildPath(Line(t, new Vector2(-0.4f + k * 0.4f, -0.6f), new Vector2(-0.4f + k * 0.4f, 0.6f)), PathKind.Road);
            w.BuildPath(Line(t, new Vector2(-0.6f, 0.55f), new Vector2(0.6f, 0.5f)), PathKind.Footpath);
            var mesh = w.Paths.GetComponentInChildren<MeshFilter>().sharedMesh;
            string Fingerprint() { var v = mesh.vertices; double sum = 0; foreach (var p in v) sum += p.x * 3.1 + p.y * 1000.0 + p.z * 7.7; return $"{v.Length} vertices, {sum:0.000}"; }
            w.Paths.Rebuild();
            bool idle = w.Paths.PiecesBuilt == 0;
            var steps = new (string what, System.Action act)[]
            {
                ("sculpting beside a road", () => { for (int k = 0; k < 15; k++) w.TerrainStroke(TerrainBrush.Raise, new Vector3(0.2f, 0f, 0.1f), 0.1f, 1f, 0.05f, 0f); w.RefreshBridges(); }),
                ("sculpting over a road", () => { for (int k = 0; k < 15; k++) w.TerrainStroke(TerrainBrush.Lower, new Vector3(0f, 0f, -0.4f), 0.12f, 1f, 0.05f, 0f); w.RefreshBridges(); }),
                ("a road across the others", () => w.BuildPath(Line(t, new Vector2(-0.6f, -0.2f), new Vector2(0.6f, 0.25f)), PathKind.Road)),
            };
            foreach (var (what, act) in steps)
            {
                act();
                int redone = w.Paths.PiecesBuilt;
                string cached = Fingerprint();
                w.Paths.ForgetAllPieces(); w.Paths.Rebuild();
                string fresh = Fingerprint();
                Check(cached == fresh && redone < w.Paths.PiecesBuilt, $"Roads kept piece by piece after {what} are exactly as built from scratch ({cached} / {fresh}; {redone} of {w.Paths.PiecesBuilt} pieces redone)");
            }
            Check(idle, "Re-laying the roads with nothing changed redoes no piece");
            Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Where the time goes drawing a road and sculpting in a busy town (Logs/editperf-results.txt).</summary>
        public static void EditPerf()
        {
            var report = new System.Text.StringBuilder();
            bool gridWas = BuildGrid.Snap; BuildGrid.Snap = true;
            var w = new GameObject("Edit perf").AddComponent<WonderlandWorld>();
            w.Create(2f, Materials(), 7, true, endless: true);
            var t = w.Terrain;
            for (int k = 0; k < 6; k++) w.BuildPath(Line(t, new Vector2(-1.2f, -0.9f + k * 0.35f), new Vector2(1.2f, -0.9f + k * 0.35f)), PathKind.Road);
            for (int k = 0; k < 6; k++) w.BuildPath(Line(t, new Vector2(-1.1f + k * 0.45f, -1f), new Vector2(-1.1f + k * 0.45f, 0.95f)), PathKind.Road);
            for (int k = 0; k < 30; k++) w.PlaceHouse(new HouseInstance { x = -1f + (k % 10) * 0.22f, z = -0.75f + (k / 10) * 0.7f, variant = k % 4 == 0 ? Catalog.Bakery : Catalog.Cottage });
            for (int k = 0; k < 8; k++) w.PaintForest(new Vector3(-0.8f + k * 0.2f, 0f, 1.1f), 0.15f, 1f, 0.3f);
            w.BuildStream(Line(t, new Vector2(-1.1f, 1.5f), new Vector2(1.1f, 1.45f)), 1f);
            w.BuildStream(Line(t, new Vector2(0.3f, 1.9f), new Vector2(0.35f, 1.5f)), 0.6f);
            w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
            var clock = new System.Diagnostics.Stopwatch();
            double Time(System.Action a, int n = 1) { clock.Restart(); for (int i = 0; i < n; i++) a(); return clock.Elapsed.TotalMilliseconds / n; }
            report.AppendLine($"Town: {w.Paths.Lines.Count} roads, {w.Paths.Junctions.Count} junctions, {w.Buildings.Houses.Count} houses, {w.Forest.Trees.Count} trees");
            report.AppendLine($"Plan a road: {Time(() => w.Paths.Plan(Line(t, new Vector2(-0.9f, 0.1f), new Vector2(0.9f, 0.12f)), (int)PathKind.Road, out _)):0.0} ms");
            report.AppendLine($"Paths.Rebuild (all road meshes): {Time(() => w.Paths.Rebuild(), 3):0.0} ms");
            report.AppendLine($"Bridges + paths (RefreshBridges): {Time(() => w.RefreshBridges(), 3):0.0} ms");
            report.AppendLine($"Build a road (whole): {Time(() => w.BuildPath(Line(t, new Vector2(-0.95f, 0.15f), new Vector2(0.95f, 0.17f)), PathKind.Road)):0.0} ms");
            {
                var line = w.Paths.Plan(Line(t, new Vector2(-0.95f, 0.62f), new Vector2(0.95f, 0.6f)), (int)PathKind.Road, out _);
                report.AppendLine($"  checkpoint {Time(() => w.Checkpoint()):0.0} ms, add {Time(() => w.Paths.Add(line)):0.0} ms, grade {Time(() => w.TerrainView.Rebuild(w.Paths.Grade(line))):0.0} ms, bridges+paths {Time(() => w.RefreshBridges()):0.0} ms ({w.Paths.PiecesBuilt} pieces)");
                report.AppendLine($"  trees cleared {Time(() => w.Forest.RemoveWhere(tr => PathSystem.Distance(line, new Vector2(tr.x, tr.z)) < 0.04f)):0.0} ms, resit {Time(() => w.Buildings.Resit()):0.0} ms, rehome {Time(() => w.People.Rehome()):0.0} ms");
            }
            w.Traffic.Refresh(true);   // (the first time spawns the vehicles and builds their meshes)
            report.AppendLine($"Traffic refresh after a road change: {Time(() => w.Traffic.Refresh(true), 3):0.0} ms");
            // Sculpting: away from roads, and next to one (per brush step, as a frame does).
            var far = new Vector3(0.9f, 0f, 1.2f); var near = new Vector3(0f, 0f, -0.55f);
            report.AppendLine($"Raise, away from roads: {Time(() => w.TerrainStroke(TerrainBrush.Raise, far, 0.15f, 1f, 0.014f, 0f), 20):0.00} ms a step");
            report.AppendLine($"  of which ApplyBrush: {Time(() => t.ApplyBrush(TerrainBrush.Raise, far, 0.15f, 1f, 0.014f, 0f), 20):0.00} ms, terrain mesh: {Time(() => w.TerrainView.Rebuild(t.RegionAround(new Vector2(far.x, far.z), 0.15f)), 20):0.00} ms");
            report.AppendLine($"Raise, beside a road: {Time(() => w.TerrainStroke(TerrainBrush.Raise, near, 0.15f, 1f, 0.014f, 0f), 20):0.00} ms a step");
            report.AppendLine($"Roads re-laid after sculpting beside one (RefreshBridges): {Time(() => w.RefreshBridges()):0.0} ms, {w.Paths.PiecesBuilt} pieces redone");
            report.AppendLine($"Roads re-laid with nothing changed: {Time(() => w.Paths.Rebuild()):0.0} ms, {w.Paths.PiecesBuilt} pieces redone");
            w.Paths.ForgetAllPieces();
            report.AppendLine($"Roads re-laid from scratch: {Time(() => w.Paths.Rebuild()):0.0} ms, {w.Paths.PiecesBuilt} pieces");
            report.AppendLine($"Update loop after sculpting by a road (roads re-laid): {Time(() => { w.SendMessage("Update"); }, 1):0.0} ms");
            var bank = new Vector3(-0.4f, 0f, 1.38f);
            report.AppendLine($"Raise beside a stream: {Time(() => w.TerrainStroke(TerrainBrush.Raise, bank, 0.12f, 1f, 0.014f, 0f), 20):0.00} ms a step; streams re-levelled and re-carved: {Time(() => w.UpdateStreamsFromTerrain()):0.0} ms");
            {
                var st = w.Streams.Streams[0]; var r = new RectInt();
                report.AppendLine($"  one stream: relevel {Time(() => StreamSystem.Relevel(st, t, w.Streams.Streams)):0.0} ms, carve {Time(() => r = StreamSystem.Carve(t, st, w.Streams.Streams)):0.0} ms, terrain mesh {Time(() => w.TerrainView.Rebuild(r)):0.0} ms ({r.width}x{r.height} cells), stream meshes {Time(() => w.Streams.Rebuild()):0.0} ms");
            }
            report.AppendLine($"Forest + cover rebuild after strokes: {Time(() => { w.Forest.RebuildDirty(); w.Cover.RebuildDirty(); }):0.0} ms");
            report.AppendLine($"People grid after a change: {Time(() => w.People.Grid.Complete()):0.0} ms");
            System.IO.File.WriteAllText("Logs/editperf-results.txt", report.ToString());
            Debug.Log(report.ToString());
            BuildGrid.Snap = gridWas;
            Object.DestroyImmediate(w.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
