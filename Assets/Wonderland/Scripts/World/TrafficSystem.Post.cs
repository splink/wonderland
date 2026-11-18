using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The post: a post office by a road keeps a red van. Twice a day (half past nine, half past two) it goes round:
    /// every letterbox the roads reach is emptied - the van pulls in and a sack is carried across - then the post is
    /// taken to a few homes, and the van comes back to the post office. Letterboxes without a road to them, or a post
    /// office without letterboxes, simply have a quieter round.
    /// </summary>
    public sealed partial class TrafficSystem
    {
        public static readonly float[] RoundHours = { 9.5f, 14.5f };
        public const float PostStop = 2.4f;

        /// <summary>A post round: the stops in order, which one it is at, and what it does there.</summary>
        public sealed class Round
        {
            public readonly List<(Vector2 kerb, Vector2 door, string what, bool collect)> Stops = new();
            public int Next;
            public bool AtStop;
            public float Timer;
            public int Collected;
        }

        /// <summary>A post van: on a round, or (at the times of day) starting one.</summary>
        private void StepPost(Vehicle v, float dt)
        {
            var r = v.Round;
            if (r == null)
            {
                float hour = world.Clock.Hour;
                int slot = -1;
                for (int k = 0; k < RoundHours.Length; k++) if (hour >= RoundHours[k] && hour < RoundHours[k] + 1.5f) slot = k;
                int key = world.Clock.Day * 4 + slot;
                if (slot < 0 || key == v.LastRound) return;
                v.LastRound = key;
                v.Round = r = PlanRound(v);
                if (r.Stops.Count == 0) { v.Round = null; return; }
                NextStop(v);
                return;
            }
            if (!r.AtStop) return;
            var stop = r.Stops[r.Next];
            r.Timer -= dt;
            float t = 1f - Mathf.Clamp01(r.Timer / PostStop);
            ShowHop(v, stop.door, stop.collect ? t : 1f - t);
            if (r.Timer > 0f) return;
            ShowHop(v, stop.door, -1f);
            if (stop.collect) r.Collected++;
            r.AtStop = false;
            r.Next++;
            NextStop(v);
        }

        /// <summary>The letterboxes the roads reach (nearest first, one after another), then a few homes, then back.</summary>
        private Round PlanRound(Vehicle v)
        {
            var r = new Round();
            var houses = world.Buildings.Houses;
            var boxes = new List<(Vector2 kerb, Vector2 door, string what, bool collect)>();
            var homes = new List<(Vector2 kerb, Vector2 door, string what, bool collect)>();
            for (int i = 0; i < houses.Count; i++)
            {
                var bp = Catalog.Get(houses[i].variant);
                if (bp.Id != Catalog.Letterbox && !bp.Dwelling) continue;
                var door = bp.Id == Catalog.Letterbox ? new Vector2(houses[i].x, houses[i].z) : PeopleSystem.DoorStep(houses[i]);
                if (Kerb(door) is not { } kerb || (kerb - door).magnitude > 0.12f) continue;
                if (bp.Id == Catalog.Letterbox)
                {
                    int near = -1; float nd = 0.3f;
                    for (int k = 0; k < houses.Count; k++)
                    {
                        if (k == i || Catalog.Get(houses[k].variant).Small) continue;
                        float d = (new Vector2(houses[k].x, houses[k].z) - door).magnitude;
                        if (d < nd) { nd = d; near = k; }
                    }
                    string by = near >= 0 ? $"by the {Catalog.Get(houses[near].variant).Name.ToLower()}" : "on the corner";
                    boxes.Add((kerb, door, $"emptying the letterbox {by}", true));
                }
                else homes.Add((kerb, door, $"delivering the post to the {bp.Name.ToLower()}", false));
            }
            // Nearest one after another from the post office, all the letterboxes; then up to four homes.
            var at = v.Home;
            void Take(List<(Vector2 kerb, Vector2 door, string what, bool collect)> from, int most)
            {
                for (int n = 0; n < most && from.Count > 0; n++)
                {
                    int best = 0;
                    for (int k = 1; k < from.Count; k++) if ((from[k].kerb - at).sqrMagnitude < (from[best].kerb - at).sqrMagnitude) best = k;
                    r.Stops.Add(from[best]); at = from[best].kerb; from.RemoveAt(best);
                }
            }
            Take(boxes, 12);
            Take(homes, 4);
            return r;
        }

        /// <summary>On to the next stop (one it can't reach is skipped); after the last, home.</summary>
        private void NextStop(Vehicle v)
        {
            var r = v.Round;
            while (r.Next < r.Stops.Count)
            {
                var kerb = r.Stops[r.Next].kerb;
                if ((kerb - v.Position).magnitude < 0.04f) { PostArrived(v); return; }
                if (DriveTo(v, kerb, false)) return;
                r.Next++;
            }
            v.Round = null;
            Trips++; Rounds++;
            if ((v.Home - v.Position).magnitude < 0.08f || !DriveTo(v, v.Home, true)) GoHome(v, true);
        }

        private void PostArrived(Vehicle v)
        {
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            Place(v, v.Position + new Vector2(fwd.y, -fwd.x) * 0.006f, v.Heading);
            v.State = Mode.Parked; v.Speed = 0f; v.Path.Clear();
            v.Round.AtStop = true; v.Round.Timer = PostStop;
        }

        /// <summary>Post rounds completed (tests).</summary>
        public int Rounds { get; private set; }

        private string PostLine(Vehicle v)
        {
            var r = v.Round;
            if (r == null) return v.State == Mode.Driving ? "The post van - back to the post office" : $"The post van - at the post office (rounds at {HourText(RoundHours[0])} and {HourText(RoundHours[1])})";
            var stop = r.Stops[Mathf.Min(r.Next, r.Stops.Count - 1)];
            int boxesLeft = 0; for (int k = r.Next; k < r.Stops.Count; k++) if (r.Stops[k].collect) boxesLeft++;
            string doing = r.AtStop ? stop.what : stop.collect ? $"on its round - {boxesLeft} letterbox{(boxesLeft == 1 ? "" : "es")} to empty" : "on its round - taking the post to people's homes";
            return $"The post van - {doing}";
        }

        private static string HourText(float h) => $"{Mathf.FloorToInt(h)}:{Mathf.RoundToInt(h % 1f * 60f):00}";

        /// <summary>For a letterbox's card: when it is emptied, and whether the van is on its way.</summary>
        public string PostFor(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count || world.Buildings.Houses[house].variant != Catalog.Letterbox) return null;
            var h = world.Buildings.Houses[house];
            var at = new Vector2(h.x, h.z);
            bool van = false;
            foreach (var v in Vehicles)
            {
                if (v.Kind != VehicleKind.Van || v.Depot < 0) continue;
                van = true;
                if (v.Round == null) continue;
                for (int k = v.Round.Next; k < v.Round.Stops.Count; k++) if (v.Round.Stops[k].door == at) return k == v.Round.Next && v.Round.AtStop ? "the post van is emptying it now" : "the post van is on its way";
            }
            return van ? $"emptied at {HourText(RoundHours[0])} and {HourText(RoundHours[1])}" : "no post office by a road yet";
        }
    }
}
