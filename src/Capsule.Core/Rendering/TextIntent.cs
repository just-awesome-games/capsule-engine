using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One run of text as the simulation wants it drawn, laid out inside a box.
/// <see cref="FrameView.Add(in TextIntent)"/> lays it out and adds one
/// <see cref="SpriteIntent"/> per glyph to the list it is drawing into, so text interpolates, culls
/// and counts exactly as sprites do.
/// <para>
/// <see cref="Position"/> is the box's alignment point: the corner, edge midpoint or centre that
/// <see cref="HorizontalAlignment"/> and <see cref="VerticalAlignment"/> name, which is also where
/// the text sits inside the box. Left and top, the defaults, make it the box's top-left corner.
/// </para>
/// </summary>
/// <param name="Font">The font the run is laid out and drawn with; null draws nothing.</param>
/// <param name="Text">
/// The text drawn; null or empty draws nothing. <c>\n</c> starts a new line one
/// <see cref="BitmapFont.LineHeight"/> down, <c>\r</c> is ignored, and a codepoint the font carries
/// no glyph for draws nothing and advances nothing.
/// </param>
/// <param name="PreviousPosition">
/// Where the box's alignment point sat at the end of the previous step, in the drawn space's units.
/// </param>
/// <param name="Position">Where the box's alignment point sits now, in the drawn space's units.</param>
/// <param name="Scale">
/// Multiplies font pixels into the drawn space's units per axis. <see cref="Vector2.One"/> draws one
/// font pixel per unit; a non-positive component draws nothing.
/// </param>
/// <param name="Color">
/// Multiplied into every texel of every glyph; <see cref="ColorRgba.White"/> draws the pages as
/// they are.
/// </param>
public readonly record struct TextIntent(
    BitmapFont? Font,
    string? Text,
    Vector2 PreviousPosition,
    Vector2 Position,
    Vector2 Scale,
    ColorRgba Color)
{
    /// <summary>
    /// The box the run is laid out in, in the drawn space's units. A non-positive component is the
    /// measured run on that axis, which is the default on both: the box is then the text itself, so
    /// centring it centres the run on <see cref="Position"/>.
    /// </summary>
    public Vector2 Size { get; init; }

    /// <summary>
    /// Whether a line wider than <see cref="Size"/> breaks inside the box. Wrapping needs a positive
    /// <see cref="Size"/> on X; without one there is no box to wrap in.
    /// </summary>
    public TextWrap Wrap { get; init; }

    /// <summary>Where each line sits between the box's left and right edges.</summary>
    public HorizontalAlignment HorizontalAlignment { get; init; }

    /// <summary>Where the run sits between the box's top and bottom edges.</summary>
    public VerticalAlignment VerticalAlignment { get; init; }

    /// <summary>
    /// How many of the text's leading codepoints are drawn, or null — the default — for all of them.
    /// Zero draws nothing and a count past the end draws everything. Layout runs over the whole text
    /// whatever this says, so revealing a run one codepoint at a time never reflows it; the count is
    /// in codepoints of <see cref="Text"/>, so a line break and a codepoint the font has no glyph for
    /// each spend one.
    /// </summary>
    public int? VisibleCharacters { get; init; }

    /// <summary>
    /// The box this run occupies around <see cref="Position"/>, in the drawn space's units; empty
    /// where the run draws nothing. A run with no <see cref="Size"/> measures itself, so this is the
    /// text's own extent.
    /// </summary>
    public ViewBounds Bounds => TryPlace(out TextPlacement placed) ? placed.Box : default;

    // The resolved layout, or false where the run draws nothing at all.
    internal bool TryPlace(out TextPlacement placement)
    {
        placement = default;

        if (Font is not { } font || string.IsNullOrEmpty(Text))
        {
            return false;
        }

        // Only a positive scale turns a box in space units back into the font pixels the layout
        // wants; a degenerate one still submits its glyphs, which cull on their own extent.
        static int WrapWidth(float extent, float scale) => scale > 0f && extent > 0f ? (int)(extent / scale) : 0;

        int wrapWidth = Wrap == TextWrap.Word ? WrapWidth(Size.X, Scale.X) : 0;
        Vector2 measured = font.Measure(Text, wrapWidth) * Scale;

        Vector2 box = new(
            Size.X > 0f ? Size.X : measured.X,
            Size.Y > 0f ? Size.Y : measured.Y);

        Vector2 share = new(Share(HorizontalAlignment), Share(VerticalAlignment));
        Vector2 topLeft = Position - (box * share);

        placement = new TextPlacement(
            font,
            new Vector2(topLeft.X, topLeft.Y + ((box.Y - measured.Y) * share.Y)),
            new ViewBounds(topLeft.X, topLeft.Y, topLeft.X + box.X, topLeft.Y + box.Y),
            WrapWidth(box.X, Scale.X),
            Wrap,
            HorizontalAlignment);

        return true;
    }

    private static float Share(HorizontalAlignment alignment) => alignment switch
    {
        HorizontalAlignment.Center => 0.5f,
        HorizontalAlignment.Right => 1f,
        _ => 0f,
    };

    private static float Share(VerticalAlignment alignment) => alignment switch
    {
        VerticalAlignment.Middle => 0.5f,
        VerticalAlignment.Bottom => 1f,
        _ => 0f,
    };
}

// A text intent's resolved box: where its first line's pen starts, the box around it, and what the
// glyph walk still needs. Origin and Box are in the drawn space's units; BoxWidth is font pixels.
internal readonly record struct TextPlacement(
    BitmapFont Font,
    Vector2 Origin,
    ViewBounds Box,
    int BoxWidth,
    TextWrap Wrap,
    HorizontalAlignment Alignment);
