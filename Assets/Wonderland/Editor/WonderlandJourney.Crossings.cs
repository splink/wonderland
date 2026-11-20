using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
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
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
