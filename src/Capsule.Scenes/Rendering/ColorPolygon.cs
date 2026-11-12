using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>Draws its entity as one convex polygon of flat colour.</summary>
/// <remarks>
/// The points are in the entity's own units, placed by its world transform: its position, turn and scale.
/// Between steps each point moves in a straight line from where the entity's last step left it, and a new
/// outline shows from the step that sets it. Coordinates are Y-down, in world units under a world root and canvas
/// pixels under a screen root.
/// <para>
/// It draws the engine's white texel and loads no asset. The same points can shape a
/// <see cref="PolygonCollider2D"/>, and what is drawn is then what collides.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// ColorPolygon fill = Add(new ColorPolygon([Vector2.Zero, new Vector2(-40f, 120f), new Vector2(40f, 120f)])
/// {
///     Color = ColorRgba.Yellow,
///     Blend = BlendMode.Additive,
/// });
/// </code>
/// </example>
public sealed class ColorPolygon : Renderer
{
    // An edge within this share of the outline's size is rounding between two copies of one point, and a corner
    // within this share of a right angle is straight. Both scale with the outline, and the check reads the same at
    // any size.
    private const float RepeatedPoint = 1e-5f;
    private const double StraightCorner = 1e-4;

    private Vector2[] _points = [];

    // The outline placed at both ends of the step for the frame, the previous points first.
    private Vector2[] _placed = [];

    private int _count;

    /// <summary>The fill colour, in straight alpha. White and opaque by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>How the polygon's colour combines with what is already drawn. Alpha by default.</summary>
    public BlendMode Blend { get; set; }

    /// <summary>A polygon with no points, which draws nothing until <see cref="SetPoints"/> gives it an outline.</summary>
    public ColorPolygon()
    {
    }

    /// <summary>A polygon with the outline <paramref name="points"/> describes. See <see cref="SetPoints"/>.</summary>
    /// <param name="points">The outline, in the entity's own units.</param>
    public ColorPolygon(ReadOnlySpan<Vector2> points) => SetPoints(points);

    /// <summary>The outline's points in order, in the entity's own units. Empty until an outline is set.</summary>
    public ReadOnlySpan<Vector2> Points => _points.AsSpan(0, _count);

    internal override bool Steps => false;

    /// <summary>
    /// The rect the outline covers, under the rules <see cref="Renderer.Bounds"/> states. Reads empty while the
    /// polygon has no outline.
    /// </summary>
    public override Rect Bounds
    {
        get
        {
            if (Entity is null || _count == 0)
            {
                return default;
            }

            Transform2D transform = RenderTransform;
            Vector2 min = transform.TransformPoint(_points[0]);
            Vector2 max = min;
            for (int index = 1; index < _count; index++)
            {
                Vector2 point = transform.TransformPoint(_points[index]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Rect.FromCorners(min, max);
        }
    }

    /// <summary>Replaces the outline with a copy of <paramref name="points"/>, in the entity's own units.</summary>
    /// <remarks>
    /// The points run in order around a convex outline, either way round, three of them or more. Points along
    /// one line, and an outline with no area, are allowed and draw nothing where they have no width, as a shape
    /// that opens from nothing does. An outline no longer than one the polygon has held allocates nothing.
    /// </remarks>
    /// <param name="points">The outline's points, in order.</param>
    /// <exception cref="ArgumentException">There are fewer than three points, a point is not finite, or the outline is not convex.</exception>
    public void SetPoints(ReadOnlySpan<Vector2> points)
    {
        if (points.Length < 3)
        {
            throw new ArgumentException($"A polygon takes three points or more, not {points.Length}.", nameof(points));
        }

        for (int index = 0; index < points.Length; index++)
        {
            _ = Guard.Finite(points[index], nameof(points));
        }

        RequireConvex(points);
        if (_points.Length < points.Length)
        {
            _points = new Vector2[points.Length];
            _placed = new Vector2[points.Length * 2];
        }

        points.CopyTo(_points);
        _count = points.Length;
    }

    /// <inheritdoc/>
    protected internal override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (_count == 0)
        {
            return;
        }

        Transform2D previous = PreviousRenderTransform;
        Transform2D current = RenderTransform;
        Span<Vector2> before = _placed.AsSpan(0, _count);
        Span<Vector2> now = _placed.AsSpan(_count, _count);
        for (int index = 0; index < _count; index++)
        {
            before[index] = previous.TransformPoint(_points[index]);
            now[index] = current.TransformPoint(_points[index]);
        }

        view.Add(new PolygonIntent(before, now, Color, Blend));
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Points", _count);
        panel.Field("Color", Color);
        panel.Field("Blend", Blend.ToString());
    }

    // Throws unless the points turn one way at every corner and go once around. A turn is read between the edges
    // longer than a repeated point's, and a repeated point hides none. An outline that turns one way can still wind
    // twice, as a five-pointed star does, and then its edges' direction changes sign more than twice on an axis.
    private static void RequireConvex(ReadOnlySpan<Vector2> points)
    {
        float size = 0f;
        foreach (Vector2 point in points)
        {
            Vector2 away = Vector2.Abs(point - points[0]);
            size = MathF.Max(size, MathF.Max(away.X, away.Y));
        }

        float repeated = size * RepeatedPoint;
        int count = points.Length;
        int turn = 0;
        int firstStart = -1;
        Vector2 first = default;
        Vector2 last = default;
        Signs across = new(repeated);
        Signs down = new(repeated);
        for (int index = 0; index < count; index++)
        {
            Vector2 edge = points[(index + 1) % count] - points[index];
            if (MathF.Abs(edge.X) <= repeated && MathF.Abs(edge.Y) <= repeated)
            {
                continue;
            }

            if (firstStart < 0)
            {
                firstStart = index;
                first = edge;
            }
            else
            {
                turn = Turn(last, edge, index, turn);
            }

            last = edge;
            across.Add(edge.X);
            down.Add(edge.Y);
        }

        if (firstStart >= 0)
        {
            _ = Turn(last, first, firstStart, turn);
        }

        if (across.Changes > 2 || down.Changes > 2)
        {
            throw new ArgumentException(
                "The outline goes around more than once, and a polygon is convex. List its points once around a convex outline.",
                "points");
        }
    }

    // The side the outline turns to at point, from edge to next, which must be the side it has turned so far.
    private static int Turn(Vector2 edge, Vector2 next, int point, int turn)
    {
        double cross = ((double)edge.X * next.Y) - ((double)edge.Y * next.X);
        double straight = StraightCorner * Math.Sqrt((double)edge.LengthSquared() * next.LengthSquared());
        int side = cross > straight ? 1 : cross < -straight ? -1 : 0;
        if (side != 0 && turn != 0 && side != turn)
        {
            throw new ArgumentException(
                $"The outline turns back at point {point}, and a polygon is convex. List its points in order around a convex outline.",
                "points");
        }

        return side != 0 ? side : turn;
    }

    // The sign changes along a closed run of values, those within zero of zero counting as neither sign.
    private struct Signs(float zero)
    {
        private readonly float _zero = zero;
        private int _first;
        private int _last;
        private int _changes;

        internal readonly int Changes => _changes + (_first != 0 && _last != _first ? 1 : 0);

        internal void Add(float value)
        {
            int sign = value > _zero ? 1 : value < -_zero ? -1 : 0;
            if (sign == 0)
            {
                return;
            }

            if (_last != 0 && sign != _last)
            {
                _changes++;
            }

            _first = _first == 0 ? sign : _first;
            _last = sign;
        }
    }
}
