using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// What pressing a button does - and so how it looks, so that buttons doing different kinds of thing never look
    /// alike: a <b>Choice</b> picks one of a set (the chosen one gold), a <b>Toggle</b> switches something on or off (a
    /// light on it, green when on), an <b>Action</b> does something at once (outlined), <b>Open</b> opens another panel
    /// (blue, with ›) and <b>Danger</b> can't be undone easily (red).
    /// </summary>
    public enum OptionKind { Choice, Toggle, Action, Open, Danger }

    /// <summary>
    /// The palette's look and its parts beyond the tool tiles: buttons dressed by what they do, tooltips, captioned rows,
    /// the detail panel on the left (a tool's options, each with what it does), tabs in the right-hand panel (Help's
    /// topics), the header's buttons and the bottom bar in clusters.
    /// </summary>
    public sealed partial class PaletteUI
    {
        private static readonly Color OpenTint = new(0.42f, 0.62f, 1f, 0.3f), OpenHover = new(0.55f, 0.72f, 1f, 0.45f);
        private static readonly Color DangerTint = new(1f, 0.38f, 0.34f, 0.3f), DangerHover = new(1f, 0.5f, 0.45f, 0.48f);
        private static readonly Color ActionTint = new(1f, 1f, 1f, 0.17f);
        public const string MutedInk = "#D2CBDD";

        /// <summary>Sets a button up for an option: its words, its state, what it does, and the look of its kind.</summary>
        private void Dress(PaletteButton b, Option o)
        {
            b.Kind = o.Kind; b.Tip = o.Tip; b.Name = o.Label;
            b.Selected = o.Selected; b.Enabled = o.Enabled; b.OnClick = o.OnClick;
            string label = o.Label ?? "";
            if (o.Kind == OptionKind.Open && !label.EndsWith("›")) label += " ›";
            b.Label.text = label;
            (b.Idle, b.Hover, b.Active) = o.Kind switch
            {
                OptionKind.Open => (OpenTint, OpenHover, new Color(0.55f, 0.72f, 1f, 0.6f)),
                OptionKind.Danger => (DangerTint, DangerHover, new Color(1f, 0.45f, 0.4f, 0.7f)),
                OptionKind.Action => (ActionTint, new Color(1f, 1f, 1f, 0.3f), new Color(1f, 1f, 1f, 0.4f)),
                OptionKind.Toggle => (new Color(1f, 1f, 1f, 0.12f), new Color(1f, 1f, 1f, 0.26f), new Color(0.45f, 0.95f, 0.5f, 0.22f)),
                _ => (new Color(1f, 1f, 1f, 0.12f), new Color(1f, 1f, 1f, 0.26f), new Color(1f, 0.8f, 0.4f, 0.55f)),
            };
            // A switch's light, at its left; its words make room for it.
            var size = b.GetComponent<BoxCollider>().size;
            if (o.Kind == OptionKind.Toggle)
            {
                if (b.Light == null)
                {
                    b.Light = Quad("Light", b.transform, 0.0055f, 0.0055f, 0.00275f, Color.white, Color.white, -0.0025f, 2995);
                    b.Light.material = new Material(uiMaterial) { renderQueue = 2995 };
                }
                b.Light.gameObject.SetActive(true);
                b.Light.transform.localPosition = new Vector3(-size.x / 2 + 0.0065f, 0f, -0.0025f);
                b.Label.rectTransform.localPosition = new Vector3(0.0045f, b.Label.rectTransform.localPosition.y, -0.003f);
                b.Label.rectTransform.sizeDelta = new Vector2(size.x - 0.013f, b.Label.rectTransform.sizeDelta.y);
            }
            else if (b.Light != null)
            {
                b.Light.gameObject.SetActive(false);
                b.Label.rectTransform.localPosition = new Vector3(0f, b.Label.rectTransform.localPosition.y, -0.003f);
            }
            b.ApplyVisuals();
        }

        // ------------------------------------------------------------------ row captions

        private readonly Dictionary<Transform, TextMeshPro> captions = new();

        /// <summary>A small caption at the left of a row of chips ("BRUSH", "CATEGORY"); null removes it.</summary>
        public void SetCaption(bool itemsRow, string text)
        {
            var row = itemsRow ? itemRow : optionRow;
            if (!captions.TryGetValue(row, out var t))
            {
                t = Text("Caption", row, "", new Vector3(-Width / 2 + 0.006f + CaptionWidth / 2, 0f, -0.002f), new Vector2(CaptionWidth, 0.016f), 0.04f, TextAlignmentOptions.Left);
                t.color = new Color(0.86f, 0.76f, 0.56f); t.fontStyle = FontStyles.Bold; t.characterSpacing = 4f;
                t.textWrappingMode = TextWrappingModes.Normal;
                captions[row] = t;
            }
            t.text = text == null ? "" : text.ToUpperInvariant();
        }

        private const float CaptionWidth = 0.042f;

        /// <summary>How much of a row its caption takes.</summary>
        private float RowLead(Transform row) => captions.TryGetValue(row, out var t) && t.text.Length > 0 ? CaptionWidth + 0.004f : 0f;

        // ------------------------------------------------------------------ tooltips

        private Transform tooltip;
        private TextMeshPro tooltipText;
        private MeshRenderer tooltipBack;
        private PaletteButton tooltipFor;
        private float tooltipHover;
        public const float TooltipDelay = 0.45f, TooltipWidth = 0.15f;

        /// <summary>The tooltip showing now (tests), or null.</summary>
        public string TooltipShown => tooltip != null && tooltip.gameObject.activeSelf ? tooltipText.text : null;

        /// <summary>
        /// Pointing at a button for a moment shows what it does, in a sentence or two, in a dark bubble just above it
        /// (below it near the panel's top); it goes when the pointer moves off.
        /// </summary>
        private void UpdateTooltip(float dt)
        {
            var b = Hovered;
            if (b == null || string.IsNullOrEmpty(b.Tip)) { tooltipHover = 0f; if (tooltip) tooltip.gameObject.SetActive(false); tooltipFor = null; return; }
            if (b != tooltipFor) { tooltipFor = b; tooltipHover = 0f; if (tooltip) tooltip.gameObject.SetActive(false); }
            tooltipHover += dt;
            if (tooltipHover < TooltipDelay) return;
            if (tooltip == null)
            {
                var go = new GameObject("Tooltip") { layer = UiLayer };
                go.transform.SetParent(transform, false);
                tooltip = go.transform;
                tooltipBack = Quad("Back", tooltip, TooltipWidth, 0.03f, 0.005f, new Color(0.06f, 0.05f, 0.08f, 0.97f), new Color(0.04f, 0.03f, 0.05f, 0.97f), 0f, 3010);   // (over every label)
                tooltipText = Text("Text", tooltip, "", new Vector3(0f, 0f, -0.001f), new Vector2(TooltipWidth - 0.01f, 0.03f), 0.048f, TextAlignmentOptions.Left);
                tooltipText.enableAutoSizing = false; tooltipText.fontSize = 0.048f;
                tooltipText.textWrappingMode = TextWrappingModes.Normal; tooltipText.overflowMode = TextOverflowModes.Overflow;
                tooltipText.color = new Color(0.97f, 0.95f, 0.9f);
                tooltipText.fontMaterial = new Material(tooltipText.fontSharedMaterial) { renderQueue = 3011 };   // (its own: the font's is everyone's)
            }
            if (!tooltip.gameObject.activeSelf || tooltipText.text != b.Tip)
            {
                tooltipText.text = b.Tip;
                float h = Mathf.Max(0.014f, tooltipText.GetPreferredValues(b.Tip, TooltipWidth - 0.01f, 1f).y) + 0.008f;
                tooltipText.rectTransform.sizeDelta = new Vector2(TooltipWidth - 0.01f, h - 0.006f);
                tooltipBack.GetComponent<MeshFilter>().sharedMesh = RoundedRect(TooltipWidth, h, 0.005f, new Color(0.06f, 0.05f, 0.08f, 0.97f), new Color(0.04f, 0.03f, 0.05f, 0.97f));
                var at = transform.InverseTransformPoint(b.transform.position);
                float bh = b.GetComponent<BoxCollider>().size.y;
                bool below = at.y + bh / 2 + h + 0.004f > Height / 2 + 0.02f && at.y < Height / 2;   // (no room above near the top: below)
                float y = below ? at.y - bh / 2 - 0.003f - h / 2 : at.y + bh / 2 + 0.003f + h / 2;
                tooltip.localPosition = new Vector3(at.x, y, at.z - 0.006f);
                tooltip.gameObject.SetActive(true);
            }
        }

        // ------------------------------------------------------------------ the detail panel (left)

        /// <summary>One of a tool's options in the detail panel: what it is called, what it does, and the button behind it.</summary>
        public struct DetailRow
        {
            public Option Option;
            public string Description;
            public DetailRow(Option option, string description) { Option = option; Description = description; }
        }

        private sealed class Details
        {
            public Transform Root;
            public TextMeshPro Title, Subtitle, Footer;
            public PaletteButton Prev, Next;
            public readonly List<PaletteButton> Rows = new();
        }

        private Details details;
        private readonly List<DetailRow> detailRows = new();
        private int detailPage;
        private string detailKey;
        private bool detailsWanted;
        public const int DetailsPerPage = 5;
        private const float RowH = 0.033f, RowGap = 0.0032f;

        public bool DetailsShown => details != null && details.Root.gameObject.activeSelf;
        public string DetailsTitle => details != null ? details.Title.text : null;

        /// <summary>
        /// The detail panel beside the palette (left): a tool's options as rows, each with its name and what it does;
        /// pressing a row chooses it (or does it, or switches it). A footer says how the left stick adjusts it. Six rows
        /// to a page; the page with the chosen row is shown. A card (Select) or the Build library takes its place.
        /// </summary>
        public void ShowDetails(string title, string subtitle, IReadOnlyList<DetailRow> rows, string footer = null)
        {
            details ??= MakeDetails();
            detailsWanted = true;
            string key = title + "|" + string.Join(",", System.Linq.Enumerable.Select(rows, r => r.Option.Label));
            detailRows.Clear(); detailRows.AddRange(rows);
            if (key != detailKey) { detailKey = key; detailPage = Mathf.Max(0, detailRows.FindIndex(r => r.Option.Selected)) / DetailsPerPage; }
            details.Title.text = title; details.Subtitle.text = subtitle ?? ""; details.Footer.text = footer ?? "";
            details.Root.gameObject.SetActive(!CardShown && !LibraryShown);
            ShowDetailPage();
        }

        public void HideDetails()
        {
            detailsWanted = false;
            if (details != null) details.Root.gameObject.SetActive(false);
        }

        private Details MakeDetails()
        {
            var d = new Details();
            var go = new GameObject("Details") { layer = UiLayer };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(-Width / 2 - LibraryWidth / 2 - 0.006f, 0f, 0f);
            d.Root = go.transform;
            float h = PanelHeight, top = h / 2;
            Quad("Border", d.Root, LibraryWidth + 0.006f, h + 0.006f, 0.02f, new Color(1f, 0.93f, 0.8f, 0.3f), new Color(1f, 0.93f, 0.8f, 0.15f), 0.002f, 2990);
            Quad("Panel", d.Root, LibraryWidth, h, 0.018f, new Color(0.17f, 0.15f, 0.2f, 0.98f), new Color(0.1f, 0.09f, 0.12f, 0.98f), 0f, 2991);
            const float band = 0.04f;
            var header = Quad("Header", d.Root, LibraryWidth - 0.008f, band, 0.012f, new Color(0.26f, 0.22f, 0.3f, 0.95f), new Color(0.2f, 0.17f, 0.24f, 0.95f), -0.001f, 2992);
            header.transform.localPosition = new Vector3(0f, top - 0.004f - band / 2, -0.001f);
            d.Title = Text("Title", d.Root, "", new Vector3(0f, top - 0.016f, -0.002f), new Vector2(LibraryWidth - 0.02f, 0.016f), 0.075f, TextAlignmentOptions.Left);
            d.Title.color = new Color(1f, 0.88f, 0.6f); d.Title.fontStyle = FontStyles.Bold;
            d.Subtitle = Text("Subtitle", d.Root, "", new Vector3(0f, top - 0.032f, -0.002f), new Vector2(LibraryWidth - 0.02f, 0.012f), 0.046f, TextAlignmentOptions.Left);
            d.Subtitle.color = new Color(0.82f, 0.79f, 0.88f);
            d.Footer = Text("Footer", d.Root, "", new Vector3(-0.016f, -top + 0.012f, -0.002f), new Vector2(LibraryWidth - 0.06f, 0.016f), 0.042f, TextAlignmentOptions.Left);
            d.Footer.color = new Color(0.78f, 0.75f, 0.84f); d.Footer.textWrappingMode = TextWrappingModes.Normal;
            d.Prev = Button("Previous", d.Root, new Vector3(LibraryWidth / 2 - 0.042f, -top + 0.012f, 0f), new Vector2(0.02f, 0.014f), "‹", false);
            d.Next = Button("Next", d.Root, new Vector3(LibraryWidth / 2 - 0.016f, -top + 0.012f, 0f), new Vector2(0.026f, 0.014f), "›", false);
            d.Prev.OnClick = () => { detailPage = Mathf.Max(0, detailPage - 1); ShowDetailPage(); };
            d.Next.OnClick = () => { detailPage = Mathf.Min(DetailPages - 1, detailPage + 1); ShowDetailPage(); };
            return d;
        }

        public int DetailPages => Mathf.Max(1, (detailRows.Count + DetailsPerPage - 1) / DetailsPerPage);

        private void ShowDetailPage()
        {
            if (details == null) return;
            detailPage = Mathf.Clamp(detailPage, 0, DetailPages - 1);
            float top = PanelHeight / 2 - 0.004f - 0.04f - 0.006f, w = LibraryWidth - 0.016f;
            int first = detailPage * DetailsPerPage, n = Mathf.Min(DetailsPerPage, detailRows.Count - first);
            while (details.Rows.Count < DetailsPerPage)
            {
                var b = Button("Row", details.Root, Vector3.zero, new Vector2(w, RowH), "", false);
                b.Label.alignment = TextAlignmentOptions.MidlineLeft;
                b.Label.enableAutoSizing = false; b.Label.fontSize = 0.056f;
                b.Label.textWrappingMode = TextWrappingModes.Normal; b.Label.overflowMode = TextOverflowModes.Ellipsis;
                b.Label.lineSpacing = -10f;
                b.Label.rectTransform.sizeDelta = new Vector2(w - 0.01f, RowH - 0.003f);
                details.Rows.Add(b);
            }
            for (int k = 0; k < details.Rows.Count; k++)
            {
                var b = details.Rows[k];
                bool on = k < n;
                b.gameObject.SetActive(on);
                if (!on) continue;
                var r = detailRows[first + k];
                b.transform.localPosition = new Vector3(0f, top - RowH / 2 - k * (RowH + RowGap), 0f);
                var o = r.Option;
                string name = o.Label + (o.Kind == OptionKind.Open ? " ›" : "");
                var shown = o; shown.Label = string.IsNullOrEmpty(r.Description) ? $"<b>{name}</b>" : $"<b>{name}</b>\n<size=88%><color={MutedInk}>{r.Description}</color></size>";
                if (o.OnClick == null) shown.Kind = OptionKind.Action;   // (a row that only explains)
                Dress(b, shown);
                b.Label.text = shown.Label;   // (the arrow, if any, after the name - not after the description)
                b.Name = o.Label;
                b.Label.rectTransform.sizeDelta = new Vector2(w - (o.Kind == OptionKind.Toggle ? 0.016f : 0.01f), RowH - 0.003f);
                if (o.OnClick == null) { b.Idle = new Color(1f, 1f, 1f, 0.05f); b.Hover = b.Idle; }
                if (o.Kind == OptionKind.Choice) b.Active = new Color(1f, 0.8f, 0.4f, 0.32f);   // (chosen: gold, soft enough to read on)
                b.ApplyVisuals();
            }
            bool paged = DetailPages > 1;
            details.Prev.gameObject.SetActive(paged); details.Next.gameObject.SetActive(paged);
            if (paged) { details.Next.Label.text = $"{detailPage + 1}/{DetailPages} ›"; details.Prev.Enabled = detailPage > 0; details.Next.Enabled = true; details.Prev.ApplyVisuals(); details.Next.ApplyVisuals(); }
        }

        /// <summary>The left slot: a card first, then the library, then the detail panel.</summary>
        private void UpdateLeftSlot()
        {
            if (details == null) return;
            bool want = detailsWanted && !CardShown && !LibraryShown;
            if (details.Root.gameObject.activeSelf != want) details.Root.gameObject.SetActive(want);
        }

        // ------------------------------------------------------------------ tabs in the right-hand panel

        private readonly List<PaletteButton> infoTabs = new();
        private List<Option> lastInfoButtons = new();
        private const float TabH = 0.016f, TabGap = 0.003f;
        public const int TabsPerRow = 4;

        private float TabsHeight(Panel panel)
        {
            if (panel != info || infoTabs.Count == 0 || !infoTabs[0].gameObject.activeSelf) return 0f;
            int rows = (infoTabs.Count + TabsPerRow - 1) / TabsPerRow;
            return rows * (TabH + TabGap) + 0.002f;
        }

        public IReadOnlyList<PaletteButton> InfoTabs => infoTabs;

        /// <summary>
        /// Tabs along the top of the right-hand panel, under its header, four to a row (Help's topics); the body starts
        /// below them. Null or empty removes them. Call after ShowInfo.
        /// </summary>
        public void SetInfoTabs(IReadOnlyList<Option> list)
        {
            list ??= new List<Option>();
            if (info == null) { if (list.Count == 0) return; info = MakePanel("Info", Width / 2 + InfoWidth / 2 + 0.006f, InfoWidth, false); }
            while (infoTabs.Count > list.Count) { if (Application.isPlaying) Destroy(infoTabs[^1].gameObject); else DestroyImmediate(infoTabs[^1].gameObject); infoTabs.RemoveAt(infoTabs.Count - 1); }
            float w = (InfoWidth - 0.016f - TabGap * (TabsPerRow - 1)) / TabsPerRow, top = PanelHeight / 2 - 0.004f - 0.046f - 0.004f;
            for (int i = 0; i < list.Count; i++)
            {
                if (i >= infoTabs.Count) infoTabs.Add(Button("Tab", info.Root, Vector3.zero, new Vector2(w, TabH), "", false));
                var b = infoTabs[i];
                int row = i / TabsPerRow, col = i % TabsPerRow;
                b.transform.localPosition = new Vector3(-InfoWidth / 2 + 0.008f + w / 2 + col * (w + TabGap), top - TabH / 2 - row * (TabH + TabGap), 0f);
                b.gameObject.SetActive(true);
                Dress(b, list[i]);
            }
            PanelButtons(info, InfoWidth, infoButtons, lastInfoButtons, 2);
        }

        // ------------------------------------------------------------------ the header's buttons, and the bottom bar in clusters

        private readonly List<PaletteButton> headerButtons = new();

        /// <summary>A small button in the palette's header, at its right (Help, Settings).</summary>
        public PaletteButton AddHeaderButton(Option o)
        {
            var b = Button(o.Label, transform, Vector3.zero, new Vector2(0.046f, 0.016f), o.Label, false);
            Dress(b, o);
            headerButtons.Add(b);
            for (int k = 0; k < headerButtons.Count; k++)
                headerButtons[k].transform.localPosition = new Vector3(Width / 2 - 0.008f - 0.023f - (headerButtons.Count - 1 - k) * 0.05f, Height / 2 - 0.0115f, 0f);
            // The clock and its light move left to make room.
            float right = Width / 2 - 0.008f - headerButtons.Count * 0.05f;
            if (clock != null) { clock.rectTransform.sizeDelta = new Vector2(0.034f, 0.012f); clock.rectTransform.localPosition = new Vector3(right - 0.028f, Height / 2 - 0.011f, -0.002f); }
            if (health != null) health.transform.localPosition = new Vector3(right - 0.004f, Height / 2 - 0.011f, -0.002f);
            return b;
        }

        private readonly List<int> globalClusters = new();
        private readonly List<MeshRenderer> clusterRules = new();

        /// <summary>
        /// A button on the bottom bar, in a cluster of its kind (editing, the world, worlds): clusters are set apart by a
        /// gap and a faint rule, so the bar reads as a few groups rather than a row of look-alikes.
        /// </summary>
        public PaletteButton AddGlobal(Option o, int cluster)
        {
            var b = Button(o.Label, transform, Vector3.zero, new Vector2(GlobalWidth, 0.02f), o.Label, false);
            Dress(b, o);
            globals.Add(b); globalClusters.Add(cluster);
            LayoutGlobals();
            return b;
        }

        private void LayoutGlobals()
        {
            int clusters = 0; for (int k = 0; k < globalClusters.Count; k++) if (k == 0 || globalClusters[k] != globalClusters[k - 1]) clusters++;
            const float gap = 0.004f, between = 0.016f;
            float w = (Width - 0.012f - gap * (globals.Count - clusters) - between * (clusters - 1)) / Mathf.Max(1, globals.Count);
            w = Mathf.Min(w, 0.058f);
            float total = w * globals.Count + gap * (globals.Count - clusters) + between * (clusters - 1);
            float x = -total / 2;
            foreach (var r in clusterRules) if (r) { if (Application.isPlaying) Destroy(r.gameObject); else DestroyImmediate(r.gameObject); }
            clusterRules.Clear();
            for (int k = 0; k < globals.Count; k++)
            {
                if (k > 0) x += globalClusters[k] != globalClusters[k - 1] ? between : gap;
                if (k > 0 && globalClusters[k] != globalClusters[k - 1])
                {
                    var rule = Quad("Rule", transform, 0.0007f, 0.016f, 0.0003f, new Color(1f, 0.93f, 0.8f, 0.3f), new Color(1f, 0.93f, 0.8f, 0.3f), -0.001f, 2993);
                    rule.transform.localPosition = new Vector3(x - between / 2, -0.078f, -0.001f);
                    clusterRules.Add(rule);
                }
                globals[k].transform.localPosition = new Vector3(x + w / 2, -0.078f, 0f);
                ResizeButton(globals[k], new Vector2(w, 0.02f));
                x += w;
            }
        }

        /// <summary>A button on the bottom bar or in the header, by its words (tests).</summary>
        public PaletteButton GlobalButton(string label)
        {
            foreach (var b in globals) if (b.Text.StartsWith(label)) return b;
            foreach (var b in headerButtons) if (b.Text.StartsWith(label)) return b;
            return null;
        }
    }
}
