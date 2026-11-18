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
    }
}
