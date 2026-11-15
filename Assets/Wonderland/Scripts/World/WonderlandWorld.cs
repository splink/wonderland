using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Wonderland
{
    [Serializable]
    public sealed class WorldMaterials
    {
        public Material Lit, Terrain, UI, Smoke;
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
        public TerrainField Terrain { get; private set; }
        public TerrainRenderer TerrainView { get; private set; }
        public ForestSystem Forest { get; private set; }
        public TrackSystem Track { get; private set; }
        public BuildingSystem Buildings { get; private set; }
        public TrainSystem Train { get; private set; }
        public GroundCover Cover { get; private set; }
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
            Cover = Child<GroundCover>("Ground cover");
            Cover.Init(Terrain, materials.Lit, seed, p => Buildings.Contains(p, 0.01f));
            undo.Clear();
            FitToRoom();
        }

        /// <summary>
        /// Cuts the terrain grid to cover the room (and all existing land and railway) as seen from the world root, and
        /// marks where land may grow. Called when the room becomes known, after loading and after the world moves.
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
                    Cover.Scatter();
                }
            }
            var free = roomLocal;
            Terrain.SetAllowed(free != null ? free.IsFree : null);
            TerrainView.RebuildAll();
            Forest.MarkAllDirty();
        }

        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        private static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        /// <summary>Whether land may grow at a local point (inside the room, off the walls and furniture).</summary>
        public bool CanGrowLand(Vector3 local) => Terrain.AllowedAt(local.x, local.z);

        private T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        private void LateUpdate()
        {
            if (resitHouses) { Buildings.Resit(); resitHouses = false; }
        }

        // ------------------------------------------------------------------ undo

        private sealed class Snapshot
        {
            public RectInt Cells;
            public float[] Height;
            public byte[] Land, Lock, Soil;   // 0..1 masks, quantised: snapshots of a room-sized grid stay small
            public TreeInstance[] Trees;
            public HouseInstance[] Houses;
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
            Trees = Forest.Trees.ToArray(), Houses = Buildings.Houses.ToArray(),
            Track = Track.Layout != null ? new List<Vector3>(Track.Layout.Points) : null, TrackClosed = Track.Layout?.Closed ?? false,
            TrainPlaced = Train.Placed, TrainRunning = Train.Running, TrainS = Train.S,
        };

        private void Restore(Snapshot s)
        {
            if (s.Cells != Terrain.Cells) { Terrain.Resize(s.Cells); TerrainView.Reinit(); Cover.Scatter(); }
            Terrain.CopyFrom(s.Height, Expand(s.Land), Expand(s.Lock), Expand(s.Soil));
            TerrainView.RebuildAll();
            Forest.SetTrees(s.Trees);
            Buildings.SetHouses(s.Houses);
            Track.SetLayout(s.Track != null ? new TrackLayout(new List<Vector3>(s.Track), s.TrackClosed) : null);
            if (s.TrainPlaced && Track.Layout != null && Train.Place(Track.Layout, s.TrainS)) Train.Running = s.TrainRunning;
            else Train.Remove();
            Cover.MarkAllDirty();
            if (room != null) FitToRoom();
        }

        /// <summary>Remember the world before an operation, so Undo can return to it.</summary>
        public void Checkpoint()
        {
            undo.Add(Capture());
            if (undo.Count > MaxUndo) undo.RemoveAt(0);
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
            var region = Terrain.ApplyBrush(brush, centre, radius, strength, dt, flattenTarget);
            if (region.width == 0) return;
            TerrainView.Rebuild(region);
            // Land edits move the shore (and its taper) up to a rim width beyond the brush.
            float reach = brush == TerrainBrush.Grow || brush == TerrainBrush.Trim ? radius + TerrainField.RimWidth : radius;
            Forest.MarkDirtyRegion(centre, reach);
            Cover.MarkDirtyRegion(centre, reach);
            if (brush == TerrainBrush.Trim)
                Forest.RemoveWhere(t => (new Vector2(t.x, t.z) - centre).sqrMagnitude < reach * reach && Terrain.EdgeAt(t.x, t.z) < 0.3f);
            resitHouses = true;
        }

        // ------------------------------------------------------------------ forest

        public bool CanGrowTree(Vector2 p, float footprint)
        {
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
            var plan = TrackPlanner.Plan(drawn, Terrain);
            if (!plan.Ok) return plan;
            Checkpoint();
            bool trainWasRunning = Train.Running;
            TrackSystem.UnlockTerrain(Terrain);
            Track.SetLayout(plan.Layout);
            TrackSystem.GradeTerrain(Terrain, plan.Layout);
            Terrain.UpdateSurface(Terrain.All);
            TerrainView.RebuildAll();
            var layout = plan.Layout;
            Forest.RemoveWhere(t => { layout.Nearest(new Vector3(t.x, 0f, t.z), out float d); return d < TrackSystem.BedHalfWidth + TreeFactory.Radius((TreeKind)t.kind) * t.scale * 0.5f + 0.006f; });
            Forest.MarkAllDirty();
            for (int i = Buildings.Houses.Count - 1; i >= 0; i--)
                if (Buildings.Validate(Buildings.Houses[i], i, p => Track.DistanceTo(new Vector3(p.x, 0f, p.y))) == "That's the railway") Buildings.RemoveAt(i);
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

        public string ValidateHouse(HouseInstance h, int ignore = -1) =>
            Buildings.Validate(h, ignore, p => Track.DistanceTo(new Vector3(p.x, 0f, p.y)));

        public string PlaceHouse(HouseInstance h, bool checkpoint = true)
        {
            string error = ValidateHouse(h);
            if (error != null) return error;
            if (checkpoint) Checkpoint();
            float r = BuildingSystem.Radius(h.variant) * 0.85f;
            Forest.RemoveWhere(t => new Vector2(t.x - h.x, t.z - h.z).magnitude < r + TreeFactory.Radius((TreeKind)t.kind) * t.scale * 0.4f);
            Buildings.Add(h);
            Cover.MarkDirtyRegion(new Vector2(h.x, h.z), r);
            return null;
        }

        /// <summary>Lifts a house off the map so it can be repositioned (checkpointed: undo puts it back).</summary>
        public HouseInstance PickUpHouse(int index)
        {
            Checkpoint();
            var h = Buildings.RemoveAt(index);
            Cover.MarkDirtyRegion(new Vector2(h.x, h.z), BuildingSystem.Radius(h.variant));
            return h;
        }

        // ------------------------------------------------------------------ eraser

        /// <summary>Erases the most specific thing under the pointer: a house, the train, or the railway. Returns what went.</summary>
        public string EraseAt(Vector3 origin, Vector3 direction, Vector3 hit)
        {
            int house = Buildings.Pick(origin, direction);
            if (house >= 0) { Checkpoint(); var h = Buildings.RemoveAt(house); Cover.MarkDirtyRegion(new Vector2(h.x, h.z), 0.1f); return "House removed"; }
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
            public string height, land, locks, soil;
            public List<TreeInstance> trees;
            public List<HouseInstance> houses;
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
            trees = new List<TreeInstance>(Forest.Trees), houses = new List<HouseInstance>(Buildings.Houses),
            track = Track.Layout != null ? new List<Vector3>(Track.Layout.Points) : new List<Vector3>(),
            trackClosed = Track.Layout?.Closed ?? false,
            trainPlaced = Train.Placed, trainRunning = Train.Running, trainS = Train.S,
        });

        public void Deserialize(string json, WorldMaterials materials)
        {
            var d = JsonUtility.FromJson<SaveData>(json);
            if (d == null || d.size <= 0f || (d.version != 1 && d.version != 2)) throw new InvalidDataException("Not a Wonderland save.");
            float[] height, land, locks, soil;
            RectInt cells;
            if (d.version == 2)
            {
                cells = new RectInt(d.gx, d.gz, d.nx, d.nz);
                int count = d.nx * d.nz;
                if (d.nx < 8 || d.nz < 8 || d.nx > TerrainField.MaxCells * 2 || d.nz > TerrainField.MaxCells * 2) throw new InvalidDataException("Saved terrain grid is invalid.");
                height = Unpack(d.height, count); land = Unpack(d.land, count); locks = Unpack(d.locks, count); soil = Unpack(d.soil, count);
            }
            else MigrateV1(d, out cells, out height, out land, out locks, out soil);
            var keepRoom = room; room = null;   // fit once, after the data is in
            Create(d.size, materials, d.seed, false);
            Restore(new Snapshot
            {
                Cells = cells, Height = height, Land = Quantise(land), Lock = Quantise(locks), Soil = Quantise(soil), Trees = (d.trees ?? new List<TreeInstance>()).ToArray(),
                Houses = (d.houses ?? new List<HouseInstance>()).ToArray(), Track = d.track != null && d.track.Count > 2 ? d.track : null,
                TrackClosed = d.trackClosed, TrainPlaced = d.trainPlaced, TrainRunning = d.trainRunning, TrainS = d.trainS,
            });
            Terrain.CopyFrom(height, land, locks, soil);   // exact values (snapshots are quantised)
            TerrainView.RebuildAll();
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
