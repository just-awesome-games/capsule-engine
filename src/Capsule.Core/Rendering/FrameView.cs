using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// Mutable render intent, populated at scene startup, rewritten after each fixed step and read on draw frames: two ordered lists
/// of sprites to draw, one in world units and one in canvas pixels, the whole screen list over the
/// whole world list. Text and nine-sliced panels are on those lists too — a <see cref="TextIntent"/>
/// becomes one sprite per glyph and a <see cref="NineSliceIntent"/> one per slice — so
/// <see cref="Metrics"/> counts a run of text once per glyph. Each layer also carries an ordered
/// list of lines, drawn over that layer's sprites and counted as sprites are. World intent is at
/// authored positions whatever scroll factor its entity carries; <see cref="ParallaxLayers"/> says
/// which runs of it a renderer moves by a factor.
/// </summary>
public sealed class FrameView
{
    private readonly List<SpriteIntent> _sprites = [];
    private readonly List<SpriteIntent> _screen = [];
    private readonly List<LineIntent> _lines = [];
    private readonly List<LineIntent> _screenLines = [];
    private readonly List<ParallaxLayer> _layers = [];

    private int _submitted;

    private CameraView _camera;
    private CameraView _layerCamera;
    private Vector2 _scrollFactor = Vector2.One;
    private Rect _cullBounds;
    private bool _hasCullBounds;

    private Vector2 _canvas;
    private Rect _canvasBounds;
    private bool _hasCanvasBounds;

    private TextureSampling _sampling = TextureSampling.Linear;

    /// <summary>
    /// The world region on screen. A non-positive <see cref="CameraView.Size"/> draws nothing.
    /// <para>
    /// Inside a renderer on an entity whose scroll factor is not one, this is the view that entity
    /// is drawn by: the corners of the swept region moved by the factor about the camera's scroll
    /// origin, already confined to its bounds, with the span widened where a factor lets the layer
    /// outrun the frame. Its <see cref="CameraView.SweptBounds"/> cover everything the frame can
    /// draw of that entity, so a renderer culling against them needs no knowledge of the factor.
    /// </para>
    /// </summary>
    public CameraView Camera
    {
        get => _scrollFactor == Vector2.One ? _camera : _layerCamera;
        internal set
        {
            _camera = value;
            Recull();
        }
    }

    // The scroll factor of the entity whose renderer is running, which the scene sets before each
    // Draw; one outside a renderer. Every change opens a run in ParallaxLayers and reculls, so an
    // unchanged factor between two entities costs a comparison.
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
            _layers.Add(new ParallaxLayer(_sprites.Count, _lines.Count, value));
            Recull();
        }
    }

    /// <summary>
    /// The screen layer's extent in canvas pixels, whose origin is its top-left corner. Screen intent
    /// is culled against it, and a non-positive canvas disables that culling.
    /// </summary>
    public Vector2 Canvas
    {
        get => _canvas;
        internal set
        {
            _canvas = value;
            _canvasBounds = new Rect(Vector2.Zero, value);
            _hasCanvasBounds = !_canvasBounds.IsEmpty;
        }
    }

    // The layer the Add overloads that name none draw onto: the layer of the entity whose renderer is
    // running, which the scene sets before each Draw, and the world outside a renderer.
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

    /// <summary>
    /// The world-space sprites to draw, in the order added; drawn under every screen sprite.
    /// Invalidated by the next mutation.
    /// </summary>
    public ReadOnlySpan<SpriteIntent> Sprites => CollectionsMarshal.AsSpan(_sprites);

    /// <summary>
    /// The screen-space sprites to draw, in canvas pixels and in the order added; drawn over the
    /// whole of <see cref="Sprites"/>. Invalidated by the next mutation.
    /// </summary>
    public ReadOnlySpan<SpriteIntent> ScreenSprites => CollectionsMarshal.AsSpan(_screen);

    /// <summary>
    /// The world-space lines to draw, in the order added; drawn over every world sprite and under
    /// every screen sprite. Invalidated by the next mutation.
    /// </summary>
    public ReadOnlySpan<LineIntent> Lines => CollectionsMarshal.AsSpan(_lines);

    /// <summary>
    /// The screen-space lines to draw, in canvas pixels and in the order added; drawn over the
    /// whole of <see cref="ScreenSprites"/>. Invalidated by the next mutation.
    /// </summary>
    public ReadOnlySpan<LineIntent> ScreenLines => CollectionsMarshal.AsSpan(_screenLines);

    /// <summary>
    /// The runs of <see cref="Sprites"/> and <see cref="Lines"/> whose entity carries a scroll
    /// factor, in list order and back-to-back: each opens where the factor changed and closes where
    /// the next opens, and intent before the first is drawn with the world. Empty where no entity
    /// carries one. Invalidated by the next mutation.
    /// </summary>
    public ReadOnlySpan<ParallaxLayer> ParallaxLayers => CollectionsMarshal.AsSpan(_layers);

    /// <summary>Submission counts from the current rewrite, across both layers, lines included.</summary>
    public RenderMetrics Metrics => new(_submitted, _sprites.Count + _screen.Count + _lines.Count + _screenLines.Count);

    // Drops the ordered intent and resets Metrics, retaining capacity.
    internal void Clear()
    {
        _sprites.Clear();
        _screen.Clear();
        _lines.Clear();
        _screenLines.Clear();
        _layers.Clear();
        _submitted = 0;
        Space = RenderSpace.World;
        _scrollFactor = Vector2.One;
        Recull();
    }

    // The region world intent is culled against: the camera's swept bounds, or the layer's where
    // the running entity carries a scroll factor. A camera that spans nothing has swept bounds only
    // where it also moved, and a sliver of a rect is not a region anything should be culled against.
    private void Recull()
    {
        if (_scrollFactor != Vector2.One)
        {
            _layerCamera = _camera.ScrolledBy(_scrollFactor);
        }

        CameraView camera = Camera;
        _cullBounds = camera.SweptBounds;
        _hasCullBounds = camera.Size.X > 0f && camera.Size.Y > 0f && !_cullBounds.IsEmpty;
    }

    /// <summary>
    /// Adds a sprite to the layer the running renderer's entity lives in, culled against the camera in
    /// world space and against <see cref="Canvas"/> in screen space; an unset camera or canvas disables
    /// culling. Outside a renderer the layer is the world.
    /// </summary>
    public void Add(in SpriteIntent sprite) => Add(in sprite, Space);

    /// <summary>Adds a sprite to <paramref name="space"/>'s list, culled as that space culls.</summary>
    public void Add(in SpriteIntent sprite, RenderSpace space)
    {
        _submitted++;

        bool screen = space == RenderSpace.Screen;
        if (screen ? _hasCanvasBounds : _hasCullBounds)
        {
            Rect against = screen ? _canvasBounds : _cullBounds;
            if (!(sprite.TryGetSweptBounds(out Rect swept) && swept.Intersects(against)))
            {
                return;
            }
        }

        (screen ? _screen : _sprites).Add(sprite);
    }

    /// <summary>
    /// Adds <paramref name="sprite"/> repeated across <paramref name="tiling"/>, one sprite per copy
    /// culled and counted on its own, to the layer the running renderer's entity lives in. On each
    /// axis the period is the sprite's drawn <see cref="SpriteIntent.Size"/>: zero draws the frame
    /// once; a finite extent covers that many units from the frame's low edge towards +X or +Y,
    /// cropping the copy at the far edge; <see cref="float.PositiveInfinity"/> repeats without bound
    /// on both sides of the frame, which with culling disabled draws the frame once. A negative or
    /// NaN extent draws nothing, and so does a non-zero tiling of a sprite with a non-zero
    /// <see cref="SpriteIntent.Rotation"/> or <see cref="SpriteIntent.PreviousRotation"/>: a tiled
    /// sprite does not turn.
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

        // Negated so a NaN extent is refused alongside the negative ones, as a scale is. A turned
        // sprite is refused whole rather than turning each copy about its own pivot.
        if (!(tiling.X >= 0f) || !(tiling.Y >= 0f) ||
            sprite.PreviousRotation != 0f || sprite.Rotation != 0f ||
            !sprite.TryGetSweptBounds(out Rect swept))
        {
            _submitted++;
            return;
        }

        bool screen = space == RenderSpace.Screen;
        bool culled = screen ? _hasCanvasBounds : _hasCullBounds;
        Rect against = screen ? _canvasBounds : _cullBounds;

        TextureRegion region = sprite.Sprite.Region;
        Vector2 texelSize = new(sprite.Size.X / region.Width, sprite.Size.Y / region.Height);

        // The drawn rect's corner, which every copy is placed from, at both ends of the step.
        Vector2 corner = sprite.Position - (sprite.DrawOrigin * texelSize);
        Vector2 travel = sprite.PreviousPosition - sprite.Position;

        TileRange columns = TileRange.Along(tiling.X, sprite.Size.X, swept.Left, swept.Right, culled, against.Left, against.Right);
        TileRange rows = TileRange.Along(tiling.Y, sprite.Size.Y, swept.Top, swept.Bottom, culled, against.Top, against.Bottom);

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

                // Anchored at the drawn rect's corner whichever way the frame faces, so a copy lands
                // where its index says rather than swinging across the position on a flipped axis.
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

    /// <summary>
    /// Adds a line to the layer the running renderer's entity lives in, culled by the rect it covers
    /// as a sprite is. Outside a renderer the layer is the world.
    /// </summary>
    public void Add(in LineIntent line) => Add(in line, Space);

    /// <summary>Adds a line to <paramref name="space"/>'s list, culled as that space culls.</summary>
    public void Add(in LineIntent line, RenderSpace space)
    {
        _submitted++;

        if (!line.TryGetBounds(out Rect bounds))
        {
            return;
        }

        bool screen = space == RenderSpace.Screen;
        if ((screen ? _hasCanvasBounds : _hasCullBounds) && !bounds.Intersects(screen ? _canvasBounds : _cullBounds))
        {
            return;
        }

        (screen ? _screenLines : _lines).Add(line);
    }

    /// <summary>
    /// Lays <paramref name="text"/> out and adds one sprite per glyph to the layer the running
    /// renderer's entity lives in, each culled and counted on its own. Glyphs are added in reading
    /// order, so a later one covers an earlier one where they overlap. A null font or empty text adds
    /// nothing.
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

        // The whole run is laid out whatever the visible count, so revealing it a codepoint at a
        // time never moves a glyph already shown.
        int visible = text.VisibleCharacters ?? int.MaxValue;

        // The previous origin differs from the current one by the step's travel alone: the box is the
        // same shape at both ends.
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
    /// Expands <paramref name="panel"/> and adds one sprite per slice to the layer the running
    /// renderer's entity lives in, each culled and counted on its own. Slices are added left to right
    /// then top to bottom, so where a panel too small for its insets makes two of them overlap, the
    /// later one covers the earlier. A slice with no texels or no extent adds nothing.
    /// </summary>
    public void Add(in NineSliceIntent panel) => Add(in panel, Space);

    /// <summary>Expands <paramref name="panel"/> onto <paramref name="space"/>'s list.</summary>
    public void Add(in NineSliceIntent panel, RenderSpace space)
    {
        // Negated so a NaN extent is rejected alongside the non-positive ones: the corner slices keep
        // their own size whatever the target is, so without this a panel of no extent would draw them.
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
}
