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

    // The engine's default bitmap font page, loaded from this assembly once for the renderer.
    private readonly Texture2D _defaultFontPage;

    // Every texture the engine owns, so the draw path resolves one handle through one table.
    private readonly Dictionary<TextureHandle, Texture2D> _engineTextures;

    // The declared canvas, or null when the world rasterises straight into the back buffer at
    // whatever size the window is.
    private readonly (int Width, int Height)? _canvas;

    // Null exactly when no canvas is declared. Its extent is the canvas under Letterbox and grows
    // with the resolved world rect under a fit that reveals more of it.
    private RenderTarget2D? _target;

    // Where the screen layer landed on the last frame drawn, which is what turns a sampled mouse
    // position back into a canvas position. Seeded by ResolveScreenLayer before the first frame, since
    // the host samples the mouse ahead of that frame.
    private ScreenPlacement _placement = ScreenPlacement.Identity;

    // Where the last frame's world landed in the back buffer, which is what a host-owned
    // world-anchored draw over that frame is placed by; null until a frame has drawn a world.
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

        ScreenLayout layout = Layout(_canvas, view, outputWidth, outputHeight);
        Rect world = view.Camera.Resolve(alpha, new Vector2(outputWidth, outputHeight));

        if (_canvas is null)
        {
            DrawWorld(view, alpha, world, layout.Span, outputWidth, outputHeight, ScreenPlacement.Identity);
            DrawScreen(view, alpha, layout.OnSurface, outputWidth, outputHeight, view.Sampling);
        }
        else
        {
            RenderTarget2D target = Surface(layout.Surface);

            _device.SetRenderTarget(target);
            DrawWorld(view, alpha, world, layout.Span, target.Width, target.Height, layout.Present);

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

        _device.Viewport = new Viewport(0, 0, width, height);
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
            DrawSprite(sprite, alpha: 1f, snap: true, pixelsPerUnit, ref resolved, ref texture);
        }

        foreach (ref readonly LineIntent line in view.Lines)
        {
            // Snapped on the game surface's grid, the one its sprites were quantised to, so a line
            // lands on the sprite it outlines rather than gliding between its steps.
            DrawLine(line, pixelsPerUnit, world.Snap, world.Fit.Scale);
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
        ScreenLayout layout = Layout(_canvas, view, backBuffer.BackBufferWidth, backBuffer.BackBufferHeight);

        if (layout.Layer.Scale > 0f)
        {
            _placement = layout.Layer;
        }
    }

    // The whole presentation geometry for view on a back buffer of this extent, on both paths: the
    // canvas letterboxed straight into the back buffer, and the render surface presented into it where a
    // resolution is declared. Drawing and pointer mapping must resolve from the same geometry, or a
    // sampled window position comes back as the wrong canvas pixel.
    internal static ScreenLayout Layout(
        (int Width, int Height)? renderResolution,
        FrameView view,
        int outputWidth,
        int outputHeight)
    {
        // The window is the output the fit answers to on both paths: a declared canvas is derived
        // from the resolved rect rather than being the thing that shapes it. The span travels beside
        // the rect because subtracting the rect's edges loses precision far from the origin, and the
        // scale it feeds is what quantises every sprite to the pixel grid.
        Vector2 span = view.Camera.ResolveSpan(new Vector2(outputWidth, outputHeight));

        if (renderResolution is not { } resolution)
        {
            ScreenPlacement windowed = WindowPlacement(view.Canvas, outputWidth, outputHeight);

            return new ScreenLayout(span, (outputWidth, outputHeight), windowed, default, windowed);
        }

        (int Width, int Height) surface = SurfaceSize(resolution, view.Camera.Size, span, outputWidth, outputHeight);
        Vector2 slack = ScreenSlack(surface.Width, surface.Height, view.Canvas);
        ScreenPlacement presented = TargetPlacement(view.Sampling, surface.Width, surface.Height, outputWidth, outputHeight);

        return new ScreenLayout(
            span,
            surface,
            new ScreenPlacement(slack, 1f),
            presented,
            presented.Scale > 0f
                ? new ScreenPlacement(presented.Origin + (slack * presented.Scale), presented.Scale)
                : default);
    }

    // Half of what the surface has over the canvas, in whole surface pixels: under Expand or
    // FixedHeight the surface grows past the canvas to reveal more world, and the screen layer stays
    // the canvas, centred in it.
    internal static Vector2 ScreenSlack(int surfaceWidth, int surfaceHeight, Vector2 canvas) => new(
        MathF.Max(MathF.Floor((surfaceWidth - canvas.X) / 2f), 0f),
        MathF.Max(MathF.Floor((surfaceHeight - canvas.Y) / 2f), 0f));

    // Where the canvas lands straight in the back buffer, with no render surface between them: its
    // own centred fit, so the layer keeps its aspect and its place whatever the camera's fit did
    // with the world behind it. A scale of 0 is a canvas or a window with no area.
    internal static ScreenPlacement WindowPlacement(Vector2 canvas, int outputWidth, int outputHeight)
    {
        Letterbox fit = Letterbox.Fit(canvas.X, canvas.Y, outputWidth, outputHeight);

        return fit.IsEmpty ? default : new ScreenPlacement(new Vector2(fit.X, fit.Y), fit.Scale);
    }

    // Where the render surface's own top-left corner lands in the back buffer, on the fit its
    // sampling mode calls for. A scale of 0 is a surface or a back buffer with no area.
    internal static ScreenPlacement TargetPlacement(
        TextureSampling sampling,
        int targetWidth,
        int targetHeight,
        int containerWidth,
        int containerHeight)
    {
        Letterbox fit = PresentFit(sampling, targetWidth, targetHeight, containerWidth, containerHeight);
        if (fit.IsEmpty)
        {
            return default;
        }

        // The fit's own whole-pixel corner, not the exact centre: a bar of an odd number of pixels
        // centres on a half pixel, which under point sampling puts every texel boundary on a pixel
        // centre and leaves the fill rule to break a tie per row. Linear sampling answers to no
        // pixel grid, so the half pixel the rounded corner gives up is invisible there.
        //
        // The scale travels as one scalar rather than the fit's extents becoming a destination
        // rectangle, whose two extents would round independently and skew the blit.
        return new ScreenPlacement(new Vector2(fit.X, fit.Y), fit.Scale);
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
    // longer reports once narrowed to the letterbox; present is where that surface lands in the
    // back buffer, identity when the surface is the back buffer.
    private void DrawWorld(
        FrameView view,
        float alpha,
        in Rect world,
        Vector2 span,
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

        foreach (ref readonly SpriteIntent sprite in view.Sprites)
        {
            DrawSprite(sprite, alpha, snap, fit.Scale, ref resolved, ref texture);
        }

        foreach (ref readonly LineIntent line in view.Lines)
        {
            DrawLine(line, fit.Scale, snap, fit.Scale);
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
            DrawSprite(sprite, alpha, snap, placement.Scale, ref resolved, ref texture);
        }

        foreach (ref readonly LineIntent line in lines)
        {
            DrawLine(line, placement.Scale, snap, placement.Scale);
        }

        _batch.End();
    }

    // The white texel stretched to the segment's length and thickness and turned along it, from
    // the middle of its left edge on A. surfaceScale is the batch's pixels per unit on the surface
    // being drawn, which is what a hairline's one pixel is in units: the back buffer's here and
    // on the overlay path, the render surface's where one is declared, since a quad thinner than
    // that surface's pixel would miss its pixel centres and not rasterise at all.
    //
    // snap quantises both ends to the grid of snapScale pixels per unit — the grid the frame's
    // sprites were snapped to — before the segment is measured, as DrawSprite does its position.
    private void DrawLine(in LineIntent line, float surfaceScale, bool snap, float snapScale)
    {
        Vector2 a = line.A;
        Vector2 b = line.B;
        if (snap)
        {
            a = PixelGrid.Snap(a, snapScale);
            b = PixelGrid.Snap(b, snapScale);
        }

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

            texture = resolved.IsEngineOwned ? EngineTexture(resolved) : _textures.Get(resolved);
        }

        Vector2 position = StepInterpolation.Interpolate(sprite.PreviousPosition, sprite.Position, alpha);
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

    // TopLeft is the world rect's snapped corner, Fit where that rect landed on the surface,
    // Present where the surface landed in the back buffer, and Snap whether the frame quantised to
    // the surface's pixel grid.
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
