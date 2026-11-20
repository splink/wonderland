using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// What a card lets you do: buttons along its bottom for everything that can be done with what it shows - Move and
    /// Remove for most things, Stop / Go, speed and carriages for a train, which way a switch sends trains, a signal's
    /// setting, bending a piece of track. Nothing needs grip or a particular tool: select it, press the button. Moving
    /// from a card works like grip, but the trigger puts it down.
    /// Switches, signals, pieces of track and bridges have cards too (Select, trigger on them); a bridge's buttons are the
    /// styles that suit it.
    /// </summary>
    public sealed partial class ToolController
    {
        private Vector3 followPoint;                 // a switch's, a signal's, a piece of track's or a bridge's point (kinds 6, 7, 8, 9)

        private void InspectSwitch(int joint) { StopCameras(); followKind = 6; followPoint = World.Track.Network.Joints[joint].Position; cardTimer = 0f; Haptic(0.4f, 0.03f); }

        private void InspectSignal(int sig) { StopCameras(); followKind = 7; var g = World.Track.Signals[sig]; followPoint = new Vector3(g.x, 0f, g.z); cardTimer = 0f; Haptic(0.4f, 0.03f); }

        private void InspectTrack(int edge, float s) { StopCameras(); followKind = 8; followPoint = World.Track.Network.Edges[edge].Layout.Evaluate(s, out _); cardTimer = 0f; Haptic(0.4f, 0.03f); }

        private void InspectBridge(int bridge) { StopCameras(); followKind = 9; var a = World.Bridges.Bridges[bridge].Anchor; followPoint = new Vector3(a.x, 0f, a.y); cardTimer = 0f; Haptic(0.4f, 0.03f); }

        /// <summary>Whether a switch or a signal is at a point (they are picked before the bridge they stand on).</summary>
        private bool RailFixtureAt(Vector3 local) => World.Track.HasTrack && (World.Track.PickJoint(local, 0.045f) >= 0 || World.Track.PickSignal(local, 0.03f) >= 0);

        /// <summary>A tap with Roads or Track: the bridge pointed at (or under the point) takes its next style.</summary>
        private void TapBridge(Vector3 at)
        {
            var bridge = World.CycleBridge(pressBridge >= 0 ? pressBridge : World.Bridges.Pick(new Vector2(at.x, at.z)));
            if (bridge != null) { Say(bridge + " - tap again for another style (Select: its card lists them all)"); Haptic(0.5f, 0.05f); }
        }

        /// <summary>Which boats pass under a bridge with this much room.</summary>
        private static string Passes(float room) =>
            room >= BoatFactory.AirDraft(BoatKind.Sailing) ? "every boat and ship, sailing boats too"
            : room >= HarbourSystem.AirDraft ? "rowing boats, steamers and container ships - not sailing boats"
            : room >= BoatFactory.AirDraft(BoatKind.Rowing) ? "rowing boats only" : "nothing - it is too low for any boat";

        private void SetBridge(BridgeStyle style)
        {
            int i = FollowedRail();
            if (i < 0 || !World.SetBridgeStyle(i, style)) return;
            i = World.Bridges.Find(new Vector2(followPoint.x, followPoint.z));
            if (i >= 0) { var a = World.Bridges.Bridges[i].Anchor; followPoint = new Vector3(a.x, 0f, a.y); }
            Say(BridgeSystem.Names[(int)style] + (i >= 0 ? " - under it: " + Passes(World.Bridges.ClearanceOf(i, true)) : ""));
            Haptic(0.5f, 0.05f);
            cardTimer = 0f;
        }

        /// <summary>Select's trigger on the railway (nothing else under the pointer): a switch's, a signal's or the track's card.</summary>
        private bool SelectRail(Vector3 local)
        {
            if (!World.Track.HasTrack) return false;
            int joint = World.Track.PickJoint(local, 0.045f);
            if (joint >= 0) { InspectSwitch(joint); return true; }
            int sig = World.Track.PickSignal(local, 0.03f);
            if (sig >= 0) { InspectSignal(sig); return true; }
            if (World.Track.Network.Nearest(local, out int edge, out float s, out _, 0.035f)) { InspectTrack(edge, s); return true; }
            return false;
        }

        /// <summary>The followed switch / signal / piece of track, found again by where it is (edits renumber them).</summary>
        private int FollowedRail()
        {
            var net = World.Track.Network;
            if (followKind == 6) return World.Track.PickJoint(followPoint, 0.01f);
            if (followKind == 7) return World.Track.PickSignal(followPoint, 0.01f);
            if (followKind == 9) return World.Bridges.Find(new Vector2(followPoint.x, followPoint.z));
            return net.Nearest(followPoint, out int edge, out _, out _, 0.05f) ? edge : -1;
        }

        /// <summary>Which way each branch of a switch goes, seen from a train coming off the trunk: { "left", "right" } by branch.</summary>
        private string[] SwitchSides(TrackJoint j)
        {
            var net = World.Track.Network;
            Vector3 Away(EdgeEnd e)
            {
                var l = net.Edges[e.Edge].Layout;
                float s = e.End == 0 ? Mathf.Min(0.06f, l.Length * 0.5f) : Mathf.Max(l.Length - 0.06f, l.Length * 0.5f);
                return l.Evaluate(s, out _) - j.Position;
            }
            var travel = -Away(j.Trunk);
            float r0 = Vector3.Cross(travel, Away(j.Branch(0))).y, r1 = Vector3.Cross(travel, Away(j.Branch(1))).y;
            return r0 > r1 ? new[] { "right", "left" } : new[] { "left", "right" };
        }

        /// <summary>Where a switch's branch takes trains, in words.</summary>
        private string Leads(TrackJoint j, EdgeEnd b, out string colour)
        {
            var net = World.Track.Network;
            colour = CardText.Warn;
            if (!net.Through(b.Edge)) return "to a dead end - trains stop at the buffers";
            if (net.CircuitOf(b.Edge) != net.CircuitOf(j.Trunk.Edge)) return "into another part of the railway - trains can't come back (draw a link back)";
            colour = null;
            return "on round the railway and back";
        }

        private void SetSwitch(int joint, int branch)
        {
            if (joint < 0) return;
            if (World.Trains.JointLocked(joint)) { Say("Wait - a train is on the switch"); return; }
            var j = World.Track.Network.Joints[joint];
            var side = SwitchSides(j);
            if (branch < 0) { j.Pinned = false; Say(World.Trains.Mode == SwitchMode.Manual ? "Switches are on Manual (Train tool): it stays as it is" : "Trains choose which way here again"); }
            else { j.Setting = branch; j.Pinned = World.Trains.Mode != SwitchMode.Manual; Say($"Set: every train goes {side[branch]} here"); }
            World.Track.RebuildFixtures();
            cardTimer = 0f;
            Haptic(0.5f, 0.04f);
        }

        private void SetSignal(int sig, bool red)
        {
            if (sig < 0) return;
            if (World.Track.Signals[sig].red != red) World.ToggleSignal(sig);
            Say(SignalSaid(red));
            cardTimer = 0f;
        }

        /// <summary>The card of a switch, a signal or a piece of track.</summary>
        private void RailCard(int i, bool refresh, out Vector3 head)
        {
            var net = World.Track.Network;
            head = followPoint + Vector3.up * 0.04f;
            if (followKind == 6) head = net.Joints[i].Position + Vector3.up * 0.04f;
            else if (followKind == 9) head = World.Bridges.Middle(i) + Vector3.up * 0.04f;
            else if (followKind == 8 && net.Nearest(followPoint, out int e, out float s, out _, 0.05f)) head = net.Edges[e].Layout.Evaluate(s, out _) + Vector3.up * 0.04f;
            if (!refresh) return;
            Pin(homePin, null); Pin(workPin, null);
            routeLine.enabled = false;
            var body = new CardText();
            if (followKind == 6)
            {
                var j = net.Joints[i];
                var side = SwitchSides(j);
                bool manual = World.Trains.Mode == SwitchMode.Manual;
                string now = j.Pinned ? $"always {side[j.Setting]} (set by you)" : manual ? $"{side[j.Setting]} (switches on Manual)" : $"trains choose - {side[j.Setting]} now";
                body.Section("Now").Row("Set", now);
                body.Section("Trains coming off the trunk");
                for (int k = 0; k < 2; k++) { string how = Leads(j, j.Branch(k), out string c); body.Row("Going " + side[k], how, c); }
                body.Section("Trains coming the other way").Row("Either branch", "run straight through onto the trunk");
                Palette.ShowCard("Switch", j.Pinned ? $"set: always {side[j.Setting]}" : manual ? "set by hand" : "trains choose", body.ToString(), null, CloseCard);
            }
            else if (followKind == 7)
            {
                bool red = World.Track.Signals[i].red;
                body.Section("Now").Row("Set", red ? "held at red - trains wait here" : "automatic");
                body.Section("Settings").Row("Automatic", "red while a train is on the line just past it").Row("Held at red", "trains wait here until you let them go");
                Palette.ShowCard("Signal", red ? "held at red" : "automatic", body.ToString(), null, CloseCard);
            }
            else if (followKind == 9)
            {
                var b = World.Bridges.Bridges[i];
                string carries = b.Rail && b.Road ? "the railway and a road" : b.Rail ? b.Members.Count > 1 ? $"{b.Members.Count} tracks" : "the railway" : b.Road ? "a road" : "a path";
                body.Section("This bridge").Row("Style", BridgeSystem.Names[(int)b.Style]).Row("Carries", carries).Row("Across", $"{b.Length * 100f:0} cm");
                float room = World.Bridges.ClearanceOf(i);
                if (room < float.MaxValue)
                {
                    body.Section("Under it");
                    if (b.Lift != null)
                    {
                        float open = World.Bridges.ClearanceOf(i, true);
                        body.Row("Room", $"{room * 100f:0.0} cm - {open * 100f:0.0} cm with the span up")
                            .Row("Span", b.Lift.Up ? "up: a boat is passing, trains wait" : b.Lift.Height > 0f ? "moving" : "down: trains cross")
                            .Row("Passes", Passes(open));
                    }
                    else body.Row("Room", $"{room * 100f:0.0} cm").Row("Passes", Passes(room), room < HarbourSystem.AirDraft ? CardText.Warn : null);
                }
                body.Section("Styles that suit it");
                foreach (var st in BridgeSystem.Allowed(b)) body.Row(BridgeSystem.Short[(int)st], BridgeSystem.About[(int)st]);
                if (b.Rail && !BridgeSystem.Allowed(b).Contains(BridgeStyle.Lift))
                    body.Row("Lift", b.Members.TrueForAll(m => m.Track) ? "needs a longer crossing (16 cm or more)" : "only for a bridge that carries just the railway", CardText.Warn);
                Palette.ShowCard(BridgeSystem.Names[(int)b.Style], "carries " + carries, body.ToString(), null, CloseCard);
            }
            else
            {
                var edge = net.Edges[i];
                string End(int k) => edge.Layout.Closed ? "none - it's a loop" : edge.Joint[k] >= 0 ? "a switch" : "buffers: the end of the line";
                body.Section("This piece").Row("Length", $"{edge.Layout.Length * 100f:0} cm").Row("Runs", net.Directed(i) ? "one way, as the arrows point" : "not set yet");
                if (!edge.Layout.Closed) body.Row("Starts at", End(0)).Row("Ends at", End(1));
                body.Section("Railway").Row("In all", $"{net.TotalLength:0.0} m, {net.Joints.Count} switches").Row("Trains", World.Trains.Trains.Count.ToString());
                if (net.Traps > 0) body.Row("Watch out", "trains can get into a part they can't leave - join it back", CardText.Warn);
                Palette.ShowCard("Railway line", edge.Layout.Closed ? "a loop" : "between switches", body.ToString(), null, CloseCard);
            }
            Palette.SetCardAction(null, false, null);
        }

        /// <summary>The buttons along the bottom of the open card.</summary>
        private List<PaletteUI.Option> CardActions(int i)
        {
            var o = new List<PaletteUI.Option>();
            switch (followKind)
            {
                case 1: case 2:
                    o.Add(new PaletteUI.Option("Move", false, MoveFromCard));
                    o.Add(new PaletteUI.Option("Remove", false, RemoveFromCard));
                    break;
                case 3:
                {
                    var h = World.Buildings.Houses[i];
                    o.Add(new PaletteUI.Option("Move", false, MoveFromCard));
                    if (Catalog.Get(h.variant).Styles > 1) o.Add(new PaletteUI.Option("New look", false, () => { Say(World.RestyleHouse(Followed()) ?? "A new look"); cardTimer = 0f; }));
                    o.Add(new PaletteUI.Option("Remove", false, RemoveFromCard));
                    break;
                }
                case 4:
                {
                    var t = followTrain;
                    o.Add(new PaletteUI.Option(t.Running ? "Stop" : "Go", false, () => { t.Running = !t.Running; Say(t.Running ? "All aboard!" : "Train stopping"); cardTimer = 0f; }));
                    o.Add(new PaletteUI.Option("Slower", false, () => { t.SpeedFactor = Mathf.Max(0.3f, t.SpeedFactor - 0.2f); Say($"Speed {t.SpeedFactor * 100f:0} %", 1.5f); }, t.SpeedFactor > 0.31f));
                    o.Add(new PaletteUI.Option("Faster", false, () => { t.SpeedFactor = Mathf.Min(1.8f, t.SpeedFactor + 0.2f); Say($"Speed {t.SpeedFactor * 100f:0} %", 1.5f); }, t.SpeedFactor < 1.79f));
                    o.Add(new PaletteUI.Option("Longer", false, () => { Couple(t, 1); cardTimer = 0f; }));
                    o.Add(new PaletteUI.Option("Shorter", false, () => { Couple(t, -1); cardTimer = 0f; }));
                    o.Add(new PaletteUI.Option("Move", false, MoveFromCard));
                    o.Add(new PaletteUI.Option("Remove", false, RemoveFromCard));
                    break;
                }
                case 5:
                    if (followVehicle.Roams) o.Add(new PaletteUI.Option("Remove", false, RemoveFromCard));
                    break;
                case 6:
                {
                    var j = World.Track.Network.Joints[i];
                    var side = SwitchSides(j);
                    int left = side[0] == "left" ? 0 : 1;
                    bool manual = World.Trains.Mode == SwitchMode.Manual;
                    o.Add(new PaletteUI.Option("Trains choose", !j.Pinned && !manual, () => SetSwitch(FollowedRail(), -1), !manual));
                    o.Add(new PaletteUI.Option("Always left", (j.Pinned || manual) && j.Setting == left, () => SetSwitch(FollowedRail(), left)));
                    o.Add(new PaletteUI.Option("Always right", (j.Pinned || manual) && j.Setting == 1 - left, () => SetSwitch(FollowedRail(), 1 - left)));
                    break;
                }
                case 7:
                {
                    bool red = World.Track.Signals[i].red;
                    o.Add(new PaletteUI.Option("Automatic", !red, () => SetSignal(FollowedRail(), false)));
                    o.Add(new PaletteUI.Option("Hold at red", red, () => SetSignal(FollowedRail(), true)));
                    o.Add(new PaletteUI.Option("Remove", false, RemoveFromCard));
                    break;
                }
                case 9:
                {
                    var b = World.Bridges.Bridges[i];
                    foreach (var st in BridgeSystem.Allowed(b)) { var style = st; o.Add(new PaletteUI.Option(BridgeSystem.Short[(int)st], b.Style == st, () => SetBridge(style))); }
                    break;
                }
                case 8:
                    o.Add(new PaletteUI.Option("Bend", false, MoveFromCard));
                    o.Add(new PaletteUI.Option("Signal here", false, () => { string e = World.PlaceSignal(followPoint); Say(e ?? "Signal up: automatic - red while a train is on the line just past it"); }));
                    o.Add(new PaletteUI.Option("Remove", false, RemoveFromCard));
                    break;
            }
            return o;
        }

        /// <summary>The card's Move (Bend for track): pick it up as grip would; the trigger puts it down.</summary>
        private void MoveFromCard()
        {
            int i = followKind >= 6 ? FollowedRail() : Followed();
            int kind = followKind; var train = followTrain;
            if (i < 0) return;
            float s = 0f;
            if (kind == 8) World.Track.Network.Nearest(followPoint, out _, out s, out _, 0.05f);
            CloseCard();
            heldByTrigger = true;
            bool held = kind switch
            {
                1 => PickUpPerson(i), 2 => PickUpAnimal(i), 3 => PickUpHouse(i), 4 => PickUpTrain(train), 8 => BendTrack(i, s), _ => false,
            };
            if (!held) heldByTrigger = false;
        }

        /// <summary>The card's Remove: takes it away (B undoes it) and closes the card.</summary>
        private void RemoveFromCard()
        {
            int i = followKind >= 6 ? FollowedRail() : Followed();
            if (i < 0) return;
            string said;
            switch (followKind)
            {
                case 1: World.Checkpoint(); said = World.People.NameOf(i) + " has left"; World.People.RemoveAt(i); break;
                case 2: World.Checkpoint(); World.Animals.RemoveAt(i); said = "Gone"; break;
                case 3: said = World.RemoveHouse(i); break;
                case 4: World.Checkpoint(); World.Trains.Remove(followTrain); said = "Train removed"; break;
                case 5: World.Checkpoint(); World.Traffic.RemovePlaced(followVehicle.Position, 0.001f); said = "Removed"; break;
                case 7: said = World.RemoveSignal(i); break;
                case 8: said = World.RemovePieceAt(followPoint) ? "Track removed" : "Couldn't remove it"; break;
                default: return;
            }
            CloseCard();
            Say(said + " - B brings it back");
            Haptic(0.6f, 0.06f);
            RefreshOptions();
        }
    }
}
