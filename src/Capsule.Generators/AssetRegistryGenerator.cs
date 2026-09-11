using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

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

        IncrementalValuesProvider<(AdditionalText Text, AnalyzerConfigOptionsProvider Options)> files =
            context.AdditionalTextsProvider
                .Combine(context.AnalyzerConfigOptionsProvider)
                .Select(static (input, _) => (input.Left, input.Right));

        IncrementalValuesProvider<AssetModel> assets = files
            .Select(static (input, _) => AssetRegistrySource.Describe(input.Text, input.Options))
            .Where(static model => model.HasValue)
            .Select(static (model, _) => model!.Value);

        // A font's pages ship like any other texture; the '.fnt' beside them is compiled into the
        // game, so both halves of the fonts domain are read here and neither is a texture handle.
        IncrementalValuesProvider<(AdditionalText Text, AnalyzerConfigOptionsProvider Options)> fontFiles = files
            .Where(static input => AssetRegistrySource.InDomain(input.Text, input.Options, FontRegistrySource.Domain));

        IncrementalValuesProvider<KeyValuePair<string, string>> pages = fontFiles
            .Select(static (input, _) => AssetRegistrySource.DescribePage(input.Text, input.Options))
            .Where(static page => page.HasValue)
            .Select(static (page, _) => page!.Value);

        IncrementalValuesProvider<FontModel> fonts = fontFiles
            .Where(static input => string.Equals(
                Path.GetExtension(input.Text.Path),
                BmFontParser.BmFontExtension,
                StringComparison.OrdinalIgnoreCase))
            .Select(static (input, cancellation) => FontRegistrySource.Describe(
                input.Text,
                AssetRegistrySource.Authored(input.Text, input.Options),
                cancellation));

        // A sheet is text in and C# out: nothing ships for it, so it reaches the generator as an
        // additional file of its own domain and never as an asset.
        IncrementalValuesProvider<SheetModel> sheets = files
            .Where(static input => AssetRegistrySource.InDomain(input.Text, input.Options, SpriteRegistrySource.Domain))
            .Select(static (input, cancellation) => SpriteRegistrySource.Describe(
                input.Text,
                AssetRegistrySource.Authored(input.Text, input.Options),
                cancellation));

        context.RegisterSourceOutput(
            assets.Collect().Combine(pages.Collect()).Combine(fonts.Collect()).Combine(sheets.Collect()).Combine(emitting),
            static (production, input) => AssetRegistrySource.Emit(
                production,
                input.Left.Left.Left.Left,
                input.Left.Left.Left.Right,
                input.Left.Left.Right,
                input.Left.Right,
                input.Right));
    }
}
