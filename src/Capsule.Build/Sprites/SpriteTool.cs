using System.Text;
using Capsule.Generators;

namespace Capsule.Build.Sprites;

/// <summary>
/// The sprite half of the build hook. Reads every sheet a game authors and renders the set as a
/// single C# file a game compiles. Nothing is derived onto disk, and a sheet does not ship.
/// </summary>
internal static class SpriteTool
{
    private const string Name = "sprites";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Reads every sheet and renders the set at <paramref name="generatedPath"/>. The file is
    /// rewritten in full. A sheet deleted since the last build leaves nothing behind.
    /// </summary>
    /// <param name="sources">The sheets to read, each with the key it claims.</param>
    /// <param name="textures">Every texture this game ships, by key, with the extension it ships under.</param>
    /// <param name="generatedPath">Where the generated C# is written.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every sheet succeeded, 1 when any failed.</returns>
    internal static int Emit(
        IReadOnlyList<DocumentSource> sources,
        IReadOnlyDictionary<string, string> textures,
        string generatedPath,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        List<DocumentSource> ordered = [.. sources];
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        RegistryDomain<SpriteSheet> registry = SpriteRegistrySource.Registry();
        int failures = 0;

        foreach (DocumentSource source in ordered)
        {
            SpriteSheet sheet;
            try
            {
                sheet = SpriteSheetFile.Read(source.Path);
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{source.Path}: {ex.Message}");
                failures++;
                continue;
            }

            // Resolved by key against what the build ships, and carrying the shipped extension. The
            // handle names the file the build wrote, however the sheet spelled it.
            if (!textures.TryGetValue(sheet.TextureKey, out string? extension))
            {
                error.WriteLine(
                    $"{source.Path}: cuts from texture \"{sheet.TextureKey}{sheet.TextureExtension}\", which this game does not ship. Author it at Assets/Textures/{sheet.TextureKey}{sheet.TextureExtension}.");
                failures++;
                continue;
            }

            string? refused = null;
            bool declared = registry.Add(
                source.Key,
                Name + "/" + source.Key + SpriteSheetFile.Extension,
                sheet with { TextureExtension = extension },
                RegistryClaims.Check<SpriteSheet>(
                    refusal => refused = Refusals.Because(refusal),
                    SpriteRegistrySource.Reserves));

            if (!declared)
            {
                error.WriteLine($"{source.Path}: is keyed \"{source.Key}\", {refused}");
                failures++;
                continue;
            }

            output.WriteLine($"{Name}: {source.Path} -> {source.Key}");
        }

        if (failures > 0)
        {
            error.WriteLine($"{Name}: {failures} of {sources.Count} source(s) failed");

            return 1;
        }

        StringBuilder generated = RegistryFile.Open();
        registry.Append(generated, "        ");

        try
        {
            AtomicFile.Write(generatedPath, path => File.WriteAllText(path, RegistryFile.Close(generated), Utf8NoBom));
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot write '{generatedPath}': {ex.Message}");

            return 1;
        }

        output.WriteLine($"{Name}: {sources.Count} sheet(s) -> {generatedPath}");

        return 0;
    }

    private static bool IsReportable(Exception exception) =>
        exception is FormatException or IOException or UnauthorizedAccessException;
}
