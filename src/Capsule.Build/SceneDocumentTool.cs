using System.Text;
using Capsule.Scenes.Documents;

namespace Capsule.Build;

public static class SceneDocumentTool
{
    public const string DocumentExtension = ".scene.json";

    private const string Name = "scene documents";

    public static int ImportFromList(
        string outputDirectory,
        string listPath,
        int? tileSize,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        string[] sourcePaths;
        try
        {
            sourcePaths = File.ReadAllLines(listPath)
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0)
                .ToArray();
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot read the source list '{listPath}' — {ex.Message}");
            return 1;
        }

        return Import(outputDirectory, sourcePaths, tileSize, output, error);
    }

    public static int Import(
        string outputDirectory,
        IReadOnlyList<string> sourcePaths,
        int? tileSize,
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
            error.WriteLine($"{Name}: cannot create '{outputDirectory}' — {ex.Message}");
            return 1;
        }

        Dictionary<string, string> claimedBy = new(StringComparer.OrdinalIgnoreCase);
        int failures = 0;
        foreach (string sourcePath in sourcePaths)
        {
            string documentPath = Path.Combine(outputDirectory, Stem(sourcePath) + DocumentExtension);

            if (!claimedBy.TryAdd(documentPath, sourcePath))
            {
                error.WriteLine(
                    $"{sourcePath}: would overwrite the scene document of '{claimedBy[documentPath]}'; a document is named after its source, so source names must be unique.");
                failures++;
                continue;
            }

            try
            {
                SceneDocumentFile.Save(NativeSceneImporter.Import(sourcePath, tileSize), documentPath);
                output.WriteLine($"{Name}: {sourcePath} -> {documentPath}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{sourcePath}: {ex.Message}");
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"{Name}: {failures} of {sourcePaths.Count} source(s) failed");
            return 1;
        }

        return 0;
    }

    // Not GetFileNameWithoutExtension: the format's extension is two of them, and stripping one
    // would leave a scene named 'room.scene'.
    private static string Stem(string sourcePath)
    {
        string name = Path.GetFileName(sourcePath);

        return name.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^DocumentExtension.Length]
            : Path.GetFileNameWithoutExtension(name);
    }

    private static bool IsReportable(Exception exception) =>
        exception is SceneDocumentFormatException or IOException
            or UnauthorizedAccessException or DecoderFallbackException;
}
