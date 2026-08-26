using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Capsule.Rendering;

namespace Capsule.Maps.Cli.Tiled;

/// <summary>
/// Turns a Tiled map into a Capsule map. A pure function of the source and its tilesets: the
/// same inputs always produce the same map, which is what lets a build skip an unchanged source
/// and a golden fixture pin the output byte for byte.
/// </summary>
public static class TiledImporter
{
    /// <summary>The value stamped into an imported map's <c>source.tool</c>.</summary>
    public const string ToolName = "tiled";

    /// <summary>The tileset tile property carrying optional colour presentation.</summary>
    public const string ColorProperty = "color";

    // Tiled's name for the Color property type; it equals ColorProperty only by coincidence.
    private const string ColorPropertyType = "color";

    // Tiled packs flip and rotation into the top nibble of a gid.
    private const uint OrientationFlags = 0xF000_0000u;

    /// <summary>
    /// Imports the Tiled map at <paramref name="mapPath"/>. The path is stamped into the map's
    /// source block exactly as given, separators normalised — so it must be relative, and it
    /// means what it means from the working directory this ran in.
    /// </summary>
    /// <param name="tileSize">
    /// The tile size the game declares, which every map it imports must match. Null declares
    /// nothing, and each map keeps its own.
    /// </param>
    /// <param name="dependencyRoot">
    /// When supplied, external tilesets must resolve beneath this tracked source directory.
    /// </param>
    /// <exception cref="TiledImportException">The source uses something Capsule does not import.</exception>
    public static Map Import(string mapPath, int? tileSize = null, string? dependencyRoot = null)
    {
        byte[] mapBytes = File.ReadAllBytes(mapPath);
        TiledMap map = Deserialize(mapBytes, mapPath, TiledJsonContext.Default.TiledMap);

        RequireSupportedMap(map, mapPath, tileSize);

        string mapDirectory = DirectoryOf(mapPath);
        string? resolvedDependencyRoot = dependencyRoot is null ? null : Path.GetFullPath(dependencyRoot);
        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sourceHash.AppendData(mapBytes);
        TiledTileset[] tilesets = LoadTilesets(map, mapDirectory, resolvedDependencyRoot, sourceHash);

        List<TileDefinition> palette = [TileGrid.EmptyTile];
        Dictionary<int, int> paletteIndexByGid = BuildPalette(tilesets, palette);

        (TiledLayer tileLayer, List<TiledLayer> objectLayers) = SplitLayers(map);
        int[] tiles = ReadTiles(tileLayer, map, tilesets, paletteIndexByGid);
        List<MapObject> objects = ReadObjects(objectLayers);

        MapSource source = new(
            ToolName,
            mapPath.Replace('\\', '/'),
            Convert.ToHexStringLower(sourceHash.GetHashAndReset()));

        try
        {
            return new Map(
                new TileGrid(map.TileWidth, map.Width, map.Height, palette, tiles),
                objects,
                map.NextObjectId,
                source);
        }
        catch (MapFormatException ex)
        {
            throw new TiledImportException($"'{mapPath}' imports to an invalid map: {ex.Message}", ex);
        }
    }

    private static void RequireSupportedMap(TiledMap map, string mapPath, int? tileSize)
    {
        if (!string.Equals(map.Orientation, "orthogonal", StringComparison.Ordinal))
        {
            throw new TiledImportException(
                $"'{mapPath}' is a '{map.Orientation}' map; Capsule imports orthogonal maps only.");
        }

        if (map.Infinite)
        {
            throw new TiledImportException(
                $"'{mapPath}' is an infinite map; turn off Infinite in Map > Map Properties.");
        }

        if (map.TileWidth != map.TileHeight)
        {
            throw new TiledImportException(
                $"'{mapPath}' has {map.TileWidth}x{map.TileHeight} tiles; Capsule imports square tiles only.");
        }

        if (tileSize is { } declared && map.TileWidth != declared)
        {
            throw new TiledImportException(
                $"'{mapPath}' has {map.TileWidth}px tiles but the game declares {declared}px; set Map > Map Properties > Tile Width and Tile Height to {declared}, or change CapsuleTileSize.");
        }

        // Widened before anything is sized off it: an int product wraps, and the wrapped value
        // would size the tile array rather than fail here.
        long area = (long)map.Width * map.Height;
        if (map.Width <= 0 || map.Height <= 0 || area > Array.MaxLength)
        {
            throw new TiledImportException(
                $"'{mapPath}' is {map.Width}x{map.Height}, which is not a grid Capsule can hold.");
        }
    }

    private static TiledTileset[] LoadTilesets(
        TiledMap map,
        string mapDirectory,
        string? dependencyRoot,
        IncrementalHash sourceHash)
    {
        List<TiledTileset> resolved = [];
        foreach (TiledTileset entry in map.Tilesets)
        {
            if (string.IsNullOrEmpty(entry.Source))
            {
                resolved.Add(entry);
                continue;
            }

            string extension = Path.GetExtension(entry.Source);
            if (extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tmx", StringComparison.OrdinalIgnoreCase))
            {
                throw new TiledImportException(
                    $"tileset '{entry.Source}' is XML; Capsule reads JSON tilesets only — re-save it from Tiled as .tsj.");
            }

            string path = Path.GetFullPath(Path.Combine(mapDirectory, entry.Source));
            if (dependencyRoot is not null && !IsWithin(path, dependencyRoot))
            {
                throw new TiledImportException(
                    $"tileset '{entry.Source}' resolves outside the tracked asset source root '{dependencyRoot}'; move it under that root so the build can track it.");
            }

            if (!File.Exists(path))
            {
                throw new TiledImportException($"tileset '{entry.Source}' is missing (expected at '{path}').");
            }

            byte[] tilesetBytes = File.ReadAllBytes(path);
            AppendLengthPrefixed(sourceHash, tilesetBytes);
            TiledTileset tileset = Deserialize(tilesetBytes, path, TiledJsonContext.Default.TiledTileset);
            tileset.FirstGid = entry.FirstGid;
            tileset.Name ??= Path.GetFileNameWithoutExtension(entry.Source);
            resolved.Add(tileset);
        }

        return [.. resolved.OrderBy(tileset => tileset.FirstGid)];
    }

    private static bool IsWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void AppendLengthPrefixed(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    // Every Class in the tilesets enters the palette, painted or not, in tileset then tile-id
    // order: painting a new type must not renumber the types already in a committed map.
    private static Dictionary<int, int> BuildPalette(TiledTileset[] tilesets, List<TileDefinition> palette)
    {
        Dictionary<int, int> paletteIndexByGid = [];
        Dictionary<string, string> tilesetByClass = new(StringComparer.Ordinal);

        foreach (TiledTileset tileset in tilesets)
        {
            string tilesetName = tileset.Name ?? "?";
            foreach (TiledTile tile in (tileset.Tiles ?? []).OrderBy(tile => tile.Id))
            {
                string? tileClass = tile.ResolvedClass;
                if (string.IsNullOrWhiteSpace(tileClass))
                {
                    continue;
                }

                if (string.Equals(tileClass, TileGrid.EmptyTileType, StringComparison.Ordinal))
                {
                    throw new TiledImportException(
                        $"tileset '{tilesetName}' tile {tile.Id} has Class '{TileGrid.EmptyTileType}', which is reserved for the absence of a tile; rename it.");
                }

                if (!tilesetByClass.TryAdd(tileClass, tilesetName))
                {
                    throw new TiledImportException(
                        $"Class '{tileClass}' is defined by more than one tile (tilesets '{tilesetByClass[tileClass]}' and '{tilesetName}'); a Class must name exactly one tile.");
                }

                paletteIndexByGid[tileset.FirstGid + tile.Id] = palette.Count;
                palette.Add(new TileDefinition(tileClass, ColorOf(tile, tileClass, tilesetName)));
            }
        }

        return paletteIndexByGid;
    }

    // Colour is one presentation lane, not tile identity. When supplied it stays strict so a
    // malformed property cannot silently become an absent one.
    private static ColorRgba? ColorOf(TiledTile tile, string tileClass, string tilesetName)
    {
        TiledProperty? property = tile.Property(ColorProperty);
        if (property is null)
        {
            return null;
        }

        // A string property holding '#AARRGGBB' reads identically here, so only the declared type
        // separates a tile authored to the contract from one that happens to look like it. Tiled
        // omits the type of a string property, which is why an absent one is a mismatch.
        if (!string.Equals(property.Type, ColorPropertyType, StringComparison.Ordinal))
        {
            throw new TiledImportException(
                $"tileset '{tilesetName}' tile {tile.Id} (Class '{tileClass}') declares '{ColorProperty}' as a '{property.Type ?? "string"}' property; it has to be of type Color in Tiled.");
        }

        string? authored = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;

        return (authored is null ? null : ParseColor(authored))
            ?? throw new TiledImportException(
                $"tileset '{tilesetName}' tile {tile.Id} (Class '{tileClass}') has '{ColorProperty}' = '{authored}', which is not a Tiled colour.");
    }

    // Tiled writes a colour as #AARRGGBB — alpha leading, and optional — while every colour past
    // this point is RGBA. Reordering here is what keeps the two apart.
    private static ColorRgba? ParseColor(string authored)
    {
        ReadOnlySpan<char> hex = authored;
        if (hex.Length is not (7 or 9) || hex[0] != '#')
        {
            return null;
        }

        hex = hex[1..];

        byte alpha = byte.MaxValue;
        if (hex.Length == 8)
        {
            if (!TryHexByte(hex[..2], out alpha))
            {
                return null;
            }

            hex = hex[2..];
        }

        return TryHexByte(hex[..2], out byte red)
            && TryHexByte(hex[2..4], out byte green)
            && TryHexByte(hex[4..], out byte blue)
                ? new ColorRgba(red, green, blue, alpha)
                : null;
    }

    private static bool TryHexByte(ReadOnlySpan<char> hex, out byte value)
    {
        value = 0;

        return char.IsAsciiHexDigit(hex[0])
            && char.IsAsciiHexDigit(hex[1])
            && byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    // Layer type, never layer name: what a layer is called is a game's convention, not Capsule's.
    private static (TiledLayer TileLayer, List<TiledLayer> ObjectLayers) SplitLayers(TiledMap map)
    {
        TiledLayer? tileLayer = null;
        List<TiledLayer> objectLayers = [];

        foreach (TiledLayer layer in map.Layers)
        {
            switch (layer.Type)
            {
                case "tilelayer":
                    if (tileLayer is not null)
                    {
                        throw new TiledImportException(
                            $"the map has more than one tile layer ('{tileLayer.Name}' and '{layer.Name}'); exactly one is supported.");
                    }

                    tileLayer = layer;
                    break;

                case "objectgroup":
                    objectLayers.Add(layer);
                    break;

                default:
                    throw new TiledImportException(
                        $"unsupported layer type '{layer.Type}' (layer '{layer.Name}'); Capsule imports tile layers and object layers only.");
            }
        }

        return (tileLayer ?? throw new TiledImportException("the map has no tile layer."), objectLayers);
    }

    private static int[] ReadTiles(
        TiledLayer layer,
        TiledMap map,
        TiledTileset[] tilesets,
        Dictionary<int, int> paletteIndexByGid)
    {
        if (layer.Width != map.Width || layer.Height != map.Height)
        {
            throw new TiledImportException(
                $"tile layer '{layer.Name}' is {layer.Width}x{layer.Height} but the map is {map.Width}x{map.Height}.");
        }

        if (layer.Encoding is { } encoding && !encoding.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new TiledImportException(
                $"tile layer '{layer.Name}' uses '{encoding}' tile data; set Map > Map Properties > Tile Layer Format to CSV.");
        }

        if (layer.Compression is { Length: > 0 } compression)
        {
            throw new TiledImportException(
                $"tile layer '{layer.Name}' is '{compression}'-compressed; set Map > Map Properties > Tile Layer Format to CSV.");
        }

        if (layer.Data.ValueKind != JsonValueKind.Array)
        {
            throw new TiledImportException(
                $"tile layer '{layer.Name}' has no plain tile data; set Map > Map Properties > Tile Layer Format to CSV.");
        }

        int[] tiles = new int[(long)map.Width * map.Height];
        int index = 0;
        foreach (JsonElement element in layer.Data.EnumerateArray())
        {
            if (index == tiles.Length)
            {
                throw TileCountMismatch(layer, map, tiles.Length, "more than");
            }

            if (!element.TryGetUInt32(out uint gid))
            {
                throw new TiledImportException(
                    $"tile layer '{layer.Name}' has a non-numeric tile at index {index}.");
            }

            tiles[index] = ResolveGid(gid, index, layer, tilesets, paletteIndexByGid);
            index++;
        }

        if (index != tiles.Length)
        {
            throw TileCountMismatch(layer, map, index, "only");
        }

        return tiles;
    }

    private static TiledImportException TileCountMismatch(TiledLayer layer, TiledMap map, int count, string qualifier) =>
        new($"tile layer '{layer.Name}' carries {qualifier} {count} tiles but {map.Width}x{map.Height} requires {map.Width * map.Height}.");

    private static int ResolveGid(
        uint gid,
        int index,
        TiledLayer layer,
        TiledTileset[] tilesets,
        Dictionary<int, int> paletteIndexByGid)
    {
        if ((gid & OrientationFlags) != 0)
        {
            throw new TiledImportException(
                $"tile layer '{layer.Name}' has a flipped or rotated tile at index {index}; Capsule imports unflipped tiles only.");
        }

        if (gid == 0)
        {
            return 0;
        }

        if (paletteIndexByGid.TryGetValue((int)gid, out int paletteIndex))
        {
            return paletteIndex;
        }

        TiledTileset? owner = null;
        foreach (TiledTileset tileset in tilesets)
        {
            if (tileset.FirstGid <= (int)gid)
            {
                owner = tileset;
            }
        }

        throw owner is null
            ? new TiledImportException($"tile gid {gid} at index {index} belongs to no tileset in the map.")
            : new TiledImportException(
                $"tile {(int)gid - owner.FirstGid} of tileset '{owner.Name}' is painted at index {index} but has no Class; give every painted tile a Class in Tiled.");
    }

    private static List<MapObject> ReadObjects(List<TiledLayer> objectLayers)
    {
        List<MapObject> objects = [];
        foreach (TiledLayer layer in objectLayers)
        {
            foreach (TiledObject placed in layer.Objects ?? [])
            {
                string? objectClass = placed.ResolvedClass;
                if (string.IsNullOrWhiteSpace(objectClass))
                {
                    throw new TiledImportException(
                        $"object {placed.Id} on layer '{layer.Name}' has no Class; every object is typed by its Class.");
                }

                objects.Add(new MapObject(placed.Id, objectClass, (float)placed.X, (float)placed.Y));
            }
        }

        return objects;
    }

    private static string DirectoryOf(string path) =>
        Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();

    private static T Deserialize<T>(byte[] utf8, string path, JsonTypeInfo<T> typeInfo)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        ReadOnlySpan<byte> bytes = utf8;
        if (bytes.StartsWith(bom))
        {
            bytes = bytes[bom.Length..];
        }

        T? document;
        try
        {
            document = JsonSerializer.Deserialize(bytes, typeInfo);
        }
        catch (JsonException ex)
        {
            throw new TiledImportException(
                string.Create(CultureInfo.InvariantCulture, $"'{path}' is not readable Tiled JSON — {ex.Message}"),
                ex);
        }

        return document ?? throw new TiledImportException($"'{path}' is empty.");
    }
}
