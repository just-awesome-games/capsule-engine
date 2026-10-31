using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;

namespace Capsule.Tiles;

/// <summary>A validated rectangular grid of palette indices.</summary>
/// <remarks>
/// The grid keeps only painted cells, in blocks of 16 by 16 tiles. A block with no painted cell costs one
/// reference, and a block keeps per-tile transforms only once one of its tiles is mirrored or turned.
/// </remarks>
public sealed class TileGrid
{
    /// <summary>The name of the palette entry at index 0, meaning "no tile here". Reserved, and not available to a game's own tile types.</summary>
    public const string EmptyTileName = "empty";

    // The cells built from each authored tiles array, once per array. Every scene composed from one document
    // reads the same authored arrays, and so shares the same cells.
    private static readonly ConditionalWeakTable<int[], AuthoredCells> Authored = new();

    /// <summary>The plain palette entry every unpainted cell points at, named <see cref="EmptyTileName"/>.</summary>
    public static TileType EmptyTile { get; } = new() { Name = EmptyTileName };

    private readonly TileType[] _tileTypes;

    // The palette index and TileTransform of every painted cell, never written. A grid built from a scene
    // document shares them with every other grid built from it.
    private readonly CellBlocks _cells;

    // One sprite per palette entry, cut once so drawing a cell is a table lookup instead of arithmetic
    // per tile. An entry is null when its tile type draws nothing. An animated entry holds its first
    // frame, and a map steps its own copy from there.
    private readonly Sprite?[] _sprites;

    // One per palette entry with frames, cut once beside _sprites.
    private readonly Animation[] _animations;

    // Each palette entry's collision polygon, built once from its points, or null for a whole tile.
    private readonly Shape2D?[] _shapes;

    /// <inheritdoc cref="TileMap.TileSize"/>
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

    /// <param name="tileSize">The edge length of one tile. See <see cref="TileSize"/>.</param>
    /// <param name="width">Grid width in tiles.</param>
    /// <param name="height">Grid height in tiles.</param>
    /// <param name="tileTypes">The palette, starting with <see cref="EmptyTile"/>, of at most 65,536 entries.</param>
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
    /// <exception cref="ArgumentException">The grid is malformed. The message names the defect.</exception>
    public TileGrid(
        int tileSize,
        int width,
        int height,
        IReadOnlyList<TileType> tileTypes,
        IReadOnlyList<int> tiles,
        TextureHandle? texture = null,
        int columns = 0,
        IReadOnlyList<TileTransform>? transforms = null)
        : this(
            tileSize,
            width,
            height,
            [.. tileTypes ?? throw new ArgumentNullException(nameof(tileTypes))],
            [.. tiles ?? throw new ArgumentNullException(nameof(tiles))],
            texture,
            columns,
            cells: null,
            transforms,
            authored: null,
            shared: false)
    {
    }

    // A grid over a scene document's tiles and transforms, which it shares and never writes. Each authored
    // transform is a TileTransform value, checked here.
    internal TileGrid(
        int tileSize,
        int width,
        int height,
        TileType[] tileTypes,
        ReadOnlyMemory<int> tiles,
        TextureHandle? texture,
        int columns,
        ReadOnlyMemory<int>? transforms)
        : this(tileSize, width, height, [.. tileTypes], Whole(tiles), texture, columns, cells: null, transforms: null, authored: transforms is { } dense ? Whole(dense) : null, shared: true)
    {
    }

    // A grid over a compiled scene document's cells, which it shares and never writes.
    internal TileGrid(int tileSize, int width, int height, TileType[] tileTypes, CellBlocks cells, TextureHandle? texture, int columns)
        : this(tileSize, width, height, [.. tileTypes], tiles: null, texture, columns, cells, transforms: null, authored: null, shared: true)
    {
    }

    // The grid takes compiled cells or dense tiles. Dense transforms are listed, authored or neither, and are read
    // only once the rest of the grid is valid. Shared dense cells are built once per authored tiles array.
    private TileGrid(
        int tileSize,
        int width,
        int height,
        TileType[] tileTypes,
        int[]? tiles,
        TextureHandle? texture,
        int columns,
        CellBlocks? cells,
        IReadOnlyList<TileTransform>? transforms,
        int[]? authored,
        bool shared)
    {
        TileSize = tileSize;
        Width = width;
        Height = height;
        Texture = texture;
        Columns = columns;
        _tileTypes = tileTypes;
        _shapes = TileGridValidation.Validate(tileSize, width, height, tileTypes, tiles, texture, columns);
        if (cells is not null)
        {
            TileGridValidation.ValidateCells(cells, tileTypes.Length);
            _cells = cells;
        }
        else
        {
            _cells = shared
                ? Shared(tiles!, authored, width, height)
                : Build(tiles!, transforms is null ? null : [.. transforms.Select(static transform => (byte)transform)], width, height);
        }

        _sprites = CutCells();
        _animations = CutAnimations();
    }

    /// <summary>The tile palette. Index 0 is an empty tile like <see cref="EmptyTile"/>, and names are unique.</summary>
    public ReadOnlySpan<TileType> TileTypes => _tileTypes;

    // The cells a map reads until it edits one. They are never written.
    internal CellBlocks Cells => _cells;

    // Whether any palette entry is on a layer. A grid with none needs no collider.
    internal bool Collides => Array.Exists(_tileTypes, static tileType => tileType.Layer is not null);

    // Whether any palette entry has a surface velocity. Only such a grid's collider steps.
    internal bool Carries => Array.Exists(_tileTypes, static tileType => tileType.SurfaceVelocity != Vector2.Zero);

    // Memory instead of a span, which a map can hold. A map never writes the grid's own table.
    internal ReadOnlyMemory<Sprite?> Sprites => _sprites;

    internal ReadOnlyMemory<Animation> Animations => _animations;

    // Parallel to TileTypes. A collider reads each entry's polygon here, not off its points.
    internal ReadOnlySpan<Shape2D?> Shapes => _shapes;

    /// <summary>
    /// Returns the palette entry at a tile coordinate, and the entry named <see cref="EmptyTileName"/> where the
    /// grid is empty.
    /// </summary>
    public TileType TileAt(int x, int y)
    {
        _cells.RequireOnGrid(x, y);

        return _tileTypes[_cells.ValueAt(x, y)];
    }

    /// <summary>Returns how the tile at a tile coordinate is mirrored or turned.</summary>
    public TileTransform TransformAt(int x, int y)
    {
        _cells.RequireOnGrid(x, y);

        return (TileTransform)_cells.VariantAt(x, y);
    }

    // The cells of validated tiles and their facings, which are null for tiles drawn as authored.
    private static CellBlocks Build(int[] tiles, byte[]? facings, int width, int height)
    {
        if (facings is not null)
        {
            TileGridValidation.ValidateTransforms(facings, width, height, tiles.Length);
        }

        return CellBlocks.FromDense(width, height, tiles, facings);
    }

    private static CellBlocks BuildAuthored(int[] tiles, int[]? authored, int width, int height) =>
        Build(tiles, authored is null ? null : TileGridValidation.Turned(authored), width, height);

    // The cells of authored tiles and transforms, built once per tiles array. Tiles authored once come with the
    // same transforms every time, and other transforms build cells of their own.
    private static CellBlocks Shared(int[] tiles, int[]? authored, int width, int height)
    {
        AuthoredCells built = Authored.GetValue(tiles, _ => new AuthoredCells(authored, BuildAuthored(tiles, authored, width, height)));

        return ReferenceEquals(built.Transforms, authored) ? built.Cells : BuildAuthored(tiles, authored, width, height);
    }

    // The array a memory spans, or a copy when it spans part of one.
    private static int[] Whole(ReadOnlyMemory<int> memory) =>
        MemoryMarshal.TryGetArray(memory, out ArraySegment<int> segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? segment.Array
            : memory.ToArray();

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
            if (_tileTypes[i].Cell is { } cell)
            {
                sprites[i] = CutCell(texture, cell);
            }
            else if (_tileTypes[i].Frames is { } frames)
            {
                sprites[i] = CutCell(texture, frames[0].Cell);
            }
        }

        return sprites;
    }

    private Animation[] CutAnimations()
    {
        int count = _tileTypes.Count(static tileType => tileType.Frames is not null);
        if (count == 0)
        {
            return [];
        }

        // Validation refused frames on a grid with no texture.
        TextureHandle texture = Texture!.Value;
        Animation[] animations = new Animation[count];
        int next = 0;
        for (int i = 0; i < _tileTypes.Length; i++)
        {
            if (_tileTypes[i].Frames is not { } frames)
            {
                continue;
            }

            Sprite[] sprites = new Sprite[frames.Length];
            int[] ticks = new int[frames.Length];
            for (int frame = 0; frame < sprites.Length; frame++)
            {
                sprites[frame] = CutCell(texture, frames[frame].Cell);
                ticks[frame] = frames[frame].Ticks;
            }

            animations[next++] = new Animation(i, new SpriteClip(sprites, ticks, loop: true));
        }

        return animations;
    }

    private Sprite CutCell(TextureHandle texture, int cell) => new(
        texture,
        new TextureRegion(cell % Columns * TileSize, cell / Columns * TileSize, TileSize, TileSize),
        new Vector2(TileSize / 2f, TileSize / 2f));

    // The cells built from one authored tiles array, and the transforms array they were built with.
    private sealed record AuthoredCells(int[]? Transforms, CellBlocks Cells);

    // One animated palette entry and the looping clip its frames make.
    internal readonly record struct Animation(int Palette, SpriteClip Clip);
}
