using System.Numerics;

namespace Capsule.Rendering;

internal static class StepInterpolation
{
    // alpha is the fraction of a fixed step not yet simulated. Equal endpoints return the endpoint
    // unchanged, because Vector2.Lerp evaluates a * (1 - t) + b * t and lands a ULP either side of a
    // stationary sample depending on alpha. A sample on a pixel-snap boundary would then round to a
    // different whole pixel from frame to frame and flicker.
    internal static Vector2 Interpolate(Vector2 previous, Vector2 current, float alpha) =>
        previous == current ? current : previous + (current - previous) * alpha;

    // Angles in radians, along the shortest arc. The turn is wrapped into (-pi, pi] before scaling, so a
    // facing that snaps across the wrap turns the short way instead of the long way for one frame. Equal
    // endpoints return the endpoint unchanged, as the position rule does.
    internal static float Interpolate(float previous, float current, float alpha)
    {
        if (previous == current)
        {
            return current;
        }

        float turn = MathF.IEEERemainder(current - previous, MathF.Tau);
        if (turn <= -MathF.PI)
        {
            turn += MathF.Tau;
        }

        return previous + (turn * alpha);
    }
}
