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

        private readonly float[] radius = { 0.16f, 0.12f, 0.13f, 0.03f, 0.05f, 0.05f, 0.05f, 0.03f, 0.06f };
        public const int Villager = AnimalFactory.KindCount;
        public int DecorKind { get; private set; } = Villager;   // an AnimalKind, or Villager
        public PathKind PathKind { get; private set; } = PathKind.Road;
        public PlaceCategory BuildCategory { get; private set; } = PlaceCategory.Homes;
        public bool SignalMode { get; private set; }  // Track tool: placing stop signals instead of drawing
        public TrainKind TrainKind { get; private set; } = TrainKind.Passenger;
        private Train hoveredTrain;
        private Train carriedTrain; private Vector3 carriedFacing;
        private float strokeStarted;
        private int dragEdge = -1, dragIndex; private float dragPreviewAt; private string dragError;
        private Vector3 lastDrop; private float sinceDrop;
        public bool Draining { get; private set; }
        public bool StreamMode { get; private set; }
        private float waterLevel;
        private float strength = 0.6f, density = 0.6f, houseRotation;
        private int houseStyle;   // each new house gets the next colour style, so a village never repeats itself
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
            if (carriedTrain != null) { carriedTrain = null; World.Undo(); }
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
        public string LastMessage => message;

        private void RefreshOptions()
        {
            var o = new List<PaletteUI.Option>();
            var items = new List<PaletteUI.Option>();
            switch (Tool)
            {
                case Tool.Terrain:
                    foreach (TerrainBrush m in System.Enum.GetValues(typeof(TerrainBrush)))
                    {
                        var mode = m;
                        o.Add(new PaletteUI.Option(m.ToString(), TerrainMode == m, () => { TerrainMode = mode; RefreshOptions(); }));
                    }
                    break;
                case Tool.Water:
                    o.Add(new PaletteUI.Option("Pond", !Draining && !StreamMode, () => { Draining = false; StreamMode = false; RefreshOptions(); }));
                    o.Add(new PaletteUI.Option("Stream", StreamMode, () => { Draining = false; StreamMode = true; RefreshOptions(); }));
                    o.Add(new PaletteUI.Option("Drain", Draining, () => { Draining = true; StreamMode = false; RefreshOptions(); }));
                    break;
                case Tool.Forest:
                    o.Add(new PaletteUI.Option("Paint", !ForestErase, () => { ForestErase = false; RefreshOptions(); }));
                    o.Add(new PaletteUI.Option("Erase", ForestErase, () => { ForestErase = true; RefreshOptions(); }));
                    o.Add(new PaletteUI.Option("Sparse", density < 0.45f, () => { density = 0.3f; RefreshOptions(); }));
                    o.Add(new PaletteUI.Option("Dense", density > 0.75f, () => { density = 0.95f; RefreshOptions(); }));
                    break;
                case Tool.Track:
                    o.Add(new PaletteUI.Option("Draw", !SignalMode, () => { SignalMode = false; RefreshOptions(); }));
                    o.Add(new PaletteUI.Option("Signal", SignalMode, () => { SignalMode = true; RefreshOptions(); }));
                    o.Add(new PaletteUI.Option("Remove all", false, () => { if (World.Track.HasTrack) { World.RemoveTrack(); Say("Railway removed"); RefreshOptions(); } }, World.Track.HasTrack));
                    break;
                case Tool.Train:
                    foreach (var (kind, label) in new[] { (TrainKind.Passenger, "Steam"), (TrainKind.Goods, "Goods"), (TrainKind.Railcar, "Railcar") })
                    {
                        var k = kind;
                        o.Add(new PaletteUI.Option(label, TrainKind == k, () => { TrainKind = k; RefreshOptions(); }));
                    }
                    bool running = World.Trains.AnyRunning;
                    o.Add(new PaletteUI.Option(running ? "Stop all" : "Start all", running, () => { ToggleAll(); }, World.Trains.Any));
                    o.Add(new PaletteUI.Option("Switches: " + World.Trains.Mode, false, () =>
                    {
                        World.Trains.Mode = (SwitchMode)(((int)World.Trains.Mode + 1) % 3);
                        Say(World.Trains.Mode switch
                        {
                            SwitchMode.Explore => "Switches alternate - trains explore everywhere",
                            SwitchMode.Random => "Switches pick a way at random",
                            _ => "Switches stay as you set them",
                        });
                        RefreshOptions();
                    }));
                    break;
                case Tool.Houses:
                    for (int c = 0; c < Catalog.CategoryNames.Length; c++)
                    {
                        var cat = (PlaceCategory)c;
                        o.Add(new PaletteUI.Option(Catalog.CategoryNames[c], BuildCategory == cat, () => SelectCategory(cat)));
                    }
                    foreach (int id in Catalog.InCategory(BuildCategory))
                    {
                        int v = id;
                        items.Add(new PaletteUI.Option(Catalog.Get(id).Short, HouseVariant == id && !carrying, () => { HouseVariant = v; carrying = false; houseStyle %= Mathf.Max(1, Catalog.Get(v).Styles); RefreshOptions(); }));
                    }
                    break;
                case Tool.Decor:
                    o.Add(new PaletteUI.Option("People", DecorKind == Villager, () => { DecorKind = Villager; RefreshOptions(); }));
                    for (int k = 0; k < AnimalFactory.KindCount; k++)
                    {
                        int kind = k;
                        o.Add(new PaletteUI.Option(AnimalFactory.Names[k], DecorKind == k, () => { DecorKind = kind; RefreshOptions(); }));
                    }
                    break;
                case Tool.Paths:
                    for (int k = 0; k < PathSystem.Names.Length; k++)
                    {
                        var kind = (PathKind)k;
                        o.Add(new PaletteUI.Option(PathSystem.Names[k], PathKind == kind, () => { PathKind = kind; RefreshOptions(); }));
                    }
                    break;
                case Tool.Eraser:
                    o.Add(new PaletteUI.Option("Tap: anything you built", false, null));
                    o.Add(new PaletteUI.Option("Hold: trees", false, null));
                    break;
            }
            Palette.SetOptions(o);
            Palette.SetItems(items);
        }

        /// <summary>A: start / stop the train being pointed at, or all trains.</summary>
        private void ToggleTrain()
        {
            if (!World.Trains.Any) { Say(World.Track.HasTrack ? "Tap the railway to put a train on it" : "Draw a railway first (Track tool)"); return; }
            if (hoveredTrain != null && World.Trains.Trains.Contains(hoveredTrain))
            {
                hoveredTrain.Running = !hoveredTrain.Running;
                Say(hoveredTrain.Running ? "All aboard!" : "Train stopping");
            }
            else ToggleAll();
            RefreshOptions();
        }

        private void ToggleAll()
        {
            bool run = !World.Trains.AnyRunning;
            World.SetTrainsRunning(run);
            Say(run ? "All aboard!" : "All trains stopping");
            RefreshOptions();
        }

        private void SelectCategory(PlaceCategory c)
        {
            BuildCategory = c;
            var ids = Catalog.InCategory(c);
            if (!ids.Contains(HouseVariant)) HouseVariant = ids[0];
            carrying = false;
            RefreshOptions();
        }

        private string Hint() => Tool switch
        {
            Tool.Terrain => TerrainMode switch
            {
                TerrainBrush.Grow => "Grow: paint over the floor to extend the land anywhere in the room  ·  A: mode",
                TerrainBrush.Peaks => "Peaks: raise craggy mountains - high tops get rock and snow  ·  A: mode",
                TerrainBrush.Trim => "Trim: paint along the shore to take land away  ·  A: mode",
                _ => $"Hold trigger to {TerrainMode.ToString().ToLower()}  ·  A: mode  ·  stick: size / strength  ·  B: undo",
            },
            Tool.Water => StreamMode ? "Hold trigger and draw a stream - it flows downhill and the railway bridges it  ·  A: next mode"
                : Draining ? "Hold trigger over water to drain it (tap a stream to remove it)  ·  A: next mode" : "Hold trigger to dig a pond; start in a pond to make it bigger  ·  A: next mode  ·  stick: size",
            Tool.Forest => ForestErase ? "Hold trigger to clear trees  ·  A: paint" : "Hold trigger to paint a forest  ·  A: erase  ·  stick: size / density",
            Tool.Track => SignalMode ? "Trigger beside the track: put up a stop signal  ·  tap a signal: red / green  ·  A: draw"
                : World.Track.HasTrack ? "Draw from the track to branch off, from an end to extend, back onto track to join  ·  tap a switch to throw it  ·  grip: bend track  ·  A: signals"
                : "Hold trigger and draw a railway - come back to the start for a loop (over bare floor is fine)",
            Tool.Train => !World.Track.HasTrack ? "Draw a railway first (Track tool)"
                : hoveredTrain != null ? "Trigger: stop / go  ·  stick up-down: speed  ·  grip: pick it up  ·  A: stop / go"
                : "Trigger on the track: add a train  ·  tap a switch or signal to change it  ·  A: start / stop all",
            Tool.Paths => PathSystem.IsWay((int)PathKind) ? $"Draw a {PathSystem.Names[(int)PathKind].ToLower()} - start or end on another to join them; villagers walk along it  ·  A: next"
                : $"Draw a {PathSystem.Names[(int)PathKind].ToLower()} - close it into a loop for a pasture that keeps animals in  ·  A: next",
            Tool.Houses => HouseVariant == HouseFactory.Station ? "Point beside the railway: the station snaps to it and the train will stop there"
                : !carrying && Catalog.Get(HouseVariant).Hint != null ? $"Trigger: place the {Catalog.Get(HouseVariant).Name.ToLower()} - {Catalog.Get(HouseVariant).Hint}  ·  stick: rotate  ·  A: next"
                : carrying ? "Trigger: put the house down  ·  stick: rotate" : "Trigger: place  ·  stick: rotate  ·  A: next house  ·  grip / trigger on a house: move it",
            Tool.Decor => DecorKind == Villager ? "Trigger: place a villager - they visit shops and squares, ride the carousel and the train  ·  A: next"
                : DecorKind == (int)AnimalKind.Duck ? "Trigger on a pond: ducks paddle about  ·  A: next"
                : DecorKind == (int)AnimalKind.Deer ? "Trigger near the woods: deer stay among the trees and shy away from people  ·  A: next"
                : $"Trigger: place a {AnimalFactory.Names[DecorKind].ToLower()} - hold and sweep for a herd; inside a fence they stay in their pasture  ·  A: next",
            _ => "Tap a house, an animal, a villager, a train, a signal or a piece of track to erase  ·  hold over trees to clear them",
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
            if (!hit && (LandMode || Tool == Tool.Track)) hit = World.Terrain.RaycastFloor(localOrigin, localDir, 5f, out local);
            if (PlacingWorld) { PlaceWorld(input, ray, dt); return; }

            // Stick: size on X (rotation for houses), strength / density on Y.
            var stick = input.Stick;
            if (Mathf.Abs(stick.x) > 0.25f)
            {
                if (Tool == Tool.Houses) houseRotation += stick.x * 120f * dt;
                else radius[(int)Tool] = Mathf.Clamp(Radius * (1f + stick.x * 1.2f * dt), 0.04f, 0.4f);
            }
            if (Mathf.Abs(stick.y) > 0.4f && Tool != Tool.Train)
            {
                if (Tool == Tool.Forest) { density = Mathf.Clamp01(density + stick.y * 0.6f * dt); }
                else strength = Mathf.Clamp(strength + stick.y * 0.6f * dt, 0.15f, 1f);
            }
            if (input.PrimaryDown) CycleMode();

            if (hit) { rayLength = Vector3.Distance(ray.origin, wt.TransformPoint(local)); }
            switch (Tool)
            {
                case Tool.Terrain: TerrainTool(input, hit, local, dt, ref colour); break;
                case Tool.Water: WaterTool(input, hit, local, dt, ref colour); break;
                case Tool.Forest: ForestTool(input, hit, local, dt, ref colour); break;
                case Tool.Track: TrackTool(input, hit, local, ref colour); break;
                case Tool.Train: TrainTool(input, hit, local, localDir, dt, ref colour); break;
                case Tool.Houses: HouseTool(input, hit, local, localOrigin, localDir, ref colour); break;
                case Tool.Paths: PathTool(input, hit, local, ref colour); break;
                case Tool.Decor: DecorTool(input, hit, local, dt, ref colour); break;
                case Tool.Eraser: EraserTool(input, hit, local, localOrigin, localDir, dt, ref colour); break;
            }
            if (Tool != Tool.Houses) ghost.gameObject.SetActive(false);
            if (Tool != Tool.Train) { hoveredTrain = null; if (World.Trains.Highlighted != null) World.Trains.Highlighted = null; }
            floorRing.gameObject.SetActive(LandMode && hit);
            DrawPointer(ray, rayLength, colour, hit);
        }

        private void CycleMode()
        {
            switch (Tool)
            {
                case Tool.Terrain: TerrainMode = (TerrainBrush)(((int)TerrainMode + 1) % 7); Say(TerrainMode.ToString()); break;
                case Tool.Forest: ForestErase = !ForestErase; Say(ForestErase ? "Erase trees" : "Paint trees"); break;
                case Tool.Water:
                    if (!Draining && !StreamMode) StreamMode = true; else if (StreamMode) { StreamMode = false; Draining = true; } else Draining = false;
                    Say(StreamMode ? "Stream" : Draining ? "Drain" : "Pond"); break;
                case Tool.Decor: DecorKind = (DecorKind + 1) % (AnimalFactory.KindCount + 1); Say(DecorKind == Villager ? "People" : AnimalFactory.Names[DecorKind]); break;
                case Tool.Paths: PathKind = (PathKind)(((int)PathKind + 1) % PathSystem.Names.Length); Say(PathSystem.Names[(int)PathKind]); break;
                case Tool.Houses:
                {
                    var ids = Catalog.InCategory(BuildCategory);
                    HouseVariant = ids[(ids.IndexOf(HouseVariant) + 1) % ids.Count];
                    Say(HouseFactory.Name(HouseVariant)); break;
                }
                case Tool.Train: ToggleTrain(); break;
                case Tool.Track: SignalMode = !SignalMode; Say(SignalMode ? "Signals" : "Draw track"); break;
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
            TerrainBrush.Peaks => new Color(0.85f, 0.85f, 0.95f),
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
                    if (blocked && input.Trigger) Say("That's as far as the land can reach", 1.5f);
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

        private void WaterTool(PointerInput input, bool hit, Vector3 local, float dt, ref Color colour)
        {
            var ring = Draining ? new Color(1f, 0.75f, 0.45f) : new Color(0.45f, 0.8f, 1f);
            colour = ring; colour.a = 0.8f;
            if (hit) World.TerrainView.SetBrush(local, Radius, ring, 0.6f + (stroking ? 0.3f : 0f));
            else World.TerrainView.HideBrush();
            if (StreamMode)
            {
                // Drawn like the railway.
                World.TerrainView.SetBrush(local, 0.02f, ring, hit ? 0.9f : 0f);
                if (input.TriggerDown && hit) { drawn.Clear(); drawn.Add(local); }
                if (input.Trigger && drawn.Count > 0 && hit && new Vector2(local.x - drawn[^1].x, local.z - drawn[^1].z).magnitude > 0.012f)
                { drawn.Add(local); World.Track.ShowPreview(drawn, false, false); }
                if ((input.TriggerUp || !input.Trigger) && drawn.Count > 0)
                {
                    string error = World.BuildStream(drawn);
                    World.Track.HidePreview();
                    Say(error ?? "A stream! It flows downhill", 3f);
                    Haptic(error == null ? 0.5f : 0.8f, error == null ? 0.06f : 0.12f);
                    drawn.Clear();
                }
                return;
            }
            if (input.TriggerDown && hit && Draining && World.RemoveStreamAt(local)) { Say("Stream removed"); Haptic(0.5f, 0.05f); return; }
            if (input.TriggerDown && hit) { World.Checkpoint(); stroking = true; waterLevel = World.WaterLevelForStroke(local); }
            if (stroking && input.Trigger && hit) { World.WaterStroke(local, Radius, strength, dt, waterLevel, Draining); Buzz(0.12f); }
            if (stroking && (input.TriggerUp || !input.Trigger)) EndStroke();
        }

        private void DecorTool(PointerInput input, bool hit, Vector3 local, float dt, ref Color colour)
        {
            bool person = DecorKind == Villager;
            var kind = (AnimalKind)Mathf.Min(DecorKind, AnimalFactory.KindCount - 1);
            float radius = person ? PeopleSystem.Radius * 2f : AnimalFactory.Radius(kind);
            var p2 = new Vector2(local.x, local.z);
            bool free = hit && (person ? World.People.Walkable(p2) : World.Habitat(p2, kind, p2));
            colour = free ? new Color(1f, 0.95f, 0.75f, 0.85f) : new Color(1f, 0.5f, 0.45f, 0.85f);
            if (hit) World.TerrainView.SetBrush(local, radius * 1.4f, free ? new Color(1f, 0.95f, 0.7f) : new Color(1f, 0.4f, 0.35f), 0.8f);
            else World.TerrainView.HideBrush();
            sinceDrop += dt;
            if (input.TriggerDown && hit) { World.Checkpoint(); stroking = true; lastDrop = new Vector3(1e6f, 0f, 0f); sinceDrop = 1f; }
            if (stroking && input.Trigger && hit && (local - lastDrop).magnitude > (person ? 0.04f : AnimalFactory.Radius(kind) * 2.8f) && sinceDrop > 0.12f)
            {
                string error = person ? World.PlacePerson(local, Random.Range(0, PersonFactory.StyleCount), Random.Range(1, 1 << 30))
                    : World.PlaceAnimal(local, kind, Random.Range(1, 1 << 30));
                if (error == null) { lastDrop = local; sinceDrop = 0f; Haptic(0.35f, 0.03f); }
                else if (input.TriggerDown) Say(error);
            }
            if (stroking && (input.TriggerUp || !input.Trigger)) stroking = false;
        }

        /// <summary>Draws roads, lanes, paths, trails, fences, hedges and walls - the same gesture as the railway and streams.</summary>
        private void PathTool(PointerInput input, bool hit, Vector3 local, ref Color colour)
        {
            colour = new Color(0.95f, 0.85f, 0.65f, 0.85f);
            float width = Mathf.Max(PathSystem.HalfWidth((int)PathKind), 0.008f);
            if (drawn.Count == 0)
            {
                if (hit) World.TerrainView.SetBrush(local, width, new Color(1f, 0.9f, 0.6f), 0.9f); else World.TerrainView.HideBrush();
            }
            if (input.TriggerDown && hit) { drawn.Clear(); drawn.Add(local); }
            if (input.Trigger && drawn.Count > 0 && hit)
            {
                var last = drawn[^1];
                if (new Vector2(local.x - last.x, local.z - last.z).magnitude > 0.01f) { drawn.Add(local); World.Track.ShowPreview(drawn, false, false); }
            }
            if ((input.TriggerUp || !input.Trigger) && drawn.Count > 0)
            {
                World.Track.HidePreview();
                string error = World.BuildPath(drawn, PathKind);
                if (error == null) { Say(PathSystem.Names[(int)PathKind] + " added"); Haptic(0.5f, 0.05f); }
                else { Say(error); Haptic(0.7f, 0.1f); }
                drawn.Clear();
            }
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
            var net = World.Track.Network;
            if (SignalMode)
            {
                int sig = hit ? World.Track.PickSignal(local, 0.03f) : -1;
                World.TerrainView.SetBrush(local, sig >= 0 ? 0.02f : 0.015f, sig >= 0 ? new Color(1f, 0.6f, 0.4f) : new Color(1f, 0.9f, 0.5f), hit ? 0.9f : 0f);
                if (input.TriggerDown && hit)
                {
                    if (sig >= 0) { bool red = World.ToggleSignal(sig); Say(red ? "Signal red: trains wait here" : "Signal green"); Haptic(0.4f, 0.04f); }
                    else
                    {
                        string error = World.PlaceSignal(local);
                        if (error != null) { Say(error); Haptic(0.7f, 0.1f); } else { Say("Stop signal up - tap it to turn it green"); Haptic(0.5f, 0.05f); }
                    }
                }
                return;
            }
            // Grip on the track: bend it like a rope (the piece between its switches follows), let go to keep the shape.
            if (dragEdge >= 0)
            {
                if (dragEdge >= net.Edges.Count) { dragEdge = -1; World.Track.HidePreview(); return; }
                if (hit && Time.time - dragPreviewAt > 0.08f)
                {
                    dragPreviewAt = Time.time;
                    dragError = World.ReshapePreview(dragEdge, dragIndex, local, out var preview);
                    World.Track.ShowPreview(preview, false, dragError != null);
                }
                if (hit) World.TerrainView.SetBrush(local, 0.025f, dragError == null ? new Color(0.45f, 0.9f, 1f) : new Color(1f, 0.4f, 0.35f), 0.95f);
                if (input.GripUp || !input.Grip)
                {
                    string error = hit ? World.Reshape(dragEdge, dragIndex, local) : "Point at the ground to put the track down";
                    World.Track.HidePreview();
                    if (error != null) { Say(error); Haptic(0.7f, 0.1f); } else { Say("Track reshaped"); Haptic(0.5f, 0.05f); }
                    dragEdge = -1;
                }
                return;
            }
            if (drawn.Count == 0 && hit && input.GripDown && net.Nearest(local, out int grabEdge, out float grabS, out _, 0.05f))
            {
                dragEdge = grabEdge; dragIndex = TrackPlanner.IndexAt(net.Edges[grabEdge].Layout, grabS); dragError = null; dragPreviewAt = 0f;
                Say("Move the track and let go");
                Haptic(0.4f, 0.04f);
                return;
            }
            // Hover: switches light up (tap to throw), dead ends and track show where a stroke would attach.
            if (drawn.Count == 0 && hit)
            {
                int joint = World.Track.PickJoint(local, 0.045f);
                if (joint >= 0) World.TerrainView.SetBrush(net.Joints[joint].Position, 0.035f, new Color(1f, 0.7f, 0.35f), 0.95f);
                else
                {
                    var probe = new List<Vector3> { local };
                    var a = TrackPlanner.Attach(net, probe, true, out _);
                    if (a.Kind != AttachKind.None) World.TerrainView.SetBrush(new Vector3(a.Point.x, local.y, a.Point.y), 0.022f, new Color(0.45f, 0.9f, 1f), 0.95f);
                    else World.TerrainView.SetBrush(local, 0.012f, new Color(1f, 0.9f, 0.5f), 0.9f);
                }
            }
            else if (!hit) World.TerrainView.HideBrush();
            if (input.TriggerDown && hit) { drawn.Clear(); drawn.Add(local); strokeStarted = Time.time; }
            if (input.Trigger && drawn.Count > 0 && hit)
            {
                var last = drawn[^1];
                if (new Vector2(local.x - last.x, local.z - last.z).magnitude > 0.012f) drawn.Add(local);
                bool closing = TrackPlanner.WouldClose(drawn, net);
                var a = TrackPlanner.Attach(net, drawn, true, out _);
                var b = drawn.Count > 3 ? TrackPlanner.Attach(net, drawn, false, out _) : default;
                Vector3? snapA = a.Kind != AttachKind.None ? new Vector3(a.Point.x, World.Terrain.SurfaceAt(a.Point.x, a.Point.y), a.Point.y) : null;
                Vector3? snapB = b.Kind != AttachKind.None ? new Vector3(b.Point.x, World.Terrain.SurfaceAt(b.Point.x, b.Point.y), b.Point.y) : null;
                World.Track.ShowPreview(drawn, closing, false, snapA, snapB);
                if (closing || snapB.HasValue) Buzz(0.15f);
            }
            if ((input.TriggerUp || !input.Trigger) && drawn.Count > 0)
            {
                // A tap (no stroke) on a switch throws it; on a stop signal toggles it.
                if (TrackPlanner.Length(drawn) < 0.03f)
                {
                    int joint = World.Track.PickJoint(drawn[0], 0.045f);
                    int sig = World.Track.PickSignal(drawn[0], 0.03f);
                    if (joint >= 0) { Say(World.FlipSwitch(joint)); Haptic(0.5f, 0.04f); }
                    else if (sig >= 0) { bool red = World.ToggleSignal(sig); Say(red ? "Signal red" : "Signal green"); Haptic(0.4f, 0.04f); }
                    World.Track.HidePreview();
                    drawn.Clear();
                    return;
                }
                int before = net.Edges.Count, joints = net.Joints.Count;
                var plan = World.BuildTrack(drawn);
                if (plan.Ok)
                {
                    World.Track.HidePreview();
                    var after = World.Track.Network;
                    string what = plan.Closed ? $"Loop complete! {after.TotalLength:0.0} m of railway"
                        : after.Joints.Count > joints ? "New switch - trains can go both ways now"
                        : plan.Start.Kind == AttachKind.End || plan.Finish.Kind == AttachKind.End ? $"Railway extended ({after.TotalLength:0.0} m)"
                        : $"Railway laid ({after.TotalLength:0.0} m) - draw back to the start for a loop";
                    if (after.Crossings.Count > 0 && plan.Start.Kind == AttachKind.None && plan.Finish.Kind == AttachKind.None && !plan.Closed) what += " - with a crossing";
                    Say(what, 4f);
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

        private void TrainTool(PointerInput input, bool hit, Vector3 local, Vector3 direction, float dt, ref Color colour)
        {
            colour = new Color(1f, 0.6f, 0.5f, 0.85f);
            var net = World.Track.Network;
            // Carrying a train (grip): show where it would go; let go over track to put it there.
            if (carriedTrain != null)
            {
                bool onTrack = hit && net.Nearest(local, out _, out _, out _, 0.08f);
                World.TerrainView.SetBrush(local, 0.05f, onTrack ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.5f, 0.4f), hit ? 0.9f : 0f);
                if (input.GripUp || !input.Grip)
                {
                    var kind = carriedTrain.Kind; bool running = carriedTrain.Running; float speed = carriedTrain.SpeedFactor;
                    carriedTrain = null;
                    // The pick-up was checkpointed: undo puts it back if the new spot doesn't work.
                    Train placed = null;
                    string error = onTrack ? World.PlaceTrainNoCheckpoint(local, kind, direction, out placed) : "Put trains down on the track";
                    if (error != null) { World.Undo(); Say(error); Haptic(0.7f, 0.1f); }
                    else { placed.Running = running; placed.SpeedFactor = speed; Say("Train moved"); Haptic(0.5f, 0.05f); }
                    RefreshOptions();
                }
                return;
            }
            hoveredTrain = hit ? World.Trains.Near(local, 0.05f) : null;
            World.Trains.Highlighted = hoveredTrain;
            int joint = hit && hoveredTrain == null ? World.Track.PickJoint(local, 0.045f) : -1;
            int sig = hit && hoveredTrain == null && joint < 0 ? World.Track.PickSignal(local, 0.03f) : -1;
            if (hoveredTrain != null)
            {
                World.TerrainView.SetBrush(World.Trains.CentrePosition(hoveredTrain), 0.06f, TrainFactory.LiveryColour(hoveredTrain.Livery), 0.9f);
                if (Mathf.Abs(input.Stick.y) > 0.4f)
                {
                    hoveredTrain.SpeedFactor = Mathf.Clamp(hoveredTrain.SpeedFactor + input.Stick.y * 0.8f * dt, 0.3f, 1.8f);
                    Say($"Speed {hoveredTrain.SpeedFactor * 100f:0} %", 1f);
                }
                if (input.TriggerDown) { hoveredTrain.Running = !hoveredTrain.Running; Say(hoveredTrain.Running ? "All aboard!" : "Train stopping"); Haptic(0.5f, 0.05f); RefreshOptions(); }
                if (input.GripDown)
                {
                    World.Checkpoint();
                    carriedTrain = new Train { Kind = hoveredTrain.Kind, Running = hoveredTrain.Running, SpeedFactor = hoveredTrain.SpeedFactor };
                    World.Trains.Remove(hoveredTrain);
                    hoveredTrain = null;
                    Say("Point at the track and let go");
                }
                return;
            }
            if (joint >= 0)
            {
                World.TerrainView.SetBrush(net.Joints[joint].Position, 0.035f, new Color(1f, 0.7f, 0.35f), 0.95f);
                if (input.TriggerDown) { Say(World.FlipSwitch(joint)); Haptic(0.5f, 0.04f); }
                return;
            }
            if (sig >= 0)
            {
                World.TerrainView.SetBrush(new Vector3(World.Track.Signals[sig].x, local.y, World.Track.Signals[sig].z), 0.02f, new Color(1f, 0.6f, 0.4f), 0.9f);
                if (input.TriggerDown) { bool red = World.ToggleSignal(sig); Say(red ? "Signal red: trains wait here" : "Signal green"); Haptic(0.4f, 0.04f); }
                return;
            }
            int edge = -1; float s = 0f;
            bool onRails = hit && net.Nearest(local, out edge, out s, out _, 0.08f);
            if (onRails)
            {
                var p = net.Edges[edge].Layout.Evaluate(s, out _);
                World.TerrainView.SetBrush(p, 0.05f, new Color(1f, 0.85f, 0.4f), 0.8f);
            }
            else World.TerrainView.SetBrush(local, 0.03f, new Color(1f, 0.5f, 0.4f), hit ? 0.5f : 0f);
            if (input.TriggerDown && hit)
            {
                string error = World.PlaceTrain(local, TrainKind, direction, out _);
                if (error != null) { Say(error); Haptic(0.7f, 0.1f); }
                else { Say("Off it goes! Tap a train to stop it"); Haptic(0.5f, 0.05f); }
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
                        HouseVariant = h.variant; houseRotation = h.rotation; houseStyle = h.style; carrying = true; pickedHover = -1;
                        BuildCategory = Catalog.Get(h.variant).Category;
                        Say("Moving " + HouseFactory.Name(h.variant));
                        RefreshOptions();
                    }
                    return;
                }
            }
            if (!hit) { ghost.gameObject.SetActive(false); World.TerrainView.HideBrush(); return; }
            var candidate = new HouseInstance { x = local.x, z = local.z, rotation = houseRotation, variant = HouseVariant, style = houseStyle };
            string error;
            if (HouseVariant == HouseFactory.Station)
            {
                // Stations snap beside the rails, facing them, on the side the pointer is on.
                var snapped = World.StationAt(local, houseStyle);
                if (snapped.HasValue) { candidate = snapped.Value; local = new Vector3(candidate.x, local.y, candidate.z); houseRotation = candidate.rotation; error = World.ValidateHouse(candidate); }
                else error = World.Track.HasTrack ? "Point next to the railway for a station" : "Draw a railway first";
            }
            else error = World.ValidateHouse(candidate);
            ghost.gameObject.SetActive(true);
            ghostFilter.sharedMesh = HouseFactory.Get(HouseVariant, houseStyle);
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
                    bool wasCarrying = carrying;
                    World.PlaceHouse(candidate, !carrying);
                    carrying = false;
                    int styles = Mathf.Max(1, Catalog.Get(HouseVariant).Styles);
                    if (!wasCarrying) houseStyle = styles > 1 ? (houseStyle + 1 + Random.Range(0, styles - 1)) % styles : 0;
                    Haptic(0.5f, 0.05f);
                    Say(HouseFactory.Name(HouseVariant) + " placed");
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
            bool target = house >= 0 || (hit && (World.Trains.Near(local, 0.05f) != null || World.Track.DistanceTo(local) < 0.045f || World.Track.PickSignal(local, 0.025f) >= 0 || World.Paths.Pick(new Vector2(local.x, local.z)) >= 0));
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
