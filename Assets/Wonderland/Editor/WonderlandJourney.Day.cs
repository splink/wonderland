using System;
using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// Following villagers through a day: each leaves home with a plan, goes where it says (their usual shop, work or
        /// school, lunch, an afternoon out), is home by bedtime, and nobody with a home wanders about aimlessly. The card
        /// tells the truth: where it says they are going is where they are walking. A planned place taken away drops out
        /// of the plan; nobody gets stuck.
        /// </summary>
        private static void DayJourney()
        {
            var w = Scratch("Daily life", 420f);
            var t = w.Terrain;
            w.BuildPath(Stroke(t, new Vector2(-0.75f, 0f), new Vector2(0.75f, 0f)), PathKind.Road);
            w.BuildPath(Stroke(t, new Vector2(0f, -0.6f), new Vector2(0f, 0.6f)), PathKind.Footpath);
            foreach (var (id, x, z, rot) in new[]
            {
                (Catalog.Cottage, -0.6f, 0.12f, 0f), (Catalog.Townhouse, -0.4f, 0.12f, 0f), (Catalog.Apartments, -0.2f, 0.13f, 0f), (Catalog.Cottage, 0.22f, 0.12f, 0f),
                (Catalog.Thatched, 0.45f, 0.13f, 0f), (Catalog.Apartments, 0.65f, 0.13f, 0f), (Catalog.Bakery, -0.5f, -0.12f, 180f), (Catalog.Cafe, -0.25f, -0.12f, 180f),
                (Catalog.Grocer, 0.25f, -0.12f, 180f), (Catalog.School, 0.55f, -0.14f, 180f), (Catalog.Park, -0.35f, 0.42f, 0f), (Catalog.Fountain, 0.3f, 0.42f, 0f),
                (Catalog.Carousel, 0.6f, 0.45f, 0f), (Catalog.Museum, -0.4f, -0.45f, 0f), (Catalog.Restaurant, 0.3f, -0.45f, 0f),
            })
                Put(w, id, x, z, rot);
            var people = w.People;
            people.Grid.Complete();
            people.Step(Dt);
            Check(people.People.Count >= 8, $"A small town with homes, shops, a school, a park and a road ({people.People.Count} villagers)");
            w.Clock.Hour = 5f;
            int n = people.People.Count;
            var places = new List<HashSet<int>>(); var leftHome = new bool[n]; var workedOrSchool = new bool[n];
            for (int i = 0; i < n; i++) places.Add(new HashSet<int>());
            int planned = 0, wandering = 0, truthful = 0, lying = 0, stuck = 0, walkSamples = 0, homeAtNight = 0, housed = 0;
            var walkingFor = new float[n];
            string example = null;
            int startDay = w.Clock.Day;
            while (w.Clock.Day == startDay || w.Clock.Hour < 1.5f)
            {
                Live(w, 0.5f, false);
                float hour = w.Clock.Hour;
                for (int i = 0; i < n; i++)
                {
                    var plan = people.PlanOf(i);
                    var st = people.StateOf(i);
                    bool home = people.HomeOf(i) >= 0;
                    if (home && plan.What == Intent.Kind.Wander) wandering++;
                    if (plan.Planned) planned++;
                    if (people.AtPlace(i) && people.PlaceOf(i) != people.HomeOf(i)) { places[i].Add(people.PlaceOf(i)); leftHome[i] = true; }
                    if (st == PeopleSystem.Activity.Working) workedOrSchool[i] = true;
                    walkingFor[i] = st == PeopleSystem.Activity.Walking ? walkingFor[i] + 0.5f : 0f;
                    if (walkingFor[i] > 90f) stuck++;
                    if (st == PeopleSystem.Activity.Walking && plan.House >= 0 && hour > 8f && hour < 20f && walkSamples < 400)
                    {
                        walkSamples++;
                        var card = people.CardOf(i);
                        string place = Catalog.Get(w.Buildings.Houses[plan.House].variant).Name.ToLower();
                        if (card.Goal != null && (card.Goal.Contains(place) || card.Goal.StartsWith("home"))) truthful++; else { lying++; Debug.Log($"CARD mismatch: {card.Goal} vs {place} ({plan.What})"); }
                        if (example == null && plan.Planned && hour > 9f) example = $"{card.Name} ({card.Role}): {card.Now}; going to {card.Goal}; then {card.Next}";
                    }
                }
                if (hour >= 1f && hour < 1.5f && homeAtNight == 0)
                    for (int i = 0; i < n; i++) if (people.HomeOf(i) >= 0) { housed++; if (people.StateOf(i) == PeopleSystem.Activity.Sleeping && people.IsHidden(i)) homeAtNight++; }
            }
            int outAndAbout = 0, busyDay = 0, toWork = 0;
            for (int i = 0; i < n; i++) { if (leftHome[i]) outAndAbout++; if (places[i].Count >= 2) busyDay++; if (workedOrSchool[i]) toWork++; }
            Check(outAndAbout >= n - 1 && busyDay >= n / 2, $"Everyone leaves home during the day; most visit two or more places ({busyDay}/{n}; e.g. \"{example}\")");
            Check(toWork >= n / 3, $"Workers go to work and children to school ({toWork}/{n})");
            Check(planned > 0 && wandering == 0, $"Days follow plans (planned choices seen {planned} times) and nobody with a home wanders aimlessly ({wandering})");
            Check(lying == 0 && truthful > 20, $"The card says where they are really going ({truthful} checked, {lying} wrong)");
            Check(stuck == 0, $"Nobody walks for ages without getting anywhere ({stuck})");
            Check(housed > 0 && homeAtNight == housed, $"By one in the morning everyone with a home is in bed there ({homeAtNight}/{housed})");
            // A planned place taken away: it drops out of the plan.
            w.Clock.Hour = 7.5f;
            Live(w, 3f, false);
            int who = -1; string plannedPlace = null;
            for (int i = 0; i < n && who < 0; i++)
            {
                var card = people.CardOf(i);
                foreach (int id in new[] { Catalog.Bakery, Catalog.Grocer, Catalog.Cafe })
                    if (card.Next != null && card.Next.Contains(Catalog.Get(id).Name.ToLower())) { who = i; plannedPlace = Catalog.Get(id).Name.ToLower(); break; }
            }
            if (who >= 0)
            {
                var gone = w.Buildings.Houses.Find(h => Catalog.Get(h.variant).Name.ToLower() == plannedPlace);
                w.EraseAt(new Vector3(gone.x, 0.5f, gone.z), Vector3.down, new Vector3(gone.x, t.SurfaceAt(gone.x, gone.z), gone.z));
                Live(w, 3f, false);
                var card = people.CardOf(who);
                Check(card.Next == null || !card.Next.Contains(plannedPlace), $"A planned place taken away drops out of {card.Name}'s plan (then: \"{card.Next}\")");
            }
            else Check(false, "Someone plans to visit a shop later in the morning");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }
    }
}
