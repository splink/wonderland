using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Lorries: places that make something and stand by a road (the catalogue says how many: the barn, the factory, the
    /// sawmill...) keep an open lorry, which carries that kind of cargo from wherever it is ready to wherever it is
    /// taken in - a farm's crates to the bakery, the factory's boxes to the grocer, anything to the station for the goods
    /// train. A delivery is real: it has a pickup and a drop, the crates are lifted onto the bed one by one and taken off
    /// again at the other end (into the place's stock), and the lorry can be followed all the way.
    /// Nothing is planned that can't happen (no road, nothing ready, nobody wanting it), deliveries keep to working
    /// hours, a place isn't served again for a while, and only a few lorries are out at once, so the roads stay calm.
    /// If a place goes while a lorry is on its way, it takes the load somewhere else that wants it, or home.
    /// </summary>
    public sealed partial class TrafficSystem
    {
        public const int LorryHolds = 6;
        public const float CrateSeconds = 1.1f, ServedAgain = 90f;

        /// <summary>A lorry's delivery: from where to where, how far along, and what it carries.</summary>
        public sealed class Delivery
        {
            public int Stage;                    // 0 to the pickup, 1 loading, 2 to the drop, 3 unloading
            public Vector2 From, To;             // the two places, by where they stand
            public int FromVariant, ToVariant;
            public Vector2 FromDoor, ToDoor;
            public int Crates, Shown;            // crates aboard; crates showing on the bed
            public float Timer;
            public string What;
        }

        private readonly Dictionary<long, float> servedUntil = new();
        private float freightClock;
        private Transform hop;

        /// <summary>Deliveries completed (tests).</summary>
        public int Delivered { get; private set; }

        private static long PosKey(Vector2 p) => ((long)Mathf.RoundToInt(p.x * 2000f) << 32) ^ (uint)Mathf.RoundToInt(p.y * 2000f);

        /// <summary>The building standing at <paramref name="at"/> (a place kept by where it stands), or -1.</summary>
        private int HouseAt(Vector2 at, int variant)
        {
            var houses = world.Buildings.Houses;
            for (int i = 0; i < houses.Count; i++)
                if (houses[i].variant == variant && Mathf.Abs(houses[i].x - at.x) < 1e-4f && Mathf.Abs(houses[i].z - at.y) < 1e-4f) return i;
            return -1;
        }

        private int ActiveDeliveries { get { int n = 0; foreach (var v in Vehicles) if (v.Job != null) n++; return n; } }

        private bool Reserved(Vector2 place, bool asPickup, Vehicle except)
        {
            foreach (var o in Vehicles)
                if (o != except && o.Job != null && (asPickup ? o.Job.Stage <= 1 && o.Job.From == place : o.Job.Stage <= 3 && o.Job.To == place)) return true;
            return false;
        }

        /// <summary>A parked lorry: carry on loading or unloading, or (now and then, by day) look for a delivery to make.</summary>
        private void Freight(Vehicle v, float dt)
        {
            if (v.Job != null) { AtStop(v, dt); return; }
            v.Timer -= dt;
            if (v.Timer > 0f) return;
            v.Timer = v.Rng.Range(8f, 20f);
            float hour = world.Clock.Hour;
            if (hour < 6.5f || hour > 19.5f) return;
            var job = PlanDelivery(v);
            if (job == null) return;
            v.Job = job;
            if ((job.FromDoor - v.Position).magnitude < 0.07f) { BeginStop(v); return; }   // loading right here, at its own depot
            if (!DriveTo(v, job.FromDoor, false)) v.Job = null;
        }

        /// <summary>
        /// The most useful delivery for this lorry: crates of its kind ready somewhere, and a place that wants them, both
        /// reachable by road, neither already being served; nearer and fuller first.
        /// </summary>
        private Delivery PlanDelivery(Vehicle v)
        {
            var houses = world.Buildings.Houses;
            int takers = 0;
            var from = new List<(int i, int n)>();
            var to = new List<(int i, int n)>();
            for (int i = 0; i < houses.Count; i++)
            {
                var bp = Catalog.Get(houses[i].variant);
                var at = new Vector2(houses[i].x, houses[i].z);
                if ((bp.Makes & v.Cargo) != 0) { int n = world.People.ReadyAt(i); if (n >= 2 && !Reserved(at, true, v)) from.Add((i, n)); }
                if ((bp.Takes & v.Cargo) != 0)
                {
                    takers++;
                    int n = world.People.WantedAt(i);
                    if (n >= 2 && !Reserved(at, false, v) && !(servedUntil.TryGetValue(PosKey(at), out float until) && until > freightClock)) to.Add((i, n));
                }
            }
            if (from.Count == 0 || to.Count == 0) return null;
            if (ActiveDeliveries >= Mathf.Clamp(takers / 2, 1, 4)) return null;   // a few at a time: busy, not chaotic
            var pairs = new List<(float score, int f, int t, int n)>();
            foreach (var f in from)
            foreach (var t in to)
            {
                if (f.i == t.i) continue;
                var fd = PeopleSystem.DoorStep(houses[f.i]); var td = PeopleSystem.DoorStep(houses[t.i]);
                float d = (fd - v.Position).magnitude + (td - fd).magnitude;
                int n = Mathf.Min(LorryHolds, f.n, t.n);
                pairs.Add((n / (0.4f + d), f.i, t.i, n));
            }
            pairs.Sort((a, b) => b.score.CompareTo(a.score));
            int here = NearestNode(v.Position, Reach + 0.04f, true);
            for (int k = 0; k < pairs.Count && k < 4; k++)
            {
                var (_, fi, ti, n) = pairs[k];
                var fd = PeopleSystem.DoorStep(houses[fi]); var td = PeopleSystem.DoorStep(houses[ti]);
                int a = NearestNode(fd, Reach + 0.04f, true), b = NearestNode(td, Reach + 0.04f, true);
                if (a < 0 || b < 0 || here < 0) continue;
                if (here != a && RouteNodes(here, a, true) == null || a != b && RouteNodes(a, b, true) == null) continue;
                return new Delivery
                {
                    From = new Vector2(houses[fi].x, houses[fi].z), To = new Vector2(houses[ti].x, houses[ti].z),
                    FromVariant = houses[fi].variant, ToVariant = houses[ti].variant, FromDoor = fd, ToDoor = td,
                    Crates = n, What = PeopleSystem.Produce(houses[fi].variant),
                };
            }
            return null;
        }

        /// <summary>Arrived at the pickup or the drop: pull in at the kerb and start loading or unloading.</summary>
        private void JobArrived(Vehicle v)
        {
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            Place(v, v.Position + new Vector2(fwd.y, -fwd.x) * 0.008f, v.Heading);
            BeginStop(v);
        }

        private void BeginStop(Vehicle v)
        {
            var j = v.Job;
            v.State = Mode.Parked; v.Speed = 0f; v.Path.Clear();
            if (j.Stage == 0)
            {
                // Loading: the crates come out of the place onto the bed (no more than it has, or than is wanted).
                int house = HouseAt(j.From, j.FromVariant);
                j.Crates = house >= 0 ? world.People.TakeCrates(house, j.Crates) : 0;
                j.Stage = 1; j.Shown = 0;
            }
            else j.Stage = 3;
            j.Timer = CrateSeconds;
        }

        /// <summary>Loading or unloading, a crate at a time, carried across from the door to the bed (or back).</summary>
        private void AtStop(Vehicle v, float dt)
        {
            var j = v.Job;
            bool loading = j.Stage == 1;
            var door = loading ? j.FromDoor : j.ToDoor;
            j.Timer -= dt;
            float t = 1f - Mathf.Clamp01(j.Timer / CrateSeconds);
            bool more = loading ? j.Shown < j.Crates : j.Shown > 0;
            ShowHop(v, door, more ? (loading ? t : 1f - t) : -1f);
            if (j.Timer > 0f) return;
            j.Timer = CrateSeconds;
            if (more) { j.Shown += loading ? 1 : -1; ShowLoad(v); return; }
            ShowHop(v, door, -1f);
            if (loading)
            {
                if (j.Crates == 0) { EndJob(v); return; }   // nothing there after all
                j.Stage = 2;
                if (!DriveTo(v, j.ToDoor, false) && !Redirect(v)) EndJob(v);
                return;
            }
            int house = HouseAt(j.To, j.ToVariant);
            if (house >= 0) world.People.BringCrates(house, j.Crates, j.What);
            servedUntil[PosKey(j.To)] = freightClock + ServedAgain;
            Delivered++; Trips++;
            EndJob(v);
        }

        /// <summary>The delivery is over (done, or given up): home to the depot, empty.</summary>
        private void EndJob(Vehicle v)
        {
            v.Job = null;
            ShowLoad(v);
            ShowHop(v, Vector2.zero, -1f);
            v.Timer = v.Rng.Range(20f, 50f);
            if ((v.Home - v.Position).magnitude < 0.08f || !DriveTo(v, v.Home, true)) GoHome(v, true);
        }

        /// <summary>The place it was taking the load to is gone (or out of reach): somewhere else that wants it, if any.</summary>
        private bool Redirect(Vehicle v)
        {
            var j = v.Job;
            var houses = world.Buildings.Houses;
            float best = float.MaxValue; int pick = -1;
            for (int i = 0; i < houses.Count; i++)
            {
                if ((Catalog.Get(houses[i].variant).Takes & v.Cargo) == 0 || world.People.WantedAt(i) < 1) continue;
                var at = new Vector2(houses[i].x, houses[i].z);
                if (at == j.From || Reserved(at, false, v)) continue;
                float d = (PeopleSystem.DoorStep(houses[i]) - v.Position).magnitude;
                if (d < best) { best = d; pick = i; }
            }
            if (pick < 0) return false;
            var door = PeopleSystem.DoorStep(houses[pick]);
            if (!DriveTo(v, door, false)) return false;
            j.To = new Vector2(houses[pick].x, houses[pick].z); j.ToVariant = houses[pick].variant; j.ToDoor = door;
            return true;
        }

        /// <summary>Once a second: deliveries whose places went are taken elsewhere or called off.</summary>
        private void CheckDeliveries()
        {
            foreach (var v in Vehicles)
            {
                var j = v.Job;
                if (j == null) continue;
                if (j.Stage <= 1 && HouseAt(j.From, j.FromVariant) < 0 && j.Shown == 0) { EndJob(v); continue; }
                if (j.Stage >= 2 && HouseAt(j.To, j.ToVariant) < 0 && !Redirect(v)) EndJob(v);
            }
        }

        /// <summary>What shows on the bed: as many crates as are aboard (loading: as many as have been lifted on).</summary>
        private void ShowLoad(Vehicle v)
        {
            if (v.Load == null)
            {
                var go = new GameObject("Load");
                go.layer = WonderlandWorld.MiniatureLayer;
                go.transform.SetParent(v.Root, false);
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                v.Load = go.GetComponent<MeshFilter>();
            }
            int n = v.Job == null ? 0 : v.Job.Stage == 2 ? v.Job.Crates : v.Job.Shown;
            v.Load.gameObject.SetActive(n > 0);
            if (n > 0) v.Load.sharedMesh = VehicleFactory.Load(v.Cargo, n);
        }

        /// <summary>The crate being carried across, <paramref name="t"/> of the way from the door to the bed (negative: none).</summary>
        private void ShowHop(Vehicle v, Vector2 door, float t)
        {
            if (hop == null)
            {
                var go = new GameObject("Crate carried");
                go.layer = WonderlandWorld.MiniatureLayer;
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hop = go.transform;
                hop.gameObject.SetActive(false);
            }
            if (t < 0f) { if (hopFor == v) { hop.gameObject.SetActive(false); hopFor = null; } return; }
            hopFor = v;
            hop.GetComponent<MeshFilter>().sharedMesh = VehicleFactory.OneCrate(v.Cargo);
            var bed = transform.InverseTransformPoint(v.Root.TransformPoint(new Vector3(0f, VehicleFactory.BedTop, VehicleFactory.BedCentre)));
            var from = new Vector3(door.x, world.People.GroundHeight(door) + 0.004f, door.y);
            var p = Vector3.Lerp(from, bed, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.012f;
            hop.localPosition = p;
            hop.localRotation = Quaternion.Euler(0f, v.Heading + t * 90f, 0f);
            hop.gameObject.SetActive(true);
        }

        private Vehicle hopFor;

        /// <summary>A lorry, in words: whose it is, and what it is doing.</summary>
        private string LorryLine(Vehicle v)
        {
            string depot = v.Owner != null ? v.Owner.ToLower() : "depot";
            var j = v.Job;
            string Name(int variant) => Catalog.Get(variant).Name.ToLower();
            string doing = j == null
                ? v.State == Mode.Driving ? $"heading back to the {depot}" : $"waiting at the {depot} for a load"
                : j.Stage switch
                {
                    0 => $"on its way to collect {j.What} from the {Name(j.FromVariant)}",
                    1 => $"loading {j.What} at the {Name(j.FromVariant)}",
                    2 => $"taking {j.Crates} {Unit(v.Cargo, j.Crates)} of {j.What} to the {Name(j.ToVariant)}",
                    _ => $"unloading {j.What} at the {Name(j.ToVariant)}",
                };
            return $"A lorry from the {depot} - {doing}";
        }

        private static string Unit(Cargo c, int n) => (c switch { Cargo.Timber => "load", Cargo.Goods => "box", _ => "crate" }) + (n == 1 ? "" : c == Cargo.Goods ? "es" : "s");

        /// <summary>What lorries are doing for a place (for its card): on their way with something, loading, unloading.</summary>
        public string DeliveriesFor(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count) return null;
            var h = world.Buildings.Houses[house];
            var at = new Vector2(h.x, h.z);
            foreach (var v in Vehicles)
            {
                var j = v.Job;
                if (j == null) continue;
                if (j.To == at) return j.Stage == 3 ? $"a lorry is unloading {j.What}" : j.Stage == 2 ? $"a lorry is bringing {j.What}" : $"a lorry will bring {j.What}";
                if (j.From == at) return j.Stage == 1 ? "a lorry is loading up" : "a lorry is coming to collect";
            }
            return null;
        }
    }
}
