using Capsule.Input;

namespace Capsule.Tests.Input;

// What the input specs share.
internal static class InputFixtures
{
    // Axis values are floats carried through a clamp and a sum. They are compared to a
    // tolerance rather than exactly.
    internal const float Tolerance = 1e-6f;

    // An AZERTY layout's characters: Z on W's position and A on Q's, and nothing elsewhere.
    internal static string?[] Azerty()
    {
        string?[] layout = new string?[(int)Key.NumpadDecimal + 1];
        layout[(int)Key.W] = "Z";
        layout[(int)Key.Q] = "A";

        return layout;
    }
}
