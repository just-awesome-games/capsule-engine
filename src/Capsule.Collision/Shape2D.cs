using System.Numerics;
using System.Runtime.CompilerServices;

namespace Capsule.Collision;

/// <summary>
/// One convex collision shape: the region within <see cref="Radius"/> of the convex hull of its
/// points. A shape carries no angle — rotation stays render-side — and is validated on
/// construction, so one built by a factory is one every query accepts. The default value holds no
/// points and is no region; every seam that takes a shape rejects it with
/// <see cref="ArgumentException"/>.
/// </summary>
public readonly struct Shape2D : IEquatable<Shape2D>
{
    /// <summary>The most points a shape may hold.</summary>
    public const int MaxPoints = 8;

    // Points closer together than this, and corners flatter than its square, have no well-defined
    // outward direction and are refused.
    internal const float PointTolerance = CollisionWorld2D.LinearSlop;

    private readonly PointBuffer _points;
    private readonly int _count;

    private Shape2D(ShapeKind2D kind, in PointBuffer points, int count, float radius, in Aabb2D bounds)
    {
        Kind = kind;
        _points = points;
        _count = count;
        Radius = radius;
        Bounds = bounds;
    }

    /// <summary>Which member of the shape union this is.</summary>
    public ShapeKind2D Kind { get; }

    /// <summary>How far the shape extends beyond the hull of its points, in world units.</summary>
    public float Radius { get; }

    /// <summary>
    /// How many points the shape holds: one for a circle, two for a capsule, three or more
    /// otherwise, and zero for the default value, which is no shape.
    /// </summary>
    public int PointCount => _count;

    /// <summary>The shape's own bounds, in the space its points are expressed in, radius included.</summary>
    public Aabb2D Bounds { get; }

    /// <summary>A circle of <paramref name="radius"/> around <paramref name="center"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and positive, or the centre is not finite.</exception>
    /// <exception cref="ArgumentException">The centre and radius together overflow the shape's bounds.</exception>
    public static Shape2D Circle(Vector2 center, float radius)
    {
        RequirePositiveRadius(radius);
        RequireFinite(center, nameof(center));

        PointBuffer points = default;
        points[0] = center;

        return new Shape2D(ShapeKind2D.Circle, points, 1, radius, Bounded(points, 1, radius, nameof(radius)));
    }

    /// <summary>Everything within <paramref name="radius"/> of the segment from <paramref name="start"/> to <paramref name="end"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and positive, or an endpoint is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// The endpoints coincide, or something derived from them is not finite: the segment between
    /// them, its squared length, or the bounds the endpoints and radius describe.
    /// </exception>
    public static Shape2D Capsule(Vector2 start, Vector2 end, float radius)
    {
        RequirePositiveRadius(radius);
        RequireFinite(start, nameof(start));
        RequireFinite(end, nameof(end));

        PointBuffer points = default;
        points[0] = start;
        points[1] = end;

        RequireApart(points, nameof(end));

        // The narrowphase measures along the segment, so its length must be finite even when both
        // ends are.
        RequireFiniteGeometry(points, 2, nameof(end));

        return new Shape2D(ShapeKind2D.Capsule, points, 2, radius, Bounded(points, 2, radius, nameof(radius)));
    }

    /// <summary>An axis-aligned rectangle covering <paramref name="box"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A corner is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// The box is inverted, spans nothing on an axis, or spans more than a float can measure.
    /// </exception>
    public static Shape2D Box(in Aabb2D box)
    {
        RequireFinite(box.Min, nameof(box));
        RequireFinite(box.Max, nameof(box));

        // A box reaching from one end of the float range to the other has finite corners and an
        // infinite width, which would pass a greater-than test on its way into the mover's inset
        // and the tree's area heuristic. Negated comparisons, so a NaN is refused with it.
        float width = box.Max.X - box.Min.X;
        float height = box.Max.Y - box.Min.Y;

        if (!(width > PointTolerance) || !(height > PointTolerance) || !float.IsFinite(width + height))
        {
            throw new ArgumentException(
                "A box must span more than the linear slop on both axes and no more than a float can measure, with Min below Max.",
                nameof(box));
        }

        PointBuffer points = default;
        points[0] = box.Min;
        points[1] = new Vector2(box.Max.X, box.Min.Y);
        points[2] = box.Max;
        points[3] = new Vector2(box.Min.X, box.Max.Y);

        // The box is its own bounds, already checked finite and expanded by no radius.
        return new Shape2D(ShapeKind2D.Box, points, 4, 0f, box);
    }

    /// <summary>
    /// An axis-aligned rectangle of <paramref name="size"/> whose lower corner is
    /// <paramref name="corner"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The size spans nothing on an axis, or the box it describes spans more than a float can
    /// measure on an axis or across both.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="size"/> is negative or not finite.</exception>
    public static Shape2D Box(Vector2 corner, Vector2 size) => Box(Aabb2D.FromCorner(corner, size));

    /// <summary>
    /// A convex polygon of three to eight points, rounded by <paramref name="radius"/> when one is
    /// given. Winding is normalised on construction, so either order is accepted.
    /// </summary>
    /// <param name="points">The hull's corners, convex and in either winding order.</param>
    /// <param name="radius">How far the polygon extends beyond that hull; zero for a plain polygon.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is negative or not finite, or a point is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// There are not three to eight points, two points nearly coincide, the points are not strictly
    /// convex, or something derived from them is not finite: an edge vector, the twice-signed area
    /// the winding is decided by, a corner's cross product, or the bounds the points and radius
    /// describe.
    /// </exception>
    public static Shape2D Polygon(ReadOnlySpan<Vector2> points, float radius = 0f)
    {
        if (!float.IsFinite(radius) || radius < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "A shape's radius must be finite and non-negative.");
        }

        if (points.Length is < 3 or > MaxPoints)
        {
            throw new ArgumentException(
                $"A polygon takes 3 to {MaxPoints} points, not {points.Length}.",
                nameof(points));
        }

        PointBuffer buffer = default;
        for (int index = 0; index < points.Length; index++)
        {
            RequireFinite(points[index], nameof(points));
            buffer[index] = points[index];
        }

        RequireDistinct(buffer, points.Length, nameof(points));

        // Before the winding is normalised and the corners are tested, because both compute the
        // products this refuses; reversing the winding only negates them.
        RequireFiniteGeometry(buffer, points.Length, nameof(points));

        NormaliseWinding(ref buffer, points.Length);
        RequireConvex(buffer, points.Length, nameof(points));

        Aabb2D bounds = Bounded(buffer, points.Length, radius, nameof(radius));
        ShapeKind2D kind = radius == 0f && points.Length == 4 && IsCornersOf(buffer, bounds)
            ? ShapeKind2D.Box
            : ShapeKind2D.Polygon;

        return new Shape2D(kind, buffer, points.Length, radius, bounds);
    }

    // A hull of two points with no radius: one face of a grid cell. Never public — every shape a
    // game can build has an interior.
    internal static Shape2D Segment(Vector2 start, Vector2 end)
    {
        PointBuffer points = default;
        points[0] = start;
        points[1] = end;

        return new Shape2D(ShapeKind2D.Capsule, points, 2, 0f, Bounded(points, 2, 0f, nameof(start)));
    }

    /// <summary>The point at <paramref name="index"/>, in the shape's own space.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside <see cref="PointCount"/>.</exception>
    public Vector2 Point(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);

        return _points[index];
    }

    /// <summary>This shape with every point moved by <paramref name="offset"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The offset is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// The offset carries the shape's bounds past what a float box holds, or places it where the
    /// coordinates are coarser than the shape is small, collapsing its bounds to a line on an axis
    /// it has extent on.
    /// </exception>
    public Shape2D Translated(Vector2 offset)
    {
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "A shape's offset must be finite.");
        }

        PointBuffer moved = _points;
        for (int index = 0; index < _count; index++)
        {
            moved[index] += offset;
        }

        Aabb2D bounds = Finite(Bounds.Translated(offset), nameof(offset));
        RequireExtentSurvives(Bounds.Size, bounds.Size);

        return new Shape2D(Kind, moved, _count, Radius, bounds);
    }

    /// <summary>
    /// This shape with every point multiplied by <paramref name="scale"/>, about the origin of the
    /// collider's own local space, so where the collider sits is unaffected. A rounded shape takes
    /// a uniform scale only: its radius is one distance and has no per-axis form.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A component of the scale is not finite and greater than zero.</exception>
    /// <exception cref="ArgumentException">
    /// The scale is non-uniform on a rounded shape, or the shape it produces is one construction
    /// would refuse: a radius that has overflowed or vanished, points that have collapsed onto each
    /// other, or bounds and derived geometry that are no longer finite.
    /// </exception>
    public Shape2D Scaled(Vector2 scale)
    {
        if (!float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || scale.X <= 0f || scale.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "A shape's scale must be finite and greater than zero on both axes.");
        }

        if (Radius > 0f && scale.X != scale.Y)
        {
            throw new ArgumentException(
                $"A {Kind} is rounded by a radius, which is one distance and has no per-axis form; scale it by the same factor on both axes.",
                nameof(scale));
        }

        PointBuffer scaled = _points;
        for (int index = 0; index < _count; index++)
        {
            scaled[index] *= scale;
        }

        // Uniform wherever it is non-zero, so either component is the factor.
        float radius = Radius * scale.X;
        if (Radius > 0f && !(float.IsFinite(radius) && radius > 0f))
        {
            throw new ArgumentException(
                "The scaled shape's radius is no longer a distance the narrowphase can measure with.",
                nameof(scale));
        }

        // A positive scale preserves winding and convexity in exact arithmetic; in floats it can
        // still fold two corners together or push an edge past what a float measures.
        if (_count == 2)
        {
            RequireApart(scaled, nameof(scale));
            RequireFiniteGeometry(scaled, 2, nameof(scale));
        }
        else if (_count >= 3)
        {
            RequireDistinct(scaled, _count, nameof(scale));
            RequireFiniteGeometry(scaled, _count, nameof(scale));
            RequireConvex(scaled, _count, nameof(scale));
        }

        return new Shape2D(Kind, scaled, _count, radius, Bounded(scaled, _count, radius, nameof(scale)));
    }

    // Far enough out, the floats either side of a small shape are the same float: the hull survives
    // but the broadphase box folds to a line, and the tree skips geometry the shape still holds.
    private static void RequireExtentSurvives(Vector2 was, Vector2 now)
    {
        if ((was.X > 0f && !(now.X > 0f)) || (was.Y > 0f && !(now.Y > 0f)))
        {
            throw new ArgumentException(
                "The shape has no extent left once it is placed there; the coordinate is coarser than the shape is wide, so its bounds collapse to a line.",
                "offset");
        }
    }

    /// <inheritdoc/>
    public bool Equals(Shape2D other)
    {
        if (Kind != other.Kind || _count != other._count || Radius != other.Radius)
        {
            return false;
        }

        for (int index = 0; index < _count; index++)
        {
            if (_points[index] != other._points[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Shape2D other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Kind);
        hash.Add(Radius);
        for (int index = 0; index < _count; index++)
        {
            hash.Add(_points[index]);
        }

        return hash.ToHashCode();
    }

    /// <summary>Whether two shapes hold the same kind, radius and points.</summary>
    public static bool operator ==(Shape2D left, Shape2D right) => left.Equals(right);

    /// <summary>Whether two shapes differ in kind, radius or points.</summary>
    public static bool operator !=(Shape2D left, Shape2D right) => !left.Equals(right);

    // The furthest point along a direction, ties to the lowest index so identical inputs walk an
    // identical simplex.
    internal Vector2 Support(Vector2 direction)
    {
        Vector2 best = _points[0];
        float bestDot = Vector2.Dot(best, direction);

        for (int index = 1; index < _count; index++)
        {
            Vector2 candidate = _points[index];
            float dot = Vector2.Dot(candidate, direction);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = candidate;
            }
        }

        return best;
    }

    internal Vector2 PointAt(int index) => _points[index];

    private static void RequirePositiveRadius(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "A rounded shape's radius must be finite and greater than zero.");
        }
    }

    private static void RequireFinite(Vector2 point, string parameterName)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, point, "A shape's points must be finite.");
        }
    }

    private static void RequireApart(in PointBuffer points, string parameterName)
    {
        if (Vector2.DistanceSquared(points[0], points[1]) <= PointTolerance * PointTolerance)
        {
            throw new ArgumentException(
                "A capsule's endpoints must be further apart than the linear slop; a capsule of no length is a circle.",
                parameterName);
        }
    }

    private static void RequireDistinct(in PointBuffer points, int count, string parameterName)
    {
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if (Vector2.DistanceSquared(points[i], points[j]) <= PointTolerance * PointTolerance)
                {
                    throw new ArgumentException(
                        $"A polygon's points {i} and {j} are closer together than the linear slop; every corner must be its own.",
                        parameterName);
                }
            }
        }
    }

    // Cross-positive winding throughout, so the outward normal of the edge from p[i] to p[i+1] is
    // always (e.Y, -e.X); reversing here is what lets a caller author either order.
    private static void NormaliseWinding(ref PointBuffer points, int count)
    {
        float twiceArea = 0f;
        for (int index = 0; index < count; index++)
        {
            Vector2 current = points[index];
            Vector2 next = points[(index + 1) % count];
            twiceArea += (current.X * next.Y) - (current.Y * next.X);
        }

        if (twiceArea >= 0f)
        {
            return;
        }

        for (int low = 0, high = count - 1; low < high; low++, high--)
        {
            (points[low], points[high]) = (points[high], points[low]);
        }
    }

    private static void RequireConvex(in PointBuffer points, int count, string parameterName)
    {
        for (int index = 0; index < count; index++)
        {
            Vector2 previous = points[index];
            Vector2 current = points[(index + 1) % count];
            Vector2 next = points[(index + 2) % count];

            Vector2 incoming = current - previous;
            Vector2 outgoing = next - current;
            if ((incoming.X * outgoing.Y) - (incoming.Y * outgoing.X) <= PointTolerance * PointTolerance)
            {
                throw new ArgumentException(
                    $"A polygon's corner {(index + 1) % count} is collinear or reflex; only strictly convex polygons are shapes.",
                    parameterName);
            }
        }
    }

    private static Aabb2D Bounded(in PointBuffer points, int count, float radius, string parameterName)
    {
        Vector2 min = points[0];
        Vector2 max = min;
        for (int index = 1; index < count; index++)
        {
            min = Vector2.Min(min, points[index]);
            max = Vector2.Max(max, points[index]);
        }

        return Finite(new Aabb2D(min, max).Expanded(radius), parameterName);
    }

    // Each input can be finite while the box they describe is not, and one infinite bound unions
    // its way up the broadphase and makes unrelated colliders unfindable. The width is checked as
    // well as the corners: it is what the mover subtracts its inset from and what the tree's
    // surface-area heuristic sums, and it overflows while the corners are still in range.
    private static Aabb2D Finite(in Aabb2D bounds, string parameterName)
    {
        if (!float.IsFinite(bounds.Min.X) || !float.IsFinite(bounds.Min.Y)
            || !float.IsFinite(bounds.Max.X) || !float.IsFinite(bounds.Max.Y))
        {
            throw new ArgumentException(
                "The shape's bounds are not finite; its points and radius are each within range but the box they span is not.",
                parameterName);
        }

        Vector2 size = bounds.Size;
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || !float.IsFinite(bounds.Perimeter))
        {
            throw new ArgumentException(
                "The shape's bounds span more than a float can measure; its corners are within range but the extent between them is not.",
                parameterName);
        }

        return bounds;
    }

    // Everything the narrowphase and the winding rules derive from a hull's points: each edge
    // vector and its squared length, the twice-signed area the winding is normalised by, and the
    // cross products the corner tests and outward normals come from. Two corners can each be a real
    // float with an infinity between them, which becomes a NaN normal at query time.
    private static void RequireFiniteGeometry(in PointBuffer points, int count, string parameterName)
    {
        float twiceArea = 0f;

        for (int index = 0; index < count; index++)
        {
            Vector2 current = points[index];
            Vector2 next = points[(index + 1) % count];
            Vector2 edge = next - current;

            if (!float.IsFinite(edge.X) || !float.IsFinite(edge.Y) || !float.IsFinite(edge.LengthSquared()))
            {
                throw new ArgumentException(
                    $"The edge leaving point {index} spans more than a float can measure; its ends are within range and the step between them is not.",
                    parameterName);
            }

            twiceArea += (current.X * next.Y) - (current.Y * next.X);
        }

        if (!float.IsFinite(twiceArea))
        {
            throw new ArgumentException(
                "The polygon's area overflows; its points are within range but the products the winding is decided by are not.",
                parameterName);
        }

        for (int index = 0; count >= 3 && index < count; index++)
        {
            Vector2 incoming = points[(index + 1) % count] - points[index];
            Vector2 outgoing = points[(index + 2) % count] - points[(index + 1) % count];

            if (!float.IsFinite((incoming.X * outgoing.Y) - (incoming.Y * outgoing.X)))
            {
                throw new ArgumentException(
                    $"The turn at corner {(index + 1) % count} overflows; the shape's outward normals there would not be a direction.",
                    parameterName);
            }
        }
    }

    private static bool IsCornersOf(in PointBuffer points, in Aabb2D bounds)
    {
        int seen = 0;
        for (int index = 0; index < 4; index++)
        {
            Vector2 point = points[index];
            bool low = point.X == bounds.Min.X;
            bool high = point.X == bounds.Max.X;
            if (low == high || (point.Y != bounds.Min.Y && point.Y != bounds.Max.Y))
            {
                return false;
            }

            seen |= 1 << ((low ? 0 : 1) | (point.Y == bounds.Min.Y ? 0 : 2));
        }

        return seen == 0b1111;
    }

    [InlineArray(MaxPoints)]
    private struct PointBuffer
    {
        private Vector2 _element0;
    }
}
