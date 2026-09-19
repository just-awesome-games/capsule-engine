namespace Capsule.Input;

/// <summary>A continuous gamepad axis, already past the backend's deadzone filtering.</summary>
public enum PadAxis
{
    /// <summary>No axis. It is the default, and an unassigned <see cref="PadAxis"/> does not mean a real axis.</summary>
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
