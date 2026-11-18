using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Taking a taxi (see TrafficSystem.Taxi.cs): a grown-up going somewhere far by day, with a free taxi about and roads
    /// at both ends, sometimes hails one instead of walking. They walk to the kerb and wait; the taxi pulls up; they get
    /// in (and ride, visibly, in the back - their place in the world is the taxi's); at the far kerb they get out and
    /// walk the rest of the way. A taxi that doesn't come, or goes, leaves them to walk.
    /// </summary>
    public sealed partial class PeopleSystem
    {
        public const float TaxiFrom = 0.8f;      // walks further than this may be by taxi

        /// <summary>Always take a taxi when one can be had (tests).</summary>
        public bool PreferTaxi;

        /// <summary>Whether to go by taxi; if so they set off for the kerb (true).</summary>
        private bool TryTaxi(Agent a, Intent plan)
        {
            if (a.Child || world.Traffic == null) return false;
            if (plan.What is not (Intent.Kind.Spot or Intent.Kind.Work or Intent.Kind.Home or Intent.Kind.Sleep or Intent.Kind.Neighbour or Intent.Kind.Gather)) return false;
            if (plan.House < 0 || Clock.Hour < 6.5f || Clock.Hour > 23f) return false;
            float far = (plan.Target - a.Position).magnitude;
            if (far < TaxiFrom || !PreferTaxi && !a.Rng.Chance(Mathf.Clamp01(0.35f + (far - TaxiFrom) * 0.6f))) return false;
            string where = plan.What is Intent.Kind.Home or Intent.Kind.Sleep ? "home" : "the " + (plan.Place ?? Catalog.Get(world.Buildings.Houses[plan.House].variant).Name).ToLower();
            var kerb = world.Traffic.Hail(a.Seed, a.Style, a.Name, where, a.Position, plan.Target);
            if (kerb == null) return false;
            var stand = FindNear(kerb.Value, 0.05f, out var near) ? near : kerb.Value;
            a.Leg = Leg.ToTaxi;
            GoTo(a, stand, false);
            return true;
        }

        /// <summary>At the kerb, waiting: in when the taxi pulls up; if it doesn't, they walk.</summary>
        private void StepHailing(Agent a, float dt)
        {
            if (world.Traffic.Board(a.Seed, a.Position))
            {
                a.State = Activity.InTaxi;
                if (world.Traffic.Riding(a.Seed) is { } seat) a.Position = seat;   // (in the taxi, where it is)
                a.Approach = a.Position;
                Hide(a, true);
                TaxiRides++;
                return;
            }
            if (world.Traffic.Hailed(a.Seed) && a.Timer > 0f && SpotStillThere(a)) return;
            world.Traffic.CancelFare(a.Seed);
            a.Leg = Leg.Direct;
            if (SpotStillThere(a)) GoTo(a, a.Plan.Target, false); else Stop(a);
        }

        /// <summary>In the back of the taxi: they are where it is; at the far kerb they get out and walk the rest.</summary>
        private void StepInTaxi(Agent a)
        {
            if (world.Traffic.Arrived(a.Seed, out var drop))
            {
                GetOut(a, drop);
                a.Leg = Leg.Direct;
                if (SpotStillThere(a)) GoTo(a, a.Plan.Target, false); else Stop(a, 1f);
                return;
            }
            if (world.Traffic.Riding(a.Seed) is { } at) { a.Position = a.Approach = at; return; }
            GetOut(a, a.Position);   // the taxi went (taken away): out here, and on foot
            Stop(a, 1f);
        }

        private void GetOut(Agent a, Vector2 at)
        {
            Hide(a, false);
            a.Position = FindNear(at, 0.06f, out var near) ? near : a.Home;
            a.Height = a.Ground = GroundAt(a.Position); a.GroundPos = a.Position;
            a.State = Activity.Idle;
        }

        /// <summary>Taxi fares taken by villagers (tests).</summary>
        public int TaxiRides { get; private set; }

        /// <summary>Whether villager <paramref name="i"/> is in a taxi now (tests, cards).</summary>
        public bool InTaxi(int i) => agents[i].State == Activity.InTaxi;
    }
}
