// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE


using osu.Framework.Input.States;

namespace osu.Framework.Input.Events
{
    /// <summary>
    /// Represents a focus state change.
    /// </summary>
    public abstract class FocusEvent : UIEvent
    {
        protected FocusEvent(InputState state)
            : base(state)
        {
        }
    }
}
