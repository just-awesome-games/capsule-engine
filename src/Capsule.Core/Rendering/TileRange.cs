namespace Capsule.Rendering;

// One axis of a tiled sprite: the copies to emit, as indices from the authored frame at 0, and how
// far the copy at Last reaches — a whole Period, or less where the extent cuts it short.
internal readonly record struct TileRange(int First, int Last, float Period, float LastExtent)
{
    // extent is the tiling on this axis, non-negative and possibly infinite; period the drawn
    // frame's extent; low and high the edges of the frame's own swept rect; and the cull edges the
    // region a copy must touch to be worth emitting, read only while culled.
    internal static TileRange Along(
        float extent,
        float period,
        float low,
        float high,
        bool culled,
        float cullLow,
        float cullHigh)
    {
        if (extent == 0f)
        {
            return new TileRange(0, 0, period, period);
        }

        // Copy k sweeps [low + k period, high + k period], which touches the cull region on the open
        // overlap the sprite cull uses; clamped to int before the cast, since a tiny period against
        // a far region divides past what an int holds.
        int first = int.MinValue;
        int last = int.MaxValue;
        if (culled)
        {
            first = (int)Math.Clamp(Math.Floor((cullLow - high) / period) + 1, int.MinValue, int.MaxValue);
            last = (int)Math.Clamp(Math.Ceiling((cullHigh - low) / period) - 1, int.MinValue, int.MaxValue);
        }

        if (float.IsPositiveInfinity(extent))
        {
            return culled ? new TileRange(first, last, period, period) : new TileRange(0, 0, period, period);
        }

        int count = (int)Math.Clamp(Math.Ceiling(extent / period), 1, int.MaxValue);
        float lastExtent = extent - ((count - 1) * period);

        return new TileRange(Math.Max(first, 0), Math.Min(last, count - 1), period, lastExtent);
    }

    // The texel span copy index draws of a region starting at origin and texels wide, cropped to
    // LastExtent on the copy at Last: the near texels of the frame, which on a flipped axis are the
    // far texels of the region.
    internal (int Offset, int Texels) Crop(int index, int origin, int texels, float texelSize, bool flipped)
    {
        if (index != Last || !(LastExtent < Period))
        {
            return (origin, texels);
        }

        int kept = Math.Clamp((int)MathF.Round(LastExtent / texelSize), 0, texels);

        return (flipped ? origin + texels - kept : origin, kept);
    }
}
