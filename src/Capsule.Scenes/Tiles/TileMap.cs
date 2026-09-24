using System.Numerics;
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
/// world values. It draws every palette entry that names a cell of the grid's texture, and
/// registers one <see cref="GridCollider2D"/> with the scene's world when any tile type collides.
/// Every tile draws in the map's own <see cref="Entity.ZIndex"/> band. Tiles follow the map's
/// <see cref="Entity.ScrollFactor"/>, which must stay one when any tile type collides.
/// <para>
/// The map copies the grid's cells when it is built, and <see cref="SetTile"/> changes that copy.
/// The <see cref="TileGrid"/> handed in is never written. A scene rebuilt from it starts as
/// authored.
/// </para>
/// </remarks>
public sealed class TileMap : Entity
{
    private readonly TileGrid _grid;

    // The map's own palette indices, row-major. Drawing, reading and SetTile all use this copy.
    private readonly int[] _cells;

    private CollisionWorld2D? _world;

    /// <param name="grid">The grid to hold and draw. Its palette decides what each tile looks like.</param>
    public TileMap(TileGrid grid)
        : base(Vector2.Zero)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Anchored = true;
        _grid = grid;
        _cells = grid.Tiles.ToArray();
        Size = new Vector2(grid.Width * grid.TileSize, grid.Height * grid.TileSize);

        Add(new VisibleTiles(grid, _cells));
    }

    /// <summary>The edge length of one tile, taken from <see cref="TileGrid.TileSize"/>.</summary>
    public int TileSize => _grid.TileSize;

    /// <summary>Grid width in tiles.</summary>
    public int Width => _grid.Width;

    /// <summary>Grid height in tiles.</summary>
    public int Height => _grid.Height;

    /// <summary>How many world units the grid spans, measured from the world origin.</summary>
    public Vector2 Size { get; }

    internal override bool Collides => _grid.Collides;

    /// <summary>
    /// This grid's collider in the scene's world, or null when the map is in no scene or no tile
    /// type in its palette collides. Each cell carries the collision layer its tile type was
    /// authored on.
    /// </summary>
    /// <remarks>A tile type name identifies the tile and does not name a layer.</remarks>
    public GridCollider2D? Collision { get; private set; }

    /// <summary>
    /// Returns the tile type name at a tile coordinate, and <see cref="TileGrid.EmptyTileType"/> where
    /// the map is empty.
    /// </summary>
    public string TileAt(int x, int y) => _grid.TileTypes[_cells[IndexOf(x, y)]].Type;

    /// <summary>
    /// Returns the tile coordinate of the cell a world position falls in. The cell may lie outside the
    /// map, where <see cref="TileAt"/> throws.
    /// </summary>
    /// <example>
    /// <code>
    /// var (x, y) = map.CellAt(Position);
    /// string tile = map.TileAt(x, y);
    /// </code>
    /// </example>
    public (int X, int Y) CellAt(Vector2 position)
    {
        Guard.Finite(position, nameof(position));

        return (GridCollider2D.FloorDiv(position.X, TileSize), GridCollider2D.FloorDiv(position.Y, TileSize));
    }

    /// <summary>Clears the tile at a tile coordinate to <see cref="TileGrid.EmptyTileType"/>.</summary>
    public void RemoveTile(int x, int y) => SetTile(x, y, TileGrid.EmptyTileType);

    /// <summary>
    /// Changes the tile at a tile coordinate to the palette entry named <paramref name="type"/>, which
    /// sets what the cell draws and collides as.
    /// </summary>
    /// <param name="x">The tile column.</param>
    /// <param name="y">The tile row.</param>
    /// <param name="type">
    /// A tile type name from the grid's palette. <see cref="TileGrid.EmptyTileType"/> clears the cell.
    /// </param>
    /// <exception cref="ArgumentException">The palette has no tile type named <paramref name="type"/>.</exception>
    public void SetTile(int x, int y, string type)
    {
        ArgumentNullException.ThrowIfNull(type);

        int index = IndexOf(x, y);
        int palette = PaletteIndexOf(type);
        if (_cells[index] == palette)
        {
            return;
        }

        _cells[index] = palette;
        Collision?.SetCell(x, y, palette);
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

    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        if (!_grid.Collides)
        {
            return;
        }

        _world = Scene.Collision;

        ReadOnlySpan<TileDefinition> palette = _grid.TileTypes;
        CellProfile2D[] profiles = new CellProfile2D[palette.Length];
        for (int index = 0; index < profiles.Length; index++)
        {
            profiles[index] = new CellProfile2D(
                palette[index].Layer is { } layer ? _world.Layer(layer) : null,
                palette[index].Shape,
                palette[index].OneWay,
                palette[index].SolidSides);
        }

        Collision = _world.AddGrid(_grid.TileSize, _grid.Width, _grid.Height, _cells, profiles, this);
    }

    /// <inheritdoc/>
    protected internal override void OnRemovedFromScene()
    {
        if (Collision is { } collider)
        {
            _world!.Remove(collider);
        }

        Collision = null;
        _world = null;
    }

    private int IndexOf(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return (y * Width) + x;
    }

    private int PaletteIndexOf(string type)
    {
        ReadOnlySpan<TileDefinition> palette = _grid.TileTypes;
        for (int index = 0; index < palette.Length; index++)
        {
            if (string.Equals(palette[index].Type, type, StringComparison.Ordinal))
            {
                return index;
            }
        }

        string[] names = new string[palette.Length];
        for (int index = 0; index < names.Length; index++)
        {
            names[index] = palette[index].Type;
        }

        throw new ArgumentException(
            $"The palette has no tile type \"{type}\". Use one of: {string.Join(", ", names)}.",
            nameof(type));
    }

    private sealed class VisibleTiles(TileGrid grid, int[] cells) : Renderer
    {
        protected internal override void Draw(FrameView view)
        {
            ArgumentNullException.ThrowIfNull(view);

            (int minX, int minY, int maxX, int maxY) = VisibleBounds(view.Camera);
            ReadOnlySpan<int> tiles = cells;
            ReadOnlySpan<Sprite?> sprites = grid.Sprites;
            Vector2 size = new(grid.TileSize, grid.TileSize);

            for (int y = minY; y < maxY; y++)
            {
                int row = y * grid.Width;
                for (int x = minX; x < maxX; x++)
                {
                    if (sprites[tiles[row + x]] is not { } sprite)
                    {
                        continue;
                    }

                    // Terrain never moves or flips, and its frames anchor at their own corner, so the
                    // cell's corner serves as both ends of the interpolation.
                    Vector2 corner = new(x * grid.TileSize, y * grid.TileSize);
                    view.Add(new SpriteIntent(
                        sprite,
                        corner,
                        corner,
                        PreviousRotation: 0f,
                        Rotation: 0f,
                        size,
                        FlipX: false,
                        FlipY: false,
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
                StartCoordinate(swept.Left, grid.TileSize, grid.Width),
                StartCoordinate(swept.Top, grid.TileSize, grid.Height),
                EndCoordinate(swept.Right, grid.TileSize, grid.Width),
                EndCoordinate(swept.Bottom, grid.TileSize, grid.Height));
        }

        private static int StartCoordinate(float boundary, int tileSize, int limit)
        {
            float coordinate = boundary / tileSize;
            if (coordinate <= 0f)
            {
                return 0;
            }

            if (coordinate >= limit)
            {
                return limit;
            }

            return (int)MathF.Floor(coordinate);
        }

        private static int EndCoordinate(float boundary, int tileSize, int limit)
        {
            float coordinate = boundary / tileSize;
            if (coordinate <= 0f)
            {
                return 0;
            }

            if (coordinate >= limit)
            {
                return limit;
            }

            return (int)MathF.Ceiling(coordinate);
        }
    }

    // Draws the grid's live edges on the Colliders channel, only for the cells the camera's view reaches
    // plus one cell of margin, which keeps a large grid cheap to draw. The derived state culls the shared
    // sides inside a solid run, and a wall shows as its outline. The map is anchored, so its edges carry
    // no motion.
    /// <inheritdoc/>
    protected internal override void OnDebugDraw()
    {
        if (Collision is not { } grid || Scene is not { } scene)
        {
            return;
        }

        Rect view = scene.Camera.VisibleRegion;
        if (view.IsEmpty)
        {
            return;
        }

        int minX = Math.Max(GridCollider2D.FloorDiv(view.Left, grid.CellSize) - 1, 0);
        int minY = Math.Max(GridCollider2D.FloorDiv(view.Top, grid.CellSize) - 1, 0);
        int maxX = Math.Min(LastCell(view.Right, grid.CellSize) + 1, grid.Width - 1);
        int maxY = Math.Min(LastCell(view.Bottom, grid.CellSize) + 1, grid.Height - 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                CellState2D state = grid.StateAt(x, y);
                if ((state & CellState2D.Edges) != 0)
                {
                    DrawEdges(grid, x, y, state);
                    continue;
                }

                DrawFace(grid, x, y, state, CellState2D.FaceMinX);
                DrawFace(grid, x, y, state, CellState2D.FaceMaxX);
                DrawFace(grid, x, y, state, CellState2D.FaceMinY);
                DrawFace(grid, x, y, state, CellState2D.FaceMaxY);
            }
        }
    }

    // Returns the last cell a half-open rect's high edge reaches. That is the cell the edge lies in, or
    // the cell before it when the edge sits exactly on a boundary and so falls outside the rect.
    private static int LastCell(float edge, int cellSize)
    {
        int cell = GridCollider2D.FloorDiv(edge, cellSize);

        return cell * cellSize == edge ? cell - 1 : cell;
    }

    private static void DrawEdges(GridCollider2D grid, int x, int y, CellState2D state)
    {
        ReadOnlySpan<CellEdge2D> edges = grid.EdgesAt(x, y);
        Vector2 corner = grid.CellCorner(x, y);
        for (int index = 0; index < edges.Length; index++)
        {
            if ((state & GridCollider2D.EdgeBit(index)) != 0)
            {
                DebugDraw.Line(DebugDraw.Colliders, corner + edges[index].Start, corner + edges[index].End);
            }
        }
    }

    private static void DrawFace(GridCollider2D grid, int x, int y, CellState2D state, CellState2D face)
    {
        if ((state & face) != 0)
        {
            Aabb2D edge = grid.FaceEdge(x, y, face);
            DebugDraw.Line(DebugDraw.Colliders, edge.Min, edge.Max);
        }
    }
}
