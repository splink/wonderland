using UnityEngine;

namespace Wonderland
{
    /// <summary>One frame of controller input, from the Quest controllers or a scripted/desktop source.</summary>
    public struct PointerInput
    {
        public bool Valid;
        public Ray Ray;
        public bool TriggerDown, Trigger, TriggerUp;
        public bool GripDown, Grip, GripUp;
        public bool PrimaryDown, SecondaryDown;      // A / B on the right controller
        public bool MenuDown;                        // Y on the left controller: hide / show the palette
        public Vector2 Stick;
    }

    /// <summary>
    /// Quest Touch controllers via OVRInput: right hand points and acts, left hand holds the palette. The trigger has
    /// hysteresis so a half-pressed trigger does not chatter; haptic ticks confirm actions.
    /// </summary>
    public sealed class QuestInput
    {
        private bool trigger, grip;
        private float hapticUntil;

        public PointerInput Read(Transform rightAnchor)
        {
            var input = new PointerInput();
            if (rightAnchor == null) return input;
            const OVRInput.Controller right = OVRInput.Controller.RTouch;
            float t = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, right);
            float g = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, right);
            bool newTrigger = trigger ? t > 0.35f : t > 0.6f;
            bool newGrip = grip ? g > 0.35f : g > 0.6f;
            input.TriggerDown = newTrigger && !trigger; input.TriggerUp = !newTrigger && trigger; input.Trigger = newTrigger;
            input.GripDown = newGrip && !grip; input.GripUp = !newGrip && grip; input.Grip = newGrip;
            trigger = newTrigger; grip = newGrip;
            input.PrimaryDown = OVRInput.GetDown(OVRInput.Button.One, right);
            input.SecondaryDown = OVRInput.GetDown(OVRInput.Button.Two, right);
            input.MenuDown = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch);
            input.Stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, right);
            // Point slightly downwards from the controller's nose: a relaxed wrist then aims at the floor-level world.
            var dir = rightAnchor.rotation * Quaternion.Euler(12f, 0f, 0f) * Vector3.forward;
            input.Ray = new Ray(rightAnchor.position, dir);
            input.Valid = OVRInput.IsControllerConnected(right) || Application.isEditor;
            if (hapticUntil > 0f && Time.time > hapticUntil) { OVRInput.SetControllerVibration(0f, 0f, right); hapticUntil = 0f; }
            return input;
        }

        public void Haptic(float amplitude, float seconds)
        {
            OVRInput.SetControllerVibration(0.5f, Mathf.Clamp01(amplitude), OVRInput.Controller.RTouch);
            hapticUntil = Time.time + seconds;
        }
    }
}
