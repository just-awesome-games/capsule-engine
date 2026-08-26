namespace Capsule.Runtime.Rendering;

/// <summary>
/// Where content lands inside a container of a different shape: scaled uniformly to the
/// largest size that fits, centred, and the slack on the other axis left as bars. Capsule
/// never stretches, at any stage of the presentation path. <see cref="X"/> and
/// <see cref="Y"/> are the top-left corner in whole container pixels, snapped to fit inside
/// the container; <see cref="Scale"/> is the exact container pixels one content unit spans,
/// the same number on both axes.
/// </summary>
internal readonly record struct Letterbox(int X, int Y, int Width, int Height, float Scale)
{
    /// <summary>No area to draw into; a caller must skip rather than divide by it.</summary>
    internal bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Fits a content shape — in any unit, since only its ratio is read — into a container
    /// measured in pixels. The scale is fractional, so the fit binds on one axis exactly.
    /// Degenerate input on either rectangle yields an empty fit rather than throwing.
    /// </summary>
    internal static Letterbox Fit(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        // Negated so a NaN extent is rejected alongside the non-positive ones.
        if (!(contentWidth > 0f) || !(contentHeight > 0f) || containerWidth <= 0 || containerHeight <= 0)
        {
            return default;
        }

        float scale = MathF.Min(containerWidth / contentWidth, containerHeight / contentHeight);

        // The clamp only absorbs float error: the binding axis multiplies back to the
        // container's own extent and the other is shorter.
        int width = Math.Min(containerWidth, (int)MathF.Round(contentWidth * scale));
        int height = Math.Min(containerHeight, (int)MathF.Round(contentHeight * scale));

        // Centred from the rounded extents rather than the exact ones, so the rect can
        // never spill past the container it will be set as a viewport over.
        return new Letterbox((containerWidth - width) / 2, (containerHeight - height) / 2, width, height, scale);
    }
}
