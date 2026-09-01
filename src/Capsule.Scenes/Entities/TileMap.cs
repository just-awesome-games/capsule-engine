using System.Numerics;
using Capsule.Collision;
using Capsule.Rendering;
using Capsule.Scenes.Tiles;

namespace Capsule.Scenes.Entities;

/// <summary>
/// A stationary, world-origin tile grid that draws colored palette entries as quads and, where its
/// palette says any tile type collides, registers one <see cref="GridCollider"/> with the
/// scene's world for the whole layer.
/// </summary>
public sealed class TileMap : Entity
{
    private readonly TileGrid _grid;

    private CollisionWorld? _world;

    /// <param name="grid">The grid to hold and to draw; its palette decides what a tile looks like.</param>
    public TileMap(TileGrid grid)
        : base(Vector2.Zero)
    {
        ArgumentNullException.ThrowIfNull(grid);

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
    /// This layer's collider in the scene's world, or null when it is in no scene or no tile type
    /// in its palette collides. Its cells carry the tile type as their tag, so a contact against
    /// terrain names the type that was authored.
    /// </summary>
    public GridCollider? Collision { get; private set; }

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
        CellProfile[] profiles = new CellProfile[palette.Length];
        for (int index = 0; index < profiles.Length; index++)
        {
            profiles[index] = new CellProfile(ToCellCollision(palette[index].Collision), palette[index].Type);
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

    private static CellCollision ToCellCollision(TileCollision collision) => collision switch
    {
        TileCollision.None => CellCollision.None,
        TileCollision.Solid => CellCollision.Solid,
        TileCollision.OneWay => CellCollision.OneWay,
        _ => throw new ArgumentOutOfRangeException(nameof(collision), collision, "The tile collision kind is not defined."),
    };

    private sealed class VisibleTiles(TileGrid grid) : Renderer
    {
        public override void Draw(FrameView view)
        {
            ArgumentNullException.ThrowIfNull(view);

            (int minX, int minY, int maxX, int maxY) = VisibleBounds(view.Camera);
            ReadOnlySpan<int> tiles = grid.Tiles;
            ReadOnlySpan<TileDefinition> palette = grid.TileTypes;
            Vector2 size = new(grid.TileSize, grid.TileSize);

            for (int y = minY; y < maxY; y++)
            {
                int row = y * grid.Width;
                for (int x = minX; x < maxX; x++)
                {
                    int tile = tiles[row + x];
                    ColorRgba? color = palette[tile].Color;
                    if (color is null)
                    {
                        continue;
                    }

                    Vector2 corner = new(x * grid.TileSize, y * grid.TileSize);
                    view.AddQuad(new QuadIntent(corner, corner, size, color.Value));
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
