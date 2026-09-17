using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

/// <summary>
/// A component that draws. A scene walks its renderers by draw key — its entity's
/// <see cref="Entity.ZIndex"/> summed up the ancestry plus this renderer's <see cref="ZIndex"/>,
/// lowest first — and an equal key keeps entity order and, within an entity, attachment order, so
/// what draws later covers what drew earlier. A renderer is placed by its entity's
/// <see cref="RenderTransform"/> and carries no turn or size of its own.
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
    /// The rect this renderer covers, in the space it draws in — world units under a world root,
    /// canvas pixels with the anchor resolved under a screen one. Read from the entity's current
    /// world transform, so a moving entity reports where the next frame places it rather than where
    /// the last one drew it.
    /// <para>
    /// The box the renderer reports, not the texels it happens to draw: a sized label whose text is
    /// hidden still reports its box. Empty while attached to no entity, and empty by default, which is
    /// what a renderer with no rect to report leaves it; a renderer reporting empty bounds is never
    /// under the pointer, so it cannot be picked.
    /// </para>
    /// </summary>
    public virtual Rect Bounds => default;

    /// <summary>
    /// The transform this renderer draws by: its entity's <see cref="Entity.WorldTransform"/>, in
    /// world units under a world root and in canvas pixels with the <see cref="ScreenEntity.Anchor"/>
    /// resolved under a screen one. An intent's position is a point in the entity's own space placed
    /// through it — <c>RenderTransform.Apply(Offset)</c> — its rotation is the transform's, and its
    /// extent is its texels or size times the transform's scale, so a renderer of the game's own
    /// places itself the same way on either layer. <see cref="Transform2D.Identity"/> while
    /// attached to no entity.
    /// </summary>
    protected Transform2D RenderTransform => Entity is { } entity ? Placed(entity.World, entity) : Transform2D.Identity;

    /// <summary>
    /// <see cref="RenderTransform"/> as of the previous step, which is what an intent interpolates
    /// from. <see cref="Transform2D.Identity"/> while attached to no entity.
    /// </summary>
    protected Transform2D PreviousRenderTransform => Entity is { } entity ? Placed(entity.PreviousWorld, entity) : Transform2D.Identity;

    private static Transform2D Placed(in Transform2D world, Entity entity) =>
        world.With(world.Position + entity.SpaceOrigin, world.Scale);

    /// <summary>
    /// Writes this renderer's intent onto the frame under construction — already cleared, with
    /// the camera set. Called after scene startup for the initial frame, then after each completed step.
    /// </summary>
    public abstract void Draw(FrameView view);

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Field("ZIndex", ZIndex);
    }
}
