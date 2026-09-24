using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Tests.Physics;

internal static class CollisionFixtures
{
    internal const int TileSize = 16;

    internal const string Solid = "solid";
    internal const string Platform = "platform";

    /// <summary>A second solid kind, so a filter can admit one wall and not the one beside it.</summary>
    internal const string Climb = "climb";

    /// <summary>The layer the single collidable cell of <see cref="OneWay"/> is on.</summary>
    internal const string Ledge = "ledge";

    /// <summary>
    /// What a move is allowed to land short of a surface by: the mover stops a slop clear of what
    /// blocked it, and a two-axis move can spend that twice.
    /// </summary>
    internal const float Tolerance = 2f * CollisionTolerance.LinearSlop;

    /// <summary>
    /// A grid painted from rows of characters: '.' empty, '#' solid, '-' one-way, '=' climbable, '/' a
    /// solid slope rising to the right and '\' one falling to the right, both at 45 degrees.
    /// </summary>
    internal static GridCollider2D Paint(CollisionWorld2D world, params string[] rows)
    {
        int width = rows[0].Length;
        int[] cells = new int[width * rows.Length];

        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cells[(y * width) + x] = rows[y][x] switch
                {
                    '#' => 1,
                    '-' => 2,
                    '=' => 3,
                    '/' => 4,
                    '\\' => 5,
                    _ => 0,
                };
            }
        }

        return world.AddGrid(TileSize, width, rows.Length, cells, Profiles(world));
    }

    internal static CellProfile2D[] Profiles(CollisionWorld2D world) =>
    [
        new(null),
        new(world.Layer(Solid)),
        new(world.Layer(Platform), OneWay: true),
        new(world.Layer(Climb)),
        new(world.Layer(Solid), SlopeUp),
        new(world.Layer(Solid), SlopeDown),
    ];

    /// <summary>The cell below the diagonal from its bottom-left corner to its top-right.</summary>
    internal static readonly Shape2D SlopeUp = Shape2D.Polygon([new(0f, TileSize), new(TileSize, 0f), new(TileSize, TileSize)]);

    /// <summary>The cell below the diagonal from its top-left corner to its bottom-right.</summary>
    internal static readonly Shape2D SlopeDown = Shape2D.Polygon([new(0f, 0f), new(TileSize, TileSize), new(0f, TileSize)]);

    internal static Aabb2D Box(float x, float y, float width, float height) =>
        Aabb2D.FromCorner(new Vector2(x, y), new Vector2(width, height));

    /// <summary>
    /// A 3x3 grid whose only collidable cell is the middle one, a one-way tile. That cell spans
    /// x = 16..32 and y = 16..32, so its one edge lies along y = 16.
    /// </summary>
    internal static CollisionWorld2D OneWay()
    {
        CollisionWorld2D world = new();
        world.AddGrid(
            TileSize,
            3,
            3,
            [0, 0, 0, 0, 1, 0, 0, 0, 0],
            [new CellProfile2D(null), new CellProfile2D(world.Layer(Ledge), OneWay: true)]);

        return world;
    }
}

/// <summary>A bare collider that queries the world for itself; no body, so nothing ever moves it.</summary>
internal sealed class Prober : Entity
{
    internal Prober(Vector2 position, params string[] detects)
        : this(position, new Vector2(8f, 8f), detects)
    {
    }

    internal Prober(Vector2 position, Vector2 size, params string[] detects)
        : base(position)
    {
        Collider = new BoxCollider2D(size);
        Collider.SetFilter(detects);
        Add(Collider);
    }

    internal BoxCollider2D Collider { get; }
}

/// <summary>The rounded <see cref="Prober"/>: its sweeps take the iterated narrowphase.</summary>
internal sealed class RoundProber : Entity
{
    internal RoundProber(Vector2 position, float radius, params string[] detects)
        : base(position)
    {
        Collider = new CircleCollider2D(radius);
        Collider.SetFilter(detects);
        Add(Collider);
    }

    internal CircleCollider2D Collider { get; }
}
