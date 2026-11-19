using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// The way across the world: eight arrows lying on the floor just outside the window's cut edge - one mid-side each
    /// way, one at each corner. Point at one and pull the trigger: the window slides half its size that way. They are
    /// faint until pointed at, so they never get in the way of the land.
    /// </summary>
    public sealed class WindowNavigator : MonoBehaviour
    {
        public static readonly Vector2Int[] Directions =
        {
            new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1), new(0, -1), new(1, -1),
        };
        public static readonly string[] Names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };

        private const float Out = 0.1f;       // how far outside the edge the arrows lie
        private const float Size = 0.09f;     // arrow half length
        private const float Lift = 0.004f;
        private const float Reach = 0.12f;    // how near the pointer must pass

        private WonderlandWorld world;
        private readonly GameObject[] arrows = new GameObject[8];
        private readonly Mesh[] meshes = new Mesh[8];
        private int hovered = -1;

        public void Init(WonderlandWorld world, Material ui)
        {
            this.world = world;
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject("Arrow " + Names[i]);
                go.transform.SetParent(transform, false);
                meshes[i] = new Mesh { name = go.name };
                go.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = ui;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                arrows[i] = go;
                Paint(i, false);
            }
            Place();
        }

        /// <summary>Where arrow <paramref name="i"/> lies (local XZ): outside the middle of a side, or outside a corner.</summary>
        public Vector2 SpotOf(int i)
        {
            var r = world.WindowRect;
            var d = Directions[i];
            return new Vector2(
                d.x == 0 ? r.center.x : d.x > 0 ? r.xMax + Out : r.xMin - Out,
                d.y == 0 ? r.center.y : d.y > 0 ? r.yMax + Out : r.yMin - Out);
        }

        /// <summary>The arrow a ray (world space) points at, or -1.</summary>
        public int Pick(Ray ray)
        {
            if (world == null || !world.Windowed || !gameObject.activeInHierarchy) return -1;
            var o = world.transform.InverseTransformPoint(ray.origin);
            var d = world.transform.InverseTransformDirection(ray.direction);
            if (Mathf.Abs(d.y) < 1e-4f) return -1;
            float t = (Lift - o.y) / d.y;
            if (t < 0f) return -1;
            var p = o + d * t;
            int best = -1; float bestD = Reach;
            for (int i = 0; i < 8; i++)
            {
                float dist = (new Vector2(p.x, p.z) - SpotOf(i)).magnitude;
                if (dist < bestD) { bestD = dist; best = i; }
            }
            return best;
        }

        /// <summary>Lights up the arrow pointed at (-1: none).</summary>
        public void Hover(int i)
        {
            if (i == hovered) return;
            if (hovered >= 0) Paint(hovered, false);
            hovered = i;
            if (hovered >= 0) Paint(hovered, true);
        }

        private void LateUpdate() => Place();

        private void Place()
        {
            if (world == null) return;
            bool show = world.Windowed;
            for (int i = 0; i < 8; i++)
            {
                if (arrows[i].activeSelf != show) arrows[i].SetActive(show);
                var s = SpotOf(i);
                arrows[i].transform.localPosition = new Vector3(s.x, Lift, s.y);
                var d = (Vector2)Directions[i];
                arrows[i].transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 0f);
            }
        }

        /// <summary>A flat chevron pointing along local +Z: faint, or bright when pointed at.</summary>
        private void Paint(int i, bool lit)
        {
            var b = new MeshBuilder();
            var c = lit ? new Color(1f, 0.93f, 0.6f, 0.95f) : new Color(1f, 1f, 1f, 0.5f);
            float w = Size * 0.95f, t = Size * 0.42f;
            var up = Vector3.up;
            // Two bars meeting at the tip.
            void Bar(Vector3 a, Vector3 tip)
            {
                var along = (tip - a).normalized; var side = Vector3.Cross(up, along) * (t * 0.5f);
                int v0 = b.AddVertex(a - side, up, c), v1 = b.AddVertex(a + side, up, c);
                int v2 = b.AddVertex(tip + side, up, c), v3 = b.AddVertex(tip - side, up, c);
                b.AddTriangle(v0, v1, v2); b.AddTriangle(v0, v2, v3);
            }
            var tipP = new Vector3(0f, 0f, Size);
            Bar(new Vector3(-w, 0f, -Size * 0.2f), tipP);
            Bar(new Vector3(w, 0f, -Size * 0.2f), tipP);
            b.ToMesh(arrows[i].name, meshes[i]);
            meshes[i].RecalculateBounds();
        }
    }
}
