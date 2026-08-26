using Microsoft.CodeAnalysis;

namespace Capsule.Scenes.Generator;

internal enum EntityFault
{
    None,
    NotAConcreteEntity,
    MissingSpawnConstructor,
    BlankSpawnType,
}

/// <summary>
/// One candidate class as the pipeline carries it. Symbols are flattened to strings here: a model
/// that outlives its compilation would pin the whole one in the generator cache.
/// </summary>
internal readonly struct EntityModel : IEquatable<EntityModel>
{
    internal EntityModel(string qualifiedName, string displayName, string spawnType, EntityFault fault, Location location)
    {
        QualifiedName = qualifiedName;
        DisplayName = displayName;
        SpawnType = spawnType;
        Fault = fault;
        Location = location;
    }

    /// <summary>The type as generated code must name it, <c>global::</c> and all.</summary>
    internal string QualifiedName { get; }

    /// <summary>The type as a diagnostic message names it.</summary>
    internal string DisplayName { get; }

    internal string SpawnType { get; }

    internal EntityFault Fault { get; }

    internal Location Location { get; }

    public static bool operator ==(EntityModel left, EntityModel right) => left.Equals(right);

    public static bool operator !=(EntityModel left, EntityModel right) => !left.Equals(right);

    public bool Equals(EntityModel other) =>
        Fault == other.Fault
        && string.Equals(QualifiedName, other.QualifiedName, StringComparison.Ordinal)
        && string.Equals(SpawnType, other.SpawnType, StringComparison.Ordinal)
        && Location.Equals(other.Location);

    public override bool Equals(object? obj) => obj is EntityModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + QualifiedName.GetHashCode();
        hash = (hash * 31) + SpawnType.GetHashCode();
        hash = (hash * 31) + (int)Fault;

        return hash;
    }
}
