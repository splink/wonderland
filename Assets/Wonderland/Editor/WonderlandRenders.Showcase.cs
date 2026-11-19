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

        /// <summary>A villager standing at <paramref name="at"/> (for scale beside the buildings).</summary>
        public static void Villager(Transform parent, Vector3 at, int style)
        {
            var lit = WonderlandMaterials.Lit;
            var root = new GameObject("Villager").transform;
            root.SetParent(parent, false); root.localPosition = at; root.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Place(root, PersonFactory.Body(style), lit, Vector3.zero, Quaternion.identity);
            Place(root, PersonFactory.Leg(style), lit, PersonFactory.Hip + Vector3.left * PersonFactory.LegX, Quaternion.identity);
            Place(root, PersonFactory.Leg(style), lit, PersonFactory.Hip + Vector3.right * PersonFactory.LegX, Quaternion.identity);
            Place(root, PersonFactory.Arm(style), lit, new Vector3(-PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f), Quaternion.identity);
            Place(root, PersonFactory.Arm(style), lit, new Vector3(PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f), Quaternion.identity);
        }

        /// <summary>
        /// The buildings of the categories named in WL_CATS (all but the details when unset), each category in a row seen
        /// from a player's tabletop distance, a villager beside each for scale (docs/renders/lineup-*.png): for judging
        /// whether each is recognisable by its silhouette alone.
        /// </summary>
        public static void Lineup()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var lit = WonderlandMaterials.Lit;
            WonderlandLighting.CreateKeyLight();
            CreateFloor(8f);
            var cam = CreateCamera(new Vector3(0f, 0.5f, -1f), Vector3.zero, 34f);
            Capture(cam, "warmup.png", 64, 64); File.Delete(Path.Combine(Output, "warmup.png"));
            string want = System.Environment.GetEnvironmentVariable("WL_CATS");
            foreach (var cat in Catalog.CategoryOrder)
            {
                string name = Catalog.CategoryNames[(int)cat].ToLower();
                if (!string.IsNullOrEmpty(want) ? !want.ToLower().Contains(name) : cat == PlaceCategory.Small) continue;
                var ids = Catalog.InCategory(cat).FindAll(id => !Catalog.Get(id).Boat && !Catalog.Get(id).Small);
                var root = new GameObject("Lineup").transform;
                float x = 0f, tallest = 0.05f;
                var at = new List<float>();
                foreach (int id in ids) { var h = Catalog.Get(id).Half; x += h.x; at.Add(x); x += h.x + 0.04f; tallest = Mathf.Max(tallest, Catalog.Get(id).Height); }
                for (int k = 0; k < ids.Count; k++)
                {
                    var o = Place(root, HouseFactory.Get(ids[k], 0), lit, new Vector3(at[k] - x / 2f, 0f, 0f), Quaternion.Euler(0f, 18f, 0f));
                    var rotor = HouseFactory.Rotor(ids[k]);
                    if (rotor != null) Place(o, rotor, lit, Catalog.Get(ids[k]).RotorPivot, Quaternion.identity);
                    Villager(root, new Vector3(at[k] - x / 2f + Catalog.Get(ids[k]).Half.x * 0.7f, 0f, -Catalog.Get(ids[k]).Half.y - 0.02f), k);
                }
                float dist = Mathf.Max(x * 0.9f, tallest * 3f);
                cam.transform.position = new Vector3(0f, tallest * 0.8f + dist * 0.3f, -dist); cam.transform.LookAt(new Vector3(0f, tallest * 0.3f, 0f)); cam.fieldOfView = 34f;
                Capture(cam, $"lineup-{name}.png", 2400, 900);
                Object.DestroyImmediate(root.gameObject);
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
