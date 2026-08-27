using System.Numerics;

namespace Capsule.Scenes;

/// <summary>
/// Where a scene is looked at from. <see cref="Center"/> is the world point at the centre of the
/// viewport and <see cref="ViewportSize"/> how many world units it spans; a viewport spanning
/// nothing draws nothing, which is what a scene opens with unless the game or the scene says
/// otherwise.
/// Moving it interpolates with the world it is looking at. Cutting it — a respawn, a warp, a
/// scene that opens somewhere else — goes through <see cref="Teleport"/>, or the cut renders as
/// a sweep across everything in between.
/// </summary>
public sealed class Camera
{
    private Vector2? _viewportSize;

    /// <summary>The world point the viewport is centred on.</summary>
    public Vector2 Center { get; set; }

    /// <summary>
    /// <see cref="Center"/> as of the previous step. Engine-managed exactly as
    /// <see cref="Entity.PreviousPosition"/> is: the scene retains it at the top of every step,
    /// and the renderer interpolates the pair by the frame alpha.
    /// </summary>
    public Vector2 PreviousCenter { get; internal set; }

    /// <summary>World units the viewport spans; zero until the game or the scene sets it.</summary>
    public Vector2 ViewportSize
    {
        get => _viewportSize.GetValueOrDefault();
        set => _viewportSize = value;
    }

    /// <summary>Cuts to <paramref name="center"/>, with no interpolation from the old centre.</summary>
    public void Teleport(Vector2 center)
    {
        Center = center;
        PreviousCenter = center;
    }

    // Null is "no scene has spoken", which is what separates a game default from a scene that
    // deliberately spans nothing.
    internal void OpenAt(Vector2 viewportSize) => _viewportSize ??= viewportSize;

    internal void Retain() => PreviousCenter = Center;
}
