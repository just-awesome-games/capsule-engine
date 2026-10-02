namespace Capsule.Rendering;

// A centred fit in whole container pixels. Scale is isotropic and stays float.
internal readonly record struct Letterbox(int X, int Y, int Width, int Height, float Scale)
{
    internal bool IsEmpty => Width <= 0 || Height <= 0;

    // Fits content measured in its own units, such as a camera's world extent, into a surface. The scale
    // takes whatever the binding axis allows, because a world unit answers to no pixel grid.
    internal static Letterbox Fit(float contentWidth, float contentHeight, int containerWidth, int containerHeight) =>
        FitAt(contentWidth, contentHeight, containerWidth, containerHeight, UniformScale(contentWidth, contentHeight, containerWidth, containerHeight));

    // Fits content at a stated scale instead of the largest that fits. It is for content whose grown
    // axis is already a whole number of container pixels at that scale, so the scale is not recomputed
    // from a division that can land an ulp off. The other axis's extent is rounded as Fit rounds it.
    // Content the container cannot hold at that scale is clipped to the container, centred.
    internal static Letterbox FitAt(float contentWidth, float contentHeight, int containerWidth, int containerHeight, float scale)
    {
        // Negated comparisons reject a NaN extent or scale along with the non-positive ones.
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
        // the container's extent.
        int width = Math.Min(containerWidth, (int)MathF.Round(contentWidth * scale));
        int height = Math.Min(containerHeight, (int)MathF.Round(contentHeight * scale));

        // Centred from the rounded extents, not the exact ones, so the rect cannot spill past the
        // container it is set as a viewport over.
        return new Letterbox((containerWidth - width) / 2, (containerHeight - height) / 2, width, height, scale);
    }
}
