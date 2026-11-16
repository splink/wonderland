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
            var hand = new Vector3(0.2f, 1.1f, 0.25f);

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
            Check(world.Buildings.Houses.Count == 1 && Mathf.Abs(world.Buildings.Houses[0].rotation) > 10f, "Stick rotated the house before placing");
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
                if (world.ValidateHouse(new HouseInstance { x = c.x, z = c.y, variant = Catalog.Fountain }) == null) { free = c; break; }
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
        private static void Frame(Vector3 hand, Vector3 target, bool trigger, Vector2 stick = default, bool primary = false, bool secondary = false)
        {
            Physics.SyncTransforms();
            var input = new PointerInput
            {
                Valid = true, Ray = new Ray(hand, (target - hand).normalized),
                Trigger = trigger, TriggerDown = trigger && !triggerHeld, TriggerUp = !trigger && triggerHeld,
                Stick = stick, PrimaryDown = primary, SecondaryDown = secondary,
            };
            triggerHeld = trigger;
            tools.Process(input, Dt);
            world.Forest.RebuildDirty();
        }
    }
}
