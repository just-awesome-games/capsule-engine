using System.Numerics;

namespace Capsule.Scenes;

/// <summary>
/// A scene's world-space viewport. Movement interpolates; deliberate cuts use
/// <see cref="Teleport"/>. A non-positive <see cref="ViewportSize"/> draws nothing.
/// </summary>
public sealed class Camera
{
    private Vector2? _viewportSize;

    /// <summary>The world point the viewport is centred on.</summary>
    public Vector2 Center { get; set; }

    /// <summary><see cref="Center"/> at the previous fixed step, retained by the engine.</summary>
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
