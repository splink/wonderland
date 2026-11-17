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
            var body = new CardText(26).Section("Settings");
            if (CycleSound != null) body.Row("Sound", "on, quiet or off");
            body.Row("Tips", "a word now and then about what you can do")
                .Row("Labels", "beside the pointer: what each button does to what you point at")
                .Row("Grid", "roads come out straight and square, buildings line up along them")
                .Section("More")
                .Row("World stats", "how many people, buildings, trains...")
                .Row("Performance", "what the headset is doing, and switches to make it lighter")
                .Row("New world", "start again from a fresh landscape (press twice)");
            Palette.ShowInfo("Settings", "tap a button to change it", body.ToString(), CloseInfo);
            var o = new List<PaletteUI.Option>();
            if (CycleSound != null) o.Add(new PaletteUI.Option("Sound: " + SoundState(), false, () => { CycleSound(); ShowSettings(); }));
            o.Add(new PaletteUI.Option("Tips: " + OnOff(TipsOn), TipsOn, () => { ToggleTips(); ShowSettings(); }));
            o.Add(new PaletteUI.Option("Labels: " + OnOff(LabelsOn), LabelsOn, () =>
            {
                LabelsOn = !LabelsOn; labelKey = 0;
                Say(LabelsOn ? "Labels on: the pointer says what each button does" : "Labels off - Help still explains each tool");
                ShowSettings();
            }));
            o.Add(new PaletteUI.Option("Grid: " + OnOff(BuildGrid.Snap), BuildGrid.Snap, () =>
            {
                BuildGrid.Snap = !BuildGrid.Snap;
                Say(BuildGrid.Snap ? "Grid on: roads come out straight and square, buildings line up along the roads" : "Grid off: draw and place freely");
                RefreshOptions(); ShowSettings();
            }));
            o.Add(new PaletteUI.Option("World stats", false, () => { infoMode = 1; ShowStats(); }));
            o.Add(new PaletteUI.Option("Performance", false, ShowPerformance));
            o.Add(new PaletteUI.Option(resetArmed && Time.time < resetUntil ? "Sure? Again" : "New world", resetArmed && Time.time < resetUntil, () => { RequestReset(); ShowSettings(); }));
            Palette.SetInfoButtons(o);
        }
    }
}
