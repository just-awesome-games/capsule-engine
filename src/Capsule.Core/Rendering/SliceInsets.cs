namespace Capsule.Rendering;

/// <summary>
/// How far in from each edge of a sprite's region its nine-slice cuts fall, in texels. The four
/// strips they cut off are the corners and edges, which keep their own size; what is left in the
/// middle stretches. A negative inset is no inset, and an inset pair wider than the region is cut
/// back to it, leaving no middle on that axis.
/// </summary>
/// <param name="Left">Texels from the region's left edge to the first vertical cut.</param>
/// <param name="Top">Texels from the region's top edge to the first horizontal cut.</param>
/// <param name="Right">Texels from the region's right edge back to the second vertical cut.</param>
/// <param name="Bottom">Texels from the region's bottom edge back to the second horizontal cut.</param>
public readonly record struct SliceInsets(int Left, int Top, int Right, int Bottom)
{
    /// <summary>The same inset on all four edges.</summary>
    public SliceInsets(int inset)
        : this(inset, inset, inset, inset)
    {
    }
}
