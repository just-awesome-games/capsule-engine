using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal static class AssetRegistrySource
{
    private const string FileName = "CapsuleAssets.g.cs";

    private const string TextureDomain = "textures";

    private const string HandleType = "global::Capsule.Assets.TextureHandle";

    internal static AssetModel? Describe(AssetFile file)
    {
        // Every additional file a project carries arrives here, most of them assets of no domain
        // this generator declares a class for. Audio is one: the build tool emits its registry
        // because it measures each clip's duration.
        if (!file.InDomain(TextureDomain))
        {
            return null;
        }

        // The build ships the asset at the normalized path, so the handle names the key and not the
        // authored spelling.
        string path = file.Authored;
        string extension = Path.GetExtension(file.Text.Path);

        return TypeNaming.NormalizeKey(path, out _) is { } key
            ? new AssetModel(TextureDomain, key, path, extension, file.Text.Path, AssetFault.None)
            : new AssetModel(TextureDomain, path, path, extension, file.Text.Path, AssetFault.UnsafeName);
    }

    /// <summary>Reads one fonts-domain page into the key and extension the build ships it at.</summary>
    internal static KeyValuePair<string, string>? DescribePage(AssetFile file)
    {
        string extension = Path.GetExtension(file.Text.Path);
        if (string.Equals(extension, BmFontParser.BmFontExtension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // A page whose path is not a valid key is refused by the key pass over the same paths.
        return TypeNaming.NormalizeKey(file.Authored, out _) is { } key
            ? new KeyValuePair<string, string>(key, extension)
            : null;
    }

    internal static void Emit(
        SourceProductionContext context,
        ImmutableArray<AssetModel> models,
        ImmutableArray<KeyValuePair<string, string>> pages,
        ImmutableArray<ParsedAsset<BmFontDescription>> fonts,
        bool emitting)
    {
        if (!emitting)
        {
            return;
        }

        RegistryDomain<AssetModel> textures = new(
            "Textures",
            TextureDomain,
            HandleType,
            "asset",
            "Everything shipped at <c>assets/" + TextureDomain + "</c>.",
            AppendHandle);

        // Sorted before the tree is built from it. Additional files arrive in MSBuild's collection
        // order, and the generated source must not reorder between machines.
        List<AssetModel> sound = new(models.Length);
        foreach (AssetModel model in models)
        {
            if (model.Fault == AssetFault.UnsafeName)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnsafeAssetName, ParsedAsset.At(model.Source), model.Display));
                continue;
            }

            sound.Add(model);
        }

        sound.Sort(static (left, right) =>
        {
            int byPath = string.CompareOrdinal(left.Path, right.Path);

            return byPath != 0 ? byPath : string.CompareOrdinal(left.Extension, right.Extension);
        });

        foreach (AssetModel model in sound)
        {
            textures.Add(model.Path, model.Display, model, Refused<AssetModel>(context, ParsedAsset.At(model.Source)));
        }

        StringBuilder source = RegistryFile.Open();
        textures.Append(source, "        ");
        source.AppendLine();
        Fonts(context, pages, fonts).Append(source, "        ");

        context.AddSource(FileName, SourceText.From(RegistryFile.Close(source), Encoding.UTF8));
    }

    private static RegistryDomain<FontSource> Fonts(
        SourceProductionContext context,
        ImmutableArray<KeyValuePair<string, string>> pages,
        ImmutableArray<ParsedAsset<BmFontDescription>> fonts)
    {
        RegistryDomain<FontSource> registry = FontRegistrySource.Registry();

        Dictionary<string, string> shipped = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> page in pages)
        {
            shipped[page.Key] = page.Value;
        }

        List<ParsedAsset<BmFontDescription>> ordered = [.. fonts];
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        foreach (ParsedAsset<BmFontDescription> font in ordered)
        {
            if (font.Fault == ParsedFault.UnsafeName)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnsafeAssetName, font.Location, font.Display));
                continue;
            }

            if (font.Parsed is not { } described)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnreadableFont, font.Location, font.Display, font.Message));
                continue;
            }

            if (FontRegistrySource.Resolve(font.Key, described, shipped, out string? error) is not { } source)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnshippedFontPage, font.Location, font.Display, error));
                continue;
            }

            registry.Add(font.Key, font.Display, source, Refused<FontSource>(context, font.Location));
        }

        return registry;
    }

    // Turns a registry refusal into the diagnostic the game sees at the source's file.
    internal static RegistryClaimCheck<T> Refused<T>(SourceProductionContext context, Location location) =>
        RegistryClaims.Check<T>(refusal => context.ReportDiagnostic(
            refusal.Fault == RegistryFault.AlreadyDeclared
                ? Diagnostic.Create(
                    RegistryDiagnostics.DuplicateAssetIdentifier,
                    location,
                    refusal.ClaimedBy,
                    refusal.Display,
                    refusal.Identifier,
                    refusal.Directory)
                : Diagnostic.Create(
                    RegistryDiagnostics.AssetNamedAfterItsDomain,
                    location,
                    refusal.Display,
                    refusal.Identifier,
                    refusal.Directory)));

    private static void AppendHandle(StringBuilder source, string indent, string identifier, AssetModel model)
    {
        source.Append(indent).Append("/// <summary><c>assets/").Append(model.Shipped).AppendLine("</c>.</summary>");
        source.Append(indent).Append("public static ").Append(HandleType).Append(' ').Append(identifier);
        source.Append(" => new ").Append(HandleType).Append('(');
        source.Append(SymbolDisplay.FormatLiteral(model.Path, quote: true));
        source.Append(", ");
        source.Append(SymbolDisplay.FormatLiteral(model.Extension, quote: true));
        source.AppendLine(");");
    }
}
