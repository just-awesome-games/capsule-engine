using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity's light into the frame's light map.
/// <example>
/// <code>
/// Add(new SpriteRenderer(CapsuleAssets.Sprites.Props.Lamp.Frames.Lit));
/// Add(new PointLight { Radius = 48f, Color = ColorRgba.Orange });
/// </code>
/// </example>
/// The light is drawn as its sprite, additively, into the frame's light map, so two lights add and a
/// light lights the sprites it sits on. <see cref="Renderer.ZIndex"/> orders nothing for a light, since additive
/// light is order-free. Under a <see cref="ScreenEntity"/> drawing throws: the screen layer is
/// never lit. A cone or any other shape is a <see cref="Rendering.Sprite"/> from a sheet, turned by the
/// entity.
/// </summary>
public sealed class PointLight : Renderer
{
    private float _radius = 32f;
    private float _intensity = 1f;

    /// <summary>World units from the entity to the light's falloff edge. Must be finite and non-negative.</summary>
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

    /// <summary>The light's colour, added into the light map at full strength. White by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// How many times the colour is added. Must be finite and non-negative. A light of one on a white
    /// ambient brightens what it reaches towards white, up to twice the authored colour. Above one
    /// widens that core, one quad per whole unit and 16 at most.
    /// </summary>
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

    /// <summary>The point in the entity's own space the light is placed at, placed by the entity's world transform.</summary>
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
    public override void Draw(FrameView view)
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
