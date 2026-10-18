using System.Numerics;

namespace Capsule.Rendering;

// The box arithmetic a run of text shares between a TextIntent and a Label: which extent a box needs
// measured, where the pivot puts it, how far the vertical alignment drops the run inside it, and what
// the run can reach across a step. All of it is in the drawn space's units, except where a name says
// font pixels.
internal static class TextBox
{
    // Only a positive scale converts a box in space units back into the font pixels layout wants. A
    // degenerate scale still submits its glyphs, which cull on their own extent.
    internal static int WrapWidth(float extent, float scale) => scale > 0f && extent > 0f ? (int)(extent / scale) : 0;

    // Measuring the run costs a pass over the text. It is skipped when the box is given on both axes
    // and the run sits at its top. Nothing then reads the extent.
    internal static bool NeedsMeasure(Vector2 size, VerticalAlignment alignment) =>
        !(size.X > 0f && size.Y > 0f && Share(alignment) == 0f);

    // The box a run is laid out in: the given size on each axis where it is positive, the measured run elsewhere.
    internal static Vector2 Extent(Vector2 size, Vector2 measured) => new(
        size.X > 0f ? size.X : measured.X,
        size.Y > 0f ? size.Y : measured.Y);

    // Places box on position by its pivot. origin is where the first line's pen starts: the box's
    // top-left corner, dropped by the vertical alignment's share of the room the measured run leaves.
    internal static Rect Place(
        Vector2 position,
        Pivot pivot,
        Vector2 box,
        Vector2 measured,
        VerticalAlignment alignment,
        out Vector2 origin)
    {
        Vector2 topLeft = position - pivot.On(box);
        origin = new Vector2(topLeft.X, topLeft.Y + ((box.Y - measured.Y) * Share(alignment)));

        return Rect.FromEdges(topLeft.X, topLeft.Y, topLeft.X + box.X, topLeft.Y + box.Y);
    }

    // Where a run's glyphs can draw across the step: its box grown by the overhang, and by one font
    // pixel that the box width loses to whole pixels. An axis the caller sized can overflow the box and
    // bounds nothing.
    internal static Rect Reach(Vector2 previous, Vector2 position, Vector2 size, Vector2 scale, int overhang, in Rect box)
    {
        Vector2 pad = new Vector2(overhang + 1) * scale;
        pad.X = size.X > 0f ? float.PositiveInfinity : pad.X;
        pad.Y = size.Y > 0f ? float.PositiveInfinity : pad.Y;

        return Rect.Sweep(previous, position, box.Position - position - pad, new Vector2(box.Right, box.Bottom) - position + pad);
    }

    private static float Share(VerticalAlignment alignment) => alignment switch
    {
        VerticalAlignment.Middle => 0.5f,
        VerticalAlignment.Bottom => 1f,
        _ => 0f,
    };
}
