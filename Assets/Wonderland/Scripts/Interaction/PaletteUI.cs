using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    public enum Tool { Terrain, Water, Forest, Paths, Track, Train, Houses, Decor, Select, Eraser }

    /// <summary>One pressable element of the palette (tool tile or option chip).</summary>
    public sealed class PaletteButton : MonoBehaviour
    {
        public Action OnClick;
        public bool Selected;
        public bool Hovered;
        public bool Enabled = true;
        internal MeshRenderer Background;
        internal MeshRenderer Ring;
        internal TextMeshPro Label;
        public string Text => Label != null ? Label.text : "";
        internal Transform Icon;
        internal Color Idle, Hover, Active;
        private float hover;
        private MaterialPropertyBlock block;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        internal float IconYaw = 30f, IconPitch = -20f;

        private void Update()
        {
            hover = Mathf.MoveTowards(hover, Hovered && Enabled ? 1f : 0f, Time.deltaTime * 8f);
            ApplyVisuals(Time.time);
        }

        /// <summary>Colour, selection ring, hover pop and icon pose (also applied at once when state changes).</summary>
        public void ApplyVisuals(float time = 0f)
        {
            block ??= new MaterialPropertyBlock();
            var c = Selected ? Active : Color.Lerp(Idle, Hover, hover);
            if (!Enabled) c.a *= 0.4f;
            block.SetColor(ColorId, c);
            Background.SetPropertyBlock(block);
            if (Ring) Ring.enabled = Selected;
            transform.localScale = Vector3.one * (1f + hover * 0.05f);
            if (Icon) Icon.localRotation = Quaternion.Euler(IconPitch, IconYaw + time * (Selected || Hovered ? 50f : 0f), 0f);
            if (Label) Label.alpha = Enabled ? 1f : 0.45f;
        }
    }

    /// <summary>
    /// The compact palette on the left controller: six tool tiles with little 3D models of what they make, a row of
    /// options for the current tool, global actions (undo, save, load, move, new) and a status / hint line. Built at
    /// runtime from meshes and TextMeshPro; buttons are hit with the right-hand pointer.
    /// </summary>
    public sealed class PaletteUI : MonoBehaviour
    {
        public const int UiLayer = 5;
        public const float Width = 0.33f, Height = 0.195f;
        private readonly Dictionary<Tool, PaletteButton> tools = new();
        private readonly List<PaletteButton> options = new();
        private readonly List<PaletteButton> items = new();
        private readonly List<PaletteButton> globals = new();
        private TextMeshPro hint, title;
        private Material uiMaterial, litMaterial;
        private Transform optionRow, itemRow;
        public PaletteButton Hovered { get; private set; }
        public event Action<Tool> ToolSelected;

        public static readonly string[] ToolNames = { "Terrain", "Water", "Forest", "Roads", "Track", "Train", "Build", "Life", "Select", "Eraser" };
        private MeshRenderer viewer;

        /// <summary>The CCTV viewer: a little monitor above the palette showing a live feed (null hides it).</summary>
        public void SetViewer(Material feed)
        {
            if (feed == null) { if (viewer) viewer.transform.parent.gameObject.SetActive(false); return; }
            if (viewer == null)
            {
                var frame = new GameObject("Viewer");
                frame.transform.SetParent(transform, false);
                frame.transform.localPosition = new Vector3(0f, Height / 2 + 0.07f, 0f);
                Quad("Bezel", frame.transform, 0.172f, 0.13f, 0.008f, new Color(0.12f, 0.12f, 0.14f, 0.97f), new Color(0.08f, 0.08f, 0.1f, 0.97f), 0.0015f, 2990);
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                if (q.TryGetComponent<Collider>(out var c)) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
                q.name = "Feed";
                q.layer = UiLayer;
                q.transform.SetParent(frame.transform, false);
                q.transform.localPosition = new Vector3(0f, 0f, -0.002f);
                q.transform.localScale = new Vector3(0.16f, 0.12f, 1f);
                viewer = q.GetComponent<MeshRenderer>();
                viewer.shadowCastingMode = ShadowCastingMode.Off; viewer.receiveShadows = false;
            }
            viewer.sharedMaterial = feed;
            viewer.transform.parent.gameObject.SetActive(true);
        }

        private readonly PaletteButton[] viewerTurn = new PaletteButton[4];

        /// <summary>
        /// Turn and tilt buttons round the monitor (for a CCTV camera on a pole): left, right, up, down - each press calls
        /// <paramref name="turn"/> with (yaw, pitch) steps. Null hides them.
        /// </summary>
        public void SetViewerControls(Action<float, float> turn)
        {
            if (viewer == null) return;
            var frame = viewer.transform.parent;
            (string label, Vector3 at, float yaw, float pitch)[] keys =
            {
                ("<", new Vector3(-0.097f, 0f, -0.001f), -15f, 0f), (">", new Vector3(0.097f, 0f, -0.001f), 15f, 0f),
                ("^", new Vector3(0f, 0.076f, -0.001f), 0f, -7f), ("v", new Vector3(0f, -0.076f, -0.001f), 0f, 7f),
            };
            for (int k = 0; k < keys.Length; k++)
            {
                if (viewerTurn[k] == null) viewerTurn[k] = Button("Turn " + keys[k].label, frame, keys[k].at, new Vector2(0.02f, 0.02f), keys[k].label, false);
                var b = viewerTurn[k];
                b.gameObject.SetActive(turn != null);
                if (turn == null) continue;
                var (_, _, yaw, pitch) = keys[k];
                b.OnClick = () => turn(yaw, pitch);
                b.ApplyVisuals();
            }
        }

        public void Build(Material ui, Material lit)
        {
            uiMaterial = ui; litMaterial = lit;
            // Backing panel with a soft lighter border.
            Quad("Border", transform, Width + 0.006f, Height + 0.006f, 0.022f, new Color(1f, 0.93f, 0.8f, 0.35f), new Color(1f, 0.93f, 0.8f, 0.18f), 0.002f, 2990);
            Quad("Panel", transform, Width, Height, 0.019f, new Color(0.17f, 0.15f, 0.2f, 0.9f), new Color(0.1f, 0.09f, 0.12f, 0.9f), 0f, 2991);
            title = Text("Title", transform, "Wonderland", new Vector3(-Width / 2 + 0.062f, Height / 2 - 0.011f, -0.002f), new Vector2(0.1f, 0.012f), 0.07f, TextAlignmentOptions.Left);
            title.color = new Color(1f, 0.86f, 0.55f);
            title.fontStyle = FontStyles.Bold;

            // The tools in four groups, each named above its tiles: shaping the land, building, bringing it to life, and
            // changing what is there.
            const float groupGap = 0.007f;
            float pitch = (Width - 0.012f - groupGap * (Groups.Length - 1)) / ToolNames.Length;
            float left = -Width / 2 + 0.006f;
            foreach (var (heading, members) in Groups)
            {
                float span = pitch * members.Length;
                var h = Text("Group " + heading, transform, heading, new Vector3(left + span / 2, 0.0765f, -0.002f), new Vector2(span - 0.004f, 0.009f), 0.048f, TextAlignmentOptions.Center);
                h.color = new Color(0.86f, 0.76f, 0.56f);
                var rule = Quad("Rule", transform, span - 0.006f, 0.0006f, 0.0003f, new Color(0.86f, 0.76f, 0.56f, 0.35f), new Color(0.86f, 0.76f, 0.56f, 0.35f), -0.001f, 2993);
                rule.transform.localPosition = new Vector3(left + span / 2, 0.0718f, -0.001f);
                for (int m = 0; m < members.Length; m++) MakeTool(members[m], left + pitch * (m + 0.5f), pitch);
                left += span + groupGap;
            }
            optionRow = new GameObject("Options").transform;
            optionRow.SetParent(transform, false);
            optionRow.localPosition = new Vector3(0f, -0.009f, 0f);
            itemRow = new GameObject("Items").transform;
            itemRow.SetParent(transform, false);
            itemRow.localPosition = new Vector3(0f, -0.033f, 0f);

            hint = Text("Hint", transform, "", new Vector3(0f, -0.054f, -0.002f), new Vector2(Width - 0.02f, 0.011f), 0.05f, TextAlignmentOptions.Center);
            hint.color = new Color(0.86f, 0.84f, 0.8f);

            // Always there: the village clock and a light for how the headset is keeping up.
            clock = Text("Clock", transform, "", new Vector3(-0.022f, Height / 2 - 0.011f, -0.002f), new Vector2(0.04f, 0.012f), 0.06f, TextAlignmentOptions.Right);
            clock.color = new Color(0.95f, 0.92f, 0.85f);
            health = Quad("Health", transform, 0.0075f, 0.0075f, 0.0037f, Color.white, Color.white, -0.002f, 2995);
            health.transform.localPosition = new Vector3(0.006f, Height / 2 - 0.011f, -0.002f);
        }

        /// <summary>The tools as the palette shows them: in groups, each with a heading.</summary>
        public static readonly (string heading, Tool[] tools)[] Groups =
        {
            ("Landscape", new[] { Tool.Terrain, Tool.Water, Tool.Forest }),
            ("Build", new[] { Tool.Paths, Tool.Track, Tool.Houses }),
            ("Bring to life", new[] { Tool.Train, Tool.Decor }),
            ("Change", new[] { Tool.Select, Tool.Eraser }),
        };

        private void MakeTool(Tool tool, float x, float pitch)
        {
            string name = ToolNames[(int)tool];
            var b = Button(name, transform, new Vector3(x, 0.044f, 0f), new Vector2(pitch - 0.0045f, 0.05f), name, true);
            b.Label.transform.localPosition = new Vector3(0f, -0.018f, -0.003f);
            b.Icon = Icon(tool, b.transform);
            (b.IconYaw, b.IconPitch) = tool switch { Tool.Train => (115f, -15f), Tool.Track => (20f, -55f), Tool.Terrain => (25f, -30f), _ => (30f, -20f) };
            b.ApplyVisuals();
            b.OnClick = () => ToolSelected?.Invoke(tool);
            tools[tool] = b;
        }

        private TextMeshPro clock;
        public string ClockText => clock != null ? clock.text : null;
        private MeshRenderer health;
        private static readonly int TintId = Shader.PropertyToID("_Color");

        /// <summary>The clock ("14:20") and the performance light: green (keeping up), orange (close to the limit), red (falling behind).</summary>
        public void SetClock(string time, Color light)
        {
            if (clock == null) return;
            if (clock.text != time) clock.text = time;
            health.sharedMaterial.SetColor(TintId, light);
        }

        public const float GlobalWidth = 0.04f;    // seven fit along the bottom

        public PaletteButton AddGlobal(string label, Action onClick)
        {
            var b = Button(label, transform, Vector3.zero, new Vector2(GlobalWidth, 0.02f), label, false);
            b.OnClick = onClick;
            b.ApplyVisuals();
            globals.Add(b);
            // Spread the row evenly across the palette, however many there are.
            float pitch = (Width - 0.008f) / globals.Count;
            for (int k = 0; k < globals.Count; k++)
            {
                globals[k].transform.localPosition = new Vector3(-Width / 2 + 0.004f + pitch * (k + 0.5f), -0.078f, 0f);
            }
            return b;
        }

        // ------------------------------------------------------------------ side panels: the card (left), help and stats (right)

        private sealed class Panel
        {
            public Transform Root, Portrait;
            public TextMeshPro Title, Subtitle, Body;
            public PaletteButton Close;
            public readonly List<MeshRenderer> Parts = new();
        }

        private Panel card, info;

        /// <summary>Side panels are taller than the palette (centred on it), so a card never has to shrink its text.</summary>
        public const float PanelHeight = 0.25f, BodySize = 0.058f;

        /// <summary>
        /// A side panel: a header band (portrait, title in bold, a one-line summary under it, Close), then the body in
        /// <see cref="CardText"/> rich text at a fixed size - sections and two-column rows, never auto-shrunk.
        /// </summary>
        private Panel MakePanel(string name, float x, float width, bool portrait)
        {
            var p = new Panel();
            var go = new GameObject(name);
            go.layer = UiLayer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x, 0f, 0f);
            p.Root = go.transform;
            float h = PanelHeight, top = h / 2;
            Quad("Border", p.Root, width + 0.006f, h + 0.006f, 0.02f, new Color(1f, 0.93f, 0.8f, 0.3f), new Color(1f, 0.93f, 0.8f, 0.15f), 0.002f, 2990);
            Quad("Panel", p.Root, width, h, 0.018f, new Color(0.17f, 0.15f, 0.2f, 0.94f), new Color(0.1f, 0.09f, 0.12f, 0.94f), 0f, 2991);
            // Header band: a slightly lighter strip behind the portrait and the title.
            const float band = 0.046f;
            var header = Quad("Header", p.Root, width - 0.008f, band, 0.012f, new Color(0.26f, 0.22f, 0.3f, 0.95f), new Color(0.2f, 0.17f, 0.24f, 0.95f), -0.001f, 2992);
            header.transform.localPosition = new Vector3(0f, top - 0.004f - band / 2, -0.001f);
            float textLeft = portrait ? 0.044f : 0.008f;
            float textWidth = width - textLeft - (portrait ? 0.084f : 0.036f);   // (the card has a second button in its header)
            p.Title = Text("Title", p.Root, "", new Vector3(-width / 2 + textLeft + textWidth / 2, top - 0.019f, -0.002f), new Vector2(textWidth, 0.016f), 0.085f, TextAlignmentOptions.Left);
            p.Title.color = new Color(1f, 0.88f, 0.6f); p.Title.fontStyle = FontStyles.Bold; p.Title.fontSizeMin = 0.06f;
            p.Subtitle = Text("Subtitle", p.Root, "", new Vector3(-width / 2 + textLeft + textWidth / 2, top - 0.037f, -0.002f), new Vector2(textWidth, 0.013f), 0.05f, TextAlignmentOptions.Left);
            p.Subtitle.color = new Color(0.8f, 0.77f, 0.86f); p.Subtitle.fontSizeMin = 0.04f;
            float bodyTop = top - 0.004f - band - 0.008f, bodyBottom = -top + 0.008f;
            p.Body = Text("Body", p.Root, "", new Vector3(0f, (bodyTop + bodyBottom) / 2, -0.002f), new Vector2(width - 0.022f, bodyTop - bodyBottom), BodySize, TextAlignmentOptions.TopLeft);
            p.Body.enableAutoSizing = false; p.Body.fontSize = BodySize;
            p.Body.textWrappingMode = TextWrappingModes.Normal; p.Body.overflowMode = TextOverflowModes.Truncate;
            p.Body.lineSpacing = 6f;
            p.Body.color = new Color(0.95f, 0.93f, 0.9f);
            p.Close = Button("Close", p.Root, new Vector3(width / 2 - 0.019f, top - 0.019f, 0f), new Vector2(0.026f, 0.018f), "Close", false);
            p.Close.ApplyVisuals();
            if (portrait)
            {
                p.Portrait = new GameObject("Portrait").transform;
                p.Portrait.SetParent(p.Root, false);
                p.Portrait.localPosition = new Vector3(-width / 2 + 0.026f, top - 0.032f, -0.014f);
            }
            return p;
        }

        /// <summary>
        /// The character card beside the palette: a little turning model of them, a title ("Emma - Baker") and a few lines
        /// of what is true about them now. <paramref name="close"/> runs when its Close button is pressed.
        /// </summary>
        public void ShowCard(string title, string body, IReadOnlyList<(Mesh mesh, Matrix4x4 at)> model, Action close) => ShowCard(title, null, body, model, close);

        /// <summary>The card with a one-line summary under the title ("Baker · grown-up · early riser").</summary>
        public void ShowCard(string title, string subtitle, string body, IReadOnlyList<(Mesh mesh, Matrix4x4 at)> model, Action close)
        {
            card ??= MakePanel("Card", -Width / 2 - CardWidth / 2 - 0.006f, CardWidth, true);
            card.Root.gameObject.SetActive(true);
            card.Title.text = title; card.Subtitle.text = subtitle ?? ""; card.Body.text = body;
            if (model == null) foreach (var part in card.Parts) part.gameObject.SetActive(false);
            card.Close.OnClick = close;
            if (model != null)
            {
                while (card.Parts.Count < model.Count)
                {
                    var go = new GameObject("Part");
                    go.layer = UiLayer;
                    go.transform.SetParent(card.Portrait, false);
                    go.AddComponent<MeshFilter>();
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = litMaterial; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                    card.Parts.Add(r);
                }
                var bounds = new Bounds();
                for (int k = 0; k < card.Parts.Count; k++)
                {
                    bool on = k < model.Count;
                    card.Parts[k].gameObject.SetActive(on);
                    if (!on) continue;
                    var (mesh, at) = model[k];
                    card.Parts[k].GetComponent<MeshFilter>().sharedMesh = mesh;
                    var t = card.Parts[k].transform;
                    t.localPosition = at.GetColumn(3); t.localRotation = at.rotation; t.localScale = at.lossyScale;
                    var b = mesh.bounds; var centre = at.MultiplyPoint3x4(b.center);
                    if (k == 0) bounds = new Bounds(centre, b.size); else bounds.Encapsulate(new Bounds(centre, b.size));
                }
                float scale = 0.036f / Mathf.Max(0.001f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
                card.Portrait.localScale = Vector3.one * scale;
                portraitCentre = bounds.center;
            }
        }

        private Vector3 portraitCentre;

        public void HideCard() { if (card != null) card.Root.gameObject.SetActive(false); }

        private PaletteButton cardAction;

        /// <summary>A button in the card's header, beside Close (a villager's "Camera"); null label removes it.</summary>
        public void SetCardAction(string label, bool selected, Action click)
        {
            if (card == null) return;
            if (label == null) { if (cardAction) cardAction.gameObject.SetActive(false); return; }
            if (cardAction == null)
            {
                float top = PanelHeight / 2;
                cardAction = Button("Card action", card.Root, new Vector3(CardWidth / 2 - 0.019f - 0.037f, top - 0.019f, 0f), new Vector2(0.044f, 0.018f), label, false);
            }
            cardAction.gameObject.SetActive(true);
            cardAction.Label.text = label; cardAction.Selected = selected; cardAction.OnClick = click;
            cardAction.ApplyVisuals();
        }
        public bool CardShown => card != null && card.Root.gameObject.activeSelf;
        public string CardTitle => card != null ? card.Title.text : null;

        /// <summary>The panel on the right of the palette (help, the world's statistics); null text hides it.</summary>
        public const float CardWidth = 0.22f, InfoWidth = 0.23f;

        public void ShowInfo(string title, string body, Action close) => ShowInfo(title, null, body, close);

        public void ShowInfo(string title, string subtitle, string body, Action close)
        {
            if (body == null) { if (info != null) info.Root.gameObject.SetActive(false); SetInfoButtons(null); return; }
            info ??= MakePanel("Info", Width / 2 + InfoWidth / 2 + 0.006f, InfoWidth, false);
            info.Root.gameObject.SetActive(true);
            info.Title.text = title; info.Subtitle.text = subtitle ?? ""; info.Body.text = body;
            info.Close.OnClick = close;
        }

        public bool InfoShown => info != null && info.Root.gameObject.activeSelf;

        private readonly List<PaletteButton> infoButtons = new();

        /// <summary>
        /// Buttons along the bottom of the right-hand panel, two to a row (the Perf panel's settings); the body text
        /// shrinks to the space above them. Null or empty removes them. Call after <see cref="ShowInfo(string, string, string, Action)"/>.
        /// </summary>
        public void SetInfoButtons(IReadOnlyList<Option> list)
        {
            list ??= new List<Option>();
            if (info == null) { if (list.Count == 0) return; info = MakePanel("Info", Width / 2 + InfoWidth / 2 + 0.006f, InfoWidth, false); }
            PanelButtons(info, InfoWidth, infoButtons, list, 2);
        }

        private readonly List<PaletteButton> cardButtons = new();

        /// <summary>
        /// What can be done with what the card shows - Move, Remove, Stop, Always left... - as buttons along the bottom of
        /// the card, three to a row; the body shrinks to the space above them. Null or empty removes them. Call after
        /// <see cref="ShowCard(string, string, string, IReadOnlyList{ValueTuple{Mesh, Matrix4x4}}, Action)"/>.
        /// </summary>
        public void SetCardButtons(IReadOnlyList<Option> list)
        {
            list ??= new List<Option>();
            if (card == null) return;
            PanelButtons(card, CardWidth, cardButtons, list, 3);
        }

        public IReadOnlyList<PaletteButton> CardButtons => cardButtons;

        private void PanelButtons(Panel panel, float width, List<PaletteButton> pool, IReadOnlyList<Option> list, int perRow)
        {
            while (pool.Count > list.Count)
            {
                if (Application.isPlaying) Destroy(pool[^1].gameObject); else DestroyImmediate(pool[^1].gameObject);
                pool.RemoveAt(pool.Count - 1);
            }
            const float h = 0.0155f, gap = 0.003f;
            float w = (width - 0.016f - gap * (perRow - 1)) / perRow, bottom = -PanelHeight / 2 + 0.008f;
            int rows = (list.Count + perRow - 1) / perRow;
            for (int i = 0; i < list.Count; i++)
            {
                if (i >= pool.Count) pool.Add(Button("Setting", panel.Root, Vector3.zero, new Vector2(w, h), "", false));
                var b = pool[i];
                int row = i / perRow, col = i % perRow;
                int inRow = Mathf.Min(perRow, list.Count - row * perRow);
                float x = (col - (inRow - 1) / 2f) * (w + gap);
                b.transform.localPosition = new Vector3(x, bottom + (rows - 1 - row) * (h + gap) + h / 2, 0f);
                b.Label.text = list[i].Label;
                b.Selected = list[i].Selected; b.Enabled = list[i].Enabled;
                b.OnClick = list[i].OnClick;
                b.ApplyVisuals();
            }
            // The body fills what is left between the header and the buttons.
            float top = PanelHeight / 2 - 0.004f - 0.046f - 0.008f, low = rows > 0 ? bottom + rows * (h + gap) + 0.002f : -PanelHeight / 2 + 0.008f;
            panel.Body.rectTransform.localPosition = new Vector3(0f, (top + low) / 2, -0.002f);
            panel.Body.rectTransform.sizeDelta = new Vector2(width - 0.022f, top - low);
        }

        public IReadOnlyList<PaletteButton> InfoButtons => infoButtons;

        private void Update()
        {
            if (card == null || !card.Root.gameObject.activeSelf || card.Portrait == null) return;
            // The little model turns slowly about its own middle.
            float yaw = Time.time * 40f;
            var rot = Quaternion.Euler(-15f, yaw, 0f);
            card.Portrait.localRotation = rot;
            float s = card.Portrait.localScale.x;
            card.Portrait.localPosition = new Vector3(-0.196f / 2 + 0.024f, Height / 2 - 0.032f, -0.014f) - rot * portraitCentre * s;
        }

        /// <summary>The palette's title: "Wonderland · World 2".</summary>
        public void SetTitle(string text) { if (title != null) title.text = text; }

        public void SetTool(Tool tool)
        {
            foreach (var kv in tools) { kv.Value.Selected = kv.Key == tool; kv.Value.ApplyVisuals(); }
        }

        public struct Option
        {
            public string Label; public bool Selected; public bool Enabled; public Action OnClick;
            public Option(string label, bool selected, Action onClick, bool enabled = true) { Label = label; Selected = selected; OnClick = onClick; Enabled = enabled; }
        }

        /// <summary>Rebuilds the option chips (cheap: a handful of objects, only when the tool or mode changes).</summary>
        public void SetOptions(IReadOnlyList<Option> list) => FillRow(options, optionRow, list, 0.02f);

        /// <summary>The second row of chips (the items of a category, for the Build tool); empty hides it.</summary>
        public void SetItems(IReadOnlyList<Option> list) => FillRow(items, itemRow, list ?? new List<Option>(), 0.018f);

        private void FillRow(List<PaletteButton> options, Transform optionRow, IReadOnlyList<Option> list, float height)
        {
            while (options.Count > list.Count)
            {
                if (Application.isPlaying) Destroy(options[^1].gameObject); else DestroyImmediate(options[^1].gameObject);
                options.RemoveAt(options.Count - 1);
            }
            float w = Mathf.Min(0.06f, (Width - 0.02f - (list.Count - 1) * 0.004f) / Mathf.Max(1, list.Count));
            float total = list.Count * w + (list.Count - 1) * 0.004f;
            for (int i = 0; i < list.Count; i++)
            {
                if (i >= options.Count) options.Add(Button("Option", optionRow, Vector3.zero, new Vector2(w, height), "", false));
                var b = options[i];
                b.transform.localPosition = new Vector3(-total / 2 + w / 2 + i * (w + 0.004f), 0f, 0f);
                b.Label.text = list[i].Label;
                b.Selected = list[i].Selected; b.Enabled = list[i].Enabled;
                var action = list[i].OnClick;
                b.OnClick = action;
                ResizeButton(b, new Vector2(w, height));
                b.ApplyVisuals();
            }
        }

        /// <summary>
        /// What just happened, or what letting go will do: a banner just above the palette, two lines at a fixed, readable
        /// size (wrapping, never shrinking); hidden when there is nothing to say.
        /// </summary>
        public void SetStatus(string text)
        {
            text ??= "";
            if (statusText == text) return;
            statusText = text;
            if (message.root == null) message = Banner("Message", new Color(0.17f, 0.15f, 0.2f, 0.92f), new Color(1f, 0.96f, 0.88f));
            message.text.text = text;
            message.root.gameObject.SetActive(text.Length > 0);
            PlaceBanners();
        }

        /// <summary>A tip (amber banner above the message): how to use what is in hand, what to try next. Null hides it.</summary>
        public void SetTip(string text)
        {
            text ??= "";
            if (tipText == text) return;
            tipText = text;
            if (tip.root == null) tip = Banner("Tip", new Color(0.36f, 0.26f, 0.08f, 0.94f), new Color(1f, 0.9f, 0.62f));
            tip.text.text = text.Length > 0 ? $"<b>Tip</b>   {text}" : "";
            tip.root.gameObject.SetActive(text.Length > 0);
            PlaceBanners();
        }

        public string StatusText => statusText;
        public string TipText => tipText;

        private (Transform root, TextMeshPro text) message, tip;
        private string statusText = "", tipText = "";
        private const float BannerHeight = 0.028f;

        private (Transform, TextMeshPro) Banner(string name, Color back, Color ink)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            go.transform.SetParent(transform, false);
            Quad("Back", go.transform, Width, BannerHeight, 0.008f, back, Color.Lerp(back, Color.black, 0.15f), 0f, 2991);
            var t = Text("Text", go.transform, "", new Vector3(0f, 0f, -0.002f), new Vector2(Width - 0.016f, BannerHeight - 0.004f), 0.052f, TextAlignmentOptions.Center);
            t.enableAutoSizing = false; t.fontSize = 0.052f;
            t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Ellipsis;
            t.color = ink;
            go.SetActive(false);
            return (go.transform, t);
        }

        private void PlaceBanners()
        {
            float y = Height / 2 + 0.006f + BannerHeight / 2;
            if (message.root != null && message.root.gameObject.activeSelf) { message.root.localPosition = new Vector3(0f, y, 0f); y += BannerHeight + 0.004f; }
            if (tip.root != null && tip.root.gameObject.activeSelf) tip.root.localPosition = new Vector3(0f, y, 0f);
        }
        public void SetHint(string text) => hint.text = text;
        public string HintText => hint.text;

        /// <summary>The button under the ray (UI layer colliders), and hover state for all buttons.</summary>
        public PaletteButton Raycast(Ray ray, out float distance)
        {
            distance = 0f;
            PaletteButton hit = null;
            if (gameObject.activeInHierarchy && Physics.Raycast(ray, out var info, 1.5f, 1 << UiLayer, QueryTriggerInteraction.Collide))
            {
                hit = info.collider.GetComponent<PaletteButton>();
                distance = info.distance;
                if (hit == null && info.collider.transform == transform) distance = info.distance; // panel back: swallow the ray
                // From a steep angle the ray can graze the edge of a neighbouring chip first: the chip pressed is the one
                // whose face the pointer actually lands on.
                if (hit != null && !OnFace(hit, info.collider, ray, out _))
                    foreach (var other in Physics.RaycastAll(ray, 1.5f, 1 << UiLayer, QueryTriggerInteraction.Collide))
                        if (other.collider.GetComponent<PaletteButton>() is { } b && b != hit && OnFace(b, other.collider, ray, out float d)) { hit = b; distance = d; break; }
            }
            if (Hovered && Hovered != hit) Hovered.Hovered = false;
            Hovered = hit;
            if (hit) hit.Hovered = true;
            return hit;
        }

        /// <summary>Whether the ray crosses a button's face (its front plane, within its outline); where, along the ray.</summary>
        private static bool OnFace(PaletteButton b, Collider c, Ray ray, out float distance)
        {
            distance = 0f;
            if (c is not BoxCollider box) return true;
            var t = b.transform;
            if (!new Plane(t.forward, t.TransformPoint(box.center)).Raycast(ray, out distance)) return false;
            var p = t.InverseTransformPoint(ray.GetPoint(distance)) - box.center;
            return Mathf.Abs(p.x) <= box.size.x / 2f && Mathf.Abs(p.y) <= box.size.y / 2f;
        }

        public void ClearHover()
        {
            if (Hovered) Hovered.Hovered = false;
            Hovered = null;
        }

        public bool RayHitsPanel(Ray ray, out float distance)
        {
            distance = 0f;
            if (!gameObject.activeInHierarchy) return false;
            if (!Physics.Raycast(ray, out var info, 1.5f, 1 << UiLayer, QueryTriggerInteraction.Collide)) return false;
            distance = info.distance;
            return true;
        }

        // ------------------------------------------------------------------ construction helpers

        private PaletteButton Button(string name, Transform parent, Vector3 position, Vector2 size, string label, bool tile)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var b = go.AddComponent<PaletteButton>();
            b.Idle = tile ? new Color(1f, 1f, 1f, 0.09f) : new Color(1f, 1f, 1f, 0.12f);
            b.Hover = new Color(1f, 1f, 1f, 0.26f);
            b.Active = tile ? new Color(1f, 0.85f, 0.45f, 0.28f) : new Color(1f, 0.8f, 0.4f, 0.55f);
            b.Background = Quad("Background", go.transform, size.x, size.y, Mathf.Min(size.x, size.y) * 0.28f, Color.white, new Color(0.85f, 0.85f, 0.9f), -0.001f, 2992);
            if (tile) b.Ring = Ring("Selected", go.transform, size.x + 0.003f, size.y + 0.003f, 0.0016f, new Color(1f, 0.82f, 0.35f, 1f));
            b.Label = Text("Label", go.transform, label, new Vector3(0f, 0f, -0.003f), new Vector2(size.x - 0.004f, tile ? 0.009f : size.y - 0.005f), tile ? 0.045f : 0.05f, TextAlignmentOptions.Center);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.002f);   // thin (oblique rays must not clip a neighbour), just in front of the panel
            col.center = new Vector3(0f, 0f, -0.003f);
            return b;
        }

        private void ResizeButton(PaletteButton b, Vector2 size)
        {
            var mesh = RoundedRect(size.x, size.y, Mathf.Min(size.x, size.y) * 0.28f, Color.white, new Color(0.85f, 0.85f, 0.9f));
            b.Background.GetComponent<MeshFilter>().sharedMesh = mesh;
            b.GetComponent<BoxCollider>().size = new Vector3(size.x, size.y, 0.002f);
            b.Label.rectTransform.sizeDelta = new Vector2(size.x - 0.004f, size.y - 0.005f);
        }

        private MeshRenderer Quad(string name, Transform parent, float w, float h, float radius, Color top, Color bottom, float z, int queue)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.AddComponent<MeshFilter>().sharedMesh = RoundedRect(w, h, radius, top, bottom);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(uiMaterial) { renderQueue = queue };
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            if (name == "Panel")
            {
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(w, h, 0.004f);
            }
            return r;
        }

        private MeshRenderer Ring(string name, Transform parent, float w, float h, float thickness, Color colour)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.0015f);
            var outer = RoundedRectPoints(w, h, Mathf.Min(w, h) * 0.3f);
            var inner = RoundedRectPoints(w - thickness * 2f, h - thickness * 2f, Mathf.Min(w, h) * 0.3f - thickness);
            var b = new MeshBuilder();
            for (int i = 0; i < outer.Count; i++)
            {
                b.AddVertex(outer[i], Vector3.back, colour); b.AddVertex(inner[i], Vector3.back, colour);
            }
            for (int i = 0; i < outer.Count; i++)
            {
                int a = i * 2, n = ((i + 1) % outer.Count) * 2;
                b.AddTriangle(a, n, n + 1); b.AddTriangle(a, n + 1, a + 1);
            }
            go.AddComponent<MeshFilter>().sharedMesh = b.ToMesh("Ring");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(uiMaterial) { renderQueue = 2994 };
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        private static List<Vector3> RoundedRectPoints(float w, float h, float r)
        {
            var pts = new List<Vector3>();
            const int seg = 6;
            var corners = new[] { new Vector2(w / 2 - r, h / 2 - r), new Vector2(-w / 2 + r, h / 2 - r), new Vector2(-w / 2 + r, -h / 2 + r), new Vector2(w / 2 - r, -h / 2 + r) };
            for (int c = 0; c < 4; c++)
            for (int i = 0; i <= seg; i++)
            {
                float a = (c * 90f + i * 90f / seg) * Mathf.Deg2Rad;
                pts.Add(new Vector3(corners[c].x + Mathf.Cos(a) * r, corners[c].y + Mathf.Sin(a) * r, 0f));
            }
            return pts;
        }

        public static Mesh RoundedRect(float w, float h, float r, Color top, Color bottom)
        {
            var b = new MeshBuilder();
            var pts = RoundedRectPoints(w, h, r);
            int centre = b.AddVertex(Vector3.zero, Vector3.back, Color.Lerp(top, bottom, 0.5f));
            foreach (var p in pts) b.AddVertex(p, Vector3.back, Color.Lerp(bottom, top, p.y / h + 0.5f));
            for (int i = 0; i < pts.Count; i++) b.AddTriangle(centre, 1 + (i + 1) % pts.Count, 1 + i);
            return b.ToMesh("RoundedRect");
        }

        private TextMeshPro Text(string name, Transform parent, string text, Vector3 position, Vector2 size, float fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = size;
            t.rectTransform.localPosition = position;
            t.text = text;
            t.fontSize = fontSize;
            t.enableAutoSizing = true;
            t.fontSizeMin = fontSize * 0.5f; t.fontSizeMax = fontSize;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.color = Color.white;
            t.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        /// <summary>A tiny 3D model of what each tool makes, floating on its tile.</summary>
        private Transform Icon(Tool tool, Transform parent)
        {
            Mesh mesh = tool switch
            {
                Tool.Terrain => HillIcon(),
                Tool.Water => PondIcon(),
                Tool.Forest => TreeFactory.Get(TreeKind.Oak, 0),
                Tool.Track => TrackIcon(),
                Tool.Paths => PathIcon(),
                Tool.Train => TrainFactory.LocoMesh(),
                Tool.Houses => HouseFactory.Get(Catalog.Church),
                Tool.Decor => SheepIcon(),
                Tool.Select => HouseFactory.CameraHead(),
                _ => EraserIcon(),
            };
            var pivot = new GameObject("Icon").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = new Vector3(0f, 0.004f, -0.012f);
            var go = new GameObject("Model");
            go.layer = UiLayer;
            go.transform.SetParent(pivot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = litMaterial; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            var bounds = mesh.bounds;
            float scale = 0.026f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = -bounds.center * scale;
            return pivot;
        }

        private static Mesh HillIcon()
        {
            var b = new MeshBuilder();
            b.AddCylinder(new Vector3(0f, -0.004f, 0f), 0.05f, 0.05f, 0.004f, 20, Pal.Hex("#7A5232"), Pal.Hex("#5E8F33"), true);
            b.AddBlob(new Vector3(-0.012f, 0f, 0f), new Vector3(0.034f, 0.026f, 0.03f), 2, 0.1f, 3, (d, _) => Color.Lerp(Pal.Hex("#3E7E2C"), Pal.Hex("#8DC24D"), d.y * 0.6f + 0.4f));
            b.AddBlob(new Vector3(0.022f, 0f, 0.008f), new Vector3(0.022f, 0.016f, 0.02f), 1, 0.1f, 5, (d, _) => Color.Lerp(Pal.Hex("#3E7E2C"), Pal.Hex("#A6CF57"), d.y * 0.6f + 0.4f));
            var m = b.ToMesh("HillIcon");
            return m;
        }

        private static Mesh SheepIcon()
        {
            var b = new MeshBuilder();
            b.Append(new MeshData(AnimalFactory.Body(AnimalKind.Sheep)), Matrix4x4.identity, Color.white);
            b.Append(new MeshData(AnimalFactory.Head(AnimalKind.Sheep)), Matrix4x4.TRS(AnimalFactory.NeckOffset(AnimalKind.Sheep), Quaternion.Euler(10f, 0f, 0f), Vector3.one), Color.white);
            return b.ToMesh("SheepIcon");
        }

        private static Mesh PondIcon()
        {
            var b = new MeshBuilder();
            var grass = Pal.Hex("#5E9A3A");
            b.AddCylinder(new Vector3(0f, -0.004f, 0f), 0.05f, 0.05f, 0.004f, 24, Pal.Hex("#7A5232"), grass, true);
            b.AddCylinder(new Vector3(0f, 0.0002f, 0f), 0.036f, 0.036f, 0.001f, 24, Pal.Hex("#2E7FA8"), Pal.Hex("#5BC3D8"), true);
            b.AddDisc(new Vector3(-0.01f, 0.0014f, 0.006f), 0.006f, 10, Color.white, true);
            var rng = new Rng(5);
            for (int i = 0; i < 7; i++)
            {
                float a = 2.2f + i * 0.22f;
                var p = new Vector3(Mathf.Cos(a) * 0.04f, 0f, Mathf.Sin(a) * 0.04f);
                b.AddCylinder(p, 0.0016f, 0.0008f, rng.Range(0.014f, 0.026f), 4, Pal.Hex("#3E7A33"), Pal.Hex("#8FBF4A"), false);
                if (i % 2 == 0) b.AddCylinder(p + Vector3.up * 0.016f, 0.0022f, 0.0022f, 0.006f, 6, Pal.Hex("#6B4630"), Pal.Hex("#6B4630"), true);
            }
            return b.ToMesh("PondIcon");
        }

        private static Mesh PathIcon()
        {
            var b = new MeshBuilder();
            b.AddBox(new Vector3(0f, -0.004f, 0f), new Vector3(0.11f, 0.006f, 0.08f), Pal.Hex("#5E9A3E"));
            for (int i = 0; i < 8; i++)
            {
                float t = i / 7f, x = -0.05f + t * 0.1f, z = Mathf.Sin(t * 3f) * 0.018f;
                b.AddBox(new Vector3(x, 0.0f, z), new Vector3(0.016f, 0.003f, 0.03f), i % 2 == 0 ? Pal.Hex("#C9BBA3") : Pal.Hex("#B5AA98"));
            }
            for (float x = -0.05f; x <= 0.051f; x += 0.017f) b.AddBox(new Vector3(x, 0.01f, 0.034f), new Vector3(0.004f, 0.022f, 0.004f), Pal.Hex("#8A5A36"));
            b.AddBox(new Vector3(0f, 0.012f, 0.034f), new Vector3(0.1f, 0.003f, 0.002f), Pal.Hex("#A8743E"));
            b.AddBox(new Vector3(0f, 0.004f, 0.034f), new Vector3(0.1f, 0.003f, 0.002f), Pal.Hex("#A8743E"));
            return b.ToMesh("PathIcon");
        }

        private static Mesh TrackIcon()
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= 12; i++) { float a = i / 12f * 0.9f - 0.45f; pts.Add(new Vector3(Mathf.Sin(a) * 0.12f, 0f, Mathf.Cos(a) * 0.12f - 0.12f)); }
            var b = new MeshBuilder();
            TrackSystem.BuildTrack(b, new TrackLayout(pts, false));
            return b.ToMesh("TrackIcon");
        }

        private static Mesh EraserIcon()
        {
            var b = new MeshBuilder();
            b.Push(Vector3.zero, Quaternion.Euler(0f, 0f, 20f));
            b.AddBox(new Vector3(-0.012f, 0f, 0f), new Vector3(0.026f, 0.016f, 0.02f), Pal.Hex("#F28AA0"), Pal.Hex("#FFB3C2"), Pal.Hex("#C96A80"), false);
            b.AddBox(new Vector3(0.016f, 0f, 0f), new Vector3(0.03f, 0.016f, 0.02f), Pal.Hex("#4F7FC8"), Pal.Hex("#77A3E6"), Pal.Hex("#3B62A0"), false);
            b.Pop();
            return b.ToMesh("EraserIcon");
        }
    }
}
