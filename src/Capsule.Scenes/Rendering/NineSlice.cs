using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Scenes.Rendering;

/// <summary>
/// Draws its entity as one nine-sliced panel: a frame cut into corners, edges and a middle by
/// <see cref="Insets"/>, laid over <see cref="Size"/> with the corners at their own texel size and
/// everything between them stretched. The panel's top-left corner lands on the entity's position plus
/// <see cref="Offset"/>. Y-down, in world units on a world entity and canvas pixels on a screen one.
/// </summary>
/// <param name="sprite">The frame cut into slices; its pivot is not read.</param>
/// <param name="insets">Where the cuts fall inside the frame's region, in texels.</param>
/// <param name="size">The extent the panel covers; a non-positive axis draws nothing.</param>
public sealed class NineSlice(Sprite sprite, SliceInsets insets, Vector2 size) : Renderer
{
    /// <summary>The frame cut into slices; swapped to change the panel's skin.</summary>
    public Sprite Sprite { get; set; } = sprite;

    /// <summary>Where the cuts fall inside the frame's region, in texels of that region.</summary>
    public SliceInsets Insets { get; set; } = insets;

    /// <summary>
    /// The extent the panel covers, in the entity's units. One texel of a slice covers one unit, so a
    /// size below the insets on an axis keeps both edge slices at their own size and overlaps them.
    /// </summary>
    public Vector2 Size { get; set; } = size;

    /// <summary>
    /// Added to the entity's position to give the panel's top-left corner. In the entity's own units;
    /// zero by default.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>Multiplied into every texel of every slice; white, which draws the frame as it is, by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <inheritdoc/>
    public override Rect Bounds => Entity is null ? default : Intent().Bounds;

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        TextureHandle texture = Sprite.Texture;
        if (texture != default)
        {
            assets.Add(texture);
        }
    }

    /// <inheritdoc/>
    public override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Add(Intent());
    }

    private NineSliceIntent Intent() => new(
        Sprite,
        Insets,
        PreviousRenderPosition + Offset,
        RenderPosition + Offset,
        Size,
        Color);
}
