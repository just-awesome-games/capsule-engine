using Capsule.Scenes;
using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>The options screen, a class-only scene showing the <see cref="OptionsMenu"/> and nothing else.</summary>
public sealed class Options : Scene
{
    // Added in the constructor rather than in OnStart, which puts the menu's font in the scene's preload.
    public Options() => Add(new OptionsMenu());
}
