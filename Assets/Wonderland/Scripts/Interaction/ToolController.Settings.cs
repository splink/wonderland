using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Settings: one place, beside the palette, for everything that isn't a tool - sound, tips, the label at the
    /// pointer, the building grid, the world's numbers, the performance switches and starting a new world. Each button
    /// says its current state ("Tips: On"); a tap changes it.
    /// </summary>
    public sealed partial class ToolController
    {
        /// <summary>Sound, from the app: its state in a word ("On", "Quiet", "Off"), and a way to change it.</summary>
        public Func<string> SoundState;
        public Action CycleSound;

        private static string OnOff(bool on) => on ? "On" : "Off";

        /// <summary>The Settings button: opens the panel; closes it (or Stats / Performance, opened from it).</summary>
        public void ToggleSettings()
        {
            if (infoMode is 1 or 3 or 4) CloseInfo();
            else { infoMode = 4; ShowSettings(); }
        }

        private void ShowSettings()
        {
            var body = new CardText(30).Section("Switches")
                .Row("Sound", "the village's sounds: on, quiet or off")
                .Row("Tips", "a word now and then about what you can do next")
                .Row("Labels", "beside the pointer: what each controller button does to what you point at")
                .Row("Grid", "roads and railway straight and square, buildings lined up along the roads")
                .Section("More")
                .Row("World stats", "how many people, homes, buildings, trains and vehicles")
                .Row("Performance", "how the headset is keeping up, and switches to make it lighter");
            Palette.ShowInfo("Settings", "switches and more panels", body.ToString(), CloseInfo);
            var o = new List<PaletteUI.Option>();
            if (CycleSound != null) o.Add(new PaletteUI.Option("Sound: " + SoundState(), false, () => { CycleSound(); ShowSettings(); }) { Kind = OptionKind.Action, Tip = "Each press: on, quiet, off." });
            o.Add(new PaletteUI.Option("Tips", TipsOn, () => { ToggleTips(); ShowSettings(); }) { Kind = OptionKind.Toggle, Tip = "A word now and then in the amber banner about what you can do next. Turning them on again starts from the first." });
            o.Add(new PaletteUI.Option("Labels", LabelsOn, () =>
            {
                LabelsOn = !LabelsOn; labelKey = 0;
                Say(LabelsOn ? "Labels on: the pointer says what each button does" : "Labels off - Help still explains each tool");
                ShowSettings();
            }) { Kind = OptionKind.Toggle, Tip = "A little label beside the pointer saying what the trigger, grip and A would do to what you point at." });
            o.Add(new PaletteUI.Option("Grid", BuildGrid.Snap, () =>
            {
                BuildGrid.Snap = !BuildGrid.Snap;
                Say(BuildGrid.Snap ? GridOnSaid : "Grid off: draw and place freely");
                RefreshOptions(); ShowSettings();
            }) { Kind = OptionKind.Toggle, Tip = "On: roads and railway come out straight and square with standard curves; buildings line up along roads. Off: freehand." });
            o.Add(new PaletteUI.Option("World stats", false, () => { infoMode = 1; ShowStats(); }) { Kind = OptionKind.Open, Tip = "The world in numbers: people, homes, buildings, trains, vehicles." });
            o.Add(new PaletteUI.Option("Performance", false, ShowPerformance) { Kind = OptionKind.Open, Tip = "How the headset is keeping up, and switches that make the world lighter to draw." });
            Palette.SetInfoButtons(o);
        }
    }
}
