using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Where the railway can be joined, always in sight with the Track tool: a pale blue ring at every free end of a line
    /// (draw on from it, or end a stroke on it to join up) and an orange ring at every switch. Rings near the pointer
    /// swell, so while drawing it is plain where letting go will join on; the label at the pointer says what will be built.
    /// </summary>
    public sealed partial class ToolController
    {
        private readonly List<Transform> railMarks = new();
        private readonly List<(Vector3 at, bool end)> railSpots = new();
        private TrackNetwork marksFor;
        private int marksVersion = -1;
        private Mesh endRing, switchRing;

        /// <summary>The free ends and switches being marked now (for the tests).</summary>
        public int RailMarksShown { get; private set; }

        private void ShowRailMarks(bool show, bool hit, Vector3 local)
        {
            var net = World.Track.Network;
            if (!show || net == null || net.Empty)
            {
                if (RailMarksShown > 0) { foreach (var m in railMarks) m.gameObject.SetActive(false); RailMarksShown = 0; }
                return;
            }
            if (net != marksFor || net.Version != marksVersion)
            {
                marksFor = net; marksVersion = net.Version;
                railSpots.Clear();
                for (int e = 0; e < net.Edges.Count; e++)
                {
                    if (net.Edges[e].Closed) continue;
                    for (int k = 0; k < 2; k++) if (net.Edges[e].Joint[k] < 0) railSpots.Add((net.EndPoint(new EdgeEnd(e, k)), true));
                }
                foreach (var j in net.Joints) if (j != null && j.Ends.Count == 3) railSpots.Add((j.Position, false));
            }
            endRing ??= Ring(new Color(0.45f, 0.9f, 1f));
            switchRing ??= Ring(new Color(1f, 0.7f, 0.35f));
            while (railMarks.Count < railSpots.Count)
            {
                var go = new GameObject("Rail mark");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = World.Materials.Lit; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                railMarks.Add(go.transform);
            }
            var wt = World.transform;
            // The pointer: the end of the stroke while drawing, else where it points.
            Vector3? near = drawn.Count > 0 ? drawn[^1] : hit ? local : null;
            for (int i = 0; i < railMarks.Count; i++)
            {
                var m = railMarks[i];
                bool on = i < railSpots.Count;
                m.gameObject.SetActive(on);
                if (!on) continue;
                var (at, end) = railSpots[i];
                m.GetComponent<MeshFilter>().sharedMesh = end ? endRing : switchRing;
                float close = near.HasValue ? Mathf.Clamp01(1f - new Vector2(near.Value.x - at.x, near.Value.z - at.z).magnitude / 0.12f) : 0f;
                float r = (end ? 0.022f : 0.03f) * (1f + 0.7f * close) * (1f + 0.08f * Mathf.Sin(Time.time * 4f + i));
                m.SetPositionAndRotation(wt.TransformPoint(new Vector3(at.x, Mathf.Max(at.y, World.Terrain.SurfaceAt(at.x, at.z)) + 0.008f, at.z)), wt.rotation);
                m.localScale = Vector3.one * r * wt.lossyScale.x;
            }
            RailMarksShown = railSpots.Count;
        }

        /// <summary>A thin glowing ring of radius 1 lying flat.</summary>
        private static Mesh Ring(Color colour)
        {
            var b = new MeshBuilder();
            var glow = Pal.Glow(colour);
            const int seg = 28;
            Vector3 P(float a, float rr) => new(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
            for (int k = 0; k < seg; k++)
            {
                float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                b.AddQuad(P(a0, 0.82f), P(a1, 0.82f), P(a1, 1f), P(a0, 1f), glow);   // (wound to face up)
            }
            return b.ToMesh("Rail mark");
        }
    }
}
