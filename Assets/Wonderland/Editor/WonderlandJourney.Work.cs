using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// The working village, built from the same blocks: opening hours (shops shut in the evening, the fair after
        /// dark), jobs (a baker over the bakery, a farmer in the field, a teacher indoors), produce grown on the farm and
        /// carried to the shops, bare shelves when nobody brings any, friendships made by spending time together and
        /// kept across save and load, a fête handed out by an outside decider, and deciders that hand out nonsense
        /// without breaking anything. Then produce crossing a railway on the goods train, and passengers who only
        /// board a train that goes where they are going.
        /// </summary>
        private static void VillageWorkJourney()
        {
            var w = Scratch("Working village", 230f);
            var t = w.Terrain;
            var build = new List<(int id, float x, float z, float rot)>
            {
                (Catalog.Bakery, 0.05f, 0.05f, 0f), (Catalog.Grocer, 0.25f, 0.05f, 0f), (Catalog.Cafe, -0.18f, 0.02f, 0f),
                (Catalog.Fountain, 0.05f, -0.28f, 0f), (Catalog.WheatField, -0.45f, -0.45f, 0f), (Catalog.Garden, 0.5f, -0.45f, 0f),
                (Catalog.School, 0.5f, 0.35f, 180f), (Catalog.Cottage, -0.3f, 0.35f, 180f), (Catalog.Townhouse, -0.05f, 0.4f, 180f),
                (Catalog.Apartments, 0.2f, 0.62f, 180f), (Catalog.Thatched, -0.5f, 0.05f, 90f), (Catalog.Cottage, 0.62f, -0.05f, 270f),
                (Catalog.Apartments, -0.2f, -0.68f, 0f), (Catalog.Bench, 0.25f, -0.25f, 0f), (Catalog.Carousel, -0.62f, 0.55f, 0f),
            };
            int okCount = 0;
            foreach (var (id, x, z, rot) in build)
            {
                string e = Put(w, id, x, z, rot);
                if (e == null) okCount++; else Check(false, $"Place {Catalog.Get(id).Name} at {x:0.00},{z:0.00} (\"{e}\")");
            }
            var people = w.People;
            people.Grid.Complete();
            people.Step(Dt);
            int Find(int id) => w.Buildings.Houses.FindIndex(h => h.variant == id);
            int bakery = Find(Catalog.Bakery), grocer = Find(Catalog.Grocer), cafe = Find(Catalog.Cafe), field = Find(Catalog.WheatField), garden = Find(Catalog.Garden), school = Find(Catalog.School);
            Check(okCount == build.Count, $"A working village goes up ({okCount}/{build.Count} places, {people.People.Count} villagers)");

            // Opening hours, from the catalogue.
            var bakeryBp = Catalog.Get(Catalog.Bakery);
            Check(!bakeryBp.OpenAt(5f) && bakeryBp.OpenAt(9f) && !bakeryBp.OpenAt(19f) && !Catalog.Get(Catalog.Carousel).OpenAt(21f) && Catalog.Get(Catalog.Fountain).OpenAt(3f),
                "Opening hours: the bakery opens at half six and shuts at six, the carousel stops in the evening, the fountain never closes");

            // Jobs: one worker per place, the nearest willing resident; the baker over the bakery.
            int baker = people.WorkerAt(bakery), farmer = people.WorkerAt(field), teacher = people.WorkerAt(school);
            int staffed = 0, jobs = 0;
            for (int h = 0; h < w.Buildings.Houses.Count; h++) if (Catalog.Get(w.Buildings.Houses[h].variant).Job != null) { jobs++; if (people.WorkerAt(h) >= 0) staffed++; }
            int jobless = 0; for (int i = 0; i < people.People.Count; i++) if (people.WorkplaceOf(i) < 0) jobless++;
            Check(baker >= 0 && people.HomeOf(baker) == bakery && farmer >= 0 && teacher >= 0,
                $"Jobs go to the villagers: {people.NameOf(Math.Max(0, baker))} the baker lives over the bakery, {(farmer >= 0 ? people.NameOf(farmer) : "nobody")} farms the field, {(teacher >= 0 ? people.NameOf(teacher) : "nobody")} teaches ({staffed}/{jobs} places staffed, {jobless} without a job)");

            // A working day from five in the morning: the shops start with bare shelves and the farms with nothing.
            w.Clock.Hour = 5f;
            foreach (int shop in new[] { bakery, grocer, cafe }) people.SetStock(shop, 0f);
            people.SetStock(field, 0f); people.SetStock(garden, 0f);
            var news = new List<string>();
            bool teacherIndoors = false, farmerTending = false, workedAtNight = false, carried = false, visitorAfterClosing = false, grew = false;
            float stockBefore = 0f;
            int startDay = w.Clock.Day;
            string closedAt20 = null;
            while (w.Clock.Day == startDay && w.Clock.Hour < 22.5f)
            {
                LiveListening(w, 0.5f, false, news);
                float hour = w.Clock.Hour;
                for (int i = 0; i < people.People.Count; i++)
                {
                    var st = people.StateOf(i);
                    if (st == PeopleSystem.Activity.Working && VillageClock.IsNight(hour)) workedAtNight = true;
                    if (i == teacher && st == PeopleSystem.Activity.Working && people.IsHidden(i)) teacherIndoors = true;
                    if (i == farmer && st == PeopleSystem.Activity.Working && !people.IsHidden(i) && people.PlaceOf(i) == field) farmerTending = true;
                    carried |= people.CarryingOf(i) > 0;
                    if (hour > 18.4f && people.AtPlace(i) && people.PlaceOf(i) == bakery && people.PlanOf(i).What == Intent.Kind.Spot && people.PlanOf(i).For != Need.Work) visitorAfterClosing = true;
                }
                grew |= people.StockAt(field) >= 1f || people.StockAt(garden) >= 1f;
                if (closedAt20 == null && hour >= 20f) closedAt20 = people.DescribePlace(bakery);
                if (hour < 6f) stockBefore = people.StockAt(bakery) + people.StockAt(grocer) + people.StockAt(cafe);
            }
            bool brought = news.Exists(n => n.Contains("brought the first"));
            Check(teacherIndoors && farmerTending && !workedAtNight, "Workers keep their shifts: the teacher indoors at the school, the farmer out in the field, nobody at work at night");
            Check(grew && carried && brought, $"Farms grow produce while the farmer works, and it is carried to the shops (\"{news.Find(n => n.Contains("brought the first"))}\")");
            Check(!visitorAfterClosing && closedAt20 != null && closedAt20.Contains("closed"), $"After closing time nobody shops at the bakery (\"{closedAt20}\")");
            Check(news.Exists(n => n.Contains("is the new") || n.Contains("bare")) || brought, "The village tells the player what changes: " + string.Join(" / ", news.GetRange(0, Math.Min(4, news.Count))));

            // Bare shelves: take the farms away and empty the shops at lunchtime.
            foreach (int id in new[] { Catalog.WheatField, Catalog.Garden }) { var at = w.Buildings.Houses[Find(id)]; w.EraseAt(new Vector3(at.x, 0.5f, at.z), Vector3.down, new Vector3(at.x, t.SurfaceAt(at.x, at.z), at.z)); }
            bakery = Find(Catalog.Bakery); grocer = Find(Catalog.Grocer); cafe = Find(Catalog.Cafe);
            w.Clock.Hour = 9f;
            Live(w, 1f, false);
            foreach (int shop in new[] { bakery, grocer, cafe }) people.SetStock(shop, 0f);
            news.Clear();
            for (float s = 0f; s < 150f && !news.Exists(n => n.Contains("bare")); s += 0.5f) LiveListening(w, 0.5f, false, news);
            Check(news.Exists(n => n.Contains("bare")), $"With no farm, shelves run bare and the village says so (\"{news.Find(n => n.Contains("bare"))}\")");
            Check(people.DescribePlace(bakery).Contains("bare") || people.DescribePlace(grocer).Contains("bare") || people.DescribePlace(cafe).Contains("bare"), $"...pointing at the shop shows it (\"{people.DescribePlace(bakery)}\")");

            // Friendships, made by time spent together: a few more days.
            int friendships = 0, already = 0;
            for (int i = 0; i < people.People.Count; i++) already += people.FriendCount(i);
            news.Clear();
            for (int day = 0; day < 2; day++)
            {
                int d0 = w.Clock.Day;
                while (w.Clock.Day == d0) LiveListening(w, 0.5f, false, news);
            }
            for (int i = 0; i < people.People.Count; i++) friendships += people.FriendCount(i);
            friendships /= 2;
            int someone = -1; for (int i = 0; i < people.People.Count && someone < 0; i++) if (people.FriendCount(i) > 0) someone = i;
            Check(friendships > 0 && (news.Exists(n => n.Contains("friends")) || already > 0), $"Villagers who spend time together become friends ({friendships} friendships{(already > 0 ? ", some made earlier" : "")}; \"{news.Find(n => n.Contains("friends"))}\")");
            string json = w.Serialize();
            var copy = new GameObject("Working copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(240f, 0f, 0f);
            copy.Deserialize(json, Materials());
            copy.People.Grid.Complete(); copy.People.Step(Dt);
            int copied = 0; for (int i = 0; i < copy.People.People.Count; i++) copied += copy.People.FriendCount(i);
            int bakeryCopy = copy.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
            Check(copied / 2 == friendships && Mathf.Abs(copy.People.StockAt(bakeryCopy) - people.StockAt(bakery)) < 0.3f,
                $"Save and load keep the friendships ({copied / 2}) and what is on the shelves ({copy.People.StockAt(bakeryCopy):0.0} crates at the bakery)");
            UnityEngine.Object.DestroyImmediate(copy.gameObject);

            // A fête: the first outside decider. Most of the village gathers round the fountain.
            w.Clock.Hour = 14f;
            Live(w, 1f, false);
            news.Clear();
            string fete = w.HoldFete();
            bool feteOn = w.Fete != null;
            int fountain = Find(Catalog.Fountain);
            int most = 0;
            for (float s = 0f; s < 120f && w.Fete != null; s += 0.5f)
            {
                LiveListening(w, 0.5f, false, news);
                int there = 0; for (int i = 0; i < people.People.Count; i++) if (people.PlaceOf(i) == fountain && people.AtPlace(i)) there++;
                most = Math.Max(most, there);
            }
            Check(feteOn && most >= people.People.Count / 2, $"{fete} - {most} of {people.People.Count} gather at the fountain");
            Live(w, 5f, false);
            int still = 0; for (int i = 0; i < people.People.Count; i++) if (people.PlanOf(i).What == Intent.Kind.Gather) still++;
            Check(w.Fete == null && news.Exists(n => n.Contains("over")) && still == 0, $"The fête ends after {VillageFete.Hours} hours and everyone gets on with their day ({still} still gathering)");

            // An outside decider sending everyone to the café, and one handing out nonsense: the café fills; nothing breaks.
            w.Clock.Hour = 12f;
            cafe = Find(Catalog.Cafe);
            var lunch = new SendTo(cafe);
            people.Deciders.Add(new Nonsense());
            people.Deciders.Add(lunch);
            int atCafe = 0;
            for (float s = 0f; s < 150f && atCafe < Catalog.Get(Catalog.Cafe).Spots.Length - 1; s += 0.5f)
            {
                Live(w, 0.5f, false);
                atCafe = Math.Max(atCafe, people.VisitorsAt(cafe));
            }
            int cafeSpots = Catalog.Get(Catalog.Cafe).Spots.Length;
            Check(lunch.Asked > 0 && atCafe >= cafeSpots - 1 && atCafe <= cafeSpots, $"A decider from outside fills the café ({atCafe} of {cafeSpots} spots, asked {lunch.Asked} times); one handing out nonsense is ignored");
            lunch.Done = true;
            Live(w, 1f, false);
            Check(people.Deciders.Count == 1, "Deciders that are done are dropped");
            people.Deciders.Clear();
            UnityEngine.Object.DestroyImmediate(w.gameObject);

            GoodsTrainJourney();
            BoardingJourney();
        }

        /// <summary>Produce across a railway loop: the farm inside, the bakery outside, a goods train between two stations.</summary>
        private static void GoodsTrainJourney()
        {
            var w = Scratch("Goods line", 250f);
            var t = w.Terrain;
            var rail = w.BuildTrack(Loop(t, 0.42f, 0.34f, 0.97f, false));
            var build = new List<(int id, float x, float z, float rot)>
            {
                (Catalog.WheatField, -0.05f, -0.1f, 0f), (Catalog.Cottage, -0.12f, 0.12f, 180f), (Catalog.Townhouse, 0.12f, 0.14f, 180f),
                (Catalog.Bakery, -0.66f, 0.28f, 90f), (Catalog.Cottage, 0.55f, 0.45f, 120f), (Catalog.Grocer, -0.64f, -0.26f, 90f),
            };
            int okCount = 0;
            foreach (var (id, x, z, rot) in build) if (Put(w, id, x, z, rot) == null) okCount++;
            var inner = w.StationAt(t.PointAt(0.28f, -0.02f), 0, Catalog.FreightYard);
            var outer = w.StationAt(t.PointAt(-0.56f, -0.02f), 0, Catalog.FreightYard);
            string stationError = inner.HasValue && outer.HasValue ? w.PlaceHouse(inner.Value) ?? w.PlaceHouse(outer.Value) : "no railway";
            var top = Main(w).Points[Main(w).Points.Count / 4];
            string trainError = w.PlaceTrain(top, TrainKind.Goods, Vector3.forward, out _);
            var people = w.People;
            people.Grid.Complete();
            people.Step(Dt);
            int Find(int id) => w.Buildings.Houses.FindIndex(h => h.variant == id);
            int field = Find(Catalog.WheatField), bakery = Find(Catalog.Bakery), grocer = Find(Catalog.Grocer);
            int innerStation = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.FreightYard && inner.HasValue && Mathf.Abs(h.x - inner.Value.x) < 1e-4f);
            int outerStation = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.FreightYard && outer.HasValue && Mathf.Abs(h.x - outer.Value.x) < 1e-4f);
            int farmer = people.WorkerAt(field);
            Check(rail.Ok && okCount == build.Count && stationError == null && trainError == null && farmer >= 0,
                $"A farm inside a railway loop, a bakery outside, two freight yards and a goods train ({okCount}/{build.Count} places{(stationError ?? trainError ?? "")}; farmer {(farmer >= 0 ? people.NameOf(farmer) : "none")})");
            people.SetStock(field, 3f);
            people.SetStock(bakery, 0f); people.SetStock(grocer, 0f);
            w.Clock.Hour = 8f;
            var news = new List<string>();
            float mostWaiting = 0f; int mostCargo = 0; float mostArrived = 0f; bool fetched = false;
            for (float s = 0f; s < 700f && !fetched; s += 0.5f)
            {
                if (w.Clock.Hour > 16f) { w.Clock.Hour = 8f; people.SetStock(bakery, 0f); people.SetStock(grocer, 0f); }   // a working day; the shops sell out
                LiveListening(w, 0.5f, true, news);
                mostWaiting = Mathf.Max(mostWaiting, people.WaitingAt(innerStation));
                mostCargo = Math.Max(mostCargo, w.Trains.Trains[0].Cargo);
                mostArrived = Mathf.Max(mostArrived, people.StockAt(outerStation));
                fetched = news.Exists(n => n.Contains("brought the first") && (n.Contains("bakery") || n.Contains("grocer")));
            }
            Check(mostWaiting >= 1f, $"The farmer carries crates to the freight yard for the goods train (up to {mostWaiting:0} waiting on the yard)");
            Check(mostCargo > 0 && mostArrived >= 1f, $"The yard's crane loads them onto the goods train, and the far yard's crane unloads them ({mostCargo} aboard, {mostArrived:0} unloaded)");
            Check(fetched, $"...where the shopkeeper fetches them (\"{news.Find(n => n.Contains("brought the first"))}\")");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Passengers board only a train that will call at their station: a loop with a branch line and a switch.</summary>
        private static void BoardingJourney()
        {
            var w = Scratch("Branch line", 270f);
            var t = w.Terrain;
            var loop = w.BuildTrack(Loop(t, 0.42f, 0.34f, 0.97f, false));
            var from = new Vector2(0.44f, 0.02f);
            var branch = w.BuildTrack(Line(t, from, from + new Vector2(0.12f, 0.2f), from + new Vector2(0.32f, 0.48f)));
            var n = w.Track.Network;
            var a = w.StationAt(t.PointAt(-0.32f, 0.0f), 0);
            var b = w.StationAt(t.PointAt(0.545f, 0.336f), 1);
            string stationError = a.HasValue && b.HasValue ? w.PlaceHouse(a.Value) ?? w.PlaceHouse(b.Value) : "no railway";
            string benchError = Put(w, Catalog.Bench, 0.61f, 0.46f, 35f);
            string trainError = PlaceTrainAt(w, t.PointAt(0f, 0.34f));
            int stA = w.Trains.Stations.FindIndex(st => a.HasValue && (st.House - new Vector2(a.Value.x, a.Value.z)).magnitude < 1e-3f);
            int stB = w.Trains.Stations.FindIndex(st => b.HasValue && (st.House - new Vector2(b.Value.x, b.Value.z)).magnitude < 1e-3f);
            bool ok = loop.Ok && branch.Ok && n.Joints.Count == 1 && stationError == null && benchError == null && trainError == null && stA >= 0 && stB >= 0;
            Check(ok, $"A loop with a branch line, a station on each and a train ({n.Joints.Count} switch{(stationError ?? benchError ?? trainError ?? "")})");
            if (!ok) { UnityEngine.Object.DestroyImmediate(w.gameObject); return; }
            var j = n.Joints[0];
            int branchEdge = n.Edges[0].Closed || n.Edges[0].Joint[0] >= 0 && n.Edges[0].Joint[1] >= 0 ? 1 : 0;
            int toLoop = j.Branch(0).Edge == branchEdge ? 1 : 0;
            w.Trains.Mode = SwitchMode.Manual;
            j.Setting = toLoop;
            var train = FirstTrain(w);
            j.Setting = 1 - toLoop;
            if (!w.Trains.CallsAt(train, stB, 4)) w.Trains.Reverse(train);   // running round the loop the way that can't take the branch
            j.Setting = toLoop;
            bool loopOnly = !w.Trains.CallsAt(train, stB, 4) && w.Trains.CallsAt(train, stA, 4);
            j.Setting = 1 - toLoop;
            bool branchToo = w.Trains.CallsAt(train, stB, 4);
            Check(loopOnly && branchToo, "The destination board follows the switches: set for the loop the train won't call at the branch station; set for the branch it will");

            // A villager inside the loop off to the bench by the branch station: by train from A to B.
            j.Setting = toLoop;
            var people = w.People;
            w.PlacePerson(t.PointAt(-0.22f, 0.05f), 0, 4242);
            people.Grid.Complete();
            people.Step(Dt);
            int bench = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bench);
            w.Clock.Hour = 10f; w.Clock.Mode = ClockMode.Paused;
            bool planned = people.PlanVisit(0, bench, Need.Rest, out var plan) && plan.ByTrain && people.Direct(0, plan);
            bool wrongTrain = false, waited = false;
            for (float s = 0f; s < 70f; s += 0.5f)
            {
                Live(w, 0.5f, true);
                var p = people.PlanOf(0);
                if (people.StateOf(0) == PeopleSystem.Activity.Waiting && train.AtStation == stA) waited = true;
                if (people.StateOf(0) == PeopleSystem.Activity.Riding && p.ByTrain && p.ToStation == stB) wrongTrain = true;
            }
            Check(planned && waited && !wrongTrain, $"With the switch set for the loop, the passenger lets the train go by (plan: {plan.FromStation}->{plan.ToStation})");
            j.Setting = 1 - toLoop;
            bool rode = false, arrived = false;
            for (float s = 0f; s < 400f && !arrived; s += 0.5f)
            {
                // Back from the branch the train runs round the loop the other way, which can't take the branch:
                // the passenger rightly lets it go, and the test has it reverse at their station.
                if (train.AtStation == stA && !w.Trains.CallsAt(train, stB, 4)) w.Trains.Reverse(train);
                var p = people.PlanOf(0);
                if (people.IsFree(0) && !(p.ByTrain && p.ToStation == stB) && people.StateOf(0) != PeopleSystem.Activity.Sitting && people.PlanVisit(0, bench, Need.Rest, out plan) && plan.ByTrain) people.Direct(0, plan);
                Live(w, 0.5f, true);
                rode |= people.StateOf(0) == PeopleSystem.Activity.Riding;
                arrived = people.AtPlace(0) && people.PlaceOf(0) == bench;
            }
            Check(rode && arrived, "Set for the branch: they board, ride to the branch station and walk on to the bench" + (arrived && rode ? "" : $" ({people.Inspect(0)}; rode {rode}, arrived {arrived}; train at {train.AtStation}, calls at B {w.Trains.CallsAt(train, stB, 4)})"));
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Lives a while, collecting whatever the village tells the player.</summary>
        private static void LiveListening(WonderlandWorld w, float seconds, bool trains, List<string> news)
        {
            int seen = w.People.NewsVersion;
            var log = w.People.NewsLog;
            for (float s = 0f; s < seconds; s += Dt)
            {
                w.Clock.Tick(Dt);
                if (trains) { w.Trains.Step(Dt); w.Freight.Step(Dt); }
                w.People.Step(Dt);
                int fresh = w.People.NewsVersion - seen;
                if (fresh > 0) { seen = w.People.NewsVersion; for (int k = Mathf.Max(0, log.Count - fresh); k < log.Count; k++) if (log[k] != null) news.Add(log[k]); }
            }
        }

        /// <summary>A test decider: everyone free goes to one place (as long as it is not done).</summary>
        private sealed class SendTo : IVillageDecider
        {
            private readonly int house;
            public int Asked;
            public bool Done;
            public SendTo(int house) { this.house = house; }
            public bool Active => !Done;
            public bool Decide(PeopleSystem people, int villager, out Intent plan) { Asked++; return people.PlanVisit(villager, house, Need.Food, out plan); }
        }

        /// <summary>A decider handing out plans that can't be carried out: missing places, taken spots, nowhere.</summary>
        private sealed class Nonsense : IVillageDecider
        {
            private int k;
            public bool Active => true;
            public bool Decide(PeopleSystem people, int villager, out Intent plan)
            {
                plan = Intent.Nothing;
                switch (k++ % 3)
                {
                    case 0: plan.What = Intent.Kind.Spot; plan.House = 999; plan.Spot = 0; break;
                    case 1: plan.What = Intent.Kind.Gather; plan.House = 0; plan.Target = new Vector2(50f, 50f); break;
                    default: plan.What = Intent.Kind.Haul; plan.House = 0; plan.From = 1; break;
                }
                return true;
            }
        }
    }
}
