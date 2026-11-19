using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The land outside the terrain grid. The grid holds the ground round the window; everywhere else the ground is
    /// fresh flat land, except where the player changed it - those chunks (60 x 60 cells, 1.2 m square, on a lattice
    /// fixed in the world) are kept here, so the grid can slide away and come back to them, and saved one file per chunk
    /// (only the chunks that changed since the last save are written). Untouched land costs nothing at all.
    /// </summary>
    public sealed class TerrainStore
    {
        public const int Cells = 60;
        public const int CellCount = Cells * Cells;
        private const int FileVersion = 1;

        /// <summary>One chunk's ground, cell by cell (row by row, z then x, from its first global cell).</summary>
        public sealed class Chunk
        {
            public readonly float[] Height = new float[CellCount], Surface = new float[CellCount], WaterLevel = new float[CellCount];
            public readonly byte[] Land = new byte[CellCount], Lock = new byte[CellCount], Soil = new byte[CellCount], Water = new byte[CellCount];
            public bool Dirty;   // changed since it was last written
        }

        private readonly Dictionary<long, Chunk> chunks = new();
        private readonly HashSet<long> onDisk = new();   // chunk files in Folder not read yet
        /// <summary>Where the chunk files live (null: kept in memory only, until the world is saved).</summary>
        public string Folder { get; private set; }

        public int Count => chunks.Count + onDisk.Count;
        public int Loaded => chunks.Count;

        public static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
        public static (int cx, int cz) Unkey(long k) => ((int)(k >> 32), (int)(uint)k);
        public static int ChunkOf(int globalCell) => Mathf.FloorToInt(globalCell / (float)Cells);

        public bool Has(int cx, int cz) => chunks.ContainsKey(Key(cx, cz)) || onDisk.Contains(Key(cx, cz));

        /// <summary>The chunk at (cx, cz) if the player changed it (read from its file the first time it is needed).</summary>
        public bool TryGet(int cx, int cz, out Chunk chunk)
        {
            long k = Key(cx, cz);
            if (chunks.TryGetValue(k, out chunk)) return true;
            if (!onDisk.Contains(k)) return false;
            onDisk.Remove(k);
            chunk = Read(Path.Combine(Folder, FileName(cx, cz)));
            if (chunk == null) return false;   // (unreadable: that chunk is fresh land again rather than the world failing)
            chunks[k] = chunk;
            return true;
        }

        /// <summary>Keeps a chunk (replacing what was kept there); it is written with the next save.</summary>
        public void Put(int cx, int cz, Chunk chunk)
        {
            long k = Key(cx, cz);
            onDisk.Remove(k);
            chunk.Dirty = true;
            chunks[k] = chunk;
        }

        public IEnumerable<long> Keys
        {
            get { foreach (var k in chunks.Keys) yield return k; foreach (var k in onDisk) yield return k; }
        }

        /// <summary>Forgets everything (a new world).</summary>
        public void Clear() { chunks.Clear(); onDisk.Clear(); Folder = null; }

        // ------------------------------------------------------------------ files

        private static string FileName(int cx, int cz) => $"{cx}_{cz}.chunk";

        /// <summary>Starts from the chunk files in <paramref name="folder"/> (read as they are needed).</summary>
        public void Open(string folder)
        {
            Clear();
            Folder = folder;
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, "*.chunk"))
            {
                var parts = Path.GetFileNameWithoutExtension(file).Split('_');
                if (parts.Length == 2 && int.TryParse(parts[0], out int cx) && int.TryParse(parts[1], out int cz)) onDisk.Add(Key(cx, cz));
            }
        }

        /// <summary>
        /// Writes the chunks into <paramref name="folder"/>: into its own folder, only those changed since the last save;
        /// into another (a named world), all of them. Each file is written whole and then swapped in, so a save that is
        /// cut short never leaves a broken chunk behind.
        /// </summary>
        public void Save(string folder)
        {
            Directory.CreateDirectory(folder);
            bool same = Folder != null && Path.GetFullPath(folder) == Path.GetFullPath(Folder);
            // Into its own folder: only what changed. Into another: what changed is written, the rest copied over when
            // the copy there is older.
            void CopyIfNewer(int cx, int cz)
            {
                string from = Path.Combine(Folder, FileName(cx, cz)), to = Path.Combine(folder, FileName(cx, cz));
                if (!File.Exists(from)) return;
                if (File.Exists(to) && File.GetLastWriteTimeUtc(to) >= File.GetLastWriteTimeUtc(from)) return;
                File.Copy(from, to, true);
            }
            if (!same) foreach (var k in onDisk) { var (cx, cz) = Unkey(k); CopyIfNewer(cx, cz); }
            foreach (var (k, c) in chunks)
            {
                var (cx, cz) = Unkey(k);
                if (!c.Dirty) { if (!same && Folder != null) CopyIfNewer(cx, cz); continue; }
                Write(Path.Combine(folder, FileName(cx, cz)), c);
                if (same) c.Dirty = false;
            }
            // Leftovers of an earlier world saved there.
            if (!same)
                foreach (var file in Directory.GetFiles(folder, "*.chunk"))
                {
                    var parts = Path.GetFileNameWithoutExtension(file).Split('_');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int cx) && int.TryParse(parts[1], out int cz) && !Has(cx, cz)) File.Delete(file);
                }
            if (Folder == null)
            {
                // Saved for the first time: this is its folder from now on.
                Folder = folder;
                foreach (var c in chunks.Values) c.Dirty = false;
            }
        }

        private static void Write(string path, Chunk c)
        {
            string tmp = path + ".tmp";
            using (var s = File.Create(tmp)) WriteTo(s, c);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static void WriteTo(Stream stream, Chunk c)
        {
            using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            w.Write(FileVersion);
            foreach (var a in new[] { c.Height, c.Surface, c.WaterLevel }) foreach (float v in a) w.Write(v);
            foreach (var a in new[] { c.Land, c.Lock, c.Soil, c.Water }) w.Write(a);
        }

        private static Chunk ReadFrom(Stream stream)
        {
            using var r = new BinaryReader(stream);
            if (r.ReadInt32() != FileVersion) return null;
            var c = new Chunk();
            foreach (var a in new[] { c.Height, c.Surface, c.WaterLevel }) for (int i = 0; i < CellCount; i++) a[i] = r.ReadSingle();
            foreach (var a in new[] { c.Land, c.Lock, c.Soil, c.Water }) { var b = r.ReadBytes(CellCount); if (b.Length != CellCount) return null; Array.Copy(b, a, CellCount); }
            return c;
        }

        private static Chunk Read(string path)
        {
            try { using var s = File.OpenRead(path); return ReadFrom(s); }
            catch (Exception e)
            {
                Debug.LogWarning($"Wonderland: terrain chunk {path} could not be read ({e.Message}); that land starts fresh");
                return null;
            }
        }

        /// <summary>A chunk as text (a world saved in one piece, e.g. for a copy).</summary>
        public string Export(int cx, int cz)
        {
            if (!TryGet(cx, cz, out var c)) return null;
            using var m = new MemoryStream();
            WriteTo(m, c);
            return Convert.ToBase64String(m.ToArray());
        }

        public void Import(int cx, int cz, string text)
        {
            try { using var m = new MemoryStream(Convert.FromBase64String(text)); var c = ReadFrom(m); if (c != null) Put(cx, cz, c); }
            catch (Exception e) { Debug.LogWarning($"Wonderland: terrain chunk {cx},{cz} could not be read ({e.Message})"); }
        }
    }
}
