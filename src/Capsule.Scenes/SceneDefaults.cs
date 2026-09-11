using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// What a scene opens under: how it filters world-space textures, and the canvas its screen layer is
/// laid out in. The default value is <see cref="TextureSampling.Linear"/> over
/// <see cref="StandardCanvas"/>.
/// </summary>
/// <param name="Sampling">How world-space textures are filtered where the scene sets nothing.</param>
/// <param name="Canvas">
/// The screen layer's extent in canvas pixels (<see cref="Scene.Canvas"/>). A non-positive component
/// on either axis is <see cref="StandardCanvas"/>, so the default value carries the standard canvas.
/// </param>
public readonly record struct SceneDefaults(TextureSampling Sampling, Vector2 Canvas = default)
{
    /// <summary>The canvas a run that declares none is laid out in: 1280 by 720 pixels.</summary>
    public static Vector2 StandardCanvas => new(1280f, 720f);

    // The canvas a scene actually opens with.
    internal Vector2 ResolvedCanvas => Canvas.X > 0f && Canvas.Y > 0f ? Canvas : StandardCanvas;
}
