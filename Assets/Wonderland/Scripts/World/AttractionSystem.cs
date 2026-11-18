using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// What makes the attractions worth visiting. At the zoo, giraffes, elephants, lions, zebras and penguins wander
    /// about their enclosures (now and then stopping to graze or look about). At the football stadium matches kick off at
    /// three and at half past seven: word goes round and villagers come to watch, the stands fill with a crowd (one mesh
    /// of seated fans, cheap however many), two teams chase the ball about the pitch, goals are news, and when the final
    /// whistle goes everybody heads home. Nothing here is saved: it follows the buildings.
    /// </summary>
    public sealed class AttractionSystem : MonoBehaviour
    {
        public static readonly float[] KickOffs = { 15f, 19.5f };
        public const float MatchHours = 1.5f;
        private static readonly string[] Species = { "giraffes", "elephants", "lions", "zebras", "penguins" };
        private static readonly int[] PerPen = { 2, 2, 2, 3, 4 };

        private sealed class Beast { public Transform Root; public Vector2 Pos, Target; public float Heading, Pause; public int Pen; }
        private sealed class ZooPlace { public Vector2 House; public Transform Root; public readonly List<Beast> Animals = new(); }
        private sealed class Ground
        {
            public Vector2 House; public Transform Root, Crowd, Ball;
            public readonly List<Transform> Players = new();
            public readonly List<Vector2> PlayerPos = new();
            public Vector2 BallPos, BallTo; public float BallTimer;
            public int Home, Away, LastKickOff = -1; public bool On;
            public StadiumMatch Match;
        }

        private WonderlandWorld world;
        private Material material;
        private readonly List<ZooPlace> zoos = new();
        private readonly List<Ground> grounds = new();
        private float sync;
        private Rng rng = new(311);
        private static Mesh[] beasts;
        private static Mesh crowd, ballMesh;

        /// <summary>Goals scored, and matches played (tests).</summary>
        public int Goals { get; private set; }
        public int Matches { get; private set; }

        public void Init(WonderlandWorld owner, Material lit) { world = owner; material = lit; }

        private void Update() { if (world != null && world.Terrain != null) Step(Time.deltaTime); }

        public void Step(float dt)
        {
            sync -= dt;
            if (sync <= 0f) { sync = 1f; Sync(); }
            foreach (var z in zoos) foreach (var a in z.Animals) StepBeast(z, a, dt);
            foreach (var g in grounds) StepGround(g, dt);
        }

        private Transform Part(string name, Transform parent, Mesh mesh, bool shadow = true)
        {
            var go = new GameObject(name);
            go.layer = WonderlandWorld.MiniatureLayer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        private static void Gone(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        private int HouseOf(Vector2 at, int variant) => world.Buildings.Houses.FindIndex(h => h.variant == variant && new Vector2(h.x, h.z) == at);

        private Transform Anchor(int house, string name)
        {
            var h = world.Buildings.Houses[house];
            var root = new GameObject(name).transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(h.x, world.Buildings.SitHeight(h, out _), h.z);
            root.localRotation = Quaternion.Euler(0f, h.rotation, 0f);
            return root;
        }

        private void Sync()
        {
            for (int k = zoos.Count - 1; k >= 0; k--) if (HouseOf(zoos[k].House, Catalog.Zoo) < 0) { Gone(zoos[k].Root.gameObject); zoos.RemoveAt(k); }
            for (int k = grounds.Count - 1; k >= 0; k--) if (HouseOf(grounds[k].House, Catalog.Stadium) < 0) { Gone(grounds[k].Root.gameObject); grounds.RemoveAt(k); }
            var houses = world.Buildings.Houses;
            for (int i = 0; i < houses.Count; i++)
            {
                var at = new Vector2(houses[i].x, houses[i].z);
                if (houses[i].variant == Catalog.Zoo && !zoos.Exists(z => z.House == at))
                {
                    var z = new ZooPlace { House = at, Root = Anchor(i, "Zoo animals") };
                    beasts ??= new[] { Giraffe(), Elephant(), Lion(), Zebra(), Penguin() };
                    for (int pen = 0; pen < HouseFactory.ZooPens.Length; pen++)
                        for (int n = 0; n < PerPen[pen]; n++)
                        {
                            var a = new Beast { Pen = pen, Root = Part(Species[pen], z.Root, beasts[pen]) };
                            a.Pos = a.Target = InPen(pen); a.Heading = rng.Range(0f, 360f); a.Pause = rng.Range(0f, 4f);
                            z.Animals.Add(a);
                        }
                    zoos.Add(z);
                }
                if (houses[i].variant == Catalog.Stadium && !grounds.Exists(g => g.House == at))
                {
                    var g = new Ground { House = at, Root = Anchor(i, "Stadium") };
                    g.Crowd = Part("Crowd", g.Root, crowd ??= Crowd(), false);
                    g.Crowd.gameObject.SetActive(false);
                    g.Ball = Part("Ball", g.Root, ballMesh ??= HouseFactory.Ball(), false);
                    g.Ball.localScale = Vector3.one * 0.6f;
                    for (int k = 0; k < 10; k++)
                    {
                        g.Players.Add(Part("Player", g.Root, PersonFactory.Body(k < 5 ? 0 : 3)));
                        g.PlayerPos.Add(new Vector2((k < 5 ? -1f : 1f) * (0.015f + k % 5 * 0.012f), (k % 5 - 2) * 0.015f));
                    }
                    ShowMatch(g, false);
                    grounds.Add(g);
                }
            }
        }

        // ------------------------------------------------------------------ the zoo

        private Vector2 InPen(int pen)
        {
            var (c, h) = HouseFactory.ZooPens[pen];
            return c + new Vector2(rng.Range(-h.x, h.x) * 0.75f, rng.Range(-h.y, h.y) * 0.75f);
        }

        private void StepBeast(ZooPlace z, Beast a, float dt)
        {
            var to = a.Target - a.Pos;
            if (a.Pause > 0f) a.Pause -= dt;
            else if (to.magnitude < 0.003f) { a.Pause = rng.Range(2f, 8f); a.Target = InPen(a.Pen); }
            else
            {
                float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                a.Heading = Mathf.MoveTowardsAngle(a.Heading, want, 90f * dt);
                float speed = a.Pen == 4 ? 0.006f : a.Pen == 2 ? 0.01f : 0.008f;
                if (Mathf.Abs(Mathf.DeltaAngle(a.Heading, want)) < 40f) a.Pos += to.normalized * Mathf.Min(speed * dt, to.magnitude);
            }
            float bob = a.Pause > 0f ? 0f : Mathf.Abs(Mathf.Sin(Time.time * 8f + a.Pos.x * 60f)) * 0.0006f;
            a.Root.localPosition = new Vector3(a.Pos.x, 0.002f + bob, a.Pos.y);
            a.Root.localRotation = Quaternion.Euler(0f, a.Heading, 0f);
        }

        /// <summary>How many animals a zoo has (for its card and the tests).</summary>
        public string ZooFor(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count || world.Buildings.Houses[house].variant != Catalog.Zoo) return null;
            var h = world.Buildings.Houses[house];
            var z = zoos.Find(x => x.House == new Vector2(h.x, h.z));
            return z == null ? null : $"{z.Animals.Count} animals: giraffes, elephants, lions, zebras and penguins";
        }

        public int AnimalsAt(int house)
        {
            var h = world.Buildings.Houses[house];
            return zoos.Find(x => x.House == new Vector2(h.x, h.z))?.Animals.Count ?? 0;
        }

        // ------------------------------------------------------------------ the stadium

        /// <summary>The kick-off a time of day belongs to (-1: no match on now).</summary>
        private static int KickOffAt(float hour)
        {
            for (int k = 0; k < KickOffs.Length; k++) if (hour >= KickOffs[k] && hour < KickOffs[k] + MatchHours) return k;
            return -1;
        }

        private void StepGround(Ground g, float dt)
        {
            int house = HouseOf(g.House, Catalog.Stadium);
            if (house < 0) return;
            int k = KickOffAt(world.Clock.Hour);
            int key = world.Clock.Day * 4 + k;
            if (k >= 0 && !g.On && key != g.LastKickOff)
            {
                // Kick-off: word goes round, the crowd comes, the teams run out.
                g.On = true; g.LastKickOff = key; g.Home = g.Away = 0; Matches++;
                g.Match = new StadiumMatch(world, house, KickOffs[k] + MatchHours);
                world.People.Deciders.Add(g.Match);
                g.Match.Invite();
                world.People.Tell("Kick-off at the stadium - the village is going to watch");
                ShowMatch(g, true);
            }
            else if (g.On && k < 0)
            {
                g.On = false;
                world.People.Tell($"Final whistle at the stadium: {g.Home} - {g.Away}");
                ShowMatch(g, false);
            }
            if (!g.On) return;
            // Play: the ball goes from player to player; now and then it ends up in a goal.
            g.BallTimer -= dt;
            if (g.BallTimer <= 0f)
            {
                g.BallTimer = rng.Range(0.8f, 2f);
                if (rng.Chance(0.05f))
                {
                    bool home = rng.Chance(0.5f);
                    g.BallTo = new Vector2((home ? 1f : -1f) * HouseFactory.PitchHalfX, rng.Range(-0.006f, 0.006f));
                    if (home) g.Home++; else g.Away++;
                    Goals++;
                    world.People.Tell($"Goal at the stadium! {g.Home} - {g.Away}");
                }
                else g.BallTo = g.PlayerPos[rng.Range(0, g.PlayerPos.Count)];
            }
            g.BallPos = Vector2.MoveTowards(g.BallPos, g.BallTo, 0.05f * dt);
            if (Mathf.Abs(g.BallPos.x) >= HouseFactory.PitchHalfX - 0.001f && g.BallTimer < 0.5f) g.BallPos = g.BallTo = Vector2.zero;   // back to the centre spot
            g.Ball.localPosition = new Vector3(g.BallPos.x, 0.004f, g.BallPos.y);
            for (int p = 0; p < g.Players.Count; p++)
            {
                // Each player drifts towards the ball (their own patch of the pitch kept), turning to face it.
                bool home = p < 5;
                var patch = new Vector2((home ? -1f : 1f) * (0.012f + p % 5 * 0.011f), (p % 5 - 2) * 0.017f);
                var want = Vector2.Lerp(patch, g.BallPos, 0.45f);
                g.PlayerPos[p] = Vector2.MoveTowards(g.PlayerPos[p], want, 0.02f * dt);
                var face = g.BallPos - g.PlayerPos[p];
                g.Players[p].localPosition = new Vector3(g.PlayerPos[p].x, 0.0022f + Mathf.Abs(Mathf.Sin(Time.time * 9f + p)) * 0.001f, g.PlayerPos[p].y);
                g.Players[p].localRotation = Quaternion.Euler(0f, Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg, 0f);
            }
            g.Crowd.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(Time.time * 3f)) * 0.0008f, 0f);   // the crowd bobs and cheers
        }

        private void ShowMatch(Ground g, bool on)
        {
            g.Crowd.gameObject.SetActive(on);
            g.Ball.gameObject.SetActive(on);
            foreach (var p in g.Players) p.gameObject.SetActive(on);
        }

        /// <summary>For a stadium's card: the match, or when the next one is.</summary>
        public string StadiumFor(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count || world.Buildings.Houses[house].variant != Catalog.Stadium) return null;
            var h = world.Buildings.Houses[house];
            var g = grounds.Find(x => x.House == new Vector2(h.x, h.z));
            if (g == null) return null;
            return g.On ? $"match on: {g.Home} - {g.Away}" : "next match at 15:00 or 19:30";
        }

        public bool MatchOn(int house)
        {
            var h = world.Buildings.Houses[house];
            return grounds.Find(x => x.House == new Vector2(h.x, h.z))?.On ?? false;
        }

        // ------------------------------------------------------------------ models

        /// <summary>The crowd: rows of seated fans in team colours all round the stands, one mesh.</summary>
        private static Mesh Crowd()
        {
            var b = new MeshBuilder();
            var r = new Rng(77);
            var shirts = new[] { "#C8392E", "#F2EFE8", "#2F5A8A", "#E2B04A", "#C8392E", "#3E8A4A" };
            var skins = new[] { "#F2C9A6", "#E3B48E", "#C68E68", "#8E5E42" };
            for (int row = 0; row < 3; row++)
            {
                float gap = 0.012f + row * 0.01f, y = 0.008f + row * 0.008f;
                void Fan(Vector3 at)
                {
                    var shirt = Pal.Hex(shirts[r.Range(0, shirts.Length)]); var skin = Pal.Hex(skins[r.Range(0, skins.Length)]);
                    b.AddBox(at + new Vector3(0f, 0.002f, 0f), new Vector3(0.004f, 0.004f, 0.003f), shirt);
                    b.AddBlob(at + new Vector3(0f, 0.0055f, 0f), Vector3.one * 0.0019f, 0, 0f, r.Range(0, 99), (_, __) => skin);
                }
                foreach (int s in new[] { -1, 1 })
                {
                    for (float x = -HouseFactory.PitchHalfX - gap + 0.004f; x <= HouseFactory.PitchHalfX + gap - 0.004f; x += 0.0062f) if (r.Chance(0.82f)) Fan(new Vector3(x, y, s * (HouseFactory.PitchHalfZ + gap)));
                    for (float z = -HouseFactory.PitchHalfZ - gap + 0.008f; z <= HouseFactory.PitchHalfZ + gap - 0.008f; z += 0.0062f) if (r.Chance(0.82f)) Fan(new Vector3(s * (HouseFactory.PitchHalfX + gap), y, z));
                }
            }
            return b.ToMesh("Crowd");
        }

        private static Mesh Giraffe()
        {
            var b = new MeshBuilder();
            var coat = Pal.Hex("#E8B85A"); var spot = Pal.Hex("#9A6A32");
            foreach (var (x, z) in new[] { (-0.0025f, 0.004f), (0.0025f, 0.004f), (-0.0025f, -0.004f), (0.0025f, -0.004f) }) b.AddBox(new Vector3(x, 0.006f, z), new Vector3(0.0012f, 0.012f, 0.0012f), coat);
            b.AddBlob(new Vector3(0f, 0.013f, 0f), new Vector3(0.0045f, 0.0035f, 0.0065f), 1, 0f, 1, (d, _) => (Mathf.Sin(d.x * 9f) * Mathf.Cos(d.z * 7f)) > 0.4f ? spot : coat);
            b.Push(new Vector3(0f, 0.016f, 0.005f), Quaternion.Euler(-25f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0016f, 0.0011f, 0.016f, 6, coat, coat, false);
            b.Pop();
            b.AddBlob(new Vector3(0f, 0.0305f, 0.0122f), new Vector3(0.0017f, 0.0018f, 0.0032f), 1, 0f, 2, (_, __) => coat);
            foreach (int s in new[] { -1, 1 }) b.AddCylinder(new Vector3(s * 0.0008f, 0.032f, 0.0112f), 0.0003f, 0.0003f, 0.002f, 4, spot, spot, true);
            return b.ToMesh("Giraffe");
        }

        private static Mesh Elephant()
        {
            var b = new MeshBuilder();
            var grey = Pal.Hex("#8E8E96");
            foreach (var (x, z) in new[] { (-0.0035f, 0.005f), (0.0035f, 0.005f), (-0.0035f, -0.005f), (0.0035f, -0.005f) }) b.AddCylinder(new Vector3(x, 0f, z), 0.0019f, 0.0019f, 0.008f, 7, grey, grey, true);
            b.AddBlob(new Vector3(0f, 0.011f, 0f), new Vector3(0.0062f, 0.0058f, 0.0085f), 1, 0f, 3, (_, __) => grey);
            b.AddBlob(new Vector3(0f, 0.0135f, 0.0095f), new Vector3(0.0042f, 0.0042f, 0.0038f), 1, 0f, 4, (_, __) => grey);
            foreach (int s in new[] { -1, 1 }) b.AddBlob(new Vector3(s * 0.0048f, 0.0135f, 0.0085f), new Vector3(0.0012f, 0.0042f, 0.0036f), 1, 0f, 5 + s, (_, __) => Pal.Shade(grey, 0.9f));
            b.Push(new Vector3(0f, 0.0115f, 0.0128f), Quaternion.Euler(150f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0014f, 0.0008f, 0.009f, 6, grey, grey, true);
            b.Pop();
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.002f, 0.0105f, 0.0128f), new Vector3(0.0006f, 0.0006f, 0.003f), Pal.FlowerWhite);
            return b.ToMesh("Elephant");
        }

        private static Mesh Lion()
        {
            var b = new MeshBuilder();
            var coat = Pal.Hex("#D9A55A"); var mane = Pal.Hex("#8A5A2A");
            foreach (var (x, z) in new[] { (-0.002f, 0.004f), (0.002f, 0.004f), (-0.002f, -0.004f), (0.002f, -0.004f) }) b.AddBox(new Vector3(x, 0.0025f, z), new Vector3(0.0014f, 0.005f, 0.0014f), coat);
            b.AddBlob(new Vector3(0f, 0.0062f, 0f), new Vector3(0.0034f, 0.003f, 0.0065f), 1, 0f, 7, (_, __) => coat);
            b.AddBlob(new Vector3(0f, 0.008f, 0.006f), new Vector3(0.0042f, 0.0042f, 0.0032f), 1, 0.15f, 8, (_, __) => mane);
            b.AddBlob(new Vector3(0f, 0.008f, 0.0085f), new Vector3(0.0022f, 0.0022f, 0.0022f), 1, 0f, 9, (_, __) => coat);
            b.AddCylinder(new Vector3(0f, 0.006f, -0.0065f), 0.0004f, 0.0004f, 0.002f, 4, coat, mane, true);
            return b.ToMesh("Lion");
        }

        private static Mesh Zebra()
        {
            var b = new MeshBuilder();
            var white = Pal.FlowerWhite; var black = Pal.TrainBlack;
            foreach (var (x, z) in new[] { (-0.0018f, 0.0035f), (0.0018f, 0.0035f), (-0.0018f, -0.0035f), (0.0018f, -0.0035f) }) b.AddBox(new Vector3(x, 0.003f, z), new Vector3(0.0011f, 0.006f, 0.0011f), white);
            b.AddBlob(new Vector3(0f, 0.0075f, 0f), new Vector3(0.0028f, 0.0028f, 0.0058f), 1, 0f, 10, (d, _) => Mathf.Sin(d.z * 22f) > 0.2f ? black : white);
            b.AddBlob(new Vector3(0f, 0.0105f, 0.0062f), new Vector3(0.0014f, 0.0022f, 0.0026f), 1, 0f, 11, (d, _) => Mathf.Sin(d.y * 18f) > 0.3f ? black : white);
            return b.ToMesh("Zebra");
        }

        private static Mesh Penguin()
        {
            var b = new MeshBuilder();
            b.AddBlob(new Vector3(0f, 0.003f, 0f), new Vector3(0.0018f, 0.003f, 0.0016f), 1, 0f, 12, (d, _) => d.z > 0.2f ? Pal.FlowerWhite : Pal.TrainBlack);
            b.AddBlob(new Vector3(0f, 0.0065f, 0.0002f), Vector3.one * 0.0012f, 1, 0f, 13, (_, __) => Pal.TrainBlack);
            b.AddBox(new Vector3(0f, 0.0062f, 0.0014f), new Vector3(0.0006f, 0.0004f, 0.0008f), Pal.Hex("#F2A030"));
            return b.ToMesh("Penguin");
        }
    }

    /// <summary>A football match: whoever is free (not at work, not asleep) goes along to watch, until the final whistle.</summary>
    public sealed class StadiumMatch : IVillageDecider
    {
        private readonly WonderlandWorld world;
        private readonly Vector2 at;
        private readonly float until;

        public StadiumMatch(WonderlandWorld world, int house, float untilHour)
        {
            this.world = world;
            var h = world.Buildings.Houses[house];
            at = new Vector2(h.x, h.z);
            until = untilHour;
        }

        public int House => world.Buildings.Houses.FindIndex(h => h.variant == Catalog.Stadium && h.x == at.x && h.z == at.y);

        public bool Active => House >= 0 && world.Clock.Hour < until && world.Clock.Hour >= until - AttractionSystem.MatchHours - 0.01f;

        public void Invite()
        {
            var people = world.People;
            for (int i = 0; i < people.People.Count; i++)
                if (people.IsFree(i) && people.StateOf(i) != PeopleSystem.Activity.Working && Decide(people, i, out var plan)) people.Direct(i, plan);
        }

        public bool Decide(PeopleSystem people, int villager, out Intent plan)
        {
            plan = Intent.Nothing;
            if ((villager * 7919 + Mathf.FloorToInt(world.Clock.Total * 4f)) % 3 == 0) return false;   // not everyone is a football fan
            return people.PlanGather(villager, House, Need.Fun, out plan);
        }
    }
}
