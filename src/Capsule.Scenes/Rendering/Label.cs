using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Scenes.Rendering;

/// <summary>
/// Draws its entity as one run of text inside a box, one font pixel per unit of the entity's space
/// until <see cref="Scale"/> says otherwise. Y-down, in world units on a world entity and canvas
/// pixels on a screen one.
/// <para>
/// The entity's position plus <see cref="Offset"/> is the box's alignment point — the corner, edge
/// midpoint or centre that <see cref="HorizontalAlignment"/> and <see cref="VerticalAlignment"/>
/// name — and the text is aligned inside the box the same way. A <see cref="Size"/> of zero makes the
/// box the measured run, so a centred label centres its text on that point.
/// </para>
/// </summary>
/// <param name="font">The font the run is drawn with.</param>
/// <param name="text">The text drawn; empty, which draws nothing, by default.</param>
public sealed class Label(BitmapFont font, string text = "") : Renderer
{
    /// <summary>The font the run is laid out and drawn with.</summary>
    public BitmapFont Font { get; set; } = font ?? throw new ArgumentNullException(nameof(font));

    /// <summary>
    /// The text drawn; empty draws nothing. <c>\n</c> starts a new line, and a codepoint
    /// <see cref="Font"/> carries no glyph for draws nothing and advances nothing.
    /// </summary>
    public string Text { get; set; } = text ?? throw new ArgumentNullException(nameof(text));

    /// <summary>
    /// Added to the entity's position to give the box's alignment point. In the entity's own units;
    /// zero by default.
    /// </summary>
    public Vector2 Offset { get; set; }

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
    /// Where each line sits between the box's left and right edges, and which of those the alignment
    /// point is; <see cref="Capsule.Rendering.HorizontalAlignment.Left"/> by default.
    /// </summary>
    public HorizontalAlignment HorizontalAlignment { get; set; }

    /// <summary>
    /// Where the run sits between the box's top and bottom edges, and which of those the alignment
    /// point is; <see cref="Capsule.Rendering.VerticalAlignment.Top"/> by default.
    /// </summary>
    public VerticalAlignment VerticalAlignment { get; set; }

    /// <summary>
    /// How many of <see cref="Text"/>'s leading codepoints are drawn, or null — the default — for all
    /// of them. Zero draws nothing. Layout runs over the whole text whatever this is, so revealing a
    /// line one codepoint at a time never reflows it; a line break and a codepoint the font has no
    /// glyph for each spend one.
    /// </summary>
    public int? VisibleCharacters { get; set; }

    /// <summary>
    /// Multiplies font pixels into the entity's units per axis; <see cref="Vector2.One"/>, one font
    /// pixel per unit, by default. A component that is not positive and finite draws nothing.
    /// </summary>
    public Vector2 Scale { get; set; } = Vector2.One;

    /// <summary>Multiplied into every texel; white, which draws the pages as they are, by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// The box this label lays its text out in: <see cref="Size"/> on an axis it is positive on and the
    /// measured run on one it is not, around the alignment point. Laid out on each read, in the space
    /// and on the terms <see cref="Renderer.Bounds"/> states.
    /// </summary>
    public override ViewBounds Bounds => Entity is null ? default : Intent().Bounds;

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

    private TextIntent Intent()
    {
        Entity entity = Entity!;
        Vector2 origin = entity.SpaceOrigin + Offset;

        return new TextIntent(
            Font,
            Text,
            entity.PreviousPosition + origin,
            entity.Position + origin,
            Scale,
            Color)
        {
            Size = Size,
            Wrap = Wrap,
            HorizontalAlignment = HorizontalAlignment,
            VerticalAlignment = VerticalAlignment,
            VisibleCharacters = VisibleCharacters,
        };
    }
}
