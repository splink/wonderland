using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Wonderland
{
    /// <summary>
    /// Boots the experience: asks for spatial data, finds the real floor and a free patch of it with MR Utility Kit
    /// (fallback: the guardian's floor level), grows the miniature world there (or restores the saved one), and wires up
    /// the palette on the left controller and the tools on the right. Saves automatically when the app pauses.
    /// </summary>
    public sealed class WonderlandApp : MonoBehaviour
    {
        private const string ScenePermission = "com.oculus.permission.USE_SCENE";
        public WorldMaterials Materials;
        public OVRCameraRig Rig;
        /// <summary>The starting patch (metres per side): small on purpose, the player grows the land from here.</summary>
        public float StartSize = 1.1f;   // ~1 m² of land once the corners are rounded off

        public WonderlandWorld World { get; private set; }
        public ToolController Tools { get; private set; }
        public PaletteUI Palette { get; private set; }
        private float floorY;
        private MRUKRoom scanned;
        public WorldAnchor Anchor { get; private set; }

        /// <summary>The real floor's height from the room scan (looked up again until the scan has loaded).</summary>
        private float? ScannedFloor()
        {
            if (scanned == null && MRUK.Instance != null) scanned = MRUK.Instance.GetCurrentRoom();
            return scanned != null && scanned.FloorAnchor != null ? scanned.FloorAnchor.transform.position.y : null;
        }
        private string roomNote;
        private RoomFootprint room;

        private IEnumerator Start()
        {
            WonderlandLighting.Apply();
            // Fixed foveated rendering: the periphery shades at lower resolution (the terrain's per-pixel grass is the
            // heaviest shader); dynamic, so the runtime only raises it when the GPU needs headroom.
            try { OVRManager.foveatedRenderingLevel = OVRManager.FoveatedRenderingLevel.High; OVRManager.useDynamicFoveatedRendering = true; }
            catch (System.Exception e) { Debug.LogWarning("Wonderland: foveation unavailable: " + e.Message); }
            var perf = gameObject.AddComponent<PerfLog>();
            perf.Context = () => World == null || World.Terrain == null ? "" :
                $"{World.Forest.Trees.Count} trees, {World.Buildings.Houses.Count} houses, land {World.Terrain.LandArea():0.0} m2, tool {Tools?.Tool}, {World.People.Census()}, {World.Traffic.Vehicles.Count} vehicles ({World.Traffic.Moving} moving), {World.Cctv.Count} cameras ({World.Cctv.Filmed} pictures), sound {SoundSystem.Setting} ({World.Sounds.Playing.Count} playing; {World.Sounds.Diagnostics})";
            if (Rig == null) Rig = FindAnyObjectByType<OVRCameraRig>();
            // Let head tracking settle so "in front of you" means something.
            for (int i = 0; i < 30; i++) yield return null;

            float size = StartSize;
            var centre = Vector3.zero; var yaw = Quaternion.identity;
            yield return DetectRoom((c, r) => { centre = c; yaw = r; });

            World = new GameObject("Wonderland").AddComponent<WonderlandWorld>();
            World.transform.SetPositionAndRotation(new Vector3(centre.x, floorY, centre.z), yaw);
            World.Room = room;
            bool restored = false;
            if (WonderlandWorld.HasSave())
            {
                try { World.Load(Materials); restored = true; }
                catch (System.Exception e) { Debug.LogWarning("Wonderland: could not restore the saved world: " + e.Message); }
            }
            if (!restored) World.Create(size, Materials, endless: true);
            // The world goes on in every direction, seen through a window of up to 3 x 3 m (older island worlds too).
            World.MakeEndless();
            World.Lift = WorldAnchor.SavedLift;
            World.UseWindow(true);

            Palette = new GameObject("Palette").AddComponent<PaletteUI>();
            Palette.gameObject.layer = PaletteUI.UiLayer;
            var left = Rig != null ? Rig.leftControllerAnchor : null;
            if (left != null)
            {
                Palette.transform.SetParent(left, false);
                Palette.transform.localPosition = new Vector3(0f, 0.07f, 0.06f);
                Palette.transform.localRotation = Quaternion.Euler(38f, 0f, 0f);
            }
            Palette.Build(Materials.UI, Materials.Lit);

            Tools = new GameObject("Tools").AddComponent<ToolController>();
            Tools.Init(World, Palette, Rig != null ? Rig.rightControllerAnchor : null);
            Tools.LeftHand = Rig != null ? Rig.leftControllerAnchor : null;
            Tools.Head = Rig != null ? Rig.centerEyeAnchor : null;
            StartCoroutine(HouseFactory.WarmPreviews());   // (the library's tile models, ready before they are asked for)
            // The map above the palette: the whole world from above, the trains and cars moving on it.
            var map = new GameObject("Mini map") { layer = PaletteUI.UiLayer }.AddComponent<MiniMap>();
            map.transform.SetParent(Palette.transform, false);
            map.transform.localPosition = new Vector3(0f, PaletteUI.Height * 0.5f + MiniMap.Size * 0.5f + 0.012f, 0f);
            map.Init(World, Materials.Screen);
            Tools.Map = map;
            Tools.ResetRequested = () => { World.Create(StartSize, Materials, endless: true); World.UseWindow(true); WonderlandWorld.CurrentSlot = 0; Tools.SelectTool(Tool.Terrain); Tools.Say("A fresh landscape - Save it as a world to keep it"); };
            // The bottom bar in three clusters: editing (Undo, Redo), the world as a whole (World, Home), worlds (save,
            // load, new); Help and Settings sit in the header.
            Palette.AddGlobal(new PaletteUI.Option("Undo", false, () => Tools.Say(World.Undo() ? "Undone (B does this too)" : "Nothing to undo")) { Kind = OptionKind.Action, Tip = "Takes back the last change. B on the right controller does this too." }, 0);
            Palette.AddGlobal(new PaletteUI.Option("Redo", false, () => Tools.Say(World.Redo() ? "Redone" : "Nothing to redo")) { Kind = OptionKind.Action, Tip = "Puts back what Undo took back." }, 0);
            // Several worlds: Save and Load list World 1-6; the one being played is kept up to date by the autosave.
            Tools.SaveWorld = n =>
            {
                try { World.SaveSlot(n); World.Save(); Tools.Say($"Saved as World {n} - it keeps itself up to date from now on"); }
                catch (System.Exception e) { Tools.Say("Could not save: " + e.Message); }
            };
            Tools.LoadWorld = n =>
            {
                try { World.LoadSlot(n, Materials); World.MakeEndless(); World.UseWindow(true); World.Save(); Tools.SelectTool(Tool.Terrain); Tools.Say($"World {n} loaded"); }
                catch (System.Exception e) { Tools.Say($"Could not load World {n}: " + e.Message); }
            };
            Palette.AddGlobal(new PaletteUI.Option("World", false, Tools.ToggleMoving) { Kind = OptionKind.Open, Tip = "The world as a whole: move it in the room, lift it, turn it, its window's size and scale. (To turn it quickly: hold both grips and twist.)" }, 1);
            Palette.AddGlobal(new PaletteUI.Option("Home", false, () => Tools.Say(World.WindowTarget == Vector2.zero ? "Already home, where the world started" : World.NavigateHome() ? "Back home, where the world started" : "Can't get home from here")) { Kind = OptionKind.Action, Tip = "Scrolls back to where the world started." }, 1);
            Palette.AddGlobal(new PaletteUI.Option("Worlds", false, Tools.ToggleWorlds) { Kind = OptionKind.Open, Tip = "Save this world, load another, or start a new one." }, 2);
            // Sound, tips, labels, the grid, stats, performance and New world: all under Settings.
            Tools.SoundState = () => SoundSystem.Setting switch { SoundSetting.On => "On", SoundSetting.Quiet => "Quiet", _ => "Off" };
            Tools.CycleSound = () =>
            {
                SoundSystem.Setting = (SoundSetting)(((int)SoundSystem.Setting + 1) % 3);
                Tools.Say(SoundSystem.Setting switch { SoundSetting.On => "Sound on", SoundSetting.Quiet => "Sound quiet", _ => "Sound off" });
            };
            Palette.AddHeaderButton(new PaletteUI.Option("Help", false, Tools.ToggleHelp) { Kind = OptionKind.Open, Tip = "How everything works, topic by topic." });
            Palette.AddHeaderButton(new PaletteUI.Option("Settings", false, Tools.ToggleSettings) { Kind = OptionKind.Open, Tip = "Sound, tips, labels, the grid, stats and performance." });
            Tools.Perf = GetComponent<PerfLog>();
            // The little figures (2-3 cm) are not drawn further than 3 m from the eyes: barely a pixel there, and in a big
            // world over a thousand draws saved (the CCTV camera still sees them).
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.targetTexture != null || !cam.enabled) continue;
                var cull = cam.layerCullDistances;
                cull[WonderlandWorld.MiniatureLayer] = WonderlandWorld.MiniatureCullDistance;
                cam.layerCullDistances = cull;
                cam.layerCullSpherical = true;
            }
            // Pinned to the room: back on the same spot next launch, and steady while it runs.
            Anchor = gameObject.AddComponent<WorldAnchor>();
            Anchor.World = World;
            Anchor.Floor = ScannedFloor;
            Anchor.Holding = () => Tools.PlacingWorld;
            Tools.WorldPlaced = Anchor.Pin;
            // The Perf panel's settings: applied now, and whenever one changes.
            Light Sun() { foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) return l; return null; }
            PerfSettings.Apply(Sun());
            World.transform.localScale = Vector3.one * PerfSettings.WorldScale;
            PerfSettings.Changed += s =>
            {
                PerfSettings.Apply(Sun());
                if (s == PerfSettings.Detail) Lod.Apply(World.transform);
                if (s == PerfSettings.WorldSize) { World.transform.localScale = Vector3.one * PerfSettings.WorldScale; World.FitToRoom(); }
            };
            if (System.IO.File.Exists(PerfBench.Trigger)) { var bench = gameObject.AddComponent<PerfBench>(); bench.World = World; bench.Anchor = Anchor; bench.Materials = Materials; }
            yield return Anchor.Restore();
            // Now that the world stands where it belongs in the room, the window is fitted to the room from there.
            World.FitToRoom();
            if (Anchor.Status.StartsWith("back")) roomNote = null;
            else if (Anchor.Status.StartsWith("the saved spot")) roomNote = "Your world's saved spot wasn't found in this room - use Move to put it back";
            string note = World.RoomStatus.Contains("ignored") ? World.RoomStatus : roomNote;
            Tools.Say(restored ? "Welcome back - your world was restored" + (note != null ? " · " + note : "") : note ?? "Sculpt some hills to begin", 8f);
        }

        private Light keyLight;

        /// <summary>The light follows the village clock: morning sun, golden evening, moonlit night.</summary>
        private void Update()
        {
            if (World == null || World.Terrain == null) return;
            if (keyLight == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) keyLight = l;
            WonderlandLighting.SetTimeOfDay(keyLight, World.Clock.Hour, World.Clock.Daylight, World.transform.eulerAngles.y, World.Sky.Rain);
        }

        /// <summary>Leaving the app: the world is saved for next time, and into its named world if it has one.</summary>
        private void OnApplicationPause(bool paused)
        {
            if (!paused || World == null || World.Terrain == null || PerfBench.Running) return;
            try { World.Save(); if (WonderlandWorld.CurrentSlot > 0) World.Save(WonderlandWorld.SlotPath(WonderlandWorld.CurrentSlot)); }
            catch (System.Exception) { /* best effort */ }
        }

        private void OnApplicationQuit() => OnApplicationPause(true);

        /// <summary>
        /// Finds the floor height and the room's free floor (outline + furniture footprints) and picks a free spot about a
        /// metre in front of the player for the starting patch. Falls back to the guardian play area, then to no bounds.
        /// </summary>
        private IEnumerator DetectRoom(System.Action<Vector3, Quaternion> result)
        {
            var head = Rig != null ? Rig.centerEyeAnchor : Camera.main != null ? Camera.main.transform : transform;
            var forward = head.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            var yaw = Quaternion.LookRotation(forward, Vector3.up);
            floorY = 0f;
            roomNote = "No room setup found - land can grow anywhere nearby. Use Move to reposition.";

            bool granted = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                bool answered = false;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => answered = true;
                callbacks.PermissionDenied += _ => answered = true;
                Permission.RequestUserPermission(ScenePermission, callbacks);
                for (float t = 0f; !answered && t < 30f; t += Time.unscaledDeltaTime) yield return null;
            }
            granted = Permission.HasUserAuthorizedPermission(ScenePermission);
#endif
            var mruk = MRUK.Instance;
            if (granted && mruk != null)
            {
                var load = mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
                for (float t = 0f; !load.IsCompleted && t < 10f; t += Time.unscaledDeltaTime) yield return null;
                var mrukRoom = mruk.GetCurrentRoom();
                if (mrukRoom != null && mrukRoom.FloorAnchor != null)
                {
                    scanned = mrukRoom;
                    floorY = mrukRoom.FloorAnchor.transform.position.y;
                    room = FromRoomScan(mrukRoom, floorY);
                    roomNote = null;
                    foreach (float d in new[] { 1.0f, 0.8f, 1.3f, 0.6f, 1.6f })
                    {
                        var c = head.position + forward * d;
                        if (Fits(mrukRoom, c, yaw, StartSize)) { result(c, yaw); yield break; }
                    }
                    roomNote = "Couldn't find free floor in front of you - use Move to place the world.";
                }
            }
            else if (!granted) roomNote = "No spatial data permission - land can grow anywhere nearby. Use Move to reposition.";
            room ??= FromGuardian();
            if (room != null && roomNote != null && roomNote.StartsWith("No")) roomNote = "Using your guardian area as the room. Use Move to reposition.";
            result(head.position + forward * 1.0f, yaw);
        }

        /// <summary>Scene volumes that are real furniture standing in the room (not the scanned room mesh, walls etc.).</summary>
        private const MRUKAnchor.SceneLabels Furniture = MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH |
            MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.BED | MRUKAnchor.SceneLabels.SCREEN |
            MRUKAnchor.SceneLabels.LAMP | MRUKAnchor.SceneLabels.PLANT | MRUKAnchor.SceneLabels.OTHER;

        /// <summary>
        /// Floor outline and furniture footprints from the MRUK room scan. Only furniture labels count: the scanned room
        /// mesh (GLOBAL_MESH) also carries a volume - the whole room's bounding box - which would otherwise cover the
        /// entire floor and forbid all growth. Implausibly large volumes (over 40 % of the floor) are ignored too.
        /// </summary>
        private static RoomFootprint FromRoomScan(MRUKRoom room, float floorY)
        {
            var footprint = new RoomFootprint { Source = "room scan" };
            var floor = room.FloorAnchor;
            foreach (var p in floor.PlaneBoundary2D) footprint.Floor.Add(floor.transform.TransformPoint(new Vector3(p.x, p.y, 0f)));
            float floorArea = Mathf.Abs(Area(footprint.Floor));
            foreach (var anchor in room.Anchors)
            {
                if (!anchor.VolumeBounds.HasValue) continue;
                if (!anchor.HasAnyLabel(Furniture)) { Debug.Log($"Wonderland room: ignoring volume {anchor.Label} (not furniture)"); continue; }
                var b = anchor.VolumeBounds.Value;
                var corners = new System.Collections.Generic.List<Vector3>();
                float lowest = float.MaxValue;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var w = anchor.transform.TransformPoint(c);
                    corners.Add(w); lowest = Mathf.Min(lowest, w.y);
                }
                if (lowest >= floorY + 0.35f) continue;   // a wall shelf or hanging lamp, not on the floor
                float area = Mathf.Abs(Area(RoomFootprint.ConvexHull(corners.ConvertAll(c => new Vector2(c.x, c.z))).Select3()));
                if (floorArea > 0f && area > floorArea * 0.4f) { Debug.Log($"Wonderland room: ignoring volume {anchor.Label} ({area:0.0} m2 of {floorArea:0.0} m2 floor)"); continue; }
                footprint.Obstacles.Add(corners);
                Debug.Log($"Wonderland room: furniture {anchor.Label} ({area:0.00} m2)");
            }
            Debug.Log($"Wonderland room: floor {footprint.Floor.Count} points, {floorArea:0.0} m2, {footprint.Obstacles.Count} furniture footprints");
            return footprint.Floor.Count >= 3 ? footprint : null;
        }

        /// <summary>Signed area of a polygon in the XZ plane.</summary>
        private static float Area(System.Collections.Generic.List<Vector3> poly)
        {
            float a = 0f;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) a += (poly[j].x + poly[i].x) * (poly[j].z - poly[i].z);
            return a * 0.5f;
        }

        /// <summary>The guardian play area, when there is no room scan (may be unavailable under OpenXR).</summary>
        private RoomFootprint FromGuardian()
        {
            try
            {
                if (OVRManager.boundary == null || !OVRManager.boundary.GetConfigured()) return null;
                var points = OVRManager.boundary.GetGeometry(OVRBoundary.BoundaryType.PlayArea);
                if (points == null || points.Length < 3) return null;
                var space = Rig != null ? Rig.trackingSpace : null;
                var footprint = new RoomFootprint { Source = "guardian" };
                foreach (var p in points) footprint.Floor.Add(space != null ? space.TransformPoint(p) : p);
                return footprint;
            }
            catch (System.Exception) { return null; }
        }

        private bool Fits(MRUKRoom room, Vector3 centre, Quaternion yaw, float size)
        {
            for (int i = 0; i <= 6; i++)
            for (int j = 0; j <= 6; j++)
            {
                // The organic rim leaves the corners empty, so test a rounded square.
                float u = i / 6f * 2f - 1f, v = j / 6f * 2f - 1f;
                if (u * u + v * v > 1.5f) continue;
                var p = centre + yaw * new Vector3(u * size * 0.47f, 0f, v * size * 0.47f);
                p.y = floorY + 0.1f;
                if (!room.IsPositionInRoom(p, false)) return false;
                if (room.IsPositionInSceneVolume(p + Vector3.up * 0.05f, 0.03f)) return false;
            }
            return true;
        }
    }
}
