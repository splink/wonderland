using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>Flat polylines (x, z as x, y): lengths, even spacing, simplifying and segment tests, shared by track, roads and streams.</summary>
    public static class Polyline
    {
        public static float Length(List<Vector2> p)
        {
            float l = 0f;
            for (int i = 1; i < p.Count; i++) l += (p[i] - p[i - 1]).magnitude;
            return l;
        }

        public static float Perimeter(List<Vector2> p, bool closed) => Length(p) + (closed ? (p[^1] - p[0]).magnitude : 0f);

        /// <summary>Longer than any track or road in a room-sized world: a line this long is a geometry bug.</summary>
        public const float MaxLine = 200f;

        /// <summary>Points evenly every <paramref name="spacing"/> along the line (an open line keeps its exact ends).</summary>
        public static List<Vector2> Resample(List<Vector2> p, float spacing, bool closed)
        {
            var src = new List<Vector2>(p);
            if (closed) src.Add(p[0]);
            float total = Length(src);
            // A line kilometres long (or not a number) is a geometry bug, never something drawn on a living-room floor:
            // hand it back as it is rather than fill the headset's memory with points (the caller refuses it).
            if (!(total < MaxLine)) { Debug.LogWarning($"Resample: refusing a line {total} m long"); return src; }
            int count = Mathf.Max(closed ? 4 : 2, Mathf.RoundToInt(total / spacing));
            float step = total / count;
            var result = new List<Vector2>(count + 1) { src[0] };
            float acc = 0f; int seg = 0; float segLen = (src[1] - src[0]).magnitude;
            for (int k = 1; k < (closed ? count : count + 1); k++)
            {
                float target = k * step;
                while (acc + segLen < target && seg < src.Count - 2) { acc += segLen; seg++; segLen = (src[seg + 1] - src[seg]).magnitude; }
                float t = segLen > 1e-7f ? Mathf.Clamp01((target - acc) / segLen) : 0f;
                result.Add(Vector2.Lerp(src[seg], src[seg + 1], t));
            }
            if (!closed) result[^1] = p[^1];
            return result;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Whether segments a-b and c-d cross (touching doesn't count).</summary>
        public static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /// <summary>Douglas-Peucker: the fewest corners within <paramref name="tolerance"/> of the line (its ends kept).</summary>
        public static List<Vector2> Simplify(List<Vector2> p, float tolerance)
        {
            var keep = new bool[p.Count];
            keep[0] = keep[^1] = true;
            var stack = new Stack<(int a, int b)>();
            stack.Push((0, p.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                float worst = 0f; int at = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = DistanceToSegment(p[i], p[a], p[b]);
                    if (d > worst) { worst = d; at = i; }
                }
                if (at >= 0 && worst > tolerance) { keep[at] = true; stack.Push((a, at)); stack.Push((at, b)); }
            }
            var r = new List<Vector2>();
            for (int i = 0; i < p.Count; i++) if (keep[i]) r.Add(p[i]);
            return r;
        }

        /// <summary>Douglas-Peucker round a ring: split at its farthest point from the start and both halves simplified.</summary>
        public static List<Vector2> ClosedSimplify(List<Vector2> p, float tolerance)
        {
            int far = 0; float best = 0f;
            for (int i = 0; i < p.Count; i++) { float d = (p[i] - p[0]).sqrMagnitude; if (d > best) { best = d; far = i; } }
            var first = Simplify(p.GetRange(0, far + 1), tolerance);
            var second = p.GetRange(far, p.Count - far); second.Add(p[0]);
            second = Simplify(second, tolerance);
            first.RemoveAt(first.Count - 1);
            second.RemoveAt(second.Count - 1);
            first.AddRange(second);
            return first;
        }
    }
}
