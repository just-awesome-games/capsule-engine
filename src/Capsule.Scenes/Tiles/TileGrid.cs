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

    /// <summary>The plain palette entry every unpainted cell points at, named <see cref="EmptyTileName"/>.</summary>
    public static TileType EmptyTile { get; } = new() { Name = EmptyTileName };

    // What each scene document's palette makes, by the palette array the document's maps share.
    private static readonly ConditionalWeakTable<TileType[], PaletteCut> Palettes = new();

    private readonly TileType[] _tileTypes;

    // Every slot a cell can hold: the palette, then a slot per look of each rule entry. It is the palette itself when
    // no entry has rules.
    private readonly TileType[] _slots;

    // What the palette's rule entries pick between, or null when none has rules.
    private readonly TileRules? _rules;

    // Each palette entry's index by its name, built the first time a map sets a tile by name.
    private Dictionary<string, int>? _indexByName;

    // The palette index and TileTransform of every painted cell, never written.
    private readonly CellBlocks _cells;

    // One sprite per slot, cut once so drawing a cell is a table lookup instead of arithmetic
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
            authored: null)
    {
    }

    // A grid over a scene document's palette, tiles and transforms, which it shares and never writes. Each
    // authored transform is a TileTransform value, checked here.
    internal TileGrid(
        int tileSize,
        int width,
        int height,
        ReadOnlyMemory<TileType> tileTypes,
        ReadOnlyMemory<int> tiles,
        TextureHandle? texture,
        int columns,
        ReadOnlyMemory<int>? transforms)
        : this(tileSize, width, height, Whole(tileTypes), Whole(tiles), texture, columns, cells: null, transforms: null, authored: transforms, shared: true)
    {
    }

    // A grid over a compiled scene document's palette and cells, which it shares and never writes.
    internal TileGrid(int tileSize, int width, int height, ReadOnlyMemory<TileType> tileTypes, CellBlocks cells, TextureHandle? texture, int columns)
        : this(tileSize, width, height, Whole(tileTypes), tiles: null, texture, columns, cells, transforms: null, authored: null, shared: true)
    {
    }

    // The grid takes compiled cells or dense tiles. Dense transforms are listed, authored or neither, and are read
    // only once the rest of the grid is valid. A shared palette is a document's, which every map that authors it
    // holds.
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
        ReadOnlyMemory<int>? authored,
        bool shared = false)
    {
        TileSize = tileSize;
        Width = width;
        Height = height;
        Texture = texture;
        Columns = columns;
        _tileTypes = tileTypes;
        TileGridValidation.ValidateSize(tileSize, width, height);
        PaletteCut palette = Cut(tileTypes, tileSize, texture, columns, shared);
        _rules = palette.Rules;
        _slots = _rules?.Slots ?? tileTypes;
        _shapes = palette.Shapes;
        _sprites = palette.Sprites;
        _animations = palette.Animations;
        if (tiles is not null)
        {
            TileGridValidation.ValidateTiles(tiles, width, height, tileTypes.Length);
        }

        if (cells is not null)
        {
            TileGridValidation.ValidateCells(cells, tileTypes.Length);

            // Decoded cells are kept with the document for every composition of it. Looks settle into a copy.
            _cells = _rules is null ? cells : cells.Share();
        }
        else
        {
            byte[]? facings = authored is { } dense
                ? TileGridValidation.Turned(dense.Span)
                : transforms is null ? null : [.. transforms.Select(static transform => (byte)transform)];
            _cells = Build(tiles!, facings, width, height);
        }

        _rules?.Settle(_cells);
    }

    /// <summary>The tile palette. Index 0 is an empty tile like <see cref="EmptyTile"/>, and names are unique.</summary>
    public ReadOnlySpan<TileType> TileTypes => _tileTypes;

    // The cells a map reads until it edits one. They are never written.
    internal CellBlocks Cells => _cells;

    // The palette index of the entry named name, or -1 where none is. The index holds each unique name once.
    internal int IndexOf(string name)
    {
        if (_indexByName is null)
        {
            Dictionary<string, int> indexByName = new(_tileTypes.Length, StringComparer.Ordinal);
            for (int index = 0; index < _tileTypes.Length; index++)
            {
                indexByName[_tileTypes[index].Name] = index;
            }

            _indexByName = indexByName;
        }

        return _indexByName.GetValueOrDefault(name, -1);
    }

    // Every slot a cell can hold, each the palette entry it draws for.
    internal ReadOnlySpan<TileType> Slots => _slots;

    internal TileRules? Rules => _rules;

    // Whether any palette entry is on a layer. A grid with none needs no collider.
    internal bool Collides => Array.Exists(_tileTypes, static tileType => tileType.Layer is not null);

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

        return _slots[_cells.ValueAt(x, y)];
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

    // The array a memory spans, or a copy when it spans part of one.
    private static T[] Whole<T>(ReadOnlyMemory<T> memory) =>
        MemoryMarshal.TryGetArray(memory, out ArraySegment<T> segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? segment.Array
            : memory.ToArray();

    // Checks the palette against the grid's texture and cuts what it draws and collides as. A shared palette's cut
    // is made once and reused by every grid that holds it at the same size, texture and columns.
    private static PaletteCut Cut(TileType[] tileTypes, int tileSize, TextureHandle? texture, int columns, bool shared)
    {
        if (shared && Palettes.TryGetValue(tileTypes, out PaletteCut? made) && made.Makes(tileSize, texture, columns))
        {
            return made;
        }

        Shape2D?[] shapes = TileGridValidation.ValidatePalette(tileTypes, tileSize);
        TileRules? rules = TileRules.Of(tileTypes);
        TileGridValidation.ValidateTexture(tileTypes, texture, columns, tileSize);
        PaletteCut cut = new(
            tileSize,
            texture,
            columns,
            rules,
            shapes,
            CutCells(tileTypes, rules, tileSize, texture, columns),
            CutAnimations(tileTypes, tileSize, texture, columns));
        if (shared)
        {
            Palettes.AddOrUpdate(tileTypes, cut);
        }

        return cut;
    }

    // Each sprite pivots on its centre. A mirrored or turned tile then stays inside its cell. A rule entry's own slot
    // draws nothing, since every cell painted with it holds one of its looks.
    private static Sprite?[] CutCells(TileType[] tileTypes, TileRules? rules, int tileSize, TextureHandle? texture, int columns)
    {
        Sprite?[] sprites = new Sprite?[rules?.Slots.Length ?? tileTypes.Length];
        if (texture is not { } cut)
        {
            return sprites;
        }

        for (int i = 0; i < tileTypes.Length; i++)
        {
            if (tileTypes[i].Cell is { } cell)
            {
                sprites[i] = CutCell(cut, cell, tileSize, columns);
            }
            else if (tileTypes[i].Frames is { } frames)
            {
                sprites[i] = CutCell(cut, frames[0].Cell, tileSize, columns);
            }
        }

        for (int slot = tileTypes.Length; slot < sprites.Length; slot++)
        {
            sprites[slot] = CutCell(cut, rules!.LookCell(slot), tileSize, columns);
        }

        return sprites;
    }

    private static Animation[] CutAnimations(TileType[] tileTypes, int tileSize, TextureHandle? texture, int columns)
    {
        int count = tileTypes.Count(static tileType => tileType.Frames is not null);
        if (count == 0)
        {
            return [];
        }

        // Validation refused frames on a grid with no texture.
        TextureHandle cut = texture!.Value;
        Animation[] animations = new Animation[count];
        int next = 0;
        for (int i = 0; i < tileTypes.Length; i++)
        {
            if (tileTypes[i].Frames is not { } frames)
            {
                continue;
            }

            Sprite[] sprites = new Sprite[frames.Length];
            int[] ticks = new int[frames.Length];
            for (int frame = 0; frame < sprites.Length; frame++)
            {
                sprites[frame] = CutCell(cut, frames[frame].Cell, tileSize, columns);
                ticks[frame] = frames[frame].Ticks;
            }

            animations[next++] = new Animation(i, new SpriteClip(sprites, ticks, loop: true));
        }

        return animations;
    }

    private static Sprite CutCell(TextureHandle texture, int cell, int tileSize, int columns) => new(
        texture,
        new TextureRegion(cell % columns * tileSize, cell / columns * tileSize, tileSize, tileSize),
        new Vector2(tileSize / 2f, tileSize / 2f));

    // One animated palette entry and the looping clip its frames make.
    internal readonly record struct Animation(int Palette, SpriteClip Clip);

    // What a valid palette makes at one tile size, cut from one texture of so many columns. Every grid holding it
    // reads it and none writes it.
    private sealed record PaletteCut(int TileSize, TextureHandle? Texture, int Columns, TileRules? Rules, Shape2D?[] Shapes, Sprite?[] Sprites, Animation[] Animations)
    {
        internal bool Makes(int tileSize, TextureHandle? texture, int columns) => TileSize == tileSize && Texture == texture && Columns == columns;
    }
}
