using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wonderland.Editor
{
    public static partial class WonderlandJourney
    {
        /// <summary>
        /// The livelier town: the new places (each with a mesh, spots and its own rhythm), traffic on the roads the
        /// player draws (staying on them, joining at junctions, stopping at a level crossing while a train passes, going
        /// home when a road is taken away), emergencies answered (or safely given up), dogs following their owners and cats
        /// keeping to their territory, children at school and on the pitch, a match with a ball, the balloon flying by day,
        /// clouds drifting, CCTV cameras that keep their aim through edits, saving and loading, the studio's screens, and
        /// sound that picks what is near and respects its setting. Editor timings for a busy town.
        /// </summary>
        private static void TownJourney()
        {
            // The catalogue: every new place has a mesh and fits its category.
            int built = 0;
            foreach (int id in new[] { Catalog.Park, Catalog.Museum, Catalog.Restaurant, Catalog.Disco, Catalog.Hospital, Catalog.Police, Catalog.Factory, Catalog.Football, Catalog.Studio, Catalog.Camera, Catalog.Balloon })
                if (HouseFactory.Get(id).vertexCount > 50) built++;
            int vehicles = 0;
            foreach (VehicleKind k in Enum.GetValues(typeof(VehicleKind))) if (VehicleFactory.Get(k).vertexCount > 50) vehicles++;
            int kinds = Enum.GetValues(typeof(VehicleKind)).Length;
            Check(built == 11 && vehicles == kinds && AnimalFactory.Body(AnimalKind.Dog).vertexCount > 50 && AnimalFactory.Body(AnimalKind.Cat).vertexCount > 50,
                $"New places ({built}/11), vehicles ({vehicles}/{kinds}), a dog and a cat are generated");
            Check(Catalog.Get(Catalog.Disco).OpenAt(23.5f) && Catalog.Get(Catalog.Disco).OpenAt(1f) && !Catalog.Get(Catalog.Disco).OpenAt(15f) && Needs.Appeal(Catalog.Get(Catalog.Restaurant), 20f) > Needs.Appeal(Catalog.Get(Catalog.Restaurant), 16f),
                "The disco opens at eight and dances past midnight; the restaurant is busiest at supper");
            var bank = 0;
            foreach (SoundLoop k in Enum.GetValues(typeof(SoundLoop))) { var d = SoundBank.Build(k); float peak = 0f; foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v)); if (d.Length > 1000 && peak > 0.02f && peak <= 1f) bank++; }
            foreach (SoundCue k in Enum.GetValues(typeof(SoundCue))) { var d = SoundBank.Build(k); float peak = 0f; foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v)); if (d.Length > 500 && peak > 0.02f && peak <= 1.2f) bank++; }
            Check(bank == Enum.GetValues(typeof(SoundLoop)).Length + Enum.GetValues(typeof(SoundCue)).Length, $"Every sound is synthesised, audible and unclipped ({bank} clips)");

            var w = Scratch("Town", 290f);
            var t = w.Terrain;
            string road1 = w.BuildPath(Stroke(t, new Vector2(-0.82f, 0f), new Vector2(0.82f, 0f)), PathKind.Road);
            string road2 = w.BuildPath(Stroke(t, new Vector2(0f, -0.7f), new Vector2(0f, 0.7f)), PathKind.Road);
            string lane = w.BuildPath(Stroke(t, new Vector2(-0.6f, 0.62f), new Vector2(-0.05f, 0.62f)), PathKind.Lane);
            var build = new List<(int id, float x, float z, float rot)>
            {
                (Catalog.Cottage, -0.66f, 0.1f, 0f), (Catalog.Townhouse, -0.45f, 0.1f, 0f), (Catalog.Restaurant, -0.2f, 0.14f, 0f), (Catalog.Museum, 0.2f, 0.13f, 0f),
                (Catalog.Apartments, 0.45f, 0.1f, 0f), (Catalog.Cottage, 0.68f, 0.1f, 0f),
                (Catalog.Hospital, -0.62f, -0.12f, 180f), (Catalog.Police, -0.4f, -0.1f, 180f), (Catalog.FireStation, -0.18f, -0.11f, 180f), (Catalog.Disco, 0.18f, -0.1f, 180f),
                (Catalog.Studio, 0.42f, -0.1f, 180f), (Catalog.Townhouse, 0.66f, -0.1f, 180f),
                (Catalog.Park, -0.42f, 0.4f, 0f), (Catalog.Football, 0.36f, 0.42f, 0f), (Catalog.School, -0.4f, -0.42f, 0f), (Catalog.Factory, 0.36f, -0.44f, 0f),
                (Catalog.Apartments, -0.12f, -0.4f, 90f), (Catalog.Thatched, 0.12f, 0.42f, 270f), (Catalog.Balloon, 0.72f, 0.45f, 0f), (Catalog.Bakery, 0.12f, -0.3f, 270f),
                (Catalog.Camera, 0.07f, 0.07f, 0f), (Catalog.Camera, 0.08f, 0.32f, 180f), (Catalog.Cottage, 0.62f, -0.56f, 0f),
            };
            int ok = 0;
            foreach (var (id, x, z, rot) in build)
            {
                string e = Put(w, id, x, z, rot);
                if (e == null) ok++; else Check(false, $"Place {Catalog.Get(id).Name} at {x:0.00},{z:0.00} (\"{e}\")");
            }
            var wall = w.BuildPath(Stroke(t, new Vector2(0.58f, -0.24f), new Vector2(0.8f, -0.24f)), PathKind.Wall);
            var people = w.People;
            var traffic = w.Traffic;
            people.Grid.Complete();
            people.Step(Dt);
            traffic.Refresh(true);
            int Find(int id) => w.Buildings.Houses.FindIndex(h => h.variant == id);
            Check(road1 == null && road2 == null && lane == null && wall == null && ok == build.Count, $"A town goes up along two crossing roads and a lane ({ok}/{build.Count} places, {people.People.Count} villagers)");

            // Traffic: a graph from the roads, vehicles from the households and the stations.
            var cottage = PeopleSystem.DoorStep(w.Buildings.Houses[Find(Catalog.Cottage)]);
            var studio = PeopleSystem.DoorStep(w.Buildings.Houses[Find(Catalog.Studio)]);
            Check(traffic.Nodes > 50 && traffic.Connected(cottage, studio, true) && traffic.Connected(cottage, PeopleSystem.DoorStep(w.Buildings.Houses[Find(Catalog.Restaurant)]), true),
                $"The roads make one network, joined where they cross ({traffic.Nodes} nodes)");
            int privateVehicles = traffic.Count(VehicleKind.Car) + traffic.Count(VehicleKind.Motorbike) + traffic.Count(VehicleKind.Bicycle);
            Check(privateVehicles >= 3 && traffic.Count(VehicleKind.Ambulance) == 1 && traffic.Count(VehicleKind.Police) == 1 && traffic.Count(VehicleKind.FireEngine) == 1,
                $"Homes by the road have vehicles ({traffic.Count(VehicleKind.Car)} cars, {traffic.Count(VehicleKind.Motorbike)} motorbikes, {traffic.Count(VehicleKind.Bicycle)} bicycles), and the stations theirs");

            // A pet each: a dog at the first cottage, a cat behind a townhouse by a wall.
            var dogHome = cottage + new Vector2(0.03f, 0f);   // (in the cottage: its first steps out cross the house's own ground)
            string dog = w.PlaceAnimal(new Vector3(dogHome.x, 0f, dogHome.y), AnimalKind.Dog, 501);
            var catHome = new Vector2(0.7f, -0.2f);
            string cat = w.PlaceAnimal(new Vector3(catHome.x, 0f, catHome.y), AnimalKind.Cat, 502);
            int dogIndex = w.Animals.Animals.Count - 2, catIndex = w.Animals.Animals.Count - 1;
            Check(dog == null && cat == null, $"A dog and a cat move in{(dog ?? cat ?? "")}");

            // Three more children, living by the pitch.
            for (int seed = 3000, added = 0; added < 3 && seed < 4000; seed++)
                if (PeopleSystem.ChildSeed(seed) && w.PlacePerson(t.PointAt(0.2f + added * 0.03f, 0.33f), added, seed) == null) added++;
            // A day in town, from seven in the morning to two at night.
            w.Clock.Hour = 7f;
            var listener = new GameObject("Ears").transform;
            listener.SetParent(w.transform, false);
            w.Sounds.Listener = listener;
            var keep = SoundSystem.Setting;
            SoundSystem.Setting = SoundSetting.On;
            string strayWhy = "", discoDayHours = ""; bool lastDogWalkable = true;
            float worstOff = 0f; bool inHouse = false, moved = false, followed = false, dogStrayed = false, catStrayed = false, perched = false, childAtSchool = false, aloft = false, nightFlight = false;
            int restaurantEvening = 0, restaurantAfternoon = 0, discoNight = 0, discoDay = 0, museum = 0, park = 0, kidsPlaying = 0, smoke = 0, fairHeard = 0, discoHeard = 0;
            int restaurant = Find(Catalog.Restaurant), disco = Find(Catalog.Disco), museumAt = Find(Catalog.Museum), parkAt = Find(Catalog.Park), pitch = Find(Catalog.Football), school = Find(Catalog.School), factory = Find(Catalog.Factory), balloon = Find(Catalog.Balloon);
            int startDay = w.Clock.Day;
            var cloud0 = w.Sky.PositionOf(0);
            while (w.Clock.Day == startDay || w.Clock.Hour < 2f)
            {
                Town(w, 0.5f, false);
                float hour = w.Clock.Hour;
                foreach (var v in traffic.Vehicles)
                {
                    if (v.State != TrafficSystem.Mode.Driving) continue;
                    moved = true;
                    worstOff = Mathf.Max(worstOff, traffic.OffRoad(v.Position));
                    if (people.InsideHouse(v.Position, 0f)) inHouse = true;
                }
                for (int i = 0; i < people.People.Count; i++)
                {
                    if (!people.AtPlace(i)) continue;
                    int at = people.PlaceOf(i);
                    var st = people.StateOf(i);
                    if (at == restaurant && st != PeopleSystem.Activity.Working) { if (hour >= 18.5f && hour < 21.5f) restaurantEvening++; if (hour >= 14f && hour < 17f) restaurantAfternoon++; }
                    if (at == disco && st != PeopleSystem.Activity.Working) { if (hour >= 20f || hour < 1.5f) discoNight++; else { discoDay++; discoDayHours += $" {hour:0.0}"; } }   // (it opens at eight)
                    if (at == museumAt) museum++;
                    if (at == parkAt) park++;
                    if (at == pitch && people.IsChild(i)) kidsPlaying++;
                    if (at == school && people.IsChild(i) && st == PeopleSystem.Activity.Working && people.IsHidden(i)) childAtSchool = true;
                }
                followed |= w.Animals.IsFollowing(dogIndex);
                var dp = w.Animals.PositionOf(dogIndex);
                bool walkableNow = people.Walkable(dp) || w.Paths.Covers(dp, 0.01f);
                if ((dp - dogHome).magnitude > AnimalSystem.Leash + 0.1f) { dogStrayed = true; strayWhy = $"{(dp - dogHome).magnitude:0.00} m from home"; }
                else if (!people.Walkable(dp) && !w.Paths.Covers(dp, 0.01f) && (dp - dogHome).magnitude > 0.08f) { dogStrayed = true; int nb = w.Buildings.Nearest(dp, 0.2f); strayWhy = $"on unwalkable ground at ({dp.x:0.000}, {dp.y:0.000}){(nb >= 0 ? $", {(new Vector2(w.Buildings.Houses[nb].x, w.Buildings.Houses[nb].z) - dp).magnitude * 100f:0.0} cm from the {Catalog.Get(w.Buildings.Houses[nb].variant).Name}" : "")}, at {hour:0.0} h, following {w.Animals.IsFollowing(dogIndex)}, walkable a moment ago {lastDogWalkable}"; }
                if (!dogStrayed) lastDogWalkable = walkableNow;
                var cp = w.Animals.PositionOf(catIndex);
                if ((cp - catHome).magnitude > AnimalSystem.CatRange * 1.4f) catStrayed = true;
                perched |= w.Animals.Perched(catIndex);
                if (people.Smoking(factory)) smoke++;
                if (w.Buildings.Aloft(balloon)) { if (VillageClock.IsNight(hour) && hour > 2f && hour < 6f) nightFlight = true; else aloft = true; }
                // Ears by the fair in the afternoon, by the disco at night.
                listener.localPosition = new Vector3(0.18f, 0.3f, -0.25f);
                foreach (var (kind, _) in w.Sounds.Playing) { if (kind == SoundLoop.Disco) discoHeard++; }
            }
            Check(moved && worstOff < 0.04f && !inHouse, $"Vehicles drive the roads all day and never leave them (worst {worstOff * 100f:0.0} cm from a road's centre line; {traffic.Trips} trips, {traffic.Stops} stops for traffic or people)");
            Check(traffic.Trips >= 2, $"Households drive to the shops, the restaurant and the park and come home ({traffic.Trips} round trips)");
            Check(restaurantEvening > restaurantAfternoon && restaurantEvening > 0, $"Supper fills the restaurant terrace ({restaurantEvening} in the evening vs {restaurantAfternoon} in the afternoon)");
            int owls = 0; for (int i = 0; i < people.People.Count; i++) if (people.NightOwl(i)) owls++;
            Check(discoNight > 0 && discoDay == 0 && discoHeard > 0, $"The disco comes alive after dark ({discoNight} dancing, {owls} night owls; heard {discoHeard} times; nobody there by day{(discoDay > 0 ? ": " + discoDay + " seen at" + discoDayHours.Substring(0, Mathf.Min(60, discoDayHours.Length)) : "")})");
            Check(museum > 0 && park > 0, $"Visitors come to the museum ({museum}) and the park ({park})");
            Check(childAtSchool && kidsPlaying > 0 && people.Matches + people.GoalsScored >= 0 && people.BallTravel > 0.05f,
                $"Children go to school and play football: a match with a ball ({people.BallTravel:0.0} m of passes, {people.GoalsScored} goals)");
            Check(smoke > 0, $"The factory chimney smokes while it works ({(people.WorkerAt(factory) >= 0 ? people.NameOf(people.WorkerAt(factory)) + " works there" : "nobody works there")}; next door: {Neighbour(w, Catalog.Cottage, 0.62f)})");
            Check(followed && !dogStrayed, $"The dog goes out with its owner, in their footsteps, and never strays from home (followed {followed}{(dogStrayed ? ", strayed: " + strayWhy : "")})");
            Check(!catStrayed && perched, "The cat keeps to its territory and sits up on the wall");
            Check(aloft && !nightFlight, "The balloon flies by day and stays down at night");
            // The disco's neon sign chases, blinks and shines: over its cycle every number of letters from none to all is seen.
            var litCounts = new HashSet<int>();
            for (float tt = 0f; tt < 7f; tt += 0.05f) { w.Buildings.Animate(tt, 0f, disco); litCounts.Add(w.Buildings.NeonLit(disco)); }
            Check(litCounts.Contains(0) && litCounts.Contains(5) && litCounts.Count >= 4, $"The disco's DISCO sign lights up letter by letter, blinks and shines ({litCounts.Count} different states)");
            Check((w.Sky.PositionOf(0) - cloud0).magnitude > 0.05f, "Clouds drift on the breeze");

            // Emergencies: each service answers, the patient is helped up, the fire goes out; vehicles come home.
            w.Clock.Hour = 10f;
            traffic.IncidentsOn = false;   // only the ones called here
            Town(w, 30f, false);           // everyone up and about
            foreach (var kind in new[] { IncidentKind.Unwell, IncidentKind.Fire, IncidentKind.Police })
            {
                int resolved = traffic.Resolved;
                string said = traffic.RaiseIncident(kind);
                bool siren = false, faint = false;
                int patient = -1;
                for (float s = 0f; s < 150f && traffic.Resolved == resolved && traffic.Active != null; s += 0.5f)
                {
                    Town(w, 0.5f, false);
                    if (traffic.Active == null) break;
                    siren |= traffic.Active.Responder != null && traffic.Active.Responder.Siren;
                    if (kind == IncidentKind.Unwell) { patient = traffic.Active.Person; faint |= people.IsUnwell(patient); }
                }
                for (float s = 0f; s < 90f; s += 0.5f)
                {
                    Town(w, 0.5f, false);
                    if (traffic.Vehicles.TrueForAll(v => v.Base < 0 || v.State == TrafficSystem.Mode.Parked)) break;
                }
                var home = traffic.Vehicles.Find(v => v.Kind == (kind == IncidentKind.Unwell ? VehicleKind.Ambulance : kind == IncidentKind.Fire ? VehicleKind.FireEngine : VehicleKind.Police));
                Check(traffic.Resolved == resolved + 1 && siren && (kind != IncidentKind.Unwell || faint && !people.IsUnwell(patient)) && home != null && home.State == TrafficSystem.Mode.Parked,
                    $"{kind}: \"{said}\" - answered with lights and siren, dealt with, and back at the station ({traffic.Resolved} resolved, {traffic.Abandoned} given up{(traffic.LastAbandoned != null ? ": " + traffic.LastAbandoned : "")})");
            }

            // CCTV: two cameras; watching films the selected one; the studio's wall refreshes in turn.
            var cctv = w.Cctv;
            cctv.Watching = true;
            int filmed = cctv.FilmedForViewer;
            Town(w, 2f, false);
            int perSecond = (cctv.FilmedForViewer - filmed) / 2;
            cctv.Watching = false;
            filmed = cctv.Filmed;
            Town(w, 3f, false);
            int wallFilmed = cctv.Filmed - filmed;
            Check(cctv.Count == 2 && cctv.Studios == 1 && perSecond >= 12 && perSecond <= 16 && wallFilmed >= 8 && wallFilmed <= 11,
                $"CCTV: {cctv.Count} cameras; the watched feed is filmed {perSecond} times a second, the studio's screens {wallFilmed / 3f:0.0} a second between them");
            cctv.Selected = 1;
            var target = new Vector3(0.36f, t.SurfaceAt(0.36f, 0.42f), 0.42f);
            cctv.AimAt(1, target);
            var aim = cctv.AimOf(1);
            string json = w.Serialize();
            var copy = new GameObject("Town copy").AddComponent<WonderlandWorld>();
            copy.transform.position = new Vector3(300f, 0f, 0f);
            copy.Deserialize(json, Materials());
            copy.People.Grid.Complete(); copy.People.Step(Dt); copy.Traffic.Refresh(true);
            var aimCopy = copy.Cctv.Count == 2 ? copy.Cctv.AimOf(1) : default;
            int pets = copy.Animals.Animals.FindAll(a => a.kind == (int)AnimalKind.Dog || a.kind == (int)AnimalKind.Cat).Count;
            Check(Mathf.Abs(Mathf.DeltaAngle(aimCopy.yaw, aim.yaw)) < 0.5f && Mathf.Abs(aimCopy.pitch - aim.pitch) < 0.5f && aim.pitch > 5f && pets == 2 && copy.Traffic.Vehicles.Count == traffic.Vehicles.Count,
                $"Save and load keep where the cameras look ({aim.yaw:0} / {aim.pitch:0} degrees; loaded {aimCopy.yaw:0} / {aimCopy.pitch:0}, {copy.Cctv.Count} cameras), the pets ({pets}), and the vehicles come back with their homes ({copy.Traffic.Vehicles.Count} of {traffic.Vehicles.Count})");
            UnityEngine.Object.DestroyImmediate(copy.gameObject);
            // Erasing the first camera: the selection follows the one that is left, aim and all.
            var cam0 = w.Buildings.Houses[cctv.HouseOf(0)];
            w.EraseAt(new Vector3(cam0.x, 0.5f, cam0.z), Vector3.down, new Vector3(cam0.x, t.SurfaceAt(cam0.x, cam0.z), cam0.z));
            Town(w, 0.5f, false);
            Check(cctv.Count == 1 && cctv.Selected == 0 && Mathf.Abs(Mathf.DeltaAngle(cctv.AimOf(0).yaw, aim.yaw)) < 0.5f, "Erasing a camera leaves the other selected, still aimed where it was");

            // Sound: settings are respected; moments are rate-limited and only heard within earshot.
            listener.localPosition = new Vector3(0f, 0.3f, 0f);
            traffic.RaiseIncident(IncidentKind.Fire);   // a siren in earshot
            Town(w, 1f, false);
            w.Sounds.Step(0.3f);
            int playing = w.Sounds.Playing.Count;
            SoundSystem.Setting = SoundSetting.Off;
            w.Sounds.Step(0.3f);
            int off = w.Sounds.Playing.Count, played = w.Sounds.CuesPlayed;
            w.Sounds.Cue(SoundCue.Bark, new Vector2(0f, 0.05f));
            int whileOff = w.Sounds.CuesPlayed - played;
            SoundSystem.Setting = SoundSetting.On;
            w.Sounds.Step(6f);   // past any bark the dog let out just now
            played = w.Sounds.CuesPlayed;
            for (int i = 0; i < 5; i++) w.Sounds.Cue(SoundCue.Bark, new Vector2(0f, 0.05f));
            int burst = w.Sounds.CuesPlayed - played;
            w.Sounds.Cue(SoundCue.Meow, new Vector2(0f, 0.05f) + new Vector2(5f, 0f));
            int far = w.Sounds.CuesPlayed - played - burst;
            Check(playing > 0 && playing <= SoundSystem.Loops && off == 0 && whileOff == 0 && burst == 1 && far == 0,
                $"Sound: {playing} sources near the crossroads (at most {SoundSystem.Loops}); off means silent; five barks in a row play once; a meow out of earshot isn't played");
            SoundSystem.Setting = keep;

            // Taking a road away while traffic is out: everyone gets home (or stays put), nothing breaks.
            w.Clock.Hour = 11f;
            Town(w, 20f, false);
            int line = w.Paths.Lines.FindIndex(l => l.kind == (int)PathKind.Road && Mathf.Abs(l.points[0].x) < 0.05f);
            if (line >= 0) { w.Paths.Lines.RemoveAt(line); w.Paths.Reindex(); w.Paths.Rebuild(); people.WorldChanged(); }
            Town(w, 30f, false);
            worstOff = 0f;
            foreach (var v in traffic.Vehicles) if (v.State == TrafficSystem.Mode.Driving) worstOff = Mathf.Max(worstOff, traffic.OffRoad(v.Position));
            Check(line >= 0 && worstOff < 0.04f && !traffic.Connected(cottage, PeopleSystem.DoorStep(w.Buildings.Houses[Find(Catalog.Factory)]), true),
                $"Taking a road away re-routes or parks the traffic (worst {worstOff * 100f:0.0} cm off the remaining roads)");

            // A busy town: 40 villagers, its vehicles, pets, sound and cameras (editor timing, not Quest).
            for (int k = 0; people.People.Count < WonderlandWorld.MaxPeople && k < 400; k++)
            {
                float a = k * 2.39996f, r = 0.3f + (k % 7) * 0.06f;
                w.PlacePerson(t.PointAt(Mathf.Cos(a) * r * 1.5f, Mathf.Sin(a) * r), k % PersonFactory.StyleCount, 1900 + k);
            }
            w.Clock.Hour = 17f;
            people.Grid.Complete();
            Town(w, 3f, false);
            cctv.Watching = true;
            var watch = new Stopwatch();
            double sum = 0, worst = 0; int frames = 0;
            for (int k = 0; k < 72 * 20; k++)
            {
                watch.Restart();
                Town(w, Dt, false);
                double ms = watch.Elapsed.TotalMilliseconds;
                sum += ms; worst = Math.Max(worst, ms); frames++;
            }
            cctv.Watching = false;
            Check(sum / frames < 4.0, $"A busy town ({people.People.Count} villagers, {traffic.Vehicles.Count} vehicles, {w.Animals.Animals.Count} animals, sound and a camera feed): {sum / frames:0.00} ms per frame on average, worst {worst:0.0} ms (editor timing, simulation only)");
            UnityEngine.Object.DestroyImmediate(w.gameObject);

            CrossingJourney();
        }

        /// <summary>A road across a railway: vehicles wait at the level crossing while a train goes by.</summary>
        private static void CrossingJourney()
        {
            var w = Scratch("Crossing", 310f);
            var t = w.Terrain;
            var rail = w.BuildTrack(Loop(t, 0.42f, 0.34f, 0.97f, false));
            string road = w.BuildPath(Stroke(t, new Vector2(0.05f, -0.05f), new Vector2(0.05f, -0.75f)), PathKind.Road);
            string road2 = w.BuildPath(Stroke(t, new Vector2(-0.3f, -0.6f), new Vector2(0.4f, -0.6f)), PathKind.Road);
            int ok = 0;
            foreach (var (id, x, z, rot) in new[] { (Catalog.Cottage, -0.06f, -0.08f, 270f), (Catalog.Townhouse, 0.17f, -0.1f, 90f), (Catalog.Grocer, -0.2f, -0.68f, 180f), (Catalog.Bakery, 0.3f, -0.68f, 180f), (Catalog.Cottage, 0.3f, -0.52f, 0f) })
                if (Put(w, id, x, z, rot) == null) ok++;
            string train = PlaceTrainAt(w, Main(w).Points[Main(w).Points.Count / 4]);
            w.People.Grid.Complete(); w.People.Step(Dt);
            w.Traffic.Refresh(true);
            Check(rail.Ok && road == null && road2 == null && train == null && w.Paths.LevelCrossings.Count > 0 && w.Traffic.Vehicles.Count > 0,
                $"A road crosses the railway at a level crossing; homes on both sides have vehicles ({ok} places, {w.Traffic.Vehicles.Count} vehicles){road ?? road2 ?? train ?? ""}");
            w.Clock.Hour = 9f;
            bool hit = false;
            for (float s = 0f; s < 300f; s += 0.5f)
            {
                if (w.Clock.Hour > 17f) w.Clock.Hour = 9f;
                Town(w, 0.5f, true);
                foreach (var v in w.Traffic.Vehicles)
                {
                    if (v.State != TrafficSystem.Mode.Driving) continue;
                    foreach (var tr in w.Trains.Trains)
                    {
                        var c = w.Trains.CentrePosition(tr);
                        var l = w.Trains.LocoPosition(tr);
                        if (new Vector2(c.x - v.Position.x, c.z - v.Position.y).magnitude < tr.Length * 0.5f && w.Paths.NearLevelCrossing(v.Position, 0.03f)
                            && new Vector2(l.x - v.Position.x, l.z - v.Position.y).magnitude < tr.Length) hit = true;
                    }
                }
            }
            Check(w.Traffic.CrossingWaits > 0 && !hit, $"Vehicles wait at the level crossing while a train goes by ({w.Traffic.CrossingWaits} waits, {w.Traffic.Trips} trips), and are never on it with the train");
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        /// <summary>Editor renders of the livelier town: streets with traffic, the pitch and the balloon, a fire, night, CCTV.</summary>
        private static void TownRenders(Camera cam)
        {
            var w = Scratch("Town demo", 330f);
            var t = w.Terrain;
            w.BuildPath(Stroke(t, new Vector2(-0.82f, 0f), new Vector2(0.82f, 0f)), PathKind.Road);
            w.BuildPath(Stroke(t, new Vector2(0f, -0.7f), new Vector2(0f, 0.7f)), PathKind.Road);
            w.BuildPath(Stroke(t, new Vector2(-0.6f, 0.62f), new Vector2(-0.05f, 0.62f)), PathKind.Lane);
            w.BuildPath(Stroke(t, new Vector2(0.58f, -0.24f), new Vector2(0.8f, -0.24f)), PathKind.Wall);
            foreach (var (id, x, z, rot) in new[]
            {
                (Catalog.Cottage, -0.66f, 0.1f, 0f), (Catalog.Townhouse, -0.45f, 0.1f, 0f), (Catalog.Restaurant, -0.2f, 0.14f, 0f), (Catalog.Museum, 0.2f, 0.13f, 0f),
                (Catalog.Apartments, 0.45f, 0.1f, 0f), (Catalog.Cottage, 0.68f, 0.1f, 0f), (Catalog.Hospital, -0.62f, -0.12f, 180f), (Catalog.Police, -0.4f, -0.1f, 180f),
                (Catalog.FireStation, -0.18f, -0.11f, 180f), (Catalog.Disco, 0.18f, -0.1f, 180f), (Catalog.Studio, 0.42f, -0.1f, 180f), (Catalog.Townhouse, 0.66f, -0.1f, 180f),
                (Catalog.Park, -0.42f, 0.4f, 0f), (Catalog.Football, 0.36f, 0.42f, 0f), (Catalog.School, -0.4f, -0.42f, 0f), (Catalog.Factory, 0.36f, -0.44f, 0f),
                (Catalog.Apartments, -0.12f, -0.4f, 90f), (Catalog.Thatched, 0.12f, 0.42f, 270f), (Catalog.Balloon, 0.72f, 0.45f, 0f), (Catalog.Bakery, 0.12f, -0.3f, 270f),
                (Catalog.Camera, 0.07f, 0.07f, 0f), (Catalog.Camera, 0.08f, 0.32f, 180f), (Catalog.Cottage, 0.62f, -0.56f, 0f), (Catalog.Lamp, 0.05f, -0.05f, 0f), (Catalog.Lamp, -0.3f, 0.04f, 0f),
            })
                Put(w, id, x, z, rot);
            for (int i = 0; i < 10; i++) w.PaintForest(new Vector3(-0.75f + Mathf.Sin(i) * 0.08f, 0f, -0.6f + Mathf.Cos(i * 1.3f) * 0.08f), 0.1f, 0.8f, Dt * 4f);
            var dogAt = PeopleSystem.DoorStep(w.Buildings.Houses[0]) + new Vector2(0.03f, 0f);
            w.PlaceAnimal(new Vector3(dogAt.x, 0f, dogAt.y), AnimalKind.Dog, 501);
            w.PlaceAnimal(new Vector3(0.7f, 0f, -0.2f), AnimalKind.Cat, 502);
            for (int seed = 3000, added = 0; added < 4 && seed < 4000; seed++)
                if (PeopleSystem.ChildSeed(seed) && w.PlacePerson(t.PointAt(0.2f + added * 0.03f, 0.33f), added, seed) == null) added++;
            for (int k = 0; w.People.People.Count < 30 && k < 200; k++)
            {
                float a = k * 2.39996f, r = 0.25f + (k % 5) * 0.08f;
                w.PlacePerson(t.PointAt(Mathf.Cos(a) * r * 1.5f, Mathf.Sin(a) * r), k % PersonFactory.StyleCount, 2900 + k);
            }
            w.People.Grid.Complete();
            w.Traffic.Refresh(true);
            w.Traffic.IncidentsOn = false;
            w.Cctv.AimAt(0, new Vector3(-0.1f, t.SurfaceAt(-0.1f, 0f), 0f));
            w.Cctv.AimAt(1, new Vector3(0.36f, t.SurfaceAt(0.36f, 0.42f), 0.42f));
            Light key = null;
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) key = l;
            var c = w.transform.position;
            void Until(float hour, float extra = 0f)
            {
                for (int guard = 0; guard < 72 * 900 && Mathf.Abs(w.Clock.Hour - hour) > 0.05f; guard++) Town(w, Dt, false);
                for (float s = 0f; s < extra; s += Dt) Town(w, Dt, false);
                WonderlandLighting.SetTimeOfDay(key, w.Clock.Hour, w.Clock.Daylight, 0f);
                w.Buildings.Animate(3f, w.Buildings.RidesOpen * 3f);
                w.Cover.RebuildDirty(); w.Forest.RebuildDirty();
            }
            w.Clock.Hour = 8f;
            Until(10.5f);
            Town(w, 20f, false);
            WonderlandLighting.SetTimeOfDay(key, w.Clock.Hour, w.Clock.Daylight, 0f);
            cam.transform.position = c + new Vector3(0.2f, 1.1f, -1.4f); cam.transform.LookAt(c + new Vector3(0f, 0.02f, 0f)); cam.fieldOfView = 42f;
            WonderlandRenders.Capture(cam, "editor-town.png", 1920, 1200);
            cam.transform.position = c + new Vector3(0.32f, 0.07f, 0.03f); cam.transform.LookAt(c + new Vector3(0f, 0.015f, 0.01f)); cam.fieldOfView = 46f;
            WonderlandRenders.Capture(cam, "editor-town-street.png", 1920, 1200);
            Debug.Log($"editor-town-street.png: {w.People.Census()}, {w.Traffic.Vehicles.Count} vehicles, {w.Traffic.Moving} moving");
            Until(15.5f);
            for (int k = 0; k < 72 * 120 && w.People.Matches == 0; k++) Town(w, Dt, false);
            for (int k = 0; k < 72 * 4; k++) Town(w, Dt, false);
            cam.transform.position = c + new Vector3(0.62f, 0.3f, 0.05f); cam.transform.LookAt(c + new Vector3(0.42f, 0.12f, 0.45f)); cam.fieldOfView = 46f;
            WonderlandRenders.Capture(cam, "editor-town-pitch.png", 1920, 1200);
            Debug.Log($"editor-town-pitch.png: {w.People.Matches} matches, balloon aloft {w.Buildings.Aloft(w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Balloon))}");
            // CCTV: the studio's wall and one camera's feed.
            for (int i = 0; i < 8; i++) w.Cctv.Step(CctvSystem.WallSeconds);
            cam.transform.position = c + new Vector3(0.43f, 0.075f, 0.06f); cam.transform.LookAt(c + new Vector3(0.41f, 0.045f, -0.06f)); cam.fieldOfView = 44f;
            WonderlandRenders.Capture(cam, "editor-town-studio.png", 1920, 1200);
            foreach (int k in new[] { 0, 1 })
            {
                w.Cctv.Selected = k;
                w.Cctv.FilmNow();
                var shot = w.Cctv.Snapshot();
                System.IO.File.WriteAllBytes($"docs/renders/editor-cctv-{k + 1}.png", shot.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(shot);
            }
            // A fire: the engine at the scene, smoke and the hose.
            string fire = w.Traffic.RaiseIncident(IncidentKind.Fire);
            for (int k = 0; k < 72 * 120 && (w.Traffic.Active == null || w.Traffic.Active.Scene < 6f); k++) Town(w, Dt, false);
            for (int k = 0; k < 72 * 2; k++) Town(w, Dt, false);
            var active = w.Traffic.Active;
            var engine = active?.Responder != null ? active.Responder.Position : Vector2.zero;
            var house = active != null ? active.HousePos : Vector2.zero;
            if (active?.Smoke != null) active.Smoke.Simulate(3f, true, true);
            if (active?.Spray != null) active.Spray.Simulate(1f, true, true);
            var side = (engine - house).normalized;
            var from = engine + side * 0.12f + new Vector2(-side.y, side.x) * 0.1f;
            cam.transform.position = c + new Vector3(from.x, 0.13f, from.y); cam.transform.LookAt(c + new Vector3((engine.x + house.x) * 0.5f, 0.04f, (engine.y + house.y) * 0.5f)); cam.fieldOfView = 46f;
            WonderlandRenders.Capture(cam, "editor-town-fire.png", 1920, 1200);
            Debug.Log($"editor-town-fire.png: {fire}; {(active != null ? active.Responder.Kind + " " + active.Responder.State + " at " + engine + ", scene " + active.Scene : "over")}");
            Until(22.5f);
            cam.transform.position = c + new Vector3(0.1f, 0.1f, 0.06f); cam.transform.LookAt(c + new Vector3(0.18f, 0.04f, -0.08f)); cam.fieldOfView = 50f;
            WonderlandRenders.Capture(cam, "editor-town-night.png", 1920, 1200);
            Debug.Log($"editor-town-night.png: {w.People.Census()}, disco visitors {w.People.VisitorsAt(w.Buildings.Houses.FindIndex(h => h.variant == Catalog.Disco))}");
            WonderlandLighting.SetTimeOfDay(key, 13f, 1f, 0f);
            WonderlandLighting.Configure(key);
            UnityEngine.Object.DestroyImmediate(w.gameObject);
        }

        private static string Neighbour(WonderlandWorld w, int variant, float x)
        {
            int house = w.Buildings.Houses.FindIndex(h => h.variant == variant && Mathf.Abs(h.x - x) < 0.01f);
            for (int i = 0; i < w.People.People.Count; i++)
                if (w.People.HomeOf(i) == house) return $"{w.People.NameOf(i)} (child {w.People.IsChild(i)}, works at {w.People.WorkplaceOf(i)})";
            return "nobody";
        }

        /// <summary>Lives a while: the clock, trains, villagers, traffic, animals, balloons, clouds, sound and cameras.</summary>
        private static void Town(WonderlandWorld w, float seconds, bool trains)
        {
            for (float s = 0f; s < seconds - 1e-4f; s += Dt)
            {
                w.Clock.Tick(Dt);
                if (trains) w.Trains.Step(Dt);
                w.People.Step(Dt);
                w.Traffic.Step(Dt);
                w.Animals.Step(Dt);
                w.Buildings.StepFlights(Dt);
                w.Sky.Step(Dt);
                w.Sounds.Step(Dt);
                w.Cctv.Step(Dt);
                w.Boats.Step(Dt);
            }
        }
    }
}
