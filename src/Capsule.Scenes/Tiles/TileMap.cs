using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tiles;

/// <summary>A tile grid anchored at the world origin, with cell (0, 0) at the top-left.</summary>
/// <remarks>
/// Writing <see cref="Entity.Position"/>, <see cref="Entity.Rotation"/> or <see cref="Entity.Scale"/>
/// throws, and the map takes no <see cref="Entity.Parent"/>. Its own children's local values are world
/// values. Every tile draws in the map's own <see cref="Entity.ZIndex"/> band and follows its
/// <see cref="Entity.ScrollFactor"/>. A map whose grid has no texture draws nothing and adds no draw
/// work. The map collides only through a <see cref="TileMapCollider2D"/> added to it.
/// <para>
/// The map shares its grid's cells, and an edit copies only the block of 16 by 16 tiles it changes. The
/// <see cref="TileGrid"/> is never written.
/// </para>
/// <para>
/// A scene document places one by the type key <c>tile-map</c>. The map is built from its authorable
/// members before a subclass constructor body runs.
/// </para>
/// </remarks>
[TypeKey(Key)]
public class TileMap : Entity
{
    // The type key every logic assembly registers the engine's map under.
    internal const string Key = "tile-map";

    private TileGrid _grid;

    // The palette index and TileTransform of every painted cell, which drawing, reading and the collider all
    // use. They are the grid's own until the first edit, and then the map's copy, which copies a block as it writes it.
    private CellBlocks _cells;

    // Null for a map whose grid has no texture.
    private VisibleTiles? _tiles;
    private Material? _material;

    /// <summary>The edge length of one tile in world units, which also equals its edge in atlas pixels.</summary>
    [Authorable(Required = true)]
    public int TileSize { get; protected set; }

    /// <summary>Grid width in tiles.</summary>
    [Authorable(Required = true)]
    public int Width { get; protected set; }

    /// <summary>Grid height in tiles.</summary>
    [Authorable(Required = true)]
    public int Height { get; protected set; }

    /// <summary>The texture every drawn tile is cut from, or null for a map that draws nothing.</summary>
    [Authorable]
    protected TextureHandle? Texture { get; set; }

    /// <summary>
    /// How many cells wide <see cref="Texture"/> is, and 0 without one. Cell numbers run across a row of this
    /// many and then wrap to the next row.
    /// </summary>
    [Authorable]
    protected int Columns { get; set; }

    /// <summary>
    /// The palette every tile indexes. Index 0 is a plain <see cref="TileType"/> named
    /// <see cref="TileGrid.EmptyTileName"/>, and names are unique.
    /// </summary>
    /// <remarks>
    /// Each entry composes a <see cref="TileType"/>, or the subclass its <c>type</c> names. <see cref="TileAt(int, int)"/>
    /// and <c>TileContact2D.Type</c> return that instance, read as <c>map.TileAt(x, y).Name</c> or matched as
    /// <c>map.TileAt(x, y) is Ice ice</c>.
    /// </remarks>
    [Authorable(Required = true)]
    protected TileType[] TileTypes { get; set; } = [];

    /// <summary>
    /// Every tile's palette index, <see cref="Width"/> * <see cref="Height"/> of them, row by row from the top-left.
    /// The build ships them compiled into blocks, and a map composed from a built document finds this member empty.
    /// </summary>
    [Authorable]
    protected ReadOnlyMemory<int> Tiles { get; set; }

    /// <summary>
    /// How each tile in <see cref="Tiles"/> is mirrored or turned, as its <see cref="TileTransform"/> value, or
    /// null for every tile as authored. 1 mirrors it left to right, 2 top to bottom, 4 swaps its axes before
    /// either, and the sum combines them.
    /// </summary>
    [Authorable]
    protected ReadOnlyMemory<int>? Transforms { get; set; }

    /// <summary>
    /// Whether the map is built with a <see cref="TileMapCollider2D"/>, false by default. The palette must then
    /// name a layer, and the map's scroll factor stay one.
    /// </summary>
    /// <remarks>Code finds the collider as the map's component, through <c>TryGet</c>.</remarks>
    [Authorable]
    protected bool Collider { get; set; }

    // The map's collider, set while one is attached.
    internal TileMapCollider2D? AttachedCollider { get; set; }

    /// <param name="grid">The grid to hold and draw. Its palette decides what each tile looks like.</param>
    public TileMap(TileGrid grid)
        : base(Vector2.Zero)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Anchored = true;
        Build(grid);
    }

    /// <summary>A map built from the members a scene document authors.</summary>
    /// <param name="spawn">The placement, which leaves the map at the world origin, unturned and unscaled.</param>
    /// <exception cref="ArgumentException">The placement moves, turns or scales the map, or its members describe no valid grid.</exception>
    public TileMap(EntitySpawn spawn)
        : base(spawn)
    {
        if (spawn.Position != Vector2.Zero || spawn.Rotation != 0f || spawn.Scale != Vector2.One)
        {
            throw new ArgumentException(
                "A tile map is anchored at the world origin, unturned and unscaled. Drop its x, y, rotation and scale, and turn single tiles with transforms.",
                nameof(spawn));
        }

        Anchored = true;
        Build(CompiledCells(spawn.Members) is { } cells
            ? new TileGrid(TileSize, Width, Height, TileTypes, cells, Texture, Columns)
            : new TileGrid(TileSize, Width, Height, TileTypes, Tiles, Texture, Columns, Transforms));
        if (Collider)
        {
            Add(new TileMapCollider2D());
        }
    }

    /// <summary>How many world units the grid spans, measured from the world origin.</summary>
    public Vector2 Size => new(Width * TileSize, Height * TileSize);

    /// <summary>The material every tile draws with, and null for the engine's own sprite shader, the default.</summary>
    /// <remarks>
    /// The map switches to it once, not per tile. <see cref="Entity.Tint"/> and <see cref="Entity.Flash"/>
    /// still apply. A material set in the scene's constructor loads with the scene, and one set later
    /// loads on its first draw. A map whose grid has no texture keeps the material and never loads it.
    /// </remarks>
    public Material? Material
    {
        get => _material;
        set
        {
            _material = value;
            _tiles?.Material = value;
        }
    }

    internal TileGrid Grid => _grid;

    // The cells a collision grid reads in place.
    internal CellBlocks Cells => _cells;

    /// <summary>
    /// Returns the palette entry at a tile coordinate, and the entry named <see cref="TileGrid.EmptyTileName"/>
    /// where the map is empty. Every cell painted with one entry returns the same instance.
    /// </summary>
    /// <example>
    /// <code>
    /// if (map.TileAt(x, y) is Ice ice) { ... }
    /// bool open = map.TileAt(x, y).Name == TileGrid.EmptyTileName;
    /// </code>
    /// </example>
    public TileType TileAt(int x, int y)
    {
        _cells.RequireOnGrid(x, y);

        return _grid.TileTypes[_cells.ValueAt(x, y)];
    }

    /// <summary>
    /// Returns the palette entry of the cell a world position falls in, or null where the position lies
    /// outside the map.
    /// </summary>
    /// <example>
    /// <code>
    /// bool onIce = map.TileAt(Position + new Vector2(0f, 1f)) is Ice;
    /// </code>
    /// </example>
    public TileType? TileAt(Vector2 position)
    {
        (int x, int y) = CellAt(position);

        return (uint)x < (uint)Width && (uint)y < (uint)Height ? _grid.TileTypes[_cells.ValueAt(x, y)] : null;
    }

    /// <summary>Returns how the tile at a tile coordinate is mirrored or turned.</summary>
    public TileTransform TransformAt(int x, int y)
    {
        _cells.RequireOnGrid(x, y);

        return (TileTransform)_cells.VariantAt(x, y);
    }

    // The surface velocity of the tile a cell holds now, turned the way the cell faces.
    internal Vector2 SurfaceVelocityAt(int x, int y)
    {
        _cells.RequireOnGrid(x, y);

        // A direction maps as a point in a tile of size 0, where each flip negates its axis.
        return TileTransforms.Apply(_grid.TileTypes[_cells.ValueAt(x, y)].SurfaceVelocity, 0f, (TileTransform)_cells.VariantAt(x, y));
    }

    /// <summary>
    /// Returns the tile coordinate of the cell a world position falls in. The cell may lie outside the
    /// map, where <see cref="TileAt(int, int)"/> throws.
    /// </summary>
    /// <example>
    /// <code>
    /// var (x, y) = map.CellAt(Position);
    /// string tile = map.TileAt(x, y).Name;
    /// </code>
    /// </example>
    public (int X, int Y) CellAt(Vector2 position)
    {
        Guard.Finite(position, nameof(position));

        return (CollisionGrid2D.FloorDiv(position.X, TileSize), CollisionGrid2D.FloorDiv(position.Y, TileSize));
    }

    /// <summary>
    /// Returns the world position of the centre of the cell at a tile coordinate, which may lie outside
    /// the map.
    /// </summary>
    /// <example>
    /// <code>
    /// var (x, y) = map.CellAt(Position);
    /// Vector2 topCentre = map.CellCenter(x, y) - new Vector2(0f, map.TileSize / 2f);
    /// </code>
    /// </example>
    public Vector2 CellCenter(int x, int y)
    {
        float half = TileSize / 2f;

        return new Vector2((x * (float)TileSize) + half, (y * (float)TileSize) + half);
    }

    /// <summary>
    /// Clears the tile at a tile coordinate to <see cref="TileGrid.EmptyTileName"/> with
    /// <see cref="TileTransform.None"/>.
    /// </summary>
    public void RemoveTile(int x, int y) => SetTile(x, y, TileGrid.EmptyTileName);

    /// <summary>
    /// Changes the tile at a tile coordinate to the palette entry named <paramref name="name"/>, facing
    /// the way <paramref name="transform"/> says. This sets what the cell draws and collides as.
    /// </summary>
    /// <param name="x">The tile column.</param>
    /// <param name="y">The tile row.</param>
    /// <param name="name">
    /// A tile type name from the grid's palette. <see cref="TileGrid.EmptyTileName"/> clears the cell.
    /// </param>
    /// <param name="transform">
    /// How the tile is mirrored or turned. The default paints it as authored, whichever way the cell
    /// faced before.
    /// </param>
    /// <exception cref="ArgumentException">The palette has no tile type named <paramref name="name"/>.</exception>
    /// <example>
    /// <code>
    /// map.SetTile(x, y, "slope", TileTransform.FlipX);
    /// </code>
    /// </example>
    public void SetTile(int x, int y, string name, TileTransform transform = TileTransform.None)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!TileTransforms.IsDefined(transform))
        {
            throw new ArgumentOutOfRangeException(
                nameof(transform),
                transform,
                $"transform is {(byte)transform}. Use a TileTransform in 0..{TileTransforms.Count - 1}.");
        }

        _cells.RequireOnGrid(x, y);
        int palette = PaletteIndexOf(name);
        if (_cells.ValueAt(x, y) == palette && _cells.VariantAt(x, y) == (int)transform)
        {
            return;
        }

        // The map edits its own copy of the cells, never the grid's. The collider's grid reads the copy from then on.
        if (_cells == _grid.Cells)
        {
            _cells = _grid.Cells.Share();
            AttachedCollider?.Rebind(_cells);
        }

        _cells.Set(x, y, palette, (int)transform);
        AttachedCollider?.Refresh(x, y);
    }

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (_grid.Texture is { } texture && texture != default)
        {
            assets.Add(texture);
        }
    }

    // A map with animated entries draws from its own copy of the table. Two maps may share a grid and step apart.
    // A grid with no texture draws nothing, and its map adds no renderer.
    [MemberNotNull(nameof(_grid), nameof(_cells))]
    private void Build(TileGrid grid)
    {
        _grid = grid;
        _cells = grid.Cells;
        TileSize = grid.TileSize;
        Width = grid.Width;
        Height = grid.Height;
        if (grid.Texture is null)
        {
            return;
        }

        ReadOnlyMemory<Sprite?> sprites = grid.Sprites;
        if (!grid.Animations.IsEmpty)
        {
            Sprite?[] own = grid.Sprites.ToArray();
            sprites = own;
            Add(new TileAnimator(grid.Animations, own));
        }

        _tiles = new VisibleTiles(this, grid, sprites);
        Add(_tiles);
    }

    // The cells a compiled document's blocks hold, decoded once and shared by every map composed from it, or null
    // for dense tiles.
    private CellBlocks? CompiledCells(AuthoredMembers members)
    {
        if (!members.Has(CompiledTiles.Key))
        {
            return null;
        }

        if (members.Has("tiles") || members.Has("transforms"))
        {
            throw new ArgumentException("The map authors both compiled blocks and dense tiles or transforms. Keep tiles and transforms, which the build compiles.", nameof(members));
        }

        // The grid refuses a size it cannot hold, naming the defect.
        if (Width <= 0 || Height <= 0)
        {
            return null;
        }

        (int width, int height) = (Width, Height);

        return members.Built(CompiledTiles.Key, blocks => CompiledTiles.Read(blocks.Span, width, height));
    }

    private int PaletteIndexOf(string name)
    {
        ReadOnlySpan<TileType> palette = _grid.TileTypes;
        for (int index = 0; index < palette.Length; index++)
        {
            if (string.Equals(palette[index].Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new ArgumentException(
            $"The palette has no tile type named \"{name}\". Use one of: {string.Join(", ", palette.ToArray().Select(static tileType => tileType.Name))}.",
            nameof(name));
    }

    // Steps each animated entry's clip and writes its current frame into the map's table. It steps with
    // the map, and a held map holds its frames.
    private sealed class TileAnimator(ReadOnlyMemory<TileGrid.Animation> animations, Sprite?[] sprites) : Component
    {
        private readonly AnimationPlayback[] _cursors = new AnimationPlayback[animations.Length];

        protected internal override void OnStep(in StepContext context)
        {
            ReadOnlySpan<TileGrid.Animation> entries = animations.Span;
            for (int i = 0; i < entries.Length; i++)
            {
                SpriteClip clip = entries[i].Clip;
                _cursors[i].Step(clip.FrameTicks, loop: true);
                sprites[entries[i].Palette] = clip.Frames[_cursors[i].FrameIndex];
            }
        }
    }

    // The sprite table is indexed by palette. The map's cells are read on each draw, since an edit replaces them.
    private sealed class VisibleTiles(TileMap map, TileGrid grid, ReadOnlyMemory<Sprite?> sprites)
        : Renderer
    {
        internal override bool Steps => false;

        // Draws only the cells the camera's swept bounds reach, row by row, and skips each stretch of a row whose
        // block holds nothing. Every cell drawn meets the bounds the view culls against, and the view's own test
        // is skipped.
        protected internal override void Draw(FrameView view)
        {
            ArgumentNullException.ThrowIfNull(view);

            (int minX, int minY, int maxX, int maxY) = VisibleBounds(view.Camera);
            if (minX >= maxX)
            {
                return;
            }

            CellBlocks cells = map._cells;
            ReadOnlySpan<Sprite?> table = sprites.Span;
            Vector2 size = new(grid.TileSize, grid.TileSize);
            int firstBlock = minX >> CellBlocks.BlockShift;
            int lastBlock = (maxX - 1) >> CellBlocks.BlockShift;
            for (int y = minY; y < maxY; y++)
            {
                for (int bx = firstBlock; bx <= lastBlock; bx++)
                {
                    if (cells.BlockAt(bx, y >> CellBlocks.BlockShift) is not { } block)
                    {
                        continue;
                    }

                    int end = Math.Min(maxX, (bx + 1) << CellBlocks.BlockShift);
                    for (int x = Math.Max(minX, bx << CellBlocks.BlockShift); x < end; x++)
                    {
                        int cell = CellBlocks.CellOf(x, y);
                        if (table[block.Values[cell]] is not { } sprite)
                        {
                            continue;
                        }

                        // Terrain never moves, and its frames pivot on their centre. The cell's centre serves
                        // as both ends of the interpolation, and a mirror or turn stays inside the cell.
                        Vector2 centre = map.CellCenter(x, y);
                        ref readonly TileTransforms.Pose pose = ref TileTransforms.PoseOf((TileTransform)block.VariantAt(cell));
                        view.AddUnculled(new SpriteIntent(
                            sprite,
                            centre,
                            centre,
                            pose.Rotation,
                            pose.Rotation,
                            size,
                            pose.FlipX,
                            pose.FlipY,
                            ColorRgba.White));
                    }
                }
            }
        }

        // Uses the camera's swept bounds instead of its settled region, because the renderer interpolates
        // the camera and a tile the camera only reaches mid-step must still be drawn this frame.
        private (int MinX, int MinY, int MaxX, int MaxY) VisibleBounds(CameraView camera)
        {
            Rect swept = camera.SweptBounds;

            if (!(camera.Size.X > 0f) || !(camera.Size.Y > 0f) || swept.IsEmpty)
            {
                return default;
            }

            return (
                (int)MathF.Floor(ToTiles(swept.Left, grid.Width)),
                (int)MathF.Floor(ToTiles(swept.Top, grid.Height)),
                (int)MathF.Ceiling(ToTiles(swept.Right, grid.Width)),
                (int)MathF.Ceiling(ToTiles(swept.Bottom, grid.Height)));
        }

        // A world coordinate in tiles, clamped to the grid's extent on that axis.
        private float ToTiles(float boundary, int limit) => Math.Clamp(boundary / grid.TileSize, 0f, limit);
    }
}
