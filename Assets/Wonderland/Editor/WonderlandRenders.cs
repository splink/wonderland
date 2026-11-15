using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Wonderland.Editor
{
    /// <summary>
    /// Editor renders (NOT headset captures): the asset gallery and assembled scenes, drawn over a procedural wooden floor
    /// that stands in for the passthrough living room. Batch: -executeMethod Wonderland.Editor.WonderlandRenders.Gallery
    /// (needs graphics, so no -nographics). Output: docs/renders/.
    /// </summary>
    public static class WonderlandRenders
    {
        public const string Output = "docs/renders";

        [MenuItem("Wonderland/Render asset gallery")]
        public static void Gallery()
        {
            Directory.CreateDirectory(Output);
            ShaderUtil.allowAsyncCompilation = false;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lit = WonderlandMaterials.Lit;
            WonderlandLighting.CreateKeyLight();
            CreateFloor(4f);

            var budget = new System.Text.StringBuilder("Wonderland asset vertex budget:\n");
            for (int k = 0; k < TreeFactory.KindCount; k++)
            for (int v = 0; v < TreeFactory.Variants((TreeKind)k); v++) budget.AppendLine($"  tree {(TreeKind)k} {v}: {TreeFactory.Get((TreeKind)k, v).vertexCount} verts");
            for (int v = 0; v < HouseFactory.VariantCount; v++) budget.AppendLine($"  house {HouseFactory.Names[v]}: {HouseFactory.Get(v).vertexCount} verts");
            budget.AppendLine($"  villager: {PersonFactory.Body(0).vertexCount + 2 * PersonFactory.Arm(0).vertexCount + 2 * PersonFactory.Leg(0).vertexCount} verts");
            budget.AppendLine($"  sheep: {AnimalFactory.Body(AnimalKind.Sheep).vertexCount + AnimalFactory.Head(AnimalKind.Sheep).vertexCount} verts, cow: {AnimalFactory.Body(AnimalKind.Cow).vertexCount + AnimalFactory.Head(AnimalKind.Cow).vertexCount} verts");
            Debug.Log(budget.ToString());
            File.WriteAllText(Path.Combine(Output, "asset-budget.txt"), budget.ToString());

            // Trees: kinds in rows, variants in columns.
            var root = new GameObject("Gallery").transform;
            for (int k = 0; k < TreeFactory.KindCount; k++)
            for (int v = 0; v < TreeFactory.Variants((TreeKind)k); v++)
                Place(root, TreeFactory.Get((TreeKind)k, v), lit, new Vector3((v - 1.5f) * 0.11f, 0f, (k - 2) * 0.11f), Quaternion.Euler(0f, k * 40f + v * 70f, 0f));
            var cam = CreateCamera(new Vector3(0.0f, 0.46f, -0.62f), new Vector3(0f, 0.03f, 0.02f), 34f);
            Capture(cam, "warmup.png", 64, 64); File.Delete(Path.Combine(Output, "warmup.png"));
            Capture(cam, "editor-trees.png", 1800, 1350);
            cam.transform.position = new Vector3(-0.05f, 0.09f, -0.37f); cam.transform.LookAt(new Vector3(-0.07f, 0.05f, -0.22f)); cam.fieldOfView = 34f;
            Capture(cam, "editor-trees-closeup.png", 1800, 1100);
            Object.DestroyImmediate(root.gameObject);

            // Houses: types in rows, the four colour styles in columns.
            root = new GameObject("Gallery").transform;
            for (int v = 0; v < HouseFactory.VariantCount; v++)
            for (int st = 0; st < HouseFactory.StyleCount; st++)
                Place(root, HouseFactory.Get(v, st), lit, new Vector3((st - 1.5f) * 0.17f, 0f, (v - 1) * 0.17f), Quaternion.Euler(0f, 18f - st * 9f, 0f));
            cam = CreateCamera(new Vector3(0.0f, 0.42f, -0.78f), new Vector3(0f, 0.04f, 0.0f), 36f);
            Capture(cam, "editor-houses.png", 1800, 1200);
            cam.transform.position = new Vector3(0.02f, 0.4f, 0.75f); cam.transform.LookAt(new Vector3(0f, 0.04f, 0f));
            Capture(cam, "editor-houses-back.png", 1800, 1200);
            Object.DestroyImmediate(root.gameObject);

            // Hero close-ups: one of each house with a few trees and meadow details.
            root = new GameObject("Gallery").transform;
            Place(root, HouseFactory.Get(0, 0), lit, new Vector3(-0.07f, 0f, 0f), Quaternion.Euler(0f, 20f, 0f));
            Place(root, HouseFactory.Get(2, 1), lit, new Vector3(0.1f, 0f, 0.04f), Quaternion.Euler(0f, -18f, 0f));
            Place(root, HouseFactory.Get(1, 2), lit, new Vector3(0.0f, 0f, 0.15f), Quaternion.Euler(0f, 5f, 0f));
            Place(root, TreeFactory.Get(TreeKind.Oak, 0), lit, new Vector3(-0.17f, 0f, 0.1f), Quaternion.Euler(0f, 30f, 0f));
            Place(root, TreeFactory.Get(TreeKind.Blossom, 0), lit, new Vector3(0.2f, 0f, 0.13f), Quaternion.Euler(0f, 80f, 0f));
            Place(root, TreeFactory.Get(TreeKind.Conifer, 0), lit, new Vector3(-0.11f, 0f, 0.2f), Quaternion.identity);
            Place(root, TreeFactory.Get(TreeKind.Bush, 1), lit, new Vector3(0.03f, 0f, -0.07f), Quaternion.identity);
            Place(root, TreeFactory.Get(TreeKind.Bush, 3), lit, new Vector3(-0.16f, 0f, -0.04f), Quaternion.identity);
            cam = CreateCamera(new Vector3(0.05f, 0.17f, -0.42f), new Vector3(0.0f, 0.05f, 0.05f), 36f);
            Capture(cam, "editor-hero.png", 1800, 1100);
            Object.DestroyImmediate(root.gameObject);

            root = new GameObject("Gallery").transform;
            var train = TrainVisualFactory(root, lit);
            train.localRotation = Quaternion.Euler(0f, 90f, 0f);
            cam = CreateCamera(new Vector3(-0.08f, 0.13f, -0.32f), new Vector3(-0.08f, 0.035f, 0f), 30f);
            Capture(cam, "editor-train.png", 1600, 900);
            cam.transform.position = new Vector3(0.18f, 0.09f, -0.12f); cam.transform.LookAt(new Vector3(0.06f, 0.04f, 0f));
            Capture(cam, "editor-train-front.png", 1600, 900);
            Object.DestroyImmediate(root.gameObject);

            // Villagers: the six outfits standing, walking and waving, with a sheep and a cow for scale.
            root = new GameObject("Gallery").transform;
            for (int st = 0; st < PersonFactory.StyleCount; st++)
            {
                var person = Place(root, PersonFactory.Body(st), lit, new Vector3((st - 2.5f) * 0.022f, 0f, 0f), Quaternion.Euler(0f, 180f + (st - 2.5f) * 8f, 0f));
                float swing = st % 3 == 1 ? 1f : 0f;
                Place(person, PersonFactory.Leg(st), lit, PersonFactory.Hip + Vector3.left * PersonFactory.LegX, Quaternion.Euler(swing * 28f, 0f, 0f));
                Place(person, PersonFactory.Leg(st), lit, PersonFactory.Hip + Vector3.right * PersonFactory.LegX, Quaternion.Euler(-swing * 28f, 0f, 0f));
                Place(person, PersonFactory.Arm(st), lit, new Vector3(-PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f), Quaternion.Euler(-swing * 24f, 0f, -6f));
                Place(person, PersonFactory.Arm(st), lit, new Vector3(PersonFactory.ShoulderX, PersonFactory.ShoulderY, 0f), st % 3 == 2 ? Quaternion.Euler(0f, 0f, 150f) : Quaternion.Euler(swing * 24f, 0f, 6f));
            }
            foreach (var (kind, at, yaw) in new[] { (AnimalKind.Sheep, new Vector3(-0.085f, 0f, 0.03f), 120f), (AnimalKind.Cow, new Vector3(0.09f, 0f, 0.035f), 230f) })
            {
                var body = Place(root, AnimalFactory.Body(kind), lit, at, Quaternion.Euler(0f, yaw, 0f));
                Place(body, AnimalFactory.Head(kind), lit, AnimalFactory.NeckOffset(kind), Quaternion.Euler(8f, 0f, 0f));
            }
            cam = CreateCamera(new Vector3(0f, 0.045f, -0.2f), new Vector3(0f, 0.017f, 0f), 30f);
            Capture(cam, "editor-villagers.png", 1800, 900);
            Debug.Log("Wonderland: gallery rendered to " + Path.GetFullPath(Output));
        }

        /// <summary>Loco and two coaches in a straight line along +Z (gallery only; the runtime uses TrainSystem).</summary>
        private static Transform TrainVisualFactory(Transform parent, Material lit)
        {
            var root = new GameObject("Train").transform;
            root.SetParent(parent, false);
            float z = 0f;
            void Car(Mesh body, CarLayout layout, float centre, bool rods)
            {
                var car = Place(root, body, lit, new Vector3(0f, 0f, centre), Quaternion.identity);
                for (int i = 0; i < layout.Axles.Length; i++)
                    Place(car, TrainFactory.AxleMesh(layout.WheelRadius[i]), lit, layout.Axles[i], Quaternion.Euler(i * 37f, 0f, 0f));
                if (rods) Place(car, TrainFactory.RodMesh, lit, new Vector3(0f, layout.Axles[0].y + layout.WheelRadius[0] * 0.45f, 0f), Quaternion.identity);
            }
            Car(TrainFactory.LocoMesh, TrainFactory.Loco, z, true);
            z += TrainFactory.Loco.Rear - 0.006f + TrainFactory.Coach.Rear;
            Car(TrainFactory.CoachMesh(0), TrainFactory.Coach, z, false);
            z += TrainFactory.Coach.Rear * 2f - 0.006f;
            Car(TrainFactory.CoachMesh(1), TrainFactory.Coach, z, false);
            return root;
        }

        public static Transform Place(Transform parent, Mesh mesh, Material material, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localRotation = rotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        public static Camera CreateCamera(Vector3 position, Vector3 target, float fov)
        {
            var cam = Object.FindAnyObjectByType<Camera>();
            if (cam == null) cam = new GameObject("Render Camera").AddComponent<Camera>();
            cam.transform.position = position; cam.transform.LookAt(target);
            cam.fieldOfView = fov; cam.nearClipPlane = 0.01f; cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.12f, 0.1f);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderShadows = true; data.renderPostProcessing = false;
            return cam;
        }

        /// <summary>A warm oak plank floor standing in for the passthrough living room (renders only).</summary>
        public static GameObject CreateFloor(float size)
        {
            const int res = 1024;
            var tex = new Texture2D(res, res, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color[res * res];
            const int planks = 8;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                int plank = x * planks / res;
                float u = (x % (res / planks)) / (float)(res / planks);
                float offset = Noise.Hash(plank, 3) * res;
                float v = (y + offset) % res / res;
                int board = Mathf.FloorToInt((y + offset) / (res * 0.5f));
                float grain = Noise.Value2(u * 3f + plank * 7f, v * 60f) * 0.6f + Noise.Value2(u * 20f, v * 200f + plank) * 0.4f;
                float tone = 0.85f + Noise.Hash(plank, board) * 0.25f;
                var c = Color.Lerp(new Color(0.42f, 0.27f, 0.16f), new Color(0.6f, 0.41f, 0.25f), grain) * tone;
                if (u < 0.012f || u > 0.988f || ((y + (int)offset) % (res / 2)) < 2) c *= 0.55f;
                px[y * res + x] = c;
            }
            tex.SetPixels(px); tex.Apply(true);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "Simulated floor";
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.position = new Vector3(0f, -0.0005f, 0f);
            floor.transform.localScale = Vector3.one * size;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", Vector2.one * size / 1.6f);
            mat.SetFloat("_Smoothness", 0.35f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return floor;
        }

        public static void Capture(Camera camera, string file, int width, int height)
        {
            var target = new RenderTexture(width, height, 24) { antiAliasing = 8 };
            target.Create();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, file), image.EncodeToPNG());
            RenderTexture.active = previous;
            Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target);
        }
    }
}
