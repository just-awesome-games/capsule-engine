namespace Capsule.Scenes.Spawning;

/// <summary>
/// Fixes the <see cref="EntitySpawn.Type"/> this class claims, in place of its kebab-cased name:
/// <c>HealthPickup</c> claims <c>health-pickup</c> on its own, and this is how it keeps claiming
/// that after a rename. Derivation breaks a word before an upper-case letter and around a run of
/// digits, so <c>Enemy2</c> claims <c>enemy-2</c>. A class reaches its assembly's generated
/// registry by being a non-abstract <see cref="Entity"/> subclass with a public constructor
/// taking one <see cref="EntitySpawn"/>, with or without this; one of any other shape is passed
/// over silently rather than diagnosed. Types share one flat space across the assembly, and two
/// classes claiming one is a compile error.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SpawnTypeAttribute : Attribute
{
    /// <param name="type">The spawn type this class claims, in place of its kebab-cased name.</param>
    public SpawnTypeAttribute(string type) => Type = type;

    /// <summary>The spawn type this class claims.</summary>
    public string Type { get; }
}
