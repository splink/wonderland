using UnityEngine;

namespace Wonderland
{
    public enum DayPart { Night, Morning, Midday, Afternoon, Evening }
    public enum ClockMode { Normal, Fast, Paused }

    /// <summary>
    /// The village's time of day. A day passes in under eight minutes (the small hours, when everyone is asleep, go by
    /// three times faster), so a play session sees mornings at the bakery, busy afternoons at the fair and quiet nights
    /// with lit windows. Everything that keeps a routine reads it: villagers, the rides, the light. Saved with the world.
    /// </summary>
    public sealed class VillageClock
    {
        public const float SecondsPerHour = 22.5f;          // daytime, at normal speed
        public const float NightSpeed = 3f, FastSpeed = 4f;
        public const float StartHour = 8f;

        public float Hour = StartHour;                       // 0..24
        public int Day;                                      // days since the world began (novelty, statistics)
        public ClockMode Mode;

        public static bool IsNight(float hour) => hour >= 22f || hour < 6f;

        /// <summary>Hours since the world began (handy for "how long ago").</summary>
        public float Total => Day * 24f + Hour;

        public DayPart Part => PartAt(Hour);

        public static DayPart PartAt(float hour) => hour < 6f ? DayPart.Night : hour < 11f ? DayPart.Morning : hour < 14f ? DayPart.Midday
            : hour < 18f ? DayPart.Afternoon : hour < 22f ? DayPart.Evening : DayPart.Night;

        public bool Night => IsNight(Hour);

        /// <summary>0 at night, 1 in full day, easing through dawn (5-7) and dusk (19.5-21.5).</summary>
        public float Daylight => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 7f, Hour)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(19.5f, 21.5f, Hour)));

        /// <summary>In-game hours that pass in <paramref name="seconds"/> of real time from now (ignores wrapping into night).</summary>
        public float HoursPer(float seconds) => Mode == ClockMode.Paused ? 0f : seconds / SecondsPerHour * (Mode == ClockMode.Fast ? FastSpeed : 1f) * (Hour >= 23.5f || Hour < 5f ? NightSpeed : 1f);

        public void Tick(float dt)
        {
            float h = HoursPer(dt);
            if (h <= 0f) return;
            Hour += h;
            while (Hour >= 24f) { Hour -= 24f; Day++; }
        }

        public string Label => $"{Mathf.FloorToInt(Hour):00}:{Mathf.FloorToInt(Hour % 1f * 6f) * 10:00}";

        public static string PartName(DayPart p) => p switch
        {
            DayPart.Night => "night", DayPart.Morning => "morning", DayPart.Midday => "midday", DayPart.Afternoon => "afternoon", _ => "evening",
        };
    }
}
