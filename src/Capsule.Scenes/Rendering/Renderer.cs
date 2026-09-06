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
    /// <see cref="Entity.ZIndex"/>: the two sum to the key the scene draws by. Higher draws later.
    /// Zero by default, which draws it in attachment order among its entity's other unoffset
    /// renderers.
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
    /// Writes this renderer's intent onto the frame under construction — already cleared, with
    /// the camera set. Called once per step, after the whole scene has stepped.
    /// </summary>
    public abstract void Draw(FrameView view);
}
