using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// A day in the village, checked against what the player built: a village outside a railway loop and two homes
        /// inside it. Night sends everyone with a home to bed (windows lit, then dark; rides stopped); mornings fill the
        /// shops, afternoons the fair. Nobody inside the loop sets off for a place they cannot reach - until stations and
        /// a train connect them (they ride across), and a lane with a level crossing lets them walk. A new café draws its
        /// first visitors; removing it sends them elsewhere. Spots are never shared. The clock and needs survive save and
        /// load; a world with no places still lives; 40 people stay within a time budget (editor timings, not Quest).
        /// </summary>
        private static void VillageLifeJourney()
        {
            // The clock: a day of under eight minutes, the small hours three times faster.
            var c = new VillageClock { Hour = 6f };
            float day = 0f; while (c.Hour < 22f) { c.Tick(0.1f); day += 0.1f; }
            float night = 0f; while (c.Hour >= 22f || c.Hour < 6f) { c.Tick(0.1f); night += 0.1f; }
            Check(Mathf.Abs(day - 16f * VillageClock.SecondsPerHour) < 1f && night < day / 3f && c.Day == 1,
                $"The village clock runs a day in {(day + night) / 60f:0.0} minutes ({day:0} s of daylight, {night:0} s of night)");
            Check(Needs.Appeal(Need.Errands, 9f) > Needs.Appeal(Need.Errands, 17f) && Needs.Appeal(Need.Fun, 16f) > Needs.Appeal(Need.Fun, 9f) && Needs.Appeal(Need.Social, 20f) > Needs.Appeal(Need.Social, 10f)
                && Needs.Appeal(Need.Food, 12.5f) > Needs.Appeal(Need.Food, 9f), "The daily rhythm: shops in the morning, lunch at midday, the fair in the afternoon, the square in the evening");

            var w = Scratch("Village life", 160f);
            var t = w.Terrain;
            // A railway loop through the middle; two homes and a bench inside it, the village and a fair outside.
            var rail = w.BuildTrack(Loop(t, 0.42f, 0.34f, 0.97f, false));
            Check(rail.Ok, "A railway loop divides the land" + (rail.Ok ? "" : $" (\"{rail.Error}\")"));
            var build = new List<(int id, float x, float z, float rot)>
            {
                (Catalog.Cottage, -0.1f, 0.06f, 0f), (Catalog.Townhouse, 0.13f, 0.1f, 0f), (Catalog.Bench, 0.0f, -0.14f, 180f),
                (Catalog.Bakery, 0.68f, 0.0f, 90f), (Catalog.Grocer, 0.6f, -0.33f, 60f), (Catalog.Apartments, 0.62f, 0.37f, 120f),
                (Catalog.Cottage, 0.2f, 0.62f, 180f), (Catalog.Thatched, -0.25f, 0.62f, 180f), (Catalog.Fountain, 0.3f, -0.6f, 0f),
                (Catalog.Townhouse, -0.2f, -0.62f, 0f), (Catalog.Carousel, -0.64f, -0.22f, 0f), (Catalog.Playground, -0.64f, 0.26f, 90f),
                (Catalog.Church, -0.55f, -0.56f, 30f),
            };
            int okCount = 0;
            foreach (var (id, x, z, rot) in build)
            {
                string e = Put(w, id, x, z, rot);
                if (e == null) okCount++; else Check(false, $"Place {Catalog.Get(id).Name} at {x:0.00},{z:0.00} (\"{e}\")");
            }
            Check(okCount == build.Count, $"A village goes up around the railway ({okCount}/{build.Count} places, {w.People.People.Count} residents moved in)");
            var people = w.People;
            people.Grid.Complete();
            people.Step(Dt);
            int insideHome = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Cottage && h.z < 0.3f);
            int bakery = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
            var insideDoor = PeopleSystem.DoorStep(w.Buildings.Houses[insideHome]);
            var bakeryAt = new Vector2(w.Buildings.Houses[bakery].x, w.Buildings.Houses[bakery].z);
            Check(!people.Grid.Reachable(insideDoor, bakeryAt + new Vector2(0f, -0.07f)) && people.Grid.Regions >= 2,
                $"The walk grid sees the railway as a divide ({people.Grid.Regions} regions over {people.Grid.Cells} cells, built in {people.Grid.LastBuildMs:0} ms)");
            var inside = new List<int>();
            for (int i = 0; i < people.People.Count; i++)
            {
                int home = people.HomeOf(i);
                if (home >= 0 && new Vector2(w.Buildings.Houses[home].x, w.Buildings.Houses[home].z).magnitude < 0.3f) inside.Add(i);
            }
            int housed = 0; for (int i = 0; i < people.People.Count; i++) if (people.HomeOf(i) >= 0) housed++;
            Check(housed == people.People.Count && inside.Count == 2, $"Every resident knows their home ({housed}/{people.People.Count}; {inside.Count} live inside the loop)");
            int shopkeeper = -1; for (int i = 0; i < people.People.Count; i++) if (people.WorkplaceOf(i) == bakery) shopkeeper = i;
            Check(shopkeeper >= 0, "The baker lives over the bakery");

            // A whole day, from five in the morning.
            w.Clock.Hour = 5f;
            var byPart = new Dictionary<DayPart, Dictionary<Need, int>>();
            foreach (DayPart p in Enum.GetValues(typeof(DayPart))) byPart[p] = new Dictionary<Need, int>();
            bool shared = false, overCapacity = false, unreachable = false, worked = false, workedAtNight = false;
            int asleepAt1 = -1, visibleAt2 = -1;
            string awake = "";
            float litHome = 0f, darkPlace = 9f;
            float ridesAt2 = -1f, ridesAt15 = -1f;
            int startDay = w.Clock.Day;
            var spotUse = new Dictionary<long, int>();
            bool sampled1 = false, sampled2 = false, sampled15 = false, sampled21 = false;
            string why = "";
            while (w.Clock.Day == startDay || w.Clock.Hour < 5f)
            {
                Live(w, 0.5f, false);
                float hour = w.Clock.Hour;
                spotUse.Clear();
                for (int i = 0; i < people.People.Count; i++)
                {
                    var st = people.StateOf(i);
                    worked |= st == PeopleSystem.Activity.Working;
                    if (st == PeopleSystem.Activity.Working && VillageClock.IsNight(hour)) workedAtNight = true;
                    var plan = people.PlanOf(i);
                    // Inside the loop, with no train and no crossing: never off to a place outside.
                    if (inside.Contains(i) && plan.House >= 0 && !plan.ByTrain && !people.Grid.Reachable(people.PositionOf(i), plan.Target)) { unreachable = true; why = people.Describe(i); }
                    if (!people.AtPlace(i) || st == PeopleSystem.Activity.Sleeping) continue;
                    var bp = Catalog.Get(w.Buildings.Houses[people.PlaceOf(i)].variant);
                    if (plan.For != Need.None && plan.For != Need.Rest)
                    {
                        var counts = byPart[VillageClock.PartAt(hour)];
                        counts[plan.For] = (counts.TryGetValue(plan.For, out int n) ? n : 0) + 1;
                    }
                    if (bp.Dwelling && people.SpotOf(i) == Array.FindIndex(bp.Spots, s => s.Kind == SpotKind.Door)) continue;   // a household at its own door
                    if (people.SpotOf(i) < 0) continue;   // working a field, in a crowd: no spot
                    long key = people.PlaceOf(i) * 100L + people.SpotOf(i);
                    spotUse[key] = (spotUse.TryGetValue(key, out int u) ? u : 0) + 1;
                    if (spotUse[key] > 1) shared = true;
                }
                for (int h = 0; h < w.Buildings.Houses.Count; h++)
                    if (people.VisitorsAt(h) > Mathf.Max(1, Catalog.Get(w.Buildings.Houses[h].variant).Spots.Length) + people.ResidentsOf(h)) overCapacity = true;
                if (!sampled1 && hour >= 2f && hour < 5f)
                {
                    sampled1 = true; asleepAt1 = 0;
                    for (int i = 0; i < people.People.Count; i++)
                        if (people.StateOf(i) == PeopleSystem.Activity.Sleeping && people.IsHidden(i)) asleepAt1++;
                        else { awake += people.Describe(i) + "; "; Debug.Log("AWAKE " + people.Inspect(i)); }
                }
                if (!sampled2 && hour >= 2.5f && hour < 5f)
                {
                    sampled2 = true; visibleAt2 = 0; ridesAt2 = w.Buildings.RidesOpen;
                    for (int i = 0; i < people.People.Count; i++) if (!people.IsHidden(i)) visibleAt2++;
                }
                if (!sampled15 && hour >= 15f && hour < 16f) { sampled15 = true; ridesAt15 = w.Buildings.RidesOpen; }
                if (!sampled21 && hour >= 21.2f && hour < 21.6f)
                {
                    sampled21 = true;
                    for (int h = 0; h < w.Buildings.Houses.Count; h++)
                    {
                        float g = w.Buildings.GlowOf(h);
                        if (float.IsNaN(g)) continue;
                        if (people.VisitorsAt(h) > 0 && Catalog.Get(w.Buildings.Houses[h].variant).Category == PlaceCategory.Homes) litHome = Mathf.Max(litHome, g);
                        if (people.VisitorsAt(h) == 0) darkPlace = Mathf.Min(darkPlace, g);
                    }
                }
            }
            int Of(DayPart p, Need n) => byPart[p].TryGetValue(n, out int v) ? v : 0;
            int Total(DayPart p) { int s = 0; foreach (var v in byPart[p].Values) s += v; return s; }
            float Share(DayPart p, Need n) => Of(p, n) / (float)Mathf.Max(1, Total(p));
            Debug.Log("Village day: " + string.Join("; ", Array.ConvertAll((DayPart[])Enum.GetValues(typeof(DayPart)), p => p + " " + string.Join(",", byPart[p].Keys.Select(k => k + "=" + byPart[p][k])))));
            Check(asleepAt1 == people.People.Count, $"At two in the morning everyone is asleep at home ({asleepAt1}/{people.People.Count}{(awake.Length > 0 ? "; awake: " + awake : "")})");
            Check(visibleAt2 == 0, $"The streets are empty at night ({visibleAt2} out)");
            Check(ridesAt2 == 0f && ridesAt15 == 1f, "The fair's rides stop at night and run in the afternoon");
            Check(litHome > 1.4f && darkPlace < 0.5f, $"In the evening windows glow where people are home ({litHome:0.0}) and stay dark where nobody is ({darkPlace:0.0})");
            Check(Share(DayPart.Morning, Need.Errands) > Share(DayPart.Afternoon, Need.Errands) && Of(DayPart.Morning, Need.Errands) > 0,
                $"Mornings fill the shops (errands {Share(DayPart.Morning, Need.Errands) * 100f:0}% of visits in the morning, {Share(DayPart.Afternoon, Need.Errands) * 100f:0}% in the afternoon)");
            Check(Share(DayPart.Afternoon, Need.Fun) > Share(DayPart.Morning, Need.Fun) && Of(DayPart.Afternoon, Need.Fun) > 0,
                $"Afternoons fill the fair (fun {Share(DayPart.Afternoon, Need.Fun) * 100f:0}% of visits in the afternoon, {Share(DayPart.Morning, Need.Fun) * 100f:0}% in the morning)");
            Check(Share(DayPart.Evening, Need.Social) > Share(DayPart.Morning, Need.Social), $"Evenings gather people at the fountain ({Share(DayPart.Evening, Need.Social) * 100f:0}% vs {Share(DayPart.Morning, Need.Social) * 100f:0}% in the morning)");
            Check(worked && !workedAtNight, "Shopkeepers mind their shops in opening hours, not at night");
            Check(!shared && !overCapacity, "Places never hold more people than they have spots, and no spot is shared");
            Check(!unreachable, "Nobody inside the loop sets off for a place they cannot reach" + (unreachable ? $" ({why})" : ""));

            // Before dawn: taking away the bench (listed before the homes) doesn't wake anyone.
            int sleepers = people.Count(PeopleSystem.Activity.Sleeping);
            int bench = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bench);
            var benchAt = w.Buildings.Houses[bench];
            string benchGone = w.EraseAt(new Vector3(benchAt.x, 0.5f, benchAt.z), Vector3.down, new Vector3(benchAt.x, t.SurfaceAt(benchAt.x, benchAt.z), benchAt.z));
            w.Clock.Hour = Mathf.Min(w.Clock.Hour, 5.5f);
            Live(w, 1f, false);
            Check(benchGone == "House removed" && sleepers > 0 && people.Count(PeopleSystem.Activity.Sleeping) == sleepers, $"Removing a building at night leaves the sleepers asleep ({sleepers})");

            // A new café at lunchtime: the village discovers it.
            w.Clock.Hour = 11.5f;
            string cafeError = Put(w, Catalog.Cafe, 0.0f, -0.66f, 0f);
            int cafe = w.Buildings.Houses.Count - 1;
            int newsBefore = people.NewsVersion;
            bool visited = false; float firstAfter = -1f;
            for (float s = 0f; s < 120f && !visited; s += 0.5f)
            {
                Live(w, 0.5f, false);
                if (people.VisitorsAt(cafe) > 0) { visited = true; firstAfter = s; }
            }
            Check(cafeError == null && visited, $"A new café draws visitors (first one after {firstAfter:0} s)" + (cafeError == null ? "" : $" (\"{cafeError}\")"));
            Check(people.NewsVersion > newsBefore && people.News != null && people.News.Contains("café"), $"...and the village notices (\"{people.News}\")");
            // Removing it while people are there: they get up and go elsewhere.
            for (float s = 0f; s < 60f && people.VisitorsAt(cafe) == 0; s += 0.5f) Live(w, 0.5f, false);
            int sitting = people.VisitorsAt(cafe);
            var at = w.Buildings.Houses[cafe];
            string erased = w.EraseAt(new Vector3(at.x, 0.5f, at.z), Vector3.down, new Vector3(at.x, t.SurfaceAt(at.x, at.z), at.z));
            Live(w, 3f, false);
            bool stillCafe = false, stuck = false;
            for (int i = 0; i < people.People.Count; i++)
            {
                if (people.PlanOf(i).Place == "Café") stillCafe = true;
                if (people.AtPlace(i) && people.PlaceOf(i) >= w.Buildings.Houses.Count) stuck = true;
            }
            Live(w, 20f, false);
            int busyAfter = 0; for (int i = 0; i < people.People.Count; i++) if (people.StateOf(i) != PeopleSystem.Activity.Idle) busyAfter++;
            Check(erased == "House removed" && sitting > 0 && !stillCafe && !stuck && busyAfter > 0, $"Removing the café while {sitting} sit there sends them elsewhere ({busyAfter} busy again)");

            // Stations inside and outside the loop and a train: the people inside ride across.
            var inner = w.StationAt(t.PointAt(0.28f, -0.02f), 0);
            var outer = w.StationAt(t.PointAt(-0.56f, -0.02f), 1);
            string stationError = inner.HasValue && outer.HasValue ? w.PlaceHouse(inner.Value) ?? w.PlaceHouse(outer.Value) : "no railway";
            var top = Main(w).Points[Main(w).Points.Count / 4];
            string trainError = PlaceTrainAt(w, top);
            Check(stationError == null && trainError == null && w.Trains.Stations.Count == 2, "Stations inside and outside the loop, and a train"
                + (stationError ?? trainError ?? "") + $" ({w.Trains.Stations.Count} stations)");
            w.Clock.Hour = 9f;
            bool rodeAcross = false, arrivedAcross = false;
            for (float s = 0f; s < 600f && !arrivedAcross; s += 0.5f)
            {
                if (w.Clock.Hour > 17f) w.Clock.Hour = 9f;   // keep it daytime
                Live(w, 0.5f, true);
                foreach (int i in inside)
                {
                    var plan = people.PlanOf(i);
                    if (people.StateOf(i) == PeopleSystem.Activity.Riding) rodeAcross = true;
                    if (people.AtPlace(i) && people.PlaceOf(i) < w.Buildings.Houses.Count && !people.Grid.Reachable(insideDoor, PlaceDoor(w, people.PlaceOf(i)))) arrivedAcross = true;
                }
            }
            Check(rodeAcross, "People inside the loop take the train to the village");
            Check(arrivedAcross, "...get off at the far station and walk on to where they were going");

            // A lane across the railway: a level crossing, and they walk instead.
            w.SetTrainsRunning(false);
            for (int k = 0; k < 72 * 20; k++) w.Trains.Step(Dt);
            string lane = w.BuildPath(Stroke(t, new Vector2(-0.02f, 0.14f), new Vector2(0.0f, 0.34f), new Vector2(0.04f, 0.5f)), PathKind.Lane);
            Live(w, 1f, false);
            people.Grid.Complete();
            Check(lane == null && w.Paths.LevelCrossings.Count > 0 && people.Grid.Reachable(insideDoor, bakeryAt + new Vector2(0f, -0.07f)),
                $"A lane with a level crossing joins the two sides ({people.Grid.Regions} regions)" + (lane == null ? "" : $" (\"{lane}\")"));
            bool walkedAcross = false;
            w.Clock.Hour = 9f;
            for (float s = 0f; s < 400f && !walkedAcross; s += 0.5f)
            {
                if (w.Clock.Hour > 17f) w.Clock.Hour = 9f;
                Live(w, 0.5f, false);
                foreach (int i in inside)
                    if (people.AtPlace(i) && people.PlaceOf(i) < w.Buildings.Houses.Count && !people.PlanOf(i).ByTrain && (PlaceDoor(w, people.PlaceOf(i)) - insideDoor).magnitude > 0.5f) walkedAcross = true;
            }
            Check(walkedAcross, "...and people from inside the loop walk over it to the village");

            // Save and load: the time of day and everyone's needs and homes come back.
            float hourBefore = w.Clock.Hour;
            w.Clock.Mode = ClockMode.Fast;
            float need0 = people.NeedOf(0, Need.Food);
            string json = w.Serialize();
            var copy = new GameObject("Village copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(170f, 0f, 0f);
            copy.Deserialize(json, Materials());
            copy.People.Grid.Complete(); copy.People.Step(Dt);
            int sameHomes = 0; for (int i = 0; i < people.People.Count && i < copy.People.People.Count; i++) if (people.HomeOf(i) == copy.People.HomeOf(i)) sameHomes++;
            Check(Mathf.Abs(copy.Clock.Hour - hourBefore) < 0.01f && copy.Clock.Mode == ClockMode.Fast && Mathf.Abs(copy.People.NeedOf(0, Need.Food) - need0) < 0.01f && sameHomes == people.People.Count,
                $"Save/load keeps the time ({copy.Clock.Label}), the clock speed, needs and homes ({sameHomes}/{people.People.Count})");
            UnityEngine.Object.DestroyImmediate(copy.gameObject);

            // 40 villagers and a busier village: the cost of a frame stays small (editor timing, not Quest).
            foreach (var (id, x, z) in new[] { (Catalog.Cafe, 0.0f, -0.66f), (Catalog.Bench, 0.45f, -0.5f), (Catalog.Well, -0.45f, 0.48f), (Catalog.Picnic, -0.48f, -0.48f), (Catalog.Apartments, -0.75f, 0.0f) })
                Put(w, id, x, z);
            for (int k = 0; people.People.Count < WonderlandWorld.MaxPeople && k < 400; k++)
            {
                float a = k * 2.39996f, r = 0.5f + (k % 7) * 0.04f;
                w.PlacePerson(t.PointAt(Mathf.Cos(a) * r * 1.2f, Mathf.Sin(a) * r), k % PersonFactory.StyleCount, 900 + k);
            }
            w.Clock.Hour = 10f;
            people.Grid.Complete();
            Live(w, 2f, false);
            var watch = new Stopwatch();
            people.WorstDecideMs = 0; people.WorstPlacesMs = 0;
            double worst = 0, sum = 0; int frames = 0, decisions = people.Decisions;
            for (int k = 0; k < 72 * 30; k++)
            {
                w.Clock.Tick(Dt);
                watch.Restart();
                people.Step(Dt);
                double ms = watch.Elapsed.TotalMilliseconds;
                sum += ms; worst = Math.Max(worst, ms); frames++;
            }
            float perSecond = (people.Decisions - decisions) / 30f;
            Check(people.People.Count == WonderlandWorld.MaxPeople && sum / frames < 2.5 && perSecond <= PeopleSystem.MaxDecisionsPerStep * 72,
                $"{people.People.Count} villagers: {sum / frames:0.00} ms per frame on average, worst {worst:0.0} ms, {perSecond:0.0} decisions/s, slowest decision {people.WorstDecideMs:0.0} ms, places directory {people.WorstPlacesMs:0.0} ms (editor timing)");
            // Rebuilding the walk grid after an edit is spread over frames.
            people.WorldChanged();
            int slices = 0; worst = 0;
            while (people.Grid.Busy && slices < 2000) { watch.Restart(); people.Step(Dt); worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds); slices++; }
            Check(!people.Grid.Busy && worst < 12.0, $"After an edit the walk grid rebuilds over {slices} frames ({people.Grid.LastBuildMs:0} ms of work, worst frame {worst:0.0} ms)");
            UnityEngine.Object.DestroyImmediate(w.gameObject);

            // A world with nowhere to go: people still live (wander, sleep under the stars) and nothing breaks.
            var bare = Scratch("Bare", 180f);
            for (int i = 0; i < 3; i++) bare.PlacePerson(bare.Terrain.PointAt(-0.1f + i * 0.1f, 0.05f), i, 40 + i);
            bare.Clock.Hour = 20f;
            bool wandered = false, slept = false, stuckWalking = false;
            var walkingSince = new float[3];
            for (float s = 0f; s < 300f; s += 0.5f)
            {
                Live(bare, 0.5f, false);
                for (int i = 0; i < 3; i++)
                {
                    var st = bare.People.StateOf(i);
                    wandered |= st == PeopleSystem.Activity.Walking;
                    slept |= st == PeopleSystem.Activity.Sleeping && !bare.People.IsHidden(i);
                    walkingSince[i] = st == PeopleSystem.Activity.Walking ? walkingSince[i] + 0.5f : 0f;
                    if (walkingSince[i] > 60f) stuckWalking = true;
                }
            }
            Check(wandered && slept && !stuckWalking, "With no places at all, villagers wander by day and sleep under the stars at night");
            UnityEngine.Object.DestroyImmediate(bare.gameObject);
        }

        private static Vector2 PlaceDoor(WonderlandWorld w, int house) => PeopleSystem.DoorStep(w.Buildings.Houses[house]);

        /// <summary>Lets the village live for a while: the clock, the villagers (and the trains).</summary>
        private static void Live(WonderlandWorld w, float seconds, bool trains)
        {
            for (float s = 0f; s < seconds; s += Dt)
            {
                w.Clock.Tick(Dt);
                if (trains) { w.Trains.Step(Dt); w.Freight.Step(Dt); }   // (freight yards' cranes load the goods trains)
                w.People.Step(Dt);
            }
        }
    }
}
