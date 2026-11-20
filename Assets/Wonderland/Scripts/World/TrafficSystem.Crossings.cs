using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Barriers at level crossings: where a street or a lane crosses the railway, a half barrier on each side covers the
    /// lane coming in, on a post with a crossbuck and two red lamps. A crossing shuts while a train is on the track either
    /// side of it, or has reserved it - the railway's own reservations, so trains from either direction count and a second
    /// train keeps it shut - and opens a moment after the last has gone: the lamps flash, the arms come down, and traffic
    /// waits at them until they are up again. The arms are moved only while they move; the check is ten times a second.
    /// </summary>
    public sealed partial class TrafficSystem
    {
        private sealed class Gate
        {
            public PathSystem.LevelCrossing Crossing;
            public readonly Transform[] Arms = new Transform[2];
            public readonly Quaternion[] Facing = new Quaternion[2];
            public readonly GameObject[] Lamps = new GameObject[4];   // two per post, flashing in turn
            public float Lowered;   // 0 up .. 1 down
            public bool Shut;       // a train is near: coming down, or down
            public float ClearFor;  // how long since no train was near
        }

        private readonly List<Gate> gates = new();
        private int gatesVersion = -1;
        private GameObject gatesRoot;
        private float gateCheck;
        /// <summary>How far along the track either side of a crossing a train shuts it (from a train at full speed: some seconds' warning).</summary>
        public const float GateApproach = 0.32f;
        /// <summary>How fast an arm swings (of its whole travel, a second), and how long a crossing stays shut after the last train.</summary>
        public const float GateSpeed = 0.7f, GateHold = 0.8f;

        /// <summary>The barriers (tests): how many crossings have them, and how far down a crossing's arms are (0 up, 1 down).</summary>
        public int GateCount => gates.Count;
        public float GateLowered(int i) => i >= 0 && i < gates.Count ? gates[i].Lowered : 0f;
        public bool GateShut(int i) => i >= 0 && i < gates.Count && gates[i].Shut;
        /// <summary>Times a vehicle drove onto a crossing whose barriers were down or moving (tests: never).</summary>
        public int GateRuns { get; private set; }

        private void StepGates(float dt)
        {
            if (gatesVersion != world.Paths.CrossingsVersion) BuildGates();
            if (gates.Count == 0) return;
            gateCheck -= dt;
            bool check = gateCheck <= 0f;
            if (check) gateCheck += 0.1f;
            int phase = Mathf.FloorToInt(blink * 2.5f) % 2;
            foreach (var g in gates)
            {
                if (check)
                {
                    if (TrainAtCrossing(g.Crossing)) { g.Shut = true; g.ClearFor = 0f; }
                    else if ((g.ClearFor += 0.1f) >= GateHold) g.Shut = false;
                }
                float target = g.Shut ? 1f : 0f;
                if (g.Lowered != target)
                {
                    g.Lowered = Mathf.MoveTowards(g.Lowered, target, dt * GateSpeed);
                    for (int k = 0; k < 2; k++) if (g.Arms[k]) g.Arms[k].localRotation = g.Facing[k] * Quaternion.Euler(0f, 0f, -88f * (1f - Mathf.SmoothStep(0f, 1f, g.Lowered)));
                }
                bool flash = g.Shut || g.Lowered > 0f;
                for (int k = 0; k < 4; k++) if (g.Lamps[k] && g.Lamps[k].activeSelf != (flash && k % 2 == phase)) g.Lamps[k].SetActive(flash && k % 2 == phase);
            }
        }

        /// <summary>Whether a train is on, or has reserved, the track near a crossing (either way along it).</summary>
        private bool TrainAtCrossing(PathSystem.LevelCrossing c) =>
            world.Trains.Holds(c.edge, c.s - GateApproach, c.s + GateApproach) || TrainNear(new Vector2(c.at.x, c.at.z), 0.12f);

        /// <summary>Whether traffic must wait at the crossing nearest a point: its barriers down, coming down or not yet up.</summary>
        private bool CrossingClosed(Vector2 p)
        {
            foreach (var g in gates)
                if (new Vector2(g.Crossing.at.x - p.x, g.Crossing.at.z - p.y).magnitude < 0.05f) return g.Shut || g.Lowered > 0.02f;
            return TrainNear(p, 0.3f);   // (a crossing without barriers - a footpath's: look out for trains)
        }

        /// <summary>Notes a vehicle driving onto a crossing whose barriers are down or coming down (for the tests: it never should).</summary>
        private void WatchGates(Vehicle v, Vector2 before)
        {
            foreach (var g in gates)
            {
                var c = new Vector2(g.Crossing.at.x, g.Crossing.at.z);
                if ((before - c).magnitude >= 0.035f && (v.Position - c).magnitude < 0.035f && g.Lowered > 0.5f) GateRuns++;
            }
        }

        /// <summary>Builds the barriers again for the crossings there are now.</summary>
        private void BuildGates()
        {
            gatesVersion = world.Paths.CrossingsVersion;
            if (gatesRoot != null) { if (Application.isPlaying) Destroy(gatesRoot); else DestroyImmediate(gatesRoot); }
            gates.Clear();
            gatesRoot = null;
            foreach (var c in world.Paths.LevelCrossings)
            {
                if (!PathSystem.IsCarWay(c.kind)) continue;
                if (gatesRoot == null) { gatesRoot = new GameObject("Crossing barriers"); gatesRoot.transform.SetParent(transform, false); }
                var g = new Gate { Crossing = c };
                var posts = new MeshBuilder();
                float hw = PathSystem.HalfWidth(c.kind);
                for (int k = 0; k < 2; k++)
                {
                    // Traffic coming in heading `h`: its barrier stands before the crossing, on its right, reaching across
                    // its lane to the middle of the way.
                    var h = c.dir * (k == 0 ? 1f : -1f);
                    var right = new Vector3(h.z, 0f, -h.x);
                    var foot = c.at - h * (PathSystem.CrossingHalf + 0.014f) + right * (hw + 0.003f);
                    foot.y = world.People.GroundHeight(new Vector2(foot.x, foot.z));
                    var facing = Quaternion.LookRotation(h, Vector3.up);
                    Post(posts, foot, facing);
                    var arm = new GameObject("Barrier arm");
                    arm.transform.SetParent(gatesRoot.transform, false);
                    arm.transform.localPosition = foot + Vector3.up * 0.0085f + right * -0.0022f;
                    g.Facing[k] = facing;
                    arm.transform.localRotation = facing * Quaternion.Euler(0f, 0f, -88f);
                    arm.AddComponent<MeshFilter>().sharedMesh = ArmMesh(hw + 0.004f);
                    var ar = arm.AddComponent<MeshRenderer>(); ar.sharedMaterial = material; ar.shadowCastingMode = ShadowCastingMode.Off;
                    g.Arms[k] = arm.transform;
                    // The two lamps, side by side under the crossbuck, facing the traffic coming in.
                    for (int l = 0; l < 2; l++)
                    {
                        var lamp = new MeshBuilder();
                        var at = foot + Vector3.up * 0.0165f + right * ((l == 0 ? -1f : 1f) * 0.0026f) - h * 0.0016f;
                        lamp.AddFace(at, -h, Vector3.up, 0.0012f, 0.0012f, Pal.Glow(Pal.Hex("#FF3B30")));
                        var go = new GameObject("Crossing lamp");
                        go.transform.SetParent(gatesRoot.transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = lamp.ToMesh("Crossing lamp");
                        var lr = go.AddComponent<MeshRenderer>(); lr.sharedMaterial = material; lr.shadowCastingMode = ShadowCastingMode.Off;
                        go.SetActive(false);
                        g.Lamps[k * 2 + l] = go;
                    }
                }
                var postsGo = new GameObject("Crossing posts");
                postsGo.transform.SetParent(gatesRoot.transform, false);
                postsGo.AddComponent<MeshFilter>().sharedMesh = posts.ToMesh("Crossing posts");
                var pr = postsGo.AddComponent<MeshRenderer>(); pr.sharedMaterial = material; pr.shadowCastingMode = ShadowCastingMode.Off;
                gates.Add(g);
            }
        }

        /// <summary>A barrier's post: the drive housing at its foot, the post, two lamps' backboard and a crossbuck on top.</summary>
        private static void Post(MeshBuilder b, Vector3 foot, Quaternion facing)
        {
            var white = Pal.Hex("#F7F1E6"); var red = Pal.Hex("#E25B4F"); var dark = Pal.Hex("#3A3A40");
            b.Push(foot, facing);
            b.AddBox(new Vector3(0f, 0.0035f, 0f), new Vector3(0.0045f, 0.007f, 0.0045f), Pal.Hex("#D8D2C4"), Pal.Hex("#B9B2A2"), Pal.Hex("#D8D2C4"));
            b.AddBox(new Vector3(0f, 0.013f, 0f), new Vector3(0.0016f, 0.026f, 0.0016f), white);
            b.AddBox(new Vector3(0f, 0.0165f, -0.0012f), new Vector3(0.0085f, 0.0034f, 0.0006f), dark);   // (the lamps' backboard)
            b.Push(new Vector3(0f, 0.024f, -0.0012f), Quaternion.identity);
            foreach (float a in new[] { 40f, -40f })
            {
                b.Push(Vector3.zero, Quaternion.Euler(0f, 0f, a));
                b.AddBox(Vector3.zero, new Vector3(0.014f, 0.0026f, 0.0007f), white);
                b.AddBox(new Vector3(0f, 0f, -0.0004f), new Vector3(0.0046f, 0.0027f, 0.0004f), red);
                b.Pop();
            }
            b.Pop();
            b.Pop();
        }

        private readonly Dictionary<int, Mesh> armMeshes = new();

        /// <summary>A barrier arm reaching <paramref name="length"/> from its pivot (along -x): red and white in turn, a little lamp at its tip.</summary>
        private Mesh ArmMesh(float length)
        {
            int key = Mathf.RoundToInt(length * 10000f);
            if (armMeshes.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var b = new MeshBuilder();
            const float stripe = 0.004f;
            int n = Mathf.Max(2, Mathf.CeilToInt(length / stripe));
            for (int i = 0; i < n; i++)
            {
                float x0 = i * length / n, x1 = (i + 1) * length / n;
                b.AddBox(new Vector3(-(x0 + x1) * 0.5f, 0f, 0f), new Vector3(x1 - x0, 0.0012f, 0.0011f), i % 2 == 0 ? Pal.Hex("#E25B4F") : Pal.Hex("#F7F1E6"));
            }
            b.AddBox(new Vector3(0.0018f, 0f, 0f), new Vector3(0.0036f, 0.0024f, 0.0016f), Pal.Hex("#3A3A40"));   // (the counterweight behind the pivot)
            b.AddBox(new Vector3(-length + 0.0006f, 0.0011f, 0f), new Vector3(0.0009f, 0.0009f, 0.0009f), Pal.Glow(Pal.Hex("#FF3B30")));
            mesh = b.ToMesh("Barrier arm");
            armMeshes[key] = mesh;
            return mesh;
        }
    }
}
