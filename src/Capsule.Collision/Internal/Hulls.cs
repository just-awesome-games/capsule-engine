using System.Numerics;

namespace Capsule.Collision.Internal;

/// <summary>Point queries against one shape whose points are already in world space.</summary>
internal static class Hulls
{
    /// <summary>Whether <paramref name="point"/> lies inside the shape or on its outline.</summary>
    internal static bool Contains(in Shape shape, Vector2 point)
    {
        float radius = shape.Radius;

        if (shape.PointCount >= 3 && InsideCore(shape, point))
        {
            return true;
        }

        return DistanceToCore(shape, point) <= radius;
    }

    /// <summary>How far <paramref name="point"/> is from the hull of the shape's points; zero inside it.</summary>
    internal static float DistanceToCore(in Shape shape, Vector2 point)
    {
        int count = shape.PointCount;

        if (count == 1)
        {
            return Vector2.Distance(point, shape.PointAt(0));
        }

        if (count >= 3 && InsideCore(shape, point))
        {
            return 0f;
        }

        float nearest = float.PositiveInfinity;
        int edges = count == 2 ? 1 : count;
        for (int index = 0; index < edges; index++)
        {
            Vector2 a = shape.PointAt(index);
            Vector2 b = shape.PointAt((index + 1) % count);
            nearest = MathF.Min(nearest, Vector2.Distance(point, ClosestOnSegment(a, b, point)));
        }

        return nearest;
    }

    internal static Vector2 ClosestOnSegment(Vector2 a, Vector2 b, Vector2 point)
    {
        Vector2 edge = b - a;
        float lengthSquared = Vector2.Dot(edge, edge);
        if (lengthSquared <= 0f)
        {
            return a;
        }

        float t = Math.Clamp(Vector2.Dot(point - a, edge) / lengthSquared, 0f, 1f);

        return a + (edge * t);
    }

    // Winding is normalised on construction, so every outward normal points the same way round
    // and one sign test per edge decides the question.
    private static bool InsideCore(in Shape shape, Vector2 point)
    {
        for (int index = 0; index < shape.PointCount; index++)
        {
            Vector2 edge = shape.PointAt((index + 1) % shape.PointCount) - shape.PointAt(index);
            if (Segments.Cross(edge, point - shape.PointAt(index)) < 0f)
            {
                return false;
            }
        }

        return true;
    }
}
