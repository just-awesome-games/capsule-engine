namespace Capsule.Input;

/// <summary>
/// A named continuous thing the player can do, independent of what is bound to it. A
/// distinct type from <see cref="InputAction"/> so that the compiler, not a convention,
/// keeps a valued read off a boolean action and an edge read off a valued one. Equality
/// is ordinal over the name.
/// </summary>
public readonly record struct AxisAction(string Name);
