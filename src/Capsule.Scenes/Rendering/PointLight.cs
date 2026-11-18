using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

/// <summary>
/// Adds its entity's light into the frame's light as its <see cref="PointLight.Sprite"/>, which multiplies
/// every world sprite drawn where it reaches.
/// </summary>
/// <remarks>
/// Two lights add, and a light brightens the sprites it overlaps. <see cref="Renderer.ZIndex"/> has
/// no effect on a light. Drawing under a <see cref="ScreenEntity"/> throws, because the screen
/// layer is never lit. A cone or any other shape is a <see cref="Rendering.Sprite"/> from a sheet,
/// turned, scaled and mirrored by the entity. A flat shape whose outline changes as it plays is a
/// <see cref="PolygonLight"/>. A light draws nothing itself: a visible glow is a sprite of its own, which an
/// <see cref="Entity.Unlit"/> entity keeps bright in the dark.
/// </remarks>
/// <example>
/// <code>
/// Add(new SpriteRenderer(CapsuleAssets.Sprites.Props.LampSheet.Frames.Lit));
/// Add(new PointLight { Radius = 48f, Color = ColorRgba.Orange });
/// </code>
/// </example>
public sealed class PointLight : Renderer
{
    /// <summary>World units from the light's point to its falloff edge along the sprite's wider axis, 32 by default.</summary>
    /// <remarks>
    /// The other axis keeps the sprite's aspect. The entity's world scale multiplies each axis, and a negative axis
    /// mirrors the sprite.
    /// </remarks>
    public float Radius
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    } = 32f;

    /// <summary>The light's colour, added into the light scaled by its alpha. White by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// How many times the colour is added, 1 by default. Zero adds nothing.
    /// </summary>
    /// <remarks>
    /// A light of one on a white ambient doubles the authored colour at its centre. Above one it brightens
    /// further, with no ceiling.
    /// </remarks>
    public float Intensity
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    } = 1f;

    /// <summary>The frame the light is drawn as. <see cref="Rendering.Sprite.Light"/> by default, the engine's radial falloff.</summary>
    /// <remarks>
    /// A light adds the frame's colour times its alpha, as a sprite drawn additively would. Author a falloff in the
    /// alpha of a white image, or in the grey of an opaque one, but not in both, which squares it.
    /// </remarks>
    public Sprite Sprite { get; set; } = Sprite.Light;

    /// <summary>The point in the entity's own space where the light sits, placed by the entity's world transform. Zero by default.</summary>
    public Vector2 Offset { get; set; }

    internal override bool Steps => false;

    /// <summary>
    /// The rect the light covers, under the rules <see cref="Renderer.Bounds"/> states. Reads empty
    /// when the light draws nothing.
    /// </summary>
    public override Rect Bounds =>
        Entity is not null && Intensity > 0f && Intent(RenderTransform, RenderTransform).ToSprite().TryGetSweptBounds(out Rect box)
            ? box
            : default;

    /// <inheritdoc/>
    protected internal override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Add(Intent(PreviousRenderTransform, RenderTransform));
    }

    // The render transform's scale stretches each axis of the extent. A negative axis folds into the flip, as a
    // sprite's does.
    private LightIntent Intent(in Transform2D previous, in Transform2D current)
    {
        Vector2 size = Extent(Sprite, Radius) * current.Scale;

        return new LightIntent(
            Sprite,
            previous.TransformPoint(Offset),
            current.TransformPoint(Offset),
            previous.Rotation,
            current.Rotation,
            Vector2.Abs(size),
            size.X < 0f,
            size.Y < 0f,
            Color,
            Intensity);
    }

    // The size a light sprite is drawn at for a radius at unit scale: its wider axis spans 2 * radius and the other
    // keeps the region's aspect. A particle light shares it.
    internal static Vector2 Extent(in Sprite sprite, float radius)
    {
        TextureRegion region = sprite.Region;
        float span = 2f * radius;

        return region.Width <= 0 || region.Height <= 0 ? Vector2.Zero
            : region.Width >= region.Height ? new Vector2(span, span * region.Height / region.Width)
            : new Vector2(span * region.Width / region.Height, span);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Radius", Radius);
        panel.Field("Color", Color);
        panel.Field("Intensity", Intensity);
        panel.Field("Offset", Offset);
    }
}
