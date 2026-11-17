using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Tips: a short word at the right moment about what can be done with what is in hand, shown in an amber banner
    /// above the palette. One at a time, each at most twice (remembered between sessions), never more often than every
    /// twenty seconds, and only when it applies (no railway yet, a loop without a train, a one-way trap, rain...). The
    /// Tips button turns them off (and on again).
    /// </summary>
    public sealed partial class ToolController
    {
        private const string TipsKey = "wonderland.tips";
        private const float TipSeconds = 9f, TipGap = 20f;

        public static bool TipsOn
        {
            get => PlayerPrefs.GetInt(TipsKey, 1) == 1;
            set { PlayerPrefs.SetInt(TipsKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        private sealed class TipRule
        {
            public string Id, Text;
            public Func<bool> When;
            public int Times = 2;
        }

        private List<TipRule> tipRules;
        private float tipClock, tipUntil, nextTipAt = 4f;
        public string TipShowing { get; private set; }

        /// <summary>The Tips button: turns tips off (and on again, starting over from the first).</summary>
        public void ToggleTips()
        {
            TipsOn = !TipsOn;
            if (TipsOn) { foreach (var r in Rules()) PlayerPrefs.DeleteKey("wonderland.tip." + r.Id); nextTipAt = tipClock + 1f; }
            else { TipShowing = null; Palette.SetTip(null); }
            Say(TipsOn ? "Tips on - a hint now and then about what you can do" : "Tips off - Help still explains the tool in hand");
        }

        private List<TipRule> Rules()
        {
            if (tipRules != null) return tipRules;
            var net = (Func<TrackNetwork>)(() => World.Track.Network);
            tipRules = new List<TipRule>
            {
                new() { Id = "welcome", When = () => true, Times = 1,
                    Text = "Welcome! Point at the palette and pull the trigger to pick a tool. Try Track to lay a railway, or Terrain to shape the land." },
                new() { Id = "first-track", When = () => Tool == Tool.Track && net().Empty,
                    Text = "Hold the trigger and draw a big circle. Come back to where you started and it becomes a loop - trains run round loops." },
                new() { Id = "drawing", When = () => Tool == Tool.Track && drawn.Count > 5,
                    Text = "While you draw: green means it can be built, red says why not. The arrows show which way trains will run." },
                new() { Id = "add-train", When = () => !net().Empty && World.Trains.Trains.Count == 0 && net().Circuits > 0,
                    Text = "Your railway is ready for a train: pick the Train tool and pull the trigger on the track." },
                new() { Id = "branch", When = () => Tool == Tool.Track && !net().Empty && World.Trains.Trains.Count > 0,
                    Text = "Start a stroke on a track to branch off it, end on a track to join it. The whole railway runs one way - follow the arrows." },
                new() { Id = "trap", When = () => net().Traps > 0, Times = 3,
                    Text = "Trains can get into part of your railway but not back out. Draw a second link the other way so they can return." },
                new() { Id = "dead-end", When = () => Tool == Tool.Track && DeadEndCount() > 0,
                    Text = "A line that stops at a dead end isn't used by trains (they'd have to reverse). Join its end back onto the railway." },
                new() { Id = "train-tool", When = () => Tool == Tool.Train && World.Trains.Trains.Count > 0,
                    Text = "Point at a train: trigger stops or starts it, + / - Carriage changes its length. Trains always run the way the arrows point." },
                new() { Id = "stations", When = () => World.Trains.Trains.Count > 0 && World.Trains.Stations.Count == 0,
                    Text = "Put a station beside the track (Build > Travel): trains stop there and villagers ride them to other villages." },
                new() { Id = "build", When = () => Tool == Tool.Houses,
                    Text = "Pick a category, then a building, and point where it should go. A flick of the stick turns it; the arrow shows its front." },
                new() { Id = "cards", When = () => peekPerson >= 0, Times = 2,
                    Text = "That's one of your villagers - the gold ring shows who. Squeeze grip to meet them: their card says what they're up to, and their camera follows their day." },
                new() { Id = "follow", When = () => World.Cctv.FollowSeed != 0, Times = 1,
                    Text = "Their camera goes where they go: through their eyes, out of a train or car window, outside the door they went in. Keep building - Close stops following." },
                new() { Id = "grip-anything", When = () => peekHouse >= 0 || peekTrain != null || peekVehicle != null, Times = 1,
                    Text = "Grip works on anything with a gold ring under it - buildings, trains, cars, animals - in any tool." },
                new() { Id = "water", When = () => Tool == Tool.Water,
                    Text = "Pond digs a basin that fills; Stream flows downhill, even from one pond to another. Boats and landing stages are in Build > Travel." },
                new() { Id = "watch", When = () => Tool == Tool.Watch,
                    Text = "Watch a camera (Build > Small > Camera) or ride along on a train. The stick turns the camera you're watching." },
                new() { Id = "rain", When = () => World.Sky.Raining, Times = 1,
                    Text = "Rain! Villagers out and about put up umbrellas and would rather be indoors. The Rain chip in the Life tool starts or stops a shower." },
                new() { Id = "heavy", When = () => Perf != null && Health(out _) == Red, Times = 2,
                    Text = "The light by the clock is red: the headset is working hard. Fewer trees or buildings in view will make it smoother." },
                new() { Id = "worlds", When = () => World.Buildings.Houses.Count > 8 && WonderlandWorld.CurrentSlot == 0, Times = 1,
                    Text = "Save keeps this world as World 1-6, so you can build another and come back to it. Tips can be turned off with the Tips button." },
            };
            return tipRules;
        }

        private int DeadEndCount()
        {
            var n = World.Track.Network; int k = 0;
            foreach (var e in n.Edges) if (!e.Closed) { if (e.Joint[0] < 0) k++; if (e.Joint[1] < 0) k++; }
            return k;
        }

        /// <summary>Shows the next tip that applies (if tips are on and it is time).</summary>
        private void UpdateTips(float dt)
        {
            tipClock += dt;
            if (!TipsOn) { if (TipShowing != null) { TipShowing = null; Palette.SetTip(null); } return; }
            if (TipShowing != null && tipClock < tipUntil) return;
            if (TipShowing != null) { TipShowing = null; Palette.SetTip(null); }
            if (tipClock < nextTipAt) return;
            nextTipAt = tipClock + 1f;   // look again in a second
            foreach (var r in Rules())
            {
                string key = "wonderland.tip." + r.Id;
                int seen = PlayerPrefs.GetInt(key, 0);
                if (seen >= r.Times) continue;
                bool applies;
                try { applies = r.When(); } catch (Exception) { applies = false; }
                if (!applies) continue;
                PlayerPrefs.SetInt(key, seen + 1);
                TipShowing = r.Text;
                Palette.SetTip(r.Text);
                tipUntil = tipClock + TipSeconds;
                nextTipAt = tipUntil + TipGap;
                return;
            }
        }
    }
}
