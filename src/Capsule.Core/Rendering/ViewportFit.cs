namespace Capsule.Rendering;

/// <summary>How a camera's world-unit span answers an output whose aspect ratio differs from it.</summary>
public enum ViewportFit
{
    /// <summary>
    /// The span is the visible world on both axes. Every display shows the same region, and the
    /// output's slack becomes bars.
    /// </summary>
    Letterbox,

    /// <summary>The span is a minimum on both axes.</summary>
    /// <remarks>
    /// The output's slack axis reveals more world at the same scale instead of becoming bars. On a
    /// declared render surface the revealed axis grows in whole surface pixels, and the scale stays
    /// at the declared pixels per unit.
    /// </remarks>
    Expand,

    /// <summary>
    /// The vertical span is exact and the horizontal span follows the output's aspect.
    /// </summary>
    /// <remarks>
    /// A wider output shows more width and a taller one less. On a declared render surface the
    /// width is a whole number of surface pixels, and the scale stays at the declared pixels per
    /// unit.
    /// </remarks>
    FixedHeight,
}
