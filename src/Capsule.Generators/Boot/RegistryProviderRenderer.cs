using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Writes CapsuleRegistryProvider.g.cs, the class a logic assembly's registries are reached through from the
// shell's CapsuleBoot. Boot/CapsuleRegistryProvider.sample.g.cs shows the file.
internal static class RegistryProviderRenderer
{
    private const string FileName = "CapsuleRegistryProvider.g.cs";

    /// <param name="providerName">The provider class's name, or null in any assembly but a logic assembly.</param>
    internal static void Emit(SourceProductionContext context, string? providerName)
    {
        if (providerName is not null)
        {
            GeneratedFile.Add(context, FileName, Render(providerName));
        }
    }

    // The body of every method building a scene registry, from each provider's AddScenes. CapsuleBoot passes every
    // referenced logic assembly's provider. CapsuleScenes passes its own and builds the same registry.
    internal static string SceneRegistryBody(IEnumerable<string> providers, string indent) =>
        $"{indent}var scenes = new global::Capsule.Scenes.SceneRegistryBuilder();\n"
        + string.Concat(providers.Select(provider => $"{indent}{provider}.AddScenes(scenes);\n"))
        + $"{indent}return scenes.Build();\n";

    // The provider an assembly is reached through, plus the two attributes a shell reads its
    // registry metadata from. Only generated code calls these members.
    internal static string Render(string providerName) => GeneratedFile.Write(
        [$"[assembly: global::Capsule.Generated.CapsuleGeneratedRegistryProviderAttribute(typeof(global::Capsule.Generated.{providerName}))]"],
        $$"""
            [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = false)]
            {{GeneratedFile.ExcludeFromCodeCoverage}}
            internal sealed class CapsuleGeneratedRegistryProviderAttribute : global::System.Attribute
            {
                public CapsuleGeneratedRegistryProviderAttribute(global::System.Type providerType) => ProviderType = providerType;
                public global::System.Type ProviderType { get; }
            }

            [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
            {{GeneratedFile.ExcludeFromCodeCoverage}}
            internal sealed class CapsuleGeneratedRegistryClaimAttribute : global::System.Attribute
            {
                public CapsuleGeneratedRegistryClaimAttribute(int kind, string key, global::System.Type declaringType)
                {
                    Kind = kind;
                    Key = key;
                    DeclaringType = declaringType;
                }
                public int Kind { get; }
                public string Key { get; }
                public global::System.Type DeclaringType { get; }
            }

            /// <summary>This assembly's registries, read by the shell's generated <c>CapsuleBoot</c> and by <c>CapsuleScenes.Registry</c>. Generated code. Do not edit.</summary>
            [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
            {{GeneratedFile.ExcludeFromCodeCoverage}}
            public static class {{providerName}}
            {
                [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
                public static void AddScenes(global::Capsule.Scenes.SceneRegistryBuilder scenes)
                {
                    global::System.ArgumentNullException.ThrowIfNull(scenes);
                    scenes.AddEntities(typeof(global::Capsule.Generated.CapsuleEntities).Assembly, global::Capsule.Generated.CapsuleEntities.Registrations);
                    scenes.AddScenes(global::Capsule.Generated.CapsuleScenes.Registrations);
                    scenes.AddAppliers(global::Capsule.Generated.CapsuleScenes.Appliers);
                }

                [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
                public static void AddDrivers(global::System.Collections.Generic.List<global::Capsule.Input.InputDriverRegistration> registrations)
                {
                    global::System.ArgumentNullException.ThrowIfNull(registrations);
                    registrations.AddRange(global::Capsule.Generated.CapsuleInputDrivers.Registrations);
                }
            }
        """);
}
