using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>The world's roads, lanes, paths, fences and hedges.</summary>
    public sealed partial class WonderlandWorld
    {
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
            MarkAlong(line, hw + 0.05f);   // (the trees and ground cover along it, not the whole world's)
            People.Rehome();
            return null;
        }

        /// <summary>The trees and ground cover along a way are laid again.</summary>
        private void MarkAlong(PathLine line, float reach)
        {
            var last = new Vector2(float.MaxValue, 0f);
            foreach (var p in line.points)
            {
                if ((p - last).sqrMagnitude < 0.0064f) continue;   // (every 8 cm)
                last = p;
                Forest.MarkDirtyRegion(p, reach); Cover.MarkDirtyRegion(p, reach);
            }
            if (line.points.Count > 0) { Forest.MarkDirtyRegion(line.points[^1], reach); Cover.MarkDirtyRegion(line.points[^1], reach); }
        }

        public bool RemovePathAt(Vector3 local)
        {
            int i = Paths.Pick(new Vector2(local.x, local.z));
            if (i < 0) return false;
            Checkpoint();
            var gone = Paths.Lines[i];
            Paths.Lines.RemoveAt(i);
            RefreshBridges();
            MarkAlong(gone, PathSystem.HalfWidth(gone.kind) + 0.05f);
            return true;
        }
    }
}
