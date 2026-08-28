namespace Capsule.Input;

/// <summary>
/// A continuous gamepad axis. Values in a <see cref="DeviceSnapshot"/> are already
/// past the backend's deadzone filtering.
/// </summary>
public enum PadAxis
{
    /// <summary>No axis. The default, so an unassigned <see cref="PadAxis"/> never means a real axis.</summary>
    None,

    /// <summary>Left stick, horizontal, in [-1, 1]; positive is right.</summary>
    LeftStickX,

    /// <summary>Left stick, vertical, in [-1, 1]; positive is the stick pushed up, whichever way the game's own Y points.</summary>
    LeftStickY,

    /// <summary>Right stick, horizontal, in [-1, 1]; positive is right.</summary>
    RightStickX,

    /// <summary>Right stick, vertical, in [-1, 1]; positive is the stick pushed up, whichever way the game's own Y points.</summary>
    RightStickY,

    /// <summary>Left trigger, in [0, 1]; 0 is released.</summary>
    LeftTrigger,

    /// <summary>Right trigger, in [0, 1]; 0 is released.</summary>
    RightTrigger,
}
