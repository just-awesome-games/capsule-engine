using Capsule.Build.Caching;

namespace Capsule.Build.Sheets;

/// <summary>
/// Reads every sheet and declares it as a class of typed frames, clips and sockets, or a variant sheet
/// as its frames and the variant of its base. A misspelt frame, clip or socket is a compile error, and
/// no sheet ships beside the executable.
/// </summary>
internal static class SheetStep
{
    private const string Step = "sheets";

    internal static void Run(PipelinePass pass)
    {
        List<(Source Source, Sheet Document)> read = pass.Each(
            Step,
            pass.Of(AssetType.Sheets),
            source => Derivation.Of(source),
            (source, _) => SheetFile.Read(source.Path),
            DerivationCacheJsonContext.Default.Sheet);
        Dictionary<string, Sheet> byKey = read.ToDictionary(static sheet => sheet.Source.Key, static sheet => sheet.Document, StringComparer.Ordinal);

        foreach ((Source sheet, Sheet document) in read)
        {
            // Resolved by key against what the build ships, and carrying the shipped extension,
            // however the sheet spelled it. Checked on every run, since the texture is not the sheet's.
            if (!pass.Textures.TryGetValue(document.TextureKey, out Source texture))
            {
                pass.Fail(
                    sheet.Path,
                    $"cuts from texture \"{document.TextureKey}{document.TextureExtension}\", which this game does not ship. Author it at Assets/{document.TextureKey}{document.TextureExtension}.");
                continue;
            }

            Sheet shipped = document with { TextureExtension = texture.Extension };
            if (document.VariantOf is not { } baseKey)
            {
                pass.Declare(sheet, shipped, SheetMembers.Write);
                continue;
            }

            // Checked on every run, since the base is not the variant's. A base that failed its own
            // read has reported that, and the variant waits for it.
            if (byKey.TryGetValue(baseKey, out Sheet baseSheet))
            {
                if (VariantDefect(shipped, baseSheet, baseKey) is { } defect)
                {
                    pass.Fail(sheet.Path, defect);
                    continue;
                }

                pass.Declare(sheet, new SheetVariant(shipped, baseKey, baseSheet), SheetMembers.WriteVariant);
            }
            else if (!pass.Of(AssetType.Sheets).Any(source => string.Equals(source.Key, baseKey, StringComparison.Ordinal)))
            {
                pass.Fail(
                    sheet.Path,
                    $"is a variant of \"{baseKey}{AssetType.Sheets.Extensions[0]}\", which this game does not ship. Name a sheet authored under Assets/.");
            }
        }
    }

    // Why the variant cannot draw its base's frames, or null when it can.
    private static string? VariantDefect(Sheet variant, Sheet baseSheet, string baseKey)
    {
        string named = $"is a variant of \"{baseKey}{AssetType.Sheets.Extensions[0]}\"";
        if (baseSheet.VariantOf is { } further)
        {
            return $"{named}, which is itself a variant of \"{further}{AssetType.Sheets.Extensions[0]}\". A variant draws a sheet of its own. Name that sheet in \"variantOf\".";
        }

        Dictionary<string, SheetFrame> drawn = variant.Frames.ToDictionary(static frame => frame.Name, StringComparer.Ordinal);
        foreach (SheetFrame frame in baseSheet.Frames)
        {
            if (!drawn.ContainsKey(frame.Name))
            {
                return $"{named} and has no frame \"{frame.Name}\". A variant lists every frame of its base once. Add \"{frame.Name}\", with only its name to draw nothing.";
            }
        }

        HashSet<string> baseNames = baseSheet.Frames.Select(static frame => frame.Name).ToHashSet(StringComparer.Ordinal);
        foreach (SheetFrame frame in variant.Frames)
        {
            if (!baseNames.Contains(frame.Name))
            {
                return $"{named}, which has no frame \"{frame.Name}\". A variant lists only its base's frames. Remove \"{frame.Name}\", or rename it to a base frame.";
            }
        }

        // Two unmarked base frames drawing the same region from the same pivot are one sprite, which a
        // variant looks up by value and so draws one way.
        SheetFrame[] frames = baseSheet.Frames;
        for (int i = 0; i < frames.Length; i++)
        {
            for (int j = 0; j < i; j++)
            {
                if (frames[i].DrawsAs(frames[j]) && frames[i].Sockets.Length + frames[i].Boxes.Length + frames[j].Sockets.Length + frames[j].Boxes.Length == 0
                    && !drawn[frames[i].Name].DrawsAs(drawn[frames[j].Name]))
                {
                    return $"{named}, whose frames \"{frames[j].Name}\" and \"{frames[i].Name}\" are one sprite, and draws them differently. A variant finds a frame by its sprite, and one sprite draws one way. Draw both the same, or make the base frames differ.";
                }
            }
        }

        return null;
    }
}
