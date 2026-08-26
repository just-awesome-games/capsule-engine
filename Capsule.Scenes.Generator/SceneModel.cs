using Microsoft.CodeAnalysis;

namespace Capsule.Scenes.Generator;

/// <summary>
/// One scene class as the pipeline carries it. Symbols are flattened to strings here: a model that
/// outlives its compilation would pin the whole one in the generator cache.
/// </summary>
internal readonly struct SceneModel : IEquatable<SceneModel>
{
    internal SceneModel(string qualifiedName, string displayName, string? mapName, Location location)
    {
        QualifiedName = qualifiedName;
        DisplayName = displayName;
        MapName = mapName;
        Location = location;
    }

    /// <summary>The type as generated code must name it, <c>global::</c> and all.</summary>
    internal string QualifiedName { get; }

    /// <summary>The type as a diagnostic message names it.</summary>
    internal string DisplayName { get; }

    /// <summary>The map composed into it, or null when no map backs it.</summary>
    internal string? MapName { get; }

    internal Location Location { get; }

    public static bool operator ==(SceneModel left, SceneModel right) => left.Equals(right);

    public static bool operator !=(SceneModel left, SceneModel right) => !left.Equals(right);

    public bool Equals(SceneModel other) =>
        string.Equals(QualifiedName, other.QualifiedName, StringComparison.Ordinal)
        && string.Equals(MapName, other.MapName, StringComparison.Ordinal)
        && Location.Equals(other.Location);

    public override bool Equals(object? obj) => obj is SceneModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + QualifiedName.GetHashCode();
        hash = (hash * 31) + (MapName is null ? 0 : MapName.GetHashCode());

        return hash;
    }
}
