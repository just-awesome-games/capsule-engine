using System.Text;
using Capsule.Build.Atlases;
using Capsule.Build.Fonts;
using Capsule.Build.Registry;
using Capsule.Build.Textures;
using Capsule.Scenes.Documents;

namespace Capsule.Build;

/// <summary>
/// What every step of one run reads and writes. The values one step hands the next are properties set
/// once by the step named on each. Reading one before that step ran throws.
/// </summary>
internal sealed class BuildPass(string outputDirectory, CapsuleBuild configuration, TextWriter output, TextWriter error)
{
    private readonly TextWriter _output = output;

    private readonly TextWriter _error = error;

    private BuildRequests? _requests;

    private IReadOnlyList<Source>? _keyed;

    private IReadOnlySet<string>? _fontPages;

    private IReadOnlyDictionary<string, DeclaredAtlas>? _atlases;

    private IReadOnlyDictionary<string, ResolvedTexture>? _textureSettings;

    private TextureMapBuilder? _textureMap;

    /// <summary>Where the run writes everything.</summary>
    internal string OutputDirectory { get; } = outputDirectory;

    /// <summary>What the game's build project configured.</summary>
    internal CapsuleBuild Configuration { get; } = configuration;

    /// <summary>Everything the game ships, laid out under <c>assets/</c>.</summary>
    internal ShippedFiles Shipped { get; } = new(Path.Combine(outputDirectory, "assets"));

    /// <summary>The generated <c>CapsuleAssets</c> class every step declares its members on.</summary>
    internal CapsuleAssetsFile Assets { get; } = new();

    /// <summary>How many defects the run has reported.</summary>
    internal int Failures { get; private set; }

    /// <summary>The manifest, set once it is read.</summary>
    internal BuildRequests Requests
    {
        get => Ran(_requests, nameof(Requests), "reading the manifest");
        set => _requests = value;
    }

    /// <summary>Every source the key pass admitted, in request order. Set by <see cref="Keys.Derive"/>.</summary>
    internal IReadOnlyList<Source> Keyed
    {
        get => Ran(_keyed, nameof(Keyed), nameof(Keys) + "." + nameof(Keys.Derive));
        set => _keyed = value;
    }

    /// <summary>The key of every texture a font names as its page. Set by <see cref="FontStep"/>.</summary>
    internal IReadOnlySet<string> FontPages
    {
        get => Ran(_fontPages, nameof(FontPages), nameof(FontStep));
        set => _fontPages = value;
    }

    /// <summary>Every declared atlas by its name. Set by <see cref="AtlasDeclarationStep"/>.</summary>
    internal IReadOnlyDictionary<string, DeclaredAtlas> Atlases
    {
        get => Ran(_atlases, nameof(Atlases), nameof(AtlasDeclarationStep));
        set => _atlases = value;
    }

    /// <summary>Every texture's resolved settings by its key. Set by <see cref="TextureSettingsStep"/>.</summary>
    internal IReadOnlyDictionary<string, ResolvedTexture> TextureSettings
    {
        get => Ran(_textureSettings, nameof(TextureSettings), nameof(TextureSettingsStep));
        set => _textureSettings = value;
    }

    /// <summary>The texture map the pipeline ships. Set by <see cref="AtlasStep"/>, and added to by <see cref="TextureStep"/>.</summary>
    internal TextureMapBuilder TextureMap
    {
        get => Ran(_textureMap, nameof(TextureMap), nameof(AtlasStep));
        set => _textureMap = value;
    }

    /// <summary>Whether <paramref name="exception"/> is a defect of the input or the disk, reported against its source.</summary>
    /// <remarks>Anything else is a defect of the build itself and stops the process.</remarks>
    internal static bool IsReportable(Exception exception) =>
        exception is FormatException or SceneDocumentFormatException or DecoderFallbackException
            or IOException or UnauthorizedAccessException;

    /// <summary>Every source of <paramref name="type"/>, in request order.</summary>
    internal IEnumerable<Source> Of(AssetType type) => Keyed.Where(source => source.Type == type);

    /// <summary>
    /// Reads every source through <paramref name="read"/>. A source whose read threw or reported a
    /// defect is left out of the result, and the rest are still read.
    /// </summary>
    internal List<(Source Source, T Value)> Each<T>(IEnumerable<Source> sources, Func<Source, T> read)
    {
        List<(Source, T)> results = [];
        foreach (Source source in sources)
        {
            int failures = Failures;
            try
            {
                T value = read(source);
                if (Failures == failures)
                {
                    results.Add((source, value));
                }
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                Fail(source.Path, ex.Message);
            }
        }

        return results;
    }

    /// <summary>Declares <paramref name="source"/> on <c>CapsuleAssets</c> as <paramref name="model"/>, which <paramref name="write"/> writes.</summary>
    internal void Declare<T>(Source source, T model, MemberWriter<T> write) =>
        Assets.Declare(source, (code, indent, identifier) => write(code, indent, identifier, source, model));

    /// <summary>Declares <paramref name="declaration"/> once beside <c>CapsuleAssets</c>, as a type the members use.</summary>
    internal void Beside(string declaration) => Assets.Beside(declaration);

    /// <summary>
    /// The directory a step keeps what it reuses between runs and does not ship, created. The atlas
    /// step keeps a stamp per atlas at <c>atlases/&lt;name&gt;.stamp</c>. The shader step keeps each
    /// composed source at <c>shaders/&lt;key&gt;.fx</c> and its parameter table beside it.
    /// </summary>
    internal string CacheDirectory(string step)
    {
        string directory = Path.Combine(OutputDirectory, step);
        Directory.CreateDirectory(directory);

        return directory;
    }

    /// <summary>Reports one defect against <paramref name="anchor"/>, the file or line it is in.</summary>
    internal void Fail(string anchor, string message)
    {
        // A reader that already anchored its message to the same path is not anchored twice.
        string prefix = anchor + ": ";
        _error.WriteLine(prefix + (message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message));
        Failures++;
    }

    /// <summary>Reports a warning against <paramref name="anchor"/>. A warning fails nothing.</summary>
    internal void Warn(string anchor, string message) => _output.WriteLine($"{anchor}: {message}");

    /// <summary>Reports one line of progress, as <c>audio: Assets/hum.wav -&gt; hum</c>.</summary>
    internal void Progress(string step, string line) => _output.WriteLine($"{step}: {line}");

    /// <summary>Reports that <paramref name="source"/> was built.</summary>
    internal void Progress(string step, Source source) => Progress(step, $"{source.Path} -> {source.Key}");

    private static T Ran<T>(T? value, string property, string setter)
        where T : class =>
        value ?? throw new InvalidOperationException(
            $"{property} is read before {setter} set it. {nameof(BuildRun)}.{nameof(BuildRun.Run)} runs the step that sets a value before every step that reads it.");
}
