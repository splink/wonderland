using System.Collections.Generic;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// Buildings joined to the streets: a building put down beside a curving street faces it, lined up behind the kerb;
        /// a fire station's engine and a factory's lorry wait off the street (on the forecourt, or in the yard beside
        /// the building), never on the carriageway; cars call at a fuel station, stand at the pump and drive on; a
        /// railway drawn across water with a bridge style chosen gets that style.
        /// </summary>
        private static void AccessJourney()
        {
            var w = Scratch("Access", 350f);
            var t = w.Terrain;
            // A street curving round.
            string road = w.BuildPath(Stroke(t, new Vector2(-0.7f, -0.3f), new Vector2(-0.2f, -0.35f), new Vector2(0.2f, -0.15f), new Vector2(0.6f, 0.25f)), PathKind.Road);
            var street = w.Paths.Lines.Count > 0 ? w.Paths.Lines[0] : null;
            // Beside the bend: facing the street, its front behind the kerb.
            float facing = 0f, gap = 0f;
            if (street != null && w.Paths.AlongRoad(new Vector2(0.05f, -0.15f), 0.04f, out var at, out float yaw, false))
            {
                var front = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
                StreetPoint(street, at, out var near);
                facing = Vector2.Dot(new Vector2(front.x, front.z), (near - at).normalized);
                gap = PathSystem.Distance(street, at) - 0.04f;
            }
            Check(road == null && facing > 0.97f && gap > PathSystem.HalfWidth((int)PathKind.Road) && gap < PathSystem.HalfWidth((int)PathKind.Road) + 0.03f,
                $"A building beside a curving street faces it (front to the street: {facing:0.00}) and stands just behind the kerb ({gap * 100f:0.0} cm from its middle)");

            Object.DestroyImmediate(w.gameObject);

            // A straight street with a fire station and a factory beside it (put down as the Build tool does: facing it,
            // behind the kerb): their engine and lorry wait off the carriageway.
            var w1 = Scratch("Parking", 360f);
            var t1 = w1.Terrain;
            w1.BuildPath(Stroke(t1, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f)), PathKind.Road);
            var line = w1.Paths.Lines[0];
            string Beside(int id, float x, float z)
            {
                if (!w1.Paths.AlongRoad(new Vector2(x, z), Catalog.Get(id).Half.y, out var p, out float r, false)) return "no street near";
                return w1.PlaceHouse(new HouseInstance { x = p.x, z = p.y, rotation = r, variant = id });
            }
            string placed = (Beside(Catalog.FireStation, -0.35f, 0.1f) ?? "") + (Beside(Catalog.Factory, 0.3f, 0.1f) ?? "") + (Beside(Catalog.Fuel, -0.05f, -0.1f) ?? "");
            foreach (float x in new[] { -0.55f, -0.3f, 0.2f, 0.45f }) Beside(Catalog.Cottage, x, -0.1f);
            w1.People.Grid.Complete(); w1.People.Step(Dt); w1.Traffic.Refresh(true);
            for (float s = 0f; s < 2f; s += Dt) w1.Traffic.Step(Dt);
            float worst = float.MaxValue; string which = "";
            foreach (var v in w1.Traffic.Vehicles)
            {
                if (v.Kind is not (VehicleKind.FireEngine or VehicleKind.Flatbed) || v.State != TrafficSystem.Mode.Parked) continue;
                float clear = PathSystem.Distance(line, v.Position) - (PathSystem.HalfWidth((int)PathKind.Road) - PathSystem.KerbWidth) - VehicleFactory.Width(v.Kind) * 0.5f;
                if (clear < worst) { worst = clear; which = v.Kind.ToString(); }
            }
            int based = w1.Traffic.Vehicles.FindAll(v => v.Kind is VehicleKind.FireEngine or VehicleKind.Flatbed).Count;
            Check(based >= 2 && worst > 0f, $"The fire engine and the factory's lorry wait off the street, clear of the carriageway ({based} vehicles; the nearest, the {which.ToLower()}, {worst * 100f:0.0} cm clear{(placed.Length > 0 ? "; " + placed : "")})");

            // ...and the cottages' cars call at the fuel station, stand at the pump and drive on.
            float chance = TrafficSystem.RefuelChance;
            TrafficSystem.RefuelChance = 0.6f;
            w1.Clock.Hour = 10f;
            int fuel = w1.Buildings.Houses.FindIndex(h => h.variant == Catalog.Fuel);
            bool atPump = false, drive = fuel >= 0 && w1.BayOf(new Vector2(w1.Buildings.Houses[fuel].x, w1.Buildings.Houses[fuel].z)) != null;
            int cars = w1.Traffic.Vehicles.FindAll(v => v.Kind == VehicleKind.Car).Count;
            for (float s = 0f; s < 300f && !(atPump && w1.Traffic.Refuels >= 2); s += Dt)
            {
                w1.Clock.Tick(Dt); w1.Traffic.Step(Dt);
                if (w1.Clock.Hour > 18f) w1.Clock.Hour = 10f;
                if (fuel >= 0 && w1.Traffic.ParkedAt(fuel) > 0) atPump = true;
            }
            TrafficSystem.RefuelChance = chance;
            Check(drive && atPump && w1.Traffic.Refuels >= 2, $"Cars call at the fuel station by its driveway, stand at the pump and drive on ({w1.Traffic.Refuels} filled up by {cars} cars; driveway {drive})");
            Object.DestroyImmediate(w1.gameObject);

            // A railway across a pond, with Lift chosen for new bridges: a lift bridge.
            var w2 = Scratch("Bridge choice", 370f);
            var t2 = w2.Terrain;
            var pond = new Vector3(0f, 0f, 0f);
            float lvl = w2.WaterLevelForStroke(pond);
            for (float s = 0f; s < 2f; s += Dt) w2.WaterStroke(pond, 0.16f, 1f, Dt, lvl, false);
            var before = w2.Bridges.Bridges.ConvertAll(b => b.Anchor);
            var plan = w2.BuildTrack(Stroke(t2, new Vector2(-0.6f, 0.02f), new Vector2(0.6f, -0.02f)));
            string said = plan.Ok ? w2.StyleNewBridges(before, BridgeStyle.Lift) : plan.Error;
            Check(plan.Ok && w2.Bridges.Bridges.Count == 1 && w2.Bridges.Bridges[0].Style == BridgeStyle.Lift && w2.Bridges.Lifts.Count == 1,
                $"With Lift chosen for new bridges, a railway drawn across water gets a lift bridge (\"{said}\")");
            Object.DestroyImmediate(w2.gameObject);
        }

        /// <summary>The point of a way's line nearest <paramref name="p"/>.</summary>
        private static void StreetPoint(PathLine l, Vector2 p, out Vector2 nearest)
        {
            nearest = l.points[0]; float best = float.MaxValue;
            foreach (var q in l.points) { float d = (q - p).sqrMagnitude; if (d < best) { best = d; nearest = q; } }
        }
    }
}
