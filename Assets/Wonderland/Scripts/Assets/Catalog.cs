using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    public enum PlaceCategory { Homes, Town, Farm, Fun, Travel, Nature, Small }

    /// <summary>What people do at an activity spot.</summary>
    public enum SpotKind { Door, Sit, Gather, Play, Ride, Watch, Browse }

    /// <summary>
    /// A place where a person can do something: in the object's local frame (front is -Z), with the direction the person
    /// faces (yaw in degrees; 0 looks along +Z, i.e. at a building from in front of it).
    /// </summary>
    public struct ActivitySpot
    {
        public Vector3 At;
        public SpotKind Kind;
        public float Facing;
        public ActivitySpot(float x, float z, SpotKind kind, float facing = 0f, float y = 0f) { At = new Vector3(x, y, z); Kind = kind; Facing = facing; }
    }

    /// <summary>Everything the world needs to know about one kind of placeable object.</summary>
    public sealed class Blueprint
    {
        public int Id;
        public string Name, Short;
        public PlaceCategory Category;
        public Vector2 Half;                 // footprint half extents (x, z) including eaves
        public float Height = 0.12f;         // for picking
        public bool Pad = true;              // grade a flat pad under it
        public bool Small;                   // a detail: may stand close to other things
        public int Residents;                // villagers who move in when it is built
        public int Styles = 1;
        public ActivitySpot[] Spots = Array.Empty<ActivitySpot>();
        public Vector3 RotorPivot;           // a turning part (carousel, wheel, sails), local
        public Vector3 RotorAxis = Vector3.up;
        public float RotorSpeed;             // degrees per second; 0 = none
        public string Hint;                  // a word about what it brings to the world
        public bool Walkable;                // people may walk inside its footprint (playground, campsite, tower)
        public int Gondolas;                 // cabins that hang upright from the rotor (Ferris wheel)
        public float GondolaRadius;
    }

    /// <summary>
    /// The library of placeable objects, grouped by category so the palette stays small: pick a category, then an item.
    /// Ids are stable (saved worlds store them): 0-3 are the original houses and the station, new objects are appended.
    /// Every object brings its own life with it through its activity spots: a café's chairs, a fountain's rim, a carousel's
    /// horses, a shop's counter. People nearby find them and use them; nothing needs configuring.
    /// </summary>
    public static class Catalog
    {
        public static readonly Blueprint[] All = Build();
        public static int Count => All.Length;
        public static Blueprint Get(int id) => All[Mathf.Clamp(id, 0, All.Length - 1)];

        public static readonly string[] CategoryNames = { "Homes", "Town", "Farm", "Fun", "Travel", "Nature", "Small" };

        public static List<int> InCategory(PlaceCategory c)
        {
            var list = new List<int>();
            foreach (var b in All) if (b.Category == c) list.Add(b.Id);
            return list;
        }

        // Ids (stable).
        public const int Cottage = 0, Townhouse = 1, Thatched = 2, Station = 3, Apartments = 4, Chalet = 5, Church = 6, TownHall = 7,
            Bakery = 8, Cafe = 9, Grocer = 10, School = 11, FireStation = 12, Fountain = 13, Barn = 14, Windmill = 15, Silo = 16,
            Greenhouse = 17, WheatField = 18, Garden = 19, Carousel = 20, FerrisWheel = 21, Playground = 22, Bandstand = 23,
            CastleRuin = 24, Lookout = 25, Campsite = 26, WaterTower = 27, SignalBox = 28, Fuel = 29, Boulders = 30, Crag = 31,
            LogPile = 32, Lamp = 33, Bench = 34, FlowerBed = 35, Well = 36, Statue = 37, Signpost = 38, Picnic = 39, HayBales = 40;

        private static ActivitySpot Door(float x, float halfZ) => new(x, -(halfZ * 0.86f + 0.014f), SpotKind.Door);

        private static Blueprint[] Build()
        {
            var list = new List<Blueprint>
            {
                new() { Name = "Cottage", Short = "Cottage", Category = PlaceCategory.Homes, Half = new Vector2(0.061f, 0.05f), Residents = 1, Styles = 4, Spots = new[] { Door(-0.022f, 0.05f) } },
                new() { Name = "Townhouse", Short = "Town", Category = PlaceCategory.Homes, Half = new Vector2(0.05f, 0.047f), Height = 0.18f, Residents = 1, Styles = 4, Spots = new[] { Door(0f, 0.047f) } },
                new() { Name = "Thatched", Short = "Thatch", Category = PlaceCategory.Homes, Half = new Vector2(0.072f, 0.054f), Residents = 1, Styles = 4, Spots = new[] { Door(0f, 0.054f) } },
                new() { Name = "Station", Short = "Station", Category = PlaceCategory.Travel, Half = new Vector2(0.062f, 0.036f), Styles = 4, Hint = "passengers wait on its platform" },
                new() { Name = "Apartments", Short = "Flats", Category = PlaceCategory.Homes, Half = new Vector2(0.06f, 0.045f), Height = 0.2f, Residents = 2, Styles = 4, Spots = new[] { Door(0f, 0.045f) } },
                new() { Name = "Chalet", Short = "Chalet", Category = PlaceCategory.Homes, Half = new Vector2(0.058f, 0.05f), Height = 0.14f, Residents = 1, Styles = 4, Spots = new[] { Door(0.02f, 0.05f), new ActivitySpot(-0.03f, -0.058f, SpotKind.Sit, 180f) } },
                new() { Name = "Church", Short = "Church", Category = PlaceCategory.Town, Half = new Vector2(0.048f, 0.085f), Height = 0.3f, Styles = 2, Hint = "a place to gather",
                    Spots = new[] { Door(0f, 0.085f), new ActivitySpot(-0.025f, -0.105f, SpotKind.Gather), new ActivitySpot(0.025f, -0.108f, SpotKind.Gather), new ActivitySpot(0f, -0.12f, SpotKind.Gather) } },
                new() { Name = "Town hall", Short = "Hall", Category = PlaceCategory.Town, Half = new Vector2(0.075f, 0.05f), Height = 0.24f, Styles = 2, Hint = "people meet on its steps",
                    Spots = new[] { Door(0f, 0.05f), new ActivitySpot(-0.04f, -0.07f, SpotKind.Gather), new ActivitySpot(0.04f, -0.072f, SpotKind.Gather) } },
                new() { Name = "Bakery", Short = "Bakery", Category = PlaceCategory.Town, Half = new Vector2(0.05f, 0.047f), Height = 0.16f, Residents = 1, Styles = 4, Hint = "people pop in for bread",
                    Spots = new[] { Door(-0.014f, 0.047f), new ActivitySpot(0.022f, -0.06f, SpotKind.Browse) } },
                new() { Name = "Café", Short = "Café", Category = PlaceCategory.Town, Half = new Vector2(0.05f, 0.045f), Height = 0.15f, Styles = 4, Hint = "tables outside to sit at",
                    Spots = new[] { new ActivitySpot(-0.028f, -0.072f, SpotKind.Sit, 90f), new ActivitySpot(-0.006f, -0.072f, SpotKind.Sit, -90f), new ActivitySpot(0.022f, -0.075f, SpotKind.Sit, 90f), new ActivitySpot(0.044f, -0.075f, SpotKind.Sit, -90f), Door(-0.004f, 0.045f) } },
                new() { Name = "Grocer", Short = "Grocer", Category = PlaceCategory.Town, Half = new Vector2(0.05f, 0.045f), Height = 0.15f, Residents = 1, Styles = 4, Hint = "fruit and veg to browse",
                    Spots = new[] { new ActivitySpot(-0.026f, -0.068f, SpotKind.Browse), new ActivitySpot(0.026f, -0.068f, SpotKind.Browse), Door(0f, 0.045f) } },
                new() { Name = "School", Short = "School", Category = PlaceCategory.Town, Half = new Vector2(0.08f, 0.05f), Height = 0.2f, Styles = 2, Hint = "a yard to play in",
                    Spots = new[] { new ActivitySpot(-0.05f, -0.075f, SpotKind.Play), new ActivitySpot(-0.02f, -0.08f, SpotKind.Play), new ActivitySpot(0.02f, -0.078f, SpotKind.Play), Door(0.05f, 0.05f) } },
                new() { Name = "Fire station", Short = "Fire", Category = PlaceCategory.Town, Half = new Vector2(0.062f, 0.05f), Height = 0.2f, Styles = 1, Hint = "people stop to look at the engine",
                    Spots = new[] { new ActivitySpot(-0.012f, -0.075f, SpotKind.Watch), new ActivitySpot(0.03f, -0.072f, SpotKind.Watch) } },
                new() { Name = "Fountain", Short = "Fountain", Category = PlaceCategory.Town, Half = new Vector2(0.042f, 0.042f), Height = 0.08f, Styles = 1, Hint = "the village square",
                    Spots = Ring(0.058f, 5, SpotKind.Gather) },
                new() { Name = "Barn", Short = "Barn", Category = PlaceCategory.Farm, Half = new Vector2(0.072f, 0.055f), Height = 0.18f, Styles = 2, Spots = new[] { new ActivitySpot(0f, -0.07f, SpotKind.Browse) } },
                new() { Name = "Windmill", Short = "Mill", Category = PlaceCategory.Farm, Half = new Vector2(0.038f, 0.038f), Height = 0.26f, Styles = 1, RotorPivot = new Vector3(0f, 0.15f, -0.036f), RotorAxis = Vector3.forward, RotorSpeed = 40f,
                    Hint = "its sails turn", Spots = new[] { new ActivitySpot(0.03f, -0.06f, SpotKind.Watch, -20f) } },
                new() { Name = "Silo", Short = "Silo", Category = PlaceCategory.Farm, Half = new Vector2(0.03f, 0.03f), Height = 0.22f, Styles = 1 },
                new() { Name = "Greenhouse", Short = "Glass", Category = PlaceCategory.Farm, Half = new Vector2(0.055f, 0.035f), Height = 0.08f, Styles = 1, Spots = new[] { new ActivitySpot(0f, -0.05f, SpotKind.Browse) } },
                new() { Name = "Wheat field", Short = "Wheat", Category = PlaceCategory.Farm, Half = new Vector2(0.11f, 0.08f), Height = 0.03f, Styles = 3 },
                new() { Name = "Garden", Short = "Garden", Category = PlaceCategory.Farm, Half = new Vector2(0.07f, 0.055f), Height = 0.03f, Styles = 1, Spots = new[] { new ActivitySpot(0f, -0.068f, SpotKind.Browse) } },
                new() { Name = "Carousel", Short = "Carousel", Category = PlaceCategory.Fun, Half = new Vector2(0.06f, 0.06f), Height = 0.13f, Styles = 1, RotorPivot = Vector3.zero, RotorSpeed = 28f,
                    Hint = "visitors ride the horses", Spots = Concat(Ring(0.047f, 6, SpotKind.Ride, 0.012f, true), Ring(0.083f, 4, SpotKind.Watch)) },
                new() { Name = "Ferris wheel", Short = "Wheel", Category = PlaceCategory.Fun, Half = new Vector2(0.075f, 0.045f), Height = 0.3f, Styles = 1, Gondolas = 8, GondolaRadius = 0.122f, RotorPivot = new Vector3(0f, 0.16f, 0f), RotorAxis = Vector3.forward, RotorSpeed = 14f,
                    Hint = "a ride with a view", Spots = new[] { new ActivitySpot(0f, -0.06f, SpotKind.Ride), new ActivitySpot(-0.07f, -0.075f, SpotKind.Watch, 20f), new ActivitySpot(0.07f, -0.078f, SpotKind.Watch, -20f) } },
                new() { Name = "Playground", Short = "Play", Category = PlaceCategory.Fun, Half = new Vector2(0.07f, 0.055f), Height = 0.07f, Styles = 1, Walkable = true, Hint = "swings and a slide",
                    Spots = new[] { new ActivitySpot(-0.035f, -0.02f, SpotKind.Play, 0f), new ActivitySpot(0.035f, 0.01f, SpotKind.Play), new ActivitySpot(0f, 0.035f, SpotKind.Play), new ActivitySpot(-0.07f, -0.075f, SpotKind.Watch, 30f) } },
                new() { Name = "Bandstand", Short = "Band", Category = PlaceCategory.Fun, Half = new Vector2(0.045f, 0.045f), Height = 0.12f, Styles = 2, Hint = "people gather to listen",
                    Spots = Ring(0.07f, 6, SpotKind.Watch) },
                new() { Name = "Castle ruin", Short = "Castle", Category = PlaceCategory.Fun, Half = new Vector2(0.08f, 0.07f), Height = 0.22f, Styles = 1, Hint = "something to explore",
                    Spots = new[] { new ActivitySpot(0f, -0.09f, SpotKind.Watch), new ActivitySpot(0.09f, 0.02f, SpotKind.Watch, -90f), new ActivitySpot(-0.09f, -0.03f, SpotKind.Watch, 90f) } },
                new() { Name = "Lookout tower", Short = "Lookout", Category = PlaceCategory.Fun, Half = new Vector2(0.035f, 0.035f), Height = 0.3f, Styles = 1, Walkable = true, Hint = "climb up for the view",
                    Spots = new[] { new ActivitySpot(0f, 0f, SpotKind.Watch, 180f, 0.205f), new ActivitySpot(0.012f, 0.012f, SpotKind.Watch, -60f, 0.205f) } },
                new() { Name = "Campsite", Short = "Camp", Category = PlaceCategory.Fun, Half = new Vector2(0.07f, 0.06f), Height = 0.06f, Pad = false, Styles = 1, Walkable = true, Hint = "sit by the campfire",
                    Spots = Ring(0.03f, 4, SpotKind.Sit, 0f, false, new Vector2(0.01f, 0.005f)) },
                new() { Name = "Water tower", Short = "Water", Category = PlaceCategory.Travel, Half = new Vector2(0.035f, 0.035f), Height = 0.2f, Styles = 1 },
                new() { Name = "Signal box", Short = "Signals", Category = PlaceCategory.Travel, Half = new Vector2(0.036f, 0.03f), Height = 0.14f, Styles = 1, Spots = new[] { new ActivitySpot(0f, -0.045f, SpotKind.Watch, 180f) } },
                new() { Name = "Fuel station", Short = "Fuel", Category = PlaceCategory.Travel, Half = new Vector2(0.07f, 0.05f), Height = 0.12f, Styles = 1, Spots = new[] { new ActivitySpot(-0.03f, -0.07f, SpotKind.Browse), Door(0.035f, 0.05f) } },
                new() { Name = "Boulders", Short = "Rocks", Category = PlaceCategory.Nature, Half = new Vector2(0.045f, 0.04f), Height = 0.06f, Pad = false, Styles = 3 },
                new() { Name = "Crag", Short = "Crag", Category = PlaceCategory.Nature, Half = new Vector2(0.06f, 0.055f), Height = 0.18f, Pad = false, Styles = 2, Spots = new[] { new ActivitySpot(0f, -0.075f, SpotKind.Watch) } },
                new() { Name = "Log pile", Short = "Logs", Category = PlaceCategory.Nature, Half = new Vector2(0.04f, 0.025f), Height = 0.04f, Pad = false, Styles = 1, Spots = new[] { new ActivitySpot(0f, -0.035f, SpotKind.Sit, 180f) } },
                new() { Name = "Street lamp", Short = "Lamp", Category = PlaceCategory.Small, Half = new Vector2(0.008f, 0.008f), Height = 0.07f, Pad = false, Small = true, Styles = 1 },
                new() { Name = "Bench", Short = "Bench", Category = PlaceCategory.Small, Half = new Vector2(0.018f, 0.007f), Height = 0.02f, Pad = false, Small = true, Styles = 2, Spots = new[] { new ActivitySpot(-0.007f, -0.009f, SpotKind.Sit, 180f), new ActivitySpot(0.007f, -0.009f, SpotKind.Sit, 180f) } },
                new() { Name = "Flower bed", Short = "Flowers", Category = PlaceCategory.Small, Half = new Vector2(0.03f, 0.016f), Height = 0.02f, Pad = false, Small = true, Styles = 4 },
                new() { Name = "Well", Short = "Well", Category = PlaceCategory.Small, Half = new Vector2(0.016f, 0.016f), Height = 0.05f, Pad = false, Small = true, Styles = 1, Spots = Ring(0.03f, 3, SpotKind.Gather) },
                new() { Name = "Statue", Short = "Statue", Category = PlaceCategory.Small, Half = new Vector2(0.016f, 0.016f), Height = 0.06f, Pad = false, Small = true, Styles = 1, Spots = new[] { new ActivitySpot(0f, -0.035f, SpotKind.Watch) } },
                new() { Name = "Signpost", Short = "Sign", Category = PlaceCategory.Small, Half = new Vector2(0.008f, 0.008f), Height = 0.05f, Pad = false, Small = true, Styles = 1 },
                new() { Name = "Picnic table", Short = "Picnic", Category = PlaceCategory.Small, Half = new Vector2(0.02f, 0.02f), Height = 0.02f, Pad = false, Small = true, Styles = 1, Spots = new[] { new ActivitySpot(0f, -0.022f, SpotKind.Sit, 0f), new ActivitySpot(0f, 0.022f, SpotKind.Sit, 180f) } },
                new() { Name = "Hay bales", Short = "Hay", Category = PlaceCategory.Small, Half = new Vector2(0.028f, 0.02f), Height = 0.02f, Pad = false, Small = true, Styles = 1, Spots = new[] { new ActivitySpot(0f, -0.028f, SpotKind.Sit, 180f) } },
            };
            for (int i = 0; i < list.Count; i++) list[i].Id = i;
            return list.ToArray();
        }

        private static ActivitySpot[] Ring(float radius, int count, SpotKind kind, float y = 0f, bool tangent = false, Vector2 offset = default)
        {
            var spots = new ActivitySpot[count];
            for (int i = 0; i < count; i++)
            {
                float a = (i + 0.5f) / count * Mathf.PI * 2f;
                var p = new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius + offset;
                // Facing the centre (or along the ring for riders).
                float facing = Mathf.Atan2(-p.x, -p.y) * Mathf.Rad2Deg + (tangent ? 90f : 0f);
                spots[i] = new ActivitySpot(p.x, p.y, kind, facing, y);
            }
            return spots;
        }

        private static ActivitySpot[] Concat(ActivitySpot[] a, ActivitySpot[] b)
        {
            var r = new ActivitySpot[a.Length + b.Length];
            a.CopyTo(r, 0); b.CopyTo(r, a.Length);
            return r;
        }
    }
}
