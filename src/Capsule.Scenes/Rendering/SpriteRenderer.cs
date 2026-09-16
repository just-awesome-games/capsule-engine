using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one sprite, one texel per unit of the entity's space until
/// <see cref="Scale"/> says otherwise. The frame's pivot lands on the entity's position plus
/// <see cref="Offset"/>. Y-down, in world units on a world entity and canvas pixels on a screen one.
/// </summary>
/// <param name="sprite">The frame to draw.</param>
public sealed class SpriteRenderer(Sprite sprite) : Renderer
{
    /// <summary>The frame drawn; swapped to animate, or to change a static frame.</summary>
    public Sprite Sprite { get; set; } = sprite;

    /// <summary>
    /// Added to the entity's position to give the point the frame's pivot lands on. In the entity's
    /// own units; zero by default.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// Multiplies the frame's drawn extent per axis, about its pivot; <see cref="Vector2.One"/>,
    /// one texel per world unit, by default. Presentation only — a collider never reads it — and
    /// not a mirror: <see cref="FlipX"/> and <see cref="FlipY"/> are. A component that is not
    /// positive and finite draws nothing.
    /// </summary>
    public Vector2 Scale { get; set; } = Vector2.One;

    /// <summary>
    /// How far the frame repeats, per axis, in the entity's own units; zero, the default, draws it
    /// once. A finite extent covers that much from the frame's low edge towards +X or +Y at a period
    /// of the frame's drawn extent, cropping the copy at the far edge; <see cref="float.PositiveInfinity"/>
    /// repeats without bound on both sides of the frame, and draws it once where nothing culls. A
    /// component that is negative or NaN draws nothing, as a scale that is not positive does.
    /// </summary>
    public Vector2 Tiling { get; set; }

    /// <summary>Whether the frame is mirrored horizontally about its pivot.</summary>
    public bool FlipX { get; set; }

    /// <summary>Whether the frame is mirrored vertically about its pivot.</summary>
    public bool FlipY { get; set; }

    /// <summary>Multiplied into every texel; white, which draws the texture as it is, by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// The rect the frame covers: its region at <see cref="Scale"/>, placed by the pivot a flip has
    /// mirrored, and extended to a finite <see cref="Tiling"/> — an unbounded axis reports the frame's
    /// own extent — in the space and on the terms <see cref="Renderer.Bounds"/> states. Empty where
    /// the frame draws nothing — a region with no texels, or a scale or tiling that is not a scale
    /// or tiling.
    /// </summary>
    public override Rect Bounds
    {
        get
        {
            if (Entity is null || !(Tiling.X >= 0f) || !(Tiling.Y >= 0f))
            {
                return default;
            }

            // The rect at rest, not the one it swept: bounds answer for the entity's current position.
            Vector2 position = RenderPosition + Offset;

            if (!Intent(position, position).TryGetSweptBounds(out Rect frame))
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

        view.Add(Intent(PreviousRenderPosition + Offset, RenderPosition + Offset), Tiling);
    }

    private SpriteIntent Intent(Vector2 previousPosition, Vector2 position)
    {
        Sprite frame = Sprite;

        return new SpriteIntent(
            frame,
            previousPosition,
            position,
            new Vector2(frame.Region.Width, frame.Region.Height) * Scale,
            FlipX,
            FlipY,
            Color);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        TextureRegion region = Sprite.Region;
        panel.Field("Sprite", string.Create(CultureInfo.InvariantCulture, $"({region.X}, {region.Y}) {region.Width}x{region.Height}"));
        panel.Field("Offset", Offset);
        panel.Field("Scale", Scale);
        panel.Field("Tiling", Tiling);
        panel.Field("Color", Color);
        panel.Toggle("FlipX", FlipX, on => FlipX = on);
        panel.Toggle("FlipY", FlipY, on => FlipY = on);
    }
}
