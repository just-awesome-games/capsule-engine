using System.Text;
using Capsule.Maps.Cli.Tiled;

namespace Capsule.Maps.Cli;

/// <summary>
/// The verbs, separated from argument parsing so they are driveable from a test. Each returns a
/// process exit code: 0 on success, 1 on any failure, with every failure written to <c>error</c>.
/// </summary>
public static class MapTool
{
    private const string MapExtension = ".map.json";

    /// <summary>
    /// As <see cref="ImportTiled"/>, reading the Tiled maps one per line from
    /// <paramref name="listPath"/>. This is the form a build drives: a few hundred source paths
    /// overflow a command line, and the failure when they do names nothing useful.
    /// </summary>
    public static int ImportTiledFromList(
        string outputDirectory,
        string listPath,
        int? tileSize,
        TextWriter output,
        TextWriter error,
        string? dependencyRoot = null) =>
        ImportFromList(Tiled(tileSize, dependencyRoot), outputDirectory, listPath, output, error);

    /// <summary>
    /// Generates one map per Tiled map into <paramref name="outputDirectory"/>, creating it if
    /// absent. A map is named after its source, so two sources sharing a file name is an error
    /// rather than a silent overwrite. Every source is attempted: one broken source reports and
    /// does not hide the rest.
    /// </summary>
    /// <param name="tileSize">
    /// The tile size the game declares, which every map must match; null declares nothing.
    /// </param>
    public static int ImportTiled(
        string outputDirectory,
        IReadOnlyList<string> sourcePaths,
        int? tileSize,
        TextWriter output,
        TextWriter error,
        string? dependencyRoot = null) =>
        Import(Tiled(tileSize, dependencyRoot), outputDirectory, sourcePaths, output, error);

    /// <summary>
    /// As <see cref="ImportNative"/>, reading the maps one per line from
    /// <paramref name="listPath"/>. This is the form a build drives.
    /// </summary>
    public static int ImportNativeFromList(
        string outputDirectory,
        string listPath,
        int? tileSize,
        TextWriter output,
        TextWriter error) =>
        ImportFromList(Native(tileSize), outputDirectory, listPath, output, error);

    /// <summary>
    /// Re-emits every hand-authored map into <paramref name="outputDirectory"/> in canonical form,
    /// validated and stamped with its source path. Otherwise as <see cref="ImportTiled"/>.
    /// </summary>
    /// <param name="tileSize">
    /// The tile size the game declares, which every map must match; null declares nothing.
    /// </param>
    public static int ImportNative(
        string outputDirectory,
        IReadOnlyList<string> sourcePaths,
        int? tileSize,
        TextWriter output,
        TextWriter error) =>
        Import(Native(tileSize), outputDirectory, sourcePaths, output, error);

    private static Verb Tiled(int? tileSize, string? dependencyRoot) =>
        new(
            "import-tiled",
            static path => Path.GetFileNameWithoutExtension(path),
            path => TiledImporter.Import(path, tileSize, dependencyRoot));

    private static Verb Native(int? tileSize) =>
        new("import-native", NativeStem, path => NativeMapImporter.Import(path, tileSize));

    // Not GetFileNameWithoutExtension: the format's extension is two of them, and stripping one
    // would leave a map named 'room.map' where the Tiled path names the same map 'room' — the
    // duplicate-name check across the two source kinds only means anything if the stems agree.
    private static string NativeStem(string sourcePath)
    {
        string name = Path.GetFileName(sourcePath);

        return name.EndsWith(MapExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^MapExtension.Length]
            : Path.GetFileNameWithoutExtension(name);
    }

    private static int ImportFromList(
        Verb verb,
        string outputDirectory,
        string listPath,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        string[] sourcePaths;
        try
        {
            sourcePaths = [.. File.ReadAllLines(listPath).Where(line => !string.IsNullOrWhiteSpace(line))];
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{verb.Name}: cannot read the map list '{listPath}' — {ex.Message}");
            return 1;
        }

        return Import(verb, outputDirectory, sourcePaths, output, error);
    }

    private static int Import(
        Verb verb,
        string outputDirectory,
        IReadOnlyList<string> sourcePaths,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{verb.Name}: cannot create '{outputDirectory}' — {ex.Message}");
            return 1;
        }

        Dictionary<string, string> claimedBy = new(StringComparer.OrdinalIgnoreCase);
        int failures = 0;
        foreach (string sourcePath in sourcePaths)
        {
            string mapPath = Path.Combine(outputDirectory, verb.Stem(sourcePath) + MapExtension);

            if (!claimedBy.TryAdd(mapPath, sourcePath))
            {
                error.WriteLine(
                    $"{sourcePath}: would overwrite the map of '{claimedBy[mapPath]}'; a map is named after its source, so source names must be unique.");
                failures++;
                continue;
            }

            try
            {
                MapFile.Save(verb.Import(sourcePath), mapPath);
                output.WriteLine($"{verb.Name}: {sourcePath} -> {mapPath}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{sourcePath}: {ex.Message}");
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"{verb.Name}: {failures} of {sourcePaths.Count} source(s) failed");
            return 1;
        }

        return 0;
    }

    private static bool IsReportable(Exception exception) =>
        exception is MapFormatException or TiledImportException or IOException or UnauthorizedAccessException
            or DecoderFallbackException;

    // One authoring format as the batch loop sees it: what to call it, how a source names its
    // map, and how a source becomes one.
    private sealed record Verb(string Name, Func<string, string> Stem, Func<string, Map> Import);
}
