namespace Capsule.Generators;

/// <summary>One class a <see cref="NamedRegistry"/> considered, and the name its command-line option reaches it by.</summary>
/// <param name="TypeName">The simple class name, which is the key the option takes.</param>
/// <param name="At">Where a fault about this model is reported.</param>
internal readonly record struct NamedClassModel(
    string QualifiedName,
    string DisplayName,
    string TypeName,
    bool Accessible,
    DeclaredAt At);
