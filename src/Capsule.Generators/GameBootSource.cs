using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal readonly struct RegistryClaimModel
{
    internal RegistryClaimModel(int kind, string key, string declaringType)
    {
        Kind = kind;
        Key = key;
        DeclaringType = declaringType;
    }

    internal int Kind { get; }

    internal string Key { get; }

    internal string DeclaringType { get; }
}

internal readonly struct RegistryProviderModel
{
    internal RegistryProviderModel(string assemblyName, string qualifiedName, ImmutableArray<RegistryClaimModel> claims)
    {
        AssemblyName = assemblyName;
        QualifiedName = qualifiedName;
        Claims = claims;
    }

    internal string AssemblyName { get; }

    internal string QualifiedName { get; }

    internal ImmutableArray<RegistryClaimModel> Claims { get; }
}

internal sealed class BootModel
{
    internal static readonly BootModel None = new(
        false,
        ImmutableArray<RegistryProviderModel>.Empty,
        ImmutableArray<string>.Empty);

    internal BootModel(bool runtimePresent, ImmutableArray<RegistryProviderModel> providers, ImmutableArray<string> invalidAssemblies)
    {
        RuntimePresent = runtimePresent;
        Providers = providers;
        InvalidAssemblies = invalidAssemblies;
    }

    internal bool RuntimePresent { get; }

    internal ImmutableArray<RegistryProviderModel> Providers { get; }

    internal ImmutableArray<string> InvalidAssemblies { get; }
}

internal static class GameBootSource
{
    private const string FileName = "CapsuleGameBoot.g.cs";

    internal static BootModel Describe(Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(Symbols.CapsuleEngine) is null)
        {
            return BootModel.None;
        }

        ImmutableArray<RegistryProviderModel>.Builder providers = ImmutableArray.CreateBuilder<RegistryProviderModel>();
        ImmutableArray<string>.Builder invalid = ImmutableArray.CreateBuilder<string>();
        foreach (IAssemblySymbol assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            AttributeData? providerAttribute = null;
            ImmutableArray<RegistryClaimModel>.Builder claims = ImmutableArray.CreateBuilder<RegistryClaimModel>();
            bool malformed = false;

            foreach (AttributeData attribute in assembly.GetAttributes())
            {
                string attributeName = attribute.AttributeClass?.ToDisplayString() ?? string.Empty;
                if (string.Equals(attributeName, Symbols.RegistryProviderAttribute, StringComparison.Ordinal))
                {
                    if (providerAttribute is not null)
                    {
                        malformed = true;
                        break;
                    }

                    providerAttribute = attribute;
                }
                else if (string.Equals(attributeName, Symbols.RegistryClaimAttribute, StringComparison.Ordinal))
                {
                    if (TryReadClaim(attribute, out RegistryClaimModel claim))
                    {
                        claims.Add(claim);
                    }
                    else
                    {
                        malformed = true;
                        break;
                    }
                }
            }

            if (malformed)
            {
                invalid.Add(assembly.Name);
                continue;
            }

            if (providerAttribute is null)
            {
                continue;
            }

            if (providerAttribute.ConstructorArguments.Length != 1
                || providerAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol providerType)
            {
                invalid.Add(assembly.Name);
                continue;
            }

            providers.Add(new RegistryProviderModel(
                assembly.Name,
                providerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                claims.ToImmutable()));
        }

        providers.Sort(static (left, right) => string.CompareOrdinal(left.AssemblyName, right.AssemblyName));
        return new BootModel(true, providers.ToImmutable(), invalid.ToImmutable());
    }

    internal static void Emit(SourceProductionContext context, BootModel model)
    {
        if (!model.RuntimePresent)
        {
            return;
        }

        foreach (string assemblyName in model.InvalidAssemblies)
        {
            context.ReportDiagnostic(Diagnostic.Create(RegistryDiagnostics.InvalidRegistryProvider, Location.None, assemblyName));
        }

        // The entry point exists to hand the game's scenes over, so a shell with no logic assembly
        // to take them from is a wiring mistake, caught here rather than at the first RunScene.
        if (model.Providers.IsEmpty && model.InvalidAssemblies.IsEmpty)
        {
            context.ReportDiagnostic(Diagnostic.Create(RegistryDiagnostics.ShellRoleMissingLogic, Location.None));
        }

        ReportDuplicateClaims(context, model.Providers);
        context.AddSource(FileName, SourceText.From(Render(model.Providers), Encoding.UTF8));
    }

    private static bool TryReadClaim(AttributeData attribute, out RegistryClaimModel claim)
    {
        if (attribute.ConstructorArguments.Length == 3
            && attribute.ConstructorArguments[0].Value is int kind
            && attribute.ConstructorArguments[1].Value is string key
            && attribute.ConstructorArguments[2].Value is INamedTypeSymbol declaringType
            && kind is 0 or 1)
        {
            claim = new RegistryClaimModel(kind, key, declaringType.ToDisplayString());
            return true;
        }

        claim = default;
        return false;
    }

    private static void ReportDuplicateClaims(SourceProductionContext context, ImmutableArray<RegistryProviderModel> providers)
    {
        Dictionary<string, RegistryClaimModel> entities = new(StringComparer.Ordinal);
        Dictionary<string, RegistryClaimModel> documents = new(StringComparer.Ordinal);
        foreach (RegistryProviderModel provider in providers)
        {
            foreach (RegistryClaimModel claim in provider.Claims)
            {
                Dictionary<string, RegistryClaimModel> claimed = claim.Kind == 0 ? entities : documents;
                if (claimed.TryGetValue(claim.Key, out RegistryClaimModel previous))
                {
                    DiagnosticDescriptor descriptor = claim.Kind == 0
                        ? RegistryDiagnostics.DuplicateSpawnType
                        : RegistryDiagnostics.DuplicateSceneDocumentName;
                    context.ReportDiagnostic(Diagnostic.Create(
                        descriptor,
                        Location.None,
                        previous.DeclaringType,
                        claim.DeclaringType,
                        claim.Key));
                }
                else
                {
                    claimed.Add(claim.Key, claim);
                }
            }
        }
    }

    private static string Render(ImmutableArray<RegistryProviderModel> providers)
    {
        StringBuilder source = new();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Capsule.Runtime.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>This game's entry point. Generated; do not edit.</summary>");
        source.AppendLine("    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        source.AppendLine("    public static class GameBoot");
        source.AppendLine("    {");
        source.AppendLine("        private static global::Capsule.Scenes.SceneRegistry Scenes { get; } = CreateScenes();");
        source.AppendLine();
        source.AppendLine("        /// <summary>The engine, configured with every registry this game generates.</summary>");
        source.AppendLine("        /// <param name=\"gameName\">The game's display name: its window title, and its crash-log folder as a slug.</param>");
        source.AppendLine("        public static global::Capsule.Runtime.SceneEngineBuilder Configure(string gameName) =>");
        source.AppendLine("            global::Capsule.Runtime.CapsuleEngine.Configure(gameName, Scenes);");
        source.AppendLine();
        source.AppendLine("        private static global::Capsule.Scenes.SceneRegistry CreateScenes()");
        source.AppendLine("        {");
        source.AppendLine("            var entities = new global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, global::Capsule.Scenes.Spawning.EntitySpawner>>();");
        foreach (RegistryProviderModel provider in providers)
        {
            source.Append("            ");
            source.Append(provider.QualifiedName);
            source.AppendLine(".AddEntities(entities);");
        }

        source.AppendLine("            var scenes = new global::System.Collections.Generic.List<global::Capsule.Scenes.SceneRegistration>();");
        foreach (RegistryProviderModel provider in providers)
        {
            source.Append("            ");
            source.Append(provider.QualifiedName);
            source.AppendLine(".AddScenes(scenes);");
        }

        source.AppendLine("            return new global::Capsule.Scenes.SceneRegistry(");
        source.AppendLine("                new global::Capsule.Scenes.Spawning.EntityRegistry(entities),");
        source.AppendLine("                scenes);");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }
}
