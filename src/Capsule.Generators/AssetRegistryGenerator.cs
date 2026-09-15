using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class AssetRegistryGenerator : IIncrementalGenerator
{
    /// <summary>The pipeline step that reads a '.fnt', named so a spec can hold it to caching.</summary>
    internal const string FontParseStep = "FontParse";

    /// <summary>The pipeline step that reads a sheet document, named for the same reason.</summary>
    internal const string SheetParseStep = "SheetParse";

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

        // A font's pages ship like any other texture; the '.fnt' beside them is compiled into the
        // game, so both halves of the fonts domain are read here and neither is a texture handle.
        IncrementalValuesProvider<AssetFile> fontFiles = files
            .Where(static file => file.InDomain(FontRegistrySource.Domain));

        IncrementalValuesProvider<KeyValuePair<string, string>> pages = fontFiles
            .Select(static (file, _) => AssetRegistrySource.DescribePage(file))
            .Where(static page => page.HasValue)
            .Select(static (page, _) => page!.Value);

        // Parsed behind the role gate: a project that emits no registry reads no font and no sheet.
        IncrementalValuesProvider<ParsedAsset<BmFontDescription>> fonts = fontFiles
            .Where(static file => string.Equals(
                Path.GetExtension(file.Text.Path),
                BmFontParser.BmFontExtension,
                StringComparison.OrdinalIgnoreCase))
            .Combine(emitting)
            .Where(static input => input.Right)
            .Select(static (input, cancellation) =>
                FontRegistrySource.Describe(input.Left.Text, input.Left.Authored, cancellation))
            .WithTrackingName(FontParseStep);

        // A sheet is text in and C# out: nothing ships for it, so it reaches the generator as an
        // additional file of its own domain and never as an asset.
        IncrementalValuesProvider<ParsedAsset<SheetDocument>> sheets = files
            .Where(static file => file.InDomain(SpriteRegistrySource.Domain))
            .Combine(emitting)
            .Where(static input => input.Right)
            .Select(static (input, cancellation) =>
                SpriteRegistrySource.Describe(input.Left.Text, input.Left.Authored, cancellation))
            .WithTrackingName(SheetParseStep);

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
