using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// The map above the palette: the whole world built so far, from above and simply drawn - land, water, roads, the
    /// railway and the buildings - with the trains and cars moving on it as dots, the window as a frame and home marked.
    /// So what goes on out of sight can be followed, and pointing at it and pulling the trigger slides the window there.
    /// </summary>
    public sealed class MiniMap : MonoBehaviour
    {
        public const int Pixels = 160;
        public const float Size = 0.12f;   // metres across, in the hand

        private WonderlandWorld world;
        private Texture2D texture;
        private Color32[] backdrop, pixels;
        private Rect frame;   // the part of the world shown (local XZ, square)
        private float backdropTimer, drawTimer;
        private int contentKey = int.MinValue;
        private BoxCollider box;

        private static readonly Color32 Land = new(122, 168, 88, 235), Water = new(84, 146, 196, 235), Road = new(70, 70, 76, 255),
            Rail = new(120, 82, 56, 255), House = new(196, 82, 62, 255), Train = new(255, 214, 70, 255), Car = new(250, 250, 250, 255),
            Frame = new(255, 255, 255, 255), Home = new(255, 120, 200, 255), Edge = new(40, 52, 40, 255);

        /// <summary>The world area the map shows (local XZ).</summary>
        public Rect Shows => frame;
        public Texture2D Texture => texture;

        /// <summary>The colour the map shows at a world point (tests).</summary>
        public Color32 ColourAt(Vector2 local) { var p = ToPixel(local); return pixels[Mathf.Clamp(p.y, 0, Pixels - 1) * Pixels + Mathf.Clamp(p.x, 0, Pixels - 1)]; }

        public void Init(WonderlandWorld world, Material screen)
        {
            this.world = world;
            texture = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Mini map" };
            backdrop = new Color32[Pixels * Pixels];
            pixels = new Color32[Pixels * Pixels];
            var mesh = new Mesh { name = "Mini map" };
            float h = Size * 0.5f;
            mesh.vertices = new[] { new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f), new Vector3(h, h, 0f), new Vector3(-h, h, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(screen) { name = "Mini map", mainTexture = texture };
            r.sharedMaterial.SetFloat("_Glow", 1f);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(Size, Size, 0.002f);
            box.isTrigger = true;
            Refresh(true);
        }

        private void LateUpdate()
        {
            if (world == null || world.Terrain == null) return;
            backdropTimer -= Time.unscaledDeltaTime; drawTimer -= Time.unscaledDeltaTime;
            if (drawTimer > 0f) return;
            drawTimer = 0.2f;
            Refresh(false);
        }

        /// <summary>Redraws the map: the moving things always, the land, roads, railway and buildings when they changed.</summary>
        public void Refresh(bool all)
        {
            int key = ContentKey();
            if (all || key != contentKey || backdropTimer <= 0f) { contentKey = key; backdropTimer = 3f; DrawBackdrop(); }
            System.Array.Copy(backdrop, pixels, pixels.Length);
            foreach (var t in world.Trains.Trains) Dot(world.Trains.LocoPosition(t), 2, Train);
            foreach (var v in world.Traffic.Vehicles) Dot(new Vector3(v.Position.x, 0f, v.Position.y), 1, Car);
            Dot(Vector3.zero, 1, Home);
            if (world.Windowed)
            {
                var r = world.WindowRect;
                Line(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), Frame); Line(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), Frame);
                Line(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), Frame); Line(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), Frame);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private int ContentKey()
        {
            int k = world.Buildings.Houses.Count * 7919 + world.Track.Network.Version * 31 + world.Paths.Lines.Count * 131;
            foreach (var l in world.Paths.Lines) k = k * 17 + l.points.Count;
            var w = world.WindowTarget;
            return k * 13 + Mathf.RoundToInt(w.x * 10f) * 101 + Mathf.RoundToInt(w.y * 10f);
        }

        /// <summary>The still things, fitted so everything built and the window are on it.</summary>
        private void DrawBackdrop()
        {
            float x0 = -1f, z0 = -1f, x1 = 1f, z1 = 1f;
            void Add(float x, float z) { x0 = Mathf.Min(x0, x); z0 = Mathf.Min(z0, z); x1 = Mathf.Max(x1, x); z1 = Mathf.Max(z1, z); }
            var win = world.WindowRect;
            Add(win.xMin, win.yMin); Add(win.xMax, win.yMax);
            foreach (var h in world.Buildings.Houses) Add(h.x, h.z);
            foreach (var l in world.Paths.Lines) for (int i = 0; i < l.points.Count; i += 10) Add(l.points[i].x, l.points[i].y);
            foreach (var e in world.Track.Network.Edges) for (int i = 0; i < e.Layout.Points.Count; i += 10) Add(e.Layout.Points[i].x, e.Layout.Points[i].z);
            float size = Mathf.Max(9f, Mathf.Max(x1 - x0, z1 - z0) + 2f);
            var c = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
            frame = new Rect(c - Vector2.one * size * 0.5f, Vector2.one * size);
            var t = world.Terrain;
            float px = frame.width / Pixels;
            for (int y = 0; y < Pixels; y++)
            for (int x = 0; x < Pixels; x++)
            {
                float wx = frame.xMin + (x + 0.5f) * px, wz = frame.yMin + (y + 0.5f) * px;
                bool wet = t.WaterAt(wx, wz) > 0.5f, land = t.LandAt(wx, wz);
                backdrop[y * Pixels + x] = wet ? Water : land ? Land : new Color32(60, 60, 60, 200);
            }
            foreach (var l in world.Paths.Lines)
            {
                if (!PathSystem.IsWay(l.kind)) continue;
                for (int i = 4; i < l.points.Count; i += 4) LineInto(backdrop, l.points[i - 4], l.points[i], Road);
                if (l.closed && l.points.Count > 4) LineInto(backdrop, l.points[^1], l.points[0], Road);
            }
            foreach (var e in world.Track.Network.Edges)
            {
                var pts = e.Layout.Points;
                for (int i = 4; i < pts.Count; i += 4) LineInto(backdrop, new Vector2(pts[i - 4].x, pts[i - 4].z), new Vector2(pts[i].x, pts[i].z), Rail);
                if (e.Closed) LineInto(backdrop, new Vector2(pts[^1].x, pts[^1].z), new Vector2(pts[0].x, pts[0].z), Rail);
            }
            foreach (var h in world.Buildings.Houses)
            {
                var p = ToPixel(new Vector2(h.x, h.z));
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) Set(backdrop, p.x + dx, p.y + dy, House);
            }
            for (int i = 0; i < Pixels; i++) { Set(backdrop, i, 0, Edge); Set(backdrop, i, Pixels - 1, Edge); Set(backdrop, 0, i, Edge); Set(backdrop, Pixels - 1, i, Edge); }
        }

        private Vector2Int ToPixel(Vector2 p) => new(Mathf.FloorToInt((p.x - frame.xMin) / frame.width * Pixels), Mathf.FloorToInt((p.y - frame.yMin) / frame.height * Pixels));

        private static void Set(Color32[] into, int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < Pixels && y < Pixels) into[y * Pixels + x] = c; }

        private void Dot(Vector3 local, int r, Color32 c)
        {
            var p = ToPixel(new Vector2(local.x, local.z));
            for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) Set(pixels, p.x + dx, p.y + dy, c);
        }

        private void Line(Vector2 a, Vector2 b, Color32 c) => LineInto(pixels, a, b, c);

        private void LineInto(Color32[] into, Vector2 a, Vector2 b, Color32 c)
        {
            var pa = ToPixel(a); var pb = ToPixel(b);
            int steps = Mathf.Max(Mathf.Abs(pb.x - pa.x), Mathf.Abs(pb.y - pa.y), 1);
            for (int s = 0; s <= steps; s++) Set(into, pa.x + (pb.x - pa.x) * s / steps, pa.y + (pb.y - pa.y) * s / steps, c);
        }

        /// <summary>Where on the world a ray (world space) points at the map, if it does.</summary>
        public bool Pick(Ray ray, out Vector2 local, out float distance)
        {
            local = default; distance = 0f;
            if (box == null || !gameObject.activeInHierarchy || !box.Raycast(ray, out var hit, 2f)) return false;
            distance = hit.distance;
            var q = transform.InverseTransformPoint(hit.point);
            float u = q.x / Size + 0.5f, v = q.y / Size + 0.5f;
            local = new Vector2(frame.xMin + u * frame.width, frame.yMin + v * frame.height);
            return true;
        }
    }
}
