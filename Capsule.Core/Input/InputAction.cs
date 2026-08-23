namespace Capsule.Input;

/// <summary>
/// A named thing the player can do, independent of what is bound to it. Games
/// declare their actions once as <c>static readonly</c> fields and pass those
/// around; the name exists for diagnostics, and equality is ordinal over it.
/// </summary>
public readonly record struct InputAction(string Name);
