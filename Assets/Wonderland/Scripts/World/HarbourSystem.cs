using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Harbours and container ships. Two harbours on the shores of the same water - wide enough for a ship all the way
    /// (a route is found over the water, never over land) - are served by a little container ship that sails between
    /// them. At each harbour it moors alongside the quay and the quay crane lifts its containers off onto the quay, one by
    /// one (the harbour's lorry takes them on), then loads what lorries brought for shipping, and the ship sails for the
    /// other harbour. A harbour with no other harbour across the water stays quiet; a harbour taken away (or the water
    /// cut) sends its ship away. Nothing here is saved: ships come from the harbours.
    /// </summary>
    public sealed class HarbourSystem : MonoBehaviour
    {
        public const float MooringZ = -0.09f, ShipSpeed = 0.03f, CraneCycle = 2.6f, Clearance = 0.018f, Grid = 0.02f;
        private static readonly string[] Names = { "Puffin", "Kittiwake", "Gannet", "Cormorant", "Tern", "Guillemot" };

        private sealed class Port
        {
            public Vector2 House;
            public Transform Root, Jib, Trolley, Cable, Box, Stack;
            public float X = -0.03f;
        }

        private sealed class Ship
        {
            public string Name;
            public Vector2 A, B;                  // the two harbours (by where they stand)
            public List<Vector2> Route;           // from A's mooring to B's
            public Transform Root; public MeshFilter Deck;
            public Vector2 Pos; public float Heading, Speed;
            public bool ToB, Docked, Unloading, Loading;   // Loading: done unloading at this quay, now loading
            public int Next, Cargo;
            public float T = -1f, Wait;
        }

        private WonderlandWorld world;
        private Material material;
        private readonly List<Port> ports = new();
        private readonly List<Ship> ships = new();
        private float sync, check;

        /// <summary>Containers lifted by the quay cranes, and voyages completed (tests).</summary>
        public int Lifted { get; private set; }
        public int Voyages { get; private set; }
        public int ShipCount => ships.Count;

        public void Init(WonderlandWorld owner, Material lit) { world = owner; material = lit; }

        private void Update() { if (world != null && world.Terrain != null) Step(Time.deltaTime); }

        public void Step(float dt)
        {
            sync -= dt;
            if (sync <= 0f) { sync = 2f; SyncPorts(); }
            check -= dt;
            if (check <= 0f) { check = 5f; CheckRoutes(); }
            foreach (var s in ships.ToArray()) StepShip(s, dt);
            foreach (var p in ports) if (!ships.Exists(s => s.Docked && At(s) == p.House)) RestCrane(p, dt);
        }

        // ------------------------------------------------------------------ harbours and their ships

        private int HouseOf(Vector2 at) => world.Buildings.Houses.FindIndex(h => h.variant == Catalog.Harbour && new Vector2(h.x, h.z) == at);

        private Vector2 Mooring(Vector2 harbour, out float heading)
        {
            var h = world.Buildings.Houses[HouseOf(harbour)];
            var d = Quaternion.Euler(0f, h.rotation, 0f) * new Vector3(0f, 0f, MooringZ + 0.005f);
            heading = h.rotation + 90f;
            return new Vector2(h.x + d.x, h.z + d.z);
        }

        private Transform Part(string name, Transform parent, Mesh mesh)
        {
            var go = new GameObject(name);
            go.layer = WonderlandWorld.MiniatureLayer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        private static void Gone(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        /// <summary>A crane for each harbour, and a ship for each pair of harbours the water joins.</summary>
        private void SyncPorts()
        {
            var houses = world.Buildings.Houses;
            for (int k = ports.Count - 1; k >= 0; k--) if (HouseOf(ports[k].House) < 0) { Gone(ports[k].Root.gameObject); ports.RemoveAt(k); }
            for (int k = ships.Count - 1; k >= 0; k--) if (HouseOf(ships[k].A) < 0 || HouseOf(ships[k].B) < 0) { Gone(ships[k].Root.gameObject); ships.RemoveAt(k); }
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (h.variant != Catalog.Harbour) continue;
                var at = new Vector2(h.x, h.z);
                if (ports.Exists(p => p.House == at)) continue;
                var p = new Port { House = at };
                p.Root = new GameObject("Harbour crane").transform;
                p.Root.SetParent(transform, false);
                p.Root.localPosition = new Vector3(h.x, world.Buildings.SitHeight(h, out _), h.z);
                p.Root.localRotation = Quaternion.Euler(0f, h.rotation, 0f);
                p.Jib = Part("Crane", p.Root, HouseFactory.CraneMesh());
                Lod.Attach(p.Jib.GetComponent<MeshRenderer>(), HouseFactory.CraneMesh, HouseFactory.CraneHeight);
                p.Trolley = Part("Trolley", p.Jib, HouseFactory.TrolleyMesh());
                var cable = new MeshBuilder(); cable.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(0.0006f, 1f, 0.0006f), Pal.TrainBlack);
                p.Cable = Part("Cable", p.Trolley, cable.ToMesh("Cable"));
                p.Box = Part("Container", p.Root, HouseFactory.ContainerMesh());
                p.Stack = Part("Quay stack", p.Root, null);
                p.Stack.localPosition = new Vector3(-0.035f, 0.002f - 0.013f, 0.025f);
                p.Stack.localRotation = Quaternion.Euler(0f, 90f, 0f);
                ports.Add(p);
            }
            // Pair harbours up: each harbour in at most one pair, joined by water a ship can sail.
            foreach (var a in ports)
            {
                if (ships.Exists(s => s.A == a.House || s.B == a.House)) continue;
                foreach (var b in ports)
                {
                    if (b == a || ships.Exists(s => s.A == b.House || s.B == b.House)) continue;
                    var from = Mooring(a.House, out float heading); var to = Mooring(b.House, out _);
                    var route = Route(from, to);
                    if (route == null) continue;
                    var s = new Ship { Name = "MV " + Names[ships.Count % Names.Length], A = a.House, B = b.House, Route = route, Pos = from, Heading = heading, ToB = true, Docked = true, Cargo = 4, Wait = 2f };
                    s.Root = Part("Container ship", transform, HouseFactory.ShipMesh());
                    Lod.Attach(s.Root.GetComponent<MeshRenderer>(), HouseFactory.ShipMesh, 0.16f);
                    s.Deck = Part("Deck", s.Root, null).GetComponent<MeshFilter>();
                    ships.Add(s);
                    Pose(s);
                    break;
                }
            }
        }

        /// <summary>Every few seconds: is each ship's water still there? If not, find another way, or the ship goes.</summary>
        private void CheckRoutes()
        {
            for (int k = ships.Count - 1; k >= 0; k--)
            {
                var s = ships[k];
                bool ok = true;
                for (int q = 1; q < s.Route.Count - 1; q++) if (!Navigable(s.Route[q])) { ok = false; break; }   // (the moorings themselves lie by the quay)
                if (ok) continue;
                var r = HouseOf(s.A) >= 0 && HouseOf(s.B) >= 0 ? Route(Mooring(s.A, out _), Mooring(s.B, out _)) : null;
                if (r != null)
                {
                    // The water changed: a new way, carrying on from the point nearest the ship.
                    s.Route = r;
                    int near = 0; for (int q = 1; q < r.Count; q++) if ((r[q] - s.Pos).sqrMagnitude < (r[near] - s.Pos).sqrMagnitude) near = q;
                    s.Next = s.ToB ? near : r.Count - 1 - near;
                    continue;
                }
                Gone(s.Root.gameObject); ships.RemoveAt(k);
            }
        }

        // ------------------------------------------------------------------ the water

        private bool Wet(float x, float z) => world.Terrain.WaterAt(x, z) > 0.6f;

        /// <summary>Open water wide enough for a ship here.</summary>
        private bool Navigable(Vector2 p) =>
            Wet(p.x, p.y) && Wet(p.x + Clearance, p.y) && Wet(p.x - Clearance, p.y) && Wet(p.x, p.y + Clearance) && Wet(p.x, p.y - Clearance);

        /// <summary>
        /// A way over the water from <paramref name="a"/> to <paramref name="b"/> wide enough for a ship (a search over a
        /// 2 cm grid of the water, then straightened where the water allows), or null if the water doesn't join them.
        /// </summary>
        private List<Vector2> Route(Vector2 a, Vector2 b)
        {
            var t = world.Terrain;
            int nx = Mathf.CeilToInt((t.XMax - t.X0) / Grid) + 1, nz = Mathf.CeilToInt((t.ZMax - t.Z0) / Grid) + 1;
            if (nx * nz > 600000) return null;
            Vector2 P(int i) => new(t.X0 + i % nx * Grid, t.Z0 + i / nx * Grid);
            int Cell(Vector2 p) => Mathf.Clamp(Mathf.RoundToInt((p.y - t.Z0) / Grid), 0, nz - 1) * nx + Mathf.Clamp(Mathf.RoundToInt((p.x - t.X0) / Grid), 0, nx - 1);
            int start = Cell(a), goal = Cell(b);
            var parent = new int[nx * nz];
            for (int i = 0; i < parent.Length; i++) parent[i] = -2;
            var queue = new Queue<int>();
            parent[start] = -1; queue.Enqueue(start);
            bool found = false;
            while (queue.Count > 0 && !found)
            {
                int c = queue.Dequeue();
                int cx = c % nx, cz = c / nx;
                for (int dz = -1; dz <= 1 && !found; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= nx || z >= nz) continue;
                    int n = z * nx + x;
                    if (parent[n] != -2) continue;
                    if (n != goal && !Navigable(P(n))) { parent[n] = -3; continue; }
                    parent[n] = c;
                    if (n == goal) { found = true; break; }
                    queue.Enqueue(n);
                }
            }
            if (!found) return null;
            var cells = new List<Vector2>();
            for (int c = goal; c >= 0; c = parent[c]) cells.Add(P(c));
            cells.Reverse();
            cells[0] = a; cells[^1] = b;
            // Straighten: from each point, on to the furthest one in plain sight over open water.
            var route = new List<Vector2> { a };
            int at = 0;
            while (at < cells.Count - 1)
            {
                int far = at + 1;
                for (int k = cells.Count - 1; k > at + 1; k--) if (Open(cells[at], cells[k])) { far = k; break; }
                route.Add(cells[far]); at = far;
            }
            return route;
        }

        private bool Open(Vector2 a, Vector2 b)
        {
            float len = (b - a).magnitude;
            for (float d = 0f; d <= len; d += 0.01f) if (!Navigable(Vector2.Lerp(a, b, d / Mathf.Max(len, 1e-4f)))) return false;
            return true;
        }

        // ------------------------------------------------------------------ ships

        private Vector2 At(Ship s) => s.ToB ? s.A : s.B;   // (docked: where it is - it sets off towards the other)

        private void StepShip(Ship s, float dt)
        {
            if (s.Docked) { AtQuay(s, dt); Pose(s); return; }
            // Sailing: along the route (backwards on the way home), then alongside the quay.
            int last = s.Route.Count - 1;
            var target = s.Route[s.ToB ? s.Next : last - s.Next];
            var to = target - s.Pos;
            if (to.magnitude < (s.Next < last ? 0.02f : 0.01f))
            {
                if (s.Next < last) { s.Next++; return; }
                var harbour = s.ToB ? s.B : s.A;
                if (HouseOf(harbour) < 0) return;
                Mooring(harbour, out float dock);
                if (Mathf.Abs(Mathf.DeltaAngle(s.Heading, dock)) > 90f) dock += 180f;
                s.Heading = Mathf.MoveTowardsAngle(s.Heading, dock, 25f * dt);
                s.Speed = 0f;
                if (Mathf.Abs(Mathf.DeltaAngle(s.Heading, dock)) < 1f) { s.Docked = true; s.Loading = false; s.ToB = !s.ToB; s.Next = 0; s.Wait = 1.5f; Voyages++; }
                Pose(s);
                return;
            }
            float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            float turn = Mathf.DeltaAngle(s.Heading, want);
            s.Heading += Mathf.Clamp(turn, -25f * dt, 25f * dt);
            bool arriving = s.Next == last;
            float speed = ShipSpeed * Mathf.Clamp(1.1f - Mathf.Abs(turn) / 60f, 0.08f, 1f) * (arriving ? Mathf.Clamp01(to.magnitude / 0.08f) * 0.7f + 0.3f : 1f);
            s.Speed = Mathf.MoveTowards(s.Speed, speed, (speed < s.Speed ? 0.04f : 0.01f) * dt);   // (it slows quickly for a sharp turn: no circling round a point it can't turn into)
            var f = new Vector2(Mathf.Sin(s.Heading * Mathf.Deg2Rad), Mathf.Cos(s.Heading * Mathf.Deg2Rad));
            s.Pos += f * s.Speed * dt;
            Pose(s);
        }

        /// <summary>Alongside the quay: the crane unloads every container, then loads what waits for shipping; then off it goes.</summary>
        private void AtQuay(Ship s, float dt)
        {
            var here = At(s);
            int house = HouseOf(here);
            var port = ports.Find(p => p.House == here);
            if (house < 0 || port == null) return;
            if (s.T < 0f)
            {
                s.Wait -= dt;
                if (s.Wait > 0f) { RestCrane(port, dt); return; }
                if (!s.Loading && s.Cargo > 0) { s.Unloading = true; s.T = 0f; }
                else if (s.Cargo < 8 && (world.People.WaitingAt(house) >= 1f || s.Cargo < 3)) { s.Loading = true; s.Unloading = false; s.T = 0f; }
                else { s.Docked = false; s.Loading = false; s.Wait = 0f; return; }   // all done: cast off
            }
            float before = s.T;
            s.T += dt / CraneCycle;
            if (before < 0.6f && s.T >= 0.6f)
            {
                if (s.Unloading) { s.Cargo--; world.People.Receive(house, 1); }
                else { world.People.TakeWaiting(house); s.Cargo++; }   // (what lorries brought; otherwise empties going back)
                Lifted++;
            }
            int slot = s.Unloading ? Mathf.Max(0, s.Cargo - (s.T < 0.6f ? 1 : 0)) : s.Cargo - (s.T >= 0.6f ? 1 : 0);
            Swing(port, s, s.T, s.Unloading, Mathf.Clamp(slot, 0, 7));
            if (s.T >= 1f) { s.T = -1f; s.Wait = s.Unloading && s.Cargo == 0 ? 1f : 0.2f; }
        }

        private void Pose(Ship s)
        {
            float y = world.Terrain.WaterLevelAt(s.Pos.x, s.Pos.y) + 0.0015f * Mathf.Sin(Time.time * 1.3f + s.Pos.x * 40f);
            s.Root.localPosition = new Vector3(s.Pos.x, y, s.Pos.y);
            s.Root.localRotation = Quaternion.Euler(0f, s.Heading, 0f);
            s.Deck.sharedMesh = HouseFactory.ShipDeck(s.Cargo);
        }

        // ------------------------------------------------------------------ the quay crane

        private void RestCrane(Port p, float dt)
        {
            p.X = Mathf.MoveTowards(p.X, -0.03f, 0.02f * dt);
            p.Jib.localPosition = new Vector3(p.X, 0.002f, HouseFactory.CraneRailZ);
            p.Trolley.localPosition = new Vector3(0f, HouseFactory.CraneHeight - 0.001f, 0.01f - HouseFactory.CraneRailZ);
            p.Cable.localScale = new Vector3(1f, 0.015f, 1f);
            p.Box.gameObject.SetActive(false);
            int house = HouseOf(p.House);
            int n = house < 0 ? 0 : Mathf.Min(8, Mathf.FloorToInt(world.People.WaitingAt(house)) + Mathf.Max(0, Mathf.FloorToInt(world.People.StockAt(house))));
            p.Stack.GetComponent<MeshFilter>().sharedMesh = HouseFactory.ShipDeck(n);
        }

        private void Swing(Port p, Ship s, float t, bool unloading, int slot)
        {
            var deck = p.Root.InverseTransformPoint(s.Root.TransformPoint(HouseFactory.DeckSlot(slot)));
            var quay = new Vector3(-0.035f, 0.002f, 0.025f);
            var from = unloading ? deck : quay; var to = unloading ? quay : deck;
            p.X = Mathf.Lerp(p.X, Mathf.Lerp(from.x, to.x, Mathf.SmoothStep(0.15f, 0.55f, t)), 0.2f);
            p.Jib.localPosition = new Vector3(Mathf.Clamp(p.X, -0.065f, 0.065f), 0.002f, HouseFactory.CraneRailZ);
            float along = t < 0.15f ? from.z : t < 0.55f ? Mathf.Lerp(from.z, to.z, Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.4f)) : t < 0.75f ? to.z : Mathf.Lerp(to.z, from.z, (t - 0.75f) / 0.25f);
            float lift = HouseFactory.CraneHeight - 0.001f;
            float drop = t < 0.15f ? Mathf.Lerp(0.02f, lift - from.y - 0.014f, t / 0.15f) : t < 0.55f ? 0.03f : t < 0.65f ? Mathf.Lerp(0.03f, lift - to.y - 0.014f, (t - 0.55f) / 0.1f) : 0.02f;
            p.Trolley.localPosition = new Vector3(0f, lift, along - HouseFactory.CraneRailZ);
            p.Cable.localScale = new Vector3(1f, Mathf.Max(0.005f, drop), 1f);
            bool carrying = t >= 0.12f && t < 0.62f;
            p.Box.gameObject.SetActive(carrying);
            if (carrying)
            {
                var hook = p.Root.InverseTransformPoint(p.Trolley.TransformPoint(new Vector3(0f, -drop, 0f)));
                p.Box.localPosition = hook - new Vector3(0f, 0.013f, 0f);
                p.Box.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            int house = HouseOf(p.House);
            int n = house < 0 ? 0 : Mathf.Min(8, Mathf.FloorToInt(world.People.WaitingAt(house)) + Mathf.Max(0, Mathf.FloorToInt(world.People.StockAt(house))));
            p.Stack.GetComponent<MeshFilter>().sharedMesh = HouseFactory.ShipDeck(n);
        }

        /// <summary>The first ship's state in numbers (tests).</summary>
        public string ShipState()
        {
            if (ships.Count == 0) return "no ship";
            var s = ships[0];
            int last = s.Route.Count - 1;
            var target = s.Route[s.ToB ? Mathf.Min(s.Next, last) : Mathf.Max(0, last - s.Next)];
            return $"at ({s.Pos.x:0.000}, {s.Pos.y:0.000}) heading {s.Heading:0} speed {s.Speed * 100f:0.0} cm/s, point {s.Next}/{last} at ({target.x:0.000}, {target.y:0.000}) {(target - s.Pos).magnitude * 100f:0.0} cm away, docked {s.Docked}, to B {s.ToB}";
        }

        // ------------------------------------------------------------------ cards

        /// <summary>For a harbour's card: its ship, and what it is doing.</summary>
        public string HarbourFor(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count || world.Buildings.Houses[house].variant != Catalog.Harbour) return null;
            var h = world.Buildings.Houses[house];
            var at = new Vector2(h.x, h.z);
            var s = ships.Find(x => x.A == at || x.B == at);
            if (s == null) return "no other harbour the water reaches yet - build one across the lake";
            if (s.Docked && At(s) == at) return s.T >= 0f ? (s.Unloading ? $"the crane is unloading the {s.Name}" : $"the crane is loading the {s.Name}") : $"the {s.Name} is alongside";
            if (s.Docked) return $"the {s.Name} is at the other harbour";
            return (s.ToB ? s.B : s.A) == at ? $"the {s.Name} is on its way here with {s.Cargo} containers" : $"the {s.Name} has sailed with {s.Cargo} containers";
        }
    }
}
