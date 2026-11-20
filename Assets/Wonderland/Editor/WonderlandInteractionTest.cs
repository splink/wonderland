using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wonderland.Editor
{
    /// <summary>
    /// Drives the real <see cref="ToolController"/> and <see cref="PaletteUI"/> with synthetic controller input: a
    /// virtual right hand aims rays at palette tiles and at the landscape, presses and holds the trigger, uses the stick
    /// and the A / B buttons. This covers the input-to-world path (UI hit testing, tool switching, strokes, track
    /// drawing, placement, undo) that the API-level journey skips. Not covered: OVRInput itself, tracking, passthrough.
    /// Report: docs/interaction-test-results.txt; batch exit code 1 on failure.
    /// </summary>
    public static class WonderlandInteractionTest
    {
        private const float Dt = 1f / 72f;
        private static readonly StringBuilder Report = new();
        private static int failures;
        private static ToolController tools;
        private static PaletteUI palette;
        private static WonderlandWorld world;
        private static PointerInput current;
        private static bool triggerHeld, gripHeld;

        public static void Run()
        {
            Report.Clear(); failures = 0;
            bool gridWas = BuildGrid.Snap;
            BuildGrid.Snap = true;   // (the default: roads and railway on the grid)
            try { Journey(); }
            catch (Exception e) { Check(false, "Exception: " + e); }
            finally { BuildGrid.Snap = gridWas; }
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            File.WriteAllText("docs/interaction-test-results.txt", Report.ToString());
            Debug.Log("Wonderland interaction test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static void Check(bool ok, string what)
        {
            Report.AppendLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) failures++;
        }

        private static void Journey()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            world = new GameObject("Wonderland").AddComponent<WonderlandWorld>();
            world.transform.position = new Vector3(0f, 0f, 1.3f);
            world.Create(1.8f, WonderlandJourney.Materials());
            palette = new GameObject("Palette").AddComponent<PaletteUI>();
            palette.transform.SetPositionAndRotation(new Vector3(-0.15f, 1.0f, 0.3f), Quaternion.Euler(38f, 0f, 0f));
            palette.Build(world.Materials.UI, world.Materials.Lit);
            tools = new GameObject("Tools").AddComponent<ToolController>();
            tools.HapticsEnabled = false;
            tools.Init(world, palette, null);
            AppButtons();
            // Named worlds, kept in a scratch folder for the test.
            WonderlandWorld.SlotFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wonderland-slots-test");
            if (System.IO.Directory.Exists(WonderlandWorld.SlotFolder)) System.IO.Directory.Delete(WonderlandWorld.SlotFolder, true);
            System.IO.Directory.CreateDirectory(WonderlandWorld.SlotFolder);
            int keepSlot = WonderlandWorld.CurrentSlot;
            tools.SaveWorld = n => world.SaveSlot(n);
            tools.LoadWorld = n => world.LoadSlot(n, WonderlandJourney.Materials());
            bool keepTips = ToolController.TipsOn;
            var hand = new Vector3(0.2f, 1.1f, 0.25f);
            // Tips: on, a first tip appears in the banner above the palette after a moment; the Tips button turns them off.
            if (!ToolController.TipsOn) tools.ToggleTips(); else { tools.ToggleTips(); tools.ToggleTips(); }   // (on, and starting over)
            for (int i = 0; i < 72 * 6; i++) Frame(hand, hand + Vector3.forward, false);
            string firstTip = palette.TipText;
            ClickButton(hand, "Settings");
            ClickButton(hand, "Tips");
            var tipsSwitch = Find("Tips");
            bool tipsSaysOff = tipsSwitch != null && !tipsSwitch.Selected;   // (its light off)
            ClickButton(hand, "Settings");
            for (int i = 0; i < 3; i++) Frame(hand, hand + Vector3.forward, false);
            Check(!string.IsNullOrEmpty(firstTip) && !ToolController.TipsOn && tipsSaysOff && string.IsNullOrEmpty(palette.TipText) && !palette.InfoShown,
                $"A tip appears above the palette (\"{firstTip}\"), and Settings' Tips button turns tips off (and says so)");
            if (keepTips != ToolController.TipsOn) tools.ToggleTips();

            // Terrain: the default tool. Aim at the landscape and hold the trigger.
            Check(tools.Tool == Tool.Terrain, "Starts with the Terrain tool");
            var target = world.transform.TransformPoint(new Vector3(-0.2f, 0f, 0.2f));
            float before = world.Terrain.SurfaceAt(-0.2f, 0.2f);
            HoldAt(hand, () => world.transform.TransformPoint(new Vector3(-0.2f, world.Terrain.SurfaceAt(-0.2f, 0.2f), 0.2f)), 2.5f);
            float after = world.Terrain.SurfaceAt(-0.2f, 0.2f);
            Check(after > before + 0.05f, $"Trigger + aim raises a hill ({before * 100f:0.0} -> {after * 100f:0.0} cm)");
            Check(world.UndoDepth == 1, "One stroke = one undo step");

            // The left stick right grows the brush.
            float radius = tools.Radius;
            for (int i = 0; i < 40; i++) Frame(hand, target, false, stick: new Vector2(1f, 0f));
            Check(tools.Radius > radius * 1.3f, $"The left stick right enlarges the brush ({radius * 100f:0} -> {tools.Radius * 100f:0} cm)");

            // A cycles the sculpt mode.
            Frame(hand, target, false, primary: true);
            Check(tools.TerrainMode == TerrainBrush.Lower, "A switches Raise -> Lower");

            // Grow: pick the mode chip, aim past the shore at the floor, hold the trigger.
            ClickButton(hand, "Land");
            ClickButton(hand, "Grow");
            Check(tools.TerrainMode == TerrainBrush.Grow, "Grow chip selects the land brush");
            float landBefore = world.Terrain.LandArea();
            var floor = world.transform.TransformPoint(new Vector3(1.15f, 0f, -0.2f));
            HoldAt(hand, () => floor, 1.5f);
            Check(world.Terrain.EdgeAt(1.15f, -0.2f) > 0.1f && world.Terrain.LandArea() > landBefore, $"Aiming at the floor beyond the shore grows land ({landBefore:0.00} -> {world.Terrain.LandArea():0.00} m2)");
            ClickButton(hand, "Shape");
            ClickButton(hand, "Raise");

            // Palette: click the Forest tile, paint.
            ClickButton(hand, "Forest");
            Check(tools.Tool == Tool.Forest, "Clicking the Forest tile selects the forest brush");
            HoldPath(hand, new Vector3(0.35f, 0f, 0.4f), new Vector3(0.6f, 0f, 0.0f), 2.5f);
            Check(world.Forest.Trees.Count > 20, $"Painting with the trigger grows trees ({world.Forest.Trees.Count})");
            // No small limit on the forest: the whole island can be wooded (up to what the window can show).
            var keepTrees = world.Forest.Trees.ToArray();
            for (int pass = 0; pass < 12 && world.Forest.Full == null; pass++)
                for (float fx = -0.9f; fx <= 0.9f; fx += 0.15f)
                for (float fz = -0.9f; fz <= 0.9f; fz += 0.15f)
                    world.Forest.Paint(new Vector2(fx, fz), 0.12f, 1f, 0.1f, (_, _) => true);
            int many = world.Forest.Trees.Count, inView = world.Forest.InView();
            string full = world.Forest.Full;
            world.Forest.SetTrees(keepTrees); world.Forest.RebuildDirty();
            Check(many > 600 && inView <= ForestSystem.MaxInView && (full != null) == (inView >= ForestSystem.MaxInView), $"The forest takes far more than the old 450 trees: the island wooded all over ({many}, {inView} in the window{(full != null ? "; " + full : "")})");

            // Track: click the tile, draw a loop with the trigger held, release near the start.
            ClickButton(hand, "Track");
            Check(tools.Tool == Tool.Track, "Track tool selected");
            var loop = WonderlandJourney.HandDrawnLoop(world.Terrain, new Vector2(-0.05f, -0.1f), new Vector2(0.5f, 0.38f));
            for (int i = 0; i < loop.Count; i++)
            {
                var p = world.transform.TransformPoint(loop[i]);
                Frame(hand, p, true);
            }
            Frame(hand, world.transform.TransformPoint(loop[^1]), false);
            var net = world.Track.Network;
            Check(world.Track.HasTrack && net.Edges.Count == 1 && net.Edges[0].Closed, "Drawing a loop with the trigger lays a closed railway" + (world.Track.HasTrack ? $" ({net.TotalLength:0.00} m)" : ""));
            // Branch: draw from the loop outwards; a switch appears. A tap on the switch throws (and pins) it.
            var branch = new List<Vector3>();
            var from = net.Edges[0].Layout.Points[net.Edges[0].Layout.Points.Count / 8];
            var outward = new Vector3(from.x + 0.05f, 0f, from.z + 0.02f); outward = new Vector3(outward.x, 0f, outward.z).normalized;
            for (int i = 0; i <= 40; i++) { var q = from + outward * (i * 0.012f); branch.Add(world.Terrain.PointAt(q.x, q.z)); }
            foreach (var q in branch) Frame(hand, world.transform.TransformPoint(q), true);
            Frame(hand, world.transform.TransformPoint(branch[^1]), false);
            net = world.Track.Network;
            Check(net.Joints.Count == 1, $"Drawing from the track outwards branches off with a switch ({net.Joints.Count} switch, {net.Edges.Count} pieces; \"{tools.LastMessage}\")");
            if (net.Joints.Count == 1)
            {
                int setting = net.Joints[0].Setting;
                var sw = world.transform.TransformPoint(net.Joints[0].Position);
                Frame(hand, sw, true); Frame(hand, sw, false);
                Check(net.Joints[0].Setting != setting && net.Joints[0].Pinned, "Tapping the switch throws it (and pins it)");
            }

            // Grip on the track and drag: the piece bends like a rope to where the pointer is let go.
            {
                ClickButton(hand, "Track");
                var n0 = world.Track.Network;
                int big = 0; for (int e = 1; e < n0.Edges.Count; e++) if (n0.Edges[e].Layout.Length > n0.Edges[big].Layout.Length) big = e;
                var l = n0.Edges[big].Layout;
                var grabAt = l.Evaluate(l.Length * 0.5f, out var tan); tan.y = 0f;
                var away = Vector3.Cross(Vector3.up, tan.normalized);
                if (Vector3.Dot(away, grabAt) < 0f) away = -away;   // away from the middle of the loop
                var to = grabAt + away * 0.08f;
                Vector3 W(Vector3 local) => world.transform.TransformPoint(new Vector3(local.x, world.Terrain.SurfaceAt(local.x, local.z), local.z));
                Frame(hand, W(grabAt), false); Frame(hand, W(grabAt), false, grip: true);
                for (int i = 1; i <= 30; i++) Frame(hand, W(Vector3.Lerp(grabAt, to, i / 30f)), false, grip: true);
                for (int i = 0; i < 10; i++) Frame(hand, W(to), false, grip: true);
                string dragSaid = tools.LastMessage;
                Frame(hand, W(to), false);
                float near = world.Track.Network.DistanceTo(to);
                float within = BuildGrid.Snap ? BuildGrid.Size * 0.5f + 0.005f : 0.015f;   // (on the grid: to the nearest grid line)
                Check(near < within, $"Grip on the track and drag bends it to where you let go (now {near * 100f:0.0} cm from there{(BuildGrid.Snap ? ", on the grid" : "")}; \"{tools.LastMessage}\", while dragging \"{dragSaid}\")");
            }
            // Trains: click the tile, trigger on the track places a running train; Stop all / Start all.
            ClickButton(hand, "Train");
            var mainEdge = 0; float longest = 0f;
            for (int e = 0; e < net.Edges.Count; e++) if (net.Edges[e].Layout.Length > longest) { longest = net.Edges[e].Layout.Length; mainEdge = e; }
            var onTrack = world.transform.TransformPoint(net.Edges[mainEdge].Layout.Points[net.Edges[mainEdge].Layout.Points.Count / 2]);
            Frame(hand, onTrack, true); Frame(hand, onTrack, false);
            Check(world.Trains.Trains.Count == 1 && world.Trains.Trains[0].Running, "Trigger on the railway puts a running train on it");
            for (int i = 0; i < 72 * 3; i++) world.Trains.Step(Dt);
            Check(world.Trains.Any && world.Trains.Trains[0].Odometer > 0.2f, $"Train moves ({(world.Trains.Any ? world.Trains.Trains[0].Odometer : 0f):0.00} m)");
            // Carriages: point at the train, then the palette's + / - carriage buttons.
            {
                var tr = world.Trains.Trains[0];
                world.Trains.Step(0f);
                var at = world.transform.TransformPoint(world.Trains.LocoPosition(tr));
                Frame(hand, at, false);
                int cars = TrainSystem.CarsOf(tr);
                ClickButton(hand, "Carriages");
                ClickButton(hand, "+ Carriage");
                int more = TrainSystem.CarsOf(tr);
                ClickButton(hand, "- Carriage");
                Check(more == cars + 1 && TrainSystem.CarsOf(tr) == cars, $"Point at a train, then + / - Carriage couple and uncouple a carriage ({cars} -> {more} -> {TrainSystem.CarsOf(tr)})");
                Check(Find("Turn round") == null && tr.Dir > 0, "Trains run engine first, the way the arrows point (no Turn round)");
            }
            ClickButton(hand, "Running");
            ClickButton(hand, "Stop all");
            Check(!world.Trains.AnyRunning, "Stop all stops the trains");
            ClickButton(hand, "Start all");
            Check(world.Trains.AnyRunning, "Start all sets them running again");

            // Build: tile, aim inside the loop, trigger; stick rotates.
            ClickButton(hand, "Build");
            var spot = world.transform.TransformPoint(new Vector3(-0.05f, world.Terrain.SurfaceAt(-0.05f, -0.1f), -0.1f));
            for (int i = 0; i < 20; i++) Frame(hand, spot, false, stick: new Vector2(1f, 0f));
            Frame(hand, spot, true); Frame(hand, spot, false);
            Check(world.Buildings.Houses.Count == 1, "Trigger places a house where the pointer is");
            Check(world.Buildings.Houses.Count == 1 && Mathf.Abs(world.Buildings.Houses[0].rotation - 90f) < 0.01f && Mathf.Abs(world.Buildings.Houses[0].x / BuildGrid.Size - Mathf.Round(world.Buildings.Houses[0].x / BuildGrid.Size)) < 0.01f, $"On the grid a flick of the stick turns the house a quarter turn, and it sits on a grid point ({(world.Buildings.Houses.Count > 0 ? world.Buildings.Houses[0].rotation : 0f):0}°)");
            ClickButton(hand, "Thatched");
            var spot2 = world.transform.TransformPoint(new Vector3(0.18f, world.Terrain.SurfaceAt(0.18f, -0.12f), -0.12f));
            Frame(hand, spot2, true); Frame(hand, spot2, false);
            Check(world.Buildings.Houses.Count == 2 && world.Buildings.Houses[1].variant == 2, "House type chips choose the variant");

            // B undoes the last placement.
            Frame(hand, spot2, false, secondary: true);
            Check(world.Buildings.Houses.Count == 1, "B undoes the last house");

            // Eraser: tile, tap the remaining house.
            ClickButton(hand, "Eraser");
            var h = world.Buildings.Houses[0];
            var houseTop = world.transform.TransformPoint(new Vector3(h.x, world.Terrain.SurfaceAt(h.x, h.z) + 0.05f, h.z));
            Frame(hand, houseTop, true); Frame(hand, houseTop, false);
            Check(world.Buildings.Houses.Count == 0, "Eraser tap removes the house under the pointer");

            // Palette Undo button.
            ClickButton(hand, "Undo");
            Check(world.Buildings.Houses.Count == 1, "Palette Undo restores it");

            // The library: a category, then a family, then the object's tile (a little model of it) beside the palette;
            // pointing at a tile says what the object is and does.
            ClickButton(hand, "Build");
            ClickButton(hand, "Leisure");
            ClickButton(hand, "Evenings");
            bool shown = palette.LibraryShown && Find("Disco") != null && Find("Disco").GetComponentInChildren<MeshFilter>().sharedMesh != null;
            ClickButton(hand, "Disco");
            Check(shown && tools.HouseVariant == Catalog.Disco, $"The library: Leisure, then Evenings, then the disco's tile with its model (chosen {tools.HouseVariant})");
            var stationTile = (Vector3?)null;
            ClickButton(hand, "Travel"); ClickButton(hand, "Railway");
            if (Find("Station") != null) stationTile = Find("Station").transform.position;
            if (stationTile.HasValue) { Physics.SyncTransforms(); Frame(hand, stationTile.Value, false); palette.SendMessage("Update"); }
            string about = palette.LibraryInfo ?? "";
            Check(about.Contains("Station") && about.Contains("beside the railway"), $"Pointing at a tile says what it is, what it does and where it must go (\"{about.Replace("\n", " / ")}\")");
            int[] counts = new int[Catalog.CategoryNames.Length]; foreach (var b in Catalog.All) counts[(int)b.Category]++;
            int total = 0, unfamilied = 0; foreach (int c in counts) total += c;
            foreach (var b in Catalog.All) if (b.Family == null || b.Family == "Other" || b.About == null) unfamilied++;
            Check(total == Catalog.Count && unfamilied == 0, $"Every object in the library is in a category and a family, with a description ({total} of {Catalog.Count}, {unfamilied} without)");
            ClickButton(hand, "Town");
            ClickButton(hand, "Squares");
            ClickButton(hand, "Fountain");
            var free = new Vector2(0.12f, 0.18f);
            for (int k = 0; k < 60; k++)
            {
                var c = new Vector2(Mathf.Cos(k * 2.4f), Mathf.Sin(k * 2.4f)) * (0.02f + k * 0.008f) + new Vector2(-0.05f, 0.05f);
                var seen = world.transform.TransformPoint(new Vector3(c.x, world.Terrain.SurfaceAt(c.x, c.y), c.y));
                var o = world.transform.InverseTransformPoint(hand); var dir = world.transform.InverseTransformDirection((seen - hand).normalized);
                if (world.ValidateHouse(new HouseInstance { x = c.x, z = c.y, variant = Catalog.Fountain }) == null && world.Buildings.Pick(o, dir) < 0) { free = c; break; }   // (and not behind a house)
            }
            var square = world.transform.TransformPoint(new Vector3(free.x, world.Terrain.SurfaceAt(free.x, free.y), free.y));
            int housesBefore = world.Buildings.Houses.Count;
            Frame(hand, square, true); Frame(hand, square, false);
            Check(world.Buildings.Houses.Count == housesBefore + 1 && world.Buildings.Houses[^1].variant == Catalog.Fountain, $"Category and item chips place a fountain from the Town set ({tools.LastMessage}; selected {tools.HouseVariant})");

            // Paths: draw a footpath; draw a fence.
            ClickButton(hand, "Roads");
            ClickButton(hand, "Paths");
            var pathRow = Find("Path");
            Check(palette.DetailsShown && palette.DetailsTitle != null && palette.DetailsTitle.Contains("Paths") && pathRow != null && pathRow.Shown.Contains("people on foot") && Find("Streets") is { Kind: OptionKind.Choice },
                $"The palette's row holds groups (Streets, Paths, Fences, Vehicles); picking one lists its kinds beside it, each with what it is ({palette.DetailsTitle})");
            ClickButton(hand, "Path");
            HoldPath(hand, new Vector3(-0.3f, 0f, -0.02f), new Vector3(-0.05f, 0f, 0.05f), 1f);
            Check(world.Paths.Lines.Count == 1 && world.Paths.Lines[0].kind == (int)PathKind.Footpath, $"Drawing with the Paths tool lays a footpath ({world.Paths.Lines.Count})");
            ClickButton(hand, "Fences");
            ClickButton(hand, "Fence");
            HoldPath(hand, new Vector3(-0.3f, 0f, -0.2f), new Vector3(-0.1f, 0f, -0.25f), 1f);
            Check(world.Paths.Lines.Count == 2 && world.Paths.Lines[1].kind == (int)PathKind.Fence, "...and a fence");
            // Roads: a wobbly stroke comes out straight; a bus chip puts a bus on it that drives off.
            ClickButton(hand, "Streets");
            ClickButton(hand, "Road");
            HoldPath(hand, new Vector3(-0.35f, 0f, 0.14f), new Vector3(0.25f, 0f, 0.16f), 1.2f);
            var road = world.Paths.Lines[^1];
            Check(world.Paths.Lines.Count == 3 && road.kind == (int)PathKind.Road, $"The Road chip lays a road ({tools.LastMessage})");
            ClickButton(hand, "Vehicles");
            ClickButton(hand, "Double-decker");
            var stop = world.transform.TransformPoint(new Vector3(0f, world.Terrain.SurfaceAt(0f, 0.15f), 0.15f));
            Frame(hand, stop, true); Frame(hand, stop, false);
            var bus = world.Traffic.Vehicles.Find(v => v.Roams);
            Check(bus != null && bus.Kind == VehicleKind.Bus && bus.Colour == 1, $"The Double-decker in the Vehicles group puts a double-decker bus on the road ({tools.LastMessage})");
            ClickButton(hand, "Streets");
            Check(tools.PlacingVehicle == null, "...and the Streets group goes back to drawing roads");

            // Life: a hen on the grass.
            ClickButton(hand, "Life");
            ClickButton(hand, "Farm");
            ClickButton(hand, "Hen");
            var yard = world.transform.TransformPoint(new Vector3(0.05f, world.Terrain.SurfaceAt(0.05f, 0.05f), 0.05f));
            int animals = world.Animals.Animals.Count;
            Frame(hand, yard, true); Frame(hand, yard, false);
            Check(world.Animals.Animals.Count == animals + 1 && world.Animals.Animals[^1].kind == (int)AnimalKind.Chicken, "The Life tool's chips place a hen");
            // ...and set the pace of the village clock.
            ClickButton(hand, "Time");
            ClickButton(hand, "Fast days");
            bool fast = world.Clock.Mode == ClockMode.Fast;
            ClickButton(hand, "Stop time");
            float hour = world.Clock.Hour; world.Clock.Tick(30f);
            Check(fast && world.Clock.Mode == ClockMode.Paused && world.Clock.Hour == hour, "The Life tool's clock chips speed the days up and stop time");
            ClickButton(hand, "Days pass");
            Check(world.Clock.Mode == ClockMode.Normal, "...and let the days pass again");
            // ...and call a fête round a fountain (tapped again, it is called off).
            string fountain = "no spot";
            foreach (var at in new[] { new Vector2(0.3f, 0.3f), new Vector2(-0.3f, 0.3f), new Vector2(0.3f, -0.35f), new Vector2(-0.35f, -0.4f) })
            {
                fountain = world.PlaceHouse(new HouseInstance { x = at.x, z = at.y, variant = Catalog.Fountain });
                if (fountain == null) break;
            }
            if (world.People.People.Count == 0) world.PlacePerson(new Vector3(0.1f, 0f, 0.1f), 0, 77);
            world.Clock.Hour = 15f;
            ClickButton(hand, "Events");
            ClickButton(hand, "Fête");
            bool on = world.Fete != null;
            ClickButton(hand, "Fête");
            Check(fountain == null && on && world.Fete == null, "The Life tool's Fête chip calls the village to the fountain, and calls it off" + (fountain == null ? "" : $" (\"{fountain}\")"));
            // ...the clouds come and go.
            bool clouds = world.Sky.Shown;
            ClickButton(hand, "Weather");
            ClickButton(hand, "Clouds");
            bool toggled = world.Sky.Shown != clouds;
            ClickButton(hand, "Clouds");
            Check(toggled && world.Sky.Shown == clouds, "The Life tool's Clouds chip hides and shows the clouds");

            // Watch: put up a camera, then watch it - the viewer appears above the palette; tap the land to aim it.
            string camera = "no spot";
            foreach (var at in new[] { new Vector2(0.15f, -0.3f), new Vector2(-0.2f, -0.3f), new Vector2(0.25f, 0.15f) })
            {
                camera = world.PlaceHouse(new HouseInstance { x = at.x, z = at.y, variant = Catalog.Camera });
                if (camera == null) break;
            }
            // Select: trigger on the camera - its card, its picture on the monitor with buttons round it to turn it;
            // the stick turns it too, the trigger on the land points it there; Close puts the picture away.
            tools.CloseCards();
            ClickButton(hand, "Select");
            var feed = palette.transform.Find("Viewer");
            bool hiddenAtFirst = feed == null || !feed.gameObject.activeSelf;
            bool selected = false, buttons = false, turned = false, aimed = false;
            if (world.Cctv.Count > 0)
            {
                int ch = world.Cctv.HouseOf(0);
                var camAt = world.transform.TransformPoint(new Vector3(world.Buildings.Houses[ch].x, world.Buildings.TopOf(ch) - 0.01f, world.Buildings.Houses[ch].z));
                Frame(hand, camAt, false); Frame(hand, camAt, true); Frame(hand, camAt, false);
                feed = palette.transform.Find("Viewer");
                selected = palette.CardShown && world.Cctv.Watching && world.Cctv.Selected == 0 && feed != null && feed.gameObject.activeSelf;
                var right = Find(">");
                buttons = right != null && right.gameObject.activeInHierarchy;
                float yaw0 = world.Cctv.AimOf(0).yaw;
                if (buttons) { ClickButton(hand, ">"); ClickButton(hand, ">"); }
                turned = Mathf.Abs(Mathf.DeltaAngle(world.Cctv.AimOf(0).yaw, yaw0)) > 25f;
                float yaw1 = world.Cctv.AimOf(0).yaw;
                // A spot of open land to point it at (not the railway, which the trigger would select instead).
                var aimSpot = new Vector2(-0.3f, 0.3f);
                foreach (var c in new[] { new Vector2(-0.3f, 0.3f), new Vector2(-0.25f, 0.15f), new Vector2(0.3f, 0.1f), new Vector2(-0.35f, -0.2f), new Vector2(0.1f, 0.15f) })
                    if (world.Track.DistanceTo(new Vector3(c.x, 0f, c.y)) > 0.1f && world.Buildings.Nearest(c, 0.08f) < 0) { aimSpot = c; break; }
                var aimAt = world.transform.TransformPoint(new Vector3(aimSpot.x, world.Terrain.SurfaceAt(aimSpot.x, aimSpot.y), aimSpot.y));
                Frame(hand, aimAt, true); Frame(hand, aimAt, false);
                aimed = Mathf.Abs(Mathf.DeltaAngle(world.Cctv.AimOf(0).yaw, yaw1)) > 5f;
                // The stick turns and tilts it too.
                var aim0 = world.Cctv.AimOf(0);
                for (int i = 0; i < 72; i++) Frame(hand, aimAt + Vector3.up * 0.5f, false, stick: new Vector2(1f, 0f));
                for (int i = 0; i < 36; i++) Frame(hand, aimAt + Vector3.up * 0.5f, false, stick: new Vector2(0f, -1f));
                var aim1 = world.Cctv.AimOf(0);
                Check(Mathf.Abs(Mathf.DeltaAngle(aim1.yaw, aim0.yaw)) > 40f && aim1.pitch > aim0.pitch + 5f, $"The stick turns and tilts the selected camera (yaw {aim0.yaw:0}° -> {aim1.yaw:0}°, tilt {aim0.pitch:0}° -> {aim1.pitch:0}°)");
            }
            Check(camera == null && hiddenAtFirst && selected && buttons && turned && aimed,
                "Select: trigger on a camera - its card and its live picture over the palette, with buttons round it that turn it; the trigger on the land points it there" + (camera == null ? "" : $" (\"{camera}\")")
                + $" [first hidden {hiddenAtFirst}, selected {selected}, buttons {buttons}, turned {turned}, aimed {aimed}]");
            tools.CloseCards();
            Check(!world.Cctv.Watching && !feed.gameObject.activeSelf, "Close puts the picture away");
            ClickButton(hand, "Life");

            // Grip on the track in any tool (here Select) bends it; the label says so first. Over a switch it names it.
            if (world.Track.HasTrack)
            {
                ClickButton(hand, "Select");
                tools.CloseCards();
                var rail = world.Track.Network;
                int e0 = 0; float bestLen = 0f;
                for (int e = 0; e < rail.Edges.Count; e++) if (rail.Edges[e].Layout.Length > bestLen) { bestLen = rail.Edges[e].Layout.Length; e0 = e; }
                var mid = rail.Edges[e0].Layout.Evaluate(bestLen * 0.5f, out var tan);
                var midW = world.transform.TransformPoint(mid);
                Frame(hand, midW, false); Frame(hand, midW, false);
                string trackLabel = tools.PointerLabel ?? "";
                var side = Vector3.Cross(Vector3.up, tan.normalized) * 0.03f;
                
                Frame(hand, midW, false, grip: true);
                bool bending = tools.Holding;
                var toW = world.transform.TransformPoint(mid + side);
                for (int i = 0; i < 4; i++) Frame(hand, toW, false, grip: true);
                Frame(hand, toW, false);
                Check(trackLabel.Contains("Grip") && bending && !tools.Holding, $"Grip on the track bends it in any tool (label: \"{trackLabel.Replace("\n", " / ")}\") - {tools.LastMessage}");
                if (rail.Joints.Count > 0)
                {
                    var jw = world.transform.TransformPoint(world.Track.Network.Joints[0].Position);
                    Frame(hand, jw, false); Frame(hand, jw, false);
                    Check((tools.PointerLabel ?? "").Contains("Switch"), $"Pointing at a switch, the label says what it is and what the trigger does (\"{(tools.PointerLabel ?? "").Replace("\n", " / ")}\")");
                }
                world.Undo();
                // The Track tool rings every free end and switch; other tools don't.
                ClickButton(hand, "Track");
                Frame(hand, midW, false);
                int marks = tools.RailMarksShown;
                int ends = 0; foreach (var ed in world.Track.Network.Edges) if (!ed.Closed) for (int k = 0; k < 2; k++) if (ed.Joint[k] < 0) ends++;
                ClickButton(hand, "Select");
                Frame(hand, midW, false);
                Check(marks == ends + world.Track.Network.Joints.Count && tools.RailMarksShown == 0, $"The Track tool rings every free end and switch ({marks}: {ends} ends, {world.Track.Network.Joints.Count} switches); Select doesn't");
                // Cards with buttons: a switch's card chooses which way trains go; a piece of track's card bends it.
                if (world.Track.Network.Joints.Count > 0)
                {
                    var jw = world.transform.TransformPoint(world.Track.Network.Joints[0].Position);
                    tools.CloseCards();
                    Frame(hand, jw, false); Frame(hand, jw, true); Frame(hand, jw, false);
                    bool switchCard = palette.CardShown && palette.CardTitle == "Switch" && Find("Always left") != null;
                    ClickButton(hand, "Always left");
                    var j0 = world.Track.Network.Joints[0];
                    bool pinnedLeft = j0.Pinned;
                    ClickButton(hand, "Trains choose");
                    Check(switchCard && pinnedLeft && !j0.Pinned, $"Select a switch: its card says where each way leads, and Always left / Always right / Trains choose set it [card {switchCard}, pinned {pinnedLeft}, free {!j0.Pinned}]");
                }
                {
                    var rl = world.Track.Network;
                    int e1 = 0; float len1 = 0f;
                    for (int e = 0; e < rl.Edges.Count; e++) if (rl.Edges[e].Layout.Length > len1) { len1 = rl.Edges[e].Layout.Length; e1 = e; }
                    var mid1 = rl.Edges[e1].Layout.Evaluate(len1 * 0.3f, out var tan1);
                    var midW1 = world.transform.TransformPoint(mid1);
                    tools.CloseCards();
                    Frame(hand, midW1, false); Frame(hand, midW1, true); Frame(hand, midW1, false);
                    bool trackCard = palette.CardShown && palette.CardTitle == "Railway line";
                    ClickButton(hand, "Bend");
                    bool bending1 = tools.Holding && !palette.CardShown;
                    var toW1 = world.transform.TransformPoint(mid1 + Vector3.Cross(Vector3.up, tan1.normalized) * 0.03f);
                    for (int i = 0; i < 3; i++) Frame(hand, toW1, false);
                    Frame(hand, toW1, true); Frame(hand, toW1, false);
                    Check(trackCard && bending1 && !tools.Holding && tools.LastMessage == "Track reshaped", $"Select the track: its card's Bend button bends it, the trigger puts it down [card {trackCard}, bending1 {bending1}] - {tools.LastMessage}");
                    world.Undo();
                }
                BridgeCard(hand);
            }
            // A building's card: Move (trigger puts it down), Remove (B brings it back).
            {
                ClickButton(hand, "Select");
                int bh = world.Buildings.Houses.FindIndex(h => h.variant != Catalog.Camera);
                if (bh >= 0)
                {
                    var hh = world.Buildings.Houses[bh];
                    var roof = world.transform.TransformPoint(new Vector3(hh.x, world.Buildings.TopOf(bh) - 0.01f, hh.z));
                    tools.CloseCards();
                    Frame(hand, roof, false); Frame(hand, roof, true); Frame(hand, roof, false);
                    int n = world.Buildings.Houses.Count;
                    bool opened = palette.CardShown && Find("Remove") != null;
                    ClickButton(hand, "Remove");
                    bool gone = world.Buildings.Houses.Count == n - 1 && !palette.CardShown;
                    world.Undo();
                    bool backAgain = world.Buildings.Houses.Count == n;
                    Check(opened && gone && backAgain, $"A building's card has Remove; B brings it back [card {opened}, removed {gone}, back {backAgain}]");
                }
            }

            // Several worlds: save this one as World 2, change it, save that as World 3, and load World 2 back.
            int houses = world.Buildings.Houses.Count;
            ClickButton(hand, "Worlds"); ClickButton(hand, "Save"); ClickButton(hand, WorldButton(2));
            bool saved2 = WonderlandWorld.SlotUsed(2) && WonderlandWorld.CurrentSlot == 2;
            string extra = null;
            foreach (var at in new[] { new Vector2(-0.3f, -0.35f), new Vector2(0.35f, -0.3f), new Vector2(-0.35f, 0.35f) })
                if ((extra = world.PlaceHouse(new HouseInstance { x = at.x, z = at.y, variant = Catalog.Bench })) == null) break;
            ClickButton(hand, "Save"); ClickButton(hand, WorldButton(3));
            bool saved3 = WonderlandWorld.SlotUsed(3) && WonderlandWorld.CurrentSlot == 3 && world.Buildings.Houses.Count == houses + 1;
            ClickButton(hand, "Load"); ClickButton(hand, WorldButton(2));
            bool back = WonderlandWorld.CurrentSlot == 2 && world.Buildings.Houses.Count == houses;
            Check(extra == null && saved2 && saved3 && back, $"Save and Load keep several worlds: saved as World 2 and World 3, and World 2 loads back as it was ({world.Buildings.Houses.Count} buildings)");
            WonderlandWorld.CurrentSlot = keepSlot;
            WonderlandWorld.SlotFolder = null;

            // Help and Stats open beside the palette and close again; the stats are the world's real numbers.
            ClickButton(hand, "Help");
            var infoPanel = palette.transform.Find("Info");
            bool helpOpen = infoPanel != null && infoPanel.gameObject.activeSelf && palette.InfoShown;
            // Help in topics, as tabs along its top; each topic says what it's for, how, with an example.
            bool everyTopic = true;
            foreach (var topic in ToolController.HelpTopics) everyTopic &= Find(topic) != null;
            ClickButton(hand, "Moving");
            string moving = palette.InfoBody ?? "";
            ClickButton(hand, "Buildings");
            string buildings = palette.InfoBody ?? "";
            Check(helpOpen && everyTopic && moving.Contains("BOTH grips") && buildings != moving && buildings.Contains("EXAMPLE") && tools.HelpTopic == System.Array.IndexOf(ToolController.HelpTopics, "Buildings"),
                $"Help opens in {ToolController.HelpTopics.Length} topics as tabs; Moving tells of the two-hand twist, Buildings has its own page with an example" + (helpOpen && everyTopic ? "" : $" (open {helpOpen}, tabs {everyTopic})"));
            // A tooltip: the pointer resting on a button says more about it after a moment, and goes when it moves off.
            var undo = Find("Undo");
            Physics.SyncTransforms();
            var paletteUpdate = typeof(PaletteUI).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);   // (no player loop here)
            for (int i = 0; i < 20; i++) { Frame(hand, undo.transform.position, false); paletteUpdate.Invoke(palette, null); }
            string tip = palette.TooltipShown;
            for (int i = 0; i < 3; i++) { Frame(hand, hand + Vector3.down, false); paletteUpdate.Invoke(palette, null); }
            Check(!string.IsNullOrEmpty(tip) && tip == undo.Tip && palette.TooltipShown == null, $"Resting the pointer on a button shows its tooltip (\"{tip}\"), and it goes again");
            // Settings: the label at the pointer can be turned off (and on); Grid shows its state.
            ClickButton(hand, "Settings");
            bool settingsOpen = palette.InfoShown && Find("Labels") is { Selected: true } && Find("Grid") is { } grid && grid.Selected == BuildGrid.Snap && grid.Kind == OptionKind.Toggle
                && Find("World stats") is { Kind: OptionKind.Open };
            ClickButton(hand, "Labels");
            bool labelsOff = !ToolController.LabelsOn && Find("Labels") is { Selected: false };
            ClickButton(hand, "Labels");
            Check(settingsOpen && labelsOff && ToolController.LabelsOn, "Settings beside the palette: switches (Tips, Labels, Grid) show their state by their light and a tap changes it; World stats and Performance open panels");
            ClickButton(hand, "World stats");
            string stats = tools.StatsText();
            bool statsTrue = palette.InfoShown && stats.Contains($"{world.People.People.Count} people") && stats.Contains($"{world.Buildings.Houses.Count} buildings");
            // The performance panel: from Stats; a setting cycles when tapped and takes effect; Defaults puts it back.
            PerfSettings.ResetAll();
            ClickButton(hand, "Performance");
            bool perfOpen = palette.InfoShown && Find("Grass: Full") != null && Find("Measure") != null;
            int tufts = world.Cover.Count;
            ClickButton(hand, "Grass: Full"); ClickButton(hand, "Grass: Half"); world.Cover.RebuildDirty();
            bool grassOff = PerfSettings.Grass.Value == 2 && world.Cover.Count == 0 && Find("Grass: Off") != null;
            ClickButton(hand, "Defaults");
            Check(perfOpen && grassOff && PerfSettings.Grass.Value == 0 && world.Cover.Count == tufts, $"Stats opens the performance panel; Grass cycles to Off (no tufts) and Defaults brings them back ({tufts} tufts)");
            ClickButton(hand, "World stats");
            ClickButton(hand, "Settings");
            Check(helpOpen && statsTrue && !palette.InfoShown, "Help and Stats open beside the palette (with the world's own numbers) and close again");
            bool standIns = false; int levels = 0;
            foreach (var r in world.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Proxy") standIns = true;
                if (r.name.StartsWith("Detail ")) { levels++; var m = r.GetComponent<MeshFilter>().sharedMesh; if (m == null || !m.name.Contains("detail")) standIns = true; }
            }
            Check(!standIns && levels > 0, $"No blocky stand-ins anywhere: further away the same buildings and trees are drawn with less detail ({levels} simpler levels)");

            // The card: trigger on a villager with the Select tool opens theirs, and follows them.
            ClickButton(hand, "Select");
            if (world.People.People.Count == 0) world.PlacePerson(new Vector3(0.1f, 0f, 0.1f), 0, 77);
            // (someone in plain sight from the hand - the pointer can't see through walls): a villager on open ground
            var lo = world.transform.InverseTransformPoint(hand);
            for (float gz = -0.2f; gz <= 0.3f && world.People.People.Count < 100; gz += 0.05f)
            {
                bool placed = false;
                for (float gx = -0.3f; gx <= 0.3f; gx += 0.05f)
                {
                    var open = new Vector3(gx, world.Terrain.SurfaceAt(gx, gz) + 0.015f, gz);
                    var to = open - lo;
                    bool walled = world.Buildings.Pick(lo, to.normalized, out float wallT, true) >= 0 && wallT < to.magnitude;
                    if (walled || !world.People.Walkable(new Vector2(gx, gz)) || world.People.Nearest(open, 0.04f) >= 0) continue;
                    if (world.PlacePerson(new Vector3(gx, 0f, gz), 0, 4242) == null) { placed = true; break; }
                }
                if (placed) break;
            }
            int pick = world.People.People.Count - 1;
            world.Clock.Mode = ClockMode.Paused;
            var who = world.People.PositionOf(pick);
            var atWho = world.transform.TransformPoint(new Vector3(who.x, world.Terrain.SurfaceAt(who.x, who.y) + 0.015f, who.y));   // (at their middle)
            int count = world.People.People.Count;
            tools.CloseCards();
            Frame(hand, atWho, true); Frame(hand, atWho, false);
            var cardPanel = palette.transform.Find("Card");
            Check(palette.CardShown && world.People.People.Count == count && cardPanel != null, $"Select: the trigger on a villager opens their card ({world.People.NameOf(pick)})" + (palette.CardShown ? "" : $" [following {tools.Following}, hover {tools.HoverPerson}, tool {tools.Tool}, people {count}->{world.People.People.Count}, hidden {world.People.IsHidden(pick)}]"));
            // Their camera: on as they are selected, on the monitor over the palette, filmed through their eyes; the
            // card's Camera button turns it off.
            ToolController.FollowCamera = true;
            var viewer = palette.transform.Find("Viewer");
            int seedOf = world.People.People[pick].seed, filmedBefore = world.Cctv.FilmedForViewer;
            for (int k = 0; k < 6; k++) world.Cctv.Step(0.1f);
            Check(world.Cctv.FollowSeed == seedOf && viewer != null && viewer.gameObject.activeSelf && world.Cctv.FilmedForViewer > filmedBefore && world.Cctv.FollowView != null,
                $"Selecting a villager turns on their camera: the monitor over the palette shows them {world.Cctv.FollowView} ({world.Cctv.FilmedForViewer - filmedBefore} pictures)");
            ClickButton(hand, "Camera on");
            Check(world.Cctv.FollowSeed == 0 && !viewer.gameObject.activeSelf && palette.CardShown, "The card's Camera button turns it off (the card stays)");
            ToolController.FollowCamera = true;
            // Any tool: pointing at someone names them, and the label at the pointer says what the buttons do; grip picks
            // them up, and letting go sets them down where the pointer is.
            tools.CloseCards();
            ClickButton(hand, "Build");
            Frame(hand, atWho, false); Frame(hand, atWho, false);
            string named = palette.HintText, labelled = tools.PointerLabel ?? "";
            string peeked = $"peek person {tools.PeekPerson}, house {tools.PeekHouse}";
            int seedPick = world.People.People[pick].seed;
            Frame(hand, atWho, false, grip: true);
            bool lifted = tools.Holding && world.People.People.Count == count - 1;
            var setDown = world.transform.TransformPoint(new Vector3(who.x + 0.04f, world.Terrain.SurfaceAt(who.x + 0.04f, who.y), who.y));
            Frame(hand, setDown, false, grip: true); Frame(hand, setDown, false);
            int moved = -1;
            for (int i = 0; i < world.People.People.Count; i++) if (world.People.People[i].seed == seedPick) moved = i;
            bool setOk = !tools.Holding && moved >= 0 && world.People.People.Count == count && Mathf.Abs(world.People.People[moved].x - (who.x + 0.04f)) < 0.01f;
            Check(named.Contains(world.People.NameOf(moved >= 0 ? moved : pick)) && labelled.Contains("Grip") && lifted && setOk && tools.Tool == Tool.Houses,
                $"With any tool, pointing at a villager names them (label: \"{labelled.Replace("\n", " / ")}\"); grip picks them up and letting go sets them down there [{peeked}, lifted {lifted}, set {setOk}]");
            if (moved >= 0) { pick = moved; who = world.People.PositionOf(pick); atWho = world.transform.TransformPoint(new Vector3(who.x, world.Terrain.SurfaceAt(who.x, who.y) + 0.015f, who.y)); }
            // A building: grip picks it up in any tool (here Roads); letting go where it can't go puts it back. Select's
            // trigger opens its card.
            ClickButton(hand, "Roads");
            int someHouse = world.Buildings.Houses.Count - 1;
            if (someHouse >= 0)
            {
                var hh = world.Buildings.Houses[someHouse];
                int liftFrom = world.Buildings.Houses.Count;
                var roof = world.transform.TransformPoint(new Vector3(hh.x, world.Buildings.TopOf(someHouse) - 0.01f, hh.z));
                Frame(hand, roof, false); Frame(hand, roof, false, grip: true);
                bool picked = tools.Holding && world.Buildings.Houses.Count == liftFrom - 1;
                var water = world.transform.TransformPoint(new Vector3(5f, 0f, 5f));   // (off the land: it can't go there)
                Frame(hand, water, false, grip: true); Frame(hand, water, false);
                bool wentBack = !tools.Holding && world.Buildings.Houses.Count == liftFrom;
                Check(picked && wentBack, $"Grip picks a building up in any tool; let go where it can't go and it goes back [picked {picked}, back {wentBack}]");
                ClickButton(hand, "Select");
                int sh = world.Buildings.Houses.Count - 1;
                hh = world.Buildings.Houses[sh];
                roof = world.transform.TransformPoint(new Vector3(hh.x, world.Buildings.TopOf(sh) - 0.01f, hh.z));
                tools.CloseCards();
                Frame(hand, roof, false); Frame(hand, roof, true); Frame(hand, roof, false);
                bool titled = palette.CardTitle == Catalog.Get(hh.variant).Name || hh.variant == Catalog.Camera && palette.CardTitle.StartsWith("Camera") || palette.CardTitle == world.People.PlaceCardOf(sh).Name;
                Check(palette.CardShown && titled, $"Select: the trigger on a building opens its card (\"{palette.CardTitle}\")");
            }
            // Pointing at someone puts a gold ring under them; selecting another villager moves the camera to them.
            ClickButton(hand, "Select");
            tools.CloseCards();
            Frame(hand, atWho, false); Frame(hand, atWho, false);
            var ring = GameObject.Find("Target ring");
            bool ringed = ring != null && ring.activeSelf && (new Vector2(ring.transform.position.x, ring.transform.position.z) - new Vector2(atWho.x, atWho.z)).magnitude < 0.03f;
            Frame(hand, atWho, true); Frame(hand, atWho, false);
            int firstSeed = world.Cctv.FollowSeed;
            int second = -1;
            for (float gx = -0.3f; gx <= 0.3f && second < 0; gx += 0.05f)
            {
                var open = new Vector3(gx, world.Terrain.SurfaceAt(gx, 0.25f) + 0.015f, 0.25f);
                var to = open - world.transform.InverseTransformPoint(hand);
                if (world.Buildings.Pick(world.transform.InverseTransformPoint(hand), to.normalized, out float wt2, true) >= 0 && wt2 < to.magnitude) continue;
                if (!world.People.Walkable(new Vector2(gx, 0.25f)) || world.People.Nearest(open, 0.04f) >= 0) continue;
                if (world.PlacePerson(new Vector3(gx, 0f, 0.25f), 1, 4343) == null) second = world.People.People.Count - 1;
            }
            if (second >= 0)
            {
                var p2 = world.People.PositionOf(second);
                var atSecond = world.transform.TransformPoint(new Vector3(p2.x, world.Terrain.SurfaceAt(p2.x, p2.y) + 0.015f, p2.y));
                Frame(hand, atSecond, false); Frame(hand, atSecond, true); Frame(hand, atSecond, false);
            }
            Check(ringed && firstSeed != 0 && second >= 0 && world.Cctv.FollowSeed == world.People.People[second].seed && palette.CardTitle == world.People.NameOf(second),
                $"A gold ring marks who the trigger would select; selecting another villager moves the camera to them ({palette.CardTitle})");
            // Select on a train: its card, with its camera.
            if (world.Trains.Trains.Count > 0)
            {
                var t = world.Trains.Trains[0];
                var at = world.transform.TransformPoint(world.Trains.LocoPosition(t) + Vector3.up * 0.02f);
                tools.CloseCards();
                Frame(hand, at, false); Frame(hand, at, true); Frame(hand, at, false);
                Check(palette.CardShown && Find("Camera") != null && Find("Camera").gameObject.activeInHierarchy, $"Select: the trigger on a train opens its card, with its camera (\"{palette.CardTitle}\")");
                ClickButton(hand, "Camera");
                Check(world.Cctv.Watching && palette.transform.Find("Viewer").gameObject.activeSelf, "...which rides along on the monitor");
                tools.CloseCards();
                Check(!world.Cctv.Watching && !palette.transform.Find("Viewer").gameObject.activeSelf, "Close puts the camera away");
            }
            world.Clock.Mode = ClockMode.Normal;
            // The clock and the performance light are always on the palette.
            Check(palette.ClockText == world.Clock.Label, $"The village clock is always shown ({palette.ClockText})");

            // Undo and Redo.
            int housesNow = world.Buildings.Houses.Count;
            string redoBench = world.PlaceHouse(new HouseInstance { x = 0.32f, z = -0.1f, variant = Catalog.Bench });
            world.Undo();
            bool undone = world.Buildings.Houses.Count == housesNow;
            ClickButton(hand, "Redo");
            Check(redoBench == null && undone && world.Buildings.Houses.Count == housesNow + 1, "Undo takes a step back and Redo puts it back" + (redoBench ?? ""));

            // The right stick scrolls the world the way the player faces; the left stick still sizes the brush.
            tools.SelectTool(Tool.Terrain);
            world.UseWindow(true);
            var head = new GameObject("Head").transform;
            head.SetPositionAndRotation(new Vector3(0f, 1.5f, 0f), Quaternion.LookRotation(Vector3.right));
            tools.Head = head;
            var aim = world.transform.TransformPoint(new Vector3(0.3f, 0f, -0.3f));
            var start = world.WindowCenter; var placedAt = world.PlacementPosition;
            float brush = tools.Radius;
            for (int i = 0; i < 72; i++) Frame(hand, aim, false, scroll: new Vector2(0f, 1f));
            var went = world.WindowCenter - start;
            Check(went.x > 0.3f && Mathf.Abs(went.y) < 0.02f && (world.PlacementPosition - placedAt).magnitude < 1e-3f && tools.Radius == brush,
                $"The right stick scrolls the world the way the player faces ({went.x * 100f:0} cm east in a second, facing east); the world stays where it is in the room, the brush as it was");
            for (int i = 0; i < 20; i++) Frame(hand, aim, false, scroll: new Vector2(-1f, 0f));
            Check(world.WindowCenter.y > start.y + 0.05f, $"...and left and right ({(world.WindowCenter.y - start.y) * 100f:0} cm north for left, facing east)");
            // Turning the world: hold grip and push the right stick sideways - it turns about the window's middle.
            var turnedFrom = world.PlacementRotation.eulerAngles.y; var middle = world.PlacementPosition;
            for (int i = 0; i < 36; i++) Frame(hand, aim + Vector3.up * 3f, false, grip: true, scroll: new Vector2(1f, 0f));
            Frame(hand, aim + Vector3.up * 3f, false);
            float turnedBy = Mathf.DeltaAngle(turnedFrom, world.PlacementRotation.eulerAngles.y);
            Check(turnedBy > 20f && (world.PlacementPosition - middle).magnitude < 1e-3f, $"Grip and the right stick sideways turn the world about the window's middle ({turnedBy:0} degrees in half a second)");
            // Both grips held, hands twisted: the world turns with them, about its middle; letting go puts it down.
            var twistFrom = world.PlacementRotation.eulerAngles.y;
            var left0 = new Vector3(-0.2f, 1.1f, 0.3f); var right0 = new Vector3(0.2f, 1.1f, 0.3f);
            Frame(hand, aim + Vector3.up * 3f, false, grip: true, leftGrip: true, leftHand: left0, rightHand: right0);
            for (int i = 1; i <= 10; i++)
            {
                var r = Quaternion.Euler(0f, 4f * i, 0f);   // (the hands turn 40 degrees about the point between them)
                Frame(hand, aim + Vector3.up * 3f, false, grip: true, leftGrip: true, leftHand: (left0 + right0) / 2 + r * (left0 - (left0 + right0) / 2), rightHand: (left0 + right0) / 2 + r * (right0 - (left0 + right0) / 2));
            }
            float twisted = Mathf.DeltaAngle(twistFrom, world.PlacementRotation.eulerAngles.y);
            Frame(hand, aim + Vector3.up * 3f, false);
            Frame(hand, aim + Vector3.up * 3f, false, grip: true, leftHand: left0, rightHand: right0 + Vector3.forward * 0.2f);   // (one grip: no turning)
            Frame(hand, aim + Vector3.up * 3f, false);
            Check(Mathf.Abs(twisted - 40f) < 1f && Mathf.Abs(Mathf.DeltaAngle(twistFrom, world.PlacementRotation.eulerAngles.y) - 40f) < 1f,
                $"Both grips held and the hands twisted turn the world with them ({twisted:0} of 40 degrees); one grip alone does not");
            // The right grip closing a moment before the left, on a building: the building goes back, the world turns.
            int hn = world.Buildings.Houses.Count - 1;
            if (hn >= 0)
            {
                var hb = world.Buildings.Houses[hn]; int housesWas = world.Buildings.Houses.Count;
                var top = world.transform.TransformPoint(new Vector3(hb.x, world.Buildings.TopOf(hn) - 0.01f, hb.z));
                float turnFrom = world.PlacementRotation.eulerAngles.y;
                Frame(hand, top, false, leftHand: left0, rightHand: right0);
                for (int i = 0; i < 6; i++) Frame(hand, top, false, grip: true, leftHand: left0, rightHand: right0);
                bool grabbed = tools.Holding;
                for (int i = 0; i <= 10; i++)
                {
                    var r = Quaternion.Euler(0f, 4f * i, 0f);
                    Frame(hand, top, false, grip: true, leftGrip: true, leftHand: (left0 + right0) / 2 + r * (left0 - (left0 + right0) / 2), rightHand: (left0 + right0) / 2 + r * (right0 - (left0 + right0) / 2));
                }
                bool turning = tools.TurningWorld;
                Frame(hand, top, false);
                float late = Mathf.DeltaAngle(turnFrom, world.PlacementRotation.eulerAngles.y);
                Check(grabbed && turning && !tools.Holding && world.Buildings.Houses.Count == housesWas && Mathf.Abs(late - 40f) < 1f,
                    $"The right grip closing first on a building, the left a moment later: the building goes back and the world turns with the hands ({late:0} of 40 degrees; picked up first: {grabbed}, back: {world.Buildings.Houses.Count == housesWas})");
            }
            // The World panel, in tabs: Place (Move), Turn, Height (Floor, Table, Lower, Higher), Size (Window, Scale).
            tools.ToggleMoving();
            bool tabs = Find("Place") != null && Find("Turn") != null && Find("Height") != null && Find("Size") != null && Find("Move") != null;
            ClickButton(hand, "Size");
            Check(!tools.PlacingWorld && tabs && Find("Scale: 100 %") != null && Find("Window: 2 m") != null,
                "The World button opens one panel in tabs - Place, Turn, Height, Size - each with its own buttons");
            float facing = world.PlacementRotation.eulerAngles.y;
            ClickButton(hand, "Turn");
            ClickButton(hand, "Turn right");
            Check(Mathf.Abs(Mathf.DeltaAngle(facing, world.PlacementRotation.eulerAngles.y) - 15f) < 0.1f, "Turn right turns the world 15 degrees");
            ClickButton(hand, "Place");
            ClickButton(hand, "Move");
            ClickButton(hand, "Height");
            float lift = world.Lift;
            float floorY = world.PlacementPosition.y - world.Lift;
            ClickButton(hand, "Higher"); ClickButton(hand, "Higher");
            Check(Mathf.Abs(world.Lift - lift - 0.1f) < 1e-3f && Mathf.Abs(world.PlacementPosition.y - floorY - world.Lift) < 1e-3f, $"Higher lifts the world 5 cm a tap ({world.Lift * 100f:0} cm)");
            float higher = world.Lift;
            for (int i = 0; i < 36; i++) Frame(hand, aim, false, stick: new Vector2(0f, 1f));
            Check(world.Lift > higher + 0.05f, $"...and so does the stick, up ({world.Lift * 100f:0} cm)");
            ClickButton(hand, "Table");
            Check(Mathf.Abs(world.Lift - 0.72f) < 1e-3f, $"Table puts it at table height ({world.Lift * 100f:0} cm)");
            ClickButton(hand, "Floor");
            ClickButton(hand, "Close");
            Check(world.Lift == 0f && !tools.PlacingWorld && !palette.InfoShown && Mathf.Abs(world.PlacementPosition.y - floorY) < 1e-3f, "Floor puts it back on the floor, Close puts it down and closes the panel");
            world.UseWindow(false);
            UnityEngine.Object.DestroyImmediate(head.gameObject);
        }

        /// <summary>
        /// A railway across a pond gets a bridge: pointing at its deck names it; Select's trigger on it opens its card, whose
        /// buttons are the styles that suit it (Lift among them); a tap on the deck with the Track tool, from the side,
        /// takes the next style. All undone afterwards.
        /// </summary>
        private static void BridgeCard(Vector3 hand)
        {
            var t = world.Terrain;
            var pond = new Vector3(-0.45f, 0f, 0.5f);
            world.Checkpoint();
            float lvl = world.WaterLevelForStroke(pond);
            for (float s = 0f; s < 1.6f; s += Dt) world.WaterStroke(pond, 0.15f, 1f, Dt, lvl, false);
            var line = new List<Vector3>();
            for (float x = -0.34f; x <= 0.341f; x += 0.02f) line.Add(t.PointAt(pond.x + x, pond.z));
            var plan = world.BuildTrack(line);
            int bi = world.Bridges.Find(new Vector2(pond.x, pond.z));
            Check(plan.Ok && bi >= 0, $"A railway across a pond gets a bridge (track: {plan.Error ?? "ok"}; {world.Bridges.Bridges.Count} bridges)");
            if (!plan.Ok || bi < 0) { world.Undo(); if (plan.Ok) world.Undo(); return; }
            string was = BridgeSystem.Names[(int)world.Bridges.Bridges[bi].Style];
            ClickButton(hand, "Select");
            tools.CloseCards();
            var deck = world.transform.TransformPoint(world.Bridges.Middle(bi));
            Frame(hand, deck, false); Frame(hand, deck, false);
            string hint = palette.HintText ?? "";
            Frame(hand, deck, true); Frame(hand, deck, false);
            bool card = palette.CardShown && palette.CardTitle == was && Find("Lift") != null && Find("Steel truss") != null;
            ClickButton(hand, "Lift");
            for (int i = 0; i < 3; i++) Frame(hand, hand + Vector3.forward, false);
            bi = world.Bridges.Find(new Vector2(pond.x, pond.z));
            bool lift = bi >= 0 && world.Bridges.Bridges[bi].Style == BridgeStyle.Lift && palette.CardTitle == "Lift bridge";
            Check(hint.Contains(was) && card && lift,
                $"Pointing at a bridge names it; Select's trigger on it opens its card with the styles that suit it, and Lift makes it a lift bridge [hint \"{hint}\", card {card} ({palette.CardTitle}), lift {lift}]");
            // A tap from the side with the Track tool: the deck is found by the ray, not the water under it.
            tools.CloseCards();
            ClickButton(hand, "Track");
            var side = world.transform.TransformPoint(world.Bridges.Middle(bi) + new Vector3(0f, 0f, -0.01f));
            int edges = world.Track.Network.Edges.Count;
            Frame(hand, side, false); Frame(hand, side, true); Frame(hand, side, false);
            bi = world.Bridges.Find(new Vector2(pond.x, pond.z));
            var after = bi >= 0 ? world.Bridges.Bridges[bi].Style : BridgeStyle.Lift;
            Check(after != BridgeStyle.Lift && world.Track.Network.Edges.Count == edges, $"A tap on the bridge with the Track tool takes the next style ({BridgeSystem.Names[(int)after]}), and draws no track");
            // While building: Track > Bridges chooses the style new bridges get.
            ClickButton(hand, "Bridges");
            string opened = palette.DetailsTitle;
            ClickButton(hand, "Lift");
            bool chosen = tools.BridgeChoice == BridgeStyle.Lift;
            string then = palette.DetailsTitle;
            tools.BridgeChoice = null;   // (Automatic again: the rest of the journey builds as before)
            Check(chosen && opened == "Track · Bridges" && then == opened, $"Track > Bridges chooses the style new bridges are built in [panel \"{opened}\", then \"{then}\", Lift chosen {chosen}]");
            ClickButton(hand, "Select");
            for (int k = 0; k < 4; k++) world.Undo();
        }

        private static void ClickButton(Vector3 hand, string label)
        {
            var button = Find(label);
            // Not on the page showing: turn the option row's (or the items') pages until it is.
            for (int turn = 0; button == null && turn < 6; turn++)
            {
                PaletteButton next = null;
                foreach (var b in palette.GetComponentsInChildren<PaletteButton>(false)) if (b.Text != null && b.Text.EndsWith("›") && b.Kind != OptionKind.Open) { next = b; if (turn % 2 == 0) break; }
                if (next == null) break;
                Physics.SyncTransforms();
                var at = next.transform.position;   // (the click rebuilds the row: the pager itself may go)
                Frame(hand, at, false); Frame(hand, at, true); Frame(hand, at, false);
                button = Find(label);
            }
            if (button == null) { Check(false, $"Button '{label}' exists"); return; }
            Physics.SyncTransforms();
            var p = button.transform.position;
            Frame(hand, p, false); Frame(hand, p, true); Frame(hand, p, false);
            if (button != null && palette.Hovered != null && palette.Hovered != button) Debug.Log($"Click on '{label}' ({Path(button.transform)} at {palette.transform.InverseTransformPoint(p)}) hit '{palette.Hovered.Text}' ({Path(palette.Hovered.transform)} at {palette.transform.InverseTransformPoint(palette.Hovered.transform.position)}) instead");
            static string Path(Transform t) { var s = t.name; while (t.parent != null && t.parent != palette.transform) { t = t.parent; s = t.name + "/" + s; } return s; }
        }

        /// <summary>A world's button in the Worlds panel, whatever it says of its state ("World 2 · saved").</summary>
        private static string WorldButton(int n)
        {
            foreach (var b in palette.InfoButtons) if (b.Text != null && b.Text.StartsWith($"World {n} ")) return b.Text;
            return $"World {n}";
        }

        private static PaletteButton Find(string label)
        {
            foreach (var b in palette.GetComponentsInChildren<PaletteButton>(false))   // (a showing one first)
                if (b.Text == label) return b;
            foreach (var b in palette.GetComponentsInChildren<PaletteButton>(true))
                if (b.Text == label) return b;
            return null;
        }

        private static void HoldAt(Vector3 hand, Func<Vector3> target, float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt) Frame(hand, target(), true);
            Frame(hand, target(), false);
        }

        private static void HoldPath(Vector3 hand, Vector3 fromLocal, Vector3 toLocal, float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                var l = Vector3.Lerp(fromLocal, toLocal, t / seconds);
                l.y = world.Terrain.SurfaceAt(l.x, l.z);
                Frame(hand, world.transform.TransformPoint(l), true);
            }
            Frame(hand, world.transform.TransformPoint(toLocal), false);
        }

        /// <summary>One simulated frame: the hand aims at <paramref name="target"/> with the given buttons.</summary>
        private static void Frame(Vector3 hand, Vector3 target, bool trigger, Vector2 stick = default, bool primary = false, bool secondary = false, bool grip = false, Vector2 scroll = default,
            bool leftGrip = false, Vector3 leftHand = default, Vector3 rightHand = default)
        {
            Physics.SyncTransforms();
            var input = new PointerInput
            {
                Valid = true, Ray = new Ray(hand, (target - hand).normalized),
                Trigger = trigger, TriggerDown = trigger && !triggerHeld, TriggerUp = !trigger && triggerHeld,
                LeftStick = stick, Stick = scroll, PrimaryDown = primary, SecondaryDown = secondary, Grip = grip, GripDown = grip && !gripHeld, GripUp = !grip && gripHeld,
                LeftGrip = leftGrip, LeftHand = leftHand, RightHand = rightHand,
            };
            triggerHeld = trigger; gripHeld = grip;
            tools.Process(input, Dt);
            world.Forest.RebuildDirty();
        }

        /// <summary>The palette set up as the app sets it up: the bottom bar in clusters, Help and Settings in the header.</summary>
        private static void AppButtons()
        {
            palette.AddGlobal(new PaletteUI.Option("Undo", false, () => world.Undo()) { Kind = OptionKind.Action, Tip = "Takes back the last change. B on the right controller does this too." }, 0);
            palette.AddGlobal(new PaletteUI.Option("Redo", false, () => world.Redo()) { Kind = OptionKind.Action, Tip = "Puts back what Undo took back." }, 0);
            palette.AddGlobal(new PaletteUI.Option("World", false, tools.ToggleMoving) { Kind = OptionKind.Open, Tip = "The world as a whole: move it, lift it, turn it, its window and scale." }, 1);
            palette.AddGlobal(new PaletteUI.Option("Home", false, () => world.NavigateHome()) { Kind = OptionKind.Action, Tip = "Scrolls back to where the world started." }, 1);
            palette.AddGlobal(new PaletteUI.Option("Worlds", false, tools.ToggleWorlds) { Kind = OptionKind.Open, Tip = "Save this world, load another, or start a new one." }, 2);
            palette.AddHeaderButton(new PaletteUI.Option("Help", false, tools.ToggleHelp) { Kind = OptionKind.Open, Tip = "How everything works, topic by topic." });
            palette.AddHeaderButton(new PaletteUI.Option("Settings", false, tools.ToggleSettings) { Kind = OptionKind.Open, Tip = "Sound, tips, labels, the grid, stats and performance." });
        }

        /// <summary>
        /// The palette in its states, as the player sees it (docs/renders/editor-panel-*.png): Terrain with its detail
        /// panel and a tooltip, Roads, Life, Build with the library, Help, Settings, Worlds and the World panel.
        /// </summary>
        public static void PanelRenders()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            world = new GameObject("Wonderland").AddComponent<WonderlandWorld>();
            world.transform.position = new Vector3(0f, 0f, 1.3f);
            world.Create(1.8f, WonderlandJourney.Materials());
            palette = new GameObject("Palette").AddComponent<PaletteUI>();
            palette.transform.SetPositionAndRotation(new Vector3(0f, 1.0f, 0.3f), Quaternion.Euler(38f, 0f, 0f));
            palette.Build(world.Materials.UI, world.Materials.Lit);
            tools = new GameObject("Tools").AddComponent<ToolController>();
            tools.HapticsEnabled = false;
            tools.Init(world, palette, null);
            AppButtons();
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.32f, 0.3f, 0.28f);
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 34f;
            cam.transform.position = palette.transform.position - palette.transform.forward * 0.62f;
            cam.transform.rotation = Quaternion.LookRotation(palette.transform.forward, palette.transform.up);
            cam.tag = "MainCamera";
            var hand = palette.transform.position - palette.transform.forward * 0.35f + palette.transform.right * 0.12f - palette.transform.up * 0.15f;   // (in front of it, low and to the right)
            void Shot(string name) { for (int k = 0; k < 12; k++) palette.SendMessage("Update"); WonderlandRenders.Capture(cam, $"editor-panel-{name}.png", 2400, 1000); }
            void Point(string label) { foreach (var pb in palette.GetComponentsInChildren<PaletteButton>()) if (pb.Text == label) { Physics.SyncTransforms(); Frame(hand, pb.transform.position, false); break; } }
            tools.SelectTool(Tool.Terrain);
            Point("Shape"); Shot("terrain");
            tools.SelectTool(Tool.Paths); Point("Road"); Shot("roads");
            tools.SelectTool(Tool.Decor); Point("Farm"); Shot("life");
            tools.SelectTool(Tool.Houses); Point("Homes"); Shot("build");
            tools.SelectTool(Tool.Train); tools.ToggleHelp(); Point("Help"); Shot("help");
            tools.ToggleSettings(); Point("Grid"); Shot("settings");
            tools.ToggleWorlds(); Shot("worlds");
            tools.ToggleMoving(); Point("World"); Shot("world");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Renders of the side panels as the player sees them (docs/renders/editor-cards-*.png): a villager's card with
        /// Stats, then a building's card with Help - driven through the controller like the test, in a small town that
        /// has lived for a while.
        /// </summary>
        public static void CardRenders()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            world = new GameObject("Wonderland").AddComponent<WonderlandWorld>();
            world.transform.position = new Vector3(0f, 0f, 1.3f);
            world.Create(1.8f, WonderlandJourney.Materials());
            palette = new GameObject("Palette").AddComponent<PaletteUI>();
            palette.transform.SetPositionAndRotation(new Vector3(0f, 1.0f, 0.3f), Quaternion.Euler(38f, 0f, 0f));
            palette.Build(world.Materials.UI, world.Materials.Lit);
            tools = new GameObject("Tools").AddComponent<ToolController>();
            tools.HapticsEnabled = false;
            tools.Init(world, palette, null);
            AppButtons();
            var t = world.Terrain;
            world.BuildPath(new System.Collections.Generic.List<Vector3> { t.PointAt(-0.6f, 0f), t.PointAt(0f, 0f), t.PointAt(0.6f, 0f) }, PathKind.Road);
            foreach (var (id, x, z, rot) in new[] { (Catalog.Cottage, -0.45f, 0.12f, 0f), (Catalog.Townhouse, -0.25f, 0.12f, 0f), (Catalog.Apartments, 0.0f, 0.13f, 0f), (Catalog.Cottage, 0.25f, 0.12f, 0f),
                (Catalog.Bakery, -0.35f, -0.12f, 180f), (Catalog.Cafe, -0.1f, -0.12f, 180f), (Catalog.Grocer, 0.15f, -0.12f, 180f), (Catalog.Park, 0.35f, 0.4f, 0f), (Catalog.School, 0.4f, -0.14f, 180f) })
                world.PlaceHouse(new HouseInstance { x = x, z = z, rotation = rot, variant = id });
            for (int k = 0; k < 14; k++) world.PlacePerson(t.PointAt(-0.4f + k * 0.06f, 0.03f), k % PersonFactory.StyleCount, 4100 + k);
            world.People.Grid.Complete();
            world.Clock.Hour = 7f;
            for (int k = 0; k < 72 * 240; k++) { world.Clock.Tick(Dt); world.People.Step(Dt); }
            var hand = new Vector3(0.1f, 1.2f, 0.4f);
            int pick = -1;
            for (int k = 0; k < world.People.People.Count && pick < 0; k++) if (!world.People.IsHidden(k) && world.People.HomeOf(k) >= 0) pick = k;
            if (pick < 0) pick = 0;
            world.Clock.Mode = ClockMode.Paused;
            var who = world.People.PositionOf(pick);
            var at = world.transform.TransformPoint(new Vector3(who.x, t.SurfaceAt(who.x, who.y), who.y));
            tools.SelectTool(Tool.Select);
            Frame(hand, at, false); Frame(hand, at, true); Frame(hand, at, false); Frame(hand, at, false);
            tools.ToggleSettings();
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.32f, 0.3f, 0.28f);
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 34f;
            cam.transform.position = palette.transform.position - palette.transform.forward * 0.62f;
            cam.transform.rotation = Quaternion.LookRotation(palette.transform.forward, palette.transform.up);
            cam.tag = "MainCamera";
            Frame(hand, at, false); Frame(hand, at, false);
            WonderlandRenders.Capture(cam, "editor-cards-person-stats.png", 2400, 1000);
            // A building, and Help.
            int bakery = world.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
            var b = world.Buildings.Houses[bakery];
            var top = world.transform.TransformPoint(new Vector3(b.x, world.Buildings.TopOf(bakery) - 0.01f, b.z));
            Frame(hand, top, false); Frame(hand, top, true); Frame(hand, top, false); Frame(hand, top, false);
            tools.ToggleHelp();
            WonderlandRenders.Capture(cam, "editor-cards-building-help.png", 2400, 1000);
            // The library: Build, the Town category, its Civic family; the town hall's tile pointed at.
            tools.CloseCards(); tools.ToggleHelp();
            tools.SelectTool(Tool.Houses);
            foreach (var label in new[] { "Town", "Civic" })
                foreach (var pb in palette.GetComponentsInChildren<PaletteButton>()) if (pb.Text == label) { pb.OnClick(); break; }
            foreach (var pb in palette.GetComponentsInChildren<PaletteButton>())
                if (pb.Text == "Town hall") { Physics.SyncTransforms(); Frame(hand, pb.transform.position, false); break; }
            for (int k = 0; k < 20; k++) palette.SendMessage("Update");
            cam.transform.position = palette.transform.position - palette.transform.forward * 0.62f;
            cam.transform.rotation = Quaternion.LookRotation(palette.transform.forward, palette.transform.up);
            WonderlandRenders.Capture(cam, "editor-library.png", 2400, 1000);
            tools.ToggleHelp();
            // The label at the pointer, seen from beside the hand: over the bakery in Select, and over a villager in Build.
            tools.CloseCards(); tools.ToggleHelp();
            cam.tag = "MainCamera";
            cam.fieldOfView = 60f;
            world.Sky.Shown = false;
            cam.transform.position = Vector3.Lerp(hand, top, 0.55f) + new Vector3(-0.03f, 0.05f, 0f);
            cam.transform.rotation = Quaternion.LookRotation(top + new Vector3(0.03f, 0.02f, 0f) - cam.transform.position);
            Frame(hand, top, false); Frame(hand, top, false);
            WonderlandRenders.Capture(cam, "editor-label-building.png", 1600, 1000);
            tools.SelectTool(Tool.Houses);
            Frame(hand, at, false); Frame(hand, at, false);
            cam.transform.position = Vector3.Lerp(hand, at, 0.55f) + new Vector3(-0.03f, 0.05f, 0f);
            cam.transform.rotation = Quaternion.LookRotation(at + new Vector3(0.03f, 0.02f, 0f) - cam.transform.position);
            Frame(hand, at, false); Frame(hand, at, false);
            WonderlandRenders.Capture(cam, "editor-label-villager.png", 1600, 1000);
            // The Track tool: free ends (blue) and switches (orange) ringed; pointing at a free end, the label says so.
            var line = new System.Collections.Generic.List<Vector3>();
            for (int k = 0; k <= 12; k++) line.Add(t.PointAt(-0.55f + k * 0.08f, -0.42f + 0.04f * Mathf.Sin(k * 0.5f)));
            var laid = world.BuildTrack(line);
            var branch = new System.Collections.Generic.List<Vector3>();
            for (int k = 0; k <= 6; k++) branch.Add(t.PointAt(-0.1f + k * 0.05f, -0.4f - k * k * 0.006f - k * 0.01f));
            var branched = world.BuildTrack(branch);
            Debug.Log($"Wonderland renders: track {laid.Ok} {laid.Error}, branch {branched.Ok} {branched.Error}, switches {world.Track.Network.Joints.Count}");
            tools.SelectTool(Tool.Track);
            var endW = world.transform.TransformPoint(line[^1]);
            cam.transform.position = endW + new Vector3(-0.25f, 0.35f, -0.25f);
            cam.transform.rotation = Quaternion.LookRotation(endW + new Vector3(-0.2f, 0f, 0f) - cam.transform.position);
            Frame(hand, endW, false); Frame(hand, endW, false);
            Debug.Log($"Wonderland renders: rail marks {tools.RailMarksShown}, label \"{tools.PointerLabel}\"");
            WonderlandRenders.Capture(cam, "editor-track-marks.png", 1600, 1000);
            Debug.Log("Card renders written");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
