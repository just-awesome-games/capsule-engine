namespace Capsule.Generators;

/// <summary>One class the driver registry considered, and the name <c>--driver</c> reaches it by.</summary>
/// <param name="TypeName">The simple class name, which is the key <c>--driver</c> takes.</param>
/// <param name="At">Where a fault about this model is reported.</param>
internal readonly record struct InputDriverModel(
    string QualifiedName,
    string DisplayName,
    string TypeName,
    bool Accessible,
    DeclaredAt At);
