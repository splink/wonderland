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

            // Palette: click the Forest tile, paint.
            ClickButton(hand, "Forest");
            Check(tools.Tool == Tool.Forest, "Clicking the Forest tile selects the forest brush");
            HoldPath(hand, new Vector3(0.35f, 0f, 0.4f), new Vector3(0.6f, 0f, 0.0f), 2.5f);
            Check(world.Forest.Trees.Count > 30, $"Painting with the trigger grows trees ({world.Forest.Trees.Count})");

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
            Check(world.Track.HasTrack && world.Track.Layout.Closed, "Drawing a loop with the trigger lays a closed railway" + (world.Track.HasTrack ? $" ({world.Track.Layout.Length:0.00} m)" : ""));

            // Train: click the tile, trigger on the track, Start option.
            ClickButton(hand, "Train");
            var onTrack = world.transform.TransformPoint(world.Track.Layout.Points[world.Track.Layout.Points.Count / 4]);
            Frame(hand, onTrack, true); Frame(hand, onTrack, false);
            Check(world.Train.Placed, "Trigger on the railway places the train");
            ClickButton(hand, "Start");
            Check(world.Train.Running, "Start option sets the train running");
            for (int i = 0; i < 72 * 3; i++) world.Train.Step(Dt);
            Check(world.Train.Odometer > 0.2f, $"Train moves ({world.Train.Odometer:0.00} m)");

            // Houses: tile, aim inside the loop, trigger; stick rotates.
            ClickButton(hand, "Houses");
            var spot = world.transform.TransformPoint(new Vector3(-0.05f, world.Terrain.SurfaceAt(-0.05f, -0.1f), -0.1f));
            for (int i = 0; i < 20; i++) Frame(hand, spot, false, stick: new Vector2(1f, 0f));
            Frame(hand, spot, true); Frame(hand, spot, false);
            Check(world.Buildings.Houses.Count == 1, "Trigger places a house where the pointer is");
            Check(world.Buildings.Houses.Count == 1 && Mathf.Abs(world.Buildings.Houses[0].rotation) > 10f, "Stick rotated the house before placing");
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
        }

        private static void ClickButton(Vector3 hand, string label)
        {
            var button = Find(label);
            if (button == null) { Check(false, $"Button '{label}' exists"); return; }
            var p = button.transform.position;
            Frame(hand, p, false); Frame(hand, p, true); Frame(hand, p, false);
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
