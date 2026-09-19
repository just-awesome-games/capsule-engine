using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class AssetRegistryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // An assembly that does not reference the handle types gets nothing to compile.
        IncrementalValueProvider<bool> emitting = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                Symbols.Declares(options.GlobalOptions, Symbols.LogicRole)
                && !Symbols.Declares(options.GlobalOptions, Symbols.ShellRole))
            .Combine(context.CompilationProvider
                .Select(static (compilation, _) => compilation.GetTypeByMetadataName(Symbols.TextureHandle) is not null))
            .Select(static (input, _) => input.Left && input.Right);

        IncrementalValuesProvider<AssetFile> files = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (input, _) => AssetFile.From(input.Left, input.Right));

        IncrementalValuesProvider<AssetModel> assets = files
            .Select(static (file, _) => AssetRegistrySource.Describe(file))
            .Where(static model => model.HasValue)
            .Select(static (model, _) => model!.Value);

        // A font's pages ship like any other texture and the '.fnt' beside them is compiled into
        // the game. Both halves of the fonts domain are read here.
        IncrementalValuesProvider<AssetFile> fontFiles = files
            .Where(static file => file.InDomain(FontRegistrySource.Domain));

        IncrementalValuesProvider<KeyValuePair<string, string>> pages = fontFiles
            .Select(static (file, _) => AssetRegistrySource.DescribePage(file))
            .Where(static page => page.HasValue)
            .Select(static (page, _) => page!.Value);

        // Parsed behind the role gate. A project that emits no registry reads no font.
        IncrementalValuesProvider<ParsedAsset<BmFontDescription>> fonts = fontFiles
            .Where(static file => string.Equals(
                Path.GetExtension(file.Text.Path),
                BmFontParser.BmFontExtension,
                StringComparison.OrdinalIgnoreCase))
            .Combine(emitting)
            .Where(static input => input.Right)
            .Select(static (input, cancellation) =>
                FontRegistrySource.Describe(input.Left.Text, input.Left.Authored, cancellation))
            .WithTrackingName("FontParse");

        context.RegisterSourceOutput(
            assets.Collect().Combine(pages.Collect()).Combine(fonts.Collect()).Combine(emitting),
            static (production, input) => AssetRegistrySource.Emit(
                production,
                input.Left.Left.Left,
                input.Left.Left.Right,
                input.Left.Right,
                input.Right));
    }
}
