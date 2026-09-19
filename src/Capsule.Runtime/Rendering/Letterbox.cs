namespace Capsule.Runtime.Rendering;

// A centred fit in whole container pixels. Scale is isotropic and stays float, fractional for world
// content and whole for a pixel surface the container can hold at least once.
internal readonly record struct Letterbox(int X, int Y, int Width, int Height, float Scale)
{
    internal bool IsEmpty => Width <= 0 || Height <= 0;

    // Fits content measured in its own units, such as a camera's world extent, into a surface. The scale
    // takes whatever the binding axis allows, because a world unit answers to no pixel grid.
    internal static Letterbox Fit(float contentWidth, float contentHeight, int containerWidth, int containerHeight)
    {
        // Negated to reject a NaN extent alongside the non-positive ones.
        if (!(contentWidth > 0f) || !(contentHeight > 0f) || containerWidth <= 0 || containerHeight <= 0)
        {
            return default;
        }

        return Place(contentWidth, contentHeight, containerWidth, containerHeight, UniformScale(contentWidth, contentHeight, containerWidth, containerHeight));
    }

    // Fits a pixel surface into a container of pixels. The scale is the largest whole number that fits
    // and the bars absorb the remainder, so every source pixel covers the same square block instead of
    // three columns here and four there. A container too small to hold the surface once falls back to
    // the fractional fit.
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

    // Fits content at a stated scale instead of the largest that fits. It is for content whose grown
    // axis is already a whole number of container pixels at that scale, so the scale is not recomputed
    // from a division that can land an ulp off. The other axis's extent is rounded as Fit rounds it.
    // Content the container cannot hold at that scale is clipped to the container, centred.
    internal static Letterbox FitAt(float contentWidth, float contentHeight, int containerWidth, int containerHeight, float scale)
    {
        if (!(contentWidth > 0f) || !(contentHeight > 0f) || containerWidth <= 0 || containerHeight <= 0 || !(scale > 0f))
        {
            return default;
        }

        return Place(contentWidth, contentHeight, containerWidth, containerHeight, scale);
    }

    private static float UniformScale(float contentWidth, float contentHeight, int containerWidth, int containerHeight) =>
        MathF.Min(containerWidth / contentWidth, containerHeight / contentHeight);

    private static Letterbox Place(float contentWidth, float contentHeight, int containerWidth, int containerHeight, float scale)
    {
        // The clamp absorbs float error. At the fractional scale the binding axis multiplies back to
        // the container's extent, and a whole scale can only come up short.
        int width = Math.Min(containerWidth, (int)MathF.Round(contentWidth * scale));
        int height = Math.Min(containerHeight, (int)MathF.Round(contentHeight * scale));

        // Centred from the rounded extents, not the exact ones, so the rect cannot spill past the
        // container it is set as a viewport over.
        return new Letterbox((containerWidth - width) / 2, (containerHeight - height) / 2, width, height, scale);
    }
}
