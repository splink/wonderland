using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Paint, draw, place. Turns right-controller input into world edits for the selected tool, shows the pointer,
    /// brush ring, track preview and house ghost, and keeps the palette's options and hints in sync.
    /// Controls: trigger = use tool / press buttons; stick left-right = brush size (houses: rotate); stick up-down =
    /// strength or density; A = next mode (raise / lower / smooth / flatten, paint / erase, house type); B = undo;
    /// grip = pick up a house; left Y = hide or show the palette.
    /// </summary>
    public sealed class ToolController : MonoBehaviour
    {
        public WonderlandWorld World;
        public PaletteUI Palette;
        public Transform RightHand;
        public System.Func<PointerInput> InputOverride;   // tests / desktop
        public System.Action ResetRequested;

        public Tool Tool { get; private set; } = Tool.Terrain;
        public TerrainBrush TerrainMode { get; private set; } = TerrainBrush.Raise;
        public bool ForestErase { get; private set; }
        public int HouseVariant { get; private set; }
        public float Radius => radius[(int)Tool];
        public bool PlacingWorld;
        public bool HapticsEnabled = true;

        private readonly float[] radius = { 0.16f, 0.13f, 0.05f, 0.05f, 0.05f, 0.06f };
        private float strength = 0.6f, density = 0.6f, houseRotation;
        private readonly QuestInput quest = new();
        private LineRenderer line;
        private Transform dot;
        private Transform floorRing;
        private bool stroking;
        private float flattenTarget;
        private readonly List<Vector3> drawn = new();
        private float previewErrorUntil;
        private string message; private float messageUntil;
        private Transform ghost; private MeshFilter ghostFilter; private MeshRenderer ghostRenderer; private MaterialPropertyBlock ghostBlock;
        private bool carrying; private int pickedHover = -1;
        private bool resetArmed; private float resetUntil;
        private float hapticTick;

        public void Init(WonderlandWorld world, PaletteUI palette, Transform rightHand)
        {
            World = world; Palette = palette; RightHand = rightHand;
            palette.ToolSelected += SelectTool;
            line = new GameObject("Pointer").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = new Material(world.Materials.UI) { renderQueue = 3010 };
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.003f), new Keyframe(1f, 0.0015f));
            line.positionCount = 2; line.useWorldSpace = true; line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.numCapVertices = 2;
            dot = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            if (Application.isPlaying) Destroy(dot.GetComponent<Collider>()); else DestroyImmediate(dot.GetComponent<Collider>());
            dot.SetParent(transform, false);
            dot.localScale = Vector3.one * 0.008f;
            dot.GetComponent<MeshRenderer>().sharedMaterial = new Material(world.Materials.UI) { renderQueue = 3011 };
            dot.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            floorRing = CreateFloorRing(world.Materials.UI);
            floorRing.SetParent(transform, false);

            ghost = new GameObject("House ghost").transform;
            ghost.SetParent(transform, false);   // not under the world root: loading a world rebuilds the root's children
            ghostFilter = ghost.gameObject.AddComponent<MeshFilter>();
            ghostRenderer = ghost.gameObject.AddComponent<MeshRenderer>();
            ghostRenderer.sharedMaterial = world.Materials.Lit; ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ghostBlock = new MaterialPropertyBlock();
            ghost.gameObject.SetActive(false);
            SelectTool(Tool.Terrain);
        }

        public void SelectTool(Tool tool)
        {
            if (stroking) EndStroke();
            if (carrying) DropCarried();
            Tool = tool;
            Palette.SetTool(tool);
            drawn.Clear(); World.Track.HidePreview();
            RefreshOptions();
            Haptic(0.25f, 0.03f);
        }

        /// <summary>Ring + faint disc lying on the real floor: the brush where there is no landscape to draw it on.</summary>
        private static Transform CreateFloorRing(Material ui)
        {
            var b = new MeshBuilder();
            const int seg = 48;
            var ring = new Color(1f, 0.92f, 0.6f, 0.95f); var fill = new Color(1f, 0.92f, 0.6f, 0.12f);
            int centre = b.AddVertex(Vector3.zero, Vector3.up, fill);
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.AddVertex(d * 0.95f, Vector3.up, fill);
                b.AddVertex(d * 0.95f, Vector3.up, ring);
                b.AddVertex(d * 1.02f, Vector3.up, ring);
                if (i > 0)
                {
                    int v = 1 + (i - 1) * 3, n = v + 3;
                    b.AddTriangle(centre, v, n);
                    b.AddTriangle(v + 1, n + 1, n + 2); b.AddTriangle(v + 1, n + 2, v + 2);
                }
            }
            var go = new GameObject("Floor brush");
            go.AddComponent<MeshFilter>().sharedMesh = b.ToMesh("FloorRing");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(ui) { renderQueue = 3004 };
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        private bool LandMode => Tool == Tool.Terrain && (TerrainMode == TerrainBrush.Grow || TerrainMode == TerrainBrush.Trim);

        public void Say(string text, float seconds = 3f) { message = text; messageUntil = Time.time + seconds; }

        private void RefreshOptions()
        {
            var o = new List<PaletteUI.Option>();
            switch (Tool)
            {
                case Tool.Terrain:
                    foreach (TerrainBrush m in System.Enum.GetValues(typeof(TerrainBrush)))
                    {
                        var mode = m;
                        o.Add(new PaletteUI.Option(m.ToString(), TerrainMode == m, () => { TerrainMode = mode; RefreshOptions(); }));
                    }
                    break;
                case Tool.Forest:
                    o.Add(new PaletteUI.Option("Paint", !ForestErase, () => { ForestErase = false; RefreshOptions(); }));
                    o.Add(new PaletteUI.Option("Erase", ForestErase, () => { ForestErase = true; RefreshOptions(); }));
                    o.Add(new PaletteUI.Option("Sparse", density < 0.45f, () => { density = 0.3f; RefreshOptions(); }));
                    o.Add(new PaletteUI.Option("Dense", density > 0.75f, () => { density = 0.95f; RefreshOptions(); }));
                    break;
                case Tool.Track:
                    o.Add(new PaletteUI.Option("Draw", true, null));
                    o.Add(new PaletteUI.Option("Remove railway", false, () => { if (World.Track.HasTrack) { World.RemoveTrack(); Say("Railway removed"); } }, World.Track.HasTrack));
                    break;
                case Tool.Train:
                    o.Add(new PaletteUI.Option(World.Train.Running ? "Stop" : "Start", World.Train.Running, ToggleTrain, World.Train.Placed));
                    o.Add(new PaletteUI.Option("Remove train", false, () => { World.Checkpoint(); World.Train.Remove(); RefreshOptions(); }, World.Train.Placed));
                    break;
                case Tool.Houses:
                    for (int i = 0; i < HouseFactory.VariantCount; i++)
                    {
                        int v = i;
                        o.Add(new PaletteUI.Option(HouseFactory.Names[i], HouseVariant == i && !carrying, () => { HouseVariant = v; carrying = false; RefreshOptions(); }));
                    }
                    break;
                case Tool.Eraser:
                    o.Add(new PaletteUI.Option("Tap: house / train / track", false, null));
                    o.Add(new PaletteUI.Option("Hold: trees", false, null));
                    break;
            }
            Palette.SetOptions(o);
        }

        private void ToggleTrain()
        {
            if (!World.Train.Placed) { Say("Place the train on the railway first"); return; }
            World.SetTrainRunning(!World.Train.Running);
            Say(World.Train.Running ? "All aboard!" : "Train stopping");
            RefreshOptions();
        }

        private string Hint() => Tool switch
        {
            Tool.Terrain => TerrainMode switch
            {
                TerrainBrush.Grow => "Grow: paint over the floor to extend the land anywhere in the room  ·  A: mode",
                TerrainBrush.Trim => "Trim: paint along the shore to take land away  ·  A: mode",
                _ => $"Hold trigger to {TerrainMode.ToString().ToLower()}  ·  A: mode  ·  stick: size / strength  ·  B: undo",
            },
            Tool.Forest => ForestErase ? "Hold trigger to clear trees  ·  A: paint" : "Hold trigger to paint a forest  ·  A: erase  ·  stick: size / density",
            Tool.Track => World.Track.HasTrack ? "Draw a new loop to replace the railway  ·  end near the start to close it" : "Hold trigger and draw a loop; finish near the start",
            Tool.Train => World.Train.Placed ? "Trigger on the track: move train  ·  A: start / stop" : World.Track.HasTrack ? "Point at the railway and pull the trigger" : "Draw a railway first (Track tool)",
            Tool.Houses => carrying ? "Trigger: put the house down  ·  stick: rotate" : "Trigger: place  ·  stick: rotate  ·  A: next house  ·  grip / trigger on a house: move it",
            _ => "Tap a house, the train or the railway to erase  ·  hold over trees to clear them",
        };

        private void Update()
        {
            if (World == null || Palette == null) return;
            var input = InputOverride != null ? InputOverride() : quest.Read(RightHand);
            if (resetArmed && Time.time > resetUntil) resetArmed = false;
            Palette.SetStatus(Time.time < messageUntil ? message : "");
            Palette.SetHint(PlacingWorld ? "Point at the floor, stick to rotate, trigger to place the world here" : Hint());
            if (!input.Valid) { line.enabled = false; dot.gameObject.SetActive(false); return; }
            if (input.MenuDown) Palette.gameObject.SetActive(!Palette.gameObject.activeSelf);
            Process(input, Time.deltaTime);
        }

        /// <summary>One frame of tool logic (public so a scripted input source can drive it).</summary>
        public void Process(PointerInput input, float dt)
        {
            var ray = input.Ray;
            float rayLength = 2.5f;
            var colour = new Color(1f, 1f, 1f, 0.7f);

            // 1. The palette takes the ray first (unless a stroke is in progress).
            if (!stroking && drawn.Count == 0 && Palette.gameObject.activeInHierarchy)
            {
                var button = Palette.Raycast(ray, out float uiDistance);
                if (button != null || Palette.RayHitsPanel(ray, out uiDistance))
                {
                    World.TerrainView.HideBrush();
                    ghost.gameObject.SetActive(false);
                    if (button != null && input.TriggerDown && button.Enabled && button.OnClick != null) { button.OnClick(); Haptic(0.4f, 0.02f); }
                    DrawPointer(ray, uiDistance, new Color(1f, 0.9f, 0.6f, 0.9f), true);
                    return;
                }
            }
            else Palette.ClearHover();

            if (input.SecondaryDown && !stroking) { if (World.Undo()) { Say("Undone"); Haptic(0.3f, 0.03f); RefreshOptions(); } else Say("Nothing to undo"); }

            // 2. World-space hit.
            var wt = World.transform;
            var localOrigin = wt.InverseTransformPoint(ray.origin);
            var localDir = wt.InverseTransformDirection(ray.direction);
            bool hit = World.Terrain.Raycast(localOrigin, localDir, 3f, out var local);
            // Growing land: past the shore the pointer lands on the real floor.
            if (!hit && LandMode) hit = World.Terrain.RaycastFloor(localOrigin, localDir, 4f, out local);
            if (PlacingWorld) { PlaceWorld(input, ray, dt); return; }

            // Stick: size on X (rotation for houses), strength / density on Y.
            var stick = input.Stick;
            if (Mathf.Abs(stick.x) > 0.25f)
            {
                if (Tool == Tool.Houses) houseRotation += stick.x * 120f * dt;
                else radius[(int)Tool] = Mathf.Clamp(Radius * (1f + stick.x * 1.2f * dt), 0.04f, 0.4f);
            }
            if (Mathf.Abs(stick.y) > 0.4f)
            {
                if (Tool == Tool.Forest) { density = Mathf.Clamp01(density + stick.y * 0.6f * dt); }
                else strength = Mathf.Clamp(strength + stick.y * 0.6f * dt, 0.15f, 1f);
            }
            if (input.PrimaryDown) CycleMode();

            if (hit) { rayLength = Vector3.Distance(ray.origin, wt.TransformPoint(local)); }
            switch (Tool)
            {
                case Tool.Terrain: TerrainTool(input, hit, local, dt, ref colour); break;
                case Tool.Forest: ForestTool(input, hit, local, dt, ref colour); break;
                case Tool.Track: TrackTool(input, hit, local, ref colour); break;
                case Tool.Train: TrainTool(input, hit, local, ref colour); break;
                case Tool.Houses: HouseTool(input, hit, local, localOrigin, localDir, ref colour); break;
                case Tool.Eraser: EraserTool(input, hit, local, localOrigin, localDir, dt, ref colour); break;
            }
            if (Tool != Tool.Houses) ghost.gameObject.SetActive(false);
            floorRing.gameObject.SetActive(LandMode && hit);
            DrawPointer(ray, rayLength, colour, hit);
        }

        private void CycleMode()
        {
            switch (Tool)
            {
                case Tool.Terrain: TerrainMode = (TerrainBrush)(((int)TerrainMode + 1) % 6); Say(TerrainMode.ToString()); break;
                case Tool.Forest: ForestErase = !ForestErase; Say(ForestErase ? "Erase trees" : "Paint trees"); break;
                case Tool.Houses: HouseVariant = (HouseVariant + 1) % HouseFactory.VariantCount; Say(HouseFactory.Names[HouseVariant]); break;
                case Tool.Train: ToggleTrain(); break;
            }
            RefreshOptions();
            Haptic(0.3f, 0.02f);
        }

        private static Color ModeColour(TerrainBrush m) => m switch
        {
            TerrainBrush.Raise => new Color(0.75f, 1f, 0.45f),
            TerrainBrush.Lower => new Color(0.45f, 0.75f, 1f),
            TerrainBrush.Smooth => new Color(1f, 1f, 1f),
            TerrainBrush.Grow => new Color(1f, 0.92f, 0.55f),
            TerrainBrush.Trim => new Color(1f, 0.5f, 0.42f),
            _ => new Color(1f, 0.75f, 0.4f),
        };

        private void TerrainTool(PointerInput input, bool hit, Vector3 local, float dt, ref Color colour)
        {
            colour = ModeColour(TerrainMode); colour.a = 0.8f;
            if (LandMode)
            {
                World.TerrainView.HideBrush();
                if (hit)
                {
                    var wt = World.transform;
                    bool blocked = TerrainMode == TerrainBrush.Grow && !World.CanGrowLand(local);
                    floorRing.SetPositionAndRotation(wt.TransformPoint(local + Vector3.up * 0.004f), wt.rotation);
                    floorRing.localScale = Vector3.one * Radius * (stroking ? 1.04f : 1f);
                    floorRing.GetComponent<MeshRenderer>().sharedMaterial.color = blocked ? new Color(1f, 0.4f, 0.35f) : ModeColour(TerrainMode);
                    if (blocked && input.Trigger) Say("No room here - land grows only on free floor", 1.5f);
                }
                else if (input.Trigger) Say("The land can't grow any further this way", 1.5f);
            }
            else if (hit) World.TerrainView.SetBrush(local, Radius, ModeColour(TerrainMode), 0.55f + (stroking ? 0.35f : 0f));
            else World.TerrainView.HideBrush();
            if (input.TriggerDown && hit) { World.Checkpoint(); stroking = true; flattenTarget = local.y; }
            if (stroking && input.Trigger && hit)
            {
                World.TerrainStroke(TerrainMode, local, Radius, strength, dt, flattenTarget);
                Buzz(0.12f);
            }
            if (stroking && (input.TriggerUp || !input.Trigger)) EndStroke();
        }

        private void ForestTool(PointerInput input, bool hit, Vector3 local, float dt, ref Color colour)
        {
            var ring = ForestErase ? new Color(1f, 0.45f, 0.4f) : new Color(0.55f, 1f, 0.5f);
            colour = ring; colour.a = 0.8f;
            if (hit) World.TerrainView.SetBrush(local, Radius, ring, 0.6f + (stroking ? 0.3f : 0f));
            else World.TerrainView.HideBrush();
            if (input.TriggerDown && hit) { World.Checkpoint(); stroking = true; }
            if (stroking && input.Trigger && hit)
            {
                int n = ForestErase ? World.EraseForest(local, Radius, strength + 0.4f, dt) : World.PaintForest(local, Radius, density, dt);
                if (n > 0) Buzz(0.2f);
                if (!ForestErase && World.Forest.Trees.Count >= ForestSystem.MaxTrees) Say("The forest is full - erase some trees first");
            }
            if (stroking && (input.TriggerUp || !input.Trigger)) EndStroke();
        }

        private void EndStroke()
        {
            stroking = false;
            World.Buildings.Resit();
        }

        private void TrackTool(PointerInput input, bool hit, Vector3 local, ref Color colour)
        {
            colour = new Color(1f, 0.9f, 0.55f, 0.85f);
            World.TerrainView.SetBrush(local, 0.012f, new Color(1f, 0.9f, 0.5f), hit ? 0.9f : 0f);
            if (input.TriggerDown && hit) { drawn.Clear(); drawn.Add(local); }
            if (input.Trigger && drawn.Count > 0 && hit)
            {
                var last = drawn[^1];
                if (new Vector2(local.x - last.x, local.z - last.z).magnitude > 0.012f) drawn.Add(local);
                bool closing = TrackPlanner.WouldClose(drawn);
                World.Track.ShowPreview(drawn, closing, false);
                if (closing) Buzz(0.15f);
            }
            if ((input.TriggerUp || !input.Trigger) && drawn.Count > 0)
            {
                var plan = World.BuildTrack(drawn);
                if (plan.Ok)
                {
                    World.Track.HidePreview();
                    Say(plan.Layout.Closed ? $"Loop complete! {plan.Layout.Length:0.0} m of railway" : $"Railway laid ({plan.Layout.Length:0.0} m) - draw back to the start for a loop", 4f);
                    Haptic(0.6f, 0.08f);
                    RefreshOptions();
                }
                else
                {
                    World.Track.ShowPreview(plan.Smoothed ?? new List<Vector3>(drawn), false, true);
                    previewErrorUntil = Time.time + 1.6f;
                    Say(plan.Error, 4f);
                    Haptic(0.8f, 0.15f);
                }
                drawn.Clear();
            }
            if (drawn.Count == 0 && previewErrorUntil > 0f && Time.time > previewErrorUntil) { World.Track.HidePreview(); previewErrorUntil = 0f; }
        }

        private void TrainTool(PointerInput input, bool hit, Vector3 local, ref Color colour)
        {
            colour = new Color(1f, 0.6f, 0.5f, 0.85f);
            bool onTrack = hit && World.Track.HasTrack && World.Track.DistanceTo(local) < 0.08f;
            if (onTrack)
            {
                float s = World.Track.Layout.Nearest(local, out _);
                var p = World.Track.Layout.Evaluate(s, out _);
                World.TerrainView.SetBrush(p, 0.05f, new Color(1f, 0.85f, 0.4f), 0.8f);
            }
            else World.TerrainView.SetBrush(local, 0.03f, new Color(1f, 0.5f, 0.4f), hit ? 0.5f : 0f);
            if (input.TriggerDown && hit)
            {
                string error = World.PlaceTrain(local);
                if (error != null) { Say(error); Haptic(0.7f, 0.1f); }
                else { Say("Train on the track - press Start (or A)"); Haptic(0.5f, 0.05f); }
                RefreshOptions();
            }
        }

        private void HouseTool(PointerInput input, bool hit, Vector3 local, Vector3 localOrigin, Vector3 localDir, ref Color colour)
        {
            colour = new Color(1f, 0.85f, 0.6f, 0.85f);
            if (!carrying)
            {
                int hover = World.Buildings.Pick(localOrigin, localDir);
                if (hover != pickedHover) { World.Buildings.Highlight(hover, new Color(1f, 0.9f, 0.5f, 0.25f)); pickedHover = hover; }
                if (hover >= 0)
                {
                    ghost.gameObject.SetActive(false);
                    World.TerrainView.HideBrush();
                    if (input.TriggerDown || input.GripDown)
                    {
                        var h = World.PickUpHouse(hover);
                        HouseVariant = h.variant; houseRotation = h.rotation; carrying = true; pickedHover = -1;
                        Say("Moving " + HouseFactory.Names[h.variant]);
                        RefreshOptions();
                    }
                    return;
                }
            }
            if (!hit) { ghost.gameObject.SetActive(false); World.TerrainView.HideBrush(); return; }
            var candidate = new HouseInstance { x = local.x, z = local.z, rotation = houseRotation, variant = HouseVariant };
            string error = World.ValidateHouse(candidate);
            ghost.gameObject.SetActive(true);
            ghostFilter.sharedMesh = HouseFactory.Get(HouseVariant);
            var wt = World.transform;
            ghost.position = wt.TransformPoint(new Vector3(local.x, World.Buildings.SitHeight(candidate, out _) + 0.004f * Mathf.Sin(Time.time * 6f) + 0.004f, local.z));
            ghost.rotation = wt.rotation * Quaternion.Euler(0f, houseRotation, 0f);
            ghostBlock.SetColor("_Highlight", error == null ? new Color(0.6f, 1f, 0.5f, 0.18f) : new Color(1f, 0.3f, 0.25f, 0.45f));
            ghostRenderer.SetPropertyBlock(ghostBlock);
            World.TerrainView.SetBrush(local, BuildingSystem.Radius(HouseVariant) * 0.9f, error == null ? new Color(0.6f, 1f, 0.5f) : new Color(1f, 0.4f, 0.35f), 0.6f);
            if (input.TriggerDown || (carrying && input.GripUp))
            {
                if (error == null)
                {
                    World.PlaceHouse(candidate, !carrying);
                    carrying = false;
                    Haptic(0.5f, 0.05f);
                    Say(HouseFactory.Names[HouseVariant] + " placed");
                    RefreshOptions();
                }
                else { Say(error); Haptic(0.7f, 0.1f); }
            }
        }

        private void DropCarried()
        {
            // Switching tools while carrying: undo the pick-up so the house returns to where it was.
            carrying = false;
            World.Undo();
        }

        private void EraserTool(PointerInput input, bool hit, Vector3 local, Vector3 localOrigin, Vector3 localDir, float dt, ref Color colour)
        {
            colour = new Color(1f, 0.45f, 0.45f, 0.85f);
            int house = World.Buildings.Pick(localOrigin, localDir);
            if (house != pickedHover) { World.Buildings.Highlight(house, new Color(1f, 0.25f, 0.2f, 0.45f)); pickedHover = house; }
            bool target = house >= 0 || (hit && (World.Train.Near(local, 0.05f) || World.Track.DistanceTo(local) < 0.045f));
            if (hit) World.TerrainView.SetBrush(local, target ? 0.03f : Radius, new Color(1f, 0.4f, 0.35f), stroking ? 0.9f : 0.6f);
            else World.TerrainView.HideBrush();
            if (input.TriggerDown && (hit || house >= 0))
            {
                string what = World.EraseAt(localOrigin, localDir, local);
                if (what != null) { Say(what); Haptic(0.6f, 0.06f); pickedHover = -1; RefreshOptions(); return; }
                World.Checkpoint(); stroking = true;
            }
            if (stroking && input.Trigger && hit) { if (World.EraseForest(local, Radius, 1.2f, dt) > 0) Buzz(0.2f); }
            if (stroking && (input.TriggerUp || !input.Trigger)) stroking = false;
        }

        private void PlaceWorld(PointerInput input, Ray ray, float dt)
        {
            World.TerrainView.HideBrush();
            float floorY = World.transform.position.y;
            if (Mathf.Abs(ray.direction.y) > 1e-3f)
            {
                float t = (floorY - ray.origin.y) / ray.direction.y;
                if (t > 0f && t < 6f)
                {
                    var p = ray.origin + ray.direction * t;
                    World.transform.position = new Vector3(p.x, floorY, p.z);
                    DrawPointer(ray, t, new Color(0.6f, 0.9f, 1f, 0.9f), true);
                }
            }
            if (Mathf.Abs(input.Stick.x) > 0.25f) World.transform.Rotate(0f, input.Stick.x * 60f * dt, 0f, Space.World);
            if (input.TriggerDown) { PlacingWorld = false; World.FitToRoom(); Say("World placed"); Haptic(0.5f, 0.06f); }
        }

        public void RequestReset()
        {
            if (resetArmed) { resetArmed = false; ResetRequested?.Invoke(); return; }
            resetArmed = true; resetUntil = Time.time + 3f;
            Say("Press New again to start a fresh world", 3f);
        }

        private void Haptic(float amplitude, float seconds)
        {
            if (HapticsEnabled) quest.Haptic(amplitude, seconds);
        }

        private void Buzz(float amplitude)
        {
            hapticTick += Time.deltaTime > 0f ? Time.deltaTime : 1f / 72f;
            if (hapticTick > 0.08f) { hapticTick = 0f; Haptic(amplitude, 0.02f); }
        }

        private void DrawPointer(Ray ray, float length, Color colour, bool showDot)
        {
            line.enabled = true;
            var end = ray.origin + ray.direction * length;
            line.SetPosition(0, ray.origin + ray.direction * 0.02f);
            line.SetPosition(1, end);
            var faded = colour; faded.a *= 0.15f;
            line.startColor = faded; line.endColor = colour;
            dot.gameObject.SetActive(showDot);
            dot.position = end;
            dot.GetComponent<MeshRenderer>().sharedMaterial.color = colour;
        }
    }
}
