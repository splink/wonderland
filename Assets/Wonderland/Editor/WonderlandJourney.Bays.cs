using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>A street with a bus stop, a taxi rank, a car park, a parking garage and a police station on it, a bus stop across the way.</summary>
        private static WonderlandWorld BayStreet(float x, out string report)
        {
            var w = Scratch("Bays", x);
            var t = w.Terrain;
            var log = new System.Text.StringBuilder();
            log.AppendLine($"street: {w.BuildPath(Line(t, new Vector2(-0.78f, 0f), new Vector2(0.78f, 0f)), PathKind.Road) ?? "ok"}");
            foreach (var (id, px, pz, rot) in new[]
            {
                (Catalog.BusStop, -0.62f, 0.05f, 0f), (Catalog.TaxiRank, -0.36f, 0.05f, 0f), (Catalog.CarPark, -0.04f, 0.115f, 0f),
                (Catalog.ParkingGarage, 0.3f, 0.12f, 0f), (Catalog.Police, 0.62f, 0.11f, 0f), (Catalog.BusStop, 0.12f, -0.05f, 180f),
            })
                log.AppendLine($"{Catalog.Get(id).Name}: {w.PlaceHouse(new HouseInstance { x = px, z = pz, rotation = rot, variant = id }) ?? "ok"}");
            log.AppendLine($"{w.Paths.Bays.Count} bays: {string.Join(", ", w.Paths.Bays.ConvertAll(b => $"{b.Kind} {b.Depth * 1000f:0} mm"))}");
            report = log.ToString();
            return w;
        }

        private static object CallTraffic(WonderlandWorld w, string method, params object[] args) =>
            typeof(TrafficSystem).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(w.Traffic, args);

        /// <summary>
        /// Lay-bys and driveways (roads suite): stops and stations stand back from the street with theirs; taxis wait and
        /// help stands off the street; a bus in its lay-by and help at a scene don't hold the traffic up (it swings round
        /// what stands at the kerb); a visiting car drives in by the driveway to its bay, and into a garage, on asphalt
        /// all the way.
        /// </summary>
        private static void BayJourney()
        {
            {
                // A car park put down carelessly beside a street - turned sideways, overlapping the pavement - turns to
                // face it and stands back with a driveway to its way in.
                var c = Scratch("Careless car park", 720f);
                c.BuildPath(Line(c.Terrain, new Vector2(-0.6f, 0f), new Vector2(0.6f, 0f)), PathKind.Road);
                string e = c.PlaceHouse(new HouseInstance { x = 0.05f, z = 0.07f, rotation = 90f, variant = Catalog.CarPark });
                var lot = c.Buildings.Houses.Find(h => h.variant == Catalog.CarPark);
                var way = e == null ? c.BayOf(new Vector2(lot.x, lot.z)) : null;
                Check(e == null && way != null && Mathf.Abs(Mathf.DeltaAngle(lot.rotation, 0f)) < 1f, $"A car park put down sideways against a street turns to face it and gets its driveway ({e ?? "placed"}, turned {lot.rotation:0}, driveway {(way != null ? $"{way.Depth * 1000f:0} mm" : "none")})");
                Object.DestroyImmediate(c.gameObject);
            }
            var w = BayStreet(700f, out string report);
            Report.Append("INFO  ").AppendLine(report.Replace("\n", "; "));
            var kinds = w.Paths.Bays.ConvertAll(b => b.Kind);
            int bus = kinds.FindAll(k => k == BayKind.Bus).Count, taxi = kinds.FindAll(k => k == BayKind.Taxi).Count, drive = kinds.FindAll(k => k == BayKind.Drive).Count;
            float edge = PathSystem.HalfWidth((int)PathKind.Road) - PathSystem.KerbWidth, back = float.MaxValue;
            foreach (var h in w.Buildings.Houses)
                if (h.variant is Catalog.BusStop or Catalog.TaxiRank) back = Mathf.Min(back, Mathf.Abs(h.z) - Catalog.Get(h.variant).Half.y);
            Check(bus == 2 && taxi == 1 && drive == 3 && back > edge + WonderlandWorld.TaxiBayDepth,
                $"Bus stops and a taxi rank get lay-bys and stand back behind them ({bus} bus, {taxi} taxi; nearest front {back * 1000f:0} mm from the street's middle, the asphalt's edge {edge * 1000f:0}); a car park, a garage and a police station get driveways ({drive})");
            // Taxis in the lay-by, the patrol car on the forecourt, nose to the street.
            w.Traffic.Refresh(true);
            var taxis = w.Traffic.Vehicles.FindAll(v => v.Kind == VehicleKind.Taxi && v.Rank >= 0);
            bool taxisIn = taxis.Count == 2 && taxis.TrueForAll(v => w.Paths.BayAt(v.Position)?.Kind == BayKind.Taxi) && (taxis[0].Position - taxis[1].Position).magnitude > VehicleFactory.Length(VehicleKind.Taxi);
            var patrol = w.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Police);
            var court = patrol != null ? w.Paths.BayAt(patrol.Position) : null;
            bool noseOut = court != null && Vector2.Dot(new Vector2(Mathf.Sin(patrol.Heading * Mathf.Deg2Rad), Mathf.Cos(patrol.Heading * Mathf.Deg2Rad)), court.Out) < -0.9f;
            Check(taxisIn && court?.Kind == BayKind.Drive && noseOut, $"The taxis wait in their lay-by one behind the other ({taxis.Count}: {string.Join(", ", taxis.ConvertAll(v => $"({v.Position.x:0.000}, {v.Position.y:0.000})"))}) and the patrol car on the station's forecourt, nose to the street ({(patrol != null ? $"({patrol.Position.x:0.000}, {patrol.Position.y:0.000})" : "none")})");
            // A bus at the south stop (eastbound traffic's side), and a car right behind it.
            var stopAt = w.Buildings.Houses.Find(h => h.variant == Catalog.BusStop && h.z < 0f);
            w.PlaceVehicle(new Vector3(stopAt.x, 0f, -PathSystem.LaneOffset((int)PathKind.Road, false)), VehicleKind.Bus, 90f, 1);
            w.PlaceVehicle(new Vector3(stopAt.x - 0.08f, 0f, -PathSystem.LaneOffset((int)PathKind.Road, false)), VehicleKind.Car, 90f, 2);
            var theBus = w.Traffic.Vehicles.FindLast(v => v.Kind == VehicleKind.Bus);
            var car = w.Traffic.Vehicles.FindLast(v => v.Kind == VehicleKind.Car);
            float Held()
            {
                car.State = TrafficSystem.Mode.Driving; car.Heading = 90f; car.Pushing = 0f;
                car.Position = theBus.Position - new Vector2(0.075f, 0f);
                return (float)CallTraffic(w, "Obstacle", car);
            }
            theBus.Heading = 90f; theBus.State = TrafficSystem.Mode.Driving; theBus.Dwell = 0f;
            float inLane = Held();
            theBus.Dwell = 2f; theBus.PullIn = theBus.Shift = w.BayOf(new Vector2(stopAt.x, stopAt.z)) is { } sb ? sb.Edge + sb.Depth * 0.5f - PathSystem.LaneOffset((int)PathKind.Road, false) : 0f;
            CallTraffic(w, "Pose", theBus, 0f);
            float inBay = Held();
            var shown = new Vector2(theBus.Root.localPosition.x, theBus.Root.localPosition.z);
            Check(inBay > 0.03f && inLane < 0.01f && w.Paths.BayAt(shown)?.Kind == BayKind.Bus,
                $"A bus standing in its lay-by doesn't hold up the car behind (it may go {inBay * 1000f:0} mm/s; behind the same bus stopped in the lane, {inLane * 1000f:0}), and it stands in the lay-by");
            // Help at a scene pulls in to the kerb: a car coming up behind swings round it.
            theBus.Dwell = 0f; theBus.PullIn = theBus.Shift = 0f; theBus.State = TrafficSystem.Mode.AtScene; theBus.Shift = 0.012f; theBus.Roams = false;   // (help isn't one of the player's own vehicles)
            car.Position = theBus.Position - new Vector2(0.075f, 0f); car.Heading = 90f; car.State = TrafficSystem.Mode.Driving;
            float swing = (float)CallTraffic(w, "Swerve", car, new Vector2(1f, 0f));
            float scene = (float)CallTraffic(w, "Obstacle", car);
            Check(swing > 0.005f && scene > 0.03f, $"Something standing at the kerb (help at a scene) isn't queued behind: the traffic swings out round it ({swing * 1000f:0.0} mm out, going on at {scene * 1000f:0} mm/s)");
            // Visiting cars into the car park and the garage: on asphalt all the way, ending in their space.
            foreach (int variant in new[] { Catalog.CarPark, Catalog.ParkingGarage })
            {
                var lot = w.Buildings.Houses.Find(h => h.variant == variant);
                var lotAt = new Vector2(lot.x, lot.z);
                var visitor = w.Traffic.Vehicles.FindLast(v => v.Kind == VehicleKind.Car);
                visitor.CarPark = lotAt; visitor.Bay = 3; visitor.State = TrafficSystem.Mode.Parked;
                var aim = (Vector2?)CallTraffic(w, "ParkingAim", lotAt) ?? Vector2.zero;
                visitor.Position = aim; visitor.Heading = 270f; visitor.ParkT = 0f; visitor.ParkDir = 1; visitor.Kerbside = aim;
                CallTraffic(w, "PlanParking", visitor);
                int off = 0, steps = 0; float far = 0f;
                var footprint = Catalog.Get(variant).Half;
                for (; steps < 2000 && visitor.ParkDir != 0; steps++)
                {
                    CallTraffic(w, "StepParking", visitor, 0.02f, false);
                    var local = Quaternion.Euler(0f, -lot.rotation, 0f) * new Vector3(visitor.Position.x - lot.x, 0f, visitor.Position.y - lot.z);
                    bool onLot = Mathf.Abs(local.x) < footprint.x && Mathf.Abs(local.z) < footprint.y + 0.003f;
                    bool onStreet = Mathf.Abs(visitor.Position.y) < edge;
                    if (!onLot && !onStreet && w.Paths.BayAt(visitor.Position) == null) { off++; far = Mathf.Max(far, w.Paths.BayAt(visitor.Position) == null ? 1f : 0f); }
                }
                bool parked = visitor.ParkT >= 1f && (variant != Catalog.ParkingGarage || visitor.Inside);
                Check(parked && off == 0, $"A visiting car turns in off the street by the {Catalog.Get(variant).Name.ToLower()}'s driveway and drives to its space on asphalt all the way ({steps} steps, {off} off the asphalt{(variant == Catalog.ParkingGarage ? $", inside: {visitor.Inside}" : "")}; path {visitor.ParkPath.Count} points)");
                visitor.Bay = -1; visitor.Inside = false; visitor.ParkPath.Clear();
            }
            Object.DestroyImmediate(w.gameObject);
        }

        public static void BayRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), Vector3.zero, 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = BayStreet(0f, out string report);
            w.Sky.Shown = false;
            w.Clock.Hour = 11f;
            // A few minutes of traffic, so buses call and cars park.
            for (int k = 0; k < 600; k++) { w.Traffic.Step(0.05f); w.People.Step(0.05f); }
            void Shot(string file, Vector3 from, Vector3 at, float fov) { cam.transform.position = from; cam.transform.LookAt(at); cam.fieldOfView = fov; WonderlandRenders.Capture(cam, file, 1600, 1000); }
            Shot("editor-bays-street.png", new Vector3(0f, 0.75f, -0.9f), new Vector3(0f, 0f, 0.05f), 50f);
            Shot("editor-bays-bus.png", new Vector3(-0.55f, 0.16f, -0.24f), new Vector3(-0.6f, 0f, 0.03f), 40f);
            Shot("editor-bays-taxi.png", new Vector3(-0.3f, 0.16f, -0.24f), new Vector3(-0.36f, 0f, 0.03f), 40f);
            Shot("editor-bays-parking.png", new Vector3(0.1f, 0.3f, -0.3f), new Vector3(0.1f, 0f, 0.08f), 45f);
            Shot("editor-bays-police.png", new Vector3(0.6f, 0.2f, -0.25f), new Vector3(0.62f, 0f, 0.06f), 40f);
            System.IO.File.WriteAllText("Logs/bayrender-results.txt", report);
            Object.DestroyImmediate(w.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
