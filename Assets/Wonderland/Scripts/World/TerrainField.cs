using System;
using UnityEngine;

namespace Wonderland
{
    public enum TerrainBrush { Raise, Lower, Smooth, Flatten }

    /// <summary>
    /// The sculptable landscape as a height grid in the world root's local space (metres; y = 0 is the real floor).
    /// <para>
    /// <see cref="Height"/> is what brushes edit. The rendered <see cref="Surface"/> is Height shaped by an organic rim
    /// (<see cref="Edge"/>) that tapers to the floor, so the landscape grows out of the room instead of standing on a
    /// pedestal. <see cref="Lock"/> marks the railway corridor: the track owns that ground (it was graded to the track's
    /// smooth profile) and brushes leave it alone until the track is removed.
    /// </para>
    /// </summary>
    public sealed class TerrainField
    {
        public const int N = 97;
        public const float BaseHeight = 0.022f;
        public const float MinHeight = 0.004f;
        public const float MaxHeight = 0.3f;
        public const float RaiseRate = 0.09f;   // metres per second at full strength, brush centre

        public readonly float Size, Half, Cell;
        public readonly float[] Height = new float[N * N];
        public readonly float[] Surface = new float[N * N];
        public readonly float[] Edge = new float[N * N];
        public readonly float[] Lock = new float[N * N];
        public readonly float[] Soil = new float[N * N];
        private readonly float[] scratch = new float[N * N];
        private readonly float[] rim = new float[N * N];   // Shape(Edge), cached

        public TerrainField(float size, int seed, bool hills = true)
        {
            Size = size; Half = size * 0.5f; Cell = size / (N - 1);
            var rng = new Rng(seed);
            // Organic, slightly wavy rounded-square rim.
            float p1 = rng.Range(0f, 6.28f), p2 = rng.Range(0f, 6.28f), p3 = rng.Range(0f, 6.28f);
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                float u = (x / (float)(N - 1)) * 2f - 1f, v = (z / (float)(N - 1)) * 2f - 1f;
                float d = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 3.2f) + Mathf.Pow(Mathf.Abs(v), 3.2f), 1f / 3.2f);
                float a = Mathf.Atan2(v, u);
                float rimRadius = 0.95f + 0.025f * Mathf.Sin(3f * a + p1) + 0.018f * Mathf.Sin(5f * a + p2) + 0.01f * Mathf.Sin(11f * a + p3);
                Edge[z * N + x] = Mathf.Clamp01((rimRadius - d) / 0.3f);
                this.rim[z * N + x] = Shape(Edge[z * N + x]);
            }
            // A gentle starting landscape: low rolling swells, never a flat sheet.
            for (int i = 0; i < Height.Length; i++) Height[i] = BaseHeight;
            if (hills)
            {
                for (int k = 0; k < 4; k++)
                {
                    var c = new Vector2(rng.Range(-0.45f, 0.45f), rng.Range(-0.45f, 0.45f)) * Half;
                    float r = rng.Range(0.18f, 0.3f) * Half * 1.6f, h = rng.Range(0.012f, 0.03f);
                    for (int z = 0; z < N; z++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = LocalX(x) - c.x, dz = LocalZ(z) - c.y;
                        Height[z * N + x] += h * Mathf.Exp(-(dx * dx + dz * dz) / (r * r));
                    }
                }
                for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                    Height[z * N + x] += (Noise.Fbm2(LocalX(x) * 4f + seed, LocalZ(z) * 4f) - 0.5f) * 0.008f;
            }
            UpdateSurface(new RectInt(0, 0, N, N));
        }

        public float LocalX(int x) => -Half + x * Cell;
        public float LocalZ(int z) => -Half + z * Cell;

        /// <summary>Rim shaping: 0 outside, 1 well inside.</summary>
        public static float Shape(float edge) => edge * edge * (3f - 2f * edge);

        public void UpdateSurface(RectInt r)
        {
            for (int z = r.yMin; z < r.yMax; z++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                int i = z * N + x;
                Surface[i] = Height[i] * Shape(Edge[i]);
            }
        }

        public bool Inside(float x, float z) => x > -Half && x < Half && z > -Half && z < Half;

        private float Bilinear(float[] grid, float x, float z)
        {
            float gx = Mathf.Clamp((x + Half) / Cell, 0f, N - 1.001f), gz = Mathf.Clamp((z + Half) / Cell, 0f, N - 1.001f);
            int ix = (int)gx, iz = (int)gz;
            float fx = gx - ix, fz = gz - iz;
            int i = iz * N + ix;
            float a = Mathf.Lerp(grid[i], grid[i + 1], fx), b = Mathf.Lerp(grid[i + N], grid[i + N + 1], fx);
            return Mathf.Lerp(a, b, fz);
        }

        public float SurfaceAt(float x, float z) => Bilinear(Surface, x, z);
        public float EdgeAt(float x, float z) => Inside(x, z) ? Bilinear(Edge, x, z) : 0f;
        public float LockAt(float x, float z) => Inside(x, z) ? Bilinear(Lock, x, z) : 0f;
        public Vector3 PointAt(float x, float z) => new(x, SurfaceAt(x, z), z);

        public Vector3 NormalAt(float x, float z)
        {
            float e = Cell;
            float dx = SurfaceAt(x - e, z) - SurfaceAt(x + e, z), dz = SurfaceAt(x, z - e) - SurfaceAt(x, z + e);
            return new Vector3(dx, 2f * e, dz).normalized;
        }

        /// <summary>Ray against the landscape (local space). Only the visible part counts (rim edge above ~0.05).</summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 hit)
        {
            hit = default;
            direction.Normalize();
            // Clip the ray to the terrain's bounding box.
            var bounds = new Bounds(new Vector3(0f, MaxHeight * 0.5f, 0f), new Vector3(Size, MaxHeight + 0.02f, Size));
            if (!bounds.IntersectRay(new Ray(origin, direction), out float t0)) return false;
            t0 = Mathf.Max(0f, t0);
            float step = Cell * 0.5f;
            float prevT = t0;
            bool prevAbove = true;
            for (float t = t0; t < maxDistance; t += step)
            {
                var p = origin + direction * t;
                if (p.y < -0.01f || !Inside(p.x, p.z)) { prevT = t; prevAbove = true; continue; }
                bool above = p.y > SurfaceAt(p.x, p.z);
                if (!above && prevAbove)
                {
                    float lo = prevT, hi = t;
                    for (int k = 0; k < 8; k++)
                    {
                        float mid = (lo + hi) * 0.5f; var m = origin + direction * mid;
                        if (m.y > SurfaceAt(m.x, m.z)) lo = mid; else hi = mid;
                    }
                    hit = origin + direction * hi;
                    if (EdgeAt(hit.x, hit.z) < 0.04f) return false;
                    hit.y = SurfaceAt(hit.x, hit.z);
                    return true;
                }
                prevAbove = above; prevT = t;
            }
            return false;
        }

        public RectInt RegionAround(Vector2 centre, float radius)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((centre.x - radius + Half) / Cell) - 1);
            int x1 = Mathf.Min(N, Mathf.CeilToInt((centre.x + radius + Half) / Cell) + 2);
            int z0 = Mathf.Max(0, Mathf.FloorToInt((centre.y - radius + Half) / Cell) - 1);
            int z1 = Mathf.Min(N, Mathf.CeilToInt((centre.y + radius + Half) / Cell) + 2);
            return new RectInt(x0, z0, Mathf.Max(0, x1 - x0), Mathf.Max(0, z1 - z0));
        }

        /// <summary>
        /// One frame of a brush stroke. Falloff is a smooth bell, raising and lowering also relax the touched ground a
        /// little so hills stay rounded and lush instead of spiky. Returns the touched grid region.
        /// </summary>
        public RectInt ApplyBrush(TerrainBrush brush, Vector2 centre, float radius, float strength, float dt, float flattenTarget)
        {
            var r = RegionAround(centre, radius);
            float r2 = radius * radius;
            if (brush == TerrainBrush.Smooth || brush == TerrainBrush.Raise || brush == TerrainBrush.Lower)
                System.Array.Copy(Height, scratch, Height.Length);
            for (int z = r.yMin; z < r.yMax; z++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                float dx = LocalX(x) - centre.x, dz = LocalZ(z) - centre.y;
                float d2 = (dx * dx + dz * dz) / r2;
                if (d2 >= 1f) continue;
                int i = z * N + x;
                // Fade towards the rim (where the landscape tapers to the floor) so hills never pile up into a cliff there.
                float w = (1f - d2) * (1f - d2) * (1f - Lock[i]) * Mathf.Sqrt(Shape(Edge[i]));
                if (w <= 0f) continue;
                float h = Height[i];
                float relax = Average(scratch, x, z) - h;
                switch (brush)
                {
                    case TerrainBrush.Raise:
                        // Ease off near the ceiling so tops round over.
                        h += RaiseRate * strength * dt * w * (1f - Mathf.Pow(h / MaxHeight, 3f));
                        h += relax * Mathf.Min(1f, 4f * dt) * w * 0.85f;
                        break;
                    case TerrainBrush.Lower:
                        h -= RaiseRate * strength * dt * w;
                        h += relax * Mathf.Min(1f, 4f * dt) * w * 0.6f;
                        break;
                    case TerrainBrush.Smooth:
                        h += relax * Mathf.Min(1f, 10f * strength * dt) * w;
                        break;
                    case TerrainBrush.Flatten:
                        h = Mathf.Lerp(h, flattenTarget, Mathf.Min(1f, 6f * strength * dt) * w);
                        break;
                }
                Height[i] = Mathf.Clamp(h, MinHeight, MaxHeight);
            }
            if (brush == TerrainBrush.Raise || brush == TerrainBrush.Lower) Slump(r, 2);
            UpdateSurface(r);
            return r;
        }

        /// <summary>
        /// Talus relaxation: where ground is steeper than <see cref="MaxSlope"/> material slides to the lower neighbour, so
        /// raising keeps widening a hill into a soft, rolling mound instead of growing a spike.
        /// </summary>
        private void Slump(RectInt r, int iterations)
        {
            float talus = MaxSlope * Cell;
            for (int it = 0; it < iterations; it++)
            {
                System.Array.Copy(Height, scratch, Height.Length);
                for (int z = Mathf.Max(1, r.yMin); z < Mathf.Min(N - 1, r.yMax); z++)
                for (int x = Mathf.Max(1, r.xMin); x < Mathf.Min(N - 1, r.xMax); x++)
                {
                    int i = z * N + x;
                    if (Lock[i] > 0.5f) continue;
                    // Compare rendered surfaces (height x rim taper) so steepness made by the rim counts too.
                    float si = Math.Max(rim[i], 0.15f), h = scratch[i] * rim[i];
                    for (int k = 0; k < 4; k++)
                    {
                        int j = k == 0 ? i - 1 : k == 1 ? i + 1 : k == 2 ? i - N : i + N;
                        float excess = h - scratch[j] * rim[j] - talus;
                        if (excess <= 0f || Lock[j] > 0.5f) continue;
                        float move = excess * 0.2f / si;
                        Height[i] -= move; Height[j] += move * 0.6f;
                    }
                }
            }
        }

        public const float MaxSlope = 0.85f;

        private static float Average(float[] g, int x, int z)
        {
            float sum = 0f, wsum = 0f;
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int xx = x + ox, zz = z + oz;
                if (xx < 0 || zz < 0 || xx >= N || zz >= N) continue;
                float w = (ox == 0 && oz == 0) ? 4f : (ox == 0 || oz == 0) ? 2f : 1f;
                sum += g[zz * N + xx] * w; wsum += w;
            }
            return sum / wsum;
        }

        /// <summary>Raw height that renders as <paramref name="surface"/> at grid index <paramref name="i"/>.</summary>
        public float HeightForSurface(int i, float surface)
        {
            float s = Shape(Edge[i]);
            return s > 0.05f ? Mathf.Clamp(surface / s, MinHeight, MaxHeight) : Height[i];
        }

        public void CopyFrom(float[] height, float[] locks, float[] soil)
        {
            System.Array.Copy(height, Height, Height.Length);
            System.Array.Copy(locks, Lock, Lock.Length);
            System.Array.Copy(soil, Soil, Soil.Length);
            UpdateSurface(new RectInt(0, 0, N, N));
        }
    }
}
