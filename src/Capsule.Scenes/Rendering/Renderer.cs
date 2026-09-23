using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

/// <summary>
/// A component that draws. A scene walks its renderers by draw key, lowest first. A renderer's key is its
/// entity's <see cref="Entity.ZIndex"/> summed up the ancestry plus this renderer's <see cref="ZIndex"/>.
/// Equal keys keep entity order, and attachment order within an entity, which draws a later renderer
/// over an earlier one. A renderer is placed by its entity's <see cref="RenderTransform"/> and carries no turn or
/// size of its own.
/// </summary>
public abstract class Renderer : Component
{
    /// <summary>
    /// This renderer's offset from its entity's own <see cref="Entity.ZIndex"/>. The two add as a
    /// <see cref="long"/>, with neither side clamped, to give the key the scene draws by, and a higher sum
    /// draws later. Zero by default, which draws it in attachment order among its entity's other unoffset
    /// renderers. Writing it inside <see cref="Draw"/> orders the next step's frame, not the frame being
    /// drawn, as with any change to what the scene holds.
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
            Entity?.SceneOrNull?.InvalidateRenderers();
        }
    }

    /// <summary>Whether this renderer draws. Hiding it changes nothing else.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// The rect this renderer covers, in the space it draws in: world units under a world root, or canvas
    /// pixels with the anchor resolved under a screen root. It reads the entity's current world
    /// transform, which reports where the next frame places a moving entity, not where the last frame
    /// drew it.
    /// <para>
    /// This is the box the renderer claims, not the texels it draws. A sized label with hidden text
    /// still reports its box. It reads empty while attached to no entity, and empty is the default for a
    /// renderer with no rect to report. A renderer with empty bounds is never under the pointer and cannot
    /// be picked.
    /// </para>
    /// </summary>
    public virtual Rect Bounds => default;

    /// <summary>
    /// The transform this renderer draws by, which is its entity's <see cref="Entity.WorldTransform"/> in
    /// world units under a world root, or in canvas pixels with the <see cref="ScreenEntity.Anchor"/>
    /// resolved under a screen root. An intent takes its position from a point in the entity's own space
    /// placed through this transform, as <c>RenderTransform.TransformPoint(Offset)</c>, its rotation from the
    /// transform, and its extent from its texels or size times the transform's scale. A game's own renderer
    /// therefore places itself the same way on either layer. Reads
    /// <see cref="Transform2D.Identity"/> while attached to no entity.
    /// </summary>
    protected Transform2D RenderTransform => Entity is { } entity ? Placed(entity.World, entity) : Transform2D.Identity;

    /// <summary>
    /// <see cref="RenderTransform"/> as of the previous step, which an intent interpolates from. Reads
    /// <see cref="Transform2D.Identity"/> while attached to no entity.
    /// </summary>
    protected Transform2D PreviousRenderTransform => Entity is { } entity ? Placed(entity.PreviousWorld, entity) : Transform2D.Identity;

    private static Transform2D Placed(in Transform2D world, Entity entity) =>
        world.With(world.Position + entity.SpaceOrigin, world.Scale);

    /// <summary>
    /// Writes this renderer's intent onto the frame being built. The frame is already cleared and its camera
    /// is set. The engine calls this after scene startup for the first frame, then after each completed step.
    /// </summary>
    public abstract void Draw(FrameView view);

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Toggle("Visible", Visible, value => Visible = value);
        panel.Field("ZIndex", ZIndex);
    }
}
