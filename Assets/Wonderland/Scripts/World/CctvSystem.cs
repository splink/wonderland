using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Wonderland
{
    /// <summary>How a CCTV camera is aimed (saved, by where the camera stands).</summary>
    [Serializable]
    public struct CameraAim
    {
        public float x, z;
        public float yaw, pitch;   // the direction it looks: yaw 0 = +Z, pitch down in degrees
    }

    /// <summary>
    /// CCTV: miniature cameras on poles the player puts up (Build, Small, Camera), and what they see - live, from the
    /// miniature world only (never the passthrough room). One hidden camera does all the filming, on demand: the feed
    /// being watched in the Watch tool's viewer is filmed about 15 times a second into a small 320 x 240 picture, and
    /// each monitoring studio's wall of four screens is refreshed in turn, a screen at a time, only while a studio is in
    /// view. At most one picture is filmed per frame, so the cost stays bounded however many cameras there are.
    /// Cameras keep their aim (tap the land in the Watch tool to point the selected one there) through edits, saves and
    /// loads; moving one resets its aim to straight ahead.
    /// </summary>
    public sealed class CctvSystem : MonoBehaviour
    {
        public const int Width = 320, Height = 240, Screens = 4;
        public const float ViewerHz = 15f, WallSeconds = 0.3f, DefaultPitch = 28f;

        private WonderlandWorld world;
        private Material screenMaterial;
        private Camera film;
        private RenderTexture viewer;
        private readonly RenderTexture[] wall = new RenderTexture[Screens];
        private readonly List<int> cameras = new();                    // building indices of the cameras, in placement order
        private readonly List<Transform> heads = new();
        private readonly Dictionary<Vector2, CameraAim> aims = new();
        private readonly List<Renderer> studioScreens = new();
        private readonly List<int> studios = new();
        private Material viewerMaterial;
        private readonly Material[] wallMaterials = new Material[Screens];
        private int version = -1, wallNext;
        private float viewerTimer, wallTimer;
        private Vector2? selected;                                      // the watched camera, by where it stands

        /// <summary>The Watch tool is open: film the selected camera for the viewer.</summary>
        public bool Watching;
        public int Filmed { get; private set; }                         // pictures filmed so far (tests, diagnostics)
        public int FilmedForViewer { get; private set; }
        public int LastFilmed { get; private set; } = -1;
        public RenderTexture Viewer => viewer;
        public Material ViewerMaterial => viewerMaterial;
        public int Count { get { Refresh(); return cameras.Count; } }
        public int Studios { get { Refresh(); return studios.Count; } }

        public void Init(WonderlandWorld owner, Material screen)
        {
            world = owner; screenMaterial = screen;
            var go = new GameObject("CCTV film camera");
            go.transform.SetParent(transform, false);
            film = go.AddComponent<Camera>();
            film.enabled = false;
            film.clearFlags = CameraClearFlags.SolidColor;
            film.backgroundColor = new Color(0.58f, 0.72f, 0.86f);
            film.nearClipPlane = 0.004f; film.farClipPlane = 6f; film.fieldOfView = 62f;
            film.cullingMask = ~(1 << 5);   // not the UI layer (the palette)
            film.allowHDR = false; film.allowMSAA = false;
            var data = film.GetUniversalAdditionalCameraData();
            data.renderShadows = false; data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None;
            viewer = Picture("CCTV viewer");
            for (int i = 0; i < Screens; i++) wall[i] = Picture("CCTV wall " + i);
            if (screen != null)
            {
                viewerMaterial = new Material(screen) { mainTexture = viewer };
                for (int i = 0; i < Screens; i++) wallMaterials[i] = new Material(screen) { mainTexture = wall[i] };
            }
        }

        private static RenderTexture Picture(string name) => new(Width, Height, 16, RenderTextureFormat.ARGB32) { name = name, antiAliasing = 1, useMipMap = false, filterMode = FilterMode.Bilinear };

        private void OnDestroy()
        {
            if (viewer) viewer.Release();
            foreach (var rt in wall) if (rt) rt.Release();
        }

        // ------------------------------------------------------------------ the cameras

        /// <summary>The cameras and studios in the world changed: their heads and screens follow.</summary>
        private void Refresh()
        {
            if (version == world.Buildings.Version) return;
            version = world.Buildings.Version;
            cameras.Clear(); studios.Clear();
            var houses = world.Buildings.Houses;
            for (int i = 0; i < houses.Count; i++)
            {
                if (houses[i].variant == Catalog.Camera) cameras.Add(i);
                else if (houses[i].variant == Catalog.Studio) studios.Add(i);
            }
            while (heads.Count < cameras.Count)
            {
                var go = new GameObject("Camera head");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = HouseFactory.CameraHead();
                go.AddComponent<MeshRenderer>().sharedMaterial = world.Materials.Lit;
                heads.Add(go.transform);
            }
            for (int k = 0; k < heads.Count; k++) heads[k].gameObject.SetActive(k < cameras.Count);
            for (int k = 0; k < cameras.Count; k++) PoseHead(k);
            // The studios' screens.
            foreach (var r in studioScreens) if (r) Release(r.gameObject);
            studioScreens.Clear();
            foreach (int s in studios)
                for (int k = 0; k < Screens; k++)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    if (q.TryGetComponent<Collider>(out var col)) Release(col);
                    q.name = "Studio screen";
                    q.transform.SetParent(transform, false);
                    var c = HouseFactory.StudioScreens[k];
                    q.transform.localPosition = world.Buildings.LocalToWorld(s, c);
                    q.transform.localRotation = Quaternion.Euler(0f, world.Buildings.Houses[s].rotation, 0f);
                    q.transform.localScale = new Vector3(HouseFactory.StudioScreenHalf.x * 2f, HouseFactory.StudioScreenHalf.y * 2f, 1f);
                    var r = q.GetComponent<MeshRenderer>();
                    r.sharedMaterial = wallMaterials[k] != null ? wallMaterials[k] : world.Materials.Lit;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    studioScreens.Add(r);
                }
            if (selected.HasValue && IndexOf(selected.Value) < 0) selected = null;
        }

        private static void Release(UnityEngine.Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        private int IndexOf(Vector2 at)
        {
            for (int k = 0; k < cameras.Count; k++) if (Pos(k) == at) return k;
            return -1;
        }

        private Vector2 Pos(int k) { var h = world.Buildings.Houses[cameras[k]]; return new Vector2(h.x, h.z); }

        /// <summary>How camera <paramref name="k"/> is aimed: as the player set it, or straight out from its front.</summary>
        public CameraAim AimOf(int k)
        {
            var at = Pos(k);
            if (aims.TryGetValue(at, out var aim)) return aim;
            return new CameraAim { x = at.x, z = at.y, yaw = world.Buildings.Houses[cameras[k]].rotation + 180f, pitch = DefaultPitch };
        }

        private Quaternion Look(int k) { var a = AimOf(k); return Quaternion.Euler(a.pitch, a.yaw, 0f); }

        private Vector3 Eye(int k) => world.Buildings.LocalToWorld(cameras[k], HouseFactory.CameraHeadAt);

        private void PoseHead(int k)
        {
            heads[k].localPosition = Eye(k);
            heads[k].localRotation = Look(k) * Quaternion.Euler(0f, 180f, 0f);   // the head's lens faces its -Z
        }

        /// <summary>Points camera <paramref name="k"/> at a point of the world (it can tilt between level and 80 degrees down).</summary>
        public void AimAt(int k, Vector3 target)
        {
            Refresh();
            if (k < 0 || k >= cameras.Count) return;
            var d = target - Eye(k);
            if (d.sqrMagnitude < 1e-6f) return;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, 0f, 80f);
            var at = Pos(k);
            aims[at] = new CameraAim { x = at.x, z = at.y, yaw = yaw, pitch = pitch };
            PoseHead(k);
        }

        /// <summary>Turns camera <paramref name="k"/> by <paramref name="yaw"/> and tilts it by <paramref name="pitch"/> degrees (down is positive; level to 80 down).</summary>
        public void Turn(int k, float yaw, float pitch)
        {
            Refresh();
            if (k < 0 || k >= cameras.Count) return;
            var a = AimOf(k);
            var at = Pos(k);
            aims[at] = new CameraAim { x = at.x, z = at.y, yaw = Mathf.Repeat(a.yaw + yaw, 360f), pitch = Mathf.Clamp(a.pitch + pitch, 0f, 80f) };
            PoseHead(k);
            viewerTimer = 0f;   // film the new view straight away
        }

        /// <summary>The camera being watched (an index into the cameras, -1 if there are none).</summary>
        public int Selected
        {
            get
            {
                Refresh();
                var trains = world.Trains.Trains;
                if (selectedTrain != null && trains.Contains(selectedTrain)) return cameras.Count + trains.IndexOf(selectedTrain);
                if (cameras.Count == 0) return trains.Count > 0 ? 0 : -1;
                int k = selected.HasValue ? IndexOf(selected.Value) : -1;
                return k >= 0 ? k : 0;
            }
            set
            {
                Refresh();
                if (value >= 0 && value < cameras.Count) { selected = Pos(value); selectedTrain = null; viewerTimer = 0f; FollowSeed = 0; }
                else if (value >= cameras.Count && value < Feeds) { selectedTrain = world.Trains.Trains[value - cameras.Count]; viewerTimer = 0f; FollowSeed = 0; }
            }
        }

        // ------------------------------------------------------------------ cameras on trains

        private Train selectedTrain;
        private Transform cabCamera;

        /// <summary>Everything that can be watched: the cameras on poles, then a camera on every train's engine.</summary>
        public int Feeds { get { Refresh(); return cameras.Count + world.Trains.Trains.Count; } }

        private Train TrainOf(int k) => k >= cameras.Count && k - cameras.Count < world.Trains.Trains.Count ? world.Trains.Trains[k - cameras.Count] : null;

        /// <summary>A train's camera: on the engine's nose, looking ahead and a little down.</summary>
        private void TrainEye(Train t, out Vector3 eye, out Quaternion look)
        {
            var rot = world.Trains.LocoRotation(t);
            float front = t.Cars != null && t.Cars.Length > 0 ? t.Cars[0].Layout.Front : TrainFactory.Loco.Front;
            eye = world.Trains.LocoPosition(t) + rot * new Vector3(0f, 0.042f, front + 0.008f);   // on the nose, above the buffers
            look = rot * Quaternion.Euler(6f, 0f, 0f);
        }

        /// <summary>Where feed <paramref name="k"/> looks from, in world space (renders and tests).</summary>
        public void ViewOf(int k, out Vector3 eye, out Quaternion look)
        {
            if (TrainOf(k) is { } train) TrainEye(train, out eye, out look);
            else { eye = Eye(k); look = Look(k); }
            eye = transform.TransformPoint(eye + look * new Vector3(0f, 0f, 0.004f)); look = transform.rotation * look;
        }

        /// <summary>The little camera on the nose of the engine being watched.</summary>
        private void MountCab()
        {
            var t = Watching && selectedTrain != null && world.Trains.Trains.Contains(selectedTrain) ? selectedTrain : null;
            if (t == null) { if (cabCamera) cabCamera.gameObject.SetActive(false); return; }
            if (cabCamera == null)
            {
                var go = new GameObject("Cab camera");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = HouseFactory.CameraHead();
                go.AddComponent<MeshRenderer>().sharedMaterial = world.Materials.Lit;
                cabCamera = go.transform;
            }
            cabCamera.gameObject.SetActive(true);
            TrainEye(t, out var eye, out var look);
            cabCamera.localPosition = eye - look * new Vector3(0f, 0f, 0.006f);
            cabCamera.localRotation = look * Quaternion.Euler(0f, 180f, 0f);
            cabCamera.localScale = Vector3.one * 0.8f;
        }

        /// <summary>The camera nearest a point (within <paramref name="within"/>), or -1.</summary>
        public int NearestCamera(Vector2 p, float within)
        {
            Refresh();
            int best = -1; float bestD = within;
            for (int k = 0; k < cameras.Count; k++) { float d = (Pos(k) - p).magnitude; if (d < bestD) { bestD = d; best = k; } }
            return best;
        }

        /// <summary>"Camera 2 - by the station".</summary>
        public string NameOf(int k)
        {
            if (TrainOf(k) is { } train) return $"Train {k - cameras.Count + 1} - from the front of the engine ({world.Trains.Describe(train).Split(" - ")[0]})";
            if (k < 0 || k >= cameras.Count) return "No cameras";
            var at = Pos(k);
            var houses = world.Buildings.Houses;
            string near = null; float best = 0.45f;
            for (int i = 0; i < houses.Count; i++)
            {
                var bp = Catalog.Get(houses[i].variant);
                if (bp.Small) continue;
                float d = (new Vector2(houses[i].x, houses[i].z) - at).magnitude;
                if (d < best) { best = d; near = bp.Name.ToLower(); }
            }
            return $"Camera {k + 1}" + (near != null ? $" - by the {near}" : "");
        }

        /// <summary>The building index of camera <paramref name="k"/>.</summary>
        public int HouseOf(int k) => k >= 0 && k < cameras.Count ? cameras[k] : -1;

        // ------------------------------------------------------------------ filming

        private void LateUpdate()
        {
            if (world == null) return;
            long t0 = SimCost.Now; Step(Time.unscaledDeltaTime); SimCost.Add(SimCost.Cameras, t0);
        }

        /// <summary>Films at most one picture: the watched feed when due, else the next studio screen when due.</summary>
        public void Step(float dt)
        {
            Refresh();
            viewerTimer -= dt; wallTimer -= dt;
            MountCab();
            bool following = FollowSeed != 0 && FollowVillager(dt);
            if (following && viewerTimer <= 0f) { viewerTimer += 1f / ViewerHz; if (viewerTimer < 0f) viewerTimer = 0f; FilmFrom(povPos, povRot, viewer); FilmedForViewer++; return; }
            int feeds = Feeds;
            if (feeds == 0) return;
            if (Watching && !following && viewerTimer <= 0f) { viewerTimer += 1f / ViewerHz; if (viewerTimer < 0f) viewerTimer = 0f; Film(Selected, viewer); FilmedForViewer++; return; }
            if (studios.Count > 0 && wallTimer <= 0f && StudioInView())
            {
                wallTimer = WallSeconds;
                int screen = wallNext++ % Screens;
                Film(screen % feeds, wall[screen]);
            }
        }

        private bool StudioInView()
        {
            if (!Application.isPlaying) return true;   // tests and renders
            foreach (var r in studioScreens) if (r && r.isVisible) return true;
            return false;
        }

        private void Film(int k, RenderTexture into)
        {
            if (TrainOf(k) is { } train)
            {
                TrainEye(train, out var eye, out var look);
                FilmFrom(eye + look * new Vector3(0f, 0f, 0.004f), look, into);
            }
            else if (k >= 0 && k < cameras.Count) FilmFrom(Eye(k) + Look(k) * new Vector3(0f, 0f, 0.008f), Look(k), into);
            if (k >= 0) LastFilmed = k;
        }

        /// <summary>Films from a point of the world (local) looking one way, into a picture.</summary>
        private void FilmFrom(Vector3 eye, Quaternion look, RenderTexture into)
        {
            film.transform.position = transform.TransformPoint(eye);
            film.transform.rotation = transform.rotation * look;
            float scale = transform.lossyScale.x;
            film.nearClipPlane = 0.004f * scale; film.farClipPlane = 6f * scale;
            Filmed++;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;   // batch tests: nothing to draw with
            var request = new RenderPipeline.StandardRequest { destination = into };
            if (RenderPipeline.SupportsRenderRequest(film, request)) RenderPipeline.SubmitRenderRequest(film, request);
            else { film.targetTexture = into; film.Render(); film.targetTexture = null; }
        }

        /// <summary>Films the watched camera into the viewer right now (renders, tests).</summary>
        public void FilmNow() { Refresh(); if (FollowSeed != 0) FilmFrom(povPos, povRot, viewer); else if (Feeds > 0) Film(Selected, viewer); }

        /// <summary>Copies the viewer picture into a texture (the renders save it as an image).</summary>
        public Texture2D Snapshot()
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var keep = RenderTexture.active;
            RenderTexture.active = viewer;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = keep;
            return tex;
        }

        // ------------------------------------------------------------------ following a villager

        /// <summary>The villager (seed) whose day the viewer follows (0: nobody) - see <see cref="FollowVillager"/>.</summary>
        public int FollowSeed;
        /// <summary>Where the followed villager's camera is: "through their eyes", "from the train window", "outside the bakery"...</summary>
        public string FollowView { get; private set; }
        /// <summary>Something is being filmed for the viewer.</summary>
        public bool Live => Watching || FollowSeed != 0;

        /// <summary>
        /// Whether figures at a local point may be on the viewer: anywhere for the Watch tool's cameras (they can look
        /// anywhere), within a metre of the followed villager's camera when following. Those are drawn and kept moving
        /// smoothly even out of the player's sight.
        /// </summary>
        public bool Filming(Vector3 local) => Watching || FollowSeed != 0 && (local - povPos).sqrMagnitude < 1f;

        private Vector3 povPos;
        private Quaternion povRot = Quaternion.identity;
        /// <summary>Where the followed villager's camera is and which way it looks (local; tests and renders).</summary>
        public Vector3 FollowEye => povPos;
        public Quaternion FollowLook => povRot;
        private string povMode;

        /// <summary>
        /// Keeps the camera with the followed villager, whatever they are doing: through their eyes on foot (and sitting,
        /// on a boat or a carousel), out of their carriage window on a train, out of the passenger window when they are
        /// out in the household's car, and - when they have gone indoors, where nothing is modelled - a steady view of
        /// the building from outside. Within one view it glides (smoothed position and turn); from one view to the next
        /// it cuts, as a film would. False when they are no longer in the world (following stops).
        /// </summary>
        private bool FollowVillager(float dt)
        {
            int i = world.People.IndexOfSeed(FollowSeed);
            if (i < 0) { FollowSeed = 0; FollowView = null; povMode = null; return false; }
            var p = world.People.PovOf(i);
            Vector3 eye; Quaternion look; string mode;
            var car = p.Hidden ? world.Traffic.DrivenBy(FollowSeed) : null;
            if (p.Train != null && world.Trains.Trains.Contains(p.Train))
            {
                world.Trains.WindowView(p.Train, out eye, out look);
                mode = "train " + world.Trains.Trains.IndexOf(p.Train); FollowView = "from the train window";
            }
            else if (car != null && car.State == TrafficSystem.Mode.Driving)
            {
                // The front passenger window of the car.
                var rot = car.Root.localRotation;
                eye = car.Root.localPosition + rot * new Vector3(0.0125f, 0.016f, 0.004f);
                look = rot * Quaternion.Euler(5f, 48f, 0f);
                mode = "car"; FollowView = "from the car";
            }
            else if (p.Hidden)
            {
                // Indoors (or parked up at a place): the building, from outside.
                int house = car != null ? world.Buildings.Nearest(car.Position, 0.15f) : p.House;
                if (house >= 0) { BuildingView(house, out eye, out look); FollowView = "outside the " + Catalog.Get(world.Buildings.Houses[house].variant).Name.ToLower(); }
                else
                {
                    var at = new Vector3(p.Where.x, world.Terrain.SurfaceAt(p.Where.x, p.Where.y), p.Where.y);
                    eye = at + new Vector3(0f, 0.09f, -0.12f); look = Quaternion.LookRotation(at - eye, Vector3.up);
                    FollowView = "where they went in";
                }
                mode = "house " + house;
            }
            else
            {
                // With them, at eye level: just over their shoulder (their hat at the bottom of the picture), looking where
                // they look - someone they stand talking to stays in the picture instead of filling it.
                var turn = Quaternion.Euler(0f, p.Heading, 0f);
                float s = p.Child ? 0.72f : 1f;
                eye = p.Eye + turn * new Vector3(0.004f * s, 0.006f * s, -0.016f * s);
                look = turn * Quaternion.Euler(11f, 0f, 0f);
                mode = "eyes"; FollowView = "over their shoulder";
            }
            if (mode != povMode) { povMode = mode; povPos = eye; povRot = look; viewerTimer = 0f; return true; }   // a cut
            float glide = 1f - Mathf.Exp(-dt * (mode == "eyes" ? 7f : 10f)), turnRate = 1f - Mathf.Exp(-dt * (mode == "eyes" ? 4f : 8f));
            povPos = Vector3.Lerp(povPos, eye, glide);
            povRot = Quaternion.Slerp(povRot, look, turnRate);
            return true;
        }

        /// <summary>
        /// A steady view of a building from outside: in front of it, a little above, looking at its door - or from
        /// another side when the front is crowded (inside another building, or under the ground).
        /// </summary>
        private void BuildingView(int house, out Vector3 eye, out Quaternion look)
        {
            var h = world.Buildings.Houses[house];
            var bp = Catalog.Get(h.variant);
            var centre = new Vector3(h.x, world.Terrain.SurfaceAt(h.x, h.z), h.z);
            var target = centre + Vector3.up * Mathf.Max(0.02f, bp.Height * 0.4f);
            float away = Mathf.Max(bp.Half.x, bp.Half.y) + 0.1f, up = Mathf.Max(0.05f, bp.Height * 0.6f);
            eye = target; look = Quaternion.identity;
            for (int k = 0; k < 8; k++)
            {
                float yaw = h.rotation + 180f + (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 45f;   // the front first, then round
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var at = centre + dir * away + Vector3.up * up;
                at.y = Mathf.Max(at.y, world.Terrain.SurfaceAt(at.x, at.z) + 0.03f);
                eye = at;
                if (!InsideBuilding(at, house)) break;
            }
            look = Quaternion.LookRotation(target - eye, Vector3.up);
        }

        private bool InsideBuilding(Vector3 p, int except)
        {
            var houses = world.Buildings.Houses;
            for (int j = 0; j < houses.Count; j++)
            {
                if (j == except) continue;
                var h = houses[j];
                var bp = Catalog.Get(h.variant);
                var q = Quaternion.Inverse(Quaternion.Euler(0f, h.rotation, 0f)) * new Vector3(p.x - h.x, 0f, p.z - h.z);
                if (Mathf.Abs(q.x) < bp.Half.x + 0.012f && Mathf.Abs(q.z) < bp.Half.y + 0.012f && p.y < world.Terrain.SurfaceAt(h.x, h.z) + bp.Height + 0.01f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ saving

        public CameraAim[] Export()
        {
            Refresh();
            var list = new List<CameraAim>();
            for (int k = 0; k < cameras.Count; k++) if (aims.ContainsKey(Pos(k))) list.Add(aims[Pos(k)]);
            return list.ToArray();
        }

        public void Import(IEnumerable<CameraAim> list)
        {
            aims.Clear();
            if (list != null) foreach (var a in list) aims[new Vector2(a.x, a.z)] = a;
            version = -1;
        }
    }
}
