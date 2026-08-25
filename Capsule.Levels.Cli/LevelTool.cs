using System.Text;
using Capsule.Levels.Cli.Tiled;

namespace Capsule.Levels.Cli;

/// <summary>
/// The verbs, separated from argument parsing so they are driveable from a test. Each returns
/// a process exit code: 0 on success, 1 on any failure, with every failure written to
/// <c>error</c>.
/// </summary>
public static class LevelTool
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Generates the level at <paramref name="levelPath"/> from the Tiled map at <paramref name="mapPath"/>.</summary>
    public static int ImportTiled(string mapPath, string levelPath, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            LevelFile.Save(TiledImporter.Import(mapPath, levelPath), levelPath);
            output.WriteLine($"import-tiled: wrote {levelPath}");
            return 0;
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"import-tiled: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Numbers every entity in <paramref name="levelPath"/> that lacks an id and rewrites the
    /// file canonically. A generated level never needs this; a hand-authored one does.
    /// </summary>
    public static int AssignIds(string levelPath, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            Level level = LevelFile.ReadAssigningIds(levelPath, out int assigned);
            LevelFile.Save(level, levelPath);
            output.WriteLine($"assign-ids: {assigned} id(s) assigned in {levelPath}");
            return 0;
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"assign-ids: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The commit gate. Validates each level, and where one carries a source block re-runs the
    /// import in memory and byte-compares it, so a hand-edit to a generated file fails here
    /// rather than surviving into the repository.
    /// </summary>
    public static int Validate(IReadOnlyList<string> levelPaths, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(levelPaths);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        int failures = 0;
        foreach (string levelPath in levelPaths)
        {
            if (!ValidateOne(levelPath, error))
            {
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"validate: {failures} of {levelPaths.Count} level(s) failed");
            return 1;
        }

        output.WriteLine($"validate: {levelPaths.Count} level(s) ok");
        return 0;
    }

    private static bool ValidateOne(string levelPath, TextWriter error)
    {
        Level level;
        string text;
        try
        {
            ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
            byte[] bytes = File.ReadAllBytes(levelPath);
            if (bytes.AsSpan().StartsWith(bom))
            {
                error.WriteLine($"{levelPath}: starts with a UTF-8 BOM; the canonical form has none.");
                return false;
            }

            // The same text is both parsed and compared, so the gate can never pass a file
            // whose bytes differ from what was validated.
            text = StrictUtf8.GetString(bytes);
            level = LevelFile.Parse(text);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{levelPath}: {ex.Message}");
            return false;
        }

        if (level.Source is not { } source)
        {
            return true;
        }

        if (!string.Equals(source.Tool, TiledImporter.ToolName, StringComparison.Ordinal))
        {
            error.WriteLine($"{levelPath}: source.tool is '{source.Tool}', which no importer handles.");
            return false;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(levelPath)) ?? Directory.GetCurrentDirectory();
        string sourcePath = Path.GetFullPath(Path.Combine(directory, source.Path));
        if (!File.Exists(sourcePath))
        {
            error.WriteLine($"{levelPath}: its source '{source.Path}' is missing (expected at '{sourcePath}').");
            return false;
        }

        string regenerated;
        try
        {
            regenerated = LevelFile.ToJson(TiledImporter.Import(sourcePath, levelPath));
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{levelPath}: re-importing its source failed — {ex.Message}");
            return false;
        }

        if (!string.Equals(regenerated, text, StringComparison.Ordinal))
        {
            error.WriteLine(
                $"{levelPath}: does not match its source. It is generated — edit '{sourcePath}', then re-run: "
                + $"Capsule.Levels.Cli import-tiled \"{sourcePath}\" \"{levelPath}\"");
            return false;
        }

        return true;
    }

    private static bool IsReportable(Exception exception) =>
        exception is LevelFormatException or TiledImportException or IOException or UnauthorizedAccessException
            or DecoderFallbackException;
}
