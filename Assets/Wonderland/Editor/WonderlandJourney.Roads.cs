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
            Object.DestroyImmediate(w.gameObject);
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
            Debug.Log($"Road renders: {w.Paths.Lines.Count} roads, {w.Paths.Junctions.Count} junctions, {w.Traffic.Vehicles.Count} vehicles, {w.Traffic.LightStops} stops at lights, {w.Traffic.RedRuns} red runs");
            Object.DestroyImmediate(w.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
