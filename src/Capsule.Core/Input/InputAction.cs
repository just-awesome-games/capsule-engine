namespace Capsule.Input;

/// <summary>
/// A named thing the player can do, independent of what is bound to it. Equality is
/// ordinal over the name.
/// </summary>
public readonly record struct InputAction(string Name);
