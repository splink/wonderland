using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        private static readonly int[] ZooParts = { Catalog.ZooGate, Catalog.ZooPlaza, Catalog.Savanna, Catalog.ElephantHouse, Catalog.LionRock, Catalog.PenguinPool, Catalog.MonkeyIsland, Catalog.Aviary, Catalog.TropicalHouse, Catalog.PettingZoo, Catalog.ZooKiosk, Catalog.SafariCafe, Catalog.ZooGarden };

        /// <summary>A zoo laid out from its pieces round a plaza, homes for visitors, and a street (the journey and the renders).</summary>
        private static WonderlandWorld BuildZoo(float x, out int gate)
        {
            var w = new GameObject("Zoo").AddComponent<WonderlandWorld>();
            w.transform.position = new Vector3(x, 0f, 0f);
            w.Create(2.6f, Materials(), 7, true, endless: true);
            var t = w.Terrain;
            w.BuildPath(Line(t, new Vector2(-1.1f, -0.62f), new Vector2(1.1f, -0.62f)), PathKind.Road);
            foreach (var (id, px, pz, rot) in new[]
            {
                (Catalog.ZooGate, 0f, -0.48f, 0f), (Catalog.ZooPlaza, 0f, -0.3f, 0f),
                (Catalog.Savanna, -0.28f, -0.2f, 90f), (Catalog.LionRock, 0.26f, -0.22f, -90f), (Catalog.PenguinPool, -0.22f, 0.12f, 120f),
                (Catalog.ElephantHouse, 0.02f, 0.2f, 0f), (Catalog.MonkeyIsland, 0.27f, 0.12f, -120f), (Catalog.Aviary, -0.47f, 0.06f, 90f),
                (Catalog.TropicalHouse, 0.5f, -0.05f, -90f), (Catalog.PettingZoo, -0.46f, -0.3f, 90f), (Catalog.ZooKiosk, 0.14f, -0.38f, 0f),
                (Catalog.SafariCafe, -0.16f, -0.42f, 0f), (Catalog.ZooGarden, 0.09f, -0.2f, 0f), (Catalog.ZooGarden, -0.09f, -0.18f, 0f),
            })
            {
                string e = w.PlaceHouse(new HouseInstance { x = px, z = pz, rotation = rot, variant = id });
                if (e != null) Debug.Log($"Zoo: {Catalog.Get(id).Name} not placed: {e}");
            }
            // Footpaths from the gate round the enclosures.
            w.BuildPath(Line(t, new Vector2(0f, -0.56f), new Vector2(0f, -0.38f)), PathKind.Footpath);
            w.BuildPath(Line(t, new Vector2(-0.07f, -0.27f), new Vector2(-0.12f, -0.05f), new Vector2(0f, 0.04f), new Vector2(0.12f, -0.05f), new Vector2(0.07f, -0.27f)), PathKind.Footpath);
            // The village the visitors come from.
            for (int k = 0; k < 6; k++) w.PlaceHouse(new HouseInstance { x = -0.9f + k * 0.17f, z = -0.8f, rotation = 0f, variant = k % 2 == 0 ? Catalog.Cottage : Catalog.Townhouse });
            for (int k = 0; k < 18; k++) w.PlacePerson(t.PointAt(-0.9f + (k % 9) * 0.1f, -0.7f - (k / 9) * 0.04f), k % PersonFactory.StyleCount, 7300 + k);
            w.People.Grid.Complete();
            gate = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.ZooGate);
            return w;
        }

        /// <summary>
        /// The zoo, piece by piece: every piece builds at all five levels; placed together behind an entrance they make a
        /// zoo that opens with the news; the animals live in their pens, sized to the villagers; on a zoo day visitors
        /// come through the gate, go round several enclosures and end at the kiosk or café; it all saves and loads.
        /// </summary>
        private static void ZooJourney()
        {
            int levelsOk = 0;
            foreach (int id in ZooParts)
            {
                bool ok = true;
                for (int d = MeshBuilder.Extreme; d <= MeshBuilder.Minimal; d++) { var m = HouseFactory.Get(id, 0, d); ok &= m != null && m.vertexCount > 20; }
                if (ok) levelsOk++;
            }
            Check(levelsOk == ZooParts.Length && Catalog.InCategory(PlaceCategory.Zoo).Count >= 14,
                $"The zoo is a family of pieces, each built at all five levels ({levelsOk}/{ZooParts.Length}; {Catalog.InCategory(PlaceCategory.Zoo).Count} in the Zoo category)");
            var w = BuildZoo(1500f, out int gate);
            int placed = 0; foreach (int id in ZooParts) if (w.Buildings.Houses.Exists(h => h.variant == id)) placed++;
            Check(placed == ZooParts.Length && gate >= 0, $"A zoo is laid out from its pieces round a plaza ({placed}/{ZooParts.Length} placed)");
            w.Clock.Hour = 9.5f;
            string news = "";
            for (int s = 0; s < 72 * 2; s++) { w.Clock.Tick(Dt); w.Attractions.Step(Dt); w.People.Step(Dt); if (w.People.News != null && w.People.News.Contains("zoo")) news = w.People.News; }
            Check(w.Attractions.ZoosOpened == 1 && w.Attractions.ZooOpen(gate) && news.Contains("opens"), $"An entrance with enclosures behind it is a zoo: it opens with the news (\"{news}\")");
            int savanna = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Savanna), aviary = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Aviary);
            int animals = 0; foreach (int id in ZooParts) { int i = w.Buildings.Houses.FindIndex(h => h.variant == id); if (i >= 0) animals += w.Attractions.AnimalsAt(i); }
            Check(animals >= 30 && w.Attractions.ZooFor(savanna).Contains("giraffes"), $"The animals live in their enclosures ({animals}: {w.Attractions.ZooFor(savanna)})");
            // Sized to the villagers: a giraffe stands well over twice a villager's height; parrots fly up in the aviary.
            var giraffeTop = 0f; var probe = GameObject.Find("Zoo animals");
            foreach (var r in w.Attractions.GetComponentsInChildren<MeshRenderer>()) if (r.name == "giraffes") giraffeTop = Mathf.Max(giraffeTop, r.bounds.size.y / w.transform.lossyScale.y);
            var bird = w.Attractions.AnimalPosition(aviary, 0);
            float birdHeight = bird.HasValue ? bird.Value.y - w.Terrain.SurfaceAt(bird.Value.x, bird.Value.z) : 0f;
            Check(giraffeTop > PersonFactory.Height * 2.5f && giraffeTop < PersonFactory.Height * 3.5f && birdHeight > 0.025f, $"Sized to the villagers: a giraffe stands {giraffeTop * 100f:0.0} cm, a villager {PersonFactory.Height * 100f:0.0} cm; parrots fly {birdHeight * 100f:0.0} cm up in the aviary");
            // A zoo day: visitors come through the gate, see the animals one after another, and end with a snack.
            var seenAt = new HashSet<int>(); int touring = 0, atGate = 0, snack = 0; float inPens = 0f;
            int kiosk = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.ZooKiosk), cafe = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.SafariCafe);
            for (int s = 0; s < 72 * 360; s++)
            {
                w.Clock.Tick(Dt); w.Attractions.Step(Dt); w.People.Step(Dt);
                if (s % 72 != 0) continue;
                touring = Mathf.Max(touring, w.Attractions.ZooVisitors);
                atGate += w.People.VisitorsAt(gate);
                snack += (kiosk >= 0 ? w.People.VisitorsAt(kiosk) : 0) + (cafe >= 0 ? w.People.VisitorsAt(cafe) : 0);
                foreach (int id in new[] { Catalog.Savanna, Catalog.LionRock, Catalog.PenguinPool, Catalog.ElephantHouse, Catalog.MonkeyIsland, Catalog.Aviary, Catalog.TropicalHouse, Catalog.PettingZoo })
                {
                    int i = w.Buildings.Houses.FindIndex(h => h.variant == id);
                    if (i >= 0 && w.People.VisitorsAt(i) > 0) seenAt.Add(id);
                }
                // The animals stay in their pens.
                for (int k = 0; k < 5; k++)
                {
                    var p = w.Attractions.AnimalPosition(savanna, k);
                    if (!p.HasValue) continue;
                    var h = w.Buildings.Houses[savanna];
                    inPens = Mathf.Max(inPens, (new Vector2(p.Value.x, p.Value.z) - new Vector2(h.x, h.z)).magnitude);
                }
            }
            Check(touring >= 3 && atGate > 0 && seenAt.Count >= 4 && snack > 0,
                $"On a zoo day visitors come through the gate ({atGate} ticket stops), go round the animals ({seenAt.Count} enclosures visited, up to {touring} on their round) and end with a snack ({snack})");
            Check(inPens < 0.16f, $"...and the animals keep to their enclosures (at most {inPens * 100f:0} cm from the savanna's middle)");
            // Saved and loaded: still a zoo.
            var copy = new GameObject("Zoo copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(1540f, 0f, 0f);
            copy.Deserialize(w.Serialize(), Materials());
            copy.Attractions.Step(1.1f);
            int copyGate = copy.Buildings.Houses.FindIndex(h => h.variant == Catalog.ZooGate);
            Check(copy.Attractions.ZooOpen(copyGate), "Saved and loaded, the zoo is still a zoo");
            Object.DestroyImmediate(copy.gameObject);
            Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>
        /// One scale for everything, with a villager as the measure: a car is about two villagers long, a bus four, a
        /// railway coach five; a doorway is taller than a villager; a giraffe nearly three villagers tall.
        /// </summary>
        private static void ProportionsJourney()
        {
            float h = PersonFactory.Height;
            float car = VehicleFactory.Length(VehicleKind.Car) / h, bus = VehicleFactory.Length(VehicleKind.Bus) / h, coach = (TrainFactory.Coach.Front - TrainFactory.Coach.Rear) / h;
            Check(car > 1.8f && car < 3f && bus > 3.5f && bus < 7f && coach > 4f, $"One scale, a villager as the measure ({h * 100f:0.0} cm): a car {car:0.0} villagers long, a bus {bus:0.0}, a coach {coach:0.0}");
            // Doorways: every building with a front door has one at least a villager's height.
            int ok = 0, all = 0; string low = "";
            foreach (var bp in Catalog.All)
            {
                float door = HouseFactory.DoorHeightOf(bp.Id);
                if (door <= 0f) continue;
                all++;
                if (door >= h) ok++; else low += $" {bp.Name} {door * 100f:0.0} cm";
            }
            Check(all > 10 && ok == all, $"A villager fits through every front door ({ok}/{all} doors at least {h * 100f:0.0} cm high{low})");
        }

        /// <summary>Renders of the zoo: the whole of it from above the entrance, the gate, and the enclosures close up.</summary>
        public static void ZooRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(8f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.2f, -1.6f), Vector3.zero, 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = BuildZoo(0f, out _);
            w.Sky.Shown = false;
            w.Clock.Hour = 10f;
            for (int s = 0; s < 72 * 120; s++) { w.Clock.Tick(Dt); w.Attractions.Step(Dt); w.People.Step(Dt); }
            w.Forest.RebuildDirty(); w.Cover.RebuildDirty();
            w.People.SendMessage("LateUpdate");
            var c = w.transform.position;
            cam.transform.position = c + new Vector3(0.05f, 0.95f, -1.35f); cam.transform.LookAt(c + new Vector3(0f, 0f, -0.1f)); cam.fieldOfView = 42f;
            WonderlandRenders.Capture(cam, "editor-zoo.png", 1920, 1200);
            cam.transform.position = c + new Vector3(0.12f, 0.12f, -0.8f); cam.transform.LookAt(c + new Vector3(0f, 0.05f, -0.48f)); cam.fieldOfView = 40f;
            WonderlandRenders.Capture(cam, "editor-zoo-gate.png", 1600, 1000);
            foreach (var (name, id, from) in new[] { ("savanna", Catalog.Savanna, new Vector3(0.25f, 0.18f, 0f)), ("elephants", Catalog.ElephantHouse, new Vector3(0f, 0.16f, -0.28f)), ("penguins", Catalog.PenguinPool, new Vector3(0.15f, 0.14f, -0.15f)), ("aviary", Catalog.Aviary, new Vector3(0.2f, 0.12f, -0.12f)) })
            {
                var h = w.Buildings.Houses.Find(x => x.variant == id);
                var at = c + new Vector3(h.x, 0.02f, h.z);
                cam.transform.position = at + from; cam.transform.LookAt(at); cam.fieldOfView = 40f;
                WonderlandRenders.Capture(cam, $"editor-zoo-{name}.png", 1600, 1000);
            }
            Object.DestroyImmediate(w.gameObject);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
