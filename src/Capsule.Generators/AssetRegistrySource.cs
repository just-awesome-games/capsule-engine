using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal static class AssetRegistrySource
{
    internal const string DomainMetadata = "build_metadata.AdditionalFiles.CapsuleAssetDomain";

    internal const string PathMetadata = "build_metadata.AdditionalFiles.CapsuleAssetPath";

    private const string FileName = "CapsuleAssets.g.cs";

    private const string TextureDomain = "textures";

    private const string HandleType = "global::Capsule.Assets.TextureHandle";

    /// <summary>Whether the domain <paramref name="text"/> declares is one this generator emits.</summary>
    internal static bool InDomain(AdditionalText text, AnalyzerConfigOptionsProvider options, string domain) =>
        options.GetOptions(text).TryGetValue(DomainMetadata, out string? declared)
        && string.Equals(declared, domain, StringComparison.Ordinal);

    /// <summary>The path the asset hook authored <paramref name="text"/> at, with one spelling.</summary>
    internal static string Authored(AdditionalText text, AnalyzerConfigOptionsProvider options) =>
        // MSBuild's %(RecursiveDir) carries the platform's separator; a handle has one spelling.
        options.GetOptions(text).TryGetValue(PathMetadata, out string? authored) && !string.IsNullOrEmpty(authored)
            ? authored!.Replace('\\', '/')
            : Path.GetFileNameWithoutExtension(text.Path);

    internal static AssetModel? Describe(AdditionalText text, AnalyzerConfigOptionsProvider options)
    {
        // Every other additional file a project carries reaches this the same way and is no asset.
        // A domain this generator declares no class for is no asset of its either: audio arrives
        // here like every other shipped file, and its registry is emitted by the build tool, which
        // measures each clip's duration.
        if (!options.GetOptions(text).TryGetValue(DomainMetadata, out string? domain)
            || !string.Equals(domain, TextureDomain, StringComparison.Ordinal))
        {
            return null;
        }

        string path = Authored(text, options);

        // The key, not the spelling: the build ships the asset at the normalized path, so the
        // handle this declares must name that and no other.
        string extension = Path.GetExtension(text.Path);

        return TypeNaming.NormalizeKey(path, out _) is { } key
            ? new AssetModel(domain!, key, path, extension, AssetFault.None)
            : new AssetModel(domain!, path, path, extension, AssetFault.UnsafeName);
    }

    /// <summary>Reads one fonts-domain page into the key and spelling the build ships it at.</summary>
    internal static KeyValuePair<string, string>? DescribePage(AdditionalText text, AnalyzerConfigOptionsProvider options)
    {
        string extension = Path.GetExtension(text.Path);
        if (string.Equals(extension, BmFontParser.BmFontExtension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // A page whose path is no key fails the key pass, which reads the same authored paths.
        return TypeNaming.NormalizeKey(Authored(text, options), out _) is { } key
            ? new KeyValuePair<string, string>(key, extension)
            : null;
    }

    internal static void Emit(
        SourceProductionContext context,
        ImmutableArray<AssetModel> models,
        ImmutableArray<KeyValuePair<string, string>> pages,
        ImmutableArray<FontModel> fonts,
        ImmutableArray<SheetModel> sheets,
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

        // Sorted before the tree is built off it: the additional files arrive in whatever order
        // MSBuild collected them, and the generated source must not reorder between machines.
        List<AssetModel> sound = new(models.Length);
        foreach (AssetModel model in models)
        {
            if (model.Fault == AssetFault.UnsafeName)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnsafeAssetName, Location.None, model.Display));
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
            textures.Add(model.Path, model.Display, model, Claimable<AssetModel>(context));
        }

        StringBuilder source = RegistryFile.Open();
        textures.Append(source, "        ");
        source.AppendLine();
        Fonts(context, pages, fonts).Append(source, "        ");
        source.AppendLine();
        Sprites(context, sound, sheets).Append(source, "        ");

        context.AddSource(FileName, SourceText.From(RegistryFile.Close(source), Encoding.UTF8));
    }

    private static RegistryDomain<SheetDocument> Sprites(
        SourceProductionContext context,
        List<AssetModel> textures,
        ImmutableArray<SheetModel> sheets)
    {
        RegistryDomain<SheetDocument> registry = SpriteRegistrySource.Registry();

        // Ordinal, not case-insensitive: the runtime store is keyed by the shipped spelling, so a
        // sheet naming 'player.PNG' against 'player.png' would carry a handle nothing loaded.
        HashSet<string> shipped = new(StringComparer.Ordinal);
        foreach (AssetModel texture in textures)
        {
            shipped.Add(texture.Path + texture.Extension);
        }

        List<SheetModel> ordered = [.. sheets];
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        foreach (SheetModel sheet in ordered)
        {
            if (sheet.Fault == SheetFault.UnsafeName)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnsafeAssetName, sheet.Location, sheet.Display));
                continue;
            }

            if (sheet.Fault != SheetFault.None || sheet.Document is not { } document)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnreadableSheet, sheet.Location, sheet.Display, sheet.Message));
                continue;
            }

            if (!shipped.Contains(document.Texture))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnshippedSheetTexture,
                    sheet.Location,
                    sheet.Display,
                    $"cuts from texture \"{document.Texture}\", which this game does not ship; author it at Assets/Textures/{document.Texture}."));
                continue;
            }

            registry.Add(sheet.Key, sheet.Display, document, Claimable<SheetDocument>(context, SpriteRegistrySource.Reserves));
        }

        return registry;
    }

    private static RegistryDomain<FontSource> Fonts(
        SourceProductionContext context,
        ImmutableArray<KeyValuePair<string, string>> pages,
        ImmutableArray<FontModel> fonts)
    {
        RegistryDomain<FontSource> registry = FontRegistrySource.Registry();

        Dictionary<string, string> shipped = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> page in pages)
        {
            shipped[page.Key] = page.Value;
        }

        List<FontModel> ordered = [.. fonts];
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        foreach (FontModel font in ordered)
        {
            if (font.Fault == FontFault.UnsafeName)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnsafeAssetName, font.Location, font.Display));
                continue;
            }

            if (font.Fault != FontFault.None || font.Description is not { } described)
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

            registry.Add(font.Key, font.Display, source, Claimable<FontSource>(context));
        }

        return registry;
    }

    // Whether an identifier may be declared on this class: not the class's own name (CS0542), not a
    // name the domain's own generated members take, and not one already claimed here.
    private static RegistryClaimCheck<T> Claimable<T>(
        SourceProductionContext context,
        Func<string, bool, bool>? reserves = null) =>
        (node, identifier, display, leaf) =>
        {
            bool reserved = reserves is null
                ? string.Equals(identifier, RegistryFile.ListMember, StringComparison.Ordinal)
                : reserves(identifier, leaf);

            if (string.Equals(identifier, node.Identifier, StringComparison.Ordinal) || reserved)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.AssetNamedAfterItsDomain, Location.None, display, identifier, node.Display));

                return false;
            }

            if (node.ClaimedBy.TryGetValue(identifier, out string? claimed))
            {
                // A directory declared twice is one class, not a collision.
                if (node.Directories.ContainsKey(identifier) && string.Equals(claimed, display, StringComparison.Ordinal))
                {
                    return true;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DuplicateAssetIdentifier, Location.None, claimed, display, identifier, node.Display));

                return false;
            }

            return true;
        };

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
