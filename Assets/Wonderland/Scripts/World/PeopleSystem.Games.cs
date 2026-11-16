using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Games and mishaps. A football pitch with two or more players on it has a match: the ball is passed between them
    /// and now and then shot at a goal, and the players run about their positions following it (left half against
    /// right half). And a villager can feel faint (an incident the ambulance answers): they sit down where they are until
    /// help comes. Plus the small questions pets ask about the people they live with.
    /// </summary>
    public sealed partial class PeopleSystem
    {
        private sealed class Match
        {
            public Transform Ball;
            public Vector2 At, Goal;          // the ball (world-local) and where it is going
            public Agent To;                  // the player it is passed to (null: a shot)
            public float Hop, Speed;
            public int Goals;
        }

        private readonly Dictionary<int, Match> matches = new();
        private readonly List<int> finished = new();
        public int GoalsScored { get; private set; }
        public float BallTravel { get; private set; }   // how far balls have rolled (tests)

        /// <summary>The football pitch someone is playing on, or null.</summary>
        private Place Pitch(Agent a)
        {
            var p = a.SpotHouse >= 0 && a.SpotHouse < places.Count ? places[a.SpotHouse] : null;
            return p != null && p.House == a.SpotHouse && p.Bp.Id == Catalog.Football && a.SpotIndex >= 0 && a.SpotIndex < 6 ? p : null;
        }

        private Vector2 PitchToWorld(Place p, Vector2 local)
        {
            var h = world.Buildings.Houses[p.House];
            var d = Quaternion.Euler(0f, h.rotation, 0f) * new Vector3(local.x, 0f, local.y);
            return p.Centre + new Vector2(d.x, d.z);
        }

        /// <summary>A player follows the ball about their position (never more than 4 cm from it).</summary>
        private void StepPlayer(Agent a, Place p, float dt)
        {
            if (!matches.TryGetValue(p.House, out var m)) { a.Running = false; return; }
            var spot = world.Buildings.SpotPosition(p.House, a.SpotIndex, out _);
            var home = new Vector2(spot.x, spot.z);
            var toBall = m.At - home;
            var want = home + Vector2.ClampMagnitude(toBall * 0.5f, 0.04f);
            if (m.To == a) want = m.At;
            var step = want - a.Position;
            a.Running = step.magnitude > 0.003f;
            if (a.Running) { var d = step.normalized * Mathf.Min(step.magnitude, Speed * 1.3f * dt); a.Position += d; a.Phase += d.magnitude / 0.0105f * Mathf.PI; }
            var look = m.At - a.Position;
            if (look.sqrMagnitude > 1e-6f) a.Heading = Mathf.MoveTowardsAngle(a.Heading, Mathf.Atan2(look.x, look.y) * Mathf.Rad2Deg, 300f * dt);
        }

        /// <summary>The matches: a ball per pitch with players, passed about; goals are cheered.</summary>
        private void StepMatches(float dt)
        {
            foreach (var p in places)
            {
                if (p.Bp.Id != Catalog.Football) continue;
                Agent first = null; int players = 0;
                foreach (var a in agents)
                    if (a.State == Activity.Playing && a.SpotHouse == p.House && Pitch(a) != null) { players++; first ??= a; }
                bool on = matches.TryGetValue(p.House, out var m);
                if (players < 1) { if (on) EndMatch(p.House); continue; }
                if (!on)
                {
                    var ball = new GameObject("Football");
                    ball.transform.SetParent(transform, false);
                    ball.AddComponent<MeshFilter>().sharedMesh = HouseFactory.Ball();
                    ball.AddComponent<MeshRenderer>().sharedMaterial = material;
                    matches[p.House] = m = new Match { Ball = ball.transform, At = p.Centre, To = first, Speed = 0.05f };
                }
                if (m.To != null && (m.To.State != Activity.Playing || m.To.SpotHouse != p.House)) m.To = first;
                var target = m.To != null ? m.To.Position : m.Goal;
                var to = target - m.At;
                float step = Mathf.Min(to.magnitude, m.Speed * dt);
                m.At += to.sqrMagnitude > 1e-8f ? to.normalized * step : Vector2.zero;
                BallTravel += step;
                m.Hop = Mathf.Max(0f, m.Hop - dt * 2f);
                if (to.magnitude < 0.005f)
                {
                    if (m.To == null)
                    {
                        // Goal! Back to the centre spot.
                        m.Goals++; GoalsScored++;
                        world.Sounds?.Cue(SoundCue.Cheer, m.At);
                        m.At = p.Centre; m.To = first;
                    }
                    else
                    {
                        // A kick: pass to someone else, or (a quarter of the time) a shot at the far goal.
                        var kicker = m.To;
                        world.Sounds?.Cue(SoundCue.Kick, m.At);
                        if (Rand(kicker) < 0.25f)
                        {
                            bool left = kicker.SpotIndex < 3;
                            m.Goal = PitchToWorld(p, new Vector2(left ? 0.105f : -0.105f, (Rand(kicker) - 0.5f) * 0.03f));
                            m.To = null; m.Speed = 0.09f; m.Hop = 1f;
                        }
                        else
                        {
                            if (players == 1) { m.Goal = PitchToWorld(p, new Vector2(kicker.SpotIndex < 3 ? 0.105f : -0.105f, 0f)); m.To = null; m.Speed = 0.07f; m.Hop = 0.6f; continue; }   // a kick-about alone: shots at goal
                            Agent next = null; int k = Mathf.FloorToInt(Rand(kicker) * players), n = 0;
                            foreach (var a in agents)
                                if (a != kicker && a.State == Activity.Playing && a.SpotHouse == p.House && Pitch(a) != null && n++ >= k - 1) { next = a; break; }
                            m.To = next ?? kicker; m.Speed = 0.05f + Rand(kicker) * 0.02f;
                        }
                    }
                }
                var ground = GroundAt(m.At);
                m.Ball.localPosition = new Vector3(m.At.x, ground + 0.003f + Mathf.Sin(m.Hop * Mathf.PI) * 0.012f, m.At.y);
                m.Ball.localRotation = Quaternion.Euler(BallTravel * 4000f, 0f, 0f);
            }
            finished.Clear();
            foreach (var kv in matches) if (kv.Key >= places.Count || places[kv.Key].Bp.Id != Catalog.Football) finished.Add(kv.Key);
            foreach (int house in finished) EndMatch(house);
        }

        private float Rand(Agent a) => a.Rng.Value;

        private void EndMatch(int house)
        {
            if (!matches.TryGetValue(house, out var m)) return;
            if (m.Ball) Release(m.Ball.gameObject);
            matches.Remove(house);
            foreach (var a in agents) if (a.SpotHouse == house) a.Running = false;
        }

        /// <summary>Football matches going on now (and the ball of one, for tests and cameras).</summary>
        public int Matches => matches.Count;
        public Vector2? BallOf(int house) => matches.TryGetValue(house, out var m) ? m.At : null;

        // ------------------------------------------------------------------ mishaps

        /// <summary>Someone out of doors and awake who could feel faint (an incident for the ambulance), or -1.</summary>
        public int SomeoneOutdoors(ref Rng rng, Func<Vector2, bool> reachable = null)
        {
            int start = agents.Count > 0 ? rng.Range(0, agents.Count) : 0;
            for (int k = 0; k < agents.Count; k++)
            {
                var a = agents[(start + k) % agents.Count];
                if (a.Hidden || a.Child || a.State is Activity.Riding or Activity.OnRide or Activity.Sleeping or Activity.Waiting or Activity.Unwell or Activity.Working) continue;
                if (!Walkable(a.Position) && a.State != Activity.Sitting) continue;
                if (reachable != null && !reachable(a.Position)) continue;
                return a.Index;
            }
            return -1;
        }

        /// <summary>They feel faint and sit down where they are, until help comes (or a good while passes).</summary>
        public bool Faint(int i)
        {
            var a = agents[i];
            if (a.Hidden || a.State is Activity.Riding or Activity.OnRide or Activity.Sleeping or Activity.Unwell) return false;
            if (a.State == Activity.Sitting || a.State == Activity.Playing || a.SpotLift > 0f) a.Position = a.Approach;
            Unclaim(a); DropCrates(a);
            a.Waypoints.Clear(); a.Hiking = false; a.Running = false; a.SpotHouse = -1; a.SpotIndex = -1; a.SpotLift = 0f;
            a.Plan = Intent.Nothing;
            a.SpotYaw = a.Heading;
            a.State = Activity.Unwell; a.Timer = 600f;
            Pose(a);
            return true;
        }

        /// <summary>Back on their feet (the ambulance came), and home for a rest.</summary>
        public void Recover(int i)
        {
            var a = agents[i];
            if (a.State != Activity.Unwell) return;
            a.Need[(int)Need.Rest] = 1f;
            Stop(a, 1.5f);
            Pose(a);
        }

        public bool IsUnwell(int i) => i >= 0 && i < agents.Count && agents[i].State == Activity.Unwell;
        public bool IsChild(int i) => agents[i].Child;
        public bool NightOwl(int i) => agents[i].BedHour > 23.5f;

        // ------------------------------------------------------------------ for pets

        /// <summary>The villager whose home door is nearest <paramref name="home"/> (within <paramref name="within"/>), or -1.</summary>
        public int ResidentNear(Vector2 home, float within)
        {
            int best = -1; float bestD = within;
            foreach (var a in agents)
            {
                var door = a.HomeHouse >= 0 && a.HomeHouse < world.Buildings.Houses.Count ? DoorOf(a.HomeHouse) : a.Home;
                float d = (door - home).magnitude;
                if (d < bestD) { bestD = d; best = a.Index; }
            }
            return best;
        }

        /// <summary>Where a villager is, if they are out of doors on foot (not indoors, asleep, on a train or a ride).</summary>
        public Vector2? OutdoorsAt(int i)
        {
            if (i < 0 || i >= agents.Count) return null;
            var a = agents[i];
            if (a.Hidden || a.State is Activity.Riding or Activity.OnRide or Activity.Sleeping or Activity.Waiting) return null;
            return a.Position;
        }

        /// <summary>The ground (or a platform or bridge deck) under a point.</summary>
        public float GroundHeight(Vector2 p) => GroundAt(p);
    }
}
