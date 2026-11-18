using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Freight by rail made visible. A freight yard beside the railway has a crane that rolls along its rail; when a goods
    /// train stands at the yard it lifts the crates waiting there (brought by lorries) onto the train one by one - or,
    /// if the train brought crates from elsewhere and this side of the line wants them, lifts them off onto the yard,
    /// where the yard's lorry collects them. The train waits until the crane is done. Crates waiting show as a stack,
    /// and a goods train's open wagon shows the crates it carries.
    /// </summary>
    public sealed class FreightSystem : MonoBehaviour
    {
        public const float CraneCycle = 2.4f;

        private sealed class Crane
        {
            public Vector2 House;
            public Transform Root, Jib, Trolley, Cable, Crate, Stack;
            public float T = -1f, X;
            public bool Loading;
            public Train Train;
            public MeshFilter StackMesh;
        }

        private WonderlandWorld world;
        private Material material;
        private readonly List<Crane> cranes = new();
        private readonly Dictionary<Train, (MeshFilter[] loads, int shown)> wagons = new();
        private float refresh;

        /// <summary>Crates the cranes have lifted (onto or off trains; tests).</summary>
        public int Lifted { get; private set; }

        public void Init(WonderlandWorld owner, Material lit) { world = owner; material = lit; }

        private void Update() { if (world != null && world.Terrain != null) Step(Time.deltaTime); }

        public void Step(float dt)
        {
            refresh -= dt;
            if (refresh <= 0f) { refresh = 1f; SyncCranes(); }
            foreach (var c in cranes) StepCrane(c, dt);
            ShowWagons();
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

        /// <summary>A crane for every freight yard (made once; taken away with the yard).</summary>
        private void SyncCranes()
        {
            var houses = world.Buildings.Houses;
            for (int k = cranes.Count - 1; k >= 0; k--)
            {
                int i = houses.FindIndex(h => h.variant == Catalog.FreightYard && new Vector2(h.x, h.z) == cranes[k].House);
                if (i < 0) { if (Application.isPlaying) Destroy(cranes[k].Root.gameObject); else DestroyImmediate(cranes[k].Root.gameObject); cranes.RemoveAt(k); }
            }
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (h.variant != Catalog.FreightYard) continue;
                var at = new Vector2(h.x, h.z);
                if (cranes.Exists(c => c.House == at)) continue;
                var c = new Crane { House = at };
                c.Root = new GameObject("Freight yard crane").transform;
                c.Root.SetParent(transform, false);
                c.Root.localPosition = new Vector3(h.x, world.Terrain.SurfaceAt(h.x, h.z), h.z);
                c.Root.localRotation = Quaternion.Euler(0f, h.rotation, 0f);
                c.Jib = Part("Crane", c.Root, HouseFactory.CraneMesh());
                c.Jib.localPosition = new Vector3(0f, 0.002f, HouseFactory.CraneRailZ);
                c.Trolley = Part("Trolley", c.Jib, HouseFactory.TrolleyMesh());
                c.Cable = Part("Cable", c.Trolley, null);
                var cable = new MeshBuilder(); cable.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(0.0006f, 1f, 0.0006f), Pal.TrainBlack);
                c.Cable.GetComponent<MeshFilter>().sharedMesh = cable.ToMesh("Cable");
                c.Crate = Part("Crate", c.Root, VehicleFactory.OneCrate(Cargo.Produce));
                c.Stack = Part("Waiting", c.Root, null);
                c.Stack.localPosition = new Vector3(-0.03f, 0.002f, 0.012f);
                c.StackMesh = c.Stack.GetComponent<MeshFilter>();
                cranes.Add(c);
            }
        }

        /// <summary>
        /// One crane: a goods train at its yard with crates to load (or to unload here) holds there while the crane swings
        /// them across, a crate every couple of seconds; otherwise the crane rests over the yard.
        /// </summary>
        private void StepCrane(Crane c, float dt)
        {
            var houses = world.Buildings.Houses;
            int house = houses.FindIndex(h => h.variant == Catalog.FreightYard && new Vector2(h.x, h.z) == c.House);
            if (house < 0) return;
            int waiting = Mathf.FloorToInt(world.People.WaitingAt(house));
            int arrived = Mathf.Max(0, Mathf.FloorToInt(world.People.StockAt(house)));
            c.StackMesh.sharedMesh = VehicleFactory.Load(Cargo.Produce, Mathf.Min(6, waiting + arrived));
            c.Stack.localPosition = new Vector3(-0.03f, 0.002f - VehicleFactory.BedTop, 0.012f - VehicleFactory.BedCentre - VehicleFactory.BedLength / 2f + 0.011f);
            int st = world.Trains.Stations.FindIndex(s => s.House == c.House);
            Train train = null;
            if (st >= 0) foreach (var t in world.Trains.Trains) if (t.Kind == TrainKind.Goods && t.AtStation == st) { train = t; break; }
            bool unload = train != null && train.Cargo > 0 && train.LoadedAt != st && world.People.WantsAt(st);
            bool load = train != null && !unload && waiting >= 1 && train.Cargo < PeopleSystem.TrainHolds;
            if (c.T < 0f)
            {
                if (!unload && !load) { Rest(c, dt); return; }
                c.T = 0f; c.Loading = load; c.Train = train;
            }
            if (c.Train != null && world.Trains.Trains.Contains(c.Train)) c.Train.Dwell = Mathf.Max(c.Train.Dwell, 0.6f);   // wait for the crane
            float before = c.T;
            c.T += dt / CraneCycle;
            // The hand-over, half way: the crate goes into the wagon (or onto the yard).
            if (before < 0.6f && c.T >= 0.6f && c.Train != null)
            {
                if (c.Loading) { if (world.People.TakeWaiting(house)) { c.Train.Cargo++; c.Train.LoadedAt = st; Lifted++; } }
                else if (c.Train.Cargo > 0) { c.Train.Cargo--; world.People.Receive(house, 1); Lifted++; }
            }
            Swing(c, c.T, c.Loading);
            if (c.T >= 1f) { c.T = -1f; c.Train = null; }
        }

        /// <summary>The crane between moves: back over the yard, nothing on its hook.</summary>
        private void Rest(Crane c, float dt)
        {
            c.X = Mathf.MoveTowards(c.X, -0.03f, 0.02f * dt);
            c.Jib.localPosition = new Vector3(c.X, 0.002f, HouseFactory.CraneRailZ);
            c.Trolley.localPosition = new Vector3(0f, HouseFactory.CraneHeight - 0.001f, 0.012f - HouseFactory.CraneRailZ);
            c.Cable.localScale = new Vector3(1f, 0.015f, 1f);
            c.Crate.gameObject.SetActive(false);
        }

        /// <summary>
        /// One crate's move at time <paramref name="t"/> (0-1): the crane rolls to line up with the wagon, the trolley runs
        /// out along the jib, the hook goes down, the crate rises, swings across and is set down; then back.
        /// </summary>
        private void Swing(Crane c, float t, bool loading)
        {
            var stack = new Vector3(-0.03f, 0.008f, 0.012f);
            var wagon = WagonPoint(c);
            var from = loading ? stack : wagon; var to = loading ? wagon : stack;
            c.X = Mathf.Lerp(c.X, Mathf.Lerp(from.x, to.x, Mathf.SmoothStep(0.15f, 0.55f, t)), 0.2f);
            c.Jib.localPosition = new Vector3(Mathf.Clamp(c.X, -0.065f, 0.065f), 0.002f, HouseFactory.CraneRailZ);
            float along = t < 0.15f ? from.z : t < 0.55f ? Mathf.Lerp(from.z, to.z, Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.4f)) : t < 0.75f ? to.z : Mathf.Lerp(to.z, from.z, (t - 0.75f) / 0.25f);
            float drop = t < 0.15f ? Mathf.Lerp(0.03f, 0.075f, t / 0.15f) : t < 0.55f ? 0.035f : t < 0.65f ? Mathf.Lerp(0.035f, 0.065f, (t - 0.55f) / 0.1f) : 0.03f;
            c.Trolley.localPosition = new Vector3(0f, HouseFactory.CraneHeight - 0.001f, along - HouseFactory.CraneRailZ);
            c.Cable.localScale = new Vector3(1f, drop, 1f);
            bool carrying = t >= 0.12f && t < 0.62f;
            c.Crate.gameObject.SetActive(carrying);
            if (carrying)
            {
                var hook = c.Root.InverseTransformPoint(c.Trolley.TransformPoint(new Vector3(0f, -drop, 0f)));
                c.Crate.localPosition = hook - new Vector3(0f, 0.008f, 0f);
                c.Crate.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Where the crane sets a crate into the train: the open wagon nearest the jib (yard-local), or under the jib.</summary>
        private Vector3 WagonPoint(Crane c)
        {
            var under = new Vector3(0f, 0.02f, HouseFactory.CraneReach + 0.01f);
            if (c.Train?.Cars == null) return under;
            float best = float.MaxValue; var pick = under;
            for (int k = 1; k < c.Train.Cars.Length; k++)
            {
                var body = c.Train.Cars[k].Body;
                if (body == null) continue;
                var p = c.Root.InverseTransformPoint(body.position);
                int variant = (k - 1) % TrainFactory.WagonVariants;
                float d = Mathf.Abs(p.x) + (variant <= 1 ? 0f : 0.5f);   // (an open wagon or a van takes crates; not the tank or the coal wagon)
                if (d < best) { best = d; pick = new Vector3(Mathf.Clamp(p.x, -0.065f, 0.065f), p.y + 0.025f, Mathf.Clamp(p.z, HouseFactory.CraneReach - 0.01f, -0.02f)); }
            }
            return pick;
        }

        private static bool WagonOpen(int index) => (index - 1) % TrainFactory.WagonVariants == 0;

        /// <summary>Goods trains' open wagons show the crates aboard (filled in order, six to a wagon).</summary>
        private void ShowWagons()
        {
            foreach (var t in world.Trains.Trains)
            {
                if (t.Kind != TrainKind.Goods || t.Cars == null) continue;
                if (wagons.TryGetValue(t, out var w) && w.shown == t.Cargo && w.loads.Length == t.Cars.Length && (w.loads.Length == 0 || w.loads[^1] != null)) continue;
                var loads = w.loads != null && w.loads.Length == t.Cars.Length ? w.loads : new MeshFilter[t.Cars.Length];
                int left = t.Cargo;
                for (int k = 1; k < t.Cars.Length; k++)
                {
                    if (!WagonOpen(k) || t.Cars[k].Body == null) continue;
                    if (loads[k] == null) loads[k] = Part("Load", t.Cars[k].Body, null).GetComponent<MeshFilter>();
                    int n = Mathf.Min(6, left); left -= n;
                    loads[k].sharedMesh = n > 0 ? VehicleFactory.WagonLoad(n) : null;
                }
                wagons[t] = (loads, t.Cargo);
            }
            if (wagons.Count > world.Trains.Trains.Count)
            {
                var gone = new List<Train>();
                foreach (var t in wagons.Keys) if (!world.Trains.Trains.Contains(t)) gone.Add(t);
                foreach (var t in gone) wagons.Remove(t);
            }
        }

        /// <summary>For a freight yard's card: what its crane is doing.</summary>
        public string CraneFor(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count || world.Buildings.Houses[house].variant != Catalog.FreightYard) return null;
            var h = world.Buildings.Houses[house];
            var c = cranes.Find(x => x.House == new Vector2(h.x, h.z));
            if (c == null || c.T < 0f) return "the crane waits for a goods train";
            return c.Loading ? "the crane is loading the goods train" : "the crane is unloading the goods train";
        }
    }
}
