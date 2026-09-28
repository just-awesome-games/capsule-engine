using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Checks the logic assemblies a shell references against each other: each registry is readable, there is
// at least one, and no two claim one key.
internal static class BootResolver
{
    internal static BootPlan Resolve(BootInputs inputs)
    {
        BootModel model = inputs.Boot;
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
        diagnostics.AddRange(inputs.Drivers.Diagnostics.Items);

        return new BootPlan(Generates: true, model.Providers, inputs.Drivers.Registered, new([.. diagnostics]));
    }

    // Each logic assembly refused its own duplicates. Two assemblies claiming one key are caught only here.
    private static void RejectDuplicateClaims(List<Diagnostic> diagnostics, EquatableArray<RegistryProviderModel> providers)
    {
        Dictionary<string, RegistryClaimModel> entities = new(StringComparer.Ordinal);
        Dictionary<string, RegistryClaimModel> documents = new(StringComparer.Ordinal);
        foreach (RegistryProviderModel provider in providers.Items)
        {
            foreach (RegistryClaimModel claim in provider.Claims.Items)
            {
                Dictionary<string, RegistryClaimModel> claimed = claim.Kind == RegistryClaimKind.Entity ? entities : documents;
                if (claimed.TryGetValue(claim.Key, out RegistryClaimModel previous))
                {
                    DiagnosticDescriptor descriptor = claim.Kind == RegistryClaimKind.Entity
                        ? Diagnostics.DuplicateSpawnType
                        : Diagnostics.DuplicateSceneDocumentName;
                    diagnostics.Add(Diagnostic.Create(descriptor, Location.None, previous.DeclaringType, claim.DeclaringType, claim.Key));
                }
                else
                {
                    claimed.Add(claim.Key, claim);
                }
            }
        }
    }
}
