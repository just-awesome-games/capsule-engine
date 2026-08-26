namespace Capsule.Scenes.Spawning;

/// <summary>
/// Declares the <c>type</c> string this class claims in level data — the same <c>type</c> a
/// level's entity records carry — and puts it in its assembly's generated registry. The type
/// defaults to the kebab-cased class name, so <c>HealthPickup</c> claims <c>health-pickup</c>,
/// unless one is given here. The class is a non-abstract <see cref="Entity"/> subclass with a
/// public constructor taking one <see cref="EntitySpawn"/>. Level types share one flat space
/// across the assembly, and two classes claiming one is a compile error.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LevelTypeAttribute : Attribute
{
    public LevelTypeAttribute()
    {
    }

    /// <param name="id">The level entity type this class claims, in place of its kebab-cased name.</param>
    public LevelTypeAttribute(string id) => Id = id;

    /// <summary>The explicit level type, or null where the class name decides it.</summary>
    public string? Id { get; }
}
