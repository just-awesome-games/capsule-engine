using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One nine-sliced panel as the simulation wants it drawn: a sprite's region cut into corners, edges
/// and a middle by <see cref="Insets"/>, then laid over <see cref="Size"/> with the corners at their
/// own texel size, the edges stretched along one axis and the middle stretched along both.
/// <see cref="FrameView.Add(in NineSliceIntent)"/> expands it into one
/// <see cref="SpriteIntent"/> per slice that has both texels and extent, so a panel interpolates,
/// culls and counts per slice.
/// </summary>
/// <param name="Sprite">
/// The frame cut into slices. Its <see cref="Rendering.Sprite.Pivot"/> is not read: a panel is placed
/// by its top-left corner.
/// </param>
/// <param name="Insets">Where the cuts fall inside the frame's region.</param>
/// <param name="PreviousPosition">Where the panel's top-left corner sat at the end of the previous step, in the drawn space's units.</param>
/// <param name="Position">Where the panel's top-left corner sits now, in the drawn space's units.</param>
/// <param name="Size">
/// The extent the panel covers, in the drawn space's units. A size smaller than its insets on an
/// axis keeps both edge slices at their own size, so they overlap rather than shrink; a non-positive
/// size draws nothing.
/// </param>
/// <param name="Color">Multiplied into every texel of every slice; <see cref="ColorRgba.White"/> draws the frame as it is.</param>
public readonly record struct NineSliceIntent(
    Sprite Sprite,
    SliceInsets Insets,
    Vector2 PreviousPosition,
    Vector2 Position,
    Vector2 Size,
    ColorRgba Color)
{
    /// <summary>
    /// The <see cref="Size"/> box at <see cref="Position"/>, in the drawn space's units; empty where
    /// it has no extent. A corner that overhangs a panel smaller than its insets draws past this box.
    /// </summary>
    // Negated so a NaN extent is rejected alongside the non-positive ones, as the expansion into
    // sprites rejects it.
    public Rect Bounds => !(Size.X > 0f) || !(Size.Y > 0f) ? default : new Rect(Position, Size);

    // The cut on both axes, written into the caller's three-slot spans. A slice with no texels or no
    // extent is left at zero extent and never drawn.
    internal void Slice(Span<SliceSpan> columns, Span<SliceSpan> rows)
    {
        TextureRegion region = Sprite.Region;

        Axis(region.X, region.Width, Insets.Left, Insets.Right, Size.X, columns);
        Axis(region.Y, region.Height, Insets.Top, Insets.Bottom, Size.Y, rows);
    }

    // One axis of the cut. The far edge is placed from the target's far side, so it stays on that
    // edge however the middle was squeezed.
    private static void Axis(int origin, int extent, int near, int far, float size, Span<SliceSpan> slices)
    {
        int low = Math.Clamp(near, 0, Math.Max(extent, 0));
        int high = Math.Clamp(far, 0, Math.Max(extent - low, 0));

        slices[0] = new SliceSpan(origin, low, 0f, low);
        slices[1] = new SliceSpan(origin + low, extent - low - high, low, MathF.Max(size - low - high, 0f));
        slices[2] = new SliceSpan(origin + extent - high, high, size - high, high);
    }
}

// One slice of one axis: where it is cut from in texels, and where it lands in the drawn space's
// units.
internal readonly record struct SliceSpan(int SourceOffset, int SourceExtent, float TargetOffset, float TargetExtent);
