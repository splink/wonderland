using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Undo and redo, one action at a time (the last <see cref="MaxUndo"/>). A step remembers the world as it was before
    /// the action - but never the whole world: the ground is kept chunk by chunk (1.2 m squares) and a chunk the action
    /// didn't touch is shared with the step before, so a step costs only what the action changed. Undoing puts back only
    /// what differs, and the life going on - trains running, people walking - carries on, unless the action itself put
    /// something there or took it away.
    /// </summary>
    public sealed partial class WonderlandWorld
    {
        public const int MaxUndo = 10;

        private readonly List<Snapshot> undo = new();
        private readonly List<Snapshot> redo = new();
        private Dictionary<long, GroundCopy> lastGround = new();   // the ground as the latest step kept it, to share from

        private sealed class Snapshot
        {
            public RectInt Cells;
            public List<GroundCopy> Ground;   // the grid's ground, one copy per chunk (null: not part of it)
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
            public List<TrafficSystem.PlacedVehicle> Cars;
        }

        /// <summary>The ground of one chunk of the grid (the part of it in the grid), as it was.</summary>
        private sealed class GroundCopy
        {
            public RectInt Span;   // global cells
            public float[] Height, WaterLevel;
            public byte[] Land, Lock, Soil, Water, Pave;
        }

        private static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        /// <summary>The grid's ground, chunk by chunk; chunks unchanged since the last step are shared with it.</summary>
        private List<GroundCopy> CopyGround()
        {
            var t = Terrain;
            var grid = t.Cells;
            int n = TerrainStore.Cells;
            var kept = new Dictionary<long, GroundCopy>();
            var list = new List<GroundCopy>();
            for (int cz = TerrainStore.ChunkOf(grid.yMin); cz <= TerrainStore.ChunkOf(grid.yMax - 1); cz++)
            for (int cx = TerrainStore.ChunkOf(grid.xMin); cx <= TerrainStore.ChunkOf(grid.xMax - 1); cx++)
            {
                int x0 = Mathf.Max(cx * n, grid.xMin), z0 = Mathf.Max(cz * n, grid.yMin);
                var span = new RectInt(x0, z0, Mathf.Min((cx + 1) * n, grid.xMax) - x0, Mathf.Min((cz + 1) * n, grid.yMax) - z0);
                long key = TerrainStore.Key(cx, cz);
                if (lastGround.TryGetValue(key, out var before) && before.Span == span && Same(before)) { kept[key] = before; list.Add(before); continue; }
                int count = span.width * span.height;
                var c = new GroundCopy { Span = span, Height = new float[count], WaterLevel = new float[count], Land = new byte[count], Lock = new byte[count], Soil = new byte[count], Water = new byte[count], Pave = new byte[count] };
                for (int z = 0; z < span.height; z++)
                for (int x = 0; x < span.width; x++)
                {
                    int i = (span.yMin + z - grid.yMin) * t.Nx + (span.xMin + x - grid.xMin), k = z * span.width + x;
                    c.Height[k] = t.Height[i]; c.WaterLevel[k] = t.WaterLevel[i];
                    c.Land[k] = Byte(t.Land[i]); c.Lock[k] = Byte(t.Lock[i]); c.Soil[k] = Byte(t.Soil[i]); c.Water[k] = Byte(t.Water[i]); c.Pave[k] = t.Pave[i];
                }
                kept[key] = c; list.Add(c);
            }
            lastGround = kept;
            return list;

            bool Same(GroundCopy c)
            {
                for (int z = 0; z < c.Span.height; z++)
                for (int x = 0; x < c.Span.width; x++)
                {
                    int i = (c.Span.yMin + z - grid.yMin) * t.Nx + (c.Span.xMin + x - grid.xMin), k = z * c.Span.width + x;
                    if (c.Height[k] != t.Height[i] || c.WaterLevel[k] != t.WaterLevel[i] || c.Land[k] != Byte(t.Land[i]) || c.Lock[k] != Byte(t.Lock[i])
                        || c.Soil[k] != Byte(t.Soil[i]) || c.Water[k] != Byte(t.Water[i]) || c.Pave[k] != t.Pave[i]) return false;
                }
                return true;
            }
        }

        private Snapshot Capture() => new()
        {
            Cells = Terrain.Cells,
            Ground = CopyGround(),
            Trees = Forest.Trees.ToArray(), Houses = Buildings.Houses.ToArray(), Animals = Animals.Animals.ToArray(), People = People.Export(),
            Streams = CopyStreams(Streams.Streams), Paths = PathSystem.Copy(Paths.Lines), Bridges = new List<BridgeChoice>(Bridges.Choices),
            Network = Track.Network.ToData(), TrainList = Trains.ToData(), Signals = new List<SignalInstance>(Track.Signals), Mode = Trains.Mode,
            Boats = Boats.Export(),
            Cars = Traffic.ExportPlaced(),
        };

        private static List<StreamPath> CopyStreams(List<StreamPath> streams)
        {
            var copy = new List<StreamPath>(streams.Count);
            foreach (var st in streams) copy.Add(st.Copy());
            return copy;
        }

        /// <summary>
        /// Puts the world back as a step (or a save) has it. <paramref name="everything"/> (loading) sets it all; an undo
        /// changes only what differs, and leaves the trains, people, animals, boats and cars as they are unless the step
        /// differs in which there are (the action placed or removed one).
        /// </summary>
        private void Restore(Snapshot s, bool everything = false)
        {
            if (s.Ground != null)
            {
                if (!Terrain.Endless && s.Cells != Terrain.Cells) { Terrain.Resize(s.Cells); TerrainView.Reinit(); Cover.Scatter(); WaterView.RebuildAll(); }
                var changed = new List<RectInt>();
                foreach (var g in s.Ground)
                {
                    var r = Terrain.WriteCells(g.Span, g.Height, g.WaterLevel, g.Land, g.Lock, g.Soil, g.Water, g.Pave);
                    if (r.width > 0) changed.Add(r);
                }
                TerrainView.Rebuild(changed);
                foreach (var r in changed) WaterView.Rebuild(r);
                lastGround.Clear();   // (the next step copies afresh)
            }
            bool housesChanged = everything || !Buildings.Houses.SequenceEqual(s.Houses);
            if (everything || !Forest.Trees.SequenceEqual(s.Trees)) Forest.SetTrees(s.Trees);
            if (housesChanged) Buildings.SetHouses(s.Houses);
            if (housesChanged || s.Ground != null) ApplyPads();
            if (everything || Animals.Animals.Count != (s.Animals?.Length ?? 0)) Animals.SetAnimals(s.Animals ?? new AnimalInstance[0]);
            if (everything || People.Population != (s.People?.Length ?? 0)) People.SetPeople(s.People ?? new PersonInstance[0]);
            if (everything || !SameStreams(Streams.Streams, s.Streams)) Streams.SetStreams(s.Streams ?? new List<StreamPath>());
            bool pathsChanged = everything || !SamePaths(Paths.Lines, s.Paths);
            if (pathsChanged) Paths.SetLines(s.Paths);
            Track.Signals.Clear();
            if (s.Signals != null) Track.Signals.AddRange(s.Signals);
            Trains.Mode = s.Mode;
            bool networkChanged = everything || !SameNetwork(Track.Network.ToData(), s.Network);
            if (networkChanged) Track.SetNetwork(TrackNetwork.FromData(s.Network), WaterLevelFor, Terrain);
            else { Track.SnapSignals(); Track.RebuildFixtures(); }
            if (everything || Trains.Trains.Count != (s.TrainList?.Count ?? 0)) Trains.FromData(s.TrainList);
            else if (networkChanged) Trains.Relocate();
            Bridges.Choices.Clear();
            if (s.Bridges != null) Bridges.Choices.AddRange(s.Bridges);
            if (everything || pathsChanged || networkChanged || s.Ground != null) RefreshBridges();
            if (everything || Boats.Boats.Count != (s.Boats?.Length ?? 0)) Boats.SetBoats(s.Boats);
            if (everything || Traffic.ExportPlaced().Count != (s.Cars?.Count ?? 0)) Traffic.ImportPlaced(s.Cars);
            RebuildStations();
            Cover.MarkAllDirty();
            if (room != null) FitToRoom();
        }

        private static bool SameStreams(List<StreamPath> a, List<StreamPath> b)
        {
            if (b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].width != b[i].width || !a[i].points.SequenceEqual(b[i].points) || !(a[i].widths ?? new List<float>()).SequenceEqual(b[i].widths ?? new List<float>())) return false;
            return true;
        }

        private static bool SamePaths(List<PathLine> a, List<PathLine> b)
        {
            if (b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].kind != b[i].kind || a[i].closed != b[i].closed || !a[i].points.SequenceEqual(b[i].points)) return false;
            return true;
        }

        private static bool SameNetwork(TrackNetwork.Data a, TrackNetwork.Data b)
        {
            if (b == null || a.edges.Count != b.edges.Count || a.joints.Count != b.joints.Count) return false;
            for (int i = 0; i < a.edges.Count; i++)
            {
                var x = a.edges[i]; var y = b.edges[i];
                if (x.closed != y.closed || x.joint0 != y.joint0 || x.joint1 != y.joint1 || !x.points.SequenceEqual(y.points)) return false;
            }
            for (int i = 0; i < a.joints.Count; i++)
            {
                var x = a.joints[i]; var y = b.joints[i];
                if (x.position != y.position || x.setting != y.setting || x.pinned != y.pinned || !x.ends.SequenceEqual(y.ends)) return false;
            }
            return true;
        }

        /// <summary>Remember the world before an operation, so Undo can return to it.</summary>
        public void Checkpoint()
        {
            undo.Add(Capture());
            redo.Clear();            // a new step: what was undone stays undone
            People.WorldChanged();   // whatever comes next changes where people can walk
            while (undo.Count > MaxUndo) undo.RemoveAt(0);
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

        private void ClearUndo() { undo.Clear(); redo.Clear(); lastGround.Clear(); }

        /// <summary>How many steps can be undone (and redone).</summary>
        public int UndoCount => undo.Count;
        public int RedoCount => redo.Count;
    }
}
