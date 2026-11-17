using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// Roads: a wobbly stroke comes out straight, corners rounded; roads crossing make a crossroads with traffic lights,
        /// a road ending on another a T-junction; a highway has its own lanes. Cars and lorries put on the roads drive
        /// about the town, keep to the road, keep their distance and never cross a stop line on red. They are saved
        /// with the world and can be erased.
        /// </summary>
        private static void RoadJourney()
        {
            bool gridWas = BuildGrid.Snap;
            BuildGrid.Snap = true;
            var w = Scratch("Roads", 500f);
            var t = w.Terrain;
            var rng = new Rng(5);
            List<Vector3> Wobbly(params Vector2[] via)
            {
                var line = Line(t, via);
                for (int k = 0; k < line.Count; k++) line[k] += new Vector3(rng.Range(-0.008f, 0.008f), 0f, rng.Range(-0.008f, 0.008f));
                return line;
            }
            // A straight road from a wobbly stroke.
            string e1 = w.BuildPath(Wobbly(new Vector2(-0.75f, 0f), new Vector2(0.75f, 0f)), PathKind.Road);
            var main = w.Paths.Lines[^1];
            float worst = 0f;
            foreach (var p in main.points) worst = Mathf.Max(worst, Mathf.Abs(p.y - Mathf.Lerp(main.points[0].y, main.points[^1].y, Mathf.InverseLerp(main.points[0].x, main.points[^1].x, p.x))));
            Check(e1 == null && worst < 0.004f, $"A road drawn by hand comes out straight (at most {worst * 1000f:0.0} mm off the line)");
            // A road with a corner: rounded.
            string e2 = w.BuildPath(Wobbly(new Vector2(-0.6f, -0.6f), new Vector2(-0.6f, -0.25f), new Vector2(-0.2f, -0.25f)), PathKind.Road);
            var bend = w.Paths.Lines[^1];
            var coarse = TrackPlanner.Resample(bend.points, 0.02f, false);
            float radius = TrackPlanner.MinRadiusOf(coarse, false);
            Check(e2 == null && radius > 0.03f && radius < 0.2f, $"A corner is rounded off ({radius * 100f:0} cm radius)");
            // A crossroads, and a T-junction.
            string e3 = w.BuildPath(Wobbly(new Vector2(0.1f, -0.6f), new Vector2(0.1f, 0.5f)), PathKind.Road);
            string e4 = w.BuildPath(Wobbly(new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.005f)), PathKind.Road);
            var junctions = w.Paths.Junctions;
            int cross = junctions.FindIndex(j => j.Arms.Count == 4), tee = junctions.FindIndex(j => j.Arms.Count == 3);
            Check(e3 == null && e4 == null && cross >= 0 && tee >= 0, $"Roads crossing make a crossroads, a road ending on another a T-junction ({junctions.Count} junctions: {string.Join(", ", junctions.ConvertAll(j => j.Arms.Count + " arms"))})");
            Check(w.Paths.LightHeads.Count == 7, $"Traffic lights on every arm of both junctions ({w.Paths.LightHeads.Count})");
            // A highway.
            string e5 = w.BuildPath(Wobbly(new Vector2(-0.6f, 0.66f), new Vector2(0.6f, 0.66f)), PathKind.Highway);
            Check(e5 == null && w.Paths.Lines[^1].kind == (int)PathKind.Highway, "A highway can be drawn" + (e5 == null ? "" : $" ({e5})"));
            // Cars and lorries.
            w.Traffic.Refresh(true);
            var spots = new[] { new Vector2(-0.6f, 0f), new Vector2(-0.3f, 0f), new Vector2(0.3f, 0.01f), new Vector2(0.1f, -0.4f), new Vector2(0.1f, 0.35f), new Vector2(0.5f, 0.3f), new Vector2(-0.6f, -0.45f) };
            var kinds = new[] { VehicleKind.Car, VehicleKind.Taxi, VehicleKind.Van, VehicleKind.Bus, VehicleKind.Truck, VehicleKind.Tanker, VehicleKind.Car };
            int placed = 0; string why = null;
            for (int k = 0; k < spots.Length; k++) { string err = w.PlaceVehicle(new Vector3(spots[k].x, 0f, spots[k].y), kinds[k], k * 90f, k); if (err == null) placed++; else why = err; }
            Check(placed == spots.Length, $"Cars, a taxi, a van, a bus, a lorry and a tanker go on the roads ({placed}{(why != null ? ", " + why : "")})");
            var mine = w.Traffic.Vehicles.FindAll(v => v.Roams);
            var start = mine.ConvertAll(v => v.Position);
            var travelled = new float[mine.Count];
            float offRoad = 0f, closest = float.MaxValue; string pair = "";
            for (float s = 0f; s < 180f; s += Dt)
            {
                var before = mine.ConvertAll(v => v.Position);
                w.Clock.Tick(Dt); w.Traffic.Step(Dt);
                for (int k = 0; k < mine.Count; k++)
                {
                    travelled[k] += (mine[k].Position - before[k]).magnitude;
                    offRoad = Mathf.Max(offRoad, w.Traffic.OffRoad(mine[k].Position));
                    for (int m = k + 1; m < mine.Count; m++)
                    {
                        float d = (mine[k].Position - mine[m].Position).magnitude;
                        string Of(TrafficSystem.Vehicle x) => $"{x.Kind}#{x.Id} {x.State} {x.Speed * 100f:0.0} cm/s heading {x.Heading:0} push {x.Pushing:0.0} wait {x.Waiting:0.0} queue {x.Queue} light {x.AtLight} next {x.Next}/{x.Path.Count} at ({x.Position.x:0.000}, {x.Position.y:0.000})";
                        if (d < 0.015f && closest >= 0.015f) pair = $"first at {s:0.0} s: {Of(mine[k])} / {Of(mine[m])}";
                        closest = Mathf.Min(closest, d);
                    }
                }
            }
            float least = float.MaxValue; foreach (float d in travelled) least = Mathf.Min(least, d);
            Check(least > 0.5f, $"Every one of them drives about the town (the least went {least * 100f:0} cm in 3 minutes)");
            Check(w.Traffic.RedRuns == 0 && w.Traffic.LightStops > 0, $"They stop at red lights and never cross on red ({w.Traffic.LightStops} stops at lights, {w.Traffic.RedRuns} red crossings{(w.Traffic.RedRuns > 0 ? ": " + w.Traffic.LastRedRun : "")})");
            Check(offRoad < 0.05f, $"They keep to the roads (at most {offRoad * 100f:0.0} cm from a road's centre line)");
            Check(closest > 0.015f, $"They keep their distance (closest {closest * 100f:0.0} cm between two{(closest <= 0.015f ? ": " + pair : "")})");
            // Saved with the world; the eraser takes one away and Undo brings it back.
            var copy = new GameObject("Roads copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(510f, 0f, 0f);
            copy.Deserialize(w.Serialize(), Materials());
            int kept = copy.Traffic.Vehicles.FindAll(v => v.Roams).Count;
            Object.DestroyImmediate(copy.gameObject);
            Check(kept == placed, $"The vehicles are saved with the world ({kept})");
            var victim = mine[0].Position;
            int before2 = w.Traffic.Vehicles.FindAll(v => v.Roams).Count;
            string erased = w.EraseAt(new Vector3(victim.x, 1f, victim.y), Vector3.down, new Vector3(victim.x, t.SurfaceAt(victim.x, victim.y), victim.y));
            int after = w.Traffic.Vehicles.FindAll(v => v.Roams).Count;
            w.Undo();
            int back = w.Traffic.Vehicles.FindAll(v => v.Roams).Count;
            Check(after == before2 - 1 && back == before2, $"The eraser takes a vehicle away (\"{erased}\") and Undo brings it back");
            // The grid: an askew stroke comes out straight and on the grid; a building beside it faces it; off, as drawn.
            string e6 = w.BuildPath(Wobbly(new Vector2(-0.7f, 0.33f), new Vector2(-0.25f, 0.355f)), PathKind.Road);
            var gridRoad = w.Paths.Lines[^1];
            float skew = 0f; foreach (var p in gridRoad.points) skew = Mathf.Max(skew, Mathf.Abs(p.y - gridRoad.points[0].y));
            bool onGrid = Mathf.Abs(gridRoad.points[0].y / BuildGrid.Size - Mathf.Round(gridRoad.points[0].y / BuildGrid.Size)) < 0.01f;
            Check(e6 == null && skew < 0.0005f && onGrid, $"On the grid an askew stroke comes out straight, on a grid line ({skew * 1000f:0.0} mm off straight)");
            bool beside = w.Paths.AlongRoad(new Vector2(-0.45f, 0.42f), Catalog.Get(Catalog.Bakery).Half.y, out var at, out float yaw);
            var front = new Vector2(-Mathf.Sin(yaw * Mathf.Deg2Rad), -Mathf.Cos(yaw * Mathf.Deg2Rad));   // (a building's front is its -Z)
            Check(beside && Vector2.Dot(front, new Vector2(at.x, 0.35f) - at) > 0.07f && at.y > 0.36f && at.y < 0.45f, $"A building beside it faces the road from just behind the kerb (facing {yaw:0}°, at {at.y * 100f:0.0} cm)");
            w.Undo();
            BuildGrid.Snap = false;
            string e7 = w.BuildPath(Wobbly(new Vector2(-0.7f, 0.33f), new Vector2(-0.25f, 0.355f)), PathKind.Road);
            var free = w.Paths.Lines[^1];
            Check(e7 == null && Mathf.Abs(free.points[^1].y - free.points[0].y) > 0.015f, "With the grid off the stroke keeps its angle");
            BuildGrid.Snap = gridWas;
            Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Where the villagers' and animals' time goes on a saved world (Logs/simprofile-results.txt).</summary>
        public static void SimProfile()
        {
            var w = new GameObject("Profile").AddComponent<WonderlandWorld>();
            w.Deserialize(System.IO.File.ReadAllText(System.Environment.GetEnvironmentVariable("WL_SAVE") ?? "Tests/Saves/headset-wonderland-world.json"), Materials());
            w.People.Grid.Complete();
            // A player at the near edge of the land, looking across it (what is behind them or beyond 3 m thinks coarsely).
            var land = w.Terrain.LandBounds();
            var eye = new GameObject("Player") { tag = "MainCamera" }.AddComponent<Camera>();
            eye.transform.position = w.transform.TransformPoint(new Vector3(land.center.x, 0.6f, land.yMin - 0.4f));
            eye.transform.LookAt(w.transform.TransformPoint(new Vector3(land.center.x, 0f, land.center.y)));
            for (float s = 0f; s < 10f; s += Dt) { w.Clock.Tick(Dt); w.People.Step(Dt); w.Animals.Step(Dt); w.Trains.Step(Dt); }
            var sb = new System.Text.StringBuilder($"a player at the near edge of {land.width:0.0} x {land.height:0.0} m of land, looking across it\n");
            foreach (int mode in new[] { 1, 0 })
            {
                PerfSettings.Villagers.Value = mode;
                var watch = new System.Diagnostics.Stopwatch();
                double people = 0, animals = 0, trains = 0; int frames = 0;
                for (float s = 0f; s < 20f; s += Dt)
                {
                    w.Clock.Tick(Dt);
                    watch.Restart(); w.People.Step(Dt); people += watch.Elapsed.TotalMilliseconds;
                    watch.Restart(); w.Animals.Step(Dt); animals += watch.Elapsed.TotalMilliseconds;
                    watch.Restart(); w.Trains.Step(Dt); trains += watch.Elapsed.TotalMilliseconds;
                    frames++;
                }
                double f = frames;
                sb.AppendLine($"{w.People.People.Count} people, {w.Animals.Animals.Count} animals, {w.Trains.Trains.Count} trains; per frame: people {people / f:0.000} ms, animals {animals / f:0.000} ms, trains {trains / f:0.000} ms ({PerfSettings.Villagers.Label})");
                sb.AppendLine("  " + w.People.Diagnostics);
            }
            PerfSettings.ResetAll();
            Object.DestroyImmediate(eye.gameObject);
            System.IO.File.WriteAllText("Logs/simprofile-results.txt", sb.ToString());
            Debug.Log(sb.ToString());
            Object.DestroyImmediate(w.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Following villagers on saved worlds (Logs/follow-results.txt, docs/renders/editor-follow-*.png): someone on
        /// foot, someone indoors, someone on a train, someone driving - each followed for a while, the camera checked
        /// (at their eyes on foot, never under the ground, never inside another building) and its view saved.
        /// </summary>
        public static void FollowRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            var report = new System.Text.StringBuilder();
            int failures = 0;
            void Expect(bool ok, string what) { report.AppendLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) failures++; }
            var saves = (System.Environment.GetEnvironmentVariable("WL_SAVES") ?? "Tests/Saves/headset-wonderland-world.json").Split(';');
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var save in saves)
            {
                var w = new GameObject("Follow").AddComponent<WonderlandWorld>();
                w.Deserialize(System.IO.File.ReadAllText(save), Materials());
                w.Sky.Shown = false;
                w.People.Grid.Complete();
                w.Clock.Hour = 9f;
                void Step(float seconds)
                {
                    for (float s2 = 0f; s2 < seconds; s2 += 1f / 30f)
                    {
                        float dt = 1f / 30f;
                        w.Clock.Tick(dt); w.People.Step(dt); w.Animals.Step(dt); w.Trains.Step(dt); w.Traffic.Step(dt); w.Boats.Step(dt); w.Cctv.Step(dt);
                    }
                }
                Step(20f);
                report.AppendLine($"--- {System.IO.Path.GetFileName(save)}: {w.People.People.Count} people, {w.Trains.Trains.Count} trains, {w.Traffic.Vehicles.Count} vehicles");
                // Find someone in each situation (simulating a little while until they turn up), follow them, check, picture.
                var wants = new (string view, System.Func<int, bool> who)[]
                {
                    ("over their shoulder", i => !w.People.IsHidden(i) && w.People.StateOf(i) == PeopleSystem.Activity.Walking),
                    ("outside", i => w.People.IsHidden(i) && w.People.StateOf(i) == PeopleSystem.Activity.Inside),
                    ("from the train window", i => w.People.StateOf(i) == PeopleSystem.Activity.Riding),
                    ("from the car", i => w.Traffic.DrivenBy(w.People.People[i].seed) is { State: TrafficSystem.Mode.Driving }),
                };
                foreach (var (view, who) in wants)
                {
                    int found = -1;
                    for (int tries = 0; tries < 80 && found < 0; tries++)
                    {
                        for (int i = 0; i < w.People.People.Count; i++) if (who(i)) { found = i; break; }
                        if (found < 0) Step(15f);
                    }
                    if (found < 0) { report.AppendLine($"INFO  nobody {view} within 20 minutes"); continue; }
                    w.Cctv.FollowSeed = w.People.People[found].seed;
                    float worstGround = float.MaxValue, worstEyes = 0f; bool inside = false; var views = new System.Collections.Generic.List<string>();
                    for (int k = 0; k < 90; k++)
                    {
                        Step(1f / 3f);
                        var e = w.Cctv.FollowEye;
                        if (w.Cctv.FollowView == null) break;
                        if (views.Count == 0 || views[^1] != w.Cctv.FollowView) views.Add(w.Cctv.FollowView);
                        if (!(w.Cctv.FollowView == "from the train window" && w.Terrain.SurfaceAt(e.x, e.z) > e.y)) worstGround = Mathf.Min(worstGround, e.y - w.Terrain.SurfaceAt(e.x, e.z));
                        int idx = w.People.IndexOfSeed(w.Cctv.FollowSeed);
                        if (idx >= 0 && w.Cctv.FollowView == "over their shoulder")
                        {
                            var head = w.People.PovOf(idx).Eye;
                            worstEyes = Mathf.Max(worstEyes, (e - head).magnitude);
                        }
                        if (w.Cctv.FollowView.StartsWith("outside")) for (int j = 0; j < w.Buildings.Houses.Count; j++)
                        {
                            var h = w.Buildings.Houses[j]; var bp = Catalog.Get(h.variant);
                            var q = Quaternion.Inverse(Quaternion.Euler(0f, h.rotation, 0f)) * new Vector3(e.x - h.x, 0f, e.z - h.z);
                            if (Mathf.Abs(q.x) < bp.Half.x && Mathf.Abs(q.z) < bp.Half.y && e.y < w.Terrain.SurfaceAt(h.x, h.z) + bp.Height * 0.9f) inside = true;
                        }
                        if (k == 30 && seen.Add(view))
                        {
                            w.Cctv.FilmNow();   // (the viewer picture: the followed villager's)
                            var tex = w.Cctv.Snapshot();
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(WonderlandRenders.Output, $"editor-follow-{view.Split(' ')[^1].Replace("eyes", "shoulder")}.png"), tex.EncodeToPNG());
                        }
                    }
                    string name = w.People.NameOf(found);
                    Expect(views.Count > 0 && worstGround > 0.002f && worstEyes < 0.025f && !inside,
                        $"Following {name} ({view}): views {string.Join(" > ", views)}; lowest {worstGround * 1000f:0} mm above ground, camera within {worstEyes * 1000f:0.0} mm of their eyes, inside a building {inside}");
                }
                w.Cctv.FollowSeed = 0;
                Object.DestroyImmediate(w.gameObject);
            }
            report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            System.IO.File.WriteAllText("Logs/follow-results.txt", report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// How villagers walk on saved worlds (Logs/walkprobe-results.txt): people bumping into each other, standing in
        /// cars, spinning on the spot - two minutes each, with the walking checks before and after this change.
        /// </summary>
        public static void WalkProbe()
        {
            var report = new System.Text.StringBuilder();
            var saves = (System.Environment.GetEnvironmentVariable("WL_SAVES") ?? "Tests/Saves/headset-wonderland-world.json").Split(';');
            PeopleSystem.Diagnose = true;
            foreach (bool legacy in new[] { true, false })
            foreach (var save in saves)
            {
                PeopleSystem.Legacy = legacy;
                var w = new GameObject("Walk").AddComponent<WonderlandWorld>();
                w.Deserialize(System.IO.File.ReadAllText(save), Materials());
                w.People.Grid.Complete();
                w.Clock.Hour = 10f;
                long touching = 0, inCars = 0; int frames = 0;
                w.People.Spins = 0;
                for (float s2 = 0f; s2 < 120f; s2 += 1f / 30f)
                {
                    float dt = 1f / 30f;
                    w.Clock.Tick(dt); w.People.Step(dt); w.Animals.Step(dt); w.Trains.Step(dt); w.Traffic.Step(dt);
                    if (s2 > 10f) { var (t1, c1) = w.People.Crowding(); touching += t1; inCars += c1; frames++; }
                }
                if (!legacy) foreach (var note in w.People.SpinNotes) report.AppendLine("    spin: " + note);
                report.AppendLine($"{(legacy ? "before" : "after ")} {System.IO.Path.GetFileName(save)}: {w.People.People.Count} people, {w.Traffic.Vehicles.Count} vehicles; per frame {touching / (float)frames:0.00} pairs walking within 5 mm, {inCars / (float)frames:0.00} people inside a car; {w.People.Spins} spins in 110 s");
                Object.DestroyImmediate(w.gameObject);
            }
            PeopleSystem.Diagnose = false; PeopleSystem.Legacy = false;
            System.IO.File.WriteAllText("Logs/walkprobe-results.txt", report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Trains on saved worlds (Logs/trainflow-results.txt): five minutes running - every train always the way the
        /// arrows point (no reversal, engine first), how far each went, the longest wait, and the closest two trains came.
        /// </summary>
        public static void TrainFlow()
        {
            var report = new System.Text.StringBuilder();
            var saves = (System.Environment.GetEnvironmentVariable("WL_SAVES") ?? "Tests/Saves/headset-wonderland-world.json").Split(';');
            int failures = 0;
            foreach (var save in saves)
            {
                var w = new GameObject("Trains").AddComponent<WonderlandWorld>();
                w.Deserialize(System.IO.File.ReadAllText(save), Materials());
                var net = w.Track.Network;
                foreach (var tr in w.Trains.Trains) tr.Running = true;
                var trains = w.Trains.Trains;
                var longest = new float[trains.Count]; var waited = new float[trains.Count];
                float closest = float.MaxValue, closestSide = float.MaxValue; Vector3 sideAt = Vector3.zero;
                bool against = false;
                for (float s2 = 0f; s2 < 300f; s2 += 1f / 30f)
                {
                    w.Clock.Tick(1f / 30f); w.Trains.Step(1f / 30f);
                    for (int i = 0; i < trains.Count; i++)
                    {
                        waited[i] = trains[i].Speed < 0.002f && trains[i].Dwell <= 0f ? waited[i] + 1f / 30f : 0f;
                        longest[i] = Mathf.Max(longest[i], waited[i]);
                        if (trains[i].Dir < 0) against = true;
                        for (int k = i + 1; k < trains.Count; k++)
                        {
                            float d = w.Trains.Between(trains[i], trains[k], out bool side, out var at);
                            closest = Mathf.Min(closest, d);
                            if (side && d < closestSide) { closestSide = d; sideAt = at; }
                        }
                    }
                }
                int reversals = 0; foreach (var tr in trains) reversals += tr.Reversals;
                // Every switch trains came to facing: which way they went, and why.
                for (int ji = 0; ji < net.Joints.Count; ji++)
                {
                    var j = net.Joints[ji];
                    if (j == null || j.Ends.Count != 3) continue;
                    var sb = new System.Text.StringBuilder();
                    foreach (var kv in w.Trains.SwitchLog) if (kv.Key.joint == ji) sb.Append($" {kv.Value}x branch {kv.Key.branch} ({kv.Key.why});");
                    string Info(int k)
                    {
                        var b = j.Branch(k);
                        return $"branch {k}: {(net.Through(b.Edge) ? "through" : "dead end/spur")}, circuit {net.CircuitOf(b.Edge)}";
                    }
                    report.AppendLine($"  switch {ji}{(j.Pinned ? " PINNED" : "")} at ({j.Position.x:0.00}, {j.Position.z:0.00}): trunk circuit {net.CircuitOf(j.Trunk.Edge)}; {Info(0)}; {Info(1)};{(sb.Length > 0 ? sb.ToString() : " no train came facing")}");
                }
                report.AppendLine($"  switch mode {w.Trains.Mode}");
                report.AppendLine($"--- {System.IO.Path.GetFileName(save)}: {net.Edges.Count} pieces, {net.Joints.Count} switches, {trains.Count} trains; parts that can't run one way everywhere: {net.TwoWayParts}, traps {net.Traps}");
                for (int i = 0; i < trains.Count; i++) report.AppendLine($"  {w.Trains.Describe(trains[i]),-60} {trains[i].Odometer,6:0.0} m, longest wait {longest[i],5:0} s");
                bool ok = reversals == 0 && !against;
                if (!ok) failures++;
                report.AppendLine($"  {(ok ? "PASS" : "FAIL")}  no train reversed or ran engine-last ({reversals} reversals); closest two trains came: {closest * 100f:0.0} cm; side by side: {closestSide * 100f:0.0} cm at ({sideAt.x:0.00}, {sideAt.z:0.00}){Near(w, sideAt)}");
                Object.DestroyImmediate(w.gameObject);
            }
            report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            System.IO.File.WriteAllText("Logs/trainflow-results.txt", report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static string Near(WonderlandWorld w, Vector3 p)
        {
            var net = w.Track.Network;
            float bestJ = float.MaxValue; foreach (var j in net.Joints) if (j != null) bestJ = Mathf.Min(bestJ, (j.Position - p).magnitude);
            float bestC = float.MaxValue; foreach (var c in net.Crossings) bestC = Mathf.Min(bestC, (c.Position - p).magnitude);
            return $" ({bestJ * 100f:0} cm from a switch, {bestC * 100f:0} cm from a crossing)";
        }

        /// <summary>
        /// Connections, old shaping vs new (Logs/trackshape-results.txt): branches off a loop, links between two loops,
        /// extensions - hand-drawn with a wobble - and how many S-bends the built pieces have.
        /// </summary>
        private static readonly List<string> WiggleWhere = new();

        /// <summary>Six connections drawn by hand onto loops, old shaping (left) and new (right), seen from above.</summary>
        public static void TrackShapeRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 2f, 0f), Vector3.zero, 40f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            foreach (bool old in new[] { true, false })
            {
                TrackPlanner.OldShaping = old;
                for (int trial = 0; trial < 6; trial++)
                {
                    var w = Scratch("Shape " + trial, 800f + trial * 4f + (old ? 0f : 40f));
                    w.Sky.Shown = false;
                    var t = w.Terrain;
                    var rng = new Rng(100 + trial);
                    w.BuildTrack(Loop(t, 0.42f, 0.3f, 0.97f, false));
                    float a0 = rng.Range(0f, Mathf.PI * 2f);
                    var start = new Vector2(Mathf.Cos(a0) * 0.42f, Mathf.Sin(a0) * 0.3f);
                    var out1 = start * rng.Range(1.6f, 2.2f) + new Vector2(rng.Range(-0.2f, 0.2f), rng.Range(-0.2f, 0.2f));
                    var stroke = new List<Vector3>();
                    for (int i = 0; i <= 40; i++)
                    {
                        float u = i / 40f;
                        var q = Vector2.Lerp(start, out1, u) + new Vector2(rng.Range(-0.006f, 0.006f), rng.Range(-0.006f, 0.006f));
                        stroke.Add(t.PointAt(q.x, q.y));
                    }
                    w.BuildTrack(stroke);
                    var c = w.transform.TransformPoint(new Vector3(start.x, 0f, start.y));
                    cam.transform.position = c + new Vector3(0f, 0.55f, 0f); cam.transform.LookAt(c); cam.fieldOfView = 40f;
                    WonderlandRenders.Capture(cam, $"editor-trackshape-{trial}-{(old ? "old" : "new")}.png", 600, 600);
                    Object.DestroyImmediate(w.gameObject);
                }
            }
            TrackPlanner.OldShaping = false;
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void TrackShape()
        {
            var report = new System.Text.StringBuilder();
            foreach (bool old in new[] { true, false })
            {
                TrackPlanner.OldShaping = old;
                TrackPlanner.WiggleAt.Clear(); WiggleWhere.Clear();
                int built = 0, wiggles = 0, pieces = 0; float excess = 0f;
                for (int trial = 0; trial < 24; trial++)
                {
                    var w = Scratch("Shapes", 700f);
                    var t = w.Terrain;
                    var rng = new Rng(100 + trial);
                    w.BuildTrack(Loop(t, 0.42f, 0.3f, 0.97f, false));
                    if (trial % 3 == 1)
                    {
                        var second = new List<Vector3>();
                        for (int i = 0; i <= 90; i++) { float a = i / 90f * Mathf.PI * 2f * 0.97f; second.Add(t.PointAt(Mathf.Cos(a) * 0.3f, 0.95f + Mathf.Sin(a) * 0.22f)); }
                        w.BuildTrack(second);
                    }
                    int before = w.Track.Network.Edges.Count;
                    var stroke = new List<Vector3>();
                    float a0 = rng.Range(0f, Mathf.PI * 2f);
                    var start = new Vector2(Mathf.Cos(a0) * 0.42f, Mathf.Sin(a0) * 0.3f);
                    var out1 = start * rng.Range(1.6f, 2.2f) + new Vector2(rng.Range(-0.2f, 0.2f), rng.Range(-0.2f, 0.2f));
                    var end = trial % 3 == 1 ? new Vector2(rng.Range(-0.2f, 0.2f), 0.7f) : out1;
                    for (int i = 0; i <= 40; i++)
                    {
                        float u = i / 40f;
                        var q = Vector2.Lerp(Vector2.Lerp(start, out1, u), Vector2.Lerp(out1, end, u), u) + new Vector2(rng.Range(-0.006f, 0.006f), rng.Range(-0.006f, 0.006f));
                        stroke.Add(t.PointAt(q.x, q.y));
                    }
                    var r = w.BuildTrack(stroke);
                    if (r.Ok)
                    {
                        built++;
                        var net = w.Track.Network;
                        for (int e = 0; e < net.Edges.Count; e++)
                        {
                            if (net.Edges[e].Closed) continue;
                            if (net.Edges[e].Layout.Length > 1.6f) continue;   // (the new connector, not the loops)
                            int seen = TrackPlanner.WiggleAt.Count;
                            var (wg, ex) = TrackPlanner.Wiggles(net.Edges[e].Layout.Points); wiggles += wg; excess += ex; pieces++;
                            bool isNew = net.Edges[e].Joint[0] < 0 || net.Edges[e].Joint[1] < 0 || trial % 3 == 1 && net.Edges[e].Layout.Length < 1.2f;
                            for (int k = seen; k < TrackPlanner.WiggleAt.Count; k++)
                            {
                                bool atStart = TrackPlanner.WiggleAt[k] >= 0f;
                                bool switchEnd = net.Edges[e].Joint[atStart ? 0 : 1] >= 0;
                                WiggleWhere.Add((isNew ? "new piece" : "old track") + (switchEnd ? " by the switch" : " by its free end"));
                                TrackPlanner.WiggleAt[k] = Mathf.Abs(TrackPlanner.WiggleAt[k]);
                            }
                        }
                    }
                    Object.DestroyImmediate(w.gameObject);
                }
                report.AppendLine($"{(old ? "old shaping" : "new shaping")}: {built}/24 built; {wiggles} wiggles on {pieces} new pieces; turning beyond what they need: {excess / Mathf.Max(1, pieces):0} degrees each; wiggles at {string.Join(", ", TrackPlanner.WiggleAt.ConvertAll(x => (x * 100f).ToString("0")))} cm from an end, on: {string.Join(", ", WiggleWhere)}");
            }
            TrackPlanner.OldShaping = false;
            // One branch off a straight line, in detail: the bend of the first 10 cm, planned and as built.
            {
                var w = Scratch("Branch", 700f);
                var t = w.Terrain;
                w.BuildTrack(Line(t, new Vector2(-0.8f, 0f), new Vector2(0.8f, 0f)));
                var stroke = new List<Vector3>();
                for (int i = 0; i <= 30; i++) { float u = i / 30f; stroke.Add(t.PointAt(-0.2f + u * 0.5f, u * u * 0.45f)); }
                var plan = TrackPlanner.Plan(stroke, t, w.Track.Network);
                string Bends(IReadOnlyList<Vector3> pts, bool fromJoint)
                {
                    var q = new List<Vector2>(); foreach (var v in pts) q.Add(new Vector2(v.x, v.z));
                    if (!fromJoint) q.Reverse();
                    q = TrackPlanner.Resample(q, 0.01f, false);
                    var sb = new System.Text.StringBuilder();
                    for (int i = 1; i + 1 < q.Count && i < 12; i++) sb.Append($"{Vector2.SignedAngle(q[i] - q[i - 1], q[i + 1] - q[i]):0.0} ");
                    return sb.ToString();
                }
                if (plan.Path != null) report.AppendLine($"branch planned, first cm bends (deg/cm): {Bends(plan.Path.ConvertAll(v => new Vector3(v.x, 0f, v.y)), true)}");
                var r = w.BuildTrack(stroke);
                var net = w.Track.Network;
                for (int e = 0; e < net.Edges.Count; e++)
                {
                    var l = net.Edges[e].Layout;
                    bool fromJoint = net.Edges[e].Joint[0] >= 0;
                    report.AppendLine($"piece {e} ({l.Length:0.00} m, joints {net.Edges[e].Joint[0]}/{net.Edges[e].Joint[1]}): first cm bends from its switch end: {Bends(l.Points, fromJoint)}");
                }
                Object.DestroyImmediate(w.gameObject);
            }
            System.IO.File.WriteAllText("Logs/trackshape-results.txt", report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>The player's world in each terrain shading mode (Full, Light, Simple), from where they would stand.</summary>
        public static void TerrainModeRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(8f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 0.8f, -1.1f), Vector3.zero, 50f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = new GameObject("Terrain modes").AddComponent<WonderlandWorld>();
            w.Deserialize(System.IO.File.ReadAllText(System.Environment.GetEnvironmentVariable("WL_SAVE") ?? "Tests/Saves/headset-wonderland-world.json"), Materials());
            w.Sky.Shown = false;
            w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
            var land = w.Terrain.LandBounds();
            var c = new Vector3(land.center.x, 0.05f, land.center.y);
            cam.transform.position = c + new Vector3(0.1f, 0.75f, -0.95f); cam.transform.LookAt(c + new Vector3(0f, 0f, 0.15f));
            string[] names = { "full", "light", "simple" };
            for (int m = 0; m < 3; m++)
            {
                TerrainNoise.SetMode(m);
                WonderlandRenders.Capture(cam, $"editor-terrain-{names[m]}.png", 1600, 1000);
            }
            cam.transform.position = c + new Vector3(0.25f, 0.18f, -0.3f); cam.transform.LookAt(c + new Vector3(0.1f, 0f, 0.05f));
            for (int m = 0; m < 3; m++)
            {
                TerrainNoise.SetMode(m);
                WonderlandRenders.Capture(cam, $"editor-terrain-{names[m]}-close.png", 1600, 1000);
            }
            // Distance haze: off, soft (the default), strong - from where a player would stand, the far side a few metres off.
            TerrainNoise.SetMode(1);
            cam.transform.position = c + new Vector3(0.0f, 0.55f, -1.25f); cam.transform.LookAt(c + new Vector3(0f, 0f, 0.6f));
            foreach (var (strength, name) in new[] { (0f, "off"), (0.35f, "soft"), (0.6f, "strong") })
            {
                Shader.SetGlobalVector("_WL_Haze", new Vector4(1.2f, 2.8f, strength, 0f));
                WonderlandRenders.Capture(cam, $"editor-haze-{name}.png", 1600, 1000);
            }
            Shader.SetGlobalVector("_WL_Haze", Vector4.zero);
            // Villagers and animals drawn together (instanced) and one by one: the pictures should match.
            for (int i = 0; i < 30; i++) { w.Clock.Tick(Dt); w.People.Step(Dt); w.Animals.Step(Dt); }
            cam.transform.position = c + new Vector3(0.15f, 0.25f, -0.45f); cam.transform.LookAt(c + new Vector3(0.05f, 0f, 0.1f));
            foreach (var (mode, name) in new[] { (0, "together"), (1, "one-by-one") })
            {
                PerfSettings.Figures.Value = mode;
                w.People.SendMessage("LateUpdate"); w.Animals.SendMessage("LateUpdate");
                WonderlandRenders.Capture(cam, $"editor-figures-{name}.png", 1600, 1000);
            }
            PerfSettings.ResetAll();
            TerrainNoise.SetMode(0);
            Object.DestroyImmediate(w.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Renders of the roads: a crossroads with its lights and traffic, a highway, the vehicles close up.</summary>
        public static void RoadRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), new Vector3(0f, 0.05f, -0.05f), 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = Scratch("Road demo", 0f);
            w.Sky.Shown = false;
            var t = w.Terrain;
            var rng = new Rng(9);
            List<Vector3> Wobbly(params Vector2[] via)
            {
                var line = Line(t, via);
                for (int k = 0; k < line.Count; k++) line[k] += new Vector3(rng.Range(-0.008f, 0.008f), 0f, rng.Range(-0.008f, 0.008f));
                return line;
            }
            w.BuildPath(Wobbly(new Vector2(-0.8f, 0f), new Vector2(0.8f, 0f)), PathKind.Road);
            w.BuildPath(Wobbly(new Vector2(-0.75f, 0.56f), new Vector2(0.75f, 0.56f)), PathKind.Highway);
            w.BuildPath(Wobbly(new Vector2(0.05f, -0.65f), new Vector2(0.05f, 0.555f)), PathKind.Road);
            w.BuildPath(Wobbly(new Vector2(-0.5f, -0.6f), new Vector2(-0.5f, -0.3f), new Vector2(-0.1f, -0.3f)), PathKind.Road);
            foreach (var (id, x, z, rot) in new[] { (Catalog.Bakery, -0.3f, -0.15f, 180f), (Catalog.Cafe, -0.15f, 0.14f, 0f), (Catalog.Townhouse, 0.25f, 0.15f, 0f), (Catalog.Grocer, 0.3f, -0.16f, 180f), (Catalog.Apartments, -0.45f, 0.16f, 0f) })
                w.PlaceHouse(new HouseInstance { x = x, z = z, rotation = rot, variant = id });
            w.Traffic.Refresh(true);
            var kinds = new[] { VehicleKind.Car, VehicleKind.Taxi, VehicleKind.Van, VehicleKind.Bus, VehicleKind.Truck, VehicleKind.Tanker, VehicleKind.Car, VehicleKind.Car };
            var spots = new[] { new Vector2(-0.6f, 0f), new Vector2(-0.35f, 0f), new Vector2(0.4f, 0f), new Vector2(0.05f, -0.45f), new Vector2(-0.5f, 0.56f), new Vector2(0.3f, 0.56f), new Vector2(0.05f, 0.35f), new Vector2(0.6f, 0f) };
            for (int k = 0; k < spots.Length; k++) w.PlaceVehicle(new Vector3(spots[k].x, 0f, spots[k].y), kinds[k], k * 90f, k);
            for (float s = 0f; s < 25f; s += Dt) { w.Clock.Tick(Dt); w.Traffic.Step(Dt); }
            w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
            var c = w.transform.position;
            cam.transform.position = c + new Vector3(0.05f, 1.2f, -1.0f); cam.transform.LookAt(c + new Vector3(0f, 0f, 0.1f)); cam.fieldOfView = 46f;
            WonderlandRenders.Capture(cam, "editor-roads.png", 1920, 1200);
            cam.transform.position = c + new Vector3(0.22f, 0.2f, -0.28f); cam.transform.LookAt(c + new Vector3(0.05f, 0.01f, 0f)); cam.fieldOfView = 42f;
            WonderlandRenders.Capture(cam, "editor-roads-crossroads.png", 1920, 1200);
            cam.transform.position = c + new Vector3(-0.2f, 0.14f, 0.3f); cam.transform.LookAt(c + new Vector3(0.05f, 0.01f, 0.56f)); cam.fieldOfView = 44f;
            WonderlandRenders.Capture(cam, "editor-roads-highway.png", 1920, 1200);
            cam.transform.position = c + new Vector3(-0.3f, 0.16f, -0.52f); cam.transform.LookAt(c + new Vector3(-0.47f, 0.0f, -0.33f)); cam.fieldOfView = 40f;
            WonderlandRenders.Capture(cam, "editor-roads-bend.png", 1920, 1200);
            // The grid around the pointer, and a row of shops placed beside the road the way the Build tool places them.
            foreach (var (id, x) in new[] { (Catalog.Townhouse, -0.62f), (Catalog.Bakery, -0.48f), (Catalog.Grocer, -0.34f) })
                if (w.Paths.AlongRoad(new Vector2(x, -0.12f), Catalog.Get(id).Half.y, out var at, out float yaw))
                    w.PlaceHouse(new HouseInstance { x = at.x, z = at.y, rotation = yaw, variant = id });
            BuildGrid.Show(true);
            w.TerrainView.SetBrush(new Vector3(-0.4f, w.Terrain.SurfaceAt(-0.4f, -0.2f), -0.2f), 0.03f, new Color(0.6f, 1f, 0.5f), 0.6f);
            cam.transform.position = c + new Vector3(-0.45f, 0.3f, -0.62f); cam.transform.LookAt(c + new Vector3(-0.47f, 0.0f, -0.12f)); cam.fieldOfView = 44f;
            WonderlandRenders.Capture(cam, "editor-grid.png", 1920, 1200);
            BuildGrid.Show(false);
            Debug.Log($"Road renders: {w.Paths.Lines.Count} roads, {w.Paths.Junctions.Count} junctions, {w.Traffic.Vehicles.Count} vehicles, {w.Traffic.LightStops} stops at lights, {w.Traffic.RedRuns} red runs");
            Object.DestroyImmediate(w.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
