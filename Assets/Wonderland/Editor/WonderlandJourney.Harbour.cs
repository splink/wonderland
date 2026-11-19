using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        private static readonly int[] HarbourParts = { Catalog.Harbour, Catalog.ContainerYard, Catalog.Warehouse, Catalog.HarbourGate };

        /// <summary>A port on a lake's north shore - quay, yard, warehouse, gate, service road and security fence - and a second quay across the water.</summary>
        private static WonderlandWorld Port(float x, out string report, out Vector2 quay)
        {
            var w = Scratch("Port", x);
            var log = new System.Text.StringBuilder();
            Lake(w, -0.55f, 0.55f, 0.22f);
            var t = w.Terrain;
            var a = w.ShoreSnap(t.PointAt(-0.1f, 0.24f), Catalog.Harbour, 0);
            var b = w.ShoreSnap(t.PointAt(0.4f, -0.27f), Catalog.Harbour, 0);
            log.AppendLine($"quay: {(a.HasValue ? w.PlaceHouse(a.Value) ?? "ok" : "no spot")}, across the water: {(b.HasValue ? w.PlaceHouse(b.Value) ?? "ok" : "no spot")}");
            quay = a.HasValue ? new Vector2(a.Value.x, a.Value.z) : new Vector2(-0.1f, 0.3f);
            float hx = quay.x, hz = quay.y;
            foreach (var (id, px, pz) in new[] { (Catalog.ContainerYard, hx - 0.05f, hz + 0.18f), (Catalog.Warehouse, hx + 0.19f, hz + 0.175f), (Catalog.HarbourGate, hx + 0.075f, hz + 0.335f) })
                log.AppendLine($"{Catalog.Get(id).Name}: {w.PlaceHouse(new HouseInstance { x = px, z = pz, rotation = 0f, variant = id }) ?? "ok"}");
            log.AppendLine($"service road: {w.BuildPath(Line(t, new Vector2(hx - 0.14f, hz + 0.078f), new Vector2(hx + 0.075f, hz + 0.078f), new Vector2(hx + 0.075f, hz + 0.29f)), PathKind.ServiceRoad) ?? "ok"}");
            log.AppendLine($"street: {w.BuildPath(Line(t, new Vector2(hx + 0.075f, hz + 0.38f), new Vector2(hx + 0.075f, hz + 0.5f), new Vector2(hx + 0.45f, hz + 0.5f)), PathKind.Road) ?? "ok"}");
            log.AppendLine($"fence: {w.BuildPath(Line(t, new Vector2(hx - 0.2f, hz + 0.02f), new Vector2(hx - 0.2f, hz + 0.31f), new Vector2(hx + 0.002f, hz + 0.31f)), PathKind.SecurityFence) ?? "ok"}, " +
                           $"{w.BuildPath(Line(t, new Vector2(hx + 0.148f, hz + 0.31f), new Vector2(hx + 0.3f, hz + 0.31f), new Vector2(hx + 0.3f, hz + 0.02f)), PathKind.SecurityFence) ?? "ok"}");
            w.Harbours.Step(2.1f);
            log.AppendLine($"{w.Harbours.ShipCount} ships, {w.Harbours.Machines} machines");
            report = log.ToString();
            return w;
        }

        /// <summary>
        /// The harbour (living suite): a family of pieces built at all five levels, laid out as a port behind a quay -
        /// yard, warehouse, gate, service road, barbed-wire fence - with its machines at work (the straddle carrier and the
        /// forklift moving loads) while the ship calls; the ship and the crane are detailed up close.
        /// </summary>
        private static void HarbourJourney()
        {
            int levels = 0;
            foreach (int id in HarbourParts) { bool all = true; for (int d = MeshBuilder.Extreme; d <= MeshBuilder.Minimal; d++) all &= HouseFactory.Get(id, 0, d) != null; if (all) levels++; }
            Check(levels == HarbourParts.Length && Catalog.InCategory(PlaceCategory.Harbour).Count == HarbourParts.Length, $"The harbour is a family of pieces, each built at all five levels ({levels}/{HarbourParts.Length}; {Catalog.InCategory(PlaceCategory.Harbour).Count} in the Harbour category)");
            int shipVerts = HouseFactory.ShipMesh().vertexCount, craneVerts = HouseFactory.CraneMesh().vertexCount, farShip = HouseFactory.ShipMesh(MeshBuilder.Minimal).vertexCount;
            Check(shipVerts > 2000 && craneVerts > 1000 && farShip < shipVerts, $"The container ship and the gantry crane are detailed up close ({shipVerts} and {craneVerts} vertices) and simpler far off (the ship {farShip})");
            var w = Port(760f, out string report, out _);
            Report.Append("INFO  ").AppendLine(report.Replace("\n", "; "));
            int placed = 0; foreach (int id in HarbourParts) if (w.Buildings.Houses.Exists(h => h.variant == id)) placed++;
            bool fence = w.Paths.Lines.Exists(l => l.kind == (int)PathKind.SecurityFence), service = w.Paths.Lines.Exists(l => l.kind == (int)PathKind.ServiceRoad);
            Check(placed == HarbourParts.Length && fence && service, $"A port is laid out behind its quay: {placed}/{HarbourParts.Length} pieces, a service road {service}, a barbed-wire fence {fence}");
            int moved0 = w.Harbours.Moved;
            Live(w, 60f, s => w.Harbours.Step(Dt));
            Check(w.Harbours.Machines == 2 && w.Harbours.Moved - moved0 >= 4, $"The straddle carrier and the forklift are at work ({w.Harbours.Machines} machines, {w.Harbours.Moved - moved0} loads moved in a minute)");
            Object.DestroyImmediate(w.gameObject);
        }

        public static void HarbourRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), Vector3.zero, 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = Port(0f, out string report, out var q);
            w.Sky.Shown = false;
            w.Clock.Hour = 11f;
            for (int k = 0; k < 300; k++) { w.Harbours.Step(0.1f); w.Traffic.Step(0.1f); }
            void Shot(string file, Vector3 from, Vector3 at, float fov) { cam.transform.position = from; cam.transform.LookAt(at); cam.fieldOfView = fov; WonderlandRenders.Capture(cam, file, 1600, 1000); }
            var c = new Vector3(q.x, 0f, q.y);
            Shot("editor-harbour.png", c + new Vector3(0.1f, 0.55f, -0.75f), c + new Vector3(0.05f, 0f, 0.12f), 48f);
            Shot("editor-harbour-quay.png", c + new Vector3(-0.2f, 0.12f, -0.22f), c + new Vector3(0f, 0.06f, -0.05f), 45f);
            var hull = w.Harbours.transform.Find("Container ship");
            if (hull != null) { var s = hull.localPosition; var f = hull.localRotation * Vector3.forward; Shot("editor-harbour-ship.png", s + f * 0.05f + hull.localRotation * Vector3.right * 0.16f + Vector3.up * 0.07f, s + Vector3.up * 0.012f, 42f); }
            Shot("editor-harbour-yard.png", c + new Vector3(-0.05f, 0.16f, 0.02f), c + new Vector3(-0.05f, 0f, 0.18f), 45f);
            Shot("editor-harbour-warehouse.png", c + new Vector3(0.2f, 0.1f, 0.03f), c + new Vector3(0.19f, 0.01f, 0.15f), 45f);
            Shot("editor-harbour-gate.png", c + new Vector3(0.08f, 0.09f, 0.52f), c + new Vector3(0.075f, 0.01f, 0.33f), 45f);
            var probe = new System.Text.StringBuilder(report);
            var qh = w.Buildings.Houses.Find(h => h.variant == Catalog.Harbour);
            for (float z = -0.04f; z >= -0.13f; z -= 0.01f)
            {
                var p = new Vector2(qh.x, qh.z) + (Vector2)(Quaternion.Euler(0f, 0f, -qh.rotation) * new Vector2(0f, z));
                probe.AppendLine($"quay z {z:0.00}: water {w.Terrain.WaterAt(p.x, p.y):0.00} ground {w.Terrain.SurfaceAt(p.x, p.y) * 1000f:0.0} mm, level {w.Terrain.WaterLevelAt(p.x, p.y) * 1000f:0.0} mm");
            }
            report = probe.ToString();
            System.IO.File.WriteAllText("Logs/harbourrender-results.txt", report);
            Object.DestroyImmediate(w.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
