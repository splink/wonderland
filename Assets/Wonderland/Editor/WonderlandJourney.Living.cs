using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wonderland.Editor
{
    /// <summary>
    /// The living world: activities that emerge from what is built. Each scene is a small world built for the purpose,
    /// run through village time, with the activity watched from start to end - and the same activity in a world where
    /// it can't happen (no road, nothing to carry, the place removed half way), which must stay calm and never get stuck.
    /// </summary>
    public static partial class WonderlandJourney
    {
        public static void TestLiving()
        {
            Report.Clear(); failures = 0; BuildGrid.Snap = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            try { Deliveries(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Taxis(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Tractors(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Town(); }
            catch (Exception e) { Fail("Exception: " + e); }
            BuildGrid.Snap = true;
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            File.WriteAllText("Logs/living-results.txt", Report.ToString());
            Debug.Log("Wonderland living test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>Runs a world's village for <paramref name="seconds"/>, calling <paramref name="watch"/> every step.</summary>
        private static void Live(WonderlandWorld w, float seconds, Action<float> watch = null)
        {
            for (float s = 0f; s < seconds; s += Dt)
            {
                w.Clock.Tick(Dt); w.People.Step(Dt); w.Traffic.Step(Dt);
                watch?.Invoke(s);
            }
        }

        /// <summary>
        /// Lorries: the barn's lorry collects crates from the barn and takes them to the bakery along the road - loading
        /// them onto its bed one by one, carrying them, unloading them into the bakery's stock - then goes home. With no
        /// road to the bakery, nothing happens; when the bakery is taken down while the lorry is on its way, the lorry
        /// takes the load to the grocer instead (or home), and nothing is left waiting.
        /// </summary>
        private static void Deliveries()
        {
            var w = Scratch("Deliveries", 0f);
            var t = w.Terrain;
            string road = w.BuildPath(Line(t, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f)), PathKind.Road);
            string barn = w.PlaceHouse(new HouseInstance { x = -0.45f, z = 0.11f, rotation = 0f, variant = Catalog.Barn });
            string bakery = w.PlaceHouse(new HouseInstance { x = 0.45f, z = 0.11f, rotation = 0f, variant = Catalog.Bakery });
            int bi = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Barn), ki = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
            w.People.Grid.Complete(); w.People.Step(Dt); w.Traffic.Refresh(true);
            w.Clock.Hour = 9f;
            w.People.SetStock(bi, 6f); w.People.SetStock(ki, 0f);
            var lorry = w.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Flatbed);
            Check(road == null && barn == null && bakery == null && lorry != null && lorry.Cargo == Cargo.Produce,
                $"A barn by a road keeps an open lorry for its produce ({(lorry != null ? lorry.Owner : "none")}{road ?? ""}{barn ?? ""}{bakery ?? ""})");
            if (lorry == null) return;
            var said = new List<string>();
            int mostShown = 0; bool hopSeen = false, onRoad = true;
            float stockBefore = w.People.StockAt(ki);
            Live(w, 300f, s =>
            {
                string line = w.Traffic.Describe(lorry);
                if (said.Count == 0 || said[^1] != line) said.Add(line);
                if (lorry.Load != null && lorry.Load.gameObject.activeSelf && lorry.Job != null) mostShown = Mathf.Max(mostShown, lorry.Job.Shown);
                var carried = w.Traffic.transform.Find("Crate carried");
                hopSeen |= carried != null && carried.gameObject.activeSelf;
                if (lorry.State == TrafficSystem.Mode.Driving) onRoad &= w.Traffic.OffRoad(lorry.Position) < 0.03f;
            });
            string story = string.Join(" / ", said);
            bool loaded = said.Exists(x => x.Contains("loading")), carriedTo = said.Exists(x => x.Contains("to the bakery")), unloaded = said.Exists(x => x.Contains("unloading"));
            Check(w.Traffic.Delivered >= 1 && loaded && carriedTo && unloaded && hopSeen && mostShown >= 2 && onRoad,
                $"The lorry loads crates at the barn one by one, takes them along the road to the bakery and unloads them ({w.Traffic.Delivered} deliveries, up to {mostShown} crates on the bed): {story}");
            Check(w.People.StockAt(ki) > stockBefore + 1.5f || w.Traffic.Delivered >= 1 && w.People.StockAt(ki) >= 1f,
                $"The bakery's shelves fill from the delivery ({stockBefore:0.0} -> {w.People.StockAt(ki):0.0} crates)");
            Check(lorry.Job == null && (lorry.State == TrafficSystem.Mode.Parked || said[^1].Contains("heading back")),
                $"...and the lorry goes home afterwards ({said[^1]})");

            // No road to the place that wants it: no delivery is planned, nothing waits.
            var w2 = Scratch("No road", 40f);
            var t2 = w2.Terrain;
            w2.BuildPath(Line(t2, new Vector2(-0.7f, 0f), new Vector2(-0.1f, 0f)), PathKind.Road);
            w2.PlaceHouse(new HouseInstance { x = -0.45f, z = 0.11f, variant = Catalog.Barn });
            w2.PlaceHouse(new HouseInstance { x = 0.5f, z = 0.4f, variant = Catalog.Bakery });
            int b2 = w2.Buildings.Houses.FindIndex(h => h.variant == Catalog.Barn), k2 = w2.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
            w2.People.Grid.Complete(); w2.People.Step(Dt); w2.Traffic.Refresh(true);
            w2.Clock.Hour = 9f; w2.People.SetStock(b2, 6f); w2.People.SetStock(k2, 0f);
            var lorry2 = w2.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Flatbed);
            bool everJob = false;
            Live(w2, 90f, s => everJob |= lorry2 != null && lorry2.Job != null);
            Check(lorry2 != null && !everJob && w2.Traffic.Delivered == 0, $"Without a road to the bakery the lorry stays at the barn ({(lorry2 != null ? w2.Traffic.Describe(lorry2) : "no lorry")})");

            // The bakery taken down while the lorry is bringing the crates: they go to the grocer instead.
            w.People.SetStock(bi, 6f); w.People.SetStock(ki, 0f);
            w.PlaceHouse(new HouseInstance { x = 0.15f, z = -0.11f, rotation = 180f, variant = Catalog.Grocer });
            int gi = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Grocer);
            w.People.Grid.Complete(); w.People.Step(Dt); w.Traffic.Refresh(true);
            w.People.SetStock(gi, 1f);
            int before = w.Traffic.Delivered;
            bool removed = false; string redirected = null; float removedAt = 0f;
            Live(w, 300f, s =>
            {
                if (!removed && lorry.Job != null && lorry.Job.Stage == 2)
                {
                    int k = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
                    if (k >= 0) { w.RemoveHouse(k); removed = true; removedAt = s; }
                }
                if (removed && redirected == null && s > removedAt + 2f) redirected = w.Traffic.Describe(lorry);   // (lorries look again once a second)
            });
            gi = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Grocer);
            Check(removed && redirected != null && redirected.Contains("grocer") && w.Traffic.Delivered > before && lorry.Job == null,
                $"Taking the bakery down while the lorry is on its way: it takes the crates to the grocer instead, and finishes ({redirected}; grocer {w.People.StockAt(gi):0.0} crates)");
            UnityEngine.Object.DestroyImmediate(w2.gameObject);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Sends one villager (by seed) to a place, once.</summary>
        private sealed class SendOnce : IVillageDecider
        {
            public int Seed, House; public bool Sent;
            public bool Active => !Sent;
            public bool Decide(PeopleSystem people, int villager, out Intent plan)
            {
                plan = Intent.Nothing;
                if (Sent || people.People[villager].seed != Seed) return false;
                Sent = people.PlanVisit(villager, House, Need.Food, out plan);
                return Sent;
            }
        }

        /// <summary>
        /// Taxis: a villager going to the café at the far end of the road walks to the kerb, waits, gets into the taxi
        /// from the rank, rides in the back (where the taxi is, visibly sitting there) and gets out at the far kerb, walks
        /// to the café and sits down; the taxi goes back to its rank. Without a road at the far end nobody hails one.
        /// </summary>
        private static void Taxis()
        {
            var w = Scratch("Taxis", 80f);
            var t = w.Terrain;
            string road = w.BuildPath(Line(t, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f)), PathKind.Road);
            string rank = w.PlaceHouse(new HouseInstance { x = -0.4f, z = 0.06f, variant = Catalog.TaxiRank });
            string cafe = w.PlaceHouse(new HouseInstance { x = 0.55f, z = 0.11f, variant = Catalog.Cafe });
            int ci = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Cafe);
            w.PlacePerson(new Vector3(-0.6f, 0f, 0.07f), 2, 777);
            w.People.Grid.Complete(); w.People.Step(Dt); w.Traffic.Refresh(true);
            w.Clock.Hour = 10f;
            int taxis = w.Traffic.Count(VehicleKind.Taxi);
            Check(road == null && rank == null && cafe == null && taxis == 2, $"A taxi rank by the road keeps two taxis ({taxis}{road ?? ""}{rank ?? ""}{cafe ?? ""})");
            w.People.PreferTaxi = true;
            w.People.Deciders.Add(new SendOnce { Seed = 777, House = ci });
            int who() => w.People.IndexOfSeed(777);
            var said = new List<string>();
            float together = 0f, apart = 0f; bool figure = false, hidden = true;
            Live(w, 240f, s =>
            {
                int i = who();
                if (i < 0) return;
                string now = w.People.CardOf(i).Now.ToLowerInvariant();
                if (said.Count == 0 || said[^1] != now) said.Add(now);
                if (w.People.InTaxi(i))
                {
                    var taxi = w.Traffic.TaxiOf(777);
                    if (taxi != null)
                    {
                        float d = (taxi.Position - w.People.PositionOf(i)).magnitude;
                        if (d < 0.005f) together += Dt; else apart += Dt;
                        figure |= taxi.Passenger != null && taxi.Passenger.gameObject.activeSelf;
                    }
                    hidden &= w.People.IsHidden(i);
                }
            });
            string story = string.Join(" / ", said);
            Check(said.Exists(x => x.Contains("kerb for a taxi")) && said.Exists(x => x.Contains("waiting at the kerb")) && said.Exists(x => x.Contains("in a taxi")),
                $"Going to the café at the far end, they walk to the kerb, wait, and get into a taxi: {story}");
            Check(together > 3f && apart == 0f && figure && hidden, $"...ride in it - where the taxi is, all the way ({together:0.0} s), sitting in the back");
            int rode = said.FindIndex(x => x.Contains("in a taxi")), sat = rode >= 0 ? said.FindIndex(rode, x => x.Contains("sitting at the café")) : -1;
            Check(w.People.TaxiRides >= 1 && w.Traffic.FaresDone >= 1 && sat > rode && rode >= 0,
                $"...get out at the far kerb, walk to the café and sit down ({w.People.TaxiRides} rides; after the taxi: {(sat >= 0 ? said[sat] : "never sat")})");
            var back = w.Traffic.Vehicles.FindAll(v => v.Kind == VehicleKind.Taxi);
            Live(w, 60f);
            Check(back.TrueForAll(v => v.Fare == null), $"The taxi is free again afterwards ({string.Join(", ", back.ConvertAll(v => w.Traffic.Describe(v)))})");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>
        /// Tractors: a wheat field has a tractor at its corner; when its farmer comes to work they climb aboard (seen at
        /// the wheel) and drive it up and down the field in rows, staying inside the field; after the shift it goes back
        /// to its corner and the farmer climbs down.
        /// </summary>
        private static void Tractors()
        {
            var w = Scratch("Tractors", 120f);
            w.PlaceHouse(new HouseInstance { x = -0.3f, z = 0.25f, variant = Catalog.Cottage });
            string field = w.PlaceHouse(new HouseInstance { x = 0.15f, z = 0.05f, variant = Catalog.WheatField });
            int fi = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.WheatField);
            w.People.Grid.Complete(); w.Clock.Hour = 7f; w.People.Step(Dt); w.Traffic.Refresh(true);
            var tractor = w.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Tractor);
            Check(field == null && tractor != null, $"A wheat field keeps a tractor at its corner ({(tractor != null ? w.Traffic.Describe(tractor) : "none")}{field ?? ""})");
            if (tractor == null) return;
            var start = tractor.Position;
            var said = new List<string>();
            float roamed = 0f, outside = 0f; bool driver = false, farmerHidden = false;
            var h = w.Buildings.Houses[fi];
            var half = Catalog.Get(h.variant).Half;
            Live(w, 400f, s =>
            {
                string line = w.Traffic.Describe(tractor);
                if (said.Count == 0 || said[^1] != line) said.Add(line);
                roamed = Mathf.Max(roamed, (tractor.Position - start).magnitude);
                var local = Quaternion.Euler(0f, -h.rotation, 0f) * new Vector3(tractor.Position.x - h.x, 0f, tractor.Position.y - h.z);
                if (Mathf.Abs(local.x) > half.x + 0.01f || Mathf.Abs(local.z) > half.y + 0.03f) outside = Mathf.Max(outside, Mathf.Max(Mathf.Abs(local.x) - half.x, Mathf.Abs(local.z) - half.y));
                if (w.People.OnTractor(fi)) { driver |= tractor.Passenger != null && tractor.Passenger.gameObject.activeSelf; int f = w.People.WorkerAt(fi); farmerHidden |= f >= 0 && w.People.IsHidden(f); }
            });
            string story = string.Join(" / ", said);
            Check(said.Exists(x => x.Contains("ploughing")) && roamed > half.x && driver && farmerHidden && outside < 0.03f,
                $"Its farmer climbs aboard and drives it up and down the field, staying in it ({roamed * 100f:0} cm about, {outside * 100f:0.0} cm past the edge): {story}");
            int farmer = w.People.WorkerAt(fi);
            Check(farmer >= 0, $"...the farmer is someone who lives here ({(farmer >= 0 ? w.People.CardOf(farmer).Now : "nobody took the job")})");
            w.Clock.Hour = 18f;
            Live(w, 90f);
            Check(!w.People.OnTractor(fi) && (tractor.Position - start).magnitude < 0.02f && (tractor.Passenger == null || !tractor.Passenger.gameObject.activeSelf),
                $"After the shift it goes back to its corner and the farmer climbs down ({w.Traffic.Describe(tractor)})");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>
        /// The working town: the sawmill's lorry takes planks to the furniture shop; the post van empties every letterbox on
        /// its round and comes back; visitors' cars use the car park by the shops (and leave it again); the ice cream parlour
        /// has customers in the afternoon and the pub in the evening.
        /// </summary>
        private static void Town()
        {
            var w = Scratch("Working town", 160f);
            var t = w.Terrain;
            w.BuildPath(Line(t, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f)), PathKind.Road);
            w.BuildPath(Line(t, new Vector2(0f, 0f), new Vector2(0f, 0.6f)), PathKind.Road);
            void Put(int id, float x, float z, float rot = 0f) { string e = w.PlaceHouse(new HouseInstance { x = x, z = z, rotation = rot, variant = id }); if (e != null) Report.AppendLine($"INFO  {Catalog.Get(id).Name} at ({x}, {z}): {e}"); }
            Put(Catalog.Sawmill, -0.5f, 0.12f); Put(Catalog.FurnitureShop, 0.5f, 0.11f);
            Put(Catalog.PostOffice, -0.25f, -0.11f, 180f);
            Put(Catalog.Letterbox, 0.3f, -0.04f); Put(Catalog.Letterbox, 0.04f, 0.4f);
            Put(Catalog.Pub, 0.25f, 0.11f); Put(Catalog.IceCream, -0.2f, 0.11f);
            Put(Catalog.CarPark, 0.35f, -0.12f, 180f);
            float[] homes = { -0.55f, -0.4f, 0.55f };
            foreach (float x in homes) Put(Catalog.Cottage, x, -0.12f, 180f);
            foreach (float z in new[] { 0.25f, 0.4f, 0.55f }) { Put(Catalog.Townhouse, -0.09f, z, 90f); Put(Catalog.Cottage, 0.1f, z, -90f); }
            int saw = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Sawmill), shop = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.FurnitureShop);
            w.People.Grid.Complete(); w.Clock.Hour = 9.3f; w.People.Step(Dt); w.Traffic.Refresh(true);
            w.People.SetStock(saw, 6f); w.People.SetStock(shop, 0f);
            var lorry = w.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Flatbed);
            var van = w.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Van && v.Depot >= 0);
            Check(lorry != null && lorry.Cargo == Cargo.Timber && van != null, $"The sawmill keeps a timber lorry and the post office a van ({(lorry != null ? w.Traffic.Describe(lorry) : "no lorry")}; {(van != null ? w.Traffic.Describe(van) : "no van")})");
            var vanSaid = new List<string>(); var lorrySaid = new List<string>();
            int carPark = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.CarPark);
            int mostParked = 0, logsShown = 0;
            Live(w, 420f, s =>
            {
                if (van != null) { string l = w.Traffic.Describe(van); if (vanSaid.Count == 0 || vanSaid[^1] != l) vanSaid.Add(l); }
                if (lorry != null) { string l = w.Traffic.Describe(lorry); if (lorrySaid.Count == 0 || lorrySaid[^1] != l) lorrySaid.Add(l); if (lorry.Job is { Stage: 2 }) logsShown = Mathf.Max(logsShown, lorry.Job.Crates); }
                mostParked = Mathf.Max(mostParked, w.Traffic.ParkedAt(carPark));
            });
            Check(lorrySaid.Exists(x => x.Contains("planks to the furniture shop")) && logsShown >= 2 && w.People.StockAt(shop) >= 1f,
                $"The sawmill's lorry takes planks to the furniture shop ({logsShown} loads of logs on the bed): {string.Join(" / ", lorrySaid)}");
            Check(w.Traffic.Rounds >= 1 && vanSaid.Exists(x => x.Contains("emptying the letterbox")) && vanSaid.Exists(x => x.Contains("delivering the post")),
                $"At half past nine the post van empties the letterboxes, delivers the post and comes back ({w.Traffic.Rounds} rounds): {string.Join(" / ", vanSaid)}");
            int cars = 0; foreach (var v in w.Traffic.Vehicles) if (v.Kind is VehicleKind.Car or VehicleKind.Motorbike) cars++;
            Check(mostParked >= 1, $"Visitors' cars park in the car park by the shops (up to {mostParked} at once; {cars} household cars and motorbikes)");
            // Afternoon at the ice cream parlour, evening at the pub.
            int ice = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.IceCream), pub = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Pub);
            int iceVisits = 0, pubVisits = 0;
            w.Clock.Hour = 13.5f;
            float h0 = w.Clock.Hour;
            Live(w, 120f, s => iceVisits = Mathf.Max(iceVisits, w.People.PlaceCardOf(ice).Visitors + w.People.PlaceCardOf(ice).Today));
            Report.AppendLine($"INFO  afternoon: {h0:0.0} -> {w.Clock.Hour:0.0} h, {w.People.People.Count} villagers; ice cream: {w.People.DescribePlace(ice)}");
            w.Clock.Hour = 18.5f;
            Live(w, 120f, s => pubVisits = Mathf.Max(pubVisits, w.People.PlaceCardOf(pub).Visitors + w.People.PlaceCardOf(pub).Today));
            Report.AppendLine($"INFO  evening: -> {w.Clock.Hour:0.0} h; pub: {w.People.DescribePlace(pub)}");
            Check(iceVisits >= 1 && pubVisits >= 1, $"The ice cream parlour has customers in the afternoon ({iceVisits}) and the pub in the evening ({pubVisits})");
            Live(w, 200f);
            Check(w.Traffic.ParkedAt(carPark) <= mostParked, $"...and the car park empties again as visits end ({w.Traffic.ParkedAt(carPark)} parked late on)");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>The living world's buildings in a row, fronts to the camera (docs/renders/living-buildings*.png).</summary>
        public static void LivingGallery()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var key = WonderlandLighting.CreateKeyLight();
            WonderlandRenders.CreateFloor(4f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 0.3f, -0.9f), new Vector3(0f, 0.05f, 0f), 46f);
            int[][] sets =
            {
                new[] { Catalog.Sawmill, Catalog.FurnitureShop, Catalog.IceCream, Catalog.Pub, Catalog.Hotel, Catalog.PostOffice },
                new[] { Catalog.TaxiRank, Catalog.Letterbox, Catalog.CarPark },
            };
            for (int set = 0; set < sets.Length; set++)
            {
                var root = new GameObject("Row").transform;
                float x = 0f; var ids = sets[set];
                float total = 0f; foreach (int id in ids) total += Catalog.Get(id).Half.x * 2f + 0.03f;
                x = -total / 2f;
                foreach (int id in ids)
                {
                    float w = Catalog.Get(id).Half.x * 2f + 0.03f;
                    WonderlandRenders.Place(root, HouseFactory.Get(id, 0), WonderlandMaterials.Lit, new Vector3(x + w / 2f, 0f, 0f), Quaternion.identity);
                    x += w;
                }
                float back = set == 0 ? 0.95f : 0.45f;
                cam.transform.position = new Vector3(0f, back * 0.32f, -back); cam.transform.LookAt(new Vector3(0f, 0.04f, 0f));
                WonderlandRenders.Capture(cam, $"living-buildings-{set + 1}.png", 2400, 1100);
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Close-ups of the living world: a lorry loading at the barn, a taxi with its passenger, the farmer on the tractor.</summary>
        public static void LivingRenders()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            BuildGrid.Snap = false;
            var w = Scratch("Living renders", 0f);
            var t = w.Terrain;
            w.Sky.Shown = false;
            w.BuildPath(Line(t, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f)), PathKind.Road);
            w.PlaceHouse(new HouseInstance { x = -0.45f, z = 0.11f, variant = Catalog.Barn });
            w.PlaceHouse(new HouseInstance { x = 0.45f, z = 0.11f, variant = Catalog.Bakery });
            w.PlaceHouse(new HouseInstance { x = -0.1f, z = -0.06f, rotation = 180f, variant = Catalog.TaxiRank });
            w.PlaceHouse(new HouseInstance { x = 0.05f, z = 0.3f, variant = Catalog.WheatField });
            w.PlaceHouse(new HouseInstance { x = -0.3f, z = 0.4f, variant = Catalog.Cottage });
            w.PlaceHouse(new HouseInstance { x = -0.45f, z = 0.55f, variant = Catalog.Cottage });
            w.PlaceHouse(new HouseInstance { x = 0.4f, z = 0.55f, variant = Catalog.Thatched });
            w.PlacePerson(new Vector3(-0.6f, 0f, 0.07f), 2, 777);
            int bi = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Barn), ki = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
            int ci = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
            w.People.Grid.Complete(); w.Clock.Hour = 7.5f; w.People.Step(Dt); w.Traffic.Refresh(true);
            w.People.SetStock(bi, 6f); w.People.SetStock(ki, 0f);
            w.People.PreferTaxi = true;
            w.People.Deciders.Add(new SendOnce { Seed = 777, House = ci });
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.32f, 0.3f, 0.28f);
            cam.nearClipPlane = 0.01f; cam.fieldOfView = 30f;
            void Shoot(Vector2 at, Vector3 from, string file)
            {
                var p = w.transform.TransformPoint(new Vector3(at.x, t.SurfaceAt(at.x, at.y) + 0.01f, at.y));
                cam.transform.position = p + from;
                cam.transform.rotation = Quaternion.LookRotation(p - cam.transform.position);
                WonderlandRenders.Capture(cam, file, 1400, 900);
            }
            var lorry = w.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Flatbed);
            var tractor = w.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Tractor);
            bool loading = false, taxi = false, plough = false;
            for (float s = 0f; s < 400f && !(loading && taxi && plough); s += Dt)
            {
                w.Clock.Tick(Dt); w.People.Step(Dt); w.Traffic.Step(Dt);
                if (!loading && lorry?.Job is { Stage: 1, Shown: >= 3 }) { loading = true; Shoot(lorry.Position, new Vector3(-0.12f, 0.1f, -0.14f), "living-lorry-loading.png"); }
                var cab = w.Traffic.TaxiOf(777);
                if (!taxi && cab != null && cab.Speed > 0.02f) { taxi = true; Shoot(cab.Position, new Vector3(0.1f, 0.08f, -0.12f), "living-taxi-passenger.png"); }
                if (!plough && tractor != null && w.People.OnTractor(tractor.Field) && tractor.Speed > 0.01f) { plough = true; Shoot(tractor.Position, new Vector3(-0.09f, 0.08f, -0.1f), "living-tractor.png"); }
            }
            Debug.Log($"Living renders: loading {loading}, taxi {taxi}, plough {plough}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
