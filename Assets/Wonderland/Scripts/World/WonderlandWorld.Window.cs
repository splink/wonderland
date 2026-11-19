using System;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The world window: the world is far larger than the floor it lies on, and the part shown is a rectangle of up to
    /// 3 x 3 m (less in a smaller room) cut off cleanly at its edges. Two things move it, and they never mix:
    /// <list type="bullet">
    /// <item>Placement (Move, Lift) puts the window somewhere else in the real room; the same land stays in it.</item>
    /// <item>Navigation slides the window across the world, half its width at a time: the land under it slides along, and
    /// new land comes into view on one side while half the old stays on the other.</item>
    /// </list>
    /// The world root's own transform is the world's frame (local coordinates are world coordinates, so nothing built
    /// ever moves in them); the placement is the point of it at the window's centre, which stays put in the room.
    /// </summary>
    public sealed partial class WonderlandWorld
    {
        public const float MaxWindow = 3f;
        public const float SlideTime = 0.45f;

        /// <summary>The arrows round the window.</summary>
        public WindowNavigator Navigator { get; private set; }
        /// <summary>How far the world is lifted off the real floor (metres; Move sets it, the room anchor keeps it).</summary>
        public float Lift;

        /// <summary>Whether the window is in use (the app); without it the whole world is drawn, as in the tests' scenes.</summary>
        public bool Windowed { get; private set; }
        /// <summary>Where the window's centre is in the world (local XZ, metres).</summary>
        public Vector2 WindowCenter { get; private set; }
        /// <summary>The window's size (metres): up to <see cref="MaxWindow"/> each way, smaller where the room is.</summary>
        public Vector2 WindowSize { get; private set; } = new(MaxWindow, MaxWindow);
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
            if (!on) { Shader.SetGlobalFloat(WindowOnId, 0f); Terrain.Window = null; TerrainView.SetCut(null); }
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
        /// Slides the window half its size in a direction (x east, y north; diagonals too). False (and nothing moves) at
        /// the edge of the world there is room for.
        /// </summary>
        public bool Navigate(Vector2Int direction)
        {
            var step = new Vector2(Snap(WindowSize.x * 0.5f), Snap(WindowSize.y * 0.5f));
            return NavigateTo(WindowTarget + new Vector2(direction.x * step.x, direction.y * step.y));
        }

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
            float reach = TerrainField.MaxReach - 0.5f;
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

        /// <summary>Finishes a slide at once (tests).</summary>
        public void FinishSlide()
        {
            if (!Sliding) return;
            slideT = 0.9999f;
            StepSlide(1f);
            ApplyWindow();
        }

        private void OnDisable() { if (Windowed) Shader.SetGlobalFloat(WindowOnId, 0f); }
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
        /// The window's size in this room: 3 x 3 m, or the largest rectangle up to that which fits the room's floor
        /// around the placement (each side shrunk on its own: a long narrow room gets a long narrow window).
        /// </summary>
        private void FitWindowToRoom()
        {
            var size = new Vector2(MaxWindow, MaxWindow);
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
                    while (size[axis] < MaxWindow)
                    {
                        var bigger = size; bigger[axis] = Mathf.Min(MaxWindow, size[axis] + 0.05f);
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
            WindowMoved?.Invoke();
        }

        /// <summary>The cut edge follows the land: after a change at the window's edge.</summary>
        public void RefreshCut() { if (Windowed) TerrainView.SetCut(WindowRect); }

        private void UpdateWindow(float dt)
        {
            if (!Windowed) return;
            StepSlide(dt);
            ApplyWindow();
        }
    }
}
