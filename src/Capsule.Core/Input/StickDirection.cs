namespace Capsule.Input;

/// <summary>
/// A stick pushed past <see cref="InputButton.StickPressPoint"/> in one direction, bindable beside a
/// <see cref="Key"/>, a <see cref="PadButton"/> or a <see cref="MouseButton"/>. Up is the positive
/// half of the stick's Y axis and down the negative half, right the positive half of X and left the
/// negative half, following <see cref="PadAxis.LeftStickY"/>, which is positive when the stick is
/// pushed up.
/// </summary>
public enum StickDirection
{
    /// <summary>No direction. The default, so an unassigned <see cref="StickDirection"/> never means a real one.</summary>
    None,

    /// <summary>Left stick pushed up.</summary>
    LeftStickUp,

    /// <summary>Left stick pushed down.</summary>
    LeftStickDown,

    /// <summary>Left stick pushed left.</summary>
    LeftStickLeft,

    /// <summary>Left stick pushed right.</summary>
    LeftStickRight,

    /// <summary>Right stick pushed up.</summary>
    RightStickUp,

    /// <summary>Right stick pushed down.</summary>
    RightStickDown,

    /// <summary>Right stick pushed left.</summary>
    RightStickLeft,

    /// <summary>Right stick pushed right.</summary>
    RightStickRight,
}
