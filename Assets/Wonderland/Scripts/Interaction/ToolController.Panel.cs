using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// What the palette shows for the tool in hand. The palette's row holds only the tool's groups of options
    /// ("Shape", "Land" for Terrain; "Streets", "Paths", "Fences", "Vehicles" for Roads) and a few switches; the detail
    /// panel on the left lists the chosen group's options, each with a sentence on what it does, and how the left stick
    /// adjusts it. Build keeps its library there instead (category, family, then the objects).
    /// </summary>
    public sealed partial class ToolController
    {
        // The group of options open for each tool (where it isn't simply the group of what is chosen).
        private readonly Dictionary<Tool, int> openGroup = new();

        private sealed class Group
        {
            public string Name, About;
            public readonly List<PaletteUI.DetailRow> Rows = new();
            public Action Opened;   // (choosing the group: pick something in it, if what is chosen isn't)
        }

        private static PaletteUI.Option Choice(string label, bool on, Action click, string tip = null) => new(label, on, click) { Kind = OptionKind.Choice, Tip = tip };
        private static PaletteUI.Option Act(string label, Action click, bool enabled = true, string tip = null) => new(label, false, click, enabled) { Kind = OptionKind.Action, Tip = tip };
        private static PaletteUI.Option Switch(string label, bool on, Action click, string tip = null) => new(label, on, click) { Kind = OptionKind.Toggle, Tip = tip };
        private static PaletteUI.Option Note(string label) => new(label, false, null) { Kind = OptionKind.Action };
        private static PaletteUI.DetailRow Row(PaletteUI.Option o, string about) => new(o, about);

        private void RefreshOptions()
        {
            var o = new List<PaletteUI.Option>();
            var items = new List<PaletteUI.Option>();
            string caption = null, footer = null;
            var groups = new List<Group>();
            int open = 0;
            switch (Tool)
            {
                case Tool.Terrain:
                {
                    caption = "Brush";
                    var shape = new Group { Name = "Shape", About = "Raise hills, dig valleys, smooth and flatten the land", Opened = () => { if (TerrainMode is TerrainBrush.Grow or TerrainBrush.Trim) TerrainMode = TerrainBrush.Raise; } };
                    var land = new Group { Name = "Land", About = "Make the land bigger or smaller", Opened = () => { if (TerrainMode is not (TerrainBrush.Grow or TerrainBrush.Trim)) TerrainMode = TerrainBrush.Grow; } };
                    void Brush(Group g, TerrainBrush b, string about) => g.Rows.Add(Row(Choice(b.ToString(), TerrainMode == b, () => { TerrainMode = b; RefreshOptions(); }), about));
                    Brush(shape, TerrainBrush.Raise, "Lifts the land under the pointer: hold the trigger and sweep for a hill");
                    Brush(shape, TerrainBrush.Lower, "Pushes the land down: valleys, hollows, a basin for a pond");
                    Brush(shape, TerrainBrush.Smooth, "Softens bumps, steps and sharp edges");
                    Brush(shape, TerrainBrush.Flatten, "Levels the land at the height you start on: plots, fields, a station yard");
                    Brush(shape, TerrainBrush.Peaks, "Grows rugged mountains with craggy rock; high tops get snow");
                    Brush(land, TerrainBrush.Grow, "Paint over the floor beyond the shore: new land grows there");
                    Brush(land, TerrainBrush.Trim, "Paint along the shore: the land shrinks back");
                    groups.Add(shape); groups.Add(land);
                    open = TerrainMode is TerrainBrush.Grow or TerrainBrush.Trim ? 1 : 0;
                    footer = $"Brush {Radius * 100f:0} cm, strength {strength * 100f:0}%  ·  left stick ↔ size, ↕ strength";
                    break;
                }
                case Tool.Water:
                {
                    caption = "Water";
                    var ponds = new Group { Name = "Ponds", About = "Dig ponds and lakes that fill with water", Opened = () => { Draining = false; StreamMode = false; } };
                    var streams = new Group { Name = "Streams", About = "Draw creeks, streams and rivers that flow downhill", Opened = () => { Draining = false; StreamMode = true; } };
                    var drain = new Group { Name = "Drain", About = "Take water away again", Opened = () => { Draining = true; StreamMode = false; } };
                    bool pond = !Draining && !StreamMode;
                    ponds.Rows.Add(Row(Choice("Pond", pond && Radius <= 0.3f, () => { Draining = false; StreamMode = false; radius[(int)Tool.Water] = 0.12f; RefreshOptions(); }), "Hold the trigger where it should be: it digs a basin that fills. Start inside a pond to make it bigger"));
                    ponds.Rows.Add(Row(Choice("Lake", pond && Radius > 0.3f, () => { Draining = false; StreamMode = false; radius[(int)Tool.Water] = 0.38f; RefreshOptions(); }), "The same with a big brush: open water with room for boats and container ships"));
                    void Width(string name, float w, string about) => streams.Rows.Add(Row(Choice(name, StreamMode && Mathf.Abs(StreamWidth - w) < 0.01f, () => { Draining = false; StreamMode = true; StreamWidth = w; RefreshOptions(); }), about));
                    Width("Creek", 0.6f, "Narrow and shallow. Draw a line: it carves its bed and flows downhill");
                    Width("Stream", 1f, "Draw a line: it flows downhill, the railway bridges it. Draw on from its end to make it longer");
                    Width("River", 2.2f, "Broad and deep: lead it into a lake. Start a stroke on its side for a branch");
                    drain.Rows.Add(Row(Choice("Drain", Draining, () => { Draining = true; StreamMode = false; RefreshOptions(); }), "Hold the trigger over a pond to drain it; tap a stream to take it away"));
                    groups.Add(ponds); groups.Add(streams); groups.Add(drain);
                    open = Draining ? 2 : StreamMode ? 1 : 0;
                    footer = StreamMode ? "Draw with the trigger held; let go to make it" : $"Brush {Radius * 100f:0} cm  ·  left stick ↔ size";
                    break;
                }
                case Tool.Forest:
                {
                    caption = "Trees";
                    var woods = new Group { Name = "Woods", About = "Kinds of wood to plant: mixed, leafy, pine, birch, autumn", Opened = () => { ForestErase = false; if ((int)ForestType > 4) ForestType = ForestType.Mixed; } };
                    var special = new Group { Name = "Special", About = "Orchards in rows, willows by the water, palms", Opened = () => { ForestErase = false; if ((int)ForestType <= 4) ForestType = ForestType.Orchard; } };
                    var thick = new Group { Name = "Density", About = "How close together the trees grow", Opened = () => ForestErase = false };
                    var clear = new Group { Name = "Clear", About = "Take trees away", Opened = () => ForestErase = true };
                    for (int k = 0; k < ForestSystem.ForestNames.Length; k++)
                    {
                        var type = (ForestType)k;
                        (k <= 4 ? woods : special).Rows.Add(Row(Choice(ForestSystem.ForestNames[k], !ForestErase && ForestType == type, () => { ForestErase = false; ForestType = type; RefreshOptions(); }), ForestSystem.ForestAbout[k]));
                    }
                    void Density(string name, float d, string about) => thick.Rows.Add(Row(Choice(name, !ForestErase && Mathf.Abs(density - d) < 0.12f, () => { ForestErase = false; density = d; RefreshOptions(); }), about));
                    Density("Sparse", 0.3f, "A few trees scattered over meadows");
                    Density("Wood", 0.6f, "Trees with clearings between");
                    Density("Dense", 0.95f, "A thick forest, trees close together");
                    clear.Rows.Add(Row(Choice("Clear trees", ForestErase, () => { ForestErase = true; RefreshOptions(); }), "Hold the trigger over trees to clear them"));
                    groups.Add(woods); groups.Add(special); groups.Add(thick); groups.Add(clear);
                    open = ForestErase ? 3 : (int)ForestType > 4 ? 1 : 0;
                    footer = $"Brush {Radius * 100f:0} cm, density {density * 100f:0}%  ·  left stick ↔ size, ↕ density";
                    break;
                }
                case Tool.Paths:
                {
                    caption = "Draw";
                    var streets = new Group { Name = "Streets", About = "Roads for cars, buses and lorries", Opened = () => { PlacingVehicle = null; if (!PathSystem.IsCarWay((int)PathKind)) PathKind = PathKind.Road; } };
                    var paths = new Group { Name = "Paths", About = "Ways for people on foot", Opened = () => { PlacingVehicle = null; if (PathKind is not (PathKind.Footpath or PathKind.Trail)) PathKind = PathKind.Footpath; } };
                    var fences = new Group { Name = "Fences", About = "Fences, hedges and walls", Opened = () => { PlacingVehicle = null; if (!PathSystem.IsBarrier((int)PathKind)) PathKind = PathKind.Fence; } };
                    var vehicles = new Group { Name = "Vehicles", About = "Put a car, bus or lorry on a road", Opened = () => PlacingVehicle ??= VehicleKind.Car };
                    void Way(Group g, PathKind k, string about) => g.Rows.Add(Row(Choice(PathSystem.Names[(int)k], PathKind == k && PlacingVehicle == null, () => { PathKind = k; PlacingVehicle = null; RefreshOptions(); }), about));
                    Way(streets, PathKind.Road, "A town street with kerbs and pavements. Cross another for a crossroads with lights; end on one for a T-junction");
                    Way(streets, PathKind.Highway, "Two lanes each way, for traffic between towns");
                    Way(streets, PathKind.Lane, "A narrow country lane without pavements");
                    Way(streets, PathKind.ServiceRoad, "A narrow concrete road for harbours, yards and depots");
                    Way(paths, PathKind.Footpath, "A gravel path for people on foot. Across water it gets a footbridge");
                    Way(paths, PathKind.Trail, "A trodden earth trail through the countryside");
                    Way(fences, PathKind.Fence, "A wooden fence. Close it into a loop for a pasture that keeps animals in");
                    Way(fences, PathKind.Hedge, "A green hedge");
                    Way(fences, PathKind.Wall, "A dry stone wall");
                    Way(fences, PathKind.SecurityFence, "Chain-link with barbed wire, for a harbour or a yard");
                    // Every style of car and bus, then the rest.
                    void Vehicle(VehicleKind k, int style, string name, string about) => vehicles.Rows.Add(Row(Choice(name, PlacingVehicle == k && (vehicleStyle == style || style < 0),
                        () => { PlacingVehicle = k; vehicleStyle = Mathf.Max(0, style); RefreshOptions(); Say($"Point at a road and pull the trigger: a {name.ToLower()} drives off about the town"); }), about));
                    foreach (var vk in VehicleFactory.Placeable)
                    {
                        if (vk == VehicleKind.Car) for (int s = 0; s < VehicleFactory.CarStyles; s++) Vehicle(vk, s, VehicleFactory.CarStyleNames[s], VehicleFactory.CarStyleAbout[s] + ". Each one a different colour");
                        else if (vk == VehicleKind.Bus) for (int s = 0; s < VehicleFactory.BusStyles; s++) Vehicle(vk, s, VehicleFactory.BusStyleNames[s], VehicleFactory.BusStyleAbout[s]);
                        else Vehicle(vk, -1, VehicleName(vk), $"Pull the trigger on a road: a {VehicleName(vk).ToLower()} drives off about the town, the way you point");
                    }
                    groups.Add(streets); groups.Add(paths); groups.Add(fences); groups.Add(vehicles);
                    open = PlacingVehicle != null ? 3 : PathSystem.IsBarrier((int)PathKind) ? 2 : PathKind is PathKind.Footpath or PathKind.Trail ? 1 : 0;
                    o.Add(GridChip());
                    footer = PlacingVehicle != null ? "The way you point is the way it faces" : "Hold the trigger and draw; let go to build it";
                    break;
                }
                case Tool.Track:
                {
                    caption = "Railway";
                    var draw = new Group { Name = "Draw", About = "Lay track: loops, branches and links", Opened = () => SignalMode = false };
                    var signals = new Group { Name = "Signals", About = "Put up signals that hold trains", Opened = () => SignalMode = true };
                    draw.Rows.Add(Row(Choice("Draw railway", !SignalMode, () => { SignalMode = false; RefreshOptions(); }), "Hold the trigger and draw. Back to the start makes a loop; start on a track to branch, end on one to join"));
                    draw.Rows.Add(Row(Note("Bend it"), "Grip on the track and move: that piece bends like a rope"));
                    draw.Rows.Add(Row(Note("Switches"), "Tap a switch (where tracks part) to throw it"));
                    signals.Rows.Add(Row(Choice("Signals", SignalMode, () => { SignalMode = true; RefreshOptions(); }), "Pull the trigger beside the track to put up a signal; tap one to hold trains at red"));
                    groups.Add(draw); groups.Add(signals);
                    open = SignalMode ? 1 : 0;
                    o.Add(GridChip());
                    o.Add(new PaletteUI.Option("Remove railway", false, () => { if (World.Track.HasTrack) { World.RemoveTrack(); Say("Railway removed - Undo brings it back"); RefreshOptions(); } }, World.Track.HasTrack)
                        { Kind = OptionKind.Danger, Tip = "Takes away the whole railway and its trains. Undo (or B) brings it back." });
                    break;
                }
                case Tool.Train:
                {
                    caption = "Trains";
                    var trains = new Group { Name = "Trains", About = "Pick a train, then pull the trigger on the track" };
                    var running = new Group { Name = "Running", About = "Start and stop the trains, and how they choose at switches" };
                    var cars = new Group { Name = "Carriages", About = "Make the train you last pointed at longer or shorter" };
                    foreach (var (kind, label, about) in new[] { (TrainKind.Passenger, "Steam", "A steam engine with passenger carriages"), (TrainKind.Express, "Express", "A fast passenger train"), (TrainKind.Tank, "Tank", "A little tank engine, good on branch lines"), (TrainKind.Goods, "Goods", "Wagons that carry freight between stations and yards"), (TrainKind.Railcar, "Railcar", "A single carriage that drives itself"), (TrainKind.HighSpeed, "High-speed", "A sleek white train with a power car at each end: the fastest"), (TrainKind.Commuter, "Commuter", "An electric unit for the stations round town: doors in the livery, a pantograph on the roof"), (TrainKind.DieselGoods, "Container", "A freight diesel with flat wagons: containers to and from the freight yards") })
                    {
                        var k = kind;
                        trains.Rows.Add(Row(Choice(label, TrainKind == k, () => { TrainKind = k; RefreshOptions(); }), about));
                    }
                    bool run = World.Trains.AnyRunning;
                    running.Rows.Add(Row(Act(run ? "Stop all" : "Start all", ToggleAll, World.Trains.Any), run ? "Every train stops at once" : "Every train sets off"));
                    foreach (var (mode, label, about) in new[] { (SwitchMode.Explore, "Spread", "At a switch each train takes the way with fewer trains ahead: they spread over the whole railway"), (SwitchMode.Random, "Random", "At a switch each train picks a way at random"), (SwitchMode.Manual, "Manual", "Switches stay as you set them: tap one to throw it") })
                    {
                        var m = mode;
                        running.Rows.Add(Row(Choice(label, World.Trains.Mode == m, () => { World.Trains.Mode = m; RefreshOptions(); }), about));
                    }
                    var chosen = lastTrain != null && World.Trains.Trains.Contains(lastTrain) ? lastTrain : null;
                    cars.Rows.Add(Row(Act("+ Carriage", () => Couple(chosen, 1), chosen != null), chosen != null ? "One more carriage on the train you last pointed at" : "Point at a train first"));
                    cars.Rows.Add(Row(Act("- Carriage", () => Couple(chosen, -1), chosen != null), chosen != null ? "One carriage fewer" : "Point at a train first"));
                    groups.Add(trains); groups.Add(running); groups.Add(cars);
                    footer = hoveredTrain != null ? "On a train: trigger stop / go, left stick ↕ speed, ↔ carriages" : "Point at a train to stop it, speed it up or pick it up (grip)";
                    break;
                }
                case Tool.Houses:
                    // The library: a category, then a family (the second row), then the object (its tile beside the palette).
                    caption = "Category";
                    foreach (var cat in Catalog.CategoryOrder)
                    {
                        var c = cat;
                        o.Add(Choice(Catalog.CategoryNames[(int)c], BuildCategory == c, () => SelectCategory(c), CategoryTip(c)));
                    }
                    var families = Catalog.Families(BuildCategory);
                    if (!families.Contains(BuildFamily)) BuildFamily = Catalog.Get(HouseVariant).Category == BuildCategory ? Catalog.Get(HouseVariant).Family : families[0];
                    foreach (var family in families)
                    {
                        string f = family;
                        items.Add(Choice(f, BuildFamily == f, () => SelectFamily(f)));
                    }
                    var tiles = new List<PaletteUI.LibraryTile>();
                    foreach (int id in Catalog.InFamily(BuildCategory, BuildFamily))
                    {
                        int v = id;
                        tiles.Add(new PaletteUI.LibraryTile { Id = id, Label = Catalog.Get(id).Name, Model = HouseFactory.Preview(id), Selected = HouseVariant == id && !carrying,
                            OnClick = () => { HouseVariant = v; carrying = false; houseStyle %= Mathf.Max(1, Catalog.Get(v).Styles); RefreshOptions(); } });
                    }
                    Palette.ShowLibrary($"{Catalog.CategoryNames[(int)BuildCategory]} · {BuildFamily}", tiles, Catalog.Info);
                    break;
                case Tool.Decor:
                {
                    caption = "Life";
                    var people = new Group { Name = "People", About = "Villagers with homes, jobs and days of their own", Opened = () => DecorKind = Villager };
                    var farm = new Group { Name = "Farm", About = "Farm animals: they graze, and stay in fenced pastures", Opened = () => { if (DecorKind is not ((int)AnimalKind.Sheep or (int)AnimalKind.Cow or (int)AnimalKind.Horse or (int)AnimalKind.Pig or (int)AnimalKind.Chicken or (int)AnimalKind.Duck)) DecorKind = (int)AnimalKind.Sheep; } };
                    var wild = new Group { Name = "Pets & wild", About = "Dogs and cats live with families; deer roam the woods", Opened = () => { if (DecorKind is not ((int)AnimalKind.Dog or (int)AnimalKind.Cat or (int)AnimalKind.Deer)) DecorKind = (int)AnimalKind.Dog; } };
                    var time = new Group { Name = "Time", About = "How fast the days go by" };
                    var events = new Group { Name = "Events", About = "Make something happen in the village now" };
                    var weather = new Group { Name = "Weather", About = "Clouds and rain" };
                    people.Rows.Add(Row(Choice("Villager", DecorKind == Villager, () => { DecorKind = Villager; RefreshOptions(); }), "Pull the trigger where they should appear. Each finds a home and a job and lives their day"));
                    void Animal(Group g, AnimalKind k, string about) => g.Rows.Add(Row(Choice(AnimalFactory.Names[(int)k], DecorKind == (int)k, () => { DecorKind = (int)k; RefreshOptions(); }), about));
                    Animal(farm, AnimalKind.Sheep, "Hold the trigger and sweep for a flock");
                    Animal(farm, AnimalKind.Cow, "Grazes in pastures");
                    Animal(farm, AnimalKind.Horse, "Gallops about its paddock");
                    Animal(farm, AnimalKind.Pig, "Snuffles round the farmyard");
                    Animal(farm, AnimalKind.Chicken, "Pecks about the yard");
                    Animal(farm, AnimalKind.Duck, "Pull the trigger on a pond: ducks paddle about");
                    Animal(wild, AnimalKind.Dog, "Lives with a family and goes for walks with them");
                    Animal(wild, AnimalKind.Cat, "Prowls round its home, sits on walls");
                    Animal(wild, AnimalKind.Deer, "Roams the woods and meadows");
                    foreach (var (mode, label, about) in new[] { (ClockMode.Normal, "Days pass", "The village keeps its routine: morning, work, evening, night"), (ClockMode.Fast, "Fast days", "Watch a whole day go by in a few minutes"), (ClockMode.Paused, "Stop time", "The time of day stands still") })
                    {
                        var m = mode;
                        time.Rows.Add(Row(Choice(label, World.Clock.Mode == m, () => { World.Clock.Mode = m; RefreshOptions(); }), about));
                    }
                    events.Rows.Add(Row(Act("Fête", () => { Say(World.HoldFete(), 4f); RefreshOptions(); }), "The whole village gathers for a fête"));
                    events.Rows.Add(Row(Act("Emergency", () => { Say(World.Traffic.RaiseIncident(), 4f); RefreshOptions(); }), "A fire, an accident or a theft: the fire engine, ambulance or police set off"));
                    weather.Rows.Add(Row(Switch("Clouds", World.Sky.Shown, () => { World.Sky.Shown = !World.Sky.Shown; RefreshOptions(); }), "Clouds drift over the land"));
                    weather.Rows.Add(Row(Switch("Rain", World.Sky.Raining, () => { if (!World.Sky.Shown) World.Sky.Shown = true; Say(World.Sky.ToggleRain()); RefreshOptions(); }), "A shower: villagers put up umbrellas and hurry indoors"));
                    groups.Add(people); groups.Add(farm); groups.Add(wild); groups.Add(time); groups.Add(events); groups.Add(weather);
                    open = DecorKind == Villager ? 0 : DecorKind is (int)AnimalKind.Dog or (int)AnimalKind.Cat or (int)AnimalKind.Deer ? 2 : 1;
                    footer = "Point at a villager or animal and pull the trigger: their card";
                    break;
                }
                case Tool.Select:
                {
                    caption = "Look";
                    var cards = new Group { Name = "Cards", About = "See what anything is and does" };
                    var cams = new Group { Name = "Cameras", About = "A camera's picture on the monitor above the palette" };
                    cards.Rows.Add(Row(Note("Point and pull"), "On anything with a gold ring under it: its card opens here, with what you can do with it"));
                    cards.Rows.Add(Row(Note("Villagers"), "Their card follows them; their camera shows their day"));
                    cards.Rows.Add(Row(Note("Trains"), "Camera rides along at the front"));
                    var cctv = World.Cctv;
                    for (int k = 0; k < cctv.Feeds && k < 10; k++)
                    {
                        int cam = k;
                        cams.Rows.Add(Row(Choice(k < cctv.Count ? $"Cam {k + 1}" : $"Train {k - cctv.Count + 1}", Watching(cam), () => { OpenFeed(cam); RefreshOptions(); }),
                            k < cctv.Count ? "A camera on a pole: its picture on the monitor; the buttons round it turn it" : "The view from the front of a train"));
                    }
                    if (cctv.Feeds == 0) cams.Rows.Add(Row(Note("No cameras yet"), "Put one up: Build > Details > Street > Camera"));
                    groups.Add(cards); groups.Add(cams);
                    break;
                }
                case Tool.Eraser:
                {
                    var erase = new Group { Name = "Erase", About = "Take away what you built" };
                    erase.Rows.Add(Row(Note("Tap"), "Pull the trigger on anything you built - a building, a road, a train, a pond: it goes"));
                    erase.Rows.Add(Row(Note("Hold"), "Hold the trigger over trees to clear them"));
                    erase.Rows.Add(Row(Note("Changed your mind?"), "Undo (or B) brings it back"));
                    groups.Add(erase);
                    break;
                }
            }
            if (groups.Count > 0)
            {
                if (!openGroup.TryGetValue(Tool, out int was) || Tool is Tool.Terrain or Tool.Water or Tool.Paths or Tool.Track || Tool == Tool.Decor && was < 3
                    || Tool == Tool.Forest && (was != 2 || ForestErase))   // (Density shows no state of its own: it stays open till left)
                    openGroup[Tool] = open;
                int g = Mathf.Clamp(openGroup[Tool], 0, groups.Count - 1);
                var chips = new List<PaletteUI.Option>();
                for (int k = 0; k < groups.Count; k++)
                {
                    int index = k; var grp = groups[k];
                    if (groups.Count > 1) chips.Add(Choice(grp.Name, k == g, () => { openGroup[Tool] = index; grp.Opened?.Invoke(); RefreshOptions(); }, grp.About));
                }
                o.InsertRange(0, chips);
                Palette.ShowDetails($"{PaletteUI.ToolNames[(int)Tool]} · {groups[g].Name}", groups[g].About, groups[g].Rows, footer);
            }
            else Palette.HideDetails();
            Palette.SetCaption(false, o.Count > 0 ? caption : null);
            Palette.SetCaption(true, items.Count > 0 ? "Family" : null);
            Palette.SetOptions(o);
            Palette.SetItems(items);
            if (Tool != Tool.Houses) Palette.HideLibrary();
            if (infoMode == 2) ShowHelp();
            Palette.SetTitle(WonderlandWorld.CurrentSlot > 0 ? $"Wonderland · World {WonderlandWorld.CurrentSlot}" : "Wonderland");
        }

        private static string CategoryTip(PlaceCategory c) => c switch
        {
            PlaceCategory.Homes => "Cottages, town houses, flats and farmhouses: each is home to villagers",
            PlaceCategory.Town => "The town hall, church, theatre, museum, hotel, squares and parks",
            PlaceCategory.Shops => "Bakers, grocers, a supermarket, a shopping mall, kiosks, boutiques, florists, cafés, a pizzeria, restaurants, pubs: where villagers shop and eat",
            PlaceCategory.Services => "Hospital, police, fire station, school, factories and the sawmill",
            PlaceCategory.Farm => "Barns, silos, windmills, glasshouses and fields",
            PlaceCategory.Fun => "Fairground rides, a castle, sports grounds and evening spots",
            PlaceCategory.Zoo => "A zoo, piece by piece: the entrance, enclosures, animal houses and kiosks",
            PlaceCategory.Travel => "Stations, bus stops, taxi ranks, car parks, boats and landing stages",
            PlaceCategory.Harbour => "A port: the container quay and its crane, the yard, a warehouse and the gate",
            PlaceCategory.Nature => "Rocks, crags and log piles",
            PlaceCategory.Small => "Street lamps, benches, letterboxes, cameras, flower beds and statues",
            _ => null,
        };
    }
}
