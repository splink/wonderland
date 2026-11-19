using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    public enum SoundSetting { On = 0, Quiet = 1, Off = 2 }

    /// <summary>
    /// The world's sound, spatial and restrained. Four times a second it lists what could be heard - birds over the
    /// woods by day, water at ponds and streams, the fair's organ while the rides run, the disco's beat after dark,
    /// chatter at a busy café or restaurant, children at the playground and the pitch, the factory at work, steam and
    /// diesel trains on the move, engines and sirens - scores each by how loud it is and how near the listener, and
    /// plays only the best eight through a fixed pool of 3D audio sources (fading in and out, no restarts as things
    /// move). Moments (a bark, a whistle as a train leaves, brakes as it stops, a kick, a cheer, a horn, the burner of a
    /// balloon) play through three more, each kind at most every few seconds and only within earshot. On / Quiet / Off
    /// is remembered between sessions.
    /// </summary>
    public sealed class SoundSystem : MonoBehaviour
    {
        public const int Loops = 8, Shots = 3;
        public const float Earshot = 2.4f;
        private const string SettingKey = "wonderland.sound";

        private struct Candidate { public SoundLoop Kind; public long Id; public Vector3 At; public float Gain, Score; }

        private sealed class Voice
        {
            public AudioSource Source;
            public long Id = -1;
            public SoundLoop Kind;
            public float Target;
        }

        private WonderlandWorld world;
        private readonly List<Voice> voices = new();
        private readonly List<AudioSource> shots = new();
        private readonly List<Candidate> candidates = new();
        private readonly float[] lastCue = new float[System.Enum.GetValues(typeof(SoundCue)).Length];
        private readonly Dictionary<Train, int> trainStop = new();
        private readonly List<Vector3> woods = new(), waters = new();
        private float timer, clock, sceneryTimer;
        private int nextShot;
        private static SoundSetting setting = (SoundSetting)(-1);

        /// <summary>Where the ears are (the head camera; tests set it).</summary>
        public Transform Listener;
        /// <summary>What plays now (tests and diagnostics): kinds of loop with their target volume.</summary>
        public readonly List<(SoundLoop kind, float volume)> Playing = new();
        public int CuesPlayed { get; private set; }
        public int CuesSkipped { get; private set; }

        public static SoundSetting Setting
        {
            get { if ((int)setting < 0) setting = (SoundSetting)Mathf.Clamp(PlayerPrefs.GetInt(SettingKey, 0), 0, 2); return setting; }
            set { setting = value; PlayerPrefs.SetInt(SettingKey, (int)value); AudioListener.volume = Volume; }
        }

        public static float Volume => Setting switch { SoundSetting.On => 1f, SoundSetting.Quiet => 0.35f, _ => 0f };

        public void Init(WonderlandWorld owner)
        {
            world = owner;
            for (int i = 0; i < Loops + Shots; i++)
            {
                var go = new GameObject(i < Loops ? "Sound" : "Sound moment");
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = i < Loops; s.spatialBlend = 1f; s.dopplerLevel = 0f;
                s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = 0.18f; s.maxDistance = Earshot + 0.6f;
                s.volume = 0f;
                if (i < Loops) voices.Add(new Voice { Source = s }); else shots.Add(s);
            }
        }

        private Vector3 Ears()
        {
            if (Listener == null)
            {
                var l = FindAnyObjectByType<AudioListener>();
                if (l == null && Camera.main != null) l = Camera.main.gameObject.AddComponent<AudioListener>();
                if (l != null) { Listener = l.transform; AudioListener.volume = Volume; }
            }
            return Listener != null ? transform.InverseTransformPoint(Listener.position) : new Vector3(0f, 1.2f, -0.6f);
        }

        private int warmed;

        private void Update()
        {
            if (world == null || world.Terrain == null) return;
            // Synthesise the sounds one a frame after start-up, so none is made in the middle of a busy moment.
            // (A sound that fails is skipped and reported, never retried every frame in place of everything else.)
            if (warmed < loopKinds + cueKinds)
            {
                try { if (warmed < loopKinds) SoundBank.Loop((SoundLoop)warmed); else SoundBank.Cue((SoundCue)(warmed - loopKinds)); }
                catch (System.Exception e) { Report("making sound " + warmed, e); }
                warmed++;
            }
            long t0 = SimCost.Now;
            try { Step(Time.deltaTime); }
            catch (System.Exception e) { Report("choosing sounds", e); }
            SimCost.Add(SimCost.Sound, t0);
        }

        private static readonly int loopKinds = System.Enum.GetValues(typeof(SoundLoop)).Length, cueKinds = System.Enum.GetValues(typeof(SoundCue)).Length;

        /// <summary>Picks what to hear (four times a second) and fades the voices (also driven by tests).</summary>
        public void Step(float dt)
        {
            clock += dt;
            timer -= dt;
            if (timer <= 0f) { timer = 0.25f; Choose(); }
            foreach (var v in voices)
            {
                var s = v.Source;
                s.volume = Mathf.MoveTowards(s.volume, v.Target * Volume, dt * 1.5f);
                if (s.volume <= 0.001f && v.Target <= 0f && s.isPlaying) { s.Stop(); v.Id = -1; }
            }
            WatchTrains();
        }

        // ------------------------------------------------------------------ what could be heard

        /// <summary>What it has to choose from, and where it listens (diagnostics: the headset's perf log line).</summary>
        public string Diagnostics => $"{woods.Count} woods, {waters.Count} waters, {candidates.Count} candidates, ears {lastEars} on {(Listener != null ? Listener.name : "nothing")}, listener volume {AudioListener.volume:0.00}{(failures > 0 ? $", {failures} errors: {lastError}" : "")}";
        private int failures;
        private string lastError;

        private void Report(string what, System.Exception e)
        {
            failures++;
            lastError = $"{what}: {e.GetType().Name} {e.Message}";
            if (failures <= 3) Debug.LogError($"Wonderland sound: {lastError}\n{e.StackTrace}");
        }
        private Vector3 lastEars;

        private void Choose()
        {
            var ears = Ears();
            lastEars = ears;
            if (Listener != null && AudioListener.volume != Volume) AudioListener.volume = Volume;
            candidates.Clear();
            Playing.Clear();
            if (Volume <= 0f) { foreach (var v in voices) v.Target = 0f; return; }
            float hour = world.Clock.Hour, day = world.Clock.Daylight;
            sceneryTimer -= 0.25f;
            if (sceneryTimer <= 0f) { sceneryTimer = 5f; Scenery(); }
            // Birds by day (a dawn chorus), water always.
            float birds = day * (1f + Mathf.Clamp01(1f - Mathf.Abs(hour - 6.5f)) * 0.8f);
            for (int i = 0; i < woods.Count; i++) Add(SoundLoop.Birds, 1000 + i, woods[i], 0.5f * birds);
            for (int i = 0; i < waters.Count; i++) Add(SoundLoop.Water, 2000 + i, waters[i], 0.35f);
            // Rain all around, just below the ears (falling on the land under them).
            if (world.Sky.Rain > 0.05f) Add(SoundLoop.Rain, 7000, ears + Vector3.down * 0.3f, 0.55f * world.Sky.Rain);
            // Places.
            var houses = world.Buildings.Houses;
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                var bp = Catalog.Get(h.variant);
                var at = new Vector3(h.x, world.Terrain.SurfaceAt(h.x, h.z) + 0.03f, h.z);
                switch (bp.Id)
                {
                    case Catalog.Carousel: case Catalog.FerrisWheel:
                        if (world.Buildings.RidesOpen > 0.5f) Add(SoundLoop.Fair, 3000 + i, at, 0.45f); break;
                    case Catalog.Disco:
                        if (bp.OpenAt(hour)) Add(SoundLoop.Disco, 3000 + i, at, 0.5f); break;
                    case Catalog.Cafe: case Catalog.Restaurant: case Catalog.Fountain:
                        { int n = world.People.VisitorsAt(i); if (n >= 2) Add(SoundLoop.Chatter, 3000 + i, at, Mathf.Min(0.6f, 0.15f * n)); } break;
                    case Catalog.Playground: case Catalog.Football: case Catalog.School:
                        { int n = world.People.VisitorsAt(i); if (n >= 1) Add(SoundLoop.Children, 3000 + i, at, Mathf.Min(0.7f, 0.2f + 0.1f * n)); } break;
                    case Catalog.Factory:
                        if (world.People.WorkerAt(i) >= 0 && world.People.StateOf(world.People.WorkerAt(i)) == PeopleSystem.Activity.Working && world.People.PlaceOf(world.People.WorkerAt(i)) == i) Add(SoundLoop.Factory, 3000 + i, at, 0.4f); break;
                }
            }
            // Trains and road vehicles on the move.
            for (int i = 0; i < world.Trains.Trains.Count; i++)
            {
                var t = world.Trains.Trains[i];
                if (t.Speed < 0.02f || world.Trains.InTunnel(t)) continue;
                Add(TrainSystem.Steam(t.Kind) ? SoundLoop.Steam : SoundLoop.Diesel, 4000 + i, world.Trains.LocoPosition(t), 0.4f + t.Speed * 2f);
            }
            if (world.Traffic != null)
                for (int i = 0; i < world.Traffic.Vehicles.Count; i++)
                {
                    var v = world.Traffic.Vehicles[i];
                    var at = new Vector3(v.Position.x, v.Height + 0.01f, v.Position.y);
                    if (v.Siren) Add(SoundLoop.Siren, 5000 + i, at, 0.55f);
                    else if (v.State == TrafficSystem.Mode.Driving && v.Kind != VehicleKind.Bicycle)
                        Add(v.Kind == VehicleKind.Motorbike ? SoundLoop.Motorbike : SoundLoop.Engine, 6000 + i, at, 0.18f + v.Speed * 2f);
                }
            // The best few by loudness at the listener; the voices follow them.
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                float d = (c.At - ears).magnitude;
                c.Score = d > Earshot ? 0f : c.Gain * Priority(c.Kind) / (1f + d * d * 4f);
                candidates[i] = c;
            }
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            int take = 0;
            foreach (var v in voices) v.Target = 0f;
            for (int i = 0; i < candidates.Count && take < Loops; i++)
            {
                var c = candidates[i];
                if (c.Score < 0.01f) break;
                var voice = voices.Find(x => x.Id == c.Id && x.Kind == c.Kind) ?? voices.Find(x => x.Target <= 0f && x.Source.volume <= 0.02f && !Claimed(x, i));
                if (voice == null) continue;
                if (voice.Id != c.Id || voice.Kind != c.Kind || !voice.Source.isPlaying)
                {
                    voice.Id = c.Id; voice.Kind = c.Kind;
                    voice.Source.clip = SoundBank.Loop(c.Kind);
                    voice.Source.time = (c.Id * 0.37f) % Mathf.Max(0.1f, voice.Source.clip.length);
                    voice.Source.Play();
                }
                voice.Target = c.Gain;
                voice.Source.transform.localPosition = c.At;
                Playing.Add((c.Kind, c.Gain));
                take++;
            }
        }

        private bool Claimed(Voice v, int upTo)
        {
            for (int i = 0; i < upTo; i++) if (candidates[i].Id == v.Id && candidates[i].Kind == v.Kind) return true;
            return false;
        }

        private static float Priority(SoundLoop k) => k switch
        {
            SoundLoop.Siren => 3f, SoundLoop.Rain => 2.5f, SoundLoop.Steam or SoundLoop.Diesel => 2f, SoundLoop.Disco or SoundLoop.Fair => 1.4f, SoundLoop.Birds or SoundLoop.Water => 0.7f, _ => 1f,
        };

        private void Add(SoundLoop kind, long id, Vector3 at, float gain)
        {
            if (gain <= 0.01f) return;
            candidates.Add(new Candidate { Kind = kind, Id = id, At = at, Gain = Mathf.Min(1f, gain) });
        }

        /// <summary>Woods (clusters of trees) and water, worked out again every few seconds: a handful of places each.</summary>
        private void Scenery()
        {
            woods.Clear(); waters.Clear();
            var cells = new Dictionary<long, (Vector3 sum, int n)>();
            foreach (var t in world.Forest.Trees)
            {
                long k = ((long)Mathf.FloorToInt(t.x / 0.3f) << 32) ^ (uint)Mathf.FloorToInt(t.z / 0.3f);
                var (sum, n) = cells.TryGetValue(k, out var c) ? c : (Vector3.zero, 0);
                cells[k] = (sum + new Vector3(t.x, world.Terrain.SurfaceAt(t.x, t.z) + 0.08f, t.z), n + 1);
            }
            foreach (var c in cells.Values) if (c.n >= 4 && woods.Count < 12) woods.Add(c.sum / c.n);
            var land = world.Terrain.LandBounds();
            for (float x = land.xMin; x < land.xMax && waters.Count < 10; x += 0.25f)
            for (float z = land.yMin; z < land.yMax && waters.Count < 10; z += 0.25f)
                if (world.Terrain.WaterAt(x, z) > 0.5f) waters.Add(new Vector3(x, world.Terrain.WaterLevelAt(x, z), z));
            foreach (var st in world.Streams.Streams)
                if (st.points.Count > 0 && waters.Count < 14) waters.Add(st.points[st.points.Count / 2]);
        }

        // ------------------------------------------------------------------ moments

        private static float Cooldown(SoundCue k) => k switch
        {
            SoundCue.Kick => 0.5f, SoundCue.Bark => 4f, SoundCue.Meow => 6f, SoundCue.Cheer => 4f, SoundCue.Whistle => 3f, SoundCue.Brakes => 3f, SoundCue.Horn => 5f, _ => 2f,
        };

        /// <summary>A moment heard at a point on the ground (world-local x, z), if near enough and not too soon after the last.</summary>
        public void Cue(SoundCue kind, Vector2 at) => Cue(kind, new Vector3(at.x, world.Terrain.SurfaceAt(at.x, at.y) + 0.01f, at.y));

        public void Cue(SoundCue kind, Vector3 at)
        {
            if (Volume <= 0f || clock - lastCue[(int)kind] < Cooldown(kind) || (at - Ears()).magnitude > Earshot) { CuesSkipped++; return; }
            lastCue[(int)kind] = clock;
            var s = shots[nextShot++ % shots.Count];
            s.transform.localPosition = at;
            s.PlayOneShot(SoundBank.Cue(kind), Volume * (kind == SoundCue.Kick ? 0.6f : 0.9f));
            CuesPlayed++;
        }

        /// <summary>A train that stops at a station squeals its brakes; one that sets off whistles.</summary>
        private void WatchTrains()
        {
            foreach (var t in world.Trains.Trains)
            {
                int now = t.AtStation;
                int before = trainStop.TryGetValue(t, out int b) ? b : -1;
                if (now >= 0 && before < 0) Cue(SoundCue.Brakes, world.Trains.LocoPosition(t));
                if (now < 0 && before >= 0 && TrainSystem.Steam(t.Kind)) Cue(SoundCue.Whistle, world.Trains.LocoPosition(t));
                trainStop[t] = now;
            }
        }
    }
}
