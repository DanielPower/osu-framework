// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Framework.Input.StateChanges;
using osu.Framework.Input.States;
using osuTK;
using osuTK.Input;
using JoystickState = osu.Framework.Input.States.JoystickState;

namespace osu.Framework.Input
{
    /// <summary>An <see cref="InputManager"/> with an ability to use parent <see cref="InputManager"/>'s input.</summary>
    /// <remarks>
    /// There are two modes of operation for this class:
    /// When <see cref="UseParentInput"/> is false, this input manager gets inputs only from own input handlers.
    /// When <see cref="UseParentInput"/> is true, this input manager ignore any local input handlers and
    /// gets inputs from the parent (its ancestor in the scene graph) <see cref="InputManager"/> in the following way:
    /// For mouse input, this only considers input that is passed as events such as <see cref="MouseDownEvent"/>.
    /// For keyboard and other inputs, this input manager try to reflect parent <see cref="InputManager"/>'s <see cref="InputState"/> closely as possible.
    /// Thus, when this is attached to the scene graph initially and when <see cref="UseParentInput"/> becomes true,
    /// multiple events may fire to synchronise the local <see cref="InputState"/> with the parent's.
    /// </remarks>
    public partial class PassThroughInputManager : CustomInputManager, IRequireHighFrequencyMousePosition
    {
        /// <summary>
        /// If there's an InputManager above us, decide whether we should use their available state.
        /// </summary>
        public virtual bool UseParentInput
        {
            get => useParentInput;
            set
            {
                if (useParentInput == value) return;

                useParentInput = value;

                if (UseParentInput)
                    Sync();
            }
        }

        private bool useParentInput = true;

        public override bool HandleHoverEvents => UseParentInput ? parentInputManager.HandleHoverEvents : base.HandleHoverEvents;

        internal override bool BuildNonPositionalInputQueue(List<Drawable> queue, bool allowBlocking = true)
        {
            if (!PropagateNonPositionalInputSubTree) return false;

            if (!allowBlocking)
                base.BuildNonPositionalInputQueue(queue, false);
            else
                queue.Add(this);

            return false;
        }

        internal override bool BuildPositionalInputQueue(Vector2 screenSpacePos, List<Drawable> queue)
        {
            if (!PropagatePositionalInputSubTree) return false;

            queue.Add(this);
            return false;
        }

        protected override List<IInput> GetPendingInputs()
        {
            //we still want to call the base method to clear any pending states that may build up.
            var pendingInputs = base.GetPendingInputs();

            if (UseParentInput)
            {
                pendingInputs.Clear();
            }

            return pendingInputs;
        }

        protected override bool Handle(UIEvent e)
        {
            if (!UseParentInput) return false;

            // Don't handle mouse events sourced from touches, we may have a
            // child drawable handling actual touches, we will produce one ourselves.
            if (e is MouseEvent && e.CurrentState.Mouse.LastSource is ISourcedFromTouch)
                return false;

            switch (e)
            {
                case MouseMoveEvent mouseMove:
                    if (mouseMove.ScreenSpaceMousePosition != CurrentState.Mouse.Position)
                        new MousePositionAbsoluteInput { Position = mouseMove.ScreenSpaceMousePosition }.Apply(CurrentState, this);
                    break;

                case MouseDownEvent mouseDown:
                    // safe-guard for edge cases.
                    if (!CurrentState.Mouse.IsPressed(mouseDown.Button))
                        new MouseButtonInput(mouseDown.Button, true).Apply(CurrentState, this);
                    break;

                case MouseUpEvent mouseUp:
                    // safe-guard for edge cases.
                    if (CurrentState.Mouse.IsPressed(mouseUp.Button))
                        new MouseButtonInput(mouseUp.Button, false).Apply(CurrentState, this);
                    break;

                case ScrollEvent scroll:
                    new MouseScrollRelativeInput { Delta = scroll.ScrollDelta, IsPrecise = scroll.IsPrecise }.Apply(CurrentState, this);
                    break;

                case TouchEvent touch:
                    new TouchInput(touch.ScreenSpaceTouch, touch.IsActive(touch.ScreenSpaceTouch)).Apply(CurrentState, this);
                    break;

                case MidiEvent midi:
                    new MidiKeyInput(midi.Key, midi.Velocity, midi.IsPressed(midi.Key)).Apply(CurrentState, this);
                    break;

                case KeyboardEvent:
                case JoystickButtonEvent:
                case JoystickAxisMoveEvent:
                case TabletPenButtonEvent:
                case TabletAuxiliaryButtonEvent:
                    SyncInputState(e.CurrentState);
                    break;
            }

            return false;
        }

        private InputManager parentInputManager;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Sync();
        }

        protected override void Update()
        {
            base.Update();

            // Some non-positional events are blocked. Sync every frame.
            if (UseParentInput) Sync(true);
        }

        /// <summary>
        /// Sync input state to parent <see cref="InputManager"/>'s <see cref="InputState"/>.
        /// Call this when parent <see cref="InputManager"/> changed somehow.
        /// </summary>
        /// <param name="useCachedParentInputManager">If this is false, assume parent input manager is unchanged from before.</param>
        public void Sync(bool useCachedParentInputManager = false)
        {
            if (!UseParentInput) return;

            if (!useCachedParentInputManager)
                parentInputManager = GetContainingInputManager();

            SyncInputState(parentInputManager?.CurrentState);
        }

        /// <summary>
        /// Sync current state to a certain state.
        /// </summary>
        /// <param name="state">The state to synchronise current with. If this is null, it is regarded as an empty state.</param>
        protected virtual void SyncInputState(InputState state)
        {
            // invariant: if mouse button is currently pressed, then it has been pressed in parent (but not the converse)
            // therefore, mouse up events are always synced from parent
            // mouse down events are not synced to prevent false clicks
            var mouseDiff = (state?.Mouse?.Buttons ?? new ButtonStates<MouseButton>()).EnumerateDifference(CurrentState.Mouse.Buttons);
            if (mouseDiff.Released.Length > 0)
                new MouseButtonInput(mouseDiff.Released.Select(button => new ButtonInputEntry<MouseButton>(button, false))).Apply(CurrentState, this);

            var keyDiff = (state?.Keyboard.Keys ?? new ButtonStates<Key>()).EnumerateDifference(CurrentState.Keyboard.Keys);
            foreach (var key in keyDiff.Released)
                new KeyboardKeyInput(key, false).Apply(CurrentState, this);
            foreach (var key in keyDiff.Pressed)
                new KeyboardKeyInput(key, true).Apply(CurrentState, this);

            var touchDiff = (state?.Touch ?? new TouchState()).EnumerateDifference(CurrentState.Touch);
            if (touchDiff.deactivated.Length > 0)
                new TouchInput(touchDiff.deactivated, false).Apply(CurrentState, this);
            if (touchDiff.activated.Length > 0)
                new TouchInput(touchDiff.activated, true).Apply(CurrentState, this);

            var joyButtonDiff = (state?.Joystick?.Buttons ?? new ButtonStates<JoystickButton>()).EnumerateDifference(CurrentState.Joystick.Buttons);
            foreach (var button in joyButtonDiff.Released)
                new JoystickButtonInput(button, false).Apply(CurrentState, this);
            foreach (var button in joyButtonDiff.Pressed)
                new JoystickButtonInput(button, true).Apply(CurrentState, this);

            // Basically only perform the full state diff if we have found that any axis changed.
            // This avoids unnecessary alloc overhead.
            for (int i = 0; i < JoystickState.MAX_AXES; i++)
            {
                if (state?.Joystick?.AxesValues[i] != CurrentState.Joystick.AxesValues[i])
                {
                    new JoystickAxisInput(state?.Joystick?.GetAxes() ?? Array.Empty<JoystickAxis>()).Apply(CurrentState, this);
                    break;
                }
            }

            var midiDiff = (state?.Midi?.Keys ?? new ButtonStates<MidiKey>()).EnumerateDifference(CurrentState.Midi.Keys);
            foreach (var key in midiDiff.Released)
                new MidiKeyInput(key, CurrentState.Midi.Velocities[key], false).Apply(CurrentState, this);
            foreach (var key in midiDiff.Pressed)
                new MidiKeyInput(key, state!.Midi!.Velocities[key], true).Apply(CurrentState, this);

            var tabletPenDiff = (state?.Tablet?.PenButtons ?? new ButtonStates<TabletPenButton>()).EnumerateDifference(CurrentState.Tablet.PenButtons);
            foreach (var button in tabletPenDiff.Released)
                new TabletPenButtonInput(button, false).Apply(CurrentState, this);
            foreach (var button in tabletPenDiff.Pressed)
                new TabletPenButtonInput(button, true).Apply(CurrentState, this);

            var tabletAuxiliaryDiff = (state?.Tablet?.AuxiliaryButtons ?? new ButtonStates<TabletAuxiliaryButton>()).EnumerateDifference(CurrentState.Tablet.AuxiliaryButtons);
            foreach (var button in tabletAuxiliaryDiff.Released)
                new TabletAuxiliaryButtonInput(button, false).Apply(CurrentState, this);
            foreach (var button in tabletAuxiliaryDiff.Pressed)
                new TabletAuxiliaryButtonInput(button, true).Apply(CurrentState, this);
        }
    }
}
