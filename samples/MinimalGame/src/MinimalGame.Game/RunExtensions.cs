namespace MinimalGame.Game;

/// <summary>The game's typed door onto the run's <see cref="MinimalGameInstance"/>.</summary>
public static class RunExtensions
{
    extension(Run run)
    {
        /// <summary>The game's run object.</summary>
        public MinimalGameInstance Game => run.State<MinimalGameInstance>();
    }
}
