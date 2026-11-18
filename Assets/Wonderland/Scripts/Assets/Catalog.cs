using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    public enum PlaceCategory { Homes, Town, Shops, Services, Farm, Fun, Travel, Nature, Small }

    /// <summary>What a place does with the village's produce: farms grow it, shops and the café sell it, stations ship it.</summary>
    public enum Goods { None, Grows, Sells, Ships }

    /// <summary>
    /// What a place makes or takes, for the lorries: produce from the farms, goods from the works, timber from the
    /// sawmill. A lorry carries its depot's kind from wherever it is ready to wherever it is taken.
    /// </summary>
    [Flags] public enum Cargo { None = 0, Produce = 1, Goods = 2, Timber = 4, Mail = 8 }

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
        public Need Serves = Need.None;      // what people come here for (homes: Rest)
        public float[] Rhythm;               // its own appeal through the day (see Needs.Appeal), instead of its need's: the disco at night
        public bool OwnClock;                // a turning part that isn't a ride (the disco's mirror ball)
        public bool Kids;                    // children love it (playground, football)
        public bool Base;                    // home of an emergency vehicle (ambulance, police car, fire engine)
        public float Opens, Closes = 24f;    // opening hours (visitors come only then); outdoor places never close
        public string Job;                   // the one person who works here ("baker"), or null
        public float ShiftFrom, ShiftTo;     // their working hours (by default from just before opening to closing)
        public bool WorksIndoors;            // the teacher in the school, the clerk in the town hall: lights on, nobody at the door
        public Goods Goods;
        public bool Boat;                    // not a building: a boat, set on a pond (see BoatSystem)
        // Capabilities: what the place offers the world's activities (who comes, what is carried, what lives here).
        public Cargo Makes, Takes;           // lorries collect what it makes, and bring what it takes
        public int Lorries;                  // lorries based here (by a road), carrying what it makes
        public int Taxis;                    // taxis waiting here for fares
        public int Tractors;                 // farm machines kept at its edge, worked by its farmer
        public bool Dwelling => Residents > 0;
        public bool Ride => Category == PlaceCategory.Fun && RotorSpeed != 0f && !OwnClock;
        public bool OpenAt(float hour) => Within(hour, Opens, Closes);
        public bool OnShift(float hour) => Job != null && Within(hour, ShiftFrom, ShiftTo);
        /// <summary>Hours that run past midnight are written past 24 (the disco: 20 to 26).</summary>
        private static bool Within(float hour, float from, float to) => (hour >= from && hour < to) || (to > 24f && hour < to - 24f);
        public bool Farm => Goods == Goods.Grows;
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

        public static readonly string[] CategoryNames = { "Homes", "Town", "Shops", "Services", "Farm", "Fun", "Travel", "Nature", "Small" };

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
            LogPile = 32, Lamp = 33, Bench = 34, FlowerBed = 35, Well = 36, Statue = 37, Signpost = 38, Picnic = 39, HayBales = 40,
            Park = 41, Museum = 42, Restaurant = 43, Disco = 44, Hospital = 45, Police = 46, Factory = 47, Football = 48, Studio = 49,
            Camera = 50, Balloon = 51, Jetty = 52, RowingBoat = 53, SailingBoat = 54, Steamer = 55, Halt = 56, TaxiRank = 57,
            Sawmill = 58, FurnitureShop = 59, IceCream = 60, Pub = 61, Hotel = 62, PostOffice = 63, Letterbox = 64, CarPark = 65, FreightYard = 66, Harbour = 67, Zoo = 68, FairyCastle = 69, Stadium = 70, Zebra = 71;

        /// <summary>A station of any size (the big one, or a country halt): trains stop beside it.</summary>
        public static bool IsStation(int variant) => variant == Station || variant == Halt || variant == FreightYard;

        /// <summary>The kind of boat a catalog item places (or -1 for anything else).</summary>
        public static int BoatOf(int variant) => variant >= RowingBoat && variant <= Steamer ? variant - RowingBoat : -1;

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
                new() { Name = "Bakery", Short = "Bakery", Category = PlaceCategory.Shops, Half = new Vector2(0.05f, 0.047f), Height = 0.16f, Residents = 1, Styles = 4, Hint = "people pop in for bread",
                    Spots = new[] { Door(-0.014f, 0.047f), new ActivitySpot(0.022f, -0.06f, SpotKind.Browse) } },
                new() { Name = "Café", Short = "Café", Category = PlaceCategory.Shops, Half = new Vector2(0.05f, 0.045f), Height = 0.15f, Styles = 4, Hint = "tables outside to sit at",
                    Spots = new[] { new ActivitySpot(-0.028f, -0.072f, SpotKind.Sit, 90f), new ActivitySpot(-0.006f, -0.072f, SpotKind.Sit, -90f), new ActivitySpot(0.022f, -0.075f, SpotKind.Sit, 90f), new ActivitySpot(0.044f, -0.075f, SpotKind.Sit, -90f), Door(-0.004f, 0.045f) } },
                new() { Name = "Grocer", Short = "Grocer", Category = PlaceCategory.Shops, Half = new Vector2(0.05f, 0.045f), Height = 0.15f, Residents = 1, Styles = 4, Hint = "fruit and veg to browse",
                    Spots = new[] { new ActivitySpot(-0.026f, -0.068f, SpotKind.Browse), new ActivitySpot(0.026f, -0.068f, SpotKind.Browse), Door(0f, 0.045f) } },
                new() { Name = "School", Short = "School", Category = PlaceCategory.Services, Half = new Vector2(0.08f, 0.05f), Height = 0.2f, Styles = 2, Hint = "a yard to play in",
                    Spots = new[] { new ActivitySpot(-0.05f, -0.075f, SpotKind.Play), new ActivitySpot(-0.02f, -0.08f, SpotKind.Play), new ActivitySpot(0.02f, -0.078f, SpotKind.Play), Door(0.05f, 0.05f) } },
                new() { Name = "Fire station", Short = "Fire", Category = PlaceCategory.Services, Half = new Vector2(0.062f, 0.05f), Height = 0.2f, Styles = 1, Hint = "people stop to look at the engine",
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
                // The livelier town (see HouseFactory.Town.cs).
                new() { Name = "Park", Short = "Park", Category = PlaceCategory.Town, Half = new Vector2(0.11f, 0.085f), Height = 0.07f, Styles = 1, Walkable = true, Hint = "a stroll, a bench, the duck pond",
                    Spots = new[] { new ActivitySpot(-0.055f, -0.048f, SpotKind.Sit, 180f), new ActivitySpot(-0.041f, -0.048f, SpotKind.Sit, 180f), new ActivitySpot(0.05f, 0.045f, SpotKind.Sit, 0f),
                        new ActivitySpot(0.064f, 0.045f, SpotKind.Sit, 0f), new ActivitySpot(0.02f, -0.012f, SpotKind.Watch, 60f), new ActivitySpot(-0.012f, 0.026f, SpotKind.Watch, 160f), new ActivitySpot(-0.07f, 0.03f, SpotKind.Gather, 90f) } },
                new() { Name = "Museum", Short = "Museum", Category = PlaceCategory.Town, Half = new Vector2(0.075f, 0.06f), Height = 0.12f, Styles = 2, Hint = "a dinosaur in the courtyard",
                    Spots = new[] { Door(-0.02f, 0.06f), new ActivitySpot(0.04f, -0.08f, SpotKind.Watch, 0f), new ActivitySpot(0.012f, -0.078f, SpotKind.Watch, 35f), new ActivitySpot(0.066f, -0.078f, SpotKind.Watch, -35f) } },
                new() { Name = "Restaurant", Short = "Dining", Category = PlaceCategory.Shops, Half = new Vector2(0.065f, 0.045f), Height = 0.13f, Residents = 1, Styles = 2, Hint = "dinner on the terrace",
                    Spots = new[] { Door(0f, 0.045f), new ActivitySpot(-0.046f, -0.072f, SpotKind.Sit, 90f), new ActivitySpot(-0.022f, -0.072f, SpotKind.Sit, -90f), new ActivitySpot(0.022f, -0.072f, SpotKind.Sit, 90f),
                        new ActivitySpot(0.046f, -0.072f, SpotKind.Sit, -90f), new ActivitySpot(-0.012f, -0.092f, SpotKind.Sit, 90f), new ActivitySpot(0.012f, -0.092f, SpotKind.Sit, -90f) } },
                new() { Name = "Disco", Short = "Disco", Category = PlaceCategory.Fun, Half = new Vector2(0.06f, 0.05f), Height = 0.12f, Styles = 1, Hint = "dancing after dark",
                    RotorPivot = new Vector3(0f, 0.098f, 0f), RotorSpeed = 50f, OwnClock = true,
                    Spots = new[] { Door(0f, 0.05f), new ActivitySpot(-0.035f, -0.068f, SpotKind.Gather), new ActivitySpot(-0.012f, -0.075f, SpotKind.Gather), new ActivitySpot(0.014f, -0.072f, SpotKind.Gather), new ActivitySpot(0.038f, -0.066f, SpotKind.Gather) } },
                new() { Name = "Hospital", Short = "Hospital", Category = PlaceCategory.Services, Half = new Vector2(0.085f, 0.06f), Height = 0.15f, Styles = 1, Base = true, Hint = "its ambulance answers calls",
                    Spots = new[] { Door(-0.02f, 0.06f), new ActivitySpot(0.04f, -0.075f, SpotKind.Sit, 180f) } },
                new() { Name = "Police station", Short = "Police", Category = PlaceCategory.Services, Half = new Vector2(0.06f, 0.05f), Height = 0.12f, Styles = 1, Base = true, Hint = "its patrol car keeps watch",
                    Spots = new[] { Door(0f, 0.05f) } },
                new() { Name = "Factory", Short = "Factory", Category = PlaceCategory.Services, Half = new Vector2(0.09f, 0.06f), Height = 0.18f, Styles = 1, Hint = "makes biscuits for the shops",
                    Spots = new[] { Door(0.04f, 0.06f), new ActivitySpot(-0.03f, -0.075f, SpotKind.Watch) } },
                new() { Name = "Football pitch", Short = "Football", Category = PlaceCategory.Fun, Half = new Vector2(0.11f, 0.075f), Height = 0.03f, Styles = 1, Walkable = true, Kids = true, Hint = "a match whenever there are players",
                    Spots = new[] { new ActivitySpot(-0.07f, 0f, SpotKind.Play, 90f), new ActivitySpot(-0.035f, -0.025f, SpotKind.Play, 90f), new ActivitySpot(-0.03f, 0.03f, SpotKind.Play, 90f),
                        new ActivitySpot(0.07f, 0f, SpotKind.Play, -90f), new ActivitySpot(0.035f, 0.025f, SpotKind.Play, -90f), new ActivitySpot(0.03f, -0.03f, SpotKind.Play, -90f),
                        new ActivitySpot(-0.04f, -0.088f, SpotKind.Watch), new ActivitySpot(0f, -0.09f, SpotKind.Watch), new ActivitySpot(0.04f, -0.088f, SpotKind.Watch) } },
                new() { Name = "Monitoring studio", Short = "Studio", Category = PlaceCategory.Services, Half = new Vector2(0.065f, 0.05f), Height = 0.11f, Styles = 1, Hint = "screens showing the camera feeds",
                    Spots = new[] { Door(0.045f, 0.05f), new ActivitySpot(-0.03f, -0.075f, SpotKind.Watch), new ActivitySpot(0.005f, -0.078f, SpotKind.Watch) } },
                new() { Name = "CCTV camera", Short = "Camera", Category = PlaceCategory.Small, Half = new Vector2(0.009f, 0.009f), Height = 0.09f, Pad = false, Small = true, Styles = 1, Hint = "select it to see its picture and turn it" },
                new() { Name = "Hot-air balloon", Short = "Balloon", Category = PlaceCategory.Fun, Half = new Vector2(0.05f, 0.05f), Height = 0.12f, Styles = 1, Hint = "flights over the countryside",
                    RotorSpeed = 0.001f, Spots = new[] { new ActivitySpot(0f, 0f, SpotKind.Ride, 0f, 0.01f), new ActivitySpot(0.004f, 0.004f, SpotKind.Ride, 90f, 0.01f), new ActivitySpot(-0.04f, -0.058f, SpotKind.Watch, 30f), new ActivitySpot(0.04f, -0.06f, SpotKind.Watch, -30f) } },
                // On the ponds: a landing stage for boat trips (its front end out over the water), and the boats themselves.
                new() { Name = "Landing stage", Short = "Jetty", Category = PlaceCategory.Travel, Half = new Vector2(0.018f, 0.066f), Height = 0.05f, Pad = false, Walkable = true, Styles = 1,
                    Hint = "boat trips - put it at a pond's edge, its end over the water",
                    Spots = new[] { new ActivitySpot(-0.007f, 0.03f, SpotKind.Ride, 180f, 0.008f), new ActivitySpot(0.007f, 0.03f, SpotKind.Ride, 180f, 0.008f),
                        new ActivitySpot(-0.007f, 0.045f, SpotKind.Ride, 180f, 0.008f), new ActivitySpot(0.007f, 0.045f, SpotKind.Ride, 180f, 0.008f) } },
                new() { Name = "Rowing boat", Short = "Rowing", Category = PlaceCategory.Travel, Half = new Vector2(0.008f, 0.025f), Height = 0.02f, Pad = false, Boat = true, Hint = "rows about the pond - put it on the water" },
                new() { Name = "Sailing boat", Short = "Sailing", Category = PlaceCategory.Travel, Half = new Vector2(0.01f, 0.035f), Height = 0.08f, Pad = false, Boat = true, Hint = "sails with the wind - put it on the water" },
                new() { Name = "Paddle steamer", Short = "Steamer", Category = PlaceCategory.Travel, Half = new Vector2(0.02f, 0.06f), Height = 0.045f, Pad = false, Boat = true, Hint = "takes six on a trip round the lake - put it on the water" },
                new() { Name = "Country halt", Short = "Halt", Category = PlaceCategory.Travel, Half = new Vector2(0.042f, 0.03f), Height = 0.07f, Styles = 1, Hint = "a little stop beside the line - trains call here too" },
                // The working world (see HouseFactory.Living.cs).
                new() { Name = "Taxi rank", Short = "Taxis", Category = PlaceCategory.Travel, Half = new Vector2(0.03f, 0.022f), Height = 0.045f, Pad = false, Small = true, Styles = 1,
                    Hint = "two taxis wait here - put it by a road; villagers going far hail them" },
                new() { Name = "Sawmill", Short = "Sawmill", Category = PlaceCategory.Services, Half = new Vector2(0.085f, 0.06f), Height = 0.09f, Styles = 1, Hint = "saws planks - its lorry takes them to the furniture maker",
                    Spots = new[] { new ActivitySpot(0.04f, -0.075f, SpotKind.Watch), new ActivitySpot(-0.03f, -0.078f, SpotKind.Watch, 20f) } },
                new() { Name = "Furniture shop", Short = "Furniture", Category = PlaceCategory.Shops, Half = new Vector2(0.05f, 0.044f), Height = 0.15f, Styles = 2, Hint = "chairs and tables made from the sawmill's planks",
                    Spots = new[] { Door(-0.018f, 0.044f), new ActivitySpot(0.018f, -0.056f, SpotKind.Browse) } },
                new() { Name = "Ice cream parlour", Short = "Ice cream", Category = PlaceCategory.Shops, Half = new Vector2(0.04f, 0.05f), Height = 0.1f, Styles = 1, Hint = "busy on summer afternoons - children love it",
                    Spots = new[] { Door(-0.014f, 0.035f), new ActivitySpot(-0.03f, -0.05f, SpotKind.Sit, 90f), new ActivitySpot(-0.018f, -0.05f, SpotKind.Sit, -90f), new ActivitySpot(0.018f, -0.05f, SpotKind.Sit, 90f), new ActivitySpot(0.03f, -0.05f, SpotKind.Sit, -90f) } },
                new() { Name = "Pub", Short = "Pub", Category = PlaceCategory.Shops, Half = new Vector2(0.06f, 0.058f), Height = 0.15f, Residents = 1, Styles = 2, Hint = "where the village gathers in the evening",
                    Spots = new[] { Door(-0.02f, 0.046f), new ActivitySpot(-0.03f, -0.058f, SpotKind.Sit, 0f), new ActivitySpot(-0.03f, -0.07f, SpotKind.Sit, 180f), new ActivitySpot(0.018f, -0.058f, SpotKind.Sit, 0f), new ActivitySpot(0.018f, -0.07f, SpotKind.Sit, 180f), new ActivitySpot(0.045f, -0.062f, SpotKind.Gather) } },
                new() { Name = "Hotel", Short = "Hotel", Category = PlaceCategory.Town, Half = new Vector2(0.072f, 0.048f), Height = 0.2f, Residents = 3, Styles = 2, Hint = "guests stay and go sightseeing",
                    Spots = new[] { Door(-0.026f, 0.048f), new ActivitySpot(0.03f, -0.06f, SpotKind.Sit, 180f), new ActivitySpot(0.045f, -0.06f, SpotKind.Sit, 180f) } },
                new() { Name = "Post office", Short = "Post", Category = PlaceCategory.Services, Half = new Vector2(0.05f, 0.044f), Height = 0.15f, Styles = 2, Hint = "its van empties the letterboxes and delivers the post",
                    Spots = new[] { Door(-0.018f, 0.044f), new ActivitySpot(0.018f, -0.056f, SpotKind.Browse) } },
                new() { Name = "Letterbox", Short = "Letters", Category = PlaceCategory.Small, Half = new Vector2(0.006f, 0.006f), Height = 0.025f, Pad = false, Small = true, Styles = 1, Hint = "the post van empties it on its round",
                    Spots = new[] { new ActivitySpot(0f, -0.012f, SpotKind.Browse) } },
                new() { Name = "Car park", Short = "Parking", Category = PlaceCategory.Travel, Half = new Vector2(0.082f, 0.052f), Height = 0.01f, Walkable = true, Styles = 1, Hint = "visitors' cars park here - put it by a road near the shops" },
                new() { Name = "Freight yard", Short = "Freight", Category = PlaceCategory.Travel, Half = new Vector2(0.075f, 0.04f), Height = 0.1f, Styles = 1,
                    Hint = "beside the railway: its crane loads goods trains, and its lorry takes what arrives on" },
                new() { Name = "Harbour", Short = "Harbour", Category = PlaceCategory.Travel, Half = new Vector2(0.075f, 0.05f), Height = 0.1f, Styles = 1,
                    Hint = "on the shore of a big lake, front to the water: container ships sail to any other harbour the water reaches",
                    Spots = new[] { new ActivitySpot(0.05f, -0.035f, SpotKind.Watch, 0f) } },
                new() { Name = "Zoo", Short = "Zoo", Category = PlaceCategory.Fun, Half = new Vector2(0.122f, 0.092f), Height = 0.05f, Walkable = true, Styles = 1, Kids = true,
                    Hint = "giraffes, elephants, lions, zebras and penguins - families come to see them",
                    Spots = new[] { new ActivitySpot(-0.07f, -0.003f, SpotKind.Watch, 0f), new ActivitySpot(0.03f, 0.008f, SpotKind.Watch, 0f), new ActivitySpot(-0.022f, -0.045f, SpotKind.Watch, -90f),
                        new ActivitySpot(0.032f, -0.025f, SpotKind.Watch, 90f), new ActivitySpot(-0.005f, -0.061f, SpotKind.Watch, 0f), new ActivitySpot(0.012f, -0.061f, SpotKind.Watch, -20f),
                        new ActivitySpot(0.075f, -0.097f, SpotKind.Sit, 0f), new ActivitySpot(0.095f, -0.097f, SpotKind.Sit, 0f) } },
                new() { Name = "Fairy-tale castle", Short = "Fairy", Category = PlaceCategory.Fun, Half = new Vector2(0.118f, 0.095f), Height = 0.3f, Styles = 1, Kids = true,
                    Hint = "visitors come to gaze at its towers",
                    Spots = new[] { new ActivitySpot(-0.05f, -0.112f, SpotKind.Watch, 0f), new ActivitySpot(-0.015f, -0.118f, SpotKind.Watch, 0f), new ActivitySpot(0.02f, -0.116f, SpotKind.Watch, 0f),
                        new ActivitySpot(0.055f, -0.11f, SpotKind.Watch, 0f), new ActivitySpot(0.13f, -0.03f, SpotKind.Watch, -90f), new ActivitySpot(-0.13f, 0.01f, SpotKind.Watch, 90f) } },
                new() { Name = "Football stadium", Short = "Stadium", Category = PlaceCategory.Fun, Half = new Vector2(0.135f, 0.1f), Height = 0.09f, Styles = 1,
                    Hint = "matches at three and half past seven: the crowd fills the stands",
                    Spots = new[] { new ActivitySpot(-0.06f, -0.105f, SpotKind.Watch, 0f), new ActivitySpot(-0.02f, -0.106f, SpotKind.Watch, 0f), new ActivitySpot(0.02f, -0.106f, SpotKind.Watch, 0f),
                        new ActivitySpot(0.06f, -0.105f, SpotKind.Watch, 0f), new ActivitySpot(-0.04f, 0.106f, SpotKind.Watch, 180f), new ActivitySpot(0.04f, 0.106f, SpotKind.Watch, 180f) } },
            };
            list.Add(new() { Name = "Zebra crossing", Short = "Zebra", Category = PlaceCategory.Small, Half = new Vector2(0.034f, 0.011f), Height = 0.04f, Pad = false, Small = true, Styles = 1,
                Hint = "point at a street: it goes across it - people cross here, cars stop for them" });
            for (int i = 0; i < list.Count; i++) list[i].Id = i;
            // What each place is for: the needs it satisfies decide who comes and when (see Needs).
            foreach (var b in list)
                b.Serves = b.Id switch
                {
                    Bakery or Grocer or Fuel or Barn or Greenhouse or Garden or FurnitureShop or PostOffice or Letterbox => Need.Errands,
                    Cafe or Picnic or Restaurant or IceCream => Need.Food,
                    Church or TownHall or Fountain or Well or Bandstand or Campsite or Pub => Need.Social,
                    Carousel or FerrisWheel or Playground or School or Disco or Football or Jetty or Stadium => Need.Fun,
                    CastleRuin or Lookout or Crag or Windmill or FireStation or Statue or SignalBox or Park or Museum or Factory or Balloon or Studio or Sawmill or Zoo or FairyCastle => Need.Sights,
                    Bench or LogPile or HayBales => Need.Rest,
                    _ => b.Category == PlaceCategory.Homes ? Need.Rest : Need.None,
                };
            // Opening hours, jobs and goods: the working day of the village (see PeopleSystem.Work.cs).
            void Hours(int id, float opens, float closes) { list[id].Opens = opens; list[id].Closes = closes; }
            void Work(int id, string job, float from, float to, bool indoors = false, Goods goods = Goods.None)
            { var b = list[id]; b.Job = job; b.ShiftFrom = from; b.ShiftTo = to; b.WorksIndoors = indoors; b.Goods = goods; }
            Hours(Bakery, 6.5f, 18f); Work(Bakery, "baker", 6f, 18f, false, Goods.Sells);
            Hours(Grocer, 7f, 18.5f); Work(Grocer, "grocer", 6.5f, 18.5f, false, Goods.Sells);
            Hours(Cafe, 8f, 21.5f); Work(Cafe, "café owner", 7.5f, 21.5f, false, Goods.Sells);
            Hours(Fuel, 7f, 19f); Work(Fuel, "attendant", 7f, 19f);
            Hours(School, 8f, 17f); Work(School, "teacher", 7.5f, 15.5f, true);
            Work(TownHall, "clerk", 8.5f, 17f, true);
            Work(FireStation, "firefighter", 7f, 19f);
            Work(Station, "station master", 6f, 21f, false, Goods.Ships);
            Work(SignalBox, "signalman", 6f, 21.5f, true);
            Work(WheatField, "farmer", 6f, 17f, false, Goods.Grows);
            Work(Garden, "gardener", 6.5f, 16.5f, false, Goods.Grows);
            Work(Greenhouse, "grower", 7f, 17f, false, Goods.Grows);
            Work(Barn, "farmer", 5.5f, 17f, false, Goods.Grows);
            Work(Windmill, "miller", 7f, 17f, false, Goods.Grows);
            Hours(Carousel, 10f, 20f); Hours(FerrisWheel, 10f, 20f); Hours(Playground, 8f, 20.5f);
            Hours(Barn, 7f, 18f); Hours(Greenhouse, 8f, 17f); Hours(Garden, 8f, 18f);
            Hours(Museum, 9f, 17.5f); Work(Museum, "curator", 8.5f, 17.5f, true);
            Hours(Restaurant, 11.5f, 23f); Work(Restaurant, "waiter", 11f, 23f, false, Goods.Sells);
            Hours(Disco, 20f, 25.5f); Work(Disco, "DJ", 19.5f, 25.5f, true);
            Work(Hospital, "doctor", 7f, 20f, true);
            Work(Police, "police officer", 7f, 21f, true);
            Work(Factory, "factory hand", 7f, 17f, true, Goods.Grows);
            Hours(Jetty, 9f, 19.5f);
            Hours(Football, 9f, 20f); Hours(Balloon, 9f, 19f); Hours(Park, 6f, 22f); Hours(Factory, 8f, 16f);
            Work(Studio, "watchman", 8f, 22f, true);
            Work(Sawmill, "sawyer", 7f, 17f, false, Goods.Grows); Hours(Sawmill, 8f, 16f);
            Hours(FurnitureShop, 9f, 17.5f); Work(FurnitureShop, "cabinet maker", 8.5f, 17.5f, false, Goods.Sells);
            Hours(IceCream, 11f, 19.5f); Work(IceCream, "ice cream seller", 10.5f, 19.5f, false, Goods.Sells);
            Hours(Pub, 16f, 24.5f); Work(Pub, "landlord", 15.5f, 24.5f, false, Goods.Sells);
            Work(Hotel, "hotel keeper", 7f, 21f);
            Hours(PostOffice, 9f, 17.5f); Work(PostOffice, "postmaster", 8f, 17.5f);
            Hours(Zoo, 9f, 18f); Work(Zoo, "zookeeper", 8f, 18f);
            Hours(Stadium, 10f, 21.5f); Work(Stadium, "groundsman", 9f, 18f);
            // Their own rhythms: supper in the evening, dancing after dark.
            list[Restaurant].Rhythm = new[] { 0f, 0f, 0f, 0.05f, 0.85f, 0.35f, 1f, 0.8f, 0f };
            list[Disco].Rhythm = new[] { 0.9f, 0f, 0f, 0f, 0f, 0f, 0.3f, 1f, 1f };
            list[IceCream].Rhythm = new[] { 0f, 0f, 0f, 0.1f, 0.6f, 1f, 0.7f, 0.2f, 0f };   // afternoons
            list[Pub].Rhythm = new[] { 0.5f, 0f, 0f, 0f, 0.15f, 0.25f, 0.8f, 1f, 1f };     // evenings
            list[IceCream].Kids = true;
            list[Playground].Kids = true;
            // What is carried where: farms make produce, the factory goods; kitchens and shops take them in, and the
            // station sends them on by goods train. Lorries live at the barn and the factory.
            foreach (int id in new[] { Barn, WheatField, Garden, Greenhouse, Windmill }) list[id].Makes = Cargo.Produce;
            list[Factory].Makes = Cargo.Goods;
            foreach (int id in new[] { Bakery, Cafe, Restaurant, IceCream, Pub }) list[id].Takes = Cargo.Produce;
            list[Sawmill].Makes = Cargo.Timber; list[Sawmill].Lorries = 1;
            list[FurnitureShop].Takes = Cargo.Timber;

            Work(FreightYard, "crane driver", 6.5f, 19.5f, false, Goods.Ships);
            list[FreightYard].Takes = list[FreightYard].Makes = Cargo.Produce | Cargo.Goods | Cargo.Timber;
            list[FreightYard].Lorries = 1;
            Work(Harbour, "dock worker", 6f, 20f, false, Goods.Ships);
            list[Harbour].Takes = Cargo.Produce | Cargo.Goods | Cargo.Timber; list[Harbour].Makes = Cargo.Goods; list[Harbour].Lorries = 1;
            list[Grocer].Takes = Cargo.Produce | Cargo.Goods;
            list[Station].Goods = Goods.None;   // (passengers only: goods trains call at freight yards)
            list[Barn].Lorries = 1; list[Factory].Lorries = 1;
            list[Station].Taxis = 1; list[TaxiRank].Taxis = 2;
            list[WheatField].Tractors = 1;
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
