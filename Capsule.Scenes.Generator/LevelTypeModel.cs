using Microsoft.CodeAnalysis;

namespace Capsule.Scenes.Generator;

internal enum LevelTypeFault
{
    None,
    NotAConcreteEntity,
    MissingSpawnConstructor,
    BlankType,
}

/// <summary>
/// One <c>[LevelType]</c> class as the pipeline carries it. Symbols are flattened to strings
/// here: a model that outlives its compilation would pin the whole one in the generator cache.
/// </summary>
internal readonly struct LevelTypeModel : IEquatable<LevelTypeModel>
{
    internal LevelTypeModel(string qualifiedName, string displayName, string id, LevelTypeFault fault, Location location)
    {
        QualifiedName = qualifiedName;
        DisplayName = displayName;
        Id = id;
        Fault = fault;
        Location = location;
    }

    /// <summary>The type as generated code must name it, <c>global::</c> and all.</summary>
    internal string QualifiedName { get; }

    /// <summary>The type as a diagnostic message names it.</summary>
    internal string DisplayName { get; }

    internal string Id { get; }

    internal LevelTypeFault Fault { get; }

    internal Location Location { get; }

    public static bool operator ==(LevelTypeModel left, LevelTypeModel right) => left.Equals(right);

    public static bool operator !=(LevelTypeModel left, LevelTypeModel right) => !left.Equals(right);

    public bool Equals(LevelTypeModel other) =>
        Fault == other.Fault
        && string.Equals(QualifiedName, other.QualifiedName, StringComparison.Ordinal)
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && Location.Equals(other.Location);

    public override bool Equals(object? obj) => obj is LevelTypeModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + QualifiedName.GetHashCode();
        hash = (hash * 31) + Id.GetHashCode();
        hash = (hash * 31) + (int)Fault;

        return hash;
    }
}
