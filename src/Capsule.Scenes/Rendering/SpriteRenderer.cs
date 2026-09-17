using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one sprite, one texel per unit of the entity's space: the frame's pivot
/// lands on <see cref="Offset"/> placed by the entity's world transform, and the frame turns about
/// that point and is sized by the transform, a negative axis of whose scale mirrors the frame about
/// the pivot as a flip would. Y-down, in world units under a world root and canvas pixels under a
/// screen one.
/// </summary>
/// <param name="sprite">The frame to draw.</param>
public sealed class SpriteRenderer(Sprite sprite) : Renderer
{
    /// <summary>The frame drawn; swapped to animate, or to change a static frame.</summary>
    public Sprite Sprite { get; set; } = sprite;

    /// <summary>
    /// The point in the entity's own space the frame's pivot lands on, placed by the entity's world
    /// transform; zero by default, which is the entity itself.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// How far the frame repeats, per axis, in the entity's own units; zero, the default, draws it
    /// once. A finite extent covers that much from the frame's low edge towards +X or +Y at a period
    /// of the frame's drawn extent, cropping the copy at the far edge; <see cref="float.PositiveInfinity"/>
    /// repeats without bound on both sides of the frame, and draws it once where nothing culls. A
    /// component that is negative or NaN draws nothing, as a world scale with a zero axis does, and
    /// so does a non-zero tiling on an entity whose <see cref="Entity.WorldTransform"/> is turned:
    /// a tiled frame does not turn.
    /// </summary>
    public Vector2 Tiling { get; set; }

    /// <summary>Whether the frame is mirrored horizontally about its pivot.</summary>
    public bool FlipX { get; set; }

    /// <summary>Whether the frame is mirrored vertically about its pivot.</summary>
    public bool FlipY { get; set; }

    /// <summary>Multiplied into every texel; white, which draws the texture as it is, by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// The rect the frame covers: its region at the entity's world scale, placed by the pivot a
    /// flip has mirrored, and extended to a finite <see cref="Tiling"/> — an unbounded axis reports
    /// the frame's own extent — in the space and on the terms <see cref="Renderer.Bounds"/> states.
    /// A frame on an entity whose world rotation is not zero reports the box of its bounding
    /// circle about the pivot, which covers it at every angle, rather than the tighter rect it
    /// draws. Empty where the frame draws nothing — a region with no texels, a tiling that is not
    /// a tiling, a world scale with a zero axis, or a turned frame that tiles.
    /// </summary>
    public override Rect Bounds
    {
        get
        {
            if (Entity is null || !(Tiling.X >= 0f) || !(Tiling.Y >= 0f))
            {
                return default;
            }

            // The rect at rest, not the one it swept: bounds answer for the entity's current transform.
            Transform2D at = RenderTransform;
            if ((Tiling != Vector2.Zero && at.Rotation != 0f) || !Intent(at, at).TryGetSweptBounds(out Rect frame))
            {
                return default;
            }

            return new Rect(
                frame.Left,
                frame.Top,
                Tiling.X > 0f && float.IsFinite(Tiling.X) ? frame.Left + Tiling.X : frame.Right,
                Tiling.Y > 0f && float.IsFinite(Tiling.Y) ? frame.Top + Tiling.Y : frame.Bottom);
        }
    }

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

        view.Add(Intent(PreviousRenderTransform, RenderTransform), Tiling);
    }

    // A negative axis of the world scale is a mirror about the pivot, which is what a flip is, so
    // it folds into the flip and the extent stays the magnitude the backend draws.
    private SpriteIntent Intent(in Transform2D previous, in Transform2D current)
    {
        Sprite frame = Sprite;
        Vector2 size = new Vector2(frame.Region.Width, frame.Region.Height) * current.Scale;

        return new SpriteIntent(
            frame,
            previous.Apply(Offset),
            current.Apply(Offset),
            previous.Rotation,
            current.Rotation,
            Vector2.Abs(size),
            FlipX ^ (size.X < 0f),
            FlipY ^ (size.Y < 0f),
            Color);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        TextureRegion region = Sprite.Region;
        panel.Field("Sprite", string.Create(CultureInfo.InvariantCulture, $"({region.X}, {region.Y}) {region.Width}x{region.Height}"));
        panel.Field("Offset", Offset);
        panel.Field("Tiling", Tiling);
        panel.Field("Color", Color);
        panel.Toggle("FlipX", FlipX, on => FlipX = on);
        panel.Toggle("FlipY", FlipY, on => FlipY = on);
    }
}
