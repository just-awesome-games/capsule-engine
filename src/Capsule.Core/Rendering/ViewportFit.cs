namespace Capsule.Rendering;

/// <summary>How a camera's world-unit span answers an output whose aspect ratio differs from it.</summary>
/// <remarks>
/// A declared render surface fills the output edge to edge on its binding axis, at whatever fractional
/// scale keeps its aspect, with bars on the other axis. A point-sampled surface enlarged past a whole
/// scale is first enlarged to the next whole scale with the nearest texel and then filtered down to fit.
/// Its texels stay crisp and even in width. A window smaller than the surface filters it straight down.
/// </remarks>
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
