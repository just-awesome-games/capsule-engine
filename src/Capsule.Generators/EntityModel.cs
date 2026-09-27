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
internal readonly record struct EntityModel(
    string QualifiedName,
    string DisplayName,
    string ContainingNamespace,
    string TypeName,
    string? Declared,
    EntityFault Fault,
    DeclaredAt At,
    EquatableArray<PropertyModel> Properties)
{
    // The generated new T(spawn) cannot satisfy C#'s required, so only code places such a class.
    internal bool CodeOnly => Properties.Items.Any(static property => property.RequiredKeyword);
}
