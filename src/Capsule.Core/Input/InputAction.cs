namespace Capsule.Input;

/// <summary>A named thing the player can do, independent of what is bound to it. Equality is ordinal.</summary>
public readonly record struct InputAction(string Name);
