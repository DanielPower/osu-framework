﻿// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using osu.Framework.Input.States;
using OpenTK;
using OpenTK.Input;

namespace osu.Framework.Input.Events
{
    /// <summary>
    /// Events for mouse dragging.
    /// </summary>
    public abstract class DragEvent : MouseActionEvent
    {
        protected DragEvent(InputState state, MouseButton button, Vector2? screenSpaceMouseDownPosition)
            : base(state, button, screenSpaceMouseDownPosition)
        {
        }
    }
}
