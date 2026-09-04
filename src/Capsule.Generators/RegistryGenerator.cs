using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class RegistryGenerator : IIncrementalGenerator
{
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

        IncrementalValueProvider<BootModel> boot = context.CompilationProvider
            .Select(static (compilation, _) => CapsuleBootSource.Describe(compilation))
            .Combine(roles)
            .Select(static (input, _) => input.Right.Shell && !input.Right.Logic ? input.Left : BootModel.None);

        IncrementalValuesProvider<EntityModel> entities = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => Symbols.MayBeRegistered(node),
                static (syntax, cancellation) => EntityRegistrySource.Describe(syntax, cancellation))
            .Where(static model => model.HasValue)
            .Select(static (model, _) => model!.Value);

        IncrementalValuesProvider<SceneModel> scenes = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => Symbols.MayBeRegistered(node),
                static (syntax, cancellation) => SceneRegistrySource.Describe(syntax, cancellation))
            .Where(static model => model.HasValue)
            .Select(static (model, _) => model!.Value);

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

        context.RegisterSourceOutput(provider, static (production, providerName) => RegistryProviderSource.Emit(production, providerName));
        context.RegisterSourceOutput(boot, static (production, wiring) => CapsuleBootSource.Emit(production, wiring));
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
}
