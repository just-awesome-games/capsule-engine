using System.Numerics;

namespace Capsule.Physics.Internal;

// Ray casting against one shape. Every routine parameterises the ray as origin + direction * t over t
// in [0, limit]. A unit direction with a distance and a translation with a limit of 1 read the same
// way. A ray that starts inside a box reports t = 0 and a zero normal. RayShape turns that into
// the nearest face, since a reported hit carries a unit normal.
internal static class Rays2D
{
    private const float Parallel = 1e-8f;

    internal static bool RayBox(
        in Aabb2D box,
        Vector2 origin,
        Vector2 direction,
        float limit,
        out float t,
        out Vector2 normal) =>
        RayBoxRange(box, origin, direction, limit, out t, out _, out normal);

    // Where a ray enters and leaves a box. The entry is zero when it starts inside.
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

        // Clipped X then Y. The entry the caller reads is whichever slab pushed it last.
        if (!Slab(direction.X, origin.X, box.Min.X, box.Max.X, true, ref lower, ref upper, ref normal)
            || !Slab(direction.Y, origin.Y, box.Min.Y, box.Max.Y, false, ref lower, ref upper, ref normal))
        {
            return false;
        }

        t = lower;
        exit = upper;

        return true;
    }

    // The outward normal of the box side nearest a point inside it. A ray that started inside crossed
    // no face, and this is the surface the hit names.
    internal static Vector2 NearestFace(in Aabb2D box, Vector2 point)
    {
        float left = point.X - box.Min.X;
        float right = box.Max.X - point.X;
        float top = point.Y - box.Min.Y;
        float bottom = box.Max.Y - point.Y;
        float least = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));

        if (least == left)
        {
            return new Vector2(-1f, 0f);
        }

        if (least == right)
        {
            return new Vector2(1f, 0f);
        }

        return least == top ? new Vector2(0f, -1f) : new Vector2(0f, 1f);
    }

    // The nearest point of a shape a ray reaches. The shape's points are in world space.
    internal static bool RayShape(
        in Shape2D shape,
        Vector2 origin,
        Vector2 direction,
        float limit,
        out float t,
        out Vector2 normal)
    {
        bool hit = Reach(shape, origin, direction, limit, out t, out normal);

        if (hit && normal == Vector2.Zero)
        {
            normal = NearestFace(shape.Bounds, origin);
        }

        return hit;
    }

    // The ray's first crossing of the segment from a to b, ignoring a ray running along it.
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
        float denominator = Math2D.Cross(direction, edge);
        if (MathF.Abs(denominator) < Parallel)
        {
            return false;
        }

        Vector2 toStart = a - origin;
        float rayT = Math2D.Cross(toStart, edge) / denominator;
        float edgeT = Math2D.Cross(toStart, direction) / denominator;

        if (rayT < 0f || rayT > limit || edgeT < 0f || edgeT > 1f)
        {
            return false;
        }

        t = rayT;

        return true;
    }

    // One axis of the slab clip. Narrows the surviving interval and names the entry face.
    private static bool Slab(
        float component,
        float start,
        float min,
        float max,
        bool horizontal,
        ref float lower,
        ref float upper,
        ref Vector2 normal)
    {
        if (MathF.Abs(component) < Parallel)
        {
            return !(start < min || start > max);
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
            normal = horizontal ? new Vector2(sign, 0f) : new Vector2(0f, sign);
        }

        upper = MathF.Min(upper, far);

        return lower <= upper;
    }

    private static bool Reach(
        in Shape2D shape,
        Vector2 origin,
        Vector2 direction,
        float limit,
        out float t,
        out Vector2 normal)
    {
        switch (shape.Kind)
        {
            case ShapeKind2D.Box:
                return RayBox(shape.Bounds, origin, direction, limit, out t, out normal);

            case ShapeKind2D.Segment:
                // A bare segment has no interior for the half-plane clip to bound.
                normal = EdgeNormal(shape, 0);
                if (Vector2.Dot(normal, direction) > 0f)
                {
                    normal = -normal;
                }

                return RaySegment(shape.PointAt(0), shape.PointAt(1), origin, direction, limit, out t);

            case ShapeKind2D.Polygon when shape.Radius == 0f:
                return RayPolygon(shape, origin, direction, limit, out t, out normal);

            default:
                return RayRounded(shape, origin, direction, limit, out t, out normal);
        }
    }

    private static bool RayCircle(
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

    // The outward unit normal of the edge leaving point index.
    private static Vector2 EdgeNormal(in Shape2D shape, int index)
    {
        Vector2 edge = shape.PointAt((index + 1) % shape.PointCount) - shape.PointAt(index);

        return Vector2.Normalize(new Vector2(edge.Y, -edge.X));
    }

    // Half-plane clipping over the polygon's edges. This routine reports which face was crossed
    // instead of deriving a normal from a witness point.
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

    // A rounded hull is its offset faces plus its corner circles. The nearest of those is exact for a
    // circle, a capsule and a rounded polygon.
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

        if (Contains(shape, origin))
        {
            return true;
        }

        float nearest = float.PositiveInfinity;
        Vector2 nearestNormal = Vector2.Zero;
        int count = shape.PointCount;

        if (count >= 2)
        {
            // A capsule's segment is walked both ways, so its two offset faces fall out of the polygon
            // loop.
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

    // Whether a point lies inside a shape whose points are already in world space, or on its outline.
    private static bool Contains(in Shape2D shape, Vector2 point)
    {
        int count = shape.PointCount;

        if (count >= 3 && Inside(shape, point))
        {
            return true;
        }

        int edges = count == 2 ? 1 : count;
        for (int index = 0; index < edges; index++)
        {
            Vector2 a = shape.PointAt(index);
            Vector2 b = shape.PointAt((index + 1) % count);
            if (Vector2.Distance(point, ClosestOnSegment(a, b, point)) <= shape.Radius)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector2 ClosestOnSegment(Vector2 a, Vector2 b, Vector2 point)
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

    // Winding is normalised on construction, so one sign test per edge decides it.
    private static bool Inside(in Shape2D shape, Vector2 point)
    {
        for (int index = 0; index < shape.PointCount; index++)
        {
            Vector2 edge = shape.PointAt((index + 1) % shape.PointCount) - shape.PointAt(index);
            if (Math2D.Cross(edge, point - shape.PointAt(index)) < 0f)
            {
                return false;
            }
        }

        return true;
    }
}
