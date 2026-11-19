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
            try { WindowJourney(); }
            catch (Exception e) { Fail("Exception: " + e); }
            finally { BuildGrid.Snap = gridWas; Shader.SetGlobalFloat("_WL_WindowOn", 0f); }
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            File.WriteAllText("Logs/window-results.txt", Report.ToString());
            Debug.Log("Wonderland window test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static WonderlandWorld Endless(string name, float x)
        {
            var w = new GameObject(name).AddComponent<WonderlandWorld>();
            w.transform.SetPositionAndRotation(new Vector3(x, 0f, 0f), Quaternion.Euler(0f, 25f, 0f));
            w.Create(2f, Materials(), 7, false, endless: true);
            w.UseWindow(true);
            return w;
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
            bool went = w.Navigate(new Vector2Int(1, 0));
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
            w.Navigate(new Vector2Int(-1, 0)); w.FinishSlide(); w.SendMessage("LateUpdate");
            Check(w.WindowCenter == Vector2.zero && new Vector2(w.Buildings.Houses[0].x, w.Buildings.Houses[0].z) == houseAt && w.Track.Network.Edges[0].Layout.Points[0] == trackStart
                && (w.PlacementPosition - placed).magnitude < 1e-4f, "Back west, home is exactly as it was");

            // 6. Every direction and back, many steps, and home.
            bool all = true;
            for (int d = 0; d < 8; d++)
            {
                var dir = WindowNavigator.Directions[d];
                w.Navigate(dir); w.FinishSlide();
                all &= w.WindowCenter == new Vector2(dir.x * 1.5f, dir.y * 1.5f);
                w.Navigate(-dir); w.FinishSlide();
                all &= w.WindowCenter == Vector2.zero;
            }
            Check(all && (w.PlacementPosition - placed).magnitude < 1e-4f, "The window goes all eight ways and back");
            int steps = 0;
            while (steps < 20 && w.Navigate(new Vector2Int(1, 1))) { w.FinishSlide(); steps++; }
            bool covered = t.Inside(w.WindowRect.xMax, w.WindowRect.yMax) && t.Inside(w.WindowRect.xMin, w.WindowRect.yMin);
            Check(steps >= 3 && covered && t.LandAt(w.WindowCenter.x, w.WindowCenter.y), $"Step after step north-east, there is always land under the window ({steps} steps to {w.WindowCenter}; {w.WhereLabel()})");
            w.NavigateHome(); w.FinishSlide(); w.SendMessage("LateUpdate");
            Check(w.WindowCenter == Vector2.zero && w.WhereLabel().StartsWith("at home"), "Home goes back to where the world started");

            // 7. The arrows: each lies outside the edge it leads over, and pointing at it finds it.
            bool arrows = true;
            for (int d = 0; d < 8; d++)
            {
                var spot = w.Navigator.SpotOf(d);
                var from = w.transform.TransformPoint(new Vector3(spot.x, 0.6f, spot.y));
                arrows &= !w.InWindow(spot) && w.Navigator.Pick(new Ray(from, -w.transform.up)) == d;
            }
            Check(arrows, "Eight arrows lie round the window, each found by pointing at it");

            // 8. Lifted off the floor: navigating keeps the height.
            w.Lift = 0.4f;
            w.Place(placed + Vector3.up * 0.4f, w.PlacementRotation);
            w.Navigate(new Vector2Int(0, -1)); w.FinishSlide();
            Check(Mathf.Abs(w.PlacementPosition.y - placed.y - 0.4f) < 1e-4f && Mathf.Abs(w.transform.position.y - placed.y - 0.4f) < 1e-4f, "Lifted off the floor, the world stays at its height while it slides");
            w.Navigate(new Vector2Int(0, 1)); w.FinishSlide();
            w.Place(placed, w.PlacementRotation);

            // 9. Saved and loaded: the land everywhere, the window where it was, everything built.
            w.Navigate(new Vector2Int(1, 0)); w.FinishSlide();
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
            w.SendMessage("LateUpdate"); w.Navigator.SendMessage("LateUpdate");
            w.Navigator.Hover(0);
            WonderlandRenders.Capture(cam, "editor-window-home.png", 1280, 800);
            w.Navigate(new Vector2Int(1, 0)); w.FinishSlide(); w.SendMessage("LateUpdate"); w.Navigator.SendMessage("LateUpdate");
            w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
            WonderlandRenders.Capture(cam, "editor-window-east.png", 1280, 800);
            cam.transform.position = new Vector3(1.9f, 0.35f, -1.9f); cam.transform.LookAt(new Vector3(1.2f, 0.02f, -1.2f));
            WonderlandRenders.Capture(cam, "editor-window-edge.png", 1280, 800);
            Shader.SetGlobalFloat("_WL_WindowOn", 0f);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
