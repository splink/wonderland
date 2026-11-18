using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// The railway on the grid, as a player draws it: a round loop, straights and curves, two loops linked both ways,
        /// branches at every angle, parallel tracks and loops, extending a line, linking two railways, removing and
        /// bending track, several trains, saving, and old freely drawn railways (and real saved worlds) carrying on.
        /// Every piece is checked to be standard track: straights at a multiple of 45 degrees through grid points, and
        /// curves of a standard radius.
        /// </summary>
        private static void GridRailJourney()
        {
            bool gridWas = BuildGrid.Snap;
            BuildGrid.Snap = true;
            try
            {
                GridLoopAndLines();
                GridBranches();
                GridParallel();
                GridNetwork();
                GridOldWorlds();
            }
            finally { BuildGrid.Snap = gridWas; }
        }

        private static void GridLoopAndLines()
        {
            // A round loop, drawn by hand.
            {
                var w = Scratch("Grid circle", 600f);
                var t = w.Terrain;
                var drawn = HandDrawnLoop(t, new Vector2(0.02f, -0.01f), new Vector2(0.42f, 0.42f));
                float ms = PlanTime(drawn, t, w.Track.Network, true), freeMs = PlanTime(drawn, t, w.Track.Network, false);
                var plan = GridBuild(w, drawn);
                var n = w.Track.Network;
                bool standard = GridConforms(n, out string why, out var radii);
                var (mean, far) = Strays(n, drawn);
                Check(plan.Ok && n.Edges.Count == 1 && n.Edges[0].Closed && standard,
                    $"A hand-drawn round loop comes out as standard track: closed, straights on the grid and curves of {Radii(radii)} ({why}{plan.Error})");
                Check(mean < 0.03f && far < 0.08f, $"...and stays close to what was drawn (on average {mean * 100f:0.0} cm off, at most {far * 100f:0.0} cm; planned in {ms:0.0} ms, {freeMs:0.0} ms drawn freely)");
                PlaceTrainAt(w, n.Edges[0].Layout.Points[0]);
                var (_, off) = RunTrains(w, 30f);
                var tr = FirstTrain(w);
                Check(tr != null && off < 0.006f && tr.Odometer > 2f, $"A train runs round it on the rails (worst {off * 1000f:0.0} mm off, {(tr != null ? tr.Odometer : 0f):0.0} m)");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            // A line drawn a few degrees off the grid comes out dead straight on a grid line, its ends on grid points.
            {
                var w = Scratch("Grid straight", 605f);
                var t = w.Terrain;
                var rng = new Rng(3);
                var stroke = Line(t, new Vector2(-0.6f, 0.1f), new Vector2(0.6f, 0.1f + 1.2f * Mathf.Tan(4f * Mathf.Deg2Rad)));
                for (int i = 0; i < stroke.Count; i++) stroke[i] += new Vector3(rng.Range(-0.006f, 0.006f), 0f, rng.Range(-0.006f, 0.006f));
                var plan = GridBuild(w, stroke);
                var n = w.Track.Network;
                bool straight = plan.Ok && n.Edges.Count == 1;
                float z0 = straight ? n.Edges[0].Layout.Points[0].z : 0f, worst = 0f;
                if (straight) foreach (var p in n.Edges[0].Layout.Points) worst = Mathf.Max(worst, Mathf.Abs(p.z - z0));
                var a = straight ? n.Edges[0].Layout.Points[0] : default; var b = straight ? n.Edges[0].Layout.Points[^1] : default;
                Check(straight && worst < 1e-4f && OnGrid(a) && OnGrid(b),
                    $"A line drawn 4 degrees off comes out dead straight along the grid, its ends on grid points ({worst * 1000f:0.00} mm off straight; ends ({a.x:0.000}, {a.z:0.000}) and ({b.x:0.000}, {b.z:0.000}){plan.Error})");
                // An L with a rounded corner: a straight, one standard curve, a straight.
                var l = new List<Vector2> { new(-0.5f, -0.45f), new(0f, -0.45f) };
                for (int i = 1; i <= 8; i++) { float ang = i / 8f * Mathf.PI / 2f; l.Add(new Vector2(0f + Mathf.Sin(ang) * 0.3f, -0.45f + (1f - Mathf.Cos(ang)) * 0.3f)); }
                l.Add(new Vector2(0.3f, 0.35f));
                var w2 = Scratch("Grid L", 608f);
                var plan2 = GridBuild(w2, Line(w2.Terrain, l.ToArray()));
                bool lOk = GridConforms(w2.Track.Network, out string whyL, out var radiiL);
                Check(plan2.Ok && lOk && radiiL.Count == 1, $"An L drawn with a rounded corner: two straights and one standard curve of {Radii(radiiL)} ({whyL}{plan2.Error})");
                UnityEngine.Object.DestroyImmediate(w2.gameObject);
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
        }

        /// <summary>Branches off a straight track at several angles: each gets a switch exactly on the old line, leaving it tangentially.</summary>
        private static void GridBranches()
        {
            int ok = 0, tried = 0;
            var failed = new List<string>();
            foreach (var (angle, side) in new[] { (30f, 1f), (45f, 1f), (60f, 1f), (90f, 1f), (45f, -1f), (135f, 1f), (25f, -1f) })
            {
                var w = Scratch("Grid branch", 612f);
                var t = w.Terrain;
                GridBuild(w, Line(t, new Vector2(-0.8f, 0f), new Vector2(0.8f, 0f)));
                var n = w.Track.Network;
                var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), side * Mathf.Sin(angle * Mathf.Deg2Rad));
                var from = new Vector2(-0.1f, 0f);
                var plan = GridBuild(w, Line(t, from, from + dir * 0.2f, from + dir * 0.5f));
                tried++;
                string why = plan.Error;
                if (plan.Ok)
                {
                    n = w.Track.Network;
                    var j = n.Joints.Count == 1 ? n.Joints[0] : null;
                    float onLine = j != null ? Mathf.Abs(j.Position.z) : 1f;
                    // The branch: the piece with a dead end that is not on the old line.
                    float leave = 99f;
                    for (int e = 0; e < n.Edges.Count && j != null; e++)
                    {
                        var l = n.Edges[e].Layout;
                        if (Mathf.Abs(l.Points[0].z) < 1e-3f && Mathf.Abs(l.Points[^1].z) < 1e-3f) continue;   // (a half of the old line)
                        int k = (new Vector2(l.Points[0].x, l.Points[0].z) - new Vector2(j.Position.x, j.Position.z)).magnitude < 0.003f ? 0 : 1;
                        // The tangent where it leaves the switch (second order from the first two centimetres).
                        var d0 = k == 0 ? (l.Points[1] - l.Points[0]) * 4f - (l.Points[2] - l.Points[0]) : (l.Points[^2] - l.Points[^1]) * 4f - (l.Points[^3] - l.Points[^1]);
                        leave = Mathf.Min(Vector2.Angle(new Vector2(d0.x, d0.z), Vector2.right), Vector2.Angle(new Vector2(d0.x, d0.z), Vector2.left));
                    }
                    bool standard = GridConforms(n, out string whyStd, out _);
                    if (j != null && onLine < 0.0005f && leave < 0.5f && standard && JointsTrue(n, out _)) ok++;
                    else why = $"switch {onLine * 1000f:0.0} mm off the line, leaves at {leave:0.0} deg, {whyStd}";
                }
                if (why != null) failed.Add($"{angle:0} deg{(side < 0 ? " down" : "")}: {why}");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            Check(ok == tried, $"Branches drawn off a straight at 25 to 135 degrees get a switch exactly on the line, the branch leaving it tangentially on a standard curve ({ok}/{tried}{(failed.Count > 0 ? ": " + string.Join("; ", failed) : "")})");
        }

        private static void GridParallel()
        {
            // A second line drawn about 8 cm beside a straight: exactly the standard distance from it.
            {
                var w = Scratch("Grid parallel", 620f);
                var t = w.Terrain;
                GridBuild(w, Line(t, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f)));
                var rng = new Rng(5);
                var stroke = Line(t, new Vector2(-0.6f, 0.08f), new Vector2(0f, 0.09f), new Vector2(0.6f, 0.075f));
                for (int i = 0; i < stroke.Count; i++) stroke[i] += new Vector3(0f, 0f, rng.Range(-0.008f, 0.008f));
                var plan = GridBuild(w, stroke);
                var n = w.Track.Network;
                float worst = 0f;
                if (plan.Ok) foreach (var p in n.Edges[^1].Layout.Points) worst = Mathf.Max(worst, Mathf.Abs(p.z - TrackPlanner.ParallelSpacing));
                Check(plan.Ok && n.Edges.Count == 2 && worst < 0.0005f, $"A line drawn about 8 cm beside a straight runs exactly {TrackPlanner.ParallelSpacing * 100f:0} cm from it ({worst * 1000f:0.0} mm off{plan.Error})");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            // A loop drawn round a loop: the same distance all the way round, its curves concentric.
            {
                var w = Scratch("Grid parallel loops", 625f);
                var t = w.Terrain;
                var inner = GridBuild(w, RoundedRect(t, new Vector2(0f, 0f), new Vector2(0.6f, 0.4f), 0.25f, 0.004f, 11));
                var rng = new Rng(8);
                var outerStroke = RoundedRect(t, new Vector2(0f, 0f), new Vector2(0.71f, 0.51f), 0.36f, 0.012f, 12);
                var outer = GridBuild(w, outerStroke);
                var n = w.Track.Network;
                bool both = inner.Ok && outer.Ok && n.Edges.Count == 2;
                float min = float.MaxValue, max = 0f;
                if (both)
                {
                    var a = n.Edges[0].Layout; var b = n.Edges[1].Layout;
                    foreach (var p in b.Points) { a.Nearest(p, out float d); min = Mathf.Min(min, d); max = Mathf.Max(max, d); }
                }
                GridConforms(n, out string why, out var radii);
                Check(both && min > TrackPlanner.ParallelSpacing - 0.003f && max < TrackPlanner.ParallelSpacing + 0.008f,
                    $"A loop drawn round a loop keeps {TrackPlanner.ParallelSpacing * 100f:0} cm from it all the way round, on concentric curves ({min * 100f:0.0} to {max * 100f:0.0} cm; curves {Radii(radii)}{inner.Error}{outer.Error})");
                // Crossovers between them, one each way: a gentle diagonal from one track to the other, a switch on each.
                var outward = GridBuild(w, Line(t, new Vector2(-0.3f, -0.4f), new Vector2(-0.1f, -0.44f), new Vector2(0.1f, -0.51f)));
                var inward = GridBuild(w, Line(t, new Vector2(0.3f, 0.51f), new Vector2(0.1f, 0.46f), new Vector2(-0.1f, 0.4f)));
                n = w.Track.Network;
                Check(outward.Ok && inward.Ok && n.Joints.Count == 4 && JointsTrue(n, out _) && n.Traps == 0 && DeadEnds(n) == 0,
                    $"Crossovers drawn from one loop to the other, one each way, each get a switch at both ends ({n.Joints.Count} switches, {n.Traps} one-way traps{outward.Error}{inward.Error})");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
        }

        /// <summary>Two loops linked both ways, extended, linked to a third railway, edited, run by three trains, saved and loaded.</summary>
        private static void GridNetwork()
        {
            var w = Scratch("Grid network", 630f);
            var t = w.Terrain;
            var left = GridBuild(w, RoundedRect(t, new Vector2(-0.5f, 0f), new Vector2(0.35f, 0.35f), 0.15f, 0.005f, 21));
            var right = GridBuild(w, RoundedRect(t, new Vector2(0.5f, 0f), new Vector2(0.35f, 0.35f), 0.15f, 0.005f, 22));
            var overStroke = Line(t, new Vector2(-0.4f, 0.35f), new Vector2(-0.25f, 0.5f), new Vector2(0.25f, 0.5f), new Vector2(0.4f, 0.35f));
            float ms = PlanTime(overStroke, t, w.Track.Network, true), freeMs = PlanTime(overStroke, t, w.Track.Network, false);
            var over = GridBuild(w, overStroke);
            var under = GridBuild(w, Line(t, new Vector2(0.4f, -0.35f), new Vector2(0.25f, -0.5f), new Vector2(-0.25f, -0.5f), new Vector2(-0.4f, -0.35f)));
            var n = w.Track.Network;
            bool standard = GridConforms(n, out string why, out var radii);
            Check(left.Ok && right.Ok && over.Ok && under.Ok && n.Joints.Count == 4 && DeadEnds(n) == 0 && JointsTrue(n, out _) && n.Traps == 0 && standard,
                $"Two loops linked both ways on the grid: 4 switches, no dead ends, no one-way traps, all standard track ({n.Joints.Count} switches, {DeadEnds(n)} dead ends, {n.Traps} traps; {why}{left.Error}{right.Error}{over.Error}{under.Error}; a link planned in {ms:0.0} ms, {freeMs:0.0} ms drawn freely)");
            // Extending: a spur off the right loop, then carried on from its dead end.
            var spur = GridBuild(w, Line(t, new Vector2(0.85f, -0.1f), new Vector2(1f, -0.25f), new Vector2(1f, -0.6f)));
            n = w.Track.Network;
            int pieces = n.Edges.Count;
            var end = FreeEnd(n);
            var carry = GridBuild(w, Line(t, end, end + new Vector2(0f, -0.25f), end + new Vector2(-0.3f, -0.45f), end + new Vector2(-0.7f, -0.45f)));
            n = w.Track.Network;
            float kink = MaxKink(n);
            bool extStd = GridConforms(n, out string whyExt, out _);
            Check(spur.Ok && carry.Ok && carry.Start.Kind == AttachKind.End && n.Edges.Count == pieces && DeadEnds(n) == 1 && extStd && kink < 3.5f,
                $"A line carried on from its dead end continues it smoothly, as one piece ({n.Edges.Count} pieces, sharpest bend {kink:0.0} deg per cm; {whyExt}{spur.Error}{carry.Error})");
            // Drawn on from a little way back along the line (the hand starting on the track): still carried on from the end.
            pieces = n.Edges.Count; int joints0 = n.Joints.Count;
            end = FreeEnd(n);
            var outward = new Vector2(-1f, 0f);
            var back = GridBuild(w, Line(t, end - outward * 0.15f, end + outward * 0.3f));
            n = w.Track.Network;
            Check(back.Ok && back.Start.Kind == AttachKind.End && n.Edges.Count == pieces && n.Joints.Count == joints0 && DeadEnds(n) == 1,
                $"Drawn on from 15 cm back along the line, it still carries the line on from its end - one piece, no new switch ({joints0} -> {n.Joints.Count} switches{back.Error})");
            // A railway of its own, then linked to this one.
            var third = GridBuild(w, RoundedRect(t, new Vector2(0f, 1.3f), new Vector2(0.5f, 0.3f), 0.15f, 0.005f, 23));
            int before = Components(w.Track.Network);
            var link = GridBuild(w, Line(t, new Vector2(-0.1f, 1f), new Vector2(-0.1f, 0.75f), new Vector2(-0.08f, 0.6f), new Vector2(0f, 0.52f), new Vector2(0.08f, 0.5f)));
            n = w.Track.Network;
            bool linkStd = GridConforms(n, out string whyLink, out _);
            Check(third.Ok && before == 2 && link.Ok && Components(n) == 1 && JointsTrue(n, out _) && linkStd,
                $"Two separate railways joined by a line from one to the other become one ({before} -> {Components(n)}; {whyLink}{third.Error}{link.Error})");
            // Removing a piece leaves a sound railway; undo brings it back.
            int joints = n.Joints.Count, edges = n.Edges.Count;
            bool removed = w.RemovePieceAt(t.PointAt(0f, -0.5f));
            n = w.Track.Network;
            bool sound = JointsTrue(n, out string whyRm) && n.Joints.Count == joints - 2;
            w.Undo();
            n = w.Track.Network;
            Check(removed && sound && n.Joints.Count == joints && n.Edges.Count == edges && JointsTrue(n, out _),
                $"Removing a link takes its two switches with it, the rest stays sound; undo puts it back ({joints} -> {joints - 2} -> {n.Joints.Count} switches{whyRm})");
            // Bending a piece on the grid: it stays standard and its switches stay put.
            var switches = new List<Vector3>(); foreach (var j in n.Joints) switches.Add(j.Position);
            n.Nearest(t.PointAt(-0.85f, 0f), out int bendEdge, out float bendS, out _, 0.1f);
            string bent = bendEdge >= 0 ? w.Reshape(bendEdge, TrackPlanner.IndexAt(n.Edges[bendEdge].Layout, bendS), t.PointAt(-0.95f, 0f)) : "no track";
            n = w.Track.Network;
            bool still = n.Joints.Count == switches.Count;
            for (int k = 0; still && k < switches.Count; k++) still = new Vector2(n.Joints[k].Position.x - switches[k].x, n.Joints[k].Position.z - switches[k].z).magnitude < 0.001f;
            n.Nearest(t.PointAt(-0.95f, 0f), out _, out _, out float reached, 0.3f);
            bool bendStd = GridConforms(n, out string whyBend, out _);
            Check(bent == null && still && reached < 0.03f && bendStd && JointsTrue(n, out _),
                $"Gripping a loop and pulling it out bends it on the grid: standard track, its switches where they were ({bent ?? $"{reached * 100f:0.0} cm from the pull"}{whyBend})");
            // Three trains share it.
            w.PlaceTrain(t.PointAt(-0.5f, 0.35f), TrainKind.Express, Vector3.right, out var express);
            w.PlaceTrain(t.PointAt(0.85f, 0f), TrainKind.Passenger, Vector3.forward, out var steam);
            w.PlaceTrain(t.PointAt(0f, 1.6f), TrainKind.Tank, Vector3.right, out var tank);
            float leftTime = 0f, rightTime = 0f, topTime = 0f;
            int reversals0 = 0; foreach (var tr in w.Trains.Trains) reversals0 += tr.Reversals;
            var (gap, off) = RunTrains(w, 240f, step =>
            {
                foreach (var tr in w.Trains.Trains)
                {
                    var c = w.Trains.CentrePosition(tr);
                    if (c.z > 0.75f) topTime += Dt; else if (c.x < -0.15f) leftTime += Dt; else if (c.x > 0.15f) rightTime += Dt;
                }
            });
            int reversals = -reversals0; foreach (var tr in w.Trains.Trains) reversals += tr.Reversals;
            Check(express != null && steam != null && tank != null && gap > 0.005f && off < 0.006f && leftTime > 5f && rightTime > 5f,
                $"Three trains run the grid railway through its switches without touching or leaving the rails (closest {gap * 100f:0.0} cm, worst {off * 1000f:0.0} mm off; {leftTime:0} s left, {rightTime:0} s right, {topTime:0} s top; {reversals} reversals)");
            // Saving and loading keeps every point and every switch.
            string json = w.Serialize();
            var copy = new GameObject("Grid copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(660f, 0f, 0f);
            copy.Deserialize(json, Materials());
            var cn = copy.Track.Network;
            bool same = cn.Edges.Count == n.Edges.Count && cn.Joints.Count == n.Joints.Count && copy.Trains.Trains.Count == w.Trains.Trains.Count;
            float drift = 0f;
            for (int e = 0; same && e < n.Edges.Count; e++)
            {
                var a = n.Edges[e].Layout.Points; var b = cn.Edges[e].Layout.Points;
                if (a.Count != b.Count || n.Edges[e].Closed != cn.Edges[e].Closed || n.Edges[e].Joint[0] != cn.Edges[e].Joint[0] || n.Edges[e].Joint[1] != cn.Edges[e].Joint[1]) { same = false; break; }
                for (int i = 0; i < a.Count; i++) drift = Mathf.Max(drift, new Vector2(a[i].x - b[i].x, a[i].z - b[i].z).magnitude);
            }
            for (int j = 0; same && j < n.Joints.Count; j++)
            {
                var ja = n.Joints[j]; var jb = cn.Joints[j];
                same = ja.Trunk == jb.Trunk && ja.Ends.Count == jb.Ends.Count;
                foreach (var e in ja.Ends) same &= jb.Ends.Contains(e);
            }
            bool copyStd = GridConforms(cn, out string whyCopy, out _);
            Check(same && drift < 1e-5f && copyStd && JointsTrue(cn, out _),
                $"Save and load keep every piece, point and switch of the grid railway ({cn.Edges.Count} pieces, {cn.Joints.Count} switches, {drift * 1000f:0.000} mm drift{whyCopy})");
            UnityEngine.Object.DestroyImmediate(copy.gameObject);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Railways drawn before the grid: grid track joins them; real saved worlds load and run as they were.</summary>
        private static void GridOldWorlds()
        {
            {
                var w = Scratch("Grid old", 680f);
                var t = w.Terrain;
                BuildGrid.Snap = false;
                var old = GridBuild(w, HandDrawnLoop(t, new Vector2(0f, 0f), new Vector2(0.45f, 0.32f)));
                BuildGrid.Snap = true;
                var branch = GridBuild(w, Line(t, new Vector2(0.46f, 0.02f), new Vector2(0.6f, 0.2f), new Vector2(0.65f, 0.6f)));
                var n = w.Track.Network;
                var across = GridBuild(w, Line(t, new Vector2(-0.7f, -0.6f), new Vector2(-0.2f, -0.1f), new Vector2(0.2f, 0.5f)));
                n = w.Track.Network;
                bool sound0 = JointsTrue(n, out string why);
                Check(old.Ok && branch.Ok && branch.Start.Kind == AttachKind.Mid && across.Ok && sound0 && n.Crossings.Count >= 1,
                    $"A railway drawn freely before the grid takes a grid branch (a switch on its curve) and a grid line across it ({n.Joints.Count} switch, {n.Crossings.Count} crossings{why}{old.Error}{branch.Error}{across.Error})");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            // Real players' worlds (pulled from the headset; not in git): they load with their railway intact and its trains run.
            int worlds = 0, sound = 0;
            var notes = new List<string>();
            if (Directory.Exists("Tests/Saves"))
                foreach (var file in Directory.GetFiles("Tests/Saves", "*.json"))
                {
                    var w = new GameObject("Saved world").AddComponent<WonderlandWorld>();
                    w.transform.position = new Vector3(700f + worlds * 20f, 0f, 0f);
                    try
                    {
                        w.Deserialize(File.ReadAllText(file), Materials());
                        worlds++;
                        var n = w.Track.Network;
                        var (_, off) = RunTrains(w, 10f);
                        bool ok = JointsTrue(n, out string why) && off < 0.006f;
                        if (ok) sound++;
                        notes.Add($"{Path.GetFileNameWithoutExtension(file)}: {n.Edges.Count} pieces, {n.Joints.Count} switches, {w.Trains.Trains.Count} trains{(ok ? "" : $", {why} {off * 1000f:0.0} mm off")}");
                    }
                    catch (Exception e) { notes.Add($"{Path.GetFileName(file)}: {e.GetType().Name}"); }
                    UnityEngine.Object.DestroyImmediate(w.gameObject);
                }
            if (worlds == 0) Report.AppendLine("INFO  No saved worlds in Tests/Saves to load");
            else Check(sound == worlds, $"Saved worlds load with their railway intact and its trains on the rails ({sound}/{worlds}: {string.Join("; ", notes)})");
        }

        /// <summary>
        /// Renders of a railway drawn on the grid (docs/renders/editor-grid-*.png): a loop with a second loop drawn round
        /// it, crossovers both ways between them, a siding with a station, a branch line and trains; from above and close
        /// to a switch and a crossover.
        /// </summary>
        public static void GridRailRenders()
        {
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), new Vector3(0f, 0.05f, -0.05f), 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            File.Delete(Path.Combine(WonderlandRenders.Output, "warmup.png"));
            bool gridWas = BuildGrid.Snap;
            BuildGrid.Snap = true;
            var w = Scratch("Grid demo", 0f);
            w.Sky.Shown = false;
            var t = w.Terrain;
            var said = new List<string>();
            void Build(string what, List<Vector3> stroke) { var p = GridBuild(w, stroke); said.Add($"{what}: {p.Error ?? "ok"}"); }
            Build("inner loop", RoundedRect(t, new Vector2(0f, 0f), new Vector2(0.6f, 0.38f), 0.22f, 0.004f, 31));
            Build("outer loop", RoundedRect(t, new Vector2(0f, 0f), new Vector2(0.71f, 0.49f), 0.33f, 0.01f, 32));
            Build("crossover out", Line(t, new Vector2(-0.3f, -0.38f), new Vector2(-0.1f, -0.42f), new Vector2(0.1f, -0.49f)));
            Build("crossover in", Line(t, new Vector2(0.3f, 0.49f), new Vector2(0.1f, 0.44f), new Vector2(-0.1f, 0.38f)));
            Build("siding", Line(t, new Vector2(0.3f, -0.49f), new Vector2(0.45f, -0.57f), new Vector2(0.8f, -0.6f)));
            Build("branch", Line(t, new Vector2(-0.71f, 0.05f), new Vector2(-0.85f, 0.2f), new Vector2(-0.95f, 0.55f), new Vector2(-0.95f, 0.8f)));
            var st = w.StationAt(t.PointAt(0f, -0.62f), 0); if (st.HasValue) w.PlaceHouse(st.Value);
            w.PlaceTrain(t.PointAt(-0.6f, 0f), TrainKind.Express, Vector3.forward, out _);
            w.PlaceTrain(t.PointAt(0.71f, 0f), TrainKind.Passenger, Vector3.forward, out _);
            w.PlaceTrain(t.PointAt(0f, 0.49f), TrainKind.Tank, Vector3.right, out _);
            for (int i = 0; i < 72 * 10; i++) w.Trains.Step(Dt);
            w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
            var c = w.transform.position;
            cam.transform.position = c + new Vector3(0f, 1.6f, -0.25f); cam.transform.LookAt(c + new Vector3(0f, 0f, 0.02f)); cam.fieldOfView = 50f;
            WonderlandRenders.Capture(cam, "editor-grid-layout.png", 1920, 1200);
            cam.transform.position = c + new Vector3(-0.05f, 0.35f, -0.95f); cam.transform.LookAt(c + new Vector3(-0.05f, 0f, -0.42f)); cam.fieldOfView = 45f;
            WonderlandRenders.Capture(cam, "editor-grid-crossover.png", 1600, 1200);
            cam.transform.position = c + new Vector3(-1.05f, 0.4f, -0.25f); cam.transform.LookAt(c + new Vector3(-0.8f, 0f, 0.2f)); cam.fieldOfView = 45f;
            WonderlandRenders.Capture(cam, "editor-grid-branch.png", 1600, 1200);
            bool standard = GridConforms(w.Track.Network, out string why, out var radii);
            Debug.Log($"Grid renders: {string.Join("; ", said)}; {w.Track.Network.Edges.Count} pieces, {w.Track.Network.Joints.Count} switches, standard {standard} {why} curves {Radii(radii)}");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
            BuildGrid.Snap = gridWas;
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>How long planning a stroke takes (the median of five, after a first run), on the grid or drawn freely.</summary>
        private static float PlanTime(List<Vector3> stroke, TerrainField t, TrackNetwork network, bool grid)
        {
            bool was = BuildGrid.Snap;
            BuildGrid.Snap = grid;
            TrackPlanner.Plan(stroke, t, network, null, true);
            var times = new List<float>();
            for (int i = 0; i < 5; i++)
            {
                var clock = Stopwatch.StartNew();
                TrackPlanner.Plan(stroke, t, network, null, true);
                times.Add((float)clock.Elapsed.TotalMilliseconds);
            }
            BuildGrid.Snap = was;
            times.Sort();
            return times[2];
        }

        /// <summary>Builds a stroke; when it can't be built, the readings the planner tried go to the log.</summary>
        private static TrackPlan GridBuild(WonderlandWorld w, List<Vector3> stroke)
        {
            TrackPlanner.GridLog = new System.Text.StringBuilder();
            var plan = w.BuildTrack(stroke);
            if (!plan.Ok) Debug.Log($"Grid track refused (\"{plan.Error}\"), start {plan.Start.Kind}, finish {plan.Finish.Kind}; readings:\n{TrackPlanner.GridLog}");
            TrackPlanner.GridLog = null;
            return plan;
        }

        /// <summary>A rounded rectangle drawn by hand (half-size, corner radius, jitter), starting at the bottom edge, ending short.</summary>
        private static List<Vector3> RoundedRect(TerrainField t, Vector2 centre, Vector2 half, float corner, float jitter, int seed)
        {
            var rng = new Rng(seed);
            var pts = new List<Vector3>();
            var outline = new List<Vector2>();
            var c = new[] { new Vector2(half.x - corner, -half.y + corner), new Vector2(half.x - corner, half.y - corner), new Vector2(-half.x + corner, half.y - corner), new Vector2(-half.x + corner, -half.y + corner) };
            outline.Add(new Vector2(0f, -half.y));
            for (int k = 0; k < 4; k++)
                for (int i = 0; i <= 8; i++)
                {
                    float a = -Mathf.PI / 2f + (k + i / 8f) * Mathf.PI / 2f;
                    outline.Add(c[k] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * corner);
                }
            outline.Add(new Vector2(-0.12f, -half.y));
            for (int i = 0; i < outline.Count - 1; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt((outline[i + 1] - outline[i]).magnitude / 0.012f));
                for (int s = 0; s < steps; s++)
                {
                    var q = centre + Vector2.Lerp(outline[i], outline[i + 1], s / (float)steps) + rng.InsideUnitCircle() * jitter;
                    pts.Add(t.PointAt(q.x, q.y));
                }
            }
            return pts;
        }

        private static bool OnGrid(Vector3 p) =>
            Mathf.Abs(p.x / BuildGrid.Size - Mathf.Round(p.x / BuildGrid.Size)) < 0.01f && Mathf.Abs(p.z / BuildGrid.Size - Mathf.Round(p.z / BuildGrid.Size)) < 0.01f;

        private static string Radii(List<float> radii) => radii.Count == 0 ? "no curves" : string.Join(", ", radii.ConvertAll(r => $"{r * 100f:0} cm"));

        /// <summary>
        /// Standard track everywhere: each stretch of even curvature is either straight - at a multiple of 45 degrees, on a
        /// grid line - or a curve of a standard radius. The radii found are listed.
        /// </summary>
        private static bool GridConforms(TrackNetwork n, out string why, out List<float> radii)
        {
            why = ""; radii = new List<float>();
            int bad = 0; string first = null;
            for (int e = 0; e < n.Edges.Count; e++)
            {
                var l = n.Edges[e].Layout; var p = l.Points; int count = p.Count; bool closed = l.Closed;
                if (count < 12) continue;
                var r = new float[count];
                Vector2 F(int i) { i = closed ? (i % count + count) % count : Mathf.Clamp(i, 0, count - 1); return new Vector2(p[i].x, p[i].z); }
                for (int i = 0; i < count; i++)
                {
                    var a = F(i - 3); var b = F(i); var c = F(i + 3);
                    float cross = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                    r[i] = cross < 1e-7f ? float.MaxValue : (b - a).magnitude * (c - b).magnitude * (c - a).magnitude / (2f * cross);
                }
                for (int i = closed ? 0 : 6; i < (closed ? count : count - 6); i++)
                {
                    bool straight = r[i] > 5f, even = true;
                    for (int k = -5; k <= 5 && even; k++)
                    {
                        float rk = r[closed ? ((i + k) % count + count) % count : i + k];
                        even = straight ? rk > 5f : rk < 5f && Mathf.Abs(rk - r[i]) < 0.02f * r[i];
                    }
                    if (!even) continue;
                    string fault = null;
                    if (straight)
                    {
                        var d = F(i + 1) - F(i - 1);
                        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                        float off45 = Mathf.Abs(Mathf.DeltaAngle(ang, Mathf.Round(ang / 45f) * 45f));
                        var q = F(i);
                        int dir = ((Mathf.RoundToInt(ang / 45f) % 4) + 4) % 4;
                        float coord = dir == 0 ? q.y : dir == 2 ? q.x : dir == 1 ? q.y - q.x : q.y + q.x;
                        float lineOff = Mathf.Abs(coord / BuildGrid.Size - Mathf.Round(coord / BuildGrid.Size)) * BuildGrid.Size;
                        if (off45 > 0.3f) fault = $"a straight at {ang:0.0} deg";
                        else if (lineOff > 0.0015f) fault = $"a straight {lineOff * 1000f:0.0} mm off the grid";
                    }
                    else
                    {
                        float best = float.MaxValue, std = 0f;
                        foreach (float g in TrackPlanner.GridRadii) if (Mathf.Abs(r[i] - g) < best) { best = Mathf.Abs(r[i] - g); std = g; }
                        if (best > 0.03f * std) fault = $"a curve of {r[i] * 100f:0.0} cm";
                        else if (!radii.Exists(x => Mathf.Abs(x - std) < 1e-4f)) radii.Add(std);
                    }
                    if (fault != null) { bad++; first ??= $"piece {e} has {fault} at ({p[i].x:0.00}, {p[i].z:0.00})"; }
                }
            }
            radii.Sort();
            why = bad == 0 ? "" : $"{first} ({bad} points); ";
            return bad == 0;
        }

        /// <summary>How far the railway lies from a drawn stroke: on average and at worst.</summary>
        private static (float mean, float far) Strays(TrackNetwork n, List<Vector3> drawn)
        {
            float sum = 0f, far = 0f;
            foreach (var p in drawn) { float d = n.DistanceTo(p); sum += d; far = Mathf.Max(far, d); }
            return (sum / Mathf.Max(1, drawn.Count), far);
        }

        /// <summary>The sharpest change of direction from one centimetre of track to the next, anywhere (degrees).</summary>
        private static float MaxKink(TrackNetwork n)
        {
            float worst = 0f;
            foreach (var e in n.Edges)
            {
                var p = e.Layout.Points;
                for (int i = 1; i + 1 < p.Count; i++)
                {
                    var a = new Vector2(p[i].x - p[i - 1].x, p[i].z - p[i - 1].z); var b = new Vector2(p[i + 1].x - p[i].x, p[i + 1].z - p[i].z);
                    if (a.magnitude < 0.004f || b.magnitude < 0.004f) continue;
                    worst = Mathf.Max(worst, Vector2.Angle(a, b) / ((a.magnitude + b.magnitude) * 0.5f / 0.01f));
                }
            }
            return worst;
        }

        /// <summary>Separate railways: groups of pieces joined by switches.</summary>
        private static int Components(TrackNetwork n)
        {
            var parent = new int[n.Edges.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            foreach (var j in n.Joints) for (int k = 1; k < j.Ends.Count; k++) parent[Find(j.Ends[k].Edge)] = Find(j.Ends[0].Edge);
            int c = 0;
            for (int i = 0; i < parent.Length; i++) if (Find(i) == i) c++;
            return c;
        }
    }
}
