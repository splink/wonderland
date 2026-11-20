using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The places vehicles stop are part of the street they stand by: a bus stop and a taxi rank get a lay-by (and step
    /// back from the kerb to make room for it, facing the street), a car park, a parking garage, a fuel station and the
    /// police, fire and ambulance stations a driveway from the street to their way in. Worked out again whenever the
    /// buildings or the streets change.
    /// </summary>
    public sealed partial class WonderlandWorld
    {
        public const float BusBayLength = 0.105f, BusBayDepth = 0.025f, TaxiBayLength = 0.11f, TaxiBayDepth = 0.023f, DrivewayWidth = 0.036f;

        private int baysFor = int.MinValue;

        private static BayKind? BayFor(int variant) => variant switch
        {
            Catalog.BusStop => BayKind.Bus,
            Catalog.TaxiRank => BayKind.Taxi,
            Catalog.CarPark or Catalog.ParkingGarage or Catalog.Fuel or Catalog.Police or Catalog.FireStation or Catalog.Hospital => BayKind.Drive,
            _ => null,
        };

        /// <summary>Where a building's driveway reaches it: a car park's or garage's way in, a fuel station's forecourt, a station's doors.</summary>
        public static Vector2 WayIn(HouseInstance h)
        {
            if (h.variant != Catalog.CarPark && h.variant != Catalog.ParkingGarage && h.variant != Catalog.Fuel) return PeopleSystem.DoorStep(h);
            var local = h.variant == Catalog.CarPark ? HouseFactory.CarParkEntrance : h.variant == Catalog.Fuel ? HouseFactory.FuelEntrance : HouseFactory.GarageEntrance;
            var d = Quaternion.Euler(0f, h.rotation, 0f) * local;
            return new Vector2(h.x + d.x, h.z + d.z);
        }

        /// <summary>The lay-by or driveway serving a building (by where it stands), or null.</summary>
        public RoadBay BayOf(Vector2 building)
        {
            foreach (var b in Paths.Bays) if ((b.Of - building).sqrMagnitude < 1e-8f) return b;
            return null;
        }

        /// <summary>Works the lay-bys and driveways out again if the buildings or streets changed; true if they did.</summary>
        public bool UpdateBays()
        {
            int key = Buildings.Version * 7919;
            foreach (var l in Paths.Lines) key = key * 31 + l.points.Count * 7 + l.kind + (l.points.Count > 0 ? Mathf.RoundToInt(l.points[0].x * 1e4f) : 0);
            if (key == baysFor) return false;
            baysFor = key;
            var bays = new List<RoadBay>();
            float edge = PathSystem.HalfWidth((int)PathKind.Road) - PathSystem.KerbWidth;
            foreach (var h in Buildings.Houses)
            {
                var kind = BayFor(h.variant);
                if (kind == null) continue;
                var way = WayIn(h);
                if (!Paths.NearestStreet(way, 0.25f, out var line, out var at, out var along)) continue;
                var side = Vector2.Dot(way - at, new Vector2(along.y, -along.x)) >= 0f ? 1 : -1;
                var bay = new RoadBay { Kind = kind.Value, Line = line, At = at, Along = along, Side = side, Edge = edge, Of = new Vector2(h.x, h.z) };
                if (kind == BayKind.Drive)
                {
                    bay.Length = DrivewayWidth;
                    bay.Depth = Vector2.Dot(way - at, bay.Out) - edge;
                    if (bay.Depth < 0.004f || bay.Depth > 0.22f || Paths.NearJunction(at, DrivewayWidth * 0.5f)) continue;   // (not opening into a junction)
                }
                else
                {
                    bay.Length = kind == BayKind.Bus ? BusBayLength : TaxiBayLength;
                    bay.Depth = kind == BayKind.Bus ? BusBayDepth : TaxiBayDepth;
                    if (Vector2.Dot(way - at, bay.Out) > 0.12f || Paths.NearJunction(at, bay.Length * 0.5f + bay.Taper)) continue;
                }
                bays.Add(bay);
            }
            return Paths.SetBays(bays);
        }

        /// <summary>The forecourt a station needs in front of its doors: room for its vehicle, nose to the street.</summary>
        private static float Forecourt(int variant) => variant switch
        {
            Catalog.FireStation => VehicleFactory.Length(VehicleKind.FireEngine) + 0.012f,
            Catalog.Hospital => VehicleFactory.Length(VehicleKind.Ambulance) + 0.012f,
            Catalog.Police => VehicleFactory.Length(VehicleKind.Police) + 0.012f,
            Catalog.CarPark or Catalog.ParkingGarage or Catalog.Fuel => 0.02f,   // (a short driveway to its way in)
            _ => 0f,
        };

        /// <summary>
        /// A bus stop or taxi rank put down beside a street stands back from it, facing it, with room for its lay-by
        /// between it and the traffic; a police, fire or ambulance station with room for its forecourt (where along the
        /// street it goes is left as it was put).
        /// </summary>
        private HouseInstance StandBackFromStreet(HouseInstance h)
        {
            var kind = BayFor(h.variant);
            float forecourt = Forecourt(h.variant);
            if (kind is not (BayKind.Bus or BayKind.Taxi) && forecourt <= 0f) return h;
            var p = new Vector2(h.x, h.z);
            var half = Catalog.Get(h.variant).Half;
            if (!Paths.NearestStreet(p, forecourt > 0f ? half.y + 0.11f : 0.1f, out _, out var at, out var along)) return h;
            if (Paths.NearJunction(at, forecourt > 0f ? DrivewayWidth * 0.5f : (kind == BayKind.Bus ? BusBayLength : TaxiBayLength) * 0.5f + 0.04f)) return h;
            var right = new Vector2(along.y, -along.x);
            var away = Vector2.Dot(p - at, right) >= 0f ? right : -right;
            float edge = PathSystem.HalfWidth((int)PathKind.Road) - PathSystem.KerbWidth, depth = kind == BayKind.Bus ? BusBayDepth : TaxiBayDepth;
            bool lot = h.variant is Catalog.CarPark or Catalog.ParkingGarage or Catalog.Fuel;
            var c = lot ? at + away * Mathf.Max(Vector2.Dot(p - at, away), edge + forecourt + half.y)   // (its front edge, where its way in is, at least that far back; further if it was put further)
                : forecourt > 0f
                ? at + away * (edge + forecourt + half.y * 0.86f + PeopleSystem.Radius + 0.008f)   // (its doors - where the driveway ends - that far back)
                : at + away * (edge + depth + PathSystem.KerbWidth + 0.003f + half.y);
            h.x = c.x; h.z = c.y;
            h.rotation = Mathf.Atan2(away.x, away.y) * Mathf.Rad2Deg;   // (its front, local -z, to the street)
            return h;
        }
    }
}
