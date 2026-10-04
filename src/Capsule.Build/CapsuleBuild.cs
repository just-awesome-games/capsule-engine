using System.Reflection;
using Capsule.Build.Shaders;

namespace Capsule.Build;

/// <summary>A game's build configuration, which its build project's entry point configures and runs.</summary>
/// <remarks>
/// The build project is a console app referencing the <c>JAG.Capsule.Build</c> package, and the logic project
/// names it in <c>CapsuleBuildProject</c>. Capsule's build targets run it on every build, and its
/// arguments go to <see cref="Configure"/> unchanged. The run reuses whatever it derived before from
/// sources that have not changed since.
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
        "usage: <build project> --assets <dir> --out <dir> [--shipping] --shader-tools <dxc> <spirv-cross>. Capsule's build targets run the build project with these.";

    private readonly string[] _args;

    private readonly List<(string Extension, IAssetImporter Importer)> _claims = [];

    internal int? TileSize { get; private set; }

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

    /// <summary>Runs the build and returns the process's exit code: 0 on success, 1 when a source has a defect, 2 on a usage error.</summary>
    /// <remarks>Each defect is one line on the error stream, naming the file that has it.</remarks>
    public int Run() => Run(Console.Out, Console.Error);

    internal int Run(TextWriter output, TextWriter error)
    {
        // The options come in the order Usage names them.
        if (_args is not ["--assets", string assets, "--out", string outputDirectory, .. ([] or ["--shipping"]) and var flags, "--shader-tools", string dxc, string spirvCross])
        {
            error.WriteLine(Usage);

            return 2;
        }

        return AssetPipeline.Run(assets, new ShaderTools(dxc, spirvCross), shipping: flags is ["--shipping"], outputDirectory, this, output, error);
    }

    // The importer claiming the path by its extension, or null when none does.
    internal IAssetImporter? ImporterOf(string path) =>
        _claims.Find(claim => path.Length > claim.Extension.Length && path.EndsWith(claim.Extension, StringComparison.OrdinalIgnoreCase)).Importer;

    // Two extensions overlap when a file named for one also ends in the other, as ".json" and ".scene.json".
    private static bool Overlaps(string first, string second) =>
        first.EndsWith(second, StringComparison.OrdinalIgnoreCase) || second.EndsWith(first, StringComparison.OrdinalIgnoreCase);
}
