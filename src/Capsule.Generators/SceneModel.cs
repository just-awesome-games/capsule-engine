namespace Capsule.Generators;

internal enum SceneFault
{
    None,
    SceneDocumentRequiresContentConstructor,
    InaccessibleType,
    AmbiguousConstructors,
    NotAbstract,
    AmbiguousBaseConstructors,
}

/// <summary>
/// One class deriving <see cref="Capsule.Scenes.Scene"/>, abstract included, described once for
/// both keys a scene document can resolve to it. A document's own key composes it when
/// <see cref="Registrable"/> and <see cref="Fault"/> is <see cref="SceneFault.None"/>. A document's
/// <c>baseScene</c> key names it when <see cref="BaseFault"/> is <see cref="SceneFault.None"/>.
/// </summary>
/// <param name="Documented">Whether a document composes this scene.</param>
/// <param name="Declared">The key <c>[SceneDocument]</c> names, or null when the type claims one by convention.</param>
/// <param name="Fault">Why a registration candidate cannot register.</param>
/// <param name="Registrable">Whether this class is a registration candidate at all.</param>
/// <param name="Abstract">Whether the class is abstract, the shape a baseScene's generated subclass needs.</param>
/// <param name="DerivableContentConstructors">Constructors taking <c>SceneContent</c> a derived type in this assembly can call.</param>
/// <param name="AccessibleType">Whether the class itself is reachable from generated code.</param>
/// <param name="At">Where a fault about this model is reported.</param>
internal readonly record struct SceneModel(
    string QualifiedName,
    string DisplayName,
    string ContainingNamespace,
    string TypeName,
    bool Documented,
    string? Declared,
    SceneFault Fault,
    bool Registrable,
    bool Abstract,
    int DerivableContentConstructors,
    bool AccessibleType,
    DeclaredAt At)
{
    /// <summary>Why a document's baseScene cannot name this class, or <see cref="SceneFault.None"/> when it can.</summary>
    internal SceneFault BaseFault =>
        !Abstract ? SceneFault.NotAbstract
        : !AccessibleType ? SceneFault.InaccessibleType
        : DerivableContentConstructors != 1 ? SceneFault.AmbiguousBaseConstructors
        : SceneFault.None;
}

/// <summary>
/// One scene document the build ships, keyed the way the asset hook already keys it, with the two
/// top-level fields <see cref="SceneRegistrySource"/> resolves before the document is otherwise read.
/// </summary>
internal readonly record struct SceneDocumentInfo(string Key, string? BaseScene, string? Camera);

/// <summary>One class a scene document's <c>camera</c> key can name, and the key it claims by convention.</summary>
internal readonly record struct CameraModel(
    string QualifiedName,
    string DisplayName,
    string ContainingNamespace,
    string TypeName,
    bool Concrete,
    bool AccessibleParameterless,
    DeclaredAt At)
{
    internal bool Valid => Concrete && AccessibleParameterless;
}
