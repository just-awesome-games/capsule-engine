using System.Numerics;

namespace Capsule.Runtime.Rendering;

// Point sampling fetches one texel per pixel centre. A texel boundary landing exactly on a centre
// leaves float error to decide which side is fetched, dropping a column and doubling its
// neighbour; snapping keeps every boundary between centres.
internal static class PixelGrid
{
    // value is world units, scale surface pixels per world unit. Midpoints round away from zero so
    // a snapped position never depends on the parity of the pixel it falls in.
    internal static Vector2 Snap(Vector2 value, float scale) => new(
        MathF.Round(value.X * scale, MidpointRounding.AwayFromZero) / scale,
        MathF.Round(value.Y * scale, MidpointRounding.AwayFromZero) / scale);
}
