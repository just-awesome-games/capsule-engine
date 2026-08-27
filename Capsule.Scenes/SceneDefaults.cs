using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// What a scene opens at where it sets nothing itself: the world units its camera spans, and how
/// it filters world-space textures. A scene that sets either keeps its own value. The default
/// value is a camera spanning nothing and <see cref="TextureSampling.Linear"/>.
/// </summary>
public readonly record struct SceneDefaults(Vector2 CameraViewport, TextureSampling Sampling);
