namespace Capsule.Input;

/// <summary>
/// An axis of the mouse wheel, bindable to an <see cref="AxisAction"/> beside a
/// <see cref="PadAxis"/>. It reads the notches turned this step, not a position, so it rests at 0 and is
/// not bounded to [-1, 1]. A wheel contribution is added to an action's value after the bounded
/// contributions are clamped. A flick of three notches reads 3, while a stick pushed to its stop
/// alongside it still reads 1.
/// </summary>
public enum MouseAxis
{
    /// <summary>Horizontal notches. Positive scrolls right.</summary>
    ScrollX,

    /// <summary>Vertical notches. Positive scrolls away from the user.</summary>
    ScrollY,
}
