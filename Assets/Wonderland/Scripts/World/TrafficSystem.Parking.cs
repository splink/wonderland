using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Car parks: a household's car going to a place with a car park near it (by a road) drives to the car park instead
    /// of stopping at the kerb, turns into a free bay and stays while its driver is out, then backs out and drives home.
    /// So a car park fills up when the shops near it are busy, and stands empty at night. A full car park, or one taken
    /// away, means the kerb as before.
    /// </summary>
    public sealed partial class TrafficSystem
    {
        public const float ParkSeconds = 1.6f;

        /// <summary>A car park near <paramref name="door"/> with a free bay and a road to it (its bay, by position), or false.</summary>
        private bool FreeBay(Vector2 door, out Vector2 carPark, out int bay)
        {
            carPark = default; bay = -1;
            var houses = world.Buildings.Houses;
            float best = 0.35f;
            for (int i = 0; i < houses.Count; i++)
            {
                if (houses[i].variant != Catalog.CarPark) continue;
                var at = new Vector2(houses[i].x, houses[i].z);
                float d = (at - door).magnitude;
                if (d > best || Kerb(at) == null) continue;
                for (int k = 0; k < HouseFactory.ParkingBays; k++)
                    if (!BayTaken(at, k)) { best = d; carPark = at; bay = k; break; }
            }
            return bay >= 0;
        }

        private bool BayTaken(Vector2 carPark, int bay)
        {
            foreach (var v in Vehicles) if (v.Bay == bay && v.CarPark == carPark) return true;
            return false;
        }

        /// <summary>Where a bay is (local to the world) and which way a car parked in it faces.</summary>
        private bool BayPlace(Vector2 carPark, int bay, out Vector2 at, out float yaw)
        {
            at = default; yaw = 0f;
            int house = HouseAt(carPark, Catalog.CarPark);
            if (house < 0) return false;
            var h = world.Buildings.Houses[house];
            var local = HouseFactory.ParkingBay(bay, out float bayYaw);
            var d = Quaternion.Euler(0f, h.rotation, 0f) * local;
            at = new Vector2(h.x + d.x, h.z + d.z); yaw = h.rotation + bayYaw;
            return true;
        }

        /// <summary>For a car park's card: how full it is.</summary>
        public string ParkingFor(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count || world.Buildings.Houses[house].variant != Catalog.CarPark) return null;
            int n = ParkedAt(house);
            return n == 0 ? "empty" : $"{n} of {HouseFactory.ParkingBays} bays taken";
        }

        /// <summary>Cars in a car park now (tests, cards).</summary>
        public int ParkedAt(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count) return 0;
            var at = new Vector2(world.Buildings.Houses[house].x, world.Buildings.Houses[house].z);
            int n = 0;
            foreach (var v in Vehicles) if (v.Bay >= 0 && v.CarPark == at && v.State == Mode.Parked && v.ParkT >= 0.99f) n++;
            return n;
        }

        /// <summary>
        /// A visiting car in a car park: turning into its bay, waiting, or backing out (true while it is busy with that).
        /// The car park gone: it stays where it is and leaves from there.
        /// </summary>
        private bool StepParking(Vehicle v, float dt, bool leaving)
        {
            if (v.Bay < 0) return false;
            if (!BayPlace(v.CarPark, v.Bay, out var bay, out float yaw)) { v.Bay = -1; return false; }
            if (leaving && v.ParkDir >= 0) v.ParkDir = -1;
            if (v.ParkDir == 0) return false;
            v.ParkT = Mathf.Clamp01(v.ParkT + v.ParkDir * dt / ParkSeconds);
            float s = Mathf.SmoothStep(0f, 1f, v.ParkT);
            v.Position = Vector2.Lerp(v.Kerbside, bay, s);
            v.Heading = Mathf.LerpAngle(v.KerbHeading, yaw, s);
            Pose(v, dt);
            if (v.ParkDir > 0 && v.ParkT >= 1f) { v.ParkDir = 0; return true; }
            if (v.ParkDir < 0 && v.ParkT <= 0f) { v.ParkDir = 0; v.Bay = -1; return false; }   // out on the road: off home
            return true;
        }
    }
}
