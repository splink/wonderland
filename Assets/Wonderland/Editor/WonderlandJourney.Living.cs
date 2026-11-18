using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wonderland.Editor
{
    /// <summary>
    /// The living world: activities that emerge from what is built. Each scene is a small world built for the purpose,
    /// run through village time, with the activity watched from start to end - and the same activity in a world where
    /// it can't happen (no road, nothing to carry, the place removed half way), which must stay calm and never get stuck.
    /// </summary>
    public static partial class WonderlandJourney
    {
        public static void TestLiving()
        {
            Report.Clear(); failures = 0; BuildGrid.Snap = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            try { Deliveries(); }
            catch (Exception e) { Fail("Exception: " + e); }
            BuildGrid.Snap = true;
            Report.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
            File.WriteAllText("Logs/living-results.txt", Report.ToString());
            Debug.Log("Wonderland living test\n" + Report);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>Runs a world's village for <paramref name="seconds"/>, calling <paramref name="watch"/> every step.</summary>
        private static void Live(WonderlandWorld w, float seconds, Action<float> watch = null)
        {
            for (float s = 0f; s < seconds; s += Dt)
            {
                w.Clock.Tick(Dt); w.People.Step(Dt); w.Traffic.Step(Dt);
                watch?.Invoke(s);
            }
        }

        /// <summary>
        /// Lorries: the barn's lorry collects crates from the barn and takes them to the bakery along the road - loading
        /// them onto its bed one by one, carrying them, unloading them into the bakery's stock - then goes home. With no
        /// road to the bakery, nothing happens; when the bakery is taken down while the lorry is on its way, the lorry
        /// takes the load to the grocer instead (or home), and nothing is left waiting.
        /// </summary>
        private static void Deliveries()
        {
            var w = Scratch("Deliveries", 0f);
            var t = w.Terrain;
            string road = w.BuildPath(Line(t, new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f)), PathKind.Road);
            string barn = w.PlaceHouse(new HouseInstance { x = -0.45f, z = 0.11f, rotation = 0f, variant = Catalog.Barn });
            string bakery = w.PlaceHouse(new HouseInstance { x = 0.45f, z = 0.11f, rotation = 0f, variant = Catalog.Bakery });
            int bi = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Barn), ki = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
            w.People.Grid.Complete(); w.People.Step(Dt); w.Traffic.Refresh(true);
            w.Clock.Hour = 9f;
            w.People.SetStock(bi, 6f); w.People.SetStock(ki, 0f);
            var lorry = w.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Flatbed);
            Check(road == null && barn == null && bakery == null && lorry != null && lorry.Cargo == Cargo.Produce,
                $"A barn by a road keeps an open lorry for its produce ({(lorry != null ? lorry.Owner : "none")}{road ?? ""}{barn ?? ""}{bakery ?? ""})");
            if (lorry == null) return;
            var said = new List<string>();
            int mostShown = 0; bool hopSeen = false, onRoad = true;
            float stockBefore = w.People.StockAt(ki);
            Live(w, 300f, s =>
            {
                string line = w.Traffic.Describe(lorry);
                if (said.Count == 0 || said[^1] != line) said.Add(line);
                if (lorry.Load != null && lorry.Load.gameObject.activeSelf && lorry.Job != null) mostShown = Mathf.Max(mostShown, lorry.Job.Shown);
                var carried = w.Traffic.transform.Find("Crate carried");
                hopSeen |= carried != null && carried.gameObject.activeSelf;
                if (lorry.State == TrafficSystem.Mode.Driving) onRoad &= w.Traffic.OffRoad(lorry.Position) < 0.03f;
            });
            string story = string.Join(" / ", said);
            bool loaded = said.Exists(x => x.Contains("loading")), carriedTo = said.Exists(x => x.Contains("to the bakery")), unloaded = said.Exists(x => x.Contains("unloading"));
            Check(w.Traffic.Delivered >= 1 && loaded && carriedTo && unloaded && hopSeen && mostShown >= 2 && onRoad,
                $"The lorry loads crates at the barn one by one, takes them along the road to the bakery and unloads them ({w.Traffic.Delivered} deliveries, up to {mostShown} crates on the bed): {story}");
            Check(w.People.StockAt(ki) > stockBefore + 1.5f || w.Traffic.Delivered >= 1 && w.People.StockAt(ki) >= 1f,
                $"The bakery's shelves fill from the delivery ({stockBefore:0.0} -> {w.People.StockAt(ki):0.0} crates)");
            Check(lorry.Job == null && (lorry.State == TrafficSystem.Mode.Parked || said[^1].Contains("heading back")),
                $"...and the lorry goes home afterwards ({said[^1]})");

            // No road to the place that wants it: no delivery is planned, nothing waits.
            var w2 = Scratch("No road", 40f);
            var t2 = w2.Terrain;
            w2.BuildPath(Line(t2, new Vector2(-0.7f, 0f), new Vector2(-0.1f, 0f)), PathKind.Road);
            w2.PlaceHouse(new HouseInstance { x = -0.45f, z = 0.11f, variant = Catalog.Barn });
            w2.PlaceHouse(new HouseInstance { x = 0.5f, z = 0.4f, variant = Catalog.Bakery });
            int b2 = w2.Buildings.Houses.FindIndex(h => h.variant == Catalog.Barn), k2 = w2.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
            w2.People.Grid.Complete(); w2.People.Step(Dt); w2.Traffic.Refresh(true);
            w2.Clock.Hour = 9f; w2.People.SetStock(b2, 6f); w2.People.SetStock(k2, 0f);
            var lorry2 = w2.Traffic.Vehicles.Find(v => v.Kind == VehicleKind.Flatbed);
            bool everJob = false;
            Live(w2, 90f, s => everJob |= lorry2 != null && lorry2.Job != null);
            Check(lorry2 != null && !everJob && w2.Traffic.Delivered == 0, $"Without a road to the bakery the lorry stays at the barn ({(lorry2 != null ? w2.Traffic.Describe(lorry2) : "no lorry")})");

            // The bakery taken down while the lorry is bringing the crates: they go to the grocer instead.
            w.People.SetStock(bi, 6f); w.People.SetStock(ki, 0f);
            w.PlaceHouse(new HouseInstance { x = 0.15f, z = -0.11f, rotation = 180f, variant = Catalog.Grocer });
            int gi = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Grocer);
            w.People.Grid.Complete(); w.People.Step(Dt); w.Traffic.Refresh(true);
            w.People.SetStock(gi, 1f);
            int before = w.Traffic.Delivered;
            bool removed = false; string redirected = null; float removedAt = 0f;
            Live(w, 300f, s =>
            {
                if (!removed && lorry.Job != null && lorry.Job.Stage == 2)
                {
                    int k = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Bakery);
                    if (k >= 0) { w.RemoveHouse(k); removed = true; removedAt = s; }
                }
                if (removed && redirected == null && s > removedAt + 2f) redirected = w.Traffic.Describe(lorry);   // (lorries look again once a second)
            });
            gi = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Grocer);
            Check(removed && redirected != null && redirected.Contains("grocer") && w.Traffic.Delivered > before && lorry.Job == null,
                $"Taking the bakery down while the lorry is on its way: it takes the crates to the grocer instead, and finishes ({redirected}; grocer {w.People.StockAt(gi):0.0} crates)");
            UnityEngine.Object.DestroyImmediate(w2.gameObject);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }
    }
}
