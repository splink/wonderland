using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// The library beside the palette (Build): a family of objects at a time as tiles, each with a little model of the
    /// object and its name, eight to a page; under them, what the object pointed at (or chosen) is and does in the
    /// world - who works there, what it makes and needs, and where it has to go. The palette's rows choose the category
    /// and the family; the tiles choose the object. It sits on the palette's left, out of the pointing hand's way; a
    /// card (Select) takes its place while open.
    /// </summary>
    public sealed partial class PaletteUI
    {
        public const float LibraryWidth = 0.25f;
        public const int TilesPerPage = 8;
        private const float TileW = 0.056f, TileH = 0.07f, TileGap = 0.005f;

        public struct LibraryTile
        {
            public int Id; public string Label; public Mesh Model; public bool Selected; public Action OnClick;
        }

        private sealed class Library
        {
            public Transform Root;
            public TextMeshPro Title, Page, Info;
            public PaletteButton Prev, Next;
            public readonly List<(PaletteButton button, Transform model, MeshFilter filter, int id)> Tiles = new();
        }

        private Library library;
        private readonly List<LibraryTile> libraryTiles = new();
        private Func<int, string> describe;
        private int libraryPage, libraryShownId = -2;
        private bool libraryWanted;
        private string libraryKey;

        public bool LibraryShown => library != null && library.Root.gameObject.activeSelf;
        /// <summary>The object whose tile is pointed at (-1: none).</summary>
        public int LibraryHovered { get; private set; } = -1;
        public int LibraryPage => libraryPage;
        public int LibraryPages => Mathf.Max(1, (libraryTiles.Count + TilesPerPage - 1) / TilesPerPage);
        public string LibraryInfo => library != null ? library.Info.text : null;

        /// <summary>Shows a family's objects as tiles (the page with the chosen one); <paramref name="info"/> describes an object.</summary>
        public void ShowLibrary(string title, IReadOnlyList<LibraryTile> tiles, Func<int, string> info)
        {
            library ??= MakeLibrary();
            libraryWanted = true;
            describe = info;
            string key = title + "|" + string.Join(",", System.Linq.Enumerable.Select(tiles, t => t.Id));
            libraryTiles.Clear(); libraryTiles.AddRange(tiles);
            if (key != libraryKey) { libraryKey = key; libraryPage = Mathf.Max(0, libraryTiles.FindIndex(t => t.Selected)) / TilesPerPage; }
            library.Title.text = title;
            library.Root.gameObject.SetActive(!CardShown);
            ShowLibraryPage();
        }

        public void HideLibrary()
        {
            libraryWanted = false;
            if (library != null) library.Root.gameObject.SetActive(false);
        }

        private Library MakeLibrary()
        {
            var l = new Library();
            var go = new GameObject("Library");
            go.layer = UiLayer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(-Width / 2 - LibraryWidth / 2 - 0.006f, 0f, 0f);   // (on the left: out of the way of the pointing hand)
            l.Root = go.transform;
            float h = PanelHeight, top = h / 2;
            Quad("Border", l.Root, LibraryWidth + 0.006f, h + 0.006f, 0.02f, new Color(1f, 0.93f, 0.8f, 0.3f), new Color(1f, 0.93f, 0.8f, 0.15f), 0.002f, 2990);
            Quad("Panel", l.Root, LibraryWidth, h, 0.018f, new Color(0.17f, 0.15f, 0.2f, 0.94f), new Color(0.1f, 0.09f, 0.12f, 0.94f), 0f, 2991);
            l.Title = Text("Title", l.Root, "", new Vector3(-0.02f, top - 0.014f, -0.002f), new Vector2(LibraryWidth - 0.07f, 0.014f), 0.07f, TextAlignmentOptions.Left);
            l.Title.color = new Color(1f, 0.88f, 0.6f); l.Title.fontStyle = FontStyles.Bold; l.Title.fontSizeMin = 0.05f;
            l.Prev = Button("Library back", l.Root, new Vector3(LibraryWidth / 2 - 0.05f, top - 0.014f, 0f), new Vector2(0.016f, 0.016f), "‹", false);
            l.Next = Button("Library on", l.Root, new Vector3(LibraryWidth / 2 - 0.014f, top - 0.014f, 0f), new Vector2(0.016f, 0.016f), "›", false);
            l.Page = Text("Page", l.Root, "", new Vector3(LibraryWidth / 2 - 0.032f, top - 0.014f, -0.002f), new Vector2(0.02f, 0.012f), 0.045f, TextAlignmentOptions.Center);
            l.Prev.OnClick = () => { libraryPage = (libraryPage + LibraryPages - 1) % LibraryPages; ShowLibraryPage(); };
            l.Next.OnClick = () => { libraryPage = (libraryPage + 1) % LibraryPages; ShowLibraryPage(); };
            l.Prev.ApplyVisuals(); l.Next.ApplyVisuals();
            // Two rows of four tiles.
            float gridTop = top - 0.028f;
            for (int i = 0; i < TilesPerPage; i++)
            {
                int row = i / 4, col = i % 4;
                var pos = new Vector3((col - 1.5f) * (TileW + TileGap), gridTop - TileH / 2 - row * (TileH + TileGap), 0f);
                var b = Button("Tile", l.Root, pos, new Vector2(TileW, TileH), "", true);
                b.Label.rectTransform.localPosition = new Vector3(0f, -TileH / 2 + 0.0065f, -0.003f);
                b.Label.rectTransform.sizeDelta = new Vector2(TileW - 0.004f, 0.009f);
                b.Label.enableAutoSizing = true; b.Label.fontSizeMin = 0.03f; b.Label.fontSizeMax = 0.048f;
                var model = new GameObject("Model");
                model.layer = UiLayer;
                model.transform.SetParent(b.transform, false);
                var f = model.AddComponent<MeshFilter>();
                var r = model.AddComponent<MeshRenderer>();
                r.sharedMaterial = litMaterial; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                l.Tiles.Add((b, model.transform, f, -1));
            }
            float infoTop = gridTop - 2f * TileH - TileGap - 0.004f, infoBottom = -top + 0.006f;
            l.Info = Text("About", l.Root, "", new Vector3(0f, (infoTop + infoBottom) / 2, -0.002f), new Vector2(LibraryWidth - 0.016f, infoTop - infoBottom), 0.054f, TextAlignmentOptions.TopLeft);
            l.Info.enableAutoSizing = false; l.Info.fontSize = 0.054f; l.Info.lineSpacing = 4f;
            l.Info.textWrappingMode = TextWrappingModes.Normal; l.Info.overflowMode = TextOverflowModes.Truncate;
            l.Info.color = new Color(0.95f, 0.93f, 0.9f);
            return l;
        }

        private void ShowLibraryPage()
        {
            if (library == null) return;
            int pages = LibraryPages;
            libraryPage = Mathf.Clamp(libraryPage, 0, pages - 1);
            library.Prev.gameObject.SetActive(pages > 1); library.Next.gameObject.SetActive(pages > 1);
            library.Page.text = pages > 1 ? $"{libraryPage + 1}/{pages}" : "";
            for (int i = 0; i < library.Tiles.Count; i++)
            {
                var (b, model, filter, _) = library.Tiles[i];
                int k = libraryPage * TilesPerPage + i;
                bool on = k < libraryTiles.Count;
                b.gameObject.SetActive(on);
                if (!on) { library.Tiles[i] = (b, model, filter, -1); continue; }
                var tile = libraryTiles[k];
                b.Label.text = tile.Label;
                b.Selected = tile.Selected; b.Enabled = true; b.OnClick = tile.OnClick;
                b.ApplyVisuals();
                if (library.Tiles[i].id != tile.Id || filter.sharedMesh != tile.Model)
                {
                    filter.sharedMesh = tile.Model;
                    // The model fits the tile above its name.
                    var bounds = tile.Model != null ? tile.Model.bounds : new Bounds(Vector3.zero, Vector3.one * 0.01f);
                    float size = Mathf.Max(bounds.size.x, bounds.size.z, bounds.size.y * 1.1f);
                    model.localScale = Vector3.one * (0.046f / Mathf.Max(0.005f, size));
                    model.localRotation = TileTilt(30f);
                    model.localPosition = new Vector3(0f, 0.006f, -0.014f) - model.localRotation * bounds.center * model.localScale.x;
                }
                library.Tiles[i] = (b, model, filter, tile.Id);
            }
            libraryShownId = -2;
        }

        /// <summary>A model on a tile seen from a little above, turned by <paramref name="yaw"/>.</summary>
        private static Quaternion TileTilt(float yaw) => Quaternion.Euler(-28f, 0f, 0f) * Quaternion.Euler(0f, yaw, 0f);

        /// <summary>Turns the model pointed at (and the chosen one), and says what the object pointed at is.</summary>
        private void UpdateLibrary()
        {
            if (library == null || !libraryWanted) return;
            bool show = !CardShown;
            if (library.Root.gameObject.activeSelf != show) library.Root.gameObject.SetActive(show);
            if (!show) return;
            LibraryHovered = -1;
            int selected = -1;
            foreach (var (b, model, filter, id) in library.Tiles)
            {
                if (id < 0 || !b.gameObject.activeSelf) continue;
                if (b.Hovered) LibraryHovered = id;
                if (b.Selected) selected = id;
                if (b.Hovered || b.Selected)
                {
                    var bounds = filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds();
                    model.localRotation = TileTilt(30f + Time.time * 45f);
                    model.localPosition = new Vector3(0f, 0.006f, -0.014f) - model.localRotation * bounds.center * model.localScale.x;
                }
            }
            int about = LibraryHovered >= 0 ? LibraryHovered : selected;
            if (about != libraryShownId && describe != null)
            {
                libraryShownId = about;
                library.Info.text = about >= 0 ? $"<b>{Catalog.Get(about).Name}</b>\n{describe(about)}" : "";
            }
        }
    }
}
