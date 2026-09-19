using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One run of text as the simulation wants it drawn, laid out inside a box.
/// <see cref="FrameView.Add(in TextIntent)"/> adds one <see cref="SpriteIntent"/> per glyph, so text
/// interpolates, culls and counts as sprites do. <see cref="Pivot"/> places the box, and the two
/// alignments move the text inside it.
/// </summary>
/// <param name="Font">The font the run is laid out and drawn with. Null draws nothing.</param>
/// <param name="Text">
/// The text drawn, where empty draws nothing. It is read while the intent is laid out and not after,
/// and a caller may hand over a buffer it rewrites next frame. <c>\n</c> starts a new line, <c>\r</c>
/// is ignored, and a codepoint the font carries no glyph for draws and advances nothing.
/// </param>
/// <param name="PreviousPosition">Where the box's <see cref="Pivot"/> sat at the end of the previous step.</param>
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

    /// <summary>
    /// The box the run is laid out in, in the drawn space's units. A non-positive component, which is
    /// the default on both axes, takes the measured run on that axis, so the box becomes the text.
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
    /// How many of the text's leading codepoints are drawn. Null, the default, draws all of them, zero
    /// draws nothing, and a count past the end draws everything. Layout runs over the full text
    /// whatever this says, so revealing a run one codepoint at a time does not reflow it. The count is
    /// in codepoints of <see cref="Text"/>. A line break and a codepoint the font has no glyph for each
    /// spend one.
    /// </summary>
    public int? VisibleCharacters { get; init; }

    /// <summary>
    /// The box this run is laid out in, placed by <see cref="Pivot"/> on <see cref="Position"/>. Empty
    /// with no font or no text. Reading it lays the run out.
    /// </summary>
    public Rect Bounds => TryPlace(out TextPlacement placed) ? placed.Box : default;

    // The resolved layout, or false where the run draws nothing.
    internal bool TryPlace(out TextPlacement placement)
    {
        placement = default;

        if (Font is not { } font || Text.IsEmpty)
        {
            return false;
        }

        // Only a positive scale converts a box in space units back into the font pixels layout wants. A
        // degenerate scale still submits its glyphs, which cull on their own extent.
        static int WrapWidth(float extent, float scale) => scale > 0f && extent > 0f ? (int)(extent / scale) : 0;

        // Measuring the run costs a pass over the text, so it is skipped when the box is given on both
        // axes and the run sits at its top. Nothing then reads the extent.
        float share = Share(VerticalAlignment);
        Vector2 measured = Size.X > 0f && Size.Y > 0f && share == 0f
            ? Vector2.Zero
            : font.Measure(Text.Span, Wrap == TextWrap.Word ? WrapWidth(Size.X, Scale.X) : 0) * Scale;

        Vector2 box = new(
            Size.X > 0f ? Size.X : measured.X,
            Size.Y > 0f ? Size.Y : measured.Y);

        Vector2 topLeft = Position - Pivot.On(box);

        placement = new TextPlacement(
            font,
            new Vector2(topLeft.X, topLeft.Y + ((box.Y - measured.Y) * share)),
            new Rect(topLeft.X, topLeft.Y, topLeft.X + box.X, topLeft.Y + box.Y),
            WrapWidth(box.X, Scale.X),
            Wrap,
            HorizontalAlignment);

        return true;
    }

    private static float Share(VerticalAlignment alignment) => alignment switch
    {
        VerticalAlignment.Middle => 0.5f,
        VerticalAlignment.Bottom => 1f,
        _ => 0f,
    };
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
