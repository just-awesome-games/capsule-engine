using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one run of text inside a box, at one font pixel per unit of the entity's space
/// times the scale of the entity's <see cref="Entity.WorldTransform"/>. Coordinates are Y-down, in world
/// units under a world root and canvas pixels under a screen root.
/// <para>
/// The box's <see cref="Pivot"/> sits at the entity's position plus <see cref="Offset"/>, and defaults to
/// the box's top-left corner. <see cref="HorizontalAlignment"/> and <see cref="VerticalAlignment"/> then
/// move the text inside the box and never move the box. A <see cref="Size"/> of zero makes the box the
/// measured run. To centre a label on its point, use <see cref="Capsule.Rendering.Pivot.Center"/>.
/// </para>
/// </summary>
/// <param name="font">The font the run is drawn with.</param>
/// <param name="text">The text to draw. Empty by default, which draws nothing.</param>
public sealed class Label(BitmapFont font, string text = "") : Renderer
{
    /// <summary>The font the run is laid out and drawn with.</summary>
    public BitmapFont Font { get; set; } = font ?? throw new ArgumentNullException(nameof(font));

    // The text in the form it was last written: either a string, or a span copied into a buffer this label
    // owns and grows only when a longer span arrives. _text holds the string form and is null after a span
    // write until Text builds it. Layout reads _memory, so neither path allocates in Draw.
    private string? _text = text ?? throw new ArgumentNullException(nameof(text));
    private ReadOnlyMemory<char> _memory = text.AsMemory();
    private char[] _buffer = [];

    /// <summary>
    /// The text to draw. Empty draws nothing. A <c>\n</c> starts a new line, and a codepoint
    /// <see cref="Font"/> has no glyph for draws nothing and advances nothing. After
    /// <see cref="SetText"/> the getter returns the same characters as a string, built on the first read
    /// after each write.
    /// </summary>
    public string Text
    {
        get => _text ??= _memory.ToString();

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _text = value;
            _memory = value.AsMemory();
        }
    }

    /// <summary>
    /// Sets <see cref="Text"/> from a span. It copies the characters into a buffer the label owns, so the
    /// caller's span may change as soon as this returns, and it allocates nothing once the label has held a
    /// run this long. Use it for a readout rewritten every frame. A string assignment covers the usual
    /// case.
    /// </summary>
    public void SetText(ReadOnlySpan<char> text)
    {
        if (_buffer.Length < text.Length)
        {
            _buffer = new char[Math.Max(text.Length, _buffer.Length * 2)];
        }

        text.CopyTo(_buffer);
        _memory = _buffer.AsMemory(0, text.Length);
        _text = null;
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
    /// The box the run is laid out in, in the entity's units. A non-positive component uses the measured
    /// run on that axis, which is the default on both axes. <see cref="Wrap"/> wraps inside a positive X.
    /// </summary>
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
    /// How many of <see cref="Text"/>'s leading codepoints are drawn, or null, the default, to draw them
    /// all. Zero draws nothing. Layout always runs over the whole text, so revealing a line one codepoint
    /// at a time never reflows it. A line break and a codepoint the font has no glyph for each count as
    /// one codepoint.
    /// </summary>
    public int? VisibleCharacters { get; set; }

    /// <summary>A tint multiplied into every texel. White by default, which draws the font pages unchanged.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// The box this label lays its text out in. It uses <see cref="Size"/> on each axis where that is
    /// positive and the measured run elsewhere, placed by <see cref="Pivot"/> at the entity's position plus
    /// <see cref="Offset"/>. Each read lays the text out again, in the space and under the rules
    /// <see cref="Renderer.Bounds"/> states.
    /// </summary>
    public override Rect Bounds => Entity is null ? default : Intent().Bounds;

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        assets.Add(Font.Pages);
    }

    /// <inheritdoc/>
    public override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Add(Intent());
    }

    internal override TransformSupport Supports => TransformSupport.Scale;

    private TextIntent Intent()
    {
        Transform2D previous = PreviousRenderTransform;
        Transform2D current = RenderTransform;

        return new TextIntent(Font, _memory, previous.TransformPoint(Offset), current.TransformPoint(Offset), current.Scale, Color)
        {
            Size = Size * current.Scale,
            Pivot = Pivot,
            Wrap = Wrap,
            HorizontalAlignment = HorizontalAlignment,
            VerticalAlignment = VerticalAlignment,
            VisibleCharacters = VisibleCharacters,
        };
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Text", Text);
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
