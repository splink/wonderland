using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Taxis: places with a taxi rank (the station, a taxi rank by the road) keep taxis that wait there for fares. A
    /// villager with somewhere far to go hails one (see PeopleSystem.Taxi.cs): the taxi drives to the kerb nearest them,
    /// they get in - and can be seen sitting in the back - it takes them along the roads to the kerb nearest where they
    /// are going, they get out and walk the rest, and the taxi goes back to its rank. Only a free taxi is hailed, and only
    /// when the roads join both ends; a fare who doesn't turn up, or a taxi that is taken away, leaves nobody waiting.
    /// </summary>
    public sealed partial class TrafficSystem
    {
        public const float TaxiWait = 40f;

        /// <summary>A taxi's fare: who, from which kerb to which, and how far along.</summary>
        public sealed class Fare
        {
            public int Seed, Style;
            public string Name, Where;
            public Vector2 Pickup, Drop;         // kerb points
            public int Stage;                    // 0 to the pickup, 1 waiting for them, 2 taking them, 3 there (getting out)
            public float Timer;
        }

        /// <summary>Fares completed (tests).</summary>
        public int FaresDone { get; private set; }

        /// <summary>The kerb beside the road nearest a point (on the point's side), or null if no road comes near.</summary>
        public Vector2? Kerb(Vector2 p)
        {
            int n = NearestNode(p, Reach + 0.06f, true);
            if (n < 0) return null;
            var at = graph[n].P;
            var dir = Direction(n);
            var side = new Vector2(dir.y, -dir.x);
            if (Vector2.Dot(p - at, side) < 0f) side = -side;
            return at + side * 0.024f;
        }

        /// <summary>
        /// A villager hails a taxi from <paramref name="from"/> to <paramref name="to"/>: the nearest free one sets off
        /// for the kerb by them, if the roads join everything up. Returns where to wait (null: no taxi).
        /// </summary>
        public Vector2? Hail(int seed, int style, string name, string where, Vector2 from, Vector2 to)
        {
            var pickup = Kerb(from); var drop = Kerb(to);
            if (pickup == null || drop == null || (drop.Value - pickup.Value).magnitude < 0.3f) return null;
            int a = NearestNode(pickup.Value, Reach + 0.06f, true), z = NearestNode(drop.Value, Reach + 0.06f, true);
            if (a < 0 || z < 0 || RouteNodes(a, z, true) == null) return null;
            Vehicle best = null; float bestD = float.MaxValue;
            foreach (var v in Vehicles)
            {
                if (v.Rank < 0 || v.Fare != null || v.State != Mode.Parked) continue;
                float d = (v.Position - pickup.Value).magnitude;
                if (d < bestD) { bestD = d; best = v; }
            }
            if (best == null) return null;
            best.Fare = new Fare { Seed = seed, Style = style, Name = name, Where = where, Pickup = pickup.Value, Drop = drop.Value };
            if ((best.Position - pickup.Value).magnitude < 0.05f) TaxiArrived(best);
            else if (!DriveTo(best, pickup.Value, false)) { best.Fare = null; return null; }
            return pickup;
        }

        /// <summary>The taxi for a fare (whatever stage), or null.</summary>
        private Vehicle TaxiFor(int seed)
        {
            foreach (var v in Vehicles) if (v.Fare != null && v.Fare.Seed == seed) return v;
            return null;
        }

        /// <summary>A taxi is on its way to them, or waiting for them.</summary>
        public bool Hailed(int seed) => TaxiFor(seed) is { Fare: { Stage: <= 1 } };

        /// <summary>
        /// They try to get in: true when their taxi is waiting at the kerb within reach (and now they are in it, sitting
        /// in the back).
        /// </summary>
        public bool Board(int seed, Vector2 at)
        {
            var v = TaxiFor(seed);
            if (v == null || v.Fare.Stage != 1 || (v.Position - at).magnitude > 0.06f) return false;
            v.Fare.Stage = 2;
            ShowPassenger(v, true);
            if (!DriveTo(v, v.Fare.Drop, false)) { v.Fare.Stage = 3; v.Fare.Timer = 0f; }
            return true;
        }

        /// <summary>Where someone riding in a taxi is now (null: they're in no taxi any more - it went).</summary>
        public Vector2? Riding(int seed) => TaxiFor(seed) is { Fare: { Stage: >= 2 } } v ? v.Position : null;

        /// <summary>Their taxi has arrived: they get out at the kerb (returns where), and the taxi goes back to its rank.</summary>
        public bool Arrived(int seed, out Vector2 at)
        {
            at = default;
            var v = TaxiFor(seed);
            if (v == null || v.Fare.Stage != 3) return false;
            at = v.Fare.Drop;
            FaresDone++; Trips++;
            EndFare(v);
            return true;
        }

        /// <summary>They gave up waiting (or went elsewhere): the taxi is free again.</summary>
        public void CancelFare(int seed) { var v = TaxiFor(seed); if (v != null) EndFare(v); }

        private void TaxiArrived(Vehicle v)
        {
            var f = v.Fare;
            v.State = Mode.Parked; v.Speed = 0f; v.Path.Clear();
            if (f.Stage == 0) { f.Stage = 1; f.Timer = TaxiWait; }
            else { f.Stage = 3; f.Timer = 10f; }
        }

        private void EndFare(Vehicle v)
        {
            v.Fare = null;
            ShowPassenger(v, false);
            if ((v.Home - v.Position).magnitude < 0.08f || !DriveTo(v, v.Home, true)) GoHome(v, true);
        }

        /// <summary>
        /// A parked taxi with a fare: waiting at the pickup (not for ever), or at the drop while they get out. Once a
        /// second, fares whose villager has gone are called off.
        /// </summary>
        private void StepTaxi(Vehicle v, float dt)
        {
            var f = v.Fare;
            if (f == null) return;
            if (f.Stage is 1 or 3)
            {
                f.Timer -= dt;
                if (f.Timer <= 0f) EndFare(v);   // they never came (or got out long ago)
            }
        }

        private void CheckFares()
        {
            foreach (var v in Vehicles)
                if (v.Fare != null && FareAlive != null && !FareAlive(v.Fare.Seed)) EndFare(v);
        }

        /// <summary>Whether a villager (by seed) is still in the world (set by the world).</summary>
        public System.Func<int, bool> FareAlive;

        /// <summary>The passenger in the back of the taxi, seen through its back windows.</summary>
        private void ShowPassenger(Vehicle v, bool on)
        {
            if (v.Passenger == null)
            {
                var go = new GameObject("Passenger");
                go.layer = WonderlandWorld.MiniatureLayer;
                go.transform.SetParent(v.Root, false);

                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                v.Passenger = go.transform;
            }
            v.Passenger.gameObject.SetActive(on);
            if (on && v.Fare != null) v.Passenger.GetComponent<MeshFilter>().sharedMesh = PersonFactory.WindowFace(v.Fare.Style);
        }

        private string TaxiLine(Vehicle v)
        {
            string rank = v.Owner != null ? v.Owner.ToLower() : "rank";
            var f = v.Fare;
            string doing = f == null
                ? v.State == Mode.Driving ? $"heading back to the {rank}" : $"waiting at the {rank} for a fare"
                : f.Stage switch
                {
                    0 => $"on its way to pick up {f.Name}",
                    1 => $"waiting at the kerb for {f.Name}",
                    2 => $"taking {f.Name} to {f.Where}",
                    _ => $"{f.Name} is getting out at {f.Where}",
                };
            return $"A taxi - {doing}";
        }

        /// <summary>The taxi a villager is riding in (for the follow camera's passenger seat), or null.</summary>
        public Vehicle TaxiOf(int seed) => TaxiFor(seed) is { Fare: { Stage: 2 } } v ? v : null;
    }
}
