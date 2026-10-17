namespace Capsule.Generators;

/// <summary>One class the payload registry considered, and the name <c>--payload</c> reaches it by.</summary>
/// <param name="TypeName">The simple class name, which is the key <c>--payload</c> takes.</param>
/// <param name="At">Where a fault about this model is reported.</param>
internal readonly record struct ScenePayloadModel(
    string QualifiedName,
    string DisplayName,
    string TypeName,
    bool Accessible,
    DeclaredAt At);
