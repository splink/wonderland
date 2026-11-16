using UnityEngine;

namespace Wonderland
{
    /// <summary>What a villager wants, and what a place offers. Places serve one need; homes serve Rest (and sleep).</summary>
    public enum Need { Errands, Food, Social, Fun, Sights, Rest, Work, None }

    /// <summary>
    /// The village's daily rhythm, as data: how much each need appeals at each hour (shops in the morning, the café at
    /// lunch, the fair in the afternoon, the square in the evening), how fast each need grows back, and how people talk
    /// about it. Decisions read these tables; nothing here knows about any particular building.
    /// </summary>
    public static class Needs
    {
        public const int Count = 7;

        //                                        0h    5.5h  7h    9h    12.5h 16h   19.5h 21.5h 24h
        private static readonly float[] Hours = { 0f, 5.5f, 7f, 9f, 12.5f, 16f, 19.5f, 21.5f, 24f };
        private static readonly float[][] Curve =
        {
            new[] { 0f, 0f, 0.6f, 1f, 0.6f, 0.45f, 0.12f, 0f, 0f },        // Errands: the morning shop
            new[] { 0f, 0f, 0.35f, 0.4f, 1f, 0.55f, 0.75f, 0.1f, 0f },     // Food: lunch, and supper out
            new[] { 0f, 0f, 0.2f, 0.35f, 0.45f, 0.5f, 1f, 0.3f, 0f },      // Social: the evening square
            new[] { 0f, 0f, 0f, 0.15f, 0.6f, 1f, 0.8f, 0.1f, 0f },         // Fun: the afternoon fair
            new[] { 0f, 0f, 0.4f, 0.6f, 0.5f, 0.9f, 0.5f, 0f, 0f },        // Sights: walks and views
            new[] { 1f, 1f, 0.25f, 0.1f, 0.3f, 0.25f, 0.5f, 1f, 1f },      // Rest: home, a bench
            new[] { 0f, 0f, 0.3f, 1f, 0.8f, 0.9f, 0f, 0f, 0f },            // Work: shop hours
        };

        /// <summary>How fast each need grows back, per in-game hour.</summary>
        private static readonly float[] Growth = { 0.07f, 0.16f, 0.11f, 0.1f, 0.09f, 0.08f, 0.35f };

        public static float Appeal(Need n, float hour) => Appeal(Curve[(int)n], hour);

        /// <summary>How much a place appeals now: its own rhythm if it has one (the disco), else its need's.</summary>
        public static float Appeal(Blueprint bp, float hour) => bp.Rhythm != null ? Appeal(bp.Rhythm, hour) : Appeal(bp.Serves, hour);

        private static float Appeal(float[] c, float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            for (int i = 1; i < Hours.Length; i++)
                if (hour <= Hours[i]) return Mathf.Lerp(c[i - 1], c[i], Mathf.InverseLerp(Hours[i - 1], Hours[i], hour));
            return c[^1];
        }

        public static float GrowthOf(Need n) => Growth[(int)n];

        /// <summary>Why someone goes somewhere, as they would put it.</summary>
        public static string Reason(Need n, DayPart part) => n switch
        {
            Need.Errands => part == DayPart.Morning ? "the morning shopping" : "a bit of shopping",
            Need.Food => part == DayPart.Midday ? "lunch" : part == DayPart.Evening ? "supper" : "a bite to eat",
            Need.Social => part == DayPart.Evening ? "the evening company" : "a chat",
            Need.Fun => "some fun",
            Need.Sights => "the view",
            Need.Rest => "a rest",
            Need.Work => "work",
            _ => "a look around",
        };

        /// <summary>A villager's own leanings (0.6-1.4 per need), fixed by their seed: some love the fair, some the café.</summary>
        public static float Affinity(int seed, Need n)
        {
            uint h = (uint)seed * 2654435761u ^ (uint)((int)n + 1) * 40503u;
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return 0.6f + (h & 0xffff) / 65535f * 0.8f;
        }

        private static readonly string[] Names =
        {
            "Ada", "Ben", "Clara", "Dev", "Elsa", "Finn", "Greta", "Hugo", "Ida", "Jonas", "Kit", "Lena", "Milo", "Nora", "Otto", "Pia",
            "Quinn", "Rosa", "Sami", "Tilda", "Ugo", "Vera", "Wim", "Xena", "Yara", "Zeno", "Alma", "Bruno", "Cleo", "Dora", "Emil", "Frida",
            "Gus", "Hanna", "Ivo", "Juno", "Karl", "Lotte", "Mats", "Nell", "Oskar", "Paula", "Rufus", "Stella", "Theo", "Ulla", "Viggo", "Wanda",
        };

        public static string NameOf(int seed) => Names[(int)((uint)seed * 2246822519u >> 7) % Names.Length];
    }

    /// <summary>
    /// A decision: what someone means to do and why. Choosing produces one (from the needs, the hour and the places in
    /// reach); the people system carries it out. Kept apart so other deciders - a schedule, a story, one day an agent -
    /// can hand villagers intents of their own (see <see cref="IVillageDecider"/>).
    /// </summary>
    public struct Intent
    {
        public enum Kind { None, Spot, Home, MoveIn, Neighbour, Stroll, Hike, TrainTrip, Wander, Sleep, Work, Haul, Gather }
        public Kind What;
        public Need For;
        public int House, Spot;          // the place (building index) and its activity spot (-1: none, e.g. a crowd)
        public Vector2 Target;           // where to walk (the spot's approach, a door, a point)
        public int FromStation, ToStation;   // a train journey: board here, alight there (-1: walk)
        public int From;                 // Haul: the place to collect the goods from first (-1: they set off with them)
        public Vector2 Pickup;           // ...and where to collect them
        public float Score;
        public string Place;             // the place's name, for describing it
        public bool Planned;             // part of their plan for the day

        public bool ByTrain => FromStation >= 0 && ToStation >= 0;
        public static Intent Nothing => new() { What = Kind.None, FromStation = -1, ToStation = -1, House = -1, Spot = -1, From = -1, For = Need.None };
    }

    /// <summary>
    /// Something other than a villager's own needs deciding what they do next: a fête, a story, a schedule, one day an
    /// outside agent. Asked (in order) whenever a villager is free to choose and it is not their bedtime; a plan it
    /// returns is checked (the place still there, the spot free, the way there walkable or by train) and then carried
    /// out like any other. Build plans with the PeopleSystem's Plan* helpers.
    /// </summary>
    public interface IVillageDecider
    {
        /// <summary>False once it has nothing more to say (it is then dropped).</summary>
        bool Active { get; }
        bool Decide(PeopleSystem people, int villager, out Intent plan);
    }
}
