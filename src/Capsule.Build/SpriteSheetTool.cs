using System.Text;
using Capsule.Build.Sprites;

namespace Capsule.Build;

/// <summary>
/// The sheet half of the build hook: validates every authored <c>*.sheet.json</c>, re-emits it
/// canonically, and renders the whole set as the one C# file a game compiles against. Nothing it
/// writes ships beside the executable.
/// </summary>
public static class SpriteSheetTool
{
    private const string Name = "sprite sheets";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Imports every sheet named in <paramref name="listPath"/>, one path per line relative to the
    /// working directory.
    /// </summary>
    /// <param name="outputDirectory">Where the canonical documents are written.</param>
    /// <param name="listPath">The sheet sources to import.</param>
    /// <param name="texturesPath">The game's texture file names, one per line.</param>
    /// <param name="generatedPath">Where the generated C# is written.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every source succeeded, 1 when any failed.</returns>
    public static int ImportFromList(
        string outputDirectory,
        string listPath,
        string texturesPath,
        string generatedPath,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        string[] sourcePaths;
        string[] textures;
        try
        {
            sourcePaths = Lines(listPath);
            textures = Lines(texturesPath);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot read the source list — {ex.Message}");
            return 1;
        }

        return Import(outputDirectory, sourcePaths, textures, generatedPath, output, error);
    }

    /// <summary>Imports <paramref name="sourcePaths"/>, as <see cref="ImportFromList"/> does.</summary>
    /// <param name="outputDirectory">Where the canonical documents are written.</param>
    /// <param name="sourcePaths">The sheet sources to import.</param>
    /// <param name="textures">The game's texture file names, extension included.</param>
    /// <param name="generatedPath">Where the generated C# is written.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every source succeeded, 1 when any failed.</returns>
    public static int Import(
        string outputDirectory,
        IReadOnlyList<string> sourcePaths,
        IReadOnlyList<string> textures,
        string generatedPath,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot create '{outputDirectory}' — {ex.Message}");
            return 1;
        }

        // Ordinal, not case-insensitive: the runtime store is keyed by the shipped spelling, so a
        // document naming 'player.PNG' against 'player.png' would carry a handle nothing loaded.
        HashSet<string> shipped = new(textures, StringComparer.Ordinal);
        Dictionary<string, string> claimedBy = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> byIdentifier = new(StringComparer.Ordinal);
        List<(string Identifier, SpriteSheetDocument Document)> sheets = new(sourcePaths.Count);
        int failures = 0;

        foreach (string sourcePath in sourcePaths)
        {
            string stem = SpriteSheetDocumentFile.Stem(sourcePath);
            string documentPath = Path.Combine(outputDirectory, stem + SpriteSheetDocumentFile.DocumentExtension);

            try
            {
                if (claimedBy.TryGetValue(documentPath, out string? claimed))
                {
                    throw new SpriteSheetFormatException(
                        $"would overwrite the sheet document of '{claimed}'; a document is named after its source, so source names must be unique.");
                }

                string identifier = SheetIdentifier(stem, byIdentifier);
                claimedBy[documentPath] = sourcePath;
                byIdentifier[identifier] = stem;

                SpriteSheetDocument document = SpriteSheetDocumentFile.Load(sourcePath);
                string texture = document.Texture.Name + document.Texture.Extension;
                if (!shipped.Contains(texture))
                {
                    throw new SpriteSheetFormatException(
                        $"cuts from texture \"{texture}\", which this game does not ship; author it at asset-sources/textures/{texture}.");
                }

                SpriteSheetDocumentFile.Save(document, documentPath);
                sheets.Add((identifier, document));
                output.WriteLine($"{Name}: {sourcePath} -> {documentPath}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{sourcePath}: {Message(ex, sourcePath)}");
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"{Name}: {failures} of {sourcePaths.Count} source(s) failed");
            return 1;
        }

        try
        {
            // Written whole every time, so a sheet deleted since the last build leaves nothing
            // behind for a game to still compile against.
            File.WriteAllText(generatedPath, SpriteRegistrySource.Render(sheets), Utf8NoBom);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot write '{generatedPath}' — {ex.Message}");
            return 1;
        }

        output.WriteLine($"{Name}: {sheets.Count} sheet(s) -> {generatedPath}");

        return 0;
    }

    // A sheet's own name has the frame and clip names' constraints and two more: it is declared on
    // the registry class and declares the frame and clip classes, and a type may not carry the name
    // of the type it is declared on or of one it declares (CS0542).
    private static string SheetIdentifier(string stem, Dictionary<string, string> byIdentifier)
    {
        if (SpriteSheetNaming.ToIdentifier(stem) is not { } identifier)
        {
            throw new SpriteSheetFormatException(
                $"is named \"{stem}\", which is no C# name; a sheet name is letters, digits, '-' and '_', and does not start with a digit.");
        }

        if (identifier is SpriteRegistrySource.RegistryClass
            or SpriteRegistrySource.FramesClass
            or SpriteRegistrySource.ClipsClass)
        {
            throw new SpriteSheetFormatException(
                $"is named \"{stem}\", which is one of the generated classes it would be declared beside ('{SpriteRegistrySource.RegistryClass}', '{SpriteRegistrySource.FramesClass}', '{SpriteRegistrySource.ClipsClass}'); name it something else.");
        }

        if (byIdentifier.TryGetValue(identifier, out string? claimed))
        {
            throw new SpriteSheetFormatException(
                $"is named \"{stem}\" and \"{claimed}\" is already declared as '{identifier}'; two names that differ only in their separators are one C# name.");
        }

        return identifier;
    }

    // The path is the prefix of every line already, so a message the document reader anchored to
    // the same path is not anchored to it twice.
    private static string Message(Exception exception, string sourcePath)
    {
        string message = exception.Message;
        string prefix = sourcePath + ": ";

        return message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message;
    }

    private static string[] Lines(string path) =>
        [.. File.ReadAllLines(path).Select(static line => line.Trim()).Where(static line => line.Length > 0)];

    private static bool IsReportable(Exception exception) =>
        exception is SpriteSheetFormatException or IOException
            or UnauthorizedAccessException or DecoderFallbackException;
}
