using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one run of text inside a box, at one font pixel per unit of the entity's
/// space times the scale of the entity's <see cref="Entity.WorldTransform"/>.
/// </summary>
/// <remarks>
/// Rotation anywhere in the entity's ancestry is refused while a label is present. A negative scale
/// axis mirrors the placed run about the entity's position. Coordinates are Y-down, in world units under a world root and canvas pixels
/// under a screen root.
/// <para>
/// The box's <see cref="Pivot"/> sits at the entity's position plus <see cref="Offset"/>.
/// <see cref="HorizontalAlignment"/> and <see cref="VerticalAlignment"/> move the text inside the box
/// and never move the box. A <see cref="Size"/> of zero makes the box the rect of the
/// <see cref="ScreenEntity"/> holding it, or else the measured run.
/// </para>
/// </remarks>
/// <param name="font">The font the run is drawn with.</param>
/// <param name="text">The text to draw. Empty by default, which draws nothing.</param>
public sealed class Label(BitmapFont font, string text = "") : Renderer
{
    /// <summary>The font the run is laid out and drawn with.</summary>
    public BitmapFont Font
    {
        get;

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = font ?? throw new ArgumentNullException(nameof(font));

    // The text in the form it was last written: either a string, or a span copied into a buffer this label
    // owns and grows only when a longer span arrives. _text holds the string form and is null after a span
    // write until Text builds it. Layout reads _memory, and neither path allocates in Draw.
    private string? _text = text ?? throw new ArgumentNullException(nameof(text));
    private ReadOnlyMemory<char> _memory = text.AsMemory();
    private char[] _buffer = [];

    // Counts every write to the text. The cached layout reads it to tell the characters changed.
    private int _textVersion;

    // The run laid out, built on the first draw or bounds read and kept until something it depends on changes.
    private LabelLayout? _layout;

    /// <summary>The text to draw. Empty draws nothing.</summary>
    /// <remarks>
    /// A <c>\n</c> starts a new line, and a codepoint <see cref="Font"/> has no glyph for draws
    /// nothing and advances nothing. After <see cref="SetText"/> the getter returns the same
    /// characters as a string, built on the first read after each write.
    /// </remarks>
    public string Text
    {
        get => _text ??= _memory.ToString();

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _text = value;
            _memory = value.AsMemory();
            _textVersion++;
        }
    }

    /// <summary>
    /// Sets <see cref="Text"/> from a span, for a readout rewritten every frame.
    /// </summary>
    /// <remarks>
    /// The label copies the characters into a buffer it owns. The caller's span may change as soon as
    /// this returns. The call allocates nothing once the label has held a run this long.
    /// </remarks>
    public void SetText(ReadOnlySpan<char> text)
    {
        if (_buffer.Length < text.Length)
        {
            _buffer = new char[Math.Max(text.Length, _buffer.Length * 2)];
        }

        text.CopyTo(_buffer);
        _memory = _buffer.AsMemory(0, text.Length);
        _text = null;
        _textVersion++;
    }

    /// <summary>
    /// Added to the entity's position to give the point the box's <see cref="Pivot"/> sits on, in the
    /// entity's own units. Zero by default.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// The point on the box that sits at the entity's position plus <see cref="Offset"/>. Defaults to
    /// <see cref="Capsule.Rendering.Pivot.TopLeft"/>.
    /// </summary>
    public Pivot Pivot { get; set; }

    /// <summary>
    /// The box the run is laid out in, in the entity's units. A non-positive component uses the
    /// measured run on that axis, which is the default on both axes.
    /// </summary>
    /// <remarks>
    /// A zero axis on a <see cref="ScreenEntity"/> takes the entity's rect extent first, and the measured
    /// run where that extent is zero too. On an axis the rect fills, <see cref="Pivot"/> reads zero and the
    /// box is the rect itself. <see cref="Wrap"/> wraps inside a positive X.
    /// </remarks>
    public Vector2 Size { get; set; }

    /// <summary>
    /// Whether a line wider than <see cref="Size"/> breaks inside the box. Defaults to
    /// <see cref="TextWrap.None"/>.
    /// </summary>
    public TextWrap Wrap { get; set; }

    /// <summary>
    /// Where each line sits between the box's left and right edges. Defaults to
    /// <see cref="Capsule.Rendering.HorizontalAlignment.Left"/>.
    /// </summary>
    public HorizontalAlignment HorizontalAlignment { get; set; }

    /// <summary>
    /// Where the run sits between the box's top and bottom edges. Defaults to
    /// <see cref="Capsule.Rendering.VerticalAlignment.Top"/>.
    /// </summary>
    public VerticalAlignment VerticalAlignment { get; set; }

    /// <summary>
    /// How many of <see cref="Text"/>'s leading codepoints are drawn, or null, the default, to draw
    /// them all. Zero draws nothing.
    /// </summary>
    /// <remarks>
    /// Layout always runs over the whole text. Revealing a line one codepoint at a time never
    /// reflows it. A line break and a codepoint the font has no glyph for each count as one
    /// codepoint.
    /// </remarks>
    public int? VisibleCharacters { get; set; }

    /// <summary>Whether <see cref="Text"/> is read as markup, defaulting to false to draw it exactly as written.</summary>
    /// <remarks>
    /// <c>[color=#rrggbb]...[/color]</c> tints the glyphs between the tags, and <c>#rrggbbaa</c> adds an
    /// alpha. Spans nest, the innermost wins, and an unclosed span runs to the end. A glyph's colour is its
    /// span's multiplied by <see cref="Color"/>. <c>[[</c> draws one <c>[</c>. Any other bracketed text
    /// draws as written, including an unknown tag, a malformed colour and a stray <c>[/color]</c>.
    /// <see cref="VisibleCharacters"/> counts no tag.
    /// </remarks>
    /// <example>
    /// <code>
    /// new Label(font, "Press [color=#ffd060]Start[/color]") { RichText = true };
    /// </code>
    /// </example>
    public bool RichText { get; set; }

    /// <summary>A tint multiplied into every texel. White by default, which draws the font pages unchanged.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    internal override bool Steps => false;

    internal override TransformSupport Supports => TransformSupport.Scale;

    /// <summary>
    /// The box this label lays its text out in. It uses <see cref="Size"/> on each axis where that
    /// is positive and the measured run elsewhere, placed by <see cref="Pivot"/> at the entity's
    /// position plus <see cref="Offset"/>.
    /// </summary>
    /// <remarks>
    /// The layout is kept until the text, the font, the box width, <see cref="Wrap"/>,
    /// <see cref="HorizontalAlignment"/> or <see cref="RichText"/> changes, in the space and under the
    /// rules <see cref="Renderer.Bounds"/> states.
    /// </remarks>
    public override Rect Bounds
    {
        get
        {
            if (Entity is null)
            {
                return default;
            }

            Transform2D current = RenderTransform;
            LabelLayout layout = Placed(current.Scale, current.TransformPoint(Offset), out Rect placed, out _, out _);

            return layout.IsEmpty ? default : placed;
        }
    }

    /// <summary>
    /// The laid-out text's extent in the entity's units, its widest line by its lines' total height, or zero
    /// when it draws nothing.
    /// </summary>
    /// <remarks>
    /// It ignores the box. A parent can fit itself to what a label filling it draws, or place something
    /// where the text ends.
    /// </remarks>
    public Vector2 ContentSize
    {
        get
        {
            if (Entity is null)
            {
                return default;
            }

            LabelLayout layout = Laid(Vector2.Abs(RenderTransform.Scale), out _, out _);

            return layout.IsEmpty ? default : new Vector2(layout.Width, layout.Height);
        }
    }

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        assets.Add(Font.Pages);
    }

    /// <inheritdoc/>
    protected internal override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        Transform2D current = RenderTransform;
        Vector2 scale = current.Scale;
        Vector2 position = current.TransformPoint(Offset);
        Vector2 previous = PreviousRenderTransform.TransformPoint(Offset);
        LabelLayout layout = Placed(scale, position, out Rect placed, out Vector2 origin, out Vector2 size);
        if (layout.IsEmpty)
        {
            return;
        }

        if (view.Rejects(TextBox.Reach(previous, position, size, Vector2.Abs(scale), Font.Overhang, placed), view.Space))
        {
            return;
        }

        ReadOnlySpan<TextureHandle> pages = Font.Pages;

        // The run is laid out in full whatever the visible count. Revealing it never moves a glyph.
        int visible = VisibleCharacters ?? int.MaxValue;

        // The box is the same shape at both ends of the step. The previous origin differs only by travel.
        Vector2 travel = previous - position;

        foreach (LabelGlyph glyph in layout.Glyphs)
        {
            if (glyph.Index >= visible)
            {
                return;
            }

            Sprite sprite = new(pages[glyph.Page], glyph.Region);
            Vector2 at = origin + (new Vector2(glyph.X, glyph.Y) * scale);
            // A negative scale axis folds into the flip, and the backend draws the magnitude of the extent.
            Vector2 extent = new Vector2(sprite.Region.Width, sprite.Region.Height) * scale;

            view.Add(
                new SpriteIntent(
                    sprite,
                    at + travel,
                    at,
                    PreviousRotation: 0f,
                    Rotation: 0f,
                    Vector2.Abs(extent),
                    FlipX: extent.X < 0f,
                    FlipY: extent.Y < 0f,
                    glyph.Tint == ColorRgba.White ? Color : ColorRgba.Multiply(glyph.Tint, Color)));
        }
    }

    // Brings the layout up to date for the scale it will draw at. box is what the entity gives the label in
    // its own units, and size the same in the drawn space's.
    private LabelLayout Laid(Vector2 scale, out Vector2 box, out Vector2 size)
    {
        box = ScreenEntity.Fill(Entity, Size);
        size = box * scale;

        LabelLayoutKey key = new(
            _textVersion,
            Font,
            size.X > 0f ? TextBox.WrapWidth(size.X, scale.X) : -1,
            Wrap,
            HorizontalAlignment,
            RichText);

        _layout ??= new LabelLayout();
        _layout.Update(_memory.Span, in key);

        return _layout;
    }

    // Lays the run out and places it as an unmirrored scale would, then mirrors the placed box and the pen start
    // about position on each axis the scale turns negative. size is the box in the drawn space's units.
    private LabelLayout Placed(Vector2 scale, Vector2 position, out Rect placed, out Vector2 origin, out Vector2 size)
    {
        Vector2 magnitude = Vector2.Abs(scale);
        LabelLayout layout = Laid(magnitude, out Vector2 box, out size);
        Rect upright = Place(layout, position, magnitude, box, size, out Vector2 pen);

        Vector2 sign = new(scale.X < 0f ? -1f : 1f, scale.Y < 0f ? -1f : 1f);
        Vector2 near = position + ((upright.Position - position) * sign);
        Vector2 far = position + ((new Vector2(upright.Right, upright.Bottom) - position) * sign);
        placed = new Rect(Vector2.Min(near, far), Vector2.Abs(far - near));
        origin = position + ((pen - position) * sign);

        return layout;
    }

    private Rect Place(LabelLayout layout, Vector2 position, Vector2 scale, Vector2 box, Vector2 size, out Vector2 origin)
    {
        Vector2 measured = new Vector2(layout.Width, layout.Height) * scale;

        // An axis the fill changed is the screen entity's rect, which starts at the entity's position.
        Pivot pivot = new(box.X != Size.X ? 0f : Pivot.X, box.Y != Size.Y ? 0f : Pivot.Y);

        return TextBox.Place(position, pivot, TextBox.Extent(size, measured), measured, VerticalAlignment, out origin);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Text", Text);
        panel.Field("RichText", RichText);
        panel.Field("Font", string.Create(CultureInfo.InvariantCulture, $"{Font.LineHeight}px line"));
        panel.Field("Pivot", new Vector2(Pivot.X, Pivot.Y));
        panel.Field("Offset", Offset);
        panel.Field("Size", Size);
        panel.Field("Color", Color);
        panel.Field("Wrap", Wrap);
        panel.Field("HorizontalAlignment", HorizontalAlignment);
        panel.Field("VerticalAlignment", VerticalAlignment);
        panel.Field("VisibleCharacters", VisibleCharacters?.ToString(CultureInfo.InvariantCulture));
    }
}
