using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// Weather, water and watching: a shower greys the clouds, rain falls over the land, it can be heard, and nobody
        /// picks the park while it pours; a stream drawn between two ponds runs downhill at the ponds' own levels and
        /// doesn't dam them; every train carries a camera that can be watched.
        /// </summary>
        private static void WeatherJourney()
        {
            var w = Scratch("Weather", 440f);
            var t = w.Terrain;

            // A shower.
            w.Sky.Shown = true;
            w.Sky.ToggleRain();
            for (int i = 0; i < 400; i++) w.Sky.Step(Dt);
            for (int i = 0; i < 60; i++) { w.Sky.Step(Dt); foreach (var ps in w.Sky.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(Dt, true, false); }
            Check(w.Sky.Rain > 0.95f && w.Sky.RainFalling && w.Sky.Raining, $"A shower: rain falls from the clouds over the land ({w.Sky.Raindrops} drops)");
            Check(w.People.Rain > 0.95f && PeopleSystem.OpenAir(Catalog.Park) && !PeopleSystem.OpenAir(Catalog.Museum), "Villagers know it is raining: the park is out, the museum is in");
            var a = w.Sky.PositionOf(1); for (int i = 0; i < 300; i++) w.Sky.Step(Dt);
            Check((w.Sky.PositionOf(1) - a).magnitude > 0.05f && SkySystem.Count >= 12, $"{SkySystem.Count} clouds drift on the wind");
            w.Sky.ToggleRain();
            for (int i = 0; i < 400; i++) w.Sky.Step(Dt);
            Check(w.Sky.Rain == 0f && !w.Sky.RainFalling, "The rain stops when the shower passes");
            Check(SoundBank.Build(SoundLoop.Rain).Length > SoundBank.Rate, "Rain can be heard");

            // Two ponds, one higher than the other, joined by a stream.
            var high = new Vector3(-0.45f, 0f, 0.1f); var low = new Vector3(0.45f, 0f, 0.1f);
            Hold(() => w.TerrainStroke(TerrainBrush.Raise, high, 0.25f, 0.6f, Dt, 0f), 1.2f);
            float hl = w.WaterLevelForStroke(high); Hold(() => w.WaterStroke(high, 0.12f, 0.9f, Dt, hl, false), 1.5f);
            float ll = w.WaterLevelForStroke(low); Hold(() => w.WaterStroke(low, 0.12f, 0.9f, Dt, ll, false), 1.5f);
            hl = t.WaterLevelAt(high.x, high.z); ll = t.WaterLevelAt(low.x, low.z);
            float bedHigh = t.SurfaceAt(high.x, high.z);
            // Drawn from the low pond up to the high one: it still flows down.
            string err = w.BuildStream(Stroke(t, new Vector2(low.x - 0.02f, low.z), new Vector2(0f, 0.16f), new Vector2(high.x + 0.02f, high.z)));
            var s = w.Streams.Streams.Count > 0 ? w.Streams.Streams[^1] : null;
            Check(err == null && s != null, "A stream joins two ponds" + (err == null ? "" : $" (\"{err}\")"));
            if (s != null)
            {
                bool downhill = true;
                for (int i = 1; i < s.points.Count; i++) if (s.points[i].y > s.points[i - 1].y + 1e-5f) downhill = false;
                Check(downhill && s.points[0].y > s.points[^1].y, $"It flows from the higher pond down to the lower, whichever way it was drawn ({s.points[0].y * 100f:0.0} -> {s.points[^1].y * 100f:0.0} cm)");
                Check(Mathf.Abs(s.points[0].y - hl) < 0.003f && Mathf.Abs(s.points[^1].y - ll) < 0.003f,
                    $"It leaves and enters at the ponds' own surfaces (high {hl * 100f:0.0}/{s.points[0].y * 100f:0.0} cm, low {ll * 100f:0.0}/{s.points[^1].y * 100f:0.0} cm), not their beds");
                Check(Mathf.Abs(t.SurfaceAt(high.x, high.z) - bedHigh) < 0.001f && t.WaterAt(high.x, high.z) > 0.9f, "The ponds are left as they were (no dam, no ditch across them)");
            }

            // Train cameras.
            w.BuildTrack(Loop(t, 0.42f, 0.32f, 0.97f, false));
            int before = w.Cctv.Feeds;
            w.PlaceTrain(t.PointAt(0f, -0.32f), TrainKind.Passenger, Vector3.left, out var train);
            Check(train != null && w.Cctv.Feeds == before + 1, $"Every train carries a camera to watch ({w.Cctv.Feeds} feeds)");
            if (train != null)
            {
                w.Cctv.Selected = w.Cctv.Feeds - 1;
                Check(w.Cctv.NameOf(w.Cctv.Selected).StartsWith("Train"), $"Watched from the front of the engine: \"{w.Cctv.NameOf(w.Cctv.Selected)}\"");
            }
            Object.DestroyImmediate(w.gameObject);
            BoatJourney();
        }

        /// <summary>
        /// Boats and a landing stage: the stage only goes at a pond's edge; boats only on water, and they keep to it;
        /// villagers who come for a boat trip wait on the stage, are taken aboard, go round the lake and step off again.
        /// </summary>
        private static void BoatJourney()
        {
            var w = Scratch("Boats", 470f);
            var t = w.Terrain;
            var pond = new Vector3(0.15f, 0f, 0.05f);
            float level = w.WaterLevelForStroke(pond);
            Hold(() => w.WaterStroke(pond, 0.2f, 0.9f, Dt, level, false), 2f);
            Hold(() => w.WaterStroke(pond + new Vector3(0.1f, 0f, 0.05f), 0.18f, 0.9f, Dt, level, false), 1.5f);
            Check(t.WaterAt(pond.x, pond.z) > 0.9f, "A pond to sail on");
            Check(w.ValidateHouse(new HouseInstance { x = -0.5f, z = -0.4f, variant = Catalog.Jetty }) != null, "A landing stage can't go inland");
            // Round the shore, facing the water, until one fits.
            HouseInstance jetty = default; string why = "nowhere fitted";
            for (int k = 0; k < 36 && why != null; k++)
            {
                float a = k * 10f * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (float r = 0.1f; r < 0.45f && why != null; r += 0.01f)
                {
                    var at = new Vector2(pond.x, pond.z) + dir * r;
                    float rot = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;   // its front (-Z) towards the middle of the pond
                    jetty = new HouseInstance { x = at.x, z = at.y, rotation = rot, variant = Catalog.Jetty };
                    why = w.PlaceHouse(jetty);
                }
            }
            Check(why == null, "A landing stage goes at the pond's edge, reaching out over the water" + (why == null ? "" : $" (\"{why}\")"));
            Check(w.ValidateHouse(new HouseInstance { x = -0.5f, z = -0.4f, variant = Catalog.Steamer }) == "Boats go on a pond", "Boats can't be put on land");
            string steamer = w.PlaceHouse(new HouseInstance { x = pond.x + 0.06f, z = pond.z + 0.04f, rotation = 0f, variant = Catalog.Steamer });
            string rowing = w.PlaceHouse(new HouseInstance { x = pond.x - 0.06f, z = pond.z - 0.06f, rotation = 90f, variant = Catalog.RowingBoat });
            string sailing = w.PlaceHouse(new HouseInstance { x = pond.x + 0.1f, z = pond.z - 0.08f, rotation = 45f, variant = Catalog.SailingBoat });
            Check(steamer == null && rowing == null && sailing == null && w.Boats.Boats.Count == 3, $"A paddle steamer, a rowing boat and a sailing boat on the pond ({steamer ?? rowing ?? sailing ?? "all afloat"})");
            int stage = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Jetty);
            // Villagers living nearby, sent on a boat trip.
            var land = t.LandBounds();
            for (int k = 0; k < 4; k++) w.PlacePerson(t.PointAt(-0.45f + k * 0.04f, -0.35f), k, 5200 + k);
            w.People.Grid.Complete();
            w.Clock.Hour = 11f;
            w.People.Step(Dt);
            int sent = 0;
            for (int k = 0; k < w.People.People.Count; k++)
                if (stage >= 0 && w.People.PlanVisit(k, stage, Need.Fun, out var plan) && w.People.Direct(k, plan)) sent++;
            var start = new System.Collections.Generic.List<Vector2>();
            foreach (var b in w.Boats.Boats) start.Add(b.Pos);
            bool offWater = false, boarded = false, landedAfter = false, waited = false;
            string trip = null;
            for (float s = 0f; s < 400f; s += Dt)
            {
                w.Clock.Tick(Dt); w.People.Step(Dt); w.Boats.Step(Dt); w.Sky.Step(Dt);
                foreach (var b in w.Boats.Boats) if (t.WaterAt(b.Pos.x, b.Pos.y) < 0.5f) offWater = true;
                bool aboardNow = false;
                for (int k = 0; k < w.People.People.Count; k++)
                {
                    string d = w.People.Describe(k);
                    if (d.Contains("waiting for a boat")) waited = true;
                    if (d.Contains("boat trip")) { aboardNow = true; boarded = true; }
                }
                for (int i = 0; i < w.Boats.Boats.Count; i++) if (w.Boats.Describe(i).Contains("trip round the lake")) trip ??= w.Boats.Describe(i);
                if (boarded && !aboardNow) { landedAfter = true; break; }
            }
            float moved = 0f; for (int i = 0; i < start.Count; i++) moved = Mathf.Max(moved, (w.Boats.Boats[i].Pos - start[i]).magnitude);
            Check(moved > 0.05f && !offWater, $"Boats move about the pond and never leave the water (furthest {moved * 100f:0} cm)");
            Check(sent > 0 && waited, $"Villagers who want a boat trip wait on the landing stage ({sent} sent)");
            Check(boarded && trip != null, $"A boat comes alongside and takes them round the lake (\"{trip}\")");
            Check(landedAfter, "...and brings them back, and they step off and go on with their day");
            var saved = w.Serialize();
            var copy = new GameObject("Boats copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(480f, 0f, 0f);
            copy.Deserialize(saved, Materials());
            Check(copy.Boats.Boats.Count == 3, $"Boats are saved with the world ({copy.Boats.Boats.Count})");
            Object.DestroyImmediate(copy.gameObject);
            int before = w.Boats.Boats.Count;
            var bp = w.Boats.Boats[0].Pos;
            w.EraseAt(new Vector3(bp.x, 1f, bp.y), Vector3.down, new Vector3(bp.x, w.Terrain.WaterLevelAt(bp.x, bp.y), bp.y));
            bool erased = w.Boats.Boats.Count == before - 1;
            w.Undo();
            Check(erased && w.Boats.Boats.Count == before, "The eraser takes a boat away, and Undo brings it back");
            Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Editor renders: two ponds joined by a stream in a shower, and the view from a train's camera.</summary>
        private static void WeatherRenders(Camera cam)
        {
            var w = Scratch("Weather demo", 450f);
            var t = w.Terrain;
            var high = new Vector3(-0.4f, 0f, 0.05f); var low = new Vector3(0.4f, 0f, 0.1f);
            Hold(() => w.TerrainStroke(TerrainBrush.Raise, high, 0.3f, 0.6f, Dt, 0f), 1.2f);
            float hl = w.WaterLevelForStroke(high); Hold(() => w.WaterStroke(high, 0.13f, 0.9f, Dt, hl, false), 1.5f);
            float ll = w.WaterLevelForStroke(low); Hold(() => w.WaterStroke(low, 0.14f, 0.9f, Dt, ll, false), 1.5f);
            w.BuildStream(Stroke(t, new Vector2(high.x + 0.05f, high.z), new Vector2(-0.1f, -0.05f), new Vector2(0.15f, 0.2f), new Vector2(low.x - 0.05f, low.z)));
            for (int i = 0; i < 8; i++) w.PaintForest(new Vector3(-0.6f + i * 0.15f, 0f, -0.45f + Mathf.Sin(i) * 0.05f), 0.08f, 0.8f, Dt * 4f);
            w.BuildTrack(Loop(t, 0.7f, 0.55f, 0.97f, false));
            w.PlaceTrain(t.PointAt(0f, -0.55f), TrainKind.Passenger, Vector3.left, out var train);
            Light key = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) key = l;
            w.Clock.Hour = 11f;
            w.Sky.Shown = true; w.Sky.ToggleRain();
            for (int i = 0; i < 500; i++) { w.Sky.Step(Dt); w.Trains.Step(Dt); }
            foreach (var ps in w.Sky.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(2f, true, true);
            WonderlandLighting.SetTimeOfDay(key, w.Clock.Hour, w.Clock.Daylight, 0f, w.Sky.Rain);
            w.Cover.RebuildDirty(); w.Forest.RebuildDirty();
            var c = w.transform.position;
            cam.transform.position = c + new Vector3(0.1f, 1.6f, -2.1f); cam.transform.LookAt(c + new Vector3(0f, 0.3f, 0f)); cam.fieldOfView = 46f;
            WonderlandRenders.Capture(cam, "editor-weather-shower.png", 1920, 1200);
            cam.transform.position = c + new Vector3(0.05f, 0.35f, -0.75f); cam.transform.LookAt(c + new Vector3(0f, 0.02f, 0.08f)); cam.fieldOfView = 42f;
            WonderlandRenders.Capture(cam, "editor-weather-stream.png", 1920, 1200);
            if (train != null)
            {
                w.Cctv.Watching = true;
                w.Cctv.Selected = w.Cctv.Feeds - 1;
                w.Cctv.Step(Dt);
                w.Cctv.ViewOf(w.Cctv.Selected, out var eye, out var look);
                cam.transform.SetPositionAndRotation(eye, look); cam.fieldOfView = 62f;
                float near = cam.nearClipPlane; cam.nearClipPlane = 0.003f;
                WonderlandRenders.Capture(cam, "editor-train-camera.png", 1600, 1200);
                cam.nearClipPlane = near;
            }
            WonderlandLighting.SetTimeOfDay(key, 13f, 1f, 0f);
            WonderlandLighting.Configure(key);
            Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Just the pond renders (boats, the landing stage, umbrellas in a shower): quick to iterate on.</summary>
        public static void PondRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), new Vector3(0f, 0.05f, -0.05f), 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            BoatRenders(cam);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Renders of the pond: a landing stage with the steamer alongside, a sailing boat and a rowing boat; villagers under umbrellas in a shower.</summary>
        private static void BoatRenders(Camera cam)
        {
            var w = Scratch("Pond demo", 490f);
            var t = w.Terrain;
            var pond = new Vector3(0.1f, 0f, 0.1f);
            float level = w.WaterLevelForStroke(pond);
            Hold(() => w.WaterStroke(pond, 0.22f, 0.9f, Dt, level, false), 2f);
            Hold(() => w.WaterStroke(pond + new Vector3(0.14f, 0f, 0.06f), 0.2f, 0.9f, Dt, level, false), 1.5f);
            HouseInstance jetty = default; string why = "none";
            for (int k = 0; k < 36 && why != null; k++)
            {
                float a = (200f + k * 10f) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (float r = 0.1f; r < 0.5f && why != null; r += 0.01f)
                {
                    var at = new Vector2(pond.x, pond.z) + dir * r;
                    jetty = new HouseInstance { x = at.x, z = at.y, rotation = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, variant = Catalog.Jetty };
                    why = w.PlaceHouse(jetty);
                }
            }
            var dock = BoatSystem.DockOf(jetty);
            w.PlaceHouse(new HouseInstance { x = dock.x, z = dock.y, rotation = jetty.rotation + 90f, variant = Catalog.Steamer });
            w.PlaceHouse(new HouseInstance { x = pond.x + 0.12f, z = pond.z + 0.06f, rotation = 30f, variant = Catalog.SailingBoat });
            w.PlaceHouse(new HouseInstance { x = pond.x + 0.02f, z = pond.z + 0.12f, rotation = 120f, variant = Catalog.RowingBoat });
            foreach (var (id, x, z) in new[] { (Catalog.Cottage, -0.45f, -0.3f), (Catalog.Cafe, -0.2f, -0.42f), (Catalog.Townhouse, 0.3f, -0.4f) })
                w.PlaceHouse(new HouseInstance { x = x, z = z, variant = id });
            for (int i = 0; i < 6; i++) w.PaintForest(new Vector3(0.55f + Mathf.Sin(i) * 0.06f, 0f, 0.35f + i * 0.04f), 0.08f, 0.8f, Dt * 4f);
            for (int k = 0; k < 10; k++) w.PlacePerson(t.PointAt(-0.3f + k * 0.05f, -0.22f), k % PersonFactory.StyleCount, 6100 + k);
            w.People.Grid.Complete();
            w.Clock.Hour = 11f;
            int stage = w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Jetty);
            for (int s = 0; s < 72 * 4; s++) { w.Clock.Tick(Dt); w.People.Step(Dt); }
            for (int k = 0; k < 3; k++) if (stage >= 0 && w.People.PlanVisit(k, stage, Need.Fun, out var plan)) w.People.Direct(k, plan);
            for (int s = 0; s < 72 * 30; s++) { w.Clock.Tick(Dt); w.People.Step(Dt); w.Boats.Step(Dt); w.Sky.Step(Dt); }
            w.Sky.Shown = true; w.Sky.StartShower(2f);
            for (int s = 0; s < 72 * 8; s++) { w.Clock.Tick(Dt); w.People.Step(Dt); w.Boats.Step(Dt); w.Sky.Step(Dt); }
            Light key = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) key = l;
            WonderlandLighting.SetTimeOfDay(key, w.Clock.Hour, w.Clock.Daylight, 0f, w.Sky.Rain);
            foreach (var ps in w.Sky.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(2f, true, true);
            w.Cover.RebuildDirty(); w.Forest.RebuildDirty();
            var c = w.transform.position;
            var j = c + new Vector3(jetty.x, 0f, jetty.z);
            cam.transform.position = j + new Vector3(-0.25f, 0.3f, -0.45f); cam.transform.LookAt(j + new Vector3(0.05f, 0.0f, 0.08f)); cam.fieldOfView = 40f;
            WonderlandRenders.Capture(cam, "editor-pond-jetty.png", 1920, 1200);
            cam.transform.position = c + new Vector3(0.1f, 0.6f, -0.85f); cam.transform.LookAt(c + new Vector3(0.1f, 0f, 0.05f)); cam.fieldOfView = 42f;
            WonderlandRenders.Capture(cam, "editor-pond-boats.png", 1920, 1200);
            // Close up: the queue on the landing stage under their umbrellas.
            var land = j + Quaternion.Euler(0f, jetty.rotation, 0f) * new Vector3(0f, 0f, 0.04f);
            land.y = t.SurfaceAt(land.x - c.x, land.z - c.z) + c.y;
            var back = Quaternion.Euler(0f, jetty.rotation, 0f) * new Vector3(0.12f, 0.07f, -0.1f);
            cam.transform.position = land + back; cam.transform.LookAt(land + Vector3.up * 0.03f); cam.fieldOfView = 40f;
            WonderlandRenders.Capture(cam, "editor-umbrellas.png", 1600, 1200);
            int umbrellas = 0; foreach (Transform tr in w.People.GetComponentsInChildren<Transform>()) if (tr.name == "Umbrella" && tr.gameObject.activeInHierarchy) umbrellas++;
            var states = new System.Text.StringBuilder();
            for (int k = 0; k < w.People.People.Count; k++) states.Append($" {w.People.StateOf(k)}");
            Debug.Log($"Pond renders: jetty {why ?? "placed"}; {w.Boats.Describe(0)}; {w.People.Census()}; rain {w.Sky.Rain:0.00} raining {w.Sky.Raining} people.rain {w.People.Rain:0.00}; {umbrellas} umbrellas up;{states}");
            WonderlandLighting.SetTimeOfDay(key, 13f, 1f, 0f);
            WonderlandLighting.Configure(key);
            Object.DestroyImmediate(w.gameObject);
        }
    }
}
