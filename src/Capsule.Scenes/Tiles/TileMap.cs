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
/// Its cells sit at fixed world coordinates. Writing <see cref="Entity.Position"/>,
/// <see cref="Entity.Rotation"/> or <see cref="Entity.Scale"/> throws, and the map takes no
/// <see cref="Entity.Parent"/>. It may still place children of its own, whose local values are
/// world values. It draws every palette entry that names a cell of the grid's texture. Every tile
/// draws in the map's own <see cref="Entity.ZIndex"/> band and follows its
/// <see cref="Entity.ScrollFactor"/>. The map collides only through a <see cref="TileMapCollider2D"/>
/// added to it, and a palette entry's layer still reaches <see cref="TileAt"/> on a map without one.
/// <para>
/// The map copies the grid's cells when it is built, and <see cref="SetTile"/> changes that copy.
/// The <see cref="TileGrid"/> handed in is never written. A scene rebuilt from it starts as
/// authored.
/// </para>
/// </remarks>
public sealed class TileMap : Entity
{
    private readonly TileGrid _grid;

    // The map's own palette indices and transforms, row-major. Drawing, reading and SetTile all use
    // this copy.
    private readonly int[] _cells;
    private readonly TileTransform[] _transforms;

    private readonly VisibleTiles _tiles;

    // The map's collider, set while one is attached.
    internal TileMapCollider2D? Collider { get; set; }

    /// <param name="grid">The grid to hold and draw. Its palette decides what each tile looks like.</param>
    public TileMap(TileGrid grid)
        : base(Vector2.Zero)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Anchored = true;
        _grid = grid;
        _cells = grid.Tiles.ToArray();
        _transforms = grid.Transforms.ToArray();
        Size = new Vector2(grid.Width * grid.TileSize, grid.Height * grid.TileSize);

        // A map with animated entries draws from its own copy of the table. Two maps may share a grid
        // and step apart.
        ReadOnlyMemory<Sprite?> sprites = grid.Sprites;
        if (!grid.Animations.IsEmpty)
        {
            Sprite?[] own = grid.Sprites.ToArray();
            sprites = own;
            Add(new TileAnimator(grid.Animations, own));
        }

        _tiles = new VisibleTiles(grid, _cells, _transforms, sprites);
        Add(_tiles);
    }

    /// <summary>The edge length of one tile, taken from <see cref="TileGrid.TileSize"/>.</summary>
    public int TileSize => _grid.TileSize;

    /// <summary>Grid width in tiles.</summary>
    public int Width => _grid.Width;

    /// <summary>Grid height in tiles.</summary>
    public int Height => _grid.Height;

    /// <summary>How many world units the grid spans, measured from the world origin.</summary>
    public Vector2 Size { get; }

    /// <summary>The material every tile draws with, and null for the engine's own sprite shader, the default.</summary>
    /// <remarks>
    /// The map switches to it once, not per tile. <see cref="Entity.Tint"/> and <see cref="Entity.Flash"/>
    /// still apply. A material set in the scene's constructor loads with the scene, and one set later
    /// loads on its first draw.
    /// </remarks>
    public Material? Material
    {
        get => _tiles.Material;
        set => _tiles.Material = value;
    }

    internal TileGrid Grid => _grid;

    internal ReadOnlySpan<int> Cells => _cells;

    internal ReadOnlySpan<TileTransform> Transforms => _transforms;

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
    public TileType TileAt(int x, int y) => _grid.TileTypes[_cells[IndexOf(x, y)]];

    /// <summary>Returns how the tile at a tile coordinate is mirrored or turned.</summary>
    public TileTransform TransformAt(int x, int y) => _transforms[IndexOf(x, y)];

    /// <summary>
    /// Returns the tile coordinate of the cell a world position falls in. The cell may lie outside the
    /// map, where <see cref="TileAt"/> throws.
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

        int index = IndexOf(x, y);
        int palette = PaletteIndexOf(name);
        if (_cells[index] == palette && _transforms[index] == transform)
        {
            return;
        }

        _cells[index] = palette;
        _transforms[index] = transform;
        Collider?.SetCell(x, y, palette, transform);
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

    private int IndexOf(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return (y * Width) + x;
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

    // The sprite table is indexed by palette.
    private sealed class VisibleTiles(TileGrid grid, int[] cells, TileTransform[] transforms, ReadOnlyMemory<Sprite?> sprites)
        : Renderer
    {
        internal override bool Steps => false;

        protected internal override void Draw(FrameView view)
        {
            ArgumentNullException.ThrowIfNull(view);

            (int minX, int minY, int maxX, int maxY) = VisibleBounds(view.Camera);
            ReadOnlySpan<int> tiles = cells;
            ReadOnlySpan<TileTransform> facings = transforms;
            ReadOnlySpan<Sprite?> table = sprites.Span;
            Vector2 size = new(grid.TileSize, grid.TileSize);
            float half = grid.TileSize / 2f;

            for (int y = minY; y < maxY; y++)
            {
                int row = y * grid.Width;
                for (int x = minX; x < maxX; x++)
                {
                    if (table[tiles[row + x]] is not { } sprite)
                    {
                        continue;
                    }

                    // Terrain never moves, and its frames pivot on their centre. The cell's centre serves
                    // as both ends of the interpolation, and a mirror or turn stays inside the cell.
                    Vector2 centre = new((x * grid.TileSize) + half, (y * grid.TileSize) + half);
                    ref readonly TileTransforms.Pose pose = ref TileTransforms.PoseOf(facings[row + x]);
                    view.Add(new SpriteIntent(
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
                (int)MathF.Floor(Cells(swept.Left, grid.Width)),
                (int)MathF.Floor(Cells(swept.Top, grid.Height)),
                (int)MathF.Ceiling(Cells(swept.Right, grid.Width)),
                (int)MathF.Ceiling(Cells(swept.Bottom, grid.Height)));
        }

        // A world coordinate in tiles, clamped to the grid's extent on that axis.
        private float Cells(float boundary, int limit) => Math.Clamp(boundary / grid.TileSize, 0f, limit);
    }
}
