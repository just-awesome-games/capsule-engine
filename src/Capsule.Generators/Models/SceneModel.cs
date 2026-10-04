namespace Capsule.Generators;

internal enum SceneFault
{
    None,
    TypeKeyRequiresContentConstructor,
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
/// <param name="Declared">The key <c>[TypeKey]</c> names, or null when the type claims one by convention.</param>
/// <param name="Fault">Why a registration candidate cannot register.</param>
/// <param name="Registrable">Whether this class is a registration candidate at all.</param>
/// <param name="Abstract">Whether the class is abstract, the shape a baseScene's generated subclass needs.</param>
/// <param name="Generic">Whether the class declares type parameters, which generated code cannot name it without.</param>
/// <param name="DerivableContentConstructors">Constructors taking <c>SceneContent</c> a derived type in this assembly can call.</param>
/// <param name="AccessibleType">Whether the class itself is reachable from generated code.</param>
/// <param name="At">Where a fault about this model is reported.</param>
/// <param name="Properties">What a document's top-level keys may name, as <see cref="PropertySchema.Of"/> finds it.</param>
/// <param name="Objects">Every class a member's JSON object fills or constructs, as <see cref="PropertySchema.WithObjects"/> finds it.</param>
/// <param name="ContentModifier">How the content constructor takes its content: empty, <c>in </c> or <c>ref readonly </c>.</param>
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
    bool Generic,
    int DerivableContentConstructors,
    bool AccessibleType,
    DeclaredAt At,
    EquatableArray<PropertyModel> Properties,
    EquatableArray<ObjectModel> Objects,
    string ContentModifier) : IClaimingClass
{
    /// <summary>Every member a document's top-level keys set, the engine's own Scene members included.</summary>
    internal IEnumerable<PropertyModel> Authored => Properties.Items.Where(static property => property.Authorable && property.Settable);

    /// <summary>The engine's plain Scene, whose applier serves every class that adds no authorable member to it.</summary>
    internal const string EngineScene = "global::Capsule.Scenes.Scene";

    /// <summary>
    /// Whether generated code emits and registers an applier for the class: code can name it, and it or a game base
    /// declares an authorable member.
    /// </summary>
    internal bool Applied => AccessibleType && !Generic && Authored.Any(static property => property.Declaring != EngineScene);

    /// <summary>The class whose applier composes this one: itself, or the engine's Scene when it adds no member.</summary>
    internal string Composing => Applied ? QualifiedName : EngineScene;

    /// <summary>
    /// Whether generated code constructs the class past C#'s required check: its required members are all
    /// entity references the document sets. A class with any other required member stays the compiler's error.
    /// </summary>
    internal bool Required =>
        Properties.Items.Any(static property => property.RequiredKeyword)
        && !Properties.Items.Any(static property => property.CodeOnly);

    /// <summary>Why a document's baseScene cannot name this class, or <see cref="SceneFault.None"/> when it can.</summary>
    internal SceneFault BaseFault =>
        !Abstract ? SceneFault.NotAbstract
        : !AccessibleType ? SceneFault.InaccessibleType
        : DerivableContentConstructors != 1 ? SceneFault.AmbiguousBaseConstructors
        : SceneFault.None;
}
