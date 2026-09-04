using System.Numerics;
using Capsule.Collision;
using Capsule.Rendering;
using Capsule.Scenes.Rendering;

namespace Capsule.Scenes.Tiles;

/// <summary>
/// A tile grid anchored at the world origin — its cells are world coordinates, so its
/// <see cref="Entity.Position"/> cannot be written. It draws every palette entry that names a cell
/// of the grid's texture and, where any tile type collides, registers one
/// <see cref="GridCollider2D"/> with the scene's world.
/// </summary>
public sealed class TileMap : Entity
{
    private readonly TileGrid _grid;

    private CollisionWorld2D? _world;

    /// <param name="grid">The grid to hold and to draw; its palette decides what a tile looks like.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public TileMap(TileGrid grid)
        : base(Vector2.Zero)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Anchored = true;
        _grid = grid;
        Size = new Vector2(grid.Width * grid.TileSize, grid.Height * grid.TileSize);

        Add(new VisibleTiles(grid));
    }

    /// <summary>World units a tile spans on each axis.</summary>
    public int TileSize => _grid.TileSize;

    /// <summary>Grid width in tiles.</summary>
    public int Width => _grid.Width;

    /// <summary>Grid height in tiles.</summary>
    public int Height => _grid.Height;

    /// <summary>World units the grid spans, from the world origin.</summary>
    public Vector2 Size { get; }

    /// <summary>
    /// This grid's collider in the scene's world, or null when it is in no scene or no tile type
    /// in its palette collides. Its cells carry the collision layer their tile type was authored
    /// on; a tile type name is identity, never a layer.
    /// </summary>
    public GridCollider2D? Collision { get; private set; }

    /// <summary>The palette index at a tile coordinate; 0 where the grid is empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public int TileAt(int x, int y) => _grid.TileAt(x, y);

    /// <summary>The tile type name at a tile coordinate.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public string TileTypeAt(int x, int y) => _grid.TileTypeAt(x, y);

    /// <inheritdoc/>
    protected internal override void OnAddedToScene()
    {
        if (!_grid.Collides)
        {
            return;
        }

        _world = Scene!.Collision;

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

                    // Terrain never moves and never flips, and its frames are anchored at their
                    // own corner, so the cell's corner is both endpoints of the interpolation.
                    Vector2 corner = new(x * grid.TileSize, y * grid.TileSize);
                    view.Add(new SpriteIntent(
                        sprite, corner, corner, size, FlipX: false, FlipY: false, ColorRgba.White));
                }
            }
        }

        // The camera's swept bounds, not its settled region: the renderer interpolates the
        // camera, so a tile the camera only reaches mid-step is still drawn on this frame.
        private (int MinX, int MinY, int MaxX, int MaxY) VisibleBounds(CameraView camera)
        {
            ViewBounds swept = camera.SweptBounds;

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
}
