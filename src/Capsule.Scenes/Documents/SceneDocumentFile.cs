using System.Globalization;
using System.Text;
using System.Text.Json;
using Capsule.Rendering;
using Capsule.Scenes.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>
/// Reading and writing the scene document format. The written form is canonical — fixed field
/// order, two-space indent, LF, UTF-8 without a BOM, one trailing newline — so re-generating an
/// unchanged document reproduces its bytes exactly and a diff shows only real change.
/// </summary>
public static class SceneDocumentFile
{
    private const int FormatVersion = 1;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Reads and validates the scene document at <paramref name="path"/>.</summary>
    /// <exception cref="SceneDocumentFormatException">The file is malformed; the message is prefixed with the path.</exception>
    public static SceneDocument Load(string path)
    {
        string json = File.ReadAllText(path);

        try
        {
            return Parse(json);
        }
        catch (SceneDocumentFormatException ex)
        {
            throw new SceneDocumentFormatException($"{path}: {ex.Message}", ex);
        }
    }

    /// <summary>Reads and validates scene document JSON that is already in hand.</summary>
    /// <exception cref="SceneDocumentFormatException">The JSON is malformed or the document breaks the format.</exception>
    public static SceneDocument Parse(string json)
    {
        SceneDocumentJson file = Deserialize(json);
        if (file.FormatVersion is not { } formatVersion)
        {
            throw new SceneDocumentFormatException(
                $"the scene document has no formatVersion; this build supports formatVersion {FormatVersion}.");
        }

        if (formatVersion != FormatVersion)
        {
            throw new SceneDocumentFormatException(
                $"formatVersion {formatVersion} is unsupported; this build supports formatVersion {FormatVersion}.");
        }

        if (file.Entities is not { } entries)
        {
            throw new SceneDocumentFormatException(
                "the scene document has no entities; a scene with nothing in it is written as an empty list.");
        }

        TileMapPlacement? tileMap = null;
        int first = 0;
        if (entries.Length > 0)
        {
            SceneEntryJson head = Entry(entries, 0, out float headX, out float headY);
            if (IsTileMap(head))
            {
                tileMap = ReadTileMap(head, headX, headY);
                first = 1;
            }
        }

        EntityPlacement[] entities = new EntityPlacement[entries.Length - first];
        for (int i = first; i < entries.Length; i++)
        {
            SceneEntryJson entry = Entry(entries, i, out float x, out float y);
            string type = entry.Type ?? string.Empty;

            if (IsTileMap(entry))
            {
                throw new SceneDocumentFormatException(tileMap is not null
                    ? $"entities[{i}] is a second '{SceneDocument.TileMapType}' entry; a scene carries one terrain."
                    : $"the '{SceneDocument.TileMapType}' entry at entities[{i}] must be the document's first entry: file order is composition order, so terrain composes under everything placed on it.");
            }

            if (entry.Properties is not null)
            {
                throw new SceneDocumentFormatException(
                    $"entities[{i}] declares properties, but the type '{type}' has no properties contract; only '{SceneDocument.TileMapType}' declares one.");
            }

            entities[i - first] = new EntityPlacement(entry.Id ?? 0, type, x, y);
        }

        return new SceneDocument(tileMap, entities, file.NextEntityId, ToSource(file.Source));
    }

    /// <summary>The canonical text of <paramref name="document"/>.</summary>
    public static string ToJson(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        ReadOnlySpan<EntityPlacement> placements = document.Entities;
        bool hasTerrain = document.TileMap is not null;
        SceneEntryJson[] entries = new SceneEntryJson[placements.Length + (hasTerrain ? 1 : 0)];

        if (document.TileMap is { } tileMap)
        {
            // The terrain's origin is written, not left implicit: every entry carries an x and a y.
            entries[0] = new SceneEntryJson
            {
                Id = tileMap.Id,
                Type = SceneDocument.TileMapType,
                X = 0f,
                Y = 0f,
                Properties = JsonSerializer.SerializeToElement(
                    ToJson(tileMap.Grid),
                    SceneDocumentJsonContext.Default.TileGridJson),
            };
        }

        int next = hasTerrain ? 1 : 0;
        foreach (EntityPlacement placed in placements)
        {
            entries[next++] = new SceneEntryJson
            {
                Id = placed.Id,
                Type = placed.Type,
                X = placed.X,
                Y = placed.Y,
            };
        }

        SceneDocumentJson file = new()
        {
            FormatVersion = FormatVersion,
            Entities = entries,
            NextEntityId = document.NextEntityId,
            Source = document.Source is { } source
                ? new SceneDocumentSourceJson { Tool = source.Tool, Path = source.Path, Hash = source.Hash }
                : null,
        };

        string json = JsonSerializer.Serialize(file, SceneDocumentJsonContext.Default.SceneDocumentJson);

        // The writer's newline is platform-dependent; the format's is not.
        return json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>Writes <paramref name="document"/> to <paramref name="path"/> in canonical form.</summary>
    public static void Save(SceneDocument document, string path) =>
        File.WriteAllText(path, ToJson(document), Utf8NoBom);

    private static bool IsTileMap(SceneEntryJson entry) =>
        string.Equals(entry.Type, SceneDocument.TileMapType, StringComparison.Ordinal);

    // Every entry's common half — the object itself and its position — read before any type reads
    // its own. A hole here would otherwise surface as a null reference, or as a position the file
    // never stated.
    private static SceneEntryJson Entry(SceneEntryJson?[] entries, int index, out float x, out float y)
    {
        if (entries[index] is not { } entry)
        {
            throw new SceneDocumentFormatException(
                $"entities[{index}] is null; every entry is an object with an id, a type and a position.");
        }

        if (entry.X is not { } entryX || entry.Y is not { } entryY)
        {
            throw new SceneDocumentFormatException(
                $"entities[{index}] has no {(entry.X is null ? "x" : "y")}; every entry carries an x and a y, and the '{SceneDocument.TileMapType}' entry's are 0.");
        }

        x = entryX;
        y = entryY;

        return entry;
    }

    private static TileMapPlacement ReadTileMap(SceneEntryJson entry, float x, float y)
    {
        // Terrain is drawn in world coordinates whatever its entity's position says, so a
        // position here would be a coordinate the engine then ignores.
        if (x != 0f || y != 0f)
        {
            throw new SceneDocumentFormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"the '{SceneDocument.TileMapType}' entry is at ({x}, {y}); terrain is anchored at the world origin, so its x and y are 0."));
        }

        if (entry.Properties is not { } properties)
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry declares no properties; its grid — tileSize, width, height, tileTypes, tiles — is written there.");
        }

        return new TileMapPlacement(entry.Id ?? 0, Grid(DeserializeGrid(properties)));
    }

    private static TileGridJson DeserializeGrid(JsonElement properties)
    {
        TileGridJson? grid;
        try
        {
            grid = properties.Deserialize(SceneDocumentJsonContext.Default.TileGridJson);
        }
        catch (JsonException ex)
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's properties are not a grid — {ex.Message}", ex);
        }

        return grid ?? throw new SceneDocumentFormatException(
            $"the '{SceneDocument.TileMapType}' entry's properties are empty.");
    }

    private static TileGridJson ToJson(TileGrid grid)
    {
        ReadOnlySpan<TileDefinition> palette = grid.TileTypes;
        TileTypeJson[] tileTypes = new TileTypeJson[palette.Length];
        for (int i = 0; i < tileTypes.Length; i++)
        {
            tileTypes[i] = new TileTypeJson
            {
                Type = palette[i].Type,
                Color = palette[i].Color is { } color ? FormatColor(color) : null,
            };
        }

        return new TileGridJson
        {
            TileSize = grid.TileSize,
            Width = grid.Width,
            Height = grid.Height,
            TileTypes = tileTypes,
            Tiles = [.. grid.Tiles],
        };
    }

    // A grid rejects its own malformed input as an argument fault, which is what a caller building
    // one in code has broken. Read out of a file it is the file that is malformed, so the defect
    // reaches the reader under the one exception type the format throws.
    private static TileGrid Grid(TileGridJson grid)
    {
        if (grid.TileTypes is not { } palette)
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid has no tileTypes; the palette every tile indexes is written there, starting with \"{TileGrid.EmptyTileType}\".");
        }

        if (grid.Tiles is not { } tiles)
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid has no tiles; its width x height palette indices are written there.");
        }

        TileDefinition[] tileTypes = new TileDefinition[palette.Length];
        for (int i = 0; i < tileTypes.Length; i++)
        {
            if (palette[i] is not { } tileType)
            {
                throw new SceneDocumentFormatException(
                    $"tileTypes[{i}] is null; every palette entry is an object naming a tile type.");
            }

            tileTypes[i] = new TileDefinition(
                tileType.Type ?? string.Empty,
                tileType.Color is { } color ? ParseColor(color, i) : null);
        }

        try
        {
            return new TileGrid(grid.TileSize, grid.Width, grid.Height, tileTypes, tiles);
        }
        catch (ArgumentException ex)
        {
            throw new SceneDocumentFormatException(ex.Message, ex);
        }
    }

    // Lowercase is part of the canonical form, so an uppercase spelling is rejected rather than
    // accepted and written back differently: a document must survive its own round trip byte for
    // byte.
    private static ColorRgba ParseColor(string color, int index)
    {
        if (color.Length == 9 && color[0] == '#'
            && TryHexByte(color.AsSpan(1, 2), out byte r)
            && TryHexByte(color.AsSpan(3, 2), out byte g)
            && TryHexByte(color.AsSpan(5, 2), out byte b)
            && TryHexByte(color.AsSpan(7, 2), out byte a))
        {
            return new ColorRgba(r, g, b, a);
        }

        throw new SceneDocumentFormatException(
            $"tileTypes[{index}].color must be lowercase #rrggbbaa, not \"{color}\".");
    }

    private static bool TryHexByte(ReadOnlySpan<char> hex, out byte value)
    {
        value = 0;

        return char.IsAsciiHexDigitLower(hex[0])
            && char.IsAsciiHexDigitLower(hex[1])
            && byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatColor(ColorRgba color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:x2}{color.G:x2}{color.B:x2}{color.A:x2}");

    private static SceneDocumentJson Deserialize(string json)
    {
        SceneDocumentJson? document;
        try
        {
            document = JsonSerializer.Deserialize(json, SceneDocumentJsonContext.Default.SceneDocumentJson);
        }
        catch (JsonException ex)
        {
            throw new SceneDocumentFormatException($"malformed scene document JSON — {ex.Message}", ex);
        }

        return document ?? throw new SceneDocumentFormatException("the scene document file is empty.");
    }

    // Completeness is the SceneDocument constructor's to enforce, so a source block is malformed
    // the same way whether it arrived from a file or from code.
    private static SceneDocumentSource? ToSource(SceneDocumentSourceJson? source) =>
        source is null
            ? null
            : new SceneDocumentSource(source.Tool ?? string.Empty, source.Path ?? string.Empty, source.Hash ?? string.Empty);
}
