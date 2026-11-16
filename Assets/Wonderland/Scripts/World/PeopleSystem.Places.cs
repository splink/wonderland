using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    public sealed partial class PeopleSystem
    {
        /// <summary>What a building's card shows: all of it read from the simulation as it is now.</summary>
        public struct PlaceCard
        {
            public string Name, Kind, Status, StatusColour, Hours, Staff, Goods, Busiest, Here, Residents;
            public int Visitors, Coming, Today, Yesterday, Living, Beds;
            public bool Home, Visited;
        }

        // Visits counted per place per village day (by the place's key, so moving other buildings doesn't mix them up).
        private readonly Dictionary<long, (int day, int today, int yesterday)> visits = new();

        private void CountVisit(int house)
        {
            if (PlaceAt(house) is not { } p) return;
            int day = Clock.Day;
            visits.TryGetValue(p.Key, out var v);
            if (v.day != day) v = (day, 0, v.day == day - 1 ? v.today : 0);
            v.today++;
            visits[p.Key] = v;
        }

        public PlaceCard PlaceCardOf(int house)
        {
            var c = new PlaceCard();
            if (house < 0 || house >= world.Buildings.Houses.Count) return c;
            RefreshPlaces();
            var bp = Catalog.Get(world.Buildings.Houses[house].variant);
            var place = PlaceAt(house);
            c.Name = bp.Name;
            c.Kind = bp.Category.ToString();
            c.Home = bp.Dwelling && bp.Category == PlaceCategory.Homes;
            if (c.Home)
            {
                c.Living = ResidentsOf(house);
                c.Beds = place != null ? place.Beds : bp.Residents + 2;
                c.Residents = c.Living > 0 ? Residents(house) : null;
                c.Status = c.Living == 0 ? "Empty" : c.Living >= c.Beds ? "Full" : $"{c.Beds - c.Living} beds free";
                c.StatusColour = c.Living >= c.Beds ? CardText.Warn : CardText.Good;
                int asleep = 0, home = 0;
                for (int i = 0; i < agents.Count; i++)
                    if (agents[i].HomeHouse == house) { if (agents[i].State == Activity.Sleeping) asleep++; else if (agents[i].Hidden && agents[i].SpotHouse == house) home++; }
                c.Here = asleep > 0 ? $"{asleep} asleep" + (home > 0 ? $", {home} in" : "") : home > 0 ? $"{home} in" : "nobody in";
                return c;
            }
            float hour = Clock.Hour;
            bool hours = bp.Opens > 0f || bp.Closes < 24f;
            if (hours)
            {
                c.Hours = $"{HourLabel(bp.Opens)} - {HourLabel(bp.Closes % 24f)}";
                bool open = bp.OpenAt(hour);
                c.Status = open ? $"Open till {HourLabel(bp.Closes % 24f)}" : $"Closed, opens {HourLabel(bp.Opens)}";
                c.StatusColour = open ? CardText.Good : CardText.Bad;
            }
            else if (bp.Serves != Need.None) { c.Status = "Always open"; c.StatusColour = CardText.Good; }
            if (bp.Job != null)
            {
                var w = place?.Worker;
                c.Staff = w == null ? $"no {bp.Job} yet"
                    : AtWork(w, house) ? $"{w.Name} the {bp.Job}, at work"
                    : $"{w.Name} the {bp.Job} - {CardOf(w.Index).Now.ToLower()}";
            }
            if (place != null) c.Goods = StockLine(place);
            if (bp.Serves != Need.None)
            {
                c.Visitors = VisitorsAt(house);
                c.Coming = HeadingTo(house);
                var names = new List<string>();
                for (int i = 0; i < agents.Count && names.Count < 4; i++) if (agents[i].SpotHouse == house && AtPlace(i) && agents[i].Workplace != house) names.Add(agents[i].Name);
                if (names.Count > 0) c.Here = string.Join(", ", names) + (c.Visitors > names.Count ? $" and {c.Visitors - names.Count} more" : "");
                float peak = 0f, best = 0f;
                for (float h = 6f; h < 24f; h += 0.5f) { float v = Needs.Appeal(bp, h) * (bp.OpenAt(h) ? 1f : 0f); if (v > best) { best = v; peak = h; } }
                c.Busiest = $"in the {VillageClock.PartName(VillageClock.PartAt(peak))} - for {Needs.Reason(bp.Serves, VillageClock.PartAt(peak))}";
                if (place != null && visits.TryGetValue(place.Key, out var v2))
                {
                    c.Today = v2.day == Clock.Day ? v2.today : 0;
                    c.Yesterday = v2.day == Clock.Day ? v2.yesterday : v2.day == Clock.Day - 1 ? v2.today : 0;
                }
                c.Visited = place != null && place.Visited;
            }
            return c;
        }
    }
}
