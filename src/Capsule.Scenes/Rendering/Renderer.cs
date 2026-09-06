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
    /// it in attachment order among its entity's other unoffset renderers. Set from inside
    /// <see cref="Draw"/>, it orders the next step's frame rather than the one being drawn.
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
            Entity?.Scene?.InvalidateRendererOrder();
        }
    }

    // The draw pass this renderer last wrote into. A Draw may detach a renderer or remove an
    // entity, which rebuilds the list the traversal is walking, so each renderer is claimed by
    // pass rather than trusted to stay at the index the cursor left it on.
    internal long DrawPass { get; private set; }

    /// <summary>
    /// Writes this renderer's intent onto the frame under construction — already cleared, with
    /// the camera set. Called once per step, after the whole scene has stepped.
    /// </summary>
    public abstract void Draw(FrameView view);

    // False when this renderer has already drawn into the pass, which is what makes "once per
    // step" hold however the list moved underneath the traversal.
    internal bool TryClaimDraw(long pass)
    {
        if (DrawPass == pass)
        {
            return false;
        }

        DrawPass = pass;

        return true;
    }
}
