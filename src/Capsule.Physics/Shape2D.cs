using System.Numerics;
using System.Runtime.CompilerServices;
using Capsule.Physics.Internal;

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
    public const int MaxPoints = 64;

    // How many points a shape holds in place.
    private const int InlinePoints = 4;

    // Points closer together than this, and corners flatter than its square, have no well-defined
    // outward direction and are refused.
    internal const float PointTolerance = CollisionTolerance.LinearSlop;

    // A polygon of more than InlinePoints corners keeps them in HullStore2D, in the space it was built
    // in, and holds their index here. Its _points[0] holds how far it has been moved from that space.
    private readonly int _hull;
    private readonly PointBuffer _points;
    private readonly byte _count;

    /// <summary>Which <see cref="ShapeKind2D"/> this shape is.</summary>
    public ShapeKind2D Kind { get; }

    /// <summary>How far the shape extends beyond the hull of its points, in world units.</summary>
    public float Radius { get; }

    /// <summary>The shape's bounds in the space its points are expressed in, radius included.</summary>
    public Aabb2D Bounds { get; }

    private Shape2D(ShapeKind2D kind, in PointBuffer points, int count, float radius, in Aabb2D bounds, int hull = 0)
    {
        Kind = kind;
        _hull = hull;
        _points = points;
        _count = (byte)count;
        Radius = radius;
        Bounds = bounds;
    }

    /// <summary>
    /// How many points the shape holds. One for a circle, two for a capsule, three to
    /// <see cref="MaxPoints"/> otherwise, and zero for the default value.
    /// </summary>
    public int PointCount => _count;

    /// <summary>A circle of <paramref name="radius"/> around <paramref name="center"/>.</summary>
    /// <exception cref="ArgumentException">The centre and radius together span more than a float box holds.</exception>
    public static Shape2D Circle(Vector2 center, float radius)
    {
        Guard.Positive(radius, nameof(radius));
        Guard.Finite(center, nameof(center));

        PointBuffer points = default;
        points[0] = center;

        return new Shape2D(ShapeKind2D.Circle, points, 1, radius, Bounded(points[..1], radius, nameof(radius)));
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

        return new Shape2D(ShapeKind2D.Capsule, points, 2, radius, Bounded(points[..2], radius, nameof(radius)));
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

        return Rectangle(box);
    }

    /// <summary>An axis-aligned rectangle of <paramref name="size"/> whose lower corner is <paramref name="corner"/>.</summary>
    /// <exception cref="ArgumentException">The box it describes is one <see cref="Box(in Aabb2D)"/> refuses.</exception>
    public static Shape2D Box(Vector2 corner, Vector2 size) => Box(Aabb2D.FromCorner(corner, size));

    /// <summary>A convex polygon of three to <see cref="MaxPoints"/> points, rounded by <paramref name="radius"/> when one is given.</summary>
    /// <remarks>
    /// Four points on the corners of an axis-aligned rectangle with a zero radius return a shape of
    /// kind <see cref="ShapeKind2D.Box"/>. The points of a polygon past four are kept once per distinct
    /// polygon for the life of the process. Build a changing polygon once, not every step.
    /// </remarks>
    /// <param name="points">The hull's corners, convex and in either winding order.</param>
    /// <param name="radius">How far the polygon extends beyond that hull. Zero for a plain polygon.</param>
    /// <exception cref="ArgumentException">There are fewer than three points or more than <see cref="MaxPoints"/>, two of them nearly coincide, they are not strictly convex, or the bounds they and the radius describe are not a box a float can measure.</exception>
    public static Shape2D Polygon(ReadOnlySpan<Vector2> points, float radius = 0f)
    {
        Guard.Finite(radius, nameof(radius));
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        if (points.Length is < 3 or > MaxPoints)
        {
            throw new ArgumentException($"A polygon takes 3 to {MaxPoints} points, not {points.Length}.", nameof(points));
        }

        PointBuffer buffer = default;
        Span<Vector2> corners = points.Length > InlinePoints ? stackalloc Vector2[points.Length] : buffer[..points.Length];
        for (int index = 0; index < points.Length; index++)
        {
            Guard.Finite(points[index], nameof(points));
            corners[index] = points[index];
        }

        RequireDistinct(corners, nameof(points));
        NormaliseWinding(corners);
        RequireConvex(corners, nameof(points));

        Aabb2D bounds = Bounded(corners, radius, nameof(radius));
        ShapeKind2D kind = radius == 0f && points.Length == 4 && IsCornersOf(corners, bounds)
            ? ShapeKind2D.Box
            : ShapeKind2D.Polygon;

        int hull = points.Length > InlinePoints ? HullStore2D.Add(corners) : 0;

        return new Shape2D(kind, buffer, points.Length, radius, bounds, hull);
    }

    // A grid cell or one of its faces. The grid already validated these coordinates, and the bounds
    // are taken unchecked. A cell with no thickness on an axis becomes a segment.
    internal static Shape2D OfCell(in Aabb2D cell) =>
        cell.Min.X != cell.Max.X && cell.Min.Y != cell.Max.Y ? Rectangle(cell) : Segment(cell.Min, cell.Max);

    // A point query's probe, a circle of no radius. The caller validated the point.
    internal static Shape2D OfPoint(Vector2 point)
    {
        PointBuffer points = default;
        points[0] = point;

        return new Shape2D(ShapeKind2D.Circle, points, 1, 0f, new Aabb2D(point, point));
    }

    // Whether point lies inside the shape or on its edge, as a point query finds it, in the space its points
    // are in. The caller validated the point.
    internal bool Contains(Vector2 point) =>
        point.X >= Bounds.Min.X && point.X <= Bounds.Max.X && point.Y >= Bounds.Min.Y && point.Y <= Bounds.Max.Y
        && Narrowphase2D.Separation(OfPoint(point), this, out _, out _) <= 0f;

    // One edge of a grid cell, from start to end at any angle. The grid validated both points.
    internal static Shape2D Segment(Vector2 start, Vector2 end)
    {
        PointBuffer ends = default;
        ends[0] = start;
        ends[1] = end;

        return new Shape2D(ShapeKind2D.Segment, ends, 2, 0f, new Aabb2D(Vector2.Min(start, end), Vector2.Max(start, end)));
    }

    /// <summary>The point at <paramref name="index"/>, in the shape's own space.</summary>
    public Vector2 Point(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);

        return PointAt(index);
    }

    /// <summary>This shape with every point moved by <paramref name="offset"/>.</summary>
    /// <exception cref="ArgumentException">The offset carries the shape's bounds past what a float box holds.</exception>
    public Shape2D Translated(Vector2 offset)
    {
        Guard.Finite(offset, nameof(offset));

        // Unrolled over constant indices, which need no bounds checks. Every mover translates its shape
        // on each sweep pass. A stored hull moves only its offset. Its points after two moves round as one
        // move by their sum.
        PointBuffer moved = _points;
        if (_hull != 0)
        {
            moved[0] += offset;
        }
        else if (_count > 0)
        {
            moved[0] += offset;
            if (_count > 1)
            {
                moved[1] += offset;
                if (_count > 2)
                {
                    moved[2] += offset;
                    if (_count > 3)
                    {
                        moved[3] += offset;
                    }
                }
            }
        }

        return new Shape2D(Kind, moved, _count, Radius, Finite(Bounds.Translated(offset), nameof(offset)), _hull);
    }

    /// <summary>
    /// This shape with every point multiplied by <paramref name="scale"/> about the origin of its own
    /// space. The radius scales too, and a rounded shape needs the same factor on both axes.
    /// </summary>
    /// <exception cref="ArgumentException">The shape is the default value, the scale is non-uniform on a rounded shape, or the result is a shape construction would refuse.</exception>
    public Shape2D Scaled(Vector2 scale)
    {
        RequireBuilt(this, null);
        Guard.Positive(scale.X, nameof(scale));
        Guard.Positive(scale.Y, nameof(scale));

        if (Radius > 0f && scale.X != scale.Y)
        {
            throw new ArgumentException(
                $"A {Kind} is rounded and needs the same scale factor on both axes. Pass a uniform scale.",
                nameof(scale));
        }

        PointBuffer buffer = default;
        Span<Vector2> scaled = _hull != 0 ? stackalloc Vector2[_count] : buffer[.._count];
        for (int index = 0; index < _count; index++)
        {
            scaled[index] = PointAt(index) * scale;
        }

        // The scale is uniform whenever the radius is non-zero. Either component works.
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
            RequireDistinct(scaled, nameof(scale));
            RequireConvex(scaled, nameof(scale));
        }

        return new Shape2D(Kind, buffer, _count, radius, Bounded(scaled, radius, nameof(scale)), _hull != 0 ? HullStore2D.Add(scaled) : 0);
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
            if (PointAt(index) != other.PointAt(index))
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
            hash.Add(PointAt(index));
        }

        return hash.ToHashCode();
    }

    /// <summary>Whether two shapes hold the same kind, radius and points.</summary>
    public static bool operator ==(Shape2D left, Shape2D right) => left.Equals(right);

    /// <summary>Whether two shapes differ in kind, radius or points.</summary>
    public static bool operator !=(Shape2D left, Shape2D right) => !left.Equals(right);

    // Refuses the default value, which holds no points.
    internal static void RequireBuilt(in Shape2D shape, string? parameterName)
    {
        if (shape._count == 0)
        {
            throw new ArgumentException(
                "Shape is a default Shape2D. Build one with Shape2D.Box, Circle, Capsule or Polygon.",
                parameterName);
        }
    }

    // The furthest point along a direction.
    internal Vector2 Support(Vector2 direction) => PointAt(SupportIndex(direction));

    // The index of the furthest point along a direction. Ties go to the lowest index so identical
    // inputs walk the same simplex.
    internal int SupportIndex(Vector2 direction)
    {
        if (_hull != 0)
        {
            return HullSupportIndex(direction);
        }

        int best = 0;
        float bestDot = Vector2.Dot(_points[0], direction);

        for (int index = 1; index < _count; index++)
        {
            float dot = Vector2.Dot(_points[index], direction);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = index;
            }
        }

        return best;
    }

    internal Vector2 PointAt(int index) => _hull == 0 ? _points[index] : HullStore2D.Get(_hull)[index] + _points[0];

    // The outward unit normal of the edge leaving point index.
    internal Vector2 EdgeNormal(int index)
    {
        Vector2 edge = PointAt((index + 1) % _count) - PointAt(index);

        return Vector2.Normalize(new Vector2(edge.Y, -edge.X));
    }

    private int HullSupportIndex(Vector2 direction)
    {
        Vector2[] hull = HullStore2D.Get(_hull);
        Vector2 offset = _points[0];
        int best = 0;
        float bestDot = Vector2.Dot(hull[0] + offset, direction);

        for (int index = 1; index < hull.Length; index++)
        {
            float dot = Vector2.Dot(hull[index] + offset, direction);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = index;
            }
        }

        return best;
    }

    // A box's four corners, in the winding every polygon is normalised to.
    private static Shape2D Rectangle(in Aabb2D box)
    {
        PointBuffer points = default;
        points[0] = box.Min;
        points[1] = new Vector2(box.Max.X, box.Min.Y);
        points[2] = box.Max;
        points[3] = new Vector2(box.Min.X, box.Max.Y);

        return new Shape2D(ShapeKind2D.Box, points, 4, 0f, box);
    }

    private static void RequireApart(ReadOnlySpan<Vector2> points, string parameterName)
    {
        if (Vector2.DistanceSquared(points[0], points[1]) <= PointTolerance * PointTolerance)
        {
            throw new ArgumentException(
                "Capsule endpoints are within the linear slop of each other. Use a circle instead.",
                parameterName);
        }
    }

    private static void RequireDistinct(ReadOnlySpan<Vector2> points, string parameterName)
    {
        int count = points.Length;
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

    // Normalises to cross-positive winding. The outward normal of the edge from p[i] to p[i+1] is then
    // (e.Y, -e.X). Reversing here lets a caller author either order.
    private static void NormaliseWinding(Span<Vector2> points)
    {
        int count = points.Length;
        float twiceArea = 0f;
        for (int index = 0; index < count; index++)
        {
            twiceArea += Math2D.Cross(points[index], points[(index + 1) % count]);
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

    private static void RequireConvex(ReadOnlySpan<Vector2> points, string parameterName)
    {
        int count = points.Length;
        for (int index = 0; index < count; index++)
        {
            Vector2 current = points[(index + 1) % count];
            if (Math2D.Cross(current - points[index], points[(index + 2) % count] - current) <= PointTolerance * PointTolerance)
            {
                throw new ArgumentException(
                    $"Polygon corner {(index + 1) % count} is collinear or reflex. A shape must be strictly convex.",
                    parameterName);
            }
        }

        // Corners that all turn one way still admit a star, whose outline winds round more than once. Every
        // corner of a simple convex outline lies inside every edge.
        for (int edge = 0; edge < count; edge++)
        {
            Vector2 start = points[edge];
            Vector2 along = points[(edge + 1) % count] - start;
            for (int corner = 0; corner < count; corner++)
            {
                if (corner != edge && corner != (edge + 1) % count && Math2D.Cross(along, points[corner] - start) <= 0f)
                {
                    throw new ArgumentException(
                        "Polygon points wind round the hull more than once. List the corners in order around it.",
                        parameterName);
                }
            }
        }
    }

    private static Aabb2D Bounded(ReadOnlySpan<Vector2> points, float radius, string parameterName)
    {
        int count = points.Length;
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

    private static bool IsCornersOf(ReadOnlySpan<Vector2> points, in Aabb2D bounds)
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

    [InlineArray(InlinePoints)]
    private struct PointBuffer
    {
        private Vector2 _element0;
    }
}
