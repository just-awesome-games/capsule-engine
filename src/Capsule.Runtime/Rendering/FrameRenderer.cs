using System.Diagnostics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Vector2 = System.Numerics.Vector2;

namespace Capsule.Runtime.Rendering;

// Draws a FrameView. Holds no scene state of its own, since what a sprite interpolates from travels
// in the sprite.
internal sealed class FrameRenderer : IDisposable
{
    private const string DefaultFontPageResource = "Capsule.Runtime.Assets.default-font.png";

    // Bars are presentation, not world intent, and stay black.
    private static readonly Color BarColor = Color.FromNonPremultiplied(
        ColorRgba.Black.R,
        ColorRgba.Black.G,
        ColorRgba.Black.B,
        ColorRgba.Black.A);

    private readonly GraphicsDevice _device;
    private readonly SpriteBatcher _batcher;
    private readonly TextureStore _textures;

    // One white texel, tinted and stretched across the camera to draw the clear colour.
    private readonly Texture2D _white;

    // The engine's radial light falloff, computed once at startup. Registered under TextureHandle.Light.
    private readonly Texture2D _light;

    private readonly Texture2D _defaultFontPage;

    // Every engine-owned texture, so the draw path resolves a handle through a single table.
    private readonly Dictionary<TextureHandle, Texture2D> _engineTextures;

    // The declared canvas, or null when the world rasterises straight into the back buffer at
    // whatever size the window is.
    private readonly (int Width, int Height)? _canvas;

    // Null when no canvas is declared. Its extent is the canvas under Letterbox and grows with the
    // resolved world rect under a fit that reveals more of it.
    private RenderTarget2D? _target;

    // The frame's light map, allocated at the surface's size on the first frame that lights and
    // reallocated when that size changes. Null while no frame has lit.
    private RenderTarget2D? _lightMap;

    // Where the screen layer landed on the last frame drawn. It turns a sampled mouse position back
    // into a canvas position, and ResolveScreenLayer seeds it before the first frame.
    private ScreenPlacement _placement = ScreenPlacement.Identity;

    // Where the last frame's world landed in the back buffer, which places a host-owned
    // world-anchored draw over that frame. Null until a frame has drawn a world.
    private WorldPlacement? _world;

    // renderResolution: A fixed render surface, or null to draw into the back buffer.
    //
    // textures: The scene texture cache, loading on first use. The caller owns it.
    internal FrameRenderer(GraphicsDevice device, (int Width, int Height)? renderResolution, TextureStore textures)
    {
        _device = device;
        _batcher = new SpriteBatcher(device);
        _textures = textures;
        _white = new Texture2D(device, 1, 1);
        _white.SetData<Color>([Color.White]);
        _light = BuildLightTexture(device);
        _defaultFontPage = LoadDefaultFontPage(device);
        _engineTextures = new Dictionary<TextureHandle, Texture2D>
        {
            [TextureHandle.White] = _white,
            [TextureHandle.Light] = _light,
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

    // The engine's radial light: full at the centre, falling to nothing at the edge on a squared
    // falloff. Computed once, so the engine ships no PNG and no byte table for it.
    private static Texture2D BuildLightTexture(GraphicsDevice device)
    {
        const int Size = 128;
        const float Center = 64f;

        Texture2D texture = new(device, Size, Size);
        Color[] texels = new Color[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) - Center;
                float dy = (y + 0.5f) - Center;
                float d = MathF.Sqrt((dx * dx) + (dy * dy)) / Center;
                float v = d >= 1f ? 0f : (1f - d) * (1f - d);
                byte b = (byte)MathF.Round(v * 255f);

                texels[(y * Size) + x] = new Color(b, b, b, b);
            }
        }

        texture.SetData(texels);

        return texture;
    }

    private static Texture2D LoadDefaultFontPage(GraphicsDevice device)
    {
        using Stream resource = typeof(FrameRenderer).Assembly.GetManifestResourceStream(DefaultFontPageResource)
            ?? throw new InvalidOperationException($"The embedded default font page '{DefaultFontPageResource}' is missing.");

        return Texture2D.FromStream(device, resource, DefaultColorProcessors.PremultiplyAlpha);
    }

    // The cost of the last game frame Draw submitted. Default before the first.
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
            if (view.LitWorld)
            {
                DrawLightMap(view, alpha, world, layout.Span, layout.World, outputWidth, outputHeight);
            }

            DrawWorld(view, alpha, world, layout.Span, layout.World, outputWidth, outputHeight, ScreenPlacement.Identity);
            DrawScreen(view, alpha, layout.OnSurface, outputWidth, outputHeight, view.Sampling);
        }
        else
        {
            RenderTarget2D target = Surface(layout.Surface);

            if (view.LitWorld)
            {
                DrawLightMap(view, alpha, world, layout.Span, layout.World, target.Width, target.Height);
            }

            _device.SetRenderTarget(target);
            DrawWorld(view, alpha, world, layout.Span, layout.World, target.Width, target.Height, layout.Present);

            // Drawn over the world's bars. The viewport is the full surface again and the canvas sits
            // centred in it on whole pixels, so nothing lands on a grid the world did not use.
            if (layout.ScreenOnSurface)
            {
                DrawScreen(view, alpha, layout.OnSurface, target.Width, target.Height, view.Sampling);
            }

            _device.SetRenderTarget(null);
            Present(target, view.Sampling, layout.Present);

            if (!layout.ScreenOnSurface)
            {
                DrawScreen(view, alpha, layout.Layer, outputWidth, outputHeight, view.Sampling);
            }
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

    // Draws a host-owned frame over the game frame already submitted to the back buffer. Its world
    // list comes first, placed where the last game frame's world landed so it annotates that frame's
    // sprites, then its screen layer at an integer scale of the back buffer. The host's own camera is
    // not used here, and the world list is culled against nothing and drawn at the settled step.
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

        // DrawScreen restores the full back buffer itself. A view with an empty screen layer writes
        // nothing here.
        DrawScreen(
            view,
            alpha: 1f,
            new ScreenPlacement(System.Numerics.Vector2.Zero, scale),
            width,
            height,
            TextureSampling.Point);
    }

    // Back-buffer pixels per world unit on the last frame drawn, or zero before one has drawn a
    // world. It sizes a screen-sized glyph placed at a world point.
    internal float WorldPixelsPerUnit => _world is { } world ? world.PixelsPerUnit : 0f;

    // The world's region of the back buffer is the fit inside the surface carried through the
    // surface's placement, and the viewport is narrowed to that region so nothing lands on the bars.
    // With the viewport's origin at the region's corner, the transform maps the world's top-left to
    // the origin and units to back-buffer pixels.
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

        _batcher.Begin(in worldToBackBuffer, SamplerState.PointClamp);

        Pass pass = new()
        {
            Alpha = 1f,
            Snap = true,
            Scale = pixelsPerUnit,
            SnapLines = world.Snap,
            LineScale = world.Fit.Scale,
            LayerCorner = world.TopLeft,
            FrameCorner = world.TopLeft,
        };

        DrawIntents(view.Sprites, view.Lines, default, default, ref pass);
        _batcher.End();
    }

    // Where the screen layer lands in the window, from the last frame drawn or from
    // ResolveScreenLayer before the first one.
    internal ScreenPlacement ScreenLayer => _placement;

    // Settles ScreenLayer for view at the back buffer's current extent, drawing nothing. The host
    // samples the mouse before the first frame, and an unplaced layer would hand the simulation
    // window pixels as canvas pixels for that frame.
    internal void ResolveScreenLayer(FrameView view)
    {
        PresentationParameters backBuffer = _device.PresentationParameters;
        ScreenLayout layout = FrameLayout.Layout(_canvas, view, backBuffer.BackBufferWidth, backBuffer.BackBufferHeight);

        if (layout.Layer.Scale > 0f)
        {
            _placement = layout.Layer;
        }
    }

    // Modulates the world's colour by twice the light map: source * destination + destination * source.
    // The map is drawn at half scale (an ambient of white is stored as 128), so its 8 bits span a light of
    // zero to two, and a light on a white ambient brightens the world towards white as Godot's Add lights
    // do. Both factors stay inside [0, 1], where every desktop API blends exactly.
    private static readonly BlendState Modulate2x = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.SourceColor,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
    };

    // Gets or (re)allocates the light map at width x height, disposing a stale one as Surface does.
    private RenderTarget2D LightMap(int width, int height)
    {
        if (_lightMap is { } map && map.Width == width && map.Height == height)
        {
            return map;
        }

        _device.SetRenderTarget(null);
        _lightMap?.Dispose();
        _lightMap = new RenderTarget2D(_device, width, height, false, SurfaceFormat.Color, DepthFormat.None);

        return _lightMap;
    }

    // Draws every light and every additive world sprite into the light map, cleared to the scene's
    // ambient colour. Mirrors DrawWorld's preamble, on the light map instead of the world's surface.
    private void DrawLightMap(FrameView view, float alpha, in Rect world, Vector2 span, in Letterbox fit, int surfaceWidth, int surfaceHeight)
    {
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            return;
        }

        RenderTarget2D map = LightMap(surfaceWidth, surfaceHeight);
        _device.SetRenderTarget(map);
        _device.Viewport = new Viewport(0, 0, surfaceWidth, surfaceHeight);
        ColorRgba ambient = Scaled(view.Ambient, MapScale);
        _device.Clear(Color.FromNonPremultiplied(ambient.R, ambient.G, ambient.B, view.Ambient.A));

        if (world.IsEmpty || fit.IsEmpty)
        {
            _device.SetRenderTarget(null);
            return;
        }

        _device.Viewport = new Viewport(fit.X, fit.Y, fit.Width, fit.Height);

        Vector2 topLeft = new(world.Left, world.Top);
        bool snap = view.Sampling == TextureSampling.Point;

        Matrix worldToScreen =
            Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0f) *
            Matrix.CreateScale(fit.Scale, fit.Scale, 1f);

        Pass pass = new()
        {
            Alpha = alpha,
            Snap = snap,
            Scale = fit.Scale,
            SnapLines = snap,
            LineScale = fit.Scale,
            LayerCorner = topLeft,
            FrameCorner = topLeft,
        };

        // The premultiplied state, not BlendState.Additive: that one scales the source by its alpha, and
        // an additive intent packs alpha zero (D-capsule-109), which under One/InverseSourceAlpha is
        // exactly One/One. Every quad here is additive.
        _batcher.Begin(in worldToScreen, Sampler(view.Sampling), BlendState.AlphaBlend);

        DrawLights(view.Lights, view.ParallaxLayers, view.Camera.ScrollOrigin, ref pass);
        DrawAdditiveSprites(view.Sprites, view.ParallaxLayers, view.Camera.ScrollOrigin, ref pass);

        _batcher.End();
        _device.SetRenderTarget(null);
    }

    private void DrawLights(ReadOnlySpan<LightIntent> lights, ReadOnlySpan<ParallaxLayer> layers, Vector2 scrollOrigin, ref Pass pass)
    {
        Vector2 frameCorner = pass.FrameCorner;
        int next = 0;
        for (int index = 0; index < lights.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstLight <= index)
            {
                pass.LayerCorner = ScrollLayout.Corner(frameCorner, scrollOrigin, layers[next].ScrollFactor);
                next++;
            }

            LightIntent light = lights[index];
            if (!(light.Intensity > 0f))
            {
                continue;
            }

            int quads = Math.Min(16, (int)MathF.Ceiling(light.Intensity));
            for (int quad = 0; quad < quads; quad++)
            {
                bool last = quad == quads - 1;
                float fraction = light.Intensity - quad;
                ColorRgba color = Scaled(light.Color, last && fraction < 1f ? MapScale * fraction : MapScale);
                SpriteIntent intent = light.ToSprite(color);
                DrawSprite(in intent, ref pass);
            }
        }

        pass.LayerCorner = frameCorner;
    }

    private void DrawAdditiveSprites(ReadOnlySpan<SpriteIntent> sprites, ReadOnlySpan<ParallaxLayer> layers, Vector2 scrollOrigin, ref Pass pass)
    {
        Vector2 frameCorner = pass.FrameCorner;
        int next = 0;
        for (int index = 0; index < sprites.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstSprite <= index)
            {
                pass.LayerCorner = ScrollLayout.Corner(frameCorner, scrollOrigin, layers[next].ScrollFactor);
                next++;
            }

            if (sprites[index].Blend == BlendMode.Additive)
            {
                SpriteIntent halved = sprites[index] with { Color = Scaled(sprites[index].Color, MapScale) };
                DrawSprite(in halved, ref pass);
            }
        }

        pass.LayerCorner = frameCorner;
    }

    // Scales a colour's RGB by fraction, for a light's fractional last quad. Additive accumulation
    // saturates and is order-free, so this stays exact and deterministic.
    // The light map's scale: a light of one is stored as half of full, so the map holds zero to two.
    private const float MapScale = 0.5f;

    private static ColorRgba Scaled(ColorRgba color, float fraction) => new(
        (byte)Math.Clamp(MathF.Round(color.R * fraction), 0f, 255f),
        (byte)Math.Clamp(MathF.Round(color.G * fraction), 0f, 255f),
        (byte)Math.Clamp(MathF.Round(color.B * fraction), 0f, 255f),
        color.A);

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

    // Whether this frame drew. A capture request stands until one does.
    internal bool CanCaptureFrame => FrameCapture.CanCapture(_device);

    // Saves the surface the world was drawn on as a PNG at path. See FrameCapture.
    internal void SaveSurface(string path) => FrameCapture.Save(_device, _target, path);

    // surfaceWidth and surfaceHeight are the bound surface's extent, which the viewport no longer
    // reports once narrowed to the letterbox. present is where that surface lands in the back buffer,
    // identity when the surface is the back buffer.
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

        // The camera interpolates on the same clock as what it looks at. Snapping it to the step's
        // end would slide the world back once per step. The corner itself is not snapped, because
        // each sprite snaps to the grid anchored here and stays a whole number of pixels from the
        // corner, and a followed sprite does not crawl between two roundings. The simulation
        // keeps its fractional positions.
        Vector2 topLeft = new(world.Left, world.Top);
        bool snap = view.Sampling == TextureSampling.Point;

        if (present.Scale > 0f)
        {
            _world = new WorldPlacement(topLeft, fit, present, snap);
        }

        Matrix worldToScreen =
            Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0f) *
            Matrix.CreateScale(fit.Scale, fit.Scale, 1f);

        _batcher.Begin(in worldToScreen, Sampler(view.Sampling));

        // Drawn through the narrowed world viewport, so presentation bars stay black.
        _batcher.DrawWhole(_white, topLeft, Vector2.Zero, span, rotation: 0f, view.ClearColor);

        Pass pass = new()
        {
            Alpha = alpha,
            Snap = snap,
            Scale = fit.Scale,
            SnapLines = snap,
            LineScale = fit.Scale,
            LayerCorner = topLeft,
            FrameCorner = topLeft,
        };

        DrawIntents(view.Sprites, view.Lines, view.ParallaxLayers, view.Camera.ScrollOrigin, ref pass);
        _batcher.End();

        if (view.LitWorld && _lightMap is { } map)
        {
            _device.Viewport = new Viewport(0, 0, surfaceWidth, surfaceHeight);
            _batcher.Begin(Matrix.Identity, SamplerState.PointClamp, Modulate2x);
            _batcher.DrawWhole(map, Vector2.Zero, Vector2.Zero, Vector2.One, rotation: 0f, ColorRgba.White);
            _batcher.End();
        }
    }

    // The screen layer, in canvas pixels placed by placement. Drawn after the world and across the
    // full surface, so it covers the bars the world's fit left.
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

        _batcher.Begin(in canvasToSurface, Sampler(sampling));

        bool snap = sampling == TextureSampling.Point;
        Pass pass = new()
        {
            Alpha = alpha,
            Snap = snap,
            Scale = placement.Scale,
            SnapLines = snap,
            LineScale = placement.Scale,
        };

        DrawIntents(sprites, lines, default, default, ref pass);
        _batcher.End();
    }

    // Draws the sprites, then the lines, each scrolled run from its own layer's corner, formed as the
    // run opens. The runs are in list order, and a single cursor walks them.
    private void DrawIntents(
        ReadOnlySpan<SpriteIntent> sprites,
        ReadOnlySpan<LineIntent> lines,
        ReadOnlySpan<ParallaxLayer> layers,
        Vector2 scrollOrigin,
        ref Pass pass)
    {
        Vector2 frameCorner = pass.FrameCorner;
        int next = 0;
        for (int index = 0; index < sprites.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstSprite <= index)
            {
                pass.LayerCorner = ScrollLayout.Corner(frameCorner, scrollOrigin, layers[next].ScrollFactor);
                next++;
            }

            DrawSprite(in sprites[index], ref pass);
        }

        pass.LayerCorner = frameCorner;
        next = 0;
        for (int index = 0; index < lines.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstLine <= index)
            {
                pass.LayerCorner = ScrollLayout.Corner(frameCorner, scrollOrigin, layers[next].ScrollFactor);
                next++;
            }

            DrawLine(in lines[index], in pass);
        }
    }

    // The white texel stretched to the segment's length and thickness and turned along it, from the
    // middle of its left edge on A. A hairline is one pixel of the surface being drawn, since a
    // thinner quad would miss the pixel centres and not rasterise.
    private void DrawLine(in LineIntent line, in Pass pass)
    {
        Vector2 a = ScrollLayout.Place(line.A, pass.LayerCorner, pass.FrameCorner, pass.SnapLines, pass.LineScale);
        Vector2 b = ScrollLayout.Place(line.B, pass.LayerCorner, pass.FrameCorner, pass.SnapLines, pass.LineScale);

        Vector2 delta = b - a;
        float length = delta.Length();

        float thickness = line.Thickness > 0f ? line.Thickness : 1f / pass.Scale;

        _batcher.DrawWhole(
            _white,
            a,
            new Vector2(0f, 0.5f),
            new Vector2(length, thickness),
            MathF.Atan2(delta.Y, delta.X),
            line.Color);
    }

    // What one pass of intents is drawn with. LayerCorner is the camera corner the current scrolled
    // run is drawn by, and FrameCorner the corner of the rect the frame draws, the same point outside
    // a scrolled run and the canvas's origin on a screen pass. The pixel grid is anchored at
    // FrameCorner. Resolved is the handle Slice was fetched for, carried across the stream, and a run
    // of sprites on one texture resolves it once.
    private struct Pass
    {
        internal float Alpha;

        // Whether a sprite quantises to whole pixels, and the pixels per world unit of the surface
        // being drawn. The scale is both that grid and a hairline's width.
        internal bool Snap;
        internal float Scale;

        // The same two for a line. On the overlay pass a line quantises on the game surface's grid,
        // not the back buffer's, so it lands on the sprite it outlines.
        internal bool SnapLines;
        internal float LineScale;

        internal Vector2 LayerCorner;
        internal Vector2 FrameCorner;
        internal TextureHandle Resolved;
        internal TextureSlice Slice;
    }

    private void DrawSprite(in SpriteIntent sprite, ref Pass pass)
    {
        if (pass.Slice.Texture is null || sprite.Sprite.Texture != pass.Resolved)
        {
            pass.Resolved = sprite.Sprite.Texture;
            pass.Slice = pass.Resolved.IsEngineOwned
                ? new TextureSlice(EngineTexture(pass.Resolved), 0, 0)
                : _textures.Get(pass.Resolved);
        }

        TextureSlice slice = pass.Slice;
        Vector2 position = ScrollLayout.Place(
            StepInterpolation.Interpolate(sprite.PreviousPosition, sprite.Position, pass.Alpha),
            pass.LayerCorner,
            pass.FrameCorner,
            pass.Snap,
            pass.Scale);

        TextureRegion region = sprite.Sprite.Region;

        // The drawn rect is already placed by the mirrored origin. The flips swap the texture
        // coordinates that fill it.
        _batcher.Draw(
            slice.Texture,
            position,
            sprite.DrawOrigin,
            new Vector2(sprite.Size.X / region.Width, sprite.Size.Y / region.Height),
            in region,
            slice.OffsetX,
            slice.OffsetY,
            StepInterpolation.Interpolate(sprite.PreviousRotation, sprite.Rotation, pass.Alpha),
            sprite.FlipX,
            sprite.FlipY,
            sprite.Color,
            sprite.Blend);
    }

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

        _batcher.Begin(Matrix.Identity, Sampler(sampling));
        _batcher.DrawWhole(target, placement.Origin, Vector2.Zero, new Vector2(placement.Scale), rotation: 0f, ColorRgba.White);
        _batcher.End();
    }

    private static SamplerState Sampler(TextureSampling sampling) => sampling switch
    {
        TextureSampling.Linear => SamplerState.LinearClamp,
        TextureSampling.Point => SamplerState.PointClamp,
        _ => throw new ArgumentOutOfRangeException(nameof(sampling), sampling, "Unknown texture sampling mode."),
    };

    // TopLeft is the world rect's corner, where the frame's pixel grid is anchored. Fit is where that
    // rect landed on the surface, Present where the surface landed in the back buffer, and Snap
    // whether the frame quantised to the surface's pixel grid.
    private readonly record struct WorldPlacement(Vector2 TopLeft, Letterbox Fit, ScreenPlacement Present, bool Snap)
    {
        internal float PixelsPerUnit => Fit.Scale * Present.Scale;
    }

    public void Dispose()
    {
        _batcher.Dispose();
        foreach (Texture2D texture in _engineTextures.Values)
        {
            texture.Dispose();
        }

        _target?.Dispose();
        _lightMap?.Dispose();
    }
}
