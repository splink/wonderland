using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The deciding half of the villagers: what each one does next. A directory of places (what each serves, its free
    /// activity spots, which walking region each spot lies in, how new it is) is kept up to date as the world changes;
    /// homes are matched to their residents. A choice scores every option - a place's spot, home, a neighbour's door, a
    /// hike, a stroll, a train trip - as the villager's need for it x how much it appeals at this hour x their own
    /// leaning x nearness x novelty, keeps only what they can actually get to (on foot, or by train between stations),
    /// and picks one with weights that favour the best without always taking it. Places keep opening hours; a friend
    /// there draws more; workers on shift go to work. Night overrides everything: bed.
    /// </summary>
    public sealed partial class PeopleSystem
    {
        private sealed class Place
        {
            public int House;
            public Blueprint Bp;
            public Vector2 Centre;
            public float FirstSeen;              // village hours when it appeared (new places draw the curious)
            public Vector2[] Approach;           // where to walk for each spot
            public int[] Region;                 // walking region of each spot (0: none)
            public bool[] Usable;
            public int Door = -1;                // the door spot, if any
            public int Beds, Residents;          // homes: how many may live here, and do
            public bool Visited;
            public long Key;
            public Vector2 Work;                 // where its worker stands (or where deliveries go)
            public float WorkYaw;
            public int WorkRegion;               // its walking region (the "address" for jobs and produce)
            public Agent Worker;
            public Stock Stock;
        }

        private readonly List<Place> places = new();
        private readonly List<int> stationRegion = new();
        private readonly Dictionary<long, float> firstSeen = new();
        private readonly List<Intent> options = new();
        private readonly List<int> free = new();
        private bool placesDirty = true, placesBuilt, seenAll = true;
        private int pitches, outdoorWalkers;
        private int placesGridVersion = -1, placesBuildingsVersion = -1, placesStations = -1;
        private float lightsTimer;
        private string news;

        /// <summary>Something worth telling the player ("Ida moved into the cottage"); NewsVersion bumps with each.</summary>
        public string News { get => news; private set { news = value; NewsVersion++; NewsLog.Add(value); if (NewsLog.Count > 20) NewsLog.RemoveAt(0); } }

        /// <summary>The last twenty bits of news (several can come in one frame).</summary>
        public readonly List<string> NewsLog = new();
        public int NewsVersion { get; private set; }

        private void RefreshPlaces()
        {
            if (placesDirty || placesGridVersion != Grid.Version || placesBuildingsVersion != world.Buildings.Version || placesStations != stations.Count) RebuildPlaces();
        }

        private static long PlaceKey(HouseInstance h) =>
            ((long)Mathf.RoundToInt(h.x * 2000f) << 34) ^ ((long)Mathf.RoundToInt(h.z * 2000f) << 10) ^ (uint)h.variant;

        public double WorstPlacesMs { get; set; }     // the slowest rebuild of the places directory, diagnostics

        private void RebuildPlaces()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            RebuildPlacesNow();
            WorstPlacesMs = System.Math.Max(WorstPlacesMs, watch.Elapsed.TotalMilliseconds);
        }

        private void RebuildPlacesNow()
        {
            placesDirty = false; placesBuilt = true;
            placeByKey.Clear();
            foreach (var a in agents) a.DayOf = -1;   // the world changed: plans are made again (at the next choice)
            placesGridVersion = Grid.Version; placesBuildingsVersion = world.Buildings.Version; placesStations = stations.Count;
            var visited = new HashSet<long>();
            foreach (var p in places) if (p.Visited && p.House < world.Buildings.Houses.Count) visited.Add(PlaceKey(world.Buildings.Houses[p.House]));
            places.Clear();
            var houses = world.Buildings.Houses;
            // Buildings removed earlier in the list shift the others' indices: follow each place by where it stands.
            var indexAt = new Dictionary<Vector2, int>();
            for (int i = 0; i < houses.Count; i++) indexAt[new Vector2(houses[i].x, houses[i].z)] = i;
            foreach (var a in agents)
                if (a.SpotHouse >= 0 && indexAt.TryGetValue(a.SpotHousePos, out int now))
                {
                    if (a.Plan.House == a.SpotHouse) a.Plan.House = now;
                    a.SpotHouse = now;
                }
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                var bp = Catalog.Get(h.variant);
                var place = new Place
                {
                    House = i, Bp = bp, Centre = new Vector2(h.x, h.z),
                    Approach = new Vector2[bp.Spots.Length], Region = new int[bp.Spots.Length], Usable = new bool[bp.Spots.Length],
                    Beds = bp.Dwelling ? bp.Residents + 2 : 0,   // room for a family
                };
                long key = PlaceKey(h);
                place.Key = key;
                place.Stock = StockFor(key, bp);
                if (!firstSeen.TryGetValue(key, out place.FirstSeen)) firstSeen[key] = place.FirstSeen = seenAll ? -1000f : Clock.Total;
                place.Visited = visited.Contains(key);
                for (int k = 0; k < bp.Spots.Length; k++)
                {
                    var sp = bp.Spots[k];
                    if (sp.Kind == SpotKind.Door && place.Door < 0) place.Door = k;
                    var pos = world.Buildings.SpotPosition(i, k, out _);
                    var p2 = new Vector2(pos.x, pos.z);
                    var approach = p2;
                    if (sp.Kind == SpotKind.Ride || sp.At.y > 0f || !Walkable(p2))
                    {
                        // Spots on a ride, up a tower or inside a footprint: walk to the edge of the place first.
                        var dir = p2 - place.Centre;
                        if (dir.sqrMagnitude < 1e-8f) dir = (Vector2)(Quaternion.Euler(0f, 0f, -h.rotation) * Vector2.down);
                        approach = place.Centre + dir.normalized * (Mathf.Max(bp.Half.x, bp.Half.y) * 0.95f + 0.014f);
                    }
                    place.Approach[k] = approach;
                    place.Usable[k] = Walkable(approach);
                    place.Region[k] = Grid.RegionAt(approach);
                }
                SetWorkPoint(place, h);
                places.Add(place);
                placeByKey[place.Key] = place;
            }
            bool quiet = seenAll;
            seenAll = false;
            pitches = places.FindAll(p => p.Bp.Id == Catalog.Football).Count;
            // Who lives where: a villager whose home is a house's doorstep lives there, while it has beds.
            foreach (var a in agents)
            {
                a.HomeHouse = -1; a.Workplace = -1;
                Place best = null; float bestD = 0.035f;
                foreach (var p in places)
                {
                    if (p.Beds == 0 || p.Residents >= p.Beds || p.Door < 0) continue;
                    float d = (p.Approach[p.Door] - a.Home).magnitude;
                    if (d < bestD) { bestD = d; best = p; }
                }
                if (best == null) continue;
                best.Residents++;
                a.HomeHouse = best.House;
            }
            // Whoever lives over a shop keeps it: a grown-up.
            foreach (var a in agents) SetChild(a, a.SeedChild && !(a.HomeHouse >= 0 && places[a.HomeHouse].Bp.Job != null));
            stationRegion.Clear(); stationPlace.Clear(); regionStation.Clear(); regionYard.Clear();
            foreach (var st in stations)
            {
                int region = Grid.RegionAt(PlatformSpot(st, 0f));
                stationRegion.Add(region);
                var at = places.Find(p => p.Centre == st.House);
                (at != null && at.Bp.Id == Catalog.FreightYard ? regionYard : regionStation).Add(region);   // (passengers from stations, goods from freight yards)
                if (at != null) at.WorkRegion = region;   // the station's goods go from the platform
                stationPlace.Add(at);
            }
            AssignJobs(quiet);
        }

        /// <summary>A visitor at a place that has closed (they wrap up and go).</summary>
        private bool Closed(Agent a) => a.Plan.What == Intent.Kind.Spot && a.Plan.For != Need.Work && a.SpotHouse >= 0 && a.SpotHouse < world.Buildings.Houses.Count
            && !Catalog.Get(world.Buildings.Houses[a.SpotHouse].variant).OpenAt(Clock.Hour);

        private Vector2 DoorOf(int house)
        {
            if (house < places.Count && places[house].House == house && places[house].Door >= 0) return places[house].Approach[places[house].Door];
            return DoorStep(world.Buildings.Houses[house]);
        }

        /// <summary>From bedtime to waking. Night owls' bedtimes are written past 24 (half past midnight: 24.5).</summary>
        private static bool Bedtime(Agent a, float hour)
        {
            float h = hour < 12f ? hour + 24f : hour;
            return h >= a.BedHour && h < a.WakeHour + 24f;
        }

        /// <summary>The hour as it feels to them: night owls' evenings run late (their supper, their evening company).</summary>
        private static float Feel(Agent a, float hour) => a.BedHour > 23.5f && hour > 15f ? hour - (a.BedHour - 22f) : hour;

        /// <summary>The village clock after walking <paramref name="metres"/> from <paramref name="hour"/> (the small hours pass three times faster).</summary>
        private static float After(float hour, float metres, float pace = 1f)
        {
            float day = metres / (Speed * pace) / VillageClock.SecondsPerHour;   // hours at the daytime rate
            float h = hour < 12f ? hour + 24f : hour, fast = 23.5f;
            if (h >= fast) return h + day * VillageClock.NightSpeed;
            float end = h + day;
            return end <= fast ? end : fast + (end - fast) * VillageClock.NightSpeed;
        }

        /// <summary>In the evening: whether there is time to walk <paramref name="metres"/> (there and home) and stay a while before bed.</summary>
        private static bool BackBeforeBed(Agent a, float hour, float metres) => hour < 15f && hour >= 5f || After(hour, metres * 1.3f) + 0.5f < a.BedHour;

        /// <summary>Time to set off home so as to be there by bedtime (from wherever they are).</summary>
        private bool HeadHome(Agent a, float hour)
        {
            if ((hour < 17f && hour >= 5f) || a.HomeHouse < 0 || a.HomeHouse >= places.Count) return false;
            return After(hour, (DoorOf(a.HomeHouse) - a.Position).magnitude * 1.25f, 1.35f) >= a.BedHour;
        }

        private static float Weight(SpotKind k) => k switch
        {
            SpotKind.Door => 0.8f, SpotKind.Sit => 1.3f, SpotKind.Gather => 1.4f, SpotKind.Play => 1.1f,
            SpotKind.Ride => 1.7f, SpotKind.Watch => 1.0f, _ => 1.4f,
        };

        private bool PassengerTrains
        {
            get { foreach (var t in world.Trains.Trains) if (t.Running && !TrainSystem.Freight(t.Kind)) return true; return false; }
        }

        /// <summary>A train journey between two walking regions: the nearest station on each side.</summary>
        private bool TrainRoute(int from, int to, Vector2 here, Vector2 there, out int board, out int alight)
        {
            board = alight = -1;
            if (from <= 0 || to <= 0 || stations.Count < 2 || !PassengerTrains) return false;
            float bd = float.MaxValue, ad = float.MaxValue;
            for (int i = 0; i < stations.Count && i < stationRegion.Count; i++)
            {
                var spot = PlatformSpot(stations[i], 0f);
                if (stationRegion[i] == from) { float d = (spot - here).magnitude; if (d < bd) { bd = d; board = i; } }
            }
            for (int i = 0; i < stations.Count && i < stationRegion.Count; i++)
            {
                var spot = PlatformSpot(stations[i], 0f);
                if (stationRegion[i] == to && i != board) { float d = (spot - there).magnitude; if (d < ad) { ad = d; alight = i; } }
            }
            return board >= 0 && alight >= 0;
        }

        private float Novelty(Place p) => 1f + 1.2f * Mathf.Clamp01(1f - (Clock.Total - p.FirstSeen) / 12f);

        private static float Near(float d, float scale) => 1f / (1f + (d / scale) * (d / scale));

        /// <summary>What this villager does next.</summary>
        private Intent Choose(Agent a)
        {
            float hour = Clock.Hour;
            var part = Clock.Part;
            int mine = Grid.RegionAt(a.Position);
            if (Bedtime(a, hour) || HeadHome(a, hour)) return Bed(a, mine);
            float feel = Feel(a, hour);
            var slot = Now(a, hour);
            options.Clear();
            AddPlanOption(a, mine, hour);
            float Urge(Need n) => 0.15f + a.Need[(int)n];
            // Somewhere to live first.
            if (a.HomeHouse < 0) { var move = MoveIn(a, mine); if (move.What != Intent.Kind.None) options.Add(move); }
            // The places within reach that serve something wanted now.
            foreach (var p in places)
            {
                var need = p.Bp.Serves;
                if (need == Need.None || (need == Need.Rest && p.Bp.Dwelling) || p.House == a.Workplace || Avoided(a, p)) continue;
                float appeal = p.Bp.Rhythm != null ? Needs.Appeal(p.Bp, hour) : Needs.Appeal(need, feel);
                bool nightOut = p.Bp.Rhythm != null && a.BedHour > 23.5f && hour > 19f;
                if (nightOut) appeal *= 3f;   // the night owls' evening out (and they'll go further for it)
                if (appeal < 0.02f || !p.Bp.OpenAt(hour) || !p.Bp.OpenAt(hour + 0.3f)) continue;   // closed, or about to close
                if (p.Bp.Goods == Goods.Sells && p.Stock.Have < SaleSize) appeal *= 0.45f;          // word gets round: bare shelves
                float d = (p.Centre - a.Position).magnitude, reach = p.Bp.Leisure ? 1.4f : 0.9f;
                if (d > reach * 2.5f) continue;
                int spot = FreeSpot(p, a);
                if (spot < 0) continue;
                bool walk = Grid.SameRegion(mine, p.Region[spot]);
                int board = -1, alight = -1;
                if (!walk)
                {
                    if (!TrainRoute(mine, p.Region[spot], a.Position, p.Approach[spot], out board, out alight)) continue;
                    d = (PlatformSpot(stations[board], 0f) - a.Position).magnitude + (PlatformSpot(stations[alight], 0f) - p.Approach[spot]).magnitude + 0.2f;
                }
                else if (d > reach) continue;
                if (!BackBeforeBed(a, hour, d + (p.Approach[spot] - a.Home).magnitude)) continue;   // too far for this late
                float score = Urge(need) * appeal * Needs.Affinity(a.Seed, need) * Weight(p.Bp.Spots[spot].Kind) * Novelty(p) * Near(d, nightOut ? 0.8f : 0.35f) * (walk ? 1f : 0.55f);
                if (a.Friends.Count > 0 && FriendAt(a, p.House) != null) score *= 1.8f;   // a friend is there
                if (p.Bp.Kids) score *= a.Child ? 2.4f : 0.5f;                              // children love the playground and the pitch
                else if (a.Child && (p.Bp.Id == Catalog.Disco || p.Bp.Rhythm != null && hour > 19f)) continue;
                options.Add(new Intent { What = Intent.Kind.Spot, For = need, House = p.House, Spot = spot, Target = p.Approach[spot], FromStation = board, ToStation = alight, Score = score, Place = p.Bp.Name });
            }
            // Work: on shift, mind the place (or carry its produce where it is wanted).
            AddWorkOptions(a, mine, hour, Urge(Need.Work));
            // Home for a rest.
            if (a.HomeHouse >= 0 && a.HomeHouse < places.Count)
            {
                var home = Home(a, mine, Intent.Kind.Home);
                if (home.What != Intent.Kind.None)
                {
                    home.Score = Urge(Need.Rest) * Needs.Appeal(Need.Rest, feel) * 0.8f;
                    if (slot is { Kind: SlotKind.Home }) home.Score = Mathf.Max(home.Score, 2.5f);   // the plan says: home for a while
                    else home.Score = Mathf.Max(home.Score, 0.12f);   // nothing better to do: home, not wandering about
                    options.Add(home);
                }
            }
            // A neighbour's door for a chat - a friend's, if one is at home.
            float social = Needs.Appeal(Need.Social, feel);
            var friend = social > 0.05f ? FriendIn(a) : null;
            if (friend != null && friend.HomeHouse < places.Count)
            {
                var p = places[friend.HomeHouse];
                if (p.Door >= 0 && Grid.SameRegion(mine, p.Region[p.Door]) && (p.Centre - a.Position).magnitude < 0.9f)
                    options.Add(new Intent { What = Intent.Kind.Neighbour, For = Need.Social, House = p.House, Spot = p.Door, Target = p.Approach[p.Door], FromStation = -1, ToStation = -1, From = -1,
                        Score = Urge(Need.Social) * social * Needs.Affinity(a.Seed, Need.Social) * 1.1f, Place = p.Bp.Name });
            }
            else if (social > 0.05f && places.Count > 0)
            {
                var p = places[a.Rng.Range(0, places.Count)];
                if (p.Bp.Dwelling && p.House != a.HomeHouse && p.Door >= 0 && (p.Centre - a.Position).magnitude < 0.5f && Grid.SameRegion(mine, p.Region[p.Door]))
                    options.Add(new Intent { What = Intent.Kind.Neighbour, For = Need.Social, House = p.House, Spot = p.Door, Target = p.Approach[p.Door], FromStation = -1, ToStation = -1,
                        Score = Urge(Need.Social) * social * Needs.Affinity(a.Seed, Need.Social) * 0.45f, Place = p.Bp.Name });
            }
            float sights = Needs.Appeal(Need.Sights, hour) * Needs.Affinity(a.Seed, Need.Sights) * Urge(Need.Sights);
            // A hike along a trail nearby; a stroll along the roads and paths.
            if (sights > 0.01f && world.Paths.Lines.Count > 0 && outdoorWalkers < Mathf.Max(3, agents.Count / 12))   // a few walkers out on the trails, not crowds
            {
                foreach (var l in world.Paths.Lines)
                {
                    float far = l.kind == (int)PathKind.Trail ? PathSystem.Distance(l, a.Home) : float.MaxValue;
                    if (far >= 0.45f) continue;
                    options.Add(new Intent { What = Intent.Kind.Hike, For = Need.Sights, House = -1, Spot = -1, FromStation = -1, ToStation = -1, Score = sights * (far < 0.2f ? 1.3f : 0.9f), Place = "trail" });   // living by a trail: a hiker
                    break;
                }
                var stroll = world.Paths.StrollTarget(a.Home, 0.45f, ref a.Rng);
                if (stroll.HasValue && Walkable(stroll.Value) && Grid.Reachable(a.Position, stroll.Value))
                    options.Add(new Intent { What = Intent.Kind.Stroll, For = Need.Sights, House = -1, Spot = -1, Target = stroll.Value, FromStation = -1, ToStation = -1, Score = sights * 0.35f });
            }
            // A ride on the train, for its own sake.
            if (sights > 0.01f && stations.Count >= 1 && PassengerTrains)
            {
                int board = -1; float bd = 1.2f;
                for (int i = 0; i < stations.Count && i < stationRegion.Count; i++)
                {
                    float d = (PlatformSpot(stations[i], 0f) - a.Position).magnitude;
                    if (d < bd && Grid.SameRegion(mine, stationRegion[i])) { bd = d; board = i; }
                }
                if (board >= 0) options.Add(new Intent { What = Intent.Kind.TrainTrip, For = Need.Sights, House = -1, Spot = -1, FromStation = board, ToStation = -1, Score = sights * 0.6f * Near(bd, 0.6f), Place = "train" });
            }
            // Only someone with no home to go to wanders about.
            var wander = a.HomeHouse < 0 ? WanderTarget(a, mine) : null;
            if (wander.HasValue) options.Add(new Intent { What = Intent.Kind.Wander, For = Need.None, House = -1, Spot = -1, Target = wander.Value, FromStation = -1, ToStation = -1, Score = 0.04f });
            return Pick(a);
        }

        private Intent Pick(Agent a)
        {
            if (options.Count == 0) return Intent.Nothing;
            // Weights favour the best options (score squared) without always taking the very best.
            float total = 0f;
            foreach (var o in options) total += o.Score * o.Score;
            float pick = a.Rng.Value * total;
            foreach (var o in options)
            {
                pick -= o.Score * o.Score;
                if (pick <= 0f) return o;
            }
            return options[^1];
        }

        private int FreeSpot(Place p, Agent a)
        {
            free.Clear();
            for (int k = 0; k < p.Approach.Length; k++)
                if (p.Usable[k] && !Taken(p.House, k) && !(k == p.Door && p.Bp.Serves == Need.Rest)) free.Add(k);   // not into people's homes
            return free.Count == 0 ? -1 : free[a.Rng.Range(0, free.Count)];
        }

        private Intent Home(Agent a, int mine, Intent.Kind kind)
        {
            var p = places[a.HomeHouse];
            if (p.Door < 0) return Intent.Nothing;
            var plan = new Intent { What = kind, For = Need.Rest, House = p.House, Spot = p.Door, Target = p.Approach[p.Door], FromStation = -1, ToStation = -1, Place = p.Bp.Name };
            if (Grid.SameRegion(mine, p.Region[p.Door])) return plan;
            if (TrainRoute(mine, p.Region[p.Door], a.Position, plan.Target, out plan.FromStation, out plan.ToStation)) return plan;
            return Intent.Nothing;
        }

        /// <summary>Bedtime: home if they can get there; otherwise a bench, a log or the campfire; otherwise right here.</summary>
        private Intent Bed(Agent a, int mine)
        {
            if (a.HomeHouse >= 0 && a.HomeHouse < places.Count)
            {
                var home = Home(a, mine, Intent.Kind.Sleep);
                if (home.What != Intent.Kind.None) return home;
            }
            Place best = null; int bestSpot = -1; float bestD = 0.5f;
            foreach (var p in places)
            {
                if (p.Bp.Dwelling || (p.Bp.Serves != Need.Rest && p.Bp.Id != Catalog.Campsite)) continue;
                float d = (p.Centre - a.Position).magnitude;
                if (d >= bestD) continue;
                for (int k = 0; k < p.Approach.Length; k++)
                    if (p.Usable[k] && p.Bp.Spots[k].Kind == SpotKind.Sit && !Taken(p.House, k) && Grid.SameRegion(mine, p.Region[k])) { best = p; bestSpot = k; bestD = d; break; }
            }
            if (best != null) return new Intent { What = Intent.Kind.Sleep, For = Need.Rest, House = best.House, Spot = bestSpot, Target = best.Approach[bestSpot], FromStation = -1, ToStation = -1, Place = best.Bp.Name };
            return new Intent { What = Intent.Kind.Sleep, For = Need.Rest, House = -1, Spot = -1, Target = a.Position, FromStation = -1, ToStation = -1 };
        }

        /// <summary>No home: the nearest house with a free bed they can reach.</summary>
        private Intent MoveIn(Agent a, int mine)
        {
            Place best = null; float bestD = 1f;   // a house with a spare bed within a walk; further out, they stay a camper
            foreach (var p in places)
            {
                if (p.Beds == 0 || p.Residents >= p.Beds || p.Door < 0 || !p.Usable[p.Door] || !Grid.SameRegion(mine, p.Region[p.Door])) continue;
                float d = (p.Approach[p.Door] - a.Home).magnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            if (best == null) return Intent.Nothing;
            best.Residents++;   // spoken for
            return new Intent { What = Intent.Kind.MoveIn, For = Need.Rest, House = best.House, Spot = best.Door, Target = best.Approach[best.Door], FromStation = -1, ToStation = -1, Score = 3f, Place = best.Bp.Name };
        }

        private Vector2? WanderTarget(Agent a, int mine)
        {
            for (int k = 0; k < 6; k++)
            {
                var t = a.Home + a.Rng.InsideUnitCircle() * 0.12f;   // their own patch, not the whole countryside
                if ((t - a.Position).magnitude < 0.03f || !Walkable(t) || !Grid.SameRegion(mine, Grid.RegionAt(t))) continue;
                return t;
            }
            return null;
        }

        private void NoteVisit(int house, Agent a)
        {
            if (house >= places.Count || places[house].House != house) return;
            var p = places[house];
            if (p.Visited) return;
            p.Visited = true;
            if (Clock.Total - p.FirstSeen < 12f) News = $"{a.Name} is the first to visit the new {p.Bp.Name.ToLower()}";
        }

        // ------------------------------------------------------------------ lights

        /// <summary>
        /// Evening and night: windows glow where someone is in (home, or minding the shop), and go dark where nobody is;
        /// after midnight, sleeping households dim to a night light. By day every building keeps its own look.
        /// </summary>
        private void UpdateLights()
        {
            var b = world.Buildings;
            world.Buildings.RidesOpen = Catalog.Get(Catalog.Carousel).OpenAt(Clock.Hour) ? 1f : 0f;
            outdoorWalkers = 0;
            foreach (var a in agents) if (a.State == Activity.Walking && (a.Hiking || a.Plan.What == Intent.Kind.Stroll)) outdoorWalkers++;
            world.Buildings.BalloonsFly = Catalog.Get(Catalog.Balloon).OpenAt(Clock.Hour);
            float night = 1f - Clock.Daylight;
            if (night < 0.05f) { for (int i = 0; i < b.Houses.Count; i++) b.SetGlow(i, float.NaN); return; }
            bool late = Clock.Hour < 5f;
            for (int i = 0; i < b.Houses.Count; i++)
            {
                var bp = Catalog.Get(b.Houses[i].variant);
                if (bp.Small || (!bp.Dwelling && !System.Array.Exists(bp.Spots, s => s.Kind == SpotKind.Door))) { b.SetGlow(i, float.NaN); continue; }
                int awake = 0, asleep = 0;
                foreach (var a in agents)
                {
                    if (a.SpotHouse != i) continue;
                    if (a.State == Activity.Inside || a.State == Activity.Working) awake++;
                    else if (a.State == Activity.Sleeping && a.Hidden) asleep++;
                }
                float lit = awake > 0 ? 1.9f : asleep > 0 ? (late ? 0.1f : 1.5f) : 0f;   // lights out after midnight
                if (bp.Rhythm != null && bp.OpenAt(Clock.Hour)) lit = Mathf.Max(lit, 1.9f);   // the disco's neon, the restaurant's lanterns
                b.SetGlow(i, Mathf.Lerp(1.3f, lit, night));
            }
        }

        // ------------------------------------------------------------------ what is going on (tests, the palette, logs)

        public string NameOf(int i) => agents[i].Name;
        public Intent PlanOf(int i) => agents[i].Plan;
        public int HomeOf(int i) => agents[i].HomeHouse;
        public int WorkplaceOf(int i) => agents[i].Workplace;
        public float NeedOf(int i, Need n) => agents[i].Need[(int)n];
        public int PlaceOf(int i) => agents[i].SpotHouse;
        public int SpotOf(int i) => agents[i].SpotIndex;
        public int Count(Activity s) { int n = 0; foreach (var a in agents) if (a.State == s) n++; return n; }

        /// <summary>Whether they are at a place doing what it is for (not on the way to it).</summary>
        public bool AtPlace(int i) => agents[i].SpotHouse >= 0 && agents[i].State != Activity.Walking && agents[i].State != Activity.Idle && agents[i].State != Activity.Waving && agents[i].State != Activity.Waiting && agents[i].State != Activity.Riding;

        /// <summary>People at (or in) a building right now.</summary>
        /// <summary>Villagers who have got on a bus at a stop (tests).</summary>
        public int BusRiders { get; private set; }

        /// <summary>A bus has drawn up at the stop at <paramref name="stop"/>: everyone waiting there gets on (and goes on their way).</summary>
        public void BoardBus(Vector2 stop, float within)
        {
            int house = world.Buildings.Houses.FindIndex(h => h.variant == Catalog.BusStop && (new Vector2(h.x, h.z) - stop).magnitude < within);
            if (house < 0) return;
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                if (a.SpotHouse != house || !AtPlace(i) || a.State == Activity.Working) continue;
                BusRiders++;
                Stop(a);
            }
        }

                public int VisitorsAt(int house) { int n = 0; for (int i = 0; i < agents.Count; i++) if (agents[i].SpotHouse == house && AtPlace(i) && agents[i].State != Activity.Working) n++; return n; }

        /// <summary>People heading for a building right now.</summary>
        public int HeadingTo(int house) { int n = 0; foreach (var a in agents) if (a.SpotHouse == house && (a.State == Activity.Walking || a.State == Activity.Waiting || a.State == Activity.Riding)) n++; return n; }

        public int ResidentsOf(int house) { int n = 0; foreach (var a in agents) if (a.HomeHouse == house) n++; return n; }

        /// <summary>One line on what a villager is up to and why: "Greta - browsing at the bakery (the morning shopping)".</summary>
        public string Describe(int i)
        {
            var a = agents[i];
            string place = a.Plan.Place != null ? "the " + a.Plan.Place.ToLower() : "a neighbour's";
            string why = a.Plan.For != Need.None ? $" ({Needs.Reason(a.Plan.For, Clock.Part)})" : "";
            string doing = a.State switch
            {
                Activity.Sleeping => a.Hidden ? "asleep at home" : a.SpotHouse >= 0 ? $"dozing on {place}" : "asleep under the stars",
                Activity.Inside => a.Plan.What == Intent.Kind.Home || a.Plan.What == Intent.Kind.MoveIn ? "at home" : $"inside {place}{why}",
                Activity.Working => a.Child ? "at school" : WorkingLine(a, place),
                Activity.Unwell => "feeling faint - help is on its way",
                Activity.Walking => a.Plan.What switch
                {
                    Intent.Kind.Spot => $"on the way to {place}{why}",
                    Intent.Kind.Home => "heading home for a rest",
                    Intent.Kind.Sleep => a.Plan.House >= 0 && Catalog.Get(world.Buildings.Houses[Mathf.Min(a.Plan.House, world.Buildings.Houses.Count - 1)].variant).Dwelling ? "heading home to bed" : "looking for somewhere to sleep",
                    Intent.Kind.MoveIn => $"moving into {place}",
                    Intent.Kind.Neighbour => "off to see a neighbour",
                    Intent.Kind.Stroll => "out for a stroll",
                    Intent.Kind.Hike => "hiking the trail",
                    Intent.Kind.TrainTrip => "off to catch a train for the ride",
                    Intent.Kind.Work => $"on the way to work at {place}",
                    Intent.Kind.Haul => a.Carrying > 0 ? $"carrying {a.CargoName} to {place}" : $"off to fetch produce from the {PlaceName(a.Plan.From)}",
                    Intent.Kind.Gather => $"off to join everyone at {place}",
                    _ => a.Hiking ? "hiking the trail" : "wandering about",
                } + (a.Plan.ByTrain && a.Leg == Leg.ToPlatform ? ", by train" : a.Leg == Leg.ToTaxi ? " - walking to the kerb for a taxi" : ""),
                Activity.Hailing => $"waiting at the kerb for a taxi to {place}",
                Activity.InTaxi => $"in a taxi to {place}",
                Activity.Waiting => a.Changing ? $"changing trains for {place}" : a.Plan.ByTrain ? $"waiting for the train to {place}" : "waiting for a train",
                Activity.Riding => a.Plan.ByTrain ? $"on the train to {place}" : "riding the train",
                Activity.Sitting => $"sitting at {place}{Company(a)}{why}",
                Activity.Gathering => a.Plan.What == Intent.Kind.Gather ? $"in the crowd at {place}{Company(a)}" : $"chatting at {place}{Company(a)}",
                Activity.Browsing => $"browsing at {place}{why}" + (a.Bare ? " - the shelves are bare" : ""),
                Activity.Watching => $"taking in {place}{Company(a)}",
                Activity.Playing => $"playing at {place}{Company(a)}",
                Activity.OnRide when a.SpotHouse >= 0 && a.SpotHouse < world.Buildings.Houses.Count && world.Buildings.Houses[a.SpotHouse].variant == Catalog.Jetty
                    => a.Aboard ? "on a boat trip round the lake" : "waiting for a boat at the landing stage",
                Activity.OnRide => $"riding {place}",
                Activity.Chatting => ChattingLine(a),
                Activity.Waving => "waving at the train",
                _ => "looking about",
            };
            string home = a.HomeHouse >= 0 && a.HomeHouse < world.Buildings.Houses.Count ? "" : " - no home yet";
            return $"{a.Name}{(a.Child ? " (a child)" : "")} - {doing}{home}";
        }

        private string WorkingLine(Agent a, string place)
        {
            var p = PlaceAt(a.SpotHouse);
            if (p == null) return $"at work at {place}";
            if (p.Bp.WorksIndoors) return $"at work in {place} (the {p.Bp.Job})";
            if (p.Bp.Tractors > 0 && a.Hidden) return $"driving the tractor up and down {place}";
            if (p.Bp.Farm) return $"tending {place}";
            return $"minding {place}";
        }

        private string ChattingLine(Agent a)
        {
            foreach (var f in a.Friends)
                if (f.State == Activity.Chatting && f.SpotHouse == a.SpotHouse && (f.Position - a.Position).magnitude < 0.03f) return $"chatting with {f.Name} at the door";
            return "chatting at a neighbour's door";
        }

        private string PlaceName(int house) => house >= 0 && house < world.Buildings.Houses.Count ? Catalog.Get(world.Buildings.Houses[house].variant).Name.ToLower() : "farm";

        /// <summary>One line on a place: who is there, and what draws people to it when.</summary>
        public string DescribePlace(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count) return null;
            var bp = Catalog.Get(world.Buildings.Houses[house].variant);
            if (bp.Dwelling && bp.Category == PlaceCategory.Homes)
            {
                int living = ResidentsOf(house);
                return living == 0 ? $"{bp.Name}: nobody lives here yet" : $"{bp.Name}: home to {Residents(house)}";
            }
            var place = PlaceAt(house);
            var extra = new List<string>();
            if (bp.Job != null && place != null)
                extra.Add(place.Worker == null ? $"no {bp.Job} yet" : AtWork(place.Worker, house) ? $"{place.Worker.Name} the {bp.Job} is at work" : $"{place.Worker.Name} is the {bp.Job}");
            string supply = place != null ? StockLine(place) : null;
            if (supply != null) extra.Add(supply);
            string tail = extra.Count > 0 ? " · " + string.Join(" · ", extra) : "";
            if (bp.Opens > 0f || bp.Closes < 24f)
            {
                if (!bp.OpenAt(Clock.Hour)) return $"{bp.Name}: closed, opens at {HourLabel(bp.Opens)}{tail}";
                tail += $" · open till {HourLabel(bp.Closes)}";
            }
            if (bp.Serves == Need.None) return bp.Name + (tail.Length > 0 ? ":" + tail.Substring(2) : "");
            int here = VisitorsAt(house), coming = HeadingTo(house);
            float peak = 0f, best = 0f;
            for (float h = 6f; h < 24f; h += 0.5f) { float v = Needs.Appeal(bp, h) * (bp.OpenAt(h) ? 1f : 0f); if (v > best) { best = v; peak = h; } }
            string crowd = here == 0 ? "nobody here" : here == 1 ? "1 visitor" : $"{here} visitors";
            if (coming > 0) crowd += $", {coming} on the way";
            return $"{bp.Name}: {crowd} - people come for {Needs.Reason(bp.Serves, VillageClock.PartAt(peak))}, busiest in the {VillageClock.PartName(VillageClock.PartAt(peak))}{tail}";
        }

        private string Residents(int house)
        {
            var names = new List<string>();
            foreach (var a in agents) if (a.HomeHouse == house) names.Add(a.Name);
            return names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[^1];
        }

        /// <summary>Internals of one villager, for test diagnostics.</summary>
        public string Inspect(int i)
        {
            var a = agents[i];
            return $"{a.Name}: {a.State} timer {a.Timer:0.0} plan {a.Plan.What}/{a.Plan.For} house {a.Plan.House} target {a.Plan.Target} leg {a.Leg} pos {a.Position} home {a.Home} homeHouse {a.HomeHouse} work {a.Workplace} wp {a.Waypoints.Count} bed {a.BedHour:0.0} wake {a.WakeHour:0.0} carrying {a.Carrying} hidden {a.Hidden} day {a.DayOf}: {string.Join(" / ", a.Day.ConvertAll(sl => SlotLine(a, sl) + (sl.Kind == SlotKind.Visit ? "" : "")))}";
        }

        /// <summary>A census for the performance log: "24 people: 9 asleep, 6 out and about...".</summary>
        public string Census()
        {
            int asleep = Count(Activity.Sleeping), walking = Count(Activity.Walking), busy = 0;
            for (int i = 0; i < agents.Count; i++) if (AtPlace(i) && agents[i].State != Activity.Sleeping) busy++;
            int working = Count(Activity.Working), friends = 0;
            foreach (var a in agents) friends += a.Friends.Count;
            int homeless = 0; foreach (var a in agents) if (a.HomeHouse < 0) homeless++;
            return $"{agents.Count} people ({asleep} asleep, {walking} walking, {busy} at places, {working} at work, {homeless} without a home, {friends / 2} friendships), {Clock.Label}";
        }
    }
}
