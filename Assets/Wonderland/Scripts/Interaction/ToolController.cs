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
    public sealed partial class ToolController : MonoBehaviour
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
        /// <summary>The world was put down somewhere new (Move): the app pins it to the room there.</summary>
        public System.Action WorldPlaced;
        public bool HapticsEnabled = true;

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

        /// <summary>The engine runs round to the other end of the train (a train running backwards then runs engine first).</summary>
        private void TurnRound(Train t)
        {
            if (t == null || !World.Trains.Trains.Contains(t)) { Say("Point at a train first, then press Turn round"); return; }
            World.Checkpoint();
            World.Trains.TurnRound(t);
            Say(t.Kind == TrainKind.Railcar ? "The driver changes ends" : "The engine runs round to the other end of its train");
            Haptic(0.5f, 0.05f);
        }

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
            if (carrying) DropCarried();
            if (carriedTrain != null) { carriedTrain = null; World.Undo(false); }
            if (tool != Tool) CloseCard();
            Tool = tool;
            Palette.SetTool(tool);
            World.Cctv.Watching = tool == Tool.Watch;
            if (infoMode == 2) ShowHelp();
            Palette.SetViewer(tool == Tool.Watch ? World.Cctv.ViewerMaterial : null);
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
                    foreach (var (kind, label) in new[] { (TrainKind.Passenger, "Steam"), (TrainKind.Express, "Express"), (TrainKind.Tank, "Tank"), (TrainKind.Goods, "Goods"), (TrainKind.Railcar, "Railcar") })
                    {
                        var k = kind;
                        items.Add(new PaletteUI.Option(label, TrainKind == k, () => { TrainKind = k; RefreshOptions(); }));   // (the second row: which train)
                    }
                    // Carriages for the train last pointed at.
                    var chosen = lastTrain != null && World.Trains.Trains.Contains(lastTrain) ? lastTrain : null;
                    o.Add(new PaletteUI.Option("- Carriage", false, () => Couple(chosen, -1), chosen != null));
                    o.Add(new PaletteUI.Option("+ Carriage", false, () => Couple(chosen, 1), chosen != null));
                    o.Add(new PaletteUI.Option("Turn round", false, () => TurnRound(chosen), chosen != null));
                    bool running = World.Trains.AnyRunning;
                    o.Add(new PaletteUI.Option(running ? "Stop all" : "Start all", running, () => { ToggleAll(); }, World.Trains.Any));
                    o.Add(new PaletteUI.Option("Switches: " + (World.Trains.Mode == SwitchMode.Explore ? "Spread" : World.Trains.Mode.ToString()), false, () =>
                    {
                        World.Trains.Mode = (SwitchMode)(((int)World.Trains.Mode + 1) % 3);
                        Say(World.Trains.Mode switch
                        {
                            SwitchMode.Explore => "Switches spread the trains: each goes the way with fewer trains ahead, so they cover the whole railway",
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
                    // The village clock: let the days go by, hurry them, or stop the time of day.
                    foreach (var (mode, label, said) in new[] { (ClockMode.Normal, "Days pass", "Days pass - the village keeps its routine"), (ClockMode.Fast, "Fast days", "Fast days - watch a whole day go by"), (ClockMode.Paused, "Stop time", "Time stands still - it stays") })
                    {
                        var m = mode; string s = said;
                        items.Add(new PaletteUI.Option(label, World.Clock.Mode == m, () => { World.Clock.Mode = m; Say(m == ClockMode.Paused ? $"{s} {World.Clock.Label} ({VillageClock.PartName(World.Clock.Part)})" : s); RefreshOptions(); }));
                    }
                    // A fête: the whole village gathers (the first decider from outside the villagers' own routine).
                    items.Add(new PaletteUI.Option("Fête", World.Fete != null, () => { Say(World.HoldFete(), 4f); RefreshOptions(); }));
                    // An emergency now (the ambulance, police or fire engine sets off), and the clouds on or off.
                    items.Add(new PaletteUI.Option("Emergency", World.Traffic.Active != null, () => { Say(World.Traffic.RaiseIncident(), 4f); RefreshOptions(); }));
                    items.Add(new PaletteUI.Option("Clouds", World.Sky.Shown, () => { World.Sky.Shown = !World.Sky.Shown; Say(World.Sky.Shown ? "Clouds drift over the land" : "Clear skies"); RefreshOptions(); }));
                    items.Add(new PaletteUI.Option("Rain", World.Sky.Raining, () => { if (!World.Sky.Shown) World.Sky.Shown = true; Say(World.Sky.ToggleRain()); RefreshOptions(); }));
                    break;
                case Tool.Watch:
                {
                    var cctv = World.Cctv;
                    if (cctv.Feeds == 0) o.Add(new PaletteUI.Option("Put up a camera: Build, Small, Camera", false, null, false));
                    for (int k = 0; k < cctv.Feeds && k < 10; k++)
                    {
                        int cam = k;
                        o.Add(new PaletteUI.Option(k < cctv.Count ? $"Cam {k + 1}" : $"Train {k - cctv.Count + 1}", cctv.Selected == k, () => { cctv.Selected = cam; Say(cctv.NameOf(cam)); RefreshOptions(); }));
                    }
                    break;
                }
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
            if (worldMenu != 0) { items.Clear(); WorldItems(items); }
            Palette.SetOptions(o);
            Palette.SetItems(items);
            if (infoMode == 2) ShowHelp();
            Palette.SetTitle(WonderlandWorld.CurrentSlot > 0 ? $"Wonderland · World {WonderlandWorld.CurrentSlot}" : "Wonderland");
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
                : World.Track.HasTrack ? (World.Bridges.Bridges.Count > 0 ? "Tap a bridge to change its style  ·  draw from the track to branch, from an end to extend  ·  tap a switch to throw it  ·  grip: bend track"
                    : "Draw from the track to branch off, from an end to extend, back onto track to join  ·  tap a switch to throw it  ·  grip: bend track  ·  A: signals")
                : "Hold trigger and draw a railway - come back to the start for a loop (over bare floor is fine)",
            Tool.Train => !World.Track.HasTrack ? "Draw a railway first (Track tool)"
                : hoveredTrain != null ? $"{World.Trains.Describe(hoveredTrain)}  ·  trigger: stop / go  ·  stick up-down: speed, left-right: carriages  ·  grip: move it"
                : "Trigger on the track: add a train  ·  tap a switch or signal to change it  ·  A: start / stop all",
            Tool.Paths => PathSystem.IsWay((int)PathKind) ? $"Draw a {PathSystem.Names[(int)PathKind].ToLower()} - across water it gets a bridge (tap a bridge to change it); start on another way to join  ·  A: next"
                : $"Draw a {PathSystem.Names[(int)PathKind].ToLower()} - close it into a loop for a pasture that keeps animals in  ·  A: next",
            Tool.Houses => pickedHover >= 0 && !carrying && pickedHover < World.Buildings.Houses.Count ? World.People.DescribePlace(pickedHover) + "  ·  trigger: move it"
                : Catalog.IsStation(HouseVariant) ? "Point beside the railway: the station snaps to it and the train will stop there"
                : !carrying && Catalog.Get(HouseVariant).Hint != null ? $"Trigger: place the {Catalog.Get(HouseVariant).Name.ToLower()} - {Catalog.Get(HouseVariant).Hint}  ·  stick: turn ({houseRotation:0}°, the arrow is its front)  ·  A: next"
                : carrying ? "Trigger: put the house down  ·  stick: rotate" : "Trigger: place  ·  stick: rotate  ·  A: next house  ·  grip / trigger on a house: move it",
            Tool.Decor => hoverPerson >= 0 && hoverPerson < World.People.People.Count ? World.People.Describe(hoverPerson) + "  ·  trigger: their card"
                : hoverAnimal >= 0 && hoverAnimal < World.Animals.Animals.Count ? AnimalLine(hoverAnimal) + "  ·  trigger: its card"
                : hoverVehicle != null && World.Traffic.Vehicles.Contains(hoverVehicle) ? World.Traffic.Describe(hoverVehicle)
                : DecorKind == Villager ? $"{World.Clock.Label}, {VillageClock.PartName(World.Clock.Part)}  ·  Trigger: place a villager - point at one to see what they're up to  ·  A: next"
                : DecorKind == (int)AnimalKind.Duck ? "Trigger on a pond: ducks paddle about  ·  A: next"
                : DecorKind == (int)AnimalKind.Deer ? "Trigger near the woods: deer stay among the trees and shy away from people  ·  A: next"
                : $"Trigger: place a {AnimalFactory.Names[DecorKind].ToLower()} - hold and sweep for a herd; inside a fence they stay in their pasture  ·  A: next",
            Tool.Watch => World.Cctv.Feeds == 0 ? "No cameras yet: put one up with Build, Small, Camera (its front faces the way it looks), or put a train on the track"
                : hoverCamera >= 0 ? $"{World.Cctv.NameOf(hoverCamera)}  ·  trigger: watch it"
                : World.Cctv.Selected >= World.Cctv.Count ? $"Watching {World.Cctv.NameOf(World.Cctv.Selected)}  ·  trigger on a camera: watch it  ·  A: next camera"
                : $"Watching {World.Cctv.NameOf(World.Cctv.Selected)}  ·  stick: turn and tilt it  ·  trigger on a camera: watch it  ·  trigger on the land: point it there  ·  A: next camera",
            _ => "Tap a house, an animal, a villager, a train, a signal or a piece of track to erase  ·  hold over trees to clear them",
        };

        private void Update()
        {
            if (World == null || Palette == null) return;
            var input = InputOverride != null ? InputOverride() : quest.Read(RightHand);
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
            Palette.SetHint(PlacingWorld ? "Point at the floor, stick to rotate, trigger to place the world here" : Peek() ?? Hint());
            UpdateInspection(dt);
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
            if (Tool == Tool.Houses)
            {
                // Turning what is being placed: a flick turns it 15 degrees; holding the stick over turns it smoothly.
                if (Mathf.Abs(stick.x) > 0.6f)
                {
                    if (turnHeld == 0f) houseRotation = Mathf.Round((houseRotation + Mathf.Sign(stick.x) * 15f) / 15f) * 15f;
                    else if (turnHeld > 0.45f) houseRotation += stick.x * 60f * dt;
                    turnHeld += dt;
                }
                else turnHeld = 0f;
                houseRotation = Mathf.Repeat(houseRotation, 360f);
            }
            else if (Mathf.Abs(stick.x) > 0.25f && Tool != Tool.Watch) radius[(int)Tool] = Mathf.Clamp(Radius * (1f + stick.x * 1.2f * dt), 0.04f, 0.4f);
            if (Mathf.Abs(stick.y) > 0.4f && Tool != Tool.Train && Tool != Tool.Watch)
            {
                if (Tool == Tool.Forest) { density = Mathf.Clamp01(density + stick.y * 0.6f * dt); }
                else strength = Mathf.Clamp(strength + stick.y * 0.6f * dt, 0.15f, 1f);
            }
            if (input.PrimaryDown) CycleMode();

            if (hit) { rayLength = Vector3.Distance(ray.origin, wt.TransformPoint(local)); }
            // Whoever is being pointed at, in any tool: grip opens their card.
            peekPerson = hit && !stroking ? World.People.Nearest(local, 0.015f) : -1;
            peekAnimal = hit && !stroking && peekPerson < 0 ? World.Animals.Nearest(local, 0.02f) : -1;
            if (input.GripDown && (peekPerson >= 0 || peekAnimal >= 0) && carriedTrain == null && !carrying) { Inspect(peekPerson, peekAnimal); DrawPointer(ray, rayLength, new Color(1f, 0.85f, 0.3f, 0.9f), true); return; }
            // ...and a building: grip (trigger in the Life tool) opens its card - not in Build or Eraser, where these pick up or remove.
            peekBoat = hit && !stroking && peekPerson < 0 && peekAnimal < 0 ? World.Boats.Nearest(local, 0.015f) : -1;
            peekHouse = !stroking && peekPerson < 0 && peekAnimal < 0 && peekBoat < 0 && Tool != Tool.Houses && Tool != Tool.Eraser ? World.Buildings.Pick(localOrigin, localDir) : -1;
            if (peekHouse >= 0 && carriedTrain == null && !carrying && (input.GripDown || Tool == Tool.Decor && input.TriggerDown))
            {
                InspectHouse(peekHouse);
                DrawPointer(ray, rayLength, new Color(1f, 0.85f, 0.3f, 0.9f), true);
                return;
            }
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
                case Tool.Watch: WatchTool(input, hit, local, dt, ref colour); break;
                case Tool.Eraser: EraserTool(input, hit, local, localOrigin, localDir, dt, ref colour); break;
            }
            if (Tool != Tool.Houses) ghost.gameObject.SetActive(false);
            if (facing != null && (Tool != Tool.Houses || !ghost.gameObject.activeSelf)) facing.enabled = false;
            if (Tool != Tool.Track || drawn.Count == 0) HideEntryArrows();
            if (Tool != Tool.Train) { hoveredTrain = null; if (World.Trains.Highlighted != null) World.Trains.Highlighted = null; }
            floorRing.gameObject.SetActive(LandMode && hit);
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
                case Tool.Paths: PathKind = (PathKind)(((int)PathKind + 1) % PathSystem.Names.Length); Say(PathSystem.Names[(int)PathKind]); break;
                case Tool.Houses:
                {
                    var ids = Catalog.InCategory(BuildCategory);
                    HouseVariant = ids[(ids.IndexOf(HouseVariant) + 1) % ids.Count];
                    Say(HouseFactory.Name(HouseVariant)); break;
                }
                case Tool.Train: ToggleTrain(); break;
                case Tool.Track: SignalMode = !SignalMode; Say(SignalMode ? "Signals" : "Draw track"); break;
                case Tool.Watch:
                    if (World.Cctv.Feeds > 0) { World.Cctv.Selected = (World.Cctv.Selected + 1) % World.Cctv.Feeds; Say(World.Cctv.NameOf(World.Cctv.Selected)); }
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

        /// <summary>Watch: pick a camera to watch (trigger on it), or point the watched one at a spot (trigger on the land).</summary>
        private void WatchTool(PointerInput input, bool hit, Vector3 local, float dt, ref Color colour)
        {
            var cctv = World.Cctv;
            World.TerrainView.HideBrush();
            hoverCamera = hit ? cctv.NearestCamera(new Vector2(local.x, local.z), 0.035f) : -1;
            colour = hoverCamera >= 0 ? new Color(0.55f, 0.9f, 1f, 0.9f) : new Color(1f, 0.95f, 0.75f, 0.85f);
            // The stick turns (left/right) and tilts (up/down) the camera being watched.
            int watched = cctv.Selected;
            if (watched >= 0 && watched < cctv.Count && input.Stick.sqrMagnitude > 0.09f)
            {
                cctv.Turn(watched, input.Stick.x * 70f * dt, -input.Stick.y * 40f * dt);
                turnSaid -= dt;
                if (turnSaid <= 0f) { turnSaid = 0.4f; var a = cctv.AimOf(watched); Say($"{cctv.NameOf(watched)}: facing {Compass(a.yaw)}, {a.pitch:0}° down", 1f); }
            }
            if (!input.TriggerDown || !hit) return;
            if (hoverCamera >= 0) { cctv.Selected = hoverCamera; Say(cctv.NameOf(hoverCamera)); RefreshOptions(); Haptic(0.4f, 0.03f); return; }
            int k = cctv.Selected;
            if (k < 0) { Say("Put up a camera first: Build, Small, Camera"); return; }
            cctv.AimAt(k, local);
            Say($"{cctv.NameOf(k)} now looks this way");
            Haptic(0.4f, 0.03f);
        }

        private void DecorTool(PointerInput input, bool hit, Vector3 local, float dt, ref Color colour)
        {
            bool person = DecorKind == Villager;
            var kind = (AnimalKind)Mathf.Min(DecorKind, AnimalFactory.KindCount - 1);
            float radius = person ? PeopleSystem.Radius * 2f : AnimalFactory.Radius(kind);
            var p2 = new Vector2(local.x, local.z);
            hoverPerson = hit ? World.People.Nearest(local, 0.015f) : -1;
            hoverAnimal = hit && hoverPerson < 0 ? World.Animals.Nearest(local, 0.02f) : -1;
            hoverVehicle = hit && hoverPerson < 0 && hoverAnimal < 0 ? World.Traffic.Nearest(p2, 0.03f) : null;
            // Trigger on someone who is already there: their card (and follow them), not another one.
            if (input.TriggerDown && hit && (hoverPerson >= 0 || hoverAnimal >= 0)) { Inspect(hoverPerson, hoverAnimal); return; }
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
                int bridge = hit ? World.Bridges.Pick(new Vector2(local.x, local.z)) : -1;
                if (bridge >= 0) { var a = World.Bridges.Bridges[bridge].Anchor; World.TerrainView.SetBrush(new Vector3(a.x, local.y, a.y), 0.05f, new Color(0.55f, 0.85f, 1f), 0.95f); }
                else if (hit) World.TerrainView.SetBrush(local, width, new Color(1f, 0.9f, 0.6f), 0.9f); else World.TerrainView.HideBrush();
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
                    if (sig >= 0) { bool red = World.ToggleSignal(sig); Say(SignalSaid(red)); Haptic(0.4f, 0.04f); }
                    else
                    {
                        string error = World.PlaceSignal(local);
                        if (error != null) { Say(error); Haptic(0.7f, 0.1f); } else { Say("Signal up: on automatic, red while a train is on the line beside it - tap it to hold trains at red"); Haptic(0.5f, 0.05f); }
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
                    int bridge = World.Bridges.Pick(new Vector2(local.x, local.z));
                    if (a.Kind != AttachKind.None) World.TerrainView.SetBrush(new Vector3(a.Point.x, local.y, a.Point.y), 0.022f, new Color(0.45f, 0.9f, 1f), 0.95f);
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
                if (input.GripUp || !input.Grip)
                {
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
                if (Mathf.Abs(input.Stick.y) > 0.4f)
                {
                    hoveredTrain.SpeedFactor = Mathf.Clamp(hoveredTrain.SpeedFactor + input.Stick.y * 0.8f * dt, 0.3f, 1.8f);
                    Say($"Speed {hoveredTrain.SpeedFactor * 100f:0} %", 1f);
                }
                // Stick left / right: uncouple / couple a carriage (wagon), one per push.
                carTimer -= dt;
                if (Mathf.Abs(input.Stick.x) > 0.6f && carTimer <= 0f)
                {
                    carTimer = 0.45f;
                    int now = TrainSystem.CarsOf(hoveredTrain), want = now + (input.Stick.x > 0f ? 1 : -1);
                    World.Checkpoint();
                    string why = World.Trains.SetCars(hoveredTrain, want);
                    int cars = TrainSystem.CarsOf(hoveredTrain);
                    Say(why ?? (hoveredTrain.Kind == TrainKind.Railcar ? $"{cars} {TrainSystem.CarWord(hoveredTrain.Kind, cars)} coupled" : $"{cars} {TrainSystem.CarWord(hoveredTrain.Kind, cars)} behind the engine"));
                    Haptic(why == null ? 0.5f : 0.7f, why == null ? 0.04f : 0.1f);
                }
                if (Mathf.Abs(input.Stick.x) < 0.3f) carTimer = 0f;
                if (input.TriggerDown) { hoveredTrain.Running = !hoveredTrain.Running; Say(hoveredTrain.Running ? "All aboard!" : "Train stopping"); Haptic(0.5f, 0.05f); RefreshOptions(); }
                if (input.GripDown)
                {
                    World.Checkpoint();
                    carriedTrain = new Train { Kind = hoveredTrain.Kind, Running = hoveredTrain.Running, SpeedFactor = hoveredTrain.SpeedFactor, Carriages = hoveredTrain.Carriages };
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
            if (Catalog.IsStation(HouseVariant))
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
            ShowFacing(new Vector3(local.x, 0f, local.z), houseRotation, Catalog.Get(HouseVariant).Half.y, HouseVariant == Catalog.Camera);
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
            if (input.TriggerDown) { PlacingWorld = false; World.FitToRoom(); WorldPlaced?.Invoke(); Say("World placed - it stays on this spot"); Haptic(0.5f, 0.06f); }
        }

        // ------------------------------------------------------------------ named worlds

        /// <summary>Save (or load) is open: the item row lists the worlds. Set by the app (it knows the materials).</summary>
        private int worldMenu;          // 0 closed, 1 save, 2 load
        private int armedSlot;
        public System.Action<int> SaveWorld, LoadWorld;

        /// <summary>Opens the list of worlds to save into (<paramref name="save"/>) or load from; pressing the same button again closes it.</summary>
        public void ShowWorlds(bool save)
        {
            int menu = save ? 1 : 2;
            worldMenu = worldMenu == menu ? 0 : menu;
            armedSlot = 0;
            if (worldMenu != 0) Say(save ? "Save as which world? Tap one (a saved world needs a second tap to save over it)" : "Load which world?", 5f);
            RefreshOptions();
        }

        private void WorldItems(List<PaletteUI.Option> items)
        {
            bool save = worldMenu == 1;
            for (int n = 1; n <= WonderlandWorld.Slots; n++)
            {
                int slot = n;
                bool used = WonderlandWorld.SlotUsed(n);
                string label = $"World {n}";
                items.Add(new PaletteUI.Option(label, armedSlot == n || n == WonderlandWorld.CurrentSlot, () => PickWorld(slot), save || used));
            }
            items.Add(new PaletteUI.Option("Cancel", false, () => { worldMenu = 0; RefreshOptions(); }));
        }

        private void PickWorld(int n)
        {
            bool save = worldMenu == 1;
            if (save && WonderlandWorld.SlotUsed(n) && n != WonderlandWorld.CurrentSlot && armedSlot != n)
            {
                armedSlot = n;
                Say($"{WonderlandWorld.SlotLabel(n)} holds another world - tap it again to save over it", 4f);
                RefreshOptions();
                return;
            }
            worldMenu = 0; armedSlot = 0;
            if (save) SaveWorld?.Invoke(n); else LoadWorld?.Invoke(n);
            RefreshOptions();
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
