using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>A stop signal placed by hand beside the track: trains wait at it while it shows red.</summary>
    [System.Serializable]
    public struct SignalInstance
    {
        public float x, z;   // where it stands (beside the track)
        public bool red;
    }

    /// <summary>
    /// The railway network: generates ballast, sleepers and rails for every piece of a <see cref="TrackNetwork"/>, the
    /// switch stands (with arrows showing which way each switch is set), automatic signals at every switch, stop signals,
    /// bridges and tunnels; grades and locks the terrain corridor under the track, and draws the stroke preview.
    /// Terrain rule: laying track grades the ground to the track's smooth profile (embankments / cuttings) and locks the
    /// corridor; terrain brushes skip locked ground, so later sculpting can never bury or float the track.
    /// </summary>
    public sealed class TrackSystem : MonoBehaviour
    {
        public const float RailTop = 0.0063f;    // rail top above the ballast bed
        public const float BedHalfWidth = 0.042f;
        public const float CorridorHalfWidth = 0.12f;
        public const float BedSink = 0.0015f;    // the graded ground sits this far below the ballast top
        public const float SignalSetback = TrackNetwork.FoulDistance + 0.03f;

        public TrackNetwork Network { get; private set; } = new();
        public readonly List<SignalInstance> Signals = new();
        /// <summary>Hand-placed signals snapped to the track: piece, arc length (-1 when not beside any track).</summary>
        public readonly List<(int edge, float s)> SignalSpots = new();
        private readonly List<(GameObject go, Mesh mesh)> pieces = new();
        private Mesh fixturesMesh, lampsMesh, previewMesh;
        private Material lit, previewMaterial;
        private bool[] lampState = new bool[0];

        public void Init(Material litMaterial, Material ui)
        {
            lit = litMaterial;
            fixturesMesh = Renderer("Switches and signals", lit, ShadowCastingMode.On);
            lampsMesh = Renderer("Signal lamps", lit, ShadowCastingMode.Off);
            lampsMesh.MarkDynamic();
            previewMaterial = new Material(ui) { renderQueue = 3005 };
            previewMesh = Renderer("Track preview", previewMaterial, ShadowCastingMode.Off);
            previewMesh.MarkDynamic();
        }

        private Mesh Renderer(string name, Material material, ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = shadows;
            return mesh;
        }

        public bool HasTrack => !Network.Empty;

        public const float TunnelHeight = 0.1f;   // ground this far above the bed clears the tube's roof and hides the 8 cm train

        /// <summary>Takes a network (already profiled) and rebuilds everything: tunnels, pieces, switches, signals.</summary>
        public void SetNetwork(TrackNetwork network, System.Func<Vector2, float> waterLevel = null, TerrainField terrain = null)
        {
            Network = network ?? new TrackNetwork();
            Network.Reindex();
            if (terrain != null) foreach (var e in Network.Edges) e.Tunnel = TunnelMask(terrain, e.Layout);
            while (pieces.Count > Network.Edges.Count) { var p = pieces[^1]; Release(p.go); Release(p.mesh); pieces.RemoveAt(pieces.Count - 1); }
            for (int i = 0; i < Network.Edges.Count; i++) RebuildPiece(i, waterLevel, terrain);
            SnapSignals();
            RebuildFixtures();
        }

        private static void Release(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        /// <summary>Rebuilds one piece's mesh (after its profile changed).</summary>
        public void RebuildPiece(int i, System.Func<Vector2, float> waterLevel, TerrainField terrain)
        {
            while (pieces.Count <= i)
            {
                var go = new GameObject("Track piece");
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = "Track piece", indexFormat = IndexFormat.UInt32 };
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = lit; r.shadowCastingMode = ShadowCastingMode.On;
                pieces.Add((go, mesh));
            }
            var e = Network.Edges[i];
            var b = new MeshBuilder();
            BuildTrack(b, e.Layout, e.Joint[0] < 0, e.Joint[1] < 0);
            if (Network.Directed(i)) Arrows(b, e.Layout, 1, ArrowColour, RailTop - 0.0012f, 0.16f);
            if (e.Tunnel != null) BuildTunnels(b, e.Layout, e.Tunnel);
            b.ToMesh("Track piece", pieces[i].mesh);
            pieces[i].mesh.RecalculateBounds();
        }

        /// <summary>Whether a piece's point is inside a tunnel (no steam underground).</summary>
        public bool InTunnel(int edge, float s)
        {
            if (edge < 0 || edge >= Network.Edges.Count) return false;
            var e = Network.Edges[edge];
            if (e.Tunnel == null) return false;
            return e.Tunnel[TrackPlanner.IndexAt(e.Layout, e.Layout.Wrap(s))];
        }

        // ------------------------------------------------------------------ switches and signals

        /// <summary>Where the automatic signal guarding a switch end stands: on the right of an approaching train.</summary>
        public Vector3 JointSignalPosition(EdgeEnd end, out Vector3 facing)
        {
            var l = Network.Edges[end.Edge].Layout;
            float s = end.End == 0 ? Mathf.Min(SignalSetback, l.Length * 0.45f) : Mathf.Max(l.Length - SignalSetback, l.Length * 0.55f);
            var p = l.Evaluate(s, out var t); t.y = 0f; t.Normalize();
            var travel = end.End == 0 ? -t : t;   // towards the switch
            facing = -travel;
            return p + Vector3.Cross(Vector3.up, travel) * 0.05f;
        }

        public void SnapSignals()
        {
            SignalSpots.Clear();
            foreach (var sig in Signals)
            {
                bool on = Network.Nearest(new Vector3(sig.x, 0f, sig.z), out int edge, out float s, out _, 0.1f);
                SignalSpots.Add(on ? (edge, s) : (-1, 0f));
            }
        }

        /// <summary>Signals, switch stands and the route arrows; rebuilt when the network or a switch setting changes.</summary>
        public void RebuildFixtures()
        {
            var b = new MeshBuilder();
            var post = Pal.TrainBlack; var white = Pal.Hex("#F2EEE4");
            var net = Network;
            for (int ji = 0; ji < net.Joints.Count; ji++)
            {
                var j = net.Joints[ji];
                if (j.Ends.Count != 3) continue;
                // Route arrows between the rails: two chevrons into the branch the switch is set to, a red bar across the other.
                var arrow = j.Pinned ? Pal.Glow(Pal.Hex("#FF9A3C")) : Pal.Glow(Pal.Hex("#FFF4C8"));
                for (int k = 0; k < 2; k++)
                {
                    var end = j.Branch(k);
                    var l = net.Edges[end.Edge].Layout;
                    bool set = k == j.Setting;
                    foreach (float d in set ? new[] { 0.05f, 0.08f } : new[] { 0.06f })
                    {
                        float s = end.End == 0 ? d : l.Length - d;
                        var p = l.Evaluate(s, out var t); t.y = 0f; t.Normalize();
                        var into = end.End == 0 ? t : -t;
                        b.Push(p + Vector3.up * 0.0042f, Quaternion.LookRotation(into, Vector3.up));
                        if (set)
                            foreach (int side in new[] { -1, 1 })
                            {
                                b.Push(new Vector3(side * 0.0045f, 0f, 0f), Quaternion.Euler(0f, -side * 40f, 0f));
                                b.AddBox(Vector3.zero, new Vector3(0.0022f, 0.0012f, 0.013f), arrow);
                                b.Pop();
                            }
                        else b.AddBox(Vector3.zero, new Vector3(0.018f, 0.0012f, 0.0035f), Pal.Glow(Pal.Hex("#E8463C")));
                        b.Pop();
                    }
                }
                // A little lever frame beside the trunk, its lever thrown towards the set branch.
                {
                    var trunk = j.Trunk;
                    var l = net.Edges[trunk.Edge].Layout;
                    float s = trunk.End == 0 ? Mathf.Min(0.05f, l.Length * 0.3f) : Mathf.Max(l.Length - 0.05f, l.Length * 0.7f);
                    var p = l.Evaluate(s, out var t); t.y = 0f; t.Normalize();
                    var towards = trunk.End == 0 ? -t : t;   // towards the switch along the trunk
                    var right = Vector3.Cross(Vector3.up, towards);
                    var setDir = net.IntoEdge(j.Branch(j.Setting));
                    float lean = Vector3.Dot(setDir, right) >= 0f ? 1f : -1f;
                    b.Push(p + right * -0.052f, Quaternion.LookRotation(towards, Vector3.up));
                    b.AddBox(new Vector3(0f, 0.004f, 0f), new Vector3(0.012f, 0.008f, 0.016f), Pal.Hex("#3E5A3A"), Pal.Hex("#4E7048"), Pal.Hex("#3E5A3A"));
                    b.Push(new Vector3(0f, 0.008f, 0f), Quaternion.Euler(0f, 0f, -lean * 28f));
                    b.AddBox(new Vector3(0f, 0.008f, 0f), new Vector3(0.0018f, 0.016f, 0.0018f), j.Pinned ? Pal.Hex("#FF9A3C") : white);
                    b.AddBlob(new Vector3(0f, 0.0165f, 0f), Vector3.one * 0.0022f, 0, 0f, ji, (_, __) => Pal.TrainRed);
                    b.Pop();
                    b.Pop();
                }
                // Signal posts at each of the three ends.
                foreach (var end in j.Ends) SignalPost(b, JointSignalPosition(end, out var facing), facing, post, false);
            }
            for (int i = 0; i < Signals.Count; i++)
            {
                var (edge, s) = i < SignalSpots.Count ? SignalSpots[i] : (-1, 0f);
                var at = new Vector3(Signals[i].x, 0f, Signals[i].z);
                var facing = Vector3.forward;
                if (edge >= 0)
                {
                    var p = net.Edges[edge].Layout.Evaluate(s, out var t); t.y = 0f;
                    at.y = p.y;
                    facing = Vector3.Cross(Vector3.up, t.normalized);
                    if (Vector3.Dot(facing, at - p) < 0f) facing = -facing;
                }
                SignalPost(b, at, facing, post, true);
            }
            b.ToMesh("Switches and signals", fixturesMesh);
            fixturesMesh.RecalculateBounds();
            lampState = new bool[0];
        }

        private static void SignalPost(MeshBuilder b, Vector3 at, Vector3 facing, Color post, bool stopSignal)
        {
            b.Push(at, Quaternion.LookRotation(facing, Vector3.up));
            float h = stopSignal ? 0.05f : 0.038f;
            b.AddCylinder(Vector3.zero, 0.0016f, 0.0013f, h, 6, post, post, false);
            b.AddCylinder(Vector3.zero, 0.004f, 0.0035f, 0.002f, 8, Pal.StoneDark, Pal.Stone, true);
            if (stopSignal)
            {
                // A round stop board with a white rim, the lamp in its middle.
                b.Push(new Vector3(0f, h, 0.0012f), Quaternion.Euler(-90f, 0f, 0f));
                b.AddCylinder(Vector3.zero, 0.0082f, 0.0082f, 0.0016f, 16, Pal.Hex("#F2EEE4"), Pal.Hex("#F2EEE4"), true, true);
                b.AddCylinder(new Vector3(0f, 0.0016f, 0f), 0.0064f, 0.0064f, 0.0004f, 16, post, post, true);
                b.Pop();
            }
            else b.AddBox(new Vector3(0f, h, 0.0008f), new Vector3(0.0068f, 0.013f, 0.0042f), post);
            b.Pop();
        }

        /// <summary>
        /// Lights the signal lamps: <paramref name="green"/>[k] for the k-th switch end (in joint order), then one per
        /// hand-placed signal. Only rebuilt when something changed.
        /// </summary>
        public void SetLamps(bool[] green)
        {
            bool same = lampState.Length == green.Length;
            for (int i = 0; same && i < green.Length; i++) same = lampState[i] == green[i];
            if (same) return;
            lampState = (bool[])green.Clone();
            var b = new MeshBuilder();
            var red = Pal.Glow(Pal.Hex("#FF3B30")); var go = Pal.Glow(Pal.Hex("#5BFF7A")); var off = Pal.Hex("#3A2E2E"); var offGreen = Pal.Hex("#2E3A30");
            int k = 0;
            foreach (var j in Network.Joints)
            {
                if (j.Ends.Count != 3) continue;
                foreach (var end in j.Ends)
                {
                    bool g = k < green.Length && green[k]; k++;
                    var at = JointSignalPosition(end, out var facing);
                    b.Push(at, Quaternion.LookRotation(facing, Vector3.up));
                    b.AddFace(new Vector3(0f, 0.038f + 0.0034f, 0.003f), Vector3.forward, Vector3.up, 0.0019f, 0.0019f, g ? offGreen : red);
                    b.AddFace(new Vector3(0f, 0.038f - 0.0034f, 0.003f), Vector3.forward, Vector3.up, 0.0019f, 0.0019f, g ? go : off);
                    b.Pop();
                }
            }
            for (int i = 0; i < Signals.Count; i++)
            {
                bool g = k < green.Length && green[k]; k++;
                var (edge, s) = i < SignalSpots.Count ? SignalSpots[i] : (-1, 0f);
                var at = new Vector3(Signals[i].x, 0f, Signals[i].z);
                var facing = Vector3.forward;
                if (edge >= 0)
                {
                    var p = Network.Edges[edge].Layout.Evaluate(s, out var t); t.y = 0f; at.y = p.y;
                    facing = Vector3.Cross(Vector3.up, t.normalized);
                    if (Vector3.Dot(facing, at - p) < 0f) facing = -facing;
                }
                b.Push(at, Quaternion.LookRotation(facing, Vector3.up));
                b.AddFace(new Vector3(0f, 0.05f, 0.0034f), Vector3.forward, Vector3.up, 0.0042f, 0.0042f, g ? go : red);
                b.AddFace(new Vector3(0f, 0.05f, -0.0034f), Vector3.back, Vector3.up, 0.0042f, 0.0042f, g ? go : red);
                b.Pop();
            }
            b.ToMesh("Signal lamps", lampsMesh);
            lampsMesh.RecalculateBounds();
        }

        /// <summary>The switch nearest a local point within <paramref name="within"/>, or -1.</summary>
        public int PickJoint(Vector3 p, float within)
        {
            int best = -1; float bestD = within * within;
            for (int i = 0; i < Network.Joints.Count; i++)
            {
                var q = Network.Joints[i].Position;
                float d = (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>The hand-placed signal nearest a local point within <paramref name="within"/>, or -1.</summary>
        public int PickSignal(Vector3 p, float within)
        {
            int best = -1; float bestD = within * within;
            for (int i = 0; i < Signals.Count; i++)
            {
                float d = (Signals[i].x - p.x) * (Signals[i].x - p.x) + (Signals[i].z - p.z) * (Signals[i].z - p.z);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public static void BuildTrack(MeshBuilder b, TrackLayout layout, bool bufferStart = true, bool bufferEnd = true)
        {
            var pts = layout.Points; int n = pts.Count;
            var right = new List<Vector3>(n); var up = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                var a = pts[layout.Closed ? (i - 1 + n) % n : Mathf.Max(0, i - 1)];
                var c = pts[layout.Closed ? (i + 1) % n : Mathf.Min(n - 1, i + 1)];
                var t = c - a; t.y = 0f; t.Normalize();
                right.Add(Vector3.Cross(Vector3.up, t).normalized);
                up.Add(Vector3.up);
            }
            // Ballast bed: trapezoid that sinks into the graded ground; gravel speckle per vertex.
            int start = b.VertexCount;
            var ballastProfile = new[] { new Vector2(-BedHalfWidth, -0.012f), new Vector2(-0.027f, 0.0006f), new Vector2(0.027f, 0.0006f), new Vector2(BedHalfWidth, -0.012f) };
            var ballastColours = new[] { Pal.BallastDark, Pal.Ballast, Pal.Ballast, Pal.BallastDark };
            b.AddExtrusion(pts, right, up, ballastProfile, ballastColours, layout.Closed);
            for (int i = start; i < b.VertexCount; i++)
            {
                var v = b.Vertices[i];
                float g = Noise.Hash(Mathf.RoundToInt(v.x * 2000f), Mathf.RoundToInt(v.z * 2000f), i & 3);
                var c = (Color)b.Colors[i];
                b.Colors[i] = Pal.Shade(c, 0.86f + g * 0.26f);
            }
            // Sleepers every 14 mm, slightly irregular like hand-laid track.
            var rng = new Rng(77);
            for (float s = 0.005f; s < layout.Length - (layout.Closed ? 0.007f : 0.004f); s += 0.014f)
            {
                var p = layout.Evaluate(s, out var tangent);
                tangent.y = 0f;
                var rot = Quaternion.LookRotation(tangent.normalized, Vector3.up) * Quaternion.Euler(0f, rng.Range(-2.5f, 2.5f), 0f);
                b.Push(p + Vector3.up * 0.0016f, rot);
                var colour = Pal.Jitter(Pal.Sleeper, ref rng, 0.25f);
                b.AddBox(Vector3.zero, new Vector3(0.046f, 0.0028f, 0.0065f), Pal.Shade(colour, 0.8f), colour, colour);
                b.Pop();
            }
            // Rails: a simple flat-bottom profile with a bright running surface and rusty sides.
            var railProfile = new[]
            {
                new Vector2(-0.0008f, 0f), new Vector2(-0.0008f, 0.0026f), new Vector2(-0.0012f, 0.0028f),
                new Vector2(-0.0012f, 0.0035f), new Vector2(0.0012f, 0.0035f), new Vector2(0.0012f, 0.0028f),
                new Vector2(0.0008f, 0.0026f), new Vector2(0.0008f, 0f),
            };
            var railColours = new[] { Pal.RailSide, Pal.RailSide, Pal.RailSide, Pal.RailTop, Pal.RailTop, Pal.RailSide, Pal.RailSide, Pal.RailSide };
            foreach (int side in new[] { -1, 1 })
            {
                var rail = new List<Vector3>(n);
                for (int i = 0; i < n; i++) rail.Add(pts[i] + right[i] * (side * TrainFactory.Gauge * 0.5f) + Vector3.up * 0.0028f);
                b.AddExtrusion(rail, right, up, railProfile, railColours, layout.Closed);
            }
            if (!layout.Closed)
            {
                // Buffer stops at dead ends.
                foreach (bool atStart in new[] { true, false })
                {
                    if (atStart ? !bufferStart : !bufferEnd) continue;
                    var p = atStart ? pts[0] : pts[n - 1];
                    var t = atStart ? pts[0] - pts[1] : pts[n - 1] - pts[n - 2]; t.y = 0f;
                    b.Push(p, Quaternion.LookRotation(t.normalized, Vector3.up));
                    b.AddBox(new Vector3(0f, 0.008f, 0f), new Vector3(0.034f, 0.008f, 0.005f), Pal.TrainRed);
                    b.AddBox(new Vector3(0f, 0.004f, -0.004f), new Vector3(0.03f, 0.008f, 0.006f), Pal.Timber);
                    b.Pop();
                }
            }
        }

        /// <summary>
        /// Where the railway runs so deep under a hill that the ground above would hide the train, it becomes a tunnel:
        /// runs of at least 8 cm with the surface more than <see cref="TunnelHeight"/> above the bed (small gaps filled).
        /// </summary>
        public static bool[] TunnelMask(TerrainField terrain, TrackLayout layout)
        {
            var pts = layout.Points; int n = pts.Count;
            var mask = new bool[n];
            bool any = false;
            for (int i = 0; i < n; i++) { mask[i] = terrain.SurfaceAt(pts[i].x, pts[i].z) > pts[i].y + TunnelHeight; any |= mask[i]; }
            if (!any) return null;
            // Fill gaps under 3 cm, then drop runs under 8 cm.
            Runs(mask, layout.Closed, (s, e, on) => { if (!on && e - s + 1 < 3) for (int i = s; i <= e; i++) mask[i % n] = true; });
            Runs(mask, layout.Closed, (s, e, on) => { if (on && e - s + 1 < 8) for (int i = s; i <= e; i++) mask[i % n] = false; });
            foreach (bool m in mask) if (m) return mask;
            return null;
        }

        /// <summary>Inside the tunnel by at least <see cref="PortalInset"/> points on both sides (so each mouth gets a cutting).</summary>
        public static bool DeepInTunnel(bool[] tunnel, int i, bool closed)
        {
            int n = tunnel.Length;
            for (int k = -PortalInset; k <= PortalInset; k += PortalInset)
            {
                int j = i + k;
                if (closed) j = (j % n + n) % n; else if (j < 0 || j >= n) return false;
                if (!tunnel[j]) return false;
            }
            return true;
        }

        public const int PortalInset = 4;   // 4 cm of cutting before each portal

        /// <summary>Calls <paramref name="visit"/>(start, end, value) for each run of equal values (end may exceed n on a loop).</summary>
        private static void Runs(bool[] m, bool closed, System.Action<int, int, bool> visit)
        {
            int n = m.Length, start = 0;
            if (closed) { int k = 0; while (k < n && m[k] == m[0]) k++; if (k == n) { visit(0, n - 1, m[0]); return; } start = k; }
            int i = start, count = 0;
            while (count < n)
            {
                int s = i; bool v = m[i % n];
                while (count < n && m[i % n] == v) { i++; count++; }
                if (!closed && s == 0 && v == false) { visit(s, i - 1, v); continue; }
                visit(s, i - 1, v);
            }
        }

        /// <summary>Stone portals at each tunnel mouth and a dark arched tube inside, so nobody sees through the hill.</summary>
        public static void BuildTunnels(MeshBuilder b, TrackLayout layout, bool[] tunnel)
        {
            var pts = layout.Points; int n = pts.Count;
            var dark = new Color(0.07f, 0.06f, 0.06f); var stone = Pal.Stone; var stoneDark = Pal.StoneDark;
            Runs(tunnel, layout.Closed, (s0, e0, on) =>
            {
                if (!on || e0 - s0 < PortalInset * 2 + 2) return;
                int s = s0 + 1, e = e0 - 1;   // portals stand in front of the step where the kept hill begins (PortalInset further in)
                // The tube: an arched cross-section facing inwards, along the run (plus a little beyond each mouth).
                var path = new List<Vector3>(); var right = new List<Vector3>(); var up = new List<Vector3>();
                for (int i = s - 2; i <= e + 2; i++)
                {
                    int j = layout.Closed ? ((i % n) + n) % n : Mathf.Clamp(i, 0, n - 1);
                    var t = pts[layout.Closed ? (j + 1) % n : Mathf.Min(j + 1, n - 1)] - pts[layout.Closed ? (j - 1 + n) % n : Mathf.Max(j - 1, 0)]; t.y = 0f; t.Normalize();
                    path.Add(pts[j]); right.Add(Vector3.Cross(Vector3.up, t)); up.Add(Vector3.up);
                }
                const int arc = 8; const float w = 0.042f, wall = 0.05f;
                var profile = new Vector2[arc + 3]; var colours = new Color[arc + 3];
                // Inward-facing: order counter-clockwise (right wall up, over the top, left wall down).
                profile[0] = new Vector2(w, -0.012f);
                for (int k = 0; k <= arc; k++) { float a = k / (float)arc * Mathf.PI; profile[k + 1] = new Vector2(Mathf.Cos(a) * w, wall + Mathf.Sin(a) * w * 0.9f); }
                profile[arc + 2] = new Vector2(-w, -0.012f);
                for (int k = 0; k < colours.Length; k++) colours[k] = dark;
                b.AddExtrusion(path, right, up, profile, colours, false);
                foreach (int mouth in new[] { s, e })
                {
                    int j = layout.Closed ? ((mouth % n) + n) % n : Mathf.Clamp(mouth, 0, n - 1);
                    int dir = mouth == s ? -1 : 1;
                    var t = pts[layout.Closed ? (j + 1) % n : Mathf.Min(j + 1, n - 1)] - pts[layout.Closed ? (j - 1 + n) % n : Mathf.Max(j - 1, 0)]; t.y = 0f; t.Normalize();
                    var outward = t * dir;
                    b.Push(pts[j] + outward * 0.004f, Quaternion.LookRotation(outward, Vector3.up));
                    // The opening: an opaque dark arch just inside the portal (the toy-railway trick: the train disappears
                    // into it, and the hill's cut face behind it is never seen).
                    {
                        var c = new Vector3(0f, wall, -0.003f);
                        var bl = new Vector3(-w, -0.012f, -0.003f); var br = new Vector3(w, -0.012f, -0.003f);
                        var tl = new Vector3(-w, wall, -0.003f); var tr = new Vector3(w, wall, -0.003f);
                        b.AddQuad(br, tr, tl, bl, dark);   // faces outwards (+Z)
                        const int seg = 12;
                        for (int k = 0; k < seg; k++)
                        {
                            float a0 = k / (float)seg * Mathf.PI, a1 = (k + 1) / (float)seg * Mathf.PI;
                            b.AddTriangleOutward(c, c + new Vector3(Mathf.Cos(a0) * w, Mathf.Sin(a0) * w * 0.9f, 0f), c + new Vector3(Mathf.Cos(a1) * w, Mathf.Sin(a1) * w * 0.9f, 0f), c - Vector3.forward * 0.01f, dark);
                        }
                    }
                    // Pillars, a semicircle of voussoirs with a keystone, and a parapet band above.
                    foreach (int side in new[] { -1, 1 })
                        b.AddBox(new Vector3(side * 0.054f, wall / 2f - 0.006f, 0f), new Vector3(0.024f, wall + 0.012f, 0.012f), stoneDark, stone, stoneDark);
                    const int stones = 9;
                    for (int k = 0; k < stones; k++)
                    {
                        float a = (k + 0.5f) / stones * Mathf.PI;
                        var c = new Vector3(Mathf.Cos(a) * (w + 0.007f), wall + Mathf.Sin(a) * (w * 0.9f + 0.007f), 0f);
                        b.Push(c, Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f));
                        bool key = k == stones / 2;
                        b.AddBox(Vector3.zero, new Vector3(key ? 0.014f : 0.0105f, key ? 0.018f : 0.014f, 0.013f), key ? Pal.Hex("#C9BBA3") : (k % 2 == 0 ? stone : stoneDark));
                        b.Pop();
                    }
                    b.AddBox(new Vector3(0f, wall + w * 0.9f + 0.022f, 0f), new Vector3(0.14f, 0.012f, 0.014f), stone, Pal.Hex("#C9BBA3"), stone);
                    // Spandrel fill between the arch and the band, and the wall face either side.
                    foreach (int side in new[] { -1, 1 })
                    {
                        b.AddBox(new Vector3(side * 0.055f, wall + 0.03f, -0.001f), new Vector3(0.03f, 0.05f, 0.01f), stone, stone, stone);
                        b.AddBox(new Vector3(side * 0.03f, wall + w * 0.9f + 0.006f, -0.001f), new Vector3(0.03f, 0.02f, 0.01f), stone, stone, stone);
                    }
                    b.Pop();
                }
            });
        }

        /// <summary>Grades the corridor to the track bed and locks it; returns the touched region. Water is left alone.</summary>
        public static RectInt GradeTerrain(TerrainField terrain, TrackLayout layout, System.Func<Vector2, float> waterLevel = null, bool[] tunnel = null)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in layout.Points) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
            var region = terrain.RegionAround(new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f), Mathf.Max(maxX - minX, maxZ - minZ) / 2f + CorridorHalfWidth + 0.02f);
            // Bucket track points into 6 cm cells for fast nearest queries.
            const float cellSize = 0.06f;
            var buckets = new Dictionary<long, List<int>>();
            long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            for (int i = 0; i < layout.Points.Count; i++)
            {
                var p = layout.Points[i];
                long k = Key(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));
                if (!buckets.TryGetValue(k, out var list)) buckets[k] = list = new List<int>();
                list.Add(i);
            }
            int reach = Mathf.CeilToInt(CorridorHalfWidth / cellSize) + 1;
            for (int z = region.yMin; z < region.yMax; z++)
            for (int x = region.xMin; x < region.xMax; x++)
            {
                float px = terrain.LocalX(x), pz = terrain.LocalZ(z);
                int cx = Mathf.FloorToInt(px / cellSize), cz = Mathf.FloorToInt(pz / cellSize);
                float best = float.MaxValue; int bestIndex = -1;
                for (int oz = -reach; oz <= reach; oz++)
                for (int ox = -reach; ox <= reach; ox++)
                {
                    if (!buckets.TryGetValue(Key(cx + ox, cz + oz), out var list)) continue;
                    foreach (int i in list)
                    {
                        var q = layout.Points[i];
                        float d = (q.x - px) * (q.x - px) + (q.z - pz) * (q.z - pz);
                        if (d < best) { best = d; bestIndex = i; }
                    }
                }
                if (bestIndex < 0) continue;
                float dist = Mathf.Sqrt(best);
                if (dist > CorridorHalfWidth) continue;
                if (waterLevel != null && waterLevel(new Vector2(px, pz)) >= 0f) continue;   // the bridge spans it
                if (tunnel != null && DeepInTunnel(tunnel, bestIndex, layout.Closed) && terrain.Surface[z * terrain.Nx + x] > layout.Points[bestIndex].y + TunnelHeight)
                    continue;   // the hill stays over the tunnel (a short cutting is dug up to each portal)
                int g = z * terrain.Nx + x;
                // Under a tunnel (and in the cutting up to its portals) the floor is flat right out to the tube walls, so no
                // bank shows inside the opening.
                float flat = tunnel != null && tunnel[bestIndex] ? 0.056f : BedHalfWidth * 0.8f;
                float w = dist < flat ? 1f : Mathf.SmoothStep(1f, 0f, (dist - flat) / (CorridorHalfWidth - flat));
                float target = layout.Points[bestIndex].y - BedSink;
                terrain.Height[g] = Mathf.Lerp(terrain.Height[g], terrain.HeightForSurface(g, target), w);
                terrain.Lock[g] = Mathf.Max(terrain.Lock[g], Mathf.Clamp01(w * 1.15f));
                terrain.Soil[g] = Mathf.Max(terrain.Soil[g], Mathf.SmoothStep(1f, 0f, (dist - 0.03f) / 0.04f));
            }
            terrain.UpdateSurface(region);
            return region;
        }

        public static void UnlockTerrain(TerrainField terrain)
        {
            System.Array.Clear(terrain.Lock, 0, terrain.Lock.Length);
            System.Array.Clear(terrain.Soil, 0, terrain.Soil.Length);
        }

        /// <summary>Horizontal distance from <paramref name="p"/> to the nearest track centre line (large without track nearby).</summary>
        public float DistanceTo(Vector3 p) => Network.DistanceTo(p);

        /// <summary>Whether a point is within <paramref name="within"/> of a track (only the lookup cells that close are searched).</summary>
        public bool Near(Vector3 p, float within) => Network.Nearest(p, out _, out _, out float d, within) && d < within;

        // ------------------------------------------------------------------ drawing preview

        /// <summary>
        /// Ribbon along the stroke being drawn. <paramref name="closing"/> highlights the start point (release to close the
        /// loop); <paramref name="error"/> paints it red.
        /// </summary>
        public void ShowPreview(IReadOnlyList<Vector3> points, bool closing, bool error, Vector3? snapStart = null, Vector3? snapEnd = null, bool good = false)
        {
            previewMesh.Clear();
            if (points == null || points.Count < 2) return;
            var b = new MeshBuilder();
            var colour = error ? new Color(1f, 0.35f, 0.3f, 0.85f) : good ? new Color(0.55f, 0.95f, 0.55f, 0.8f) : new Color(1f, 0.92f, 0.55f, 0.85f);
            const float half = 0.012f;
            for (int i = 0; i < points.Count; i++)
            {
                var prev = points[Mathf.Max(0, i - 1)]; var next = points[Mathf.Min(points.Count - 1, i + 1)];
                var t = next - prev; t.y = 0f;
                var side = Vector3.Cross(Vector3.up, t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward) * half;
                var c = colour; c.a *= Mathf.Lerp(0.5f, 1f, i / (float)points.Count);
                b.AddVertex(points[i] - side + Vector3.up * 0.004f, Vector3.up, c);
                b.AddVertex(points[i] + side + Vector3.up * 0.004f, Vector3.up, c);
                if (i > 0) { int v = b.VertexCount - 4; b.AddTriangle(v, v + 2, v + 3); b.AddTriangle(v, v + 3, v + 1); }
            }
            // Start marker: a ring that lights up when releasing would close the loop.
            var start = points[0] + Vector3.up * 0.005f;
            float r = closing ? CloseRingRadius * (1f + 0.08f * Mathf.Sin(Time.time * 8f)) : 0.03f;
            var ring = closing ? new Color(0.55f, 1f, 0.5f, 0.95f) : new Color(1f, 1f, 1f, 0.6f);
            const int seg = 32;
            int first = b.VertexCount;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.AddVertex(start + d * (r - 0.004f), Vector3.up, ring);
                b.AddVertex(start + d * (r + 0.004f), Vector3.up, ring);
                if (i > 0) { int v = first + (i - 1) * 2; b.AddTriangle(v, v + 2, v + 3); b.AddTriangle(v, v + 3, v + 1); }
            }
            // Snap rings where the stroke will join existing track (a new switch, or a dead end extended).
            foreach (var snap in new[] { snapStart, snapEnd })
            {
                if (!snap.HasValue) continue;
                var c = snap.Value + Vector3.up * 0.007f;
                float rr = 0.028f * (1f + 0.1f * Mathf.Sin(Time.time * 10f));
                var col = new Color(0.45f, 0.9f, 1f, 0.95f);
                int f = b.VertexCount;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    b.AddVertex(c + d * (rr - 0.004f), Vector3.up, col);
                    b.AddVertex(c + d * (rr + 0.004f), Vector3.up, col);
                    if (i > 0) { int v = f + (i - 1) * 2; b.AddTriangle(v, v + 2, v + 3); b.AddTriangle(v, v + 3, v + 1); }
                }
            }
            b.ToMesh("Track preview", previewMesh);
            previewMesh.RecalculateBounds();
        }

        public const float CloseRingRadius = 0.05f;

        /// <summary>The arrows painted between the rails: which way trains run (cream, faintly glowing so they read at a glance).</summary>
        public static readonly Color ArrowColour = new(1f, 0.93f, 0.7f, 0.55f);

        /// <summary>
        /// Chevrons along a line every <paramref name="spacing"/>, pointing the way trains run (<paramref name="direction"/>
        /// +1: the way its points go, -1: the other way), lying <paramref name="lift"/> above it.
        /// </summary>
        public static void Arrows(MeshBuilder b, TrackLayout l, int direction, Color colour, float lift, float spacing)
        {
            if (direction == 0 || l.Length < 0.1f) return;
            const float w = 0.01f, len = 0.013f, thick = 0.0028f;
            float first = l.Closed ? spacing * 0.5f : 0.06f, last = l.Closed ? l.Length : l.Length - 0.06f;
            for (float s = first; s < last; s += spacing)
            {
                var p = l.Evaluate(s, out var t); t.y = 0f;
                if (t.sqrMagnitude < 1e-8f) continue;
                var f = t.normalized * direction; var side = Vector3.Cross(Vector3.up, f);
                var tip = p + f * len * 0.5f + Vector3.up * lift;
                foreach (int k in new[] { -1, 1 })
                {
                    var tail = p - f * len * 0.5f + side * (w * k) + Vector3.up * lift;
                    var across = Vector3.Cross(Vector3.up, (tip - tail).normalized) * (thick * 0.5f);
                    // A flat stroke from the tail to the tip (facing up whichever way it winds).
                    var q0 = tail - across; var q1 = tip - across; var q2 = tip + across; var q3 = tail + across;
                    if (Vector3.Cross(q1 - q0, q2 - q0).y < 0f) (q1, q3) = (q3, q1);
                    b.AddQuad(q0, q1, q2, q3, colour);
                }
            }
        }

        /// <summary>The planned line while drawing: a ribbon (red when it can't be built) with arrows the way trains will run on it.</summary>
        public void ShowPlan(IReadOnlyList<Vector3> line, bool error, int direction, Vector3? snapStart, Vector3? snapEnd)
        {
            ShowPreview(line, false, error, snapStart, snapEnd, !error);
            if (error || direction == 0 || line == null || line.Count < 2) return;
            var b = new MeshBuilder();
            var copy = new List<Vector3>(line);
            var l = new TrackLayout(copy, false);
            Arrows(b, l, direction, new Color(0.12f, 0.45f, 0.15f, 0.95f), 0.006f, 0.07f);
            // Append the arrows to the ribbon mesh.
            var ribbon = new MeshBuilder();
            ribbon.AddMesh(previewMesh, Matrix4x4.identity);
            ribbon.AddMesh(b.ToMesh("Arrows", arrowsMesh ??= new Mesh { name = "Arrows" }), Matrix4x4.identity);   // (one mesh, reused every frame)
            ribbon.ToMesh("Track preview", previewMesh);
            previewMesh.RecalculateBounds();
        }

        private Mesh arrowsMesh;

        public void HidePreview() => previewMesh.Clear();
    }
}
