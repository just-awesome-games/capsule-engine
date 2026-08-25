using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Capsule.Levels.Cli.Tiled;

/// <summary>
/// Turns a Tiled map into a Capsule level. A pure function of the map and its tilesets: the
/// same inputs always produce the same level, which is what lets a build skip an unchanged map
/// and a golden fixture pin the output byte for byte.
/// </summary>
public static class TiledImporter
{
    /// <summary>The value stamped into an imported level's <c>source.tool</c>.</summary>
    public const string ToolName = "tiled";

    // Tiled packs flip and rotation into the top nibble of a gid.
    private const uint OrientationFlags = 0xF000_0000u;

    /// <summary>
    /// Imports the map at <paramref name="mapPath"/>. The path is stamped into the level's source
    /// block exactly as given, separators normalised — so it must be relative, and it means what
    /// it means from the working directory this ran in.
    /// </summary>
    /// <exception cref="TiledImportException">The map uses something Capsule does not import.</exception>
    public static Level Import(string mapPath)
    {
        byte[] mapBytes = File.ReadAllBytes(mapPath);
        TiledMap map = Deserialize(mapBytes, mapPath, TiledJsonContext.Default.TiledMap);

        RequireSupportedMap(map, mapPath);

        string mapDirectory = DirectoryOf(mapPath);
        TiledTileset[] tilesets = LoadTilesets(map, mapDirectory);

        List<string> palette = [Level.EmptyTileType];
        Dictionary<int, int> paletteIndexByGid = BuildPalette(tilesets, palette);

        (TiledLayer tileLayer, List<TiledLayer> objectLayers) = SplitLayers(map);
        int[] tiles = ReadTiles(tileLayer, map, tilesets, paletteIndexByGid);
        List<LevelEntity> entities = ReadEntities(objectLayers);

        LevelSource source = new(
            ToolName,
            mapPath.Replace('\\', '/'),
            Convert.ToHexStringLower(SHA256.HashData(mapBytes)));

        try
        {
            return new Level(
                map.TileWidth,
                map.Width,
                map.Height,
                palette,
                tiles,
                entities,
                map.NextObjectId,
                source);
        }
        catch (LevelFormatException ex)
        {
            throw new TiledImportException($"'{mapPath}' imports to an invalid level: {ex.Message}", ex);
        }
    }

    private static void RequireSupportedMap(TiledMap map, string mapPath)
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

        // Widened before anything is sized off it: an int product wraps, and the wrapped value
        // would size the tile array rather than fail here.
        long area = (long)map.Width * map.Height;
        if (map.Width <= 0 || map.Height <= 0 || area > Array.MaxLength)
        {
            throw new TiledImportException(
                $"'{mapPath}' is {map.Width}x{map.Height}, which is not a grid Capsule can hold.");
        }
    }

    private static TiledTileset[] LoadTilesets(TiledMap map, string mapDirectory)
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
            if (!File.Exists(path))
            {
                throw new TiledImportException($"tileset '{entry.Source}' is missing (expected at '{path}').");
            }

            TiledTileset tileset = Deserialize(File.ReadAllBytes(path), path, TiledJsonContext.Default.TiledTileset);
            tileset.FirstGid = entry.FirstGid;
            tileset.Name ??= Path.GetFileNameWithoutExtension(entry.Source);
            resolved.Add(tileset);
        }

        return [.. resolved.OrderBy(tileset => tileset.FirstGid)];
    }

    // Every Class in the tilesets enters the palette, painted or not, in tileset then tile-id
    // order: painting a new type must not renumber the types already in a committed level.
    private static Dictionary<int, int> BuildPalette(TiledTileset[] tilesets, List<string> palette)
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

                if (string.Equals(tileClass, Level.EmptyTileType, StringComparison.Ordinal))
                {
                    throw new TiledImportException(
                        $"tileset '{tilesetName}' tile {tile.Id} has Class '{Level.EmptyTileType}', which is reserved for the absence of a tile; rename it.");
                }

                if (!tilesetByClass.TryAdd(tileClass, tilesetName))
                {
                    throw new TiledImportException(
                        $"Class '{tileClass}' is defined by more than one tile (tilesets '{tilesetByClass[tileClass]}' and '{tilesetName}'); a Class must name exactly one tile.");
                }

                paletteIndexByGid[tileset.FirstGid + tile.Id] = palette.Count;
                palette.Add(tileClass);
            }
        }

        return paletteIndexByGid;
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

    private static List<LevelEntity> ReadEntities(List<TiledLayer> objectLayers)
    {
        List<LevelEntity> entities = [];
        foreach (TiledLayer layer in objectLayers)
        {
            foreach (TiledObject entity in layer.Objects ?? [])
            {
                string? entityClass = entity.ResolvedClass;
                if (string.IsNullOrWhiteSpace(entityClass))
                {
                    throw new TiledImportException(
                        $"object {entity.Id} on layer '{layer.Name}' has no Class; every object becomes an entity and every entity is typed by its Class.");
                }

                entities.Add(new LevelEntity(entity.Id, entityClass, (float)entity.X, (float)entity.Y));
            }
        }

        return entities;
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
