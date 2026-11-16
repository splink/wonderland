using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Friendships, grown from time spent together: two villagers at the same place at the same time (the café's
    /// tables, the fountain, the fair, a fête, a chat at the door) get to know each other, and after a while become
    /// friends. Friends then seek each other out: a place where a friend is (or is heading) draws more, a visit to a
    /// neighbour is a visit to a friend, who comes out to chat. Friendships are saved; acquaintance is not.
    /// </summary>
    public sealed partial class PeopleSystem
    {
        public const float FriendAfter = 45f;      // seconds (real time) spent together
        public const int MaxFriends = 5;

        private readonly Dictionary<long, float> acquaintance = new();
        private readonly Dictionary<int, List<Agent>> together = new();
        private readonly Stack<List<Agent>> spareLists = new();
        private float friendsTimer;

        private static long PairKey(Agent x, Agent y)
        {
            uint a = (uint)x.Seed, b = (uint)y.Seed;
            return a < b ? ((long)a << 32) | b : ((long)b << 32) | a;
        }

        /// <summary>Once a second: who is spending time with whom.</summary>
        private void StepFriendships(float dt)
        {
            friendsTimer -= dt;
            if (friendsTimer > 0f) return;
            float span = 1f - friendsTimer;
            friendsTimer = 1f;
            foreach (var list in together.Values) { list.Clear(); spareLists.Push(list); }
            together.Clear();
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                if (a.SpotHouse < 0 || !AtPlace(i) || a.State == Activity.Sleeping || a.State == Activity.Working) continue;
                if (a.Hidden && a.State == Activity.Inside && a.Plan.What != Intent.Kind.Spot) continue;   // at home, not together
                if (!together.TryGetValue(a.SpotHouse, out var list)) together[a.SpotHouse] = list = spareLists.Count > 0 ? spareLists.Pop() : new List<Agent>();
                list.Add(a);
            }
            foreach (var kv in together)
            {
                var list = kv.Value;
                for (int x = 0; x < list.Count; x++)
                for (int y = x + 1; y < list.Count; y++)
                {
                    var a = list[x]; var b = list[y];
                    if (a.Friends.Contains(b)) continue;
                    float close = a.State == b.State && (a.State == Activity.Sitting || a.State == Activity.Gathering || a.State == Activity.Chatting) ? 1.5f : 1f;
                    long key = PairKey(a, b);
                    float known = (acquaintance.TryGetValue(key, out float k) ? k : 0f) + span * close;
                    acquaintance[key] = known;
                    if (known < FriendAfter || a.Friends.Count >= MaxFriends || b.Friends.Count >= MaxFriends) continue;
                    a.Friends.Add(b); b.Friends.Add(a);
                    acquaintance.Remove(key);
                    var bp = Catalog.Get(world.Buildings.Houses[kv.Key].variant);
                    News = $"{a.Name} and {b.Name} have become friends at the {bp.Name.ToLower()}";
                }
            }
        }

        /// <summary>A friend at (or on the way to) a place.</summary>
        private Agent FriendAt(Agent a, int house)
        {
            foreach (var f in a.Friends) if (f.SpotHouse == house && f.State != Activity.Sleeping) return f;
            return null;
        }

        /// <summary>A friend at home and awake, to call on.</summary>
        private Agent FriendIn(Agent a)
        {
            if (a.Friends.Count == 0) return null;
            var f = a.Friends[a.Rng.Range(0, a.Friends.Count)];
            return f.HomeHouse >= 0 && f.State == Activity.Inside && f.SpotHouse == f.HomeHouse && f.HomeHouse != a.HomeHouse ? f : null;
        }

        /// <summary>Calling on a friend: they come out to the door to chat.</summary>
        private void ComeOut(Agent visitor)
        {
            foreach (var f in visitor.Friends)
            {
                if (f.HomeHouse != visitor.SpotHouse || f.State != Activity.Inside || !f.Hidden) continue;
                Hide(f, false);
                f.Position = f.Approach + (visitor.Position - f.Approach).normalized * 0.002f; f.Height = GroundAt(f.Position);
                var to = visitor.Position - f.Position;
                f.Heading = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                visitor.Heading = f.Heading + 180f;
                f.Plan = new Intent { What = Intent.Kind.Neighbour, For = Need.Social, House = f.HomeHouse, Spot = -1, Target = f.Position, FromStation = -1, ToStation = -1, From = -1, Place = visitor.Plan.Place };
                f.SpotHouse = f.HomeHouse; f.SpotIndex = -1;
                f.State = Activity.Chatting; f.Timer = visitor.Timer;
                Pose(f);
                return;
            }
        }

        /// <summary>Friends who are at the same place now, for describing it ("with Ben and Ida").</summary>
        private string Company(Agent a)
        {
            if (a.SpotHouse < 0 || a.Friends.Count == 0) return "";
            List<string> names = null;
            foreach (var f in a.Friends)
                if (f.SpotHouse == a.SpotHouse && f.State != Activity.Walking && f.State != Activity.Sleeping) (names ??= new List<string>()).Add(f.Name);
            if (names == null) return "";
            return " with " + (names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[^1]);
        }

        /// <summary>Friends, after loading: their seeds back to villagers.</summary>
        private void LinkFriends()
        {
            var bySeed = new Dictionary<int, Agent>();
            foreach (var a in agents) bySeed.TryAdd(a.Seed, a);
            for (int i = 0; i < agents.Count; i++)
            {
                var seeds = People[i].friends;
                if (seeds == null) continue;
                foreach (int s in seeds)
                    if (bySeed.TryGetValue(s, out var f) && f != agents[i] && !agents[i].Friends.Contains(f) && agents[i].Friends.Count < MaxFriends) agents[i].Friends.Add(f);
            }
        }

        private static int[] FriendSeeds(Agent a)
        {
            if (a.Friends.Count == 0) return null;
            var seeds = new int[a.Friends.Count];
            for (int k = 0; k < seeds.Length; k++) seeds[k] = a.Friends[k].Seed;
            return seeds;
        }

        public int FriendCount(int i) => agents[i].Friends.Count;
        public bool AreFriends(int i, int j) => agents[i].Friends.Contains(agents[j]);

        /// <summary>The names of a villager's friends.</summary>
        public string FriendsOf(int i)
        {
            var a = agents[i];
            if (a.Friends.Count == 0) return null;
            var names = new List<string>();
            foreach (var f in a.Friends) names.Add(f.Name);
            return string.Join(", ", names);
        }
    }
}
