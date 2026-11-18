using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>The world's railway: building, reshaping and removing track, its profile over the land, stations, trains, switches and signals.</summary>
    public sealed partial class WonderlandWorld
    {
        // ------------------------------------------------------------------ the railway over the land

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
                Trains.Stations.Add(new StationStop { Edge = edge, S = s, Side = side, House = new Vector2(h.x, h.z), Freight = h.variant == Catalog.FreightYard });
            }
            b.ToMesh("Platforms", mesh);
            mesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ railway

        /// <summary>
        /// Plans new track from a drawn stroke and, when valid, adds it to the network (checkpointed): extending a line,
        /// branching off or joining with switches, crossing other track, or a new loop. Nothing existing is replaced.
        /// </summary>
        public TrackPlan BuildTrack(IReadOnlyList<Vector3> drawn)
        {
            // Planned first, changing nothing: a stroke that can't be built costs no undo step (and no restoring the world).
            var net = Track.Network;
            var plan = TrackPlanner.Plan(drawn, Terrain, net, null, landWillGrow: true);
            if (!plan.Ok) return plan;
            var before = net.Clone();
            if (!plan.Closed && !net.Empty)
            {
                // One way: would trains have to turn round? Tried on a copy.
                var copy = net.Clone();
                Attach(copy, plan);
                copy.Orient(e => copy.Agreement(e, before), out _);
                if (copy.NewTurnBack(before)) { plan.Error = TrackNetwork.TurnBack; return plan; }
            }
            // Railway drawn over bare floor grows its own land: checkpoint first so undo also takes the new land back.
            Checkpoint();
            if (!GrowLandUnder(plan.Path)) { Undo(false); plan.Error = "Part of the railway is too far out to build"; return plan; }
            Attach(net, plan);
            // One way: the new piece runs the way the track it joins runs (a loop being joined turns to match).
            net.Orient(e => net.Agreement(e, before), out _);
            AfterNetworkChanged();
            return plan;
        }

        /// <summary>Grows land under any part of a new railway drawn over bare floor; false if some of it is too far out.</summary>
        private bool GrowLandUnder(List<Vector2> path)
        {
            bool off = false;
            foreach (var p in path) if (Terrain.EdgeAt(p.x, p.y) < 0.4f) { off = true; break; }
            if (!off) return true;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in path) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
            EnsureGridCovers(Rect.MinMaxRect(minX - 0.25f, minZ - 0.25f, maxX + 0.25f, maxZ + 0.25f));
            var region = Terrain.GrowLandAlong(path, 0.22f);
            TerrainView.Rebuild(region);
            Cover.MarkAllDirty();
            foreach (var p in path) if (Terrain.EdgeAt(p.x, p.y) < 0.4f) return false;
            return true;
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
            var before = net;   // (left as it is: the piece goes onto the copy)
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
            net.Reindex();   // (a middle attachment is found on the pieces as they are; each split re-indexes)
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
            // The switch lands on the existing track's own point (it is split there), a few millimetres from the planned
            // one: move the new piece's first (last) 5 cm with it, fading out - so it leaves the track smoothly instead of
            // with a kink, a little wiggle, on a curve.
            void Ease(Vector3 to, bool atStart)
            {
                var from = atStart ? pts[0] : pts[^1];
                var shift = new Vector3(to.x - from.x, 0f, to.z - from.z);
                for (int k = 0; k < pts.Count && k <= 5; k++)
                {
                    int i = atStart ? k : pts.Count - 1 - k;
                    float w = 1f - k / 5f;
                    pts[i] = new Vector3(pts[i].x + shift.x * w, pts[i].y, pts[i].z + shift.z * w);
                }
                if (atStart) pts[0] = to; else pts[^1] = to;
            }
            if (startEnds != null) Ease(startAt, true);
            if (finishEnds != null) Ease(finishAt, false);
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
                if (!net.Nearest(p, out int edge, out float s, out _, 0.02f)) return null;
                int index = net.PointAt(edge, s);
                at = net.Edges[edge].Layout.Points[index];
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
            // Only the ground the railway lies on (now or before) changes: rebuild just that part of the land.
            var graded = railArea;
            foreach (var e in net.Edges) graded = Span(graded, AreaOf(TrackSystem.GradeTerrain(Terrain, e.Layout, WaterLevelFor, e.Tunnel)));
            Terrain.UpdateSurface(Terrain.All);
            if (graded.width > 0f) TerrainView.Rebuild(Terrain.RegionAround(graded.center, Mathf.Max(graded.width, graded.height) * 0.5f + 0.02f));
            railArea = graded;
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

        private Rect railArea;   // the land (metres) the railway has been graded into: rebuilt when the railway changes

        private Rect AreaOf(RectInt r) => r.width <= 0 || r.height <= 0 ? default
            : Rect.MinMaxRect(Terrain.LocalX(r.xMin), Terrain.LocalZ(r.yMin), Terrain.LocalX(r.xMax), Terrain.LocalZ(r.yMax));

        /// <summary>Both areas (either may be empty).</summary>
        private static Rect Span(Rect a, Rect b)
        {
            if (a.width <= 0f || a.height <= 0f) return b;
            if (b.width <= 0f || b.height <= 0f) return a;
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
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
        /// <summary>Puts one of the player's vehicles on the road nearest <paramref name="local"/> (checkpointed); null, or why not.</summary>
        public string PlaceVehicle(Vector3 local, VehicleKind kind, float facing, int colour)
        {
            Checkpoint();
            var v = Traffic.PlaceOnRoad(kind, new Vector2(local.x, local.z), facing, colour, out string error);
            if (v == null) Undo(false);
            return error;
        }

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

        /// <summary>Takes away signal <paramref name="i"/> (checkpointed).</summary>
        public string RemoveSignal(int i)
        {
            Checkpoint();
            Track.Signals.RemoveAt(i); Track.SnapSignals(); Track.RebuildFixtures();
            return "Signal removed";
        }
    }
}
