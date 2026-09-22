using System.Text;
using Capsule.Scenes.Documents;

namespace Capsule.Build.Scenes;

/// <summary>
/// The scene half of the build hook. Every authored source is re-derived canonically under the
/// output directory, at the key its request claimed.
/// </summary>
internal static class SceneDocumentTool
{
    internal const string DocumentExtension = ".scene.json";

    private const string Name = "scene documents";

    /// <summary>Imports <paramref name="sources"/>, each derived to <c>&lt;key&gt;.scene.json</c>.</summary>
    /// <param name="outputDirectory">Where the canonical documents are written.</param>
    /// <param name="sources">The scene sources to import, each with the key it claims.</param>
    /// <param name="tileSize">The tile size every grid must be authored at, or null to impose none.</param>
    /// <param name="fields">Receives each imported document's baseScene and camera, keyed the same way.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every source succeeded, 1 when any failed.</returns>
    internal static int Import(
        string outputDirectory,
        IReadOnlyList<DocumentSource> sources,
        int? tileSize,
        IDictionary<string, (string? BaseScene, string? Camera)> fields,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot create '{outputDirectory}': {ex.Message}");

            return 1;
        }

        int failures = 0;

        foreach (DocumentSource source in sources)
        {
            string documentPath = Path.Combine(outputDirectory, source.Key + DocumentExtension);

            try
            {
                // A key may nest, so the directory the document lands in may not exist yet.
                Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
                SceneDocument document = NativeSceneImporter.Import(source.Path, tileSize);
                AtomicFile.Write(documentPath, path => SceneDocumentFile.Save(document, path));
                fields[source.Key] = (document.Settings.BaseScene, document.Settings.Camera);
                output.WriteLine($"{Name}: {source.Path} -> {documentPath}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{source.Path}: {Unanchored(ex.Message, source.Path)}");
                failures++;
            }
        }

        if (failures == 0)
        {
            return 0;
        }

        error.WriteLine($"{Name}: {failures} of {sources.Count} source(s) failed");

        return 1;
    }

    // Every line is already prefixed with the path. A message the document reader anchored to the
    // same path must not be anchored twice.
    private static string Unanchored(string message, string sourcePath)
    {
        string prefix = sourcePath + ": ";

        return message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message;
    }

    private static bool IsReportable(Exception exception) =>
        exception is SceneDocumentFormatException or IOException
            or UnauthorizedAccessException or DecoderFallbackException;
}
