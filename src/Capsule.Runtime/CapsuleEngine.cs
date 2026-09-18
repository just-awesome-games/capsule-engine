using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.Runtime;

/// <summary>The engine's entry point.</summary>
public static class CapsuleEngine
{
    /// <summary>
    /// Begins host configuration. Called by the <c>Capsule.Runtime.Generated.CapsuleBoot</c> the
    /// compiler generates into a game's shell, or by a project that is not that shell — a test
    /// project or a CI harness — passing the generated registry itself.
    /// </summary>
    /// <param name="gameName">
    /// The game's display name: the window's title, and the per-user local folder — the crash log and
    /// the saves — as a slug of it.
    /// </param>
    /// <param name="scenes">
    /// Every scene the game declares, plain and document-backed alike, so the host can resolve a
    /// class or document name without reflection.
    /// </param>
    /// <param name="drivers">
    /// Every input driver the game declares, which is what <c>--driver</c> resolves a name through;
    /// null registers none, which is the case for a caller handing <c>RunHeadless</c> a driver of
    /// its own.
    /// </param>
    /// <exception cref="ArgumentException">The name is blank, or slugs to no safe directory name.</exception>
    /// <exception cref="ArgumentNullException">The scene registry is null.</exception>
    public static EngineBuilder Configure(string gameName, SceneRegistry scenes, InputDriverRegistry? drivers = null) =>
        new(gameName, scenes, drivers ?? InputDriverRegistry.Empty);
}
