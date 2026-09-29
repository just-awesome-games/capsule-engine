using System.Numerics;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;

namespace Capsule.Tiles;

/// <summary>A validated rectangular grid of palette indices.</summary>
public sealed class TileGrid
{
    /// <summary>The name of the palette entry at index 0, meaning "no tile here". Reserved, and not available to a game's own tile types.</summary>
    public const string EmptyTileName = "empty";

    private readonly TileType[] _tileTypes;
    private readonly int[] _tiles;
    private readonly TileTransform[] _transforms;

    // One sprite per palette entry, cut once so drawing a cell is a table lookup instead of arithmetic
    // per tile. An entry is null when its tile type draws nothing.
    private readonly Sprite?[] _sprites;

    // Parallel to the palette, or null when no entry authors a class or properties.
    private readonly AuthoredTileType[]? _authored;

    /// <param name="tileSize">The edge length of one tile. See <see cref="TileSize"/>.</param>
    /// <param name="width">Grid width in tiles.</param>
    /// <param name="height">Grid height in tiles.</param>
    /// <param name="tileTypes">The palette, starting with <see cref="EmptyTile"/>.</param>
    /// <param name="tiles">Palette indices, row-major, <paramref name="width"/> * <paramref name="height"/> of them.</param>
    /// <param name="texture">The texture every drawn tile is cut from, or null for a grid that draws nothing.</param>
    /// <param name="columns">
    /// How many cells wide <paramref name="texture"/> is. This turns a cell number into a source
    /// region. Pass at least 1 with a texture, and 0 without one.
    /// </param>
    /// <param name="transforms">
    /// How each tile is mirrored or turned, row-major and parallel to <paramref name="tiles"/>, or null
    /// for a grid of tiles drawn as authored.
    /// </param>
    /// <param name="authored">
    /// What a scene document authors for each palette entry beyond the engine's fields, parallel to
    /// <paramref name="tileTypes"/>, or null for a palette of plain entries. A scene composes each entry that
    /// names a type into an instance of that type, carried in <see cref="TileTypes"/>.
    /// </param>
    /// <exception cref="ArgumentException">The grid is malformed. The message names the defect.</exception>
    public TileGrid(
        int tileSize,
        int width,
        int height,
        IReadOnlyList<TileType> tileTypes,
        IReadOnlyList<int> tiles,
        TextureHandle? texture = null,
        int columns = 0,
        IReadOnlyList<TileTransform>? transforms = null,
        IReadOnlyList<AuthoredTileType>? authored = null)
    {
        ArgumentNullException.ThrowIfNull(tileTypes);
        ArgumentNullException.ThrowIfNull(tiles);
        if (authored is not null && authored.Count != tileTypes.Count)
        {
            throw new ArgumentException(
                $"authored has {authored.Count} entries for a palette of {tileTypes.Count}. Give one per palette entry.",
                nameof(authored));
        }

        TileSize = tileSize;
        Width = width;
        Height = height;
        Texture = texture;
        Columns = columns;
        _tileTypes = [.. tileTypes];
        _tiles = [.. tiles];
        _authored = authored is null ? null : [.. authored];

        Validate();

        // Sized after Validate, which bounds the grid's area.
        _transforms = transforms is null ? new TileTransform[_tiles.Length] : [.. transforms];
        ValidateTransforms();

        _sprites = CutCells();
    }

    /// <summary>The plain palette entry every unpainted cell points at, named <see cref="EmptyTileName"/>.</summary>
    public static TileType EmptyTile { get; } = new() { Name = EmptyTileName };

    /// <summary>
    /// The edge length of one tile in world units, which also equals its edge in atlas pixels.
    /// </summary>
    public int TileSize { get; }

    /// <summary>Grid width in tiles.</summary>
    public int Width { get; }

    /// <summary>Grid height in tiles.</summary>
    public int Height { get; }

    /// <summary>The texture every drawn tile is cut from, or null when no tile type draws.</summary>
    public TextureHandle? Texture { get; }

    /// <summary>
    /// How many cells wide <see cref="Texture"/> is, or 0 when the grid has no texture. Cell numbers run
    /// across a row of this many and then wrap to the next row.
    /// </summary>
    public int Columns { get; }

    /// <summary>The tile palette. Index 0 is an empty tile like <see cref="EmptyTile"/>, and names are unique.</summary>
    public ReadOnlySpan<TileType> TileTypes => _tileTypes;

    /// <summary>Palette indices, row-major from the top row, <see cref="Width"/> * <see cref="Height"/> of them.</summary>
    public ReadOnlySpan<int> Tiles => _tiles;

    /// <summary>
    /// How each tile is mirrored or turned, parallel to <see cref="Tiles"/>. Every entry is
    /// <see cref="TileTransform.None"/> on a grid built without transforms.
    /// </summary>
    public ReadOnlySpan<TileTransform> Transforms => _transforms;

    // Whether any palette entry is on a layer. A grid with none needs no collider.
    internal bool Collides
    {
        get
        {
            foreach (TileType tileType in _tileTypes)
            {
                if (tileType.Layer is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal ReadOnlySpan<Sprite?> Sprites => _sprites;

    // Build metadata for the scene document writer. A composed grid carries instances and no authored entries.
    internal IReadOnlyList<AuthoredTileType>? Authored => _authored;

    // This grid with the palette a scene composed from it, whose instances now carry what was authored.
    internal TileGrid Composed(TileType[] tileTypes) => new(TileSize, Width, Height, tileTypes, _tiles, Texture, Columns, _transforms);

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
        int missing = Array.IndexOf(_tileTypes, null);
        if (missing >= 0)
        {
            throw Malformed($"tileTypes[{missing}] is null. Give every palette entry a tile type.", "tileTypes");
        }

        if (_tileTypes.Length == 0 || !IsEmpty(_tileTypes[0]))
        {
            string actual = _tileTypes.Length == 0
                ? "an empty palette"
                : $"\"{_tileTypes[0].Name}\" with cell {_tileTypes[0].Cell?.ToString() ?? "none"} and layer {_tileTypes[0].Layer ?? "none"}";
            throw Malformed(
                $"tileTypes[0] is {actual}. Make it a plain TileType named \"{EmptyTileName}\" with no cell, no layer and no properties.",
                "tileTypes");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < _tileTypes.Length; i++)
        {
            TileType tileType = _tileTypes[i];

            if (string.IsNullOrWhiteSpace(tileType.Name))
            {
                throw Malformed($"tileTypes[{i}] has no name. Name every tile type.", "tileTypes");
            }

            if (!seen.Add(tileType.Name))
            {
                throw Malformed($"tileTypes[{i}] repeats \"{tileType.Name}\". Give every tile type a unique name.", "tileTypes");
            }

            if (tileType.Cell is { } cell && cell < 0)
            {
                throw Malformed($"tileTypes[{i}] draws cell {cell}. Count cells from 0.", "tileTypes");
            }

            if (tileType.Layer is { } layer)
            {
                if (string.IsNullOrWhiteSpace(layer))
                {
                    throw Malformed($"tileTypes[{i}] has a blank layer. Name the layer a colliding tile is on.", "tileTypes");
                }
            }
            else if (tileType.Shape is not null || tileType.OneWay)
            {
                throw Malformed(
                    $"tileTypes[{i}] declares {(tileType.Shape is null ? "oneWay" : "a shape")} but no layer and collides as nothing. Add a layer or drop it.",
                    "tileTypes");
            }

            if (tileType.SolidSides && !tileType.OneWay)
            {
                throw Malformed(
                    $"tileTypes[{i}] declares solidSides but no oneWay. Add oneWay, or drop solidSides for a tile solid from every side.",
                    "tileTypes");
            }

            if (tileType.Shape is { } shape)
            {
                ValidateShape(shape, i);
            }
        }
    }

    // A tile's shape is a plain convex polygon inside its own tile. Shape2D already refused a concave one.
    private void ValidateShape(in Shape2D shape, int index)
    {
        if (shape.Kind is not (ShapeKind2D.Polygon or ShapeKind2D.Box) || shape.Radius != 0f)
        {
            throw Malformed(
                $"tileTypes[{index}] has a {shape.Kind} shape of radius {shape.Radius}. Give a tile a polygon of 3 or 4 points with no radius.",
                "tileTypes");
        }

        Aabb2D bounds = shape.Bounds;
        if (bounds.Min.X < 0f || bounds.Min.Y < 0f || bounds.Max.X > TileSize || bounds.Max.Y > TileSize)
        {
            throw Malformed(
                $"tileTypes[{index}] has a shape point outside its tile. Keep every point within [0, {TileSize}] on both axes, measured from the tile's top-left corner.",
                "tileTypes");
        }
    }

    // Checks both directions, because a cell without a texture or a texture without a cell is a
    // half-written grid.
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
                    $"tileTypes[{i}] draws cell {_tileTypes[i].Cell} but the grid names no texture. Give the grid a texture to cut from.",
                    "tileTypes");
            }
        }

        if (Texture is null)
        {
            if (Columns != 0)
            {
                throw Malformed(
                    $"columns is {Columns} on a grid that names no texture. Set columns to 0, or name a texture.",
                    "columns");
            }

            return;
        }

        if (drawn == 0)
        {
            throw Malformed(
                $"the grid names texture \"{Texture.Value.Name}\" but no tile type draws a cell of it. Give a tile type a cell, or drop the texture.",
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

    // Computes in long, and runs only after columns is known positive, because a cell far enough down
    // the atlas overflows int and the wrapped coordinate would cut the wrong region.
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
                    $"tileTypes[{i}] (\"{_tileTypes[i].Name}\") draws cell {cell}, whose source region starts at ({x}, {y}) texels across {Columns} columns of {TileSize}px, beyond the reach of a texture coordinate. Lower the cell number or the tile size.",
                    "tileTypes");
            }
        }
    }

    private void ValidateTiles()
    {
        // Computes in long, because an int product wraps and 65536 x 65536 wrapping to 0 would let an
        // empty tiles array pass here and fail later when a cell is read.
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
                    $"tiles[{i}] is {_tiles[i]}. Use a tileTypes index in 0..{_tileTypes.Length - 1}.",
                    "tiles");
            }
        }
    }

    private void ValidateTransforms()
    {
        if (_transforms.Length != _tiles.Length)
        {
            throw Malformed(
                $"transforms has {_transforms.Length} entries but width {Width} x height {Height} requires {_tiles.Length}. Give one per tile, or pass null for none.",
                "transforms");
        }

        for (int i = 0; i < _transforms.Length; i++)
        {
            if (!TileTransforms.IsDefined(_transforms[i]))
            {
                throw Malformed(
                    $"transforms[{i}] is {(byte)_transforms[i]}. Use a TileTransform in 0..{TileTransforms.Count - 1}.",
                    "transforms");
            }
        }
    }

    // Each sprite pivots on its centre. A mirrored or turned tile then stays inside its cell.
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
                new Vector2(TileSize / 2f, TileSize / 2f));
        }

        return sprites;
    }

    // The empty entry draws, collides and authors nothing. A class or properties would fill every unpainted cell.
    private bool IsEmpty(TileType tileType) =>
        tileType is { Name: EmptyTileName, Cell: null, Layer: null, Shape: null, OneWay: false, SolidSides: false }
        && tileType.GetType() == typeof(TileType)
        && _authored?[0] is null or { Type: null, Properties: null };

    private static ArgumentException Malformed(string message, string parameterName) => new(message, parameterName);
}
