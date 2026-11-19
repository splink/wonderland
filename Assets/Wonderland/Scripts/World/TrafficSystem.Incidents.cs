using UnityEngine;

namespace Wonderland
{
    public enum IncidentKind { Unwell, Fire, Police }

    /// <summary>
    /// The village's small emergencies, one at a time and a few hours apart by day: someone feels faint (the ambulance
    /// comes, the paramedics help them up), smoke from a roof (the fire engine sprays it out), a bicycle goes missing
    /// (the patrol car looks into it). The vehicle drives from its station with lights and siren, parks at the scene,
    /// deals with it and drives back. If there is no way there by road, or the vehicle or the place goes, the incident
    /// ends quietly instead. The Life tool's Emergency chip calls one at once.
    /// </summary>
    public sealed partial class TrafficSystem
    {
        public sealed class Incident
        {
            public IncidentKind Kind;
            public Vector2 At;                 // the scene
            public int Person = -1, House = -1;
            public Vector2 HousePos;
            public Vehicle Responder;
            public float Clock, Scene;         // seconds since the call / at the scene
            public bool Arrived;
            public string Where;
            public ParticleSystem Smoke, Spray;
        }

        public Incident Active { get; private set; }
        public bool IncidentsOn = true;
        public int Resolved { get; private set; }
        public int Abandoned { get; private set; }
        public string LastAbandoned { get; private set; }
        private float nextIncident = 3f;
        private Rng incidentRng = new(4242);

        private bool Responding(Vehicle v) => Active != null && Active.Responder == v && !Active.Arrived;

        private void StepIncidents(float dt)
        {
            var clock = world.Clock;
            if (Active == null)
            {
                if (IncidentsOn && clock.Hour >= 8f && clock.Hour < 21f && clock.Total > nextIncident) { nextIncident = clock.Total + incidentRng.Range(2.5f, 5f); Raise(null); }
                return;
            }
            var inc = Active;
            inc.Clock += dt;
            var v = inc.Responder;
            if (v == null || !Vehicles.Contains(v)) { Abandon("The emergency is over"); return; }
            if (inc.Kind == IncidentKind.Unwell && !world.People.IsUnwell(inc.Person) && !inc.Arrived) { Abandon("The patient felt better before help arrived"); return; }
            if (inc.House >= 0 && !HouseStillThere(inc)) { Abandon("The emergency is over"); return; }
            if (!inc.Arrived)
            {
                if (v.State == Mode.Parked || inc.Clock > 120f) Abandon(Name(v.Kind) + " couldn't get through - " + (inc.Kind == IncidentKind.Unwell ? "they felt better on their own" : "it sorted itself out"));
                return;
            }
            inc.Scene += dt;
            switch (inc.Kind)
            {
                case IncidentKind.Unwell:
                    if (inc.Scene > 9f && world.People.IsUnwell(inc.Person)) { world.People.Recover(inc.Person); world.People.Tell($"The paramedics helped {world.People.NameOf(inc.Person)} back on their feet"); }
                    if (inc.Scene > 15f) Finish();
                    break;
                case IncidentKind.Fire:
                    if (inc.Spray != null && !inc.Spray.isPlaying) inc.Spray.Play();
                    if (inc.Scene > 14f && inc.Smoke != null && inc.Smoke.isEmitting) { inc.Smoke.Stop(); world.People.Tell($"The fire at the {inc.Where} is out"); }
                    if (inc.Scene > 20f) Finish();
                    break;
                default:
                    if (inc.Scene > 14f) { world.People.Tell($"The police found the bicycle behind the {inc.Where}"); Finish(); }
                    break;
            }
        }

        private bool HouseStillThere(Incident inc) => inc.House < world.Buildings.Houses.Count && new Vector2(world.Buildings.Houses[inc.House].x, world.Buildings.Houses[inc.House].z) == inc.HousePos;

        private static string Name(VehicleKind k) => k == VehicleKind.Ambulance ? "The ambulance" : k == VehicleKind.Police ? "The police car" : "The fire engine";

        /// <summary>Calls an emergency now (a kind, or any that can be answered); returns what happened (for the palette).</summary>
        public string RaiseIncident(IncidentKind? kind = null)
        {
            if (Active != null) return "Help is already on its way to " + (Active.Where ?? "an emergency");
            return Raise(kind) ?? "No emergency service can get there - build a hospital, police or fire station by a road";
        }

        private string Raise(IncidentKind? only)
        {
            int start = incidentRng.Range(0, 3);
            for (int k = 0; k < 3; k++)
            {
                var kind = only ?? (IncidentKind)((start + k) % 3);
                var v = Vehicles.Find(x => x.State == Mode.Parked && x.Kind == (kind == IncidentKind.Unwell ? VehicleKind.Ambulance : kind == IncidentKind.Fire ? VehicleKind.FireEngine : VehicleKind.Police));
                if (v != null)
                {
                    string said = Dispatch(kind, v);
                    if (said != null) return said;
                }
                if (only.HasValue) break;
            }
            return null;
        }

        private string Dispatch(IncidentKind kind, Vehicle v)
        {
            var inc = new Incident { Kind = kind, Responder = v };
            var houses = world.Buildings.Houses;
            if (kind == IncidentKind.Unwell)
            {
                int who = world.People.SomeoneOutdoors(ref incidentRng, p => NearestNode(p, 0.18f, true) >= 0);
                if (who < 0) return null;
                var at = world.People.PositionOf(who);
                if (NearestNode(at, 0.18f, true) < 0 || !DriveTo(v, at, false)) return null;
                world.People.Faint(who);
                inc.Person = who; inc.At = at; inc.Where = world.People.NameOf(who);
                Begin(inc, v);
                world.People.Tell($"{inc.Where} feels faint - the ambulance is on its way");
                return world.People.News;
            }
            // A place by the road: a fire (any building) or a missing bicycle (somewhere people go).
            for (int tries = 0; tries < 10; tries++)
            {
                int i = incidentRng.Range(0, houses.Count);
                if (houses.Count == 0) break;
                var bp = Catalog.Get(houses[i].variant);
                if (bp.Small || bp.Base || bp.Id == Catalog.FireStation || bp.Walkable) continue;
                if (kind == IncidentKind.Police && bp.Serves == Need.None && !bp.Dwelling) continue;
                var door = PeopleSystem.DoorStep(houses[i]);
                if (NearestNode(door, 0.18f, true) < 0 || !DriveTo(v, door, false)) continue;
                inc.House = i; inc.HousePos = new Vector2(houses[i].x, houses[i].z); inc.At = door; inc.Where = bp.Name.ToLower();
                if (kind == IncidentKind.Fire)
                {
                    inc.Smoke = Smoke(transform, new Vector3(houses[i].x, world.Buildings.TopOf(i), houses[i].z), world.Materials.Smoke, new Color(0.45f, 0.45f, 0.48f, 0.8f), 14f, 0.025f);
                    world.People.Tell($"Smoke from the {inc.Where}! The fire engine is coming");
                }
                else world.People.Tell($"A bicycle has gone missing at the {inc.Where} - the police are coming");
                Begin(inc, v);
                return world.People.News;
            }
            return null;
        }

        private void Begin(Incident inc, Vehicle v)
        {
            v.Siren = true;
            Active = inc;
            world.Sounds?.Cue(SoundCue.Bell, v.Position);
        }

        private void OnScene(Vehicle v)
        {
            var inc = Active;
            inc.Arrived = true; inc.Scene = 0f;
            v.Siren = false; v.State = Mode.AtScene;
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            v.Shift = v.ShiftTo = 0.012f;   // pulled in to the kerb, up on it: the traffic passes
            Pose(v, 0f);
            if (inc.Kind == IncidentKind.Fire)
            {
                // The hose: a spray from the engine up at the roof.
                var target = new Vector3(inc.HousePos.x, world.Buildings.TopOf(inc.House) - 0.02f, inc.HousePos.y);
                var from = new Vector3(v.Position.x, v.Height + 0.03f, v.Position.y);
                inc.Spray = Smoke(transform, from, world.Materials.Smoke, new Color(0.75f, 0.88f, 1f, 0.7f), 40f, 0.006f, (target - from), 0.4f);
            }
        }

        private void Finish()
        {
            Resolved++;
            End(true);
        }

        private void Abandon(string why)
        {
            Abandoned++;
            LastAbandoned = why;
            world.People.Tell(why);
            End(false);
        }

        private void End(bool done)
        {
            var inc = Active;
            Active = null;
            if (inc.Kind == IncidentKind.Unwell && world.People.IsUnwell(inc.Person)) world.People.Recover(inc.Person);
            foreach (var ps in new[] { inc.Smoke, inc.Spray })
                if (ps != null) { if (Application.isPlaying) Object.Destroy(ps.gameObject); else Object.DestroyImmediate(ps.gameObject); }
            var v = inc.Responder;
            if (v != null && Vehicles.Contains(v))
            {
                v.Siren = false;
                if (!DriveTo(v, v.Home, true)) GoHome(v, true);
            }
        }

        /// <summary>After the vehicles were rebuilt: an incident whose vehicle went ends.</summary>
        private void AbandonOrphans()
        {
            if (Active != null && (Active.Responder == null || !Vehicles.Contains(Active.Responder))) Abandon("The emergency is over");
        }

        /// <summary>A puff of smoke (or a water spray, given a direction): a small particle system, local to <paramref name="parent"/>.</summary>
        public static ParticleSystem Smoke(Transform parent, Vector3 at, Material material, Color colour, float rate, float size, Vector3? direction = null, float speed = 0.02f)
        {
            var go = new GameObject(direction.HasValue ? "Spray" : "Smoke");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            if (direction.HasValue) go.transform.localRotation = Quaternion.LookRotation(direction.Value.normalized + Vector3.up * 0.4f);
            else go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = direction.HasValue ? 0.9f : 2.6f;
            main.startSpeed = direction.HasValue ? speed : 0.02f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = colour;
            main.gravityModifier = direction.HasValue ? 0.08f : -0.004f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 80;
            var emission = ps.emission; emission.rateOverTime = rate;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = direction.HasValue ? 6f : 14f; shape.radius = 0.004f;
            var sizeOver = ps.sizeOverLifetime; sizeOver.enabled = true; sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, direction.HasValue ? 1.2f : 2.4f));
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!direction.HasValue) ps.Play();
            return ps;
        }
    }
}
