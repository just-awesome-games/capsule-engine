using Capsule.Rendering;

namespace Capsule.Scenes;

/// <summary>
/// What a scene opens at where it sets nothing itself: how it filters world-space textures. A
/// scene that sets its own keeps it. The default value is <see cref="TextureSampling.Linear"/>.
/// </summary>
public readonly record struct SceneDefaults(TextureSampling Sampling);
