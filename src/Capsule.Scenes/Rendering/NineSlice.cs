using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one nine-sliced panel. <see cref="Insets"/> cut the frame into corners, edges and a
/// middle, laid over <see cref="Size"/> with the corners at their own texel size and everything between them
/// stretched. The panel's top-left corner lands on <see cref="Offset"/> placed by the entity's
/// <see cref="Entity.WorldTransform"/>, and it spans <see cref="Size"/> times that transform's scale. The
/// corners keep their own texel size whatever the scale, and a negative or zero axis of the product draws
/// nothing. A panel cannot turn. A rotation anywhere in the entity's ancestry and this component refuse
/// each other. Coordinates are Y-down, in world units under a world root and canvas
/// pixels under a screen root.
/// </summary>
/// <param name="sprite">The frame to cut into slices. Its pivot is not read.</param>
/// <param name="insets">Where the cuts fall inside the frame's region, in texels.</param>
/// <param name="size">The extent the panel covers. A non-positive axis draws nothing.</param>
public sealed class NineSlice(Sprite sprite, SliceInsets insets, Vector2 size) : Renderer
{
    /// <summary>The frame cut into slices. Swap it to change the panel's skin.</summary>
    public Sprite Sprite { get; set; } = sprite;

    /// <summary>Where the cuts fall inside the frame's region, in texels of that region.</summary>
    public SliceInsets Insets { get; set; } = insets;

    /// <summary>
    /// The extent the panel covers, in the entity's units. One slice texel covers one unit. A size
    /// smaller than the insets on an axis keeps both edge slices at their own size and overlaps them.
    /// </summary>
    public Vector2 Size { get; set; } = size;

    /// <summary>
    /// The point in the entity's own space where the panel's top-left corner lands, placed by the entity's
    /// world transform. Zero by default, which puts the corner on the entity.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>A tint multiplied into every texel of every slice. White by default, which draws the frame unchanged.</summary>
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

    internal override TransformSupport Supports => TransformSupport.Scale;

    private NineSliceIntent Intent()
    {
        Transform2D current = RenderTransform;

        return new NineSliceIntent(Sprite, Insets, PreviousRenderTransform.TransformPoint(Offset), current.TransformPoint(Offset), Size * current.Scale, Color);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        TextureRegion region = Sprite.Region;
        panel.Field("Sprite", string.Create(CultureInfo.InvariantCulture, $"({region.X}, {region.Y}) {region.Width}x{region.Height}"));
        panel.Field("Insets", string.Create(CultureInfo.InvariantCulture, $"{Insets.Left}, {Insets.Top}, {Insets.Right}, {Insets.Bottom}"));
        panel.Field("Size", Size);
        panel.Field("Offset", Offset);
        panel.Field("Color", Color);
    }
}
