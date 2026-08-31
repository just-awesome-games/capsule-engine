using Capsule.Scenes;

namespace Capsule.Runtime;

/// <summary>
/// The engine's entry point. A game's <c>Program</c> starts at the
/// <c>Capsule.Runtime.Generated.CapsuleBoot</c> generated into its shell, which starts here with the
/// scene registry the compiler built from the game's own classes.
/// </summary>
public static class CapsuleEngine
{
    /// <param name="gameName">
    /// The game's display name: the window's title, and the crash log's folder as a slug of it.
    /// </param>
    public static SimulationEngineBuilder Configure(string gameName) => new(gameName);

    /// <param name="gameName">
    /// The game's display name: the window's title, and the crash log's folder as a slug of it.
    /// </param>
    /// <param name="scenes">Every scene the game declares, plain and document-backed alike.</param>
    public static SceneEngineBuilder Configure(string gameName, SceneRegistry scenes) =>
        new(gameName, scenes);
}
