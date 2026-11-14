using System.Globalization;
using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Navigation;

/// <summary>A run of straight segments through two or more points, measured by distance along it.</summary>
/// <remarks>
/// A path whose last point equals its first is <see cref="Closed"/>. A path never changes once built, and any
/// number of followers share one. Reading it allocates nothing.
/// </remarks>
/// <example>
/// A square loop, closed by repeating its first point:
/// <code>
/// Path2D loop = new(new(0f, 0f), new(32f, 0f), new(32f, 32f), new(0f, 32f), new(0f, 0f));
/// Vector2 halfway = loop.PointAt(loop.Length / 2f);
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

    /// <summary>Creates a path through a copy of <paramref name="points"/>, in order.</summary>
    /// <param name="points">At least two finite points, not all the same.</param>
    public Path2D(params ReadOnlySpan<Vector2> points)
    {
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

            if (i > 0)
            {
                _distances[i] = _distances[i - 1] + Vector2.Distance(_points[i - 1], point);
            }

            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        if (!float.IsFinite(Length))
        {
            throw new ArgumentException("The path is too long to measure. Bring its points closer together.", nameof(points));
        }

        if (Length == 0f)
        {
            throw new ArgumentException("Every point of the path is the same, and a path needs length. Move a point apart from the others.", nameof(points));
        }

        Bounds = Rect.FromEdges(min.X, min.Y, max.X, max.Y);
        Closed = _points[^1] == _points[0];
    }

    /// <summary>The points the path runs through, in order.</summary>
    public ReadOnlySpan<Vector2> Points => _points;

    /// <summary>Each point's distance along the path from the first, in step with <see cref="Points"/>.</summary>
    public ReadOnlySpan<float> Distances => _distances;

    /// <summary>The distance along the path from its first point to its last.</summary>
    public float Length => _distances[^1];

    /// <summary>The point <paramref name="distance"/> along the path from its first point.</summary>
    /// <remarks>A distance before the start reads the first point, and one past <see cref="Length"/> reads the last.</remarks>
    public Vector2 PointAt(float distance)
    {
        Guard.Finite(distance, nameof(distance));

        // The last point whose distance is no more than the one asked for starts the segment holding it.
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

        float span = _distances[to] - _distances[from];
        float amount = span > 0f ? Math.Clamp((distance - _distances[from]) / span, 0f, 1f) : 1f;

        return DeterministicMath.Lerp(_points[from], _points[to], amount);
    }
}
