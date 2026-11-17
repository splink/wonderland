using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Levels of detail for the generated buildings, trees and figures: the full mesh close up, then the same object
    /// built with less detail (<see cref="MeshBuilder.Detail"/> 1 beyond ~1.3 m, 2 beyond ~2.6 m) - the same shape,
    /// colours, windows and signs, with the millimetre-scale ornament left out and fewer facets. The Distance setting
    /// "Full" holds every object at full detail (<see cref="Apply"/>).
    /// </summary>
    public static class Lod
    {
        public const float Near = 1.3f, Far = 2.6f;

        /// <summary>Screen-height share of an object of <paramref name="size"/> at <paramref name="distance"/> (a Quest eye's ~96 degree field).</summary>
        public static float ScreenShare(float size, float distance) => size / (2f * distance * Mathf.Tan(48f * Mathf.Deg2Rad)) * QualitySettings.lodBias;

        /// <summary>Gives a renderer its simpler levels as children (made once; updated if called again) and a level-of-detail group.</summary>
        public static void Attach(MeshRenderer full, Mesh level1, Mesh level2, float size)
        {
            if (level1 == null || level2 == null) return;
            var r1 = Child(full, "Detail 1", level1);
            var r2 = Child(full, "Detail 2", level2);
            if (!full.TryGetComponent<LODGroup>(out var lod)) lod = full.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[]
            {
                new LOD(ScreenShare(size, Near), new Renderer[] { full }),
                new LOD(ScreenShare(size, Far), new Renderer[] { r1 }),
                new LOD(0f, new Renderer[] { r2 }),
            });
            lod.size = size;
            lod.fadeMode = LODFadeMode.None;
            lod.ForceLOD(PerfSettings.ReducedDetail ? -1 : 0);
        }

        private static MeshRenderer Child(MeshRenderer full, string name, Mesh mesh)
        {
            var t = full.transform.Find(name);
            var go = t != null ? t.gameObject : new GameObject(name);
            go.transform.SetParent(full.transform, false);
            go.layer = full.gameObject.layer;
            if (!go.TryGetComponent<MeshFilter>(out var f)) f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            if (!go.TryGetComponent<MeshRenderer>(out var r)) r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = full.sharedMaterial; r.shadowCastingMode = full.shadowCastingMode; r.receiveShadows = full.receiveShadows;
            return r;
        }

        /// <summary>The Distance setting: every level-of-detail group under <paramref name="root"/> chooses by distance, or holds full detail.</summary>
        public static void Apply(Transform root)
        {
            if (root == null) return;
            foreach (var lod in root.GetComponentsInChildren<LODGroup>(true)) if (lod.lodCount == 3) lod.ForceLOD(PerfSettings.ReducedDetail ? -1 : 0);
        }
    }
}
