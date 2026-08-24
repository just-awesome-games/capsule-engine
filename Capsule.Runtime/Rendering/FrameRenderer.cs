using Capsule.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Rendering;

/// <summary>
/// Draws a <see cref="FrameView"/>. Simulations never reach the device; everything
/// they want on screen arrives here as data.
/// </summary>
internal sealed class FrameRenderer
{
    // ColorRgba is straight alpha and the studio blend convention is premultiplied.
    private static readonly Color Clear = Color.FromNonPremultiplied(
        ColorRgba.Black.R,
        ColorRgba.Black.G,
        ColorRgba.Black.B,
        ColorRgba.Black.A);

    private readonly GraphicsDevice _device;

    internal FrameRenderer(GraphicsDevice device) => _device = device;

    /// <param name="alpha">
    /// Fraction of a fixed step not yet simulated, in [0, 1). Interpolates previous
    /// to current state; nothing moves yet, so nothing reads it beyond the signature.
    /// </param>
    internal void Draw(FrameView view, float alpha) => _device.Clear(Clear);
}
