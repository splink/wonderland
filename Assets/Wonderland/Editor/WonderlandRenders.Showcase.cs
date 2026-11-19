using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandRenders
    {
        /// <summary>
        /// Close looks at catalog objects, for reviewing them one by one (docs/renders/showcase/): each from a player's
        /// tabletop distance, from close by, and its five levels of detail side by side (Extreme to Minimal, with their
        /// vertex counts in the log). WL_IDS picks the objects (comma-separated catalog ids; all of them when unset).
        /// </summary>
        public static void Showcase()
        {
            string dir = Path.Combine(Output, "showcase");
            Directory.CreateDirectory(dir);
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var lit = WonderlandMaterials.Lit;
            WonderlandLighting.CreateKeyLight();
            CreateFloor(6f);
            var cam = CreateCamera(new Vector3(0f, 0.5f, -1f), Vector3.zero, 40f);
            Capture(cam, "warmup.png", 64, 64); File.Delete(Path.Combine(Output, "warmup.png"));
            var ids = new List<int>();
            string want = System.Environment.GetEnvironmentVariable("WL_IDS");
            if (string.IsNullOrEmpty(want)) for (int i = 0; i < Catalog.Count; i++) ids.Add(i);
            else foreach (var part in want.Split(',')) if (int.TryParse(part.Trim(), out int id)) ids.Add(id);
            var report = new System.Text.StringBuilder();
            foreach (int id in ids)
            {
                var bp = Catalog.Get(id);
                string name = bp.Name.ToLower().Replace(' ', '-').Replace("é", "e");
                var root = new GameObject("Showcase").transform;
                var high = HouseFactory.Get(id, 0, 0);
                var b = high.bounds;
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                Place(root, high, lit, Vector3.zero, Quaternion.Euler(0f, 25f, 0f));
                var centre = b.center;
                // From where a player would see it on the floor, a metre and a bit away.
                cam.fieldOfView = 40f;
                cam.transform.position = new Vector3(0.25f, 0.85f, -1.0f) * Mathf.Clamp(size / 0.3f, 0.45f, 1.3f); cam.transform.LookAt(new Vector3(0f, centre.y * 0.6f, 0f));
                Capture(cam, Path.Combine("showcase", $"{id:00}-{name}-table.png"), 1280, 900);
                // Close by.
                cam.fieldOfView = 36f;
                cam.transform.position = new Vector3(0.3f, 0.32f, -0.75f) * size * 1.6f + Vector3.up * centre.y; cam.transform.LookAt(new Vector3(0f, centre.y, 0f));
                Capture(cam, Path.Combine("showcase", $"{id:00}-{name}-close.png"), 1280, 900);
                Object.DestroyImmediate(root.gameObject);
                // The five levels in a row.
                root = new GameObject("Levels").transform;
                var counts = new List<string>();
                for (int d = MeshBuilder.Extreme; d <= MeshBuilder.Minimal; d++)
                {
                    var mesh = HouseFactory.Get(id, 0, d);
                    counts.Add($"{mesh.vertexCount}");
                    Place(root, mesh, lit, new Vector3((d - 1) * size * 1.15f, 0f, 0f), Quaternion.Euler(0f, 25f, 0f));
                }
                cam.fieldOfView = 30f;
                cam.transform.position = new Vector3(0f, size * 1.6f + centre.y, -size * 5.2f); cam.transform.LookAt(new Vector3(0f, centre.y, 0f));
                Capture(cam, Path.Combine("showcase", $"{id:00}-{name}-levels.png"), 1800, 600);
                Object.DestroyImmediate(root.gameObject);
                report.AppendLine($"{id} {bp.Name}: {size * 100f:0.0} cm; vertices by level (Extreme..Minimal) {string.Join(" / ", counts)}");
            }
            File.WriteAllText(Path.Combine(dir, "levels.txt"), report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
