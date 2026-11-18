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
            try { Immediate(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Deliveries(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Taxis(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Tractors(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Town(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { FreightByRail(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Shipping(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Attractions(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Waterways(); }
            catch (Exception e) { Fail("Exception: " + e); }
            try { Soak(); }
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
        /// Immediate: in a lived-in village at ten in the morning, a barn and a bakery go up by the road. Within a minute
        /// someone has taken each job and is on the way to work, and the barn's lorry has set off with a first load for the
        /// bakery - no waiting for tomorrow.
        /// </summary>
        private static void Immediate()
        {
            var w = Scratch("Immediate", 480f);
            var t = w.Terrain;
            w.BuildPath(Line(t, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f)), PathKind.Road);
            foreach (var (x, z) in new[] { (-0.3f, -0.12f), (-0.1f, -0.12f), (0.1f, -0.12f), (0.3f, -0.12f) }) w.PlaceHouse(new HouseInstance { x = x, z = z, rotation = 180f, variant = Catalog.Cottage });
            w.People.Grid.Complete(); w.Clock.Hour = 10f;
            Live(w, 20f);   // (the village settles in)
            w.PlaceHouse(new HouseInstance { x = -0.45f, z = 0.11f, variant = Catalog.Barn });
            w.PlaceHouse(new HouseInstance { x = 0.45f, z = 0.11f, variant = Catalog.Factory });
            w.PlaceHouse(new HouseInstance { x = 0.2f, z = 0.11f, variant = Catalog.Bakery });
            int barn = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Barn), works = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Factory);
            float hired = -1f, onTheWay = -1f, lorryOff = -1f;
            Live(w, 60f, s =>
            {
                int worker = w.People.WorkerAt(works);
                if (hired < 0f && worker >= 0) hired = s;
                if (onTheWay < 0f && worker >= 0 && w.People.StateOf(worker) is PeopleSystem.Activity.Walking or PeopleSystem.Activity.Working) onTheWay = s;
                var lorry = w.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Flatbed);
                if (lorryOff < 0f && lorry?.Job != null) lorryOff = s;
            });
            Check(hired >= 0f && hired < 5f && onTheWay >= 0f && onTheWay < 10f, $"A new factory has a worker at once, on the way to work (hired after {hired:0.0} s, setting off after {onTheWay:0.0} s)");
            Check(lorryOff >= 0f && lorryOff < 15f, $"The new barn's lorry sets off with a first load at once (after {lorryOff:0.0} s): {w.Traffic.Describe(w.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Flatbed))}");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
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

        /// <summary>
        /// Freight by rail: crates waiting at a freight yard beside the railway; a goods train stops there and holds while
        /// the yard's crane lifts them across one by one into its open wagon (where they show); then it goes on with them.
        /// </summary>
        private static void FreightByRail()
        {
            var w = Scratch("Freight", 200f);
            var t = w.Terrain;
            var plan = w.BuildTrack(Loop(t, 0.5f, 0.38f, 0.97f, false));
            var st = w.StationAt(t.PointAt(0f, -0.3f), 0, Catalog.FreightYard);
            string yard = st.HasValue ? w.PlaceHouse(st.Value) : "no railway";
            int yi = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.FreightYard);
            w.PlaceTrain(t.PointAt(0f, 0.38f), TrainKind.Goods, Vector3.left, out var train);
            w.People.Grid.Complete(); w.Clock.Hour = 9f; w.People.Step(Dt); w.Traffic.Refresh(true); w.Freight.Step(Dt);
            Check(plan.Ok && yard == null && yi >= 0 && train != null, $"A loop, a freight yard beside it and a goods train ({yard ?? ""}{(train == null ? " no train" : "")})");
            if (yi < 0 || train == null) return;
            w.People.BringCrates(yi, 5, "crates");
            int lifted0 = w.Freight.Lifted;
            float held = 0f, craneBusy = 0f; bool shown = false, left = false; int cargoMax = 0;
            string busy = null;
            for (float s = 0f; s < 240f && !(left && cargoMax >= 3); s += Dt)
            {
                w.Clock.Tick(Dt); w.People.Step(Dt); w.Traffic.Step(Dt); w.Trains.Step(Dt); w.Freight.Step(Dt);
                bool there = train.AtStation >= 0 && w.Trains.Stations[train.AtStation].House == new Vector2(st.Value.x, st.Value.z);
                if (there) held += Dt;
                string c = w.Freight.CraneFor(yi);
                if (c.Contains("loading")) { craneBusy += Dt; busy = c; }
                cargoMax = Mathf.Max(cargoMax, train.Cargo);
                if (!shown) foreach (var mf in w.Trains.GetComponentsInChildren<MeshFilter>()) shown |= mf.name == "Load" && mf.sharedMesh != null;
                if (cargoMax >= 3 && !there && train.Speed > 0.01f) left = true;
            }
            Check(w.Freight.Lifted - lifted0 >= 3 && cargoMax >= 3 && craneBusy > 5f && held > 8f,
                $"The goods train holds at the freight yard while the crane lifts the crates across ({w.Freight.Lifted - lifted0} lifted, {cargoMax} aboard, crane busy {craneBusy:0} s, stood {held:0} s): {busy}");
            Check(shown && left, $"...its open wagon shows them, and it goes on with them ({w.Trains.Describe(train)})");
            // A passenger station on the same loop and a passenger train: each train calls only where it belongs.
            var ps = w.StationAt(t.PointAt(0f, 0.3f), 0);
            if (ps.HasValue) w.PlaceHouse(ps.Value);
            w.PlaceTrain(t.PointAt(-0.5f, 0f), TrainKind.Passenger, Vector3.forward, out var coach);
            int yardStop = w.Trains.Stations.FindIndex(x => x.Freight), passengerStop = w.Trains.Stations.FindIndex(x => !x.Freight);
            bool goodsAtStation = false, coachAtYard = false, coachStopped = false, goodsStopped = false;
            for (float s = 0f; s < 200f; s += Dt)
            {
                w.Clock.Tick(Dt); w.People.Step(Dt); w.Trains.Step(Dt); w.Freight.Step(Dt);
                goodsAtStation |= train.AtStation == passengerStop && passengerStop >= 0;
                coachAtYard |= coach != null && coach.AtStation == yardStop && yardStop >= 0;
                coachStopped |= coach != null && coach.AtStation == passengerStop && passengerStop >= 0;
                goodsStopped |= train.AtStation == yardStop;
            }
            Check(ps.HasValue && coach != null && !goodsAtStation && !coachAtYard && coachStopped && goodsStopped,
                $"Goods trains call at the freight yard but pass the station; passenger trains the other way round [goods at station {goodsAtStation}, passenger at yard {coachAtYard}, passenger stopped {coachStopped}, goods stopped {goodsStopped}]");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Digs a long lake along z = 0 from x0 to x1 (radius r).</summary>
        private static void Lake(WonderlandWorld w, float x0, float x1, float r)
        {
            var mid = w.Terrain.PointAt((x0 + x1) / 2f, 0f);
            float level = w.WaterLevelForStroke(mid);
            for (float x = x0; x <= x1; x += 0.06f) { var at = w.Terrain.PointAt(x, 0f); Hold(() => w.WaterStroke(at, r, 0.9f, Dt, level, false), 0.6f); }
        }

        /// <summary>The first place a harbour can stand between (x0, z0) and (x1, z1), facing -Z, or null.</summary>
        private static HouseInstance? HarbourSpot(WonderlandWorld w, float x0, float x1, float z0, float z1)
        {
            for (float z = z0; z <= z1; z += 0.01f)
            for (float x = x0; x <= x1; x += 0.02f)
            {
                var h = new HouseInstance { x = x, z = z, rotation = 0f, variant = Catalog.Harbour };
                if (w.ValidateHouse(h) == null) return h;
            }
            return null;
        }

        /// <summary>
        /// Shipping: two harbours on the shore of one long lake get a container ship that sails between them over the
        /// water; at each, the quay crane unloads its containers onto the quay and loads what waits for shipping. A third
        /// harbour on a pond of its own stays quiet. Taking a harbour away sends its ship away.
        /// </summary>
        private static void Shipping()
        {
            var w = Scratch("Shipping", 240f);
            Lake(w, -0.55f, 0.55f, 0.22f);
            var a = HarbourSpot(w, -0.5f, -0.25f, 0.18f, 0.45f);
            var b = HarbourSpot(w, 0.25f, 0.5f, 0.18f, 0.45f);
            string ea = a.HasValue ? w.PlaceHouse(a.Value) : "no spot", eb = b.HasValue ? w.PlaceHouse(b.Value) : "no spot";
            w.People.Grid.Complete(); w.Clock.Hour = 9f; w.People.Step(Dt); w.Harbours.Step(Dt); w.Harbours.Step(2.1f);
            Check(ea == null && eb == null && w.Harbours.ShipCount == 1, $"Two harbours on one lake get a container ship ({w.Harbours.ShipCount} ships{(ea != null ? ", A: " + ea : "")}{(eb != null ? ", B: " + eb : "")})");
            if (w.Harbours.ShipCount == 0) return;
            int ia = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Harbour), ib = w.Buildings.Houses.FindLastIndex(h => h.variant == Catalog.Harbour);
            var said = new List<string>();
            bool overLand = false;
            var ship = w.Harbours.transform.Find("Container ship");
            Live(w, 300f, s =>
            {
                w.Harbours.Step(Dt);
                string l = w.Harbours.HarbourFor(ib);
                if (said.Count == 0 || said[^1] != l) said.Add(l);
                if (ship != null && w.Terrain.WaterAt(ship.localPosition.x, ship.localPosition.z) < 0.3f) overLand = true;
            });
            Check(w.Harbours.Voyages >= 1 && w.Harbours.Lifted >= 4 && !overLand && said.Exists(x => x.Contains("unloading")),
                $"The ship is unloaded by the crane, sails over the water to the other harbour and is unloaded there ({w.Harbours.Voyages} voyages, {w.Harbours.Lifted} containers lifted): {string.Join(" / ", said)}");
            Check(w.People.StockAt(ib) >= 1f || w.People.StockAt(ia) >= 1f, $"...the containers wait on the quay for the harbour's lorry ({w.People.StockAt(ia):0} and {w.People.StockAt(ib):0})");
            // A harbour on a pond of its own: no ship, and its card says why.
            var w2 = Scratch("Pond harbour", 280f);
            Lake(w2, -0.1f, 0.1f, 0.2f);
            var c = HarbourSpot(w2, -0.2f, 0.2f, 0.15f, 0.45f);
            if (c.HasValue) w2.PlaceHouse(c.Value);
            w2.People.Grid.Complete(); w2.Harbours.Step(2.1f);
            int ic = w2.Buildings.Houses.FindIndex(h => h.variant == Catalog.Harbour);
            Check(ic >= 0 && w2.Harbours.ShipCount == 0 && w2.Harbours.HarbourFor(ic).Contains("no other harbour"), $"A lone harbour stays quiet ({(ic >= 0 ? w2.Harbours.HarbourFor(ic) : "couldn't place one")})");
            // Taking a harbour away: the ship goes.
            w.RemoveHouse(ia);
            w.Harbours.Step(2.1f);
            Check(w.Harbours.ShipCount == 0, $"Taking a harbour away sends its ship away ({w.Harbours.ShipCount} ships left)");
            UnityEngine.Object.DestroyImmediate(w2.gameObject);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>
        /// Attractions: the zoo's animals wander about their enclosures (never out of them) and villagers come to see them;
        /// at kick-off time a match starts at the stadium - villagers come to watch, the stands fill, the teams play - and
        /// after the final whistle the crowd goes.
        /// </summary>
        private static void Attractions()
        {
            var w = Scratch("Attractions", 320f);
            void Put(int id, float x, float z, float rot = 0f) { string e = w.PlaceHouse(new HouseInstance { x = x, z = z, rotation = rot, variant = id }); if (e != null) Report.AppendLine($"INFO  {Catalog.Get(id).Name}: {e}"); }
            Put(Catalog.Zoo, -0.3f, 0.25f); Put(Catalog.Stadium, 0.3f, 0.25f); Put(Catalog.FairyCastle, 0f, -0.3f);
            foreach (var (x, z) in new[] { (-0.5f, -0.1f), (-0.35f, -0.1f), (0.35f, -0.1f), (0.5f, -0.1f), (-0.5f, -0.45f), (0.5f, -0.45f), (-0.2f, 0.0f), (0.2f, 0.0f) }) Put(Catalog.Cottage, x, z);
            int zoo = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Zoo), ground = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Stadium);
            w.People.Grid.Complete(); w.Clock.Hour = 10f; w.People.Step(Dt); w.Attractions.Step(1.1f);
            Check(zoo >= 0 && ground >= 0 && w.Attractions.AnimalsAt(zoo) >= 10, $"A zoo full of animals and a stadium ({(zoo >= 0 ? w.Attractions.ZooFor(zoo) : "no zoo")})");
            if (zoo < 0 || ground < 0) return;
            var animals = w.Attractions.transform.Find("Zoo animals");
            var start = new List<Vector3>(); foreach (Transform a in animals) start.Add(a.localPosition);
            bool escaped = false; int seen = 0;
            Live(w, 150f, s =>
            {
                w.Attractions.Step(Dt);
                foreach (Transform a in animals)
                {
                    var p = a.localPosition;
                    bool inPen = false;
                    foreach (var (c, half) in HouseFactory.ZooPens) inPen |= Mathf.Abs(p.x - c.x) <= half.x && Mathf.Abs(p.z - c.y) <= half.y;
                    escaped |= !inPen;
                }
                seen = Mathf.Max(seen, w.People.PlaceCardOf(zoo).Visitors + w.People.PlaceCardOf(zoo).Today);
            });
            float moved = 0f; int k = 0; foreach (Transform a in animals) moved = Mathf.Max(moved, (a.localPosition - start[k++]).magnitude);
            Check(moved > 0.01f && !escaped, $"The zoo's animals wander about their enclosures and stay in them (furthest {moved * 100f:0.0} cm)");
            Check(seen >= 1, $"Villagers come to see them ({seen} visits)");
            // A match.
            w.Clock.Hour = 19.45f;   // (the zoo's afternoon took the clock past three: the evening match)
            var news = new List<string>(); w.People.NewsLog.Clear();
            int most = 0; bool crowd = false, playing = false;
            Live(w, 80f, s =>
            {
                w.Attractions.Step(Dt);
                foreach (var n in w.People.NewsLog) if (!news.Contains(n)) news.Add(n);
                var c = w.Attractions.transform.Find("Stadium/Crowd");
                crowd |= c != null && c.gameObject.activeSelf;
                playing |= w.Attractions.MatchOn(ground);
                int near = 0;
                var h = w.Buildings.Houses[ground];
                for (int i = 0; i < w.People.People.Count; i++) if ((w.People.PositionOf(i) - new Vector2(h.x, h.z)).magnitude < 0.25f) near++;
                most = Mathf.Max(most, near);
            });
            Check(playing && crowd && most >= 3 && news.Exists(n => n.Contains("Kick-off")),
                $"At kick-off time a match starts: villagers come to watch ({most} at the stadium), the stands fill, the teams play [playing {playing}, crowd {crowd}] ({w.Attractions.StadiumFor(ground)}; {string.Join(" / ", news.FindAll(n => n.Contains("stadium")))})");
            w.Clock.Hour = 21.1f;
            Live(w, 10f, s => w.Attractions.Step(Dt));
            var after = w.Attractions.transform.Find("Stadium/Crowd");
            Check(!w.Attractions.MatchOn(ground) && after != null && !after.gameObject.activeSelf, $"After the final whistle the stands empty ({w.Attractions.StadiumFor(ground)})");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>
        /// Water: a creek, a stream and a river drawn side by side carve channels of growing width; a stream down a steep
        /// hill gets a waterfall with spray at its foot; widths survive saving and undo.
        /// </summary>
        private static void Waterways()
        {
            var w = Scratch("Waterways", 360f);
            var t = w.Terrain;
            float Channel(float x)
            {
                // How wide the carved channel is across the stream at its middle (ground at least 3 mm below its banks).
                float bank = Mathf.Min(t.SurfaceAt(x - 0.14f, 0.05f), t.SurfaceAt(x + 0.14f, 0.05f)), width = 0f;
                for (float dx = -0.12f; dx <= 0.12f; dx += 0.002f) if (t.SurfaceAt(x + dx, 0.05f) < bank - 0.003f) width += 0.002f;
                return width;
            }
            string a = w.BuildStream(Line(t, new Vector2(-0.3f, 0.4f), new Vector2(-0.3f, -0.3f)), 0.6f);
            string b = w.BuildStream(Line(t, new Vector2(0f, 0.4f), new Vector2(0f, -0.3f)), 1f);
            string c = w.BuildStream(Line(t, new Vector2(0.3f, 0.4f), new Vector2(0.3f, -0.3f)), 2.2f);
            float ca = Channel(-0.3f), cb = Channel(0f), cc = Channel(0.3f);
            Check(a == null && b == null && c == null && ca < cb && cb < cc && cc > cb * 1.6f,
                $"A creek, a stream and a river carve channels of growing width ({ca * 100f:0.0}, {cb * 100f:0.0}, {cc * 100f:0.0} cm){a ?? ""}{b ?? ""}{c ?? ""}");
            var w2 = Scratch("Waterfall", 400f);
            var t2 = w2.Terrain;
            Hold(() => w2.TerrainStroke(TerrainBrush.Raise, new Vector3(0f, 0f, 0.3f), 0.18f, 1f, Dt, 0f), 4f);
            string fall = w2.BuildStream(Line(t2, new Vector2(0f, 0.3f), new Vector2(0f, -0.35f)), 1f);
            Check(fall == null && w2.Streams.Waterfalls >= 1, $"A stream tumbling down a steep hill has a waterfall, spray at its foot ({w2.Streams.Waterfalls} falls, hill {t2.SurfaceAt(0f, 0.3f) * 100f:0.0} cm high){fall ?? ""}");
            // A creek drawn to end on the river joins it: its end on the river's course, full width.
            string trib = w.BuildStream(Line(t, new Vector2(0.05f, -0.2f), new Vector2(0.27f, -0.12f)), 0.6f);
            var creek = w.Streams.Streams[^1];
            float gap = StreamSystem.Nearest(w.Streams.Streams[2], new Vector2(creek.points[^1].x, creek.points[^1].z), out _, out _);
            Check(trib == null && creek.JoinsAtEnd && gap < 0.003f, $"A creek drawn to the river joins it (its end {gap * 100f:0.0} cm from the river's course){trib ?? ""}");
            w.Undo();
            w.Undo(); w.Redo();
            Check(w.Streams.Streams.Count == 3 && Mathf.Abs(w.Streams.Streams[2].Width - 2.2f) < 0.01f, $"Widths survive undo and redo ({(w.Streams.Streams.Count > 2 ? w.Streams.Streams[2].Width : 0f):0.0})");
            UnityEngine.Object.DestroyImmediate(w2.gameObject);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>
        /// Everything at once, for a long while, with things taken away part way: a town with roads, farms, a sawmill, shops,
        /// a pub, a post office and letterboxes, a taxi rank, a car park, a zoo and a stadium. Nothing may get stuck: no
        /// delivery, fare or round lasts for ever, nobody waits for a taxi indefinitely, and after the taxi rank, the barn and
        /// a shop are taken away mid-day everything carries on.
        /// </summary>
        private static void Soak()
        {
            var w = Scratch("Soak", 440f);
            var t = w.Terrain;
            w.BuildPath(Line(t, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f)), PathKind.Road);
            w.BuildPath(Line(t, new Vector2(0f, -0.6f), new Vector2(0f, 0.6f)), PathKind.Road);
            void Put(int id, float x, float z, float rot = 0f) => w.PlaceHouse(new HouseInstance { x = x, z = z, rotation = rot, variant = id });
            Put(Catalog.Barn, -0.5f, 0.11f); Put(Catalog.WheatField, -0.45f, 0.35f); Put(Catalog.Sawmill, -0.5f, -0.12f, 180f);
            Put(Catalog.Bakery, 0.15f, 0.1f); Put(Catalog.Grocer, 0.3f, 0.1f); Put(Catalog.FurnitureShop, 0.45f, 0.1f);
            Put(Catalog.Pub, 0.15f, -0.11f, 180f); Put(Catalog.PostOffice, 0.3f, -0.11f, 180f); Put(Catalog.CarPark, 0.5f, -0.13f, 180f);
            Put(Catalog.TaxiRank, -0.2f, 0.05f); Put(Catalog.Letterbox, -0.08f, 0.04f); Put(Catalog.Letterbox, 0.04f, 0.3f);
            Put(Catalog.Zoo, 0.35f, 0.4f); Put(Catalog.Stadium, 0.35f, -0.42f, 180f); Put(Catalog.Factory, -0.3f, -0.45f);
            foreach (var (x, z) in new[] { (-0.1f, 0.2f), (-0.1f, 0.35f), (-0.1f, 0.5f), (0.1f, 0.25f), (0.1f, 0.42f), (-0.1f, -0.25f), (-0.1f, -0.4f), (0.1f, -0.3f), (-0.25f, 0.2f), (-0.65f, 0.3f) })
                Put(Catalog.Cottage, x, z, x < 0f ? 90f : -90f);
            w.People.Grid.Complete(); w.Clock.Hour = 7f; w.People.Step(Dt); w.Traffic.Refresh(true);
            var started = new Dictionary<object, float>();
            float longest = 0f; string longestWhat = null;
            int errors = 0; string firstError = null;
            void OnLog(string msg, string trace, LogType type) { if (type is LogType.Exception or LogType.Error) { errors++; firstError ??= msg; } }
            Application.logMessageReceived += OnLog;
            try
            {
                bool removed = false;
                Live(w, 900f, s =>
                {
                    w.Attractions.Step(Dt); w.Freight.Step(Dt); w.Harbours.Step(Dt);
                    foreach (var v in w.Traffic.Vehicles)
                    {
                        object job = (object)v.Job ?? (object)v.Fare ?? v.Round;
                        if (job == null) continue;
                        if (!started.ContainsKey(job)) started[job] = s;
                        float age = s - started[job];
                        if (age > longest) { longest = age; longestWhat = w.Traffic.Describe(v); }
                    }
                    if (!removed && w.Clock.Hour > 12f)
                    {
                        removed = true;
                        foreach (int id in new[] { Catalog.TaxiRank, Catalog.Barn, Catalog.Bakery }) { int k = w.Buildings.Houses.FindIndex(h => h.variant == id); if (k >= 0) w.RemoveHouse(k); }
                    }
                });
            }
            finally { Application.logMessageReceived -= OnLog; }
            int hailing = 0; for (int i = 0; i < w.People.People.Count; i++) if (w.People.StateOf(i) is PeopleSystem.Activity.Hailing or PeopleSystem.Activity.InTaxi) hailing++;
            Check(errors == 0, $"A busy town for a whole day, things taken away at noon: no errors ({errors}{(firstError != null ? ": " + firstError : "")})");
            Check(longest < 240f, $"No delivery, fare or post round lasts for ever (longest {longest:0} s: {longestWhat})");
            Check(w.Traffic.Delivered >= 1 && w.Traffic.Rounds >= 1 && hailing == 0,
                $"Deliveries and post rounds happened and ended, and nobody is left waiting for a taxi ({w.Traffic.Delivered} deliveries, {w.Traffic.Rounds} rounds, {w.Traffic.FaresDone} fares; {hailing} still waiting or riding at the end)");
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
                new[] { Catalog.TaxiRank, Catalog.Letterbox, Catalog.CarPark, Catalog.FreightYard, Catalog.Harbour },
                new[] { Catalog.Zoo, Catalog.FairyCastle, Catalog.Stadium },
                new[] { Catalog.FairyCastle },
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
                float back = set == 0 ? 0.95f : set == 1 ? 0.75f : set == 2 ? 0.8f : 0.32f;
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
            // The freight yard's crane, lifting a crate into a goods train.
            var f = Scratch("Freight render", 60f);
            f.Sky.Shown = false;
            var ft = f.Terrain;
            f.BuildTrack(Loop(ft, 0.5f, 0.38f, 0.97f, false));
            var yardAt = f.StationAt(ft.PointAt(0f, -0.3f), 0, Catalog.FreightYard);
            if (yardAt.HasValue) f.PlaceHouse(yardAt.Value);
            int fy = f.Buildings.Houses.FindIndex(h => h.variant == Catalog.FreightYard);
            f.PlaceTrain(ft.PointAt(0f, 0.38f), TrainKind.Goods, Vector3.left, out var goods);
            f.People.Grid.Complete(); f.Clock.Hour = 9f; f.People.Step(Dt); f.Freight.Step(Dt);
            if (fy >= 0) f.People.BringCrates(fy, 6, "crates");
            bool shot = false;
            for (float s = 0f; s < 240f && !shot && fy >= 0; s += Dt)
            {
                f.Clock.Tick(Dt); f.People.Step(Dt); f.Trains.Step(Dt); f.Freight.Step(Dt);
                if (goods.Cargo >= 2 && f.Freight.CraneFor(fy).Contains("loading"))
                {
                    var crate = f.Freight.transform.Find("Freight yard crane/Crate");
                    if (crate == null || !crate.gameObject.activeSelf) continue;
                    var h = f.Buildings.Houses[fy];
                    var p = f.transform.TransformPoint(new Vector3(h.x, ft.SurfaceAt(h.x, h.z) + 0.04f, h.z - 0.04f));
                    cam.transform.position = p + Quaternion.Euler(0f, h.rotation, 0f) * new Vector3(0.16f, 0.12f, 0.12f);
                    cam.transform.rotation = Quaternion.LookRotation(p - cam.transform.position);
                    WonderlandRenders.Capture(cam, "living-freight-crane.png", 1400, 900);
                    shot = true;
                }
            }
            Debug.Log($"Living renders: crane {shot}");
            // A container ship alongside a harbour, the quay crane at work.
            var hw = Scratch("Harbour render", 120f);
            hw.Sky.Shown = false;
            Lake(hw, -0.55f, 0.55f, 0.22f);
            var ha = HarbourSpot(hw, -0.5f, -0.25f, 0.18f, 0.45f); var hb = HarbourSpot(hw, 0.25f, 0.5f, 0.18f, 0.45f);
            if (ha.HasValue) hw.PlaceHouse(ha.Value); if (hb.HasValue) hw.PlaceHouse(hb.Value);
            hw.People.Grid.Complete(); hw.People.Step(Dt); hw.Harbours.Step(2.1f);
            bool harbourShot = false, sailShot = false;
            for (float s = 0f; s < 200f && !(harbourShot && sailShot) && ha.HasValue; s += Dt)
            {
                hw.Clock.Tick(Dt); hw.Harbours.Step(Dt);
                var box = hw.Harbours.transform.Find("Harbour crane/Container");
                var shipT = hw.Harbours.transform.Find("Container ship");
                if (!harbourShot && box != null && box.gameObject.activeSelf)
                {
                    var p = hw.transform.TransformPoint(new Vector3(ha.Value.x, hw.Terrain.SurfaceAt(ha.Value.x, ha.Value.z) + 0.02f, ha.Value.z - 0.05f));
                    cam.transform.position = p + new Vector3(0.2f, 0.16f, -0.18f);
                    cam.transform.rotation = Quaternion.LookRotation(p - cam.transform.position);
                    WonderlandRenders.Capture(cam, "living-harbour.png", 1400, 900);
                    harbourShot = true;
                }
                if (harbourShot && !sailShot && shipT != null && Mathf.Abs(shipT.localPosition.x) < 0.1f)
                {
                    var p = hw.transform.TransformPoint(shipT.localPosition);
                    cam.transform.position = p + new Vector3(-0.15f, 0.12f, -0.2f);
                    cam.transform.rotation = Quaternion.LookRotation(p - cam.transform.position);
                    WonderlandRenders.Capture(cam, "living-ship-sailing.png", 1400, 900);
                    sailShot = true;
                }
            }
            Debug.Log($"Living renders: harbour {harbourShot}, sailing {sailShot}");
            // A creek tumbling off a hill into a river, the river into a lake.
            var ww = Scratch("Water render", 180f);
            ww.Sky.Shown = false;
            var wt = ww.Terrain;
            Hold(() => ww.TerrainStroke(TerrainBrush.Raise, new Vector3(-0.35f, 0f, 0.3f), 0.2f, 1f, Dt, 0f), 4f);
            Lake(ww, 0.15f, 0.45f, 0.2f);
            ww.BuildStream(Line(wt, new Vector2(-0.35f, 0.32f), new Vector2(-0.3f, 0.05f), new Vector2(-0.15f, -0.05f)), 0.6f);
            ww.BuildStream(Line(wt, new Vector2(-0.15f, -0.05f), new Vector2(0f, -0.02f), new Vector2(0.15f, 0f)), 2.2f);
            var focus = ww.transform.TransformPoint(wt.PointAt(-0.12f, 0.05f));
            cam.transform.position = focus + new Vector3(0.05f, 0.42f, -0.55f);
            cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position);
            for (int k = 0; k < 120; k++) foreach (var ps in ww.Streams.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(1f / 30f, true, false);
            WonderlandRenders.Capture(cam, "living-river-waterfall.png", 1400, 900);
            Debug.Log($"Living renders: water ({ww.Streams.Waterfalls} falls)");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
