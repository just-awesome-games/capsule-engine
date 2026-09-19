namespace Capsule.Scenes.Spawning;

/// <summary>
/// Overrides the <see cref="EntitySpawn.Type"/> an entity class would otherwise derive from its namespace.
/// The value is a complete key: '/'-joined segments of ASCII letters, digits, hyphens and underscores, and no
/// segment may be a reserved Windows device name.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SpawnTypeAttribute : Attribute
{
    /// <param name="type">The spawn type this class claims in place of the key its namespace names.</param>
    public SpawnTypeAttribute(string type) => Type = type;

    /// <summary>The spawn type this class claims.</summary>
    public string Type { get; }
}
