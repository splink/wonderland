using System;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The world window: the world is far larger than the floor it lies on, and the part shown is a rectangle of up to
    /// 2 x 2 m (3 x 3 m in Settings; less in a smaller room) cut off cleanly at its edges. Two things move it, and they
    /// never mix:
    /// <list type="bullet">
    /// <item>Placement (Move, Lift) puts the window somewhere else in the real room; the same land stays in it.</item>
    /// <item>Navigation moves the window across the world: scrolled smoothly (the right stick), or stepped a tenth of its
    /// size at a time (the arrows round it): the land under it slides along, and new land comes into view.</item>
    /// </list>
    /// The world root's own transform is the world's frame (local coordinates are world coordinates, so nothing built
    /// ever moves in them); the placement is the point of it at the window's centre, which stays put in the room.
    /// </summary>
    public sealed partial class WonderlandWorld
    {
        public const float SlideTime = 0.45f;
        /// <summary>How far an arrow steps the window: a tenth of its size.</summary>
        public const float StepFraction = 0.1f;
        /// <summary>How fast the stick scrolls at full tilt: this much of the window's size a second.</summary>
        public const float PanSpeed = 0.4f;
        private const string WindowKey = "wonderland.window";
        /// <summary>The window sizes Settings offers (metres a side).</summary>
        public static readonly float[] WindowSizes = { 2f, 3f, 1.5f };

        /// <summary>The largest the window may be (metres a side, a room permitting): 2 m unless chosen otherwise in Settings.</summary>
        public static float MaxWindow
        {
            get => PlayerPrefs.GetFloat(WindowKey, 2f);
            set { PlayerPrefs.SetFloat(WindowKey, value); PlayerPrefs.Save(); }
        }

        /// <summary>The arrows round the window.</summary>
        public WindowNavigator Navigator { get; private set; }
        /// <summary>How far the world is lifted off the real floor (metres; Move sets it, the room anchor keeps it).</summary>
        public float Lift;

        /// <summary>Whether the window is in use (the app); without it the whole world is drawn, as in the tests' scenes.</summary>
        public bool Windowed { get; private set; }
        /// <summary>Where the window's centre is in the world (local XZ, metres).</summary>
        public Vector2 WindowCenter { get; private set; }
        /// <summary>The window's size (metres): up to <see cref="MaxWindow"/> each way, smaller where the room is.</summary>
        public Vector2 WindowSize { get; private set; } = new(2f, 2f);
        public Rect WindowRect => new(WindowCenter - WindowSize * 0.5f, WindowSize);
        /// <summary>Where navigation is heading (the centre once the slide is over).</summary>
        public Vector2 WindowTarget { get; private set; }
        public bool Sliding => slideT < 1f;
        /// <summary>The window moved (or changed size): the navigation arrows, the map and the banner follow.</summary>
        public event Action WindowMoved;

        private Vector2 slideFrom;
        private float slideT = 1f;
        private bool windowDirty = true;

        private static readonly int WorldToWindowId = Shader.PropertyToID("_WL_WorldToWindow");
        private static readonly int WindowId = Shader.PropertyToID("_WL_Window");
        private static readonly int WindowOnId = Shader.PropertyToID("_WL_WindowOn");

        /// <summary>Starts (or stops) showing just the window, centred where it was last.</summary>
        public void UseWindow(bool on)
        {
            Windowed = on;
            if (!on) { Shader.SetGlobalFloat(WindowOnId, 0f); Terrain.Window = null; TerrainView.SetCut(null); CullToWindow(); }
            windowDirty = true;
            ApplyWindow();
        }

        /// <summary>Whether a local point is inside the window (always, without one).</summary>
        public bool InWindow(Vector3 local, float margin = 0f) => InWindow(new Vector2(local.x, local.z), margin);

        public bool InWindow(Vector2 p, float margin = 0f)
        {
            if (!Windowed) return true;
            var r = WindowRect;
            return p.x >= r.xMin - margin && p.x <= r.xMax + margin && p.y >= r.yMin - margin && p.y <= r.yMax + margin;
        }

        // ------------------------------------------------------------------ placement in the room

        /// <summary>Where the window's centre is in the room (on the world's floor) and which way the world faces.</summary>
        public Vector3 PlacementPosition => transform.TransformPoint(new Vector3(WindowCenter.x, 0f, WindowCenter.y));
        public Quaternion PlacementRotation => transform.rotation;

        /// <summary>Puts the window's centre at <paramref name="position"/> in the room, facing <paramref name="rotation"/>.</summary>
        public void Place(Vector3 position, Quaternion rotation)
        {
            transform.rotation = rotation;
            transform.position = position - rotation * Vector3.Scale(transform.localScale, new Vector3(WindowCenter.x, 0f, WindowCenter.y));
            windowDirty = true;
        }

        // ------------------------------------------------------------------ navigation

        /// <summary>
        /// Slides the window a step in a direction (x east, y north; diagonals too): <paramref name="fraction"/> of its size
        /// (a tenth, unless asked otherwise). False (and nothing moves) at the edge of the world there is room for.
        /// </summary>
        public bool Navigate(Vector2Int direction, float fraction = StepFraction)
        {
            var step = new Vector2(Mathf.Max(BuildGrid.Size, Snap(WindowSize.x * fraction)), Mathf.Max(BuildGrid.Size, Snap(WindowSize.y * fraction)));
            return NavigateTo(WindowTarget + new Vector2(direction.x * step.x, direction.y * step.y));
        }

        /// <summary>
        /// Scrolls the window across the world (the right stick): <paramref name="velocity"/> metres a second, in the
        /// world's own frame. The land is there before it shows: the grid moves on ahead only when the window's leading
        /// edge comes near its end (not every frame), carrying half a second's travel with it.
        /// </summary>
        public void Pan(Vector2 velocity, float dt)
        {
            if (!Windowed || dt <= 0f || velocity.sqrMagnitude < 1e-10f) return;
            if (Sliding) { slideT = 1f; WindowTarget = WindowCenter; }
            float reach = Terrain.Store != null ? 5000f : TerrainField.MaxReach - 0.5f;
            var next = WindowCenter + velocity * dt;
            next = new Vector2(Mathf.Clamp(next.x, -reach + WindowSize.x * 0.5f, reach - WindowSize.x * 0.5f), Mathf.Clamp(next.y, -reach + WindowSize.y * 0.5f, reach - WindowSize.y * 0.5f));
            var shown = new Rect(next - WindowSize * 0.5f, WindowSize);
            var t = Terrain;
            const float margin = 0.15f;
            bool covered = shown.xMin - margin >= t.X0 && shown.xMax + margin <= t.XMax && shown.yMin - margin >= t.Z0 && shown.yMax + margin <= t.ZMax;
            if (!covered)
            {
                var ahead = velocity * 0.5f;
                EnsureGridCovers(Rect.MinMaxRect(shown.xMin + Mathf.Min(0f, ahead.x), shown.yMin + Mathf.Min(0f, ahead.y), shown.xMax + Mathf.Max(0f, ahead.x), shown.yMax + Mathf.Max(0f, ahead.y)));
            }
            var placed = PlacementPosition;
            WindowCenter = WindowTarget = next;
            Place(placed, PlacementRotation);
            pannedFrame = Time.frameCount;
        }

        private int pannedFrame = -10;
        /// <summary>The window is moving across the world (sliding, or being scrolled).</summary>
        public bool Moving => Sliding || Time.frameCount - pannedFrame <= 1;

        /// <summary>Back to where the world started.</summary>
        public bool NavigateHome() => NavigateTo(Vector2.zero);

        /// <summary>Where the window is, in words: how far east or west and north or south of where the world started.</summary>
        public string WhereLabel()
        {
            var c = WindowTarget;
            if (c.magnitude < 0.01f) return "at home, where the world started";
            string Part(float v, string plus, string minus) => Mathf.Abs(v) < 0.01f ? null : $"{Mathf.Abs(v):0.0} m {(v > 0f ? plus : minus)}";
            var parts = new System.Collections.Generic.List<string>();
            if (Part(c.x, "east", "west") is string a) parts.Add(a);
            if (Part(c.y, "north", "south") is string b) parts.Add(b);
            return string.Join(", ", parts) + " of home";
        }

        /// <summary>Slides the window to be centred at <paramref name="center"/> (local XZ).</summary>
        public bool NavigateTo(Vector2 center)
        {
            center = new Vector2(Snap(center.x), Snap(center.y));
            float reach = Terrain.Store != null ? 5000f : TerrainField.MaxReach - 0.5f;   // (endless: far further than anyone walks)
            if (Mathf.Abs(center.x) + WindowSize.x * 0.5f > reach || Mathf.Abs(center.y) + WindowSize.y * 0.5f > reach) return false;
            if (center == WindowTarget && !Sliding) return true;
            // The land the window slides onto is there before it shows.
            var to = new Rect(center - WindowSize * 0.5f, WindowSize);
            EnsureGridCovers(Rect.MinMaxRect(Mathf.Min(to.xMin, WindowRect.xMin), Mathf.Min(to.yMin, WindowRect.yMin), Mathf.Max(to.xMax, WindowRect.xMax), Mathf.Max(to.yMax, WindowRect.yMax)));
            slideFrom = WindowCenter;
            WindowTarget = center;
            slideT = 0f;
            return true;
        }

        /// <summary>Jumps straight to a centre, without sliding (loading a world; tests).</summary>
        public void SetWindowCenter(Vector2 center)
        {
            var placed = PlacementPosition;
            WindowCenter = WindowTarget = center;
            slideT = 1f;
            EnsureGridCovers(WindowRect);
            Place(placed, PlacementRotation);
            ApplyWindow();
        }

        /// <summary>One frame of the window (the slide, the cut, what is shown) - what LateUpdate does (tests, probes).</summary>
        public void Advance(float dt) => UpdateWindow(dt);

        /// <summary>Works out at once what the window shows (tests: things placed or moved since the last frame).</summary>
        public void RefreshVisibility() => CullToWindow();

        /// <summary>Finishes a slide at once (tests).</summary>
        public void FinishSlide()
        {
            if (!Sliding) return;
            slideT = 0.9999f;
            StepSlide(1f);
            ApplyWindow();
        }

        private void OnDisable() { if (Windowed) { Shader.SetGlobalFloat(WindowOnId, 0f); FigureBatch.Window = null; } }
        private void OnEnable() => windowDirty = true;

        private static float Snap(float v) => Mathf.Round(v / BuildGrid.Size) * BuildGrid.Size;

        /// <summary>One frame of a slide: the land moves under the window, the window stays where it is in the room.</summary>
        private void StepSlide(float dt)
        {
            if (!Sliding) return;
            var placed = PlacementPosition;
            slideT = Mathf.Min(1f, slideT + dt / SlideTime);
            float s = Mathf.SmoothStep(0f, 1f, slideT);
            WindowCenter = Vector2.Lerp(slideFrom, WindowTarget, s);
            if (!Sliding) WindowCenter = WindowTarget;
            Place(placed, PlacementRotation);
        }

        // ------------------------------------------------------------------ size from the room

        /// <summary>
        /// The window's size in this room: <see cref="MaxWindow"/> square, or the largest rectangle up to that which fits
        /// the room's floor around the placement (each side shrunk on its own: a long narrow room gets a long narrow window).
        /// </summary>
        private void FitWindowToRoom()
        {
            float most = MaxWindow;
            var size = new Vector2(most, most);
            if (roomLocal != null)
            {
                var c = WindowCenter;
                bool Fits(Vector2 s)
                {
                    for (int i = 0; i <= 8; i++)
                    {
                        float u = i / 8f - 0.5f;
                        foreach (var p in new[] { new Vector2(u * s.x, -s.y * 0.5f), new Vector2(u * s.x, s.y * 0.5f), new Vector2(-s.x * 0.5f, u * s.y), new Vector2(s.x * 0.5f, u * s.y) })
                            if (!RoomFootprint.Inside(roomLocal.Floor, c + p)) return false;
                    }
                    return true;
                }
                // Shrink evenly until it fits, then let each side grow back as far as the room allows.
                float k = 1f;
                while (k > 0.2f && !Fits(size * k)) k -= 0.05f;
                size *= k;
                for (int axis = 0; axis < 2; axis++)
                    while (size[axis] < most)
                    {
                        var bigger = size; bigger[axis] = Mathf.Min(most, size[axis] + 0.05f);
                        if (!Fits(bigger)) break;
                        size = bigger;
                    }
            }
            size = new Vector2(Mathf.Max(0.6f, Mathf.Floor(size.x / BuildGrid.Size) * BuildGrid.Size), Mathf.Max(0.6f, Mathf.Floor(size.y / BuildGrid.Size) * BuildGrid.Size));
            if (size != WindowSize) { WindowSize = size; windowDirty = true; }
        }

        // ------------------------------------------------------------------ drawing just the window

        private Matrix4x4 lastWorldToLocal;

        /// <summary>Keeps the shaders' cut, the pointer's reach and the cut edge in step with the window.</summary>
        private void ApplyWindow()
        {
            if (!Windowed || Terrain == null) return;
            var toLocal = transform.worldToLocalMatrix;
            var r = WindowRect;
            Shader.SetGlobalMatrix(WorldToWindowId, toLocal);
            Shader.SetGlobalVector(WindowId, new Vector4(WindowCenter.x, WindowCenter.y, WindowSize.x * 0.5f, WindowSize.y * 0.5f));
            Shader.SetGlobalFloat(WindowOnId, 1f);
            if (!windowDirty && toLocal == lastWorldToLocal) return;
            lastWorldToLocal = toLocal;
            windowDirty = false;
            Terrain.Window = r;
            TerrainView.SetCut(r);
            windowMovedSinceCull = true;
            WindowMoved?.Invoke();
        }

        /// <summary>The cut edge follows the land: after a change at the window's edge.</summary>
        public void RefreshCut() { if (Windowed) TerrainView.SetCut(WindowRect); }

        private void UpdateWindow(float dt)
        {
            if (!Windowed) return;
            bool slid = Moving;
            StepSlide(dt);
            ApplyWindow();
            if (slid || windowMovedSinceCull || ++cullFrame % 3 == 0) CullToWindow();   // (things move in and out of the window slowly: every third frame is enough)
        }

        // ------------------------------------------------------------------ out of sight

        /// <summary>
        /// What the window doesn't show isn't drawn or animated: trees, grass, buildings, carriages, vehicles, boats and
        /// figures outside it are switched off (the land, roads and track are cut by the shaders). Everything still
        /// lives - trains run, people go about their day, cars drive - it just isn't shown. Ground just outside is built in
        /// advance, so what slides into view is there.
        /// </summary>
        private Func<Vector2, float, bool> inWindow, nearWindow, everywhere, keptNear;
        private int cullFrame;
        private bool windowMovedSinceCull = true;
        private Func<Vector3, bool> figureInWindow;

        private void CullToWindow()
        {
            inWindow ??= (p, r) => InWindow(p, r);
            nearWindow ??= (p, r) => InWindow(p, r + 0.6f);
            everywhere ??= (p, r) => true;
            keptNear ??= (p, r) => InWindow(p, r + 2f);
            figureInWindow ??= p => InWindow(transform.InverseTransformPoint(p), 0.03f);
            var shown = Windowed ? inWindow : everywhere;
            Forest.Shown = Cover.Shown = Windowed ? nearWindow : null;   // (built a little ahead)
            FigureBatch.Window = Windowed ? figureInWindow : null;
            windowMovedSinceCull = false;
            Forest.Cull(shown, Windowed ? keptNear : null); Cover.Cull(shown, Windowed ? keptNear : null); Buildings.Cull(shown); Trains.Cull(shown); Traffic.Cull(shown); Boats.Cull(shown);
            if (Moving) { Forest.RebuildDirty(6); Cover.RebuildDirty(6); }
        }
    }
}
