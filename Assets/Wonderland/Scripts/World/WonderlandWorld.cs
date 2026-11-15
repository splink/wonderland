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
        public const int MaxUndo = 30;
        public TerrainField Terrain { get; private set; }
        public TerrainRenderer TerrainView { get; private set; }
        public ForestSystem Forest { get; private set; }
        public TrackSystem Track { get; private set; }
        public BuildingSystem Buildings { get; private set; }
        public TrainSystem Train { get; private set; }
        public GroundCover Cover { get; private set; }
        public WorldMaterials Materials { get; private set; }
        public float Size => Terrain.Size;
        public int Seed { get; private set; }
        public int UndoDepth => undo.Count;

        private readonly List<Snapshot> undo = new();
        private bool resitHouses;

        public static string SavePath => Path.Combine(Application.persistentDataPath, "wonderland-world.json");

        public void Create(float size, WorldMaterials materials, int seed = 7, bool hills = true)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying) Destroy(transform.GetChild(i).gameObject); else DestroyImmediate(transform.GetChild(i).gameObject);
            }
            Materials = materials; Seed = seed;
            Terrain = new TerrainField(size, seed, hills);
            TerrainView = Child<TerrainRenderer>("Terrain", true);
            TerrainView.Init(Terrain, materials.Terrain);
            Forest = Child<ForestSystem>("Forest");
            Forest.Init(Terrain, materials.Lit, seed);
            Track = Child<TrackSystem>("Railway");
            Track.Init(materials.Lit, materials.UI);
            Buildings = Child<BuildingSystem>("Buildings");
            Buildings.Init(Terrain, materials.Lit);
            Train = Child<TrainSystem>("Train");
            Train.Init(materials.Lit, materials.Smoke);
            Cover = Child<GroundCover>("Ground cover");
            Cover.Init(Terrain, materials.Lit, seed, p => Buildings.Contains(p, 0.01f));
            undo.Clear();
        }

        private T Child<T>(string name, bool mesh = false) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            if (mesh) { go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>(); }
            return go.AddComponent<T>();
        }

        private void LateUpdate()
        {
            if (resitHouses) { Buildings.Resit(); resitHouses = false; }
        }

        // ------------------------------------------------------------------ undo

        private sealed class Snapshot
        {
            public float[] Height, Lock, Soil;
            public TreeInstance[] Trees;
            public HouseInstance[] Houses;
            public List<Vector3> Track;
            public bool TrackClosed, TrainPlaced, TrainRunning;
            public float TrainS;
        }

        private Snapshot Capture() => new()
        {
            Height = (float[])Terrain.Height.Clone(), Lock = (float[])Terrain.Lock.Clone(), Soil = (float[])Terrain.Soil.Clone(),
            Trees = Forest.Trees.ToArray(), Houses = Buildings.Houses.ToArray(),
            Track = Track.Layout != null ? new List<Vector3>(Track.Layout.Points) : null, TrackClosed = Track.Layout?.Closed ?? false,
            TrainPlaced = Train.Placed, TrainRunning = Train.Running, TrainS = Train.S,
        };

        private void Restore(Snapshot s)
        {
            Terrain.CopyFrom(s.Height, s.Lock, s.Soil);
            TerrainView.RebuildAll();
            Forest.SetTrees(s.Trees);
            Buildings.SetHouses(s.Houses);
            Track.SetLayout(s.Track != null ? new TrackLayout(new List<Vector3>(s.Track), s.TrackClosed) : null);
            if (s.TrainPlaced && Track.Layout != null && Train.Place(Track.Layout, s.TrainS)) Train.Running = s.TrainRunning;
            else Train.Remove();
            Cover.MarkAllDirty();
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
            var region = Terrain.ApplyBrush(brush, new Vector2(local.x, local.z), radius, strength, dt, flattenTarget);
            TerrainView.Rebuild(region);
            Forest.MarkDirtyRegion(new Vector2(local.x, local.z), radius);
            Cover.MarkDirtyRegion(new Vector2(local.x, local.z), radius);
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
            Terrain.UpdateSurface(new RectInt(0, 0, TerrainField.N, TerrainField.N));
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
            public int version = 1;
            public float size;
            public int seed;
            public string height, locks, soil;
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
            size = Terrain.Size, seed = Seed,
            height = Pack(Terrain.Height), locks = Pack(Terrain.Lock), soil = Pack(Terrain.Soil),
            trees = new List<TreeInstance>(Forest.Trees), houses = new List<HouseInstance>(Buildings.Houses),
            track = Track.Layout != null ? new List<Vector3>(Track.Layout.Points) : new List<Vector3>(),
            trackClosed = Track.Layout?.Closed ?? false,
            trainPlaced = Train.Placed, trainRunning = Train.Running, trainS = Train.S,
        });

        public void Deserialize(string json, WorldMaterials materials)
        {
            var d = JsonUtility.FromJson<SaveData>(json);
            if (d == null || d.version != 1 || d.size <= 0f) throw new InvalidDataException("Not a Wonderland save.");
            int count = TerrainField.N * TerrainField.N;
            var height = Unpack(d.height, count); var locks = Unpack(d.locks, count); var soil = Unpack(d.soil, count);
            Create(d.size, materials, d.seed, false);
            Restore(new Snapshot
            {
                Height = height, Lock = locks, Soil = soil, Trees = (d.trees ?? new List<TreeInstance>()).ToArray(),
                Houses = (d.houses ?? new List<HouseInstance>()).ToArray(), Track = d.track != null && d.track.Count > 2 ? d.track : null,
                TrackClosed = d.trackClosed, TrainPlaced = d.trainPlaced, TrainRunning = d.trainRunning, TrainS = d.trainS,
            });
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
