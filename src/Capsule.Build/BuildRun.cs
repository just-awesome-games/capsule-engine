using System.Text;
using Capsule.Build.Atlases;
using Capsule.Build.Audio;
using Capsule.Build.Fonts;
using Capsule.Build.Registry;
using Capsule.Build.Scenes;
using Capsule.Build.Shaders;
using Capsule.Build.Sprites;
using Capsule.Build.Textures;
using Capsule.Scenes.Documents;

namespace Capsule.Build;

/// <summary>
/// One run over one request manifest. It keys every source, derives each type, and writes two things
/// under the output directory: <c>assets/</c> holding exactly what the game ships, laid out as the game
/// authored it, and <c>CapsuleAssets.g.cs</c> declaring every asset. A run reports every authoring
/// defect instead of stopping at the first.
/// </summary>
internal static class BuildRun
{
    /// <summary>
    /// Empty, and written last. It is the run's single MSBuild output. A run that failed part way
    /// leaves it older than the manifest, and the next build runs the pass again.
    /// </summary>
    private const string StampFile = "build.stamp";

    /// <param name="requestsPath">The manifest to run.</param>
    /// <param name="outputDirectory">Where everything is written.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every step succeeded, 1 when any failed.</returns>
    internal static int Run(string requestsPath, string outputDirectory, TextWriter output, TextWriter error)
    {
        BuildRequests requests;
        try
        {
            Directory.CreateDirectory(outputDirectory);
            requests = BuildRequests.Read(requestsPath);
        }
        catch (Exception ex) when (BuildPass.IsReportable(ex))
        {
            error.WriteLine($"{requestsPath}: {ex.Message}");

            return 1;
        }

        // Nothing downstream can name a source whose key the pass refused.
        List<Source> keyed = [];
        if (Keys.Derive(requests, keyed, error) > 0)
        {
            return 1;
        }

        BuildPass pass = new(requests, keyed, outputDirectory, output, error);
        try
        {
            // Fonts first: a texture a font names is its page, and no atlas packs one.
            HashSet<string> pages = FontStep.Build(pass);
            TextureStep.Build(pass, AtlasStep.Pack(pass, pages));
            AudioStep.Build(pass);
            SpriteStep.Build(pass);
            ShaderStep.Build(pass);
            SceneStep.Build(pass);

            // Rendered whatever failed, so a name C# would refuse is reported beside every other defect.
            string? generated = pass.Assets.Render(error);
            if (pass.Failures > 0 || generated is null)
            {
                return 1;
            }

            AtomicFile.WriteText(Path.Combine(outputDirectory, CapsuleAssetsFile.FileName), generated);
            pass.Shipped.Prune();
            // Always written, never compared, since its timestamp is what MSBuild reads.
            File.WriteAllText(Path.Combine(outputDirectory, StampFile), string.Empty);
        }
        catch (Exception ex) when (BuildPass.IsReportable(ex))
        {
            error.WriteLine($"{outputDirectory}: cannot be written: {ex.Message}");

            return 1;
        }

        output.WriteLine($"capsule: {keyed.Count} source(s) built into {outputDirectory}");

        return 0;
    }
}

/// <summary>What every type's step reads and writes during one run.</summary>
internal sealed class BuildPass(
    BuildRequests requests,
    IReadOnlyList<Source> keyed,
    string outputDirectory,
    TextWriter output,
    TextWriter error)
{
    internal BuildRequests Requests { get; } = requests;

    /// <summary>Where a step keeps what it reuses between runs and does not ship.</summary>
    internal string OutputDirectory { get; } = outputDirectory;

    internal ShippedFiles Shipped { get; } = new(Path.Combine(outputDirectory, "assets"));

    internal CapsuleAssetsFile Assets { get; } = new();

    internal TextWriter Output { get; } = output;

    internal TextWriter Error { get; } = error;

    internal int Failures { get; set; }

    /// <summary>Every source of <paramref name="type"/>, in request order.</summary>
    internal IEnumerable<Source> Of(AssetType type) => keyed.Where(source => source.Type == type);

    /// <summary>
    /// Reads every source through <paramref name="read"/>. A failure is reported against its source
    /// and counted, and the rest are still read.
    /// </summary>
    internal List<(Source Source, T Value)> Each<T>(IEnumerable<Source> sources, Func<Source, T> read)
    {
        List<(Source, T)> results = [];
        foreach (Source source in sources)
        {
            try
            {
                results.Add((source, read(source)));
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                Fail(source.Path, ex.Message);
            }
        }

        return results;
    }

    /// <summary>Declares <paramref name="source"/> on <c>CapsuleAssets</c>, written by <paramref name="write"/>.</summary>
    internal void Declare(Source source, MemberWriter write) => Assets.Declare(source, write);

    /// <summary>Reports one defect against <paramref name="path"/>.</summary>
    internal void Fail(string path, string message)
    {
        // A reader that already anchored its message to the same path is not anchored twice.
        string prefix = path + ": ";
        Error.WriteLine(prefix + (message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message));
        Failures++;
    }

    internal static bool IsReportable(Exception exception) =>
        exception is FormatException or SceneDocumentFormatException or DecoderFallbackException
            or IOException or UnauthorizedAccessException;
}
