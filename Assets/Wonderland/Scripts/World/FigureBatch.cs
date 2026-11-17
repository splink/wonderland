using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Draws figures (villagers, animals) together: every part of every figure is gathered by its mesh - which also
    /// names its level of detail - and each mesh is drawn once for all of them (GPU instancing), instead of a draw per
    /// arm, leg and head. The figures keep their own objects for posing; only their renderers stay switched off.
    /// What is behind the player or beyond drawing distance is left out (unless a CCTV feed is being watched).
    /// The Figures setting "One by one" draws them the old way, to compare.
    /// </summary>
    public sealed class FigureBatch
    {
        private readonly Dictionary<Mesh, List<Matrix4x4>> casting = new(), plain = new();
        private Material material;
        private Vector3 eye, forward, centre;
        private float reach;
        private bool cull;
        private System.Func<Vector3, bool> filming;   // (world position: on a camera feed)

        /// <summary>Whether figures are drawn together (the Figures setting).</summary>
        public static bool Together => PerfSettings.Figures.Value == 0;

        /// <summary>Starts a frame: what will be seen is worked out from the main camera.</summary>
        public void Begin(Material source, bool watched, System.Func<Vector3, bool> onFeed = null)
        {
            filming = onFeed;
            if (material == null || material.shader != source.shader) material = new Material(source) { name = source.name + " (figures)", enableInstancing = true };
            foreach (var l in casting.Values) l.Clear();
            foreach (var l in plain.Values) l.Clear();
            var cam = Camera.main;
            // The bounds the draws are culled by are centred on the eyes: the headset's camera culls the figures' layer
            // beyond 3 m measured to the centre of the bounds, so bounds centred anywhere else (the room's origin) would
            // make every figure vanish at once whenever the player stood 3 m from it. Which figures are near enough is
            // decided here, figure by figure (Visible).
            centre = cam != null ? cam.transform.position : Vector3.zero;
            cull = cam != null && !watched;
            if (cull) { eye = cam.transform.position; forward = cam.transform.forward; reach = WonderlandWorld.MiniatureCullDistance; }
        }

        /// <summary>Whether a figure at <paramref name="position"/> (world) could be seen: in front of the player, within drawing distance.</summary>
        public bool Visible(Vector3 position)
        {
            if (!cull || filming != null && filming(position)) return true;
            var d = position - eye;
            float dist = d.magnitude;
            return dist < reach && (dist < 0.3f || Vector3.Dot(d, forward) > dist * 0.2f);   // (about 78 degrees either side: more than the headset sees)
        }

        /// <summary>Adds a figure's parts (their renderers are switched off; drawn here instead).</summary>
        public void Add(MeshRenderer[] parts, MeshFilter[] filters)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                var r = parts[i];
                if (r == null) continue;
                if (r.enabled) r.enabled = false;
                if (!r.gameObject.activeInHierarchy) continue;
                var mesh = filters[i].sharedMesh;
                if (mesh == null) continue;
                var into = r.shadowCastingMode == ShadowCastingMode.Off ? plain : casting;
                if (!into.TryGetValue(mesh, out var list)) into[mesh] = list = new List<Matrix4x4>(64);
                list.Add(r.transform.localToWorldMatrix);
            }
        }

        /// <summary>Puts the renderers back (Figures: one by one).</summary>
        public static void Restore(MeshRenderer[] parts) { foreach (var r in parts) if (r != null && !r.enabled) r.enabled = true; }

        public void Draw(int layer)
        {
            Draw(casting, ShadowCastingMode.On, layer);
            Draw(plain, ShadowCastingMode.Off, layer);
        }

        private void Draw(Dictionary<Mesh, List<Matrix4x4>> lists, ShadowCastingMode shadows, int layer)
        {
            var rp = new RenderParams(material) { shadowCastingMode = shadows, receiveShadows = true, layer = layer, worldBounds = new Bounds(centre, Vector3.one * 2000f) };
            foreach (var (mesh, list) in lists)
                for (int start = 0; start < list.Count; start += 511)
                    Graphics.RenderMeshInstanced(rp, mesh, 0, list, Mathf.Min(511, list.Count - start), start);
        }
    }
}
