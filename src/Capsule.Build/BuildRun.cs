using Capsule.Build.Audio;
using Capsule.Build.Keys;
using Capsule.Build.Scenes;

namespace Capsule.Build;

/// <summary>
/// One run over one request manifest: key every authored path, derive every keyed scene, measure
/// the whole clip set, and write the manifests the targets read their items back from. The scene
/// and audio steps are independent, so one run reports every defect in the authoring plane rather
/// than the first kind of defect it met.
/// </summary>
internal static class BuildRun
{
    /// <summary>Where the keyed scenes are derived, below the output directory.</summary>
    private const string ScenesDirectory = "scenes";

    /// <summary>The whole clip set rendered as the one C# file a game compiles against.</summary>
    private const string AudioRegistryFile = "CapsuleAssets.Audio.g.cs";

    /// <summary>
    /// Empty, and written last: it is the run's single MSBuild output, so a run that failed part
    /// way leaves it older than the manifest and the next build runs the whole pass again.
    /// </summary>
    private const string StampFile = "build.stamp";

    /// <param name="requestsPath">The manifest to run.</param>
    /// <param name="outputDirectory">Where everything derived and every read-back manifest is written.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every step succeeded, 1 when any failed.</returns>
    internal static int Run(string requestsPath, string outputDirectory, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        BuildRequests requests;
        try
        {
            Directory.CreateDirectory(outputDirectory);
            requests = BuildRequests.Read(requestsPath);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{requestsPath}: {ex.Message}");

            return 1;
        }

        // Nothing downstream can name a source whose key the pass refused, so keying is the one
        // step whose failure ends the run.
        List<KeyedAsset> keyed = [];
        if (KeyTool.Derive(requests.Assets, keyed, error) > 0)
        {
            return 1;
        }

        string scenesDirectory = Path.Combine(outputDirectory, ScenesDirectory);
        DocumentSource[] scenes = Sources(keyed, "scenes");
        DocumentSource[] clips = Sources(keyed, "audio");

        int failed = 0;

        if (scenes.Length > 0)
        {
            failed += SceneDocumentTool.Import(scenesDirectory, scenes, requests.TileSize, output, error);
        }

        // Left alone when nothing asked for audio, so a game with no clips compiles the registry it
        // already has rather than losing one mid-build.
        if (clips.Length > 0)
        {
            failed += AudioTool.Emit(clips, Path.Combine(outputDirectory, AudioRegistryFile), output, error);
        }

        try
        {
            KeyTool.WriteManifests(keyed, outputDirectory, scenesDirectory);

            if (failed == 0)
            {
                AtomicFile.Write(
                    Path.Combine(outputDirectory, StampFile),
                    path => File.WriteAllText(path, string.Empty));
            }
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{outputDirectory}: cannot be written — {ex.Message}");

            return 1;
        }

        if (failed > 0)
        {
            return 1;
        }

        output.WriteLine($"capsule: {keyed.Count} source(s) keyed into {outputDirectory}");

        return 0;
    }

    private static DocumentSource[] Sources(List<KeyedAsset> keyed, string group) =>
        [.. keyed
            .Where(entry => entry.Group == group)
            .Select(static entry => new DocumentSource(entry.Key, entry.Source))];

    private static bool IsReportable(Exception exception) =>
        exception is FormatException or IOException or UnauthorizedAccessException;
}
