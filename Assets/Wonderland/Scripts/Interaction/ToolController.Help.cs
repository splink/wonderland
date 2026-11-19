using System.Collections.Generic;

namespace Wonderland
{
    /// <summary>
    /// Help: eight topics as tabs along the top of the panel - Start, Moving, Landscape, Roads & rail, Buildings, Life,
    /// Look & erase, Worlds - opening on the one for the tool in hand. Each says what it is for, how to do it step by
    /// step, an example, and the controls.
    /// </summary>
    public sealed partial class ToolController
    {
        public static readonly string[] HelpTopics = { "Start", "Moving", "Landscape", "Roads & rail", "Buildings", "Life", "Look & erase", "Worlds" };
        private int helpTopic;

        /// <summary>The topic Help opens on for a tool.</summary>
        public static int TopicFor(Tool t) => t switch
        {
            Tool.Terrain or Tool.Water or Tool.Forest => 2,
            Tool.Paths or Tool.Track or Tool.Train => 3,
            Tool.Houses => 4,
            Tool.Decor => 5,
            Tool.Select or Tool.Eraser => 6,
            _ => 0,
        };

        /// <summary>The topic Help shows now (tests).</summary>
        public int HelpTopic => helpTopic;

        private void ShowHelp()
        {
            Palette.ShowInfo("Help", HelpTopics[helpTopic], HelpBody(helpTopic), CloseInfo);
            var tabs = new List<PaletteUI.Option>();
            for (int k = 0; k < HelpTopics.Length; k++)
            {
                int topic = k;
                tabs.Add(new PaletteUI.Option(HelpTopics[k], k == helpTopic, () => { helpTopic = topic; ShowHelp(); }) { Tip = HelpTips[k] });
            }
            Palette.SetInfoTabs(tabs);
            Palette.SetInfoButtons(null);
        }

        private static readonly string[] HelpTips =
        {
            "The controllers, the palette, undo", "Scrolling, turning and placing the world", "Terrain, water and forest", "Roads, the railway and trains",
            "The library and placing buildings", "Villagers, animals, time and weather", "Cards, cameras and the eraser", "Saving, loading and new worlds",
        };

        /// <summary>One topic: what it is for, how to (steps), an example, the controls.</summary>
        public static string HelpBody(int topic)
        {
            var t = new CardText(30);
            switch (topic)
            {
                case 0:
                    t.Section("What it's for").Line("Build a model world: shape the land, lay roads and railway, put up a town and watch it live.");
                    t.Section("How to").Line("1. Point the right controller at a tool on the palette and pull the trigger.")
                        .Line("2. Pick an option on the row below the tools; the panel on the left says what each does.")
                        .Line("3. Point at the world and pull (or hold) the trigger.");
                    t.Section("Example").Line("Terrain > Shape > Raise, then hold the trigger and sweep: a hill grows.");
                    t.Section("Controls").Row("Trigger", "use the tool; press buttons").Row("Grip", "pick up what has a gold ring under it").Row("A", "the tool's next option").Row("B", "undo").Row("Y (left)", "hide or show the palette");
                    break;
                case 1:
                    t.Section("What it's for").Line("The world goes on in every direction; the window shows part of it.");
                    t.Section("How to").Row("Scroll", "push the right stick: the land glides by")
                        .Row("Turn", "hold BOTH grips and twist your hands, like turning a model on a table; let go to put it down")
                        .Row("Jump", "tap a place on the map above the palette")
                        .Row("Home", "the Home button: back to where the world started")
                        .Row("Place", "World > Place > Move, point at the floor, trigger: it lies there")
                        .Row("Height", "World > Height: on the floor, at table height, or a step higher or lower")
                        .Row("Size", "World > Size: how much of the room it fills, and its scale");
                    t.Section("Example").Line("Twist your hands a quarter turn to see the town from its other side.");
                    break;
                case 2:
                    t.Section("What it's for").Line("Shape the land, dig water and plant forests.");
                    t.Section("How to").Row("Terrain", "Shape (Raise, Lower, Smooth, Flatten, Peaks) or Land (Grow, Trim): hold the trigger and sweep")
                        .Row("Water", "Ponds: hold where it should be. Streams: draw a line - it flows downhill; draw on from its end to make it longer, from its side for a branch")
                        .Row("Forest", "Plant: sweep for trees; Clear: sweep them away")
                        .Row("Brush", "left stick ↔ size, ↕ strength");
                    t.Section("Example").Line("Flatten a plot, dig a pond beside it, draw a stream from the hills into the pond.");
                    break;
                case 3:
                    t.Section("What it's for").Line("Roads for traffic, a railway for trains, paths and fences.");
                    t.Section("How to").Row("Roads", "pick Streets, Paths or Fences, hold the trigger and draw. Cross a road: crossroads with lights. End on one: a T-junction")
                        .Row("Railway", "Track > Draw: draw a big circle back to its start for a loop. Start on a track to branch; tap a switch to throw it")
                        .Row("Trains", "Train: pick one, trigger on the track. Point at a train: trigger stop / go, left stick speed")
                        .Row("Bridges", "where a road or railway crosses water it gets a bridge. Tap one (Roads or Track tool) for another style")
                        .Row("Ships", "boats and container ships only pass under a bridge high enough for them. A railway's Lift bridge opens for ships: trains wait while it is up")
                        .Row("Grid", "on: straight and square; off: freehand");
                    t.Section("Example").Line("Draw a loop of track, put a Steam train on it, add a station beside it (Buildings).");
                    break;
                case 4:
                    t.Section("What it's for").Line("Houses, shops, a zoo, a harbour - over a hundred buildings, each with a job in the world.");
                    t.Section("How to").Line("1. Build tool: pick a category, then a family on the second row.")
                        .Line("2. Point at a tile on the left: it says what it is, does and needs.")
                        .Line("3. Point at the land and pull the trigger. Left stick: turn it (the arrow is its front).");
                    t.Section("Example").Line("Travel > Road > Bus stop beside a street: it stands back with a lay-by, and buses pull in.");
                    t.Section("Move one").Line("Grip (or trigger) on a building: pick it up, point elsewhere, let go.");
                    break;
                case 5:
                    t.Section("What it's for").Line("People and animals who live their own days, and the time and weather.");
                    t.Section("How to").Row("People", "Life > People: trigger where they should appear; they find homes and jobs")
                        .Row("Animals", "Farm or Pets & wild: trigger (hold and sweep for a herd)")
                        .Row("Time", "Days pass, Fast days or Stop time")
                        .Row("Events", "Fête: the village gathers. Emergency: help sets off")
                        .Row("Weather", "Clouds and Rain on or off");
                    t.Section("Example").Line("Put ten villagers near some houses and a bakery, then Fast days: watch them go to work.");
                    break;
                case 6:
                    t.Section("What it's for").Line("See what anything is and does; take away what you don't want.");
                    t.Section("How to").Row("Cards", "Select: trigger on anything with a gold ring - its card opens on the left with what you can do")
                        .Row("Follow", "a villager's card follows them; their camera shows their day")
                        .Row("Cameras", "Select > Cameras: a camera's picture on the monitor")
                        .Row("Erase", "Eraser: trigger on anything you built; hold over trees to clear them");
                    t.Section("Example").Line("Select a train, then Camera: ride along at the front.");
                    break;
                default:
                    t.Section("What it's for").Line("Keep up to six worlds and come back to them.");
                    t.Section("How to").Row("Save", "Worlds > Save > a world: kept there, and kept up to date from then on")
                        .Row("Load", "Worlds > Load > a world")
                        .Row("New", "Worlds > New > Start a new world (press twice)")
                        .Row("Undo", "B, or Undo on the palette; Redo brings it back");
                    t.Section("Example").Line("Save your town as World 1, start a new world for a harbour, and Load World 1 to go back.");
                    t.Section("Settings").Line("Sound, tips, labels, the grid, stats and performance: the Settings button.");
                    break;
            }
            return t.ToString();
        }
    }
}
