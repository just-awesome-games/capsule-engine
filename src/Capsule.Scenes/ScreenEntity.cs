using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// An entity on the frame's screen layer: <see cref="Entity.Position"/> is canvas pixels from the
/// point <see cref="Anchor"/> names, Y-down, and every renderer it holds draws over the whole world
/// layer however the two layers are banded. This is what an interface is built from — a menu item, a
/// bar, a panel — and a plain <see cref="Entity"/> is what the world is built from.
/// <para>
/// The canvas is the run's, never the window the player drags, so a corner-anchored element keeps its
/// distance from that corner at every window size. Subclass it for behaviour and attach
/// <see cref="Component"/>s for what composes, exactly as with an entity in the world.
/// </para>
/// </summary>
public class ScreenEntity : Entity
{
    /// <param name="anchor">The point on the canvas <paramref name="offset"/> is measured from.</param>
    /// <param name="offset">
    /// Canvas pixels from that point to this entity's position, which a negative component measures
    /// back towards the canvas's origin.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The anchor or the offset is not finite.</exception>
    public ScreenEntity(Anchor anchor, Vector2 offset)
        : base(offset) =>
        Anchor = anchor;

    /// <summary>
    /// The point on the canvas <see cref="Entity.Position"/> is measured from, as a fraction of the
    /// canvas on each axis.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A fraction is not finite.</exception>
    public Anchor Anchor
    {
        get;

        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "An anchor is a finite fraction of the canvas on each axis.");
            }

            field = value;
        }
    }

    internal sealed override RenderSpace Space => RenderSpace.Screen;

    // Zero before this entity is in a scene, which is where the run's canvas is reached.
    internal sealed override Vector2 SpaceOrigin => Anchor.On(Scene?.Canvas ?? Vector2.Zero);
}
