using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

// Every model the boot pipeline carries is compared by value: without that the pipeline sees a new
// model on every pass and re-emits the entry point whatever the edit was.
internal static class Models
{
    internal static bool SequenceEqual<T>(ImmutableArray<T> left, ImmutableArray<T> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }
}

internal readonly struct RegistryClaimModel(int kind, string key, string declaringType) : IEquatable<RegistryClaimModel>
{
    internal int Kind { get; } = kind;

    internal string Key { get; } = key;

    internal string DeclaringType { get; } = declaringType;

    public bool Equals(RegistryClaimModel other) =>
        Kind == other.Kind
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && string.Equals(DeclaringType, other.DeclaringType, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is RegistryClaimModel other && Equals(other);

    public override int GetHashCode() => (((17 * 31) + Kind) * 31) + Key.GetHashCode();
}

internal readonly struct RegistryProviderModel(
    string assemblyName,
    string qualifiedName,
    ImmutableArray<RegistryClaimModel> claims)
    : IEquatable<RegistryProviderModel>
{
    internal string AssemblyName { get; } = assemblyName;

    internal string QualifiedName { get; } = qualifiedName;

    internal ImmutableArray<RegistryClaimModel> Claims { get; } = claims;

    public bool Equals(RegistryProviderModel other) =>
        string.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal)
        && string.Equals(QualifiedName, other.QualifiedName, StringComparison.Ordinal)
        && Models.SequenceEqual(Claims, other.Claims);

    public override bool Equals(object? obj) => obj is RegistryProviderModel other && Equals(other);

    public override int GetHashCode() => (((17 * 31) + AssemblyName.GetHashCode()) * 31) + Claims.Length;
}

internal sealed class BootModel(
    bool runtimePresent,
    ImmutableArray<RegistryProviderModel> providers,
    ImmutableArray<string> invalidAssemblies)
    : IEquatable<BootModel>
{
    internal static readonly BootModel None = new(
        false,
        ImmutableArray<RegistryProviderModel>.Empty,
        ImmutableArray<string>.Empty);

    internal bool RuntimePresent { get; } = runtimePresent;

    internal ImmutableArray<RegistryProviderModel> Providers { get; } = providers;

    internal ImmutableArray<string> InvalidAssemblies { get; } = invalidAssemblies;

    public bool Equals(BootModel? other) =>
        other is not null
        && RuntimePresent == other.RuntimePresent
        && Models.SequenceEqual(Providers, other.Providers)
        && Models.SequenceEqual(InvalidAssemblies, other.InvalidAssemblies);

    public override bool Equals(object? obj) => Equals(obj as BootModel);

    public override int GetHashCode() =>
        (((17 * 31) + (RuntimePresent ? 1 : 0)) * 31) + Providers.Length;
}

internal static class CapsuleBootSource
{
    private const string FileName = "CapsuleBoot.g.cs";

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

    internal static void Emit(SourceProductionContext context, BootModel model, ImmutableArray<InputDriverModel> drivers)
    {
        if (!model.RuntimePresent)
        {
            return;
        }

        foreach (string assemblyName in model.InvalidAssemblies)
        {
            context.ReportDiagnostic(Diagnostic.Create(RegistryDiagnostics.InvalidRegistryProvider, Location.None, assemblyName));
        }

        // A shell with no logic assembly to take scenes from is a wiring mistake, caught here
        // rather than at the first RunScene.
        if (model.Providers.IsEmpty && model.InvalidAssemblies.IsEmpty)
        {
            context.ReportDiagnostic(Diagnostic.Create(RegistryDiagnostics.ShellRoleMissingLogic, Location.None));
        }

        ReportDuplicateClaims(context, model.Providers);
        context.AddSource(
            FileName,
            SourceText.From(Render(model.Providers, InputDriverRegistrySource.Sound(context, drivers)), Encoding.UTF8));
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

    private static string Render(ImmutableArray<RegistryProviderModel> providers, List<InputDriverModel> drivers)
    {
        StringBuilder source = new();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Capsule.Runtime.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>This game's entry point. Generated; do not edit.</summary>");
        source.AppendLine("    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        source.AppendLine("    public static class CapsuleBoot");
        source.AppendLine("    {");
        source.AppendLine("        private static global::Capsule.Scenes.SceneRegistry Scenes { get; } = CreateScenes();");
        source.AppendLine();
        source.AppendLine("        private static global::Capsule.Input.InputDriverRegistry Drivers { get; } = CreateDrivers();");
        source.AppendLine();
        source.AppendLine("        /// <summary>The engine, configured with every registry this game generates.</summary>");
        source.AppendLine("        /// <param name=\"gameName\">The game's display name: its window title, and its local folder for the crash log and the saves as a slug.</param>");
        source.AppendLine("        /// <param name=\"platform\">The platform module for the host family this shell is.</param>");
        source.AppendLine("        public static global::Capsule.Runtime.EngineBuilder Configure(string gameName, global::Capsule.Runtime.HostPlatform platform) =>");
        source.AppendLine("            global::Capsule.Runtime.CapsuleEngine.Configure(gameName, platform, Scenes, Drivers);");
        source.AppendLine();
        source.AppendLine("        private static global::Capsule.Scenes.SceneRegistry CreateScenes()");
        source.AppendLine("        {");
        source.AppendLine("            var entities = new global::System.Collections.Generic.List<global::Capsule.Scenes.Spawning.EntityRegistration>();");
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
        source.AppendLine();
        source.AppendLine("        private static global::Capsule.Input.InputDriverRegistry CreateDrivers()");
        source.AppendLine("        {");
        source.AppendLine("            var drivers = new global::System.Collections.Generic.List<global::Capsule.Input.InputDriverRegistration>();");
        foreach (RegistryProviderModel provider in providers)
        {
            source.Append("            ");
            source.Append(provider.QualifiedName);
            source.AppendLine(".AddDrivers(drivers);");
        }

        // A driver declared in the shell itself, which holds no registry provider of its own.
        foreach (InputDriverModel driver in drivers)
        {
            source.Append("            drivers.Add(");
            InputDriverRegistrySource.AppendRegistration(source, driver);
            source.AppendLine(");");
        }

        source.AppendLine("            return new global::Capsule.Input.InputDriverRegistry(drivers);");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }
}
