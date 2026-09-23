using System.Numerics;
using System.Runtime.CompilerServices;

namespace Capsule.Physics;

/// <summary>
/// A convex collision shape, the region within <see cref="Radius"/> of the convex hull of its
/// points.
/// </summary>
/// <remarks>
/// A shape carries no angle. Rotation stays render-side. Every query accepts a shape a factory
/// returned. The default value holds no points, and every API that takes a shape rejects it.
/// </remarks>
public readonly struct Shape2D : IEquatable<Shape2D>
{
    /// <summary>The most points a shape may hold.</summary>
    public const int MaxPoints = 4;

    // Points closer together than this, and corners flatter than its square, have no well-defined
    // outward direction and are refused.
    internal const float PointTolerance = CollisionTolerance.LinearSlop;

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

    /// <summary>Which <see cref="ShapeKind2D"/> this shape is.</summary>
    public ShapeKind2D Kind { get; }

    /// <summary>How far the shape extends beyond the hull of its points, in world units.</summary>
    public float Radius { get; }

    /// <summary>
    /// How many points the shape holds. One for a circle, two for a capsule, three or four
    /// otherwise, and zero for the default value.
    /// </summary>
    public int PointCount => _count;

    /// <summary>The shape's bounds in the space its points are expressed in, radius included.</summary>
    public Aabb2D Bounds { get; }

    /// <summary>A circle of <paramref name="radius"/> around <paramref name="center"/>.</summary>
    /// <exception cref="ArgumentException">The centre and radius together span more than a float box holds.</exception>
    public static Shape2D Circle(Vector2 center, float radius)
    {
        Guard.Positive(radius, nameof(radius));
        Guard.Finite(center, nameof(center));

        PointBuffer points = default;
        points[0] = center;

        return new Shape2D(ShapeKind2D.Circle, points, 1, radius, Bounded(points, 1, radius, nameof(radius)));
    }

    /// <summary>Everything within <paramref name="radius"/> of the segment from <paramref name="start"/> to <paramref name="end"/>.</summary>
    /// <exception cref="ArgumentException">The endpoints are within the linear slop of each other, or the bounds they and the radius describe are not a box a float can measure.</exception>
    public static Shape2D Capsule(Vector2 start, Vector2 end, float radius)
    {
        Guard.Positive(radius, nameof(radius));
        Guard.Finite(start, nameof(start));
        Guard.Finite(end, nameof(end));

        PointBuffer points = default;
        points[0] = start;
        points[1] = end;

        RequireApart(points, nameof(end));

        return new Shape2D(ShapeKind2D.Capsule, points, 2, radius, Bounded(points, 2, radius, nameof(radius)));
    }

    /// <summary>An axis-aligned rectangle covering <paramref name="box"/>.</summary>
    /// <exception cref="ArgumentException">The box is inverted, spans no more than the linear slop on an axis, or spans more than a float can measure.</exception>
    public static Shape2D Box(in Aabb2D box)
    {
        Guard.Finite(box.Min, nameof(box));
        Guard.Finite(box.Max, nameof(box));

        // A box reaching across the float range has finite corners and an infinite width, which the
        // mover's inset and the tree's area heuristic both read. The comparisons are negated so NaN
        // is refused too.
        float width = box.Max.X - box.Min.X;
        float height = box.Max.Y - box.Min.Y;

        if (!(width > PointTolerance) || !(height > PointTolerance) || !float.IsFinite(width + height))
        {
            throw new ArgumentException(
                "Box is inverted, thinner than the linear slop on an axis, or wider than a float can measure.",
                nameof(box));
        }

        PointBuffer points = default;
        points[0] = box.Min;
        points[1] = new Vector2(box.Max.X, box.Min.Y);
        points[2] = box.Max;
        points[3] = new Vector2(box.Min.X, box.Max.Y);

        return new Shape2D(ShapeKind2D.Box, points, 4, 0f, box);
    }

    /// <summary>An axis-aligned rectangle of <paramref name="size"/> whose lower corner is <paramref name="corner"/>.</summary>
    /// <exception cref="ArgumentException">The box it describes is one <see cref="Box(in Aabb2D)"/> refuses.</exception>
    public static Shape2D Box(Vector2 corner, Vector2 size) => Box(Aabb2D.FromCorner(corner, size));

    /// <summary>A convex polygon of three or four points, rounded by <paramref name="radius"/> when one is given.</summary>
    /// <remarks>
    /// Four points on the corners of an axis-aligned rectangle with a zero radius return a shape of
    /// kind <see cref="ShapeKind2D.Box"/>.
    /// </remarks>
    /// <param name="points">The hull's corners, convex and in either winding order.</param>
    /// <param name="radius">How far the polygon extends beyond that hull. Zero for a plain polygon.</param>
    /// <exception cref="ArgumentException">There are not three or four points, two of them nearly coincide, they are not strictly convex, or the bounds they and the radius describe are not a box a float can measure.</exception>
    public static Shape2D Polygon(ReadOnlySpan<Vector2> points, float radius = 0f)
    {
        Guard.Finite(radius, nameof(radius));
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        if (points.Length is < 3 or > MaxPoints)
        {
            throw new ArgumentException($"A polygon takes 3 to {MaxPoints} points, not {points.Length}.", nameof(points));
        }

        PointBuffer buffer = default;
        for (int index = 0; index < points.Length; index++)
        {
            Guard.Finite(points[index], nameof(points));
            buffer[index] = points[index];
        }

        RequireDistinct(buffer, points.Length, nameof(points));
        NormaliseWinding(ref buffer, points.Length);
        RequireConvex(buffer, points.Length, nameof(points));

        Aabb2D bounds = Bounded(buffer, points.Length, radius, nameof(radius));
        ShapeKind2D kind = radius == 0f && points.Length == 4 && IsCornersOf(buffer, bounds)
            ? ShapeKind2D.Box
            : ShapeKind2D.Polygon;

        return new Shape2D(kind, buffer, points.Length, radius, bounds);
    }

    // A grid cell or one of its faces. The grid already validated these coordinates, so the bounds
    // are taken unchecked. A cell with no thickness on an axis becomes a segment.
    internal static Shape2D OfCell(in Aabb2D cell)
    {
        if (cell.Min.X != cell.Max.X && cell.Min.Y != cell.Max.Y)
        {
            PointBuffer corners = default;
            corners[0] = cell.Min;
            corners[1] = new Vector2(cell.Max.X, cell.Min.Y);
            corners[2] = cell.Max;
            corners[3] = new Vector2(cell.Min.X, cell.Max.Y);

            return new Shape2D(ShapeKind2D.Box, corners, 4, 0f, cell);
        }

        PointBuffer ends = default;
        ends[0] = cell.Min;
        ends[1] = cell.Max;

        return new Shape2D(ShapeKind2D.Segment, ends, 2, 0f, cell);
    }

    /// <summary>The point at <paramref name="index"/>, in the shape's own space.</summary>
    public Vector2 Point(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);

        return _points[index];
    }

    /// <summary>This shape with every point moved by <paramref name="offset"/>.</summary>
    /// <exception cref="ArgumentException">The offset carries the shape's bounds past what a float box holds.</exception>
    public Shape2D Translated(Vector2 offset)
    {
        Guard.Finite(offset, nameof(offset));

        PointBuffer moved = _points;
        for (int index = 0; index < _count; index++)
        {
            moved[index] += offset;
        }

        return new Shape2D(Kind, moved, _count, Radius, Finite(Bounds.Translated(offset), nameof(offset)));
    }

    /// <summary>
    /// This shape with every point multiplied by <paramref name="scale"/> about the origin of its own
    /// space. The radius scales too, and a rounded shape needs the same factor on both axes.
    /// </summary>
    /// <exception cref="ArgumentException">The scale is non-uniform on a rounded shape, or the result is a shape construction would refuse.</exception>
    public Shape2D Scaled(Vector2 scale)
    {
        Guard.Positive(scale.X, nameof(scale));
        Guard.Positive(scale.Y, nameof(scale));

        if (Radius > 0f && scale.X != scale.Y)
        {
            throw new ArgumentException(
                $"A {Kind} is rounded, so it needs the same scale factor on both axes.",
                nameof(scale));
        }

        PointBuffer scaled = _points;
        for (int index = 0; index < _count; index++)
        {
            scaled[index] *= scale;
        }

        // The scale is uniform whenever the radius is non-zero, so either component works.
        float radius = Radius * scale.X;
        if (Radius > 0f && !(float.IsFinite(radius) && radius > 0f))
        {
            throw new ArgumentException("Scaled radius is no longer a positive finite distance.", nameof(scale));
        }

        // A positive scale preserves winding and convexity in exact arithmetic. In floats it can
        // still fold two corners together.
        if (_count == 2)
        {
            RequireApart(scaled, nameof(scale));
        }
        else if (_count >= 3)
        {
            RequireDistinct(scaled, _count, nameof(scale));
            RequireConvex(scaled, _count, nameof(scale));
        }

        return new Shape2D(Kind, scaled, _count, radius, Bounded(scaled, _count, radius, nameof(scale)));
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

    // The furthest point along a direction. Ties go to the lowest index so identical inputs walk the
    // same simplex.
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

    private static void RequireApart(in PointBuffer points, string parameterName)
    {
        if (Vector2.DistanceSquared(points[0], points[1]) <= PointTolerance * PointTolerance)
        {
            throw new ArgumentException(
                "Capsule endpoints are within the linear slop of each other. Use a circle instead.",
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
                        $"Polygon points {i} and {j} are closer together than the linear slop. Give each corner its own place.",
                        parameterName);
                }
            }
        }
    }

    // Normalises to cross-positive winding, so the outward normal of the edge from p[i] to p[i+1] is
    // (e.Y, -e.X). Reversing here lets a caller author either order.
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
                    $"Polygon corner {(index + 1) % count} is collinear or reflex. A shape must be strictly convex.",
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

    // Inputs can each be finite while the box they describe is not, and one infinite bound unions its
    // way up the broadphase and hides unrelated colliders. The extent is checked alongside the
    // corners because the tree's surface-area heuristic sums it and it overflows first.
    private static Aabb2D Finite(in Aabb2D bounds, string parameterName) =>
        Aabb2D.IsFinite(bounds.Min) && Aabb2D.IsFinite(bounds.Max) && float.IsFinite(bounds.Perimeter)
            ? bounds
            : throw new ArgumentException("The shape's bounds are not a box a float can measure.", parameterName);

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
