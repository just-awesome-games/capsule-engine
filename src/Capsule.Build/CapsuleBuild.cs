using System.Reflection;
using Capsule.Build.Scenes;
using Capsule.Build.Shaders;

namespace Capsule.Build;

/// <summary>A game's build configuration, which its build project's entry point configures and runs.</summary>
/// <remarks>
/// The build project is a console app referencing the <c>JAG.Capsule.Build</c> package, and the logic project
/// names it in <c>CapsuleBuildProject</c>. Capsule's build targets run it before every compile, and again after
/// a compile that changed the logic assembly, and its arguments go to <see cref="Configure"/> unchanged. The run
/// reuses whatever it derived before from sources that have not changed since.
/// </remarks>
/// <example>
/// <code>
/// return CapsuleBuild.Configure(args)
///     .AddImporter(new TiledImporter())
///     .WithTileSize(16)
///     .Run();
/// </code>
/// </example>
public sealed class CapsuleBuild
{
    private const string Usage =
        "usage: <build project> --assets <dir> --out <dir> [--shipping] [--tile-maps <logic assembly>] --shader-tools <dir> [--shader-format <name>=<tool>]... Capsule's build targets run the build project with these.";

    private readonly string[] _args;

    private readonly List<(string Extension, IAssetImporter Importer)> _claims = [];

    internal int? TileSize { get; private set; }

    // The tile-map keys a run after a compile read from the logic assembly, or null to build with the kept ones.
    internal string[]? TileMaps { get; private set; }

    internal string SourceLanguage { get; private set; } = "en";

    // Null falls back to the source language.
    internal string? FallbackLanguage { get; private set; }

    // What identifies a derivation's implementing assembly in place of its build, which a test injects.
    // Null reads the build.
    internal Func<Assembly, string>? IdentifyTool { get; set; }

    // How many derivations run at once. A test forces 1 to compare against the default.
    internal int Parallelism { get; set; } = Environment.ProcessorCount;

    private CapsuleBuild(string[] args) => _args = args;

    /// <summary>Starts a build over the command line Capsule's build targets passed to the build project.</summary>
    public static CapsuleBuild Configure(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new CapsuleBuild([.. args]);
    }

    /// <summary>Imports every source whose extension <paramref name="importer"/> claims, in place of reading it as an asset.</summary>
    /// <exception cref="ArgumentException">
    /// The importer claims no extension, an extension a Capsule asset type admits, or an extension an
    /// added importer claims.
    /// </exception>
    public CapsuleBuild AddImporter(IAssetImporter importer)
    {
        ArgumentNullException.ThrowIfNull(importer);
        string name = importer.GetType().Name;
        if (importer.Extensions is not { Count: > 0 } extensions)
        {
            throw new ArgumentException($"The importer {name} claims no extension. Return the extensions of the sources it reads, as \".tmj\".", nameof(importer));
        }

        foreach (string extension in extensions)
        {
            if (extension is not ['.', _, ..])
            {
                throw new ArgumentException($"The importer {name} claims \"{extension}\", which is no extension. Start each extension with '.', as \".tmj\".", nameof(importer));
            }

            if (AssetType.AdmittedExtensions.FirstOrDefault(admitted => Overlaps(admitted, extension)) is { } admittedBy)
            {
                throw new ArgumentException($"The importer {name} claims \"{extension}\", which overlaps \"{admittedBy}\", an extension Capsule reads itself. Claim an extension of the editor's own format.", nameof(importer));
            }

            if (_claims.Find(claim => Overlaps(claim.Extension, extension)) is { Importer: { } claimant } claimed)
            {
                throw new ArgumentException($"The importer {name} claims \"{extension}\", which overlaps \"{claimed.Extension}\" that {claimant.GetType().Name} claims. Add one importer per source extension.", nameof(importer));
            }
        }

        _claims.AddRange(extensions.Select(extension => (extension, importer)));

        return this;
    }

    /// <summary>Declares the game's tile size in pixels, which every importer reads as <see cref="AssetImportContext.TileSize"/>.</summary>
    /// <remarks>A game with no one tile size leaves it unset, and each tile map declares its own.</remarks>
    public CapsuleBuild WithTileSize(int pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixels);
        TileSize = pixels;

        return this;
    }

    /// <summary>Declares the language the game's texts are authored in, as a BCP 47 tag, defaulting to <c>en</c>.</summary>
    /// <remarks>
    /// Its <c>&lt;tag&gt;.strings.json</c> files define every key <c>CapsuleStrings</c> declares, and every other
    /// language is checked against them.
    /// </remarks>
    public CapsuleBuild WithSourceLanguage(string tag)
    {
        SourceLanguage = Canonical(tag);

        return this;
    }

    /// <summary>Declares the language a run shows when no shipped language matches its request, as a BCP 47 tag, defaulting to the source language.</summary>
    public CapsuleBuild WithFallbackLanguage(string tag)
    {
        FallbackLanguage = Canonical(tag);

        return this;
    }

    /// <summary>Runs the build and returns the process's exit code: 0 on success, 1 when a source has a defect, 2 on a usage error.</summary>
    /// <remarks>Each defect is one line on the error stream, naming the file that has it.</remarks>
    public int Run() => Run(Console.Out, Console.Error);

    internal int Run(TextWriter output, TextWriter error)
    {
        // The options come in the order Usage names them.
        if (_args is not ["--assets", string assets, "--out", string outputDirectory, .. var options])
        {
            error.WriteLine(Usage);

            return 2;
        }

        bool shipping = options is ["--shipping", ..];
        options = options[(shipping ? 1 : 0)..];
        string? tileMaps = options is ["--tile-maps", string assembly, ..] ? Path.GetFullPath(assembly) : null;
        options = options[(tileMaps is null ? 0 : 2)..];
        if (options is not ["--shader-tools", string shaderTools, .. var formats] || ShaderFormats(formats) is not { } further)
        {
            error.WriteLine(Usage);

            return 2;
        }

        // The run after a compile builds only when the new logic assembly changed which entry types are tile maps.
        if (tileMaps is not null)
        {
            TileMaps = TileMapKeys.Changed(outputDirectory, tileMaps, error, out int exitCode);
            if (TileMaps is null)
            {
                return exitCode;
            }
        }

        return AssetPipeline.Run(assets, new ShaderTools(shaderTools, further), shipping, outputDirectory, this, output, error);
    }

    // The further formats a platform module names, each as a --shader-format name=tool pair, or null
    // for anything else. A name is lowercase letters and digits, and none of the engine's own.
    private static List<ShaderFormatTool>? ShaderFormats(ReadOnlySpan<string> options)
    {
        List<ShaderFormatTool> formats = [];
        for (; options is ["--shader-format", string pair, ..]; options = options[2..])
        {
            int split = pair.IndexOf('=', StringComparison.Ordinal);
            string format = split > 0 ? pair[..split] : string.Empty;
            if (split <= 0 || split == pair.Length - 1 || !format.All(static c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
                || ShaderCompiler.Formats.Contains(format) || formats.Exists(known => known.Format == format))
            {
                return null;
            }

            formats.Add(new ShaderFormatTool(format, Path.GetFullPath(pair[(split + 1)..])));
        }

        return options.IsEmpty ? formats : null;
    }

    // The importer claiming the path by its extension, or null when none does.
    internal IAssetImporter? ImporterOf(string path) =>
        _claims.Find(claim => path.Length > claim.Extension.Length && path.EndsWith(claim.Extension, StringComparison.OrdinalIgnoreCase)).Importer;

    // A language tag spelled as a string table's file name must spell it.
    private static string Canonical(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        return string.Equals(tag, Strings.StringFile.CanonicalTag(tag), StringComparison.Ordinal)
            ? tag
            : throw new ArgumentException(
                Strings.StringFile.CanonicalTag(tag) is { Length: > 0 } canonical
                    ? $"\"{tag}\" is spelled \"{canonical}\" as a BCP 47 tag. Pass \"{canonical}\"."
                    : $"\"{tag}\" is no language tag. Pass a BCP 47 tag, as \"de\" or \"pt-BR\".",
                nameof(tag));
    }

    // Two extensions overlap when a file named for one also ends in the other, as ".json" and ".scene.json".
    private static bool Overlaps(string first, string second) =>
        first.EndsWith(second, StringComparison.OrdinalIgnoreCase) || second.EndsWith(first, StringComparison.OrdinalIgnoreCase);
}
