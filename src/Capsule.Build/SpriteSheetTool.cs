using System.Text;
using Capsule.Build.Sprites;

namespace Capsule.Build;

/// <summary>
/// The sheet half of the build hook: validates every authored <c>*.sheet.json</c>, re-emits it
/// canonically, and renders the whole set as the one C# file a game compiles against.
/// </summary>
internal static class SpriteSheetTool
{
    private const string Name = "sprite sheets";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Imports every sheet named in <paramref name="listPath"/>, one <c>key|path</c> per line, the
    /// path relative to the working directory.
    /// </summary>
    /// <param name="outputDirectory">Where the canonical documents are written.</param>
    /// <param name="listPath">The sheet sources to import.</param>
    /// <param name="texturesPath">The game's texture paths under the textures root, one per line.</param>
    /// <param name="generatedPath">Where the generated C# is written.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every source succeeded, 1 when any failed.</returns>
    internal static int ImportFromList(
        string outputDirectory,
        string listPath,
        string texturesPath,
        string generatedPath,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        DocumentSource[] sources;
        string[] textures;
        try
        {
            sources = DocumentSource.Read(listPath, SpriteSheetDocumentFile.DocumentExtension);
            textures = Lines(texturesPath);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot read the source list — {ex.Message}");
            return 1;
        }

        return Import(outputDirectory, sources, textures, generatedPath, output, error);
    }

    /// <summary>Imports <paramref name="sources"/>, as <see cref="ImportFromList"/> does.</summary>
    /// <param name="outputDirectory">Where the canonical documents are written.</param>
    /// <param name="sources">The sheet sources to import, each with the key it claims.</param>
    /// <param name="textures">The game's texture paths under the textures root, extension included.</param>
    /// <param name="generatedPath">Where the generated C# is written.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every source succeeded, 1 when any failed.</returns>
    internal static int Import(
        string outputDirectory,
        IReadOnlyList<DocumentSource> sources,
        IReadOnlyList<string> textures,
        string generatedPath,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        // Ordinal, not case-insensitive: the runtime store is keyed by the shipped spelling, so a
        // document naming 'player.PNG' against 'player.png' would carry a handle nothing loaded.
        // The build writes whichever separator its platform uses; the format has one spelling.
        HashSet<string> shipped = new(StringComparer.Ordinal);
        foreach (string texture in textures)
        {
            shipped.Add(texture.Replace('\\', '/'));
        }

        SheetNames declared = new();
        List<(string Key, SpriteSheetDocument Document)> sheets = new(sources.Count);

        int result = DocumentImport.Run(
            Name,
            "sheet",
            outputDirectory,
            sources,
            SpriteSheetDocumentFile.DocumentExtension,
            IsReportable,
            (source, documentPath) =>
            {
                declared.Declare(source.Key);

                SpriteSheetDocument document = SpriteSheetDocumentFile.Load(source.Path);
                string texture = document.Texture.Name + document.Texture.Extension;
                if (!shipped.Contains(texture))
                {
                    throw new SpriteSheetFormatException(
                        $"cuts from texture \"{texture}\", which this game does not ship; author it at asset-sources/textures/{texture}.");
                }

                SpriteSheetDocumentFile.Save(document, documentPath);
                sheets.Add((source.Key, document));
            },
            output,
            error);

        if (result != 0)
        {
            return result;
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

    private static string[] Lines(string path) =>
        [.. File.ReadAllLines(path).Select(static line => line.Trim()).Where(static line => line.Length > 0)];

    private static bool IsReportable(Exception exception) =>
        exception is SpriteSheetFormatException or IOException
            or UnauthorizedAccessException or DecoderFallbackException;
}
