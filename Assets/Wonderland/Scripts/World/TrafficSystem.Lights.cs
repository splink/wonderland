using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// Traffic lights at every junction where three or more roads meet. Arms facing each other share a phase (the
    /// through road of a T, both roads of a crossroads): green, amber, then a moment of all red before the next phase.
    /// Vehicles coming in on a red (or an amber they can still stop for) halt with their bumper at the stop line; once
    /// past it they carry on. Sirens go through. The lamps are one small mesh, rebuilt only when a light changes.
    /// </summary>
    public sealed partial class TrafficSystem
    {
        public enum Signal { Red, Amber, Green }
        public const float GreenSeconds = 6f, AmberSeconds = 1.6f, AllRedSeconds = 1.2f;

        private float lightClock;
        private Mesh lampMesh;
        private long lampKey = -1;

        /// <summary>Times a vehicle crossed a stop line on red (tests: never, sirens apart).</summary>
        public int RedRuns { get; private set; }
        public string LastRedRun { get; private set; }
        /// <summary>Times a vehicle halted for a light.</summary>
        public int LightStops { get; private set; }

        /// <summary>What the light on arm <paramref name="arm"/> of junction <paramref name="junction"/> shows now.</summary>
        public Signal SignalFor(int junction, int arm)
        {
            var junctions = world.Paths.Junctions;
            if (junction < 0 || junction >= junctions.Count) return Signal.Green;
            var j = junctions[junction];
            if (!j.Lights) return Signal.Green;
            const float phase = GreenSeconds + AmberSeconds + AllRedSeconds;
            float t = Mathf.Repeat(lightClock + junction * 3.7f, j.Phases * phase);
            int active = Mathf.FloorToInt(t / phase);
            float within = t - active * phase;
            if (j.Phase[arm] != active) return Signal.Red;
            return within < GreenSeconds ? Signal.Green : within < GreenSeconds + AmberSeconds ? Signal.Amber : Signal.Red;
        }

        /// <summary>The arm of a junction a point lies on (the one pointing most towards it).</summary>
        private static int ArmTowards(PathSystem.RoadJunction j, Vector2 p)
        {
            var d = (p - j.At).normalized;
            int best = 0; float bestDot = -2f;
            for (int k = 0; k < j.Arms.Count; k++) { float dot = Vector2.Dot(j.Arms[k], d); if (dot > bestDot) { bestDot = dot; best = k; } }
            return best;
        }

        /// <summary>How fast a vehicle may go for the lights ahead: stopping at the stop line on red (and amber, if it can).</summary>
        private float LightAhead(Vehicle v, Vector2 fwd, float half)
        {
            if (v.Siren) return float.MaxValue;
            float limit = float.MaxValue;
            var junctions = world.Paths.Junctions;
            for (int i = 0; i < junctions.Count; i++)
            {
                var j = junctions[i];
                if (!j.Lights) continue;
                var to = j.At - v.Position;
                float dist = to.magnitude;
                if (dist - half > j.Radius + 0.09f || dist < j.Radius + 0.004f) continue;   // far off, or already over the line
                if (Vector2.Dot(fwd, to) < dist * 0.35f) continue;                           // not heading in
                var signal = SignalFor(i, ArmTowards(j, v.Position));
                if (signal == Signal.Green) continue;
                float stop = dist - (j.Radius + 0.007f) - half;                       // front bumper to the stop line
                if (stop < -0.001f) continue;                                         // its nose is over the line already: clear the junction
                if (signal == Signal.Amber && stop < v.Speed * v.Speed / (2f * 0.12f) && v.Speed > 0.006f) continue;   // too close to stop for amber: go on
                float allowed = stop <= 0.001f ? 0f : Mathf.Sqrt(2f * 0.12f * stop) * 0.85f;
                if (allowed <= 0f && v.Speed > 0.004f) LightStops++;
                limit = Mathf.Min(limit, allowed);
            }
            return limit;
        }

        /// <summary>Notes a vehicle crossing into a junction on red (for the tests: it never should).</summary>
        private void WatchStopLines(Vehicle v, Vector2 before)
        {
            if (v.Siren) return;
            var junctions = world.Paths.Junctions;
            for (int i = 0; i < junctions.Count; i++)
            {
                var j = junctions[i];
                if (!j.Lights) continue;
                // The front bumper crossing the stop line.
                float line = j.Radius + 0.007f, half = VehicleFactory.Length(v.Kind) * 0.5f;
                var fwd = (v.Position - before).normalized;
                float was = (before + fwd * half - j.At).magnitude, now = (v.Position + fwd * half - j.At).magnitude;
                if (was >= line && now < line && SignalFor(i, ArmTowards(j, before)) == Signal.Red)
                {
                    RedRuns++;
                    LastRedRun = $"{v.Kind} at {v.Speed * 100f:0.0} cm/s, pushing {v.Pushing > 0f}, queue {v.Queue}, at light {v.AtLight}, {(v.Position - j.At).magnitude * 100f:0.0} cm from junction {i} (radius {j.Radius * 100f:0.0})";
                }
            }
        }

        /// <summary>Lamps: red over amber over green on every light, the lit one glowing. Rebuilt when any light changes.</summary>
        private void DrawLights(float dt)
        {
            lightClock += dt;
            var heads = world.Paths.LightHeads;
            long key = heads.Count;
            foreach (var (j, arm, _, _) in heads) key = key * 3 + (int)SignalFor(j, arm);
            if (key == lampKey && lampMesh != null) return;
            lampKey = key;
            if (lampMesh == null)
            {
                var go = new GameObject("Traffic lights");
                go.transform.SetParent(transform, false);
                lampMesh = new Mesh { name = "Traffic lamps" };
                lampMesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = lampMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off;
            }
            var b = new MeshBuilder();
            Color red = Pal.Hex("#FF3B30"), amber = Pal.Hex("#FFB020"), green = Pal.Hex("#3BE06A");
            foreach (var (j, arm, head, facing) in heads)
            {
                var s = SignalFor(j, arm);
                for (int k = 0; k < 3; k++)
                {
                    var colour = k == 0 ? red : k == 1 ? amber : green;
                    bool lit = (k == 0 && s == Signal.Red) || (k == 1 && s == Signal.Amber) || (k == 2 && s == Signal.Green);
                    var at = head + Vector3.up * (0.0045f - k * 0.0045f) + facing * 0.0006f;
                    b.AddFace(at, facing, Vector3.up, 0.0017f, 0.0017f, lit ? Pal.Glow(colour) : Pal.Shade(colour, 0.25f));
                }
            }
            b.ToMesh("Traffic lamps", lampMesh);
            lampMesh.RecalculateBounds();
        }
    }
}
