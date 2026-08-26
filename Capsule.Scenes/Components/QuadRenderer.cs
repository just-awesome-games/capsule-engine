using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Scenes.Components;

/// <summary>
/// Draws its entity as one axis-aligned rectangle. The quad's top-left corner is the entity's
/// position plus <see cref="Offset"/>, so an entity whose position anchors something other than
/// a corner still places its body exactly. World units, Y-down.
/// </summary>
public sealed class QuadRenderer(Vector2 size, ColorRgba color) : Renderer
{
    /// <summary>The extent drawn from the corner.</summary>
    public Vector2 Size { get; set; } = size;

    public ColorRgba Color { get; set; } = color;

    /// <summary>Added to the entity's position to give the quad's corner; zero by default.</summary>
    public Vector2 Offset { get; set; }

    public override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        Entity entity = Entity!;
        view.AddQuad(new QuadIntent(entity.PreviousPosition + Offset, entity.Position + Offset, Size, Color));
    }
}
