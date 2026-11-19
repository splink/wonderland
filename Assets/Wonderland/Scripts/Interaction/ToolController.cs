using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Paint, draw, place. Turns right-controller input into world edits for the selected tool, shows the pointer,
    /// brush ring, track preview and house ghost, and keeps the palette's options and hints in sync.
    /// Controls: trigger = use tool / press buttons; right stick = scroll the world; left stick left-right = brush size
    /// (houses: rotate); left stick up-down = strength or density; A = next mode (raise / lower / smooth / flatten,
    /// paint / erase, house type); B = undo; grip = pick up a house; left Y = hide or show the palette.
    /// </summary>
    public sealed partial class ToolController : MonoBehaviour
    {
        public WonderlandWorld World;
        public PaletteUI Palette;
        public Transform RightHand, LeftHand;
        /// <summary>The player's head (scrolling goes the way they face); the main camera when not set.</summary>
        public Transform Head;
        public System.Func<PointerInput> InputOverride;   // tests / desktop
        public System.Action ResetRequested;

        public Tool Tool { get; private set; } = Tool.Terrain;
        public TerrainBrush TerrainMode { get; private set; } = TerrainBrush.Raise;
        public bool ForestErase { get; private set; }
        /// <summary>What the forest brush plants.</summary>
        public ForestType ForestType { get; set; }
        public int HouseVariant { get; private set; }
        public float Radius => radius[(int)Tool];
        public bool PlacingWorld;
        /// <summary>The world was put down somewhere new (Move): the app pins it to the room there.</summary>
        public System.Action WorldPlaced;
        public bool HapticsEnabled = true;
        /// <summary>The map above the palette (the app adds it).</summary>
        public MiniMap Map;

        private readonly float[] radius = { 0.16f, 0.12f, 0.13f, 0.03f, 0.05f, 0.05f, 0.05f, 0.03f, 0.03f, 0.06f };
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
        public float StreamWidth { get; private set; } = 1f;   // 0.6 a creek, 1 a stream, 2.2 a river
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
        private int hoverPerson = -1, newsSeen, hoverCamera = -1;
        private TrafficSystem.Vehicle hoverVehicle;
        private float carTimer, turnHeld, turnSaid, nextPlan;
        private bool grabbable;
        private TrackPlan livePlan;
        private int liveDirection, planCount;
        private string liveNote;

        /// <summary>What letting go now will build, in words (while drawing).</summary>
        private string PlanSaid(TrackPlan p)
        {
            bool s = p.Start.Kind != AttachKind.None, f = p.Finish.Kind != AttachKind.None;
            string what = p.Closed ? "a loop" : s && f ? "a line joining the two tracks" : s || f ? (p.Start.Kind == AttachKind.End || p.Finish.Kind == AttachKind.End ? "the line extended" : "a branch with a new switch") : "a new line";
            return $"Let go to build {what} - trains will run the way the green arrows point" + (liveNote != null ? $" ({liveNote})" : "");
        }

        private static string Compass(float yaw) => new[] { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" }[Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 45f) % 8];
        private readonly LineRenderer[] entry = new LineRenderer[2];

        /// <summary>
        /// While drawing from (or onto) a track: an arrow on that track showing which way trains must be going to turn into
        /// the new line - a switch can only send a train into its branch when the train comes towards it from the trunk.
        /// </summary>
        private void EntryArrow(int k, TrackAttach at, bool start)
        {
            if (entry[k] == null)
            {
                var go = new GameObject("Entry arrow");
                entry[k] = go.AddComponent<LineRenderer>();
                entry[k].sharedMaterial = World.Materials.UI;
                entry[k].widthMultiplier = 0.004f;
                entry[k].startColor = entry[k].endColor = new Color(0.45f, 0.9f, 1f, 0.95f);
            }
            var line = entry[k];
            if (at.Kind != AttachKind.Mid) { line.enabled = false; return; }
            var d = new Vector3(at.Dir.x, 0f, at.Dir.y).normalized;
            if (!start) d = -d;   // onto a track: trains coming the other way along it turn off into the new line
            var p = new Vector3(at.Point.x, 0f, at.Point.y);
            var from = p - d * 0.16f; var tip = p - d * 0.02f;
            var side = Vector3.Cross(Vector3.up, d) * 0.014f;
            var pts = new[] { from, tip, tip - d * 0.02f + side, tip, tip - d * 0.02f - side };
            line.positionCount = pts.Length;
            for (int i = 0; i < pts.Length; i++) { var q = pts[i]; q.y = World.Terrain.SurfaceAt(q.x, q.z) + 0.016f; line.SetPosition(i, World.transform.TransformPoint(q)); }
            line.enabled = true;
        }

        private void HideEntryArrows() { foreach (var l in entry) if (l != null) l.enabled = false; }
        private LineRenderer facing;

        /// <summary>
        /// Which way the ghost faces: an arrow on the ground from its front; a camera also shows the edges of what it will
        /// see (looking out from its front, tilted down a little, as it will be put up).
        /// </summary>
        private void ShowFacing(Vector3 local, float yaw, float halfDepth, bool camera)
        {
            if (facing == null)
            {
                var go = new GameObject("Facing");
                facing = go.AddComponent<LineRenderer>();
                facing.sharedMaterial = World.Materials.UI;
                facing.widthMultiplier = 0.0025f;
                facing.startColor = facing.endColor = new Color(1f, 0.9f, 0.4f, 0.9f);
                facing.useWorldSpace = true;
            }
            var wt = World.transform;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var front = rot * Vector3.back;   // the front of everything is -Z
            var side = rot * Vector3.right;
            float y = World.Terrain.SurfaceAt(local.x, local.z) + 0.006f;
            var points = new List<Vector3>();
            if (camera)
            {
                var eye = local + rot * HouseFactory.CameraHeadAt; eye.y = y + HouseFactory.CameraHeadAt.y;
                var look = Quaternion.Euler(CctvSystem.DefaultPitch, yaw + 180f, 0f);
                float reach = 0.35f, half = Mathf.Tan(31f * Mathf.Deg2Rad) * reach, halfV = half * 0.75f;
                var c = eye + look * new Vector3(0f, 0f, reach);
                var r = look * Vector3.right * half; var u = look * Vector3.up * halfV;
                foreach (var corner in new[] { c - r - u, c + r - u, c + r + u, c - r + u, c - r - u })
                { points.Add(eye); points.Add(corner); }
                points.Add(c - r - u); points.Add(c + r - u); points.Add(c + r + u); points.Add(c - r + u); points.Add(c - r - u);
            }
            else
            {
                var a = new Vector3(local.x, y, local.z) + front * (halfDepth + 0.01f);
                var tip = a + front * 0.04f;
                points.AddRange(new[] { a, tip, tip - front * 0.014f + side * 0.012f, tip, tip - front * 0.014f - side * 0.012f });
            }
            facing.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) facing.SetPosition(i, wt.TransformPoint(points[i]));
            facing.enabled = true;
        }
        private Train lastTrain;


        /// <summary>Couples on (+1) or uncouples (-1) a carriage of a train, and says how long it is now (or why not).</summary>
        private void Couple(Train t, int change)
        {
            if (t == null || !World.Trains.Trains.Contains(t)) { Say("Point at a train first, then press + or - carriage"); return; }
            World.Checkpoint();
            string why = World.Trains.SetCars(t, TrainSystem.CarsOf(t) + change);
            int cars = TrainSystem.CarsOf(t);
            Say(why ?? $"{cars} {TrainSystem.CarWord(t.Kind, cars)} behind the engine");
            Haptic(why == null ? 0.5f : 0.7f, why == null ? 0.04f : 0.1f);
        }
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
            PutBack();
            Tool = tool;   // (an open card - and the camera following someone - stays open: keep building while watching)
            Palette.SetTool(tool);
            World.Cctv.Watching = tool == Tool.Select;
            if (infoMode == 2) ShowHelp();
            SyncViewer();
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
            BuildFamily = Catalog.Get(HouseVariant).Family;
            carrying = false;
            RefreshOptions();
        }

        /// <summary>The library's family (Cottages, Dining, Railway...): its objects show as tiles beside the palette.</summary>
        public string BuildFamily { get; private set; }

        private void SelectFamily(string family)
        {
            BuildFamily = family;
            var ids = Catalog.InFamily(BuildCategory, family);
            if (!ids.Contains(HouseVariant) && ids.Count > 0) HouseVariant = ids[0];
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
                _ => $"Hold trigger to {TerrainMode.ToString().ToLower()}  ·  A: mode  ·  left stick: size / strength  ·  B: undo",
            },
            Tool.Water => StreamMode ? "Hold trigger and draw a stream - it flows downhill and the railway bridges it  ·  A: next mode"
                : Draining ? "Hold trigger over water to drain it (tap a stream to remove it)  ·  A: next mode" : "Hold trigger to dig a pond; start in a pond to make it bigger  ·  A: next mode  ·  left stick: size",
            Tool.Forest => ForestErase ? "Hold trigger to clear trees  ·  A: paint" : $"Hold trigger to plant: {ForestSystem.ForestNames[(int)ForestType].ToLower()}  ·  A: erase  ·  left stick: size / density",
            Tool.Track => SignalMode ? "Trigger beside the track: put up a stop signal  ·  tap a signal: red / green  ·  A: draw"
                : grabbable ? "On the track: trigger and draw to branch off  ·  GRIP and move: bend this piece of track  ·  tap a switch to throw it"
                : World.Track.HasTrack ? (World.Bridges.Bridges.Count > 0 ? "Tap a bridge to change its style  ·  draw from the track to branch, from an end to extend  ·  tap a switch to throw it  ·  grip: bend track"
                    : "Draw from the track to branch off, from an end to extend, back onto track to join  ·  tap a switch to throw it  ·  grip: bend track  ·  A: signals")
                : "Hold trigger and draw a railway - come back to the start for a loop (over bare floor is fine)",
            Tool.Train => !World.Track.HasTrack ? "Draw a railway first (Track tool)"
                : hoveredTrain != null ? $"{World.Trains.Describe(hoveredTrain)}  ·  trigger: stop / go  ·  left stick up-down: speed, left-right: carriages  ·  grip: move it"
                : "Trigger on the track: add a train  ·  tap a switch or signal to change it  ·  A: start / stop all",
            Tool.Paths => PlacingVehicle is { } pv ? $"Trigger on a road: a {VehicleName(pv).ToLower()} - it drives about the town, keeps to its lane and stops at red lights  ·  the way you point is the way it faces"
                : PathSystem.IsRoad((int)PathKind) ? $"Draw a {PathSystem.Names[(int)PathKind].ToLower()}: it comes out straight with rounded corners; draw across another road for a crossroads with traffic lights, end on one for a T-junction  ·  A: next"
                : PathSystem.IsWay((int)PathKind) ? $"Draw a {PathSystem.Names[(int)PathKind].ToLower()} - across water it gets a bridge (tap a bridge to change it); start on another way to join  ·  A: next"
                : $"Draw a {PathSystem.Names[(int)PathKind].ToLower()} - close it into a loop for a pasture that keeps animals in  ·  A: next",
            Tool.Houses => pickedHover >= 0 && !carrying && pickedHover < World.Buildings.Houses.Count ? World.People.DescribePlace(pickedHover) + "  ·  trigger: move it"
                : Catalog.IsStation(HouseVariant) ? "Point beside the railway: the station snaps to it and the train will stop there"
                : !carrying && Catalog.Get(HouseVariant).Hint != null ? $"Trigger: place the {Catalog.Get(HouseVariant).Name.ToLower()} - {Catalog.Get(HouseVariant).Hint}  ·  left stick: turn ({houseRotation:0}°, the arrow is its front)  ·  A: next"
                : carrying ? "Trigger: put the house down  ·  left stick: rotate" : "Trigger: place  ·  left stick: rotate  ·  A: next house  ·  grip / trigger on a house: move it",
            Tool.Decor => hoverPerson >= 0 && hoverPerson < World.People.People.Count ? World.People.Describe(hoverPerson) + "  ·  trigger: their card"
                : hoverAnimal >= 0 && hoverAnimal < World.Animals.Animals.Count ? AnimalLine(hoverAnimal) + "  ·  trigger: its card"
                : hoverVehicle != null && World.Traffic.Vehicles.Contains(hoverVehicle) ? World.Traffic.Describe(hoverVehicle)
                : DecorKind == Villager ? $"{World.Clock.Label}, {VillageClock.PartName(World.Clock.Part)}  ·  Trigger: place a villager - point at one to see what they're up to  ·  A: next"
                : DecorKind == (int)AnimalKind.Duck ? "Trigger on a pond: ducks paddle about  ·  A: next"
                : DecorKind == (int)AnimalKind.Deer ? "Trigger near the woods: deer stay among the trees and shy away from people  ·  A: next"
                : $"Trigger: place a {AnimalFactory.Names[DecorKind].ToLower()} - hold and sweep for a herd; inside a fence they stay in their pasture  ·  A: next",
            Tool.Select => SelectedCamera() >= 0 ? $"{World.Cctv.NameOf(SelectedCamera())}  ·  turn it with the buttons round its picture or the left stick  ·  trigger on the land: point it there  ·  A: next camera"
                : "Point at anything - a villager, an animal, a building, a train, a car, a camera - and pull the trigger to select it  ·  A: next camera",
            _ => "Tap a house, an animal, a villager, a train, a signal or a piece of track to erase  ·  hold over trees to clear them",
        };

        private void Update()
        {
            if (World == null || Palette == null) return;
            var input = InputOverride != null ? InputOverride() : quest.Read(RightHand, LeftHand);
            if (resetArmed && Time.time > resetUntil) resetArmed = false;
            if (World.People.NewsVersion != newsSeen) { newsSeen = World.People.NewsVersion; if (World.People.News != null) Say(World.People.News, 4f); }
            Palette.SetStatus(Time.time < messageUntil ? message : "");
            if (!input.Valid) { line.enabled = false; dot.gameObject.SetActive(false); return; }
            if (input.MenuDown) Palette.gameObject.SetActive(!Palette.gameObject.activeSelf);
            Process(input, Time.deltaTime);
        }

        /// <summary>One frame of tool logic (public so a scripted input source can drive it).</summary>
        public void Process(PointerInput input, float dt)
        {
            Palette.SetClock(World.Clock.Label, Health(out _));
            UpdateTips(dt);
            Palette.SetHint(PlacingWorld ? "Point at the floor and pull the trigger to put the world there  ·  stick left/right: turn it, up/down: raise or lower it" : Peek() ?? Hint());
            UpdateInspection(dt);
            if (Twist(input)) return;   // (both grips: the world turns with the hands, nothing else this frame)
            if (PlacingWorld) TurnAndLift(input.Stick + input.LeftStick, dt);
            else if (input.Grip && !Holding) GripTurn(input, dt);
            else { GripTurn(input, dt); Scroll(input.Stick, dt); }
            var ray = input.Ray;
            float rayLength = 2.5f;
            var colour = new Color(1f, 1f, 1f, 0.7f);
            pointerOnPalette = false;

            // 0. The map above the palette: point at a place and pull the trigger to slide the window there.
            if (!stroking && drawn.Count == 0 && Map != null && Palette.gameObject.activeInHierarchy && Map.Pick(ray, out var mapAt, out float mapDistance))
            {
                World.TerrainView.HideBrush();
                ghost.gameObject.SetActive(false);
                Palette.ClearHover();
                if (input.TriggerDown)
                {
                    if (World.NavigateTo(mapAt)) { Say("Off there - now " + World.WhereLabel()); Haptic(0.4f, 0.04f); }
                    else Say("The world doesn't go that far");
                }
                pointerOnPalette = true;
                DrawPointer(ray, mapDistance, new Color(1f, 0.9f, 0.6f, 0.9f), true);
                return;
            }

            // 1. The palette takes the ray first (unless a stroke is in progress).
            if (!stroking && drawn.Count == 0 && Palette.gameObject.activeInHierarchy)
            {
                var button = Palette.Raycast(ray, out float uiDistance);
                if (button != null || Palette.RayHitsPanel(ray, out uiDistance))
                {
                    World.TerrainView.HideBrush();
                    ghost.gameObject.SetActive(false);
                    if (button != null && input.TriggerDown && button.Enabled && button.OnClick != null) { button.OnClick(); Haptic(0.4f, 0.02f); }
                    pointerOnPalette = true;
                    ShowRailMarks(Tool == Tool.Track && !SignalMode, false, default);
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
            lastHit = hit; lastLocal = local;
            if (PlacingWorld) { PlaceWorld(input, ray, dt); return; }

            // Left stick: size on X (rotation for houses), strength / density on Y.
            var stick = input.LeftStick;
            if (Tool == Tool.Houses || carrying)
            {
                // Turning what is being placed: a flick turns it 15 degrees (on the grid: a quarter turn); holding the stick
                // over turns it smoothly (off the grid).
                if (Mathf.Abs(stick.x) > 0.6f)
                {
                    if (BuildGrid.Snap) { if (turnHeld == 0f) houseRotation = BuildGrid.Quarter(houseRotation + Mathf.Sign(stick.x) * 90f); }
                    else if (turnHeld == 0f) houseRotation = Mathf.Round((houseRotation + Mathf.Sign(stick.x) * 15f) / 15f) * 15f;
                    else if (turnHeld > 0.45f) houseRotation += stick.x * 60f * dt;
                    turnHeld += dt;
                }
                else turnHeld = 0f;
                houseRotation = Mathf.Repeat(houseRotation, 360f);
            }
            else if (Mathf.Abs(stick.x) > 0.25f && Tool != Tool.Select) radius[(int)Tool] = Mathf.Clamp(Radius * (1f + stick.x * 1.2f * dt), 0.04f, 0.4f);
            if (Mathf.Abs(stick.y) > 0.4f && Tool != Tool.Train && Tool != Tool.Select)
            {
                if (Tool == Tool.Forest) { density = Mathf.Clamp01(density + stick.y * 0.6f * dt); }
                else strength = Mathf.Clamp(strength + stick.y * 0.6f * dt, 0.15f, 1f);
            }
            if (input.PrimaryDown) CycleMode();

            if (hit) { rayLength = Vector3.Distance(ray.origin, wt.TransformPoint(local)); }
            // What is being pointed at, in any tool: grip opens its card (a ring under it shows what grip would select).
            // Small things are found by how close the ray passes to them - a villager in front of a house is found
            // before the house - but nothing behind a building or the land is.
            bool looking = !stroking && !Holding && drawn.Count == 0;
            var dirN = localDir.normalized;
            int houseHit = rayHouse = World.Buildings.Pick(localOrigin, dirN);
            if (houseHit >= 0 && !World.InWindow(new Vector2(World.Buildings.Houses[houseHit].x, World.Buildings.Houses[houseHit].z), 0.03f)) houseHit = rayHouse = -1;   // (outside the window: not there to point at)
            // What hides things: the land, and buildings' walls (not the air under a sloping roof). Someone at a door
            // stands a little inside its box (eaves): a few centimetres' grace - anyone behind it is further.
            bool walled = World.Buildings.Pick(localOrigin, dirN, out float wallT, true) >= 0;
            float reach = Mathf.Min(hit ? (local - localOrigin).magnitude + 0.012f : 3f, walled ? wallT + 0.03f : 3f);
            peekPerson = looking ? World.People.PickRay(localOrigin, dirN, reach, out _) : -1;
            peekAnimal = looking && peekPerson < 0 ? World.Animals.PickRay(localOrigin, dirN, reach, out _) : -1;
            peekVehicle = looking && peekPerson < 0 && peekAnimal < 0 ? World.Traffic.PickRay(localOrigin, dirN, reach, out _) : null;
            peekTrain = looking && peekPerson < 0 && peekAnimal < 0 && peekVehicle == null && Tool != Tool.Train ? World.Trains.PickRay(localOrigin, dirN, reach, out _) : null;
            bool small = peekPerson >= 0 || peekAnimal >= 0 || peekVehicle != null || peekTrain != null;
            peekBoat = hit && looking && !small ? World.Boats.Nearest(local, 0.015f) : -1;
            // ...and a building: grip (trigger in the Life tool) opens its card - not in Build or Eraser, where these pick up or remove.
            peekHouse = looking && !small && peekBoat < 0 && Tool != Tool.Houses && Tool != Tool.Eraser ? houseHit : -1;
            ShowTarget();
            // Grip, in any tool: pick up what is pointed at (a building, a train, someone) or bend the track under it.
            if (looking && input.GripDown && GrabAt(input, hit, local, houseHit))
            {
                DrawPointer(ray, rayLength, new Color(1f, 0.85f, 0.3f, 0.9f), true);
                return;
            }
            // Holding something: it follows the pointer until it is put down.
            if (carriedPerson.HasValue || carriedAnimal.HasValue) { CarryLiving(input, hit, local, ref colour); DrawPointer(ray, rayLength, colour, hit); return; }
            if (dragEdge >= 0) { DragTrack(input, hit, local, ref colour); DrawPointer(ray, rayLength, colour, hit); return; }
            // Select: the trigger picks what is pointed at and opens its card.
            bool picking = input.TriggerDown && Tool == Tool.Select && (small || peekHouse >= 0);
            if (looking && picking)
            {
                if (peekPerson >= 0 || peekAnimal >= 0) Inspect(peekPerson, peekAnimal);
                else if (peekVehicle != null) InspectVehicle(peekVehicle);
                else if (peekTrain != null) InspectTrain(peekTrain);
                else InspectHouse(peekHouse);
                DrawPointer(ray, rayLength, new Color(1f, 0.85f, 0.3f, 0.9f), true);
                return;
            }
            bool meeting = small || peekHouse >= 0;
            // What is carried decides which tool's hands are on it (a building picked up in any tool is placed as in Build).
            var active = carrying ? Tool.Houses : carriedTrain != null ? Tool.Train : Tool;
            switch (active)
            {
                case Tool.Terrain: TerrainTool(input, hit, local, dt, ref colour); break;
                case Tool.Water: WaterTool(input, hit, local, dt, ref colour); break;
                case Tool.Forest: ForestTool(input, hit, local, dt, ref colour); break;
                case Tool.Track: TrackTool(input, hit, local, ref colour); break;
                case Tool.Train: TrainTool(input, hit, local, localDir, dt, ref colour); break;
                case Tool.Houses: HouseTool(input, hit, local, localOrigin, localDir, ref colour); break;
                case Tool.Paths: PathTool(input, hit, local, ref colour); break;
                case Tool.Decor: DecorTool(input, hit, local, dt, ref colour); break;
                case Tool.Select: SelectTool(input, hit, local, dt, ref colour); break;
                case Tool.Eraser: EraserTool(input, hit, local, localOrigin, localDir, dt, ref colour); break;
            }
            if (active != Tool.Houses) ghost.gameObject.SetActive(false);
            BuildGrid.Show(active == Tool.Houses || Tool == Tool.Paths && PlacingVehicle == null && PathSystem.IsRoad((int)PathKind) || Tool == Tool.Track && !SignalMode);
            if (facing != null && (active != Tool.Houses || !ghost.gameObject.activeSelf)) facing.enabled = false;
            if (Tool != Tool.Track || drawn.Count == 0) HideEntryArrows();
            if (Tool != Tool.Train) { hoveredTrain = null; if (World.Trains.Highlighted != null) World.Trains.Highlighted = null; }
            floorRing.gameObject.SetActive(LandMode && hit);
            ShowRailMarks(Tool == Tool.Track && !SignalMode, hit, local);
            if (meeting && !stroking) colour = new Color(1f, 0.85f, 0.3f, 0.85f);   // (the pointer turns gold over something to meet)
            DrawPointer(ray, rayLength, colour, hit);
        }

        private static string SignalSaid(bool held) => held ? "Signal held at red: trains wait here - tap again for automatic" : "Signal on automatic: red while a train is on the line beside it";

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
                case Tool.Paths: PathKind = WayOrder[(System.Array.IndexOf(WayOrder, PathKind) + 1) % WayOrder.Length]; PlacingVehicle = null; Say(PathSystem.Names[(int)PathKind]); RefreshOptions(); break;
                case Tool.Houses:
                {
                    var ids = Catalog.InCategory(BuildCategory);
                    HouseVariant = ids[(ids.IndexOf(HouseVariant) + 1) % ids.Count];
                    BuildFamily = Catalog.Get(HouseVariant).Family;
                    Say(HouseFactory.Name(HouseVariant)); RefreshOptions(); break;
                }
                case Tool.Train: ToggleTrain(); break;
                case Tool.Track: SignalMode = !SignalMode; Say(SignalMode ? "Signals" : "Draw track"); break;
                case Tool.Select:
                    if (World.Cctv.Feeds > 0) OpenFeed((World.Cctv.Selected + 1) % World.Cctv.Feeds);
                    break;
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
                    string error = World.BuildStream(drawn, StreamWidth);
                    World.Track.HidePreview();
                    Say(error ?? (StreamWidth > 1f ? "A river! It flows downhill" : StreamWidth < 1f ? "A creek! It trickles downhill" : "A stream! It flows downhill"), 3f);
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

        /// <summary>Watch: pick a camera to watch (trigger on it), or point the watched one at a spot (trigger on the land).</summary>
        /// <summary>
        /// Select: the trigger picks whatever has the gold ring under it - a villager, an animal, a building, a train, a
        /// car, a boat, a CCTV camera - and opens its card. With a camera's card open, its picture is on the monitor:
        /// the buttons round it (or the stick) turn and tilt it, and the trigger on bare land points it there.
        /// </summary>
        private void SelectTool(PointerInput input, bool hit, Vector3 local, float dt, ref Color colour)
        {
            var cctv = World.Cctv;
            World.TerrainView.HideBrush();
            colour = new Color(1f, 0.95f, 0.75f, 0.85f);
            int cam = SelectedCamera();
            if (cam >= 0 && input.LeftStick.sqrMagnitude > 0.09f) TurnCamera(input.LeftStick.x * 70f * dt, -input.LeftStick.y * 40f * dt);
            bool target = peekPerson >= 0 || peekAnimal >= 0 || peekVehicle != null || peekTrain != null || peekHouse >= 0 || peekBoat >= 0;
            if (!input.TriggerDown || target) return;
            if (hit && SelectRail(local)) return;
            if (cam >= 0 && hit) { cctv.AimAt(cam, local); Say($"{cctv.NameOf(cam)} now looks this way"); Haptic(0.4f, 0.03f); cardTimer = 0f; return; }
            Say("Point at something - a villager, a building, a train, a switch, the track - and pull the trigger to select it");
        }

        /// <summary>Turns the selected camera (on a pole) by <paramref name="yaw"/> and tilts it by <paramref name="pitch"/> degrees.</summary>
        private void TurnCamera(float yaw, float pitch)
        {
            int cam = SelectedCamera();
            if (cam < 0) return;
            var cctv = World.Cctv;
            cctv.Turn(cam, yaw, pitch);
            turnSaid -= Time.deltaTime;
            if (turnSaid <= 0f || Mathf.Abs(yaw) >= 5f || Mathf.Abs(pitch) >= 5f) { turnSaid = 0.4f; var a = cctv.AimOf(cam); Say($"{cctv.NameOf(cam)}: facing {Compass(a.yaw)}, {a.pitch:0}° down", 1f); }
            cardTimer = 0f;
        }

        /// <summary>Opens feed <paramref name="k"/>: a pole camera's card (its picture on the monitor), or a train's card with its camera.</summary>
        private void OpenFeed(int k)
        {
            var cctv = World.Cctv;
            if (k < 0 || k >= cctv.Feeds) return;
            if (k < cctv.Count) InspectHouse(cctv.HouseOf(k));
            else { InspectTrain(World.Trains.Trains[k - cctv.Count]); trainCamera = true; cctv.Selected = k; }
            Say(cctv.NameOf(k));
            SyncViewer();
        }

        private void DecorTool(PointerInput input, bool hit, Vector3 local, float dt, ref Color colour)
        {
            bool person = DecorKind == Villager;
            var kind = (AnimalKind)Mathf.Min(DecorKind, AnimalFactory.KindCount - 1);
            float radius = person ? PeopleSystem.Radius * 2f : AnimalFactory.Radius(kind);
            var p2 = new Vector2(local.x, local.z);
            hoverPerson = peekPerson;   // (the same pointing as everywhere: the ray passing near them)
            hoverAnimal = hoverPerson < 0 ? peekAnimal : -1;
            hoverVehicle = hit && hoverPerson < 0 && hoverAnimal < 0 ? World.Traffic.Nearest(p2, 0.03f) : null;
            // Trigger on someone who is already there: not another one on top of them (their card is in Select).
            if (input.TriggerDown && hit && (hoverPerson >= 0 || hoverAnimal >= 0)) { Say("Someone's here already - Select shows their card, grip moves them"); return; }
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
        /// <summary>The Grid chip (Roads, Track and Build tools): snapping to the building grid on or off.</summary>
        private const string GridOnSaid = "Grid on: roads and railway come out straight and square, curves are standard, buildings line up along the roads";

        private PaletteUI.Option GridChip() => new("Grid", BuildGrid.Snap, () =>
        {
            BuildGrid.Snap = !BuildGrid.Snap;
            Say(BuildGrid.Snap ? GridOnSaid : "Grid off: draw and place freely");
            RefreshOptions();
        }) { Kind = OptionKind.Toggle, Tip = "Grid on: roads and railway come out straight and square with standard curves, and buildings line up along the roads. Off: draw and place freely." };

        /// <summary>The order of the Roads tool's chips: roads first, then country ways, then fences.</summary>
        private static readonly PathKind[] WayOrder = { PathKind.Road, PathKind.Highway, PathKind.Lane, PathKind.ServiceRoad, PathKind.Footpath, PathKind.Trail, PathKind.Fence, PathKind.Hedge, PathKind.Wall, PathKind.SecurityFence };

        /// <summary>A vehicle chosen to put on the road (null: drawing roads).</summary>
        public VehicleKind? PlacingVehicle { get; private set; }
        private int vehicleColour;
        private int vehicleStyle;   // the car (or bus) style the Vehicles group has picked
        private float nextRoadPlan;
        private int roadPlanCount;

        private static string VehicleName(VehicleKind k) => k switch { VehicleKind.Truck => "Lorry", _ => k.ToString() };

        private void PathTool(PointerInput input, bool hit, Vector3 local, ref Color colour)
        {
            colour = new Color(0.95f, 0.85f, 0.65f, 0.85f);
            if (PlacingVehicle is { } placing)
            {
                bool onRoad = hit && World.Traffic.NearestNode(new Vector2(local.x, local.z), 0.05f, true) >= 0;
                if (hit) World.TerrainView.SetBrush(local, 0.025f, onRoad ? new Color(0.6f, 0.95f, 0.6f) : new Color(1f, 0.45f, 0.4f), 0.9f); else World.TerrainView.HideBrush();
                if (input.TriggerDown && hit)
                {
                    var f = input.Ray.direction;
                    float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg - World.transform.eulerAngles.y;
                    int paint = placing switch
                    {
                        VehicleKind.Car => vehicleStyle * VehicleFactory.Paints + vehicleColour++ % VehicleFactory.Paints,   // (the style chosen, a new colour each time)
                        VehicleKind.Bus => vehicleStyle,
                        _ => vehicleColour++ % VehicleFactory.Paints,
                    };
                    string error = World.PlaceVehicle(local, placing, yaw, paint);
                    if (error == null) { Say($"{VehicleFactory.Name(placing, paint)} on the road - off it goes"); Haptic(0.5f, 0.05f); }
                    else { Say(error); Haptic(0.7f, 0.1f); }
                }
                return;
            }
            float width = Mathf.Max(PathSystem.HalfWidth((int)PathKind), 0.008f);
            if (drawn.Count == 0)
            {
                int bridge = hit ? World.Bridges.Pick(new Vector2(local.x, local.z)) : -1;
                if (bridge >= 0) { var a = World.Bridges.Bridges[bridge].Anchor; World.TerrainView.SetBrush(new Vector3(a.x, local.y, a.y), 0.05f, new Color(0.55f, 0.85f, 1f), 0.95f); }
                else if (hit) World.TerrainView.SetBrush(local, width, new Color(1f, 0.9f, 0.6f), 0.9f); else World.TerrainView.HideBrush();
            }
            if (input.TriggerDown && hit) { drawn.Clear(); drawn.Add(local); }
            if (input.Trigger && drawn.Count > 0 && hit)
            {
                var last = drawn[^1];
                if (new Vector2(local.x - last.x, local.z - last.z).magnitude > 0.01f) drawn.Add(local);
                // Roads: show what letting go builds (straightened, corners rounded) while drawing.
                if (PathSystem.IsRoad((int)PathKind) && drawn.Count > 3)
                {
                    if (Time.time >= nextRoadPlan && drawn.Count != roadPlanCount)
                    {
                        nextRoadPlan = Time.time + 0.15f; roadPlanCount = drawn.Count;
                        var plan = World.Paths.Plan(drawn, (int)PathKind, out string why);
                        if (plan != null)
                        {
                            var line = new List<Vector3>();
                            for (int k = 0; k < plan.points.Count; k += 2) { var q = plan.points[k]; line.Add(new Vector3(q.x, World.Terrain.SurfaceAt(q.x, q.y) + 0.004f, q.y)); }
                            if (plan.closed) line.Add(line[0]);
                            World.Track.ShowPreview(line, false, false, null, null, true);
                            Say("Let go to build this " + PathSystem.Names[(int)PathKind].ToLower(), 1f);
                        }
                        else { World.Track.ShowPreview(drawn, false, why != "Draw a little longer"); Say(why == "Draw a little longer" ? "Keep drawing..." : "Can't build this: " + why, 1f); }
                    }
                }
                else World.Track.ShowPreview(drawn, false, false);
            }
            if ((input.TriggerUp || !input.Trigger) && drawn.Count > 0)
            {
                World.Track.HidePreview();
                if (TrackPlanner.Length(drawn) < 0.03f)
                {
                    // A tap: on a bridge, try its next style.
                    var bridge = World.CycleBridge(drawn[0]);
                    if (bridge != null) { Say(bridge + " - tap again for another style"); Haptic(0.5f, 0.05f); }
                    drawn.Clear();
                    return;
                }
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
                int n = ForestErase ? World.EraseForest(local, Radius, strength + 0.4f, dt) : World.PaintForest(local, Radius, density, dt, ForestType);
                if (n > 0) Buzz(0.2f);
                if (!ForestErase && n == 0 && World.Forest.Full is { } full) Say(full);
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
                    if (sig >= 0) { bool red = World.ToggleSignal(sig); Say(SignalSaid(red)); Haptic(0.4f, 0.04f); }
                    else
                    {
                        string error = World.PlaceSignal(local);
                        if (error != null) { Say(error); Haptic(0.7f, 0.1f); } else { Say("Signal up: on automatic, red while a train is on the line beside it - tap it to hold trains at red"); Haptic(0.5f, 0.05f); }
                    }
                }
                return;
            }
            // Pointing at a piece of track (not drawing): it can be grabbed - show where, and say so.
            int grabEdge = -1; float grabS = 0f;
            grabbable = drawn.Count == 0 && hit && net.Nearest(local, out grabEdge, out grabS, out _, 0.06f) && World.Track.PickJoint(local, 0.045f) < 0;
            if (grabbable && input.GripDown) { heldByTrigger = false; BendTrack(grabEdge, grabS); return; }   // grip on the track: bend it like a rope
            // Hover: switches light up (tap to throw), dead ends and track show where a stroke would attach.
            if (drawn.Count == 0 && hit)
            {
                int joint = World.Track.PickJoint(local, 0.045f);
                if (joint >= 0) World.TerrainView.SetBrush(net.Joints[joint].Position, 0.035f, new Color(1f, 0.7f, 0.35f), 0.95f);
                else
                {
                    var probe = new List<Vector3> { local };
                    var a = TrackPlanner.Attach(net, probe, true, out _);
                    int bridge = World.Bridges.Pick(new Vector2(local.x, local.z));
                    if (grabbable && net.Nearest(local, out int ge, out float gs, out _, 0.06f))
                    {
                        // The grab point, ringed (pulsing): grip here to bend this piece.
                        var gp = net.Edges[ge].Layout.Evaluate(gs, out _);
                        World.TerrainView.SetBrush(gp, 0.026f + 0.004f * Mathf.Sin(Time.time * 6f), new Color(0.75f, 0.55f, 1f), 0.95f);
                    }
                    else if (a.Kind != AttachKind.None) World.TerrainView.SetBrush(new Vector3(a.Point.x, local.y, a.Point.y), 0.022f, new Color(0.45f, 0.9f, 1f), 0.95f);
                    else if (bridge >= 0) { var c = World.Bridges.Bridges[bridge].Anchor; World.TerrainView.SetBrush(new Vector3(c.x, local.y, c.y), 0.05f, new Color(0.55f, 0.85f, 1f), 0.95f); }
                    else World.TerrainView.SetBrush(local, 0.012f, new Color(1f, 0.9f, 0.5f), 0.9f);
                }
            }
            else if (!hit) World.TerrainView.HideBrush();
            if (input.TriggerDown && hit) { drawn.Clear(); drawn.Add(local); strokeStarted = Time.time; livePlan = null; planCount = 0; }
            if (input.Trigger && drawn.Count > 0 && hit)
            {
                var last = drawn[^1];
                if (new Vector2(local.x - last.x, local.z - last.z).magnitude > 0.012f) drawn.Add(local);
                bool closing = TrackPlanner.WouldClose(drawn, net);
                var a = TrackPlanner.Attach(net, drawn, true, out _);
                var b = drawn.Count > 3 ? TrackPlanner.Attach(net, drawn, false, out _) : default;
                Vector3? snapA = a.Kind != AttachKind.None ? new Vector3(a.Point.x, World.Terrain.SurfaceAt(a.Point.x, a.Point.y), a.Point.y) : null;
                Vector3? snapB = b.Kind != AttachKind.None ? new Vector3(b.Point.x, World.Terrain.SurfaceAt(b.Point.x, b.Point.y), b.Point.y) : null;
                // Plan it as letting go would, a few times a second: show what will be built (or why not) while drawing.
                if (drawn.Count > 3 && drawn.Count != planCount && Time.time >= nextPlan)
                {
                    nextPlan = Time.time + 0.2f; planCount = drawn.Count;
                    livePlan = World.PreviewTrack(drawn, out liveDirection, out liveNote);
                }
                bool incomplete = livePlan == null || livePlan.Error is "Draw a longer track" or "Make the loop a little bigger";
                if (livePlan != null && livePlan.Ok && livePlan.Path != null)
                {
                    var line = new List<Vector3>();
                    for (int k = 0; k < livePlan.Path.Count; k += 3) { var q = livePlan.Path[k]; line.Add(new Vector3(q.x, World.Terrain.SurfaceAt(q.x, q.y) + 0.004f, q.y)); }
                    if (livePlan.Closed) line.Add(line[0]);
                    World.Track.ShowPlan(line, false, liveDirection, snapA, snapB);
                    Say(PlanSaid(livePlan), 1f);
                }
                else if (!incomplete)
                {
                    World.Track.ShowPlan(livePlan.Smoothed ?? new List<Vector3>(drawn), true, 0, snapA, snapB);
                    Say("Can't build this: " + livePlan.Error, 1f);
                }
                else
                {
                    World.Track.ShowPreview(drawn, closing, false, snapA, snapB);
                    Say(closing ? "Let go to close the loop" : "Keep drawing...", 1f);
                }
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
                    else if (sig >= 0) { bool red = World.ToggleSignal(sig); Say(SignalSaid(red)); Haptic(0.4f, 0.04f); }
                    else { var bridge = World.CycleBridge(drawn[0]); if (bridge != null) { Say(bridge + " - tap again for another style"); Haptic(0.5f, 0.05f); } }
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
                    bool startOn = plan.Start.Kind != AttachKind.None, finishOn = plan.Finish.Kind != AttachKind.None;
                    int newSwitches = after.Joints.Count - joints;
                    string what = plan.Closed ? $"Loop complete! {after.TotalLength:0.0} m of railway"
                        : startOn && finishOn && newSwitches >= 2 ? "Joined up, with a switch at each end - trains can run between them now"
                        : startOn && finishOn && newSwitches == 1 ? "Joined up, with a new switch - trains can run between them now"
                        : startOn && finishOn ? "Joined up end to end - one line now"
                        : newSwitches > 0 ? "New branch with a switch - it runs the way its arrows point; join its end back to the railway so trains can use it"
                        : startOn || finishOn ? $"Railway extended ({after.TotalLength:0.0} m)"
                        : $"Railway laid ({after.TotalLength:0.0} m) - draw back to the start for a loop, or from an end to carry on";
                    if (after.Crossings.Count > 0 && plan.Start.Kind == AttachKind.None && plan.Finish.Kind == AttachKind.None && !plan.Closed) what += " - with a crossing";
                    if (after.Traps > 0) what = "Joined one way: trains can go across but not back yet - draw a second link the other way (follow the arrows) so they can return";
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
                if (LetGo(input))
                {
                    heldByTrigger = false;
                    var kind = carriedTrain.Kind; bool running = carriedTrain.Running; float speed = carriedTrain.SpeedFactor; int cars = carriedTrain.Carriages;
                    carriedTrain = null;
                    // The pick-up was checkpointed: undo puts it back if the new spot doesn't work.
                    Train placed = null;
                    string error = onTrack ? World.PlaceTrainNoCheckpoint(local, kind, direction, out placed) : "Put trains down on the track";
                    if (error != null) { World.Undo(false); Say(error); Haptic(0.7f, 0.1f); }
                    else { placed.Running = running; placed.SpeedFactor = speed; if (cars >= 0) World.Trains.SetCars(placed, cars); Say("Train moved"); Haptic(0.5f, 0.05f); }
                    RefreshOptions();
                }
                return;
            }
            hoveredTrain = hit ? World.Trains.Near(local, 0.05f) : null;
            if (hoveredTrain != null && hoveredTrain != lastTrain) { lastTrain = hoveredTrain; RefreshOptions(); }
            World.Trains.Highlighted = hoveredTrain;
            int joint = hit && hoveredTrain == null ? World.Track.PickJoint(local, 0.045f) : -1;
            int sig = hit && hoveredTrain == null && joint < 0 ? World.Track.PickSignal(local, 0.03f) : -1;
            if (hoveredTrain != null)
            {
                World.TerrainView.SetBrush(World.Trains.CentrePosition(hoveredTrain), 0.06f, TrainFactory.LiveryColour(hoveredTrain.Livery), 0.9f);
                if (Mathf.Abs(input.LeftStick.y) > 0.4f)
                {
                    hoveredTrain.SpeedFactor = Mathf.Clamp(hoveredTrain.SpeedFactor + input.LeftStick.y * 0.8f * dt, 0.3f, 1.8f);
                    Say($"Speed {hoveredTrain.SpeedFactor * 100f:0} %", 1f);
                }
                // Stick left / right: uncouple / couple a carriage (wagon), one per push.
                carTimer -= dt;
                if (Mathf.Abs(input.LeftStick.x) > 0.6f && carTimer <= 0f)
                {
                    carTimer = 0.45f;
                    int now = TrainSystem.CarsOf(hoveredTrain), want = now + (input.LeftStick.x > 0f ? 1 : -1);
                    World.Checkpoint();
                    string why = World.Trains.SetCars(hoveredTrain, want);
                    int cars = TrainSystem.CarsOf(hoveredTrain);
                    Say(why ?? (TrainSystem.Units(hoveredTrain.Kind) ? $"{cars} {TrainSystem.CarWord(hoveredTrain.Kind, cars)} coupled" : $"{cars} {TrainSystem.CarWord(hoveredTrain.Kind, cars)} behind the engine"));
                    Haptic(why == null ? 0.5f : 0.7f, why == null ? 0.04f : 0.1f);
                }
                if (Mathf.Abs(input.LeftStick.x) < 0.3f) carTimer = 0f;
                if (input.TriggerDown) { hoveredTrain.Running = !hoveredTrain.Running; Say(hoveredTrain.Running ? "All aboard!" : "Train stopping"); Haptic(0.5f, 0.05f); RefreshOptions(); }
                if (input.GripDown) { heldByTrigger = false; PickUpTrain(hoveredTrain); }
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
                if (input.TriggerDown) { bool red = World.ToggleSignal(sig); Say(SignalSaid(red)); Haptic(0.4f, 0.04f); }
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
                    if (input.TriggerDown || input.GripDown) { heldByTrigger = !input.GripDown; PickUpHouse(hover); }
                    return;
                }
            }
            if (!hit)
            {
                ghost.gameObject.SetActive(false); World.TerrainView.HideBrush();
                if (carrying && !heldByTrigger && input.GripUp) { DropCarried(); Say("Point at the land to put it down - it's back where it was", 4f); Haptic(0.7f, 0.1f); RefreshOptions(); }
                return;
            }
            if (BuildGrid.Snap && !Catalog.IsStation(HouseVariant) && HouseVariant is not (Catalog.Harbour or Catalog.Jetty or Catalog.Zebra))
            {
                // On the grid: beside a road, facing it and lined up along it; elsewhere on the grid, in quarter turns.
                var bp = Catalog.Get(HouseVariant);
                var p2 = new Vector2(local.x, local.z);
                if (!bp.Small && World.Paths.AlongRoad(p2, bp.Half.y, out var beside, out float facingRoad)) { p2 = beside; houseRotation = facingRoad; }
                else { p2 = BuildGrid.Point(p2); houseRotation = BuildGrid.Quarter(houseRotation); }
                local = new Vector3(p2.x, World.Terrain.SurfaceAt(p2.x, p2.y), p2.y);
            }
            var candidate = new HouseInstance { x = local.x, z = local.z, rotation = houseRotation, variant = HouseVariant, style = houseStyle };
            string error;
            if (HouseVariant == Catalog.Zebra)
            {
                // A zebra crossing goes across the street nearest the pointer.
                var snapped = World.ZebraSnap(local);
                if (snapped.HasValue) { candidate = snapped.Value; local = new Vector3(candidate.x, local.y, candidate.z); houseRotation = candidate.rotation; error = World.ValidateHouse(candidate); }
                else error = "Point at a street - the crossing goes across it";
            }
            else if (HouseVariant is Catalog.Harbour or Catalog.Jetty)
            {
                // A harbour or a landing stage finds the shore: it turns to face the water and settles on the bank.
                var snapped = World.ShoreSnap(local, HouseVariant, houseStyle);
                if (snapped.HasValue) { candidate = snapped.Value; local = new Vector3(candidate.x, local.y, candidate.z); houseRotation = candidate.rotation; error = null; }
                else error = HouseVariant == Catalog.Harbour ? "Point at the shore of a big lake - a harbour faces open water" : "Point at a pond's edge";
            }
            else if (Catalog.IsStation(HouseVariant))
            {
                // Stations snap beside the rails, facing them, on the side the pointer is on.
                var snapped = World.StationAt(local, houseStyle, HouseVariant);
                if (snapped.HasValue) { candidate = snapped.Value; local = new Vector3(candidate.x, local.y, candidate.z); houseRotation = candidate.rotation; error = World.ValidateHouse(candidate); }
                else error = World.Track.HasTrack ? "Point next to the railway for a station" : "Draw a railway first";
            }
            else error = World.ValidateHouse(candidate);
            ghost.gameObject.SetActive(true);
            ghostFilter.sharedMesh = HouseFactory.Get(HouseVariant, houseStyle);
            var wt = World.transform;
            ghost.position = wt.TransformPoint(new Vector3(local.x, World.SitHeightFor(candidate) + 0.004f * Mathf.Sin(Time.time * 6f) + 0.004f, local.z));
            ghost.rotation = wt.rotation * Quaternion.Euler(0f, houseRotation, 0f);
            ghost.localScale = wt.lossyScale;
            ShowFacing(new Vector3(local.x, 0f, local.z), houseRotation, Catalog.Get(HouseVariant).Half.y, HouseVariant == Catalog.Camera);
            ghostBlock.SetColor("_Highlight", error == null ? new Color(0.6f, 1f, 0.5f, 0.18f) : new Color(1f, 0.3f, 0.25f, 0.45f));
            ghostRenderer.SetPropertyBlock(ghostBlock);
            World.TerrainView.SetBrush(local, BuildingSystem.Radius(HouseVariant) * 0.9f, error == null ? new Color(0.6f, 1f, 0.5f) : new Color(1f, 0.4f, 0.35f), 0.6f);
            bool gripped = carrying && !heldByTrigger;
            if (input.TriggerDown || gripped && input.GripUp)
            {
                if (error != null && gripped && input.GripUp)
                {
                    // Let go where it can't go: back where it was.
                    DropCarried();
                    Say(error + " - it's back where it was", 4f); Haptic(0.7f, 0.1f);
                    RefreshOptions();
                }
                else if (error == null)
                {
                    bool wasCarrying = carrying;
                    World.PlaceHouse(candidate, !carrying);
                    carrying = false; heldByTrigger = false;
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

        /// <summary>Move: the pointer on the floor puts the window's centre there, the stick turns it (X) and lifts it (Y).</summary>
        private void PlaceWorld(PointerInput input, Ray ray, float dt)
        {
            World.TerrainView.HideBrush();
            float floorY = World.PlacementPosition.y - World.Lift;
            var at = World.PlacementPosition;
            if (Mathf.Abs(ray.direction.y) > 1e-3f)
            {
                float t = (floorY - ray.origin.y) / ray.direction.y;
                if (t > 0f && t < 6f)
                {
                    var p = ray.origin + ray.direction * t;
                    at = new Vector3(p.x, floorY, p.z);
                    DrawPointer(ray, t, new Color(0.6f, 0.9f, 1f, 0.9f), true);
                }
            }
            World.Place(new Vector3(at.x, floorY + World.Lift, at.z), World.PlacementRotation);
            if (input.TriggerDown) FinishMoving();
        }

        /// <summary>Move: either stick turns the world (left / right) and lifts it (up / down), wherever the pointer is.</summary>
        private void TurnAndLift(Vector2 stick, float dt)
        {
            var p = World.PlacementPosition;
            float floorY = p.y - World.Lift;
            if (Mathf.Abs(stick.y) > 0.3f) World.Lift = Mathf.Clamp(World.Lift + stick.y * 0.35f * dt, 0f, MaxLift);
            var turn = Mathf.Abs(stick.x) > 0.25f ? Quaternion.Euler(0f, stick.x * 60f * dt, 0f) : Quaternion.identity;
            if (Mathf.Abs(stick.y) > 0.3f || Mathf.Abs(stick.x) > 0.25f) World.Place(new Vector3(p.x, floorY + World.Lift, p.z), turn * World.PlacementRotation);
            if (Mathf.Abs(stick.y) > 0.3f && infoMode == 5 && (Time.frameCount & 7) == 0) ShowWorldPanel();   // (the height shown)
        }

        /// <summary>The highest the world may be lifted off the floor (metres).</summary>
        public const float MaxLift = 1.2f;

        /// <summary>
        /// The World panel (the World button): everything about the world as a whole in one place - Move (it follows the
        /// pointer on the floor until the trigger puts it down), its height (Higher / Lower, Table, Floor; or either stick
        /// up and down while moving), turning it (Turn left / right; or hold grip and push the right stick sideways at
        /// any time), the window's size and the world's scale.
        /// </summary>
        public void OpenWorldPanel()
        {
            infoMode = 5;
            ShowWorldPanel();
            Say("The world as a whole: move it, lift it, turn it, size its window and its scale", 4f);
        }

        /// <summary>Starts moving the world (from the World panel, or Settings > Height).</summary>
        public void StartMoving()
        {
            PlacingWorld = true;
            worldTab = 0;
            if (infoMode != 5) infoMode = 5;
            ShowWorldPanel();
            Say("Point where the world should lie and pull the trigger to put it down", 5f);
        }

        // The World panel's tab: 0 place, 1 turn, 2 height, 3 size.
        private int worldTab;

        /// <summary>
        /// The World panel, in four tabs - Place (move it in the room, or go home), Turn, Height and Size (its window and
        /// scale) - each with a word on what it does and only its own buttons.
        /// </summary>
        private void ShowWorldPanel()
        {
            string height = World.Lift < 0.005f ? "on the floor" : $"{World.Lift * 100f:0} cm above the floor";
            var size = World.WindowSize * World.transform.lossyScale.x;
            var body = new CardText(30);
            var o = new List<PaletteUI.Option>();
            void Lift(float to)
            {
                var p = World.PlacementPosition;
                float floorY = p.y - World.Lift;
                World.Lift = Mathf.Clamp(to, 0f, MaxLift);
                World.Place(new Vector3(p.x, floorY + World.Lift, p.z), World.PlacementRotation);
                if (!PlacingWorld) WorldPlaced?.Invoke();
                ShowWorldPanel();
            }
            void Turn(float degrees) { TurnWorld(degrees); if (!PlacingWorld) WorldPlaced?.Invoke(); ShowWorldPanel(); }
            switch (worldTab)
            {
                case 0:
                    body.Section("Place").Line(PlacingWorld ? "Point at the floor where the world should lie and pull the trigger." : "Move the world to another spot in your room: press Move, point at the floor, pull the trigger.")
                        .Row("Now", height);
                    o.Add(new PaletteUI.Option(PlacingWorld ? "Put it here" : "Move", PlacingWorld, () => { if (PlacingWorld) FinishMoving(); else StartMoving(); }) { Kind = OptionKind.Action, Tip = "The world follows the pointer over the floor; pull the trigger to put it down there." });
                    break;
                case 1:
                    body.Section("Turn").Line("Hold BOTH grips (the side buttons on both controllers) and twist your hands, as if turning a model on a table. Let go to put it down.")
                        .Line("Or turn it a step at a time:", true);
                    o.Add(new PaletteUI.Option("Turn left", false, () => Turn(-15f)) { Kind = OptionKind.Action, Tip = "Turns the world 15 degrees anticlockwise." });
                    o.Add(new PaletteUI.Option("Turn right", false, () => Turn(15f)) { Kind = OptionKind.Action, Tip = "Turns the world 15 degrees clockwise." });
                    break;
                case 2:
                    body.Section("Height").Line("On the floor the whole room is the world; at table height you build standing up.").Row("Now", height);
                    o.Add(new PaletteUI.Option("Floor", World.Lift < 0.005f, () => Lift(0f)) { Tip = "On the floor." });
                    o.Add(new PaletteUI.Option("Table", World.Lift > 0.7f && World.Lift < 0.74f, () => Lift(0.72f)) { Tip = "At table height, 72 cm." });
                    o.Add(new PaletteUI.Option("Lower", false, () => Lift(World.Lift - 0.05f)) { Kind = OptionKind.Action, Tip = "5 cm lower." });
                    o.Add(new PaletteUI.Option("Higher", false, () => Lift(World.Lift + 0.05f)) { Kind = OptionKind.Action, Tip = "5 cm higher." });
                    break;
                default:
                    body.Section("Size").Row("Window", $"{size.x:0.0} x {size.y:0.0} m: how much of your room the world fills" + (World.WindowLimit != null ? $" ({World.WindowLimit})" : ""))
                        .Row("Scale", $"{PerfSettings.WorldScale * 100f:0} %: smaller shows more of the world in the same window");
                    o.Add(new PaletteUI.Option($"Window: {WonderlandWorld.MaxWindow:0.#} m", false, () =>
                    {
                        var sizes = WonderlandWorld.WindowSizes;
                        int at = System.Array.FindIndex(sizes, x => Mathf.Abs(x - WonderlandWorld.MaxWindow) < 0.01f);
                        WonderlandWorld.MaxWindow = sizes[(at + 1) % sizes.Length];
                        World.FitToRoom();
                        var now = World.WindowSize * World.transform.lossyScale.x;
                        Say($"The window: {now.x:0.0} x {now.y:0.0} m" + (World.WindowLimit != null ? $" - {World.WindowLimit}" : ""));
                        ShowWorldPanel();
                    }) { Kind = OptionKind.Action, Tip = "Each press the next size: 2, 3, 4, 1.5 m." });
                    o.Add(new PaletteUI.Option($"Scale: {PerfSettings.WorldScale * 100f:0} %", false, () => { PerfSettings.WorldSize.Next(); Say($"The world at {PerfSettings.WorldScale * 100f:0} % - its window stays the same size in the room"); ShowWorldPanel(); })
                        { Kind = OptionKind.Action, Tip = "Each press the next scale. Smaller shows more of the world; bigger brings it closer." });
                    break;
            }
            Palette.ShowInfo("World", PlacingWorld ? "moving - trigger on the floor puts it down" : "the world as a whole, in your room", body.ToString(), CloseWorldPanel);
            var tabs = new List<PaletteUI.Option>();
            foreach (var (label, tip, tab) in new[] { ("Place", "Move it in your room", 0), ("Turn", "Turn it round", 1), ("Height", "Floor, table or in between", 2), ("Size", "Its window and its scale", 3) })
            {
                int t = tab;
                tabs.Add(new PaletteUI.Option(label, worldTab == t, () => { worldTab = t; ShowWorldPanel(); }) { Tip = tip });
            }
            Palette.SetInfoTabs(tabs);
            Palette.SetInfoButtons(o);
        }

        /// <summary>Turns the whole world about the window's middle (it stays where it is in the room).</summary>
        public void TurnWorld(float degrees)
        {
            World.Place(World.PlacementPosition, Quaternion.Euler(0f, degrees, 0f) * World.PlacementRotation);
            World.FitToRoom();
        }

        /// <summary>The World button: opens the World panel, or closes it (putting the world down if it was moving).</summary>
        public void ToggleMoving() { if (infoMode == 5) CloseWorldPanel(); else OpenWorldPanel(); }

        private void CloseWorldPanel()
        {
            if (PlacingWorld) FinishMoving();
            if (infoMode == 5) CloseInfo();
        }

        /// <summary>Puts the world down where it is (Move).</summary>
        private void FinishMoving()
        {
            if (!PlacingWorld) return;
            PlacingWorld = false;
            World.FitToRoom(); WorldPlaced?.Invoke();
            Say(World.Lift < 0.005f ? "World placed - it stays on this spot" : $"World placed, {World.Lift * 100f:0} cm above the floor - it stays on this spot");
            Haptic(0.5f, 0.06f);
            if (infoMode == 5) ShowWorldPanel();
        }

        // Grabbing the world with both hands: where the hands were and how the world lay when both grips closed.
        private bool twisting;
        private float twistFrom;
        private Quaternion twistWorld;

        /// <summary>
        /// Both grips held (nothing in hand): the world is grabbed with both hands and turns as they turn round each other -
        /// like turning a model on a table by its edges - about the window's middle; letting go of either grip puts it
        /// down there. True while it is held so.
        /// </summary>
        private bool Twist(PointerInput input)
        {
            bool both = input.Grip && input.LeftGrip && !Holding && input.LeftHand != Vector3.zero && input.RightHand != Vector3.zero;
            if (!both)
            {
                if (twisting) { twisting = false; World.FitToRoom(); WorldPlaced?.Invoke(); Haptic(0.4f, 0.05f); }
                return false;
            }
            var d = input.RightHand - input.LeftHand;
            float angle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            if (!twisting)
            {
                twisting = true; twistFrom = angle; twistWorld = World.PlacementRotation;
                Haptic(0.5f, 0.05f);
                Say("Turning the world: twist your hands, let go to put it down", 3f);
                twistSaid = true;
                return true;
            }
            World.Place(World.PlacementPosition, Quaternion.Euler(0f, Mathf.DeltaAngle(twistFrom, angle), 0f) * twistWorld);
            return true;
        }

        /// <summary>Whether the world has been turned with both hands yet (else a tip offers it).</summary>
        private bool twistSaid;

        /// <summary>Grip held (nothing in hand) and the right stick sideways: the world turns, like a turntable.</summary>
        private void GripTurn(PointerInput input, float dt)
        {
            if (!input.Grip || Mathf.Abs(input.Stick.x) < 0.25f || Holding) { if (gripTurned && !input.Grip) { gripTurned = false; World.FitToRoom(); WorldPlaced?.Invoke(); } return; }
            gripTurned = true;
            World.Place(World.PlacementPosition, Quaternion.Euler(0f, input.Stick.x * 60f * dt, 0f) * World.PlacementRotation);
        }

        private bool gripTurned;

        // ------------------------------------------------------------------ scrolling

        /// <summary>
        /// The right stick scrolls the world: pushed away, the window travels forward over the land (the way the player
        /// faces), left and right likewise. Gentle near the middle, quick at full tilt.
        /// </summary>
        private void Scroll(Vector2 stick, float dt)
        {
            float tilt = Mathf.Clamp01((stick.magnitude - 0.15f) / 0.8f);
            if (tilt <= 0f || !World.Windowed) return;
            var head = Head != null ? Head : Camera.main != null ? Camera.main.transform : null;
            var wt = World.transform;
            var forward = head != null ? Vector3.ProjectOnPlane(head.forward, wt.up) : wt.forward;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(head != null ? head.up : wt.forward, wt.up);
            var f = wt.InverseTransformDirection(forward); var fwd = new Vector2(f.x, f.z).normalized;
            var right = new Vector2(fwd.y, -fwd.x);
            var dir = (right * stick.x + fwd * stick.y).normalized;
            World.Pan(dir * tilt * tilt * WonderlandWorld.PanSpeed * Mathf.Max(World.WindowSize.x, World.WindowSize.y), dt);
            if (!scrollSaid) { scrollSaid = true; Say("Scrolling the world - the map shows where you are", 3f); }
        }

        private bool scrollSaid;

        // ------------------------------------------------------------------ named worlds

        /// <summary>Save (or load) is open: the item row lists the worlds. Set by the app (it knows the materials).</summary>
        private int worldMenu;          // 0 closed, 1 save, 2 load
        private int armedSlot;
        public System.Action<int> SaveWorld, LoadWorld;

        /// <summary>
        /// The Worlds panel (the Worlds button): Save, Load and New as tabs. Save and Load list the six worlds - which hold
        /// a world, which is open now - as buttons; saving over another world takes a second tap. New starts a fresh
        /// landscape (a second tap, too). <paramref name="save"/> opens it at Save (else Load); pressing it again closes it.
        /// </summary>
        public void ShowWorlds(bool save)
        {
            if (infoMode == 6 && worldMenu == (save ? 1 : 2)) { CloseInfo(); return; }
            infoMode = 6; worldMenu = save ? 1 : 2; armedSlot = 0;
            ShowWorldsPanel();
        }

        /// <summary>The Worlds button: opens the panel (at Save) or closes it.</summary>
        public void ToggleWorlds() { if (infoMode == 6) CloseInfo(); else ShowWorlds(true); }

        private void ShowWorldsPanel()
        {
            int current = WonderlandWorld.CurrentSlot;
            var body = new CardText(30);
            body.Section(worldMenu == 1 ? "Save" : worldMenu == 2 ? "Load" : "New world");
            if (worldMenu == 1) body.Line("Keep this world in one of six places. From then on it keeps itself up to date there. Saving over another world needs a second tap.");
            else if (worldMenu == 2) body.Line("Open a world you saved. The one open now is kept as it is.");
            else body.Line("Start again from a fresh landscape. The world open now stays saved if you saved it; otherwise it is lost. Press twice.");
            body.Section("Now").Row("Open", current > 0 ? $"World {current}" : "a world not saved yet", current > 0 ? null : CardText.Warn);
            Palette.ShowInfo("Worlds", "save, load or start a new one", body.ToString(), CloseInfo);
            Palette.SetInfoTabs(new[]
            {
                new PaletteUI.Option("Save", worldMenu == 1, () => { worldMenu = 1; armedSlot = 0; ShowWorldsPanel(); }) { Tip = "Keep this world in one of six places." },
                new PaletteUI.Option("Load", worldMenu == 2, () => { worldMenu = 2; armedSlot = 0; ShowWorldsPanel(); }) { Tip = "Open a world you saved." },
                new PaletteUI.Option("New", worldMenu == 3, () => { worldMenu = 3; ShowWorldsPanel(); }) { Tip = "Start again from a fresh landscape." },
            });
            var o = new List<PaletteUI.Option>();
            if (worldMenu == 3)
                o.Add(new PaletteUI.Option(resetArmed && Time.time < resetUntil ? "Sure? Press again" : "Start a new world", resetArmed && Time.time < resetUntil, () => { RequestReset(); if (infoMode == 6) ShowWorldsPanel(); })
                    { Kind = OptionKind.Danger, Tip = "A fresh landscape. Press twice." });
            else
                for (int n = 1; n <= WonderlandWorld.Slots; n++)
                {
                    int slot = n;
                    bool used = WonderlandWorld.SlotUsed(n);
                    string state = n == current ? "open now" : used ? "saved" : "empty";
                    string label = worldMenu == 1 ? (armedSlot == n ? $"World {n}: over it?" : $"World {n} · {state}") : $"World {n} · {state}";
                    o.Add(new PaletteUI.Option(label, n == current || armedSlot == n, () => PickWorld(slot), worldMenu == 1 || used && n != current)
                        { Kind = worldMenu == 1 && used && n != current ? OptionKind.Danger : OptionKind.Action, Tip = worldMenu == 1 ? (used && n != current ? "Holds another world: saving here replaces it (tap twice)." : "Save this world here.") : used ? "Open this world." : "Nothing saved here yet." });
                }
            Palette.SetInfoButtons(o);
        }

        private void PickWorld(int n)
        {
            bool save = worldMenu == 1;
            if (save && WonderlandWorld.SlotUsed(n) && n != WonderlandWorld.CurrentSlot && armedSlot != n)
            {
                armedSlot = n;
                Say($"{WonderlandWorld.SlotLabel(n)} holds another world - tap it again to save over it", 4f);
                ShowWorldsPanel();
                return;
            }
            armedSlot = 0;
            if (save) SaveWorld?.Invoke(n); else LoadWorld?.Invoke(n);
            if (infoMode == 6) ShowWorldsPanel();
            RefreshOptions();
        }

        public void RequestReset()
        {
            if (resetArmed) { resetArmed = false; ResetRequested?.Invoke(); return; }
            resetArmed = true; resetUntil = Time.time + 3f;
            Say("Press New world again to start a fresh landscape", 3f);
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
            ShowLabel(end, ray.origin);
        }
    }
}
