using System.Globalization;
using System.Text;
using System.Text.Json;
using Capsule.Rendering;

namespace Capsule.Maps;

/// <summary>
/// Reading and writing the map format. The written form is canonical — fixed field order,
/// two-space indent, LF, UTF-8 without a BOM, one trailing newline — so re-generating an
/// unchanged map reproduces its bytes exactly and a diff shows only real change.
/// </summary>
public static class MapFile
{
    private const int FormatVersion = 1;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Reads and validates the map at <paramref name="path"/>.</summary>
    /// <exception cref="MapFormatException">The file is malformed; the message is prefixed with the path.</exception>
    public static Map Load(string path)
    {
        string json = File.ReadAllText(path);

        try
        {
            return Parse(json);
        }
        catch (MapFormatException ex)
        {
            throw new MapFormatException($"{path}: {ex.Message}", ex);
        }
    }

    /// <summary>Reads and validates map JSON that is already in hand.</summary>
    /// <exception cref="MapFormatException">The JSON is malformed or the map breaks the format.</exception>
    public static Map Parse(string json)
    {
        MapJson document = Deserialize(json);
        if (document.FormatVersion is not { } formatVersion)
        {
            throw new MapFormatException(
                $"the map has no formatVersion; this build supports formatVersion {FormatVersion}.");
        }

        if (formatVersion != FormatVersion)
        {
            throw new MapFormatException(
                $"formatVersion {formatVersion} is unsupported; this build supports formatVersion {FormatVersion}.");
        }

        TileGridJson grid = document.Grid ?? throw new MapFormatException("the map has no grid.");

        MapObject[] objects = new MapObject[document.Objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            MapObjectJson placed = document.Objects[i];
            objects[i] = new MapObject(placed.Id ?? 0, placed.Type ?? string.Empty, placed.X, placed.Y);
        }

        TileDefinition[] tileTypes = new TileDefinition[grid.TileTypes.Length];
        for (int i = 0; i < tileTypes.Length; i++)
        {
            TileTypeJson tileType = grid.TileTypes[i];
            tileTypes[i] = new TileDefinition(
                tileType.Type ?? string.Empty,
                tileType.Color is { } color ? ParseColor(color, i) : null);
        }

        return new Map(
            new TileGrid(grid.TileSize, grid.Width, grid.Height, tileTypes, grid.Tiles),
            objects,
            document.NextObjectId,
            ToSource(document.Source));
    }

    /// <summary>The canonical text of <paramref name="map"/>.</summary>
    public static string ToJson(Map map)
    {
        ArgumentNullException.ThrowIfNull(map);

        MapObjectJson[] objects = new MapObjectJson[map.Objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            MapObject placed = map.Objects[i];
            objects[i] = new MapObjectJson
            {
                Id = placed.Id,
                Type = placed.Type,
                X = placed.X,
                Y = placed.Y,
            };
        }

        ReadOnlySpan<TileDefinition> palette = map.Grid.TileTypes;
        TileTypeJson[] tileTypes = new TileTypeJson[palette.Length];
        for (int i = 0; i < tileTypes.Length; i++)
        {
            tileTypes[i] = new TileTypeJson
            {
                Type = palette[i].Type,
                Color = palette[i].Color is { } color ? FormatColor(color) : null,
            };
        }

        MapJson document = new()
        {
            FormatVersion = FormatVersion,
            Grid = new TileGridJson
            {
                TileSize = map.Grid.TileSize,
                Width = map.Grid.Width,
                Height = map.Grid.Height,
                TileTypes = tileTypes,
                Tiles = [.. map.Grid.Tiles],
            },
            Objects = objects,
            NextObjectId = map.NextObjectId,
            Source = map.Source is { } source
                ? new MapSourceJson { Tool = source.Tool, Path = source.Path, Hash = source.Hash }
                : null,
        };

        string json = JsonSerializer.Serialize(document, MapJsonContext.Default.MapJson);

        // The writer's newline is platform-dependent; the format's is not.
        return json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>Writes <paramref name="map"/> to <paramref name="path"/> in canonical form.</summary>
    public static void Save(Map map, string path) => File.WriteAllText(path, ToJson(map), Utf8NoBom);

    // Lowercase is part of the canonical form, so an uppercase spelling is rejected rather than
    // accepted and written back differently: a map must survive its own round trip byte for byte.
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

        throw new MapFormatException(
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

    private static MapJson Deserialize(string json)
    {
        MapJson? document;
        try
        {
            document = JsonSerializer.Deserialize(json, MapJsonContext.Default.MapJson);
        }
        catch (JsonException ex)
        {
            throw new MapFormatException($"malformed map JSON — {ex.Message}", ex);
        }

        return document ?? throw new MapFormatException("the map file is empty.");
    }

    // Completeness is the Map constructor's to enforce, so a source block is malformed the same
    // way whether it arrived from a file or from code.
    private static MapSource? ToSource(MapSourceJson? source) =>
        source is null
            ? null
            : new MapSource(source.Tool ?? string.Empty, source.Path ?? string.Empty, source.Hash ?? string.Empty);
}
