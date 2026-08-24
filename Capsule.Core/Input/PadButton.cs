namespace Capsule.Input;

/// <summary>
/// A physical gamepad button. Face buttons are named by position rather than by brand:
/// <see cref="South"/> is Xbox A / PS Cross, <see cref="East"/> is Xbox B / PS Circle,
/// <see cref="West"/> is Xbox X / PS Square, <see cref="North"/> is Xbox Y / PS Triangle.
/// Values must stay below <see cref="DeviceSnapshot.PadCapacity"/>; the backend maps each one.
/// </summary>
public enum PadButton
{
    /// <summary>No button. The default, so an unassigned <see cref="PadButton"/> never means a real button.</summary>
    None,

    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,

    South,
    East,
    West,
    North,

    LeftShoulder,
    RightShoulder,

    LeftStickClick,
    RightStickClick,

    Start,
    Select,
}
