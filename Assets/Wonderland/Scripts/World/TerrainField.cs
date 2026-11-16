using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    public enum TerrainBrush { Raise, Lower, Smooth, Flatten, Grow, Trim, Peaks }

    /// <summary>
    /// The sculptable landscape as a height grid in the world root's local space (metres; y = 0 is the real floor).
    /// <para>
    /// The grid is a rectangle of 2 cm cells aligned to the root's origin, sized to cover the real room (it re-fits when
    /// the world moves; cells always line up, so re-fitting copies data exactly). Only <see cref="Land"/> is landscape:
    /// the world starts as an organic patch and the Grow / Trim brushes paint land onto or off the surrounding floor,
    /// wherever <see cref="Allowed"/> (free floor inside the room) permits. <see cref="Edge"/> is
    /// derived from the land mask (distance to the shore, roughened by noise) and shapes the rendered
    /// <see cref="Surface"/> = Height x rim taper, so the landscape always grows out of the room instead of standing on a
    /// pedestal. <see cref="Lock"/> marks the railway corridor: the track owns that ground (it was graded to the track's
    /// smooth profile) and brushes leave it alone until the track is removed.
    /// </para>
    /// </summary>
    public sealed class TerrainField
    {
        public const int DefaultHalfCells = 120;   // 4.8 m square when the room is unknown
        public const int MaxCells = 800;           // per axis: 16 m, a memory backstop
        public const float MaxReach = (MaxCells / 2) * CellSize;   // how far from the world's centre land may reach
        public const float CellSize = 0.02f;
        public const float BaseHeight = 0.022f;
        public const float MinHeight = 0.004f;
        public const float MaxHeight = 0.3f;
        public const float RaiseRate = 0.09f;   // metres per second at full strength, brush centre
        public const float RimWidth = 0.28f;    // shore-to-full-height distance
        public const float MaxSlope = 0.85f;
        private const int RimCells = 16;        // RimWidth / CellSize, rounded up

        public readonly float Cell = CellSize;
        public readonly float PatchSize;
        public readonly int Seed;
        /// <summary>Grid size in cells and the global cell index of cell (0, 0): local x = (GX0 + x) * Cell.</summary>
        public int Nx { get; private set; }
        public int Nz { get; private set; }
        public int GX0 { get; private set; }
        public int GZ0 { get; private set; }
        public float[] Height, Surface, Land, Edge, Lock, Soil;
        /// <summary>House pads (1 = under a house): brushes and trimming leave them alone. Derived from the houses.</summary>
        public float[] Pad;
        /// <summary>Ponds and lakes: water mask (1 = water) and the water surface height for each water cell.</summary>
        public float[] Water, WaterLevel;
        public const float PondDepth = 0.028f;
        /// <summary>1 where land may grow (free floor inside the room).</summary>
        public byte[] Allowed;
        private float[] scratch, rim, rimNoise, distance, blur;   // rim = Shape(Edge); rimNoise = shoreline roughness (m)

        /// <summary>Cells the Trim brush must not remove (e.g. under houses). Set by the world.</summary>
        public Func<float, float, bool> Protect;

        public TerrainField(float patchSize, int seed, bool hills = true, RectInt? cells = null)
        {
            Seed = seed;
            Allocate(cells ?? new RectInt(-DefaultHalfCells, -DefaultHalfCells, DefaultHalfCells * 2 + 1, DefaultHalfCells * 2 + 1));
            PatchSize = Mathf.Min(patchSize, Mathf.Min(Nx, Nz) * Cell - 0.4f);
            for (int z = 0; z < Nz; z++)
            for (int x = 0; x < Nx; x++)
            {
                int i = z * Nx + x;
                InitCell(i, x, z, hills);
                Land[i] = OrganicPatch(PatchSize, seed, LocalX(x), LocalZ(z));
            }
            if (hills)
            {
                // A gentle starting landscape: low rolling swells, never a flat sheet.
                var rng = new Rng(seed);
                float half = PatchSize * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    var c = new Vector2(rng.Range(-0.45f, 0.45f), rng.Range(-0.45f, 0.45f)) * half;
                    float r = rng.Range(0.18f, 0.3f) * half * 1.6f, h = rng.Range(0.012f, 0.03f);
                    var region = RegionAround(c, r * 3f);
                    for (int z = region.yMin; z < region.yMax; z++)
                    for (int x = region.xMin; x < region.xMax; x++)
                    {
                        float dx = LocalX(x) - c.x, dz = LocalZ(z) - c.y;
                        Height[z * Nx + x] += h * Mathf.Exp(-(dx * dx + dz * dz) / (r * r));
                    }
                }
            }
            RecomputeEdge(All);
            UpdateSurface(All);
        }

        public RectInt All => new(0, 0, Nx, Nz);
        /// <summary>The grid in global cell indices (x, z, width, height).</summary>
        public RectInt Cells => new(GX0, GZ0, Nx, Nz);
        public int Index(int x, int z) => z * Nx + x;

        private void Allocate(RectInt c)
        {
            GX0 = c.x; GZ0 = c.y; Nx = c.width; Nz = c.height;
            int n = Nx * Nz;
            Height = new float[n]; Surface = new float[n]; Land = new float[n]; Edge = new float[n]; Lock = new float[n]; Soil = new float[n]; Pad = new float[n];
            Water = new float[n]; WaterLevel = new float[n];
            Allowed = new byte[n]; scratch = new float[n]; rim = new float[n]; rimNoise = new float[n]; distance = new float[n]; blur = new float[n];
            for (int i = 0; i < n; i++) Allowed[i] = 1;
        }

        /// <summary>Fresh floor cell: base height with a little noise, no land. Noise uses global coordinates, so it is
        /// identical however the grid is cut.</summary>
        private void InitCell(int i, int x, int z, bool undulate = true)
        {
            float lx = LocalX(x), lz = LocalZ(z);
            rimNoise[i] = (Noise.Fbm2(lx * 3.5f + Seed * 0.71f, lz * 3.5f - Seed * 0.37f, 3) - 0.5f) * 0.12f;
            Height[i] = BaseHeight + (undulate ? (Noise.Fbm2(lx * 4f + Seed, lz * 4f) - 0.5f) * 0.008f : 0f);
            Land[i] = 0f; Lock[i] = 0f; Soil[i] = 0f; Water[i] = 0f; WaterLevel[i] = 0f;
        }

        /// <summary>
        /// Re-cuts the grid to <paramref name="cells"/> (global cell indices). Overlapping cells keep their data exactly;
        /// new cells start as bare floor.
        /// </summary>
        public void Resize(RectInt cells)
        {
            if (cells == Cells) return;
            int oldNx = Nx, oldGX = GX0, oldGZ = GZ0;
            var oldRect = Cells;
            float[] h = Height, l = Land, lk = Lock, so = Soil, wa = Water, wl = WaterLevel;
            Allocate(cells);
            for (int z = 0; z < Nz; z++)
            for (int x = 0; x < Nx; x++)
            {
                int i = z * Nx + x, gx = GX0 + x, gz = GZ0 + z;
                InitCell(i, x, z);
                if (gx < oldRect.xMin || gx >= oldRect.xMax || gz < oldRect.yMin || gz >= oldRect.yMax) continue;
                int j = (gz - oldGZ) * oldNx + (gx - oldGX);
                Height[i] = h[j]; Land[i] = l[j]; Lock[i] = lk[j]; Soil[i] = so[j]; Water[i] = wa[j]; WaterLevel[i] = wl[j];
            }
            RecomputeEdge(All);
            UpdateSurface(All);
        }

        /// <summary>Global cell rectangle covering the local XZ rectangle <paramref name="r"/>.</summary>
        public static RectInt CellsCovering(Rect r) =>
            new(Mathf.FloorToInt(r.xMin / CellSize), Mathf.FloorToInt(r.yMin / CellSize),
                Mathf.CeilToInt(r.xMax / CellSize) - Mathf.FloorToInt(r.xMin / CellSize) + 1,
                Mathf.CeilToInt(r.yMax / CellSize) - Mathf.FloorToInt(r.yMin / CellSize) + 1);

        /// <summary>Local XZ bounds of the land (cells with land), or an empty rect.</summary>
        public Rect LandBounds()
        {
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            for (int z = 0; z < Nz; z++)
            for (int x = 0; x < Nx; x++)
            {
                if (Land[z * Nx + x] < 0.5f) continue;
                x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z);
            }
            return x0 > x1 ? Rect.zero : Rect.MinMaxRect(LocalX(x0), LocalZ(z0), LocalX(x1), LocalZ(z1));
        }

        /// <summary>Marks where land may grow; null allows the whole grid.</summary>
        public void SetAllowed(System.Func<Vector2, bool> free)
        {
            for (int z = 0; z < Nz; z++)
            for (int x = 0; x < Nx; x++)
                Allowed[z * Nx + x] = (byte)(free == null || free(new Vector2(LocalX(x), LocalZ(z))) ? 1 : 0);
        }

        public bool AllowedAt(float x, float z)
        {
            if (!Inside(x, z)) return false;
            int ix = Mathf.RoundToInt((x - X0) / Cell), iz = Mathf.RoundToInt((z - Z0) / Cell);
            return Allowed[Mathf.Clamp(iz, 0, Nz - 1) * Nx + Mathf.Clamp(ix, 0, Nx - 1)] != 0;
        }

        public float X0 => GX0 * Cell;
        public float Z0 => GZ0 * Cell;
        public float XMax => (GX0 + Nx - 1) * Cell;
        public float ZMax => (GZ0 + Nz - 1) * Cell;

        /// <summary>The starting island: a slightly wavy rounded square of side <paramref name="size"/> (1 inside, 0 outside).</summary>
        public static float OrganicPatch(float size, int seed, float x, float z)
        {
            var rng = new Rng(seed);
            float p1 = rng.Range(0f, 6.28f), p2 = rng.Range(0f, 6.28f), p3 = rng.Range(0f, 6.28f);
            float u = x / (size * 0.5f), v = z / (size * 0.5f);
            float d = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 3.2f) + Mathf.Pow(Mathf.Abs(v), 3.2f), 1f / 3.2f);
            float a = Mathf.Atan2(v, u);
            float shore = 0.97f + 0.025f * Mathf.Sin(3f * a + p1) + 0.018f * Mathf.Sin(5f * a + p2) + 0.01f * Mathf.Sin(11f * a + p3);
            return Mathf.Clamp01((shore - d) * size * 0.5f / CellSize + 0.5f);
        }

        public float LocalX(int x) => (GX0 + x) * Cell;
        public float LocalZ(int z) => (GZ0 + z) * Cell;

        /// <summary>Rim shaping: 0 at the shore, 1 well inland.</summary>
        public static float Shape(float edge) => edge * edge * (3f - 2f * edge);

        public void UpdateSurface(RectInt r)
        {
            for (int z = r.yMin; z < r.yMax; z++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                int i = z * Nx + x;
                Surface[i] = Height[i] * rim[i];
            }
        }

        /// <summary>
        /// Recomputes the rim (distance to the shore) for <paramref name="r"/>. Distances are capped at the rim width, so a
        /// chamfer transform over the region plus a margin of that width gives exact results inside the region.
        /// </summary>
        public void RecomputeEdge(RectInt r)
        {
            var w = Expand(r, RimCells + 1);
            const float inf = 1e9f, diag = 1.41421356f;
            for (int z = w.yMin; z < w.yMax; z++)
            for (int x = w.xMin; x < w.xMax; x++)
            {
                int i = z * Nx + x;
                distance[i] = Land[i] < 0.5f ? 0f : (x == 0 || z == 0 || x == Nx - 1 || z == Nz - 1) ? 0f : inf;
            }
            for (int z = w.yMin; z < w.yMax; z++)
            for (int x = w.xMin; x < w.xMax; x++)
            {
                int i = z * Nx + x;
                float d = distance[i];
                if (d == 0f) continue;
                if (x > w.xMin) d = Mathf.Min(d, distance[i - 1] + 1f);
                if (z > w.yMin)
                {
                    d = Mathf.Min(d, distance[i - Nx] + 1f);
                    if (x > w.xMin) d = Mathf.Min(d, distance[i - Nx - 1] + diag);
                    if (x < w.xMax - 1) d = Mathf.Min(d, distance[i - Nx + 1] + diag);
                }
                distance[i] = d;
            }
            for (int z = w.yMax - 1; z >= w.yMin; z--)
            for (int x = w.xMax - 1; x >= w.xMin; x--)
            {
                int i = z * Nx + x;
                float d = distance[i];
                if (d == 0f) continue;
                if (x < w.xMax - 1) d = Mathf.Min(d, distance[i + 1] + 1f);
                if (z < w.yMax - 1)
                {
                    d = Mathf.Min(d, distance[i + Nx] + 1f);
                    if (x < w.xMax - 1) d = Mathf.Min(d, distance[i + Nx + 1] + diag);
                    if (x > w.xMin) d = Mathf.Min(d, distance[i + Nx - 1] + diag);
                }
                distance[i] = d;
            }
            // Signed shore distance in metres, then two 3x3 blurs: chamfer distances are quantised per cell and would
            // otherwise print a diamond lattice into the rim's normals.
            for (int z = w.yMin; z < w.yMax; z++)
            for (int x = w.xMin; x < w.xMax; x++)
            {
                int i = z * Nx + x;
                distance[i] = (Mathf.Min(distance[i], RimCells + 2f) - 0.5f + Land[i]) * Cell;
            }
            var inner = Expand(r, 2);
            for (int pass = 0; pass < 2; pass++)
            {
                var src = pass == 0 ? distance : blur; var dst = pass == 0 ? blur : distance;
                var area = pass == 0 ? inner : r;
                for (int z = area.yMin; z < area.yMax; z++)
                for (int x = area.xMin; x < area.xMax; x++)
                {
                    float sum = 0f; int n = 0;
                    for (int oz = -1; oz <= 1; oz++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int xx = x + ox, zz = z + oz;
                        if (xx < w.xMin || zz < w.yMin || xx >= w.xMax || zz >= w.yMax) continue;
                        sum += src[zz * Nx + xx]; n++;
                    }
                    dst[z * Nx + x] = sum / n;
                }
            }
            for (int z = r.yMin; z < r.yMax; z++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                int i = z * Nx + x;
                Edge[i] = Land[i] < 0.5f ? 0f : Mathf.Clamp01((distance[i] + rimNoise[i]) / RimWidth);
                rim[i] = Shape(Edge[i]);
            }
        }

        private RectInt Expand(RectInt r, int by)
        {
            int x0 = Mathf.Max(0, r.xMin - by), z0 = Mathf.Max(0, r.yMin - by);
            int x1 = Mathf.Min(Nx, r.xMax + by), z1 = Mathf.Min(Nz, r.yMax + by);
            return new RectInt(x0, z0, x1 - x0, z1 - z0);
        }

        public bool Inside(float x, float z) => x >= X0 && x <= XMax && z >= Z0 && z <= ZMax;

        private float Bilinear(float[] grid, float x, float z)
        {
            float gx = Mathf.Clamp((x - X0) / Cell, 0f, Nx - 1.001f), gz = Mathf.Clamp((z - Z0) / Cell, 0f, Nz - 1.001f);
            int ix = (int)gx, iz = (int)gz;
            float fx = gx - ix, fz = gz - iz;
            int i = iz * Nx + ix;
            float a = Mathf.Lerp(grid[i], grid[i + 1], fx), b = Mathf.Lerp(grid[i + Nx], grid[i + Nx + 1], fx);
            return Mathf.Lerp(a, b, fz);
        }

        public float SurfaceAt(float x, float z) => Bilinear(Surface, x, z);
        public float EdgeAt(float x, float z) => Inside(x, z) ? Bilinear(Edge, x, z) : 0f;
        public bool LandAt(float x, float z) => Inside(x, z) && Bilinear(Land, x, z) >= 0.5f;
        public float WaterAt(float x, float z) => Inside(x, z) ? Bilinear(Water, x, z) : 0f;

        /// <summary>Water surface height at a point (the level of the nearest water cell), or -1 when dry.</summary>
        public float WaterLevelAt(float x, float z)
        {
            if (!Inside(x, z)) return -1f;
            int ix = Mathf.Clamp(Mathf.RoundToInt((x - X0) / Cell), 0, Nx - 1), iz = Mathf.Clamp(Mathf.RoundToInt((z - Z0) / Cell), 0, Nz - 1);
            int i = iz * Nx + ix;
            return Water[i] >= 0.5f ? WaterLevel[i] : -1f;
        }

        /// <summary>
        /// One frame of the Pond brush: cells inside the brush become water at <paramref name="level"/> and the ground
        /// under them is dug into a soft basin (deepest in the middle, banks sloping in); <paramref name="drain"/> removes
        /// water and fills the basin back up to its old water level. The railway corridor and house pads are left alone.
        /// </summary>
        public RectInt PaintWater(Vector2 centre, float radius, float strength, float dt, float level, bool drain)
        {
            var r = RegionAround(centre, radius);
            float r2 = radius * radius, rate = Mathf.Min(1f, 6f * strength * dt);
            for (int z = r.yMin; z < r.yMax; z++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                int i = z * Nx + x;
                float dx = LocalX(x) - centre.x, dz = LocalZ(z) - centre.y;
                float d2 = (dx * dx + dz * dz) / r2 * (1f + rimNoise[i] * 2.5f);
                if (d2 >= 1f || Lock[i] > 0.05f || Pad[i] > 0.05f || Land[i] < 0.5f || rim[i] < 0.15f) continue;
                float inner = 1f - d2;
                if (drain)
                {
                    if (Water[i] < 0.5f) continue;
                    float fill = HeightForSurface(i, WaterLevel[i] + 0.002f);
                    Height[i] = Mathf.Lerp(Height[i], Mathf.Max(Height[i], fill), Mathf.Min(1f, rate * 2f));
                    if (Surface[i] >= WaterLevel[i]) Water[i] = 0f;
                    Water[i] = inner > 0.2f ? 0f : Water[i];
                    continue;
                }
                if (inner > 0.22f && rim[i] > 0.65f)   // water stays clear of the shore taper, where no bank could hold it
                {
                    if (Water[i] < 0.5f) { Water[i] = 1f; WaterLevel[i] = level; }
                    float lvl = WaterLevel[i];
                    float bed = lvl - Mathf.Min(PondDepth, lvl - MinHeight - 0.002f) * Mathf.SmoothStep(0.15f, 1f, (inner - 0.22f) / 0.78f) - 0.003f;
                    float target = HeightForSurface(i, bed);
                    if (target < Height[i]) Height[i] = Mathf.Lerp(Height[i], target, rate);
                }
                else if (Water[i] < 0.5f && inner <= 0.22f)
                {
                    // The bank: shaped to stand just above the water - eased down where the ground was higher, raised
                    // into a little levee where it was lower (downhill, towards the shore), so the pond always holds.
                    float bank = level + 0.002f + (0.22f - inner) * 0.04f;
                    float target = HeightForSurface(i, bank);
                    if (Surface[i] < level + 0.0015f) Height[i] = Mathf.Max(Height[i], Mathf.Lerp(Height[i], target, rate));
                    else if (target < Height[i]) Height[i] = Mathf.Lerp(Height[i], target, rate * 0.5f);
                }
            }
            UpdateSurface(r);
            return r;
        }
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
            var bounds = new Bounds(new Vector3((X0 + XMax) * 0.5f, MaxHeight * 0.5f, (Z0 + ZMax) * 0.5f), new Vector3(XMax - X0, MaxHeight + 0.02f, ZMax - Z0));
            if (!bounds.IntersectRay(new Ray(origin, direction), out float t0)) return false;
            t0 = Mathf.Max(0f, t0);
            float step = Cell * 0.5f;
            float prevT = t0;
            bool prevAbove = true;
            for (float t = t0; t < maxDistance; t += step)
            {
                var p = origin + direction * t;
                if (p.y < -0.01f) break;
                if (!Inside(p.x, p.z)) { prevT = t; prevAbove = true; continue; }
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

        /// <summary>Ray against the real floor (y = 0) inside the grid: where land can be grown.</summary>
        public bool RaycastFloor(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 hit)
        {
            hit = default;
            direction.Normalize();
            if (direction.y > -1e-4f) return false;
            float t = -origin.y / direction.y;
            if (t < 0f || t > maxDistance) return false;
            hit = origin + direction * t;
            hit.y = 0f;
            // Anywhere on the floor within reach: the grid grows to meet new land.
            return Mathf.Abs(hit.x) < MaxReach - 0.3f && Mathf.Abs(hit.z) < MaxReach - 0.3f;
        }

        public RectInt RegionAround(Vector2 centre, float radius)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((centre.x - radius - X0) / Cell) - 1);
            int x1 = Mathf.Min(Nx, Mathf.CeilToInt((centre.x + radius - X0) / Cell) + 2);
            int z0 = Mathf.Max(0, Mathf.FloorToInt((centre.y - radius - Z0) / Cell) - 1);
            int z1 = Mathf.Min(Nz, Mathf.CeilToInt((centre.y + radius - Z0) / Cell) + 2);
            return new RectInt(x0, z0, Mathf.Max(0, x1 - x0), Mathf.Max(0, z1 - z0));
        }

        /// <summary>
        /// One frame of a brush stroke; returns the grid region whose surface changed. Sculpting uses a smooth bell and
        /// relaxes the touched ground so hills stay rounded and lush; Grow / Trim paint the land mask with a noisy edge
        /// so new shores stay organic.
        /// </summary>
        public RectInt ApplyBrush(TerrainBrush brush, Vector2 centre, float radius, float strength, float dt, float flattenTarget)
        {
            if (brush == TerrainBrush.Grow || brush == TerrainBrush.Trim) return PaintLand(brush == TerrainBrush.Grow, centre, radius, strength, dt);
            var r = RegionAround(centre, radius);
            float r2 = radius * radius;
            if (brush == TerrainBrush.Smooth || brush == TerrainBrush.Raise || brush == TerrainBrush.Lower || brush == TerrainBrush.Peaks)
                CopyRegion(Height, scratch, Expand(r, 1));
            for (int z = r.yMin; z < r.yMax; z++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                float dx = LocalX(x) - centre.x, dz = LocalZ(z) - centre.y;
                float d2 = (dx * dx + dz * dz) / r2;
                if (d2 >= 1f) continue;
                int i = z * Nx + x;
                // Fade towards the shore (where the landscape tapers to the floor) so hills never pile up into a cliff there.
                // The railway is not protected: the track re-profiles to sculpted ground. House pads are.
                float w = (1f - d2) * (1f - d2) * (1f - Pad[i]) * Mathf.Sqrt(rim[i]);
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
                    case TerrainBrush.Peaks:
                    {
                        // Ridged noise: sharp crests and gullies, so mountains grow craggy summits instead of domes.
                        float px = LocalX(x), pz = LocalZ(z);
                        float ridge = 1f - Mathf.Abs(Noise.Fbm2(px * 9f + 31f, pz * 9f - 17f, 3) * 2f - 1f);
                        ridge = ridge * ridge;
                        h += RaiseRate * 1.4f * strength * dt * w * (0.25f + 1.5f * ridge) * (1f - Mathf.Pow(h / MaxHeight, 4f));
                        h += relax * Mathf.Min(1f, 4f * dt) * w * 0.2f;
                        break;
                    }
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
            if (brush == TerrainBrush.Peaks) Slump(r, 1);
            UpdateSurface(r);
            return r;
        }

        private RectInt PaintLand(bool grow, Vector2 centre, float radius, float strength, float dt)
        {
            var r = RegionAround(centre, radius);
            float r2 = radius * radius;
            bool changed = false;
            for (int z = r.yMin; z < r.yMax; z++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                if (x < 2 || z < 2 || x >= Nx - 2 || z >= Nz - 2) continue;   // keep a floor margin around the grid
                float dx = LocalX(x) - centre.x, dz = LocalZ(z) - centre.y;
                int i = z * Nx + x;
                if (grow && Allowed[i] == 0) continue;                         // walls, furniture, outside the room
                // Roughen the brush edge so painted shores meander.
                float d2 = (dx * dx + dz * dz) / r2 * (1f + rimNoise[i] * 3f);
                if (d2 >= 1f) continue;
                if (!grow && (Lock[i] > 0.02f || Pad[i] > 0.02f || (Protect != null && Protect(LocalX(x), LocalZ(z))))) continue;
                float w = Mathf.Clamp01((1f - d2) * 2.5f) * Mathf.Min(1f, 5f * strength * dt);
                float before = Land[i];
                Land[i] = Mathf.Clamp01(Land[i] + (grow ? w : -w));
                changed |= before != Land[i];
            }
            if (!changed) return new RectInt(r.xMin, r.yMin, 0, 0);
            var affected = Expand(r, RimCells + 1);
            RecomputeEdge(affected);
            if (!grow && RemoveSlivers(Expand(r, 8))) RecomputeEdge(affected);
            UpdateSurface(affected);
            return affected;
        }

        /// <summary>
        /// Trimming can leave thin crescents of land that are too narrow to rise (a ghostly line on the floor): drop land
        /// with no cell at least ~6 cm inland within 12 cm. The shore of real land always has such a cell nearby.
        /// </summary>
        private bool RemoveSlivers(RectInt r)
        {
            bool removed = false;
            for (int z = Mathf.Max(3, r.yMin); z < Mathf.Min(Nz - 3, r.yMax); z++)
            for (int x = Mathf.Max(3, r.xMin); x < Mathf.Min(Nx - 3, r.xMax); x++)
            {
                int i = z * Nx + x;
                if (Land[i] < 0.5f || Lock[i] > 0.02f || Pad[i] > 0.02f) continue;
                float best = 0f;
                for (int oz = -3; oz <= 3 && best < 0.22f; oz++)
                for (int ox = -3; ox <= 3; ox++) best = Mathf.Max(best, Edge[i + oz * Nx + ox]);
                if (best < 0.22f) { Land[i] = 0f; removed = true; }
            }
            return removed;
        }

        /// <summary>
        /// Talus relaxation: where ground is steeper than <see cref="MaxSlope"/> material slides to the lower neighbour, so
        /// raising keeps widening a hill into a soft, rolling mound instead of growing a spike. Compares rendered surfaces
        /// (height x rim taper) so steepness made by the rim counts too.
        /// </summary>
        private void Slump(RectInt r, int iterations)
        {
            float talus = MaxSlope * Cell;
            var window = Expand(r, 1);
            for (int it = 0; it < iterations; it++)
            {
                CopyRegion(Height, scratch, window);
                for (int z = Mathf.Max(1, r.yMin); z < Mathf.Min(Nz - 1, r.yMax); z++)
                for (int x = Mathf.Max(1, r.xMin); x < Mathf.Min(Nx - 1, r.xMax); x++)
                {
                    int i = z * Nx + x;
                    if (Pad[i] > 0.5f) continue;
                    float si = Math.Max(rim[i], 0.15f), h = scratch[i] * rim[i];
                    for (int k = 0; k < 4; k++)
                    {
                        int j = k == 0 ? i - 1 : k == 1 ? i + 1 : k == 2 ? i - Nx : i + Nx;
                        float excess = h - scratch[j] * rim[j] - talus;
                        if (excess <= 0f || Pad[j] > 0.5f) continue;
                        float move = excess * 0.2f / si;
                        Height[i] -= move; Height[j] += move * 0.6f;
                    }
                }
            }
        }

        private void CopyRegion(float[] from, float[] to, RectInt r)
        {
            for (int z = r.yMin; z < r.yMax; z++) Array.Copy(from, z * Nx + r.xMin, to, z * Nx + r.xMin, r.width);
        }

        private float Average(float[] g, int x, int z)
        {
            float sum = 0f, wsum = 0f;
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int xx = x + ox, zz = z + oz;
                if (xx < 0 || zz < 0 || xx >= Nx || zz >= Nz) continue;
                float w = (ox == 0 && oz == 0) ? 4f : (ox == 0 || oz == 0) ? 2f : 1f;
                sum += g[zz * Nx + xx] * w; wsum += w;
            }
            return sum / wsum;
        }

        /// <summary>Raw height that renders as <paramref name="surface"/> at grid index <paramref name="i"/>.</summary>
        public float HeightForSurface(int i, float surface)
        {
            float s = rim[i];
            return s > 0.05f ? Mathf.Clamp(surface / s, MinHeight, MaxHeight) : Height[i];
        }

        /// <summary>Restores data saved for this grid size (resize first if the snapshot's grid differs).</summary>
        public void CopyFrom(float[] height, float[] land, float[] locks, float[] soil, float[] water = null, float[] waterLevel = null)
        {
            if (height.Length != Height.Length) throw new ArgumentException("Terrain data does not match the grid size.");
            if (water != null && water.Length == Water.Length) Array.Copy(water, Water, Water.Length); else Array.Clear(Water, 0, Water.Length);
            if (waterLevel != null && waterLevel.Length == WaterLevel.Length) Array.Copy(waterLevel, WaterLevel, WaterLevel.Length); else Array.Clear(WaterLevel, 0, WaterLevel.Length);
            Array.Copy(height, Height, Height.Length);
            Array.Copy(land, Land, Land.Length);
            Array.Copy(locks, Lock, Lock.Length);
            Array.Copy(soil, Soil, Soil.Length);
            RecomputeEdge(All);
            UpdateSurface(All);
        }

        /// <summary>
        /// Makes everything within <paramref name="radius"/> of the path land (railway drawn over bare floor). Returns
        /// the touched region.
        /// </summary>
        public RectInt GrowLandAlong(List<Vector2> path, float radius)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in path) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
            var r = RegionAround(new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f), Mathf.Max(maxX - minX, maxZ - minZ) / 2f + radius + Cell);
            float r2 = radius * radius;
            for (int z = Mathf.Max(2, r.yMin); z < Mathf.Min(Nz - 2, r.yMax); z++)
            for (int x = Mathf.Max(2, r.xMin); x < Mathf.Min(Nx - 2, r.xMax); x++)
            {
                int i = z * Nx + x;
                if (Land[i] >= 1f) continue;
                var c = new Vector2(LocalX(x), LocalZ(z));
                for (int k = 0; k < path.Count; k += 3)
                    if ((path[k] - c).sqrMagnitude < r2) { Land[i] = 1f; break; }
            }
            var affected = Expand(r, RimCells + 1);
            RecomputeEdge(affected);
            UpdateSurface(affected);
            return affected;
        }

        /// <summary>Land area in square metres (for feedback and tests).</summary>
        public float LandArea()
        {
            int count = 0;
            foreach (float l in Land) if (l >= 0.5f) count++;
            return count * Cell * Cell;
        }
    }
}
