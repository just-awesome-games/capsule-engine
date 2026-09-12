namespace Capsule.Input;

/// <summary>
/// A physical mouse button, bindable beside a <see cref="Key"/> or a <see cref="PadButton"/>. Values
/// must stay below <see cref="DeviceSnapshot.MouseCapacity"/>.
/// </summary>
public enum MouseButton
{
    /// <summary>No button. The default, so an unassigned <see cref="MouseButton"/> never means a real button.</summary>
    None,

    /// <summary>The primary button, which is the left one under a right-handed mouse mapping.</summary>
    Left,

    /// <summary>The secondary button, which is the right one under a right-handed mouse mapping.</summary>
    Right,

    /// <summary>The wheel pressed in.</summary>
    Middle,

    /// <summary>The first extra button, where the mouse carries one.</summary>
    X1,

    /// <summary>The second extra button, where the mouse carries one.</summary>
    X2,
}
