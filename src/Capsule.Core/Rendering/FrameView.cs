using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// Mutable render intent, built from a scene on the first read after each step and on the first read
/// before any step. It holds two ordered layers of sprites and lines, one in world units and one in
/// canvas pixels, and the screen layer draws over the world layer.
/// </summary>
/// <remarks>
/// Text and nine-sliced panels expand onto those lists, one sprite per glyph and one per slice.
/// Each <c>Add</c> draws onto the layer of the running renderer's entity, or onto the world outside
/// a renderer, and culls against the camera in world space and against <see cref="Canvas"/> in
/// screen space. Screen intent from under a root that sorts with the world lands on the world's list in
/// canvas pixels, at its place in the world's draw order.
/// <para>
/// World intent sits at its authored position whatever scroll factor its entity carries. The host
/// moves each entity's intent by its factor at draw time.
/// </para>
/// </remarks>
public sealed class FrameView
{
    // A rect that overlaps nothing, which an empty one does not guarantee: a crossed rect still overlaps
    // a rect that straddles it.
    private static readonly Rect Nothing = Rect.FromEdges(float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity);

    // A store under no clip. The clip's own serials count up from zero.
    private const int Unclipped = -1;

    private readonly List<PlacementRun> _placements = [];
    private readonly List<LightIntent> _lights = [];

    // Every level of every clip set this frame, which both lists' clip runs name.
    private readonly List<ClipRegion> _clipLevels = [];

    // Where each space's intent lands, each from its own list. Each is rewritten as the state behind it
    // changes, never per store.
    private readonly Route _worldRoute = new(new Layer(Unclipped), Unclipped);
    private readonly Route _screenRoute = new(new Layer(0), 0);

    // Where the levels of the clip screen intent is stored under start, and how many there are. Zero
    // levels is no clip.
    private int _clipStart;
    private int _clipCount;

    private ColorRgba _ambient = ColorRgba.White;

    private int _submitted;

    private CameraView _camera;
    private CameraView _layerCamera;
    private Vector2 _scrollFactor = Vector2.One;
    private ColorRgba _tint = ColorRgba.White;
    private bool _tinted;
    private ColorRgba _flash;
    private bool _flashing;

    // Whether a stored sprite takes the tint or the flash. An unstyled store costs one flag test.
    private bool _styled;

    private Vector2 _canvas;

    private TextureSampling _sampling = TextureSampling.Linear;

    /// <summary>
    /// The world region on screen. A non-positive <see cref="CameraView.Size"/> draws nothing.
    /// </summary>
    /// <remarks>
    /// Inside a renderer on an entity whose scroll factor is not one, this is the view that entity
    /// is drawn by: the swept region moved by the factor about the camera's scroll centre, widened
    /// where the factor holds the layer back from the frame. Its <see cref="CameraView.SweptBounds"/>
    /// cover everything the frame can draw of that entity. A renderer can cull against them without
    /// knowing the factor.
    /// </remarks>
    public CameraView Camera
    {
        get => _scrollFactor == Vector2.One ? _camera : _layerCamera;
        internal set
        {
            _camera = value;
            UpdateCullBounds();
        }
    }

    // The scroll factor of the entity whose renderer is running, and one outside a renderer. Each change
    // opens a run in PlacementRuns. An unchanged factor between two entities costs one comparison.
    internal Vector2 ScrollFactor
    {
        get => _scrollFactor;
        set
        {
            if (value == _scrollFactor)
            {
                return;
            }

            _scrollFactor = value;
            OpenPlacementRun(canvas: false);
            UpdateCullBounds();
        }
    }

    // Whether the running renderer's root sorts with the world, and false outside a renderer. While it
    // does, screen intent lands on the world's list under a run placed in canvas pixels. An unchanged
    // value between two renderers costs one comparison.
    internal bool SortsWithWorld
    {
        get => _screenRoute.Canvas;
        set
        {
            if (value == _screenRoute.Canvas)
            {
                return;
            }

            _screenRoute.Canvas = value;
            _screenRoute.List = value ? WorldList : ScreenList;
        }
    }

    /// <summary>
    /// The screen layer's extent in canvas pixels, with the origin at its top-left corner.
    /// </summary>
    /// <remarks>
    /// Screen intent is culled against it. A non-positive canvas disables that culling. Inside a renderer
    /// under a <c>ClipRect</c>, screen intent is also culled against the region that clip shows.
    /// </remarks>
    public Vector2 Canvas
    {
        get => _canvas;
        internal set
        {
            _canvas = value;
            UpdateScreenBounds();
        }
    }

    // Clips the screen intent stored from here on until the next BeginClip or ClearClip. A clip is the
    // intersection of its levels, one per nested ClipRect, each a rect in canvas pixels that moves from
    // previous to current across the step. BeginClip starts one, AddClipLevel adds a level, and EndClip
    // closes it. An unchanged clip between two renderers is never set again. Each change opens a clip run
    // on the list screen intent next lands on, and narrows the screen space's cull bounds to the canvas and
    // the clip's reach. World intent is never clipped.
    internal void BeginClip()
    {
        _clipStart = _clipLevels.Count;
        _clipCount = 0;
    }

    internal void AddClipLevel(in Rect previous, in Rect current)
    {
        _clipLevels.Add(new ClipRegion(previous, current));
        _clipCount++;
    }

    internal void EndClip()
    {
        _screenRoute.ClipSerial++;
        UpdateScreenBounds();
    }

    // Ends the clip. The screen intent stored from here on is cut by none.
    internal void ClearClip()
    {
        if (_clipCount == 0)
        {
            return;
        }

        _clipCount = 0;
        _screenRoute.ClipSerial++;
        UpdateScreenBounds();
    }

    // The running renderer's material, and null for the engine's own shader outside a renderer. A
    // sprite stored under a material other than its layer's last opens a run in MaterialRuns.
    internal Material? Material { get; set; }

    // The scene's shader globals, which the host reads when it applies a material. A view built outside
    // a scene holds an empty set.
    internal ShaderGlobals ShaderGlobals { get; set; } = new();

    // The layer the Add overloads draw onto when none is named. The scene sets it to the running
    // renderer's entity's layer before each Draw, and to the world outside a renderer.
    internal RenderSpace Space { get; set; }

    /// <summary>The colour behind world render intent. Black by default.</summary>
    public ColorRgba ClearColor { get; internal set; } = ColorRgba.Black;

    /// <summary>
    /// The colour the world is lit by where no light reaches. White by default, the world at its
    /// authored colour.
    /// </summary>
    /// <remarks>
    /// A frame with white ambient and no light in <see cref="Lights"/> runs no lighting pass.
    /// </remarks>
    public ColorRgba Ambient
    {
        get => _ambient;
        internal set
        {
            _ambient = value;

            if (value != ColorRgba.White)
            {
                LitWorld = true;
            }
        }
    }

    // Whether this frame runs the lighting pass: a non-white ambient or a light added. Rewritten from
    // false by every Clear, and a frame whose last light left runs none. An additive sprite alone does
    // not open the pass. It is a light only in a frame that lights, and a scene that set up no
    // lighting keeps its unlit look and pays nothing.
    internal bool LitWorld { get; private set; }

    /// <summary>How world textures are filtered. Linear by default.</summary>
    public TextureSampling Sampling
    {
        get => _sampling;
        internal set
        {
            if (value is not TextureSampling.Linear and not TextureSampling.Point)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown texture sampling mode.");
            }

            _sampling = value;
        }
    }

    /// <summary>The world layer's sprites to draw, in the order added, under the screen sprites. Invalidated by the next mutation.</summary>
    /// <remarks>
    /// Those a screen root that sorts with the world draws are in canvas pixels, and the rest in world units.
    /// </remarks>
    public ReadOnlySpan<SpriteIntent> Sprites => CollectionsMarshal.AsSpan(WorldList.Sprites);

    /// <summary>
    /// The lights to draw into the frame's light map, in the order added. World-only: the screen
    /// layer is never lit.
    /// </summary>
    /// <remarks>Invalidated by the next mutation.</remarks>
    public ReadOnlySpan<LightIntent> Lights => CollectionsMarshal.AsSpan(_lights);

    /// <summary>
    /// The screen-space sprites to draw, in canvas pixels and in the order added, over every sprite
    /// in <see cref="Sprites"/>. Invalidated by the next mutation.
    /// </summary>
    public ReadOnlySpan<SpriteIntent> ScreenSprites => CollectionsMarshal.AsSpan(ScreenList.Sprites);

    /// <summary>
    /// The world layer's lines to draw, in the order added, over the world sprites and under the
    /// screen sprites, placed as <see cref="Sprites"/> are. Invalidated by the next mutation.
    /// </summary>
    public ReadOnlySpan<LineIntent> Lines => CollectionsMarshal.AsSpan(WorldList.Lines);

    /// <summary>
    /// The screen-space lines to draw, in canvas pixels and in the order added, over every sprite in
    /// <see cref="ScreenSprites"/>. Invalidated by the next mutation.
    /// </summary>
    public ReadOnlySpan<LineIntent> ScreenLines => CollectionsMarshal.AsSpan(ScreenList.Lines);

    // The runs of Sprites, Lines and Lights drawn by one placement, in list order and back-to-back: a scroll
    // factor, or canvas pixels for a screen root sorted with the world. A run opens where the placement
    // changed and closes where the next one opens. Intent before the first run draws with the world.
    // Invalidated by the next mutation.
    internal ReadOnlySpan<PlacementRun> PlacementRuns => CollectionsMarshal.AsSpan(_placements);

    // The runs of Sprites and of ScreenSprites drawn by one material, in list order and back-to-back. A
    // run opens where the material changed and closes where the next one opens. Invalidated by the
    // next mutation.
    internal ReadOnlySpan<MaterialRun> MaterialRuns => CollectionsMarshal.AsSpan(WorldList.Runs);

    internal ReadOnlySpan<MaterialRun> ScreenMaterialRuns => CollectionsMarshal.AsSpan(ScreenList.Runs);

    // The runs of each list drawn under one clip, in list order and back-to-back. A run opens where the
    // clip changed and closes where the next one opens. The world's list has runs only where a screen root
    // sorted with the world stores under a clip. Invalidated by the next mutation.
    internal ReadOnlySpan<ClipRun> ClipRuns => CollectionsMarshal.AsSpan(WorldList.ClipRuns);

    internal ReadOnlySpan<ClipRun> ScreenClipRuns => CollectionsMarshal.AsSpan(ScreenList.ClipRuns);

    // The levels both lists' clip runs name, back-to-back, in the order they were added. Invalidated by the
    // next mutation.
    internal ReadOnlySpan<ClipRegion> ClipLevels => CollectionsMarshal.AsSpan(_clipLevels);

    /// <summary>
    /// Submission counts from the current rewrite, across both layers, lines included.
    /// </summary>
    /// <remarks>
    /// One submission is one sprite or line offered to the culler. Text, a panel and a tiling count
    /// the copies they expand to, and an expansion that produces nothing counts nothing. A text run
    /// rejected whole before layout counts nothing.
    /// </remarks>
    public RenderMetrics Metrics => new(
        _submitted,
        WorldList.Sprites.Count + ScreenList.Sprites.Count + WorldList.Lines.Count + ScreenList.Lines.Count,
        _lights.Count);

    // The colour the running renderer's entity is tinted by, and white outside a renderer, with its
    // composed flash, the colour in RGB and the amount in alpha, transparent outside a renderer. The
    // scene sets both before each Draw. Only the leaves that store an intent apply them, and an
    // expansion such as text is tinted once. The flash applies to sprites only, because only a
    // sprite draws through the shader that mixes it. White and transparent cost one flag test.
    internal void SetStyle(ColorRgba tint, ColorRgba flash)
    {
        _tint = tint;
        _tinted = tint != ColorRgba.White;
        _flash = flash;
        _flashing = flash.A != 0;
        _styled = _tinted || _flashing;
    }

    // The region the running renderer's space culls against, and whether it culls at all. A
    // bulk-drawing renderer tests its own bounds against this once instead of paying an Add
    // overload's per-sprite test.
    internal bool TryGetCullRegion(out Rect region)
    {
        Route route = RouteOf(Space);
        region = route.Bounds;

        return route.Culls;
    }

    // Adds a sprite with no bounds test, for a caller that already tested its own bounds.
    internal void AddUnculled(in SpriteIntent sprite)
    {
        _submitted++;
        Store(RouteOf(Space), in sprite);
    }

    /// <summary>Adds a sprite. An unset camera or canvas disables culling.</summary>
    public void Add(in SpriteIntent sprite) => Add(in sprite, Space);

    /// <summary>Adds a sprite to <paramref name="space"/>'s list, culled as that space culls.</summary>
    public void Add(in SpriteIntent sprite, RenderSpace space)
    {
        _submitted++;

        Route route = RouteOf(space);
        if (route.Culls && !(sprite.TryGetSweptBounds(out Rect swept) && swept.Intersects(route.Bounds)))
        {
            return;
        }

        Store(route, in sprite);
    }

    /// <summary>Adds a light, culled through its swept region against the world layer as a sprite is.</summary>
    /// <remarks>
    /// A light is world-only. Attach a <c>PointLight</c> to a world entity. A light whose
    /// <see cref="LightIntent.Intensity"/> is not positive and finite is dropped.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The running renderer's space is the screen layer.</exception>
    public void Add(in LightIntent light)
    {
        if (Space == RenderSpace.Screen)
        {
            throw new InvalidOperationException(
                "A light is world-only: the screen layer is never lit. Attach the PointLight to a world entity.");
        }

        if (!(light.Intensity > 0f) || !float.IsFinite(light.Intensity) ||
            !light.ToSprite(light.Color).TryGetSweptBounds(out Rect swept) ||
            (_worldRoute.Culls && !swept.Intersects(_worldRoute.Bounds)))
        {
            return;
        }

        // A light is placed by the world's run, never by a sorted screen root's canvas run before it.
        if (WorldList.RunCanvas)
        {
            OpenPlacementRun(canvas: false);
        }

        _lights.Add(_tinted ? light with { Color = ColorRgba.Multiply(light.Color, _tint) } : light);
        LitWorld = true;
    }

    /// <summary>
    /// Adds <paramref name="sprite"/> repeated across <paramref name="tiling"/>, with each copy
    /// culled and counted on its own. On each axis the period is the sprite's drawn
    /// <see cref="SpriteIntent.Size"/>.
    /// </summary>
    /// <remarks>
    /// Zero draws the frame once. A finite extent covers that many units from the frame's low edge
    /// towards +X or +Y and crops the copy at the far edge. <see cref="float.PositiveInfinity"/>
    /// repeats without bound, and draws the frame once when culling is disabled. A negative or NaN
    /// extent draws nothing. A tiled sprite does not turn, and one with a non-zero rotation at
    /// either end draws nothing.
    /// <para>
    /// At most 1024 copies are emitted per axis. An extent far larger than the period covers only
    /// what those copies reach.
    /// </para>
    /// </remarks>
    public void Add(in SpriteIntent sprite, Vector2 tiling) => Add(in sprite, tiling, Space);

    /// <summary>Adds <paramref name="sprite"/> repeated across <paramref name="tiling"/> onto <paramref name="space"/>'s list.</summary>
    public void Add(in SpriteIntent sprite, Vector2 tiling, RenderSpace space)
    {
        if (tiling == Vector2.Zero)
        {
            Add(in sprite, space);
            return;
        }

        AddTiled(in sprite, tiling, space);
    }

    private void AddTiled(in SpriteIntent sprite, Vector2 tiling, RenderSpace space)
    {
        // Negated comparisons refuse a NaN extent along with the negative ones. A turned sprite is refused
        // outright, because turning each copy about its own pivot is not tiling.
        if (!(tiling.X >= 0f) || !(tiling.Y >= 0f) ||
            sprite.PreviousRotation != 0f || sprite.Rotation != 0f ||
            !sprite.TryGetSweptBounds(out Rect swept))
        {
            return;
        }

        Route route = RouteOf(space);
        bool culls = route.Culls;
        Rect against = route.Bounds;

        TextureRegion region = sprite.Sprite.Region;
        Vector2 texelSize = new(sprite.Size.X / region.Width, sprite.Size.Y / region.Height);

        // The drawn rect's corner. Copies are placed from it at both ends of the step.
        Vector2 corner = sprite.Position - (sprite.DrawOrigin * texelSize);
        Vector2 travel = sprite.PreviousPosition - sprite.Position;

        TileRange columns = TileRange.ForAxis(tiling.X, sprite.Size.X, swept.Left, swept.Right, culls, against.Left, against.Right);
        TileRange rows = TileRange.ForAxis(tiling.Y, sprite.Size.Y, swept.Top, swept.Bottom, culls, against.Top, against.Bottom);

        for (int row = rows.First; row <= rows.Last; row++)
        {
            (int sourceY, int sourceHeight) = rows.Crop(row, region.Y, region.Height, texelSize.Y, sprite.FlipY);
            if (sourceHeight <= 0)
            {
                continue;
            }

            for (int column = columns.First; column <= columns.Last; column++)
            {
                (int sourceX, int sourceWidth) = columns.Crop(column, region.X, region.Width, texelSize.X, sprite.FlipX);
                if (sourceWidth <= 0)
                {
                    continue;
                }

                // Anchor at the drawn rect's corner whichever way the frame faces. Anchoring at the
                // position instead would swing copies across it on a flipped axis.
                Vector2 at = corner + new Vector2(column * sprite.Size.X, row * sprite.Size.Y);
                Sprite copy = new(
                    sprite.Sprite.Texture,
                    new TextureRegion(sourceX, sourceY, sourceWidth, sourceHeight),
                    new Vector2(sprite.FlipX ? sourceWidth : 0, sprite.FlipY ? sourceHeight : 0));

                Add(
                    new SpriteIntent(
                        copy,
                        at + travel,
                        at,
                        PreviousRotation: 0f,
                        Rotation: 0f,
                        new Vector2(sourceWidth * texelSize.X, sourceHeight * texelSize.Y),
                        sprite.FlipX,
                        sprite.FlipY,
                        sprite.Color,
                        sprite.Blend),
                    space);
            }
        }
    }

    /// <summary>Adds a line, culled by the rect it covers as a sprite is.</summary>
    public void Add(in LineIntent line) => Add(in line, Space);

    /// <summary>Adds a line to <paramref name="space"/>'s list, culled as that space culls.</summary>
    public void Add(in LineIntent line, RenderSpace space)
    {
        _submitted++;

        Route route = RouteOf(space);
        if (!line.TryGetBounds(out Rect bounds) || (route.Culls && !bounds.Intersects(route.Bounds)))
        {
            return;
        }

        Enter(route);
        route.List.Lines.Add(_tinted ? line with { Color = ColorRgba.Multiply(line.Color, _tint) } : line);
    }

    /// <summary>
    /// Lays <paramref name="text"/> out and adds one sprite per glyph, each culled and counted on
    /// its own.
    /// </summary>
    /// <remarks>
    /// Glyphs are added in reading order. Where two overlap, the later one covers the earlier. A run
    /// whose measured box is wholly outside the culled region is rejected before layout.
    /// </remarks>
    public void Add(in TextIntent text) => Add(in text, Space);

    /// <summary>Lays <paramref name="text"/> out onto <paramref name="space"/>'s list.</summary>
    public void Add(in TextIntent text, RenderSpace space)
    {
        if (!text.TryPlace(out TextPlacement placed)
            || Rejects(TextBox.Reach(text.PreviousPosition, text.Position, text.Size, text.Scale, placed.Font.Overhang, placed.Box), space))
        {
            return;
        }

        BitmapFont font = placed.Font;
        int lineHeight = font.LineHeight;
        ReadOnlySpan<TextureHandle> pages = font.Pages;

        // The run is laid out in full whatever the visible count. Revealing it never moves a glyph.
        int visible = text.VisibleCharacters ?? int.MaxValue;

        // The box is the same shape at both ends of the step. The previous origin differs by travel.
        Vector2 travel = text.PreviousPosition - text.Position;

        foreach (GlyphPlacement glyphPlacement in new GlyphRun(font, text.Text.Span, placed.BoxWidth, placed.Wrap, placed.Alignment))
        {
            if (glyphPlacement.Index >= visible)
            {
                return;
            }

            Glyph glyph = glyphPlacement.Glyph;
            Vector2 origin = placed.Origin + (new Vector2(
                glyphPlacement.PenX + glyph.XOffset,
                (glyphPlacement.Line * lineHeight) + glyph.YOffset) * text.Scale);

            Add(
                new SpriteIntent(
                    new Sprite(pages[glyph.Page], glyph.Region),
                    origin + travel,
                    origin,
                    PreviousRotation: 0f,
                    Rotation: 0f,
                    new Vector2(glyph.Region.Width, glyph.Region.Height) * text.Scale,
                    FlipX: false,
                    FlipY: false,
                    text.Color),
                space);
        }
    }

    // Whether space's list culls and reach lies wholly outside what it keeps. A run is rejected whole,
    // before any of its glyphs is built.
    internal bool Rejects(in Rect reach, RenderSpace space)
    {
        Route route = RouteOf(space);

        return route.Culls && !reach.Intersects(route.Bounds);
    }

    /// <summary>
    /// Expands <paramref name="panel"/> and adds one sprite per slice that has both texels and
    /// extent, each culled and counted on its own.
    /// </summary>
    /// <remarks>
    /// Slices are added left to right then top to bottom. A panel too small for its insets overlaps
    /// slices, and the later one covers the earlier.
    /// </remarks>
    public void Add(in NineSliceIntent panel) => Add(in panel, Space);

    /// <summary>Expands <paramref name="panel"/> onto <paramref name="space"/>'s list.</summary>
    public void Add(in NineSliceIntent panel, RenderSpace space)
    {
        // Negated comparisons reject a NaN extent along with the non-positive ones. Corner slices keep
        // their own size whatever the target. Without this a panel of no extent would still draw them.
        if (!(panel.Size.X > 0f) || !(panel.Size.Y > 0f))
        {
            return;
        }

        Span<SliceSpan> columns = stackalloc SliceSpan[3];
        Span<SliceSpan> rows = stackalloc SliceSpan[3];
        panel.Slice(columns, rows);

        TextureHandle texture = panel.Sprite.Texture;
        Vector2 travel = panel.PreviousPosition - panel.Position;

        for (int row = 0; row < rows.Length; row++)
        {
            SliceSpan vertical = rows[row];
            if (vertical.SourceExtent <= 0 || !(vertical.TargetExtent > 0f))
            {
                continue;
            }

            for (int column = 0; column < columns.Length; column++)
            {
                SliceSpan horizontal = columns[column];
                if (horizontal.SourceExtent <= 0 || !(horizontal.TargetExtent > 0f))
                {
                    continue;
                }

                Vector2 corner = panel.Position + new Vector2(horizontal.TargetOffset, vertical.TargetOffset);

                Add(
                    new SpriteIntent(
                        new Sprite(
                            texture,
                            new TextureRegion(
                                horizontal.SourceOffset,
                                vertical.SourceOffset,
                                horizontal.SourceExtent,
                                vertical.SourceExtent)),
                        corner + travel,
                        corner,
                        PreviousRotation: 0f,
                        Rotation: 0f,
                        new Vector2(horizontal.TargetExtent, vertical.TargetExtent),
                        FlipX: false,
                        FlipY: false,
                        panel.Color),
                    space);
            }
        }
    }

    // The one store point every sprite passes, where the running entity's tint and flash apply.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Store(Route route, in SpriteIntent sprite)
    {
        Layer layer = route.List;
        if (!ReferenceEquals(Material, layer.RunMaterial))
        {
            layer.Runs.Add(new MaterialRun(layer.Sprites.Count, Material));
            layer.RunMaterial = Material;
        }

        Enter(route);
        layer.Sprites.Add(_styled ? Styled(in sprite) : sprite);
    }

    // Opens the clip run and the placement run route's intent is stored under where its list's last ones
    // differ. The test is one comparison of both packed into a key. Only the world's list opens placement
    // runs, because the screen layer's route is never in canvas pixels on its own list.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Enter(Route route)
    {
        if (route.Key != route.List.RunKey)
        {
            Reenter(route);
        }
    }

    // Screen intent is stored under the running clip, and world intent under none.
    private void Reenter(Route route)
    {
        Layer layer = route.List;
        if (route.Key >> 1 != layer.RunKey >> 1)
        {
            bool clipped = route.ClipSerial != Unclipped;
            layer.ClipRuns.Add(new ClipRun(layer.Sprites.Count, layer.Lines.Count, clipped ? _clipStart : 0, clipped ? _clipCount : 0));
        }

        if (route.Canvas != layer.RunCanvas)
        {
            OpenPlacementRun(route.Canvas);
        }

        layer.RunKey = route.Key;
    }

    // Opens a run on the world's list placed in canvas pixels, or by the running scroll factor.
    private void OpenPlacementRun(bool canvas)
    {
        _placements.Add(new PlacementRun(WorldList.Sprites.Count, WorldList.Lines.Count, _scrollFactor, _lights.Count, canvas));
        WorldList.RunKey = (WorldList.RunKey & ~1) | (canvas ? 1 : 0);
    }

    private SpriteIntent Styled(in SpriteIntent sprite) => sprite with
    {
        Color = _tinted ? ColorRgba.Multiply(sprite.Color, _tint) : sprite.Color,
        Flash = _flashing ? _flash : sprite.Flash,
    };

    // Drops the ordered intent and resets Metrics, retaining capacity.
    internal void Clear()
    {
        WorldList.Clear();
        ScreenList.Clear();
        _placements.Clear();
        _lights.Clear();
        _clipLevels.Clear();
        _clipStart = 0;
        _clipCount = 0;
        _screenRoute.ClipSerial = 0;
        _submitted = 0;
        Space = RenderSpace.World;
        SortsWithWorld = false;
        SetStyle(ColorRgba.White, default);
        Material = null;
        _scrollFactor = Vector2.One;
        LitWorld = false;
        _ambient = ColorRgba.White;
        UpdateCullBounds();
        UpdateScreenBounds();
    }

    private Layer WorldList => _worldRoute.Home;

    private Layer ScreenList => _screenRoute.Home;

    private Route RouteOf(RenderSpace space) => space == RenderSpace.Screen ? _screenRoute : _worldRoute;

    // The region world intent is culled against: the camera's swept bounds, or the layer's when the
    // running entity carries a scroll factor. A camera that spans nothing culls nothing, because a sliver
    // of a rect is not a usable cull region.
    private void UpdateCullBounds()
    {
        if (_scrollFactor != Vector2.One)
        {
            _layerCamera = _camera.ScrolledBy(_scrollFactor);
        }

        CameraView camera = Camera;
        _worldRoute.Bounds = camera.SweptBounds;
        _worldRoute.Culls = camera.Size.X > 0f && camera.Size.Y > 0f && !camera.SweptBounds.IsEmpty;
    }

    // The region screen intent is culled against: the canvas, and inside a clip also the intersection over
    // its levels of each level's union of its two ends. That region holds whatever any interpolated frame of
    // the step shows. A clip culls even where the canvas is unset, and one that shows nothing culls
    // everything.
    private void UpdateScreenBounds()
    {
        Rect canvas = new(Vector2.Zero, _canvas);
        bool bounded = !canvas.IsEmpty;

        if (_clipCount == 0)
        {
            _screenRoute.Bounds = canvas;
            _screenRoute.Culls = bounded;

            return;
        }

        Rect reach = bounded ? canvas : Rect.FromEdges(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);
        ReadOnlySpan<ClipRegion> levels = CollectionsMarshal.AsSpan(_clipLevels).Slice(_clipStart, _clipCount);
        for (int i = 0; i < levels.Length; i++)
        {
            ClipRegion level = levels[i];
            Rect span = level.Previous.IsEmpty && level.Current.IsEmpty ? Nothing : Rect.Union(level.Previous, level.Current);
            reach = Rect.Intersection(reach, span);
        }

        _screenRoute.Bounds = reach.IsEmpty ? Nothing : reach;
        _screenRoute.Culls = true;
    }

    // One of the frame's two ordered lists, and the runs that cut it by material and by clip.
    // unclipped: the clip serial the list starts under, which opens no run for intent stored under no clip.
    private sealed class Layer(int unclipped)
    {
        private readonly int _unclipped = unclipped;

        internal List<SpriteIntent> Sprites { get; } = [];

        internal List<LineIntent> Lines { get; } = [];

        internal List<MaterialRun> Runs { get; } = [];

        internal List<ClipRun> ClipRuns { get; } = [];

        // The Route.Key of the last runs opened: the clip serial the clip run was opened for, and whether the
        // placement run is in canvas pixels, which only the world's list opens.
        internal int RunKey { get; set; } = unclipped << 1;

        internal bool RunCanvas => (RunKey & 1) != 0;

        // The material of the last run opened, null before the first.
        internal Material? RunMaterial { get; set; }

        internal void Clear()
        {
            Sprites.Clear();
            Lines.Clear();
            Runs.Clear();
            ClipRuns.Clear();
            RunKey = _unclipped << 1;
            RunMaterial = null;
        }
    }

    // Where one space's intent lands: the list, the region it culls against, the clip serial it is stored
    // under, Unclipped for none, and whether it is placed in canvas pixels on the world's list. Key packs the
    // last two, the serial above the canvas bit. Home is the space's own list, which List leaves only for a
    // screen root sorted with the world.
    private sealed class Route(Layer home, int clipSerial)
    {
        internal Layer Home { get; } = home;

        internal Layer List { get; set; } = home;

        internal Rect Bounds { get; set; }

        internal bool Culls { get; set; }

        internal int Key { get; private set; } = clipSerial << 1;

        internal int ClipSerial
        {
            get => Key >> 1;
            set => Key = (value << 1) | (Key & 1);
        }

        internal bool Canvas
        {
            get => (Key & 1) != 0;
            set => Key = (Key & ~1) | (value ? 1 : 0);
        }
    }
}
