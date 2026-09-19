using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.Runtime;

/// <summary>The engine's entry point.</summary>
public static class CapsuleEngine
{
    /// <summary>
    /// Begins host configuration. Called by the <c>Capsule.Runtime.Generated.CapsuleBoot</c> the
    /// compiler generates into a game's shell, or by a test project or CI harness passing the
    /// generated registry itself.
    /// </summary>
    /// <param name="gameName">The game's display name. It titles the window, and slugs to the local folder name.</param>
    /// <param name="platform">Where content, saves and the crash log are, and the window policy.</param>
    /// <param name="scenes">Every scene the game declares. The host resolves a class or document name through it without reflection.</param>
    /// <param name="drivers">Every input driver the game declares. <c>--driver</c> resolves a name through it. Null registers none.</param>
    /// <exception cref="ArgumentException">The name is blank, or slugs to no safe directory name.</exception>
    public static EngineBuilder Configure(string gameName, HostPlatform platform, SceneRegistry scenes, InputDriverRegistry? drivers = null) =>
        new(gameName, platform, scenes, drivers ?? InputDriverRegistry.Empty);
}
