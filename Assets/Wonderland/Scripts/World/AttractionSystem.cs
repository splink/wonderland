using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonderland
{
    /// <summary>
    /// What makes the attractions worth visiting. In every zoo piece the animals live in their pens (see
    /// HouseFactory.PensOf), sized to the villagers - a giraffe three times their height, an elephant twice - wandering,
    /// grazing, looking about; parrots fly under their nets, monkeys climb. A zoo entrance with at least two enclosures
    /// behind it makes a zoo: it opens (word goes round the village) and on its open days visitors come through the
    /// gate, buy their tickets, go round the animals one after another and end with a snack (see <see cref="ZooDay"/>). At the football stadium matches kick off at
    /// three and at half past seven: word goes round and villagers come to watch, the stands fill with a crowd (one mesh
    /// of seated fans, cheap however many), two teams chase the ball about the pitch, goals are news, and when the final
    /// whistle goes everybody heads home. Nothing here is saved: it follows the buildings.
    /// </summary>
    public sealed class AttractionSystem : MonoBehaviour
    {
        public static readonly float[] KickOffs = { 15f, 19.5f };
        public const float MatchHours = 1.5f;
        private static readonly string[] Species = { "giraffes", "elephants", "lions", "zebras", "penguins", "monkeys", "flamingos", "goats", "parrots" };
        /// <summary>Each species' size against the 3 cm villagers (their models are made small; a giraffe stands some 8 cm tall).</summary>
        private static readonly float[] Size = { 2.6f, 2.5f, 1.9f, 2f, 1.4f, 1f, 1f, 1f, 1f };
        private static readonly float[] Pace = { 0.012f, 0.009f, 0.012f, 0.014f, 0.006f, 0.014f, 0.008f, 0.01f, 0.03f };
        /// <summary>A zoo entrance this close to enclosures makes a zoo with them.</summary>
        public const float ZooReach = 0.8f;

        private sealed class Beast { public Transform Root; public Vector2 Pos, Target; public float Heading, Pause, Y, TargetY; public int Pen; }
        private sealed class ZooPlace { public Vector2 House; public int Variant; public Transform Root; public HouseFactory.Pen[] Pens; public readonly List<Beast> Animals = new(); }
        private sealed class ZooPark { public Vector2 Gate; public ZooDay Day; public bool Open; }
        private readonly List<ZooPark> parks = new();
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
            for (int k = zoos.Count - 1; k >= 0; k--) if (HouseOf(zoos[k].House, zoos[k].Variant) < 0) { Gone(zoos[k].Root.gameObject); zoos.RemoveAt(k); }
            for (int k = grounds.Count - 1; k >= 0; k--) if (HouseOf(grounds[k].House, Catalog.Stadium) < 0) { Gone(grounds[k].Root.gameObject); grounds.RemoveAt(k); }
            var houses = world.Buildings.Houses;
            for (int i = 0; i < houses.Count; i++)
            {
                var at = new Vector2(houses[i].x, houses[i].z);
                var pens = HouseFactory.PensOf(houses[i].variant);
                if (pens.Length > 0 && !zoos.Exists(z => z.House == at && z.Variant == houses[i].variant))
                {
                    var z = new ZooPlace { House = at, Variant = houses[i].variant, Pens = pens, Root = Anchor(i, "Zoo animals") };
                    beasts ??= new[] { Giraffe(), Elephant(), Lion(), Zebra(), Penguin(), Monkey(), Flamingo(), Goat(), Parrot() };
                    for (int pen = 0; pen < pens.Length; pen++)
                        for (int n = 0; n < pens[pen].Count; n++)
                        {
                            int sp = pens[pen].Species;
                            var a = new Beast { Pen = pen, Root = Part(Species[sp], z.Root, beasts[sp]) };
                            a.Root.localScale = Vector3.one * Size[sp];
                            a.Pos = a.Target = InPen(pens[pen]); a.Y = a.TargetY = pens[pen].Y; a.Heading = rng.Range(0f, 360f); a.Pause = rng.Range(0f, 4f);
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
            SyncParks();
        }

        /// <summary>
        /// Zoos: every entrance with at least two animal pieces (with animals) within <see cref="ZooReach"/> is a zoo.
        /// A new one opens with the news, and its visitors' day begins (see <see cref="ZooDay"/>).
        /// </summary>
        private void SyncParks()
        {
            var houses = world.Buildings.Houses;
            for (int k = parks.Count - 1; k >= 0; k--) if (HouseOf(parks[k].Gate, Catalog.ZooGate) < 0) { parks[k].Open = false; parks.RemoveAt(k); }
            for (int i = 0; i < houses.Count; i++)
            {
                if (houses[i].variant != Catalog.ZooGate) continue;
                var at = new Vector2(houses[i].x, houses[i].z);
                var park = parks.Find(p => p.Gate == at);
                if (park == null) parks.Add(park = new ZooPark { Gate = at });
                int exhibits = ZooPieces(at, true).Count;
                bool open = exhibits >= 2;
                if (open && !park.Open)
                {
                    park.Day = new ZooDay(world, this, at);
                    world.People.Deciders.Add(park.Day);
                    world.People.Tell($"The zoo opens its gates - {exhibits} enclosures to see. Families are on their way!");
                    ZoosOpened++;
                    if (park.Day.Active) park.Day.Invite();
                }
                park.Open = open;
            }
        }

        /// <summary>The zoo pieces round an entrance (within <see cref="ZooReach"/>): just those with animals, or every one.</summary>
        public List<int> ZooPieces(Vector2 gate, bool animals)
        {
            var list = new List<int>();
            var houses = world.Buildings.Houses;
            for (int i = 0; i < houses.Count; i++)
            {
                var bp = Catalog.Get(houses[i].variant);
                if (bp.Category != PlaceCategory.Zoo || houses[i].variant == Catalog.ZooGate) continue;
                if ((new Vector2(houses[i].x, houses[i].z) - gate).magnitude > ZooReach) continue;
                if (animals && HouseFactory.PensOf(houses[i].variant).Length == 0) continue;
                list.Add(i);
            }
            return list;
        }

        /// <summary>Zoos that have opened, and whether the entrance at a house is a zoo now (tests, its card).</summary>
        public int ZoosOpened { get; private set; }
        public bool ZooOpen(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count) return false;
            var h = world.Buildings.Houses[house];
            return parks.Exists(p => p.Open && p.Gate == new Vector2(h.x, h.z));
        }

        /// <summary>Villagers on a zoo day now (tests).</summary>
        public int ZooVisitors { get { int n = 0; foreach (var p in parks) if (p.Day != null) n += p.Day.Touring; return n; } }

        // ------------------------------------------------------------------ the zoo

        private Vector2 InPen(HouseFactory.Pen pen) => pen.Centre + new Vector2(rng.Range(-pen.Half.x, pen.Half.x) * 0.75f, rng.Range(-pen.Half.y, pen.Half.y) * 0.75f);

        private void StepBeast(ZooPlace z, Beast a, float dt)
        {
            var pen = z.Pens[a.Pen];
            int sp = pen.Species;
            var to = a.Target - a.Pos;
            if (a.Pause > 0f && !pen.Fly) a.Pause -= dt;
            else if (to.magnitude < 0.003f)
            {
                a.Pause = pen.Fly ? 0f : rng.Range(2f, 8f);
                a.Target = InPen(pen);
                a.TargetY = pen.Fly ? pen.Y + rng.Range(-0.02f, 0.02f) : pen.Climb ? pen.Y + (rng.Chance(0.5f) ? rng.Range(0.015f, 0.035f) : 0f) : pen.Y;
            }
            else
            {
                float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                a.Heading = Mathf.MoveTowardsAngle(a.Heading, want, (pen.Fly ? 160f : 90f) * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(a.Heading, want)) < (pen.Fly ? 70f : 40f)) a.Pos += to.normalized * Mathf.Min(Pace[sp] * dt, to.magnitude);
            }
            a.Y = Mathf.MoveTowards(a.Y, a.TargetY, (pen.Fly ? 0.02f : 0.01f) * dt);
            float bob = pen.Fly ? Mathf.Sin(Time.time * 14f + a.Pos.x * 90f) * 0.0012f : a.Pause > 0f ? 0f : Mathf.Abs(Mathf.Sin(Time.time * 8f + a.Pos.x * 60f)) * 0.0006f * Size[sp];
            a.Root.localPosition = new Vector3(a.Pos.x, a.Y + bob, a.Pos.y);
            a.Root.localRotation = Quaternion.Euler(0f, a.Heading, pen.Fly ? Mathf.Sin(Time.time * 3f + a.Pos.y * 40f) * 15f : 0f);
        }

        /// <summary>What lives in a zoo piece (for its card and the tests), or null.</summary>
        public string ZooFor(int house)
        {
            if (house < 0 || house >= world.Buildings.Houses.Count) return null;
            var h = world.Buildings.Houses[house];
            var z = zoos.Find(x => x.House == new Vector2(h.x, h.z) && x.Variant == h.variant);
            if (z == null) return null;
            var kinds = new List<string>();
            foreach (var pen in z.Pens) if (!kinds.Contains(Species[pen.Species])) kinds.Add(Species[pen.Species]);
            return $"{z.Animals.Count} animals: {string.Join(", ", kinds)}" + (h.variant != Catalog.Zoo && !InPark(new Vector2(h.x, h.z)) ? " - put a zoo entrance nearby to open a zoo" : "");
        }

        private bool InPark(Vector2 at) => parks.Exists(p => p.Open && (p.Gate - at).magnitude <= ZooReach);

        public int AnimalsAt(int house)
        {
            var h = world.Buildings.Houses[house];
            return zoos.Find(x => x.House == new Vector2(h.x, h.z) && x.Variant == h.variant)?.Animals.Count ?? 0;
        }

        /// <summary>Where an animal is now (world-local), for tests: the first of a species in a piece.</summary>
        public Vector3? AnimalPosition(int house, int index)
        {
            var h = world.Buildings.Houses[house];
            var z = zoos.Find(x => x.House == new Vector2(h.x, h.z) && x.Variant == h.variant);
            if (z == null || index >= z.Animals.Count) return null;
            return transform.InverseTransformPoint(z.Animals[index].Root.position);
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

        private static Mesh Monkey()
        {
            var b = new MeshBuilder();
            var fur = Pal.Hex("#7A5232"); var face = Pal.Hex("#E2B48A");
            b.AddBlob(new Vector3(0f, 0.0075f, 0f), new Vector3(0.0028f, 0.0036f, 0.0024f), 1, 0f, 14, (_, __) => fur);
            b.AddBlob(new Vector3(0f, 0.0125f, 0.0006f), Vector3.one * 0.0022f, 1, 0f, 15, (_, __) => fur);
            b.AddBlob(new Vector3(0f, 0.0122f, 0.0022f), new Vector3(0.0015f, 0.0013f, 0.0008f), 1, 0f, 16, (_, __) => face);
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(s * 0.0026f, 0.0075f, 0.001f), new Vector3(0.0009f, 0.0055f, 0.0009f), fur);   // arms
                b.AddBox(new Vector3(s * 0.0013f, 0.0025f, 0f), new Vector3(0.001f, 0.005f, 0.001f), fur);           // legs
                b.AddBlob(new Vector3(s * 0.0022f, 0.0135f, 0f), Vector3.one * 0.0007f, 0, 0f, 17, (_, __) => face);
            }
            b.Push(new Vector3(0f, 0.005f, -0.002f), Quaternion.Euler(-130f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0005f, 0.0003f, 0.008f, 5, fur, fur, true);   // the tail
            b.Pop();
            return b.ToMesh("Monkey");
        }

        private static Mesh Flamingo()
        {
            var b = new MeshBuilder();
            var pink = Pal.Hex("#F28AA8");
            b.AddBox(new Vector3(0f, 0.008f, 0f), new Vector3(0.0005f, 0.016f, 0.0005f), pink);   // one leg (the other tucked up)
            b.AddBlob(new Vector3(0f, 0.017f, 0f), new Vector3(0.0022f, 0.002f, 0.0036f), 1, 0f, 18, (_, __) => pink);
            b.Push(new Vector3(0f, 0.018f, 0.003f), Quaternion.Euler(-15f, 0f, 0f));
            b.AddCylinder(Vector3.zero, 0.0005f, 0.0004f, 0.009f, 5, pink, pink, false);
            b.Pop();
            b.AddBlob(new Vector3(0f, 0.027f, 0.0052f), new Vector3(0.0009f, 0.0009f, 0.0012f), 1, 0f, 19, (_, __) => pink);
            b.AddBox(new Vector3(0f, 0.0265f, 0.0068f), new Vector3(0.0005f, 0.0006f, 0.0014f), Pal.TrainBlack);
            return b.ToMesh("Flamingo");
        }

        private static Mesh Goat()
        {
            var b = new MeshBuilder();
            var coat = Pal.Hex("#EDE6DA"); var horn = Pal.Hex("#8A7A62");
            foreach (var (x, z) in new[] { (-0.0014f, 0.0026f), (0.0014f, 0.0026f), (-0.0014f, -0.0026f), (0.0014f, -0.0026f) }) b.AddBox(new Vector3(x, 0.0025f, z), new Vector3(0.0008f, 0.005f, 0.0008f), Pal.Shade(coat, 0.85f));
            b.AddBlob(new Vector3(0f, 0.0062f, 0f), new Vector3(0.0022f, 0.0022f, 0.0042f), 1, 0f, 20, (_, __) => coat);
            b.AddBlob(new Vector3(0f, 0.0088f, 0.0045f), new Vector3(0.0013f, 0.0014f, 0.002f), 1, 0f, 21, (_, __) => coat);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.0007f, 0.0105f, 0.0038f), new Vector3(0.0004f, 0.0018f, 0.0004f), horn);
            b.AddBox(new Vector3(0f, 0.0072f, 0.0058f), new Vector3(0.0005f, 0.0015f, 0.0005f), Pal.Shade(coat, 0.8f));   // the beard
            return b.ToMesh("Goat");
        }

        private static Mesh Parrot()
        {
            var b = new MeshBuilder();
            var red = Pal.Hex("#E2383E"); var blue = Pal.Hex("#2E6ED8"); var yellow = Pal.Hex("#F2C12E");
            b.AddBlob(Vector3.zero, new Vector3(0.0014f, 0.0014f, 0.0028f), 1, 0f, 22, (_, __) => red);
            b.AddBlob(new Vector3(0f, 0.0008f, 0.0024f), Vector3.one * 0.001f, 1, 0f, 23, (_, __) => red);
            b.AddBox(new Vector3(0f, 0.0005f, 0.0036f), new Vector3(0.0005f, 0.0006f, 0.0008f), Pal.Hex("#F2E3C2"));
            foreach (int s in new[] { -1, 1 })
            {
                b.Push(new Vector3(s * 0.0012f, 0.0004f, 0f), Quaternion.Euler(0f, 0f, s * 20f));
                b.AddBox(new Vector3(s * 0.0025f, 0f, 0f), new Vector3(0.005f, 0.0004f, 0.0026f), blue);
                b.AddBox(new Vector3(s * 0.0012f, 0.0001f, 0f), new Vector3(0.0022f, 0.0004f, 0.002f), yellow);
                b.Pop();
            }
            b.AddBox(new Vector3(0f, 0f, -0.0034f), new Vector3(0.0012f, 0.0003f, 0.0032f), blue);
            return b.ToMesh("Parrot");
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

    /// <summary>
    /// A day at the zoo: during opening hours, some of the villagers who are free (more of the children) set off for the
    /// zoo. They go in through the gate and buy a ticket at a booth, then go round the enclosures one after another -
    /// the nearest they haven't seen yet - and finish with a snack at the kiosk or the café, if the zoo has one. Each
    /// stop is an ordinary visit (a free spot, on foot), so they keep to the paths and the crowds round the animals are
    /// real; when a spot is taken they stand nearby instead.
    /// </summary>
    public sealed class ZooDay : IVillageDecider
    {
        private readonly WonderlandWorld world;
        private readonly AttractionSystem zoo;
        private readonly Vector2 gate;
        private sealed class Visit { public int Day, Stage; public readonly List<int> Seen = new(); }
        private readonly Dictionary<int, Visit> visits = new();

        public ZooDay(WonderlandWorld world, AttractionSystem zoo, Vector2 gate) { this.world = world; this.zoo = zoo; this.gate = gate; }

        private int Gate => world.Buildings.Houses.FindIndex(h => h.variant == Catalog.ZooGate && h.x == gate.x && h.z == gate.y);
        public bool Active => Gate >= 0 && zoo.ZooOpen(Gate) && Catalog.Get(Catalog.ZooGate).OpenAt(world.Clock.Hour) && world.Clock.Hour < 17.5f;

        /// <summary>Villagers somewhere on their round of the zoo today.</summary>
        public int Touring { get { int n = 0, day = world.Clock.Day; foreach (var v in visits.Values) if (v.Day == day && v.Stage > 0 && v.Stage < 99) n++; return n; } }

        /// <summary>When it opens: the free villagers who fancy a zoo day set off now.</summary>
        public void Invite()
        {
            var people = world.People;
            for (int i = 0; i < people.People.Count; i++)
                if (people.IsFree(i) && people.StateOf(i) != PeopleSystem.Activity.Working && Decide(people, i, out var plan)) people.Direct(i, plan);
        }

        public bool Decide(PeopleSystem people, int villager, out Intent plan)
        {
            plan = Intent.Nothing;
            int day = world.Clock.Day;
            if (!visits.TryGetValue(villager, out var v) || v.Day != day)
            {
                // Who goes today: about two in five (the children more often).
                bool child = people.IsChild(villager);
                if ((villager * 7919 + day * 104729) % 10 >= (child ? 6 : 4)) { visits[villager] = new Visit { Day = day, Stage = 99 }; return false; }
                visits[villager] = v = new Visit { Day = day };
            }
            if (v.Stage >= 99) return false;
            if (people.StateOf(villager) == PeopleSystem.Activity.Working) return false;
            int gateHouse = Gate;
            if (v.Stage == 0)
            {
                // In through the gate: a ticket at a booth.
                if (people.PlanVisit(villager, gateHouse, Need.Sights, out plan) || people.PlanGather(villager, gateHouse, Need.Sights, out plan)) { v.Stage = 1; return true; }
                return false;
            }
            var pieces = zoo.ZooPieces(gate, true);
            pieces.RemoveAll(i => v.Seen.Contains(i));
            if (v.Stage <= 3 && pieces.Count > 0)
            {
                // The nearest enclosure not seen yet.
                var at = people.PositionOf(villager);
                pieces.Sort((x, y) => (new Vector2(world.Buildings.Houses[x].x, world.Buildings.Houses[x].z) - at).sqrMagnitude.CompareTo((new Vector2(world.Buildings.Houses[y].x, world.Buildings.Houses[y].z) - at).sqrMagnitude));
                foreach (int house in pieces)
                    if (people.PlanVisit(villager, house, Need.Sights, out plan) || people.PlanGather(villager, house, Need.Sights, out plan)) { v.Seen.Add(house); v.Stage++; return true; }
            }
            // A snack to finish with, then home (their own choosing again).
            v.Stage = 99;
            foreach (int house in zoo.ZooPieces(gate, false))
            {
                int id = world.Buildings.Houses[house].variant;
                if ((id == Catalog.ZooKiosk || id == Catalog.SafariCafe) && people.PlanVisit(villager, house, Need.Food, out plan)) return true;
            }
            return false;
        }
    }
}
