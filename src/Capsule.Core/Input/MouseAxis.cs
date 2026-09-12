namespace Capsule.Input;

/// <summary>
/// An axis of the mouse wheel, bindable to an <see cref="AxisAction"/> beside a <see cref="PadAxis"/>.
/// It reads the notches turned this step rather than a position, so it rests at 0 and is not bounded
/// to [-1, 1]: a wheel contribution is added to an action's value after the bounded contributions are
/// clamped, so a flick of three notches reads 3 and a stick pushed to its stop alongside it still
/// reads no further than 1.
/// </summary>
public enum MouseAxis
{
    /// <summary>Horizontal notches; positive scrolls right.</summary>
    ScrollX,

    /// <summary>Vertical notches; positive scrolls away from the user.</summary>
    ScrollY,
}
