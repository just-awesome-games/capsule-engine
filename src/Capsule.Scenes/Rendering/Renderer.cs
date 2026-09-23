using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

/// <summary>
/// A component that draws. A scene walks its renderers by draw key, lowest first.
/// </summary>
/// <remarks>
/// A renderer's key is its entity's <see cref="Entity.ZIndex"/> summed up the ancestry plus this
/// renderer's <see cref="ZIndex"/>. Equal keys keep entity order, and attachment order within an
/// entity, which draws a later renderer over an earlier one. A renderer is placed by its entity's
/// <see cref="RenderTransform"/> and carries no turn or size of its own.
/// </remarks>
public abstract class Renderer : Component
{
    /// <summary>
    /// This renderer's offset from its entity's summed <see cref="Entity.ZIndex"/>, zero by default. A
    /// higher sum draws later.
    /// </summary>
    /// <remarks>
    /// The two values add as a <see cref="long"/> and neither is clamped. Equal sums draw in attachment
    /// order. A write inside <see cref="Draw"/> orders the next frame, not the frame being drawn.
    /// </remarks>
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

    /// <summary>Whether this renderer draws, true by default.</summary>
    /// <remarks>
    /// Hiding it changes nothing else. An entity or ancestor hidden through
    /// <see cref="Entity.Visible"/> also stops it drawing.
    /// </remarks>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// The rect this renderer covers, in the space it draws in: world units under a world root, or
    /// canvas pixels with the anchor resolved under a screen root. It reads the entity's current
    /// world transform.
    /// </summary>
    /// <remarks>
    /// A moving entity reports where the next frame places it, not where the last frame drew it.
    /// <para>
    /// This is the box the renderer claims, not the texels it draws. A sized label with hidden text
    /// still reports its box. It reads empty while attached to no entity, and empty is the default
    /// for a renderer with no rect to report.
    /// </para>
    /// </remarks>
    public virtual Rect Bounds => default;

    /// <summary>
    /// The transform this renderer draws by: its entity's <see cref="Entity.WorldTransform"/> in world
    /// units under a world root, or in canvas pixels with the <see cref="ScreenEntity.Anchor"/> resolved
    /// under a screen root. Reads <see cref="Transform2D.Identity"/> while attached to no entity.
    /// </summary>
    /// <remarks>
    /// An intent takes its position from <c>RenderTransform.TransformPoint(Offset)</c>, its rotation from
    /// the transform, and its extent from its texels or size times the transform's scale. A renderer
    /// built this way places itself correctly on either layer.
    /// </remarks>
    protected Transform2D RenderTransform => Entity is { } entity ? Placed(entity.World, entity) : Transform2D.Identity;

    /// <summary>
    /// <see cref="RenderTransform"/> as of the previous step, which an intent interpolates from. Reads
    /// <see cref="Transform2D.Identity"/> while attached to no entity.
    /// </summary>
    protected Transform2D PreviousRenderTransform => Entity is { } entity ? Placed(entity.PreviousWorld, entity) : Transform2D.Identity;

    private static Transform2D Placed(in Transform2D world, Entity entity) =>
        world.With(world.Position + entity.SpaceOrigin, world.Scale);

    /// <summary>
    /// Writes this renderer's intent onto the frame being built. The frame is already cleared and
    /// its camera is set.
    /// </summary>
    /// <remarks>
    /// The engine calls this after scene startup for the first frame, then after each completed
    /// step.
    /// </remarks>
    protected internal abstract void Draw(FrameView view);

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Toggle("Visible", Visible, value => Visible = value);
        panel.Field("ZIndex", ZIndex);
    }
}
