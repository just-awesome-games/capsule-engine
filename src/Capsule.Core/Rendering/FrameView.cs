using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// Mutable render intent, populated at scene startup, rewritten after each fixed step and read on draw frames: two ordered lists
/// of sprites to draw, one in world units and one in canvas pixels, the whole screen list over the
/// whole world list. Text and nine-sliced panels are on those lists too — a <see cref="TextIntent"/>
/// becomes one sprite per glyph and a <see cref="NineSliceIntent"/> one per slice — so
/// <see cref="Metrics"/> counts a run of text once per glyph.
/// </summary>
public sealed class FrameView
{
    private readonly List<SpriteIntent> _sprites = [];
    private readonly List<SpriteIntent> _screen = [];

    private int _submitted;

    private CameraView _camera;
    private Rect _cullBounds;
    private bool _hasCullBounds;

    private Vector2 _canvas;
    private Rect _canvasBounds;
    private bool _hasCanvasBounds;

    private TextureSampling _sampling = TextureSampling.Linear;

    /// <summary>The world region on screen. A non-positive <see cref="CameraView.Size"/> draws nothing.</summary>
    public CameraView Camera
    {
        get => _camera;
        internal set
        {
            _camera = value;
            _cullBounds = value.SweptBounds;

            // A camera that spans nothing has swept bounds only where it also moved, and a
            // sliver of a rect is not a region anything should be culled against.
            _hasCullBounds = value.Size.X > 0f && value.Size.Y > 0f && !_cullBounds.IsEmpty;
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

    /// <summary>Sprite-submission counts from the current rewrite, across both lists.</summary>
    public RenderMetrics Metrics => new(_submitted, _sprites.Count + _screen.Count);

    // Drops the ordered intent and resets Metrics, retaining capacity.
    internal void Clear()
    {
        _sprites.Clear();
        _screen.Clear();
        _submitted = 0;
        Space = RenderSpace.World;
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

        foreach (GlyphPlacement glyphPlacement in new GlyphRun(font, text.Text, placed.BoxWidth, placed.Wrap, placed.Alignment))
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
                        new Vector2(horizontal.TargetExtent, vertical.TargetExtent),
                        FlipX: false,
                        FlipY: false,
                        panel.Color),
                    space);
            }
        }
    }
}
