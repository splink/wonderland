using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    /// <summary>Creates the few shared materials as assets so their shaders ship in the player build.</summary>
    public static class WonderlandMaterials
    {
        private const string Folder = "Assets/Wonderland/Materials";

        public static Material Lit => Ensure("WonderlandLit", "Wonderland/Lit", null);
        public static Material Terrain => Ensure("WonderlandTerrain", "Wonderland/Terrain", null);
        public static Material UI => Ensure("WonderlandUI", "Wonderland/UI", m => m.renderQueue = 3000);
        public static Material Smoke => Ensure("WonderlandSmoke", "Wonderland/Smoke", m => m.renderQueue = 3001);
        public static Material Water => Ensure("WonderlandWater", "Wonderland/Water", m => m.renderQueue = 2480);

        public static Material Ensure(string name, string shaderName, System.Action<Material> setup)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException($"Shader {shaderName} not found (compile error?).");
            string path = $"{Folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                System.IO.Directory.CreateDirectory(Folder);
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.enableInstancing = true;
            setup?.Invoke(material);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
