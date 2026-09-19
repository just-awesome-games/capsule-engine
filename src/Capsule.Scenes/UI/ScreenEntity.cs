using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.UI;

/// <summary>
/// An entity on the frame's screen layer. Its <see cref="Entity.Position"/> is canvas pixels, Y-down, from
/// the point <see cref="Anchor"/> names, and every renderer it holds draws over the world layer whatever the
/// two layers' bands are. Build an interface from these and the world from plain entities. A screen entity is
/// always a root and rejects a <see cref="Entity.Parent"/>. A plain entity parented under one joins the
/// group and draws on the screen layer in canvas pixels from this entity's anchored point.
/// <para>
/// The canvas is the run's (<see cref="Run.Canvas"/>), not the window's. A corner-anchored element keeps
/// its distance from that corner at every window size.
/// </para>
/// </summary>
public class ScreenEntity : Entity
{
    /// <param name="anchor">The point on the canvas <paramref name="offset"/> is measured from.</param>
    /// <param name="offset">
    /// Canvas pixels from that point to this entity's position. A negative component measures back towards
    /// the canvas's origin.
    /// </param>

    public ScreenEntity(Anchor anchor, Vector2 offset)
        : base(offset) =>
        Anchor = anchor;

    /// <summary>
    /// The point on the canvas <see cref="Entity.Position"/> is measured from, as a fraction of the
    /// canvas on each axis.
    /// </summary>
    public Anchor Anchor
    {
        get;

        set
        {
            Guard.Finite(value.X, nameof(value));
            Guard.Finite(value.Y, nameof(value));
            field = value;
        }
    }

    internal sealed override RenderSpace OwnSpace => RenderSpace.Screen;

    // Reads zero before this entity is in a scene, because the run's canvas is reached through the scene.
    internal sealed override Vector2 OwnSpaceOrigin => Anchor.On(SceneOrNull?.RunOrNull?.Canvas ?? Vector2.Zero);
}
