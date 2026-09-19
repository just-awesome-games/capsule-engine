namespace Capsule.Generators;

internal enum SceneFault
{
    None,
    SceneDocumentRequiresContentConstructor,
    InaccessibleType,
    AmbiguousConstructors,
}

/// <summary>One class the scene registry considered, with the key it claims or the fault it carries.</summary>
/// <param name="Documented">Whether a document composes this scene.</param>
/// <param name="Declared">The key <c>[SceneDocument]</c> names, or null when the type claims one by convention.</param>
/// <param name="At">Where a fault about this model is reported.</param>
internal readonly record struct SceneModel(
    string QualifiedName,
    string DisplayName,
    string ContainingNamespace,
    string TypeName,
    bool Documented,
    string? Declared,
    SceneFault Fault,
    DeclaredAt At);
