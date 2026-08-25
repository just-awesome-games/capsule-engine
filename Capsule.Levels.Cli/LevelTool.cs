using System.Text;
using Capsule.Levels.Cli.Tiled;

namespace Capsule.Levels.Cli;

/// <summary>
/// The verb, separated from argument parsing so it is driveable from a test. Returns a process
/// exit code: 0 on success, 1 on any failure, with every failure written to <c>error</c>.
/// </summary>
public static class LevelTool
{
    /// <summary>
    /// As <see cref="ImportTiled"/>, reading the maps one per line from
    /// <paramref name="listPath"/>. This is the form a build drives: a few hundred map paths
    /// overflow a command line, and the failure when they do names nothing useful.
    /// </summary>
    public static int ImportTiledFromList(
        string outputDirectory,
        string listPath,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        string[] mapPaths;
        try
        {
            mapPaths = [.. File.ReadAllLines(listPath).Where(line => !string.IsNullOrWhiteSpace(line))];
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"import-tiled: cannot read the map list '{listPath}' — {ex.Message}");
            return 1;
        }

        return ImportTiled(outputDirectory, mapPaths, output, error);
    }

    /// <summary>
    /// Generates one level per map into <paramref name="outputDirectory"/>, creating it if
    /// absent. A level is named after its map, so two maps sharing a file name is an error
    /// rather than a silent overwrite. Every map is attempted: one broken map reports and does
    /// not hide the rest.
    /// </summary>
    public static int ImportTiled(
        string outputDirectory,
        IReadOnlyList<string> mapPaths,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(mapPaths);
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
        foreach (string mapPath in mapPaths)
        {
            string levelPath = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(mapPath) + ".level.json");

            if (!claimedBy.TryAdd(levelPath, mapPath))
            {
                error.WriteLine(
                    $"{mapPath}: would overwrite the level of '{claimedBy[levelPath]}'; a level is named after its map, so map names must be unique.");
                failures++;
                continue;
            }

            try
            {
                LevelFile.Save(TiledImporter.Import(mapPath), levelPath);
                output.WriteLine($"import-tiled: {mapPath} -> {levelPath}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{mapPath}: {ex.Message}");
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"import-tiled: {failures} of {mapPaths.Count} map(s) failed");
            return 1;
        }

        return 0;
    }

    private static bool IsReportable(Exception exception) =>
        exception is LevelFormatException or TiledImportException or IOException or UnauthorizedAccessException
            or DecoderFallbackException;
}
