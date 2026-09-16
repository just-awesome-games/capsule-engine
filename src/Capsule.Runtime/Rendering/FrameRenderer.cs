using System.Diagnostics;
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
    private const string DefaultFontPageResource = "Capsule.Runtime.Assets.default-font.png";

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

    private readonly Texture2D _defaultFontPage;

    // Every engine-owned texture, so the draw path resolves one handle through one table.
    private readonly Dictionary<TextureHandle, Texture2D> _engineTextures;

    // The declared canvas, or null when the world rasterises straight into the back buffer at
    // whatever size the window is.
    private readonly (int Width, int Height)? _canvas;

    // Null exactly when no canvas is declared. Its extent is the canvas under Letterbox and grows
    // with the resolved world rect under a fit that reveals more of it.
    private RenderTarget2D? _target;

    // Where the screen layer landed on the last frame drawn, which is what turns a sampled mouse
    // position back into a canvas position; seeded by ResolveScreenLayer before the first frame.
    private ScreenPlacement _placement = ScreenPlacement.Identity;

    // Where the last frame's world landed in the back buffer, which places a host-owned
    // world-anchored draw over that frame; null until a frame has drawn a world.
    private WorldPlacement? _world;

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
        _defaultFontPage = LoadDefaultFontPage(device);
        _engineTextures = new Dictionary<TextureHandle, Texture2D>
        {
            [TextureHandle.White] = _white,
            [TextureHandle.DefaultFontPage] = _defaultFontPage,
        };
        _canvas = renderResolution;

        if (renderResolution is { } resolution)
        {
            _target = new RenderTarget2D(device, resolution.Width, resolution.Height);
        }
    }

    internal (int Width, int Height) BackBufferSize
    {
        get
        {
            PresentationParameters backBuffer = _device.PresentationParameters;

            return (backBuffer.BackBufferWidth, backBuffer.BackBufferHeight);
        }
    }

    private static Texture2D LoadDefaultFontPage(GraphicsDevice device)
    {
        using Stream resource = typeof(FrameRenderer).Assembly.GetManifestResourceStream(DefaultFontPageResource)
            ?? throw new InvalidOperationException($"The embedded default font page '{DefaultFontPageResource}' is missing.");

        return Texture2D.FromStream(device, resource, DefaultColorProcessors.PremultiplyAlpha);
    }

    // The cost of the last game frame Draw submitted; default before the first.
    internal RenderStats LastFrame { get; private set; }

    // Draws one frame. Allocation-free at steady state.
    //
    // alpha: Fraction of a fixed step not yet simulated, clamped to [0, 1]. Each sprite, and the
    // camera looking at it, is drawn that far from its previous position towards its current one.
    internal void Draw(FrameView view, float alpha)
    {
        long started = Stopwatch.GetTimestamp();

        // The scheduler leaves a whole step in the accumulator when a game exits mid-catch-up.
        alpha = Math.Clamp(alpha, 0f, 1f);

        PresentationParameters backBuffer = _device.PresentationParameters;
        int outputWidth = backBuffer.BackBufferWidth;
        int outputHeight = backBuffer.BackBufferHeight;

        ScreenLayout layout = FrameLayout.Layout(_canvas, view, outputWidth, outputHeight);
        Rect world = view.Camera.Place(alpha, layout.Span);

        if (_canvas is null)
        {
            DrawWorld(view, alpha, world, layout.Span, layout.World, outputWidth, outputHeight, ScreenPlacement.Identity);
            DrawScreen(view, alpha, layout.OnSurface, outputWidth, outputHeight, view.Sampling);
        }
        else
        {
            RenderTarget2D target = Surface(layout.Surface);

            _device.SetRenderTarget(target);
            DrawWorld(view, alpha, world, layout.Span, layout.World, target.Width, target.Height, layout.Present);

            // Over the world's own bars: the viewport is the whole surface again and the canvas sits
            // centred in it on whole pixels, so nothing lands on a grid the world did not already use.
            DrawScreen(view, alpha, layout.OnSurface, target.Width, target.Height, view.Sampling);

            _device.SetRenderTarget(null);
            Present(target, view.Sampling, layout.Present);
        }

        if (layout.Layer.Scale > 0f)
        {
            _placement = layout.Layer;
        }

        if (outputWidth > 0 && outputHeight > 0)
        {
            _device.Viewport = new Viewport(0, 0, outputWidth, outputHeight);
        }

        LastFrame = new RenderStats(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    // Draws a host-owned frame over the game frame already submitted to the back buffer: its world
    // list first, placed exactly where the last game frame's world landed so it annotates that
    // frame's sprites, then its screen layer at an integer scale of the whole back buffer. The
    // host's own camera never enters this path; its world list is culled against nothing and drawn
    // at the settled step.
    internal void DrawOverlay(FrameView view, int scale)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        PresentationParameters backBuffer = _device.PresentationParameters;
        int width = backBuffer.BackBufferWidth;
        int height = backBuffer.BackBufferHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_world is { } world && (!view.Sprites.IsEmpty || !view.Lines.IsEmpty))
        {
            DrawOverlayWorld(view, in world, width, height);
        }

        // DrawScreen restores the whole back buffer itself, so nothing is written here for a view
        // whose screen layer is empty.
        DrawScreen(
            view,
            alpha: 1f,
            new ScreenPlacement(System.Numerics.Vector2.Zero, scale),
            width,
            height,
            TextureSampling.Point);
    }

    // Back-buffer pixels per world unit on the last frame drawn, or zero before one has drawn a
    // world: what sizes a screen-sized glyph placed at a world point.
    internal float WorldPixelsPerUnit => _world is { } world ? world.PixelsPerUnit : 0f;

    // The world's region of the back buffer is the fit inside the surface carried through the
    // surface's own placement, and the viewport is that region so nothing lands on the bars. With
    // the viewport's origin at the region's corner, the transform is the world's top-left to the
    // origin and units to back-buffer pixels.
    private void DrawOverlayWorld(FrameView view, in WorldPlacement world, int width, int height)
    {
        Letterbox fit = world.Fit;
        ScreenPlacement present = world.Present;
        int left = (int)MathF.Round(present.Origin.X + (fit.X * present.Scale));
        int top = (int)MathF.Round(present.Origin.Y + (fit.Y * present.Scale));
        int right = Math.Min((int)MathF.Round(present.Origin.X + ((fit.X + fit.Width) * present.Scale)), width);
        int bottom = Math.Min((int)MathF.Round(present.Origin.Y + ((fit.Y + fit.Height) * present.Scale)), height);
        if (left < 0 || top < 0 || right <= left || bottom <= top)
        {
            return;
        }

        _device.Viewport = new Viewport(left, top, right - left, bottom - top);

        float pixelsPerUnit = world.PixelsPerUnit;
        Matrix worldToBackBuffer =
            Matrix.CreateTranslation(-world.TopLeft.X, -world.TopLeft.Y, 0f) *
            Matrix.CreateScale(pixelsPerUnit, pixelsPerUnit, 1f);

        _batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: worldToBackBuffer);

        TextureHandle resolved = default;
        Texture2D? texture = null;
        foreach (ref readonly SpriteIntent sprite in view.Sprites)
        {
            DrawSprite(sprite, alpha: 1f, snap: true, world.TopLeft, world.TopLeft, pixelsPerUnit, ref resolved, ref texture);
        }

        foreach (ref readonly LineIntent line in view.Lines)
        {
            // Snapped on the game surface's grid from the same corner its sprites were quantised
            // from, so a line lands on the sprite it outlines rather than gliding between its steps.
            DrawLine(line, pixelsPerUnit, world.Snap, world.TopLeft, world.TopLeft, world.Fit.Scale);
        }

        _batch.End();
    }

    // Where the screen layer lands in the window: the last frame drawn, or what ResolveScreenLayer
    // settled before the first one.
    internal ScreenPlacement ScreenLayer => _placement;

    // Settles ScreenLayer for view at the back buffer's current extent, drawing nothing. The host
    // samples the mouse before the first frame, and an unplaced layer would hand the simulation window
    // pixels as canvas pixels for that frame.
    internal void ResolveScreenLayer(FrameView view)
    {
        PresentationParameters backBuffer = _device.PresentationParameters;
        ScreenLayout layout = FrameLayout.Layout(_canvas, view, backBuffer.BackBufferWidth, backBuffer.BackBufferHeight);

        if (layout.Layer.Scale > 0f)
        {
            _placement = layout.Layer;
        }
    }

    private RenderTarget2D Surface((int Width, int Height) extent)
    {
        RenderTarget2D target = _target!;
        (int width, int height) = extent;

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

    // Whether this frame drew at all, so a capture request can stand until one does.
    internal bool CanCaptureFrame => FrameCapture.CanCapture(_device);

    // Saves the surface the world was drawn on as a PNG at path; see FrameCapture.
    internal void SaveSurface(string path) => FrameCapture.Save(_device, _target, path);

    // surfaceWidth and surfaceHeight are the bound surface's own extent, which the viewport no
    // longer reports once narrowed to the letterbox; present is where that surface lands in the
    // back buffer, identity when the surface is the back buffer.
    private void DrawWorld(
        FrameView view,
        float alpha,
        in Rect world,
        Vector2 span,
        in Letterbox fit,
        int surfaceWidth,
        int surfaceHeight,
        in ScreenPlacement present)
    {
        _world = null;

        // A minimised window can present a back buffer with no area.
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            return;
        }

        // Cleared at full extent before the viewport narrows, keeping presentation bars black.
        _device.Viewport = new Viewport(0, 0, surfaceWidth, surfaceHeight);
        _device.Clear(BarColor);

        if (world.IsEmpty || fit.IsEmpty)
        {
            return;
        }

        _device.Viewport = new Viewport(fit.X, fit.Y, fit.Width, fit.Height);

        // The camera interpolated on the same clock as what it looks at; snapping it to the step's
        // end instead would slide the whole world back once per step. The corner itself is not
        // snapped: each sprite snaps to the grid anchored here, so a sprite is a whole number of
        // pixels from the corner wherever the corner sits, and a followed sprite never crawls
        // between two roundings. The simulation keeps its fractional positions.
        Vector2 topLeft = new(world.Left, world.Top);
        bool snap = view.Sampling == TextureSampling.Point;

        if (present.Scale > 0f)
        {
            _world = new WorldPlacement(topLeft, fit, present, snap);
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

        // Each scrolled run is drawn from its own layer's corner, formed once as the run opens;
        // the runs are in list order, so one cursor walks them beside the sprites and again
        // beside the lines.
        ReadOnlySpan<ParallaxLayer> layers = view.ParallaxLayers;
        Vector2 origin = view.Camera.ScrollOrigin;
        Vector2 corner = topLeft;
        int next = 0;

        ReadOnlySpan<SpriteIntent> sprites = view.Sprites;
        for (int index = 0; index < sprites.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstSprite <= index)
            {
                corner = ScrollLayout.Corner(topLeft, origin, layers[next].ScrollFactor);
                next++;
            }

            DrawSprite(sprites[index], alpha, snap, corner, topLeft, fit.Scale, ref resolved, ref texture);
        }

        corner = topLeft;
        next = 0;

        ReadOnlySpan<LineIntent> lines = view.Lines;
        for (int index = 0; index < lines.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstLine <= index)
            {
                corner = ScrollLayout.Corner(topLeft, origin, layers[next].ScrollFactor);
                next++;
            }

            DrawLine(lines[index], fit.Scale, snap, corner, topLeft, fit.Scale);
        }

        _batch.End();
    }

    // The screen layer, in canvas pixels placed by placement. Drawn after the world and over the
    // whole surface, so it covers the bars the world's own fit left.
    private void DrawScreen(
        FrameView view,
        float alpha,
        in ScreenPlacement placement,
        int surfaceWidth,
        int surfaceHeight,
        TextureSampling sampling)
    {
        ReadOnlySpan<SpriteIntent> sprites = view.ScreenSprites;
        ReadOnlySpan<LineIntent> lines = view.ScreenLines;
        if ((sprites.IsEmpty && lines.IsEmpty) || surfaceWidth <= 0 || surfaceHeight <= 0 || !(placement.Scale > 0f))
        {
            return;
        }

        _device.Viewport = new Viewport(0, 0, surfaceWidth, surfaceHeight);

        Matrix canvasToSurface =
            Matrix.CreateScale(placement.Scale, placement.Scale, 1f) *
            Matrix.CreateTranslation(placement.Origin.X, placement.Origin.Y, 0f);

        _batch.Begin(samplerState: Sampler(sampling), transformMatrix: canvasToSurface);

        bool snap = sampling == TextureSampling.Point;
        TextureHandle resolved = default;
        Texture2D? texture = null;

        foreach (ref readonly SpriteIntent sprite in sprites)
        {
            DrawSprite(sprite, alpha, snap, Vector2.Zero, Vector2.Zero, placement.Scale, ref resolved, ref texture);
        }

        foreach (ref readonly LineIntent line in lines)
        {
            DrawLine(line, placement.Scale, snap, Vector2.Zero, Vector2.Zero, placement.Scale);
        }

        _batch.End();
    }

    // The white texel stretched to the segment's length and thickness and turned along it, from
    // the middle of its left edge on A. surfaceScale is the batch's pixels per unit on the surface
    // being drawn, which is what a hairline's one pixel is in units: the back buffer's here and
    // on the overlay path, the render surface's where one is declared, since a quad thinner than
    // that surface's pixel would miss its pixel centres and not rasterise at all.
    //
    // Both ends are placed from layerCorner into the frame at frameCorner as DrawSprite places its
    // position, snap quantising them to the grid of snapScale pixels per unit — the grid the
    // frame's sprites were snapped to — before the segment is measured, so a line lands on the
    // sprite it outlines.
    private void DrawLine(in LineIntent line, float surfaceScale, bool snap, Vector2 layerCorner, Vector2 frameCorner, float snapScale)
    {
        Vector2 a = ScrollLayout.Place(line.A, layerCorner, frameCorner, snap, snapScale);
        Vector2 b = ScrollLayout.Place(line.B, layerCorner, frameCorner, snap, snapScale);

        Vector2 delta = b - a;
        float length = delta.Length();

        float thickness = line.Thickness > 0f ? line.Thickness : 1f / surfaceScale;

        _batch.Draw(
            _white,
            new XnaVector2(a.X, a.Y),
            sourceRectangle: null,
            ToBackendColor(line.Color),
            rotation: MathF.Atan2(delta.Y, delta.X),
            origin: new XnaVector2(0f, 0.5f),
            scale: new XnaVector2(length, thickness),
            effects: SpriteEffects.None,
            layerDepth: 0f);
    }

    // resolved is the handle texture was fetched for; both are carried across the whole stream.
    // layerCorner is the corner of the camera this sprite's layer is drawn by and frameCorner the
    // corner of the rect the frame draws — the same point on a world pass outside a scrolled run,
    // and the canvas's origin on a screen pass — and the pixel grid is anchored at the frame's.
    private void DrawSprite(
        in SpriteIntent sprite,
        float alpha,
        bool snap,
        Vector2 layerCorner,
        Vector2 frameCorner,
        float surfaceScale,
        ref TextureHandle resolved,
        ref Texture2D? texture)
    {
        if (texture is null || sprite.Sprite.Texture != resolved)
        {
            resolved = sprite.Sprite.Texture;

            texture = resolved.IsEngineOwned ? EngineTexture(resolved) : _textures.Get(resolved);
        }

        Vector2 position = ScrollLayout.Place(
            StepInterpolation.Interpolate(sprite.PreviousPosition, sprite.Position, alpha),
            layerCorner,
            frameCorner,
            snap,
            surfaceScale);

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

    private Texture2D EngineTexture(in TextureHandle handle) =>
        _engineTextures.TryGetValue(handle, out Texture2D? texture)
            ? texture
            : throw new ArgumentException($"Unknown engine-owned texture handle '{handle.Name}'.", nameof(handle));

    // Letterboxed a second time, into the back buffer, at the placement TargetPlacement resolved.
    private void Present(RenderTarget2D target, TextureSampling sampling, in ScreenPlacement placement)
    {
        // Unbinding the target restored the viewport to the whole back buffer.
        PresentationParameters backBuffer = _device.PresentationParameters;
        if (backBuffer.BackBufferWidth <= 0 || backBuffer.BackBufferHeight <= 0)
        {
            return;
        }

        _device.Clear(BarColor);

        if (!(placement.Scale > 0f))
        {
            return;
        }

        _batch.Begin(samplerState: Sampler(sampling));
        _batch.Draw(
            target,
            new XnaVector2(placement.Origin.X, placement.Origin.Y),
            sourceRectangle: null,
            Color.White,
            rotation: 0f,
            origin: XnaVector2.Zero,
            placement.Scale,
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

    // TopLeft is the world rect's corner, which the frame's pixel grid is anchored at, Fit where
    // that rect landed on the surface, Present where the surface landed in the back buffer, and
    // Snap whether the frame quantised to the surface's pixel grid.
    private readonly record struct WorldPlacement(Vector2 TopLeft, Letterbox Fit, ScreenPlacement Present, bool Snap)
    {
        internal float PixelsPerUnit => Fit.Scale * Present.Scale;
    }

    public void Dispose()
    {
        _batch.Dispose();
        foreach (Texture2D texture in _engineTextures.Values)
        {
            texture.Dispose();
        }

        _target?.Dispose();
    }
}
