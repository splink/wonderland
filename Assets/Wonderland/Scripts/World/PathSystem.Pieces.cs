using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The ways' meshes are kept piece by piece - each road or path, each junction - and a rebuild only redoes the pieces
    /// whose inputs changed: the line's course, the junctions it runs into, the bridges it crosses, the ground along it
    /// (measured, not guessed: sculpting beside a road leaves the ground under it alone, so the road stays as it is).
    /// When the ways themselves haven't changed the junctions aren't looked for again, only levelled where the ground
    /// moved; and when no piece changed, the ways' mesh isn't touched at all.
    /// </summary>
    public sealed partial class PathSystem
    {
        private sealed class Piece
        {
            public readonly MeshBuilder Ground = new(), Props = new();
            public int Key, GroundKey;
            public bool Used;
            public readonly List<(Vector2 at, Vector2 p, Vector2 o, int kind)> Mouths = new();   // (at: its junction's centre)
            public readonly List<(int arm, Vector3 head, Vector3 facing)> Lights = new();

            public void Clear() { Ground.Clear(); Props.Clear(); Mouths.Clear(); Lights.Clear(); }
        }

        private readonly Dictionary<PathLine, Piece> linePieces = new();
        private readonly Dictionary<int, Piece> junctionPieces = new();
        private int waysKey, decksKey;
        private bool junctionsFound;

        // The whole network's mesh data, kept between rebuilds (no growing lists of a hundred thousand vertices each time).
        private readonly MeshBuilder ground = new(), barriers = new();

        private static void Gather(MeshBuilder into, List<Piece> pieces, Func<Piece, MeshBuilder> part)
        {
            into.Clear();
            int v = 0, t = 0;
            foreach (var p in pieces) { v += part(p).VertexCount; t += part(p).Triangles.Count; }
            if (into.Vertices.Capacity < v) { into.Vertices.Capacity = v; into.Normals.Capacity = v; into.Colors.Capacity = v; into.Uvs.Capacity = v; }
            if (into.Triangles.Capacity < t) into.Triangles.Capacity = t;
            foreach (var p in pieces) into.Append(part(p));
        }

        /// <summary>How many pieces the last rebuild had to redo (the rest were reused).</summary>
        public int PiecesBuilt { get; private set; }

        /// <summary>Throws every piece away: the next rebuild redoes them all.</summary>
        public void ForgetAllPieces() { linePieces.Clear(); junctionPieces.Clear(); junctionsFound = false; }

        /// <summary>Whether the ways (or the bridges' decks) changed since the junctions were found.</summary>
        private bool Restructured()
        {
            var h = new HashCode();
            foreach (var l in Lines) { h.Add(l.kind); h.Add(l.closed); h.Add(l.points.Count); foreach (var p in l.points) h.Add(p); }
            int ways = h.ToHashCode();
            int decks = Bridges != null ? Bridges.KeyNear(new Rect(-1e6f, -1e6f, 2e6f, 2e6f)) : 0;
            bool changed = !junctionsFound || ways != waysKey || decks != decksKey;
            waysKey = ways; decksKey = decks; junctionsFound = true;
            return changed;
        }

        /// <summary>The same junctions, levelled again where the ground under them changed.</summary>
        private void Relevel()
        {
            LightHeads.Clear(); mouths.Clear();
            foreach (var j in Junctions)
            {
                float reach = j.Radius + 0.01f;
                if (terrain.ChangedNear(new Rect(j.At.x - reach, j.At.y - reach, reach * 2f, reach * 2f))) j.Y = LevelOf(j);
            }
        }

        private static Rect AreaOf(PathLine l, float margin)
        {
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var p in l.points) { x0 = Mathf.Min(x0, p.x); z0 = Mathf.Min(z0, p.y); x1 = Mathf.Max(x1, p.x); z1 = Mathf.Max(z1, p.y); }
            return Rect.MinMaxRect(x0 - margin, z0 - margin, x1 + margin, z1 + margin);
        }

        private static bool Touches(Rect r, Vector2 c, float radius) =>
            c.x + radius > r.xMin && c.x - radius < r.xMax && c.y + radius > r.yMin && c.y - radius < r.yMax;

        /// <summary>The ground along a way, to a twentieth of a millimetre: across its width and its kerbs, three times a stretch.</summary>
        private int GroundKey(PathLine l)
        {
            var h = new HashCode();
            float reach = HalfWidth(l.kind) + Pavement(l.kind) + 0.01f;
            int n = l.points.Count, segs = l.closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = l.points[i]; var b = l.points[(i + 1) % n];
                var t = b - a; if (t.sqrMagnitude < 1e-12f) continue;
                var right = new Vector2(t.y, -t.x).normalized;
                for (int s = 0; s < 3; s++)
                {
                    var p = Vector2.Lerp(a, b, s / 3f);
                    for (int k = -3; k <= 3; k++) { var q = p + right * (reach * k / 3f); h.Add(Mathf.RoundToInt(terrain.SurfaceAt(q.x, q.y) * 20000f)); }
                }
            }
            return h.ToHashCode();
        }

        /// <summary>The ground round a junction, where its kerbs and traffic lights stand.</summary>
        private int GroundKey(RoadJunction j)
        {
            var h = new HashCode();
            for (int k = 0; k < 24; k++)
            {
                float ang = k / 24f * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                foreach (float r in new[] { j.Radius, j.Radius + 0.02f, j.Radius + 0.045f }) { var q = j.At + d * r; h.Add(Mathf.RoundToInt(terrain.SurfaceAt(q.x, q.y) * 20000f)); }
            }
            return h.ToHashCode();
        }

        private Piece WayPiece(PathLine l)
        {
            var area = AreaOf(l, HalfWidth(l.kind) + 0.03f);
            var h = new HashCode();
            h.Add(l.kind); h.Add(l.closed);
            foreach (var p in l.points) h.Add(p);
            foreach (var j in Junctions)
                if (Touches(area, j.At, j.Radius + JunctionBlend))
                {
                    h.Add(j.At); h.Add(j.Radius); h.Add(j.Y); h.Add(j.Arms.Count); h.Add(j.Lights); h.Add(j.Members.Contains(l));
                }
            if (Bridges != null) h.Add(Bridges.KeyNear(area));
            h.Add(BaysKey(l));
            int key = h.ToHashCode();
            bool have = linePieces.TryGetValue(l, out var piece) && piece.Key == key;
            int groundKey = have && !terrain.ChangedNear(area) ? piece.GroundKey : GroundKey(l);
            if (!have || groundKey != piece.GroundKey)
            {
                piece ??= new Piece();
                piece.Clear();
                int before = mouths.Count;
                if (IsRoad(l.kind)) RoadSurface(piece.Ground, piece.Props, l); else Way(piece.Ground, piece.Props, l);
                for (int i = before; i < mouths.Count; i++) { var m = mouths[i]; piece.Mouths.Add((Junctions[m.junction].At, m.p, m.o, m.kind)); }
                mouths.RemoveRange(before, mouths.Count - before);
                piece.Key = key; piece.GroundKey = groundKey;
                linePieces[l] = piece;
                PiecesBuilt++;
            }
            piece.Used = true;
            foreach (var m in piece.Mouths)
                for (int ji = 0; ji < Junctions.Count; ji++)
                    if ((Junctions[ji].At - m.at).sqrMagnitude < 1e-10f) { mouths.Add((ji, m.p, m.o, m.kind)); break; }
            return piece;
        }

        /// <summary>The cached piece for a junction (<paramref name="build"/>: it has to be built first).</summary>
        private Piece JunctionPiece(RoadJunction j, List<(Vector2 p, Vector2 o, int kind)> ms, out bool build)
        {
            var h = new HashCode();
            h.Add(j.At); h.Add(j.Radius); h.Add(j.Y); h.Add(j.Lights);
            for (int k = 0; k < j.Arms.Count; k++) { h.Add(j.Arms[k]); h.Add(j.ArmKind[k]); }
            foreach (var m in ms) { h.Add(m.p); h.Add(m.o); h.Add(m.kind); }
            float reach = j.Radius + 0.06f;
            var area = new Rect(j.At.x - reach, j.At.y - reach, reach * 2f, reach * 2f);
            if (Bridges != null) h.Add(Bridges.KeyNear(area));
            int key = h.ToHashCode();
            bool have = junctionPieces.TryGetValue(key, out var piece) && !piece.Used;
            int groundKey = have && !terrain.ChangedNear(area) ? piece.GroundKey : GroundKey(j);
            build = !have || groundKey != piece.GroundKey;
            if (build)
            {
                piece = new Piece { Key = key, GroundKey = groundKey };
                junctionPieces[key] = piece;
                PiecesBuilt++;
            }
            piece.Used = true;
            return piece;
        }

        /// <summary>After a rebuild: pieces nothing used any more go; the ground's changes have all been seen.</summary>
        private void ForgetPieces()
        {
            var gone = new List<PathLine>();
            foreach (var (l, p) in linePieces) { if (!p.Used) gone.Add(l); p.Used = false; }
            foreach (var l in gone) linePieces.Remove(l);
            var stale = new List<int>();
            foreach (var (k, p) in junctionPieces) { if (!p.Used) stale.Add(k); p.Used = false; }
            foreach (var k in stale) junctionPieces.Remove(k);
            terrain.ForgetChanges();
        }
    }
}
