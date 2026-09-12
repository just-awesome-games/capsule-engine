using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Scenes.Rendering;

/// <summary>
/// A component that draws. A scene walks its renderers by draw key — its entity's
/// <see cref="Entity.ZIndex"/> plus this renderer's <see cref="ZIndex"/>, lowest first — and an
/// equal key keeps entity order and, within an entity, attachment order, so what draws later
/// covers what drew earlier.
/// </summary>
public abstract class Renderer : Component
{
    /// <summary>
    /// This renderer's place inside its entity, relative to the entity's own
    /// <see cref="Entity.ZIndex"/>: the two sum, as a <see cref="long"/> with neither side clamped,
    /// to the key the scene draws by, and the higher sum draws later. Zero by default, which draws
    /// it in attachment order among its entity's other unoffset renderers. Written from inside
    /// <see cref="Draw"/> — like any change to what the scene holds — it orders the next step's
    /// frame rather than the one being drawn.
    /// </summary>
    public int ZIndex
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Entity?.Scene?.InvalidateRenderers();
        }
    }

    /// <summary>
    /// The rect this renderer covers, in its entity's space — world units on a world entity, canvas
    /// pixels with the anchor resolved on a screen one. Read from the entity's current position, so a
    /// moving entity reports where the next frame places it rather than where the last one drew it.
    /// <para>
    /// The box the renderer reports, not the texels it happens to draw: a sized label whose text is
    /// hidden still reports its box. Empty while attached to no entity, and empty by default, which is
    /// what a renderer with no rect to report leaves it; a renderer reporting empty bounds is never
    /// under the pointer, so it cannot be picked.
    /// </para>
    /// </summary>
    public virtual Rect Bounds => default;

    /// <summary>
    /// Where this renderer's entity sits in the space this renderer draws in: the entity's
    /// <see cref="Entity.Position"/> in world units on a world entity, and canvas pixels with the
    /// <see cref="ScreenEntity.Anchor"/> already resolved on a screen one. This is what an intent's
    /// position and <see cref="Bounds"/> are measured from, so a renderer of the game's own places
    /// itself the same way on either layer. Zero while attached to no entity.
    /// </summary>
    protected Vector2 RenderPosition => Entity is { } entity ? entity.Position + entity.SpaceOrigin : Vector2.Zero;

    /// <summary>
    /// <see cref="RenderPosition"/> as of the previous step, which is what an intent interpolates from.
    /// Zero while attached to no entity.
    /// </summary>
    protected Vector2 PreviousRenderPosition =>
        Entity is { } entity ? entity.PreviousPosition + entity.SpaceOrigin : Vector2.Zero;

    /// <summary>
    /// Writes this renderer's intent onto the frame under construction — already cleared, with
    /// the camera set. Called once per step, after the whole scene has stepped.
    /// </summary>
    public abstract void Draw(FrameView view);
}
