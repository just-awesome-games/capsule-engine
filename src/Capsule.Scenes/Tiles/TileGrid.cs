using System.Numerics;
using Capsule.Assets;
using Capsule.Collision;
using Capsule.Rendering;

namespace Capsule.Scenes.Tiles;

/// <summary>A validated rectangular grid of palette indices.</summary>
public sealed class TileGrid
{
    /// <summary>The palette entry at index 0, meaning "no tile here". Reserved; never a game's tile type.</summary>
    public const string EmptyTileType = "empty";

    private readonly TileDefinition[] _tileTypes;
    private readonly int[] _tiles;

    // One frame per palette entry, cut once so drawing a cell is a table lookup rather than
    // arithmetic per tile. Null where a tile type draws nothing.
    private readonly Sprite?[] _sprites;

    /// <param name="tileSize">The edge length of one tile, in pixels and in world units.</param>
    /// <param name="width">Grid width in tiles.</param>
    /// <param name="height">Grid height in tiles.</param>
    /// <param name="tileTypes">The palette, starting with <see cref="EmptyTile"/>.</param>
    /// <param name="tiles">Palette indices, row-major, <paramref name="width"/> * <paramref name="height"/> of them.</param>
    /// <param name="texture">The texture every drawn tile is cut from, or null for a grid that draws nothing.</param>
    /// <param name="columns">
    /// How many cells wide <paramref name="texture"/> is, which turns a cell number into a source
    /// region. At least 1 when a texture is named, and 0 when none is.
    /// </param>
    /// <exception cref="ArgumentException">Some invariant of the grid is broken; the message names the defect.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tileTypes"/> is null. <paramref name="tiles"/> is null.</exception>
    public TileGrid(
        int tileSize,
        int width,
        int height,
        IReadOnlyList<TileDefinition> tileTypes,
        IReadOnlyList<int> tiles,
        TextureHandle? texture = null,
        int columns = 0)
    {
        ArgumentNullException.ThrowIfNull(tileTypes);
        ArgumentNullException.ThrowIfNull(tiles);

        TileSize = tileSize;
        Width = width;
        Height = height;
        Texture = texture;
        Columns = columns;
        _tileTypes = [.. tileTypes];
        _tiles = [.. tiles];

        Validate();

        _sprites = CutCells();
    }

    /// <summary>The palette entry every unpainted cell points at.</summary>
    public static TileDefinition EmptyTile => new(EmptyTileType, null);

    /// <summary>The edge length of one tile in pixels. Supplied by the grid; the engine has no opinion.</summary>
    public int TileSize { get; }

    /// <summary>Grid width in tiles.</summary>
    public int Width { get; }

    /// <summary>Grid height in tiles.</summary>
    public int Height { get; }

    /// <summary>The texture every drawn tile is cut from, or null where no tile type draws.</summary>
    public TextureHandle? Texture { get; }

    /// <summary>
    /// How many cells wide <see cref="Texture"/> is; 0 where the grid has none. A cell number runs
    /// across a row of this many and then down.
    /// </summary>
    public int Columns { get; }

    /// <summary>The tile palette. Index 0 is <see cref="EmptyTile"/> and type names are unique.</summary>
    public ReadOnlySpan<TileDefinition> TileTypes => _tileTypes;

    /// <summary>Palette indices, row-major, <see cref="Width"/> * <see cref="Height"/> of them.</summary>
    public ReadOnlySpan<int> Tiles => _tiles;

    /// <summary>Whether any palette entry is on a layer, so the grid is worth a collider at all.</summary>
    public bool Collides
    {
        get
        {
            foreach (TileDefinition definition in _tileTypes)
            {
                if (definition.Layer is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Handed to a tilemap collider, which reads it rather than copying it: a room-scale grid is
    // tens of thousands of ints.
    internal int[] Cells => _tiles;

    // One frame per palette index, in palette order.
    internal ReadOnlySpan<Sprite?> Sprites => _sprites;

    /// <summary>The palette index at a tile coordinate; 0 where the grid is empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public int TileAt(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return _tiles[(y * Width) + x];
    }

    /// <summary>The tile type name at a tile coordinate; <see cref="EmptyTileType"/> where the grid is empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public string TileTypeAt(int x, int y) => _tileTypes[TileAt(x, y)].Type;

    private void Validate()
    {
        if (TileSize <= 0)
        {
            throw Malformed($"tileSize must be positive, not {TileSize}.", "tileSize");
        }

        if (Width <= 0)
        {
            throw Malformed($"width must be positive, not {Width}.", "width");
        }

        if (Height <= 0)
        {
            throw Malformed($"height must be positive, not {Height}.", "height");
        }

        ValidatePalette();
        ValidateTexture();
        ValidateTiles();
    }

    private void ValidatePalette()
    {
        if (_tileTypes.Length == 0 || _tileTypes[0] != EmptyTile)
        {
            string actual = _tileTypes.Length == 0
                ? "an empty palette"
                : $"\"{_tileTypes[0].Type}\" with cell {_tileTypes[0].Cell?.ToString() ?? "none"} and layer {_tileTypes[0].Layer ?? "none"}";
            throw Malformed(
                $"tileTypes[0] must be \"{EmptyTileType}\" with no cell and no layer, not {actual}.",
                "tileTypes");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < _tileTypes.Length; i++)
        {
            TileDefinition definition = _tileTypes[i];

            if (string.IsNullOrWhiteSpace(definition.Type))
            {
                throw Malformed($"tileTypes[{i}] is blank; every tile type must be named.", "tileTypes");
            }

            if (!seen.Add(definition.Type))
            {
                throw Malformed($"tileTypes[{i}] repeats \"{definition.Type}\"; tile type names must be unique.", "tileTypes");
            }

            if (definition.Cell is { } cell && cell < 0)
            {
                throw Malformed($"tileTypes[{i}] draws cell {cell}; a cell is counted from 0.", "tileTypes");
            }

            if ((definition.CollidableFaces & ~CellFaces2D.All) != 0)
            {
                throw Malformed(
                    $"tileTypes[{i}] declares collidableFaces {(int)definition.CollidableFaces}, which is not a combination of the four sides a tile has.",
                    "tileTypes");
            }

            if (definition.Layer is { } layer)
            {
                if (string.IsNullOrWhiteSpace(layer))
                {
                    throw Malformed($"tileTypes[{i}] has a blank layer; a tile that collides names the layer it is on.", "tileTypes");
                }

                // A tile on a layer with no face collides with nothing, which is a mistake rather
                // than a spelling of "decoration": that is written by naming no layer.
                if (definition.CollidableFaces == CellFaces2D.None)
                {
                    throw Malformed(
                        $"tileTypes[{i}] is on a layer but has no collidableFaces; a tile that collides needs at least one face, and one that collides as nothing names no layer.",
                        "tileTypes");
                }
            }
            else if (definition.CollidableFaces != CellFaces2D.All)
            {
                throw Malformed(
                    $"tileTypes[{i}] declares collidableFaces but no layer; a tile that collides as nothing has no sides to declare.",
                    "tileTypes");
            }
        }
    }

    // Strict both ways: either of cell and texture without the other is a half-written grid.
    private void ValidateTexture()
    {
        int drawn = 0;
        for (int i = 0; i < _tileTypes.Length; i++)
        {
            if (_tileTypes[i].Cell is null)
            {
                continue;
            }

            drawn++;

            if (Texture is null)
            {
                throw Malformed(
                    $"tileTypes[{i}] draws cell {_tileTypes[i].Cell}, but the grid names no texture to cut it from.",
                    "tileTypes");
            }
        }

        if (Texture is null)
        {
            if (Columns != 0)
            {
                throw Malformed(
                    $"columns is {Columns} on a grid that names no texture; columns counts the cells across the texture a grid draws from.",
                    "columns");
            }

            return;
        }

        if (drawn == 0)
        {
            throw Malformed(
                $"the grid names texture \"{Texture.Value.Name}\" but no tile type draws a cell of it.",
                "texture");
        }

        if (Columns < 1)
        {
            throw Malformed(
                $"columns must be at least 1 on a textured grid, not {Columns}.",
                "columns");
        }

        ValidateCellRegions();
    }

    // Widened to long, and only once columns is known positive: a cell far enough down the atlas
    // overflows int, and the wrapped coordinate would silently cut the wrong region.
    private void ValidateCellRegions()
    {
        for (int i = 0; i < _tileTypes.Length; i++)
        {
            if (_tileTypes[i].Cell is not { } cell)
            {
                continue;
            }

            long x = (long)(cell % Columns) * TileSize;
            long y = (long)(cell / Columns) * TileSize;

            if (x + TileSize > int.MaxValue || y + TileSize > int.MaxValue)
            {
                throw Malformed(
                    $"tileTypes[{i}] (\"{_tileTypes[i].Type}\") draws cell {cell}, whose source region starts at ({x}, {y}) texels across {Columns} columns of {TileSize}px — further than a texture coordinate reaches.",
                    "tileTypes");
            }
        }
    }

    private void ValidateTiles()
    {
        // Widened to long: an int product wraps, and 65536 x 65536 wrapping to 0 would let an
        // empty tiles array pass here and fail later inside TileAt.
        long expected = (long)Width * Height;
        if (_tiles.Length != expected)
        {
            throw Malformed(
                $"tiles has {_tiles.Length} entries but width {Width} x height {Height} requires {expected}.",
                "tiles");
        }

        for (int i = 0; i < _tiles.Length; i++)
        {
            if (_tiles[i] < 0 || _tiles[i] >= _tileTypes.Length)
            {
                throw Malformed(
                    $"tiles[{i}] is {_tiles[i]}, which is not a tileTypes index (0..{_tileTypes.Length - 1}).",
                    "tiles");
            }
        }
    }

    private Sprite?[] CutCells()
    {
        Sprite?[] sprites = new Sprite?[_tileTypes.Length];
        if (Texture is not { } texture)
        {
            return sprites;
        }

        for (int i = 0; i < sprites.Length; i++)
        {
            if (_tileTypes[i].Cell is not { } cell)
            {
                continue;
            }

            sprites[i] = new Sprite(
                texture,
                new TextureRegion(cell % Columns * TileSize, cell / Columns * TileSize, TileSize, TileSize),
                Vector2.Zero);
        }

        return sprites;
    }

    private static ArgumentException Malformed(string message, string parameterName) => new(message, parameterName);
}
