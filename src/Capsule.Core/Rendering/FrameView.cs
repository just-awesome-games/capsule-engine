using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// Mutable render intent, populated at scene startup, rewritten after each fixed step and read on
/// draw frames. It holds two ordered layers of sprites and lines, one in world units and one in
/// canvas pixels, and the screen layer draws over the world layer. Text and nine-sliced panels
/// expand onto those lists, one sprite per glyph and one per slice. Each <c>Add</c> draws onto the
/// layer of the running renderer's entity, or onto the world outside a renderer, and culls against
/// the camera in world space and against <see cref="Canvas"/> in screen space.
/// <para>
/// World intent sits at its authored position whatever scroll factor its entity carries.
/// <see cref="ParallaxLayers"/> names the runs a renderer moves by a factor.
/// </para>
/// </summary>
public sealed class FrameView
{
    private readonly Layer _world = new();
    private readonly Layer _screen = new();
    private readonly List<ParallaxLayer> _parallax = [];

    private int _submitted;

    private CameraView _camera;
    private CameraView _layerCamera;
    private Vector2 _scrollFactor = Vector2.One;

    private Vector2 _canvas;

    private TextureSampling _sampling = TextureSampling.Linear;

    /// <summary>
    /// The world region on screen. A non-positive <see cref="CameraView.Size"/> draws nothing.
    /// <para>
    /// Inside a renderer on an entity whose scroll factor is not one, this is the view that entity
    /// is drawn by: the swept region moved by the factor about the camera's scroll origin, widened
    /// where the factor lets the layer outrun the frame. Its
    /// <see cref="CameraView.SweptBounds"/> cover everything the frame can draw of that entity, so
    /// a renderer can cull against them without knowing the factor.
    /// </para>
    /// </summary>
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
            _parallax.Add(new ParallaxLayer(_world.Sprites.Count, _world.Lines.Count, value));
            UpdateCullBounds();
        }
    }

    /// <summary>
    /// The screen layer's extent in canvas pixels, with the origin at its top-left corner. Screen
    /// intent is culled against it. A non-positive canvas disables that culling.
    /// </summary>
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

    // The layer the Add overloads draw onto when none is named. The scene sets it to the running
    // renderer's entity's layer before each Draw, and to the world outside a renderer.
    internal RenderSpace Space { get; set; }

    /// <summary>The colour behind world render intent. Black by default.</summary>
    public ColorRgba ClearColor { get; internal set; } = ColorRgba.Black;

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

    /// <summary>
    /// The runs of <see cref="Sprites"/> and <see cref="Lines"/> whose entity carries a scroll
    /// factor, in list order and back-to-back. A run opens where the factor changed and closes where
    /// the next one opens. Intent before the first run draws with the world. Invalidated by the next
    /// mutation.
    /// </summary>
    public ReadOnlySpan<ParallaxLayer> ParallaxLayers => CollectionsMarshal.AsSpan(_parallax);

    /// <summary>
    /// Submission counts from the current rewrite, across both layers, lines included. One
    /// submission is one sprite or line offered to the culler. Text, a panel and a tiling count the
    /// copies they expand to, and an expansion that produces nothing counts nothing.
    /// </summary>
    public RenderMetrics Metrics => new(
        _submitted,
        _world.Sprites.Count + _screen.Sprites.Count + _world.Lines.Count + _screen.Lines.Count);

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

        layer.Sprites.Add(sprite);
    }

    /// <summary>
    /// Adds <paramref name="sprite"/> repeated across <paramref name="tiling"/>, with each copy
    /// culled and counted on its own. On each axis the period is the sprite's drawn
    /// <see cref="SpriteIntent.Size"/>. Zero draws the frame once. A finite extent covers that many
    /// units from the frame's low edge towards +X or +Y and crops the copy at the far edge.
    /// <see cref="float.PositiveInfinity"/> repeats without bound, and draws the frame once when
    /// culling is disabled. A negative or NaN extent draws nothing, and so does a tiling of a turned
    /// sprite, because a tiled sprite does not turn.
    /// <para>
    /// At most 1024 copies are emitted per axis. An extent far larger than the period covers only what
    /// those copies reach.
    /// </para>
    /// </summary>
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
                        sprite.Color),
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

        layer.Lines.Add(line);
    }

    /// <summary>
    /// Lays <paramref name="text"/> out and adds one sprite per glyph, each culled and counted on
    /// its own. Glyphs are added in reading order, so where two overlap the later one covers the
    /// earlier.
    /// </summary>
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
    /// extent, each culled and counted on its own. Slices are added left to right then top to
    /// bottom. A panel too small for its insets overlaps slices, and the later one covers the
    /// earlier.
    /// </summary>
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

    // Drops the ordered intent and resets Metrics, retaining capacity.
    internal void Clear()
    {
        _world.Sprites.Clear();
        _world.Lines.Clear();
        _screen.Sprites.Clear();
        _screen.Lines.Clear();
        _parallax.Clear();
        _submitted = 0;
        Space = RenderSpace.World;
        _scrollFactor = Vector2.One;
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

        internal Rect Bounds { get; set; }

        internal bool Culls { get; set; }
    }
}
