using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>City ground (docs/renders/editor-ground-*.png): the four kinds side by side, and a paved square with its shops.</summary>
        public static void GroundRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), Vector3.zero, 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = Scratch("Ground", 0f);
            w.Sky.Shown = false; w.Clock.Hour = 11f;
            // The four kinds in strips, a little grass between.
            for (int k = 1; k <= 4; k++)
                for (float x = -0.56f; x <= -0.24f; x += 0.02f)
                    w.PaveStroke(new Vector3(x, 0f, 0.42f - k * 0.1f), 0.04f, (byte)k);
            // A town square: slabs, cobbled lanes, shops round it, a fountain in the middle; asphalt in front of the mall.
            for (float x = -0.05f; x <= 0.55f; x += 0.02f)
                for (float z = -0.3f; z <= 0.3f; z += 0.02f)
                    w.PaveStroke(new Vector3(x, 0f, z), 0.02f, (byte)(Mathf.Abs(z) < 0.03f || Mathf.Abs(x - 0.25f) < 0.03f ? 2 : 1));
            w.PlaceHouse(new HouseInstance { x = 0.25f, z = 0.06f, variant = Catalog.Fountain });
            w.PlaceHouse(new HouseInstance { x = 0.08f, z = 0.2f, rotation = 180f, variant = Catalog.Pizzeria });
            w.PlaceHouse(new HouseInstance { x = 0.42f, z = 0.2f, rotation = 180f, variant = Catalog.Boutique });
            w.PlaceHouse(new HouseInstance { x = 0.08f, z = -0.2f, variant = Catalog.Florist });
            w.PlaceHouse(new HouseInstance { x = 0.42f, z = -0.2f, variant = Catalog.Kiosk });
            void Shot(string file, Vector3 from, Vector3 at, float fov) { cam.transform.position = from; cam.transform.LookAt(at); cam.fieldOfView = fov; WonderlandRenders.Capture(cam, file, 1600, 1000); }
            Shot("editor-ground-kinds.png", new Vector3(-0.4f, 0.32f, -0.12f), new Vector3(-0.4f, 0f, 0.17f), 42f);
            Shot("editor-ground-square.png", new Vector3(0.25f, 0.42f, -0.62f), new Vector3(0.25f, 0f, 0f), 42f);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
