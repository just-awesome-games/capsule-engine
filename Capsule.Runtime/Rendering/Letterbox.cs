namespace Capsule.Runtime.Rendering;

// Uniform fit in whole container pixels; Scale remains fractional and isotropic.
internal readonly record struct Letterbox(int X, int Y, int Width, int Height, float Scale)
{
    internal bool IsEmpty => Width <= 0 || Height <= 0;

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
