using System.Numerics;
using Capsule.Maps;
using Capsule.Rendering;

namespace Capsule.Scenes.Entities;

/// <summary>
/// A tile grid as one entity: the grid to query, and visible coloured tiles drawn as quads.
/// The quads are in world coordinates from the origin and
/// <see cref="Entity.Position"/> is never consulted, so a tilemap does not move. It draws in the
/// scene's insertion order like any entity, so a scene that adds its tilemap first draws terrain
/// behind everything.
/// </summary>
public sealed class TileMap : Entity
{
    private readonly TileGrid _grid;

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

    /// <summary>The palette index at a tile coordinate; 0 where the grid is empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public int TileAt(int x, int y) => _grid.TileAt(x, y);

    /// <summary>The tile type name at a tile coordinate.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public string TileTypeAt(int x, int y) => _grid.TileTypeAt(x, y);

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
