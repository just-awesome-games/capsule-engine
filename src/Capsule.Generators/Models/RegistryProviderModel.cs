namespace Capsule.Generators;

/// <summary>One referenced logic assembly's registry provider, and every key the assembly claims.</summary>
/// <param name="QualifiedName">The fully qualified provider class the shell's entry point calls.</param>
internal readonly record struct RegistryProviderModel(
    string AssemblyName,
    string QualifiedName,
    EquatableArray<RegistryClaimModel> Claims);
