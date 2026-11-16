using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wonderland.Editor
{
    /// <summary>
    /// The town's buildings side by side from the front, by day and by night (docs/renders/editor-town-shops*.png): to see
    /// at a glance whether each one says what it is.
    /// </summary>
    public static class WonderlandTownGallery
    {
        public static readonly int[] Shops =
        {
            Catalog.Bakery, Catalog.Cafe, Catalog.Grocer, Catalog.Restaurant, Catalog.Disco, Catalog.Museum, Catalog.Studio, Catalog.Fuel,
            Catalog.Hospital, Catalog.Police, Catalog.FireStation, Catalog.TownHall, Catalog.Church, Catalog.School, Catalog.Factory, Catalog.Station,
        };

        private const float Pitch = 0.21f, RowGap = 0.3f;

        public static void Run()
        {
            Directory.CreateDirectory(WonderlandRenders.Output);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var key = WonderlandLighting.CreateKeyLight();
            WonderlandRenders.CreateFloor(4f);
            var lit = WonderlandMaterials.Lit;
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 0.3f, -0.9f), new Vector3(0f, 0.05f, 0f), 46f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64); File.Delete(Path.Combine(WonderlandRenders.Output, "warmup.png"));
            Transform root = null;
            for (int set = 0; set < 2; set++)
            {
                if (root != null) Object.DestroyImmediate(root.gameObject);
                root = new GameObject("Shops").transform;
                for (int k = 0; k < 8; k++)
                {
                    int i = set * 8 + k;
                    var at = new Vector3((k - 3.5f) * Pitch, 0f, 0f);
                    WonderlandRenders.Place(root, HouseFactory.Get(Shops[i], 0), lit, at, Quaternion.identity);   // fronts (-Z) to the camera
                    var neon = HouseFactory.Neon(Shops[i]);
                    if (neon != null) foreach (var part in neon) WonderlandRenders.Place(root, part, lit, at, Quaternion.identity);
                }
                Shader.SetGlobalFloat("_WL_Night", 0f); WonderlandLighting.Configure(key);
                cam.transform.position = new Vector3(0f, 0.3f, -0.95f); cam.transform.LookAt(new Vector3(0f, 0.05f, 0f)); cam.fieldOfView = 46f;
                WonderlandRenders.Capture(cam, $"editor-town-shops-{set + 1}.png", 2400, 1100);
                Shader.SetGlobalFloat("_WL_Night", 1f);
                key.intensity = 0.3f; key.color = new Color(0.55f, 0.66f, 1f);
                WonderlandRenders.Capture(cam, $"editor-town-shops-night-{set + 1}.png", 2400, 1100);
            }
            // The stand-ins drawn far away: each building's beside it, and some trees.
            if (root != null) Object.DestroyImmediate(root.gameObject);
            Shader.SetGlobalFloat("_WL_Night", 0f); WonderlandLighting.Configure(key);
            root = new GameObject("Stand-ins").transform;
            for (int k = 0; k < 8; k++)
            {
                var full = HouseFactory.Get(Shops[k], 0);
                WonderlandRenders.Place(root, full, lit, new Vector3((k - 3.5f) * Pitch, 0f, 0f), Quaternion.identity);
                WonderlandRenders.Place(root, MeshProxy.Building(full, HouseFactory.SignMesh(Shops[k])), lit, new Vector3((k - 3.5f) * Pitch, 0f, 0.2f), Quaternion.identity);
            }
            for (int k = 0; k < TreeFactory.KindCount; k++)
            {
                var tree = TreeFactory.Get((TreeKind)k, 0);
                WonderlandRenders.Place(root, tree, lit, new Vector3(-0.3f + k * 0.12f, 0f, -0.25f), Quaternion.identity);
                WonderlandRenders.Place(root, MeshProxy.Tree(tree, k is 1 or 2), lit, new Vector3(-0.3f + k * 0.12f + 0.05f, 0f, -0.25f), Quaternion.identity);
            }
            cam.transform.position = new Vector3(0f, 0.5f, -1.2f); cam.transform.LookAt(new Vector3(0f, 0.03f, 0.05f)); cam.fieldOfView = 46f;
            WonderlandRenders.Capture(cam, "editor-standins.png", 2400, 1300);
            Shader.SetGlobalFloat("_WL_Night", 0f);
            Object.DestroyImmediate(root.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
