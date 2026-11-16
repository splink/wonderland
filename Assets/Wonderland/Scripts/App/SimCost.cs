using System.Diagnostics;

namespace Wonderland
{
    /// <summary>
    /// What each simulation system costs per frame on the main thread (smoothed), measured around its update with the
    /// high-resolution timer: two timestamps per system per frame, nothing else. For the Stats panel and the perf log.
    /// </summary>
    public static class SimCost
    {
        public const int Trains = 0, People = 1, Traffic = 2, Animals = 3, Sound = 4, Cameras = 5, Sky = 6, Buildings = 7, Boats = 8, Count = 9;
        public static readonly string[] Names = { "trains", "villagers", "traffic", "animals", "sound", "cameras", "sky", "rides", "boats" };
        public static readonly float[] Ms = new float[Count];

        public static long Now => Stopwatch.GetTimestamp();

        public static void Add(int system, long started)
        {
            float ms = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
            Ms[system] += (ms - Ms[system]) * 0.05f;
        }

        public static float Total { get { float t = 0f; foreach (var m in Ms) t += m; return t; } }
    }
}
