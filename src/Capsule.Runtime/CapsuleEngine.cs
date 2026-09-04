using System.ComponentModel;
using Capsule.Assets;
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
    /// <param name="scenes">Every scene the game declares, plain and document-backed alike.</param>
    /// <param name="textures">Every texture the game ships, made resident before the first frame.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SceneEngineBuilder Configure(string gameName, SceneRegistry scenes, IReadOnlyList<TextureHandle> textures) =>
        new(gameName, scenes, textures);
}
