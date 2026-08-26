namespace Capsule.Scenes;

/// <summary>
/// Fixes the authored map name a map-backed scene claims in place of its kebab-cased class name.
/// The scene still enters the generated registry by having one public constructor taking a
/// <see cref="MapSceneContext"/>. Names share one flat space across all game-logic assemblies.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MapNameAttribute : Attribute
{
    /// <param name="name">The portable map file stem, without the <c>.map.json</c> suffix.</param>
    public MapNameAttribute(string name) => Name = name;

    /// <summary>The portable map file stem, without the <c>.map.json</c> suffix.</summary>
    public string Name { get; }
}
