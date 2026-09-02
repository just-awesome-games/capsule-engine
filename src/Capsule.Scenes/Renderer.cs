using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// A component that draws. A scene walks its renderers in entity order and, within an entity, in
/// attachment order, so what draws later covers what drew earlier. Adding a kind of renderer is
/// a new subclass and nothing else.
/// </summary>
public abstract class Renderer : Component
{
    /// <summary>
    /// Writes this renderer's intent onto the frame under construction — already cleared, with
    /// the camera set. Called once per step, after the whole scene has stepped.
    /// </summary>
    public abstract void Draw(FrameView view);
}
