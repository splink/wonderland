using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// A harbour lake with a harbour at each end and a railway loop crossing it twice in between, both crossings made
        /// lift bridges by tapping them; a passenger train on the loop. Null if any of it couldn't be built (and why).
        /// </summary>
        private static WonderlandWorld LiftScene(float x, out string why)
        {
            var w = Scratch("Lift bridge", x);
            Lake(w, -0.6f, 0.6f, 0.22f);
            var t = w.Terrain;
            var a = w.ShoreSnap(t.PointAt(-0.48f, 0.24f), Catalog.Harbour, 0);
            var b = w.ShoreSnap(t.PointAt(0.48f, -0.27f), Catalog.Harbour, 0);
            string ea = a.HasValue ? w.PlaceHouse(a.Value) : "no spot", eb = b.HasValue ? w.PlaceHouse(b.Value) : "no spot";
            var plan = w.BuildTrack(Loop(t, 0.2f, 0.42f, 0.97f, false));
            w.RefreshBridges();
            // Tap each crossing until it is a lift bridge.
            var names = new List<string>();
            foreach (var at in w.Bridges.Bridges.ConvertAll(br => br.Anchor))
            {
                string name = null;
                for (int k = 0; k < 8 && name != "Lift bridge"; k++) name = w.CycleBridge(new Vector3(at.x, 0f, at.y));
                names.Add(name);
            }
            // The train on the loop's northern bend, on dry land.
            var north = new Vector3(0f, 0f, float.MinValue);
            foreach (var e in w.Track.Network.Edges) foreach (var q in e.Layout.Points) if (q.z > north.z) north = q;
            string placed = w.PlaceTrain(north, TrainKind.Passenger, Vector3.right, out _);
            w.People.Grid.Complete(); w.Harbours.Step(Dt); w.Harbours.Step(2.1f);
            why = ea ?? eb ?? (plan.Ok ? null : "track: " + plan.Error) ?? (placed != null ? "train: " + placed : null);
            if (why == null && w.Bridges.Lifts.Count != 2) why = $"{w.Bridges.Lifts.Count} lift bridges ({string.Join(", ", names)})";
            return w;
        }

        /// <summary>
        /// Lift bridges: with a railway across the harbour lake made of lift bridges, a container ship sails between the
        /// harbours under them. Each span goes up for the ship once no train is on it, the train waiting on the bank
        /// meanwhile; the ship never passes under a span that is not up, no train is ever on a span that is; the spans come
        /// down again and the train carries on. The track on a span rides up with it.
        /// </summary>
        private static void LiftBridges()
        {
            var w = LiftScene(360f, out string why);
            Check(why == null && w.Harbours.ShipCount >= 1, $"A railway across a harbour lake on two lift bridges: the ships still have their way ({w.Harbours.ShipCount} ships{(why != null ? "; " + why : "")})");
            if (why != null || w.Harbours.ShipCount == 0 || w.Trains.Trains.Count == 0) { Object.DestroyImmediate(w.gameObject); return; }
            var train = w.Trains.Trains[0];
            var ship = w.Harbours.transform.Find("Container ship");
            int voyages = w.Harbours.Voyages;
            float highest = 0f, odometer = train.Odometer, afterUp = 0f;
            bool clash = false, squeezed = false, waited = false, shipWaited = false;
            string first = "";
            var last = ship != null ? ship.localPosition : Vector3.zero;
            for (float s = 0f; s < 420f; s += Dt)
            {
                w.Trains.Step(Dt); w.Harbours.Step(Dt); w.Bridges.Step(Dt);
                foreach (var lift in w.Bridges.Lifts)
                {
                    highest = Mathf.Max(highest, lift.Height);
                    if (lift.Height > 0.001f) foreach (var (edge, lo, hi) in lift.Track) clash |= w.Trains.Holds(edge, lo, hi);
                }
                if (highest >= BridgeSystem.LiftRise - 1e-4f) afterUp += train.Speed * Dt;
                if (ship != null)
                {
                    var f = ship.localRotation * Vector3.forward;
                    foreach (float d in new[] { -0.06f, 0f, 0.06f })
                    {
                        var p = ship.localPosition + f * d;
                        float room = w.Bridges.Headroom(new Vector2(p.x, p.z), 0.01f);
                        if (room < HarbourSystem.AirDraft && !squeezed)
                            first = $" [at {s:0.0} s, {d:+0.00;-0.00} along the hull: {room * 100f:0.0} cm, open {w.Bridges.Headroom(new Vector2(p.x, p.z), 0.01f, true) * 100f:0.0} cm, spans {string.Join("/", w.Bridges.Lifts.ConvertAll(l => $"{l.Height * 100f:0.0}"))}, speed {(ship.localPosition - last).magnitude / Dt:0.000}]";
                        squeezed |= room < HarbourSystem.AirDraft;
                    }
                }
                if (ship != null) last = ship.localPosition;
                waited |= train.Blocked == BlockReason.Bridge;
                for (int k = 0; k < w.Buildings.Houses.Count; k++)
                    if (w.Buildings.Houses[k].variant == Catalog.Harbour && (w.Harbours.HarbourFor(k) ?? "").Contains("waiting for the lift bridge")) shipWaited = true;
            }
            Check(w.Harbours.Voyages >= voyages + 2 && highest >= BridgeSystem.LiftRise - 1e-4f && !squeezed,
                $"The ship sails between the harbours under the lift bridges, each span going up for it ({w.Harbours.Voyages - voyages} voyages, spans up {highest * 100f:0.0} cm; under a span not up: {squeezed}{first}; waited for one: {shipWaited})");
            Check(!clash && waited && afterUp > 0.3f && train.Odometer - odometer > 2f,
                $"...no train is ever on a span that is up: the train waits on the bank and carries on when it is down ({train.Odometer - odometer:0.0} m run, waited: {waited}, on a raised span: {clash})");
            Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Renders of a lift bridge (docs/renders/editor-lift-*.png): down with the train crossing, up with the ship under it.</summary>
        public static void LiftRenders()
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            WonderlandLighting.CreateKeyLight();
            System.IO.Directory.CreateDirectory(WonderlandRenders.Output);
            WonderlandRenders.CreateFloor(6f);
            var cam = WonderlandRenders.CreateCamera(new Vector3(0f, 1.25f, -1.75f), Vector3.zero, 42f);
            WonderlandRenders.Capture(cam, "warmup.png", 64, 64);
            System.IO.File.Delete(System.IO.Path.Combine(WonderlandRenders.Output, "warmup.png"));
            var w = LiftScene(0f, out string why);
            if (why != null) Debug.LogError("Lift scene: " + why);
            w.Sky.Shown = false;
            w.Clock.Hour = 11f;
            void Shot(string file, Vector3 from, Vector3 at, float fov) { cam.transform.position = from; cam.transform.LookAt(at); cam.fieldOfView = fov; WonderlandRenders.Capture(cam, file, 1600, 1000); }
            var lift = w.Bridges.Bridges.Find(b => b.Lift != null);
            var c = lift != null ? new Vector3(lift.Anchor.x, 0.02f, lift.Anchor.y) : Vector3.zero;
            Shot("editor-lift-down.png", c + new Vector3(0.3f, 0.16f, -0.32f), c, 40f);
            // Up: the first time a span is all the way up.
            for (float s = 0f; s < 300f && !w.Bridges.Lifts.Exists(l => l.Up); s += 0.05f) { w.Trains.Step(0.05f); w.Harbours.Step(0.05f); w.Bridges.Step(0.05f); }
            var up = w.Bridges.Bridges.Find(b => b.Lift != null && b.Lift.Up);
            if (up != null) c = new Vector3(up.Anchor.x, 0.02f, up.Anchor.y);
            Shot("editor-lift-up.png", c + new Vector3(0.3f, 0.16f, -0.32f), c + Vector3.up * 0.03f, 40f);
            for (float s = 0f; s < 6f; s += 0.05f) { w.Trains.Step(0.05f); w.Harbours.Step(0.05f); w.Bridges.Step(0.05f); }
            Shot("editor-lift-ship.png", c + new Vector3(-0.28f, 0.1f, 0.26f), c + Vector3.up * 0.03f, 42f);
            Shot("editor-lift-overview.png", new Vector3(0f, 1.0f, -0.9f), new Vector3(0f, 0f, 0f), 50f);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
