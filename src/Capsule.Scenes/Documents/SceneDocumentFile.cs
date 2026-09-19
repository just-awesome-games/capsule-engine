using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>
/// Reads and writes the scene document format. The written form is canonical: fixed field order, a
/// two-space indent, LF line endings, UTF-8 without a BOM, and one trailing newline. Re-generating an
/// unchanged document reproduces its bytes exactly.
/// </summary>
public static class SceneDocumentFile
{
    private const int FormatVersion = 5;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Reads the scene document at <paramref name="path"/>. Performs filesystem I/O.</summary>
    /// <exception cref="SceneDocumentFormatException">The file is malformed. The message is prefixed with the path.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
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

    /// <summary>Reads scene document JSON from a string.</summary>
    /// <exception cref="SceneDocumentFormatException">The JSON is malformed or the document breaks the format.</exception>
    public static SceneDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            return Translate(Deserialize(json));
        }
        catch (ArgumentException ex)
        {
            // The document model reports a defect as a bad argument. Coming from a file, the same
            // defect is a malformed document, so translate it here.
            throw new SceneDocumentFormatException(ex.Message, ex);
        }
    }

    // Turns parsed JSON into the document model. Checks the format's shape. The document model
    // checks its own invariants.
    private static SceneDocument Translate(SceneDocumentJson file)
    {
        if (file.FormatVersion is not { } formatVersion)
        {
            throw new SceneDocumentFormatException(
                $"the scene document has no formatVersion. This build supports formatVersion {FormatVersion}.");
        }

        if (formatVersion != FormatVersion)
        {
            throw new SceneDocumentFormatException(
                $"formatVersion {formatVersion} is unsupported. This build supports formatVersion {FormatVersion}.");
        }

        if (file.Entities is not { } entries)
        {
            throw new SceneDocumentFormatException(
                "the scene document has no entities. Write an empty list for a scene with nothing in it.");
        }

        SceneDocumentEntry[] documentEntries = new SceneDocumentEntry[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            SceneEntryJson entry = Entry(entries, i, out float x, out float y);
            string type = entry.Type ?? string.Empty;

            if (IsTileMap(entry))
            {
                if (entry.HasScale)
                {
                    throw new SceneDocumentFormatException(
                        $"the '{SceneDocument.TileMapType}' entry declares a scale. Terrain is anchored and unscaled. Remove the scale and set the grid's tileSize.");
                }

                documentEntries[i] = ReadTileMap(entry, x, y, i);
                continue;
            }

            if (entry.Properties is not null)
            {
                throw new SceneDocumentFormatException(
                    $"entities[{i}] declares properties, but the type '{type}' has no properties contract. Only '{SceneDocument.TileMapType}' declares one.");
            }

            Scale(entry, i, out float scaleX, out float scaleY);
            documentEntries[i] = new EntityPlacement(
                entry.Id ?? 0, type, x, y, scaleX, scaleY, entry.ZIndex, Pair(entry.ScrollFactor, $"entities[{i}]", "scrollFactor"));
        }

        return new SceneDocument(
            documentEntries,
            file.NextEntityId,
            ToSource(file.Source),
            Pair(file.ScrollOrigin, "the scene document", "scrollOrigin"));
    }

    /// <summary>Serializes <paramref name="document"/> to its canonical text.</summary>
    public static string ToJson(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        ReadOnlySpan<SceneDocumentEntry> placements = document.Entries;
        SceneEntryJson[] entries = new SceneEntryJson[placements.Length];

        for (int i = 0; i < placements.Length; i++)
        {
            SceneDocumentEntry entry = placements[i];
            if (entry.TileMap is { } tileMap)
            {
                entries[i] = new SceneEntryJson
                {
                    Id = tileMap.Id,
                    Type = SceneDocument.TileMapType,
                    X = entry.X,
                    Y = entry.Y,
                    ZIndex = tileMap.ZIndex,
                    ScrollFactor = Pair(tileMap.ScrollFactor),
                    Properties = JsonSerializer.SerializeToElement(
                        ToJson(tileMap.Grid),
                        SceneDocumentJsonContext.Default.TileGridJson),
                };
            }
            else if (entry.Entity is { } placed)
            {
                entries[i] = new SceneEntryJson
                {
                    Id = placed.Id,
                    Type = placed.Type,
                    X = placed.X,
                    Y = placed.Y,

                    // An absent scale means identity, so skip the field when the scale is identity.
                    Scale = placed.ScaleX == 1f && placed.ScaleY == 1f
                        ? null
                        : [placed.ScaleX, placed.ScaleY],
                    ZIndex = placed.ZIndex,
                    ScrollFactor = Pair(placed.ScrollFactor),
                };
            }
        }

        SceneDocumentJson file = new()
        {
            FormatVersion = FormatVersion,
            ScrollOrigin = Pair(document.ScrollOrigin),
            Entities = entries,
            NextEntityId = document.NextEntityId,
            Source = document.Source is { } source
                ? new SceneDocumentSourceJson { Tool = source.Tool, Path = source.Path, Hash = source.Hash }
                : null,
        };

        string json = JsonSerializer.Serialize(file, SceneDocumentJsonContext.Default.SceneDocumentJson);

        // The serializer emits the platform newline, but the format always uses LF.
        return TileRows(json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n", placements);
    }

    /// <summary>Writes <paramref name="document"/> to <paramref name="path"/> in canonical form.</summary>
    /// <remarks>Performs filesystem I/O. Use <see cref="ToJson(SceneDocument)"/> to serialize without writing a file.</remarks>
    /// <exception cref="SceneDocumentFormatException">A grid names a texture that has no written form.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public static void Save(SceneDocument document, string path) =>
        File.WriteAllText(path, ToJson(document), Utf8NoBom);

    // Rewrites each tiles array to one line per grid row. The serializer writes one index per line,
    // which stretches a small map over hundreds of lines and hides its shape from an editor.
    private static string TileRows(string json, ReadOnlySpan<SceneDocumentEntry> placements)
    {
        StringBuilder rewritten = new(json.Length);
        int cursor = 0;

        foreach (SceneDocumentEntry placement in placements)
        {
            if (placement.TileMap is not { } tileMap)
            {
                continue;
            }

            int open = json.IndexOf("\"tiles\": [", cursor, StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            int close = json.IndexOf(']', open);
            int indent = open - (json.LastIndexOf('\n', open) + 1);
            ReadOnlySpan<int> tiles = tileMap.Grid.Tiles;

            rewritten.Append(json, cursor, open - cursor).Append("\"tiles\": [");
            for (int i = 0; i < tiles.Length; i++)
            {
                rewritten
                    .Append(i == 0 ? string.Empty : ",")
                    .Append(i % tileMap.Grid.Width == 0 ? "\n" + new string(' ', indent + 2) : " ")
                    .Append(tiles[i].ToString(CultureInfo.InvariantCulture));
            }

            if (tiles.Length > 0)
            {
                rewritten.Append('\n').Append(' ', indent);
            }

            rewritten.Append(']');
            cursor = close + 1;
        }

        return rewritten.Append(json, cursor, json.Length - cursor).ToString();
    }

    private static bool IsTileMap(SceneEntryJson entry) =>
        string.Equals(entry.Type, SceneDocument.TileMapType, StringComparison.Ordinal);

    // Reads the fields every entry shares, the object and its position, before a type reads its own.
    // A missing field throws here instead of surfacing as a null reference later.
    private static SceneEntryJson Entry(SceneEntryJson?[] entries, int index, out float x, out float y)
    {
        if (entries[index] is not { } entry)
        {
            throw new SceneDocumentFormatException(
                $"entities[{index}] is null. Write an object with an id, a type and a position.");
        }

        if (entry.X is not { } entryX || entry.Y is not { } entryY)
        {
            throw new SceneDocumentFormatException(
                $"entities[{index}] has no {(entry.X is null ? "x" : "y")}. Every entry needs an x and a y, and the '{SceneDocument.TileMapType}' entry uses 0 for both.");
        }

        x = entryX;
        y = entryY;

        return entry;
    }

    // Reads the scale. An absent one is identity, which the writer omits. Checks the
    // component count only. The document model validates the values.
    private static void Scale(SceneEntryJson entry, int index, out float x, out float y)
    {
        if (entry.Scale is not { } scale)
        {
            x = 1f;
            y = 1f;
            return;
        }

        if (scale.Length != 2)
        {
            throw new SceneDocumentFormatException(
                $"entities[{index}] has a scale of {scale.Length} components. Write a scale as [x, y], or omit it for the authored size.");
        }

        x = scale[0];
        y = scale[1];
    }

    // Reads a two-component pair, returning null when the field is absent. Checks the component
    // count only. The document model validates the values.
    private static Vector2? Pair(float[]? pair, string owner, string field)
    {
        if (pair is null)
        {
            return null;
        }

        if (pair.Length != 2)
        {
            throw new SceneDocumentFormatException(
                $"{owner} has a {field} of {pair.Length} components. Write it as [x, y], or omit the field.");
        }

        return new Vector2(pair[0], pair[1]);
    }

    private static float[]? Pair(Vector2? pair) => pair is { } value ? [value.X, value.Y] : null;

    private static TileMapPlacement ReadTileMap(SceneEntryJson entry, float x, float y, int index)
    {
        // Terrain draws in world coordinates, so the runtime would ignore a position here.
        if (x != 0f || y != 0f)
        {
            throw new SceneDocumentFormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"the '{SceneDocument.TileMapType}' entry is at ({x}, {y}). Terrain is anchored at the world origin. Set its x and y to 0."));
        }

        if (entry.Properties is not { } properties)
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry declares no properties. Write its grid there as tileSize, width, height, tileTypes and tiles, plus texture and columns for a drawn grid.");
        }

        return new TileMapPlacement(
            entry.Id ?? 0,
            Grid(DeserializeGrid(properties)),
            entry.ZIndex,
            Pair(entry.ScrollFactor, $"entities[{index}]", "scrollFactor"));
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
                $"the '{SceneDocument.TileMapType}' entry's properties are not a grid: {ex.Message}", ex);
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
                Cell = palette[i].Cell,
                Layer = palette[i].Layer,
                CollidableFaces = palette[i].Layer is null
                    ? null
                    : TileFaceNames.Format(palette[i].CollidableFaces),
            };
        }

        return new TileGridJson
        {
            TileSize = grid.TileSize,
            Width = grid.Width,
            Height = grid.Height,
            Texture = TextureName(grid.Texture),
            Columns = grid.Texture is null ? null : grid.Columns,
            TileTypes = tileTypes,
            Tiles = [.. grid.Tiles],
        };
    }

    // Formats the texture's full path under the textures root, extension included. The build's
    // allow-list decides which extensions are valid. This checks that the path round-trips.
    private static string? TextureName(TextureHandle? texture)
    {
        if (texture is not { } handle)
        {
            return null;
        }

        return AssetPaths.Joins(handle.Name, handle.Extension)
            ? handle.Name + handle.Extension
            : throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid draws from texture handle (\"{handle.Name}\", \"{handle.Extension}\"), which does not split back out of one texture path. Use a name of '/'-joined segments, none of them empty, \".\" or \"..\", and an extension of a dot followed by at least one character and no second dot.");
    }

    // Turns the parsed properties into a grid. The TileGrid constructor checks every grid invariant.
    private static TileGrid Grid(TileGridJson grid)
    {
        if (grid.TileTypes is not { } palette)
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid has no tileTypes. Write the palette every tile indexes there, starting with \"{TileGrid.EmptyTileType}\".");
        }

        if (grid.Tiles is not { } tiles)
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid has no tiles. Write its width x height palette indices there.");
        }

        TileDefinition[] tileTypes = new TileDefinition[palette.Length];
        for (int i = 0; i < tileTypes.Length; i++)
        {
            if (palette[i] is not { } tileType)
            {
                throw new SceneDocumentFormatException(
                    $"tileTypes[{i}] is null. Write an object naming a tile type.");
            }

            if (tileType.Collision.ValueKind != JsonValueKind.Undefined)
            {
                throw new SceneDocumentFormatException(
                    $"tileTypes[{i}] declares collision, which the format no longer supports. Write the tile's layer as layer and its colliding sides as collidableFaces.");
            }

            tileTypes[i] = new TileDefinition(
                tileType.Type ?? string.Empty,
                tileType.Cell,
                tileType.Layer,
                ParseFaces(tileType.CollidableFaces, i));
        }

        return new TileGrid(
            grid.TileSize,
            grid.Width,
            grid.Height,
            tileTypes,
            tiles,
            ParseTexture(grid.Texture),
            grid.Columns ?? 0);
    }

    private static TextureHandle? ParseTexture(string? texture)
    {
        if (texture is null)
        {
            return null;
        }

        return AssetPaths.TrySplit(texture, out string name, out string extension)
            ? new TextureHandle(name, extension)
            : throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid has texture \"{texture}\". Write one asset path under assets/textures, extension included, with forward slashes and no empty, \".\" or \"..\" segment.");
    }

    // Parses the named sides into a face set. An absent list means every face, and a layer with no
    // named faces collides on every side. TileGrid checks whether the set suits the tile.
    private static CellFaces2D ParseFaces(string?[]? faces, int index)
    {
        if (faces is null)
        {
            return CellFaces2D.All;
        }

        CellFaces2D parsed = CellFaces2D.None;
        foreach (string? face in faces)
        {
            parsed |= TileFaceNames.TryParse(face, out CellFaces2D one)
                ? one
                : throw new SceneDocumentFormatException(
                    $"tileTypes[{index}].collidableFaces holds \"{face}\". Use one of {string.Join(", ", TileFaceNames.All)}.");
        }

        return parsed;
    }

    private static SceneDocumentJson Deserialize(string json)
    {
        SceneDocumentJson? document;
        try
        {
            document = JsonSerializer.Deserialize(json, SceneDocumentJsonContext.Default.SceneDocumentJson);
        }
        catch (JsonException ex)
        {
            throw new SceneDocumentFormatException($"malformed scene document JSON: {ex.Message}", ex);
        }

        return document ?? throw new SceneDocumentFormatException("the scene document file is empty.");
    }

    // Fills missing fields with empty strings and lets the SceneDocument constructor check
    // completeness. A source block then fails the same way from a file as from code.
    private static SceneDocumentSource? ToSource(SceneDocumentSourceJson? source) =>
        source is null
            ? null
            : new SceneDocumentSource(source.Tool ?? string.Empty, source.Path ?? string.Empty, source.Hash ?? string.Empty);
}
