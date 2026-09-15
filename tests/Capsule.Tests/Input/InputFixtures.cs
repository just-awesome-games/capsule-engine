namespace Capsule.Tests.Input;

// What the input specs share.
internal static class InputFixtures
{
    // Axis values are floats carried through a clamp and a sum, so they are compared to a
    // tolerance rather than exactly.
    internal const float Tolerance = 1e-6f;
}
