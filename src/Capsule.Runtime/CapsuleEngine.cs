using System.ComponentModel;
using Capsule.Scenes;

namespace Capsule.Runtime;

/// <summary>
/// The engine's entry point, reached from the <c>Capsule.Runtime.Generated.CapsuleBoot</c> the
/// compiler generates into a game's shell.
/// </summary>
public static class CapsuleEngine
{
    /// <param name="gameName">
    /// The game's display name: the window's title, and the crash log's folder as a slug of it.
    /// </param>
    /// <param name="scenes">
    /// Every scene the game declares, plain and document-backed alike. Each carries the residency
    /// groups the build derived for it, so the host has no separate texture list to be handed.
    /// </param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SceneEngineBuilder Configure(string gameName, SceneRegistry scenes) => new(gameName, scenes);
}
