using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Where villagers can get to on foot. A coarse grid (2.5 cm cells) over the land records which cells are walkable,
    /// which neighbours connect (not through a fence, a stream or across the rails), and which cells lie on a road or
    /// path. From it come two things: regions - the connected areas, so nobody sets off for a café on the far side of a
    /// railway with no crossing - and routes, an A* search that prefers roads and paths and walks round fences, ponds and
    /// buildings to the level crossing or the bridge.
    /// The grid is rebuilt in the background after the world changes, a few milliseconds per frame, keeping the previous
    /// answers until the new ones are complete.
    /// </summary>
    public sealed class WalkGrid
    {
        public const float Cell = 0.025f;
        private const byte Open = 1, East = 2, North = 4, Way = 8, Road = 16, Crossing = 32;
        private const float WayCost = 0.5f, RoadCost = 3.5f;   // paths and pavements are easy; a carriageway is crossed where it must be (a zebra is easy)

        private readonly Func<Vector2, bool> walkable;
        private readonly Func<Vector2, Vector2, bool> barrier;
        private readonly Func<Vector2, int> surface;
        private readonly Func<Rect> bounds;

        // The finished grid (queries) and the one being built.
        private float x0, z0; private int nx, nz;
        private byte[] flags = Array.Empty<byte>();
        private int[] region = Array.Empty<int>();
        private float bx0, bz0; private int bnx, bnz, cursor = -1;
        private byte[] building;
        private bool dirty = true;
        private int quiet;                      // frames since the last change (a brush stroke changes the world every frame)
        private readonly Stopwatch watch = new();

        /// <summary>Bumped whenever a rebuild completes (caches keyed on it refresh).</summary>
        public int Version { get; private set; }
        public bool Ready => Version > 0;
        public bool Busy => dirty || cursor >= 0;
        public int Regions { get; private set; }
        public int Cells => nx * nz;
        public float LastBuildMs { get; private set; }
        private float buildMs;

        /// <param name="surface">What a point is underfoot: 0 nothing, 1 a path or pavement, 2 a carriageway, 3 a zebra crossing.</param>
        public WalkGrid(Func<Vector2, bool> walkable, Func<Vector2, Vector2, bool> barrier, Func<Vector2, int> surface, Func<Rect> bounds)
        {
            this.walkable = walkable; this.barrier = barrier; this.surface = surface; this.bounds = bounds;
        }

        /// <summary>The world changed: start again (a rebuild already under way restarts).</summary>
        public void Invalidate() { dirty = true; quiet = 0; }

        /// <summary>Advances the rebuild for at most <paramref name="milliseconds"/> (it starts once the world has been still for a few frames).</summary>
        public void Update(float milliseconds)
        {
            if (dirty && ++quiet < 6) return;
            if (dirty) Begin();
            if (cursor < 0) return;
            watch.Restart();
            int count = bnx * bnz;
            while (cursor < count)
            {
                Classify(cursor++);
                if ((cursor & 31) == 0 && watch.Elapsed.TotalMilliseconds > milliseconds) break;
            }
            buildMs += (float)watch.Elapsed.TotalMilliseconds;
            if (cursor >= count) Finish();
        }

        /// <summary>Builds the whole grid now (tests, and the first frame after loading).</summary>
        public void Complete()
        {
            if (dirty) Begin();
            while (cursor >= 0) Update(1000f);
        }

        private void Begin()
        {
            dirty = false;
            var b = bounds();
            if (b.width <= 0f || b.height <= 0f) b = new Rect(-0.5f, -0.5f, 1f, 1f);
            bx0 = Mathf.Floor(b.xMin / Cell) * Cell; bz0 = Mathf.Floor(b.yMin / Cell) * Cell;
            bnx = Mathf.Max(2, Mathf.CeilToInt((b.xMax - bx0) / Cell) + 1); bnz = Mathf.Max(2, Mathf.CeilToInt((b.yMax - bz0) / Cell) + 1);
            if (building == null || building.Length != bnx * bnz) building = new byte[bnx * bnz];
            else Array.Clear(building, 0, building.Length);
            cursor = 0; buildMs = 0f;
        }

        private Vector2 CentreOf(float ox, float oz, int x, int z) => new(ox + (x + 0.5f) * Cell, oz + (z + 0.5f) * Cell);

        private void Classify(int i)
        {
            int x = i % bnx, z = i / bnx;
            var c = CentreOf(bx0, bz0, x, z);
            if (!walkable(c)) return;
            byte f = Open;
            int s = surface(c);
            if (s == 1) f |= Way; else if (s == 2) f |= Road; else if (s == 3) f |= Crossing | Way;
            // A link to the east / north neighbour when the halfway point is walkable and no fence is in between; the
            // neighbour's own centre is checked when it is classified (both must be open for the link to count).
            if (x + 1 < bnx && walkable(c + new Vector2(Cell * 0.5f, 0f)) && !barrier(c, c + new Vector2(Cell, 0f))) f |= East;
            if (z + 1 < bnz && walkable(c + new Vector2(0f, Cell * 0.5f)) && !barrier(c, c + new Vector2(0f, Cell))) f |= North;
            building[i] = f;
        }

        private void Finish()
        {
            cursor = -1;
            x0 = bx0; z0 = bz0; nx = bnx; nz = bnz;
            (flags, building) = (building, flags);   // the old answers' buffer is reused for the next rebuild
            // Links need both ends open.
            for (int i = 0; i < flags.Length; i++)
            {
                if ((flags[i] & East) != 0 && (flags[i + 1] & Open) == 0) flags[i] &= unchecked((byte)~East);
                if ((flags[i] & North) != 0 && (i + nx >= flags.Length || (flags[i + nx] & Open) == 0)) flags[i] &= unchecked((byte)~North);
            }
            // Regions: flood fill over the links.
            if (region.Length != flags.Length) region = new int[flags.Length]; else Array.Clear(region, 0, region.Length);
            int label = 0;
            var stack = new Stack<int>();
            for (int s = 0; s < flags.Length; s++)
            {
                if ((flags[s] & Open) == 0 || region[s] != 0) continue;
                label++;
                region[s] = label; stack.Push(s);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    foreach (int n in Links(c))
                        if (region[n] == 0) { region[n] = label; stack.Push(n); }
                }
            }
            Regions = label;
            if (g.Length != flags.Length) { g = new float[flags.Length]; came = new int[flags.Length]; seen = new int[flags.Length]; stamp = 0; }
            Version++;
            LastBuildMs = buildMs;
        }

        // Orthogonal neighbours over open links.
        private IEnumerable<int> Links(int c)
        {
            if ((flags[c] & East) != 0) yield return c + 1;
            if ((flags[c] & North) != 0) yield return c + nx;
            if (c % nx > 0 && (flags[c - 1] & East) != 0) yield return c - 1;
            if (c >= nx && (flags[c - nx] & North) != 0) yield return c - nx;
        }

        private bool Linked(int a, int b)
        {
            if (b == a + 1) return (flags[a] & East) != 0;
            if (b == a - 1) return (flags[b] & East) != 0;
            if (b == a + nx) return (flags[a] & North) != 0;
            if (b == a - nx) return (flags[b] & North) != 0;
            return false;
        }

        private int CellAt(Vector2 p)
        {
            int x = Mathf.FloorToInt((p.x - x0) / Cell), z = Mathf.FloorToInt((p.y - z0) / Cell);
            return x < 0 || z < 0 || x >= nx || z >= nz ? -1 : z * nx + x;
        }

        private Vector2 Centre(int i) => CentreOf(x0, z0, i % nx, i / nx);

        /// <summary>
        /// The open cell for a point: its own, or the nearest open one within two cells (doorsteps and seats sit right
        /// against walls). -1 when the point is nowhere near walkable ground.
        /// </summary>
        private int OpenCellNear(Vector2 p)
        {
            int c = CellAt(p);
            if (c >= 0 && (flags[c] & Open) != 0) return c;
            int best = -1; float bestD = float.MaxValue;
            int cx = Mathf.FloorToInt((p.x - x0) / Cell), cz = Mathf.FloorToInt((p.y - z0) / Cell);
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                int i = z * nx + x;
                if ((flags[i] & Open) == 0) continue;
                float d = (Centre(i) - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Open ground for certain: the point's cell is open and linked to all four neighbours (no wall, water, rail
        /// or fence anywhere near), and the grid is up to date. Steering asks this first and only does the exact check
        /// near obstacles. False whenever in doubt.
        /// </summary>
        public bool Interior(Vector2 p)
        {
            if (Version == 0 || dirty || cursor >= 0) return false;
            int x = Mathf.FloorToInt((p.x - x0) / Cell), z = Mathf.FloorToInt((p.y - z0) / Cell);
            if (x < 1 || z < 1 || x >= nx - 1 || z >= nz - 1) return false;
            int c = z * nx + x;
            const byte all = Open | East | North;
            return (flags[c] & all) == all && (flags[c - 1] & East) != 0 && (flags[c - nx] & North) != 0;
        }

        /// <summary>The connected area a point belongs to (0: not walkable; -1: not known yet, assume reachable).</summary>
        public int RegionAt(Vector2 p)
        {
            if (!Ready) return -1;
            int c = OpenCellNear(p);
            return c < 0 ? 0 : region[c];
        }

        /// <summary>Whether someone at <paramref name="a"/> can walk to <paramref name="b"/>.</summary>
        public bool Reachable(Vector2 a, Vector2 b)
        {
            int ra = RegionAt(a), rb = RegionAt(b);
            if (ra < 0 || rb < 0) return true;
            return ra != 0 && ra == rb;
        }

        public bool SameRegion(int ra, int rb) => ra < 0 || rb < 0 || (ra != 0 && ra == rb);

        // ------------------------------------------------------------------ routes

        private float[] g = Array.Empty<float>();
        private int[] came = Array.Empty<int>(), seen = Array.Empty<int>();
        private int stamp;
        private readonly List<(float f, int cell)> heap = new();
        private readonly List<int> cells = new();
        public int LastExpanded { get; private set; }

        private float CostOf(int c) => (flags[c] & Way) != 0 ? WayCost : (flags[c] & Road) != 0 ? RoadCost : 1f;

        /// <summary>
        /// A walking route (waypoints after <paramref name="from"/>, ending at <paramref name="to"/>), or null when there
        /// is none or the grid is not ready. Prefers roads and paths; corners are cut wherever the straight line is clear
        /// and no dearer than the way round.
        /// </summary>
        public List<Vector2> Route(Vector2 from, Vector2 to, int maxExpand = 40000)
        {
            if (!Ready) return null;
            int a = OpenCellNear(from), z = OpenCellNear(to);
            if (a < 0 || z < 0 || region[a] != region[z]) return null;
            if (a == z) return new List<Vector2> { to };
            if (++stamp == int.MaxValue) { Array.Clear(seen, 0, seen.Length); stamp = 1; }
            heap.Clear();
            var goal = Centre(z);
            g[a] = 0f; came[a] = -1; seen[a] = stamp;
            Push(Heuristic(a, goal), a);
            int expanded = 0; bool found = false;
            while (heap.Count > 0 && expanded < maxExpand)
            {
                var (f, cur) = Pop();
                if (f > g[cur] + Heuristic(cur, goal) + 1e-5f) continue;   // stale entry
                if (cur == z) { found = true; break; }
                expanded++;
                foreach (int n in Links(cur)) Relax(cur, n, Cell * 0.5f * (CostOf(cur) + CostOf(n)), goal);
                // Diagonals: only when both ways round the corner are open.
                int x = cur % nx;
                foreach (var (dx, dz) in Diagonals)
                {
                    if (x + dx < 0 || x + dx >= nx) continue;
                    int side = cur + dx, up = cur + dz * nx, n = up + dx;
                    if (n < 0 || n >= flags.Length || up < 0 || up >= flags.Length) continue;
                    if (!Linked(cur, side) || !Linked(side, n) || !Linked(cur, up) || !Linked(up, n)) continue;
                    Relax(cur, n, Cell * 0.7071f * (CostOf(cur) + CostOf(n)), goal);
                }
            }
            LastExpanded = expanded;
            if (!found) return null;
            cells.Clear();
            for (int c = z; c >= 0; c = came[c]) cells.Add(c);
            cells.Reverse();
            return Smooth(from, to);
        }

        private static readonly (int, int)[] Diagonals = { (1, 1), (-1, 1), (1, -1), (-1, -1) };

        private float Heuristic(int c, Vector2 goal)
        {
            var d = Centre(c) - goal;
            float ax = Mathf.Abs(d.x), az = Mathf.Abs(d.y);
            return (Mathf.Max(ax, az) + 0.4142f * Mathf.Min(ax, az)) * WayCost;
        }

        private void Relax(int cur, int n, float step, Vector2 goal)
        {
            float cost = g[cur] + step;
            if (seen[n] == stamp && g[n] <= cost) return;
            seen[n] = stamp; g[n] = cost; came[n] = cur;
            Push(cost + Heuristic(n, goal), n);
        }

        private void Push(float f, int cell)
        {
            heap.Add((f, cell));
            int i = heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (heap[p].f <= heap[i].f) break;
                (heap[p], heap[i]) = (heap[i], heap[p]); i = p;
            }
        }

        private (float f, int cell) Pop()
        {
            var top = heap[0];
            heap[0] = heap[^1]; heap.RemoveAt(heap.Count - 1);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < heap.Count && heap[l].f < heap[m].f) m = l;
                if (r < heap.Count && heap[r].f < heap[m].f) m = r;
                if (m == i) break;
                (heap[m], heap[i]) = (heap[i], heap[m]); i = m;
            }
            return top;
        }

        /// <summary>
        /// String-pulls the cell path: from each anchor, jumps to the furthest later cell whose straight line crosses
        /// only linked cells and costs no more than following the path (so walkers still keep to the road).
        /// </summary>
        private List<Vector2> Smooth(Vector2 from, Vector2 to)
        {
            var result = new List<Vector2>();
            int anchor = 0;
            while (anchor < cells.Count - 1)
            {
                int next = anchor + 1;
                for (int k = Mathf.Min(cells.Count - 1, anchor + 40); k > anchor + 1; k--)
                    if (Clear(cells[anchor], cells[k], g[cells[k]] - g[cells[anchor]])) { next = k; break; }
                if (next < cells.Count - 1) result.Add(Centre(cells[next]));
                anchor = next;
            }
            result.Add(to);
            return result;
        }

        /// <summary>A 4-connected walk along the straight line between two cells over open links, within a cost.</summary>
        private bool Clear(int a, int b, float pathCost)
        {
            var p = Centre(a); var q = Centre(b);
            int x = a % nx, z = a / nx, tx = b % nx, tz = b / nx;
            var d = q - p;
            int sx = Math.Sign(d.x), sz = Math.Sign(d.y);
            float tdx = sx != 0 ? Cell / Mathf.Abs(d.x) : float.MaxValue, tdz = sz != 0 ? Cell / Mathf.Abs(d.y) : float.MaxValue;
            float tmx = tdx * 0.5f, tmz = tdz * 0.5f;
            float cost = 0f, len = d.magnitude, before = 0f;
            int cur = a, guard = 0;
            while ((x != tx || z != tz) && guard++ < 200)
            {
                int n;
                float t;
                if (tmx < tmz) { t = tmx; tmx += tdx; x += sx; n = cur + sx; }
                else { t = tmz; tmz += tdz; z += sz; n = cur + sz * nx; }
                if (x < 0 || z < 0 || x >= nx || z >= nz || !Linked(cur, n)) return false;
                cost += CostOf(cur) * (Mathf.Min(t, 1f) - before) * len;   // the stretch of the line inside this cell
                before = Mathf.Min(t, 1f);
                cur = n;
            }
            cost += CostOf(cur) * (1f - before) * len;
            return cost <= pathCost * 1.05f + Cell * 0.5f;
        }
    }
}
