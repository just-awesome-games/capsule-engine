using System.Numerics;
using System.Runtime.CompilerServices;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

/// <summary>
/// A component that draws. A scene walks its renderers by draw key, lowest first.
/// </summary>
/// <remarks>
/// A renderer's key is its entity's <see cref="Entity.ZIndex"/> summed up the ancestry, as far as an
/// entity whose <see cref="Entity.ZIndexIsAbsolute"/> is set, plus this renderer's <see cref="ZIndex"/>. Equal keys keep entity order, and attachment order within an
/// entity, which draws a later renderer over an earlier one. A renderer is placed by its entity's
/// <see cref="RenderTransform"/> and carries no turn or size of its own.
/// </remarks>
public abstract class Renderer : Component
{
    // The frame dropped all this renderer last offered, and the next frame sees whether it can rest.
    private const byte Dropped = 1;

    // The most submissions a renderer at rest can stand for.
    private const int MostRestOffers = byte.MaxValue - 2;

    // Zero while the scene asks this renderer to draw each frame, and Dropped. Above that it is at rest:
    // two more than the submissions its last draw offered, every one of which the frame dropped.
    private byte _rest;

    // While at rest: the world rect its offers reach, and which of its scene's scroll factors they were
    // culled under. A frame whose view misses the rect leaves the renderer out.
    private Rect _restReach;
    private byte _restLayer;

    // Whether what this renderer offers a frame follows from its entity's transform and from state whose
    // every write calls Wake. The scene then lets it rest while it stands still out of view.
    private readonly bool _restsWhenStill;

    /// <summary>
    /// This renderer's offset from its entity's summed <see cref="Entity.ZIndex"/>, zero by default. A
    /// higher sum draws later.
    /// </summary>
    /// <remarks>
    /// The two values add as a <see cref="long"/> and neither is clamped. Equal sums draw in attachment
    /// order.
    /// </remarks>
    /// <exception cref="InvalidOperationException">It is changed inside a <see cref="Draw"/>.</exception>
    public int ZIndex
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            Entity?.SceneOrNull?.Drawing.ThrowIfDrawing("set a renderer's ZIndex");
            Entity?.SceneOrNull?.Drawing.Changing(Entity, band: false);
            field = value;
        }
    }

    /// <summary>Whether this renderer draws, true by default.</summary>
    /// <remarks>
    /// Hiding it changes nothing else. An entity or ancestor hidden through
    /// <see cref="Entity.Visible"/> also stops it drawing.
    /// </remarks>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// The material this renderer draws with, and null for its entity's <see cref="Entity.Material"/>, the
    /// default, which is the engine's own sprite shader where the entity names none.
    /// </summary>
    /// <remarks>
    /// Neighbouring sprites draw in one batch when they share a texture and a material instance.
    /// A material held at construction is loaded with the scene, and one set later loads on its
    /// first draw. <see cref="Entity.Tint"/> and <see cref="Entity.Flash"/> apply whatever the
    /// material.
    /// </remarks>
    public Material? Material { get; set; }

    /// <summary>A renderer attached to no entity.</summary>
    protected Renderer()
    {
    }

    private protected Renderer(bool restsWhenStill)
    {
        _restsWhenStill = restsWhenStill;
    }

    /// <summary>
    /// The rect this renderer covers, in the space it draws in: world units under a world root, or
    /// canvas pixels with every anchor resolved under a screen root. It reads the entity's current
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
    /// units under a world root, or in canvas pixels with every <see cref="ScreenEntity.Anchor"/>
    /// resolved under a screen root. Reads <see cref="Transform2D.Identity"/> while attached to no entity.
    /// </summary>
    /// <remarks>
    /// An intent takes its position from <c>RenderTransform.TransformPoint(Offset)</c>, its rotation from
    /// the transform, and its extent from its texels or size times the transform's scale. A renderer
    /// built this way places itself correctly on either layer.
    /// </remarks>
    protected Transform2D RenderTransform
    {
        // Every Draw reads both transforms, and a call for each costs the frame build measurably once a
        // renderer's Draw outgrows the JIT's inlining budget.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Entity is { } entity ? Placed(entity.World, entity) : Transform2D.Identity;
    }

    /// <summary>
    /// <see cref="RenderTransform"/> as of the previous step, which an intent interpolates from. Reads
    /// <see cref="Transform2D.Identity"/> while attached to no entity.
    /// </summary>
    protected Transform2D PreviousRenderTransform
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Entity is { } entity ? PreviousPlaced(entity.PreviousWorld, entity) : Transform2D.Identity;
    }

    internal bool Resting => _rest > Dropped;

    // Whether the scene asks this renderer to draw each frame and has no rest in sight for it.
    private protected bool Awake => _rest == 0;

    // Whether the frame before dropped all this renderer offered, and it is one that can rest.
    internal bool MayRest => _rest == Dropped;

    internal int RestOffers => _rest - 2;

    // A frame kept nothing this renderer offered.
    internal void NoteDropped()
    {
        if (_rest == 0 && _restsWhenStill)
        {
            _rest = Dropped;
        }
    }

    internal ref readonly Rect RestReach => ref _restReach;

    internal int RestLayer => _restLayer;

    // Puts the renderer at rest. False where it offered more than a rest can count.
    internal bool Rest(in Rect reach, int layer, int offers)
    {
        if ((uint)offers > MostRestOffers)
        {
            return false;
        }

        _restReach = reach;
        _restLayer = (byte)layer;
        _rest = (byte)(offers + 2);

        return true;
    }

    // What this renderer offers may have changed, and the next frame asks it again.
    internal void Wake() => _rest = 0;

    private static Transform2D Placed(in Transform2D world, Entity entity) =>
        world.With(world.Position + entity.SpaceOrigin, world.Scale);

    private static Transform2D PreviousPlaced(in Transform2D world, Entity entity) =>
        world.With(world.Position + entity.PreviousSpaceOrigin, world.Scale);

    /// <summary>
    /// Writes this renderer's intent onto the frame being built. The frame is already cleared and
    /// its camera is set.
    /// </summary>
    /// <remarks>
    /// A frame is built on the first read of <see cref="SceneSimulation.View"/> after a step, and on
    /// the first read before any step. Draw runs at most once per step and never when nothing reads the
    /// view. It only writes <paramref name="view"/>. Game state changes in a step. A
    /// Draw that adds, removes or reparents an entity, attaches or detaches a component, or changes a
    /// ZIndex or <see cref="Scene.YSort"/> throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    protected internal abstract void Draw(FrameView view);

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        panel.Toggle("Visible", Visible, value => Visible = value);
        panel.Field("ZIndex", ZIndex);
        panel.Field("Material", (Material ?? Entity?.Material) is { } material ? material.Shader.Name : "default");
    }
}
