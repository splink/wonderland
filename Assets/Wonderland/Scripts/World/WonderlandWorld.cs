using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Wonderland
{
    [Serializable]
    public sealed class WorldMaterials
    {
        public Material Lit, Terrain, UI, Smoke, Water, Screen;
    }

    /// <summary>
    /// The miniature world: terrain, forest, railway, houses and train under one root that sits on the real floor
    /// (local y = 0 is the floor). Every editing operation goes through here, so controller input and the scripted
    /// journey test drive exactly the same code. Undo restores whole-world snapshots taken before each operation;
    /// save/load writes one local world as JSON.
    /// </summary>
    public sealed class WonderlandWorld : MonoBehaviour
    {
        public const int MaxUndo = 20;
        private const long UndoBudgetBytes = 48L * 1024 * 1024;   // big grids keep fewer snapshots
        public TerrainField Terrain { get; private set; }
        public TerrainRenderer TerrainView { get; private set; }
        public ForestSystem Forest { get; private set; }
        public TrackSystem Track { get; private set; }
        public BuildingSystem Buildings { get; private set; }
        public TrainSystem Trains { get; private set; }
        public GroundCover Cover { get; private set; }
        public WaterRenderer WaterView { get; private set; }
        public AnimalSystem Animals { get; private set; }
        public PeopleSystem People { get; private set; }
        public StreamSystem Streams { get; private set; }
        public PathSystem Paths { get; private set; }
        public BridgeSystem Bridges { get; private set; }
        public TrafficSystem Traffic { get; private set; }
        public SoundSystem Sounds { get; private set; }
        public CctvSystem Cctv { get; private set; }
        public BoatSystem Boats { get; private set; }
        public SkySystem Sky { get; private set; }
        /// <summary>The village's time of day (villagers' routines, the rides, the light).</summary>
        public VillageClock Clock { get; private set; } = new();
        public WorldMaterials Materials { get; private set; }
        public float PatchSize => Terrain.PatchSize;
        public int Seed { get; private set; }
        public int UndoDepth => undo.Count;

        private readonly List<Snapshot> undo = new();
        private bool resitHouses;
        private RoomFootprint room;
        private RoomFootprint.Local roomLocal;

        /// <summary>The real room's free floor (null: unknown, land may grow anywhere in the default grid).</summary>
        public RoomFootprint Room { get => room; set { room = value; if (Terrain != null) FitToRoom(); } }

        /// <summary>The layer of the little moving things (villagers, animals, vehicles): the eye camera stops drawing them far away.</summary>
        public const int MiniatureLayer = 9;
        public const float MiniatureCullDistance = 3f;

        public static string SavePath => Path.Combine(Application.persistentDataPath, "wonderland-world.json");

        public void Create(float size, WorldMaterials materials, int seed = 7, bool hills = true)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying) Destroy(transform.GetChild(i).gameObject); else DestroyImmediate(transform.GetChild(i).gameObject);
            }
            Materials = materials; Seed = seed;
            platforms = null;
            Clock = new VillageClock();
            Terrain = new TerrainField(size, seed, hills);
            TerrainView = Child<TerrainRenderer>("Terrain");
            TerrainView.Init(Terrain, materials.Terrain);
            Forest = Child<ForestSystem>("Forest");
            Forest.Init(Terrain, materials.Lit, seed);
            Track = Child<TrackSystem>("Railway");
            Track.Init(materials.Lit, materials.UI);
            Buildings = Child<BuildingSystem>("Buildings");
            Buildings.Init(Terrain, materials.Lit);
            Terrain.Protect = (x, z) => Buildings.Contains(new Vector2(x, z), 0.03f);
            Trains = Child<TrainSystem>("Trains");
            Trains.Init(Track, materials.Lit, materials.Smoke, materials.UI);
            Cover = Child<GroundCover>("Ground cover");
            Cover.Init(Terrain, materials.Lit, seed, p => Buildings.Contains(p, 0.01f) || IsWet(p) || (Paths != null && Paths.Covers(p, 0.003f)));
            WaterView = Child<WaterRenderer>("Water");
            WaterView.Init(Terrain, materials.Water);
            Animals = Child<AnimalSystem>("Animals");
            Animals.Init(Terrain, materials.Lit, Habitat, (a, b) => Paths.CrossesBarrier(a, b), Threat);
            Animals.Resting = () => Clock.Hour >= 22.5f || Clock.Hour < 5.5f;
            People = Child<PeopleSystem>("Villagers");
            People.Init(this, materials.Lit);
            Streams = Child<StreamSystem>("Streams");
            Streams.Init(materials.Water);
            Streams.Terrain = Terrain;
            Paths = Child<PathSystem>("Paths");
            Paths.Init(Terrain, materials.Lit, () => Track.Network);
            Bridges = Child<BridgeSystem>("Bridges");
            Bridges.Init(Terrain, materials.Lit, WaterLevelFor);
            Paths.Bridges = Bridges;
            Traffic = Child<TrafficSystem>("Traffic");
            Traffic.Init(this, materials.Lit);
            Sounds = Child<SoundSystem>("Sound");
            Sounds.Init(this);
            Cctv = Child<CctvSystem>("CCTV");
            Cctv.Init(this, materials.Screen);
            Sky = Child<SkySystem>("Sky");
            Sky.Init(this, materials.Lit, seed);
            Boats = Child<BoatSystem>("Boats");
            Boats.Init(this, materials.Lit);
            Boats.Waiting = People.WaitingForBoat;
            Boats.Landed = People.EndBoatTrip;
            People.BoatSeat = Boats.SeatOf;
            // Pets live with people: whose they are, where their people are, walls to sit on, and their voices.
            Animals.OwnerOf = home => People.ResidentNear(home, 0.15f);
            Animals.Outdoors = i => People.OutdoorsAt(i);
            Animals.PerchAt = p => Paths.BarrierTop(p);
            Animals.GroundAt = p => People.GroundHeight(p);
            Animals.Voice += (p, kind) => Sounds.Cue(kind == AnimalKind.Dog ? SoundCue.Bark : SoundCue.Meow, p);
            undo.Clear();
            FitToRoom();
        }

        /// <summary>
        /// Cuts the terrain grid to cover the room (and all existing land and railway) as seen from the world root - a
        /// head start, since the grid also grows on demand. Called when the room becomes known, after loading and moving.
        /// </summary>
        public void FitToRoom()
        {
            roomLocal = room != null && room.Floor.Count >= 3 ? room.ToLocal(transform) : null;
            if (roomLocal != null)
            {
                var need = roomLocal.Bounds;
                var land = Terrain.LandBounds();
                if (land.width > 0f) need = Union(need, Grow(land, TerrainField.RimWidth));
                foreach (var e in Track.Network.Edges)
                    foreach (var p in e.Layout.Points) need = Union(need, new Rect(p.x - 0.15f, p.z - 0.15f, 0.3f, 0.3f));
                need = Grow(need, 0.08f);
                var cells = TerrainField.CellsCovering(need);
                // Very large rooms: keep the grid within budget, centred on the world.
                if (cells.width > TerrainField.MaxCells) cells = new RectInt(Mathf.Clamp(cells.x, -TerrainField.MaxCells / 2 - 1, cells.xMax - TerrainField.MaxCells), cells.y, TerrainField.MaxCells, cells.height);
                if (cells.height > TerrainField.MaxCells) cells = new RectInt(cells.x, Mathf.Clamp(cells.y, -TerrainField.MaxCells / 2 - 1, cells.yMax - TerrainField.MaxCells), cells.width, TerrainField.MaxCells);
                if (cells != Terrain.Cells)
                {
                    Terrain.Resize(cells);
                    TerrainView.Reinit();
                    if (WaterView != null) WaterView.RebuildAll();
                    Cover.Scatter();
                    ApplyPads();
                }
            }
            ApplyAllowed();
            TerrainView.RebuildAll();
            Forest.MarkAllDirty();
        }

        /// <summary>What the room allows, after the safety net (see <see cref="ApplyAllowed"/>).</summary>
        public string RoomStatus { get; private set; } = "no room data";

        /// <summary>
        /// Land may grow anywhere: the room scan only sizes the initial grid (which grows on demand), it does not fence
        /// the player in. The only hard limit is <see cref="TerrainField.MaxReach"/>, a memory backstop.
        /// </summary>
        private void ApplyAllowed()
        {
            Terrain.SetAllowed(null);
            RoomStatus = roomLocal != null ? $"room ({room.Source}) sizes the start; land can grow anywhere" : "no room data: land can grow anywhere";
            Debug.Log($"Wonderland: {RoomStatus}; grid {Terrain.Nx * Terrain.Cell:0.0} x {Terrain.Nz * Terrain.Cell:0.0} m, land {Terrain.LandArea():0.00} m2");
        }

        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        private static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        /// <summary>Whether land may grow at a local point: anywhere within reach (the grid grows to meet it).</summary>
        public bool CanGrowLand(Vector3 local) => Mathf.Abs(local.x) < TerrainField.MaxReach - 0.3f && Mathf.Abs(local.z) < TerrainField.MaxReach - 0.3f;

        /// <summary>
        /// Grows the terrain grid (in 1.2 m steps) so <paramref name="area"/> plus the shore taper around it fits, up to
        /// the memory backstop. Returns whether the grid changed. Growing is a brief hitch, so it happens in big steps.
        /// </summary>
        public bool EnsureGridCovers(Rect area)
        {
            float margin = TerrainField.RimWidth + 0.1f;
            var need = Rect.MinMaxRect(area.xMin - margin, area.yMin - margin, area.xMax + margin, area.yMax + margin);
            if (need.xMin >= Terrain.X0 && need.xMax <= Terrain.XMax && need.yMin >= Terrain.Z0 && need.yMax <= Terrain.ZMax) return false;
            const float step = 1.2f;
            float x0 = need.xMin < Terrain.X0 ? need.xMin - step : Terrain.X0, x1 = need.xMax > Terrain.XMax ? need.xMax + step : Terrain.XMax;
            float z0 = need.yMin < Terrain.Z0 ? need.yMin - step : Terrain.Z0, z1 = need.yMax > Terrain.ZMax ? need.yMax + step : Terrain.ZMax;
            float reach = TerrainField.MaxReach;
            var cells = TerrainField.CellsCovering(Rect.MinMaxRect(Mathf.Max(x0, -reach), Mathf.Max(z0, -reach), Mathf.Min(x1, reach), Mathf.Min(z1, reach)));
            if (cells == Terrain.Cells) return false;
            Terrain.Resize(cells);
            TerrainView.Reinit();
            if (WaterView != null) WaterView.RebuildAll();
            Cover.Scatter();
            Forest.MarkAllDirty();
            Terrain.SetAllowed(null);
            ApplyPads();
            Debug.Log($"Wonderland: terrain grid grew to {Terrain.Nx * Terrain.Cell:0.0} x {Terrain.Nz * Terrain.Cell:0.0} m");
            return true;
        }

        private T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        private bool trackDirty, streamsDirty, pathsDirty, bridgesDirty;
        private float trackUpdated, streamsUpdated, pathsUpdated;

        private void Update()
        {
            if (Terrain != null) Clock.Tick(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (resitHouses) { Buildings.Resit(); resitHouses = false; }
            // Sculpting under the railway: re-profile it a few times per second (and once more after the stroke).
            if (streamsDirty && Time.realtimeSinceStartup - streamsUpdated > 0.3f) UpdateStreamsFromTerrain();
            if (trackDirty && Time.realtimeSinceStartup - trackUpdated > 0.25f) UpdateTrackFromTerrain();
            if (bridgesDirty && !trackDirty && Time.realtimeSinceStartup - pathsUpdated > 0.3f) { bridgesDirty = false; pathsDirty = false; pathsUpdated = Time.realtimeSinceStartup; RefreshBridges(); }
            if (pathsDirty && Time.realtimeSinceStartup - pathsUpdated > 0.3f) { pathsDirty = false; pathsUpdated = Time.realtimeSinceStartup; RefreshBridges(); }
        }

        /// <summary>Sculpting near a stream: re-level it on its course (still flowing downhill) and re-carve its channel.</summary>
        public void UpdateStreamsFromTerrain()
        {
            streamsDirty = false;
            streamsUpdated = Time.realtimeSinceStartup;
            var region = new RectInt(0, 0, 0, 0);
            foreach (var st in Streams.Streams)
            {
                StreamSystem.Relevel(st, Terrain);
                var r = StreamSystem.Carve(Terrain, st);
                TerrainView.Rebuild(r);
            }
            Streams.Rebuild();
            Cover.MarkAllDirty();
            if (Track.HasTrack) trackDirty = true;
        }

        /// <summary>
        /// Re-fits the railway's height profiles to the (sculpted) ground along the same routes, regrades the bed under
        /// pieces that moved and keeps the trains running. The track rises and falls with the land, within its grade limit.
        /// </summary>
        public void UpdateTrackFromTerrain()
        {
            trackDirty = false;
            trackUpdated = Time.realtimeSinceStartup;
            if (!Track.HasTrack) return;
            var net = Track.Network;
            var changed = Reprofile(net, false);
            if (changed.Count == 0) return;
            net.Reindex();
            var region = new RectInt(0, 0, 0, 0);
            foreach (int e in changed)
            {
                var edge = net.Edges[e];
                edge.Tunnel = TrackSystem.TunnelMask(Terrain, edge.Layout);
                Track.RebuildPiece(e, WaterLevelFor, Terrain);
                var r = TrackSystem.GradeTerrain(Terrain, edge.Layout, WaterLevelFor, edge.Tunnel);
                TerrainView.Rebuild(r);
            }
            Track.SnapSignals();
            Track.RebuildFixtures();
            RebuildStations();
            Cover.MarkAllDirty();
            bridgesDirty = true;
        }

        /// <summary>
        /// Gives every piece a smooth, grade-limited height profile from the ground. Switches and crossings get one
        /// height from the ground there, shared by all the pieces meeting at them. Returns the pieces that changed.
        /// </summary>
        private List<int> Reprofile(TrackNetwork net, bool all)
        {
            var jointH = new float[net.Joints.Count];
            for (int j = 0; j < net.Joints.Count; j++) jointH[j] = BedHeightAt(net.Joints[j].Position);
            var crossingH = new float[net.Crossings.Count];
            for (int c = 0; c < net.Crossings.Count; c++) crossingH[c] = BedHeightAt(net.Crossings[c].Position);
            var changed = new List<int>();
            for (int e = 0; e < net.Edges.Count; e++)
            {
                var old = net.Edges[e].Layout;
                var xz = new List<Vector2>(old.Points.Count);
                foreach (var p in old.Points) xz.Add(new Vector2(p.x, p.z));
                var pinned = new Dictionary<int, float>();
                if (net.Edges[e].Joint[0] >= 0) pinned[0] = jointH[net.Edges[e].Joint[0]];
                if (net.Edges[e].Joint[1] >= 0) pinned[xz.Count - 1] = jointH[net.Edges[e].Joint[1]];
                for (int c = 0; c < net.Crossings.Count; c++)
                {
                    var x = net.Crossings[c];
                    if (x.EdgeA == e) pinned[TrackPlanner.IndexAt(old, x.SA)] = crossingH[c];
                    if (x.EdgeB == e) pinned[TrackPlanner.IndexAt(old, x.SB)] = crossingH[c];
                }
                var heights = TrackPlanner.Profile(xz, Terrain, old.Closed, WaterLevelFor, pinned);
                float diff = 0f;
                for (int i = 0; i < xz.Count; i++) diff = Mathf.Max(diff, Mathf.Abs(heights[i] - old.Points[i].y));
                if (!all && diff < 0.0005f) continue;
                var points = new List<Vector3>(xz.Count);
                for (int i = 0; i < xz.Count; i++) points.Add(new Vector3(xz[i].x, heights[i], xz[i].y));
                net.Edges[e].Layout = new TrackLayout(points, old.Closed);
                changed.Add(e);
            }
            // Joint positions follow the bed height.
            for (int j = 0; j < net.Joints.Count; j++) { var q = net.Joints[j].Position; q.y = jointH[j]; net.Joints[j].Position = q; }
            return changed;
        }

        /// <summary>The bed height the ground suggests at a point (averaged over a few centimetres; clear of water).</summary>
        private float BedHeightAt(Vector3 p)
        {
            float h = Terrain.SurfaceAt(p.x, p.z) * 2f;
            foreach (var o in new[] { new Vector2(0.05f, 0f), new Vector2(-0.05f, 0f), new Vector2(0f, 0.05f), new Vector2(0f, -0.05f) })
                h += Terrain.SurfaceAt(p.x + o.x, p.z + o.y);
            h = h / 6f + TrackSystem.BedSink;
            float lvl = WaterLevelFor(new Vector2(p.x, p.z));
            if (lvl >= 0f) h = Mathf.Max(h, lvl + TrackPlanner.BridgeClearance);
            return Mathf.Max(h, TerrainField.MinHeight + 0.006f);
        }

        /// <summary>
        /// Grades a flat pad under a house (cut and fill to its average footprint height, soft 7 cm margin) and protects
        /// it from brushes, so a house never ends up half buried on a slope.
        /// </summary>
        private RectInt PadHouse(HouseInstance h)
        {
            if (!Catalog.Get(h.variant).Pad) return new RectInt(0, 0, 0, 0);   // rocks, camps and small details follow the ground
            float target = Buildings.SitHeight(h, out _);
            // Fully flat and protected out to 2.5 cm beyond the footprint (bilinear sampling reaches one 2 cm cell out),
            // then a soft 7 cm blend into the surrounding ground.
            var e = HouseFactory.HalfExtents(h.variant) + Vector2.one * 0.025f;
            float margin = 0.07f;
            var region = Terrain.RegionAround(new Vector2(h.x, h.z), e.magnitude + margin + 0.02f);
            var inv = Quaternion.Inverse(Quaternion.Euler(0f, h.rotation, 0f));
            for (int z = region.yMin; z < region.yMax; z++)
            for (int x = region.xMin; x < region.xMax; x++)
            {
                var l = inv * new Vector3(Terrain.LocalX(x) - h.x, 0f, Terrain.LocalZ(z) - h.z);
                float dx = Mathf.Max(0f, Mathf.Abs(l.x) - e.x), dz = Mathf.Max(0f, Mathf.Abs(l.z) - e.y);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d >= margin) continue;
                float w = d <= 0f ? 1f : Mathf.SmoothStep(1f, 0f, d / margin);
                int i = z * Terrain.Nx + x;
                if (Terrain.Pad[i] >= 1f) continue;   // another house's flat ground
                Terrain.Height[i] = Mathf.Lerp(Terrain.Height[i], Terrain.HeightForSurface(i, target), w);
                Terrain.Pad[i] = Mathf.Max(Terrain.Pad[i], d <= 0f ? 1f : w * 0.9f);
            }
            Terrain.UpdateSurface(region);
            return region;
        }

        /// <summary>Re-derives every house pad (after houses were removed, moved, restored or loaded).</summary>
        public void ApplyPads()
        {
            System.Array.Clear(Terrain.Pad, 0, Terrain.Pad.Length);
            foreach (var h in Buildings.Houses) TerrainView.Rebuild(PadHouse(h));
            Buildings.Resit();
            Forest.MarkAllDirty();
            RebuildStations();
        }

        // ------------------------------------------------------------------ stations

        public const float PlatformLength = 0.44f;
        private const float StationSetback = 0.112f;   // track centre to station building centre
        private MeshFilter platforms;

        /// <summary>
        /// Where a station would stand for a pointer at <paramref name="local"/>: beside the nearest rails, on the side
        /// pointed at, facing the track. Null when there is no railway close by.
        /// </summary>
        public HouseInstance? StationAt(Vector3 local, int style, int variant = Catalog.Station)
        {
            if (!Track.HasTrack || !Track.Network.Nearest(local, out int edge, out float s, out _, 0.35f)) return null;
            var p = Track.Network.Edges[edge].Layout.Evaluate(s, out var tangent);
            tangent.y = 0f; tangent.Normalize();
            var normal = Vector3.Cross(Vector3.up, tangent);
            float side = Vector3.Dot(normal, local - p) >= 0f ? 1f : -1f;
            var centre = p + normal * side * StationSetback;
            var facing = normal * side;   // the building's back points away from the track; its front (-Z) faces it
            return new HouseInstance { x = centre.x, z = centre.z, rotation = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg, variant = variant, style = style };
        }

        private string ValidateStation(HouseInstance h)
        {
            if (!Track.HasTrack) return "Draw a railway first";
            if (!Track.Network.Nearest(new Vector3(h.x, 0f, h.z), out int edge, out _, out float d, 0.3f) || Mathf.Abs(d - StationSetback) > 0.03f) return "A station goes right beside the railway";
            if (Track.Network.Edges[edge].Layout.Length < PlatformLength * 0.6f) return "The track is too short here for a station";
            return null;
        }

        /// <summary>Generates every station's platform along the rails and tells the trains where to stop.</summary>
        public void RebuildStations()
        {
            if (platforms == null)
            {
                var go = new GameObject("Platforms");
                go.transform.SetParent(transform, false);
                platforms = go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = Materials.Lit; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                platforms.sharedMesh = new Mesh { name = "Platforms" };
            }
            var mesh = platforms.sharedMesh;
            mesh.Clear();
            Trains.Stations.Clear();
            if (!Track.HasTrack) return;
            var net = Track.Network;
            var b = new MeshBuilder();
            foreach (var h in Buildings.Houses)
            {
                if (!Catalog.IsStation(h.variant)) continue;
                var c = new Vector3(h.x, 0f, h.z);
                if (!net.Nearest(c, out int edge, out float s, out _, 0.3f)) continue;
                var layout = net.Edges[edge].Layout;
                var p = layout.Evaluate(s, out var tangent); tangent.y = 0f; tangent.Normalize();
                int side = Vector3.Dot(Vector3.Cross(Vector3.up, tangent), c - p) >= 0f ? 1 : -1;
                float platform = h.variant == Catalog.Halt ? PlatformLength * 0.7f : PlatformLength;   // a halt: a shorter platform
                float s0 = s - platform / 2f, s1 = s + platform / 2f;
                if (!layout.Closed) { s0 = Mathf.Max(0.02f, s0); s1 = Mathf.Min(layout.Length - 0.02f, s1); }
                HouseFactory.Platform(b, layout, s0, s1, side);
                Trains.Stations.Add(new StationStop { Edge = edge, S = s, Side = side, House = new Vector2(h.x, h.z) });
            }
            b.ToMesh("Platforms", mesh);
            mesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ undo

        private sealed class Snapshot
        {
            public RectInt Cells;
            public float[] Height, WaterLevel;
            public byte[] Land, Lock, Soil, Water;   // 0..1 masks, quantised: snapshots of a room-sized grid stay small
            public TreeInstance[] Trees;
            public HouseInstance[] Houses;
            public AnimalInstance[] Animals;
            public PersonInstance[] People;
            public List<StreamPath> Streams;
            public List<PathLine> Paths;
            public List<BridgeChoice> Bridges;
            public TrackNetwork.Data Network;
            public List<TrainSystem.TrainData> TrainList;
            public List<SignalInstance> Signals;
            public SwitchMode Mode;
            public BoatInstance[] Boats;
        }

        private static byte[] Quantise(float[] v)
        {
            var b = new byte[v.Length];
            for (int i = 0; i < v.Length; i++) b[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(v[i]) * 255f);
            return b;
        }

        private static float[] Expand(byte[] b)
        {
            var v = new float[b.Length];
            for (int i = 0; i < b.Length; i++) v[i] = b[i] / 255f;
            return v;
        }

        private Snapshot Capture() => new()
        {
            Cells = Terrain.Cells,
            Height = (float[])Terrain.Height.Clone(), Land = Quantise(Terrain.Land), Lock = Quantise(Terrain.Lock), Soil = Quantise(Terrain.Soil),
            Water = Quantise(Terrain.Water), WaterLevel = (float[])Terrain.WaterLevel.Clone(),
            Trees = Forest.Trees.ToArray(), Houses = Buildings.Houses.ToArray(), Animals = Animals.Animals.ToArray(), People = People.Export(),
            Streams = CopyStreams(Streams.Streams), Paths = PathSystem.Copy(Paths.Lines), Bridges = new List<BridgeChoice>(Bridges.Choices),
            Network = Track.Network.ToData(), TrainList = Trains.ToData(), Signals = new List<SignalInstance>(Track.Signals), Mode = Trains.Mode,
            Boats = Boats.Export(),
        };

        private static List<StreamPath> CopyStreams(List<StreamPath> streams)
        {
            var copy = new List<StreamPath>(streams.Count);
            foreach (var st in streams) copy.Add(new StreamPath { points = new List<Vector3>(st.points) });
            return copy;
        }

        private void Restore(Snapshot s)
        {
            if (s.Cells != Terrain.Cells) { Terrain.Resize(s.Cells); TerrainView.Reinit(); Cover.Scatter(); }
            Terrain.CopyFrom(s.Height, Expand(s.Land), Expand(s.Lock), Expand(s.Soil), s.Water != null ? Expand(s.Water) : null, s.WaterLevel);
            TerrainView.RebuildAll();
            WaterView.RebuildAll();
            Forest.SetTrees(s.Trees);
            Buildings.SetHouses(s.Houses);
            ApplyPads();
            Animals.SetAnimals(s.Animals ?? new AnimalInstance[0]);
            People.SetPeople(s.People ?? new PersonInstance[0]);
            Streams.SetStreams(s.Streams ?? new List<StreamPath>());
            Paths.SetLines(s.Paths);
            Track.Signals.Clear();
            if (s.Signals != null) Track.Signals.AddRange(s.Signals);
            Trains.Mode = s.Mode;
            Track.SetNetwork(TrackNetwork.FromData(s.Network), WaterLevelFor, Terrain);
            Trains.FromData(s.TrainList);
            Bridges.Choices.Clear();
            if (s.Bridges != null) Bridges.Choices.AddRange(s.Bridges);
            RefreshBridges();
            Boats.SetBoats(s.Boats);
            RebuildStations();
            Cover.MarkAllDirty();
            if (room != null) FitToRoom();
        }

        /// <summary>Remember the world before an operation, so Undo can return to it.</summary>
        public void Checkpoint()
        {
            undo.Add(Capture());
            redo.Clear();            // a new step: what was undone stays undone
            People.WorldChanged();   // whatever comes next changes where people can walk
            long perSnapshot = (long)Terrain.Nx * Terrain.Nz * 12;
            int limit = (int)Mathf.Clamp(UndoBudgetBytes / Mathf.Max(1L, perSnapshot), 4, MaxUndo);
            while (undo.Count > limit) undo.RemoveAt(0);
        }

        /// <summary>Back one step (the player's Undo: it can be redone).</summary>
        public bool Undo() => Undo(true);

        /// <summary>Back one step; <paramref name="redoable"/> false for taking back a step that didn't work out (a refused railway).</summary>
        public bool Undo(bool redoable)
        {
            if (undo.Count == 0) return false;
            var s = undo[^1];
            undo.RemoveAt(undo.Count - 1);
            if (redoable) redo.Add(Capture());
            Restore(s);
            return true;
        }

        /// <summary>Forward again after an Undo (until something new is done).</summary>
        public bool Redo()
        {
            if (redo.Count == 0) return false;
            var s = redo[^1];
            redo.RemoveAt(redo.Count - 1);
            undo.Add(Capture());
            Restore(s);
            return true;
        }

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        private readonly List<Snapshot> redo = new();

        // ------------------------------------------------------------------ terrain

        public void TerrainStroke(TerrainBrush brush, Vector3 local, float radius, float strength, float dt, float flattenTarget)
        {
            var centre = new Vector2(local.x, local.z);
            if (brush == TerrainBrush.Grow)
                EnsureGridCovers(new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f));
            var region = Terrain.ApplyBrush(brush, centre, radius, strength, dt, flattenTarget);
            if (region.width == 0) return;
            TerrainView.Rebuild(region);
            People.WorldChanged();
            if (Track.HasTrack && brush != TerrainBrush.Grow && brush != TerrainBrush.Trim && TouchesTrack(centre, radius)) trackDirty = true;
            if (Paths.Lines.Count > 0 && Paths.Covers(centre, radius + 0.04f)) pathsDirty = true;
            if (Streams.Streams.Count > 0 && brush != TerrainBrush.Grow && brush != TerrainBrush.Trim && Streams.DistanceTo(centre, out _) < radius + 0.05f) streamsDirty = true;
            // Land edits move the shore (and its taper) up to a rim width beyond the brush.
            float reach = brush == TerrainBrush.Grow || brush == TerrainBrush.Trim ? radius + TerrainField.RimWidth : radius;
            Forest.MarkDirtyRegion(centre, reach);
            Cover.MarkDirtyRegion(centre, reach);
            if (brush == TerrainBrush.Trim)
                Forest.RemoveWhere(t => (new Vector2(t.x, t.z) - centre).sqrMagnitude < reach * reach && Terrain.EdgeAt(t.x, t.z) < 0.3f);
            resitHouses = true;
        }

        // ------------------------------------------------------------------ forest

        /// <summary>Pond or stream water at a point.</summary>
        public bool IsWet(Vector2 p) => Terrain.WaterAt(p.x, p.y) > 0.2f || (Streams != null && Streams.IsWet(p, 0.004f));

        /// <summary>Water surface height at a point (pond or stream), or -1 when dry: where the railway needs a bridge.</summary>
        public float WaterLevelFor(Vector2 p)
        {
            if (Terrain.WaterAt(p.x, p.y) > 0.3f) return Terrain.WaterLevelAt(p.x, p.y);
            if (Streams != null && Streams.DistanceTo(p, out float level) < StreamSystem.HalfWidth + 0.008f) return level;
            return -1f;
        }

        // ------------------------------------------------------------------ streams

        /// <summary>Plans a stream from a drawn stroke, carves it and bridges the railway over it. Returns an error or null.</summary>
        public string BuildStream(IReadOnlyList<Vector3> drawn)
        {
            var path = StreamSystem.Plan(drawn, Terrain);
            if (path == null) return "Draw a longer stream";
            foreach (var p in path.points)
                if (Buildings.Contains(new Vector2(p.x, p.z), StreamSystem.HalfWidth)) return "Streams flow around houses";
            Checkpoint();
            Streams.Streams.Add(path);
            AfterStreamsChanged(StreamSystem.Carve(Terrain, path));
            return null;
        }

        private void AfterStreamsChanged(RectInt region)
        {
            Streams.Rebuild();
            TerrainView.Rebuild(region);
            Forest.RemoveWhere(t => IsWet(new Vector2(t.x, t.z)));
            Forest.MarkAllDirty();
            Cover.MarkAllDirty();
            RemoveStrandedAnimals();
            if (Track.HasTrack) UpdateTrackFromTerrain();   // lifts the railway onto a bridge where a stream now crosses it
            RefreshBridges();
        }

        public bool RemoveStreamAt(Vector3 local)
        {
            int i = Streams.Pick(new Vector2(local.x, local.z), 0.035f);
            if (i < 0) return false;
            Checkpoint();
            Streams.Streams.RemoveAt(i);
            AfterStreamsChanged(new RectInt(0, 0, 0, 0));
            return true;
        }

        // ------------------------------------------------------------------ animals

        /// <summary>Grass an animal may stand on: land away from the shore, dry, off the railway, clear of houses.</summary>
        public bool IsFreeGrass(Vector2 p, float radius)
        {
            if (Terrain.EdgeAt(p.x, p.y) < 0.45f || Terrain.WaterAt(p.x, p.y) > 0.05f || Terrain.LockAt(p.x, p.y) > 0.15f) return false;
            if (Streams != null && Streams.IsWet(p, radius + 0.01f)) return false;
            if (Track.Near(new Vector3(p.x, 0f, p.y), TrackSystem.BedHalfWidth + radius)) return false;
            if (Paths != null && Paths.NearBarrier(p, radius * 0.6f)) return false;
            return !Buildings.Contains(p, radius);
        }

        /// <summary>
        /// Where an animal may be: ducks on ponds; everyone else on open grass, and in the same enclosure (fenced pasture,
        /// or outside all of them) as its home.
        /// </summary>
        public bool Habitat(Vector2 p, AnimalKind kind, Vector2 home)
        {
            if (AnimalFactory.Pet(kind)) return People.Walkable(p);
            if (kind == AnimalKind.Duck)
                return Terrain.WaterAt(p.x, p.y) > 0.55f && Terrain.EdgeAt(p.x, p.y) > 0.3f && !Track.Network.Nearest(new Vector3(p.x, 0f, p.y), out _, out _, out _, 0.05f);
            if (!IsFreeGrass(p, AnimalFactory.Radius(kind) * 0.7f)) return false;
            return Paths.EnclosureAt(p) == Paths.EnclosureAt(home);
        }

        /// <summary>The nearest person or moving train within <paramref name="within"/> of a point (deer keep away).</summary>
        private Vector2? Threat(Vector2 p, float within)
        {
            var who = People.NearestPerson(p, within);
            if (who.HasValue) return who;
            foreach (var t in Trains.Trains)
            {
                if (t.Speed < 0.02f) continue;
                var loco = Trains.LocoPosition(t);
                if (new Vector2(loco.x - p.x, loco.z - p.y).magnitude < within * 1.6f) return new Vector2(loco.x, loco.z);
            }
            return null;
        }

        private int TreesNear(Vector2 p, float within)
        {
            int n = 0;
            foreach (var t in Forest.Trees) if (new Vector2(t.x - p.x, t.z - p.y).sqrMagnitude < within * within) n++;
            return n;
        }

        /// <summary>The fête going on now, if any.</summary>
        public VillageFete Fete => People.Deciders.Find(d => d is VillageFete && d.Active) as VillageFete;

        /// <summary>Calls the village to a fête round its main gathering place (or ends the one going on).</summary>
        public string HoldFete()
        {
            var on = Fete;
            if (on != null) { People.Deciders.Remove(on); return "The fête is called off"; }
            if (VillageClock.IsNight(Clock.Hour)) return "Everyone's asleep - hold the fête by day";
            int venue = VillageFete.Venue(this);
            if (venue < 0) return "A fête needs somewhere to gather: build a fountain, a bandstand or a well";
            if (People.People.Count == 0) return "Nobody lives here yet to come to a fête";
            var fete = new VillageFete(this, venue);
            People.Deciders.Add(fete);
            fete.Invite();
            return $"A fête at the {Catalog.Get(Buildings.Houses[venue].variant).Name.ToLower()} - everyone's invited!";
        }

        public string PlacePerson(Vector3 local, int style, int seed)
        {
            var p = new Vector2(local.x, local.z);
            if (!People.Walkable(p)) return "Villagers need dry land, off the rails";
            People.Add(new PersonInstance { x = p.x, z = p.y, style = style, seed = seed });
            return null;
        }

        public string PlaceAnimal(Vector3 local, AnimalKind kind, int seed)
        {
            var p = new Vector2(local.x, local.z);
            if (kind == AnimalKind.Duck) { if (!Habitat(p, kind, p)) return "Ducks need a pond to swim on"; }
            else if (AnimalFactory.Pet(kind)) { if (!People.Walkable(p)) return "Pets need dry land, off the rails - near their people's home"; }
            else if (!IsFreeGrass(p, AnimalFactory.Radius(kind))) return "Animals need open grass";
            else if (kind == AnimalKind.Deer && TreesNear(p, 0.25f) < 3) return "Deer like to live near the woods";
            Animals.Add(new AnimalInstance { x = p.x, z = p.y, kind = (int)kind, seed = seed });
            return null;
        }

        /// <summary>Animals whose home is no longer grass (new railway, a pond) wander off the map; villagers move house.</summary>
        private void RemoveStrandedAnimals()
        {
            for (int i = Animals.Animals.Count - 1; i >= 0; i--)
            {
                var a = Animals.Animals[i];
                var home = new Vector2(a.x, a.z);
                if (!Habitat(home, (AnimalKind)a.kind, home)) Animals.RemoveAt(i);
            }
            People.Rehome();
        }

        // ------------------------------------------------------------------ water

        /// <summary>The water level a Pond stroke starting here uses: the existing pond's, else just below the ground.</summary>
        public float WaterLevelForStroke(Vector3 local)
        {
            float existing = Terrain.WaterLevelAt(local.x, local.z);
            if (existing > 0f) return existing;
            return Mathf.Max(Terrain.SurfaceAt(local.x, local.z) - 0.003f, TerrainField.MinHeight + 0.01f);
        }

        public void WaterStroke(Vector3 local, float radius, float strength, float dt, float level, bool drain)
        {
            var centre = new Vector2(local.x, local.z);
            var region = Terrain.PaintWater(centre, radius, strength, dt, level, drain);
            TerrainView.Rebuild(region);
            People.WorldChanged();
            WaterView.Rebuild(new RectInt(region.x - 2, region.y - 2, region.width + 4, region.height + 4));
            Forest.RemoveWhere(t => (new Vector2(t.x, t.z) - centre).sqrMagnitude < radius * radius * 1.2f && Terrain.WaterAt(t.x, t.z) > 0.2f);
            if (!drain) RemoveStrandedAnimals();
            Forest.MarkDirtyRegion(centre, radius);
            Cover.MarkDirtyRegion(centre, radius);
            if (Track.HasTrack && TouchesTrack(centre, radius)) trackDirty = true;   // the railway rises onto a bridge
            bridgesDirty = true;
        }

        public bool CanGrowTree(Vector2 p, float footprint)
        {
            if (Terrain.WaterAt(p.x, p.y) > 0.1f || Streams.IsWet(p, footprint * 0.5f + 0.008f)) return false;
            if (Terrain.LockAt(p.x, p.y) > 0.25f) return false;
            if (Track.DistanceTo(new Vector3(p.x, 0f, p.y)) < TrackSystem.BedHalfWidth + footprint * 0.5f + 0.008f) return false;
            if (Paths.Covers(p, footprint * 0.5f)) return false;
            return !Buildings.Contains(p, footprint * 0.6f);
        }

        public int PaintForest(Vector3 local, float radius, float density, float dt) =>
            Forest.Paint(new Vector2(local.x, local.z), radius, density, dt, CanGrowTree);

        public int EraseForest(Vector3 local, float radius, float strength, float dt) =>
            Forest.Erase(new Vector2(local.x, local.z), radius, strength, dt);

        // ------------------------------------------------------------------ railway

        /// <summary>
        /// Plans new track from a drawn stroke and, when valid, adds it to the network (checkpointed): extending a line,
        /// branching off or joining with switches, crossing other track, or a new loop. Nothing existing is replaced.
        /// </summary>
        public TrackPlan BuildTrack(IReadOnlyList<Vector3> drawn)
        {
            // Railway drawn over bare floor grows its own land: checkpoint first so undo also takes the new land back.
            Checkpoint();
            var plan = TrackPlanner.Plan(drawn, Terrain, Track.Network, ensureLand: path =>
            {
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var p in path) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
                EnsureGridCovers(Rect.MinMaxRect(minX - 0.25f, minZ - 0.25f, maxX + 0.25f, maxZ + 0.25f));
                var region = Terrain.GrowLandAlong(path, 0.22f);
                TerrainView.Rebuild(region);
                Cover.MarkAllDirty();
                return true;
            });
            if (!plan.Ok) { Undo(false); return plan; }
            var net = Track.Network;
            var before = net.Clone();
            Attach(net, plan);
            // One way: the new piece runs the way the track it joins runs (a loop being joined turns to match).
            net.Orient(e => net.Agreement(e, before), out _);
            if (net.NewTurnBack(before)) { Undo(false); plan.Error = TrackNetwork.TurnBack; return plan; }
            AfterNetworkChanged();
            return plan;
        }

        /// <summary>
        /// What letting go of a stroke now would do, without changing anything: the planned line (or why it can't be
        /// built), which way trains will run on it (+1 the way it was drawn, -1 the other way), and a note when other
        /// track will turn round to match. The same planning as <see cref="BuildTrack"/>, done on a copy of the network.
        /// </summary>
        public TrackPlan PreviewTrack(IReadOnlyList<Vector3> drawn, out int direction, out string note)
        {
            direction = 0; note = null;
            var net = Track.Network;
            var plan = TrackPlanner.Plan(drawn, Terrain, net, null, landWillGrow: true);
            if (!plan.Ok || plan.Path == null) return plan;
            if (plan.Closed || net.Empty) { direction = 1; return plan; }   // a new loop on its own: it runs the way it was drawn
            var before = net.Clone();
            var copy = net.Clone();
            Attach(copy, plan);
            copy.Orient(e => copy.Agreement(e, before), out _);
            if (copy.NewTurnBack(before)) { plan.Error = TrackNetwork.TurnBack; return plan; }
            copy.Reindex();
            int m = plan.Path.Count / 2;
            var a = plan.Path[Mathf.Max(0, m - 2)]; var b = plan.Path[Mathf.Min(plan.Path.Count - 1, m + 2)];
            if (copy.Nearest(new Vector3(a.x, 0f, a.y), out int edge, out float s, out _, 0.01f))
            {
                copy.Edges[edge].Layout.Evaluate(s, out var t);
                direction = Vector2.Dot(new Vector2(t.x, t.z), b - a) >= 0f ? 1 : -1;
            }
            for (int e = 0; e < copy.Edges.Count && note == null; e++) if (copy.Agreement(e, before) < -0.05f) note = "the track it joins turns round to run the same way";
            if (copy.Traps > before.Traps) note = (note != null ? note + "; " : "") + "trains will get in but not back - add a link the other way too";
            return plan;
        }

        /// <summary>Adds a planned piece to the network: splits pieces where it branches off or joins, makes the switches.</summary>
        private void Attach(TrackNetwork net, TrackPlan plan)
        {
            var pts = plan.Path.ConvertAll(p => new Vector3(p.x, Terrain.SurfaceAt(p.x, p.y), p.y));
            if (plan.Closed) { net.AddEdge(new TrackLayout(pts, true)); return; }
            var startEnds = PrepareAttach(net, plan.Start, out var startAt);
            var finishEnds = PrepareAttach(net, plan.Finish, out var finishAt);
            // Splitting a piece for the finish can move the start's track end onto the new half (a line drawn from a
            // dead end back onto its own piece): find the ends that really are at the start now.
            if (startEnds != null)
                for (int i = 0; i < startEnds.Count; i++)
                {
                    var p = net.EndPoint(startEnds[i]);
                    if (new Vector2(p.x - startAt.x, p.z - startAt.z).magnitude <= 0.002f) continue;
                    var real = net.FreeEndAt(startAt, 0.002f, startEnds);
                    if (real.Edge >= 0) startEnds[i] = real;
                }
            if (startEnds != null) pts[0] = startAt;
            if (finishEnds != null) pts[^1] = finishAt;
            int e = net.AddEdge(new TrackLayout(pts, false));
            if (startEnds != null) { startEnds.Add(new EdgeEnd(e, 0)); net.Join(startAt, startEnds.ToArray()); }
            if (finishEnds != null) { finishEnds.Add(new EdgeEnd(e, 1)); net.Join(finishAt, finishEnds.ToArray()); }
            net.Normalize();
        }

        /// <summary>The track ends a new piece joins at an attachment (splitting a piece for a new switch), and the point.</summary>
        private static List<EdgeEnd> PrepareAttach(TrackNetwork net, TrackAttach a, out Vector3 at)
        {
            at = default;
            var p = new Vector3(a.Point.x, 0f, a.Point.y);
            if (a.Kind == AttachKind.End)
            {
                float best = 0.02f; var found = new EdgeEnd(-1, 0);
                for (int e = 0; e < net.Edges.Count; e++)
                {
                    if (net.Edges[e].Closed) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        if (net.Edges[e].Joint[k] >= 0) continue;
                        var q = net.EndPoint(new EdgeEnd(e, k));
                        float d = new Vector2(q.x - p.x, q.z - p.z).magnitude;
                        if (d < best) { best = d; found = new EdgeEnd(e, k); }
                    }
                }
                if (found.Edge < 0) return null;
                at = net.EndPoint(found);
                return new List<EdgeEnd> { found };
            }
            if (a.Kind == AttachKind.Mid)
            {
                net.Reindex();
                if (!net.Nearest(p, out int edge, out float s, out _, 0.02f)) return null;
                var l = net.Edges[edge].Layout;
                int index = TrackPlanner.IndexAt(l, s);
                at = l.Points[index];
                var (arriving, leaving) = net.Split(edge, index);
                net.Reindex();
                return new List<EdgeEnd> { arriving, leaving };
            }
            return null;
        }

        /// <summary>
        /// After pieces were added or removed: profiles the network, regrades the corridors, rebuilds the meshes, clears
        /// trees and houses in the way, puts the trains back on their track and refreshes stations and signals.
        /// </summary>
        private void AfterNetworkChanged(System.Action placeTrains = null)
        {
            var net = Track.Network;
            net.Reindex();
            Reprofile(net, true);
            TrackSystem.UnlockTerrain(Terrain);
            Track.SetNetwork(net, WaterLevelFor, Terrain);
            foreach (var e in net.Edges) TrackSystem.GradeTerrain(Terrain, e.Layout, WaterLevelFor, e.Tunnel);
            Terrain.UpdateSurface(Terrain.All);
            TerrainView.RebuildAll();
            Forest.RemoveWhere(t => net.DistanceTo(new Vector3(t.x, 0f, t.z)) < TrackSystem.BedHalfWidth + TreeFactory.Radius((TreeKind)t.kind) * t.scale * 0.5f + 0.006f);
            Forest.MarkAllDirty();
            for (int i = Buildings.Houses.Count - 1; i >= 0; i--)
            {
                var h = Buildings.Houses[i];
                bool gone = Catalog.IsStation(h.variant) ? ValidateStation(h) != null
                    : Buildings.Validate(h, i, p => Track.DistanceTo(new Vector3(p.x, 0f, p.y))) == "That's the railway";
                if (gone) Buildings.RemoveAt(i);
            }
            for (int i = Track.Signals.Count - 1; i >= 0; i--)
                if (i >= Track.SignalSpots.Count || Track.SignalSpots[i].edge < 0) Track.Signals.RemoveAt(i);
            Track.SnapSignals();
            Track.RebuildFixtures();
            ApplyPads();
            RemoveStrandedAnimals();
            People.Rehome();
            Buildings.Resit();
            Cover.MarkAllDirty();
            if (placeTrains != null) placeTrains(); else Trains.Relocate();
            RebuildStations();
            RefreshBridges();
        }

        /// <summary>Re-derives every bridge (from the railway, the ways and the water) and lays the ways onto their decks.</summary>
        public void RefreshBridges()
        {
            Bridges.Rebuild(Track.Network, Paths.Lines);
            Paths.Rebuild();
        }

        /// <summary>Tap on a bridge: the next style that suits it (checkpointed). Returns its name, or null if there is no bridge.</summary>
        public string CycleBridge(Vector3 local)
        {
            int i = Bridges.Pick(new Vector2(local.x, local.z));
            if (i < 0) return null;
            Checkpoint();
            var style = Bridges.Cycle(i);
            RefreshBridges();
            return BridgeSystem.Names[(int)style];
        }

        /// <summary>Where a grip on a piece of track would bend it to (no change yet): an error, and the preview line.</summary>
        public string ReshapePreview(int edge, int index, Vector3 target, out List<Vector3> preview) =>
            TrackPlanner.Reshape(Track.Network, edge, index, new Vector2(target.x, target.z), out _, out preview, Terrain);

        /// <summary>Bends a piece of track (checkpointed): its point <paramref name="index"/> moves to <paramref name="target"/>.</summary>
        public string Reshape(int edge, int index, Vector3 target)
        {
            var net = Track.Network;
            string error = TrackPlanner.Reshape(net, edge, index, new Vector2(target.x, target.z), out var dense, out _, Terrain);
            if (error != null) return error;
            Checkpoint();
            var old = net.Edges[edge].Layout;
            var pts = dense.ConvertAll(p => new Vector3(p.x, Terrain.SurfaceAt(p.x, p.y), p.y));
            net.Edges[edge].Layout = new TrackLayout(pts, old.Closed);
            float ratio = net.Edges[edge].Layout.Length / Mathf.Max(0.01f, old.Length);
            AfterNetworkChanged(() => Trains.Rebase(edge, ratio));
            return null;
        }

        /// <summary>Takes away the whole railway (checkpointed).</summary>
        public void RemoveTrack()
        {
            Checkpoint();
            Trains.RemoveAll();
            Track.Signals.Clear();
            Track.SetNetwork(new TrackNetwork());
            TrackSystem.UnlockTerrain(Terrain);
            TerrainView.RebuildAll();
            RebuildStations();
            Cover.MarkAllDirty();
        }

        /// <summary>Erases the piece of track under a point (from switch to switch); returns whether one went.</summary>
        public bool RemovePieceAt(Vector3 local)
        {
            var net = Track.Network;
            if (!net.Nearest(local, out int edge, out _, out _, 0.045f)) return false;
            Checkpoint();
            var before = net.Clone();
            net.RemoveEdge(edge);
            net.Orient(e => net.Agreement(e, before), out _);
            AfterNetworkChanged();
            return true;
        }

        // ------------------------------------------------------------------ trains, switches, signals

        /// <summary>Puts a new train on the track at the point nearest <paramref name="local"/>, its locomotive towards
        /// <paramref name="facing"/>, and sets it running; returns an error or null.</summary>
        public string PlaceTrain(Vector3 local, TrainKind kind, Vector3 facing, out Train train)
        {
            train = null;
            if (!Track.HasTrack) return "Draw a railway first";
            if (!Track.Network.Nearest(local, out int edge, out float s, out _, 0.08f)) return "Point at the railway to place a train";
            Track.Network.Edges[edge].Layout.Evaluate(s, out var tangent);
            int orientation = Vector3.Dot(new Vector3(tangent.x, 0f, tangent.z), new Vector3(facing.x, 0f, facing.z)) >= 0f ? 1 : -1;
            int livery = NextLivery();
            Checkpoint();
            train = Trains.Add(kind, livery, edge, s, orientation, out string error);
            if (train == null) { Undo(false); return error; }
            train.Running = true;
            return null;
        }

        /// <summary>Puts a train down without a checkpoint (moving a train that was picked up, already checkpointed).</summary>
        public string PlaceTrainNoCheckpoint(Vector3 local, TrainKind kind, Vector3 facing, out Train train)
        {
            train = null;
            if (!Track.Network.Nearest(local, out int edge, out float s, out _, 0.08f)) return "Put trains down on the track";
            Track.Network.Edges[edge].Layout.Evaluate(s, out var tangent);
            int orientation = Vector3.Dot(new Vector3(tangent.x, 0f, tangent.z), new Vector3(facing.x, 0f, facing.z)) >= 0f ? 1 : -1;
            train = Trains.Add(kind, NextLivery(), edge, s, orientation, out string error);
            return train == null ? error : null;
        }

        private int NextLivery()
        {
            for (int k = 0; k < TrainSystem.LiveryCount; k++)
            {
                bool used = false;
                foreach (var t in Trains.Trains) used |= t.Livery == k;
                if (!used) return k;
            }
            return Trains.Trains.Count % TrainSystem.LiveryCount;
        }

        public void SetTrainsRunning(bool running) { foreach (var t in Trains.Trains) t.Running = running; }

        /// <summary>Throws a switch by hand. In Explore / Random mode the first tap also pins it (trains leave it alone), the
        /// next tap throws it back and lets the trains decide again. Returns a message for the player.</summary>
        public string FlipSwitch(int joint)
        {
            if (joint < 0 || joint >= Track.Network.Joints.Count) return null;
            if (Trains.JointLocked(joint)) return "Wait - a train is on the switch";
            var j = Track.Network.Joints[joint];
            j.Setting = 1 - j.Setting;
            j.FlipPending = false;
            string message;
            if (Trains.Mode == SwitchMode.Manual) { j.Pinned = false; message = "Switch thrown"; }
            else { j.Pinned = !j.Pinned; message = j.Pinned ? "Switch set - trains will keep this way" : "Switch free - trains choose again"; }
            Track.RebuildFixtures();
            return message;
        }

        /// <summary>Puts a stop signal beside the track next to <paramref name="local"/> (checkpointed).</summary>
        public string PlaceSignal(Vector3 local)
        {
            if (!Track.HasTrack || !Track.Network.Nearest(local, out int edge, out float s, out _, 0.12f)) return "Point beside the railway";
            if (Track.Network.DistanceToJoint(edge, s) < TrackNetwork.FoulDistance + 0.05f) return "Too close to a switch";
            var p = Track.Network.Edges[edge].Layout.Evaluate(s, out var t); t.y = 0f;
            var normal = Vector3.Cross(Vector3.up, t.normalized);
            float side = Vector3.Dot(normal, local - p) >= 0f ? 1f : -1f;
            var at = p + normal * side * 0.055f;
            if (Track.PickSignal(at, 0.08f) >= 0) return "There is a signal here already";
            Checkpoint();
            Track.Signals.Add(new SignalInstance { x = at.x, z = at.z, red = false });   // automatic (tap to hold it at red)
            Track.SnapSignals();
            Track.RebuildFixtures();
            return null;
        }

        public bool ToggleSignal(int i)
        {
            if (i < 0 || i >= Track.Signals.Count) return false;
            var sig = Track.Signals[i]; sig.red = !sig.red; Track.Signals[i] = sig;
            return sig.red;
        }

        // ------------------------------------------------------------------ houses

        public string ValidateHouse(HouseInstance h, int ignore = -1)
        {
            int boat = Catalog.BoatOf(h.variant);
            if (boat >= 0) return Boats.Validate(new Vector2(h.x, h.z), h.rotation + 180f, boat);
            if (h.variant == Catalog.Jetty && ValidateJetty(h) is { } jetty) return jetty;
            string error = (Catalog.IsStation(h.variant) ? ValidateStation(h) : null) ?? Buildings.Validate(h, ignore, p => Track.DistanceTo(new Vector3(p.x, 0f, p.y)));
            if (error == null)
                foreach (var p in BuildingSystem.FootprintSamples(h))
                {
                    if (Streams.IsWet(p, 0.01f)) return "That's a stream";
                    if (Paths.NearBarrier(p, 0.004f)) return "That's a fence";
                    if (!Catalog.Get(h.variant).Small && Paths.WayDistance(p, out int kind) < -0.004f) return kind == 0 ? "That's the road" : "That's the path";
                }
            return error;
        }

        /// <summary>A landing stage stands at a pond's edge: its land end on the bank, its far end out over the water.</summary>
        private string ValidateJetty(HouseInstance h)
        {
            var rot = Quaternion.Euler(0f, h.rotation, 0f);
            float half = Catalog.Get(Catalog.Jetty).Half.y;
            var tip = new Vector3(h.x, 0f, h.z) + rot * new Vector3(0f, 0f, -half * 0.9f);
            var back = new Vector3(h.x, 0f, h.z) + rot * new Vector3(0f, 0f, half * 0.8f);
            if (Terrain.WaterAt(back.x, back.z) > 0.2f) return "Its land end must be on the bank - turn it to face the water";
            if (Terrain.WaterAt(tip.x, tip.z) < 0.5f) return "Its end must reach out over a pond";
            var dock = BoatSystem.DockOf(h);
            if (Terrain.WaterAt(dock.x, dock.y) < 0.6f) return "Boats couldn't come alongside - reach further out";
            return null;
        }

        /// <summary>How high the ghost of a building (or boat) sits: boats float, a landing stage is level with its bank.</summary>
        public float SitHeightFor(HouseInstance h)
        {
            if (Catalog.BoatOf(h.variant) >= 0) return Terrain.WaterLevelAt(h.x, h.z);
            return Buildings.SitHeight(h, out _);
        }

        private bool TouchesTrack(Vector2 centre, float radius)
        {
            float reach = radius + TrackSystem.CorridorHalfWidth;
            return Track.Network.Nearest(new Vector3(centre.x, 0f, centre.y), out _, out _, out _, reach);
        }

        public string PlaceHouse(HouseInstance h, bool checkpoint = true)
        {
            string error = ValidateHouse(h);
            if (error != null) return error;
            int boat = Catalog.BoatOf(h.variant);
            if (boat >= 0)
            {
                if (checkpoint) Checkpoint();
                Boats.Add(new BoatInstance { x = h.x, z = h.z, heading = h.rotation + 180f, kind = boat, seed = Mathf.Abs(Mathf.RoundToInt(h.x * 7919f + h.z * 104729f)) + Boats.Boats.Count });
                return null;
            }
            if (checkpoint) Checkpoint();
            float r = BuildingSystem.Radius(h.variant) * 0.85f;
            Forest.RemoveWhere(t => new Vector2(t.x - h.x, t.z - h.z).magnitude < r + TreeFactory.Radius((TreeKind)t.kind) * t.scale * 0.4f);
            Buildings.Add(h);
            TerrainView.Rebuild(PadHouse(h));
            Buildings.Resit();
            RebuildStations();
            People.Rehome();
            Forest.MarkDirtyRegion(new Vector2(h.x, h.z), r + 0.1f);
            Cover.MarkDirtyRegion(new Vector2(h.x, h.z), r + 0.1f);
            if (checkpoint) MoveIn(Buildings.Houses.Count - 1);
            return null;
        }

        public const int MaxPeople = 40;

        /// <summary>A new home brings its people: they appear at the door and live there.</summary>
        private void MoveIn(int index)
        {
            var h = Buildings.Houses[index];
            var bp = Catalog.Get(h.variant);
            int door = System.Array.FindIndex(bp.Spots, sp => sp.Kind == SpotKind.Door);
            for (int k = 0; k < bp.Residents && People.People.Count < MaxPeople; k++)
            {
                var at = door >= 0 ? Buildings.SpotPosition(index, door, out _) : new Vector3(h.x, 0f, h.z);
                var rng = new Rng(Mathf.RoundToInt(h.x * 9173f + h.z * 7919f) + k * 31);
                var spot = new Vector3(at.x + rng.Range(-0.012f, 0.012f), 0f, at.z + rng.Range(-0.006f, 0.006f));
                PlacePerson(spot, rng.Range(0, PersonFactory.StyleCount), rng.Range(1, 1 << 30));
            }
        }

        // ------------------------------------------------------------------ paths, fences and hedges

        /// <summary>Draws a road, lane, path, trail, fence, hedge or wall from a stroke (checkpointed); an error or null.</summary>
        public string BuildPath(IReadOnlyList<Vector3> drawn, PathKind kind)
        {
            var line = Paths.Plan(drawn, (int)kind, out string error);
            if (line == null) return error;
            Checkpoint();
            Paths.Add(line);
            var region = Paths.Grade(line);
            if (region.width > 0) { TerrainView.Rebuild(region); resitHouses = true; }
            RefreshBridges();
            float hw = PathSystem.HalfWidth((int)kind);
            Forest.RemoveWhere(t => PathSystem.Distance(line, new Vector2(t.x, t.z)) < hw + TreeFactory.Radius((TreeKind)t.kind) * t.scale * 0.4f);
            Forest.MarkAllDirty();
            Cover.MarkAllDirty();
            People.Rehome();
            return null;
        }

        public bool RemovePathAt(Vector3 local)
        {
            int i = Paths.Pick(new Vector2(local.x, local.z));
            if (i < 0) return false;
            Checkpoint();
            Paths.Lines.RemoveAt(i);
            RefreshBridges();
            Cover.MarkAllDirty();
            return true;
        }

        /// <summary>Lifts a house off the map so it can be repositioned (checkpointed: undo puts it back).</summary>
        public HouseInstance PickUpHouse(int index)
        {
            Checkpoint();
            var h = Buildings.RemoveAt(index);
            ApplyPads();
            Cover.MarkDirtyRegion(new Vector2(h.x, h.z), BuildingSystem.Radius(h.variant));
            return h;
        }

        // ------------------------------------------------------------------ eraser

        /// <summary>Erases the most specific thing under the pointer: a house, the train, or the railway. Returns what went.</summary>
        public string EraseAt(Vector3 origin, Vector3 direction, Vector3 hit)
        {
            int house = Buildings.Pick(origin, direction);
            if (house >= 0) { Checkpoint(); var h = Buildings.RemoveAt(house); ApplyPads(); Cover.MarkDirtyRegion(new Vector2(h.x, h.z), 0.1f); return "House removed"; }
            if (RemoveStreamAt(hit)) return "Stream removed";
            int boat = Boats.Nearest(hit, 0.02f);
            if (boat >= 0) { Checkpoint(); var b = Boats.RemoveAt(boat); return BoatFactory.Names[b.kind] + " removed"; }
            int animal = Animals.Nearest(hit, 0.025f);
            if (animal >= 0) { Checkpoint(); var a = Animals.RemoveAt(animal); return (AnimalKind)a.kind + " removed"; }
            int person = People.Nearest(hit, 0.02f);
            if (person >= 0) { Checkpoint(); People.RemoveAt(person); return "Villager removed"; }
            var train = Trains.Near(hit, 0.05f);
            if (train != null) { Checkpoint(); Trains.Remove(train); return "Train removed"; }
            int signal = Track.PickSignal(hit, 0.025f);
            if (signal >= 0) { Checkpoint(); Track.Signals.RemoveAt(signal); Track.SnapSignals(); Track.RebuildFixtures(); return "Signal removed"; }
            if (Paths.Pick(new Vector2(hit.x, hit.z)) >= 0 && Track.DistanceTo(hit) > 0.03f && RemovePathAt(hit)) return "Path removed";
            if (RemovePieceAt(hit)) return "Track removed";
            return null;
        }

        // ------------------------------------------------------------------ save / load

        [Serializable]
        private sealed class SaveData
        {
            public int version = 6;
            public int gx, gz, nx, nz;       // v2: the grid in global cells
            public float size;               // v1: island size (its grid spanned exactly this); v2: starting patch size
            public int seed;
            public string height, land, locks, soil, water, waterLevel;
            public List<TreeInstance> trees;
            public List<HouseInstance> houses;
            public List<AnimalInstance> animals;
            public List<PersonInstance> people;
            public List<StreamPath> streams;
            public List<PathLine> paths;
            public List<BridgeChoice> bridges;
            public TrackNetwork.Data network;          // v3: the railway network
            public List<TrainSystem.TrainData> trains;
            public List<SignalInstance> signals;
            public int switchMode;
            public float hour = -1f;                   // v4: the village clock (older saves start at 8:00)
            public int day, clockMode;
            public List<SupplyInstance> supplies;      // v5: produce on the shelves, the farms and the platforms
            public List<CameraAim> cameras;            // v6: where each CCTV camera looks
            public List<BoatInstance> boats;           // v6+: boats on the ponds (older saves have none)
            public List<Vector3> track;                // v1 / v2: the single railway and its train
            public bool trackClosed, trainPlaced, trainRunning;
            public float trainS;
        }

        private static string Pack(float[] values)
        {
            var bytes = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return Convert.ToBase64String(bytes);
        }

        private static float[] Unpack(string data, int count)
        {
            var bytes = Convert.FromBase64String(data);
            if (bytes.Length != count * 4) throw new InvalidDataException("Saved terrain has the wrong size.");
            var values = new float[count];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values;
        }

        public string Serialize() => JsonUtility.ToJson(new SaveData
        {
            gx = Terrain.GX0, gz = Terrain.GZ0, nx = Terrain.Nx, nz = Terrain.Nz, size = Terrain.PatchSize, seed = Seed,
            height = Pack(Terrain.Height), land = Pack(Terrain.Land), locks = Pack(Terrain.Lock), soil = Pack(Terrain.Soil),
            water = Pack(Terrain.Water), waterLevel = Pack(Terrain.WaterLevel),
            trees = new List<TreeInstance>(Forest.Trees), houses = new List<HouseInstance>(Buildings.Houses), animals = new List<AnimalInstance>(Animals.Animals), people = new List<PersonInstance>(People.Export()), streams = CopyStreams(Streams.Streams), paths = PathSystem.Copy(Paths.Lines), bridges = new List<BridgeChoice>(Bridges.Choices),
            network = Track.Network.ToData(), trains = Trains.ToData(), signals = new List<SignalInstance>(Track.Signals), switchMode = (int)Trains.Mode,
            hour = Clock.Hour, day = Clock.Day, clockMode = (int)Clock.Mode,
            supplies = new List<SupplyInstance>(People.ExportSupplies()),
            cameras = new List<CameraAim>(Cctv.Export()),
            boats = new List<BoatInstance>(Boats.Export()),
        });

        public void Deserialize(string json, WorldMaterials materials)
        {
            var d = JsonUtility.FromJson<SaveData>(json);
            if (d == null || d.size <= 0f || (d.version < 1 || d.version > 6)) throw new InvalidDataException("Not a Wonderland save.");
            float[] height, land, locks, soil, water = null, waterLevel = null;
            RectInt cells;
            if (d.version >= 2)
            {
                cells = new RectInt(d.gx, d.gz, d.nx, d.nz);
                int count = d.nx * d.nz;
                if (d.nx < 8 || d.nz < 8 || d.nx > TerrainField.MaxCells * 2 || d.nz > TerrainField.MaxCells * 2) throw new InvalidDataException("Saved terrain grid is invalid.");
                height = Unpack(d.height, count); land = Unpack(d.land, count); locks = Unpack(d.locks, count); soil = Unpack(d.soil, count);
                if (!string.IsNullOrEmpty(d.water)) { water = Unpack(d.water, count); waterLevel = Unpack(d.waterLevel, count); }
            }
            else MigrateV1(d, out cells, out height, out land, out locks, out soil);
            // Saves before the railway network (v1 / v2) had one railway and at most one train: one piece of track.
            var network = d.network;
            var trains = d.trains;
            if ((network == null || network.edges == null || network.edges.Count == 0) && d.track != null && d.track.Count > 2)
            {
                network = new TrackNetwork.Data();
                network.edges.Add(new TrackNetwork.EdgeData { points = d.track, closed = d.trackClosed });
                trains = new List<TrainSystem.TrainData>();
                if (d.trainPlaced)
                {
                    var old = new TrackLayout(new List<Vector3>(d.track), d.trackClosed);
                    var front = old.Evaluate(d.trainS + TrainFactory.Loco.Front, out var tangent);
                    trains.Add(new TrainSystem.TrainData { x = front.x, z = front.z, fx = tangent.x, fz = tangent.z, running = d.trainRunning });
                }
            }
            var keepRoom = room; room = null;   // fit once, after the data is in
            Create(d.size, materials, d.seed, false);
            Restore(new Snapshot
            {
                Cells = cells, Height = height, Land = Quantise(land), Lock = Quantise(locks), Soil = Quantise(soil),
                Water = water != null ? Quantise(water) : null, WaterLevel = waterLevel, Trees = (d.trees ?? new List<TreeInstance>()).ToArray(),
                Houses = (d.houses ?? new List<HouseInstance>()).ToArray(), Animals = (d.animals ?? new List<AnimalInstance>()).ToArray(), People = (d.people ?? new List<PersonInstance>()).ToArray(), Streams = d.streams, Paths = d.paths, Bridges = d.bridges,
                Network = network, TrainList = trains, Signals = d.signals, Mode = (SwitchMode)Mathf.Clamp(d.switchMode, 0, 2),
                Boats = d.boats?.ToArray(),
            });
            Terrain.CopyFrom(height, land, locks, soil, water, waterLevel);   // exact values (snapshots are quantised)
            if (d.hour >= 0f) { Clock.Hour = Mathf.Repeat(d.hour, 24f); Clock.Day = Mathf.Max(0, d.day); Clock.Mode = (ClockMode)Mathf.Clamp(d.clockMode, 0, 2); }
            People.SetSupplies(d.supplies);
            Cctv.Import(d.cameras);
            TerrainView.RebuildAll();
            WaterView.RebuildAll();
            room = keepRoom;
            FitToRoom();
        }

        /// <summary>
        /// Version 1 saves (before land could grow) stored a 97 x 97 grid spanning exactly the island. Resample it into
        /// the larger grid and rebuild the island's land mask from its size and seed.
        /// </summary>
        private static void MigrateV1(SaveData d, out RectInt cells, out float[] height, out float[] land, out float[] locks, out float[] soil)
        {
            const int halfCells = TerrainField.DefaultHalfCells, n = halfCells * 2 + 1;
            cells = new RectInt(-halfCells, -halfCells, n, n);
            const int oldN = 97;
            var oldHeight = Unpack(d.height, oldN * oldN); var oldLocks = Unpack(d.locks, oldN * oldN); var oldSoil = Unpack(d.soil, oldN * oldN);
            int count = n * n;
            height = new float[count]; land = new float[count]; locks = new float[count]; soil = new float[count];
            float oldHalf = d.size * 0.5f, oldCell = d.size / (oldN - 1);
            float half = halfCells * TerrainField.CellSize;
            float Sample(float[] g, float x, float z)
            {
                float gx = Mathf.Clamp((x + oldHalf) / oldCell, 0f, oldN - 1.001f), gz = Mathf.Clamp((z + oldHalf) / oldCell, 0f, oldN - 1.001f);
                int ix = (int)gx, iz = (int)gz; float fx = gx - ix, fz = gz - iz; int i = iz * oldN + ix;
                return Mathf.Lerp(Mathf.Lerp(g[i], g[i + 1], fx), Mathf.Lerp(g[i + oldN], g[i + oldN + 1], fx), fz);
            }
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                float px = -half + x * TerrainField.CellSize, pz = -half + z * TerrainField.CellSize;
                bool inside = Mathf.Abs(px) < oldHalf && Mathf.Abs(pz) < oldHalf;
                height[i] = inside ? Sample(oldHeight, px, pz) : TerrainField.BaseHeight;
                locks[i] = inside ? Sample(oldLocks, px, pz) : 0f;
                soil[i] = inside ? Sample(oldSoil, px, pz) : 0f;
                land[i] = TerrainField.OrganicPatch(d.size, d.seed, px, pz);
            }
        }

        public void Save(string path = null)
        {
            path ??= SavePath;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize());
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static bool HasSave(string path = null) => File.Exists(path ?? SavePath);

        // ------------------------------------------------------------------ named worlds

        public const int Slots = 6;
        private const string SlotKey = "wonderland.slot";
        /// <summary>Where the named worlds are kept (tests point it elsewhere).</summary>
        public static string SlotFolder;

        /// <summary>World <paramref name="n"/>'s file (1-6).</summary>
        public static string SlotPath(int n) => Path.Combine(SlotFolder ?? Application.persistentDataPath, $"world-{n}.json");

        /// <summary>The named world being played (1-6), or 0 for a world not saved under a name yet. Remembered between sessions.</summary>
        public static int CurrentSlot
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SlotKey, 0), 0, Slots);
            set => PlayerPrefs.SetInt(SlotKey, Mathf.Clamp(value, 0, Slots));
        }

        public static bool SlotUsed(int n) => File.Exists(SlotPath(n));

        /// <summary>"World 2 · 14 Nov 16:32" (when it was last saved), or "World 3 · empty".</summary>
        public static string SlotLabel(int n) => SlotUsed(n) ? $"World {n} · {File.GetLastWriteTime(SlotPath(n)):d MMM HH:mm}" : $"World {n} · empty";

        /// <summary>Saves this world as World <paramref name="n"/> (and plays on in it).</summary>
        public void SaveSlot(int n) { Save(SlotPath(n)); CurrentSlot = n; }

        /// <summary>Loads World <paramref name="n"/> in place of this one.</summary>
        public void LoadSlot(int n, WorldMaterials materials) { Load(materials, SlotPath(n)); CurrentSlot = n; }

        public void Load(WorldMaterials materials, string path = null) => Deserialize(File.ReadAllText(path ?? SavePath), materials);
    }
}
