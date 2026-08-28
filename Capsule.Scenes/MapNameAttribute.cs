namespace Capsule.Scenes;

/// <summary>
/// Overrides a map-backed scene's derived map name. The value must be a portable file stem and is
/// unique across the game's logic assemblies.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MapNameAttribute : Attribute
{
    /// <param name="name">The portable map file stem, without the <c>.map.json</c> suffix.</param>
    public MapNameAttribute(string name) => Name = name;

    /// <summary>The portable map file stem, without the <c>.map.json</c> suffix.</summary>
    public string Name { get; }
}
