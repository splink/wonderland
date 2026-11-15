using System.Text;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Lightweight on-device performance log: every 5 s, one logcat line (tag Unity, prefix "Wonderland perf") with the
    /// frame rate, frame-time average / 95th percentile / worst, the share of frames over budget, and CPU main / render
    /// and GPU times from Unity's FrameTimingManager (enabled in the player settings). Lets any play session produce
    /// numbers without a trace capture: metavr log --tag Unity | grep "Wonderland perf".
    /// </summary>
    public sealed class PerfLog : MonoBehaviour
    {
        private const float Window = 5f;
        private readonly float[] samples = new float[1024];
        private int count;
        private float elapsed;
        private double cpuMain, cpuRender, gpu;
        private int timingFrames;
        private readonly FrameTiming[] timings = new FrameTiming[1];
        public System.Func<string> Context;   // what the world looks like (trees, houses, land) for the log line

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (count < samples.Length) samples[count++] = dt;
            elapsed += dt;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                cpuMain += timings[0].cpuMainThreadFrameTime; cpuRender += timings[0].cpuRenderThreadFrameTime; gpu += timings[0].gpuFrameTime;
                timingFrames++;
            }
            if (elapsed < Window) return;
            Report();
            elapsed = 0f; count = 0; cpuMain = cpuRender = gpu = 0; timingFrames = 0;
        }

        private void Report()
        {
            if (count == 0) return;
            float refresh = 72f;
            try { if (OVRManager.display != null && OVRManager.display.displayFrequency > 1f) refresh = OVRManager.display.displayFrequency; } catch (System.Exception) { }
            float budget = 1f / refresh;
            var sorted = new float[count];
            System.Array.Copy(samples, sorted, count);
            System.Array.Sort(sorted);
            float sum = 0f; int over = 0;
            foreach (float s in sorted) { sum += s; if (s > budget * 1.25f) over++; }
            var sb = new StringBuilder("Wonderland perf: ");
            sb.Append($"{count / elapsed:0.0} fps (target {refresh:0}), frame avg {sum / count * 1000f:0.0} ms, p95 {sorted[(int)(count * 0.95f)] * 1000f:0.0} ms, worst {sorted[count - 1] * 1000f:0.0} ms, {over * 100f / count:0.0}% over budget");
            if (timingFrames > 0) sb.Append($"; cpu main {cpuMain / timingFrames:0.0} ms, render {cpuRender / timingFrames:0.0} ms, gpu {gpu / timingFrames:0.0} ms");
            if (Context != null) sb.Append("; ").Append(Context());
            Debug.Log(sb.ToString());
        }
    }
}
