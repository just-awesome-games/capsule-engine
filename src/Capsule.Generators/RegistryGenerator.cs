using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Generators;

// What one type declaration was read as, bound once. A declaration may be several of these at
// once — a scene that is also an input driver — so all three are described off the one symbol
// rather than binding it again per registry.
internal readonly struct RegistryCandidate(EntityModel? entity, SceneModel? scene, InputDriverModel? driver)
    : IEquatable<RegistryCandidate>
{
    internal EntityModel? Entity { get; } = entity;

    internal SceneModel? Scene { get; } = scene;

    internal InputDriverModel? Driver { get; } = driver;

    internal bool IsEmpty => Entity is null && Scene is null && Driver is null;

    public bool Equals(RegistryCandidate other) =>
        Nullable.Equals(Entity, other.Entity)
        && Nullable.Equals(Scene, other.Scene)
        && Nullable.Equals(Driver, other.Driver);

    public override bool Equals(object? obj) => obj is RegistryCandidate other && Equals(other);

    public override int GetHashCode() =>
        (Entity?.GetHashCode() ?? 0) ^ (Scene?.GetHashCode() ?? 0) ^ (Driver?.GetHashCode() ?? 0);
}

[Generator(LanguageNames.CSharp)]
public sealed class RegistryGenerator : IIncrementalGenerator
{
    /// <summary>The pipeline step that walks the referenced assemblies, named so a spec can hold it to caching.</summary>
    internal const string BootStep = "BootModel";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<(bool Logic, bool Shell)> roles = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => (
                Symbols.Declares(options.GlobalOptions, Symbols.LogicRole),
                Symbols.Declares(options.GlobalOptions, Symbols.ShellRole)));

        IncrementalValueProvider<(bool EnginePresent, bool RuntimePresent, bool Logic, bool Shell, string AssemblyName)> configuration =
            context.CompilationProvider
                .Select(static (compilation, _) => (
                    EnginePresent: compilation.GetTypeByMetadataName(Symbols.Scene) is not null,
                    RuntimePresent: compilation.GetTypeByMetadataName(Symbols.CapsuleEngine) is not null,
                    AssemblyName: compilation.AssemblyName ?? "Game"))
                .Combine(roles)
                .Select(static (input, _) => (
                    input.Left.EnginePresent,
                    input.Left.RuntimePresent,
                    input.Right.Logic,
                    input.Right.Shell,
                    input.Left.AssemblyName));

        // An assembly that does not reference Capsule.Scenes has no registry to hold and no call
        // site to satisfy, so it gets nothing rather than code it could not compile.
        IncrementalValueProvider<bool> registries = configuration
            .Select(static (configured, _) => configured.EnginePresent && configured.Logic && !configured.Shell);

        IncrementalValueProvider<string?> provider = configuration
            .Select(static (configured, _) => configured.EnginePresent && configured.Logic && !configured.Shell
                ? TypeNaming.RegistryProviderName(configured.AssemblyName)
                : null);

        // The role filter comes first: describing the boot model walks every referenced assembly's
        // attributes, which no project but the shell has any use for.
        IncrementalValueProvider<BootModel> boot = roles
            .Combine(context.CompilationProvider)
            .Select(static (input, _) => input.Left.Shell && !input.Left.Logic
                ? CapsuleBootSource.Describe(input.Right)
                : BootModel.None)
            .WithTrackingName(BootStep);

        IncrementalValuesProvider<RegistryCandidate> candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => Symbols.MayBeRegistered(node),
                static (syntax, cancellation) => Describe(syntax, cancellation))
            .Where(static candidate => !candidate.IsEmpty);

        IncrementalValuesProvider<EntityModel> entities = candidates
            .Where(static candidate => candidate.Entity is not null)
            .Select(static (candidate, _) => candidate.Entity!.Value);

        IncrementalValuesProvider<SceneModel> scenes = candidates
            .Where(static candidate => candidate.Scene is not null)
            .Select(static (candidate, _) => candidate.Scene!.Value);

        IncrementalValuesProvider<InputDriverModel> drivers = candidates
            .Where(static candidate => candidate.Driver is not null)
            .Select(static (candidate, _) => candidate.Driver!.Value);

        // What a key is measured against: the declared root namespace, or the assembly's name when
        // a project leaves it to MSBuild's own default.
        IncrementalValueProvider<string> rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue(Symbols.RootNamespace, out string? declared) && declared.Length > 0
                    ? declared
                    : null)
            .Combine(context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? string.Empty))
            .Select(static (input, _) => input.Left ?? input.Right);

        context.RegisterSourceOutput(
            entities.Collect().Combine(registries).Combine(rootNamespace),
            static (production, input) =>
                EntityRegistrySource.Emit(production, input.Left.Left, input.Left.Right, input.Right));

        context.RegisterSourceOutput(
            scenes.Collect().Combine(registries).Combine(rootNamespace),
            static (production, input) =>
                SceneRegistrySource.Emit(production, input.Left.Left, input.Left.Right, input.Right));

        // A logic assembly hands its drivers to the shell through its registry provider; a driver
        // the shell itself declares is emitted straight into the entry point instead.
        context.RegisterSourceOutput(
            drivers.Collect().Combine(registries),
            static (production, input) => InputDriverRegistrySource.Emit(production, input.Left, input.Right));

        context.RegisterSourceOutput(provider, static (production, providerName) => RegistryProviderSource.Emit(production, providerName));
        context.RegisterSourceOutput(
            boot.Combine(drivers.Collect()),
            static (production, input) => CapsuleBootSource.Emit(production, input.Left, input.Right));
        context.RegisterSourceOutput(configuration, static (production, configured) =>
        {
            if (configured.Logic && configured.Shell)
            {
                production.ReportDiagnostic(Diagnostic.Create(RegistryDiagnostics.ConflictingProjectRoles, Location.None));
            }
            else if (configured.Logic && !configured.EnginePresent)
            {
                production.ReportDiagnostic(Diagnostic.Create(RegistryDiagnostics.LogicRoleMissingScenes, Location.None));
            }
            else if (configured.Shell && !configured.RuntimePresent)
            {
                production.ReportDiagnostic(Diagnostic.Create(RegistryDiagnostics.ShellRoleMissingRuntime, Location.None));
            }
        });
    }

    private static RegistryCandidate Describe(GeneratorSyntaxContext context, CancellationToken cancellation)
    {
        TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellation) is not INamedTypeSymbol type)
        {
            return default;
        }

        Compilation compilation = context.SemanticModel.Compilation;

        return new RegistryCandidate(
            EntityRegistrySource.Describe(type, declaration, compilation),
            SceneRegistrySource.Describe(type, declaration, compilation),
            InputDriverRegistrySource.Describe(type, declaration, compilation));
    }
}
