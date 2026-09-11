using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Scenes.Rendering;

/// <summary>
/// Draws its entity as one run of text, one font pixel per world unit until <see cref="Scale"/>
/// says otherwise. The run's origin — the top-left corner of its first line — lands on the entity's
/// position plus <see cref="Offset"/>. World units, Y-down.
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
    /// Added to the entity's position to give the point the run's origin lands on. World units;
    /// zero by default. Centre a run by offsetting it half of <see cref="BitmapFont.Measure"/>.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// Multiplies font pixels into world units per axis; <see cref="Vector2.One"/>, one font pixel
    /// per world unit, by default. A component that is not positive and finite draws nothing.
    /// </summary>
    public Vector2 Scale { get; set; } = Vector2.One;

    /// <summary>Multiplied into every texel; white, which draws the pages as they are, by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

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

        Entity entity = Entity!;

        view.Add(new TextIntent(
            Font,
            Text,
            entity.PreviousPosition + Offset,
            entity.Position + Offset,
            Scale,
            Color));
    }
}
