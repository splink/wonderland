using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>The world's houses: placing, validating, pads under them, picking up, restyling and removing.</summary>
    public sealed partial class WonderlandWorld
    {
        // ------------------------------------------------------------------ houses

        /// <summary>
        /// Grades a flat pad under a house (cut and fill to its average footprint height, soft 7 cm margin) and protects
        /// it from brushes, so a house never ends up half buried on a slope.
        /// </summary>
        private RectInt PadHouse(HouseInstance h, bool reshape = true)
        {
            var region = PadRegion(h, out var e, out float margin);
            if (region.width <= 0) return region;
            float target = Buildings.SitHeight(h, out _);
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
                if (reshape) Terrain.Height[i] = Mathf.Lerp(Terrain.Height[i], Terrain.HeightForSurface(i, target), w);
                Terrain.Pad[i] = Mathf.Max(Terrain.Pad[i], d <= 0f ? 1f : w * 0.9f);
            }
            Terrain.UpdateSurface(region);
            return region;
        }

        /// <summary>
        /// The ground a house's pad covers: fully flat and protected out to 2.5 cm beyond the footprint (bilinear sampling
        /// reaches one 2 cm cell out), then a soft 7 cm blend into the surrounding ground. Empty for rocks, camps and small
        /// details, which follow the ground.
        /// </summary>
        private RectInt PadRegion(HouseInstance h, out Vector2 halfExtents, out float margin)
        {
            halfExtents = HouseFactory.HalfExtents(h.variant) + Vector2.one * 0.025f;
            margin = 0.07f;
            if (!Catalog.Get(h.variant).Pad) return new RectInt(0, 0, 0, 0);
            return Terrain.RegionAround(new Vector2(h.x, h.z), halfExtents.magnitude + margin + 0.02f);
        }

        /// <summary>
        /// Re-derives every house pad (after houses were removed, moved, restored or loaded) - or, with
        /// <paramref name="within"/>, the pads there: every house whose pad reaches into it is padded again.
        /// </summary>
        /// <remarks>With <paramref name="reshape"/> false only the protection is laid down again, the ground left as it is
        /// (land coming back into the grid: it was graded when the house was put there).</remarks>
        public void ApplyPads(RectInt? within = null, bool reshape = true)
        {
            var area = within ?? Terrain.All;
            for (int z = area.yMin; z < area.yMax; z++)
                System.Array.Clear(Terrain.Pad, z * Terrain.Nx + area.xMin, area.width);
            var padded = new List<RectInt>();
            foreach (var h in Buildings.Houses)
                if (within == null || PadRegion(h, out _, out _).Overlaps(area)) padded.Add(PadHouse(h, reshape));
            if (!reshape) return;   // (the ground is as it was: nothing looks or sits differently)
            TerrainView.Rebuild(padded);
            Buildings.Resit();
            Forest.MarkAllDirty();
            RebuildStations();
        }

        public string ValidateHouse(HouseInstance h, int ignore = -1)
        {
            int boat = Catalog.BoatOf(h.variant);
            if (boat >= 0) return Boats.Validate(new Vector2(h.x, h.z), h.rotation + 180f, boat);
            if (h.variant == Catalog.Jetty && ValidateJetty(h) is { } jetty) return jetty;
            if (h.variant == Catalog.Harbour && ValidateHarbour(h) is { } harbour) return harbour;
            if (h.variant == Catalog.Zebra && !(Paths.WayDistance(new Vector2(h.x, h.z), out int zk) < -0.01f && PathSystem.IsCarWay(zk))) return "A zebra crossing goes across a street";
            string error = (Catalog.IsStation(h.variant) ? ValidateStation(h) : null) ?? Buildings.Validate(h, ignore, p => Track.DistanceTo(new Vector3(p.x, 0f, p.y)));
            if (error == null)
                foreach (var p in BuildingSystem.FootprintSamples(h))
                {
                    if (Streams.IsWet(p, 0.01f)) return "That's a stream";
                    if (Paths.NearBarrier(p, 0.004f)) return "That's a fence";
                    if (!Catalog.Get(h.variant).Small && Paths.WayDistance(p, out int kind) < PathSystem.Pavement(kind) - 0.004f) return kind == 0 ? "That's the road (and its pavement)" : "That's the path";
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

        /// <summary>
        /// A harbour (or a landing stage) where the pointer is, snapped to the shore: turned to face the open water nearest
        /// it and moved on or off the bank until it fits. Null if there is no water near enough.
        /// </summary>
        public HouseInstance? ShoreSnap(Vector3 local, int variant, int style)
        {
            var at = new Vector2(local.x, local.z);
            var options = new List<(float score, Vector2 dir)>();
            for (int k = 0; k < 24; k++)
            {
                float a = k * 15f * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                float wet = 0f;
                for (float r = 0.04f; r <= 0.24f; r += 0.02f) wet += Terrain.WaterAt(at.x + d.x * r, at.y + d.y * r);
                if (wet > 0.5f) options.Add((wet, d));
            }
            options.Sort((p, q) => q.score.CompareTo(p.score));
            foreach (var (_, d) in options)
            {
                float yaw = Mathf.Atan2(-d.x, -d.y) * Mathf.Rad2Deg;   // its front (-Z) to the water
                for (float shift = 0f; shift <= 0.16f; shift += 0.01f)
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        var p = at + d * (sign * shift);
                        var h = new HouseInstance { x = p.x, z = p.y, rotation = yaw, variant = variant, style = style };
                        if (ValidateHouse(h) == null) return h;
                    }
            }
            return null;
        }

        /// <summary>A zebra crossing where the pointer is, snapped across the street nearest it (null: no street near).</summary>
        public HouseInstance? ZebraSnap(Vector3 local)
        {
            var p = new Vector2(local.x, local.z);
            if (!Paths.AcrossRoad(p, out var at, out float yaw)) return null;
            return new HouseInstance { x = at.x, z = at.y, rotation = yaw, variant = Catalog.Zebra };
        }

        /// <summary>Whether a point is on a zebra crossing.</summary>
        public bool OnZebra(Vector2 p)
        {
            foreach (var h in Buildings.Houses)
            {
                if (h.variant != Catalog.Zebra) continue;
                var d = Quaternion.Euler(0f, -h.rotation, 0f) * new Vector3(p.x - h.x, 0f, p.y - h.z);
                var half = Catalog.Get(Catalog.Zebra).Half;
                if (Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.z) <= half.y + 0.006f) return true;
            }
            return false;
        }

        /// <summary>A harbour stands on the shore, its front to open water deep and wide enough for a ship to moor.</summary>
        private string ValidateHarbour(HouseInstance h)
        {
            var rot = Quaternion.Euler(0f, h.rotation, 0f);
            foreach (float x in new[] { -0.07f, 0f, 0.07f })
            {
                var p = new Vector3(h.x, 0f, h.z) + rot * new Vector3(x, 0f, HarbourSystem.MooringZ);
                if (Terrain.WaterAt(p.x, p.z) < 0.6f) return "Its front must face open water - a ship moors alongside it";
            }
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

        /// <summary>Lifts a house off the map so it can be repositioned (checkpointed: undo puts it back).</summary>
        public HouseInstance PickUpHouse(int index)
        {
            Checkpoint();
            var h = Buildings.RemoveAt(index);
            ApplyPads();
            Cover.MarkDirtyRegion(new Vector2(h.x, h.z), BuildingSystem.Radius(h.variant));
            return h;
        }

        /// <summary>Takes away building <paramref name="index"/> (checkpointed); what went, in words.</summary>
        public string RemoveHouse(int index)
        {
            Checkpoint();
            var h = Buildings.RemoveAt(index);
            ApplyPads();
            Cover.MarkDirtyRegion(new Vector2(h.x, h.z), 0.1f);
            return HouseFactory.Name(h.variant) + " removed";
        }

        /// <summary>Gives building <paramref name="index"/> its next colour style (checkpointed); null, or why not.</summary>
        public string RestyleHouse(int index)
        {
            if (index < 0 || index >= Buildings.Houses.Count) return "It has gone";
            var h = PickUpHouse(index);
            h.style = (h.style + 1) % Mathf.Max(1, Catalog.Get(h.variant).Styles);
            string error = PlaceHouse(h, false);
            if (error != null) { Undo(false); return error; }
            return null;
        }
    }
}
