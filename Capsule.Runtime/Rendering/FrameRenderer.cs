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

    // Null when no render resolution is declared: the world then rasterises straight into
    // the back buffer at whatever size the window is.
    private readonly RenderTarget2D? _target;

    internal FrameRenderer(GraphicsDevice device, (int Width, int Height)? renderResolution)
    {
        _device = device;
        _batch = new SpriteBatch(device);
        _white = new Texture2D(device, 1, 1);
        _white.SetData<Color>([Color.White]);

        // Fixed size, so neither a window resize nor a fullscreen toggle ever churns it.
        _target = renderResolution is { } resolution
            ? new RenderTarget2D(device, resolution.Width, resolution.Height)
            : null;
    }

    // Allocation-free at steady state: the span is iterated directly, the matrix and the
    // fits are on the stack, and there is no closure or LINQ in the path.

    /// <param name="alpha">
    /// Fraction of a fixed step not yet simulated, in [0, 1). Each quad is drawn that
    /// far from its previous position towards its current one.
    /// </param>
    internal void Draw(FrameView view, float alpha)
    {
        if (_target is null)
        {
            PresentationParameters backBuffer = _device.PresentationParameters;
            DrawWorld(view, alpha, backBuffer.BackBufferWidth, backBuffer.BackBufferHeight);

            return;
        }

        _device.SetRenderTarget(_target);
        DrawWorld(view, alpha, _target.Width, _target.Height);
        _device.SetRenderTarget(null);
        Present(_target);
    }

    /// <summary>
    /// Rasterises the world into the surface already bound, letterboxed to the camera's
    /// shape. <paramref name="surfaceWidth"/> and <paramref name="surfaceHeight"/> are that
    /// surface's own extent, which the viewport no longer reports once narrowed.
    /// </summary>
    private void DrawWorld(FrameView view, float alpha, int surfaceWidth, int surfaceHeight)
    {
        // A minimised window can present a back buffer with no area.
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            return;
        }

        // Cleared at full extent, before the viewport narrows: the bars are then black
        // whether or not the backend scissors a clear.
        _device.Viewport = new Viewport(0, 0, surfaceWidth, surfaceHeight);
        _device.Clear(Clear);

        CameraView camera = view.Camera;
        if (camera.Size.X <= 0f || camera.Size.Y <= 0f)
        {
            return;
        }

        Letterbox fit = Letterbox.Fit(camera.Size.X, camera.Size.Y, surfaceWidth, surfaceHeight);
        if (fit.IsEmpty)
        {
            return;
        }

        _device.Viewport = new Viewport(fit.X, fit.Y, fit.Width, fit.Height);

        Vector2 topLeft = camera.Center - (camera.Size / 2f);
        Matrix worldToScreen =
            Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0f) *
            Matrix.CreateScale(fit.Scale, fit.Scale, 1f);

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

    /// <summary>Blits <paramref name="target"/> into the back buffer, letterboxed a second time.</summary>
    private void Present(RenderTarget2D target)
    {
        // Unbinding the target restored the viewport to the whole back buffer.
        PresentationParameters backBuffer = _device.PresentationParameters;
        if (backBuffer.BackBufferWidth <= 0 || backBuffer.BackBufferHeight <= 0)
        {
            return;
        }

        _device.Clear(Clear);

        Letterbox fit = Letterbox.Fit(target.Width, target.Height, backBuffer.BackBufferWidth, backBuffer.BackBufferHeight);
        if (fit.IsEmpty)
        {
            return;
        }

        // One scalar scale and a fractional position rather than a destination rectangle:
        // rounding the two extents to whole pixels independently is what makes a blit
        // anisotropic. The sub-pixel slack at the far edge is already cleared black.
        XnaVector2 position = new(
            (backBuffer.BackBufferWidth - (target.Width * fit.Scale)) / 2f,
            (backBuffer.BackBufferHeight - (target.Height * fit.Scale)) / 2f);

        _batch.Begin(samplerState: SamplerState.PointClamp);
        _batch.Draw(
            target,
            position,
            sourceRectangle: null,
            Color.White,
            rotation: 0f,
            origin: XnaVector2.Zero,
            fit.Scale,
            SpriteEffects.None,
            layerDepth: 0f);
        _batch.End();
    }

    public void Dispose()
    {
        _batch.Dispose();
        _white.Dispose();
        _target?.Dispose();
    }
}
