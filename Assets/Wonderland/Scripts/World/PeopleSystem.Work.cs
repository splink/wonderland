using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>A place's produce, as saved: crates on the shelves (or ready on the farm, or arrived at the station) and
    /// crates on a platform waiting for the goods train.</summary>
    [System.Serializable]
    public struct SupplyInstance
    {
        public float x, z;
        public int variant;
        public float have, waiting;
    }

    /// <summary>
    /// The working day. Places with a job (the catalogue says which: baker, farmer, teacher, station master...) get one
    /// worker each, the nearest resident who can get there (the baker lives over the bakery; others walk, or commute by
    /// train), and that worker minds it during their shift. Opening hours decide when visitors come.
    /// Produce is one more building block, a few crates kept per place: farms grow it while their farmer works, shops
    /// and the café sell a little with every visit (bare shelves draw fewer people), and workers carry it by hand -
    /// the farmer to the shops, the shopkeeper fetching from a farm - within their side of the line. Across it, goods
    /// trains do: crates carried to a station leave with the next goods train and are unloaded where shops want them.
    /// A van from beyond the village tops shops up slowly to a couple of crates, so a village without farms still has
    /// bread, just less of it; full shelves need the village's own farms and works (and their lorries).
    /// </summary>
    public sealed partial class PeopleSystem
    {
        public const float StockCap = 6f;
        public const float GrowRate = 0.8f;        // crates per hour of work on a farm
        public const float SaleSize = 0.1f;        // of a crate, per shopping trip or meal (the shelves also empty through the day: SoldPerHour)
        public const float VanRate = 0.15f;        // crates per hour a shop gets from beyond the village anyway...
        public const float VanFloor = 2f;          // ...but only up to this: the rest comes from the village's farms and works
        public const float SoldPerHour = 0.35f;    // crates an open shop sells in an hour to customers from round about (a lorry comes twice a day)
        public const int CratesPerTrip = 2, TrainHolds = 12;

        private sealed class Stock { public float Have, Waiting; public bool Supplied; public int BareDay = -1; }
        private readonly Dictionary<long, Stock> stocks = new();
        private readonly Dictionary<int, float> regionWant = new();
        private readonly HashSet<int> regionStation = new();   // sides of the line with a passenger station (a train ride away)
        private readonly HashSet<int> regionYard = new();      // ...and with a freight yard (goods go by rail)
        private readonly List<Place> stationPlace = new();
        private readonly List<(float d, Agent a, Place p)> jobPairs = new();
        private float supplyClock = -1f;
        private readonly Dictionary<long, ParticleSystem> chimneys = new();

        /// <summary>One in four villagers doesn't work (retired, or just not the working kind): fixed by their seed.</summary>
        private static bool Works(Agent a) => ((uint)a.Seed * 2654435761u >> 16) % 4 != 0;

        private Stock StockFor(long key, Blueprint bp)
        {
            if (!stocks.TryGetValue(key, out var s))
                stocks[key] = s = new Stock { Have = bp.Goods == Goods.Sells ? StockCap - 2f : bp.Goods == Goods.Grows ? 3f : 0f };   // a new shop opens two crates short (its first lorry comes at once), a new farm has a first harvest ready
            return s;
        }

        /// <summary>Where the worker stands (the door, or the edge of the field) and which way they face.</summary>
        private void SetWorkPoint(Place p, HouseInstance h)
        {
            var bp = p.Bp;
            if (p.Door >= 0) { p.Work = p.Approach[p.Door]; p.WorkYaw = h.rotation + 180f; }
            else
            {
                var local = new Vector3(bp.Farm ? bp.Half.x * 0.45f : 0f, 0f, -(bp.Half.y * 0.95f + 0.012f));
                var d = Quaternion.Euler(0f, h.rotation, 0f) * local;
                p.Work = new Vector2(h.x + d.x, h.z + d.z);
                p.WorkYaw = bp.Farm ? h.rotation : h.rotation + 180f;   // a farmer faces the field
                if (!Walkable(p.Work) && FindNear(p.Work, 0.04f, out var near)) p.Work = near;
            }
            p.WorkRegion = Grid.RegionAt(p.Work);
        }

        /// <summary>
        /// Who works where: the nearest willing residents, closest first, one per place, as long as they can get there
        /// (on foot, or between two stations). Somebody newly taken on is news.
        /// </summary>
        private void AssignJobs(bool quiet)
        {
            jobPairs.Clear();
            foreach (var a in agents)
            {
                if (a.HomeHouse >= places.Count || a.Child) continue;
                var home = a.HomeHouse >= 0 ? places[a.HomeHouse] : null;   // (no home yet: still glad of a job)
                if (!Works(a) && home?.Bp.Job == null) continue;   // (whoever lives over the shop keeps it)
                int region = home != null && home.Door >= 0 ? home.Region[home.Door] : Grid.RegionAt(a.Home);
                foreach (var p in places)
                {
                    if (p.Bp.Job == null || (!Works(a) && p.House != a.HomeHouse)) continue;
                    float d = p.House == a.HomeHouse ? -1f : (p.Work - a.Home).magnitude;   // living over the shop
                    if (d > 1.5f) continue;
                    if (!Grid.SameRegion(region, p.WorkRegion) && !(regionStation.Contains(region) && regionStation.Contains(p.WorkRegion))) continue;
                    jobPairs.Add((d, a, p));
                }
            }
            jobPairs.Sort((x, y) => x.d.CompareTo(y.d));
            foreach (var (_, a, p) in jobPairs)
            {
                if (a.Workplace >= 0 || p.Worker != null) continue;
                a.Workplace = p.House; p.Worker = a;
            }
            // Children go to the nearest school they can walk to.
            foreach (var a in agents)
            {
                a.School = -1;
                if (!a.Child) continue;
                float best = 1.2f;
                foreach (var p in places)
                    if (p.Bp.Id == Catalog.School && (p.Work - a.Home).magnitude < best && Grid.SameRegion(Grid.RegionAt(a.Home), p.WorkRegion)) { best = (p.Work - a.Home).magnitude; a.School = p.House; }
            }
            foreach (var a in agents)
            {
                long key = a.Workplace >= 0 ? places[a.Workplace].Key : 0;
                bool fresh = key != 0 && key != a.JobKey;
                if (fresh && !quiet) News = $"{a.Name} is the new {places[a.Workplace].Bp.Job} at the {places[a.Workplace].Bp.Name.ToLower()}";
                a.JobKey = key;
                if (fresh && !quiet) StartWorkNow(a);
            }
        }

        /// <summary>Just taken on: if it is their shift and they are free, they set off for work now (not when next they choose).</summary>
        private void StartWorkNow(Agent a)
        {
            if (a.State is Activity.Sleeping or Activity.Riding or Activity.InTaxi or Activity.Hailing or Activity.OnRide or Activity.Working or Activity.Unwell) return;
            if (a.Workplace < 0 || a.Workplace >= places.Count) return;
            var p = places[a.Workplace];
            if (!p.Bp.OnShift(Clock.Hour)) return;
            var work = new Intent { What = Intent.Kind.Work, For = Need.Work, House = p.House, Spot = p.Door >= 0 && !p.Bp.WorksIndoors ? p.Door : -1, Target = p.Work, FromStation = -1, ToStation = -1, From = -1, Place = p.Bp.Name, Planned = true };
            int mine = Grid.RegionAt(a.Position);
            if (work.Spot >= 0 && Taken(p.House, work.Spot)) return;
            if (!Grid.SameRegion(mine, p.WorkRegion) && !TrainRoute(mine, p.WorkRegion, a.Position, p.Work, out work.FromStation, out work.ToStation)) return;
            if (a.Hidden || a.Claimed) Stop(a, 0f);
            Carry(a, work);
        }

        // ------------------------------------------------------------------ choosing work

        /// <summary>On shift: go and mind the place; or carry produce to where it is wanted.</summary>
        private void AddWorkOptions(Agent a, int mine, float hour, float urge)
        {
            if (a.Child && a.School >= 0 && a.School < places.Count && hour >= 8f && hour < 14.5f && Now(a, hour) is not { Kind: SlotKind.Visit })
            {
                var school = places[a.School];
                if (school.Door >= 0 && Grid.SameRegion(mine, school.Region[school.Door]))
                    options.Add(new Intent { What = Intent.Kind.Work, For = Need.Work, House = school.House, Spot = -1, Target = school.Approach[school.Door], FromStation = -1, ToStation = -1, From = -1, Score = (0.6f + urge) * 1.8f, Place = school.Bp.Name });
                return;
            }
            if (a.Workplace < 0 || a.Workplace >= places.Count) return;
            var p = places[a.Workplace];
            if (!p.Bp.OnShift(hour)) return;
            var work = new Intent { What = Intent.Kind.Work, For = Need.Work, House = p.House, Spot = p.Door >= 0 && !p.Bp.WorksIndoors ? p.Door : -1, Target = p.Work, FromStation = -1, ToStation = -1, From = -1, Place = p.Bp.Name };
            bool walk = Grid.SameRegion(mine, p.WorkRegion);
            if ((work.Spot < 0 || !Taken(p.House, work.Spot)) && (walk || TrainRoute(mine, p.WorkRegion, a.Position, p.Work, out work.FromStation, out work.ToStation)))
            {
                work.Score = urge * 1.6f * (walk ? 1f : 0.8f);
                if (Now(a, hour) is { Kind: SlotKind.Work }) { work.Score = Mathf.Max(work.Score, 3f); work.Planned = true; }   // the shift
                options.Add(work);
            }
            if (!walk || (a.Position - p.Work).magnitude > 0.2f) return;   // hauling starts from work
            var haul = BestHaul(p, a);
            if (haul.What == Intent.Kind.Haul) { haul.Score = (0.5f + urge) * 1.3f; if (Now(a, hour) is { Kind: SlotKind.Work }) haul.Score = 3.3f; options.Add(haul); }   // carrying the produce is part of the shift
        }

        /// <summary>The most useful crate run for the worker of <paramref name="p"/>: deliver its produce, or fetch some.</summary>
        private Intent BestHaul(Place p, Agent a)
        {
            var plan = Intent.Nothing;
            float best = 0f;
            void Consider(Place from, Place to, bool export)
            {
                float want = export ? 2f : StockCap - to.Stock.Have;
                if (want < 1.5f) return;
                float d = (from.Work - a.Position).magnitude + (to.Work - from.Work).magnitude;
                if (d > 1.6f) return;
                float score = want * Near(d, 0.6f);
                if (score <= best) return;
                best = score;
                plan = new Intent { What = Intent.Kind.Haul, For = Need.Work, House = to.House, Spot = -1, Target = DropPoint(to), From = from.House, Pickup = from.Work,
                    FromStation = -1, ToStation = -1, Place = to.Bp.Name };
            }
            bool hasGoods = (p.Bp.Goods == Goods.Grows || p.Bp.Goods == Goods.Ships) && p.Stock.Have >= 1f;
            if (hasGoods)
            {
                foreach (var q in places)
                    if (q.Bp.Goods == Goods.Sells && Grid.SameRegion(p.WorkRegion, q.WorkRegion)) Consider(p, q, false);
                if (plan.What == Intent.Kind.None && p.Bp.Goods == Goods.Grows && ExportWanted(p.WorkRegion))
                    foreach (var s in stationPlace)
                        if (s != null && s.Bp.Id == Catalog.FreightYard && Grid.SameRegion(p.WorkRegion, s.WorkRegion)) Consider(p, s, true);
            }
            if (p.Bp.Goods == Goods.Sells && StockCap - p.Stock.Have >= 2f)
                foreach (var q in places)
                    if ((q.Bp.Goods == Goods.Grows || q.Bp.Goods == Goods.Ships) && q.Stock.Have >= 1f && Grid.SameRegion(p.WorkRegion, q.WorkRegion)) Consider(q, p, false);
            return plan;
        }

        /// <summary>Where a delivery is set down: beside the door (the shopkeeper may be standing in it), or the work point.</summary>
        private Vector2 DropPoint(Place p)
        {
            var h = world.Buildings.Houses[p.House];
            var side = (Vector2)(Quaternion.Euler(0f, 0f, -h.rotation) * Vector2.right) * 0.012f;
            var at = p.Work + side;
            return Walkable(at) ? at : p.Work;
        }

        /// <summary>Crates wanted on the far side of the line (where a station is), and a goods train to take them.</summary>
        private bool ExportWanted(int from)
        {
            if (!regionYard.Contains(from)) return false;
            bool goods = false;
            foreach (var t in world.Trains.Trains) goods |= t.Running && TrainSystem.Freight(t.Kind);
            if (!goods) return false;
            foreach (var kv in regionWant) if (kv.Key != from && kv.Value >= 2f && regionYard.Contains(kv.Key)) return true;
            return false;
        }

        // ------------------------------------------------------------------ carrying it out

        private Place PlaceAt(int house) => house >= 0 && house < places.Count && places[house].House == house ? places[house] : null;

        /// <summary>At the pickup: take up to two crates (nothing there any more: give up).</summary>
        private bool PickUp(Agent a)
        {
            var from = PlaceAt(a.Plan.From);
            if (from == null || (from.Centre - a.FromPos).sqrMagnitude > 1e-6f) return false;
            int n = Mathf.Min(CratesPerTrip, Mathf.FloorToInt(from.Stock.Have));
            if (n <= 0) return false;
            from.Stock.Have -= n;
            a.Carrying = n; a.CargoName = ProduceOf(from.Bp);
            a.Crate.gameObject.SetActive(true);
            return true;
        }

        private void Deliver(Agent a)
        {
            var to = PlaceAt(a.SpotHouse);
            if (to != null && a.Carrying > 0)
            {
                if (to.Bp.Goods == Goods.Ships) to.Stock.Waiting += a.Carrying;   // on the platform for the goods train
                else
                {
                    to.Stock.Have = Mathf.Min(StockCap + 1f, to.Stock.Have + a.Carrying);
                    if (!to.Stock.Supplied && to.Bp.Goods == Goods.Sells) News = $"{a.Name} brought the first {a.CargoName} to the {to.Bp.Name.ToLower()}";
                    to.Stock.Supplied = true;
                }
            }
            DropCrates(a);
            a.Need[(int)Need.Work] *= 0.6f;
        }

        private static void DropCrates(Agent a)
        {
            a.Carrying = 0;
            if (a.Crate != null) a.Crate.gameObject.SetActive(false);
        }

        private static string ProduceOf(Blueprint bp) => bp.Id switch
        {
            Catalog.WheatField => "sacks of wheat", Catalog.Windmill => "flour", Catalog.Garden => "vegetables", Catalog.Greenhouse => "tomatoes",
            Catalog.Barn => "milk and eggs", Catalog.Factory => "boxes of biscuits", Catalog.Sawmill => "planks", _ => "goods",
        };

        /// <summary>A shopping trip or a meal takes a little off the shelves; bare shelves send people off half satisfied.</summary>
        private bool Sell(Place p)
        {
            if (p.Bp.Goods != Goods.Sells) return true;
            if (p.Stock.Have >= SaleSize) { p.Stock.Have -= SaleSize; return true; }
            if (p.Stock.BareDay != Clock.Day) { p.Stock.BareDay = Clock.Day; News = $"The shelves at the {p.Bp.Name.ToLower()} are bare - a farm nearby would help"; }
            return false;
        }

        /// <summary>A farmer at work fills the barn.</summary>
        private void Grow(Agent a, float hours)
        {
            var p = PlaceAt(a.SpotHouse);
            if (p != null && p.Bp.Farm && a.Workplace == p.House) p.Stock.Have = Mathf.Min(StockCap, p.Stock.Have + GrowRate * hours);
        }

        /// <summary>
        /// Twice a second: the van tops up the shops, what each side of the line wants is totted up, and goods trains
        /// standing at a station unload what that side wants and load what waits on the platform.
        /// </summary>
        private void UpdateSupply()
        {
            float hours = supplyClock < 0f ? 0f : Mathf.Clamp(Clock.Total - supplyClock, 0f, 2f);
            if (Clock.Total < supplyClock) hours = 0f;
            supplyClock = Clock.Total;
            regionWant.Clear(); regionStation.Clear(); regionYard.Clear();
            foreach (var p in places)
            {
                if (p.Bp.Goods != Goods.Sells) continue;
                if (p.Bp.OpenAt(Clock.Hour) && p.Stock.Have > VanFloor) p.Stock.Have = Mathf.Max(VanFloor, p.Stock.Have - SoldPerHour * hours);   // the day's custom empties the shelves...
                if (p.Stock.Have < VanFloor) p.Stock.Have = Mathf.Min(VanFloor, p.Stock.Have + VanRate * hours);   // (the van only keeps shelves from going bare: the rest comes by lorry or by hand)
                regionWant[p.WorkRegion] = (regionWant.TryGetValue(p.WorkRegion, out float w) ? w : 0f) + StockCap - p.Stock.Have;
            }
            foreach (var s in stationPlace) if (s != null) (s.Bp.Id == Catalog.FreightYard ? regionYard : regionStation).Add(s.WorkRegion);
            UpdateChimneys();
            foreach (var t in world.Trains.Trains)
            {
                if (!TrainSystem.Freight(t.Kind)) continue;
                int st = t.AtStation;
                if (st < 0) { t.HandledStop = -1; continue; }
                if (st == t.HandledStop || st >= stationPlace.Count || stationPlace[st] == null) continue;
                if (stationPlace[st].Bp.Id == Catalog.FreightYard) continue;   // (the yard's crane does it, crate by crate: see FreightSystem)
                t.HandledStop = st;
                var sp = stationPlace[st];
                if (t.Cargo > 0 && st != t.LoadedAt && regionWant.TryGetValue(sp.WorkRegion, out float want) && want >= 1f)
                {
                    sp.Stock.Have += t.Cargo;
                    News = $"The goods train unloaded {t.Cargo} crate{(t.Cargo == 1 ? "" : "s")} at the station";
                    t.Cargo = 0;
                }
                int load = Mathf.Min(Mathf.FloorToInt(sp.Stock.Waiting), TrainHolds - t.Cargo);
                if (load > 0) { t.Cargo += load; sp.Stock.Waiting -= load; t.LoadedAt = st; }
            }
        }

        /// <summary>The factory chimney smokes while someone is at work there.</summary>
        private void UpdateChimneys()
        {
            List<long> gone = null;
            foreach (var kv in chimneys) if (!places.Exists(p => p.Key == kv.Key && p.Bp.Id == Catalog.Factory)) (gone ??= new List<long>()).Add(kv.Key);
            if (gone != null) foreach (long k in gone) { if (chimneys[k]) Release(chimneys[k].gameObject); chimneys.Remove(k); }
            foreach (var p in places)
            {
                if (p.Bp.Id != Catalog.Factory) continue;
                bool working = p.Worker != null && AtWork(p.Worker, p.House);
                if (!chimneys.TryGetValue(p.Key, out var smoke) || smoke == null)
                {
                    if (!working) continue;
                    var top = world.Buildings.LocalToWorld(p.House, HouseFactory.FactoryChimney + new Vector3(0f, 0.125f, 0f));
                    chimneys[p.Key] = smoke = TrafficSystem.Smoke(transform, top, world.Materials.Smoke, new Color(0.82f, 0.82f, 0.85f, 0.7f), 8f, 0.02f);
                }
                if (working && !smoke.isEmitting) smoke.Play();
                else if (!working && smoke.isEmitting) smoke.Stop();
            }
        }

        /// <summary>Whether the factory's chimney is smoking (tests).</summary>
        public bool Smoking(int house) => PlaceAt(house) is { } p && chimneys.TryGetValue(p.Key, out var s) && s != null && s.isEmitting;

        // ------------------------------------------------------------------ saving and asking

        public SupplyInstance[] ExportSupplies()
        {
            var list = new List<SupplyInstance>();
            foreach (var h in world.Buildings.Houses)
            {
                var bp = Catalog.Get(h.variant);
                if (bp.Goods == Goods.None || !stocks.TryGetValue(PlaceKey(h), out var s)) continue;
                list.Add(new SupplyInstance { x = h.x, z = h.z, variant = h.variant, have = s.Have, waiting = s.Waiting });
            }
            return list.ToArray();
        }

        public void SetSupplies(IEnumerable<SupplyInstance> list)
        {
            if (list == null) return;
            foreach (var s in list)
                stocks[PlaceKey(new HouseInstance { x = s.x, z = s.z, variant = s.variant })] = new Stock { Have = Mathf.Max(0f, s.have), Waiting = Mathf.Max(0f, s.waiting), Supplied = true };
            placesDirty = true;
        }

        /// <summary>Crates at a place (on the shelves, ready on the farm, arrived at the station); -1 if it keeps none.</summary>
        public float StockAt(int house)
        {
            var p = PlaceAt(house);
            return p == null || p.Bp.Goods == Goods.None ? -1f : p.Stock.Have;
        }

        /// <summary>Sets the crates kept at a place (tools and tests).</summary>
        public void SetStock(int house, float have)
        {
            RefreshPlaces();
            if (PlaceAt(house) is { } p) { p.Stock.Have = Mathf.Max(0f, have); p.Stock.BareDay = -1; }   // (shelves set anew: a bare day is news again)
        }

        /// <summary>Crates on a station's platform waiting for a goods train.</summary>
        public float WaitingAt(int house) => PlaceAt(house)?.Stock.Waiting ?? 0f;

        // ------------------------------------------------------------------ lorries (see TrafficSystem.Freight.cs)

        /// <summary>Crates ready to be collected at a place that makes something (whole crates), or 0.</summary>
        public int ReadyAt(int house)
        {
            var p = PlaceAt(house);
            return p == null || p.Bp.Makes == Cargo.None ? 0 : Mathf.FloorToInt(p.Stock.Have);
        }

        /// <summary>How many crates a place that takes something would welcome now (0: none).</summary>
        public int WantedAt(int house)
        {
            var p = PlaceAt(house);
            if (p == null || p.Bp.Takes == Cargo.None) return 0;
            if (p.Bp.Id == Catalog.Harbour) return p.Stock.Waiting < 6f ? 3 : 0;   // for the next ship
            if (p.Bp.Goods == Goods.Ships) return p.Stock.Waiting < TrainHolds && ExportWanted(p.WorkRegion) ? 3 : 0;   // for the goods train
            return Mathf.FloorToInt(StockCap - p.Stock.Have);
        }

        /// <summary>A lorry loads up to <paramref name="most"/> crates; how many it got.</summary>
        public int TakeCrates(int house, int most)
        {
            var p = PlaceAt(house);
            if (p == null) return 0;
            int n = Mathf.Clamp(Mathf.FloorToInt(p.Stock.Have), 0, most);
            p.Stock.Have -= n;
            return n;
        }

        /// <summary>A lorry unloads crates at a place (on the shelves, or on the platform for the goods train).</summary>
        public void BringCrates(int house, int n, string what)
        {
            var to = PlaceAt(house);
            if (to == null || n <= 0) return;
            if (to.Bp.Goods == Goods.Ships) { to.Stock.Waiting += n; return; }
            to.Stock.Have = Mathf.Min(StockCap + 1f, to.Stock.Have + n);
            if (!to.Stock.Supplied && to.Bp.Goods == Goods.Sells) News = $"The first lorry of {what} arrived at the {to.Bp.Name.ToLower()}";
            to.Stock.Supplied = true;
        }

        /// <summary>The crane lifts one waiting crate off a freight yard's stack (onto a goods train): whether there was one.</summary>
        public bool TakeWaiting(int house)
        {
            var p = PlaceAt(house);
            if (p == null || p.Stock.Waiting < 1f) return false;
            p.Stock.Waiting -= 1f;
            return true;
        }

        /// <summary>The crane sets a crate down at a freight yard from a goods train: arrived, for collecting.</summary>
        public void Receive(int house, int n) { if (PlaceAt(house) is { } p) p.Stock.Have = Mathf.Min(StockCap * 2f, p.Stock.Have + n); }

        /// <summary>Whether the side of the line a station is on wants crates (a goods train unloads there).</summary>
        public bool WantsAt(int station) => station >= 0 && station < stationPlace.Count && stationPlace[station] != null && regionWant.TryGetValue(stationPlace[station].WorkRegion, out float w) && w >= 1f;

        /// <summary>What a place makes, in words ("sacks of wheat").</summary>
        public static string Produce(int variant) => ProduceOf(Catalog.Get(variant));

        /// <summary>Whether a field's farmer is at work on its tractor now.</summary>
        public bool OnTractor(int house) => PlaceAt(house) is { Worker: { } w } p && p.Bp.Tractors > 0 && AtWork(w, house) && w.Hidden;

        /// <summary>The outfit, seed and name of the villager who works at a place (style 0, seed 0, null if nobody).</summary>
        public (int style, int seed, string name) WorkerOf(int house) => PlaceAt(house) is { Worker: { } w } ? (w.Style, w.Seed, w.Name) : (0, 0, null);

        /// <summary>The villager who works at a place, or -1.</summary>
        public int WorkerAt(int house) => PlaceAt(house)?.Worker?.Index ?? -1;

        public int CarryingOf(int i) => agents[i].Carrying;

        /// <summary>The worker is at work there now.</summary>
        private bool AtWork(Agent a, int house) => a.Workplace == house && a.SpotHouse == house && a.State == Activity.Working;

        private string StockLine(Place p)
        {
            var s = p.Stock;
            return p.Bp.Goods switch
            {
                Goods.Sells => s.Have >= StockCap * 0.66f ? "shelves full" : s.Have >= SaleSize ? "shelves half empty" : "shelves bare",
                Goods.Grows => s.Have >= 1f ? $"{Mathf.FloorToInt(s.Have)} crate{(s.Have >= 2f ? "s" : "")} of {ProduceOf(p.Bp)} ready" : "nothing ready yet",
                Goods.Ships => s.Waiting >= 1f || s.Have >= 1f ? $"{Mathf.FloorToInt(s.Waiting)} crates for the goods train, {Mathf.FloorToInt(s.Have)} arrived" : null,
                _ => null,
            };
        }

        private static string HourLabel(float h) => $"{Mathf.FloorToInt(h):0}:{Mathf.RoundToInt(h % 1f * 60f):00}";
    }
}
