using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one run of text inside a box, one font pixel per unit of the entity's space
/// times the scale of the entity's <see cref="Entity.WorldTransform"/>. Y-down, in world units under a world root
/// and canvas pixels under a screen one.
/// <para>
/// The entity's position plus <see cref="Offset"/> is where the box's <see cref="Pivot"/> sits,
/// its top-left corner by default. <see cref="HorizontalAlignment"/> and
/// <see cref="VerticalAlignment"/> then move the text inside that box and never the box itself. A
/// <see cref="Size"/> of zero makes the box the measured run, so a label centred on its point takes
/// <see cref="Capsule.Rendering.Pivot.Center"/>.
/// </para>
/// </summary>
/// <param name="font">The font the run is drawn with.</param>
/// <param name="text">The text drawn; empty, which draws nothing, by default.</param>
public sealed class Label(BitmapFont font, string text = "") : Renderer
{
    /// <summary>The font the run is laid out and drawn with.</summary>
    public BitmapFont Font { get; set; } = font ?? throw new ArgumentNullException(nameof(font));

    // The text in the form it was last written: a string, or a span copied into a buffer this label
    // owns and grows only when a longer span arrives. _text is the string form, null after a span
    // write until Text materialises it; _memory is what layout reads, so neither path allocates in
    // Draw.
    private string? _text = text ?? throw new ArgumentNullException(nameof(text));
    private ReadOnlyMemory<char> _memory = text.AsMemory();
    private char[] _buffer = [];

    /// <summary>
    /// The text drawn; empty draws nothing. <c>\n</c> starts a new line, and a codepoint
    /// <see cref="Font"/> carries no glyph for draws nothing and advances nothing. After
    /// <see cref="SetText"/> the getter returns the same characters as a string, built on the first
    /// read after each write.
    /// </summary>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
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
    /// Sets <see cref="Text"/> from a span, allocating nothing once the label has held a run this
    /// long: the characters are copied into a buffer the label owns, so the caller's span is free
    /// to change the moment this returns. For a readout rewritten every frame; a string assignment
    /// is the common case.
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
    /// Added to the entity's position to give the point the box's <see cref="Pivot"/> sits on. In the
    /// entity's own units; zero by default.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// The point on the box that sits on the entity's position plus <see cref="Offset"/>;
    /// <see cref="Capsule.Rendering.Pivot.TopLeft"/> by default.
    /// </summary>
    public Pivot Pivot { get; set; }

    /// <summary>
    /// The box the run is laid out in, in the entity's units. A non-positive component is the measured
    /// run on that axis, which is the default on both. A positive X is what <see cref="Wrap"/> wraps
    /// inside.
    /// </summary>
    public Vector2 Size { get; set; }

    /// <summary>
    /// Whether a line wider than <see cref="Size"/> breaks inside the box;
    /// <see cref="TextWrap.None"/> by default.
    /// </summary>
    public TextWrap Wrap { get; set; }

    /// <summary>
    /// Where each line sits between the box's left and right edges;
    /// <see cref="Capsule.Rendering.HorizontalAlignment.Left"/> by default.
    /// </summary>
    public HorizontalAlignment HorizontalAlignment { get; set; }

    /// <summary>
    /// Where the run sits between the box's top and bottom edges;
    /// <see cref="Capsule.Rendering.VerticalAlignment.Top"/> by default.
    /// </summary>
    public VerticalAlignment VerticalAlignment { get; set; }

    /// <summary>
    /// How many of <see cref="Text"/>'s leading codepoints are drawn, or null — the default — for all
    /// of them. Zero draws nothing. Layout runs over the whole text whatever this is, so revealing a
    /// line one codepoint at a time never reflows it; a line break and a codepoint the font has no
    /// glyph for each spend one.
    /// </summary>
    public int? VisibleCharacters { get; set; }

    /// <summary>Multiplied into every texel; white, which draws the pages as they are, by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// The box this label lays its text out in: <see cref="Size"/> on an axis it is positive on and the
    /// measured run on one it is not, placed by <see cref="Pivot"/> on the entity's position plus
    /// <see cref="Offset"/>. Laid out on each read, in the space and on the terms
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

        return new TextIntent(Font, _memory, previous.Apply(Offset), current.Apply(Offset), current.Scale, Color)
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
