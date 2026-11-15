using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The free floor of the real room, in world space: the floor outline (from the room scan, or the guardian play area)
    /// and the footprints of furniture standing on it. Land may grow inside the outline, a little away from the walls,
    /// and never under furniture.
    /// </summary>
    public sealed class RoomFootprint
    {
        public const float WallMargin = 0.06f;
        public const float FurnitureMargin = 0.04f;
        public readonly List<Vector3> Floor = new();
        public readonly List<List<Vector3>> Obstacles = new();
        public string Source = "room scan";

        /// <summary>The footprint in a frame's local XZ plane (e.g. the world root), for per-cell tests.</summary>
        public sealed class Local
        {
            public Vector2[] Floor;
            public List<Vector2[]> Obstacles = new();
            public Rect Bounds;

            public bool IsFree(Vector2 p)
            {
                if (!Inside(Floor, p) || DistanceToEdges(Floor, p) < WallMargin) return false;
                foreach (var o in Obstacles)
                    if (Inside(o, p) || DistanceToEdges(o, p) < FurnitureMargin) return false;
                return true;
            }
        }

        public Local ToLocal(Transform frame)
        {
            var local = new Local { Floor = new Vector2[Floor.Count] };
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < Floor.Count; i++)
            {
                var p = frame.InverseTransformPoint(Floor[i]);
                local.Floor[i] = new Vector2(p.x, p.z);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            local.Bounds = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
            foreach (var o in Obstacles)
            {
                var pts = new List<Vector2>();
                foreach (var w in o) { var p = frame.InverseTransformPoint(w); pts.Add(new Vector2(p.x, p.z)); }
                local.Obstacles.Add(ConvexHull(pts));
            }
            return local;
        }

        public static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        public static float DistanceToEdges(Vector2[] poly, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[j]; var ab = poly[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
                best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
            }
            return best;
        }

        /// <summary>Monotone-chain convex hull (furniture boxes projected onto the floor).</summary>
        public static Vector2[] ConvexHull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (pts.Count < 3) return pts.ToArray();
            var hull = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < pts.Count; k++)
                {
                    var p = pass == 0 ? pts[k] : pts[pts.Count - 1 - k];
                    while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull.ToArray();
        }
    }
}
