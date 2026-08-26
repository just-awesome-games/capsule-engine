using System.Numerics;

namespace Capsule.Scenes;

/// <summary>
/// Where a scene is looked at from. <see cref="Center"/> is the world point at the centre of the
/// viewport and <see cref="ViewportSize"/> how many world units it spans; a viewport spanning
/// nothing draws nothing, which is what a scene starts with.
/// </summary>
public sealed class Camera
{
    public Vector2 Center { get; set; }

    public Vector2 ViewportSize { get; set; }
}
