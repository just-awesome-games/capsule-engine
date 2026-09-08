using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Vector2 = System.Numerics.Vector2;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace Capsule.Runtime.Rendering;

// Draws a FrameView. Holds no scene state of its own: what a sprite interpolates from travels in
// the sprite.
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

    // The declared canvas, or null when the world rasterises straight into the back buffer at
    // whatever size the window is.
    private readonly (int Width, int Height)? _canvas;

    // Null exactly when no canvas is declared. Its extent is the canvas under Letterbox and grows
    // with the resolved world rect under a fit that reveals more of it.
    private RenderTarget2D? _target;

    // renderResolution: A fixed render surface, or null to draw into the back buffer.
    //
    // textures: The scene texture cache, loading on first use; owned by the caller.
    internal FrameRenderer(GraphicsDevice device, (int Width, int Height)? renderResolution, TextureStore textures)
    {
        _device = device;
        _batch = new SpriteBatch(device);
        _textures = textures;
        _white = new Texture2D(device, 1, 1);
        _white.SetData<Color>([Color.White]);
        _canvas = renderResolution;

        if (renderResolution is { } resolution)
        {
            _target = new RenderTarget2D(device, resolution.Width, resolution.Height);
        }
    }

    // Draws one frame. Allocation-free at steady state.
    //
    // alpha: Fraction of a fixed step not yet simulated, clamped to [0, 1]. Each sprite, and the
    // camera looking at it, is drawn that far from its previous position towards its current one.
    internal void Draw(FrameView view, float alpha)
    {
        // The scheduler leaves a whole step in the accumulator when a game exits mid-catch-up.
        alpha = Math.Clamp(alpha, 0f, 1f);

        PresentationParameters backBuffer = _device.PresentationParameters;
        int outputWidth = backBuffer.BackBufferWidth;
        int outputHeight = backBuffer.BackBufferHeight;

        // The window is the output the fit answers to on both paths: a declared canvas is derived
        // from the resolved rect rather than being the thing that shapes it. The span travels
        // beside the rect because subtracting the rect's edges loses precision far from the
        // origin, and the scale it feeds is what quantises every sprite to the pixel grid.
        Vector2 output = new(outputWidth, outputHeight);
        Vector2 span = view.Camera.ResolveSpan(output);
        ViewBounds world = view.Camera.Resolve(alpha, output);

        if (_canvas is not { } canvas)
        {
            DrawWorld(view, alpha, world, span, outputWidth, outputHeight);

            return;
        }

        RenderTarget2D target = Surface(canvas, view.Camera.Size, span, outputWidth, outputHeight);

        _device.SetRenderTarget(target);
        DrawWorld(view, alpha, world, span, target.Width, target.Height);
        _device.SetRenderTarget(null);
        Present(target, view.Sampling);
    }

    private RenderTarget2D Surface(
        (int Width, int Height) canvas,
        Vector2 declaredSpan,
        Vector2 resolvedSpan,
        int outputWidth,
        int outputHeight)
    {
        RenderTarget2D target = _target!;
        (int width, int height) = SurfaceSize(canvas, declaredSpan, resolvedSpan, outputWidth, outputHeight);

        if (target.Width == width && target.Height == height)
        {
            return target;
        }

        // Rebound before the old surface goes, or the device keeps a disposed target bound.
        _device.SetRenderTarget(null);
        target.Dispose();
        _target = new RenderTarget2D(_device, width, height);

        return _target;
    }

    // The surface a declared canvas draws on for a resolved world rect. Pixels per world unit are
    // whatever the canvas and the camera's declared span give, so the fit changes how much world is
    // on the surface and never how large a world unit is on it. Under Letterbox the resolved rect
    // is that declared span, so the surface is the canvas exactly. It never shrinks below the
    // canvas, and never exceeds the back buffer on an axis: past that the present can only scale
    // the extra pixels back down, so they buy nothing and cost the whole surface every frame.
    internal static (int Width, int Height) SurfaceSize(
        (int Width, int Height) canvas,
        Vector2 declaredSpan,
        Vector2 resolvedSpan,
        int outputWidth,
        int outputHeight)
    {
        if (!(declaredSpan.X > 0f) || !(declaredSpan.Y > 0f) || !(resolvedSpan.X > 0f) || !(resolvedSpan.Y > 0f))
        {
            return canvas;
        }

        float pixelsPerUnit = MathF.Min(canvas.Width / declaredSpan.X, canvas.Height / declaredSpan.Y);

        return (
            Extent(resolvedSpan.X, pixelsPerUnit, canvas.Width, outputWidth),
            Extent(resolvedSpan.Y, pixelsPerUnit, canvas.Height, outputHeight));
    }

    private static int Extent(float span, float pixelsPerUnit, int canvas, int output) =>
        Math.Clamp((int)MathF.Round(span * pixelsPerUnit), canvas, Math.Max(canvas, output));

    // Whether this frame drew at all. A back buffer with no area, as a minimised window has,
    // presents nothing and leaves no frame to save — including behind a render target, which is
    // drawn but never presented.
    internal bool CanCaptureFrame =>
        _device.PresentationParameters.BackBufferWidth > 0 && _device.PresentationParameters.BackBufferHeight > 0;

    // The capture is staged beside its destination under a name ending in this suffix. A random
    // segment precedes it, so a capture never touches a file it did not create.
    internal const string TemporarySuffix = ".tmp";

    // Saves the surface the world was drawn on as a PNG at path, creating the directory it names
    // and overwriting the file. Called after Draw and before the frame is presented, while that
    // surface still holds the frame: the render target where one is configured, whose extent is
    // the declared render resolution and so is independent of the window, and the back buffer
    // where there is none. Read-back and encoding failures are logged rather than thrown into the
    // frame loop, and the destination is replaced only once a whole PNG is in hand, so whatever is
    // already there survives a capture that failed.
    internal void SaveSurface(string path)
    {
        if (!CanCaptureFrame)
        {
            return;
        }

        byte[] png;

        try
        {
            using MemoryStream encoded = new();
            EncodeSurface(encoded);
            png = encoded.ToArray();
        }
        catch (Exception error)
        {
            Log.Warning($"Frame capture to '{path}' failed before writing: {error.Message}");
            return;
        }

        WriteCapture(png, path);
    }

    // Encoded PNG lands on a temporary sibling this call creates exclusively and moves onto the
    // destination only once it is whole: neither a partial write nor a denied one touches the file
    // already at path, nor any other file already beside it. Resolving path is part of the
    // protected operation, so a path the file system rejects is logged rather than thrown.
    internal static void WriteCapture(byte[] png, string path)
    {
        // Null until this call owns a staging file, so cleanup never deletes a sibling it found.
        string? created = null;

        try
        {
            string full = Path.GetFullPath(path);

            if (Path.GetDirectoryName(full) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = full + '.' + Path.GetRandomFileName() + TemporarySuffix;

            using (FileStream staging = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = temporary;
                staging.Write(png);
            }

            File.Move(temporary, full, overwrite: true);
            created = null;
        }
        catch (Exception error)
        {
            Log.Warning($"Frame capture to '{path}' failed while writing: {error.Message}");

            if (created is not null)
            {
                Discard(created);
            }
        }
    }

    // The temporary is all a failed write can have left behind, and a truncated PNG nobody can
    // read is worse than none.
    private static void Discard(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception error)
        {
            Log.Warning($"Removing the failed frame capture at '{temporary}' failed: {error.Message}");
        }
    }

    private void EncodeSurface(Stream destination)
    {
        if (_target is not null)
        {
            _target.SaveAsPng(destination, _target.Width, _target.Height);
            return;
        }

        PresentationParameters backBuffer = _device.PresentationParameters;
        int width = backBuffer.BackBufferWidth;
        int height = backBuffer.BackBufferHeight;

        Color[] pixels = new Color[width * height];
        _device.GetBackBufferData(pixels);

        using Texture2D surface = new(_device, width, height);
        surface.SetData(pixels);
        surface.SaveAsPng(destination, width, height);
    }

    // surfaceWidth and surfaceHeight are the bound surface's own extent, which the viewport no
    // longer reports once narrowed to the letterbox.
    private void DrawWorld(FrameView view, float alpha, in ViewBounds world, Vector2 span, int surfaceWidth, int surfaceHeight)
    {
        // A minimised window can present a back buffer with no area.
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            return;
        }

        // Cleared at full extent before the viewport narrows, keeping presentation bars black.
        _device.Viewport = new Viewport(0, 0, surfaceWidth, surfaceHeight);
        _device.Clear(BarColor);

        if (world.IsEmpty)
        {
            return;
        }

        Letterbox fit = Letterbox.Fit(span.X, span.Y, surfaceWidth, surfaceHeight);
        if (fit.IsEmpty)
        {
            return;
        }

        _device.Viewport = new Viewport(fit.X, fit.Y, fit.Width, fit.Height);

        // The camera interpolated on the same clock as what it looks at; snapping it to the step's
        // end instead would slide the whole world back once per step.
        Vector2 topLeft = new(world.Left, world.Top);

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
            scale: new XnaVector2(span.X, span.Y),
            effects: SpriteEffects.None,
            layerDepth: 0f);

        // Compared before the dictionary is asked, so the lookup is once per texture change
        // rather than once per sprite.
        TextureHandle resolved = default;
        Texture2D? texture = null;

        foreach (ref readonly SpriteIntent sprite in view.Sprites)
        {
            DrawSprite(sprite, alpha, snap, fit.Scale, ref resolved, ref texture);
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

    // Letterboxed a second time, into the back buffer, on the fit its sampling mode calls for.
    private void Present(RenderTarget2D target, TextureSampling sampling)
    {
        // Unbinding the target restored the viewport to the whole back buffer.
        PresentationParameters backBuffer = _device.PresentationParameters;
        if (backBuffer.BackBufferWidth <= 0 || backBuffer.BackBufferHeight <= 0)
        {
            return;
        }

        _device.Clear(BarColor);

        Letterbox fit = PresentFit(sampling, target.Width, target.Height, backBuffer.BackBufferWidth, backBuffer.BackBufferHeight);
        if (fit.IsEmpty)
        {
            return;
        }

        // One scalar scale and a fractional position rather than a destination rectangle, whose
        // two extents would round to whole pixels independently and skew the blit.
        XnaVector2 position = new(
            (backBuffer.BackBufferWidth - (target.Width * fit.Scale)) / 2f,
            (backBuffer.BackBufferHeight - (target.Height * fit.Scale)) / 2f);

        _batch.Begin(samplerState: Sampler(sampling));
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

    // Which fit the render surface takes into the back buffer. Point sampling owes its source
    // pixels a square block each, so it takes the whole scale and lets the bars absorb the
    // remainder; linear sampling answers to no pixel grid and fills the window.
    internal static Letterbox PresentFit(
        TextureSampling sampling,
        int targetWidth,
        int targetHeight,
        int containerWidth,
        int containerHeight) =>
        sampling == TextureSampling.Point
            ? Letterbox.FitPixels(targetWidth, targetHeight, containerWidth, containerHeight)
            : Letterbox.Fit(targetWidth, targetHeight, containerWidth, containerHeight);

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
