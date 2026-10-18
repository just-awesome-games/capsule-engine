using System.Numerics;

namespace Capsule.Rendering;

// The float4 a material parameter or scene global is stored as, and the name lookup both stores share.
// Each Of rejects a non-finite component and fills the components its kind lacks with zero.
internal static class ShaderValue
{
    internal static Vector4 Of(float value) => new(Guard.Finite(value, nameof(value)), 0f, 0f, 0f);

    internal static Vector4 Of(Vector2 value) => new(Guard.Finite(value, nameof(value)), 0f, 0f);

    internal static Vector4 Of(Vector3 value) => Guard.Finite(new Vector4(value, 0f), nameof(value));

    internal static Vector4 Of(Vector4 value) => Guard.Finite(value, nameof(value));

    // Straight alpha, each channel from 0 to 1.
    internal static Vector4 Of(ColorRgba value) => new Vector4(value.R, value.G, value.B, value.A) / 255f;

    internal static Vector2 AsVector2(Vector4 value) => new(value.X, value.Y);

    internal static Vector3 AsVector3(Vector4 value) => new(value.X, value.Y, value.Z);

    // The straight-alpha colour of a float4 holding 0 to 1 channels, the inverse of Of(ColorRgba).
    internal static ColorRgba ColorOf(Vector4 value)
    {
        static byte Channel(float channel) => (byte)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);

        return new ColorRgba(Channel(value.X), Channel(value.Y), Channel(value.Z), Channel(value.W));
    }

    // The index of the parameter named name, or -1. A shader declares a handful of names, and a linear
    // scan allocates nothing.
    internal static int IndexOf(ReadOnlySpan<ShaderParameter> parameters, string name)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
