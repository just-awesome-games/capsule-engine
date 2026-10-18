using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>The options screen, a class-only scene showing the <see cref="OptionsMenu"/> and nothing else.</summary>
public sealed class Options : Scene
{
    public Options() => Add(new OptionsMenu());
}
