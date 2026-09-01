using System.Numerics;
using System.Runtime.CompilerServices;

namespace Capsule.Collision;

/// <summary>
/// One convex collision shape, stored the way the narrowphase reads it: a point set and a radius,
/// so every shape is the region within <see cref="Radius"/> of the convex hull of its points. A
/// shape carries no angle — rotation stays render-side — and is validated on construction, so a
/// shape built by one of the factories is one every query accepts.
/// <para>
/// The struct's default value is the one exception: it holds no points and so is no region at all.
/// Every seam that takes a shape rejects it with <see cref="ArgumentException"/> rather than
/// answering about an empty point set.
/// </para>
/// </summary>
public readonly struct Shape : IEquatable<Shape>
{
    /// <summary>The most points a shape may hold.</summary>
    public const int MaxPoints = 8;

    // Points closer together than this, and polygon corners flatter than its square, are read as
    // authoring mistakes rather than geometry: neither has a well-defined outward direction.
    private const float PointTolerance = CollisionWorld.LinearSlop;

    private readonly PointBuffer _points;
    private readonly int _count;

    private Shape(ShapeKind kind, in PointBuffer points, int count, float radius, in Aabb bounds)
    {
        Kind = kind;
        _points = points;
        _count = count;
        Radius = radius;
        Bounds = bounds;
    }

    /// <summary>Which member of the shape union this is.</summary>
    public ShapeKind Kind { get; }

    /// <summary>How far the shape extends beyond the hull of its points, in world units.</summary>
    public float Radius { get; }

    /// <summary>
    /// How many points the shape holds: one for a circle, two for a capsule, three or more
    /// otherwise, and zero for the default value, which is no shape.
    /// </summary>
    public int PointCount => _count;

    /// <summary>The shape's own bounds, in the space its points are expressed in, radius included.</summary>
    public Aabb Bounds { get; }

    /// <summary>A circle of <paramref name="radius"/> around <paramref name="center"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and positive, or the centre is not finite.</exception>
    /// <exception cref="ArgumentException">The centre and radius together overflow the shape's bounds.</exception>
    public static Shape Circle(Vector2 center, float radius)
    {
        RequirePositiveRadius(radius);
        RequireFinite(center, nameof(center));

        PointBuffer points = default;
        points[0] = center;

        return new Shape(ShapeKind.Circle, points, 1, radius, Bounded(points, 1, radius, nameof(radius)));
    }

    /// <summary>Everything within <paramref name="radius"/> of the segment from <paramref name="start"/> to <paramref name="end"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and positive, or an endpoint is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// The endpoints coincide — a capsule of no length is a circle — or something derived from
    /// them is not a number: the segment between them, its squared length, or the bounds and
    /// extent the endpoints and radius together describe.
    /// </exception>
    public static Shape Capsule(Vector2 start, Vector2 end, float radius)
    {
        RequirePositiveRadius(radius);
        RequireFinite(start, nameof(start));
        RequireFinite(end, nameof(end));

        if (Vector2.DistanceSquared(start, end) <= PointTolerance * PointTolerance)
        {
            throw new ArgumentException(
                "A capsule's endpoints must be further apart than the linear slop; a capsule of no length is a circle.",
                nameof(end));
        }

        PointBuffer points = default;
        points[0] = start;
        points[1] = end;

        // The segment between the endpoints is what the narrowphase measures along, so its length
        // has to be a number even when both ends are.
        RequireFiniteGeometry(points, 2, nameof(end));

        return new Shape(ShapeKind.Capsule, points, 2, radius, Bounded(points, 2, radius, nameof(radius)));
    }

    /// <summary>An axis-aligned rectangle covering <paramref name="box"/>.</summary>
    /// <exception cref="ArgumentException">
    /// The box is inverted, spans nothing on an axis, or spans more than a float can measure.
    /// </exception>
    public static Shape Box(in Aabb box)
    {
        RequireFinite(box.Min, nameof(box));
        RequireFinite(box.Max, nameof(box));

        // The span test is where the extent is already in hand, so it is also where an unmeasurable
        // one is caught: a box reaching from one end of the float range to the other has corners
        // that are real numbers and a width that is not, and an infinite width passes a
        // greater-than test on its way into the mover's inset and the tree's area heuristic. The
        // sum covers both axes and their total, and the negated comparisons refuse a NaN with them.
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

        // The box is its own bounds, already checked finite corner by corner and expanded by no
        // radius, so there is nothing left to derive or to re-test.
        return new Shape(ShapeKind.Box, points, 4, 0f, box);
    }

    /// <summary>
    /// An axis-aligned rectangle of <paramref name="size"/> whose lower corner is
    /// <paramref name="corner"/> — the anchor a <c>QuadRenderer</c> draws from.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The size spans nothing on an axis, or the box it describes spans more than a float can
    /// measure on an axis or across both.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="size"/> is negative or not finite.</exception>
    public static Shape Box(Vector2 corner, Vector2 size) => Box(Aabb.FromCorner(corner, size));

    /// <summary>
    /// A convex polygon of three to eight points, rounded by <paramref name="radius"/> when one is
    /// given. Winding is normalised on construction, so either order is accepted.
    /// </summary>
    /// <param name="points">The hull's corners, convex and in either winding order.</param>
    /// <param name="radius">How far the polygon extends beyond that hull; zero for a plain polygon.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is negative or not finite.</exception>
    /// <exception cref="ArgumentException">
    /// There are not three to eight points, a point is not finite, two points nearly coincide, the
    /// points are not strictly convex, or something derived from them is not a number: an edge
    /// vector or its squared length, the twice-signed area the winding is decided by, a corner's
    /// cross product, or the bounds and extent the points and radius together describe.
    /// </exception>
    public static Shape Polygon(ReadOnlySpan<Vector2> points, float radius = 0f)
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

        RequireDistinct(buffer, points.Length);

        // Before the winding is normalised and the corners are tested, because both compute the
        // very products this refuses; reversing the winding only negates them, so the answer here
        // holds for either order.
        RequireFiniteGeometry(buffer, points.Length, nameof(points));

        NormaliseWinding(ref buffer, points.Length);
        RequireConvex(buffer, points.Length);

        Aabb bounds = Bounded(buffer, points.Length, radius, nameof(radius));
        ShapeKind kind = radius == 0f && points.Length == 4 && IsCornersOf(buffer, bounds)
            ? ShapeKind.Box
            : ShapeKind.Polygon;

        return new Shape(kind, buffer, points.Length, radius, bounds);
    }

    // A bare segment: a hull of two points with no radius, which is what a one-way tile edge is.
    // Never public — every shape a game can build has an interior — and never routed through the
    // rounded-hull ray casts, which would read its zero radius as a polygon.
    internal static Shape Segment(Vector2 start, Vector2 end)
    {
        PointBuffer points = default;
        points[0] = start;
        points[1] = end;

        return new Shape(ShapeKind.Capsule, points, 2, 0f, Bounded(points, 2, 0f, nameof(start)));
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
    /// The offset carries the shape's bounds past what a float box holds, or places the shape where
    /// the coordinates are coarser than the shape is small: its bounds would collapse to a line on
    /// an axis it has extent on, or two of the points its hull is built from would land on each
    /// other.
    /// </exception>
    public Shape Translated(Vector2 offset)
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

        Aabb bounds = Finite(Bounds.Translated(offset), nameof(offset));
        RequireExtentSurvives(Bounds.Size, bounds.Size);

        if (Kind is ShapeKind.Capsule or ShapeKind.Polygon)
        {
            RequireDistinctSurvives(_points, moved, _count);
        }

        return new Shape(Kind, moved, _count, Radius, bounds);
    }

    // Far enough out, the floats either side of a small shape are the same float. The narrowphase
    // would still hold its radius and its hull, but the broadphase box it is pruned by has folded
    // to a line, so the tree skips geometry the shape still represents and a query misses it.
    private static void RequireExtentSurvives(Vector2 was, Vector2 now)
    {
        if ((was.X > 0f && !(now.X > 0f)) || (was.Y > 0f && !(now.Y > 0f)))
        {
            throw new ArgumentException(
                "The shape has no extent left once it is placed there; the coordinate is coarser than the shape is wide, so its bounds collapse to a line.",
                "offset");
        }
    }

    // The same rounding seen from the hull's side. A box's corners are distinct exactly when its
    // bounds have extent, and a circle has one point, so only the shapes carrying a hull of their
    // own are walked — and only pairs that were distinct before are held to being distinct after.
    private static void RequireDistinctSurvives(in PointBuffer was, in PointBuffer now, int count)
    {
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if (was[i] != was[j] && now[i] == now[j])
                {
                    throw new ArgumentException(
                        $"Points {i} and {j} land on the same coordinate once the shape is placed there; the hull it is built from would have an edge with no direction.",
                        "offset");
                }
            }
        }
    }

    /// <inheritdoc/>
    public bool Equals(Shape other)
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
    public override bool Equals(object? obj) => obj is Shape other && Equals(other);

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
    public static bool operator ==(Shape left, Shape right) => left.Equals(right);

    /// <summary>Whether two shapes differ in kind, radius or points.</summary>
    public static bool operator !=(Shape left, Shape right) => !left.Equals(right);

    // The furthest point along a direction, ties going to the lowest index so a query over
    // identical inputs walks the identical simplex.
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

    private static void RequireDistinct(in PointBuffer points, int count)
    {
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if (Vector2.DistanceSquared(points[i], points[j]) <= PointTolerance * PointTolerance)
                {
                    throw new ArgumentException(
                        $"A polygon's points {i} and {j} are closer together than the linear slop; every corner must be its own.",
                        "points");
                }
            }
        }
    }

    // Cross-positive winding throughout, so the outward normal of the edge from p[i] to p[i+1] is
    // always (e.Y, -e.X). Reversing here is what lets a caller author either order.
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

    private static void RequireConvex(in PointBuffer points, int count)
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
                    "points");
            }
        }
    }

    private static Aabb Bounded(in PointBuffer points, int count, float radius, string parameterName)
    {
        Vector2 min = points[0];
        Vector2 max = min;
        for (int index = 1; index < count; index++)
        {
            min = Vector2.Min(min, points[index]);
            max = Vector2.Max(max, points[index]);
        }

        return Finite(new Aabb(min, max).Expanded(radius), parameterName);
    }

    // Each input can be finite while the box they describe is not: a point out at the edge of the
    // float range, widened by a radius, overflows. The broadphase unions boxes as it balances, so
    // one infinite bound there spreads to nodes holding unrelated colliders and makes them
    // unfindable — which is why a shape that cannot state its own extent never gets built.
    //
    // The corners are not the whole of it. A box spanning the range from one end to the other has
    // finite corners and an infinite width, and that width is what the mover subtracts its inset
    // from and what the tree's surface-area heuristic sums; both would go on to compute with an
    // infinity. Every quantity derived from the bounds is checked here, not at the query that
    // reads it.
    private static Aabb Finite(in Aabb bounds, string parameterName)
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
    // cross products the corner tests and the outward normals come from. Two corners can each be a
    // real float with an infinity between them, and an infinity here does not stop the shape being
    // built — it becomes a NaN normal and a wrong answer at query time instead.
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

    private static bool IsCornersOf(in PointBuffer points, in Aabb bounds)
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
