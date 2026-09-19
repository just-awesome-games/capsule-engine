using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tiles;

/// <summary>
/// A tile grid anchored at the world origin. Its cells are world coordinates, so writing
/// <see cref="Entity.Position"/>, <see cref="Entity.Rotation"/> or <see cref="Entity.Scale"/> throws and
/// it takes no <see cref="Entity.Parent"/>. It may still place children of its own, whose local values
/// are world values. It draws every palette entry that names a cell of the grid's texture, and registers
/// one <see cref="GridCollider2D"/> with the scene's world when any tile type collides. Every tile draws
/// in the map's own <see cref="Entity.ZIndex"/> band, so one value puts every tile behind or in front
/// of the rest of the scene. Tiles follow the map's <see cref="Entity.ScrollFactor"/>,
/// which a grid with a colliding palette rejects.
/// </summary>
public sealed class TileMap : Entity
{
    private readonly TileGrid _grid;

    private CollisionWorld2D? _world;

    /// <param name="grid">The grid to hold and draw. Its palette decides what each tile looks like.</param>
    public TileMap(TileGrid grid)
        : base(Vector2.Zero)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Anchored = true;
        _grid = grid;
        Size = new Vector2(grid.Width * grid.TileSize, grid.Height * grid.TileSize);

        Add(new VisibleTiles(grid));
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
    /// This grid's collider in the scene's world, or null when the map is in no scene or no tile type in
    /// its palette collides. Each cell carries the collision layer its tile type was authored on. A tile
    /// type name identifies the tile and does not name a layer.
    /// </summary>
    public GridCollider2D? Collision { get; private set; }

    /// <summary>Returns the palette index at a tile coordinate, and 0 where the grid is empty.</summary>
    public int TileAt(int x, int y) => _grid.TileAt(x, y);

    /// <summary>Returns the tile type name at a tile coordinate.</summary>
    public string TileTypeAt(int x, int y) => _grid.TileTypeAt(x, y);

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
                palette[index].CollidableFaces);
        }

        Collision = _world.AddGrid(_grid.TileSize, _grid.Width, _grid.Height, _grid.Cells, profiles, this);
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

    private sealed class VisibleTiles(TileGrid grid) : Renderer
    {
        public override void Draw(FrameView view)
        {
            ArgumentNullException.ThrowIfNull(view);

            (int minX, int minY, int maxX, int maxY) = VisibleBounds(view.Camera);
            ReadOnlySpan<int> tiles = grid.Tiles;
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

    // Draws the grid's faces on the Colliders channel. It draws only the faces a query can meet, and only
    // for the cells the camera's view reaches plus one cell of margin, which keeps a large grid cheap
    // to draw. The derived state culls the shared faces inside a solid run, and a wall shows as its
    // outline. The map is anchored, so its faces carry no motion.
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

    private static void DrawFace(GridCollider2D grid, int x, int y, CellState2D state, CellState2D face)
    {
        if ((state & face) != 0)
        {
            Aabb2D edge = grid.FaceEdge(x, y, face);
            DebugDraw.Line(DebugDraw.Colliders, edge.Min, edge.Max);
        }
    }
}
