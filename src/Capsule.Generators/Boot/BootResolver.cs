using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Checks the logic assemblies a shell references against each other: each registry is readable, there is
// at least one, and no two claim one key.
internal static class BootResolver
{
    // The drivers are the ones the shell declares itself.
    internal static BootPlan Resolve(BootModel model, InputDriverPlan drivers)
    {
        if (!model.RuntimePresent)
        {
            return default;
        }

        List<Diagnostic> diagnostics = [];
        foreach (string assemblyName in model.InvalidAssemblies.Items)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidRegistryProvider, Location.None, assemblyName));
        }

        // A shell with no logic assembly to take scenes from is a wiring mistake. Catch it at build
        // time instead of at the first RunScene.
        if (model.Providers.Items.IsEmpty && model.InvalidAssemblies.Items.IsEmpty)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ShellRoleMissingLogic, Location.None));
        }

        RejectDuplicateClaims(diagnostics, model.Providers);
        diagnostics.AddRange(drivers.Diagnostics.Items);

        return new BootPlan(Generates: true, model.Providers, drivers.Registered, new([.. diagnostics]));
    }

    // Each logic assembly refused its own duplicates. Two assemblies claiming one key are caught only here.
    private static void RejectDuplicateClaims(List<Diagnostic> diagnostics, EquatableArray<RegistryProviderModel> providers)
    {
        Dictionary<(RegistryClaimKind Kind, string Key), RegistryClaimModel> claimed = [];
        foreach (RegistryProviderModel provider in providers.Items)
        {
            foreach (RegistryClaimModel claim in provider.Claims.Items)
            {
                if (!claimed.TryGetValue((claim.Kind, claim.Key), out RegistryClaimModel previous))
                {
                    claimed.Add((claim.Kind, claim.Key), claim);
                    continue;
                }

                // Every logic assembly claims the engine's tile map for the same class, which is no conflict.
                if (previous.DeclaringType == claim.DeclaringType)
                {
                    continue;
                }

                diagnostics.Add(Diagnostic.Create(
                    claim.Kind == RegistryClaimKind.Entity ? Diagnostics.DuplicateTypeKey : Diagnostics.DuplicateSceneDocumentName,
                    Location.None,
                    previous.DeclaringType,
                    claim.DeclaringType,
                    claim.Key));
            }
        }
    }
}
