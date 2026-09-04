namespace Capsule.Input;

/// <summary>
/// A physical gamepad button, face buttons named by position rather than by brand. The triggers
/// appear here as well as on <see cref="PadAxis"/>, pressed once pulled past the trigger deadzone.
/// Values must stay below <see cref="DeviceSnapshot.PadCapacity"/>.
/// </summary>
public enum PadButton
{
    /// <summary>No button. The default, so an unassigned <see cref="PadButton"/> never means a real button.</summary>
    None,

    /// <summary>Up on the directional pad.</summary>
    DPadUp,

    /// <summary>Down on the directional pad.</summary>
    DPadDown,

    /// <summary>Left on the directional pad.</summary>
    DPadLeft,

    /// <summary>Right on the directional pad.</summary>
    DPadRight,

    /// <summary>The bottom face button: Xbox A, PlayStation Cross, Nintendo B.</summary>
    South,

    /// <summary>The right face button: Xbox B, PlayStation Circle, Nintendo A.</summary>
    East,

    /// <summary>The left face button: Xbox X, PlayStation Square, Nintendo Y.</summary>
    West,

    /// <summary>The top face button: Xbox Y, PlayStation Triangle, Nintendo X.</summary>
    North,

    /// <summary>The left shoulder button, above the left trigger.</summary>
    LeftShoulder,

    /// <summary>The right shoulder button, above the right trigger.</summary>
    RightShoulder,

    /// <summary>The digital view of <see cref="PadAxis.LeftTrigger"/>, held whenever that axis reads above 0.</summary>
    LeftTrigger,

    /// <summary>The digital view of <see cref="PadAxis.RightTrigger"/>, held whenever that axis reads above 0.</summary>
    RightTrigger,

    /// <summary>The left stick pressed in.</summary>
    LeftStickClick,

    /// <summary>The right stick pressed in.</summary>
    RightStickClick,

    /// <summary>The primary menu button: Xbox Menu, PlayStation Options.</summary>
    Start,

    /// <summary>The secondary menu button: Xbox View, PlayStation Share.</summary>
    Select,
}
