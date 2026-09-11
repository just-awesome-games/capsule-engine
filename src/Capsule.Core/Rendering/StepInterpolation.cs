using System.Numerics;

namespace Capsule.Rendering;

internal static class StepInterpolation
{
    // alpha is the fraction of a fixed step not yet simulated. Equal endpoints return the endpoint
    // itself: Vector2.Lerp evaluates a * (1 - t) + b * t, which lands a ULP either side of a
    // stationary sample depending on alpha, so a sample sitting on a pixel-snap boundary would
    // round to a different whole pixel from frame to frame and flicker.
    internal static Vector2 Interpolate(Vector2 previous, Vector2 current, float alpha) =>
        previous == current ? current : previous + (current - previous) * alpha;
}
