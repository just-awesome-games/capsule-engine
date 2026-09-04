using System.Text;
using Capsule.Scenes.Documents;

namespace Capsule.Build;

internal static class SceneDocumentTool
{
    internal const string DocumentExtension = ".scene.json";

    private const string Name = "scene documents";

    internal static int ImportFromList(
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

        return DocumentImport.Run(
            Name,
            "scene",
            outputDirectory,
            sources,
            DocumentExtension,
            IsReportable,
            (source, documentPath) =>
                SceneDocumentFile.Save(NativeSceneImporter.Import(source.Path, tileSize), documentPath),
            output,
            error);
    }

    private static bool IsReportable(Exception exception) =>
        exception is SceneDocumentFormatException or IOException
            or UnauthorizedAccessException or DecoderFallbackException;
}
