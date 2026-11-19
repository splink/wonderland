using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Getting to know the world: the character card (trigger on a villager or an animal with the Life tool), a marker
    /// over whoever is followed with a line along the way they are going and pins over their home and work, the Stats
    /// panel (frame rate, CPU and GPU time, what the simulation costs, what the world holds) and the Help panel (the
    /// controls of the tool in hand). Who is followed is kept by their seed, so they are not lost when others come and go.
    /// Nothing here runs while the card and panels are closed.
    /// </summary>
    public sealed partial class ToolController
    {
        private int hoverAnimal = -1, peekPerson = -1, peekAnimal = -1, peekHouse = -1, peekBoat = -1;
        private TrafficSystem.Vehicle peekVehicle, followVehicle;
        private Train peekTrain, followTrain;
        private bool trainCamera;                   // the followed train's card shows its camera

        /// <summary>"Emma, baker - grip: their card", when pointing at someone with any tool but Life (which says more).</summary>
        private string peekText; private int peekKey = -2; private float peekUntil;

        private string Peek()
        {
            int key = peekPerson >= 0 ? peekPerson : peekAnimal >= 0 ? 100000 + peekAnimal : peekVehicle != null ? 400000 + peekVehicle.Id
                : peekTrain != null ? 500000 + World.Trains.Trains.IndexOf(peekTrain) : peekBoat >= 0 ? 300000 + peekBoat : peekHouse >= 0 ? 200000 + peekHouse : -1;
            if (key == peekKey && Time.time < peekUntil) return peekText;   // (worked out a few times a second, not every frame)
            peekKey = key; peekUntil = Time.time + 0.3f;
            return peekText = PeekNow();
        }

        private string PeekNow()
        {
            if (peekPerson >= 0 && peekPerson < World.People.People.Count)
            {
                var c = World.People.CardOf(peekPerson);
                return $"{c.Name}, {c.Role.ToLower()} - {c.Now.ToLower()}" + PeekDo(true, "their card");
            }
            if (peekAnimal >= 0 && peekAnimal < World.Animals.Animals.Count) return AnimalLine(peekAnimal) + PeekDo(true);
            if (peekVehicle != null) return World.Traffic.Describe(peekVehicle) + PeekDo(false);
            if (peekTrain != null) return World.Trains.Describe(peekTrain) + PeekDo(true);
            if (peekBoat >= 0 && peekBoat < World.Boats.Boats.Count) return World.Boats.Describe(peekBoat);
            if (peekHouse >= 0 && peekHouse < World.Buildings.Houses.Count)
            {
                var c = World.People.PlaceCardOf(peekHouse);
                string state = c.Home ? (c.Living > 0 ? $"{c.Living} live here" : "nobody lives here yet") : c.Status != null ? c.Status.ToLower() : null;
                string crowd = !c.Home && c.Visitors > 0 ? $"{c.Visitors} here" : null;
                return string.Join(" · ", new[] { c.Name, state, crowd }.Where(x => x != null)) + PeekDo(true);
            }
            return null;
        }
        /// <summary>What a kind of place takes part in, in a few words ("makes produce · its lorry delivers it").</summary>
        private static string PlaceRoles(Blueprint bp)
        {
            var parts = new List<string>();
            static string Cargo(Cargo c) => c switch { Wonderland.Cargo.Produce => "produce", Wonderland.Cargo.Goods => "goods", Wonderland.Cargo.Timber => "timber", Wonderland.Cargo.Mail => "post", _ => "produce and goods" };
            if (bp.Makes != Wonderland.Cargo.None) parts.Add("makes " + Cargo(bp.Makes));
            if (bp.Takes != Wonderland.Cargo.None) parts.Add("takes in " + Cargo(bp.Takes));
            if (bp.Lorries > 0) parts.Add(bp.Lorries == 1 ? "a lorry is based here" : $"{bp.Lorries} lorries are based here");
            if (bp.Taxis > 0) parts.Add(bp.Taxis == 1 ? "a taxi waits here" : $"{bp.Taxis} taxis wait here");
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }

        /// <summary>What the buttons do to what is pointed at, for the palette's hint line.</summary>
        private string PeekDo(bool movable, string card = "its card") =>
            Tool == Tool.Select ? $"  ·  trigger: {card}" + (movable ? "  ·  grip: move it" : "") : movable ? "  ·  grip: move it  ·  Select: " + card : "  ·  Select: " + card;

        private int followSeed, followKind;          // kind: 0 nobody, 1 a villager, 2 an animal, 3 a building, 4 a train, 5 a vehicle
        private Vector2 followHouse;                 // the building's position (buildings are kept by where they stand)
        private float cardTimer, infoTimer;
        private int infoMode;                        // 0 closed, 1 stats, 2 help, 3 performance
        private Transform followMarker, homePin, workPin;
        private LineRenderer routeLine;
        private readonly List<Vector3> route = new();
        private readonly List<(Mesh, Matrix4x4)> model = new();
        public PerfLog Perf;
        public bool Following => followKind != 0;
        public int HoverPerson => hoverPerson;
        /// <summary>What grip would select now (tests): a villager, or a building.</summary>
        public int PeekPerson => peekPerson;
        public int PeekHouse => peekHouse;
        /// <summary>Closes the card (as its Close button does): stops following, and the camera.</summary>
        public void CloseCards() => CloseCard();

        private string AnimalLine(int i)
        {
            var c = World.Animals.CardOf(i, PersonName);
            return $"{(c.Name != null ? c.Name + " the " + c.Species.ToLower() : "A " + c.Species.ToLower())} - {c.Now.ToLower()}";
        }

        private string PersonName(int i) => i >= 0 && i < World.People.People.Count ? World.People.NameOf(i) : null;

        /// <summary>Whether selecting a villager turns their camera on (on by default; the card's Camera button remembers).</summary>
        public static bool FollowCamera
        {
            get => PlayerPrefs.GetInt("wonderland.followcam", 1) == 1;
            set { PlayerPrefs.SetInt("wonderland.followcam", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Opens the card of a villager (or animal) and follows them - with their camera, for a villager.</summary>
        private void Inspect(int person, int animal)
        {
            StopCameras();
            if (person >= 0)
            {
                followKind = 1; followSeed = World.People.People[person].seed;
                if (FollowCamera) World.Cctv.FollowSeed = followSeed;
            }
            else { followKind = 2; followSeed = World.Animals.Animals[animal].seed; }
            cardTimer = 0f;
            Haptic(0.4f, 0.03f);
        }

        private void InspectVehicle(TrafficSystem.Vehicle v) { StopCameras(); followKind = 5; followVehicle = v; cardTimer = 0f; Haptic(0.4f, 0.03f); }

        private void InspectTrain(Train t) { StopCameras(); followKind = 4; followTrain = t; cardTimer = 0f; Haptic(0.4f, 0.03f); }

        /// <summary>The card's Camera button: a villager's camera (or a train's) on or off.</summary>
        private void ToggleCamera()
        {
            if (followKind == 1)
            {
                bool on = World.Cctv.FollowSeed != followSeed;
                FollowCamera = on;
                World.Cctv.FollowSeed = on ? followSeed : 0;
                Say(on ? "Following them with their camera - it goes where they go" : "Camera off - their card stays open");
            }
            else if (followKind == 4)
            {
                trainCamera = !trainCamera;
                if (trainCamera) World.Cctv.Selected = World.Cctv.Count + World.Trains.Trains.IndexOf(followTrain);
                Say(trainCamera ? "Riding along at the front of the engine" : "Camera off");
            }
            cardTimer = 0f;
            SyncViewer();
        }

        private void StopCameras() { World.Cctv.FollowSeed = 0; trainCamera = false; SyncViewer(); }

        /// <summary>The CCTV camera whose card is open (its feed index), or -1.</summary>
        private int SelectedCamera()
        {
            if (followKind != 3) return -1;
            int i = Followed();
            return i >= 0 && World.Buildings.Houses[i].variant == Catalog.Camera ? World.Cctv.CameraOn(i) : -1;
        }

        /// <summary>Whether feed <paramref name="k"/> is the one on the monitor now.</summary>
        private bool Watching(int k) => k == World.Cctv.Selected && (SelectedCamera() == k || trainCamera);

        private bool viewerShown;

        private int controlsFor = -2;

        /// <summary>
        /// The monitor over the palette: shown while there is something to watch - a CCTV camera's card (with buttons to
        /// turn it), a train's camera, or a followed villager's.
        /// </summary>
        private void SyncViewer()
        {
            int cam = SelectedCamera();
            if (cam >= 0 && World.Cctv.Selected != cam) World.Cctv.Selected = cam;
            bool show = World.Cctv.FollowSeed != 0 || trainCamera || cam >= 0;
            World.Cctv.Watching = trainCamera || cam >= 0;
            if (show != viewerShown || Palette == null)
            {
                viewerShown = show;
                Palette.SetViewer(show ? World.Cctv.ViewerMaterial : null);
                controlsFor = -2;
            }
            if (show && controlsFor != cam)
            {
                controlsFor = cam;
                Palette.SetViewerControls(cam >= 0 ? TurnCamera : null);
            }
        }

        /// <summary>Opens the card of a building (visitors, hours, staff, goods; residents for a home).</summary>
        private void InspectHouse(int house)
        {
            StopCameras();
            var h = World.Buildings.Houses[house];
            followKind = 3; followHouse = new Vector2(h.x, h.z);
            cardTimer = 0f;
            Haptic(0.4f, 0.03f);
        }

        private void CloseCard()
        {
            followKind = 0; followVehicle = null; followTrain = null;
            StopCameras();
            Palette.SetCardAction(null, false, null);
            Palette.SetCardButtons(null);
            Palette.HideCard();
            if (followMarker) { followMarker.gameObject.SetActive(false); homePin.gameObject.SetActive(false); workPin.gameObject.SetActive(false); routeLine.enabled = false; }
        }

        private int Followed()
        {
            if (followKind == 1) { var list = World.People.People; for (int i = 0; i < list.Count; i++) if (list[i].seed == followSeed) return i; }
            else if (followKind == 2) { var list = World.Animals.Animals; for (int i = 0; i < list.Count; i++) if (list[i].seed == followSeed) return i; }
            else if (followKind == 3) { var list = World.Buildings.Houses; for (int i = 0; i < list.Count; i++) if (Mathf.Abs(list[i].x - followHouse.x) < 1e-4f && Mathf.Abs(list[i].z - followHouse.y) < 1e-4f) return i; }
            else if (followKind == 4) return World.Trains.Trains.IndexOf(followTrain);
            else if (followKind == 5) return World.Traffic.Vehicles.IndexOf(followVehicle);
            else if (followKind >= 6) return FollowedRail();
            return -1;
        }

        private void UpdateInspection(float dt)
        {
            if (infoMode != 0) { infoTimer -= dt; if (infoTimer <= 0f) { infoTimer = 0.5f; if (infoMode == 1) ShowStats(); else if (infoMode == 3) ShowPerf(); } }
            SyncViewer();
            if (followKind == 0) return;
            int i = Followed();
            if (i < 0) { Say(followKind == 1 ? "They have left the world" : followKind == 3 ? "It was taken down" : followKind >= 6 ? "That part of the railway has changed" : "It has gone"); CloseCard(); return; }
            EnsureMarkers();
            var root = World.transform;
            Vector3 head;
            Vector3? home = null, work = null;
            cardTimer -= dt;
            bool refresh = cardTimer <= 0f;
            if (refresh) cardTimer = 0.5f;
            if (followKind == 1)
            {
                var people = World.People;
                head = people.HeadOf(i) + Vector3.up * (people.IsChild(i) ? 0.03f : 0.042f);
                if (refresh)
                {
                    var c = people.CardOf(i);
                    // Out in the household's car: driving somewhere, or parked up at a place they went to.
                    if (people.IsHidden(i) && World.Traffic.DrivenBy(followSeed) is { Rank: < 0 } car)
                    {
                        int at = World.Buildings.Nearest(car.Path.Count > 0 ? car.Path[^1] : car.Position, 0.15f);
                        string place = at >= 0 ? "the " + Catalog.Get(World.Buildings.Houses[at].variant).Name.ToLower() : "town";
                        c.Now = car.State == TrafficSystem.Mode.Driving ? (car.ToHome ? "driving home" : $"driving to {place}") : $"at {place} (came by car)";
                    }
                    PersonModel(c.Style, c.Child);
                    string body = PersonBody(c);
                    if (World.Cctv.FollowSeed == followSeed && World.Cctv.FollowView != null) body = new CardText().Row("Camera", World.Cctv.FollowView) + body;
                    Palette.ShowCard(c.Name, $"{c.Role} · {c.Stage}", body, model, CloseCard);
                    Palette.SetCardAction(World.Cctv.FollowSeed == followSeed ? "Camera on" : "Camera", World.Cctv.FollowSeed == followSeed, ToggleCamera);
                    home = c.HomeAt; work = c.WorkAt;
                    Pin(homePin, home); Pin(workPin, work);
                    people.RouteOf(i, route);
                    routeLine.positionCount = route.Count;
                    for (int k = 0; k < route.Count; k++) routeLine.SetPosition(k, root.TransformPoint(route[k]));
                    routeLine.enabled = route.Count > 1;
                }
            }
            else if (followKind == 3)
            {
                var h = World.Buildings.Houses[i];
                head = new Vector3(h.x, World.Buildings.TopOf(i) + 0.012f, h.z);
                if (refresh)
                {
                    var c = World.People.PlaceCardOf(i);
                    string place = PlaceBody(c, i);
                    // What it takes part in, and what is happening for it now.
                    var does = PlaceRoles(Catalog.Get(h.variant));
                    string deliveries = World.Traffic.DeliveriesFor(i) ?? World.Traffic.PostFor(i) ?? World.Traffic.ParkingFor(i) ?? World.Freight.CraneFor(i) ?? World.Harbours.HarbourFor(i) ?? World.Attractions.ZooFor(i) ?? World.Attractions.StadiumFor(i);
                    if (does != null || deliveries != null)
                    {
                        var roles = new CardText().Section("Activities");
                        if (does != null) roles.Row("Here", does);
                        if (deliveries != null) roles.Row("Now", deliveries, CardText.Good);
                        place = roles + place;
                    }
                    int cam = World.Cctv.CameraOn(i);
                    if (h.variant == Catalog.Camera && cam >= 0)
                    {
                        var aim = World.Cctv.AimOf(cam);
                        place = new CardText().Section("Camera").Row("Looking", $"{Compass(aim.yaw)}, {aim.pitch:0}° down")
                            .Row("Turn it", "the buttons round its picture, or the left stick").Row("Point it", "trigger on the land") + place;
                    }
                    model.Clear();
                    model.Add((HouseFactory.Get(h.variant, h.style), Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f))));
                    Palette.ShowCard(h.variant == Catalog.Camera && cam >= 0 ? World.Cctv.NameOf(cam) : c.Name, c.Status != null ? $"{c.Kind} · {CardText.Tint(c.Status, c.StatusColour)}" : c.Kind, place, model, CloseCard);
                    Palette.SetCardAction(null, false, null);
                    Pin(homePin, null); Pin(workPin, null);
                    routeLine.enabled = false;
                }
            }
            else if (followKind == 4)
            {
                var t = followTrain;
                head = World.Trains.CentrePosition(t) + Vector3.up * 0.07f;
                if (refresh)
                {
                    var said = World.Trains.Describe(t).Split(" - ");
                    model.Clear();
                    if (t.Cars.Length > 0 && t.Cars[0].Body.TryGetComponent<MeshFilter>(out var mf)) model.Add((mf.sharedMesh, Matrix4x4.Rotate(Quaternion.Euler(0f, 150f, 0f))));
                    var body = new CardText().Section("Now").Row("Doing", said.Length > 1 ? said[1] : said[0]).Row("Train", said[0]).Row("Length", $"{TrainSystem.CarsOf(t)} {TrainSystem.CarWord(t.Kind, TrainSystem.CarsOf(t))}");
                    if (trainCamera) body.Row("Camera", "from the front of the engine");
                    Palette.ShowCard(said[0], t.Running ? "running" : "standing", body.ToString(), model, CloseCard);
                    Palette.SetCardAction(trainCamera ? "Camera on" : "Camera", trainCamera, ToggleCamera);
                    Pin(homePin, null); Pin(workPin, null);
                    routeLine.enabled = false;
                }
            }
            else if (followKind == 5)
            {
                var v = followVehicle;
                head = new Vector3(v.Position.x, v.Height + 0.04f, v.Position.y);
                if (refresh)
                {
                    model.Clear();
                    if (v.Root.TryGetComponent<MeshFilter>(out var mf)) model.Add((mf.sharedMesh, Matrix4x4.Rotate(Quaternion.Euler(0f, 150f, 0f))));
                    var said = World.Traffic.Describe(v).Split(" - ");
                    var body = new CardText().Section("Now").Row("Doing", said.Length > 1 ? said[1] : "-");
                    int who = World.People.IndexOfSeed(v.Driver);
                    if (who >= 0) body.Row("Driver", World.People.NameOf(who));
                    Palette.ShowCard(said[0], v.Roams ? "drives about the town" : v.Base >= 0 ? "on call" : "a household's", body.ToString(), model, CloseCard);
                    Palette.SetCardAction(null, false, null);
                    Pin(homePin, null); Pin(workPin, null);
                    routeLine.enabled = false;
                }
            }
            else if (followKind >= 6) RailCard(i, refresh, out head);
            else
            {
                var animals = World.Animals;
                var c = animals.CardOf(i, PersonName);
                head = c.Position + Vector3.up * 0.03f;
                if (refresh)
                {
                    var body = new CardText().Section("Now").Row("Doing", c.Now).Row("Going to", c.Goal).Section("Lives").Row("Home", c.Home);
                    model.Clear();
                    model.Add((AnimalFactory.Body(c.Kind), Matrix4x4.identity));
                    model.Add((AnimalFactory.Head(c.Kind), Matrix4x4.Translate(AnimalFactory.NeckOffset(c.Kind))));
                    Palette.ShowCard(c.Name ?? c.Species, c.Name != null ? c.Species : "wild - no name", body.ToString(), model, CloseCard);
                    Palette.SetCardAction(null, false, null);
                    Pin(homePin, null); Pin(workPin, null);
                    routeLine.positionCount = 2;
                    routeLine.SetPosition(0, root.TransformPoint(c.Position + Vector3.up * 0.004f));
                    routeLine.SetPosition(1, root.TransformPoint((c.Destination ?? c.Position) + Vector3.up * 0.004f));
                    routeLine.enabled = c.Destination.HasValue;
                }
            }
            if (refresh) Palette.SetCardButtons(CardActions(i));
            followMarker.gameObject.SetActive(true);
            followMarker.position = root.TransformPoint(head + Vector3.up * (Mathf.Sin(Time.time * 3f) * 0.003f));
            followMarker.rotation = root.rotation * Quaternion.Euler(0f, Time.time * 90f, 0f);
        }

        private Transform targetRing;

        /// <summary>A soft gold ring on the ground under whatever grip would select now (none: hidden).</summary>
        private void ShowTarget()
        {
            Vector3? at = null; float r = 0.01f;
            if (peekPerson >= 0) { var p = World.People.PovOf(peekPerson); at = new Vector3(p.Eye.x, p.Eye.y - 0.027f, p.Eye.z); r = 0.009f; }
            else if (peekAnimal >= 0) { var q = World.Animals.PositionOf(peekAnimal); at = new Vector3(q.x, World.Terrain.SurfaceAt(q.x, q.y), q.y); r = 0.016f; }
            else if (peekVehicle != null) { at = new Vector3(peekVehicle.Position.x, peekVehicle.Height, peekVehicle.Position.y); r = VehicleFactory.Length(peekVehicle.Kind) * 0.62f; }
            else if (peekTrain != null) { var c = World.Trains.LocoPosition(peekTrain); at = c; r = 0.04f; }
            else if (peekHouse >= 0) { var h = World.Buildings.Houses[peekHouse]; var e = Catalog.Get(h.variant).Half; at = new Vector3(h.x, World.Terrain.SurfaceAt(h.x, h.z), h.z); r = Mathf.Max(e.x, e.y) + 0.01f; }
            if (targetRing == null)
            {
                var b = new MeshBuilder();
                var glow = Pal.Glow(new Color(1f, 0.85f, 0.3f));
                const int seg = 32;
                for (int k = 0; k < seg; k++)
                {
                    float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                    Vector3 P(float a, float rr) => new(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                    b.AddQuad(P(a0, 0.86f), P(a1, 0.86f), P(a1, 1f), P(a0, 1f), glow);   // (wound to face up)
                }
                var go = new GameObject("Target ring");
                go.AddComponent<MeshFilter>().sharedMesh = b.ToMesh("Target ring");
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = World.Materials.Lit; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                targetRing = go.transform;
            }
            targetRing.gameObject.SetActive(at.HasValue);
            if (!at.HasValue) return;
            var wt = World.transform;
            float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.06f;
            targetRing.SetPositionAndRotation(wt.TransformPoint(at.Value + Vector3.up * 0.0015f), wt.rotation);
            targetRing.localScale = Vector3.one * r * pulse * wt.lossyScale.x;
        }

        private void Pin(Transform pin, Vector3? at)
        {
            pin.gameObject.SetActive(at.HasValue);
            if (at.HasValue) pin.position = World.transform.TransformPoint(at.Value);
        }

        private void PersonModel(int style, bool child)
        {
            model.Clear();
            var m = Matrix4x4.Scale(Vector3.one * (child ? 0.72f : 1f));
            model.Add((PersonFactory.Body(style), m));
            foreach (int side in new[] { -1, 1 })
            {
                model.Add((PersonFactory.Leg(style), m * Matrix4x4.Translate(PersonFactory.Hip + Vector3.right * side * PersonFactory.LegX)));
                model.Add((PersonFactory.Arm(style), m * Matrix4x4.TRS(new Vector3(side * PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f), Quaternion.Euler(0f, 0f, side * 8f), Vector3.one)));
            }
        }

        private void EnsureMarkers()
        {
            if (followMarker != null) return;
            followMarker = Marker("Followed", new Color(1f, 0.85f, 0.3f), 1f);
            homePin = Marker("Home pin", new Color(0.45f, 0.95f, 0.5f), 0.8f);
            workPin = Marker("Work pin", new Color(0.45f, 0.75f, 1f), 0.8f);
            var go = new GameObject("Route");
            routeLine = go.AddComponent<LineRenderer>();
            routeLine.sharedMaterial = World.Materials.UI;
            routeLine.widthMultiplier = 0.002f;
            routeLine.startColor = routeLine.endColor = new Color(1f, 0.85f, 0.3f, 0.8f);
            routeLine.numCapVertices = 2;
            routeLine.enabled = false;
        }

        private Transform Marker(string name, Color colour, float size)
        {
            var b = new MeshBuilder();
            var glow = Pal.Glow(colour);
            b.AddCylinder(new Vector3(0f, 0f, 0f), 0f, 0.006f * size, 0.009f * size, 4, glow, glow, false, false, false);
            b.AddCylinder(new Vector3(0f, 0.009f * size, 0f), 0.006f * size, 0f, 0.004f * size, 4, glow, glow, false, false, false);
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = b.ToMesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = World.Materials.Lit; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        // ------------------------------------------------------------------ the performance light

        public static readonly Color Green = new(0.35f, 0.9f, 0.4f), Orange = new(1f, 0.65f, 0.2f), Red = new(1f, 0.3f, 0.25f), Grey = new(0.6f, 0.6f, 0.6f);

        /// <summary>
        /// How the headset is keeping up, from the measured frame time against the display's budget: green at full rate
        /// (room to build more), orange when frames start to run late (close to the limit), red when it falls well behind.
        /// </summary>
        public Color Health(out string words)
        {
            if (Perf == null || Perf.FrameMs <= 0f) { words = "measuring..."; return Grey; }
            float budget = 1000f / Perf.Refresh;
            float ratio = Perf.FrameMs / budget;
            if (ratio <= 1.08f) { words = "keeping up - room for more"; return Green; }
            if (ratio <= 1.5f) { words = "close to the limit"; return Orange; }
            words = "falling behind - the world is heavy for the headset";
            return Red;
        }

        // ------------------------------------------------------------------ stats and help

        /// <summary>The Stats button: frame rate and the world's numbers (again: closes).</summary>
        public void ToggleStats() { infoMode = infoMode is 1 or 3 ? 0 : 1; infoTimer = 0f; if (infoMode == 0) Palette.ShowInfo(null, null, null); else ShowStats(); }

        /// <summary>The Help button: the controls of the tool in hand (again: closes).</summary>
        public void ToggleHelp() { infoMode = infoMode == 2 ? 0 : 2; if (infoMode == 0) Palette.ShowInfo(null, null, null); else { helpTopic = TopicFor(Tool); ShowHelp(); } }

        private void CloseInfo() { infoMode = 0; Palette.ShowInfo(null, null, null); }

        private void ShowStats()
        {
            Palette.ShowInfo("Stats", $"{World.Clock.Label} · day {World.Clock.Day + 1}", StatsText(), CloseInfo);
            Palette.SetInfoButtons(new[] { new PaletteUI.Option("Performance", false, () => { infoMode = 3; ShowPerf(); }) { Kind = OptionKind.Open, Tip = "How the headset is keeping up, and switches that make the world lighter to draw." } });
        }

        // ------------------------------------------------------------------ the performance panel

        /// <summary>Opens the performance panel (live numbers, the settings, Measure).</summary>
        public void ShowPerformance() { infoMode = 3; ShowPerf(); }

        private bool measuring;
        private string measuringWhat;
        private readonly List<PerfBench.Result> measured = new();

        private void ShowPerf()
        {
            Palette.ShowInfo("Performance", measuring ? $"Measuring {measuringWhat} - keep still" : "Tap a setting to change it and watch the numbers", PerfText(), CloseInfo);
            var buttons = new List<PaletteUI.Option>();
            foreach (var s in PerfSettings.All)
            {
                if (s == PerfSettings.WorldSize) continue;   // (the World panel has it)
                var setting = s;
                buttons.Add(new PaletteUI.Option(s.Label, s.Value != s.Default, () => { setting.Next(); Say($"{setting.Label} - {setting.Help}"); ShowPerf(); }, !measuring));
            }
            buttons.Add(new PaletteUI.Option(measuring ? "Measuring..." : "Measure", measuring, StartMeasure, !measuring) { Kind = OptionKind.Action, Tip = "Times a few seconds of drawing and says what costs most." });
            buttons.Add(new PaletteUI.Option("Defaults", false, () => { PerfSettings.ResetAll(); Say("Performance settings back to their defaults"); ShowPerf(); }, !measuring) { Kind = OptionKind.Action, Tip = "Every setting back as it came." });
            buttons.Add(new PaletteUI.Option("World stats", false, () => { infoMode = 1; ShowStats(); }) { Kind = OptionKind.Open, Tip = "The world in numbers." });
            Palette.SetInfoButtons(buttons);
        }

        /// <summary>The live numbers: frame rate, the headset's GPU and CPU time per frame, and the simulation by system.</summary>
        public string PerfText()
        {
            var t = new CardText(30);
            if (Perf == null || Perf.FrameMs <= 0f) { t.Line("Measuring...", true); return t.ToString(); }
            float budget = 1000f / Perf.Refresh;
            var light = Health(out _);
            string colour = light == Green ? CardText.Good : light == Orange ? CardText.Warn : CardText.Bad;
            t.Row("Frame rate", $"{Perf.Fps:0} fps of {Perf.Refresh:0}", colour);
            float gpu = Perf.AppGpuMs > 0f ? Perf.AppGpuMs : Perf.GpuMs;
            string busy = Perf.GpuUtil >= 0f ? $" · {Perf.GpuUtil * 100f:0}% busy" : "";
            t.Row("GPU", gpu > 0f ? $"{gpu:0.0} ms of {budget:0.0}{busy}" : busy.Length > 0 ? busy.Substring(3) : "not reported", gpu > budget ? CardText.Bad : gpu > budget * 0.85f ? CardText.Warn : null);
            t.Row("CPU", Perf.CpuMs > 0f ? $"main {Perf.CpuMs:0.0} ms" + (Perf.AppCpuMs > 0f ? $" · app {Perf.AppCpuMs:0.0} ms" : "") : "not reported", Perf.CpuMs > budget * 0.85f ? CardText.Warn : null);
            var costs = new List<(float ms, string name)>();
            for (int k = 0; k < SimCost.Count; k++) if (SimCost.Ms[k] >= 0.05f) costs.Add((SimCost.Ms[k], SimCost.Names[k]));
            costs.Sort((a, b) => b.ms.CompareTo(a.ms));
            var top = new StringBuilder($"{SimCost.Total:0.0} ms");
            for (int k = 0; k < costs.Count && k < 3; k++) top.Append(k == 0 ? ": " : ", ").Append($"{costs[k].name} {costs[k].ms:0.0}");
            t.Row("Simulation", top.ToString());
            return t.ToString();
        }

        /// <summary>
        /// Measure: switches each part of the world off for a moment, on the view as it is, and shows on the card what
        /// each one costs (GPU and CPU time it saves), biggest first.
        /// </summary>
        private void StartMeasure()
        {
            if (measuring) return;
            StartCoroutine(Measure());
        }

        private IEnumerator Measure()
        {
            measuring = true; measured.Clear();
            Say("Measuring for about 40 seconds - keep the view still", 4f);
            yield return PerfBench.Sweep(World, 1f, 1.6f, (what, i, n) => { measuringWhat = $"{what} ({i + 1}/{n})"; if (infoMode == 3) ShowPerf(); }, r => measured.Add(r));
            measuring = false;
            Palette.ShowCard("What it costs", "time each part takes per frame, biggest first", MeasureText(), null, () => Palette.HideCard());
            if (infoMode == 3) ShowPerf();
            Say("Measured - the card on the left shows what each part costs");
        }

        public string MeasureText()
        {
            var t = new CardText(26);
            if (measured.Count < 2) { t.Line("Nothing measured", true); return t.ToString(); }
            var a = measured[0]; var z = measured[^1];
            float gpu = (a.GpuMs + z.GpuMs) / 2f, cpu = (a.CpuMs + z.CpuMs) / 2f;
            t.Row("Now", gpu > 0f ? $"GPU {gpu:0.0} ms · CPU {cpu:0.0} ms" : $"{(a.Fps + z.Fps) / 2f:0} fps");
            var rows = new List<(float gpu, float cpu, string name)>();
            for (int i = 1; i < measured.Count - 1; i++) rows.Add((gpu - measured[i].GpuMs, cpu - measured[i].CpuMs, measured[i].Name));
            rows.Sort((x, y) => (y.gpu + y.cpu * 0.5f).CompareTo(x.gpu + x.cpu * 0.5f));
            t.Section("Switched off, it saves");
            foreach (var (g, c, name) in rows)
            {
                if (gpu <= 0f) { t.Row(name, "-"); continue; }
                string text = $"GPU {Signed(g)}" + (Mathf.Abs(c) >= 0.3f ? $" · CPU {Signed(c)}" : "");
                t.Row(name, text, g >= 2f ? CardText.Bad : g >= 1f ? CardText.Warn : null);
            }
            return t.ToString();
            static string Signed(float ms) => Mathf.Abs(ms) < 0.15f ? "~0" : $"{ms:0.0} ms";
        }

        public string StatsText()
        {
            var t = new CardText(30);
            t.Section("Headset");
            if (Perf != null && Perf.FrameMs > 0f)
            {
                float budget = 1000f / Perf.Refresh;
                var light = Health(out string words);
                string colour = light == Green ? CardText.Good : light == Orange ? CardText.Warn : CardText.Bad;
                t.Row("Keeping up", words, colour);
                t.Row("Frame rate", $"{Perf.Fps:0} fps of {Perf.Refresh:0}");
                t.Row("Frame time", $"{Perf.FrameMs:0.0} ms (budget {budget:0.0})");
                t.Row("CPU main", Perf.CpuMs > 0f ? $"{Perf.CpuMs:0.0} ms" : "not reported");
                t.Row("GPU", Perf.GpuMs > 0f ? $"{Perf.GpuMs:0.0} ms" : Perf.GpuUtil >= 0f ? $"{Perf.GpuUtil * 100f:0}% busy" : "not reported");
            }
            else t.Line("Measuring...", true);
            t.Section("Simulation");
            var costs = new List<(float ms, string name)>();
            for (int k = 0; k < SimCost.Count; k++) if (SimCost.Ms[k] >= 0.05f) costs.Add((SimCost.Ms[k], SimCost.Names[k]));
            costs.Sort((a, b) => b.ms.CompareTo(a.ms));
            var top = new StringBuilder();
            for (int k = 0; k < costs.Count && k < 4; k++) top.Append(k > 0 ? ", " : "").Append($"{costs[k].name} {costs[k].ms:0.0}");
            t.Row("Per frame", $"{SimCost.Total:0.0} ms");
            t.Row("Most", top.Length > 0 ? top.ToString() : "-");
            t.Section("World");
            var people = World.People;
            int asleep = people.Count(PeopleSystem.Activity.Sleeping), walking = people.Count(PeopleSystem.Activity.Walking), working = people.Count(PeopleSystem.Activity.Working);
            int homeless = 0; for (int k = 0; k < people.People.Count; k++) if (people.HomeOf(k) < 0) homeless++;
            t.Row("People", $"{people.People.Count} people · {walking} walking, {working} at work, {asleep} asleep");
            int homes = 0, beds = 0;
            foreach (var h in World.Buildings.Houses) { var bp = Catalog.Get(h.variant); if (bp.Dwelling) { homes++; beds += bp.Residents + 2; } }
            t.Row("Homes", $"{homes} homes, beds for {beds}" + (homeless > 0 ? $" · {homeless} without a home" : ""), homeless > 0 ? CardText.Warn : null);
            t.Row("Buildings", $"{World.Buildings.Houses.Count} buildings · {World.Forest.Trees.Count} trees");
            int[] kinds = new int[AnimalFactory.KindCount];
            foreach (var a in World.Animals.Animals) kinds[Mathf.Clamp(a.kind, 0, kinds.Length - 1)]++;
            var animals = new StringBuilder($"{World.Animals.Animals.Count}");
            int listed = 0;
            for (int k = 0; k < kinds.Length; k++) if (kinds[k] > 0 && listed++ < 4) animals.Append(listed == 1 ? ": " : ", ").Append($"{kinds[k]} {AnimalFactory.Names[k].ToLower()}");
            t.Row("Animals", animals.ToString());
            var net = World.Track.Network;
            int running = 0; foreach (var tr in World.Trains.Trains) if (tr.Running) running++;
            t.Row("Railway", $"{World.Trains.Trains.Count} trains ({running} running) · {net.TotalLength:0.0} m · {net.Joints.Count} switches · {World.Trains.Stations.Count} stations");
            t.Row("Roads", $"{World.Traffic.Vehicles.Count} vehicles ({World.Traffic.Moving} moving) · {World.Cctv.Count} cameras");
            t.Row("Land", $"{World.Terrain.LandArea():0.0} m²");
            return t.ToString();
        }

        /// <summary>A villager's card: what they're doing (and why), their day, who they are, home and work, friends.</summary>
        private static string PersonBody(PeopleSystem.PersonCard c)
        {
            var t = new CardText();
            t.Section("Now").Row("Doing", c.Now).Row("Going to", c.Goal).Row("Then", c.Next);
            if (c.Today != null && c.Today.Count > 0)
            {
                t.Section("Today");
                foreach (var (line, now, done) in c.Today)
                {
                    string time = line.Substring(0, 5), what = line.Substring(6);
                    t.Row(time, now ? CardText.Tint(what + "  (now)", CardText.Accent) : done ? CardText.Tint(what, CardText.Muted) : what);
                }
            }
            t.Section(c.Child ? "Family and school" : "Home and work").Row("Home", c.Home ?? CardText.Tint("none yet", CardText.Warn)).Row(c.Child ? "School" : "Works at", c.Work);
            t.Section("About").Row("Character", c.Traits).Row("Day", c.Hours).Row("Friends", c.Friends);
            return t.ToString();
        }

        /// <summary>A building's card: open or not, who is there, its staff and goods, how busy it gets; residents for a home.</summary>
        private string PlaceBody(PeopleSystem.PlaceCard c, int house)
        {
            var t = new CardText();
            if (c.Home)
            {
                t.Section("Home").Row("Living here", c.Living > 0 ? c.Residents : "nobody yet").Row("Beds", $"{c.Living} of {c.Beds} taken").Row("Now", c.Here);
                return t.ToString();
            }
            t.Section("Now");
            if (c.Hours != null) t.Row("Hours", c.Hours);
            if (c.Name != null && Catalog.Get(World.Buildings.Houses[house].variant).Serves != Need.None)
                t.Row("Visitors", c.Visitors == 0 ? "nobody here" + (c.Coming > 0 ? $", {c.Coming} on the way" : "") : $"{c.Visitors} here" + (c.Coming > 0 ? $", {c.Coming} on the way" : ""))
                 .Row("Here", c.Here);
            if (c.Staff != null || c.Goods != null) t.Section("Work").Row("Staff", c.Staff).Row("Goods", c.Goods);
            if (c.Busiest != null) t.Section("Visits").Row("Today", $"{c.Today}").Row("Yesterday", $"{c.Yesterday}").Row("Busiest", c.Busiest);
            if (Catalog.IsStation(World.Buildings.Houses[house].variant))
            {
                var hh = World.Buildings.Houses[house];
                int st = World.Trains.StationAt(new Vector2(hh.x, hh.z));
                t.Section("Trains").Row("Station", st >= 0 ? World.Trains.StationName(st) : CardText.Tint("not beside a railway", CardText.Warn));
                if (st >= 0)
                {
                    var calling = new List<string>();
                    for (int k = 0; k < World.Trains.Trains.Count; k++)
                    {
                        var tr = World.Trains.Trains[k];
                        if (tr.AtStation == st) calling.Insert(0, $"train {k + 1} standing here");
                        else if (World.Trains.CallsAt(tr, st, 1)) calling.Add($"train {k + 1} next");
                    }
                    t.Row("Calling", calling.Count > 0 ? string.Join(", ", calling) : "no train is due");
                    float crates = World.People.WaitingAt(house);
                    if (crates >= 1f) t.Row("Platform", $"{Mathf.FloorToInt(crates)} crates for the goods train");
                }
            }
            string s = t.ToString();
            return s.Length > 0 ? s : new CardText().Line("A quiet spot: nobody visits it on purpose", true).ToString();
        }

    }
}
