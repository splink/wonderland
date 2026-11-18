using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Farm machines: a wheat field keeps a little tractor parked at its edge (no road needed). When the field's farmer
    /// comes to work they climb onto it - and can be seen sitting at the wheel - and drive it up and down the field in
    /// rows, turning at the headlands, a puff of smoke from its chimney now and then; when their shift (or the day's
    /// work) is over the tractor goes back to its corner and they climb down.
    /// </summary>
    public sealed partial class TrafficSystem
    {
        public const float TractorSpeed = 0.018f;

        /// <summary>Ploughing rows across a field, in its own frame (x along the rows, z across them).</summary>
        private static List<Vector2> Rows(Vector2 half)
        {
            var rows = new List<Vector2>();
            float x = half.x * 0.78f, z0 = -half.y * 0.72f, z1 = half.y * 0.72f;
            int n = Mathf.Max(2, Mathf.RoundToInt((z1 - z0) / 0.024f) + 1);
            for (int i = 0; i < n; i++)
            {
                float z = Mathf.Lerp(z0, z1, i / (float)(n - 1));
                bool back = i % 2 == 1;
                rows.Add(new Vector2(back ? x : -x, z));
                rows.Add(new Vector2(back ? -x : x, z));
            }
            return rows;
        }

        /// <summary>Where a field's tractor waits: at its front corner, just outside the crop.</summary>
        private static Vector2 TractorPark(HouseInstance h)
        {
            var half = Catalog.Get(h.variant).Half;
            var d = Quaternion.Euler(0f, h.rotation, 0f) * new Vector3(-half.x * 0.78f, 0f, -half.y - 0.016f);
            return new Vector2(h.x + d.x, h.z + d.z);
        }

        private Vector2 FieldPoint(HouseInstance h, Vector2 local)
        {
            var d = Quaternion.Euler(0f, h.rotation, 0f) * new Vector3(local.x, 0f, local.y);
            return new Vector2(h.x + d.x, h.z + d.z);
        }

        /// <summary>The field's tractor (by building index), or null.</summary>
        private Vehicle TractorOf(int house)
        {
            foreach (var v in Vehicles) if (v.Field == house) return v;
            return null;
        }

        /// <summary>Whether field <paramref name="house"/> has a tractor (its farmer drives it at work).</summary>
        public bool HasTractor(int house) => TractorOf(house) != null;

        /// <summary>Whether a field is being worked by its farmer (set by the world: the farmer is at work on the tractor).</summary>
        public System.Func<int, bool> Operating;

        /// <summary>A tractor: up and down its field while the farmer is aboard; otherwise back to its corner and parked.</summary>
        private void StepTractor(Vehicle v, float dt)
        {
            var houses = world.Buildings.Houses;
            if (v.Field < 0 || v.Field >= houses.Count) return;
            var h = houses[v.Field];
            bool working = Operating != null && Operating(v.Field);
            ShowDriver(v, working || v.Next >= 0 && v.Speed > 0.001f);
            Vector2 target;
            if (working)
            {
                var rows = Rows(Catalog.Get(h.variant).Half);
                if (v.Next < 0 || v.Next >= rows.Count) v.Next = 0;
                target = FieldPoint(h, rows[v.Next]);
                if ((target - v.Position).magnitude < 0.008f) { v.Next = (v.Next + 1) % rows.Count; target = FieldPoint(h, rows[v.Next]); }
            }
            else
            {
                target = TractorPark(h);
                if ((target - v.Position).magnitude < 0.006f)
                {
                    v.Speed = 0f; v.Next = -1;
                    v.Heading = Mathf.MoveTowardsAngle(v.Heading, h.rotation + 90f, 90f * dt);
                    Pose(v, dt);
                    ShowDriver(v, false);
                    if (v.Exhaust != null && v.Exhaust.isEmitting) v.Exhaust.Stop();
                    return;
                }
            }
            var to = target - v.Position;
            float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            float turn = Mathf.Abs(Mathf.DeltaAngle(v.Heading, want));
            v.Heading = Mathf.MoveTowardsAngle(v.Heading, want, 110f * dt);
            v.Speed = Mathf.MoveTowards(v.Speed, TractorSpeed * Mathf.Lerp(1f, 0.35f, turn / 90f), 0.03f * dt);
            var fwd = new Vector2(Mathf.Sin(v.Heading * Mathf.Deg2Rad), Mathf.Cos(v.Heading * Mathf.Deg2Rad));
            v.Position += fwd * v.Speed * dt;
            Pose(v, dt);
            if (v.Exhaust == null) v.Exhaust = Smoke(v.Root, v.Root.position, world.Materials.Smoke, new Color(0.5f, 0.5f, 0.52f, 0.5f), 3f, 0.005f);
            if (v.Exhaust != null)
            {
                v.Exhaust.transform.localPosition = new Vector3(0.0035f, 0.028f, 0.012f);
                if (!v.Exhaust.isEmitting) v.Exhaust.Play();
            }
        }

        /// <summary>The farmer at the wheel (their own figure, by the field's worker), or nobody.</summary>
        private void ShowDriver(Vehicle v, bool on)
        {
            if (v.Passenger == null)
            {
                var go = new GameObject("Driver");
                go.layer = WonderlandWorld.MiniatureLayer;
                go.transform.SetParent(v.Root, false);
                go.transform.localPosition = VehicleFactory.TractorSeat;
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                v.Passenger = go.transform;
            }
            if (on && FarmerStyle != null) v.Passenger.GetComponent<MeshFilter>().sharedMesh = PersonFactory.Body(FarmerStyle(v.Field));
            if (v.Passenger.gameObject.activeSelf != on) v.Passenger.gameObject.SetActive(on);
        }

        /// <summary>The outfit of the farmer working a field (set by the world).</summary>
        public System.Func<int, int> FarmerStyle;

        /// <summary>The seed of the farmer working a field (set by the world).</summary>
        public System.Func<int, int> FarmerSeed;

        /// <summary>A tractor's farmer's name (set by the world), for its card.</summary>
        public System.Func<int, string> FarmerName;

        private string TractorLine(Vehicle v)
        {
            string field = v.Owner != null ? v.Owner.ToLower() : "field";
            bool working = Operating != null && Operating(v.Field);
            string who = FarmerName?.Invoke(v.Field);
            return working ? $"The tractor - {who ?? "the farmer"} is ploughing the {field}" : v.Speed > 0.001f ? $"The tractor - going back to the corner of the {field}" : $"The tractor - parked by the {field}";
        }
    }
}
