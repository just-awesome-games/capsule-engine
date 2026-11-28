using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;

namespace Capsule.Runtime.Rendering;

// Draws a FrameView. Holds no scene state of its own, since what a sprite interpolates from travels
// in the sprite. A frame is recorded whole, then uploaded in one copy pass ahead of its render passes,
// into a command buffer that Submit hands to the device once the overlay has joined it. The frame's
// last pass, the window pass, draws straight into the swapchain texture, laid out at that texture's
// extent. A capture reads a surface, never the swapchain: the canvas, or with no canvas a window pass
// drawn offscreen on the capturing frame alone and then shown, ahead of the overlay. A device with no
// window acquires no swapchain texture, and its frames lay out at OffscreenExtent to be captured.
internal sealed partial class FrameRenderer : IDisposable
{
    private const string DefaultFontPageResource = "Capsule.Runtime.Assets.default-font.png";

    // The light's format: a float per channel, which holds a light past white with no ceiling.
    internal const GpuTextureFormat LightFormat = GpuTextureFormat.R16G16B16A16Float;

    // How many handles a frame keeps the slices of.
    private const int RecentSlices = 8;

    private readonly GpuDevice _device;
    // The draw list every walk records onto. The overlay swaps in its own, which leaves the game frame's
    // recording to be reused.
    private SpriteDrawList _sprites;
    private SpriteDrawList _overlaySprites;

    // The chunks of tiles a view that retains them names, kept on the device across frames.
    private readonly TileChunks _tiles = new(checks: Development.IsSupported && TileChunks.ChecksSwitch);
    private readonly ShaderStore _shaders;
    private readonly TextureStore _textures;

    // One white texel, tinted and stretched across the camera to draw the clear colour.
    private readonly SampledTexture _white;

    // One transparent texel, which a material texture never set samples.
    private readonly SampledTexture _unset;

    // Every engine-owned texture. The draw path resolves a handle through a single table.
    private readonly Dictionary<TextureHandle, SampledTexture> _engineTextures;

    // The slices of the handles this frame last changed to, the newest written over the oldest. Sprites
    // that alternate among a few handles look each up once a frame instead of once a change.
    private readonly TextureHandle[] _recentHandles = new TextureHandle[RecentSlices];
    private readonly TextureSlice[] _recentSlices = new TextureSlice[RecentSlices];
    private int _recentCount;

    // The declared canvas, or null when the world rasterises straight into the window pass at
    // whatever size the window is.
    private readonly (int Width, int Height)? _canvas;

    // Null when no canvas is declared. Its extent is the canvas under Letterbox and grows with the
    // resolved world rect under a fit that reveals more of it.
    private SampledTexture? _target;

    // The frame's light, which every lit world sprite is multiplied by at its pixel, allocated at the surface's
    // size on the first frame that lights and reallocated when that size changes. Null while no frame has lit.
    private SampledTexture? _light;

    // A pixel-perfect surface blown up to the whole scale above a fractional present, allocated on the
    // first such present and reallocated when its size changes. Null until then.
    private SampledTexture? _prescaled;

    // The swapchain texture Draw acquired, which the window pass and the overlay draw into, or null when
    // the window shows nothing new this frame.
    private GpuTexture? _window;

    // The offscreen window pass of a capturing or picture-reading frame with no canvas, since nothing reads the
    // swapchain's texture. Allocated on the first such frame and reallocated when the window's size changes.
    private SampledTexture? _offscreen;

    // What a material reads as the picture, at the surface's size, allocated on the first frame that draws
    // one and reallocated when that size changes. Null while no frame has.
    private SampledTexture? _screen;

    // Whether the warning that a screen material cannot read here has been logged.
    private bool _warnedUnreadable;

    // Where the screen layer landed on the last frame drawn. It turns a sampled mouse position back
    // into a canvas position, and ResolveScreenLayer seeds it before the first frame.
    private ScreenPlacement _placement = ScreenPlacement.Identity;

    // Where the last frame's world landed in the window, which places a host-owned
    // world-anchored draw over that frame. Null until a frame has drawn a world.
    private WorldPlacement? _world;

    // The frame's command buffer from Draw to Submit.
    private GpuCommandBuffer? _commands;

    // The view and build the draw list last recorded, and the output it was laid out for: the extent, whether
    // the swapchain showed a texture and whether a capture drew. A frame that draws the same build on the same
    // output reuses the recording.
    private FrameView? _recordedView;
    private int _recordedBuild;
    private (int Width, int Height, bool Shown, bool Captured) _recordedOutput;

    // A capture of Draw's frame, written once its command buffer completes.
    private (GpuReadback Readback, string Path)? _capture;

    // Whether the frame's submission waits for the GPU, which a capture's readback needs.
    private bool _waitForFrame;

    // Whether a frame is submitted for the device's WaitForFrame to wait on.
    internal bool WatchesFrames { get; set; }

    // Wall-clock milliseconds the last game frame's recording and submission took, excluding the
    // present and its vsync wait. Zero before the first.
    internal double LastFrameMs { get; private set; }

    // How many times wider than the game's view the host's view of the world spans, defaulting to 1.
    // Past 1, a render surface is drawn larger by a whole factor that keeps its present whole. The world
    // and the screen layer land on the same window pixels at any factor.
    internal float WorldZoomOut { get; set; } = 1f;

    // The extent a frame on a device with no window lays out at, as a window's drawable extent would lay
    // it out. Zero until the host sets it, and a frame drawn at zero draws nothing.
    internal (int Width, int Height) OffscreenExtent { get; set; }

    // The captures Submit has written to their files since the renderer was made.
    internal long CapturesSaved { get; private set; }

    // The sprite instances the last frame Draw recorded uploaded, in bytes, its kept chunks of tiles among them.
    internal int UploadedBytes { get; private set; }

    // renderResolution: A fixed render surface, or null to draw the world in the window pass.
    //
    // textures: The scene texture cache, loading on first use. The caller owns it.
    //
    // shaders: The shaders sprites draw with. The caller owns it, and this renderer resolves the
    // textures its materials bind.
    internal FrameRenderer(GpuDevice device, TextureSamplers samplers, (int Width, int Height)? renderResolution, TextureStore textures, ShaderStore shaders)
    {
        _device = device;
        _white = textures.Create(new DecodedTexture([255, 255, 255, 255], 1, 1));
        _sprites = new SpriteDrawList(device, shaders, samplers, _white);
        _overlaySprites = new SpriteDrawList(device, shaders, samplers, _white);
        _shaders = shaders;
        _textures = textures;
        shaders.MaterialTexture = MaterialTexture;
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

    // The window's drawable extent, or OffscreenExtent on a device with no window.
    internal (int Width, int Height) WindowSize => _device.HasWindow ? _device.WindowSize : OffscreenExtent;

    // Where the screen layer lands in the window, from the last frame drawn or from
    // ResolveScreenLayer before the first one.
    internal ScreenPlacement ScreenLayer => _placement;

    // Window pixels per world unit on the last frame drawn, or zero before one has drawn a
    // world. It sizes a screen-sized glyph placed at a world point.
    internal float WorldPixelsPerUnit => _world is { } world ? world.PixelsPerUnit : 0f;

    // Where the last frame's world landed in the window, or null before one has drawn a world.
    internal WorldPlacement? WorldPlacement => _world;

    // Whether this frame drew. A window with no area, as a minimised one has, presents nothing and
    // leaves no frame to save, including behind a render target, which is drawn but not presented. A
    // capture request stands until one does.
    internal bool CanCaptureFrame => WindowSize is ( > 0, > 0);

    // Draws one frame into a new command buffer, which Submit submits. Allocation-free at steady state.
    //
    // alpha: Fraction of a fixed step not yet simulated, clamped to [0, 1]. Each sprite, and the
    // camera looking at it, is drawn that far from its previous position towards its current one. A
    // pixel-perfect frame snaps both ends and carries the whole pixels that far, as WorldEnds describes.
    //
    // capturePath: Where the frame is saved as a PNG once Submit has run it, or null for none. The
    // image is the canvas where a render resolution is configured, whose extent is independent of the
    // window, and the window pass where there is none. The overlay draws after it and never shows.
    internal void Draw(FrameView view, float alpha, string? capturePath = null)
    {
        long started = Stopwatch.GetTimestamp();
        _shaders.Globals = view.ShaderGlobals;

        // The scheduler leaves a whole step in the accumulator when a game exits mid-catch-up.
        alpha = Math.Clamp(alpha, 0f, 1f);

        GpuCommandBuffer commands = _device.AcquireCommandBuffer();
        _commands = commands;
        _tiles.BeginFrame();
        _window = commands.AcquireSwapchainTexture();

        // A frame that draws the build the last one recorded, on the same output, reuses the recording. The
        // passes a frame draws follow from its output. A reuse that does not hold records the frame again.
        (int width, int height) = _window is { } shown ? (shown.Width, shown.Height) : WindowSize;
        (int, int, bool, bool) output = (width, height, _window is not null, capturePath is not null);
        bool reuse = ReferenceEquals(view, _recordedView) && view.Builds == _recordedBuild && output == _recordedOutput;
        if (!Record(view, alpha, capturePath, reuse))
        {
            Record(view, alpha, capturePath, reuse: false);
            _sprites.CompareCheck();
        }

        _recordedView = view;
        _recordedBuild = view.Builds;
        _recordedOutput = output;

        GpuCopyPass copy = commands.BeginCopyPass();
        _textures.Pump(copy);
        UploadedBytes = _sprites.Upload(copy) + _tiles.Upload(_device, copy);
        copy.End();
        _sprites.Execute(commands);

        if (capturePath is not null)
        {
            Capture(commands, capturePath);
        }

        LastFrameMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    // Walks the frame onto the draw list, as a recording or as a reuse of the last one, and returns whether the
    // walk holds.
    private bool Record(FrameView view, float alpha, string? capturePath, bool reuse)
    {
        _sprites.Reset(reuse);

        // A store may load or release between frames, and no slice outlives the walk that fetched it.
        _recentSlices.AsSpan().Clear();
        _recentCount = 0;

        // The window pass is laid out at the swapchain texture's extent, which can trail the window's
        // through a resize. A frame the swapchain shows nothing of still draws the canvas and a
        // capture, at the window's extent.
        (int outputWidth, int outputHeight) = _window is { } shown ? (shown.Width, shown.Height) : WindowSize;
        GpuTexture? windowPass = _window;
        if ((capturePath is not null || view.ReadsScreen) && _canvas is null && outputWidth > 0 && outputHeight > 0)
        {
            windowPass = Sized(ref _offscreen, outputWidth, outputHeight).Texture;
        }

        CameraView camera = view.Camera.At(alpha);
        ScreenLayout layout = FrameLayout.Layout(_canvas, camera, view.Canvas, outputWidth, outputHeight, view.IntegerScaling);
        Rect world = camera.Place(1f, layout.Span);

        if (_canvas is null)
        {
            WorldEnds ends = WorldEnds.Resolve(view, alpha, null, outputWidth, outputHeight, factor: 1, in world, layout.Span, layout.World);
            if (view.LitWorld && windowPass is not null)
            {
                DrawLight(view, world, in ends, layout.World, outputWidth, outputHeight);
            }

            DrawWorld(view, alpha, windowPass, world, layout.Span, in ends, layout.World, outputWidth, outputHeight, ScreenPlacement.Identity, layout.OnSurface, surfaceFactor: 1);
            if (windowPass is not null)
            {
                DrawScreen(view, alpha, windowPass, layout.OnSurface, outputWidth, outputHeight, surfaceFactor: 1);
            }

            // The offscreen window pass is shown as drawn, texel for texel.
            if (!ReferenceEquals(windowPass, _window) && _window is { } window)
            {
                _sprites.BeginPass(window, ColorRgba.Black);
                _sprites.AddWhole(_offscreen!, Vector2.Zero, Vector2.Zero, Vector2.One, rotation: 0f, ColorRgba.White);
            }
        }
        else
        {
            int limit = FrameLayout.SurfaceFactorLimit(view.PixelPerfect, layout.Present.Scale);
            int factor = FrameLayout.SurfaceFactor(WorldZoomOut, limit);
            ScreenLayout drawn = FrameLayout.Enlarge(layout, factor);
            SampledTexture target = Sized(ref _target, drawn.Surface.Width, drawn.Surface.Height);
            int width = target.Texture.Width;
            int height = target.Texture.Height;
            WorldEnds ends = WorldEnds.Resolve(view, alpha, _canvas, outputWidth, outputHeight, factor, in world, layout.Span, drawn.World);

            if (view.LitWorld)
            {
                DrawLight(view, world, in ends, drawn.World, width, height);
            }

            DrawWorld(view, alpha, target.Texture, world, layout.Span, in ends, drawn.World, width, height, drawn.Present, drawn.OnSurface, factor);

            // Drawn over the world's bars. The viewport is the full surface again and the canvas sits
            // centred in it on whole pixels. Nothing lands on a grid the world did not use.
            if (layout.ScreenOnSurface)
            {
                DrawScreen(view, alpha, target.Texture, drawn.OnSurface, width, height, factor);
            }

            // A surface under integer scaling is shown in whole blocks whether or not it is pixel-perfect.
            Present(target, view.PixelPerfect || view.IntegerScaling, layout.Present, factor);

            if (!layout.ScreenOnSurface && _window is not null)
            {
                DrawScreen(view, alpha, null, layout.Layer, outputWidth, outputHeight, surfaceFactor: 1);
            }
        }

        if (layout.Layer.Scale > 0f)
        {
            _placement = layout.Layer;
        }

        return _sprites.Finish();
    }

    // Lets go of everything kept on the device from earlier frames, and of the sources it names, at a scene boundary.
    internal void ForgetRetained() => _tiles.Forget();

    // Submits the frame Draw recorded, with the overlay that joined it, which presents the swapchain
    // texture they drew into. Never waits for the display. A frame with a capture waits for the GPU,
    // then writes the capture.
    internal void Submit()
    {
        if (_commands is not { } commands)
        {
            return;
        }

        _commands = null;
        _window = null;
        bool wait = _waitForFrame;
        _waitForFrame = false;
        if (wait)
        {
            commands.SubmitAndWait();
        }
        else if (WatchesFrames)
        {
            commands.SubmitWatched();
        }
        else
        {
            commands.Submit();
        }

        if (wait && _capture is { } capture)
        {
            _capture = null;
            if (WriteCapture(capture.Readback, capture.Path))
            {
                CapturesSaved++;
            }
        }
    }

    private static bool WriteCapture(GpuReadback readback, string path)
    {
        using (readback)
        {
            return FrameCapture.Save(readback.EncodePng, path);
        }
    }

    // Settles ScreenLayer for view at the window's current extent, drawing nothing. The host
    // samples the mouse before the first frame, and an unplaced layer would hand the simulation
    // window pixels as canvas pixels for that frame.
    internal void ResolveScreenLayer(FrameView view)
    {
        (int width, int height) = WindowSize;
        ScreenLayout layout = FrameLayout.Layout(_canvas, view.Camera, view.Canvas, width, height, view.IntegerScaling);

        if (layout.Layer.Scale > 0f)
        {
            _placement = layout.Layer;
        }
    }

    // Draws every light into the frame's light, cleared to the scene's ambient colour. Mirrors DrawWorld's
    // preamble, on the light instead of the world's surface.
    private void DrawLight(FrameView view, in Rect world, in WorldEnds ends, in Letterbox fit, int surfaceWidth, int surfaceHeight)
    {
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            return;
        }

        SampledTexture light = Sized(ref _light, surfaceWidth, surfaceHeight, LightFormat);
        _sprites.BeginPass(light.Texture, view.Ambient);

        if (world.IsEmpty || fit.IsEmpty)
        {
            return;
        }

        _sprites.SetViewport(fit.X, fit.Y, fit.Width, fit.Height);

        Pass pass = WorldPass(in ends, fit.Scale);

        // Every quad here is additive, drawn with the pass's premultiplied blend: an additive intent packs
        // alpha zero, and under One/OneMinusSourceAlpha that is exactly One/One. An additive blend would
        // scale the source by its alpha.
        _sprites.SetTransform(WorldToSurface(ends.Corner, fit.Scale));
        _sprites.SetSampling(DrawSampling(view.PixelPerfect));

        DrawLights(view.Lights, view.PlacementRuns, ref pass);
        DrawLightPolygons(view.LightPolygons, view.PolygonPoints, view.PlacementRuns, ref pass);
    }

    // A light is always stored under a run of the world's own, never a canvas run. Its intensity scales its colour
    // in the vertex stage, and one quad draws it at any intensity.
    private void DrawLights(ReadOnlySpan<LightIntent> lights, ReadOnlySpan<PlacementRun> layers, ref Pass pass)
    {
        int next = 0;
        for (int index = 0; index < lights.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstLight <= index)
            {
                ScrollTo(ref pass, layers[next].ScrollFactor);
                next++;
            }

            SpriteIntent intent = lights[index].ToSprite();
            DrawSprite(in intent, ref pass, lights[index].Intensity);
        }

        ScrollTo(ref pass, Vector2.One);
    }

    // Each polygon light's fan of triangles, placed as a polygon is and added as a light is.
    private void DrawLightPolygons(ReadOnlySpan<StoredLightPolygon> polygons, ReadOnlySpan<Vector2> points, ReadOnlySpan<PlacementRun> layers, ref Pass pass)
    {
        int next = 0;
        for (int index = 0; index < polygons.Length; index++)
        {
            while (next < layers.Length && layers[next].FirstLightPolygon <= index)
            {
                ScrollTo(ref pass, layers[next].ScrollFactor);
                next++;
            }

            StoredLightPolygon light = polygons[index];
            DrawFan(
                points.Slice(light.FirstPoint, light.Count),
                points.Slice(light.FirstPoint + light.Count, light.Count),
                SpriteDrawList.Tint(light.Color, BlendMode.Additive),
                flash: 0u,
                light.Intensity,
                in pass);
        }

        ScrollTo(ref pass, Vector2.One);
    }

    // The sampler a texture with none of its own takes: point on a pixel-perfect frame, else linear.
    // A texture the build shipped always has its own, and only the engine's textures and the picture a
    // material samples take this one.
    private static TextureSampling DrawSampling(bool pixelPerfect) => pixelPerfect ? TextureSampling.Point : TextureSampling.Linear;

    // Returns target at width x height, in the swapchain's format unless format names another, allocating it on
    // first use and reallocating it when its size differs.
    private SampledTexture Sized(ref SampledTexture? target, int width, int height, GpuTextureFormat format = GpuTextureFormat.Invalid)
    {
        if (target is { } current && current.Texture.Width == width && current.Texture.Height == height)
        {
            return current;
        }

        target?.Dispose();
        target = Surface(width, height, format);

        return target;
    }

    private SampledTexture Surface(int width, int height, GpuTextureFormat format = GpuTextureFormat.Invalid) =>
        new(_device.CreateTexture(new GpuTextureInfo(format == GpuTextureFormat.Invalid ? _device.SwapchainFormat : format, GpuTextureUsage.Sampler | GpuTextureUsage.ColorTarget, width, height)));

    // Downloads the surface Draw's capture reads once its render passes are recorded, ahead of the
    // overlay's, for Submit to write to path.
    private void Capture(GpuCommandBuffer commands, string path)
    {
        if ((_target ?? _offscreen)?.Texture is not { } surface)
        {
            return;
        }

        try
        {
            GpuReadback readback = _device.CreateReadback(surface);
            GpuCopyPass copy = commands.BeginCopyPass();
            copy.DownloadFromTexture(surface, readback);
            copy.End();
            _capture = (readback, path);
            _waitForFrame = true;
        }
        catch (Exception error)
        {
            Log.Warning($"Frame capture to '{path}' failed before writing: {error.Message}");
        }
    }

    // surfaceWidth and surfaceHeight are the bound surface's extent, which the viewport no longer
    // reports once narrowed to the letterbox. present is where that surface lands in the window,
    // identity when the surface is the window pass's. target is null when the window has no area or the
    // swapchain shows nothing this frame, and the world's placement still settles then.
    private void DrawWorld(
        FrameView view,
        float alpha,
        GpuTexture? target,
        in Rect world,
        Vector2 span,
        in WorldEnds ends,
        in Letterbox fit,
        int surfaceWidth,
        int surfaceHeight,
        in ScreenPlacement present,
        in ScreenPlacement canvas,
        int surfaceFactor)
    {
        _world = null;

        // A minimised window has no area.
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            return;
        }

        // Cleared at full extent before the viewport narrows. Bars are presentation, not world intent,
        // and stay black.
        if (target is not null)
        {
            _sprites.BeginPass(target, ColorRgba.Black);
        }

        if (world.IsEmpty || fit.IsEmpty)
        {
            return;
        }

        // The camera moves on the same clock as what it looks at. Snapping it to the step's end would
        // slide the world back once per step. A pixel-perfect frame snaps the camera and everything it
        // draws at both ends of the step, and carries the whole pixels between them, as WorldEnds
        // describes. The simulation keeps its fractional positions.
        bool snap = view.PixelPerfect;
        if (present.Scale > 0f)
        {
            _world = new WorldPlacement(ends, fit, present, ScrollLayout.Parallax(ends.Corner, span, view.Camera.ScrollCenter));
        }

        if (target is null)
        {
            return;
        }

        _sprites.SetViewport(fit.X, fit.Y, fit.Width, fit.Height);
        Pass pass = WorldPass(in ends, fit.Scale);
        pass.Surface = target;

        // A canvas run lands where the screen layer would on this surface, through the narrowed viewport
        // the world draws in, on the grid the canvas had at a factor of 1.
        pass.WorldTransform = WorldToSurface(ends.Corner, fit.Scale);
        pass.CanvasTransform = SpriteDrawList.Multiply(CanvasToSurface(canvas), Matrix4x4.CreateTranslation(-fit.X, -fit.Y, 0f));
        pass.Canvas = GridPlacement.Canvas(canvas.Scale / surfaceFactor, snap, alpha);
        pass.Placement = canvas;
        pass.SurfaceWidth = surfaceWidth;
        pass.SurfaceHeight = surfaceHeight;

        _sprites.SetTransform(in pass.WorldTransform);
        _sprites.SetSampling(DrawSampling(snap));

        // Each lit quad, the clear colour's among them unless the whole world is unlit, is multiplied by the light at
        // its pixel as it draws. The light was drawn at this surface's extent and through the same viewport.
        pass.Light = view.LitWorld ? _light : null;
        _sprites.SetLight(pass.Light, lit: !view.UnlitWorld);

        // Drawn through the narrowed world viewport. Presentation bars stay black.
        _sprites.AddWhole(_white, ends.Corner, Vector2.Zero, span, rotation: 0f, view.ClearColor);

        DrawIntents(view.Sprites, view.Polygons, view.PolygonPoints, view.TileRuns, view.Lines, view.PlacementRuns, view.MaterialRuns, view.ClipRuns, view.ClipLevels, ref pass);
    }

    // The screen layer, in canvas pixels placed by placement. Drawn after the world and across the
    // full surface. It covers the bars the world's fit left. On a surface drawn surfaceFactor times
    // larger it snaps to the grid it had at a factor of 1, and its pixels are those pixels enlarged.
    private void DrawScreen(FrameView view, float alpha, GpuTexture? surface, in ScreenPlacement placement, int surfaceWidth, int surfaceHeight, int surfaceFactor)
    {
        bool snap = view.PixelPerfect;
        ReadOnlySpan<SpriteIntent> sprites = view.ScreenSprites;
        ReadOnlySpan<StoredPolygon> polygons = view.ScreenPolygons;
        ReadOnlySpan<LineIntent> lines = view.ScreenLines;
        if ((sprites.IsEmpty && polygons.IsEmpty && lines.IsEmpty) || surfaceWidth <= 0 || surfaceHeight <= 0 || !(placement.Scale > 0f))
        {
            return;
        }

        // The screen layer is never lit, though it can share the world's pass.
        _sprites.SetViewport(0, 0, surfaceWidth, surfaceHeight);
        _sprites.SetTransform(CanvasToSurface(placement));
        _sprites.SetSampling(DrawSampling(snap));
        _sprites.SetLight(null, lit: false);

        float grid = placement.Scale / surfaceFactor;
        GridPlacement canvas = GridPlacement.Canvas(grid, snap, alpha);
        Pass pass = new()
        {
            Scale = grid,
            Sprites = canvas,
            Lines = canvas,
            Canvas = canvas,
            Placement = placement,
            SurfaceWidth = surfaceWidth,
            SurfaceHeight = surfaceHeight,
            Surface = surface,
        };

        DrawIntents(sprites, polygons, view.PolygonPoints, view.TileRuns, lines, default, view.ScreenMaterialRuns, view.ScreenClipRuns, view.ClipLevels, ref pass);
    }

    // Draws the sprites with the polygons and the chunks of tiles among them, then the lines, each placement run as it
    // opens, each material run with its material, and each clip run cut to its rect. A polygon draws before the sprite
    // stored after it. The runs are in list order, and one cursor walks each kind. A run opens at a sprite, a
    // polygon and a line index, and applies to the intent stored after it: a sprite or polygon whose cursors
    // have both reached the run's. The lines walk the placement and clip runs again from the start. Lines draw
    // with Capsule's own shader. The quads after it draw with no material or clip.
    private void DrawIntents(
        ReadOnlySpan<SpriteIntent> sprites,
        ReadOnlySpan<StoredPolygon> polygons,
        ReadOnlySpan<Vector2> points,
        ReadOnlySpan<TileRun> tiles,
        ReadOnlySpan<LineIntent> lines,
        ReadOnlySpan<PlacementRun> layers,
        ReadOnlySpan<MaterialRun> materials,
        ReadOnlySpan<ClipRun> clips,
        ReadOnlySpan<ClipRegion> clipLevels,
        ref Pass pass)
    {
        int next = 0;
        int nextMaterial = 0;
        int nextClip = 0;
        int index = 0;
        int polygon = 0;
        while (index < sprites.Length || polygon < polygons.Length)
        {
            while (next < layers.Length && layers[next].FirstSprite <= index && layers[next].FirstPolygon <= polygon)
            {
                Place(in layers[next], ref pass);
                next++;
            }

            while (nextMaterial < materials.Length && materials[nextMaterial].FirstSprite <= index && materials[nextMaterial].FirstPolygon <= polygon)
            {
                _sprites.SetMaterial(materials[nextMaterial].Material);
                pass.Reading = materials[nextMaterial].Material is { Shader.ReadsScreen: true } && BeginRead(in pass);
                nextMaterial++;
            }

            while (nextClip < clips.Length && clips[nextClip].FirstSprite <= index && clips[nextClip].FirstPolygon <= polygon)
            {
                ApplyClip(clips[nextClip], clipLevels, in pass);
                nextClip++;
            }

            if (polygon < polygons.Length && polygons[polygon].Before <= index)
            {
                if (pass.Reading)
                {
                    _sprites.Widen(polygons[polygon].Bounds, ReadShift(in pass.Sprites));
                }

                if (polygons[polygon].HoldsTiles)
                {
                    DrawTiles(in polygons[polygon], in tiles[polygons[polygon].TileRunIndex], ref pass);
                }
                else
                {
                    DrawPolygon(in polygons[polygon], points, in pass);
                }

                polygon++;
                continue;
            }

            // The sprites up to where the next run opens or the next polygon draws share every setting.
            // A run whose polygon cursor has not come opens after a polygon, which ends the stretch itself.
            int end = sprites.Length;
            if (next < layers.Length && layers[next].FirstSprite > index)
            {
                end = Math.Min(end, layers[next].FirstSprite);
            }

            if (nextMaterial < materials.Length && materials[nextMaterial].FirstSprite > index)
            {
                end = Math.Min(end, materials[nextMaterial].FirstSprite);
            }

            if (nextClip < clips.Length && clips[nextClip].FirstSprite > index)
            {
                end = Math.Min(end, clips[nextClip].FirstSprite);
            }

            if (polygon < polygons.Length)
            {
                end = Math.Min(end, polygons[polygon].Before);
            }

            if (pass.Reading)
            {
                for (; index < end; index++)
                {
                    if (sprites[index].TryGetSweptBounds(out Rect swept))
                    {
                        _sprites.Widen(in swept, ReadShift(in pass.Sprites));
                    }

                    DrawSprite(in sprites[index], ref pass);
                }
            }
            else
            {
                DrawSprites(sprites[index..end], ref pass);
                index = end;
            }
        }

        _sprites.SetMaterial(null);
        _sprites.ClearClip();
        Place(new PlacementRun(0, 0, Vector2.One), ref pass);
        next = 0;
        nextClip = 0;
        for (int line = 0; line < lines.Length; line++)
        {
            while (next < layers.Length && layers[next].FirstLine <= line)
            {
                Place(in layers[next], ref pass);
                next++;
            }

            while (nextClip < clips.Length && clips[nextClip].FirstLine <= line)
            {
                ApplyClip(clips[nextClip], clipLevels, in pass);
                nextClip++;
            }

            DrawLine(in lines[line], in pass);
        }

        _sprites.ClearClip();
    }

    // Splits the pass for a material that reads the picture. Returns false where the surface cannot be read: a
    // canvas declared apart from the render resolution draws the screen layer on the window after the world.
    private bool BeginRead(in Pass pass)
    {
        if (pass.Surface is not { } surface)
        {
            if (!_warnedUnreadable)
            {
                _warnedUnreadable = true;
                Log.Warning("A screen entity's material calls SampleScreen, but a canvas declared apart from the render resolution draws the screen layer on the window, which it cannot read. The read shows nothing. Declare the canvas at the render resolution, or draw it in the world.");
            }

            return false;
        }

        _sprites.BeginRead(Sized(ref _screen, surface.Width, surface.Height));
        _sprites.SetReadScale(ReadScale(in pass));

        return true;
    }

    // The surface pixels one unit of the current placement spans.
    private static float ReadScale(in Pass pass) => pass.WorldScale > 0f && !pass.OnCanvas ? pass.WorldScale : pass.Placement.Scale;

    // Moves the pass onto run's placement: a scroll factor's corners on the world's grid, or canvas pixels on
    // the canvas's grid. Only a world pass holds a canvas run, and it swaps the draw list's transform to reach it.
    private void Place(in PlacementRun run, ref Pass pass)
    {
        _sprites.SetLight(pass.Light, lit: !run.Unlit);
        if (run.Canvas != pass.OnCanvas)
        {
            pass.OnCanvas = run.Canvas;
            _sprites.SetTransform(run.Canvas ? pass.CanvasTransform : pass.WorldTransform);
            pass.Scale = run.Canvas ? pass.Canvas.Grids.X : pass.WorldScale;
            pass.Sprites = pass.Lines = run.Canvas ? pass.Canvas : pass.World.Base;

            // A read's offset is in its sprite's units, and a run can cross from the world to a screen root.
            if (pass.Reading)
            {
                _sprites.SetReadScale(ReadScale(in pass));
            }
        }

        if (!run.Canvas)
        {
            ScrollTo(ref pass, run.ScrollFactor);
        }
    }

    // Moves the pass onto the world layer at factor, its corners at each end of the step.
    private static void ScrollTo(ref Pass pass, Vector2 factor)
    {
        pass.Sprites = pass.World.Layer(in pass.Sprites, factor);
        pass.Lines = pass.World.Layer(in pass.Lines, factor);
    }

    // How far a world rect is moved onto the frame where the pass draws it, within the pixel snapping adds,
    // which a read's reach covers.
    private static Vector2 ReadShift(in GridPlacement sprites) =>
        sprites.FrameCorner - StepInterpolation.Interpolate(sprites.PreviousLayerCorner, sprites.LayerCorner, sprites.Alpha);

    // Cuts the quads drawn next to the run's clip, each level moved by alpha between its two ends, intersected, and
    // placed on the surface. On a frame that snaps, each level's edges snap at both ends and move between them as a
    // sprite does. A clip that leaves nothing of the surface still takes its quads, and draws none of them, which
    // keeps what a frame stores the same at any fraction of a step.
    private void ApplyClip(in ClipRun run, ReadOnlySpan<ClipRegion> clipLevels, in Pass pass)
    {
        if (run.Levels == 0)
        {
            _sprites.ClearClip();
            return;
        }

        ReadOnlySpan<ClipRegion> levels = clipLevels.Slice(run.FirstLevel, run.Levels);
        if (ClipScissor.TryShow(levels, in pass.Canvas, in pass.Placement, pass.SurfaceWidth, pass.SurfaceHeight, out int x, out int y, out int width, out int height))
        {
            _sprites.SetClip(x, y, width, height);
        }
        else
        {
            _sprites.SetClip(0, 0, 0, 0);
        }
    }

    // The white texel stretched to the segment's length and thickness and turned along it, from the
    // middle of its left edge on A. A hairline is one pixel of the surface being drawn, since a
    // thinner quad would miss the pixel centres and not rasterise.
    private void DrawLine(in LineIntent line, in Pass pass)
    {
        Vector2 a = pass.Lines.Place(line.A - line.Motion, line.A);
        Vector2 b = pass.Lines.Place(line.B - line.Motion, line.B);

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

    // A chunk of tiles from the instances kept for it, written again where what it draws has changed. A chunk the
    // frame has no room to keep yet draws the cells it shows among the frame's own quads. The polygon that holds it
    // carries the style it was stored under.
    private void DrawTiles(in StoredPolygon stored, in TileRun run, ref Pass pass)
    {
        Resolve(ref pass, run.Source.Texture);
        _sprites.SetPlacement(in pass.Sprites);
        if (_tiles.Resolve(in run, stored.Color, stored.Flash, in pass.Slice) is not { } chunk)
        {
            _sprites.AddFrameRange(pass.Slice.Texture, _tiles.WriteShown(in run, stored.Color, stored.Flash), pass.Slice.OffsetX, pass.Slice.OffsetY);
        }
        else if (chunk.Count > 0)
        {
            _sprites.AddRetained(pass.Slice.Texture, chunk.Page.Buffer, chunk.First, chunk.Count);
        }
    }

    // A polygon's fan of triangles from its first point, on the white texel. The vertex stage snaps every
    // triangle by that point, and the fan moves as one shape.
    private void DrawPolygon(in StoredPolygon polygon, ReadOnlySpan<Vector2> points, in Pass pass) =>
        DrawFan(
            points.Slice(polygon.FirstPoint, polygon.Count),
            points.Slice(polygon.FirstPoint + polygon.Count, polygon.Count),
            SpriteDrawList.Tint(polygon.Color, polygon.Blend),
            SpriteDrawList.Flash(polygon.Flash, polygon.Color.A),
            intensity: 1f,
            in pass);

    private void DrawFan(ReadOnlySpan<Vector2> previous, ReadOnlySpan<Vector2> current, uint tint, uint flash, float intensity, in Pass pass)
    {
        _sprites.SetPlacement(in pass.Sprites);
        for (int corner = 1; corner < current.Length - 1; corner++)
        {
            _sprites.AddTriangle(_white, previous[0], current[0], previous[corner], current[corner], previous[corner + 1], current[corner + 1], tint, flash, intensity);
        }
    }

    // What one pass of intents is drawn with. Resolved is the handle Slice was fetched for, carried across the
    // stream, and a run of sprites on one texture resolves it once.
    private struct Pass
    {
        // The pixels per unit of the surface being drawn, which is a hairline's width.
        internal float Scale;

        // Where the current run's sprites and lines are placed. On the overlay pass a sprite snaps on the window's
        // grid and a line on the game surface's. It lands on the sprite it outlines.
        internal GridPlacement Sprites;
        internal GridPlacement Lines;

        // Where a canvas run is placed, on the grid the canvas had at a factor of 1. A clip is placed by it too.
        internal GridPlacement Canvas;

        // Where the canvas lands on the surface, and the surface's extent, which a clip is placed by.
        internal ScreenPlacement Placement;
        internal int SurfaceWidth;
        internal int SurfaceHeight;

        // On a world pass: the world's ends, its scale and transform, the canvas's transform for a screen root
        // sorted with the world, and whether the current run is that root's.
        internal WorldEnds World;
        internal float WorldScale;
        internal Matrix4x4 WorldTransform;
        internal Matrix4x4 CanvasTransform;
        internal bool OnCanvas;

        internal TextureHandle Resolved;
        internal TextureSlice Slice;

        // The surface a material can read as the picture, which a frame holding one never draws straight into
        // the window but for a screen layer past the present, and whether the current run is reading it.
        internal GpuTexture? Surface;
        internal bool Reading;

        // The light on a world pass in a lit frame, which multiplies every run but an unlit entity's, and null
        // otherwise.
        internal SampledTexture? Light;
    }

    // A pass over a world surface at scale pixels per unit, placed from ends.
    private static Pass WorldPass(in WorldEnds ends, float scale) => new()
    {
        Scale = scale,
        Sprites = ends.Base,
        Lines = ends.Base,
        World = ends,
        WorldScale = scale,
    };

    private void DrawSprite(in SpriteIntent sprite, ref Pass pass, float intensity = 1f)
    {
        Resolve(ref pass, sprite.Sprite.Texture);

        // A snapped quad puts both edges on the grid, or abutting tiles leave a gap at a fractional
        // scale. The vertex stage snaps a quad at a whole number of quarter turns whole, and any other
        // turn by its pivot alone.
        _sprites.SetPlacement(in pass.Sprites);
        _sprites.Add(pass.Slice.Texture, in sprite, pass.Slice.OffsetX, pass.Slice.OffsetY, intensity);
    }

    // A stretch of sprites under one placement, each run of them on one handle recorded in one pass.
    private void DrawSprites(ReadOnlySpan<SpriteIntent> sprites, ref Pass pass)
    {
        _sprites.SetPlacement(in pass.Sprites);
        int first = 0;
        while (first < sprites.Length)
        {
            TextureHandle handle = sprites[first].Sprite.Texture;
            int end = first + 1;
            while (end < sprites.Length && SameHandle(sprites[end].Sprite.Texture, in handle))
            {
                end++;
            }

            Resolve(ref pass, handle);
            _sprites.AddRange(pass.Slice.Texture, sprites[first..end], pass.Slice.OffsetX, pass.Slice.OffsetY);
            first = end;
        }
    }

    // Points the pass's slice at texture, fetched only where the pass holds another.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Resolve(ref Pass pass, in TextureHandle texture)
    {
        if (pass.Slice.Texture is null || texture != pass.Resolved)
        {
            pass.Resolved = texture;
            pass.Slice = Slice(in pass.Resolved);
        }
    }

    // Handles a sheet hands out share their strings, and two of one texture match without a character read.
    private static bool SameHandle(TextureHandle left, in TextureHandle right) =>
        (ReferenceEquals(left.Name, right.Name) && ReferenceEquals(left.Extension, right.Extension) && left.IsEngineOwned == right.IsEngineOwned)
        || left == right;

    private TextureSlice Slice(in TextureHandle handle)
    {
        int held = Math.Min(_recentCount, RecentSlices);
        for (int index = 0; index < held; index++)
        {
            if (_recentHandles[index] == handle)
            {
                return _recentSlices[index];
            }
        }

        TextureSlice slice = handle.IsEngineOwned
            ? new TextureSlice(EngineTexture(handle), 0, 0)
            : _textures.Get(handle);
        int slot = _recentCount++ % RecentSlices;
        _recentHandles[slot] = handle;
        _recentSlices[slot] = slice;

        return slice;
    }

    // A texture a material reads, whole or on its atlas page, or the transparent texel for the default
    // handle of one it never set.
    private MaterialTexture MaterialTexture(TextureHandle handle) =>
        handle.Name is null ? Assets.MaterialTexture.Whole(_unset)
        : handle.IsEngineOwned ? Assets.MaterialTexture.Whole(EngineTexture(handle))
        : _textures.GetForMaterial(handle);

    private SampledTexture EngineTexture(in TextureHandle handle) =>
        _engineTextures.TryGetValue(handle, out SampledTexture? texture)
            ? texture
            : throw new ArgumentException($"Unknown engine-owned texture handle '{handle.Name}'.", nameof(handle));

    // Letterboxed a second time, into the swapchain texture, at the surface's present placement. See
    // FrameLayout.PresentPass for how a pixel-perfect surface reaches a fractional scale. placement is
    // the layout's own, and factor how many times larger than it the surface was drawn.
    private void Present(SampledTexture target, bool pixelPerfect, in ScreenPlacement placement, int factor)
    {
        if (_window is not { } window)
        {
            return;
        }

        SampledTexture source = target;
        (int prescale, TextureSampling final, float scale) = FrameLayout.PresentPass(pixelPerfect, placement.Scale, factor);

        if (prescale > 1)
        {
            SampledTexture prescaled = Sized(ref _prescaled, target.Texture.Width * prescale, target.Texture.Height * prescale);
            _sprites.BeginPass(prescaled.Texture, null);
            _sprites.AddWhole(target, Vector2.Zero, Vector2.Zero, new Vector2(prescale), rotation: 0f, ColorRgba.White);

            source = prescaled;
        }

        _sprites.BeginPass(window, ColorRgba.Black);

        if (!(scale > 0f))
        {
            return;
        }

        _sprites.SetSampling(final);
        _sprites.AddWhole(source, placement.Origin, Vector2.Zero, new Vector2(scale), rotation: 0f, ColorRgba.White);
    }

    // The engine's radial light: full at the centre, falling to nothing at the edge as one minus the squared distance.
    // Most of its radius reads lit. Its texels are premultiplied, white at the falloff's opacity, and a PNG copying
    // them channel for channel would be premultiplied again on load.
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
                float v = d >= 1f ? 0f : 1f - (d * d);
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

        return TextureDecoder.Decode(resource, "default-font");
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
        _light?.Dispose();
        _prescaled?.Dispose();
        _offscreen?.Dispose();
        _screen?.Dispose();
        _sprites.Dispose();
        _overlaySprites.Dispose();
        _tiles.Dispose();
    }
}
