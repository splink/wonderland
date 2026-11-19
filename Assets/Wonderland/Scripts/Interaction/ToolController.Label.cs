using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// The label at the pointer: what is under it, and what each button does to it right now - "Trigger: its card ·
    /// Grip: move it". It appears only over something you can do something with (a villager, a building, a train, a
    /// switch, the track...) or while something is in hand, beside the pointer's tip where the eyes already are, and
    /// keeps the same size to the eye however far away it is. Settings turns it off.
    /// </summary>
    public sealed partial class ToolController
    {
        private const string LabelsKey = "wonderland.labels";
        public static bool LabelsOn
        {
            get => PlayerPrefs.GetInt(LabelsKey, 1) == 1;
            set { PlayerPrefs.SetInt(LabelsKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        private TextMeshPro label;
        private MeshFilter labelBack;
        private string labelText; private float labelUntil; private int labelKey;
        private bool pointerOnPalette, lastHit;
        private Vector3 lastLocal;
        private int rayHouse = -1;

        /// <summary>The words at the pointer now (null: hidden) - for the tests.</summary>
        public string PointerLabel => label != null && label.gameObject.activeSelf ? label.text : null;

        private const string Key = "#FFD27A";
        private static string Act(string button, string what) => $"<color={Key}><b>{button}</b></color>  {what}";

        /// <summary>What the label says now, or null. Worked out a few times a second.</summary>
        private string LabelNow()
        {
            if (!LabelsOn || pointerOnPalette || PlacingWorld) return null;
            string let = heldByTrigger ? "Trigger" : "Let go";
            if (carrying) return Lines($"Moving the {HouseFactory.Name(HouseVariant).ToLower()}", Act(let, "put it down here"), Act("Left stick", "turn it"));
            if (carriedTrain != null) return Lines("Moving a train", Act(let, "put it on the track here"));
            if (carriedPerson.HasValue) return Lines("Moving a villager", Act(let, "set them down here"));
            if (carriedAnimal.HasValue) return Lines("Moving an animal", Act(let, "set it down here"));
            if (dragEdge >= 0) return Lines("Bending the track", dragError != null ? $"<color=#FF8A7A>{dragError}</color>" : Act(let, "keep this shape"));
            if (Tool == Tool.Track && drawn.Count > 0) return Time.time < messageUntil ? Lines(null, message) : null;
            if (stroking) return null;

            bool select = Tool == Tool.Select, erase = Tool == Tool.Eraser;
            string card = select ? Act("Trigger", "its card") : null;
            string remove = erase ? Act("Trigger", "remove it") : null;
            if (peekPerson >= 0 && peekPerson < World.People.People.Count)
            {
                var c = World.People.CardOf(peekPerson);
                return Lines($"{c.Name}, {c.Role.ToLower()}", select ? Act("Trigger", "their card") : remove, Act("Grip", "move them"));
            }
            if (peekAnimal >= 0 && peekAnimal < World.Animals.Animals.Count)
                return Lines(AnimalLine(peekAnimal), card ?? remove, Act("Grip", "move it"));
            if (peekVehicle != null) return Lines(World.Traffic.Describe(peekVehicle), card ?? remove);
            if (peekTrain != null) return Lines(World.Trains.Describe(peekTrain), card ?? remove, Act("Grip", "move it"));
            if (Tool == Tool.Train && hoveredTrain != null)
                return Lines(World.Trains.Describe(hoveredTrain), Act("Trigger", hoveredTrain.Running ? "stop it" : "set it going"), Act("Left stick", "up / down: speed · left / right: carriages"), Act("Grip", "move it"));
            if (peekBoat >= 0 && peekBoat < World.Boats.Boats.Count) return Lines(World.Boats.Describe(peekBoat), card ?? remove);
            int house = peekHouse >= 0 ? peekHouse : Tool is Tool.Houses or Tool.Eraser ? rayHouse : -1;
            if (house >= 0 && house < World.Buildings.Houses.Count)
            {
                string name = World.People.PlaceCardOf(house).Name ?? HouseFactory.Name(World.Buildings.Houses[house].variant);
                return Lines(name, Tool == Tool.Houses ? Act("Trigger or grip", "move it") : card ?? remove, Tool == Tool.Houses ? null : Act("Grip", "move it"));
            }
            if (!lastHit) return null;
            return TrackLabel(lastLocal, select, erase);
        }

        /// <summary>Over the railway: a switch, a signal, the end of a line, or a piece of track - and what can be done to it here.</summary>
        private string TrackLabel(Vector3 local, bool select, bool erase)
        {
            var net = World.Track.Network;
            if (!World.Track.HasTrack) return null;
            bool track = Tool == Tool.Track, train = Tool == Tool.Train;
            int joint = World.Track.PickJoint(local, 0.045f);
            if (joint >= 0)
            {
                var j = net.Joints[joint];
                string state = j.Pinned ? "set: trains always go the way it points" : World.Trains.Mode == SwitchMode.Manual ? "set by hand (Manual)" : "trains choose which way";
                string act = select ? Act("Trigger", "its card - choose which way trains go")
                    : track || train ? Act("Trigger", j.Pinned ? "throw it back and let trains choose" : "set it the other way, for good") : null;
                return Lines($"Switch · {state}", act);
            }
            int sig = World.Track.PickSignal(local, 0.03f);
            if (sig >= 0)
            {
                bool red = World.Track.Signals[sig].red;
                return Lines(red ? "Signal · held at red" : "Signal · automatic", select ? Act("Trigger", "its card") : erase ? Act("Trigger", "remove it")
                    : track || train ? Act("Trigger", red ? "back to automatic" : "hold trains at red") : null);
            }
            float reach = track ? 0.06f : 0.035f;
            if (!net.Nearest(local, out int edge, out float s, out _, reach)) return null;
            if (track && SignalMode) return Lines("Railway", Act("Trigger", "put a signal up here"), Act("A", "back to drawing track"));
            var probe = new System.Collections.Generic.List<Vector3> { local };
            var at = TrackPlanner.Attach(net, probe, true, out _);
            if (track && at.Kind == AttachKind.End)
                return Lines("End of the line", Act("Trigger", "draw on from here"), Act("Grip", "bend this piece"));
            string head = "Railway" + (net.Directed(edge) ? " · trains run the way the arrows point" : "");
            if (track) return Lines(head, Act("Trigger", "draw a branch off from here"), Act("Grip", "bend this piece"), Act("A", "signals"));
            if (train) return Lines(head, Act("Trigger", $"put a train here"));
            if (erase) return Lines(head, Act("Trigger", "remove this piece"));
            return Lines(head, select ? Act("Trigger", "its card") : null, Act("Grip", "bend it"));
        }

        private static string Lines(string title, params string[] lines)
        {
            var sb = new StringBuilder();
            if (title != null) sb.Append($"<b><color=#FFE3A8>{title}</color></b>");
            foreach (var l in lines)
            {
                if (string.IsNullOrEmpty(l)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(l);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>Places the label up and to the right of the pointer's tip, facing the eyes, the same size to the eye at any distance.</summary>
        private void ShowLabel(Vector3 tip, Vector3 hand)
        {
            // Worked out again when what is pointed at changes, and a few times a second (what they're doing changes too).
            var spot = lastHit ? Vector3Int.RoundToInt(lastLocal / 0.015f) : Vector3Int.zero;
            int key = System.HashCode.Combine(System.HashCode.Combine(peekPerson, peekAnimal, peekVehicle, peekTrain, peekBoat, peekHouse, rayHouse),
                System.HashCode.Combine(Tool, Holding, pointerOnPalette, drawn.Count > 0, hoveredTrain, spot, SignalMode));
            float now = Time.realtimeSinceStartup;
            if (key != labelKey || now >= labelUntil || now < labelUntil - 1f) { labelKey = key; labelUntil = now + 0.15f; labelText = LabelNow(); }
            if (label == null) MakeLabel();
            bool on = labelText != null;
            label.gameObject.SetActive(on);
            if (!on) return;
            if (label.text != labelText)
            {
                label.text = labelText;
                label.ForceMeshUpdate();
                var b = label.textBounds;
                const float pad = 0.006f;
                if (labelBack.sharedMesh != null) { if (Application.isPlaying) Destroy(labelBack.sharedMesh); else DestroyImmediate(labelBack.sharedMesh); }
                labelBack.sharedMesh = PaletteUI.RoundedRect(b.size.x + pad * 2f, b.size.y + pad * 1.4f, 0.005f, new Color(0.17f, 0.15f, 0.2f, 0.88f), new Color(0.1f, 0.09f, 0.12f, 0.88f));
                labelBack.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.001f);
            }
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : hand;
            var up = cam != null ? cam.transform.up : Vector3.up;
            float d = Mathf.Max(0.15f, Vector3.Distance(eye, tip));
            var toTip = (tip - eye) / d;
            var right = Vector3.Cross(up, toTip).normalized;
            float k = Mathf.Clamp(d / 0.5f, 0.5f, 5f);
            // Up and right of the tip, pulled a little towards the eye so it never sinks into what it names.
            var at = tip + (right * 0.016f + up * 0.012f) * k - toTip * Mathf.Min(0.05f, d * 0.08f);
            // Never over the palette (or a panel beside it): when it is between the eyes and the label, the label waits.
            var look = at - eye;
            if (Palette != null && Palette.gameObject.activeInHierarchy && Palette.RayHitsPanel(new Ray(eye, look.normalized), out float behind) && behind < look.magnitude)
            { label.gameObject.SetActive(false); return; }
            label.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toTip, up));
            label.transform.localScale = Vector3.one * k;
        }

        private void MakeLabel()
        {
            var go = new GameObject("Pointer label");
            go.transform.SetParent(transform, false);
            label = go.AddComponent<TextMeshPro>();
            label.rectTransform.pivot = new Vector2(0f, 0f);
            label.rectTransform.sizeDelta = new Vector2(0.3f, 0.06f);
            label.fontSize = 0.1f;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.lineSpacing = 4f;
            label.color = new Color(0.95f, 0.93f, 0.9f);
            label.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            label.GetComponent<MeshRenderer>().sortingOrder = 1;
            var back = new GameObject("Back");
            back.transform.SetParent(go.transform, false);
            labelBack = back.AddComponent<MeshFilter>();
            var r = back.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(World.Materials.UI) { renderQueue = 2996 };
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
    }
}
