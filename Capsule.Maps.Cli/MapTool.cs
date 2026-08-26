using System.Text;
using Capsule.Maps.Cli.Tiled;

namespace Capsule.Maps.Cli;

/// <summary>
/// The verb, separated from argument parsing so it is driveable from a test. Returns a process
/// exit code: 0 on success, 1 on any failure, with every failure written to <c>error</c>.
/// </summary>
public static class MapTool
{
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
        string? dependencyRoot = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        string[] sourcePaths;
        try
        {
            sourcePaths = [.. File.ReadAllLines(listPath).Where(line => !string.IsNullOrWhiteSpace(line))];
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"import-tiled: cannot read the Tiled map list '{listPath}' — {ex.Message}");
            return 1;
        }

        return ImportTiled(outputDirectory, sourcePaths, tileSize, output, error, dependencyRoot);
    }

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
        string? dependencyRoot = null)
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
            error.WriteLine($"import-tiled: cannot create '{outputDirectory}' — {ex.Message}");
            return 1;
        }

        Dictionary<string, string> claimedBy = new(StringComparer.OrdinalIgnoreCase);
        int failures = 0;
        foreach (string sourcePath in sourcePaths)
        {
            string mapPath = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(sourcePath) + ".map.json");

            if (!claimedBy.TryAdd(mapPath, sourcePath))
            {
                error.WriteLine(
                    $"{sourcePath}: would overwrite the map of '{claimedBy[mapPath]}'; a map is named after its source, so source names must be unique.");
                failures++;
                continue;
            }

            try
            {
                MapFile.Save(TiledImporter.Import(sourcePath, tileSize, dependencyRoot), mapPath);
                output.WriteLine($"import-tiled: {sourcePath} -> {mapPath}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{sourcePath}: {ex.Message}");
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"import-tiled: {failures} of {sourcePaths.Count} Tiled map(s) failed");
            return 1;
        }

        return 0;
    }

    private static bool IsReportable(Exception exception) =>
        exception is MapFormatException or TiledImportException or IOException or UnauthorizedAccessException
            or DecoderFallbackException;
}
