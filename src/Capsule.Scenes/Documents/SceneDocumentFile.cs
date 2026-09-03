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
/// unchanged document reproduces its bytes exactly and a diff shows only real change.
/// </summary>
public static class SceneDocumentFile
{
    private const int FormatVersion = 3;

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

        SceneDocumentEntry[] documentEntries = new SceneDocumentEntry[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            SceneEntryJson entry = Entry(entries, i, out float x, out float y);
            string type = entry.Type ?? string.Empty;

            if (IsTileMap(entry))
            {
                documentEntries[i] = ReadTileMap(entry, x, y);
                continue;
            }

            if (entry.Properties is not null)
            {
                throw new SceneDocumentFormatException(
                    $"entities[{i}] declares properties, but the type '{type}' has no properties contract; only '{SceneDocument.TileMapType}' declares one.");
            }

            documentEntries[i] = new EntityPlacement(entry.Id ?? 0, type, x, y);
        }

        return new SceneDocument(documentEntries, file.NextEntityId, ToSource(file.Source));
    }

    /// <summary>The canonical text of <paramref name="document"/>.</summary>
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

    // The whole file name, extension included: which extensions a textures domain admits is the
    // build's allow-list to hold, so the format asks only that the name it writes reads back.
    private static string? TextureName(TextureHandle? texture)
    {
        if (texture is not { } handle)
        {
            return null;
        }

        return SplitsBackInto(handle)
            ? handle.Name + handle.Extension
            : throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid draws from texture handle (\"{handle.Name}\", \"{handle.Extension}\"), which does not split back out of one file name: a name is non-empty and an extension is a dot followed by at least one character and no second dot, and neither carries a path segment.");
    }

    // The exact inverse of the reader's split on the last dot, which is what makes a written name
    // give its handle back unchanged. A name carrying dots of its own is fine: "x.atlas" and
    // ".png" write "x.atlas.png" and split apart again at the last one.
    private static bool SplitsBackInto(TextureHandle handle) =>
        !string.IsNullOrEmpty(handle.Name)
        && handle.Extension is { Length: > 1 }
        && handle.Extension[0] == '.'
        && handle.Extension.IndexOf('.', 1) < 0
        && !HasPathSegment(handle.Name)
        && !HasPathSegment(handle.Extension);

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

        // Asked of the field's presence rather than its value: read as an absent 0, a columns on a
        // grid with no texture would be accepted and then written back without it, so the document
        // would not survive its own round trip.
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

    // Split on the last dot rather than matched against a known extension: the handle carries
    // whatever the build shipped, and the format only has to name one file in a flat directory.
    private static TextureHandle? ParseTexture(string? texture)
    {
        if (texture is null)
        {
            return null;
        }

        if (!IsOneFileName(texture))
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid has texture \"{texture}\"; a texture is the file name of one asset under assets/textures, extension included — \"tiles.png\" — and that directory is flat, so the name carries no path segments.");
        }

        int dot = texture.LastIndexOf('.');

        return new TextureHandle(texture[..dot], texture[dot..]);
    }

    // One file name with a stem and an extension, and nothing that would reach out of the flat
    // directory the build ships into.
    private static bool IsOneFileName(string name)
    {
        int dot = name.LastIndexOf('.');

        return !string.IsNullOrWhiteSpace(name)
            && dot > 0
            && dot < name.Length - 1
            && !HasPathSegment(name);
    }

    private static bool HasPathSegment(string value) =>
        value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal);

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

        // A face list on a tile that is on no layer describes sides of a tile that never collides,
        // so it would be written back and then ignored.
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
    // the same way whether it arrived from a file or from code.
    private static SceneDocumentSource? ToSource(SceneDocumentSourceJson? source) =>
        source is null
            ? null
            : new SceneDocumentSource(source.Tool ?? string.Empty, source.Path ?? string.Empty, source.Hash ?? string.Empty);
}
