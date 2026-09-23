using Capsule.Build.Atlases;
using Capsule.Build.Audio;
using Capsule.Build.Keys;
using Capsule.Build.Scenes;
using Capsule.Build.Sprites;

namespace Capsule.Build;

/// <summary>
/// One run over one request manifest: key every authored path, derive every keyed scene, measure the
/// clip set, pack every atlas, and write the manifests the targets read their items back from. The
/// scene, audio and atlas steps are independent. One run reports every authoring defect instead of
/// stopping at the first kind it met.
/// </summary>
internal static class BuildRun
{
    /// <summary>Where the keyed scenes are derived, below the output directory.</summary>
    private const string ScenesDirectory = "scenes";

    /// <summary>The clip set rendered as a single C# file a game compiles against.</summary>
    private const string AudioRegistryFile = "CapsuleAssets.Audio.g.cs";

    /// <summary>The sheet set rendered as a single C# file a game compiles against.</summary>
    private const string SpriteRegistryFile = "CapsuleAssets.Sprites.g.cs";

    /// <summary>Where atlas pages, stamps and the map are written, below the output directory.</summary>
    private const string AtlasesDirectory = "atlases";

    /// <summary>
    /// Empty, and written last. It is the run's single MSBuild output. A run that failed part way
    /// leaves it older than the manifest, and the next build runs the pass again.
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

        // Nothing downstream can name a source whose key the pass refused. A keying failure ends
        // the run.
        List<KeyedAsset> keyed = [];
        if (KeyTool.Derive(requests.Assets, keyed, error) > 0)
        {
            return 1;
        }

        string scenesDirectory = Path.Combine(outputDirectory, ScenesDirectory);
        DocumentSource[] scenes = Sources(keyed, "scenes");
        DocumentSource[] clips = Sources(keyed, "audio");
        DocumentSource[] sheets = Sources(keyed, "sprites");

        int failed = 0;

        // Every scene's baseScene and camera, keyed the way the derived document itself is, so the
        // manifest pass can carry them out to the generator beside the key.
        Dictionary<string, (string? BaseScene, string? Camera)> sceneFields = new(StringComparer.Ordinal);
        if (scenes.Length > 0)
        {
            failed += SceneDocumentTool.Import(scenesDirectory, scenes, requests.TileSize, sceneFields, output, error);
        }

        // Left alone when nothing asked for audio. A game with no clips keeps the registry it
        // already has instead of losing it mid-build.
        if (clips.Length > 0)
        {
            failed += AudioTool.Emit(clips, Path.Combine(outputDirectory, AudioRegistryFile), output, error);
        }

        // Left alone when nothing asked for sprites, for the same reason as the audio registry.
        if (sheets.Length > 0)
        {
            failed += SpriteTool.Emit(
                sheets,
                Textures(keyed),
                Path.Combine(outputDirectory, SpriteRegistryFile),
                output,
                error);
        }

        HashSet<string> packedTextures = new(StringComparer.Ordinal);
        List<string> atlasLines = [];
        try
        {
            failed += AtlasTool.Pack(keyed, Path.Combine(outputDirectory, AtlasesDirectory), output, error, packedTextures, atlasLines);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{outputDirectory}: atlases cannot be written: {ex.Message}");

            return 1;
        }

        try
        {
            KeyTool.WriteManifests(keyed, outputDirectory, scenesDirectory, packedTextures, atlasLines, sceneFields);

            if (failed == 0)
            {
                AtomicFile.Write(
                    Path.Combine(outputDirectory, StampFile),
                    path => File.WriteAllText(path, string.Empty));
            }
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{outputDirectory}: cannot be written: {ex.Message}");

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

    // Every shipped texture by key. A sheet's texture reference resolves through this, so the sheet
    // carries the extension the build wrote instead of the extension the sheet spelled.
    private static Dictionary<string, string> Textures(List<KeyedAsset> keyed)
    {
        Dictionary<string, string> textures = new(StringComparer.Ordinal);

        foreach (KeyedAsset entry in keyed)
        {
            if (entry.Group == "textures")
            {
                textures[entry.Key] = entry.Extension;
            }
        }

        return textures;
    }

    private static bool IsReportable(Exception exception) =>
        exception is FormatException or IOException or UnauthorizedAccessException;
}
