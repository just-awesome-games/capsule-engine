using Capsule.Scenes;
using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>The options screen, a class-only scene showing the <see cref="OptionsMenu"/> and nothing else.</summary>
public sealed class Options : Scene
{
    // Added in the constructor rather than in OnStart, which is what makes the menu's font part of the
    // scene's asset preload.
    public Options() => Add(new OptionsMenu());
}
