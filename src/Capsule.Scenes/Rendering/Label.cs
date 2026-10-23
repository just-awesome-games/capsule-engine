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

    /// <summary>The first laid-out line drawn, at the box's top. Zero by default, which draws from the first line.</summary>
    /// <remarks>
    /// With <see cref="MaxLines"/> it windows the run, and measures and alignment cover only the window.
    /// <see cref="VisibleCharacters"/> still counts from the start of <see cref="Text"/>. A page reveals
    /// from <see cref="LineStart"/> of its first line.
    /// </remarks>
    /// <example>
    /// Typing one page, then turning to the next:
    /// <code>
    /// int end = label.LineStart(label.FirstLine + label.MaxLines!.Value);
    /// for (int shown = label.LineStart(label.FirstLine) + 1; shown &lt;= end; shown++)
    /// {
    ///     label.VisibleCharacters = shown;
    ///     yield return Wait.Ticks(2);
    /// }
    ///
    /// label.FirstLine += label.MaxLines.Value;
    /// </code>
    /// </example>
    public int FirstLine
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>How many lines from <see cref="FirstLine"/> draw, or null, the default, to draw every line from it.</summary>
    public int? MaxLines
    {
        get;
        set
        {
            if (value is { } max)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>
    /// Font pixels added between one line and the next, before the entity's scale. Zero by default, which
    /// sets lines <see cref="BitmapFont.LineHeight"/> apart, and negative is refused.
    /// </summary>
    /// <remarks><see cref="ContentSize"/> and an unsized box include it, between lines and never below the last.</remarks>
    public int LineSpacing
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// A rect the lines flow around, in the box's own units from its top-left corner, or null, the
    /// default, for lines that span the box.
    /// </summary>
    /// <remarks>
    /// Rows run down from the box's top at the line pitch. A line whose row overlaps the rect wraps in the
    /// wider side the rect leaves, the left on a tie, and aligns within it. A row with no side left holds
    /// an empty line. With <see cref="MaxLines"/> the rect stands on every page, line L in row L modulo
    /// <see cref="MaxLines"/>. Turning a page never reflows. It shapes only a word-wrapped,
    /// top-aligned label with a box of positive width, and a rect that leaves no room on any row of a
    /// page shapes nothing.
    /// </remarks>
    public Rect? Exclusion { get; set; }

    /// <summary>
    /// The lines the whole of <see cref="Text"/> lays out to in the box, wraps and <c>\n</c> breaks
    /// included, whatever <see cref="FirstLine"/> and <see cref="MaxLines"/> say.
    /// </summary>
    /// <remarks>
    /// It counts up to the last line that draws a glyph. A trailing blank line is not counted. Reading it
    /// lays the run out under the entity's current scale and box, and at unit scale on no entity.
    /// </remarks>
    public int LineCount => Laid(LayoutScale, out _, out _).LineCount(_memory.Span);

    /// <summary>
    /// Where <paramref name="line"/> starts, as a count of <see cref="VisibleCharacters"/> that draws every
    /// line before it and none of its own.
    /// </summary>
    /// <remarks>
    /// A line a wrap broke at a space starts after the space, and one after a <c>\n</c> after the break. A
    /// line at or past <see cref="LineCount"/> returns the codepoint count of the whole text, which draws
    /// everything. With <see cref="RichText"/> no tag is counted.
    /// </remarks>
    /// <param name="line">A laid-out line, counted from the first line of the text.</param>
    public int LineStart(int line)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);

        return Laid(LayoutScale, out _, out _).LineStart(_memory.Span, line);
    }

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
    /// <see cref="HorizontalAlignment"/>, <see cref="RichText"/>, the line window or
    /// <see cref="LineSpacing"/> changes, in the space and under the rules <see cref="Renderer.Bounds"/>
    /// states.
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
    /// It ignores the box and counts only the lines <see cref="FirstLine"/> and <see cref="MaxLines"/>
    /// draw. A parent can fit itself to what a label filling it draws, or place something where the text
    /// ends.
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
            RichText,
            FirstLine,
            MaxLines,
            LineSpacing,
            Exclusion is { } area && Wrap == TextWrap.Word && VerticalAlignment == VerticalAlignment.Top && size.X > 0f ? area : null);

        _layout ??= new LabelLayout();
        _layout.Update(_memory.Span, in key);

        return _layout;
    }

    // The scale a line query lays the run out at, which is the one the next draw uses.
    private Vector2 LayoutScale => Entity is null ? Vector2.One : Vector2.Abs(RenderTransform.Scale);

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
        panel.Field("FirstLine", FirstLine);
        panel.Field("MaxLines", MaxLines?.ToString(CultureInfo.InvariantCulture));
        panel.Field("LineSpacing", LineSpacing);
        if (Exclusion is { } exclusion)
        {
            panel.Field("Exclusion", exclusion);
        }
    }
}
