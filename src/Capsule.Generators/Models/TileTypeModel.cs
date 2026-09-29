namespace Capsule.Generators;

/// <summary>One class a palette entry's <c>type</c> can name, and what keeps a palette entry from composing it.</summary>
/// <param name="Fault">Why a palette entry cannot compose the class, with the fix, or null when nothing but its members does.</param>
/// <param name="Properties">What a palette entry's <c>properties</c> keys may name, as <see cref="PropertySchema.Of"/> finds it.</param>
internal readonly record struct TileTypeModel(
    string QualifiedName,
    string DisplayName,
    string ContainingNamespace,
    string TypeName,
    string? Fault,
    DeclaredAt At,
    EquatableArray<PropertyModel> Properties) : IClaimingClass
{
    /// <summary>
    /// Whether generated code composes the class. A C# required member the game marks [Authorable] is already
    /// its own error where it is declared, and keeps the class out too.
    /// </summary>
    internal bool Valid => Fault is null && !Properties.Items.Any(static property => property.RequiredKeyword);

    /// <summary>Every member a palette entry's properties set.</summary>
    internal IEnumerable<PropertyModel> Authored => Properties.Items.Where(static property => property.Authorable && property.Settable);
}
