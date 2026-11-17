using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Five levels of detail for the generated buildings, trees and figures (<see cref="MeshBuilder.Detail"/>), chosen
    /// by how big an object looks - a level per doubling of distance: Extreme for a 10 cm building nearer than 0.45 m,
    /// High (the model as designed) to 0.9 m, Medium to 1.8 m, Low to 3.5 m, Minimal beyond. A bigger building keeps
    /// its detail further away, a small one gives it up sooner. The Distance setting "Full" holds everything at High.
    /// </summary>
    public static class Lod
    {
        /// <summary>The distances at which a 10 cm object steps down a level.</summary>
        public static readonly float[] Steps = { 0.45f, 0.9f, 1.8f, 3.5f };
        public const float Reference = 0.1f;
        private static readonly string[] Names = { "Detail -1", null, "Detail 1", "Detail 2", "Detail 3" };

        /// <summary>Screen-height share of an object of <paramref name="size"/> at <paramref name="distance"/> (a Quest eye's ~96 degree field).</summary>
        public static float ScreenShare(float size, float distance) => size / (2f * distance * Mathf.Tan(48f * Mathf.Deg2Rad)) * QualitySettings.lodBias;

        /// <summary>
        /// Gives a renderer (its High level) the other four as children and a level-of-detail group. <paramref name="level"/>
        /// makes the mesh of a level (-1 Extreme ... 3 Minimal).
        /// </summary>
        public static void Attach(MeshRenderer high, System.Func<int, Mesh> level, float size)
        {
            var renderers = new Renderer[MeshBuilder.Levels];
            for (int d = MeshBuilder.Extreme; d <= MeshBuilder.Minimal; d++)
                renderers[d + 1] = d == MeshBuilder.High ? high : Child(high, Names[d + 1], level(d));
            if (!high.TryGetComponent<LODGroup>(out var lod)) lod = high.gameObject.AddComponent<LODGroup>();
            var lods = new LOD[MeshBuilder.Levels];
            for (int i = 0; i < lods.Length; i++)
                lods[i] = new LOD(i < Steps.Length ? ScreenShare(Reference, Steps[i]) : 0f, new[] { renderers[i] });
            lod.SetLODs(lods);
            lod.size = size;
            lod.fadeMode = LODFadeMode.None;
            lod.ForceLOD(PerfSettings.ReducedDetail ? -1 : 1);
        }

        private static MeshRenderer Child(MeshRenderer high, string name, Mesh mesh)
        {
            var t = high.transform.Find(name);
            var go = t != null ? t.gameObject : new GameObject(name);
            go.transform.SetParent(high.transform, false);
            go.layer = high.gameObject.layer;
            if (!go.TryGetComponent<MeshFilter>(out var f)) f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            if (!go.TryGetComponent<MeshRenderer>(out var r)) r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = high.sharedMaterial; r.shadowCastingMode = high.shadowCastingMode; r.receiveShadows = high.receiveShadows;
            return r;
        }

        /// <summary>The level for a figure (a villager, an animal) of <paramref name="size"/> at <paramref name="distance"/>, with a little hysteresis around <paramref name="current"/>.</summary>
        public static int For(float size, float distance, int current)
        {
            if (!PerfSettings.ReducedDetail) return MeshBuilder.High;
            // As far as a 10 cm building would be for the same look - but figures are the eye's favourite: twice as generous.
            float d = distance * Reference / Mathf.Max(size, 0.005f) * 0.5f;
            int level = MeshBuilder.Extreme;
            for (int i = 0; i < Steps.Length; i++) if (d > Steps[i] * (current > i - 1 ? 0.92f : 1.08f)) level = i;   // (step back only clearly past the line)
            return level;
        }

        /// <summary>The Distance setting: every level-of-detail group under <paramref name="root"/> chooses by size on screen, or holds High.</summary>
        public static void Apply(Transform root)
        {
            if (root == null) return;
            foreach (var lod in root.GetComponentsInChildren<LODGroup>(true)) if (lod.lodCount == MeshBuilder.Levels) lod.ForceLOD(PerfSettings.ReducedDetail ? -1 : 1);
        }
    }
}
