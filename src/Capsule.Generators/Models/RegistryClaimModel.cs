namespace Capsule.Generators;

/// <summary>
/// What a registry claim attribute claims a key for. A logic assembly writes the value as an int
/// and the shell reads it back, so a member's value never changes.
/// </summary>
internal enum RegistryClaimKind
{
    Entity = 0,
    SceneDocument = 1,
    TileType = 2,
}

/// <summary>One key a referenced logic assembly claims, as its registry claim attribute records it.</summary>
/// <param name="DeclaringType">The claiming class, as a message names it.</param>
internal readonly record struct RegistryClaimModel(RegistryClaimKind Kind, string Key, string DeclaringType);
