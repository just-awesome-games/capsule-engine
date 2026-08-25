using System.Text;
using System.Text.Json;

namespace Capsule.Levels;

/// <summary>
/// Reading and writing the level format. The written form is canonical — fixed field order,
/// two-space indent, LF, UTF-8 without a BOM, one trailing newline — so re-generating an
/// unchanged level reproduces its bytes exactly and a diff shows only real change.
/// </summary>
public static class LevelFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Reads and validates the level at <paramref name="path"/>.</summary>
    /// <exception cref="LevelFormatException">The file is malformed; the message is prefixed with the path.</exception>
    public static Level Load(string path)
    {
        string json = File.ReadAllText(path);

        try
        {
            return Parse(json);
        }
        catch (LevelFormatException ex)
        {
            throw new LevelFormatException($"{path}: {ex.Message}", ex);
        }
    }

    /// <summary>Reads and validates level JSON that is already in hand.</summary>
    /// <exception cref="LevelFormatException">The JSON is malformed or the level breaks the format.</exception>
    public static Level Parse(string json)
    {
        LevelJson document = Deserialize(json);
        LevelEntity[] entities = new LevelEntity[document.Entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            LevelEntityJson entity = document.Entities[i];
            entities[i] = new LevelEntity(entity.Id ?? 0, entity.Type ?? string.Empty, entity.X, entity.Y);
        }

        return new Level(
            document.TileSize,
            document.Width,
            document.Height,
            document.TileTypes,
            document.Tiles,
            entities,
            document.NextEntityId,
            ToSource(document.Source));
    }

    /// <summary>The canonical text of <paramref name="level"/>.</summary>
    public static string ToJson(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);

        LevelEntityJson[] entities = new LevelEntityJson[level.Entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            LevelEntity entity = level.Entities[i];
            entities[i] = new LevelEntityJson
            {
                Id = entity.Id,
                Type = entity.Type,
                X = entity.X,
                Y = entity.Y,
            };
        }

        LevelJson document = new()
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            TileTypes = [.. level.TileTypes],
            Tiles = [.. level.Tiles],
            Entities = entities,
            NextEntityId = level.NextEntityId,
            Source = level.Source is { } source
                ? new LevelSourceJson { Tool = source.Tool, Path = source.Path, Hash = source.Hash }
                : null,
        };

        string json = JsonSerializer.Serialize(document, LevelJsonContext.Default.LevelJson);

        // The writer's newline is platform-dependent; the format's is not.
        return json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>Writes <paramref name="level"/> to <paramref name="path"/> in canonical form.</summary>
    public static void Save(Level level, string path) => File.WriteAllText(path, ToJson(level), Utf8NoBom);

    /// <summary>
    /// Reads the level at <paramref name="path"/>, giving every entity that lacks an id the next
    /// value from <c>nextEntityId</c> in file order and advancing it. A file with no
    /// <c>nextEntityId</c> starts at 1. For hand-authored levels; an imported one never needs it.
    /// </summary>
    /// <exception cref="LevelFormatException">The file is malformed for a reason ids cannot fix.</exception>
    public static Level ReadAssigningIds(string path, out int assignedCount)
    {
        LevelJson document = Deserialize(File.ReadAllText(path));

        int next = Math.Max(document.NextEntityId, 1);
        assignedCount = 0;
        LevelEntity[] entities = new LevelEntity[document.Entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            LevelEntityJson entity = document.Entities[i];
            int id = entity.Id ?? 0;
            if (id < 1)
            {
                id = next++;
                assignedCount++;
            }

            entities[i] = new LevelEntity(id, entity.Type ?? string.Empty, entity.X, entity.Y);
        }

        try
        {
            return new Level(
                document.TileSize,
                document.Width,
                document.Height,
                document.TileTypes,
                document.Tiles,
                entities,
                next,
                ToSource(document.Source));
        }
        catch (LevelFormatException ex)
        {
            throw new LevelFormatException($"{path}: {ex.Message}", ex);
        }
    }

    private static LevelJson Deserialize(string json)
    {
        LevelJson? document;
        try
        {
            document = JsonSerializer.Deserialize(json, LevelJsonContext.Default.LevelJson);
        }
        catch (JsonException ex)
        {
            throw new LevelFormatException($"malformed level JSON — {ex.Message}", ex);
        }

        return document ?? throw new LevelFormatException("the level file is empty.");
    }

    // Completeness is the Level constructor's to enforce, so a source block is malformed the
    // same way whether it arrived from a file or from code.
    private static LevelSource? ToSource(LevelSourceJson? source) =>
        source is null
            ? null
            : new LevelSource(source.Tool ?? string.Empty, source.Path ?? string.Empty, source.Hash ?? string.Empty);
}
