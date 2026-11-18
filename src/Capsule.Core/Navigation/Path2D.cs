using System.Globalization;
using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Navigation;

/// <summary>A run of straight legs through two or more points, measured by distance along it.</summary>
/// <remarks>
/// A path whose last point equals its first is <see cref="Closed"/>. A path never changes once built, and any
/// number of followers share one. Reading it allocates nothing. A path measures each leg by its straight length
/// unless it is built with a measure of the caller's own, which paces a follower by it instead.
/// </remarks>
/// <example>
/// A square loop, closed by repeating its first point:
/// <code>
/// Path2D loop = new(new(0f, 0f), new(32f, 0f), new(32f, 32f), new(0f, 32f), new(0f, 0f));
/// Vector2 halfway = loop.PointAt(loop.Length / 2f);
/// </code>
/// A loop whose diagonal is measured by its longer axis, which a follower rides at its speed on both axes:
/// <code>
/// Path2D paced = new([new(0f, 0f), new(32f, 32f), new(0f, 32f), new(0f, 0f)], static (from, to) =>
/// {
///     Vector2 leg = Vector2.Abs(to - from);
///     return MathF.Max(leg.X, leg.Y);
/// });
/// </code>
/// </example>
public sealed class Path2D
{
    private readonly Vector2[] _points;
    private readonly float[] _distances;

    /// <summary>Whether the path ends where it starts, its last point equal to its first.</summary>
    public bool Closed { get; }

    /// <summary>The smallest rect holding every point.</summary>
    public Rect Bounds { get; }

    /// <summary>Creates a path through a copy of <paramref name="points"/>, in order, measured by the straight length of each leg.</summary>
    /// <param name="points">At least two finite points, not all the same.</param>
    public Path2D(params ReadOnlySpan<Vector2> points)
        : this(points, Vector2.Distance)
    {
    }

    /// <summary>Creates a path through a copy of <paramref name="points"/>, in order, with each leg as long as <paramref name="measure"/> says.</summary>
    /// <remarks>
    /// <see cref="Length"/>, <see cref="PointAt"/> and a follower's speed all read in the measure's unit. The measure
    /// runs once a leg while the path is built. A point repeated with a leg of some length holds a follower on it.
    /// </remarks>
    /// <param name="points">At least two finite points, not all the same.</param>
    /// <param name="measure">How long the leg from one point to the next is: 0 or more, and more than 0 between points that differ.</param>
    public Path2D(ReadOnlySpan<Vector2> points, Func<Vector2, Vector2, float> measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        if (points.Length < 2)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"A path runs through at least two points, and this one has {points.Length}. Add a point."),
                nameof(points));
        }

        _points = points.ToArray();
        _distances = new float[_points.Length];
        Vector2 min = _points[0];
        Vector2 max = _points[0];
        for (int i = 0; i < _points.Length; i++)
        {
            Vector2 point = _points[i];
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Point {i} of the path is {point}. Write every coordinate as a finite number."),
                    nameof(points));
            }

            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
            if (i > 0)
            {
                _distances[i] = _distances[i - 1] + Leg(measure, i);
            }
        }

        if (min == max)
        {
            throw new ArgumentException("Every point of the path is the same, and a path needs length. Move a point apart from the others.", nameof(points));
        }

        if (!float.IsFinite(Length))
        {
            throw new ArgumentException("The path is too long to measure. Bring its points closer together.", nameof(points));
        }

        Bounds = Rect.FromEdges(min.X, min.Y, max.X, max.Y);
        Closed = _points[^1] == _points[0];
    }

    /// <summary>The points the path runs through, in order.</summary>
    public ReadOnlySpan<Vector2> Points => _points;

    /// <summary>Each point's distance along the path from the first, in step with <see cref="Points"/>, in the path's unit.</summary>
    public ReadOnlySpan<float> Distances => _distances;

    /// <summary>The distance along the path from its first point to its last.</summary>
    public float Length => _distances[^1];

    /// <summary>The point <paramref name="distance"/> along the path from its first point.</summary>
    /// <remarks>A distance before the start reads the first point, and one past <see cref="Length"/> reads the last.</remarks>
    public Vector2 PointAt(float distance)
    {
        Guard.Finite(distance, nameof(distance));

        // The last point whose distance is no more than the one asked for starts the leg holding it.
        int from = 0;
        int to = _points.Length - 1;
        while (to - from > 1)
        {
            int middle = (from + to) >>> 1;
            if (_distances[middle] <= distance)
            {
                from = middle;
            }
            else
            {
                to = middle;
            }
        }

        // A repeated point holds a follower exactly on it, where a blend of the two would round off it.
        if (_points[from] == _points[to])
        {
            return _points[to];
        }

        // A leg too short to add to a long path's distance spans none of it, and is passed at once.
        float span = _distances[to] - _distances[from];
        float amount = span > 0f ? Math.Clamp((distance - _distances[from]) / span, 0f, 1f) : 1f;

        return DeterministicMath.Lerp(_points[from], _points[to], amount);
    }

    // The leg into point `to` as measure gives it, refused when no follower could ride it.
    private float Leg(Func<Vector2, Vector2, float> measure, int to)
    {
        float leg = measure(_points[to - 1], _points[to]);
        if (!(leg >= 0f))
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The leg from point {to - 1} to {to} of the path measures {leg}. Measure every leg as 0 or more."),
                nameof(measure));
        }

        if (leg == 0f && _points[to - 1] != _points[to])
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Points {to - 1} and {to} of the path differ, but the leg between them measures 0. Move them further apart."),
                nameof(measure));
        }

        return leg;
    }
}
