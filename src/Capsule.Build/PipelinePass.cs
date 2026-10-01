using System.Text;
using System.Text.Json.Serialization.Metadata;
using Capsule.Build.Atlases;
using Capsule.Build.Caching;
using Capsule.Build.Fonts;
using Capsule.Build.Registry;
using Capsule.Build.Textures;
using Capsule.Scenes.Documents;

namespace Capsule.Build;

/// <summary>
/// What every step of one run reads and writes. The values one step hands the next are properties set
/// once by the step named on each. Reading one before that step ran throws.
/// </summary>
internal sealed class PipelinePass(string outputDirectory, CapsuleBuild configuration, TextWriter output, TextWriter error)
{
    // What the item an Each runs on this thread has reported, replayed in item order once every item
    // has run. Inside an item, Fail, Warn and Progress write here and Failures counts only that item's
    // defects. Null outside one, where each line reaches the run's streams at once.
    [ThreadStatic]
    private static Report? Buffering;

    private readonly TextWriter _output = output;

    private readonly TextWriter _error = error;

    private BuildRequests? _requests;

    private IReadOnlyList<Source>? _keyed;

    private IReadOnlyDictionary<string, Source>? _textures;

    private IReadOnlySet<string>? _fontPages;

    private IReadOnlyDictionary<string, DeclaredAtlas>? _atlases;

    private IReadOnlyDictionary<string, ResolvedTexture>? _textureSettings;

    private TextureMapBuilder? _textureMap;

    private DerivationCache? _cache;

    private int _failures;

    /// <summary>Where the run writes everything.</summary>
    internal string OutputDirectory { get; } = outputDirectory;

    /// <summary>What the game's build project configured.</summary>
    internal CapsuleBuild Configuration { get; } = configuration;

    /// <summary>Everything the game ships, laid out under <c>assets/</c>.</summary>
    internal OutputFiles Shipped { get; } = new(Path.Combine(outputDirectory, "assets"));

    /// <summary>Every file the importers wrote, laid out under <c>imported/</c> as though authored below the asset root.</summary>
    internal OutputFiles Imported { get; } = new(Path.Combine(outputDirectory, "imported"));

    /// <summary>The generated <c>CapsuleAssets</c> class every step declares its members on.</summary>
    internal CapsuleAssetsFile Assets { get; } = new();

    /// <summary>How many defects the run has reported, or inside one item of an Each, how many that item has.</summary>
    internal int Failures => Buffering?.Failures ?? _failures;

    /// <summary>Every file the run builds and the options it runs with, set once the tree is walked.</summary>
    internal BuildRequests Requests
    {
        get => Ran(_requests, nameof(Requests), nameof(BuildRequests) + "." + nameof(BuildRequests.Walk));
        set => _requests = value;
    }

    /// <summary>What the last run derived, which every cached step reuses from. Set once the tree is walked.</summary>
    internal DerivationCache Cache
    {
        get => Ran(_cache, nameof(Cache), nameof(DerivationCache) + "." + nameof(DerivationCache.Read));
        set => _cache = value;
    }

    /// <summary>Every source the key pass admitted, in request order. Set by <see cref="Keys.Derive"/>.</summary>
    internal IReadOnlyList<Source> Keyed
    {
        get => Ran(_keyed, nameof(Keyed), nameof(Keys) + "." + nameof(Keys.Derive));
        set => _keyed = value;
    }

    /// <summary>Every texture source by its key. Set by <see cref="Keys.Derive"/>.</summary>
    internal IReadOnlyDictionary<string, Source> Textures
    {
        get => Ran(_textures, nameof(Textures), nameof(Keys) + "." + nameof(Keys.Derive));
        set => _textures = value;
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
    /// Reads every source through <paramref name="read"/>, uncached. A source whose read threw or
    /// reported a defect is left out of the result, and the rest are still read.
    /// </summary>
    /// <remarks>Sources are read concurrently. Results and reports come in source order.</remarks>
    internal List<(Source Source, T Value)> Each<T>(IEnumerable<Source> sources, Func<Source, T> read)
    {
        Source[] all = [.. sources];
        (T Value, Report Report)[] done = Buffered(all, source =>
        {
            try
            {
                return read(source);
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                Fail(source.Path, ex.Message);

                return default!;
            }
        });

        List<(Source, T)> results = [];
        for (int i = 0; i < all.Length; i++)
        {
            Flush(done[i].Report);
            if (done[i].Report.Failures == 0)
            {
                results.Add((all[i], done[i].Value));
            }
        }

        return results;
    }

    /// <summary>
    /// Derives every item through <paramref name="derive"/>, or reuses the facts and written files the
    /// cache recorded for it. An item whose derivation threw or reported a defect is left out of the
    /// result and out of the cache, and the rest are still derived.
    /// </summary>
    /// <remarks>
    /// Items are derived concurrently. Every item that writes or keeps a file another item also writes
    /// fails, whether reused or derived. Results, cache entries and reports all come in item order.
    /// </remarks>
    /// <param name="step">The step's name, which starts each progress line and groups its cache entries, as <c>textures</c>.</param>
    /// <param name="describe">What the item's derivation reads and applies, which the cache stamps.</param>
    /// <param name="derive">Writes the item's files and returns the facts later work reads in place of its sources.</param>
    /// <param name="facts">How the facts are kept in the cache, or null when later work reads nothing of them.</param>
    /// <param name="written">Where the derivations write, or null for <see cref="Shipped"/>.</param>
    internal List<(T Item, TFacts Facts)> Each<T, TFacts>(
        string step,
        IEnumerable<T> items,
        Func<T, Derivation> describe,
        Func<T, DerivedFiles, TFacts> derive,
        JsonTypeInfo<TFacts>? facts,
        OutputFiles? written = null)
    {
        written ??= Shipped;
        T[] all = [.. items];
        (Derived<TFacts> Derived, Report Report)[] done = Buffered(all, item =>
        {
            Derivation derivation = describe(item);
            DerivedFiles? files = null;
            try
            {
                if (Cache.Reusable(step, derivation, written, facts, out TFacts kept) is { } entry)
                {
                    foreach (string output in entry.Outputs.Keys)
                    {
                        written.Claim(output, derivation.Name);
                    }

                    return new Derived<TFacts>(derivation, kept, entry, Files: null);
                }

                files = new DerivedFiles(written, derivation.Name);
                TFacts value = derive(item, files);
                if (Failures == 0)
                {
                    return new Derived<TFacts>(derivation, value, Cache.Entry(step, derivation, files, value, facts), files);
                }
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                Fail(derivation.Name, ex.Message);
            }

            return new Derived<TFacts>(derivation, default!, Entry: null, files);
        });

        List<(T, TFacts)> results = [];
        int reused = 0;
        int built = 0;
        for (int i = 0; i < all.Length; i++)
        {
            ((Derivation derivation, TFacts value, DerivationJson? entry, DerivedFiles? files), Report report) = done[i];
            Flush(report);
            IEnumerable<string> outputs = files?.Written.Select(static file => file.Path) ?? entry?.Outputs.Keys.AsEnumerable() ?? [];
            if (Collision(written, outputs) is { } collision)
            {
                Fail(derivation.Name, collision);
                continue;
            }

            if (entry is null)
            {
                continue;
            }

            Cache.Complete(step, derivation.Name, entry);
            results.Add((all[i], value));
            if (files is null)
            {
                reused++;
            }
            else
            {
                built++;
                Progress(step, "built " + derivation.Name);
            }
        }

        if (all.Length > 0)
        {
            int failed = all.Length - reused - built;
            Progress(step, $"{reused} reused, {built} built" + (failed > 0 ? $", {failed} failed" : string.Empty));
        }

        return results;
    }

    // The defect of writing a path another writer also writes, or null. Writers are named in ordinal
    // order, which no degree changes. The file there is deleted.
    private static string? Collision(OutputFiles written, IEnumerable<string> outputs)
    {
        foreach (string output in outputs)
        {
            if (written.Owners(output) is { Length: > 1 } owners)
            {
                // Which writer's bytes are there depends on the degree.
                File.Delete(Path.Combine(written.Root, output));
                return $"\"{Path.GetFileName(written.Root)}/{output}\" is written by {string.Join(" and ", owners.Order(StringComparer.Ordinal).Select(static owner => $"'{owner}'"))}. Rename all but one.";
            }
        }

        return null;
    }

    /// <summary>Derives every item as the overload handing on facts does, for a step whose later work reads nothing of the result.</summary>
    internal List<T> Each<T>(string step, IEnumerable<T> items, Func<T, Derivation> describe, Action<T, DerivedFiles> derive) =>
        [
            .. Each<T, bool>(
                step,
                items,
                describe,
                (item, files) =>
                {
                    derive(item, files);

                    return true;
                },
                facts: null)
                .Select(static derived => derived.Item),
        ];

    /// <summary>Declares <paramref name="source"/> on <c>CapsuleAssets</c> as <paramref name="model"/>, which <paramref name="write"/> writes.</summary>
    internal void Declare<T>(Source source, T model, MemberWriter<T> write) =>
        Assets.Declare(source, (code, indent, identifier) => write(code, indent, identifier, source, model));

    /// <summary>Reports one defect against <paramref name="anchor"/>, the file or line it is in.</summary>
    internal void Fail(string anchor, string message)
    {
        // A reader that already anchored its message to the same path is not anchored twice.
        string prefix = anchor + ": ";
        Emit(error: true, prefix + (message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message));
    }

    /// <summary>Reports a warning against <paramref name="anchor"/>. A warning fails nothing.</summary>
    internal void Warn(string anchor, string message) => Emit(error: false, $"{anchor}: {message}");

    /// <summary>Reports one line of progress, as <c>audio: built Assets/hum.wav</c>.</summary>
    internal void Progress(string step, string line) => Emit(error: false, $"{step}: {line}");

    // Runs work on every item at up to the configured degree, each reporting into its own buffer.
    private (TResult Result, Report Report)[] Buffered<TItem, TResult>(TItem[] items, Func<TItem, TResult> work)
    {
        (TResult, Report)[] done = new (TResult, Report)[items.Length];
        Concurrently.For(items.Length, Configuration.Parallelism, i =>
        {
            Report? outer = Buffering;
            Report report = new();
            Buffering = report;
            try
            {
                done[i] = (work(items[i]), report);
            }
            finally
            {
                Buffering = outer;
            }
        });

        return done;
    }

    // Replays what one item reported, into the report around it or onto the run's streams.
    private void Flush(Report report)
    {
        foreach ((bool error, string line) in report.Lines)
        {
            Emit(error, line);
        }
    }

    private void Emit(bool error, string line)
    {
        if (Buffering is { } report)
        {
            report.Lines.Add((error, line));
            report.Failures += error ? 1 : 0;

            return;
        }

        (error ? _error : _output).WriteLine(line);
        _failures += error ? 1 : 0;
    }

    private static T Ran<T>(T? value, string property, string setter)
        where T : class =>
        value ?? throw new InvalidOperationException(
            $"{property} is read before {setter} set it. {nameof(AssetPipeline)}.{nameof(AssetPipeline.Run)} runs the step that sets a value before every step that reads it.");

    // One derivation's outcome: its entry when it succeeded, and the files it wrote when it ran.
    private sealed record Derived<TFacts>(Derivation Derivation, TFacts Value, DerivationJson? Entry, DerivedFiles? Files);

    private sealed class Report
    {
        internal List<(bool Error, string Line)> Lines { get; } = [];

        internal int Failures { get; set; }
    }
}
