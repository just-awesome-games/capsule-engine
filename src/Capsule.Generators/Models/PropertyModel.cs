namespace Capsule.Generators;

internal enum PropertyKind
{
    // One of PropertyForms.BuiltIns, read by the method its entry names.
    BuiltIn,

    // An enum member or a definition, read as a name the generated switch matches.
    Named,

    // A type declaring [JsonConverter], read through that converter at load.
    Converted,

    // An entity class or an interface, written as the id of another entry and set once every entry is constructed.
    Reference,

    // A [Flags] enum, read as member names joined by commas, each matched by the generated switch.
    Flags,

    // One of PropertyForms.Assets, read as a key its generated lookup resolves to a CapsuleAssets member.
    Asset,

    // A class declaring authorable members, read as a JSON object of them into the instance the member holds,
    // or into a new instance of the class or of the subclass its type key names.
    Object,
}

/// <summary>
/// One field or property of a class a document object's key may name: an authorable member it sets, or one with the
/// reason it cannot, or a member only code can set because it carries C#'s <c>required</c>.
/// </summary>
/// <param name="Key">The member's name camel-cased, a field's leading underscore dropped, which is its JSON key.</param>
/// <param name="Required">
/// Whether every placement must set it: <c>[Authorable(Required = true)]</c>, or C#'s <c>required</c> on an entity reference.
/// </param>
/// <param name="RequiredKeyword">Whether the member carries C#'s <c>required</c>.</param>
/// <param name="Direct">Whether generated code assigns it in plain C#, where otherwise an accessor sets it.</param>
/// <param name="Readable">Whether generated code reads it in plain C#, where otherwise an accessor reads it.</param>
/// <param name="Held">Whether it has no setter, so a document fills the object it holds in place.</param>
/// <param name="Declaring">
/// The fully qualified type declaring the member, which its accessor takes. A generic type is written with
/// its own type parameters.
/// </param>
/// <param name="TypeParameters">The declaring type's type parameters, outer types' first, or empty.</param>
/// <param name="TypeArguments">What the entity's base closes those parameters with, fully qualified.</param>
/// <param name="Constraints">The <c>where</c> clauses on those parameters, fully qualified, or empty.</param>
/// <param name="Type">The fully qualified type, or an array's element type, without the <c>?</c> of a nullable one.</param>
/// <param name="Nullable">Whether a JSON null is accepted.</param>
/// <param name="Array">Whether the member is an array or a memory of <paramref name="Type"/>, written as a JSON array.</param>
/// <param name="Shared">Whether the member is a <c>ReadOnlyMemory</c>, whose elements every composition of one document shares.</param>
/// <param name="Names">An enum's members or a type's definitions, each with its JSON name.</param>
/// <param name="Converter">The fully qualified converter a converted type declares.</param>
/// <param name="Refusal">Why a placement cannot set the member and the fix, or null when it can.</param>
/// <param name="Clash">An earlier authorable member taking the same key, or null.</param>
internal readonly record struct PropertyModel(
    string Name,
    string Key,
    bool Authorable,
    bool Required,
    bool RequiredKeyword,
    bool Field,
    bool Direct,
    bool Readable,
    bool Held,
    PropertyKind Kind,
    string Declaring,
    string TypeParameters,
    string TypeArguments,
    string Constraints,
    string Type,
    bool Nullable,
    bool Array,
    bool Shared,
    EquatableArray<(string Json, string Member)> Names,
    string? Converter,
    string? Refusal,
    string? Clash,
    DeclaredAt At)
{
    internal bool Settable => Refusal is null && Clash is null;

    /// <summary>The member's type as C# declares it, fully qualified.</summary>
    internal string Declared => (Shared ? $"global::System.ReadOnlyMemory<{Type}>" : Type + (Array ? "[]" : string.Empty)) + (Nullable ? "?" : string.Empty);

    // C#'s required on anything but an [Authorable] entity reference or array of them, which only code can satisfy.
    internal bool CodeOnly => RequiredKeyword && !(Authorable && Kind == PropertyKind.Reference);
}
