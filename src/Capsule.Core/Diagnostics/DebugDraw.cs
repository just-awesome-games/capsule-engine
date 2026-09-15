using System.Diagnostics;
using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Diagnostics;

/// <summary>
/// How game logic draws a wire shape or a label over the world to see what it is doing. Write-only,
/// as <see cref="Log"/> is: nothing reads back, so a run with the draws shown reaches the same state
/// as a run without, and no call can learn whether its channel is being looked at. Every method is
/// compiled out of an assembly that does not define <c>CAPSULE_DEVELOPMENT</c> — the call and every
/// argument expression it was passed are absent — and Capsule's build defines that symbol for a
/// consuming game whenever <c>CapsuleShipping</c> is not <c>true</c>.
/// <para>
/// Positions are world units at the settled step: a draw lands where the step left it while the
/// sprites around it interpolate towards it. Each draw stays for <c>steps</c> fixed steps, counted
/// by the scheduler's tick rather than the clock, so a held run keeps it on screen. A draw is
/// dropped until the host attaches a buffer, which the runtime's development overlay does; a
/// headless run draws nothing.
/// </para>
/// <para>
/// A channel is any name, and its draws are shown only while that channel is switched on in the
/// overlay's Debug Draw menu; every channel starts off. A draw passing no colour takes its
/// channel's: the engine channels below are bootstrapped with one, <see cref="SetColor"/> overrides
/// one or names a game's own, and an unmapped channel draws white. An engine object draws itself
/// from its <c>OnDebugDraw</c> hook once a step and follows its entity between steps the way its
/// sprite does; a call from game code lands at the settled step.
/// </para>
/// <para>
/// Every verb takes the channel it is shown under, the colour it is drawn in — straight alpha, and
/// null takes the channel's — and how many fixed steps it stays, at least one. Geometry it is
/// handed is read during the call and never retained. A null channel throws
/// <see cref="ArgumentNullException"/>, <see cref="SetColor"/> included.
/// </para>
/// </summary>
public static class DebugDraw
{
    /// <summary>
    /// The engine channel every collider's shape is drawn on, exactly as the collision world holds
    /// it, dimmed while disabled, with the faces of the grid cells in and around the camera's view.
    /// </summary>
    public const string Colliders = "Colliders";

    /// <summary>The engine channel the camera's bounds are drawn on, while it has any.</summary>
    public const string Camera = "Camera";

    /// <summary>The engine channel a cross is drawn on at every world entity's position.</summary>
    public const string Origins = "Origins";

    // Segments a circle is drawn as, and a capsule's half arc.
    private const int CircleSegments = 24;
    private const int CapSegments = CircleSegments / 2;

    // Private, as Log's sink is: a reader would let a game inspect what the host attached.
    private static DebugDrawBuffer? Buffer { get; set; }

    // Write-only from the game's side: the host resolves it at draw time, so nothing in the
    // simulation can read a channel's colour back any more than its toggle.
    private static readonly Dictionary<string, ColorRgba> Colors = new(StringComparer.Ordinal)
    {
        [Colliders] = ColorRgba.Lime,
        [Camera] = ColorRgba.Cyan,
        [Origins] = ColorRgba.White,
    };

    /// <summary>
    /// Sets the colour <paramref name="channel"/>'s draws take when a call passes none, for the
    /// rest of the process. Configuration rather than a draw, so it is never compiled out: call it
    /// from <c>Main</c> before the run starts or from a scene's <c>OnStart</c>.
    /// </summary>
    public static void SetColor(string channel, ColorRgba color)
    {
        ArgumentNullException.ThrowIfNull(channel);

        Colors[channel] = color;
    }

    // The colour a channel's uncoloured draws take; white for one nothing has named.
    internal static ColorRgba ColorOf(string channel) =>
        Colors.TryGetValue(channel, out ColorRgba color) ? color : ColorRgba.White;

    // Attaches buffer, replacing whatever was there; null drops every draw again.
    internal static void UseBuffer(DebugDrawBuffer? buffer) => Buffer = buffer;

    // Whether a call would land anywhere, so an engine pass can skip the walk that feeds it.
    // Internal: no game may branch on it. The development switch comes first so a trimmed
    // shipping publish, where it folds to false, drops the walk and every OnDebugDraw override
    // with it; only the overlay attaches a buffer, so at runtime the two agree.
    internal static bool IsAttached => Development.IsSupported && Buffer is not null;

    /// <summary>Draws the segment from <paramref name="a"/> to <paramref name="b"/>.</summary>
    [Conditional(Development.Symbol)]
    public static void Line(string channel, Vector2 a, Vector2 b, ColorRgba? color = null, int steps = 1) =>
        Segment(channel, a, b, color, steps, default);

    // For one step. motion is how far what the draw follows moved this step; the host draws the
    // shape back along it by the frame's unsimulated fraction, as it does the sprite. Every verb
    // below has such a twin.
    [Conditional(Development.Symbol)]
    internal static void Line(string channel, Vector2 a, Vector2 b, ColorRgba? color, Vector2 motion) =>
        Segment(channel, a, b, color, 1, motion);

    /// <summary>Draws the outline of <paramref name="rect"/>: its four edges.</summary>
    [Conditional(Development.Symbol)]
    public static void Rect(string channel, global::Capsule.Rendering.Rect rect, ColorRgba? color = null, int steps = 1)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is { } buffer)
        {
            RectEdges(buffer, channel, rect, color, steps, default);
        }
    }

    [Conditional(Development.Symbol)]
    internal static void Rect(string channel, global::Capsule.Rendering.Rect rect, ColorRgba? color, Vector2 motion)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is { } buffer)
        {
            RectEdges(buffer, channel, rect, color, 1, motion);
        }
    }

    /// <summary>
    /// Draws the outline of the circle of <paramref name="radius"/> around <paramref name="center"/>,
    /// as twenty-four segments.
    /// </summary>
    [Conditional(Development.Symbol)]
    public static void Circle(string channel, Vector2 center, float radius, ColorRgba? color = null, int steps = 1)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is { } buffer)
        {
            Arc(buffer, channel, center, radius, 0f, MathF.Tau, CircleSegments, color, steps, default);
        }
    }

    [Conditional(Development.Symbol)]
    internal static void Circle(string channel, Vector2 center, float radius, ColorRgba? color, Vector2 motion)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is { } buffer)
        {
            Arc(buffer, channel, center, radius, 0f, MathF.Tau, CircleSegments, color, 1, motion);
        }
    }

    /// <summary>
    /// Draws the outline of a capsule: everything within <paramref name="radius"/> of the segment
    /// from <paramref name="start"/> to <paramref name="end"/>, the shape
    /// <c>Capsule.Physics.Shape2D.Capsule</c> describes with the same three arguments. A half arc
    /// of twelve segments around each end, joined by the two sides.
    /// </summary>
    [Conditional(Development.Symbol)]
    public static void Capsule(string channel, Vector2 start, Vector2 end, float radius, ColorRgba? color = null, int steps = 1)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is { } buffer)
        {
            CapsuleOutline(buffer, channel, start, end, radius, color, steps, default);
        }
    }

    [Conditional(Development.Symbol)]
    internal static void Capsule(string channel, Vector2 start, Vector2 end, float radius, ColorRgba? color, Vector2 motion)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is { } buffer)
        {
            CapsuleOutline(buffer, channel, start, end, radius, color, 1, motion);
        }
    }

    /// <summary>
    /// Draws the closed outline through <paramref name="points"/> in order, the last joined back to
    /// the first. Fewer than two points draw nothing.
    /// </summary>
    [Conditional(Development.Symbol)]
    public static void Polygon(string channel, ReadOnlySpan<Vector2> points, ColorRgba? color = null, int steps = 1)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is { } buffer)
        {
            ClosedOutline(buffer, channel, points, color, steps, default);
        }
    }

    [Conditional(Development.Symbol)]
    internal static void Polygon(string channel, ReadOnlySpan<Vector2> points, ColorRgba? color, Vector2 motion)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Buffer is { } buffer)
        {
            ClosedOutline(buffer, channel, points, color, 1, motion);
        }
    }

    /// <summary>
    /// Draws <paramref name="text"/> with its top-left corner on <paramref name="position"/>; null
    /// or empty draws nothing. The glyphs are screen-sized at the overlay's own scale rather than
    /// world-sized, so a label reads the same however far the camera is zoomed.
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

    // The one segment both Line verbs raise.
    private static void Segment(string channel, Vector2 a, Vector2 b, ColorRgba? color, int steps, Vector2 motion)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Buffer?.Segment(channel, a, b, color, steps, motion);
    }

    // Each corner joined to the next and the last back to the first; fewer than two draw nothing.
    private static void ClosedOutline(DebugDrawBuffer buffer, string channel, ReadOnlySpan<Vector2> points, ColorRgba? color, int steps, Vector2 motion)
    {
        if (points.Length < 2)
        {
            return;
        }

        for (int index = 0; index < points.Length; index++)
        {
            buffer.Segment(channel, points[index], points[(index + 1) % points.Length], color, steps, motion);
        }
    }

    // The four edges clockwise from the top-left corner.
    private static void RectEdges(DebugDrawBuffer buffer, string channel, global::Capsule.Rendering.Rect rect, ColorRgba? color, int steps, Vector2 motion)
    {
        Vector2 topLeft = new(rect.Left, rect.Top);
        Vector2 topRight = new(rect.Right, rect.Top);
        Vector2 bottomRight = new(rect.Right, rect.Bottom);
        Vector2 bottomLeft = new(rect.Left, rect.Bottom);
        buffer.Segment(channel, topLeft, topRight, color, steps, motion);
        buffer.Segment(channel, topRight, bottomRight, color, steps, motion);
        buffer.Segment(channel, bottomRight, bottomLeft, color, steps, motion);
        buffer.Segment(channel, bottomLeft, topLeft, color, steps, motion);
    }

    // Two half arcs, one around each end facing away from the other, joined by the two sides one
    // radius either side of the axis. A capsule of no length is its circle.
    private static void CapsuleOutline(DebugDrawBuffer buffer, string channel, Vector2 start, Vector2 end, float radius, ColorRgba? color, int steps, Vector2 motion)
    {
        Vector2 axis = end - start;
        float length = axis.Length();
        if (!(length > 0f))
        {
            Arc(buffer, channel, start, radius, 0f, MathF.Tau, CircleSegments, color, steps, motion);
            return;
        }

        float heading = MathF.Atan2(axis.Y, axis.X);
        Vector2 normal = new Vector2(-axis.Y, axis.X) / length * radius;

        Arc(buffer, channel, end, radius, heading - (MathF.PI / 2f), MathF.PI, CapSegments, color, steps, motion);
        Arc(buffer, channel, start, radius, heading + (MathF.PI / 2f), MathF.PI, CapSegments, color, steps, motion);
        buffer.Segment(channel, start + normal, end + normal, color, steps, motion);
        buffer.Segment(channel, start - normal, end - normal, color, steps, motion);
    }

    private static void Arc(DebugDrawBuffer buffer, string channel, Vector2 center, float radius, float from, float sweep, int segments, ColorRgba? color, int steps, Vector2 motion)
    {
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
