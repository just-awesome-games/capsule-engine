using Microsoft.CodeAnalysis;

namespace Capsule.Scenes.Generator;

internal enum SceneFault
{
    None,
    MapNameRequiresMapScene,
    UnsafeMapName,
    InaccessibleType,
    AmbiguousConstructors,
}

internal readonly struct SceneModel : IEquatable<SceneModel>
{
    internal SceneModel(string qualifiedName, string displayName, string? mapName, SceneFault fault, Location location)
    {
        QualifiedName = qualifiedName;
        DisplayName = displayName;
        MapName = mapName;
        Fault = fault;
        Location = location;
    }

    internal string QualifiedName { get; }

    internal string DisplayName { get; }

    internal string? MapName { get; }

    internal SceneFault Fault { get; }

    internal Location Location { get; }

    public static bool operator ==(SceneModel left, SceneModel right) => left.Equals(right);

    public static bool operator !=(SceneModel left, SceneModel right) => !left.Equals(right);

    public bool Equals(SceneModel other) =>
        Fault == other.Fault
        && string.Equals(QualifiedName, other.QualifiedName, StringComparison.Ordinal)
        && string.Equals(MapName, other.MapName, StringComparison.Ordinal)
        && Location.Equals(other.Location);

    public override bool Equals(object? obj) => obj is SceneModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + QualifiedName.GetHashCode();
        hash = (hash * 31) + (MapName is null ? 0 : MapName.GetHashCode());
        hash = (hash * 31) + (int)Fault;

        return hash;
    }
}
