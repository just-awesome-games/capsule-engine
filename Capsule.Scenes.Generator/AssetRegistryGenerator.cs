using Microsoft.CodeAnalysis;

namespace Capsule.Scenes.Generator;

/// <summary>
/// Emits <c>Capsule.Assets.Generated.GameAssets</c> into the game's logic assembly
/// (<c>CapsuleGameLogic</c>): one typed handle per asset the build ships, nested under its domain.
/// The asset list arrives as additional files from <c>build/Capsule.Assets.targets</c>, so naming
/// an asset is a compile-time act — a misspelling is a build error rather than a file that turns
/// out to be missing on somebody else's machine.
/// <para>
/// The shell gets nothing: it ships the bytes, and the code that names them lives in the logic
/// assemblies it references.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class AssetRegistryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // An assembly that does not reference the handle types has no registry to hold, so it
        // gets nothing rather than code it could not compile.
        IncrementalValueProvider<bool> emitting = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                Symbols.Declares(options.GlobalOptions, Symbols.LogicRole)
                && !Symbols.Declares(options.GlobalOptions, Symbols.ShellRole))
            .Combine(context.CompilationProvider
                .Select(static (compilation, _) => compilation.GetTypeByMetadataName(Symbols.TextureHandle) is not null))
            .Select(static (input, _) => input.Left && input.Right);

        IncrementalValuesProvider<AssetModel> assets = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (input, _) => AssetRegistrySource.Describe(input.Left, input.Right))
            .Where(static model => model.HasValue)
            .Select(static (model, _) => model!.Value);

        context.RegisterSourceOutput(
            assets.Collect().Combine(emitting),
            static (production, input) => AssetRegistrySource.Emit(production, input.Left, input.Right));
    }
}
