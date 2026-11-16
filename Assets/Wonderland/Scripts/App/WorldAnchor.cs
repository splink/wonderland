using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Keeps the world where it was put in the real room. A Meta spatial anchor marks the world's origin; it is saved on
    /// the headset, so the next launch finds it again and puts the world back on the same spot. While the app runs the
    /// world follows the anchor, so it stays put when the headset re-localises (taken off and put on again, a recentre,
    /// the guardian redrawn). The height is the room scan's floor when there is one (the anchor's own height otherwise),
    /// and only the anchor's heading is used, so the world always lies level on the floor. Move re-pins it.
    /// </summary>
    public sealed class WorldAnchor : MonoBehaviour
    {
        private const string Key = "wonderland.anchor";

        public Transform World;
        /// <summary>The real floor's height, when the room scan knows it.</summary>
        public Func<float?> Floor;
        /// <summary>True while the player is moving the world by hand (the anchor isn't followed then).</summary>
        public Func<bool> Holding;

        private OVRSpatialAnchor anchor;
        private bool busy;

        public string Status { get; private set; } = "not anchored yet";
        public bool Following => anchor != null && anchor.Localized && !busy;

        /// <summary>At launch: finds the saved anchor and puts the world back where it was. Pins a new one if there is none.</summary>
        public IEnumerator Restore()
        {
#if UNITY_EDITOR
            Status = "editor: no anchors";
            yield break;
#else
            string saved = PlayerPrefs.GetString(Key, "");
            if (!Guid.TryParse(saved, out var uuid)) { Status = "first launch"; Pin(); yield break; }
            busy = true;
            var found = new List<OVRSpatialAnchor.UnboundAnchor>();
            var load = OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, found);
            for (float t = 0f; !load.IsCompleted && t < 10f; t += Time.unscaledDeltaTime) yield return null;
            if (!load.IsCompleted || !load.GetResult().Success || found.Count == 0)
            {
                busy = false;
                Status = "the saved spot wasn't found in this room - placed in front of you";
                Pin();
                yield break;
            }
            var unbound = found[0];
            if (!unbound.Localized)
            {
                var localise = unbound.LocalizeAsync();
                for (float t = 0f; !localise.IsCompleted && t < 10f; t += Time.unscaledDeltaTime) yield return null;
            }
            var go = new GameObject("World anchor");
            anchor = go.AddComponent<OVRSpatialAnchor>();
            try { unbound.BindTo(anchor); }
            catch (Exception e) { Debug.LogWarning("Wonderland: anchor bind failed: " + e.Message); Destroy(go); anchor = null; busy = false; Pin(); yield break; }
            busy = false;
            Status = "back where you left it";
            Follow(true);
#endif
        }

        /// <summary>Pins the world where it is now (after it was placed or moved), replacing the old anchor.</summary>
        public void Pin()
        {
#if !UNITY_EDITOR
            if (World != null) StartCoroutine(PinNow());
#endif
        }

        private IEnumerator PinNow()
        {
            busy = true;
            var old = anchor;
            var go = new GameObject("World anchor");
            go.transform.SetPositionAndRotation(World.position, Quaternion.Euler(0f, World.eulerAngles.y, 0f));
            var fresh = go.AddComponent<OVRSpatialAnchor>();
            for (float t = 0f; !fresh.Created && t < 5f; t += Time.unscaledDeltaTime) yield return null;
            if (!fresh.Created) { Destroy(go); busy = false; Status = "couldn't pin the world to the room"; yield break; }
            var save = fresh.SaveAnchorAsync();
            for (float t = 0f; !save.IsCompleted && t < 10f; t += Time.unscaledDeltaTime) yield return null;
            if (!save.IsCompleted || !save.GetResult().Success) { Destroy(go); busy = false; Status = "couldn't save the world's spot"; yield break; }
            PlayerPrefs.SetString(Key, fresh.Uuid.ToString());
            PlayerPrefs.Save();
            anchor = fresh;
            if (old != null) { old.EraseAnchorAsync(); Destroy(old.gameObject); }
            busy = false;
            Status = "pinned to the room";
        }

        private void LateUpdate() => Follow(false);

        /// <summary>Puts the world on the anchor (level, on the floor); small corrections only when they matter.</summary>
        private void Follow(bool always)
        {
            if (World == null || Holding != null && Holding()) return;
            if (!Following)
            {
                // Not pinned (yet): still keep it on the floor.
                float? floor = Floor?.Invoke();
                if (floor.HasValue && Mathf.Abs(World.position.y - floor.Value) > 0.002f) World.position = new Vector3(World.position.x, floor.Value, World.position.z);
                return;
            }
            var a = anchor.transform;
            float y = Floor?.Invoke() ?? a.position.y;
            var position = new Vector3(a.position.x, y, a.position.z);
            var rotation = Quaternion.Euler(0f, a.eulerAngles.y, 0f);
            if (!always && (World.position - position).sqrMagnitude < 0.0005f * 0.0005f && Quaternion.Angle(World.rotation, rotation) < 0.05f) return;
            World.SetPositionAndRotation(position, rotation);
        }
    }
}
