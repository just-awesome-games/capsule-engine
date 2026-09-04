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

        DocumentSource[] sources;
        try
        {
            sources = DocumentSource.Read(listPath, DocumentExtension);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot read the source list '{listPath}' — {ex.Message}");
            return 1;
        }

        return Import(outputDirectory, sources, tileSize, output, error);
    }

    /// <summary>Imports <paramref name="sources"/>, each derived to <c>&lt;key&gt;.scene.json</c>.</summary>
    /// <param name="outputDirectory">Where the canonical documents are written.</param>
    /// <param name="sources">The scene sources to import, each with the key it claims.</param>
    /// <param name="tileSize">The tile size every grid must be authored at, or null to impose none.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every source succeeded, 1 when any failed.</returns>
    internal static int Import(
        string outputDirectory,
        IReadOnlyList<DocumentSource> sources,
        int? tileSize,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(sources);
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
        foreach (DocumentSource source in sources)
        {
            if (!source.HasSafeKey())
            {
                error.WriteLine(
                    $"{source.Path}: claims scene key \"{source.Key}\"; a key is one or more '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name (nul, con, ...), and carries no extension.");
                failures++;
                continue;
            }

            string documentPath = Path.Combine(outputDirectory, source.Key + DocumentExtension);

            if (!claimedBy.TryAdd(documentPath, source.Path))
            {
                error.WriteLine(
                    $"{source.Path}: would overwrite the scene document of '{claimedBy[documentPath]}'; a document is written at the key its source claims, so keys must be unique.");
                failures++;
                continue;
            }

            try
            {
                // The key nests, so the directory the document lands in may not exist yet.
                Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
                SceneDocumentFile.Save(NativeSceneImporter.Import(source.Path, tileSize), documentPath);
                output.WriteLine($"{Name}: {source.Path} -> {documentPath}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{source.Path}: {ex.Message}");
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"{Name}: {failures} of {sources.Count} source(s) failed");
            return 1;
        }

        return 0;
    }

    private static bool IsReportable(Exception exception) =>
        exception is SceneDocumentFormatException or IOException
            or UnauthorizedAccessException or DecoderFallbackException;
}
