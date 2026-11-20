using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Car parks and parking garages: a household's car going to a place with one near it (by a street) drives there
    /// instead of stopping at the kerb, turns in off the street by its driveway, goes up the way in and along the aisle
    /// and into a free bay (in a garage, inside and out of sight), stays while its driver is out, then comes back out the
    /// same way and drives home. So they fill up when the shops near them are busy and stand empty at night. Full, or
    /// taken away, means the kerb as before.
    /// </summary>
    public sealed partial class TrafficSystem
    {
        public const float ParkSeconds = 1.6f, ParkSpeed = 0.045f;

        private static bool Parking(int variant) => variant == Catalog.CarPark || variant == Catalog.ParkingGarage || variant == Catalog.Fuel;
        private static int Spaces(int variant) => variant == Catalog.ParkingGarage ? HouseFactory.GarageSpaces : variant == Catalog.Fuel ? 1 : HouseFactory.ParkingBays;

        /// <summary>How often a household's car, setting out, calls at a fuel station first.</summary>
        public static float RefuelChance = 0.15f;
        /// <summary>Times a car filled up at a fuel station (tests, cards).</summary>
        public int Refuels { get; private set; }

        private bool AtFuel(Vehicle v) { int i = v.Bay >= 0 ? ParkingAt(v.CarPark) : -1; return i >= 0 && world.Buildings.Houses[i].variant == Catalog.Fuel; }

        /// <summary>A fuel station within reach of <paramref name="from"/>, by a road, with its pump free (its position), or false.</summary>
        private bool FreePump(Vector2 from, float reach, out Vector2 station)
        {
            station = default;
            var houses = world.Buildings.Houses;
            float best = reach;
            for (int i = 0; i < houses.Count; i++)
            {
                if (houses[i].variant != Catalog.Fuel) continue;
                var at = new Vector2(houses[i].x, houses[i].z);
                float d = (at - from).magnitude;
                if (d > best || d < 0.05f || world.BayOf(at) == null || BayTaken(at, 0)) continue;
                best = d; station = at;
            }
            return best < reach;
        }

        private int ParkingAt(Vector2 at)
        {
            var houses = world.Buildings.Houses;
            for (int i = 0; i < houses.Count; i++) if (Parking(houses[i].variant) && (new Vector2(houses[i].x, houses[i].z) - at).sqrMagnitude < 1e-8f) return i;
            return -1;
        }

        /// <summary>A car park or garage near <paramref name="door"/> with a free space and a road to it (its space, by position), or false.</summary>
        private bool FreeBay(Vector2 door, out Vector2 carPark, out int bay)
        {
            carPark = default; bay = -1;
            var houses = world.Buildings.Houses;
            float best = 0.35f;
            for (int i = 0; i < houses.Count; i++)
            {
                if (!Parking(houses[i].variant) || houses[i].variant == Catalog.Fuel) continue;   // (a fuel station is no place to leave a car)
                var at = new Vector2(houses[i].x, houses[i].z);
                float d = (at - door).magnitude;
                if (d > best || ParkingAim(at) == null) continue;
                for (int k = 0; k < Spaces(houses[i].variant); k++)
                    if (!BayTaken(at, k)) { best = d; carPark = at; bay = k; break; }
            }
            return bay >= 0;
        }

        /// <summary>Where a car heading for a car park drives to: the street beside its driveway (else the nearest kerb).</summary>
        private Vector2? ParkingAim(Vector2 carPark)
        {
            var drive = world.BayOf(carPark);
            if (drive != null && drive.Kind == BayKind.Drive) return drive.At + drive.Out * PathSystem.LaneOffset((int)PathKind.Road, false);
            return Kerb(carPark);
        }

        private bool BayTaken(Vector2 carPark, int bay)
        {
            foreach (var v in Vehicles) if (v.Bay == bay && v.CarPark == carPark) return true;
            return false;
        }

        /// <summary>Where a space is (local to the world) and which way a car parked in it faces.</summary>
        private bool BayPlace(Vector2 carPark, int bay, out Vector2 at, out float yaw)
        {
            at = default; yaw = 0f;
            int house = ParkingAt(carPark);
            if (house < 0) return false;
            var h = world.Buildings.Houses[house];
            float bayYaw = 0f;
            var local = h.variant == Catalog.ParkingGarage ? HouseFactory.GarageInside : h.variant == Catalog.Fuel ? HouseFactory.FuelPump : HouseFactory.ParkingBay(bay, out bayYaw);
            if (h.variant == Catalog.Fuel) bayYaw = 90f;   // (along the pump lane)
            var d = Quaternion.Euler(0f, h.rotation, 0f) * local;
            at = new Vector2(h.x + d.x, h.z + d.z); yaw = h.rotation + bayYaw;
            return true;
        }

        /// <summary>
        /// The way from where the car stopped in the street to its space: across to the driveway, up it to the way in,
        /// along the aisle and round into the bay (a garage: in at its barrier).
        /// </summary>
        private void PlanParking(Vehicle v)
        {
            v.ParkPath.Clear();
            v.ParkPath.Add(v.Position);
            int house = ParkingAt(v.CarPark);
            if (house < 0 || !BayPlace(v.CarPark, v.Bay, out var bay, out _)) return;
            var h = world.Buildings.Houses[house];
            var rot = Quaternion.Euler(0f, h.rotation, 0f);
            Vector2 L(Vector3 local) { var d = rot * local; return new Vector2(h.x + d.x, h.z + d.z); }
            var drive = world.BayOf(v.CarPark);
            if (drive != null && drive.Kind == BayKind.Drive) { v.ParkPath.Add(drive.Mouth + drive.Out * 0.008f); v.ParkPath.Add(drive.End); }
            if (h.variant == Catalog.ParkingGarage) { v.ParkPath.Add(L(HouseFactory.GarageEntrance + new Vector3(0f, 0f, 0.008f))); v.ParkPath.Add(bay); return; }
            if (h.variant == Catalog.Fuel)
            {
                // Into the pump lane at its near end, along it to the pump.
                v.ParkPath.Add(L(new Vector3(-HouseFactory.FuelLaneEnd, 0f, HouseFactory.FuelPump.z)));
                v.ParkPath.Add(bay);
                return;
            }
            var gate = HouseFactory.CarParkEntrance;
            var space = HouseFactory.ParkingBay(v.Bay, out _);
            v.ParkPath.Add(L(new Vector3(gate.x, 0f, gate.z + 0.012f)));
            v.ParkPath.Add(L(new Vector3(gate.x + 0.006f, 0f, -0.002f)));
            v.ParkPath.Add(L(new Vector3(space.x - Mathf.Sign(space.z) * 0f - 0.022f, 0f, 0f)));
            v.ParkPath.Add(L(new Vector3(space.x, 0f, space.z * 0.4f)));
            v.ParkPath.Add(bay);
        }

        /// <summary>
        /// Where a vehicle belonging to a building waits when there is no lay-by or forecourt for it: in the yard beside
        /// the building - to its left or right, its front level with the building's, nose to the street - or else in
        /// front of it, wherever it stands wholly on open ground: off every street and path, clear of buildings, the
        /// railway and water; the <paramref name="slot"/>-th such spot (a place's second lorry stands beside its first).
        /// False when there is no such spot (it waits by the kerb as before).
        /// </summary>
        private bool YardSpot(int house, VehicleKind kind, int slot, out Vector2 at, out float heading)
        {
            at = default; heading = 0f;
            var h = world.Buildings.Houses[house];
            var half = Catalog.Get(h.variant).Half;
            float len = VehicleFactory.Length(kind), wide = VehicleFactory.Width(kind) * 0.5f;
            var rot = Quaternion.Euler(0f, h.rotation, 0f);
            Vector2 L(float x, float z) { var d = rot * new Vector3(x, 0f, z); return new Vector2(h.x + d.x, h.z + d.z); }
            var toStreet = L(0f, -1f) - new Vector2(h.x, h.z);   // (a building's front, its -z, faces the street)
            var across = new Vector2(toStreet.y, -toStreet.x);
            foreach (var (x, z, along) in new[]
            {
                (half.x + wide + 0.005f, -half.y + len * 0.5f, true), (-(half.x + wide + 0.005f), -half.y + len * 0.5f, true),
                (half.x + wide + 0.005f, -half.y + len * 0.5f + 0.02f, true), (-(half.x + wide + 0.005f), -half.y + len * 0.5f + 0.02f, true),
                (half.x * 0.4f, -half.y - wide - 0.004f, false), (-half.x * 0.4f, -half.y - wide - 0.004f, false),
            })
            {
                var c = L(x, z);
                var fwd = along ? toStreet : across;   // (beside it: nose to the street; in front: along the front)
                var side = new Vector2(fwd.y, -fwd.x);
                bool clear = true;
                for (int i = -1; i <= 1 && clear; i++)
                    for (int j = -1; j <= 1 && clear; j++)
                        clear = OpenGround(c + fwd * (len * 0.5f * i) + side * (wide * j));
                if (!clear || slot-- > 0) continue;   // (a second lorry: the next spot)
                at = c; heading = Mathf.Atan2(fwd.x, fwd.y) * Mathf.Rad2Deg;
                return true;
            }
            return false;
        }

        /// <summary>Whether a vehicle may stand at a point: dry land, off every way, clear of buildings and the railway.</summary>
        private bool OpenGround(Vector2 p)
        {
            var t = world.Terrain;
            if (!t.Inside(p.x, p.y) || !t.LandAt(p.x, p.y) || t.WaterAt(p.x, p.y) > 0.1f || world.Streams.IsWet(p, 0.004f)) return false;
            if (world.Paths.Covers(p, 0.003f) || world.Buildings.Contains(p, 0.002f)) return false;
            return world.Track.DistanceTo(new Vector3(p.x, 0f, p.y)) > TrackSystem.BedHalfWidth + 0.004f;
        }

        /// <summary>
        /// Leaving the pumps: on along the lane and out at its far end, back across the forecourt to the driveway and the
        /// street (forwards all the way: the path is walked from the pump back to its start).
        /// </summary>
        private void FuelLeaving(Vehicle v, Vector2 pump)
        {
            int house = ParkingAt(v.CarPark);
            if (house < 0) return;
            var h = world.Buildings.Houses[house];
            var rot = Quaternion.Euler(0f, h.rotation, 0f);
            Vector2 L(Vector3 local) { var d = rot * local; return new Vector2(h.x + d.x, h.z + d.z); }
            v.ParkPath.Clear();
            v.ParkPath.Add(v.Kerbside);
            var drive = world.BayOf(v.CarPark);
            if (drive != null && drive.Kind == BayKind.Drive) { v.ParkPath.Add(drive.Mouth + drive.Out * 0.008f); v.ParkPath.Add(drive.End); }
            v.ParkPath.Add(L(new Vector3(HouseFactory.FuelLaneEnd, 0f, -(Catalog.Get(Catalog.Fuel).Half.y + 0.01f))));
            v.ParkPath.Add(L(new Vector3(HouseFactory.FuelLaneEnd, 0f, HouseFactory.FuelPump.z)));
            v.ParkPath.Add(pump);
        }

        private static float Length(List<Vector2> path) { float s = 0f; for (int i = 1; i < path.Count; i++) s += (path[i] - path[i - 1]).magnitude; return s; }

        /// <summary>The point <paramref name="t"/> (0 to 1) of the way along a path, and the direction there.</summary>
        private static Vector2 Along(List<Vector2> path, float t, out Vector2 dir, out int leg)
        {
            float total = Length(path), want = t * total;
            dir = Vector2.up; leg = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var seg = path[i] - path[i - 1];
                float len = seg.magnitude;
                if (len > 1e-6f) dir = seg / len;
                if (want <= len || i == path.Count - 1) { leg = i; return path[i - 1] + dir * Mathf.Min(want, len); }
                want -= len;
            }
            return path[0];
        }

        /// <summary>For a car park's or garage's card: how full it is.</summary>
        public string ParkingFor(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count || !Parking(world.Buildings.Houses[house].variant)) return null;
            int n = ParkedAt(house);
            return n == 0 ? "empty" : $"{n} of {Spaces(world.Buildings.Houses[house].variant)} spaces taken";
        }

        /// <summary>Cars in a car park or garage now (tests, cards).</summary>
        public int ParkedAt(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count) return 0;
            var at = new Vector2(world.Buildings.Houses[house].x, world.Buildings.Houses[house].z);
            int n = 0;
            foreach (var v in Vehicles) if (v.Bay >= 0 && v.CarPark == at && v.State == Mode.Parked && v.ParkT >= 0.99f) n++;
            return n;
        }

        /// <summary>
        /// A visiting car at a car park or garage: driving in to its space, waiting, or coming back out (true while it is
        /// busy with that). It drives forwards in and reverses out of a bay, then drives forwards to the street. The car
        /// park gone: it stays where it is and leaves from there.
        /// </summary>
        private bool StepParking(Vehicle v, float dt, bool leaving)
        {
            if (v.Bay < 0) return false;
            if (!BayPlace(v.CarPark, v.Bay, out var bay, out float yaw)) { v.Bay = -1; v.Inside = false; return false; }
            bool fuel = AtFuel(v);
            if (leaving && v.ParkDir >= 0)
            {
                v.ParkDir = -1; v.Inside = false;
                if (fuel) FuelLeaving(v, bay);
            }
            if (v.ParkDir == 0) return false;
            if (v.ParkPath.Count < 2) { v.ParkPath.Clear(); v.ParkPath.Add(v.Kerbside); v.ParkPath.Add(bay); }
            float length = Mathf.Max(0.01f, Length(v.ParkPath));
            v.ParkT = Mathf.Clamp01(v.ParkT + v.ParkDir * dt * ParkSpeed / length);
            v.Position = Along(v.ParkPath, Mathf.SmoothStep(0f, 1f, v.ParkT), out var dir, out int leg);
            // Facing the way it goes - except backing out of a bay (the last leg, going out).
            bool reversing = v.ParkDir < 0 && leg == v.ParkPath.Count - 1 && !fuel;   // (from the pumps it drives on, out the far end)
            var face = v.ParkDir > 0 || reversing ? dir : -dir;
            float want = v.ParkT >= 0.999f ? yaw : Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg;
            v.Heading = Mathf.MoveTowardsAngle(v.Heading, want, 360f * dt);
            Pose(v, dt);
            bool garage = world.Buildings.Houses[ParkingAt(v.CarPark)].variant == Catalog.ParkingGarage;
            if (v.ParkDir > 0 && v.ParkT >= 1f) { v.ParkDir = 0; v.Inside = garage; if (fuel) Refuels++; return true; }
            if (v.ParkDir < 0 && v.ParkT <= 0f) { v.ParkDir = 0; v.Bay = -1; v.ParkPath.Clear(); return false; }   // out on the street: off home
            return true;
        }
    }
}
