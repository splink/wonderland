using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Picking small things with the pointer: how close a ray passes to an object's upright axis (a villager, from feet
    /// to head), with a tolerance that grows with distance - so a 3 cm villager is as easy to point at a metre away
    /// as up close, and a thing standing in front of a building is found before the building.
    /// </summary>
    public static class RayPick
    {
        /// <summary>Allowed miss at distance <paramref name="t"/> along the ray (about a degree, never less than 5 mm).</summary>
        public static float Tolerance(float t) => 0.005f + t * 0.016f;

        /// <summary>
        /// The closest approach of the ray (<paramref name="o"/>, unit <paramref name="d"/>) to segment a-b: the miss
        /// distance, and how far along the ray (<paramref name="t"/>).
        /// </summary>
        public static float Miss(Vector3 o, Vector3 d, Vector3 a, Vector3 b, out float t)
        {
            var u = b - a; var w = o - a;
            float uu = Vector3.Dot(u, u), ud = Vector3.Dot(u, d), uw = Vector3.Dot(u, w), dw = Vector3.Dot(d, w);
            float den = uu - ud * ud;
            float s = den > 1e-12f ? Mathf.Clamp01((uw - ud * dw) / den) : 0f;   // along the segment
            var p = a + u * s;
            t = Mathf.Max(0f, Vector3.Dot(p - o, d));
            return (o + d * t - p).magnitude;
        }

        /// <summary>A score for a candidate (lower is better): the miss as a share of the tolerance; above 1 is no hit.</summary>
        public static float Score(Vector3 o, Vector3 d, Vector3 a, Vector3 b, float maxT, out float t)
        {
            float miss = Miss(o, d, a, b, out t);
            return t > maxT ? float.MaxValue : miss / Tolerance(t);
        }
    }
}
