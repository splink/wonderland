using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Plans from outside: the seam where something other than a villager's own needs can tell them what to do (a fête,
    /// a story, a schedule, one day an agent). Deciders are asked before the villager chooses for themselves; the Plan*
    /// helpers build intents the same way the villagers' own choosing does (a free spot, on foot or by train), and
    /// every plan from outside is checked before it is carried out, so a decider can be naive without breaking anything.
    /// </summary>
    public sealed partial class PeopleSystem
    {
        public readonly List<IVillageDecider> Deciders = new();

        /// <summary>Tells the player something (the palette shows it).</summary>
        public void Tell(string news) => News = news;

        public float Hour => Clock.Hour;

        /// <summary>Awake, not on a train or a ride: free to be given something to do.</summary>
        public bool IsFree(int villager)
        {
            var a = agents[villager];
            return !Bedtime(a, Clock.Hour) && a.State != Activity.Sleeping && a.State != Activity.Riding && a.State != Activity.OnRide && a.State != Activity.Waiting && a.State != Activity.Hailing && a.State != Activity.InTaxi;
        }

        private bool AskDeciders(Agent a, out Intent plan)
        {
            plan = Intent.Nothing;
            for (int k = 0; k < Deciders.Count; k++)
            {
                var d = Deciders[k];
                if (d == null || !d.Active) continue;
                if (d.Decide(this, a.Index, out plan) && Check(a, ref plan)) return true;
            }
            return false;
        }

        private void PruneDeciders()
        {
            for (int k = Deciders.Count - 1; k >= 0; k--) if (Deciders[k] == null || !Deciders[k].Active) Deciders.RemoveAt(k);
        }

        /// <summary>Whether a plan can be carried out by this villager now (fixing up what it can).</summary>
        private bool Check(Agent a, ref Intent plan)
        {
            if (plan.What == Intent.Kind.None) return false;
            var houses = world.Buildings.Houses;
            if (plan.House >= houses.Count || plan.House < -1) return false;
            if (plan.What == Intent.Kind.Haul) return false;   // produce moves by the villagers' own judgement
            bool needsPlace = plan.What is Intent.Kind.Spot or Intent.Kind.Work or Intent.Kind.Gather or Intent.Kind.Home or Intent.Kind.MoveIn or Intent.Kind.Neighbour;
            if (needsPlace && plan.House < 0) return false;
            if (plan.Spot >= 0)
            {
                if (plan.House < 0 || plan.Spot >= Catalog.Get(houses[plan.House].variant).Spots.Length || Taken(plan.House, plan.Spot)) return false;
            }
            if (!Walkable(plan.Target) && !OnPlatform(plan.Target, out _)) return false;
            if (plan.ByTrain) return plan.FromStation < stations.Count && plan.ToStation < stations.Count;
            plan.FromStation = plan.ToStation = -1;
            return Grid.Reachable(a.Position, plan.Target);
        }

        /// <summary>Carries out a plan now, interrupting whatever they were doing (not while asleep or aboard a train).</summary>
        public bool Direct(int villager, Intent plan)
        {
            var a = agents[villager];
            if (a.State == Activity.Sleeping || a.State == Activity.Riding || a.State == Activity.OnRide || a.State == Activity.InTaxi || a.State == Activity.Hailing) return false;
            if (!Check(a, ref plan)) return false;
            if (a.Hidden) { Hide(a, false); a.Position = a.Approach; a.Height = GroundAt(a.Position); }
            else if (a.State == Activity.Sitting || a.State == Activity.Playing || a.SpotLift > 0f) a.Position = a.Approach;
            Unclaim(a); DropCrates(a);
            a.Waypoints.Clear(); a.Hiking = false; a.SpotLift = 0f; a.SpotHouse = -1; a.SpotIndex = -1;
            Carry(a, plan);
            return true;
        }

        /// <summary>A visit to a place for a reason: a free spot there, on foot or by train. False if there is none or it is out of reach.</summary>
        public bool PlanVisit(int villager, int house, Need why, out Intent plan)
        {
            plan = Intent.Nothing;
            var a = agents[villager];
            var p = PlaceAt(house);
            if (p == null) return false;
            int spot = FreeSpot(p, a);
            if (spot < 0) return false;
            plan = new Intent { What = Intent.Kind.Spot, For = why, House = house, Spot = spot, Target = p.Approach[spot], FromStation = -1, ToStation = -1, From = -1, Place = p.Bp.Name };
            int mine = Grid.RegionAt(a.Position);
            return Grid.SameRegion(mine, p.Region[spot]) || TrainRoute(mine, p.Region[spot], a.Position, plan.Target, out plan.FromStation, out plan.ToStation);
        }

        /// <summary>Joining a crowd around a place (no spot needed: a fête, a band playing). False if it can't be walked to.</summary>
        public bool PlanGather(int villager, int house, Need why, out Intent plan)
        {
            plan = Intent.Nothing;
            var a = agents[villager];
            var p = PlaceAt(house);
            if (p == null) return false;
            float ring = Mathf.Max(p.Bp.Half.x, p.Bp.Half.y) + 0.035f;
            for (int k = 0; k < 8; k++)
            {
                float ang = a.Rng.Range(0f, Mathf.PI * 2f), r = ring + a.Rng.Range(0f, 0.06f);
                var at = p.Centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                if (!Walkable(at) || !Grid.Reachable(a.Position, at)) continue;
                plan = new Intent { What = Intent.Kind.Gather, For = why, House = house, Spot = -1, Target = at, FromStation = -1, ToStation = -1, From = -1, Place = p.Bp.Name };
                return true;
            }
            return false;
        }

        /// <summary>Home (on foot or by train). False without a home they can get to.</summary>
        public bool PlanHome(int villager, out Intent plan)
        {
            var a = agents[villager];
            plan = a.HomeHouse >= 0 && a.HomeHouse < places.Count ? Home(a, Grid.RegionAt(a.Position), Intent.Kind.Home) : Intent.Nothing;
            return plan.What != Intent.Kind.None;
        }

        /// <summary>Places that serve a need (building indices), nearest a point first.</summary>
        public List<int> PlacesFor(Need need, Vector2 near)
        {
            RefreshPlaces();
            var list = new List<int>();
            foreach (var p in places) if (p.Bp.Serves == need && !p.Bp.Dwelling) list.Add(p.House);
            list.Sort((x, y) => (places[x].Centre - near).sqrMagnitude.CompareTo((places[y].Centre - near).sqrMagnitude));
            return list;
        }
    }

    /// <summary>
    /// A fête: for a couple of hours the village gathers round one place (the fountain, the bandstand, the green by
    /// the town hall). The first outside decider - it only hands out plans; walking there, the crowd, friendships made
    /// in it are the villagers' ordinary doing. Most people come; some carry on with their day.
    /// </summary>
    public sealed class VillageFete : IVillageDecider
    {
        public const float Hours = 2.5f;
        private readonly WonderlandWorld world;
        private readonly Vector2 at;
        private readonly float until;
        private readonly string name;
        private bool ended;

        public VillageFete(WonderlandWorld world, int house)
        {
            this.world = world;
            var h = world.Buildings.Houses[house];
            at = new Vector2(h.x, h.z);
            name = Catalog.Get(h.variant).Name.ToLower();
            until = world.Clock.Total + Hours;
        }

        /// <summary>Word goes round: whoever is free (not at work, not asleep) drops what they are doing and comes.</summary>
        public void Invite()
        {
            var people = world.People;
            for (int i = 0; i < people.People.Count; i++)
                if (people.IsFree(i) && people.StateOf(i) != PeopleSystem.Activity.Working && Decide(people, i, out var plan)) people.Direct(i, plan);
        }

        public bool Active
        {
            get
            {
                if (ended) return false;
                if (world.Clock.Total < until && House >= 0 && !VillageClock.IsNight(world.Clock.Hour)) return true;
                ended = true;
                world.People.Tell($"The fête at the {name} is over");
                return false;
            }
        }

        /// <summary>The place it is held at (followed by where it stands, as buildings come and go), or -1.</summary>
        public int House => world.Buildings.Houses.FindIndex(h => h.x == at.x && h.z == at.y);

        public bool Decide(PeopleSystem people, int villager, out Intent plan)
        {
            plan = Intent.Nothing;
            if ((villager * 7919 + Mathf.FloorToInt(world.Clock.Total * 4f)) % 5 == 0) return false;   // some carry on with their day
            return people.PlanGather(villager, House, Need.Social, out plan);
        }

        /// <summary>Where to hold one: the gathering place with most homes around it.</summary>
        public static int Venue(WonderlandWorld world)
        {
            var houses = world.Buildings.Houses;
            int best = -1; float bestScore = 0f;
            for (int i = 0; i < houses.Count; i++)
            {
                var bp = Catalog.Get(houses[i].variant);
                if (bp.Serves != Need.Social && bp.Id != Catalog.Bandstand) continue;
                float score = bp.Small ? 0.5f : 1f;
                foreach (var h in houses)
                    if (Catalog.Get(h.variant).Dwelling && (new Vector2(h.x - houses[i].x, h.z - houses[i].z)).magnitude < 0.7f) score += 1f;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
    }
}
