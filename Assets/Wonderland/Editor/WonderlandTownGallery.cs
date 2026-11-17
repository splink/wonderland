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
            // The levels of detail: each building and tree at full detail, then levels 1 and 2 behind it (seen close up
            // here to judge them; in the world they are only drawn from 1.3 m and 2.6 m away).
            if (root != null) Object.DestroyImmediate(root.gameObject);
            Shader.SetGlobalFloat("_WL_Night", 0f); WonderlandLighting.Configure(key);
            root = new GameObject("Levels").transform;
            for (int level = -1; level <= 3; level++)
            {
                for (int k = 0; k < 8; k++) WonderlandRenders.Place(root, HouseFactory.Get(Shops[k], 0, level), lit, new Vector3((k - 3.5f) * Pitch, 0f, (level + 1) * 0.24f), Quaternion.identity);
                for (int k = 0; k < TreeFactory.KindCount; k++) WonderlandRenders.Place(root, TreeFactory.Get((TreeKind)k, 0, level), lit, new Vector3(0.62f + (level + 1) * 0.1f, 0f, -0.1f + k * 0.11f), Quaternion.identity);
            }
            cam.transform.position = new Vector3(0.05f, 0.55f, -0.85f); cam.transform.LookAt(new Vector3(0.05f, 0.03f, 0.4f)); cam.fieldOfView = 52f;
            WonderlandRenders.Capture(cam, "editor-levels.png", 2400, 1400);
            // Figures at twice their size: villagers (body, arms and legs as posed standing) and animals, level by level.
            Object.DestroyImmediate(root.gameObject);
            root = new GameObject("Figure levels").transform;
            for (int level = -1; level <= 3; level++)
            {
                for (int k = 0; k < PersonFactory.StyleCount; k++)
                {
                    var at = new Vector3(-0.17f + k * 0.022f, 0f, (level + 1) * 0.05f);
                    var fig = new GameObject("Figure").transform; fig.SetParent(root, false); fig.localPosition = at; fig.localScale = Vector3.one * 2f;
                    WonderlandRenders.Place(fig, PersonFactory.Body(k, level), lit, Vector3.zero, Quaternion.identity);
                    foreach (int sd in new[] { -1, 1 })
                    {
                        WonderlandRenders.Place(fig, PersonFactory.Leg(k, level), lit, PersonFactory.Hip + Vector3.right * sd * PersonFactory.LegX, Quaternion.identity);
                        WonderlandRenders.Place(fig, PersonFactory.Arm(k, level), lit, new Vector3(sd * PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f), Quaternion.identity);
                    }
                }
                for (int k = 0; k < AnimalFactory.KindCount; k++)
                {
                    var at = new Vector3(0.0f + k * 0.035f, 0f, (level + 1) * 0.05f);
                    var fig = new GameObject("Animal").transform; fig.SetParent(root, false); fig.localPosition = at; fig.localScale = Vector3.one * 2f; fig.localRotation = Quaternion.Euler(0f, -90f, 0f);
                    WonderlandRenders.Place(fig, AnimalFactory.Body((AnimalKind)k, level), lit, Vector3.zero, Quaternion.identity);
                    WonderlandRenders.Place(fig, AnimalFactory.Head((AnimalKind)k, level), lit, AnimalFactory.NeckOffset((AnimalKind)k), Quaternion.identity);
                }
            }
            foreach (Transform f in root) f.localRotation *= Quaternion.Euler(0f, 180f, 0f);   // (faces to the camera)
            cam.transform.position = new Vector3(0.0f, 0.16f, -0.24f); cam.transform.LookAt(new Vector3(0.0f, 0.01f, 0.08f)); cam.fieldOfView = 52f;
            WonderlandRenders.Capture(cam, "editor-levels-figures.png", 2400, 1300);
            // Triangles at each level, every building in the catalogue and every tree.
            var report = new System.Text.StringBuilder("triangles at Extreme / High / Medium / Low / Minimal\n");
            string Row(string what, System.Func<int, long> count)
            {
                var row = new System.Text.StringBuilder($"{what,-26}");
                long high = count(0);
                for (int d = -1; d <= 3; d++) row.Append($" {count(d),8:N0} ({count(d) * 100 / System.Math.Max(1, high),3}%)");
                return row.ToString();
            }
            report.AppendLine(Row($"buildings ({Catalog.Count})", d => { long n = 0; for (int id = 0; id < Catalog.Count; id++) n += HouseFactory.Get(id, 0, d).triangles.Length / 3; return n; }));
            report.AppendLine(Row("trees", d => { long n = 0; for (int k = 0; k < TreeFactory.KindCount; k++) for (int v = 0; v < TreeFactory.Variants((TreeKind)k); v++) n += TreeFactory.Get((TreeKind)k, v, d).triangles.Length / 3; return n; }));
            report.AppendLine(Row("villagers (6)", d => { long n = 0; for (int k = 0; k < PersonFactory.StyleCount; k++) n += (PersonFactory.Body(k, d).triangles.Length + 2 * PersonFactory.Leg(k, d).triangles.Length + 2 * PersonFactory.Arm(k, d).triangles.Length) / 3; return n; }));
            report.AppendLine(Row("animals (9)", d => { long n = 0; for (int k = 0; k < AnimalFactory.KindCount; k++) n += (AnimalFactory.Body((AnimalKind)k, d).triangles.Length + AnimalFactory.Head((AnimalKind)k, d).triangles.Length) / 3; return n; }));
            System.IO.File.WriteAllText("Logs/lod-results.txt", report.ToString());
            Debug.Log(report.ToString());
            Shader.SetGlobalFloat("_WL_Night", 0f);
            Object.DestroyImmediate(root.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
