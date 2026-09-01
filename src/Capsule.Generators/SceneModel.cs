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
    internal SceneModel(string qualifiedName, string displayName, string? documentName, SceneFault fault, Location location)
    {
        QualifiedName = qualifiedName;
        DisplayName = displayName;
        DocumentName = documentName;
        Fault = fault;
        Location = location;
    }

    internal string QualifiedName { get; }

    internal string DisplayName { get; }

    internal string? DocumentName { get; }

    internal SceneFault Fault { get; }

    internal Location Location { get; }

    public static bool operator ==(SceneModel left, SceneModel right) => left.Equals(right);

    public static bool operator !=(SceneModel left, SceneModel right) => !left.Equals(right);

    // Location participates in equality only for faulted models, so an unrelated edit does not re-emit the registry.
    public bool Equals(SceneModel other) =>
        Fault == other.Fault
        && string.Equals(QualifiedName, other.QualifiedName, StringComparison.Ordinal)
        && string.Equals(DocumentName, other.DocumentName, StringComparison.Ordinal)
        && (Fault == SceneFault.None || Location.Equals(other.Location));

    public override bool Equals(object? obj) => obj is SceneModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + QualifiedName.GetHashCode();
        hash = (hash * 31) + (DocumentName is null ? 0 : DocumentName.GetHashCode());
        hash = (hash * 31) + (int)Fault;

        return hash;
    }
}
