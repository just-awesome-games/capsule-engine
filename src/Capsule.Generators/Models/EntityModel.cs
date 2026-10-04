namespace Capsule.Generators;

internal enum EntityFault
{
    None,
    NotAConcreteEntity,
    MissingSpawnConstructor,
    BlankSpawnType,
    InaccessibleType,
    AmbiguousSpawnConstructors,
    SpawnNotPassedToBase,
}

/// <summary>One class the entity registry considered, with the key it claims or the fault it carries.</summary>
/// <param name="Declared">The key <c>[SpawnType]</c> names, or null when the type claims one by convention.</param>
/// <param name="At">Where a fault about this model is reported.</param>
/// <param name="Properties">What a document entry's keys may name, as <see cref="PropertySchema.Of"/> finds it.</param>
/// <param name="Objects">Every class a member's JSON object fills or constructs, as <see cref="PropertySchema.WithObjects"/> finds it.</param>
/// <param name="AssignableTo">What a reference member naming this entity may declare, as <see cref="PropertySchema.AssignableTo"/> finds it.</param>
/// <param name="SpawnModifier">How the spawn constructor takes its spawn: empty, <c>in </c> or <c>ref readonly </c>.</param>
internal readonly record struct EntityModel(
    string QualifiedName,
    string DisplayName,
    string ContainingNamespace,
    string TypeName,
    string? Declared,
    EntityFault Fault,
    DeclaredAt At,
    EquatableArray<PropertyModel> Properties,
    EquatableArray<ObjectModel> Objects,
    EquatableArray<string> AssignableTo,
    string SpawnModifier)
{
    // Only code satisfies C#'s required on a member no placement sets, so only code places such a class.
    internal bool CodeOnly => Properties.Items.Any(static property => property.CodeOnly);

    // A class whose placement sets C#'s required members is constructed past the compiler's check.
    internal bool Required => Properties.Items.Any(static property => property.RequiredKeyword);

    /// <summary>Every member a placement sets.</summary>
    internal IEnumerable<PropertyModel> Authored => Properties.Items.Where(static property => property.Authorable && property.Settable);
}
