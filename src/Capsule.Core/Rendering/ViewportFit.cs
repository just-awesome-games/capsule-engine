namespace Capsule.Rendering;

/// <summary>How a camera's world-unit span answers an output whose aspect ratio differs from it.</summary>
public enum ViewportFit
{
    /// <summary>
    /// The span is exactly the visible world on both axes, so every display shows the same region
    /// and the output's slack becomes bars.
    /// </summary>
    Letterbox,

    /// <summary>
    /// The span is a minimum on both axes: the output's slack axis reveals more world at the same
    /// scale rather than becoming bars. Rasterised to a declared render surface, the revealed
    /// axis grows in whole surface pixels, so the scale stays exactly the declared pixels per
    /// unit at every output size; with no surface the world fills the window at its own scale.
    /// </summary>
    Expand,

    /// <summary>
    /// The vertical span is exact and the horizontal span follows the output's aspect, so a wider
    /// output shows more width and a taller one shows less. Rasterised to a declared render
    /// surface, the width is a whole number of surface pixels, so the scale stays exactly the
    /// declared pixels per unit; with no surface the world fills the window at its own scale.
    /// </summary>
    FixedHeight,
}
