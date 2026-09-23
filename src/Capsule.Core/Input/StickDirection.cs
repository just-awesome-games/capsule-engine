namespace Capsule.Input;

/// <summary>
/// A stick pushed past <see cref="InputButton.StickPressPoint"/> in one direction, bindable beside
/// a <see cref="Key"/>, a <see cref="PadButton"/> or a <see cref="MouseButton"/>. Up is the
/// positive half of the stick's Y axis and right the positive half of X, following
/// <see cref="PadAxis.LeftStickY"/>, which is positive when the stick is pushed up.
/// </summary>
/// <remarks><see cref="None"/> is the default and names no direction.</remarks>
#pragma warning disable CS1591 // Each member's name is the stick and direction it stands for.
public enum StickDirection
{
    None,

    LeftStickUp,
    LeftStickDown,
    LeftStickLeft,
    LeftStickRight,

    RightStickUp,
    RightStickDown,
    RightStickLeft,
    RightStickRight,
}
#pragma warning restore CS1591
