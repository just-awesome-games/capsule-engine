using Capsule.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// Both namespaces define Vector2. Render intent speaks the BCL one; the aliases keep
// which is which visible at every use rather than resting on directive order.
using Vector2 = System.Numerics.Vector2;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace Capsule.Runtime.Rendering;

/// <summary>
/// Draws a <see cref="FrameView"/>. Holds no scene state of its own: what a quad
/// interpolates from travels in the quad.
/// </summary>
internal sealed class FrameRenderer : IDisposable
{
    // ColorRgba is straight alpha and the studio blend convention is premultiplied.
    // Not host-configurable: a clear colour is render intent, never host boot config.
    private static readonly Color Clear = Color.FromNonPremultiplied(
        ColorRgba.Black.R,
        ColorRgba.Black.G,
        ColorRgba.Black.B,
        ColorRgba.Black.A);

    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _batch;

    // One white texel, tinted and stretched per quad: an untextured rectangle needs no
    // asset, and keeps every quad in the same batch.
    private readonly Texture2D _white;

    internal FrameRenderer(GraphicsDevice device)
    {
        _device = device;
        _batch = new SpriteBatch(device);
        _white = new Texture2D(device, 1, 1);
        _white.SetData<Color>([Color.White]);
    }

    // Allocation-free at steady state: the span is iterated directly, the matrix is on
    // the stack, and there is no closure or LINQ in the path.

    /// <param name="alpha">
    /// Fraction of a fixed step not yet simulated, in [0, 1). Each quad is drawn that
    /// far from its previous position towards its current one.
    /// </param>
    internal void Draw(FrameView view, float alpha)
    {
        _device.Clear(Clear);

        CameraView camera = view.Camera;
        if (camera.Size.X <= 0f || camera.Size.Y <= 0f)
        {
            return;
        }

        // Read the viewport every draw: the window is resizable, so the world-to-pixel
        // scale is not a constant. Aspect mismatch stretches rather than letterboxing.
        Viewport viewport = _device.Viewport;
        Vector2 topLeft = camera.Center - (camera.Size / 2f);
        Matrix worldToScreen =
            Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0f) *
            Matrix.CreateScale(viewport.Width / camera.Size.X, viewport.Height / camera.Size.Y, 1f);

        _batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: worldToScreen);

        foreach (QuadIntent quad in view.Quads)
        {
            Vector2 position = Vector2.Lerp(quad.PreviousPosition, quad.Position, alpha);

            _batch.Draw(
                _white,
                new XnaVector2(position.X, position.Y),
                sourceRectangle: null,
                Color.FromNonPremultiplied(quad.Color.R, quad.Color.G, quad.Color.B, quad.Color.A),
                rotation: 0f,
                origin: XnaVector2.Zero,
                scale: new XnaVector2(quad.Size.X, quad.Size.Y),
                effects: SpriteEffects.None,
                layerDepth: 0f);
        }

        _batch.End();
    }

    public void Dispose()
    {
        _batch.Dispose();
        _white.Dispose();
    }
}
