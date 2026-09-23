namespace Capsule.Input;

/// <summary>A continuous gamepad axis. A sampled pad's snapshot carries it after the run's <see cref="InputConfiguration"/> deadzones.</summary>
public enum PadAxis
{
    /// <summary>No axis, and the default.</summary>
    None,

    /// <summary>Left stick, horizontal, in [-1, 1]. Positive is right.</summary>
    LeftStickX,

    /// <summary>Left stick, vertical, in [-1, 1]. Positive is the stick pushed up, whichever way the game's own Y points.</summary>
    LeftStickY,

    /// <summary>Right stick, horizontal, in [-1, 1]. Positive is right.</summary>
    RightStickX,

    /// <summary>Right stick, vertical, in [-1, 1]. Positive is the stick pushed up, whichever way the game's own Y points.</summary>
    RightStickY,

    /// <summary>Left trigger, in [0, 1]. Zero is released.</summary>
    LeftTrigger,

    /// <summary>Right trigger, in [0, 1]. Zero is released.</summary>
    RightTrigger,
}
