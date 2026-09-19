using System.Diagnostics;
using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Diagnostics;

/// <summary>
/// How game logic draws a wire shape or a label over the world to see what it is doing. It is
/// write-only, like <see cref="Log"/>, so showing the draws does not change the state a run reaches.
/// Every method is compiled out of an assembly that does not define <c>CAPSULE_DEVELOPMENT</c>.
/// <para>
/// Positions are world units at the settled step, and a draw stays for <c>steps</c> fixed steps
/// counted by the scheduler's tick. Draws are dropped until the host attaches a buffer, which the
/// development overlay does. A headless run draws nothing. A draw is shown while its channel is
/// switched on in the overlay, in the colour <see cref="SetColor"/> gave it.
/// </para>
/// </summary>
public static class DebugDraw
{
    /// <summary>
    /// The engine channel every collider's shape is drawn on, as the collision world holds it and
    /// dimmed while disabled, with the faces of the grid cells in and around the camera's view.
    /// </summary>
    public const string Colliders = "Colliders";

    /// <summary>The engine channel the camera's bounds are drawn on, when it has bounds.</summary>
    public const string Camera = "Camera";

    /// <summary>The engine channel a cross is drawn on at every world entity's position.</summary>
    public const string Origins = "Origins";

    // Segment counts for a full circle and for a capsule's half arc.
    private const int CircleSegments = 24;
    private const int CapSegments = CircleSegments / 2;

    // Private, like Log's sink, because a reader would let a game inspect what the host attached. It is
    // thread-static. A draw reaches only the buffer attached on the thread that emitted it. The buffer
    // is a single-threaded list and the host attaches it on the thread that steps the simulation, so test
    // hosts stepping their own runs in parallel stay out of each other's buffers.
    [field: ThreadStatic]
    private static DebugDrawBuffer? Buffer { get; set; }

    // Write-only from the game's side. The host resolves a colour at draw time, and the simulation can
    // read back neither a channel's colour nor its toggle.
    private static readonly Dictionary<string, ColorRgba> Colors = new(StringComparer.Ordinal)
    {
        [Colliders] = ColorRgba.Lime,
        [Camera] = ColorRgba.Cyan,
        [Origins] = ColorRgba.White,
    };

    /// <summary>
    /// Sets the colour <paramref name="channel"/>'s draws take when a call passes none, for the rest
    /// of the process. This is configuration, not a draw, so it is never compiled out. Call it from
    /// <c>Main</c> before the run starts or from a scene's <c>OnStart</c>.
    /// </summary>
    public static void SetColor(string channel, ColorRgba color)
    {
        ArgumentNullException.ThrowIfNull(channel);

        Colors[channel] = color;
    }

    // The colour a channel's uncoloured draws take. An unnamed channel draws white.
    internal static ColorRgba ColorOf(string channel) =>
        Colors.TryGetValue(channel, out ColorRgba color) ? color : ColorRgba.White;

    // Attaches buffer, replacing whatever was there. Null drops every draw again.
    internal static void UseBuffer(DebugDrawBuffer? buffer) => Buffer = buffer;

    // Whether a call would land anywhere, letting an engine pass skip the walk that feeds it. Internal
    // because no game may branch on it. The development switch is tested first. A trimmed shipping publish
    // folds it to false and drops the walk along with every OnDebugDraw override.
    internal static bool IsAttached => Development.IsSupported && Buffer is not null;

    /// <summary>Draws the segment from <paramref name="a"/> to <paramref name="b"/>.</summary>
    [Conditional(Development.Symbol)]
    public static void Line(string channel, Vector2 a, Vector2 b, ColorRgba? color = null, int steps = 1) =>
        Segment(channel, a, b, color, steps, default);

    // For one step, following something that moved by motion this step. The host draws the shape back
    // along that motion by the frame's unsimulated fraction, as it does the sprite. Every verb below has
    // a twin like this.
    [Conditional(Development.Symbol)]
    internal static void Line(string channel, Vector2 a, Vector2 b, ColorRgba? color, Vector2 motion) =>
        Segment(channel, a, b, color, 1, motion);

    /// <summary>Draws the four edges of <paramref name="rect"/>.</summary>
    [Conditional(Development.Symbol)]
    public static void Rect(string channel, global::Capsule.Rendering.Rect rect, ColorRgba? color = null, int steps = 1) =>
        RectEdges(channel, rect, color, steps, default);

    [Conditional(Development.Symbol)]
    internal static void Rect(string channel, global::Capsule.Rendering.Rect rect, ColorRgba? color, Vector2 motion) =>
        RectEdges(channel, rect, color, 1, motion);

    /// <summary>Draws the outline of the circle of <paramref name="radius"/> around <paramref name="center"/>, as twenty-four segments.</summary>
    [Conditional(Development.Symbol)]
    public static void Circle(string channel, Vector2 center, float radius, ColorRgba? color = null, int steps = 1) =>
        Arc(channel, center, radius, 0f, MathF.Tau, CircleSegments, color, steps, default);

    [Conditional(Development.Symbol)]
    internal static void Circle(string channel, Vector2 center, float radius, ColorRgba? color, Vector2 motion) =>
        Arc(channel, center, radius, 0f, MathF.Tau, CircleSegments, color, 1, motion);

    /// <summary>
    /// Draws the outline of everything within <paramref name="radius"/> of the segment from
    /// <paramref name="start"/> to <paramref name="end"/>. This is the shape
    /// <c>Capsule.Physics.Shape2D.Capsule</c> describes with the same three arguments.
    /// </summary>
    [Conditional(Development.Symbol)]
    public static void Capsule(string channel, Vector2 start, Vector2 end, float radius, ColorRgba? color = null, int steps = 1) =>
        CapsuleOutline(channel, start, end, radius, color, steps, default);

    [Conditional(Development.Symbol)]
    internal static void Capsule(string channel, Vector2 start, Vector2 end, float radius, ColorRgba? color, Vector2 motion) =>
        CapsuleOutline(channel, start, end, radius, color, 1, motion);

    /// <summary>
    /// Draws the closed outline through <paramref name="points"/> in order, joining the last back to
    /// the first. Fewer than two points draw nothing.
    /// </summary>
    [Conditional(Development.Symbol)]
    public static void Polygon(string channel, ReadOnlySpan<Vector2> points, ColorRgba? color = null, int steps = 1) =>
        ClosedOutline(channel, points, color, steps, default);

    [Conditional(Development.Symbol)]
    internal static void Polygon(string channel, ReadOnlySpan<Vector2> points, ColorRgba? color, Vector2 motion) =>
        ClosedOutline(channel, points, color, 1, motion);

    /// <summary>
    /// Draws <paramref name="text"/> with its top-left corner on <paramref name="position"/>. Null or
    /// empty text draws nothing. Glyphs are sized in screen pixels at the overlay's scale. A label
    /// reads the same however far the camera is zoomed.
    /// </summary>
    [Conditional(Development.Symbol)]
    public static void Text(string channel, Vector2 position, string? text, ColorRgba? color = null, int steps = 1)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (!string.IsNullOrEmpty(text))
        {
            Buffer?.Label(channel, position, text, color, steps, default);
        }
    }

    private static void Segment(string channel, Vector2 a, Vector2 b, ColorRgba? color, int steps, Vector2 motion)
    {
        ArgumentNullException.ThrowIfNull(channel);

        Buffer?.Segment(channel, a, b, color, steps, motion);
    }

    // Joins each corner to the next and the last back to the first.
    private static void ClosedOutline(string channel, ReadOnlySpan<Vector2> points, ColorRgba? color, int steps, Vector2 motion)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is not { } buffer || points.Length < 2)
        {
            return;
        }

        for (int index = 0; index < points.Length; index++)
        {
            buffer.Segment(channel, points[index], points[(index + 1) % points.Length], color, steps, motion);
        }
    }

    // The four edges clockwise from the top-left corner.
    private static void RectEdges(string channel, global::Capsule.Rendering.Rect rect, ColorRgba? color, int steps, Vector2 motion)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is not { } buffer)
        {
            return;
        }

        Vector2 topLeft = new(rect.Left, rect.Top);
        Vector2 topRight = new(rect.Right, rect.Top);
        Vector2 bottomRight = new(rect.Right, rect.Bottom);
        Vector2 bottomLeft = new(rect.Left, rect.Bottom);
        buffer.Segment(channel, topLeft, topRight, color, steps, motion);
        buffer.Segment(channel, topRight, bottomRight, color, steps, motion);
        buffer.Segment(channel, bottomRight, bottomLeft, color, steps, motion);
        buffer.Segment(channel, bottomLeft, topLeft, color, steps, motion);
    }

    // Two half arcs, one around each end facing away from the other, joined by two sides one radius from
    // the axis. A capsule of no length draws as a circle.
    private static void CapsuleOutline(string channel, Vector2 start, Vector2 end, float radius, ColorRgba? color, int steps, Vector2 motion)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is not { } buffer)
        {
            return;
        }

        Vector2 axis = end - start;
        float length = axis.Length();
        if (!(length > 0f))
        {
            Arc(channel, start, radius, 0f, MathF.Tau, CircleSegments, color, steps, motion);
            return;
        }

        float heading = MathF.Atan2(axis.Y, axis.X);
        Vector2 normal = new Vector2(-axis.Y, axis.X) / length * radius;

        Arc(channel, end, radius, heading - (MathF.PI / 2f), MathF.PI, CapSegments, color, steps, motion);
        Arc(channel, start, radius, heading + (MathF.PI / 2f), MathF.PI, CapSegments, color, steps, motion);
        buffer.Segment(channel, start + normal, end + normal, color, steps, motion);
        buffer.Segment(channel, start - normal, end - normal, color, steps, motion);
    }

    private static void Arc(string channel, Vector2 center, float radius, float from, float sweep, int segments, ColorRgba? color, int steps, Vector2 motion)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is not { } buffer)
        {
            return;
        }

        Vector2 previous = center + (radius * new Vector2(MathF.Cos(from), MathF.Sin(from)));
        for (int segment = 1; segment <= segments; segment++)
        {
            float angle = from + (sweep * segment / segments);
            Vector2 next = center + (radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle)));
            buffer.Segment(channel, previous, next, color, steps, motion);
            previous = next;
        }
    }
}
