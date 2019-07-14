using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GK6X
{
    // TODO: When there is better lighting / macro support, put these in the appropriate files

    public enum LightingEffectType
    {
        /// <summary>
        /// Static "DIY" lighting with an RGB value for each key
        /// </summary>
        Static = 0,
        /// <summary>
        /// Lighting effect with 1 or more frames of lighting
        /// </summary>
        Dynamic = 3
    }

    public enum LightingEffectColorType
    {
        /// <summary>
        /// Single solid color
        /// </summary>
        Monochrome = 0,
        /// <summary>
        /// Color which changes through the color spectrum
        /// </summary>
        RGB = 1,
        /// <summary>
        /// Color which changes through the color spectrum, and visually "breathes"
        /// </summary>
        Breathing = 2
    }

    /// <summary>
    /// Defines how a macro should be repeated when pressing a key bound to a macro
    /// </summary>
    public enum MacroRepeatType
    {
        /// <summary>
        /// Repeat the macro X number of times after the key is pressed (subsequent key presses are ignored until the
        /// macro has completely finished - there doesn't appear to be any way to stop the macro once it has started,
        /// and the key must be released and pressed again to start the macro again after it has finished)
        /// </summary>
        RepeatXTimes = 1,
        /// <summary>
        /// Release key to stop the macro (repeats the macro until the key is released (even if the macro is partially complete))
        /// </summary>
        ReleaseKeyToStop = 2,
        /// <summary>
        /// Press key a second time to stop the macro (repeats the macro until the key is pressed again)
        /// </summary>
        PressKeyAgainToStop = 3
    }

    // This is different compared to DriverValue
    public enum MacroKeyType
    {
        Key = 1,
        Mouse = 2
    }

    public enum MacroKeyState
    {
        Down = 1,
        Up = 2
    }
}
