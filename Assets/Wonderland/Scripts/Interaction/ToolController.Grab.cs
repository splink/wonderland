using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Grab: grip means "pick up what I'm pointing at and move it", in every tool - a building, a train, a villager, an
    /// animal, or a piece of track (which bends like a rope). Let go of grip to put it down; where it can't go, it goes
    /// back where it was and the banner says why. The same moves start from a card's Move / Bend buttons, for those who
    /// never use grip: then the trigger puts it down.
    /// </summary>
    public sealed partial class ToolController
    {
        private bool heldByTrigger;                 // started from a card button: the trigger puts it down (not letting go of grip)
        private PersonInstance? carriedPerson;
        private AnimalInstance? carriedAnimal;

        /// <summary>Something is in hand (a building, a train, someone, a piece of track being bent).</summary>
        public bool Holding => carrying || carriedTrain != null || dragEdge >= 0 || carriedPerson.HasValue || carriedAnimal.HasValue;

        /// <summary>Whether this frame puts down what is held: grip let go, or (started from a card) the trigger.</summary>
        private bool LetGo(PointerInput input) => heldByTrigger ? input.TriggerDown : input.GripUp || !input.Grip;

        /// <summary>Grip down on what the pointer is on: picks it up. Returns whether something was taken.</summary>
        private bool GrabAt(PointerInput input, bool hit, Vector3 local, int house)
        {
            heldByTrigger = false;
            if (peekPerson >= 0) return PickUpPerson(peekPerson);
            if (peekAnimal >= 0) return PickUpAnimal(peekAnimal);
            if (peekTrain != null) return PickUpTrain(peekTrain);
            if (peekVehicle != null) { Say("Cars keep to the roads - the Eraser takes one away, Roads puts new ones down"); Haptic(0.6f, 0.05f); return true; }
            if (house >= 0 && Tool != Tool.Houses) return PickUpHouse(house);
            // A piece of track (the Track tool has its own, with a wider reach and the grab point ringed).
            if (Tool != Tool.Track && hit && World.Track.Network.Nearest(local, out int edge, out float s, out _, 0.035f) && World.Track.PickJoint(local, 0.045f) < 0)
                return BendTrack(edge, s);
            return false;
        }

        private bool PickUpHouse(int house)
        {
            var h = World.PickUpHouse(house);
            HouseVariant = h.variant; houseRotation = h.rotation; houseStyle = h.style; carrying = true; pickedHover = -1;
            BuildCategory = Catalog.Get(h.variant).Category; BuildFamily = Catalog.Get(h.variant).Family;
            Say($"Moving the {HouseFactory.Name(h.variant).ToLower()} - {(heldByTrigger ? "trigger" : "let go of grip")} to put it down, left stick to turn it", 4f);
            Haptic(0.4f, 0.04f);
            RefreshOptions();
            return true;
        }

        private bool PickUpTrain(Train t)
        {
            World.Checkpoint();
            carriedTrain = new Train { Kind = t.Kind, Running = t.Running, SpeedFactor = t.SpeedFactor, Carriages = t.Carriages };
            World.Trains.Remove(t);
            hoveredTrain = null;
            Say($"Point at the track and {(heldByTrigger ? "pull the trigger" : "let go of grip")} - it will face the way the arrows point", 4f);
            Haptic(0.4f, 0.04f);
            return true;
        }

        private bool BendTrack(int edge, float s)
        {
            var net = World.Track.Network;
            dragEdge = edge; dragIndex = TrackPlanner.IndexAt(net.Edges[edge].Layout, s); dragError = null; dragPreviewAt = 0f;
            Debug.Log($"Wonderland track: grabbed piece {edge} at {s:0.00} m");
            Say($"Move the pointer and {(heldByTrigger ? "pull the trigger" : "let go of grip")} - the track bends like a rope (red: it can't go there)", 6f);
            Haptic(0.4f, 0.04f);
            return true;
        }

        private bool PickUpPerson(int i)
        {
            World.Checkpoint();
            carriedPerson = World.People.RemoveAt(i);
            Say($"Point where they should go and {(heldByTrigger ? "pull the trigger" : "let go of grip")}", 3f);
            Haptic(0.4f, 0.04f);
            return true;
        }

        private bool PickUpAnimal(int i)
        {
            World.Checkpoint();
            carriedAnimal = World.Animals.RemoveAt(i);
            Say($"Point where it should go and {(heldByTrigger ? "pull the trigger" : "let go of grip")}", 3f);
            Haptic(0.4f, 0.04f);
            return true;
        }

        /// <summary>Carrying a villager or an animal: a ghost of them at the pointer; letting go sets them down (or puts them back).</summary>
        private void CarryLiving(PointerInput input, bool hit, Vector3 local, ref Color colour)
        {
            colour = new Color(1f, 0.85f, 0.3f, 0.85f);
            var p2 = new Vector2(local.x, local.z);
            bool person = carriedPerson.HasValue;
            bool ok = hit && (person ? World.People.Walkable(p2) : true);
            ghost.gameObject.SetActive(hit);
            if (hit)
            {
                var wt = World.transform;
                ghostFilter.sharedMesh = person ? PersonFactory.Body(carriedPerson.Value.style) : AnimalFactory.Body((AnimalKind)carriedAnimal.Value.kind);
                ghost.position = wt.TransformPoint(local + Vector3.up * (0.006f + 0.003f * Mathf.Sin(Time.time * 6f)));
                ghost.rotation = wt.rotation * Quaternion.Euler(0f, Time.time * 40f, 0f);
                ghost.localScale = wt.lossyScale;
                ghostBlock.SetColor("_Highlight", ok ? new Color(0.6f, 1f, 0.5f, 0.18f) : new Color(1f, 0.3f, 0.25f, 0.45f));
                ghostRenderer.SetPropertyBlock(ghostBlock);
                World.TerrainView.SetBrush(local, 0.012f, ok ? new Color(0.6f, 1f, 0.5f) : new Color(1f, 0.4f, 0.35f), 0.8f);
            }
            if (!LetGo(input)) return;
            string error;
            if (!hit) error = "Point at the land";
            else if (person)
            {
                var p = carriedPerson.Value;
                if (!World.People.Walkable(p2)) error = "Villagers need dry land, off the rails";
                else { p.x = p2.x; p.z = p2.y; World.People.Add(p); error = null; }
            }
            else { var a = carriedAnimal.Value; error = World.PlaceAnimal(local, (AnimalKind)a.kind, a.seed); }
            carriedPerson = null; carriedAnimal = null; heldByTrigger = false;
            ghost.gameObject.SetActive(false);
            if (error != null) { World.Undo(false); Say(error + " - back where they were", 4f); Haptic(0.7f, 0.1f); }
            else { Say(person ? "Set down - they'll make themselves at home" : "Set down"); Haptic(0.5f, 0.05f); }
        }

        /// <summary>Bending a piece of track: a preview follows the pointer; letting go keeps the new shape.</summary>
        private void DragTrack(PointerInput input, bool hit, Vector3 local, ref Color colour)
        {
            colour = new Color(0.75f, 0.55f, 1f, 0.9f);
            var net = World.Track.Network;
            if (dragEdge >= net.Edges.Count) { dragEdge = -1; World.Track.HidePreview(); return; }
            if (hit && Time.time - dragPreviewAt > 0.08f)
            {
                dragPreviewAt = Time.time;
                dragError = World.ReshapePreview(dragEdge, dragIndex, local, out var preview);
                World.Track.ShowPreview(preview, false, dragError != null);
            }
            if (hit) World.TerrainView.SetBrush(local, 0.025f, dragError == null ? new Color(0.45f, 0.9f, 1f) : new Color(1f, 0.4f, 0.35f), 0.95f);
            if (!LetGo(input)) return;
            string error = hit ? World.Reshape(dragEdge, dragIndex, local) : "Point at the ground to put the track down";
            World.Track.HidePreview();
            Debug.Log($"Wonderland track: reshape {(error ?? "done")}");
            if (error != null) { Say("Can't bend it there: " + error, 4f); Haptic(0.7f, 0.1f); } else { Say("Track reshaped"); Haptic(0.5f, 0.05f); }
            dragEdge = -1; heldByTrigger = false;
        }

        /// <summary>Puts back whatever is held (switching tools, or opening a world): as if it had never been picked up.</summary>
        private void PutBack()
        {
            if (carrying) DropCarried();
            if (carriedTrain != null) { carriedTrain = null; World.Undo(false); }
            if (carriedPerson.HasValue || carriedAnimal.HasValue) { carriedPerson = null; carriedAnimal = null; World.Undo(false); ghost.gameObject.SetActive(false); }
            if (dragEdge >= 0) { dragEdge = -1; World.Track.HidePreview(); }
            heldByTrigger = false;
        }
    }
}
