using System.Numerics;

namespace Capsule.Collision.Internal;

/// <summary>
/// Ray casting against one shape. Every routine parameterises the ray as
/// <c>origin + direction * t</c> over <c>t</c> in <c>[0, limit]</c>, so a caller may pass a unit
/// direction with a distance or a whole translation with a limit of 1 and read the result the
/// same way. A ray that starts inside reports <c>t = 0</c> and a zero normal.
/// </summary>
internal static class Segments
{
    private const float Parallel = 1e-8f;

    /// <summary>Whether a ray could reach <paramref name="box"/> within <paramref name="limit"/>.</summary>
    internal static bool IntersectsBox(in Aabb2D box, Vector2 origin, Vector2 direction, float limit) =>
        RayBox(box, origin, direction, limit, out _, out _);

    internal static bool RayBox(
        in Aabb2D box,
        Vector2 origin,
        Vector2 direction,
        float limit,
        out float t,
        out Vector2 normal) =>
        RayBoxRange(box, origin, direction, limit, out t, out _, out normal);

    /// <summary>Where a ray enters and leaves a box; the entry is zero when it starts inside.</summary>
    internal static bool RayBoxRange(
        in Aabb2D box,
        Vector2 origin,
        Vector2 direction,
        float limit,
        out float t,
        out float exit,
        out Vector2 normal)
    {
        t = 0f;
        exit = 0f;
        normal = Vector2.Zero;

        float lower = 0f;
        float upper = limit;

        for (int axis = 0; axis < 2; axis++)
        {
            float component = axis == 0 ? direction.X : direction.Y;
            float start = axis == 0 ? origin.X : origin.Y;
            float min = axis == 0 ? box.Min.X : box.Min.Y;
            float max = axis == 0 ? box.Max.X : box.Max.Y;

            if (MathF.Abs(component) < Parallel)
            {
                if (start < min || start > max)
                {
                    return false;
                }

                continue;
            }

            float inverse = 1f / component;
            float near = (min - start) * inverse;
            float far = (max - start) * inverse;
            float sign = -1f;

            if (near > far)
            {
                (near, far) = (far, near);
                sign = 1f;
            }

            if (near > lower)
            {
                lower = near;
                normal = axis == 0 ? new Vector2(sign, 0f) : new Vector2(0f, sign);
            }

            upper = MathF.Min(upper, far);
            if (lower > upper)
            {
                return false;
            }
        }

        t = lower;
        exit = upper;

        return true;
    }

    /// <summary>The nearest point of a shape a ray reaches; the shape's points are in world space.</summary>
    internal static bool RayShape(
        in Shape2D shape,
        Vector2 origin,
        Vector2 direction,
        float limit,
        out float t,
        out Vector2 normal)
    {
        if (shape.Kind == ShapeKind2D.Box)
        {
            return RayBox(shape.Bounds, origin, direction, limit, out t, out normal);
        }

        if (shape.Radius == 0f)
        {
            if (shape.PointCount == 2)
            {
                // A bare segment has no interior for the half-plane clip to bound.
                normal = EdgeNormal(shape, 0);
                if (Vector2.Dot(normal, direction) > 0f)
                {
                    normal = -normal;
                }

                return RaySegment(shape.PointAt(0), shape.PointAt(1), origin, direction, limit, out t);
            }

            return RayPolygon(shape, origin, direction, limit, out t, out normal);
        }

        return RayRounded(shape, origin, direction, limit, out t, out normal);
    }

    internal static bool RayCircle(
        Vector2 center,
        float radius,
        Vector2 origin,
        Vector2 direction,
        float limit,
        out float t)
    {
        Vector2 toStart = origin - center;
        float a = Vector2.Dot(direction, direction);
        if (a < Parallel)
        {
            t = 0f;
            return Vector2.Dot(toStart, toStart) <= radius * radius;
        }

        float b = Vector2.Dot(toStart, direction);
        float c = Vector2.Dot(toStart, toStart) - (radius * radius);

        if (c <= 0f)
        {
            t = 0f;
            return true;
        }

        float discriminant = (b * b) - (a * c);
        if (discriminant < 0f)
        {
            t = 0f;
            return false;
        }

        t = (-b - MathF.Sqrt(discriminant)) / a;

        return t >= 0f && t <= limit;
    }

    /// <summary>
    /// The ray's first crossing of the segment from <paramref name="a"/> to <paramref name="b"/>,
    /// ignoring a ray running along it.
    /// </summary>
    internal static bool RaySegment(
        Vector2 a,
        Vector2 b,
        Vector2 origin,
        Vector2 direction,
        float limit,
        out float t)
    {
        t = 0f;

        Vector2 edge = b - a;
        float denominator = Cross(direction, edge);
        if (MathF.Abs(denominator) < Parallel)
        {
            return false;
        }

        Vector2 toStart = a - origin;
        float rayT = Cross(toStart, edge) / denominator;
        float edgeT = Cross(toStart, direction) / denominator;

        if (rayT < 0f || rayT > limit || edgeT < 0f || edgeT > 1f)
        {
            return false;
        }

        t = rayT;

        return true;
    }

    internal static float Cross(Vector2 left, Vector2 right) => (left.X * right.Y) - (left.Y * right.X);

    /// <summary>The outward unit normal of the edge leaving point <paramref name="index"/>.</summary>
    internal static Vector2 EdgeNormal(in Shape2D shape, int index)
    {
        Vector2 edge = shape.PointAt((index + 1) % shape.PointCount) - shape.PointAt(index);

        return Vector2.Normalize(new Vector2(edge.Y, -edge.X));
    }

    // Half-plane clipping over the polygon's own edges: exact, and the only routine that reports
    // which face was crossed rather than deriving a normal from a witness point.
    private static bool RayPolygon(
        in Shape2D shape,
        Vector2 origin,
        Vector2 direction,
        float limit,
        out float t,
        out Vector2 normal)
    {
        t = 0f;
        normal = Vector2.Zero;

        float lower = 0f;
        float upper = limit;
        int entered = -1;

        for (int index = 0; index < shape.PointCount; index++)
        {
            Vector2 face = EdgeNormal(shape, index);
            float numerator = Vector2.Dot(face, shape.PointAt(index) - origin);
            float denominator = Vector2.Dot(face, direction);

            if (denominator == 0f)
            {
                if (numerator < 0f)
                {
                    return false;
                }

                continue;
            }

            if (denominator < 0f && numerator < lower * denominator)
            {
                lower = numerator / denominator;
                entered = index;
            }
            else if (denominator > 0f && numerator < upper * denominator)
            {
                upper = numerator / denominator;
            }

            if (upper < lower)
            {
                return false;
            }
        }

        if (entered < 0)
        {
            // No face was crossed on the way in, so the ray began inside the polygon.
            return true;
        }

        t = lower;
        normal = EdgeNormal(shape, entered);

        return true;
    }

    // A rounded hull is its offset faces and its corner circles; taking the nearest of those is
    // exact for a circle, a capsule and a polygon with a radius alike.
    private static bool RayRounded(
        in Shape2D shape,
        Vector2 origin,
        Vector2 direction,
        float limit,
        out float t,
        out Vector2 normal)
    {
        t = 0f;
        normal = Vector2.Zero;

        if (Hulls.Contains(shape, origin))
        {
            return true;
        }

        float nearest = float.PositiveInfinity;
        Vector2 nearestNormal = Vector2.Zero;
        int count = shape.PointCount;

        if (count >= 2)
        {
            // A capsule is its segment walked both ways, so its two offset faces fall out of the
            // same loop the polygon's do.
            for (int index = 0; index < count; index++)
            {
                Vector2 face = EdgeNormal(shape, index);
                Vector2 offset = face * shape.Radius;
                Vector2 a = shape.PointAt(index) + offset;
                Vector2 b = shape.PointAt((index + 1) % count) + offset;

                if (RaySegment(a, b, origin, direction, limit, out float faceT) && faceT < nearest)
                {
                    nearest = faceT;
                    nearestNormal = face;
                }
            }
        }

        for (int index = 0; index < count; index++)
        {
            Vector2 corner = shape.PointAt(index);
            if (RayCircle(corner, shape.Radius, origin, direction, limit, out float cornerT)
                && cornerT < nearest)
            {
                nearest = cornerT;
                nearestNormal = Vector2.Normalize(origin + (direction * cornerT) - corner);
            }
        }

        if (float.IsPositiveInfinity(nearest))
        {
            return false;
        }

        t = nearest;
        normal = nearestNormal;

        return true;
    }
}
