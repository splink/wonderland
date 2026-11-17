using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// The railway as a player builds it, stroke by stroke: a loop drawn in two halves, branches drawn at every angle
        /// from every side of a loop, two loops joined, a line ended on another track, a dead end extended; the
        /// network afterwards (no stray dead ends, every switch with three ends), a train exploring every branch,
        /// automatic signals, wagons and saving.
        /// </summary>
        private static void RailJourney()
        {
            // 1. A loop in two strokes: half, then the other half back to the start.
            {
                var w = Scratch("Two halves", 350f);
                var t = w.Terrain;
                var first = new List<Vector3>(); var second = new List<Vector3>();
                for (int i = 0; i <= 60; i++) { float a = i / 60f * Mathf.PI; first.Add(t.PointAt(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.38f)); }
                for (int i = 0; i <= 60; i++) { float a = Mathf.PI + i / 60f * Mathf.PI; second.Add(t.PointAt(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.38f)); }
                var p1 = w.BuildTrack(first); var p2 = w.BuildTrack(second);
                var n = w.Track.Network;
                Check(p1.Ok && p2.Ok && DeadEnds(n) == 0, $"A loop drawn in two halves closes ({n.Edges.Count} pieces, {DeadEnds(n)} dead ends{(p2.Ok ? "" : ": \"" + p2.Error + "\"")})");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            // 2. Branches at every angle from every side of a loop (planned only, not built): what works, what is refused and why.
            {
                var w = Scratch("Branch angles", 360f);
                var t = w.Terrain;
                w.BuildTrack(Loop(t, 0.42f, 0.32f, 0.97f, false));
                var n = w.Track.Network;
                var reasons = new Dictionary<string, int>();
                int ok = 0, tried = 0;
                var failedAngles = new StringBuilder();
                for (int side = 0; side < 8; side++)
                {
                    float a0 = side / 8f * Mathf.PI * 2f + 0.2f;
                    var on = new Vector2(Mathf.Cos(a0) * 0.42f, Mathf.Sin(a0) * 0.32f);
                    var tangent = new Vector2(-Mathf.Sin(a0) * 0.42f, Mathf.Cos(a0) * 0.32f).normalized;
                    var outward = new Vector2(tangent.y, -tangent.x);
                    if (Vector2.Dot(outward, on) < 0f) outward = -outward;
                    foreach (float angle in new[] { 20f, 35f, 50f, 65f, 80f, 90f })
                    {
                        var dir = (Vector2)(Quaternion.Euler(0f, 0f, -angle) * tangent);
                        if (Vector2.Dot(dir, outward) < 0f) dir = (Vector2)(Quaternion.Euler(0f, 0f, angle) * tangent);
                        var end = on + dir * 0.45f;
                        if (end.magnitude > 0.85f) continue;
                        tried++;
                        var plan = TrackPlanner.Plan(Line(t, on, on + dir * 0.2f, end), t, n);
                        if (plan.Ok && plan.Start.Kind == AttachKind.Mid) ok++;
                        else
                        {
                            string why = plan.Ok ? "did not attach" : plan.Error;
                            reasons[why] = (reasons.TryGetValue(why, out int c) ? c : 0) + 1;
                            failedAngles.Append($"{angle:0} ");
                        }
                    }
                }
                Debug.Log("Branch matrix refusals: " + string.Join("; ", reasons) + " at angles " + failedAngles);
                Check(ok >= tried * 0.9f, $"Branches drawn off a loop at any angle from 20 to 90 degrees are built ({ok}/{tried}; refused: {string.Join("; ", reasons)})");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            // 3. Two loops side by side, joined by a connector drawn from one to the other.
            {
                var w = Scratch("Two loops", 370f);
                var t = w.Terrain;
                var left = new List<Vector3>(); var right = new List<Vector3>();
                for (int i = 0; i <= 120; i++) { float a = i / 120f * Mathf.PI * 2f * 0.97f; left.Add(t.PointAt(-0.45f + Mathf.Cos(a) * 0.3f, Mathf.Sin(a) * 0.3f)); right.Add(t.PointAt(0.45f + Mathf.Cos(a) * 0.3f, Mathf.Sin(a) * 0.3f)); }
                var l = w.BuildTrack(left); var r = w.BuildTrack(right);
                var n = w.Track.Network;
                // A connector arcing over the top, from the left loop to the right one.
                var connector = w.BuildTrack(Line(t, new Vector2(-0.38f, 0.29f), new Vector2(-0.2f, 0.45f), new Vector2(0f, 0.52f), new Vector2(0.2f, 0.45f), new Vector2(0.38f, 0.29f)));
                Check(l.Ok && r.Ok && connector.Ok && n.Joints.Count == 2 && DeadEnds(n) == 0,
                    $"Two loops are joined by a line drawn from one to the other ({n.Joints.Count} switches, {DeadEnds(n)} dead ends{(connector.Ok ? "" : ": \"" + connector.Error + "\"")})");
                // A straight, short connector between the facing sides: either built, or refused with a reason the player can act on.
                var shortcut = TrackPlanner.Plan(Line(t, new Vector2(-0.16f, -0.05f), new Vector2(0.16f, -0.05f)), t, n);
                Check(shortcut.Ok || !string.IsNullOrEmpty(shortcut.Error), $"A connector with no room for its curves is refused with a reason (\"{shortcut.Error ?? "built"}\")");
                // One way: one link lets trains across but not back - a trap, until a second link goes the other way.
                int trapped = n.Traps;
                var back = w.BuildTrack(Line(t, new Vector2(0.38f, -0.29f), new Vector2(0.2f, -0.45f), new Vector2(0f, -0.52f), new Vector2(-0.2f, -0.45f), new Vector2(-0.38f, -0.29f)));
                n = w.Track.Network;
                Check(n.OneWay && trapped == 1 && back.Ok && n.Traps == 0 && n.Circuits >= 1,
                    $"The railway runs one way: one link between loops is a one-way trap ({trapped}), a second link the other way frees it ({n.Traps} left{(back.Ok ? "" : ": \"" + back.Error + "\"")})");
                // Trains on the joined network visit both loops.
                PlaceTrainAt(w, n.Edges[0].Layout.Points[n.Edges[0].Layout.Points.Count / 3]);
                var tr = FirstTrain(w);
                bool leftSide = false, rightSide = false;
                for (int k = 0; k < 72 * 240; k++)
                {
                    w.Trains.Step(Dt);
                    if (k % 36 != 0 || tr == null) continue;
                    var c = w.Trains.CentrePosition(tr);
                    leftSide |= c.x < -0.5f; rightSide |= c.x > 0.5f;
                }
                Check(leftSide && rightSide, "A train on the joined network runs through the switches into both loops");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            // 4. A line ended on the middle of a track (a T), and a dead end extended.
            {
                var w = Scratch("Joins", 380f);
                var t = w.Terrain;
                w.BuildTrack(Loop(t, 0.42f, 0.32f, 0.97f, false));
                var n = w.Track.Network;
                var tee = w.BuildTrack(Line(t, new Vector2(0.9f, -0.5f), new Vector2(0.75f, -0.15f), new Vector2(0.44f, 0.04f)));
                int deadAfterTee = DeadEnds(n);
                var extend = w.BuildTrack(Line(t, n.Edges.Count > 1 ? FreeEnd(n) : Vector2.zero, new Vector2(0.9f, -0.75f)));
                Check(tee.Ok && n.Joints.Count == 1 && deadAfterTee == 1, $"A line drawn onto a track joins it with a switch ({n.Joints.Count} switch{(tee.Ok ? "" : ": \"" + tee.Error + "\"")})");
                Check(extend.Ok && DeadEnds(n) == 1 && n.Joints.Count == 1, $"Drawing on from a dead end extends the line (still {DeadEnds(n)} dead end{(extend.Ok ? "" : ": \"" + extend.Error + "\"")})");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            // 5. A line drawn from a dead end back onto its own piece (a "6"): the switch joins the right ends, and trains
            // run round it without leaving the rails. A save with the old wrong switch is repaired when it loads.
            {
                var w = Scratch("Six", 385f);
                var t = w.Terrain;
                var first = w.BuildTrack(Line(t, new Vector2(-0.7f, -0.3f), new Vector2(0f, -0.3f), new Vector2(0.55f, -0.3f)));
                var n = w.Track.Network;
                // Back onto itself facing the wrong way: trains would have to reverse - refused, saying so.
                var wrongWay = w.BuildTrack(Line(t, new Vector2(0.55f, -0.3f), new Vector2(0.7f, 0f), new Vector2(0.45f, 0.3f), new Vector2(0.05f, 0.25f), new Vector2(-0.15f, -0.05f), new Vector2(-0.2f, -0.3f)));
                Check(!wrongWay.Ok && wrongWay.Error == TrackNetwork.TurnBack, $"A hook joining its own line the wrong way is refused: \"{wrongWay.Error ?? "built"}\"");
                var hook = w.BuildTrack(Line(t, new Vector2(0.55f, -0.3f), new Vector2(0.75f, -0.1f), new Vector2(0.6f, 0.25f), new Vector2(0.1f, 0.3f), new Vector2(-0.4f, 0.2f), new Vector2(-0.5f, -0.05f), new Vector2(-0.35f, -0.25f), new Vector2(-0.2f, -0.3f)));
                Check(first.Ok && hook.Ok, $"A line, then a hook from its end back onto itself the way it runs ({hook.Start.Kind} -> {hook.Finish.Kind})" + (hook.Ok ? "" : $" (\"{hook.Error}\")"));
                Check(JointsTrue(n, out string why), $"Every switch joins the track ends that are really there, and no piece jumps ({n.Joints.Count} switches{why})");
                w.PlaceTrain(t.PointAt(-0.5f, -0.3f), TrainKind.Passenger, Vector3.right, out var train);
                var (_, off) = RunTrains(w, 90f);
                Check(train != null && off < 0.006f && train.Odometer > 3f, $"A train runs round the hook and back without leaving the rails (worst {off * 100f:0.0} cm over {(train != null ? train.Odometer : 0f):0.0} m)");
                // The old bug, as saved: a switch listing the far end of its own piece instead of the hook's end.
                var data = n.ToData();
                int jointIndex = -1, wrong = -1;
                for (int j = 0; j < data.joints.Count && jointIndex < 0; j++)
                    for (int k = 0; k < data.joints[j].ends.Length; k++)
                    {
                        int code = data.joints[j].ends[k], edge = code / 2, end = code % 2;
                        if (data.edges[edge].closed) continue;
                        int other = edge * 2 + (1 - end);
                        if (Array.IndexOf(data.joints[j].ends, other) < 0) { jointIndex = j; wrong = k; data.joints[j].ends[k] = other; if (end == 0) data.edges[edge].joint0 = -1; else data.edges[edge].joint1 = -1; break; }
                    }
                var broken = TrackNetwork.FromData(data);
                // ...and the other form of it: a piece with a jump inside (the far end merged on).
                var jumped = n.ToData();
                int longest = 0; for (int e = 1; e < jumped.edges.Count; e++) if (jumped.edges[e].points.Count > jumped.edges[longest].points.Count) longest = e;
                var gapPts = jumped.edges[longest].points; gapPts.RemoveRange(gapPts.Count / 3, gapPts.Count / 3);
                var cut = TrackNetwork.FromData(jumped);
                Check(JointsTrue(cut, out string cutWhy), $"...and a piece with a jump in it is cut at the jump{cutWhy}");
                string healed = "";
                Check(jointIndex >= 0 && JointsTrue(broken, out healed), $"A railway saved with a wrongly joined switch is repaired when loaded{healed}");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            // 6. An old railway that turns back on itself (a lead into a loop, joined the wrong way, from before the railway
            // ran one way) must not stop anything else being built: it stays two-way, the rest runs one way.
            {
                var w = Scratch("Old two-way", 386f);
                var t = w.Terrain;
                var data = new TrackNetwork.Data();
                var lead = Line(t, new Vector2(-0.75f, -0.3f), new Vector2(-0.1f, -0.3f));
                var loop = new List<Vector3>();
                for (int i = 0; i <= 160; i++) { float a = Mathf.PI + i / 160f * Mathf.PI * 2f; loop.Add(t.PointAt(0.17f + Mathf.Cos(a) * 0.27f, -0.3f + Mathf.Sin(a) * 0.27f)); }
                loop[0] = lead[^1]; loop[^1] = lead[^1];
                data.edges.Add(new TrackNetwork.EdgeData { points = lead, joint1 = 0 });
                data.edges.Add(new TrackNetwork.EdgeData { points = loop, joint0 = 0, joint1 = 0 });
                data.joints.Add(new TrackNetwork.JointData { position = lead[^1], ends = new[] { 0 * 2 + 1, 1 * 2 + 0, 1 * 2 + 1 } });   // the lead is the trunk: trains would have to reverse
                w.Track.SetNetwork(TrackNetwork.FromData(data));
                var n = w.Track.Network;
                int oldParts = n.TwoWayParts;
                var loop2 = new List<Vector3>();
                for (int i = 0; i <= 90; i++) { float a = i / 90f * Mathf.PI * 2f * 0.98f; loop2.Add(t.PointAt(-0.35f + Mathf.Cos(a) * 0.3f, 0.45f + Mathf.Sin(a) * 0.22f)); }
                var preview = w.PreviewTrack(loop2, out _, out _);
                var built = w.BuildTrack(loop2);
                n = w.Track.Network;
                Check(oldParts == 1 && preview.Ok && built.Ok, $"An old two-way railway in the world doesn't stop a new loop being built (preview {preview.Error ?? "ok"}, built {built.Error ?? "ok"})");
                var branch = w.BuildTrack(Line(t, new Vector2(-0.55f, -0.3f), new Vector2(-0.4f, -0.4f), new Vector2(-0.3f, -0.62f)));
                Check(branch.Ok, $"...nor a branch off the old part ({branch.Error ?? "built"})");
                bool arrowsOnNew = false, arrowsOnOld = true;
                for (int e = 0; e < n.Edges.Count; e++) { if (n.Edges[e].Closed && n.Directed(e)) arrowsOnNew = true; }
                n = w.Track.Network;
                for (int e = 0; e < n.Edges.Count; e++) if (!n.Directed(e)) arrowsOnOld = false;
                Check(arrowsOnNew && arrowsOnOld && n.TwoWayParts == 1, $"Every piece has a direction - the new loop and the old part too (its wrong-way branch is never taken); the old part is still counted as one that can't run one way everywhere ({n.TwoWayParts})");
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            RailOperations();
            LoopJoinProbe();
            OneWayJourney();
        }

        /// <summary>Every switch lists three track ends, each of which ends at the switch.</summary>
        private static bool JointsTrue(TrackNetwork n, out string why)
        {
            why = "";
            for (int e = 0; e < n.Edges.Count; e++)
            {
                var pts = n.Edges[e].Layout.Points;
                for (int i = 1; i < pts.Count; i++)
                    if (new Vector2(pts[i].x - pts[i - 1].x, pts[i].z - pts[i - 1].z).magnitude > 0.03f) { why = $": piece {e} jumps {(pts[i] - pts[i - 1]).magnitude * 100f:0} cm"; return false; }
            }
            for (int j = 0; j < n.Joints.Count; j++)
            {
                if (n.Joints[j].Ends.Count != 3) { why = $": switch {j} has {n.Joints[j].Ends.Count} ends"; return false; }
                foreach (var e in n.Joints[j].Ends)
                {
                    var p = n.EndPoint(e);
                    float d = new Vector2(p.x - n.Joints[j].Position.x, p.z - n.Joints[j].Position.z).magnitude;
                    if (d > 0.005f || n.Edges[e.Edge].Joint[e.End] != j) { why = $": switch {j} lists an end {d * 100f:0.0} cm away"; return false; }
                }
            }
            return true;
        }

        /// <summary>Signals, carriages, several trains and saving, on a loop with a branch line.</summary>
        private static void RailOperations()
        {
            var w = Scratch("Operations", 390f);
            var t = w.Terrain;
            w.BuildTrack(Loop(t, 0.5f, 0.38f, 0.97f, false));
            var from = new Vector2(0.52f, 0.02f);
            var branch = w.BuildTrack(Line(t, from, from + new Vector2(0.12f, 0.2f), from + new Vector2(0.32f, 0.48f)));
            var n = w.Track.Network;
            string sig = w.PlaceSignal(t.PointAt(-0.58f, 0f));
            var st = w.StationAt(t.PointAt(0f, -0.3f), 0);
            string station = st.HasValue ? w.PlaceHouse(st.Value) : "no railway";
            PlaceTrainAt(w, t.PointAt(0f, 0.38f));
            w.PlaceTrain(t.PointAt(0f, -0.38f), TrainKind.Goods, Vector3.left, out _);
            Check(branch.Ok && sig == null && station == null && w.Trains.Trains.Count == 2, "A loop with a branch, a signal, a station and two trains" + (sig ?? station ?? ""));
            // Carriages: couple two more on, past the most, and take some off; the engine stays where it is.
            var steam = FirstTrain(w);
            float before = steam.Length;
            var loco = w.Trains.LocoPosition(steam);
            string more = w.Trains.SetCars(steam, 4);
            string tooMany = w.Trains.SetCars(steam, 9);
            float after = steam.Length;
            bool stayed = (w.Trains.LocoPosition(steam) - loco).magnitude < 0.01f;
            Check(more == null && TrainSystem.CarsOf(steam) == 4 && after > before + 0.05f && stayed && tooMany != null,
                $"Carriages couple on behind the engine ({before * 100f:0} -> {after * 100f:0} cm, engine still); more than the most is refused (\"{tooMany}\")");
            var goods = w.Trains.Trains[1];
            string fewer = w.Trains.SetCars(goods, 1);
            Check(fewer == null && TrainSystem.CarsOf(goods) == 1, $"Wagons uncouple ({w.Trains.Describe(goods)})");
            // Running: automatic signal goes red as trains pass and green again; no two trains ever touch.
            w.Trains.Mode = SwitchMode.Explore;
            bool sawRed = false, sawGreen = false, branchVisited = false;
            int branchEdge = -1;
            for (int e = 0; e < n.Edges.Count; e++) if (!n.Edges[e].Closed && (n.Edges[e].Joint[0] < 0 || n.Edges[e].Joint[1] < 0)) branchEdge = e;
            var (gap, off) = RunTrains(w, 150f, step =>
            {
                if (step % 18 != 0) return;
                if (w.Trains.SignalGreen(0)) sawGreen = true; else sawRed = true;
                foreach (var tr in w.Trains.Trains) if (OnEdge(tr, branchEdge)) branchVisited = true;
            });
            Check(sawRed && sawGreen, "The automatic signal turns red as a train passes and green once the line is clear");
            Check(gap > 0.005f && off < 0.006f && !branchVisited, $"Two trains share the network without touching (closest {gap * 100f:0.0} cm), and keep off the dead-end branch (one way, they couldn't come back out)");
            Check(w.Trains.Describe(steam).Contains("Steam train, 4 carriages"), $"Pointing at a train says what it is doing (\"{w.Trains.Describe(steam)}\")");
            // Holding the signal at red stops the next train at it; the description says why.
            w.ToggleSignal(0);
            RunTrains(w, 40f);
            bool waiting = false; string why = "";
            foreach (var tr in w.Trains.Trains) if (tr.Blocked == BlockReason.Signal && tr.Speed < 0.004f) { waiting = true; why = w.Trains.Describe(tr); }
            Check(waiting && why.Contains("red signal"), $"Held at red, the signal stops a train, and the train says so (\"{why}\")");
            // Save and load: the same network, signals, station, trains and their carriages.
            string json = w.Serialize();
            var copy = new GameObject("Operations copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(400f, 0f, 0f);
            copy.Deserialize(json, Materials());
            var cn = copy.Track.Network;
            bool same = cn.Edges.Count == n.Edges.Count && cn.Joints.Count == n.Joints.Count && DeadEnds(cn) == DeadEnds(n) && copy.Trains.Stations.Count == w.Trains.Stations.Count
                && copy.Track.Signals.Count == 1 && copy.Track.Signals[0].red && copy.Trains.Trains.Count == 2
                && TrainSystem.CarsOf(copy.Trains.Trains[0]) == 4 && TrainSystem.CarsOf(copy.Trains.Trains[1]) == 1;
            Check(same, $"Save and load keep the network ({cn.Edges.Count} pieces, {cn.Joints.Count} switch), the held signal, the station ({copy.Trains.StationName(0)}) and each train's carriages");
            UnityEngine.Object.DestroyImmediate(copy.gameObject);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        private static Vector2 FreeEnd(TrackNetwork n)
        {
            foreach (var e in n.Edges)
                if (!e.Closed)
                    for (int k = 0; k < 2; k++)
                        if (e.Joint[k] < 0) { var p = k == 0 ? e.Layout.Points[0] : e.Layout.Points[^1]; return new Vector2(p.x, p.z); }
            return Vector2.zero;
        }

        /// <summary>Just the railway scenarios: Logs/rail-results.txt.</summary>
        public static void TestRail()
        {
            Report.Clear(); failures = 0;
            try { RailJourney(); }
            catch (Exception e) { Fail("Exception: " + e); }
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            System.IO.File.WriteAllText("Logs/rail-results.txt", Report.ToString());
            Debug.Log("Wonderland rail test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>
        /// The one-way railway in use: two loops linked both ways, an express, a tank engine and a steam train, a station
        /// and a country halt. Every train runs the way the arrows go (none ever reverses), they call at the halt too, and
        /// they share both loops instead of piling into one.
        /// </summary>
        private static void OneWayJourney()
        {
            var w = Scratch("One way", 395f);
            var t = w.Terrain;
            var left = new List<Vector3>(); var right = new List<Vector3>();
            for (int i = 0; i <= 120; i++) { float a = i / 120f * Mathf.PI * 2f * 0.97f; left.Add(t.PointAt(-0.45f + Mathf.Cos(a) * 0.3f, Mathf.Sin(a) * 0.3f)); right.Add(t.PointAt(0.45f + Mathf.Cos(a) * 0.3f, Mathf.Sin(a) * 0.3f)); }
            w.BuildTrack(left); w.BuildTrack(right);
            w.BuildTrack(Line(t, new Vector2(-0.38f, 0.29f), new Vector2(-0.2f, 0.45f), new Vector2(0f, 0.52f), new Vector2(0.2f, 0.45f), new Vector2(0.38f, 0.29f)));
            w.BuildTrack(Line(t, new Vector2(0.38f, -0.29f), new Vector2(0.2f, -0.45f), new Vector2(0f, -0.52f), new Vector2(-0.2f, -0.45f), new Vector2(-0.38f, -0.29f)));
            var n = w.Track.Network;
            var st = w.StationAt(t.PointAt(-0.45f, -0.42f), 0); string stErr = st.HasValue ? w.PlaceHouse(st.Value) : "no spot";
            var halt = w.StationAt(t.PointAt(0.45f, 0.42f), 0, Catalog.Halt); string haltErr = halt.HasValue ? w.PlaceHouse(halt.Value) : "no spot";
            Check(stErr == null && haltErr == null && w.Trains.Stations.Count == 2, $"A station and a country halt beside the line ({w.Trains.Stations.Count} stops{(haltErr != null ? ", halt: " + haltErr : "")})");
            w.PlaceTrain(t.PointAt(-0.75f, 0f), TrainKind.Express, Vector3.back, out var express);
            w.PlaceTrain(t.PointAt(0.75f, 0f), TrainKind.Tank, Vector3.back, out var tank);
            w.PlaceTrain(t.PointAt(-0.15f, -0.3f), TrainKind.Passenger, Vector3.left, out var steam);
            Check(express != null && tank != null && steam != null && express.SpeedFactor > 1.1f && tank.SpeedFactor < 0.9f,
                $"An express (faster) and a tank engine (slower) join the steam train ({(express != null ? w.Trains.Describe(express) : "no express")}; {(tank != null ? w.Trains.Describe(tank) : "no tank engine")})");
            int halts = 0; float leftTime = 0f, rightTime = 0f;
            int reversals0 = 0; foreach (var tr in w.Trains.Trains) reversals0 += tr.Reversals;
            int haltIndex = w.Trains.Stations.FindIndex(s => (s.House - new Vector2(halt?.x ?? 0f, halt?.z ?? 0f)).sqrMagnitude < 1e-6f);
            int lastAt = -1;
            var (gap, off) = RunTrains(w, 300f, step =>
            {
                foreach (var tr in w.Trains.Trains)
                {
                    var c = w.Trains.CentrePosition(tr);
                    if (c.x < -0.2f) leftTime += Dt; else if (c.x > 0.2f) rightTime += Dt;
                    if (tr.AtStation == haltIndex && haltIndex >= 0 && lastAt != step / 72) { halts++; lastAt = step / 72; }
                }
            });
            int reversals = -reversals0; foreach (var tr in w.Trains.Trains) reversals += tr.Reversals;
            float share = Mathf.Min(leftTime, rightTime) / Mathf.Max(0.001f, leftTime + rightTime);
            Check(reversals == 0 && gap > 0.005f, $"Every train runs one way round the network, never backing up or meeting another (closest {gap * 100f:0.0} cm, {reversals} reversals)");
            Check(share > 0.25f, $"Trains share both loops rather than piling into one ({share * 100f:0}% of train-time in the quieter loop)");
            Check(halts > 0, $"Trains call at the country halt ({halts} calls)");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>
        /// How often joining two separate loops works, drawn the way a person would: from somewhere on one loop to somewhere
        /// on the other, straight or with a wobble, at many angles. Counts successes and the reasons for refusals.
        /// </summary>
        private static void LoopJoinProbe()
        {
            var reasons = new Dictionary<string, int>();
            int ok = 0, tried = 0, surprises = 0;
            var rng = new Rng(77);
            for (int trial = 0; trial < 60; trial++)
            {
                var w = Scratch("Join probe", 520f + trial * 0.0f);
                var t = w.Terrain;
                float gap = rng.Range(0.15f, 0.5f);
                var c1 = new Vector2(-0.45f - gap * 0.5f, 0f); var c2 = new Vector2(0.45f + gap * 0.5f, rng.Range(-0.2f, 0.2f));
                w.BuildTrack(HandDrawnLoop(t, c1, new Vector2(0.4f, 0.3f)));
                w.BuildTrack(HandDrawnLoop(t, c2, new Vector2(0.38f, 0.32f)));
                // From a point on loop 1's near side to a point on loop 2's near side.
                float a1 = rng.Range(-1.2f, 1.2f), a2 = Mathf.PI + rng.Range(-1.2f, 1.2f);
                var p1 = c1 + new Vector2(Mathf.Cos(a1) * 0.4f, Mathf.Sin(a1) * 0.3f);
                var p2 = c2 + new Vector2(Mathf.Cos(a2) * 0.38f, Mathf.Sin(a2) * 0.32f);
                var mid = (p1 + p2) * 0.5f + new Vector2(0f, rng.Range(-0.15f, 0.15f));
                var stroke = Line(t, p1, mid, p2);
                for (int k = 0; k < stroke.Count; k++) stroke[k] += new Vector3(rng.Range(-0.004f, 0.004f), 0f, rng.Range(-0.004f, 0.004f));
                tried++;
                var preview = w.PreviewTrack(stroke, out _, out _);
                var plan = w.BuildTrack(stroke);
                if (preview.Ok != plan.Ok) { surprises++; Debug.Log($"PREVIEW MISMATCH: preview {preview.Error ?? "ok"} vs built {plan.Error ?? "ok"}"); }
                if (plan.Ok) ok++;
                else { reasons.TryGetValue(plan.Error, out int n); reasons[plan.Error] = n + 1; }
                UnityEngine.Object.DestroyImmediate(w.gameObject);
            }
            var sb = new StringBuilder();
            foreach (var kv in reasons) sb.Append($"; {kv.Value}x \"{kv.Key}\"");
            Report.AppendLine($"INFO  Joining two loops: {ok}/{tried} built{sb}");
            Check(surprises == 0, $"What the drawing shows while the trigger is held is what letting go builds ({tried - surprises}/{tried} agree)");
            Check(ok >= tried * 0.85f, $"Most hand-drawn links between two loops can be built ({ok}/{tried})");
        }

        /// <summary>
        /// Renders of the one-way railway (docs/renders/editor-rail-*.png): two loops linked both ways with arrows on the
        /// track, an express, a tank engine and a steam train, a station and a country halt; and the live plan while
        /// drawing - green with arrows when it can be built, red when not.
        /// </summary>
        public static void RailRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), new Vector3(0f, 0.05f, -0.05f), 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = Scratch("Rail demo", 0f);
            w.Sky.Shown = false;
            var t = w.Terrain;
            var left = new List<Vector3>(); var right = new List<Vector3>();
            for (int i = 0; i <= 120; i++) { float a = i / 120f * Mathf.PI * 2f * 0.97f; left.Add(t.PointAt(-0.45f + Mathf.Cos(a) * 0.3f, Mathf.Sin(a) * 0.3f)); right.Add(t.PointAt(0.45f + Mathf.Cos(a) * 0.3f, Mathf.Sin(a) * 0.3f)); }
            w.BuildTrack(left); w.BuildTrack(right);
            w.BuildTrack(Line(t, new Vector2(-0.38f, 0.29f), new Vector2(-0.2f, 0.45f), new Vector2(0f, 0.52f), new Vector2(0.2f, 0.45f), new Vector2(0.38f, 0.29f)));
            w.BuildTrack(Line(t, new Vector2(0.38f, -0.29f), new Vector2(0.2f, -0.45f), new Vector2(0f, -0.52f), new Vector2(-0.2f, -0.45f), new Vector2(-0.38f, -0.29f)));
            var st = w.StationAt(t.PointAt(-0.45f, -0.42f), 0); if (st.HasValue) w.PlaceHouse(st.Value);
            var halt = w.StationAt(t.PointAt(0.45f, 0.42f), 0, Catalog.Halt); if (halt.HasValue) w.PlaceHouse(halt.Value);
            w.PlaceTrain(t.PointAt(-0.75f, 0f), TrainKind.Express, Vector3.forward, out _);
            w.PlaceTrain(t.PointAt(0.75f, 0f), TrainKind.Tank, Vector3.forward, out _);
            w.PlaceTrain(t.PointAt(-0.15f, -0.3f), TrainKind.Passenger, Vector3.right, out _);
            for (int i = 0; i < 72 * 12; i++) w.Trains.Step(Dt);
            w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
            var c = w.transform.position;
            cam.transform.position = c + new Vector3(0f, 1.25f, -0.9f); cam.transform.LookAt(c); cam.fieldOfView = 46f;
            WonderlandRenders.Capture(cam, "editor-rail-oneway.png", 1920, 1200);
            var express = w.Trains.Trains[0];
            var at = w.Trains.LocoPosition(express);
            cam.transform.position = c + at + new Vector3(0.18f, 0.12f, 0.2f); cam.transform.LookAt(c + at + Vector3.up * 0.02f); cam.fieldOfView = 40f;
            WonderlandRenders.Capture(cam, "editor-rail-express.png", 1600, 1200);
            var tank = w.Trains.Trains[1];
            at = w.Trains.LocoPosition(tank);
            cam.transform.position = c + at + new Vector3(-0.2f, 0.12f, -0.15f); cam.transform.LookAt(c + at + Vector3.up * 0.02f); cam.fieldOfView = 40f;
            WonderlandRenders.Capture(cam, "editor-rail-tank-halt.png", 1600, 1200);
            // The live plan: a good branch (green, with arrows) and a wrong-way join (red, with the reason).
            var good = Line(t, new Vector2(-0.6f, 0.26f), new Vector2(-0.7f, 0.5f), new Vector2(-0.5f, 0.75f));
            var plan = w.PreviewTrack(good, out int dir, out _);
            var line = new List<Vector3>();
            if (plan.Path != null) foreach (var q in plan.Path) line.Add(new Vector3(q.x, t.SurfaceAt(q.x, q.y) + 0.004f, q.y));
            w.Track.ShowPlan(line, !plan.Ok, dir, null, null);
            cam.transform.position = c + new Vector3(-0.55f, 0.55f, -0.05f); cam.transform.LookAt(c + new Vector3(-0.55f, 0f, 0.45f)); cam.fieldOfView = 44f;
            WonderlandRenders.Capture(cam, "editor-rail-plan.png", 1600, 1200);
            Debug.Log($"Rail renders: plan {(plan.Ok ? "ok" : plan.Error)} dir {dir}; one way {w.Track.Network.OneWay}, traps {w.Track.Network.Traps}; {string.Join(", ", w.Trains.Trains.ConvertAll(x => w.Trains.Describe(x)))}");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
