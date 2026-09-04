namespace Capsule.Scenes.Spawning;

/// <summary>
/// Overrides the <see cref="EntitySpawn.Type"/> an entity class derives from where it is declared.
/// The value is a whole key — '/'-joined segments of ASCII letters, digits, hyphens and
/// underscores, none of them a reserved Windows device name.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SpawnTypeAttribute : Attribute
{
    /// <param name="type">The spawn type this class claims, in place of the key its namespace names.</param>
    public SpawnTypeAttribute(string type) => Type = type;

    /// <summary>The spawn type this class claims.</summary>
    public string Type { get; }
}
