using System.Numerics;
using Capsule.Collision;

namespace Capsule.Tests.Collision;

internal static class CollisionFixtures
{
    internal const int TileSize = 16;

    internal const string Solid = "solid";
    internal const string Platform = "platform";

    /// <summary>A second solid kind, so a filter can admit one wall and not the one beside it.</summary>
    internal const string Climb = "climb";

    /// <summary>A grid painted from rows of characters: '.' empty, '#' solid, '-' top-face only, '=' climbable.</summary>
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

        return world.AddGrid(TileSize, width, rows.Length, cells, Profiles(world));
    }

    internal static CellProfile2D[] Profiles(CollisionWorld2D world) =>
    [
        new(null),
        new(world.Layer(Solid)),
        new(world.Layer(Platform), CellFaces2D.Top),
        new(world.Layer(Climb)),
    ];

    internal static Aabb2D Box(float x, float y, float width, float height) =>
        Aabb2D.FromCorner(new Vector2(x, y), new Vector2(width, height));
}
