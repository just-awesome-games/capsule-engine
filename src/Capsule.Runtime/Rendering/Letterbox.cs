namespace Capsule.Runtime.Rendering;

// A centred fit in whole container pixels. Scale is isotropic and stays float: fractional for
// world content, whole for a pixel surface the container can hold at least once.
internal readonly record struct Letterbox(int X, int Y, int Width, int Height, float Scale)
{
    internal bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Fits content measured in its own units — a camera's world extent — into a surface. The
    /// scale is whatever the binding axis allows, because a world unit answers to no pixel grid.
    /// </summary>
    internal static Letterbox Fit(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        // Negated so a NaN extent is rejected alongside the non-positive ones.
        if (!(contentWidth > 0f) || !(contentHeight > 0f) || containerWidth <= 0 || containerHeight <= 0)
        {
            return default;
        }

        return Place(contentWidth, contentHeight, containerWidth, containerHeight, UniformScale(contentWidth, contentHeight, containerWidth, containerHeight));
    }

    /// <summary>
    /// Fits a pixel surface into a container of pixels. The scale is the largest whole number that
    /// fits and the bars absorb the remainder, so every source pixel covers the same square block
    /// of the container rather than three columns here and four there. A container too small to
    /// hold the surface once falls back to the fractional fit, which is the only way it fits at
    /// all.
    /// </summary>
    internal static Letterbox FitPixels(int contentWidth, int contentHeight, int containerWidth, int containerHeight)
    {
        if (contentWidth <= 0 || contentHeight <= 0 || containerWidth <= 0 || containerHeight <= 0)
        {
            return default;
        }

        float scale = UniformScale(contentWidth, contentHeight, containerWidth, containerHeight);
        if (scale >= 1f)
        {
            scale = MathF.Floor(scale);
        }

        return Place(contentWidth, contentHeight, containerWidth, containerHeight, scale);
    }

    private static float UniformScale(float contentWidth, float contentHeight, int containerWidth, int containerHeight) =>
        MathF.Min(containerWidth / contentWidth, containerHeight / contentHeight);

    private static Letterbox Place(float contentWidth, float contentHeight, int containerWidth, int containerHeight, float scale)
    {
        // The clamp only absorbs float error: at the fractional scale the binding axis multiplies
        // back to the container's own extent, and a whole scale can only come up short.
        int width = Math.Min(containerWidth, (int)MathF.Round(contentWidth * scale));
        int height = Math.Min(containerHeight, (int)MathF.Round(contentHeight * scale));

        // Centred from the rounded extents rather than the exact ones, so the rect can
        // never spill past the container it will be set as a viewport over.
        return new Letterbox((containerWidth - width) / 2, (containerHeight - height) / 2, width, height, scale);
    }
}
