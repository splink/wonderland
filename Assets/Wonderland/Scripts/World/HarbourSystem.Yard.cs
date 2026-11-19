using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The harbour's machines at work: each container yard's straddle carrier drives up and down its aisle, stopping at
    /// a stack to lower its spreader and pick up a container (or set one down), and each warehouse's forklift shuttles
    /// between the loading docks and the pallets out on the concrete, forks up with a pallet, down without.
    /// </summary>
    public sealed partial class HarbourSystem
    {
        private sealed class Machine
        {
            public Vector2 House; public int Variant;
            public Transform Root, Lift;
            public MeshFilter Load;
            public Vector2 Pos;               // local to its building
            public float Heading, LiftT, Wait;
            public int Stop = -1;
            public bool Loaded, Lowering;
            public Rng Rng;
        }

        private readonly List<Machine> machines = new();

        /// <summary>Machines at work now, and loads they have moved (tests).</summary>
        public int Machines => machines.Count;
        public int Moved { get; private set; }

        private static readonly float[] YardColumns = { -0.088f, -0.0605f, -0.033f, -0.0055f, 0.022f, 0.0495f, 0.077f };

        /// <summary>The places a machine goes between (local to its building).</summary>
        private static Vector2 StopOf(Machine m, int k)
        {
            if (m.Variant == Catalog.ContainerYard) return new Vector2(YardColumns[k % YardColumns.Length], 0f);
            // A warehouse: in front of one of its three docks, or by the pallets.
            float front = 0.055f - 0.06f - 0.004f;
            return k < 3 ? new Vector2(-0.075f + 0.03f + k * 0.035f, front - 0.014f) : new Vector2(0.025f + (k - 3) * 0.013f, front - 0.03f);
        }

        private int MachineHouse(Machine m) => world.Buildings.Houses.FindIndex(h => h.variant == m.Variant && new Vector2(h.x, h.z) == m.House);

        private void SyncMachines()
        {
            for (int k = machines.Count - 1; k >= 0; k--) if (MachineHouse(machines[k]) < 0) { Gone(machines[k].Root.gameObject); machines.RemoveAt(k); }
            var houses = world.Buildings.Houses;
            foreach (var h in houses)
            {
                if (h.variant != Catalog.ContainerYard && h.variant != Catalog.Warehouse) continue;
                var at = new Vector2(h.x, h.z);
                if (machines.Exists(m => m.House == at && m.Variant == h.variant)) continue;
                bool yard = h.variant == Catalog.ContainerYard;
                var m = new Machine { House = at, Variant = h.variant, Rng = new Rng(Mathf.RoundToInt(h.x * 7919f + h.z * 6271f)) };
                m.Root = Part(yard ? "Straddle carrier" : "Forklift", transform, yard ? HouseFactory.StraddleCarrier(0) : HouseFactory.ForkliftMesh(0));
                int variant = h.variant;
                Lod.Attach(m.Root.GetComponent<MeshRenderer>(), d => variant == Catalog.ContainerYard ? HouseFactory.StraddleCarrier(d) : HouseFactory.ForkliftMesh(d), yard ? 0.03f : 0.016f);
                m.Lift = Part(yard ? "Spreader" : "Forks", m.Root, yard ? HouseFactory.ContainerMesh() : HouseFactory.ForkMesh(false));
                if (yard) m.Lift.localRotation = Quaternion.Euler(0f, 90f, 0f);
                m.Load = m.Lift.GetComponent<MeshFilter>();
                m.Pos = StopOf(m, yard ? 0 : 3);
                m.Loaded = yard;
                m.LiftT = 1f;
                machines.Add(m);
                PoseMachine(m, h);
            }
        }

        private void StepMachines(float dt)
        {
            var houses = world.Buildings.Houses;
            foreach (var m in machines)
            {
                int i = MachineHouse(m);
                if (i < 0) continue;
                bool yard = m.Variant == Catalog.ContainerYard;
                if (m.Wait > 0f)
                {
                    // At a stop: down, take or leave the load, back up.
                    m.Wait -= dt;
                    m.LiftT = Mathf.MoveTowards(m.LiftT, m.Lowering ? 0f : 1f, dt / 0.7f);
                    if (m.Lowering && m.LiftT <= 0f) { m.Loaded = !m.Loaded; m.Lowering = false; Moved++; if (!yard) m.Load.sharedMesh = HouseFactory.ForkMesh(m.Loaded); else m.Lift.gameObject.SetActive(m.Loaded || true); }
                }
                else
                {
                    if (m.Stop < 0)
                    {
                        // The next stop: a yard's carrier to another stack; a forklift from the docks to the pallets and back.
                        m.Stop = yard ? m.Rng.Range(0, YardColumns.Length) : m.Loaded ? 3 + m.Rng.Range(0, 3) : m.Rng.Range(0, 3);
                    }
                    var to = StopOf(m, m.Stop);
                    var d = to - m.Pos;
                    if (d.magnitude < 0.001f) { m.Stop = -1; m.Wait = 1.6f; m.Lowering = true; }
                    else
                    {
                        float want = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                        if (yard) want = d.x >= 0f ? 90f : 270f;   // (a carrier drives straight along its aisle, forwards or back)
                        m.Heading = Mathf.MoveTowardsAngle(m.Heading, want, 220f * dt);
                        if (Mathf.Abs(Mathf.DeltaAngle(m.Heading, want)) < 20f) m.Pos = Vector2.MoveTowards(m.Pos, to, (yard ? 0.02f : 0.025f) * dt);
                    }
                }
                PoseMachine(m, houses[i]);
            }
        }

        private void PoseMachine(Machine m, HouseInstance h)
        {
            var rot = Quaternion.Euler(0f, h.rotation, 0f);
            var d = rot * new Vector3(m.Pos.x, 0f, m.Pos.y);
            m.Root.localPosition = new Vector3(h.x + d.x, world.Buildings.SitHeight(h, out _) + 0.0016f, h.z + d.z);
            m.Root.localRotation = rot * Quaternion.Euler(0f, m.Heading, 0f);
            if (m.Variant == Catalog.ContainerYard)
            {
                m.Lift.localPosition = new Vector3(0f, 0.0015f + m.LiftT * HouseFactory.CarrierLift, 0f);
                m.Lift.gameObject.SetActive(m.Loaded || m.Lowering);
            }
            else m.Lift.localPosition = new Vector3(0f, 0.0004f + m.LiftT * (m.Loaded ? 0.005f : 0.0015f), 0.0058f);
        }
    }
}
