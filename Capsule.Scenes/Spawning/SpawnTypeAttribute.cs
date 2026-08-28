namespace Capsule.Scenes.Spawning;

/// <summary>Overrides the kebab-cased <see cref="EntitySpawn.Type"/> claimed by an entity class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SpawnTypeAttribute : Attribute
{
    /// <param name="type">The spawn type this class claims, in place of its kebab-cased name.</param>
    public SpawnTypeAttribute(string type) => Type = type;

    /// <summary>The spawn type this class claims.</summary>
    public string Type { get; }
}
