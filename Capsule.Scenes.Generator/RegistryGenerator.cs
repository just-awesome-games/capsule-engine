using Microsoft.CodeAnalysis;

namespace Capsule.Scenes.Generator;

/// <summary>
/// Emits what the project's declared role calls for. In the game's logic assembly
/// (<c>CapsuleGameLogic</c>): its spawnable entities and scenes as the registries the engine boots
/// through, <c>Capsule.Scenes.Generated.GameEntities.Registry</c> and
/// <c>Capsule.Scenes.Generated.GameScenes.Registry</c>. A class joins either by its constructor
/// shape alone; anything else is an ordinary class and is passed over in silence. Both registries
/// are emitted whatever the assembly declares, so a call site referring to one always compiles. A
/// uniquely named provider keeps each assembly's factories beside the types they construct. In the
/// shell (<c>CapsuleGameShell</c>): <c>Capsule.Runtime.Generated.GameBoot</c>, the engine entry point
/// already composing every provider in the logic assemblies it references.
/// <para>
/// Which half is emitted follows the declared role and never what a compilation happens to see: a
/// shell reaches Capsule.Scenes through the logic assemblies, and registries emitted there would
/// shadow their generated metadata.
/// </para>
/// Generating this rather than reflecting for it at boot is what keeps a Capsule game NativeAOT-able
/// and its boot table compile-checked.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RegistryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<(bool Logic, bool Shell)> roles = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => (
                Symbols.Declares(options.GlobalOptions, Symbols.LogicRole),
                Symbols.Declares(options.GlobalOptions, Symbols.ShellRole)));

        // An assembly that does not reference Capsule.Scenes has no registry to hold and no call
        // site to satisfy, so it gets nothing rather than code it could not compile.
        IncrementalValueProvider<bool> registries = context.CompilationProvider
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName(Symbols.Scene) is not null)
            .Combine(roles)
            .Select(static (input, _) => input.Left && input.Right.Logic && !input.Right.Shell);

        IncrementalValueProvider<string?> provider = context.CompilationProvider
            .Select(static (compilation, _) => (
                EnginePresent: compilation.GetTypeByMetadataName(Symbols.Scene) is not null,
                AssemblyName: compilation.AssemblyName ?? "Game"))
            .Combine(roles)
            .Select(static (input, _) => input.Left.EnginePresent && input.Right.Logic && !input.Right.Shell
                ? TypeNaming.RegistryProviderName(input.Left.AssemblyName)
                : null);

        IncrementalValueProvider<BootModel> boot = context.CompilationProvider
            .Select(static (compilation, _) => GameBootSource.Describe(compilation))
            .Combine(roles)
            .Select(static (input, _) => input.Right.Shell && !input.Right.Logic ? input.Left : BootModel.None);

        IncrementalValueProvider<(bool EnginePresent, bool RuntimePresent, bool Logic, bool Shell)> configuration =
            context.CompilationProvider
                .Select(static (compilation, _) => (
                    EnginePresent: compilation.GetTypeByMetadataName(Symbols.Scene) is not null,
                    RuntimePresent: compilation.GetTypeByMetadataName(Symbols.CapsuleEngine) is not null))
                .Combine(roles)
                .Select(static (input, _) => (
                    input.Left.EnginePresent,
                    input.Left.RuntimePresent,
                    input.Right.Logic,
                    input.Right.Shell));

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

        context.RegisterSourceOutput(
            entities.Collect().Combine(registries),
            static (production, input) => EntityRegistrySource.Emit(production, input.Left, input.Right));

        context.RegisterSourceOutput(
            scenes.Collect().Combine(registries),
            static (production, input) => SceneRegistrySource.Emit(production, input.Left, input.Right));

        context.RegisterSourceOutput(provider, static (production, providerName) => RegistryProviderSource.Emit(production, providerName));
        context.RegisterSourceOutput(boot, static (production, wiring) => GameBootSource.Emit(production, wiring));
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
