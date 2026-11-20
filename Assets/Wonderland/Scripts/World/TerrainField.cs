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
        /// <summary>What the ground is paved with (see <see cref="Paving"/>; 0: grass, nothing laid).</summary>
        public byte[] Pave;
        /// <summary>House pads (1 = under a house): brushes and trimming leave them alone. Derived from the houses.</summary>
        public float[] Pad;
        /// <summary>Ponds and lakes: water mask (1 = water) and the water surface height for each water cell.</summary>
        public float[] Water, WaterLevel;
        public const float PondDepth = 0.028f;
        /// <summary>
        /// Land everywhere: fresh cells are flat land (not bare floor), the grid's edge is no shore - the land goes on past
        /// it - and land, edge and height are those of fresh flat land outside the grid.
        /// </summary>
        public bool Endless { get; private set; }
        /// <summary>
        /// The ground outside the grid (an endless world): the grid hands it the chunks it lets go of and takes them back
        /// when it comes their way, and every query off the grid reads it - or fresh flat land where nothing was changed -
        /// so the land is known everywhere (the trains and people out of sight walk and run on the real ground).
        /// </summary>
        public TerrainStore Store;
        /// <summary>The part of the world shown (local XZ): the pointer finds the land only inside it. Null: everywhere.</summary>
        public Rect? Window;
        /// <summary>1 where land may grow (free floor inside the room).</summary>
        public byte[] Allowed;
        private float[] scratch, rim, rimNoise, distance, blur;   // rim = Shape(Edge); rimNoise = shoreline roughness (m)
        private float[] fresh;   // each cell's fresh-land height (NaN: none), to tell untouched land cheaply

        /// <summary>Cells the Trim brush must not remove (e.g. under houses). Set by the world.</summary>
        public Func<float, float, bool> Protect;

        /// <summary>
        /// Ground the brushes leave alone (1) because something lies flat on it - a road, a path, the railway - so it can
        /// never be pushed up through them. Filled by the world (<see cref="KeepFill"/>) whenever <see cref="KeepKey"/>
        /// says the roads or track changed.
        /// </summary>
        public float[] Keep;
        public Func<int> KeepKey;
        public Action<float[]> KeepFill;
        private int keepKey = int.MinValue;

        private float KeepAt(int i)
        {
            if (KeepFill == null) return 0f;
            int key = (KeepKey?.Invoke() ?? 0) * 31 + Height.Length;
            if (Keep == null || Keep.Length != Height.Length || key != keepKey)
            {
                Keep = new float[Height.Length];
                KeepFill(Keep);
                keepKey = key;
            }
            return Keep[i];
        }

        /// <summary>Marks ground as kept (see <see cref="Keep"/>) within <paramref name="radius"/> of a local point.</summary>
        public void MarkKeep(float[] keep, Vector2 p, float radius)
        {
            var r = RegionAround(p, radius);
            float r2 = radius * radius;
            for (int z = r.yMin; z < r.yMax; z++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                float dx = LocalX(x) - p.x, dz = LocalZ(z) - p.y;
                if (dx * dx + dz * dz <= r2) keep[z * Nx + x] = 1f;
            }
        }

        public TerrainField(float patchSize, int seed, bool hills = true, RectInt? cells = null, bool endless = false)
        {
            Seed = seed;
            Endless = endless;
            Allocate(cells ?? new RectInt(-DefaultHalfCells, -DefaultHalfCells, DefaultHalfCells * 2 + 1, DefaultHalfCells * 2 + 1));
            PatchSize = Mathf.Min(patchSize, Mathf.Min(Nx, Nz) * Cell - 0.4f);
            for (int z = 0; z < Nz; z++)
            for (int x = 0; x < Nx; x++)
            {
                int i = z * Nx + x;
                InitCell(i, x, z, hills || endless);   // (endless: fresh land everywhere is the same gently uneven land)
                if (!endless) Land[i] = OrganicPatch(PatchSize, seed, LocalX(x), LocalZ(z));
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
            changedAll = true;
            GX0 = c.x; GZ0 = c.y; Nx = c.width; Nz = c.height;
            int n = Nx * Nz;
            Height = new float[n]; Surface = new float[n]; Land = new float[n]; Edge = new float[n]; Lock = new float[n]; Soil = new float[n]; Pad = new float[n]; Pave = new byte[n];
            Water = new float[n]; WaterLevel = new float[n];
            Allowed = new byte[n]; scratch = new float[n]; rim = new float[n]; rimNoise = new float[n]; distance = new float[n]; blur = new float[n]; fresh = new float[n];
            for (int i = 0; i < n; i++) Allowed[i] = 1;
        }

        /// <summary>Fresh cell: base height with a little noise, no land (flat land when <see cref="Endless"/>). Noise uses
        /// global coordinates, so it is identical however the grid is cut.</summary>
        private void InitCell(int i, int x, int z, bool undulate = true)
        {
            float lx = LocalX(x), lz = LocalZ(z);
            rimNoise[i] = float.NaN;   // (worked out when a shore is near: RimNoise)
            Height[i] = BaseHeight + (undulate ? (Noise.Fbm2(lx * 4f + Seed, lz * 4f) - 0.5f) * 0.008f : 0f);
            fresh[i] = undulate ? Height[i] : float.NaN;
            Land[i] = Endless ? 1f : 0f; Lock[i] = 0f; Soil[i] = 0f; Water[i] = 0f; WaterLevel[i] = 0f; Pave[i] = 0;
        }

        /// <summary>
        /// Turns an island world into an endless one (an older save): the bare floor round the islands becomes flat land
        /// at the base height, so the land goes on everywhere. Ponds, hills and everything on the land stay as they were.
        /// </summary>
        public void MakeEndless()
        {
            if (Endless) return;
            Endless = true;
            for (int z = 0; z < Nz; z++)
            for (int x = 0; x < Nx; x++)
            {
                int i = z * Nx + x;
                if (Land[i] >= 0.5f) continue;
                Land[i] = 1f;
                Height[i] = fresh[i] = BaseHeight + (Noise.Fbm2(LocalX(x) * 4f + Seed, LocalZ(z) * 4f) - 0.5f) * 0.008f;
            }
            RecomputeEdge(All);
            UpdateSurface(All);
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
            if (Store != null) Evict(oldRect, cells);
            float[] h = Height, l = Land, lk = Lock, so = Soil, wa = Water, wl = WaterLevel, rn = rimNoise, fr = fresh, ed = Edge, rm = rim, sf = Surface;
            byte[] pv = Pave;
            Allocate(cells);
            for (int z = 0; z < Nz; z++)
            for (int x = 0; x < Nx; x++)
            {
                int i = z * Nx + x, gx = GX0 + x, gz = GZ0 + z;
                if (gx < oldRect.xMin || gx >= oldRect.xMax || gz < oldRect.yMin || gz >= oldRect.yMax)
                {
                    // Coming back into the grid: the ground as it was left (no need to work out fresh land there).
                    if (Store != null && Store.TryGet(TerrainStore.ChunkOf(gx), TerrainStore.ChunkOf(gz), out var c))
                    {
                        int k = ChunkIndex(gx, gz);
                        Height[i] = c.Height[k]; Land[i] = c.Land[k] / 255f; Lock[i] = c.Lock[k] / 255f; Soil[i] = c.Soil[k] / 255f; Water[i] = c.Water[k] / 255f; WaterLevel[i] = c.WaterLevel[k];
                        Pave[i] = c.Pave[k];
                        rimNoise[i] = float.NaN; fresh[i] = float.NaN;
                    }
                    else InitCell(i, x, z);
                    continue;
                }
                int j = (gz - oldGZ) * oldNx + (gx - oldGX);
                Height[i] = h[j]; Land[i] = l[j]; Lock[i] = lk[j]; Soil[i] = so[j]; Water[i] = wa[j]; WaterLevel[i] = wl[j]; Pave[i] = pv[j];
                rimNoise[i] = rn[j]; fresh[i] = fr[j]; Edge[i] = ed[j]; rim[i] = rm[j]; Surface[i] = sf[j];
            }
            // An endless world's kept land keeps its shores: only round the new land are they worked out again.
            var redo = All;
            if (Endless && oldRect.Overlaps(cells))
            {
                int nx0 = int.MaxValue, nz0 = int.MaxValue, nx1 = int.MinValue, nz1 = int.MinValue;
                for (int z = 0; z < Nz; z++)
                for (int x = 0; x < Nx; x++)
                {
                    int gx = GX0 + x, gz = GZ0 + z;
                    if (gx >= oldRect.xMin && gx < oldRect.xMax && gz >= oldRect.yMin && gz < oldRect.yMax) { x = Mathf.Max(x, oldRect.xMax - GX0 - 1); continue; }
                    nx0 = Mathf.Min(nx0, x); nz0 = Mathf.Min(nz0, z); nx1 = Mathf.Max(nx1, x); nz1 = Mathf.Max(nz1, z);
                }
                redo = nx0 > nx1 ? new RectInt(0, 0, 0, 0) : Expand(new RectInt(nx0, nz0, nx1 - nx0 + 1, nz1 - nz0 + 1), RimCells + 2);
            }
            RecomputeEdge(redo);
            UpdateSurface(redo);
            if (MendOld) RepairWalls();   // (land coming back into the grid from a save made before the fault was mended)
        }

        private static int ChunkIndex(int gx, int gz) =>
            (gz - TerrainStore.ChunkOf(gz) * TerrainStore.Cells) * TerrainStore.Cells + (gx - TerrainStore.ChunkOf(gx) * TerrainStore.Cells);

        /// <summary>Fresh land's height at a global cell (the same however the grid is cut).</summary>
        private float FreshHeight(int gx, int gz) => BaseHeight + (Noise.Fbm2(gx * Cell * 4f + Seed, gz * Cell * 4f) - 0.5f) * 0.008f;

        /// <summary>
        /// Hands the store every chunk the grid is letting go of (the part of it leaving the grid; the rest as the store
        /// had it, or fresh land) - unless it is fresh land through and through, which needs no keeping.
        /// </summary>
        private void Evict(RectInt from, RectInt to)
        {
            int n = TerrainStore.Cells;
            for (int cz = TerrainStore.ChunkOf(from.yMin); cz <= TerrainStore.ChunkOf(from.yMax - 1); cz++)
            for (int cx = TerrainStore.ChunkOf(from.xMin); cx <= TerrainStore.ChunkOf(from.xMax - 1); cx++)
            {
                var span = new RectInt(cx * n, cz * n, n, n);
                bool leaving = false;
                for (int gz = Mathf.Max(span.yMin, from.yMin); gz < Mathf.Min(span.yMax, from.yMax) && !leaving; gz++)
                for (int gx = Mathf.Max(span.xMin, from.xMin); gx < Mathf.Min(span.xMax, from.xMax) && !leaving; gx++)
                    leaving = !to.Contains(new Vector2Int(gx, gz));
                if (!leaving) continue;
                bool kept = Store.TryGet(cx, cz, out var c);
                if (!kept)
                {
                    // Untouched land (the usual case) needs no keeping: found out before building anything.
                    bool untouched = true;
                    for (int gz = Mathf.Max(span.yMin, from.yMin); gz < Mathf.Min(span.yMax, from.yMax) && untouched; gz++)
                    for (int gx = Mathf.Max(span.xMin, from.xMin); gx < Mathf.Min(span.xMax, from.xMax) && untouched; gx++)
                    {
                        int i = (gz - GZ0) * Nx + (gx - GX0);
                        untouched = Lock[i] == 0f && Soil[i] == 0f && Water[i] == 0f && Pave[i] == 0 && Land[i] == 1f && Mathf.Abs(Height[i] - fresh[i]) <= 1e-5f && Mathf.Abs(Surface[i] - Height[i]) <= 1e-5f;
                    }
                    if (untouched) continue;
                    c = FreshChunk(cx, cz, from);
                }
                bool isFresh = false, changed = false;
                for (int gz = Mathf.Max(span.yMin, from.yMin); gz < Mathf.Min(span.yMax, from.yMax); gz++)
                for (int gx = Mathf.Max(span.xMin, from.xMin); gx < Mathf.Min(span.xMax, from.xMax); gx++)
                {
                    int i = (gz - GZ0) * Nx + (gx - GX0), k = ChunkIndex(gx, gz);
                    if (kept && !changed) changed = c.Height[k] != Height[i] || c.Surface[k] != Surface[i] || c.WaterLevel[k] != WaterLevel[i] || c.Land[k] != Byte(Land[i])
                        || c.Lock[k] != Byte(Lock[i]) || c.Soil[k] != Byte(Soil[i]) || c.Water[k] != Byte(Water[i]) || c.Pave[k] != Pave[i];
                    c.Pave[k] = Pave[i];
                    c.Height[k] = Height[i]; c.Surface[k] = Surface[i]; c.WaterLevel[k] = WaterLevel[i];
                    c.Land[k] = Byte(Land[i]); c.Lock[k] = Byte(Lock[i]); c.Soil[k] = Byte(Soil[i]); c.Water[k] = Byte(Water[i]);
                    if (isFresh && (c.Lock[k] != 0 || c.Soil[k] != 0 || c.Pave[k] != 0 || c.Water[k] != 0 || c.Land[k] != 255 || !(Mathf.Abs(Height[i] - this.fresh[i]) <= 1e-5f) || Mathf.Abs(Surface[i] - Height[i]) > 1e-5f))
                        isFresh = false;
                }
                if (!isFresh && (!kept || changed)) Store.Put(cx, cz, c);
            }
        }

        private static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        /// <summary>A chunk of fresh flat land (the cells of <paramref name="skip"/>, about to be written over, are left).</summary>
        private TerrainStore.Chunk FreshChunk(int cx, int cz, RectInt? skip = null)
        {
            var c = new TerrainStore.Chunk();
            int n = TerrainStore.Cells;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int k = z * n + x;
                if (skip is RectInt sk && sk.Contains(new Vector2Int(cx * n + x, cz * n + z))) continue;
                c.Height[k] = c.Surface[k] = FreshHeight(cx * n + x, cz * n + z);
                c.Land[k] = (byte)(Endless ? 255 : 0);
            }
            return c;
        }

        /// <summary>
        /// Puts ground back as it was (undo): the cells of <paramref name="span"/> (global) that are in the grid into it,
        /// the rest into the store. Returns the grid cells whose look may have changed (empty when nothing did).
        /// </summary>
        public RectInt WriteCells(RectInt span, float[] height, float[] waterLevel, byte[] land, byte[] locks, byte[] soil, byte[] water, byte[] pave = null)
        {
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            TerrainStore.Chunk chunk = null; int ccx = 0, ccz = 0; bool chunkChanged = false;
            for (int gz = span.yMin; gz < span.yMax; gz++)
            for (int gx = span.xMin; gx < span.xMax; gx++)
            {
                int k = (gz - span.yMin) * span.width + (gx - span.xMin);
                if (gx >= GX0 && gx < GX0 + Nx && gz >= GZ0 && gz < GZ0 + Nz)
                {
                    int i = (gz - GZ0) * Nx + (gx - GX0);
                    if (Height[i] == height[k] && WaterLevel[i] == waterLevel[k] && Byte(Land[i]) == land[k] && Byte(Lock[i]) == locks[k] && Byte(Soil[i]) == soil[k] && Byte(Water[i]) == water[k] && Pave[i] == (pave != null ? pave[k] : 0)) continue;
                    Height[i] = height[k]; WaterLevel[i] = waterLevel[k];
                    Land[i] = land[k] / 255f; Lock[i] = locks[k] / 255f; Soil[i] = soil[k] / 255f; Water[i] = water[k] / 255f; Pave[i] = pave != null ? pave[k] : (byte)0;
                    int x = gx - GX0, z = gz - GZ0;
                    x0 = Mathf.Min(x0, x); z0 = Mathf.Min(z0, z); x1 = Mathf.Max(x1, x); z1 = Mathf.Max(z1, z);
                }
                else if (Store != null)
                {
                    if (chunk == null)
                    {
                        ccx = TerrainStore.ChunkOf(gx); ccz = TerrainStore.ChunkOf(gz);
                        if (!Store.TryGet(ccx, ccz, out chunk)) chunk = FreshChunk(ccx, ccz);
                    }
                    int c = ChunkIndex(gx, gz);
                    chunk.Height[c] = chunk.Surface[c] = height[k]; chunk.WaterLevel[c] = waterLevel[k];
                    chunk.Land[c] = land[k]; chunk.Lock[c] = locks[k]; chunk.Soil[c] = soil[k]; chunk.Water[c] = water[k]; chunk.Pave[c] = pave != null ? pave[k] : (byte)0;
                    chunkChanged = true;
                }
            }
            if (chunkChanged) Store.Put(ccx, ccz, chunk);
            if (x0 > x1) return new RectInt(0, 0, 0, 0);
            var r = Expand(new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1), RimCells + 2);
            RecomputeEdge(r);
            UpdateSurface(r);
            return r;
        }

        /// <summary>
        /// Cuts the grid to <paramref name="cells"/> from the store alone (a world being loaded from its chunk files): what
        /// the grid held before is dropped, not kept.
        /// </summary>
        public void LoadFromStore(RectInt cells)
        {
            Allocate(cells);
            for (int z = 0; z < Nz; z++)
            for (int x = 0; x < Nx; x++)
            {
                int i = z * Nx + x, gx = GX0 + x, gz = GZ0 + z;
                InitCell(i, x, z);
                if (Store != null && Store.TryGet(TerrainStore.ChunkOf(gx), TerrainStore.ChunkOf(gz), out var c))
                {
                    int k = ChunkIndex(gx, gz);
                    Height[i] = c.Height[k]; Land[i] = c.Land[k] / 255f; Lock[i] = c.Lock[k] / 255f; Soil[i] = c.Soil[k] / 255f; Water[i] = c.Water[k] / 255f; WaterLevel[i] = c.WaterLevel[k];
                    Pave[i] = c.Pave[k];
                }
            }
            RecomputeEdge(All);
            UpdateSurface(All);
        }

        /// <summary>Hands the store the whole grid (before saving: the chunk files then hold everything).</summary>
        public void FlushToStore()
        {
            if (Store == null) return;
            var none = new RectInt(int.MinValue / 4, int.MinValue / 4, 1, 1);
            Evict(Cells, none);
        }

        /// <summary>A global cell's value off the grid: what the store keeps, or fresh land's.</summary>
        private float OffGrid(int gx, int gz, int what)
        {
            if (gx >= GX0 && gx < GX0 + Nx && gz >= GZ0 && gz < GZ0 + Nz)
            {
                int i = (gz - GZ0) * Nx + (gx - GX0);
                return what switch { 0 => Surface[i], 1 => Land[i], 2 => Edge[i], 3 => Water[i], 4 => WaterLevel[i], _ => Lock[i] };
            }
            if (Store != null && Store.TryGet(TerrainStore.ChunkOf(gx), TerrainStore.ChunkOf(gz), out var c))
            {
                int k = ChunkIndex(gx, gz);
                return what switch { 0 => c.Surface[k], 1 or 2 => c.Land[k] / 255f, 3 => c.Water[k] / 255f, 4 => c.WaterLevel[k], _ => c.Lock[k] / 255f };
            }
            return what switch { 0 => FreshHeight(gx, gz), 1 or 2 => Endless ? 1f : 0f, _ => 0f };
        }

        /// <summary>Bilinear off the grid (0 surface, 1 land, 2 edge, 3 water, 4 water level, 5 lock).</summary>
        private float SampleOff(float x, float z, int what)
        {
            float fx = x / Cell, fz = z / Cell;
            int gx = Mathf.FloorToInt(fx), gz = Mathf.FloorToInt(fz);
            fx -= gx; fz -= gz;
            float a = Mathf.Lerp(OffGrid(gx, gz, what), OffGrid(gx + 1, gz, what), fx);
            float b = Mathf.Lerp(OffGrid(gx, gz + 1, what), OffGrid(gx + 1, gz + 1, what), fx);
            return Mathf.Lerp(a, b, fz);
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

        // Where the ground has changed since the ways last rebuilt (local space; overlapping changes merged), or everywhere.
        private readonly List<Rect> changed = new();
        private bool changedAll = true;

        /// <summary>Whether the ground has changed in (or a cell beside) <paramref name="area"/> since <see cref="ForgetChanges"/>.</summary>
        public bool ChangedNear(Rect area)
        {
            if (changedAll) return true;
            foreach (var r in changed) if (r.Overlaps(area)) return true;
            return false;
        }

        /// <summary>The ways have caught up with the ground.</summary>
        public void ForgetChanges() { changed.Clear(); changedAll = false; }

        private void NoteChanged(Rect area)
        {
            for (int k = changed.Count - 1; k >= 0; k--)
            {
                var r = changed[k];
                if (!r.Overlaps(area)) continue;
                area = Rect.MinMaxRect(Mathf.Min(r.xMin, area.xMin), Mathf.Min(r.yMin, area.yMin), Mathf.Max(r.xMax, area.xMax), Mathf.Max(r.yMax, area.yMax));
                changed.RemoveAt(k);
            }
            changed.Add(area);
            if (changed.Count > 64) changedAll = true;
        }

        public void UpdateSurface(RectInt r)
        {
            if (r.width > 0 && r.height > 0 && !changedAll)
                NoteChanged(Rect.MinMaxRect(LocalX(r.xMin) - Cell, LocalZ(r.yMin) - Cell, LocalX(r.xMax) + Cell, LocalZ(r.yMax) + Cell));
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
                distance[i] = Land[i] < 0.5f ? 0f : !Endless && (x == 0 || z == 0 || x == Nx - 1 || z == Nz - 1) ? 0f : inf;
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
                // (Far from any shore the edge is 1 whatever the shoreline's roughness: no need to work that out.)
                Edge[i] = Land[i] < 0.5f ? 0f : distance[i] - 0.13f >= RimWidth ? 1f : Mathf.Clamp01((distance[i] + RimNoise(i, x, z)) / RimWidth);
                rim[i] = Shape(Edge[i]);
            }
        }

        /// <summary>The shoreline's roughness at a cell (metres), worked out the first time it is needed.</summary>
        private float RimNoise(int i, int x, int z)
        {
            if (float.IsNaN(rimNoise[i])) rimNoise[i] = (Noise.Fbm2(LocalX(x) * 3.5f + Seed * 0.71f, LocalZ(z) * 3.5f - Seed * 0.37f, 3) - 0.5f) * 0.12f;
            return rimNoise[i];
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

        public float SurfaceAt(float x, float z) => Inside(x, z) || !Endless ? Bilinear(Surface, x, z) : SampleOff(x, z, 0);
        public float EdgeAt(float x, float z) => Inside(x, z) ? Bilinear(Edge, x, z) : Endless ? SampleOff(x, z, 2) : 0f;
        public bool LandAt(float x, float z) => Inside(x, z) ? Bilinear(Land, x, z) >= 0.5f : Endless && SampleOff(x, z, 1) >= 0.5f;
        /// <summary>Whether a point is inside the window (or there is no window).</summary>
        public bool Shown(float x, float z) => Window is not Rect w || x >= w.xMin && x <= w.xMax && z >= w.yMin && z <= w.yMax;
        public float WaterAt(float x, float z) => Inside(x, z) ? Bilinear(Water, x, z) : Endless ? SampleOff(x, z, 3) : 0f;

        /// <summary>Water surface height at a point (the level of the nearest water cell), or -1 when dry.</summary>
        public float WaterLevelAt(float x, float z)
        {
            if (!Inside(x, z))
            {
                if (!Endless) return -1f;
                int gx = Mathf.RoundToInt(x / Cell), gz = Mathf.RoundToInt(z / Cell);
                return OffGrid(gx, gz, 3) >= 0.5f ? OffGrid(gx, gz, 4) : -1f;
            }
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
                float d2 = (dx * dx + dz * dz) / r2 * (1f + RimNoise(i, x, z) * 2.5f);
                if (d2 >= 1f || Lock[i] > 0.05f || Pad[i] > 0.05f || Land[i] < 0.5f || rim[i] < 0.15f || KeepAt(i) > 0.5f) continue;
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
            Despike(r);
            SmoothShores(r);
            UpdateSurface(r);
            return r;
        }

        /// <summary>
        /// Smooths the ground along a pond's edge - the cells with pond on one side and dry land on the other - where it
        /// steps: painting decides cell by cell between the pond's bed and its bank, and the water's edge followed those
        /// steps round the grid in saw-teeth. A cell standing more than 1.5 mm off the average round it eases towards it,
        /// so smooth shores are left alone (and mending one again changes nothing). House pads and the ground under roads
        /// and the railway aren't touched. Returns how many cells moved.
        /// </summary>
        public int SmoothShores(RectInt r, int passes = 3)
        {
            var w = Expand(r, 2);
            int moved = 0;
            for (int pass = 0; pass < passes; pass++)
            {
                CopyRegion(Surface, scratch, Expand(w, 1));
                for (int z = Mathf.Max(1, w.yMin); z < Mathf.Min(Nz - 1, w.yMax); z++)
                for (int x = Mathf.Max(1, w.xMin); x < Mathf.Min(Nx - 1, w.xMax); x++)
                {
                    int i = z * Nx + x;
                    if (Pad[i] > 0.5f || Lock[i] > 0.05f || KeepAt(i) > 0.5f) continue;
                    bool wet = false, dry = false; float sum = 0f, level = -1f;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = i + dz * Nx + dx;
                        if (Water[j] >= 0.5f) { wet = true; level = Mathf.Max(level, WaterLevel[j]); } else dry = true;
                        sum += scratch[j];
                    }
                    if (!wet || !dry) continue;
                    float avg = sum / 9f, s = scratch[i];
                    if (Mathf.Abs(s - avg) < 0.0015f) continue;
                    // (the pond stays a pond - its cells under the water - and the land round it just above the water:
                    // the shoreline moves smoothly between cells, the pond never dries up or spills)
                    float to = Mathf.Lerp(s, avg, 0.6f);
                    to = Water[i] >= 0.5f ? Mathf.Min(to, WaterLevel[i] - 0.001f) : Mathf.Max(to, level + 0.0005f);
                    if (Mathf.Abs(to - s) < 0.0002f) continue;
                    Height[i] = HeightForSurface(i, to);
                    Surface[i] = Height[i] * rim[i];
                    moved++;
                }
            }
            return moved;
        }

        /// <summary>Set for a world from an older save: land coming into the grid is mended (see <see cref="RepairWalls"/>).</summary>
        public bool MendOld;

        /// <summary>
        /// Takes down walls left by an old fault (a stream whose water level was never set carved its banks up to the
        /// land's ceiling): cells in a strip no wider than a few cells standing 10 cm or more above the land round them go
        /// back down to it. Nothing sculpted is ever that sheer: a real mountain's slopes slump.
        /// </summary>
        public int RepairWalls()
        {
            int fixedCells = 0;
            var near = new List<float>(25);
            for (int z = 2; z < Nz - 2; z++)
            for (int x = 2; x < Nx - 2; x++)
            {
                int i = z * Nx + x;
                if (Height[i] < 0.15f) continue;
                near.Clear();
                for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) near.Add(Height[i + dz * Nx + dx]);
                near.Sort();
                float median = near[12];
                // (10 cm above the middle of the land 4 cm round it: no brush makes that - the slopes slump long before)
                if (Height[i] - median < 0.1f) continue;
                Height[i] = median; fixedCells++;
            }
            if (fixedCells > 0) { Despike(All); UpdateSurface(All); Debug.Log($"Wonderland: took down {fixedCells} cells of wall"); }
            return fixedCells;
        }

        /// <summary>A brush raised ground out of a pond (the water's mesh wants rebuilding); cleared by whoever rebuilds it.</summary>
        public bool WaterChanged;

        /// <summary>The most a single cell may stand above (or sink below) all eight of its neighbours.</summary>
        public const float SpikeLimit = 0.003f;

        /// <summary>
        /// Takes out single-cell spikes and pits in a region (and its margin): a cell standing more than
        /// <see cref="SpikeLimit"/> above all eight of its neighbours (or below all of them) is brought back to that. Real
        /// shapes - a ridge, a crag, a channel two or more cells wide - are never touched; neither are house pads or the
        /// ground under roads and the railway. Whatever made the spike (a brush at a pond's edge, a stream's banks, a
        /// drained pond), it doesn't survive the stroke, and nothing is left sticking up that resists flattening.
        /// </summary>
        public void Despike(RectInt r)
        {
            var w = Expand(r, 1);
            for (int z = Mathf.Max(1, w.yMin); z < Mathf.Min(Nz - 1, w.yMax); z++)
            for (int x = Mathf.Max(1, w.xMin); x < Mathf.Min(Nx - 1, w.xMax); x++)
            {
                int i = z * Nx + x;
                if (Pad[i] > 0.5f || KeepAt(i) > 0.5f) continue;
                float lo = float.MaxValue, hi = float.MinValue, h = Height[i] * rim[i];
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int j = i + dz * Nx + dx;
                    float v = Height[j] * rim[j];
                    lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
                }
                if (h > hi + SpikeLimit) Height[i] = HeightForSurface(i, hi + SpikeLimit * 0.5f);
                else if (h < lo - SpikeLimit && Water[i] < 0.5f) Height[i] = HeightForSurface(i, lo - SpikeLimit * 0.5f);
            }
        }
        public float LockAt(float x, float z) => Inside(x, z) ? Bilinear(Lock, x, z) : Endless ? SampleOff(x, z, 5) : 0f;
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
                if (!Inside(p.x, p.z) || !Shown(p.x, p.z)) { prevT = t; prevAbove = true; continue; }
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
            if (!Shown(hit.x, hit.z)) return false;
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
        /// <summary>What can be laid on the ground (<see cref="Pave"/> values): grass is nothing laid.</summary>
        public static readonly string[] Paving = { "Grass", "Paving slabs", "Cobbles", "Asphalt", "Gravel" };

        /// <summary>What the ground is paved with at a local point (0: grass, or nothing known there).</summary>
        public int PaveAt(float x, float z)
        {
            if (!Inside(x, z)) return 0;
            int ix = Mathf.Clamp(Mathf.RoundToInt((x - X0) / Cell), 0, Nx - 1), iz = Mathf.Clamp(Mathf.RoundToInt((z - Z0) / Cell), 0, Nz - 1);
            return Pave[iz * Nx + ix];
        }

        /// <summary>
        /// Lays <paramref name="kind"/> (see <see cref="Paving"/>; 0 takes it up again) on the land within the brush - not
        /// in the water, not past the shore. Returns the cells changed.
        /// </summary>
        public RectInt PaveBrush(Vector2 centre, float radius, byte kind)
        {
            var r = RegionAround(centre, radius);
            float r2 = radius * radius;
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            for (int z = r.yMin; z < r.yMax; z++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                float dx = LocalX(x) - centre.x, dz = LocalZ(z) - centre.y;
                if (dx * dx + dz * dz >= r2) continue;
                int i = z * Nx + x;
                if (Pave[i] == kind || kind != 0 && (Edge[i] < 0.3f || Water[i] > 0.05f)) continue;
                Pave[i] = kind;
                x0 = Mathf.Min(x0, x); z0 = Mathf.Min(z0, z); x1 = Mathf.Max(x1, x); z1 = Mathf.Max(z1, z);
            }
            return x0 > x1 ? new RectInt(0, 0, 0, 0) : new RectInt(x0 - 1, z0 - 1, x1 - x0 + 3, z1 - z0 + 3);
        }

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
                float w = (1f - d2) * (1f - d2) * (1f - Pad[i]) * Mathf.Sqrt(rim[i]) * (1f - KeepAt(i));   // (never under a road or the railway)
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
                // Raised out of a pond: dry land now (else it stood up through the water as a spike that stayed wet).
                if (Water[i] > 0.5f && Height[i] * rim[i] > WaterLevel[i] + 0.002f) { Water[i] = 0f; WaterChanged = true; }
            }
            if (brush == TerrainBrush.Raise || brush == TerrainBrush.Lower) Slump(r, 2);
            if (brush == TerrainBrush.Peaks) Slump(r, 1);
            Despike(r);
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
                float d2 = (dx * dx + dz * dz) / r2 * (1f + RimNoise(i, x, z) * 3f);
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
                    if (Pad[i] > 0.5f || KeepAt(i) > 0.5f) continue;
                    float si = Math.Max(rim[i], 0.15f), h = scratch[i] * rim[i];
                    for (int k = 0; k < 4; k++)
                    {
                        int j = k == 0 ? i - 1 : k == 1 ? i + 1 : k == 2 ? i - Nx : i + Nx;
                        float excess = h - scratch[j] * rim[j] - talus;
                        if (excess <= 0f || Pad[j] > 0.5f || KeepAt(j) > 0.5f) continue;   // (nothing slides onto a road or the railway)
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
        public void CopyFrom(float[] height, float[] land, float[] locks, float[] soil, float[] water = null, float[] waterLevel = null, byte[] pave = null)
        {
            if (pave != null && pave.Length == Pave.Length) Array.Copy(pave, Pave, Pave.Length); else Array.Clear(Pave, 0, Pave.Length);
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
