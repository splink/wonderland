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
        private static bool triggerHeld;

        public static void Run()
        {
            Report.Clear(); failures = 0;
            try { Journey(); }
            catch (Exception e) { Check(false, "Exception: " + e); }
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
            palette.AddGlobal("Undo", () => world.Undo());
            // Named worlds, kept in a scratch folder for the test.
            WonderlandWorld.SlotFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wonderland-slots-test");
            if (System.IO.Directory.Exists(WonderlandWorld.SlotFolder)) System.IO.Directory.Delete(WonderlandWorld.SlotFolder, true);
            System.IO.Directory.CreateDirectory(WonderlandWorld.SlotFolder);
            int keepSlot = WonderlandWorld.CurrentSlot;
            tools.SaveWorld = n => world.SaveSlot(n);
            tools.LoadWorld = n => world.LoadSlot(n, WonderlandJourney.Materials());
            palette.AddGlobal("Save", () => tools.ShowWorlds(true));
            palette.AddGlobal("Load", () => tools.ShowWorlds(false));
            palette.AddGlobal("Redo", () => world.Redo());
            palette.AddGlobal("Help", tools.ToggleHelp);
            palette.AddGlobal("Stats", tools.ToggleStats);
            palette.AddGlobal("Tips", tools.ToggleTips);
            bool keepTips = ToolController.TipsOn;
            var hand = new Vector3(0.2f, 1.1f, 0.25f);
            // Tips: on, a first tip appears in the banner above the palette after a moment; the Tips button turns them off.
            if (!ToolController.TipsOn) tools.ToggleTips(); else { tools.ToggleTips(); tools.ToggleTips(); }   // (on, and starting over)
            for (int i = 0; i < 72 * 6; i++) Frame(hand, hand + Vector3.forward, false);
            string firstTip = palette.TipText;
            ClickButton(hand, "Tips");
            for (int i = 0; i < 3; i++) Frame(hand, hand + Vector3.forward, false);
            Check(!string.IsNullOrEmpty(firstTip) && !ToolController.TipsOn && string.IsNullOrEmpty(palette.TipText), $"A tip appears above the palette (\"{firstTip}\"), and the Tips button turns tips off");
            if (keepTips != ToolController.TipsOn) tools.ToggleTips();

            // Terrain: the default tool. Aim at the landscape and hold the trigger.
            Check(tools.Tool == Tool.Terrain, "Starts with the Terrain tool");
            var target = world.transform.TransformPoint(new Vector3(-0.2f, 0f, 0.2f));
            float before = world.Terrain.SurfaceAt(-0.2f, 0.2f);
            HoldAt(hand, () => world.transform.TransformPoint(new Vector3(-0.2f, world.Terrain.SurfaceAt(-0.2f, 0.2f), 0.2f)), 2.5f);
            float after = world.Terrain.SurfaceAt(-0.2f, 0.2f);
            Check(after > before + 0.05f, $"Trigger + aim raises a hill ({before * 100f:0.0} -> {after * 100f:0.0} cm)");
            Check(world.UndoDepth == 1, "One stroke = one undo step");

            // Stick right grows the brush.
            float radius = tools.Radius;
            for (int i = 0; i < 40; i++) Frame(hand, target, false, stick: new Vector2(1f, 0f));
            Check(tools.Radius > radius * 1.3f, $"Stick right enlarges the brush ({radius * 100f:0} -> {tools.Radius * 100f:0} cm)");

            // A cycles the sculpt mode.
            Frame(hand, target, false, primary: true);
            Check(tools.TerrainMode == TerrainBrush.Lower, "A switches Raise -> Lower");

            // Grow: pick the mode chip, aim past the shore at the floor, hold the trigger.
            ClickButton(hand, "Grow");
            Check(tools.TerrainMode == TerrainBrush.Grow, "Grow chip selects the land brush");
            float landBefore = world.Terrain.LandArea();
            var floor = world.transform.TransformPoint(new Vector3(1.15f, 0f, -0.2f));
            HoldAt(hand, () => floor, 1.5f);
            Check(world.Terrain.EdgeAt(1.15f, -0.2f) > 0.1f && world.Terrain.LandArea() > landBefore, $"Aiming at the floor beyond the shore grows land ({landBefore:0.00} -> {world.Terrain.LandArea():0.00} m2)");
            ClickButton(hand, "Raise");

            // Palette: click the Forest tile, paint.
            ClickButton(hand, "Forest");
            Check(tools.Tool == Tool.Forest, "Clicking the Forest tile selects the forest brush");
            HoldPath(hand, new Vector3(0.35f, 0f, 0.4f), new Vector3(0.6f, 0f, 0.0f), 2.5f);
            Check(world.Forest.Trees.Count > 20, $"Painting with the trigger grows trees ({world.Forest.Trees.Count})");

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
                ClickButton(hand, "+ Carriage");
                int more = TrainSystem.CarsOf(tr);
                ClickButton(hand, "- Carriage");
                Check(more == cars + 1 && TrainSystem.CarsOf(tr) == cars, $"Point at a train, then + / - Carriage couple and uncouple a carriage ({cars} -> {more} -> {TrainSystem.CarsOf(tr)})");
                var engine = world.Trains.LocoPosition(tr); int dir = tr.Dir;
                ClickButton(hand, "Turn round");
                float shift = (world.Trains.LocoPosition(tr) - engine).magnitude;
                Check(shift > 0.05f && tr.Dir == -dir, $"Turn round moves the engine to the other end of its train ({shift * 100f:0} cm)");
            }
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
            Check(world.Buildings.Houses.Count == 1 && Mathf.Abs(world.Buildings.Houses[0].rotation - 15f) < 0.01f, $"A flick of the stick turns the house 15 degrees before placing ({(world.Buildings.Houses.Count > 0 ? world.Buildings.Houses[0].rotation : 0f):0}°)");
            ClickButton(hand, "Thatch");
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

            // Build categories: Fun shows its items; placing a fountain from Town.
            ClickButton(hand, "Build");
            ClickButton(hand, "Town");
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
            ClickButton(hand, "Paths");
            ClickButton(hand, "Path");
            HoldPath(hand, new Vector3(-0.3f, 0f, -0.02f), new Vector3(-0.05f, 0f, 0.05f), 1f);
            Check(world.Paths.Lines.Count == 1 && world.Paths.Lines[0].kind == (int)PathKind.Footpath, $"Drawing with the Paths tool lays a footpath ({world.Paths.Lines.Count})");
            ClickButton(hand, "Fence");
            HoldPath(hand, new Vector3(-0.3f, 0f, -0.2f), new Vector3(-0.1f, 0f, -0.25f), 1f);
            Check(world.Paths.Lines.Count == 2 && world.Paths.Lines[1].kind == (int)PathKind.Fence, "...and a fence");

            // Life: a hen on the grass.
            ClickButton(hand, "Life");
            ClickButton(hand, "Hen");
            var yard = world.transform.TransformPoint(new Vector3(0.05f, world.Terrain.SurfaceAt(0.05f, 0.05f), 0.05f));
            int animals = world.Animals.Animals.Count;
            Frame(hand, yard, true); Frame(hand, yard, false);
            Check(world.Animals.Animals.Count == animals + 1 && world.Animals.Animals[^1].kind == (int)AnimalKind.Chicken, "The Life tool's chips place a hen");
            // ...and set the pace of the village clock.
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
            ClickButton(hand, "Fête");
            bool on = world.Fete != null;
            ClickButton(hand, "Fête");
            Check(fountain == null && on && world.Fete == null, "The Life tool's Fête chip calls the village to the fountain, and calls it off" + (fountain == null ? "" : $" (\"{fountain}\")"));
            // ...the clouds come and go.
            bool clouds = world.Sky.Shown;
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
            ClickButton(hand, "Watch");
            var feed = palette.transform.Find("Viewer");
            bool watching = tools.Tool == Tool.Watch && world.Cctv.Watching && feed != null && feed.gameObject.activeSelf && world.Cctv.Count >= 1;
            float yaw = world.Cctv.Count > 0 ? world.Cctv.AimOf(0).yaw : 0f;
            var aimAt = world.transform.TransformPoint(new Vector3(-0.3f, world.Terrain.SurfaceAt(-0.3f, 0.3f), 0.3f));
            Frame(hand, aimAt, true); Frame(hand, aimAt, false);
            bool aimed = world.Cctv.Count > 0 && Mathf.Abs(Mathf.DeltaAngle(world.Cctv.AimOf(0).yaw, yaw)) > 5f;
            // The stick turns and tilts the camera being watched.
            if (world.Cctv.Count > 0)
            {
                world.Cctv.Selected = 0;
                var aim0 = world.Cctv.AimOf(0);
                for (int i = 0; i < 72; i++) Frame(hand, aimAt + Vector3.up * 0.5f, false, stick: new Vector2(1f, 0f));
                for (int i = 0; i < 36; i++) Frame(hand, aimAt + Vector3.up * 0.5f, false, stick: new Vector2(0f, -1f));
                var aim1 = world.Cctv.AimOf(0);
                Check(Mathf.Abs(Mathf.DeltaAngle(aim1.yaw, aim0.yaw)) > 40f && aim1.pitch > aim0.pitch + 5f, $"The stick turns and tilts the watched camera (yaw {aim0.yaw:0}° -> {aim1.yaw:0}°, tilt {aim0.pitch:0}° -> {aim1.pitch:0}°)");
            }
            ClickButton(hand, "Life");
            Check(camera == null && watching && aimed && !world.Cctv.Watching && !feed.gameObject.activeSelf,
                "The Watch tool shows the camera's live feed above the palette, aims it where you point, and puts it away again" + (camera == null ? "" : $" (\"{camera}\")")
                + $" [watching {watching}, aimed {aimed}, cameras {world.Cctv.Count}, tool {tools.Tool}, feed {(feed != null ? feed.gameObject.activeSelf.ToString() : "none")}]");

            // Several worlds: save this one as World 2, change it, save that as World 3, and load World 2 back.
            int houses = world.Buildings.Houses.Count;
            ClickButton(hand, "Save"); ClickButton(hand, "World 2");
            bool saved2 = WonderlandWorld.SlotUsed(2) && WonderlandWorld.CurrentSlot == 2;
            string extra = null;
            foreach (var at in new[] { new Vector2(-0.3f, -0.35f), new Vector2(0.35f, -0.3f), new Vector2(-0.35f, 0.35f) })
                if ((extra = world.PlaceHouse(new HouseInstance { x = at.x, z = at.y, variant = Catalog.Bench })) == null) break;
            ClickButton(hand, "Save"); ClickButton(hand, "World 3");
            bool saved3 = WonderlandWorld.SlotUsed(3) && WonderlandWorld.CurrentSlot == 3 && world.Buildings.Houses.Count == houses + 1;
            ClickButton(hand, "Load"); ClickButton(hand, "World 2");
            bool back = WonderlandWorld.CurrentSlot == 2 && world.Buildings.Houses.Count == houses;
            Check(extra == null && saved2 && saved3 && back, $"Save and Load keep several worlds: saved as World 2 and World 3, and World 2 loads back as it was ({world.Buildings.Houses.Count} buildings)");
            WonderlandWorld.CurrentSlot = keepSlot;
            WonderlandWorld.SlotFolder = null;

            // Help and Stats open beside the palette and close again; the stats are the world's real numbers.
            ClickButton(hand, "Help");
            var infoPanel = palette.transform.Find("Info");
            bool helpOpen = infoPanel != null && infoPanel.gameObject.activeSelf && palette.InfoShown;
            ClickButton(hand, "Stats");
            string stats = tools.StatsText();
            bool statsTrue = palette.InfoShown && stats.Contains($"{world.People.People.Count} people") && stats.Contains($"{world.Buildings.Houses.Count} buildings");
            ClickButton(hand, "Stats");
            Check(helpOpen && statsTrue && !palette.InfoShown, "Help and Stats open beside the palette (with the world's own numbers) and close again");

            // The card: trigger on a villager with the Life tool opens theirs, and follows them.
            ClickButton(hand, "Life");
            if (world.People.People.Count == 0) world.PlacePerson(new Vector3(0.1f, 0f, 0.1f), 0, 77);
            int pick = 0;
            for (int i = 0; i < world.People.People.Count; i++) if (!world.People.IsHidden(i)) { pick = i; break; }
            world.Clock.Mode = ClockMode.Paused;
            var who = world.People.PositionOf(pick);
            var atWho = world.transform.TransformPoint(new Vector3(who.x, world.Terrain.SurfaceAt(who.x, who.y), who.y));
            int count = world.People.People.Count;
            Frame(hand, atWho, true); Frame(hand, atWho, false);
            var cardPanel = palette.transform.Find("Card");
            Check(palette.CardShown && world.People.People.Count == count && cardPanel != null, $"Trigger on a villager opens their card instead of placing another ({world.People.NameOf(pick)})" + (palette.CardShown ? "" : $" [following {tools.Following}, hover {tools.HoverPerson}, tool {tools.Tool}, people {count}->{world.People.People.Count}, hidden {world.People.IsHidden(pick)}]"));
            // Any tool: pointing at someone names them; grip opens their card.
            palette.HideCard();
            ClickButton(hand, "Build");
            Frame(hand, atWho, false);
            string named = palette.HintText;
            Frame(hand, atWho, false, grip: true); Frame(hand, atWho, false);
            Check(palette.CardShown && named.Contains(world.People.NameOf(pick)) && tools.Tool == Tool.Houses, $"With any tool, pointing at a villager names them and grip opens their card (\"{named}\")");
            // A building's card: grip on it (here with the Build tool's neighbour, the Paths tool).
            ClickButton(hand, "Paths");
            int someHouse = world.Buildings.Houses.Count - 1;
            if (someHouse >= 0)
            {
                var hh = world.Buildings.Houses[someHouse];
                var roof = world.transform.TransformPoint(new Vector3(hh.x, world.Buildings.TopOf(someHouse) - 0.01f, hh.z));
                Frame(hand, roof, false); Frame(hand, roof, false, grip: true); Frame(hand, roof, false);
                Check(palette.CardShown && palette.CardTitle == Catalog.Get(hh.variant).Name, $"Grip on a building opens its card (\"{palette.CardTitle}\")");
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
        }

        private static void ClickButton(Vector3 hand, string label)
        {
            var button = Find(label);
            if (button == null) { Check(false, $"Button '{label}' exists"); return; }
            Physics.SyncTransforms();
            var p = button.transform.position;
            Frame(hand, p, false); Frame(hand, p, true); Frame(hand, p, false);
            if (palette.Hovered != null && palette.Hovered != button) Debug.Log($"Click on '{label}' hit '{palette.Hovered.Text}' instead");
        }

        private static PaletteButton Find(string label)
        {
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
        private static void Frame(Vector3 hand, Vector3 target, bool trigger, Vector2 stick = default, bool primary = false, bool secondary = false, bool grip = false)
        {
            Physics.SyncTransforms();
            var input = new PointerInput
            {
                Valid = true, Ray = new Ray(hand, (target - hand).normalized),
                Trigger = trigger, TriggerDown = trigger && !triggerHeld, TriggerUp = !trigger && triggerHeld,
                Stick = stick, PrimaryDown = primary, SecondaryDown = secondary, Grip = grip, GripDown = grip,
            };
            triggerHeld = trigger;
            tools.Process(input, Dt);
            world.Forest.RebuildDirty();
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
            foreach (var g in new[] { "Undo", "Redo", "Save", "Load", "Move", "New", "Sound" }) palette.AddGlobal(g, null);
            palette.AddGlobal("Help", tools.ToggleHelp);
            palette.AddGlobal("Stats", tools.ToggleStats);
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
            Frame(hand, at, false); Frame(hand, at, false, grip: true); Frame(hand, at, false); Frame(hand, at, false);
            tools.ToggleStats();
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.32f, 0.3f, 0.28f);
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 34f;
            cam.transform.position = palette.transform.position - palette.transform.forward * 0.62f;
            cam.transform.rotation = Quaternion.LookRotation(palette.transform.forward, palette.transform.up);
            WonderlandRenders.Capture(cam, "editor-cards-person-stats.png", 2400, 1000);
            // A building, and Help.
            int bakery = world.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
            var b = world.Buildings.Houses[bakery];
            var top = world.transform.TransformPoint(new Vector3(b.x, world.Buildings.TopOf(bakery) - 0.01f, b.z));
            Frame(hand, top, false); Frame(hand, top, false, grip: true); Frame(hand, top, false); Frame(hand, top, false);
            tools.ToggleHelp();
            WonderlandRenders.Capture(cam, "editor-cards-building-help.png", 2400, 1000);
            Debug.Log("Card renders written");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
