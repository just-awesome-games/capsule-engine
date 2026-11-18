using System.Numerics;

namespace Capsule.Rendering;

// A convex outline in an entity's own units, held by the renderers that draw one, with the room to place it at both
// ends of the step. Setting one no longer than any it has held allocates nothing.
internal struct ConvexOutline
{
    private Vector2[]? _points;

    // The outline placed at both ends of the step, the previous points first.
    private Vector2[]? _placed;

    private int _count;

    internal readonly ReadOnlySpan<Vector2> Points => new(_points, 0, _count);

    internal readonly int Count => _count;

    internal void Set(ReadOnlySpan<Vector2> points)
    {
        ColorPolygon.RequireOutline(points);
        if (_points is null || _points.Length < points.Length)
        {
            _points = new Vector2[points.Length];
            _placed = new Vector2[points.Length * 2];
        }

        points.CopyTo(_points);
        _count = points.Length;
    }

    // The rect the outline covers once transform places it, or empty while there is no outline.
    internal readonly Rect Cover(in Transform2D transform)
    {
        if (_count == 0)
        {
            return default;
        }

        Vector2 min = transform.TransformPoint(_points![0]);
        Vector2 max = min;
        for (int index = 1; index < _count; index++)
        {
            Vector2 point = transform.TransformPoint(_points[index]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return Rect.FromCorners(min, max);
    }

    // Places the outline by both ends of the step into storage the next call overwrites.
    internal readonly void Place(in Transform2D previous, in Transform2D current, out ReadOnlySpan<Vector2> before, out ReadOnlySpan<Vector2> now)
    {
        Span<Vector2> from = _placed.AsSpan(0, _count);
        Span<Vector2> to = _placed.AsSpan(_count, _count);
        for (int index = 0; index < _count; index++)
        {
            from[index] = previous.TransformPoint(_points![index]);
            to[index] = current.TransformPoint(_points[index]);
        }

        before = from;
        now = to;
    }
}
