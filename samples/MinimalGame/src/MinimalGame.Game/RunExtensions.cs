using Capsule;

namespace MinimalGame.Game;

/// <summary>The game's typed door onto the run object <see cref="GameBoot.Start"/> attached.</summary>
public static class RunExtensions
{
    extension(Run run)
    {
        /// <summary>The game's run object.</summary>
        public GameInstance Game => run.State<GameInstance>();
    }
}
