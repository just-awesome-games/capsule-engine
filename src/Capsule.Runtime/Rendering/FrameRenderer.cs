using Capsule.Assets;
using Capsule.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Vector2 = System.Numerics.Vector2;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace Capsule.Runtime.Rendering;

/// <summary>
/// Draws a <see cref="FrameView"/>. Holds no scene state of its own: what a sprite interpolates
/// from travels in the sprite.
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
    private readonly TextureStore _textures;

    // One white texel, tinted and stretched across the camera to draw the clear colour.
    private readonly Texture2D _white;

    // Null when no render resolution is declared: the world then rasterises straight into the
    // back buffer at whatever size the window is.
    private readonly RenderTarget2D? _target;

    /// <param name="device">The device to rasterise on.</param>
    /// <param name="renderResolution">A fixed render surface, or null to draw into the back buffer.</param>
    /// <param name="textures">The resident textures to draw from; owned by the caller.</param>
    internal FrameRenderer(GraphicsDevice device, (int Width, int Height)? renderResolution, TextureStore textures)
    {
        _device = device;
        _batch = new SpriteBatch(device);
        _textures = textures;
        _white = new Texture2D(device, 1, 1);
        _white.SetData<Color>([Color.White]);

        // Fixed size, so neither a window resize nor a fullscreen toggle ever churns it.
        _target = renderResolution is { } resolution
            ? new RenderTarget2D(device, resolution.Width, resolution.Height)
            : null;
    }

    /// <summary>Draws one frame. Allocation-free at steady state.</summary>
    /// <param name="view">What the simulation wants drawn.</param>
    /// <param name="alpha">
    /// Fraction of a fixed step not yet simulated, clamped to [0, 1]. Each sprite, and the camera
    /// looking at it, is drawn that far from its previous position towards its current one.
    /// </param>
    internal void Draw(FrameView view, float alpha)
    {
        // The scheduler leaves a whole step in the accumulator when a game exits mid-catch-up.
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

    // surfaceWidth and surfaceHeight are the bound surface's own extent, which the viewport no
    // longer reports once narrowed to the letterbox.
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

        // The camera interpolates on the same clock as what it looks at; snapping it to the step's
        // end instead would slide the whole world back once per step.
        Vector2 center = Vector2.Lerp(camera.PreviousCenter, camera.Center, alpha);
        Vector2 topLeft = center - (camera.Size / 2f);

        // Camera and sprites quantise to the same grid, so the two never disagree by a pixel. The
        // simulation keeps its fractional positions.
        bool snap = view.Sampling == TextureSampling.Point;
        if (snap)
        {
            topLeft = PixelGrid.Snap(topLeft, fit.Scale);
        }

        Matrix worldToScreen =
            Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0f) *
            Matrix.CreateScale(fit.Scale, fit.Scale, 1f);

        _batch.Begin(samplerState: Sampler(view.Sampling), transformMatrix: worldToScreen);

        // Drawn through the narrowed world viewport, so presentation bars stay black.
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

        // Compared before the dictionary is asked, so the lookup is once per texture change
        // rather than once per sprite.
        TextureHandle resolved = default;
        Texture2D? texture = null;

        ReadOnlySpan<SpriteIntent> sprites = view.Sprites;
        foreach (RenderCommand command in view.Commands)
        {
            if (command.Kind != RenderKind.Sprite)
            {
                throw new InvalidOperationException($"Unknown render kind '{command.Kind}'.");
            }

            DrawSprite(sprites[command.Index], alpha, snap, fit.Scale, ref resolved, ref texture);
        }

        _batch.End();
    }

    // resolved is the handle texture was fetched for; both are carried across the whole stream.
    private void DrawSprite(
        in SpriteIntent sprite,
        float alpha,
        bool snap,
        float surfaceScale,
        ref TextureHandle resolved,
        ref Texture2D? texture)
    {
        if (texture is null || sprite.Sprite.Texture != resolved)
        {
            resolved = sprite.Sprite.Texture;
            texture = _textures.Get(resolved);
        }

        Vector2 position = Vector2.Lerp(sprite.PreviousPosition, sprite.Position, alpha);
        if (snap)
        {
            position = PixelGrid.Snap(position, surfaceScale);
        }

        TextureRegion region = sprite.Sprite.Region;
        Vector2 origin = sprite.DrawOrigin;

        _batch.Draw(
            texture,
            new XnaVector2(position.X, position.Y),
            new Rectangle(region.X, region.Y, region.Width, region.Height),
            ToBackendColor(sprite.Color),
            rotation: 0f,
            origin: new XnaVector2(origin.X, origin.Y),
            scale: new XnaVector2(sprite.Size.X / region.Width, sprite.Size.Y / region.Height),
            effects: Mirroring(sprite),
            layerDepth: 0f);
    }

    // The drawn rect is already placed by the mirrored origin; these only swap the texture
    // coordinates that fill it.
    private static SpriteEffects Mirroring(in SpriteIntent sprite) =>
        (sprite.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None)
        | (sprite.FlipY ? SpriteEffects.FlipVertically : SpriteEffects.None);

    // Letterboxed a second time, into the back buffer.
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

        // One scalar scale and a fractional position rather than a destination rectangle, whose
        // two extents would round to whole pixels independently and skew the blit.
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
