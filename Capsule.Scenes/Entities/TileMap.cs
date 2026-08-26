using System.Numerics;
using Capsule.Levels;
using Capsule.Rendering;

namespace Capsule.Scenes.Entities;

/// <summary>The colour a tile type draws as.</summary>
/// <remarks>Transient: a tile's appearance moves into the level format's palette, and this goes with it.</remarks>
public delegate ColorRgba TileColorResolver(string tileType);

/// <summary>
/// A level's tile grid as one entity: the grid to query, and one quad per non-empty tile baked
/// at construction. The quads are in world coordinates from the origin and
/// <see cref="Entity.Position"/> is never consulted, so a tilemap does not move. It draws in the
/// scene's insertion order like any entity, so a scene that adds its tilemap first draws terrain
/// behind everything.
/// </summary>
public sealed class TileMap : Entity
{
    private readonly Level _level;

    /// <param name="tileColor">
    /// Asked for every tile type in the palette here, so a type the game cannot draw fails at
    /// construction rather than on the first painted tile.
    /// </param>
    public TileMap(Level level, TileColorResolver tileColor)
        : base(Vector2.Zero)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(tileColor);

        _level = level;
        Size = new Vector2(level.Width * level.TileSize, level.Height * level.TileSize);

        Add(new BakedQuads(BuildQuads(level, tileColor)));
    }

    /// <summary>World units a tile spans on each axis.</summary>
    public int TileSize => _level.TileSize;

    /// <summary>Grid width in tiles.</summary>
    public int Width => _level.Width;

    /// <summary>Grid height in tiles.</summary>
    public int Height => _level.Height;

    /// <summary>World units the grid spans, from the world origin.</summary>
    public Vector2 Size { get; }

    /// <summary>The palette index at a tile coordinate; 0 where the grid is empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public int TileAt(int x, int y) => _level.TileAt(x, y);

    /// <summary>The tile type name at a tile coordinate.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public string TileTypeAt(int x, int y) => _level.TileTypeAt(x, y);

    private static QuadIntent[] BuildQuads(Level level, TileColorResolver tileColor)
    {
        // The whole palette is resolved, not only what happens to be painted. Index 0 is the
        // reserved empty type and is never drawn.
        ReadOnlySpan<string> tileTypes = level.TileTypes;
        ColorRgba[] colorByTile = new ColorRgba[tileTypes.Length];
        for (int tile = 1; tile < tileTypes.Length; tile++)
        {
            colorByTile[tile] = tileColor(tileTypes[tile]);
        }

        Vector2 size = new(level.TileSize, level.TileSize);
        List<QuadIntent> quads = [];
        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                int tile = level.TileAt(x, y);
                if (tile == 0)
                {
                    continue;
                }

                Vector2 corner = new(x * level.TileSize, y * level.TileSize);
                quads.Add(new QuadIntent(corner, corner, size, colorByTile[tile]));
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
