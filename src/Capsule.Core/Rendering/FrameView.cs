using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// Mutable render intent, populated at scene startup, rewritten after each fixed step and read on
/// draw frames. It holds two ordered layers of sprites and lines, one in world units and one in
/// canvas pixels, and the screen layer draws over the world layer.
/// </summary>
/// <remarks>
/// Text and nine-sliced panels expand onto those lists, one sprite per glyph and one per slice.
/// Each <c>Add</c> draws onto the layer of the running renderer's entity, or onto the world outside
/// a renderer, and culls against the camera in world space and against <see cref="Canvas"/> in
/// screen space.
/// <para>
/// World intent sits at its authored position whatever scroll factor its entity carries. The host
/// moves each entity's intent by its factor at draw time.
/// </para>
/// </remarks>
public sealed class FrameView
{
    private readonly Layer _world = new();
    private readonly Layer _screen = new();
    private readonly List<ParallaxLayer> _parallax = [];
    private readonly List<LightIntent> _lights = [];

    private ColorRgba _ambient = ColorRgba.White;

    private int _submitted;

    private CameraView _camera;
    private CameraView _layerCamera;
    private Vector2 _scrollFactor = Vector2.One;
    private ColorRgba _tint = ColorRgba.White;
    private bool _tinted;
    private ColorRgba _flash;
    private bool _flashing;

    // Whether a stored sprite takes the tint or the flash, so an unstyled store costs one flag test.
    private bool _styled;
    private Material? _material;

    private Vector2 _canvas;

    private TextureSampling _sampling = TextureSampling.Linear;

    /// <summary>
    /// The world region on screen. A non-positive <see cref="CameraView.Size"/> draws nothing.
    /// </summary>
    /// <remarks>
    /// Inside a renderer on an entity whose scroll factor is not one, this is the view that entity
    /// is drawn by: the swept region moved by the factor about the camera's scroll origin, widened
    /// where the factor lets the layer outrun the frame. Its <see cref="CameraView.SweptBounds"/>
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
    // opens a run in ParallaxLayers. An unchanged factor between two entities costs one comparison.
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
            _parallax.Add(new ParallaxLayer(_world.Sprites.Count, _world.Lines.Count, value, _lights.Count));
            UpdateCullBounds();
        }
    }

    /// <summary>
    /// The screen layer's extent in canvas pixels, with the origin at its top-left corner.
    /// </summary>
    /// <remarks>
    /// Screen intent is culled against it. A non-positive canvas disables that culling.
    /// </remarks>
    public Vector2 Canvas
    {
        get => _canvas;
        internal set
        {
            _canvas = value;
            _screen.Bounds = new Rect(Vector2.Zero, value);
            _screen.Culls = !_screen.Bounds.IsEmpty;
        }
    }

    // The colour the running renderer's entity is tinted by, and white outside a renderer, with its
    // composed flash, the colour in RGB and the amount in alpha, transparent outside a renderer. The
    // scene sets both before each Draw. Only the leaves that store an intent apply them, so an
    // expansion such as text is tinted once, and the flash applies to sprites only, since only a
    // sprite draws through the shader that mixes it. White and transparent cost one flag test.
    internal void SetStyle(ColorRgba tint, ColorRgba flash)
    {
        _tint = tint;
        _tinted = tint != ColorRgba.White;
        _flash = flash;
        _flashing = flash.A != 0;
        _styled = _tinted || _flashing;
    }

    // The running renderer's material, and null for the engine's own shader outside a renderer. A
    // sprite stored under a material other than its layer's last opens a run in MaterialRuns.
    internal Material? Material
    {
        set => _material = value;
    }

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
    // false by every Clear, so a frame whose last light left runs none. An additive sprite alone does
    // not open the pass: it is a light only in a frame that lights, so a scene that set up no
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

    /// <summary>The world-space sprites to draw, in the order added, under the screen sprites. Invalidated by the next mutation.</summary>
    public ReadOnlySpan<SpriteIntent> Sprites => CollectionsMarshal.AsSpan(_world.Sprites);

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
    public ReadOnlySpan<SpriteIntent> ScreenSprites => CollectionsMarshal.AsSpan(_screen.Sprites);

    /// <summary>
    /// The world-space lines to draw, in the order added, over the world sprites and under the
    /// screen sprites. Invalidated by the next mutation.
    /// </summary>
    public ReadOnlySpan<LineIntent> Lines => CollectionsMarshal.AsSpan(_world.Lines);

    /// <summary>
    /// The screen-space lines to draw, in canvas pixels and in the order added, over every sprite in
    /// <see cref="ScreenSprites"/>. Invalidated by the next mutation.
    /// </summary>
    public ReadOnlySpan<LineIntent> ScreenLines => CollectionsMarshal.AsSpan(_screen.Lines);

    // The runs of Sprites, Lines and Lights whose entity carries a scroll factor, in list order and
    // back-to-back. A run opens where the factor changed and closes where the next one opens. Intent
    // before the first run draws with the world. Invalidated by the next mutation.
    internal ReadOnlySpan<ParallaxLayer> ParallaxLayers => CollectionsMarshal.AsSpan(_parallax);

    // The runs of Sprites and of ScreenSprites drawn by one material, in list order and back-to-back. A
    // run opens where the material changed and closes where the next one opens. Invalidated by the
    // next mutation.
    internal ReadOnlySpan<MaterialRun> MaterialRuns => CollectionsMarshal.AsSpan(_world.Runs);

    internal ReadOnlySpan<MaterialRun> ScreenMaterialRuns => CollectionsMarshal.AsSpan(_screen.Runs);

    /// <summary>
    /// Submission counts from the current rewrite, across both layers, lines included.
    /// </summary>
    /// <remarks>
    /// One submission is one sprite or line offered to the culler. Text, a panel and a tiling count
    /// the copies they expand to, and an expansion that produces nothing counts nothing.
    /// </remarks>
    public RenderMetrics Metrics => new(
        _submitted,
        _world.Sprites.Count + _screen.Sprites.Count + _world.Lines.Count + _screen.Lines.Count,
        _lights.Count);

    // The region the running renderer's space culls against, and whether it culls at all. A
    // bulk-drawing renderer tests its own bounds against this once instead of paying an Add
    // overload's per-sprite test.
    internal bool TryGetCullRegion(out Rect region)
    {
        Layer layer = Of(Space);
        region = layer.Bounds;

        return layer.Culls;
    }

    // Adds a sprite with no bounds test, for a caller that already tested its own bounds.
    internal void AddUnculled(in SpriteIntent sprite)
    {
        _submitted++;
        Store(Of(Space), in sprite);
    }

    /// <summary>Adds a sprite. An unset camera or canvas disables culling.</summary>
    public void Add(in SpriteIntent sprite) => Add(in sprite, Space);

    /// <summary>Adds a sprite to <paramref name="space"/>'s list, culled as that space culls.</summary>
    public void Add(in SpriteIntent sprite, RenderSpace space)
    {
        _submitted++;

        Layer layer = Of(space);
        if (layer.Culls && !(sprite.TryGetSweptBounds(out Rect swept) && swept.Intersects(layer.Bounds)))
        {
            return;
        }

        Store(layer, in sprite);
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

        if (!(light.Intensity > 0f) || !float.IsFinite(light.Intensity))
        {
            return;
        }

        if (!light.ToSprite(light.Color).TryGetSweptBounds(out Rect swept))
        {
            return;
        }

        if (_world.Culls && !swept.Intersects(_world.Bounds))
        {
            return;
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

        // Negated comparisons refuse a NaN extent along with the negative ones. A turned sprite is refused
        // outright, because turning each copy about its own pivot is not tiling.
        if (!(tiling.X >= 0f) || !(tiling.Y >= 0f) ||
            sprite.PreviousRotation != 0f || sprite.Rotation != 0f ||
            !sprite.TryGetSweptBounds(out Rect swept))
        {
            return;
        }

        Layer layer = Of(space);
        Rect against = layer.Bounds;

        TextureRegion region = sprite.Sprite.Region;
        Vector2 texelSize = new(sprite.Size.X / region.Width, sprite.Size.Y / region.Height);

        // The drawn rect's corner. Copies are placed from it at both ends of the step.
        Vector2 corner = sprite.Position - (sprite.DrawOrigin * texelSize);
        Vector2 travel = sprite.PreviousPosition - sprite.Position;

        TileRange columns = TileRange.ForAxis(tiling.X, sprite.Size.X, swept.Left, swept.Right, layer.Culls, against.Left, against.Right);
        TileRange rows = TileRange.ForAxis(tiling.Y, sprite.Size.Y, swept.Top, swept.Bottom, layer.Culls, against.Top, against.Bottom);

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

        if (!line.TryGetBounds(out Rect bounds))
        {
            return;
        }

        Layer layer = Of(space);
        if (layer.Culls && !bounds.Intersects(layer.Bounds))
        {
            return;
        }

        layer.Lines.Add(_tinted ? line with { Color = ColorRgba.Multiply(line.Color, _tint) } : line);
    }

    /// <summary>
    /// Lays <paramref name="text"/> out and adds one sprite per glyph, each culled and counted on
    /// its own.
    /// </summary>
    /// <remarks>
    /// Glyphs are added in reading order. Where two overlap, the later one covers the earlier.
    /// </remarks>
    public void Add(in TextIntent text) => Add(in text, Space);

    /// <summary>Lays <paramref name="text"/> out onto <paramref name="space"/>'s list.</summary>
    public void Add(in TextIntent text, RenderSpace space)
    {
        if (!text.TryPlace(out TextPlacement placed))
        {
            return;
        }

        BitmapFont font = placed.Font;
        int lineHeight = font.LineHeight;
        ReadOnlySpan<TextureHandle> pages = font.Pages;

        // The run is laid out in full whatever the visible count, so revealing it never moves a glyph.
        int visible = text.VisibleCharacters ?? int.MaxValue;

        // The box is the same shape at both ends of the step, so the previous origin differs by travel.
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
        // their own size whatever the target, so without this a panel of no extent would still draw them.
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
    private void Store(Layer layer, in SpriteIntent sprite)
    {
        if (!ReferenceEquals(_material, layer.RunMaterial))
        {
            layer.Runs.Add(new MaterialRun(layer.Sprites.Count, _material));
            layer.RunMaterial = _material;
        }

        layer.Sprites.Add(_styled ? Styled(in sprite) : sprite);
    }

    private SpriteIntent Styled(in SpriteIntent sprite) => sprite with
    {
        Color = _tinted ? ColorRgba.Multiply(sprite.Color, _tint) : sprite.Color,
        Flash = _flashing ? _flash : sprite.Flash,
    };

    // Drops the ordered intent and resets Metrics, retaining capacity.
    internal void Clear()
    {
        _world.Sprites.Clear();
        _world.Lines.Clear();
        _screen.Sprites.Clear();
        _screen.Lines.Clear();
        _world.ClearRuns();
        _screen.ClearRuns();
        _parallax.Clear();
        _lights.Clear();
        _submitted = 0;
        Space = RenderSpace.World;
        SetStyle(ColorRgba.White, default);
        _material = null;
        _scrollFactor = Vector2.One;
        LitWorld = false;
        _ambient = ColorRgba.White;
        UpdateCullBounds();
    }

    private Layer Of(RenderSpace space) => space == RenderSpace.Screen ? _screen : _world;

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
        _world.Bounds = camera.SweptBounds;
        _world.Culls = camera.Size.X > 0f && camera.Size.Y > 0f && !_world.Bounds.IsEmpty;
    }

    // One of the frame's two layers: the intent drawn in that space and the region it culls against.
    private sealed class Layer
    {
        internal List<SpriteIntent> Sprites { get; } = [];

        internal List<LineIntent> Lines { get; } = [];

        internal List<MaterialRun> Runs { get; } = [];

        // The material of the last run opened, null before the first.
        internal Material? RunMaterial { get; set; }

        internal Rect Bounds { get; set; }

        internal bool Culls { get; set; }

        internal void ClearRuns()
        {
            Runs.Clear();
            RunMaterial = null;
        }
    }
}
