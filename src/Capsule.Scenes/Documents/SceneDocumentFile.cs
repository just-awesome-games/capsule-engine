using System.Globalization;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Collision;
using Capsule.Scenes.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>
/// Reading and writing the scene document format. The written form is canonical — fixed field
/// order, two-space indent, LF, UTF-8 without a BOM, one trailing newline — so re-generating an
/// unchanged document reproduces its bytes exactly.
/// </summary>
public static class SceneDocumentFile
{
    private const int FormatVersion = 4;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Reads and validates the scene document at <paramref name="path"/>.</summary>
    /// <exception cref="SceneDocumentFormatException">The file is malformed; the message is prefixed with the path.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="ArgumentException">The path is empty or malformed.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be opened.</exception>
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
    /// <exception cref="ArgumentNullException">The text is null.</exception>
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
                        $"the '{SceneDocument.TileMapType}' entry declares a scale; terrain is anchored and unscaled, and a tile's size is its grid's tileSize.");
                }

                documentEntries[i] = ReadTileMap(entry, x, y);
                continue;
            }

            if (entry.Properties is not null)
            {
                throw new SceneDocumentFormatException(
                    $"entities[{i}] declares properties, but the type '{type}' has no properties contract; only '{SceneDocument.TileMapType}' declares one.");
            }

            Scale(entry, i, out float scaleX, out float scaleY);
            documentEntries[i] = new EntityPlacement(entry.Id ?? 0, type, x, y, scaleX, scaleY);
        }

        return new SceneDocument(documentEntries, file.NextEntityId, ToSource(file.Source));
    }

    /// <summary>The canonical text of <paramref name="document"/>.</summary>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <exception cref="SceneDocumentFormatException">A grid names a texture that has no written form.</exception>
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

                    // Written only where it says something: identity is what an absent scale means,
                    // so emitting it would put a field in every entry the format already covers.
                    Scale = placed.ScaleX == 1f && placed.ScaleY == 1f
                        ? null
                        : [placed.ScaleX, placed.ScaleY],
                };
            }
            else
            {
                throw new SceneDocumentFormatException($"entry {entry.Id} has no entry type.");
            }
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
    /// <exception cref="ArgumentNullException">The document or the path is null.</exception>
    /// <exception cref="ArgumentException">The path is empty or malformed.</exception>
    /// <exception cref="SceneDocumentFormatException">A grid names a texture that has no written form.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be written to.</exception>
    public static void Save(SceneDocument document, string path) =>
        File.WriteAllText(path, ToJson(document), Utf8NoBom);

    private static bool IsTileMap(SceneEntryJson entry) =>
        string.Equals(entry.Type, SceneDocument.TileMapType, StringComparison.Ordinal);

    // Every entry's common half — the object itself and its position — read before any type reads
    // its own, so a hole here does not surface later as a null reference.
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

    // An absent scale is identity, which is what the writer leaves out. Only the arity is decided
    // here; whether the components are a scale is the document's own invariant.
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
                $"entities[{index}] has a scale of {scale.Length} components; a scale is written [x, y], and an entry at the authored size leaves it out.");
        }

        x = scale[0];
        y = scale[1];
    }

    private static TileMapPlacement ReadTileMap(SceneEntryJson entry, float x, float y)
    {
        // Terrain is drawn in world coordinates, so a position here would be ignored.
        if (x != 0f || y != 0f)
        {
            throw new SceneDocumentFormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"the '{SceneDocument.TileMapType}' entry is at ({x}, {y}); terrain is anchored at the world origin, so its x and y are 0."));
        }

        if (entry.Properties is not { } properties)
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry declares no properties; its grid — tileSize, width, height, tileTypes, tiles, and the texture and columns a drawn grid adds — is written there.");
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

    // The whole path under the textures root, extension included. Which extensions are admitted is
    // the build's allow-list; the format asks only that the path it writes reads back.
    private static string? TextureName(TextureHandle? texture)
    {
        if (texture is not { } handle)
        {
            return null;
        }

        return AssetPaths.Joins(handle.Name, handle.Extension)
            ? handle.Name + handle.Extension
            : throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid draws from texture handle (\"{handle.Name}\", \"{handle.Extension}\"), which does not split back out of one texture path: a name is one or more '/'-joined segments, none of them empty, \".\" or \"..\", and an extension is a dot followed by at least one character and no second dot.");
    }

    // A grid rejects malformed input as an argument fault; read out of a file it is the file that
    // is malformed, so the defect is rethrown under the format's own exception type.
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

        // Asked of presence, not value: read as an absent 0 it would be accepted and then written
        // back without it, so the document would not survive its own round trip.
        if (grid.Columns is not null && grid.Texture is null)
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid declares columns but no texture; columns counts the cells across the texture a grid draws from, and a grid that draws nothing leaves both out.");
        }

        TileDefinition[] tileTypes = new TileDefinition[palette.Length];
        for (int i = 0; i < tileTypes.Length; i++)
        {
            if (palette[i] is not { } tileType)
            {
                throw new SceneDocumentFormatException(
                    $"tileTypes[{i}] is null; every palette entry is an object naming a tile type.");
            }

            if (tileType.Collision.ValueKind != JsonValueKind.Undefined)
            {
                throw new SceneDocumentFormatException(
                    $"tileTypes[{i}] declares collision, which the format no longer has; the layer a tile is on is written as its layer, and which of its sides collide as its collidableFaces.");
            }

            string? layer = ParseLayer(tileType.Layer, i);
            tileTypes[i] = new TileDefinition(
                tileType.Type ?? string.Empty,
                tileType.Cell,
                layer,
                ParseFaces(tileType.CollidableFaces, layer, i));
        }

        try
        {
            return new TileGrid(
                grid.TileSize,
                grid.Width,
                grid.Height,
                tileTypes,
                tiles,
                ParseTexture(grid.Texture),
                grid.Columns ?? 0);
        }
        catch (ArgumentException ex)
        {
            throw new SceneDocumentFormatException(ex.Message, ex);
        }
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
                $"the '{SceneDocument.TileMapType}' entry's grid has texture \"{texture}\"; a texture is one asset's path under assets/textures, extension included — \"tiles.png\" at the root, \"terrain/cave.png\" below it — with forward slashes and no empty, \".\" or \"..\" segment.");
    }

    private static string? ParseLayer(string? layer, int index)
    {
        if (layer is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(layer)
            ? throw new SceneDocumentFormatException(
                $"tileTypes[{index}].layer is blank; a tile that collides names the layer it is on, and one that collides as nothing leaves it out.")
            : layer;
    }

    private static CellFaces2D ParseFaces(string?[]? faces, string? layer, int index)
    {
        if (faces is null)
        {
            return CellFaces2D.All;
        }

        // Faces on a tile that is on no layer would be written back and then ignored.
        if (layer is null)
        {
            throw new SceneDocumentFormatException(
                $"tileTypes[{index}] declares collidableFaces on a tile that collides as nothing; name the layer it is on, or leave the collidableFaces out.");
        }

        CellFaces2D parsed = CellFaces2D.None;
        foreach (string? face in faces)
        {
            parsed |= TileFaceNames.TryParse(face, out CellFaces2D one)
                ? one
                : throw new SceneDocumentFormatException(
                    $"tileTypes[{index}].collidableFaces holds \"{face}\"; every face is one of {string.Join(", ", TileFaceNames.All)}.");
        }

        if (parsed == CellFaces2D.None)
        {
            throw new SceneDocumentFormatException(
                $"tileTypes[{index}].collidableFaces is empty; a tile that collides has at least one face, and one that collides as nothing names no layer.");
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
            throw new SceneDocumentFormatException($"malformed scene document JSON — {ex.Message}", ex);
        }

        return document ?? throw new SceneDocumentFormatException("the scene document file is empty.");
    }

    // Completeness is the SceneDocument constructor's to enforce, so a source block is malformed
    // the same way whether it came from a file or from code.
    private static SceneDocumentSource? ToSource(SceneDocumentSourceJson? source) =>
        source is null
            ? null
            : new SceneDocumentSource(source.Tool ?? string.Empty, source.Path ?? string.Empty, source.Hash ?? string.Empty);
}
