namespace Capsule.Input;

/// <summary>
/// The family of gamepad the player last used, which decides what its buttons print, as
/// <see cref="InputState.PadFamily"/> reads it.
/// </summary>
public enum PadFamily : byte
{
    /// <summary>A pad of no known family, which prints what an <see cref="Xbox"/> pad does, and the default.</summary>
    Generic,

    /// <summary>An Xbox pad, or one with Xbox-lettered face buttons in Xbox positions.</summary>
    Xbox,

    /// <summary>A PlayStation pad.</summary>
    PlayStation,

    /// <summary>A Nintendo pad, a Joy-Con among them.</summary>
    Nintendo,
}
