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
    // Bars are presentation rather than world intent and remain black.
    private static readonly Color BarColor = Color.FromNonPremultiplied(
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

    /// <param name="view">What the simulation wants drawn.</param>
    /// <param name="alpha">
    /// Fraction of a fixed step not yet simulated, clamped to [0, 1]. Each quad, and the camera
    /// looking at it, is drawn that far from its previous position towards its current one.
    /// </param>
    internal void Draw(FrameView view, float alpha)
    {
        // The scheduler leaves the accumulator holding a whole step when a game exits mid-catch-up,
        // and an alpha past 1 would throw every quad beyond where it actually is.
        alpha = Math.Clamp(alpha, 0f, 1f);

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

        // Cleared at full extent before the viewport narrows, keeping presentation bars black.
        _device.Viewport = new Viewport(0, 0, surfaceWidth, surfaceHeight);
        _device.Clear(BarColor);

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

        // The camera moves on the same clock as what it looks at. Snapping it to the step's end
        // while the quads interpolate would slide the whole world back and let it catch up once
        // per step, which is the one artefact interpolation exists to remove.
        Vector2 center = Vector2.Lerp(camera.PreviousCenter, camera.Center, alpha);
        Vector2 topLeft = center - (camera.Size / 2f);
        Matrix worldToScreen =
            Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0f) *
            Matrix.CreateScale(fit.Scale, fit.Scale, 1f);

        _batch.Begin(samplerState: Sampler(view.Sampling), transformMatrix: worldToScreen);

        // Clear colour is world intent rather than host configuration. Drawing it through the
        // narrowed world viewport leaves presentation bars black on every backend.
        _batch.Draw(
            _white,
            new XnaVector2(topLeft.X, topLeft.Y),
            sourceRectangle: null,
            ToBackendColor(view.ClearColor),
            rotation: 0f,
            origin: XnaVector2.Zero,
            scale: new XnaVector2(camera.Size.X, camera.Size.Y),
            effects: SpriteEffects.None,
            layerDepth: 0f);

        foreach (QuadIntent quad in view.Quads)
        {
            Vector2 position = Vector2.Lerp(quad.PreviousPosition, quad.Position, alpha);

            _batch.Draw(
                _white,
                new XnaVector2(position.X, position.Y),
                sourceRectangle: null,
                ToBackendColor(quad.Color),
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

        _device.Clear(BarColor);

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

    private static SamplerState Sampler(TextureSampling sampling) => sampling switch
    {
        TextureSampling.Linear => SamplerState.LinearClamp,
        TextureSampling.Point => SamplerState.PointClamp,
        _ => throw new ArgumentOutOfRangeException(nameof(sampling), sampling, "Unknown texture sampling mode."),
    };

    // ColorRgba is straight alpha and the backend blend convention is premultiplied.
    private static Color ToBackendColor(ColorRgba color) =>
        Color.FromNonPremultiplied(color.R, color.G, color.B, color.A);

    public void Dispose()
    {
        _batch.Dispose();
        _white.Dispose();
        _target?.Dispose();
    }
}
