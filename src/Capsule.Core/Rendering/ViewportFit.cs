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
    /// scale rather than becoming bars.
    /// </summary>
    Expand,

    /// <summary>
    /// The vertical span is exact and the horizontal span follows the output's aspect, so a wider
    /// output shows more width and a taller one shows less.
    /// </summary>
    FixedHeight,
}
