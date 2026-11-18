using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Track on the grid (<see cref="BuildGrid.Snap"/>): a stroke becomes standard track, like a model railway's
    /// sectional track but without having to pick pieces. Straights run along the grid, across it or at 45 degrees,
    /// through its points; curves between them have a standard radius (<see cref="GridRadii"/>: 20 cm to 1 m in 10 cm
    /// steps), chosen to follow what was drawn - a round loop comes out round, a square one with tight corners. A line
    /// drawn beside a straight track keeps the standard distance from it (<see cref="ParallelSpacing"/>), and its
    /// curves go round the other track's concentrically. A branch leaves a straight track like a real switch: the
    /// switch slides along the track to where the branch's curve starts, so the branch leaves it tangentially, and a
    /// short sideways step (a crossover to the next track) becomes a reverse curve. Of a few readings of the stroke the
    /// simplest that stays close to it wins.
    /// </summary>
    public static partial class TrackPlanner
    {
        /// <summary>The standard curve radii on the grid: ten centimetres apart, the distance between parallel tracks.</summary>
        public static readonly float[] GridRadii = { 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 1f };
        /// <summary>The radii the reading in progress uses: the railway's, or a road's (see <see cref="GridShapes"/>).</summary>
        private static float[] Radii = GridRadii;
        /// <summary>Parallel tracks on the grid: two grid squares apart (three diagonal grid lines on a diagonal), or twice that.</summary>
        public const float ParallelSpacing = 0.1f;

        private sealed class GridLine
        {
            public Vector2 P, H;        // a point on it and its heading (the way the stroke runs)
            public Vector2 Enter, Exit; // about where the stroke joins and leaves it
            public int Attach;          // 1: the start attachment's line, 2: the finish's, 3: both (a straight between them)
            public bool Beside;         // kept at the standard distance (or twice it) from a straight track alongside
            public float BesideOffset;  // ...that distance
            public float SRadius;       // > 0: the middle of a reverse curve; both its corners take this radius
            public float SOffset, SSide; public Vector2 SBase;   // the reverse curve's sideways step, which side, the outer lines' heading
            public float Cap = float.MaxValue;   // the largest radius for the corner after this line
            public bool Bridged;        // an attachment's line that already had a line put in after it
            public bool Reseated;       // a line between two fixed ends, already placed where both its curves fit
            public Vector2 NearAt = new(float.NaN, 0f); public float Near;   // (cached: how near the stroke passes the corner after it)
            public GridLine Copy() => (GridLine)MemberwiseClone();
        }

        public sealed class GridShape
        {
            public List<Vector2> Path;
            public TrackAttach Start, Finish;
            public float Score;
            public string Error;
        }

        /// <summary>Diagnostics: when set, each reading of a stroke is described here (its lines, curves and outcome).</summary>
        public static System.Text.StringBuilder GridLog;

        private static readonly float Half = Mathf.Sqrt(0.5f);
        private static Vector2 Dir8(int d) => (d & 7) switch
        {
            0 => new Vector2(1f, 0f), 1 => new Vector2(Half, Half), 2 => new Vector2(0f, 1f), 3 => new Vector2(-Half, Half),
            4 => new Vector2(-1f, 0f), 5 => new Vector2(-Half, -Half), 6 => new Vector2(0f, -1f), _ => new Vector2(Half, -Half),
        };

        private static Vector2 Left(Vector2 h) => new(-h.y, h.x);
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static Vector2 Rotate(Vector2 v, float radians) { float c = Mathf.Cos(radians), s = Mathf.Sin(radians); return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }
        private static bool Octilinear(Vector2 h) => Mathf.Abs(h.x) < 1e-4f || Mathf.Abs(h.y) < 1e-4f || Mathf.Abs(Mathf.Abs(h.x) - Mathf.Abs(h.y)) < 1e-4f;
        private static bool Diagonal(Vector2 h) => Mathf.Abs(h.x) > 0.3f && Mathf.Abs(h.y) > 0.3f;
        /// <summary>How far apart neighbouring grid lines of this heading are.</summary>
        private static float LineSpacing(Vector2 h) => Diagonal(h) ? BuildGrid.Size * Half : BuildGrid.Size;
        /// <summary>Distance between parallel tracks of this heading: the first grid line at least <see cref="ParallelSpacing"/> away.</summary>
        public static float BesideSpacing(Vector2 h) { float l = LineSpacing(h); return Mathf.Ceil(ParallelSpacing / l - 0.01f) * l; }
        private static float Along(GridLine l, Vector2 p) => Vector2.Dot(p - l.P, l.H);
        private static Vector2 OnLine(GridLine l, Vector2 p) => l.P + l.H * Along(l, p);

        private static bool Intersect(GridLine a, GridLine b, out Vector2 x)
        {
            float c = Cross(a.H, b.H);
            x = default;
            if (Mathf.Abs(c) < 1e-6f) return false;
            x = a.P + a.H * (Cross(b.P - a.P, b.H) / c);
            return true;
        }

        /// <summary>Moves an octilinear line sideways onto the nearest grid line of its heading.</summary>
        private static void SnapLine(GridLine l)
        {
            if (!Octilinear(l.H)) return;
            var n = Left(l.H);
            float sp = LineSpacing(l.H), o = Vector2.Dot(l.P, n);
            l.P += n * (Mathf.Round(o / sp) * sp - o);
        }

        /// <summary>The grid point on a grid line nearest a point (or just its projection, off the grid).</summary>
        private static Vector2 GridPointOn(GridLine l, Vector2 p)
        {
            var q = OnLine(l, p);
            var g = BuildGrid.Point(q);
            return (OnLine(l, g) - g).sqrMagnitude < 1e-8f ? g : q;
        }

        /// <summary>
        /// Every reading of a stroke as standard track, best first (those that can't be built at all last, with why).
        /// <paramref name="stroke"/> is the drawn line (a loop's closed part when <paramref name="closed"/>); the
        /// attachments say where it meets the railway. A switch in the middle of a straight track slides along it.
        /// <paramref name="radii"/>: the standard curves (smallest first), the railway's by default; roads use their own,
        /// with no network and their joins as fixed ends.
        /// </summary>
        public static List<GridShape> GridShapes(List<Vector2> stroke, bool closed, TrackNetwork network, TrackAttach start, TrackAttach finish, float[] radii = null)
        {
            Radii = radii ?? GridRadii;
            try { return Shapes(stroke, closed, network, start, finish); }
            finally { Radii = GridRadii; }
        }

        private static List<GridShape> Shapes(List<Vector2> stroke, bool closed, TrackNetwork network, TrackAttach start, TrackAttach finish)
        {
            var results = Readings(stroke, closed, network, start, finish);
            if (results.Count > 0 && results[0].Error == null) return results;
            // Nothing fits with a switch on a curve: try it on the nearest straight instead, as a railway builder would.
            var s2 = start; var f2 = finish;
            bool moved = OntoStraight(network, ref s2) | OntoStraight(network, ref f2);
            if (!moved) return results;
            var more = Readings(stroke, closed, network, s2, f2);
            if (more.Count > 0 && more[0].Error == null) return more;
            return results;
        }

        /// <summary>A switch on a curve moved along its track to the nearest straight within 35 cm (false if it is on one, or none is near).</summary>
        private static bool OntoStraight(TrackNetwork network, ref TrackAttach a)
        {
            if (a.Kind != AttachKind.Mid || StraightAt(network, a.Edge, a.S)) return false;
            var l = network.Edges[a.Edge].Layout;
            l.Evaluate(a.S, out var t0);
            float sign = Vector2.Dot(a.Dir, new Vector2(t0.x, t0.z)) >= 0f ? 1f : -1f;
            for (float d = 0.01f; d <= 0.35f; d += 0.01f)
                foreach (float s in new[] { a.S - d, a.S + d })
                {
                    if (!l.Closed && (s < MinJointGap || s > l.Length - MinJointGap)) continue;
                    if (!StraightAt(network, a.Edge, l.Wrap(s))) continue;
                    var p = l.Evaluate(l.Wrap(s), out var t);
                    a.S = l.Wrap(s); a.Point = new Vector2(p.x, p.z); a.Dir = new Vector2(t.x, t.z).normalized * sign;
                    return true;
                }
            return false;
        }

        private static List<GridShape> Readings(List<Vector2> stroke, bool closed, TrackNetwork network, TrackAttach start, TrackAttach finish)
        {
            var results = new List<GridShape>();
            bool attached = start.Kind != AttachKind.None || finish.Kind != AttachKind.None;
            var p = Steady(stroke, closed);
            if (!attached && p.Count >= 2) p = RemoveSmallLoops(Polyline.Resample(p, Coarse, closed), closed);   // (on a coarse copy: quicker)
            p = p.Count >= 2 ? Polyline.Resample(p, Spacing, closed) : p;
            if (p.Count < 3) { results.Add(new GridShape { Error = "Draw a longer track" }); return results; }
            var dirs = Headings(p, closed);
            bool slideStart = start.Kind == AttachKind.Mid && StraightAt(network, start.Edge, start.S);
            bool slideFinish = finish.Kind == AttachKind.Mid && StraightAt(network, finish.Edge, finish.S);
            // A branch drawn square off a track could leave it either way: try both.
            var startDirs = Directions(network, start, p, true);
            var finishDirs = Directions(network, finish, p, false);
            // ...but on a railway that runs one way, not the way that would make trains turn round (if the other works).
            bool Runs(Vector2 sd, Vector2 fd) => !TurnsBack(network, start, sd, finish, fd);
            bool anyRuns = false;
            foreach (var sd in startDirs) foreach (var fd in finishDirs) anyRuns |= Runs(sd, fd);
            // A switch that can't slide far enough along its straight stays where the stroke began.
            var slides = new List<(bool, bool)> { (slideStart, slideFinish) };
            if (slideStart || slideFinish) slides.Add((false, false));
            var seen = new HashSet<string>();
            foreach (var (minRun, split, merge) in new[] { (0.05f, false, false), (0.09f, false, false), (0.15f, false, false), (0.05f, true, false),
                         (0.09f, true, false), (0.25f, false, false), (0.09f, false, true) })
            {
                var runs = RunLines(p, dirs, closed, minRun, split);
                foreach (var sd in startDirs)
                foreach (var fd in finishDirs)
                {
                    if (anyRuns && !Runs(sd, fd)) continue;
                    var s = start; s.Dir = sd; var f = finish; f.Dir = fd;
                    var lines = new List<GridLine>();
                    foreach (var r in runs) lines.Add(r.Copy());
                    Assemble(lines, p, closed, s, f, network);
                    if (merge) MergeCorners(lines, closed);
                    string key = Key(lines);
                    foreach (var (ss, sf) in slides)
                    {
                        if (!seen.Add(key + ss + sf)) continue;   // (the same lines as another reading)
                        var copy = lines.ConvertAll(l => l.Copy());
                        results.Add(Realize(copy, closed, p, network, s, f, ss, sf));
                    }
                }
            }
            results.Sort((a, b) => a.Error == null != (b.Error == null) ? (a.Error == null ? -1 : 1) : a.Score.CompareTo(b.Score));
            return results;
        }

        /// <summary>
        /// Plans grid track: the best reading of the stroke that can be built (crossings at a decent angle, room beside
        /// other track, a loop big enough), or the best reading's reason why not. A reading that strays much further from
        /// the stroke than the best one is not built instead: the track goes where it was drawn, or not at all.
        /// </summary>
        private static List<Vector2> GridPlan(TrackPlan plan, List<Vector2> path, bool closed, TerrainField terrain, TrackNetwork network)
        {
            var shapes = GridShapes(path, closed, network, plan.Start, plan.Finish);
            GridShape shown = null, chosen = null;
            string error = null;
            foreach (var g in shapes)
            {
                if (g.Error != null) { error ??= g.Error; break; }   // (sorted: those that can't be built come last)
                if (shown != null && g.Score > shown.Score + 0.03f) break;
                shown ??= g;
                var trial = new TrackPlan { Start = g.Start, Finish = g.Finish };
                string why = closed && Polyline.Perimeter(g.Path, true) < MinLoopLength ? "Make the loop a little bigger"
                    : CheckCrossings(Every(g.Path, 2, closed), closed, network, trial) ?? CheckClearance(g.Path, closed, network, trial);
                if (why == null) { chosen = g; break; }
                error ??= why;
            }
            var feedback = chosen ?? shown;
            plan.Smoothed = feedback != null ? Sparse(feedback.Path, terrain) : path.ConvertAll(p => terrain.PointAt(p.x, p.y) + Vector3.up * 0.006f);
            if (chosen == null) { plan.Error = error ?? "Draw a longer track"; return null; }
            plan.Start = chosen.Start; plan.Finish = chosen.Finish;
            return chosen.Path;
        }

        /// <summary>Bending a piece on the grid: <paramref name="bent"/> refitted as grid track between its switches.</summary>
        private static string GridReshape(TrackNetwork network, int edge, List<Vector2> bent, out List<Vector2> dense, out List<Vector3> preview, TerrainField terrain)
        {
            dense = null;
            var e = network.Edges[edge];
            var l = e.Layout;
            var others = network.Clone();
            others.RemoveEdge(edge);
            others.Reindex();
            var plan = new TrackPlan();
            if (!l.Closed && e.Joint[0] >= 0)
                plan.Start = new TrackAttach { Kind = AttachKind.End, Point = Flat(l.Points[0]), Dir = Flat(network.IntoEdge(new EdgeEnd(edge, 0))).normalized };
            if (!l.Closed && e.Joint[1] >= 0)
                plan.Finish = new TrackAttach { Kind = AttachKind.End, Point = Flat(l.Points[^1]), Dir = -Flat(network.IntoEdge(new EdgeEnd(edge, 1))).normalized };
            var line = GridPlan(plan, bent, l.Closed, terrain, others);
            preview = plan.Smoothed;
            if (line == null) return plan.Error;
            foreach (var p in line) if (terrain.EdgeAt(p.x, p.y) < 0.4f) return "Keep the track on the land";
            // The ends meeting switches stay exactly where they were.
            if (plan.Start.Kind != AttachKind.None) line[0] = Flat(l.Points[0]);
            if (plan.Finish.Kind != AttachKind.None) line[^1] = Flat(l.Points[^1]);
            dense = line;
            return null;
        }

        /// <summary>Every k-th point (and the last): straights and standard curves lose nothing a crossing check needs.</summary>
        private static List<Vector2> Every(List<Vector2> path, int k, bool closed)
        {
            var r = new List<Vector2>(path.Count / k + 2);
            for (int i = 0; i < path.Count; i += k) r.Add(path[i]);
            if (!closed && (path.Count - 1) % k != 0) r.Add(path[^1]);
            return r;
        }

        private static List<Vector3> Sparse(List<Vector2> path, TerrainField terrain)
        {
            var r = new List<Vector3>();
            for (int i = 0; i < path.Count; i += 3) r.Add(terrain.PointAt(path[i].x, path[i].y) + Vector3.up * 0.006f);
            if ((path.Count - 1) % 3 != 0) r.Add(terrain.PointAt(path[^1].x, path[^1].y) + Vector3.up * 0.006f);
            return r;
        }

        /// <summary>
        /// On a railway that runs one way: would a new piece leaving <paramref name="start"/> along <paramref name="sd"/> and
        /// arriving at <paramref name="finish"/> along <paramref name="fd"/> have to run both ways at once (each end
        /// wanting it the other way round, on one connected railway)? False when either end is free or the railway is two-way.
        /// </summary>
        private static bool TurnsBack(TrackNetwork network, TrackAttach start, Vector2 sd, TrackAttach finish, Vector2 fd)
        {
            if (network == null || start.Kind == AttachKind.None || finish.Kind == AttachKind.None) return false;
            int a = Wants(network, start, sd, true), b = Wants(network, finish, fd, false);
            if (a == 0 || b == 0 || a == b) return false;
            int ea = start.Kind == AttachKind.End ? start.End.Edge : start.Edge, eb = finish.Kind == AttachKind.End ? finish.End.Edge : finish.Edge;
            return Connected(network, ea, eb);
        }

        /// <summary>Which way the railway at an attachment wants the new piece to run: +1 from its start to its finish, -1 back, 0 either.</summary>
        private static int Wants(TrackNetwork network, TrackAttach a, Vector2 dir, bool start)
        {
            if (a.Kind == AttachKind.End)
            {
                if (!network.Directed(a.End.Edge)) return 0;
                // Trains arrive at a piece's end 1 and leave from its end 0.
                bool leavesHere = a.End.End == 1;
                return start ? (leavesHere ? 1 : -1) : (leavesHere ? -1 : 1);
            }
            if (a.Kind != AttachKind.Mid || !network.Directed(a.Edge)) return 0;
            network.Edges[a.Edge].Layout.Evaluate(a.S, out var t);
            return Vector2.Dot(dir, new Vector2(t.x, t.z)) >= 0f ? 1 : -1;
        }

        private static bool Connected(TrackNetwork network, int from, int to)
        {
            if (from == to) return true;
            var seen = new HashSet<int> { from };
            var queue = new Queue<int>(); queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int e = queue.Dequeue();
                foreach (int ji in network.Edges[e].Joint)
                {
                    if (ji < 0) continue;
                    foreach (var x in network.Joints[ji].Ends) if (seen.Add(x.Edge)) { if (x.Edge == to) return true; queue.Enqueue(x.Edge); }
                }
            }
            return false;
        }

        private static string Key(List<GridLine> lines)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in lines)
                sb.Append(Mathf.RoundToInt(Mathf.Atan2(l.H.y, l.H.x) * 573f)).Append(',').Append(Mathf.RoundToInt(Vector2.Dot(l.P, Left(l.H)) * 2000f)).Append(',').Append(l.Attach).Append(';');
            return sb.ToString();
        }

        private static List<Vector2> Directions(TrackNetwork network, TrackAttach a, List<Vector2> p, bool start)
        {
            var dirs = new List<Vector2> { a.Dir.normalized };
            if (a.Kind != AttachKind.Mid) return dirs;
            // The stroke's way over its first (last) 12 cm, against the track's.
            var from = start ? p[0] : p[^1];
            var to = from;
            for (int k = 0; k < p.Count; k++) { var q = start ? p[k] : p[p.Count - 1 - k]; if ((q - from).magnitude > 0.12f) { to = q; break; } }
            var d = start ? to - from : from - to;
            if (d.sqrMagnitude > 1e-6f && Mathf.Abs(Vector2.Dot(d.normalized, a.Dir.normalized)) < 0.6f) dirs.Add(-a.Dir.normalized);
            return dirs;
        }

        /// <summary>Is the piece straight around this point (a new switch can slide along it)?</summary>
        private static bool StraightAt(TrackNetwork network, int edge, float s, float reach = 0.04f)
        {
            if (network == null || edge < 0 || edge >= network.Edges.Count) return false;
            var l = network.Edges[edge].Layout;
            var c = l.Evaluate(s, out var t);
            var h = new Vector2(t.x, t.z).normalized; var n = Left(h);
            for (float o = -reach; o <= reach + 1e-4f; o += 0.01f)
            {
                if (!l.Closed && (s + o < 0f || s + o > l.Length)) return false;
                var q = l.Evaluate(s + o, out _);
                if (Mathf.Abs(Vector2.Dot(new Vector2(q.x - c.x, q.z - c.z), n)) > 0.0012f) return false;
            }
            return true;
        }

        /// <summary>
        /// The stroke as straight lines, one per stretch running in one of the eight grid directions: a stretch shorter
        /// than <paramref name="minRun"/> joins the neighbour nearer its heading. With <paramref name="split"/>, a stretch
        /// drawn between two directions (a long line at 20 degrees) becomes a straight and a diagonal, keeping both ends.
        /// </summary>
        /// <summary>The grid direction (0-7) the stroke runs in at each point, over 8 cm, with a little hysteresis.</summary>
        private static int[] Headings(List<Vector2> p, bool closed)
        {
            int n = p.Count;
            var dir = new int[n];
            const int k = 4;
            int cur = -1;
            for (int i = 0; i < n; i++)
            {
                var a = closed ? p[(i - k + n) % n] : p[Mathf.Max(0, i - k)];
                var b = closed ? p[(i + k) % n] : p[Mathf.Min(n - 1, i + k)];
                var d = b - a;
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                int q = ((Mathf.RoundToInt(ang / 45f) % 8) + 8) % 8;
                if (cur < 0 || Mathf.Abs(Mathf.DeltaAngle(ang, cur * 45f)) > 22.5f + 8f) cur = q;
                dir[i] = cur;
            }
            return dir;
        }

        private static List<GridLine> RunLines(List<Vector2> p, int[] dir, bool closed, float minRun, bool split)
        {
            int n = p.Count;
            // Runs of one direction (on a loop, starting where a run starts).
            int first = 0;
            if (closed) { while (first < n && dir[first] == dir[(first - 1 + n) % n]) first++; if (first == n) first = 0; }
            var runs = new List<(int start, int count, int dir)>();
            for (int c = 0; c < n; c++)
            {
                int i = (first + c) % n;
                if (runs.Count > 0 && runs[^1].dir == dir[i]) { var r = runs[^1]; r.count++; runs[^1] = r; }
                else runs.Add((i, 1, dir[i]));
            }
            // Short runs join a neighbour.
            int minPoints = Mathf.Max(2, Mathf.RoundToInt(minRun / Spacing));
            int keep = closed ? 3 : 1;
            for (int guard = 0; guard < 400 && runs.Count > keep; guard++)
            {
                int shortest = -1;
                for (int r = 0; r < runs.Count; r++) if (runs[r].count < minPoints && (shortest < 0 || runs[r].count < runs[shortest].count)) shortest = r;
                if (shortest < 0) break;
                var run = runs[shortest];
                int prev = shortest - 1, next = shortest + 1;
                if (closed) { prev = (prev + runs.Count) % runs.Count; next %= runs.Count; }
                var chord = p[(run.start + run.count - 1) % n] - p[run.start];
                float ang = chord.sqrMagnitude > 1e-8f ? Mathf.Atan2(chord.y, chord.x) * Mathf.Rad2Deg : run.dir * 45f;
                float Off(int r) => r < 0 || r >= runs.Count ? float.MaxValue : Mathf.Abs(Mathf.DeltaAngle(ang, runs[r].dir * 45f)) - runs[r].count * 1e-4f;
                int into = Off(prev) <= Off(next) ? prev : next;
                if (into == prev) { var r = runs[prev]; r.count += run.count; runs[prev] = r; runs.RemoveAt(shortest); }
                else { var r = runs[next]; r.start = run.start; r.count += run.count; runs[next] = r; runs.RemoveAt(shortest); }
                // Neighbours now of one direction become one run.
                for (int r = runs.Count - 1; r >= 1; r--)
                    if (runs[r].dir == runs[r - 1].dir) { var m = runs[r - 1]; m.count += runs[r].count; runs[r - 1] = m; runs.RemoveAt(r); }
                if (closed && runs.Count > 1 && runs[0].dir == runs[^1].dir) { var m = runs[^1]; m.count += runs[0].count; runs[^1] = m; runs.RemoveAt(0); }
            }
            var lines = new List<GridLine>();
            foreach (var run in runs)
            {
                var idx = new List<int>(run.count);
                for (int c = 0; c < run.count; c++) idx.Add((run.start + c) % n);
                if (split && run.count * Spacing > 0.12f && SplitRun(p, idx, run.dir, lines)) continue;
                lines.Add(FitLine(p, idx, 0, idx.Count, run.dir));
            }
            return lines;
        }

        private static GridLine FitLine(List<Vector2> p, List<int> idx, int from, int to, int dir)
        {
            var h = Dir8(dir); var nrm = Left(h);
            float o = 0f, a = 0f;
            for (int i = from; i < to; i++) { o += Vector2.Dot(p[idx[i]], nrm); a += Vector2.Dot(p[idx[i]], h); }
            o /= to - from; a /= to - from;
            var l = new GridLine { P = h * a + nrm * o, H = h };
            l.Enter = OnLine(l, p[idx[from]]); l.Exit = OnLine(l, p[idx[to - 1]]);
            return l;
        }

        private static float Deviation(List<Vector2> p, List<int> idx, int from, int to, GridLine l)
        {
            float worst = 0f; var nrm = Left(l.H);
            for (int i = from; i < to; i++) worst = Mathf.Max(worst, Mathf.Abs(Vector2.Dot(p[idx[i]] - l.P, nrm)));
            return worst;
        }

        /// <summary>A run drawn between two grid directions: a straight and a diagonal (whichever order fits) instead.</summary>
        private static bool SplitRun(List<Vector2> p, List<int> idx, int dir, List<GridLine> into)
        {
            var whole = FitLine(p, idx, 0, idx.Count, dir);
            float dev = Deviation(p, idx, 0, idx.Count, whole);
            if (dev < 0.03f) return false;
            // Drawn within 10 degrees of a grid direction: meant straight.
            var chord = p[idx[^1]] - p[idx[0]];
            if (Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(chord.y, chord.x) * Mathf.Rad2Deg, dir * 45f)) < 10f) return false;
            float best = dev * 0.6f; GridLine a = null, b = null;
            foreach (int other in new[] { dir + 1, dir + 7 })
            foreach (bool otherFirst in new[] { false, true })
                for (int m = 3; m < idx.Count - 3; m += 2)
                {
                    int d0 = otherFirst ? other : dir, d1 = otherFirst ? dir : other;
                    var l0 = FitLine(p, idx, 0, m, d0); var l1 = FitLine(p, idx, m, idx.Count, d1);
                    float cost = Mathf.Max(Deviation(p, idx, 0, m, l0), Deviation(p, idx, m, idx.Count, l1));
                    if (cost < best) { best = cost; a = l0; b = l1; }
                }
            if (a == null) return false;
            into.Add(a); into.Add(b);
            return true;
        }

        /// <summary>
        /// Puts the lines on the grid (or beside a track running alongside), adds the attachments' lines (the old track's
        /// direction at a switch or a dead end) and tidies neighbours: lines in line become one, a sideways step between
        /// parallel lines gets a diagonal (or, for a U-turn, a square line) between them.
        /// </summary>
        private static void Assemble(List<GridLine> lines, List<Vector2> p, bool closed, TrackAttach start, TrackAttach finish, TrackNetwork network)
        {
            // The hand settling on the track at either end is not a line of its own.
            if (start.Kind != AttachKind.None)
                while (lines.Count > 0 && (lines[0].Exit - start.Point).magnitude < 0.06f && (lines[0].Enter - start.Point).magnitude < 0.06f) lines.RemoveAt(0);
            if (finish.Kind != AttachKind.None)
                while (lines.Count > 0 && (lines[^1].Exit - finish.Point).magnitude < 0.06f && (lines[^1].Enter - finish.Point).magnitude < 0.06f) lines.RemoveAt(lines.Count - 1);
            foreach (var l in lines) if (!Beside(l, network)) SnapLine(l);
            foreach (var l in lines) { l.Enter = OnLine(l, l.Enter); l.Exit = OnLine(l, l.Exit); }
            if (start.Kind != AttachKind.None) lines.Insert(0, new GridLine { P = start.Point, H = start.Dir.normalized, Enter = start.Point, Exit = start.Point, Attach = 1 });
            if (finish.Kind != AttachKind.None) lines.Add(new GridLine { P = finish.Point, H = finish.Dir.normalized, Enter = finish.Point, Exit = finish.Point, Attach = 2 });
            Untangle(lines, closed);
        }

        /// <summary>
        /// A line running alongside a straight track, 3 to 25 cm off: exactly the standard distance from it, or twice that
        /// (room for a platform between) when drawn about that far.
        /// </summary>
        private static bool Beside(GridLine l, TrackNetwork network)
        {
            if (network == null || network.Empty || !Octilinear(l.H)) return false;
            var m = (l.Enter + l.Exit) * 0.5f;
            if (!network.Nearest(new Vector3(m.x, 0f, m.y), out int edge, out float s, out _, 0.18f)) return false;
            var at = network.Edges[edge].Layout.Evaluate(s, out var t);
            var th = new Vector2(t.x, t.z).normalized;
            if (Mathf.Abs(Cross(th, l.H)) > Mathf.Sin(3f * Mathf.Deg2Rad) || !StraightAt(network, edge, s, 0.05f)) return false;
            var nrm = Left(l.H);
            float off = Vector2.Dot(m - new Vector2(at.x, at.z), nrm);
            if (Mathf.Abs(off) < 0.03f || Mathf.Abs(off) > 0.25f) return false;
            l.BesideOffset = (Mathf.Abs(off) < 0.155f ? 1f : 2f) * BesideSpacing(l.H);
            float target = Vector2.Dot(new Vector2(at.x, at.z), nrm) + Mathf.Sign(off) * l.BesideOffset;
            l.P += nrm * (target - Vector2.Dot(l.P, nrm));
            l.Beside = true;
            return true;
        }

        /// <summary>
        /// Another reading: a line between two turns the same way is part of one bigger curve (a rounded corner drawn
        /// as one, not as two bends with a diagonal between) - so a loop drawn round a loop can curve concentrically.
        /// </summary>
        private static void MergeCorners(List<GridLine> lines, bool closed)
        {
            for (int i = closed ? 0 : 1; i < (closed ? lines.Count : lines.Count - 1) && lines.Count > (closed ? 3 : 2); i++)
            {
                int n = lines.Count;
                var a = lines[(i - 1 + n) % n]; var m = lines[i]; var b = lines[(i + 1) % n];
                if (m.Attach != 0 || m.Beside) continue;
                float t1 = Cross(a.H, m.H), t2 = Cross(m.H, b.H);
                if (t1 * t2 <= 0f || Mathf.Abs(Cross(a.H, b.H)) < Mathf.Sin(6f * Mathf.Deg2Rad) || Vector2.Dot(a.H, b.H) < -0.1f) continue;
                lines.RemoveAt(i);
                i--;
            }
        }

        private static void Untangle(List<GridLine> lines, bool closed)
        {
            float parallel = Mathf.Sin(6f * Mathf.Deg2Rad);
            for (int guard = 0; guard < 60; guard++)
            {
                bool changed = false;
                int pairs = closed ? lines.Count : lines.Count - 1;
                for (int i = 0; i < pairs && lines.Count > (closed ? 3 : 1); i++)
                {
                    var a = lines[i]; var b = lines[(i + 1) % lines.Count];
                    if (Mathf.Abs(Cross(a.H, b.H)) >= parallel) continue;
                    var na = Left(a.H);
                    float off = Vector2.Dot(b.Enter - a.P, na);
                    bool exact = Mathf.Abs(Cross(a.H, b.H)) < 1e-3f;
                    if (!exact && Vector2.Dot(a.H, b.H) > 0f && (a.Attach != 0) != (b.Attach != 0))
                    {
                        // Almost in line with an old track's own (off the grid) direction: carry on along that instead.
                        if (a.Attach != 0) { a.Exit = OnLine(a, b.Exit); lines.RemoveAt((i + 1) % lines.Count); }
                        else { b.Enter = OnLine(b, a.Enter); lines.RemoveAt(i); }
                    }
                    else if (Vector2.Dot(a.H, b.H) > 0f && Mathf.Abs(off) < Mathf.Max(0.012f, 0.6f * LineSpacing(a.H)))
                    {
                        // In line: one line (an attachment's line wins, it is where the old track is).
                        if (a.Attach != 0 && b.Attach != 0) { a.Attach = 3; a.Exit = b.Exit; lines.RemoveAt((i + 1) % lines.Count); }
                        else if (b.Attach != 0 || a.Attach == 0 && (b.Exit - b.Enter).sqrMagnitude > (a.Exit - a.Enter).sqrMagnitude)
                        { b.Enter = OnLine(b, a.Enter); lines.RemoveAt(i); }
                        else { a.Exit = OnLine(a, b.Exit); lines.RemoveAt((i + 1) % lines.Count); }
                    }
                    else
                    {
                        // A step sideways (or a U-turn): a line across, through the middle of the step.
                        float side = Mathf.Sign(off == 0f ? 1f : off);
                        bool uTurn = Vector2.Dot(a.H, b.H) < 0f;
                        var h = Rotate(a.H, side * (uTurn ? 90f : 45f) * Mathf.Deg2Rad).normalized;
                        if (Octilinear(a.H)) h = Dir8(Mathf.RoundToInt(Mathf.Atan2(h.y, h.x) * Mathf.Rad2Deg / 45f));
                        var mid = (a.Exit + b.Enter) * 0.5f;
                        var l = new GridLine { P = mid, H = h, Enter = mid, Exit = mid };
                        SnapLine(l);
                        lines.Insert(i + 1, l);
                    }
                    changed = true;
                    break;
                }
                if (!changed) break;
            }
        }

        /// <summary>
        /// The track along a list of lines: straights joined by standard curves. A curve that has no room is made smaller,
        /// two curves the same way with no room between them become one, a sideways step with no room for its diagonal
        /// becomes a reverse curve; a free end steps back along its line to give the first curve room, and a line drawn
        /// too close to a fixed end moves out a little. Scored by how far it strays from the stroke, and how simple it is.
        /// </summary>
        private static GridShape Realize(List<GridLine> lines, bool closed, List<Vector2> stroke, TrackNetwork network,
            TrackAttach start, TrackAttach finish, bool slideStart, bool slideFinish)
        {
            var shape = new GridShape { Start = start, Finish = finish };
            bool fixedStart = start.Kind != AttachKind.None && !slideStart, fixedFinish = finish.Kind != AttachKind.None && !slideFinish;
            Vector2 freeStart = default, freeEnd = default;
            bool startMoved = false, endMoved = false;
            int count = 0;
            var trail = GridLog != null ? new System.Text.StringBuilder() : null;   // (diagnostics: which line was short, by how much, at each try)
            Vector2[] v = null; float[] phi = null, rad = null, tan = null;
            Vector2 S = default, E = default;
            for (int iter = 0; ; iter++)
            {
                if (iter > 80) return Failed(shape, "A curve is too tight for the train - draw it a little wider", "no fix in 80 tries:" + trail);
                count = lines.Count;
                if (closed ? count < 3 : count < 1) return Failed(shape, closed ? "Make the loop a little bigger" : "Draw a longer track", $"{count} lines left");
                if (!closed && count == 1 && lines[0].Attach == 0 && (lines[0].Exit - lines[0].Enter).magnitude < 0.05f) return Failed(shape, "Draw a longer track", "one short line");
                int corners = closed ? count : count - 1;
                v = new Vector2[corners]; phi = new float[corners]; rad = new float[corners]; tan = new float[corners];
                bool parallelPair = false;
                for (int c = 0; c < corners; c++)
                {
                    var a = lines[c]; var b = lines[(c + 1) % count];
                    if (!Intersect(a, b, out v[c])) { parallelPair = true; break; }
                    phi[c] = Vector2.SignedAngle(a.H, b.H) * Mathf.Deg2Rad;
                    rad[c] = CornerRadius(a, b, v[c], phi[c], stroke, network);
                    tan[c] = rad[c] * Mathf.Tan(Mathf.Abs(phi[c]) * 0.5f);
                }
                if (parallelPair) { Untangle(lines, closed); if (lines.Count == count) return Failed(shape, "Draw a longer track", "parallel lines left"); continue; }
                // Lines almost parallel meet far away: never a reading of a stroke on the floor (and never traced point by point).
                foreach (var c in v) if (!((c - stroke[0]).sqrMagnitude < 25f)) return Failed(shape, "A curve is too tight for the train - draw it a little wider", "a corner far away");
                if (!closed)
                {
                    var l0 = lines[0]; var ln = lines[^1];
                    if (!startMoved) freeStart = GridPointOn(l0, stroke[0]);
                    if (!endMoved) freeEnd = GridPointOn(ln, stroke[^1]);
                    S = start.Kind == AttachKind.None ? freeStart : slideStart && corners > 0 ? v[0] - l0.H * tan[0] : start.Point;
                    E = finish.Kind == AttachKind.None ? freeEnd : slideFinish && corners > 0 ? v[^1] + ln.H * tan[^1] : finish.Point;
                    if (count == 1 && l0.Attach == 3) { S = start.Point; E = finish.Point; }
                }
                // The line with least room for its curves.
                int worst = -1; float worstDeficit = 1e-4f;
                for (int j = 0; j < count; j++)
                {
                    bool hasIn = closed || j > 0, hasOut = closed || j < count - 1;
                    var entry = hasIn ? v[(j - 1 + corners) % corners] : S;
                    var exit = hasOut ? v[j % corners] : E;
                    float span = Vector2.Dot(exit - entry, lines[j].H);
                    float need = (hasIn ? tan[(j - 1 + corners) % corners] : 0f) + (hasOut ? tan[j % corners] : 0f);
                    if (!closed && count == 1) need = 0.05f;
                    float deficit = need - span;
                    if (deficit > worstDeficit) { worstDeficit = deficit; worst = j; }
                }
                if (worst < 0) break;
                trail?.Append($" {worst}/{count}:{worstDeficit * 100f:0.0}");
                if (!Fix(lines, worst, worstDeficit, closed, phi, rad, fixedStart, fixedFinish, start.Kind == AttachKind.None, finish.Kind == AttachKind.None,
                        ref freeStart, ref startMoved, ref freeEnd, ref endMoved, v, tan, stroke, out string why))
                {
                    shape.Error = why;
                    GridLog?.AppendLine($"  reading failed ({why}) at line {worst} of {lines.Count}, short by {worstDeficit * 100f:0.0} cm");
                    return shape;
                }
            }
            // The switch slid along a straight track: it must still be on it, clear of other switches and crossings.
            if (slideStart && !closed && !SlideOk(network, ref shape.Start, S, lines[0].H, out string e1)) return Failed(shape, e1, "start switch slid off");
            if (slideFinish && !closed && !SlideOk(network, ref shape.Finish, E, lines[^1].H, out string e2)) return Failed(shape, e2, "finish switch slid off");
            if (shape.Start.Kind == AttachKind.Mid && shape.Finish.Kind == AttachKind.Mid && shape.Start.Edge == shape.Finish.Edge
                && TrackNetwork.ArcGap(network.Edges[shape.Start.Edge].Layout, shape.Start.S, shape.Finish.S) < MinJointGap)
                return Failed(shape, "The two switches would be too close together", "");
            if (GridLog != null)
            {
                GridLog.Append($"  reading: {lines.Count} lines");
                for (int c = 0; c < v.Length; c++) GridLog.Append($" | {Mathf.Atan2(lines[c].H.y, lines[c].H.x) * Mathf.Rad2Deg:0}deg{(lines[c].Beside ? " beside" : "")}{(lines[c].Attach != 0 ? " attach" : "")} -> corner ({v[c].x:0.000},{v[c].y:0.000}) {phi[c] * Mathf.Rad2Deg:0}deg R{rad[c]:0.0}");
                GridLog.AppendLine();
            }
            shape.Path = Trace(lines, closed, S, E, v, phi, rad, tan);
            if (shape.Path.Count < 3) return Failed(shape, "Draw a longer track", "a short path");
            // Score: how far it strays from the stroke (on average and at worst), plus a little for each curve. Both run
            // the same way, so each stroke point looks near where the last one found the track (all of it if that fails).
            float sum = 0f, far = 0f; int samples = 0, at = -1, pc = shape.Path.Count;
            for (int i = 0; i < stroke.Count; i += 2)
            {
                float best = float.MaxValue; int bestAt = at;
                if (at >= 0)
                    for (int o = -40; o <= 40; o += 2)
                    {
                        int k = closed ? ((at + o) % pc + pc) % pc : at + o;
                        if (k < 0 || k >= pc) continue;
                        float d = (shape.Path[k] - stroke[i]).sqrMagnitude;
                        if (d < best) { best = d; bestAt = k; }
                    }
                if (at < 0 || best > 0.0025f)
                    for (int k = 0; k < pc; k += 2) { float d = (shape.Path[k] - stroke[i]).sqrMagnitude; if (d < best) { best = d; bestAt = k; } }
                at = bestAt;
                best = Mathf.Sqrt(best); sum += best; far = Mathf.Max(far, best); samples++;
            }
            shape.Score = sum / Mathf.Max(1, samples) + 0.2f * far + 0.004f * v.Length;
            if (!closed && start.Kind == AttachKind.None) shape.Score += 0.2f * (S - stroke[0]).magnitude;
            if (!closed && finish.Kind == AttachKind.None) shape.Score += 0.2f * (E - stroke[^1]).magnitude;
            return shape;
        }

        private static GridShape Failed(GridShape shape, string error, string detail)
        {
            shape.Error = error;
            GridLog?.AppendLine($"  reading failed ({error}): {detail}");
            return shape;
        }

        /// <summary>
        /// The radius for the curve between two lines: a reverse curve's own; beside a track, the one keeping the standard
        /// distance from it round the bend; otherwise the standard radius whose curve passes nearest the stroke.
        /// </summary>
        private static float CornerRadius(GridLine a, GridLine b, Vector2 v, float phi, List<Vector2> stroke, TrackNetwork network)
        {
            float cap = a.Cap;
            if (a.SRadius > 0f) return a.SRadius;
            if (b.SRadius > 0f) return b.SRadius;
            float turn = Mathf.Abs(phi);
            float bulge = 1f / Mathf.Cos(turn * 0.5f) - 1f;   // how far the curve's middle lies from the corner, per metre of radius
            float pref;
            var bisector = (b.H - a.H).normalized;
            if (a.Beside && b.Beside && network != null)
            {
                // Concentric with the track alongside: the radius whose curve stays the standard distance from it.
                pref = Radii[0]; float bestDev = float.MaxValue;
                float want = (a.BesideOffset + b.BesideOffset) * 0.5f;
                foreach (float r in Radii)
                {
                    if (r > cap + 1e-4f) break;
                    float t = r * Mathf.Tan(turn * 0.5f);
                    var centre = v + bisector * (r / Mathf.Cos(turn * 0.5f));
                    float dev = 0f;
                    for (int k = 0; k <= 4; k++)
                    {
                        var q = centre + Rotate(v - a.H * t - centre, Mathf.Sign(phi) * turn * k / 4f);
                        network.Nearest(new Vector3(q.x, 0f, q.y), out _, out _, out float d, 0.3f);
                        dev = Mathf.Max(dev, Mathf.Abs(d - want));
                    }
                    if (dev < bestDev) { bestDev = dev; pref = r; }
                }
                if (bestDev > 0.02f) pref = FromStroke();
            }
            else pref = FromStroke();
            // The largest standard radius no bigger than the preferred one and the cap.
            float chosen = Radii[0];
            foreach (float r in Radii) if (r <= Mathf.Min(pref, cap) + 1e-4f) chosen = r;
            return chosen;

            float FromStroke()
            {
                if (bulge < 1e-4f) return Radii[^1];
                if (a.NearAt != v)
                {
                    float near = float.MaxValue;
                    for (int i = 0; i < stroke.Count; i += 2) near = Mathf.Min(near, (stroke[i] - v).sqrMagnitude);
                    a.NearAt = v; a.Near = Mathf.Sqrt(near);
                }
                float r = a.Near / bulge;
                // Nearest standard radius.
                float best = Radii[0];
                foreach (float g in Radii) if (Mathf.Abs(g - r) < Mathf.Abs(best - r)) best = g;
                return best;
            }
        }

        /// <summary>Makes room on line <paramref name="j"/>, which is <paramref name="deficit"/> too short for its curves.</summary>
        private static bool Fix(List<GridLine> lines, int j, float deficit, bool closed, float[] phi, float[] rad, bool fixedStart, bool fixedFinish,
            bool freeStartEnd, bool freeFinishEnd, ref Vector2 freeStart, ref bool startMoved, ref Vector2 freeEnd, ref bool endMoved,
            Vector2[] v, float[] tan, List<Vector2> stroke, out string why)
        {
            why = null;
            int count = lines.Count, corners = closed ? count : count - 1;
            // One line between two fixed ends: put it where both its curves fit (pushing it would only see-saw).
            bool between = !closed && count == 3 && fixedStart && fixedFinish && lines[1].Attach == 0;
            int cin = closed || j > 0 ? (j - 1 + corners) % corners : -1;
            int cout = closed || j < count - 1 ? j % corners : -1;
            // 1. A smaller curve, at whichever end of this line has the bigger one (a reverse curve: a shorter, steeper one).
            int shrink = -1;
            foreach (int c in new[] { cin, cout })
            {
                if (c < 0 || rad[c] <= Radii[0] + 1e-4f) continue;
                var s = lines[c].SRadius > 0f ? lines[c] : lines[(c + 1) % count].SRadius > 0f ? lines[(c + 1) % count] : null;
                if (s != null && rad[c] <= ReverseLeast(s.SOffset) + 1e-4f) continue;
                if (shrink < 0 || rad[c] > rad[shrink]) shrink = c;
            }
            if (shrink >= 0)
            {
                float smaller = Radii[0];
                foreach (float r in Radii) if (r < rad[shrink] - 1e-4f) smaller = r;
                var s = lines[shrink].SRadius > 0f ? lines[shrink] : lines[(shrink + 1) % count].SRadius > 0f ? lines[(shrink + 1) % count] : null;
                if (s != null) SetReverse(s, smaller);
                else lines[shrink].Cap = smaller;
                return true;
            }
            // 2. Change the lines.
            if (!closed && j == 0 && cin < 0)
            {
                if (freeStartEnd && count > 1)
                {
                    // A free end: step back along the line (to a grid point) so the first curve fits.
                    var l = lines[0];
                    float back = Vector2.Dot(v[0] - l.P, l.H) - tan[0];
                    freeStart = StepBack(l, l.P + l.H * back, -1f);
                    startMoved = true;
                    return true;
                }
                if (between) { if (!lines[1].Reseated && Reseat(lines, stroke)) return true; why = "No room for the curves between these two tracks - draw the link a little longer"; return false; }
                if (fixedStart && count > 1 && lines[1].Attach == 0 && Push(lines[1], lines[0].H, deficit)) return true;
                if (fixedStart && count > 2 && lines[1].Attach == 0) { lines.RemoveAt(1); lines[0].Cap = float.MaxValue; return true; }
                if (count == 2 && Bridge(lines)) return true;
                why = "Too sharp a turn right at the start - begin the curve a little further out";
                return false;
            }
            if (!closed && j == count - 1 && cout < 0)
            {
                if (freeFinishEnd && count > 1)
                {
                    var l = lines[^1];
                    float on = Vector2.Dot(v[^1] - l.P, l.H) + tan[^1];
                    freeEnd = StepBack(l, l.P + l.H * on, 1f);
                    endMoved = true;
                    return true;
                }
                if (between) { if (!lines[1].Reseated && Reseat(lines, stroke)) return true; why = "No room for the curves between these two tracks - draw the link a little longer"; return false; }
                if (fixedFinish && count > 1 && lines[^2].Attach == 0 && Push(lines[^2], -lines[^1].H, deficit)) return true;
                if (fixedFinish && count > 2 && lines[^2].Attach == 0) { lines.RemoveAt(lines.Count - 2); lines[^2].Cap = float.MaxValue; return true; }
                if (count == 2 && Bridge(lines)) return true;
                why = "Too sharp a turn right at the end - end the curve a little further out";
                return false;
            }
            if (count == 1) { why = "Draw a longer track"; return false; }
            var mid = lines[j];
            var before = lines[(j - 1 + count) % count]; var after = lines[(j + 1) % count];
            if (mid.Attach != 0) { why = "A curve is too tight for the train - draw it a little wider"; return false; }
            if (between && !mid.Reseated && Reseat(lines, stroke)) return true;
            bool sameWay = Mathf.Sign(phi[cin]) == Mathf.Sign(phi[cout]);
            bool outerParallel = Mathf.Abs(Cross(before.H, after.H)) < 1e-3f;
            if (!outerParallel && (closed ? count > 3 : true))
            {
                // Two curves become one (or a little kink goes).
                lines.RemoveAt(j);
                lines[(j - 1 + lines.Count) % lines.Count].Cap = float.MaxValue;
                return true;
            }
            if (!sameWay && outerParallel && mid.SRadius == 0f)
            {
                // A sideways step between parallel lines (a crossover): a reverse curve, as gentle as there is room for
                // (it starts gentle and gets steeper while its neighbours need the room).
                float d = Mathf.Abs(Vector2.Dot(after.P - before.P, Left(before.H)));
                if (d < 1e-3f || ReverseLeast(d) > Radii[^1]) { why = "That step sideways is too sharp - draw it a little longer"; return false; }
                mid.SOffset = d; mid.SBase = before.H;
                mid.SSide = Mathf.Sign(Vector2.Dot(after.P - before.P, Left(before.H)));
                mid.P = (v[cin] + v[cout]) * 0.5f;
                float r = Radii[^1];
                foreach (float g in Radii) if (g >= ReverseLeast(d) - 1e-4f && g <= 0.5f + 1e-4f) { r = g; }
                SetReverse(mid, Mathf.Max(r, ReverseLeast(d)));
                return true;
            }
            why = "A curve is too tight for the train - draw it a little wider";
            return false;
        }

        /// <summary>
        /// The line between two fixed ends, placed where both its curves fit at the smallest radius: along a grid direction
        /// (or straight from one end towards the other), on a grid line, as near the stroke as can be. Once.
        /// </summary>
        private static bool Reseat(List<GridLine> lines, List<Vector2> stroke)
        {
            var a = lines[0]; var m = lines[1]; var b = lines[2];
            m.Reseated = true;
            float r = Radii[0];
            GridLine best = null; float bestScore = float.MaxValue;
            // The grid directions first; then any angle (in 5 degree steps) off the first end's direction - a crossover
            // between tracks 10 cm apart wants a shallower diagonal than 45 degrees.
            var headings = new List<Vector2>();
            for (int d = 0; d < 8; d++) headings.Add(Dir8(d));
            if ((b.P - a.P).sqrMagnitude > 1e-6f) headings.Add((b.P - a.P).normalized);
            for (int k = 1; k < 18; k++) { headings.Add(Rotate(a.H, k * 5f * Mathf.Deg2Rad)); headings.Add(Rotate(a.H, -k * 5f * Mathf.Deg2Rad)); }
            foreach (var h in headings)
            {
                if (Mathf.Abs(Cross(h, a.H)) < 0.05f || Mathf.Abs(Cross(h, b.H)) < 0.05f || Vector2.Dot(h, a.H) < -0.2f || Vector2.Dot(h, b.H) < -0.2f) continue;
                var n = Left(h);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var q in stroke) { float o = Vector2.Dot(q, n); lo = Mathf.Min(lo, o); hi = Mathf.Max(hi, o); }
                float step = Octilinear(h) ? LineSpacing(h) : 0.01f;
                for (float o = Mathf.Floor((lo - 0.3f) / step) * step; o <= hi + 0.3f; o += step)
                {
                    var l = new GridLine { P = n * o, H = h };
                    if (!Intersect(a, l, out var v0) || !Intersect(l, b, out var v1)) continue;
                    float t0 = r * Mathf.Tan(Vector2.Angle(a.H, h) * Mathf.Deg2Rad * 0.5f), t1 = r * Mathf.Tan(Vector2.Angle(h, b.H) * Mathf.Deg2Rad * 0.5f);
                    if (Vector2.Dot(v0 - a.P, a.H) < t0 || Vector2.Dot(v1 - v0, h) < t0 + t1 || Vector2.Dot(b.P - v1, b.H) < t1) continue;
                    float score = 0f;
                    for (int i = 0; i < stroke.Count; i += 8)
                    {
                        var q = stroke[i];
                        score += Mathf.Min(Polyline.DistanceToSegment(q, a.P, v0), Mathf.Min(Polyline.DistanceToSegment(q, v0, v1), Polyline.DistanceToSegment(q, v1, b.P)));
                    }
                    if (score < bestScore) { bestScore = score; best = l; }
                }
            }
            if (best == null) return false;
            m.P = best.P; m.H = best.H; m.SRadius = 0f;
            m.Enter = OnLine(m, m.Enter); m.Exit = OnLine(m, m.Exit);
            a.Cap = float.MaxValue; m.Cap = float.MaxValue;
            return true;
        }

        /// <summary>
        /// Just the two attachments' lines, meeting too soon (or behind) for one curve: a line between them, along the grid
        /// direction nearest the way from one to the other, through the middle. Once.
        /// </summary>
        private static bool Bridge(List<GridLine> lines)
        {
            var a = lines[0]; var b = lines[1];
            if (a.Attach == 0 || b.Attach == 0 || a.Bridged) return false;
            var d = b.P - a.P;
            if (d.sqrMagnitude < 1e-6f) return false;
            var h = Dir8(Mathf.RoundToInt(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg / 45f));
            if (Mathf.Abs(Cross(h, a.H)) < 1e-3f || Mathf.Abs(Cross(h, b.H)) < 1e-3f) h = d.normalized;
            var mid = (a.P + b.P) * 0.5f;
            var l = new GridLine { P = mid, H = h, Enter = mid, Exit = mid };
            SnapLine(l);
            a.Bridged = true;
            lines.Insert(1, l);
            return true;
        }

        /// <summary>The smallest standard radius a reverse curve over a sideways step <paramref name="d"/> can have (its curves at most 45 degrees).</summary>
        private static float ReverseLeast(float d)
        {
            float least = d / (2f * (1f - Half));
            foreach (float g in Radii) if (g >= least - 1e-4f) return g;
            return float.MaxValue;
        }

        private static void SetReverse(GridLine mid, float r)
        {
            r = Mathf.Max(r, ReverseLeast(mid.SOffset));
            float alpha = Mathf.Acos(Mathf.Clamp(1f - mid.SOffset / (2f * r), -1f, 1f)) * 0.999f;
            mid.SRadius = r;
            mid.H = Rotate(mid.SBase, mid.SSide * alpha).normalized;
        }

        /// <summary>Moves a line along <paramref name="dir"/> by at least <paramref name="by"/>, onto a grid line.</summary>
        private static bool Push(GridLine l, Vector2 dir, float by)
        {
            var n = Left(l.H);
            float across = Vector2.Dot(dir, n);
            if (Mathf.Abs(across) < 0.1f) return false;
            // Moving the line by dir * x moves its corner with the fixed end's line x along that line.
            float o = Vector2.Dot(l.P, n), target = o + by * across;
            if (Octilinear(l.H))
            {
                float sp = LineSpacing(l.H);
                target = across > 0f ? Mathf.Ceil(target / sp - 1e-3f) * sp : Mathf.Floor(target / sp + 1e-3f) * sp;
            }
            l.P += n * (target - o);
            l.Enter = OnLine(l, l.Enter); l.Exit = OnLine(l, l.Exit);
            return true;
        }

        /// <summary>The grid point on a line at or beyond <paramref name="p"/> in direction <paramref name="sign"/> (or p itself off the grid).</summary>
        private static Vector2 StepBack(GridLine l, Vector2 p, float sign)
        {
            if (!Octilinear(l.H)) return p;
            var g = GridPointOn(l, p);
            if ((OnLine(l, g) - g).sqrMagnitude > 1e-8f) return p;
            float step = Diagonal(l.H) ? BuildGrid.Size / Half : BuildGrid.Size;
            float d = Vector2.Dot(p - g, l.H) * sign;
            if (d > 1e-4f) g += l.H * sign * step;
            return g;
        }

        /// <summary>Where the switch slid to: still on the straight track, clear of switches, crossings and the buffers.</summary>
        private static bool SlideOk(TrackNetwork network, ref TrackAttach a, Vector2 at, Vector2 heading, out string why)
        {
            why = null;
            var l = network.Edges[a.Edge].Layout;
            float s = l.Nearest(new Vector3(at.x, 0f, at.y), out float d);
            if (d > 0.002f || !StraightAt(network, a.Edge, s, 0.03f)) { why = "The new switch needs straight track to sit on - start the branch further along"; return false; }
            if (!l.Closed && (s < 0.05f || s > l.Length - 0.05f)) { why = "Too close to the end of the line for a switch"; return false; }
            if (network.DistanceToJoint(a.Edge, s) < MinJointGap) { why = "Too close to a switch - start a little further along"; return false; }
            if (network.DistanceToCrossing(a.Edge, s) < MinJointGap * 0.7f) { why = "Too close to a crossing"; return false; }
            a.Point = at; a.S = s; a.Dir = heading;
            return true;
        }

        /// <summary>The centre line every centimetre: straights and arcs, exactly.</summary>
        private static List<Vector2> Trace(List<GridLine> lines, bool closed, Vector2 S, Vector2 E, Vector2[] v, float[] phi, float[] rad, float[] tan)
        {
            var pts = new List<Vector2>();
            int count = lines.Count, corners = v.Length;
            void To(Vector2 q)
            {
                var from = pts[^1];
                int steps = Mathf.CeilToInt((q - from).magnitude / Spacing - 1e-3f);
                for (int k = 1; k <= steps; k++) pts.Add(Vector2.Lerp(from, q, k / (float)steps));
            }
            void Arc(int c)
            {
                var a = lines[c].H;
                var start = v[c] - a * tan[c];
                To(start);
                float side = Mathf.Sign(phi[c]);
                var centre = start + Left(a) * (rad[c] * side);
                float turn = Mathf.Abs(phi[c]);
                int steps = Mathf.Max(2, Mathf.CeilToInt(turn * rad[c] / Spacing));
                for (int k = 1; k <= steps; k++) pts.Add(centre + Rotate(start - centre, side * turn * k / steps));
            }
            if (closed)
            {
                var b = lines[0].H;
                pts.Add(v[corners - 1] + b * tan[corners - 1]);
                for (int c = 0; c < corners; c++) Arc(c);
                if (pts.Count > 1 && (pts[^1] - pts[0]).magnitude < Spacing * 0.5f) pts.RemoveAt(pts.Count - 1);
                return pts;
            }
            pts.Add(S);
            for (int c = 0; c < corners; c++) Arc(c);
            To(E);
            return pts;
        }
    }
}
