namespace Capsule.Scenes;

/// <summary>
/// Fixes the authored map name a map-backed scene claims in place of its kebab-cased class name,
/// so an authored identity outlives a class rename. Derivation breaks a word before an upper-case
/// letter and around a run of digits: <c>CaveEntrance</c> claims <c>cave-entrance</c> and
/// <c>Room01</c> claims <c>room-01</c>. The scene still enters the generated registry by having
/// one public constructor taking a <see cref="MapSceneContext"/>, and a class of neither registry
/// shape is passed over silently rather than diagnosed — a scene the game's own code constructs
/// is an ordinary class. Names share one flat space across all game-logic assemblies, and each
/// must be a portable file stem: ASCII letters, digits, hyphens and underscores.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MapNameAttribute : Attribute
{
    /// <param name="name">The portable map file stem, without the <c>.map.json</c> suffix.</param>
    public MapNameAttribute(string name) => Name = name;

    /// <summary>The portable map file stem, without the <c>.map.json</c> suffix.</summary>
    public string Name { get; }
}
