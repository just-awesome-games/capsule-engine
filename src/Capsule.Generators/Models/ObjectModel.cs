namespace Capsule.Generators;

/// <summary>How generated code constructs an object class.</summary>
internal enum ObjectConstruction
{
    // Abstract, with no parameterless constructor, or with C#'s required members. A document fills an instance a
    // member holds, or names a subclass.
    None,

    // A parameterless constructor generated code can call.
    New,

    // A non-public parameterless constructor, which generated code reaches through an accessor.
    Accessor,
}

/// <summary>One class a member's JSON object fills or constructs, with what the object's keys may name.</summary>
/// <param name="QualifiedName">The class, fully qualified.</param>
/// <param name="Properties">What the object's keys may name, as <see cref="PropertySchema.Of"/> finds it.</param>
/// <param name="Subclasses">
/// Every class deriving from it that generated code can name, fully qualified, deepest first. Each has an
/// <see cref="ObjectModel"/> of its own.
/// </param>
internal readonly record struct ObjectModel(
    string QualifiedName,
    string DisplayName,
    string ContainingNamespace,
    string TypeName,
    DeclaredAt At,
    ObjectConstruction Construction,
    EquatableArray<PropertyModel> Properties,
    EquatableArray<string> Subclasses) : IClaimingClass
{
    /// <summary>Every member the object's keys set.</summary>
    internal IEnumerable<PropertyModel> Authored => Properties.Items.Where(static property => property.Authorable && property.Settable);
}
