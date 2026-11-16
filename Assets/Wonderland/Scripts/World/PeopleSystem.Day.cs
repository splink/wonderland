using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// A villager's plan for the day, made when they get up, so that one can be followed and recognised: the morning
    /// shopping at their usual shop, a shift at work with a lunch break (school for children), an afternoon out at a
    /// place they like, an evening out, home to bed. The places are real ones they can get to (on foot or by train),
    /// open at that hour; favourite places are kept from day to day. The plan doesn't dictate - needs still decide - but
    /// what it says for this hour is much the strongest choice, so a day reads as a routine. When a planned place goes,
    /// closes or can't be reached, that part of the plan is dropped (and the card says so). A place they failed to get
    /// to is avoided for a few hours instead of being tried again and again.
    /// </summary>
    public sealed partial class PeopleSystem
    {
        public enum SlotKind { Home, Work, School, Visit, Hike }

        public struct Slot
        {
            public float From, To;
            public SlotKind Kind;
            public Need For;
            public long Place;        // the place (by its key; 0: home or none)
        }

        private readonly Dictionary<long, Place> placeByKey = new();
        private readonly List<Place> candidates = new();

        private Place ByKey(long key) => key != 0 && placeByKey.TryGetValue(key, out var p) ? p : null;

        /// <summary>The day's plan: made on waking (or when it no longer fits the world).</summary>
        private void PlanDay(Agent a)
        {
            a.Day.Clear();
            a.DayOf = Clock.Day;
            var rng = new Rng(a.Seed * 7919 + Clock.Day * 104729);
            float wake = a.WakeHour, bed = a.BedHour > 24f ? 23.9f : a.BedHour;
            void Add(float from, float to, SlotKind kind, Need need, Place p = null)
            {
                from = Mathf.Max(from, wake + 0.2f); to = Mathf.Min(to, bed - 0.5f);
                if (to - from < 0.5f) return;
                a.Day.Add(new Slot { From = from, To = to, Kind = kind, For = need, Place = p?.Key ?? 0 });
            }
            void Out(float from, float to, Need need)
            {
                // A walker living by a trail: the afternoon is a hike.
                if (need == Need.Sights && !a.Child && TrailNear(a.Home) && rng.Chance(0.6f)) { Add(from, to, SlotKind.Hike, need); return; }
                Add(from, to, SlotKind.Visit, need, PickPlace(a, need, (from + to) * 0.5f, ref rng));
            }
            Need Leisure(ref Rng r) => Needs.Affinity(a.Seed, Need.Fun) * r.Range(0.7f, 1.3f) > Needs.Affinity(a.Seed, Need.Sights) * r.Range(0.7f, 1.3f) ? Need.Fun : Need.Sights;
            if (a.Child)
            {
                if (a.School >= 0 && a.School < places.Count) Add(8f, 14.5f, SlotKind.School, Need.Work, places[a.School]);
                else Out(9f, 12f, Need.Fun);
                Add(12f, 13f, SlotKind.Home, Need.Food);
                Out(a.School >= 0 ? 14.7f : 13.2f, 17.8f, Need.Fun);
                return;
            }
            var job = a.Workplace >= 0 && a.Workplace < places.Count ? places[a.Workplace] : null;
            if (job != null)
            {
                float s = job.Bp.ShiftFrom, e = Mathf.Min(job.Bp.ShiftTo, bed - 0.5f);
                if (s - wake > 1.4f) Out(wake + 0.3f, s - 0.4f, Need.Errands);
                if (s < 12f && e > 13.5f)
                {
                    Add(s, 12f, SlotKind.Work, Need.Work, job);
                    Out(12f, 13f, Need.Food);
                    Add(13f, e, SlotKind.Work, Need.Work, job);
                }
                else Add(s, e, SlotKind.Work, Need.Work, job);
                if (e < 17.5f) Out(e + 0.3f, 18.5f, Leisure(ref rng));
            }
            else
            {
                Out(wake + 0.6f, 10.8f, Need.Errands);
                Out(10.8f, 12f, rng.Chance(0.5f) ? Need.Social : Need.Sights);
                Out(12f, 13.4f, Need.Food);
                Out(13.6f, 17.5f, Leisure(ref rng));
            }
            // The evening: company, supper out, or (night owls) the disco; then home.
            float evening = a.BedHour > 23.5f ? 19.5f : 18.6f;
            Out(evening, bed - 0.6f, a.BedHour > 23.5f && rng.Chance(0.6f) ? Need.Fun : rng.Chance(0.5f) ? Need.Social : Need.Food);
            for (int i = 0; i < a.Day.Count; i++)
                if (a.Day[i].Kind == SlotKind.Visit && a.Day[i].Place == 0) { var s = a.Day[i]; s.Kind = SlotKind.Home; a.Day[i] = s; }   // nowhere suits: a quiet time at home
        }

        /// <summary>
        /// Where they'd go for <paramref name="need"/> around <paramref name="hour"/>: their usual place if it still suits,
        /// else the best place they can get to that is open then - near home, what they like, a little chance. Null: nowhere.
        /// </summary>
        private Place PickPlace(Agent a, Need need, float hour, ref Rng rng)
        {
            var home = a.HomeHouse >= 0 && a.HomeHouse < places.Count ? places[a.HomeHouse] : null;
            int region = home != null && home.Door >= 0 ? home.Region[home.Door] : Grid.RegionAt(a.Home);
            bool somethingNew = false;
            foreach (var p in places) if (Clock.Total - p.FirstSeen < 12f && p.Bp.Serves == need) somethingNew = true;
            if (!somethingNew && a.Favourites.TryGetValue(need, out long fav) && ByKey(fav) is { } usual && Suits(a, usual, need, hour, region) && rng.Chance(0.8f)) return usual;
            Place best = null; float bestScore = 0f;
            foreach (var p in places)
            {
                if (!Suits(a, p, need, hour, region)) continue;
                float d = (p.Centre - a.Home).magnitude;
                bool walk = Grid.SameRegion(region, p.WorkRegion);
                if (walk && d > 1f) continue;   // further than an hour or two on foot: only by train
                float score = Near(d, walk ? 0.35f : 0.9f) * (walk ? 1f : 0.6f) * rng.Range(0.6f, 1.4f) * (p.Bp.Kids ? (a.Child ? 2.4f : 0.6f) : 1f);
                if (p.Bp.Rhythm != null) score *= 0.4f + Needs.Appeal(p.Bp, hour);
                float novelty = Novelty(p); score *= novelty * novelty;   // something new: everyone wants a look
                if (Rain > 0.5f && OpenAir(p.Bp.Id)) score *= 0.15f;   // in the rain: the museum, not the park
                if (score > bestScore) { bestScore = score; best = p; }
            }
            if (best != null) a.Favourites[need] = best.Key;
            return best;
        }

        /// <summary>How hard it is raining (0..1, from the sky).</summary>
        public float Rain;

        /// <summary>Places out in the open, which nobody much fancies in a shower.</summary>
        public static bool OpenAir(int id) => id is Catalog.Park or Catalog.Playground or Catalog.Football or Catalog.Fountain or Catalog.Carousel or Catalog.FerrisWheel
            or Catalog.Bandstand or Catalog.CastleRuin or Catalog.Lookout or Catalog.Campsite or Catalog.Garden or Catalog.Picnic or Catalog.Statue or Catalog.Well or Catalog.Bench or Catalog.Jetty;

        private bool Suits(Agent a, Place p, Need need, float hour, int region)
        {
            if (p.Bp.Serves != need || p.Bp.Dwelling && p.Bp.Category == PlaceCategory.Homes || p.House == a.Workplace) return false;
            if (!p.Bp.OpenAt(hour) || p.Approach.Length == 0 && p.Door < 0) return false;
            if (a.Child && (p.Bp.Id == Catalog.Disco || p.Bp.Rhythm != null && hour > 19f)) return false;
            if (a.Avoid != null && a.Avoid.TryGetValue(p.Key, out float until) && Clock.Total < until) return false;
            if ((p.Centre - a.Home).magnitude > (p.Bp.Category == PlaceCategory.Fun ? 2.2f : 1.6f)) return false;
            if (Grid.SameRegion(region, p.WorkRegion)) return true;
            return regionStation.Contains(region) && regionStation.Contains(p.WorkRegion) && PassengerTrains;   // a train ride away
        }

        /// <summary>The part of the plan for this hour (or null).</summary>
        private Slot? Now(Agent a, float hour, bool plan = true)
        {
            if (plan && a.DayOf != Clock.Day && hour >= a.WakeHour && hour < 20f) PlanDay(a);   // (only when choosing: never mid-frame for everyone at once)
            foreach (var s in a.Day) if (hour >= s.From && hour < s.To) return s;
            return null;
        }

        /// <summary>A planned visit whose part of the day is over (lunch is over: back to work, or on to the afternoon).</summary>
        private bool SlotOver(Agent a) => a.Plan.Planned && a.DayOf == Clock.Day && (Now(a, Clock.Hour, false) is not { } s || s.From != a.VisitedFrom);

        /// <summary>The next part of the plan after this hour (or null).</summary>
        private Slot? Next(Agent a, float hour)
        {
            foreach (var s in a.Day) if (s.From > hour) return s;
            return null;
        }

        /// <summary>What the plan says to do now, as an option (strongly preferred); adds nothing for a home slot or a gap.</summary>
        private void AddPlanOption(Agent a, int mine, float hour)
        {
            var slot = Now(a, hour);
            if (slot == null || slot.Value.Kind is SlotKind.Work or SlotKind.Home) return;   // work: see AddWorkOptions; home: the home option
            if (slot.Value.Kind == SlotKind.Hike)
            {
                if (Rain < 0.5f && outdoorWalkers < Mathf.Max(3, agents.Count / 12)) options.Add(new Intent { What = Intent.Kind.Hike, For = Need.Sights, House = -1, Spot = -1, FromStation = -1, ToStation = -1, From = -1, Score = 4f, Planned = true, Place = "trail" });
                return;
            }
            var p = ByKey(slot.Value.Place);
            if (p == null) { DropSlot(a, slot.Value); return; }   // the place is gone
            if (slot.Value.Kind == SlotKind.School) return;
            if (!p.Bp.OpenAt(hour) || !p.Bp.OpenAt(hour + 0.3f)) return;   // not open yet (or closing): later
            int spot = FreeSpot(p, a);
            if (spot < 0) return;   // full right now: something else for a while
            var plan = new Intent { What = Intent.Kind.Spot, For = slot.Value.For, House = p.House, Spot = spot, Target = p.Approach[spot], FromStation = -1, ToStation = -1, From = -1, Place = p.Bp.Name };
            if (!Grid.SameRegion(mine, p.Region[spot]) && !TrainRoute(mine, p.Region[spot], a.Position, plan.Target, out plan.FromStation, out plan.ToStation)) { Avoid(a, p.Key, 2f); DropSlot(a, slot.Value); return; }
            plan.Score = 4f;
            plan.Planned = true;
            options.Add(plan);
        }

        private static void DropSlot(Agent a, Slot s)
        {
            for (int i = 0; i < a.Day.Count; i++)
                if (a.Day[i].From == s.From && a.Day[i].To == s.To) { var d = a.Day[i]; d.Kind = SlotKind.Home; d.Place = 0; a.Day[i] = d; return; }
        }

        /// <summary>Somewhere they couldn't get to: not again for a while.</summary>
        private void Avoid(Agent a, long key, float hours)
        {
            if (key == 0) return;
            (a.Avoid ??= new Dictionary<long, float>())[key] = Clock.Total + hours;
        }

        private bool Avoided(Agent a, Place p) => a.Avoid != null && a.Avoid.TryGetValue(p.Key, out float until) && Clock.Total < until;

        // ------------------------------------------------------------------ for the card

        private bool TrailNear(Vector2 home)
        {
            foreach (var l in world.Paths.Lines) if (l.kind == (int)PathKind.Trail && PathSystem.Distance(l, home) < 0.45f) return true;
            return false;
        }

        private string SlotLine(Agent a, Slot s)
        {
            var p = ByKey(s.Place);
            string where = p != null ? $"the {p.Bp.Name.ToLower()}{Village(p)}" : "home";
            string at = $"{Mathf.FloorToInt(s.From):00}:{Mathf.FloorToInt(s.From % 1f * 6f) * 10:00}";
            return s.Kind switch
            {
                SlotKind.Work => $"{at} work at {where}",
                SlotKind.School => $"{at} school",
                SlotKind.Hike => $"{at} a hike along the trail",
                SlotKind.Home => $"{at} home" + (s.For == Need.Food ? " for lunch" : ""),
                _ => $"{at} {Needs.Reason(s.For, VillageClock.PartAt(s.From))} at {where}",
            };
        }

        /// <summary>" in Brookside": the village (by its station) a place belongs to, when there are stations.</summary>
        private string Village(Place p)
        {
            var trains = world.Trains;
            int best = -1; float bestD = 1.1f;
            for (int i = 0; i < trains.Stations.Count; i++) { float d = (trains.Stations[i].House - p.Centre).magnitude; if (d < bestD) { bestD = d; best = i; } }
            return best >= 0 && trains.Stations.Count > 1 ? " in " + trains.StationName(best) : "";
        }
    
        // ------------------------------------------------------------------ the character card

        /// <summary>Who a villager is and what they are doing, from the simulation itself (nothing made up): for the card.</summary>
        public struct PersonCard
        {
            public string Name, Role, About, Home, Work, Now, Goal, Next, Friends, Stage, Traits, Hours;
            /// <summary>Today's plan, one part of the day a line ("08:00 work at the bakery"); the one under way is marked.</summary>
            public List<(string line, bool now, bool done)> Today;
            public int Style;
            public bool Child;
            public Vector3 Position;
            public Vector3? HomeAt, WorkAt, Destination;
        }

        public PersonCard CardOf(int i)
        {
            var a = agents[i];
            var c = new PersonCard { Name = a.Name, Style = People[i].style, Child = a.Child, Position = new Vector3(a.Position.x, a.Height, a.Position.y) };
            var work = a.Workplace >= 0 && a.Workplace < places.Count ? places[a.Workplace] : null;
            var school = a.Child && a.School >= 0 && a.School < places.Count ? places[a.School] : null;
            c.Role = a.Child ? (school != null ? "Pupil" : "Child") : work != null ? Capital(work.Bp.Job) : Works(a) ? "Between jobs" : "Retired";
            // A few lasting traits that shape what they do.
            var traits = new List<string> { a.Child ? "a child" : "a grown-up" };
            if (a.BedHour > 23.5f) traits.Add("a night owl"); else if (a.WakeHour < 6.4f && !a.Child) traits.Add("an early riser");
            Need fond = Need.Errands; float best = 0f;
            for (int n = 0; n < Needs.Count; n++) { if ((Need)n == Need.Work || (Need)n == Need.Rest) continue; float f = Needs.Affinity(a.Seed, (Need)n); if (f > best) { best = f; fond = (Need)n; } }
            traits.Add(fond switch { Need.Fun => "loves a day at the fair", Need.Food => "loves good food", Need.Social => "sociable", Need.Sights => "a keen walker", _ => "likes a browse in the shops" });
            c.About = Capital(string.Join(", ", traits));
            c.Stage = a.Child ? "child" : "grown-up";
            c.Traits = Capital(string.Join(", ", traits.GetRange(1, traits.Count - 1)));
            c.Hours = $"up at {HourLabel(a.WakeHour)}, bed at {HourLabel(a.BedHour % 24f)}";
            if (a.DayOf == Clock.Day && a.Day.Count > 0)
            {
                c.Today = new List<(string, bool, bool)>();
                foreach (var sl in a.Day) c.Today.Add((SlotLine(a, sl), Clock.Hour >= sl.From && Clock.Hour < sl.To, Clock.Hour >= sl.To));
            }
            if (a.HomeHouse >= 0 && a.HomeHouse < places.Count)
            {
                var home = places[a.HomeHouse];
                c.Home = $"the {home.Bp.Name.ToLower()}{Landmark(home)}";
                c.HomeAt = Above(home);
            }
            else c.Home = null;
            if (work != null) { c.Work = $"the {work.Bp.Name.ToLower()}{Village(work)} ({HourLabel(work.Bp.ShiftFrom)}-{HourLabel(Mathf.Min(work.Bp.ShiftTo, 24f))})"; c.WorkAt = Above(work); }
            else if (school != null) { c.Work = $"the school{Village(school)}"; c.WorkAt = Above(school); }
            string doing = Describe(i);
            c.Now = Capital(doing.Substring(doing.IndexOf(" - ") + 3));
            // The goal: where the current plan leads, and why.
            var plan = a.Plan;
            if (plan.What != Intent.Kind.None && plan.House >= 0 && plan.House < world.Buildings.Houses.Count)
            {
                var p = PlaceAt(plan.House);
                string where = p != null ? $"the {p.Bp.Name.ToLower()}{Village(p)}" : "somewhere";
                c.Goal = plan.What switch
                {
                    Intent.Kind.Home or Intent.Kind.Sleep when p != null && p.House == a.HomeHouse => "home" + (plan.What == Intent.Kind.Sleep ? " to bed" : " for a rest"),
                    Intent.Kind.Work => $"work at {where}",
                    Intent.Kind.Haul => $"take {(a.Carrying > 0 ? a.CargoName : "produce")} to {where}",
                    Intent.Kind.MoveIn => $"move into {where}",
                    Intent.Kind.Neighbour => $"call at {where}",
                    _ => $"{Needs.Reason(plan.For, Clock.Part)} at {where}",
                } + (plan.ByTrain ? $", by train from {world.Trains.StationName(plan.FromStation)} to {world.Trains.StationName(plan.ToStation)}" : "") + (plan.Planned ? " (as planned)" : "");
                c.Destination = Above(p) ?? new Vector3(plan.Target.x, GroundAt(plan.Target), plan.Target.y);
            }
            else if (plan.What is Intent.Kind.Hike) c.Goal = "a hike along the trail";
            else if (plan.What is Intent.Kind.Stroll) { c.Goal = "a stroll"; c.Destination = new Vector3(plan.Target.x, GroundAt(plan.Target), plan.Target.y); }
            else if (a.HomeHouse < 0) c.Goal = "find somewhere to live";
            if (a.DayOf == Clock.Day && Next(a, Clock.Hour) is { } next) c.Next = SlotLine(a, next);
            if (a.Friends.Count > 0)
            {
                var names = new List<string>();
                foreach (var f in a.Friends) names.Add(f.Name);
                c.Friends = string.Join(", ", names);
            }
            return c;
        }

        /// <summary>The way they are walking now (their position, the waypoints left, the target), for the follow line.</summary>
        public void RouteOf(int i, List<Vector3> into)
        {
            into.Clear();
            var a = agents[i];
            if (a.State != Activity.Walking) return;
            into.Add(new Vector3(a.Position.x, a.Height + 0.004f, a.Position.y));
            foreach (var w in a.Waypoints) into.Add(new Vector3(w.x, GroundAt(w) + 0.004f, w.y));
            into.Add(new Vector3(a.Target.x, GroundAt(a.Target) + 0.004f, a.Target.y));
        }

        public Vector3 HeadOf(int i) { var a = agents[i]; return a.Hidden ? new Vector3(a.Approach.x, GroundAt(a.Approach), a.Approach.y) : new Vector3(a.Position.x, a.Height, a.Position.y); }

        private Vector3? Above(Place p) => p == null ? null : new Vector3(p.Centre.x, world.Buildings.TopOf(p.House) + 0.02f, p.Centre.y);

        /// <summary>" near Brookside station", " by the church": how a home is found.</summary>
        private string Landmark(Place home)
        {
            string station = Village(home);
            if (station.Length > 0) return station.Replace(" in ", " near ") + " station";
            Place best = null; float bestD = 0.35f;
            foreach (var p in places)
            {
                if (p == home || p.Bp.Small || p.Bp.Dwelling && p.Bp.Category == PlaceCategory.Homes) continue;
                float d = (p.Centre - home.Centre).magnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best != null ? $" by the {best.Bp.Name.ToLower()}" : "";
        }

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
}
