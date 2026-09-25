using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tiles;

// The one definition of what a TileTransform does to a point in its tile. Collision shapes go through
// Apply. The draw poses reproduce it with the renderer's mirror-then-turn about the tile's centre.
internal static class TileTransforms
{
    // How many distinct transforms there are. Every value below it is valid.
    internal const int Count = 8;

    // Indexed by transform. The renderer mirrors a sprite about its pivot and then turns it clockwise, so
    // each entry is the mirror and quarter turn that compose to the transform about the tile's centre.
    private static readonly Pose[] Poses =
    [
        new(0f, FlipX: false, FlipY: false),
        new(0f, FlipX: true, FlipY: false),
        new(0f, FlipX: false, FlipY: true),
        new(0f, FlipX: true, FlipY: true),
        new(MathF.PI / 2f, FlipX: false, FlipY: true),
        new(MathF.PI / 2f, FlipX: false, FlipY: false),
        new(MathF.PI / 2f, FlipX: true, FlipY: true),
        new(MathF.PI / 2f, FlipX: true, FlipY: false),
    ];

    internal static bool IsDefined(TileTransform transform) => (uint)transform < Count;

    // Maps a point measured from the tile's top-left corner to where the transform puts it.
    internal static Vector2 Apply(Vector2 point, float tileSize, TileTransform transform)
    {
        if ((transform & TileTransform.Transpose) != 0)
        {
            point = new Vector2(point.Y, point.X);
        }

        if ((transform & TileTransform.FlipX) != 0)
        {
            point.X = tileSize - point.X;
        }

        if ((transform & TileTransform.FlipY) != 0)
        {
            point.Y = tileSize - point.Y;
        }

        return point;
    }

    // Shape2D.Polygon restores the winding an odd number of mirrors reverses.
    internal static Shape2D Apply(in Shape2D shape, float tileSize, TileTransform transform)
    {
        Span<Vector2> points = stackalloc Vector2[shape.PointCount];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = Apply(shape.PointAt(index), tileSize, transform);
        }

        return Shape2D.Polygon(points);
    }

    internal static ref readonly Pose PoseOf(TileTransform transform) => ref Poses[(int)transform];

    internal readonly record struct Pose(float Rotation, bool FlipX, bool FlipY);
}
