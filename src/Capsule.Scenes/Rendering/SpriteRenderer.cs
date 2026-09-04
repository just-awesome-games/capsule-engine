using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Scenes.Rendering;

/// <summary>
/// Draws its entity as one sprite, one texel per world unit until <see cref="Scale"/> says
/// otherwise. The frame's pivot lands on the entity's position plus <see cref="Offset"/>. World
/// units, Y-down.
/// </summary>
/// <param name="sprite">The frame to draw.</param>
public sealed class SpriteRenderer(Sprite sprite) : Renderer
{
    /// <summary>The frame drawn; swapped to animate, or to change a static frame.</summary>
    public Sprite Sprite { get; set; } = sprite;

    /// <summary>
    /// Added to the entity's position to give the point the frame's pivot lands on. World units;
    /// zero by default.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// Multiplies the frame's drawn extent per axis, about its pivot; <see cref="Vector2.One"/>,
    /// one texel per world unit, by default. Presentation only — a collider never reads it — and
    /// not a mirror: <see cref="FlipX"/> and <see cref="FlipY"/> are. A component that is not
    /// positive and finite draws nothing.
    /// </summary>
    public Vector2 Scale { get; set; } = Vector2.One;

    /// <summary>Whether the frame is mirrored horizontally about its pivot.</summary>
    public bool FlipX { get; set; }

    /// <summary>Whether the frame is mirrored vertically about its pivot.</summary>
    public bool FlipY { get; set; }

    /// <summary>Multiplied into every texel; white, which draws the texture as it is, by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <inheritdoc/>
    public override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        Entity entity = Entity!;
        Sprite frame = Sprite;

        view.Add(new SpriteIntent(
            frame,
            entity.PreviousPosition + Offset,
            entity.Position + Offset,
            new Vector2(frame.Region.Width, frame.Region.Height) * Scale,
            FlipX,
            FlipY,
            Color));
    }
}
