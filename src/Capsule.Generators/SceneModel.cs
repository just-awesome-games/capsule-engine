using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

internal enum SceneFault
{
    None,
    SceneDocumentRequiresContentConstructor,
    UnsafeDocumentName,
    InaccessibleType,
    AmbiguousConstructors,
}

internal readonly struct SceneModel : IEquatable<SceneModel>
{
    internal SceneModel(
        string qualifiedName,
        string displayName,
        string containingNamespace,
        string typeName,
        bool documented,
        string? declared,
        SceneFault fault,
        Location location)
    {
        QualifiedName = qualifiedName;
        DisplayName = displayName;
        ContainingNamespace = containingNamespace;
        TypeName = typeName;
        Documented = documented;
        Declared = declared;
        Fault = fault;
        Location = location;
    }

    internal string QualifiedName { get; }

    internal string DisplayName { get; }

    internal string ContainingNamespace { get; }

    internal string TypeName { get; }

    /// <summary>Whether a document composes this scene at all.</summary>
    internal bool Documented { get; }

    /// <summary>The key <c>[SceneDocument]</c> names, or null when the type claims one by convention.</summary>
    internal string? Declared { get; }

    internal SceneFault Fault { get; }

    internal Location Location { get; }

    public static bool operator ==(SceneModel left, SceneModel right) => left.Equals(right);

    public static bool operator !=(SceneModel left, SceneModel right) => !left.Equals(right);

    // Location participates in equality only for faulted models, so an unrelated edit does not re-emit the registry.
    public bool Equals(SceneModel other) =>
        Fault == other.Fault
        && Documented == other.Documented
        && string.Equals(QualifiedName, other.QualifiedName, StringComparison.Ordinal)
        && string.Equals(ContainingNamespace, other.ContainingNamespace, StringComparison.Ordinal)
        && string.Equals(TypeName, other.TypeName, StringComparison.Ordinal)
        && string.Equals(Declared, other.Declared, StringComparison.Ordinal)
        && (Fault == SceneFault.None || Location.Equals(other.Location));

    public override bool Equals(object? obj) => obj is SceneModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + QualifiedName.GetHashCode();
        hash = (hash * 31) + ContainingNamespace.GetHashCode();
        hash = (hash * 31) + TypeName.GetHashCode();
        hash = (hash * 31) + (Declared is null ? 0 : Declared.GetHashCode());
        hash = (hash * 31) + (Documented ? 1 : 0);
        hash = (hash * 31) + (int)Fault;

        return hash;
    }
}
