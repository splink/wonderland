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
        public float PreferredSize = 2f;

        public WonderlandWorld World { get; private set; }
        public ToolController Tools { get; private set; }
        public PaletteUI Palette { get; private set; }
        private float floorY;
        private string roomNote;

        private IEnumerator Start()
        {
            WonderlandLighting.Apply();
            if (Rig == null) Rig = FindAnyObjectByType<OVRCameraRig>();
            // Let head tracking settle so "in front of you" means something.
            for (int i = 0; i < 30; i++) yield return null;

            float size = PreferredSize;
            var centre = Vector3.zero; var yaw = Quaternion.identity;
            yield return DetectRoom((c, r, s) => { centre = c; yaw = r; size = s; });

            World = new GameObject("Wonderland").AddComponent<WonderlandWorld>();
            World.transform.SetPositionAndRotation(new Vector3(centre.x, floorY, centre.z), yaw);
            bool restored = false;
            if (WonderlandWorld.HasSave())
            {
                try { World.Load(Materials); restored = true; }
                catch (System.Exception e) { Debug.LogWarning("Wonderland: could not restore the saved world: " + e.Message); }
            }
            if (!restored) World.Create(size, Materials);

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
            Tools.ResetRequested = () => { World.Create(size, Materials); Tools.SelectTool(Tool.Terrain); Tools.Say("A fresh landscape"); };
            Palette.AddGlobal("Undo", () => Tools.Say(World.Undo() ? "Undone" : "Nothing to undo"));
            Palette.AddGlobal("Save", Save);
            Palette.AddGlobal("Load", () =>
            {
                if (!WonderlandWorld.HasSave()) { Tools.Say("No saved world yet"); return; }
                World.Load(Materials); Tools.SelectTool(Tool.Terrain); Tools.Say("World loaded");
            });
            Palette.AddGlobal("Move", () => { Tools.PlacingWorld = true; Tools.Say("Point where the world should grow"); });
            Palette.AddGlobal("New", Tools.RequestReset);
            Tools.Say(restored ? "Welcome back - your world was restored" : roomNote ?? "Sculpt some hills to begin", 6f);
        }

        private void Save()
        {
            try { World.Save(); Tools.Say("World saved"); }
            catch (System.Exception e) { Tools.Say("Could not save: " + e.Message); }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && World != null && World.Terrain != null) { try { World.Save(); } catch (System.Exception) { /* best effort */ } }
        }

        private void OnApplicationQuit() => OnApplicationPause(true);

        /// <summary>Finds the floor height and the largest free square (up to the preferred size) in front of the player.</summary>
        private IEnumerator DetectRoom(System.Action<Vector3, Quaternion, float> result)
        {
            var head = Rig != null ? Rig.centerEyeAnchor : Camera.main != null ? Camera.main.transform : transform;
            var forward = head.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            var yaw = Quaternion.LookRotation(forward, Vector3.up);
            floorY = 0f;
            float size = Mathf.Min(PreferredSize, 1.8f);
            roomNote = "No room setup found - using the guardian floor. Use Move to reposition.";

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
                var room = mruk.GetCurrentRoom();
                if (room != null && room.FloorAnchor != null)
                {
                    floorY = room.FloorAnchor.transform.position.y;
                    roomNote = null;
                    foreach (float s in new[] { 2f, 1.8f, 1.6f, 1.4f, 1.2f, 1f })
                    {
                        if (s > PreferredSize) continue;
                        var c = head.position + forward * (s * 0.5f + 0.35f);
                        if (Fits(room, c, yaw, s)) { size = s; result(c, yaw, s); yield break; }
                    }
                    size = 1f;
                    roomNote = "Not much free floor here - the world is small. Use Move to find more space.";
                }
            }
            else if (!granted) roomNote = "No spatial data permission - using the guardian floor. Use Move to reposition.";
            result(head.position + forward * (size * 0.5f + 0.35f), yaw, size);
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
