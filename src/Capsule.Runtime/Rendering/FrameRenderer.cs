using System.Diagnostics;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;

namespace Capsule.Runtime.Rendering;

// Draws a FrameView. Holds no scene state of its own, since what a sprite interpolates from travels
// in the sprite. A frame is recorded whole, then uploaded in one copy pass ahead of its render passes,
// into a command buffer that Submit hands to the device once the capture and the overlay have joined
// it.
internal sealed partial class FrameRenderer : IDisposable
{
    // The format of every surface the renderer draws into before the window.
    internal const GpuTextureFormat SurfaceFormat = GpuTextureFormat.R8G8B8A8Unorm;

    private const string DefaultFontPageResource = "Capsule.Runtime.Assets.default-font.png";

    // The light map's scale: a light of one is stored as half of full. The map holds zero to two.
    private const float MapScale = 0.5f;

    private readonly GpuDevice _device;
    private readonly SpriteDrawList _sprites;
    private readonly ShaderStore _shaders;
    private readonly TextureStore _textures;

    // One white texel, tinted and stretched across the camera to draw the clear colour.
    private readonly SampledTexture _white;

    // One transparent texel, which a material texture never set samples.
    private readonly SampledTexture _unset;

    // Every engine-owned texture. The draw path resolves a handle through a single table.
    private readonly Dictionary<TextureHandle, SampledTexture> _engineTextures;

    // The declared canvas, or null when the world rasterises straight into the back buffer at
    // whatever size the window is.
    private readonly (int Width, int Height)? _canvas;

    // Null when no canvas is declared. Its extent is the canvas under Letterbox and grows with the
    // resolved world rect under a fit that reveals more of it.
    private SampledTexture? _target;

    // The frame's light map, allocated at the surface's size on the first frame that lights and
    // reallocated when that size changes. Null while no frame has lit.
    private SampledTexture? _lightMap;

    // A point-sampled surface blown up to the whole scale above a fractional present, allocated on the
    // first such present and reallocated when its size changes. Null until then.
    private SampledTexture? _prescaled;

    // Where the screen layer landed on the last frame drawn. It turns a sampled mouse position back
    // into a canvas position, and ResolveScreenLayer seeds it before the first frame.
    private ScreenPlacement _placement = ScreenPlacement.Identity;

    // Where the last frame's world landed in the back buffer, which places a host-owned
    // world-anchored draw over that frame. Null until a frame has drawn a world.
    private WorldPlacement? _world;

    // The frame's command buffer from Draw to Submit.
    private GpuCommandBuffer? _commands;

    // A capture of Draw's frame, written once its command buffer completes.
    private (GpuReadback Readback, string Path)? _capture;

    // Wall-clock milliseconds the last game frame's recording and submission took, excluding the
    // present and its vsync wait. Zero before the first.
    internal double LastFrameMs { get; private set; }

    // How many times wider than the game's view the host's view of the world spans, defaulting to 1.
    // Past 1, a render surface is drawn larger by a whole factor that keeps its present whole. The world
    // and the screen layer land on the same back-buffer pixels at any factor.
    internal float WorldZoomOut { get; set; } = 1f;

    // renderResolution: A fixed render surface, or null to draw into the back buffer.
    //
    // textures: The scene texture cache, loading on first use. The caller owns it.
    //
    // shaders: The shaders sprites draw with. The caller owns it, and this renderer resolves the
    // textures its materials bind.
    internal FrameRenderer(GpuDevice device, TextureSamplers samplers, (int Width, int Height)? renderResolution, TextureStore textures, ShaderStore shaders)
    {
        _device = device;
        _sprites = new SpriteDrawList(device, shaders, samplers);
        _shaders = shaders;
        _textures = textures;
        shaders.WholeTexture = WholeTexture;
        _white = textures.Create(new DecodedTexture([255, 255, 255, 255], 1, 1));
        _unset = textures.Create(new DecodedTexture([0, 0, 0, 0], 1, 1));
        _engineTextures = new Dictionary<TextureHandle, SampledTexture>
        {
            [TextureHandle.White] = _white,
            [TextureHandle.Light] = textures.Create(LightTexels()),
            [TextureHandle.DefaultFontPage] = textures.Create(DefaultFontPage()),
        };
        _canvas = renderResolution;

        if (renderResolution is { } resolution)
        {
            _target = Surface(resolution.Width, resolution.Height);
        }
    }

    internal (int Width, int Height) BackBufferSize => _device.SwapchainSize;

    // Where the screen layer lands in the window, from the last frame drawn or from
    // ResolveScreenLayer before the first one.
    internal ScreenPlacement ScreenLayer => _placement;

    // Back-buffer pixels per world unit on the last frame drawn, or zero before one has drawn a
    // world. It sizes a screen-sized glyph placed at a world point.
    internal float WorldPixelsPerUnit => _world is { } world ? world.PixelsPerUnit : 0f;

    // Where the last frame's world landed in the back buffer, or null before one has drawn a world.
    internal WorldPlacement? WorldPlacement => _world;

    // Whether this frame drew. A window with no area, as a minimised one has, presents nothing and
    // leaves no frame to save, including behind a render target, which is drawn but not presented. A
    // capture request stands until one does.
    internal bool CanCaptureFrame => BackBufferSize is ( > 0, > 0);

    // Draws one frame into a new command buffer, which Submit submits. Allocation-free at steady state.
    //
    // alpha: Fraction of a fixed step not yet simulated, clamped to [0, 1]. Each sprite, and the
    // camera looking at it, is drawn that far from its previous position towards its current one.
    internal void Draw(FrameView view, float alpha)
    {
        long started = Stopwatch.GetTimestamp();
        _shaders.Globals = view.ShaderGlobals;

        // The scheduler leaves a whole step in the accumulator when a game exits mid-catch-up.
        alpha = Math.Clamp(alpha, 0f, 1f);
        _sprites.Reset(alpha);

        (int outputWidth, int outputHeight) = BackBufferSize;

        CameraView camera = view.Camera.At(alpha);
        ScreenLayout layout = FrameLayout.Layout(_canvas, camera, view.Canvas, outputWidth, outputHeight);
        Rect world = camera.Place(1f, layout.Span);

        if (_canvas is null)
        {
            if (view.LitWorld)
            {
                DrawLightMap(view, alpha, world, layout.Span, layout.World, outputWidth, outputHeight);
            }

            DrawWorld(view, alpha, null, world, layout.Span, layout.World, outputWidth, outputHeight, ScreenPlacement.Identity, layout.OnSurface, surfaceFactor: 1);
            DrawScreen(view, alpha, layout.OnSurface, outputWidth, outputHeight, view.Sampling, view.ScreenMaterialRuns, view.ScreenClipRuns, view.ClipLevels, surfaceFactor: 1);
        }
        else
        {
            int limit = FrameLayout.SurfaceFactorLimit(view.Sampling, layout.Present.Scale);
            int factor = FrameLayout.SurfaceFactor(WorldZoomOut, limit);
            ScreenLayout drawn = FrameLayout.Enlarge(layout, factor);
            SampledTexture target = Sized(ref _target, drawn.Surface.Width, drawn.Surface.Height);
            int width = target.Texture.Width;
            int height = target.Texture.Height;

            if (view.LitWorld)
            {
                DrawLightMap(view, alpha, world, layout.Span, drawn.World, width, height);
            }

            DrawWorld(view, alpha, target.Texture, world, layout.Span, drawn.World, width, height, drawn.Present, drawn.OnSurface, factor);

            // Drawn over the world's bars. The viewport is the full surface again and the canvas sits
            // centred in it on whole pixels. Nothing lands on a grid the world did not use.
            if (layout.ScreenOnSurface)
            {
                DrawScreen(view, alpha, drawn.OnSurface, width, height, view.Sampling, view.ScreenMaterialRuns, view.ScreenClipRuns, view.ClipLevels, factor);
            }

            Present(target, view.Sampling, layout.Present, factor);

            if (!layout.ScreenOnSurface)
            {
                DrawScreen(view, alpha, layout.Layer, outputWidth, outputHeight, view.Sampling, view.ScreenMaterialRuns, view.ScreenClipRuns, view.ClipLevels, surfaceFactor: 1);
            }
        }

        if (layout.Layer.Scale > 0f)
        {
            _placement = layout.Layer;
        }

        GpuCommandBuffer commands = _device.AcquireCommandBuffer();
        _commands = commands;
        GpuCopyPass copy = commands.BeginCopyPass();
        _textures.Pump(copy);
        _sprites.Upload(copy);
        copy.End();
        _sprites.Execute(commands);

        LastFrameMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    // Submits the frame Draw recorded, with the capture and the overlay that joined it, then writes the
    // capture once the frame completes.
    internal void Submit()
    {
        if (_commands is not { } commands)
        {
            return;
        }

        commands.Submit();
        _commands = null;

        if (_capture is { } capture)
        {
            _capture = null;
            WriteCapture(capture.Readback, capture.Path);
        }
    }

    // Apart from Submit, whose every call would otherwise allocate the lambda's closure.
    private void WriteCapture(GpuReadback readback, string path) => FrameCapture.Save(() => _device.EncodePng(readback), path);

    // Settles ScreenLayer for view at the back buffer's current extent, drawing nothing. The host
    // samples the mouse before the first frame, and an unplaced layer would hand the simulation
    // window pixels as canvas pixels for that frame.
    internal void ResolveScreenLayer(FrameView view)
    {
        (int width, int height) = BackBufferSize;
        ScreenLayout layout = FrameLayout.Layout(_canvas, view.Camera, view.Canvas, width, height);

        if (layout.Layer.Scale > 0f)
        {
            _placement = layout.Layer;
        }
    }

    // Draws every light and every additive world sprite into the light map, cleared to the scene's
    // ambient colour. Mirrors DrawWorld's preamble, on the light map instead of the world's surface.
    private void DrawLightMap(FrameView view, float alpha, in Rect world, Vector2 span, in Letterbox fit, int surfaceWidth, int surfaceHeight)
    {
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            return;
        }

        SampledTexture map = Sized(ref _lightMap, surfaceWidth, surfaceHeight);
        _sprites.BeginPass(map.Texture, Scaled(view.Ambient, MapScale));

        if (world.IsEmpty || fit.IsEmpty)
        {
            return;
        }

        _sprites.SetViewport(fit.X, fit.Y, fit.Width, fit.Height);

        Vector2 topLeft = new(world.Left, world.Top);
        bool snap = view.Sampling == TextureSampling.Point;

        Pass pass = WorldPass(alpha, snap, fit.Scale, topLeft);

        // Every quad here is additive, drawn with the pass's premultiplied blend: an additive intent packs
        // alpha zero, and under One/OneMinusSourceAlpha that is exactly One/One. An additive blend would
        // scale the source by its alpha.
        _sprites.SetTransform(WorldToSurface(topLeft, fit.Scale));
        _sprites.SetSampling(view.Sampling);

        Vector2 parallax = ScrollLayout.Parallax(topLeft, span, view.Camera.ScrollCenter);
        DrawLights(view.Lights, view.PlacementRuns, parallax, ref pass);
        DrawAdditiveSprites(view.Sprites, view.PlacementRuns, parallax, ref pass);
    }

    // A light is always stored under a run of the world's own, never a canvas run.
    private void DrawLights(ReadOnlySpan<LightIntent> lights, ReadOnlySpan<PlacementRun> layers, Vector2 parallax, ref Pass pass)
    {
        Vector2 frameCorner = pass.FrameCorner;
        int next = 0;
        for (int index = 0; index < lights.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstLight <= index)
            {
                pass.LayerCorner = ScrollLayout.Corner(frameCorner, parallax, layers[next].ScrollFactor);
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

    // A screen root sorted with the world is lit with it but lights nothing, and its canvas runs are skipped.
    private void DrawAdditiveSprites(ReadOnlySpan<SpriteIntent> sprites, ReadOnlySpan<PlacementRun> layers, Vector2 parallax, ref Pass pass)
    {
        Vector2 frameCorner = pass.FrameCorner;
        int next = 0;
        bool canvas = false;
        for (int index = 0; index < sprites.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstSprite <= index)
            {
                pass.LayerCorner = ScrollLayout.Corner(frameCorner, parallax, layers[next].ScrollFactor);
                canvas = layers[next].Canvas;
                next++;
            }

            if (!canvas && sprites[index].Blend == BlendMode.Additive)
            {
                // The flash's colour halves with the tint and its amount stays. The map holds half of
                // exactly what the world draws.
                SpriteIntent halved = sprites[index] with
                {
                    Color = Scaled(sprites[index].Color, MapScale),
                    Flash = Scaled(sprites[index].Flash, MapScale),
                };
                DrawSprite(in halved, ref pass);
            }
        }

        pass.LayerCorner = frameCorner;
    }

    // Scales a colour's RGB by fraction. Additive accumulation saturates and is order-free. This
    // stays exact and deterministic.
    private static ColorRgba Scaled(ColorRgba color, float fraction) => new(
        (byte)Math.Clamp(MathF.Round(color.R * fraction), 0f, 255f),
        (byte)Math.Clamp(MathF.Round(color.G * fraction), 0f, 255f),
        (byte)Math.Clamp(MathF.Round(color.B * fraction), 0f, 255f),
        color.A);

    // Returns target at width x height, allocating it on first use and reallocating it when its size
    // differs.
    private SampledTexture Sized(ref SampledTexture? target, int width, int height)
    {
        if (target is { } current && current.Texture.Width == width && current.Texture.Height == height)
        {
            return current;
        }

        target?.Dispose();
        target = Surface(width, height);

        return target;
    }

    private SampledTexture Surface(int width, int height) =>
        new(_device.CreateTexture(new GpuTextureInfo(SurfaceFormat, GpuTextureUsage.Sampler | GpuTextureUsage.ColorTarget, width, height)));

    // Saves the surface the world was drawn on as a PNG at path, once Submit has run the frame. Called
    // after Draw, ahead of the overlay. The surface is the canvas where a render resolution is
    // configured, whose extent is independent of the window, and the back buffer where there is none.
    internal void SaveSurface(string path)
    {
        if (_commands is not { } commands || (_target?.Texture ?? commands.AcquireSwapchainTexture()) is not { } surface)
        {
            return;
        }

        try
        {
            GpuReadback readback = _device.CreateReadback(surface.Width * surface.Height * 4);
            GpuCopyPass copy = commands.BeginCopyPass();
            copy.DownloadFromTexture(surface, readback);
            copy.End();
            _capture = (readback, path);
        }
        catch (Exception error)
        {
            Log.Warning($"Frame capture to '{path}' failed before writing: {error.Message}");
        }
    }

    // surfaceWidth and surfaceHeight are the bound surface's extent, which the viewport no longer
    // reports once narrowed to the letterbox. present is where that surface lands in the back buffer,
    // identity when the surface is the back buffer.
    private void DrawWorld(
        FrameView view,
        float alpha,
        GpuTexture? target,
        in Rect world,
        Vector2 span,
        in Letterbox fit,
        int surfaceWidth,
        int surfaceHeight,
        in ScreenPlacement present,
        in ScreenPlacement canvas,
        int surfaceFactor)
    {
        _world = null;

        // A minimised window can present a back buffer with no area.
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            return;
        }

        // Cleared at full extent before the viewport narrows. Bars are presentation, not world intent,
        // and stay black.
        _sprites.BeginPass(target, ColorRgba.Black);

        if (world.IsEmpty || fit.IsEmpty)
        {
            return;
        }

        _sprites.SetViewport(fit.X, fit.Y, fit.Width, fit.Height);

        // The camera interpolates on the same clock as what it looks at. Snapping it to the step's
        // end would slide the world back once per step. The corner itself is not snapped, because
        // each sprite snaps to the grid anchored here and stays a whole number of pixels from the
        // corner, and a followed sprite does not crawl between two roundings. The simulation
        // keeps its fractional positions.
        Vector2 topLeft = new(world.Left, world.Top);
        bool snap = view.Sampling == TextureSampling.Point;
        Vector2 parallax = ScrollLayout.Parallax(topLeft, span, view.Camera.ScrollCenter);

        if (present.Scale > 0f)
        {
            _world = new WorldPlacement(topLeft, fit, present, snap, parallax);
        }

        Pass pass = WorldPass(alpha, snap, fit.Scale, topLeft);

        // A canvas run lands where the screen layer would on this surface, through the narrowed viewport
        // the world draws in, on the grid the canvas had at a factor of 1.
        pass.WorldTransform = WorldToSurface(topLeft, fit.Scale);
        pass.CanvasTransform = SpriteDrawList.Multiply(CanvasToSurface(canvas), Matrix4x4.CreateTranslation(-fit.X, -fit.Y, 0f));
        pass.CanvasScale = canvas.Scale / surfaceFactor;
        pass.Placement = canvas;
        pass.SurfaceWidth = surfaceWidth;
        pass.SurfaceHeight = surfaceHeight;

        _sprites.SetTransform(in pass.WorldTransform);
        _sprites.SetSampling(view.Sampling);

        // Drawn through the narrowed world viewport. Presentation bars stay black.
        _sprites.AddWhole(_white, topLeft, Vector2.Zero, span, rotation: 0f, view.ClearColor);

        DrawIntents(view.Sprites, view.Lines, view.PlacementRuns, parallax, view.MaterialRuns, view.ClipRuns, view.ClipLevels, ref pass);

        // The world's colour times twice the light map, which is drawn at half scale (an ambient of
        // white is stored as 128). Its 8 bits span a light of zero to two, and a light on a white ambient
        // brightens the world towards white.
        if (view.LitWorld && _lightMap is { } map)
        {
            _sprites.SetViewport(0, 0, surfaceWidth, surfaceHeight);
            _sprites.SetTransform(Matrix4x4.Identity);
            _sprites.SetSampling(TextureSampling.Point);
            _sprites.SetBlend(ShaderStore.Modulate2x);
            _sprites.AddWhole(map, Vector2.Zero, Vector2.Zero, Vector2.One, rotation: 0f, ColorRgba.White);
            _sprites.SetBlend(ShaderStore.Premultiplied);
        }
    }

    // The screen layer, in canvas pixels placed by placement. Drawn after the world and across the
    // full surface. It covers the bars the world's fit left. On a surface drawn surfaceFactor times
    // larger it snaps to the grid it had at a factor of 1, and its pixels are those pixels enlarged.
    private void DrawScreen(
        FrameView view,
        float alpha,
        in ScreenPlacement placement,
        int surfaceWidth,
        int surfaceHeight,
        TextureSampling sampling,
        ReadOnlySpan<MaterialRun> materials,
        ReadOnlySpan<ClipRun> clips,
        ReadOnlySpan<ClipRegion> clipLevels,
        int surfaceFactor)
    {
        ReadOnlySpan<SpriteIntent> sprites = view.ScreenSprites;
        ReadOnlySpan<LineIntent> lines = view.ScreenLines;
        if ((sprites.IsEmpty && lines.IsEmpty) || surfaceWidth <= 0 || surfaceHeight <= 0 || !(placement.Scale > 0f))
        {
            return;
        }

        _sprites.SetViewport(0, 0, surfaceWidth, surfaceHeight);
        _sprites.SetTransform(CanvasToSurface(placement));
        _sprites.SetSampling(sampling);

        bool snap = sampling == TextureSampling.Point;
        float grid = placement.Scale / surfaceFactor;
        Pass pass = new()
        {
            Alpha = alpha,
            Snap = snap,
            Scale = grid,
            SnapLines = snap,
            LineScale = grid,
            Placement = placement,
            SurfaceWidth = surfaceWidth,
            SurfaceHeight = surfaceHeight,
        };

        DrawIntents(sprites, lines, default, default, materials, clips, clipLevels, ref pass);
    }

    // Draws the sprites, then the lines, each placement run as it opens, each material run with its
    // material, and each clip run cut to its rect. The runs are in list order, and one cursor walks each
    // kind. A placement or clip run opens at both a sprite and a line index, and the lines walk them again
    // from the start. Lines draw with Capsule's own shader. The quads after it draw with no material or
    // clip.
    private void DrawIntents(
        ReadOnlySpan<SpriteIntent> sprites,
        ReadOnlySpan<LineIntent> lines,
        ReadOnlySpan<PlacementRun> layers,
        Vector2 parallax,
        ReadOnlySpan<MaterialRun> materials,
        ReadOnlySpan<ClipRun> clips,
        ReadOnlySpan<ClipRegion> clipLevels,
        ref Pass pass)
    {
        int next = 0;
        int nextMaterial = 0;
        int nextClip = 0;
        bool hidden = false;
        for (int index = 0; index < sprites.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstSprite <= index)
            {
                Place(in layers[next], parallax, ref pass);
                next++;
            }

            while (nextMaterial < materials.Length && materials[nextMaterial].FirstSprite <= index)
            {
                _sprites.SetMaterial(materials[nextMaterial].Material);
                nextMaterial++;
            }

            while (nextClip < clips.Length && clips[nextClip].FirstSprite <= index)
            {
                hidden = ApplyClip(clips[nextClip], clipLevels, in pass);
                nextClip++;
            }

            if (!hidden)
            {
                DrawSprite(in sprites[index], ref pass);
            }
        }

        _sprites.SetMaterial(null);
        _sprites.ClearClip();
        Place(new PlacementRun(0, 0, Vector2.One), parallax, ref pass);
        next = 0;
        nextClip = 0;
        hidden = false;
        for (int index = 0; index < lines.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstLine <= index)
            {
                Place(in layers[next], parallax, ref pass);
                next++;
            }

            while (nextClip < clips.Length && clips[nextClip].FirstLine <= index)
            {
                hidden = ApplyClip(clips[nextClip], clipLevels, in pass);
                nextClip++;
            }

            if (!hidden)
            {
                DrawLine(in lines[index], in pass);
            }
        }

        _sprites.ClearClip();
    }

    // Moves the pass onto run's placement: a scroll factor's corner on the world's grid, or canvas pixels on
    // the canvas's grid. Only a world pass holds a canvas run, and it swaps the draw list's transform to reach it.
    private void Place(in PlacementRun run, Vector2 parallax, ref Pass pass)
    {
        if (run.Canvas != pass.OnCanvas)
        {
            pass.OnCanvas = run.Canvas;
            _sprites.SetTransform(run.Canvas ? pass.CanvasTransform : pass.WorldTransform);
            pass.Scale = pass.LineScale = run.Canvas ? pass.CanvasScale : pass.WorldScale;
            pass.FrameCorner = run.Canvas ? Vector2.Zero : pass.WorldCorner;
        }

        pass.LayerCorner = run.Canvas ? Vector2.Zero : ScrollLayout.Corner(pass.FrameCorner, parallax, run.ScrollFactor);
    }

    // Cuts the quads drawn next to the run's clip, each level moved by alpha between its two ends, intersected, and
    // placed on the surface. Returns true when nothing of the surface is left to draw on.
    private bool ApplyClip(in ClipRun run, ReadOnlySpan<ClipRegion> clipLevels, in Pass pass)
    {
        if (run.Levels == 0)
        {
            _sprites.ClearClip();

            return false;
        }

        Rect shown = ClipScissor.Shown(clipLevels.Slice(run.FirstLevel, run.Levels), pass.Alpha);
        if (!ClipScissor.TryMap(in shown, in pass.Placement, pass.SurfaceWidth, pass.SurfaceHeight, out int x, out int y, out int width, out int height))
        {
            return true;
        }

        _sprites.SetClip(x, y, width, height);

        return false;
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

        _sprites.AddWhole(
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
        // not the back buffer's. It lands on the sprite it outlines.
        internal bool SnapLines;
        internal float LineScale;

        internal Vector2 LayerCorner;
        internal Vector2 FrameCorner;

        // Where the canvas lands on the surface, and the surface's extent, which a clip is placed by.
        internal ScreenPlacement Placement;
        internal int SurfaceWidth;
        internal int SurfaceHeight;

        // On a world pass: the world's corner, scale and transform, the canvas's grid and transform for a
        // screen root sorted with the world, and whether the current run is that root's.
        internal Vector2 WorldCorner;
        internal float WorldScale;
        internal Matrix4x4 WorldTransform;
        internal float CanvasScale;
        internal Matrix4x4 CanvasTransform;
        internal bool OnCanvas;

        internal TextureHandle Resolved;
        internal TextureSlice Slice;
    }

    // A pass over a world surface at scale pixels per unit, its grid anchored at corner.
    private static Pass WorldPass(float alpha, bool snap, float scale, Vector2 corner) => new()
    {
        Alpha = alpha,
        Snap = snap,
        Scale = scale,
        SnapLines = snap,
        LineScale = scale,
        LayerCorner = corner,
        FrameCorner = corner,
        WorldCorner = corner,
        WorldScale = scale,
    };

    private void DrawSprite(in SpriteIntent sprite, ref Pass pass)
    {
        if (pass.Slice.Texture is null || sprite.Sprite.Texture != pass.Resolved)
        {
            pass.Resolved = sprite.Sprite.Texture;
            pass.Slice = pass.Resolved.IsEngineOwned
                ? new TextureSlice(EngineTexture(pass.Resolved), 0, 0)
                : _textures.Get(pass.Resolved);
        }

        // A snapped quad puts both edges on the grid, or abutting tiles leave a gap at a fractional
        // scale. The vertex stage snaps a quad at a whole number of quarter turns whole, and any other
        // turn by its pivot alone.
        _sprites.SetPlacement(pass.LayerCorner, pass.FrameCorner, pass.Snap ? pass.Scale : 0f);
        _sprites.Add(pass.Slice.Texture, in sprite, pass.Slice.OffsetX, pass.Slice.OffsetY);
    }

    // A texture a material binds, whole and never through an atlas page, or the transparent texel for
    // the default handle of one it never set.
    private SampledTexture WholeTexture(TextureHandle handle) =>
        handle.Name is null ? _unset
        : handle.IsEngineOwned ? EngineTexture(handle)
        : _textures.GetWhole(handle);

    private SampledTexture EngineTexture(in TextureHandle handle) =>
        _engineTextures.TryGetValue(handle, out SampledTexture? texture)
            ? texture
            : throw new ArgumentException($"Unknown engine-owned texture handle '{handle.Name}'.", nameof(handle));

    // Letterboxed a second time, into the back buffer, at the surface's present placement. See
    // FrameLayout.PresentPass for how a point-sampled surface reaches a fractional scale. placement is
    // the layout's own, and factor how many times larger than it the surface was drawn.
    private void Present(SampledTexture target, TextureSampling sampling, in ScreenPlacement placement, int factor)
    {
        (int width, int height) = BackBufferSize;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        SampledTexture source = target;
        (int prescale, TextureSampling final, float scale) = FrameLayout.PresentPass(sampling, placement.Scale, factor);

        if (prescale > 1)
        {
            SampledTexture prescaled = Sized(ref _prescaled, target.Texture.Width * prescale, target.Texture.Height * prescale);
            _sprites.BeginPass(prescaled.Texture, null);
            _sprites.AddWhole(target, Vector2.Zero, Vector2.Zero, new Vector2(prescale), rotation: 0f, ColorRgba.White);

            source = prescaled;
        }

        _sprites.BeginPass(null, ColorRgba.Black);

        if (!(scale > 0f))
        {
            return;
        }

        _sprites.SetSampling(final);
        _sprites.AddWhole(source, placement.Origin, Vector2.Zero, new Vector2(scale), rotation: 0f, ColorRgba.White);
    }

    // The engine's radial light: full at the centre, falling to nothing at the edge on a squared
    // falloff. Computed once. The engine ships no PNG and no byte table for it.
    private static DecodedTexture LightTexels()
    {
        const int Size = 128;
        const float Center = 64f;

        byte[] texels = new byte[Size * Size * 4];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) - Center;
                float dy = (y + 0.5f) - Center;
                float d = MathF.Sqrt((dx * dx) + (dy * dy)) / Center;
                float v = d >= 1f ? 0f : (1f - d) * (1f - d);
                byte b = (byte)MathF.Round(v * 255f);

                texels.AsSpan(((y * Size) + x) * 4, 4).Fill(b);
            }
        }

        return new DecodedTexture(texels, Size, Size);
    }

    private static DecodedTexture DefaultFontPage()
    {
        using Stream resource = typeof(FrameRenderer).Assembly.GetManifestResourceStream(DefaultFontPageResource)
            ?? throw new InvalidOperationException($"The embedded default font page '{DefaultFontPageResource}' is missing.");

        return TextureDecoder.Decode(resource, new TexelPool(), "default-font");
    }

    // Maps canvas pixels to surface pixels at placement.
    private static Matrix4x4 CanvasToSurface(in ScreenPlacement placement) =>
        SpriteDrawList.Multiply(Matrix4x4.CreateScale(placement.Scale, placement.Scale, 1f), Matrix4x4.CreateTranslation(placement.Origin.X, placement.Origin.Y, 0f));

    // Maps world units from corner to surface pixels at scale per unit.
    private static Matrix4x4 WorldToSurface(Vector2 corner, float scale) =>
        SpriteDrawList.Multiply(Matrix4x4.CreateTranslation(-corner.X, -corner.Y, 0f), Matrix4x4.CreateScale(scale, scale, 1f));

    public void Dispose()
    {
        foreach (SampledTexture texture in _engineTextures.Values)
        {
            texture.Dispose();
        }

        _unset.Dispose();
        _target?.Dispose();
        _lightMap?.Dispose();
        _prescaled?.Dispose();
    }
}
