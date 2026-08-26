using System.Numerics;
using Capsule.Maps;
using Capsule.Rendering;

namespace Capsule.Scenes.Entities;

/// <summary>
/// A tile grid as one entity: the grid to query, and one quad per non-empty tile baked at
/// construction. The quads are in world coordinates from the origin and
/// <see cref="Entity.Position"/> is never consulted, so a tilemap does not move. It draws in the
/// scene's insertion order like any entity, so a scene that adds its tilemap first draws terrain
/// behind everything.
/// </summary>
public sealed class TileMap : Entity
{
    private readonly TileGrid _grid;

    public TileMap(TileGrid grid)
        : base(Vector2.Zero)
    {
        ArgumentNullException.ThrowIfNull(grid);

        _grid = grid;
        Size = new Vector2(grid.Width * grid.TileSize, grid.Height * grid.TileSize);

        Add(new BakedQuads(BuildQuads(grid)));
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

    private static QuadIntent[] BuildQuads(TileGrid grid)
    {
        ReadOnlySpan<TileDefinition> palette = grid.TileTypes;
        Vector2 size = new(grid.TileSize, grid.TileSize);
        List<QuadIntent> quads = [];

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                int tile = grid.TileAt(x, y);
                if (tile == 0)
                {
                    continue;
                }

                // Index 0 is the one palette entry without a colour, and the grid guarantees it.
                Vector2 corner = new(x * grid.TileSize, y * grid.TileSize);
                quads.Add(new QuadIntent(corner, corner, size, palette[tile].Color!.Value));
            }
        }

        return [.. quads];
    }

    // Terrain never moves, so the intents are the bake: drawing is a copy, and a step allocates
    // nothing to produce it.
    private sealed class BakedQuads(QuadIntent[] quads) : Renderer
    {
        public override void Draw(FrameView view)
        {
            ArgumentNullException.ThrowIfNull(view);

            foreach (ref readonly QuadIntent quad in quads.AsSpan())
            {
                view.AddQuad(quad);
            }
        }
    }
}
