using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

internal static class CollisionFixtures
{
    internal const int TileSize = 16;

    internal const string Solid = "solid";
    internal const string OneWay = "one-way";

    /// <summary>A second solid kind, so a filter can admit one wall and not the one beside it.</summary>
    internal const string Climb = "climb";

    internal static readonly CellProfile[] Profiles =
    [
        new(CellCollision.None, "empty"),
        new(CellCollision.Solid, Solid),
        new(CellCollision.OneWay, OneWay),
        new(CellCollision.Solid, Climb),
    ];

    /// <summary>A grid painted from rows of characters: '.' empty, '#' solid, '-' one-way, '=' climbable.</summary>
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
                    _ => 0,
                };
            }
        }

        return world.AddGrid(TileSize, width, rows.Length, cells, Profiles);
    }

    internal static Aabb2D Box(float x, float y, float width, float height) =>
        Aabb2D.FromCorner(new Vector2(x, y), new Vector2(width, height));
}
