using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>Reads and writes the scene document format.</summary>
/// <remarks>
/// The written form is canonical: fixed field order, a two-space indent, LF line endings, UTF-8
/// without a BOM, and one trailing newline. Re-generating an unchanged document reproduces its
/// bytes exactly.
/// </remarks>
public static class SceneDocumentFile
{
    internal const int FormatVersion = 8;

    internal const string LinearSampling = "linear";

    internal const string PointSampling = "point";

    private static readonly SearchValues<char> NumberText = SearchValues.Create("0123456789+-.eE,");

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
            if (string.Equals(entry.Type, SceneDocument.TileMapType, StringComparison.Ordinal))
            {
                if (entry.HasScale)
                {
                    throw new SceneDocumentFormatException(
                        $"the '{SceneDocument.TileMapType}' entry declares a scale. Terrain is anchored and unscaled. Remove the scale and set the grid's tileSize.");
                }

                if (entry.HasRotation)
                {
                    throw new SceneDocumentFormatException(
                        $"the '{SceneDocument.TileMapType}' entry declares a rotation. Terrain is anchored and unturned. Remove the rotation, and turn single tiles with the grid's transforms.");
                }

                documentEntries[i] = ReadTileMap(entry, x, y, i);
                continue;
            }

            Vector2 scale = Pair(entry.Scale, $"entities[{i}]", "scale") ?? Vector2.One;
            documentEntries[i] = new EntityPlacement(
                entry.Id ?? 0,
                entry.Type ?? string.Empty,
                x,
                y,
                scale.X,
                scale.Y,
                entry.ZIndex,
                Pair(entry.ScrollFactor, $"entities[{i}]", "scrollFactor"),
                entry.Rotation ?? 0f,
                entry.Properties);
        }

        return new SceneDocument(
            documentEntries,
            file.NextEntityId,
            ToSource(file.Source),
            new SceneSettings
            {
                BaseScene = file.BaseScene,
                Camera = file.Camera,
                Size = Pair(file.Size, "the scene document", "size"),
                ScrollCenter = Pair(file.ScrollCenter, "the scene document", "scrollCenter"),
                ClearColor = ParseColor(file.ClearColor, "clearColor"),
                Ambient = ParseColor(file.Ambient, "ambient"),
                Sampling = ParseSampling(file.Sampling),
                Properties = file.Properties,
            });
    }

    /// <summary>Serializes <paramref name="document"/> to its canonical text.</summary>
    /// <exception cref="SceneDocumentFormatException">A grid names a texture that has no written form.</exception>
    public static string ToJson(SceneDocument document) => ToJson(document, compact: false);

    // The build ships the compact form. Only the runtime reads it, and whitespace is most of an
    // indented document's size.
    internal static string ToJson(SceneDocument document, bool compact)
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

                    // An absent rotation is unturned and an absent scale is identity, so skip either at its default.
                    Rotation = placed.RotationDegrees == 0f ? null : placed.RotationDegrees,
                    Scale = placed.ScaleX == 1f && placed.ScaleY == 1f
                        ? null
                        : [placed.ScaleX, placed.ScaleY],
                    ZIndex = placed.ZIndex,
                    ScrollFactor = Pair(placed.ScrollFactor),
                    Properties = placed.Properties,
                };
            }
        }

        SceneSettings settings = document.Settings;
        SceneDocumentJson file = new()
        {
            FormatVersion = FormatVersion,
            BaseScene = settings.BaseScene,
            Camera = settings.Camera,
            Size = Pair(settings.Size),
            ScrollCenter = Pair(settings.ScrollCenter),
            ClearColor = FormatColor(settings.ClearColor),
            Ambient = FormatColor(settings.Ambient),
            Sampling = FormatSampling(settings.Sampling),
            Properties = settings.Properties,
            Entities = entries,
            NextEntityId = document.NextEntityId,
            Source = document.Source is { } source
                ? new SceneDocumentSourceJson { Tool = source.Tool, Path = source.Path, Hash = source.Hash }
                : null,
        };

        string json = JsonSerializer.Serialize(file, SceneDocumentJsonContext.Default.SceneDocumentJson);

        return compact ? json : Canonical(json);
    }

    /// <summary>Writes <paramref name="document"/> to <paramref name="path"/> in canonical form.</summary>
    /// <remarks>Performs filesystem I/O. Use <see cref="ToJson(SceneDocument)"/> to serialize without writing a file.</remarks>
    /// <exception cref="SceneDocumentFormatException">A grid names a texture that has no written form.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public static void Save(SceneDocument document, string path) =>
        File.WriteAllText(path, ToJson(document));

    // Lays the compact form out as the canonical text: one member or element per line at a two-space
    // indent. An array of numbers is written on one line, as "size": [96, 80]. A tile map's tiles and
    // transforms are written one grid row per line, and each palette shape on one line.
    private static string Canonical(string compact)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(compact);
        Utf8JsonReader reader = new(utf8);
        StringBuilder text = new(utf8.Length * 2);
        string? key = null;
        bool named = false, opened = false, entities = false, tileMap = false;
        int width = 0;

        while (reader.Read())
        {
            int depth = reader.CurrentDepth;
            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                (opened ? text : Line(text, depth)).Append(reader.TokenType == JsonTokenType.EndObject ? '}' : ']');
                opened = false;
                entities &= depth != 1;
                continue;
            }

            if (!named && depth > 0)
            {
                Line(opened ? text : text.Append(','), depth);
            }

            named = opened = false;
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    text.Append('{');
                    opened = true;
                    break;
                case JsonTokenType.StartArray:
                    // Depth 3 inside the entities array holds an entry's fields, depth 4 a tile map's grid
                    // fields and depth 6 its palette entries' fields.
                    Utf8JsonReader end = reader;
                    end.Skip();
                    string array = Encoding.UTF8.GetString(utf8, (int)reader.TokenStartIndex, (int)(end.TokenStartIndex - reader.TokenStartIndex) + 1);
                    if (tileMap && depth == 4 && key is "tiles" or "transforms")
                    {
                        GridRows(text, array, width, depth);
                    }
                    else if ((tileMap && depth == 6 && key == "shape") || (array.Length > 2 && array.AsSpan(1, array.Length - 2).IndexOfAnyExcept(NumberText) < 0))
                    {
                        text.Append(array.Replace(",", ", ", StringComparison.Ordinal));
                    }
                    else
                    {
                        text.Append('[');
                        opened = true;
                        entities |= depth == 1 && key == "entities";
                        break;
                    }

                    reader = end;
                    break;
                case JsonTokenType.PropertyName:
                    key = reader.GetString();
                    text.Append(Encoding.UTF8.GetString(utf8, (int)reader.TokenStartIndex, reader.ValueSpan.Length + 2)).Append(": ");
                    named = true;
                    break;
                default:
                    int length = reader.TokenType == JsonTokenType.String ? reader.ValueSpan.Length + 2 : reader.ValueSpan.Length;
                    text.Append(Encoding.UTF8.GetString(utf8, (int)reader.TokenStartIndex, length));
                    tileMap = entities && depth == 3 && key == "type" ? reader.ValueTextEquals(SceneDocument.TileMapType) : tileMap;
                    width = tileMap && depth == 4 && key == "width" ? reader.GetInt32() : width;
                    break;
            }
        }

        return text.Append('\n').ToString();
    }

    private static StringBuilder Line(StringBuilder text, int depth) => text.Append('\n').Append(' ', depth * 2);

    // Writes a grid-shaped array of numbers one row of width per line.
    private static void GridRows(StringBuilder text, string array, int width, int depth)
    {
        string[] values = array[1..^1].Split(',');
        text.Append('[');
        for (int i = 0; i < values.Length; i++)
        {
            (i % width == 0 ? Line(i == 0 ? text : text.Append(','), depth + 1) : text.Append(", ")).Append(values[i]);
        }

        Line(text, depth).Append(']');
    }

    // The grid's transforms as the document writes them, or null when every tile is drawn as authored.
    private static int[]? Transformed(TileGrid grid) =>
        grid.Transforms.ContainsAnyExcept(TileTransform.None) ? [.. grid.Transforms.ToArray().Select(static transform => (int)transform)] : null;

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

    // Reads a colour, returning null when the field is absent. SceneDocument refuses a translucent one,
    // and the writer emits lowercase "#rrggbb".
    private static ColorRgba? ParseColor(string? color, string field)
    {
        if (color is null)
        {
            return null;
        }

        try
        {
            return ColorRgba.FromHex(color);
        }
        catch (FormatException ex)
        {
            throw new SceneDocumentFormatException(
                $"{field} is \"{color}\", which is not a colour. Write it as \"#rrggbb\", or as \"#rrggbbaa\" with an ff alpha.",
                ex);
        }
    }

    private static string? FormatColor(ColorRgba? color) =>
        color is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"#{value.R:x2}{value.G:x2}{value.B:x2}")
            : null;

    private static TextureSampling? ParseSampling(string? sampling) => sampling switch
    {
        null => null,
        LinearSampling => TextureSampling.Linear,
        PointSampling => TextureSampling.Point,
        _ => throw new SceneDocumentFormatException(
            $"sampling is \"{sampling}\". Write \"{LinearSampling}\" or \"{PointSampling}\", or omit it for the game's setting."),
    };

    private static string? FormatSampling(TextureSampling? sampling) => sampling switch
    {
        null => null,
        TextureSampling.Point => PointSampling,
        _ => LinearSampling,
    };

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
        ReadOnlySpan<TileType> palette = grid.TileTypes;
        TileTypeJson[] tileTypes = new TileTypeJson[palette.Length];
        for (int i = 0; i < tileTypes.Length; i++)
        {
            AuthoredTileType authored = grid.Authored?[i] ?? default;
            tileTypes[i] = new TileTypeJson
            {
                Name = palette[i].Name,
                Type = authored.Type,
                Cell = palette[i].Cell,
                Frames = FormatFrames(palette[i].Frames),
                Layer = palette[i].Layer,
                Shape = FormatShape(palette[i].Shape),
                OneWay = palette[i].OneWay ? true : null,
                SolidSides = palette[i].SolidSides ? true : null,
                Properties = authored.Properties,
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
            Transforms = Transformed(grid),
        };
    }

    // Formats the texture's key, extension included. The build's
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
                $"the '{SceneDocument.TileMapType}' entry's grid has no tileTypes. Write the palette every tile indexes there, starting with \"{TileGrid.EmptyTileName}\".");
        }

        if (grid.Tiles is not { } tiles)
        {
            throw new SceneDocumentFormatException(
                $"the '{SceneDocument.TileMapType}' entry's grid has no tiles. Write its width x height palette indices there.");
        }

        TileType[] tileTypes = new TileType[palette.Length];
        AuthoredTileType[]? authored = null;
        for (int i = 0; i < tileTypes.Length; i++)
        {
            if (palette[i] is not { } tileType)
            {
                throw new SceneDocumentFormatException(
                    $"tileTypes[{i}] is null. Write an object naming a tile type.");
            }

            // Always a plain TileType here. The class an entry's type names is built when the scene is composed.
            tileTypes[i] = new TileType
            {
                Name = tileType.Name ?? string.Empty,
                Cell = tileType.Cell,
                Frames = ParseFrames(tileType.Frames, i),
                Layer = tileType.Layer,
                Shape = ParseShape(tileType.Shape, i),
                OneWay = tileType.OneWay ?? false,
                SolidSides = tileType.SolidSides ?? false,
            };

            if (tileType.Type is not null || tileType.Properties is not null)
            {
                authored ??= new AuthoredTileType[palette.Length];
                authored[i] = new AuthoredTileType(tileType.Type, TileProperties(tileType.Properties, i));
            }
        }

        return new TileGrid(
            grid.TileSize,
            grid.Width,
            grid.Height,
            tileTypes,
            tiles,
            ParseTexture(grid.Texture),
            grid.Columns ?? 0,
            ParseTransforms(grid.Transforms),
            authored);
    }

    // Checks a palette entry's properties as SceneDocument checks an entity's. The composing class reads the keys.
    private static JsonElement? TileProperties(JsonElement? properties, int index)
    {
        if (properties is { ValueKind: not JsonValueKind.Object })
        {
            throw new SceneDocumentFormatException(
                $"tileTypes[{index}] has properties that are not an object. Write them as {{ \"name\": value }}, or omit them.");
        }

        if (properties is { } members && !members.EnumerateObject().All(static member => SceneDocument.Finite(member.Value)))
        {
            throw new SceneDocumentFormatException(
                $"tileTypes[{index}] has a property number beyond the range of a double. Write a finite number.");
        }

        return properties;
    }

    // Absent means every tile is drawn as authored. TileGrid checks the count against the grid.
    private static TileTransform[]? ParseTransforms(int[]? transforms)
    {
        if (transforms is null)
        {
            return null;
        }

        TileTransform[] parsed = new TileTransform[transforms.Length];
        for (int i = 0; i < parsed.Length; i++)
        {
            if (transforms[i] is < 0 or >= TileTransforms.Count)
            {
                throw new SceneDocumentFormatException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"transforms[{i}] is {transforms[i]}. Use 0 for a tile as authored, or add 1 to mirror it left to right, 2 to mirror it top to bottom and 4 to swap its axes, up to {TileTransforms.Count - 1}."));
            }

            parsed[i] = (TileTransform)transforms[i];
        }

        return parsed;
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
                $"the '{SceneDocument.TileMapType}' entry's grid has texture \"{texture}\". Write the texture's path under Assets/, extension included, with forward slashes and no empty, \".\" or \"..\" segment.");
    }

    // Reads a tile's shape as a convex polygon of [x, y] points. TileGrid checks that it fits its tile.
    private static Shape2D? ParseShape(float[]?[]? points, int index)
    {
        if (points is null)
        {
            return null;
        }

        if (points.Length is < 3 or > Shape2D.MaxPoints)
        {
            throw new SceneDocumentFormatException(
                $"tileTypes[{index}].shape has {points.Length} points. Write 3 or {Shape2D.MaxPoints} [x, y] points.");
        }

        Span<Vector2> corners = stackalloc Vector2[points.Length];
        for (int corner = 0; corner < points.Length; corner++)
        {
            if (points[corner] is not { Length: 2 } point)
            {
                throw new SceneDocumentFormatException(
                    $"tileTypes[{index}].shape[{corner}] is not a point. Write each point as [x, y].");
            }

            corners[corner] = new Vector2(point[0], point[1]);
        }

        try
        {
            return Shape2D.Polygon(corners);
        }
        catch (ArgumentException ex)
        {
            throw new SceneDocumentFormatException($"tileTypes[{index}].shape is not a convex polygon. {ex.Message}", ex);
        }
    }

    // TileGrid checks each frame's cell and ticks. This checks only what the JSON shape can leave out.
    private static TileFrame[]? ParseFrames(TileFrameJson?[]? frames, int index)
    {
        if (frames is null)
        {
            return null;
        }

        TileFrame[] parsed = new TileFrame[frames.Length];
        for (int frame = 0; frame < parsed.Length; frame++)
        {
            if (frames[frame] is not { Cell: { } cell, Ticks: { } ticks })
            {
                throw new SceneDocumentFormatException(
                    $"tileTypes[{index}].frames[{frame}] is not a frame. Write each frame as {{ \"cell\": 0, \"ticks\": 8 }}.");
            }

            parsed[frame] = new TileFrame(cell, ticks);
        }

        return parsed;
    }

    private static TileFrameJson[]? FormatFrames(IReadOnlyList<TileFrame>? frames) =>
        frames is null ? null : [.. frames.Select(static frame => new TileFrameJson { Cell = frame.Cell, Ticks = frame.Ticks })];

    private static float[][]? FormatShape(Shape2D? shape) =>
        shape is { } polygon
            ? [.. Enumerable.Range(0, polygon.PointCount).Select(corner => (float[])[polygon.Point(corner).X, polygon.Point(corner).Y])]
            : null;

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
