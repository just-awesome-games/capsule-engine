using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

internal enum EntityFault
{
    None,
    NotAConcreteEntity,
    MissingSpawnConstructor,
    BlankSpawnType,
    InaccessibleType,
    AmbiguousSpawnConstructors,
}

internal readonly struct EntityModel : IEquatable<EntityModel>
{
    internal EntityModel(
        string qualifiedName,
        string displayName,
        string containingNamespace,
        string typeName,
        string? declared,
        EntityFault fault,
        Location location)
    {
        QualifiedName = qualifiedName;
        DisplayName = displayName;
        ContainingNamespace = containingNamespace;
        TypeName = typeName;
        Declared = declared;
        Fault = fault;
        Location = location;
    }

    internal string QualifiedName { get; }

    internal string DisplayName { get; }

    internal string ContainingNamespace { get; }

    internal string TypeName { get; }

    /// <summary>The key <c>[SpawnType]</c> names, or null when the type claims one by convention.</summary>
    internal string? Declared { get; }

    internal EntityFault Fault { get; }

    internal Location Location { get; }

    public static bool operator ==(EntityModel left, EntityModel right) => left.Equals(right);

    public static bool operator !=(EntityModel left, EntityModel right) => !left.Equals(right);

    // Location participates in equality only for faulted models, so an unrelated edit does not re-emit the registry.
    public bool Equals(EntityModel other) =>
        Fault == other.Fault
        && string.Equals(QualifiedName, other.QualifiedName, StringComparison.Ordinal)
        && string.Equals(ContainingNamespace, other.ContainingNamespace, StringComparison.Ordinal)
        && string.Equals(TypeName, other.TypeName, StringComparison.Ordinal)
        && string.Equals(Declared, other.Declared, StringComparison.Ordinal)
        && (Fault == EntityFault.None || Location.Equals(other.Location));

    public override bool Equals(object? obj) => obj is EntityModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + QualifiedName.GetHashCode();
        hash = (hash * 31) + ContainingNamespace.GetHashCode();
        hash = (hash * 31) + TypeName.GetHashCode();
        hash = (hash * 31) + (Declared is null ? 0 : Declared.GetHashCode());
        hash = (hash * 31) + (int)Fault;

        return hash;
    }
}
