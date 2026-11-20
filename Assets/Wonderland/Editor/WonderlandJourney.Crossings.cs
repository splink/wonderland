using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// A street's dead end with a turning circle: tapped on, there it is (and Undo, Redo take it away and back); the
        /// cars that come to the end drive round it and back up the street, never off the road.
        /// </summary>
        private static void TurningCircleJourney()
        {
            var w = Scratch("Turning circle", 330f);
            var t = w.Terrain;
            string road = w.BuildPath(Stroke(t, new Vector2(-0.6f, 0f), new Vector2(0.3f, 0f)), PathKind.Road);
            var end = w.Paths.Lines.Count > 0 ? w.Paths.Lines[0].points[^1] : Vector2.zero;
            string said = w.ToggleTurningCircle(new Vector3(end.x, 0f, end.y));
            int circles = w.Paths.TurningCircles.Count;
            w.Undo(); int undone = w.Paths.TurningCircles.Count;
            w.Redo(); int redone = w.Paths.TurningCircles.Count;
            Check(road == null && circles == 1 && undone == 0 && redone == 1, $"A tap at a street's end puts a turning circle there; Undo and Redo take it away and back ({said}; {circles}, {undone}, {redone})");
            if (w.Paths.TurningCircles.Count == 0) { UnityEngine.Object.DestroyImmediate(w.gameObject); return; }
            var centre = w.Paths.TurningCircles[0].Centre;
            w.Traffic.Refresh(true);
            foreach (float x in new[] { -0.45f, -0.2f }) w.PlaceVehicle(new Vector3(x, 0f, 0f), VehicleKind.Car, 90f, 0);
            float worst = 0f; int rounds = 0; var inside = new Dictionary<TrafficSystem.Vehicle, bool>();
            for (float s = 0f; s < 120f; s += Dt)
            {
                w.Traffic.Step(Dt);
                foreach (var v in w.Traffic.Vehicles)
                {
                    if (v.State == TrafficSystem.Mode.Driving) worst = Mathf.Max(worst, w.Traffic.OffRoad(v.Position));
                    bool near = (v.Position - centre).magnitude < PathSystem.CircleRadius * 0.9f;
                    if (near && !(inside.TryGetValue(v, out bool was) && was)) rounds++;
                    inside[v] = near;
                }
            }
            Check(w.Traffic.Vehicles.Count == 2 && rounds >= 2 && worst < 0.03f, $"Cars coming to the end drive round the turning circle and back up the street ({rounds} times round), never off the road (at most {worst * 100f:0.0} cm)");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>
        /// Renders of a level crossing (docs/renders/editor-crossing-*.png): the barriers up with traffic crossing, down
        /// with a train going over and cars waiting, and the road rising flush over the rails, from low down.
        /// </summary>
        public static void CrossingRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), Vector3.zero, 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = Scratch("Crossing demo", 0f);
            w.Sky.Shown = false;
            var t = w.Terrain;
            w.BuildTrack(Loop(t, 0.42f, 0.34f, 0.97f, false));
            w.BuildPath(Stroke(t, new Vector2(0.05f, -0.05f), new Vector2(0.05f, -0.75f)), PathKind.Road);
            foreach (var (id, x, z, rot) in new[] { (Catalog.Cottage, -0.06f, -0.08f, 270f), (Catalog.Townhouse, 0.17f, -0.1f, 90f), (Catalog.Grocer, -0.08f, -0.68f, 270f), (Catalog.Bakery, 0.18f, -0.68f, 90f) })
                Put(w, id, x, z, rot);
            PlaceTrainAt(w, Main(w).Points[Main(w).Points.Count / 4]);
            w.People.Grid.Complete();
            w.Traffic.Refresh(true);
            w.Clock.Hour = 9f;
            var c = w.Paths.LevelCrossings.Count > 0 ? w.Paths.LevelCrossings[0] : default;
            var at = c.at + Vector3.up * 0.01f;
            void Shot(string file, Vector3 from, float fov) { cam.transform.position = at + from; cam.transform.LookAt(at); cam.fieldOfView = fov; WonderlandRenders.Capture(cam, file, 1600, 1000); }
            Shot("editor-crossing-up.png", new Vector3(0.16f, 0.12f, -0.14f), 40f);
            Shot("editor-crossing-flush.png", new Vector3(0.1f, 0.015f, -0.06f), 40f);
            // Run until the barriers are down with the train on (or nearly on) the crossing.
            bool shot = false;
            for (float s = 0f; s < 200f && !shot; s += 0.05f)
            {
                Town(w, 0.05f, true);
                if (w.Traffic.GateLowered(0) < 0.99f) continue;
                foreach (var tr in w.Trains.Trains)
                {
                    var l = w.Trains.LocoPosition(tr);
                    if (new Vector2(l.x - c.at.x, l.z - c.at.z).magnitude < 0.08f) shot = true;
                }
            }
            Shot("editor-crossing-down.png", new Vector3(0.16f, 0.12f, -0.14f), 40f);
            Shot("editor-crossing-down-low.png", new Vector3(-0.05f, 0.03f, -0.13f), 40f);
            // A turning circle at a street's end, with a car going round.
            w.BuildPath(Stroke(t, new Vector2(0.05f, -0.8f), new Vector2(0.55f, -0.8f)), PathKind.Road);
            var dead = w.Paths.Lines[^1].points[^1];
            w.ToggleTurningCircle(new Vector3(dead.x, 0f, dead.y));
            w.PlaceVehicle(new Vector3(0.3f, 0f, -0.8f), VehicleKind.Car, 90f, 3);
            for (float s = 0f; s < 6f; s += 0.05f) w.Traffic.Step(0.05f);
            at = new Vector3(dead.x + 0.03f, t.SurfaceAt(dead.x, dead.y) + 0.005f, dead.y);
            Shot("editor-turning-circle.png", new Vector3(-0.14f, 0.13f, -0.12f), 40f);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
