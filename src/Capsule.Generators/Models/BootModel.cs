namespace Capsule.Generators;

/// <summary>What a shell's entry point is built from: every referenced logic assembly's registry provider.</summary>
/// <param name="RuntimePresent">Whether the shell references the runtime, without which it gets no entry point.</param>
/// <param name="Providers">Every valid provider, sorted by assembly name.</param>
/// <param name="InvalidAssemblies">Every referenced assembly whose registry metadata is malformed.</param>
internal readonly record struct BootModel(
    bool RuntimePresent,
    EquatableArray<RegistryProviderModel> Providers,
    EquatableArray<string> InvalidAssemblies)
{
    /// <summary>The model of an assembly that is not a shell.</summary>
    internal static BootModel None => default;
}
