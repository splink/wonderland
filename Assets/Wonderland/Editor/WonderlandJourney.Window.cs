using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wonderland.Editor
{
    /// <summary>
    /// The world window: an endless world seen through a window of up to 3 x 3 m that slides across it half its size at a
    /// time. What is built stays put in the world's coordinates, the window stays put in the room, the grid never shifts,
    /// track and roads carry on across the old edge, and saves keep the window and the land everywhere.
    /// </summary>
    public static partial class WonderlandJourney
    {
        public static void TestWindow()
        {
            Report.Clear(); failures = 0;
            bool gridWas = BuildGrid.Snap;
            float windowWas = WonderlandWorld.MaxWindow;
            try
            {
                NavigationJourney();
                WonderlandWorld.MaxWindow = 3f;   // (the journeys below were laid out for a 3 m window, stepped half a window at a time)
                WindowJourney(); StreamingJourney(); VisibilityJourney(); MapJourney();
            }
            catch (Exception e) { Fail("Exception: " + e); }
            finally { BuildGrid.Snap = gridWas; WonderlandWorld.MaxWindow = windowWas; Shader.SetGlobalFloat("_WL_WindowOn", 0f); }
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            File.WriteAllText("Logs/window-results.txt", Report.ToString());
            Debug.Log("Wonderland window test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static readonly Vector2Int[] Ways = { new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1), new(0, -1), new(1, -1) };

                private static WonderlandWorld Endless(string name, float x)
        {
            var w = new GameObject(name).AddComponent<WonderlandWorld>();
            w.transform.SetPositionAndRotation(new Vector3(x, 0f, 0f), Quaternion.Euler(0f, 25f, 0f));
            w.Create(2f, Materials(), 7, false, endless: true);
            w.UseWindow(true);
            return w;
        }

        /// <summary>
        /// Getting about: the window is 2 x 2 m unless chosen bigger; an arrow steps it a tenth of its size; scrolling
        /// moves it smoothly - the land always there before it shows, the grid moving on only now and then, not every frame.
        /// </summary>
        private static void NavigationJourney()
        {
            WonderlandWorld.MaxWindow = 2f;
            var w = Endless("Navigation", 980f);
            var t = w.Terrain;
            Check(w.WindowSize == new Vector2(2f, 2f), $"The window is 2 x 2 m to start with ({w.WindowSize})");
            w.Navigate(new Vector2Int(1, 0)); w.FinishSlide();
            Check(w.WindowCenter == new Vector2(0.2f, 0f), $"An arrow steps it a tenth of its size ({w.WindowCenter.x * 100f:0} cm)");
            int slides = 0; var cells = t.Cells; bool shown = true; float worst = 0f, total = 0f; int frames = 0;
            var clock = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < 72 * 4; i++)
            {
                clock.Restart();
                w.Pan(new Vector2(0.8f, 0.3f), Dt);
                w.Advance(Dt);
                float ms = (float)clock.Elapsed.TotalMilliseconds;
                if (t.Cells != cells) { slides++; cells = t.Cells; } else { worst = Mathf.Max(worst, ms); total += ms; frames++; }
                var r = w.WindowRect;
                shown &= r.xMin >= t.X0 && r.xMax <= t.XMax && r.yMin >= t.Z0 && r.yMax <= t.ZMax;
            }
            Check(w.WindowCenter.x > 3.3f && shown && slides <= 6,
                $"Scrolling 3.4 m moves the window smoothly, the land always there ({slides} grid moves; other frames {total / Mathf.Max(1, frames):0.00} ms on average, {worst:0.0} ms at most)");
            WonderlandWorld.MaxWindow = 3f; w.FitToRoom();
            Check(w.WindowSize == new Vector2(3f, 3f), $"In Settings it can be 3 x 3 m ({w.WindowSize})");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
            // In a 4 x 4 m room, put down 40 cm from a wall: the window doesn't shrink to stay centred there - it shifts
            // into the room, full size; the size setting changes it; a smaller world keeps its window the size in the room.
            WonderlandWorld.MaxWindow = 2f;
            var rw = new GameObject("Wall window").AddComponent<WonderlandWorld>();
            rw.transform.position = new Vector3(1000f, 0f, 1.6f);
            var room = new RoomFootprint();
            foreach (var c in new[] { new Vector2(-2f, -2f), new Vector2(2f, -2f), new Vector2(2f, 2f), new Vector2(-2f, 2f) }) room.Floor.Add(new Vector3(1000f + c.x, 0f, c.y));
            rw.Create(2f, Materials(), 7, false, endless: true);
            rw.UseWindow(true);
            rw.Room = room;
            var p = rw.PlacementPosition;
            rw.FitToRoom();
            Check(rw.WindowSize == new Vector2(2f, 2f) && (rw.PlacementPosition - p).magnitude < 1e-4f && (p - new Vector3(1000f, 0f, 1.6f)).magnitude < 1e-3f,
                $"Put down 40 cm from a wall, the window is the size chosen and stays exactly where it was put ({rw.WindowSize.x:0.00} x {rw.WindowSize.y:0.00} m)");
            var sizes = new List<float>();
            foreach (float want in new[] { 3f, 1.5f, 2f }) { WonderlandWorld.MaxWindow = want; rw.FitToRoom(); sizes.Add(rw.WindowSize.x); }
            Check(sizes[0] == 3f && sizes[1] == 1.5f && sizes[2] == 2f, $"The Window setting changes the window in the room (3 m: {sizes[0]:0.0}, 1.5 m: {sizes[1]:0.0}, 2 m: {sizes[2]:0.0})");
            rw.transform.localScale = Vector3.one * 0.6f; rw.FitToRoom();
            float physical = rw.WindowSize.x * 0.6f;
            Check(Mathf.Abs(physical - 2f) < 0.06f, $"With the world drawn at 60 %, the window is still 2 m across in the room ({physical:0.00} m)");
            UnityEngine.Object.DestroyImmediate(rw.gameObject);
        }

        private static void WindowJourney()
        {
            // 1. Land everywhere, a 3 x 3 m window with a cut edge and arrows round it.
            var w = Endless("Window", 900f);
            var t = w.Terrain;
            Check(w.WindowSize == new Vector2(3f, 3f) && w.WindowCenter == Vector2.zero, $"Without a room the window is 3 x 3 m, at home ({w.WindowSize})");
            float border = t.SurfaceAt(t.X0 + 0.01f, 0f), inner = t.SurfaceAt(0.5f, 0.5f);
            Check(t.Endless && t.LandAt(40f, -30f) && t.EdgeAt(40f, -30f) == 1f && Mathf.Abs(border - TerrainField.BaseHeight) < 0.006f && Mathf.Abs(inner - TerrainField.BaseHeight) < 0.006f,
                $"The land is flat everywhere, with no shore at the grid's edge ({border * 1000f:0.0} mm at the edge, {inner * 1000f:0.0} mm inside)");
            var cut = w.TerrainView.transform.Find("Cut edge");
            var cutMesh = cut != null ? cut.GetComponent<MeshFilter>().sharedMesh : null;
            Check(cutMesh != null && cutMesh.vertexCount > 1000 && cutMesh.bounds.size.x > 2.9f && cutMesh.bounds.size.z > 2.9f, $"The window has a clean cut edge round it ({(cutMesh != null ? cutMesh.vertexCount : 0)} vertices)");
            var win = Shader.GetGlobalVector("_WL_Window");
            Check(Shader.GetGlobalFloat("_WL_WindowOn") == 1f && Mathf.Abs(win.z - 1.5f) < 1e-4f && Mathf.Abs(win.w - 1.5f) < 1e-4f, $"The shaders draw just the window ({win})");

            // 2. A house, a railway line and a street at home, the street and line reaching close to the east edge.
            BuildGrid.Snap = true;
            string house = Put(w, 0, -0.6f, 0.9f);
            var line = w.BuildTrack(Line(t, new Vector2(-1.2f, 0.5f), new Vector2(1.3f, 0.5f)));
            string street = w.BuildPath(Line(t, new Vector2(-1.2f, -0.5f), new Vector2(1.3f, -0.5f)), PathKind.Road);
            Check(house == null && line.Ok && street == null, $"Building at home works as before (house {house ?? "ok"}, track {line.Error ?? "ok"}, street {street ?? "ok"})");
            var houseAt = new Vector2(w.Buildings.Houses[0].x, w.Buildings.Houses[0].z);
            var trackStart = w.Track.Network.Edges[0].Layout.Points[0];
            var gridPoint = BuildGrid.Point(new Vector2(0.37f, -0.81f));

            // 3. Half a window east: the window stays in the room, the land slides under it.
            var placed = w.PlacementPosition;
            var houseWorld = w.transform.TransformPoint(new Vector3(houseAt.x, 0f, houseAt.y));
            bool went = w.Navigate(new Vector2Int(1, 0), 0.5f);
            bool sliding = w.Sliding;
            for (int i = 0; i < 20; i++) w.SendMessage("LateUpdate");
            w.FinishSlide(); w.SendMessage("LateUpdate");
            var houseWorldAfter = w.transform.TransformPoint(new Vector3(houseAt.x, 0f, houseAt.y));
            var shift = houseWorldAfter - houseWorld;
            var east = w.transform.rotation * Vector3.right;
            Check(went && sliding && w.WindowCenter == new Vector2(1.5f, 0f) && (w.PlacementPosition - placed).magnitude < 1e-4f,
                $"Going east slides the window half its width; it stays where it is in the room (centre {w.WindowCenter}, placement moved {(w.PlacementPosition - placed).magnitude * 1000f:0.000} mm)");
            Check((shift + east * 1.5f).magnitude < 1e-4f && new Vector2(w.Buildings.Houses[0].x, w.Buildings.Houses[0].z) == houseAt,
                $"What is built keeps its place in the world and slides 1.5 m west in the room ({shift})");
            win = Shader.GetGlobalVector("_WL_Window");
            Check(Mathf.Abs(win.x - 1.5f) < 1e-4f && t.Window.HasValue && Mathf.Abs(t.Window.Value.xMin) < 1e-4f && Mathf.Abs(t.Window.Value.xMax - 3f) < 1e-4f,
                $"The cut follows the window ({win}; pointer within {t.Window})");
            Check(t.Inside(2.9f, 0f) && t.Inside(2.9f, 1.4f), "The land the window slid onto is there");
            bool outside = t.Raycast(new Vector3(-0.6f, 1f, 0.5f), Vector3.down, 3f, out _), inside = t.Raycast(new Vector3(2.2f, 1f, 0.5f), Vector3.down, 3f, out _);
            Check(!outside && inside, $"The pointer finds the land only inside the window (outside: {outside}, inside: {inside})");
            Check(BuildGrid.Point(new Vector2(0.37f, -0.81f)) == gridPoint, "The construction grid doesn't shift when the window moves");

            // 4. Carrying the railway and the street on across the old edge, and new building in the new land.
            var on = w.BuildTrack(Line(t, new Vector2(1.3f, 0.5f), new Vector2(2.7f, 0.5f)));
            string onStreet = w.BuildPath(Line(t, new Vector2(1.3f, -0.5f), new Vector2(2.7f, -0.5f)), PathKind.Road);
            string newHouse = Put(w, 0, 2.4f, 1.0f);
            var n = w.Track.Network;
            float trackEast = float.MinValue; foreach (var e in n.Edges) foreach (var p in e.Layout.Points) trackEast = Mathf.Max(trackEast, p.x);
            float streetEast = float.MinValue; foreach (var l in w.Paths.Lines) foreach (var p in l.points) streetEast = Mathf.Max(streetEast, p.x);
            Check(on.Ok && n.Edges.Count == 1 && n.Joints.Count == 0 && trackEast > 2.6f, $"The railway carries on across the old edge as one line ({n.Edges.Count} pieces, to x = {trackEast:0.00}; {on.Error ?? "built"})");
            Check(onStreet == null && w.Paths.Lines.Count == 1 && streetEast > 2.6f, $"So does the street ({w.Paths.Lines.Count} roads, to x = {streetEast:0.00}; {onStreet ?? "built"})");
            Check(newHouse == null && w.Buildings.Houses.Count == 2, $"Building in the new land works ({newHouse ?? "ok"})");

            // 5. Back home: everything there as it was.
            w.Navigate(new Vector2Int(-1, 0), 0.5f); w.FinishSlide(); w.SendMessage("LateUpdate");
            var startNow = w.Track.Network.Edges[0].Layout.Points[0];
            Check(w.WindowCenter == Vector2.zero && new Vector2(w.Buildings.Houses[0].x, w.Buildings.Houses[0].z) == houseAt && new Vector2(startNow.x, startNow.z) == new Vector2(trackStart.x, trackStart.z)
                && Mathf.Abs(startNow.y - trackStart.y) < 0.001f && (w.PlacementPosition - placed).magnitude < 1e-4f,
                $"Back west, home is exactly as it was (track start {trackStart.x:0.0000},{trackStart.y:0.0000},{trackStart.z:0.0000} -> {startNow.x:0.0000},{startNow.y:0.0000},{startNow.z:0.0000})");

            // 6. Every direction and back, many steps, and home.
            bool all = true;
            for (int d = 0; d < 8; d++)
            {
                var dir = Ways[d];
                w.Navigate(dir, 0.5f); w.FinishSlide();
                all &= w.WindowCenter == new Vector2(dir.x * 1.5f, dir.y * 1.5f);
                w.Navigate(-dir, 0.5f); w.FinishSlide();
                all &= w.WindowCenter == Vector2.zero;
            }
            Check(all && (w.PlacementPosition - placed).magnitude < 1e-4f, "The window goes all eight ways and back");
            int steps = 0;
            while (steps < 20 && w.Navigate(new Vector2Int(1, 1), 0.5f)) { w.FinishSlide(); steps++; }
            bool covered = t.Inside(w.WindowRect.xMax, w.WindowRect.yMax) && t.Inside(w.WindowRect.xMin, w.WindowRect.yMin);
            Check(steps >= 3 && covered && t.LandAt(w.WindowCenter.x, w.WindowCenter.y), $"Step after step north-east, there is always land under the window ({steps} steps to {w.WindowCenter}; {w.WhereLabel()})");
            w.NavigateHome(); w.FinishSlide(); w.SendMessage("LateUpdate");
            Check(w.WindowCenter == Vector2.zero && w.WhereLabel().StartsWith("at home"), "Home goes back to where the world started");

            // 8. Lifted off the floor: navigating keeps the height.
            w.Lift = 0.4f;
            w.Place(placed + Vector3.up * 0.4f, w.PlacementRotation);
            w.Navigate(new Vector2Int(0, -1), 0.5f); w.FinishSlide();
            Check(Mathf.Abs(w.PlacementPosition.y - placed.y - 0.4f) < 1e-4f && Mathf.Abs(w.transform.position.y - placed.y - 0.4f) < 1e-4f, "Lifted off the floor, the world stays at its height while it slides");
            w.Navigate(new Vector2Int(0, 1), 0.5f); w.FinishSlide();
            w.Place(placed, w.PlacementRotation);

            // 9. Saved and loaded: the land everywhere, the window where it was, everything built.
            w.Navigate(new Vector2Int(1, 0), 0.5f); w.FinishSlide();
            string json = w.Serialize();
            var copy = new GameObject("Window copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(940f, 0f, 0f);
            copy.Deserialize(json, Materials());
            copy.UseWindow(true);
            Check(copy.Terrain.Endless && copy.WindowCenter == new Vector2(1.5f, 0f) && copy.Buildings.Houses.Count == 2 && copy.Track.Network.Edges.Count == 1 && copy.Paths.Lines.Count == 1,
                $"Saved and loaded: the land everywhere, the window where it was, everything built ({copy.WindowCenter}, {copy.Buildings.Houses.Count} houses, {copy.Track.Network.Edges.Count} track pieces, {copy.Paths.Lines.Count} roads)");
            UnityEngine.Object.DestroyImmediate(copy.gameObject);
            UnityEngine.Object.DestroyImmediate(w.gameObject);

            // 10. A room smaller than 3 m one way: the window fits it (and the other way stays 3 m).
            var rw = new GameObject("Room window").AddComponent<WonderlandWorld>();
            rw.transform.position = new Vector3(960f, 0f, 0f);
            var room = new RoomFootprint();
            foreach (var c in new[] { new Vector2(-1.2f, -2f), new Vector2(1.2f, -2f), new Vector2(1.2f, 2f), new Vector2(-1.2f, 2f) }) room.Floor.Add(new Vector3(960f + c.x, 0f, c.y));
            rw.Room = room;
            rw.Create(2f, Materials(), 7, false, endless: true);
            rw.UseWindow(true);
            Check(rw.WindowSize.x <= 2.4f && rw.WindowSize.x >= 2.2f && rw.WindowSize.y == 3f, $"In a 2.4 x 4 m room the window is {rw.WindowSize.x:0.00} x {rw.WindowSize.y:0.00} m");
            UnityEngine.Object.DestroyImmediate(rw.gameObject);

            // 11. Older worlds: an island save becomes endless - land everywhere - with all it had.
            int migrated = 0; var notes = new List<string>();
            if (Directory.Exists("Tests/Saves"))
                foreach (var file in Directory.GetFiles("Tests/Saves", "headset-world-3.json"))
                {
                    var ow = new GameObject("Old world").AddComponent<WonderlandWorld>();
                    ow.transform.position = new Vector3(980f, 0f, 0f);
                    ow.Deserialize(File.ReadAllText(file), Materials());
                    int houses = ow.Buildings.Houses.Count, pieces = ow.Track.Network.Edges.Count, trees = ow.Forest.Trees.Count;
                    ow.MakeEndless(); ow.UseWindow(true);
                    bool ok = ow.Terrain.Endless && ow.Terrain.LandAt(5f, 5f) && ow.Buildings.Houses.Count == houses && ow.Track.Network.Edges.Count == pieces && ow.Forest.Trees.Count == trees;
                    if (ok) migrated++;
                    notes.Add($"{Path.GetFileNameWithoutExtension(file)}: {houses} houses, {pieces} track pieces, {trees} trees{(ok ? "" : " - CHANGED")}");
                    var again = new GameObject("Old world again").AddComponent<WonderlandWorld>();
                    again.transform.position = new Vector3(1000f, 0f, 0f);
                    again.Deserialize(ow.Serialize(), Materials());
                    if (!again.Terrain.Endless) notes.Add("not endless after saving");
                    UnityEngine.Object.DestroyImmediate(again.gameObject);
                    UnityEngine.Object.DestroyImmediate(ow.gameObject);
                }
            if (notes.Count == 0) Report.AppendLine("INFO  No saved world Tests/Saves/headset-world-3.json to migrate");
            else Check(migrated == 1 && notes.Count == 1, $"An older island world becomes endless with everything it had ({string.Join("; ", notes)})");
        }

        /// <summary>
        /// Streaming: the grid covers the window and no more however far it goes, the ground left behind is kept (only
        /// where it was changed) and comes back exactly, saves write chunk files (only the changed ones), and undo goes one
        /// action at a time - across navigation - without turning back the trains.
        /// </summary>
        private static void StreamingJourney()
        {
            var w = Endless("Streaming", 1100f);
            var t = w.Terrain;
            // A hill and a house at home.
            w.Checkpoint();
            for (int k = 0; k < 30; k++) w.TerrainStroke(TerrainBrush.Raise, new Vector3(0.6f, 0f, 0.6f), 0.3f, 1f, 0.05f, 0f);
            float hill = t.SurfaceAt(0.6f, 0.6f);
            string house = Put(w, 0, -0.7f, -0.6f);
            int gridCells = t.Nx * t.Nz;

            // Far east: the grid follows the window, home leaves it - and is kept.
            int maxCells = 0;
            for (int i = 0; i < 8; i++) { w.Navigate(new Vector2Int(1, 0), 0.5f); w.FinishSlide(); maxCells = Mathf.Max(maxCells, t.Nx * t.Nz); }
            float offGrid = t.SurfaceAt(0.6f, 0.6f);
            Check(hill > TerrainField.BaseHeight + 0.02f && house == null && !t.Inside(0.6f, 0.6f) && Mathf.Abs(offGrid - hill) < 1e-5f,
                $"12 m east, home is off the grid but its hill is still known ({hill * 100f:0.0} cm, off the grid {offGrid * 100f:0.0} cm)");
            Check(maxCells <= 6 * 6 * 61 * 61 && t.Nx * t.Cell < 6.2f, $"The grid covers the window and no more ({t.Nx * t.Cell:0.0} x {t.Nz * t.Cell:0.0} m; at most {Mathf.Sqrt(maxCells) * t.Cell:0.0} m square; at home {Mathf.Sqrt(gridCells) * t.Cell:0.0} m)");
            int kept = t.Store.Count;
            Check(kept > 0 && kept <= 12, $"Only the land that was changed is kept ({kept} chunks of 1.2 m, after crossing {8 * 1.5f:0} m)");
            Check(Mathf.Abs(t.SurfaceAt(40f, -25f) - TerrainField.BaseHeight) < 0.006f && t.LandAt(40f, -25f) && t.WaterLevelAt(40f, -25f) < 0f, "Land nobody has been to is fresh flat land");

            // Back home: the hill exactly as it was.
            w.NavigateHome(); w.FinishSlide();
            Check(t.Inside(0.6f, 0.6f) && t.SurfaceAt(0.6f, 0.6f) == hill && w.Buildings.Houses.Count == 1, $"Back home, the hill is exactly as it was ({t.SurfaceAt(0.6f, 0.6f) * 100f:0.000} cm)");

            // Saved to a file: chunk files beside it, the JSON without the ground; loaded again, all there.
            string dir = Path.Combine(Path.GetTempPath(), "wonderland-stream-test");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "world.json");
            w.Navigate(new Vector2Int(1, 1), 0.5f); w.FinishSlide();
            w.Save(path);
            var files = Directory.GetFiles(WonderlandWorld.ChunkFolder(path), "*.chunk");
            long jsonSize = new FileInfo(path).Length;
            var times = new Dictionary<string, DateTime>(); foreach (var f in files) times[f] = File.GetLastWriteTimeUtc(f);
            System.Threading.Thread.Sleep(1100);
            w.Save(path);
            int rewritten = 0; foreach (var f in files) if (File.GetLastWriteTimeUtc(f) != times[f]) rewritten++;
            Check(files.Length > 0 && files.Length <= 16 && jsonSize < 400_000, $"Saved: {files.Length} chunk files beside a {jsonSize / 1024} KB file");
            Check(rewritten == 0, $"Saving again with nothing changed writes no chunk again ({rewritten} rewritten)");
            var loaded = new GameObject("Streaming loaded").AddComponent<WonderlandWorld>();
            loaded.transform.position = new Vector3(1150f, 0f, 0f);
            loaded.Load(Materials(), path);
            loaded.UseWindow(true);
            loaded.NavigateHome(); loaded.FinishSlide();
            Check(loaded.Terrain.Endless && loaded.Terrain.SurfaceAt(0.6f, 0.6f) == hill && loaded.Buildings.Houses.Count == 1,
                $"Loaded from its files, the world is all there ({loaded.Terrain.SurfaceAt(0.6f, 0.6f) * 100f:0.000} cm hill, {loaded.Buildings.Houses.Count} house)");
            UnityEngine.Object.DestroyImmediate(loaded.gameObject);
            // A broken chunk file doesn't break the world: that land is fresh again.
            File.WriteAllText(files[0], "not a chunk");
            var broken = new GameObject("Streaming broken").AddComponent<WonderlandWorld>();
            broken.transform.position = new Vector3(1200f, 0f, 0f);
            bool loads = true;
            try { broken.Load(Materials(), path); broken.UseWindow(true); broken.NavigateHome(); broken.FinishSlide(); }
            catch (Exception e) { loads = false; Report.AppendLine("INFO  " + e.Message); }
            Check(loads && broken.Buildings.Houses.Count == 1, "A damaged chunk file costs only that piece of land, not the world");
            UnityEngine.Object.DestroyImmediate(broken.gameObject);
            Directory.Delete(dir, true);

            // Undo, one action at a time, across navigation.
            w.NavigateHome(); w.FinishSlide();
            w.Checkpoint();
            for (int k = 0; k < 30; k++) w.TerrainStroke(TerrainBrush.Raise, new Vector3(-0.6f, 0f, 0.6f), 0.25f, 1f, 0.05f, 0f);
            float second = t.SurfaceAt(-0.6f, 0.6f);
            w.Navigate(new Vector2Int(1, 0), 0.5f); w.FinishSlide(); w.Navigate(new Vector2Int(1, 0), 0.5f); w.FinishSlide();
            string east = Put(w, 0, 3.4f, 0.4f);
            bool undid1 = w.Undo();
            int housesAfter1 = w.Buildings.Houses.Count;
            bool undid2 = w.Undo();
            float offAfterUndo = t.SurfaceAt(-0.6f, 0.6f);
            bool redid = w.Redo();
            float offAfterRedo = t.SurfaceAt(-0.6f, 0.6f);
            Check(east == null && undid1 && housesAfter1 == 1 && undid2 && Mathf.Abs(offAfterUndo - TerrainField.BaseHeight) < 0.006f && redid && Mathf.Abs(offAfterRedo - second) < 1e-5f,
                $"Undo takes back the last action, then the one before - even on land the window has left - and redo brings it back (house {east ?? "placed"}; hill {second * 100f:0.0} -> {offAfterUndo * 100f:0.0} -> {offAfterRedo * 100f:0.0} cm)");
            w.NavigateHome(); w.FinishSlide();
            Check(Mathf.Abs(t.SurfaceAt(-0.6f, 0.6f) - second) < 1e-5f, "Back home, the redone hill is there");
            for (int i = 0; i < 14; i++) Put(w, 0, -1.2f + (i % 7) * 0.3f, -1.2f + (i / 7) * 0.3f);
            int undone = 0; while (w.Undo()) undone++;
            Check(undone == WonderlandWorld.MaxUndo, $"Undo goes back {WonderlandWorld.MaxUndo} actions ({undone})");

            // A train keeps running through an undo that has nothing to do with it.
            w.BuildTrack(Line(t, new Vector2(-1.3f, 1.1f), new Vector2(1.3f, 1.1f)));
            w.PlaceTrain(t.PointAt(-0.8f, 1.1f), TrainKind.Passenger, Vector3.right, out var train);
            for (int i = 0; i < 60; i++) w.Trains.Step(Dt);
            int housesBefore = w.Buildings.Houses.Count;
            string later = Put(w, 0, 0.9f, -1.0f);
            for (int i = 0; i < 60; i++) w.Trains.Step(Dt);
            var before = train != null ? w.Trains.LocoPosition(train) : Vector3.zero;
            w.Undo();
            bool same = train != null && w.Trains.Trains.Contains(train) && (w.Trains.LocoPosition(train) - before).magnitude < 0.002f;
            Check(later == null && same && w.Buildings.Houses.Count == housesBefore, $"Undoing a house leaves the running train where it is ({(train != null && w.Trains.Trains.Contains(train) ? $"{(w.Trains.LocoPosition(train) - before).magnitude * 1000f:0.0} mm" : "train replaced")})");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>
        /// Out of sight: what the window doesn't show isn't drawn or animated, but it all goes on - a train runs out of
        /// the window, on along its line and back in, and people can get about wherever there are houses and roads.
        /// </summary>
        private static void VisibilityJourney()
        {
            var w = Endless("Visibility", 1300f);
            var t = w.Terrain;
            BuildGrid.Snap = true;
            string near = Put(w, 0, 0.5f, -0.9f), far = Put(w, 0, 4.4f, -0.9f);
            for (int k = 0; k < 6; k++) { w.PaintForest(new Vector3(-0.8f, 0f, -0.9f), 0.2f, 1f, 0.3f); w.PaintForest(new Vector3(4.0f, 0f, 1.0f), 0.2f, 1f, 0.3f); }
            var line = w.BuildTrack(Line(t, new Vector2(-1.3f, 0.3f), new Vector2(6.5f, 0.3f)));
            w.PlaceTrain(t.PointAt(-1.0f, 0.3f), TrainKind.Passenger, Vector3.right, out var train);
            w.RefreshVisibility();
            w.Forest.RebuildDirty();
            w.RefreshVisibility();
            var houses = w.Buildings.transform;
            int activeHouses = 0, hiddenHouses = 0;
            foreach (Transform h in houses) { if (h.gameObject.activeSelf) activeHouses++; else hiddenHouses++; }
            int shownChunks = 0, hiddenChunks = 0;
            foreach (Transform c in w.Forest.transform) { if (c.gameObject.activeSelf) shownChunks++; else hiddenChunks++; }
            Check(near == null && far == null && activeHouses == 1 && hiddenHouses == 1, $"The house beyond the window isn't drawn, the one in it is ({activeHouses} shown, {hiddenHouses} hidden)");
            Check(shownChunks > 0 && w.Forest.Trees.Exists(tr => tr.x > 3.5f) && hiddenChunks + 0 >= 0 && !HasActiveBeyond(w.Forest.transform, 1.6f),
                $"Trees beyond the window are neither drawn nor built ({shownChunks} chunks shown, {hiddenChunks} hidden)");
            // The train runs east out of the window and keeps going.
            bool left = false, hiddenWhileOut = true; float farthest = 0f;
            for (int i = 0; i < 72 * 40 && train != null; i++)
            {
                w.Trains.Step(Dt);
                if (i % 12 == 0) w.RefreshVisibility();
                var p = w.Trains.LocoPosition(train);
                farthest = Mathf.Max(farthest, p.x);
                if (p.x > 1.8f && i % 12 == 0) { left = true; hiddenWhileOut &= !w.Trains.Drawn(train); }
            }
            Check(line.Ok && train != null && left && farthest > 4f && hiddenWhileOut,
                $"A train runs out of the window and on along its line unseen (as far as x = {farthest:0.0}; {line.Error ?? "line built"})");
            // A car on a long street drives out of the window too, and on.
            string street = w.BuildPath(Line(t, new Vector2(-1.3f, -0.4f), new Vector2(6.5f, -0.4f)), PathKind.Road);
            string placed = w.PlaceVehicle(t.PointAt(-1.0f, -0.4f), VehicleKind.Car, 90f, 0);
            var car = w.Traffic.Vehicles.Count > 0 ? w.Traffic.Vehicles[^1] : null;
            float carFarthest = float.MinValue; bool carHidden = true, carOut = false;
            for (int i = 0; i < 72 * 60 && car != null; i++)
            {
                w.Traffic.Step(Dt);
                if (i % 12 != 0) continue;
                w.RefreshVisibility();
                carFarthest = Mathf.Max(carFarthest, car.Position.x);
                if (!w.InWindow(car.Position, 0.1f)) { carOut = true; carHidden &= !car.Root.gameObject.activeSelf; }
            }
            Check(street == null && placed == null && car != null && carOut && carHidden && carFarthest > 2.5f,
                $"A car drives out of the window along its street, unseen but on its way (as far as x = {carFarthest:0.0}; {street ?? placed ?? "placed"})");

            // Going after it: the window follows, the train and the far house appear.
            w.Navigate(new Vector2Int(1, 0), 0.5f); w.FinishSlide(); w.Navigate(new Vector2Int(1, 0), 0.5f); w.FinishSlide(); w.Navigate(new Vector2Int(1, 0), 0.5f); w.FinishSlide();
            w.RefreshVisibility();
            activeHouses = 0; foreach (Transform h in houses) if (h.gameObject.activeSelf) activeHouses++;
            var at = train != null ? w.Trains.LocoPosition(train) : Vector3.zero;
            bool trainShown = train != null && (w.InWindow(at) == w.Trains.Drawn(train));
            Check(activeHouses == 1 && w.Buildings.transform.GetChild(1).gameObject.activeSelf && trainShown, $"Following it east, the far house and the train show where the window now is (train at x = {at.x:0.0})");
            // People get about wherever there is anything for them.
            w.People.Grid.Invalidate();
            for (int i = 0; i < 400 && (w.People.Grid.Busy || !w.People.Grid.Ready); i++) w.People.Grid.Update(50f);
            var area = w.People.Grid.Area;
            Check(area.xMin < 0.25f && area.xMax > 4.6f, $"Where people can walk covers the houses in and out of the window ({area.xMin:0.0} to {area.xMax:0.0} m)");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        private static bool HasActiveBeyond(Transform parent, float x)
        {
            foreach (Transform c in parent)
            {
                if (!c.gameObject.activeSelf) continue;
                var r = c.GetComponent<MeshRenderer>();
                if (r != null && r.bounds.size.sqrMagnitude > 0f && c.parent.InverseTransformPoint(r.bounds.center).x > x) return true;
            }
            return false;
        }

        /// <summary>The map: the whole world built so far, the trains on it, the window framed - and pointing at it goes there.</summary>
        private static void MapJourney()
        {
            var w = Endless("Map", 1400f);
            var t = w.Terrain;
            BuildGrid.Snap = true;
            Put(w, 0, 0.5f, -0.9f);
            Put(w, 0, 7.2f, 3.4f);
            var line = w.BuildTrack(Line(t, new Vector2(-1.3f, 0.3f), new Vector2(6.5f, 0.3f)));
            w.BuildPath(Line(t, new Vector2(-1.3f, -0.4f), new Vector2(6.5f, -0.4f)), PathKind.Road);
            w.PlaceTrain(t.PointAt(-1.0f, 0.3f), TrainKind.Passenger, Vector3.right, out var train);
            var map = new GameObject("Map") .AddComponent<MiniMap>();
            map.transform.SetPositionAndRotation(new Vector3(1400f, 1f, -1f), Quaternion.identity);
            map.Init(w, Materials().Screen);
            map.Refresh(true);
            var f = map.Shows;
            Check(f.Contains(new Vector2(7.2f, 3.4f)) && f.Contains(new Vector2(-1.3f, -1.5f)) && f.width >= 9f, $"The map shows everything built and the window ({f})");
            var house = map.ColourAt(new Vector2(7.2f, 3.4f)); var rail = map.ColourAt(new Vector2(3.0f, 0.3f)); var land = map.ColourAt(new Vector2(3.0f, 2.5f));
            var loco = train != null ? w.Trains.LocoPosition(train) : Vector3.zero;
            var dot = map.ColourAt(new Vector2(loco.x, loco.z));
            Check(house.r > 150 && house.g < 120 && rail.r > 100 && rail.b < 80 && land.g > 140 && dot.r > 240 && dot.g > 200 && dot.b < 100,
                $"Houses, the railway, the land and the train show on it (house {house}, rail {rail}, land {land}, train {dot})");
            // Pointing at the map's right half, a little up: the window slides there.
            var aim = new Vector2(f.xMin + f.width * 0.8f, f.yMin + f.height * 0.6f);
            var from = map.transform.TransformPoint(new Vector3((0.8f - 0.5f) * MiniMap.Size, (0.6f - 0.5f) * MiniMap.Size, -0.3f));
            bool picked = map.Pick(new Ray(from, map.transform.forward), out var at, out _);
            bool went = picked && w.NavigateTo(at); w.FinishSlide();
            Check(picked && (at - aim).magnitude < f.width / MiniMap.Pixels * 2f && went && (w.WindowCenter - aim).magnitude < 0.06f,
                $"Pointing at the map slides the window there (aimed at {aim}, picked {at}, now at {w.WindowCenter})");
            File.WriteAllBytes(Path.Combine(WonderlandRenders.Output, "editor-minimap.png"), map.Texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(map.gameObject);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>The window seen from a player's eye height: the cut edge, the arrows, half a window after going east.</summary>
        public static void WindowRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(12f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.6f, -2.6f), new Vector3(0f, 0f, 0f), 60f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            File.Delete(Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = new GameObject("Window demo").AddComponent<WonderlandWorld>();
            w.Create(2f, Materials(), 7, true, endless: true);
            w.UseWindow(true);
            w.Sky.Shown = false;
            var t = w.Terrain;
            for (int k = 0; k < 40; k++) w.TerrainStroke(TerrainBrush.Raise, new Vector3(1.2f, 0f, 0.9f), 0.45f, 1f, 0.05f, 0f);
            for (int k = 0; k < 20; k++) w.WaterStroke(new Vector3(-1.3f, 0f, -1.2f), 0.35f, 1f, 0.05f, w.WaterLevelForStroke(new Vector3(-1.3f, 0f, -1.2f)), false);
            BuildGrid.Snap = true;
            w.BuildTrack(Line(t, new Vector2(-2.2f, 0.3f), new Vector2(2.2f, 0.3f)));
            w.BuildPath(Line(t, new Vector2(-2.2f, -0.4f), new Vector2(2.2f, -0.4f)), PathKind.Road);
            for (int i = 0; i < 6; i++) Put(w, i % 3, -1.0f + i * 0.35f, -0.75f, 180f);
            for (int k = 0; k < 10; k++) w.PaintForest(new Vector3(0.6f, 0f, 1.1f), 0.5f, 1f, 0.2f);
            w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
            w.SendMessage("LateUpdate");
            WonderlandRenders.Capture(cam, "editor-window-home.png", 1280, 800);
            w.Navigate(new Vector2Int(1, 0), 0.5f); w.FinishSlide(); w.SendMessage("LateUpdate");
            w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
            WonderlandRenders.Capture(cam, "editor-window-east.png", 1280, 800);
            cam.transform.position = new Vector3(1.9f, 0.35f, -1.9f); cam.transform.LookAt(new Vector3(1.2f, 0.02f, -1.2f));
            WonderlandRenders.Capture(cam, "editor-window-edge.png", 1280, 800);
            Shader.SetGlobalFloat("_WL_WindowOn", 0f);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
