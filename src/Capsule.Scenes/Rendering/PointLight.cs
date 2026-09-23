using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity's light additively into the frame's light map as its
/// <see cref="PointLight.Sprite"/>.
/// </summary>
/// <remarks>
/// Two lights add, and a light brightens the sprites it overlaps. <see cref="Renderer.ZIndex"/> has
/// no effect on a light. Drawing under a <see cref="ScreenEntity"/> throws, because the screen
/// layer is never lit. A cone or any other shape is a <see cref="Rendering.Sprite"/> from a sheet,
/// turned by the entity.
/// </remarks>
/// <example>
/// <code>
/// Add(new SpriteRenderer(CapsuleAssets.Sprites.Props.Lamp.Frames.Lit));
/// Add(new PointLight { Radius = 48f, Color = ColorRgba.Orange });
/// </code>
/// </example>
public sealed class PointLight : Renderer
{
    private float _radius = 32f;
    private float _intensity = 1f;

    /// <summary>
    /// World units from the light's point to its falloff edge, 32 by default. The larger axis of the
    /// entity's world scale multiplies it.
    /// </summary>
    public float Radius
    {
        get => _radius;
        set
        {
            if (!(value >= 0f) || !float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A light's radius must be finite and non-negative. Set it to zero or above.");
            }

            _radius = value;
        }
    }

    /// <summary>The light's colour, added into the light map scaled by its alpha. White by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// How many times the colour is added, 1 by default. Zero adds nothing.
    /// </summary>
    /// <remarks>
    /// A light of one on a white ambient brightens what it reaches towards white, up to twice the
    /// authored colour. Above one widens that core, one quad per whole unit and 16 at most.
    /// </remarks>
    public float Intensity
    {
        get => _intensity;
        set
        {
            if (!(value >= 0f) || !float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A light's intensity must be finite and non-negative. Set it to zero or above.");
            }

            _intensity = value;
        }
    }

    /// <summary>The frame the light is drawn as. <see cref="Rendering.Sprite.Light"/> by default, the engine's radial falloff.</summary>
    public Sprite Sprite { get; set; } = Sprite.Light;

    /// <summary>The point in the entity's own space where the light sits, placed by the entity's world transform. Zero by default.</summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// The rect the light covers, under the rules <see cref="Renderer.Bounds"/> states. Reads empty
    /// when the light draws nothing.
    /// </summary>
    public override Rect Bounds =>
        Entity is not null && Intent(PreviousRenderTransform, RenderTransform).ToSprite(Color).TryGetSweptBounds(out Rect box)
            ? box
            : default;

    /// <inheritdoc/>
    protected internal override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Add(Intent(PreviousRenderTransform, RenderTransform));
    }

    // The radius scales with the render transform's scale, the larger axis, as a turned circle would.
    private LightIntent Intent(in Transform2D previous, in Transform2D current)
    {
        Vector2 scale = current.Scale;
        float radius = Radius * MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Y));

        return new LightIntent(
            Sprite,
            previous.TransformPoint(Offset),
            current.TransformPoint(Offset),
            previous.Rotation,
            current.Rotation,
            radius,
            Color,
            Intensity);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Radius", Radius);
        panel.Field("Color", Color);
        panel.Field("Intensity", Intensity);
        panel.Field("Offset", Offset);

        if (Entity?.SceneOrNull?.Ambient == ColorRgba.White)
        {
            panel.Field("Ambient", "white: nothing to light");
        }
    }
}
