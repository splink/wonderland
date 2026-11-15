using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Wonderland
{
    [Serializable]
    public sealed class WorldMaterials
    {
        public Material Lit, Terrain, UI, Smoke, Water;
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
        public TrainSystem Train { get; private set; }
        public GroundCover Cover { get; private set; }
        public WaterRenderer WaterView { get; private set; }
        public AnimalSystem Animals { get; private set; }
        public PeopleSystem People { get; private set; }
        public StreamSystem Streams { get; private set; }
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

        public static string SavePath => Path.Combine(Application.persistentDataPath, "wonderland-world.json");

        public void Create(float size, WorldMaterials materials, int seed = 7, bool hills = true)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying) Destroy(transform.GetChild(i).gameObject); else DestroyImmediate(transform.GetChild(i).gameObject);
            }
            Materials = materials; Seed = seed;
            platforms = null;
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
            Train = Child<TrainSystem>("Train");
            Train.Init(materials.Lit, materials.Smoke);
            Train.InTunnel = arc =>
            {
                var t = Track.Tunnel; var l = Track.Layout;
                if (t == null || l == null) return false;
                float s = l.Wrap(arc);
                int lo = 0, hi = l.Points.Count - 1;
                while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (l.Cum[mid] <= s) lo = mid; else hi = mid; }
                return t[lo];
            };
            Cover = Child<GroundCover>("Ground cover");
            Cover.Init(Terrain, materials.Lit, seed, p => Buildings.Contains(p, 0.01f) || IsWet(p));
            WaterView = Child<WaterRenderer>("Water");
            WaterView.Init(Terrain, materials.Water);
            Animals = Child<AnimalSystem>("Animals");
            Animals.Init(Terrain, materials.Lit, IsFreeGrass);
            People = Child<PeopleSystem>("Villagers");
            People.Init(this, materials.Lit);
            Streams = Child<StreamSystem>("Streams");
            Streams.Init(materials.Water);
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
                if (Track.Layout != null)
                    foreach (var p in Track.Layout.Points) need = Union(need, new Rect(p.x - 0.15f, p.z - 0.15f, 0.3f, 0.3f));
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

        private bool trackDirty, streamsDirty;
        private float trackUpdated, streamsUpdated;

        private void LateUpdate()
        {
            if (resitHouses) { Buildings.Resit(); resitHouses = false; }
            // Sculpting under the railway: re-profile it a few times per second (and once more after the stroke).
            if (streamsDirty && Time.realtimeSinceStartup - streamsUpdated > 0.3f) UpdateStreamsFromTerrain();
            if (trackDirty && Time.realtimeSinceStartup - trackUpdated > 0.25f) UpdateTrackFromTerrain();
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
        /// Re-fits the railway's height profile to the (sculpted) ground along the same route, regrades its bed and keeps
        /// the train running. The track rises and falls with the land, within its gentle grade limit.
        /// </summary>
        public void UpdateTrackFromTerrain()
        {
            trackDirty = false;
            trackUpdated = Time.realtimeSinceStartup;
            var old = Track.Layout;
            if (old == null) return;
            var xz = new List<Vector2>(old.Points.Count);
            foreach (var p in old.Points) xz.Add(new Vector2(p.x, p.z));
            var heights = TrackPlanner.Profile(xz, Terrain, old.Closed, WaterLevelFor);
            var points = new List<Vector3>(xz.Count);
            for (int i = 0; i < xz.Count; i++) points.Add(new Vector3(xz[i].x, heights[i], xz[i].y));
            var layout = new TrackLayout(points, old.Closed);
            var tunnel = TrackSystem.TunnelMask(Terrain, layout);
            Track.SetLayout(layout, WaterLevelFor, Terrain, tunnel);
            var region = TrackSystem.GradeTerrain(Terrain, layout, WaterLevelFor, tunnel);
            TerrainView.Rebuild(region);
            Train.SwapLayout(layout);
            RebuildStations();
            Cover.MarkAllDirty();
        }

        /// <summary>
        /// Grades a flat pad under a house (cut and fill to its average footprint height, soft 7 cm margin) and protects
        /// it from brushes, so a house never ends up half buried on a slope.
        /// </summary>
        private RectInt PadHouse(HouseInstance h)
        {
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
        public HouseInstance? StationAt(Vector3 local, int style)
        {
            if (!Track.HasTrack) return null;
            float s = Track.Layout.Nearest(local, out float d);
            if (d > 0.35f) return null;
            var p = Track.Layout.Evaluate(s, out var tangent);
            tangent.y = 0f; tangent.Normalize();
            var normal = Vector3.Cross(Vector3.up, tangent);
            float side = Vector3.Dot(normal, local - p) >= 0f ? 1f : -1f;
            var centre = p + normal * side * StationSetback;
            var facing = normal * side;   // the building's back points away from the track; its front (-Z) faces it
            return new HouseInstance { x = centre.x, z = centre.z, rotation = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg, variant = HouseFactory.Station, style = style };
        }

        private string ValidateStation(HouseInstance h)
        {
            if (!Track.HasTrack) return "Draw a railway first";
            Track.Layout.Nearest(new Vector3(h.x, 0f, h.z), out float d);
            if (Mathf.Abs(d - StationSetback) > 0.03f) return "A station goes right beside the railway";
            if (Track.Layout.Length < PlatformLength + 0.3f) return "The railway is too short for a station";
            return null;
        }

        /// <summary>Generates every station's platform along the rails and tells the train where to stop.</summary>
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
            Train.Stops.Clear();
            if (!Track.HasTrack) return;
            var layout = Track.Layout;
            var b = new MeshBuilder();
            foreach (var h in Buildings.Houses)
            {
                if (h.variant != HouseFactory.Station) continue;
                var c = new Vector3(h.x, 0f, h.z);
                float s = layout.Nearest(c, out _);
                var p = layout.Evaluate(s, out var tangent); tangent.y = 0f; tangent.Normalize();
                int side = Vector3.Dot(Vector3.Cross(Vector3.up, tangent), c - p) >= 0f ? 1 : -1;
                float s0 = s - PlatformLength / 2f, s1 = s + PlatformLength / 2f;
                if (!layout.Closed) { s0 = Mathf.Max(0.02f, s0); s1 = Mathf.Min(layout.Length - 0.02f, s1); }
                HouseFactory.Platform(b, layout, s0, s1, side);
                Train.Stops.Add(s);
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
            public List<Vector3> Track;
            public bool TrackClosed, TrainPlaced, TrainRunning;
            public float TrainS;
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
            Trees = Forest.Trees.ToArray(), Houses = Buildings.Houses.ToArray(), Animals = Animals.Animals.ToArray(), People = People.People.ToArray(),
            Streams = CopyStreams(Streams.Streams),
            Track = Track.Layout != null ? new List<Vector3>(Track.Layout.Points) : null, TrackClosed = Track.Layout?.Closed ?? false,
            TrainPlaced = Train.Placed, TrainRunning = Train.Running, TrainS = Train.S,
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
            var restored = s.Track != null ? new TrackLayout(new List<Vector3>(s.Track), s.TrackClosed) : null;
            Track.SetLayout(restored, WaterLevelFor, Terrain, restored != null ? TrackSystem.TunnelMask(Terrain, restored) : null);
            if (s.TrainPlaced && Track.Layout != null && Train.Place(Track.Layout, s.TrainS)) Train.Running = s.TrainRunning;
            else Train.Remove();
            RebuildStations();
            Cover.MarkAllDirty();
            if (room != null) FitToRoom();
        }

        /// <summary>Remember the world before an operation, so Undo can return to it.</summary>
        public void Checkpoint()
        {
            undo.Add(Capture());
            long perSnapshot = (long)Terrain.Nx * Terrain.Nz * 12;
            int limit = (int)Mathf.Clamp(UndoBudgetBytes / Mathf.Max(1L, perSnapshot), 4, MaxUndo);
            while (undo.Count > limit) undo.RemoveAt(0);
        }

        public bool Undo()
        {
            if (undo.Count == 0) return false;
            var s = undo[^1];
            undo.RemoveAt(undo.Count - 1);
            Restore(s);
            return true;
        }

        // ------------------------------------------------------------------ terrain

        public void TerrainStroke(TerrainBrush brush, Vector3 local, float radius, float strength, float dt, float flattenTarget)
        {
            var centre = new Vector2(local.x, local.z);
            if (brush == TerrainBrush.Grow)
                EnsureGridCovers(new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f));
            var region = Terrain.ApplyBrush(brush, centre, radius, strength, dt, flattenTarget);
            if (region.width == 0) return;
            TerrainView.Rebuild(region);
            if (Track.Layout != null && brush != TerrainBrush.Grow && brush != TerrainBrush.Trim && TouchesTrack(centre, radius)) trackDirty = true;
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
            if (Track.DistanceTo(new Vector3(p.x, 0f, p.y)) < TrackSystem.BedHalfWidth + radius) return false;
            return !Buildings.Contains(p, radius);
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
            if (!IsFreeGrass(p, AnimalFactory.Radius(kind))) return "Animals need open grass";
            Animals.Add(new AnimalInstance { x = p.x, z = p.y, kind = (int)kind, seed = seed });
            return null;
        }

        /// <summary>Animals whose home is no longer grass (new railway, a pond) wander off the map; villagers move house.</summary>
        private void RemoveStrandedAnimals()
        {
            for (int i = Animals.Animals.Count - 1; i >= 0; i--)
            {
                var a = Animals.Animals[i];
                if (!IsFreeGrass(new Vector2(a.x, a.z), AnimalFactory.Radius((AnimalKind)a.kind) * 0.5f)) Animals.RemoveAt(i);
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
            WaterView.Rebuild(new RectInt(region.x - 2, region.y - 2, region.width + 4, region.height + 4));
            Forest.RemoveWhere(t => (new Vector2(t.x, t.z) - centre).sqrMagnitude < radius * radius * 1.2f && Terrain.WaterAt(t.x, t.z) > 0.2f);
            if (!drain) RemoveStrandedAnimals();
            Forest.MarkDirtyRegion(centre, radius);
            Cover.MarkDirtyRegion(centre, radius);
        }

        public bool CanGrowTree(Vector2 p, float footprint)
        {
            if (Terrain.WaterAt(p.x, p.y) > 0.1f || Streams.IsWet(p, footprint * 0.5f + 0.008f)) return false;
            if (Terrain.LockAt(p.x, p.y) > 0.25f) return false;
            if (Track.DistanceTo(new Vector3(p.x, 0f, p.y)) < TrackSystem.BedHalfWidth + footprint * 0.5f + 0.008f) return false;
            return !Buildings.Contains(p, footprint * 0.6f);
        }

        public int PaintForest(Vector3 local, float radius, float density, float dt) =>
            Forest.Paint(new Vector2(local.x, local.z), radius, density, dt, CanGrowTree);

        public int EraseForest(Vector3 local, float radius, float strength, float dt) =>
            Forest.Erase(new Vector2(local.x, local.z), radius, strength, dt);

        // ------------------------------------------------------------------ railway

        /// <summary>Plans a railway from a drawn stroke and, when valid, replaces the existing one (checkpointed).</summary>
        public TrackPlan BuildTrack(IReadOnlyList<Vector3> drawn)
        {
            // Railway drawn over bare floor grows its own land: checkpoint first so undo also takes the new land back.
            Checkpoint();
            var plan = TrackPlanner.Plan(drawn, Terrain, ensureLand: path =>
            {
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var p in path) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
                EnsureGridCovers(Rect.MinMaxRect(minX - 0.25f, minZ - 0.25f, maxX + 0.25f, maxZ + 0.25f));
                var region = Terrain.GrowLandAlong(path, 0.22f);
                TerrainView.Rebuild(region);
                Cover.MarkAllDirty();
                return true;
            }, waterLevel: WaterLevelFor);
            if (!plan.Ok) { Undo(); return plan; }
            bool trainWasRunning = Train.Running;
            TrackSystem.UnlockTerrain(Terrain);
            var tunnel = TrackSystem.TunnelMask(Terrain, plan.Layout);
            Track.SetLayout(plan.Layout, WaterLevelFor, Terrain, tunnel);
            TrackSystem.GradeTerrain(Terrain, plan.Layout, WaterLevelFor, tunnel);
            Terrain.UpdateSurface(Terrain.All);
            TerrainView.RebuildAll();
            var layout = plan.Layout;
            Forest.RemoveWhere(t => { layout.Nearest(new Vector3(t.x, 0f, t.z), out float d); return d < TrackSystem.BedHalfWidth + TreeFactory.Radius((TreeKind)t.kind) * t.scale * 0.5f + 0.006f; });
            Forest.MarkAllDirty();
            for (int i = Buildings.Houses.Count - 1; i >= 0; i--)
            {
                var h = Buildings.Houses[i];
                bool gone = h.variant == HouseFactory.Station ? ValidateStation(h) != null
                    : Buildings.Validate(h, i, p => Track.DistanceTo(new Vector3(p.x, 0f, p.y))) == "That's the railway";
                if (gone) Buildings.RemoveAt(i);
            }
            ApplyPads();
            RemoveStrandedAnimals();
            Buildings.Resit();
            Cover.MarkAllDirty();
            if (Train.Placed)
            {
                if (Train.Place(layout, 0f)) Train.Running = trainWasRunning; else Train.Remove();
            }
            return plan;
        }

        public void RemoveTrack()
        {
            Checkpoint();
            Train.Remove();
            Track.SetLayout(null);
            TrackSystem.UnlockTerrain(Terrain);
            TerrainView.RebuildAll();
            RebuildStations();
            Cover.MarkAllDirty();
        }

        // ------------------------------------------------------------------ train

        /// <summary>Puts the train on the track at the point nearest <paramref name="local"/>; returns an error or null.</summary>
        public string PlaceTrain(Vector3 local)
        {
            if (!Track.HasTrack) return "Draw a railway first";
            float s = Track.Layout.Nearest(local, out float d);
            if (d > 0.08f) return "Point at the railway to place the train";
            Checkpoint();
            if (!Train.Place(Track.Layout, s)) { Undo(); return "This railway is too short for the train"; }
            return null;
        }

        public void SetTrainRunning(bool running) { if (Train.Placed) Train.Running = running; }

        // ------------------------------------------------------------------ houses

        public string ValidateHouse(HouseInstance h, int ignore = -1)
        {
            string error = (h.variant == HouseFactory.Station ? ValidateStation(h) : null) ?? Buildings.Validate(h, ignore, p => Track.DistanceTo(new Vector3(p.x, 0f, p.y)));
            if (error == null)
                foreach (var p in BuildingSystem.FootprintSamples(h))
                    if (Streams.IsWet(p, 0.01f)) return "That's a stream";
            return error;
        }

        private bool TouchesTrack(Vector2 centre, float radius)
        {
            float reach = radius + TrackSystem.CorridorHalfWidth;
            foreach (var p in Track.Layout.Points)
                if ((new Vector2(p.x, p.z) - centre).sqrMagnitude < reach * reach) return true;
            return false;
        }

        public string PlaceHouse(HouseInstance h, bool checkpoint = true)
        {
            string error = ValidateHouse(h);
            if (error != null) return error;
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
            return null;
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
            int animal = Animals.Nearest(hit, 0.025f);
            if (animal >= 0) { Checkpoint(); var a = Animals.RemoveAt(animal); return (AnimalKind)a.kind + " removed"; }
            int person = People.Nearest(hit, 0.02f);
            if (person >= 0) { Checkpoint(); People.RemoveAt(person); return "Villager removed"; }
            if (Train.Near(hit, 0.05f)) { Checkpoint(); Train.Remove(); return "Train removed"; }
            if (Track.DistanceTo(hit) < 0.045f) { RemoveTrack(); return "Railway removed"; }
            return null;
        }

        // ------------------------------------------------------------------ save / load

        [Serializable]
        private sealed class SaveData
        {
            public int version = 2;
            public int gx, gz, nx, nz;       // v2: the grid in global cells
            public float size;               // v1: island size (its grid spanned exactly this); v2: starting patch size
            public int seed;
            public string height, land, locks, soil, water, waterLevel;
            public List<TreeInstance> trees;
            public List<HouseInstance> houses;
            public List<AnimalInstance> animals;
            public List<PersonInstance> people;
            public List<StreamPath> streams;
            public List<Vector3> track;
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
            trees = new List<TreeInstance>(Forest.Trees), houses = new List<HouseInstance>(Buildings.Houses), animals = new List<AnimalInstance>(Animals.Animals), people = new List<PersonInstance>(People.People), streams = CopyStreams(Streams.Streams),
            track = Track.Layout != null ? new List<Vector3>(Track.Layout.Points) : new List<Vector3>(),
            trackClosed = Track.Layout?.Closed ?? false,
            trainPlaced = Train.Placed, trainRunning = Train.Running, trainS = Train.S,
        });

        public void Deserialize(string json, WorldMaterials materials)
        {
            var d = JsonUtility.FromJson<SaveData>(json);
            if (d == null || d.size <= 0f || (d.version != 1 && d.version != 2)) throw new InvalidDataException("Not a Wonderland save.");
            float[] height, land, locks, soil, water = null, waterLevel = null;
            RectInt cells;
            if (d.version == 2)
            {
                cells = new RectInt(d.gx, d.gz, d.nx, d.nz);
                int count = d.nx * d.nz;
                if (d.nx < 8 || d.nz < 8 || d.nx > TerrainField.MaxCells * 2 || d.nz > TerrainField.MaxCells * 2) throw new InvalidDataException("Saved terrain grid is invalid.");
                height = Unpack(d.height, count); land = Unpack(d.land, count); locks = Unpack(d.locks, count); soil = Unpack(d.soil, count);
                if (!string.IsNullOrEmpty(d.water)) { water = Unpack(d.water, count); waterLevel = Unpack(d.waterLevel, count); }
            }
            else MigrateV1(d, out cells, out height, out land, out locks, out soil);
            var keepRoom = room; room = null;   // fit once, after the data is in
            Create(d.size, materials, d.seed, false);
            Restore(new Snapshot
            {
                Cells = cells, Height = height, Land = Quantise(land), Lock = Quantise(locks), Soil = Quantise(soil),
                Water = water != null ? Quantise(water) : null, WaterLevel = waterLevel, Trees = (d.trees ?? new List<TreeInstance>()).ToArray(),
                Houses = (d.houses ?? new List<HouseInstance>()).ToArray(), Animals = (d.animals ?? new List<AnimalInstance>()).ToArray(), People = (d.people ?? new List<PersonInstance>()).ToArray(), Streams = d.streams, Track = d.track != null && d.track.Count > 2 ? d.track : null,
                TrackClosed = d.trackClosed, TrainPlaced = d.trainPlaced, TrainRunning = d.trainRunning, TrainS = d.trainS,
            });
            Terrain.CopyFrom(height, land, locks, soil, water, waterLevel);   // exact values (snapshots are quantised)
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

        public void Load(WorldMaterials materials, string path = null) => Deserialize(File.ReadAllText(path ?? SavePath), materials);
    }
}
