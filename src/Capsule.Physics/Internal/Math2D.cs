using System.Numerics;

namespace Capsule.Physics.Internal;

// 2D vector algebra the narrowphase needs and System.Numerics does not provide.
internal static class Math2D
{
    // The scalar cross product, the signed area of the parallelogram the two vectors span. It is
    // positive when right lies clockwise of left as seen in the Y-down world, counterclockwise on Y-up axes.
    internal static float Cross(Vector2 left, Vector2 right) => (left.X * right.Y) - (left.Y * right.X);
}
