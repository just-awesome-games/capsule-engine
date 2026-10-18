using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One run of text as the simulation wants it drawn, laid out inside a box.
/// <see cref="FrameView.Add(in TextIntent)"/> adds one <see cref="SpriteIntent"/> per glyph.
/// </summary>
/// <remarks>
/// Text interpolates, culls and counts as sprites do. <see cref="Pivot"/> places the box, and the
/// two alignments move the text inside it.
/// </remarks>
/// <param name="Font">The font the run is laid out and drawn with. Null draws nothing.</param>
/// <param name="Text">
/// The text drawn, where empty draws nothing. It is read only while the intent is laid out, and a
/// caller may reuse the buffer next frame. <c>\n</c> starts a new line, <c>\r</c>
/// is ignored, and a codepoint the font carries no glyph for draws and advances nothing.
/// </param>
/// <param name="PreviousPosition">Where the box's <see cref="Pivot"/> sat at the end of the previous step, in the drawn space's units.</param>
/// <param name="Position">Where the box's <see cref="Pivot"/> sits now, in the drawn space's units.</param>
/// <param name="Scale">Font pixels to the drawn space's units per axis. A non-positive component draws nothing.</param>
/// <param name="Color">Multiplied into every texel of every glyph.</param>
public readonly record struct TextIntent(
    BitmapFont? Font,
    ReadOnlyMemory<char> Text,
    Vector2 PreviousPosition,
    Vector2 Position,
    Vector2 Scale,
    ColorRgba Color)
{
    /// <summary>
    /// The box the run is laid out in, in the drawn space's units. A non-positive component, the
    /// default, takes the measured run's extent on that axis.
    /// </summary>
    public Vector2 Size { get; init; }

    /// <summary>The point on the box that sits on <see cref="Position"/>. Top-left by default.</summary>
    public Pivot Pivot { get; init; }

    /// <summary>Whether a line wider than <see cref="Size"/> breaks inside the box. Wrapping needs a positive <see cref="Size"/> on X.</summary>
    public TextWrap Wrap { get; init; }

    /// <summary>Where each line sits between the box's left and right edges. It does not move the box.</summary>
    public HorizontalAlignment HorizontalAlignment { get; init; }

    /// <summary>Where the run sits between the box's top and bottom edges. It does not move the box.</summary>
    public VerticalAlignment VerticalAlignment { get; init; }

    /// <summary>
    /// How many of the text's leading codepoints are drawn. Null, the default, draws all of them,
    /// zero draws nothing, and a count past the end draws everything.
    /// </summary>
    /// <remarks>
    /// Layout runs over the full text whatever this says. Revealing a run one codepoint at a time
    /// does not reflow it. The count is in codepoints of <see cref="Text"/>. A line break and a
    /// codepoint the font has no glyph for each spend one.
    /// </remarks>
    public int? VisibleCharacters { get; init; }

    /// <summary>The same run over a string, which is the common case. Null or empty draws nothing.</summary>
    public TextIntent(
        BitmapFont? font,
        string? text,
        Vector2 previousPosition,
        Vector2 position,
        Vector2 scale,
        ColorRgba color)
        : this(font, text.AsMemory(), previousPosition, position, scale, color)
    {
    }

    // The box this run is laid out in, placed by Pivot on Position, and empty with no font or no text.
    // Reading it lays the run out.
    internal Rect Bounds => TryPlace(out TextPlacement placed) ? placed.Box : default;

    // The resolved layout, or false where the run draws nothing.
    internal bool TryPlace(out TextPlacement placement)
    {
        placement = default;

        if (Font is not { } font || Text.IsEmpty)
        {
            return false;
        }

        Vector2 measured = TextBox.NeedsMeasure(Size, VerticalAlignment)
            ? font.Measure(Text.Span, Wrap == TextWrap.Word ? TextBox.WrapWidth(Size.X, Scale.X) : 0) * Scale
            : Vector2.Zero;

        Vector2 extent = TextBox.Extent(Size, measured);
        Rect box = TextBox.Place(Position, Pivot, extent, measured, VerticalAlignment, out Vector2 origin);

        placement = new TextPlacement(
            font,
            origin,
            box,
            TextBox.WrapWidth(extent.X, Scale.X),
            Wrap,
            HorizontalAlignment);

        return true;
    }
}

// A text intent's resolved box: where its first line's pen starts, the box around it, and what the glyph
// walk needs. Origin and Box are in the drawn space's units, and BoxWidth is in font pixels.
internal readonly record struct TextPlacement(
    BitmapFont Font,
    Vector2 Origin,
    Rect Box,
    int BoxWidth,
    TextWrap Wrap,
    HorizontalAlignment Alignment);
