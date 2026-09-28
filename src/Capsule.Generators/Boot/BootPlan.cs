using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>What <c>CapsuleBoot.g.cs</c> holds: every logic assembly's registry, and the drivers the shell declares itself.</summary>
/// <param name="Generates">Whether the assembly gets the file at all: only a shell referencing the runtime does.</param>
/// <param name="Providers">Every referenced logic assembly's registry provider, sorted by assembly name.</param>
/// <param name="Drivers">Every sound input driver the shell declares, in declaration order.</param>
/// <param name="Diagnostics">Every fault found resolving the plan.</param>
internal readonly record struct BootPlan(
    bool Generates,
    EquatableArray<RegistryProviderModel> Providers,
    EquatableArray<InputDriverModel> Drivers,
    EquatableArray<Diagnostic> Diagnostics);
