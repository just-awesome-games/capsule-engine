using System.Text;
using Capsule.Cli.Tiled;
using Capsule.Scenes.Documents;

namespace Capsule.Cli;

public static class SceneDocumentTool
{
    public const string DocumentExtension = ".scene.json";

    public static int ImportTiledFromList(
        string outputDirectory,
        string listPath,
        int? tileSize,
        TextWriter output,
        TextWriter error,
        string? dependencyRoot = null) =>
        ImportFromList(Tiled(tileSize, dependencyRoot), outputDirectory, listPath, output, error);

    public static int ImportTiled(
        string outputDirectory,
        IReadOnlyList<string> sourcePaths,
        int? tileSize,
        TextWriter output,
        TextWriter error,
        string? dependencyRoot = null) =>
        Import(Tiled(tileSize, dependencyRoot), outputDirectory, sourcePaths, output, error);

    public static int ImportNativeFromList(
        string outputDirectory,
        string listPath,
        int? tileSize,
        TextWriter output,
        TextWriter error) =>
        ImportFromList(Native(tileSize), outputDirectory, listPath, output, error);

    public static int ImportNative(
        string outputDirectory,
        IReadOnlyList<string> sourcePaths,
        int? tileSize,
        TextWriter output,
        TextWriter error) =>
        Import(Native(tileSize), outputDirectory, sourcePaths, output, error);

    private static Verb Tiled(int? tileSize, string? dependencyRoot) =>
        new(
            "import-tiled",
            static path => Path.GetFileNameWithoutExtension(path),
            path => TiledImporter.Import(path, tileSize, dependencyRoot));

    private static Verb Native(int? tileSize) =>
        new("import-native", NativeStem, path => NativeSceneImporter.Import(path, tileSize));

    // Not GetFileNameWithoutExtension: the format's extension is two of them, and stripping one
    // would leave a scene named 'room.scene' where the Tiled path names the same scene 'room' —
    // the duplicate-name check across the two source kinds only means anything if the stems agree.
    private static string NativeStem(string sourcePath)
    {
        string name = Path.GetFileName(sourcePath);

        return name.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^DocumentExtension.Length]
            : Path.GetFileNameWithoutExtension(name);
    }

    private static int ImportFromList(
        Verb verb,
        string outputDirectory,
        string listPath,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        string[] sourcePaths;
        try
        {
            sourcePaths = [.. File.ReadAllLines(listPath).Where(line => !string.IsNullOrWhiteSpace(line))];
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{verb.Name}: cannot read the source list '{listPath}' — {ex.Message}");
            return 1;
        }

        return Import(verb, outputDirectory, sourcePaths, output, error);
    }

    private static int Import(
        Verb verb,
        string outputDirectory,
        IReadOnlyList<string> sourcePaths,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{verb.Name}: cannot create '{outputDirectory}' — {ex.Message}");
            return 1;
        }

        Dictionary<string, string> claimedBy = new(StringComparer.OrdinalIgnoreCase);
        int failures = 0;
        foreach (string sourcePath in sourcePaths)
        {
            string documentPath = Path.Combine(outputDirectory, verb.Stem(sourcePath) + DocumentExtension);

            if (!claimedBy.TryAdd(documentPath, sourcePath))
            {
                error.WriteLine(
                    $"{sourcePath}: would overwrite the scene document of '{claimedBy[documentPath]}'; a document is named after its source, so source names must be unique.");
                failures++;
                continue;
            }

            try
            {
                SceneDocumentFile.Save(verb.Import(sourcePath), documentPath);
                output.WriteLine($"{verb.Name}: {sourcePath} -> {documentPath}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{sourcePath}: {ex.Message}");
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"{verb.Name}: {failures} of {sourcePaths.Count} source(s) failed");
            return 1;
        }

        return 0;
    }

    private static bool IsReportable(Exception exception) =>
        exception is SceneDocumentFormatException or TiledImportException or IOException
            or UnauthorizedAccessException or DecoderFallbackException;

    // One authoring format as the batch loop sees it: what to call it, how a source names its
    // scene document, and how a source becomes one.
    private sealed record Verb(string Name, Func<string, string> Stem, Func<string, SceneDocument> Import);
}
