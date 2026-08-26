using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Capsule.Scenes.Generator;

/// <summary>
/// Emits what the project's declared role calls for. In the game's logic assembly
/// (<c>CapsuleGameLogic</c>): its spawnable entities and scenes as the registries the engine boots
/// through, <c>Capsule.Scenes.Generated.GameEntities.Registry</c> and
/// <c>Capsule.Scenes.Generated.GameScenes.Registry</c>. A class joins either by its constructor
/// shape alone; anything else is an ordinary class and is passed over in silence. Both registries
/// are emitted whatever the assembly declares, so a call site referring to one always compiles. In
/// the shell (<c>CapsuleGameShell</c>): <c>Capsule.Runtime.Generated.GameBoot</c>, the engine entry
/// point already carrying the registry it finds in the logic assembly it references.
/// <para>
/// Which half is emitted follows the declared role and never what a compilation happens to see: a
/// shell reaches Capsule.Scenes through the logic assembly, and registries emitted there would
/// shadow the game's own, empty.
/// </para>
/// Generating this rather than reflecting for it at boot is what keeps a Capsule game NativeAOT-able
/// and its boot table compile-checked.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RegistryGenerator : IIncrementalGenerator
{
    private const string LogicRole = "build_property.CapsuleGameLogic";
    private const string ShellRole = "build_property.CapsuleGameShell";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<(bool Logic, bool Shell)> roles = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => (Declares(options.GlobalOptions, LogicRole), Declares(options.GlobalOptions, ShellRole)));

        // An assembly that does not reference Capsule.Scenes has no registry to hold and no call
        // site to satisfy, so it gets nothing rather than code it could not compile.
        IncrementalValueProvider<bool> registries = context.CompilationProvider
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName(Symbols.Scene) is not null)
            .Combine(roles)
            .Select(static (input, _) => input.Left && input.Right.Logic);

        IncrementalValueProvider<BootWiring> boot = context.CompilationProvider
            .Select(static (compilation, _) => GameBootSource.Describe(compilation))
            .Combine(roles)
            .Select(static (input, _) => input.Right.Shell ? input.Left : BootWiring.None);

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

        context.RegisterSourceOutput(boot, static (production, wiring) => GameBootSource.Emit(production, wiring));
    }

    // MSBuild passes a boolean property through verbatim and compares it case-insensitively itself.
    private static bool Declares(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out string? value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
